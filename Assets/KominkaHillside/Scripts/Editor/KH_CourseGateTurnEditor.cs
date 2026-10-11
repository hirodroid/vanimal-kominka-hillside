using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace KominkaHillside.Editor
{
    /// <summary>DES-WLD-004 M3 Editor 1 (docs/plans/auto-walk-cart/implementation.md「M3」): 門前の折返し (節 433〜435 → 0 (門) → 1) を滑らかにする。
    ///
    /// Course/21a は既存の周回路を**その場で**直す (節の数と番号は変えない。Course/21 の「17 節の初期状態」は前提にしない):
    /// 節 432 と節 2 を結ぶ 2 次ベジエ (制御点は門側へ膨らむ (44.5, 45.0) から、門枠の bounds から 1.0 m 以上離れるまで弦の中点へ寄せる) の上へ、
    /// 節 433・434・435・0・1 を置き直す。節 0 (入口の目的地) は曲線上で門に最も近い点、433〜435 は 432 と節 0 の間を弧長で等分、節 1 は節 0 と節 2 の中点。
    /// 各点は Raycast (Default + Environment) で高さを取り NavMesh.SamplePosition (0.5 m) で通行面へ寄せる。arriveRadius 1.2、Transform と nodePositions の両方を更新、
    /// loopDistances / loopLength を NavMesh の経路長で再計算 (Course/21 と同じ式)、置き直した 6 リンクは経路長が直線の 1.3 倍以内、セル索引を作り直し、CopyProxyToUdon、保存。
    /// 失敗は書き換え前に止まる (退避: Temp/des-env-002/before-gate-turn.unity)。後で Cart/1 を流して軌道を焼き直す。
    ///
    /// Course/22 - Verify Full Course Route は、節 0・1 だけ「整形済み (arriveRadius 1.2 かつ門枠から 1.0 m 以上)」なら元の位置との差を許容する (<see cref="IsSmoothedGateNode"/>)。</summary>
    public static class KH_CourseGateTurnEditor
    {
        const string OutDir = "Temp/des-env-002";
        public static readonly int[] Moved = { 433, 434, 435, 0, 1 };
        public const int From = 432, To = 2, Nodes = 436;
        public static readonly Vector2 InitialControl = new Vector2(44.5f, 45.0f);
        public const float GateClear = 1.0f, Radius = 1.2f, LinkRatio = 1.3f, TurnLimitDeg = 45f, NavSnap = .5f;
        const int Mask = (1 << 0) | (1 << 11);

        [Serializable] class Report
        {
            public string utc, scene, status, gatePath;
            public Vector2 control;
            public float gateClearMin, loopLengthBefore, loopLengthAfter;
            public List<string> before = new List<string>(), after = new List<string>(), links = new List<string>(), findings = new List<string>();
        }

        static Vector2 XZ(Vector3 v) { return new Vector2(v.x, v.z); }

        /// <summary>節 i (0 / 1) が門前整形後の状態か: arriveRadius が 1.2 で、門枠から 1.0 m 以上離れている。Course/22 - Verify Full Course Route が元の位置との差を許すときに使う。</summary>
        public static bool IsSmoothedGateNode(KH_WalkRoute r, int i)
        {
            if ((i != 0 && i != 1) || r.count != Nodes || !Mathf.Approximately(r.arriveRadius[i], Radius)) return false;
            return KH_CartSetupEditor.GateBounds(out Bounds gate, out string _) && GateDistance(gate, XZ(r.nodePositions[i])) >= GateClear;
        }
        static Vector2 Bez(Vector2 a, Vector2 c, Vector2 b, float t) { float u = 1f - t; return u * u * a + 2f * u * t * c + t * t * b; }

        /// <summary>点と門枠の bounds (水平の矩形) の距離。中なら 0。</summary>
        public static float GateDistance(Bounds gate, Vector2 p)
        {
            float dx = Mathf.Max(gate.min.x - p.x, 0f, p.x - gate.max.x), dz = Mathf.Max(gate.min.z - p.y, 0f, p.y - gate.max.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static KH_WalkRoute Route()
        {
            var routes = UnityEngine.Object.FindObjectsOfType<KH_WalkRoute>(true);
            if (routes.Length != 1) throw new InvalidOperationException("Exactly one KH_WalkRoute required");
            var r = routes[0];
            if (!r.closedLoop || r.count != Nodes || r.nodes == null || r.nodes.Length != Nodes || r.nodePositions == null || r.nodePositions.Length != Nodes || r.arriveRadius == null || r.arriveRadius.Length != Nodes)
                throw new InvalidOperationException("Closed " + Nodes + "-node course route required (count " + r.count + ")");
            return r;
        }

        [MenuItem("Tools/KominkaHillside/Course/21a - Smooth Gate Turn")]
        public static void Smooth()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = Route();
            var report = new Report { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, loopLengthBefore = r.loopLength };
            if (!KH_CartSetupEditor.GateBounds(out Bounds gate, out string gatePath)) throw new InvalidOperationException("Gate frame '" + KH_CartSetupEditor.GateFrameName + "' not found; nothing changed");
            report.gatePath = gatePath;
            foreach (int i in new[] { From }.Concat(Moved).Concat(new[] { To })) report.before.Add(i + ": " + r.nodePositions[i].ToString("F2") + " gate " + GateDistance(gate, XZ(r.nodePositions[i])).ToString("F2") + " m");

            // ---- 曲線 (読むだけ) ----
            Vector2 a = XZ(r.nodePositions[From]), b = XZ(r.nodePositions[To]), mid = (a + b) * .5f, c = InitialControl;
            const int Samples = 400;
            float CurveClear(Vector2 ctl) { float m = float.MaxValue; for (int k = 0; k <= Samples; k++) m = Mathf.Min(m, GateDistance(gate, Bez(a, ctl, b, k / (float)Samples))); return m; }
            int guard = 0;
            while (CurveClear(c) < GateClear + .05f && guard++ < 100) c = Vector2.Lerp(c, mid, .1f);
            if (CurveClear(c) < GateClear) throw new InvalidOperationException("Cannot keep the curve " + GateClear + " m from the gate; nothing changed");
            report.control = c;
            // 弧長表
            var pts = new Vector2[Samples + 1]; var arc = new float[Samples + 1];
            for (int k = 0; k <= Samples; k++) { pts[k] = Bez(a, c, b, k / (float)Samples); if (k > 0) arc[k] = arc[k - 1] + (pts[k] - pts[k - 1]).magnitude; }
            Vector2 gateCentre = XZ(gate.center);
            int kGate = 0; for (int k = 1; k < Samples; k++) if (GateDistance(gate, pts[k]) < GateDistance(gate, pts[kGate]) || (Mathf.Approximately(GateDistance(gate, pts[k]), GateDistance(gate, pts[kGate])) && (pts[k] - gateCentre).sqrMagnitude < (pts[kGate] - gateCentre).sqrMagnitude)) kGate = k;
            if (kGate < 4 || kGate > Samples - 2) throw new InvalidOperationException("Closest point to the gate is at the curve end (k " + kGate + "); nothing changed");
            float sGate = arc[kGate], sEnd = arc[Samples];
            Vector2 AtArc(float s) { int k = 1; while (k < Samples && arc[k] < s) k++; float t = arc[k] - arc[k - 1] > 1e-6f ? (s - arc[k - 1]) / (arc[k] - arc[k - 1]) : 0f; return Vector2.Lerp(pts[k - 1], pts[k], t); }
            var flat = new Dictionary<int, Vector2> {
                { 433, AtArc(sGate * .25f) }, { 434, AtArc(sGate * .5f) }, { 435, AtArc(sGate * .75f) }, { 0, pts[kGate] }, { 1, AtArc(sGate + (sEnd - sGate) * .5f) } };

            // ---- 高さと通行面 (読むだけ) ----
            var placed = new Dictionary<int, Vector3>();
            foreach (var kv in flat)
            {
                var q = new Vector3(kv.Value.x, 150f, kv.Value.y);
                if (!Physics.Raycast(q, Vector3.down, out RaycastHit hit, 160f, Mask, QueryTriggerInteraction.Ignore)) throw new InvalidOperationException("No footing at node " + kv.Key + " " + kv.Value + "; nothing changed");
                var p = new Vector3(kv.Value.x, hit.point.y, kv.Value.y);
                if (!NavMesh.SamplePosition(p, out NavMeshHit nav, NavSnap, NavMesh.AllAreas)) throw new InvalidOperationException("No navigation at node " + kv.Key + " " + p + "; nothing changed");
                if (GateDistance(gate, XZ(nav.position)) < GateClear) throw new InvalidOperationException("Node " + kv.Key + " snapped within " + GateClear + " m of the gate; nothing changed");
                placed[kv.Key] = nav.position;
            }
            var points = (Vector3[])r.nodePositions.Clone();
            foreach (var kv in placed) points[kv.Key] = kv.Value;

            // ---- 経路長 (Course/21 と同じ式) と、置き直した 6 リンクの 1.3 倍検査 ----
            var cumulative = new float[Nodes]; float total = 0f; var path = new NavMeshPath();
            var touched = new HashSet<int> { From, 433, 434, 435, 0, 1 };   // リンク i = 節 i → i+1
            for (int i = 0; i < Nodes; i++)
            {
                int j = (i + 1) % Nodes;
                if (!NavMesh.CalculatePath(points[i], points[j], NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) throw new InvalidOperationException("Disconnected route link " + i + "; nothing changed");
                float length = 0f; for (int k = 1; k < path.corners.Length; k++) length += Vector3.Distance(path.corners[k - 1], path.corners[k]);
                float straight = Vector3.Distance(points[i], points[j]);
                if (touched.Contains(i))
                {
                    report.links.Add(i + "->" + j + " path " + length.ToString("F2") + " m / straight " + straight.ToString("F2") + " m");
                    if (length > straight * LinkRatio || length < .01f) throw new InvalidOperationException("Link " + i + "->" + j + " path " + length.ToString("F2") + " > " + LinkRatio + " x straight " + straight.ToString("F2") + "; nothing changed");
                }
                else if (length > straight * 1.5f + 1f || length < .01f) throw new InvalidOperationException("Invalid route link length " + i + " " + length + "; nothing changed");
                total += length; if (j > 0) cumulative[j] = total;
            }

            // ---- ここから書き換え ----
            Directory.CreateDirectory(OutDir);
            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-gate-turn.unity", true);
            try
            {
                foreach (var kv in placed)
                {
                    r.nodes[kv.Key].position = kv.Value; EditorUtility.SetDirty(r.nodes[kv.Key]);
                    r.arriveRadius[kv.Key] = Radius;
                }
                r.nodePositions = points; r.loopDistances = cumulative; r.loopLength = total;
                KH_CourseRouteEditor.Index(r);
                UdonSharpEditorUtility.CopyProxyToUdon(r); EditorUtility.SetDirty(r);
                EditorSceneManager.MarkSceneDirty(r.gameObject.scene);
                if (!EditorSceneManager.SaveScene(r.gameObject.scene)) throw new IOException("Gate turn save failed");
            }
            catch
            {
                Debug.LogError("[KH Gate Turn] failed; the scene before this run is at " + OutDir + "/before-gate-turn.unity. Reopen the scene without saving to discard partial changes.");
                throw;
            }
            report.loopLengthAfter = total;
            report.gateClearMin = Moved.Min(i => GateDistance(gate, XZ(points[i])));
            foreach (int i in new[] { From }.Concat(Moved).Concat(new[] { To })) report.after.Add(i + ": " + points[i].ToString("F2") + " gate " + GateDistance(gate, XZ(points[i])).ToString("F2") + " m");
            report.status = "SMOOTHED";
            File.WriteAllText(OutDir + "/gate-turn.json", JsonUtility.ToJson(report, true));
            Debug.Log("[KH Gate Turn] SMOOTHED control=" + c.ToString("F2") + " gate=" + gatePath + " clear>=" + report.gateClearMin.ToString("F2") + " m loop " + report.loopLengthBefore.ToString("F1") + " -> " + total.ToString("F1")
                + "\nbefore: " + string.Join(" | ", report.before) + "\nafter:  " + string.Join(" | ", report.after) + "\nNext: Course/21b, then Cart/1 (rebake the cart track), Cart/2, Cart/3.");
        }

        [MenuItem("Tools/KominkaHillside/Course/21b - Verify Gate Turn")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var report = new Report { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            var e = report.findings;
            try
            {
                var r = Route();
                report.loopLengthAfter = r.loopLength;
                bool gateOk = KH_CartSetupEditor.GateBounds(out Bounds gate, out string gatePath);
                report.gatePath = gatePath;
                if (!gateOk) e.Add("FAIL gate frame not found");
                var seq = new[] { From }.Concat(Moved).Concat(new[] { To }).ToArray();
                foreach (int i in seq) report.after.Add(i + ": " + r.nodePositions[i].ToString("F2") + (gateOk ? " gate " + GateDistance(gate, XZ(r.nodePositions[i])).ToString("F2") + " m" : ""));
                // 向きの変化 (432→2 の連続する 3 節)
                for (int k = 1; k + 1 < seq.Length; k++)
                {
                    var d0 = XZ(r.nodePositions[seq[k]] - r.nodePositions[seq[k - 1]]); var d1 = XZ(r.nodePositions[seq[k + 1]] - r.nodePositions[seq[k]]);
                    float turn = Vector2.Angle(d0, d1);
                    if (turn >= TurnLimitDeg) e.Add("FAIL turn at node " + seq[k] + " = " + turn.ToString("F1") + " deg (>= " + TurnLimitDeg + ")");
                    else report.links.Add("turn at " + seq[k] + " = " + turn.ToString("F1") + " deg");
                }
                // 曲線の両端 (431→432→433 と 1→2→3) は参考 (警告のみ)
                foreach (var (p0, p1, p2) in new[] { (From - 1, From, 433), (1, To, To + 1) })
                {
                    float turn = Vector2.Angle(XZ(r.nodePositions[p1] - r.nodePositions[p0]), XZ(r.nodePositions[p2] - r.nodePositions[p1]));
                    if (turn >= TurnLimitDeg) e.Add("WARN turn at curve end node " + p1 + " = " + turn.ToString("F1") + " deg"); else report.links.Add("turn at end " + p1 + " = " + turn.ToString("F1") + " deg");
                }
                foreach (int i in Moved)
                {
                    var p = r.nodePositions[i];
                    if (!NavMesh.SamplePosition(p, out NavMeshHit nav, NavSnap, NavMesh.AllAreas) || XZ(nav.position - p).magnitude > .05f) e.Add("FAIL node " + i + " is not on the NavMesh");
                    if (gateOk && GateDistance(gate, XZ(p)) < GateClear) e.Add("FAIL node " + i + " is " + GateDistance(gate, XZ(p)).ToString("F2") + " m from the gate (< " + GateClear + ")");
                    if (r.nodes[i].position != p) e.Add("FAIL node " + i + " Transform differs from nodePositions");
                    if (!Mathf.Approximately(r.arriveRadius[i], Radius)) e.Add("FAIL node " + i + " arriveRadius " + r.arriveRadius[i] + " (expected " + Radius + ")");
                }
                if (gateOk) report.gateClearMin = Moved.Min(i => GateDistance(gate, XZ(r.nodePositions[i])));
                // 経路長の整合 (保存値 = 隣接の経路長の累積)
                bool mono = r.loopDistances[0] == 0f; for (int i = 1; i < Nodes && mono; i++) mono = r.loopDistances[i] > r.loopDistances[i - 1];
                if (!mono || r.loopDistances[Nodes - 1] >= r.loopLength) e.Add("FAIL loopDistances not increasing / last >= loopLength");
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(r);
                foreach (var field in new[] { "nodePositions", "loopDistances", "arriveRadius", "linkCellStarts", "linkCellIndices", "entryCellStarts", "entryCellNodes" })
                {
                    var expected = (Array)typeof(KH_WalkRoute).GetField(field).GetValue(r);
                    if (backing == null || !backing.publicVariables.TryGetVariableValue(field, out object saved) || !(saved is Array actual) || actual.Length != expected.Length) { e.Add("FAIL backing " + field + " missing / length"); continue; }
                    for (int i = 0; i < expected.Length; i++) if (!Equals(actual.GetValue(i), expected.GetValue(i))) { e.Add("FAIL backing " + field + "[" + i + "] differs"); break; }
                }
                if (backing != null && backing.publicVariables.TryGetVariableValue("loopLength", out object ll) && !(ll is float lf && Mathf.Abs(lf - r.loopLength) < 1e-3f)) e.Add("FAIL backing loopLength differs");
                // 乗り場 0 (spotNodes[0] = 435) は警告だけ (着地位置は Course/80 のまま)
                var leash = KH_CourseTravelEditor.FindPouch().manager;
                if (leash.spotNodes == null || leash.spotNodes.Length == 0 || leash.spotNodes[0] != 435) e.Add("WARN spotNodes[0] = " + (leash.spotNodes != null && leash.spotNodes.Length > 0 ? leash.spotNodes[0].ToString() : "none") + " (expected 435)");
                else if (leash.spotPositions != null && leash.spotPositions.Length > 0 && XZ(leash.spotPositions[0] - r.nodePositions[435]).magnitude > 3f)
                    e.Add("WARN spot 0 landing " + leash.spotPositions[0].ToString("F1") + " is " + XZ(leash.spotPositions[0] - r.nodePositions[435]).magnitude.ToString("F1") + " m from node 435 (Course/80 keeps the landing)");
                if (File.Exists(OutDir + "/gate-turn.json")) { var prev = JsonUtility.FromJson<Report>(File.ReadAllText(OutDir + "/gate-turn.json")); report.before = prev.before; report.control = prev.control; report.loopLengthBefore = prev.loopLengthBefore; }
            }
            catch (Exception ex) { e.Add("FAIL Exception: " + ex.Message); }
            int fails = e.Count(x => x.StartsWith("FAIL"));
            report.status = fails == 0 ? "GATE_TURN_OK" : "FAIL";
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/gate-turn-verify.json", JsonUtility.ToJson(report, true));
            string summary = "[KH Gate Turn] " + report.status + " fails=" + fails + " warns=" + (e.Count - fails) + " clear>=" + report.gateClearMin.ToString("F2") + " m"
                + "\nbefore: " + string.Join(" | ", report.before) + "\nafter:  " + string.Join(" | ", report.after) + "\n" + string.Join("; ", report.links) + (e.Count > 0 ? "\n" + string.Join("\n", e) : "");
            if (fails > 0) throw new InvalidOperationException(summary);
            Debug.Log(summary);
        }
    }
}
