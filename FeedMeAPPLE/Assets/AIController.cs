using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Newtonsoft.Json;

/// <summary>
/// 学習済みモデル（Qテーブル / 模倣学習ポリシー）を読み込み、
/// それに従って自動で行動してゴールを目指す自動プレイヤー。
/// - 優先順位: ILポリシーがあればそれを使う。なければQテーブルの argmax を使う。
/// - Qテーブルが全て0（未学習）ならランダム or 簡単なフォールバック選択を行う。
/// </summary>
public class AIController : MonoBehaviour
{
    // ステージ参照
    private StageManager stageGenerator;
    private Rigidbody rb;

    // movement control
    private bool isMoving = false;
    private Vector3 startPos;

    // 学習モデルファイルパス（Python側で出力したものと合わせる）
    private string qTablePath;
    private string ilPolicyPath;

    // 学習モデル格納
    // Qテーブル: List of float[] (stateIndex -> array of action-values; action 0 is unused)
    //private List<List<float>> qTable = null;
    private Dictionary<string, List<float>> qTable = null;
    // ILポリシー: stateIndex (string) -> action (int)
    private Dictionary<string, int> ilPolicy = null;

    // 同期する定数（Python側と一致させること）
    private const int MAX_X = 20;
    private const int MAX_Y = 20;
    private const int ENV_TYPES = 3; // 0,1,2
    private const int ACTION_SPACE = 4; // actions 1..4
    private const int STATE_SPACE_SIZE = MAX_X * MAX_Y * ENV_TYPES;

    // 公開設定（Inspectorで変更可）
    [Tooltip("1秒ごとに次の行動を決めたい場合は大きくしてください")]
    public float thinkInterval = 0.25f;

    // 再試行制御（同じステップで止まらないように）
    private int maxStepsPerEpisode = 100;
    private int currentStep = 0;

    //AIマッピング
    // 推定マップ：穴とゴール
    private HashSet<Vector2Int> estimatedHoles = new HashSet<Vector2Int>();
    private Vector2Int? estimatedGoal = null;


    private void Onstart()
    {
        stageGenerator = FindFirstObjectByType<StageManager>();
        if (stageGenerator == null) Debug.LogError("StageManager が見つかりません");

        rb = GetComponent<Rigidbody>();
        startPos = new Vector3(0, 2, 0);
        // JSONファイルのパス
        qTablePath = Path.Combine(Application.dataPath, "DemoAIs", "ai-model_q_table.json");
        ilPolicyPath = Path.Combine(Application.dataPath, "DemoAIs", "ai-model_il_policy.json");

        // モデル読み込み（成功しなくても動作するフェールセーフ実装）
        LoadModels();

        // 少し待ってから自律開始（物理挙動の安定のため）
        StartCoroutine(StartEpisodeAfterDelay(0.5f));
    }

    private IEnumerator StartEpisodeAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        ResetToStart();
        StartCoroutine(AutonomousLoop());
    }

    private void LoadModels()
    {
        // IL policy
        if (File.Exists(ilPolicyPath))
        {
            try
            {
                string ilJson = File.ReadAllText(ilPolicyPath);
                // IL JSON is a dict with string keys: {"0": 1, "1": 2, ...}
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
                // qJson is an array of arrays. Deserialize into List<List<float>>
                //qTable = JsonConvert.DeserializeObject<List<List<float>>>(qJson);
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
        // ILポリシーを頻度テーブルに変換
        ConvertILToFrequency();
        
        // ★ q-table から地図推定
        InferMapFromQTable();
    }

    private void InferMapFromQTable()
    {
        estimatedHoles.Clear();
        estimatedGoal = null;

        foreach (var kv in qTable)
        {
            int state = kv.Key;
            float[] qs = kv.Value;

            // Q値の最大・最小を取得
            float maxQ = qs.Max();
            float minQ = qs.Min();

            // 報酬推定
            // 大きくマイナス → 穴に落ちた状態
            // 大きくプラス → ゴール状態
            int x = state / 1000;
            int y = state % 1000;
            Vector2Int pos = new Vector2Int(x, y);

            if (minQ < -300f)      // 閾値は適宜調整
            {
                estimatedHoles.Add(pos);
            }
            else if (maxQ > 300f)
            {
                estimatedGoal = pos;
            }
        }

        Debug.Log($"推定穴={estimatedHoles.Count}, 推定ゴール={estimatedGoal}");
    }


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

    // 自律ループ: thinkIntervalごとに次の行動を決定して移動
    private IEnumerator AutonomousLoop()
    {
        while (true)
        {
            // 終端チェック（ゴール or 落下）: 現在のタイルをチェック
            Vector3 pos = new Vector3(Mathf.Round(transform.position.x), transform.position.y, Mathf.Round(transform.position.z));
            int env = getEnvType((int)pos.x, (int)pos.z);

            // 最大行動回数を超えた場合、穴に落ちたとみなす
            if (currentStep > maxStepsPerEpisode)
            {
                Debug.LogWarning("AI: 最大行動回数を超えました。穴に落ちたとみなします。");
                env = 0; // 穴に落ちたとみなす
            }
            // 行動回数が100を超えたら、150に向けて大きさを小さくしていく
            if (currentStep > 50 && currentStep <= 100)
            {
                float scaleFactor = Mathf.Lerp(1.0f, 0.0f, (currentStep - 50) / 50.0f);
                transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);
            }

            if (env == 2)
            {
                Debug.Log("AI: GOAL に到達しました");
                // エピソード終了処理: リセットして再開
                yield return new WaitForSeconds(1.5f);
                //シミュ完了の合図をGameControllerに送る
                GameController.instance.SendMessage("OnAIGoal");
                ResetToStart();
                yield return new WaitForSeconds(0.5f);
                continue;
            }
            else if (env == 0)
            {
                Debug.Log("AI: 落下しました");
                yield return new WaitForSeconds(1.5f);
                //シミュ完了の合図をGameControllerに送る
                GameController.instance.SendMessage("OnAIFall");
                ResetToStart();
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            if (!isMoving)
            {
                // 現在の状態を作り、行動を決定して移動を開始
                int chosenAction = DecideActionForCurrentState();
                Vector3 moveDir = ActionToVector(chosenAction);
                Vector3 targetPos = new Vector3(Mathf.Round(transform.position.x) + moveDir.x,
                                                transform.position.y,
                                                Mathf.Round(transform.position.z) + moveDir.z);
                StartCoroutine(MoveToPosByAI(targetPos, chosenAction));
                currentStep++;
            }

            yield return new WaitForSeconds(thinkInterval);
        }
    }


    // IL 70% / Q 30% の重み
    private const float IL_WEIGHT = 0.3f;

    // IL 集計版（ロード時に変換）
    private Dictionary<string, Dictionary<int, int>> ilPolicyFreq = null;


    // LoadModels() の最後に追加する初期化処理
    private void ConvertILToFrequency()
    {
        if (ilPolicy == null)
        {
            ilPolicyFreq = null;
            return;
        }

        ilPolicyFreq = new Dictionary<string, Dictionary<int, int>>();

        foreach (var kv in ilPolicy)
        {
            string key = kv.Key;
            int act = kv.Value;

            if (!ilPolicyFreq.ContainsKey(key))
                ilPolicyFreq[key] = new Dictionary<int, int>();

            if (!ilPolicyFreq[key].ContainsKey(act))
                ilPolicyFreq[key][act] = 0;

            ilPolicyFreq[key][act] += 1;
        }

        Debug.Log($"ILポリシーを頻度テーブルに変換しました: {ilPolicyFreq.Count} 状態");
    }



    private int DecideActionForCurrentState()
    {
        int px = (int)Mathf.Round(transform.position.x);
        int py = (int)Mathf.Round(transform.position.z);
        string stateKey = BuildStateKey(px, py);

        bool hasIL = ilPolicyFreq != null && ilPolicyFreq.ContainsKey(stateKey);
        // Qテーブルのエントリがあるか確認
        bool hasQ = qTable != null && qTable.ContainsKey(stateKey);

        // ★修正: ILとQの併用ロジックの整理
        // ランダム値でILを使うか決定
        bool useIL = UnityEngine.Random.value < IL_WEIGHT;

        // 1. ILを使うモードで、かつILデータがある場合
        if (useIL && hasIL)
        {
            var table = ilPolicyFreq[stateKey];
            int bestAction = 1;
            int bestCount = -1;
            foreach (var kv in table)
            {
                if (kv.Value > bestCount)
                {
                    bestCount = kv.Value;
                    bestAction = kv.Key;
                }
            }
            return bestAction;
        }

        // 2. Qテーブルを使って最善の手を選ぶ (Greedy)
        if (hasQ)
        {
            List<float> qrow = qTable[stateKey];
            
            // アクション 1~4 の中で最大値を持つインデックスを探す
            // (qrow[0]は未使用なので無視)
            if (qrow != null && qrow.Count >= ACTION_SPACE + 1)
            {
                float maxQ = -999999f;
                int bestAction = UnityEngine.Random.Range(1, ACTION_SPACE + 1);
                
                // 単純に現在のQ値が最も高い行動を選ぶ
                for (int a = 1; a <= ACTION_SPACE; a++)
                {
                    if (qrow[a] > maxQ)
                    {
                        maxQ = qrow[a];
                        bestAction = a;
                    }
                }
                
                // すべて0（未学習）でないか確認。すべて0ならランダムにするか、そのまま返すか
                // ここではmaxQが初期値より更新されていれば採用
                if (maxQ > -99999f)
                {
                    return bestAction;
                }
            }
        }

        // ★ 穴回避とゴール誘導 ------------

        int ppx = (int)Mathf.Round(transform.position.x);
        int ppy = (int)Mathf.Round(transform.position.z);
            Vector2Int now = new Vector2Int(ppx, ppy);

            // 次の行動で向かう座標
            Dictionary<int, Vector2Int> nextPos = new Dictionary<int, Vector2Int>() {
                {1, now + new Vector2Int(0, 1)},   // 上
                {2, now + new Vector2Int(1, 0)},   // 右
                {3, now + new Vector2Int(0, -1)},  // 下
                {4, now + new Vector2Int(-1, 0)},  // 左
            };

        // 1. 穴方向は候補から外す
        List<int> safeActions = new List<int>();
        foreach (var kv in nextPos)
        {
            if (!estimatedHoles.Contains(kv.Value))safeActions.Add(kv.Key);
        }

        // すべて穴方向 → ランダム
        if (safeActions.Count == 0)
            return UnityEngine.Random.Range(1, 5);
        // 2. ゴールが推定できている → もっとも距離が近づく行動を採用
        if (estimatedGoal.HasValue)
        {
            int best = safeActions[0];
            float bestDist = float.MaxValue;

            foreach (int a in safeActions)
            {
                float d = Vector2.Distance(nextPos[a], estimatedGoal.Value);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = a;
                }
            }

            Debug.Log($"fallback → goal direction: {best}");
            return best;
        }

        // 3. ゴール不明でも穴回避した上でランダム
        return safeActions[UnityEngine.Random.Range(0, safeActions.Count)];

        
    }

    // ------------------------------------------
    // BuildStateKey の確認（スペースに注意）
    // ------------------------------------------
    private string BuildStateKey(int x, int y)
    {
        int env = getEnvType(x, y);
        int up    = getEnvType(x, y + 1);
        int right = getEnvType(x + 1, y);
        int down  = getEnvType(x, y - 1);
        int left  = getEnvType(x - 1, y);

        // Python側: f"({x}, {y}, {env}, {up}, {down}, {right}, {left})"
        // カンマの後に半角スペースを入れるフォーマットで統一します。
        return $"({x}, {y}, {env}, {up}, {down}, {right}, {left})";
    }


    // 行動番号 -> ベクトル
    // 1=up(z+1), 2=right(x+1), 3=down(z-1), 4=left(x-1)
    private Vector3 ActionToVector(int action)
    {
        switch (action)
        {
            case 1: return new Vector3(0, 0, 1);
            case 2: return new Vector3(1, 0, 0);
            case 3: return new Vector3(0, 0, -1);
            case 4: return new Vector3(-1, 0, 0);
            default: return Vector3.zero;
        }
    }

    // AI用の移動コルーチン（移動後に環境チェック・ログなどを行う）
    private IEnumerator MoveToPosByAI(Vector3 targetPos, int action)
    {
        isMoving = true;
        float elapsedTime = 0;
        Vector3 from = transform.position;
        float duration = 0.25f;
        if(currentStep > 50)
        {
            duration = 0.2f;
        }

        while (elapsedTime < duration)
        {
            transform.position = Vector3.Lerp(from, targetPos, elapsedTime / duration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // 到達
        transform.position = targetPos;
        int envValue = getEnvType((int)targetPos.x, (int)targetPos.z);
        
        Debug.Log($"AI Move: pos=({targetPos.x},{targetPos.z}) env={envValue} action={action}");

        if (envValue == 2)
        {
            // GOAL
            Debug.Log("AI: goal reached!");
        }
        else if (envValue == 0)
        {
            // FALL: 物理で落とす
            rb.isKinematic = false;
        }

        isMoving = false;
        yield break;
    }

    // getEnvType と state-index 関数は Python 側と同じロジックで
    private int getEnvType(int x, int z)
    {
        // StageManager 側の GetTileState を使う（2=goal,1=stage,0=hole）
        if (stageGenerator == null) return 0;
        return stageGenerator.GetTileState(x, z);
    }

    private int get_state_index(int x, int y, int env)
    {
        if (x < 0 || y < 0 || x >= MAX_X || y >= MAX_Y || env < 0 || env > 2) return -1;
        return x * MAX_Y * ENV_TYPES + y * ENV_TYPES + env;
    }

    // (デバッグ用) キーボードで強制リセット
    void Update()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.rKey.wasPressedThisFrame)
            {
                ResetToStart();
                Debug.Log("AI: Reset by R key");
            }
        }
    }
}
