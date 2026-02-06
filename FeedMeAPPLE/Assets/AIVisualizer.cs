using UnityEngine;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// AI思考の可視化システム
/// - 方向別スコアをテキスト表示（AIUICanvasの既存TMP使用）
/// - ゴール位置を金色枠で表示＆接続線で繋ぐ
/// - 危険地帯を赤黒い枠で表示
/// </summary>
public class AIVisualizer : MonoBehaviour
{
    [Header("--- スコア表示設定（既存UI参照） ---")]
    [SerializeField] private Canvas aiUICanvas;         // AIUICanvasオブジェクト
    [SerializeField] private GameObject aiPlayer;          // AIプレイヤーオブジェクト
    [SerializeField] private TextMeshProUGUI scoreUp;      // ue
    [SerializeField] private TextMeshProUGUI scoreRight;   // migi
    [SerializeField] private TextMeshProUGUI scoreDown;    // sita
    [SerializeField] private TextMeshProUGUI scoreLeft;    // hidari

    [Header("--- マーキング設定 ---")]
    [SerializeField] private GameObject framePrefab;       // frame Prefab
    [SerializeField] private Color goalColor = new Color(0.8f, 0.7f, 0f, 1f);
    [SerializeField] private Color dangerColor = new Color(0.5f, 0f, 0f, 1f);
    [SerializeField] private float markerScale = 0.8f;

    [Header("--- 線の設定 ---")]
    [SerializeField] private Material lineMaterial;
    [SerializeField] private float lineWidth = 0.1f;

    private GameObject goalMarker;
    private List<GameObject> dangerMarkers = new List<GameObject>();
    private LineRenderer goalLine;

    private Vector2Int lastGoalPos = new Vector2Int(-999, -999);
    private HashSet<Vector2Int> lastDangerZones = new HashSet<Vector2Int>();

    void Start()
    {
        InitializeLineRenderer();
    }

    void Update()
    {
        UpdateGoalLine();
    }

    void ShowScores()
    {
        if (aiUICanvas != null) aiUICanvas.enabled = true;
        aiUICanvas.transform.position = aiPlayer.transform.position + new Vector3(0, 2.0f, 0);
    }
    void HideScores()
    {
        if (aiUICanvas != null)
            aiUICanvas.enabled = false;
    }

    /// <summary>
    /// LineRendererを初期化（ゴールへの接続線）
    /// </summary>
    private void InitializeLineRenderer()
    {
        GameObject lineObj = new GameObject("GoalLine");
        lineObj.transform.SetParent(transform);
        lineObj.transform.localPosition = Vector3.zero;

        goalLine = lineObj.AddComponent<LineRenderer>();
        if (lineMaterial != null)
            goalLine.material = lineMaterial;
        goalLine.startWidth = lineWidth;
        goalLine.endWidth = lineWidth;
        goalLine.positionCount = 0;
    }

    /// <summary>
    /// マーカープリファブを取得（ユーザーが設定したframeを使用）
    /// </summary>
    private GameObject GetOrCreateMarker()
    {
        if (framePrefab != null)
        {
            return Instantiate(framePrefab);
        }
        else
        {
            // フォールバック：デフォルトでシンプルなCubeを生成
            GameObject cube = new GameObject("DefaultMarker");
            cube.AddComponent<MeshFilter>().mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            MeshRenderer renderer = cube.AddComponent<MeshRenderer>();
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = goalColor;
            renderer.material = mat;
            cube.transform.localScale = new Vector3(1, 0.1f, 1) * markerScale;
            return cube;
        }
    }

    /// <summary>
    /// AIControllerからスコアを受け取り、TextMeshProに表示
    /// </summary>
    public void UpdateScoreValues(float[] finalScores)
    {
        if (scoreUp != null) scoreUp.text = finalScores[1].ToString("F0");
        if (scoreRight != null) scoreRight.text = finalScores[2].ToString("F0");
        if (scoreDown != null) scoreDown.text = finalScores[3].ToString("F0");
        if (scoreLeft != null) scoreLeft.text = finalScores[4].ToString("F0");
    }

    /// <summary>
    /// ゴール位置のマーキングを更新
    /// </summary>
    public void UpdateGoalPosition(Vector2Int goalPos)
    {
        if (goalPos == lastGoalPos) return;
        lastGoalPos = goalPos;

        if (goalMarker != null) Destroy(goalMarker);

        if (goalPos.x >= -100 && goalPos.x < 100)
        {
            goalMarker = GetOrCreateMarker();
            goalMarker.name = "GoalMarker";
            goalMarker.transform.position = new Vector3(goalPos.x, 0.5f, goalPos.y);
            goalMarker.transform.localScale = Vector3.one * markerScale;

            // マテリアルを金色に設定
            MeshRenderer renderer = goalMarker.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = goalColor;
                renderer.material = mat;
            }
        }
    }

    /// <summary>
    /// 危険地帯のマーキングを更新
    /// </summary>
    public void UpdateDangerZones(HashSet<Vector2Int> dangerZones)
    {
        // 削除されたものを検出して削除
        foreach (Vector2Int zone in lastDangerZones)
        {
            if (!dangerZones.Contains(zone))
            {
                var marker = dangerMarkers.Find(m => 
                    Mathf.Approximately(m.transform.position.x, zone.x) &&
                    Mathf.Approximately(m.transform.position.z, zone.y));
                if (marker != null)
                {
                    Destroy(marker);
                    dangerMarkers.Remove(marker);
                }
            }
        }

        // 新規のものを追加
        foreach (Vector2Int zone in dangerZones)
        {
            if (!lastDangerZones.Contains(zone))
            {
                GameObject marker = GetOrCreateMarker();
                marker.name = $"DangerMarker_{zone.x}_{zone.y}";
                marker.transform.position = new Vector3(zone.x, 0.5f, zone.y);
                marker.transform.localScale = Vector3.one * markerScale;

                // マテリアルを赤黒に設定
                MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    Material mat = new Material(Shader.Find("Standard"));
                    mat.color = dangerColor;
                    renderer.material = mat;
                }

                dangerMarkers.Add(marker);
            }
        }

        lastDangerZones.Clear();
        foreach (Vector2Int zone in dangerZones) lastDangerZones.Add(zone);
    }

    /// <summary>
    /// AIからゴールへの接続線を更新
    /// </summary>
    private void UpdateGoalLine()
    {
        if (goalMarker == null || goalLine == null) return;

        goalLine.positionCount = 2;
        goalLine.SetPosition(0, transform.position + Vector3.up * 0.5f);
        goalLine.SetPosition(1, goalMarker.transform.position);
    }

    /// <summary>
    /// クリーンアップ
    /// </summary>
    public void Cleanup()
    {
        foreach (GameObject marker in dangerMarkers)
        {
            if (marker != null) Destroy(marker);
        }
        dangerMarkers.Clear();

        if (goalMarker != null) Destroy(goalMarker);
    }
}
