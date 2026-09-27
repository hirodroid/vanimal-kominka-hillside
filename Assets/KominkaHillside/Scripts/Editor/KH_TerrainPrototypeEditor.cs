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
            KominkaHillside.Editor.KH_ProductionScene.RejectLegacyRebuild();
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

        // 古民家 (門の位置がスポーンの基準になるので先に置く)
        Vector3 gateCenter, gateOut;
        var kom = BuildKominka(terrain, places, log, out gateCenter, out gateOut);

        // 入口 = 門の外側。門の開口は透明 Collider で塞ぐ (古民家側へは戻れない)
        var entrance = NewChild(places, "KH_Entrance");
        var spawnXZ = new Vector2(gateCenter.x + gateOut.x * C.SpawnDistanceFromGate, gateCenter.z + gateOut.z * C.SpawnDistanceFromGate);
        entrance.transform.position = GroundXZ(terrain, spawnXZ, C.SpawnGroundOffset);
        entrance.transform.rotation = Quaternion.LookRotation(new Vector3(gateOut.x, 0f, gateOut.z), Vector3.up); // 門から外を向く
        BuildGateBlock(entrance, gateCenter, gateOut, log);

        var viewpoint = NewChild(places, "KH_Viewpoint");
        viewpoint.transform.position = GroundXZ(terrain, new Vector2(C.Flats[C.ViewpointFlat, 0], C.Flats[C.ViewpointFlat, 1]), 0f);
        BuildBench(terrain, viewpoint);

        var plaza = NewChild(places, "KH_Plaza");
        plaza.transform.position = GroundXZ(terrain, new Vector2(C.Flats[C.PlazaFlat, 0], C.Flats[C.PlazaFlat, 1]), 0f);

        BuildBounds(terrain, root, log);
        BuildDummies(terrain, root, log);
        WireDescriptor(entrance.transform, log);

        SaveAll(td, log);
        Finish(log, "build-A.log");
    }

    // ------------------------------------------------------------------ Build Stage B
    [MenuItem(Menu + "Build Stage B (Dressing)")]
    public static void BuildStageB()
    {
            KominkaHillside.Editor.KH_ProductionScene.RejectLegacyRebuild();
        var log = new StringBuilder();
        var root = GameObject.Find(RootName);
        var terrain = root != null ? root.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { Fail(log, "Stage A output not found. Run Build Stage A first.", "build-B.log"); return; }
        var td = terrain.terrainData;

        foreach (var name in new[] { "KH_Trees", "KH_BoardTrees", "KH_Shrubs", "KH_Scenery" })
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

    // 門の開口を塞ぐ透明 BoxCollider (門枠の中心、外向きに直交)
    static void BuildGateBlock(GameObject entrance, Vector3 gateCenter, Vector3 gateOut, StringBuilder log)
    {
        var go = new GameObject("KH_GateBlock");
        go.transform.SetParent(entrance.transform.parent, false);
        go.layer = LayerMask.NameToLayer(C.BoundLayerName);
        go.transform.position = new Vector3(gateCenter.x, gateCenter.y + C.GateBlockSize.y * 0.5f - 1.5f, gateCenter.z);
        go.transform.rotation = Quaternion.LookRotation(new Vector3(gateOut.x, 0f, gateOut.z), Vector3.up);
        var bc = go.AddComponent<BoxCollider>();
        bc.size = C.GateBlockSize;
        log.AppendLine("gate block at " + go.transform.position.ToString("0.00") + " out=" + gateOut.ToString("0.00") + " spawn=" + entrance.transform.position.ToString("0.00"));
    }

    static void BuildBench(Terrain terrain, GameObject viewpoint)
    {
        var wood = ProtoMaterial("Wood", new Color(0.45f, 0.30f, 0.18f));
        var vp = viewpoint.transform.position;
        var pos = GroundXZ(terrain, new Vector2(vp.x + C.BenchOffsetFromViewpoint.x, vp.z + C.BenchOffsetFromViewpoint.y), 0.225f);
        Cube(viewpoint, "KH_Bench", pos, new Vector3(1.6f, 0.45f, 0.5f), wood, true);
    }

    static GameObject BuildKominka(Terrain terrain, GameObject places, StringBuilder log, out Vector3 gateCenter, out Vector3 gateOut)
    {
        gateCenter = C.KominkaPosition; gateOut = Vector3.forward;
        var holder = NewChild(places, "KH_FarKominka");
        holder.transform.position = C.KominkaPosition;
        holder.transform.rotation = Quaternion.Euler(0f, C.KominkaYaw, 0f);
        var building = InstantiatePrefab(C.KominkaBuildingPrefabGuid, holder, "KH_KominkaBuilding", log);
        var wall = InstantiatePrefab(C.KominkaWallPrefabGuid, holder, "KH_KominkaWall", log);
        if (building == null || wall == null) return holder;

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

        // 建物の底を谷底の地面に合わせる
        var bb = ActiveBounds(building);
        float ground = terrain.SampleHeight(bb.center) + terrain.transform.position.y;
        holder.transform.position += new Vector3(0f, ground - bb.min.y, 0f);
        bb = ActiveBounds(building);
        log.AppendLine("kominka bounds center=" + bb.center.ToString("0.0") + " size=" + bb.size.ToString("0.0") + " ground=" + ground.ToString("0.00"));

        foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        // 遠景専用: 障子の Udon (ネットワークに露出する) / Animator / Collider を削除 (プレハブの removed-component override)
        int removed = 0, remain = 0;
        // Remove U# proxies first: surviving proxies recreate removed Udon components during Play/build.
        foreach (var proxy in holder.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true)) { Object.DestroyImmediate(proxy); removed++; }
        foreach (var ub in holder.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true)) { Object.DestroyImmediate(ub); removed++; }
        foreach (var an in holder.GetComponentsInChildren<Animator>(true)) { Object.DestroyImmediate(an); removed++; }
        foreach (var col in holder.GetComponentsInChildren<Collider>(true)) { Object.DestroyImmediate(col); removed++; }
        remain = holder.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true).Length + holder.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Length + holder.GetComponentsInChildren<Animator>(true).Length + holder.GetComponentsInChildren<Collider>(true).Length;
        log.AppendLine("kominka removed components (udon/animator/collider)=" + removed + " remaining=" + remain);
        // 塀は歩ける側にあるので MeshCollider で塞ぐ (敷地に入れない)
        var wallMesh = wall.transform.Find(C.KominkaWallMeshChild);
        if (wallMesh != null) { var mc = wallMesh.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = wallMesh.GetComponent<MeshFilter>().sharedMesh; log.AppendLine("wall MeshCollider added"); }
        else log.AppendLine("ERROR wall mesh child not found: " + C.KominkaWallMeshChild);
        // 門の位置と外向き (門枠の中心、玄関 → 門枠 の方向)
        var gate = wall.transform.Find(C.KominkaGateFrameChild);
        var genkan = building.transform.Find(C.KominkaGenkanChild);
        if (gate != null && genkan != null)
        {
            gateCenter = gate.GetComponentInChildren<Renderer>().bounds.center;
            var gk = genkan.GetComponentInChildren<Renderer>().bounds.center;
            gateOut = gateCenter - gk; gateOut.y = 0f; gateOut.Normalize();
            log.AppendLine("gate center=" + gateCenter.ToString("0.00") + " out=" + gateOut.ToString("0.00"));
        }
        else log.AppendLine("ERROR gate/genkan child not found");
        log.AppendLine("kominka active tris=" + ActiveTris(holder));
        return holder;
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

        // 低木: 歩ける範囲に seed で散らす (道・平坦部・古民家から離す)
        var shrubs = NewChild(root, "KH_Shrubs");
        var srng = new System.Random(C.ShrubSeed);
        var path = KH_TerrainShape.SamplePath();
        var komT = root.transform.Find("KH_Places/KH_FarKominka");
        var komB = komT != null ? ActiveBounds(komT.gameObject) : new Bounds(Vector3.zero, Vector3.zero);
        int placed = 0, tries = 0;
        while (placed < C.ShrubCount && tries < 2000)
        {
            tries++;
            float x = Mathf.Lerp(C.WalkX0, C.WalkX1, (float)srng.NextDouble()), z = Mathf.Lerp(C.WalkZ0, C.WalkZ1, (float)srng.NextDouble());
            float rh; if (KH_TerrainShape.DistanceToPath(path, x, z, out rh) < C.ShrubClearPath) continue;
            bool nearFlat = false; for (int f = 0; f < C.Flats.GetLength(0); f++) if (Vector2.Distance(new Vector2(x, z), new Vector2(C.Flats[f, 0], C.Flats[f, 1])) < C.Flats[f, 3] + C.ShrubClearFlat) nearFlat = true;
            if (nearFlat) continue;
            var kb = komB; kb.Expand(C.ShrubClearKominka * 2f); if (kb.Contains(new Vector3(x, kb.center.y, z))) continue;
            var sp = AssetDatabase.LoadAssetAtPath<GameObject>(C.ShrubPrefabPaths[placed % C.ShrubPrefabPaths.Length]);
            if (sp == null) { log.AppendLine("ERROR shrub prefab not found"); break; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(sp, root.scene);
            go.name = "KH_Shrub_" + placed.ToString("00");
            go.transform.SetParent(shrubs.transform, false);
            go.transform.position = GroundXZ(terrain, new Vector2(x, z), -0.1f);
            go.transform.rotation = Quaternion.Euler(0f, (float)(srng.NextDouble() * 360.0), 0f);
            go.transform.localScale = Vector3.one * Mathf.Lerp(C.ShrubScaleMin, C.ShrubScaleMax, (float)srng.NextDouble());
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            placed++;
        }
        log.AppendLine("shrubs=" + placed + " (tries " + tries + ")");
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
