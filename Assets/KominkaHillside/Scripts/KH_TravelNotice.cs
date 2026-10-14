using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>IMP-WLD-001 段階 3: 合流の通知板 (設計書 §A.4 の T 側・§A.5)。ローカルだけ (同期なし)。シーンに 1 枚。
    ///
    /// 受付箱 (自分の KH_TravelMailbox) が申し込みを受けると <see cref="_Show"/> を呼ぶ。板は目の前 1.1 m・視線の 15° 下に据え、
    /// 本人が 3 m 以上歩いたら置き直す (歩いていても通り過ぎない)。BUG-UI-002: おさんぽカートの乗車中は KH_CartDock が板をカートの右前のドックへ
    /// 毎フレーム運ぶので、その間 (<see cref="_SetDocked"/>) はここで置かない (出す・隠す・中身の更新は従来どおりここ)。中身は「(名前) さんが近くに来たいそうです」+ 相手の場所
    /// (最寄りの地点の名前) + 残り秒数、[いいよ] [いまはだめ] [しばらく受けない (10分)] [この人からはこの滞在中は受けない]。
    /// 20 秒の時間切れ・申し込みの取り消し・相手の退出では受付箱が <see cref="_Hide"/> を呼ぶ。手元メニューの「申し込み 1 件」から
    /// <see cref="_Reshow"/> でもう一度出せる。
    /// IMP-WLD-002 (設計書 §F.2): 呼ぶのときは題「(名前) さんが呼んでいます」、場所の行に自分の犬の数、ボタンは別の実体 [行く] (Button4) / [今は行かない] (Button5)。
    /// IMP-WLD-008 W4 (動画カメラ §7.4・§13.6): 撮影の種類は 3 つめの組 [いいよ] [いまはだめ] (Button6 / Button7 → _FilmYes / _FilmNo)、見出しは撮影の依頼、
    /// 場所の行は「映像は相手の画面だけ」。[いいよ] の後の 4 秒は保留とは別の状態 (_afterUntil) で「許可しました」と止め方を出す (ボタンなし)。
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
        [Header("撮影の許可 (IMP-WLD-008 W4。Video/4 が配線する)")]
        public GameObject[] filmButtons;         // [いいよ] [いまはだめ] (Button6 / Button7。撮影の種類だけ出す)
        public TextMeshProUGUI filmYesLabel, filmNoLabel;
        [Tooltip("どの種類でも出す [しばらく受けない] [この人は退室まで受けない] (Button2 / Button3)。いいよの後の 4 秒の表示では隠す")]
        public GameObject[] commonButtons;
        public AudioSource chime;                // 任意 (null なら鳴らさない)

        // 撮影の形の文言。W5 (2026-10-08): 表示は UITranslations.json の film.* (13 言語) を TJa(key, ja) で引く。下の ja は表に無いときの既定
        // (Video/2・Video/5 が表の ja と一致を照合する)。[いいよ] [いまはだめ] は合流の翻訳済みのキーを使う
        public const string WordFilmTitle = "{0} さんが撮影カメラであなたを撮りたいそうです";
        public const string WordFilmPlace = "映像は撮る人の画面だけに出ます";   // 場所の行は共通フォント (表示名の補助フォントではない) なので名前を入れない
        public const string WordFilmGranted = "{0} さんに撮影を許可しました";
        public const string WordFilmGrantedNote = "さんぽタブの [撮影を許可中] からいつでもやめてもらえます";
        public const string KeyFilmTitle = "film.notice", KeyFilmPlace = "film.notice.place", KeyFilmGranted = "film.granted", KeyFilmGrantedNote = "film.granted.note";
        public float distance = 1.1f, lowerDegrees = 15f, replaceMeters = 3f;

        // 地点の呼び名 (KH_LeashManager の地点の並び)
        private string[] _spotKeys = { "KD_TreatPouch.WordWalkGate", "KD_TreatPouch.WordWalkPlaza", "course.walk.upper", "course.travel.join.spot.bridgeUp", "course.walk.river", "course.travel.join.spot.bridgeDown" };
        private KH_TravelMailbox _box;
        private bool _showing;
        private Vector3 _placedFrom;
        private float _nextRefresh;
        private float _chimedAt = -1000f;
        private int _shownGen = -1, _shownKind = -1;   // 描いた世代と種類 (押下と一緒に受付箱へ渡す)
        private float _afterUntil;   // 撮影の [いいよ] の後の 4 秒の表示 (保留は消えているので、保留とは別の状態。§13.6)
        // BUG-UI-002: 乗車中はカートのドック (KH_CartDock.dockNotice) が毎フレーム置くので、ここは Place() しない (書き手を 1 つにする)。
        // 値 = ドックしているカートの番号 (-1 = ドックなし)。乗り換えで ApplyState が「新しいカートの on → 古いカートの off」の順に呼んでも外れないよう、番号で持つ
        private int _dockCart = -1;

        void Start()
        {
            if (board != null) board.SetActive(false);
        }

        /// <summary>申し込みが届いた。ローカル専用 (受付箱から)。</summary>
        public void _Show(KH_TravelMailbox box)
        {
            _box = box;
            _showing = true;
            _afterUntil = 0f; _shownKind = -1;   // いいよの後の表示の途中なら、ボタンの組を出し直す
            // 音は 1 回。続けて届いても 5 秒は鳴らし直さない (W3 レビュー 15)
            if (chime != null && Time.time - _chimedAt > 5f) { _chimedAt = Time.time; chime.Play(); }
            PlaceOrOpen();
            Refresh();
        }

        public void _Hide()
        {
            _showing = false;
            if (board != null && board.activeSelf) board.SetActive(false);
        }

        /// <summary>撮影の [いいよ] の後、4 秒だけ「(F) さんに撮影を許可しました」と止め方を出す。ボタンは出さない。「撮影中」はここでは言わない
        /// (相手の申告がそろうまで出さない。§13.7)。ローカル専用 (受付箱から)。</summary>
        public void _ShowFilmGranted(int fromId)
        {
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(fromId);
            string name = Utilities.IsValid(p) ? p.displayName : "?";
            _showing = true; _afterUntil = Time.time + 4f;
            SetActive(joinButtons, false); SetActive(callButtons, false); SetActive(filmButtons, false); SetActive(commonButtons, false);
            _shownKind = -1; _shownGen = -1;
            if (titleLabel != null) titleLabel.text = TJa(KeyFilmGranted, WordFilmGranted).Replace("{0}", name);
            if (placeLabel != null) placeLabel.text = TJa(KeyFilmGrantedNote, WordFilmGrantedNote);
            if (countLabel != null) countLabel.text = "";
            if (reasonLabel != null) reasonLabel.text = "";
            PlaceOrOpen();
        }

        public void _FilmYes() { if (_box != null) _box._AnswerFilmYesGen(_shownGen); }
        public void _FilmNo() { if (_box != null) _box._AnswerFilmNoGen(_shownGen); }

        /// <summary>手元メニューの「申し込み 1 件」から。保留が残っていれば目の前へ出し直す。ローカル専用。</summary>
        public void _Reshow()
        {
            if (_box == null || _box._IncomingFrom() < 0) return;
            _showing = true;
            PlaceOrOpen();
            Refresh();
        }

        public bool _Showing() { return _showing; }

        /// <summary>BUG-UI-002: カートのドック (KH_CartDock._SetRiding) から。on = このカートに乗った (以後ドックが板を置く)、
        /// off = 降りた (そのカートがドックしていたときだけ外す)。外した後は従来の 3 m 規則へ戻る (板はその場に残り、
        /// 最後に Place() した地点から 3 m 以上離れていれば次の Update で目の前へ置き直す)。ローカル専用。冪等。</summary>
        public void _SetDocked(bool on, int cart)
        {
            if (on) _dockCart = cart;
            else if (_dockCart == cart) _dockCart = -1;
        }

        public void _Yes() { if (_box != null) _box._AnswerYesGen(_shownGen); }
        public void _No() { if (_box != null) _box._AnswerNoGen(_shownGen); }
        public void _Go() { if (_box != null) _box._AnswerGoGen(_shownGen); Refresh(); }
        public void _NotNow() { if (_box != null) _box._AnswerNotNowGen(_shownGen); }
        public void _Pause() { if (_box != null) _box._PauseIncoming(); }
        public void _Block() { if (_box != null) _box._BlockIncoming(); }

        void Update()
        {
            if (!_showing) return;
            bool after = Time.time < _afterUntil;
            if (!after && (_box == null || _box._IncomingFrom() < 0)) { _Hide(); return; }
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (_dockCart < 0 && Utilities.IsValid(lp))   // 乗車中はドックが置く (BUG-UI-002)
            {
                Vector3 d = lp.GetPosition() - _placedFrom; d.y = 0f;
                if (d.magnitude > replaceMeters) Place();
            }
            if (after) return;   // いいよの後の表示は書き換えない
            if (Time.time < _nextRefresh) return;
            _nextRefresh = Time.time + 0.25f;
            Refresh();
        }

        /// <summary>出す: ドック中は板を出すだけ (位置は KH_CartDock の LateUpdate が同じフレームで決める)、それ以外は目の前へ置く。</summary>
        private void PlaceOrOpen()
        {
            if (_dockCart < 0) { Place(); return; }
            if (board != null && !board.activeSelf) board.SetActive(true);
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
            bool film = kind == KH_TravelMailbox.KindFilm;
            if (kind != _shownKind || _box._IncomingGen() != _shownGen)
            {
                // 種類が変わったらボタンの実体を切り替える (古い種類のボタンは押せなくなる)。撮影は 3 つめの組 (Button6 / Button7)
                SetActive(joinButtons, !call && !film); SetActive(callButtons, call); SetActive(filmButtons, film); SetActive(commonButtons, true);
                _shownKind = kind; _shownGen = _box._IncomingGen();
            }
            if (film)
            {
                // 撮影の形 (§7.4): 見出しは撮影の依頼、場所の行の代わりに「映像は相手の画面だけ」。相手の場所は出さない
                if (titleLabel != null) titleLabel.text = TJa(KeyFilmTitle, WordFilmTitle).Replace("{0}", name);
                if (placeLabel != null) placeLabel.text = TJa(KeyFilmPlace, WordFilmPlace);
                if (filmYesLabel != null) filmYesLabel.text = T("course.travel.join.notice.yes");
                if (filmNoLabel != null) filmNoLabel.text = T("course.travel.join.notice.no");
                if (reasonLabel != null) reasonLabel.text = "";
                if (countLabel != null) countLabel.text = Mathf.CeilToInt(_box._IncomingLeft()).ToString();
                if (pauseLabel != null) pauseLabel.text = T("course.travel.join.notice.pause");
                if (blockLabel != null) blockLabel.text = T("course.travel.join.notice.block2");
                return;
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

        /// <summary>W5: the film texts. ja when there is no language object or the table has no such key yet.</summary>
        private string TJa(string key, string ja)
        {
            if (language == null) return ja;
            string s = language._Text(key);
            return s == null || s.Length == 0 || s == key ? ja : s;
        }
    }
}
