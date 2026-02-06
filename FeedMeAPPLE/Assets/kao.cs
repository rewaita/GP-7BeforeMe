using UnityEngine;
using System.Collections;

public class kao : MonoBehaviour
{
    private void OnStart()
    {
        InvokeRepeating(nameof(PlayRandomAnimation), 0f, 5f);
    }

    private void OnStop()
    {
        CancelInvoke(nameof(PlayRandomAnimation));
    }

    private void PlayRandomAnimation()
    {
        int randomIndex = Random.Range(0, 3);
        
        switch (randomIndex)
        {
            case 0:
                StartCoroutine(ShakeHeadHorizontal());
                break;
            case 1:
                StartCoroutine(ShakeHeadVertical());
                break;
            case 2:
                StartCoroutine(RotateHeadCircle());
                break;
        }
    }

    /// <summary>
    /// 首を横に振るアニメーション（2秒間）
    /// yを+30~-30へと数回振る
    /// </summary>
    private IEnumerator ShakeHeadHorizontal()
    {
        Quaternion baseRotation = Quaternion.Euler(-90f, 0f, 90f);
        float elapsedTime = 0f;
        float animationDuration = 2f;
        int shakes = 3; // 振動回数

        while (elapsedTime < animationDuration)
        {
            // サイン波で+30～-30の範囲で振動
            float yRotation = Mathf.Sin((elapsedTime / animationDuration) * shakes * Mathf.PI * 2f) * 30f;
            transform.localRotation = baseRotation * Quaternion.Euler(0, yRotation, 0);
            
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        transform.localRotation = baseRotation;
    }

    /// <summary>
    /// 首を縦に振るアニメーション（2秒間）
    /// xを-70~-120へと数回振る
    /// </summary>
    private IEnumerator ShakeHeadVertical()
    {
        Quaternion baseRotation = Quaternion.Euler(-90f, 0f, 90f);
        float elapsedTime = 0f;
        float animationDuration = 2f;
        int shakes = 3; // 振動回数
        float minX = -120f;
        float maxX = -70f;
        float centerX = (minX + maxX) / 2f; // -95
        float rangeX = (maxX - minX) / 2f;  // 25

        while (elapsedTime < animationDuration)
        {
            // サイン波で-120～-70の範囲で振動
            float xRotation = centerX + Mathf.Sin((elapsedTime / animationDuration) * shakes * Mathf.PI * 2f) * rangeX;
            transform.localRotation = baseRotation * Quaternion.Euler(xRotation, 0, 0);
            
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        transform.localRotation = baseRotation;
    }

    /// <summary>
    /// 顔を回すアニメーション（2秒間で2回転）
    /// 基準値(x=-90, y=0)に対して円を描くように2回転
    /// </summary>
    private IEnumerator RotateHeadCircle()
    {
        Quaternion baseRotation = Quaternion.Euler(-90f, 0f, 90f); // 基準値
        float elapsedTime = 0f;
        float animationDuration = 2f;
        float rotations = 2f; // 2回転

        while (elapsedTime < animationDuration)
        {
            float progress = elapsedTime / animationDuration;
            float angle = progress * rotations * Mathf.PI * 2f; // 0～4π (2回転)

            // 円を描くようにx=-90を中心にy方向に回転
            float yRotation = Mathf.Sin(angle) * 30f;
            float zRotation = Mathf.Cos(angle) * 30f;

            transform.localRotation = baseRotation * Quaternion.Euler(0, yRotation, zRotation);
            
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        transform.localRotation = baseRotation;
    }
}
