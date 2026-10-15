using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;
using KominkaDogRun;

namespace KominkaHillside
{
    // IMP-LIV-001. The request is a transaction: consent and reservation never end a use.
    // This behaviour shares the LeashManager/GroupActivities GameObject (one authority).
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KH_HalloweenParade : UdonSharpBehaviour
    {
        public const int Idle = 0, Consent = 1, Reserve = 2, EndUses = 3, Place = 4, Playing = 5, Restore = 6, RecoveryFailed = 7;
        public KH_HalloweenParadeDog[] dogs;
        public KH_LeashManager leashes;
        public KD_GroupActivities activities;
        public KH_WashStationCall[] washStations;
        public KD_SleepBoard sleepBoard;
        public KD_TreatBox treatBox;
        public KD_LiveSystem liveSystem;
        public KD_ShopItem hostingProduct;
        public const string ProductKey = "live.hillside_halloween_parade";
        // Authentication uses this requester's CE entitlement, never instance ownership.
        public bool _CanHost(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player)) return false;
            return hostingProduct != null && hostingProduct.productKey == ProductKey
                && hostingProduct.IsCE() && hostingProduct.ceProduct != null
                && hostingProduct.OwnedBy(player);
        }
        public bool _LocalCanStart()
        {
            return phase == Idle && _CanHost(Networking.LocalPlayer)
                && (liveSystem == null || (!liveSystem.IsNightWanted && !liveSystem.CandyPreparing && !liveSystem.DogRecovering));
        }
        public Transform[] startSlots, finaleSlots, routePoints;
        [Tooltip("Legacy preview field; retained for serialization only. Does not grant hosting permission.")]
        public bool developmentEnabled;
        public float durationSeconds = 220f, consentSeconds = 45f, preparationSeconds = 15f;
        public float recoverySeconds = 30f;
        public float marchStartSeconds = 25f, marchEndSeconds = 130f, entranceEndSeconds = 150f;
        public float dogSpacing = 1.8f, startRouteDistance = 14.201333f;
        public int[] processionRanks;
        public const int ResultNone = 0, ResultConfiguration = 1, ResultOtherShow = 2, ResultUsageChanged = 3, ResultDeclined = 4, ResultTimeout = 5,
            ResultPreparationFailed = 6, ResultFinished = 7, ResultCancelled = 8, ResultOwnerChanged = 9, ResultParticipantLeft = 10, ResultRecoveryFailed = 11;
        [UdonSynced] private int recoveryFailedMask;
        public int RecoveryFailedMask { get { return recoveryFailedMask; } }
        public bool HasRecoveryFailure { get { return phase == RecoveryFailed; } }
        public bool _DogIsolated(int dog) { return phase == RecoveryFailed && DogInMask(recoveryFailedMask, dog); }
        public bool _BlocksDog(int dog) { return BlocksDog(phase, recoveryFailedMask, dog); }
        [UdonSynced] private int resultCode;
        [UdonSynced] private bool resultAfterCommit;
        public int ResultCode { get { return resultCode; } }
        public bool ResultAfterCommit { get { return resultAfterCommit; } }
        public int _AnswerState(int player) { int i = UserIndex(player); return i < 0 ? 0 : (answers[i] ? 2 : 1); }
        [UdonSynced] private int phase;
        [UdonSynced] private int requestId;
        [UdonSynced] private int hostId = -1;
        [UdonSynced] private int phaseMs, startMs;
        [UdonSynced] private int[] users = new int[80];
        [UdonSynced] private int userCount;
        [UdonSynced] private bool[] answers = new bool[80];
        [UdonSynced] private string[] usageKeys = new string[] { "", "", "", "", "", "" };
        [UdonSynced] private int[] rootOwners = new int[6];
        [UdonSynced] private bool[] washAtRequest = new bool[6];
        [UdonSynced] private int preparedMask, endedMask, placedMask, restoredMask;
        [UdonSynced] private bool showStarted;
        [UdonSynced] private string status = "開発用パレードは未開始です";
        private float _nextCheck;
        private bool _sharedEnded;
        private int _sharedEndedId;
        private float _routeLength;
        private float _nextCleanup;

        public int Phase { get { return phase; } }
        public int RequestId { get { return requestId; } }
        public int HostId { get { return hostId; } }
        public bool BlocksNewUses { get { return phase >= Reserve && phase <= Restore; } }
        public bool Committed { get { return phase >= EndUses && phase <= Restore; } }
        public bool ShowStarted { get { return showStarted; } }
        public float Elapsed { get { return phase == Playing ? (Networking.GetServerTimeInMilliseconds() - startMs) * .001f : 0f; } }
        public string StatusText { get { return status; } }
        public int PendingCount { get { int n = 0; for (int i = 0; i < userCount; i++) if (!answers[i]) n++; return n; } }
        public int ConsentRemainingSeconds { get { return phase==Consent?Mathf.Max(0,Mathf.CeilToInt(consentSeconds-(Networking.GetServerTimeInMilliseconds()-phaseMs)*.001f)):0; } }
        public bool LocalConsentRequired
        {
            get { VRCPlayerApi p = Networking.LocalPlayer; if (phase != Consent || !Utilities.IsValid(p)) return false; int i = UserIndex(p.playerId); return i >= 0 && !answers[i]; }
        }
        public string _ExpectedUsage(int dog) { return dog >= 0 && dog < 6 ? usageKeys[dog] : "invalid"; }
        public bool _AcceptedUser(int player) { int i = UserIndex(player); return i >= 0 && answers[i]; }
        public bool _HasUser(int player) { return UserIndex(player) >= 0; }
        public bool _WasWashing(int dog) { return dog >= 0 && dog < washAtRequest.Length && washAtRequest[dog]; }
        public int _RankOfDog(int dog) { return processionRanks != null && dog >= 0 && dog < processionRanks.Length ? processionRanks[dog] : dog; }
        private int UserIndex(int player) { for (int i = 0; i < userCount; i++) if (users[i] == player) return i; return -1; }

        public void _RequestStart()
        {
            if (!_LocalCanStart()) return;
            if (Networking.IsOwner(gameObject)) Begin(Networking.LocalPlayer);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestStart));
        }
        [NetworkCallable(maxEventsPerSecond: 2)]
        public void RequestStart() { Begin(NetworkCalling.CallingPlayer); }
        public void _Approve() { AnswerLocal(true); }
        public void _Decline() { AnswerLocal(false); }
        private void AnswerLocal(bool allow)
        {
            if (!LocalConsentRequired) return;
            if (Networking.IsOwner(gameObject)) Answer(Networking.LocalPlayer, requestId, allow);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ConsentAnswer), requestId, allow);
        }
        [NetworkCallable(maxEventsPerSecond: 8)]
        public void ConsentAnswer(int id, bool allow) { Answer(NetworkCalling.CallingPlayer, id, allow); }
        public void _Cancel()
        {
            if (Networking.IsOwner(gameObject)) CancelFor(Networking.LocalPlayer, requestId);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(CancelRequest), requestId);
        }
        [NetworkCallable(maxEventsPerSecond: 4)]
        public void CancelRequest(int id) { CancelFor(NetworkCalling.CallingPlayer, id); }
        private void CancelFor(VRCPlayerApi caller, int id)
        {
            if (!Networking.IsOwner(gameObject) || id != requestId || !Utilities.IsValid(caller)) return;
            if (caller.playerId != hostId && (liveSystem == null || !liveSystem.CanManageNow(caller))) return;
            FailCode(ResultCancelled, "開催を取り消しました");
        }
        private void Begin(VRCPlayerApi caller)
        {
            if (!Networking.IsOwner(gameObject) || !_CanHost(caller) || phase != Idle) return;
            resultAfterCommit = false;
            if (!ConfigurationReady() || !SharedOwnersReady()) { resultCode = ResultConfiguration; status = "管理同期または6匹の配置設定が未準備です"; RequestSerialization(); return; }
            if (liveSystem != null && (liveSystem.IsNightWanted || liveSystem.CandyPreparing || liveSystem.DogRecovering))
            { resultCode = ResultOtherShow; status = "別の公演が終了してから依頼してください"; RequestSerialization(); return; }
            resultCode = ResultNone; resultAfterCommit = false; userCount = 0;
            for (int i = 0; i < 80; i++) { users[i] = -1; answers[i] = false; }
            for (int i = 0; i < 6; i++)
            {
                if (!dogs[i]._ReadyForRequest()) { resultCode = ResultConfiguration; status = "犬の同期または洗浄利用者の記録を確認できません"; RequestSerialization(); return; }
                usageKeys[i] = dogs[i]._UsageKey();
                washAtRequest[i] = dogs[i].wash._WashHold();
                for (int j = 0; j < washStations.Length; j++) if (washStations[j]._HeldDog() == i) washAtRequest[i] = true;
                rootOwners[i] = Networking.GetOwner(dogs[i].root.gameObject).playerId;
                int[] ids = dogs[i]._UserIds();
                for (int j = 0; j < ids.Length; j++) if (ids[j] >= 0 && !AddUser(ids[j])) return;
            }
            if (activities != null && activities.Busy && !AddUser(activities.HostId)) return;
            requestId = requestId >= 0x3ffffffe ? 1 : requestId + 1;
            hostId = caller.playerId; preparedMask = 0; endedMask = 0; placedMask = 0; restoredMask = 0; showStarted = false;
            _sharedEnded = false;
            SetPhase(Consent, userCount == 0 ? "6匹の準備を確認しています" : "利用中の方全員の許可を待っています");
        }
        private bool AddUser(int id)
        {
            if (id < 0 || UserIndex(id) >= 0) return true;
            if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(id)) || userCount >= users.Length)
            { resultCode = ResultUsageChanged; status = "利用者を確定できません。時間をおいて再依頼してください"; RequestSerialization(); return false; }
            users[userCount++] = id; return true;
        }
        private void Answer(VRCPlayerApi caller, int id, bool allow)
        {
            if (!Networking.IsOwner(gameObject) || !Utilities.IsValid(caller)) return;
            int i = UserIndex(caller.playerId);
            if (!CanAcceptAnswer(phase, requestId, id, i, i >= 0 && answers[i])) return;
            if (!allow) { FailCode(ResultDeclined, "許可が揃わなかったため取り消しました。遊びはそのまま続けられます"); return; }
            answers[i] = true; RequestSerialization(); _nextCheck = 0f;
        }
        // Pure predicates are extracted by the source probe; the runtime must pass these gates.
        public static bool CanAcceptAnswer(int p, int current, int incoming, int index, bool answered)
        { return p == Consent && current == incoming && index >= 0 && !answered; }
        public static bool CanSeal(int pending, bool unchanged, bool ownersReady) { return pending == 0 && unchanged && ownersReady; }
        public static bool AllSix(int mask) { return mask == 63; }
        public static bool CanBeginShow(int p, int ended, int placed) { return p == Place && AllSix(ended) && AllSix(placed); }
        public static int PhaseAfterOwnerChange(int p) { return p == Consent || p == Reserve ? Idle : (p == EndUses || p == Place ? Restore : p); }
        public static bool DogInMask(int mask, int dog) { return dog >= 0 && dog < 6 && (mask & (1 << dog)) != 0; }
        public static bool BlocksDog(int p, int mask, int dog) { return (p >= Reserve && p <= Restore) || (p == RecoveryFailed && DogInMask(mask, dog)); }
        public static int FailedDogs(int restored, int shared) { return (63 & ~restored) | shared; }
        public static bool RecoveryExpired(int p, int ageMs, int limitMs) { return p == Restore && ageMs >= limitMs; }

        private void Start() { MeasureRoute(); }
        private void Update()
        {
            // Shared resource owners can differ during automatic takeover. Never take ownership to force cleanup.
            if (phase == Restore && Time.time >= _nextCleanup) { _nextCleanup = Time.time + .5f; ReleaseSharedUses(); }
            if (!Networking.IsOwner(gameObject) || phase == Idle || phase == RecoveryFailed || Time.time < _nextCheck) return;
            _nextCheck = Time.time + .2f;
            float age = (Networking.GetServerTimeInMilliseconds() - phaseMs) * .001f;
            if (phase == Consent || phase == Reserve)
            {
                if (!UsageUnchanged() || !SharedOwnersReady()) { FailCode(ResultUsageChanged, "利用者または管理同期が変わりました。遊びを続けたまま再依頼できます"); return; }
                if (age > (phase == Consent ? consentSeconds : preparationSeconds)) { FailCode(phase == Consent ? ResultTimeout : ResultPreparationFailed, "確認の期限が過ぎたため取り消しました"); return; }
                if (phase == Consent && CanSeal(PendingCount, true, true)) { SetPhase(Reserve, "全員の許可を確認しました。6匹を確保しています"); return; }
                if (phase == Reserve && AllSix(preparedMask))
                {
                    // This is the sole commit point. No cleaning, release or root writes precede it.
                    if (!UsageUnchanged()) { FailCode(ResultUsageChanged, "利用状況が変わったため取り消しました"); return; }
                    SetPhase(EndUses, "許可された遊びを終了し、6匹を準備しています");
                }
                return;
            }
            if (phase == EndUses)
            {
                if (!SharedOwnersReady()) { Fail("準備中に管理同期が変わったため復帰します"); return; }
                if (!_sharedEnded || _sharedEndedId != requestId)
                {
                    // Dog washing is completed by every dog before the shared restraints are released.
                    if (AllSix(endedMask))
                    {
                        ReleaseSharedUses();
                        _sharedEnded = true; _sharedEndedId = requestId;
                        SetPhase(Place, "6匹を開始列へ集めています");
                    }
                }
                if (age > preparationSeconds) Fail("6匹の準備が完了しなかったため復帰します");
                return;
            }
            if (phase == Place)
            {
                if (CanBeginShow(phase, endedMask, placedMask))
                { showStarted = true; startMs = Networking.GetServerTimeInMilliseconds() + 1500; SetPhase(Playing, "かぼちゃ灯りの坂道行進"); }
                else if (age > preparationSeconds) Fail("6匹が揃わなかったため本編を開始せず復帰します");
                return;
            }
            if (phase == Playing)
            {
                if (Elapsed >= durationSeconds) { resultCode = ResultFinished; resultAfterCommit = true; SetPhase(Restore, "パレードが終わりました。犬たちは自由に過ごします"); }
                for (int i = 0; i < 6; i++) if (dogs[i] == null || dogs[i].root == null || !dogs[i].root.gameObject.activeInHierarchy)
                { Fail("出演犬の状態を確認できないため公演を終了します"); break; }
                return;
            }
            if (phase == Restore)
            {
                // No fixed-delay claim of recovery: wait for actual dog and shared-resource acknowledgements.
                if (AllSix(restoredMask) && SharedUsesReleased()) { leashes._AdoptParadeHomes(); SetPhase(Idle, status); }
                else if (RecoveryExpired(phase, Networking.GetServerTimeInMilliseconds() - phaseMs, (int)(Mathf.Max(1f, recoverySeconds) * 1000f)))
                {
                    recoveryFailedMask = FailedDogs(restoredMask, SharedUseMask());
                    leashes._AdoptParadeHomes();
                    resultCode = ResultRecoveryFailed; resultAfterCommit = true;
                    SetPhase(RecoveryFailed, "一部の犬の復旧を確認できません。該当犬を保留し、他の犬の遊びを再開しました。このインスタンスでの公演再開はできません");
                    Debug.LogWarning("[KH_Parade] recovery failed request=" + requestId + " dogMask=" + recoveryFailedMask);
                }
            }
        }
        private void ReleaseSharedUses()
        {
            if (leashes != null) leashes._EndForParade();
            if (activities != null) activities._EndForParade();
            if (washStations != null) for (int i = 0; i < washStations.Length; i++) if (washStations[i] != null) washStations[i]._EndForParade();
            for (int i = 0; i < 6; i++) { KD_SleepSlot s = sleepBoard != null ? sleepBoard.SlotOf(i) : null; if (s != null) s._ReleaseForParade(); }
        }
        private bool SharedUsesReleased()
        {
            if (leashes == null || leashes.walkers == null || leashes.walkers.Length != 6 || (activities != null && activities.Busy)) return false;
            for (int i = 0; i < 6; i++)
            {
                if (leashes.walkers[i] >= 0) return false;
                if (sleepBoard != null && sleepBoard.LiveSleeperOf(i) >= 0) return false;
                if (treatBox != null && treatBox.LiveSlotOf(i) != null) return false;
            }
            if (washStations != null) for (int i = 0; i < washStations.Length; i++) if (washStations[i] != null && washStations[i]._HeldDog() >= 0) return false;
            return true;
        }
        private int SharedUseMask()
        {
            int mask = 0;
            for (int i = 0; i < 6; i++)
                if (leashes == null || leashes.walkers == null || i >= leashes.walkers.Length || leashes.walkers[i] >= 0
                    || (sleepBoard != null && sleepBoard.LiveSleeperOf(i) >= 0) || (treatBox != null && treatBox.LiveSlotOf(i) != null)
                    || (activities != null && activities.Busy && activities._IsParticipant(i))) mask |= 1 << i;
            if (washStations != null) for (int i = 0; i < washStations.Length; i++)
                if (washStations[i] != null && washStations[i]._HeldDog() >= 0) mask |= 1 << washStations[i]._HeldDog();
            return mask;
        }
        private bool UsageUnchanged()
        {
            if (liveSystem != null && (liveSystem.IsNightWanted || liveSystem.CandyPreparing || liveSystem.DogRecovering)) return false;
            for (int i = 0; i < userCount; i++) if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(users[i]))) return false;
            for (int i = 0; i < 6; i++)
            {
                if (dogs[i] == null || !dogs[i]._ReadyForRequest() || dogs[i]._UsageKey() != usageKeys[i]) return false;
                if (Networking.GetOwner(dogs[i].root.gameObject).playerId != rootOwners[i]) return false;
            }
            return true;
        }
        private bool SharedOwnersReady()
        {
            if (leashes == null || leashes.gameObject != gameObject || !Networking.IsOwner(leashes.gameObject)) return false;
            if (activities != null && (activities.gameObject != gameObject || !Networking.IsOwner(activities.gameObject))) return false;
            if (treatBox != null && !Networking.IsOwner(treatBox.gameObject)) return false;
            if (washStations == null) return false;
            for (int i = 0; i < washStations.Length; i++) if (washStations[i] == null || !Networking.IsOwner(washStations[i].gameObject)) return false;
            for (int i = 0; i < 6; i++) { KD_SleepSlot s = sleepBoard != null ? sleepBoard.SlotOf(i) : null; if (s != null && !Networking.IsOwner(s.gameObject)) return false; }
            return true;
        }
        public bool ConfigurationReady()
        {
            if (dogs == null || dogs.Length != 6 || startSlots == null || startSlots.Length != 6 || finaleSlots == null || finaleSlots.Length != 6
                || routePoints == null || routePoints.Length < 2 || marchEndSeconds <= marchStartSeconds || entranceEndSeconds <= marchEndSeconds
                || durationSeconds <= entranceEndSeconds || leashes == null || activities == null || sleepBoard == null || treatBox == null
                || liveSystem == null || washStations == null || washStations.Length == 0 || processionRanks == null || processionRanks.Length != 6
                || startRouteDistance < dogSpacing * 5f || leashes.parade != this || activities.parade != this || liveSystem.parade != this) return false;
            int ranks = 0;
            for (int i = 0; i < 6; i++)
            {
                if (dogs[i] == null || dogs[i].root == null || startSlots[i] == null || finaleSlots[i] == null || dogs[i].parade != this
                    || dogs[i].dogIndex != i || dogs[i].startSlot != startSlots[i] || dogs[i].wander == null || dogs[i].wander.paradeDog != dogs[i]
                    || dogs[i].wash == null || dogs[i].wash.paradeDog != dogs[i] || processionRanks[i] < 0 || processionRanks[i] > 5) return false;
                if ((ranks & (1 << processionRanks[i])) != 0) return false;
                ranks |= 1 << processionRanks[i];
                for (int j = 0; j < i; j++) if (dogs[i].root == dogs[j].root) return false;
            }
            for (int i = 0; i < washStations.Length; i++) if (washStations[i] == null || washStations[i].parade != this) return false;
            for (int i = 0; i < routePoints.Length; i++) if (routePoints[i] == null) return false;
            return true;
        }
        private void SetPhase(int next, string message)
        {
            phase = next; phaseMs = Networking.GetServerTimeInMilliseconds(); status = message;
            Debug.Log("[KH_Parade] request=" + requestId + " phase=" + phase + " prepared=" + preparedMask + " ended=" + endedMask + " placed=" + placedMask);
            RequestSerialization();
        }
        private void Fail(string reason) { FailCode(ResultPreparationFailed, reason); }
        private void FailCode(int code, string reason) { if (phase == Idle || phase == Restore || phase == RecoveryFailed) return; resultCode = code; resultAfterCommit = Committed; SetPhase(Committed ? Restore : Idle, reason); }
        public override void OnPlayerLeft(VRCPlayerApi player)
        { if (Networking.IsOwner(gameObject) && phase != Idle && Utilities.IsValid(player) && (player.playerId == hostId || (phase <= Reserve && _HasUser(player.playerId)))) FailCode(ResultParticipantLeft, "関係者が退出したため開催を取り消しました"); }
        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            int next = PhaseAfterOwnerChange(phase);
            if (next == Idle && phase != Idle) FailCode(ResultOwnerChanged, "管理者が交代したため再依頼してください");
            else if (next == Restore && phase != Restore) FailCode(ResultOwnerChanged, "準備中に管理者が交代したため復帰します");
            // Playing retains the clock; never repeat cleaning or the placement teleport.
            _nextCheck = 0f;
        }
        public override bool OnOwnershipRequest(VRCPlayerApi requester, VRCPlayerApi newOwner) { return false; }

        public void _ReportDog(int dog, int id, int step, bool ok)
        {
            if (Networking.IsOwner(gameObject)) AcceptDog(Networking.LocalPlayer, dog, id, step, ok);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(DogReport), dog, id, step, ok);
        }
        [NetworkCallable(maxEventsPerSecond: 30)]
        public void DogReport(int dog, int id, int step, bool ok) { AcceptDog(NetworkCalling.CallingPlayer, dog, id, step, ok); }
        private void AcceptDog(VRCPlayerApi sender, int dog, int id, int step, bool ok)
        {
            if (!Networking.IsOwner(gameObject) || id != requestId || dog < 0 || dog >= 6 || dogs == null || dogs[dog] == null
                || !Utilities.IsValid(sender) || sender != Networking.GetOwner(dogs[dog].root.gameObject) || step != phase) return;
            if (!ok) { Fail("犬の準備確認が成立しなかったため取り消しました"); return; }
            if (phase == Reserve) preparedMask |= 1 << dog;
            else if (phase == EndUses) endedMask |= 1 << dog;
            else if (phase == Place) placedMask |= 1 << dog;
            else if (phase == Restore) restoredMask |= 1 << dog;
            RequestSerialization();
        }
        private void MeasureRoute()
        {
            _routeLength = 0f;
            if (routePoints == null) return;
            for (int i = 1; i < routePoints.Length; i++) if (routePoints[i] != null && routePoints[i - 1] != null)
                _routeLength += Vector3.Distance(routePoints[i - 1].position, routePoints[i].position);
        }
        public Vector3 _RoutePosition(float distance)
        {
            if (routePoints == null || routePoints.Length < 2) return transform.position;
            float remain = Mathf.Max(0f, distance);
            for (int i = 1; i < routePoints.Length; i++)
            {
                Vector3 a = routePoints[i - 1].position, b = routePoints[i].position;
                float length = Vector3.Distance(a, b);
                if (remain <= length) return Vector3.Lerp(a, b, length > .001f ? remain / length : 0f);
                remain -= length;
            }
            return routePoints[routePoints.Length - 1].position;
        }
        public Vector3 _DogTarget(int dog)
        { return _DogTargetAt(Elapsed,dog); }
        public Vector3 _DogTargetAt(float t,int dog)
        {
            if (_routeLength <= 0f) MeasureRoute();
            float speed = (_routeLength - startRouteDistance) / Mathf.Max(1f, marchEndSeconds - marchStartSeconds - 25f);
            int rank = _RankOfDog(dog);
            if (t <= marchStartSeconds) return startSlots[dog].position;
            float d = startRouteDistance + _MarchTime(t) * speed - dogSpacing * rank;
            if (t >= 55f && t <= 80f)
            {
                float centreD = startRouteDistance + _MarchTime(55f) * speed - dogSpacing * (rank / 2 * 2 + .5f);
                Vector3 centre = _RoutePosition(centreD), tangent = _RoutePosition(centreD + .3f) - _RoutePosition(centreD - .3f);
                tangent.y = 0f; tangent.Normalize();
                Vector3 pairAxis=(Vector3.Cross(Vector3.up,tangent)+tangent).normalized;
                Vector3 paired = centre + pairAxis * (rank % 2 == 0 ? .65f : -.65f);
                float blend = Mathf.SmoothStep(0f,1f,Mathf.Clamp01((t-55f)/2.5f)) * Mathf.SmoothStep(0f,1f,Mathf.Clamp01((80f-t)/2.5f));
                return Vector3.Lerp(_RoutePosition(d),paired,blend);
            }
            // Split at the final route corner, instead of funneling all six through one stopped point.
            // Editor validation checks the six straight branches against the actual NavMesh.
            Vector3 branch = routePoints[routePoints.Length-2].position;
            float branchDistance = _routeLength-Vector3.Distance(branch,routePoints[routePoints.Length-1].position);
            if (d < branchDistance) return _RoutePosition(d);
            float branchLength=Vector3.Distance(branch,finaleSlots[dog].position);
            return Vector3.Lerp(branch,finaleSlots[dog].position,Mathf.Clamp01((d-branchDistance)/Mathf.Max(.01f,branchLength)));
        }
        public Vector3 _DogFacing(int dog)
        { return _DogFacingAt(Elapsed,dog); }
        public Vector3 _DogFacingAt(float t,int dog)
        {
            if (_routeLength <= 0f) MeasureRoute();
            if (t >= entranceEndSeconds) return finaleSlots[dog].forward;
            float speed = (_routeLength - startRouteDistance) / Mathf.Max(1f, marchEndSeconds - marchStartSeconds - 25f);
            int rank = _RankOfDog(dog);
            float d = startRouteDistance + _MarchTime(t) * speed - dogSpacing * rank;
            if (t <= marchStartSeconds) return startSlots[dog].forward;
            Vector3 p = _DogTargetAt(t,dog);
            if (t >= 55f && t <= 80f)
            {
                Vector3 forward = _RoutePosition(d+.3f)-_RoutePosition(d-.3f);
                forward.y=0f;forward.Normalize();
                return (Vector3.Cross(Vector3.up,forward)-forward).normalized;
            }
            Vector3 next = _DogTargetAt(t+.2f,dog);
            Vector3 delta = next - p; delta.y = 0f;
            return delta.sqrMagnitude > .001f ? delta.normalized : finaleSlots[dog].forward;
        }
        // The 55-80 s response is a real stop for all six dogs. Remaining travel is 80 s, ~1 m/s.
        // Direct evaluation from shared time also restores late joiners without accumulating a local timer.
        public float _MarchTime(float seconds)
        { return Mathf.Max(0f,seconds-marchStartSeconds)-Mathf.Clamp(seconds-55f,0f,25f); }
        // Authored score: 10 Groove / 11 Reply / 12 Bow / 13 LookLeft / 14 LookRight / 15 Hop.
        public int _DancePose(float t,int dog)
        {
            int rank=_RankOfDog(dog);bool left=rank%2==0;
            if(t<0f)return 0;
            if(t<10f)return 11;
            if(t<25f)return 10;
            if(t<57.5f)return 0;
            if(t<60f)return left?14:13;
            if(t<62.5f)return left?11:13;
            if(t<65f)return left?14:11;
            if(t<67.5f)return dog==1?11:(left?14:13);
            if(t<70f)return dog==0?12:dog==1?11:(left?14:13);
            if(t<75f)return 10;
            if(t<77.5f)return 15;
            if(t<150f)return 0;
            if(t<165f)return rank/2==(int)((t-150f)/5f)?10:(left?14:13);
            if(t<195f)return (int)((t-165f)/5f)%2==0?10:15;
            if(t<200f)return 11;
            if(t<205f)return 12;
            if(t<215f)return 10;
            return 12;
        }
        public float _DanceTime(float t,int dog)
        {
            int pose=_DancePose(t,dog),rank=_RankOfDog(dog);
            if(t<10f)return Mathf.Clamp01((Mathf.Repeat(t,5f)-rank*.3125f)/2.5f);
            if(t>=205f&&t<215f)return 0f;
            if(pose==13||pose==14)return .5f; // listening pose, held; transitions supply the look-in/out.
            if(dog==1&&t>=66.25f&&t<70f)return Mathf.Clamp((t-66.25f)/2.5f,0f,.125f); // lift once more, then hold proudly.
            if(dog==1&&t>=65f&&t<66.25f)return (t-65f)/1.25f;
            return Mathf.Repeat(t,pose==10?5f:2.5f)/(pose==10?5f:2.5f);
        }
    }
}
