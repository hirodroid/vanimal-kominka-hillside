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
    /// <see cref="_Reshow"/> でもう一度出せる。
    /// IMP-WLD-002 (設計書 §F.2): 呼ぶのときは題「(名前) さんが呼んでいます」、場所の行に自分の犬の数、ボタンは別の実体 [行く] (Button4) / [今は行かない] (Button5)。
    /// 見せるのは今の種類の 2 つだけ。押下は描いた世代と一緒に受付箱へ渡し、受付箱が世代・種類・0.5 秒で照合する。[行く] を始められなかった理由は理由の行に出す。名前のラベルは表示名の補助フォント KH_DisplayName SDF (Static。JIS 第 1 水準まで焼いたもの + Latin fallback。§A.6、Course/86) を使う。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_TravelNotice : UdonSharpBehaviour
    {
        [Header("Wiring (Tools/KominkaHillside/Course/85 writes these)")]
        public GameObject board;                 // Canvas (VRCUiShape + BoxCollider) — 出している間だけ active
        public KominkaDogRun.KD_Language language;
        public KH_LeashManager manager;
        public TextMeshProUGUI titleLabel, placeLabel, countLabel, yesLabel, noLabel, pauseLabel, blockLabel;
        [Header("呼ぶ (IMP-WLD-002。Course/85 が配線する)")]
        public GameObject[] joinButtons;         // [いいよ] [いまはだめ] (Button0 / Button1)
        public GameObject[] callButtons;         // [行く] [今は行かない] (Button4 / Button5)
        public TextMeshProUGUI goLabel, notNowLabel, reasonLabel;
        public AudioSource chime;                // 任意 (null なら鳴らさない)
        public float distance = 1.1f, lowerDegrees = 15f, replaceMeters = 3f;

        // 地点の呼び名 (KH_LeashManager の地点の並び)
        private string[] _spotKeys = { "KD_TreatPouch.WordWalkGate", "KD_TreatPouch.WordWalkPlaza", "course.walk.upper", "course.travel.join.spot.bridgeUp", "course.walk.river", "course.travel.join.spot.bridgeDown" };
        private KH_TravelMailbox _box;
        private bool _showing;
        private Vector3 _placedFrom;
        private float _nextRefresh;
        private float _chimedAt = -1000f;
        private int _shownGen = -1, _shownKind = -1;   // 描いた世代と種類 (押下と一緒に受付箱へ渡す)

        void Start()
        {
            if (board != null) board.SetActive(false);
        }

        /// <summary>申し込みが届いた。ローカル専用 (受付箱から)。</summary>
        public void _Show(KH_TravelMailbox box)
        {
            _box = box;
            _showing = true;
            // 音は 1 回。続けて届いても 5 秒は鳴らし直さない (W3 レビュー 15)
            if (chime != null && Time.time - _chimedAt > 5f) { _chimedAt = Time.time; chime.Play(); }
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

        public void _Yes() { if (_box != null) _box._AnswerYesGen(_shownGen); }
        public void _No() { if (_box != null) _box._AnswerNoGen(_shownGen); }
        public void _Go() { if (_box != null) _box._AnswerGoGen(_shownGen); Refresh(); }
        public void _NotNow() { if (_box != null) _box._AnswerNotNowGen(_shownGen); }
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
            // 視線 (頭の向きの上下を含む) から 15° 下 (W3 レビュー 14)。頭の右方向を軸に下へ回す
            Vector3 look = head.rotation * Vector3.forward;
            Vector3 right = head.rotation * Vector3.right;
            Vector3 dir = Quaternion.AngleAxis(lowerDegrees, right) * look;
            if (dir.sqrMagnitude < 0.0001f) dir = lp.GetRotation() * Vector3.forward;
            Vector3 pos = head.position + dir.normalized * distance;
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
            int kind = _box._IncomingKind();
            bool call = kind == KH_TravelMailbox.KindCall;
            if (kind != _shownKind || _box._IncomingGen() != _shownGen)
            {
                // 種類が変わったらボタンの実体を切り替える (古い種類のボタンは押せなくなる)
                SetActive(joinButtons, !call); SetActive(callButtons, call);
                _shownKind = kind; _shownGen = _box._IncomingGen();
            }
            if (titleLabel != null) titleLabel.text = T(call ? "course.travel.call.notice.title" : "course.travel.join.notice.title").Replace("{0}", name);
            string place = Utilities.IsValid(p) ? T("course.travel.join.near").Replace("{0}", _NearestSpotName(p.GetPosition())) : "";
            if (call && manager != null)
            {
                int dogs = 0, mask = manager._TravelMyMask();
                for (int i = 0; i < 6; i++) if ((mask & (1 << i)) != 0) dogs++;
                if (dogs > 0) place += "  " + T("course.travel.map.dogs").Replace("{0}", dogs.ToString());
            }
            if (placeLabel != null) placeLabel.text = place;
            if (goLabel != null) goLabel.text = T("course.travel.call.notice.go");
            if (notNowLabel != null) notNowLabel.text = T("course.travel.call.notice.notnow");
            if (reasonLabel != null) reasonLabel.text = call ? ReasonText(_box._GoFail(), _box._GoPressed()) : "";
            if (countLabel != null) countLabel.text = Mathf.CeilToInt(_box._IncomingLeft()).ToString();
            if (yesLabel != null) yesLabel.text = T("course.travel.join.notice.yes");
            if (noLabel != null) noLabel.text = T("course.travel.join.notice.no");
            if (pauseLabel != null) pauseLabel.text = T("course.travel.join.notice.pause");
            if (blockLabel != null) blockLabel.text = T("course.travel.join.notice.block2");
        }

        /// <summary>[行く] を始められなかった理由 (§F.4)。</summary>
        private string ReasonText(int fail, bool pressed)
        {
            if (pressed) return T("course.travel.map.moving");
            if (fail == KH_TravelMailbox.GoFailNone) return "";
            if (fail == KH_TravelMailbox.GoFailNoSafe) return T("course.travel.join.nosafe");
            if (fail == KH_TravelMailbox.GoFailMoving || fail == KH_LeashManager.TravelBusy) return T("course.travel.busy");
            if (fail == KH_LeashManager.TravelBall) return T("course.travel.ball");
            if (fail == KH_LeashManager.TravelCooling) return T("course.travel.cooling").Replace("{0}", manager != null ? Mathf.CeilToInt(manager._TravelCooldownLeft()).ToString() : "");
            return T("course.travel.unavailable");
        }

        private void SetActive(GameObject[] list, bool on)
        {
            if (list == null) return;
            for (int i = 0; i < list.Length; i++) if (list[i] != null && list[i].activeSelf != on) list[i].SetActive(on);
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
