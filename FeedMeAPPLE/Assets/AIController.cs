using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json;
using System.Linq;

public class AIController : MonoBehaviour
{
    // ステージ参照
    private StageManager stageManager;
    private Rigidbody rb;

    // 移動制御
    private bool isMoving = false;
    private Vector3 startPos;

    // 学習モデル
    private Dictionary<string, Dictionary<string, int>> bcPolicy = null;  // BC Policy（確率分布）
    private Dictionary<string, RewardStats> rewardGradient = null;        // 報酬勾配テーブル
    private Dictionary<string, int> goalPositions = null;                 // ゴール座標（頻度付き）

    // AI設定
    [Tooltip("思考間隔（秒）")]
    public float thinkInterval = 0.3f;

    [Tooltip("BC Policy サンプリング温度（1.0=標準, 低いほど頻出行動を選びやすい）")]
    public float temperature = 1.0f;

    // 試行制御
    private int currentStep = 0;
    private int maxStepsPerEpisode = 150;
    private int attemptCount = 0;
    private int maxAttempts = 3;

    // 思考プロセス可視化用（GameControllerから設定）
    [HideInInspector]
    public Text thinkingText;
    private string currentThinkingInfo = "";

    // 報酬統計用の構造体
    [Serializable]
    public class RewardStats
    {
        public float avg;
        public float max;
        public float min;
        public int count;
    }

    public void Onstart()
    {
        stageManager = FindFirstObjectByType<StageManager>();
        if (stageManager == null)
        {
            Debug.LogError("StageManager が見つかりません");
            return;
        }

        rb = GetComponent<Rigidbody>();
        startPos = new Vector3(0, 2, 0);
        attemptCount = 0;

        // モデル読み込み
        LoadModels();

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
    /// 学習済みモデルの読み込み
    /// </summary>
    private void LoadModels()
    {
        string aiDir = Path.Combine(Application.dataPath, "DemoAIs");

        // BC Policy読み込み
        string bcPath = Path.Combine(aiDir, "bc_policy.json");
        if (File.Exists(bcPath))
        {
            try
            {
                string json = File.ReadAllText(bcPath);
                bcPolicy = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, int>>>(json);
                Debug.Log($"BC Policy読み込み成功: {bcPolicy.Count} 状態");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"BC Policy読み込みエラー: {e.Message}");
                bcPolicy = null;
            }
        }
        else
        {
            Debug.LogWarning($"BC Policyファイルが見つかりません: {bcPath}");
        }

        // 報酬勾配テーブル読み込み
        string rgPath = Path.Combine(aiDir, "reward_gradient.json");
        if (File.Exists(rgPath))
        {
            try
            {
                string json = File.ReadAllText(rgPath);
                rewardGradient = JsonConvert.DeserializeObject<Dictionary<string, RewardStats>>(json);
                Debug.Log($"報酬勾配テーブル読み込み成功: {rewardGradient.Count} 状態");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"報酬勾配テーブル読み込みエラー: {e.Message}");
                rewardGradient = null;
            }
        }
        else
        {
            Debug.LogWarning($"報酬勾配テーブルファイルが見つかりません: {rgPath}");
        }

        // ゴール座標読み込み
        string gpPath = Path.Combine(aiDir, "goal_positions.json");
        if (File.Exists(gpPath))
        {
            try
            {
                string json = File.ReadAllText(gpPath);
                goalPositions = JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
                Debug.Log($"ゴール座標読み込み成功: {goalPositions.Count} 箇所");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"ゴール座標読み込みエラー: {e.Message}");
                goalPositions = null;
            }
        }
        else
        {
            Debug.LogWarning($"ゴール座標ファイルが見つかりません: {gpPath}");
        }
    }

    /// <summary>
    /// スタート位置にリセット
    /// </summary>
    private void ResetToStart()
    {
        // ランダムなX位置でスタート（-4 ~ 4の範囲）
        float randomX = UnityEngine.Random.Range(-4f, 4f);
        transform.position = startPos + new Vector3(randomX, 0, 0);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;
        rb.isKinematic = true;
        isMoving = false;
        currentStep = 0;
        attemptCount++;

        UpdateThinkingUI($"=== 試行 {attemptCount}/{maxAttempts} 開始 ===\nスタート位置: ({transform.position.x:F1}, {transform.position.z:F1})");
    }

    /// <summary>
    /// 自律ループ
    /// </summary>
    private IEnumerator AutonomousLoop()
    {
        while (true)
        {
            Vector3 pos = new Vector3(Mathf.Round(transform.position.x), transform.position.y, Mathf.Round(transform.position.z));
            int env = GetEnvType((int)pos.x, (int)pos.z);

            // 最大ステップ数チェック
            if (currentStep > maxStepsPerEpisode)
            {
                Debug.LogWarning("AI: 最大行動回数を超えました");
                env = 0; // 強制終了扱い
            }

            // 終端チェック: ゴール到達
            if (env == 2)
            {
                Debug.Log("AI: ゴール到達！");
                UpdateThinkingUI("🎯 ゴール到達！");
                yield return new WaitForSeconds(1.5f);

                if (GameController.instance != null)
                {
                    GameController.instance.OnAIGoal();
                }
                yield break; // ループ終了
            }
            // 終端チェック: 落下
            else if (env == 0)
            {
                Debug.Log($"AI: 落下しました（試行{attemptCount}/{maxAttempts}）");
                UpdateThinkingUI($"💀 落下... 試行 {attemptCount}/{maxAttempts}");
                yield return new WaitForSeconds(1.5f);

                if (GameController.instance != null)
                {
                    GameController.instance.OnAIFall();
                }

                // リトライ可能かチェック
                if (attemptCount < maxAttempts)
                {
                    ResetToStart();
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }
                else
                {
                    Debug.Log("AI: 最大試行回数に達しました");
                    yield break; // ループ終了
                }
            }

            // 行動決定と移動
            if (!isMoving)
            {
                int chosenAction = DecideAction();
                Vector3 moveDir = ActionToVector(chosenAction);
                Vector3 targetPos = new Vector3(
                    Mathf.Round(transform.position.x) + moveDir.x,
                    transform.position.y,
                    Mathf.Round(transform.position.z) + moveDir.z
                );
                StartCoroutine(MoveToPos(targetPos, chosenAction));
                currentStep++;
            }

            yield return new WaitForSeconds(thinkInterval);
        }
    }

    /// <summary>
    /// 行動決定メイン処理（ハイブリッドアプローチ）
    /// 優先順位: 1. BC Policy → 2. 報酬勾配 → 3. ランダム
    /// </summary>
    private int DecideAction()
    {
        int px = (int)Mathf.Round(transform.position.x);
        int py = (int)Mathf.Round(transform.position.z);

        // 現在の状態キーを構築
        string stateKey = BuildStateKey(px, py);

        // 思考プロセス情報を構築
        string thinkInfo = BuildThinkingInfo(px, py, stateKey);

        // 優先順位1: BC Policy（確率分布サンプリング）
        if (bcPolicy != null && bcPolicy.ContainsKey(stateKey))
        {
            int action = SampleFromBCPolicy(stateKey, out string bcInfo);
            thinkInfo += bcInfo;
            thinkInfo += $"\n✅ 選択: BC Policy から action={action} ({GetActionName(action)})";

            UpdateThinkingUI(thinkInfo);
            Debug.Log($"AI思考: BC Policy選択 action={action}");
            return action;
        }

        // 優先順位2: 報酬勾配参照（未知状態の場合）
        if (rewardGradient != null && rewardGradient.Count > 0)
        {
            int action = SelectActionByRewardGradient(px, py, out string rgInfo);
            if (action > 0)
            {
                thinkInfo += rgInfo;
                thinkInfo += $"\n✅ 選択: 報酬勾配参照 action={action} ({GetActionName(action)})";

                UpdateThinkingUI(thinkInfo);
                Debug.Log($"AI思考: 報酬勾配選択 action={action}");
                return action;
            }
        }

        // 優先順位3: 完全未知状態 → ランダム行動
        int randomAction = UnityEngine.Random.Range(1, 5);
        thinkInfo += "\n⚠️ 完全未知状態（BC/報酬勾配なし）";
        thinkInfo += $"\n✅ 選択: ランダム action={randomAction} ({GetActionName(randomAction)})";

        UpdateThinkingUI(thinkInfo);
        Debug.Log($"AI思考: ランダム選択 action={randomAction}");
        return randomAction;
    }

    /// <summary>
    /// 思考プロセス情報を構築
    /// </summary>
    private string BuildThinkingInfo(int px, int py, string stateKey)
    {
        int env = GetEnvType(px, py);
        int envUp = GetEnvType(px, py + 1);
        int envDown = GetEnvType(px, py - 1);
        int envRight = GetEnvType(px + 1, py);
        int envLeft = GetEnvType(px - 1, py);

        string info = $"━━━ ステップ {currentStep} ━━━\n";
        info += $"📍 位置: ({px}, {py})\n";
        info += $"🌍 環境: 現在={GetEnvName(env)} 上={GetEnvName(envUp)} 下={GetEnvName(envDown)} 右={GetEnvName(envRight)} 左={GetEnvName(envLeft)}\n";
        info += $"🔑 状態キー: {stateKey}\n";

        // ゴール座標情報
        if (goalPositions != null && goalPositions.Count > 0)
        {
            var topGoal = goalPositions.OrderByDescending(kv => kv.Value).First();
            info += $"🎯 推定ゴール: {topGoal.Key} (出現{topGoal.Value}回)\n";
        }

        return info;
    }

    /// <summary>
    /// BC Policyから確率的にサンプリング（温度パラメータ付き）
    /// </summary>
    private int SampleFromBCPolicy(string stateKey, out string info)
    {
        Dictionary<string, int> actionCounts = bcPolicy[stateKey];

        // 温度パラメータ付きソフトマックスで確率を計算
        Dictionary<int, float> probs = new Dictionary<int, float>();

        info = "📊 BC Policy 確率分布:\n";

        float sumExp = 0f;
        for (int a = 1; a <= 4; a++)
        {
            int count = actionCounts.ContainsKey(a.ToString()) ? actionCounts[a.ToString()] : 0;
            float exp = Mathf.Exp(count / temperature);
            sumExp += exp;
            probs[a] = exp;
        }

        // 正規化
        for (int a = 1; a <= 4; a++)
        {
            probs[a] /= sumExp;
            int count = actionCounts.ContainsKey(a.ToString()) ? actionCounts[a.ToString()] : 0;
            info += $"  {GetActionName(a)}: 出現{count}回 → 確率{probs[a] * 100:F1}%\n";
        }

        // ルーレット選択
        float rand = UnityEngine.Random.Range(0f, 1f);
        float cumulative = 0f;

        for (int a = 1; a <= 4; a++)
        {
            cumulative += probs[a];
            if (rand <= cumulative)
            {
                return a;
            }
        }

        return 1; // フォールバック
    }

    /// <summary>
    /// 報酬勾配を参照して行動選択（各方向の次状態の報酬を比較）
    /// </summary>
    private int SelectActionByRewardGradient(int px, int py, out string info)
    {
        info = "📈 報酬勾配による評価:\n";

        Dictionary<int, float> actionRewards = new Dictionary<int, float>();

        for (int a = 1; a <= 4; a++)
        {
            Vector2Int nextPos = GetNextPosition(px, py, a);
            string nextStateKey = BuildStateKey(nextPos.x, nextPos.y);

            float reward = 0f;
            bool found = false;

            if (rewardGradient != null && rewardGradient.ContainsKey(nextStateKey))
            {
                reward = rewardGradient[nextStateKey].avg;
                found = true;
            }

            actionRewards[a] = reward;
            string status = found ? $"{reward:F2}" : "不明";
            info += $"  {GetActionName(a)}: 平均報酬={status}\n";
        }

        // 有効なデータがあるか確認
        bool hasValidData = actionRewards.Values.Any(r => r != 0f);
        if (!hasValidData)
        {
            return -1; // ランダムにフォールバック
        }

        // 最も報酬が高い方向を選択
        int bestAction = actionRewards.OrderByDescending(kv => kv.Value).First().Key;
        return bestAction;
    }

    /// <summary>
    /// 次の位置を取得
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
    /// 状態キー構築（環境パターンのみ、座標非依存）
    /// train.pyのencode_state_env_onlyと一致
    /// </summary>
    private string BuildStateKey(int x, int y)
    {
        int env = GetEnvType(x, y);
        int up = GetEnvType(x, y + 1);
        int down = GetEnvType(x, y - 1);
        int right = GetEnvType(x + 1, y);
        int left = GetEnvType(x - 1, y);

        return $"({env}, {up}, {down}, {right}, {left})";
    }

    /// <summary>
    /// 環境タイプを取得
    /// </summary>
    private int GetEnvType(int x, int z)
    {
        if (stageManager == null) return 0;
        return stageManager.GetTileState(x, z);
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
    /// 行動名を取得
    /// </summary>
    private string GetActionName(int action)
    {
        switch (action)
        {
            case 1: return "上↑";
            case 2: return "右→";
            case 3: return "下↓";
            case 4: return "左←";
            default: return "不明";
        }
    }

    /// <summary>
    /// 環境タイプ名を取得
    /// </summary>
    private string GetEnvName(int env)
    {
        switch (env)
        {
            case 0: return "穴";
            case 1: return "床";
            case 2: return "G";
            case 3: return "罠";
            default: return "?";
        }
    }

    /// <summary>
    /// 移動処理（movP.csのMoveToPos()と同じ実装）
    /// </summary>
    private IEnumerator MoveToPos(Vector3 targetPos, int action)
    {
        isMoving = true;

        // 回転処理（movP.csと同じ）
        int rotate = 0;
        switch (action)
        {
            case 1: rotate = 0; break;      // 上
            case 2: rotate = 90; break;     // 右
            case 3: rotate = 180; break;    // 下
            case 4: rotate = -90; break;    // 左
        }
        transform.rotation = Quaternion.Euler(0, rotate, 0);

        // 移動アニメーション（movP.csと同じ0.2秒）
        float elapsedTime = 0f;
        Vector3 startPosMove = transform.position;

        while (elapsedTime < 0.2f)
        {
            transform.position = Vector3.Lerp(startPosMove, targetPos, elapsedTime / 0.2f);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        transform.position = targetPos;

        // 環境チェック
        int envValue = GetEnvType((int)targetPos.x, (int)targetPos.z);

        // ステージマネージャーに移動を通知（色変更など）
        if (stageManager != null)
        {
            stageManager.SendMessage("MatsChange", targetPos, SendMessageOptions.DontRequireReceiver);
        }

        if (envValue == 2)
        {
            // ゴール到達
            Debug.Log($"AI: ゴール到達 pos=({targetPos.x},{targetPos.z})");
        }
        else if (envValue == 0)
        {
            // 落下
            Debug.Log($"AI: 落下 pos=({targetPos.x},{targetPos.z})");
            rb.isKinematic = false;
        }
        else if (envValue == 3)
        {
            // トラップ処理（movP.csのTrapped()と同じ）
            yield return StartCoroutine(HandleTrap(targetPos));
        }

        isMoving = false;
    }

    /// <summary>
    /// トラップ処理（movP.csのTrapped()と同じ実装）
    /// </summary>
    private IEnumerator HandleTrap(Vector3 trapPos)
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

        // 移動先のターゲット位置を計算
        Vector3 startPosT = transform.position;
        Vector3 targetPosT = startPosT + direction * moveDistance;

        // 放物線の高さを設定
        float arcHeight = 2.0f;
        float moveDuration = 1.0f;
        float elapsedTime = 0;

        // 放物線を描きながら移動
        while (elapsedTime < moveDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / moveDuration;

            // 線形補間で XZ 平面の位置を計算
            Vector3 flatPos = Vector3.Lerp(startPosT, targetPosT, t);

            // 放物線の高さを計算
            float height = Mathf.Sin(t * Mathf.PI) * arcHeight;

            // 新しい位置を設定
            transform.position = new Vector3(flatPos.x, startPosT.y + height, flatPos.z);

            yield return null;
        }

        // 最終的な位置をターゲット位置に設定
        transform.position = targetPosT;

        int envValue = GetEnvType((int)targetPosT.x, (int)targetPosT.z);

        // ステージマネージャーに移動を通知
        if (stageManager != null)
        {
            stageManager.SendMessage("MatsChange", targetPosT, SendMessageOptions.DontRequireReceiver);
        }

        Debug.Log($"AI: トラップ後着地 pos=({targetPosT.x},{targetPosT.z}) env={envValue}");

        if (envValue == 2)
        {
            // ゴール到達
            Debug.Log("AI: トラップからのゴール到達");
        }
        else if (envValue == 0)
        {
            // 落下
            Debug.Log("AI: トラップから落下");
            rb.isKinematic = false;
        }
        else if (envValue == 3)
        {
            // さらにトラップに踏み込んだ場合は再度実行
            yield return StartCoroutine(HandleTrap(targetPosT));
        }
    }

    /// <summary>
    /// 思考プロセスUIを更新
    /// </summary>
    private void UpdateThinkingUI(string info)
    {
        currentThinkingInfo = info;

        if (thinkingText != null)
        {
            thinkingText.text = info;
        }

        // デバッグログにも出力
        Debug.Log($"[AI思考]\n{info}");
    }

    /// <summary>
    /// 現在の思考情報を取得（外部参照用）
    /// </summary>
    public string GetCurrentThinkingInfo()
    {
        return currentThinkingInfo;
    }
}
