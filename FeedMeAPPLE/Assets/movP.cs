using UnityEngine;
using UnityEngine.InputSystem;

//記録用
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using System.Numerics;
using Unity.VisualScripting;
//using UnityEditor.SceneManagement;

public class movP : MonoBehaviour
{
    private UnityEngine.Vector3 startPos;
    private int pMCount;
    private int demoCount = 0;
    private int demoMaxCount = 7;
    private bool isMoving = false;

    private const int rewardGOAL = 1000;
    private const int rewardFALL = -500;

    private StageManager stageManager;

    private List<string> Plogs = new List<string>();
    private Rigidbody rb;
    private Renderer rend;
    private PlayerInput pInput;
    private bool isGameActive = false;

    private Dictionary<Vector2Int, int> visitedTiles = new Dictionary<Vector2Int, int>();

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
        if (rend != null) rend.enabled = isActive;
        if (pInput != null) pInput.enabled = isActive;
        
        // 非アクティブ時は物理挙動も止めておく
        if (!isActive)
        {
            rb.isKinematic = true;
            rb.linearVelocity = UnityEngine.Vector3.zero;
        }
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
        transform.position = startPos;
        rb.linearVelocity = UnityEngine.Vector3.zero;
        rb.angularVelocity = UnityEngine.Vector3.zero;
        transform.rotation = UnityEngine.Quaternion.identity;
        StartCoroutine(startFall());
        demoCount++;
        pMCount = 0;
        Plogs.Clear();
        if (demoCount > demoMaxCount) // 10回のデモが終了したら
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
    }
    IEnumerator startFall()
    {
        pInput.enabled = false;
        rb.isKinematic = false;
        yield return new WaitForSeconds(1f);
        rb.isKinematic = true;
        pInput.enabled = true;
    }

    private int getEnvType(int x, int z)
    {
        return stageManager.GetTileState(x, z);//2=ゴール,1=ステージあり,0=穴
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
            Debug.Log($"Logs: {pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardSTEP}");
        }
        else if (envValue == 0)//FALL
        {
            Plogs.Add($"{pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardFALL}");
            Debug.Log($"Logs: {pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardFALL}");
            GetComponent<PlayerInput>().enabled = false;
            rb.isKinematic = false;
        }
        else if (envValue == 3)
        {
            StartCoroutine(Trapped());
            Plogs.Add($"{pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},-1");
            Debug.Log($"Logs: {pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},-1");
        }

        isMoving = false;
        pMCount++;
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
        action++;//log用に1増やす

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
            Debug.Log($"Logs: {pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardSTEP}");
        }
        else if (envValue == 0)//FALL
        {
            Plogs.Add($"{pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardFALL}");
            Debug.Log($"Logs: {pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardFALL}");
            GetComponent<PlayerInput>().enabled = false;
            rb.isKinematic = false;
        }
        else if (envValue == 3)
        {
            StartCoroutine(Trapped());
            Plogs.Add($"{pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},-1");
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
    }
    
    //結果判定
    
    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Ana"))
        {
            endLog();
            ResetStage();
        }
    }
    public void endLog()//logをcsvで保存
    {
        string path = Application.dataPath + "/DemoLogs/Plog"+demoCount+".csv";
        File.WriteAllLines(path, Plogs);
    }
}
