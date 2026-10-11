using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    /// <summary>Cart/3: Edit モードで経路の純関数を呼び、契約の式 (implementation.md「Motion」) と経路データを突き合わせる。シーンは変えない。
    /// KH_WalkRoute._PointAtDistance / _DirectionAtDistance / _DetourAtDistance は Runtime 担当が追加するので Reflection で呼び、無ければ SKIP。
    /// 向きの選択・隊列のずれ・橋で一列・到着の判定は、経路データ (loopDistances / linkAllowsDetour / _Next) と契約の式だけで計算する (Runtime の Manager は呼ばない)。
    /// 結果は Temp/cart-20261011/probe.json。</summary>
    public static partial class KH_CartSetupEditor
    {
        [Serializable] class ProbeReport
        {
            public string utc, scene, status;
            public int fail, skip, pass;
            public float loopLength, speed, gap, lateral, arriveSlack;
            public int nodes;
            public string settingsSource;
            public List<string> failItems = new List<string>();
            public List<string> skipItems = new List<string>();
            public List<string> passItems = new List<string>();
            public List<string> directions = new List<string>();
            public List<string> formation = new List<string>();
            public List<string> noDetourRanges = new List<string>();
            public List<string> arrivals = new List<string>();
            public float trackLength, closingSegment;   // M1.1
            public string noLateral;
            public string limitations = "Edit-mode math only: route proxy methods (if present) and the contract formulas evaluated in C#. Does not run KH_CartManager, server time, ground raycasts, SmoothDamp, stations or networking.";
        }

        class P
        {
            public ProbeReport r;
            public void Req(bool ok, string what) { if (ok) r.passItems.Add(what); else r.failItems.Add(what); }
            public void Skip(string what) { r.skipItems.Add(what); }
        }

        [MenuItem("Tools/KominkaHillside/Cart/3 Probe Cart Motion")]
        public static void Probe()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var report = new ProbeReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            var pr = new P { r = report };
            try { RunProbe(pr); }
            catch (Exception ex) { report.failItems.Add("Exception: " + ex.GetType().Name + ": " + ex.Message); }
            report.fail = report.failItems.Count; report.skip = report.skipItems.Count; report.pass = report.passItems.Count;
            report.status = report.fail > 0 ? "FAIL" : report.skip > 0 ? "OK_WITH_SKIP" : "PROBE_OK";
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/probe.json", JsonUtility.ToJson(report, true));
            string summary = "[KH Cart] Probe " + report.status + " fail=" + report.fail + " skip=" + report.skip + " pass=" + report.pass + " -> " + OutDir + "/probe.json";
            if (report.fail > 0) Debug.LogError(summary + "\n" + string.Join("\n", report.failItems.Take(30)));
            else Debug.Log(summary + (report.skip > 0 ? "\nSKIP: " + string.Join("\n", report.skipItems) : ""));
        }

        static void RunProbe(P pr)
        {
            var r = pr.r;
            var leash = KH_CourseTravelEditor.FindPouch().manager;
            var route = leash.route;
            int n = route.count; float L = route.loopLength; var ld = route.loopDistances;
            r.nodes = n; r.loopLength = L;
            if (!route.closedLoop || ld == null || ld.Length != n || L <= 0f || route.nodePositions == null || route.nodePositions.Length != n) { pr.Req(false, "closed-loop route with loopDistances/nodePositions"); return; }

            // ---- 設定値: Manager があればその値、無ければ契約の既定値 ----
            r.speed = 1.3f; r.gap = 3f; r.lateral = .6f; r.arriveSlack = .5f; r.settingsSource = "contract defaults";
            var mgr = UnityEngine.Object.FindObjectsOfType<KH_CartManager>(true).FirstOrDefault();
            if (mgr != null)
            {
                var so = new SerializedObject(mgr);
                float F(string name, float def) { var p = so.FindProperty(name); return p != null && p.propertyType == SerializedPropertyType.Float ? p.floatValue : def; }
                r.speed = F("speed", r.speed); r.gap = F("gap", r.gap); r.lateral = F("lateral", r.lateral); r.arriveSlack = F("arriveSlack", r.arriveSlack);
                r.settingsSource = "KH_CartManager proxy";
            }
            pr.Req(Mathf.Approximately(r.speed, 1.3f) && Mathf.Approximately(r.gap, 3f) && Mathf.Approximately(r.lateral, .6f) && Mathf.Approximately(r.arriveSlack, .5f),
                "settings = contract (speed 1.3, gap 3, lateral 0.6, arriveSlack 0.5; actual " + r.speed + ", " + r.gap + ", " + r.lateral + ", " + r.arriveSlack + ")");

            // ---- 契約の前提: 周回のリンク i = ノード i → i+1、loopDistances は 0 始まりで単調増加 ----
            bool linksOk = route.linkA != null && route.linkB != null && route.linkA.Length >= n && route.linkB.Length >= n;
            for (int i = 0; linksOk && i < n; i++) linksOk = route.linkA[i] == i && route.linkB[i] == (i + 1) % n;
            pr.Req(linksOk, "loop link i = node i -> i+1 for all " + n + " links (linkAllowsDetour[i] is the segment starting at node i)");
            bool mono = Mathf.Abs(ld[0]) < 1e-4f;
            for (int i = 1; mono && i < n; i++) mono = ld[i] > ld[i - 1];
            pr.Req(mono && ld[n - 1] < L, "loopDistances start at 0, strictly increase, last < loopLength");
            bool detourArray = route.linkAllowsDetour != null && route.linkAllowsDetour.Length >= n;
            if (!detourArray) pr.Skip("route.linkAllowsDetour missing or short; contract treats every segment as detour-allowed");

            // ---- 経路の純関数 (Reflection) ----
            var t = route.GetType();
            var mPoint = t.GetMethod("_PointAtDistance", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(float) }, null);
            var mDir = t.GetMethod("_DirectionAtDistance", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(float) }, null);
            var mDetour = t.GetMethod("_DetourAtDistance", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(float) }, null);
            Func<float, Vector3> point = mPoint != null ? (Func<float, Vector3>)(d => (Vector3)mPoint.Invoke(route, new object[] { d })) : null;
            Func<float, Vector3> dirAt = mDir != null ? (Func<float, Vector3>)(d => (Vector3)mDir.Invoke(route, new object[] { d })) : null;
            Func<float, bool> detourAt = mDetour != null ? (Func<float, bool>)(d => (bool)mDetour.Invoke(route, new object[] { d })) : null;
            if (point == null) pr.Skip("KH_WalkRoute._PointAtDistance(float) not found (Runtime not merged yet): proxy continuity checks skipped; reference implementation used below");
            if (dirAt == null) pr.Skip("KH_WalkRoute._DirectionAtDistance(float) not found");
            if (detourAt == null) pr.Skip("KH_WalkRoute._DetourAtDistance(float) not found");

            // 参照実装 (契約の文言どおり: [0, L) へ正規化 → 二分探索 → 線形補間)
            Func<float, Vector3> refPoint = d => RefPoint(route, d);
            Func<float, Vector3> refDir = d => RefDirection(route, d);
            Func<float, bool> refDetour = d => !detourArray || route.linkAllowsDetour[RefSegment(route, d)];

            // ---- A. _PointAtDistance の連続性 ----
            if (point != null)
            {
                Vector3 p0 = point(0f), pHalf = point(L * .5f), pEnd = point(L - .01f), pNeg = point(-.01f);
                pr.Req((pNeg - pEnd).magnitude < 1e-3f, "_PointAtDistance(-0.01) == _PointAtDistance(L-0.01) (wrap; diff " + (pNeg - pEnd).magnitude.ToString("F4") + ")");
                pr.Req((pEnd - p0).magnitude < 0.05f, "_PointAtDistance(L-0.01) is next to (0) (closing step " + (pEnd - p0).magnitude.ToString("F4") + " m)");
                pr.Req((pHalf - refPoint(L * .5f)).magnitude < 1e-3f, "_PointAtDistance(L/2) matches the reference");
                pr.Req((point(L + 5f) - point(5f)).magnitude < 1e-3f && (point(-L + 5f) - point(5f)).magnitude < 1e-3f, "_PointAtDistance normalizes d outside [0, L)");
                int nodeMiss = 0; float worstNode = 0f;
                for (int k = 0; k < n; k++) { float e = (point(ld[k]) - route.nodePositions[k]).magnitude; if (e > .01f) nodeMiss++; worstNode = Mathf.Max(worstNode, e); }
                pr.Req(nodeMiss == 0, "_PointAtDistance(loopDistances[k]) == nodePositions[k] for all nodes (worst " + worstNode.ToString("F4") + " m)");
                const float step = .25f; float worstJump = 0f, worstAt = 0f, worstRef = 0f;
                Vector3 prev = point(0f);
                for (float d = step; d <= L + 1e-3f; d += step)
                {
                    var cur = point(d); var flat = cur - prev; flat.y = 0f;
                    if (flat.magnitude > worstJump) { worstJump = flat.magnitude; worstAt = d; }
                    worstRef = Mathf.Max(worstRef, (cur - refPoint(d)).magnitude);
                    prev = cur;
                }
                pr.Req(worstJump <= step * 1.02f + 1e-3f, "_PointAtDistance continuous: max horizontal step " + worstJump.ToString("F3") + " m per " + step + " m (at d=" + worstAt.ToString("F1") + ")");
                pr.Req(worstRef < .01f, "_PointAtDistance matches the reference over the loop (worst " + worstRef.ToString("F4") + " m)");
            }
            if (dirAt != null)
            {
                float worst = 1f; bool horizontal = true;
                for (float d = 0f; d < L; d += 1f) { var a = dirAt(d); var b = refDir(d); worst = Mathf.Min(worst, Vector3.Dot(a, b)); horizontal &= Mathf.Abs(a.y) < 1e-4f && Mathf.Abs(a.magnitude - 1f) < 1e-3f; }
                pr.Req(horizontal, "_DirectionAtDistance is a horizontal unit vector");
                pr.Req(worst > .999f, "_DirectionAtDistance matches segment direction (min dot " + worst.ToString("F4") + ")");
            }

            // ---- B. 乗り場の距離 → 位置が spotPositions に 3 m 以内 ----
            Func<float, Vector3> pointOf = point ?? refPoint;
            string src = point != null ? "proxy" : "reference";
            for (int s = 0; s < StationCount; s++)
            {
                if (leash.spotNodes == null || leash.spotNodes.Length <= s || leash.spotNodes[s] < 0 || leash.spotNodes[s] >= n) { pr.Req(false, "spotNodes[" + s + "] valid"); continue; }
                var q = pointOf(ld[leash.spotNodes[s]]) - leash.spotPositions[s]; q.y = 0f;
                pr.Req(q.magnitude <= 3f, "spot " + s + ": point at loopDistances[spotNodes] within 3 m of spotPositions (" + q.magnitude.ToString("F2") + " m, " + src + ")");
            }

            // ---- C. 向きの選択 (周回は 2 = +1 / 3 = -1、それ以外は _Next と同じ短い側) ----
            for (int s = 0; s < StationCount && leash.spotNodes != null && s < leash.spotNodes.Length; s++)
            {
                int from = leash.spotNodes[s];
                var row = new List<string>();
                for (int dest = 0; dest <= 5; dest++)
                {
                    int loopDir = route._LoopDirection(dest);
                    int to = route._DestinationNode(dest);
                    if (loopDir != 0) { row.Add(dest + ":" + (loopDir > 0 ? "+1" : "-1") + "(loop)"); continue; }
                    if (to < 0) { row.Add(dest + ":none"); pr.Req(false, "destination " + dest + " has a node"); continue; }
                    if (to == from) { row.Add(dest + ":HERE"); continue; }
                    float fwd = ld[to] - ld[from]; if (fwd < 0f) fwd += L;
                    int dir = fwd <= L * .5f ? 1 : -1;
                    int next = route._Next(from, to);
                    pr.Req(next == (from + dir + n) % n, "spot " + s + " -> dest " + dest + ": contract direction " + dir + " agrees with route._Next");
                    row.Add(dest + ":" + (dir > 0 ? "+1" : "-1") + " " + (dir > 0 ? fwd : L - fwd).ToString("F0") + "m");
                }
                r.directions.Add("spot " + s + " (node " + from + "): " + string.Join("  ", row));
            }
            pr.Req(route._LoopDirection(2) == 1 && route._LoopDirection(3) == -1, "destination 2 = loop +1, 3 = loop -1");

            // ---- D. 隊列 (order 0..3) の前後・横オフセット ----
            Func<float, bool> D = detourAt ?? refDetour;
            Func<float, Vector3> Dir = dirAt ?? refDir;
            if (leash.spotNodes != null && leash.spotNodes.Length > 1)
                foreach (int dir in new[] { 1, -1 })
                {
                    float anchor = ld[leash.spotNodes[1]] + dir * 20f;
                    for (int order = 0; order < 4; order++)
                    {
                        float d = Wrap(anchor - dir * order * r.gap, L);
                        var centre = pointOf(d); var fwd = Dir(d) * dir; var right = new Vector3(fwd.z, 0f, -fwd.x);
                        int side = order % 2 == 0 ? 1 : -1; bool detour = D(d);
                        var pos = centre + right * side * r.lateral * (detour ? 1f : 0f);
                        float along = Mathf.Abs(Wrap(anchor - d + L * .5f, L) - L * .5f);
                        var off = pos - centre; off.y = 0f;
                        bool ok = Mathf.Abs(along - order * r.gap) < 1e-3f && Mathf.Abs(off.magnitude - (detour ? r.lateral : 0f)) < 1e-3f && (!detour || Vector3.Dot(off, right) * side > 0f);
                        pr.Req(ok, "formation dir " + dir + " order " + order + ": behind " + along.ToString("F2") + " m, lateral " + (Vector3.Dot(off, right)).ToString("F2") + " m (detour " + detour + ")");
                        r.formation.Add("dir " + dir + " order " + order + " d=" + d.ToString("F1") + " pos=" + pos.ToString("F2") + " side=" + side + " detour=" + detour);
                    }
                }

            // ---- E. 横ずれ禁止の区間 (橋など) で横ずれ 0 ----
            int mismatch = 0, samples = 0; float rangeStart = -1f;
            for (float d = 0f; d < L; d += .5f)
            {
                bool refD = refDetour(d); samples++;
                if (detourAt != null && detourAt(d) != refD) mismatch++;
                if (!refD && rangeStart < 0f) rangeStart = d;
                if (refD && rangeStart >= 0f) { r.noDetourRanges.Add(rangeStart.ToString("F1") + "-" + d.ToString("F1") + " m at " + refPoint(rangeStart).ToString("F0")); rangeStart = -1f; }
            }
            if (rangeStart >= 0f) r.noDetourRanges.Add(rangeStart.ToString("F1") + "-" + L.ToString("F1") + " m at " + refPoint(rangeStart).ToString("F0"));
            if (detourAt != null) pr.Req(mismatch == 0, "_DetourAtDistance == linkAllowsDetour[segment] at " + samples + " samples (mismatch " + mismatch + ")");
            if (detourArray)
            {
                pr.Req(r.noDetourRanges.Count > 0, "route has single-file (linkAllowsDetour=false) sections, e.g. bridges (" + r.noDetourRanges.Count + " ranges)");
                // 隊列を一列区間の上に置き、そこにいる車の横ずれが 0 で中心線上にいること (契約の式: side * lateral * (detour ? 1 : 0))
                float onBridge = -1f;
                for (float d = 0f; d < L && onBridge < 0f; d += .5f) if (!D(d)) onBridge = d;
                if (onBridge >= 0f)
                {
                    int onSingle = 0, offCentre = 0;
                    for (int order = 0; order < 4; order++)
                    {
                        float d = Wrap(onBridge + 1f - order * r.gap, L);
                        bool detour = D(d);
                        var fwd = Dir(d); var right = new Vector3(fwd.z, 0f, -fwd.x);
                        var off = right * (order % 2 == 0 ? 1 : -1) * r.lateral * (detour ? 1f : 0f);
                        if (!detour) { onSingle++; if (off.magnitude > 1e-4f) offCentre++; }
                    }
                    pr.Req(onSingle > 0 && offCentre == 0, "formation over the first single-file section (d=" + onBridge.ToString("F1") + "): " + onSingle + " cars on it, lateral 0");
                }
            }

            // ---- F. 到着: 目的地の距離に clamp して止まり、先頭は arriveSlack 以内で判定される ----
            for (int s = 0; s < StationCount && leash.spotNodes != null && s < leash.spotNodes.Length; s++)
            {
                int from = leash.spotNodes[s];
                for (int dest = 0; dest <= 5; dest++)
                {
                    if (route._LoopDirection(dest) != 0) continue;
                    int to = route._DestinationNode(dest);
                    if (to < 0 || to == from) continue;
                    float fwd = ld[to] - ld[from]; if (fwd < 0f) fwd += L;
                    int dir = fwd <= L * .5f ? 1 : -1;
                    float dist = dir > 0 ? fwd : L - fwd;          // 展開した距離 (0 → dist)
                    float tArrive = dist / r.speed;
                    float headAtSlack = Mathf.Min(dist, r.speed * (tArrive - r.arriveSlack / r.speed));
                    float headLater = Mathf.Min(dist, r.speed * (tArrive + 30f));   // clamp: 30 秒後も越えない
                    bool ok = dist - headAtSlack <= r.arriveSlack + 1e-3f && Mathf.Abs(headLater - dist) < 1e-4f;
                    var stopAt = pointOf(Wrap(ld[from] + dir * headLater, L)); var target = pointOf(ld[to]);
                    ok &= (stopAt - target).magnitude < .01f + (point == null ? .01f : 0f);
                    pr.Req(ok, "spot " + s + " -> dest " + dest + ": arrives after " + tArrive.ToString("F0") + " s and stops on the destination node");
                    r.arrivals.Add("spot " + s + " -> dest " + dest + " dir " + dir + " dist " + dist.ToString("F1") + " m, head " + tArrive.ToString("F0") + " s, order 3 " + ((dist + 3 * r.gap) / r.speed).ToString("F0") + " s");
                }
            }

            ProbeTrack(pr, route, leash);
        }

        /// <summary>M1.1: Manager の軌道関数 (TrackPoint / TrackDirection / TrackDetour / TrackNoLateral、Reflection) と保存済みの軌道配列を、
        /// route と参照実装に照合する。乖離は節 0 の近傍 (直結区間) を除いて 0.01 m 未満、直結区間は約 1.45 m。</summary>
        static void ProbeTrack(P pr, KH_WalkRoute route, KH_LeashManager leash)
        {
            var r = pr.r;
            var mgr = UnityEngine.Object.FindObjectsOfType<KH_CartManager>(true).FirstOrDefault();
            if (mgr == null) { pr.Skip("KH_CartManager not in scene: track probe skipped (run Cart/1)"); return; }
            var want = BuildTrack(route);
            ProjectStops(want, route, leash);
            var so = new SerializedObject(mgr);
            var pos = ReadVectors(so, "trackPositions");
            if (pos == null || pos.Length != want.positions.Length) { pr.Req(false, "manager.trackPositions length " + want.positions.Length + " (actual " + (pos != null ? pos.Length : -1) + "; run Cart/1)"); return; }
            var from = ReadFloats(so, "noLateralFrom"); var to = ReadFloats(so, "noLateralTo");
            r.trackLength = want.length; r.closingSegment = want.closing;
            r.noLateral = from != null && to != null ? string.Join(", ", from.Select((a, k) => a.ToString("F1") + "-" + to[k].ToString("F1"))) : "none";
            pr.Req(want.closing > .5f && want.closing < 3f, "closing segment 435 -> 1 is short (" + want.closing.ToString("F2") + " m, expected about 1.45)");

            var t = mgr.GetType();
            MethodInfo M(string name) { return t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(float) }, null); }
            var mPoint = M("TrackPoint"); var mDir = M("TrackDirection"); var mDetour = M("TrackDetour"); var mNo = M("TrackNoLateral");
            if (mPoint == null) pr.Skip("KH_CartManager.TrackPoint(float) not found");
            if (mDir == null) pr.Skip("KH_CartManager.TrackDirection(float) not found");
            if (mDetour == null) pr.Skip("KH_CartManager.TrackDetour(float) not found");
            if (mNo == null) pr.Skip("KH_CartManager.TrackNoLateral(float) not found");
            float ld1 = route.loopDistances[1], lastNode = want.distances[want.distances.Length - 1];
            try
            {
                if (mPoint != null)
                {
                    Func<float, Vector3> tp = d => (Vector3)mPoint.Invoke(mgr, new object[] { d });
                    int nodeMiss = 0; for (int k = 0; k < want.positions.Length; k++) if ((tp(want.distances[k]) - want.positions[k]).magnitude > .01f) nodeMiss++;
                    pr.Req(nodeMiss == 0, "TrackPoint(trackDistances[k]) == trackPositions[k] for all 435 nodes (miss " + nodeMiss + ")");
                    // route との乖離: 節 1 から節 435 までの区間 (直結区間 = 節 0 の近傍を除く)
                    float worst = 0f, worstAt = 0f;
                    for (float d = 0f; d <= lastNode; d += .5f) { float e = (tp(d) - route._PointAtDistance(d + ld1)).magnitude; if (e > worst) { worst = e; worstAt = d; } }
                    pr.Req(worst < .01f, "TrackPoint matches route._PointAtDistance away from node 0 (worst " + worst.ToString("F4") + " m at d=" + worstAt.ToString("F1") + ")");
                    // 直結区間は節 435 → 節 1 の直線
                    var mid = tp(lastNode + want.closing * .5f); var lin = Vector3.Lerp(want.positions[want.positions.Length - 1], want.positions[0], .5f);
                    pr.Req((mid - lin).magnitude < .01f, "closing segment is the straight line node 435 -> node 1");
                    pr.Req((tp(-.01f) - tp(want.length - .01f)).magnitude < 1e-3f && (tp(want.length + 3f) - tp(3f)).magnitude < 1e-3f, "TrackPoint wraps at trackLength");
                    // 門枠: 乗り場の開始点と入口の到着点
                    if (GateBounds(out Bounds gate, out string gatePath))
                    {
                        bool InGate(Vector3 q) { return q.x >= gate.min.x && q.x <= gate.max.x && q.z >= gate.min.z && q.z <= gate.max.z; }
                        for (int s = 0; s < StationCount; s++) pr.Req(!InGate(tp(want.spot[s])), "spot " + s + " start outside the gate frame (TrackPoint)");
                        var e0 = tp(want.dest[0]);
                        pr.Req(!InGate(e0) && e0.z > gate.center.z, "entrance arrival in front of the gate (TrackPoint " + e0.ToString("F1") + ")");
                    }
                    else pr.Skip("gate frame not found");
                }
                if (mDir != null)
                {
                    float worstDot = 1f;
                    for (float d = 0f; d < want.length; d += 1f) worstDot = Mathf.Min(worstDot, Vector3.Dot((Vector3)mDir.Invoke(mgr, new object[] { d }), TrackDirectionRef(want, d)));
                    pr.Req(worstDot > .999f, "TrackDirection matches the track segments (min dot " + worstDot.ToString("F4") + ")");
                }
                if (mDetour != null)
                {
                    int miss = 0;
                    for (float d = 0f; d < want.length; d += .5f) if ((bool)mDetour.Invoke(mgr, new object[] { d }) != want.detour[TrackSegment(want, d)]) miss++;
                    pr.Req(miss == 0, "TrackDetour == trackDetour[segment] (mismatch " + miss + ")");
                }
                if (mNo != null)
                {
                    int miss = 0;
                    for (float d = 0f; d < want.length; d += .5f) if ((bool)mNo.Invoke(mgr, new object[] { d }) != InNoLateral(from, to, d)) miss++;
                    pr.Req(miss == 0, "TrackNoLateral == noLateralFrom/To ranges (mismatch " + miss + ")");
                    var bench = GameObject.Find(BenchPath);
                    if (bench != null)
                    {
                        float db = ProjectToTrack(want, bench.transform.position);
                        pr.Req((bool)mNo.Invoke(mgr, new object[] { db }), "TrackNoLateral true at the viewpoint bench (d=" + db.ToString("F1") + ")");
                    }
                    else pr.Skip("bench " + BenchPath + " not found");
                }
            }
            catch (TargetInvocationException ex) { pr.Req(false, "track function threw in Edit mode: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message)); }
        }

        static float Wrap(float d, float L) { d %= L; return d < 0f ? d + L : d; }

        static int RefSegment(KH_WalkRoute route, float d)
        {
            var ld = route.loopDistances; int n = route.count;
            d = Wrap(d, route.loopLength);
            int lo = 0, hi = n - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (ld[mid] <= d) lo = mid; else hi = mid - 1; }
            return lo;
        }

        static Vector3 RefPoint(KH_WalkRoute route, float d)
        {
            var ld = route.loopDistances; int n = route.count;
            d = Wrap(d, route.loopLength);
            int i = RefSegment(route, d);
            float a = ld[i], b = i + 1 < n ? ld[i + 1] : route.loopLength;
            float u = b - a > 1e-6f ? (d - a) / (b - a) : 0f;
            return Vector3.Lerp(route.nodePositions[i], route.nodePositions[(i + 1) % n], u);
        }

        static Vector3 RefDirection(KH_WalkRoute route, float d)
        {
            int n = route.count; int i = RefSegment(route, d);
            var v = route.nodePositions[(i + 1) % n] - route.nodePositions[i]; v.y = 0f;
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
        }
    }
}
