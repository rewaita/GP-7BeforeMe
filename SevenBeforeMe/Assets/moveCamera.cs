using UnityEngine;

public class moveCamera : MonoBehaviour
{
    public GameObject Pl;
    public GameObject AIpl;

    [Header("--- ズーム設定 ---")]
    [SerializeField] private int zoomLevels = 200;  // ズームレベル数（0-200）
    [SerializeField] private float zoomCoolTime = 0.05f;  // ズーム入力のクールタイム（秒）
    private int currentZoomLevel = 5;  // 初期ズームレベル（中間）
    private float lastZoomInputTime = -1f;  // 最後のズーム入力時刻

    // ズーム時のオフセット範囲
    // 最も近い時：y+5, z+5
    // 最も遠い時：y+55, z+0
    private float minY = 5f;
    private float maxY = 55f;
    private float minZ = 5f;
    private float maxZ = 0f;

    void Update()
    {
        // ズーム入力の処理
        HandleZoomInput();

        if (Pl.activeSelf)
        {
            Vector3 PlPosition = Pl.transform.position;
            Vector3 cameraOffset = CalculateCameraOffset();
            transform.position = new Vector3(PlPosition.x, PlPosition.y + cameraOffset.y, PlPosition.z - cameraOffset.z);
            transform.LookAt(Pl.transform);
        }
        else if (AIpl.activeSelf)
        {
            Vector3 AIplPosition = AIpl.transform.position;
            Vector3 cameraOffset = CalculateCameraOffset();
            transform.position = new Vector3(AIplPosition.x, AIplPosition.y + cameraOffset.y, AIplPosition.z - cameraOffset.z);
            transform.LookAt(AIpl.transform);
        }
    }

    void DemoplayFinished()
    {
        transform.position = new Vector3(0, 2, -5);
        transform.eulerAngles = new Vector3(0, 0, 0);
    }

    /// <summary>
    /// ズーム入力を処理（マウスホイール & 矢印キー上下）
    /// クールタイムで入力頻度を制限
    /// </summary>
    private void HandleZoomInput()
    {
        // クールタイムをチェック
        bool canInput = (Time.time - lastZoomInputTime) >= zoomCoolTime;

        // マウスホイール入力
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (scrollInput != 0f && canInput)
        {
            // スクロール上（正値）でズームイン、下（負値）でズームアウト
            int zoomDelta = scrollInput > 0f ? 1 : -1;
            currentZoomLevel = Mathf.Clamp(currentZoomLevel + zoomDelta, 0, zoomLevels);
            lastZoomInputTime = Time.time;
        }

        // 矢印キー上下入力（長押し対応）
        if (Input.GetKey(KeyCode.UpArrow) && canInput)
        {
            currentZoomLevel = Mathf.Clamp(currentZoomLevel + 1, 0, zoomLevels);
            lastZoomInputTime = Time.time;
        }
        if (Input.GetKey(KeyCode.DownArrow) && canInput)
        {
            currentZoomLevel = Mathf.Clamp(currentZoomLevel - 1, 0, zoomLevels);
            lastZoomInputTime = Time.time;
        }
    }

    /// <summary>
    /// 現在のズームレベルに応じてカメラオフセットを計算
    /// </summary>
    private Vector3 CalculateCameraOffset()
    {
        // ズームレベルを0-1の比率に正規化
        float zoomRatio = (float)currentZoomLevel / (float)zoomLevels;

        // Y軸：最小から最大へ線形補間（ズームアウトで増加）
        float offsetY = Mathf.Lerp(minY, maxY, zoomRatio);

        // Z軸：最小から最大へ線形補間（ズームアウトで減少）
        float offsetZ = Mathf.Lerp(minZ, maxZ, zoomRatio);

        return new Vector3(0, offsetY, offsetZ);
    }
}
