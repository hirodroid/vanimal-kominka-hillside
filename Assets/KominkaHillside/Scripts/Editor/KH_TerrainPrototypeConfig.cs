// Terrain 試作 (VER-ENV-001) の設定。数値の正本はここ。計画: docs/plans/2026-09-19-terrain-prototype.md
// 2026-09-19 夜のユーザー判断 (手作業の試作を把握して再パラメータ化):
//   古民家は谷底の南にあり塀で囲まれている。プレイヤーは古民家の門から出て (スポーン)、獣道を西の斜面へ登り、
//   丘を周回 (広場で遊び、展望で古民家を見返す)、東の斜面を下って門の前へ戻る。門は塞いで古民家側へは戻れない。
//   道は綺麗でなくてよい (獣道)。木は多め。広さは現状 (120×180) でよい。
// 値を変えたら Build Stage A から再実行する。
using UnityEngine;

public static class KH_TerrainPrototypeConfig
{
    // --- 出力先 / 入力 ---
    public const string TerrainDataPath = "Assets/KominkaHillside/Data/Terrain/KH_Hill_TerrainData.asset";
    public const string ProtoMaterialDir = "Assets/KominkaHillside/Materials/Prototype";
    public const string SourceTerrainDataPath = "Assets/KominkaHillside/Data/Environment/Autumn/garden_Autumn.asset";
    public const string AutumnMaterialDir = "Assets/KominkaHillside/Materials/Environment/Autumn";
    public const string TreePrefabPath = "Assets/LMArt-StylizedEnvironmentKit/BroadleafTreeField/Prefabs/Trees/BroadleafTreeB01.prefab";
    public static readonly string[] ShrubPrefabPaths = {
        "Assets/LMArt-StylizedEnvironmentKit/BroadleafTreeField/Prefabs/Trees/ShrubTreeA01.prefab",
        "Assets/LMArt-StylizedEnvironmentKit/BroadleafTreeField/Prefabs/Trees/ShrubTreeA02.prefab",
        "Assets/LMArt-StylizedEnvironmentKit/BroadleafTreeField/Prefabs/Trees/ShrubTreeA03.prefab",
    };
    public const string ViewSetPrefabPath = "Assets/LMArt-StylizedEnvironmentKit/BroadleafTreeField/Prefabs/Natures/ViewSet01.prefab";
    // モサンゴ屋 (パスに日本語を含むので GUID で引く。古民家ドッグランと同じ版)
    public const string KominkaBuildingPrefabGuid = "fba38e18b5367d641bcb47d8dd11546c"; // 建物.prefab
    public const string KominkaWallPrefabGuid = "aab800e1fb5171f43a97ca3f76ab134a";     // 外壁.prefab
    public static readonly string[] KominkaInteriorChildren = { "ふすま", "タンス.001", "タンス.002", "寝室タンス", "天井", "書院", "書院タンス", "床の間" };
    public const string KominkaGateFrameChild = "門枠";   // 外壁.prefab の門 (スポーンの基準)
    public const string KominkaGenkanChild = "床/玄関床"; // 建物.prefab の玄関 (門の外向きを決める基準)
    public const string KominkaWallMeshChild = "外壁";    // MeshCollider を付ける塀

    // --- Terrain 寸法 ---
    public static readonly Vector3 TerrainSize = new Vector3(120f, 50f, 180f);
    public const int HeightmapResolution = 129;
    public const int AlphamapResolution = 256;
    public const int DetailResolution = 256;
    public const int DetailResolutionPerPatch = 16;
    public const float DetailObjectDistance = 40f;
    public const float DetailObjectDensity = 1f;
    public const float HeightmapPixelError = 5f;

    // --- 高さ (m)。z 方向の帯 (南 → 北) ---
    public const float RidgeZ0 = 0f, RidgeZ1 = 10f;        // 南の低い土手: z0 で RidgeTop → z1 で ValleyH (縁の先を隠す)
    public const float RidgeTop = 6f;
    public const float ValleyH = 1.5f;                     // 谷底 (古民家と門の前。z RidgeZ1〜SlopeZ0)
    public const float ValleyNoise = 0.3f;
    public const float SlopeZ0 = 44f, SlopeZ1 = 84f;       // 斜面 (線形): ValleyH+0.5 → HillBase。40 m で 10 m = 約 14°
    public const float HillBase = 12f;                     // 丘の南端の高さ
    public const float HillRise = 9f;                      // 丘の北端 (BankZ0) までの上昇
    public const float BankZ0 = 160f, BankZ1 = 180f;       // 北の土手
    public const float BankHeight = 10f;                   // 土手の盛り上がり (北・東・西)。尾根として縁の先を隠す
    public const float BankX0 = 15f, BankX1 = 105f;        // 東西の土手はこの外側
    public const float BankFromZ = 10f;                    // 東西の土手はこの z から北 (谷底も囲む)

    // ガウス山: x, z, 振幅, σ (道の外)
    public static readonly float[,] Bumps = {
        { 35f, 150f, 3f, 15f },  // 北西の起伏
        { 95f, 150f, 3f, 15f },  // 北東の起伏
        { 65f, 105f, 2.5f, 14f }, // 丘の中央 (道の内側)
    };

    // 平坦部: x, z, 高さ, 半径, 移行帯
    public static readonly float[,] Flats = {
        { 98f, 88f, 12.5f, 3f, 6f },  // 展望 (丘の南縁。小さな平坦部: 目線 1.6 m の俯角を足元で切らない)
        { 55f, 140f, 19.5f, 10f, 8f }, // 広場
    };
    public const int ViewpointFlat = 0, PlazaFlat = 1;

    // 散歩道の制御点: x, z, 高さ (閉曲線。門の前から西の斜面を登り、丘を周回し、東の斜面を下って門の前へ)
    public static readonly float[,] Path = {
        { 47f, 41f, 1.5f },    // 門の前 (スポーンの脇)
        { 38f, 47f, 2.5f },
        { 26f, 58f, 5.5f },    // 西の斜面を斜めに登る
        { 22f, 76f, 10f },
        { 26f, 96f, 13f },     // 丘の西側
        { 40f, 118f, 16f },
        { 55f, 140f, 19.5f },  // 広場 (Flats[1] と同じ高さ)
        { 80f, 130f, 18f },
        { 98f, 88f, 12.5f },   // 展望 (Flats[0] と同じ高さ)。ここから古民家を見返す
        { 100f, 72f, 9f },     // 東の斜面を下る
        { 98f, 56f, 5f },
        { 90f, 46f, 2.5f },
        { 75f, 42f, 1.5f },    // 谷底を西へ戻る
    };
    public const float PathHalfWidth = 1.0f;   // 獣道: 幅 2 m
    public const float PathEdgeNoise = 0.8f;   // 縁を揺らす (m)
    public const float PathShoulder = 2f;      // 路肩 (左右各)
    public const float PathBlend = 6f;         // 路肩の外の移行帯
    public const float PathSampleStep = 1f;
    public const float PathMaxSlopeDeg = 15f;  // 獣道なので 10° → 15° に緩和 (2026-09-19 ユーザーは 23° の直登も許容)
    public const float PathMaxCrossSlopeDeg = 5f;
    public const float CatmullRomAlpha = 0.5f; // centripetal

    // --- 歩ける範囲 (接地対象の範囲検査) ---
    public const float WalkX0 = 15f, WalkX1 = 105f, WalkZ0 = 12f, WalkZ1 = 165f;

    // --- 境界 (透明壁の歩行側の面) ---
    public const float BoundSouthZ = 12f, BoundNorthZ = 168f, BoundWestX = 12f, BoundEastX = 108f;
    public const float BoundThickness = 0.5f;
    public const float BoundOverlap = 2f;          // 隅で重ねる長さ
    public const float BoundBelowGround = 1f;      // 底は区間の最低地面より下
    public const float BoundAboveSouth = 4f, BoundAboveOther = 4f; // 上端は最高地面より上
    public const float BoundSampleStep = 2f;
    public const string BoundLayerName = "Environment";

    // --- 場所 ---
    public const float SpawnDistanceFromGate = 2.5f;   // 門の外側へ
    public const float SpawnGroundOffset = 0.1f;
    public static readonly Vector3 GateBlockSize = new Vector3(4.5f, 3.5f, 0.6f); // 門の開口を塞ぐ透明 Collider (門枠の向き)
    public static readonly Vector2 BenchOffsetFromViewpoint = new Vector2(0f, 2f); // 展望の北側

    // 古民家: 位置と向き (2026-09-19 ユーザーが手で決めた値。y は地面に合わせる)
    public static readonly Vector3 KominkaPosition = new Vector3(55.34f, 1.5f, 29.82f);
    public const float KominkaYaw = 158.9f;

    // ダミー犬 (広場からのオフセット x, z) と色。最後の 1 個は木陰用 (Stage B で木を隣に植える)
    public static readonly float[,] DummyOffsets = { { -3f, -3f }, { 3f, -2f }, { 0f, 2f }, { -4f, 3f }, { 4f, 4f }, { -7f, 6f } };
    public static readonly string[] DummyNames = { "Corgi", "Pomeranian", "Labrador", "Dachshund", "Shiba", "Samoyed" };
    public static readonly Color[] DummyColors = {
        new Color(0.80f, 0.55f, 0.30f), // 茶白 (コーギー)
        new Color(0.95f, 0.85f, 0.65f), // クリーム (ポメ)
        new Color(0.90f, 0.80f, 0.55f), // 淡黄 (ラブ)
        new Color(0.40f, 0.25f, 0.15f), // 濃茶 (ダックス)
        new Color(0.75f, 0.40f, 0.20f), // 赤茶 (柴)
        new Color(0.97f, 0.97f, 0.95f), // 白 (サモエド)
    };
    public const float DummyRadius = 0.25f, DummyHeight = 0.5f;
    public static readonly Vector2 BallOffset = new Vector2(1.5f, -1f);
    public const float BallRadius = 0.1f;

    // --- Stage B: 木・低木・遠景・草 ---
    // 実物の広葉樹 (x, z)。BroadleafTreeB01 は 1 本 約 2.4 万三角形・LOD 無しなので少数。最後の 1 本は木陰ダミーの隣
    public static readonly float[,] Trees = {
        { 32f, 62f },    // 西の斜面、道の東脇 (登りの目印)
        { 16f, 100f },   // 丘の西縁
        { 106f, 100f },  // 展望の背後 (額縁)
        { 68f, 112f },   // 丘の中央
        { 46f, 148f },   // 広場の木陰
    };
    public const int TreeSeed = 20260919;
    public const float TreeScaleMin = 0.9f, TreeScaleMax = 1.2f;
    public const string TreeLeafMaterialName = "Mat_BroadLeaves02"; // → Mat_BroadLeaves02_Autumn
    // 低木 (ShrubTreeA*): 歩ける範囲に seed で散らす。道から ShrubClearPath m、平坦部・古民家から離す
    public const int ShrubCount = 16;
    public const int ShrubSeed = 4242;
    public const float ShrubClearPath = 4f, ShrubClearFlat = 6f, ShrubClearKominka = 6f;
    public const float ShrubScaleMin = 0.8f, ShrubScaleMax = 1.3f;
    // 板の木 (LMArt Board_Tree*。2 三角形の板を十字に 2 枚)。土手の木立用: x, z
    public static readonly float[,] BoardTrees = {
        { 10f, 20f }, { 8f, 45f }, { 10f, 70f }, { 8f, 95f }, { 10f, 120f }, { 8f, 145f },
        { 25f, 172f }, { 50f, 174f }, { 75f, 172f }, { 100f, 174f },
        { 112f, 150f }, { 114f, 125f }, { 112f, 100f }, { 114f, 75f }, { 112f, 50f }, { 114f, 25f },
        { 20f, 6f }, { 40f, 5f }, { 80f, 5f }, { 100f, 6f },
    };
    public static readonly string[] BoardTreePrefabs = {
        "Assets/LMArt-StylizedEnvironmentKit/BroadleafTreeField/Prefabs/Natures/Board_TreeA01.prefab",
        "Assets/LMArt-StylizedEnvironmentKit/BroadleafTreeField/Prefabs/Natures/Board_TreeB01.prefab",
        "Assets/LMArt-StylizedEnvironmentKit/BroadleafTreeField/Prefabs/Natures/Board_TreeC01.prefab",
    };
    public const string BoardTreeMaterialName = "Mat_BoardTrees01"; // → Mat_BoardTrees01_Autumn
    public static readonly Vector3 ViewSetPosition = new Vector3(60f, 0f, 90f); // Terrain 中心に 1 個 (直径 561 m のリング)
    public static readonly string[] ViewSetMaterialNames = { "Mat_BroadPlants01", "Mat_BroadGround02", "Mat_Mountains01", "Mat_BoardTrees01" };

    // 草: 各枠の被覆率 (CoverageMode: 0〜255。古民家の庭は平均 120〜150) と seed。道と広場中心は 0
    public static readonly int[] DetailDensity = { 90, 90, 40, 30, 40 };
    public const int DetailSeed = 7;
    public const float DetailNoiseScale = 0.08f;
    public const float DetailClearAroundPath = 0.5f;
    public const float DetailClearPlazaRadius = 6f;

    // Layer 規則
    public const float RockySlopeDeg = 30f;
    public const float RockyNoiseThreshold = 0.58f;   // 岩混じりの草の割合 (低いほど多い)
    public const float LayerNoiseScale = 0.05f;
}
