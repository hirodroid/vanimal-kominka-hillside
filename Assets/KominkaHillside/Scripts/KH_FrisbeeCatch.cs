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
    // IMP-DOG-008 invariant: the prototype `variant` only affects the Ready target, the clip selection and the
    // flight curve. The planner, the chase, the jump and the landing are identical across presets. GiveUp, the
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
        [Tooltip("Sprint used to get under the catch point. Round 2: 4.5 m/s could not keep any ETA promise, and a dog " +
            "IS fast, so it sprints at 10 and arrives early on purpose. The run clip is sped up in proportion above runSpeed")]
        public float chaseRunSpeed = 10f;
        [Tooltip("Acceleration while chasing (a sprint start; the walk value 6 is kept for return/retrieve)")]
        public float chaseAcceleration = 25f;
        public float maxRunAnimScale = 3.5f;
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
        [Tooltip("Code-owned since round 2: Eta charges the turn serially, so a dog that has to spin 180 deg to " +
            "chase pays for it out of the catch window. 720 deg/s halves that bill and keeps Eta honest")]
        public float turnSpeedDegPerSec = 720f;
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
            _settledSince = -1f;
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
            if (next == StateIdle) { throwerId = -1; animSpeed = 1f; _plan = PlanNone; _haveLaunch = false; _localPlanValid = false; _yieldAfterLanding = false; _speed = 0f; _settledSince = -1f; }
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
            if (_plan == PlanJump)
            {
                // Sprint to the spot, then hold it. Arriving early is the goal, not a defect: the seconds spent
                // standing under the trajectory are what turns a 0.5 m miss into a catch (`takeoff settled=`).
                Vector3 to = _standRoot - dogRoot.position; to.y = 0f;
                float dist = to.magnitude;
                if (dist <= 0.1f) { if (_settledSince < 0f) _settledSince = Time.time; }
                else _settledSince = -1f;
                float crouchAt = _catchT - _tUp - _crouchReal;
                float avail = crouchAt - tNow;
                if (avail < -0.08f) { _plan = PlanNone; _nextPlan = 0f; _settledSince = -1f; }   // missed the slot: a later candidate next frame
                else if (avail <= 0.02f)
                {
                    // Never early: 0.08 s early puts the disc 0.5 m away. Too far from the spot to jump from here,
                    // so drop the plan and let the scan offer a later (lower) candidate, or the ground.
                    if (dist <= 0.5f) { BeginJump(now); return; }
                    _plan = PlanNone; _nextPlan = 0f; _settledSince = -1f;
                }
                else if (dist > 0.1f) MoveToward(_standRoot, chaseRunSpeed, 0.1f, chaseAcceleration);
                else
                {
                    // On the spot: walk off the last few centimetres of the residual and look into the disc.
                    if (dist > 0.05f) MoveToward(_standRoot, walkSpeed, 0f);
                    else Coast();
                    FaceToward(_forward); // net: owner-only
                }
            }
            else if (_plan == PlanRun)
            {
                // Stand catch: the disc comes through at standing mouth height, so the dog waits on the spot
                // instead of running through it (same decision window as before).
                Vector3 to = _standRoot - dogRoot.position; to.y = 0f;
                float dist = to.magnitude;
                if (dist <= 0.1f) { if (_settledSince < 0f) _settledSince = Time.time; }
                else _settledSince = -1f;
                float avail = _catchT - tNow;
                if (dist > 0.1f) MoveToward(_standRoot, chaseRunSpeed, 0.1f, chaseAcceleration);
                else
                {
                    if (dist > 0.05f) MoveToward(_standRoot, walkSpeed, 0f);
                    else Coast();
                    FaceToward(_forward); // net: owner-only
                }
                if (avail <= catchWindow)
                {
                    if (!_catchDecided) { _catchDecided = true; _bestGap = 99f; }
                    float gap = EdgeGap(tNow);
                    if (gap < _bestGap) _bestGap = gap;
                    if (gap <= catchTolerance && !(_discPickup != null && _discPickup.IsHeld))
                    {
                        Debug.Log("[KH_FrisbeeCatch] stand catch OK mouth-edge=" + gap.ToString("F3") + " at " + avail.ToString("F2") + "s" + VariantTail());
                        TransitionTo(StateReturn); return;
                    }
                    if (avail < -catchWindow)
                    {
                        Debug.Log("[KH_FrisbeeCatch] stand catch MISS best mouth-edge=" + _bestGap.ToString("F3") + VariantTail());
                        _plan = PlanNone; _nextPlan = 0f; _catchDecided = false; _settledSince = -1f;
                    }
                }
            }
            else if (_plan == PlanGround)
            {
                MoveToward(_landing, chaseRunSpeed, arriveRadius * 0.7f, chaseAcceleration);
            }
            else
            {
                // Nothing reachable yet: follow the disc from behind until the prediction offers a point,
                // but never out of the flat (review D1): a throw leaving the plaza is simply not chased.
                Vector3 disc = frisbee._PositionAt(_p0, _v0, _curve, tNow);
                if (!DogMayGo(disc) || (_landingValid && !DogMayGo(_landing))) { GiveUp(); return; }
                MoveToward(disc, chaseRunSpeed, 1.0f, chaseAcceleration);
            }
            ObserveFlight(tNow);
        }

        // Earliest flight time at which the mouth can be brought to the disc. See the class comment.
        // Time for the dog to bring its nose onto a line: turn, accelerate, run (review of the first Plays:
        // dist/speed alone made a chosen candidate infeasible on the very next replan, and the plan fell apart).
        float Eta(float dist, float turnDeg)
        {
            float v = chaseRunSpeed, acc = chaseAcceleration;
            float accelDist = v * v / (2f * acc);
            float run = dist < accelDist ? Mathf.Sqrt(2f * dist / acc) : v / acc + (dist - accelDist) / v;
            return run + turnDeg / turnSpeedDegPerSec;
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
        string VariantTail()
        {
            return " v=" + variant + " entry=" + EntryAngle().ToString("F0");
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
        void Plan(float tNow)
        {
            _groundY = SnapToGround(dogRoot.position).y;
            Vector3 dogPos = dogRoot.position;
            float held = _catchT - tNow;
            if ((_plan == PlanJump || _plan == PlanRun) && held > -(catchWindow + 0.02f))
            {
                // Settling is not required to KEEP a plan: once the dog is on its way, arriving at all beats
                // swapping to a candidate it has not started for.
                Vector3 toHeld = _standRoot - dogPos; toHeld.y = 0f;
                float turnHeld = toHeld.magnitude > 0.05f ? Vector3.Angle(dogRoot.forward, toHeld) : 0f;
                if (Eta(toHeld.magnitude, turnHeld) + _crouchReal + _tUp <= held + 0.05f)
                {
                    // Fine-tune while waiting: the catch time is fixed, the spot is re-derived from the prediction
                    // (the ground under the disc may read differently from where the dog now stands).
                    if (toHeld.magnitude <= 0.1f)
                    {
                        CatchGeometry(_catchT, _rootRise, GroundYAt(frisbee._PositionAt(_p0, _v0, _curve, _catchT)), _plan == PlanRun, dogPos);
                        if (DogMayGo(_geoStandRoot)) { _catchRoot = _geoCatchRoot; _standRoot = _geoStandRoot; _forward = _geoForward; }
                    }
                    return;
                }
            }
            int chosen = PlanNone;
            const int Samples = 32;
            const float Step = 0.1f;
            float g = frisbee.gravity;
            RaycastHit hit;
            for (int i = 0; i < Samples; i++)
            {
                float t = tNow + 0.2f + i * Step;
                Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
                float groundY = GroundYAt(d);
                float h = d.y - groundY;
                if (h <= frisbee.radius) { _landing = new Vector3(d.x, groundY, d.z); _landingValid = true; break; }
                float rise = h - _mouthCatch.y;
                if (rise > maxRootRise) continue;                       // still out of reach; it comes down later
                bool stand = false;
                if (rise < minRootRise)
                {
                    // Not worth a jump. At standing mouth height the dog simply waits for it; lower than that,
                    // wait for the ground instead (the same rule as before, now decided standing still).
                    if (h < mouthOffsetStand.y - tooLowBelowMouth) continue;
                    stand = true; rise = 0f;
                }
                CatchGeometry(t, rise, groundY, stand, dogPos);
                if (!DogMayGo(_geoStandRoot)) continue;
                float tUp = stand ? 0f : Mathf.Sqrt(2f * rise / g);
                float scale = stand ? 1f : _riseDur / tUp;
                float crouch = stand ? 0f : _crouchDur / scale;
                Vector3 to = _geoStandRoot - dogPos; to.y = 0f;
                float dist = to.magnitude;
                // Turning to the spot, or (already there) turning to face the disc, both cost time.
                float turn = dist > 0.05f ? Vector3.Angle(dogRoot.forward, to) : Vector3.Angle(dogRoot.forward, _geoForward);
                float slack = (t - tNow) - (Eta(dist, turn) + settleTime + crouch + tUp);
                if (slack < 0f) continue;                               // cannot be standing there in time
                if (dist > 0.05f && Physics.Raycast(dogPos + Vector3.up * 0.3f, to / dist, out hit, dist, frisbee.obstacleMask, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(_geoStandRoot + Vector3.up * groundRayOffset, Vector3.down, out hit, groundRayDistance, groundLayerMask, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(hit.point.y - groundY) > 0.25f) continue;  // a step or a hole where the dog would stand
                _catchT = t; _catchRoot = _geoCatchRoot; _standRoot = _geoStandRoot; _forward = _geoForward;
                _entryDir = dist > 0.05f ? to / dist : _geoForward;
                _rootRise = rise; _tUp = tUp; _animScale = scale; _crouchReal = crouch; _jumpSpeed = 0f;
                _planSlack = slack; _catchDecided = false;
                chosen = stand ? PlanRun : PlanJump;
                break;
            }
            if (chosen == PlanNone) { _planSlack = 0f; if (_landingValid && DogMayGo(_landing)) chosen = PlanGround; }
            if (chosen != _plan)
            {
                _settledSince = -1f;     // a new kind of plan means a new spot: the settle clock starts over
                Debug.Log("[KH_FrisbeeCatch] plan=" + chosen + " catchT=" + _catchT.ToString("F2") + " rise=" + _rootRise.ToString("F2")
                    + " tUp=" + _tUp.ToString("F2") + " dist=" + Vector3.Distance(dogPos, chosen == PlanGround ? _landing : _standRoot).ToString("F1")
                    + " slack=" + _planSlack.ToString("F2") + " entry=" + EntryAngle().ToString("F0") + " v=" + variant);
            }
            _plan = chosen;
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
            // off = how far from it the dog actually was when the crouch began (the air solve absorbs the rest).
            Vector3 offTo = _standRoot - dogRoot.position; offTo.y = 0f;
            Debug.Log("[KH_FrisbeeCatch] takeoff v=" + variant
                + " settled=" + (_settledSince < 0f ? -1f : Time.time - _settledSince).ToString("F2")
                + " off=" + offTo.magnitude.ToString("F2"));
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
                        + " yawErr=" + Vector3.Angle(dogRoot.forward, _forward).ToString("F0") + " inJumpState=" + inJump + " rise=" + _rootRise.ToString("F2") + " scale=" + _animScale.ToString("F2") + " flightEnded=" + _flightEnded + VariantTail());
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
        void MoveToward(Vector3 target, float desiredSpeed, float stopDist) { MoveToward(target, desiredSpeed, stopDist, acceleration); }

        void MoveToward(Vector3 target, float desiredSpeed, float stopDist, float accel)
        {
            Vector3 pos = dogRoot.position;
            Vector3 to = target - pos; to.y = 0f;
            float dist = to.magnitude;
            float remain = dist - stopDist; if (remain < 0f) remain = 0f;
            float brake = Mathf.Sqrt(2f * deceleration * remain);
            if (desiredSpeed > brake) desiredSpeed = brake;
            float rate = (desiredSpeed > _speed ? accel : deceleration) * Time.deltaTime;
            _speed = Mathf.MoveTowards(_speed, desiredSpeed, rate);
            if (remain <= 0f) _speed = 0f;
            if (_speed <= stopSpeedThreshold && desiredSpeed <= stopSpeedThreshold) _speed = 0f;
            if (dist > 0.02f) FaceToward(to);
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
            float turnRate = _speed <= stopSpeedThreshold ? idleTurnSpeedDegPerSec : turnSpeedDegPerSec;
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
