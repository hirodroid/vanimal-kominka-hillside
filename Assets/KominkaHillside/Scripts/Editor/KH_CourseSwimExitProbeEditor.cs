using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KominkaDogRun;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // BUG-DOG-012 (2026-10-12): Edit-mode probe of the swim floor. Read only: never writes the scene or an asset, no Play, idle Edit mode
    // on the production scene (saved, not dirty; dirtiness re-checked at the end). It calls the runtime's own functions on the U# proxies
    // of the six KD_DogWanderController (the code the dogs run): _WaterSwimRootY (the float formula of WaterSwimHeight, unfloored),
    // _WaterBedAt / _WaterSwimFloorY (the swim floor's riverbed: the spot's grid, else the river-wide grid), _WaterDepthAt (WDepth),
    // _WaterWetAt, _WaterSwimEnterDepth / _WaterSwimLeaveDepth, _WaterSwimSpeedNow, on the saved Terrain data of Course/73 / 76 / 90.
    //   1 per breed: draft (the float's root under the water: Ms + sink - swimClear, minus carryExtra while carrying), the swim start /
    //     end depths, root - riverbed at those depths, and the depth an afloat water legs frame needs (WaterLegsSwimOk: max(leave,
    //     draft - 0.03)).
    //   2 static table, per breed x carrying x region (the spot's grid vertices inside its polygon / the river-wide grid outside the
    //     spot's grid), every wet point (depth > 0.02 m): the unfloored float - riverbed (the code before the fix) and the floored one,
    //     split by depth: below the leave depth (the swim flag is up there only during the 0.5 s hold or until the dry edge) and at or
    //     above it (a legitimate float the floor lifts: the visible change of the fix), and at or above the swim start depth.
    //   3 paths (the reproduction "afloat while walking out into the shallows"): a swimmer from the deepest water straight to each spot
    //     exit, and from the deepest point of a river row to the nearest ok bank slice on each side, at its swim speed, 60 fps, with the
    //     runtime's swim end rule (dry, or below the leave depth for swimLeaveHold s; the height of a frame is computed with the flag
    //     of the frame before, as WaterSwimHeight runs before WaterPoseRefresh). Min root - riverbed while the flag is up, unfloored and
    //     floored, the depth and the reason where the swim ended. Straight lines: the runtime's own path (StepToward, push-apart,
    //     thinned decisions) is not replayed (78s W-swim-exit does that in Play).
    //   checks: known success (a point at least draft - 0.03 deep: the floor moves nothing) and known refusal (a point 0.02-0.05 m deep:
    //     the floored root lands at exactly riverbed - 0.03).
    //   fact: the spot's grid against the river-wide grid inside the spot (the leash's floor used the river-wide grid until BUG-DOG-012).
    // Result: Temp/kh-leash-river/swim-exit-probe.json and the Console ([KH Swim Exit Probe]).
    public static class KH_CourseSwimExitProbeEditor
    {
        const string OutDir = "Temp/kh-leash-river";
        static readonly string[] Breeds = { "Corgi", "Pome", "Lab", "Dachs", "Shiba", "Samoyed" };
        const float Wet = .02f, Fps = 60f, PathMax = 60f;

        [Serializable] public class Region
        {
            public string region; public bool carrying;
            public int wetPoints, shallowPoints, shallowFloorActs, swimRangePoints, swimRangeFloorActs, enterPoints, enterFloorActs;
            public float minRawLift = 9f, minRawDepth; public Vector3 minRawAt;
            public float minFlooredLift = 9f;
            public float maxSwimRangeLift, maxSwimRangeLiftDepth, maxEnterLift;
        }
        [Serializable] public class PathRow
        {
            public string name; public bool carrying; public Vector3 from, to; public float fromDepth;
            public bool swam; public int afloatFrames, floorFrames;
            public float minRawLift = 9f, minRawDepth, minFlooredLift = 9f, endDepth, endSeconds;
            public string end;
        }
        [Serializable] public class BreedRow
        {
            public string name; public int dogIndex; public bool swims; public float M, Ms, sink, draft, carryDraft, enter, leave, legsFloatLow, swimSpeed;
            public float liftAtEnter, liftAtLeave, liftAt002;
            public List<Region> regions = new List<Region>();
            public List<PathRow> paths = new List<PathRow>();
            public int pathsSwum, pathsReproduced;
            public float worstPathRaw = 9f, worstPathFloored = 9f; public string worstPath;
        }
        [Serializable] public class Report
        {
            public string status = "DONE", utc, scene;
            public bool dirtyAfter;
            public float floor = KD_DogWanderController.WaterSwimFloorDepth, swimLeaveHold;
            public float spotDeepest; public Vector3 spotDeepAt;
            public int spotRiverBedPairs; public float spotRiverBedMaxAbs, spotRiverBedMean;
            public List<BreedRow> breeds = new List<BreedRow>();
            public List<string> summary = new List<string>(), findings = new List<string>(), facts = new List<string>(), checks = new List<string>();
        }

        [MenuItem("Tools/KominkaHillside/Course/132 - Probe Swim Exit Floor")]
        public static void Probe()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = new Report { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            var wanders = UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true).Where(w => w.dogIndex >= 0 && w.dogIndex < 6).OrderBy(w => w.dogIndex).ToArray();
            if (wanders.Length != 6) throw new InvalidOperationException("Six KD_DogWanderController with dogIndex 0-5 required (" + wanders.Length + ")");
            var spot = wanders[0].waterSpot; var river = wanders[0].riverData;
            if (spot == null || !spot._Ready() || river == null || !river._FetchReady()) throw new InvalidOperationException("waterSpot / riverData not wired or not built (Course/76 / 73 / 90)");
            foreach (var w in wanders)
                if (w.riverData != river || w.waterSpot != spot) r.findings.Add(Breeds[w.dogIndex] + ": riverData / waterSpot not wired like dog 0 (Course/76 / 90)");
            r.swimLeaveHold = spot.swimLeaveHold;

            var spotPts = SpotPoints(spot);
            var riverPts = RiverPoints(spot, river);
            BedFact(r, spot, river, spotPts);
            foreach (var p in spotPts) { float d = wanders[0]._WaterDepthAt(p); if (d > r.spotDeepest) { r.spotDeepest = d; r.spotDeepAt = p; } }
            foreach (var w in wanders) r.breeds.Add(BreedTable(r, w, spot, river, spotPts, riverPts));
            Summarise(r);

            r.dirtyAfter = SceneManager.GetActiveScene().isDirty;
            if (r.dirtyAfter) r.findings.Add("Scene became dirty during the probe (it writes nothing: investigate before saving)");
            if (r.findings.Count > 0) r.status = "DONE_WITH_FINDINGS";
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/swim-exit-probe.json", JsonUtility.ToJson(r, true));
            foreach (var line in r.summary) Debug.Log("[KH Swim Exit Probe] " + line);
            foreach (var f in r.findings) Debug.LogWarning("[KH Swim Exit Probe] FINDING " + f);
            Debug.Log("[KH Swim Exit Probe] " + r.status + " findings=" + r.findings.Count + " -> " + OutDir + "/swim-exit-probe.json");
        }

        /// <summary>The spot's grid vertices inside its polygon with a riverbed (the swim region of water play and the spot Fetch).</summary>
        static List<Vector3> SpotPoints(KH_WaterSpot s)
        {
            var list = new List<Vector3>();
            for (int iz = 0; iz < s.gridNz; iz++)
                for (int ix = 0; ix < s.gridNx; ix++)
                {
                    var p = new Vector3(s.gridX0 + ix * s.gridStep, 0f, s.gridZ0 + iz * s.gridStep);
                    float b = s._Bed(p);
                    if (b <= KH_WaterSpot.NoBed + 1f || !s._Contains(p)) continue;
                    p.y = b; list.Add(p);
                }
            return list;
        }

        /// <summary>The river-wide grid points in the Fetch's water (_FetchWater) where the spot's grid has no riverbed.</summary>
        static List<Vector3> RiverPoints(KH_WaterSpot s, KH_RiverData d)
        {
            var list = new List<Vector3>();
            for (int row = 0; row < d.fetchRows; row++)
            {
                float z = d.fetchZ0 + row * d.fetchStep;
                for (int i = d.fetchRowStart[row]; i < d.fetchRowStart[row + 1]; i++)
                {
                    float bed = d.fetchBed[i];
                    if (bed <= KH_RiverData.NoBed + 1f) continue;
                    var p = new Vector3(d.fetchRowX0[row] + (i - d.fetchRowStart[row]) * d.fetchStep, bed, z);
                    if (!d._FetchWater(p) || s._Bed(p) > KH_WaterSpot.NoBed + 1f) continue;
                    list.Add(p);
                }
            }
            return list;
        }

        static void BedFact(Report r, KH_WaterSpot s, KH_RiverData d, List<Vector3> spotPts)
        {
            double sum = 0; int n = 0; float max = 0f;
            foreach (var p in spotPts)
            {
                float rb = d._BedAt(p);
                if (rb <= KH_RiverData.NoBed + 1f) continue;
                float diff = s._Bed(p) - rb; n++; sum += Mathf.Abs(diff); if (Mathf.Abs(diff) > max) max = Mathf.Abs(diff);
            }
            r.spotRiverBedPairs = n; r.spotRiverBedMaxAbs = max; r.spotRiverBedMean = n > 0 ? (float)(sum / n) : 0f;
            r.facts.Add("inside the spot, its grid's riverbed against the river-wide grid's (" + n + " points): mean |diff| " + r.spotRiverBedMean.ToString("F3") + " m, max " + max.ToString("F3")
                + " m (the swim floor uses the spot's grid there, as WDepth does; the leash's floor of IMP-DOG-053 used the river-wide grid)");
        }

        static BreedRow BreedTable(Report r, KD_DogWanderController w, KH_WaterSpot spot, KH_RiverData river, List<Vector3> spotPts, List<Vector3> riverPts)
        {
            var b = new BreedRow { name = Breeds[w.dogIndex], dogIndex = w.dogIndex, swims = w.waterSwimAllowed, M = w.waterMouthHeight, Ms = w.waterSwimMouth, sink = spot._SwimSink(w.dogIndex) };
            var refP = spotPts.Count > 0 ? spotPts[0] : new Vector3(river._CenterX(150f), 0f, 150f);
            b.draft = river._SurfaceY(refP.z) - w._WaterSwimRootY(refP, false);
            b.carryDraft = river._SurfaceY(refP.z) - w._WaterSwimRootY(refP, true);
            b.enter = w._WaterSwimEnterDepth(); b.leave = w._WaterSwimLeaveDepth();
            b.legsFloatLow = Mathf.Max(b.leave, b.draft - r.floor);
            b.swimSpeed = w._WaterSwimSpeedNow();
            b.liftAtEnter = b.enter - b.draft; b.liftAtLeave = b.leave - b.draft; b.liftAt002 = Wet - b.draft;
            foreach (bool carrying in new[] { false, true })
            {
                b.regions.Add(StaticRegion(r, b, w, "spot", spotPts, carrying));
                b.regions.Add(StaticRegion(r, b, w, "river", riverPts, carrying));
            }
            if (b.swims) Paths(r, b, w, spot, river, spotPts);
            return b;
        }

        static Region StaticRegion(Report r, BreedRow b, KD_DogWanderController w, string name, List<Vector3> pts, bool carrying)
        {
            var g = new Region { region = name, carrying = carrying };
            bool success = false, refusal = false;
            float draft = carrying ? b.carryDraft : b.draft;
            foreach (var p in pts)
            {
                float depth = w._WaterDepthAt(p);
                if (depth <= Wet) continue;
                g.wetPoints++;
                float bed = w._WaterBedAt(p), raw = w._WaterSwimRootY(p, carrying), fl = w._WaterSwimFloorY(p);
                if (bed <= KH_RiverData.NoBed + 1f) { r.findings.Add(b.name + " " + name + ": a wet point without a riverbed at " + p.ToString("F2")); continue; }
                float floored = Mathf.Max(raw, fl), rawLift = raw - bed, lift = floored - bed;
                bool acts = fl > raw;
                if (rawLift < g.minRawLift) { g.minRawLift = rawLift; g.minRawDepth = depth; g.minRawAt = p; }
                if (lift < g.minFlooredLift) g.minFlooredLift = lift;
                if (depth < b.leave) { g.shallowPoints++; if (acts) g.shallowFloorActs++; }
                else
                {
                    g.swimRangePoints++;
                    if (acts) { g.swimRangeFloorActs++; if (fl - raw > g.maxSwimRangeLift) { g.maxSwimRangeLift = fl - raw; g.maxSwimRangeLiftDepth = depth; } }
                    if (depth >= b.enter) { g.enterPoints++; if (acts) { g.enterFloorActs++; g.maxEnterLift = Mathf.Max(g.maxEnterLift, fl - raw); } }
                }
                // known success: at least draft - floor deep (+ 1 mm): the floor moves nothing
                if (depth >= draft - r.floor + .001f)
                {
                    if (acts) r.findings.Add(b.name + " " + name + (carrying ? " carrying" : "") + ": the floor lifted a float " + (fl - raw).ToString("F4") + " m at depth " + depth.ToString("F3") + " (>= draft - 0.03: expected untouched)");
                    else success = true;
                }
                // known refusal: 0.02-0.05 m deep, the unfloored float below the floor: the floored root at exactly riverbed - floor
                if (depth < .05f && rawLift < -r.floor)
                {
                    if (Mathf.Abs(lift + r.floor) > .0005f) r.findings.Add(b.name + " " + name + ": floored root - bed " + lift.ToString("F4") + " at depth " + depth.ToString("F3") + " (expected " + (-r.floor).ToString("F2") + ")");
                    else refusal = true;
                }
            }
            if (g.wetPoints > 0 && g.minFlooredLift < -r.floor - .0005f) r.findings.Add(b.name + " " + name + (carrying ? " carrying" : "") + ": floored root - bed " + g.minFlooredLift.ToString("F4") + " (< -" + r.floor.ToString("F2") + ")");
            if (g.wetPoints > 0 && b.swims)   // a non-swimmer is never afloat: its table is hypothetical, not a check
                r.checks.Add(b.name + " " + name + (carrying ? " carrying" : "") + ": known success (>= draft - 0.03, untouched) " + (success ? "seen" : "none in this region")
                    + ", known refusal (0.02-0.05 m, at bed - 0.03) " + (refusal ? "seen" : "none in this region"));
            return g;
        }

        /// <summary>Section 3: straight swims out of the deepest water (spot: to every exit; river: every 15 m, to the nearest ok slice
        /// on each side), not carrying and carrying.</summary>
        static void Paths(Report r, BreedRow b, KD_DogWanderController w, KH_WaterSpot spot, KH_RiverData river, List<Vector3> spotPts)
        {
            if (r.spotDeepest >= b.enter)
                for (int k = 0; k < spot._ExitCount(); k++)
                    foreach (bool carrying in new[] { false, true })
                        b.paths.Add(Swim(r, b, w, "spot deepest -> exit " + k, r.spotDeepAt, spot._Exit(k), carrying));
            else r.facts.Add(b.name + ": the spot's deepest water (" + r.spotDeepest.ToString("F3") + " m) is shallower than its swim start (" + b.enter.ToString("F3") + "): no spot swim");
            for (float z = 5f; z <= 715f; z += 15f)
            {
                if (!river._Covers(z) || river._InBridgeZone(z, 10f) || (z > 265f && z < 315f)) continue;
                Vector3 deep = Vector3.zero; float dd = 0f;
                float cx = river._CenterX(z), hw = river._HalfWidth(z);
                for (float x = -hw; x <= hw; x += .25f)
                {
                    var p = new Vector3(cx + x, 0f, z);
                    if (!river._FetchWater(p)) continue;
                    float d = w._WaterDepthAt(p);
                    if (d > dd) { dd = d; deep = p; }
                }
                if (dd < b.enter) continue;
                foreach (int side in new[] { -1, 1 })
                {
                    int code = river._NearestSlice(deep, side, river._BankReach(), -1);
                    if (code < 0) continue;
                    foreach (bool carrying in new[] { false, true })
                        b.paths.Add(Swim(r, b, w, "river z " + z.ToString("F0") + " side " + side + " -> slice " + code, deep, river._SliceBank(code), carrying));
                }
            }
            foreach (var row in b.paths)
            {
                if (!row.swam) continue;
                b.pathsSwum++;
                if (row.minRawLift < -r.floor) b.pathsReproduced++;
                if (row.minRawLift < b.worstPathRaw) { b.worstPathRaw = row.minRawLift; b.worstPath = row.name + (row.carrying ? " carrying" : "") + " (depth " + row.minRawDepth.ToString("F3") + ", swim ended " + row.end + " at depth " + row.endDepth.ToString("F3") + ")"; }
                b.worstPathFloored = Mathf.Min(b.worstPathFloored, row.minFlooredLift);
            }
            if (b.pathsSwum > 0 && b.worstPathFloored < -r.floor - .0005f) r.findings.Add(b.name + ": a path's floored root - bed " + b.worstPathFloored.ToString("F4") + " (< -" + r.floor.ToString("F2") + ")");
        }

        static PathRow Swim(Report r, BreedRow b, KD_DogWanderController w, string name, Vector3 from, Vector3 to, bool carrying)
        {
            var row = new PathRow { name = name, carrying = carrying, from = from, to = to, fromDepth = w._WaterDepthAt(from) };
            if (row.fromDepth < b.enter) { row.end = "not deep enough to swim"; return row; }
            row.swam = true;
            Vector3 p = from; p.y = 0f; var goal = to; goal.y = 0f;
            float step = b.swimSpeed / Fps, shallowSince = -1f, t = 0f;
            bool swimming = true;
            while (t < PathMax)
            {
                t += 1f / Fps;
                var dir = goal - p; float len = dir.magnitude;
                if (len < .4f) { row.end = "at the bank point still afloat"; row.endDepth = w._WaterDepthAt(p); row.endSeconds = t; return row; }
                p += dir / len * Mathf.Min(step, len);
                // WaterSwimHeight (the flag of the frame before), then WaterPoseRefresh (the flag for the next frame)
                float bed = w._WaterBedAt(p);
                if (bed > KH_RiverData.NoBed + 1f)
                {
                    float raw = w._WaterSwimRootY(p, carrying), floored = Mathf.Max(raw, w._WaterSwimFloorY(p));
                    float depth = w._WaterDepthAt(p);
                    row.afloatFrames++;
                    if (raw - bed < -r.floor) row.floorFrames++;
                    if (raw - bed < row.minRawLift) { row.minRawLift = raw - bed; row.minRawDepth = depth; }
                    row.minFlooredLift = Mathf.Min(row.minFlooredLift, floored - bed);
                }
                float dNow = w._WaterDepthAt(p);
                if (!w._WaterWetAt(p)) { swimming = false; row.end = "dry edge"; }   // WaterWetAt: the spot's grid, else the river-wide one
                else if (dNow < b.leave)
                {
                    if (shallowSince < 0f) shallowSince = t;
                    else if (t - shallowSince >= r.swimLeaveHold) { swimming = false; row.end = "below the leave depth for " + r.swimLeaveHold.ToString("F1") + " s"; }
                }
                else shallowSince = -1f;
                if (!swimming) { row.endDepth = dNow; row.endSeconds = t; return row; }
            }
            row.end = "still afloat after " + PathMax + " s"; row.endSeconds = t;
            return row;
        }

        static void Summarise(Report r)
        {
            var S = r.summary;
            S.Add("floor: root never below riverbed - " + r.floor.ToString("F2") + " m while afloat; spot deepest " + r.spotDeepest.ToString("F3") + " m at " + r.spotDeepAt.ToString("F2") + "; swim end hold " + r.swimLeaveHold.ToString("F2") + " s");
            foreach (var b in r.breeds)
            {
                if (!b.swims) { S.Add(b.name + ": no swim (M " + b.M.ToString("F3") + "): never afloat, the floor never applies"); continue; }
                string head = b.name + " (M " + b.M.ToString("F3") + ", Ms " + b.Ms.ToString("F3") + ", sink " + b.sink.ToString("F3") + "): draft " + b.draft.ToString("F3") + " (carrying " + b.carryDraft.ToString("F3")
                    + "), swim start " + b.enter.ToString("F3") + " / end " + b.leave.ToString("F3") + " m; unfloored root - bed at start " + b.liftAtEnter.ToString("F3") + ", at end " + b.liftAtLeave.ToString("F3")
                    + ", at 0.02 m " + b.liftAt002.ToString("F3") + "; afloat legs frames need >= " + b.legsFloatLow.ToString("F3") + " m";
                S.Add(head);
                foreach (var g in b.regions.Where(x => x.wetPoints > 0))
                    S.Add("  " + b.name + " " + g.region + (g.carrying ? " carrying" : "") + ": wet points " + g.wetPoints + "; unfloored min root - bed " + g.minRawLift.ToString("F3") + " (depth " + g.minRawDepth.ToString("F3")
                        + "), floored min " + g.minFlooredLift.ToString("F3") + "; below the end depth " + g.shallowPoints + " (floor acts " + g.shallowFloorActs + "); at/above it " + g.swimRangePoints
                        + " (floor lifts " + g.swimRangeFloorActs + ", max " + g.maxSwimRangeLift.ToString("F3") + " m at depth " + g.maxSwimRangeLiftDepth.ToString("F3") + "); at/above the start " + g.enterPoints
                        + " (floor lifts " + g.enterFloorActs + ", max " + g.maxEnterLift.ToString("F3") + ")");
                S.Add("  " + b.name + " paths: swum " + b.pathsSwum + ", sunk below bed - 0.03 before the fix " + b.pathsReproduced + "; worst unfloored root - bed with the flag up " + b.worstPathRaw.ToString("F3")
                    + (b.worstPath != null ? " [" + b.worstPath + "]" : "") + "; floored min " + b.worstPathFloored.ToString("F3"));
            }
            foreach (var c in r.checks) S.Add("check: " + c);
            foreach (var f in r.facts) S.Add("fact: " + f);
        }
    }
}
