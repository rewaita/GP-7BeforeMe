using UnityEngine;
using UnityEngine.InputSystem;

//記録用
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using UnityEditor.SceneManagement;

public class movP : MonoBehaviour
{
    private Vector3 startPos;
    private int pMCount;
    private int demoCount = 0;
    private int demoMaxCount = 5;
    private bool isMoving = false;

    private const int rewardGOAL = 200;
    private const int rewardFALL = -100;

    private StageManager stageGenerator;

    private List<string> Plogs = new List<string>();
    private Rigidbody rb;

    void Start()
    {
        stageGenerator = FindFirstObjectByType<StageManager>();
        if(stageGenerator == null)
        {
            Debug.LogError("StageManagerが見つかりません");
        }
        rb = GetComponent<Rigidbody>();
        startPos = new Vector3(0, 2, 0);

        string deleteLogs = Application.dataPath + "/DemoLogs/";
        string deleteAIs = Application.dataPath + "/DemoAIs/";
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
        ResetStage();//ステージをリセット        
    }

    private void ResetStage()
    {
        transform.position = startPos;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.rotation = Quaternion.identity;
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
            
            // デモプレイヤー自身を無効化
            this.gameObject.SetActive(false); 
            return; // リセット処理を中断
        }
        Plogs.Add("times,nowX,nowY,env,envUp,envDown,envRight,envLeft,action,reward");
        GetComponent<PlayerInput>().enabled = true;
    }
    IEnumerator startFall()
    {
        rb.isKinematic = false;
        yield return new WaitForSeconds(1f);
        rb.isKinematic = true;
    }

    private int getEnvType(int x, int z)
    {
        return stageGenerator.GetTileState(x, z);//2=ゴール,1=ステージあり,0=穴
    }

    public void OnMove(InputValue value)
    {
        transform.position = new Vector3(Mathf.Round(transform.position.x), transform.position.y, Mathf.Round(transform.position.z));
        Vector2 inputVector = value.Get<Vector2>();
        if (inputVector != Vector2.zero && !isMoving)
        {
            Vector3 Muki = new Vector3(Mathf.RoundToInt(inputVector.x), 0, Mathf.RoundToInt(inputVector.y));//.normalized;
            Vector3 targetPos = transform.position + Muki;
            StartCoroutine(MoveToPos(targetPos));
        }
    }
    IEnumerator MoveToPos(Vector3 targetPos)
    {
        //log
        int action = 0;
        float nowPosX = transform.position.x;
        float nowPosZ = transform.position.z;
        float actionX = targetPos.x - nowPosX;
        float actionZ = targetPos.z - nowPosZ;
        if (actionX > 0) action = 2;//right
        else if (actionX < 0) action = 4;//left
        else if (actionZ > 0) action = 1;//up
        else if (actionZ < 0) action = 3;//down

        isMoving = true;
        float elapsedTime = 0;
        Vector3 startPos = transform.position;

        while (elapsedTime < 0.2f)
        {
            transform.position = Vector3.Lerp(startPos, targetPos, elapsedTime / 0.2f);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        int envValue = getEnvType((int)targetPos.x, (int)targetPos.z);
        int envUp = getEnvType((int)targetPos.x, (int)targetPos.z + 1);
        int envDown = getEnvType((int)targetPos.x, (int)targetPos.z - 1);
        int envRight = getEnvType((int)targetPos.x + 1, (int)targetPos.z);
        int envLeft = getEnvType((int)targetPos.x - 1, (int)targetPos.z);
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
            Plogs.Add($"{pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},-1");
            Debug.Log($"Logs: {pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},-1");
        }
        else if (envValue == 0)//FALL
        {
            Plogs.Add($"{pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardFALL}");
            Debug.Log($"Logs: {pMCount},{targetPos.x},{targetPos.z},{envValue},{envUp},{envDown},{envRight},{envLeft},{action},{rewardFALL}");
            GetComponent<PlayerInput>().enabled = false;
            rb.isKinematic = false;
        }

        transform.position = targetPos;
        isMoving = false;
        pMCount++;
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
        for (int i = 1; i <= 10; i++)
        {
            int dCd = demoCount * 100 + i;
            string additionalPath = Application.dataPath + $"/DemoLogs/Plog"+dCd+".csv";
            File.WriteAllLines(additionalPath, Plogs);
        }
        Debug.Log("Log saved to " + path);
    }
}
