using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>DES-WLD-004 M1.1: カート左斜め前の操作帯 (各カートに 1 つ。ローカル専用、同期しない)。
    /// Canvas (panel) は Setup が layer 17 (Walkthrough。撮影カメラに映らない) で作る。自分がこのカートに乗っている間だけ表示する。
    /// ボタンの並び (buttons / labels、長さ 8): 0 メニュー / 1 撮影モニター / 2 座面↑ / 3 座面↓ / 4 向き← / 5 向き→ / 6 停止 / 7 合流の受付。
    /// 受付はマスターのときだけ押せる (Button.interactable)。撮影モニターは PC 専用 (Quest・スマホ・未配線では隠す)。
    /// manager.ApplyState → KH_CartSeat._SetRider から <see cref="_Refresh"/>、言語の変化で <see cref="_RefreshText"/>。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_CartControls : UdonSharpBehaviour
    {
        public KH_CartManager manager;
        public int index;
        public KH_CartDock dock;
        public KominkaDogRun.KD_Language language;
        public GameObject panel;                // Controls Canvas (_SetVisible で切り替え)
        public GameObject[] buttons;            // 長さ 8 (上の並び)
        public TextMeshProUGUI[] labels;        // 長さ 8

        public const int BtnMenu = 0, BtnMonitor = 1, BtnSeatUp = 2, BtnSeatDown = 3, BtnTurnLeft = 4, BtnTurnRight = 5, BtnStop = 6, BtnAccepting = 7;

        private int _visible = -1;    // -1 = まだ書いていない
        private int _master = -1;
        private int _accepting = -1;
        private Button _acceptButton;

        void Start()
        {
            if (_visible < 0) _SetVisible(false);   // manager が先に書いていればそのまま (Start の順は保証されない)
        }

        /// <summary>乗っているか・マスターか・受付の状態から表示を作り直す (冪等。変わったところだけ書く)。</summary>
        public void _Refresh()
        {
            bool mine = manager != null && manager._MyCart() == index;
            _SetVisible(mine);
            if (!mine) return;
            bool master = manager._IsMaster();
            bool accepting = manager._GroupAccepting(manager._MyGroup());
            int m = master ? 1 : 0, a = accepting ? 1 : 0;
            if (m == _master && a == _accepting) return;
            _master = m; _accepting = a;
            if (_acceptButton == null && buttons != null && buttons.Length > BtnAccepting && buttons[BtnAccepting] != null)
                _acceptButton = buttons[BtnAccepting].GetComponent<Button>();
            if (_acceptButton != null) _acceptButton.interactable = master;
            _RefreshText();
        }

        public void _SetVisible(bool on)
        {
            int want = on ? 1 : 0;
            if (want == _visible) return;
            _visible = want;
            _master = -1; _accepting = -1;   // 次に出すとき文言を書き直す
            if (panel != null && panel.activeSelf != on) panel.SetActive(on);
            if (on && buttons != null && buttons.Length > BtnMonitor && buttons[BtnMonitor] != null)
            {
                bool monitor = dock != null && dock.video != null;
#if UNITY_ANDROID || UNITY_IOS
                monitor = false;   // 撮影カメラは PC 専用 (IMP-WLD-008)
#endif
                if (buttons[BtnMonitor].activeSelf != monitor) buttons[BtnMonitor].SetActive(monitor);
            }
        }

        public void _RefreshText()
        {
            if (labels == null || language == null) return;
            SetLabel(BtnMenu, T("cart.menu"));
            SetLabel(BtnMonitor, T("cart.monitor"));
            SetLabel(BtnSeatUp, T("cart.seat.up"));
            SetLabel(BtnSeatDown, T("cart.seat.down"));
            SetLabel(BtnTurnLeft, T("cart.turn.left"));
            SetLabel(BtnTurnRight, T("cart.turn.right"));
            SetLabel(BtnStop, T("cart.stop"));
            SetLabel(BtnAccepting, T("cart.accepting") + ": " + T(_accepting == 0 ? "cart.accepting.off" : "cart.accepting.on"));
        }

        // ---- Button.onClick → SendCustomEvent (引数なし。ローカル専用なので `_` 付き) ----
        public void _PressMenu() { if (dock != null && Riding()) dock._PressMenu(); }
        public void _PressMonitor() { if (dock != null && Riding()) dock._PressMonitor(); }
        public void _PressSeatUp() { if (Riding()) manager._SeatUp(); }
        public void _PressSeatDown() { if (Riding()) manager._SeatDown(); }
        public void _PressTurnLeft() { if (Riding()) manager._TurnLeft(); }
        public void _PressTurnRight() { if (Riding()) manager._TurnRight(); }
        public void _PressStop() { if (Riding()) manager._Leave(); }
        public void _PressAccepting() { if (Riding()) manager._ToggleAccepting(); }

        private bool Riding() { return manager != null && manager._MyCart() == index; }

        private void SetLabel(int i, string text)
        {
            if (i < labels.Length && labels[i] != null) labels[i].text = text;
        }

        private string T(string key) { return language != null ? language._Text(key) : key; }
    }
}
