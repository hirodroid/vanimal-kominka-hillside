using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaDogRun
{
    /// <summary>
    /// 犬1匹の予約と確定済み呼び出し。BUG-NET-002: 枠Ownerの受付順で先勝ち。
    /// 操作ごとのSetOwnerは行わない。犬のOwnerも変更しない。
    /// 要求は送信者照合付きイベント、結果はManual同期（途中参加にも届く）。
    /// 選択予約はLiveSleeperIdへ混ぜず、確定まで犬を動かさない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KD_SleepSlot : UdonSharpBehaviour
    {
        public int dogIndex;
        public KD_DogPack pack;
        public KD_SleepBoard board;
        // One permanent entitlement per action, shared by every dog breed.
        public KD_ShopItem[] actionProducts;
        public const int BellyUp = 0, ChinRest = 1, GivePaw = 2, StretchYawn = 3;
        [SerializeField] private float releaseDistance = 8f;
        [SerializeField] private float arriveTimeout = 30f;
        [SerializeField] private float arriveRadius = 1.5f;
        [UdonSynced] private int sleeperId = -1;
        [UdonSynced] private int calledMs;
        [UdonSynced] private bool hasSpot;
        [UdonSynced] private Vector3 spot;
        [UdonSynced] private float spotYaw;
        [UdonSynced] private int poseId;
        [UdonSynced] private bool pawHeld;
        [UdonSynced] private int chinHand; // 0: none, 1: left, 2: right, 3: Desktop/Touch
        [UdonSynced] private int stretchMs;
        [UdonSynced] private bool hasStretch;
        [UdonSynced] private int callToken;
        [UdonSynced] private bool arrived;
        [UdonSynced] private int reservedId = -1;
        [UdonSynced] private int reservedToken;
        [UdonSynced] private int reservedMs;
        // A single event queue preserves each sender's reserve -> commit/cancel ordering.
        public const int MaxPoseId = 4;
        private const int Reserve = 1, Renew = 2, Cancel = 3, Commit = 4, Release = 5, Pose = 6, Paw = 7, Chin = 8, Stretch = 9;
        private const int LeaseMs = 15000, RequestMaxAgeMs = 8000;
        [System.NonSerialized] public int requestedPose;
        [System.NonSerialized] public Vector3 requestedSpot;
        [System.NonSerialized] public float requestedYaw;
        private int _tokenCounter;
        private int _selectionToken;
        private int _selectionOwner = -1;
        private float _selectionStarted, _selectionTouched, _nextRenew;
        private bool _selectionReadyOnce, _callPending, _autoCall;
        private float _callStarted;
        private bool _pendingHasSpot;
        private Vector3 _pendingSpot;
        private float _pendingYaw;
        private int _pendingPose, _previousCalledMs;
        private bool _pawRequested;
        private int _pawGeneration;
        private float _nextPawRetry;
        private int _chinRequested, _chinGeneration;
        private float _nextChinRetry;
        private float _nextWatch;
        // Owner-local tombstones: canceled/repeated requests cannot recreate reservations.
        private int[] _requestPlayers = new int[90];
        private int[] _requestTokens = new int[90];

        public int LiveSleeperId { get { return LivePlayer(sleeperId) ? sleeperId : -1; } }
        public int LiveReservationId { get { return ReservationLive() ? reservedId : -1; } }
        public bool IsSleeperLocal { get { var lp = Networking.LocalPlayer; return Utilities.IsValid(lp) && LiveSleeperId == lp.playerId; } }
        public int CalledMs { get { return calledMs; } }
        public int PoseId { get { return Mathf.Clamp(poseId, 0, MaxPoseId); } }
        public bool PawHeld { get { return LiveSleeperId >= 0 && PoseId == 2 && !StretchActive && pawHeld; } }
        public int ChinHand { get { return LiveSleeperId >= 0 && PoseId == 1 && !StretchActive ? chinHand : 0; } }
        public bool StretchActive { get { int age = Networking.GetServerTimeInMilliseconds() - stretchMs; return LiveSleeperId >= 0 && hasStretch && age >= 0 && age < 6000; } }
        public float StretchTime { get { return Mathf.Clamp01((Networking.GetServerTimeInMilliseconds() - stretchMs) / 6000f); } }
        public bool CanStretch { get { return LiveSleeperId >= 0 && arrived && !StretchActive && (!hasStretch || Networking.GetServerTimeInMilliseconds() - stretchMs >= 8000); } }
        public bool HasSpot { get { return hasSpot; } }
        public Vector3 Spot { get { return spot; } }
        public float SpotYaw { get { return spotYaw; } }
        public float ArriveRadius { get { return arriveRadius; } }
        public float ReleaseDistance { get { return releaseDistance; } }
        public float ArriveTimeout { get { return arriveTimeout; } }
        public bool CallPending { get { return _callPending || _autoCall; } }
        public bool SelectionPending { get { return _selectionToken != 0 && !SelectionReady && !_selectionReadyOnce; } }
        public bool SelectionActive { get { return _selectionToken != 0; } }
        public bool SelectionReady
        {
            get
            {
                var lp = Networking.LocalPlayer;
                return _selectionToken != 0 && Utilities.IsValid(lp) && ReservationLive()
                    && reservedId == lp.playerId && reservedToken == _selectionToken && OwnerId() == _selectionOwner;
            }
        }
        private bool LivePlayer(int id) { return id >= 0 && Utilities.IsValid(VRCPlayerApi.GetPlayerById(id)); }
        // net: callable-ok: read-only authenticated entitlement lookup; missing wiring fails closed
        public bool ActionOwnedBy(int action, VRCPlayerApi player)
        {
            return actionProducts != null && action >= 0 && action < actionProducts.Length
                && actionProducts[action] != null && actionProducts[action].OwnedBy(player);
        }
        // net: callable-ok: the four base poses are available without a purchase
        public bool PoseOwnedBy(int pose, VRCPlayerApi player)
        {
            return pose >= 0 && pose <= MaxPoseId && (pose < 4 || ActionOwnedBy(BellyUp, player));
        }
        private int OwnerId() { var owner = Networking.GetOwner(gameObject); return Utilities.IsValid(owner) ? owner.playerId : -1; }
        private bool ReservationLive() { return LivePlayer(reservedId) && Networking.GetServerTimeInMilliseconds() - reservedMs < LeaseMs; }
        // net: callable-ok: read-only dog lookup
        public Transform DogRoot()
        {
            return pack != null && pack.dogRoots != null && dogIndex >= 0 && dogIndex < pack.dogRoots.Length ? pack.dogRoots[dogIndex] : null;
        }
        public bool _BeginSelection()
        {
            var lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || (board != null && !board.CanCall(lp) && !IsSleeperLocal)) return false;
            if (LiveSleeperId >= 0 && !IsSleeperLocal) return false;
            if (board != null && board._IsWalkBusy(dogIndex)) return false;   // 裏山改変: リード接続中・集合中・フリスビー中は呼べない
            if (LiveReservationId >= 0 && LiveReservationId != lp.playerId) return false;
            if (_selectionToken != 0) return true;
            _selectionOwner = OwnerId();
            if (_selectionOwner < 0) return false;
            _tokenCounter = (_tokenCounter + 1) & 0x7fffffff;
            if (_tokenCounter == 0) _tokenCounter = 1;
            _selectionToken = _tokenCounter;
            _selectionStarted = _selectionTouched = Time.time;
            _nextRenew = Time.time + 3f;
            _selectionReadyOnce = false;
            SendRequest(Reserve, _selectionToken, Vector3.zero, 0f, 0, false, _selectionOwner);
            return true;
        }
        public void _KeepSelectionAlive() { if (_selectionToken != 0) _selectionTouched = Time.time; }
        public void _EndSelection()
        {
            int token = _selectionToken, owner = _selectionOwner;
            _selectionToken = 0; _callPending = false; _autoCall = false;
            if (token != 0) SendRequest(Cancel, token, Vector3.zero, 0f, 0, false, owner);
        }
        public void _CancelPendingCall() { _EndSelection(); }
        public bool _TryCall()
        {
            if (CallPending || !PoseOwnedBy(requestedPose, Networking.LocalPlayer) || !_BeginSelection()) return false;
            _pendingHasSpot = false; _pendingSpot = Vector3.zero; _pendingYaw = 0f;
            _pendingPose = Mathf.Clamp(requestedPose, 0, MaxPoseId); _autoCall = true;
            if (SelectionReady) SubmitCall();
            return true;
        }
        public bool _TryCallPlaced()
        {
            if (CallPending || !SelectionReady || !PoseOwnedBy(requestedPose, Networking.LocalPlayer) || !ValidPlacement(requestedSpot, requestedYaw)) return false;
            _pendingHasSpot = true; _pendingSpot = requestedSpot; _pendingYaw = requestedYaw;
            _pendingPose = Mathf.Clamp(requestedPose, 0, MaxPoseId);
            SubmitCall();
            return true;
        }
        private void SubmitCall()
        {
            _autoCall = false; _previousCalledMs = calledMs;
            _callPending = true; _callStarted = Time.time;
            SendRequest(Commit, _selectionToken, _pendingSpot, _pendingYaw, _pendingPose, _pendingHasSpot, _selectionOwner);
        }
        public void _Release()
        {
            _EndSelection();
            var lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || (!IsSleeperLocal && (board == null || !board.CanControl(lp)))) return;
            SendRequest(Release, calledMs, Vector3.zero, 0f, 0, false, OwnerId());
        }
        public bool _SetPose(int value)
        {
            if (!IsSleeperLocal || CallPending || !PoseOwnedBy(value, Networking.LocalPlayer)) return false;
            SendRequest(Pose, calledMs, Vector3.zero, 0f, value, false, OwnerId());
            _pawRequested = false;
            return true;
        }
        public void _SetPaw(bool held)
        {
            if (!IsSleeperLocal) return;
            held = held && PoseId == 2 && !CallPending && !StretchActive && ActionOwnedBy(GivePaw, Networking.LocalPlayer);
            if (_pawGeneration == calledMs && _pawRequested == held && (pawHeld == held || Time.time < _nextPawRetry)) return;
            _pawGeneration = calledMs; _pawRequested = held;
            _nextPawRetry = Time.time + 2f;
            SendRequest(Paw, calledMs, Vector3.zero, 0f, PoseId, held, OwnerId());
        }
        public void _SetChin(int hand)
        {
            if (!IsSleeperLocal) return;
            hand = PoseId == 1 && !CallPending && !StretchActive && ActionOwnedBy(ChinRest, Networking.LocalPlayer) ? Mathf.Clamp(hand, 0, 3) : 0;
            if (_chinGeneration == calledMs && _chinRequested == hand && (chinHand == hand || Time.time < _nextChinRetry)) return;
            _chinGeneration = calledMs; _chinRequested = hand; _nextChinRetry = Time.time + 2f;
            SendRequest(Chin, calledMs, Vector3.zero, 0f, hand, false, OwnerId());
        }
        public void _Stretch()
        {
            if (!IsSleeperLocal || CallPending || !CanStretch || !ActionOwnedBy(StretchYawn, Networking.LocalPlayer)) return;
            SendRequest(Stretch, calledMs, Vector3.zero, 0f, 0, false, OwnerId());
        }
        private void SendRequest(int op, int token, Vector3 position, float yaw, int pose, bool flag, int owner)
        {
            if (owner < 0) return;
            int now = Networking.GetServerTimeInMilliseconds();
            if (Networking.IsOwner(gameObject)) HandleRequest(Networking.LocalPlayer, op, token, now, position, yaw, pose, flag, owner);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestSlot), op, token, now, position, yaw, pose, flag, owner);
        }
        [NetworkCallable(maxEventsPerSecond: 30)]
        // net: callable-ok: authenticated requests; only current slot Owner writes synced results
        public void RequestSlot(int op, int token, int sentMs, Vector3 position, float yaw, int pose, bool flag, int owner)
        {
            HandleRequest(NetworkCalling.CallingPlayer, op, token, sentMs, position, yaw, pose, flag, owner);
        }
        private void HandleRequest(VRCPlayerApi player, int op, int token, int sentMs, Vector3 position, float yaw, int pose, bool flag, int owner)
        {
            if (!Networking.IsOwner(gameObject) || !Utilities.IsValid(player) || OwnerId() != owner) return;
            if (op != Cancel && op != Release && pack != null && pack.dogWander != null
                && dogIndex >= 0 && dogIndex < pack.dogWander.Length && pack.dogWander[dogIndex] != null
                && pack.dogWander[dogIndex].candyPerformanceActive) return;
            int now = Networking.GetServerTimeInMilliseconds(), age = now - sentMs;
            if (age < -1000 || age > RequestMaxAgeMs) return;
            int id = player.playerId;
            if (op == Reserve || op == Cancel)
            {
                int index = RequestIndex(id);
                if (index < 0 || token <= 0) return;
                if (op == Reserve && token <= _requestTokens[index]) return;
                if (token > _requestTokens[index]) _requestTokens[index] = token;
                if (op == Cancel)
                {
                    if (reservedId == id && reservedToken == token) ClearReservation();
                    return;
                }
                if (LiveSleeperId >= 0 && LiveSleeperId != id) return;
                if (board != null && !board.CanCall(player) && LiveSleeperId != id) return;
                if (board != null && board._IsWalkBusy(dogIndex)) return;   // 裏山改変: リード接続中・集合中・フリスビー中は予約させない
                if (ReservationLive()) return;
                reservedId = id; reservedToken = token; reservedMs = now;
                RequestSerialization();
            }
            else if (op == Renew)
            {
                if (ReservationLive() && reservedId == id && reservedToken == token)
                { reservedMs = now; RequestSerialization(); }
            }
            else if (op == Commit)
            {
                if (!ReservationLive() || reservedId != id || reservedToken != token) return;
                if ((LiveSleeperId >= 0 && LiveSleeperId != id) || (board != null && !board.CanCall(player) && LiveSleeperId != id)) return;
                if (board != null && board._IsWalkBusy(dogIndex)) return;   // 裏山改変: 予約後にリード・集合・フリスビーが始まったら確定させない
                if (!PoseOwnedBy(pose, player) || !ValidPlacement(position, yaw)) return;
                sleeperId = id; hasSpot = flag; spot = flag ? position : Vector3.zero;
                spotYaw = flag ? Mathf.Repeat(yaw, 360f) : 0f; poseId = pose; pawHeld = false; arrived = false;
                chinHand = 0; hasStretch = false;
                if (now == calledMs) now++;
                calledMs = now == 0 ? 1 : now; callToken = token;
                ClearReservation();
            }
            else if (token == calledMs && LiveSleeperId >= 0)
            {
                if (op == Release && (sleeperId == id || (board != null && board.CanControlNow(player)))) PublishRelease();   // 管理者の取り上げは受理直前に在席を測り直す (IMP-NET-007)
                else if (sleeperId == id && op == Pose && PoseOwnedBy(pose, player))
                { poseId = pose; pawHeld = false; chinHand = 0; hasStretch = false; RequestSerialization(); }
                else if (sleeperId == id && op == Paw && poseId == 2 && pose == poseId && !StretchActive && pawHeld != flag && (!flag || ActionOwnedBy(GivePaw, player)))
                { pawHeld = flag; RequestSerialization(); }
                else if (sleeperId == id && op == Chin && poseId == 1 && pose >= 0 && pose <= 3 && !StretchActive && chinHand != pose && (pose == 0 || ActionOwnedBy(ChinRest, player)))
                { chinHand = pose; RequestSerialization(); }
                else if (sleeperId == id && op == Stretch && CanStretch && ActionOwnedBy(StretchYawn, player))
                { stretchMs = now; hasStretch = true; pawHeld = false; chinHand = 0; RequestSerialization(); }
            }
        }
        private int RequestIndex(int player)
        {
            for (int i = 0; i < _requestPlayers.Length; i++) if (_requestPlayers[i] == player) return i;
            for (int i = 0; i < _requestPlayers.Length; i++)
                if (_requestPlayers[i] == 0 || !LivePlayer(_requestPlayers[i]))
                { _requestPlayers[i] = player; _requestTokens[i] = 0; return i; }
            return -1;
        }
        private bool ValidPlacement(Vector3 position, float yaw)
        {
            return !float.IsNaN(position.x) && !float.IsInfinity(position.x)
                && !float.IsNaN(position.y) && !float.IsInfinity(position.y)
                && !float.IsNaN(position.z) && !float.IsInfinity(position.z)
                && !float.IsNaN(yaw) && !float.IsInfinity(yaw);
        }
        public override bool OnOwnershipRequest(VRCPlayerApi requester, VRCPlayerApi newOwner) { return false; }
        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            _EndSelection(); _pawGeneration = 0; _chinGeneration = 0;
            if (Utilities.IsValid(player) && player.isLocal)
            {
                ClearReservation();
                if (sleeperId >= 0 && LiveSleeperId < 0) PublishRelease();
            }
        }
        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Networking.IsOwner(gameObject) || player == null) return;
            if (player.playerId == reservedId) ClearReservation();
            if (player.playerId == sleeperId) PublishRelease();
        }
        private void Start()
        {
            if (releaseDistance <= .1f) releaseDistance = 8f;
            if (arriveTimeout <= 1f) arriveTimeout = 30f;
            if (arriveRadius <= .1f) arriveRadius = 1.5f;
            if (releaseDistance <= arriveRadius) releaseDistance = Mathf.Max(8f, arriveRadius * 2f);
            if (pack == null) Debug.LogError("[KD_SleepSlot] pack is missing: " + dogIndex);
        }
        private void Update()
        {
            if (_selectionToken != 0)
            {
                if (_autoCall) _selectionTouched = Time.time;
                bool success = IsSleeperLocal && callToken == _selectionToken && calledMs != _previousCalledMs;
                if (_callPending && success) { _callPending = false; _selectionToken = 0; }
                else if (OwnerId() != _selectionOwner || Time.time - _selectionTouched > 3f
                    || (_callPending && Time.time - _callStarted > 8f)
                    || (!_callPending && ((_selectionReadyOnce && !SelectionReady) || (!_selectionReadyOnce && Time.time - _selectionStarted > 8f)))) _EndSelection();
                else if (SelectionReady)
                {
                    _selectionReadyOnce = true;
                    if (_autoCall) SubmitCall();
                    else if (!_callPending && Time.time >= _nextRenew)
                    { _nextRenew = Time.time + 3f; SendRequest(Renew, _selectionToken, Vector3.zero, 0f, 0, false, _selectionOwner); }
                }
            }
            if (Time.time < _nextWatch) return;
            _nextWatch = Time.time + .5f;
            Watch();
        }
        private void Watch()
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (reservedId >= 0 && !ReservationLive()) ClearReservation();
            if (sleeperId >= 0 && LiveSleeperId < 0) { PublishRelease(); return; }
            // 裏山改変: 「呼ばれている」と「リード / 集合 / フリスビー」が両方立ったら、さんぽ側が勝つ (呼び出しと予約を畳む)。
            // 徘徊層がさんぽ側を優先するので、残すと犬が来ないまま枠が居座る (場所指定の到着待ちの時間切れは arriveTimeout × 2 と長い。2026-10-07 audit RC-2)
            if (board != null && board._IsWalkBusy(dogIndex))
            {
                if (LiveSleeperId >= 0) { PublishRelease(); return; }
                if (reservedId >= 0) ClearReservation();
            }
            if (LiveSleeperId < 0) return;
            var player = VRCPlayerApi.GetPlayerById(sleeperId);
            Transform dog = DogRoot();
            if (!Utilities.IsValid(player) || dog == null) return;
            float d = HorizontalDistance(player.GetPosition(), dog.position);
            if (hasSpot && !arrived)
            {
                if (Vector3.Distance(dog.position, spot) <= .08f) { arrived = true; RequestSerialization(); return; }
                // 2026-10-07 audit RC-2: 届かない場所指定 (川の帯など) で枠が居座らないよう時間切れを掛ける。接近に加え持ち上げ・向き合わせがあるので場所なしの 2 倍。
                // 真上まで着いて収まり中・寝ている犬 (PlacedSleepHeld: 枠の Owner の端末が観測した配置状態。裁定は Watch を回す枠の Owner だけ) は解かない
                if (Networking.GetServerTimeInMilliseconds() - calledMs > (int)(arriveTimeout * 2000f) && !PlacedHeld()) PublishRelease();
                return;
            }
            if (!arrived)
            {
                if (d <= arriveRadius) { arrived = true; RequestSerialization(); return; }
                if (Networking.GetServerTimeInMilliseconds() - calledMs > (int)(arriveTimeout * 1000f)) PublishRelease();
                return;
            }
            if (d > releaseDistance) PublishRelease();
        }
        // 2026-10-07 audit RC-2: 場所指定の時間切れから、置いた点の真上まで着いた犬を外す (配線が無ければ外さない = 時間切れが効く)
        private bool PlacedHeld()
        {
            return pack != null && pack.dogWander != null && dogIndex >= 0 && dogIndex < pack.dogWander.Length
                && pack.dogWander[dogIndex] != null && pack.dogWander[dogIndex].PlacedSleepHeld;
        }
        private float HorizontalDistance(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }
        private void ClearReservation() { reservedId = -1; reservedToken = 0; reservedMs = 0; RequestSerialization(); }
        private void PublishRelease()
        {
            sleeperId = -1; calledMs = 0; hasSpot = false; spot = Vector3.zero; spotYaw = 0f;
            poseId = 0; pawHeld = false; chinHand = 0; hasStretch = false; callToken = 0; arrived = false;
            ClearReservation();
        }

        public void _ReleaseForCandyPerformance()
        {
            if (!Networking.IsOwner(gameObject) || pack == null || pack.dogWander == null
                || dogIndex < 0 || dogIndex >= pack.dogWander.Length
                || pack.dogWander[dogIndex] == null || !pack.dogWander[dogIndex].candyPerformanceActive) return;
            _EndSelection();
            PublishRelease();
        }
    }
}
