using System;
using System.Collections.Generic;
using System.Linq;
using KominkaDogRun;
using KominkaDogRun.Editor;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-WLD-008 W2 / W3: the scene references of the play / wash modes and the data positions of the spot cameras and the pass-by points.
    /// Video/1 resolves them (nothing is placed by hand yet; the user moves them later if a framing is poor), Video/2 checks them.
    /// Every candidate is tested in Edit mode against the scene colliders (blockMask = Default + Environment): not inside geometry, a clear line
    /// to the look point, ground below within SpotGroundMax.
    /// </summary>
    public static partial class KH_VideoCameraEditor
    {
        public const string SpotWordWash = "洗い場", SpotWordRiver = "川", SpotWordRiverLow = "川 (低い)";

        /// <summary>Per dog (dogIndex order): the frisbee catcher and its disc, the fetch (petting state), the wander (treat state); all wash station
        /// calls (hierarchy order, for the 定点 lean); the water play area. §16: the per-dog KH_DogWash array went with 手元.</summary>
        static void WirePlay(KH_VideoCamera v, Ctx c, List<string> log)
        {
            var keys = KH_PhotoGalleryEditor.Keys;
            int n = keys.Length;
            v.catchers = new KH_FrisbeeCatch[n]; v.discs = new KH_Frisbee[n]; v.fetches = new KD_DogFetchController[n]; v.wanders = new KD_DogWanderController[n];
            for (int i = 0; i < n; i++)
            {
                v.catchers[i] = Comp<KH_FrisbeeCatch>("KH_FrisbeeCatch_" + keys[i], log);
                v.discs[i] = v.catchers[i] != null ? v.catchers[i].frisbee : null;
                v.fetches[i] = Comp<KD_DogFetchController>("KD_FetchSystem_" + keys[i], log);
                v.wanders[i] = Comp<KD_DogWanderController>("KD_WanderSystem_" + keys[i], log);
                if (v.discs[i] == null) log.Add("WARN no disc for dog " + i);
            }
            v.washCalls = WashCalls();
            var ws = UnityEngine.Object.FindObjectsOfType<KH_WaterSpot>(true);
            v.waterSpot = ws.Length == 1 ? ws[0] : null;
            if (ws.Length != 1) log.Add("WARN KH_WaterSpot x" + ws.Length + ": no river spot / water dog lean");
            log.Add("play wiring: catchers / discs / fetches / wanders for " + n + " dogs, " + v.washCalls.Length + " wash stations, water spot " + (v.waterSpot != null));
        }

        /// <summary>§16 みち: the walking route = the leash manager's (the trail the dogs walk). Without it, the only KH_WalkRoute with a closed loop
        /// (the course), else null (みち then uses my heading only; Video/2 warns).</summary>
        static KH_WalkRoute RouteFor(Ctx c, List<string> log)
        {
            KH_WalkRoute route = c.manager != null ? c.manager.route : null;
            string from = "KH_LeashManager.route";
            if (route == null)
            {
                var all = UnityEngine.Object.FindObjectsOfType<KH_WalkRoute>(true).Where(w => w.closedLoop).ToArray();
                route = all.Length == 1 ? all[0] : null;
                from = "the only closed-loop KH_WalkRoute (x" + all.Length + ")";
            }
            log.Add((route != null ? "みち route: " + route.name + " (" + from + ")" : "WARN みち route: none (" + from + "): みち uses the heading only"));
            return route;
        }

        static T Comp<T>(string name, List<string> log) where T : Component
        {
            var go = KD_SceneObjects.Find(name, out int count);
            if (go == null || count != 1) { log.Add("WARN " + name + " x" + count); return null; }
            return go.GetComponent<T>();
        }

        /// <summary>Every wash station call, in hierarchy-path order (stable between runs).</summary>
        public static KH_WashStationCall[] WashCalls()
        {
            return UnityEngine.Object.FindObjectsOfType<KH_WashStationCall>(true).OrderBy(x => PathOf(x.transform), StringComparer.Ordinal).ToArray();
        }

        static string PathOf(Transform t) { string p = t.name; while (t.parent != null) { t = t.parent; p = t.name + "/" + p; } return p; }

        public class Data
        {
            public string[] spotNames = new string[0]; public Vector3[] spotPositions = new Vector3[0], spotLooks = new Vector3[0]; public float[] spotFovs = new float[0]; public int[] spotStations = new int[0];
            public string[] spotKeys = new string[0];   // W5: the translation key of each spot name (wash: {0} = station + 1)
            public Vector3[] passPositions = new Vector3[0];
        }

        static int BlockMask { get { return KH_PhotoGalleryEditor.CameraBlockMask; } }

        /// <summary>A camera point is usable: not inside geometry (0.25 m sphere), a clear line to the look point, ground below within SpotGroundMax.</summary>
        public static bool PointUsable(Vector3 pos, Vector3 look, out string why)
        {
            why = "";
            if (Physics.CheckSphere(pos, 0.25f, BlockMask, QueryTriggerInteraction.Ignore)) { why = "inside geometry"; return false; }
            if (Physics.Linecast(pos, look, out RaycastHit hit, BlockMask, QueryTriggerInteraction.Ignore) && (hit.point - look).magnitude > 0.35f) { why = "line of sight blocked at " + hit.point.ToString("F1"); return false; }
            if (!Physics.Raycast(pos, Vector3.down, SpotGroundMax, BlockMask, QueryTriggerInteraction.Ignore)) { why = "no ground within " + SpotGroundMax + " m"; return false; }
            return true;
        }

        static bool Ground(Vector3 p, out float y)
        {
            y = p.y;
            if (Physics.Raycast(p + Vector3.up * 8f, Vector3.down, out RaycastHit hit, 30f, BlockMask, QueryTriggerInteraction.Ignore)) { y = hit.point.y; return true; }
            return false;
        }

        /// <summary>Tries 12 directions (30° apart, starting at `start`, alternating sides) at `dist` from `centre`, height `height` above the ground
        /// there; the first usable one wins. `avoid` (when set): skip directions within 60° of it.</summary>
        static bool Around(Vector3 centre, Vector3 look, Vector3 start, float dist, float height, Vector3? avoid, out Vector3 pos, List<string> log, string what)
        {
            Vector3 s0 = new Vector3(start.x, 0f, start.z);
            if (s0.sqrMagnitude < 0.0001f) s0 = Vector3.forward;
            s0.Normalize();
            for (int k = 0; k < 12; k++)
            {
                float a = (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * 30f;
                Vector3 dir = Quaternion.AngleAxis(a, Vector3.up) * s0;
                if (avoid.HasValue && Vector3.Angle(dir, avoid.Value) < 60f) continue;
                Vector3 p = centre + dir * dist;
                if (!Ground(p, out float gy)) continue;
                p.y = gy + height;
                if (PointUsable(p, look, out _)) { pos = p; return true; }
            }
            pos = Vector3.zero;
            log.Add("WARN " + what + ": no usable camera point around " + centre.ToString("F1"));
            return false;
        }

        static Data BuildData(Ctx c, List<string> log)
        {
            var d = new Data();
            var names = new List<string>(); var pos = new List<Vector3>(); var looks = new List<Vector3>(); var fovs = new List<float>(); var stations = new List<int>(); var keys = new List<string>();
            // ---- wash stations: 2.6 m out, 1.5 m up, looking at the stand (+0.4 m); start 35° off the station's own facing ----
            var calls = WashCalls();
            for (int k = 0; k < calls.Length; k++)
            {
                Transform a = calls[k].anchor != null ? calls[k].anchor : calls[k].transform;
                Vector3 look = a.position + Vector3.up * 0.4f;
                Vector3 start = Quaternion.AngleAxis(35f, Vector3.up) * calls[k].transform.forward;
                if (Around(a.position, look, start, SpotWashDist, SpotWashHeight, null, out Vector3 p, log, "wash spot " + (k + 1)))
                { names.Add(SpotWordWash + (k + 1)); keys.Add(KH_VideoCamera.KeySpotWash); pos.Add(p); looks.Add(look); fovs.Add(SpotWashFov); stations.Add(k); }
            }
            // ---- the water play area: from the bank (returns[0]) side, high; and a low one at least 60° away ----
            var ws = UnityEngine.Object.FindObjectsOfType<KH_WaterSpot>(true);
            if (ws.Length == 1)
            {
                var w = ws[0];
                Vector3 look = w.exits != null && w.exits.Length > 0 ? (w.entry + w.exits[0]) * 0.5f : w.entry;
                if (Ground(look, out float ly)) look.y = ly + 0.3f;
                Vector3 bank = w.returns != null && w.returns.Length > 0 ? w.returns[0] - look : Vector3.back;
                if (Around(look, look, bank, SpotRiverDist, SpotRiverHeight, null, out Vector3 hi, log, "river spot"))
                {
                    names.Add(SpotWordRiver); keys.Add(KH_VideoCamera.KeySpotRiver); pos.Add(hi); looks.Add(look); fovs.Add(SpotRiverFov); stations.Add(-1);
                    Vector3 used = hi - look; used.y = 0f;
                    if (Around(look, look, -used, SpotRiverLowDist, SpotRiverLowHeight, used, out Vector3 lo, log, "river low spot"))
                    { names.Add(SpotWordRiverLow); keys.Add(KH_VideoCamera.KeySpotRiverLow); pos.Add(lo); looks.Add(look); fovs.Add(SpotRiverFov); stations.Add(-1); }
                }
            }
            d.spotNames = names.ToArray(); d.spotPositions = pos.ToArray(); d.spotLooks = looks.ToArray(); d.spotFovs = fovs.ToArray(); d.spotStations = stations.ToArray(); d.spotKeys = keys.ToArray();
            for (int i = 0; i < d.spotNames.Length; i++) log.Add("spot " + d.spotNames[i] + " at " + d.spotPositions[i].ToString("F2") + " looking at " + d.spotLooks[i].ToString("F2"));

            // ---- pass-by points: the photo walk spots, 3 (else 4.5) m to the side of the walk route, 1.4 m up ----
            var watchers = UnityEngine.Object.FindObjectsOfType<KH_PhotoWalkWatcher>(true);
            var route = c.manager != null ? c.manager.route : null;
            var pp = new List<Vector3>();
            if (watchers.Length == 1 && watchers[0].spotPositions != null)
            {
                var nodes = route != null && route.nodePositions != null ? route.nodePositions : new Vector3[0];
                foreach (var spot in watchers[0].spotPositions)
                {
                    Vector3 along = Vector3.forward;
                    if (nodes.Length >= 2)
                    {
                        var near = nodes.OrderBy(q => (q - spot).sqrMagnitude).Take(2).ToArray();
                        along = near[1] - near[0];
                    }
                    Vector3 side = Vector3.Cross(Vector3.up, new Vector3(along.x, 0f, along.z).normalized);
                    Vector3 look = spot + Vector3.up * 0.5f;
                    bool ok = false;
                    foreach (float s in new[] { PassSide, -PassSide, PassSideFar, -PassSideFar })
                    {
                        Vector3 p = spot + side * s;
                        if (!Ground(p, out float gy)) continue;
                        p.y = gy + PassHeight;
                        if (PointUsable(p, look, out _)) { pp.Add(p); ok = true; log.Add("pass point at " + p.ToString("F2") + " beside " + spot.ToString("F2")); break; }
                    }
                    if (!ok) log.Add("WARN no usable pass point beside " + spot.ToString("F1"));
                }
            }
            else log.Add("WARN KH_PhotoWalkWatcher x" + watchers.Length + ": no pass-by points (通り過ぎ falls back to the じぶん framing)");
            d.passPositions = pp.ToArray();
            return d;
        }
    }
}
