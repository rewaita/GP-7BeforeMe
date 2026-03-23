using UnityEngine;
using UnityEngine.UI;
using System.Diagnostics;
using System.IO; // ファイルI/O (jsonの存在確認) のため
using System.Collections;
using System.Diagnostics.Contracts;
using System.Threading;
using System.Threading.Tasks;

public class GameController : MonoBehaviour
{
    public static GameController instance; // シングルトンパターン

    [Header("UI Elements")]
    public Button trainButton;
    public Button simulateButton;
    public Button startButton;
    public Button restartButton;
    public Button restartSimulationButton; // シミュレーションをやり直すボタン
    public Button storyButton;
    public Button ruleButton;
    public Text statusText;

    [Header("AI思考プロセス可視化")]
    public Text aiThinkingText;
    public Toggle aiThinkingToggle;

    public RawImage startGamen;
    public RawImage failGamen;
    public RawImage storyGamen;
    public RawImage ruleGamen;
    public RawImage rules2;
    public RawImage rules3;
    public RawImage rules4;
    [Header("Rule Settings")]
    public RawImage[] ruleImages; // 4枚の画像をセット
    public Button nextRuleButton;
    public Button prevRuleButton;
    private int currentRuleIndex = 0;

    public RawImage kuro;
    public RawImage siro;

    public GameObject aiPlayerObject;
    public GameObject demoPlayerObject;//movP.cs
    public StageManager stageManager;
    public moveCamera moveCamera;
    public kao kao;

    [Header("Python Settings")]
    // (Windowsの例) "C:/Users/YourName/AppData/Local/Programs/Python/Python310/python.exe"
    // (macOS/Linuxの例) "python3" や "/usr/bin/python3"
    // PATHが通っていれば "python" だけで動作する場合もあります。
    
    /*private string pythonExePath = "/usr/bin/python3"; // 確認したPythonのフルパスを設定
    private string pythonScriptPath = "Assets/train.py"; // プロジェクトルートからのパス*/
    private bool isTraining = false;
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

        trainButton.gameObject.SetActive(false);
        simulateButton.gameObject.SetActive(false);

        aiPlayerObject.SetActive(false);
        demoPlayerObject.SetActive(true);

        startGamen.gameObject.SetActive(true);
        failGamen.gameObject.SetActive(false);
        storyGamen.gameObject.SetActive(false);
        ruleGamen.gameObject.SetActive(false);
        rules2.gameObject.SetActive(false);
        rules3.gameObject.SetActive(false);
        rules4.gameObject.SetActive(false);
        kuro.gameObject.SetActive(false);
        siro.gameObject.SetActive(false);

        // ルールスライドボタンの非表示
        if (nextRuleButton != null) nextRuleButton.gameObject.SetActive(false);
        if (prevRuleButton != null) prevRuleButton.gameObject.SetActive(false);
        if (nextRuleButton != null) nextRuleButton.onClick.AddListener(OnNextRuleButtonPressed);
        if (prevRuleButton != null) prevRuleButton.onClick.AddListener(OnPrevRuleButtonPressed);

        statusText.text = "デモプレイを開始してください。";
        startButton.gameObject.SetActive(true);
        storyButton.gameObject.SetActive(true);
        ruleButton.gameObject.SetActive(true);
        restartButton.gameObject.SetActive(false);
        restartSimulationButton.gameObject.SetActive(false);

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
    /// ボタンを下方向に移動する
    /// </summary>
    private void MoveButtonsDown()
    {
        float targetY = -480f;
        
        if (startButton != null)
        {
            RectTransform rect = startButton.GetComponent<RectTransform>();
            if (rect != null) rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, targetY);
        }
        if (storyButton != null)
        {
            RectTransform rect = storyButton.GetComponent<RectTransform>();
            if (rect != null) rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, targetY);
        }
        if (ruleButton != null)
        {
            RectTransform rect = ruleButton.GetComponent<RectTransform>();
            if (rect != null) rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, targetY);
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
        storyGamen.gameObject.SetActive(false);
        ruleGamen.gameObject.SetActive(false);
        if (nextRuleButton != null) nextRuleButton.gameObject.SetActive(false);
        if (prevRuleButton != null) prevRuleButton.gameObject.SetActive(false);
        startButton.gameObject.SetActive(false);
        storyButton.gameObject.SetActive(false);
        ruleButton.gameObject.SetActive(false);
        rules2.gameObject.SetActive(false);
        rules3.gameObject.SetActive(false);
        rules4.gameObject.SetActive(false);
        statusText.text = "デモプレイ：AI学習のためのデータを作成してください。";
        demoPlayerObject.SendMessage("OnStartButton");
        stageManager.SendMessage("OnStartButton");
        kao.SendMessage("OnStart");
    }

    public void OnStoryButtonPressed()
    {
        MoveButtonsDown();
        ruleGamen.gameObject.SetActive(false);
        rules2.gameObject.SetActive(false);
        rules3.gameObject.SetActive(false);
        rules4.gameObject.SetActive(false);
        startGamen.gameObject.SetActive(false);
        if (nextRuleButton != null) nextRuleButton.gameObject.SetActive(false);
        if (prevRuleButton != null) prevRuleButton.gameObject.SetActive(false);
        storyGamen.gameObject.SetActive(true);
    }

    public void OnRuleButtonPressed()
    {
        // ボタンを下方向に移動
        MoveButtonsDown();
        storyGamen.gameObject.SetActive(false);
        startGamen.gameObject.SetActive(false);
        ruleGamen.gameObject.SetActive(true);
        rules2.gameObject.SetActive(true);
        rules3.gameObject.SetActive(true);
        rules4.gameObject.SetActive(true);

        // 1枚目表示
        currentRuleIndex = 0;
        UpdateRuleDisplay();

        if (nextRuleButton != null) nextRuleButton.gameObject.SetActive(true);
        if (prevRuleButton != null) prevRuleButton.gameObject.SetActive(true);
    }

    private void UpdateRuleDisplay()
    {
        if (ruleImages == null || ruleImages.Length == 0) return;
        
        for (int i = 0; i < ruleImages.Length; i++)
        {
            if (ruleImages[i] != null)
            {
                ruleImages[i].gameObject.SetActive(i == currentRuleIndex);
            }
        }

        // 基本のruleGamen(土台)は表示したままにする (もし背景等として必要なら)
        // もしruleImages[0]がruleGamenなら上記ループで処理される

        // 矢印ボタンの有効化/無効化 (ループさせない場合)
        if (prevRuleButton != null) prevRuleButton.interactable = (currentRuleIndex > 0);
        if (nextRuleButton != null) nextRuleButton.interactable = (currentRuleIndex < ruleImages.Length - 1);
    }

    public void OnNextRuleButtonPressed()
    {
        if (currentRuleIndex < ruleImages.Length - 1)
        {
            currentRuleIndex++;
            UpdateRuleDisplay();
        }
    }

    public void OnPrevRuleButtonPressed()
    {
        if (currentRuleIndex > 0)
        {
            currentRuleIndex--;
            UpdateRuleDisplay();
        }
    }
    public void AllDemosFinished()
    {
        statusText.text = "デモプレイ完了。学習を開始できます。";
        kao.SendMessage("OnStop");
        trainButton.gameObject.SetActive(true); // 学習ボタンを表示
        moveCamera.SendMessage("DemoplayFinished");
        demoPlayerObject.SetActive(false);    // デモプレイヤーを非表示
        
    }

    /// <summary>
    /// (ステップ2) 「学習開始」ボタンに割り当てる
    /// </summary>
    public void OnTrainButtonPressed()
    {
        if (isTraining) return; // 既に学習中なら何もしない

        // movPクラスのUI要素（ログとカウント）を非表示にする
        if (demoPlayerObject != null)
        {
            movP movP = demoPlayerObject.GetComponent<movP>();
            if (movP != null)
            {
                if (movP.logText != null) movP.logText.gameObject.SetActive(false);
                if (movP.countText != null) movP.countText.gameObject.SetActive(false);
            }
        }

        isTraining = true;
        trainButton.gameObject.SetActive(false);
        statusText.text = "AI学習中...しばらくお待ちください";

        // 学習とステージ移動を並行実行
        StartCoroutine(TrainAndMoveStage());
    }

    /// <summary>
    /// 学習とステージ移動を並行実行
    /// </summary>
    IEnumerator TrainAndMoveStage()
    {
        // 1. ステージ移動コルーチンを開始（非同期）
        Coroutine moveCoroutine = StartCoroutine(stageManager.MoveStageForAI());
        
        // 2. Python学習を開始（並行実行）
        yield return StartCoroutine(RunPythonScript());
        
        // 3. ステージ移動の完了を待つ（まだ終わっていない場合）
        yield return moveCoroutine;
        
        UnityEngine.Debug.Log("学習とステージ移動の両方が完了");
    }

    /// <summary>
    /// Pythonプロセスを実行し、完了を待つコルーチン（非ブロッキング）
    /// </summary>
    IEnumerator RunPythonScript()
    {
        string scriptFullPath = Path.Combine(Application.dataPath, "..", "Assets/StreamingAssets/dist/trainex");

        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = scriptFullPath;
        startInfo.Arguments = "";
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;

        Process process = new Process { StartInfo = startInfo };
        
        UnityEngine.Debug.Log($"Python実行: {scriptFullPath}");
        process.Start();

        // 別スレッドでプロセスの完了を監視
        bool processCompleted = false;
        string output = "";
        string error = "";
        int exitCode = 0;

        Task.Run(() =>
        {
            output = process.StandardOutput.ReadToEnd();
            error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            exitCode = process.ExitCode;
            processCompleted = true;
        });

        // メインスレッドで毎フレーム進行状況を確認（ブロックしない）
        yield return new WaitUntil(() => processCompleted);

        // --- プロセス終了後の処理 ---
        isTraining = false;

        if (exitCode != 0)
        {
            UnityEngine.Debug.LogError($"Python学習エラー: {error}");
            statusText.text = "学習に失敗しました。詳細はコンソールを確認してください。";
            trainButton.gameObject.SetActive(true);
        }
        else
        {
            UnityEngine.Debug.Log($"Python学習完了: {output}");
            statusText.text = "学習完了。シミュレーションを開始して下さい。";
            simulateButton.gameObject.SetActive(true);
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

    public void OnAIGoal1()
    {
        StartCoroutine(ShiroOn());
    }
    public void OnAIGoal2()
    {
        aiPlayerObject.SetActive(false);
        restartButton.gameObject.SetActive(false);

        // AI思考プロセスUIを非表示
        if (aiThinkingText != null) aiThinkingText.gameObject.SetActive(false);
        if (aiThinkingToggle != null) aiThinkingToggle.gameObject.SetActive(false);

        startGamen.gameObject.SetActive(true);
        startButton.gameObject.SetActive(true);
        storyButton.gameObject.SetActive(true);
        ruleButton.gameObject.SetActive(true);
    }

    public IEnumerator ShiroOn()
    {
        siro.gameObject.SetActive(true);
        siro.color = new Color(1, 1, 1, 0);
        float fadeDuration = 1.5f; // フェード時間
        float elapsedTime = 0;

        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            float alpha = elapsedTime / fadeDuration; // 0 → 1

            if (siro != null)
            {
                siro.color = new Color(1, 1, 1, alpha);
            }

            yield return null;
        }
        yield return new WaitForSeconds(2.5f);
        siro.gameObject.SetActive(false);
    }

    public void OnAIFall()
    {
        failGamen.gameObject.SetActive(true);
        restartButton.gameObject.SetActive(true);
        restartButton.gameObject.SetActive(true);
        if (restartSimulationButton != null) restartSimulationButton.gameObject.SetActive(true);
        aiPlayerObject.SetActive(false);

        // AI思考プロセスUIを非表示
        if (aiThinkingText != null) aiThinkingText.gameObject.SetActive(false);
        if (aiThinkingToggle != null) aiThinkingToggle.gameObject.SetActive(false);
    }

    public void OnRestartButtonPressed()
    {
        // データの初期化と学習データの削除
        if (demoPlayerObject != null)
        {
            movP movP = demoPlayerObject.GetComponent<movP>();
            if (movP != null) movP.OnRestartButton();
        }

        if (stageManager != null)
        {
            stageManager.OnRestartButton();
        }

        // UIを最初のタイトル画面の状態に戻す
        restartButton.gameObject.SetActive(false);
        if (restartSimulationButton != null) restartSimulationButton.gameObject.SetActive(false);
        failGamen.gameObject.SetActive(false);
        startGamen.gameObject.SetActive(true);
        storyGamen.gameObject.SetActive(false);
        ruleGamen.gameObject.SetActive(false);
        rules2.gameObject.SetActive(false);
        rules3.gameObject.SetActive(false);
        rules4.gameObject.SetActive(false);
        kuro.gameObject.SetActive(false);
        siro.gameObject.SetActive(false);

        // ボタンの表示状態を戻す
        startButton.gameObject.SetActive(true);
        storyButton.gameObject.SetActive(true);
        ruleButton.gameObject.SetActive(true);
        trainButton.gameObject.SetActive(false);
        simulateButton.gameObject.SetActive(false);

        if (nextRuleButton != null) nextRuleButton.gameObject.SetActive(false);
        if (prevRuleButton != null) prevRuleButton.gameObject.SetActive(false);

        statusText.text = "デモプレイを開始してください。";

        // AI思考プロセスUIを非表示
        if (aiThinkingText != null) aiThinkingText.gameObject.SetActive(false);
        if (aiThinkingToggle != null) aiThinkingToggle.gameObject.SetActive(false);

        aiPlayerObject.SetActive(false);
        demoPlayerObject.SetActive(true);
        kao.SendMessage("OnStart");
    }

    public void OnRestartSimulationButtonPressed()
    {
        restartButton.gameObject.SetActive(false);
        if (restartSimulationButton != null) restartSimulationButton.gameObject.SetActive(false);
        failGamen.gameObject.SetActive(false);

        statusText.text = "AIシミュレーション実行中...";

        // AI思考プロセスUIを有効化
        if (aiThinkingToggle != null)
        {
            aiThinkingToggle.gameObject.SetActive(true);
            aiThinkingToggle.isOn = true;
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

        // 学習データは保持したままOnstartを呼ぶ
        aiPlayerObject.SendMessage("Onstart");
    }

    public void KuroOn()
    {
        kuro.gameObject.SetActive(true);
        kuro.color = new Color(0,0,0,0);
        StartCoroutine(FadeKuro());
    }

    public IEnumerator FadeKuro()
    {
        float fadeDuration = 1.5f; // フェード時間
        float elapsedTime = 0;

        yield return new WaitForSeconds(0.5f);

        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            float alpha = elapsedTime / fadeDuration; // 0 → 1
            
            if (kuro != null)
            {
                kuro.color = new Color(0, 0, 0, alpha);
            }
            
            yield return null;
        }

        yield return new WaitForSeconds(0.5f);
        kuro.gameObject.SetActive(false);

    }

}