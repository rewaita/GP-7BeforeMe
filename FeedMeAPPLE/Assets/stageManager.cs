using UnityEngine;
using System.Collections.Generic;
using System;
using System.Collections;
//using System.IO.Hashing;

public class StageManager : MonoBehaviour
{
    public GameObject blockPrefab; //cube
    public GameObject goalBlock;
    public GameObject tekiBlock;

    public Material floor1;
    public Material floor2;
    public Material floor3;
    public Material floor4;
    public Material floor5;
    public Material floor6;

    private int xMin = -7;
    private int xMax = 7;
    private int zMin = -5;
    private int zMax = 25;
    private Vector3 origin = new Vector3(0, 0, 0); // 基準位置

    // ステージ（1=ステージあり, 0=穴, 2=ゴール, 3=移動罠）
    private int[,] stageMap;
    private bool isStageGenerated = false;
    private GameObject[,,] copiedBlocks;
    private List<GameObject> copiedStage = new List<GameObject>();

    // 鏡ステージ用データ構造
    private int[,] mStageMap;
    private GameObject[,] mCopiedBlocks;
    private List<GameObject> mCopiedStage = new List<GameObject>();
    
    // 鏡ステージ用の複製ゴールブロック（削除用）
    private GameObject mirroredGoalBlock = null;

    public void OnStartButton()
    {
        if (isStageGenerated) return;
        GenerateStage();
        isStageGenerated = true;
    }
    public void OnRestartButton()
    {
        // 既存のステージ（ブロック）を削除
        NextStage();//ステージ削除

        GenerateStage();
        isStageGenerated = true;
    }

    private void NextStage()
    {
        int width = xMax - xMin + 1;
        int depth = zMax - zMin + 1;

        copiedStage.ForEach(block => Destroy(block));
        copiedStage.Clear();

        // 既存のブロックを削除
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int n = 0; n < 7; n++)
                {
                    if (copiedBlocks[x, z, n] != null)
                    {
                        Destroy(copiedBlocks[x, z,n]);
                        copiedBlocks[x, z, n] = null;
                    }
                }
            }
        }
        
        // 鏡ステージのブロックも削除
        mCopiedStage.ForEach(block => Destroy(block));
        mCopiedStage.Clear();
        
        // 鏡ゴールブロックも削除
        if (mirroredGoalBlock != null)
        {
            Destroy(mirroredGoalBlock);
            mirroredGoalBlock = null;
        }

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                if (mCopiedBlocks != null && mCopiedBlocks[x, z] != null)
                {
                    Destroy(mCopiedBlocks[x, z]);
                    mCopiedBlocks[x, z] = null;
                }
            }
        }
        stageMap = null;
        mStageMap = null;
        isStageGenerated = false;
    }

    void GenerateStage()
    {
        int width = xMax - xMin + 1;
        int depth = zMax - zMin + 1;

        stageMap = new int[width, depth];
        copiedBlocks = new GameObject[width, depth,7];

        mStageMap = new int[width, depth];
        mCopiedBlocks = new GameObject[width, depth];
        
        //mStageを全て0
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                mStageMap[x, z] = 0;
            }
        }
        
        //全て1
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                stageMap[x, z] = 1;
            }
        }

        //ゴール地点の設定 (手前2列)
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

        //正解ルート(9)
        CreateGuaranteedPath(width, depth);
        CreateGuaranteedPath(width, depth);// 2回呼び出して道を複雑化

        //穴作成
        CarveComplexHoles(width, depth);

        //スタート地点の安全地帯
        for (int z = 4; z < 7; z++)
        {
            for (int x = 0; x < width; x++)
            {
                stageMap[x, z] = 1;
            }
        }

        //罠生成(3)
        GenerateEnvironment3(width, depth); 

        //ステージ構築
        //BuildStage(width, depth);

        // 8. 鏡ステージのゴールブロック配置（x方向にステージ幅分ずらす）
        // AI用ステージ移動時に削除するため、参照を保持
        if (goalBlock != null)
        {
            // 既存の鏡ゴールがあれば削除
            if (mirroredGoalBlock != null)
            {
                Destroy(mirroredGoalBlock);
            }
            
            mirroredGoalBlock = Instantiate(goalBlock, origin + new Vector3(width, 0, zMax), Quaternion.identity, transform);
            mirroredGoalBlock.transform.localScale = new Vector3(width, 1, 3);
        }
    }

    /// <summary>
    /// スタートからゴールまで一本の道を確保する（この道の上には穴を空けない）
    /// </summary>
    void CreateGuaranteedPath(int width, int depth)
    {
        int oX = width / 2;
        int oZ = zMin;
        for (int z = 0; z < depth - 2; z++)
        {
            stageMap[oX, z] = 9;

            // 次のZに進む前に、確率で横に少し寄り道させる（道をうねらせる）
            if (UnityEngine.Random.value < 0.5f)
            {
                int direction = UnityEngine.Random.value < 0.5f ? -1 : 1; // 左か右か
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
        int holeAttempts = (int)(depth * 2.0f); 

        for (int i = 0; i < holeAttempts; i++)
        {
            // 穴を掘り始める起点をランダムに決定
            int startX = UnityEngine.Random.Range(0, width);
            int startZ = UnityEngine.Random.Range(2, depth - 2); // スタート直後とゴール直前は除外

            // 保護された道(9)の上ならスキップ
            if (stageMap[startX, startZ] == 9) continue;

            // どちらのパターンの穴にするかランダム決定
            if (UnityEngine.Random.value < 0.7f)
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
        int steps = UnityEngine.Random.Range(3, 8); // 穴の大きさ
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
            int dir = UnityEngine.Random.Range(0, 4);
            if (dir == 0) cx++;
            else if (dir == 1) cx--;
            else if (dir == 2) cz++;
            else if (dir == 3) cz--;
        }
    }

    // 横長の穴を掘る（道を分断して行き止まりを作る役割）
    void CarveLineHole(int x, int z, int width, int depth)
    {
        int length = UnityEngine.Random.Range(2, 5);
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

    void GenerateEnvironment3(int width, int depth)
    {
        int numBlocks = 0;
        
        // 中心マスからの相対座標（合計5マス）
        // 中心(0,0), 上(0,1), 下(0,-1), 左(-1,0), 右(1,0)
        (int dx, int dz)[] offsets = new (int, int)[]
        {
            (0, 0), (0, 1), (0, -1), (-1, 0), (1, 0)
        };

        while(numBlocks < 6)
        {
            // 敵を生成する起点をランダムに決定 (スタート直後とゴール直前は除外)
            int startX = UnityEngine.Random.Range(0, width);
            int startZ = UnityEngine.Random.Range(7, depth - 2); 

            // 5マス全てが安全に配置可能かチェック
            bool canPlace = true;
            List<(int x, int z)> positionsToSet = new List<(int, int)>();

            foreach (var offset in offsets)
            {
                int cx = startX + offset.dx;
                int cz = startZ + offset.dz;

                // 範囲外チェック
                if (cx < 0 || cx >= width || cz < 0 || cz >= depth)
                {
                    canPlace = false;
                    break;
                }

                // 既存の優先環境（0=穴, 2=ゴール, 9=安全な道）との重複チェック
                if (stageMap[cx, cz] == 0 || stageMap[cx, cz] == 2 || stageMap[cx, cz] == 9)
                {
                    canPlace = false;
                    break;
                }
                
                // 既に別の環境3のブロックが置かれていても上書きは許容する
                // if (stageMap[cx, cz] == environmentCode) continue;

                positionsToSet.Add((cx, cz));
            }

            // 配置可能であれば、stageMapに3を設定
            if (canPlace)
            {
                foreach (var pos in positionsToSet)
                {
                    // 1(通常のブロック)または既存の3を上書きする
                    stageMap[pos.x, pos.z] = 3; 
                }
                numBlocks++;
            }
        }
    }

    void BuildStage(int width, int depth)
    {
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                GameObject blockToInstantiate = null;
                Material blockMaterial = null;

                // 1(通常ブロック) または 9(保護ブロック) なら blockPrefab を使用
                if (stageMap[x, z] == 1 || stageMap[x, z] == 9)
                {
                    blockToInstantiate = blockPrefab;
                    blockMaterial = floor1;
                }
                // ★追加: 3(敵ブロック) なら tekiBlock を使用
                else if (stageMap[x, z] == 3) 
                {
                    if(tekiBlock == null)
                    {
                        Debug.LogError("tekiBlock が設定されていません！");
                        continue;
                    }
                    blockToInstantiate = tekiBlock;
                }

                if (blockToInstantiate != null)
                {
                    // 座標変換：配列インデックス -> ワールド座標
                    Vector3 pos = origin + new Vector3(x + xMin, -20, z + zMin); // Zを下げて生成
                    GameObject bblock = Instantiate(blockToInstantiate, pos, Quaternion.identity, transform);
                    
                    // blockPrefab の場合のみ Material を設定
                    if (blockMaterial != null)
                    {
                        // 敵ブロックにはRendererがない可能性もあるため、チェック
                        Renderer renderer = bblock.GetComponent<Renderer>();
                        if (renderer != null)
                        {
                            renderer.sharedMaterial = blockMaterial;
                        }
                    }

                    //コピーしたブロックを配列で保存
                    copiedStage.Add(bblock);
                }
            }
        }
    }

    void BuildStageByState(int xi, int zi)
    {
        for (int n = 0; n < 7; n++)
        {
            if (copiedBlocks[xi, zi, n] != null)
            {
                return; 
            }
        }

        int width = xMax - xMin + 1; // ステージの幅

        // 1(通常ブロック) または 9(保護ブロック) なら blockPrefab を使用
        if (stageMap[xi, zi] == 1 || stageMap[xi, zi] == 9)
        {
            // 座標変換：配列インデックス -> ワールド座標
            Vector3 pos = origin + new Vector3(xi + xMin, 0, zi + zMin);
            GameObject block = Instantiate(blockPrefab, pos, Quaternion.identity, transform);
            block.GetComponent<Renderer>().material = new Material(floor1);
            //コピーしたブロックを配列で保存
            copiedBlocks[xi, zi, 0] = block;

            int mXi = (xMax - xMin) - xi;  // X軸対称
            mStageMap[mXi, zi] = stageMap[xi, zi];
            int shiftedX = mXi + xMin + width;  // x方向にステージ幅分ずらす
            Vector3 mPos = origin + new Vector3(shiftedX, 0, zi + zMin);
            GameObject mBlock = Instantiate(blockPrefab, mPos, Quaternion.identity, transform);
            mBlock.GetComponent<Renderer>().material = new Material(floor1);
            mCopiedBlocks[mXi, zi] = mBlock;
            mCopiedStage.Add(mBlock);
        }
        // 3(敵ブロック) なら tekiBlock を使用
        else if (stageMap[xi, zi] == 3)
        {
            if (tekiBlock == null) return;
            Vector3 pos = origin + new Vector3(xi + xMin, 0, zi + zMin);
            GameObject block = Instantiate(tekiBlock, pos, Quaternion.identity, transform);
            copiedBlocks[xi, zi, 0] = block;

            // 鏡ステージにも複製（x方向にステージ幅分ずらす）
            int mXi = (xMax - xMin) - xi;  // X軸対称
            mStageMap[mXi, zi] = stageMap[xi, zi];
            int shiftedX = mXi + xMin + width;  // x方向にステージ幅分ずらす
            Vector3 mPos = origin + new Vector3(shiftedX, 0, zi + zMin);
            GameObject mBlock = Instantiate(tekiBlock, mPos, Quaternion.identity, transform);
            mCopiedBlocks[mXi, zi] = mBlock;
            mCopiedStage.Add(mBlock);
        }
    }
    /// <summary>
    /// ステージ状態を取得（1=ブロックあり, 0=穴）
    /// ステージ移動後は単純な座標変換のみ
    /// </summary>
    public int GetTileState(int x, int z)
    {
        int xi = x - xMin;
        int zi = z - zMin;
        
        if (xi < 0 || zi < 0 || xi >= stageMap.GetLength(0) || zi >= stageMap.GetLength(1))
            return 0; // 範囲外
        
        // ステージを動的に構築
        BuildStageByState(xi, zi);
        return stageMap[xi, zi];
    }

    public int GetMTileState(int x, int z)
    {
        int xi = x - xMin;
        int zi = z - zMin;
        
        if (xi < 0 || zi < 0 || xi >= mStageMap.GetLength(0) || zi >= mStageMap.GetLength(1))
            return 0; // 範囲外
        
        return mStageMap[xi, zi];
    }
    
    public void MatsChange(Vector3 targetPos)
    {
        int x = Mathf.RoundToInt(targetPos.x);
        int z = Mathf.RoundToInt(targetPos.z);
        int xi = x - xMin;
        int zi = z - zMin;
        if (xi < 0 || zi < 0 || xi >= stageMap.GetLength(0) || zi >= stageMap.GetLength(1))
            return; // 範囲外

        if (stageMap[xi, zi] == 0 || stageMap[xi, zi] == 2 || stageMap[xi, zi] == 3)
            return; // 穴の場合は処理しない
        
        
        // 現在の n を探索
        int currentN = -1;
        for (int n = 0; n <= 6; n++)
        {
            if (copiedBlocks[xi, zi, n] != null)
            {
                currentN = n;
                break;
            }
        }
        if (currentN == -1)
        {
            return; // ブロックが見つからない場合は終了
        }

        // 現在のブロックを取得
        GameObject block = copiedBlocks[xi, zi, currentN];
        Material blockMat = block.GetComponent<Renderer>().material;

        if (block != null)
        {
            //Debug.Log($"Changing material at ({xi},{zi}) with n={currentN}");
            // n に応じてマテリアルを変更
            switch (currentN)
            {
                case 0:
                    blockMat = floor1;
                    break;
                case 1:
                    blockMat = floor1;
                    break;
                case 2:
                    blockMat = floor2;
                    break;
                case 3:
                    blockMat = floor3;
                    break;
                case 4:
                    blockMat = floor4;
                    break;
                case 5:
                    blockMat = floor5;
                    break;
                case 6:
                    blockMat = floor6;
                    break;
                default:
                    Debug.LogWarning($"n の値が範囲外です: {currentN}");
                    break;
            }
            block.GetComponent<Renderer>().material = blockMat;

            // n をインクリメントして次のスロットに移動
            if (currentN < 6)
            {
                copiedBlocks[xi, zi, currentN + 1] = block;
                copiedBlocks[xi, zi, currentN] = null;
            }
        }
    }

    /// <summary>
    /// AI学習用：ステージ全体をx負方向に移動し、鏡ステージを元の位置に配置
    /// TrainButton押下時に呼び出される
    /// </summary>
    public IEnumerator MoveStageForAI()
    {
        int width = xMax - xMin + 1;
        float moveDistance = -width;
        
        Debug.Log($"=== AI用ステージ移動開始 ===");
        Debug.Log($"移動距離: x方向 {moveDistance}");
        
        // 1. 鏡ステージ用のゴールブロックを削除（元のゴールを使い回す）
        if (mirroredGoalBlock != null)
        {
            Destroy(mirroredGoalBlock);
            mirroredGoalBlock = null;
            Debug.Log("鏡ゴールブロックを削除");
        }
        
        // 2. アニメーション付きでステージ全体を移動
        Vector3 startPos = transform.position;
        Vector3 targetPos = startPos + new Vector3(moveDistance, 0, 0);
        float duration = 10.0f; // 移動時間（秒）
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            // イージング（スムーズな加減速）
            float smoothT = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(startPos, targetPos, smoothT);
            yield return null;
        }
        
        transform.position = targetPos;
        
        // 3. originも更新（座標計算用）
        origin += new Vector3(moveDistance, 0, 0);
        
        Debug.Log($"=== AI用ステージ移動完了 ===");
        Debug.Log($"新しいorigin: {origin}");
    }
}