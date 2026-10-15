using KominkaDogRun;
using QvPen.UdonScript;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-WLD-017: 筆ペン (QvPen 6 色) を手元メニューの副画面 Page_Settings/PenPage から色を選んで自分の胸の前へ呼ぶ。
    /// 設計は docs/development/issues/IMP-WLD-017-pen-call-from-menu.md §4。開く・戻るは KD_TreatPouch.Pen.cs、配線は Tools/KominkaHillside/Pen/1。
    ///
    /// - 呼ぶ: 門 (Busy = isHeld / IsUser / pickup.IsHeld) → ペンの所有権を 1 回だけ求める → Update で所有権を待つ (StageTimeout で諦める)
    ///   → 門を見直す → 胸の前 (KH_ToyRecall の HoverSpot の式) へ置き、同じフレームで FlagDiscontinuity。
    ///   持たれているペンは呼ばない (ボタンは「使用中」で押せない。2026-10-16 ユーザー決定)。進行中は 1 本だけ (他の色は捨てる)
    /// - 物理は触らない (6 本とも kinematic・重力なし)。PenManager の所有権も QvPen の状態機械も触らない (本当の Pickup で QvPen が取る)
    /// - 戻す: 置いてから returnSeconds 触られなければ PenManager.Respawn() で筆掛けへ。予約はペンごと。自分が掴んだ (OnPenPickup) /
    ///   所有権を失った / 誰かが持った / 置いた点から MovedTolerance 以上動いた、で取り消す。退室後は予約が消える (保証しない)
    /// - 同期変数 0 本。すべてローカル専用 (公開メソッドは `_` 始まり。vrc-networking §1)。例外は OnPenPickup / OnPenDrop で、
    ///   基底 (QvPen_PenCallbackListener) の名前なので `_` を付けられない。遠隔から呼ばれても、自分が使っているペンの予約を消す・描き直すだけで害は無い
    /// - このコンポーネントは面 (PenPage) の子ではなく、非表示にならないシーン側 (KH_ToyRecall の隣) に置く:
    ///   面を閉じても所有権待ちと戻しの見張りは Update で続ける。描画の更新 (_Tick) だけ面が開いている間 (page.activeInHierarchy)
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_PenCall : QvPen_PenCallbackListener
    {
        /// <summary>所有権待ちの上限 (KH_ToyRecall と同じ)。</summary>
        public const float StageTimeout = 4f;
        /// <summary>面が開いている間の見回りの間隔。</summary>
        public const float TickSeconds = 0.5f;
        /// <summary>置いた点からこれ以上動いたら、誰かが使った (戻しの予約を消す)。</summary>
        public const float MovedTolerance = 0.3f;
        private const float WatchInterval = 0.25f;   // 戻しの見張り (毎フレームにしない。Quest)
        private const float NoticeSeconds = 4f;      // 結果の 1 行 (置いた / 呼べなかった)
        private const float PenRadius = 0.05f;       // 壁・地面との間合いに使う大きさ (ペンを球とみなす)
        private const float HoverGap = 0.05f;

        private const string KeyOpen = "pen.call.open";
        private const string KeyInUse = "KD_ShopShelf.WordInUse";
        private const string KeyCalling = "KD_TreatPouch.WordToyCalling";
        private const string KeyPlaced = "KD_TreatPouch.WordToyPlaced";
        private const string KeyFailed = "KD_TreatPouch.WordToyFailed";
        private const string KeyBack = "KD_TreatPouch.WordAccBack";

        [Header("QvPen (Pen/1 が配線。(0)〜(5) の順。宣言で new しない: vrc-udonsharp §4 同長配列)")]
        public QvPen_PenManager[] penManagers;
        public QvPen_Pen[] pens;
        public KD_Language language;

        [Header("面 (Page_Settings/PenPage)")]
        [Tooltip("Page_Settings/PenPage。開いているかの判定 (_Tick の自己再予約はこれが activeInHierarchy の間だけ)")]
        public GameObject page;
        [Tooltip("6 色のボタン。色は Pen/1 が塗る (ここでは Image の色を書かない)")]
        public Button[] colorButtons;
        public TextMeshProUGUI[] colorLabels;
        public TextMeshProUGUI stateLabel;
        [Tooltip("[もどる] の Label (KD_TreatPouch.WordAccBack)")]
        public TextMeshProUGUI backLabel;
        [Tooltip("設定ホームの [筆ペン] の Label (任意。Pen/9 で消える物なので翻訳の束ね表に入れず、ここで pen.call.open を描く)")]
        public TextMeshProUGUI entryLabel;

        [Header("置き方 (KH_ToyRecall の hover* と同じ値)")]
        public float hoverForwardVR = 0.45f, hoverDownVR = 0.35f, hoverForwardFlat = 0.8f, hoverDownFlat = 0.2f;
        [Tooltip("置いてからこの秒数 触られなければ筆掛けへ戻す")]
        public float returnSeconds = 20f;
        [Tooltip("置き場所の壁・地面の判定 (KH_ToyRecall.groundMask と同じ既定。トリガーは無視)")]
        public LayerMask groundMask = (1 << 0) | (1 << 11);

        private bool _inited;
        private bool _registered;
        private VRCPickup[] _pickups;
        private VRCObjectSync[] _syncs;
        // 戻しの予約 (ペンごと)
        private bool[] _armed;
        private float[] _placedAt;
        private Vector3[] _placedPos;
        private bool _anyArmed;
        private float _nextWatchAt;
        // 進行中の呼び出し (1 本だけ)
        private int _pendingIndex = -1;
        private float _requestAt;
        // 結果の 1 行
        private string _noticeKey = "";
        private float _noticeUntil = -1f;
        // 見回り
        private bool _tickPending;
        private float _tickAt = -100f;
        private string _textLang;

        private string L(string key) { return language != null ? language._Text(key) : key; }

        private void Init()
        {
            if (_inited) return;
            _inited = true;
            int n = pens != null ? pens.Length : 0;
            if (n == 0) Debug.LogError("[KH_PenCall] pens が未配線。Run Tools/KominkaHillside/Pen/1.");
            _pickups = new VRCPickup[n];
            _syncs = new VRCObjectSync[n];
            _armed = new bool[n];
            _placedAt = new float[n];
            _placedPos = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                if (pens[i] == null) continue;
                GameObject go = pens[i].gameObject;
                _pickups[i] = go.GetComponent<VRCPickup>();
                _syncs[i] = go.GetComponent<VRCObjectSync>();
            }
        }

        /// <summary>通知 (OnPenPickup / OnPenDrop) を受ける登録。最初の Update から (全 Start の後。PenManager の listenerList の初期化より先に
        /// Register を踏まないため。vrc-udonsharp §6)。QvPen 側も Contains で二重登録を弾くが、こちらでも 1 回だけ。</summary>
        private void RegisterListeners()
        {
            _registered = true;
            if (penManagers == null) return;
            for (int i = 0; i < penManagers.Length; i++)
                if (penManagers[i] != null) penManagers[i].Register(this);
        }

        private void Start()
        {
            Init();
            DrawTexts();
            DrawButtons();
            DrawState();
        }

        // ================================================================ 受け口 (面の 6 色。onClick → SendCustomEvent)

        public void _Call0() { Call(0); }
        public void _Call1() { Call(1); }
        public void _Call2() { Call(2); }
        public void _Call3() { Call(3); }
        public void _Call4() { Call(4); }
        public void _Call5() { Call(5); }

        // ================================================================ 受け口 (他の Behaviour から)

        /// <summary>KD_TreatPouch._PenOpen (と板の開き直し) から。描き直して見回りを始める。ローカル専用。</summary>
        public void _PageOpened()
        {
            Init();
            DrawTexts();
            DrawButtons();
            DrawState();
            ScheduleTick();
        }

        /// <summary>言語の切替 (KD_TreatPouch._RefreshLanguage 経由)。面を閉じていても設定ホームの [筆ペン] を今の言語へ。ローカル専用。</summary>
        public void _RefreshLanguage()
        {
            Init();
            DrawTexts();
            DrawButtons();
            DrawState();
        }

        /// <summary>面が開いている間だけ 0.5 s ごと (自己再予約)。閉じたら止まる。ローカル専用。</summary>
        public void _Tick()
        {
            if (Time.time - _tickAt < TickSeconds * 0.5f) return;   // 二重に予約された鎖の後の方は捨てる (1 本にまとまる)
            _tickPending = false;
            if (!PageVisible()) return;
            Init();
            if (LanguageChanged()) DrawTexts();
            DrawButtons();   // isHeld は StartUsing / EndUsing (All) で変わる
            DrawState();
            ScheduleTick();
        }

        // ---- QvPen_PenManager から (自分の端末で掴んだ / 離したときだけ。どのペンかは渡らないので IsUser で探す) ----

        public override void OnPenPickup()
        {
            Init();
            // isUser は OnPickup の先頭で立つ (この通知より先)。自分が掴んだペンは戻さない
            int n = _armed.Length;
            for (int i = 0; i < n; i++)
                if (_armed[i] && pens[i] != null && pens[i].IsUser) _armed[i] = false;
            if (PageVisible()) DrawButtons();
        }

        public override void OnPenDrop()
        {
            if (PageVisible()) { Init(); DrawButtons(); }
        }

        // ================================================================ 中身

        /// <summary>持たれている (誰かの手 / 自分の手) なら呼ばない。未配線も押せない側。</summary>
        private bool Busy(int i)
        {
            if (pens == null || i < 0 || i >= pens.Length || pens[i] == null) return true;
            QvPen_Pen p = pens[i];
            if (p.isHeld || p.IsUser) return true;
            VRCPickup pk = (_pickups != null && i < _pickups.Length) ? _pickups[i] : null;
            return pk != null && pk.IsHeld;
        }

        private void Call(int i)
        {
            Init();
            if (_pendingIndex >= 0) return;            // 進行中は 1 本だけ (連打・他の色は捨てる)
            if (Busy(i)) { DrawButtons(); return; }
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            GameObject go = pens[i].gameObject;
            _pendingIndex = i;
            _requestAt = Time.time;
            _noticeUntil = -1f;
            if (!Networking.IsOwner(lp, go)) Networking.SetOwner(lp, go);   // net: setowner-ok: ペンだけ (持たれていない。Busy で門)。PenManager は取らない
            DrawState();
            if (PageVisible()) ScheduleTick();
        }

        private void Update()
        {
            if (!_registered) RegisterListeners();
            if (_pendingIndex >= 0) StepPending();   // 所有権待ちだけ毎フレーム (長くて StageTimeout)
            if (_anyArmed && Time.time >= _nextWatchAt) { _nextWatchAt = Time.time + WatchInterval; Watch(); }
        }

        private void StepPending()
        {
            int i = _pendingIndex;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (pens == null || i >= pens.Length || pens[i] == null || !Utilities.IsValid(lp)) { _pendingIndex = -1; Notice(KeyFailed); return; }
            if (Networking.IsOwner(lp, pens[i].gameObject))
            {
                _pendingIndex = -1;
                if (Busy(i)) Notice(KeyFailed);   // 所有権が来るまでに誰かが掴んだ
                else { Place(i, lp); Notice(KeyPlaced); }
                return;
            }
            if (Time.time - _requestAt >= StageTimeout) { _pendingIndex = -1; Notice(KeyFailed); }
        }

        /// <summary>Owner のときだけ (StepPending が確かめた)。瞬間移動なので FlagDiscontinuity (他の端末で滑って見えない)。</summary>
        private void Place(int i, VRCPlayerApi lp)
        {
            VRCPlayerApi.TrackingData head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 eye = head.position;
            Vector3 fwd = head.rotation * Vector3.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) { fwd = lp.GetRotation() * Vector3.forward; fwd.y = 0f; }
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 at = HoverSpot(lp, eye, fwd);
            // 向きは QvPen の CallPen と同じく前向き (水平にだけ直す)
            pens[i].transform.SetPositionAndRotation(at, Quaternion.LookRotation(fwd, Vector3.up));
            VRCObjectSync sync = _syncs[i];
            if (sync != null) sync.FlagDiscontinuity();
            _placedAt[i] = Time.time;
            _placedPos[i] = at;
            _armed[i] = true;
            if (!_anyArmed) _nextWatchAt = Time.time + WatchInterval;
            _anyArmed = true;
        }

        /// <summary>KH_ToyRecall.HoverSpot の式 (頭の水平前方・下、アバターの大きさで伸縮)。頭とその点の間の壁で手前に止め、
        /// 真下の地面より上に保つ。おもちゃ同士の間合い (AwayFromToys) と最後の重なり検査は写さない。</summary>
        private Vector3 HoverSpot(VRCPlayerApi lp, Vector3 eye, Vector3 fwd)
        {
            float scale = Mathf.Clamp(lp.GetAvatarEyeHeightAsMeters() / 1.6f, 0.5f, 2f);
            bool vr = lp.IsUserInVR();
            float ahead = (vr ? hoverForwardVR : hoverForwardFlat) * scale;
            float down = (vr ? hoverDownVR : hoverDownFlat) * scale;
            Vector3 want = eye + fwd * ahead + Vector3.down * down;
            Vector3 d = want - eye;
            float len = d.magnitude;
            RaycastHit hit;
            if (len > 0.001f)
            {
                Vector3 dir = d / len;
                if (Physics.SphereCast(eye, PenRadius + HoverGap, dir, out hit, len, groundMask, QueryTriggerInteraction.Ignore))
                    want = eye + dir * Mathf.Max(0f, hit.distance - 0.01f);
            }
            Vector3 top = new Vector3(want.x, Mathf.Max(eye.y, want.y), want.z);
            if (Physics.Raycast(top, Vector3.down, out hit, top.y - want.y + PenRadius + 1f, groundMask, QueryTriggerInteraction.Ignore))
            {
                float minY = hit.point.y + PenRadius + HoverGap;
                if (want.y < minY) want.y = minY;
            }
            return want;
        }

        /// <summary>WatchInterval ごと (予約がある間だけ)。取り消しの条件に当たれば何も書かずに外す。期限で筆掛けへ。</summary>
        private void Watch()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            float now = Time.time;
            float tol2 = MovedTolerance * MovedTolerance;
            bool any = false;
            for (int i = 0; i < _armed.Length; i++)
            {
                if (!_armed[i]) continue;
                QvPen_Pen p = pens[i];
                if (p == null || !Utilities.IsValid(lp) || !Networking.IsOwner(lp, p.gameObject) || Busy(i)
                    || (p.transform.position - _placedPos[i]).sqrMagnitude > tol2)
                { _armed[i] = false; continue; }
                if (now - _placedAt[i] >= returnSeconds)
                {
                    _armed[i] = false;
                    QvPen_PenManager m = (penManagers != null && i < penManagers.Length) ? penManagers[i] : null;
                    if (m != null) m.Respawn();   // pen._Respawn: Drop → Owner なので objectSync.Respawn (筆掛けへ)
                    continue;
                }
                any = true;
            }
            _anyArmed = any;
        }

        private void Notice(string key)
        {
            _noticeKey = key;
            _noticeUntil = Time.time + NoticeSeconds;
            if (PageVisible()) { DrawButtons(); DrawState(); ScheduleTick(); }   // 見回りが 4 s 後に行を消す
        }

        private bool PageVisible()
        {
            if (page != null) return page.activeInHierarchy;
            if (stateLabel != null) return ((Component)stateLabel).gameObject.activeInHierarchy;
            return false;   // 自分は面の外 (常に有効) なので gameObject では判定しない
        }

        private void ScheduleTick()
        {
            if (_tickPending && Time.time - _tickAt < TickSeconds * 4f) return;   // 生きている予約がある
            _tickPending = true;
            _tickAt = Time.time;
            SendCustomEventDelayedSeconds(nameof(_Tick), TickSeconds);
        }

        private bool LanguageChanged() { return (language != null ? language.CurrentLanguage : "") != _textLang; }

        private void SetText(TextMeshProUGUI label, string s)
        {
            if (label != null && label.text != s) label.text = s;
        }

        /// <summary>言語で変わる固定の文言 ([もどる]・設定ホームの [筆ペン])。</summary>
        private void DrawTexts()
        {
            SetText(backLabel, L(KeyBack));
            SetText(entryLabel, L(KeyOpen));
            _textLang = language != null ? language.CurrentLanguage : "";   // _Text の後 (言語の初期化を済ませてから読む)
        }

        /// <summary>6 色のボタンの文字と押せる / 押せない。ボタンの色 (Image) は Pen/1 が塗ったまま触らない。</summary>
        private void DrawButtons()
        {
            int n = colorButtons != null ? colorButtons.Length : 0;
            string open = L(KeyOpen);
            string inUse = L(KeyInUse);
            for (int i = 0; i < n; i++)
            {
                bool busy = Busy(i);
                Button b = colorButtons[i];
                if (b != null && b.interactable == busy) b.interactable = !busy;
                if (colorLabels != null && i < colorLabels.Length) SetText(colorLabels[i], busy ? inUse : open);
            }
        }

        /// <summary>状態の 1 行: 呼んでいる間は「呼びます」、結果は 4 s、それ以外は空。</summary>
        private void DrawState()
        {
            string line = "";
            if (_pendingIndex >= 0) line = L(KeyCalling);
            else if (_noticeUntil > Time.time) line = L(_noticeKey);
            SetText(stateLabel, line);
        }
    }
}
