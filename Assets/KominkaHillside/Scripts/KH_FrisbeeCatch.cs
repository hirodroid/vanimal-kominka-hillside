using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    // IMP-DOG-005 prototype: one dog chases a thrown disc, jumps so that its mouth (in the catch pose)
    // meets the predicted disc position at the predicted time, lands, and carries the disc back.
    //
    // Authority follows KD_DogFetchController: the Owner of dogRoot decides and writes the root
    // transform (VRCObjectSync distributes it); every client derives display (Animator, disc at the
    // mouth) from the synced state. The dog never calls SetOwner on itself; it does claim the disc
    // when it takes it (same as the ball).
    //
    // Prediction and flight share KH_Frisbee._PositionAt. The controller owns timing and position
    // (crouch start, liftoff, catch time, landing point); the clip owns the pose. After liftoff the
    // arc is fixed: no steering in the air, and an interrupted jump still lands before anything else.
    // At the catch instant the disc is pulled to the mouth if it is within catchTolerance (that pull
    // is the only absorbed error); beyond that it is a miss.
    //
    // Owner hand-over mid-sequence is not continued: a new Owner without the local plan aborts to Idle
    // (review D1). Continuing a jump on another client would need the arc synced; not in this prototype.
    //
    // IMP-DOG-008 round 2 (PCVR feedback 2026-09-21): intercept-first. Every jump from a run missed by 0.5-0.9 m,
    // because the pacing brought the dog to the crouch start early, it coasted to a stop and then jumped from a
    // standstill along a plan that assumed it was still moving. The dog now reads the whole trajectory at the throw,
    // takes the HIGHEST point it can still be standing under in time, sprints there at full speed, fine-tunes its
    // position while it waits and jumps straight up. There is no running jump any more: the takeoff is always from
    // a standstill (_jumpSpeed == 0), so the only thing the plan must get right is where to stand and when to crouch.
    //
    // IMP-DOG-008 round 4 (PCVR feedback 3, 2026-09-21): round 2/3 caught the disc but read as a machine - the dog
    // teleport-sprinted under the point, spun on the spot and popped straight up. Round 4 keeps "get under it early
    // and wait" and rebuilds the TRANSITIONS: the approach is a HOOK (run to a point 2.2 m short of the spot, swung
    // 45 deg onto the side the dog is already on, then curve in along the facing the catch needs), so the dog arrives
    // already pointing the right way and never turns in place at speed; the turn rates drop to dog rates (240 running,
    // 180 walking, 120 standing in a catch state) and the 720 deg/s snap is gone; and a candidate the hook cannot
    // reach is taken as a RUNNING jump whose takeoff speed is the speed the dog actually has, so it never stops
    // before the crouch. Jumps are preferred over stand catches wherever one is feasible.
    //
    // IMP-DOG-008 invariant: the prototype `variant` only affects the Ready target, the clip selection, the flight
    // curve and - for v3 alone - whether the overtake scan runs at all. The intercept planner,
    // the jump and the landing are identical across presets. GiveUp, the
    // ground retrieve, CanPlay, Claimed, the yield-after-landing reservation, playArea (DogMayGo) and ObserveFlight
    // never read it. UpdateLand still clamps the recover slide at the edge of the flat for every preset (Codex D).
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KH_FrisbeeCatch : UdonSharpBehaviour
    {
        public Transform dogRoot;
        public Animator dogAnimator;
        public Transform mouthAnchor;
        public KH_Frisbee frisbee;

        [Header("World-scene neighbours (null in the prototype scene: no exclusion, no play area)")]
        public KH_LeashManager hillsideLeashes;
        public KominkaDogRun.KD_DogFetchController fetch;
        public KominkaDogRun.KD_DogWanderController wander;
        public KominkaDogRun.KD_GroupActivities activities;
        public KominkaDogRun.KD_DogPetController pet;
        public int dogIndex = -1;
        [Tooltip("Flat play area (the plaza). 0 radius = unlimited. Ready and throws are refused outside it")]
        public Vector3 playAreaCenter;
        public float playAreaRadius;
        public float playAreaMargin = 2f;

        [Header("Pose facts (written by Tools/KominkaHillside/Frisbee/4 Build Scene from the clip JSON)")]
        [Tooltip("Mouth anchor relative to dogRoot (dogRoot frame, world units) at the clip's catch frame")]
        public Vector3 mouthOffsetCatch;
        [Tooltip("Mouth anchor relative to dogRoot at the idle pose")]
        public Vector3 mouthOffsetStand;
        public float clipCrouchDuration = 0.25f;   // clip start -> liftoff
        public float clipRiseDuration = 0.35f;     // liftoff -> catch frame (== catch -> touchdown; the arc is symmetric)
        public float clipRecoverDuration = 0.40f;  // touchdown -> clip end
        [Tooltip("Same facts for the twist clip (IMP-DOG-008 v4). The body is yawed 90 deg at the catch frame, so the mouth sits to the dog's right and a little behind the root")]
        public Vector3 mouthOffsetCatchTwist;
        public float clipCrouchDurationTwist = 0.25f;
        public float clipRiseDurationTwist = 0.35f;
        public float clipRecoverDurationTwist = 0.40f;

        [Header("Jump (Build Scene writes maxRootRise and the speeds below; the rest of the Inspector values are kept)")]
        [Tooltip("Root rise range. The clip plays at clipRise/tUp, so the range also bounds the playback speed. " +
            "Code-owned since round 2: the standing jump is the only jump, so this is how high the dog can reach")]
        public float maxRootRise = 0.9f;
        public float minRootRise = 0.19f;
        [Tooltip("Disc lower than the standing mouth by more than this: pick it up after it lands")]
        public float tooLowBelowMouth = 0.30f;
        [Tooltip("Mouth to the disc EDGE (centre distance minus the disc radius) at the catch instant")]
        public float catchTolerance = 0.30f;
        [Tooltip("Seconds around the planned catch instant during which the mouth may meet the disc (first frame within tolerance wins)")]
        public float catchWindow = 0.08f;
        [Tooltip("Run used to get under the catch point. Round 2 sprinted at 10 m/s with a 25 m/s2 start, which read " +
            "as a teleport (PCVR 3). Round 4: 8 m/s is still faster than the walking world and the hook approach buys " +
            "back the time the gentler acceleration costs. The run clip is sped up in proportion above runSpeed")]
        public float chaseRunSpeed = 8f;
        [Tooltip("Acceleration while chasing. Round 4: 12 m/s2 takes 0.67 s to reach the run - visible, not instant")]
        public float chaseAcceleration = 12f;
        [Tooltip("Braking while chasing (round 4). The generic deceleration 36 stops the dog dead; the chase eases " +
            "into the catch spot and into the crouch with this one instead")]
        public float chaseDeceleration = 10f;
        public float maxRunAnimScale = 2.5f;   // round 2b: 3.5x read as jitter at the sprint
        [Tooltip("Seconds the dog wants to be standing on the spot, settled, before the crouch begins. " +
            "Part of the feasibility test, so a candidate it can only just reach is refused")]
        public float settleTime = 0.2f;
        [Tooltip("Speed used to bring the disc back (retrieve from the ground, return to the thrower). " +
            "Not the sprint: the chase is the only part that has a deadline")]
        public float returnRunSpeed = 4.5f;

        [Header("Stand-off while the disc is held (user idea 2026-09-21)")]
        [Tooltip("While someone holds the disc the dog runs this far in front of them and waits facing them. " +
            "Off = stay put and chase from wherever it is (dash-and-jump mode)")]
        public bool standoffWhenHeld = true;
        public float standoffDistance = 8f;
        [Tooltip("The spot moves with the holder; the dog only re-positions once it is off by more than this")]
        public float standoffSlack = 1.5f;
        public float replanInterval = 0.1f;
        [Tooltip("Round 2: the catch point is decided at the throw, so the dog must not spend a quarter second admiring it")]
        public float noticeDelay = 0.1f;

        [Header("Locomotion (KD defaults)")]
        public float runSpeed = 2.4f;
        public float walkSpeed = 1.1f;
        public float acceleration = 6f;
        public float deceleration = 36f;
        [Tooltip("Turn rate while running (round 4). Round 2's 720 deg/s was a way to make the serial turn term in Eta " +
            "affordable; on screen it was a spin in place. The hook approach delivers the facing instead, so the rate " +
            "can come back down to something a dog does")]
        public float turnSpeedDegPerSec = 240f;
        [Tooltip("Turn rate at walking pace (the fine-tune shuffle under the catch point)")]
        public float walkTurnDegPerSec = 180f;
        [Tooltip("Turn rate standing still WHILE CHASING / JUMPING (round 4). Idle keeps the slow wander rate; a dog " +
            "waiting under a disc may swing its head round at a dog's pace, not at a turret's")]
        public float standTurnDegPerSec = 120f;
        public float idleTurnSpeedDegPerSec = 30f;
        public float runAnimSpeed = 1.7f;
        public float stopSpeedThreshold = 0.05f;
        public LayerMask groundLayerMask = (1 << 0) | (1 << 11);
        public float groundRayOffset = 0.5f;
        public float groundRayDistance = 4f;

        [Header("Retrieve / return (KD-like)")]
        public float arriveRadius = 0.35f;
        public float pickupDuration = 1.0f;
        public float returnStopDistance = 0.9f;
        public float returnRunDistance = 2.5f;
        public float dropWaitDuration = 3f;
        public float chaseTimeout = 14f;
        public float returnTimeout = 20f;
        public float retrieveMaxDistance = 25f;
        [Header("Carry pose")]
        public float carryForward = 0.08f;
        public float carryUp = -0.01f;
        public float carryTiltDeg = 8f;

        [Header("Prototype variant (IMP-DOG-008; Build writes Normal, the runtime switch cycles it)")]
        [Tooltip("Preset number. Written only by the dog Owner in SetVariant; every client derives its working values from it")]
        [UdonSynced] public int variant = VariantNormal;
        [Tooltip("Preset queued while the dog is engaged, applied on the way back to Idle. -1 = none. Owner-written, synced so the menu can show and cancel it")]
        [UdonSynced] public int pendingVariant = -1;

        public const int StateIdle = 0, StateChase = 1, StateJump = 2, StateJumpCarry = 3, StateLand = 4,
            StateLandCarry = 5, StateRetrieve = 6, StatePickup = 7, StateReturn = 8, StateDrop = 9, StateReady = 10;
        const int PlanNone = 0, PlanJump = 1, PlanGround = 2, PlanRun = 3;

        // ---- IMP-DOG-008 presets (numbers from docs/plans/2026-09-21-frisbee-variations.md; public so Verify reads them) ----
        public const int VariantNormal = 0, VariantRunInSide = 1, VariantRunInFar = 2, VariantRunInBeside = 3,
            VariantTwist = 4, VariantCurve = 5;
        public const int VariantCount = 6;
        // Run-in (v1..3). Round 2: the planner no longer differs per preset (every catch is an intercept), so all
        // that is left of the run-in is WHERE the dog waits for the throw - the run in from there is the effect.
        public const float RunInReadySlack = 2.5f;
        // Hold band (round 2b). The sprint stops at a 0.1 m arrive radius, so a hold phase that needed < 0.1 m was
        // never entered: 15 of 32 PCVR takeoffs read `settled=-1.00 off=0.10` (no about-face, no fine-tune).
        // Enter the hold well outside the arrive radius, and leave it only on a real displacement (hysteresis,
        // otherwise the dog flickers between sprint and walk on the band edge and reads as jitter).
        public const float HoldEnterRadius = 0.35f, HoldExitRadius = 0.6f;
        // Overtake (v3 並走): the disc is caught on the run from behind, so the dog must be closing on it from
        // behind (not crossing or meeting it) and must still be RUNNING when the mouth gets there.
        // The brief's "arrive just in time" was a slack ceiling of 0.15 s. At a 10 m/s sprint that ceiling can
        // never be met - measured on the design throw the dog reaches the point with 0.66-0.71 s to spare - so it
        // would have killed the preset outright. The same intent is expressed as a PACE floor instead: the run is
        // paced to arrive exactly at the catch, and a candidate is refused unless that pace is a real chase-down
        // (3.5 m/s = half again the walking-world run). This also keeps a lob, which only ever paces out to a
        // 2.5 m/s trot, with the intercept jump where it belongs.
        public const float OvertakeMinPace = 3.5f, OvertakeMinDot = 0.5f;
        // ---- Round 4: the hook approach (PCVR 3: "it slides underneath and snaps round - a machine") ----
        // The dog must reach the take-off point already facing F (into the oncoming disc) AND still running,
        // because a facing it still owes has to be paid as a turn in place, and a jump from a standstill is the
        // pop-up the user called a machine. So the run is aimed at A = C - F * len, swung about C toward the side
        // the dog is already on (C = the crouch start, F * v * (crouch + rise) short of the catch spot): the dog
        // comes round on its own side, curves onto the line, and the last strides ARE the run-up to the jump.
        //
        // Spare time is spent by making the LOOP BIGGER, not by standing still (user, round 4 amendment: "when it
        // actually jumps it should not be slowing down - top speed, surely?"). HookLenAt / HookAngAt walk six
        // loops from (2.2 m, 45 deg) to (4.5 m, 90 deg) and the planner takes the biggest one it can still run.
        // HookLength / HookAngleDeg are the loop ApproachHold uses (and the numbers Verify bounds); the momentum
        // grid runs from HookLengthMin / HookAngleMin up to the maxima, because a catch point 2 m away with a
        // second to spare needs a SMALL curve - the 2.2 m loop takes longer than the whole flight has left.
        public const float HookLength = 2.2f, HookLengthMin = 1f, HookLengthMax = 4.5f;
        public const float HookAngleDeg = 45f, HookAngleMin = 25f, HookAngleMax = 90f;
        public const int HookCombos = 8;
        // Within this of A the dog stops steering at A and starts steering at C: the corner is a blend, not a waypoint.
        public const float HookCornerRadius = 1.0f;
        // Runtime floor on the approach pace - a jog, never a stop. Below the second one there is so much spare time
        // that even the longest loop cannot use it up, and the plan becomes an honest wait (a high lob).
        public const float HookMinPace = 1.5f, HookWaitPace = 2.0f;
        // Taking the biggest loop that *exactly* fits leaves a plan with 0.1 s of slack, and the first replan that
        // disagrees by a stride drops it (measured: the lob committed at slack 0.09 and fell to the ground plan).
        // The loop is chosen to land this much inside the budget instead.
        public const float HookEtaMargin = 0.15f;
        // Over the last metres of a WAIT approach the heading rolls onto the catch facing even without a loop.
        public const float HoldBlendLength = 2.5f;
        // A jump is preferred to a stand catch wherever one is feasible; this is the rise that makes a jump a jump.
        public const float PreferJumpMinRise = 0.25f;
        // The take-off speed IS the speed the dog has at the crouch, clamped to a band a dog can push off from, and
        // it is re-read at every 10 Hz replan. Round 2 fixed it at 2.5, the approach disagreed, and the dog stopped.
        public const float RunJumpMinTakeoff = 2f, RunJumpMaxTakeoff = 6f;
        // A dog already standing inside the run-up distance does not reverse out to get a longer approach - it
        // pounces from where it is. Without this the crouch start is pushed BEHIND the dog (worst on the twist,
        // whose mouth sits 0.63 m off the running line, so the run-up has to come out of a 0.6 m gap) and the next
        // replan drops the plan: measured as v4 committing plan=1 and falling to plan=3 on every ClientSim throw.
        public const float RunJumpShortTakeoff = 1f;
        // The take-off ceiling is about the JUMP, not about getting there. Far from the crouch start the dog may
        // run at the full chase speed and trim down inside this distance (8 -> 3 m/s needs 2.75 m at
        // chaseDeceleration). Holding the whole approach at the take-off ceiling made a plan the dog fell a stride
        // behind unrecoverable, and the twist - which also has to sidestep 0.63 m - dropped to the ground plan.
        public const float RunJumpTrimDistance = 3f;
        // Round 4b: ANY direction, including straight at the disc (-1). A dog facing an incoming disc runs at it and
        // jumps, and refusing that left every flat / lob throw on the "get under it and wait" plan - the very shape
        // the user called mechanical. What a head-on meeting does not tolerate is speed: the closing rate is the sum
        // of both, so a fast run turns the catch window into centimetres. Head-on take-off is capped instead.
        public const float RunJumpMinDot = -1f;
        public const float RunJumpHeadOnMaxTakeoff = 3f;
        // A straight run-up (no loop) only delivers the facing if the dog is already coming from roughly the right
        // side; beyond this it would have to arrive backwards, so the plan waits instead.
        public const float StraightMaxOffAngle = 60f;
        // How the approach was made. Logged at the takeoff and at the catch. Everything but `wait` takes off with
        // momentum, so the harness fails those if the dog dropped below a jog in the last second.
        public const int ModeHook = 0, ModeRun = 1, ModeStand = 2, ModeWait = 3, ModeOvertake = 4;
        // Motion quality ring: the last second of (yaw rate, speed), sampled per frame while chasing.
        public const int MotionSamples = 128;
        public const float MotionWindow = 1f;
        // One sample per this many seconds (see SampleMotion): long enough that the angle and the clock agree.
        public const float MotionBucket = 0.08f;
        // The two numbers the harness judges: a turn faster than this is a snap, and outside the hook a dog that
        // dropped below this before taking off had stopped and popped.
        public const float MotionMaxYawRate = 300f, MotionMinSpeed = 1.5f;
        // Below this approach length the speed rule is not satisfiable by ANY dog: the window alone needs
        // MotionMinSpeed * MotionWindow = 1.5 m covered at speed, and the dog still has to accelerate into it from
        // a standstill and a turn. Shorter approaches are judged on the yaw rate only (the disc came down next to
        // the dog - there was nothing to run). Measured: 1.5-2.2 m approaches bottom out at 1.0 m/s on the ramp.
        public const float MotionMinPath = 2.5f;
        // Trajectory scan: 32 samples of 0.1 s from +0.2 s, walked forward so the FIRST hit is the highest point.
        public const int PlanSamples = 32;
        public const float PlanStep = 0.1f;
        // Ready geometry per run-in preset: ahead along the holder's head direction, lateral toward the side the dog is on.
        public const float ReadyAhead1 = 7f, ReadyLateral1 = 4f, ReadyAhead2 = 11f, ReadyLateral2 = 1.5f,
            ReadyAhead3 = 4f, ReadyLateral3 = 2.5f;
        // A run-in spot sits metres to the side, so a head turn toward the dog would swing it: the spot only moves
        // when the holder really turned away (or walked off, RunInReadySlack).
        public const float RunInReadyTurnDeg = 45f;
        // Sideways lift of v5 (1/m, same units as KH_Frisbee.lift; positive = the disc bends right). KH_Frisbee
        // reads it through FlightCurve at launch and hands it back with ReqCatch, so flight and prediction agree.
        public const float CurveDefault = 0.04f;

        [UdonSynced] private int state = StateIdle;
        [UdonSynced] private int throwerId = -1;
        [UdonSynced] private float animSpeed = 1f;

        // Launch facts (Owner).
        private bool _haveLaunch;
        private int _gen = -1;         // throw generation; end notices of other throws are ignored (review D5)
        private Vector3 _p0, _v0;
        private double _t0;
        private float _curve;          // the throw's own sideways lift, received with the launch facts (IMP-DOG-008 v5)
        private bool _flightEnded;
        private int _flightEndReason;
        // Plan (Owner).
        private int _plan;
        private float _catchT;         // flight time of the catch
        private Vector3 _catchRoot;    // where dogRoot must be at the catch (world; y = ground + rise, or ground for a stand catch)
        private Vector3 _standRoot;    // the spot on the ground under the catch: chase target, crouch start and takeoff
        private Vector3 _forward;      // where the dog looks at the catch (into the oncoming disc)
        private Vector3 _entryDir = Vector3.forward;  // the way it ran in to get there (log only: `entry=`)
        private float _rootRise, _tUp, _animScale, _crouchReal, _jumpSpeed;
        private float _planSlack;      // spare seconds of the committed candidate (log only)
        private float _planPath;       // length of the committed approach (log only: `path=`)
        private bool _runningCatch;    // v3: this PlanRun is taken on the run (never stop), not standing still
        private bool _holding;         // inside the hold band: turning to face the disc and fine-tuning the spot
        private bool _loggedRunning;   // the _runningCatch the last plan line was printed with
        private int _loggedMode = -1;  // and the mode (a hook and a running jump are both PlanJump)
        private float _faceErrAtTakeoff = -1f;  // facing error when the crouch began (log: `face=`)
        // Round 4 approach. _mode is how this plan gets there (ModeHook / ModeRun / ModeStand / ModeOvertake);
        // _hookPoint is A, the point short of the spot the first leg aims at, and _hookDone latches once the dog is
        // inside HookCornerRadius of it (the corner is never re-entered, so the dog cannot loop round the spot).
        private int _mode = ModeHook;
        private float _takeoffMin = RunJumpMinTakeoff, _takeoffCap = RunJumpMaxTakeoff;  // take-off speed band of THIS plan (head-on and short run-ups are slower)
        private Vector3 _hookPoint;
        private bool _hookActive, _hookDone;
        private float _hookLen = HookLength;
        // Scratch filled by Approach(): the point to run at, the mode it represents and what it costs.
        private Vector3 _apPoint; private int _apMode; private float _apEta, _apLen, _apPath; private bool _apHook;
        // Motion quality ring (Owner, Chase only) and the values latched at the takeoff.
        private float[] _mYaw, _mSpeed, _mTime;
        private int _mIdx, _mCount;
        private Vector3 _mPrevFwd; private bool _mPrimed, _motionLatched;
        private float _mAccAngle, _mAccTime, _mSlowest = -1f, _mPrevDt = 0.016f;
        private float _yawRateMax, _speedMin, _vTake;
        // Scratch filled by CatchGeometry; copied into the plan fields only once a candidate is accepted.
        private Vector3 _geoForward, _geoCatchRoot, _geoStandRoot;
        private Vector3 _landing;      // predicted disc landing point (PlanGround)
        private bool _landingValid;
        private float _nextPlan;
        private bool _localPlanValid;  // this client holds the plan/launch facts for the current sequence
        // Jump execution (Owner).
        private double _jumpStart;
        private Vector3 _jumpFrom;
        private float _groundY;
        private bool _catchDecided;
        private float _bestGap;
        private bool _airborne;
        private Vector3 _airStart, _airVel;
        private double _stateEntered;
        // Locomotion.
        private float _speed;
        private bool _dogOwner = true;
        private Vector3 _obsPrev; private bool _obsValid;
        private bool _wroteRun, _wroteWalk, _wroteJump, _wroteTwist, _wrotePickup; private float _wroteSpeed = 1f;
        private int _lastApplied = -1;
        private bool _released;
        private VRC_Pickup _discPickup;
        private float _deviationSince = -1f;
        private Vector3 _standoffSpot; private bool _standoffValid; private float _nextStandoff;
        private bool _yieldAfterLanding;   // a leash / gather arrived while airborne: land, then hand the dog over
        private bool _ownerSplitWarned;
        private bool _outputValid;         // our Run/Walk/speed writes are current (false after another controller wrote)
        // Variant working values (IMP-DOG-008). ApplyVariant expands the synced number into these; nothing else writes them.
        private int _appliedVariant = -1;  // which number the working values below were expanded from
        private float _readyAhead, _readyLateral, _readySlack;
        private bool _readyFaceForward;
        private float _readySide = 1f;     // which side of the holder's line the Ready spot sits on (latched at Ready entry)
        private Vector3 _standoffFwd = Vector3.forward;  // holder's head direction the current Ready spot was built from
        private Vector3 _standoffHolderPos;              // where the holder stood when that spot was built
        private bool _runIn, _twist;       // _runIn: Ready geometry only (round 2). _twist: Animator Bool + pose facts
        private bool _overtake;            // v3 only: try an on-the-run catch from behind before the intercept scan
        private Vector3 _mouthCatch;       // pose working values, copied from the Jump or the Twist facts set
        private float _crouchDur, _riseDur, _recoverDur;
        private float _settledSince = -1f; // Time.time the dog first stood on the catch spot, -1 = not there yet

        public int State { get { return state; } }
        public int Variant { get { return variant; } }
        public int PendingVariant { get { return pendingVariant; } }
        // Sideways lift for the next throw. Wave 2: KH_Frisbee._Launch reads it and sends it with ReqCatch, so a
        // flight already in the air keeps the curve it was launched with.
        public float FlightCurve { get { return variant == VariantCurve ? CurveDefault : 0f; } }
        public bool IsIdle { get { return state == StateIdle; } }
        public bool IsCarrying { get { return state == StateJumpCarry || state == StateLandCarry || state == StateReturn || state == StateDrop; } }
        public bool IsAirborne { get { return state == StateJump || state == StateJumpCarry || state == StateLand || state == StateLandCarry; } }
        // Busy for the other systems: Ready (standing off while someone holds the disc) is NOT engaged, so a
        // held disc never takes the dog away from Fetch, the leash or a gathering (review M1).
        public bool IsEngaged { get { return state != StateIdle && state != StateReady; } }
        public int DebugPlan { get { return _plan; } }

        void Start()
        {
            if (dogRoot == null || dogAnimator == null || mouthAnchor == null || frisbee == null)
            {
                Debug.LogError("[KH_FrisbeeCatch] wiring incomplete. Run Tools/KominkaHillside/Frisbee/4 Build Scene.");
                enabled = false; return;
            }
            if (mouthOffsetCatch.sqrMagnitude < 1e-6f || clipRiseDuration <= 0.01f || mouthOffsetCatchTwist.sqrMagnitude < 1e-6f || clipRiseDurationTwist <= 0.01f)
                Debug.LogWarning("[KH_FrisbeeCatch] pose facts are zero (Jump or Twist) - run Tools/KominkaHillside/Frisbee/2 Build Jump Clip then 4 Build Scene.");
            _mYaw = new float[MotionSamples]; _mSpeed = new float[MotionSamples]; _mTime = new float[MotionSamples];
            _discPickup = (VRC_Pickup)frisbee.GetComponent(typeof(VRC_Pickup));
            frisbee.catcher = this;
            _stateEntered = Networking.GetServerTimeInSeconds();
            if (IsDogOwner()) dogRoot.position = SnapToGround(dogRoot.position); // net: owner-only
            ApplyVariant();
            ApplyState();
        }

        bool IsDogOwner() { return Networking.IsOwner(dogRoot.gameObject); }

        // ---- Prototype variant (IMP-DOG-008) ----
        // Local entry: the hand menu's dev button and the prototype Interact cube call this. Anyone may ask; only
        // the Owner writes. net: owner-writes-synced
        public void _RequestVariant(int v)
        {
            if (v < 0 || v >= VariantCount) return;
            if (Networking.IsOwner(gameObject)) ReqVariant(v);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqVariant), v);
        }

        // net: network-callable; arguments are checked (anyone may send). Engaged = the dog is mid-sequence, so the
        // change waits for Idle instead of moving the goalposts under a running plan.
        [NetworkCallable(maxEventsPerSecond: 5)]
        public void ReqVariant(int v)
        {
            // Both Owners, because the decision is the dog's and the synced numbers live on this object
            // (an ownership split would write `variant` on a client nobody reads it from; Codex D review).
            if (!IsDogOwner() || !Networking.IsOwner(gameObject)) return;
            if (v < 0 || v >= VariantCount) return;
            if (v == variant)
            {
                // Asking for what is already running is the way back out of a queued switch (Codex review).
                if (pendingVariant >= 0) { Debug.Log("[KH_FrisbeeCatch] variant " + variant + " -> " + pendingVariant + " cancelled"); pendingVariant = -1; RequestSerialization(); }
                return;
            }
            if (IsEngaged)
            {
                pendingVariant = v;         // net: owner-only; last request wins
                RequestSerialization();
                Debug.Log("[KH_FrisbeeCatch] variant " + variant + " -> " + v + " deferred (engaged)");
                return;
            }
            SetVariant(v);
        }

        // The only writer of `variant` (review: no double write). Ready re-takes its spot because the geometry moved.
        void SetVariant(int v)
        {
            if (!IsDogOwner() || !Networking.IsOwner(gameObject)) return; // net: owner-writes-synced (Codex D: both Owners)
            int was = variant;
            variant = v;                    // net: owner-only
            pendingVariant = -1;
            ApplyVariant();                 // the Owner never gets OnDeserialization, so it expands the preset here
            _standoffValid = false;
            RequestSerialization();
            Debug.Log("[KH_FrisbeeCatch] variant " + was + " -> " + v + " applied");
        }

        // Expand the preset into the working values the Ready target and the clip read (round 2: not the planner).
        // Runs on every client, from the synced number, so display and decision agree without extra sync.
        void ApplyVariant()
        {
            _appliedVariant = variant;
            _runIn = variant == VariantRunInSide || variant == VariantRunInFar || variant == VariantRunInBeside;
            _twist = variant == VariantTwist;   // ApplyState picks the Twist Bool and the twist facts from this
            // v3 waits ahead of the holder looking downfield, so the disc passes it from behind: the only preset
            // that gets a chance at an overtake, and the only one whose planner differs (round 2b).
            _overtake = variant == VariantRunInBeside;
            // Ready target. v4 / v5 keep the Normal stand-off: only the pose / the flight differ there.
            _readyAhead = standoffDistance; _readyLateral = 0f; _readyFaceForward = false;
            if (variant == VariantRunInSide) { _readyAhead = ReadyAhead1; _readyLateral = ReadyLateral1; }
            else if (variant == VariantRunInFar) { _readyAhead = ReadyAhead2; _readyLateral = ReadyLateral2; }
            else if (variant == VariantRunInBeside) { _readyAhead = ReadyAhead3; _readyLateral = ReadyLateral3; _readyFaceForward = true; }
            // A 4 m sideways target swings 4 m when the head turns 30 degrees: a wider slack, not a faster refresh.
            _readySlack = _runIn ? Mathf.Max(standoffSlack, RunInReadySlack) : standoffSlack;
            // Round 2: the planner reads nothing else from the preset. v1..3 differ from v0 only in where the dog
            // waits, so "ran in from the side" is produced by the geometry of the throw, not by a second scan.
            // Pose facts. The planner, the jump and the landing read the working values only, so swapping the clip
            // swaps the mouth the plan aims with (the twist mouth is off to the side and slightly behind the root).
            if (_twist)
            {
                _mouthCatch = mouthOffsetCatchTwist; _crouchDur = clipCrouchDurationTwist; _riseDur = clipRiseDurationTwist; _recoverDur = clipRecoverDurationTwist;
            }
            else
            {
                _mouthCatch = mouthOffsetCatch; _crouchDur = clipCrouchDuration; _riseDur = clipRiseDuration; _recoverDur = clipRecoverDuration;
            }
        }

        // ---- Requests from the disc (thrower's client). Owner of this object executes them. ----
        [NetworkCallable(maxEventsPerSecond: 5)]
        public void ReqCatch(int gen, Vector3 p0, Vector3 v0, double launchServerTime, int thrower, float curve)
        {
            if (!IsDogOwner()) return;
            if (IsCarrying || IsAirborne) return; // finish the mouth / the landing first; the launcher refuses these too
            if (!CanPlay(p0)) return;             // same gate as Ready: leash / Fetch / gathering / treat / rest / play area
            // Plausibility (network callable; anyone may send): a real launch is capped by the disc and starts nearby.
            if (v0.magnitude > frisbee.maxSpeed * 1.05f || Vector3.Distance(p0, dogRoot.position) > 200f) return;
            if (Networking.GetServerTimeInSeconds() - launchServerTime > 5.0) return;
            if (float.IsNaN(curve) || float.IsInfinity(curve)) return;   // a forged value would poison every prediction (Codex D)
            if (curve < -1f || curve > 1f) return; // a real throw carries FlightCurve (0 or CurveDefault); clamping would split flight and prediction
            _haveLaunch = true; _gen = gen; _p0 = p0; _v0 = v0; _t0 = launchServerTime; _curve = curve; _flightEnded = false;
            _plan = PlanNone; _nextPlan = 0f; _deviationSince = -1f; _landingValid = false; _localPlanValid = true;
            _settledSince = -1f; _holding = false; _runningCatch = false; _faceErrAtTakeoff = -1f;
            _mode = ModeHook; _hookActive = false; _hookDone = false; _motionLatched = false;
            _takeoffMin = RunJumpMinTakeoff; _takeoffCap = RunJumpMaxTakeoff;
            _mCount = 0; _mIdx = 0; _mPrimed = false; _mAccAngle = 0f; _mAccTime = 0f; _mSlowest = -1f; _mPrevDt = 0.016f;   // the last second of motion belongs to THIS throw only
            _yawRateMax = 0f; _speedMin = 0f; _vTake = 0f;
            throwerId = thrower;
            LogThrow(p0, v0, curve);
            TransitionTo(StateChase);
        }

        // Where the dog stands in the throw's own frame, so a run-in preset can be judged from the log alone
        // (ahead = along the throw, lateral = to its left/right, signed).
        void LogThrow(Vector3 p0, Vector3 v0, float curve)
        {
            Vector3 flat = v0; flat.y = 0f;
            Vector3 dir = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 rel = dogRoot.position - p0; rel.y = 0f;
            float lateral = Vector3.Dot(rel, side);
            Debug.Log("[KH_FrisbeeCatch] throw v=" + variant + " speed=" + v0.magnitude.ToString("F2")
                + " ahead=" + Vector3.Dot(rel, dir).ToString("F1")
                + " lateral=" + (lateral >= 0f ? "+" : "") + lateral.ToString("F1")
                + " dogDist=" + Vector3.Distance(dogRoot.position, p0).ToString("F1")
                + " curve=" + curve.ToString("F2"));   // the throw's own bend; every prediction below uses this value
        }

        [NetworkCallable(maxEventsPerSecond: 5)]
        public void ReqFlightEnded(int gen, int reason, Vector3 pos)
        {
            if (!IsDogOwner() || !_haveLaunch) return;
            // The generation counter lives on the thrower's client; a recall by someone else cannot know it.
            // Grabbed/recalled is accepted from anyone (a hand can take the disc anyway); the rest must match.
            if (reason != KH_Frisbee.EndGrabbed && gen != _gen) return;
            _flightEnded = true; _flightEndReason = reason;
        }

        // ---- Owner-side transitions ----
        void TransitionTo(int next)
        {
            state = next;
            _stateEntered = Networking.GetServerTimeInSeconds();
            _released = false;
            if (next == StateIdle) { throwerId = -1; animSpeed = 1f; _plan = PlanNone; _haveLaunch = false; _localPlanValid = false; _yieldAfterLanding = false; _speed = 0f; _settledSince = -1f; _holding = false; _runningCatch = false; }
            if (next == StateJumpCarry || next == StateReturn) ClaimDisc();
            // A switch asked for mid-sequence lands here, at the first quiet moment (IMP-DOG-008). Only the client
            // that owns both the dog and this object may apply it (Codex D: the synced numbers live here).
            if (next == StateIdle && pendingVariant >= 0 && IsDogOwner() && Networking.IsOwner(gameObject)) { int v = pendingVariant; pendingVariant = -1; SetVariant(v); }
            RequestSerialization();
            ApplyState();
        }

        // Ownership of the disc is requested and the physics written in the same frame (the ball does the
        // same in KD_DogFetchController). If the transfer is refused the disc stays where the hand or the
        // previous Owner leaves it; KH_Frisbee.OnOwnershipTransferred drops a kinematic disc nobody carries.
        void ClaimDisc()
        {
            GameObject go = frisbee.gameObject;
            if (!Networking.IsOwner(go))
            {
                VRCPlayerApi lp = Networking.LocalPlayer;
                if (Utilities.IsValid(lp)) Networking.SetOwner(lp, go); // net: setowner-ok: disc only, never the dog
            }
            frisbee._Caught();
        }

        void ReleaseDisc(Vector3 velocity)
        {
            GameObject go = frisbee.gameObject;
            if (!Networking.IsOwner(go))
            {
                VRCPlayerApi lp = Networking.LocalPlayer;
                if (Utilities.IsValid(lp)) Networking.SetOwner(lp, go); // net: setowner-ok: disc only
            }
            frisbee._Release(velocity);
        }

        // Landed / stopped by a wall / lost track: worth walking to. Grabbed or out of the world: not (review D5).
        bool Retrievable() { return _flightEndReason != KH_Frisbee.EndGrabbed && _flightEndReason != KH_Frisbee.EndTimeout; }

        void GiveUp()
        {
            bool held = _discPickup != null && _discPickup.IsHeld;
            if (IsCarrying && !held) ReleaseDisc(Vector3.zero);
            TransitionTo(StateIdle);
        }

        // ---- Display derived from synced state (all clients) ----
        // net: non-owners follow the synced number; the working values are derived, never sent.
        public override void OnDeserialization() { if (variant != _appliedVariant) ApplyVariant(); ApplyState(); }

        void ApplyState()
        {
            bool jumping = IsAirborne;
            // Shared Animator hand-over (review M2): entering a non-Idle state, rewrite everything we own;
            // returning to Idle, stop writing and tell Wander its cached outputs are stale.
            if (_lastApplied != state)
            {
                if (state == StateIdle)
                {
                    // Hand the Animator back clean: no locomotion flags, playback speed 1 (review D2).
                    if (_wroteRun) { dogAnimator.SetBool("Run", false); _wroteRun = false; }
                    if (_wroteWalk) { dogAnimator.SetBool("Walk", false); _wroteWalk = false; }
                    // Both jump Bools go down, whichever clip this preset used (IMP-DOG-008 v4).
                    if (_wroteJump) { dogAnimator.SetBool("Jump", false); _wroteJump = false; }
                    if (_wroteTwist) { dogAnimator.SetBool("Twist", false); _wroteTwist = false; }
                    dogAnimator.speed = 1f; _wroteSpeed = 1f; _speed = 0f;
                    if (wander != null) wander._InvalidateLeashAnimation();
                }
                // Petting: allowed while Idle / Ready, suppressed while engaged (review M2; same hooks Fetch uses).
                if (pet != null) { if (IsEngaged) pet._SuppressPetting(); else if (fetch == null || fetch.IsIdle) pet._ResumePetting(); }
                _outputValid = false;
            }
            if (state != StateIdle) ApplyAnimatorSpeed();
            // One Bool per clip; the preset decides which of the two carries this jump. Both are rewritten after the
            // Animator was handed over (_outputValid false) so another controller's writes cannot leave one stuck.
            bool jumpBool = jumping && !_twist, twistBool = jumping && _twist;
            if (jumpBool != _wroteJump || _lastApplied < 0 || !_outputValid) { dogAnimator.SetBool("Jump", jumpBool); _wroteJump = jumpBool; }
            if (twistBool != _wroteTwist || _lastApplied < 0 || !_outputValid) { dogAnimator.SetBool("Twist", twistBool); _wroteTwist = twistBool; }
            bool pickup = state == StatePickup;
            if (pickup != _wrotePickup || _lastApplied < 0) { dogAnimator.SetBool("Pickup", pickup); _wrotePickup = pickup; }
            if (_discPickup != null) _discPickup.pickupable = state == StateIdle || state == StateReady || state == StateDrop || state == StateChase || state == StateRetrieve;
            _lastApplied = state;
        }

        void Update()
        {
            _dogOwner = IsDogOwner();
            if (!_dogOwner) ObserveSpeed();
            WarnIfOwnerSplit();
            if (state == StateIdle)
            {
                // Idle: Wander owns the Animator and the root; we only watch for a held disc.
                if (_dogOwner && standoffWhenHeld && frisbee.HolderPlayer != null && CanPlay(frisbee.HolderPlayer.GetPosition())) { _standoffValid = false; _nextStandoff = 0f; TransitionTo(StateReady); }
                return;
            }
            if (state == StateReady) { UpdateReady(); if (state == StateReady) UpdateLocomotionAnim(); return; }
            // Became Owner mid-sequence (previous Owner left): no local plan, so stop safely.
            if (_dogOwner && !_localPlanValid) { GiveUp(); return; }
            double now = Networking.GetServerTimeInSeconds();
            float elapsed = (float)(now - _stateEntered);

            // A hand wins over the mouth and over the chase (IsHeld is synced) - but an airborne dog lands first.
            if (_dogOwner && !IsAirborne && _discPickup != null && _discPickup.IsHeld) { GiveUp(); return; }
            // A leash or a gathering accepted meanwhile wins (review D2): on the ground stop now, in the air land first.
            if (_dogOwner && Claimed())
            {
                if (IsAirborne) _yieldAfterLanding = true; else { GiveUp(); return; }
            }

            if (state == StateChase) UpdateChase(now, elapsed);
            else if (state == StateJump || state == StateJumpCarry) UpdateJump(now);
            else if (state == StateLand || state == StateLandCarry) UpdateLand(now, elapsed);
            else if (state == StateRetrieve) UpdateRetrieve(elapsed);
            else if (state == StatePickup) UpdatePickup(elapsed);
            else if (state == StateReturn || state == StateDrop) UpdateReturn(elapsed);
            if (state != StateIdle) UpdateLocomotionAnim(); // a transition above may have handed the Animator back (review D2)
        }

        // Everything that must be true for the dog to stand off for a held disc or to accept a throw.
        // Null neighbours (prototype scene) mean "no such system" (review M1/M3: one gate for both entrances).
        bool CanPlay(Vector3 at)
        {
            if (Claimed()) return false;
            if (fetch != null && !fetch.IsIdle) return false;
            if (wander != null && (wander.IsTreatBusy || wander.IsResting || wander.IsAsleep)) return false;
            if (activities != null && activities.Mode == 2) return false;
            if (!InPlayArea(at, playAreaMargin)) return false;
            return true;
        }

        // Leash attached or gathering participant: authorities that beat the frisbee once they are accepted.
        bool Claimed()
        {
            if (hillsideLeashes != null && dogIndex >= 0 && hillsideLeashes._IsLeashed(dogIndex)) return true;
            if (activities != null && activities.Mode == 1 && activities._IsParticipant(dogIndex)) return true;
            return false;
        }

        bool InPlayArea(Vector3 p, float margin)
        {
            if (playAreaRadius <= 0f) return true;
            Vector3 d = p - playAreaCenter; d.y = 0f;
            return d.magnitude <= playAreaRadius + margin;
        }

        // Anywhere the dog itself would run, crouch, land or pick up must be inside the flat (review D1); the
        // launch point alone gets playAreaMargin because a thrower may stand at the edge.
        bool DogMayGo(Vector3 p) { return InPlayArea(p, 0f); }

        // Same contract as KD_DogFetchController (IMP-NET-005 invariant 8): dogRoot and this object share an Owner.
        void WarnIfOwnerSplit()
        {
            if (_ownerSplitWarned || _dogOwner == Networking.IsOwner(gameObject)) return;
            _ownerSplitWarned = true;
            Debug.LogError("[KH_FrisbeeCatch] Ownership split: dogRoot and " + gameObject.name + " have different Owners - throws are dropped until they converge.");
        }

        // ---- Ready: the disc is in someone's hand. Take a stand-off spot in front of them and watch. ----
        // Display needs nothing synced beyond the state: every client turns the dog by the same synced facts
        // (VRCObjectSync carries position; the facing is written by the Owner).
        void UpdateReady()
        {
            if (!_dogOwner) return;
            VRCPlayerApi holder = frisbee.HolderPlayer;
            if (holder == null) { TransitionTo(StateIdle); return; } // dropped without a throw, or thrown: ReqCatch overrides
            Vector3 holderPos = holder.GetPosition();
            if (!CanPlay(holderPos)) { TransitionTo(StateIdle); return; } // Ready never takes the dog from anyone (review M1)
            if (!_standoffValid || Time.time >= _nextStandoff)
            {
                // Target refreshed at 10 Hz (review D5); the walk toward it is still per frame.
                _nextStandoff = Time.time + 0.1f;
                VRCPlayerApi.TrackingData head = holder.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                Vector3 fwd = head.rotation * Vector3.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.0001f) { fwd = holder.GetRotation() * Vector3.forward; fwd.y = 0f; }
                if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
                fwd = fwd.normalized;
                bool first = !_standoffValid;
                // Which side to wait on is latched once per Ready entry (IMP-DOG-008): the side the dog is already
                // on, so a run-in preset never sends it across the throwing line and never flips left/right.
                if (first)
                {
                    Vector3 rel = dogRoot.position - holderPos; rel.y = 0f;
                    _readySide = Vector3.Dot(rel, Vector3.Cross(Vector3.up, fwd)) < 0f ? -1f : 1f;
                }
                if (_runIn)
                {
                    // A spot metres to the side swings just as far when the holder glances at the dog, so the
                    // hysteresis is on the cause, not on the spot: move only for a real turn or a real walk
                    // (Codex review). Variant 0 keeps the distance-based slack below.
                    Vector3 moved = holderPos - _standoffHolderPos; moved.y = 0f;
                    if (first || Vector3.Angle(fwd, _standoffFwd) > RunInReadyTurnDeg || moved.magnitude > _readySlack)
                    { _standoffSpot = ReadySpot(holderPos, fwd); _standoffFwd = fwd; _standoffHolderPos = holderPos; _standoffValid = true; }
                }
                else
                {
                    Vector3 spot = ReadySpot(holderPos, fwd);
                    if (first || Vector3.Distance(spot, _standoffSpot) > _readySlack)
                    { _standoffSpot = spot; _standoffFwd = fwd; _standoffHolderPos = holderPos; _standoffValid = true; }
                }
            }
            Vector3 to = _standoffSpot - dogRoot.position; to.y = 0f;
            float dist = to.magnitude;
            if (dist > arriveRadius)
            {
                MoveToward(_standoffSpot, dist > returnRunDistance ? runSpeed : walkSpeed, 0f);
                return;
            }
            Coast();
            // "Beside" (v3) waits looking the way the throw will go, so the disc comes past from behind; the rest
            // watch the holder as before.
            if (_readyFaceForward) FaceToward(_standoffFwd);
            else FaceToward(holderPos - dogRoot.position);
        }

        // spot = holder + head direction * ahead + right * (lateral * side), pulled back inside the flat.
        // For variant 0 (ahead = standoffDistance, lateral = 0) this is exactly the stage-2 formula.
        Vector3 ReadySpot(Vector3 holderPos, Vector3 fwd)
        {
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 spot = holderPos + fwd * _readyAhead + right * (_readyLateral * _readySide);
            if (playAreaRadius > 0f)
            {
                // Keep the stand-off spot inside the flat (review P2): pull it toward the centre if needed.
                Vector3 c = spot - playAreaCenter; c.y = 0f;
                if (c.magnitude > playAreaRadius) spot = playAreaCenter + c.normalized * playAreaRadius + Vector3.up * (spot.y - playAreaCenter.y);
            }
            return spot;
        }

        // ---- Chase: sprint to the spot under the catch point, settle there, replanning ----
        void UpdateChase(double now, float elapsed)
        {
            if (!_dogOwner) { UpdateLocomotionAnim(); return; }
            SampleMotion();   // round 4: the last second of yaw rate / speed is the evidence for "machine or dog"
            if (!_haveLaunch) { GiveUp(); return; }
            if (elapsed > chaseTimeout) { GiveUp(); return; }
            if (_flightEnded)
            {
                // Landed or stopped by a wall: fetch from the ground if it is within reach; never re-arm the jump.
                // Grabbed / recalled: nothing to fetch.
                if (Retrievable() && DogMayGo(frisbee.transform.position) && Vector3.Distance(dogRoot.position, frisbee.transform.position) <= retrieveMaxDistance) TransitionTo(StateRetrieve);
                else GiveUp();
                return;
            }
            if (elapsed < noticeDelay) { Coast(); FaceToward(frisbee.transform.position - dogRoot.position); return; }
            float tNow = frisbee._ElapsedSince(_t0);
            if (Time.time >= _nextPlan) { _nextPlan = Time.time + replanInterval; Plan(tNow); }
            if (_plan == PlanJump && _mode != ModeWait)
            {
                // Momentum jump (hook / straight run-up / chase-down). The crouch start moves with the dog's own
                // speed (Plan recomputes it at 10 Hz) and the pace is whatever covers the remaining PATH in the
                // remaining time, floored at a jog: the dog is never standing still when the crouch begins.
                float availC = _catchT - _tUp - _crouchReal - tNow;
                Vector3 toC = _standRoot - dogRoot.position; toC.y = 0f;
                float distC = toC.magnitude;
                RunApproach(availC);
                if (availC < -0.10f) { _plan = PlanNone; _nextPlan = 0f; _settledSince = -1f; _holding = false; }
                else if (availC <= 0.02f)
                {
                    if (distC <= 1.2f) { BeginJump(now); return; }
                    _plan = PlanNone; _nextPlan = 0f; _settledSince = -1f; _holding = false;
                }
            }
            else if (_plan == PlanJump)
            {
                // The wait: get under the point early and go up from a standstill (round 3's shape). Only reached
                // when even the longest loop cannot spend the spare time - a high lob.
                Vector3 to = _standRoot - dogRoot.position; to.y = 0f;
                float dist = to.magnitude;
                UpdateHold(dist);
                float crouchAt = _catchT - _tUp - _crouchReal;
                float avail = crouchAt - tNow;
                if (avail < -0.08f) { _plan = PlanNone; _nextPlan = 0f; _settledSince = -1f; _holding = false; }   // missed the slot: a later candidate next frame
                else if (avail <= 0.02f)
                {
                    // Never early: 0.08 s early puts the disc 0.5 m away. Too far from the spot to jump from here,
                    // so drop the plan and let the scan offer a later (lower) candidate, or the ground.
                    if (dist <= 0.5f) { BeginJump(now); return; }
                    _plan = PlanNone; _nextPlan = 0f; _settledSince = -1f; _holding = false;
                }
                else HookOrSprint(dist);
            }
            else if (_plan == PlanRun)
            {
                // Two shapes share PlanRun: the stand catch waits on the spot facing the disc, the v3 overtake runs
                // straight through the point along the flight and snatches it without ever stopping.
                Vector3 to = _standRoot - dogRoot.position; to.y = 0f;
                float dist = to.magnitude;
                float avail = _catchT - tNow;
                if (_runningCatch)
                {
                    // No arrive radius and no braking: aim past the point once close, so the dog is still at speed
                    // when the mouth gets there (the catch window does the rest). Paced, unlike the intercept:
                    // arriving early is the whole point there and exactly wrong here - a sprint would carry the
                    // dog through the spot before the disc reaches it.
                    float pace = avail > 0.05f ? Mathf.Clamp(dist / avail, runSpeed, chaseRunSpeed) : chaseRunSpeed;
                    MoveToward(dist < 0.5f ? _standRoot + _forward * 1.5f : _standRoot, pace, 0f, chaseAcceleration, true, chaseDeceleration);
                }
                else { UpdateHold(dist); HookOrSprint(dist); }
                if (avail <= catchWindow)
                {
                    if (!_catchDecided) { _catchDecided = true; _bestGap = 99f; }
                    float gap = EdgeGap(tNow);
                    if (gap < _bestGap) _bestGap = gap;
                    if (gap <= catchTolerance && !(_discPickup != null && _discPickup.IsHeld))
                    {
                        Debug.Log("[KH_FrisbeeCatch] " + (_runningCatch ? "overtake" : "stand") + " catch OK mouth-edge=" + gap.ToString("F3") + " at " + avail.ToString("F2") + "s" + VariantTail());
                        TransitionTo(StateReturn); return;
                    }
                    if (avail < -catchWindow)
                    {
                        Debug.Log("[KH_FrisbeeCatch] " + (_runningCatch ? "overtake" : "stand") + " catch MISS best mouth-edge=" + _bestGap.ToString("F3") + VariantTail());
                        _plan = PlanNone; _nextPlan = 0f; _catchDecided = false; _settledSince = -1f; _holding = false;
                    }
                }
            }
            else if (_plan == PlanGround)
            {
                MoveToward(_landing, chaseRunSpeed, arriveRadius * 0.7f, chaseAcceleration, true, chaseDeceleration);
            }
            else
            {
                // Nothing reachable yet: follow the disc from behind until the prediction offers a point,
                // but never out of the flat (review D1): a throw leaving the plaza is simply not chased.
                Vector3 disc = frisbee._PositionAt(_p0, _v0, _curve, tNow);
                if (!DogMayGo(disc) || (_landingValid && !DogMayGo(_landing))) { GiveUp(); return; }
                MoveToward(disc, chaseRunSpeed, 1.0f, chaseAcceleration, true, chaseDeceleration);
            }
            ObserveFlight(tNow);
        }

        // Hold band bookkeeping (round 2b). The sprint's arrive radius is 0.1 m, so the hold must start well
        // outside it or it never starts at all; once holding, only a real displacement puts the dog back on the
        // sprint, otherwise it flickers on the band edge (the "jitter" in the PCVR notes).
        void UpdateHold(float dist)
        {
            if (!_holding) { if (dist < HoldEnterRadius) { _holding = true; _settledSince = Time.time; } }
            else if (dist > HoldExitRadius) { _holding = false; _settledSince = -1f; }
        }

        // One step of a MOMENTUM approach (round 4 amendment): run the hook - leg 1 to A, then, from the corner on,
        // leg 2 along the run-up line with the heading blending into _forward - paced so the dog crosses the crouch
        // start exactly on time and still running. Nothing here ever brakes to a stop.
        void RunApproach(float avail)
        {
            Vector3 dogPos = dogRoot.position;
            if (_hookActive && !_hookDone)
            {
                Vector3 toA = _hookPoint - dogPos; toA.y = 0f;
                if (toA.magnitude <= HookCornerRadius) _hookDone = true;
            }
            float pathLeft = _hookActive && !_hookDone ? HookPath(dogPos, _hookPoint, _standRoot)
                : Vector3.Distance(new Vector3(dogPos.x, 0f, dogPos.z), new Vector3(_standRoot.x, 0f, _standRoot.z));
            // Paced to arrive AS the crouch begins, not sooner. A lower floor here (the old HookMinPace) made the
            // dog cover a short approach early, stop on the spot and then lunge - the stop the user objected to.
            // The floor only applies while there is more than a jog-second of ground left to cover.
            float pace = avail > 0.05f ? pathLeft / avail : chaseRunSpeed;
            if (pathLeft > MotionMinSpeed * MotionWindow && pace < HookMinPace) pace = HookMinPace;
            if (pace > chaseRunSpeed) pace = chaseRunSpeed;
            if (_hookActive && !_hookDone)
            {
                MoveToward(_hookPoint, pace, 0f, chaseAcceleration, true, chaseDeceleration);
                return;
            }
            // Aim past the crouch start once it is under the nose: braking onto a point is exactly what must not
            // happen here, and the air solve absorbs the last few centimetres anyway.
            Vector3 toC = _standRoot - dogPos; toC.y = 0f;
            float dT = toC.magnitude;
            // The last few metres are held at the take-off ceiling, so the speed the dog crosses the crouch start
            // with IS the speed it jumps at - no braking stride between the two. Further out it may run flat out.
            float ceiling = dT > RunJumpTrimDistance ? chaseRunSpeed : _takeoffCap;
            if (pace > ceiling) pace = ceiling;
            MoveToward(dT < 0.6f ? _standRoot + _forward * 2f : _standRoot, pace, 0f, chaseAcceleration, false, chaseDeceleration);
            if (dT > 0.05f)
            {
                // The heading rolls onto the catch facing across the run-up, so the last stride is already on it.
                float w = _hookActive ? Mathf.Clamp01(1f - dT / Mathf.Max(0.5f, _hookLen)) : 1f;
                Vector3 steer = Vector3.Lerp(toC / dT, _forward, w);
                TurnAt(steer, _speed > walkSpeed ? turnSpeedDegPerSec : walkTurnDegPerSec); // net: owner-only
            }
            else TurnAt(_forward, turnSpeedDegPerSec);
        }

        // One step of a WAIT approach toward _standRoot. Outside the hold band the dog runs the small hook: leg 1
        // to A, then leg 2 to the spot with the heading blending into the facing the catch needs, so the last
        // stride is already along _forward. Inside the band it holds, turns off the residual at a dog's standing
        // rate and shuffles the last centimetres.
        void HookOrSprint(float dist)
        {
            if (_holding)
            {
                // Round 3 turned here at 720 deg/s because FaceToward would have used the idle rate (30 deg/s = six
                // seconds for an about-face). Round 4: the hook delivers the facing, so what is left is small and
                // 120 deg/s is enough - and it is a turn, not a snap.
                TurnAt(_forward, standTurnDegPerSec); // net: owner-only
                // Deadband on the residual, and no facing from the walk, so the two do not fight each other.
                if (dist > 0.08f) MoveToward(_standRoot, walkSpeed, 0f, acceleration, false);
                else Coast();
                return;
            }
            if (_hookActive && !_hookDone)
            {
                Vector3 toA = _hookPoint - dogRoot.position; toA.y = 0f;
                float dA = toA.magnitude;
                // The corner is a blend, not a waypoint: inside HookCornerRadius the dog is already steering at the
                // spot, so it rounds A instead of touching it. Latched, so it can never be re-entered.
                if (dA <= HookCornerRadius) _hookDone = true;
                else { MoveToward(_hookPoint, chaseRunSpeed, 0f, chaseAcceleration, true, chaseDeceleration); return; }
            }
            // Second leg. MoveToward's own brake (chaseDeceleration) eases the run down into the spot - at the hold
            // band it is still rolling at a jog - and the heading is steered separately so it can
            // arrive on _forward rather than on the line it travelled.
            Vector3 toT = _standRoot - dogRoot.position; toT.y = 0f;
            float dT = toT.magnitude;
            MoveToward(_standRoot, chaseRunSpeed, 0f, chaseAcceleration, false, chaseDeceleration);
            if (dT > 0.05f)
            {
                // The heading rolls onto the catch facing across the last metres of the run-in, hook or not: that
                // is what lets Eta charge the facing at the running rate instead of billing a spin on arrival.
                float w = Mathf.Clamp01(1f - dT / Mathf.Max(0.5f, _hookActive ? _hookLen : HoldBlendLength));
                Vector3 steer = Vector3.Lerp(toT / dT, _forward, w);
                TurnAt(steer, _speed > walkSpeed ? turnSpeedDegPerSec : walkTurnDegPerSec); // net: owner-only
            }
        }

        // Turn at an explicit rate (FaceToward picks one from the gait; the approach wants to choose).
        void TurnAt(Vector3 dir, float ratePerSec)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0004f) return;
            dogRoot.rotation = Quaternion.RotateTowards(dogRoot.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up), ratePerSec * Time.deltaTime); // net: owner-only
        }

        // ---- Motion quality (round 4). Owner-only, Chase only; the harness reads the two numbers off the log. ----
        void SampleMotion()
        {
            float dt = Time.deltaTime;
            Vector3 fwd = dogRoot.forward;
            if (_mPrimed && dt > 0.0005f)
            {
                // Bucketed, not per frame: a single frame's angle divided by a single frame's dt reads 1368 deg/s
                // on a hitch (measured) even though every writer is capped at 240, because the rotation and the
                // clock do not come from the same interval. Over MotionBucket the two agree, and a REAL snap turn
                // lasts far longer than one bucket, so nothing that matters is smoothed away.
                // Pair the angle with the dt of the frame that TURNED it, not of the frame reading it. The
                // rotation between the previous sample and now was applied with the PREVIOUS frame's deltaTime;
                // summing this frame's instead let a varying frame time report 312 deg/s from a 240 deg/s cap.
                _mAccAngle += Vector3.Angle(_mPrevFwd, fwd);
                _mAccTime += _mPrevDt;
                if (_mSlowest < 0f || _speed < _mSlowest) _mSlowest = _speed;
                if (_mAccTime >= MotionBucket)
                {
                    _mYaw[_mIdx] = _mAccAngle / _mAccTime;
                    _mSpeed[_mIdx] = _mSlowest;
                    _mTime[_mIdx] = Time.time;
                    _mIdx++; if (_mIdx >= MotionSamples) _mIdx = 0;
                    if (_mCount < MotionSamples) _mCount++;
                    _mAccAngle = 0f; _mAccTime = 0f; _mSlowest = -1f;
                }
            }
            _mPrevFwd = fwd; _mPrevDt = dt; _mPrimed = true;
        }

        // Worst turn rate and slowest moment of the last MotionWindow seconds. Latched at the takeoff so the catch
        // line reports the approach, not the (already finished) flight.
        void ComputeMotion()
        {
            float yaw = 0f, slow = -1f, cut = Time.time - MotionWindow;
            for (int i = 0; i < _mCount; i++)
            {
                if (_mTime[i] < cut) continue;
                if (_mYaw[i] > yaw) yaw = _mYaw[i];
                if (slow < 0f || _mSpeed[i] < slow) slow = _mSpeed[i];
            }
            _yawRateMax = yaw; _speedMin = slow < 0f ? 0f : slow;
        }

        string ModeName()
        {
            if (_mode == ModeRun) return "run";
            if (_mode == ModeOvertake) return "overtake";
            if (_mode == ModeStand) return "stand";
            if (_mode == ModeWait) return "wait";
            return "hook";
        }

        string MotionTail()
        {
            if (!_motionLatched) ComputeMotion();
            return " mode=" + ModeName() + " yawRateMax=" + _yawRateMax.ToString("F0")
                + " speedMin=" + _speedMin.ToString("F2") + " vTake=" + _vTake.ToString("F2")
                // How far the approach was. Under MotionMinSpeed * MotionWindow the dog COULD NOT have been running
                // for the whole window, however lively it is: the disc simply came down next to it.
                + " path=" + _planPath.ToString("F1");
        }

        // Earliest flight time at which the mouth can be brought to the disc. See the class comment.
        // Time for the dog to bring its nose onto a line: turn, accelerate, run (review of the first Plays:
        // dist/speed alone made a chosen candidate infeasible on the very next replan, and the plan fell apart).
        float Eta(float dist, float turnDeg)
        {
            float v = chaseRunSpeed, acc = chaseAcceleration;
            float accelDist = v * v / (2f * acc);
            float run = dist < accelDist ? Mathf.Sqrt(2f * dist / acc) : v / acc + (dist - accelDist) / v;
            // Round 4: HALF the turn, because a dog turns while it is getting under way, not before it. The turn
            // was charged in full when it cost 720 deg/s (0.25 s for an about-face and nobody noticed); at a dog's
            // 240 deg/s the same sum bills 0.75 s and refuses every candidate on a throw that goes over its head
            // (measured: every mode fell through to the ground plan). MoveToward also translates along the target
            // direction whatever the body is doing, so a fully serial turn was never what the run actually costs.
            return run + 0.5f * turnDeg / turnSpeedDegPerSec;
        }

        // Horizontal angle between the way the dog RAN IN and the way the disc flies at the catch: 0 = came from
        // behind the disc, 90 = crossed the line, 180 = ran at it head-on. The number the presets are judged by.
        // Round 2 measures the run-in, not the facing: the dog always ends up facing into the disc (_forward),
        // so the facing would read 180 for every throw.
        float EntryAngle()
        {
            Vector3 dv = frisbee._VelocityAt(_v0, _curve, _catchT); dv.y = 0f;
            if (dv.sqrMagnitude < 1e-4f || _entryDir.sqrMagnitude < 1e-4f) return 0f;
            return Vector3.Angle(_entryDir, dv);
        }

        // Tail shared by the catch / stand-catch lines (IMP-DOG-008 log format). Round 2 drops vTake (the takeoff
        // is always from a standstill) and pass (there is one scan).
        // mouthLocal is the ONE number that separates the v4 twist hypotheses: the planner places the root so that
        // root + rotation * _mouthCatch lands on the disc, while the decision measures the real mouthAnchor. If the
        // clip's yaw reaches the runtime rig, mouthLocal == the facts; if it does not, it reads like the untwisted
        // jump mouth; if the frame or a sign is wrong, it is the facts with a component negated. Measured the same
        // way the clip editor measures the facts (rotation only, no scale), so the two are directly comparable.
        string VariantTail()
        {
            Vector3 ml = Quaternion.Inverse(dogRoot.rotation) * (mouthAnchor.position - dogRoot.position);
            // clipT = where the Animator actually is inside the jump clip. The plan puts the catch at
            // clipCrouchDuration + clipRiseDuration (0.60 s for both clips) by construction, so a clipT that is
            // not 0.60 means the pose is out of phase; a clipT of 0.60 with a mouth that is not the facts means
            // the rig is not reproducing the authored pose. Those are different bugs in different files.
            AnimatorStateInfo si = dogAnimator.GetCurrentAnimatorStateInfo(0);
            // Round 4: every catch / miss line also carries how the dog got there and the two motion numbers, so a
            // throw can be failed for LOOKING like a machine even when the mouth met the disc.
            return MotionTail() + " v=" + variant + " entry=" + EntryAngle().ToString("F0")
                + " mouthLocal=(" + ml.x.ToString("F3") + "," + ml.y.ToString("F3") + "," + ml.z.ToString("F3") + ")"
                + " rootFwd=" + Vector3.Angle(dogRoot.forward, _forward).ToString("F0")
                + " clipT=" + (si.normalizedTime * si.length).ToString("F3") + " animSpd=" + dogAnimator.speed.ToString("F2");
        }

        // Ground height under a point on the trajectory. The scan needs it per candidate (the disc may come down
        // over a different part of the flat than the dog stands on); no hit keeps the dog's own ground.
        float GroundYAt(Vector3 p)
        {
            RaycastHit hit;
            float top = p.y + groundRayOffset;
            float len = top - _groundY + groundRayDistance;
            if (len < groundRayDistance) len = groundRayDistance;
            if (Physics.Raycast(new Vector3(p.x, top, p.z), Vector3.down, out hit, len, groundLayerMask, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return _groundY;
        }

        // Where the root must be to meet the disc at flight time t, and the spot on the ground under it.
        // The dog faces INTO the oncoming disc (round 2: it waits for the disc, it no longer runs along with it),
        // so the mouth offset is taken off that facing. Fills the _geo* scratch; commits nothing.
        void CatchGeometry(float t, float rise, float groundY, bool stand, Vector3 dogPos)
        {
            Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
            Vector3 dv = frisbee._VelocityAt(_v0, _curve, t); dv.y = 0f;
            Vector3 fwd;
            if (dv.sqrMagnitude > 1e-4f) fwd = -dv.normalized;
            else { fwd = d - dogPos; fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = dogRoot.forward; }
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd = fwd.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 mouth = stand ? mouthOffsetStand : _mouthCatch;
            _geoForward = fwd;
            _geoCatchRoot = new Vector3(d.x, groundY + rise, d.z) - fwd * mouth.z - right * mouth.x;
            _geoStandRoot = new Vector3(_geoCatchRoot.x, groundY, _geoCatchRoot.z);
        }

        // Intercept-first (IMP-DOG-008 round 2). The trajectory is known at the throw, so the dog picks the point
        // it wants and goes to stand under it: the scan walks the flight forward and takes the FIRST candidate it
        // can reach in time, which - since the disc is coming down by then - is the HIGHEST one it can catch.
        // A candidate is feasible only if the dog can be standing there, settled, before the crouch has to begin.
        // A plan already chosen is kept while it stays reachable, so replanning cannot flap.
        //
        // Round 4 order (the preference, not just the fallback): hook jump AT SPEED -> running jump -> jump from a
        // wait (the round-3 shape, kept only for throws whose spare time even the longest loop cannot use) ->
        // stand catch -> v3 overtake -> ground. A jump is taken wherever one is feasible, because PCVR 3's
        // complaint was that the dog barely jumped at all.
        void Plan(float tNow)
        {
            _groundY = SnapToGround(dogRoot.position).y;
            Vector3 dogPos = dogRoot.position;
            float held = _catchT - tNow;
            if ((_plan == PlanJump || _plan == PlanRun) && held > -(catchWindow + 0.02f) && KeepPlan(tNow, dogPos, held)) return;
            int chosen = ScanJump(tNow, dogPos);
            if (chosen == PlanNone) chosen = ScanRunJump(tNow, dogPos);
            if (chosen == PlanNone) chosen = ScanWaitJump(tNow, dogPos);
            if (chosen == PlanNone) chosen = ScanStand(tNow, dogPos);
            // v3 (並走) keeps its snatch-on-the-run, but it now sits behind the two jumps: "chase it down and jump"
            // is the shape the user asked for, and the overtake is what is left when no jump fits.
            if (chosen == PlanNone && _overtake) chosen = ScanOvertake(tNow, dogPos);
            if (chosen == PlanNone) { _planSlack = 0f; _runningCatch = false; if (_landingValid && DogMayGo(_landing)) chosen = PlanGround; }
            // An overtake and a stand catch are both PlanRun but nothing alike, and a hook and a running jump are
            // both PlanJump, so the mode is part of the identity of the plan, not just of the line.
            if (chosen != _plan || _runningCatch != _loggedRunning || _mode != _loggedMode)
            {
                _settledSince = -1f; _holding = false;   // a new plan means a new spot: the hold starts over
                Debug.Log("[KH_FrisbeeCatch] plan=" + chosen + " run=" + (_runningCatch ? 1 : 0) + " mode=" + ModeName()
                    + " catchT=" + _catchT.ToString("F2") + " rise=" + _rootRise.ToString("F2")
                    + " tUp=" + _tUp.ToString("F2") + " dist=" + Vector3.Distance(dogPos, chosen == PlanGround ? _landing : _standRoot).ToString("F1")
                    + " slack=" + _planSlack.ToString("F2") + " entry=" + EntryAngle().ToString("F0") + " v=" + variant
                    // Which pose facts this plan was aimed with: the twist set is the one under suspicion (v4).
                    + (_twist ? " facts=(" + _mouthCatch.x.ToString("F3") + "," + _mouthCatch.y.ToString("F3") + "," + _mouthCatch.z.ToString("F3") + ")" : ""));
                _loggedRunning = _runningCatch; _loggedMode = _mode;
            }
            _plan = chosen;
        }

        // True = the plan the dog is already executing still works, so leave it alone.
        bool KeepPlan(float tNow, Vector3 dogPos, float held)
        {
            if (_mode == ModeHook || _mode == ModeRun || _mode == ModeStand)
            {
                // Every momentum jump is re-derived, not merely re-checked: the crouch start follows the speed the
                // dog has RIGHT NOW, so a dog that is running early pushes the crouch start further out instead of
                // arriving and stopping on it (round 2's failure, and the reason PCVR 3 saw no running jumps).
                float vTake = Mathf.Clamp(_speed, _takeoffMin, _takeoffCap);
                Vector3 ground = new Vector3(_catchRoot.x, _catchRoot.y - _rootRise, _catchRoot.z);
                // _forward is FIXED at the commit (the run-up is a line, not something re-aimed every tick); only
                // the crouch start slides along it, and never past the dog - a run-up the dog would have to reverse
                // out of is what dropped every twist plan.
                Vector3 toGround = ground - dogPos; toGround.y = 0f;
                float fit = toGround.magnitude / (_tUp + _crouchReal);
                if (vTake > fit) vTake = fit < RunJumpShortTakeoff ? RunJumpShortTakeoff : fit;
                Vector3 crouchStart = ground - _forward * (vTake * (_tUp + _crouchReal));
                if (!DogMayGo(crouchStart)) return false;
                // Deadband. The crouch start is derived from the speed, and the speed is paced at the crouch start:
                // rewriting it at 10 Hz makes the two chase each other (the dog speeds up, the point comes toward
                // it, it slows down, the point runs away). Only a real move is worth re-aiming for.
                if (Vector3.Distance(crouchStart, _standRoot) > 0.10f)
                {
                    // The loop is attached to the run-up line, so it travels with it instead of being re-solved.
                    if (_hookActive && !_hookDone) _hookPoint += crouchStart - _standRoot;
                    _standRoot = crouchStart;
                    _jumpSpeed = vTake;
                }
                float availR = (_catchT - _crouchReal - _tUp) - tNow;
                Vector3 toR = crouchStart - dogPos; toR.y = 0f;
                float pathR = _hookActive && !_hookDone ? HookPath(dogPos, _hookPoint, crouchStart) : toR.magnitude;
                if (availR <= 0.02f) return pathR < 1.2f;
                // A little over the run speed is not a dropped plan: the dog cuts the corner at A, so the measured
                // path always overstates what is left. Dropping on the nose made the lob fall to the ground plan.
                if (pathR / availR > chaseRunSpeed * 1.15f) return false;   // only the sprint is the hard limit
                _jumpSpeed = vTake;
                return true;
            }
            // Settling is not required to KEEP a plan: once the dog is on its way, arriving at all beats swapping to
            // a candidate it has not started for. The facing IS required - it is the thing the dog was skipping -
            // except on an overtake, which is caught facing the way it runs.
            Vector3 toHeld = _standRoot - dogPos; toHeld.y = 0f;
            float distHeld = toHeld.magnitude;
            // Close in, the dog is not running anywhere: it shuffles centimetres and turns on the spot, so the cost
            // of the plan is its heading residual, not a run plus a turn onto some target direction. Charging
            // Angle(dog->spot, _forward) here read 180 deg for a dog standing just PAST the spot and cost 0.25 s +
            // Eta, which dropped every stand catch onto the ground plan a moment before its window (ClientSim
            // regression, Desktop 5-6 m/s throws: plan=3 slack=0.32 -> plan=2, 0 catches).
            bool close = _holding || distHeld < HoldExitRadius;
            float cost;
            if (_runningCatch) cost = Eta(distHeld, Vector3.Angle(dogRoot.forward, toHeld));
            else if (close) cost = Eta(distHeld, 0f) + Vector3.Angle(dogRoot.forward, _forward) / standTurnDegPerSec;
            else if (_hookActive && !_hookDone) cost = HookEta(dogPos, _hookPoint, _standRoot);
            else cost = Mathf.Max(Eta(distHeld, Vector3.Angle(dogRoot.forward, toHeld)),   // same overlap as ApproachHold
                Vector3.Angle(distHeld > 0.05f ? toHeld / distHeld : dogRoot.forward, _forward) / turnSpeedDegPerSec);
            // A plan the dog is already standing on is never given up. Success is decided by the catch window
            // (stand catch) or by the 0.5 m gate at the crouch (jump) - not by a feasibility sum that can go
            // negative while the dog is in exactly the right place.
            bool committed = _holding && !_runningCatch && distHeld <= HoldEnterRadius
                && (_plan == PlanRun || _catchT - _tUp - _crouchReal - tNow <= 0.5f);
            if (!committed && cost + _crouchReal + _tUp > held + 0.05f) return false;
            // Fine-tune while waiting: the catch time is fixed, the spot is re-derived from the prediction (the
            // ground under the disc may read differently from where the dog now stands). Only a real difference
            // moves the target: a 10 Hz rewrite of the same point is what reads as jitter.
            if (_holding && !_runningCatch)
            {
                CatchGeometry(_catchT, _rootRise, GroundYAt(frisbee._PositionAt(_p0, _v0, _curve, _catchT)), _plan == PlanRun, dogPos);
                if (DogMayGo(_geoStandRoot) && Vector3.Distance(_geoStandRoot, _standRoot) > 0.03f)
                { _catchRoot = _geoCatchRoot; _standRoot = _geoStandRoot; _forward = _geoForward; }
            }
            return true;
        }

        // The six loops, smallest to largest. Spare time is spent here, on the ground, at speed.
        float HookLenAt(int k) { return HookLengthMin + (HookLengthMax - HookLengthMin) * (k / (float)(HookCombos - 1)); }
        float HookAngAt(int k) { return HookAngleMin + (HookAngleMax - HookAngleMin) * (k / (float)(HookCombos - 1)); }

        float HookPath(Vector3 dogPos, Vector3 A, Vector3 C)
        {
            Vector3 toA = A - dogPos; toA.y = 0f;
            Vector3 toC = C - A; toC.y = 0f;
            return toA.magnitude + toC.magnitude;
        }

        // The whole path run as one (the dog never stops on it), plus the turn onto it and half the corner at A -
        // the other half is turned while still running, which is the entire point of the hook.
        float HookEta(Vector3 dogPos, Vector3 A, Vector3 C)
        {
            Vector3 toA = A - dogPos; toA.y = 0f;
            float dA = toA.magnitude;
            Vector3 dirA = dA > 0.05f ? toA / dA : dogRoot.forward;
            float turnIn = Vector3.Angle(dogRoot.forward, dirA);
            Vector3 toC = C - A; toC.y = 0f;
            float legLen = toC.magnitude;
            float corner = legLen > 0.01f ? Vector3.Angle(dirA, toC / legLen) : 0f;
            return Eta(dA + legLen, turnIn) + 0.5f * corner / turnSpeedDegPerSec;
        }

        // How the dog reaches the crouch start C on the line F, STILL RUNNING, inside `avail` seconds. Fills
        // _apPoint / _apMode / _apEta / _apLen / _apPath / _apHook. It takes the BIGGEST loop that still fits, so a
        // throw with time to spare is answered by running further, not by arriving early and standing there.
        // _apMode comes back ModeStand when no loop fits but a straight run-up would (the dog is already coming
        // from a direction it can straighten out), and ModeWait when the only way to be there is to be there early.
        void Approach(Vector3 C, Vector3 F, Vector3 dogPos, float avail)
        {
            Vector3 rel = dogPos - C; rel.y = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, F);
            float side = Vector3.Dot(rel, right) < 0f ? -1f : 1f;
            bool found = false;
            avail -= HookEtaMargin;    // the loop is sized to land inside the budget, not exactly on it
            for (int k = 0; k < HookCombos; k++)
            {
                float len = HookLenAt(k), deg = HookAngAt(k);
                // -F swung `deg` toward +right for side = +1 (AngleAxis about up turns +right toward -F).
                Vector3 A = C + (Quaternion.AngleAxis(-side * deg, Vector3.up) * (-F)) * len;
                if (!DogMayGo(A)) A = C + (Quaternion.AngleAxis(side * deg, Vector3.up) * (-F)) * len;
                if (!DogMayGo(A)) continue;
                float e = HookEta(dogPos, A, C);
                if (found && e > avail) break;      // every loop after this one is only longer
                if (!found || e <= avail)
                { _apPoint = A; _apEta = e; _apLen = len; _apPath = HookPath(dogPos, A, C); _apHook = true; _apMode = ModeHook; found = true; }
            }
            if (found && _apEta <= avail) return;
            // No loop fits. A straight run-up still takes off at speed, but only if the dog is coming from a
            // direction it can straighten out; otherwise it would have to arrive backwards, and that is a wait.
            Vector3 to = C - dogPos; to.y = 0f;
            float d = to.magnitude;
            Vector3 app = d > 0.05f ? to / d : F;
            _apPoint = C; _apLen = 0f; _apHook = false; _apPath = d;
            _apEta = Eta(d, d > 0.05f ? Vector3.Angle(dogRoot.forward, to) : 0f);
            _apMode = Vector3.Angle(app, F) <= StraightMaxOffAngle ? ModeStand : ModeWait;
        }

        // The approach for the plans that really do have to be there early (a high lob, a stand catch): one small
        // hook to deliver the facing, or the straight line - whichever is QUICKER. A dog standing 2 m from the spot
        // should not run a 4.8 m loop to face the right way, and a dog that would have to arrive backwards should
        // not stand there grinding round at the standing rate; charging both and taking the cheaper one is the only
        // version that keeps the near cases (the ClientSim stand catches) and the far ones at the same time.
        // Either way the heading is rolled onto _forward across the last metres of the run-in (HookOrSprint), so
        // the residual turn is charged at the running rate, not at the standing one.
        void ApproachHold(Vector3 T, Vector3 F, Vector3 dogPos)
        {
            Vector3 to = T - dogPos; to.y = 0f;
            float d = to.magnitude;
            Vector3 app = d > 0.05f ? to / d : F;
            float turn = d > 0.05f ? Vector3.Angle(dogRoot.forward, to) : Vector3.Angle(dogRoot.forward, F);
            // max, not sum: the heading is rolled onto F ACROSS the run-in (HoldBlendLength), so the turn and the
            // run happen at the same time. Adding them charged the lob twice and left the jump with 0.03 s of
            // slack, which the next replan then dropped onto a stand catch (measured). The dog still has to be
            // there, and it still has to have turned - whichever of the two takes longer is the cost.
            float straightEta = Mathf.Max(Eta(d, turn), Vector3.Angle(app, F) / turnSpeedDegPerSec);
            Vector3 rel = dogPos - T; rel.y = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, F);
            float side = Vector3.Dot(rel, right) < 0f ? -1f : 1f;
            for (int k = 0; k < 2; k++)
            {
                float s = k == 1 ? -side : side;
                Vector3 A = T + (Quaternion.AngleAxis(-s * HookAngleDeg, Vector3.up) * (-F)) * HookLength;
                if (!DogMayGo(A)) continue;
                float e = HookEta(dogPos, A, T);
                if (e > straightEta) break;
                _apPoint = A; _apMode = ModeWait; _apLen = HookLength; _apHook = true;
                _apPath = HookPath(dogPos, A, T); _apEta = e;
                return;
            }
            _apPoint = T; _apMode = ModeWait; _apLen = 0f; _apHook = false; _apPath = d; _apEta = straightEta;
        }

        bool LaneClear(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from; d.y = 0f;
            float dist = d.magnitude;
            if (dist < 0.05f) return true;
            RaycastHit hit;
            return !Physics.Raycast(from + Vector3.up * 0.3f, d / dist, out hit, dist, frisbee.obstacleMask, QueryTriggerInteraction.Ignore);
        }

        // A step or a hole where the dog would stand or take off.
        bool GroundOk(Vector3 spot, float groundY)
        {
            RaycastHit hit;
            if (!Physics.Raycast(spot + Vector3.up * groundRayOffset, Vector3.down, out hit, groundRayDistance, groundLayerMask, QueryTriggerInteraction.Ignore)) return false;
            return Mathf.Abs(hit.point.y - groundY) <= 0.25f;
        }

        // ---- Pass 1: the jump the dog hooks into AND TAKES OFF FROM AT SPEED. The run-up is the hook's last leg:
        // the dog crosses the crouch start still running and carries that speed through the jump (user, round 4
        // amendment). The first scan also owns the landing point. ----
        int ScanJump(float tNow, Vector3 dogPos)
        {
            float g = frisbee.gravity;
            float floor = minRootRise > PreferJumpMinRise ? minRootRise : PreferJumpMinRise;
            float vTake = Mathf.Clamp(_speed, RunJumpMinTakeoff, RunJumpMaxTakeoff);
            for (int i = 0; i < PlanSamples; i++)
            {
                float t = tNow + 0.2f + i * PlanStep;
                Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
                float groundY = GroundYAt(d);
                float h = d.y - groundY;
                if (h <= frisbee.radius) { _landing = new Vector3(d.x, groundY, d.z); _landingValid = true; return PlanNone; }
                float rise = h - _mouthCatch.y;
                if (rise > maxRootRise) continue;         // still out of reach; it comes down later
                if (rise < floor) continue;               // not a jump: a later pass may stand under it
                CatchGeometry(t, rise, groundY, false, dogPos);
                if (!DogMayGo(_geoStandRoot)) continue;
                float tUp = Mathf.Sqrt(2f * rise / g);
                float scale = _riseDur / tUp;
                float crouch = _crouchDur / scale;
                Vector3 crouchStart = _geoStandRoot - _geoForward * (vTake * (tUp + crouch));
                if (!DogMayGo(crouchStart)) continue;
                float avail = (t - crouch - tUp) - tNow;
                if (avail <= 0.05f) continue;
                Approach(crouchStart, _geoForward, dogPos, avail);
                if (_apMode == ModeWait) continue;             // only reachable by being there early: pass 3's job
                if (_apEta > avail - HookEtaMargin) continue;  // cannot get onto the line in time with room to spare
                if (_apPath / avail < HookWaitPace) continue;  // even the longest loop leaves it loitering: pass 3
                if (!LaneClear(dogPos, _apPoint) || !LaneClear(_apPoint, crouchStart)) continue;
                if (!GroundOk(_geoStandRoot, groundY) || !GroundOk(crouchStart, groundY)) continue;
                CommitApproach(t, rise, tUp, scale, crouch, avail - _apEta, dogPos, crouchStart, vTake);
                return PlanJump;
            }
            return PlanNone;
        }

        // ---- Pass 3: the round-3 jump - be under the point early, settle, go up from there. Kept only for the
        // throws whose spare time even the biggest loop cannot spend (a high lob), because the user is right that
        // a dog does not brake to a halt and then hop. Logged mode=wait so the harness can tell the two apart. ----
        int ScanWaitJump(float tNow, Vector3 dogPos)
        {
            float g = frisbee.gravity;
            float floor = minRootRise > PreferJumpMinRise ? minRootRise : PreferJumpMinRise;
            for (int i = 0; i < PlanSamples; i++)
            {
                float t = tNow + 0.2f + i * PlanStep;
                Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
                float groundY = GroundYAt(d);
                float h = d.y - groundY;
                if (h <= frisbee.radius) break;
                float rise = h - _mouthCatch.y;
                if (rise > maxRootRise || rise < floor) continue;
                CatchGeometry(t, rise, groundY, false, dogPos);
                if (!DogMayGo(_geoStandRoot)) continue;
                float tUp = Mathf.Sqrt(2f * rise / g);
                float scale = _riseDur / tUp;
                float crouch = _crouchDur / scale;
                ApproachHold(_geoStandRoot, _geoForward, dogPos);
                float slack = (t - tNow) - (_apEta + settleTime + crouch + tUp);
                if (slack < 0f) continue;                 // cannot be standing there, already turned, in time
                if (!LaneClear(dogPos, _apPoint) || !LaneClear(_apPoint, _geoStandRoot)) continue;
                if (!GroundOk(_geoStandRoot, groundY)) continue;
                CommitApproach(t, rise, tUp, scale, crouch, slack, dogPos, _geoStandRoot, 0f);
                return PlanJump;
            }
            return PlanNone;
        }

        // ---- Pass 4: stand under it. Same small hook, so the dog is already facing the disc when it arrives. ----
        int ScanStand(float tNow, Vector3 dogPos)
        {
            float floor = minRootRise > PreferJumpMinRise ? minRootRise : PreferJumpMinRise;
            for (int i = 0; i < PlanSamples; i++)
            {
                float t = tNow + 0.2f + i * PlanStep;
                Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
                float groundY = GroundYAt(d);
                float h = d.y - groundY;
                if (h <= frisbee.radius) break;
                if (h - _mouthCatch.y >= floor) continue;                 // a jump candidate: passes 1-3 had their turn
                if (h < mouthOffsetStand.y - tooLowBelowMouth) continue;  // below the standing mouth: wait for the ground
                CatchGeometry(t, 0f, groundY, true, dogPos);
                if (!DogMayGo(_geoStandRoot)) continue;
                ApproachHold(_geoStandRoot, _geoForward, dogPos);
                float slack = (t - tNow) - (_apEta + settleTime);
                if (slack < 0f) continue;
                if (!LaneClear(dogPos, _apPoint) || !LaneClear(_apPoint, _geoStandRoot)) continue;
                if (!GroundOk(_geoStandRoot, groundY)) continue;
                CommitApproach(t, 0f, 0f, 1f, 0f, slack, dogPos, _geoStandRoot, 0f);
                return PlanRun;
            }
            return PlanNone;
        }

        // Shared tail of passes 1, 3 and 4: take the _geo* / _ap* scratch as the plan. standRoot is the point the
        // chase drives at - the crouch start for a momentum jump, the catch spot itself for a wait.
        void CommitApproach(float t, float rise, float tUp, float scale, float crouch, float slack, Vector3 dogPos, Vector3 standRoot, float jumpSpeed)
        {
            _catchT = t; _catchRoot = _geoCatchRoot; _standRoot = standRoot; _forward = _geoForward;
            _rootRise = rise; _tUp = tUp; _animScale = scale; _crouchReal = crouch; _jumpSpeed = jumpSpeed;
            _planSlack = slack; _planPath = _apPath; _catchDecided = false; _runningCatch = false;
            _mode = _apMode; _hookActive = _apHook; _hookPoint = _apPoint; _hookLen = _apLen; _hookDone = false;
            _takeoffMin = RunJumpMinTakeoff; _takeoffCap = RunJumpMaxTakeoff;
            // entry = the direction the dog will actually arrive from (the hook's second leg), not the beeline.
            Vector3 entry = _apHook ? standRoot - _apPoint : standRoot - dogPos; entry.y = 0f;
            _entryDir = entry.sqrMagnitude > 1e-4f ? entry.normalized : _geoForward;
        }

        // ---- Pass 2: the running jump. Run at the point along the dog's own heading and push off at the speed it
        // already has - from behind, across, or straight into the oncoming disc. Nothing here waits, so it is the
        // answer whenever the hook cannot be finished in time, and it is tried BEFORE any plan that stands still. ----
        int ScanRunJump(float tNow, Vector3 dogPos)
        {
            float g = frisbee.gravity;
            float floor = minRootRise > PreferJumpMinRise ? minRootRise : PreferJumpMinRise;
            for (int i = 0; i < PlanSamples; i++)
            {
                float t = tNow + 0.2f + i * PlanStep;
                Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
                float groundY = GroundYAt(d);
                float h = d.y - groundY;
                if (h <= frisbee.radius) break;
                float rise = h - _mouthCatch.y;
                if (rise > maxRootRise || rise < floor) continue;
                Vector3 run = d - dogPos; run.y = 0f;
                if (run.sqrMagnitude < 1f) continue;                   // on top of it already: there is no run-up
                Vector3 fwd = run.normalized;                          // it takes it facing the way it runs
                Vector3 dv = frisbee._VelocityAt(_v0, _curve, t); dv.y = 0f;
                if (dv.sqrMagnitude < 1e-4f) continue;
                float closing = Vector3.Dot(fwd, dv.normalized);
                if (closing < RunJumpMinDot) continue;
                // Running INTO the disc adds both speeds to the closing rate, so the take-off is capped there; the
                // catch window is unchanged and the occasional miss is the price of the dog looking alive.
                float cap = closing < 0f ? RunJumpHeadOnMaxTakeoff : RunJumpMaxTakeoff;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 catchRoot = new Vector3(d.x, groundY + rise, d.z) - fwd * _mouthCatch.z - right * _mouthCatch.x;
                Vector3 standRoot = new Vector3(catchRoot.x, groundY, catchRoot.z);
                float tUp = Mathf.Sqrt(2f * rise / g);
                float scale = _riseDur / tUp;
                float crouch = _crouchDur / scale;
                // Run-up length = take-off speed x (crouch + rise). If the dog is already inside it, SHORTEN the
                // run-up by taking off slower rather than putting the crouch start behind the dog.
                Vector3 toStand = standRoot - dogPos; toStand.y = 0f;
                float vTake = Mathf.Clamp(_speed, RunJumpMinTakeoff, cap);
                float lo = RunJumpMinTakeoff;
                if (toStand.magnitude < vTake * (tUp + crouch))
                { vTake = Mathf.Clamp(toStand.magnitude / (tUp + crouch), RunJumpShortTakeoff, cap); lo = vTake; }
                Vector3 crouchStart = standRoot - fwd * (vTake * (tUp + crouch));
                if (!DogMayGo(standRoot) || !DogMayGo(crouchStart)) continue;
                Vector3 to = crouchStart - dogPos; to.y = 0f;
                float dist = to.magnitude;
                float avail = (t - crouch - tUp) - tNow;
                if (avail <= 0.05f) continue;
                // Reachable at the chase speed; the take-off ceiling is applied over the last RunJumpTrimDistance
                // metres instead, so a candidate is not refused just because the run-up is long.
                if (dist / avail > chaseRunSpeed) continue;
                float eta = Eta(dist, dist > 0.05f ? Vector3.Angle(dogRoot.forward, to) : 0f);
                // Same margin as the hook: a running jump committed with 0.04 s to spare is dropped by the next
                // replan and ends on the ground plan (measured on the twist preset). A dog that is ALREADY running
                // needs half of it - the ETA it is being judged against no longer includes a standing start, and
                // this is the scan that has to re-form a plan after one was dropped mid-flight.
                if (eta > avail - (_speed > runSpeed ? HookEtaMargin * 0.5f : HookEtaMargin)) continue;
                if (!LaneClear(dogPos, crouchStart) || !GroundOk(standRoot, groundY)) continue;
                _catchT = t; _catchRoot = catchRoot; _standRoot = crouchStart; _forward = fwd; _entryDir = fwd;
                _rootRise = rise; _tUp = tUp; _animScale = scale; _crouchReal = crouch; _jumpSpeed = vTake;
                _planSlack = avail - eta; _planPath = dist; _catchDecided = false; _runningCatch = false;
                _mode = ModeRun; _hookActive = false; _hookDone = true; _hookPoint = crouchStart; _hookLen = 0f;
                // The band the 10 Hz re-derive must stay inside: without it a head-on run speeds up past its cap,
                // and a shortened run-up grows back out behind the dog.
                _takeoffMin = lo; _takeoffCap = lo > cap ? lo : cap;
                return PlanJump;
            }
            return PlanNone;
        }

        // ---- Pass 4: v3 overtake. Catch it on the run, from behind, without ever stopping. ----
        int ScanOvertake(float tNow, Vector3 dogPos)
        {
            for (int i = 0; i < PlanSamples; i++)
            {
                float t = tNow + 0.2f + i * PlanStep;
                Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
                float groundY = GroundYAt(d);
                float h = d.y - groundY;
                if (h <= frisbee.radius) break;
                if (h - _mouthCatch.y >= minRootRise) continue;          // high enough to be worth a jump: not this
                if (h < mouthOffsetStand.y - tooLowBelowMouth) continue; // below the running mouth: wait for the ground
                Vector3 dv = frisbee._VelocityAt(_v0, _curve, t); dv.y = 0f;
                if (dv.sqrMagnitude < 1e-4f) continue;
                Vector3 fwd = dv.normalized;                             // running WITH the disc, not into it
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 root = new Vector3(d.x, groundY, d.z) - fwd * mouthOffsetStand.z - right * mouthOffsetStand.x;
                if (!DogMayGo(root)) continue;
                Vector3 to = root - dogPos; to.y = 0f;
                float dist = to.magnitude;
                if (dist < 0.5f) continue;                               // already on it: there is nothing to overtake
                Vector3 approach = to / dist;
                if (Vector3.Dot(approach, fwd) < OvertakeMinDot) continue; // crossing or meeting it, not chasing it down
                float turn = Vector3.Angle(dogRoot.forward, to);
                float slack = (t - tNow) - Eta(dist, turn);              // no settle, no crouch: just be there, moving
                if (slack < 0f) continue;
                if (dist / (t - tNow) < OvertakeMinPace) continue;       // a trot-in is not an overtake
                if (!LaneClear(dogPos, root) || !GroundOk(root, groundY)) continue;
                _catchT = t; _catchRoot = root; _standRoot = root; _forward = fwd; _entryDir = approach;
                _rootRise = 0f; _tUp = 0f; _animScale = 1f; _crouchReal = 0f; _jumpSpeed = 0f;
                _planSlack = slack; _planPath = dist; _catchDecided = false; _runningCatch = true;
                _mode = ModeOvertake; _hookActive = false; _hookDone = true; _hookPoint = root; _hookLen = 0f;
                _takeoffMin = RunJumpMinTakeoff; _takeoffCap = RunJumpMaxTakeoff;
                return PlanRun;
            }
            return PlanNone;
        }

        void BeginJump(double now)
        {
            // The arc is anchored to the planned instant so the mouth meets the prediction on time; the clip
            // starts now, at most a frame or two behind the arc (starts more than 0.1 s late are replanned above).
            _jumpStart = _t0 + (_catchT - _tUp - _crouchReal);
            if (_jumpStart > now) _jumpStart = now;
            _jumpFrom = dogRoot.position; _jumpFrom.y = _groundY; _catchDecided = false; _airborne = false; _bestGap = 99f;
            animSpeed = _animScale;
            // settled = seconds spent standing on the spot before the crouch (round 2 wants this >= settleTime),
            // off = how far from it the dog actually was when the crouch began (the air solve absorbs the rest),
            // face = how many degrees of the about-face were still missing (round 2b: the twist misses came from
            // a mouth offset of 0.63 m applied in the wrong frame, so this is the number that explains them).
            Vector3 offTo = _standRoot - dogRoot.position; offTo.y = 0f;
            _faceErrAtTakeoff = Vector3.Angle(dogRoot.forward, _forward);
            // Round 4 amendment: every mode but the wait takes off with the speed it actually has - the crouch is
            // a stride, not a stop ("実際跳ぶときは減速しない、トップスピードでは?"). The air solve absorbs the rest.
            _jumpSpeed = _mode == ModeWait ? Mathf.Min(_speed, HookMinPace) : Mathf.Min(_speed, _takeoffCap);
            _vTake = _jumpSpeed;
            ComputeMotion(); _motionLatched = true;   // the approach is what these two numbers describe
            Debug.Log("[KH_FrisbeeCatch] takeoff v=" + variant
                + " settled=" + (_settledSince < 0f ? -1f : Time.time - _settledSince).ToString("F2")
                + " off=" + offTo.magnitude.ToString("F2")
                + " face=" + _faceErrAtTakeoff.ToString("F0") + MotionTail());
            TransitionTo(StateJump);
        }

        // ---- Jump: crouch on the ground, then a fixed parabola. Mouth meets the disc at tUp. ----
        void UpdateJump(double now)
        {
            if (!_dogOwner) { UpdateLocomotionAnim(); return; }
            float e = (float)(now - _jumpStart);
            Quaternion look = Quaternion.LookRotation(_forward, Vector3.up);
            dogRoot.rotation = Quaternion.RotateTowards(dogRoot.rotation, look, turnSpeedDegPerSec * Time.deltaTime); // net: owner-only
            _speed = _jumpSpeed;
            if (e < _crouchReal)
            {
                Vector3 p = _jumpFrom + _forward * (_jumpSpeed * e);
                dogRoot.position = SnapToGround(p); // net: owner-only
                _jumpFrom.y = dogRoot.position.y; _groundY = _jumpFrom.y;
                return;
            }
            float ta = e - _crouchReal;
            float g = frisbee.gravity;
            float vy = g * _tUp;
            if (!_airborne)
            {
                // Horizontal air velocity is whatever brings the root from here to the planned catch root at tUp.
                // Round 2: the takeoff is from a standstill, so this only ever corrects the few centimetres the
                // fine-tune left over (the cap stays, for the case where the dog had to jump from up to 0.5 m off).
                _airborne = true; _airStart = dogRoot.position; _airStart.y = _groundY;
                Vector3 need = _catchRoot - _airStart; need.y = 0f;
                _airVel = need / Mathf.Max(0.05f, _tUp);
                float cap = chaseRunSpeed * 1.2f;
                if (_airVel.magnitude > cap) _airVel = _airVel.normalized * cap;
                if (Vector3.Dot(_airVel, _forward) < 0f) _airVel = Vector3.zero; // never fly backwards
            }
            Vector3 pos = _airStart + _airVel * ta;
            pos.y = _groundY + vy * ta - 0.5f * g * ta * ta;
            if (ta >= 2f * _tUp) pos.y = _groundY;
            dogRoot.position = pos; // net: owner-only
            if (!_catchDecided && ta >= _tUp - catchWindow)
            {
                float tNow = frisbee._ElapsedSince(_t0);
                float gap = EdgeGap(tNow);
                if (ta < _tUp - catchWindow + 0.001f) _bestGap = 99f;
                if (gap < _bestGap) _bestGap = gap;
                bool ok = gap <= catchTolerance && !_flightEnded && !_yieldAfterLanding && !(_discPickup != null && _discPickup.IsHeld); // yielding: no new catch (review D4)
                if (ok)
                {
                    _catchDecided = true;
                    float syncGap = Vector3.Distance(frisbee.transform.position, frisbee._PositionAt(_p0, _v0, _curve, tNow));
                    Debug.Log("[KH_FrisbeeCatch] catch OK mouth-edge=" + gap.ToString("F3") + " at " + (ta - _tUp).ToString("F2") + "s observed-predicted=" + syncGap.ToString("F3") + " rise=" + _rootRise.ToString("F2") + " scale=" + _animScale.ToString("F2") + VariantTail());
                    TransitionTo(StateJumpCarry); return;
                }
                if (ta >= _tUp + catchWindow)
                {
                    _catchDecided = true;
                    Vector3 predicted = frisbee._PositionAt(_p0, _v0, _curve, tNow);
                    Vector3 err = predicted - mouthAnchor.position; float dy = err.y; err.y = 0f;
                    bool inJump = dogAnimator.GetCurrentAnimatorStateInfo(0).IsName(_twist ? "Twist" : "Jump");
                    Debug.Log("[KH_FrisbeeCatch] catch MISS best mouth-edge=" + _bestGap.ToString("F3") + " now: dxz=" + err.magnitude.ToString("F2") + " dy=" + dy.ToString("F2")
                        + " yawErr=" + Vector3.Angle(dogRoot.forward, _forward).ToString("F0") + " face=" + _faceErrAtTakeoff.ToString("F0")
                        + " inJumpState=" + inJump + " rise=" + _rootRise.ToString("F2") + " scale=" + _animScale.ToString("F2") + " flightEnded=" + _flightEnded + VariantTail());
                }
            }
            if (ta >= 2f * _tUp) TransitionTo(state == StateJumpCarry ? StateLandCarry : StateLand);
        }

        void UpdateLand(double now, float elapsed)
        {
            if (!_dogOwner) { UpdateLocomotionAnim(); return; }
            float recover = _recoverDur / Mathf.Max(0.1f, _animScale);
            // Round 2: the jump is always a standing one, so the dog lands on the spot and simply stops (v0's rule
            // for every preset). The lerp is kept so an interrupted jump with residual speed still eases out.
            float desired = Mathf.Lerp(_jumpSpeed, 0f, Mathf.Clamp01(elapsed / recover));
            // Every preset: the slide never carries the root off the flat. At the edge the dog holds its ground and
            // the clip plays on, so the landing still reads as a landing (Codex D).
            float reach = _speed > desired ? _speed : desired;
            if (DogMayGo(dogRoot.position + _forward * (reach * Time.deltaTime))) MoveToward(dogRoot.position + _forward * 2f, desired, 0f);
            else _speed = 0f;
            if (elapsed < recover) return;
            if (_yieldAfterLanding) { GiveUp(); return; } // leash / gathering took the dog while airborne
            if (state == StateLandCarry) { TransitionTo(StateReturn); return; }
            if (_discPickup != null && _discPickup.IsHeld) { GiveUp(); return; }
            if (_flightEnded)
            {
                if (Retrievable() && DogMayGo(frisbee.transform.position) && Vector3.Distance(dogRoot.position, frisbee.transform.position) <= retrieveMaxDistance) TransitionTo(StateRetrieve);
                else GiveUp();
            }
            else { _plan = PlanNone; _nextPlan = 0f; TransitionTo(StateChase); } // missed while still flying: plan again
        }

        // ---- Ground pickup: walk to the resting disc, lower the head, take it ----
        void UpdateRetrieve(float elapsed)
        {
            if (!_dogOwner) { UpdateLocomotionAnim(); return; }
            if (elapsed > chaseTimeout) { GiveUp(); return; }
            if (_flightEnded && !Retrievable()) { GiveUp(); return; }
            Vector3 target = frisbee.transform.position;
            if (!DogMayGo(target)) { GiveUp(); return; } // rolled or was carried out of the flat
            if (!_flightEnded) target = _landingValid ? _landing : frisbee._PositionAt(_p0, _v0, _curve, frisbee._ElapsedSince(_t0));
            Vector3 to = target - dogRoot.position; to.y = 0f;
            float dist = to.magnitude;
            // Still flying: sprint to where it will land. On the ground: bring it back at the carry pace, not at
            // the sprint (round 2: only the chase has a deadline).
            MoveToward(target, !_flightEnded ? chaseRunSpeed : (dist > returnRunDistance ? returnRunSpeed : walkSpeed), arriveRadius * 0.7f);
            if (_flightEnded && dist <= arriveRadius) TransitionTo(StatePickup);
        }

        void UpdatePickup(float elapsed)
        {
            if (!_dogOwner) { UpdateLocomotionAnim(); return; }
            Coast();
            if (elapsed < pickupDuration) return;
            Vector3 to = frisbee.transform.position - dogRoot.position; to.y = 0f;
            if (to.magnitude > arriveRadius * 1.5f) { TransitionTo(StateRetrieve); return; } // it moved while the head was down
            TransitionTo(StateReturn);
        }

        // ---- Return to the thrower, wait so they can take it from the mouth, else set it down ----
        void UpdateReturn(float elapsed)
        {
            if (!_dogOwner) { UpdateLocomotionAnim(); return; }
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(throwerId);
            if (!Utilities.IsValid(p)) { GiveUp(); return; }
            Vector3 playerPos = p.GetPosition();
            Vector3 to = playerPos - dogRoot.position; to.y = 0f;
            float dist = to.magnitude;
            if (state == StateReturn)
            {
                if (elapsed > returnTimeout) { GiveUp(); return; }
                MoveToward(playerPos, dist > returnRunDistance ? returnRunSpeed : walkSpeed, returnStopDistance);
                if (dist <= returnStopDistance + 0.05f && _speed <= stopSpeedThreshold) TransitionTo(StateDrop);
                return;
            }
            Coast();
            FaceToward(to);
            if (!_released && elapsed >= dropWaitDuration)
            {
                _released = true;
                ReleaseDisc(dogRoot.forward * 0.3f);
                TransitionTo(StateIdle);
            }
        }

        // Mouth anchor to the disc edge, using the prediction (== flight on the thrower's client).
        float EdgeGap(float tNow)
        {
            float gap = Vector3.Distance(mouthAnchor.position, frisbee._PositionAt(_p0, _v0, _curve, tNow)) - frisbee.radius;
            return gap < 0f ? 0f : gap;
        }

        // ---- Locomotion (compact port of KD_DogFetchController's walk/run/turn/snap) ----
        void MoveToward(Vector3 target, float desiredSpeed, float stopDist) { MoveToward(target, desiredSpeed, stopDist, acceleration, true); }

        void MoveToward(Vector3 target, float desiredSpeed, float stopDist, float accel) { MoveToward(target, desiredSpeed, stopDist, accel, true, deceleration); }

        // face = false: move without steering the body (the hold phase owns the facing itself).
        void MoveToward(Vector3 target, float desiredSpeed, float stopDist, float accel, bool face) { MoveToward(target, desiredSpeed, stopDist, accel, face, deceleration); }

        // decel: round 4. The generic 36 m/s2 is a hand-brake; the chase passes chaseDeceleration so the run rolls
        // down into the catch spot instead of stopping on it (the "ピュッと移動" of PCVR 3).
        void MoveToward(Vector3 target, float desiredSpeed, float stopDist, float accel, bool face, float decel)
        {
            Vector3 pos = dogRoot.position;
            Vector3 to = target - pos; to.y = 0f;
            float dist = to.magnitude;
            float remain = dist - stopDist; if (remain < 0f) remain = 0f;
            float brake = Mathf.Sqrt(2f * decel * remain);
            if (desiredSpeed > brake) desiredSpeed = brake;
            float rate = (desiredSpeed > _speed ? accel : decel) * Time.deltaTime;
            _speed = Mathf.MoveTowards(_speed, desiredSpeed, rate);
            if (remain <= 0f) _speed = 0f;
            if (_speed <= stopSpeedThreshold && desiredSpeed <= stopSpeedThreshold) _speed = 0f;
            if (face && dist > 0.02f) FaceToward(to);
            if (_speed > 0f && dist > 0.0001f)
            {
                float step = _speed * Time.deltaTime; if (step > remain) step = remain;
                dogRoot.position = SnapToGround(pos + (to / dist) * step); // net: owner-only
            }
        }

        void FaceToward(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0004f) return;
            // Round 4: one rate per gait instead of "idle or 720". Standing still inside a catch state is not the
            // same as standing still while wandering - a dog watching a disc turns its whole body at a dog's pace.
            float turnRate;
            if (_speed <= stopSpeedThreshold) turnRate = (state == StateChase || IsAirborne) ? standTurnDegPerSec : idleTurnSpeedDegPerSec;
            else if (_speed <= walkSpeed + 0.2f) turnRate = walkTurnDegPerSec;
            else turnRate = turnSpeedDegPerSec;
            dogRoot.rotation = Quaternion.RotateTowards(dogRoot.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up), turnRate * Time.deltaTime); // net: owner-only
        }

        void Coast()
        {
            if (_dogOwner) _speed = Mathf.MoveTowards(_speed, 0f, deceleration * Time.deltaTime);
            UpdateLocomotionAnim();
        }

        void ObserveSpeed()
        {
            Vector3 p = dogRoot.position;
            float dt = Time.deltaTime;
            // Anything faster than the sprint is a network jump, not a run (the ceiling follows chaseRunSpeed:
            // round 2 sprints at 10 m/s, and a fixed 8 would have blanked the run clip on every observer).
            if (_obsValid && dt > 0.0001f) { Vector3 d = p - _obsPrev; d.y = 0f; _speed = d.magnitude / dt; if (_speed > chaseRunSpeed * 1.5f) _speed = 0f; }
            _obsPrev = p; _obsValid = true;
        }

        void UpdateLocomotionAnim()
        {
            bool moving = _speed > stopSpeedThreshold;
            bool running = moving && _speed >= runAnimSpeed;
            bool walking = moving && !running;
            if (running != _wroteRun || !_outputValid) { dogAnimator.SetBool("Run", running); _wroteRun = running; }
            if (walking != _wroteWalk || !_outputValid) { dogAnimator.SetBool("Walk", walking); _wroteWalk = walking; }
            ApplyAnimatorSpeed();
            _outputValid = true;
        }

        // Jump states play the clip at the arc pace; a sprint above runSpeed plays the run clip faster
        // (every client derives it from its own _speed, observed or written).
        void ApplyAnimatorSpeed()
        {
            float spd = 1f;
            if (IsAirborne) spd = animSpeed;
            else if (_speed > runSpeed) spd = Mathf.Min(maxRunAnimScale, _speed / runSpeed);
            if (Mathf.Abs(spd - _wroteSpeed) > 0.01f || _lastApplied < 0 || !_outputValid) { dogAnimator.speed = spd; _wroteSpeed = spd; }
        }

        // No ground under the point: keep the height (the flat prototype ground has an edge; see review U4).
        Vector3 SnapToGround(Vector3 pos)
        {
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * groundRayOffset, Vector3.down, out hit, groundRayDistance, groundLayerMask, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y;
            return pos;
        }

        // Observed disc far from the prediction for a while means the flight was interrupted without an event.
        void ObserveFlight(float tNow)
        {
            Vector3 predicted = frisbee._PositionAt(_p0, _v0, _curve, tNow);
            float gap = Vector3.Distance(frisbee.transform.position, predicted);
            if (gap < 3f) { _deviationSince = -1f; return; }
            if (_deviationSince < 0f) { _deviationSince = Time.time; return; }
            if (Time.time - _deviationSince > 0.5f) { _flightEnded = true; _flightEndReason = KH_Frisbee.EndUnknown; }
        }

        // Disc at the mouth while carried (display on every client). PostLateUpdate runs after the
        // constraints that place the mesh-side mouth bone, so the disc follows the drawn mouth (review D6).
        public override void PostLateUpdate()
        {
            if (!IsCarrying) return;
            if (_discPickup != null && _discPickup.IsHeld) return;
            Vector3 fwd = dogRoot.forward, up = dogRoot.up;
            frisbee.transform.position = mouthAnchor.position + fwd * carryForward + up * carryUp;
            frisbee.transform.rotation = Quaternion.LookRotation(fwd, up) * Quaternion.Euler(carryTiltDeg, 0f, 0f);
        }
    }
}
