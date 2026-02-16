using UnityEngine;
using UnityEngine.InputSystem;

//記録用
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using System.Numerics;
using Unity.VisualScripting;
using UnityEngine.UI;
//using UnityEditor.SceneManagement;

public class movP : MonoBehaviour
{
    private UnityEngine.Vector3 startPos;
    private int pMCount;
    private int demoCount = 0;
    private int demoMaxCount = 7;
    private bool isMoving = false;
    public Text logText;
    public Text countText;

    private const int rewardGOAL = 1000;
    private const int rewardFALL = -500;

    private StageManager stageManager;
    public MirrorHibi mirrorHibi;

    private List<string> Plogs = new List<string>();
    private Rigidbody rb;
    private Renderer rend;
    private PlayerInput pInput;
    private bool isGameActive = false;

    private Dictionary<Vector2Int, int> visitedTiles = new Dictionary<Vector2Int, int>();

    private Coroutine trapCoroutine; // 現在実行中のトラップコルーチンを記録

    void Start()
    {
        stageManager = FindFirstObjectByType<StageManager>();
        if(stageManager == null)
        {
            Debug.LogError("StageManagerが見つかりません");
        }
        rb = GetComponent<Rigidbody>();
        rend = GetComponent<Renderer>();
        pInput = GetComponent<PlayerInput>();
        startPos = new UnityEngine.Vector3(0, 2, 0);

        SetPlayerActive(false);    
    }

    public void OnStartButton()
    {
        if (isGameActive) return;

        // 最初の初期化（ログ削除など）
        InitializeLogsAndFiles();
        
        // ゲーム開始
        isGameActive = true;
        SetPlayerActive(true);
        ResetStage();
    }

    private void SetPlayerActive(bool isActive)
    {
        // 非アクティブ時は物理挙動も止めておく
        if (!isActive)
        {
            rb.isKinematic = true;
            rb.linearVelocity = UnityEngine.Vector3.zero;
        }

        if (rend != null) rend.enabled = isActive;
        if (pInput != null) pInput.enabled = isActive;
    }

    public void OnRestartButton()
    {
        // 動作中のコルーチンを全て止める（移動中などを強制キャンセル）
        StopAllCoroutines();

        // 変数等の完全リセット
        demoCount = 0;
        isMoving = false;
        
        // ログファイルの全削除（完全な初期状態へ）
        InitializeLogsAndFiles();

        // 状態をアクティブにしてリセット再開
        isGameActive = true;
        SetPlayerActive(true);
        ResetStage();
    }

    private void InitializeLogsAndFiles()
    {
        string deleteLogs = Application.dataPath + "/DemoLogs/";
        string deleteAIs = Application.dataPath + "/DemoAIs/";
        visitedTiles.Clear();

        // ディレクトリが存在しない場合は作成しておく方が安全
        if (!Directory.Exists(deleteLogs)) Directory.CreateDirectory(deleteLogs);
        if (!Directory.Exists(deleteAIs)) Directory.CreateDirectory(deleteAIs);

        if (Directory.Exists(deleteLogs))
        {
            foreach (string file in Directory.GetFiles(deleteLogs))
            {
                File.Delete(file);
            }
        }
        if (Directory.Exists(deleteAIs))
        {
            foreach (string file in Directory.GetFiles(deleteAIs))
            {
                File.Delete(file);
            }
        }
    }

    private void ResetStage()
    {
        if(!isGameActive) return;
        float randomX = Mathf.Round(UnityEngine.Random.Range(-demoCount, demoCount));
        transform.position = startPos + new UnityEngine.Vector3(randomX, 0, 0);
        if(demoCount != 0)
        {
            getEnvType((int)transform.position.x, (int)transform.position.z);
        }
        rb.linearVelocity = UnityEngine.Vector3.zero;
        rb.angularVelocity = UnityEngine.Vector3.zero;
        transform.rotation = UnityEngine.Quaternion.identity;
        StartCoroutine(startFall());
        demoCount++;
        pMCount = 0;
        Plogs.Clear();
        if (demoCount > demoMaxCount) // 7回のデモが終了したら
        {
            // GameControllerに終了を通知
            if (GameController.instance != null)
            {
                GameController.instance.AllDemosFinished();
            }
            
            isGameActive = false;
            SetPlayerActive(false);
            return; // リセット処理を中断
        }
        Plogs.Add("times,nowX,nowY,env,envUp,envDown,envRight,envLeft,action,reward");
        if(pInput != null) pInput.enabled = true;
        
        // カウント表示更新（アニメーション付き）
        UpdateCountDisplay();
    }

    /// <summary>
    /// カウント表示を更新する（画面中央に大きく表示 → 左上に移動するアニメーション）
    /// </summary>
    private void UpdateCountDisplay()
    {
        if (countText != null)
        {
            StartCoroutine(AnimateCountText());
        }
    }

    /// <summary>
    /// カウントテキストのアニメーション
    /// </summary>
    private IEnumerator AnimateCountText()
    {
        // 初期位置を保存（左上）
        RectTransform rectTransform = countText.GetComponent<RectTransform>();
        UnityEngine.Vector3 initialPos = rectTransform.anchoredPosition;
        UnityEngine.Vector3 initialScale = rectTransform.localScale;
        Color initialColor = countText.color;

        // 画面中央に大きく表示
        UnityEngine.Vector3 centerPos = UnityEngine.Vector3.zero;
        UnityEngine.Vector3 largeScale = new UnityEngine.Vector3(3f, 3f, 3f);

        // テキスト内容を更新
        countText.text = $"{demoCount}/{demoMaxCount}";

        // 0.3秒かけて中央に大きく表示
        float duration = 0.3f;
        float elapsed = 0;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            rectTransform.anchoredPosition = UnityEngine.Vector3.Lerp(initialPos, centerPos, t);
            rectTransform.localScale = UnityEngine.Vector3.Lerp(initialScale, largeScale, t);
            yield return null;
        }

        // 0.5秒待機
        yield return new WaitForSeconds(0.5f);

        // 0.5秒かけて左上に移動して縮小
        duration = 0.5f;
        elapsed = 0;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            rectTransform.anchoredPosition = UnityEngine.Vector3.Lerp(centerPos, initialPos, t);
            rectTransform.localScale = UnityEngine.Vector3.Lerp(largeScale, initialScale, t);
            yield return null;
        }

        // 最終位置と大きさを確定
        rectTransform.anchoredPosition = initialPos;
        rectTransform.localScale = initialScale;
    }
    IEnumerator startFall()
    {
        pInput.enabled = false;
        rb.isKinematic = false;
        yield return new WaitForSeconds(1f);
        getEnvType((int)transform.position.x, (int)transform.position.z);
        getEnvType((int)transform.position.x, (int)transform.position.z + 1);
        getEnvType((int)transform.position.x, (int)transform.position.z - 1);
        getEnvType((int)transform.position.x + 1, (int)transform.position.z);
        getEnvType((int)transform.position.x - 1, (int)transform.position.z);
        rb.isKinematic = true;
        pInput.enabled = true;
    }

    private int getEnvType(int x, int z)
    {
        return stageManager.GetTileState(x, z);//2=ゴール,1=ステージあり,0=穴
    }

    /// <summary>
    /// ログテキストに追加表示する
    /// </summary>
    private void AddLogToDisplay(string logMessage)
    {
        logText.text = ""; // 一旦クリアしてから再表示
        logText.text += logMessage;
    }

    public void OnMove(InputValue value)
    {
        if (!isGameActive) return;
        transform.position = new UnityEngine.Vector3(Mathf.Round(transform.position.x), transform.position.y, Mathf.Round(transform.position.z));
        UnityEngine.Vector2 inputVector = value.Get<UnityEngine.Vector2>();
        if (inputVector != UnityEngine.Vector2.zero && !isMoving)
        {
            UnityEngine.Vector3 Muki = new UnityEngine.Vector3(Mathf.RoundToInt(inputVector.x), 0, Mathf.RoundToInt(inputVector.y));//.normalized;
            UnityEngine.Vector3 targetPos = transform.position + Muki;
            StartCoroutine(MoveToPos(targetPos));
        }
    }
    IEnumerator MoveToPos(UnityEngine.Vector3 targetPos)
    {
        //log
        int action = 0;
        int rotate = 0;
        float nowPosX = transform.position.x;
        float nowPosZ = transform.position.z;
        float actionX = targetPos.x - nowPosX;
        float actionZ = targetPos.z - nowPosZ;
        if (actionX > 0){
            action=2;//right
            rotate = 90;
        }
        else if (actionX < 0){
            action = 4;//left
            rotate = -90;
        }
        else if (actionZ > 0){
            action = 1;//up
            rotate = 0;
        }
        else if (actionZ < 0){
            action = 3;//down
            rotate = 180;
        }


        isMoving = true;
        float elapsedTime = 0;
        UnityEngine.Vector3 startPos = transform.position;
        
        transform.rotation = UnityEngine.Quaternion.Euler(0, rotate, 0);

        while (elapsedTime < 0.2f)
        {
            transform.position = UnityEngine.Vector3.Lerp(startPos, targetPos, elapsedTime / 0.2f);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        transform.position = targetPos;

        int envValue = getEnvType((int)targetPos.x, (int)targetPos.z);
        int envUp = getEnvType((int)targetPos.x, (int)targetPos.z + 1);
        int envDown = getEnvType((int)targetPos.x, (int)targetPos.z - 1);
        int envRight = getEnvType((int)targetPos.x + 1, (int)targetPos.z);
        int envLeft = getEnvType((int)targetPos.x - 1, (int)targetPos.z);

        stageManager.SendMessage("MatsChange",targetPos);
        //log

        if (envValue == 2)//GOAl
        {
            Plogs.Add($"{pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardGOAL}");
            AddLogToDisplay($"ゴール 報酬: {rewardGOAL}");
            Debug.Log($"Logs: {pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardGOAL}");
            GetComponent<PlayerInput>().enabled = false;
            StartCoroutine(GOAL());
        }
        else if (envValue == 1)//stage
        {
            Vector2Int tilePos = new Vector2Int((int)targetPos.x, (int)targetPos.z);
            int rewardSTEP = -1;
            if(visitedTiles.ContainsKey(tilePos))
            {
                visitedTiles[tilePos]++;
                switch(visitedTiles[tilePos])
                {
                    case <= 3:
                        rewardSTEP = -1;
                        break;
                    case 4:
                        rewardSTEP = -10;
                        break;
                    case 5:
                        rewardSTEP = -50;
                        break;
                    default:
                        rewardSTEP = -100;
                        break;
                }
            }
            else
            {
                visitedTiles[tilePos] = 1;
            }
            
            Plogs.Add($"{pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardSTEP}");
            AddLogToDisplay($"移動 報酬: {rewardSTEP}");
            Debug.Log($"Logs: {pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardSTEP}");
        }
        else if (envValue == 0)//FALL
        {
            Plogs.Add($"{pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardFALL}");
            AddLogToDisplay($"落下 報酬: {rewardFALL}");
            Debug.Log($"Logs: {pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardFALL}");
            StartCoroutine(FALL());
            GameController.instance.KuroOn();
        }
        else if (envValue == 3)
        {
            trapCoroutine = StartCoroutine(Trapped()); // コルーチン参照を保存
            Plogs.Add($"{pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},-1");
            AddLogToDisplay($"トラップ");
            Debug.Log($"Logs: {pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},-1");
        }
        pMCount++;
        pInput.enabled = true;
        isMoving = false;
    }

    IEnumerator Trapped()
    {
        pInput.enabled = false;
        // 移動距離をランダムに決定 (2～4マス)
        int moveDistance = UnityEngine.Random.Range(2, 5);

        // 移動方向をランダムに決定 (上下左右)
        UnityEngine.Vector3[] directions = {
            new UnityEngine.Vector3(0, 0, 1),  // 上:1
            new UnityEngine.Vector3(1, 0, 0),  // 右:2
            new UnityEngine.Vector3(0, 0, -1),  // 下:3
            new UnityEngine.Vector3(-1, 0, 0) // 左:4
        };
        int action = UnityEngine.Random.Range(0, directions.Length);
        UnityEngine.Vector3 direction = directions[action];
        action++;//用に1増やす

        // 移動先のターゲット位置を計算
        UnityEngine.Vector3 startPos = transform.position;
        UnityEngine.Vector3 targetPos = startPos + direction * moveDistance;

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
            UnityEngine.Vector3 flatPos = UnityEngine.Vector3.Lerp(startPos, targetPos, t);

            // 放物線の高さを計算
            float height = Mathf.Sin(t * Mathf.PI) * arcHeight;

            // 新しい位置を設定
            transform.position = new UnityEngine.Vector3(flatPos.x, startPos.y + height, flatPos.z);

            yield return null;
        }

        // 最終的な位置をターゲット位置に設定
        transform.position = targetPos;
        
        int envValue = getEnvType((int)targetPos.x, (int)targetPos.z);
        int envUp = getEnvType((int)targetPos.x, (int)targetPos.z + 1);
        int envDown = getEnvType((int)targetPos.x, (int)targetPos.z - 1);
        int envRight = getEnvType((int)targetPos.x + 1, (int)targetPos.z);
        int envLeft = getEnvType((int)targetPos.x - 1, (int)targetPos.z);

        stageManager.SendMessage("MatsChange",targetPos);
        //log

        if (envValue == 2)//GOAl
        {
            Plogs.Add($"{pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardGOAL}");
            Debug.Log($"Logs: {pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardGOAL}");
            GetComponent<PlayerInput>().enabled = false;
            AddLogToDisplay($"ゴール 報酬: {rewardGOAL}");
            StartCoroutine(GOAL());
        }
        else if (envValue == 1)//stage
        {
            Vector2Int tilePos = new Vector2Int((int)targetPos.x, (int)targetPos.z);
            int rewardSTEP = -1;
            if(visitedTiles.ContainsKey(tilePos))
            {
                visitedTiles[tilePos]++;
                switch(visitedTiles[tilePos])
                {
                    case <= 3:
                        rewardSTEP = -1;
                        break;
                    case 4:
                        rewardSTEP = -10;
                        break;
                    case 5:
                        rewardSTEP = -50;
                        break;
                    default:
                        rewardSTEP = -100;
                        break;
                }
            }
            else
            {
                visitedTiles[tilePos] = 1;
            }
            
            Plogs.Add($"{pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardSTEP}");
            AddLogToDisplay($"移動 報酬: {rewardSTEP}");
            Debug.Log($"Logs: {pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardSTEP}");
        }
        else if (envValue == 0)//FALL
        {
            Plogs.Add($"{pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardFALL}");
            AddLogToDisplay($"落下 報酬: {rewardFALL}");
            Debug.Log($"Logs: {pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardFALL}");
            StartCoroutine(FALL());
            GameController.instance.KuroOn();
        }
        else if (envValue == 3)
        {
            trapCoroutine = StartCoroutine(Trapped()); // コルーチン参照を保存
            Plogs.Add($"{pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},-1");
            AddLogToDisplay($"トラップ");
            Debug.Log($"Logs: {pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},-1");
        }
        pMCount++;
        pInput.enabled = true;
    }

    IEnumerator GOAL()
    {
        yield return new WaitForSeconds(1f);
        endLog();
        ResetStage();
        mirrorHibi.SendMessage("HibiChange");
    }
    IEnumerator FALL()
    {
        pInput.enabled = false;
        rb.isKinematic = false;
        yield return new WaitForSeconds(2.0f);
        rb.isKinematic = true;
        pInput.enabled = true;
        yield return new WaitForSeconds(0.5f);
        endLog();
        ResetStage();
        mirrorHibi.SendMessage("HibiChange");
    }
    
    //結果判定
    public void endLog()//logをcsvで保存
    {
        string path = Application.dataPath + "/DemoLogs/Plog"+demoCount+".csv";
        File.WriteAllLines(path, Plogs);
    }

    /// <summary>
    /// 鏡との衝突検出 - トラップ飛行中に鏡に接触した場合、落下処理に遷移
    /// </summary>
    private void OnTriggerEnter(Collider col)
    {
        // 鏡との衝突を検出
        if (col.CompareTag("Mirror"))
        {
            Debug.Log("鏡に衝突！トラップをキャンセルして落下させます");
            
            // トラップコルーチンのみを停止
            if (trapCoroutine != null)
            {
                StopCoroutine(trapCoroutine);
                trapCoroutine = null;
            }
            
            // 入力を無効化
            pInput.enabled = false;
            
            // 落下処理を開始
            StartCoroutine(FALL());
            GameController.instance.KuroOn();
        }
    }
}
