using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    // IMP-DOG-025 (DES-DOG-006 §1-5 / §1-6, prototype §7): a dog that is being washed stands still (no body turn since
    // 2026-09-30), glances at the washer now and then and tilts its head inside a glance when the stroke moves to another part.
    // IMP-DOG-027 (design §1-5): the leashed stop (WashLeashStop / WashLeashHold, called from UpdateHillsideLeash), the gates
    // (StartFetch / CanPlay / ReqAttach read WashHeld) and the stronger errands (gathering, the sleep call, the travel ticket).
    //
    // Unleashed (UpdateHillsideWash, Update in the main file): after the proximity update (_paused), before
    // ContinueSelectedBehavior. Everything that must win over washing has already returned above that point: the leash
    // (UpdateHillsideLeash), the Candy performance, "fetchController == null", and Fetch / Frisbee (!fetchController.IsIdle ||
    // FrisbeeActive). So no extra check for them here.
    //
    // Leashed (UpdateHillsideLeash, HillsideLeash.cs): Update returns at the leash line, so the branch above is never reached.
    // WashLeashStop decides on every client before LeashPose and the non-Owner return, so the head (toward the washer) and the
    // absence of LieDown / sniff poses are the same on every screen; the Owner stops the dog after the Return block and before
    // rest (WashLeashHold). Only the holder's own stroke stops the dog, only while the holder stands and the leash is slack.
    //
    // Every client runs these branches (Update runs the decisions on all clients, IMP-NET-005). _WashHold() comes from the synced
    // washMs / washSeen, so all clients reach the same answer; Coast writes the root only on the Owner, and the head
    // gaze / tilt is injected locally on every client (KD_DogPetController._SetGaze is per frame, called from Update).
    //
    // Writers are not added: the root is written only through the existing Owner-only movers (Coast eases the dog to a stop,
    // no turn), no pose Bool is raised directly (the dog is washed standing; IMP-DOG-028's satisfied sit is a
    // _wantSit request that LateUpdate writes, the paw lift a WashPawPose request for the companion writer),
    // the legs go through ApplyMoveAnim (via Coast), the head through the pet controller's single additive writer
    // (IMP-DOG-028: the shake is composed there too, with the body bones written just before the head).
    public partial class KD_DogWanderController
    {
        public KominkaHillside.KH_DogWash wash;   // null = washing does nothing (unwired scenes keep the old behaviour)

        // Frame flag: lowered at the top of Update, raised by the wash branches only (same lifetime as _treatBusy etc.).
        private bool _washActive;
        private int _washLastFrame = -10;      // last frame a wash branch ran (entry edge = not the previous frame)
        private int _washSeenPart = -1;        // last stroke part seen, for the tilt trigger
        private float _washTiltStart = -1000f; // start of the current head tilt
        private float _washTiltNextAt;         // earliest start of the next tilt (4 s spacing)
        private float _washTiltSign = 1f;      // alternates left / right
        // IMP-DOG-027 leashed stop. Local, never synced (every client derives it from synced facts).
        private bool _washLeashStop;           // this frame's decision (read by the harness; also the slack hysteresis)

        const float WashTiltDegrees = 22f;     // prototype §7 / design §1-6: 18-25 deg
        const float WashTiltSeconds = 0.9f;    // out and back
        const float WashTiltSpacing = 4f;      // "開始を 4 秒あける"
        const float WashGazeRise = 0f;         // _WasherPosition() returns the washer's head (prototype §6), so nothing is added
        // 2026-09-30 user decision (PCVR): the washed dog does not turn its body at all ("動くたびにこっち向くから横が洗えない"),
        // and looks at the washer only now and then: one glance of washGazeSeconds every washGazeIntervalMin..Max seconds;
        // the head tilt happens only inside a glance. Outside the glances the head is left to the clip (no _SetGaze).
        [Header("IMP-DOG-028 洗われ中の反応 (調整値。0 以下は既定値に戻す)")]
        [Tooltip("洗う人をちらっと見る間隔の最小 (秒)")] public float washGazeIntervalMin = DefaultWashGazeIntervalMin;
        [Tooltip("同 最大 (秒)")] public float washGazeIntervalMax = DefaultWashGazeIntervalMax;
        [Tooltip("ちらっと見る長さ (秒)")] public float washGazeSeconds = DefaultWashGazeSeconds;
        [Tooltip("満足の合図で座るのは cleanSerial がこの倍数の時だけ (5 = 5 回に 1 回。同期値から決まるので全員で揃う)")] public int washSitEvery = DefaultWashSitEvery;
        [Tooltip("濡れのブルブルの間隔 (秒)")] public float washShakeWetSpacing = DefaultWashShakeWetSpacing;
        [Tooltip("足上げの間隔 (秒)")] public float washPawSpacing = DefaultWashPawSpacing;
        const float DefaultWashGazeIntervalMin = 8f, DefaultWashGazeIntervalMax = 15f, DefaultWashGazeSeconds = 1.5f;
        const int DefaultWashSitEvery = 5;
        const float DefaultWashShakeWetSpacing = 20f, DefaultWashPawSpacing = 20f;
        private float _washGazeUntil = -1000f;  // end of the current glance
        private float _washGazeNextAt;          // start of the next glance
        private int _washGazeCount, _washTiltCount;   // harness read-outs
        // Leashed stop (provisional until Play): the hand must be well inside the leash (FollowHand walks below 0.7x), with a
        // wider band to keep a stop that has started (the hand moves a little while washing).
        const float WashLeashSlackEnter = 0.7f, WashLeashSlackHold = 0.85f;

        // ---- IMP-DOG-028: reactions (design §1-6, trial values until Play) ----
        // Clips go through the dog's own voice source (barkSource: already on the personal-volume ledger, design §1-8).
        [Tooltip("IMP-DOG-028: ブルブルの音 (barkSource で PlayOneShot)。null なら無音。Wash/11 が KH_Wash_Shake を配線する")]
        public AudioClip washShakeClip;
        [Tooltip("IMP-DOG-028: 満足の合図の短い声 (barkSource で PlayOneShot)。null なら barkClip を小さく高めに鳴らす")]
        public AudioClip washHappyClip;
        const float WashShakeSeconds = 1.2f;     // design §1-6 / IMP-DOG-028: 1.2 s, 12 Hz, 15 deg -> 0 (KD_DogPetController)
        const float WashShakeDelay = 0.8f;       // "水を止めて 0.8 秒後"
        const float WashShakeCleanSpacing = 8f;  // the clean shake (only on the sitting edges) at most once per 8 s
        const float WashShakeFoamMax = 0.15f;    // foam before the rinse is never shaken off (the rinse is the show)
        [Tooltip("濡れのブルブルのきっかけ: 有効セルの濡れの平均がこれ以上 (シャワーを横腹へ 2 秒で約 0.07。0 以下は既定値)")]
        public float washShakeWet = DefaultWashShakeWet;
        const float DefaultWashShakeWet = 0.03f;
        const float WashSitSeconds = 2.5f;       // satisfied: sit this long (the strokes still land: part colliders are bone children)
        const float WashPawSeconds = 1.5f;       // comic: lift the paw when a front leg starts being washed (spacing: washPawSpacing)
        const float WashHappySeconds = 2.5f;     // satisfied without the sit: tail wag + smile this long
        const float WashStrokeRecent = 0.35f;    // "ストローク中" = the washer's last stroke is this recent
        const float WashStrokeWag = 0.35f;       // thin tail wag while stroked (_SetUrge level)
        const float WashStrokeMetozi = 0.4f;     // eyes narrowed a little while stroked (x metoziMax)
        const float WashHappyEgao = 0.9f;        // satisfied smile (x egaoMax)
        const float WashHappyVolume = 0.6f;      // barkClip stand-in for the short happy voice
        const float WashCleanPendingSeconds = 1f;// a clean edge not consumed by a wash frame within this is dropped
        const float WashFxRange = 15f;           // splash only when the local player is this close
        // Wet shake trigger (Owner only until KH_DogWash syncs a shake counter; see WashReact)
        private bool _washShakeArmed;            // a stroke was seen within WashShakeDelay (one chance per stroke end)
        private float _washShakeWetNextAt;
        private float _washShakeCleanNextAt;
        private float _washShakeUntil = -1000f;  // splash / sound window of the current shake
        private float _washSplashNextAt;
        private int _washShakeCount;             // harness read-outs
        private int _washShakeKind;              // 1 = wet (stroke end), 2 = clean edge
        // Clean edge (every client, from the synced cleanSerial; WashCleanTick at Update top)
        private bool _washCleanInit;
        private int _washCleanSeen;
        private bool _washHeldPrev;              // KH_DogWash had a state last frame (_HasState): an edge seen without it is a first receive
        private int _washShakeSeen;              // synced shake counter (KH_DogWash._ShakeSerial), every client
        private bool _washShakePending;
        private float _washShakePendingAt;
        private bool _washCleanPending;
        private float _washCleanPendingAt;
        // Satisfied sequence: [start, sitAt) shake, [sitAt, until) sit; tail wag and smile over the whole window
        private float _washSatisfyStart = -1000f;
        private float _washSatisfySitAt = -1000f;   // the sit (sitting edges) / the voice (all edges) from here
        private bool _washSatisfySits;               // this edge sits (cleanSerial % washSitEvery == 0)
        private float _washSatisfyUntil = -1000f;
        private bool _washVoiceDone = true;
        private int _washSatisfiedCount;
        // Paw lift
        private int _washPawSeenPart = -1;
        private float _washPawUntil = -1000f;
        private float _washPawNextAt;
        private int _washPawCount;

        /// <summary>IMP-DOG-028: this frame's paw-lift request (CallReady + CallPose 3 = CallPaw on the Hillside controllers).
        /// Frame-accurate for LateUpdate: _washActive is this frame's flag. Read by LateUpdate's ApplyCompanionAnimation call
        /// (KD_DogWanderController.cs), lowest priority there.</summary>
        public bool WashPawPose { get { return _washActive && Time.time < _washPawUntil && Time.time >= _washSatisfyUntil; } } // net: callable-ok: read-only

        /// <summary>Being washed this frame (a wash branch ran). Frame flag, every client.</summary>
        public bool IsBeingWashed { get { return _washActive; } } // net: callable-ok: read-only

        /// <summary>IMP-DOG-027: the wash hold is on (a stroke within holdSeconds, synced). What the gates read: StartFetch
        /// (KD_DogFetchController), CanPlay (KH_FrisbeeCatch) and ReqAttach (KH_LeashManager). Not the frame flag - the gates run
        /// outside this Update and on clients that did not run the branch (design §1-5 「入口が読むのは有効期限から求めた状態」).</summary>
        /// IMP-DOG-035: also true while a wash station holds this dog (called to the station: the same gates, design §3).
        /// 2026-10-02: and while a released dog rides the stand down (HillsideStationLeave.cs, at most liftMax / liftSpeed + 1 s), so no
        /// Fetch / disc / new leash moves the root off a raised stand.
        public bool WashHeld { get { return (wash != null && wash._WashHold()) || HeldStation() != null || _leaveLowering; } } // net: callable-ok: read-only

        // ================= IMP-DOG-035: called to a wash station (docs/plans/2026-10-01-wash-call-and-height.md §3 / §4) =================
        //
        // The station (KH_WashStationCall) syncs heldDog / callerId / arrived / lift; every client reads them here and runs the same
        // branch. Walking there: the gathering's and the sleep call's feet (SleepStep = GatherStep on a complete NavMesh path, else
        // StepToward straight), with the leash Return's TeleportTo (NavMesh-checked, FlagDiscontinuity) to appear StationAppear m
        // from the station first when farther than StationFar. On the anchor: the placed-sleep freeze (UpdatePlacedSleep): speed /
        // move dir / stop hold zeroed and ApplyMoveAnim, and the Owner writes the root itself every frame - the position eased to
        // the anchor (which rides on liftRoot, so the root's y IS the stand's height) and the rotation to standYaw. Nothing else
        // writes the root meanwhile: Coast / StepToward (their ground snap) are not called, the leash branch returns into this one,
        // and KD_DogPack's push-apart (which snaps to the ground) never pushes a dog on the stand (StationLocked).
        public KominkaHillside.KH_WashStationCall[] washCalls;   // all stations (max 4); null / empty = never called (old behaviour)

        const float StationFar = 30f;          // farther than this: appear near the station first (design §4)
        const float StationAppear = 6f;        // ... this far from the anchor, on the dog's side
        const float StationArrive = 0.3f;      // horizontal distance that counts as "on the stand"
        const float StationSettleSpeed = 1.5f; // m/s, the root eased onto the anchor (faster than the stand's 0.5 m/s)
        const float StationTurnRate = 180f;    // deg/s, the root turned to standYaw once on the stand
        const float StationWaterTimeout = 20f; // in the water this long after the call: end the play and appear near the station
        private float _stationWaterSince = -1f;
        private int _stationIdx = -1;          // the station holding this dog last tick (-1 = none)
        private bool _stationLocked;           // on the stand (local; the synced `arrived` also counts)
        private float _stationTeleportNext;
        private int _stationFrames;            // harness read-out: frames handled by the station branch

        /// <summary>The station holding this dog now (synced heldDog), or null.</summary>
        KominkaHillside.KH_WashStationCall HeldStation()
        {
            int i = HeldStationIndex();
            return i >= 0 ? washCalls[i] : null;
        }

        /// <summary>The active station holding this dog; when two hold it (different Owners, the older one not yet let go), the
        /// newest hold wins (heldMs, ties: lower stationIndex) - the same rule KH_WashStationCall._Superseded applies (Codex 2).
        /// An inactive station is never a holder (Codex 3: its Update and release paths are stopped).</summary>
        int HeldStationIndex()
        {
            // IMP-DOG-034 C1-1a: the scan (4 stations x activeInHierarchy + 2 cross-behaviour calls) ran twice per tick (StationTick at
            // the top, UpdateWashStation in the leash / wash branch; WaterRequester a third time). It only reads synced station state,
            // which nothing in this tick changes except our own _ReleaseDog (WashErrandTick invalidates after it), so inside this dog's
            // own tick the first answer is reused. Invalidated at the top of UpdateTick. Outside the tick (WashHeld read by other
            // behaviours) it scans as before.
            if (_inTick && _heldStationMemoFrame == Time.frameCount) return _heldStationMemo;
            int found = HeldStationIndexScan();
            if (_inTick) { _heldStationMemo = found; _heldStationMemoFrame = Time.frameCount; }
            return found;
        }

        private int _heldStationMemoFrame = -1;   // IMP-DOG-034 C1-1a
        private int _heldStationMemo = -1;

        int HeldStationIndexScan()
        {
            if (washCalls == null || dogIndex < 0) return -1;
            int best = -1, bestMs = 0, bestIdx = 0;
            for (int i = 0; i < washCalls.Length; i++)
            {
                KominkaHillside.KH_WashStationCall s = washCalls[i];
                if (s == null || !s.gameObject.activeInHierarchy || s._HeldDog() != dogIndex) continue;
                int ms = s._HeldMs();
                if (best < 0 || ms - bestMs > 0 || (ms == bestMs && s.stationIndex < bestIdx)) { best = i; bestMs = ms; bestIdx = s.stationIndex; }
            }
            return best;
        }

        /// <summary>Every client, every tick (from WashErrandTick, before the early returns): the release edge. Unlocks, gives the
        /// push-apart its radius back and (Owner) puts the root back on the ground as the placed sleep does on its exit.</summary>
        void StationTick()
        {
            int idx = HeldStationIndex();   // the same scan as HeldStation (active stations only, the newest hold)
            if (idx == _stationIdx) return;
            _stationWaterSince = -1f;
            if (_stationIdx >= 0)
            {
                // 2026-10-02: released (not to another station): the wash is over - drop the hold and walk out at once, riding a
                // raised stand down first (HillsideStationLeave.cs). True = riding down: no ground snap here (it would pop the dog).
                bool riding = idx < 0 && StationLeaveEdge(_stationIdx);
                // released (or moved to another station): back to the ground, then the usual behaviour picks up next frame
                if (!riding && _stationLocked && _dogOwner && dogRoot != null) dogRoot.position = GroundAfterPlacedSleep(dogRoot.position); // net: owner-only: _dogOwner、洗い場の台から下りる
                _stationLocked = false;
                _behaviorSelectionValid = false;
            }
            _stationIdx = idx;
        }

        /// <summary>On a wash station's stand (local lock while still held). KD_DogPack reads it and never pushes this dog (the
        /// push snaps to the ground and would drop it off a raised stand); the dog keeps its radius, so others are pushed as before.</summary>
        /// 2026-10-02: also while riding a raised stand down after the release (HillsideStationLeave.cs).
        public bool StationLocked { get { return (_stationLocked && _stationIdx >= 0) || _leaveLowering; } } // net: callable-ok: read-only

        /// <summary>Unleashed (from UpdateHillsideWash) and leashed by the caller (from UpdateHillsideLeash): go to the station and
        /// stay. False = not held / yields (water first, the errands release it), and nothing was touched.</summary>
        bool UpdateWashStation(bool leashed)
        {
            KominkaHillside.KH_WashStationCall st = HeldStation();
            if (st == null) return false;
            if (fetchController == null) return false;
            // The errands release the dog on their accept edge (WashErrandTick)
            if (WashYieldsToErrand()) return false;
            // In the water: the water play walks the dog out by its own exit first (the station hold is an errand for it:
            // WaterRequester, design B-1 「受理は上陸後」) and starts no new play while held. Nothing here is touched meanwhile.
            // If it has not landed within StationWaterTimeout (stuck), the Owner ends the play and lets the dog appear near the
            // station (the same TeleportTo as a far call), with the swim height dropped (WaterClearFloat).
            if (WashYieldsToWater())
            {
                if (_stationWaterSince < 0f) _stationWaterSince = Time.time;
                if (!(_dogOwner && Time.time - _stationWaterSince > StationWaterTimeout)) return false;
                Vector3 a0 = st._AnchorPosition();
                Vector3 f0 = a0 - dogRoot.position; f0.y = 0f;
                WaterEnd("station-timeout");
                WaterClearFloat();
                Vector3 goal = f0.sqrMagnitude > 0.01f ? a0 - f0.normalized * Mathf.Min(StationAppear, f0.magnitude) : a0;
                // TeleportTo needs the NavMesh under the dog too, which a swimming dog may not have: then place it on the
                // sampled goal directly (the same discontinuity flag)
                if (!TeleportTo(goal))
                {
                    UnityEngine.AI.NavMeshHit hit;
                    if (UnityEngine.AI.NavMesh.SamplePosition(goal, out hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        VRC.SDK3.Components.VRCObjectSync sync = dogRoot.GetComponent<VRC.SDK3.Components.VRCObjectSync>();
                        if (sync != null) sync.FlagDiscontinuity();
                        _dbgWriter = DbgWStation; dogRoot.position = hit.position; // net: owner-only: _dogOwner checked above
                        _speed = 0f;
                    }
                }
                WaterPoseRefresh();
                _stationWaterSince = Time.time;   // one try per timeout
                if (WashYieldsToWater()) return false;
            }
            else _stationWaterSince = -1f;
            // Landed: an ease back to the ground that the play did not finish must not outlive it (the leash's WaterClearFloat
            // rule, map-and-travel §F.4). Flags only; the walk below snaps to the ground.
            if (_WaterFloating()) WaterClearFloat();
            if (leashed)
            {
                // Only the caller's own leash (somebody else's leashed dog is never held: the station refuses / releases it)
                if (hillsideLeashes == null || hillsideLeashes.walkers[dogIndex] != st._CallerId()) return false;
                if (_dogOwner) leashMode = LeashWait;   // a detour / stopover in progress ends as "interrupted" next frame
            }
            _activityBusy = true;            // bound to the station (TickBusy every frame, ShowEngaged for the treat assignment)
            _behaviorSelectionValid = false;
            _stationFrames++;
            if (_restPhase != RestPhaseNone) EndRest();

            Vector3 a = st._AnchorPosition();
            Vector3 flat = a - dogRoot.position; flat.y = 0f;
            float dist = flat.magnitude;
            // The synced `arrived` locks a client that has not seen the step onto the stand yet, but only near it (a late joiner
            // whose copy of the root is still elsewhere must not slide across the ground at the settle speed)
            if (!_stationLocked && ((st._Arrived() && dist <= 2f) || dist <= StationArrive)) _stationLocked = true;

            if (!_stationLocked)
            {
                if (_dogOwner && dist > StationFar && Time.time >= _stationTeleportNext)
                {
                    _stationTeleportNext = Time.time + 1f;
                    TeleportTo(a - flat / dist * StationAppear);   // the leash Return's NavMesh-checked teleport
                }
                SleepStep(a, dist > runDistance ? runSpeed : walkSpeed);
                // Coming because called: looks at the caller on the way (the sleep call's manners)
                VRCPlayerApi caller = VRCPlayerApi.GetPlayerById(st._CallerId());
                if (petController != null && Utilities.IsValid(caller))
                {
                    petController._SetGaze(caller.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position, 0f);
                    petController._SetUrge(0.6f);
                }
                return true;
            }

            // On the stand: the placed-sleep freeze, the Owner holds the root on the anchor at the stand's height
            _speed = 0f; _moveDir = Vector3.zero; _stopHoldUntil = 0f;
            ApplyMoveAnim();
            if (_dogOwner)
            {
                float dt = Dt();
                Quaternion want = Quaternion.Euler(0f, st._StandYaw(), 0f);
                dogRoot.SetPositionAndRotation(Vector3.MoveTowards(dogRoot.position, a, StationSettleSpeed * dt),
                    Quaternion.RotateTowards(dogRoot.rotation, want, StationTurnRate * dt)); // net: owner-only: _dogOwner、洗い場の台の上に留める
                if (!st._Arrived() && Horizontal(dogRoot.position, a) <= StationArrive) st._DogArrived(dogIndex);
            }
            // Washed on the stand: the usual wash frame (glances, reactions, the satisfied sit = a _wantSit request; the root y is
            // ours, so the sit happens on the stand)
            if (wash != null && (wash._WashHold() || WashSatisfyRunning()))
            {
                WashFrame();
                WashLook(wash._WasherPosition());
            }
            return true;
        }

        /// <summary>Update (every client): the wash stop. True = this frame is handled (the caller returns).
        /// False on every path where nothing was touched, so the existing branches run exactly as before.</summary>
        bool UpdateHillsideWash()
        {
            // IMP-DOG-035: called to a wash station (walk there / stay on the stand, washed there as below). False when not held.
            _dbgWashExit = 0;   // 2026-10-02 round 5 read-out: 1 station, 2 riding down, 3 wash hold / satisfied sign
            if (UpdateWashStation(false)) { _dbgWashExit = 1; return true; }
            // 2026-10-02: released from a raised stand: ride it down (same rank as the stand itself), then the walk out further down
            if (UpdateLeaveLowering()) { _dbgWashExit = 2; return true; }
            if (wash == null || fetchController == null) return false;
            // IMP-DOG-028 (Codex 4-1): a satisfied sign in progress keeps the branch after the hold ran out (its own end time)
            if (!wash._WashHold() && !WashSatisfyRunning()) return false;
            // Water is stronger than washing (design §1-5): in the water (synced pose, every client) or in the Owner's water play
            // (walking out / shaking on the bank; non-Owners always see WaterOff) the dog is not stopped. It stops once landed.
            // Nothing below is touched on that path, so WaterTick's veto / preempt checks and UpdateWaterPlay (further down in
            // Update, which keeps _waterSeenFrame fresh) run exactly as they do for an unwashed dog: the walk-out is never blocked.
            if (WashYieldsToWater()) return false;
            // Gathering (a participant), the call to sleep beside someone (呼び戻し) and the travel ticket are stronger than
            // washing (design §1-5). All synced (every client agrees); the wash stop simply yields and their own branches run
            // below. Their accept edge also released the hold (WashErrandTick), so the old strokes do not stop the dog again when
            // they end; this check keeps new strokes during the errand from stopping it. TravelPending only matches a leashed
            // dog, so here it is a guard for a ticket that outlives its leash by a frame.
            if (WashYieldsToErrand()) return false;

            WashFrame();
            // Handled every frame while washed; ended -> the next frame reselects from the top (design §1-5).
            _behaviorSelectionValid = false;
            // _paused is left as the proximity update above set it (the washer is usually within pauseRadius, and the play
            // partner reading IsPausedForPlayer should see this dog as unavailable anyway)

            Coast();   // brake to a stop (Owner writes, everyone picks the legs from _speed)

            Vector3 washer = wash._WasherPosition();
            // 2026-09-30: no body turn while washed (the side being washed must not turn away)
            WashLook(washer);
            _dbgWashExit = 3;
            return true;
        }

        bool WashYieldsToWater()
        {
            return fetchController.WaterPose != 0 || waterState != WaterOff;
        }

        private bool _washErrandPrev;   // last frame's WashYieldsToErrand (the accept edge)

        /// <summary>Codex 4-1: Update top (every client, every frame, before the early returns). On the accept edge of a gathering
        /// (participant), a sleep call or a travel ticket, the wash hold is released once: KH_DogWash._ReleaseHold expires the hold
        /// (Owner: synced; non-Owner: the local boundary) and strokes older than that point never stop the dog or close the gates
        /// again. Without it the errand only bypassed the stop, and ending within holdSeconds re-stopped the dog on the old
        /// stroke with Fetch / disc / leash still refused. wash == null: returns at once (unwired dogs untouched).</summary>
        void WashErrandTick()
        {
            // IMP-DOG-035: the station's release edge, and on the errands' accept edge the station lets the dog go (_ReleaseDog:
            // only the station Owner's call acts; every client calls it on the same synced edge)
            bool hasCalls = washCalls != null && washCalls.Length > 0;
            if (hasCalls) { StationTick(); LeaveGuardTick(); }
            if (wash == null && !hasCalls) return;
            bool now = WashYieldsToErrand();
            if (now && !_washErrandPrev)
            {
                if (wash != null && wash._WashHold()) wash._ReleaseHold();
                if (_stationIdx >= 0 && washCalls[_stationIdx] != null) washCalls[_stationIdx]._ReleaseDog(dogIndex);
                _heldStationMemoFrame = -1;   // IMP-DOG-034 C1-1a: the station Owner's _ReleaseDog clears heldDog right here
            }
            _washErrandPrev = now;
            if (wash != null) WashCleanTick();
        }

        /// <summary>IMP-DOG-028 (every client, every frame, from WashErrandTick): the clean edge from the synced cleanSerial.
        /// Also the synced shake counter. An increase counts only when KH_DogWash already held a state in the previous frame
        /// (_HasState): a joiner's first receive is the new baseline, not "just got clean". The edges are only recorded here;
        /// the wash branch consumes them (WashReact) within 1 s, so an unwashed dog never reacts.</summary>
        void WashCleanTick()
        {
            // KH_DogWash._HasState(): this client holds a state (seeded as Owner, or received). A change seen on the frame the
            // state arrives (a joiner's first receive) is the baseline; only changes after that are edges. Same rule for both counters.
            bool had = _washHeldPrev;   // reused as "had a state last frame"
            bool has = wash._HasState();
            int serial = wash._CleanSerial();
            int shakes = wash._ShakeSerial();
            if (!_washCleanInit) { _washCleanInit = true; _washCleanSeen = serial; _washShakeSeen = shakes; }
            else
            {
                if (serial != _washCleanSeen)
                {
                    if (had && serial - _washCleanSeen > 0) { _washCleanPending = true; _washCleanPendingAt = Time.time; }
                    _washCleanSeen = serial;
                }
                if (shakes != _washShakeSeen)
                {
                    if (had && shakes - _washShakeSeen > 0) { _washShakePending = true; _washShakePendingAt = Time.time; }
                    _washShakeSeen = shakes;
                }
            }
            if (_washCleanPending && Time.time - _washCleanPendingAt > WashCleanPendingSeconds) _washCleanPending = false;
            // Codex 4-1: a satisfied sign ends early only when a frame went by without the wash branch (water, gathering, the
            // sleep call, the travel ticket, Fetch / disc, or the leash holder walking off took the dog). It never resumes.
            if (WashSatisfyRunning() && _washLastFrame < Time.frameCount - 1) { _washSatisfyUntil = -1000f; _washSatisfySitAt = -1000f; }
            if (_washShakePending && Time.time - _washShakePendingAt > WashCleanPendingSeconds) _washShakePending = false;
            _washHeldPrev = has;
        }

        /// <summary>IMP-DOG-028: a satisfied sign has started and not reached its own end (_washSatisfyUntil).</summary>
        bool WashSatisfyRunning()
        {
            return Time.time >= _washSatisfyStart && Time.time < _washSatisfyUntil;
        }

        /// <summary>IMP-DOG-028: the reactions (every client, every washed frame, from WashLook = both branches). Order:
        /// satisfied (clean edge) > shake > paw > the small stroke reactions. None of them stops the washing (no hold, no gate):
        /// the dog sits or lifts a paw in place and the part colliders stay on the bones.
        /// - Satisfied (every client, synced cleanSerial): 1.2 s (the Owner's shake via shakeSerial if the foam is rinsed), then
        ///   sit 2.5 s (_wantSit, the stopover's arbitration: LateUpdate writes Sit), tail wag and smile over the whole window, one
        ///   short voice at the sit. Once started it runs to its own end even after the hold (UpdateHillsideWash keeps the branch);
        ///   only water, the errands and Fetch / disc (any frame without the wash branch) end it (WashCleanTick).
        /// - Wet shake: 0.8 s after the washer's last stroke, wet >= washShakeWet (0.03) and foam <= WashShakeFoamMax, washShakeWetSpacing (20 s) apart.
        ///   The Owner decides (its own cells) and calls wash._RequestShake() (synced shakeSerial); every client starts the shake
        ///   from that counter's edge (WashCleanTick, the same first-receive rule as cleanSerial).
        /// - Paw: the stroke moves onto a front leg (part 3 / 4) -> WashPawPose for 1.5 s, washPawSpacing (20 s) apart (every client: strokes are
        ///   seen everywhere), only when stopped. LateUpdate's ApplyCompanionAnimation turns it into CallReady + CallPose 3 at the
        ///   lowest priority (not while Sit / LieDown / group / the sleep call stand, nor while the legs move).
        /// - Small: while stroked, a thin tail wag (_SetUrge) and narrowed eyes (_SetFace); the head tilt stays in WashLook.</summary>
        void WashReact()
        {
            float now = Time.time;
            int washer = wash._WasherPlayerId();
            float age = washer >= 0 ? wash._StrokeAgeOf(washer) : 1e9f;
            bool stroking = age < WashStrokeRecent;

            bool shaking = petController != null && petController.IsShaking;

            // ---- satisfied (the clean edge recorded by WashCleanTick this frame or within the last second) ----
            // Every client runs the same timeline from the synced edge: [start, +1.2 s) the shake window, then the sit 2.5 s.
            // The sit time does not depend on whether a shake plays (Codex 4-2: the same result on every screen). The shake itself
            // is the Owner's decision (its foam), distributed through shakeSerial like the wet one; the spacing applies to the
            // shake only, the satisfied sign always plays (Codex 4-3).
            // 2026-09-30 user decision: the sit (and its shake) only on every washSitEvery-th edge, chosen from the synced
            // cleanSerial itself (serial % washSitEvery == 0), so every client agrees. The other edges: tail + smile + voice only.
            if (_washCleanPending)
            {
                _washCleanPending = false;
                int every = washSitEvery > 0 ? washSitEvery : DefaultWashSitEvery;
                _washSatisfySits = _washCleanSeen % every == 0;
                if (_washSatisfySits && _dogOwner && now >= _washShakeCleanNextAt && !shaking && wash._AverageFoam() <= WashShakeFoamMax)
                {
                    _washShakeCleanNextAt = now + WashShakeCleanSpacing;
                    wash._RequestShake();
                }
                _washSatisfyStart = now;
                _washSatisfySitAt = _washSatisfySits ? now + WashShakeSeconds : now;
                _washSatisfyUntil = _washSatisfySits ? _washSatisfySitAt + WashSitSeconds : now + WashHappySeconds;
                _washVoiceDone = false;
                _washPawUntil = -1000f;   // the satisfied sign wins over a paw in progress (one pose Bool at a time)
                _washSatisfiedCount++;
            }
            bool satisfied = now >= _washSatisfyStart && now < _washSatisfyUntil;
            bool voiceDue = satisfied && now >= _washSatisfySitAt;
            bool sitting = voiceDue && _washSatisfySits;

            // ---- wet shake (stroke end + 0.8 s) ----
            if (age < WashShakeDelay) _washShakeArmed = true;
            // A paw lift or a shake still running only postpones the chance (it stays armed until they end): a shower that ends on
            // a front leg starts a paw lift there, which used to swallow the shake (harness run 20260930-062530).
            else if (_washShakeArmed && !shaking && now >= _washPawUntil)
            {
                _washShakeArmed = false;
                if (_dogOwner && !satisfied && now >= _washShakeWetNextAt
                    && wash._AverageFoam() <= WashShakeFoamMax && wash._AverageWet() >= (washShakeWet > 0f ? washShakeWet : DefaultWashShakeWet))
                {
                    // Owner decides (its cells), KH_DogWash syncs the counter; every client (the Owner too, next tick) starts
                    // the shake from that counter's edge below
                    _washShakeWetNextAt = now + (washShakeWetSpacing > 0f ? washShakeWetSpacing : DefaultWashShakeWetSpacing);
                    wash._RequestShake();
                }
            }
            if (_washShakePending)
            {
                _washShakePending = false;
                // In the satisfied window only before the sit (a late receive does not shake a sitting dog); kind 2 = the clean shake
                if (!shaking && !sitting) { StartWashShake(satisfied ? 2 : 1); shaking = true; }
            }

            // ---- paw lift (a front leg starts being washed) ----
            int part = wash._LastStrokePart();
            if (part != _washPawSeenPart)
            {
                // Codex 4-4: only once the legs have stopped (_wasMoving = the Walk / Run written, incl. ApplyMoveAnim's stop hold)
                if ((part == 3 || part == 4) && stroking && !satisfied && !shaking && !_wasMoving && now >= _washPawNextAt)
                {
                    _washPawUntil = now + WashPawSeconds;
                    _washPawNextAt = now + (washPawSpacing > 0f ? washPawSpacing : DefaultWashPawSpacing);
                    _washPawCount++;
                }
                _washPawSeenPart = part;
            }

            // ---- pose: sit through the existing request (stopped only; ApplyMoveAnim folds it if the legs still move) ----
            if (sitting && _speed <= stopSpeedThreshold) _wantSit = true;
            if (voiceDue && !_washVoiceDone) { _washVoiceDone = true; WashVoice(); }

            // ---- tail and face (per frame, the pet controller's own writers) ----
            if (petController != null)
            {
                if (satisfied) { petController._SetUrge(1f); petController._SetFace(0f, WashHappyEgao); }
                else if (stroking) { petController._SetUrge(WashStrokeWag); petController._SetFace(WashStrokeMetozi, 0f); }
            }

            WashShakeFx(now);
        }

        /// <summary>Start a shake (kind 1 = wet, 2 = clean): the procedural roll in the pet controller, the sound through the
        /// dog's voice source, and the splash window.</summary>
        void StartWashShake(int kind)
        {
            float now = Time.time;
            if (petController != null) petController._StartShake(WashShakeSeconds);
            _washShakeUntil = now + WashShakeSeconds;
            _washSplashNextAt = now;
            _washShakeCount++;
            _washShakeKind = kind;
            if (barkSource != null && washShakeClip != null) { barkSource.pitch = 1f; barkSource.PlayOneShot(washShakeClip); }
        }

        /// <summary>The satisfied voice: washHappyClip, or the dog's bark a little higher and quieter. Through barkSource (the
        /// personal-volume ledger's dog voice).</summary>
        void WashVoice()
        {
            if (barkSource == null) return;
            if (washHappyClip != null) { barkSource.pitch = 1f; barkSource.PlayOneShot(washHappyClip); }
            else if (barkClip != null) { barkSource.pitch = 1.15f; barkSource.PlayOneShot(barkClip, WashHappyVolume); }
        }

        /// <summary>Splash while shaking: KH_DogWash's hitSplash (World space, Emit only), moved to both flanks of the body
        /// every 0.1 s (the water play's Emit pattern: move, then Emit). Only near the local player.</summary>
        void WashShakeFx(float now)
        {
            if (now >= _washShakeUntil || now < _washSplashNextAt) return;
            _washSplashNextAt = now + 0.1f;
            ParticleSystem ps = wash.hitSplash;
            if (ps == null) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || (lp.GetPosition() - dogRoot.position).sqrMagnitude > WashFxRange * WashFxRange) return;
            Vector3 head = petController != null && petController.headAnchor != null ? petController.headAnchor.position : dogRoot.position + dogRoot.up * 0.4f;
            Vector3 body = Vector3.Lerp(dogRoot.position, head, 0.5f);
            float side = Vector3.Distance(dogRoot.position, head) * 0.25f;
            int n = now < _washShakeUntil - WashShakeSeconds * 0.5f ? 5 : 2;   // most of it in the first half
            ps.transform.position = body + dogRoot.right * side; ps.Emit(n);
            ps.transform.position = body - dogRoot.right * side; ps.Emit(n);
        }

        bool WashYieldsToErrand()
        {
            if (activities != null && activities.Mode == 1 && activities._IsParticipant(dogIndex)) return true;
            if (sleepBoard != null && dogIndex >= 0 && sleepBoard.LiveSleeperOf(dogIndex) >= 0) return true;
            if (TravelPending()) return true;
            return false;
        }

        /// <summary>UpdateHillsideLeash (every client), before LeashPose: does the leashed dog stop for the wash this frame?
        /// Holder's own stroke within the hold, dry, no travel ticket, the leash slack, and (Owner) the holder standing.
        /// Non-Owners have no pace history (TrackHolderSpeed is Owner only), so for them the answer only picks the pose, and
        /// LeashPose applies it only while the observed dog is stopped - i.e. once the Owner has stopped it.</summary>
        bool WashLeashStop(VRCPlayerApi holder, float handDist, float leash)
        {
            // The wider band only for a stop that ran last frame (a decision left over from before a Fetch / disc hand-over,
            // which returns above this call, must not widen it)
            bool was = _washLeashStop && _washLastFrame == Time.frameCount - 1;
            _washLeashStop = false;
            bool satisfy = WashSatisfyRunning();   // IMP-DOG-028: runs to its end after the hold / the holder's last stroke
            if (wash == null || fetchController == null || (!satisfy && !wash._WashHold())) return false;
            if (WashYieldsToWater() || TravelPending()) return false;
            // Owner: the Return branch owns a stretched leash (handDist > 0.6x is still "returning" there)
            if (_dogOwner && leashMode == LeashReturn) return false;
            // Codex 4-3: the slack (and its enter / hold hysteresis) is the Owner's alone. A non-Owner sees the hand and the dog at
            // its own moment and cannot know which band the Owner is in, so it would refuse a stop the Owner keeps. Instead the
            // non-Owner poses from the synced hold + the holder's stroke + the observed stop (LeashPose's `stopped`): the dog only
            // stands still there because the Owner stopped it. Chosen over carrying the Owner's decision in a synced value (none of
            // the dog's synced objects is ours to extend here, and KH_DogWash does not sync it); the cost is that a dog stopped for
            // another reason (waiting at the leash end) while the holder washes it is shown with the wash gaze - the same washer.
            if (_dogOwner && handDist > leash * (was ? WashLeashSlackHold : WashLeashSlackEnter)) return false;
            // Someone else's stroke changes the mask and the values only (design §1-5: 他人のストロークでは足を止めない).
            // The holder's own last stroke (KH_DogWash._StrokeAgeOf, per player; 1e9 = none) within holdSeconds. A second washer
            // stroking after the holder does not cut a stop the holder's own stroke started, and never starts one.
            if (!satisfy && wash._StrokeAgeOf(holder.playerId) >= wash.holdSeconds) return false;
            // The holder walking off ends the stop at once (TrackHolderSpeed's tier, the same one the walk paces from)
            if (_dogOwner && HolderMoving()) return false;
            _washLeashStop = true;
            return true;
        }

        /// <summary>LeashPose (every client) when the wash stop holds: no pose Bool (washed standing), the head toward the washer.</summary>
        void WashLeashPose()
        {
            WashFrame();
            WashLook(wash._WasherPosition());
        }

        /// <summary>UpdateHillsideLeash, Owner only, after the Return block and before rest: stand still for the holder's wash.
        /// LeashWait (the dog standing on its leash) makes a detour / stopover in progress end as "interrupted" at the top of the
        /// next frame (EndPause, the same path rest and the return take). The next unwashed frame walks from step 7 as usual.</summary>
        void WashLeashHold()
        {
            leashMode = LeashWait;
            WashFrame();
            Coast();   // 2026-09-30: no body turn (the same as unleashed)
        }

        /// <summary>Every wash frame (both branches, every client): the frame flag and the entry edge. Twice in one frame is one.</summary>
        void WashFrame()
        {
            if (_washLastFrame == Time.frameCount) return;
            if (_washLastFrame != Time.frameCount - 1) WashEnter();
            _washLastFrame = Time.frameCount;
            _washLastAt = Time.time;   // 2026-10-02 round 2: washing keeps an unleash grace going (HillsideStationLeave.cs)
            _washActive = true;
        }

        /// <summary>Head (every client, local): now and then a glance at the washer (washGazeSeconds every washGazeIntervalMin..Max s,
        /// 2026-09-30 user decision); outside a glance nothing is injected and the head follows the clip. The tilt starts when the
        /// stroke moves to another part, only inside a glance with time left for it, at most one start every 4 s, alternating sides.</summary>
        void WashLook(Vector3 washer)
        {
            Vector3 dogPos = dogRoot.position;
            Vector3 flat = washer - dogPos;
            flat.y = 0f;
            bool hasWasher = flat.sqrMagnitude > 0.01f;
            float now = Time.time;

            if (now >= _washGazeNextAt && now >= _washGazeUntil)
            {
                _washGazeUntil = now + (washGazeSeconds > 0f ? washGazeSeconds : DefaultWashGazeSeconds);
                _washGazeNextAt = _washGazeUntil + WashGazeInterval();
                _washGazeCount++;
            }
            bool glance = now < _washGazeUntil;

            int part = wash._LastStrokePart();
            if (part >= 0 && part != _washSeenPart)
            {
                if (glance && _washSeenPart >= 0 && now >= _washTiltNextAt && _washGazeUntil - now >= WashTiltSeconds)
                {
                    _washTiltStart = now;
                    _washTiltNextAt = now + WashTiltSpacing;
                    _washTiltSign = -_washTiltSign;
                    _washTiltCount++;
                }
                _washSeenPart = part;
            }

            if (petController != null && glance)
            {
                float roll = 0f;
                float t = Time.time - _washTiltStart;
                if (t >= 0f && t < WashTiltSeconds) roll = _washTiltSign * WashTiltDegrees * Mathf.Sin(Mathf.PI * t / WashTiltSeconds);
                Vector3 gaze = hasWasher ? washer : dogPos + dogRoot.forward * 1.5f;
                gaze.y += WashGazeRise;
                petController._SetGaze(gaze, roll);   // valid for this frame only: passed every frame of the glance
            }
            WashReact();   // IMP-DOG-028: both branches reach here (unleashed after Coast, leashed through WashLeashPose)
        }

        /// <summary>Seconds to the next glance: uniform in washGazeIntervalMin..Max (0 or less -> the defaults; max below min -> min).</summary>
        float WashGazeInterval()
        {
            float lo = washGazeIntervalMin > 0f ? washGazeIntervalMin : DefaultWashGazeIntervalMin;
            float hi = washGazeIntervalMax > 0f ? washGazeIntervalMax : DefaultWashGazeIntervalMax;
            if (hi < lo) hi = lo;
            return Random.Range(lo, hi);
        }

        /// <summary>The first washed frame: fold what must not resume after the wash (same set as the Fetch branch, minus
        /// what a dog that stays in place can keep: the sleep / live freezes stay, the Animator is not handed to anyone).
        /// IMP-DOG-027: the leashed entry calls this too. A leashed dog's own rest is re-derived every frame (settling, LeashRest)
        /// and resumes only if the holder is still standing after the wash - that is the normal rest, not a stale one. The
        /// unleashed rest FSM and a claimed treat are frozen while leashed (Update returns at the leash line); folding them here
        /// as well keeps one entry for both branches and means neither replays an old performance when the leash comes off.</summary>
        void WashEnter()
        {
            _holderId = -1;            // feint watch
            _treatEating = false;      // a treat still claimed resumes from its synced state after the wash
            _thanksUntil = -1f;        // the thank-you look would point at someone else after the wash
            if (_restPhase != RestPhaseNone) EndRest();   // EndRest: cooldown and debt settled as on every exit
            // 2026-09-30: the first glance comes after one interval, not at once (the dog does not turn to look when touched)
            _washGazeUntil = -1000f;
            _washGazeNextAt = Time.time + WashGazeInterval();
            // The part at entry is the reference; the first tilt comes when the stroke moves on
            _washSeenPart = wash._LastStrokePart();
            // IMP-DOG-028: a stroke end from before this wash does not shake; the first stroke may start a paw lift
            _washShakeArmed = false;
            _washPawSeenPart = -1;
        }
    }
}
