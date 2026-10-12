using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    /// <summary>M1.1 Editor 2 (implementation.md「M1.1」): 各カートのドック 3 点 (Docks/Dock_Menu・Dock_Monitor・Dock_Photo + KH_CartDock) と
    /// 操作帯 (Controls + KH_CartControls / Board = WorldSpace Canvas、layer 17 Walkthrough、ボタン 8 個)。
    /// 位置は座席 (Anchor_Seat) の真下を原点にしたカートのローカル座標 (+Z 前、-X 左)。
    /// ドックの向き: +Z が座席から外向き (= Canvas の表 -Z が乗員を向く。vrc-world-ui §2 の LookRotation(板 - 読者))。</summary>
    public static partial class KH_CartSetupEditor
    {
        public const string DocksName = "Docks", DockMenuName = "Dock_Menu", DockMonitorName = "Dock_Monitor", DockPhotoName = "Dock_Photo", ControlsName = "Controls";
        public const int ControlsLayer = 17;   // Walkthrough: 既存の撮影モニター・手元メニューと同じ (撮影カメラに映らず、ポインタは届く)
        public const float ControlsScale = 0.001f, SeatedEyeAboveSeat = 0.7f;
        public static readonly Vector2 ControlsBoardSize = new Vector2(280f, 430f), TabSize = new Vector2(50f, 50f), TabPos = new Vector2(111f, 186f), ControlsButtonSize = new Vector2(128f, 54f), GuideButtonSize = new Vector2(262f, 54f);
        // M2: 結果欄 (すれ違い待ち) は Body の左上 (Tab の左)。板の大きさは M1.2 の角度配置のまま (280×430)
        public const string NoteName = "Note";
        public static readonly Vector2 NotePos = new Vector2(-30f, 186f), NoteSize = new Vector2(205f, 50f);
        // 並びは KH_CartControls.buttons / labels と同じ (ユーザー要望 2026-10-12 で 10 個): 0 メニュー, 1 モニター, 2 高さ▲, 3 高さ▼, 4 前後▲, 5 前後▼, 6 向き◀, 7 向き▶, 8 停止, 9 受付。
        // 2 列 5 行で、行ごとに組 (高さ▲▼ / 前後▲▼ / 向き◀▶) が横に並ぶ = 矢印の組どうしが被らない
        public static readonly string[] ControlEvents = { "_PressMenu", "_PressMonitor", "_PressSeatUp", "_PressSeatDown", "_PressSeatForward", "_PressSeatBack", "_PressTurnLeft", "_PressTurnRight", "_PressStop", "_PressAccepting", "_PressGuide" };   // M2: 11 個目 = 案内
        public static readonly string[] ControlKeys = { "cart.menu", "cart.monitor", "cart.seat.up", "cart.seat.down", "cart.seat.forward", "cart.seat.back", "cart.turn.left", "cart.turn.right", "cart.stop", "cart.accepting", "cart.guide" };
        public static readonly string[] ControlNames = { "Menu", "Monitor", "SeatUp", "SeatDown", "SeatForward", "SeatBack", "TurnLeft", "TurnRight", "Stop", "Accepting", "Guide" };
        public const int StopControl = 8, GuideControl = 10;
        // 最小化 (2026-10-12): Board の下に Body (10 ボタンと背景) と Tab (Body の外、右上隅 50×50、_PressToggle)。最小化しても Tab だけ残る。
        // Tab のラベルは Runtime が展開中「×」/ 最小化中「+」と書く (tabLabel)。Setup は初期表示として ≡ (フォントに無ければ -) を置く
        public const string BodyName = "Body", TabName = "Tab", TabEvent = "_PressToggle", TabSymbol = "+", TabFallback = "-";
        // 2026-10-12: タブは独立した小さな Canvas (カート直下の TabCanvas)。Runtime が展開中は TabExpanded (操作帯の右上角)、最小化中は TabMinimized (左肘掛けの前上) の姿勢へ置く
        public const string TabCanvasName = "TabCanvas", TabButtonName = "Button", TabExpandedName = "TabExpanded", TabMinimizedName = "TabMinimized";
        public static readonly Vector3 TabMinimizedLocal = new Vector3(-0.30f, 0.86f, 0.10f);
        public const float TabFrontOffset = 0.005f;   // 展開中は操作帯の面より目の側へ 5 mm (Body の背景と重ならない・ポインタが先に当たる)
        public static Vector3 TabExpandedLocalOnControls() { return new Vector3(TabPos.x * ControlsScale, TabPos.y * ControlsScale, -TabFrontOffset); }
        // 5 行 120..-160 (Tab の 161..211 と重ならない)
        public static Vector2 ControlPos(int k) { return k == GuideControl ? new Vector2(0f, -170f) : new Vector2(k % 2 == 0 ? -67f : 67f, 130f - (k / 2) * 60f); }   // 6 行 130..-170 (Note/Tab の 161..211 と重ならない)
        public static Vector2 ControlSize(int k) { return k == GuideControl ? GuideButtonSize : ControlsButtonSize; }

        /// <summary>座席の真下 (高さ 0) からの水平方向・距離と高さ。</summary>
        // Play 3 回目 (2026-10-12): 操作帯とモニターが座席から見て被らないよう、カートのローカル座標の固定値に置く (統括指示)。
        // Codex M1.2 レビュー (2026-10-12): 目 (0, 1.25, -0.26) から各板の外周 (モニター = 本体 840 + パターン板 500 px、手元メニュー 1140×700、操作帯 280×430 と Tab) を
        // 方位角・仰角へ投影し、どの組も 5° 以上離れる配置を探索で決めた (操作帯の下端は肘掛け上面 0.75 m より上)。
        // 余白: 操作帯-モニター 6.6° / Tab-モニター 6.8° / メニュー-モニター 7.2° / メニュー-操作帯 12.3°。座面の調整 (±30 cm・前後) で目が動くと余白は減る
        public static readonly Vector3 SeatedEyeLocal = new Vector3(0f, 1.25f, -0.26f);
        public const float PlacementTolerance = 0.02f;
        public static Vector3 DockMenuLocal(Vector3 seatBase) { return new Vector3(-1.15f, 1.0f, 0f); }
        public static Vector3 DockMonitorLocal(Vector3 seatBase) { return new Vector3(-0.70f, 1.80f, 1.15f); }
        public static Vector3 DockPhotoLocal(Vector3 seatBase) { return seatBase + new Vector3(-1f, 0f, -1f).normalized * 1.0f + Vector3.up * 1.0f; }
        public static Vector3 ControlsLocal(Vector3 seatBase) { return new Vector3(-0.40f, 1.00f, 0.50f); }
        /// <summary>面を目へ向ける姿勢: +Z = 目 → 板 (Canvas の表 -Z が目を向く。Runtime のドックと同じ約束)。上下の傾きも含む。</summary>
        public static Quaternion FaceEye(Vector3 local) { return Quaternion.LookRotation(local - SeatedEyeLocal, Vector3.up); }

        // ---- 目から見た板の角度範囲 (Verify と配置探索が使う) ----
        public const float AngularMarginDeg = 5f;
        public struct AngleBox { public float az0, az1, el0, el1; public override string ToString() { return "az " + az0.ToString("F1") + ".." + az1.ToString("F1") + " el " + el0.ToString("F1") + ".." + el1.ToString("F1"); } }
        /// <summary>板の局所矩形 (x0..x1, y0..y1 m、Canvas の中心が原点) を、FaceEye の姿勢で local に置いたときの目からの方位角 (右 +) と仰角 (上 +) の範囲。外周を 8 分割で標本化。</summary>
        public static AngleBox Project(Vector3 local, Quaternion rot, Rect rect)
        {
            var b = new AngleBox { az0 = 1e9f, az1 = -1e9f, el0 = 1e9f, el1 = -1e9f };
            for (int i = 0; i <= 8; i++) for (int j = 0; j <= 8; j++)
            {
                if (i % 8 != 0 && j % 8 != 0) continue;
                var p = local + rot * new Vector3(Mathf.Lerp(rect.xMin, rect.xMax, i / 8f), Mathf.Lerp(rect.yMin, rect.yMax, j / 8f), 0f) - SeatedEyeLocal;
                float az = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg, el = Mathf.Atan2(p.y, new Vector2(p.x, p.z).magnitude) * Mathf.Rad2Deg;
                b.az0 = Mathf.Min(b.az0, az); b.az1 = Mathf.Max(b.az1, az); b.el0 = Mathf.Min(b.el0, el); b.el1 = Mathf.Max(b.el1, el);
            }
            return b;
        }
        /// <summary>2 つの角度範囲の隔たり (度、正 = 離れている)。方位か仰角のどちらかで離れていればよい。</summary>
        public static float AngularGap(AngleBox a, AngleBox b) { return Mathf.Max(Mathf.Max(b.az0 - a.az1, a.az0 - b.az1), Mathf.Max(b.el0 - a.el1, a.el0 - b.el1)); }
        /// <summary>板の実寸 (m): モニター = 本体 + 右のパターン板 (KH_VideoCameraEditor)、手元メニュー = KD_TreatPouch の板 (中心がドック)、操作帯 = Body、Tab。</summary>
        public static Rect MonitorRect() { float px = KH_VideoCameraEditor.Px; return Rect.MinMaxRect(-KH_VideoCameraEditor.CanvasPx.x * .5f * px, -KH_VideoCameraEditor.CanvasPx.y * .5f * px,
            (KH_VideoCameraEditor.PatOffsetPxX + KH_VideoCameraEditor.PatCanvasPx.x * .5f) * px, Mathf.Max(KH_VideoCameraEditor.CanvasPx.y, KH_VideoCameraEditor.PatCanvasPx.y) * .5f * px); }
        public static Rect MenuRect() { return Rect.MinMaxRect(-KD_TreatSetupEditor.PanelWidth * .5f * .001f, -KD_TreatSetupEditor.PanelHeight * .5f * .001f, KD_TreatSetupEditor.PanelWidth * .5f * .001f, KD_TreatSetupEditor.PanelHeight * .5f * .001f); }
        public static Rect ControlsRect() { return Rect.MinMaxRect(-ControlsBoardSize.x * .5f * ControlsScale, -ControlsBoardSize.y * .5f * ControlsScale, ControlsBoardSize.x * .5f * ControlsScale, ControlsBoardSize.y * .5f * ControlsScale); }
        public static Rect TabRect() { return Rect.MinMaxRect((TabPos.x - TabSize.x * .5f) * ControlsScale, (TabPos.y - TabSize.y * .5f) * ControlsScale, (TabPos.x + TabSize.x * .5f) * ControlsScale, (TabPos.y + TabSize.y * .5f) * ControlsScale); }

        static Quaternion FaceAway(Vector3 from, Vector3 pos)
        {
            var d = pos - from; d.y = 0f;
            return Quaternion.LookRotation(d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.left, Vector3.up);
        }

        /// <summary>カート 1 台分のドックと操作帯。戻り値は (dock, controls)。</summary>
        static void BuildCartUi(GameObject cart, int i, Transform seatT, KH_CartManager manager, KD_Language language, KD_TreatPouch menu, KH_VideoCamera video, KD_PhotoViewer photo,
            TMP_FontAsset font, StringBuilder log, out KH_CartDock dock, out KH_CartControls controls)
        {
            var seatLocal = cart.transform.InverseTransformPoint(seatT.position);
            var seatBase = new Vector3(seatLocal.x, 0f, seatLocal.z);

            // ---- ドック ----
            var docksGo = EnsureChild(cart.transform, DocksName); Reset(docksGo.transform); docksGo.layer = 0;
            Transform Dock(string name, Vector3 local)
            {
                var t = EnsureChild(docksGo.transform, name).transform;
                t.localPosition = local; t.localRotation = name == DockPhotoName ? FaceAway(seatBase, local) : FaceEye(local); t.localScale = Vector3.one;
                return t;
            }
            var dMenu = Dock(DockMenuName, DockMenuLocal(seatBase));
            var dMonitor = Dock(DockMonitorName, DockMonitorLocal(seatBase));
            var dPhoto = Dock(DockPhotoName, DockPhotoLocal(seatBase));
            dock = EnsureU<KH_CartDock>(docksGo);
            Backing(dock, Networking.SyncType.None);
            var dso = new SerializedObject(dock);
            SetRef(dso, "manager", manager); SetInt(dso, "index", i);
            SetRef(dso, "menu", menu); SetRef(dso, "video", video); SetRef(dso, "photo", photo);
            SetRef(dso, "dockMenu", dMenu); SetRef(dso, "dockMonitor", dMonitor); SetRef(dso, "dockPhoto", dPhoto);
            dso.ApplyModifiedPropertiesWithoutUndo();
            UdonSharpEditorUtility.CopyProxyToUdon(dock); EditorUtility.SetDirty(dock);

            // ---- 操作帯 ----
            var cgo = EnsureChild(cart.transform, ControlsName);
            cgo.layer = ControlsLayer; cgo.SetActive(true);
            var local = ControlsLocal(seatBase);
            cgo.transform.localPosition = local; cgo.transform.localRotation = FaceEye(local); cgo.transform.localScale = Vector3.one;
            controls = EnsureU<KH_CartControls>(cgo);
            var backing = Backing(controls, Networking.SyncType.None);

            var boardT = cgo.transform.Find(BoardName);
            var board = boardT != null ? boardT.gameObject : new GameObject(BoardName, typeof(RectTransform));
            board.transform.SetParent(cgo.transform, false); board.layer = ControlsLayer; board.SetActive(true);
            var brt = (RectTransform)board.transform; brt.sizeDelta = ControlsBoardSize; brt.localPosition = Vector3.zero; brt.localRotation = Quaternion.identity; brt.localScale = Vector3.one * ControlsScale;
            var canvas = board.GetComponent<Canvas>(); if (canvas == null) canvas = board.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            if (board.GetComponent<GraphicRaycaster>() == null) board.AddComponent<GraphicRaycaster>();
            if (board.GetComponent<VRCUiShape>() == null) board.AddComponent<VRCUiShape>();
            var col = board.GetComponent<BoxCollider>(); if (col == null) col = board.AddComponent<BoxCollider>(); col.center = Vector3.zero; col.size = new Vector3(ControlsBoardSize.x, ControlsBoardSize.y, 2f); col.isTrigger = true;
            // 背景は Body が持つ (最小化で消える)。Board 自身の Image は外す
            var oldBg = board.GetComponent<Image>(); if (oldBg != null) UnityEngine.Object.DestroyImmediate(oldBg);
            // 旧構成 (Board 直下の 8 / 10 ボタン、Board の子だった Tab) から作り直す: Body 以外の子を消す
            for (int c = board.transform.childCount - 1; c >= 0; c--) { var ch = board.transform.GetChild(c); if (ch.name != BodyName) UnityEngine.Object.DestroyImmediate(ch.gameObject); }
            var body = KD_ShopSetupEditor.EnsureUiChild(board.transform, BodyName, Vector2.zero, ControlsBoardSize); body.SetActive(true); body.layer = ControlsLayer;
            var bg = body.GetComponent<Image>(); if (bg == null) bg = body.AddComponent<Image>(); bg.color = new Color(.14f, .12f, .11f, .92f); bg.raycastTarget = true;
            KD_UiSpriteEditor.ApplyRoundSkin(bg, KD_UiSpriteEditor.LoadRound());
            for (int c = body.transform.childCount - 1; c >= 0; c--) { var ch = body.transform.GetChild(c); if (Array.IndexOf(ControlNames, ch.name) < 0 && ch.name != NoteName) UnityEngine.Object.DestroyImmediate(ch.gameObject); }
            var buttons = new GameObject[ControlEvents.Length]; var labels = new TextMeshProUGUI[ControlEvents.Length];
            for (int k = 0; k < ControlEvents.Length; k++)
            {
                var color = k == StopControl ? new Color(.55f, .25f, .20f) : KD_UiSpriteEditor.ActionNeutral;
                var b = KD_ShopSetupEditor.EnsureButton(body.transform, ControlNames[k], ControlPos(k), ControlSize(k), color, font, Ja(ControlKeys[k]), backing, ControlEvents[k], log);
                var label = b.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                label.fontSize = 18f; label.enableWordWrapping = true; label.enableAutoSizing = true; label.fontSizeMin = 9f; label.fontSizeMax = 18f; label.overflowMode = TextOverflowModes.Overflow;
                EditorUtility.SetDirty(label);
                foreach (var t in b.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ControlsLayer;
                b.SetActive(true);
                buttons[k] = b; labels[k] = label;
            }
            string symbol = KD_LocalizationEditor.HasGlyph(font, TabSymbol[0]) ? TabSymbol : TabFallback;
            // アンカー 2 つ: 展開中 = 操作帯の右上角 (Controls の子)、最小化中 = 左肘掛けの前上 (カート直下、面は目へ)
            var tabExpanded = EnsureChild(cgo.transform, TabExpandedName).transform;
            tabExpanded.localPosition = TabExpandedLocalOnControls(); tabExpanded.localRotation = Quaternion.identity; tabExpanded.localScale = Vector3.one; tabExpanded.gameObject.layer = ControlsLayer;
            var tabMinimized = EnsureChild(cart.transform, TabMinimizedName).transform;
            tabMinimized.localPosition = TabMinimizedLocal; tabMinimized.localRotation = FaceEye(TabMinimizedLocal); tabMinimized.localScale = Vector3.one; tabMinimized.gameObject.layer = ControlsLayer;
            // タブの Canvas (カート直下、50×50 px、scale 0.001、VRCUiShape、BoxCollider trigger、layer 17)。初期姿勢は展開中のアンカー
            var tabGo = EnsureChild(cart.transform, TabCanvasName);
            if (tabGo.GetComponent<RectTransform>() == null) { UnityEngine.Object.DestroyImmediate(tabGo); tabGo = new GameObject(TabCanvasName, typeof(RectTransform)); tabGo.transform.SetParent(cart.transform, false); }
            tabGo.layer = ControlsLayer;
            var trt = (RectTransform)tabGo.transform; trt.sizeDelta = TabSize; trt.localScale = Vector3.one * ControlsScale;
            tabGo.transform.SetPositionAndRotation(tabExpanded.position, tabExpanded.rotation);
            var tcanvas = tabGo.GetComponent<Canvas>(); if (tcanvas == null) tcanvas = tabGo.AddComponent<Canvas>(); tcanvas.renderMode = RenderMode.WorldSpace;
            if (tabGo.GetComponent<GraphicRaycaster>() == null) tabGo.AddComponent<GraphicRaycaster>();
            if (tabGo.GetComponent<VRCUiShape>() == null) tabGo.AddComponent<VRCUiShape>();
            var tcol = tabGo.GetComponent<BoxCollider>(); if (tcol == null) tcol = tabGo.AddComponent<BoxCollider>(); tcol.center = Vector3.zero; tcol.size = new Vector3(TabSize.x, TabSize.y, 2f); tcol.isTrigger = true;
            for (int c = tabGo.transform.childCount - 1; c >= 0; c--) { var ch = tabGo.transform.GetChild(c); if (ch.name != TabButtonName) UnityEngine.Object.DestroyImmediate(ch.gameObject); }
            var tabButton = KD_ShopSetupEditor.EnsureButton(tabGo.transform, TabButtonName, Vector2.zero, TabSize, KD_UiSpriteEditor.ActionNeutral, font, symbol, backing, TabEvent, log);
            var tabLabel = tabButton.transform.Find("Label").GetComponent<TextMeshProUGUI>(); tabLabel.fontSize = 28f; tabLabel.enableAutoSizing = false; EditorUtility.SetDirty(tabLabel);
            foreach (var t in tabGo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ControlsLayer;
            tabButton.SetActive(true);
            tabGo.SetActive(false);   // 乗っている本人にだけ Runtime が出す
            var tab = tabGo;
            if (i == 0) log.AppendLine("controls tab symbol " + symbol + (symbol == TabFallback ? " (font lacks " + TabSymbol + ")" : "") + (KD_LocalizationEditor.HasGlyph(font, '×') ? "" : "; WARN font lacks × (runtime label)"));
            board.SetActive(false);   // 乗っている本人にだけ KH_CartControls._SetVisible が出す

            var cso = new SerializedObject(controls);
            SetRef(cso, "manager", manager); SetInt(cso, "index", i); SetRef(cso, "dock", dock); SetRef(cso, "language", language);
            SetRef(cso, "panel", board); SetRefs(cso, "buttons", buttons); SetRefs(cso, "labels", labels);
            SetRef(cso, "body", body); SetRef(cso, "tab", tab); SetRef(cso, "tabLabel", tabLabel);
            var tabLog = new List<string>(); SetRefIfPresent(cso, "tabExpandedAnchor", tabExpanded, tabLog); SetRefIfPresent(cso, "tabMinimizedAnchor", tabMinimized, tabLog);
            if (i == 0) foreach (var line in tabLog) log.AppendLine(line);
            // M2: 結果欄 1 行 (cart.result.waiting 等。Runtime が書く。初期は空)
            var note = MakeText(body.transform, NoteName, NotePos, NoteSize, 14f, font, "");
            note.alignment = TextAlignmentOptions.Left; note.enableWordWrapping = true; note.enableAutoSizing = true; note.fontSizeMin = 9f; note.fontSizeMax = 14f; note.color = new Color(1f, .85f, .55f);
            note.gameObject.layer = ControlsLayer; EditorUtility.SetDirty(note);
            var noteLog = new List<string>(); SetRefIfPresent(cso, "noteLabel", note, noteLog);
            if (i == 0) foreach (var line in noteLog) log.AppendLine(line);
            cso.ApplyModifiedPropertiesWithoutUndo();
            UdonSharpEditorUtility.CopyProxyToUdon(controls); EditorUtility.SetDirty(controls);
        }

        static T SingleOrNull<T>(string what, List<string> log) where T : UnityEngine.Object
        {
            var all = UnityEngine.Object.FindObjectsOfType<T>(true);
            if (all.Length == 1) return all[0];
            log.Add("WARN " + what + " count " + all.Length + " (expected 1); left null on the cart docks");
            return null;
        }
    }
}
