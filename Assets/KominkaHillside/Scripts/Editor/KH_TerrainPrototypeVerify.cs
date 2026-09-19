// Terrain 試作の検証 (計画 §4)。期待する状態を数え、Temp/KH_TerrainPrototype/verify.txt に書く。
// 保存後の判定 (override の永続化 / .unity の grep) は「Reopen Scene From Disk」の後に実行する。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using C = KH_TerrainPrototypeConfig;

public static class KH_TerrainPrototypeVerify
{
    const string LogDir = "Temp/KH_TerrainPrototype";
    static StringBuilder log;
    static int ok, ng;

    static void Require(string label, bool cond) { log.AppendLine((cond ? "OK  " : "NG  ") + label); if (cond) ok++; else ng++; }
    static void Info(string s) { log.AppendLine("    " + s); }

    [MenuItem("Tools/KominkaHillside/Terrain Prototype/Verify")]
    public static void Verify()
    {
        log = new StringBuilder(); ok = 0; ng = 0;
        var scene = EditorSceneManager.GetActiveScene();
        Info("scene=" + scene.path + " dirty=" + scene.isDirty);

        var root = GameObject.Find("KH_World");
        Require("KH_World exists", root != null);
        if (root == null) { Finish(); return; }
        var terrain = root.GetComponentInChildren<Terrain>();
        Require("terrain exists", terrain != null);
        if (terrain == null) { Finish(); return; }
        var td = terrain.terrainData;
        var tc = terrain.GetComponent<TerrainCollider>();

        // --- TerrainData
        Require("terrainData asset path == config", AssetDatabase.GetAssetPath(td) == C.TerrainDataPath);
        Require("heightmap resolution == " + C.HeightmapResolution, td.heightmapResolution == C.HeightmapResolution);
        Require("size == " + C.TerrainSize, (td.size - C.TerrainSize).magnitude < 0.01f);
        Require("terrainCollider enabled and same data", tc != null && tc.enabled && tc.terrainData == td);
        var wantLayers = new[] { "GrassHillAlbedo", "GrassRockyAlbedo", "SandAlbedo", "MudRockyAlbedoSpecular" };
        bool layersOk = td.terrainLayers.Length == wantLayers.Length;
        for (int i = 0; layersOk && i < wantLayers.Length; i++)
            layersOk = td.terrainLayers[i] != null && td.terrainLayers[i].diffuseTexture != null && td.terrainLayers[i].diffuseTexture.name == wantLayers[i];
        Require("4 layers by diffuse name", layersOk);

        // --- 道
        var path = KH_TerrainShape.SamplePath();
        float tanMax = Mathf.Tan(C.PathMaxSlopeDeg * Mathf.Deg2Rad), tanCross = Mathf.Tan(C.PathMaxCrossSlopeDeg * Mathf.Deg2Rad);
        int slopeNg = 0, crossNg = 0; float worstSlope = 0f, worstCross = 0f; float minEdgeZ = float.MaxValue;
        var center = new float[path.Count];
        for (int i = 0; i < path.Count; i++) center[i] = Ground(terrain, path[i].xz);
        for (int i = 0; i < path.Count; i++)
        {
            int j = (i + 1) % path.Count;
            float dist = Vector2.Distance(path[i].xz, path[j].xz);
            float slope = Mathf.Abs(center[j] - center[i]) / Mathf.Max(dist, 0.01f);
            worstSlope = Mathf.Max(worstSlope, slope);
            if (slope > tanMax + 0.01f) slopeNg++;
            Vector2 fwd, left; KH_TerrainShape.PathFrame(path, i, out fwd, out left);
            var l = path[i].xz + left * C.PathHalfWidth; var r = path[i].xz - left * C.PathHalfWidth;
            float hl = Ground(terrain, l), hr = Ground(terrain, r);
            float cl = Mathf.Abs(hl - center[i]) / C.PathHalfWidth, cr = Mathf.Abs(hr - center[i]) / C.PathHalfWidth;
            worstCross = Mathf.Max(worstCross, Mathf.Max(cl, cr));
            if (cl > tanCross + 0.01f || cr > tanCross + 0.01f) crossNg++;
            minEdgeZ = Mathf.Min(minEdgeZ, Mathf.Min(l.y, r.y));
        }
        Info("path samples=" + path.Count + " length=" + KH_TerrainShape.PathLength(path).ToString("0.0") + " m");
        Require("path slope <= " + C.PathMaxSlopeDeg + " deg (worst " + (Mathf.Atan(worstSlope) * Mathf.Rad2Deg).ToString("0.0") + " deg, ng=" + slopeNg + ")", slopeNg == 0);
        Require("path cross slope <= " + C.PathMaxCrossSlopeDeg + " deg (worst " + (Mathf.Atan(worstCross) * Mathf.Rad2Deg).ToString("0.0") + " deg, ng=" + crossNg + ")", crossNg == 0);
        Require("path edge min z >= fence z + " + C.PathFenceClearance + " (min edge z " + minEdgeZ.ToString("0.0") + ")", minEdgeZ >= C.BoundSouthZ + C.PathFenceClearance);

        // --- 古民家
        var kom = root.transform.Find("KH_Places/KH_FarKominka");
        Require("KH_FarKominka exists", kom != null);
        int blocked = -1;
        if (kom != null)
        {
            var building = kom.Find("KH_KominkaBuilding");
            Require("kominka building instance exists", building != null);
            int tris = KH_TerrainPrototypeEditor.ActiveTris(kom.gameObject);
            Require("kominka active tris <= 8000 (" + tris + ")", tris <= 8000);
            int activeInterior = 0, found = 0;
            if (building != null)
                foreach (var n in C.KominkaInteriorChildren) { var t = building.Find(n); if (t != null) { found++; if (t.gameObject.activeSelf) activeInterior++; } }
            Require("kominka interior children found " + found + "/" + C.KominkaInteriorChildren.Length + ", active " + activeInterior, found == C.KominkaInteriorChildren.Length && activeInterior == 0);
            if (building != null) Require("kominka is still a prefab instance (not unpacked)", PrefabUtility.IsPartOfPrefabInstance(building.gameObject));
            int udonN = kom.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Length, animN = kom.GetComponentsInChildren<Animator>(true).Length, colN = kom.GetComponentsInChildren<Collider>(true).Length;
            Require("kominka has no udon/animator/collider (" + udonN + "/" + animN + "/" + colN + ")", udonN == 0 && animN == 0 && colN == 0);

            // 展望 → 古民家 5 点の遮蔽 (診断値)
            var vp = new Vector2(C.Flats[C.ViewpointFlat, 0], C.Flats[C.ViewpointFlat, 1]);
            var eye = new Vector3(vp.x, Ground(terrain, vp) + 1.6f, vp.y);
            var b = KH_TerrainPrototypeEditor.ActiveBounds(kom.gameObject);
            var pts = new[] {
                new Vector3(b.center.x, b.max.y, b.center.z), new Vector3(b.min.x, b.max.y - 1f, b.center.z), new Vector3(b.max.x, b.max.y - 1f, b.center.z),
                new Vector3(b.min.x, b.min.y + 1f, b.min.z), new Vector3(b.max.x, b.min.y + 1f, b.max.z) };
            blocked = 0;
            foreach (var p in pts)
            {
                RaycastHit hit; var dir = p - eye; float dist = dir.magnitude;
                if (tc != null && tc.Raycast(new Ray(eye, dir.normalized), out hit, dist - 0.5f)) blocked++;
            }
            Info("DIAG viewpoint->kominka terrain-blocked points = " + blocked + "/5 (pass/fail is by view shots)  eye=" + eye.ToString("0.0") + " kominka=" + b.center.ToString("0.0") + " dist=" + Vector3.Distance(eye, b.center).ToString("0.0"));
        }

        // --- 接地対象の範囲
        var grounded = new List<Transform>();
        foreach (var n in new[] { "KH_Places/KH_Entrance", "KH_Places/KH_Viewpoint", "KH_Places/KH_Plaza", "KH_Places/KH_Viewpoint/KH_Bench" })
        { var t = root.transform.Find(n); if (t != null) grounded.Add(t); else Require("exists " + n, false); }
        var dum = root.transform.Find("KH_Dummies");
        if (dum != null) foreach (Transform t in dum) grounded.Add(t);
        int outside = 0;
        foreach (var t in grounded) if (!InWalk(t.position.x, t.position.z)) { outside++; Info("outside: " + t.name + " " + t.position.ToString("0.0")); }
        for (int i = 0; i < C.Path.GetLength(0); i++) if (!InWalk(C.Path[i, 0], C.Path[i, 1])) outside++;
        Require("grounded objects + path control points inside walk area (outside=" + outside + ")", outside == 0);

        // --- 境界
        var bounds = root.transform.Find("KH_Bounds");
        Require("KH_Bounds exists", bounds != null);
        if (bounds != null)
        {
            var walls = new Dictionary<string, BoxCollider>();
            foreach (Transform t in bounds) { var bc = t.GetComponent<BoxCollider>(); if (bc != null) walls[t.name] = bc; }
            Require("4 walls", walls.Count == 4);
            int layer = LayerMask.NameToLayer(C.BoundLayerName);
            bool flags = true;
            foreach (var kv in walls) flags &= kv.Value.enabled && !kv.Value.isTrigger && kv.Value.gameObject.layer == layer;
            Require("walls enabled, non-trigger, layer " + C.BoundLayerName, flags);
            BoxCollider s, n, w, e;
            if (walls.TryGetValue("KH_Bound_South", out s) && walls.TryGetValue("KH_Bound_North", out n) && walls.TryGetValue("KH_Bound_West", out w) && walls.TryGetValue("KH_Bound_East", out e))
            {
                Require("south face z == " + C.BoundSouthZ, Mathf.Abs(s.bounds.max.z - C.BoundSouthZ) < 0.05f);
                Require("north face z == " + C.BoundNorthZ, Mathf.Abs(n.bounds.min.z - C.BoundNorthZ) < 0.05f);
                Require("west face x == " + C.BoundWestX, Mathf.Abs(w.bounds.max.x - C.BoundWestX) < 0.05f);
                Require("east face x == " + C.BoundEastX, Mathf.Abs(e.bounds.min.x - C.BoundEastX) < 0.05f);
                Require("corners overlap (3D)", s.bounds.Intersects(w.bounds) && s.bounds.Intersects(e.bounds) && n.bounds.Intersects(w.bounds) && n.bounds.Intersects(e.bounds));
                int exposeNg = 0, bottomNg = 0;
                exposeNg += WallCheck(terrain, tc, s, new Vector2(C.BoundWestX, C.BoundSouthZ), new Vector2(C.BoundEastX, C.BoundSouthZ), C.BoundAboveSouth, ref bottomNg);
                exposeNg += WallCheck(terrain, tc, n, new Vector2(C.BoundWestX, C.BoundNorthZ), new Vector2(C.BoundEastX, C.BoundNorthZ), C.BoundAboveOther, ref bottomNg);
                exposeNg += WallCheck(terrain, tc, w, new Vector2(C.BoundWestX, C.BoundSouthZ), new Vector2(C.BoundWestX, C.BoundNorthZ), C.BoundAboveOther, ref bottomNg);
                exposeNg += WallCheck(terrain, tc, e, new Vector2(C.BoundEastX, C.BoundSouthZ), new Vector2(C.BoundEastX, C.BoundNorthZ), C.BoundAboveOther, ref bottomNg);
                Require("wall exposure >= required along faces (sampled, ng=" + exposeNg + ")", exposeNg == 0);
                Require("wall bottom <= ground - " + C.BoundBelowGround + " (sampled, ng=" + bottomNg + ")", bottomNg == 0);
            }
        }
        var fence = root.transform.Find("KH_Fence");
        if (fence != null)
        {
            float fx0 = float.MaxValue, fx1 = float.MinValue, fz = 0f; int posts = 0;
            foreach (Transform t in fence) if (t.name == "KH_FencePost") { posts++; fx0 = Mathf.Min(fx0, t.position.x); fx1 = Mathf.Max(fx1, t.position.x); fz = t.position.z; }
            Require("fence posts span x " + C.BoundWestX + ".." + C.BoundEastX + " at z ~" + C.BoundSouthZ + " (posts=" + posts + ", x " + fx0.ToString("0.0") + ".." + fx1.ToString("0.0") + ", z " + fz.ToString("0.0") + ")",
                posts > 0 && Mathf.Abs(fx0 - C.BoundWestX) < 0.1f && Mathf.Abs(fx1 - C.BoundEastX) < 0.1f && Mathf.Abs(fz - C.BoundSouthZ) < 0.5f);
            Info("fence visual gap at viewpoint x=" + C.Flats[C.ViewpointFlat, 0] + " +-" + C.FenceGapAtViewpointHalf + " (bound wall is continuous; checked above)");
        }
        else Require("KH_Fence exists", false);

        // --- Floor / スポーン
        bool floor = false;
        foreach (var go in scene.GetRootGameObjects()) if (go.name == "Floor") floor = true;
        Require("template Floor removed", !floor);
        var desc = Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
        var entrance = root.transform.Find("KH_Places/KH_Entrance");
        Require("descriptor exists", desc != null);
        if (desc != null && entrance != null)
        {
            Require("spawns[0] == KH_Entrance", desc.spawns != null && desc.spawns.Length >= 1 && desc.spawns[0] == entrance);
            Require("RespawnHeightY == -10", Mathf.Abs(desc.RespawnHeightY + 10f) < 0.01f);
            Require("spawn faces north (+z)", Vector3.Dot(entrance.forward, Vector3.forward) > 0.99f);
            RaycastHit hit = new RaycastHit();
            bool grounded1 = tc != null && tc.Raycast(new Ray(entrance.position + Vector3.up * 5f, Vector3.down), out hit, 50f);
            float off = grounded1 ? entrance.position.y - hit.point.y : -999f;
            Require("spawn ground offset == " + C.SpawnGroundOffset + " +-0.05 (" + off.ToString("0.00") + ")", grounded1 && Mathf.Abs(off - C.SpawnGroundOffset) <= 0.05f);
            var hits = Physics.OverlapBox(entrance.position + Vector3.up * 1f, new Vector3(0.25f, 1f, 0.25f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            int nonTerrain = 0; foreach (var h in hits) if (!(h is TerrainCollider)) { nonTerrain++; Info("spawn overlap: " + h.name); }
            Require("spawn body box overlaps no collider (except terrain)", nonTerrain == 0);
            float minPost = float.MaxValue; int postRoad = 0;
            foreach (Transform t in entrance)
            {
                if (!t.name.StartsWith("KH_GatePost")) continue;
                var pxz = new Vector2(t.position.x, t.position.z);
                minPost = Mathf.Min(minPost, Vector2.Distance(pxz, new Vector2(entrance.position.x, entrance.position.z)));
                float rh; float d = KH_TerrainShape.DistanceToPath(path, pxz.x, pxz.y, out rh);
                if (d < C.PathHalfWidth + 0.125f) postRoad++;
            }
            Require("gate posts >= 2 m from spawn (min " + minPost.ToString("0.00") + ")", minPost >= 2f);
            Require("gate posts do not cross the road", postRoad == 0);
        }

        // --- Stage 状態
        var trees = root.transform.Find("KH_Trees");
        var scenery = root.transform.Find("KH_Scenery");
        long detailTotal = 0; int roadDetail = 0, plazaDetail = 0;
        var plaza = new Vector2(C.Flats[C.PlazaFlat, 0], C.Flats[C.PlazaFlat, 1]);
        for (int l = 0; l < td.detailPrototypes.Length; l++)
        {
            var m = td.GetDetailLayer(0, 0, td.detailWidth, td.detailHeight, l);
            for (int iz = 0; iz < td.detailHeight; iz++) for (int ix = 0; ix < td.detailWidth; ix++) detailTotal += m[iz, ix];
            for (int i = 0; i < path.Count; i += 5) roadDetail += DetailAt(td, m, path[i].xz);
            plazaDetail += DetailAt(td, m, plaza);
        }
        bool stageB = trees != null;
        Info("stage detected: " + (stageB ? "B (dressing present)" : "A (ground only)") + " detailTotal=" + detailTotal);
        if (!stageB)
        {
            Require("Stage A: details all zero", detailTotal == 0);
            Require("Stage A: no KH_Scenery", scenery == null);
        }
        else
        {
            Require("Stage B: trees == " + C.Trees.GetLength(0) + " (" + trees.childCount + ")", trees.childCount == C.Trees.GetLength(0));
            int leafAutumn = 0, leafTotal = 0;
            foreach (Transform t in trees) foreach (var r in t.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials)
            { if (m == null) continue; if (m.name.StartsWith(C.TreeLeafMaterialName)) { leafTotal++; if (m.name.EndsWith("_Autumn")) leafAutumn++; } }
            Require("Stage B: tree leaf materials are autumn (" + leafAutumn + "/" + leafTotal + ")", leafTotal > 0 && leafAutumn == leafTotal);
            var boards = root.transform.Find("KH_BoardTrees");
            Require("Stage B: board trees == " + C.BoardTrees.GetLength(0) + " (" + (boards != null ? boards.childCount : 0) + ")", boards != null && boards.childCount == C.BoardTrees.GetLength(0));
            int sceneTris = 0; foreach (var mf in root.GetComponentsInChildren<MeshFilter>(false)) if (mf.sharedMesh != null) sceneTris += mf.sharedMesh.triangles.Length / 3;
            Info("DIAG KH_World active mesh triangles (excluding terrain) = " + sceneTris);
            Require("Stage B: KH_Scenery has 1 viewset", scenery != null && scenery.childCount == 1);
            if (scenery != null && scenery.childCount == 1)
            {
                int vsAutumn = 0, vsTotal = 0;
                foreach (var r in scenery.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) { if (m == null) continue; vsTotal++; if (m.name.EndsWith("_Autumn")) vsAutumn++; }
                Require("Stage B: viewset materials are autumn (" + vsAutumn + "/" + vsTotal + ")", vsTotal > 0 && vsAutumn == vsTotal);
            }
            bool protoOk = td.detailPrototypes.Length == 5;
            foreach (var p in td.detailPrototypes) { string err; if (!p.Validate(out err)) { protoOk = false; Info("detail prototype invalid: " + err); } }
            Require("Stage B: 5 detail prototypes valid", protoOk);
            Require("Stage B: details present on hill (total " + detailTotal + ")", detailTotal > 0);
            Require("Stage B: details zero on road center and plaza center (road " + roadDetail + ", plaza " + plazaDetail + ")", roadDetail == 0 && plazaDetail == 0);
        }
        if (dum != null) Require("dummies 6 + ball 1 (" + dum.childCount + ")", dum.childCount == 7);

        // --- 保存後のファイル検査 (dirty でなければ)
        if (!scene.isDirty && File.Exists(scene.path))
        {
            var text = File.ReadAllText(scene.path);
            int idx = text.IndexOf("NetworkIDCollection");
            int dangling = 0;
            if (idx >= 0) { int p = idx; while ((p = text.IndexOf("gameObject: {fileID: 0}", p + 1)) >= 0) dangling++; }
            Require("saved .unity: NetworkIDs dangling (gameObject fileID 0) == 0 (" + dangling + ")", dangling == 0);
            Require("saved .unity: KH_Terrain present in file", text.Contains("KH_Terrain"));
        }
        else Info("scene dirty or unsaved: file checks skipped (save, Reopen Scene From Disk, then Verify again)");

        Finish();
    }

    static bool InWalk(float x, float z) { return x >= C.WalkX0 && x <= C.WalkX1 && z >= C.WalkZ0 && z <= C.WalkZ1; }

    static float Ground(Terrain terrain, Vector2 xz) { return terrain.SampleHeight(new Vector3(xz.x, 0f, xz.y)) + terrain.transform.position.y; }

    static int DetailAt(TerrainData td, int[,] m, Vector2 xz)
    {
        int ix = Mathf.Clamp(Mathf.FloorToInt(xz.x / td.size.x * td.detailWidth), 0, td.detailWidth - 1);
        int iz = Mathf.Clamp(Mathf.FloorToInt(xz.y / td.size.z * td.detailHeight), 0, td.detailHeight - 1);
        return m[iz, ix];
    }

    // 壁の面に沿って地面を TerrainCollider で取り、露出高さと底を検査。戻り値 = 露出不足の数
    static int WallCheck(Terrain terrain, TerrainCollider tc, BoxCollider wall, Vector2 a, Vector2 b, float above, ref int bottomNg)
    {
        int ng = 0;
        float len = Vector2.Distance(a, b); int n = Mathf.CeilToInt(len / C.BoundSampleStep);
        for (int i = 0; i <= n; i++)
        {
            var p = Vector2.Lerp(a, b, i / (float)n);
            RaycastHit hit;
            float g = tc != null && tc.Raycast(new Ray(new Vector3(p.x, 100f, p.y), Vector3.down), out hit, 200f) ? hit.point.y : Ground(terrain, p);
            if (wall.bounds.max.y - g < above - 0.01f) ng++;
            if (wall.bounds.min.y > g - C.BoundBelowGround + 0.01f) bottomNg++;
        }
        return ng;
    }

    static void Finish()
    {
        log.Insert(0, "RESULT: ok=" + ok + " ng=" + ng + "\n");
        Directory.CreateDirectory(LogDir);
        File.WriteAllText(LogDir + "/verify.txt", log.ToString());
        Debug.Log("KH_TerrainPrototypeVerify -> " + LogDir + "/verify.txt\n" + log);
    }
}
