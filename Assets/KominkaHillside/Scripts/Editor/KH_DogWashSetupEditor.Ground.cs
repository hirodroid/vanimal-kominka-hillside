using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KominkaDogRun.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaHillside.Editor
{
    // BUG-ENV-002 (2026-10-13, user: the main station's duckboard is buried in the soil): the station root's height rule.
    //
    // Rule (one rule, three users: the builder Wash/11, the premium-safe reseat Wash/25, the Wash/12 check):
    //   root y = max( the highest ground under the shelf footprint,
    //                 the highest ground under the duckboard footprint - the duckboard's local y at lift 0 ) + StationClearance
    // sampled with StationGround (the terrain only) at RectSamples (centre + 4 corners) of the shelf top (1.2 x 0.45 at z 0) and of
    // the duckboard (1.3 x 0.7 at z -TableFront), so neither the shelf's legs nor the duckboard's bottom is under the terrain.
    // Only y changes: x, z and yaw are kept exactly (棚と洗い場の位置は保つ). Everything else hangs off the root (Homes, tools, Lift
    // with the duckboard / Anchor, PersonalFloor, Board, Guide, the premium parts and SupportGroundRoot), all in station-local
    // numbers, so they move with it and none of their local values goes stale.
    // The builder seats the duckboard at (highest ground under it) + StationClearance, station-local (BuildStand); with the root at
    // max(shelf, duckboard) + StationClearance that is a fixed point of the rule above, so 11 twice and 25 after 11 give the same y.
    // On a slope the rule lifts the whole station: the shelf's legs stand above the lower ground by up to the footprint's range
    // (Wash/24 reports it; over FootprintRangeRelocate it lists flatter spots nearby, report only).
    public static partial class KH_DogWashSetupEditor
    {
        const float StationClearance = 0.01f;               // m above the highest ground sample (also against Z-fighting, IMP-ENV-007 2026-10-11)
        const float DuckboardBuriedTol = 0.005f;            // Wash/12: duckboard bottom - ground >= -this at every corner (lift 0)
        const float DuckboardFloatMax = 0.35f;              // Wash/12: ... and <= this (not floating)
        const float FootprintRangeRelocate = 0.25f;         // Wash/24: ground max - min over the shelf + duckboard samples above this -> relocate candidate
        const float RelocateRadius = 3f, RelocateStep = 0.25f, RelocateSeparation = 0.75f;
        const int RelocateCount = 3;
        const string GroundJsonPath = "Temp/KH_WashStationGround.json", GroundMdPath = "Temp/KH_WashStationGround.md", ReseatReportPath = "Temp/KH_WashStationReseat.txt";
        /// <summary>The main station's working area in station-local metres (as KH_GuideBoardsEditor WorkHalfX / WorkMinZ / WorkMaxZ).</summary>
        const float GroundWorkHalfX = 1.65f, GroundWorkMinZ = -1.55f, GroundWorkMaxZ = 0.225f;
        const float RoamRadius = 3f;                        // KH_Dogs/KH_Roam_* free-roam circles (KH_GuideBoardsEditor.Verify RoamClear 3.3 = 3 + 0.3)

        static readonly string[] RectSampleNames = { "centre", "front-left", "front-right", "back-left", "back-right" };   // RectSamples order (-x -z, +x -z, -x +z, +x +z)

        static Vector3[] ShelfGroundSamples() { return RectSamples(Vector3.zero, ShelfWidth, ShelfDepth); }
        static Vector3[] BoardGroundSamples() { return RectSamples(new Vector3(0f, 0f, -TableFront), TableSize.x, TableSize.z); }

        /// <summary>The highest terrain (world y) under the shelf and under the duckboard samples of a station pose (y of pos is only the
        /// ray's start hint). Throws when a sample has no terrain under it.</summary>
        static void FootprintGroundMax(Vector3 pos, Quaternion rot, out float shelfMax, out float boardMax)
        {
            shelfMax = float.NegativeInfinity; boardMax = float.NegativeInfinity;
            foreach (var p in ShelfGroundSamples()) shelfMax = Mathf.Max(shelfMax, StationGround(null, pos + rot * p));
            foreach (var p in BoardGroundSamples()) boardMax = Mathf.Max(boardMax, StationGround(null, pos + rot * p));
        }

        /// <summary>The builder's root y (Wash/11, new or kept stations): the duckboard is re-seated afterwards (BuildStand).</summary>
        static float BuilderRootY(Vector3 pos, Quaternion rot)
        {
            float shelfMax, boardMax;
            FootprintGroundMax(pos, rot, out shelfMax, out boardMax);
            return Mathf.Max(shelfMax, boardMax) + StationClearance;
        }

        /// <summary>The root y for a station whose duckboard keeps its local y (tableLocalY, at lift 0): Wash/25 and the Wash/12 check.</summary>
        static float KeptRootY(Vector3 pos, Quaternion rot, float tableLocalY)
        {
            float shelfMax, boardMax;
            FootprintGroundMax(pos, rot, out shelfMax, out boardMax);
            return Mathf.Max(shelfMax, boardMax - tableLocalY) + StationClearance;
        }

        /// <summary>Wash/11: writes the root's y by the builder rule (x, z, yaw untouched) and logs the change.</summary>
        static void SeatStationRoot(Transform st, BuildReport r)
        {
            var before = st.position;
            float y = BuilderRootY(before, st.rotation);
            if (Mathf.Abs(y - before.y) > 1e-5f) st.position = new Vector3(before.x, y, before.z);
            r.log.Add(st.name + " root y " + before.y.ToString("F3") + " -> " + y.ToString("F3") + " (highest ground under the shelf / duckboard + " + StationClearance + "; x, z, yaw kept)");
        }

        // ================================================================ 24 (read only)

        [Serializable] class GroundSample
        {
            public string part, name; public float x, z, ground, delta; public bool shelf; public float shelfDelta;
        }
        [Serializable] class GroundStation
        {
            public string name, error = "";
            public Vector3 root; public float yaw, liftLocalY, tableLocalY, boardBottomY;
            public float boardDeltaMin, boardDeltaMax, shelfLegDeltaMin, shelfLegDeltaMax;
            public float footprintGroundMin, footprintGroundMax, footprintRange, workGroundMin, workGroundMax, workRange;
            public float ruleRootY, ruleChange;
            public bool relocateCandidate;
            public List<GroundSample> samples = new List<GroundSample>();
            public List<string> distances = new List<string>();
            public List<string> candidates = new List<string>();
        }
        [Serializable] class GroundReport
        {
            public string utc, scene, rule;
            public List<GroundStation> stations = new List<GroundStation>();
        }

        [MenuItem(Menu + "24 - Measure Station Ground")]
        public static void MeasureStationGround()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorSceneManager.GetActiveScene().path != KH_CourseGroundEditor.ScenePath)
                throw new InvalidOperationException("[KH Wash Ground] the production scene in idle Edit mode is required");
            var scene = EditorSceneManager.GetActiveScene();
            Physics.SyncTransforms();
            var rep = new GroundReport
            {
                utc = DateTime.UtcNow.ToString("o"), scene = scene.path,
                rule = "lift 0; duckboard bottom = root y + WashTable local y; delta = duckboard bottom - ground (every sample); shelfDelta = root y (the legs' foot) - ground; "
                    + "rule root y = max(shelf ground max, duckboard ground max - WashTable local y) + " + StationClearance + " (Wash/25); relocate candidate when the footprint range or the shelf legs' lift after Wash/25 > " + FootprintRangeRelocate,
            };
            var stations = AllStationTransforms(scene);
            if (stations.Count == 0) throw new InvalidOperationException("[KH Wash Ground] no wash station in the scene");
            var md = new StringBuilder("# Wash station ground (BUG-ENV-002)\n\n" + rep.utc + " / " + rep.scene + "\n\n" + rep.rule + "\n");
            foreach (var st in stations)
            {
                var g = new GroundStation { name = KD_SceneObjects.PathOf(st.gameObject) };
                rep.stations.Add(g);
                try { MeasureOne(scene, st, g, stations); }
                catch (Exception e) { g.error = e.GetType().Name + ": " + e.Message; }
                md.Append(GroundMarkdown(g));
                if (g.error.Length > 0) Debug.LogWarning("[KH Wash Ground] " + g.name + ": " + g.error);
                else Debug.Log("[KH Wash Ground] " + g.name + ": duckboard min delta " + g.boardDeltaMin.ToString("+0.000;-0.000") + " m (max " + g.boardDeltaMax.ToString("+0.000;-0.000")
                    + "), footprint range " + g.footprintRange.ToString("F3") + " m, lift " + g.liftLocalY.ToString("F3") + ", table local y " + g.tableLocalY.ToString("F3")
                    + ", rule root y " + g.ruleRootY.ToString("F3") + " (" + g.ruleChange.ToString("+0.000;-0.000") + ")" + (g.relocateCandidate ? ", RELOCATE CANDIDATE (" + g.candidates.Count + " spots)" : ""));
            }
            WriteJson(GroundJsonPath, rep);
            File.WriteAllText(GroundMdPath, md.ToString());
            Debug.Log("[KH Wash Ground] measured " + rep.stations.Count + " stations (read only) -> " + GroundJsonPath + " / " + GroundMdPath);
        }

        static void MeasureOne(UnityEngine.SceneManagement.Scene scene, Transform st, GroundStation g, List<Transform> stations)
        {
            var lift = st.Find(LiftName);
            var table = FindTable(st);
            if (table == null) throw new InvalidOperationException("no " + TableName);
            g.root = st.position; g.yaw = st.eulerAngles.y;
            g.liftLocalY = lift != null ? lift.localPosition.y : 0f;
            // the duckboard's local y in the station at lift 0 (under Lift since IMP-ENV-011: Lift's own offset is the lift, left out)
            g.tableLocalY = table.parent == lift && lift != null ? table.localPosition.y : st.InverseTransformPoint(table.position).y;
            g.boardBottomY = st.position.y + g.tableLocalY;
            Func<string, string, Vector3, bool, GroundSample> sample = (part, name, local, shelf) =>
            {
                var w = st.position + st.rotation * new Vector3(local.x, 0f, local.z);
                float gy = StationGround(st, w);
                var s = new GroundSample { part = part, name = name, x = w.x, z = w.z, ground = gy, delta = g.boardBottomY - gy, shelf = shelf, shelfDelta = shelf ? st.position.y - gy : 0f };
                g.samples.Add(s);
                return s;
            };
            var board = BoardGroundSamples(); var shelfPts = ShelfGroundSamples();
            for (int i = 0; i < board.Length; i++) sample("duckboard", RectSampleNames[i], board[i], false);
            for (int i = 1; i < shelfPts.Length; i++) sample("shelf", RectSampleNames[i], shelfPts[i], true);   // the 4 corners (the legs stand there)
            var work = new[] { new Vector3(-GroundWorkHalfX, 0f, GroundWorkMinZ), new Vector3(GroundWorkHalfX, 0f, GroundWorkMinZ), new Vector3(-GroundWorkHalfX, 0f, GroundWorkMaxZ), new Vector3(GroundWorkHalfX, 0f, GroundWorkMaxZ) };
            for (int i = 0; i < work.Length; i++) sample("work area", RectSampleNames[i + 1], work[i], false);

            var bs = g.samples.Where(s => s.part == "duckboard").ToList();
            var ss = g.samples.Where(s => s.part == "shelf").ToList();
            var foot = bs.Concat(ss).ToList();
            g.boardDeltaMin = bs.Min(s => s.delta); g.boardDeltaMax = bs.Max(s => s.delta);
            g.shelfLegDeltaMin = ss.Min(s => s.shelfDelta); g.shelfLegDeltaMax = ss.Max(s => s.shelfDelta);
            g.footprintGroundMin = foot.Min(s => s.ground); g.footprintGroundMax = foot.Max(s => s.ground); g.footprintRange = g.footprintGroundMax - g.footprintGroundMin;
            g.workGroundMin = g.samples.Min(s => s.ground); g.workGroundMax = g.samples.Max(s => s.ground); g.workRange = g.workGroundMax - g.workGroundMin;
            g.ruleRootY = KeptRootY(st.position, st.rotation, g.tableLocalY);
            g.ruleChange = g.ruleRootY - st.position.y;

            bool main = st.name == StationName && st.parent == null;
            if (main) g.distances.AddRange(MainNeighbourDistances(scene, st.position, st.rotation, "now"));
            // the slope, or the shelf's legs left that far above the lowest footprint ground once Wash/25 lifts the root
            g.relocateCandidate = g.footprintRange > FootprintRangeRelocate || g.ruleRootY - g.footprintGroundMin > FootprintRangeRelocate;
            if (g.relocateCandidate) g.candidates.AddRange(RelocateCandidates(scene, st, main, g.footprintRange, stations));
        }

        static string GroundMarkdown(GroundStation g)
        {
            var s = new StringBuilder("\n## " + g.name + "\n\n");
            if (g.error.Length > 0) return s.Append("ERROR " + g.error + "\n").ToString();
            s.Append("root " + g.root.ToString("F3") + " yaw " + g.yaw.ToString("F1") + "; liftRoot local y " + g.liftLocalY.ToString("F3") + " (measured at lift 0); WashTable local y " + g.tableLocalY.ToString("F3")
                + "; duckboard bottom (lift 0) " + g.boardBottomY.ToString("F3") + "\n\n");
            s.Append("| part | sample | x | z | ground y | delta (duckboard bottom - ground) | shelf leg foot - ground |\n|---|---|---|---|---|---|---|\n");
            foreach (var x in g.samples)
                s.Append("| " + x.part + " | " + x.name + " | " + x.x.ToString("F2") + " | " + x.z.ToString("F2") + " | " + x.ground.ToString("F3") + " | " + x.delta.ToString("+0.000;-0.000") + " | " + (x.shelf ? x.shelfDelta.ToString("+0.000;-0.000") : "") + " |\n");
            s.Append("\n- duckboard delta min / max: " + g.boardDeltaMin.ToString("+0.000;-0.000") + " / " + g.boardDeltaMax.ToString("+0.000;-0.000") + " m (< 0 = buried)\n");
            s.Append("- shelf leg foot - ground min / max: " + g.shelfLegDeltaMin.ToString("+0.000;-0.000") + " / " + g.shelfLegDeltaMax.ToString("+0.000;-0.000") + " m\n");
            s.Append("- ground over the footprint (shelf + duckboard samples): " + g.footprintGroundMin.ToString("F3") + " .. " + g.footprintGroundMax.ToString("F3") + ", range " + g.footprintRange.ToString("F3") + " m (slope indicator)\n");
            s.Append("- ground over all samples (incl. the work area corners): range " + g.workRange.ToString("F3") + " m\n");
            s.Append("- rule root y (Wash/25) " + g.ruleRootY.ToString("F3") + " (" + g.ruleChange.ToString("+0.000;-0.000") + " m); after it the shelf legs' foot stands up to "
                + (g.ruleRootY - g.footprintGroundMin).ToString("F3") + " m above the lowest footprint ground\n");
            foreach (var d in g.distances) s.Append("- " + d + "\n");
            if (g.relocateCandidate)
            {
                s.Append("- **relocate candidate** (footprint range or the shelf legs' lift after Wash/25 > " + FootprintRangeRelocate + " m). Flatter spots within " + RelocateRadius + " m (same yaw; report only, nothing moved):\n");
                if (g.candidates.Count == 0) s.Append("  - none passed the checks\n");
                foreach (var c in g.candidates) s.Append("  - " + c + "\n");
            }
            return s.ToString();
        }

        /// <summary>Footprint range (max - min of the shelf + duckboard samples) at a pose. Throws without terrain.</summary>
        static float FootprintRange(Vector3 pos, Quaternion rot)
        {
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            foreach (var p in ShelfGroundSamples().Concat(BoardGroundSamples())) { float y = StationGround(null, pos + rot * p); lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
            return hi - lo;
        }

        /// <summary>Up to RelocateCount spots within RelocateRadius of the root (same yaw) whose footprint range is smaller than now, the
        /// flattest first, each passing the checks the Build uses: course stations every PlacementChecks rule; the main station the solid
        /// colliders over the working area, the duckboard on the NavMesh, and Wash/12's layout clearances (1.5 m from the spawns, the gate
        /// facilities, the treats, the course start and the dogs' waiting ring). Reads only.</summary>
        static List<string> RelocateCandidates(UnityEngine.SceneManagement.Scene scene, Transform st, bool main, float rangeNow, List<Transform> stations)
        {
            var o = new List<string>();
            var rot = st.rotation;
            CourseCtx cc = null; StationSpec spec = null; string missing = "";
            if (!main)
            {
                spec = CourseStations.FirstOrDefault(s => s.key == st.name);
                cc = ResolveCourse(scene, out missing);
                if (spec == null || cc == null) { o.Add("course context missing (" + (spec == null ? "no table row for " + st.name : missing) + "): no candidates checked"); return o; }
            }
            var spots = main ? MainLayoutSpots(scene) : null;
            var tried = new List<KeyValuePair<float, Vector3>>();
            int n = Mathf.RoundToInt(RelocateRadius / RelocateStep);
            for (int i = -n; i <= n; i++)
                for (int j = -n; j <= n; j++)
                {
                    if (i == 0 && j == 0) continue;
                    var off = new Vector3(i * RelocateStep, 0f, j * RelocateStep);
                    if (off.magnitude > RelocateRadius + 1e-4f) continue;
                    var p = st.position + off;
                    float range;
                    try { range = FootprintRange(p, rot); } catch (Exception) { continue; }
                    if (range < rangeNow - 1e-3f) tried.Add(new KeyValuePair<float, Vector3>(range, p));
                }
            int rejected = 0;
            var picked = new List<Vector3>();
            foreach (var kv in tried.OrderBy(k => k.Key).ThenBy(k => FlatDist(k.Value, st.position)))
            {
                if (picked.Count >= RelocateCount) break;
                var p = kv.Value;
                if (picked.Any(q => FlatDist(q, p) < RelocateSeparation)) continue;
                Vector3 pos;
                try { pos = new Vector3(p.x, BuilderRootY(p, rot), p.z); } catch (Exception) { continue; }
                var fails = new List<string>();
                if (main)
                {
                    float plane, steep, hmin, hmax;
                    try { SlopeMeasure(pos, rot, out plane, out steep, out hmin, out hmax); } catch (Exception e) { fails.Add("ground: " + e.Message); hmin = hmax = pos.y; }
                    var hits = WorkAreaObstacles(pos, rot, hmin, hmax, st);
                    if (hits.Length > 0) fails.Add("solid colliders over the working area: " + string.Join(", ", hits));
                    float nav;
                    if (!DuckboardOnNavMesh(pos, rot, out nav)) fails.Add("the duckboard centre is not within " + CourseNavReach + " m of the NavMesh");
                    var prints = StationFootprints();
                    foreach (var s in spots)
                    {
                        string nm; float d = FootDist(pos, rot, prints, s.Value, out nm);
                        if (d < LayoutClearance) fails.Add(s.Key + " " + d.ToString("F2") + " m from the " + nm + " (< " + LayoutClearance + ")");
                    }
                    float ring = RingDistanceAt(pos, rot, prints);
                    if (ring < LayoutClearance) fails.Add("the dogs' waiting ring (Wash/12 DogWaitRing) " + ring.ToString("F2") + " m (< " + LayoutClearance + ")");
                }
                else fails.AddRange(PlacementChecks(cc, spec, pos, rot, st, stations).Where(x => !x.Key).Select(x => x.Value));
                if (fails.Count > 0) { rejected++; continue; }
                picked.Add(p);
                var line = "(" + pos.x.ToString("F2") + ", " + pos.y.ToString("F2") + ", " + pos.z.ToString("F2") + ") yaw " + rot.eulerAngles.y.ToString("F1") + ": " + FlatDist(pos, st.position).ToString("F2")
                    + " m from the root, footprint range " + kv.Key.ToString("F3") + " m (now " + rangeNow.ToString("F3") + "), root y by the builder rule " + pos.y.ToString("F3");
                if (main) line += "; " + string.Join("; ", MainNeighbourDistances(scene, pos, rot, "there"));
                o.Add(line);
            }
            o.Add("(" + tried.Count + " grid points flatter than now within " + RelocateRadius + " m at " + RelocateStep + " m steps; " + rejected + " flatter ones rejected by the checks before these)");
            return o;
        }

        /// <summary>Wash/12's hand-placed spots for the main station (CheckStationLayoutInner): the spawns, KH_Entrance, KD_ShopKeeper,
        /// KD_WelcomeSpot, KH_LeashRack, KD_Treat_*, the course start.</summary>
        static List<KeyValuePair<string, Vector3>> MainLayoutSpots(UnityEngine.SceneManagement.Scene scene)
        {
            var spots = new List<KeyValuePair<string, Vector3>>();
            var desc = UnityEngine.Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
            if (desc != null && desc.spawns != null) for (int i = 0; i < desc.spawns.Length; i++) if (desc.spawns[i] != null) spots.Add(new KeyValuePair<string, Vector3>("spawn " + i, desc.spawns[i].position));
            foreach (var nm in new[] { "KH_Entrance", "KD_ShopKeeper", "KD_WelcomeSpot", RackName })
            { var go = KD_SceneObjects.Find(nm); if (go != null) spots.Add(new KeyValuePair<string, Vector3>(nm, go.transform.position)); }
            var treat = new System.Text.RegularExpressions.Regex("^KD_Treat_\\d+$");
            var treats = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Where(t => treat.IsMatch(t.name)).ToArray();
            foreach (var x in treats) spots.Add(new KeyValuePair<string, Vector3>(x.name, x.position));
            var route = UnityEngine.Object.FindObjectsOfType<KH_WalkRoute>(true);
            if (route.Length == 1 && route[0].nodes != null && route[0].nodes.Length > 0 && route[0].nodes[0] != null) spots.Add(new KeyValuePair<string, Vector3>("course start", route[0].nodes[0].position));
            return spots;
        }

        /// <summary>The main station's footprint (shelf + duckboard) distances at a pose to the gate facilities, the dogs' free-roam
        /// circles (KH_Roam_*, radius RoamRadius: the circle's edge) and the dirt zones (KH_DirtZones children, centre). Report only.</summary>
        static List<string> MainNeighbourDistances(UnityEngine.SceneManagement.Scene scene, Vector3 pos, Quaternion rot, string tag)
        {
            var o = new List<string>();
            var prints = StationFootprints();
            foreach (var nm in new[] { RackName, "KH_Entrance", "KD_ShopKeeper" })
            {
                int count; var go = KD_SceneObjects.Find(nm, out count);
                if (go == null) { o.Add(tag + ": " + nm + " not found"); continue; }
                string near; float d = FootDist(pos, rot, prints, go.transform.position, out near);
                o.Add(tag + ": " + nm + " " + d.ToString("F2") + " m from the " + near);
            }
            var roams = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Where(t => t.name.StartsWith("KH_Roam_")).ToList();
            if (roams.Count == 0) o.Add(tag + ": no KH_Roam_* found");
            foreach (var t in roams) { string near; float d = FootDist(pos, rot, prints, t.position, out near); o.Add(tag + ": " + t.name + " centre " + d.ToString("F2") + " m from the " + near + " (circle edge " + (d - RoamRadius).ToString("+0.00;-0.00") + " m)"); }
            var zones = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "KH_DirtZones");
            if (zones == null) o.Add(tag + ": no KH_DirtZones");
            else foreach (Transform z in zones.transform) { string near; float d = FootDist(pos, rot, prints, z.position, out near); o.Add(tag + ": dirt zone " + z.name + " centre " + d.ToString("F2") + " m from the " + near); }
            return o;
        }

        /// <summary>RingDistance (StationVerify.cs) for a pose instead of a Transform: footprint to the dogs' waiting ring rectangle.</summary>
        static float RingDistanceAt(Vector3 pos, Quaternion rot, Footprint[] prints)
        {
            float best = float.PositiveInfinity;
            var ringCorners = new[] { new Vector2(DogWaitRing.xMin, DogWaitRing.yMin), new Vector2(DogWaitRing.xMax, DogWaitRing.yMin), new Vector2(DogWaitRing.xMin, DogWaitRing.yMax), new Vector2(DogWaitRing.xMax, DogWaitRing.yMax) };
            foreach (var f in prints)
            {
                foreach (var c in ringCorners) { string n; best = Mathf.Min(best, FootDist(pos, rot, new[] { f }, new Vector3(c.x, pos.y, c.y), out n)); }
                for (int i = 0; i < 4; i++)
                {
                    var w = pos + rot * new Vector3(f.centre.x + ((i & 1) == 0 ? -0.5f : 0.5f) * f.size.x, 0f, f.centre.y + ((i & 2) == 0 ? -0.5f : 0.5f) * f.size.y);
                    float dx = Mathf.Max(DogWaitRing.xMin - w.x, 0f, w.x - DogWaitRing.xMax), dz = Mathf.Max(DogWaitRing.yMin - w.z, 0f, w.z - DogWaitRing.yMax);
                    best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
                }
            }
            return best;
        }

        // ================================================================ 25 (premium-safe; writes only the roots' y)

        /// <summary>Wash/11 refuses once the premium selection is installed, so this is the targeted fix: every station root's y by
        /// KeptRootY (the duckboard keeps its local y, so Lift / Anchor / PersonalFloor / standBasePos / SupportGroundRoot / freeBaseY stay
        /// valid). x, z and yaw are not touched. Marks the scene dirty, does not save. Next: Wash/24 (measure), save, Wash/12,
        /// WashPremium/50 (bathMinLifts: the ground under the seat moved), WashPremium/44, save.</summary>
        [MenuItem(Menu + "25 - Reseat Station Roots (premium-safe)")]
        public static void ReseatStationRoots()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);   // idle Edit mode, the production scene, saved
            var scene = EditorSceneManager.GetActiveScene();
            Physics.SyncTransforms();
            var stations = AllStationTransforms(scene);
            if (stations.Count == 0) throw new InvalidOperationException("[KH Wash Ground] no wash station in the scene (nothing changed)");
            // measure everything first: nothing is written if one station cannot be measured
            var plan = new List<KeyValuePair<Transform, float>>();
            var log = new StringBuilder();
            foreach (var st in stations)
            {
                var lift = st.Find(LiftName); var table = FindTable(st);
                if (lift == null || table == null || table.parent != lift) throw new InvalidOperationException("[KH Wash Ground] " + st.name + ": " + LiftName + "/" + TableName + " missing (nothing changed)");
                if (lift.localPosition.sqrMagnitude > 1e-8f) throw new InvalidOperationException("[KH Wash Ground] " + st.name + ": Lift is not at rest (local " + lift.localPosition.ToString("F3") + "; nothing changed)");
                var e = st.eulerAngles;
                if (Mathf.Abs(Mathf.DeltaAngle(e.x, 0f)) > 0.01f || Mathf.Abs(Mathf.DeltaAngle(e.z, 0f)) > 0.01f) throw new InvalidOperationException("[KH Wash Ground] " + st.name + " is not yaw-only (" + e.ToString("F2") + "; nothing changed)");
                float shelfMax, boardMax;
                FootprintGroundMax(st.position, st.rotation, out shelfMax, out boardMax);
                float y = KeptRootY(st.position, st.rotation, table.localPosition.y);
                plan.Add(new KeyValuePair<Transform, float>(st, y));
                log.AppendLine(KD_SceneObjects.PathOf(st.gameObject) + ": root y " + st.position.y.ToString("F4") + " -> " + y.ToString("F4") + " (" + (y - st.position.y).ToString("+0.0000;-0.0000") + "); shelf ground max "
                    + shelfMax.ToString("F4") + ", duckboard ground max " + boardMax.ToString("F4") + ", WashTable local y " + table.localPosition.y.ToString("F4") + " -> duckboard bottom " + (y + table.localPosition.y).ToString("F4")
                    + " (" + (y + table.localPosition.y - boardMax).ToString("+0.0000;-0.0000") + " over its highest ground); x " + st.position.x.ToString("F4") + " z " + st.position.z.ToString("F4") + " yaw " + e.y.ToString("F2") + " kept");
            }
            int changed = 0;
            foreach (var kv in plan)
            {
                var st = kv.Key; var p = st.position;
                if (Mathf.Abs(kv.Value - p.y) <= 1e-5f) continue;
                Undo.RecordObject(st, "Reseat wash station root");
                st.position = new Vector3(p.x, kv.Value, p.z);
                EditorUtility.SetDirty(st);
                changed++;
            }
            if (changed > 0) EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory("Temp");
            File.WriteAllText(ReseatReportPath, DateTime.UtcNow.ToString("o") + "\nBUG-ENV-002 Wash/25 Reseat Station Roots: stations=" + plan.Count + " changed=" + changed + " (0 on a re-run). NOT saved.\n"
                + "Next: Wash/24 (measure), save, Wash/12, WashPremium/50 then WashPremium/44, save.\n" + log);
            Debug.Log("[KH Wash Ground] reseated " + changed + " of " + plan.Count + " station roots (y only, not saved). " + ReseatReportPath + "\n" + log);
        }

        // ================================================================ Wash/12

        /// <summary>The duckboard's bottom at lift 0 against the terrain at its 4 corners (not buried, not floating), and the root on the
        /// height rule (KeptRootY: what Wash/11 / Wash/25 leave). Called by CheckStationBody for every station.</summary>
        static void CheckDuckboardGround(VerifyReport r, Action<bool, string> req, Transform st, Transform table, string label)
        {
            float tl = table.localPosition.y;   // under Lift at the station's origin (lift 0 in the saved scene)
            float bottom = st.position.y + tl;
            var pts = BoardGroundSamples();
            var parts = new List<string>(); float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            for (int i = 1; i < pts.Length; i++)
            {
                float d = bottom - StationGround(st, st.position + st.rotation * pts[i]);
                lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d);
                parts.Add(RectSampleNames[i] + " " + d.ToString("+0.000;-0.000"));
            }
            float centre = bottom - StationGround(st, st.position + st.rotation * pts[0]);
            string values = string.Join(", ", parts) + " m (centre " + centre.ToString("+0.000;-0.000") + "; WashTable local y " + tl.ToString("F3") + ", root y " + st.position.y.ToString("F3") + ")";
            req(lo >= -DuckboardBuriedTol, label + ": duckboard not buried: bottom - ground at the 4 corners (lift 0) >= -" + DuckboardBuriedTol + ": " + values);
            req(hi <= DuckboardFloatMax, label + ": duckboard not floating: bottom - ground at the 4 corners (lift 0) <= " + DuckboardFloatMax + ": " + values);
            float rule = KeptRootY(st.position, st.rotation, tl);
            req(Mathf.Abs(st.position.y - rule) < 0.005f, label + ": root y " + st.position.y.ToString("F3") + " on the height rule " + rule.ToString("F3")
                + " (max(shelf ground, duckboard ground - its local y) + " + StationClearance + "; Wash/25 or Wash/11 writes it, BUG-ENV-002)");
            r.notes.Add(label + ": duckboard ground (BUG-ENV-002) " + values);
        }
    }
}
