using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Newtonsoft.Json;
using System.Linq;

/// <summary>
/// 改良版AIコントローラー
/// - 学習済みモデルから2Dマップを構築
/// - マップ情報を活用した賢い推論
/// - Q-Table → IL-Policy の順で行動決定
/// </summary>
public class AIController : MonoBehaviour
{
    // ステージ参照
    private StageManager stageGenerator;
    private Rigidbody rb;

    // 移動制御
    private bool isMoving = false;
    private Vector3 startPos;

    // 学習モデルファイルパス
    private string qTablePath;
    private string ilPolicyPath;

    // 学習モデル
    private Dictionary<string, List<float>> qTable = null;
    private Dictionary<string, int> ilPolicy = null;

    // 2Dマップ（-1=不明, 0=穴, 1=ステージ, 2=ゴール, 3=トラップ）
    private Dictionary<Vector2Int, int> knownMap = new Dictionary<Vector2Int, int>();
    private Vector2Int? goalPosition = null;

    // 定数
    private const int ACTION_SPACE = 4; // actions 1..4

    // 公開設定
    [Tooltip("思考間隔（秒）")]
    public float thinkInterval = 0.25f;

    // 再試行制御
    private int maxStepsPerEpisode = 100;
    private int currentStep = 0;

    /// <summary>
    /// GameControllerから呼び出される開始メソッド
    /// </summary>
    public void Onstart()
    {
        stageGenerator = FindFirstObjectByType<StageManager>();
        if (stageGenerator == null) Debug.LogError("StageManager が見つかりません");

        rb = GetComponent<Rigidbody>();
        startPos = new Vector3(0, 2, 0);

        // JSONファイルのパス設定
        qTablePath = Path.Combine(Application.dataPath, "DemoAIs", "ai-model_q_table.json");
        ilPolicyPath = Path.Combine(Application.dataPath, "DemoAIs", "ai-model_il_policy.json");

        // モデル読み込み
        LoadModels();

        // 2Dマップ構築
        BuildKnownMapFromModels();

        // 自律プレイ開始
        StartCoroutine(StartEpisodeAfterDelay(0.5f));
    }

    private IEnumerator StartEpisodeAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        ResetToStart();
        StartCoroutine(AutonomousLoop());
    }

    /// <summary>
    /// モデルファイルの読み込み
    /// </summary>
    private void LoadModels()
    {
        // IL policy
        if (File.Exists(ilPolicyPath))
        {
            try
            {
                string ilJson = File.ReadAllText(ilPolicyPath);
                ilPolicy = JsonConvert.DeserializeObject<Dictionary<string, int>>(ilJson);
                Debug.Log($"ILポリシーを読み込みました（{ilPolicy.Count}件）: {ilPolicyPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning("ILポリシーの読み込みに失敗しました: " + e.Message);
                ilPolicy = null;
            }
        }
        else
        {
            Debug.LogWarning("ILポリシーファイルが見つかりません: " + ilPolicyPath);
            ilPolicy = null;
        }

        // Q-table
        if (File.Exists(qTablePath))
        {
            try
            {
                string qJson = File.ReadAllText(qTablePath);
                qTable = JsonConvert.DeserializeObject<Dictionary<string, List<float>>>(qJson);
                Debug.Log($"Qテーブルを読み込みました（状態数: {qTable.Count}）: {qTablePath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning("Qテーブルの読み込みに失敗しました: " + e.Message);
                qTable = null;
            }
        }
        else
        {
            Debug.LogWarning("Qテーブルファイルが見つかりません: " + qTablePath);
            qTable = null;
        }
    }

    /// <summary>
    /// Q-TableとIL-Policyから2Dマップを構築
    /// </summary>
    private void BuildKnownMapFromModels()
    {
        knownMap.Clear();
        goalPosition = null;

        Debug.Log("=== 2Dマップ構築開始 ===");

        // 1. IL-Policyから環境情報を抽出
        if (ilPolicy != null)
        {
            foreach (var kv in ilPolicy)
            {
                ParseStateKeyAndUpdateMap(kv.Key);
            }
        }

        // 2. Q-Tableから環境情報を抽出（追加情報として）
        if (qTable != null)
        {
            foreach (var kv in qTable)
            {
                ParseStateKeyAndUpdateMap(kv.Key);
            }
        }

        // 3. ゴール位置の特定（最も報酬が高い位置）
        FindGoalPosition();

        Debug.Log($"2Dマップ構築完了: {knownMap.Count}タイル判明");
        Debug.Log($"ゴール位置: {(goalPosition.HasValue ? goalPosition.Value.ToString() : "不明")}");
        Debug.Log($"穴の数: {knownMap.Count(kv => kv.Value == 0)}");
        Debug.Log($"ステージの数: {knownMap.Count(kv => kv.Value == 1)}");
        Debug.Log($"トラップの数: {knownMap.Count(kv => kv.Value == 3)}");
    }

    /// <summary>
    /// 状態キーをパースして2Dマップを更新
    /// 状態キー形式: "(x, y, env, up, down, right, left)"
    /// </summary>
    private void ParseStateKeyAndUpdateMap(string stateKey)
    {
        try
        {
            string inner = stateKey.Trim();
            if (inner.StartsWith("(")) inner = inner.Substring(1);
            if (inner.EndsWith(")")) inner = inner.Substring(0, inner.Length - 1);

            var parts = inner.Split(',');
            if (parts.Length < 7) return;

            int x = int.Parse(parts[0].Trim());
            int y = int.Parse(parts[1].Trim());
            int env = int.Parse(parts[2].Trim());
            int up = int.Parse(parts[3].Trim());
            int down = int.Parse(parts[4].Trim());
            int right = int.Parse(parts[5].Trim());
            int left = int.Parse(parts[6].Trim());

            // 現在位置の環境を記録
            Vector2Int currentPos = new Vector2Int(x, y);
            UpdateMapTile(currentPos, env);

            // 上下左右の環境情報も記録
            UpdateMapTile(new Vector2Int(x, y + 1), up);      // 上
            UpdateMapTile(new Vector2Int(x, y - 1), down);    // 下
            UpdateMapTile(new Vector2Int(x + 1, y), right);   // 右
            UpdateMapTile(new Vector2Int(x - 1, y), left);    // 左
        }
        catch (Exception e)
        {
            Debug.LogWarning($"状態キーのパースに失敗: '{stateKey}' -> {e.Message}");
        }
    }

    /// <summary>
    /// マップタイルの情報を更新（既存情報より優先度が高い場合のみ）
    /// </summary>
    private void UpdateMapTile(Vector2Int pos, int envValue)
    {
        // 優先度: ゴール(2) > トラップ(3) > ステージ(1) > 穴(0)
        if (!knownMap.ContainsKey(pos))
        {
            knownMap[pos] = envValue;
        }
        else
        {
            int current = knownMap[pos];
            // ゴールは最優先
            if (envValue == 2)
            {
                knownMap[pos] = 2;
            }
            // トラップは穴・ステージより優先
            else if (envValue == 3 && current != 2)
            {
                knownMap[pos] = 3;
            }
            // ステージは穴より優先（穴の情報は不確実な場合がある）
            else if (envValue == 1 && current == 0)
            {
                knownMap[pos] = 1;
            }
        }
    }

    /// <summary>
    /// Q-Tableからゴール位置を特定
    /// </summary>
    private void FindGoalPosition()
    {
        if (qTable == null) return;

        float bestGoalScore = float.NegativeInfinity;
        Vector2Int bestPos = Vector2Int.zero;
        bool foundGoal = false;

        foreach (var kv in qTable)
        {
            string stateKey = kv.Key;
            List<float> qrow = kv.Value;

            if (qrow == null || qrow.Count < ACTION_SPACE + 1) continue;

            // 最大Q値を取得
            float maxQ = qrow.Skip(1).Take(ACTION_SPACE).Max();

            // ゴール報酬の閾値（300以上）
            if (maxQ > 300f && maxQ > bestGoalScore)
            {
                try
                {
                    string inner = stateKey.Trim();
                    if (inner.StartsWith("(")) inner = inner.Substring(1);
                    if (inner.EndsWith(")")) inner = inner.Substring(0, inner.Length - 1);

                    var parts = inner.Split(',');
                    if (parts.Length >= 2)
                    {
                        int x = int.Parse(parts[0].Trim());
                        int y = int.Parse(parts[1].Trim());

                        bestGoalScore = maxQ;
                        bestPos = new Vector2Int(x, y);
                        foundGoal = true;
                    }
                }
                catch { }
            }
        }

        if (foundGoal)
        {
            goalPosition = bestPos;
            // ゴール位置を確実にマップに記録
            knownMap[bestPos] = 2;
        }
    }

    /// <summary>
    /// スタート位置にリセット
    /// </summary>
    private void ResetToStart()
    {
        transform.position = startPos + new Vector3(UnityEngine.Random.Range(-2, 4), 0, 0);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;
        rb.isKinematic = true;
        isMoving = false;
        currentStep = 0;
    }

    /// <summary>
    /// 自律ループ
    /// </summary>
    private IEnumerator AutonomousLoop()
    {
        while (true)
        {
            Vector3 pos = new Vector3(Mathf.Round(transform.position.x), transform.position.y, Mathf.Round(transform.position.z));
            int env = getEnvType((int)pos.x, (int)pos.z);

            // 最大ステップ数チェック
            if (currentStep > maxStepsPerEpisode)
            {
                Debug.LogWarning("AI: 最大行動回数を超えました");
                env = 0;
            }

            // サイズ縮小演出
            if (currentStep > 50 && currentStep <= 100)
            {
                float scaleFactor = Mathf.Lerp(1.0f, 0.0f, (currentStep - 50) / 50.0f);
                transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);
            }

            // 終端チェック
            if (env == 2)
            {
                Debug.Log("AI: GOAL に到達しました");
                yield return new WaitForSeconds(1.5f);
                GameController.instance.SendMessage("OnAIGoal");
                ResetToStart();
                yield return new WaitForSeconds(0.5f);
                continue;
            }
            else if (env == 0)
            {
                Debug.Log("AI: 落下しました");
                yield return new WaitForSeconds(1.5f);
                GameController.instance.SendMessage("OnAIFall");
                ResetToStart();
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            // 行動決定と移動
            if (!isMoving)
            {
                int chosenAction = DecideActionForCurrentState();
                Vector3 moveDir = ActionToVector(chosenAction);
                Vector3 targetPos = new Vector3(
                    Mathf.Round(transform.position.x) + moveDir.x,
                    transform.position.y,
                    Mathf.Round(transform.position.z) + moveDir.z
                );
                StartCoroutine(MoveToPosByAI(targetPos, chosenAction));
                currentStep++;
            }

            yield return new WaitForSeconds(thinkInterval);
        }
    }

    /// <summary>
    /// 現在の状態に基づいて行動を決定
    /// 優先順位: Q-Table → IL-Policy → マップベースのフォールバック
    /// </summary>
    private int DecideActionForCurrentState()
    {
        int px = (int)Mathf.Round(transform.position.x);
        int py = (int)Mathf.Round(transform.position.z);
        string stateKey = BuildStateKey(px, py);

        // === 1. Q-Tableによる推論 ===
        if (qTable != null && qTable.ContainsKey(stateKey))
        {
            List<float> qrow = qTable[stateKey];
            if (qrow != null && qrow.Count >= ACTION_SPACE + 1)
            {
                int bestAction = GetBestActionFromQTable(qrow, px, py);
                if (bestAction > 0)
                {
                    Debug.Log($"Q-Table選択: action={bestAction}");
                    return bestAction;
                }
            }
        }

        // === 2. IL-Policyによる推論 ===
        if (ilPolicy != null && ilPolicy.ContainsKey(stateKey))
        {
            int ilAction = ilPolicy[stateKey];
            // ILの行動が安全かチェック
            if (IsActionSafe(px, py, ilAction))
            {
                Debug.Log($"IL-Policy選択: action={ilAction}");
                return ilAction;
            }
        }

        // === 3. マップベースのフォールバック ===
        Debug.Log("フォールバック: マップベース推論");
        return GetMapBasedAction(px, py);
    }

    /// <summary>
    /// Q-Tableから最適な行動を選択（マップ情報を考慮）
    /// </summary>
    private int GetBestActionFromQTable(List<float> qrow, int x, int y)
    {
        // 各行動のQ値と安全性を評価
        List<(int action, float score)> candidates = new List<(int, float)>();

        for (int a = 1; a <= ACTION_SPACE; a++)
        {
            float qValue = qrow[a];
            
            // 次の位置を計算
            Vector2Int nextPos = GetNextPosition(x, y, a);
            
            // マップ情報による安全性チェック
            float safetyBonus = 0f;
            if (knownMap.ContainsKey(nextPos))
            {
                int tileType = knownMap[nextPos];
                if (tileType == 0)
                {
                    // 穴は大幅減点
                    safetyBonus = -5000f;
                }
                else if (tileType == 2 && goalPosition.HasValue && nextPos == goalPosition.Value)
                {
                    // ゴールは大幅加点
                    safetyBonus = 10000f;
                }
                else if (tileType == 1)
                {
                    // 安全なステージは少し加点
                    safetyBonus = 10f;
                }
                // トラップ(3)は特別な調整なし
            }

            // ゴールへの距離ボーナス
            float goalBonus = 0f;
            if (goalPosition.HasValue)
            {
                float distBefore = Vector2.Distance(new Vector2(x, y), goalPosition.Value);
                float distAfter = Vector2.Distance(nextPos, goalPosition.Value);
                if (distAfter < distBefore)
                {
                    goalBonus = 50f; // ゴールに近づく行動に加点
                }
            }

            float totalScore = qValue + safetyBonus + goalBonus;
            candidates.Add((a, totalScore));
        }

        // 最高スコアの行動を選択
        var best = candidates.OrderByDescending(c => c.score).First();
        
        // スコアが極端に低い場合は無効
        if (best.score < -9000f)
        {
            return -1; // フォールバックへ
        }
        
        return best.action;
    }

    /// <summary>
    /// 行動が安全かチェック
    /// </summary>
    private bool IsActionSafe(int x, int y, int action)
    {
        Vector2Int nextPos = GetNextPosition(x, y, action);
        
        if (knownMap.ContainsKey(nextPos))
        {
            int tileType = knownMap[nextPos];
            return tileType != 0; // 穴でなければ安全
        }

        return true; // 不明なタイルは一応安全とみなす
    }

    /// <summary>
    /// マップベースのフォールバック行動決定
    /// </summary>
    private int GetMapBasedAction(int x, int y)
    {
        Vector2Int now = new Vector2Int(x, y);
        List<(int action, float score)> candidates = new List<(int, float)>();

        for (int a = 1; a <= ACTION_SPACE; a++)
        {
            Vector2Int nextPos = GetNextPosition(x, y, a);
            float score = 0f;

            // マップ情報による評価
            if (knownMap.ContainsKey(nextPos))
            {
                int tileType = knownMap[nextPos];
                if (tileType == 0)
                {
                    score = -1000f; // 穴は避ける
                }
                else if (tileType == 2)
                {
                    score = 1000f; // ゴールは最優先
                }
                else if (tileType == 1)
                {
                    score = 100f; // ステージは安全
                }
                else if (tileType == 3)
                {
                    score = 50f; // トラップは低優先度だが避けない
                }
            }
            else
            {
                score = 10f; // 不明なタイルは探索価値あり
            }

            // ゴールへの距離
            if (goalPosition.HasValue)
            {
                float dist = Vector2.Distance(nextPos, goalPosition.Value);
                score += 10000f / (dist + 1f); // 近いほど高得点
            }
            Debug.Log($"フォールバック評価: action={a} score={score}");

            candidates.Add((a, score));
        }

        // 最高スコアの行動を選択
        var best = candidates.OrderByDescending(c => c.score).First();
        
        // 全て穴の場合はランダム
        if (best.score < -500f)
        {
            return UnityEngine.Random.Range(1, 5);
        }

        return best.action;
    }

    /// <summary>
    /// 次の位置を計算
    /// </summary>
    private Vector2Int GetNextPosition(int x, int y, int action)
    {
        switch (action)
        {
            case 1: return new Vector2Int(x, y + 1);      // 上
            case 2: return new Vector2Int(x + 1, y);      // 右
            case 3: return new Vector2Int(x, y - 1);      // 下
            case 4: return new Vector2Int(x - 1, y);      // 左
            default: return new Vector2Int(x, y);
        }
    }

    /// <summary>
    /// 状態キーを構築
    /// </summary>
    private string BuildStateKey(int x, int y)
    {
        int env = getEnvType(x, y);
        int up = getEnvType(x, y + 1);
        int right = getEnvType(x + 1, y);
        int down = getEnvType(x, y - 1);
        int left = getEnvType(x - 1, y);

        return $"({x}, {y}, {env}, {up}, {down}, {right}, {left})";
    }

    /// <summary>
    /// 行動番号をベクトルに変換
    /// </summary>
    private Vector3 ActionToVector(int action)
    {
        switch (action)
        {
            case 1: return new Vector3(0, 0, 1);   // 上
            case 2: return new Vector3(1, 0, 0);   // 右
            case 3: return new Vector3(0, 0, -1);  // 下
            case 4: return new Vector3(-1, 0, 0);  // 左
            default: return Vector3.zero;
        }
    }

    /// <summary>
    /// AI移動コルーチン
    /// </summary>
    private IEnumerator MoveToPosByAI(Vector3 targetPos, int action)
    {
        isMoving = true;
        float elapsedTime = 0;
        Vector3 from = transform.position;
        float duration = currentStep > 50 ? 0.2f : 0.25f;

        int rotate = 0;
        switch (action)
        {
            case 1: rotate = 0;     // 上
                break;
            case 2: rotate = 90;    // 右
                break;
            case 3: rotate = 180;   // 下
                break;
            case 4: rotate = -90;   // 左
                break;
        }
        transform.rotation = Quaternion.Euler(0, rotate, 0);

        while (elapsedTime < duration)
        {
            transform.position = Vector3.Lerp(from, targetPos, elapsedTime / duration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        transform.position = targetPos;
        int envValue = getEnvType((int)targetPos.x, (int)targetPos.z);

        Debug.Log($"AI Move: pos=({targetPos.x},{targetPos.z}) env={envValue} action={action}");

        if (envValue == 2)
        {
            Debug.Log("AI: ゴール到達！");
        }
        else if (envValue == 0)
        {
            rb.isKinematic = false;
        }
        else if (envValue == 3)
        {
            StartCoroutine(HandleTrapTile(targetPos));
        }

        isMoving = false;
    }

    private IEnumerator HandleTrapTile(Vector3 trapPos)
    {
        // 移動距離をランダムに決定 (2～4マス)
        int moveDistance = UnityEngine.Random.Range(2, 5);

        // 移動方向をランダムに決定 (上下左右)
        Vector3[] directions = {
            new Vector3(0, 0, 1),   // 上:1
            new Vector3(1, 0, 0),   // 右:2
            new Vector3(0, 0, -1),  // 下:3
            new Vector3(-1, 0, 0)   // 左:4
        };
        int dirIndex = UnityEngine.Random.Range(0, directions.Length);
        Vector3 direction = directions[dirIndex];
        int trapAction = dirIndex + 1; // log用に1増やす

        // 移動先のターゲット位置を計算
        Vector3 startPos = transform.position;
        Vector3 targetPos = startPos + direction * moveDistance;

        // 放物線の高さを設定
        float arcHeight = 2.0f;

        // 移動時間
        float moveDuration = 1.0f;
        float elapsedTime = 0;

        // 放物線を描きながら移動
        while (elapsedTime < moveDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / moveDuration;

            // 線形補間で XZ 平面の位置を計算
            Vector3 flatPos = Vector3.Lerp(startPos, targetPos, t);

            // 放物線の高さを計算
            float height = Mathf.Sin(t * Mathf.PI) * arcHeight;

            // 新しい位置を設定
            transform.position = new Vector3(flatPos.x, startPos.y + height, flatPos.z);

            yield return null;
        }

        // 最終的な位置をターゲット位置に設定
        transform.position = targetPos;

        int envValue = getEnvType((int)targetPos.x, (int)targetPos.z);

        Debug.Log($"AI Trap: pos=({targetPos.x},{targetPos.z}) env={envValue} action={trapAction}");

        if (envValue == 2)
        {
            Debug.Log("AI: ゴール到達！");
        }
        else if (envValue == 0)
        {
            Debug.Log("AI: 落下しました");
            rb.isKinematic = false;
        }
        else if (envValue == 3)
        {
            // さらに罠マスに踏み込んだ場合は再度実行
            yield return StartCoroutine(HandleTrapTile(targetPos));
        }
    }

    /// <summary>
    /// 環境タイプを取得
    /// </summary>
    private int getEnvType(int x, int z)
    {
        if (stageGenerator == null) return 0;
        return stageGenerator.GetTileState(x, z);
    }

    /// <summary>
    /// デバッグ用リセット
    /// </summary>
    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            ResetToStart();
            Debug.Log("AI: Rキーでリセット");
        }
    }
}