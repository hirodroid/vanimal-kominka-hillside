using System;
using System.Linq;
using System.Reflection;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRCStation = VRC.SDK3.Components.VRCStation;

namespace KominkaHillside.Editor
{
    /// <summary>M3 の Verify (Cart/2) と Probe (Cart/3): 静的カート 5 台・乗り場パネルの位置・カート上メニュー 8 枚・cartParkDist / cartParkedMs、
    /// Probe は ParkedStartDistance と ReclaimCandidate (Runtime の判定、Reflection)。</summary>
    public static partial class KH_CartSetupEditor
    {
        static void M3Check(V v, KH_CartManager manager, SerializedObject mso, UdonBehaviour mb, KH_LeashManager leash, KH_WalkRoute route, Transform stationsRoot, KH_CartSeat[] seats, KH_CartStationPanel[] cartPanels)
        {
            // ---- 静的カート (メッシュだけ) ----
            for (int s = 0; s < StationCount; s++)
            {
                string n = StationCartPrefix + s;
                var sc = stationsRoot != null ? stationsRoot.Find(n) : null;
                v.Req(sc != null, n + " exists under " + StationsName);
                if (sc == null) continue;
                bool bare = sc.GetComponentsInChildren<UdonBehaviour>(true).Length == 0 && sc.GetComponentsInChildren<UdonSharpBehaviour>(true).Length == 0
                    && sc.GetComponentsInChildren<VRCStation>(true).Length == 0 && sc.GetComponentsInChildren<Collider>(true).Length == 0;
                v.Req(bare, n + " has no Udon / VRCStation / Collider");
                v.Req(sc.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == 0), n + " on layer Default");
                v.Req(sc.GetComponentsInChildren<Renderer>(true).Length > 0, n + " has renderers");
                bool fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) != null;
                if (fbx) v.Req(sc.GetComponent<LODGroup>() != null && sc.GetComponent<LODGroup>().lodCount == 2, n + " LODGroup (2 levels)");
                if (leash.spotPositions != null && leash.spotPositions.Length > s && leash.spotYaws != null && leash.spotYaws.Length > s)
                {
                    var tr = BuildTrack(route); ProjectStops(tr, route, leash);
                    StationCartPoseFor(s, tr, leash, out Vector3 pos, out Quaternion rot, out Vector3 _, out float _, out bool _);
                    v.Req((sc.position - pos).magnitude < 0.02f && Quaternion.Angle(sc.rotation, rot) < 0.5f, n + " at track d=" + tr.spot[s].ToString("F1") + (s == 0 ? " (" + tr.spot0Basis + ")" : "") + ", " + (StationSide[s] < 0 ? "left" : "right") + " " + StationCartLateral(s, tr).ToString("F2") + " m (NavMesh width + 0.8, 2.2-3.5), facing the track direction" + " (" + (sc.position - pos).magnitude.ToString("F3") + " m off)");
                    if (s == 0 && tr.spot0Basis.StartsWith("MAP BOARD NOT FOUND")) v.Pend("station 0: " + tr.spot0Basis);
                }
            }
            // ---- カート上メニュー ----
            var managerBacking = mb;
            for (int i = 0; i < MaxCarts; i++)
            {
                string c = "Cart_" + i + "/" + CartPanelName;
                var seat = seats[i]; var panel = cartPanels[i];
                v.Req(panel != null, c + " KH_CartStationPanel");
                if (seat == null || panel == null) continue;
                var pb = SyncCheck(v, panel, Networking.SyncType.None, c);
                var pso = new SerializedObject(panel);
                IntCheck(v, pso, pb, "spot", -1, c);
                if (pso.FindProperty("cart") != null) IntCheck(v, pso, pb, "cart", i, c); else v.Pend(c + " KH_CartStationPanel.cart not found (Runtime M3)");
                RefCheck(v, pso, pb, "manager", manager, c);
                var model = seat.transform.Find(ModelName); var armR = model != null ? FindDeep(model, AnchorStopR) : null;
                v.Req(armR != null && (panel.transform.position - (armR.position + seat.transform.up * PanelAboveArmrest)).magnitude < 0.02f, c + " 0.55 m above Anchor_StopR");
                v.Req(Vector3.Dot(panel.transform.forward, -seat.transform.right) > 0.999f, c + " front (-Z) faces outward (cart right)");
                var board = panel.transform.Find(BoardName);
                v.Req(board != null && !board.gameObject.activeSelf, c + " board hidden at start (shown while Parked)");
                if (pso.FindProperty("content") != null) RefCheck(v, pso, pb, "content", board != null ? board.gameObject : null, c);
                var notice = board != null ? Comp<TMPro.TextMeshProUGUI>(board.Find("Notice")) : null;
                v.Req(notice != null && notice.text == Ja("cart.parked"), c + " notice = cart.parked");
                var sso = new SerializedObject(seat);
                if (sso.FindProperty("cartPanel") != null) RefCheck(v, sso, UdonSharpEditorUtility.GetBackingUdonBehaviour(seat), "cartPanel", panel, "Cart_" + i);
                else v.Pend("Cart_" + i + " KH_CartSeat.cartPanel not found (Runtime M3)");
            }
            // ---- 置き去りの同期配列 ----
            foreach (var name in new[] { "cartParkDist", "cartParkedMs", "cartParkDir" })   // cartParkDir: M3 Codex 後 (駐車方向の同期)
            {
                var p = mso.FindProperty(name);
                v.Req(p != null && p.isArray && p.arraySize == MaxCarts, "manager." + name + " length " + MaxCarts + " (proxy " + (p != null && p.isArray ? p.arraySize : -1) + ")");
                if (managerBacking != null && managerBacking.publicVariables.TryGetVariableValue(name, out object bv)) v.Req(bv is Array a && a.Length == MaxCarts, "manager." + name + " length " + MaxCarts + " (backing)");
            }
        }

        /// <summary>Probe: プロキシの同期配列を一時的に書き換えて Runtime の判定を呼び、必ず元に戻す (シリアライズはしない。シーンは dirty にならない)。</summary>
        static void ProbeParked(P pr, KH_CartManager mgr)
        {
            var t = mgr.GetType(); const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var mStart = t.GetMethod("ParkedStartDistance", F, null, new[] { typeof(int) }, null);
            var mReclaim = t.GetMethod("ReclaimCandidate", F, null, Type.EmptyTypes, null);
            if (mStart == null) pr.Skip("KH_CartManager.ParkedStartDistance(int) not found");
            if (mReclaim == null) pr.Skip("KH_CartManager.ReclaimCandidate() not found");
            if (mStart == null && mReclaim == null) return;
            var fields = new[] { "cartState", "cartRider", "cartParkDist", "cartParkedMs", "cartFreedMs" }.Select(n => t.GetField(n, F)).ToArray();
            if (fields.Any(f => f == null)) { pr.Skip("manager arrays cartState / cartRider / cartParkDist / cartParkedMs / cartFreedMs not all present: parked probe skipped"); return; }
            var saved = fields.Select(f => ((Array)f.GetValue(mgr))?.Clone()).ToArray();
            if (saved.Any(a => a == null || ((Array)a).Length != MaxCarts)) { pr.Skip("manager arrays not length 8: parked probe skipped"); return; }
            try
            {
                var state = (int[])((int[])saved[0]).Clone(); var rider = (int[])((int[])saved[1]).Clone(); var park = (float[])((float[])saved[2]).Clone(); var parkedMs = (int[])((int[])saved[3]).Clone(); var freed = (int[])((int[])saved[4]).Clone();
                // 時刻は Runtime と同じ源 (サーバー時刻)。解放・駐車から猶予 (ReclaimGraceMs) + 10 s 以上前にする (猶予内は回収しない規則)
                int now = 0; try { now = Networking.GetServerTimeInMilliseconds(); } catch (Exception) { }
                var graceField = t.GetField("ReclaimGraceMs", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                int grace = graceField != null ? Convert.ToInt32(graceField.IsLiteral ? graceField.GetRawConstantValue() : graceField.GetValue(null)) : 3000;
                int older = unchecked(now - grace - 40000), newer = unchecked(now - grace - 10000), freedLong = unchecked(now - grace - 60000);
                for (int c = 0; c < MaxCarts; c++) { state[c] = 1; rider[c] = 100 + c; park[c] = 0f; parkedMs[c] = 0; freed[c] = freedLong; }   // 全車乗車中
                state[2] = 3; rider[2] = 0; park[2] = 123.5f; parkedMs[2] = newer;   // 置き去り (新しい)
                state[5] = 3; rider[5] = 0; park[5] = 456.25f; parkedMs[5] = older;  // 置き去り (古い)
                pr.r.traffic.Add("parked probe: now " + now + " grace " + grace + (graceField == null ? " (ReclaimGraceMs not found, 3000 assumed)" : ""));
                fields[0].SetValue(mgr, state); fields[1].SetValue(mgr, rider); fields[2].SetValue(mgr, park); fields[3].SetValue(mgr, parkedMs); fields[4].SetValue(mgr, freed);
                if (mStart != null)
                {
                    float s2 = (float)mStart.Invoke(mgr, new object[] { 2 }), s5 = (float)mStart.Invoke(mgr, new object[] { 5 }), s0 = (float)mStart.Invoke(mgr, new object[] { 0 });
                    pr.Req(Mathf.Abs(s2 - 123.5f) < 1e-4f && Mathf.Abs(s5 - 456.25f) < 1e-4f && s0 == -1f, "ParkedStartDistance = cartParkDist for Parked carts (2: " + s2 + ", 5: " + s5 + "), -1 for a riding cart (" + s0 + ")");
                }
                if (mReclaim != null)
                {
                    int pick = (int)mReclaim.Invoke(mgr, null);
                    pr.Req(pick == 5, "ReclaimCandidate with no Idle cart = the oldest Parked (expected 5, got " + pick + ")");
                    state[7] = 0; rider[7] = 0; fields[0].SetValue(mgr, state); fields[1].SetValue(mgr, rider);
                    int pickIdle = (int)mReclaim.Invoke(mgr, null);
                    pr.Req(pickIdle == 7, "ReclaimCandidate prefers an Idle cart (expected 7, got " + pickIdle + ")");
                }
            }
            catch (TargetInvocationException ex) { pr.Skip("parked probe threw in Edit mode (server time?): " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message)); }
            finally { for (int k = 0; k < fields.Length; k++) fields[k].SetValue(mgr, saved[k]); }
        }

        /// <summary>駐車枠 (KH_CartManager.ParkSlotDistance / ParkSlotLateral、Reflection) の 3 ケース。プロキシの cartState / cartParkDir / cartParkDist を一時的に書き換え、必ず戻す。
        /// ① 逆方向 (dir −1) は同じ距離でも道の反対側 (右 = 進行方向の右) に置かれ、同じ入力なら同じ結果 (同期値だけで決まる)
        /// ② 同じ目的地へ同じ向きで 3 台が続けて駐車すると、駐車距離が互いに 1.6 m 以上離れる
        /// ③ 狭い区間 (class 2) の中で止まると、進行方向側の出口 + 3 m に駐車する</summary>
        static void ProbeParkSlots(P pr, KH_CartManager mgr)
        {
            var t = mgr.GetType(); const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var mDist = t.GetMethod("ParkSlotDistance", F, null, new[] { typeof(float), typeof(int), typeof(int) }, null);
            var mLat = t.GetMethod("ParkSlotLateral", F, null, new[] { typeof(float), typeof(int) }, null);
            var mPoint = t.GetMethod("TrackPoint", F, null, new[] { typeof(float) }, null);
            var mDir = t.GetMethod("TrackDirection", F, null, new[] { typeof(float) }, null);
            if (mDist == null || mLat == null || mPoint == null || mDir == null) { pr.Skip("KH_CartManager.ParkSlotDistance / ParkSlotLateral / TrackPoint / TrackDirection not all found: park slot probe skipped"); return; }
            var fState = t.GetField("cartState", F); var fDir = t.GetField("cartParkDir", F); var fDist = t.GetField("cartParkDist", F);
            if (fState == null || fDir == null || fDist == null) { pr.Skip("cartState / cartParkDir / cartParkDist not all present: park slot probe skipped"); return; }
            var so = new SerializedObject(mgr);
            var lenP = so.FindProperty("trackLength"); float L = lenP != null ? lenP.floatValue : 0f;
            var from = ReadFloats(so, "narrowFrom"); var to = ReadFloats(so, "narrowTo"); var dest = ReadFloats(so, "destTrackDistance");
            if (L <= 0f || dest == null || dest.Length < 2) { pr.Skip("track not saved (run Cart/1): park slot probe skipped"); return; }
            var cache = t.GetField("_routeCached", F); if (cache != null && cache.FieldType == typeof(bool)) cache.SetValue(mgr, false);
            float W(float d) { d %= L; return d < 0f ? d + L : d; }
            bool InAnyZone(float d) { if (from == null || to == null) return false; for (int k = 0; k < from.Length && k < to.Length; k++) if (W(d - from[k]) <= W(to[k] - from[k])) return true; return false; }
            var savedState = (int[])((int[])fState.GetValue(mgr)).Clone(); var savedDir = (int[])((int[])fDir.GetValue(mgr)).Clone(); var savedDist = (float[])((float[])fDist.GetValue(mgr)).Clone();
            try
            {
                var state = new int[MaxCarts]; var pdir = new int[MaxCarts]; var pdist = new float[MaxCarts];
                for (int c = 0; c < MaxCarts; c++) { state[c] = 1; pdir[c] = 1; pdist[c] = 0f; }   // 全車走行中 (Parked なし)
                fState.SetValue(mgr, state); fDir.SetValue(mgr, pdir); fDist.SetValue(mgr, pdist);
                float Dist(float want, int dir, int self) { return (float)mDist.Invoke(mgr, new object[] { want, dir, self }); }
                float Lat(float d, int dir) { return (float)mLat.Invoke(mgr, new object[] { d, dir }); }
                Vector3 Pt(float d) { return (Vector3)mPoint.Invoke(mgr, new object[] { d }); }
                Vector3 Fw(float d) { return (Vector3)mDir.Invoke(mgr, new object[] { d }); }

                // 広い区間の点 (広場の行き先から、狭い区間の外を探す)
                float wide = dest[1] >= 0f ? dest[1] : 0f;
                for (int k = 0; k < 400 && (InAnyZone(wide) || InAnyZone(wide - 6f)); k++) wide = W(wide + 5f);

                // ① 逆方向の横の符号
                {
                    float latP = Lat(wide, +1), latN = Lat(wide, -1), latP2 = Lat(wide, +1);
                    var p = Pt(wide); var f = Fw(wide); var rightPlus = new Vector3(f.z, 0f, -f.x);
                    var posP = p + rightPlus * latP; var posN = p - rightPlus * latN;   // dir −1 の右 = +1 の左
                    float sideP = Vector3.Dot(posP - p, rightPlus), sideN = Vector3.Dot(posN - p, rightPlus);
                    pr.Req(latP > 0f && latN > 0f && sideP > 0f && sideN < 0f && Mathf.Approximately(latP, latP2),
                        "park 1: at d=" + wide.ToString("F1") + " dir +1 parks " + latP.ToString("F2") + " m right, dir -1 " + latN.ToString("F2") + " m on the other side (signed " + sideP.ToString("F2") + " / " + sideN.ToString("F2") + "), same input -> same result");
                }
                // ② 同じ目的地に 3 台が同じ向きで続けて駐車
                {
                    var got = new float[3];
                    for (int c = 0; c < 3; c++)
                    {
                        got[c] = Dist(wide, +1, c);
                        state[c] = 3; pdir[c] = 1; pdist[c] = got[c];   // Parked として登録してから次の車
                        fState.SetValue(mgr, state); fDir.SetValue(mgr, pdir); fDist.SetValue(mgr, pdist);
                    }
                    float g01 = Mathf.Abs(W(got[0] - got[1] + L * .5f) - L * .5f), g12 = Mathf.Abs(W(got[1] - got[2] + L * .5f) - L * .5f), g02 = Mathf.Abs(W(got[0] - got[2] + L * .5f) - L * .5f);
                    pr.Req(g01 >= 1.6f - 1e-3f && g12 >= 1.6f - 1e-3f && g02 >= 1.6f - 1e-3f, "park 2: three carts arriving at d=" + wide.ToString("F1") + " park at " + string.Join(", ", got.Select(x => x.ToString("F2"))) + " (gaps " + g01.ToString("F2") + ", " + g12.ToString("F2") + ", " + g02.ToString("F2") + " >= 1.6)");
                    for (int c = 0; c < 3; c++) { state[c] = 1; pdist[c] = 0f; }
                    fState.SetValue(mgr, state); fDist.SetValue(mgr, pdist);
                }
                // ③ 狭い区間の中で停止 → 出口 + 3 m (出口の先が別の区間なら SKIP)
                if (from == null || to == null || from.Length == 0) pr.Skip("park 3: no narrow zone saved");
                else
                {
                    int z = -1;
                    for (int k = 0; k < from.Length && z < 0; k++) if (!InAnyZone(to[k] + 3f) && !InAnyZone(from[k] - 3f)) z = k;
                    if (z < 0) pr.Skip("park 3: every zone exit + 3 m lies in another zone");
                    else
                    {
                        float mid = W(from[z] + W(to[z] - from[z]) * .5f);
                        float fwd = Dist(mid, +1, -1), back = Dist(mid, -1, -1);
                        float expF = W(to[z] + 3f), expB = W(from[z] - 3f);
                        pr.Req(Mathf.Abs(W(fwd - expF + L * .5f) - L * .5f) < 1e-3f && Mathf.Abs(W(back - expB + L * .5f) - L * .5f) < 1e-3f,
                            "park 3: stop inside zone " + z + " (" + from[z].ToString("F1") + "-" + to[z].ToString("F1") + ") at " + mid.ToString("F1") + ": dir +1 parks at " + fwd.ToString("F2") + " (exit + 3 = " + expF.ToString("F2") + "), dir -1 at " + back.ToString("F2") + " (" + expB.ToString("F2") + ")");
                    }
                }
            }
            catch (TargetInvocationException ex) { pr.Req(false, "park slot function threw in Edit mode: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message)); }
            finally { fState.SetValue(mgr, savedState); fDir.SetValue(mgr, savedDir); fDist.SetValue(mgr, savedDist); }
        }
    }
}
