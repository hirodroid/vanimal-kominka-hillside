using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using KominkaDogRun;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // DES-DOG-010 (method B, decided 2026-10-11) wave 1 / IMP-DOG-053: Edit-mode survey for the leashed river follow. Read only:
    // never writes the scene or an asset, no Play, idle Edit mode on the production scene (saved, not dirty). Decides how a leashed
    // dog gets into the river (straight from the NavMesh edge into the band, or through a nearby ok bank slice of Course/90).
    //   1 band edge vs NavMesh: every 2 m in z (0-720), both sides. The band edge point (centre +- (half width + BandMargin), on the
    //     ground), the nearest NavMesh point (SamplePosition 8 m; a hit on the other bank counts as none), and a perpendicular scan
    //     (lateral -3 .. +8 m from the band edge, 0.25 m) for where the NavMesh begins on that line. Per sample: bucket (inside the
    //     band / within BandMargin / farther / none / bridge zone), a complete path from the nearest route node, the ground slope
    //     from the NavMesh start to the water edge (centre +- half width), stones on that strip (layer 11, not Terrain, as 90), the
    //     nearest ok slice on that side. "direct" = NavMesh starts within BandMargin of the band edge on the line, connected,
    //     slope <= 25 deg (Course/76/90 exit rule), no stone, outside the bridge zones. Runs of "far / none" are listed per side.
    //   2 ok bank slices (KH_RiverData.sliceOk / sliceBank, baked by 90): gaps between consecutive ok slices per side and on
    //     either side, gaps > 8 m (split: spanning a bridge zone or not), the nearest ok slice on each side of each bridge, and
    //     the not-ok reasons (KH_RiverFetchEditor.ComputeSlices through reflection; read only, same code as 90 / 91).
    //   3 water-play spot (KH_WaterSpot): entry / exits / returns against the band, the NavMesh, the depth 1 m toward the centre,
    //     the nearest ok slice.
    //   4 depth: the river-wide grid cells inside the river-wide water (KH_RiverData._FetchWater: band, outside the bridge zones,
    //     riverbed data), depth = surface - bed. Per breed (the six KD_DogWanderController in the scene: M, Ms, swims; M cross-checked
    //     against KH_Jump_*.json mouthOffsetStand): walk limit = min(fetchWadeRatio x M, Ms - 0.03) for a non-swimmer, Ms - 0.03 for a
    //     swimmer, which floats from min(swimEnterRatio x M, Ms - wadeClear); share of wet cells walked / swum / out of reach, rows a
    //     non-swimmer can cross, how far in from each band edge it can walk.
    //   5 player floor at the centre line (the layers PlayerLocal collides with): whether a holder can walk into the river.
    // Result: Temp/kh-leash-river/survey.json; the summary lines are in the JSON (summary) and in the Console ([KH Leash River]).
    public static class KH_CourseLeashRiverSurveyEditor
    {
        const string OutDir = "Temp/kh-leash-river";
        const string DogsRoot = "KH_Dogs", RiverNode = "KH_RiverData", SpotNode = "KH_WaterSpot";
        const float Z0 = 0f, Z1 = 720f, EdgeStep = 2f;
        const float NavSearch = 8f, ScanFrom = -3f, ScanTo = 8f, ScanStep = .25f, ScanTol = .3f;
        const float ProfileStep = .25f, MaxSlopeDeg = 25f, WideGap = 8f;
        const float WaterWet = .02f, SinkGuard = .03f;   // = KD_DogWanderController WaterWet / SinkGuard (private consts there)
        const int GroundMask = (1 << 0) | (1 << 11);    // = Course/90
        static readonly string[] Breeds = { "Corgi", "Pome", "Lab", "Dachs", "Shiba", "Samoyed" };   // dogIndex order
        static readonly float[] DepthBins = { WaterWet, .1f, .2f, .3f, .35f, .4f, .45f, .5f };

        [Serializable] public class Stats { public int n; public float min, mean, median, p90, max; }
        [Serializable] public class EdgeSample
        {
            public float z; public int side, section; public bool bridgeZone;
            public Vector3 edge; public float edgeAboveWater, edgeNormalDeg;
            public string bucket;                       // inside / near / far / none / bridge
            public Vector3 nav; public float navDist = -1f, navLateral = 1000f;   // nearest NavMesh: horizontal distance, signed lateral vs the band edge (< 0 = inside)
            public float scanLateral = 1000f;           // perpendicular line: where the NavMesh begins (signed lateral vs the band edge; 1000 = none)
            public bool connected;                      // complete path from the nearest route node to the NavMesh start
            public float profileMaxSlopeDeg = -1f, dropToWater;
            public string stone;
            public bool directOk;
            public int okSlice = -1; public float okSliceDist = -1f, okSliceDz;
        }
        [Serializable] public class Run { public int side; public float z0, z1; public string bucket; public float worst; }
        [Serializable] public class SliceGap { public int side; public float z0, z1, gap, bankDist; public bool spansBridgeZone, lead, trail; }
        [Serializable] public class BridgeSide { public float bridgeZ; public int side; public float okBelowDz = -1f, okAboveDz = -1f; }
        [Serializable] public class SpotPoint
        {
            public string name; public Vector3 p; public float lateral; public bool onNav;
            public float depthInward; public bool fetchWaterInward; public int okSlice = -1; public float okSliceDist = -1f;
        }
        [Serializable] public class Breed
        {
            public string name; public int dogIndex; public float M, Ms, jsonM; public bool swims;
            public float walkLimit, swimEnter, spotRuleLimit;
            public int wetCells; public float walkFrac, swimFrac, outOfReachFrac, walkFracSpotRule;
            public float crossableRowFrac, reachCentreFrac;
            public Stats lateralReach;                  // m walked in from the band edge before the first cell out of reach (non-swimmers)
        }
        [Serializable] public class Report
        {
            public string status, utc, scene;
            public bool dirtyAfter;
            public float bandMargin, bankReach, bridgeZoneHalfZ, fetchWadeRatio, leashLength, catchUpDistance;
            public float wadeLimitRatio, swimEnterRatio, wadeClear;
            public float[] bridgeZ;
            // 1
            public int edgeSamples, edgeBridge, edgeInside, edgeNear, edgeFar, edgeNone, edgeUnconnected, edgeDirect, edgeSteep, edgeStone;
            public Stats navDist, scanLateral, toWaterEdge, okSliceDist;
            public List<Run> farRuns = new List<Run>();
            public List<EdgeSample> edges = new List<EdgeSample>();
            // 2
            public int sliceCount, okMinus, okPlus, okEither, sliceMismatches = -1;
            public string sliceReasons;
            public Stats gapMinus, gapPlus, gapEither, gapNonBridge;
            public List<SliceGap> wideGaps = new List<SliceGap>();
            public List<BridgeSide> bridges = new List<BridgeSide>();
            // 3
            public List<SpotPoint> spot = new List<SpotPoint>();
            public float spotZ0, spotZ1, spotDeepestLateral; public bool spotInBridgeZone;
            // 4
            public int waterCells, wetCells; public float maxDepth;
            public float[] depthBinEdges; public int[] depthBins;
            public List<Breed> breeds = new List<Breed>();
            // 5
            public int playerSamples, playerOnRiverbed, playerOther;
            public List<string> playerOtherAt = new List<string>();
            public List<string> summary = new List<string>(), findings = new List<string>(), notes = new List<string>();
            public string limitations = "Static scene, saved Terrain and baked NavMesh only; nothing runs. Lateral is measured along x like the band and the NavMesh riverbed volumes. A dog's real path (StepToward + ground snap, the push-apart, the holder's hand) is not simulated: the leashed river follow needs its own probe / harness after implementation (IMP-DOG-053).";
        }

        [MenuItem("Tools/KominkaHillside/Course/130 - Survey Leash River Entry")]
        public static void Survey()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = new Report { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, status = "DONE" };
            var node = GameObject.Find(DogsRoot + "/" + RiverNode);
            var data = node == null ? null : node.GetComponent<KH_RiverData>();
            if (data == null) throw new InvalidOperationException("KH_Dogs/KH_RiverData missing (Course/73)");
            if (!data._FetchReady()) throw new InvalidOperationException("River fetch data not built (Course/90)");
            var spotGo = GameObject.Find(DogsRoot + "/" + SpotNode);
            var spot = spotGo == null ? null : spotGo.GetComponent<KH_WaterSpot>();
            var pouches = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true);
            if (pouches.Length != 1 || pouches[0].manager == null || pouches[0].manager.route == null) throw new InvalidOperationException("Exactly one wired menu (with the leash manager and route) required");
            var leash = pouches[0].manager;
            var route = leash.route;
            if (route.nodePositions == null || route.nodePositions.Length < 10) throw new InvalidOperationException("Route nodes missing");

            r.bandMargin = KH_RiverData.BandMargin; r.bankReach = data._BankReach(); r.bridgeZoneHalfZ = data._BridgeZoneHalfZ();
            r.fetchWadeRatio = data._FetchWadeRatio(); r.leashLength = leash.leashLength; r.catchUpDistance = leash.catchUpDistance;
            r.wadeLimitRatio = spot != null ? spot.wadeLimitRatio : KH_WaterSpot.DefaultWadeLimitRatio;
            r.swimEnterRatio = spot != null ? spot.swimEnterRatio : KH_WaterSpot.DefaultSwimEnterRatio;
            r.wadeClear = spot != null ? spot.wadeClear : KH_WaterSpot.DefaultWadeClear;
            r.bridgeZ = data.bridgeZ;
            if (spot == null) r.findings.Add("KH_Dogs/KH_WaterSpot missing: spot section skipped, spot tuning from the constants");

            EdgeSurvey(r, data, route);
            SliceSurvey(r, data, route);
            if (spot != null && spot._Ready()) SpotSurvey(r, data, spot);
            DepthSurvey(r, data);
            PlayerSurvey(r, data);
            Summarise(r);

            r.dirtyAfter = SceneManager.GetActiveScene().isDirty;
            if (r.dirtyAfter) r.findings.Add("Scene became dirty during the survey (it writes nothing: investigate before saving)");
            if (r.findings.Count > 0) r.status = "DONE_WITH_FINDINGS";
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/survey.json", JsonUtility.ToJson(r, true));
            foreach (var line in r.summary) Debug.Log("[KH Leash River] " + line);
            Debug.Log("[KH Leash River] " + r.status + " findings=" + r.findings.Count + " -> " + OutDir + "/survey.json");
        }

        // ================================================================ 1 band edge vs NavMesh

        static void EdgeSurvey(Report r, KH_RiverData data, KH_WalkRoute route)
        {
            var path = new NavMeshPath();
            for (float z = Z0; z <= Z1 + 1e-3f; z += EdgeStep)
            {
                if (!data._Covers(z)) continue;
                float cx = data._CenterX(z), hw = data._HalfWidth(z), surface = data._SurfaceY(z);
                bool bridge = data._InBridgeZone(z, 0f);
                foreach (int side in new[] { -1, 1 })
                {
                    var s = new EdgeSample { z = z, side = side, section = data._Section(z), bridgeZone = bridge };
                    float ex = cx + side * (hw + KH_RiverData.BandMargin);
                    Vector3 n;
                    float gy = GroundY(ex, z, surface, out n);
                    s.edge = new Vector3(ex, gy, z); s.edgeAboveWater = gy - surface; s.edgeNormalDeg = Vector3.Angle(n, Vector3.up);

                    // nearest NavMesh point (any direction), the other bank does not count
                    NavMeshHit h;
                    if (NavMesh.SamplePosition(s.edge, out h, NavSearch, NavMesh.AllAreas) && Mathf.Sign(h.position.x - data._CenterX(h.position.z)) == side)
                    {
                        s.nav = h.position; s.navDist = Flat(h.position - s.edge); s.navLateral = data._Lateral(h.position, KH_RiverData.BandMargin);
                    }
                    // perpendicular scan: where the NavMesh begins on the line through the edge point
                    for (float o = ScanFrom; o <= ScanTo + 1e-3f; o += ScanStep)
                    {
                        float x = cx + side * (hw + KH_RiverData.BandMargin + o);
                        Vector3 dummy; float y = GroundY(x, z, surface, out dummy);
                        if (y <= KH_RiverData.NoBed + 1f) continue;
                        var q = new Vector3(x, y, z);
                        if (NavMesh.SamplePosition(q, out h, ScanTol, NavMesh.AllAreas) && Flat(h.position - q) <= ScanTol) { s.scanLateral = o; break; }
                    }
                    Vector3 start = s.scanLateral < 999f ? new Vector3(cx + side * (hw + KH_RiverData.BandMargin + s.scanLateral), 0f, z) : s.navDist >= 0f ? s.nav : Vector3.zero;
                    bool haveStart = s.scanLateral < 999f || s.navDist >= 0f;
                    if (haveStart)
                    {
                        Vector3 dummy; start.y = GroundY(start.x, start.z, surface, out dummy);
                        NavMeshHit from, to; Vector3 rn = NearestNode(route, start);
                        s.connected = NavMesh.SamplePosition(rn, out from, 1.5f, NavMesh.AllAreas) && NavMesh.SamplePosition(start, out to, .5f, NavMesh.AllAreas)
                            && NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                        // ground profile from the NavMesh start to the water edge (centre +- half width), along x
                        float wx = cx + side * hw, prevY = start.y, maxDeg = 0f;
                        float dir = Mathf.Sign(wx - start.x), len = Mathf.Abs(wx - start.x);
                        for (float t = ProfileStep; t <= len + 1e-3f; t += ProfileStep)
                        {
                            float y = GroundY(start.x + dir * t, z, surface, out dummy);
                            if (y <= KH_RiverData.NoBed + 1f) continue;
                            maxDeg = Mathf.Max(maxDeg, Mathf.Atan2(Mathf.Abs(y - prevY), ProfileStep) * Mathf.Rad2Deg);
                            prevY = y;
                        }
                        s.profileMaxSlopeDeg = maxDeg; s.dropToWater = start.y - surface;
                        float mid = (start.x + wx) * .5f, half = Mathf.Max(.25f, len * .5f);
                        var box = Physics.OverlapBox(new Vector3(mid, surface, z), new Vector3(half, 1.5f, .5f), Quaternion.identity, 1 << 11, QueryTriggerInteraction.Ignore);
                        var stone = box.FirstOrDefault(c => !(c is TerrainCollider));
                        if (stone != null) s.stone = stone.transform.root.name + "/" + stone.name;
                    }
                    int code = data._NearestSlice(new Vector3(ex, surface, z), side, data._BankReach(), -1);
                    if (code >= 0)
                    {
                        Vector3 b = data._SliceBank(code);
                        s.okSlice = code; s.okSliceDist = Flat(b - (haveStart ? start : s.edge)); s.okSliceDz = b.z - z;
                    }
                    if (bridge) s.bucket = "bridge";
                    else if (s.navDist < 0f) s.bucket = "none";
                    else if (s.navLateral < 0f) s.bucket = "inside";
                    else if (s.navDist <= KH_RiverData.BandMargin) s.bucket = "near";
                    else s.bucket = "far";
                    s.directOk = !bridge && s.scanLateral <= KH_RiverData.BandMargin && s.connected && s.profileMaxSlopeDeg >= 0f
                        && s.profileMaxSlopeDeg <= MaxSlopeDeg && s.stone == null;
                    r.edges.Add(s);
                }
            }
            var live = r.edges.Where(e => !e.bridgeZone).ToList();
            r.edgeSamples = r.edges.Count; r.edgeBridge = r.edges.Count - live.Count;
            r.edgeInside = live.Count(e => e.bucket == "inside"); r.edgeNear = live.Count(e => e.bucket == "near");
            r.edgeFar = live.Count(e => e.bucket == "far"); r.edgeNone = live.Count(e => e.bucket == "none");
            r.edgeUnconnected = live.Count(e => e.bucket != "none" && !e.connected);
            r.edgeDirect = live.Count(e => e.directOk);
            r.edgeSteep = live.Count(e => e.profileMaxSlopeDeg > MaxSlopeDeg);
            r.edgeStone = live.Count(e => e.stone != null);
            r.navDist = Of(live.Where(e => e.navDist >= 0f).Select(e => e.navDist));
            r.scanLateral = Of(live.Where(e => e.scanLateral < 999f).Select(e => e.scanLateral));
            r.toWaterEdge = Of(live.Where(e => e.scanLateral < 999f).Select(e => e.scanLateral + KH_RiverData.BandMargin));
            r.okSliceDist = Of(live.Where(e => e.okSliceDist >= 0f).Select(e => e.okSliceDist));
            foreach (int side in new[] { -1, 1 })
            {
                Run open = null;
                foreach (var e in r.edges.Where(e => e.side == side))
                {
                    bool bad = e.bucket == "far" || e.bucket == "none";
                    if (bad && open != null && open.bucket == e.bucket) { open.z1 = e.z; open.worst = Mathf.Max(open.worst, e.navDist); continue; }
                    open = null;
                    if (bad) { open = new Run { side = side, z0 = e.z, z1 = e.z, bucket = e.bucket, worst = e.navDist }; r.farRuns.Add(open); }
                }
            }
        }

        // ================================================================ 2 ok bank slices

        static void SliceSurvey(Report r, KH_RiverData data, KH_WalkRoute route)
        {
            int count = data.sliceOk.Length / 2;
            r.sliceCount = count;
            var gapsBySide = new Dictionary<int, List<SliceGap>>();
            foreach (int side in new[] { -1, 1, 0 })
            {
                var oks = new List<int>();
                for (int k = 0; k < count; k++)
                {
                    bool ok = side < 0 ? data.sliceOk[2 * k] : side > 0 ? data.sliceOk[2 * k + 1] : (data.sliceOk[2 * k] || data.sliceOk[2 * k + 1]);
                    if (ok) oks.Add(k);
                }
                if (side < 0) r.okMinus = oks.Count; else if (side > 0) r.okPlus = oks.Count; else r.okEither = oks.Count;
                var gaps = new List<SliceGap>();
                float zEnd = data.sliceZ0 + (count - 1) * data.sliceStep;
                if (oks.Count == 0) { gaps.Add(new SliceGap { side = side, z0 = data.sliceZ0, z1 = zEnd, gap = zEnd - data.sliceZ0, lead = true, trail = true }); }
                else
                {
                    float zf = data.sliceZ0 + oks[0] * data.sliceStep, zl = data.sliceZ0 + oks[oks.Count - 1] * data.sliceStep;
                    if (zf - data.sliceZ0 > WideGap) gaps.Add(new SliceGap { side = side, z0 = data.sliceZ0, z1 = zf, gap = zf - data.sliceZ0, lead = true, spansBridgeZone = SpansBridge(data, data.sliceZ0, zf) });
                    for (int i = 1; i < oks.Count; i++)
                    {
                        float za = data.sliceZ0 + oks[i - 1] * data.sliceStep, zb = data.sliceZ0 + oks[i] * data.sliceStep;
                        var g = new SliceGap { side = side, z0 = za, z1 = zb, gap = zb - za, spansBridgeZone = SpansBridge(data, za, zb) };
                        if (side != 0) g.bankDist = Flat(data._SliceBank(2 * oks[i] + (side > 0 ? 1 : 0)) - data._SliceBank(2 * oks[i - 1] + (side > 0 ? 1 : 0)));
                        gaps.Add(g);
                    }
                    if (zEnd - zl > WideGap) gaps.Add(new SliceGap { side = side, z0 = zl, z1 = zEnd, gap = zEnd - zl, trail = true, spansBridgeZone = SpansBridge(data, zl, zEnd) });
                }
                gapsBySide[side] = gaps;
                foreach (var g in gaps) if (g.gap > WideGap) r.wideGaps.Add(g);
            }
            Func<List<SliceGap>, Stats> inner = l => Of(l.Where(g => !g.lead && !g.trail).Select(g => g.gap));
            r.gapMinus = inner(gapsBySide[-1]); r.gapPlus = inner(gapsBySide[1]); r.gapEither = inner(gapsBySide[0]);
            r.gapNonBridge = Of(gapsBySide[-1].Concat(gapsBySide[1]).Where(g => !g.lead && !g.trail && !g.spansBridgeZone).Select(g => g.gap));

            if (data.bridgeZ != null)
                foreach (float b in data.bridgeZ)
                    foreach (int side in new[] { -1, 1 })
                    {
                        var bs = new BridgeSide { bridgeZ = b, side = side };
                        for (int k = 0; k < count; k++)
                        {
                            if (!data.sliceOk[2 * k + (side > 0 ? 1 : 0)]) continue;
                            float z = data.sliceZ0 + k * data.sliceStep;
                            if (z < b && (bs.okBelowDz < 0f || b - z < bs.okBelowDz)) bs.okBelowDz = b - z;
                            if (z > b && (bs.okAboveDz < 0f || z - b < bs.okAboveDz)) bs.okAboveDz = z - b;
                        }
                        r.bridges.Add(bs);
                    }

            // the not-ok reasons, recomputed by the same code as 90 / 91 (private there: read through reflection, read only)
            try
            {
                var m = typeof(KH_RiverFetchEditor).GetMethod("ComputeSlices", BindingFlags.NonPublic | BindingFlags.Static);
                if (m == null) throw new MissingMethodException("KH_RiverFetchEditor.ComputeSlices");
                var list = (List<KH_RiverFetchEditor.Slice>)m.Invoke(null, new object[] { data, route });
                int mism = 0, bridge = 0, exit = 0, ret = 0, pathNo = 0, stones = 0;
                for (int k = 0; k < list.Count; k++)
                {
                    var s = list[k];
                    if (k < data.sliceOk.Length && s.ok != data.sliceOk[k]) mism++;
                    if (s.ok) continue;
                    if (!s.bridgeOk) bridge++;
                    else if (!s.exitOk) exit++;
                    else if (!s.returnOk) ret++;
                    else if (!s.pathOk) pathNo++;
                    else if (!s.stonesOk) stones++;
                }
                if (list.Count != data.sliceOk.Length) r.findings.Add("Slice count stored " + data.sliceOk.Length + " vs recomputed " + list.Count + " (rebuild with Course/90)");
                r.sliceMismatches = mism;
                if (mism > 0) r.findings.Add(mism + " stored ok flags differ from the recomputed slices (NavMesh / Terrain / stones changed: Course/90 + 91)");
                r.sliceReasons = "not ok (first failing check): bridge zone " + bridge + ", exit (no bank NavMesh 1-4 m out or slope > 25 deg) " + exit
                    + ", return point " + ret + ", path from the route " + pathNo + ", stones " + stones;
            }
            catch (Exception e)
            {
                Exception inner2 = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                r.sliceReasons = "unavailable: " + inner2.GetType().Name + ": " + inner2.Message;
                r.notes.Add("Not-ok slice reasons not computed (" + inner2.Message + "); run Course/91 for the per-slice checks");
            }
        }

        static bool SpansBridge(KH_RiverData data, float za, float zb)
        {
            if (data.bridgeZ == null) return false;
            float h = data._BridgeZoneHalfZ();
            foreach (float b in data.bridgeZ) if (b + h > za && b - h < zb) return true;
            return false;
        }

        // ================================================================ 3 water-play spot

        static void SpotSurvey(Report r, KH_RiverData data, KH_WaterSpot spot)
        {
            Action<string, Vector3> add = (name, p) =>
            {
                var sp = new SpotPoint { name = name, p = p, lateral = data._Lateral(p, KH_RiverData.BandMargin) };
                NavMeshHit h; sp.onNav = NavMesh.SamplePosition(p, out h, .3f, NavMesh.AllAreas);
                float side = Mathf.Sign(p.x - data._CenterX(p.z));
                var inward = new Vector3(p.x - side * 1f, p.y, p.z);
                inward.y = data._BedAt(inward) > KH_RiverData.NoBed + 1f ? data._BedAt(inward) : p.y;
                sp.fetchWaterInward = data._FetchWater(inward);
                sp.depthInward = spot._Contains(inward) && spot._Bed(inward) > KH_WaterSpot.NoBed + 1f ? spot._Depth(inward) : data._DepthAt(inward);
                int code = data._NearestSlice(p, (int)side, data._BankReach(), -1);
                if (code >= 0) { sp.okSlice = code; sp.okSliceDist = Flat(data._SliceBank(code) - p); }
                r.spot.Add(sp);
            };
            add("entry", spot.entry);
            for (int k = 0; k < spot.exits.Length; k++) add("exit" + k, spot.exits[k]);
            for (int k = 0; k < spot.returns.Length; k++) add("return" + k, spot.returns[k]);
            r.spotZ0 = spot.polyZ.Min(); r.spotZ1 = spot.polyZ.Max();
            r.spotInBridgeZone = data._InBridgeZone(r.spotZ0, 0f) || data._InBridgeZone(r.spotZ1, 0f);
            float deepest = 1000f;
            for (int i = 0; i < spot.polyX.Length; i++) deepest = Mathf.Min(deepest, data._Lateral(new Vector3(spot.polyX[i], 0f, spot.polyZ[i]), KH_RiverData.BandMargin));
            r.spotDeepestLateral = deepest;
            foreach (var sp in r.spot)
            {
                if (sp.lateral < 0f) r.findings.Add("Spot " + sp.name + " is inside the band (lateral " + sp.lateral.ToString("F2") + ")");
                if (!sp.onNav) r.notes.Add("Spot " + sp.name + " is not on the NavMesh within 0.3 m");
            }
        }

        // ================================================================ 4 depth and breeds

        static void DepthSurvey(Report r, KH_RiverData data)
        {
            // cells of the river-wide water, row by row (rows sorted by x)
            var rows = new List<List<Vector2>>();   // (x, depth) per row
            var rowCx = new List<float>();
            r.depthBinEdges = DepthBins; r.depthBins = new int[DepthBins.Length + 1];
            for (int row = 0; row < data.fetchRows; row++)
            {
                float z = data.fetchZ0 + row * data.fetchStep;
                if (!data._Covers(z) || data._InBridgeZone(z, 0f)) continue;
                float surface = data._SurfaceY(z);
                var cells = new List<Vector2>();
                for (int i = data.fetchRowStart[row]; i < data.fetchRowStart[row + 1]; i++)
                {
                    float bed = data.fetchBed[i];
                    if (bed <= KH_RiverData.NoBed + 1f) continue;
                    float x = data.fetchRowX0[row] + (i - data.fetchRowStart[row]) * data.fetchStep;
                    if (!data._FetchWater(new Vector3(x, bed, z))) continue;
                    float d = Mathf.Max(0f, surface - bed);
                    cells.Add(new Vector2(x, d));
                    r.waterCells++;
                    if (d > WaterWet) r.wetCells++;
                    r.maxDepth = Mathf.Max(r.maxDepth, d);
                    int b = 0; while (b < DepthBins.Length && d > DepthBins[b]) b++;
                    r.depthBins[b]++;
                }
                if (cells.Count > 0) { rows.Add(cells); rowCx.Add(data._CenterX(z)); }
            }

            var wanders = UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true).Where(w => w.dogIndex >= 0 && w.dogIndex < Breeds.Length).OrderBy(w => w.dogIndex).ToArray();
            if (wanders.Length != 6 || wanders.Select(w => w.dogIndex).Distinct().Count() != 6) r.findings.Add("Expected six KD_DogWanderController with dogIndex 0-5, found " + wanders.Length);
            foreach (var w in wanders)
            {
                var b = new Breed { name = Breeds[w.dogIndex], dogIndex = w.dogIndex, M = w.waterMouthHeight, Ms = w.waterSwimMouth, swims = w.waterSwimAllowed };
                b.jsonM = JsonMouth(Breeds[w.dogIndex]);
                if (b.jsonM > 0f && Mathf.Abs(b.jsonM - b.M) > .001f) r.findings.Add(b.name + ": waterMouthHeight " + b.M.ToString("F4") + " differs from KH_Jump json " + b.jsonM.ToString("F4") + " (Course/76)");
                float sink = b.Ms - SinkGuard;
                b.walkLimit = b.swims ? sink : Mathf.Min(r.fetchWadeRatio * b.M, sink);
                b.swimEnter = b.swims ? Mathf.Min(r.swimEnterRatio * b.M, b.Ms - r.wadeClear) : -1f;
                b.spotRuleLimit = Mathf.Min(r.wadeLimitRatio * b.M, sink);
                int wet = 0, walk = 0, swim = 0, outR = 0, walkSpot = 0, crossable = 0, reachCentre = 0, sideRows = 0;
                var reach = new List<float>();
                for (int i = 0; i < rows.Count; i++)
                {
                    var cells = rows[i]; bool cross = true;
                    foreach (var c in cells)
                    {
                        bool ok = b.swims || c.y <= b.walkLimit;
                        if (!ok) cross = false;
                        if (c.y <= WaterWet) continue;
                        wet++;
                        if (c.y <= b.spotRuleLimit) walkSpot++;
                        if (b.swims) { if (c.y >= b.swimEnter) swim++; else walk++; }
                        else if (c.y <= b.walkLimit) walk++; else outR++;
                    }
                    if (cross) crossable++;
                    if (b.swims) continue;
                    // from each band edge inward: how far before the first cell out of reach
                    for (int dir = 0; dir < 2; dir++)
                    {
                        sideRows++;
                        int n = cells.Count, start = dir == 0 ? 0 : n - 1, step = dir == 0 ? 1 : -1;
                        float edgeX = cells[start].x, lastX = edgeX; bool centre = false, stopped = false;
                        for (int k = start; k >= 0 && k < n; k += step)
                        {
                            if (cells[k].y > b.walkLimit) { stopped = true; break; }
                            lastX = cells[k].x;
                            if ((dir == 0 && lastX >= rowCx[i]) || (dir == 1 && lastX <= rowCx[i])) centre = true;
                        }
                        if (centre) reachCentre++;
                        reach.Add(stopped ? Mathf.Abs(lastX - edgeX) : Mathf.Abs(cells[dir == 0 ? n - 1 : 0].x - edgeX));
                    }
                }
                b.wetCells = wet;
                b.walkFrac = wet > 0 ? (float)walk / wet : 0f; b.swimFrac = wet > 0 ? (float)swim / wet : 0f;
                b.outOfReachFrac = wet > 0 ? (float)outR / wet : 0f; b.walkFracSpotRule = wet > 0 ? (float)walkSpot / wet : 0f;
                b.crossableRowFrac = rows.Count > 0 ? (float)crossable / rows.Count : 0f;
                b.reachCentreFrac = b.swims ? 1f : sideRows > 0 ? (float)reachCentre / sideRows : 0f;
                b.lateralReach = Of(reach);
                r.breeds.Add(b);
            }
        }

        static float JsonMouth(string breed)
        {
            string path = "Assets/KominkaHillside/Data/Frisbee/KH_Jump_" + breed + ".json";
            if (!File.Exists(path)) return -1f;
            var m = Regex.Match(File.ReadAllText(path), "\"mouthOffsetStand\"\\s*:\\s*\\[\\s*[-0-9.eE]+\\s*,\\s*([-0-9.eE]+)");
            return m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1f;
        }

        // ================================================================ 5 player floor

        static void PlayerSurvey(Report r, KH_RiverData data)
        {
            int layer = LayerMask.NameToLayer("PlayerLocal");
            int mask = ~0;
            if (layer >= 0) { mask = 0; for (int l = 0; l < 32; l++) if (!Physics.GetIgnoreLayerCollision(layer, l)) mask |= 1 << l; }
            if (layer < 0) r.notes.Add("Layer PlayerLocal missing: the player floor uses every layer");
            for (float z = Z0; z <= Z1 + 1e-3f; z += EdgeStep)
            {
                if (!data._Covers(z) || data._InBridgeZone(z, 0f)) continue;
                float cx = data._CenterX(z), surface = data._SurfaceY(z);
                r.playerSamples++;
                RaycastHit g;
                if (!Physics.Raycast(new Vector3(cx, surface + 10f, z), Vector3.down, out g, 30f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (g.collider is TerrainCollider && g.point.y < surface - .05f) r.playerOnRiverbed++;
                else
                {
                    r.playerOther++;
                    if (r.playerOtherAt.Count < 40) r.playerOtherAt.Add("z " + z.ToString("F0") + ": " + g.collider.name + " at water " + (g.point.y - surface).ToString("+0.00;-0.00") + " m");
                }
            }
        }

        // ================================================================ summary

        static void Summarise(Report r)
        {
            var S = r.summary;
            int live = r.edgeSamples - r.edgeBridge;
            S.Add("band edge vs NavMesh (" + live + " samples every " + EdgeStep + " m, both sides, outside bridge zones; " + r.edgeBridge + " in bridge zones): inside the band "
                + Pct(r.edgeInside, live) + ", within BandMargin " + Pct(r.edgeNear, live) + ", farther " + Pct(r.edgeFar, live) + ", none within " + NavSearch + " m " + Pct(r.edgeNone, live)
                + "; nearest NavMesh distance " + St(r.navDist) + "; unconnected to the route " + r.edgeUnconnected);
            S.Add("perpendicular NavMesh start vs band edge (signed, < 0 = inside the band): " + St(r.scanLateral) + "; NavMesh start to the water edge " + St(r.toWaterEdge)
                + " (leash " + r.leashLength.ToString("F1") + " m)");
            var longest = r.farRuns.OrderByDescending(f => f.z1 - f.z0).FirstOrDefault();
            S.Add("NavMesh ends short of the band (far / none runs): " + r.farRuns.Count + " runs" + (longest != null ? ", longest side " + longest.side + " z " + longest.z0 + "-" + longest.z1 + " (" + longest.bucket + ")" : ""));
            S.Add("direct entry (NavMesh starts <= " + KH_RiverData.BandMargin + " m from the band edge, connected, slope <= " + MaxSlopeDeg + " deg to the water, no stone): " + Pct(r.edgeDirect, live)
                + "; too steep " + r.edgeSteep + ", stones " + r.edgeStone + "; nearest ok slice from the NavMesh start " + St(r.okSliceDist));
            S.Add("ok bank slices (every " + "1.5 m): -x " + r.okMinus + " / +x " + r.okPlus + " / either " + r.okEither + " of " + r.sliceCount + "; gap -x " + St(r.gapMinus) + "; +x " + St(r.gapPlus)
                + "; either " + St(r.gapEither) + "; gaps > " + WideGap + " m: " + r.wideGaps.Count + " (" + r.wideGaps.Count(g => g.spansBridgeZone) + " span a bridge zone)");
            if (!string.IsNullOrEmpty(r.sliceReasons)) S.Add("slices " + r.sliceReasons + (r.sliceMismatches >= 0 ? "; stored vs recomputed mismatches " + r.sliceMismatches : ""));
            foreach (var b in r.bridges) S.Add("bridge z " + b.bridgeZ + " side " + b.side + ": nearest ok slice " + (b.okBelowDz >= 0f ? b.okBelowDz.ToString("F1") : "-") + " m below / " + (b.okAboveDz >= 0f ? b.okAboveDz.ToString("F1") : "-") + " m above");
            if (r.spot.Count > 0)
                S.Add("water spot z " + r.spotZ0.ToString("F1") + "-" + r.spotZ1.ToString("F1") + ": " + string.Join(", ", r.spot.Where(p => !p.name.StartsWith("return")).Select(p => p.name + " lat " + p.lateral.ToString("+0.00;-0.00")
                    + (p.onNav ? " nav" : " off-nav") + " depth1m " + p.depthInward.ToString("F2") + " okSlice " + (p.okSliceDist >= 0f ? p.okSliceDist.ToString("F1") + " m" : "none"))));
            S.Add("depth in the river-wide water (" + r.waterCells + " cells, " + r.wetCells + " wet): max " + r.maxDepth.ToString("F3") + " m; bins (<=" + string.Join(", <=", r.depthBinEdges.Select(v => v.ToString("F2"))) + ", more): " + string.Join(" / ", r.depthBins));
            foreach (var b in r.breeds)
                S.Add(b.name + " (M " + b.M.ToString("F3") + ", Ms " + b.Ms.ToString("F3") + (b.swims ? ", swims from " + b.swimEnter.ToString("F3") : ", no swim") + ", walk <= " + b.walkLimit.ToString("F3") + " m): wet cells walked "
                    + Pct01(b.walkFrac) + ", swum " + Pct01(b.swimFrac) + ", out of reach " + Pct01(b.outOfReachFrac) + " (free-play 0.45 M rule: walked " + Pct01(b.walkFracSpotRule) + "); rows crossable "
                    + Pct01(b.crossableRowFrac) + (b.swims ? "" : ", reaches the centre from an edge " + Pct01(b.reachCentreFrac) + ", walk-in " + St(b.lateralReach)));
            S.Add("holder: the centre line is riverbed a player can stand on at " + Pct(r.playerOnRiverbed, r.playerSamples) + " (" + r.playerOther + " other floors)");
            // the hint for the entry decision (the decision itself is the implementer's, IMP-DOG-053)
            float direct = live > 0 ? (float)r.edgeDirect / live : 0f;
            string hint = direct >= .9f ? "direct entry from the NavMesh edge works almost everywhere; route the exceptions (far / steep / stone runs) through the nearest ok slice"
                : direct >= .5f ? "mixed: direct entry where the sample is direct, else the nearest ok slice (median " + r.okSliceDist.median.ToString("F1") + " m away)"
                : "use the ok bank slices as the way in (direct entry fails at most samples)";
            S.Add("entry hint: " + hint);
        }

        // ================================================================ helpers

        static float GroundY(float x, float z, float surface, out Vector3 normal)
        {
            normal = Vector3.up; RaycastHit g;
            if (!Physics.Raycast(new Vector3(x, surface + 10f, z), Vector3.down, out g, 30f, GroundMask, QueryTriggerInteraction.Ignore)) return KH_RiverData.NoBed;
            normal = g.normal; return g.point.y;
        }

        static Vector3 NearestNode(KH_WalkRoute route, Vector3 p)
        {
            Vector3 best = route.nodePositions[0]; float bestD = float.MaxValue;
            foreach (var n in route.nodePositions) { float d = Flat(n - p); if (d < bestD) { bestD = d; best = n; } }
            return best;
        }

        static Stats Of(IEnumerable<float> values)
        {
            var a = values.OrderBy(v => v).ToArray();
            var s = new Stats { n = a.Length };
            if (a.Length == 0) return s;
            s.min = a[0]; s.max = a[a.Length - 1]; s.mean = a.Average(); s.median = a[a.Length / 2]; s.p90 = a[Mathf.Min(a.Length - 1, (int)(a.Length * .9f))];
            return s;
        }

        static string St(Stats s) { return s == null || s.n == 0 ? "n 0" : "min " + s.min.ToString("F2") + " / median " + s.median.ToString("F2") + " / mean " + s.mean.ToString("F2") + " / p90 " + s.p90.ToString("F2") + " / max " + s.max.ToString("F2") + " m (n " + s.n + ")"; }
        static string Pct(int a, int n) { return n > 0 ? a + " (" + (100f * a / n).ToString("F1") + "%)" : a.ToString(); }
        static string Pct01(float f) { return (100f * f).ToString("F1") + "%"; }
        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
    }
}
