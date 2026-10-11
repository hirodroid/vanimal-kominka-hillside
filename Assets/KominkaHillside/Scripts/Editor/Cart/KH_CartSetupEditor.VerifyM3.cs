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
        static void M3Check(V v, KH_CartManager manager, SerializedObject mso, UdonBehaviour mb, KH_LeashManager leash, Transform stationsRoot, KH_CartSeat[] seats, KH_CartStationPanel[] cartPanels)
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
                    StationCartPose(leash.spotPositions[s], leash.spotYaws[s], out Vector3 pos, out Quaternion rot, out float _, out bool _);
                    v.Req((sc.position - pos).magnitude < 0.02f && Quaternion.Angle(sc.rotation, rot) < 0.5f, n + " at the landing spot's right 1.2 m, facing the trail (" + (sc.position - pos).magnitude.ToString("F3") + " m off)");
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
            foreach (var name in new[] { "cartParkDist", "cartParkedMs" })
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
    }
}
