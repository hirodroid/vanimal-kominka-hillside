using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // BUG-ENV-003 (farmhouse exterior) / BUG-DOG-013 (premium wash tools): detection + capture only, no fixes.
    //   ZFight/1 - Scan Farmhouse      read-only; Temp/KH_ZFightScan/farmhouse.json + .md
    //   ZFight/2 - Capture Farmhouse   HideAndDontSave camera at the spawn eye; JPEGs in docs/plans/evidence/2026-10-13-zfight/
    //   ZFight/3 - Scan Premium Tools  (KH_ZFightScanEditor.Premium.cs)
    //   ZFight/4 - Capture Premium Tools
    // Nothing is saved, nothing is instantiated in the open scene by 1-2; a missing object is logged and skipped.
    // Player camera: the scene's Reference Camera is near 0.3 / far 2200 / FOV 60, and VRChat clamps the near plane to
    // 0.001-0.05 (camera-design.md:44), so captures use near = clamp(ref, 0.001, 0.05) = 0.05 and far = ref (2200).
    public static partial class KH_ZFightScanEditor
    {
        const string Menu = "Tools/KominkaHillside/ZFight/";
        public const string OutDir = "Temp/KH_ZFightScan";
        public const string EvidenceDir = "docs/plans/evidence/2026-10-13-zfight";
        const string FlickerDir = OutDir + "/flicker";
        const string RootPath = "KH_World/KH_Places/KH_FarKominka";
        const string WallRelPath = "KH_KominkaWall/外壁";
        const string SpawnPath = "KH_World/KH_Places/KH_Entrance";
        static readonly Vector3 SpawnFallback = new Vector3(47.64f, 1.61f, 40.08f);
        const float EyeHeight = 1.62f, FlagRadius = 2f, WideYaw = 154f, WideFov = 70f, NarrowFov = 20f, CloserBy = 2f;
        const float FarmCell = 0.5f, FarmMinAreaM2 = 1e-4f /* 1 cm2 */, FarmClusterLink = 1f, FarmBudgetSeconds = 150;
        const int MaxNarrowGroups = 12, MaxSteps = 12;
        // Task tokens plus the names actually in 建物.prefab / 家具など.prefab (石畳み, 玄関床, 台座, 縁側床; "石" also covers 石段 / 石畳).
        static readonly string[] StepTokens = { "step", "stone", "stair", "genkan", "entrance", "石段", "階段", "石畳", "石", "玄関", "台座", "縁側", "沓脱" };

        [Serializable] public class Target { public string name, path; public Vector3 point, boundsMin, boundsMax; public float distance; }

        [Serializable] public class FarmReport
        {
            public string utc, scene, root, thresholds, scoring;
            public bool sceneDirtyBefore, sceneDirtyAfter, truncated;
            public Vector3 spawnEye; public float cellSize, minAreaCm2, clusterLink;
            public int renderersMain, renderersExtra, renderersInactiveSkipped, renderersLodSkipped, triangles, oversize;
            public long candidates, accepted; public double seconds;
            public List<string> notes = new List<string>();
            public List<Target> wallCorners = new List<Target>(), steps = new List<Target>(), gate = new List<Target>();
            public List<KH_ZFightDetector.Group> groups = new List<KH_ZFightDetector.Group>();
            public int backToBackCount; public float backToBackAreaCm2;
            public List<KH_ZFightDetector.Group> backToBackTop = new List<KH_ZFightDetector.Group>();
            public List<KH_ZFightDetector.TerrainRow> terrain = new List<KH_ZFightDetector.TerrainRow>();
            public List<KH_ZFightDetector.PartInfo> parts = new List<KH_ZFightDetector.PartInfo>();
        }

        // ================================================================ shared scene lookups

        static bool Guard(string tag)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            { Debug.LogWarning(tag + " skipped: editor is playing or compiling"); return false; }
            var s = SceneManager.GetActiveScene();
            Debug.Log(tag + " scene=" + s.path + " dirty=" + s.isDirty);
            if (s.path != KH_ProductionScene.Path) Debug.LogWarning(tag + " active scene is not " + KH_ProductionScene.Path + "; continuing");
            return true;
        }

        static GameObject FindRoot()
        {
            var root = GameObject.Find(RootPath);
            if (root != null) return root;
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    if (t.name == "KH_FarKominka") return t.gameObject;
            Debug.LogWarning("[KH ZFight] " + RootPath + " not found");
            return null;
        }

        static Vector3 SpawnEye(List<string> notes)
        {
            var sp = GameObject.Find(SpawnPath);
            if (sp == null) { notes.Add("spawn " + SpawnPath + " not found; used fallback " + KH_ZFightDetector.V(SpawnFallback)); return SpawnFallback + Vector3.up * EyeHeight; }
            return sp.transform.position + Vector3.up * EyeHeight;
        }

        static bool IsStep(string name)
        {
            var n = (name ?? "").ToLowerInvariant();
            foreach (var tok in StepTokens) if (n.Contains(tok)) return true;
            return false;
        }

        static bool TextUi(Renderer r) { return r.GetComponent("TextMeshPro") != null || r.GetComponent("TMP_SubMesh") != null || r.GetComponentInParent<Canvas>() != null; }

        /// <summary>Wall top corners (min-area rectangle of the top vertices in XZ, snapped to the nearest top vertex), step-like renderers and 門枠.</summary>
        static void References(GameObject root, Vector3 eye, FarmReport rep)
        {
            if (root == null) return;
            try
            {
                Transform wall = root.transform.Find(WallRelPath);
                if (wall == null) foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == "外壁" && t.GetComponent<MeshFilter>() != null) { wall = t; break; }
                var mf = wall != null ? wall.GetComponent<MeshFilter>() : null;
                Vector3[] v; List<int[]> subs; string note;
                if (mf == null || !KH_ZFightDetector.ReadMesh(mf.sharedMesh, out v, out subs, out note)) rep.notes.Add("外壁 mesh not found/readable under " + RootPath + "/" + WallRelPath);
                else
                {
                    var w = v.Select(p => wall.TransformPoint(p)).ToArray(); float maxY = w.Max(p => p.y);
                    var top = w.Where(p => p.y >= maxY - 0.3f).ToArray();
                    float bestArea = float.MaxValue, bestAng = 0; Vector2 bMin = Vector2.zero, bMax = Vector2.zero;
                    for (float ang = 0; ang < 90f; ang += 0.5f)
                    {
                        float c = Mathf.Cos(ang * Mathf.Deg2Rad), s = Mathf.Sin(ang * Mathf.Deg2Rad); var mn = new Vector2(float.MaxValue, float.MaxValue); var mx = -mn;
                        foreach (var p in top) { var q = new Vector2(p.x * c + p.z * s, -p.x * s + p.z * c); mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q); }
                        float a = (mx.x - mn.x) * (mx.y - mn.y); if (a < bestArea) { bestArea = a; bestAng = ang; bMin = mn; bMax = mx; }
                    }
                    float cc = Mathf.Cos(bestAng * Mathf.Deg2Rad), ss = Mathf.Sin(bestAng * Mathf.Deg2Rad);
                    var corners = new List<Target>();
                    foreach (var q in new[] { new Vector2(bMin.x, bMin.y), new Vector2(bMax.x, bMin.y), new Vector2(bMax.x, bMax.y), new Vector2(bMin.x, bMax.y) })
                    {
                        var xz = new Vector2(q.x * cc - q.y * ss, q.x * ss + q.y * cc);
                        var snap = top.OrderBy(p => (new Vector2(p.x, p.z) - xz).sqrMagnitude).First();
                        corners.Add(new Target { path = KH_ZFightDetector.LeafPath(wall, root.transform), point = snap, distance = Vector3.Distance(eye, snap) });
                    }
                    corners = corners.OrderBy(t => t.distance).ToList();
                    for (int i = 0; i < corners.Count; i++) { corners[i].name = "wallcorner" + (i + 1); corners[i].boundsMin = corners[i].boundsMax = corners[i].point; }
                    rep.wallCorners = corners;
                }
                var steps = new List<Target>();
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!IsStep(r.name)) continue;
                    var b = r.bounds; var p = new Vector3(b.center.x, b.max.y, b.center.z);
                    steps.Add(new Target { name = "step", path = KH_ZFightDetector.LeafPath(r.transform, root.transform), point = p, boundsMin = b.min, boundsMax = b.max, distance = Vector3.Distance(eye, p) });
                }
                steps = steps.OrderBy(t => t.distance).ToList();
                if (steps.Count > MaxSteps) { rep.notes.Add(steps.Count + " step-like renderers; kept the " + MaxSteps + " nearest the spawn"); steps = steps.Take(MaxSteps).ToList(); }
                for (int i = 0; i < steps.Count; i++) steps[i].name = "step" + (i + 1) + "-" + KH_ZFightDetector.Ascii(steps[i].path.Split('/').Last(), 20);
                if (steps.Count == 0) rep.notes.Add("no step-like renderer names under " + RootPath + " (tokens: " + string.Join(", ", StepTokens) + ")");
                rep.steps = steps;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "門枠") continue; var r = t.GetComponent<Renderer>(); if (r == null) continue;
                    var b = r.bounds; rep.gate.Add(new Target { name = "gate", path = KH_ZFightDetector.LeafPath(t, root.transform), point = new Vector3(b.center.x, b.min.y + 0.1f, b.center.z), boundsMin = b.min, boundsMax = b.max, distance = Vector3.Distance(eye, b.center) });
                }
            }
            catch (Exception e) { rep.notes.Add("reference lookup failed: " + e.Message); Debug.LogException(e); }
        }

        // ================================================================ 1 - Scan

        [MenuItem(Menu + "1 - Scan Farmhouse")]
        public static void ScanFarmhouse()
        {
            const string tag = "[KH ZFight 1]";
            var rep = new FarmReport { utc = DateTime.UtcNow.ToString("o"), cellSize = FarmCell, minAreaCm2 = FarmMinAreaM2 * 1e4f, clusterLink = FarmClusterLink,
                thresholds = "|dot(nA,nB)| > " + KH_ZFightDetector.NormalDot + "; coplanar: max gap <= " + KH_ZFightDetector.CoplanarGap * 1000 + " mm, near: <= " + KH_ZFightDetector.NearGap * 1000 + " mm (gap measured along nA at the overlap polygon's vertices); overlap > " + FarmMinAreaM2 * 1e4f + " cm2 per triangle pair",
                scoring = "score = area_cm2 / max(distance_m, 1)^2 x (near tier 0.25) x (text/UI 0.1); back-to-back (opposite normals, both materials culled) are excluded from the ranking" };
            try
            {
                if (!Guard(tag)) return;
                var scene = SceneManager.GetActiveScene(); rep.scene = scene.path; rep.sceneDirtyBefore = scene.isDirty;
                var root = FindRoot(); rep.root = root != null ? RootPath : "(missing)";
                var eye = SpawnEye(rep.notes); rep.spawnEye = eye;
                References(root, eye, rep);
                if (root == null) { rep.notes.Add("KH_FarKominka missing; nothing scanned"); Write(rep); return; }

                // LOD > 0 renderers are not drawn with LOD 0; exclude them
                var lodSkip = new HashSet<Renderer>();
                foreach (var lg in UnityEngine.Object.FindObjectsOfType<LODGroup>())
                {
                    var lods = lg.GetLODs(); if (lods.Length < 2) continue;
                    var keep = new HashSet<Renderer>(lods[0].renderers.Where(x => x != null));
                    for (int i = 1; i < lods.Length; i++) foreach (var x in lods[i].renderers) if (x != null && !keep.Contains(x)) lodSkip.Add(x);
                }
                var inputs = new List<KH_ZFightDetector.Input>(); var mainInputs = new List<KH_ZFightDetector.Input>();
                var region = new Bounds(); bool has = false; var mainSet = new HashSet<Renderer>();
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    mainSet.Add(r);
                    if (!r.enabled || !r.gameObject.activeInHierarchy) { rep.renderersInactiveSkipped++; continue; }
                    if (lodSkip.Contains(r)) { rep.renderersLodSkipped++; continue; }
                    var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) { rep.notes.Add("no mesh: " + KH_ZFightDetector.LeafPath(r.transform, root.transform)); continue; }
                    var inp = new KH_ZFightDetector.Input { path = KH_ZFightDetector.LeafPath(r.transform, root.transform), mesh = mf.sharedMesh, toWorld = r.transform.localToWorldMatrix,
                        materials = r.sharedMaterials, textUi = TextUi(r), stepLike = IsStep(r.name) };
                    inputs.Add(inp); mainInputs.Add(inp); rep.renderersMain++;
                    if (!has) { region = r.bounds; has = true; } else region.Encapsulate(r.bounds);
                }
                region.Expand(0.01f);   // 5 mm each side
                foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                {
                    if (mainSet.Contains(r) || !r.enabled || lodSkip.Contains(r) || !r.bounds.Intersects(region)) continue;
                    var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                    inputs.Add(new KH_ZFightDetector.Input { path = KH_ZFightDetector.LeafPath(r.transform, null), mesh = mf.sharedMesh, toWorld = r.transform.localToWorldMatrix,
                        materials = r.sharedMaterials, textUi = TextUi(r), stepLike = false, clip = region });
                    rep.renderersExtra++;
                }
                Debug.Log(tag + " renderers main=" + rep.renderersMain + " extra=" + rep.renderersExtra + " inactive=" + rep.renderersInactiveSkipped + " lodSkipped=" + rep.renderersLodSkipped);

                var res = KH_ZFightDetector.Run(inputs, FarmCell, FarmMinAreaM2, FarmClusterLink, FarmBudgetSeconds, "KH ZFight: farmhouse");
                rep.parts = res.parts; rep.triangles = res.triangles; rep.oversize = res.oversize; rep.candidates = res.candidates; rep.accepted = res.accepted;
                rep.seconds = res.seconds; rep.truncated = res.truncated; rep.notes.AddRange(res.notes);

                foreach (var g in res.groups) Score(g, eye, rep);
                rep.groups = res.groups.OrderByDescending(g => g.score).ToList();
                for (int i = 0; i < rep.groups.Count; i++) rep.groups[i].rank = i + 1;
                rep.backToBackCount = res.backToBack.Count; rep.backToBackAreaCm2 = res.backToBack.Sum(g => g.areaCm2);
                foreach (var g in res.backToBack) Score(g, eye, rep);
                rep.backToBackTop = res.backToBack.OrderByDescending(g => g.score).Take(30).ToList();

                rep.terrain = KH_ZFightDetector.TerrainContact(mainInputs, Terrain.activeTerrains)
                    .Where(t => t.coplanarCm2 > 0 || t.nearCm2 > 0 || IsStep(t.part.Split('/').Last())).OrderByDescending(t => t.coplanarCm2 + t.nearCm2).ToList();
                if (Terrain.activeTerrains.Length == 0) rep.notes.Add("no active Terrain; terrain contact not checked");
            }
            catch (Exception e) { rep.truncated = true; rep.notes.Add("exception: " + e.GetType().Name + " " + e.Message); Debug.LogException(e); }
            rep.sceneDirtyAfter = SceneManager.GetActiveScene().isDirty;
            Write(rep);
            int flagged = rep.groups.Count(g => g.flags != "");
            Debug.Log(tag + " groups=" + rep.groups.Count + " (coplanar " + rep.groups.Count(g => g.tier == "coplanar") + ", flagged " + flagged + ") backToBack=" + rep.backToBackCount
                + " tris=" + rep.triangles + " candidates=" + rep.candidates + " " + rep.seconds.ToString("F1") + " s truncated=" + rep.truncated + " dirty " + rep.sceneDirtyBefore + "->" + rep.sceneDirtyAfter + " -> " + OutDir + "/farmhouse.md");
        }

        static void Score(KH_ZFightDetector.Group g, Vector3 eye, FarmReport rep)
        {
            g.distance = Vector3.Distance(eye, g.centre);
            float d = Mathf.Max(g.distance, 1f);
            g.score = g.areaCm2 / (d * d) * (g.tier == "near" ? 0.25f : 1f) * (g.textUiInvolved ? 0.1f : 1f);
            var flags = new List<string>();
            foreach (var c in rep.wallCorners) if (Vector3.Distance(c.point, g.centre) <= FlagRadius || DistToBox(c.point, g.boundsMin, g.boundsMax) <= 0.25f) flags.Add(c.name);
            bool step = rep.parts[g.partIndexA].stepLike || rep.parts[g.partIndexB].stepLike;
            foreach (var s in rep.steps) if (DistToBox(g.centre, s.boundsMin, s.boundsMax) <= FlagRadius) step = true;
            if (step) flags.Add("steps");
            foreach (var s in rep.gate) if (DistToBox(g.centre, s.boundsMin, s.boundsMax) <= FlagRadius) { flags.Add("gate"); break; }
            g.flags = string.Join(",", flags);
        }

        static float DistToBox(Vector3 p, Vector3 mn, Vector3 mx) { return Vector3.Distance(p, Vector3.Max(mn, Vector3.Min(mx, p))); }

        static void Write(FarmReport rep)
        {
            try
            {
                Directory.CreateDirectory(OutDir);
                File.WriteAllText(OutDir + "/farmhouse.json", JsonUtility.ToJson(rep, true));
                var sb = new StringBuilder();
                sb.AppendLine("# ZFight scan: farmhouse (" + rep.utc + ")").AppendLine();
                sb.AppendLine("- scene `" + rep.scene + "` dirty " + rep.sceneDirtyBefore + " -> " + rep.sceneDirtyAfter + ", root `" + rep.root + "`, spawn eye (" + KH_ZFightDetector.V(rep.spawnEye) + ")");
                sb.AppendLine("- renderers: main " + rep.renderersMain + ", extra (bounds meet the farmhouse) " + rep.renderersExtra + ", inactive skipped " + rep.renderersInactiveSkipped + ", LOD>0 skipped " + rep.renderersLodSkipped);
                sb.AppendLine("- triangles " + rep.triangles + ", oversize " + rep.oversize + ", candidate pairs " + rep.candidates + ", accepted " + rep.accepted + ", " + rep.seconds.ToString("F1") + " s, truncated " + rep.truncated);
                sb.AppendLine("- thresholds: " + rep.thresholds);
                sb.AppendLine("- " + rep.scoring);
                sb.AppendLine("- flags: wallcornerN = within " + FlagRadius + " m of the Nth top corner of 外壁 (1 = nearest the spawn); steps = within " + FlagRadius + " m of a step-like renderer or one of the pair is step-like; gate = within " + FlagRadius + " m of 門枠").AppendLine();
                sb.AppendLine("## Reference targets").AppendLine().AppendLine("| name | path | point | dist m |").AppendLine("|---|---|---|---|");
                foreach (var t in rep.wallCorners.Concat(rep.steps).Concat(rep.gate)) sb.AppendLine("| " + t.name + " | " + KH_ZFightDetector.Md(t.path) + " | " + KH_ZFightDetector.V(t.point) + " | " + t.distance.ToString("F1") + " |");
                sb.AppendLine().AppendLine("## Overlap groups (ranked, top 80 of " + rep.groups.Count + ")").AppendLine();
                GroupTable(sb, rep.groups.Take(80), true);
                var flagged = rep.groups.Where(g => g.flags != "").ToList();
                sb.AppendLine().AppendLine("## Flagged groups (" + flagged.Count + ")").AppendLine();
                GroupTable(sb, flagged.Take(60), true);
                sb.AppendLine().AppendLine("## Terrain contact (upward faces of KH_FarKominka vs Terrain; gap = face - terrain)").AppendLine();
                sb.AppendLine("| part | terrain | up area cm2 | coplanar (<=3 mm) cm2 | near (<=20 mm or crossing) cm2 | buried cm2 | gap min..max mm | centre |").AppendLine("|---|---|---|---|---|---|---|---|");
                foreach (var t in rep.terrain.Take(60)) sb.AppendLine("| " + KH_ZFightDetector.Md(t.part) + " | " + t.terrain + " | " + t.upAreaCm2.ToString("F0") + " | " + t.coplanarCm2.ToString("F0") + " | " + t.nearCm2.ToString("F0") + " | " + t.buriedCm2.ToString("F0") + " | " + t.minGapMm.ToString("F0") + ".." + t.maxGapMm.ToString("F0") + " | " + KH_ZFightDetector.V(t.centre) + " |");
                sb.AppendLine().AppendLine("## Back-to-back (opposite normals, both culled; not z-fighting unless a material is Cull Off): " + rep.backToBackCount + " groups, " + rep.backToBackAreaCm2.ToString("F0") + " cm2. Top 30:").AppendLine();
                GroupTable(sb, rep.backToBackTop, false);
                var bad = rep.parts.Where(p => !p.readable).ToList();
                if (bad.Count > 0) { sb.AppendLine().AppendLine("## Unreadable meshes (skipped)").AppendLine(); foreach (var p in bad) sb.AppendLine("- " + p.path + ": " + p.note); }
                sb.AppendLine().AppendLine("## Notes").AppendLine(); foreach (var n in rep.notes) sb.AppendLine("- " + n);
                File.WriteAllText(OutDir + "/farmhouse.md", sb.ToString());
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        static void GroupTable(StringBuilder sb, IEnumerable<KH_ZFightDetector.Group> groups, bool withDistance)
        {
            sb.AppendLine("| rank | tier | flags | renderer A (sub) | renderer B (sub) | material A / B | relation | area cm2 | centre xyz | " + (withDistance ? "dist m | " : "") + "gap max / mean mm | notes |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|" + (withDistance ? "---|" : "") + "---|---|");
            foreach (var g in groups)
                sb.AppendLine("| " + g.rank + " | " + g.tier + (g.facing == "opposite" ? " (opp)" : "") + " | " + g.flags + " | " + KH_ZFightDetector.Md(g.partA) + " (" + g.subA + ") | " + KH_ZFightDetector.Md(g.partB) + " (" + g.subB + ") | "
                    + KH_ZFightDetector.Md(g.materialA) + " / " + KH_ZFightDetector.Md(g.materialB) + " | " + g.relation + " | " + g.areaCm2.ToString("F1") + " | " + KH_ZFightDetector.V(g.centre) + " | "
                    + (withDistance ? g.distance.ToString("F1") + " | " : "") + g.gapMaxMm.ToString("F1") + " / " + g.gapMeanMm.ToString("F1") + " | " + g.notes + (g.capped ? " (capped)" : "") + " |");
        }

        // ================================================================ 2 - Capture

        [Serializable] public class ShotRecord
        { public string name, file, diffFile, target, occluder, error; public Vector3 eye, aim; public float fov, near, far, changedFraction; }

        [Serializable] public class CaptureReport
        { public string utc, scene, cameraNote; public bool sceneDirtyBefore, sceneDirtyAfter, usedScan; public List<ShotRecord> shots = new List<ShotRecord>(); public List<string> notes = new List<string>(); }

        /// <summary>Player-like camera: copies the Reference Camera, then near = clamp(ref near, 0.001, 0.05), far = ref far (1000 without one).</summary>
        public static Camera PlayerCamera(out float near, out float far, out string note)
        {
            var go = new GameObject("KH_ZFightCapture") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>(); near = 0.05f; far = 1000f; note = "";
            var desc = UnityEngine.Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
            var refCam = desc != null && desc.ReferenceCamera != null ? desc.ReferenceCamera.GetComponent<Camera>() : null;
            if (refCam != null)
            {
                cam.CopyFrom(refCam); near = Mathf.Clamp(refCam.nearClipPlane, 0.001f, 0.05f); far = refCam.farClipPlane;
                note = "Reference Camera near " + refCam.nearClipPlane + " -> " + near + " (VRChat clamps near to 0.001-0.05, camera-design.md:44), far " + far + " (from Reference Camera)";
            }
            else note = "no Reference Camera; used near 0.05 / far 1000 / FOV 60 defaults";
            cam.enabled = false; cam.cameraType = CameraType.Game; cam.stereoTargetEye = StereoTargetEyeMask.None; cam.targetTexture = null;
            cam.nearClipPlane = near; cam.farClipPlane = far; cam.allowHDR = false; cam.allowMSAA = false; cam.useOcclusionCulling = false; cam.fieldOfView = 60f;
            note += "; Editor render (DX11, reversed-Z, 24-bit depth RT, MSAA off). A still cannot show flicker; the -diff maps mark pixels that change when the camera moves 1 mm along its view axis.";
            return cam;
        }

        [MenuItem(Menu + "2 - Capture Farmhouse")]
        public static void CaptureFarmhouse()
        {
            const string tag = "[KH ZFight 2]";
            var rep = new CaptureReport { utc = DateTime.UtcNow.ToString("o") };
            Camera cam = null;
            try
            {
                if (!Guard(tag)) return;
                var scene = SceneManager.GetActiveScene(); rep.scene = scene.path; rep.sceneDirtyBefore = scene.isDirty;
                var root = FindRoot(); var eye = SpawnEye(rep.notes);
                FarmReport scan = null;
                try { if (File.Exists(OutDir + "/farmhouse.json")) scan = JsonUtility.FromJson<FarmReport>(File.ReadAllText(OutDir + "/farmhouse.json")); }
                catch (Exception e) { rep.notes.Add("farmhouse.json unreadable: " + e.Message); }
                rep.usedScan = scan != null;
                var refs = new FarmReport(); References(root, eye, refs); rep.notes.AddRange(refs.notes);

                var targets = new List<KeyValuePair<string, Vector3>>(); var describe = new Dictionary<string, string>();
                if (scan != null)
                {
                    var pick = scan.groups.Where(g => g.flags != "").Take(MaxNarrowGroups).ToList();
                    if (pick.Count == 0) { rep.notes.Add("no flagged groups in farmhouse.json; aimed at the top 6 ranked groups within 40 m instead"); pick = scan.groups.Where(g => g.distance < 40f).Take(6).ToList(); }
                    foreach (var g in pick)
                    {
                        string n = "g" + g.rank.ToString("00") + (g.flags != "" ? "-" + KH_ZFightDetector.Ascii(g.flags.Replace(",", "-"), 30) : "");
                        targets.Add(new KeyValuePair<string, Vector3>(n, g.centre));
                        describe[n] = "group " + g.rank + " " + g.tier + " " + g.partA + "#" + g.subA + " (" + g.materialA + ") x " + g.partB + "#" + g.subB + " (" + g.materialB + ") " + g.areaCm2.ToString("F1") + " cm2";
                    }
                }
                else rep.notes.Add("farmhouse.json not found; reference targets only");
                foreach (var t in refs.wallCorners.Take(2).Concat(refs.steps.Take(4)).Concat(refs.gate.Take(1)))
                { string n = "ref-" + t.name; targets.Add(new KeyValuePair<string, Vector3>(n, t.point)); describe[n] = t.path; }

                float near, far; string camNote; cam = PlayerCamera(out near, out far, out camNote); rep.cameraNote = camNote;
                Directory.CreateDirectory(EvidenceDir); Directory.CreateDirectory(FlickerDir);
                // wide shots
                Shoot(rep, cam, "spawn-wide", eye, Quaternion.Euler(0f, WideYaw, 0f), WideFov, near, far, "yaw " + WideYaw + " toward the gate / farmhouse");
                Shoot(rep, cam, "spawn-forward", eye, Quaternion.Euler(0f, SpawnYaw(), 0f), WideFov, near, far, "spawn facing");
                Shoot(rep, cam, "spawn-wide-near030", eye, Quaternion.Euler(0f, WideYaw, 0f), WideFov, 0.3f, far, "control: Reference Camera near 0.3 unclamped");
                foreach (var kv in targets)
                {
                    var dir = kv.Value - eye; if (dir.sqrMagnitude < 1e-4f) continue;
                    Shoot(rep, cam, kv.Key, eye, Quaternion.LookRotation(dir), NarrowFov, near, far, describe[kv.Key], kv.Value);
                    var flat = new Vector3(dir.x, 0f, dir.z); float travel = Mathf.Min(CloserBy, flat.magnitude - 0.5f);
                    if (travel > 0.1f)
                    {
                        var e2 = eye + flat.normalized * travel;
                        Shoot(rep, cam, kv.Key + "-closer", e2, Quaternion.LookRotation(kv.Value - e2), NarrowFov, near, far, describe[kv.Key] + " (" + travel.ToString("F1") + " m closer)", kv.Value);
                    }
                }
            }
            catch (Exception e) { rep.notes.Add("exception: " + e.GetType().Name + " " + e.Message); Debug.LogException(e); }
            finally { if (cam != null) UnityEngine.Object.DestroyImmediate(cam.gameObject); }
            rep.sceneDirtyAfter = SceneManager.GetActiveScene().isDirty;
            WriteCapture(rep, "farmhouse-capture");
            Debug.Log(tag + " shots=" + rep.shots.Count + " errors=" + rep.shots.Count(s => s.error != null) + " usedScan=" + rep.usedScan + " dirty " + rep.sceneDirtyBefore + "->" + rep.sceneDirtyAfter + " -> " + EvidenceDir + ", " + OutDir + "/farmhouse-capture.md");
        }

        static float SpawnYaw() { var sp = GameObject.Find(SpawnPath); return sp != null ? sp.transform.eulerAngles.y : 352f; }

        static void Shoot(CaptureReport rep, Camera cam, string name, Vector3 eye, Quaternion rot, float fov, float near, float far, string target, Vector3? aim = null)
        {
            var rec = new ShotRecord { name = name, eye = eye, aim = aim ?? (eye + rot * Vector3.forward * 10f), fov = fov, near = near, far = far, target = target };
            try
            {
                cam.transform.SetPositionAndRotation(eye, rot); cam.fieldOfView = fov; cam.nearClipPlane = near; cam.farClipPlane = far;
                if (aim.HasValue)
                {
                    var d = aim.Value - eye; RaycastHit hit;
                    if (Physics.Raycast(eye, d.normalized, out hit, Mathf.Max(0f, d.magnitude - 0.3f), ~0, QueryTriggerInteraction.Ignore))
                        rec.occluder = KH_ZFightDetector.LeafPath(hit.collider.transform, null) + " at " + hit.distance.ToString("F1") + " m";
                }
                var shot = KH_ZFightDetector.Render(cam, EvidenceDir + "/" + name + ".jpg", FlickerDir + "/" + name + "-diff.jpg");
                rec.file = shot.file; rec.diffFile = shot.diffFile; rec.changedFraction = shot.changedFraction; rec.error = shot.error;
            }
            catch (Exception e) { rec.error = e.Message; }
            rep.shots.Add(rec);
        }

        static void WriteCapture(CaptureReport rep, string baseName)
        {
            try
            {
                Directory.CreateDirectory(OutDir);
                File.WriteAllText(OutDir + "/" + baseName + ".json", JsonUtility.ToJson(rep, true));
                var sb = new StringBuilder();
                sb.AppendLine("# ZFight capture: " + baseName + " (" + rep.utc + ")").AppendLine();
                sb.AppendLine("- scene `" + rep.scene + "` dirty " + rep.sceneDirtyBefore + " -> " + rep.sceneDirtyAfter + ", used scan " + rep.usedScan);
                sb.AppendLine("- camera: " + rep.cameraNote);
                sb.AppendLine("- images 1600x900 rendered, saved 800x450 JPEG (q85); change maps in " + FlickerDir + " (red = differs by > 24/255 after a 1 mm move along the view axis; animated shaders such as water, foliage sway or scrolling textures also turn red because _Time advances between the two renders, and silhouette edges can show a thin line)").AppendLine();
                sb.AppendLine("| file | target | eye | fov | near | changed % | occluder (collider ray) | error |").AppendLine("|---|---|---|---|---|---|---|---|");
                foreach (var s in rep.shots)
                    sb.AppendLine("| " + Path.GetFileName(s.file ?? s.name) + " | " + KH_ZFightDetector.Md(s.target) + " | " + KH_ZFightDetector.V(s.eye) + " | " + s.fov + " | " + s.near + " | " + (s.changedFraction * 100f).ToString("F2") + " | " + KH_ZFightDetector.Md(s.occluder) + " | " + s.error + " |");
                sb.AppendLine().AppendLine("## Notes").AppendLine(); foreach (var n in rep.notes) sb.AppendLine("- " + n);
                File.WriteAllText(OutDir + "/" + baseName + ".md", sb.ToString());
            }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
