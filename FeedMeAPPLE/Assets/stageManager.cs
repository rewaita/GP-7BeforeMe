using UnityEngine;
using System.Collections.Generic;
using System.IO.Hashing;

public class StageManager : MonoBehaviour
{
    public GameObject blockPrefab; // 1x1x1 のブロック
    public GameObject goalBlock;
    private int xMin = -7;
    private int xMax = 7;
    private int zMin = -5;
    private int zMax = 25;
    private Vector3 origin = new Vector3(0, 0, 0); // 基準位置

    // ステージ状態管理用（1=ステージあり, 0=穴）
    private int[,] stageMap;

    void Start()
    {
        GenerateStage();
    }

    void GenerateStage()
    {
        int width = xMax - xMin + 1;
        int depth = zMax - zMin + 1;

        stageMap = new int[width, depth];

        // 1. まず全てを「ブロック(1)」で埋める
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                stageMap[x, z] = 1;
            }
        }

        // 2. ゴール地点の設定 (手前2列)
        for (int z = depth - 2; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                stageMap[x, z] = 2;
            }
        }
        // ゴールオブジェクトの配置
        if (goalBlock != null)
        {
            goalBlock.transform.localScale = new Vector3(width, 1, 3);
            goalBlock.transform.position = origin + new Vector3(0, 0, zMax);
        }

        // 3. 「絶対に通行可能な正解ルート」を確保する (保護フラグ 9 を立てる)
        CreateGuaranteedPath(width, depth);

        // 4. ランダムに穴を掘る（複雑な形状＆行き止まり作成）
        CarveComplexHoles(width, depth);

        // 5. スタート地点の安全地帯を作る
        for (int z = 4; z < 7; z++)
        {
            for (int x = 0; x < width; x++)
            {
                stageMap[x, z] = 1;
            }
        }

        // 6. ステージ生成（Instantiate）
        BuildStage(width, depth);
    }

    /// <summary>
    /// スタートからゴールまで一本の道を確保する（この道の上には穴を空けない）
    /// </summary>
    void CreateGuaranteedPath(int width, int depth)
    {
        int oX = width / 2; // スタートX位置
        int oZ = zMin;
        for (int z = 0; z < depth - 2; z++)
        {
            
            // 現在地を保護(9)に設定
            stageMap[oX, z] = 9;

            // 次のZに進む前に、確率で横に少し寄り道させる（道をうねらせる）
            if (Random.value < 0.5f)
            {
                int direction = Random.value < 0.5f ? -1 : 1; // 左か右か
                int nextX = Mathf.Clamp(oX + direction, 0, width - 1);
                
                // 横移動した先も保護
                stageMap[nextX, z] = 9;
                oX = nextX;
            }
            // Zはループで自然に進む
        }
        
    }

    /// <summary>
    /// ランダムウォークとラインカットで穴を空ける
    /// </summary>
    void CarveComplexHoles(int width, int depth)
    {
        // ステージの密度調整（値が大きいほど穴だらけになる）
        int holeAttempts = (int)(depth * 1.5f); 

        for (int i = 0; i < holeAttempts; i++)
        {
            // 穴を掘り始める起点をランダムに決定
            int startX = Random.Range(0, width);
            int startZ = Random.Range(2, depth - 2); // スタート直後とゴール直前は除外

            // 保護された道(9)の上ならスキップ
            if (stageMap[startX, startZ] == 9) continue;

            // どちらのパターンの穴にするかランダム決定
            if (Random.value < 0.7f)
            {
                // パターンA: ランダムウォーク穴（不規則な形）
                CarveRandomWalkHole(startX, startZ, width, depth);
            }
            else
            {
                // パターンB: 横一直線の穴（行き止まりを作りやすい）
                CarveLineHole(startX, startZ, width, depth);
            }
        }
    }

    // 不規則な形の穴を掘る
    void CarveRandomWalkHole(int x, int z, int width, int depth)
    {
        int steps = Random.Range(3, 8); // 穴の大きさ
        int cx = x;
        int cz = z;

        for (int s = 0; s < steps; s++)
        {
            // 範囲外チェック
            if (cx < 0 || cx >= width || cz < 0 || cz >= depth) break;
            
            // 保護エリア(9)やゴール(2)でなければ穴(0)にする
            if (stageMap[cx, cz] != 9 && stageMap[cx, cz] != 2)
            {
                stageMap[cx, cz] = 0;
            }

            // 次の掘削ポイントへランダム移動 (上下左右)
            int dir = Random.Range(0, 4);
            if (dir == 0) cx++;
            else if (dir == 1) cx--;
            else if (dir == 2) cz++;
            else if (dir == 3) cz--;
        }
    }

    // 横長の穴を掘る（道を分断して行き止まりを作る役割）
    void CarveLineHole(int x, int z, int width, int depth)
    {
        int length = Random.Range(2, 5);
        // 右方向に掘る
        for (int k = 0; k < length; k++)
        {
            int cx = x + k;
            if (cx >= width) break;
            
            // 保護エリアにぶつかったらそこで止める（道を消さないため）
            if (stageMap[cx, z] == 9) break; 
            if (stageMap[cx, z] == 2) continue;

            stageMap[cx, z] = 0;
        }
    }

    void BuildStage(int width, int depth)
    {
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                // 1(通常ブロック) または 9(保護ブロック) なら生成
                if (stageMap[x, z] == 1 || stageMap[x, z] == 9)
                {
                    // 座標変換：配列インデックス -> ワールド座標
                    Vector3 pos = origin + new Vector3(x + xMin, 0, z + zMin);
                    Instantiate(blockPrefab, pos, Quaternion.identity, transform);
                }
            }
        }
    }

    /// <summary>
    /// ステージ状態を取得（1=ブロックあり, 0=穴）
    /// </summary>
    public int GetTileState(int x, int z)
    {
        int xi = x - xMin;
        int zi = z - zMin;
        if (xi < 0 || zi < 0 || xi >= stageMap.GetLength(0) || zi >= stageMap.GetLength(1))
            return 0; // 範囲外
        return stageMap[xi, zi];
    }
}
