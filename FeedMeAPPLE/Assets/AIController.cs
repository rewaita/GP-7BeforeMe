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
    private int maxStepsPerEpisode = 150;
    private int currentStep = 0;

    void Start()
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
    }

    private void ResetToStart()
    {
        transform.position = startPos;
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
            currentStep++;
            // 行動回数が100を超えたら、150に向けて大きさを小さくしていく
            if (currentStep > 100 && currentStep <= 150)
            {
                float scaleFactor = Mathf.Lerp(1.0f, 0.0f, (currentStep - 100) / 50.0f);
                transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);
            }

            if (env == 2)
            {
                Debug.Log("AI: GOAL に到達しました");
                // エピソード終了処理: リセットして再開
                yield return new WaitForSeconds(1.5f);
                ResetToStart();
                yield return new WaitForSeconds(0.5f);
                continue;
            }
            else if (env == 0)
            {
                Debug.Log("AI: 落下しました");
                yield return new WaitForSeconds(1.5f);
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
            }

            yield return new WaitForSeconds(thinkInterval);
        }
    }

    private string BuildStateKey(int x, int y)
    {
        // 現在地
        int env = getEnvType(x, y);

        // Python側: env, up, down, right, left
        // 行動定義: 1=上(y+1), 2=右(x+1), 3=下(y-1), 4=左(x-1)
        // 注意: Pythonのy座標は Unityのz座標に対応させている前提です
        
        int up    = getEnvType(x, y + 1); // Unityでは z+1
        int right = getEnvType(x + 1, y); // Unityでは x+1
        int down  = getEnvType(x, y - 1); // Unityでは z-1
        int left  = getEnvType(x - 1, y); // Unityでは x-1

        // Pythonの str(tuple) 形式に合わせる: "(x, y, env, up, down, right, left)"
        // スペースの有無に注意。Pythonの標準的な tuple 文字列化は "(1, 2, ...)" のようにカンマ後にスペースが入ります。
        // JSONのキーを確認してスペースの有無を調整してください。
        // ここでは Pythonの `str((...))` の標準挙動に合わせてスペースを入れています。
        return $"({x}, {y}, {env}, {up}, {down}, {right}, {left})";
    }

    // 現在の状態に対する行動選択
    private int DecideActionForCurrentState()
    {
        // 座標の取得 (UnityのZをPythonのYとして扱う)
        int px = (int)Mathf.Round(transform.position.x);
        int py = (int)Mathf.Round(transform.position.z);

        // キーの生成
        string stateKey = BuildStateKey(px, py);
        
        // --- 以下、辞書検索ロジック ---

        // 1) ILポリシー
        if (ilPolicy != null && ilPolicy.ContainsKey(stateKey))
        {
            return ilPolicy[stateKey];
        }

        // 2) Qテーブル
        if (qTable != null && qTable.ContainsKey(stateKey))
        {
            List<float> qrow = qTable[stateKey];
            // Python側: index 0 is unused, 1..4 represent actions
            if (qrow != null && qrow.Count >= ACTION_SPACE + 1)
            {
                int bestAction = 1;
                float bestVal = qrow[1];
                // argmax logic...
                for (int a = 2; a <= ACTION_SPACE; a++)
                {
                    if (qrow[a] > bestVal)
                    {
                        bestVal = qrow[a];
                        bestAction = a;
                    }
                }
                // 値が全て0でなければ採用
                bool anyNonZero = false;
                for(int i=1; i<=ACTION_SPACE; i++) if(Mathf.Abs(qrow[i]) > 1e-6f) anyNonZero = true;
                
                if (anyNonZero) return bestAction;
            }
        }

        // 3) フォールバック（単純なヒューリスティック or ランダム）
        // まずは安全行動（上、右、下、左の順）を試す（到達可能性を見てから選ぶ）
        // 簡易チェック: 各候補に対して穴でなければ選ぶ
        int[] order = new int[] { 1, 2, 3, 4 };
        foreach (int a in order)
        {
            Vector3 dir = ActionToVector(a);
            int nx = (int)Mathf.Round(transform.position.x + dir.x);
            int nz = (int)Mathf.Round(transform.position.z + dir.z);
            int e = getEnvType(nx, nz);
            if (e != 0) return a; // 穴でない方向を優先
        }

        // 最終的にランダム
        return UnityEngine.Random.Range(1, ACTION_SPACE + 1);
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
        float duration = 0.2f;

        while (elapsedTime < duration)
        {
            transform.position = Vector3.Lerp(from, targetPos, elapsedTime / duration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // 到達
        transform.position = targetPos;
        int envValue = getEnvType((int)targetPos.x, (int)targetPos.z);
        // デバッグログ（必要に応じて調整）
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
        if (x < 0 || y < 0 || x >= MAX_X || y >= MAX_Y || (env < 0 || env > 2)) return -1;
        return x * (MAX_Y * ENV_TYPES) + y * ENV_TYPES + env;
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
