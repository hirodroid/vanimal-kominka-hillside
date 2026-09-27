using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>IMP-WLD-001 段階 3: 合流の通知板 (設計書 §A.4 の T 側・§A.5)。ローカルだけ (同期なし)。シーンに 1 枚。
    ///
    /// 受付箱 (自分の KH_TravelMailbox) が申し込みを受けると <see cref="_Show"/> を呼ぶ。板は目の前 1.1 m・視線の 15° 下に据え、
    /// 本人が 3 m 以上歩いたら置き直す (歩いていても通り過ぎない)。中身は「(名前) さんが近くに来たいそうです」+ 相手の場所
    /// (最寄りの地点の名前) + 残り秒数、[いいよ] [いまはだめ] [しばらく受けない (10分)] [この人からはこの滞在中は受けない]。
    /// 20 秒の時間切れ・申し込みの取り消し・相手の退出では受付箱が <see cref="_Hide"/> を呼ぶ。手元メニューの「申し込み 1 件」から
    /// <see cref="_Reshow"/> でもう一度出せる。表示名は共通フォントにある字だけが出る (欠けた字は □。§A.6 の補助フォントは未導入)。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_TravelNotice : UdonSharpBehaviour
    {
        [Header("Wiring (Tools/KominkaHillside/Course/85 writes these)")]
        public GameObject board;                 // Canvas (VRCUiShape + BoxCollider) — 出している間だけ active
        public KominkaDogRun.KD_Language language;
        public KH_LeashManager manager;
        public TextMeshProUGUI titleLabel, placeLabel, countLabel, yesLabel, noLabel, pauseLabel, blockLabel;
        public AudioSource chime;                // 任意 (null なら鳴らさない)
        public float distance = 1.1f, lowerDegrees = 15f, replaceMeters = 3f;

        // 地点の呼び名 (KH_LeashManager の地点の並び)
        private string[] _spotKeys = { "KD_TreatPouch.WordWalkGate", "KD_TreatPouch.WordWalkPlaza", "course.walk.upper", "course.travel.join.spot.bridgeUp", "course.walk.river", "course.travel.join.spot.bridgeDown" };
        private KH_TravelMailbox _box;
        private bool _showing;
        private Vector3 _placedFrom;
        private float _nextRefresh;

        void Start()
        {
            if (board != null) board.SetActive(false);
        }

        /// <summary>申し込みが届いた。ローカル専用 (受付箱から)。</summary>
        public void _Show(KH_TravelMailbox box)
        {
            _box = box;
            _showing = true;
            if (chime != null) chime.Play();
            Place();
            Refresh();
        }

        public void _Hide()
        {
            _showing = false;
            if (board != null && board.activeSelf) board.SetActive(false);
        }

        /// <summary>手元メニューの「申し込み 1 件」から。保留が残っていれば目の前へ出し直す。ローカル専用。</summary>
        public void _Reshow()
        {
            if (_box == null || _box._IncomingFrom() < 0) return;
            _showing = true;
            Place();
            Refresh();
        }

        public bool _Showing() { return _showing; }

        public void _Yes() { if (_box != null) _box._AnswerYes(); }
        public void _No() { if (_box != null) _box._AnswerNo(); }
        public void _Pause() { if (_box != null) _box._PauseIncoming(); }
        public void _Block() { if (_box != null) _box._BlockIncoming(); }

        void Update()
        {
            if (!_showing) return;
            if (_box == null || _box._IncomingFrom() < 0) { _Hide(); return; }
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (Utilities.IsValid(lp))
            {
                Vector3 d = lp.GetPosition() - _placedFrom; d.y = 0f;
                if (d.magnitude > replaceMeters) Place();
            }
            if (Time.time < _nextRefresh) return;
            _nextRefresh = Time.time + 0.25f;
            Refresh();
        }

        private void Place()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (board == null || !Utilities.IsValid(lp)) return;
            VRCPlayerApi.TrackingData head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 fwd = head.rotation * Vector3.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) { fwd = lp.GetRotation() * Vector3.forward; fwd.y = 0f; }
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd = fwd.normalized;
            float rad = lowerDegrees * Mathf.Deg2Rad;
            Vector3 pos = head.position + fwd * (distance * Mathf.Cos(rad)) - Vector3.up * (distance * Mathf.Sin(rad));
            // 板の前面は -Z。読む人から板への向きで LookRotation する (vrc-world-ui §2)
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position, Vector3.up));
            _placedFrom = lp.GetPosition();
            if (!board.activeSelf) board.SetActive(true);
        }

        private void Refresh()
        {
            if (_box == null) return;
            int from = _box._IncomingFrom();
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(from);
            string name = Utilities.IsValid(p) ? p.displayName : "?";
            if (titleLabel != null) titleLabel.text = T("course.travel.join.notice.title").Replace("{0}", name);
            if (placeLabel != null) placeLabel.text = Utilities.IsValid(p) ? T("course.travel.join.near").Replace("{0}", _NearestSpotName(p.GetPosition())) : "";
            if (countLabel != null) countLabel.text = Mathf.CeilToInt(_box._IncomingLeft()).ToString();
            if (yesLabel != null) yesLabel.text = T("course.travel.join.notice.yes");
            if (noLabel != null) noLabel.text = T("course.travel.join.notice.no");
            if (pauseLabel != null) pauseLabel.text = T("course.travel.join.notice.pause");
            if (blockLabel != null) blockLabel.text = T("course.travel.join.notice.block");
        }

        /// <summary>最寄りの地点の名前 (地図の目印 6 つから)。</summary>
        public string _NearestSpotName(Vector3 p)
        {
            if (manager == null || manager.spotPositions == null) return "";
            int best = -1; float bestD = 1e9f;
            for (int i = 0; i < manager.spotPositions.Length && i < _spotKeys.Length; i++)
            {
                Vector3 d = manager.spotPositions[i] - p; d.y = 0f;
                if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; best = i; }
            }
            return best < 0 ? "" : T(_spotKeys[best]);
        }

        private string T(string key) { return language != null ? language._Text(key) : key; }
    }
}
