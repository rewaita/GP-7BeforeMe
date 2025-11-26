using UnityEngine;
using UnityEngine.UI;
using System.Diagnostics;
using System.IO; // ファイルI/O (jsonの存在確認) のため
using System.Collections;

public class GameController : MonoBehaviour
{
    public static GameController instance; // シングルトンパターン

    [Header("UI Elements")]
    public Button trainButton;      // 「学習開始」ボタン
    public Button simulateButton;   // 「シミュレーション開始」ボタン
    public Text statusText;         // "学習中..." などを表示するテキスト

    [Header("Game Objects")]
    public GameObject aiPlayerObject;   // AIPlayer (AIPlayer.cs)
    public GameObject demoPlayerObject; // デモプレイヤー (movP.cs)

    [Header("Python Settings")]
    // (Windowsの例) "C:/Users/YourName/AppData/Local/Programs/Python/Python310/python.exe"
    // (macOS/Linuxの例) "python3" や "/usr/bin/python3"
    // PATHが通っていれば "python" だけで動作する場合もあります。
    
    /*private string pythonExePath = "/usr/bin/python3"; // 確認したPythonのフルパスを設定
    private string pythonScriptPath = "Assets/train.py"; // プロジェクトルートからのパス*/
    private bool isTraining = false;
    private string aiModelPath;
    private string qModelFile = "ai-model_q_table.json";
    private string ilModelFile = "ai-model_il_policy.json";
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
    }

    /// <summary>
    /// (ステップ1) デモプレイヤー(movP.cs)が10回のデモを終えたら呼び出す
    /// </summary>
    
    
    public void AllDemosFinished()
    {
        statusText.text = "デモプレイ完了。学習を開始できます。";
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
        string qModelPath = Path.Combine(aiModelPath, qModelFile);
        string ilModelPath = Path.Combine(aiModelPath, ilModelFile);

        if (File.Exists(qModelPath) && File.Exists(ilModelPath))
        {
            // 成功: ファイル発見
            statusText.text = "学習完了！シミュレーションを開始できます。";
            simulateButton.gameObject.SetActive(true); // シミュレーションボタンを表示
        }
        else
        {
            // 失敗: ファイルが見つからない
            UnityEngine.Debug.LogError("学習プロセスは成功しましたが、指定されたJSONモデルファイルが見つかりません。");
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

        // AIプレイヤーのGameObjectをアクティブにする
        // これにより、AIPlayer.cs の Start() が実行され、AIがモデルを読み込み動き出します。
        aiPlayerObject.SetActive(true);
    }
}