// Terrain 試作 (VER-ENV-001) の設定。数値の正本はここ。計画: docs/plans/2026-09-19-terrain-prototype.md §2
// 値を変えたら Build Stage A から再実行する。
using UnityEngine;

public static class KH_TerrainPrototypeConfig
{
    // --- 出力先 ---
    public const string TerrainDataPath = "Assets/KominkaHillside/Data/Terrain/KH_Hill_TerrainData.asset";
    public const string ProtoMaterialDir = "Assets/KominkaHillside/Materials/Prototype";
    public const string SourceTerrainDataPath = "Assets/KominkaHillside/Data/Environment/Autumn/garden_Autumn.asset";
    public const string AutumnMaterialDir = "Assets/KominkaHillside/Materials/Environment/Autumn";
    public const string TreePrefabPath = "Assets/LMArt-StylizedEnvironmentKit/BroadleafTreeField/Prefabs/Trees/BroadleafTreeB01.prefab";
    public const string ViewSetPrefabPath = "Assets/LMArt-StylizedEnvironmentKit/BroadleafTreeField/Prefabs/Natures/ViewSet01.prefab";
    // モサンゴ屋 (パスに日本語を含むので GUID で引く。古民家ドッグランと同じ版)
    public const string KominkaBuildingPrefabGuid = "fba38e18b5367d641bcb47d8dd11546c"; // 建物.prefab
    public const string KominkaWallPrefabGuid = "aab800e1fb5171f43a97ca3f76ab134a";     // 外壁.prefab
    public static readonly string[] KominkaInteriorChildren = { "ふすま", "タンス.001", "タンス.002", "寝室タンス", "天井", "書院", "書院タンス", "床の間" };
    public const string KominkaEngawaFloorChild = "縁側床"; // 縁側の向きを決める基準

    // --- Terrain 寸法 ---
    public static readonly Vector3 TerrainSize = new Vector3(120f, 50f, 180f);
    public const int HeightmapResolution = 129;
    public const int AlphamapResolution = 256;
    public const int DetailResolution = 256;
    public const int DetailResolutionPerPatch = 16;
    public const float DetailObjectDistance = 40f;
    public const float DetailObjectDensity = 1f;
    public const float HeightmapPixelError = 5f;

    // --- 高さ (m)。z 方向の帯 ---
    public const float RidgeZ0 = 0f, RidgeZ1 = 15f;        // 南の尾根: z0 で RidgeTop → z1 で ValleyH
    public const float RidgeTop = 8f;
    public const float ValleyH = 1.5f;                     // 谷底 (z RidgeZ1〜SlopeZ0)
    public const float SlopeZ0 = 50f, SlopeZ1 = 76f;       // 谷側の斜面: ValleyH+0.5 → HillBase
    public const float HillBase = 12f;                     // 丘の南端の高さ
    public const float HillRise = 9f;                      // 丘の北端 (BankZ0) までの上昇
    public const float BankZ0 = 160f, BankZ1 = 180f;       // 北の土手
    public const float BankHeight = 10f;                   // 土手の盛り上がり (北・東・西)。尾根として縁の先を隠す
    public const float BankX0 = 15f, BankX1 = 105f;        // 東西の土手はこの外側
    public const float ValleyNoise = 0.3f;

    // ガウス山: x, z, 振幅, σ
    public static readonly float[,] Bumps = {
        { 98f, 80f, 4f, 8f },    // 展望の高台 (柵際。ここから古民家を見下ろす)
        { 35f, 150f, 3f, 15f },  // 北西の起伏 (道の外)
        { 95f, 150f, 3f, 15f },  // 北東の起伏 (道の外)
    };

    // 平坦部: x, z, 高さ, 半径, 移行帯
    public static readonly float[,] Flats = {
        { 98f, 80f, 17f, 3f, 6f },    // 展望 (柵際。小さな平坦部: 目線 1.6 m の俯角を足元で切らない)
        { 55f, 140f, 19.5f, 10f, 8f }, // 広場
    };
    public const int ViewpointFlat = 0, PlazaFlat = 1;

    // 散歩道の制御点: x, z, 高さ (閉曲線。順に辿る)
    public static readonly float[,] Path = {
        { 30f, 82f, 12.5f },   // 入口
        { 60f, 86f, 13f },
        { 98f, 80f, 17f },     // 展望 (Flats[0] と同じ高さ)
        { 85f, 125f, 18f },
        { 55f, 140f, 19.5f },  // 広場 (Flats[1] と同じ高さ)
        { 30f, 115f, 16f },
    };
    public const float PathHalfWidth = 1.5f;   // 道幅 3 m
    public const float PathShoulder = 2f;      // 路肩 (左右各)
    public const float PathBlend = 6f;         // 路肩の外の移行帯
    public const float PathSampleStep = 1f;
    public const float PathMaxSlopeDeg = 10f;
    public const float PathMaxCrossSlopeDeg = 5f;
    public const float CatmullRomAlpha = 0.5f; // centripetal

    // --- 歩ける範囲 (接地対象の範囲検査) ---
    public const float WalkX0 = 15f, WalkX1 = 105f, WalkZ0 = 76f, WalkZ1 = 165f;

    // --- 境界 (透明壁の歩行側の面) ---
    public const float BoundSouthZ = 76f, BoundNorthZ = 168f, BoundWestX = 12f, BoundEastX = 108f;
    public const float BoundThickness = 0.5f;
    public const float BoundOverlap = 2f;          // 隅で重ねる長さ
    public const float BoundBelowGround = 1f;      // 底は区間の最低地面より下
    public const float BoundAboveSouth = 3f, BoundAboveOther = 4f; // 上端は最高地面より上
    public const float BoundSampleStep = 2f;
    public const string BoundLayerName = "Environment";

    // 柵の見た目 (谷側)
    public const float FencePostSpacing = 2f;
    public const float FencePostHeight = 1.2f;
    public const float FenceGapAtViewpointHalf = 3f;   // 展望の正面は柵の見た目を空ける (透明壁は連続)
    public const float PathFenceClearance = 0.5f;      // 道の縁と柵線の最小離隔 (展望は柵際なので小さい)

    // --- 場所 ---
    public static readonly Vector2 EntranceXZ = new Vector2(30f, 82f);
    public const float SpawnGroundOffset = 0.1f;
    public static readonly Vector2 GateOffsetFromSpawn = new Vector2(1.5f, -3f); // 門柱: 左右 ±x, z は南へ
    public static readonly Vector2 BenchOffsetFromViewpoint = new Vector2(0f, 2f); // 展望の北側

    public static readonly Vector3 KominkaPosition = new Vector3(60f, 1.5f, 28f); // 谷底の奥 (展望からの俯角を浅く)。縁側を北 (丘) へ向ける

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

    // --- Stage B: 木・遠景・草 ---
    // 実物の木 (x, z)。BroadleafTreeB01 は 1 本 約 2.4 万三角形・LOD 無しなので少数に絞る。最後の 1 本は木陰ダミーの隣
    public static readonly float[,] Trees = {
        { 20f, 90f },    // 入口の脇
        { 55f, 72f },    // 谷側の斜面 (展望から左手に見える)
        { 106f, 108f },  // 展望の背後 (画面右を額縁に)
        { 70f, 118f },   // 道の内側
        { 46f, 148f },   // 広場の木陰
    };
    // 板の木 (LMArt Board_Tree*。2 三角形の板を十字に 2 枚)。土手の木立用: x, z
    public static readonly float[,] BoardTrees = {
        { 10f, 95f }, { 8f, 120f }, { 10f, 145f }, { 25f, 172f }, { 50f, 174f }, { 75f, 172f }, { 100f, 174f },
        { 112f, 150f }, { 114f, 125f }, { 112f, 95f }, { 40f, 62f }, { 80f, 64f },
    };
    public static readonly string[] BoardTreePrefabs = {
        "Assets/LMArt-StylizedEnvironmentKit/BroadleafTreeField/Prefabs/Natures/Board_TreeA01.prefab",
        "Assets/LMArt-StylizedEnvironmentKit/BroadleafTreeField/Prefabs/Natures/Board_TreeB01.prefab",
        "Assets/LMArt-StylizedEnvironmentKit/BroadleafTreeField/Prefabs/Natures/Board_TreeC01.prefab",
    };
    public const string BoardTreeMaterialName = "Mat_BoardTrees01"; // → Mat_BoardTrees01_Autumn
    public const int TreeSeed = 20260919;
    public const float TreeScaleMin = 0.9f, TreeScaleMax = 1.2f;
    public const string TreeLeafMaterialName = "Mat_BroadLeaves02"; // → Mat_BroadLeaves02_Autumn
    public static readonly Vector3 ViewSetPosition = new Vector3(60f, 0f, 90f); // Terrain 中心に 1 個 (直径 561 m のリング)
    public static readonly string[] ViewSetMaterialNames = { "Mat_BroadPlants01", "Mat_BroadGround02", "Mat_Mountains01", "Mat_BoardTrees01" };

    // 草: 各枠の被覆率 (CoverageMode: 0〜255。古民家の庭は平均 120〜150) と seed。道 (中心から PathHalfWidth+0.5) と広場中心 (r=6) は 0
    public static readonly int[] DetailDensity = { 90, 90, 40, 30, 40 };
    public const int DetailSeed = 7;
    public const float DetailNoiseScale = 0.08f;
    public const float DetailClearAroundPath = 0.5f;
    public const float DetailClearPlazaRadius = 6f;

    // Layer 規則
    public const float RockySlopeDeg = 30f;
    public const float RockyNoiseThreshold = 0.62f;
    public const float LayerNoiseScale = 0.05f;
}
