using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaDogRun
{
    // KominkaDogRun: shares the Live object's ownership, every shared start is adjudicated here.
    //
    // KominkaHillside (2026-09-20, game-design §4「集合と拠点のリード」): this behaviour sits on the SAME GameObject as
    // KH_LeashManager so one Owner serialises "attach" and "gather" decisions (no cross-owner race). live may be
    // null, the trainer entitlement comes from KH_TrainerPass, leashed dogs never gather, the roster is frozen at
    // start (participants), placement accepts slopes, and the end of a gathering is handed to the manager directly.
    // Race/play (op 2/3) remain in code but are not offered by the hillside menu.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KD_GroupActivities : UdonSharpBehaviour
    {
        public KominkaHillside.KH_HalloweenParade parade;
        public KD_LiveSystem live;
        public KD_DogPack pack;
        public KD_ShopItem formationItem, playItem;
        public KD_BallController[] balls;
        public KD_PhotoDirector photos;
        public KominkaHillside.KH_LeashManager hillsideLeashes;
        public KominkaHillside.KH_TrainerPass hillsidePass;
        [Tooltip("Hillside: accept slots on slopes. Ground normal must be at least this (1 = flat only)")]
        public float minGroundNormal = .9f;
        [Tooltip("Hillside: maximum height difference between a slot's ground and the host (0 = unlimited)")]
        public float maxSlotHeightDelta = .25f;
        public LayerMask groundMask = (1 << 0) | (1 << 11);
        [UdonSynced] private int participants; // bit i = dog i joined this gathering (frozen at start)
        [UdonSynced] private int mode;
        [UdonSynced] private int startedMs;
        [UdonSynced] private int host = -1;
        [UdonSynced] private int revision;
        [UdonSynced] private int formation;
        [UdonSynced] private Vector3 anchor;
        [UdonSynced] private float yaw;
        [UdonSynced] private int dogA = -1, dogB = -1;
        [UdonSynced] private int raceBall = -1, raceWinner = -1, raceThrower = -1, raceRound;
        [UdonSynced] private int raceStartedMs;
        [UdonSynced] private int photoRevision;
        private int _seenPhoto;
        private bool _received;
        private float _nextTick;
        private int _seq;
        private int _requestState;
        private float _requestAt;
        private int _pendingBall = -1, _pendingThrower = -1;
        private float _pendingUntil;
        private Vector3[] _previous;
        private float[] _movingAt;
        private int[] _samples;
        private bool _ballsWatched;   // IMP-DOG-034 A7: _previous holds last frame's positions (the race watch ran last frame)
        private float _readySince = -1;
        public int localFormation = -1;
        // net: callable-ok: read-only state derived from synchronized data
        public int Mode { get { return mode != 0 && Elapsed < 60f && Elapsed >= 0f ? mode : 0; } }
        public int HostId { get { return host; } } // net: callable-ok: read-only
        public float Elapsed { get { return (Networking.GetServerTimeInMilliseconds() - startedMs) * .001f; } } // net: callable-ok: read-only
        public int Formation { get { return formation; } } // net: callable-ok: read-only
        public Vector3 Anchor { get { return anchor; } } // net: callable-ok: read-only
        public float Yaw { get { return yaw; } } // net: callable-ok: read-only
        public int DogA { get { return dogA; } } // net: callable-ok: read-only
        public int DogB { get { return dogB; } } // net: callable-ok: read-only
        public int RaceBall { get { return raceBall; } } // net: callable-ok: read-only
        public int RaceWinner { get { return raceWinner; } } // net: callable-ok: read-only
        public int RaceThrower { get { return raceThrower; } } // net: callable-ok: read-only
        public int RaceRound { get { return raceRound; } } // net: callable-ok: read-only
        public int LocalRequestState { get { return _requestState; } } // net: callable-ok: read-only
        public bool Busy { get { return Mode != 0 || raceBall >= 0 || (live != null && (live.CandyPreparing || live.DogRecovering)); } } // net: callable-ok: read-only
        public bool _IsParticipant(int i) { return i >= 0 && i < 32 && (participants & (1 << i)) != 0; } // net: callable-ok: read-only
        public int ParticipantCount { get { int n = 0; for (int i = 0; i < 6; i++) if (_IsParticipant(i)) n++; return n; } } // net: callable-ok: read-only
        private bool OwnsFormation(VRCPlayerApi caller)
        {
            if (hillsidePass != null) return hillsidePass._OwnedBy(caller);
            return formationItem != null && formationItem.OwnedBy(caller);
        }
        public bool _LocalOwnsFormation() { VRCPlayerApi lp = Networking.LocalPlayer; return Utilities.IsValid(lp) && OwnsFormation(lp); }
        // The same trainer rule as gather's receiver check, for another authority's receiver (BUG-DOG-009:
        // KH_FrisbeeCatch.ReqVariant). Pass an authenticated player (CallingPlayer, or the local player on a direct call).
        public bool _OwnsFormationFor(VRCPlayerApi player) { return Utilities.IsValid(player) && OwnsFormation(player); } // net: callable-ok: read-only entitlement of the given player

        public void _Request(int op, int a, int b)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || _requestState == 1) return;
            Vector3 p = lp.GetPosition();
            float angle = lp.GetRotation().eulerAngles.y;
            _seq++; _requestState = 1; _requestAt = Time.time;
            if (Networking.IsOwner(gameObject)) Handle(lp, op, a, b, p, angle, revision, _seq);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestActivity), op, a, b, p, angle, revision, _seq);
        }
        [NetworkCallable(maxEventsPerSecond: 10)]
        public void RequestActivity(int op, int a, int b, Vector3 p, float angle, int expected, int seq) // net: callable-ok: authenticated sender and current revision checked by authority
        { Handle(NetworkCalling.CallingPlayer, op, a, b, p, angle, expected, seq); }

        private void Handle(VRCPlayerApi caller, int op, int a, int b, Vector3 p, float angle, int expected, int seq)
        {
            if (!Networking.IsOwner(gameObject) || !Utilities.IsValid(caller)) return;
            int result = 3;
            if (expected == revision && (parade == null || !parade.BlocksNewUses || op == 0 || op == 6))
            {
                // 「開催をおわる」: 開催者本人、または部屋の管理者 (オーナー優先、不在時だけ Master。IMP-NET-007)。
                // 管理者は受理直前に在席を測り直す — 代行 Master の要求がオーナー復帰の直後に届いても通さない
                if (op == 0 && Mode != 0 && (caller.playerId == host || (live != null && live.CanManageNow(caller))))
                { EndGathering(); result = 2; }
                else if (op == 6 && live != null && live._StopFor(caller)) { result = 2; }
                else if (op == 4 && live != null && !Busy && live._BeginFor(caller, a))
                { formation = OwnsFormation(caller) && b >= 0 && b < 6 ? b : Random.Range(0, 6); result = 2; }
                else if (op == 5 && live != null && live.HostId == caller.playerId && live.IsNightWanted
                    && OwnsFormation(caller) && a >= 0 && a < 6)
                { formation = a; result = 2; }
                else if (op >= 1 && op <= 3 && !Busy && (live == null || !live.IsNightWanted))
                {
                    bool owns = op == 1 ? OwnsFormation(caller) : playItem != null && playItem.OwnedBy(caller);
                    bool valid = op == 1 ? a >= 0 && a < 6 && ValidPlacement(p, angle, a, caller)
                        : op == 2 ? AnyAvailable() : a != b && Available(a) && Available(b);
                    if (owns && valid)
                    {
                        // Freeze the roster before Mode flips: dogs busy now (leashed, fetching, petted) do not join later.
                        participants = 0;
                        if (op == 1) for (int i = 0; i < 6; i++) if (Available(i)) participants |= 1 << i;
                        mode = op; host = caller.playerId; startedMs = Networking.GetServerTimeInMilliseconds();
                        formation = op == 1 ? a : formation; anchor = p; yaw = angle;
                        if (op == 3) { anchor = pack.dogWander[a].areaCenter.position; yaw = 0; }
                        dogA = a; dogB = b; _readySince = -1f; result = 2;
                    }
                }
            }
            if (result == 2) { revision++; RequestSerialization(); }
            if (caller.isLocal) _requestState = result;
            else SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ActivityReply), caller.playerId, seq, result);
        }
        [NetworkCallable(maxEventsPerSecond: 20)]
        public void ActivityReply(int player, int seq, int result) // net: callable-ok: only object authority can acknowledge an outstanding local request
        {
            VRCPlayerApi sender = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(sender) || sender != Networking.GetOwner(gameObject)) return;
            if (Networking.LocalPlayer != null && player == Networking.LocalPlayer.playerId && seq == _seq && _requestState == 1)
                _requestState = result;
        }
        // Owner only. The manager (same Owner) turns each participant's current spot into its new roaming home.
        public void _EndForParade()
        {
            if (parade == null || !parade.Committed || !Networking.IsOwner(gameObject)) return;
            if (mode == 0 && raceBall < 0) return;
            EndGathering(); raceBall = -1; raceWinner = -1; raceThrower = -1; revision++; RequestSerialization();
        }

        private void EndGathering()
        {
            bool wasGather = mode == 1;
            mode = 0;
            if (wasGather && hillsideLeashes != null) hillsideLeashes._OnGatherEnded(participants);
        }

        public KominkaHillside.KH_FrisbeeCatch[] frisbeeCatchers; // hillside: per dog, null where no disc yet

        public bool Available(int i) // net: callable-ok: read-only eligibility
        {
            if (pack == null || pack.dogFetch == null || i < 0 || i >= pack.dogFetch.Length) return false;
            KD_DogFetchController f = pack.dogFetch[i]; KD_DogWanderController w = pack.dogWander[i];
            if (hillsideLeashes != null && hillsideLeashes._IsLeashed(i)) return false;
            if (frisbeeCatchers != null && i < frisbeeCatchers.Length && frisbeeCatchers[i] != null && frisbeeCatchers[i].IsEngaged) return false; // IMP-DOG-005
            if (Mode == 1 && !_IsParticipant(i)) return false;
            return f != null && w != null && f.gameObject.activeInHierarchy && f.IsIdle && !f.PetActive
                && f.WaterPose == 0   // IMP-DOG-020: a wading dog does not join (it walks out instead, DES-DOG-004 B-1)
                && !w.CandyReserved && !w.ParadeReserved
                && (w.petController == null || !w.petController.IsPetting)
                && (w.sleepBoard == null || !w.sleepBoard.IsSleeping(i)) && !w.IsAsleep && !w.IsResting;
        }
        private bool AnyAvailable()
        { if (pack == null || pack.dogFetch == null) return false; for (int i = 0; i < pack.dogFetch.Length; i++) if (Available(i)) return true; return false; }

        // ---- 隊形の幾何 (2026-09-13: 固定メートルから体格ベースへ。DES-DOG-008) ----
        // 犬の半径 (KD_DogPack.dogRadius = 体幹長の半分) は静的な名簿値なので、全クライアントで同じ隊形になる。
        // 横に並ぶ距離は「幅」(半径 × widthRatio)、前後に並ぶ距離は「長さ」(半径) で数える。
        // 通常隊形は接触距離 × gapRatio (当たるか当たらないか)、ぎゅっと集合だけ huddleRatio (めり込み許容)。
        // 隊形中の犬どうしは KD_DogPack の押し出しから外れる (InFormation) ので、密着させても往復しない。
        [Tooltip("体の幅 ≒ 半径 × これ。横並びの接触距離 = 両者の幅の和。実機で詰める")]
        public float widthRatio = .55f;
        [Tooltip("通常隊形: 接触距離 × これ。1.0 = 触れる、1.05 = 紙一重")]
        public float gapRatio = 1.05f;
        [Tooltip("ぎゅっと集合: 接触距離 × これ。1 未満で体が重なる")]
        public float huddleRatio = .85f;
        [Tooltip("輪の最小半径 (m)。中心に撮る人が立ち、犬の鼻先が人に届く距離")]
        public float ringMinRadius = .9f;

        private float Radius(int i)
        { return pack != null && pack.dogRadius != null && i >= 0 && i < pack.dogRadius.Length && pack.dogRadius[i] > .001f ? pack.dogRadius[i] : .25f; }
        private float Width(int i) { return Radius(i) * widthRatio; }
        // 行 [first, first+count) の中で i の x。隣どうし (幅 + 幅) × ratio で肩を寄せ、行の広がりの中心を 0 にする
        private float RowX(int first, int count, int i, float ratio)
        {
            float x = 0, total = 0;
            for (int k = first; k < first + count - 1; k++) { float step = (Width(k) + Width(k + 1)) * ratio; total += step; if (k < i) x += step; }
            return x - total * .5f;
        }
        private float RowDepth(int first, int count)
        { float m = 0; for (int k = first; k < first + count; k++) m = Mathf.Max(m, Radius(k)); return m; }

        public Vector3 Offset(int shape, int i) // net: callable-ok: pure formation geometry from the static roster
        {
            if (i < 0 || i >= 6) return Vector3.zero;
            if (shape == 1)
            {
                float total = 0, s = 0;
                for (int k = 0; k < 6; k++) { float step = (Width(k) + Width((k + 1) % 6)) * gapRatio; total += step; if (k < i) s += step; }
                float radius = Mathf.Max(total / (2f * Mathf.PI), ringMinRadius);
                float a = total > .001f ? s / total * 2f * Mathf.PI : i * Mathf.PI / 3f;
                return new Vector3(Mathf.Sin(a) * radius, 0, Mathf.Cos(a) * radius);
            }
            if (shape == 3)
            {
                int first = i < 3 ? 0 : 3; float s = 0;
                for (int k = first; k < i; k++) s += (Radius(k) + Radius(k + 1)) * .5f * (1f + widthRatio) * gapRatio;
                float apex = (Width(0) + Width(3)) * gapRatio * .5f;
                return new Vector3((i < 3 ? -1f : 1f) * (apex + s * .75f), 0, -s * .66f);
            }
            int row = i / 3;
            float dz = (RowDepth(0, 3) + RowDepth(3, 3)) * (row - .5f);
            if (shape == 2) return new Vector3(RowX(row * 3, 3, i, huddleRatio), 0, dz * huddleRatio);
            if (shape == 4) return new Vector3(RowX(row * 3, 3, i, gapRatio * 1.3f) + row * .25f, 0, dz * gapRatio * 1.3f + (i % 2) * .2f);
            if (shape == 5) return new Vector3(RowX(row * 3, 3, i, gapRatio), 0, dz * gapRatio);
            return new Vector3(RowX(0, 6, i, gapRatio), 0, 0);
        }
        public float MarkerDiameter(int i) // net: callable-ok: pure geometry; the ground marker shows the dog's own body width
        { return Mathf.Max(.15f, Width(i) * 2f); }
        public Vector3 Slot(int i) // net: callable-ok: target from synchronized placement
        { return anchor + Quaternion.Euler(0, yaw, 0) * Offset(formation, i); }
        public bool Sit(int i) // net: callable-ok: pose from shared formation
        { return formation == 1 || formation == 2 || (formation == 5 && i >= 3); }
        public bool ValidPlacement(Vector3 p, float angle, int shape, VRCPlayerApi caller) // net: callable-ok: read-only ground and clearance validation
        {
            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.sqrMagnitude)
                || float.IsNaN(angle) || float.IsInfinity(angle) || !Utilities.IsValid(caller) || (caller.GetPosition() - p).sqrMagnitude > 9f) return false;
            for (int i = 0; i < 6; i++)
            {
                Vector3 q = p + Quaternion.Euler(0, angle, 0) * Offset(shape, i);
                RaycastHit hit;
                // 障害物は枠ごとの体格 (幅) で見る — 壁沿いや布団の横でも自分の体が収まれば組める (部屋の中で組めるようにした。2026-09-13)
                // Hillside: cast from higher so slots below the host on a slope still find ground; clearance sphere follows the normal.
                if (!Physics.Raycast(q + Vector3.up * 1.5f, Vector3.down, out hit, 4f, groundMask, QueryTriggerInteraction.Ignore)
                    || hit.normal.y < minGroundNormal || (maxSlotHeightDelta > 0f && Mathf.Abs(hit.point.y - p.y) > maxSlotHeightDelta)
                    || Physics.CheckSphere(hit.point + hit.normal * .3f, Mathf.Max(.12f, Width(i)), groundMask, QueryTriggerInteraction.Ignore)) return false;
            }
            return AnyAvailable();
        }

        public void _BallThrown(int index)
        {
            if (Mode != 2 || Networking.LocalPlayer == null) return;
            if (Networking.IsOwner(gameObject)) AcceptThrow(Networking.LocalPlayer, index);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RaceThrow), index);
        }
        [NetworkCallable(maxEventsPerSecond: 12)]
        public void RaceThrow(int index) // net: callable-ok: authentic ball owner, active race, motion evidence and eligibility required
        { AcceptThrow(NetworkCalling.CallingPlayer, index); }
        private void AcceptThrow(VRCPlayerApi caller, int index)
        {
            if (!Networking.IsOwner(gameObject) || !Utilities.IsValid(caller) || Mode != 2 || raceBall >= 0 || balls == null || index < 0 || index >= balls.Length) return;
            if (Networking.GetOwner(balls[index].gameObject) != caller) return;
            _pendingBall = index; _pendingThrower = caller.playerId; _pendingUntil = Time.time + 1.5f;
        }
        public bool BallClaimed(int index, int dog) // net: callable-ok: protects a ball assigned to another dog's Fetch
        { return raceBall == index && raceWinner >= 0 && raceWinner != dog; }
        private void Start()
        {
            _previous = new Vector3[balls.Length]; _movingAt = new float[balls.Length]; _samples = new int[balls.Length];
            for (int i = 0; i < balls.Length; i++)
            {
                _previous[i] = balls[i].transform.position; _movingAt[i] = -100f;
                balls[i].activities = this; balls[i].activityBallIndex = i;
            }
            _seenPhoto = photoRevision; _received = Networking.IsOwner(gameObject);
        }
        public override void OnDeserialization()
        { if (!_received) { _seenPhoto = photoRevision; _received = true; } }
        private void Update()
        {
            if (_requestState == 1 && Time.time - _requestAt > 4f) _requestState = 4;
            if (photoRevision != _seenPhoto) { _seenPhoto = photoRevision; if (Mode == 1 && photos != null) photos._GroupPhoto(anchor, yaw); }
            if (!Networking.IsOwner(gameObject) || _previous == null) return;
            // IMP-DOG-034 A7: the ball watch feeds only the race throw check (_movingAt, read at _pendingBall, which needs Mode 2), so it
            // runs only while the synced mode is the race (op 2; the hillside menus never request it). Entering it, the first frame
            // re-bases _previous instead of comparing against a position from before the race.
            if (mode == 2)
            {
                for (int i = 0; i < balls.Length; i++)
                {
                    Vector3 p = balls[i].transform.position;
                    if (_ballsWatched && (p - _previous[i]).magnitude > .008f && !balls[i].IsHeldNow)
                    { _samples[i]++; if (_samples[i] >= 2) _movingAt[i] = Time.time; }
                    else if (balls[i].IsHeldNow) _samples[i] = 0;
                    _previous[i] = p;
                }
                _ballsWatched = true;
            }
            else _ballsWatched = false;
            if (Time.time < _nextTick) return; _nextTick = Time.time + .1f;
            if (mode != 0 && (Mode == 0 || !Utilities.IsValid(VRCPlayerApi.GetPlayerById(host)))) { EndGathering(); revision++; RequestSerialization(); }
            if (_pendingBall >= 0)
            {
                int i = _pendingBall;
                if (Time.time > _pendingUntil || Mode != 2 || raceBall >= 0) _pendingBall = -1;
                else if (!balls[i].IsHeldNow && Time.time - _movingAt[i] < .4f && Networking.GetOwner(balls[i].gameObject).playerId == _pendingThrower && pack.dogFetch[i].IsIdle && AnyAvailable())
                { raceBall = i; raceWinner = -1; raceThrower = _pendingThrower; raceRound++; raceStartedMs = Networking.GetServerTimeInMilliseconds(); _pendingBall = -1; RequestSerialization(); }
            }
            if (raceBall >= 0)
            {
                float age = (Networking.GetServerTimeInMilliseconds() - raceStartedMs) * .001f;
                if (raceWinner < 0)
                {
                    if (Mode != 2 || age > 15f || balls[raceBall].IsHeldNow) { raceBall = -1; RequestSerialization(); }
                    else
                    {
                        int winner = -1; float closest = .55f;
                        for (int i = 0; i < pack.dogRoots.Length; i++)
                        {
                            if (!Available(i)) continue;
                            // IMP-DOG-027 (Codex 4-2): a dog being washed is not picked as the race winner (the winner goes straight
                            // to ChaseStart, past StartFetch's wash gate). Only here: Available() also serves the gathering
                            if (pack.dogWander[i].WashHeld) continue;
                            float distance = Vector3.Distance(pack.dogRoots[i].position, balls[raceBall].transform.position);
                            if (distance < closest) { closest = distance; winner = i; }
                        }
                        if (winner >= 0) { raceWinner = winner; RequestSerialization(); }
                    }
                }
                else if ((pack.dogFetch[raceWinner].CompletedRaceRound == raceRound && pack.dogFetch[raceWinner].IsIdle) || age > 50f)
                { raceBall = -1; raceWinner = -1; RequestSerialization(); }
            }
            if (Mode == 1)
            {
                int ready = 0, expected = 0;
                for (int i = 0; i < pack.dogWander.Length; i++)
                { if (!Available(i)) continue; expected++; if (pack.dogWander[i].IsGroupReady) ready++; }
                if (expected > 0 && ready == expected)
                {
                    if (_readySince < 0f) _readySince = Time.time;
                    if (Time.time - _readySince > 1.5f && photoRevision != revision) { photoRevision = revision; RequestSerialization(); }
                }
                else _readySince = -1f;
            }
        }
    }
}
