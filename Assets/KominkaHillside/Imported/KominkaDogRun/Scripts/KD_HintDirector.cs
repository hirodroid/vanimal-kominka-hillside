using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_HintDirector : UdonSharpBehaviour
    {
        public const string SaveKey = "KD_Onboarding_v1";
        // KD_Onboarding_v1 のビット。1・2 = 通知の既読 (IMP-INF-012)。4・8 = IMP-INF-020 (2026-09-17)。
        // 未知のビットは最新値と OR して保持する (下の Save)
        public const int SeenMenuNotice = 1;
        public const int SeenSleepNotice = 2;
        /// <summary>案内 (ようこそ) を離れた。以後、庭にメニューを置かない・ようこその面を出さない</summary>
        public const int SeenGuideLeft = 4;
        /// <summary>自分の操作でメニューを開けた。以後、手元ヒントを出さない</summary>
        public const int SeenSelfOpened = 8;
        public KD_Language language;
        public KD_TreatPouch pouch;
        public KD_ShopKeeper keeper;
        public KD_PhotoViewer viewer;
        public KD_LiveSystem live;
        public KD_SleepSlot[] sleepSlots;
        public KD_DogPetController[] pet;
        public Transform[] dogs;
        public KD_BallController[] balls;
        public VRC_Pickup[] pickups;
        public GameObject noticeRoot;
        public TextMeshProUGUI noticeLabel;
        public GameObject hintRoot;
        public TextMeshProUGUI hintLabel;
        public float petDelay = 90f;
        public float menuDelay = 180f;
        public float sleepDelay = 300f;
        public float noticeSeconds = 12f;
        public float noticeGap = 75f;
        public int maximumNotices = 3;
        public float petDistance = 2f;
        public bool sleepNoticeEnabled = true;
        [Tooltip("Persistenceの無い検証環境でのみ有効。保存は行わない")]
        public bool sessionOnly;

        // ---- IMP-INF-020 (2026-09-17): 初回の庭メニュー (案 1+2) と手元ヒント (案 4) ----
        //
        // 案 1+2: 復元後、案内を離れた印 (bit 4) が無ければ手元メニューを庭へ開いた状態で置く (置き方と面は KD_TreatPouch)。
        // 案 4: 自分の操作で一度も開けていない間 (bit 8 が無い)、開く操作そのものを手元で示し続ける。
        //   VR = 右手に追従する小さなラベル / Desktop = 画面上端のラベル / Touch = [メニュー] の下の吹き出し。
        //   板から離れた後・板に気づかなかった人へ「自分で呼ぶ操作」を教えるのが役目なので、庭の板を閉じても消えない。
        //   消えるのは自分でスティック / T / [メニュー] を使ったときだけ。
        [Header("IMP-INF-020: 初回の庭メニューと手元ヒント")]
        [Tooltip("復元後、案内を離れた印が無い人の手元メニューを庭 (KD_TreatPouch.welcomeSpot) へ開いた状態で置く")]
        public bool welcomeEnabled = true;
        [Tooltip("VR: 右手に追従する小さなラベル (World Space)。自分の操作で一度開くまで出す")]
        public GameObject handHintRoot;
        public TextMeshProUGUI handHintLabel;
        [Tooltip("右手 (コントローラ) からのずらし (m、ワールド上向き)。手の少し上に浮かべる")]
        public float handHintHeight = 0.09f;
        [Tooltip("Desktop: 画面上端のラベル (Screen Space Overlay)。VR では出さない")]
        public GameObject screenHintRoot;
        public TextMeshProUGUI screenHintLabel;
        [Tooltip("Touch: 画面の [メニュー] の下の吹き出し (起動ボタンの子。ボタンと一緒に出る)")]
        public GameObject touchHintRoot;
        public TextMeshProUGUI touchHintLabel;
        [Tooltip("手元ヒントが出ている間も 3 分のメニュー紹介通知 (N1) を出すか。既定 OFF — 手元ヒントが常時出るので二重になる")]
        public bool menuNoticeWithHandHint;
        private bool _handHintOn;
        private bool _restored;
        private int _seen;
        private int _sessionSeen;
        private bool _menuUsed;
        private bool _petted;
        private bool _thrown;
        private bool _slept;
        private float _entered;
        private float _nextTick;
        private float _noticeUntil;
        private float _lastNoticeEnd = -1000f;
        private int _noticeKind;
        private int _noticeCount;
        private Transform _hintTarget;

        private void Start()
        {
            _entered = Time.time; Hide();
            for (int i = 0; balls != null && i < balls.Length; i++) if (balls[i] != null) balls[i].hints = this;
        }
        private void OnDisable() { Hide(); HideHandHint(); }
        private void Hide()
        {
            if (noticeRoot != null) noticeRoot.SetActive(false);
            if (hintRoot != null) hintRoot.SetActive(false);
            _hintTarget = null;
        }
        private void HideHandHint()
        {
            _handHintOn = false;
            if (handHintRoot != null) handHintRoot.SetActive(false);
            if (screenHintRoot != null) screenHintRoot.SetActive(false);
            if (touchHintRoot != null) touchHintRoot.SetActive(false);
        }
        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            int saved;
            PlayerData.TryGetInt(player, SaveKey, out saved);
            _seen |= saved;
            _restored = true;
            // 復元前に立った印 (自分で開けた等) をここで書く — 復元前は保存しない規律 (IMP-INF-012) のまま取りこぼさない
            int pending = _sessionSeen & ~_seen;
            if (pending != 0) Save(pending);
            // IMP-INF-020 案 1: 案内を離れた印が無い人にだけ、庭へ開いた状態で置く。置いただけでは印を立てない
            // 自分で開けた人 (bit 8。復元前に開けた場合も含む) にも置かない — 呼び方を知っている人に案内は要らない (Codex 指摘 2026-09-17)
            if (welcomeEnabled && pouch != null && ((_seen | _sessionSeen) & (SeenGuideLeft | SeenSelfOpened)) == 0 && !_menuUsed) pouch._ShowWelcomeInGarden();
        }
        /// <summary>案内済みの印を立てる。復元後だけ保存 (最新値と OR。未知のビットも保持)。</summary>
        private void Save(int bit)
        {
            _sessionSeen |= bit;
            if (!_restored || sessionOnly) return;
            var lp = Networking.LocalPlayer; if (!Utilities.IsValid(lp)) return;
            int latest; PlayerData.TryGetInt(lp, SaveKey, out latest);
            _seen |= latest | bit;
            PlayerData.SetInt(SaveKey, _seen);
        }
        /// <summary>KD_TreatPouch: 自分の操作 (右スティック / T / [メニュー]) で開けた・呼び戻した。手元ヒントはここで消える。</summary>
        public void _OnMenuOpened() { _menuUsed = true; Save(SeenSelfOpened | SeenGuideLeft); }   // 自分で開けた = 案内も要らない
        /// <summary>KD_TreatPouch: ようこその面を離れた (タブ / 設定 / 閉じる / あとで / メニューを見る / 本人の呼び出し)。</summary>
        public void _OnGuideLeft() { Save(SeenGuideLeft); }
        public void _OnLocalThrow() { _thrown = true; }
        /// <summary>手元メニューに集中しているか。**庭に据えたままの板 (ようこそを離れた後も) は近くに居るときだけ集中扱い** —
        /// 遠くで開いたままの板が、犬のそばの撫で方ヒントまで止めないように。</summary>
        private bool MenuFocused(VRCPlayerApi lp)
        {
            if (pouch == null || !pouch.PanelOpen()) return false;
            if (!pouch.GardenPlaced() || pouch.panelRoot == null) return true;
            return (pouch.panelRoot.transform.position - lp.GetPosition()).sqrMagnitude < 9f;
        }
        private bool Focused()
        {
            var lp = Networking.LocalPlayer;
            if ((Utilities.IsValid(lp) && MenuFocused(lp)) || (viewer != null && viewer.panelRoot != null && viewer.panelRoot.activeInHierarchy)
                || (live != null && live.IsPlaying) || (keeper != null && keeper.menuRoot != null && keeper.menuRoot.activeInHierarchy)) return true;
            for (int i = 0; sleepSlots != null && i < sleepSlots.Length; i++)
                if (sleepSlots[i] != null && sleepSlots[i].IsSleeperLocal) return true;
            return false;
        }
        private string L(string key) { return language != null ? language._Text(key) : key; }
        private int InputKind(VRCPlayerApi lp)
        { return lp.IsUserInVR() ? 0 : InputManager.GetLastUsedInputMethod() == VRCInputMethod.Touch ? 2 : 1; }
        private void Update()
        {
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + .25f;
            var lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) { Hide(); HideHandHint(); return; }
            // 「開いている」ではなく「自分の操作で開けた」を数える — 庭に置いた板 (IMP-INF-020) は自動で開く
            // IMP-DOG-034 C1-misc: skip each "learned" scan once its flag is set. Unchanged behaviour: _menuUsed / _petted /
            // _slept / _thrown are only ever set true (never re-armed in this stay), and the getters read are side-effect free
            // (SelfOpened / IsPetting / IsSleeperLocal / lastThrowerPlayerId). Focused() keeps its own sleep scan.
            if (!_menuUsed && pouch != null && pouch.SelfOpened()) _menuUsed = true;
            if (!_petted) for (int i = 0; pet != null && i < pet.Length; i++) if (pet[i] != null && pet[i].IsPetting) { _petted = true; break; }
            if (!_slept) for (int i = 0; sleepSlots != null && i < sleepSlots.Length; i++) if (sleepSlots[i] != null && sleepSlots[i].IsSleeperLocal) { _slept = true; break; }
            if (!_thrown) for (int i = 0; balls != null && i < balls.Length; i++) if (balls[i] != null && balls[i].lastThrowerPlayerId == lp.playerId) { _thrown = true; break; }
            Hide();
            bool focused = Focused();
            if (_noticeKind != 0 && (focused || Time.time >= _noticeUntil || (_noticeKind == 1 && _menuUsed) || (_noticeKind == 2 && _slept)))
            { _noticeKind = 0; _lastNoticeEnd = Time.time; }
            if (focused) { HideHandHint(); return; }
            int input = InputKind(lp);
            // IMP-INF-020 案 4: 自分で一度も開けていない間は、開く操作そのものを手元に出し続ける (面は端末で 1 つ)。
            // 他の面 (ボール / 犬のラベル・通知) と同時に出てよい — 役割が「今の操作を助ける」ではなく「呼び方を覚えるまで」だから
            bool handHint = (_restored || sessionOnly) && !_menuUsed && ((_seen | _sessionSeen) & SeenSelfOpened) == 0;
            ShowHandHint(handHint, input);
            if (!_thrown)
                for (int i = 0; pickups != null && i < pickups.Length; i++)
                {
                    var pickup = pickups[i];
                    if (pickup == null || !pickup.IsHeld || !Networking.IsOwner(pickup.gameObject)) continue;
                    ShowHint(pickup.transform, L(input == 0 ? "hint.throw.vr" : input == 2 ? "hint.throw.touch" : "hint.throw.desktop"), .18f);
                    return;
                }
            if (!_petted && !_thrown && Time.time - _entered >= petDelay)
            {
                Transform nearest = null; float best = petDistance * petDistance;
                for (int i = 0; dogs != null && i < dogs.Length; i++)
                {
                    if (dogs[i] == null) continue;
                    float d = (dogs[i].position - lp.GetPosition()).sqrMagnitude;
                    if (d < best) { nearest = dogs[i]; best = d; }
                }
                if (nearest != null) { ShowHint(nearest, L("hint.pet"), .12f); return; }
            }
            if (_noticeKind != 0)
            { noticeLabel.text = NoticeText(_noticeKind, input); noticeRoot.SetActive(true); return; }
            // 手元ヒントが出ている間、メニュー紹介通知 (N1) は「使用済み」扱いで抑える (二重に案内しない)
            bool menuCovered = _menuUsed || (handHint && !menuNoticeWithHandHint);
            int candidate = NoticeCandidate(Time.time - _entered, _restored || sessionOnly, _seen | _sessionSeen, menuCovered, _slept,
                _noticeCount, Time.time - _lastNoticeEnd);
            if (candidate == 0 || noticeRoot == null || noticeLabel == null) return;
            _noticeKind = candidate; _noticeCount++; _noticeUntil = Time.time + noticeSeconds;
            Save(candidate == 1 ? SeenMenuNotice : SeenSleepNotice);
            var head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 forward = head.rotation * Vector3.forward; forward.y = 0f;
            if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
            forward.Normalize();
            noticeRoot.transform.SetPositionAndRotation(head.position + forward * .95f + Vector3.down * .12f, Quaternion.LookRotation(forward));
            noticeLabel.text = NoticeText(candidate, input); noticeRoot.SetActive(true);
        }
        // net: callable-ok: 通知の候補を返す純関数。状態や保存を書き換えない
        public int NoticeCandidate(float elapsed, bool ready, int seen, bool menuUsed, bool slept, int count, float gap)
        {
            if (!ready || count >= maximumNotices || gap < noticeGap) return 0;
            if (!menuUsed && (seen & 1) == 0 && elapsed >= menuDelay) return 1;
            if (sleepNoticeEnabled && !slept && (seen & 2) == 0 && elapsed >= sleepDelay) return 2;
            return 0;
        }
        private string NoticeText(int kind, int input)
        { return L(kind == 2 ? "hint.sleep" : input == 0 ? "hint.menu.vr" : input == 2 ? "hint.menu.touch" : "hint.menu.desktop"); }
        private float _hintHeight;
        private void ShowHint(Transform target, string text, float height)
        {
            if (hintRoot == null || hintLabel == null) return;
            if (_noticeKind != 0) { _noticeKind = 0; _lastNoticeEnd = Time.time; }
            _hintTarget = target; _hintHeight = height; hintLabel.text = text; hintRoot.SetActive(true);
            FollowHint();
        }
        private void FollowHint()
        {
            if (_hintTarget == null || hintRoot == null) return;
            var lp = Networking.LocalPlayer; if (!Utilities.IsValid(lp)) return;
            Vector3 pos = _hintTarget.position + Vector3.up * _hintHeight;
            Vector3 direction = pos - lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            if (direction.sqrMagnitude > .001f) hintRoot.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(direction));
        }
        /// <summary>手元ヒントの面を端末で 1 つ選ぶ。VR = 右手 (位置は PostLateUpdate で追う) / Desktop = 画面上端 / Touch = [メニュー] の下。
        /// Touch の吹き出しは起動ボタンの子なので、ボタンが隠れているとき (VR・写真ビューア中) は一緒に隠れる。</summary>
        private void ShowHandHint(bool on, int input)
        {
            _handHintOn = on && input == 0 && handHintRoot != null && handHintLabel != null;
            if (handHintRoot != null && handHintRoot.activeSelf != _handHintOn) handHintRoot.SetActive(_handHintOn);
            bool screen = on && input == 1 && screenHintRoot != null && screenHintLabel != null;
            if (screenHintRoot != null && screenHintRoot.activeSelf != screen) screenHintRoot.SetActive(screen);
            bool touch = on && input == 2 && touchHintRoot != null && touchHintLabel != null;
            if (touchHintRoot != null && touchHintRoot.activeSelf != touch) touchHintRoot.SetActive(touch);
            // 0.25 秒ごとに同じ文字列を書き直さない (TMP の再レイアウトを起こさない)
            if (_handHintOn) { string s = L("hint.hand.vr"); if (handHintLabel.text != s) handHintLabel.text = s; FollowHandHint(); }
            if (screen) { string s = L("hint.hand.desktop"); if (screenHintLabel.text != s) screenHintLabel.text = s; }
            if (touch) { string s = L("hint.hand.touch"); if (touchHintLabel.text != s) touchHintLabel.text = s; }
        }
        private void FollowHandHint()
        {
            if (!_handHintOn || handHintRoot == null) return;
            var lp = Networking.LocalPlayer; if (!Utilities.IsValid(lp)) return;
            Vector3 pos = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position + Vector3.up * handHintHeight;
            Vector3 direction = pos - lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            direction.y = 0f;   // 板は常に立てる (手を上下しても読める)
            if (direction.sqrMagnitude > .001f) handHintRoot.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(direction));
            else handHintRoot.transform.position = pos;
        }
        public override void PostLateUpdate() { FollowHint(); FollowHandHint(); }
    }
}
