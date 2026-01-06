using UnityEngine;
using UnityEngine.UI;
using System.Diagnostics;
using System.IO; // ファイルI/O (jsonの存在確認) のため
using System.Collections;
using System.Diagnostics.Contracts;

public class GameController : MonoBehaviour
{
    public static GameController instance; // シングルトンパターン

    [Header("UI Elements")]
    public Button trainButton;
    public Button simulateButton;
    public Button startButton;
    public Button restartButton;
    public Text statusText;

    [Header("AI思考プロセス可視化")]
    public Text aiThinkingText;
    public Toggle aiThinkingToggle;

    public RawImage startGamen;
    public RawImage clearGamen;
    public RawImage failGamen;

    public GameObject aiPlayerObject;
    public GameObject demoPlayerObject;//movP.cs
    public StageManager stageManager;
    public kao kao;

    [Header("Python Settings")]
    // (Windowsの例) "C:/Users/YourName/AppData/Local/Programs/Python/Python310/python.exe"
    // (macOS/Linuxの例) "python3" や "/usr/bin/python3"
    // PATHが通っていれば "python" だけで動作する場合もあります。
    
    /*private string pythonExePath = "/usr/bin/python3"; // 確認したPythonのフルパスを設定
    private string pythonScriptPath = "Assets/train.py"; // プロジェクトルートからのパス*/
    private bool isTraining = false;
    private string aiModelPath;
    // 新しいモデルファイル名（行動クローニングベース）
    private string bcPolicyFile = "bc_policy.json";
    private string rewardGradientFile = "reward_gradient.json";
    private string goalPositionsFile = "goal_positions.json";
    void Awake()
    {
        // シングルトンインスタンスの設定
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    void Start()
    {
        aiModelPath = Path.Combine(Application.dataPath, "DemoAIs");

        trainButton.gameObject.SetActive(false);
        simulateButton.gameObject.SetActive(false);

        aiPlayerObject.SetActive(false);
        demoPlayerObject.SetActive(true);

        startGamen.gameObject.SetActive(true);
        clearGamen.gameObject.SetActive(false);
        failGamen.gameObject.SetActive(false);

        statusText.text = "デモプレイを開始してください。";
        startButton.gameObject.SetActive(true);
        restartButton.gameObject.SetActive(false);

        // AI思考プロセスUIの初期化
        if (aiThinkingText != null)
        {
            aiThinkingText.gameObject.SetActive(false);
        }
        if (aiThinkingToggle != null)
        {
            aiThinkingToggle.gameObject.SetActive(false);
            aiThinkingToggle.onValueChanged.AddListener(OnThinkingToggleChanged);
        }
    }

    /// <summary>
    /// AI思考プロセス表示のトグル切り替え
    /// </summary>
    private void OnThinkingToggleChanged(bool isOn)
    {
        if (aiThinkingText != null)
        {
            aiThinkingText.gameObject.SetActive(isOn);
        }
    }

    /// <summary>
    /// (ステップ1) デモプレイヤー(movP.cs)が10回のデモを終えたら呼び出す
    /// </summary>
    
    public void OnStartButtonPressed()
    {
        startGamen.gameObject.SetActive(false);
        startButton.gameObject.SetActive(false);
        statusText.text = "デモプレイ中...";
        demoPlayerObject.SendMessage("OnStartButton");
        stageManager.SendMessage("OnStartButton");
        kao.SendMessage("OnStart");
    }    
    public void AllDemosFinished()
    {
        statusText.text = "デモプレイ完了。学習を開始できます。";
        kao.SendMessage("OnStop");
        trainButton.gameObject.SetActive(true); // 学習ボタンを表示
        demoPlayerObject.SetActive(false);    // デモプレイヤーを非表示
        
    }

    /// <summary>
    /// (ステップ2) 「学習開始」ボタンに割り当てる
    /// </summary>
    public void OnTrainButtonPressed()
    {
        if (isTraining) return; // 既に学習中なら何もしない

        isTraining = true;
        trainButton.gameObject.SetActive(false);
        statusText.text = "AI学習中... (完了まで数分待機)";

        // Pythonスクリプトを非同期で実行するコルーチンを開始
        StartCoroutine(RunPythonScript());
    }

    /// <summary>
    /// Pythonプロセスを実行し、完了を待つコルーチン
    /// </summary>
    IEnumerator RunPythonScript()
    {
        // Pythonスクリプトのフルパスを取得
        //string scriptFullPath = Path.Combine(Application.dataPath, "..", pythonScriptPath);
        string scriptFullPath = Path.Combine(Application.dataPath, "..", "Assets/dist/trainexxx");

        // --- 1. Pythonプロセスを開始 ---
        ProcessStartInfo startInfo = new ProcessStartInfo();
        //startInfo.FileName = pythonExePath;
        startInfo.FileName = scriptFullPath;
        //startInfo.Arguments = $"\"{scriptFullPath}\""; // スクリプトパスを引数として渡す
        startInfo.Arguments = ""; // 引数なしで実行
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true; // コンソールウィンドウを非表示

        Process process = new Process { StartInfo = startInfo };
        
        UnityEngine.Debug.Log($"Python実行: {scriptFullPath}");
        
        process.Start();

        // (オプション) Pythonの出力を読み取る
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        // プロセスが終了するまで待機
        yield return new WaitUntil(() => process.HasExited);

        // --- 2. プロセス終了後の処理 ---
        isTraining = false;

        if (process.ExitCode != 0)
        {
            // 学習失敗
            UnityEngine.Debug.LogError($"Python学習エラー: {error}");
            statusText.text = "学習に失敗しました。詳細はコンソールを確認してください。";
            trainButton.gameObject.SetActive(true); // 再試行できるようにボタンを戻す
        }
        else
        {
            // 学習成功
            UnityEngine.Debug.Log($"Python学習完了: {output}");
            statusText.text = "学習完了。AIモデルをチェック中...";
            
            // --- 3. JSONファイルの存在確認 ---
            CheckForModelFiles();
        }
    }

    /// <summary>
    /// 学習完了後、DemoAIsフォルダにjsonファイルがあるか確認
    /// </summary>
    private void CheckForModelFiles()
    {
        string bcPolicyPath = Path.Combine(aiModelPath, bcPolicyFile);
        string rewardGradientPath = Path.Combine(aiModelPath, rewardGradientFile);
        string goalPositionsPath = Path.Combine(aiModelPath, goalPositionsFile);

        // BC Policyは必須、他はオプション
        if (File.Exists(bcPolicyPath))
        {
            // 成功: ファイル発見
            string extraInfo = "";
            if (File.Exists(rewardGradientPath)) extraInfo += " 報酬勾配✓";
            if (File.Exists(goalPositionsPath)) extraInfo += " ゴール座標✓";
            statusText.text = $"学習完了！BC Policy✓{extraInfo}\nシミュレーションを開始できます。";
            simulateButton.gameObject.SetActive(true); // シミュレーションボタンを表示
        }
        else
        {
            // 失敗: ファイルが見つからない
            UnityEngine.Debug.LogError("学習プロセスは成功しましたが、BC Policyファイルが見つかりません。");
            statusText.text = "学習完了。しかしモデルファイルが見つかりません。";
        }
    }

    /// <summary>
    /// (ステップ3) 「シミュレーション開始」ボタンに割り当てる
    /// </summary>
    public void OnSimulateButtonPressed()
    {
        simulateButton.gameObject.SetActive(false);
        statusText.text = "AIシミュレーション実行中...";

        // AI思考プロセスUIを有効化
        if (aiThinkingToggle != null)
        {
            aiThinkingToggle.gameObject.SetActive(true);
            aiThinkingToggle.isOn = true; // デフォルトで表示ON
        }
        if (aiThinkingText != null)
        {
            aiThinkingText.gameObject.SetActive(true);
        }

        // AIプレイヤーのGameObjectをアクティブにする
        aiPlayerObject.SetActive(true);

        // AIControllerにthinkingTextを渡す
        AIController aiController = aiPlayerObject.GetComponent<AIController>();
        if (aiController != null && aiThinkingText != null)
        {
            aiController.thinkingText = aiThinkingText;
        }

        aiPlayerObject.SendMessage("Onstart");
    }

    public void OnAIGoal()
    {
        statusText.text = "AIがゴールしました！シミュレーション完了。";
        clearGamen.gameObject.SetActive(true);
        aiPlayerObject.SetActive(false);
        restartButton.gameObject.SetActive(true);

        // AI思考プロセスUIを非表示
        if (aiThinkingText != null) aiThinkingText.gameObject.SetActive(false);
        if (aiThinkingToggle != null) aiThinkingToggle.gameObject.SetActive(false);
    }
    int falseCount = 0;
    public void OnAIFall()
    {
        falseCount++;
        statusText.text = $"AIが落下しました！やり直し回数: {falseCount}";
        if(falseCount>=3)
        {
            failGamen.gameObject.SetActive(true);
            restartButton.gameObject.SetActive(true);
            aiPlayerObject.SetActive(false);

            // AI思考プロセスUIを非表示
            if (aiThinkingText != null) aiThinkingText.gameObject.SetActive(false);
            if (aiThinkingToggle != null) aiThinkingToggle.gameObject.SetActive(false);
        }
    }

    public void OnRestartButtonPressed()
    {
        
        restartButton.gameObject.SetActive(false);
        clearGamen.gameObject.SetActive(false);
        failGamen.gameObject.SetActive(false);
        falseCount = 0;

        // AI思考プロセスUIを非表示
        if (aiThinkingText != null) aiThinkingText.gameObject.SetActive(false);
        if (aiThinkingToggle != null) aiThinkingToggle.gameObject.SetActive(false);

        aiPlayerObject.SetActive(false);
        demoPlayerObject.SetActive(true);
        demoPlayerObject.SendMessage("OnRestartButton");
        stageManager.SendMessage("OnRestartButton");
        kao.SendMessage("OnStart");
    }
}