using System;
using System.Linq;
using KominkaDogRun;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>M1.1 の Verify (Cart/2 から呼ぶ): 軌道 (節数 435、loopLength より約 11 m 短い、乗り場の開始点が門枠に入らない、入口の到着点が門枠の手前、
    /// noLateral がベンチを覆う)、座席の同期配列の長さ、各カートのドック 3 点と操作帯 (layer 17、配線はプロキシと backing)。シーンは変えない。</summary>
    public static partial class KH_CartSetupEditor
    {
        static void TrackCheck(V v, KH_CartManager manager, SerializedObject mso, UdonBehaviour mb, KH_WalkRoute route, KH_LeashManager leash)
        {
            var want = BuildTrack(route);
            ProjectStops(want, route, leash);
            var pos = ReadVectors(mso, "trackPositions"); var dist = ReadFloats(mso, "trackDistances"); var det = ReadBools(mso, "trackDetour");
            var lenP = mso.FindProperty("trackLength"); float len = lenP != null && lenP.propertyType == SerializedPropertyType.Float ? lenP.floatValue : -1f;
            int m = want.positions.Length;
            v.Req(pos != null && pos.Length == m, "track nodes = " + m + " (actual " + (pos != null ? pos.Length : -1) + ")");
            v.Req(pos != null && pos.Length == m && pos.Select((p, k) => (p - want.positions[k]).sqrMagnitude).All(x => x < 1e-8f), "trackPositions = route nodes " + want.firstNode + ".." + (route.count - 1) + (want.skipsNode0 ? " (node 0 is a spike: skipped)" : " (node 0 kept: no spike)") + " (Course/21 で経路を作り直したら Cart/1 を再実行)");
            v.Req(dist != null && dist.Length == m && dist.Select((d, k) => Mathf.Abs(d - want.distances[k])).All(x => x < 1e-3f), "trackDistances = cumulative chord lengths from node " + want.firstNode);
            v.Req(det != null && det.Length == m && det.SequenceEqual(want.detour), "trackDetour = linkAllowsDetour[k+" + want.firstNode + "]");
            v.Req(Mathf.Abs(len - want.length) < 1e-3f, "trackLength " + len.ToString("F2") + " (expected " + want.length.ToString("F2") + ")");
            float shorter = route.loopLength - len;
            float chordLoop = 0f; for (int i = 0; i < route.count; i++) chordLoop += Vector3.Distance(route.nodePositions[i], route.nodePositions[(i + 1) % route.count]);
            if (want.skipsNode0) v.Req(shorter > 8f && shorter < 14f, "node 0 spike skipped: trackLength is about 11 m shorter than the loop (actual " + shorter.ToString("F2") + " m; closing segment " + want.closing.ToString("F2") + " m)");
            else v.Req(Mathf.Abs(len - chordLoop) <= 0.01f, "node 0 kept (no spike): trackLength = the route's chord sum " + chordLoop.ToString("F2") + " (actual " + len.ToString("F2") + "; NavMesh loopLength " + route.loopLength.ToString("F2") + ")");
            var spot = ReadFloats(mso, "spotTrackDistance"); var dest = ReadFloats(mso, "destTrackDistance");
            v.Req(spot != null && spot.Length == StationCount && spot.Select((d, s) => Mathf.Abs(d - want.spot[s])).All(x => x < 1e-3f), "spotTrackDistance[5] = spotNodes projected onto the track");
            v.Req(dest != null && dest.Length == 6 && dest.Select((d, k) => Mathf.Abs(d - want.dest[k])).All(x => x < 1e-3f) && dest[2] == -1f && dest[3] == -1f, "destTrackDistance[6] projected (loops 2/3 = -1)");
            // backing: 型付きで取り出し、全要素をプロキシと比べる (float / Vector3 は 1e-4。Codex M1.1 レビュー 4)
            BackingVectors(v, mb, "trackPositions", pos);
            BackingFloats(v, mb, "trackDistances", dist);
            BackingBools(v, mb, "trackDetour", det);
            foreach (var name in new[] { "spotTrackDistance", "destTrackDistance", "noLateralFrom", "noLateralTo" }) BackingFloats(v, mb, name, ReadFloats(mso, name));
            v.Req(mb != null && mb.publicVariables.TryGetVariableValue("trackLength", out object tl) && tl is float tf && Mathf.Abs(tf - len) < 1e-3f, "manager.trackLength (backing)");

            // 門枠: 乗り場の開始点が入らない / 入口の到着点は門枠の外 (z が門枠より大きい側)
            if (!GateBounds(out Bounds gate, out string gatePath)) v.Pend("gate frame '" + GateFrameName + "' not found; gate checks skipped");
            else
            {
                bool InGate(Vector3 q) { return q.x >= gate.min.x && q.x <= gate.max.x && q.z >= gate.min.z && q.z <= gate.max.z; }
                for (int s = 0; s < StationCount; s++)
                {
                    var q = TrackPointRef(want, want.spot[s]);
                    v.Req(!InGate(q), "spot " + s + " start " + q.ToString("F1") + " outside the gate frame " + gatePath);
                }
                var e = TrackPointRef(want, want.dest[0]);
                v.Req(!InGate(e) && e.z > gate.center.z, "entrance arrival " + e.ToString("F1") + " is in front of the gate (outside, z > " + gate.center.z.ToString("F1") + ")");
            }

            // 横ずれ 0 の範囲: 並びの整合とベンチ
            var from = ReadFloats(mso, "noLateralFrom"); var to = ReadFloats(mso, "noLateralTo");
            v.Req(from != null && to != null && from.Length == to.Length && from.Zip(to, (a, b) => a <= b && a >= 0f && b <= len + 1e-3f).All(x => x), "noLateralFrom/To paired ranges within [0, trackLength]");
            var bench = GameObject.Find(BenchPath);
            if (bench == null && KH_RoadsideSceneryEditor.ExcludePlazaBench) v.Req(KH_CoursePlazaBenchEditor.Targets().Count == 0, "plaza bench removed (ExcludePlazaBench, Course/16): no bench to cover with noLateral");
            else if (bench == null) v.Pend("bench " + BenchPath + " not found; noLateral bench check skipped");
            else
            {
                float db = ProjectToTrack(want, bench.transform.position);
                v.Req(InNoLateral(from, to, db), "noLateral covers the viewpoint bench at d=" + db.ToString("F1") + " (ranges " + (from != null ? string.Join(",", from.Select((a, k) => a.ToString("F0") + "-" + to[k].ToString("F0"))) : "none") + ")");
            }

            // 座席の同期配列 (宣言時に長さ 8)
            foreach (var name in new[] { "cartSeatSteps", "cartYawSteps" })
            {
                var p = mso.FindProperty(name);
                v.Req(p != null && p.isArray && p.arraySize == MaxCarts, "manager." + name + " length " + MaxCarts + " (proxy " + (p != null && p.isArray ? p.arraySize : -1) + ")");
                if (mb != null && mb.publicVariables.TryGetVariableValue(name, out object bv)) v.Req(bv is int[] ia && ia.Length == MaxCarts, "manager." + name + " length " + MaxCarts + " (backing)");
            }
        }

        static void CartUiCheck(V v, GameObject cart, int i, SerializedObject seatSo, UdonBehaviour seatBacking, Transform seatT, KH_CartManager manager, KD_Language language, KD_TreatPouch menu)
        {
            string c = "Cart_" + i;
            var seatLocal = seatT != null ? cart.transform.InverseTransformPoint(seatT.position) : Vector3.zero;
            var seatBase = new Vector3(seatLocal.x, 0f, seatLocal.z);

            // ---- ドック ----
            var docks = cart.transform.Find(DocksName);
            var dock = docks != null ? docks.GetComponent<KH_CartDock>() : null;
            v.Req(dock != null, c + "/" + DocksName + " KH_CartDock");
            if (dock != null)
            {
                var db = SyncCheck(v, dock, Networking.SyncType.None, c + " dock");
                var dso = new SerializedObject(dock);
                RefCheck(v, dso, db, "manager", manager, c + " dock");
                IntCheck(v, dso, db, "index", i, c + " dock");
                RefCheck(v, dso, db, "menu", menu, c + " dock");
                var videos = UnityEngine.Object.FindObjectsOfType<KH_VideoCamera>(true); var photos = UnityEngine.Object.FindObjectsOfType<KD_PhotoViewer>(true);
                if (videos.Length == 1) RefCheck(v, dso, db, "video", videos[0], c + " dock"); else v.Pend(c + " dock.video: scene has " + videos.Length + " KH_VideoCamera");
                if (photos.Length == 1) RefCheck(v, dso, db, "photo", photos[0], c + " dock"); else v.Pend(c + " dock.photo: scene has " + photos.Length + " KD_PhotoViewer");
                var want = new[] { DockMenuLocal(seatBase), DockMonitorLocal(seatBase), DockPhotoLocal(seatBase) };
                var names = new[] { DockMenuName, DockMonitorName, DockPhotoName }; var fields = new[] { "dockMenu", "dockMonitor", "dockPhoto" };
                for (int k = 0; k < 3; k++)
                {
                    var t = docks.Find(names[k]);
                    v.Req(t != null && (cart.transform.InverseTransformPoint(t.position) - want[k]).magnitude < PlacementTolerance, c + " " + names[k] + " at " + want[k].ToString("F2") + " (cart local, ±" + PlacementTolerance + " m)");
                    if (t != null)
                    {
                        Vector3 away;
                        if (k == 2) { away = t.position - cart.transform.TransformPoint(seatBase); away.y = 0f; }   // Dock_Photo は未使用で従来の水平向き
                        else away = t.position - cart.transform.TransformPoint(SeatedEyeLocal);
                        v.Req(Vector3.Dot(t.forward, away.normalized) > .999f, c + " " + names[k] + " +Z points from the eye to the board (front -Z faces the rider)");
                    }
                    RefCheck(v, dso, db, fields[k], t, c + " dock");
                }
            }

            // ---- 操作帯 ----
            var ct = cart.transform.Find(ControlsName);
            var controls = ct != null ? ct.GetComponent<KH_CartControls>() : null;
            v.Req(controls != null, c + "/" + ControlsName + " KH_CartControls");
            if (controls != null)
            {
                var cb = SyncCheck(v, controls, Networking.SyncType.None, c + " controls");
                v.Req((cart.transform.InverseTransformPoint(ct.position) - ControlsLocal(seatBase)).magnitude < PlacementTolerance, c + " controls at " + ControlsLocal(seatBase).ToString("F2") + " (cart local, ±" + PlacementTolerance + " m)");
                v.Req(Vector3.Dot(ct.forward, (ct.position - cart.transform.TransformPoint(SeatedEyeLocal)).normalized) > .999f, c + " controls face the eye (tilted up toward it)");
                var board = ct.Find(BoardName);
                v.Req(board != null, c + " controls Board");
                if (board != null)
                {
                    var brt = (RectTransform)board; var canvas = board.GetComponent<Canvas>(); var col = board.GetComponent<BoxCollider>();
                    bool layers = ct.gameObject.layer == ControlsLayer && board.GetComponentsInChildren<Transform>(true).All(x => x.gameObject.layer == ControlsLayer);
                    v.Req(layers, c + " controls and every child on layer 17 (Walkthrough)");
                    v.Req(canvas != null && canvas.renderMode == RenderMode.WorldSpace && board.GetComponent<VRCUiShape>() != null && board.GetComponent<GraphicRaycaster>() != null
                        && col != null && col.isTrigger && Mathf.Approximately(col.size.x, brt.sizeDelta.x) && Mathf.Approximately(col.size.y, brt.sizeDelta.y)
                        && Mathf.Approximately(brt.localScale.x, ControlsScale) && Mathf.Abs(brt.sizeDelta.x * ControlsScale - 0.28f) < 1e-4f,
                        c + " controls board: WorldSpace Canvas + VRCUiShape + Raycaster + trigger BoxCollider, width 0.28 m");
                    v.Req(!board.gameObject.activeSelf, c + " controls board hidden at start (shown to the rider by _SetVisible)");
                    var buttons = new GameObject[ControlEvents.Length]; var labels = new TextMeshProUGUI[ControlEvents.Length];
                    for (int k = 0; k < ControlEvents.Length; k++)
                    {
                        var b = board.Find(BodyName + "/" + ControlNames[k]);
                        if (b == null) { v.Req(false, c + " controls button " + ControlNames[k]); continue; }
                        EventCheck(v, b.gameObject, cb, ControlEvents[k], c + " controls");
                        var brt2 = (RectTransform)b; var lab = Comp<TextMeshProUGUI>(b.Find("Label"));
                        v.Req(brt2.anchoredPosition == ControlPos(k) && brt2.sizeDelta == ControlSize(k) && lab != null && lab.text == Ja(ControlKeys[k]),
                            c + " controls " + ControlNames[k] + " at " + ControlPos(k) + " (2 x 5 grid, pairs side by side), label '" + (lab != null ? lab.text : "") + "' = " + ControlKeys[k]);
                        buttons[k] = b.gameObject; labels[k] = Comp<TextMeshProUGUI>(b.Find("Label"));
                    }
                    var bodyT = board.Find(BodyName); var tabT = cart.transform.Find(TabCanvasName);
                    v.Req(board.childCount == 1 && bodyT != null && bodyT.childCount == ControlEvents.Length + 1 && bodyT.Find(NoteName) != null, c + " controls board = Body only (" + ControlEvents.Length + " buttons + Note; actual board " + board.childCount + ", body " + (bodyT != null ? bodyT.childCount : -1) + "; old Tab removed)");
                    // タブの Canvas (カート直下) と 2 アンカー
                    v.Req(tabT != null, c + " " + TabCanvasName + " under the cart");
                    var tabBtn = tabT != null ? tabT.Find(TabButtonName) : null;
                    if (tabT != null)
                    {
                        var tc = tabT.GetComponent<Canvas>(); var tcol = tabT.GetComponent<BoxCollider>(); var trt = tabT as RectTransform;
                        v.Req(tc != null && tc.renderMode == RenderMode.WorldSpace && tabT.GetComponent<VRCUiShape>() != null && tabT.GetComponent<GraphicRaycaster>() != null && tcol != null && tcol.isTrigger
                            && trt != null && trt.sizeDelta == TabSize && Mathf.Approximately(trt.localScale.x, ControlsScale) && tabT.GetComponentsInChildren<Transform>(true).All(x => x.gameObject.layer == ControlsLayer),
                            c + " " + TabCanvasName + ": WorldSpace Canvas 50x50 @0.001 + VRCUiShape + Raycaster + trigger BoxCollider, layer 17");
                        if (tabBtn != null) EventCheck(v, tabBtn.gameObject, cb, TabEvent, c + " tab"); else v.Req(false, c + " " + TabCanvasName + "/" + TabButtonName);
                    }
                    var tabExpT = ct.Find(TabExpandedName); var tabMinT = cart.transform.Find(TabMinimizedName);
                    v.Req(tabExpT != null && (tabExpT.position - ct.TransformPoint(TabExpandedLocalOnControls())).magnitude < PlacementTolerance && Quaternion.Angle(tabExpT.rotation, ct.rotation) < 0.5f, c + " " + TabExpandedName + " at the controls' top-right corner (±0.02)");
                    v.Req(tabMinT != null && (cart.transform.InverseTransformPoint(tabMinT.position) - TabMinimizedLocal).magnitude < PlacementTolerance && Vector3.Dot(tabMinT.forward, (tabMinT.position - cart.transform.TransformPoint(SeatedEyeLocal)).normalized) > .999f,
                        c + " " + TabMinimizedName + " at " + TabMinimizedLocal.ToString("F2") + " (±0.02), facing the eye");
                    var cso = new SerializedObject(controls);
                    RefCheck(v, cso, cb, "manager", manager, c + " controls");
                    IntCheck(v, cso, cb, "index", i, c + " controls");
                    RefCheck(v, cso, cb, "dock", dock, c + " controls");
                    RefCheck(v, cso, cb, "language", language, c + " controls");
                    RefCheck(v, cso, cb, "panel", board.gameObject, c + " controls");
                    ArrayCheck(v, cso, cb, "buttons", buttons, c + " controls");
                    ArrayCheck(v, cso, cb, "labels", labels, c + " controls");
                    RefCheck(v, cso, cb, "body", bodyT != null ? bodyT.gameObject : null, c + " controls"); RefCheck(v, cso, cb, "tab", tabT != null ? tabT.gameObject : null, c + " controls");
                    RefCheck(v, cso, cb, "tabLabel", tabBtn != null ? Comp<TextMeshProUGUI>(tabBtn.Find("Label")) : null, c + " controls");
                    if (cso.FindProperty("tabExpandedAnchor") != null) RefCheck(v, cso, cb, "tabExpandedAnchor", tabExpT, c + " controls"); else v.Pend(c + " KH_CartControls.tabExpandedAnchor not found (Runtime)");
                    if (cso.FindProperty("tabMinimizedAnchor") != null) RefCheck(v, cso, cb, "tabMinimizedAnchor", tabMinT, c + " controls"); else v.Pend(c + " KH_CartControls.tabMinimizedAnchor not found (Runtime)");
                }
            }
            // ---- 目から見た投影の非重複 (Codex M1.2 レビュー): 操作帯 / Tab / 手元メニュー と モニター、メニュー と 操作帯。保存された姿勢で計算 ----
            var dmT = docks != null ? docks.Find(DockMonitorName) : null; var dnT = docks != null ? docks.Find(DockMenuName) : null;
            if (dmT != null && dnT != null && ct != null)
            {
                Quaternion LocalRot(Transform x) { return Quaternion.Inverse(cart.transform.rotation) * x.rotation; }
                Vector3 LocalPos(Transform x) { return cart.transform.InverseTransformPoint(x.position); }
                var mon = Project(LocalPos(dmT), LocalRot(dmT), MonitorRect());
                var men = Project(LocalPos(dnT), LocalRot(dnT), MenuRect());
                var ctl = Project(LocalPos(ct), LocalRot(ct), ControlsRect());
                var tab = Project(LocalPos(ct), LocalRot(ct), TabRect());
                foreach (var (a, b, what) in new[] { (ctl, mon, "controls vs monitor"), (tab, mon, "tab vs monitor"), (men, mon, "menu vs monitor"), (men, ctl, "menu vs controls") })
                {
                    float g = AngularGap(a, b);
                    v.Req(g >= AngularMarginDeg, c + " seen from the eye " + SeatedEyeLocal + ": " + what + " apart by " + g.ToString("F1") + " deg (>= " + AngularMarginDeg + "; " + a + " / " + b + ")");
                }
            }
            RefCheck(v, seatSo, seatBacking, "controls", controls, c);
            RefCheck(v, seatSo, seatBacking, "dock", dock, c);
            // M2: 操作帯の結果欄と背面の掲示の 1 行
            if (controls != null)
            {
                var cso2 = new SerializedObject(controls); var cb2 = UdonSharpEditorUtility.GetBackingUdonBehaviour(controls);
                var noteT = ct.Find(BoardName + "/" + BodyName + "/" + NoteName);
                if (cso2.FindProperty("noteLabel") != null) RefCheck(v, cso2, cb2, "noteLabel", Comp<TextMeshProUGUI>(noteT), c + " controls"); else v.Pend(c + " KH_CartControls.noteLabel not found (Runtime M2)");
            }
            var model = cart.transform.Find(ModelName);
            var signBack = model != null ? FindDeep(model, AnchorSignBack) : null;
            var cap = signBack != null ? Comp<TextMeshProUGUI>(signBack.Find(SignCaptionName + "/Label")) : null;
            v.Req(cap != null && cap.text == Ja("cart.sign.caption") && cap.gameObject.layer == 0 && cap.GetComponentInParent<GraphicRaycaster>() == null, c + " sign caption under Anchor_SignBack (cart.sign.caption, layer Default, no pointer)");
            if (seatSo.FindProperty("signCaption") != null) RefCheck(v, seatSo, seatBacking, "signCaption", cap, c); else v.Pend(c + " KH_CartSeat.signCaption not found (Runtime M2)");
        }

        /// <summary>長さ 0 (または null) のプロキシ配列は backing に override が無い (null) ことがある。どちらも空なら一致とする (2026-10-12 ベンチ除去で noLateral が 0 本)。</summary>
        static bool BothEmpty(UdonBehaviour b, string name, Array proxy)
        {
            if (proxy != null && proxy.Length > 0) return false;
            object o = b != null ? GetBacking(b, name) : null;
            return o == null || (o is Array a && a.Length == 0);
        }

        static void BackingFloats(V v, UdonBehaviour b, string name, float[] proxy)
        {
            if (BothEmpty(b, name, proxy)) { v.Req(true, "manager." + name + " empty in proxy and backing"); return; }
            bool ok = proxy != null && proxy.Length > 0 && b != null && b.publicVariables.TryGetVariableValue(name, out object o) && o is float[] a && a.Length == proxy.Length;
            int bad = -1;
            if (ok) { var a2 = (float[])GetBacking(b, name); for (int i = 0; i < proxy.Length; i++) if (Mathf.Abs(a2[i] - proxy[i]) > 1e-4f) { bad = i; ok = false; break; } }
            v.Req(ok, "manager." + name + " backing equals proxy element-wise (" + (proxy != null ? proxy.Length : -1) + (bad >= 0 ? ", first diff at " + bad : "") + ")");
        }

        static void BackingBools(V v, UdonBehaviour b, string name, bool[] proxy)
        {
            if (BothEmpty(b, name, proxy)) { v.Req(true, "manager." + name + " empty in proxy and backing"); return; }
            bool ok = proxy != null && proxy.Length > 0 && b != null && b.publicVariables.TryGetVariableValue(name, out object o) && o is bool[] a && a.SequenceEqual(proxy);
            v.Req(ok, "manager." + name + " backing equals proxy element-wise (" + (proxy != null ? proxy.Length : -1) + ")");
        }

        static void BackingVectors(V v, UdonBehaviour b, string name, Vector3[] proxy)
        {
            if (BothEmpty(b, name, proxy)) { v.Req(true, "manager." + name + " empty in proxy and backing"); return; }
            bool ok = proxy != null && proxy.Length > 0 && b != null && b.publicVariables.TryGetVariableValue(name, out object o) && o is Vector3[] a && a.Length == proxy.Length;
            int bad = -1;
            if (ok) { var a2 = (Vector3[])GetBacking(b, name); for (int i = 0; i < proxy.Length; i++) if ((a2[i] - proxy[i]).sqrMagnitude > 1e-8f) { bad = i; ok = false; break; } }
            v.Req(ok, "manager." + name + " backing equals proxy element-wise (" + (proxy != null ? proxy.Length : -1) + (bad >= 0 ? ", first diff at " + bad : "") + ")");
        }

        static object GetBacking(UdonBehaviour b, string name) { b.publicVariables.TryGetVariableValue(name, out object o); return o; }

        static float[] ReadFloats(SerializedObject so, string name)
        {
            var p = so.FindProperty(name); if (p == null || !p.isArray) return null;
            var r = new float[p.arraySize]; for (int i = 0; i < r.Length; i++) r[i] = p.GetArrayElementAtIndex(i).floatValue; return r;
        }
        static bool[] ReadBools(SerializedObject so, string name)
        {
            var p = so.FindProperty(name); if (p == null || !p.isArray) return null;
            var r = new bool[p.arraySize]; for (int i = 0; i < r.Length; i++) r[i] = p.GetArrayElementAtIndex(i).boolValue; return r;
        }
        static Vector3[] ReadVectors(SerializedObject so, string name)
        {
            var p = so.FindProperty(name); if (p == null || !p.isArray) return null;
            var r = new Vector3[p.arraySize]; for (int i = 0; i < r.Length; i++) r[i] = p.GetArrayElementAtIndex(i).vector3Value; return r;
        }
    }
}
