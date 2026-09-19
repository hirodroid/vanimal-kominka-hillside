// Terrain 試作 (VER-ENV-001) を設定値から生成・検証する Editor ツール。
// 計画: docs/plans/2026-09-19-terrain-prototype.md §3〜4。設定: KH_TerrainPrototypeConfig。
//
// Build Stage A (Ground): TerrainData (高さ / Layer / splat / Collider)・古民家・境界・スポーン・ダミー。KH_World を消して作り直す。
// Build Stage B (Dressing): 木・草 Detail・遠景・秋材質 override。Stage A の生成物が前提。
// Verify: 期待する状態を数えて Temp/KH_TerrainPrototype/verify.txt に書く。Build の戻り値は成功判定に使わない。
// Undo は対応しない (戻すのは git と再 Build)。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using C = KH_TerrainPrototypeConfig;

public static class KH_TerrainPrototypeEditor
{
    const string Menu = "Tools/KominkaHillside/Terrain Prototype/";
    const string RootName = "KH_World";
    const string LogDir = "Temp/KH_TerrainPrototype";

    // ------------------------------------------------------------------ Build Stage A
    [MenuItem(Menu + "Build Stage A (Ground)")]
    public static void BuildStageA()
    {
        var log = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.isDirty)
        {
            Fail(log, "scene is dirty. Save or discard first (Build does not support Undo).", "build-A.log");
            return;
        }

        // 既存の生成物と Floor を消す
        var oldRoot = GameObject.Find(RootName);
        if (oldRoot != null) { Object.DestroyImmediate(oldRoot); log.AppendLine("removed old " + RootName); }
        foreach (var go in scene.GetRootGameObjects()) if (go.name == "Floor") { Object.DestroyImmediate(go); log.AppendLine("removed Floor"); }

        var root = new GameObject(RootName);
        var places = NewChild(root, "KH_Places");

        // TerrainData
        var td = LoadOrCreateTerrainData(log);
        var heights = KH_TerrainShape.BuildHeights(log);
        td.SetHeights(0, 0, heights);
        td.terrainLayers = PickLayers(log);
        td.SetAlphamaps(0, 0, KH_TerrainShape.BuildAlphamaps(td, log));
        SetupDetailPrototypes(td, log);
        ClearDetails(td);

        var terrainGo = Terrain.CreateTerrainGameObject(td);
        terrainGo.name = "KH_Terrain";
        terrainGo.transform.SetParent(root.transform, false);
        terrainGo.transform.position = Vector3.zero;
        var terrain = terrainGo.GetComponent<Terrain>();
        terrain.detailObjectDistance = C.DetailObjectDistance;
        terrain.detailObjectDensity = C.DetailObjectDensity;
        terrain.heightmapPixelError = C.HeightmapPixelError;
        terrain.drawInstanced = false;
        log.AppendLine("terrain created size=" + td.size + " hm=" + td.heightmapResolution + " layers=" + td.terrainLayers.Length);

        // 場所
        var entrance = NewChild(places, "KH_Entrance");
        var spawnPos = GroundXZ(terrain, C.EntranceXZ, C.SpawnGroundOffset);
        entrance.transform.position = spawnPos;
        entrance.transform.rotation = Quaternion.identity; // +z = 北
        BuildGate(terrain, entrance, log);

        var viewpoint = NewChild(places, "KH_Viewpoint");
        viewpoint.transform.position = GroundXZ(terrain, new Vector2(C.Flats[C.ViewpointFlat, 0], C.Flats[C.ViewpointFlat, 1]), 0f);
        BuildBench(terrain, viewpoint);

        var plaza = NewChild(places, "KH_Plaza");
        plaza.transform.position = GroundXZ(terrain, new Vector2(C.Flats[C.PlazaFlat, 0], C.Flats[C.PlazaFlat, 1]), 0f);

        BuildKominka(terrain, places, log);
        BuildBounds(terrain, root, log);
        BuildFence(terrain, root, log);
        BuildDummies(terrain, root, log);
        WireDescriptor(entrance.transform, log);

        SaveAll(td, log);
        Finish(log, "build-A.log");
    }

    // ------------------------------------------------------------------ Build Stage B
    [MenuItem(Menu + "Build Stage B (Dressing)")]
    public static void BuildStageB()
    {
        var log = new StringBuilder();
        var root = GameObject.Find(RootName);
        var terrain = root != null ? root.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { Fail(log, "Stage A output not found. Run Build Stage A first.", "build-B.log"); return; }
        var td = terrain.terrainData;

        foreach (var name in new[] { "KH_Trees", "KH_BoardTrees", "KH_Scenery" })
        {
            var t = root.transform.Find(name);
            if (t != null) { Object.DestroyImmediate(t.gameObject); log.AppendLine("removed old " + name); }
        }

        var autumn = LoadAutumnMaterials();
        BuildTrees(terrain, root, autumn, log);
        BuildScenery(root, autumn, log);
        BuildDetails(td, log);

        SaveAll(td, log);
        Finish(log, "build-B.log");
    }

    // ------------------------------------------------------------------ helpers: assets
    static TerrainData LoadOrCreateTerrainData(StringBuilder log)
    {
        var td = AssetDatabase.LoadAssetAtPath<TerrainData>(C.TerrainDataPath);
        if (td == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(C.TerrainDataPath));
            td = new TerrainData();
            AssetDatabase.CreateAsset(td, C.TerrainDataPath);
            log.AppendLine("created TerrainData " + C.TerrainDataPath);
        }
        else log.AppendLine("reusing TerrainData " + C.TerrainDataPath + " (GUID kept)");
        td.heightmapResolution = C.HeightmapResolution;
        td.size = C.TerrainSize;
        td.alphamapResolution = C.AlphamapResolution;
        td.baseMapResolution = 512;
        td.SetDetailResolution(C.DetailResolution, C.DetailResolutionPerPatch);
        return td;
    }

    static TerrainLayer[] PickLayers(StringBuilder log)
    {
        var src = AssetDatabase.LoadAssetAtPath<TerrainData>(C.SourceTerrainDataPath);
        var want = new[] { "GrassHillAlbedo", "GrassRockyAlbedo", "SandAlbedo", "MudRockyAlbedoSpecular" }; // 順 = KH_TerrainShape.Layer*
        var result = new TerrainLayer[want.Length];
        for (int i = 0; i < want.Length; i++)
        {
            foreach (var l in src.terrainLayers)
                if (l != null && l.diffuseTexture != null && l.diffuseTexture.name == want[i] && result[i] == null) result[i] = l;
            if (result[i] == null) log.AppendLine("ERROR layer not found: " + want[i]);
            else log.AppendLine("layer " + i + " = " + result[i].name + " (" + want[i] + ")");
        }
        return result;
    }

    static void SetupDetailPrototypes(TerrainData td, StringBuilder log)
    {
        var src = AssetDatabase.LoadAssetAtPath<TerrainData>(C.SourceTerrainDataPath);
        var protos = new DetailPrototype[src.detailPrototypes.Length];
        for (int i = 0; i < protos.Length; i++) protos[i] = new DetailPrototype(src.detailPrototypes[i]);
        td.SetDetailScatterMode(src.detailScatterMode);
        td.detailPrototypes = protos;
        td.wavingGrassStrength = src.wavingGrassStrength;
        td.wavingGrassAmount = src.wavingGrassAmount;
        td.wavingGrassSpeed = src.wavingGrassSpeed;
        td.wavingGrassTint = src.wavingGrassTint;
        td.RefreshPrototypes();
        log.AppendLine("detail prototypes copied: " + protos.Length + " scatter=" + td.detailScatterMode);
    }

    static void ClearDetails(TerrainData td)
    {
        var zero = new int[td.detailHeight, td.detailWidth];
        for (int i = 0; i < td.detailPrototypes.Length; i++) td.SetDetailLayer(0, 0, i, zero);
    }

    static Dictionary<string, Material> LoadAutumnMaterials()
    {
        var d = new Dictionary<string, Material>();
        foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { C.AutumnMaterialDir }))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
            if (m != null && m.name.EndsWith("_Autumn")) d[m.name.Substring(0, m.name.Length - "_Autumn".Length)] = m;
        }
        return d;
    }

    public static Material ProtoMaterial(string name, Color color)
    {
        Directory.CreateDirectory(C.ProtoMaterialDir);
        var path = C.ProtoMaterialDir + "/KH_Proto_" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_Color", color);
        m.SetFloat("_Glossiness", 0.1f);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ helpers: scene objects
    static GameObject NewChild(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    public static Vector3 GroundXZ(Terrain terrain, Vector2 xz, float offset)
    {
        var p = new Vector3(xz.x, 0f, xz.y);
        return new Vector3(xz.x, terrain.SampleHeight(p) + terrain.transform.position.y + offset, xz.y);
    }

    static GameObject Cube(GameObject parent, string name, Vector3 pos, Vector3 size, Material mat, bool collider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.position = pos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    static void BuildGate(Terrain terrain, GameObject entrance, StringBuilder log)
    {
        var wood = ProtoMaterial("Wood", new Color(0.45f, 0.30f, 0.18f));
        var sp = entrance.transform.position;
        for (int s = -1; s <= 1; s += 2)
        {
            var xz = new Vector2(sp.x + s * C.GateOffsetFromSpawn.x, sp.z + C.GateOffsetFromSpawn.y);
            var g = GroundXZ(terrain, xz, 1.1f);
            Cube(entrance, "KH_GatePost" + (s < 0 ? "L" : "R"), g, new Vector3(0.25f, 2.2f, 0.25f), wood, true);
        }
        var top = GroundXZ(terrain, new Vector2(sp.x, sp.z + C.GateOffsetFromSpawn.y), 2.3f);
        Cube(entrance, "KH_GateTop", top, new Vector3(C.GateOffsetFromSpawn.x * 2f + 0.4f, 0.15f, 0.2f), wood, false);
        log.AppendLine("gate at spawn " + sp.ToString("0.00"));
    }

    static void BuildBench(Terrain terrain, GameObject viewpoint)
    {
        var wood = ProtoMaterial("Wood", new Color(0.45f, 0.30f, 0.18f));
        var vp = viewpoint.transform.position;
        var pos = GroundXZ(terrain, new Vector2(vp.x + C.BenchOffsetFromViewpoint.x, vp.z + C.BenchOffsetFromViewpoint.y), 0.225f);
        Cube(viewpoint, "KH_Bench", pos, new Vector3(1.6f, 0.45f, 0.5f), wood, true);
    }

    static void BuildKominka(Terrain terrain, GameObject places, StringBuilder log)
    {
        var holder = NewChild(places, "KH_FarKominka");
        holder.transform.position = C.KominkaPosition;
        var building = InstantiatePrefab(C.KominkaBuildingPrefabGuid, holder, "KH_KominkaBuilding", log);
        var wall = InstantiatePrefab(C.KominkaWallPrefabGuid, holder, "KH_KominkaWall", log);
        if (building == null) return;

        // 室内の子を override で非アクティブ化
        int off = 0;
        foreach (var name in C.KominkaInteriorChildren)
        {
            var t = building.transform.Find(name);
            if (t == null) { log.AppendLine("ERROR interior child not found: " + name); continue; }
            var so = new SerializedObject(t.gameObject);
            so.Update();
            so.FindProperty("m_IsActive").boolValue = false;
            so.ApplyModifiedProperties();
            off++;
        }
        log.AppendLine("kominka interior deactivated: " + off + "/" + C.KominkaInteriorChildren.Length);

        // 縁側が北 (+z) を向くように回す
        var eng = building.transform.Find(C.KominkaEngawaFloorChild);
        var bb = ActiveBounds(building);
        if (eng != null)
        {
            var er = eng.GetComponentInChildren<Renderer>();
            var dir = er.bounds.center - bb.center; dir.y = 0f;
            var yaw = Vector3.SignedAngle(dir.normalized, Vector3.forward, Vector3.up);
            holder.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            log.AppendLine("kominka engawa dir=" + dir.normalized.ToString("0.00") + " -> yaw " + yaw.ToString("0.0"));
        }
        else log.AppendLine("WARN engawa child not found; orientation unchanged");

        // 建物の底を谷底の地面に合わせる
        bb = ActiveBounds(building);
        float ground = terrain.SampleHeight(bb.center) + terrain.transform.position.y;
        holder.transform.position += new Vector3(0f, ground - bb.min.y, 0f);
        bb = ActiveBounds(building);
        log.AppendLine("kominka bounds center=" + bb.center.ToString("0.0") + " size=" + bb.size.ToString("0.0") + " ground=" + ground.ToString("0.00"));

        foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        // 遠景専用: 障子の Udon (ネットワークに露出する) / Animator / Collider を削除 (プレハブの removed-component override)
        int removed = 0, remain = 0;
        foreach (var ub in holder.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true)) { Object.DestroyImmediate(ub); removed++; }
        foreach (var an in holder.GetComponentsInChildren<Animator>(true)) { Object.DestroyImmediate(an); removed++; }
        foreach (var col in holder.GetComponentsInChildren<Collider>(true)) { Object.DestroyImmediate(col); removed++; }
        remain = holder.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Length + holder.GetComponentsInChildren<Animator>(true).Length + holder.GetComponentsInChildren<Collider>(true).Length;
        log.AppendLine("kominka removed components (udon/animator/collider)=" + removed + " remaining=" + remain);
        log.AppendLine("kominka active tris=" + ActiveTris(holder));
    }

    static bool DisableComponent(Component c)
    {
        var so = new SerializedObject(c);
        so.Update();
        var p = so.FindProperty("m_Enabled");
        if (p == null) return false;
        p.boolValue = false;
        so.ApplyModifiedProperties();
        return true;
    }

    static GameObject InstantiatePrefab(string guid, GameObject parent, string name, StringBuilder log)
    {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { log.AppendLine("ERROR prefab not found guid=" + guid); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.scene);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return go;
    }

    public static Bounds ActiveBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(false);
        var b = new Bounds(go.transform.position, Vector3.zero);
        bool first = true;
        foreach (var r in rs) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        return b;
    }

    public static int ActiveTris(GameObject go)
    {
        int t = 0;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(false)) if (mf.sharedMesh != null) t += mf.sharedMesh.triangles.Length / 3;
        return t;
    }

    static void BuildBounds(Terrain terrain, GameObject root, StringBuilder log)
    {
        var bounds = NewChild(root, "KH_Bounds");
        int layer = LayerMask.NameToLayer(C.BoundLayerName);
        float th = C.BoundThickness, ov = C.BoundOverlap;
        // 各壁: 名前, 面の線分 (a→b), 外側方向, 上端の余裕
        Wall(terrain, bounds, "KH_Bound_South", new Vector2(C.BoundWestX - ov, C.BoundSouthZ), new Vector2(C.BoundEastX + ov, C.BoundSouthZ), new Vector2(0, -1), C.BoundAboveSouth, layer, log);
        Wall(terrain, bounds, "KH_Bound_North", new Vector2(C.BoundWestX - ov, C.BoundNorthZ), new Vector2(C.BoundEastX + ov, C.BoundNorthZ), new Vector2(0, 1), C.BoundAboveOther, layer, log);
        Wall(terrain, bounds, "KH_Bound_West", new Vector2(C.BoundWestX, C.BoundSouthZ - ov), new Vector2(C.BoundWestX, C.BoundNorthZ + ov), new Vector2(-1, 0), C.BoundAboveOther, layer, log);
        Wall(terrain, bounds, "KH_Bound_East", new Vector2(C.BoundEastX, C.BoundSouthZ - ov), new Vector2(C.BoundEastX, C.BoundNorthZ + ov), new Vector2(1, 0), C.BoundAboveOther, layer, log);
    }

    static void Wall(Terrain terrain, GameObject parent, string name, Vector2 a, Vector2 b, Vector2 outward, float above, int layer, StringBuilder log)
    {
        float minG = float.MaxValue, maxG = float.MinValue;
        float len = Vector2.Distance(a, b);
        int n = Mathf.CeilToInt(len / C.BoundSampleStep);
        for (int i = 0; i <= n; i++)
        {
            var p = Vector2.Lerp(a, b, i / (float)n);
            float g = GroundXZ(terrain, p, 0f).y;
            minG = Mathf.Min(minG, g); maxG = Mathf.Max(maxG, g);
        }
        float bottom = minG - C.BoundBelowGround, top = maxG + above;
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.layer = layer;
        var mid = (a + b) * 0.5f + outward * (C.BoundThickness * 0.5f);
        go.transform.position = new Vector3(mid.x, (bottom + top) * 0.5f, mid.y);
        var bc = go.AddComponent<BoxCollider>();
        bc.isTrigger = false;
        bool alongX = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.y - a.y);
        bc.size = alongX ? new Vector3(len, top - bottom, C.BoundThickness) : new Vector3(C.BoundThickness, top - bottom, len);
        log.AppendLine(name + " ground " + minG.ToString("0.0") + ".." + maxG.ToString("0.0") + " box y " + bottom.ToString("0.0") + ".." + top.ToString("0.0"));
    }

    static void BuildFence(Terrain terrain, GameObject root, StringBuilder log)
    {
        var fence = NewChild(root, "KH_Fence");
        var wood = ProtoMaterial("Wood", new Color(0.45f, 0.30f, 0.18f));
        float z = C.BoundSouthZ + 0.15f;
        var posts = new List<Vector3>();
        float vpX = C.Flats[C.ViewpointFlat, 0];
        for (float x = C.BoundWestX; x <= C.BoundEastX + 0.01f; x += C.FencePostSpacing)
        {
            var g = GroundXZ(terrain, new Vector2(x, z), C.FencePostHeight * 0.5f);
            bool gap = Mathf.Abs(x - vpX) < C.FenceGapAtViewpointHalf;
            if (!gap) Cube(fence, "KH_FencePost", g, new Vector3(0.15f, C.FencePostHeight, 0.15f), wood, false);
            posts.Add(g);
        }
        for (int i = 0; i + 1 < posts.Count; i++)
        {
            float mx = (posts[i].x + posts[i + 1].x) * 0.5f;
            if (Mathf.Abs(mx - vpX) < C.FenceGapAtViewpointHalf + C.FencePostSpacing * 0.5f) continue; // 展望の正面は空ける
            for (int r = 0; r < 2; r++)
            {
                float h = C.FencePostHeight * (r == 0 ? -0.1f : 0.35f);
                var a = posts[i] + Vector3.up * h; var b = posts[i + 1] + Vector3.up * h;
                var rail = Cube(fence, "KH_FenceRail", (a + b) * 0.5f, new Vector3(0.06f, 0.08f, Vector3.Distance(a, b)), wood, false);
                rail.transform.rotation = Quaternion.LookRotation(b - a, Vector3.up);
            }
        }
        log.AppendLine("fence posts=" + posts.Count);
    }

    static void BuildDummies(Terrain terrain, GameObject root, StringBuilder log)
    {
        var dummies = NewChild(root, "KH_Dummies");
        var plaza = new Vector2(C.Flats[C.PlazaFlat, 0], C.Flats[C.PlazaFlat, 1]);
        for (int i = 0; i < C.DummyNames.Length; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "KH_Dummy_" + C.DummyNames[i];
            go.transform.SetParent(dummies.transform, false);
            var xz = plaza + new Vector2(C.DummyOffsets[i, 0], C.DummyOffsets[i, 1]);
            go.transform.position = GroundXZ(terrain, xz, C.DummyRadius);
            go.transform.rotation = Quaternion.Euler(90f, (i * 61f) % 360f, 0f); // 横倒し = 犬の胴
            go.transform.localScale = new Vector3(C.DummyRadius * 2f, C.DummyHeight * 0.7f, C.DummyRadius * 2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = ProtoMaterial("Dummy_" + C.DummyNames[i], C.DummyColors[i]);
        }
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "KH_Ball";
        ball.transform.SetParent(dummies.transform, false);
        ball.transform.position = GroundXZ(terrain, plaza + C.BallOffset, C.BallRadius);
        ball.transform.localScale = Vector3.one * C.BallRadius * 2f;
        ball.GetComponent<MeshRenderer>().sharedMaterial = ProtoMaterial("Ball", new Color(0.9f, 0.15f, 0.1f));
        log.AppendLine("dummies=" + C.DummyNames.Length + " + ball");
    }

    static void WireDescriptor(Transform spawn, StringBuilder log)
    {
        var desc = Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
        if (desc == null) { log.AppendLine("ERROR VRC_SceneDescriptor not found"); return; }
        var so = new SerializedObject(desc);
        so.Update();
        var spawns = so.FindProperty("spawns");
        spawns.arraySize = 1;
        spawns.GetArrayElementAtIndex(0).objectReferenceValue = spawn;
        so.FindProperty("RespawnHeightY").floatValue = -10f;
        so.ApplyModifiedProperties();
        log.AppendLine("descriptor spawns[0]=" + spawn.name + " RespawnHeightY=-10");
    }

    // ------------------------------------------------------------------ Stage B parts
    static void BuildTrees(Terrain terrain, GameObject root, Dictionary<string, Material> autumn, StringBuilder log)
    {
        var trees = NewChild(root, "KH_Trees");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(C.TreePrefabPath);
        if (prefab == null) { log.AppendLine("ERROR tree prefab not found"); return; }
        var rng = new System.Random(C.TreeSeed);
        int leafOverrides = 0;
        for (int i = 0; i < C.Trees.GetLength(0); i++)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.scene);
            go.name = "KH_Tree_" + i.ToString("00");
            go.transform.SetParent(trees.transform, false);
            go.transform.position = GroundXZ(terrain, new Vector2(C.Trees[i, 0], C.Trees[i, 1]), -0.1f);
            go.transform.rotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
            go.transform.localScale = Vector3.one * Mathf.Lerp(C.TreeScaleMin, C.TreeScaleMax, (float)rng.NextDouble());
            leafOverrides += OverrideMaterials(go, autumn, new[] { C.TreeLeafMaterialName });
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }
        log.AppendLine("trees=" + C.Trees.GetLength(0) + " leafOverrides=" + leafOverrides);

        // 板の木 (十字 2 枚)
        var boards = NewChild(root, "KH_BoardTrees");
        int boardOverrides = 0;
        for (int i = 0; i < C.BoardTrees.GetLength(0); i++)
        {
            var bp = AssetDatabase.LoadAssetAtPath<GameObject>(C.BoardTreePrefabs[i % C.BoardTreePrefabs.Length]);
            if (bp == null) { log.AppendLine("ERROR board tree prefab not found"); break; }
            var holder = NewChild(boards, "KH_BoardTree_" + i.ToString("00"));
            holder.transform.position = GroundXZ(terrain, new Vector2(C.BoardTrees[i, 0], C.BoardTrees[i, 1]), -0.2f);
            float yaw = (float)(rng.NextDouble() * 180.0);
            float sc = Mathf.Lerp(C.TreeScaleMin, C.TreeScaleMax, (float)rng.NextDouble());
            for (int k = 0; k < 2; k++)
            {
                var q = (GameObject)PrefabUtility.InstantiatePrefab(bp, root.scene);
                q.name = "Board" + k;
                q.transform.SetParent(holder.transform, false);
                q.transform.localRotation = Quaternion.Euler(0f, yaw + k * 90f, 0f);
                q.transform.localScale = Vector3.one * sc;
                boardOverrides += OverrideMaterials(q, autumn, new[] { C.BoardTreeMaterialName });
                foreach (var r in q.GetComponentsInChildren<Renderer>(true))
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            }
        }
        log.AppendLine("boardTrees=" + C.BoardTrees.GetLength(0) + " overrides=" + boardOverrides);
    }

    static void BuildScenery(GameObject root, Dictionary<string, Material> autumn, StringBuilder log)
    {
        var scenery = NewChild(root, "KH_Scenery");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(C.ViewSetPrefabPath);
        if (prefab == null) { log.AppendLine("ERROR viewset prefab not found"); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.scene);
        go.name = "KH_ViewSet";
        go.transform.SetParent(scenery.transform, false);
        go.transform.position = C.ViewSetPosition;
        int n = OverrideMaterials(go, autumn, C.ViewSetMaterialNames);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        log.AppendLine("viewset at " + C.ViewSetPosition + " overrides=" + n + " bounds=" + ActiveBounds(go).ToString());
    }

    // Renderer の sharedMaterials のうち名前が names にあるものを autumn[name] へ (SerializedObject 経由で override)
    static int OverrideMaterials(GameObject go, Dictionary<string, Material> autumn, string[] names)
    {
        int count = 0;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                foreach (var n in names)
                    if (mats[i].name == n && autumn.ContainsKey(n)) { mats[i] = autumn[n]; changed = true; count++; }
            }
            if (!changed) continue;
            var so = new SerializedObject(r);
            so.Update();
            var arr = so.FindProperty("m_Materials");
            for (int i = 0; i < mats.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = mats[i];
            so.ApplyModifiedProperties();
        }
        return count;
    }

    static void BuildDetails(TerrainData td, StringBuilder log)
    {
        var samples = KH_TerrainShape.SamplePath();
        int D = td.detailWidth;
        var plaza = new Vector2(C.Flats[C.PlazaFlat, 0], C.Flats[C.PlazaFlat, 1]);
        long total = 0;
        for (int layer = 0; layer < td.detailPrototypes.Length; layer++)
        {
            var map = new int[td.detailHeight, D];
            int density = layer < C.DetailDensity.Length ? C.DetailDensity[layer] : 1;
            for (int iz = 0; iz < td.detailHeight; iz++)
                for (int ix = 0; ix < D; ix++)
                {
                    float x = (ix + 0.5f) / D * td.size.x, z = (iz + 0.5f) / td.detailHeight * td.size.z;
                    if (x < C.WalkX0 || x > C.WalkX1 || z < C.WalkZ0 || z > C.WalkZ1) continue;
                    if (Vector2.Distance(new Vector2(x, z), plaza) < C.DetailClearPlazaRadius) continue;
                    float d = KH_TerrainShape.DistanceToPath(samples, x, z, out _);
                    if (d < C.PathHalfWidth + C.DetailClearAroundPath) continue;
                    float n = Mathf.PerlinNoise(x * C.DetailNoiseScale + C.DetailSeed * 10f + layer * 3f, z * C.DetailNoiseScale + C.DetailSeed * 7f);
                    if (n < 0.45f) continue;
                    map[iz, ix] = density;
                    total += density;
                }
            td.SetDetailLayer(0, 0, layer, map);
        }
        log.AppendLine("details written, total density units=" + total);
    }

    // ------------------------------------------------------------------ save / log
    static void SaveAll(TerrainData td, StringBuilder log)
    {
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        bool ok = EditorSceneManager.SaveOpenScenes();
        log.AppendLine("saved assets + scene ok=" + ok);
    }

    [MenuItem(Menu + "Reopen Scene From Disk")]
    public static void ReopenScene()
    {
        var path = EditorSceneManager.GetActiveScene().path;
        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        Debug.Log("KH_TerrainPrototypeEditor: reopened " + path);
    }

    static void Fail(StringBuilder log, string msg, string file)
    {
        log.AppendLine("ABORT: " + msg);
        Finish(log, file);
        Debug.LogError("KH_TerrainPrototypeEditor: " + msg);
    }

    static void Finish(StringBuilder log, string file)
    {
        Directory.CreateDirectory(LogDir);
        File.WriteAllText(LogDir + "/" + file, log.ToString());
        Debug.Log("KH_TerrainPrototypeEditor -> " + LogDir + "/" + file + "\n" + log);
    }
}
