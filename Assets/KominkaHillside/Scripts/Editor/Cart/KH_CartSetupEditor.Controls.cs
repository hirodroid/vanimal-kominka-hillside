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
        public static readonly Vector2 ControlsBoardSize = new Vector2(280f, 300f), ControlsButtonSize = new Vector2(128f, 62f);
        // 並びは KH_CartControls.buttons / labels と同じ (impl-runtime と合意 2026-10-12): 0 Menu, 1 Monitor, 2 SeatUp, 3 SeatDown, 4 TurnLeft, 5 TurnRight, 6 Stop, 7 Accepting
        public static readonly string[] ControlEvents = { "_PressMenu", "_PressMonitor", "_PressSeatUp", "_PressSeatDown", "_PressTurnLeft", "_PressTurnRight", "_PressStop", "_PressAccepting" };
        public static readonly string[] ControlKeys = { "cart.menu", "cart.monitor", "cart.seat.up", "cart.seat.down", "cart.turn.left", "cart.turn.right", "cart.stop", "cart.accepting" };
        public static readonly string[] ControlNames = { "Menu", "Monitor", "SeatUp", "SeatDown", "TurnLeft", "TurnRight", "Stop", "Accepting" };

        /// <summary>座席の真下 (高さ 0) からの水平方向・距離と高さ。</summary>
        public static Vector3 DockMenuLocal(Vector3 seatBase) { return seatBase + new Vector3(-1.1f, 1.0f, 0f); }
        public static Vector3 DockMonitorLocal(Vector3 seatBase) { return seatBase + new Vector3(-1f, 0f, 1f).normalized * 1.2f + Vector3.up * 1.1f; }
        public static Vector3 DockPhotoLocal(Vector3 seatBase) { return seatBase + new Vector3(-1f, 0f, -1f).normalized * 1.0f + Vector3.up * 1.0f; }
        public static Vector3 ControlsLocal(Vector3 seatBase) { return seatBase + new Vector3(-1f, 0f, 1f).normalized * 0.55f + Vector3.up * 0.95f; }

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
                t.localPosition = local; t.localRotation = FaceAway(seatBase, local); t.localScale = Vector3.one;
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
            var eye = seatBase + Vector3.up * (seatLocal.y + SeatedEyeAboveSeat);
            cgo.transform.localPosition = local; cgo.transform.localRotation = Quaternion.LookRotation(local - eye, Vector3.up); cgo.transform.localScale = Vector3.one;
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
            var bg = board.GetComponent<Image>(); if (bg == null) bg = board.AddComponent<Image>(); bg.color = new Color(.14f, .12f, .11f, .92f); bg.raycastTarget = true;
            KD_UiSpriteEditor.ApplyRoundSkin(bg, KD_UiSpriteEditor.LoadRound());

            var buttons = new GameObject[ControlEvents.Length]; var labels = new TextMeshProUGUI[ControlEvents.Length];
            for (int k = 0; k < ControlEvents.Length; k++)
            {
                float x = k % 2 == 0 ? -67f : 67f, y = 105f - (k / 2) * 70f;
                var color = k == 6 ? new Color(.55f, .25f, .20f) : KD_UiSpriteEditor.ActionNeutral;
                var b = KD_ShopSetupEditor.EnsureButton(board.transform, ControlNames[k], new Vector2(x, y), ControlsButtonSize, color, font, Ja(ControlKeys[k]), backing, ControlEvents[k], log);
                var label = b.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                label.fontSize = 18f; label.enableWordWrapping = true; label.enableAutoSizing = true; label.fontSizeMin = 9f; label.fontSizeMax = 18f; label.overflowMode = TextOverflowModes.Overflow;
                EditorUtility.SetDirty(label);
                foreach (var t in b.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ControlsLayer;
                b.SetActive(true);
                buttons[k] = b; labels[k] = label;
            }
            board.SetActive(false);   // 乗っている本人にだけ KH_CartControls._SetVisible が出す

            var cso = new SerializedObject(controls);
            SetRef(cso, "manager", manager); SetInt(cso, "index", i); SetRef(cso, "dock", dock); SetRef(cso, "language", language);
            SetRef(cso, "panel", board); SetRefs(cso, "buttons", buttons); SetRefs(cso, "labels", labels);
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
