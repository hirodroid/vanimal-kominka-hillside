using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-025 犬を洗う試作 (柴 1 匹) の ClientSim ハーネス — Course/100。
    ///
    /// 枠は周回コースの歩行ハーネスと同じ (Begin / Stop (Course/41) / 全体の締切 / PlayerData の退避と復旧 (Course/46) /
    /// シーンは保存しない / 結果は Logs/kh-course-walk/&lt;runId&gt;/report.json)。ケースごとの記録は同じ場所の wash.json。
    /// 手順の形は Course/96・97 (Commerce) と同じ: ケース = 手順の列、落ちたら片付けて次のケースへ、最後に WASH_PASS / WASH_FAIL。
    ///
    /// 入力は道具 (KH_WashTool) の当たり判定を通さず、道具が毎フレーム呼ぶ入口 KH_DogWash._LocalContact へ直接渡す
    /// (点は部位 Collider の ClosestPoint = 表面の点、ワールド座標)。接触の終わりは _EndContact。状態は _DebugSetDirt / _DebugClear で作る。
    /// 引数のある U# のメソッドは名前と引数の記号が U# に付け替えられる (__N_name / __N_param__param) ので、実行中のプログラムの
    /// 記号表から候補を引き、同じ名前の引数の候補すべてへ同じ値を書いてから SendCustomEvent する (U# の呼び出しは毎回引数を書き直すので
    /// 他のメソッドの引数を書いても害は無い)。最初の _LocalContact の後で _hCollider / _hMesh を読み、呼び出しが届いたことを確かめる。
    ///
    /// 観測: マスク (RenderTexture、実行時の現在の面 = ヒープの _cur。殻の _Mask とも照合) を ReadPixels で読み、位置マップ (RGB = バインド
    /// ポーズのメッシュ座標、A = 骨 + 1) で「接触の点に近いテクセル」を選ぶ。接触の点は実行時と同じ変換
    /// (partBones[i].InverseTransformPoint → boneToMesh[i]) でメッシュ座標へ直す。セルと合図は実行時のヒープ / 読み出しの入口から。
    ///
    /// ClientSim は 1 クライアント: NetStroke は自分へ返る経路 (Owner = 自分) しか通らない。見ているのは「ローカルの描画」と
    /// 「Owner のセルの積算」で、実通信・2 人・途中参加・Owner の交代は対象外 (wash.json の notCovered)。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/100 - Harness Wash (Shiba)")]
        public static void WashHarness()
        {
            Begin(false);
            report.washOnly = true;
            report.deadline = EditorApplication.timeSinceStartup + 2200; // Wash 1500 + WashBehavior ~260 + WashSync ~400
            report.limitations = "Single local ClientSim; the local player is instance master and every Owner (the wash object, the dog root, the tools). "
                + "Contacts are injected at KH_DogWash._LocalContact / _EndContact (the entry KH_WashTool calls every frame) with collider surface points; "
                + "the tool's pickup, raycasts, VR sphere and Desktop gaze are not exercised. NetStroke only takes the local echo path. "
                + "State is set with _DebugSetDirt / _DebugClear; the Shiba is placed at the gate at the start of the contact cases (the walk harnesses' placement injection). "
                + "Not covered: real network (other clients' draw, 8 Hz budget between clients, late-join repaint, Owner handover), visuals, audio, particles, devices.";
            Save();
        }

        const int WsSponge = KH_DogWash.ToolSponge, WsShower = KH_DogWash.ToolShower;
        const int WsNearK = 6;

        [Serializable] public class WsCaseResult
        {
            public string name, status = "PENDING";
            public List<string> notes = new List<string>();
        }
        [Serializable] class WsReport
        {
            public string runId, harness = "wash (Course/100)", dogName, maskSource;
            public int dog = -1, colliders, validTexels;
            public float meshToWorldScale;
            public List<string> resolved = new List<string>();
            public List<WsCaseResult> cases = new List<WsCaseResult>();
            public List<string> notCovered = new List<string>();
        }
        class WsSkip : Exception { public WsSkip(string m) : base(m) { } }
        struct WsHit { public int ci; public Vector3 p; }
        struct WsAnchor { public int ci; public Vector3 local; }
        class WsTrace
        {
            public List<Vector3> mesh = new List<Vector3>();
            public List<int> part = new List<int>();
            public List<int> ci = new List<int>();
            public List<float> u = new List<float>();
            public float sec, maxSec; public int calls, misses;
            public string Parts() { var r = new List<string>(); int prev = -99; foreach (int p in part) if (p != prev) { r.Add(p.ToString()); prev = p; } return string.Join(">", r); }
            public float LengthM() { float l = 0f; for (int i = 1; i < mesh.Count; i++) l += (mesh[i] - mesh[i - 1]).magnitude; return l * wsMts; }
        }

        static List<List<Func<double, bool>>> wsScripts;
        static List<double> wsLimits;
        static WsReport wsReport;
        static int wsStep, wsDog = -1;
        static double wsStepAt, wsCaseAt;
        static bool wsFailed, wsProbed;
        static float wsMaxDt;
        static UdonBehaviour wsWash, wsWander;
        static Transform wsRoot;
        static Renderer wsShell;
        static Collider[] wsCols;
        static Transform[] wsBones;
        static int[] wsColPart, wsNeighbors;
        static Matrix4x4[] wsB2M;
        static float[] wsBoneToPart;
        static float wsMts = 1f, wsSpongeR = 0.09f, wsShowerR = 0.07f, wsHoldSeconds = 3f;
        static int wsLayer = ~0;
        static int[] wsTexIdx, wsTexPart;   // valid texels of the position map
        static Vector3[] wsTexPos;
        static int wsTexCount;
        static readonly Dictionary<string, string> wsEntryCache = new Dictionary<string, string>();
        static readonly Dictionary<string, string[]> wsParamCache = new Dictionary<string, string[]>();
        static readonly List<KeyValuePair<float, Vector3>> wsPosHist = new List<KeyValuePair<float, Vector3>>();

        // ============================================================
        // 組み立て (MakeCases から)
        // ============================================================

        static void MakeWashCases()
        {
            wsScripts = new List<List<Func<double, bool>>>(); wsLimits = new List<double>();
            wsStep = 0; wsCaseAt = -1; wsFailed = false; wsProbed = false; wsMaxDt = 0f;
            wsEntryCache.Clear(); wsParamCache.Clear(); wsPosHist.Clear();
            wsReport = new WsReport { runId = report.runId };
            WsBind();
            WsNotCovered();

            // ---- W-idle (必ず最初: まだストロークが無いので、汚れ 0 で殻が消える) ----
            var s = WsCase("W-idle", 60);
            Vector3 idleStart = Vector3.zero; float idlePath = 0f; Vector3 idlePrev = Vector3.zero;
            s.Add(WsDo(() =>
            {
                WsDetachShiba();
                WsPlayerNear(6f);
                wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsWait(1.5));
            s.Add(WsDo(() =>
            {
                WsExpect(!WsHold(), "_WashHold() false with no stroke");
                WsExpect(!Get<bool>(wsWander, "_washActive"), "the wash branch is not active");
                WsExpect(wsShell != null && !wsShell.enabled, "shell renderer disabled at dirt 0 (enabled=" + (wsShell != null && wsShell.enabled) + ")");
                idleStart = idlePrev = wsRoot.position;
            }));
            s.Add(WsUntil(() =>
            {
                var p = wsRoot.position; idlePath += Flat(p - idlePrev); idlePrev = p;
                WsExpect(!WsHold(), "_WashHold() stayed false");
                return idlePath >= 0.5f;
            }, 45, () => "the Shiba did not move 0.5 m in 45 s (path " + idlePath.ToString("F2") + " m, restPhase " + Get<int>(wsWander, "_restPhase") + ", paused " + Get<bool>(wsWander, "_paused") + ")"));
            s.Add(WsDo(() => WsNote("no stroke: hold false, shell off after _DebugClear, walked " + idlePath.ToString("F2") + " m (net " + Flat(wsRoot.position - idleStart).ToString("F2") + " m)")));

            // ---- W-dirt ----
            s = WsCase("W-dirt", 20);
            s.Add(WsDo(() => WsSetDirt(0.6f)));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() =>
            {
                float ad = WsAvgDirt();
                WsExpect(ad > 0.2f, "_AverageDirt() > 0.2 (" + ad.ToString("F3") + ")");
                WsExpect(wsShell.enabled, "shell renderer enabled");
                var m = WsMask();
                int n = 0; for (int k = 0; k < wsTexCount; k++) if (m[wsTexIdx[k]].r > 0.05f) n++;
                WsExpect(n > 0, "mask R > 0.05 somewhere (0 of " + wsTexCount + " valid texels)");
                WsNote("_DebugSetDirt(0.6): avg dirt " + ad.ToString("F3") + ", shell on, mask R > 0.05 on " + n + "/" + wsTexCount + " valid texels (" + (n / (float)wsTexCount).ToString("P0") + ")");
            }));

            // ---- W-foam ----
            s = WsCase("W-foam", 40);
            var foT = new WsTrace(); Color[] fo0 = null; float foAvg0 = 0f, foLen = 0f;
            s.Add(WsDo(() => { WsPlaceShiba(); WsPlayerNear(1.3f); WsSetDirt(0.6f); }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => { fo0 = WsMask(); foAvg0 = WsAvgFoam(); Vector3 a, b; foLen = WsPathEnds(out a, out b); }));
            // 腰 → 胸 → 首の付け根を 2.5 s。毎フレーム、横から体へ Raycast して当たった Collider の表面の点 (道具と同じ。部位をまたぐと実行時が区間を切る)
            s.Add(WsDrive(2.5f, 1, u => WsPath(u), WsSponge, -1f, foT));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() =>
            {
                var fo1 = WsMask();
                WsExpect(foT.misses == 0, "every frame's ray from the flank hit a part collider (" + foT.misses + " of " + (foT.calls + foT.misses) + " missed: a gap between the part colliders along " + foLen.ToString("F3") + " m)");
                WsExpect(foT.LengthM() >= 0.15f, "the contact moved along the flank (" + foT.LengthM().ToString("F3") + " m on the mesh; world path " + foLen.ToString("F3") + " m)");
                string band = WsBandCheck(foT, fo0, fo1, 1, wsSpongeR, 5, d => d > 0.1f, "G +0.1");
                string gapText; float gmin = WsBandMin(foT, fo0, fo1, 1, 24, out gapText);
                WsExpect(gmin > 0.1f, "no gap in the foam band across the parts " + foT.Parts() + " (min dG " + gmin.ToString("F3") + "; " + gapText + ")");
                int nFar; float far = WsFarMax(foT.mesh, fo0, fo1, 1, 2f * wsSpongeR + 0.05f, out nFar);
                WsExpect(nFar > 500 && far < 0.02f, "outside the band (> " + (2f * wsSpongeR + 0.05f).ToString("F2") + " m) G did not grow: max +" + far.ToString("F3") + " over " + nFar + " texels");
                float af = WsAvgFoam();
                WsExpect(af > foAvg0 + 0.001f, "_AverageFoam() grew (" + foAvg0.ToString("F3") + " -> " + af.ToString("F3") + "; the Owner integrates the NetStroke echo)");
                int lp = WsLastPart();
                // 経路の終点付近では右から打った光線が右前足 (4) に当たることがある (実際の道具でも起こる正しい当たり)。
                // 終点を縮めると首の付け根の境目を検査できなくなるので、期待を緩める: 腰と胸を含み、ほかは首か右前足だけ
                var seen = new HashSet<int>(foT.part);
                WsExpect(seen.Contains(0) && seen.Contains(1) && seen.All(q => q <= 2 || q == 4), "the path touched hip and chest, others only neck / right front leg (parts " + foT.Parts() + ")");
                WsExpect(seen.Contains(lp), "_LastStrokePart() is a part the path touched (" + lp + ", parts " + foT.Parts() + ")");
                WsNote("sponge 2.5 s along the flank hip -> chest -> neck base (world " + foLen.ToString("F3") + " m, mesh " + foT.LengthM().ToString("F3") + " m, parts " + foT.Parts() + ", " + foT.calls + " calls, " + foT.sec.ToString("F2") + " s): band " + band + "; min dG along the band " + gmin.ToString("F3") + " (" + gapText + "); far max dG " + far.ToString("F3") + " (" + nFar + " texels); avg foam " + foAvg0.ToString("F3") + " -> " + af.ToString("F3") + "; last part " + lp + " (part set checked as {0,1} + only {2,4}: the flank ray near the path end may hit the right front leg, as a real tool would)");
            }));

            // ---- W-rinse (自分で泡を付けてから、区間の前 1/3 だけ流す: 残りの泡が帯になる) ----
            s = WsCase("W-rinse", 50);
            var riF = new WsTrace(); var riR = new WsTrace(); Color[] ri1 = null;
            s.Add(WsDo(() => { WsPlaceShiba(); WsPlayerNear(1.3f); WsSetDirt(0.6f); }));
            s.Add(WsWait(1.2));
            s.Add(WsDrive(3f, 1, u => WsPath(u), WsSponge, -1f, riF));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() => { ri1 = WsMask(); }));
            s.Add(WsDrive(1f, 1, u => WsPath(u / 3f), WsShower, -1f, riR));
            s.Add(WsDo(() => WsEnd(WsShower)));
            s.Add(WsWait(0.6));
            s.Add(WsDo(() =>
            {
                var ri2 = WsMask();
                string g = WsBandCheck(riR, ri1, ri2, 1, wsShowerR, 3, d => d < -0.1f, "G -0.1");
                string r = WsBandCheck(riR, ri1, ri2, 0, wsShowerR, 3, d => d < -0.05f, "R -0.05");
                string b = WsBandCheck(riR, ri1, ri2, 2, wsShowerR, 3, d => d > 0.1f, "B +0.1");
                // 流していない側の泡: 泡の区間の点のうち、水の区間から (水の半径 + 0.04 m) より遠い所
                float gap = (wsShowerR + 0.04f) / wsMts; int kept = 0, outside = 0; var why = new List<string>();
                for (int i = 0; i < riF.mesh.Count; i += Mathf.Max(1, riF.mesh.Count / 20))
                {
                    if (WsMinDist(riF.mesh[i], riR.mesh) <= gap) continue;
                    outside++;
                    float dm; float before = WsLocal(riF.mesh[i], riF.part[i], null, ri1, 1, out dm), after = WsLocal(riF.mesh[i], riF.part[i], null, ri2, 1, out dm);
                    if (before > 0.2f && after >= 0.7f * before) kept++; else why.Add(before.ToString("F2") + "->" + after.ToString("F2"));
                }
                WsExpect(outside >= 2, "foam points outside the rinse (" + outside + "; the flank path is too short?)");
                WsExpect(kept == outside, "foam outside the rinse stays (>= 70 %): " + kept + "/" + outside + " (" + string.Join(" ", why) + ")");
                WsNote("sponge 3 s along the whole flank path (" + riF.LengthM().ToString("F3") + " m, parts " + riF.Parts() + ", misses " + riF.misses + "), shower 1 s along its first third (" + riR.LengthM().ToString("F3") + " m, parts " + riR.Parts() + "): rinsed " + g + " / " + r + " / " + b + "; foam kept outside the rinse " + kept + "/" + outside);
            }));

            // ---- W-hold ----
            s = WsCase("W-hold", 80);
            WsAnchor hoA = default; double hoStart = 0, hoEnd = 0; bool hoSeen = false, hoActive = false, hoMoving = false; float hoSpeedEnd = 0f; Vector3 hoRest = Vector3.zero; float hoPath = 0f; Vector3 hoPrev = Vector3.zero;
            s.Add(WsDo(() => { WsPlaceShiba(); wsWash.SendCustomEvent("_DebugClear"); WsPlayerNear(4f); }));
            s.Add(WsUntil(() => { hoMoving = WsSpeed(0.3f) > 0.15f; return hoMoving; }, 8, null, true));
            s.Add(WsDo(() => { WsPlayerNear(4f); var h = WsPathHit(0.5f); hoA = WsAnchorOf(h); hoStart = EditorApplication.timeSinceStartup; }));
            s.Add(WsDrive(3f, 1, u =>
            {
                var h = WsAt(hoA);
                double t = EditorApplication.timeSinceStartup - hoStart;
                if (WsHold()) hoSeen |= t <= 1.5; else if (t > 1.5) throw new InvalidOperationException("_WashHold() false " + t.ToString("F2") + " s into the contact");
                if (t > 1.5) { hoActive = Get<bool>(wsWander, "_washActive"); if (!hoActive) throw new InvalidOperationException("the wash branch not active " + t.ToString("F2") + " s into the contact"); }
                return h;
            }, WsSponge, -1f, null));
            s.Add(WsDo(() =>
            {
                hoSpeedEnd = WsSpeed(0.5f);
                WsExpect(hoSeen, "_WashHold() true within 1.5 s of the first contact");
                WsExpect(hoSpeedEnd < 0.05f, "horizontal speed near 0 at the end of the 3 s contact (" + hoSpeedEnd.ToString("F3") + " m/s, _speed " + Get<float>(wsWander, "_speed").ToString("F3") + ")");
                WsEnd(WsSponge); hoEnd = EditorApplication.timeSinceStartup;
                WsPlayerFar(12f);
            }));
            s.Add(now => now - hoEnd >= wsHoldSeconds - 0.5);
            s.Add(WsDo(() => WsExpect(WsHold(), "_WashHold() still true " + (wsHoldSeconds - 0.5f) + " s after the last contact")));
            s.Add(now => now - hoEnd >= wsHoldSeconds + 1.0);
            s.Add(WsDo(() =>
            {
                WsExpect(!WsHold(), "_WashHold() false holdSeconds + 1 s after the last contact");
                WsExpect(!Get<bool>(wsWander, "_washActive"), "the wash branch released");
                hoRest = hoPrev = wsRoot.position;
            }));
            s.Add(WsUntil(() => { var p = wsRoot.position; hoPath += Flat(p - hoPrev); hoPrev = p; return hoPath >= 0.3f; }, 30,
                () => "the Shiba did not move 0.3 m within 30 s after the hold (path " + hoPath.ToString("F2") + ", restPhase " + Get<int>(wsWander, "_restPhase") + ", paused " + Get<bool>(wsWander, "_paused") + ")"));
            s.Add(WsDo(() => WsNote((hoMoving ? "moving before the contact" : "still before the contact (did not start moving within 8 s)") + "; hold true within 1.5 s, wash branch active, speed at the end " + hoSpeedEnd.ToString("F3") + " m/s; hold still true at +" + (wsHoldSeconds - 0.5f) + " s, false at +" + (wsHoldSeconds + 1f) + " s; walked " + hoPath.ToString("F2") + " m after (player moved 12 m away so the proximity pause does not hold the dog)")));

            // ---- W-break (経路の上の腰の点 → _EndContact → 胸の点。Collider が違っても、接触が切れていれば間はつながらない) ----
            s = WsCase("W-break", 30);
            WsAnchor brA = default, brB = default; float brKeep = 0f; var brMid = new List<WsAnchor>(); Color[] br0 = null; var brTa = new WsTrace(); var brTb = new WsTrace();
            s.Add(WsDo(() =>
            {
                WsPlaceShiba(); WsPlayerNear(1.3f);
                // 間隔を稼ぐため経路の両端 (u 0 / 1)。間の点は両端から r + 0.04 m、足りなければ r + 0.01 m より遠い点
                var ha = WsPathHit(0f); var hb = WsPathHit(1f);
                if (wsColPart[ha.ci] != 0) throw new WsSkip("the ray at the path start hit part " + wsColPart[ha.ci] + ", not the hip");
                float d = (ha.p - hb.p).magnitude;
                if (d < 0.15f) throw new WsSkip("hip and chest points only " + d.ToString("F3") + " m apart (< 0.15 m)");
                brA = WsAnchorOf(ha); brB = WsAnchorOf(hb);
                foreach (float margin in new[] { 0.04f, 0.01f })
                {
                    brMid.Clear(); brKeep = wsSpongeR + margin;
                    for (int k = 1; k < 20; k++)
                    {
                        var h = WsPath(k / 20f);
                        if (h.ci < 0 || (h.p - ha.p).magnitude <= brKeep || (h.p - hb.p).magnitude <= brKeep) continue;
                        brMid.Add(WsAnchorOf(h));
                    }
                    if (brMid.Count > 0) break;
                }
                if (brMid.Count == 0) throw new WsSkip("no path point lies more than r + 0.01 m from both contacts (" + d.ToString("F3") + " m apart, r " + wsSpongeR + " m)");
                wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => { br0 = WsMask(); }));
            s.Add(WsDrive(0.5f, 1, u => WsAt(brA), WsSponge, -1f, brTa));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsDrive(0.5f, 1, u => WsAt(brB), WsSponge, -1f, brTb));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                var br1 = WsMask();
                Vector3 a = brTa.mesh[0], b = brTb.mesh[0];
                float dm, ga = WsLocal(a, brTa.part[0], br0, br1, 1, out dm), gb = WsLocal(b, brTb.part[0], br0, br1, 1, out dm);
                WsExpect(ga > 0.1f && gb > 0.1f, "both contacts drew foam (A " + ga.ToString("F3") + ", B " + gb.ToString("F3") + ")");
                float keep = brKeep / wsMts, worst = 0f; int n = 0; var vals = new List<string>();
                foreach (var m in brMid)
                {
                    var h = WsAt(m); var mm = WsToMesh(h.ci, h.p);
                    if ((mm - a).magnitude <= keep || (mm - b).magnitude <= keep) continue;
                    float g = Mathf.Abs(WsLocal(mm, wsColPart[h.ci], br0, br1, 1, out dm)), w = Mathf.Abs(WsLocal(mm, wsColPart[h.ci], br0, br1, 2, out dm));
                    worst = Mathf.Max(worst, g, w); n++; vals.Add("p" + wsColPart[h.ci] + ":" + g.ToString("F3"));
                }
                if (n == 0) throw new WsSkip("no between point is more than " + brKeep.ToString("F2") + " m from both contacts on the mesh");
                WsExpect(worst < 0.01f, "the path between the two contacts did not change (max |dG|,|dB| " + worst.ToString("F4") + ": " + string.Join(" ", vals) + ")");
                WsNote("A part " + brTa.part[0] + " (" + wsCols[brTa.ci[0]].name + "), B part " + brTb.part[0] + " (" + wsCols[brTb.ci[0]].name + "), " + ((a - b).magnitude * wsMts).ToString("F3") + " m apart on the mesh (path ends u 0 / 1), _EndContact between (same frame), between points kept beyond " + brKeep.ToString("F2") + " m (r + " + (brKeep - wsSpongeR).ToString("F2") + ") of both; A dG " + ga.ToString("F3") + ", B dG " + gb.ToString("F3") + "; between " + string.Join(" ", vals) + " (max " + worst.ToString("F4") + ")");
            }));

            // ---- W-leg (前足 1 本のすね: 足は増え、反対の前足と胴へ貫通しない) ----
            s = WsCase("W-leg", 30);
            WsAnchor lgA = default; Color[] lg0 = null; var lgT = new WsTrace();
            s.Add(WsDo(() =>
            {
                WsPlaceShiba(); WsPlayerNear(1.3f);
                int leg = -1;
                for (int i = 0; i < wsCols.Length; i++) if (WsUsable(i) && wsColPart[i] == 3 && (leg < 0 || wsCols[i].bounds.center.y < wsCols[leg].bounds.center.y)) leg = i;
                if (leg < 0) throw new InvalidOperationException("no front-leg (part 3) collider");
                var c = wsCols[leg].bounds.center; var out_ = c - WsTorsoCenter(); out_.y = 0f;
                if (out_.sqrMagnitude < 1e-4f) out_ = wsRoot.right;
                lgA = WsAnchorOf(new WsHit { ci = leg, p = wsCols[leg].ClosestPoint(c + out_.normalized * 0.3f) });
                wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => { lg0 = WsMask(); }));
            s.Add(WsDrive(1f, 1, u => WsAt(lgA), WsSponge, -1f, lgT));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                var lg1 = WsMask(); var m = lgT.mesh[0]; float dm;
                float own = WsLocal(m, 3, lg0, lg1, 1, out dm);
                WsExpect(own > 0.1f, "the front leg's own texels gained foam (dG " + own.ToString("F3") + ", nearest " + dm.ToString("F3") + " m)");
                float farTorso =(wsSpongeR + 0.02f) / wsMts;
                float opp = 0f, torsoFar = 0f, torsoAll = 0f, oppNear = float.MaxValue, torsoNear = float.MaxValue; int nOpp = 0, nTorso = 0;
                for (int k = 0; k < wsTexCount; k++)
                {
                    int q = wsTexPart[k], i = wsTexIdx[k]; float dist = (wsTexPos[k] - m).magnitude, dg = lg1[i].g - lg0[i].g;
                    if (q == 4) { nOpp++; opp = Mathf.Max(opp, dg); oppNear = Mathf.Min(oppNear, dist); }
                    else if (q == 0 || q == 1)
                    {
                        torsoAll = Mathf.Max(torsoAll, dg); torsoNear = Mathf.Min(torsoNear, dist);
                        if (dist > farTorso) { nTorso++; torsoFar = Mathf.Max(torsoFar, dg); }
                    }
                }
                WsExpect(nOpp > 0 && opp < 0.02f, "the other front leg (part 4) did not gain foam (max dG " + opp.ToString("F3") + " over " + nOpp + " texels; nearest " + (oppNear * wsMts).ToString("F3") + " m in the bind pose)");
                WsExpect(torsoFar < 0.02f, "the torso beyond r + 0.02 m did not gain foam (max dG " + torsoFar.ToString("F3") + " over " + nTorso + " texels)");
                WsNote("shin collider " + wsCols[lgA.ci].name + " 1 s: own dG " + own.ToString("F3") + "; other front leg max dG " + opp.ToString("F3") + " (nearest texel " + (oppNear * wsMts).ToString("F3") + " m in the bind pose, r " + wsSpongeR + " m); torso beyond r + 0.02 m max dG " + torsoFar.ToString("F3") + ", torso overall max dG " + torsoAll.ToString("F3") + " (nearest torso texel " + (torsoNear * wsMts).ToString("F3") + " m; the chest is a neighbour part, so a torso texel within r may legitimately grow)");
            }));

            // ---- W-rate (同じ点を、毎フレームと 3 フレームに 1 回で。接触の合計はどちらも 0.6 s) ----
            s = WsCase("W-rate", 40);
            WsAnchor raA = default; Color[] ra0 = null, ra1 = null; var raT1 = new WsTrace(); var raT3 = new WsTrace(); float raG1 = 0f, raB1 = 0f;
            s.Add(WsDo(() => { WsPlaceShiba(); WsPlayerNear(1.3f); raA = WsAnchorOf(WsPathHit(0.5f)); wsWash.SendCustomEvent("_DebugClear"); }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => { ra0 = WsMask(); }));
            s.Add(WsDrive(0.6f, 1, u => WsAt(raA), WsSponge, -1f, raT1));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsWait(0.4));
            s.Add(WsDo(() =>
            {
                ra1 = WsMask(); float dm;
                raG1 = WsLocal(raT1.mesh[0], raT1.part[0], ra0, ra1, 1, out dm); raB1 = WsLocal(raT1.mesh[0], raT1.part[0], ra0, ra1, 2, out dm);
                wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => { ra0 = WsMask(); }));
            s.Add(WsDrive(0.6f, 3, u => WsAt(raA), WsSponge, -1f, raT3));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsWait(0.4));
            s.Add(WsDo(() =>
            {
                ra1 = WsMask(); float dm;
                float g3 = WsLocal(raT1.mesh[0], raT1.part[0], ra0, ra1, 1, out dm), b3 = WsLocal(raT1.mesh[0], raT1.part[0], ra0, ra1, 2, out dm);
                WsExpect(raT1.maxSec <= 0.25f && raT3.maxSec <= 0.25f, "no call above the 0.25 s clamp (max " + raT1.maxSec.ToString("F3") + " / " + raT3.maxSec.ToString("F3") + " s: a stretched frame; rerun alone)");
                WsExpect(raG1 > 0.1f && g3 > 0.1f, "both runs drew foam (" + raG1.ToString("F3") + " / " + g3.ToString("F3") + ")");
                float rel = Mathf.Abs(raG1 - g3) / Mathf.Max(raG1, g3);
                WsExpect(rel <= 0.15f, "G at the same point within 15 % (every frame " + raG1.ToString("F3") + ", every 3rd " + g3.ToString("F3") + ", " + rel.ToString("P1") + ")");
                WsNote("0.6 s of sponge at one point: every frame " + raT1.calls + " calls / " + raT1.sec.ToString("F3") + " s -> dG " + raG1.ToString("F3") + " dB " + raB1.ToString("F3") + "; every 3rd frame " + raT3.calls + " calls / " + raT3.sec.ToString("F3") + " s -> dG " + g3.ToString("F3") + " dB " + b3.ToString("F3") + " (diff " + rel.ToString("P1") + "). 0.6 s, not 2 s: 2 s saturates G at 1 and hides a rate difference");
            }));

            // ---- W-clean ----
            // 半径 0.11 m = 実際のシャワーの範囲 (距離 1 m で 0.11、1.5 m で 0.13 程度)。かかったゲーム内の秒を出す (1 匹 1〜2 分の目標の目安)
            s = WsCase("W-clean", 130);
            const float ClRadius = 0.11f;
            int clS0 = 0; List<WsAnchor> clPts = null; float clThr = 0f, clT0 = 0f, clSec = 0f;
            s.Add(WsDo(() => { WsPlaceShiba(); WsPlayerNear(1.5f); WsSetDirt(0.6f); clThr = Get<float>(wsWash, "cleanDirtThreshold"); }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() =>
            {
                clS0 = WsSerial();
                WsExpect(Get<bool>(wsWash, "cleanArmed"), "armed after _DebugSetDirt(0.6)");
                clPts = WsCover(0.08f);
                clT0 = Time.time;
            }));
            s.Add(WsSweep(() => clPts, WsShower, ClRadius, 1.0f, 90f, () => { bool ok = WsSerial() == clS0 + 1 && WsAvgDirt() < clThr; if (ok) clSec = Time.time - clT0; return ok; },
                () => "not clean in 90 s: avg dirt " + WsAvgDirt().ToString("F3") + " (< " + clThr + "), avg foam " + WsAvgFoam().ToString("F3") + ", serial " + clS0 + " -> " + WsSerial() + ", " + clPts.Count + " points"));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                WsExpect(WsSerial() == clS0 + 1, "_CleanSerial() +1 exactly (" + clS0 + " -> " + WsSerial() + ")");
                WsNote("dirt 0.6 -> clean in " + clSec.ToString("F1") + " s of game time (shower radius " + ClRadius + " m = the real tool's range, 1 s per point, one contact at a time) over " + clPts.Count + " surface points of " + wsCols.Count(c => c != null) + " colliders: avg dirt " + WsAvgDirt().ToString("F3") + ", serial " + clS0 + " -> " + WsSerial());
            }));

            // ---- W-reclean (きれいな状態から: 泡を付けただけでは増えず、流して +1) ----
            s = WsCase("W-reclean", 150);
            int rcS0 = 0; List<WsAnchor> rcFoam = null, rcRinse = null; float rcDirtyFoam = 0f, rcCleanFoam = 0f;
            s.Add(WsDo(() => { WsPlaceShiba(); WsPlayerNear(1.5f); wsWash.SendCustomEvent("_DebugClear"); rcDirtyFoam = Get<float>(wsWash, "dirtyFoamThreshold"); rcCleanFoam = Get<float>(wsWash, "cleanFoamThreshold"); }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() =>
            {
                rcS0 = WsSerial();
                WsExpect(!Get<bool>(wsWash, "cleanArmed"), "not armed after _DebugClear");
                rcFoam = WsCover(0.1f); rcRinse = WsCover(0.12f);
            }));
            s.Add(WsSweep(() => rcFoam, WsSponge, -1f, 0.6f, 60f, () => WsAvgFoam() >= rcDirtyFoam && Get<bool>(wsWash, "cleanArmed"),
                () => "foam did not arm in 60 s: avg foam " + WsAvgFoam().ToString("F3") + " (>= " + rcDirtyFoam + "), armed " + Get<bool>(wsWash, "cleanArmed")));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() => WsExpect(WsSerial() == rcS0, "foam alone does not count (" + rcS0 + " -> " + WsSerial() + ")")));
            s.Add(WsSweep(() => rcRinse, WsShower, 0.2f, 0.5f, 60f, () => WsSerial() == rcS0 + 1,
                () => "not clean after the rinse in 60 s: avg foam " + WsAvgFoam().ToString("F3") + ", avg dirt " + WsAvgDirt().ToString("F3") + ", serial " + rcS0 + " -> " + WsSerial()));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                WsExpect(WsSerial() == rcS0 + 1, "_CleanSerial() +1 exactly (" + rcS0 + " -> " + WsSerial() + ")");
                WsExpect(WsAvgFoam() < rcCleanFoam, "avg foam below the clean threshold (" + WsAvgFoam().ToString("F3") + ")");
                WsNote("from clean: sponge over " + rcFoam.Count + " points armed the wash (avg foam >= " + rcDirtyFoam + ") without a clean edge; shower over " + rcRinse.Count + " points -> serial " + rcS0 + " -> " + WsSerial());
            }));

            // ---- W-reject (範囲外の NetStroke: 例外なし・マスクとセルが変わらない。最後に正しい 1 本で経路を確かめる) ----
            s = WsCase("W-reject", 40);
            Color[] rj0 = null; float[] rjD = null, rjF = null, rjW = null;
            const int Center = 511 | (511 << 10) | (511 << 20), Amount = 300 | (90 << 12);
            var bad = new List<KeyValuePair<string, object[]>>
            {
                new KeyValuePair<string, object[]>("tool 9", new object[] { 9, 1, Center, Center, Amount }),
                new KeyValuePair<string, object[]>("part -1", new object[] { 0, -1, Center, Center, Amount }),
                new KeyValuePair<string, object[]>("part 99", new object[] { 0, 99, Center, Center, Amount }),
                new KeyValuePair<string, object[]>("amount -5", new object[] { 0, 1, Center, Center, -5 }),
                new KeyValuePair<string, object[]>("amount ms 0", new object[] { 0, 1, Center, Center, 90 << 12 }),
                new KeyValuePair<string, object[]>("amount radius 10 mm", new object[] { 0, 1, Center, Center, 300 | (10 << 12) }),
                new KeyValuePair<string, object[]>("amount >= 2^20", new object[] { 0, 1, Center, Center, 1 << 20 }),
                new KeyValuePair<string, object[]>("from 2^30", new object[] { 0, 1, 1 << 30, Center, Amount }),
                new KeyValuePair<string, object[]>("to -1", new object[] { 0, 1, Center, -1, Amount }),
            };
            s.Add(WsDo(() => { WsPlayerNear(2f); WsSetDirt(0.6f); }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => { rj0 = WsMask(); rjD = (float[])Get<float[]>(wsWash, "_dirt").Clone(); rjF = (float[])Get<float[]>(wsWash, "_foam").Clone(); rjW = (float[])Get<float[]>(wsWash, "_wet").Clone(); }));
            foreach (var kv in bad)
            {
                var b = kv;
                s.Add(WsDo(() =>
                {
                    var a = b.Value; int seq = Get<int>(wsWash, "_seq"), ep = Get<int>(wsWash, "epoch");
                    wsWash.SendCustomNetworkEvent(NetworkEventTarget.All, "NetStroke", a[0], a[1], a[2], a[3], a[4], seq, ep);
                }));
                s.Add(WsWait(0.25));   // under the 8 / s rate limit
            }
            s.Add(WsDo(() =>
            {
                int seq = Get<int>(wsWash, "_seq"), ep = Get<int>(wsWash, "epoch");
                wsWash.SendCustomNetworkEvent(NetworkEventTarget.All, "NetStroke", -1, 1, Center, Center, Amount, seq, ep);   // tool -1
            }));
            s.Add(WsWait(0.25));
            s.Add(WsDo(() => wsWash.SendCustomNetworkEvent(NetworkEventTarget.All, "NetStroke", 0, 1, Center, Center, Amount, -1, Get<int>(wsWash, "epoch"))));   // seq -1
            s.Add(WsWait(0.6));
            s.Add(WsDo(() =>
            {
                var m = WsMask(); float md = WsMaxDiff(rj0, m);
                int cd = WsCellDiff(rjD, rjF, rjW);
                WsExpect(md < 1e-3f, "mask unchanged by the rejected strokes (max diff " + md.ToString("F4") + ")");
                WsExpect(cd == 0, "cells unchanged by the rejected strokes (" + cd + " cells changed)");
                rj0 = m; rjD = (float[])Get<float[]>(wsWash, "_dirt").Clone(); rjF = (float[])Get<float[]>(wsWash, "_foam").Clone(); rjW = (float[])Get<float[]>(wsWash, "_wet").Clone();
                // 対照: 正しい 1 本 (自分が送り主 = Owner がセルへ積算し、自分の送った分はマスクへ描き直さない)
                int seq = Get<int>(wsWash, "_seq"), ep = Get<int>(wsWash, "epoch");
                wsWash.SendCustomNetworkEvent(NetworkEventTarget.All, "NetStroke", WsSponge, 1, Center, Center, Amount, seq, ep);
            }));
            s.Add(WsWait(0.6));
            s.Add(WsDo(() =>
            {
                var m = WsMask(); float md = WsMaxDiff(rj0, m);
                int cd = WsCellDiff(rjD, rjF, rjW);
                WsExpect(cd > 0, "control: a valid NetStroke reached the cells (0 cells changed: ClientSim did not deliver the parameterised event to KH_DogWash, or the sender check refused it)");
                WsExpect(md < 1e-3f, "control: the sender's own echo is not drawn again (mask max diff " + md.ToString("F4") + ")");
                WsNote(bad.Count + 2 + " out-of-range NetStroke calls (" + string.Join(", ", bad.Select(x => x.Key)) + ", tool -1, seq -1) through SendCustomNetworkEvent(All): no exception, mask and cells unchanged; control stroke changed " + cd + " cells and not the mask");
            }));

            // ---- W-others ----
            s = WsCase("W-others", 90);
            var others = Enumerable.Range(0, wander.Length).Where(i => i != wsDog).ToArray();
            var otPath = new float[wander.Length]; var otPrev = new Vector3[wander.Length];
            s.Add(WsDo(() =>
            {
                foreach (int o in others)
                {
                    var v = WsRef(wander[o], "wash");   // 記号が無ければ例外
                    WsExpect(v == null || (v as UnityEngine.Object) == null, "dog " + o + " wash is null (" + v + ")");
                }
                WsPlaceShiba(); WsPlayerNear(1.3f); WsSetDirt(0.6f);
                foreach (int o in others) otPrev[o] = roots[o].position;
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDrive(2f, 1, u =>
            {
                foreach (int o in others) if (Get<bool>(wander[o], "_washActive")) throw new InvalidOperationException("dog " + o + " entered the wash branch while the Shiba was washed");
                return WsPath(0.4f + 0.2f * u);
            }, WsSponge, -1f, null));
            s.Add(WsDo(() =>
            {
                WsEnd(WsSponge);
                WsExpect(Get<bool>(wsWander, "_washActive") || WsHold(), "control: the Shiba itself was held");
                WsPlayerFar(12f);
            }));
            s.Add(WsUntil(() =>
            {
                foreach (int o in others) { var p = roots[o].position; otPath[o] += Flat(p - otPrev[o]); otPrev[o] = p; if (Get<bool>(wander[o], "_washActive")) throw new InvalidOperationException("dog " + o + " wash branch active"); }
                return others.All(o => otPath[o] >= 0.3f);
            }, 60, () => "not every other dog moved 0.3 m in 60 s: " + string.Join(", ", others.Select(o => o + ":" + otPath[o].ToString("F2") + "m rest" + Get<int>(wander[o], "_restPhase")))));
            s.Add(WsDo(() => WsNote("other dogs " + string.Join(",", others) + ": wash null, never in the wash branch while the Shiba was washed, each walked >= 0.3 m (" + string.Join(", ", others.Select(o => otPath[o].ToString("F1"))) + " m)")));

            MakeWashBehaviorCases(); // IMP-DOG-027 (WashBehavior.cs): leashed stop, the gates, gathering
            MakeWashSyncCases();     // IMP-DOG-026 (WashSync.cs): decay, local arm, late join / reset / handover (simulated), budget. Last: they reset the drying values

            WsWrite();
        }

        static void WsNotCovered()
        {
            var n = wsReport.notCovered;
            n.Add("Real network: other clients drawing a received NetStroke, the 8 Hz send and the per-sender budget between clients, the Owner integrating someone else's strokes, two washers at once -> PCVR with 2 people");
            n.Add("Late joiner / epoch repaint and Owner handover with real clients (W-latejoin / W-reset / W-handover call the events directly) -> PCVR");
            n.Add("The tool: VRC_Pickup hold, shower centre + ring raycasts and radius by distance, VR sponge sphere, Desktop gaze + Use, haptics, spray particles / audio and KH_WashToolState sync. The harness calls _LocalContact with collider surface points instead");
            n.Add("Visuals of the shell (foam layers, dirt pattern, wet darkening, face foam limit), splash / foam puff particles, contact audio -> Play / PCVR");
            n.Add("The dog's head gaze and tilt, the body turn toward the washer (only _washActive, _WashHold and the horizontal speed are read)");
            n.Add("Priority over washing by water / sleep call (design 1-5); the leashed stop with a stroke from someone other than the holder (ClientSim has one player)");
            n.Add("Drying (foamDrySeconds / wetDrySeconds, 90-120 s) and the shell switching off after drying; RenderTexture loss recovery");
            n.Add("Quest / iOS rendering of the mask and the shell");
        }

        // ============================================================
        // 実行
        // ============================================================

        static List<Func<double, bool>> WsCase(string name, double limit)
        {
            report.cases.Add(new Case { name = name });
            wsReport.cases.Add(new WsCaseResult { name = name });
            wsLimits.Add(limit);
            var s = new List<Func<double, bool>>(); wsScripts.Add(s); return s;
        }

        static void WashTick(double now)
        {
            if (wsScripts == null || caseIndex < 0 || caseIndex >= wsScripts.Count) throw new InvalidOperationException("Wash script lost (domain reload?)");
            if (wsStep == 0 && wsCaseAt != Current.startedAt) { wsCaseAt = Current.startedAt; wsStepAt = now; wsMaxDt = 0f; }
            wsMaxDt = Mathf.Max(wsMaxDt, Time.deltaTime);
            if (wsRoot != null)
            {
                wsPosHist.Add(new KeyValuePair<float, Vector3>(Time.time, wsRoot.position));
                while (wsPosHist.Count > 0 && Time.time - wsPosHist[0].Key > 1.5f) wsPosHist.RemoveAt(0);
            }
            string status;
            try
            {
                if (now - Current.startedAt > wsLimits[caseIndex]) throw new TimeoutException("Case deadline (" + wsLimits[caseIndex] + " s)");
                var steps = wsScripts[caseIndex];
                while (wsStep < steps.Count)
                {
                    if (!steps[wsStep](now)) return;
                    wsStep++; wsStepAt = now;
                }
                status = "PASS";
            }
            catch (WsSkip e)
            {
                status = "SKIP";
                wsReport.cases[caseIndex].notes.Add("SKIP (N/A, not counted): " + e.Message);
                WsRecover();
            }
            catch (Exception e)
            {
                status = "FAIL"; wsFailed = true;
                wsReport.cases[caseIndex].notes.Add("FAIL at step " + wsStep + ": " + e.GetType().Name + ": " + e.Message);
                wsReport.cases[caseIndex].notes.Add("at: " + TvWhere(e));
                WsRecover();
            }
            // 伸びたフレーム (VRChat クライアントの並行・重い処理の並行) は偽の失敗の元。落ちた回は単独で回し直す (kh-frisbee-harness.md)
            wsReport.cases[caseIndex].notes.Add("max frame dt " + (wsMaxDt * 1000f).ToString("F0") + " ms" + (wsMaxDt > 0.1f ? " (stretched frames: rerun alone before reading a failure as a regression)" : ""));
            Current.status = status; Current.endedAt = now; Current.reason = string.Join(" | ", wsReport.cases[caseIndex].notes);
            wsReport.cases[caseIndex].status = status;
            wsStep = 0;
            WsWrite(); Save();
            if (caseIndex + 1 < report.cases.Count) { Next(); return; }
            End(wsFailed ? "WASH_FAIL" : "WASH_PASS", wsFailed ? "One or more wash cases failed; see wash.json" : "Local ClientSim wash cases passed (SKIP = N/A). Local draw and Owner cell integration only: not network, visuals, device or PCVR acceptance.");
        }

        static void WsRecover()
        {
            for (int t = 0; t < 3; t++) { try { WsEnd(t); } catch (Exception) { } }
        }

        static void WsWrite()
        {
            if (report == null || wsReport == null) return;
            Directory.CreateDirectory(Dir + "/" + report.runId);
            File.WriteAllText(Dir + "/" + report.runId + "/wash.json", JsonUtility.ToJson(wsReport, true));
        }

        // ---- 手順の部品 (CmDo 等と同じ形。時刻は wsStepAt から) ----
        static Func<double, bool> WsDo(Action a) { return now => { a(); return true; }; }
        static Func<double, bool> WsWait(double seconds) { return now => now - wsStepAt >= seconds; }
        /// <summary>条件を毎フレーム見る。optional なら時間切れで失敗せずに進む。</summary>
        static Func<double, bool> WsUntil(Func<bool> condition, double limit, Func<string> what, bool optional = false)
        {
            return now =>
            {
                if (condition()) return true;
                if (now - wsStepAt > limit) { if (optional) return true; throw new TimeoutException(what() + " (" + limit + " s)"); }
                return false;
            };
        }
        static void WsExpect(bool ok, string what) { if (!ok) throw new InvalidOperationException("Expected: " + what); }
        static void WsNote(string text) { wsReport.cases[caseIndex].notes.Add(text); }

        /// <summary>duration 秒 (ゲーム時刻) のあいだ、every フレームに 1 回 _LocalContact。seconds は前の呼び出しからの実際の経過時間で、
        /// 最後の呼び出しは duration ちょうどまでの残りを渡す (合計の接触時間が呼び出しの間隔によらず duration になる)。u = 経過 / duration。</summary>
        static Func<double, bool> WsDrive(float duration, int every, Func<float, WsHit> at, int tool, float radius, WsTrace tr)
        {
            float t0 = -1f, last = 0f; int frame = 0;
            return now =>
            {
                float t = Time.time;
                if (t0 < 0f) { t0 = t; last = t - Mathf.Max(Time.deltaTime, 1e-3f); frame = 0; }
                bool final = t - t0 >= duration;
                if (!final && frame++ % every != 0) return false;
                float end = final ? t0 + duration : t, sec = end - last;
                if (sec > 1e-5f)
                {
                    var h = at(Mathf.Clamp01((end - t0) / duration));
                    last = end;
                    if (h.ci < 0)
                    {
                        // 当たらないフレーム: 道具と同じく接触を切る (その時間は洗わない)
                        WsEnd(tool);
                        if (tr != null) tr.misses++;
                        return final;
                    }
                    WsContact(tool, h.ci, h.p, sec, radius);
                    if (tr != null)
                    {
                        tr.mesh.Add(WsToMesh(h.ci, h.p)); tr.part.Add(wsColPart[h.ci]); tr.ci.Add(h.ci); tr.u.Add((end - t0) / duration);
                        tr.sec += sec; tr.maxSec = Mathf.Max(tr.maxSec, sec); tr.calls++;
                    }
                }
                return final;
            };
        }

        /// <summary>表面の点を順に、1 点あたり dwell 秒 (点が尽きたら最初から)。0.5 s ごとに done() を見て、満たしたら接触を終えて進む。</summary>
        static Func<double, bool> WsSweep(Func<List<WsAnchor>> points, int tool, float radius, float dwell, float limit, Func<bool> done, Func<string> why)
        {
            float t0 = -1f, last = 0f, onPoint = 0f; int idx = 0; double checkAt = 0;
            return now =>
            {
                float t = Time.time;
                if (t0 < 0f) { t0 = t; last = t - Mathf.Max(Time.deltaTime, 1e-3f); }
                if (now >= checkAt) { checkAt = now + 0.5; if (done()) { WsEnd(tool); return true; } }
                if (t - t0 > limit) throw new TimeoutException(why());
                var pts = points();
                if (pts == null || pts.Count == 0) throw new InvalidOperationException("no surface points");
                float sec = t - last; last = t;
                if (sec <= 0f) return false;
                var a = pts[idx % pts.Count];
                WsContact(tool, a.ci, WsAt(a).p, sec, radius);
                onPoint += sec;
                if (onPoint >= dwell) { onPoint = 0f; idx++; }
                return false;
            };
        }

        // ============================================================
        // 配線の読み取り
        // ============================================================

        static void WsBind()
        {
            wsWash = null; wsWander = null; wsDog = -1; wsRoot = null;
            for (int i = 0; i < wander.Length; i++)
            {
                var u = WsRef(wander[i], "wash") as UdonBehaviour;
                if (u == null) continue;
                if (wsWash != null && wsDog != i) throw new InvalidOperationException("more than one dog has a wash (" + wsDog + ", " + i + "); the prototype expects the Shiba only");
                wsWash = u; wsDog = i; wsWander = wander[i];
            }
            if (wsWash == null) throw new InvalidOperationException("no dog has KD_DogWanderController.wash wired (run Wash/11)");
            wsRoot = roots[wsDog];
            WsExpect(Get<int>(wsWash, "dogIndex") == Get<int>(wsWander, "dogIndex"), "wash.dogIndex = the dog's index");
            WsExpect(Get<Transform>(wsWash, "dogRoot") == wsRoot, "wash.dogRoot = the dog root");
            wsCols = WsArr<Collider>(wsWash, "partColliders"); wsBones = WsArr<Transform>(wsWash, "partBones");
            wsColPart = WsArr<int>(wsWash, "partOfCollider"); wsB2M = WsArr<Matrix4x4>(wsWash, "boneToMesh");
            wsNeighbors = WsArr<int>(wsWash, "partNeighbors"); wsBoneToPart = WsArr<float>(wsWash, "boneToPart");
            if (wsCols.Length == 0 || wsBones.Length < wsCols.Length || wsColPart.Length < wsCols.Length || wsB2M.Length < wsCols.Length) throw new InvalidOperationException("wash collider arrays are not wired");
            wsMts = Get<float>(wsWash, "meshToWorldScale"); if (!(wsMts > 0f)) wsMts = 1f;
            wsSpongeR = Get<float>(wsWash, "spongeRadius"); wsShowerR = Get<float>(wsWash, "showerRadius"); wsHoldSeconds = Get<float>(wsWash, "holdSeconds");
            object sh; wsShell = wsWash.TryGetProgramVariable("shellRenderer", out sh) ? sh as Renderer : null;
            if (wsShell == null) throw new InvalidOperationException("wash.shellRenderer is not wired");
            if (WsTorso().Count == 0) throw new InvalidOperationException("no torso (part 0 / 1) collider");
            wsLayer = WsLoadLayer();
            wsReport.resolved.Add("washLayer mask " + wsLayer);
            WsLoadPosMap(Get<Texture>(wsWash, "posMap"));
            WsResolve();
            wsReport.dog = wsDog; wsReport.dogName = wsRoot.name; wsReport.meshToWorldScale = wsMts; wsReport.colliders = wsCols.Count(c => c != null); wsReport.validTexels = wsTexCount;
        }

        /// <summary>参照の欄を読む。Play 中の TryGetProgramVariable は、記号があっても値が null なら false を返す
        /// (ヒープの TryGetHeapVariable が null を「無い」と返す。Wash/12 の Backing も「無い = null」と読む)。
        /// なので記号の有無はプログラムの記号表で確かめ、false は null として返す。記号が無ければ例外。</summary>
        static object WsRef(UdonBehaviour u, string key)
        {
            if (u == null) throw new InvalidOperationException("no UdonBehaviour for " + key);
            var prog = u.programSource != null && u.programSource.SerializedProgramAsset != null ? u.programSource.SerializedProgramAsset.RetrieveProgram() : null;
            if (prog == null) throw new InvalidOperationException(u.name + ": program not retrievable (reading " + key + ")");
            uint addr;
            if (!prog.SymbolTable.TryGetAddressFromSymbol(key, out addr)) throw new InvalidOperationException(u.name + " (" + u.programSource.name + ") has no symbol " + key + " (old program?)");
            object o;
            return u.TryGetProgramVariable(key, out o) ? o : null;
        }

        static T[] WsArr<T>(UdonBehaviour u, string key)
        {
            object o;
            if (u == null || !u.TryGetProgramVariable(key, out o) || !(o is Array a)) throw new InvalidOperationException("Missing runtime array " + key);
            var r = new T[a.Length];
            for (int i = 0; i < a.Length; i++) { var v = a.GetValue(i); if (v is T t) r[i] = t; }
            return r;
        }

        /// <summary>位置マップ (RGB = バインドポーズのメッシュ座標、A = 骨 + 1、無効 0) の有効なテクセルと、その部位 (boneToPart)。</summary>
        static void WsLoadPosMap(Texture tex)
        {
            if (tex == null) throw new InvalidOperationException("wash.posMap is not wired");
            Color[] px; int w = tex.width, h = tex.height;
            var t2 = tex as Texture2D;
            if (t2 != null && t2.isReadable) px = t2.GetPixels();
            else
            {
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                try { Graphics.Blit(tex, rt); px = WsRead(rt, TextureFormat.RGBAFloat); }
                finally { RenderTexture.ReleaseTemporary(rt); }
            }
            var idx = new List<int>(); var part = new List<int>(); var pos = new List<Vector3>();
            for (int i = 0; i < px.Length; i++)
            {
                int bone = Mathf.RoundToInt(px[i].a) - 1;
                if (bone < 0 || bone >= wsBoneToPart.Length) continue;
                int p = Mathf.RoundToInt(wsBoneToPart[bone]);
                if (p < 0 || p >= KH_DogWash.PartCount) continue;
                idx.Add(i); part.Add(p); pos.Add(new Vector3(px[i].r, px[i].g, px[i].b));
            }
            wsTexIdx = idx.ToArray(); wsTexPart = part.ToArray(); wsTexPos = pos.ToArray(); wsTexCount = wsTexIdx.Length;
            if (wsTexCount < 1000) throw new InvalidOperationException("position map has only " + wsTexCount + " valid texels");
            var mask = Get<RenderTexture>(wsWash, "maskA");
            if (mask == null || mask.width != w || mask.height != h) throw new InvalidOperationException("mask size " + (mask == null ? "null" : mask.width + "x" + mask.height) + " != position map " + w + "x" + h);
        }

        // ============================================================
        // U# の呼び出し (引数のあるメソッドは名前と引数の記号が付け替えられる)
        // ============================================================

        // U# の付け替えは __{番号}_{名前} (番号は同じ名前の出現順、0 から)。記号表を列挙せず (ImmutableArray を持ち込まない)、
        // 番号 0..WsMaxId の候補を記号表 / 入口の表へ問い合わせる。
        const int WsMaxId = 200;
        static IUdonSymbolTable wsSymTable, wsEntryTable;

        static void WsResolve()
        {
            var prog = wsWash.programSource.SerializedProgramAsset.RetrieveProgram();
            if (prog == null) throw new InvalidOperationException("KH_DogWash program not retrievable");
            wsSymTable = prog.SymbolTable; wsEntryTable = prog.EntryPoints;
            foreach (var m in new[] { "_LocalContact", "_EndContact", "_DebugSetDirt", "_DebugClear" }) wsReport.resolved.Add(m + " -> " + WsEntry(m));
            foreach (var p in new[] { "tool", "colliderIndex", "worldPoint", "seconds", "radiusMeters", "value" }) wsReport.resolved.Add(p + " -> " + string.Join(",", WsParam(p)));
            foreach (var r in new[] { "_WashHold", "_CleanSerial", "_LastStrokePart", "_AverageDirt", "_AverageFoam" }) wsReport.resolved.Add(r + " -> " + WsEntry(r) + " / " + WsRetSym(r));
        }
        static bool WsHas(IUdonSymbolTable t, string name) { uint a; return t.TryGetAddressFromSymbol(name, out a); }
        static List<string> WsMangled(IUdonSymbolTable t, string id)
        {
            var r = new List<string>();
            for (int n = 0; n <= WsMaxId; n++) if (WsHas(t, "__" + n + "_" + id)) r.Add("__" + n + "_" + id);
            return r;
        }

        static string WsEntry(string method)
        {
            string e;
            if (wsEntryCache.TryGetValue(method, out e)) return e;
            if (WsHas(wsEntryTable, method)) e = method;   // 引数の無いメソッドは付け替えない
            else
            {
                var m = WsMangled(wsEntryTable, method);
                if (m.Count != 1) throw new InvalidOperationException("KH_DogWash entry point " + method + ": " + m.Count + " matches (" + string.Join(",", m) + ")");
                e = m[0];
            }
            wsEntryCache[method] = e; return e;
        }
        static string[] WsParam(string name)
        {
            string[] r;
            if (wsParamCache.TryGetValue(name, out r)) return r;
            r = WsMangled(wsSymTable, name + "__param").ToArray();
            if (r.Length == 0) throw new InvalidOperationException("KH_DogWash has no parameter symbol for " + name);
            wsParamCache[name] = r; return r;
        }
        static string WsRetSym(string method)
        {
            string key = method + "#ret", e;
            if (wsEntryCache.TryGetValue(key, out e)) return e;
            var m = WsMangled(wsSymTable, method + "__ret");
            // 引数のあるメソッドは付け替えた名前に番号が付く: __{n}_ + __{k}_{名前} + __ret (例 __0___0__DebugAcceptSender__ret、uasm で確認)
            for (int k = 0; m.Count == 0 && k <= 5; k++) m = WsMangled(wsSymTable, "__" + k + "_" + method + "__ret");
            if (m.Count != 1) throw new InvalidOperationException("KH_DogWash return symbol of " + method + ": " + m.Count + " matches (" + string.Join(",", m) + ")");
            wsEntryCache[key] = m[0]; return m[0];
        }
        /// <summary>同じ名前の引数の候補すべてへ同じ値を型付きで書いてから呼ぶ (U# の呼び出しは毎回自分の引数を書くので他を壊さない)。</summary>
        static void WsCall(string method, params object[] nameValue)
        {
            for (int i = 0; i + 1 < nameValue.Length; i += 2)
                foreach (var sym in WsParam((string)nameValue[i]))
                {
                    var v = nameValue[i + 1];
                    if (v is int iv) wsWash.SetProgramVariable<int>(sym, iv);
                    else if (v is float fv) wsWash.SetProgramVariable<float>(sym, fv);
                    else if (v is Vector3 vv) wsWash.SetProgramVariable<Vector3>(sym, vv);
                    else throw new InvalidOperationException("unsupported argument type " + v);
                }
            wsWash.SendCustomEvent(WsEntry(method));
        }
        static T WsRet<T>(string method)
        {
            wsWash.SendCustomEvent(WsEntry(method));
            object o;
            if (!wsWash.TryGetProgramVariable(WsRetSym(method), out o) || !(o is T)) throw new InvalidOperationException("return of " + method + " unreadable (" + o + ")");
            return (T)o;
        }

        static void WsSetDirt(float v)
        {
            WsCall("_DebugSetDirt", "value", v);
            var d = Get<float[]>(wsWash, "_dirt"); var valid = Get<bool[]>(wsWash, "cellValid");
            for (int c = 0; c < d.Length && c < valid.Length; c++) if (valid[c]) { WsExpect(Mathf.Abs(d[c] - v) < 1e-4f, "_DebugSetDirt(" + v + ") reached the cells (cell " + c + " = " + d[c] + ")"); break; }
        }
        static void WsEnd(int tool) { WsCall("_EndContact", "tool", tool); }
        static void WsContact(int tool, int ci, Vector3 p, float sec, float radius)
        {
            WsCall("_LocalContact", "tool", tool, "colliderIndex", ci, "worldPoint", p, "seconds", sec, "radiusMeters", radius);
            if (wsProbed) return;
            // 最初の 1 回: 呼び出しが届いたか (接触の履歴 = この Collider、この点のメッシュ座標)
            var hv = Get<bool[]>(wsWash, "_hValid"); var hc = Get<int[]>(wsWash, "_hCollider"); var hm = Get<Vector3[]>(wsWash, "_hMesh");
            var want = WsToMesh(ci, p);
            WsExpect(hv[tool] && hc[tool] == ci && (hm[tool] - want).magnitude < 1e-4f, "_LocalContact reached KH_DogWash (history valid " + hv[tool] + ", collider " + hc[tool] + "/" + ci + ", mesh " + hm[tool].ToString("F4") + " vs " + want.ToString("F4") + ")");
            wsProbed = true;
        }
        static bool WsHold() { return WsRet<bool>("_WashHold"); }
        static int WsSerial() { return WsRet<int>("_CleanSerial"); }
        static int WsLastPart() { return WsRet<int>("_LastStrokePart"); }
        static float WsAvgDirt() { return WsRet<float>("_AverageDirt"); }
        static float WsAvgFoam() { return WsRet<float>("_AverageFoam"); }

        // ============================================================
        // 場所・接触の点
        // ============================================================

        // Flat (水平の長さ) は Water.cs のもの
        static Vector3 WsFlat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward; }

        static void WsDetachShiba()
        {
            if (Get<int[]>(manager, "walkers")[wsDog] >= 0) pouch.SendCustomEvent("_WalkOff" + wsDog);
        }
        /// <summary>柴を門の道の横 (平らで川から遠い) へ置く。接触のケースの最初だけ (歩行ハーネスの置き方と同じ注入)。</summary>
        static void WsPlaceShiba()
        {
            WsDetachShiba();
            Vector3 dir = nodes[1] - nodes[0]; dir.y = 0f;
            var rot = Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward);
            var p = CmGround(nodes[0] + rot * new Vector3(2.5f, 0f, 0f));
            wsRoot.SetPositionAndRotation(p, rot);
            var sync = wsRoot.GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
            wsPosHist.Clear();
        }
        /// <summary>柴の右 dist m に、柴の方を向いて立つ (NetStroke は送り主が 8 m 以内のときだけ受ける)。</summary>
        static void WsPlayerNear(float dist)
        {
            var d = wsRoot.position; var p = CmGround(d + WsFlat(wsRoot.right) * dist);
            CmTeleport(p, d - p);
        }
        /// <summary>柴から dist m 前後の周回のノードへ (近接の停止 pauseRadius で柴が止まらないように)。</summary>
        static void WsPlayerFar(float dist)
        {
            var d = wsRoot.position; int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++) { float x = Mathf.Abs(Flat(nodes[i] - d) - dist); if (x < bd) { bd = x; best = i; } }
            CmTeleport(nodes[best], d - nodes[best]);
        }
        static float WsSpeed(float window)
        {
            if (wsPosHist.Count < 2) return 0f;
            var last = wsPosHist[wsPosHist.Count - 1]; var first = last;
            for (int i = wsPosHist.Count - 1; i >= 0; i--) { if (last.Key - wsPosHist[i].Key > window) break; first = wsPosHist[i]; }
            float dt = last.Key - first.Key;
            return dt > 1e-3f ? Flat(last.Value - first.Value) / dt : 0f;
        }

        static bool WsUsable(int i) { return i >= 0 && i < wsCols.Length && wsCols[i] != null && wsBones[i] != null && wsColPart[i] >= 0 && wsColPart[i] < KH_DogWash.PartCount; }
        static List<int> WsTorso() { var r = new List<int>(); for (int i = 0; i < wsCols.Length; i++) if (WsUsable(i) && (wsColPart[i] == 0 || wsColPart[i] == 1)) r.Add(i); return r; }
        static Vector3 WsTorsoCenter() { var t = WsTorso(); var c = Vector3.zero; foreach (int i in t) c += wsCols[i].bounds.center; return c / t.Count; }
        static Vector3 WsPartCenter(int part, out bool found)
        {
            var c = Vector3.zero; int n = 0;
            for (int i = 0; i < wsCols.Length; i++) if (WsUsable(i) && wsColPart[i] == part) { c += wsCols[i].bounds.center; n++; }
            found = n > 0; return n > 0 ? c / n : Vector3.zero;
        }
        /// <summary>横腹の経路 (ワールド): 腰の Collider の中心 (の少し後ろ) から胸の中心を通り、首の付け根 (胸と首の中点) まで。
        /// 部位ごとの短いカプセルをまたぐ。長さ (m) を返す。</summary>
        static float WsPathEnds(out Vector3 a, out Vector3 b)
        {
            bool fh, fc, fn;
            Vector3 hip = WsPartCenter(0, out fh), chest = WsPartCenter(1, out fc), neck = WsPartCenter(2, out fn);
            if (!fh || !fc) throw new InvalidOperationException("hip / chest colliders missing");
            Vector3 dir = (chest - hip).sqrMagnitude > 1e-6f ? (chest - hip).normalized : WsFlat(wsRoot.forward);
            a = hip - dir * 0.03f;
            b = fn ? chest + (neck - chest) * 0.5f : chest + dir * 0.05f;
            return (b - a).magnitude;
        }
        /// <summary>経路の u (0..1) の点へ、体の外側 (右 0.6 m) から体の中へ Raycast (道具と同じ Layer、Trigger も当てる)。
        /// 柴の部位 Collider で最も手前の当たりを返す。当たらなければ ci = -1 (道具ならその接触は切れる)。</summary>
        static WsHit WsPath(float u)
        {
            Vector3 a, b; WsPathEnds(out a, out b);
            Vector3 q = Vector3.Lerp(a, b, u), side = WsFlat(wsRoot.right), o = q + side * 0.6f;
            var hits = Physics.RaycastAll(o, -side, 0.9f, wsLayer, QueryTriggerInteraction.Collide);
            var best = new WsHit { ci = -1 }; float bd = float.MaxValue;
            foreach (var h in hits)
            {
                int ci = Array.IndexOf(wsCols, h.collider);
                if (ci < 0 || !WsUsable(ci) || h.distance >= bd) continue;
                bd = h.distance; best = new WsHit { ci = ci, p = h.point };
            }
            return best;
        }
        /// <summary>経路の点のうち最初に当たる所 (u を中心から外へ探す)。</summary>
        static WsHit WsPathHit(float u)
        {
            for (int k = 0; k <= 20; k++)
            {
                float d = (k + 1) / 2 * 0.02f * (k % 2 == 0 ? 1f : -1f);
                var h = WsPath(Mathf.Clamp01(u + d));
                if (h.ci >= 0) return h;
            }
            throw new InvalidOperationException("no ray from the flank hits the Shiba near u=" + u);
        }
        /// <summary>道具の washLayer (tools[0])。読めなければ部位 Collider の Layer の和。</summary>
        static int WsLoadLayer()
        {
            int mask = 0;
            try
            {
                var tools = WsArr<UdonBehaviour>(wsWash, "tools");
                object o;
                if (tools.Length > 0 && tools[0] != null && tools[0].TryGetProgramVariable("washLayer", out o))
                    mask = o is LayerMask lm ? lm.value : Convert.ToInt32(o);
            }
            catch (Exception) { mask = 0; }
            if (mask == 0) foreach (var c in wsCols) if (c != null) mask |= 1 << c.gameObject.layer;
            return mask;
        }
        static WsAnchor WsAnchorOf(WsHit h) { return new WsAnchor { ci = h.ci, local = wsCols[h.ci].transform.InverseTransformPoint(h.p) }; }
        static WsHit WsAt(WsAnchor a) { return new WsHit { ci = a.ci, p = wsCols[a.ci].transform.TransformPoint(a.local) }; }
        /// <summary>全部位の Collider の表面の点 (犬の向きの 6 方向 + 斜め 8 方向から ClosestPoint)。dedupe m より近い点は捨てる。</summary>
        static List<WsAnchor> WsCover(float dedupe)
        {
            Vector3 r = wsRoot.right, u = wsRoot.up, f = wsRoot.forward;
            var dirs = new List<Vector3> { r, -r, u, -u, f, -f };
            foreach (float a in new[] { 1f, -1f }) foreach (float b in new[] { 1f, -1f }) foreach (float c in new[] { 1f, -1f }) dirs.Add((r * a + u * b + f * c).normalized);
            var pts = new List<WsAnchor>(); var world = new List<Vector3>();
            for (int i = 0; i < wsCols.Length; i++)
            {
                if (!WsUsable(i)) continue;
                var bnd = wsCols[i].bounds; float reach = bnd.extents.magnitude + 0.3f;
                foreach (var d in dirs)
                {
                    var p = wsCols[i].ClosestPoint(bnd.center + d * reach);
                    if (world.Any(w => (w - p).magnitude < dedupe)) continue;
                    world.Add(p); pts.Add(WsAnchorOf(new WsHit { ci = i, p = p }));
                }
            }
            return pts;
        }
        static Vector3 WsToMesh(int ci, Vector3 world) { return wsB2M[ci].MultiplyPoint3x4(wsBones[ci].InverseTransformPoint(world)); }

        // ============================================================
        // マスクとセル
        // ============================================================

        /// <summary>今の面 (ヒープの _cur、Swap のたびに殻の _Mask も同じ面を指す)。殻のマテリアルは実行時の複製 (Start の .material の後の
        /// sharedMaterial = その複製) を読むだけで、新しい複製を作らない。</summary>
        static Color[] WsMask()
        {
            object o; RenderTexture cur = wsWash.TryGetProgramVariable("_cur", out o) ? o as RenderTexture : null;
            var sm = wsShell.sharedMaterial; var viaShell = sm != null && sm.HasProperty("_Mask") ? sm.GetTexture("_Mask") as RenderTexture : null;
            var rt = cur != null ? cur : viaShell;
            if (rt == null) throw new InvalidOperationException("current mask unknown (_cur and the shell's _Mask are both null)");
            string src = (cur != null ? "_cur " + cur.name : "shell _Mask " + viaShell.name) + (cur != null && viaShell != null && viaShell != cur ? " (shell _Mask differs: " + viaShell.name + ")" : "");
            if (wsReport.maskSource == null || !wsReport.maskSource.Contains(src)) wsReport.maskSource = (wsReport.maskSource == null ? "" : wsReport.maskSource + "; ") + src;
            return WsRead(rt, TextureFormat.RGBAHalf);
        }
        static Color[] WsRead(RenderTexture rt, TextureFormat format)
        {
            var prev = RenderTexture.active;
            var tex = new Texture2D(rt.width, rt.height, format, false, true) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply(false);
                return tex.GetPixels();
            }
            finally { RenderTexture.active = prev; UnityEngine.Object.DestroyImmediate(tex); }
        }
        static float WsCh(Color c, int ch) { return ch == 0 ? c.r : ch == 1 ? c.g : c.b; }
        static bool WsAllowed(int part, int q) { return q == part || (part >= 0 && part < wsNeighbors.Length && q < 32 && ((wsNeighbors[part] >> q) & 1) != 0); }

        /// <summary>メッシュ座標の点 m に最も近い、その部位 (と隣) の有効なテクセル WsNearK 個の平均。a が null なら b の値、でなければ b - a。
        /// dminM = 最も近いテクセルまで (m)。</summary>
        static float WsLocal(Vector3 m, int part, Color[] a, Color[] b, int ch, out float dminM)
        {
            var bestD = new float[WsNearK]; var bestK = new int[WsNearK];
            for (int j = 0; j < WsNearK; j++) { bestD[j] = float.MaxValue; bestK[j] = -1; }
            for (int k = 0; k < wsTexCount; k++)
            {
                if (!WsAllowed(part, wsTexPart[k])) continue;
                float d = (wsTexPos[k] - m).sqrMagnitude;
                if (d >= bestD[WsNearK - 1]) continue;
                int j = WsNearK - 1;
                while (j > 0 && bestD[j - 1] > d) { bestD[j] = bestD[j - 1]; bestK[j] = bestK[j - 1]; j--; }
                bestD[j] = d; bestK[j] = k;
            }
            dminM = bestK[0] < 0 ? float.MaxValue : Mathf.Sqrt(bestD[0]) * wsMts;
            float sum = 0f; int n = 0;
            foreach (int k in bestK) { if (k < 0) continue; int i = wsTexIdx[k]; sum += a == null ? WsCh(b[i], ch) : WsCh(b[i], ch) - WsCh(a[i], ch); n++; }
            return n > 0 ? sum / n : 0f;
        }

        /// <summary>区間に沿った点 (トレースから等間隔に samples 個) の近くのテクセルの変化が ok を満たすか。接触の点から 0.6 r より近いテクセルが
        /// 無ければ (Collider の表面が毛から離れている) 失敗。結果の文字列を返す。</summary>
        static string WsBandCheck(WsTrace tr, Color[] a, Color[] b, int ch, float radius, int samples, Func<float, bool> ok, string label)
        {
            if (tr.mesh.Count < samples) throw new InvalidOperationException("only " + tr.mesh.Count + " contacts recorded");
            var vals = new List<string>(); bool all = true;
            for (int s = 0; s < samples; s++)
            {
                int i = Mathf.RoundToInt(s * (tr.mesh.Count - 1) / (float)Mathf.Max(1, samples - 1));
                float dm, d = WsLocal(tr.mesh[i], tr.part[i], a, b, ch, out dm);
                if (dm > 0.6f * radius) throw new InvalidOperationException("no texel within 0.6 r of the contact (nearest " + dm.ToString("F3") + " m; the collider surface is off the mesh)");
                vals.Add(d.ToString("+0.00;-0.00"));
                all &= ok(d);
            }
            string text = "d" + "RGB"[ch] + " " + string.Join(" ", vals);
            WsExpect(all, "along the stroke " + label + " at every sample (" + text + ")");
            return text;
        }

        /// <summary>区間に沿った隙間の検査: トレースを samples 等分した点ごとの変化の最小。部位の境目 (Collider をまたぐ所) で帯が切れていれば
        /// ここが小さくなる。最小の場所 (u・部位・前後の部位) と、表面が毛から 0.6 r より離れた点の数を text に。</summary>
        static float WsBandMin(WsTrace tr, Color[] a, Color[] b, int ch, int samples, out string text)
        {
            float min = float.MaxValue; int at = -1, off = 0;
            for (int s = 0; s < samples; s++)
            {
                int i = Mathf.RoundToInt(s * (tr.mesh.Count - 1) / (float)Mathf.Max(1, samples - 1));
                float dm, d = WsLocal(tr.mesh[i], tr.part[i], a, b, ch, out dm);
                if (dm > 0.6f * wsSpongeR) off++;
                if (d < min) { min = d; at = i; }
            }
            text = at < 0 ? "no samples" : "min at u=" + tr.u[at].ToString("F2") + " part " + tr.part[at] + " (" + wsCols[tr.ci[at]].name + "), parts " + tr.Parts() + (off > 0 ? ", " + off + " samples off the mesh (> 0.6 r)" : "");
            return at < 0 ? 0f : min;
        }

        /// <summary>トレースの点から farM m より遠い有効なテクセルの、チャンネルの増え方の最大。</summary>
        static float WsFarMax(List<Vector3> pts, Color[] a, Color[] b, int ch, float farM, out int n)
        {
            var sub = new List<Vector3>(); int step = Mathf.Max(1, pts.Count / 40);
            for (int i = 0; i < pts.Count; i += step) sub.Add(pts[i]);
            sub.Add(pts[pts.Count - 1]);
            float far = farM / wsMts, far2 = far * far, worst = 0f; n = 0;
            for (int k = 0; k < wsTexCount; k++)
            {
                var p = wsTexPos[k]; bool near = false;
                foreach (var q in sub) if ((p - q).sqrMagnitude <= far2) { near = true; break; }
                if (near) continue;
                n++; int i = wsTexIdx[k];
                worst = Mathf.Max(worst, WsCh(b[i], ch) - WsCh(a[i], ch));
            }
            return worst;
        }
        static float WsMinDist(Vector3 p, List<Vector3> pts) { float d = float.MaxValue; foreach (var q in pts) d = Mathf.Min(d, (p - q).magnitude); return d; }
        static float WsSegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float l2 = ab.sqrMagnitude;
            float t = l2 > 1e-12f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2) : 0f;
            return (p - (a + ab * t)).magnitude;
        }
        static float WsMaxDiff(Color[] a, Color[] b)
        {
            float m = 0f;
            for (int k = 0; k < wsTexCount; k++) { int i = wsTexIdx[k]; m = Mathf.Max(m, Mathf.Abs(a[i].r - b[i].r), Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b)); }
            return m;
        }
        /// <summary>セル (_dirt / _foam / _wet) のうち、写しと違うものの数。</summary>
        static int WsCellDiff(float[] d0, float[] f0, float[] w0)
        {
            var d = Get<float[]>(wsWash, "_dirt"); var f = Get<float[]>(wsWash, "_foam"); var w = Get<float[]>(wsWash, "_wet"); int n = 0;
            for (int c = 0; c < d.Length; c++) if (d[c] != d0[c] || f[c] != f0[c] || w[c] != w0[c]) n++;
            return n;
        }
    }
}
