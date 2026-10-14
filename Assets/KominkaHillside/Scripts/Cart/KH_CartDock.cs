using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>DES-WLD-004 M1.1: 乗車中に、開いている板をカートの左側へ寄せて一緒に運ぶ (各カートに 1 つ。ローカル専用、同期しない)。
    ///
    /// - 自分がこのカートに乗っている間だけ動く (KH_CartSeat._SetRider → <see cref="_SetRiding"/>。それ以外は enabled = false で LateUpdate も止める)。
    ///   乗車中に限り「板は据えた後は追従しない (2026-09-03)」を上書きする。降りたら以後触らない (置き去り。据え直しは各板の既存操作)。
    /// - 手元メニュー (KD_TreatPouch): `menu.transform` を dockMenu の向きにし、`menu.panelRoot` の中心が dockMenu に来るよう root を戻す
    ///   (KD_TreatPouch.PlaceInFront と同じ実測補正: `position += want - panelRoot.position`)。
    /// - 撮影モニター (KH_VideoCamera): `video.monitorRoot.transform` を dockMonitor の姿勢へ (PlaceMonitor と同じく +Z が見る人から板へ)。
    /// - 合流・呼び出し・撮影許可の通知板 (KH_TravelNotice。BUG-UI-002): `notice.transform` (Board は root の原点) を dockNotice の姿勢へ。
    ///   乗っている間は通知板自身の置き直し (3 m 規則) を <see cref="KH_TravelNotice._SetDocked"/> で止める (書き手はこちらだけ)。降りたら通知板の規則へ戻す。
    /// - 写真ビューア (KD_PhotoViewer): 自分の PostLateUpdate で毎フレーム頭の前へ置き直す作りなので、ここ (LateUpdate) で動かしても上書きされる。
    ///   頭に付いて動くので乗車中も一緒に動く。ドックへは寄せない (dockPhoto / photo は将来用に配線だけ受ける)。
    /// - 板が閉じていれば触らない。既存スクリプトは変更しない (transform を外から動かすだけ。通知板だけは上の停止の呼び出しを受ける)。
    /// - ドックの Transform の向き: +Z = 座っている人から板へ向かう方向 (PlaceInFront・PlaceMonitor と同じ約束)。</summary>
    // KD_TreatPouch.LateUpdate (入力から PlaceInFront を呼ぶ) より後に置き場所を確定する (Codex M1.1 レビュー (5)。U# は DefaultExecutionOrder を
    // UdonBehaviour の実行順へ写す — KD_SleepAudio の -100 と同じ仕組み)
    [DefaultExecutionOrder(100)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_CartDock : UdonSharpBehaviour
    {
        public KominkaDogRun.KD_TreatPouch menu;
        public KH_VideoCamera video;
        public KominkaDogRun.KD_PhotoViewer photo;
        public Transform dockMenu, dockMonitor, dockPhoto;
        [Tooltip("BUG-UI-002: 合流・呼び出し・撮影許可の通知板 (シーンに 1 枚) と、乗車中に置くドック (カートの右前。Cart/1 が配線する)")]
        public KH_TravelNotice notice;
        public Transform dockNotice;
        public KH_CartManager manager;
        public int index;

        private bool _riding;
        private bool _started;
        private GameObject _menuPanel, _monitor, _noticeBoard;
        private Transform _menuT, _menuPanelT, _monitorT, _noticeT;

        void Start()
        {
            _started = true;
            if (!_riding) enabled = false;   // 乗っていない間は LateUpdate を回さない
        }

        /// <summary>このカートにローカルプレイヤーが乗った / 降りた (manager.ApplyState から。冪等)。</summary>
        public void _SetRiding(bool on)
        {
            if (on == _riding) return;
            _riding = on;
            if (on) Cache();
            if (on || _started) enabled = on;
            // 通知板の置き直しを止める / 戻す。ドックが無いカートでは止めない (誰も置かなくなるため)。off はこのカートが止めていたときだけ効く (乗り換え)
            if (notice != null && dockNotice != null) notice._SetDocked(on, index);
            Debug.Log("[KH_Cart] dock cart=" + index + " riding=" + on);
        }

        void LateUpdate()
        {
            if (!_riding) { enabled = false; return; }   // 乗っていないカートのドックは無効 (LateUpdate も来ない)。ここは取りこぼしの保険
            // 取りこぼしの安全網は 30 フレームに 1 回 (毎フレームの他 Behaviour 呼び出しを避ける。降車は _SetRiding(false) が先に来る)
            if (manager != null && Time.frameCount % 30 == index % 30 && manager._MyCart() != index) { _SetRiding(false); return; }
            if (_menuPanel != null && dockMenu != null && _menuPanel.activeSelf)
            {
                Vector3 want = dockMenu.position;
                _menuT.rotation = dockMenu.rotation;
                _menuT.position = want;
                _menuT.position += want - _menuPanelT.position;
            }
            if (_monitor != null && dockMonitor != null && _monitor.activeSelf)
                _monitorT.SetPositionAndRotation(dockMonitor.position, dockMonitor.rotation);
            // 通知板: Board は root の原点 (Course/85) なので root をドックの姿勢へ置けば板の中心がドックに来る。表 (-Z) が座っている人を向く
            if (_noticeBoard != null && dockNotice != null && _noticeBoard.activeSelf)
                _noticeT.SetPositionAndRotation(dockNotice.position, dockNotice.rotation);
        }

        /// <summary>操作帯の [メニュー]。手元メニューを開く (開いていれば手元へ呼び戻す。次のフレームからドックへ寄る)。</summary>
        public void _PressMenu()
        {
            if (menu == null) return;
            menu._Toggle();
            Cache();
        }

        /// <summary>操作帯の [撮影モニター]。3 状態の循環: 閉 → 開く / 開 (表示中) → 板だけ隠す (撮影は続く) / 隠し中 → 再表示 (次のフレームからドックへ)。
        /// PC 専用 (Quest・スマホでは KH_VideoCamera が何もしない)。</summary>
        public void _PressMonitor()
        {
            if (video == null) return;
            Cache();
            if (video._MonitorHidden()) video._ShowMonitor();
            else if (_monitor != null && _monitor.activeSelf) video._HideMonitor();
            else video._Open();
        }

        private void Cache()
        {
            if (_menuPanel == null && menu != null)
            {
                _menuPanel = menu.panelRoot;
                _menuT = menu.transform;
                if (_menuPanel != null) _menuPanelT = _menuPanel.transform;
            }
            if (_monitor == null && video != null)
            {
                _monitor = video.monitorRoot;
                if (_monitor != null) _monitorT = _monitor.transform;
            }
            if (_noticeBoard == null && notice != null)
            {
                _noticeBoard = notice.board;
                _noticeT = notice.transform;
            }
        }
    }
}
