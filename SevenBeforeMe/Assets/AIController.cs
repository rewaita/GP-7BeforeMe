using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;

/// <summary>
/// AIController - ハイブリッド方式AI
/// 「行動クローニング(BC)」と「スコアベースの決定木(ランダムフォレスト)」を
/// 組み合わせて次の行動を決定します。
/// データは 'model_data.json' と 'parameters.json' から読み込みます。
/// </summary>
public class AIController : MonoBehaviour
{
    [Header("--- 参照設定 ---")]
    private StageManager stageManager;
    private Rigidbody rb;
    public Text thinkingText;
    private AIVisualizer visualizer;

    [Header("--- 移動・試行設定 ---")]
    [SerializeField] private float thinkInterval = 0.5f;
    [SerializeField] private int maxStepsPerEpisode = 150;
    [SerializeField] private int maxAttempts = 3;
    private Vector3 startBasePos = new Vector3(0, 2, 0);

    [Header("--- ハイブリッドAI設定 ---")]
    [SerializeField][Range(0f, 1f)] private float bcWeight = 0.6f;           // BC方式の重み（0=決定木のみ, 1=BCのみ）
    [SerializeField][Range(0f, 1f)] private float explorationRate = 0.1f;    // 探索率（ランダム行動）
    [SerializeField] private int numDecisionTrees = 3;                        // ランダムフォレストの木の数

    [Header("--- 内部状態 ---")]
    private bool isMoving = false;
    private int currentStep = 0;
    private int attemptCount = 0;
    private string currentThinkingInfo = "";
    private bool modelLoaded = false;

    // ========================================
    // AIモデルデータ（JSONから読み込み）
    // ========================================
    
    // 推定ゴール座標
    private Vector2Int estimatedGoal = new Vector2Int(-1, -1);
    private bool goalKnown = false;
    
    // 危険地帯リスト
    private HashSet<Vector2Int> dangerZones = new HashSet<Vector2Int>();
    
    // 行動クローニングテーブル（周囲状況 → 行動確率）
    private Dictionary<string, BCPolicy> bcPolicyTable = new Dictionary<string, BCPolicy>();
    
    // 決定木用スコア重み（parameters.jsonから読み込み）
    private float holeFearIndex = -2.5f;   // 穴回避指数（負の値ほど回避）
    private float trapInterest = 0.0f;     // 罠への積極性（-1.0=回避、1.0=積極）
    private float goalBias = 0.5f;         // ゴール優先度（0.0~1.0）
    private float goalApproachRate = 0.5f; // ゴール接近率
    
    // 方向別スコア（model_data.jsonから読み込み）
    private Dictionary<int, int> tileScores = new Dictionary<int, int>
    {
        { 0, -100 },  // 穴
        { 1, 0 },     // 平地
        { 2, 100 },   // ゴール
        { 3, -10 }    // 罠
    };
    private int goalDirectionBonus = 10;
    private int revisitPenalty = -5;
    private int unexploredBonus = 5;
    
    // 訪問済み座標追跡
    private Dictionary<Vector2Int, int> visitedTiles = new Dictionary<Vector2Int, int>();
    
    // ランダムフォレスト用：各決定木のノイズ係数
    private float[] treeNoiseFactors;

    // BCポリシー構造体
    [Serializable]
    private class BCPolicy
    {
        public float up;
        public float right;
        public float down;
        public float left;
        public int samples;
        
        public float GetProbability(int action)
        {
            return action switch
            {
                1 => up,
                2 => right,
                3 => down,
                4 => left,
                _ => 0.25f
            };
        }
    }
    
    // 統計情報（parameters.jsonから）
    private int bcStatesCount = 0;
    private int dangerZonesCount = 0;

    #region 1. 初期化・メインサイクル

    public void Onstart()
    {
        stageManager = FindFirstObjectByType<StageManager>();
        rb = GetComponent<Rigidbody>();
        attemptCount = 0;
        visitedTiles.Clear();

        // ビジュアライザーの初期化
        visualizer = GetComponent<AIVisualizer>();
        if (visualizer == null)
        {
            visualizer = gameObject.AddComponent<AIVisualizer>();
        }

        // ランダムフォレスト用のノイズ係数を初期化
        InitializeRandomForest();

        // モデルの読み込み（model_data.json + parameters.json）
        LoadAIModels();
        rb.isKinematic = false;
        // 実行開始
        StartCoroutine(MainLoop());
    }

    /// <summary>
    /// ランダムフォレスト用の各決定木にノイズ係数を設定
    /// </summary>
    private void InitializeRandomForest()
    {
        treeNoiseFactors = new float[numDecisionTrees];
        for (int i = 0; i < numDecisionTrees; i++)
        {
            // 各木に異なるバイアスを与える（-0.3 ~ +0.3）
            treeNoiseFactors[i] = UnityEngine.Random.Range(-0.3f, 0.3f);
        }
    }

    private IEnumerator MainLoop()
    {
        while (attemptCount < maxAttempts)
        {
            ResetToStart();
            yield return new WaitForSeconds(1.0f);
            rb.isKinematic = true;

            while (true)
            {
                // 1. 環境状態の確認
                Vector3 currentPos = GetRoundedPos();
                int env = GetEnvType((int)currentPos.x, (int)currentPos.z);

                // 2. 終了判定（落下・ゴール・歩数超過）
                if (IsEpisodeEnd(env, out string reason))
                {
                    yield return HandleEpisodeEnd(reason, env);
                    break; // 次の試行へ
                }

                // 3. 次の行動を決定（ハイブリッド方式）
                if (!isMoving)
                {
                    int action = DecideNextAction(currentPos);
                    StartCoroutine(MoveProcess(action));
                }

                yield return new WaitForSeconds(thinkInterval);
            }
        }
        UpdateThinkingUI("全ての試行が終了しました。");
    }

    #endregion

    #region 2. AI推論ロジック（ハイブリッド方式：決定木 + 行動クローニング）

    /// <summary>
    /// ハイブリッド方式で次の行動を決定
    /// 1. 探索率に基づきランダム行動の可能性をチェック
    /// 2. 各方向のスコアを計算（ランダムフォレスト：複数決定木の投票）
    /// 3. BCテーブルからの行動確率を加味
    /// 4. 重み付けで最終スコアを決定
    /// </summary>
    private int DecideNextAction(Vector3 currentPos)
    {
        // 探索率によるランダム行動
        if (UnityEngine.Random.value < explorationRate)
        {
            int randomAction = UnityEngine.Random.Range(1, 5);
            UpdateThinkingUI($"[探索モード] ランダム行動: {GetActionName(randomAction)}");
            return randomAction;
        }

        int px = (int)currentPos.x;
        int pz = (int)currentPos.z;

        // 周囲の環境取得
        int envUp = GetEnvType(px, pz + 1);
        int envDown = GetEnvType(px, pz - 1);
        int envRight = GetEnvType(px + 1, pz);
        int envLeft = GetEnvType(px - 1, pz);

        // === 決定木（ランダムフォレスト）スコア ===
        float[] treeScores = CalculateRandomForestScores(px, pz, envUp, envDown, envRight, envLeft);

        // === 行動クローニング（BC）スコア ===
        float[] bcScores = CalculateBCScores(envUp, envDown, envRight, envLeft);

        // === ハイブリッド統合 ===
        float[] finalScores = new float[5];
        for (int action = 1; action <= 4; action++)
        {
            // 決定木とBCの重み付け平均
            finalScores[action] = (1f - bcWeight) * treeScores[action] + bcWeight * bcScores[action];
        }

        // 最高スコアの方向を選択（同点時はランダム）
        int bestAction = SelectBestAction(finalScores);

        // 訪問記録を更新
        Vector2Int targetTile = GetTargetTile(px, pz, bestAction);
        if (visitedTiles.ContainsKey(targetTile))
            visitedTiles[targetTile]++;
        else
            visitedTiles[targetTile] = 1;

        // UI表示用のデバッグ情報
        string info = BuildThinkingInfo(px, pz, envUp, envDown, envRight, envLeft, 
                                         treeScores, bcScores, finalScores, bestAction);
        UpdateThinkingUI(info);

        visualizer.SendMessage("ShowScores");
        // ビジュアライザーの更新
        if (visualizer != null)
        {
            visualizer.UpdateScoreValues(finalScores);
            visualizer.UpdateGoalPosition(goalKnown ? estimatedGoal : new Vector2Int(-1, -1));
            visualizer.UpdateDangerZones(dangerZones);
        }

        return bestAction;
    }

    /// <summary>
    /// ランダムフォレスト：複数の決定木の平均スコアを計算
    /// </summary>
    private float[] CalculateRandomForestScores(int px, int pz, int envUp, int envDown, int envRight, int envLeft)
    {
        float[] avgScores = new float[5];

        for (int treeIdx = 0; treeIdx < numDecisionTrees; treeIdx++)
        {
            float noise = treeNoiseFactors[treeIdx];
            
            // 各方向のスコアを計算（ノイズ付き）
            avgScores[1] += CalculateDirectionScore(1, px, pz + 1, envUp, noise);
            avgScores[2] += CalculateDirectionScore(2, px + 1, pz, envRight, noise);
            avgScores[3] += CalculateDirectionScore(3, px, pz - 1, envDown, noise);
            avgScores[4] += CalculateDirectionScore(4, px - 1, pz, envLeft, noise);
        }

        // 平均化
        for (int action = 1; action <= 4; action++)
        {
            avgScores[action] /= numDecisionTrees;
        }

        return avgScores;
    }

    /// <summary>
    /// 行動クローニング（BC）からスコアを計算
    /// </summary>
    private float[] CalculateBCScores(int envUp, int envDown, int envRight, int envLeft)
    {
        float[] scores = new float[5];
        string bcKey = BuildBCKey(envUp, envDown, envRight, envLeft);

        if (bcPolicyTable.TryGetValue(bcKey, out BCPolicy policy))
        {
            // サンプル数による信頼度調整
            float confidence = Mathf.Clamp01(policy.samples / 50f);
            float baseScore = 50f; // BCスコアの基準値

            scores[1] = policy.up * baseScore * confidence;
            scores[2] = policy.right * baseScore * confidence;
            scores[3] = policy.down * baseScore * confidence;
            scores[4] = policy.left * baseScore * confidence;
        }
        else
        {
            // BCに該当エントリがない場合は均等
            for (int i = 1; i <= 4; i++) scores[i] = 12.5f;
        }

        return scores;
    }

    /// <summary>
    /// 各方向のスコアを計算（単一決定木）
    /// </summary>
    private float CalculateDirectionScore(int action, int targetX, int targetZ, int targetEnv, float noise = 0f)
    {
        float score = 0f;

        // 1. タイル種別によるベーススコア
        if (tileScores.TryGetValue(targetEnv, out int tileScore))
        {
            score += tileScore * (1f + noise * 0.2f); // ノイズ適用
        }

        // 2. 穴回避（holeFearIndex適用）
        if (targetEnv == 0)
        {
            score += holeFearIndex * 20f * (1f + noise);
        }

        // 3. 罠への積極性/回避性
        if (targetEnv == 3)
        {
            score += trapInterest * 10f * (1f + noise * 0.5f);
        }

        // 4. ゴール方向ボーナス（ゴール座標が既知の場合）
        if (goalKnown && estimatedGoal.x >= 0)
        {
            Vector2Int current = new Vector2Int((int)transform.position.x, (int)Mathf.Round(transform.position.z));
            int dx = estimatedGoal.x - current.x;
            int dy = estimatedGoal.y - current.y;

            // 行動がゴール方向かどうか
            bool isGoalDirection = false;
            if (action == 1 && dy > 0) isGoalDirection = true;      // 上
            if (action == 3 && dy < 0) isGoalDirection = true;      // 下
            if (action == 2 && dx > 0) isGoalDirection = true;      // 右
            if (action == 4 && dx < 0) isGoalDirection = true;      // 左

            if (isGoalDirection)
            {
                score += goalDirectionBonus * goalBias * goalApproachRate;
            }
        }

        // 5. 訪問済みペナルティ
        Vector2Int targetTile = new Vector2Int(targetX, targetZ);
        if (visitedTiles.TryGetValue(targetTile, out int visitCount))
        {
            score += revisitPenalty * visitCount * (1f + noise * 0.3f);
        }
        else
        {
            // 未訪問ボーナス
            score += unexploredBonus;
        }

        // 6. 危険地帯ペナルティ
        if (dangerZones.Contains(targetTile))
        {
            score -= 30f * (1f + noise * 0.5f);
        }

        return score;
    }

    /// <summary>
    /// 最高スコアの行動を選択（同点時はランダム）
    /// </summary>
    private int SelectBestAction(float[] scores)
    {
        float maxScore = float.MinValue;
        List<int> bestActions = new List<int>();

        for (int action = 1; action <= 4; action++)
        {
            if (scores[action] > maxScore)
            {
                maxScore = scores[action];
                bestActions.Clear();
                bestActions.Add(action);
            }
            else if (Mathf.Approximately(scores[action], maxScore))
            {
                bestActions.Add(action);
            }
        }

        // 同点の場合はランダム選択
        return bestActions[UnityEngine.Random.Range(0, bestActions.Count)];
    }

    /// <summary>
    /// BCテーブル用キーを生成（周囲4マスの状態）
    /// </summary>
    private string BuildBCKey(int up, int down, int right, int left)
    {
        return $"{up},{down},{right},{left}";
    }

    /// <summary>
    /// 行動から移動先座標を取得
    /// </summary>
    private Vector2Int GetTargetTile(int x, int z, int action)
    {
        return action switch
        {
            1 => new Vector2Int(x, z + 1),
            2 => new Vector2Int(x + 1, z),
            3 => new Vector2Int(x, z - 1),
            4 => new Vector2Int(x - 1, z),
            _ => new Vector2Int(x, z)
        };
    }

    /// <summary>
    /// 思考過程をUI表示用にフォーマット
    /// </summary>
    private string BuildThinkingInfo(int px, int pz, int up, int down, int right, int left, 
                                      float[] treeScores, float[] bcScores, float[] finalScores, int action)
    {
        string goalInfo = goalKnown ? $"({estimatedGoal.x},{estimatedGoal.y})" : "不明";
        string modelInfo = modelLoaded ? $"BC:{bcPolicyTable.Count}状態" : "モデル未読込";
        
        return $"[Step {currentStep}] 位置:({px},{pz}) {modelInfo}\n" +
               $"周囲: ↑{up} ↓{down} →{right} ←{left}\n" +
               $"決定木: ↑{treeScores[1]:F0} ↓{treeScores[3]:F0} →{treeScores[2]:F0} ←{treeScores[4]:F0}\n" +
               $"BC: ↑{bcScores[1]:F0} ↓{bcScores[3]:F0} →{bcScores[2]:F0} ←{bcScores[4]:F0}\n" +
               $"統合: ↑{finalScores[1]:F0} ↓{finalScores[3]:F0} →{finalScores[2]:F0} ←{finalScores[4]:F0}\n" +
               $"ゴール:{goalInfo} 重み:BC={bcWeight:F1} → {GetActionName(action)}";
    }

    /// <summary>
    /// 学習済みJSONファイルの読み込み（model_data.json + parameters.json）
    /// </summary>
    private void LoadAIModels()
    {
        string aiDir = Path.Combine(Application.dataPath, "DemoAIs");
        string modelPath = Path.Combine(aiDir, "model_data.json");
        string paramPath = Path.Combine(aiDir, "parameters.json");
        
        Debug.Log($"=== AIモデル読み込み開始 ===");
        Debug.Log($"  model_data.json: {modelPath}");
        Debug.Log($"  parameters.json: {paramPath}");

        // 1. model_data.json の読み込み
        if (File.Exists(modelPath))
        {
            try
            {
                string json = File.ReadAllText(modelPath);
                ParseModelData(json);
                modelLoaded = true;
                Debug.Log($"  model_data.json 読み込み完了");
            }
            catch (Exception e)
            {
                Debug.LogError($"  model_data.json 読み込みエラー: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning("  model_data.json が見つかりません。デフォルト設定で動作します。");
        }

        // 2. parameters.json の読み込み（人間可読パラメータ）
        if (File.Exists(paramPath))
        {
            try
            {
                string json = File.ReadAllText(paramPath);
                ParseParameters(json);
                Debug.Log($"  parameters.json 読み込み完了");
            }
            catch (Exception e)
            {
                Debug.LogError($"  parameters.json 読み込みエラー: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning("  parameters.json が見つかりません。");
        }

        // 3. 鏡ステージ用に座標を左右反転
        FlipCoordinatesForMirrorStage();

        // 読み込み結果のサマリー
        Debug.Log($"=== AIモデル読み込み完了 ===");
        Debug.Log($"  ゴール既知: {goalKnown} → ({estimatedGoal.x},{estimatedGoal.y})");
        Debug.Log($"  BC状態数: {bcPolicyTable.Count}");
        Debug.Log($"  危険地帯: {dangerZones.Count}箇所");
        Debug.Log($"  穴回避指数: {holeFearIndex:F2}");
        Debug.Log($"  罠への積極性: {trapInterest:F2}");
        Debug.Log($"  ゴール優先度: {goalBias:F2}");
    }

    /// <summary>
    /// parameters.json をパース
    /// </summary>
    private void ParseParameters(string json)
    {
        // estimated_goal（文字列形式 "x,y"）
        int goalIdx = json.IndexOf("\"estimated_goal\":");
        if (goalIdx >= 0)
        {
            int valueStart = json.IndexOf("\"", goalIdx + 17) + 1;
            int valueEnd = json.IndexOf("\"", valueStart);
            if (valueStart > 0 && valueEnd > valueStart)
            {
                string goalStr = json.Substring(valueStart, valueEnd - valueStart);
                if (goalStr != "unknown" && goalStr.Contains(","))
                {
                    string[] parts = goalStr.Split(',');
                    if (parts.Length == 2 && 
                        int.TryParse(parts[0], out int gx) && 
                        int.TryParse(parts[1], out int gy))
                    {
                        estimatedGoal = new Vector2Int(gx, gy);
                        goalKnown = true;
                    }
                }
            }
        }

        // hole_fear_index
        int holeIdx = json.IndexOf("\"hole_fear_index\":");
        if (holeIdx >= 0) holeFearIndex = ParseFloatValue(json, holeIdx + 18);

        // trap_interest
        int trapIdx = json.IndexOf("\"trap_interest\":");
        if (trapIdx >= 0) trapInterest = ParseFloatValue(json, trapIdx + 16);

        // goal_bias
        int biasIdx = json.IndexOf("\"goal_bias\":");
        if (biasIdx >= 0) goalBias = ParseFloatValue(json, biasIdx + 12);

        // statistics から追加情報を取得
        int statsIdx = json.IndexOf("\"statistics\"");
        if (statsIdx >= 0)
        {
            int bcStatesIdx = json.IndexOf("\"bc_states\":", statsIdx);
            int dangerIdx = json.IndexOf("\"danger_zones\":", statsIdx);
            
            if (bcStatesIdx >= 0) bcStatesCount = ParseIntValue(json, bcStatesIdx + 12);
            if (dangerIdx >= 0) dangerZonesCount = ParseIntValue(json, dangerIdx + 15);
        }
    }

    /// <summary>
    /// JSONを手動パース（Unity Sentis非使用）
    /// </summary>
    private void ParseModelData(string json)
    {
        // シンプルなJSON手動パース（Newtonsoft.Json非依存版）
        // 注: より堅牢なパースが必要な場合はJsonUtilityやカスタムパーサーを使用
        
        // estimated_goal のパース
        int goalStart = json.IndexOf("\"estimated_goal\"");
        if (goalStart >= 0)
        {
            int xStart = json.IndexOf("\"x\":", goalStart);
            int yStart = json.IndexOf("\"y\":", goalStart);
            int knownStart = json.IndexOf("\"known\":", goalStart);
            
            if (xStart >= 0 && yStart >= 0)
            {
                estimatedGoal.x = ParseIntValue(json, xStart + 4);
                estimatedGoal.y = ParseIntValue(json, yStart + 4);
            }
            if (knownStart >= 0)
            {
                goalKnown = json.Substring(knownStart + 8, 5).Contains("true");
            }
        }

        // decision_weights のパース
        int weightsStart = json.IndexOf("\"decision_weights\"");
        if (weightsStart >= 0)
        {
            int holeIdx = json.IndexOf("\"hole_fear_index\":", weightsStart);
            int trapIdx = json.IndexOf("\"trap_interest\":", weightsStart);
            int goalIdx = json.IndexOf("\"goal_bias\":", weightsStart);
            
            if (holeIdx >= 0) holeFearIndex = ParseFloatValue(json, holeIdx + 18);
            if (trapIdx >= 0) trapInterest = ParseFloatValue(json, trapIdx + 16);
            if (goalIdx >= 0) goalBias = ParseFloatValue(json, goalIdx + 12);
        }

        // bc_policy のパース
        int bcStart = json.IndexOf("\"bc_policy\"");
        if (bcStart >= 0)
        {
            int bcEnd = FindMatchingBrace(json, json.IndexOf("{", bcStart));
            string bcSection = json.Substring(bcStart, bcEnd - bcStart + 1);
            ParseBCPolicy(bcSection);
        }

        // danger_zones のパース
        int dangerStart = json.IndexOf("\"danger_zones\"");
        if (dangerStart >= 0)
        {
            int arrStart = json.IndexOf("[", dangerStart);
            int arrEnd = json.IndexOf("]", arrStart);
            if (arrStart >= 0 && arrEnd >= 0)
            {
                string dangerSection = json.Substring(arrStart, arrEnd - arrStart + 1);
                ParseDangerZones(dangerSection);
            }
        }

        // direction_scores のパース
        int scoresStart = json.IndexOf("\"direction_scores\"");
        if (scoresStart >= 0)
        {
            int bonusIdx = json.IndexOf("\"goal_direction_bonus\":", scoresStart);
            int revisitIdx = json.IndexOf("\"revisit_penalty\":", scoresStart);
            int unexploredIdx = json.IndexOf("\"unexplored_bonus\":", scoresStart);
            
            if (bonusIdx >= 0) goalDirectionBonus = ParseIntValue(json, bonusIdx + 23);
            if (revisitIdx >= 0) revisitPenalty = ParseIntValue(json, revisitIdx + 18);
            if (unexploredIdx >= 0) unexploredBonus = ParseIntValue(json, unexploredIdx + 19);
            
            // tile_scores のパース
            int tileScoresStart = json.IndexOf("\"tile_scores\":", scoresStart);
            if (tileScoresStart >= 0)
            {
                int holeScore = json.IndexOf("\"hole\":", tileScoresStart);
                int flatScore = json.IndexOf("\"flat\":", tileScoresStart);
                int goalScore = json.IndexOf("\"goal\":", tileScoresStart);
                int trapScore = json.IndexOf("\"trap\":", tileScoresStart);
                
                if (holeScore >= 0) tileScores[0] = ParseIntValue(json, holeScore + 7);
                if (flatScore >= 0) tileScores[1] = ParseIntValue(json, flatScore + 7);
                if (goalScore >= 0) tileScores[2] = ParseIntValue(json, goalScore + 7);
                if (trapScore >= 0) tileScores[3] = ParseIntValue(json, trapScore + 7);
            }
        }
    }

    private int ParseIntValue(string json, int startIdx)
    {
        int endIdx = startIdx;
        while (endIdx < json.Length && (char.IsDigit(json[endIdx]) || json[endIdx] == '-' || json[endIdx] == ' '))
            endIdx++;
        string numStr = json.Substring(startIdx, endIdx - startIdx).Trim();
        return int.TryParse(numStr, out int val) ? val : 0;
    }

    private float ParseFloatValue(string json, int startIdx)
    {
        int endIdx = startIdx;
        while (endIdx < json.Length && (char.IsDigit(json[endIdx]) || json[endIdx] == '-' || json[endIdx] == '.' || json[endIdx] == ' '))
            endIdx++;
        string numStr = json.Substring(startIdx, endIdx - startIdx).Trim();
        return float.TryParse(numStr, out float val) ? val : 0f;
    }

    private int FindMatchingBrace(string json, int openIdx)
    {
        int depth = 1;
        for (int i = openIdx + 1; i < json.Length; i++)
        {
            if (json[i] == '{') depth++;
            else if (json[i] == '}') depth--;
            if (depth == 0) return i;
        }
        return json.Length - 1;
    }

    private void ParseBCPolicy(string bcSection)
    {
        // "1,1,1,1": {"up": 0.25, "right": 0.25, ...} 形式をパース
        int idx = 0;
        while ((idx = bcSection.IndexOf("\":", idx)) >= 0)
        {
            // キーを見つける
            int keyEnd = idx;
            int keyStart = bcSection.LastIndexOf("\"", keyEnd - 1);
            if (keyStart < 0) { idx++; continue; }
            
            string key = bcSection.Substring(keyStart + 1, keyEnd - keyStart - 1);
            
            // 数字のカンマ区切りかチェック（例: "1,1,1,1"）
            if (!key.Contains(",")) { idx++; continue; }
            
            // 値オブジェクトを見つける
            int objStart = bcSection.IndexOf("{", idx);
            if (objStart < 0 || objStart > idx + 5) { idx++; continue; }
            
            int objEnd = FindMatchingBrace(bcSection, objStart);
            string objStr = bcSection.Substring(objStart, objEnd - objStart + 1);
            
            BCPolicy policy = new BCPolicy();
            int upIdx = objStr.IndexOf("\"up\":");
            int rightIdx = objStr.IndexOf("\"right\":");
            int downIdx = objStr.IndexOf("\"down\":");
            int leftIdx = objStr.IndexOf("\"left\":");
            int samplesIdx = objStr.IndexOf("\"samples\":");
            
            if (upIdx >= 0) policy.up = ParseFloatValue(objStr, upIdx + 5);
            if (rightIdx >= 0) policy.right = ParseFloatValue(objStr, rightIdx + 8);
            if (downIdx >= 0) policy.down = ParseFloatValue(objStr, downIdx + 7);
            if (leftIdx >= 0) policy.left = ParseFloatValue(objStr, leftIdx + 7);
            if (samplesIdx >= 0) policy.samples = ParseIntValue(objStr, samplesIdx + 10);
            
            bcPolicyTable[key] = policy;
            idx = objEnd;
        }
    }

    private void ParseDangerZones(string dangerSection)
    {
        // [{"x": 1, "y": 2}, ...] 形式をパース
        int idx = 0;
        while ((idx = dangerSection.IndexOf("{", idx)) >= 0)
        {
            int objEnd = dangerSection.IndexOf("}", idx);
            if (objEnd < 0) break;
            
            string objStr = dangerSection.Substring(idx, objEnd - idx + 1);
            int xIdx = objStr.IndexOf("\"x\":");
            int yIdx = objStr.IndexOf("\"y\":");
            
            if (xIdx >= 0 && yIdx >= 0)
            {
                int x = ParseIntValue(objStr, xIdx + 4);
                int y = ParseIntValue(objStr, yIdx + 4);
                dangerZones.Add(new Vector2Int(x, y));
            }
            idx = objEnd + 1;
        }
    }

    /// <summary>
    /// 鏡ステージ（mStage）用にゴール座標と危険地帯の座標を左右反転
    /// ステージ幅（xMax - xMin）に基づいて座標を反転
    /// </summary>
    private void FlipCoordinatesForMirrorStage()
    {
        // ゴール座標の左右反転
        if (goalKnown && estimatedGoal.x >= 0)
        {
            estimatedGoal.x = -estimatedGoal.x;
            Debug.Log($"  [鏡ステージ] ゴール座標を反転: ({estimatedGoal.x},{estimatedGoal.y})");
        }
        
        // 危険地帯の左右反転
        if (dangerZones.Count > 0)
        {
            HashSet<Vector2Int> flippedDangerZones = new HashSet<Vector2Int>();
            foreach (Vector2Int zone in dangerZones)
            {
                flippedDangerZones.Add(new Vector2Int(-zone.x, zone.y));
            }
            dangerZones = flippedDangerZones;
            Debug.Log($"  [鏡ステージ] 危険地帯の座標を反転: {dangerZones.Count}箇所");
        }
    }

    #endregion

    #region 3. 移動・物理挙動（基本変更不要）

    private IEnumerator MoveProcess(int action)
    {
        isMoving = true;
        currentStep++;

        Vector3 moveDir = ActionToVector(action);
        Vector3 targetPos = GetRoundedPos() + moveDir;

        // A. 回転
        float angle = (action == 1) ? 0 : (action == 2) ? 90 : (action == 3) ? 180 : -90;
        transform.rotation = Quaternion.Euler(0, angle, 0);

        yield return new WaitForSeconds(0.5f);

        visualizer.SendMessage("HideScores");
        // B. 移動アニメーション
        float duration = 0.25f;
        float elapsed = 0f;
        Vector3 startPos = transform.position;
        while (elapsed < duration)
        {
            transform.position = Vector3.Lerp(startPos, targetPos, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.position = targetPos;

        // C. 移動後のイベント処理
        int env = GetEnvType((int)targetPos.x, (int)targetPos.z);
        if (stageManager != null) stageManager.SendMessage("MatsChange", targetPos, SendMessageOptions.DontRequireReceiver);

        if (env == 3) // 罠
        {
            yield return StartCoroutine(HandleTrap());
        }
        isMoving = false;
    }

    private Coroutine trapCoroutine; // 現在実行中のトラップコルーチンを記録

    private IEnumerator HandleTrap()
    {
        UpdateThinkingUI("⚠️ 罠発動！ランダム移動中...");
        int dist = UnityEngine.Random.Range(2, 5);
        Vector3[] dirs = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
        Vector3 jumpDir = dirs[UnityEngine.Random.Range(0, 4)];
        
        Vector3 start = transform.position;
        Vector3 end = start + jumpDir * dist;

        float duration = 1.0f;
        float elapsed = 0f;

        yield return new WaitForSeconds(0.25f);
        visualizer.SendMessage("HideScores");
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            Vector3 flatPos = Vector3.Lerp(start, end, t);
            float height = Mathf.Sin(t * Mathf.PI) * 2.0f;
            transform.position = new Vector3(flatPos.x, start.y + height, flatPos.z);
            yield return null;
        }
        transform.position = end;

        int env = GetEnvType((int)end.x, (int)end.z);
        if (env == 3) 
        {
            trapCoroutine = StartCoroutine(HandleTrap()); // コルーチン参照を保存
        }
    }

    #endregion

    #region 4. システム・ユーティリティ（環境認識など）

    private void ResetToStart()
    {
        attemptCount++;
        currentStep = 0;
        isMoving = false;
        rb.linearVelocity = Vector3.zero;

        // ステージ移動後は単純な座標計算
        float randomX = Mathf.Round(UnityEngine.Random.Range(-2f, 2f));
        transform.position = startBasePos + new Vector3(randomX, 0, 0);
        transform.rotation = Quaternion.identity;

        // ビジュアライザーの初期化
        if (visualizer != null)
        {
            visualizer.UpdateGoalPosition(goalKnown ? estimatedGoal : new Vector2Int(-1, -1));
            visualizer.UpdateDangerZones(dangerZones);
        }

        UpdateThinkingUI($"=== 試行 {attemptCount}/{maxAttempts} 開始 ===");
    }

    private bool IsEpisodeEnd(int env, out string reason)
    {
        reason = "";
        if (env == 2) { reason = "Goal"; return true; }
        if (env == 0) { reason = "Fall"; return true; }
        if (currentStep > maxStepsPerEpisode) { reason = "Timeout"; return true; }
        return false;
    }

    private IEnumerator HandleEpisodeEnd(string reason, int env)
    {
        if (reason == "Goal")
        {
            UpdateThinkingUI("🎯 ゴール到達！");
            StartCoroutine(GOAL());
            yield return new WaitForSeconds(2.0f);
            GameController.instance.OnAIGoal1();
            yield return new WaitForSeconds(3.0f);
            GameController.instance.OnAIGoal2();
        }
        else
        {
            UpdateThinkingUI($"💀 終了: {reason}");
            StartCoroutine(FALL());
            GameController.instance.KuroOn();
            yield return new WaitForSeconds(2.5f);
        }
    }

    IEnumerator GOAL()
    {
        float duration = 5.0f;
        float elapsed = 0f;
        Vector3 start = transform.position;
        Vector3 end = start + Vector3.forward * 5f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(start, end, elapsed / duration);
            yield return null;
        }
        transform.position = end;
    }

    /// <summary>
    /// 鏡との衝突検出 - トラップ飛行中に鏡に接触した場合、落下処理に遷移
    /// </summary>
    private void OnTriggerEnter(Collider col)
    {
        // 鏡との衝突を検出
        if (col.CompareTag("Mirror"))
        {
            Debug.Log("[AI] 鏡に衝突！トラップをキャンセルして落下させます");
            
            // トラップコルーチンのみを停止
            if (trapCoroutine != null)
            {
                StopCoroutine(trapCoroutine);
                trapCoroutine = null;
            }
            
            // 物理演算を有効化して落下させる
            rb.isKinematic = false;
            
            // 落下コルーチンを開始
            StartCoroutine(FALL());
        }
    }

    IEnumerator FALL()
    {
        rb.isKinematic = false;
        yield return new WaitForSeconds(2.0f);
        rb.isKinematic = true;
        yield return new WaitForSeconds(0.5f);
        if (GameController.instance != null) GameController.instance.OnAIFall();
    }
    private int GetEnvType(int x, int z) => stageManager ? stageManager.GetMTileState(x, z) : 0;

    private Vector3 GetRoundedPos() => new Vector3(Mathf.Round(transform.position.x), transform.position.y, Mathf.Round(transform.position.z));

    private Vector3 ActionToVector(int action) => action == 1 ? Vector3.forward : action == 2 ? Vector3.right : action == 3 ? Vector3.back : action == 4 ? Vector3.left : Vector3.zero;

    private string GetActionName(int action) => action == 1 ? "上" : action == 2 ? "右" : action == 3 ? "下" : action == 4 ? "左" : "不明";

    private void UpdateThinkingUI(string info)
    {
        currentThinkingInfo = info;
        if (thinkingText != null) thinkingText.text = info;
    }

    #endregion
}