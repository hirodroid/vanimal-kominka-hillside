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
        public static readonly Vector2 ControlsBoardSize = new Vector2(280f, 430f), TabSize = new Vector2(50f, 50f), TabPos = new Vector2(111f, 186f), ControlsButtonSize = new Vector2(128f, 62f);
        // 並びは KH_CartControls.buttons / labels と同じ (ユーザー要望 2026-10-12 で 10 個): 0 メニュー, 1 モニター, 2 高さ▲, 3 高さ▼, 4 前後▲, 5 前後▼, 6 向き◀, 7 向き▶, 8 停止, 9 受付。
        // 2 列 5 行で、行ごとに組 (高さ▲▼ / 前後▲▼ / 向き◀▶) が横に並ぶ = 矢印の組どうしが被らない
        public static readonly string[] ControlEvents = { "_PressMenu", "_PressMonitor", "_PressSeatUp", "_PressSeatDown", "_PressSeatForward", "_PressSeatBack", "_PressTurnLeft", "_PressTurnRight", "_PressStop", "_PressAccepting" };
        public static readonly string[] ControlKeys = { "cart.menu", "cart.monitor", "cart.seat.up", "cart.seat.down", "cart.seat.forward", "cart.seat.back", "cart.turn.left", "cart.turn.right", "cart.stop", "cart.accepting" };
        public static readonly string[] ControlNames = { "Menu", "Monitor", "SeatUp", "SeatDown", "SeatForward", "SeatBack", "TurnLeft", "TurnRight", "Stop", "Accepting" };
        public const int StopControl = 8;
        // 最小化 (2026-10-12): Board の下に Body (10 ボタンと背景) と Tab (Body の外、右上隅 50×50、_PressToggle)。最小化しても Tab だけ残る。
        // Tab のラベルは Runtime が展開中「×」/ 最小化中「≡」と書く (tabLabel)。Setup は初期表示として ≡ (フォントに無ければ -) を置く
        public const string BodyName = "Body", TabName = "Tab", TabEvent = "_PressToggle", TabSymbol = "≡", TabFallback = "-";
        // 5 行 120..-160 (Tab の 161..211 と重ならない)
        public static Vector2 ControlPos(int k) { return new Vector2(k % 2 == 0 ? -67f : 67f, 120f - (k / 2) * 70f); }

        /// <summary>座席の真下 (高さ 0) からの水平方向・距離と高さ。</summary>
        // Play 3 回目 (2026-10-12): 操作帯とモニターが座席から見て被らないよう、カートのローカル座標の固定値に置く (統括指示)。
        // 目 (0, 1.25, -0.26) から: 操作帯 = 左前・低め・近め (俯角約 20°)、モニター = 左約 40°・仰角約 5°・遠め (操作帯と縦に約 25° 離れる)、メニュー = 真左
        public static readonly Vector3 SeatedEyeLocal = new Vector3(0f, 1.25f, -0.26f);
        public const float PlacementTolerance = 0.02f;
        public static Vector3 DockMenuLocal(Vector3 seatBase) { return new Vector3(-1.15f, 1.0f, 0f); }
        public static Vector3 DockMonitorLocal(Vector3 seatBase) { return new Vector3(-0.95f, 1.35f, 0.95f); }
        public static Vector3 DockPhotoLocal(Vector3 seatBase) { return seatBase + new Vector3(-1f, 0f, -1f).normalized * 1.0f + Vector3.up * 1.0f; }
        public static Vector3 ControlsLocal(Vector3 seatBase) { return new Vector3(-0.45f, 0.85f, 0.65f); }
        /// <summary>面を目へ向ける姿勢: +Z = 目 → 板 (Canvas の表 -Z が目を向く。Runtime のドックと同じ約束)。上下の傾きも含む。</summary>
        public static Quaternion FaceEye(Vector3 local) { return Quaternion.LookRotation(local - SeatedEyeLocal, Vector3.up); }

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
            // 旧構成 (Board 直下の 8 / 10 ボタン) から作り直す: Body / Tab 以外の子を消す
            for (int c = board.transform.childCount - 1; c >= 0; c--) { var ch = board.transform.GetChild(c); if (ch.name != BodyName && ch.name != TabName) UnityEngine.Object.DestroyImmediate(ch.gameObject); }
            var body = KD_ShopSetupEditor.EnsureUiChild(board.transform, BodyName, Vector2.zero, ControlsBoardSize); body.SetActive(true); body.layer = ControlsLayer;
            var bg = body.GetComponent<Image>(); if (bg == null) bg = body.AddComponent<Image>(); bg.color = new Color(.14f, .12f, .11f, .92f); bg.raycastTarget = true;
            KD_UiSpriteEditor.ApplyRoundSkin(bg, KD_UiSpriteEditor.LoadRound());
            for (int c = body.transform.childCount - 1; c >= 0; c--) { var ch = body.transform.GetChild(c); if (Array.IndexOf(ControlNames, ch.name) < 0) UnityEngine.Object.DestroyImmediate(ch.gameObject); }
            var buttons = new GameObject[ControlEvents.Length]; var labels = new TextMeshProUGUI[ControlEvents.Length];
            for (int k = 0; k < ControlEvents.Length; k++)
            {
                var color = k == StopControl ? new Color(.55f, .25f, .20f) : KD_UiSpriteEditor.ActionNeutral;
                var b = KD_ShopSetupEditor.EnsureButton(body.transform, ControlNames[k], ControlPos(k), ControlsButtonSize, color, font, Ja(ControlKeys[k]), backing, ControlEvents[k], log);
                var label = b.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                label.fontSize = 18f; label.enableWordWrapping = true; label.enableAutoSizing = true; label.fontSizeMin = 9f; label.fontSizeMax = 18f; label.overflowMode = TextOverflowModes.Overflow;
                EditorUtility.SetDirty(label);
                foreach (var t in b.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ControlsLayer;
                b.SetActive(true);
                buttons[k] = b; labels[k] = label;
            }
            string symbol = KD_LocalizationEditor.HasGlyph(font, TabSymbol[0]) ? TabSymbol : TabFallback;
            var tab = KD_ShopSetupEditor.EnsureButton(board.transform, TabName, TabPos, TabSize, KD_UiSpriteEditor.ActionNeutral, font, symbol, backing, TabEvent, log);
            var tabLabel = tab.transform.Find("Label").GetComponent<TextMeshProUGUI>(); tabLabel.fontSize = 28f; tabLabel.enableAutoSizing = false; EditorUtility.SetDirty(tabLabel);
            foreach (var t in tab.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ControlsLayer;
            tab.SetActive(true); tab.transform.SetAsLastSibling();
            if (i == 0) log.AppendLine("controls tab symbol " + symbol + (symbol == TabFallback ? " (font lacks " + TabSymbol + ")" : "") + (KD_LocalizationEditor.HasGlyph(font, '×') ? "" : "; WARN font lacks × (runtime label)"));
            board.SetActive(false);   // 乗っている本人にだけ KH_CartControls._SetVisible が出す

            var cso = new SerializedObject(controls);
            SetRef(cso, "manager", manager); SetInt(cso, "index", i); SetRef(cso, "dock", dock); SetRef(cso, "language", language);
            SetRef(cso, "panel", board); SetRefs(cso, "buttons", buttons); SetRefs(cso, "labels", labels);
            SetRef(cso, "body", body); SetRef(cso, "tab", tab); SetRef(cso, "tabLabel", tabLabel);
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
