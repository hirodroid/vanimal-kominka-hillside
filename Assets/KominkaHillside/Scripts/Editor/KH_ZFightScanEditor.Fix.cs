using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // BUG-ENV-003: fix for the farmhouse exterior z-fighting. Works on the world-side copy KH_World/KH_Places/KH_FarKominka only;
    // the purchased prefabs / FBX / meshes are never written (derived copies go to Assets/KominkaHillside/Meshes/FarKominka/).
    //   ZFight/5a - Plan Farmhouse Fix (dry run)  read-only; Temp/KH_ZFightScan/fix-plan.md + .json
    //   ZFight/5  - Apply Farmhouse Fix           writes derived mesh assets + JSON sidecars, overrides MeshFilter.sharedMesh on the
    //                                             prefab-instance children, marks the scene dirty (never saves); Temp/KH_ZFightScan/fix.md
    //   ZFight/6  - Verify Farmhouse Fix          plan == assigned meshes, colliders untouched, detector (menu 1 settings) clean within 9 m
    // Order for a fresh check: 5 -> 6 -> 1 (re-scan) -> 2 (captures).
    //
    // Method (one mechanism for every pair, decided from the mesh data at run time):
    //   component = connected piece of one renderer's SOURCE mesh (vertices welded at 0.01 mm, across all submeshes).
    //   pair      = same-facing faces (|dot| > 0.999) of two different components overlapping by > 1 cm2 within 20 mm of each other.
    //               Each pair keeps its "sheets": the signed separations s (B relative to A along A's normal) of its overlapping faces.
    //   seed      = pair with a sheet inside +-4 mm whose coplanar (<= 3 mm, detector tier) overlap within 10 m of the spawn eye is >= 5 cm2.
    //   offsets   = per component o >= 0 (world m), solved by monotone relaxation so that every sheet of every seed ends >= 4 mm apart
    //               (|s + o_B - o_A| >= 4 mm); a pair that touches a moved component is held to the same rule, so moving one piece never
    //               creates a new fight (e.g. the doors 5.6 mm above the paving). Seeds are admitted nearest-first; a seed whose
    //               solve needs > 15 mm, > 80 moved components or does not converge is dropped (listed as DROPPED, and 6 then
    //               FAILs on it if it is within 9 m), so a far cascade never cancels the near fixes. Solved to 4.1 mm internally.
    //   inflation = a moved component is inflated: each welded vertex moves by d with n_i . d = o for the distinct face normals n_i
    //               around it (least squares), so every face of the piece moves outward by o along its own normal. The piece is not
    //               stitched to anything, so no crack opens; it grows by o (typically 4-8 mm) and its hidden faces sink o deeper into
    //               what they touch. Vertex and triangle counts, UVs (incl. UV2 for lightmaps), normals, tangents, colours, submeshes
    //               and material slots are copied unchanged.
    //   front     = which side of a fighting pair moves outward: a side whose material name contains 瓦 (tile caps sit on walls)
    //               > the side needing > 1 mm less offset > the smaller component > the component with fewer other pairs > hierarchy.
    //   Not used: deleting hidden triangles (no seed face is both fully hidden and meant to be hidden: the 瓦 gable end is covered by
    //   the wall end but is the face that should show, the wall end and the paving sides are only partly covered, so removal would
    //   need re-triangulation); moving a whole renderer (a translation separates faces on one axis only and slides the others; the
    //   gate posts share faces with the paving on two sides). Coplanar faces inside ONE component cannot be separated by inflation:
    //   they are listed in the report and left alone.
    // Invisible from any standing eye: faces with n.y < -0.7 whose overlap is below the spawn eye (wall bottoms; culled from above).
    // They are not seeds here and not FAILs in 6 (same rule, listed as excluded).
    // Depth precision: a perspective depth buffer with b bits resolves about z^2 / (near * 2^b) at distance z (far >> near). With the
    // VRChat near plane clamped to <= 0.05 m (camera-design.md:44) and 24-bit depth: 3 m -> 0.01 mm, 8 m -> 0.08 mm, so a 4 mm
    // separation has >= 50x margin at 3-8 m. Assumptions: 24-bit depth on Quest and near ~0.05 m; with a 16-bit buffer (~20 mm at 8 m)
    // or a 0.001 m near (~3.8 mm at 8 m) 4 mm would not be enough at 8 m. The 0.0 mm groups fight on any buffer because the two faces'
    // interpolated depths differ only by rounding.
    // MeshColliders are never touched: physics and the Guide/1 board raycasts keep using the original meshes.
    public static partial class KH_ZFightScanEditor
    {
        const string FixMeshesParent = "Assets/KominkaHillside/Meshes";
        const string FixDir = FixMeshesParent + "/FarKominka";
        const string FixVersion = "kh-zfix-1";
        const string FixFrontToken = "瓦";
        const float FixTarget = 0.004f, FixSeedGap = 0.003f, FixWindow = 0.020f, FixSeedRadius = 10f, FixVerifyRadius = 9f;
        const float FixSolveTarget = FixTarget + 0.0001f;   // solve to 4.1 mm so float noise and the 1/(1+lambda) shrink never land a fixed pair below 4 mm
        const float FixSeedMinAreaM2 = 5e-4f /* 5 cm2 */, FixPairMinAreaM2 = 1e-4f /* 1 cm2, as menu 1 */, FixVerifyMinAreaCm2 = 20f;
        const float FixMaxOffset = 0.015f, FixLambda = 1e-4f, FixSpike = 3f, FixDownNormalY = -0.7f, FixSheetMerge = 0.0005f, FixEps = 1e-6f;
        const int FixMaxMoved = 80, FixMaxPasses = 5000;

        // ================================================================ planner types

        class FxRend
        {
            public int index; public Renderer r; public MeshFilter mf; public Mesh source;
            public string path, rel, sourceHow, sourcePath, sourceGuid; public long sourceLocalId;
            public Matrix4x4 l2w; public bool movable = true;
            public Vector3[] v; public List<int[]> subs; public int[] weld, compOfWeld; public string[] mats;
        }

        class FxComp { public int index, rend, local, tris; public float area, offset; public HashSet<int> subs = new HashSet<int>(), partners = new HashSet<int>(); }

        struct FxTri { public int rend, comp, sub, i0, i1, i2; public Vector3 a, b, c, n, min, max; public float area; }

        class FxSheet { public float lo, hi, area; }

        class FxPair
        {
            public int a, b, front = -1; public float area, seedArea, minDist = float.MaxValue;
            public Vector3 wc, nsum, bmin = Vector3.one * float.MaxValue, bmax = Vector3.one * float.MinValue;
            public bool fight, seed; public string why = "", dropped;
            public List<FxSheet> raw = new List<FxSheet>(), sheets = new List<FxSheet>();
            public HashSet<int> trisA = new HashSet<int>(), trisB = new HashSet<int>(), subsA = new HashSet<int>(), subsB = new HashSet<int>();
            public Vector3 Centre { get { return area > 0 ? wc / area : Vector3.zero; } }
        }

        class FxIntra { public int comp, subA, subB; public float area, sMin = float.MaxValue, sMax = float.MinValue; public Vector3 wc; }

        class FxPlan
        {
            public GameObject root; public Vector3 eye; public bool ok = true; public int passes, oversize; public long candidates;
            public List<FxRend> rends = new List<FxRend>(); public List<FxComp> comps = new List<FxComp>(); public List<FxTri> tris = new List<FxTri>();
            public List<FxPair> pairs = new List<FxPair>(); public Dictionary<int, FxIntra> intra = new Dictionary<int, FxIntra>();
            public List<string> errors = new List<string>(), notes = new List<string>();
            public Dictionary<int, string> assetName = new Dictionary<int, string>();   // renderer index -> derived base name (no extension)
        }

        // ================================================================ report types

        [Serializable] public class FixPairRow
        {
            public string scanRanks, rendererA, rendererB, materialsA, materialsB, relation, moved, why, sheetsBeforeMm;
            public int componentA, componentB; public bool seed, fight, separated;
            public float areaCm2, seedAreaCm2, distance, closestBeforeMm, closestAfterMm, coverAByBPct, coverBByAPct, offsetAMm, offsetBMm;
            public Vector3 centre, normal;
        }

        [Serializable] public class FixRendRow
        { public string renderer, sourceMesh, sourceHow, derived, action, offsetsMm; public int components, movedComponents, vertices, verticesMoved, triangles; public float maxVertexMoveMm; }

        [Serializable] public class FixReport
        {
            public string utc, scene, mode, status, method, depthNote, thresholds; public bool sceneDirtyBefore, sceneDirtyAfter; public Vector3 eye;
            public int renderers, components, triangles, pairs, seeds, droppedSeeds, activePairs, movedComponents, passes, unfixedFights; public long candidates;
            public List<FixPairRow> seedRows = new List<FixPairRow>(), cascadeRows = new List<FixPairRow>(), unfixedRows = new List<FixPairRow>();
            public List<FixRendRow> renderersChanged = new List<FixRendRow>();
            public List<string> intraComponent = new List<string>(), copies = new List<string>(), errors = new List<string>(), notes = new List<string>();
        }

        [Serializable] public class FixSidecar
        {
            public string version, renderer, sourceName, sourcePath, sourceGuid, planHash, utc; public long sourceLocalId;
            public int sourceVertices, sourceTriangles, derivedVertices, derivedTriangles; public List<string> components = new List<string>();
        }

        [Serializable] public class FixVerifyReport
        {
            public string utc, scene, status; public Vector3 eye; public bool truncated, sceneDirtyBefore, sceneDirtyAfter;
            public int derivedChecked, collidersChecked, detectorGroups;
            public List<string> failures = new List<string>(), excluded = new List<string>(), outOfScope = new List<string>(), nearInfo = new List<string>(), notes = new List<string>();
        }

        // ================================================================ menus

        [MenuItem(Menu + "5 - Apply Farmhouse Fix")]
        public static void ApplyFarmhouseFix() { FxRun(true); }

        [MenuItem(Menu + "5a - Plan Farmhouse Fix (dry run)")]
        public static void PlanFarmhouseFix() { FxRun(false); }

        static void FxRun(bool apply)
        {
            string tag = apply ? "[KH ZFight 5]" : "[KH ZFight 5a]", baseName = apply ? "fix" : "fix-plan";
            var rep = new FixReport { utc = DateTime.UtcNow.ToString("o"), mode = apply ? "apply" : "plan only (nothing written to the scene or assets)",
                method = "inflate whole connected components (see the header of KH_ZFightScanEditor.Fix.cs)",
                thresholds = "target separation " + FixTarget * 1000 + " mm; seed = sheet within +-" + FixTarget * 1000 + " mm with coplanar (<= " + FixSeedGap * 1000 + " mm) overlap >= " + FixSeedMinAreaM2 * 1e4f + " cm2 within " + FixSeedRadius + " m of the spawn eye; pair window " + FixWindow * 1000 + " mm; |dot| > " + KH_ZFightDetector.NormalDot + "; max offset " + FixMaxOffset * 1000 + " mm; excluded: n.y < " + FixDownNormalY + " below the eye",
                depthNote = "24-bit depth, near <= 0.05 m: step ~ z^2/(near*2^24) = 0.08 mm at 8 m, so 4 mm is >= 50x margin; not valid for a 16-bit buffer or near 0.001 m at 8 m" };
            FxPlan plan = null; bool sceneChanged = false;
            try
            {
                if (!Guard(tag)) return;
                var scene = SceneManager.GetActiveScene(); rep.scene = scene.path; rep.sceneDirtyBefore = scene.isDirty;
                var root = FindRoot();
                if (root == null) rep.errors.Add(RootPath + " not found; nothing done");
                else
                {
                    plan = FxBuildPlan(root);
                    FxFillReport(plan, rep);
                    if (!plan.ok) rep.errors.Add("plan failed; nothing applied");
                    else if (apply) sceneChanged = FxApply(plan, rep);
                    else FxCopies(plan, rep, false);
                }
                if (sceneChanged) EditorSceneManager.MarkSceneDirty(scene);
            }
            catch (Exception e) { rep.errors.Add("exception: " + e.GetType().Name + " " + e.Message); Debug.LogException(e); }
            rep.sceneDirtyAfter = SceneManager.GetActiveScene().isDirty;
            rep.status = rep.errors.Count == 0 && plan != null && plan.ok ? "OK" : "FAIL";
            FxWrite(rep, baseName);
            string line = tag + " " + (apply ? "ZFIGHT_FIX_APPLY_" : "ZFIGHT_FIX_PLAN_") + rep.status + " seeds=" + rep.seeds + " dropped=" + rep.droppedSeeds + " activePairs=" + rep.activePairs + " moved=" + rep.movedComponents
                + " renderers=" + rep.renderersChanged.Count + " sceneChanged=" + sceneChanged + " dirty " + rep.sceneDirtyBefore + "->" + rep.sceneDirtyAfter + " -> " + OutDir + "/" + baseName + ".md";
            if (rep.status == "OK") Debug.Log(line); else Debug.LogError(line + " errors: " + string.Join(" / ", rep.errors.Take(4)));
        }

        // ================================================================ planner

        static FxPlan FxBuildPlan(GameObject root)
        {
            var plan = new FxPlan { root = root }; plan.eye = SpawnEye(plan.notes);
            try
            {
                var lodSkip = FxLodSkip(); bool doubleSided = false;
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy || lodSkip.Contains(r)) continue;
                    var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                    var fr = new FxRend { r = r, mf = mf, path = KH_ZFightDetector.LeafPath(r.transform, root.transform), rel = FxRel(r.transform, root.transform), l2w = r.transform.localToWorldMatrix };
                    string how; fr.source = FxSourceMesh(mf, out how); fr.sourceHow = how;
                    if (fr.source == null) { plan.ok = false; plan.errors.Add("source mesh not found for " + fr.path + " (" + how + ")"); continue; }
                    fr.sourcePath = AssetDatabase.GetAssetPath(fr.source); string g; long id;
                    if (string.IsNullOrEmpty(fr.sourcePath) || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(fr.source, out g, out id)) { fr.movable = false; plan.notes.Add("mesh is not an asset; kept as is: " + fr.path); }
                    else { fr.sourceGuid = g; fr.sourceLocalId = id; }
                    if (fr.source.bindposes.Length > 0 || fr.source.blendShapeCount > 0) { fr.movable = false; plan.notes.Add("skinned / blend-shape mesh; kept as is: " + fr.path); }
                    if (TextUi(r)) fr.movable = false;
                    string note;
                    if (!KH_ZFightDetector.ReadMesh(fr.source, out fr.v, out fr.subs, out note)) { plan.notes.Add("unreadable, skipped: " + fr.path + " " + note); continue; }
                    var mats = r.sharedMaterials; fr.mats = new string[Math.Max(fr.subs.Count, 1)];
                    for (int s = 0; s < fr.mats.Length; s++)
                    {
                        var m = mats.Length > 0 ? mats[Math.Min(s, mats.Length - 1)] : null; fr.mats[s] = m != null ? m.name : "(none)";
                        if (m != null && KH_ZFightDetector.Describe(m).doubleSided) doubleSided = true;
                    }
                    fr.index = plan.rends.Count; plan.rends.Add(fr);
                    FxComponents(plan, fr);
                }
                plan.notes.Add(doubleSided ? "a farmhouse material is Cull Off: opposite-facing pairs are NOT handled by this fix (check menu 1's back-to-back list)"
                                           : "opposite-facing pairs ignored: every farmhouse material is culled (back-to-back faces do not fight)");
                FxFindPairs(plan);
                FxDecide(plan);
                FxSolve(plan);
                FxNames(plan);
            }
            catch (Exception e) { plan.ok = false; plan.errors.Add("planner exception: " + e.GetType().Name + " " + e.Message); Debug.LogException(e); }
            finally { EditorUtility.ClearProgressBar(); }
            return plan;
        }

        static HashSet<Renderer> FxLodSkip()
        {
            var skip = new HashSet<Renderer>();
            foreach (var lg in UnityEngine.Object.FindObjectsOfType<LODGroup>())
            {
                var lods = lg.GetLODs(); if (lods.Length < 2) continue;
                var keep = new HashSet<Renderer>(lods[0].renderers.Where(x => x != null));
                for (int i = 1; i < lods.Length; i++) foreach (var x in lods[i].renderers) if (x != null && !keep.Contains(x)) skip.Add(x);
            }
            return skip;
        }

        static string FxRel(Transform t, Transform root)
        {
            var names = new List<string>();
            for (var x = t; x != null && x != root; x = x.parent) names.Add(x.name);
            names.Reverse(); return string.Join("/", names);
        }

        static bool FxIsDerived(Mesh m)
        {
            if (m == null) return false;
            var p = AssetDatabase.GetAssetPath(m);
            return !string.IsNullOrEmpty(p) && p.StartsWith(FixDir + "/", StringComparison.Ordinal);
        }

        static string FxSidecarPath(string assetPath) { return Path.ChangeExtension(assetPath, ".json").Replace('\\', '/'); }

        static FixSidecar FxReadSidecar(string assetPath)
        {
            try { var p = FxSidecarPath(assetPath); return File.Exists(p) ? JsonUtility.FromJson<FixSidecar>(File.ReadAllText(p)) : null; }
            catch (Exception e) { Debug.LogWarning("[KH ZFight] sidecar unreadable for " + assetPath + ": " + e.Message); return null; }
        }

        /// <summary>The mesh to plan from: the current one unless it is one of ours; then the prefab's, then the sidecar's source.</summary>
        static Mesh FxSourceMesh(MeshFilter mf, out string how)
        {
            how = "current"; var cur = mf.sharedMesh;
            if (cur == null || !FxIsDerived(cur)) return cur;
            var pf = PrefabUtility.GetCorrespondingObjectFromSource(mf);
            if (pf != null && pf.sharedMesh != null && !FxIsDerived(pf.sharedMesh)) { how = "prefab source (current: " + cur.name + ")"; return pf.sharedMesh; }
            var side = FxReadSidecar(AssetDatabase.GetAssetPath(cur));
            if (side != null && !string.IsNullOrEmpty(side.sourceGuid))
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(side.sourceGuid)))
                {
                    var m = o as Mesh; string g; long id;
                    if (m != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out g, out id) && id == side.sourceLocalId) { how = "sidecar source (current: " + cur.name + ")"; return m; }
                }
            how = "unresolved: " + cur.name + " is derived but its source was not found"; return null;
        }

        static bool FxIn(int[] idx, int t, int n) { return idx[t] >= 0 && idx[t + 1] >= 0 && idx[t + 2] >= 0 && idx[t] < n && idx[t + 1] < n && idx[t + 2] < n; }

        static void FxComponents(FxPlan plan, FxRend fr)
        {
            var v = fr.v; fr.weld = new int[v.Length]; var map = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < v.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(v[i].x * 1e5f), Mathf.RoundToInt(v[i].y * 1e5f), Mathf.RoundToInt(v[i].z * 1e5f));
                int id; if (!map.TryGetValue(key, out id)) { id = map.Count; map[key] = id; } fr.weld[i] = id;
            }
            var uf = new int[map.Count]; for (int i = 0; i < uf.Length; i++) uf[i] = i;
            foreach (var idx in fr.subs)
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    if (!FxIn(idx, t, v.Length)) continue;
                    FxUnion(uf, fr.weld[idx[t]], fr.weld[idx[t + 1]]); FxUnion(uf, fr.weld[idx[t]], fr.weld[idx[t + 2]]);
                }
            var compOfRoot = new Dictionary<int, int>(); bool flip = fr.l2w.determinant < 0f;
            for (int s = 0; s < fr.subs.Count; s++)
            {
                var idx = fr.subs[s];
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    if (!FxIn(idx, t, v.Length)) continue;
                    int i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2], rootId = FxFind(uf, fr.weld[i0]);
                    int ci; if (!compOfRoot.TryGetValue(rootId, out ci)) { ci = plan.comps.Count; compOfRoot[rootId] = ci; plan.comps.Add(new FxComp { index = ci, rend = fr.index, local = compOfRoot.Count - 1 }); }
                    var comp = plan.comps[ci]; comp.tris++; comp.subs.Add(s);
                    Vector3 a = fr.l2w.MultiplyPoint3x4(v[i0]), b = fr.l2w.MultiplyPoint3x4(v[i1]), c = fr.l2w.MultiplyPoint3x4(v[i2]);
                    var cr = Vector3.Cross(b - a, c - a); float len = cr.magnitude; if (len < 1e-10f) continue;
                    comp.area += len * .5f;
                    var tr = new FxTri { rend = fr.index, comp = ci, sub = s, i0 = i0, i1 = i1, i2 = i2, a = a, b = b, c = c, n = (flip ? -cr : cr) / len, area = len * .5f };
                    tr.min = Vector3.Min(a, Vector3.Min(b, c)); tr.max = Vector3.Max(a, Vector3.Max(b, c));
                    plan.tris.Add(tr);
                }
            }
            fr.compOfWeld = new int[map.Count];
            for (int w = 0; w < fr.compOfWeld.Length; w++) { int ci; fr.compOfWeld[w] = compOfRoot.TryGetValue(FxFind(uf, w), out ci) ? ci : -1; }
        }

        static void FxFindPairs(FxPlan plan)
        {
            var tris = plan.tris; float inv = 1f / FarmCell, e = FixWindow;
            var grid = new Dictionary<long, List<int>>(); var oversize = new List<int>();
            for (int i = 0; i < tris.Count; i++)
            {
                Vector3Int lo = FxCell(tris[i].min - Vector3.one * e, inv), hi = FxCell(tris[i].max + Vector3.one * e, inv);
                long count = (long)(hi.x - lo.x + 1) * (hi.y - lo.y + 1) * (hi.z - lo.z + 1);
                if (count > KH_ZFightDetector.MaxCellsPerTri) { oversize.Add(i); continue; }
                for (int x = lo.x; x <= hi.x; x++) for (int y = lo.y; y <= hi.y; y++) for (int z = lo.z; z <= hi.z; z++)
                { long k = FxPack(x, y, z); List<int> l; if (!grid.TryGetValue(k, out l)) grid[k] = l = new List<int>(); l.Add(i); }
            }
            plan.oversize = oversize.Count;
            var acc = new Dictionary<long, FxPair>(); var clip = new FxClip(); var isOver = new bool[tris.Count]; foreach (int o in oversize) isOver[o] = true;
            int done = 0, total = Math.Max(grid.Count, 1);
            foreach (var kv in grid)
            {
                if ((++done & 255) == 0) EditorUtility.DisplayProgressBar("KH ZFight: farmhouse fix plan", "cells " + done + "/" + total, (float)done / total);
                var list = kv.Value;
                for (int x = 0; x < list.Count; x++) for (int y = x + 1; y < list.Count; y++) FxTest(plan, list[x], list[y], kv.Key, inv, acc, clip);
            }
            for (int o = 0; o < oversize.Count; o++)
                for (int j = 0; j < tris.Count; j++)
                {
                    if (j == oversize[o] || (isOver[j] && j < oversize[o])) continue;
                    FxTest(plan, oversize[o], j, long.MinValue, inv, acc, clip);
                }
            plan.pairs = acc.Values.ToList();
            if (oversize.Count > 0) plan.notes.Add(oversize.Count + " large triangles tested by brute force");
        }

        static void FxTest(FxPlan plan, int i, int j, long cellKey, float inv, Dictionary<long, FxPair> acc, FxClip clip)
        {
            FxTri A = plan.tris[i], B = plan.tris[j]; float e = FixWindow;
            if (A.min.x > B.max.x + e || B.min.x > A.max.x + e || A.min.y > B.max.y + e || B.min.y > A.max.y + e || A.min.z > B.max.z + e || B.min.z > A.max.z + e) return;
            if (cellKey != long.MinValue) { var c = FxCell(Vector3.Max(A.min, B.min) - Vector3.one * e, inv); if (FxPack(c.x, c.y, c.z) != cellKey) return; }
            if (Vector3.Dot(A.n, B.n) < KH_ZFightDetector.NormalDot) return;   // same-facing only (opposite-facing faces are culled away)
            float d0 = Vector3.Dot(A.n, B.a - A.a), d1 = Vector3.Dot(A.n, B.b - A.a), d2 = Vector3.Dot(A.n, B.c - A.a);
            if ((d0 > e && d1 > e && d2 > e) || (d0 < -e && d1 < -e && d2 < -e)) return;
            if (A.comp == B.comp)
            {
                var fr = plan.rends[A.rend];
                int wa0 = fr.weld[A.i0], wa1 = fr.weld[A.i1], wa2 = fr.weld[A.i2], wb0 = fr.weld[B.i0], wb1 = fr.weld[B.i1], wb2 = fr.weld[B.i2];
                int shared = (wa0 == wb0 || wa0 == wb1 || wa0 == wb2 ? 1 : 0) + (wa1 == wb0 || wa1 == wb1 || wa1 == wb2 ? 1 : 0) + (wa2 == wb0 || wa2 == wb1 || wa2 == wb2 ? 1 : 0);
                if (shared == 2) return;   // edge neighbours
            }
            plan.candidates++;
            Vector3 u = (A.b - A.a).normalized, vv = Vector3.Cross(A.n, u);
            clip.Set(A.a, u, vv);
            float area = clip.Overlap(A.a, A.b, A.c, B.a, B.b, B.c); if (area < FixPairMinAreaM2) return;
            float nn = Vector3.Dot(B.n, A.n); if (nn < 1e-4f) return;
            float sMin = float.MaxValue, sMax = float.MinValue; Vector3 centre = Vector3.zero;
            for (int k = 0; k < clip.count; k++)
            {
                Vector3 p = A.a + u * clip.poly[k].x + vv * clip.poly[k].y; centre += p;
                float t = Vector3.Dot(B.n, B.a - p) / nn;   // B's plane relative to A's along A.n (+ = B in front)
                sMin = Mathf.Min(sMin, t); sMax = Mathf.Max(sMax, t);
            }
            centre /= clip.count;
            if (Mathf.Max(Mathf.Abs(sMin), Mathf.Abs(sMax)) > e) return;
            bool coplanar = Mathf.Max(Mathf.Abs(sMin), Mathf.Abs(sMax)) <= FixSeedGap;
            if (A.comp == B.comp)
            {
                if (!coplanar) return;
                FxIntra it; if (!plan.intra.TryGetValue(A.comp, out it)) plan.intra[A.comp] = it = new FxIntra { comp = A.comp, subA = A.sub, subB = B.sub };
                it.area += area; it.wc += centre * area; it.sMin = Mathf.Min(it.sMin, sMin); it.sMax = Mathf.Max(it.sMax, sMax);
                return;
            }
            bool aFirst = A.comp < B.comp; FxTri P = aFirst ? A : B, Q = aFirst ? B : A; int ip = aFirst ? i : j, iq = aFirst ? j : i;
            if (!aFirst) { float t0 = -sMax, t1 = -sMin; sMin = t0; sMax = t1; }
            long key = ((long)P.comp << 32) | (uint)Q.comp;
            FxPair pr; if (!acc.TryGetValue(key, out pr)) acc[key] = pr = new FxPair { a = P.comp, b = Q.comp };
            pr.area += area; pr.wc += centre * area; pr.nsum += P.n * area;
            pr.bmin = Vector3.Min(pr.bmin, Vector3.Min(P.min, Q.min)); pr.bmax = Vector3.Max(pr.bmax, Vector3.Max(P.max, Q.max));
            pr.raw.Add(new FxSheet { lo = sMin, hi = sMax, area = area });
            pr.trisA.Add(ip); pr.trisB.Add(iq); pr.subsA.Add(P.sub); pr.subsB.Add(Q.sub);
            if (coplanar)
            {
                float dist = Vector3.Distance(plan.eye, centre); pr.minDist = Mathf.Min(pr.minDist, dist);
                if (dist <= FixSeedRadius && !FxDownHidden(P.n, centre, plan.eye)) pr.seedArea += area;
            }
        }

        static bool FxDownHidden(Vector3 n, Vector3 centre, Vector3 eye) { return n.y < FixDownNormalY && centre.y < eye.y; }

        static void FxDecide(FxPlan plan)
        {
            foreach (var p in plan.pairs)
            {
                plan.comps[p.a].partners.Add(p.b); plan.comps[p.b].partners.Add(p.a);
                foreach (var s in p.raw.OrderBy(x => x.lo))
                {
                    var last = p.sheets.Count > 0 ? p.sheets[p.sheets.Count - 1] : null;
                    if (last != null && s.lo <= last.hi + FixSheetMerge) { last.hi = Mathf.Max(last.hi, s.hi); last.area += s.area; }
                    else p.sheets.Add(new FxSheet { lo = s.lo, hi = s.hi, area = s.area });
                }
                p.raw.Clear();
            }
            foreach (var p in plan.pairs)
            {
                p.fight = !FxAllowed(p, 0f);
                p.seed = p.fight && p.seedArea >= FixSeedMinAreaM2;
                bool movable = plan.rends[plan.comps[p.a].rend].movable && plan.rends[plan.comps[p.b].rend].movable;
                if (p.seed && !movable) { p.seed = false; plan.notes.Add("seed left alone (non-movable renderer): " + FxCompName(plan, p.a) + " x " + FxCompName(plan, p.b)); }
                if (p.fight) { string why; p.front = FxChooseFront(plan, p, out why); p.why = why; }
            }
        }

        /// <summary>True when every sheet ends >= target apart for delta = o_B - o_A.</summary>
        static bool FxAllowed(FxPair p, float delta)
        {
            foreach (var s in p.sheets) if (delta > -FixSolveTarget - s.hi + FixEps && delta < FixSolveTarget - s.lo - FixEps) return false;
            return true;
        }

        /// <summary>Smallest allowed delta >= d (B moves out).</summary>
        static float FxUp(FxPair p, float d)
        {
            for (int k = 0; k <= p.sheets.Count + 1; k++)
            {
                bool moved = false;
                foreach (var s in p.sheets) if (d > -FixSolveTarget - s.hi + FixEps && d < FixSolveTarget - s.lo - FixEps) { d = FixSolveTarget - s.lo; moved = true; }
                if (!moved) break;
            }
            return d;
        }

        /// <summary>Largest allowed delta <= d (A moves out).</summary>
        static float FxDown(FxPair p, float d)
        {
            for (int k = 0; k <= p.sheets.Count + 1; k++)
            {
                bool moved = false;
                foreach (var s in p.sheets) if (d > -FixSolveTarget - s.hi + FixEps && d < FixSolveTarget - s.lo - FixEps) { d = -FixSolveTarget - s.hi; moved = true; }
                if (!moved) break;
            }
            return d;
        }

        static int FxChooseFront(FxPlan plan, FxPair p, out string why)
        {
            bool ta = FxHasToken(plan, p.a, p.subsA), tb = FxHasToken(plan, p.b, p.subsB);
            if (ta != tb) { why = "material contains " + FixFrontToken; return ta ? p.a : p.b; }
            float needB = FxUp(p, 0f) * 1000f, needA = -FxDown(p, 0f) * 1000f;
            if (Mathf.Abs(needA - needB) > 1f) { why = "needs " + Mathf.Min(needA, needB).ToString("F1") + " mm instead of " + Mathf.Max(needA, needB).ToString("F1"); return needB < needA ? p.b : p.a; }
            FxComp ca = plan.comps[p.a], cb = plan.comps[p.b];
            if (Mathf.Abs(ca.area - cb.area) > 0.01f * Mathf.Max(ca.area, cb.area))
            { why = "smaller component (" + (Mathf.Min(ca.area, cb.area) * 1e4f).ToString("F0") + " vs " + (Mathf.Max(ca.area, cb.area) * 1e4f).ToString("F0") + " cm2)"; return ca.area < cb.area ? p.a : p.b; }
            if (ca.partners.Count != cb.partners.Count) { why = "fewer other pairs (" + Mathf.Min(ca.partners.Count, cb.partners.Count) + " vs " + Mathf.Max(ca.partners.Count, cb.partners.Count) + ")"; return ca.partners.Count < cb.partners.Count ? p.a : p.b; }
            why = "tie; later in the hierarchy"; return p.b;
        }

        static bool FxHasToken(FxPlan plan, int comp, HashSet<int> subs)
        {
            var fr = plan.rends[plan.comps[comp].rend];
            foreach (int s in subs) if (fr.mats[Math.Min(s, fr.mats.Length - 1)].Contains(FixFrontToken)) return true;
            return false;
        }

        /// <summary>Admits the seeds nearest-first; a seed whose solve breaks a limit (offset, moved count, non-movable, no convergence)
        /// is dropped and noted, so a far interior cascade can never cancel the exterior fix. Final offsets = solve of the admitted set.</summary>
        static void FxSolve(FxPlan plan)
        {
            var seeds = plan.pairs.Where(p => p.seed).OrderBy(p => p.minDist).ThenBy(p => p.a).ThenBy(p => p.b).ToList();
            foreach (var p in seeds) p.seed = false;
            float[] o; string err; int passes;
            foreach (var s in seeds)
            {
                s.seed = true;
                bool ok = FxTrySolve(plan, out o, out err, out passes); plan.passes += passes;
                if (ok) continue;
                s.seed = false; s.dropped = err;
                plan.notes.Add("seed dropped (" + s.minDist.ToString("F1") + " m): " + FxCompName(plan, s.a) + " x " + FxCompName(plan, s.b) + ": " + err);
            }
            if (!FxTrySolve(plan, out o, out err, out passes)) { plan.ok = false; plan.errors.Add("final solve failed: " + err); }
            plan.passes += passes;
            for (int i = 0; i < o.Length; i++) plan.comps[i].offset = o[i];
        }

        static bool FxTrySolve(FxPlan plan, out float[] o, out string err, out int pass)
        {
            o = new float[plan.comps.Count]; err = null; pass = 0; bool changed = true;
            foreach (var p in plan.pairs) if (!p.fight) { p.front = -1; p.why = ""; }   // fronts of clear pairs are chosen on activation
            while (changed && pass < FixMaxPasses && err == null)
            {
                changed = false; pass++;
                foreach (var p in plan.pairs)
                {
                    if (!p.seed && o[p.a] <= 0f && o[p.b] <= 0f) continue;
                    float d = o[p.b] - o[p.a];
                    if (FxAllowed(p, d)) continue;
                    if (p.front < 0)
                    {   // first activation of a pair that was clear before: push the side that needs less, ties to the piece already moved
                        float up = FxUp(p, d) - d, down = d - FxDown(p, d);
                        p.front = up < down - FixEps ? p.b : down < up - FixEps ? p.a : (o[p.a] >= o[p.b] ? p.a : p.b);
                        p.why = "kept clear of a moved neighbour";
                    }
                    int f = p.front;
                    if (!plan.rends[plan.comps[f].rend].movable) { err = "would move the non-movable " + FxCompName(plan, f); break; }
                    if (f == p.b) o[p.b] = o[p.a] + FxUp(p, d); else o[p.a] = o[p.b] - FxDown(p, d);
                    changed = true;
                    if (o[f] > FixMaxOffset + FixEps)
                    { err = "offset " + (o[f] * 1000f).ToString("F1") + " mm > " + FixMaxOffset * 1000f + " mm for " + FxCompName(plan, f) + " (pair " + FxCompName(plan, p.a) + " x " + FxCompName(plan, p.b) + ")"; break; }
                }
            }
            if (err == null && changed) err = "no convergence in " + FixMaxPasses + " passes (cyclic constraints)";
            if (err == null) { int moved = o.Count(x => x > 0f); if (moved > FixMaxMoved) err = moved + " components would move (> " + FixMaxMoved + ")"; }
            return err == null;
        }

        static IEnumerable<FxComp> FxMoved(FxPlan plan, FxRend fr) { return plan.comps.Where(c => c.rend == fr.index && c.offset > 0f); }

        static string FxCompName(FxPlan plan, int comp) { var c = plan.comps[comp]; return plan.rends[c.rend].path + " #c" + c.local; }

        static string FxSafe(string s)
        {
            var bad = Path.GetInvalidFileNameChars(); var sb = new StringBuilder();
            foreach (char ch in s ?? "") sb.Append(Array.IndexOf(bad, ch) >= 0 ? '_' : ch);
            var r = sb.ToString().Trim(); return r.Length == 0 ? "mesh" : r;
        }

        /// <summary>Derived asset name per renderer: "&lt;mesh&gt;_zfix", or "&lt;mesh&gt;_&lt;renderer&gt;_zfix" when several farmhouse renderers share the mesh.</summary>
        static void FxNames(FxPlan plan)
        {
            var used = new HashSet<string>();
            foreach (var fr in plan.rends)
            {
                if (!FxMoved(plan, fr).Any()) continue;
                int sharing = plan.rends.Count(x => x.source == fr.source);
                string baseName = FxSafe(fr.source.name) + (sharing > 1 ? "_" + FxSafe(fr.r.name) : "");
                string n = baseName + "_zfix"; int k = 2;
                while (!used.Add(n)) n = baseName + "_" + (k++) + "_zfix";
                plan.assetName[fr.index] = n;
            }
        }

        static int FxTriCount(List<int[]> subs) { int n = 0; foreach (var s in subs) n += s.Length / 3; return n; }

        static int FxTriCountMesh(Mesh m)
        {
            int n = 0;
            for (int s = 0; s < m.subMeshCount; s++) if (m.GetTopology(s) == MeshTopology.Triangles) n += (int)(m.GetIndexCount(s) / 3);
            return n;
        }

        static string FxPlanHash(FxPlan plan, FxRend fr)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(FixVersion).Append('|').Append(fr.sourceGuid).Append('|').Append(fr.sourceLocalId).Append('|').Append(fr.v.Length).Append('|').Append(FxTriCount(fr.subs));
            foreach (var c in FxMoved(plan, fr)) sb.Append('|').Append(c.local).Append(':').Append((c.offset * 1000f).ToString("F2", ci));
            for (int i = 0; i < 12; i++) sb.Append('|').Append(fr.l2w[i].ToString("F4", ci));   // world-mm -> local conversion depends on the transform
            return Hash128.Compute(sb.ToString()).ToString();
        }

        // ================================================================ derived mesh

        /// <summary>Source positions with every moved component inflated by its offset (world mm converted to local).</summary>
        static Vector3[] FxDisplaced(FxPlan plan, FxRend fr, out int moved, out float maxMm)
        {
            moved = 0; maxMm = 0f; var outV = (Vector3[])fr.v.Clone();
            var normals = new Dictionary<int, List<Vector3>>();
            foreach (var t in plan.tris)
            {
                if (t.rend != fr.index || plan.comps[t.comp].offset <= 0f) continue;
                for (int k = 0; k < 3; k++)
                {
                    int w = fr.weld[k == 0 ? t.i0 : k == 1 ? t.i1 : t.i2];
                    List<Vector3> l; if (!normals.TryGetValue(w, out l)) normals[w] = l = new List<Vector3>();
                    bool dup = false; foreach (var n in l) if (Vector3.Dot(n, t.n) > KH_ZFightDetector.NormalDot) { dup = true; break; }
                    if (!dup) l.Add(t.n);
                }
            }
            var inv = fr.l2w.inverse; var dLocal = new Dictionary<int, Vector3>();
            foreach (var kv in normals)
            {
                int ci = fr.compOfWeld[kv.Key]; if (ci < 0) continue;
                var d = FxSolveVertex(kv.Value, plan.comps[ci].offset);
                dLocal[kv.Key] = inv.MultiplyVector(d); maxMm = Mathf.Max(maxMm, d.magnitude * 1000f);
            }
            for (int i = 0; i < outV.Length; i++) { Vector3 d; if (dLocal.TryGetValue(fr.weld[i], out d)) { outV[i] += d; moved++; } }
            return outV;
        }

        /// <summary>d with n_i . d = o for the distinct normals around a vertex (regularised least squares; |d| capped at 3 o).</summary>
        static Vector3 FxSolveVertex(List<Vector3> ns, float o)
        {
            float a00 = FixLambda, a01 = 0, a02 = 0, a11 = FixLambda, a12 = 0, a22 = FixLambda; Vector3 b = Vector3.zero;
            foreach (var n in ns) { a00 += n.x * n.x; a01 += n.x * n.y; a02 += n.x * n.z; a11 += n.y * n.y; a12 += n.y * n.z; a22 += n.z * n.z; b += n * o; }
            var m = Matrix4x4.identity;
            m.m00 = a00; m.m01 = a01; m.m02 = a02; m.m10 = a01; m.m11 = a11; m.m12 = a12; m.m20 = a02; m.m21 = a12; m.m22 = a22;
            var d = m.inverse.MultiplyVector(b);
            if (d.magnitude > FixSpike * o) d = d.normalized * FixSpike * o;
            return d;
        }

        /// <summary>Fills dst with src's attributes and submeshes and the given positions. Never throws.</summary>
        static bool FxFill(Mesh dst, Mesh src, Vector3[] positions, string name, out string err)
        {
            err = null;
            if (src.isReadable)
            {
                try { FxFillManaged(dst, src, positions, name); return FxSameShape(dst, src, out err); }
                catch (Exception e) { err = "managed copy failed (" + e.Message + ")"; }
            }
            try { FxFillMeshData(dst, src, positions, name); return FxSameShape(dst, src, out err); }
            catch (Exception e) { err = (err != null ? err + "; " : "") + "MeshData copy failed (" + e.Message + ")"; return false; }
        }

        static void FxFillManaged(Mesh dst, Mesh src, Vector3[] positions, string name)
        {
            int n = src.vertexCount;
            dst.Clear(); dst.name = name; dst.indexFormat = src.indexFormat; dst.SetVertices(positions);
            if (src.HasVertexAttribute(VertexAttribute.Normal)) { var l = new List<Vector3>(); src.GetNormals(l); FxCheck(l.Count, n, "normals"); dst.SetNormals(l); }
            if (src.HasVertexAttribute(VertexAttribute.Tangent)) { var l = new List<Vector4>(); src.GetTangents(l); FxCheck(l.Count, n, "tangents"); dst.SetTangents(l); }
            if (src.HasVertexAttribute(VertexAttribute.Color)) { var l = new List<Color>(); src.GetColors(l); FxCheck(l.Count, n, "colors"); dst.SetColors(l); }
            for (int ch = 0; ch < 8; ch++)
            {
                var attr = (VertexAttribute)((int)VertexAttribute.TexCoord0 + ch); if (!src.HasVertexAttribute(attr)) continue;
                int dim = src.GetVertexAttributeDimension(attr);
                if (dim <= 2) { var l = new List<Vector2>(); src.GetUVs(ch, l); FxCheck(l.Count, n, "uv" + ch); dst.SetUVs(ch, l); }
                else if (dim == 3) { var l = new List<Vector3>(); src.GetUVs(ch, l); FxCheck(l.Count, n, "uv" + ch); dst.SetUVs(ch, l); }
                else { var l = new List<Vector4>(); src.GetUVs(ch, l); FxCheck(l.Count, n, "uv" + ch); dst.SetUVs(ch, l); }
            }
            dst.subMeshCount = src.subMeshCount;
            for (int s = 0; s < src.subMeshCount; s++) dst.SetIndices(src.GetIndices(s), src.GetTopology(s), s, false);
            dst.RecalculateBounds();
        }

        static void FxCheck(int got, int want, string what) { if (got != want) throw new InvalidOperationException(what + " " + got + " != " + want); }

        static void FxFillMeshData(Mesh dst, Mesh src, Vector3[] positions, string name)
        {
            using (var data = Mesh.AcquireReadOnlyMeshData(src))
            {
                var d = data[0]; int n = d.vertexCount;
                dst.Clear(); dst.name = name; dst.indexFormat = src.indexFormat; dst.SetVertices(positions);
                if (d.HasVertexAttribute(VertexAttribute.Normal)) using (var a = new NativeArray<Vector3>(n, Allocator.Temp)) { d.GetNormals(a); dst.SetNormals(a); }
                if (d.HasVertexAttribute(VertexAttribute.Tangent)) using (var a = new NativeArray<Vector4>(n, Allocator.Temp)) { d.GetTangents(a); dst.SetTangents(a); }
                if (d.HasVertexAttribute(VertexAttribute.Color)) using (var a = new NativeArray<Color>(n, Allocator.Temp)) { d.GetColors(a); dst.SetColors(a); }
                for (int ch = 0; ch < 8; ch++)
                {
                    var attr = (VertexAttribute)((int)VertexAttribute.TexCoord0 + ch); if (!d.HasVertexAttribute(attr)) continue;
                    int dim = d.GetVertexAttributeDimension(attr);
                    if (dim <= 2) using (var a = new NativeArray<Vector2>(n, Allocator.Temp)) { d.GetUVs(ch, a); dst.SetUVs(ch, a); }
                    else if (dim == 3) using (var a = new NativeArray<Vector3>(n, Allocator.Temp)) { d.GetUVs(ch, a); dst.SetUVs(ch, a); }
                    else using (var a = new NativeArray<Vector4>(n, Allocator.Temp)) { d.GetUVs(ch, a); dst.SetUVs(ch, a); }
                }
                dst.subMeshCount = d.subMeshCount;
                for (int s = 0; s < d.subMeshCount; s++)
                {
                    var sm = d.GetSubMesh(s);
                    using (var idx = new NativeArray<int>(sm.indexCount, Allocator.Temp)) { d.GetIndices(idx, s); dst.SetIndices(idx.ToArray(), sm.topology, s, false); }
                }
                dst.RecalculateBounds();
            }
        }

        static bool FxSameShape(Mesh dst, Mesh src, out string err)
        {
            err = null;
            if (dst.vertexCount != src.vertexCount) err = "vertex count " + dst.vertexCount + " != " + src.vertexCount;
            else if (dst.subMeshCount != src.subMeshCount) err = "submesh count " + dst.subMeshCount + " != " + src.subMeshCount;
            else for (int s = 0; s < src.subMeshCount; s++) if (dst.GetIndexCount(s) != src.GetIndexCount(s)) { err = "submesh " + s + " index count differs"; break; }
            return err == null;
        }

        static void FxEnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(FixMeshesParent)) AssetDatabase.CreateFolder("Assets/KominkaHillside", "Meshes");
            if (!AssetDatabase.IsValidFolder(FixDir)) AssetDatabase.CreateFolder(FixMeshesParent, "FarKominka");
        }

        static void FxAssign(MeshFilter mf, Mesh mesh)
        {
            Undo.RecordObject(mf, "KH ZFight farmhouse fix");
            mf.sharedMesh = mesh;
            PrefabUtility.RecordPrefabInstancePropertyModifications(mf);
        }

        // ================================================================ apply

        static bool FxApply(FxPlan plan, FixReport rep)
        {
            bool changed = false; FxEnsureFolder();
            var planned = new HashSet<MeshFilter>(); var sidecarsToImport = new List<string>();
            foreach (var fr in plan.rends)
            {
                var moved = FxMoved(plan, fr).ToList(); if (moved.Count == 0) continue;
                planned.Add(fr.mf);
                var row = rep.renderersChanged.FirstOrDefault(x => x.renderer == fr.path && x.action == "planned"); if (row == null) { row = new FixRendRow { renderer = fr.path }; rep.renderersChanged.Add(row); }
                string name = plan.assetName[fr.index], path = FixDir + "/" + name + ".asset", side = FxSidecarPath(path), hash = FxPlanHash(plan, fr);
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path); var sc = FxReadSidecar(path);
                bool current = mesh != null && sc != null && sc.planHash == hash && sc.sourceGuid == fr.sourceGuid && sc.sourceLocalId == fr.sourceLocalId && mesh.vertexCount == fr.source.vertexCount;
                string action;
                if (current) action = "asset unchanged (sidecar matches)";
                else
                {
                    int mv; float mx; var pos = FxDisplaced(plan, fr, out mv, out mx); string err;
                    bool isNew = mesh == null; if (isNew) mesh = new Mesh();
                    if (!FxFill(mesh, fr.source, pos, name, out err))
                    {
                        rep.errors.Add("mesh build failed for " + fr.path + ": " + err); row.action = "FAILED: " + err;
                        if (isNew) UnityEngine.Object.DestroyImmediate(mesh);
                        continue;
                    }
                    if (isNew) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
                    var scNew = new FixSidecar { version = FixVersion, renderer = fr.path, sourceName = fr.source.name, sourcePath = fr.sourcePath, sourceGuid = fr.sourceGuid, sourceLocalId = fr.sourceLocalId,
                        sourceVertices = fr.source.vertexCount, sourceTriangles = FxTriCount(fr.subs), derivedVertices = mesh.vertexCount, derivedTriangles = FxTriCountMesh(mesh), planHash = hash, utc = DateTime.UtcNow.ToString("o") };
                    foreach (var c in moved) scNew.components.Add("c" + c.local + ": " + c.tris + " tris, submeshes " + string.Join("/", c.subs.OrderBy(x => x)) + ", inflated " + (c.offset * 1000f).ToString("F2", CultureInfo.InvariantCulture) + " mm");
                    File.WriteAllText(side, JsonUtility.ToJson(scNew, true)); sidecarsToImport.Add(side);
                    action = isNew ? "asset created" : "asset rebuilt";
                }
                if (fr.mf.sharedMesh != mesh) { FxAssign(fr.mf, mesh); changed = true; action += ", MeshFilter assigned"; }
                else action += ", MeshFilter already assigned";
                row.action = action; row.derived = path;
            }
            AssetDatabase.SaveAssets();
            foreach (var s in sidecarsToImport) AssetDatabase.ImportAsset(s, ImportAssetOptions.ForceUpdate);
            foreach (var mf in plan.root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (planned.Contains(mf) || !FxIsDerived(mf.sharedMesh)) continue;
                string how; var src = FxSourceMesh(mf, out how); string where = KH_ZFightDetector.LeafPath(mf.transform, plan.root.transform);
                if (src == null) { rep.errors.Add("cannot revert " + where + ": " + how); continue; }
                FxAssign(mf, src); changed = true; rep.notes.Add("reverted to the source mesh (no longer in the plan): " + where);
            }
            changed |= FxCopies(plan, rep, true);
            foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { FixDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!plan.assetName.Values.Any(n => FixDir + "/" + n + ".asset" == p)) rep.notes.Add("stale derived asset, not referenced by the plan (left on disk): " + p);
            }
            return changed;
        }

        /// <summary>Other MeshFilters that use a farmhouse source mesh: a copy of the farmhouse (same relative path and layout under
        /// another root) gets the same derived mesh; anything else is reported and left alone.</summary>
        static bool FxCopies(FxPlan plan, FixReport rep, bool apply)
        {
            bool changed = false; var rootT = plan.root.transform;
            var bySource = new Dictionary<Mesh, List<FxRend>>();
            foreach (var fr in plan.rends) { List<FxRend> l; if (!bySource.TryGetValue(fr.source, out l)) bySource[fr.source] = l = new List<FxRend>(); l.Add(fr); }
            foreach (var mf in UnityEngine.Object.FindObjectsOfType<MeshFilter>(true))
            {
                if (mf.transform.IsChildOf(rootT) || mf.sharedMesh == null) continue;
                string how; var src = FxIsDerived(mf.sharedMesh) ? FxSourceMesh(mf, out how) : mf.sharedMesh;
                List<FxRend> frs; if (src == null || !bySource.TryGetValue(src, out frs)) continue;
                string where = KH_ZFightDetector.LeafPath(mf.transform, null);
                Transform copyRoot = null; FxRend match = null;
                foreach (var cand in frs)
                {
                    if (string.IsNullOrEmpty(cand.rel)) continue;
                    for (var t = mf.transform.parent; t != null; t = t.parent) if (t.Find(cand.rel) == mf.transform) { copyRoot = t; match = cand; break; }
                    if (match != null) break;
                }
                if (match == null) { rep.copies.Add("other user of mesh " + src.name + " (not a farmhouse copy), left alone: " + where); continue; }
                string copyName = KH_ZFightDetector.LeafPath(copyRoot, null);
                if (!FxMoved(plan, match).Any())
                {
                    if (FxIsDerived(mf.sharedMesh) && apply) { FxAssign(mf, src); changed = true; rep.copies.Add("copy " + copyName + "/" + match.rel + ": reverted to the source mesh"); }
                    continue;
                }
                var relMain = rootT.worldToLocalMatrix * match.l2w; var relCopy = copyRoot.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                bool same = true; for (int i = 0; i < 16; i++) if (Mathf.Abs(relMain[i] - relCopy[i]) > 1e-3f) { same = false; break; }
                if (!same || (copyRoot.lossyScale - rootT.lossyScale).magnitude > 0.01f * rootT.lossyScale.magnitude)
                { rep.copies.Add("copy " + copyName + "/" + match.rel + ": layout or scale differs from " + RootPath + "; skipped (fix it separately)"); continue; }
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(FixDir + "/" + plan.assetName[match.index] + ".asset");
                if (!apply || mesh == null) { rep.copies.Add("copy " + copyName + "/" + match.rel + ": would get " + plan.assetName[match.index] + (apply ? " (asset missing)" : "")); continue; }
                if (mf.sharedMesh != mesh) { FxAssign(mf, mesh); changed = true; rep.copies.Add("copy " + copyName + "/" + match.rel + ": assigned " + plan.assetName[match.index]); }
                else rep.copies.Add("copy " + copyName + "/" + match.rel + ": already assigned");
            }
            if (rep.copies.Count == 0) rep.copies.Add("no MeshFilter outside " + RootPath + " uses a farmhouse source mesh (no backdrop copy in this scene)");
            return changed;
        }

        // ================================================================ report

        static void FxFillReport(FxPlan plan, FixReport rep)
        {
            rep.eye = plan.eye; rep.renderers = plan.rends.Count; rep.components = plan.comps.Count; rep.triangles = plan.tris.Count; rep.pairs = plan.pairs.Count;
            rep.candidates = plan.candidates; rep.passes = plan.passes;
            rep.errors.AddRange(plan.errors); rep.notes.AddRange(plan.notes);
            FarmReport scan = null;
            try { if (File.Exists(OutDir + "/farmhouse.json")) scan = JsonUtility.FromJson<FarmReport>(File.ReadAllText(OutDir + "/farmhouse.json")); }
            catch (Exception e) { rep.notes.Add("farmhouse.json unreadable (scan ranks left blank): " + e.Message); }
            if (scan == null) rep.notes.Add("no farmhouse.json; run menu 1 first to get scan ranks in the tables");
            foreach (var p in plan.pairs.OrderBy(x => x.minDist).ThenBy(x => Vector3.Distance(plan.eye, x.Centre)))
            {
                bool active = p.seed || plan.comps[p.a].offset > 0f || plan.comps[p.b].offset > 0f;
                if (p.seed) rep.seeds++;
                if (p.dropped != null) { rep.droppedSeeds++; rep.seedRows.Add(FxRow(plan, p, scan)); if (active) rep.activePairs++; continue; }
                if (active) rep.activePairs++;
                if (active) (p.seed ? rep.seedRows : rep.cascadeRows).Add(FxRow(plan, p, scan));
                else if (p.fight) { rep.unfixedFights++; if (rep.unfixedRows.Count < 40) rep.unfixedRows.Add(FxRow(plan, p, scan)); }
            }
            rep.movedComponents = plan.comps.Count(c => c.offset > 0f);
            foreach (var fr in plan.rends)
            {
                var moved = FxMoved(plan, fr).ToList(); if (moved.Count == 0) continue;
                int mv; float mx; FxDisplaced(plan, fr, out mv, out mx);
                string name; plan.assetName.TryGetValue(fr.index, out name);
                rep.renderersChanged.Add(new FixRendRow { renderer = fr.path, sourceMesh = fr.sourcePath + " :: " + fr.source.name, sourceHow = fr.sourceHow, derived = name != null ? FixDir + "/" + name + ".asset" : "",
                    action = "planned", offsetsMm = string.Join(", ", moved.Select(c => "c" + c.local + " (" + c.tris + " tris) " + (c.offset * 1000f).ToString("F1"))),
                    components = plan.comps.Count(c => c.rend == fr.index), movedComponents = moved.Count, vertices = fr.v.Length, verticesMoved = mv, triangles = FxTriCount(fr.subs), maxVertexMoveMm = mx });
            }
            foreach (var it in plan.intra.Values.OrderBy(x => Vector3.Distance(plan.eye, x.wc / Mathf.Max(x.area, 1e-9f))))
            {
                var c = it.wc / Mathf.Max(it.area, 1e-9f); float dist = Vector3.Distance(plan.eye, c);
                if (dist > 15f || it.area * 1e4f < 5f) continue;
                rep.intraComponent.Add(FxCompName(plan, it.comp) + " submeshes " + it.subA + "/" + it.subB + ": " + (it.area * 1e4f).ToString("F0") + " cm2 at (" + KH_ZFightDetector.V(c) + ") " + dist.ToString("F1") + " m, gap "
                    + (it.sMin * 1000f).ToString("F1") + ".." + (it.sMax * 1000f).ToString("F1") + " mm (one connected piece; inflation cannot separate it; left alone)");
            }
        }

        static FixPairRow FxRow(FxPlan plan, FxPair p, FarmReport scan)
        {
            FxComp ca = plan.comps[p.a], cb = plan.comps[p.b]; FxRend ra = plan.rends[ca.rend], rb = plan.rends[cb.rend];
            float areaA = p.trisA.Sum(i => plan.tris[i].area), areaB = p.trisB.Sum(i => plan.tris[i].area), delta = cb.offset - ca.offset;
            var row = new FixPairRow
            {
                rendererA = ra.path, rendererB = rb.path, componentA = ca.local, componentB = cb.local, materialsA = FxMats(ra, p.subsA), materialsB = FxMats(rb, p.subsB),
                relation = ca.rend != cb.rend ? "renderers" : p.subsA.Overlaps(p.subsB) ? "components" : "submeshes",
                seed = p.seed, fight = p.fight, why = p.dropped != null ? "DROPPED: " + p.dropped : p.why, areaCm2 = p.area * 1e4f, seedAreaCm2 = p.seedArea * 1e4f, distance = Vector3.Distance(plan.eye, p.Centre),
                sheetsBeforeMm = string.Join(" ; ", p.sheets.Select(s => (s.lo * 1000f).ToString("F1") + ".." + (s.hi * 1000f).ToString("F1"))),
                closestBeforeMm = FxClosest(p, 0f) * 1000f, closestAfterMm = FxClosest(p, delta) * 1000f,
                coverAByBPct = areaA > 0 ? Mathf.Min(100f, p.area / areaA * 100f) : 0f, coverBByAPct = areaB > 0 ? Mathf.Min(100f, p.area / areaB * 100f) : 0f,
                offsetAMm = ca.offset * 1000f, offsetBMm = cb.offset * 1000f, centre = p.Centre, normal = p.nsum.normalized,
                separated = FxAllowed(p, delta) || FxClosest(p, delta) >= FixTarget - 5e-5f
            };
            row.moved = ca.offset > 0f && cb.offset > 0f ? "A+B" : ca.offset > 0f ? "A" : cb.offset > 0f ? "B" : "-";
            row.scanRanks = FxRanks(scan, row, p);
            return row;
        }

        /// <summary>Smallest |s + delta| over the sheets (0 when a sheet straddles).</summary>
        static float FxClosest(FxPair p, float delta)
        {
            float best = float.MaxValue;
            foreach (var s in p.sheets) { float lo = s.lo + delta, hi = s.hi + delta; best = Mathf.Min(best, lo <= 0f && hi >= 0f ? 0f : Mathf.Min(Mathf.Abs(lo), Mathf.Abs(hi))); }
            return best == float.MaxValue ? 0f : best;
        }

        static string FxMats(FxRend fr, HashSet<int> subs) { return string.Join("+", subs.OrderBy(x => x).Select(s => s + ":" + fr.mats[Math.Min(s, fr.mats.Length - 1)])); }

        static string FxRanks(FarmReport scan, FixPairRow row, FxPair p)
        {
            if (scan == null || scan.groups == null) return "";
            var ranks = new List<int>(); var lo = p.bmin - Vector3.one * 0.05f; var hi = p.bmax + Vector3.one * 0.05f;
            foreach (var g in scan.groups)
            {
                bool same = g.partA == row.rendererA && g.partB == row.rendererB && p.subsA.Contains(g.subA) && p.subsB.Contains(g.subB);
                bool swap = g.partA == row.rendererB && g.partB == row.rendererA && p.subsB.Contains(g.subA) && p.subsA.Contains(g.subB);
                if (!same && !swap) continue;
                if (DistToBox(g.centre, lo, hi) <= 0f) ranks.Add(g.rank);
            }
            return string.Join(",", ranks.OrderBy(x => x).Take(8));
        }

        static void FxWrite(FixReport rep, string baseName)
        {
            try
            {
                Directory.CreateDirectory(OutDir);
                File.WriteAllText(OutDir + "/" + baseName + ".json", JsonUtility.ToJson(rep, true));
                var sb = new StringBuilder();
                sb.AppendLine("# ZFight farmhouse fix: " + rep.mode + " (" + rep.utc + ")").AppendLine();
                sb.AppendLine("- status **" + rep.status + "**; scene `" + rep.scene + "` dirty " + rep.sceneDirtyBefore + " -> " + rep.sceneDirtyAfter + " (never saved here); spawn eye (" + KH_ZFightDetector.V(rep.eye) + ")");
                sb.AppendLine("- renderers " + rep.renderers + ", components " + rep.components + ", triangles " + rep.triangles + ", candidate triangle pairs " + rep.candidates + ", component pairs " + rep.pairs + ", seeds " + rep.seeds + " (dropped " + rep.droppedSeeds + "), active pairs " + rep.activePairs + ", moved components " + rep.movedComponents + ", solver passes " + rep.passes);
                sb.AppendLine("- method: " + rep.method + ". Every face of a moved component moves outward by its offset along its own normal; vertex/triangle counts, UVs (UV2 kept), normals, tangents, colours, submeshes and material slots are unchanged; MeshColliders are not touched.");
                sb.AppendLine("- thresholds: " + rep.thresholds);
                sb.AppendLine("- depth: " + rep.depthNote);
                sb.AppendLine("- sheets = signed separations of B's faces relative to A's along A's normal (+ = B in front), merged when within " + FixSheetMerge * 1000 + " mm; closest = smallest |s| over sheets before / after the offsets").AppendLine();
                if (rep.errors.Count > 0) { sb.AppendLine("## Errors").AppendLine(); foreach (var e in rep.errors) sb.AppendLine("- " + e); sb.AppendLine(); }
                sb.AppendLine("## Seeds (fights within " + FixSeedRadius + " m that the fix targets): " + rep.seedRows.Count).AppendLine();
                FxPairTable(sb, rep.seedRows);
                sb.AppendLine().AppendLine("## Cascaded pairs (touch a moved component; kept >= " + FixTarget * 1000 + " mm apart): " + rep.cascadeRows.Count).AppendLine();
                FxPairTable(sb, rep.cascadeRows.Take(120));
                sb.AppendLine().AppendLine("## Renderers and derived meshes").AppendLine();
                sb.AppendLine("| renderer | source mesh (how found) | derived asset | action | moved / all components | offsets (component, tris, mm) | vertices moved / all | max vertex move mm | triangles |").AppendLine("|---|---|---|---|---|---|---|---|---|");
                foreach (var r in rep.renderersChanged)
                    sb.AppendLine("| " + KH_ZFightDetector.Md(r.renderer) + " | " + KH_ZFightDetector.Md(r.sourceMesh) + " (" + KH_ZFightDetector.Md(r.sourceHow) + ") | " + KH_ZFightDetector.Md(r.derived) + " | " + KH_ZFightDetector.Md(r.action) + " | " + r.movedComponents + " / " + r.components + " | " + KH_ZFightDetector.Md(r.offsetsMm) + " | " + r.verticesMoved + " / " + r.vertices + " | " + r.maxVertexMoveMm.ToString("F1") + " | " + r.triangles + " (unchanged) |");
                sb.AppendLine().AppendLine("## Fights left alone (no seed overlap within " + FixSeedRadius + " m and no moved component): " + rep.unfixedFights + " (nearest 40)").AppendLine();
                FxPairTable(sb, rep.unfixedRows);
                sb.AppendLine().AppendLine("## Coplanar faces inside one component (within 15 m, >= 5 cm2; not separable by inflation)").AppendLine();
                if (rep.intraComponent.Count == 0) sb.AppendLine("- none"); foreach (var s in rep.intraComponent) sb.AppendLine("- " + KH_ZFightDetector.Md(s));
                sb.AppendLine().AppendLine("## Other copies of the farmhouse meshes").AppendLine();
                foreach (var s in rep.copies) sb.AppendLine("- " + s);
                sb.AppendLine().AppendLine("## Notes").AppendLine();
                foreach (var s in rep.notes) sb.AppendLine("- " + s);
                File.WriteAllText(OutDir + "/" + baseName + ".md", sb.ToString());
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        static void FxPairTable(StringBuilder sb, IEnumerable<FixPairRow> rows)
        {
            sb.AppendLine("| scan rank | A (component; submesh:material) | B (component; submesh:material) | relation | area cm2 (seed) | centre | dist m | sheets before mm | cover A by B / B by A % | moved | why | offset A / B mm | closest before -> after mm | ok |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in rows)
                sb.AppendLine("| " + r.scanRanks + " | " + KH_ZFightDetector.Md(r.rendererA) + " #c" + r.componentA + "; " + KH_ZFightDetector.Md(r.materialsA) + " | " + KH_ZFightDetector.Md(r.rendererB) + " #c" + r.componentB + "; " + KH_ZFightDetector.Md(r.materialsB)
                    + " | " + r.relation + " | " + r.areaCm2.ToString("F0") + " (" + r.seedAreaCm2.ToString("F0") + ") | " + KH_ZFightDetector.V(r.centre) + " | " + r.distance.ToString("F1") + " | " + r.sheetsBeforeMm + " | "
                    + r.coverAByBPct.ToString("F0") + " / " + r.coverBByAPct.ToString("F0") + " | " + r.moved + " | " + KH_ZFightDetector.Md(r.why) + " | " + r.offsetAMm.ToString("F1") + " / " + r.offsetBMm.ToString("F1") + " | "
                    + r.closestBeforeMm.ToString("F1") + " -> " + r.closestAfterMm.ToString("F1") + " | " + (r.separated ? "yes" : "NO") + " |");
        }

        // ================================================================ 6 - Verify

        [MenuItem(Menu + "6 - Verify Farmhouse Fix")]
        public static void VerifyFarmhouseFix()
        {
            const string tag = "[KH ZFight 6]";
            var rep = new FixVerifyReport { utc = DateTime.UtcNow.ToString("o") };
            try
            {
                if (!Guard(tag)) return;
                var scene = SceneManager.GetActiveScene(); rep.scene = scene.path; rep.sceneDirtyBefore = scene.isDirty;
                var root = FindRoot();
                if (root == null) rep.failures.Add(RootPath + " not found");
                else
                {
                    var plan = FxBuildPlan(root); rep.eye = plan.eye;
                    if (!plan.ok) foreach (var e in plan.errors) rep.failures.Add("planner: " + e);
                    // 1. every planned renderer points at an up-to-date derived mesh; nothing else points at one
                    var planned = new HashSet<MeshFilter>();
                    foreach (var fr in plan.rends)
                    {
                        if (!FxMoved(plan, fr).Any()) continue;
                        planned.Add(fr.mf);
                        string path = FixDir + "/" + plan.assetName[fr.index] + ".asset";
                        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path); var sc = FxReadSidecar(path);
                        if (mesh == null) { rep.failures.Add("missing derived mesh " + path + " for " + fr.path + " (run 5)"); continue; }
                        if (sc == null) { rep.failures.Add("missing sidecar " + FxSidecarPath(path)); continue; }
                        if (sc.planHash != FxPlanHash(plan, fr)) rep.failures.Add("pending: the plan for " + fr.path + " changed since " + path + " was built (run 5)");
                        if (sc.sourceGuid != fr.sourceGuid || sc.sourceLocalId != fr.sourceLocalId) rep.failures.Add("sidecar source of " + path + " is not the source mesh of " + fr.path);
                        if (fr.mf.sharedMesh != mesh) rep.failures.Add(fr.path + " MeshFilter does not point at " + path);
                        if (mesh.vertexCount != fr.source.vertexCount || mesh.subMeshCount != fr.source.subMeshCount || FxTriCountMesh(mesh) != FxTriCount(fr.subs))
                            rep.failures.Add(path + ": vertex / submesh / triangle count differs from the source");
                        rep.derivedChecked++;
                    }
                    foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                        if (!planned.Contains(mf) && FxIsDerived(mf.sharedMesh))
                            rep.failures.Add(KH_ZFightDetector.LeafPath(mf.transform, root.transform) + " points at " + AssetDatabase.GetAssetPath(mf.sharedMesh) + " but the plan does not move it (run 5)");
                    // 2. colliders keep the original meshes
                    foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
                    {
                        rep.collidersChecked++; string where = KH_ZFightDetector.LeafPath(mc.transform, root.transform);
                        if (FxIsDerived(mc.sharedMesh)) rep.failures.Add("MeshCollider uses a derived mesh: " + where);
                        var pc = PrefabUtility.GetCorrespondingObjectFromSource(mc);
                        if (pc != null && pc.sharedMesh != mc.sharedMesh) rep.notes.Add("MeshCollider mesh differs from its prefab (not set by this tool): " + where);
                    }
                    // 3. detector with menu 1's inputs and settings
                    int main, extra; var inputs = FxScanInputs(root, out main, out extra);
                    var res = KH_ZFightDetector.Run(inputs, FarmCell, FarmMinAreaM2, FarmClusterLink, FarmBudgetSeconds, "KH ZFight: verify farmhouse fix");
                    rep.truncated = res.truncated; rep.detectorGroups = res.groups.Count;
                    rep.notes.Add("detector: renderers main " + main + ", extra " + extra + ", triangles " + res.triangles + ", groups " + res.groups.Count + ", back-to-back " + res.backToBack.Count + ", " + res.seconds.ToString("F1") + " s");
                    if (res.truncated) rep.failures.Add("detector truncated: " + string.Join("; ", res.notes));
                    foreach (var p in res.parts) if (!p.readable) rep.failures.Add("detector could not read " + p.path + ": " + p.note);
                    string prefix = root.name + "/";
                    foreach (var g in res.groups.OrderBy(x => Vector3.Distance(plan.eye, x.centre)))
                    {
                        float dist = Vector3.Distance(plan.eye, g.centre);
                        if (dist > FixVerifyRadius) continue;
                        bool farm = g.partA.StartsWith(prefix, StringComparison.Ordinal) || g.partB.StartsWith(prefix, StringComparison.Ordinal);
                        string desc = g.tier + " " + g.partA + "#" + g.subA + " (" + g.materialA + ") x " + g.partB + "#" + g.subB + " (" + g.materialB + ") " + g.relation + " " + g.areaCm2.ToString("F0") + " cm2 at (" + KH_ZFightDetector.V(g.centre) + ") " + dist.ToString("F1") + " m, gap " + g.gapMaxMm.ToString("F1") + " mm, n (" + KH_ZFightDetector.V(g.normal) + ")";
                        if (g.facing != "same") { if (farm) rep.notes.Add("opposite-facing group on a Cull Off material (not judged): " + desc); continue; }
                        if (g.tier != "coplanar") { if (farm && g.gapMaxMm < FixTarget * 1000f && g.areaCm2 > FixVerifyMinAreaCm2) rep.nearInfo.Add(desc); continue; }
                        if (g.areaCm2 <= FixVerifyMinAreaCm2) continue;
                        if (!farm) { rep.outOfScope.Add(desc); continue; }
                        if (g.textUiInvolved) { rep.excluded.Add("text/UI: " + desc); continue; }
                        if (FxDownHidden(g.normal, g.centre, plan.eye)) { rep.excluded.Add("down-facing below the spawn eye (culled from any standing eye): " + desc); continue; }
                        rep.failures.Add("coplanar group remains: " + desc);
                    }
                }
            }
            catch (Exception e) { rep.failures.Add("exception: " + e.GetType().Name + " " + e.Message); Debug.LogException(e); }
            rep.sceneDirtyAfter = SceneManager.GetActiveScene().isDirty;
            rep.status = rep.failures.Count == 0 ? "OK" : "FAIL";
            FxWriteVerify(rep);
            if (rep.status == "OK") Debug.Log(tag + " ZFIGHT_FIX_OK " + rep.derivedChecked + " (derived meshes checked; colliders " + rep.collidersChecked + "; out of scope " + rep.outOfScope.Count + ", excluded " + rep.excluded.Count + ") -> " + OutDir + "/fix-verify.md");
            else Debug.LogError(tag + " ZFIGHT_FIX_FAIL " + rep.failures.Count + ": " + string.Join(" / ", rep.failures.Take(4)) + " -> " + OutDir + "/fix-verify.md");
        }

        /// <summary>Menu 1's detector inputs: active LOD0 renderers under the root, plus other renderers whose bounds meet them (clipped).</summary>
        static List<KH_ZFightDetector.Input> FxScanInputs(GameObject root, out int main, out int extra)
        {
            main = 0; extra = 0; var lodSkip = FxLodSkip(); var inputs = new List<KH_ZFightDetector.Input>();
            var region = new Bounds(); bool has = false; var mainSet = new HashSet<Renderer>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                mainSet.Add(r);
                if (!r.enabled || !r.gameObject.activeInHierarchy || lodSkip.Contains(r)) continue;
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                inputs.Add(new KH_ZFightDetector.Input { path = KH_ZFightDetector.LeafPath(r.transform, root.transform), mesh = mf.sharedMesh, toWorld = r.transform.localToWorldMatrix,
                    materials = r.sharedMaterials, textUi = TextUi(r), stepLike = IsStep(r.name) });
                main++;
                if (!has) { region = r.bounds; has = true; } else region.Encapsulate(r.bounds);
            }
            region.Expand(0.01f);
            foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
            {
                if (mainSet.Contains(r) || !r.enabled || lodSkip.Contains(r) || !r.bounds.Intersects(region)) continue;
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                inputs.Add(new KH_ZFightDetector.Input { path = KH_ZFightDetector.LeafPath(r.transform, null), mesh = mf.sharedMesh, toWorld = r.transform.localToWorldMatrix,
                    materials = r.sharedMaterials, textUi = TextUi(r), stepLike = false, clip = region });
                extra++;
            }
            return inputs;
        }

        static void FxWriteVerify(FixVerifyReport rep)
        {
            try
            {
                Directory.CreateDirectory(OutDir);
                File.WriteAllText(OutDir + "/fix-verify.json", JsonUtility.ToJson(rep, true));
                var sb = new StringBuilder();
                sb.AppendLine("# ZFight farmhouse fix: verify (" + rep.utc + ")").AppendLine();
                sb.AppendLine("- status **" + rep.status + "**; scene `" + rep.scene + "` dirty " + rep.sceneDirtyBefore + " -> " + rep.sceneDirtyAfter + "; spawn eye (" + KH_ZFightDetector.V(rep.eye) + ")");
                sb.AppendLine("- derived meshes checked " + rep.derivedChecked + ", MeshColliders checked " + rep.collidersChecked + ", detector groups " + rep.detectorGroups + ", truncated " + rep.truncated);
                sb.AppendLine("- FAIL rule: a same-facing coplanar (<= " + KH_ZFightDetector.CoplanarGap * 1000 + " mm) group within " + FixVerifyRadius + " m of the spawn eye with area > " + FixVerifyMinAreaCm2 + " cm2 that involves " + RootPath
                    + ", except text/UI and down-facing (n.y < " + FixDownNormalY + ") below the eye; plus derived-mesh / sidecar / plan / collider consistency").AppendLine();
                FxList(sb, "Failures", rep.failures);
                FxList(sb, "Excluded (same rule as the fix planner)", rep.excluded);
                FxList(sb, "Out of scope (coplanar within " + FixVerifyRadius + " m, not part of the farmhouse; not fixed by BUG-ENV-003)", rep.outOfScope);
                FxList(sb, "Info: farmhouse near-tier groups closer than " + FixTarget * 1000 + " mm within " + FixVerifyRadius + " m", rep.nearInfo);
                FxList(sb, "Notes", rep.notes);
                File.WriteAllText(OutDir + "/fix-verify.md", sb.ToString());
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        static void FxList(StringBuilder sb, string title, List<string> items)
        {
            sb.AppendLine("## " + title + " (" + items.Count + ")").AppendLine();
            if (items.Count == 0) sb.AppendLine("- none");
            foreach (var s in items) sb.AppendLine("- " + KH_ZFightDetector.Md(s));
            sb.AppendLine();
        }

        // ================================================================ geometry helpers (the detector's are private)

        static Vector3Int FxCell(Vector3 p, float inv) { return new Vector3Int(Mathf.FloorToInt(p.x * inv), Mathf.FloorToInt(p.y * inv), Mathf.FloorToInt(p.z * inv)); }
        static long FxPack(int x, int y, int z) { return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF); }
        static int FxFind(int[] uf, int i) { while (uf[i] != i) { uf[i] = uf[uf[i]]; i = uf[i]; } return i; }
        static void FxUnion(int[] uf, int a, int b) { a = FxFind(uf, a); b = FxFind(uf, b); if (a != b) uf[b] = a; }

        class FxClip
        {
            public Vector2[] poly = new Vector2[16], tmp = new Vector2[16]; public int count;
            Vector3 o, u, v;
            public void Set(Vector3 origin, Vector3 uu, Vector3 vv) { o = origin; u = uu; v = vv; }
            Vector2 P(Vector3 p) { var d = p - o; return new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v)); }

            /// <summary>Area of (B projected on A's plane) clipped by A; leaves the polygon in poly[0..count).</summary>
            public float Overlap(Vector3 a, Vector3 b, Vector3 c, Vector3 pa, Vector3 pb, Vector3 pc)
            {
                Vector2 A0 = P(a), A1 = P(b), A2 = P(c);
                if (Cross(A1 - A0, A2 - A0) < 0) { var t = A1; A1 = A2; A2 = t; }
                poly[0] = P(pa); poly[1] = P(pb); poly[2] = P(pc); count = 3;
                ClipEdge(A0, A1); if (count < 3) return 0f;
                ClipEdge(A1, A2); if (count < 3) return 0f;
                ClipEdge(A2, A0); if (count < 3) return 0f;
                float s = 0f; for (int i = 0; i < count; i++) { var p = poly[i]; var q = poly[(i + 1) % count]; s += p.x * q.y - q.x * p.y; }
                return Mathf.Abs(s) * .5f;
            }
            void ClipEdge(Vector2 e0, Vector2 e1)
            {
                int n = 0; var edge = e1 - e0;
                for (int i = 0; i < count && n < 14; i++)
                {
                    var p = poly[i]; var q = poly[(i + 1) % count];
                    float sp = Cross(edge, p - e0), sq = Cross(edge, q - e0);
                    bool pin = sp >= 0f, qin = sq >= 0f;
                    if (pin) tmp[n++] = p;
                    if (pin != qin) { float t = sp / (sp - sq); tmp[n++] = p + (q - p) * t; }
                }
                var sw = poly; poly = tmp; tmp = sw; count = n;
            }
            static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }
        }
    }
}
