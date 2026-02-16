using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// RawImageオブジェクトに直接アタッチして使用するスクリプト
/// ヒビの進行状態に応じてテクスチャを切り替える
/// </summary>
[RequireComponent(typeof(RawImage))]
public class MirrorHibi : MonoBehaviour
{
    [Header("ヒビのテクスチャを順番に登録 (要素0は初期状態)")]
    [SerializeField] private Texture[] crackTextures;

    private RawImage rawImage;
    private int currentStage = 0;

    void Start()
    {
        // RawImageコンポーネントを取得
        rawImage = GetComponent<RawImage>();
        
        // 初期状態のテクスチャを設定
        if (crackTextures.Length > 0)
        {
            rawImage.texture = crackTextures[0];
        }
    }

    /// <summary>
    /// ステージクリア時に呼び出してヒビを進行させる
    /// 次のテクスチャに切り替える
    /// </summary>
    public void HibiChange()
    {
        Debug.Log("HibiChangeメソッドが呼び出されました");
        currentStage++;
        // 配列の範囲内でテクスチャを切り替える
        if (currentStage < crackTextures.Length)
        {
            rawImage.texture = crackTextures[currentStage];
            Debug.Log($"ミラーのヒビがステージ {currentStage} に進行しました");
        }
        else
        {
            // 最後のステージに達した場合の処理
            Debug.Log("ミラーのヒビが完全に破壊されました");
        }
    }

    /// <summary>
    /// 現在のステージを取得
    /// </summary>
    public int GetCurrentStage()
    {
        return currentStage;
    }
}