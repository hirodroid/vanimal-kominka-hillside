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
    // curve and - for v3 alone - which catch shape the solve prefers (IMP-DOG-017: B chase first). The intercept planner,
    // the jump and the landing are identical across presets. GiveUp, the
    // ground retrieve, CanPlay, Claimed, the yield-after-landing reservation, the reachability rules (PlayableAt /
    // PathOk / the jump ground, DES-DOG-005; was playArea / DogMayGo) and ObserveFlight never read it. UpdateLand still
    // stops the recover slide at a step or a drop for every preset (the walking gate; was the edge of the flat, Codex D).
    //
    // IMP-DOG-017 (user 2026-09-26): the planner is SOLVED, not tried shape by shape. At the throw the dog finds when
    // the disc is inside its own jump band (jumpFloorRise..maxRootRise, per breed), and for each instant the average
    // speed each catch shape needs (A around / B chase / C front); the best feasible one is kept for the whole
    // flight and only its pace is adjusted. Stand (D), overtake (D') and ground (E) are the fallbacks. See Planner.cs.
    //
    // IMP-DOG-012 (Codex M4): one Behaviour, one Program Asset, four files (partial). The split moved whole
    // members only; nothing about the behaviour changed.
    //   KH_FrisbeeCatch.cs             fields, constants, synced state, variant, requests, transitions, display, Update
    //   KH_FrisbeeCatch.Planner.cs     the intercept planner (Plan / KeepPlan, Solve over the jump band, D / D' fallbacks)
    //   KH_FrisbeeCatch.Motion.cs      Ready / Chase execution, jump, landing, pickup, return, locomotion
    //   KH_FrisbeeCatch.Diagnostics.cs log lines, motion quality, and the harness read-outs
    //   KH_FrisbeeCatch.Water.cs       DES-DOG-005 M3: the landing in the water (ドボン) and the water states 11-13
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public partial class KH_FrisbeeCatch : UdonSharpBehaviour
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
        [Tooltip("IMP-DOG-020 段階 7: 川の帯。直線移動 (MoveToward) が帯へ入る歩幅を断り、その回は諦める。null = 判定なし (試作シーン)。Course/89 が配線する")]
        public KH_RiverData riverData;
        [Tooltip("水遊び場の中は帯の判定の例外 (犬が水遊び場にいるときに止めない)。Course/89 が配線する")]
        public KH_WaterSpot waterSpot;
        public int dogIndex = -1;
        [Tooltip("DES-DOG-005 M4: nothing at runtime reads these. playAreaCenter = the plaza's centre, kept as the harness's plaza anchor " +
            "(Course/94 F5c-recall, 94b F11 / F12; Walking/3 writes it). playAreaRadius stays 0 (Course/92 writes 0): the circle, its ring and the stage-1 gate are gone")]
        public Vector3 playAreaCenter;
        public float playAreaRadius;
        [Tooltip("BUG-DOG-007 (決定 2026-09-29): Ready と投げの受理は、犬と持ち手 / 投げ手の水平距離がこれ以下のときだけ (m)。0 = 無制限 " +
            "(試作・ハーネス: ConfigureCatcher が 0 を書く)。本番は Course/92 が DefaultReadyMaxDistance を書く。遠い犬はリードか呼び寄せで連れて来る")]
        public float readyMaxDistance = DefaultReadyMaxDistance;
        public const float DefaultReadyMaxDistance = 40f;

        [Header("DES-DOG-005 anywhere (Course/92 and Frisbee/4 Build Scene write these; PCVR tunes them)")]
        [Tooltip("A jump (and a stand / overtake catch) only where the take-off and the landing ground are at most this steep (deg)")]
        public float jumpMaxSlopeDeg = DefaultJumpMaxSlopeDeg;
        [Tooltip("A jump only when |landing ground - take-off ground| is at most this (m)")]
        public float jumpMaxRise = DefaultJumpMaxRise;
        [Tooltip("PlayableAt: the ground may be at most this steep for the dog to go there at all (deg)")]
        public float runMaxSlopeDeg = DefaultRunMaxSlopeDeg;
        [Tooltip("PathOk and the walking gate: the most the ground may rise between two samples / in one stride (m)")]
        public float runMaxStep = DefaultRunMaxStep;

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
            "as a teleport (PCVR 3); round 4 came back down to 8. Round 5 (PCVR 2026-09-22, 'もっと遠くから跳んでいい - " +
            "跳ばずに迷っている感じが多い'): 9.5 m/s. The shape the user objected to was the SNAP TURN and the standing " +
            "start, not the speed, and every metre per second here buys jump candidates the dog can actually reach. " +
            "The run clip is sped up in proportion above runSpeed (capped by maxRunAnimScale)")]
        public float chaseRunSpeed = 9.5f;
        [Tooltip("Acceleration while chasing. Round 5: 14 m/s2 still takes 0.68 s to reach the 9.5 m/s run - the same " +
            "visible ramp as round 4's 12 -> 8, so the start does not read as a teleport")]
        public float chaseAcceleration = 14f;
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

        [Header("Breed (IMP-DOG-017; Build Scene writes these)")]
        [Tooltip("Speed cap of the B chase (catching up from behind) only; A / C keep chaseRunSpeed. Real dogs sprint " +
            "8-11 m/s (Lab / Shiba / Samoyed) and less for the short-legged ones, so +10% over the PCVR-approved 9.5: " +
            "11 for the medium / large breeds, 10.5 for Corgi / Dachs / Pome (user 2026-09-26)")]
        public float chaseSprintSpeed = 11f;
        [Tooltip("Turn rate from a standstill / below walkSpeed at the start of a chase (a real dog turns half round in " +
            "0.3 s). Running turns keep turnSpeedDegPerSec")]
        public float launchTurnDegPerSec = 480f;
        [Tooltip("Lowest root rise that counts as a jump = min(0.25, 0.6 * mouthOffsetStand.y), so a small dog's jump " +
            "band scales with its size. The floor actually used is max(jumpFloorRise, minRootRise)")]
        public float jumpFloorRise = 0.25f;

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

        [Header("Petting after the return (IMP-DOG-013, user 2026-09-22)")]
        [Tooltip("Grace at the start of Ready: for this long the dog stays where it is instead of running off to the " +
            "stand-off spot, so the player who just took the disc out of its mouth can say よしよし first. " +
            "Round 5 (PCVR 2026-09-22): 4 -> 2 s. The pet-then-step-back-then-throw flow was 'めちゃくちゃ気持ちいい' " +
            "but the wait afterwards was too long; petting itself still holds the dog for as long as it lasts")]
        public float readyGraceSeconds = 2f;
        [Tooltip("The grace only holds while the holder is still this close. Walk away and the dog takes its spot at once")]
        public float readyGraceDistance = 2.5f;

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
        // DES-DOG-005 D-4 (M3, KH_FrisbeeCatch.Water.cs): the disc in the water. Same synced `state`, no new synced variable.
        //   11 WaterChase  to a floating disc (wade / swim; the Wander's river legs), then the ownership wait (ClaimDisc, <= 2 s)
        //   12 WaterCarry  the disc in the mouth, to the exit bank (E-3: the thrower's side first)
        //   13 WaterExit   landed with it: 1.2 s, then over the NavMesh to the thrower (-> Return) or a Drop at the return point
        public const int StateWaterChase = 11, StateWaterCarry = 12, StateWaterExit = 13;
        // Catch kind (_plan). The numbers are the log contract (`plan=N`, the harness reads plan=2 as "ground"), so
        // only the names were tidied in IMP-DOG-012 (was PlanGround / PlanRun):
        //   PlanJump            1  taken in the air (log kind `jump`: catch OK / catch MISS)
        //   PlanRetrieve        2  let it land, walk there and pick it up (log kind `ground`)
        //   PlanStandOrOvertake 3  taken without leaving the ground: standing under it (`stand catch`), or, when
        //                          _runningCatch is set, running it down from behind (`overtake catch`)
        const int PlanNone = 0, PlanJump = 1, PlanRetrieve = 2, PlanStandOrOvertake = 3;

        // ---- IMP-DOG-008 presets (numbers from docs/plans/2026-09-21-frisbee-variations.md; public so Verify reads them) ----
        // VariantRunInSide (1) / VariantRunInFar (2): retired 2026-09-26 - kept as numbers only (see IsRetiredVariant).
        public const int VariantNormal = 0, VariantRunInSide = 1, VariantRunInFar = 2, VariantRunInBeside = 3,
            VariantTwist = 4, VariantCurve = 5;
        public const int VariantCount = 6;
        // v1 (VariantRunInSide) and v2 (VariantRunInFar): retired 2026-09-26 (IMP-DOG-017 PCVR round 2 - the user: the
        // normal preset already gives those run-ins through the timing of the throw). The numbers stay (log contract,
        // harness); they are refused as requests, skipped by the cycle, and a synced 1 / 2 (an old scene, a late
        // joiner) runs as Normal.
        public static bool IsRetiredVariant(int v) { return v == VariantRunInSide || v == VariantRunInFar; }
        // The next preset a switch should ask for: cur + 1, wrapping, skipping retired numbers (0 -> 3 -> 4 -> 5 -> 0).
        public static int NextVariant(int cur)
        {
            int v = cur;
            for (int k = 0; k < VariantCount; k++) { v = (v + 1) % VariantCount; if (v < 0) v = 0; if (!IsRetiredVariant(v)) return v; }
            return VariantNormal;
        }
        // DES-UI-002 (user decision 2026-10-03): the world menu's catch style ("フリスビーの取り方", trainers only) cycles
        // Normal -> beside (3) -> twist (4) -> Normal. The curve (5) left that cycle: the curve is now the thrower's own local choice
        // (KH_ToyRecall.ThrowCurve, KH_Frisbee.LaunchCurve). NextVariant above stays the prototype cube's cycle (it still needs 5).
        public static int NextMenuVariant(int cur)
        {
            if (cur == VariantNormal || IsRetiredVariant(cur)) return VariantRunInBeside;
            if (cur == VariantRunInBeside) return VariantTwist;
            return VariantNormal;
        }
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
        // Round 5 (PCVR 2026-09-22): the overtake is every preset's, not v3's alone - a throw the dog started late
        // (beside the thrower, after a よしよし) is run down from behind instead of being abandoned to the ground plan.
        // IMP-DOG-017: it is now D', tried only when no jump (A / B / C) and no stand catch (D) is feasible.
        // Runtime floor on the approach pace - a jog, never a stop (RunApproach and the A approach). Also the most
        // an A (standing) take-off may carry of the speed the dog still has at the crouch.
        public const float HookMinPace = 1.5f;
        // Over the last metres of an A / stand approach the heading rolls onto the catch facing, so the last stride
        // is already along _forward and the residual turn is paid at the running rate.
        public const float HoldBlendLength = 2.5f;
        // The take-off speed IS the speed the dog has at the crouch, clamped to a band a dog can push off from, and
        // it is re-read at every 10 Hz replan. Round 5 (PCVR 2026-09-22 "もっと遠くから跳んでいい"): ceiling 6 -> 7.
        public const float RunJumpMinTakeoff = 2f, RunJumpMaxTakeoff = 7f;
        // A dog already standing inside the run-up distance does not reverse out to get a longer approach - it
        // pounces from where it is (C front; the crouch start never goes behind the dog).
        public const float RunJumpShortTakeoff = 1f;
        // The take-off ceiling is about the JUMP, not about getting there. Far from the crouch start the dog may
        // run at the full chase speed and trim down inside this distance.
        public const float RunJumpTrimDistance = 3f;
        // Round 4b: a dog facing an incoming disc runs at it and jumps (C front). The closing rate is the sum of
        // both speeds, so the head-on take-off is capped.
        public const float RunJumpMinDot = -1f;
        public const float RunJumpHeadOnMaxTakeoff = 3.5f;
        // ---- IMP-DOG-017 solve ----
        // Catch shape (_mode) and its log name (ModeName; the names are the harness contract):
        //   ModeAround   `around`   A: run out past the catch point through one via point, come back onto it facing
        //                           the thrower, settle, jump straight up (jumpSpeed 0)
        //   ModeChase    `chase`    B: from behind, run WITH the disc (up to chaseSprintSpeed) and take off at speed;
        //                           after a catch it runs on through the landing and arcs back (UpdateLand / Return)
        //   ModeFront    `front`    C: run AT the point (in front of / beside the disc) and take off at speed
        //   ModeStand    `stand`    D: no jump reachable - stand under it facing the disc (PlanStandOrOvertake)
        //   ModeOvertake `overtake` D': run it down from behind and snatch it without stopping (PlanStandOrOvertake)
        // The old hook / run / wait shapes are gone (IMP-DOG-017 decision 1). The numbers double as _bannedModes bits.
        public const int ModeAround = 0, ModeChase = 1, ModeFront = 2, ModeStand = 3, ModeOvertake = 4;
        // Band search: coarse step from now to the landing, then bisection of each edge to this tolerance. A small
        // dog's band is 50-110 ms wide (issue table), so the old 0.1 s scan saw 0 or 1 samples of it.
        public const float BandScanStep = 0.05f, BandTolerance = 0.005f;
        // Harness round 2: the solve samples [tIn + BandInset, tOut - BandInset] and clamps the rise to
        // [floor + BandRiseInset, maxRootRise - BandRiseInset], so the pick never sits on the band edge.
        public const float BandInset = 0.03f, BandRiseInset = 0.01f;
        public const int BandScanMaxSteps = 120;   // 6 s of flight; also the D / D' scans' horizon
        // Band sampling: 20 ms, at most 60 instants per interval (a long band gets a wider step instead).
        public const float SolveStep = 0.02f;
        public const int SolveMaxSamples = 60;
        // A kept shape is dropped only when the pace it needs exceeds its cap by this factor (decision 4).
        public const float KeepPaceSlack = 1.05f;
        // Round 5: B / C are running jumps with nothing in hand, so Solve refuses a candidate with less spare time than
        // this (a zero-slack pick is lost to one frame hitch). A keeps >= 0: its settleTime is the margin.
        public const float SolveMinSlack = 0.05f;
        // Harness round 1 (2026-09-26): path / time-left explodes as time-left -> 0, so the pace rule dropped plans a
        // stride from success. It is not asked once the dog is within KeepCloseRun of a B / C / D' target (= the
        // BeginJump gate), inside the hold band for A / D, or with KeepLateSeconds or less to go.
        public const float KeepCloseRun = 1.2f, KeepLateSeconds = 0.15f;
        // Harness round 1: pace the approach to arrive this much BEFORE the deadline (A: before the settle starts;
        // B / C: before the crouch), so a stride's error is spare time instead of a late arrival.
        public const float PaceLeadAround = 0.15f, PaceLeadRun = 0.10f;
        // B / C never pace below HookMinPace while more than this is left to the crouch start (a jog, never a stop).
        public const float RunPaceFloorPath = 0.3f;
        // A's via point: this far past the stand spot along the flight, and this far out on the dog's own side.
        // Within ViaCornerRadius of it the dog starts steering at the spot (the corner is a blend, not a waypoint).
        // No via point when the straight run-in already arrives within ViaStraightAngle of the catch facing.
        public const float ViaAhead = 1.5f, ViaSide = 1.2f, ViaCornerRadius = 1.0f, ViaStraightAngle = 45f;
        // PCVR 2026-09-26: within this of the stand spot the via point is finished and the dog runs straight in.
        public const float ViaFinishDistance = 2f;
        // Solve score = pref x 10 + rise fraction x RiseScoreWeight + speed margin x SpeedScoreWeight. PCVR 2026-09-26
        // ("small dogs' jumps look small"): 3 / 2 -> 6 / 1, so the pick sits high in the band for every breed.
        public const float RiseScoreWeight = 6f, SpeedScoreWeight = 1f;
        // B's planned take-off speed as a fraction of chaseSprintSpeed, and the pace it runs on after landing.
        public const float ChaseTakeoffFraction = 0.6f;
        // B after a catch: keep running (chaseSprintSpeed * ChaseTakeoffFraction) through the recover and this much
        // longer while turning toward the thrower - the arc home (UpdateReturn) - then the normal Return.
        public const float RunThroughSeconds = 1.0f;
        // (V3RiseAnimBoost removed after Codex review: boosting the synced animSpeed alone put the pose out of phase with the arc.)
        // Motion quality ring: the last second of (yaw rate, speed), sampled per frame while chasing.
        public const int MotionSamples = 128;
        public const float MotionWindow = 1f;
        // One sample per this many seconds (see SampleMotion): long enough that the angle and the clock agree.
        public const float MotionBucket = 0.08f;
        // The two numbers the harness judges: a turn faster than this is a snap, and outside around / stand a dog
        // that dropped below this before taking off had stopped and popped.
        public const float MotionMaxYawRate = 300f, MotionMinSpeed = 1.5f;
        // Codex D1: how far past the landing the lane has to be clear. The recover slide carries the dog roughly
        // this far before UpdateLand's edge clamp can stop it, so the stride after the landing is checked too.
        public const float LandingLaneMargin = 0.6f;
        // Below this approach length the speed rule is not satisfiable by ANY dog: the window alone needs
        // MotionMinSpeed * MotionWindow = 1.5 m covered at speed, and the dog still has to accelerate into it from
        // a standstill and a turn. Shorter approaches are judged on the yaw rate only (the disc came down next to
        // the dog - there was nothing to run). Measured: 1.5-2.2 m approaches bottom out at 1.0 m/s on the ramp.
        public const float MotionMinPath = 2.5f;
        // Ready geometry per run-in preset: ahead along the holder's head direction, lateral toward the side the dog is on.
        public const float ReadyAhead1 = 7f, ReadyLateral1 = 4f, ReadyAhead2 = 11f, ReadyLateral2 = 1.5f,
            ReadyAhead3 = 4f, ReadyLateral3 = 2.5f;
        // A run-in spot sits metres to the side, so a head turn toward the dog would swing it: the spot only moves
        // when the holder really turned away (or walked off, RunInReadySlack).
        public const float RunInReadyTurnDeg = 45f;
        // Sideways lift of v5 (1/m, same units as KH_Frisbee.lift; positive = the disc bends right). KH_Frisbee
        // reads it through FlightCurve at launch and hands it back with ReqCatch, so flight and prediction agree.
        // 2026-09-26 (IMP-DOG-017 PCVR round 2, "もっと曲がってほしい"): 0.04 -> 0.07. ReqCatch's |curve| <= 1 gate still holds.
        public const float CurveDefault = 0.07f;
        // ---- DES-DOG-005 stage 3 M1 (docs/plans/2026-09-28-frisbee-anywhere.md) ----
        // Code defaults of the four Inspector values above (Course/92 and ConfigureCatcher write the same numbers).
        public const float DefaultJumpMaxSlopeDeg = 15f, DefaultJumpMaxRise = 0.5f, DefaultRunMaxSlopeDeg = 25f, DefaultRunMaxStep = 0.5f;
        // B-2: two rays at the same point must agree within this, else the candidate is refused. B-1: the apex of the
        // root must clear the landing ground by this.
        public const float JumpIterTolerance = 0.10f, JumpApexClearance = 0.10f;
        // C-1 PathOk: one ground ray every PathStep metres, the first PathMaxLength metres only (<= 25 rays). Each ray
        // starts PathRayTop (= groundRayDistance 4 + groundRayOffset 0.5, the runtime passes its own two) above the
        // previous ground, so the top of a box / cliff up to that height is hit rather than started inside (a ray that
        // starts inside a collider does not see it). A rise of more than runMaxStep, a fall of more than RunMaxDrop (or
        // no ground within that) refuses, and so does an end point whose own ground is not the walked ground. Up 0.5 /
        // down 1.0 is the M1 decision (design B / C, 2026-09-28): a 0.8 m drop is walked down (T-down08).
        public const float PathStep = 1f, PathMaxLength = 25f, PathRayTop = 4.5f, RunMaxDrop = 1.0f;
        // C-2: the most placement rays one Solve may spend on its candidates (T0 / L0 / lanes); beyond it the best so far wins.
        public const int SolveRayCap = 200;
        // D / D': give up the scan after this many ground refusals (slope / step) so a steep throw cannot cost 120 rays.
        public const int ScanGroundRefusals = 8;
        // No ground under a point (B-5): GroundYAt returns this and the sample is discarded.
        public const float NoGround = -100000f;
        // C-2 / C-3: CanPlay re-shoots its one ray at most this often; a refused Ready waits this long before trying again;
        // a dog in the water is asked out at most once per WaterAskInterval.
        public const float PlayableCacheSeconds = 0.25f, ReadyRetrySeconds = 1f, WaterAskInterval = 1f;
        // Harness only (terrain lane): the landing gap is read this long after touchdown.
        public const float LandGapDelay = 0.2f;

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
        // BUG-DOG-003. A launch notice (network event) and the pickup's held state (VRCPickup sync) travel on
        // separate channels, so on the dog Owner the notice can arrive while the disc still reads as held.
        // The notice is parked here (Owner-local, never synced) and accepted on the first frame the disc is
        // seen out of the hand. PendingHoldSeconds is an initial value; tune it from real-device heldFor logs.
        public const float PendingHoldSeconds = 1.0f;
        private bool _pendingLaunch;
        private int _pendingGen, _pendingThrower;
        private Vector3 _pendingP0, _pendingV0;
        private double _pendingT0;
        private float _pendingCurve;
        private float _pendingSince;            // Time.time at receipt
        private double _pendingAgeAtReceipt;    // server age at receipt (log)
        private bool _pendingLocal;             // direct call vs network path (log)
        private bool _pendingEnded; private int _pendingEndReason;  // ReqFlightEnded seen for THIS pending throw
        private bool _pendingSeenThrower;       // the thrower was observed on the disc while THIS notice waited
        private bool _launchLocal;              // the accepted launch's path, kept for its cancel line
        // Owned by the ACCEPTED launch alone: true from AcceptLaunch, false again at TransitionTo(StateIdle), and
        // nothing else writes it. A notice parked while a chase is live must NOT clear it, or the live chase would
        // lose its re-grab detection for as long as the notice sits there (BUG-DOG-003).
        private bool _seenReleased;
        [Header("Diagnostics (harness only)")]
        [Tooltip("0 = read the pickup. 1 = pretend held (holder = disc Owner), -1 = pretend released. Verify asserts 0 in every scene.")]
        public int debugHeldOverride = 0;
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
        // ETA calibration (round 5). _planEta is the seconds the accepted candidate's approach was ESTIMATED to
        // take and _planEtaT is the flight time it was committed at; when the dog actually gets there (hold entry
        // or the crouch) the difference is logged as etaErr. -1 = this plan has no estimate (the ground plan).
        // Nothing reads these: they exist so the harness can say whether Eta is optimistic before anyone tunes it.
        private float _planEta = -1f, _planEtaT;
        private bool _etaLogged;
        private bool _runningCatch;    // v3: this PlanStandOrOvertake is taken on the run (never stop), not standing still
        private bool _holding;         // inside the hold band: turning to face the disc and fine-tuning the spot
        private bool _loggedRunning;   // the _runningCatch the last plan line was printed with
        private int _loggedMode = -1;  // and the mode (a hook and a running jump are both PlanJump)
        private float _faceErrAtTakeoff = -1f;  // facing error when the crouch began (log: `face=`)
        // _mode is the catch shape of this plan (ModeAround / ModeChase / ModeFront / ModeStand / ModeOvertake).
        // _viaPoint is A's via point, and _viaDone latches once the dog is inside ViaCornerRadius of it (the corner is
        // never re-entered, so the dog cannot loop round the spot).
        private int _mode = ModeAround;
        private float _takeoffMin = RunJumpMinTakeoff, _takeoffCap = RunJumpMaxTakeoff;  // take-off speed band of THIS plan (head-on and short run-ups are slower)
        private Vector3 _viaPoint;
        private bool _viaActive, _viaDone;
        // IMP-DOG-017 solve state (Owner-local). _solved: the jump solve has run for this throw (reset by a dropped
        // shape or a missed slot). _bannedModes: bit (1 << mode) per shape dropped by KeepPlan, never offered again
        // for this throw. The rest is what the plan line reports (solve= / vReq= / vCap= / band=).
        private bool _solved;
        private string _dropReason = "";   // why KeepPlan / the chase last gave a plan up (plan= line: drop=)
        private int _bannedModes;
        private int _solveCount;
        private float _bandIn = -1f, _bandOut = -1f, _bandIn2 = -1f, _bandOut2 = -1f;
        private float _pickVReq, _pickVCap;
        private string _whyA = "-", _whyB = "-", _whyC = "-";   // round 7: why each shape failed at the last instant Solve tried (log only)
        // Best candidate so far inside Solve; copied into the plan fields by CommitCandidate only.
        private float _cScore; private int _cMode;
        private float _cT, _cRise, _cTUp, _cScale, _cCrouch, _cJumpSpeed, _cVReq, _cVCap, _cEta, _cPath, _cSlack, _cTakeMin, _cTakeCap;
        private Vector3 _cCatchRoot, _cDrive, _cFwd, _cVia, _cEntry; private bool _cViaActive;
        // B after a catch: the first RunThroughSeconds of Return keep running along _forward (set in UpdateLand).
        private bool _runThrough;
        private bool _runThroughClear;     // decided at touchdown: the whole run-through lane is clear (Codex review)
        // Motion quality ring (Owner, Chase only) and the values latched at the takeoff.
        private float[] _mYaw, _mSpeed, _mTime;
        private int _mIdx, _mCount;
        private Vector3 _mPrevFwd; private bool _mPrimed, _motionLatched;
        private float _mAccAngle, _mAccTime, _mSlowest = -1f, _mPrevDt = 0.016f;
        private float _yawRateMax, _speedMin, _vTake;
        private float _mRanBucketEnd = -1f;  // Time.time the bucket containing _mRanSince closed, -1 = not yet
        private float _mRanSince = -1f;  // IMP-DOG-017 (Codex review): Time.time this throw first sampled >= MotionMinSpeed, -1 = never
        private float _launchYaw;      // IMP-DOG-017: worst turn rate below walkSpeed in the window (log only; excluded from _yawRateMax)
        // Scratch filled by CatchGeometry; copied into the plan fields only once a candidate is accepted.
        private Vector3 _geoForward, _geoCatchRoot, _geoStandRoot;
        private Vector3 _landing;      // predicted disc landing point (PlanRetrieve)
        private bool _landingValid;
        private float _nextPlan;
        private bool _localPlanValid;  // this client holds the plan/launch facts for the current sequence
        // IMP-DOG-012: planner cost counters (Owner-local, never synced, never reset, read by nothing at runtime).
        // The harness reads them off the Udon heap before and after a measured window (--mode=sixdogs), so the
        // difference is the work the planner did. _planScans = Plan() calls that ran the candidate scans (KeepPlan
        // declined); _planEvaluations = trajectory samples examined by the scans (pass x time; each sample may also
        // cost ground / lane raycasts); _planHookTries = A via points sized by Solve (IMP-DOG-017; was hook loops).
        private int _planScans, _planEvaluations, _planHookTries;
        // DES-DOG-005 C-2 cost counters (same rules as above: Owner-local, never reset, harness reads the heap).
        // _planRays = every ray the DES-DOG-005 helpers cast (PlayableAt / PathOk / jump ground / lanes; the band scan's
        // GroundYAt is not counted, it is _planEvaluations' cost as before). _solveRuns = Solve calls. _solveRays = the
        // placement rays of the running Solve (capped at SolveRayCap), _solveRayMax = the largest of those so far.
        private int _planRays, _solveRuns, _solveRays, _solveRayMax;
        private bool _solveCapped;         // the last Solve hit SolveRayCap (log only)
        private bool _inSolve;             // a Solve is running: RayDown / LaneClear check SolveRayCap per ray
        private float _playY;              // the ground the last _PlayableAt ray hit
        private int _solveLaneRefused;     // candidates the last Solve refused for a blocked run-up OR landing lane (log only: laneRefused=)
        private bool _bandUneven;          // FindBand saw more than 0.05 m of ground spread (or no ground) under the flight (B-2 step 1)
        // DES-DOG-005 jump ground of the committed plan (B-2). _takeoffY / _landY: the ground the plan jumps from / lands
        // on, _tDown: fall time from the apex, _landPt: the landing point L0, _jumpRayAt: the crouch start the last T0 / L0
        // rays were shot for (KeepPlan re-shoots only when the crouch start slid >= 0.5 m).
        private float _takeoffY, _landY, _tDown;
        private Vector3 _landPt, _jumpRayAt;
        private float _cTakeoffY, _cLandY, _cTDown; private Vector3 _cLand;
        // Scratch of the last ray (RayDown) and of the last jump-ground test (JumpGround).
        private float _rayY, _rayNy;
        private float _jgY, _jgSlope; private string _jgWhy = "";
        // Scratch of the last air solve (AirLanding): the landing, its ground and the time to it.
        private Vector3 _alPt; private float _alY, _alTime;
        // The predicted landing is reachable (Reachable = PlayableAt + PathOk), computed once per landing (C-2 :228).
        private bool _landingReachable; private Vector3 _landingCheckedAt; private bool _landingChecked;
        // A plan whose PathOk failed at this spot is not re-tested every 10 Hz tick (D / D').
        private Vector3 _pathFailAt; private bool _pathFailValid;
        // CanPlay's one ray, cached (C-2).
        private Vector3 _playAt; private bool _playOk; private float _playNext = -1f;
        private float _waterAskNext;
        private string _canPlayWhy = "";
        // C-3: Ready found no playable stand-off spot; Idle does not re-enter Ready before this.
        private float _readyRetryAt = -1f; private bool _readySpotOk;
        // B-3 / B-4 execution: the air solve at liftoff (vertical speed, time to land, landing ground) and the one
        // apex write of animSpeed.
        private float _airVy, _taLand, _airLandY;
        private bool _apexWritten, _landGapLogged;
        // The walking gate (C-2): this frame's straight move found no ground / too big a step. Update cancels the fetch.
        private bool _groundStop, _snapHit;
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
        private bool _overtake;            // v3 only: the solve scores B chase highest (IMP-DOG-017). D' overtake is a fallback for every preset
        private Vector3 _mouthCatch;       // pose working values, copied from the Jump or the Twist facts set
        private float _crouchDur, _riseDur, _recoverDur;
        private float _settledSince = -1f; // Time.time the dog first stood on the catch spot, -1 = not there yet
        // IMP-DOG-013. _heldSince: Time.time this Ready began (the moment the disc went into a hand). _graceEnded
        // latches once the dog has left for its spot, so the grace is a one-shot per Ready and the dog never
        // wanders back into it. _dropPetHold: seconds of the Drop wait spent being petted, subtracted from the
        // drop timer so the disc is not set down in the middle of a stroke.
        private float _heldSince = -1f;
        private bool _graceLogged, _graceEnded;
        private float _dropPetHold; private bool _dropPetLogged;

        public int State { get { return state; } }
        // IMP-WLD-008 W3 (read-only): who threw the current flight (synced; -1 when idle). The video camera's おかえり compares it with the local id only.
        public int ThrowerId { get { return throwerId; } }
        // IMP-WLD-008 (read-only, local; nothing synced changes): the catch plan this dog committed to for the current throw. Only the dog's
        // Owner plans (ReqCatch runs there), so PlanReadable is false on every other client. The video camera's キャッチ待ち reads it.
        public bool PlanReadable { get { return _localPlanValid && _plan != PlanNone && IsDogOwner(); } }
        public float PlanCatchT { get { return _catchT; } }
        public Vector3 PlanCatchRoot { get { return _catchRoot; } }
        public Vector3 PlanP0 { get { return _p0; } }
        public Vector3 PlanV0 { get { return _v0; } }
        public float PlanCurve { get { return _curve; } }
        public int Variant { get { return variant; } }
        public int PendingVariant { get { return pendingVariant; } }
        // Sideways lift for the next throw. Wave 2: KH_Frisbee._Launch reads it and sends it with ReqCatch, so a
        // flight already in the air keeps the curve it was launched with.
        public float FlightCurve { get { return variant == VariantCurve ? CurveDefault : 0f; } }
        public bool IsIdle { get { return state == StateIdle; } }
        // DES-DOG-005 D-4 (M3): 12 / 13 carry the disc too (the mouth glue, the busy refusals, KH_Frisbee.CheckRiver's hands-off).
        public bool IsCarrying { get { return state == StateJumpCarry || state == StateLandCarry || state == StateReturn || state == StateDrop || state == StateWaterCarry || state == StateWaterExit; } }
        // The water states 11-13: the Wander's legs move the dog (its busy branch keeps _speed, its push-apart uses the river rules).
        public bool IsWaterBusy { get { return state >= StateWaterChase && state <= StateWaterExit; } }
        public bool IsAirborne { get { return state == StateJump || state == StateJumpCarry || state == StateLand || state == StateLandCarry; } }
        // Busy for the other systems: Ready (standing off while someone holds the disc) is NOT engaged, so a
        // held disc never takes the dog away from Fetch, the leash or a gathering (review M1).
        public bool IsEngaged { get { return state != StateIdle && state != StateReady; } }
        // Where the head may be borrowed by the petting controller (IMP-DOG-013). NOT the inverse of IsEngaged:
        // Drop is engaged (the disc is in the mouth, the other systems must stay off) and yet it is exactly the
        // moment the user asked for - the dog has brought the disc back, is standing still in front of the thrower
        // and wants a よしよし. Everything that moves the dog stays suppressed, so no clip fights the head writes.
        // KD_DogFetchController.ApplyState reads this, so the two controllers resume on the same rule.
        public bool PetAllowed { get { return PetAllowedIn(state); } }
        // The same rule as a pure function of the state number, so Verify can prove it over every state without a
        // running dog (the LandingOf pattern).
        public static bool PetAllowedIn(int s) { return s == StateIdle || s == StateReady || s == StateDrop; }
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
            // 2026-09-26: a scene still carrying a retired preset is normalized by the Owner (both Owners, as SetVariant
            // requires); everyone else only maps it to Normal in ApplyVariant and never writes the synced number.
            if (IsRetiredVariant(variant) && IsDogOwner() && Networking.IsOwner(gameObject)) SetVariant(VariantNormal);
            ApplyState();
        }

        bool IsDogOwner() { return Networking.IsOwner(dogRoot.gameObject); }

        // ---- Prototype variant (IMP-DOG-008) ----
        // Local entry: KD_ActivitiesMenu (trainers) and the prototype Interact cube call this. Anyone may ask; the
        // Owner re-checks the trainer entitlement (BUG-DOG-009) and only the Owner writes. net: owner-writes-synced
        public void _RequestVariant(int v)
        {
            if (v < 0 || v >= VariantCount) return;
            if (IsRetiredVariant(v)) { Debug.Log("[KH_FrisbeeCatch] variant " + v + " retired"); return; }
            if (Networking.IsOwner(gameObject)) ReqVariant(v);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqVariant), v);
        }

        // net: network-callable; the sender must be a trainer (VariantCallerAllowed) and the arguments are checked. Engaged = the dog is mid-sequence, so the
        // change waits for Idle instead of moving the goalposts under a running plan.
        [NetworkCallable(maxEventsPerSecond: 5)]
        public void ReqVariant(int v)
        {
            // Both Owners, because the decision is the dog's and the synced numbers live on this object
            // (an ownership split would write `variant` on a client nobody reads it from; Codex D review).
            if (!IsDogOwner() || !Networking.IsOwner(gameObject)) return;
            if (!VariantCallerAllowed()) return;   // BUG-DOG-009: trainers only, judged here, not just by the menu
            if (v < 0 || v >= VariantCount) return;
            if (IsRetiredVariant(v)) { Debug.Log("[KH_FrisbeeCatch] variant " + v + " retired"); return; }   // 2026-09-26
            // DES-UI-002 (2026-10-03): in the world (activities wired) the curve is the thrower's own local choice, so the shared
            // preset never becomes 5 there - not even from a trainer's forged event (every throw would bend for everyone).
            // The prototype scene (activities == null) keeps 5 for its cube and the harness's curve group.
            if (v == VariantCurve && activities != null) { Debug.Log("[KH_FrisbeeCatch] variant request refused: 5 (curve) is the thrower's own choice in the world"); return; }
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

        // BUG-DOG-009: the catch style is a trainer perk, and ReqVariant is network-callable, so the receiver re-checks
        // it with gather's rule (KD_GroupActivities -> KH_TrainerPass._OwnedBy; a pass not yet loaded counts as "no").
        // No Owner / Master exemption, like gather's start. The sender is the authenticated CallingPlayer on a network
        // call and the local player on a direct call (the Owner's own press; _RequestVariant is called from UI /
        // Interact, never from inside a network handler, so InNetworkCall is not inherited - see ReqCatch).
        // activities == null only in the prototype scene (no trainer system; Walking/5 Verify requires it in the world).
        bool VariantCallerAllowed()
        {
            if (activities == null) return true;
            VRCPlayerApi caller = NetworkCalling.InNetworkCall ? NetworkCalling.CallingPlayer : Networking.LocalPlayer;
            if (!Utilities.IsValid(caller)) { Debug.Log("[KH_FrisbeeCatch] variant request refused: no valid sender"); return false; }
            if (activities._OwnsFormationFor(caller)) return true;
            Debug.Log("[KH_FrisbeeCatch] variant request refused: player " + caller.playerId + " is not a trainer (or the pass is not loaded)");
            return false;
        }

        // The only writer of `variant` (review: no double write). Ready re-takes its spot because the geometry moved.
        void SetVariant(int v)
        {
            if (!IsDogOwner() || !Networking.IsOwner(gameObject)) return; // net: owner-writes-synced (Codex D: both Owners)
            int was = variant;
            if (IsRetiredVariant(v)) v = VariantNormal;   // 2026-09-26: a queued / scene-stored retired number lands as Normal
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
            // 2026-09-26: v1 / v2 are retired - a synced retired number runs as Normal (no write here: non-owners never write).
            int wv = IsRetiredVariant(variant) ? VariantNormal : variant;
            _runIn = wv == VariantRunInSide || wv == VariantRunInFar || wv == VariantRunInBeside;
            _twist = wv == VariantTwist;   // ApplyState picks the Twist Bool and the twist facts from this
            // v3 waits ahead of the holder looking downfield, so the disc passes it from behind. IMP-DOG-017: the only
            // preset whose planner differs - B chase scores highest there (A around elsewhere); D' is everyone's fallback.
            _overtake = wv == VariantRunInBeside;
            // Ready target. v4 / v5 keep the Normal stand-off: only the pose / the flight differ there.
            _readyAhead = standoffDistance; _readyLateral = 0f; _readyFaceForward = false;
            if (wv == VariantRunInSide) { _readyAhead = ReadyAhead1; _readyLateral = ReadyLateral1; }
            else if (wv == VariantRunInFar) { _readyAhead = ReadyAhead2; _readyLateral = ReadyLateral2; }
            else if (wv == VariantRunInBeside) { _readyAhead = ReadyAhead3; _readyLateral = ReadyLateral3; _readyFaceForward = true; }
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
            // BUG-DOG-003: local = KH_Frisbee._Launch called straight into us on the thrower's own client; net = it
            // came over the wire, and only on that path is CallingPlayer defined (ClientSim sets it the same way).
            // InNetworkCall is INHERITED by a direct call made from inside a network event handler, so this would
            // read as net there; our direct callers (_Launch from OnDrop / a later frame, _Recall from Interact)
            // are not network handlers, so local is right for them.
            bool local = !NetworkCalling.InNetworkCall;
            // Age of the launch, read once: every reject line reports it, and the age gate below uses it. A forged
            // NaN timestamp simply prints as NaN here and is refused by the implausible / age gates.
            double age = Networking.GetServerTimeInSeconds() - launchServerTime;
            // Both Owners, like ReqVariant: the plan is the dog's, but the state this call writes is synced from
            // THIS object, so an ownership split would run the sequence on a client nobody reads it from (Codex D3).
            if (!IsDogOwner() || !Networking.IsOwner(gameObject)) { LogReject("not-owner", local, thrower, gen, age); return; }
            if (IsCarrying || IsAirborne) { LogReject("busy", local, thrower, gen, age); return; } // finish the mouth / the landing first; the launcher refuses these too
            // BUG-DOG-003 (Codex D3): the arguments are forgeable, so a network sender may only announce its own
            // throw. A direct call has no CallingPlayer, which is exactly the local path. An unauthenticated network
            // call (no valid CallingPlayer) is refused outright, not just a mismatched one.
            if (!local)
            {
                VRCPlayerApi caller = NetworkCalling.CallingPlayer;
                if (!Utilities.IsValid(caller) || caller.playerId != thrower) { LogReject("sender-mismatch", local, thrower, gen, age); return; }
            }
            // Plausibility (network callable; anyone may send). Non-finite first: NaN survives every comparison
            // below (NaN > x and NaN < x are both false), so a forged launch would sail through the caps and then
            // poison the prediction, the plan and the dog's transform for the whole flight (Codex D3).
            if (!Finite(p0) || !Finite(v0)) { LogReject("implausible-nonfinite", local, thrower, gen, age); return; }
            if (double.IsNaN(launchServerTime) || double.IsInfinity(launchServerTime)) { LogReject("implausible-time", local, thrower, gen, age); return; }
            if (!CanPlay(p0, thrower)) { LogReject(_canPlayWhy, local, thrower, gen, age); return; }  // same gate as Ready: leash / Fetch / gathering / treat / rest / water / playable ground (DES-DOG-005)
            // A real launch is capped by the disc and starts nearby.
            // BUG-DOG-007: each cap has its own reason, and the far one says where the dog was (the loop is 600 x 720 m, so a dog left
            // elsewhere on the course is legitimately far from a thrower; the 200 m cap itself is unchanged)
            if (v0.magnitude > frisbee.maxSpeed * 1.05f) { LogReject("implausible-speed", local, thrower, gen, age); return; }
            float launchDist = Vector3.Distance(p0, dogRoot.position);
            if (launchDist > 200f) { LogRejectFar(launchDist, p0, local, thrower, gen, age); return; }
            // Too old: the flight is over. In the future: a forged timestamp would make _ElapsedSince negative and
            // run the whole scan on a trajectory that has not started (1 s covers clock skew between clients,
            // which is all a real launch can be out by).
            if (age > 5.0 || age < -1.0) { LogReject("age", local, thrower, gen, age); return; }
            if (float.IsNaN(curve) || float.IsInfinity(curve)) { LogReject("implausible-curve", local, thrower, gen, age); return; }   // a forged value would poison every prediction (Codex D)
            if (curve < -1f || curve > 1f) { LogReject("implausible-curve", local, thrower, gen, age); return; } // a real throw carries FlightCurve (0 or CurveDefault); clamping would split flight and prediction
            // BUG-DOG-003: the notice beat the pickup sync. Park it instead of starting a chase the very next
            // frame would cancel (L708). No state is touched here: Ready stays Ready, an engaged sequence runs on.
            if (HeldNow())
            {
                if (_pendingLaunch) LogPending("cancel   ", "replaced");
                StorePending(gen, p0, v0, launchServerTime, thrower, curve, local, age);
                LogReq("hold     ", "", local, thrower, gen, age, 0.0, -1f, true, false, 0);
                return;
            }
            if (_pendingLaunch) { LogPending("cancel   ", "replaced"); ClearPending(); }   // Codex D1: a fresh accept always drops the old notice
            AcceptLaunch(gen, p0, v0, launchServerTime, thrower, curve, local, 0f, false, 0);
        }

        // Every component finite. Vector3.magnitude on a NaN component is NaN, which compares false against every
        // bound, so the caps in ReqCatch only mean something once this has passed (Codex D3).
        bool Finite(Vector3 v)
        {
            if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)) return false;
            return !(float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }

        // BUG-DOG-003. Every read of "the disc is in a hand" goes through these two, so the harness can drive the
        // arrival-order race deterministically without touching KH_Frisbee: KH_FrisbeeLauncher keeps reading the
        // real IsHeldNow and still refuses a held disc.
        bool HeldNow()
        {
            if (debugHeldOverride > 0) return true;
            if (debugHeldOverride < 0) return false;
            return _discPickup != null && _discPickup.IsHeld;
        }

        // KH_Frisbee.HolderPlayer returns null whenever the pickup is not really held, so a simulated hold needs a
        // stand-in holder: the disc Owner is the player VRChat would report for a real hand anyway.
        VRCPlayerApi HolderNow()
        {
            if (debugHeldOverride > 0)
            {
                VRCPlayerApi sim = Networking.GetOwner(frisbee.gameObject);
                if (!Utilities.IsValid(sim)) return null;
                return sim;
            }
            if (debugHeldOverride < 0) return null;
            return frisbee.HolderPlayer;
        }

        // BUG-DOG-003 (Codex M3). The one way a live launch ends: the reason is said exactly once (GiveUp alone is
        // silent, and TransitionTo(StateIdle) clears _haveLaunch, so the line cannot repeat on the next frame).
        void CancelLaunch(string reason)
        {
            if (_haveLaunch) LogLaunchCancel(reason);
            GiveUp();
        }

        // BUG-DOG-003. Park the launch facts; nothing about the dog's state changes (Ready stays Ready, an engaged
        // sequence keeps running) and _seenReleased is left alone - that flag belongs to the launch already running,
        // which must keep cancelling on a real re-grab while this notice waits.
        void StorePending(int gen, Vector3 p0, Vector3 v0, double launchServerTime, int thrower, float curve, bool local, double age)
        {
            _pendingLaunch = true; _pendingGen = gen; _pendingP0 = p0; _pendingV0 = v0; _pendingT0 = launchServerTime;
            _pendingThrower = thrower; _pendingCurve = curve; _pendingLocal = local;
            _pendingSince = Time.time; _pendingAgeAtReceipt = age;
            _pendingEnded = false; _pendingEndReason = 0; _pendingSeenThrower = false;
        }

        void ClearPending()
        {
            _pendingLaunch = false; _pendingGen = 0; _pendingThrower = -1; _pendingP0 = Vector3.zero; _pendingV0 = Vector3.zero;
            _pendingT0 = 0.0; _pendingCurve = 0f; _pendingSince = 0f; _pendingAgeAtReceipt = 0.0; _pendingLocal = false;
            _pendingEnded = false; _pendingEndReason = 0; _pendingSeenThrower = false;   // _seenReleased is the live launch's, never this notice's (BUG-DOG-003)
        }

        // Everything that invalidates a parked notice, checked every frame in every state. Expiry FIRST, the same
        // order KD_DogFetchController uses (L880-887): otherwise a stale notice is revived by a late release.
        void ExpirePending()
        {
            if (Time.time - _pendingSince > PendingHoldSeconds) { LogPending("expired  ", ""); ClearPending(); return; }
            // Both Owners, the same gate ReqCatch passed (Codex D3).
            if (!IsDogOwner() || !Networking.IsOwner(gameObject)) { LogPending("cancel   ", "owner-change"); ClearPending(); return; }
            if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(_pendingThrower))) { LogPending("cancel   ", "thrower-left"); ClearPending(); return; }
            // _pendingP0 is the launch point, so this is literally the test ReqCatch ran on arrival.
            if (!CanPlay(_pendingP0, _pendingThrower)) { LogPending("cancel   ", _canPlayWhy); ClearPending(); return; }
            // BUG-DOG-003 (Codex D1, 2nd review). Two different situations read the same way here:
            //   (a) somebody else really has the disc now. That notice must die, because a foreign grab sends no end
            //       notice of its own (KH_Frisbee.OnPickup only notifies while ITS client had the disc flying), so
            //       the grabber's weak drop would otherwise look like the release this notice is waiting for.
            //   (b) the ownership transfer for THIS throw simply has not reached us yet, so the disc still reads as
            //       the PREVIOUS holder's. Killing the notice here would be the original bug in a new shape.
            // They are told apart by whether the thrower was ever seen on the disc while this notice waited. Note
            // that HolderPlayer returns the disc Owner itself, so holder and Owner are not independent evidence -
            // they are the same sync arriving, which is why a single sighting is enough to switch (b) off.
            // Until that sighting a mismatch means "still syncing" and we just wait; PendingHoldSeconds bounds it.
            VRCPlayerApi holderNow = HolderNow();
            VRCPlayerApi discOwnerNow = Networking.GetOwner(frisbee.gameObject);
            bool holderIsThrower = Utilities.IsValid(holderNow) && holderNow.playerId == _pendingThrower;
            bool ownerIsThrower = Utilities.IsValid(discOwnerNow) && discOwnerNow.playerId == _pendingThrower;
            if (holderIsThrower || ownerIsThrower) _pendingSeenThrower = true;
            bool takenByOther = (Utilities.IsValid(holderNow) && !holderIsThrower) || (Utilities.IsValid(discOwnerNow) && !ownerIsThrower);
            if (_pendingSeenThrower && takenByOther) { LogPending("cancel   ", "taken-by-other"); ClearPending(); return; }
            if (_pendingEnded && !RetrievableReason(_pendingEndReason)) { LogPending("cancel   ", "flight-ended:" + _pendingEndReason); ClearPending(); return; }
        }

        // Called straight after ExpirePending and only while the dog is free. True = the notice became a throw.
        bool TryResolvePending()
        {
            if (HeldNow()) return false;   // still in the hand as far as this client can see
            float heldFor = Time.time - _pendingSince;
            if (heldFor <= 0f) heldFor = 0.001f;   // resolving on the receipt frame is still a resolve, not a plain accept
            int gen = _pendingGen, thrower = _pendingThrower, endReason = _pendingEndReason;
            Vector3 p0 = _pendingP0, v0 = _pendingV0;
            double t0 = _pendingT0;                // the ORIGINAL launch time; never re-stamped to the receipt
            float curve = _pendingCurve;
            bool local = _pendingLocal, ended = _pendingEnded;
            LogPending("resolved ", "");
            ClearPending();
            AcceptLaunch(gen, p0, v0, t0, thrower, curve, local, heldFor, ended, endReason);
            return true;
        }

        // The launch facts become this client's current sequence. heldFor > 0 means it waited as a parked notice.
        void AcceptLaunch(int gen, Vector3 p0, Vector3 v0, double launchServerTime, int thrower, float curve, bool local, float heldFor, bool ended, int endReason)
        {
            _haveLaunch = true; _gen = gen; _p0 = p0; _v0 = v0; _t0 = launchServerTime; _curve = curve;
            _fxOn = false;   // M3: a new throw starts with no fixed take-off
            // BUG-DOG-003 (Codex D2): an end notice that arrived while the notice was parked is not forgotten here.
            _flightEnded = ended; _flightEndReason = endReason;
            _plan = PlanNone; _nextPlan = 0f; _deviationSince = -1f; _landingValid = false; _localPlanValid = true;
            _landingChecked = false; _landingReachable = false; _pathFailValid = false; _groundStop = false;   // DES-DOG-005
            _settledSince = -1f; _holding = false; _runningCatch = false; _faceErrAtTakeoff = -1f;
            _mode = ModeAround; _viaActive = false; _viaDone = false; _motionLatched = false;
            _solved = false; _bannedModes = 0; _runThrough = false; _runThroughClear = false; _dropReason = ""; _mRanSince = -1f; _mRanBucketEnd = -1f;   // IMP-DOG-017: a new throw is solved from scratch
            _takeoffMin = RunJumpMinTakeoff; _takeoffCap = RunJumpMaxTakeoff;
            _mCount = 0; _mIdx = 0; _mPrimed = false; _mAccAngle = 0f; _mAccTime = 0f; _mSlowest = -1f; _mPrevDt = 0.016f;   // the last second of motion belongs to THIS throw only
            _yawRateMax = 0f; _speedMin = 0f; _vTake = 0f;
            throwerId = thrower;
            _launchLocal = local;
            _seenReleased = true;   // BUG-DOG-003: the disc was out of the hand at this instant; a hand AFTER this is a new grab
            if (heldFor <= 0f) LogReq("accept   ", "", local, thrower, gen, Networking.GetServerTimeInSeconds() - launchServerTime, 0.0, -1f, false, ended, endReason);
            LogThrow(p0, v0, curve);
            TransitionTo(StateChase);
            // BUG-DOG-003: a notice that already waited must not also sit out the 0.1 s notice pause - L913 measures
            // elapsed from _stateEntered, which TransitionTo just stamped with now.
            if (heldFor > 0f) _stateEntered = _stateEntered - noticeDelay;
        }

        [NetworkCallable(maxEventsPerSecond: 5)]
        public void ReqFlightEnded(int gen, int reason, Vector3 pos, int sender, double opTime)
        {
            if (!IsDogOwner()) return;
            // BUG-DOG-003 (Codex D3): authenticate the sender on the network path, exactly as ReqCatch does. A direct
            // call has no CallingPlayer; InNetworkCall is inherited by a direct call made from inside a network event
            // handler, but KH_Frisbee's direct callers (NotifyEnd from EndFlight / OnPickup / _Recall) are not.
            // Dropped here on purpose without a line: a forger could otherwise spam the log through this path.
            if (NetworkCalling.InNetworkCall)
            {
                VRCPlayerApi caller = NetworkCalling.CallingPlayer;
                if (!Utilities.IsValid(caller) || caller.playerId != sender) return;
            }
            // Matching rules, identical for the parked notice and the live launch (Codex D4/D5):
            //   that launch's own thrower - the generation MUST match, whatever the reason (the counter lives on
            //     the thrower's client, so it only means anything coming from them);
            //   anyone else - EndGrabbed only, on the authenticated sender alone. Owning the disc is deliberately
            //     NOT required: a grab or a recall reaches the dog Owner before the ownership change does, and
            //     demanding the ownership would reject it forever. Instead the notice carries the server time it
            //     was raised at, and a foreign notice older than the launch it would end is ignored (Codex D4,
            //     2nd review). 1.0 s is the clock-skew tolerance ReqCatch already uses.
            // Wave 0 #7: the parked notice is matched FIRST, because it has no _haveLaunch for the live branch. The
            // call then falls through to the live launch: a same-thrower notice can only match one of the two
            // (sender, gen) pairs, but a FOREIGN EndGrabbed is a hand on the disc and legitimately ends both.
            bool opFinite = !(double.IsNaN(opTime) || double.IsInfinity(opTime));
            bool hitPending = false, hitLive = false;
            string drop = "dropped-none";
            if (_pendingLaunch)
            {
                if (sender == _pendingThrower)
                {
                    if (gen == _pendingGen) { _pendingEnded = true; _pendingEndReason = reason; hitPending = true; }   // resolved or cancelled by ExpirePending / TryResolvePending
                    else drop = "dropped-gen";
                }
                else if (reason != KH_Frisbee.EndGrabbed) drop = "dropped-reason";
                else if (!opFinite || opTime < _pendingT0 - 1.0) drop = "dropped-stale";
                else { LogPending("cancel   ", "flight-ended:" + reason); ClearPending(); hitPending = true; }   // a hand or a recall by somebody else
            }
            if (_haveLaunch)
            {
                if (sender == throwerId)
                {
                    if (gen == _gen) { _flightEnded = true; _flightEndReason = reason; hitLive = true; }
                    else if (!hitPending) drop = "dropped-gen";
                }
                else if (reason != KH_Frisbee.EndGrabbed) { if (!hitPending) drop = "dropped-reason"; }
                else if (!opFinite || opTime < _t0 - 1.0) { if (!hitPending) drop = "dropped-stale"; }
                else { _flightEnded = true; _flightEndReason = reason; hitLive = true; }
            }
            string target = drop;
            if (hitPending && hitLive) target = "both";
            else if (hitPending) target = "pending";
            else if (hitLive) target = "live";
            LogEnd(reason, sender, gen, opTime, target);
        }

        // ---- Owner-side transitions ----
        void TransitionTo(int next)
        {
            state = next;
            _stateEntered = Networking.GetServerTimeInSeconds();
            _released = false;
            _riverWaitSince = -1f;   // DES-DOG-005 M3: the band wait belongs to one Chase
            // M3 (Codex M3b review 3): the fixed take-off lives only through the approach and the jump that uses it
            if (next != StateChase && next != StateJump && next != StateJumpCarry) _fxOn = false;
            if (next == StateIdle) { _discConfirmed = false; _wfClaimAt = -1f; _wfHoldLostAt = -1f; }   // M3: the hold watch belongs to one carry
            if (next == StateIdle) { throwerId = -1; animSpeed = 1f; _plan = PlanNone; _haveLaunch = false; _localPlanValid = false; _yieldAfterLanding = false; _speed = 0f; _settledSince = -1f; _holding = false; _runningCatch = false; _seenReleased = false; }   // BUG-DOG-003: a parked notice is NOT dropped here (the next frame resolves it); only the release flag is
            if (next == StateJumpCarry || next == StateReturn) ClaimDisc();
            // IMP-DOG-013. Ready: start the よしよし window (one per entry, logged once). Drop: the petting
            // credit against the drop timer belongs to THIS wait only.
            if (next == StateReady) { _heldSince = Time.time; _graceLogged = false; _graceEnded = false; }
            if (next == StateDrop) { _dropPetHold = 0f; _dropPetLogged = false; }
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
            AskDisc("claim");
            frisbee._Caught();
        }

        void ReleaseDisc(Vector3 velocity)
        {
            // BUG-DOG-006: a hand has it - there is nothing in the mouth to let go, and asking for it would be a steal
            if (HeldNow()) { Debug.Log("[KH_FrisbeeCatch] release skipped: held state=" + state + DogTag()); return; }
            AskDisc("release");
            frisbee._Release(velocity);
        }

        // BUG-DOG-006: the one place the dog asks for its disc. Never while a hand holds it (VRChat refuses that and logs
        // "Attempted to steal ownership of a held pickup"); the callers are one-shot (claim / release) or already once a second
        // (RequestDisc), so neither the skip nor the ask can repeat per frame. Each ask is logged with the Owner it takes from, so a
        // real session can tell our asks from another client's.
        void AskDisc(string why)
        {
            GameObject go = frisbee.gameObject;
            if (Networking.IsOwner(go)) return;
            if (HeldNow()) { Debug.Log("[KH_FrisbeeCatch] setowner skipped: held why=" + why + " state=" + state + DogTag()); return; }
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            VRCPlayerApi prevOwner = Networking.GetOwner(go);
            Debug.Log("[KH_FrisbeeCatch] setowner disc why=" + why + " from=" + (Utilities.IsValid(prevOwner) ? prevOwner.playerId : -1) + " state=" + state + DogTag());
            Networking.SetOwner(lp, go); // net: setowner-ok: disc only, never the dog; never while held (BUG-DOG-006)
        }

        // Landed / stopped by a wall / lost track: worth walking to. Grabbed or out of the world: not (review D5).
        bool Retrievable() { return RetrievableReason(_flightEndReason); }
        // The same rule for an end notice that arrived while a launch notice was still parked (BUG-DOG-003).
        bool RetrievableReason(int reason) { return reason != KH_Frisbee.EndGrabbed && reason != KH_Frisbee.EndTimeout; }

        void GiveUp()
        {
            bool held = HeldNow();
            // DES-DOG-005 D-4 (M3): a disc whose ownership was lost (or never arrived) is left as it is (_keepDisc): taking it
            // back here would fight the hand / the client that has it. Its new Owner's KH_Frisbee.CheckRiver frees / floats it.
            // Codex M3b review 2: in the water states (12 / 13) any cancel - not only the hold watch's - leaves a disc that is not ours
            // alone (a thrower leaving or a gathering can cancel before WaterHoldTick has run this frame). The owner-change recovery
            // (the new dog Owner lets the mouth's disc go, design D-4) keeps its release.
            if (IsWaterBusy && !_ownerChangeGiveUp && !DiscOwned()) _keepDisc = true;
            if (IsCarrying && !held && !_keepDisc) ReleaseDisc(Vector3.zero);
            _keepDisc = false;
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
                if (state == StateJumpCarry && wander != null && wander.photoDirector != null) wander.photoDirector._OnFrisbeeCatch(dogIndex);   // IMP-WLD-005 photo hook: a jump catch (every client, from the synced state edge) -> the local photo director (null until Photo/1 wires it)
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
                // Petting: allowed while Idle / Ready / Drop, suppressed while the dog is moving (review M2 +
                // IMP-DOG-013; same hooks Fetch uses). The jump must never have a second writer on the head bone.
                if (pet != null) { if (!PetAllowed) pet._SuppressPetting(); else if (fetch == null || fetch.IsIdle) pet._ResumePetting(); }
                // DES-DOG-005 D-4 (M3): in 11-13 the Wander's legs write Walk / Run (every client); on the way in and out its cached
                // outputs are stale (ours are made stale by _outputValid below). The speed is observed there, on every client.
                bool water = IsWaterBusy, wasWater = _lastApplied >= StateWaterChase && _lastApplied <= StateWaterExit;
                if (water != wasWater)
                {
                    if (wander != null && state != StateIdle) wander._InvalidateLeashAnimation();   // Idle does it above
                    _obsValid = false;
                }
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
            // DES-DOG-005 M3: a floating disc the dog swims to (11) may be taken by hand, as on land (Chase / Retrieve); in the mouth (12 / 13) not
            if (_discPickup != null) _discPickup.pickupable = state == StateIdle || state == StateReady || state == StateDrop || state == StateChase || state == StateRetrieve || state == StateWaterChase;
            _lastApplied = state;
        }

        void Update()
        {
            _riverStop = false;   // Codex W6 D-中: 川の停止はそのフレームの移動で立ったものだけを見る (Ready の立ち位置移動で立ったものを次の Chase へ持ち越さない)
            _groundStop = false;  // DES-DOG-005: the same rule for the walking gate
            _dogOwner = IsDogOwner();
            // DES-DOG-005 D-4 (M3): in the water states the Wander's legs move the root, so the Owner observes its speed too
            // (Animator.speed is still ours: ApplyAnimatorSpeed, from this observed speed on every client).
            if (!_dogOwner || IsWaterBusy) ObserveSpeed();
            WarnIfOwnerSplit();
            // BUG-DOG-003. A parked launch notice is judged every frame in every state, and accepted only while the
            // dog is free. Expiry and acceptance are separate so a stale notice cannot be revived (Codex D1).
            if (_pendingLaunch) ExpirePending();
            // BUG-DOG-003 (Codex D2): a NEW throw that is already out of the hand beats a chase that is still on the
            // ground - otherwise the live chase holds the dog engaged until the notice expires and the second throw
            // is simply lost. Airborne or with the disc in the mouth we finish first, like every other interruption
            // here. Idle is reached this frame, so the next frame resolves the notice normally.
            if (_pendingLaunch && IsEngaged && !IsAirborne && !IsCarrying && _dogOwner && !HeldNow()
                && (_pendingThrower != throwerId || _pendingGen != _gen))
            { CancelLaunch("superseded"); return; }
            if (_pendingLaunch && !IsEngaged && TryResolvePending()) return;
            if (state == StateIdle)
            {
                // Idle: Wander owns the Animator and the root; we only watch for a held disc.
                VRCPlayerApi holder = HolderNow();
                // DES-DOG-005 C-3: a Ready that found no playable stand-off spot waits _readyRetryAt out (no per-frame flip).
                if (_dogOwner && standoffWhenHeld && holder != null && Time.time >= _readyRetryAt && CanPlay(holder.GetPosition(), holder.playerId)) { _standoffValid = false; _nextStandoff = 0f; TransitionTo(StateReady); }
                return;
            }
            if (state == StateReady)
            {
                // BUG-DOG-003: while a notice waits, stand and WATCH THE DISC. UpdateReady would read the holder,
                // find the hand already empty and drop to Idle (old L796) - the very loop this fixes. Codex U1: the
                // parked launch facts already predict where the disc is, so the dog follows it instead of staring at
                // the player who threw it; this is the same prediction the chase will use a moment later.
                if (_pendingLaunch)
                {
                    if (_dogOwner)
                    {
                        Coast();
                        float tPend = frisbee._ElapsedSince(_pendingT0);
                        if (tPend < 0f) tPend = 0f;
                        FaceToward(frisbee._PositionAt(_pendingP0, _pendingV0, _pendingCurve, tPend) - dogRoot.position);
                    }
                    UpdateLocomotionAnim(); return;
                }
                UpdateReady(); if (state == StateReady) UpdateLocomotionAnim(); return;
            }
            // Became Owner mid-sequence (previous Owner left): no local plan, so stop safely.
            if (_dogOwner && !_localPlanValid) { _ownerChangeGiveUp = true; CancelLaunch("owner-change"); _ownerChangeGiveUp = false; return; }   // Codex M3: every live cancel says why, once
            double now = Networking.GetServerTimeInSeconds();
            float elapsed = (float)(now - _stateEntered);

            // A hand wins over the mouth and over the chase (IsHeld is synced) - but an airborne dog lands first.
            // BUG-DOG-003: only a hand seen AFTER the launch RUNNING HERE was observed released counts as a re-grab.
            // A stale "held" from before that throw is what used to cancel every non-Owner throw one frame in. The
            // flag tracks the accepted launch only, so a notice parked meanwhile cannot switch this test off.
            if (_dogOwner && !IsAirborne && _seenReleased && HeldNow()) { CancelLaunch("held-regrab"); return; }
            // A leash or a gathering accepted meanwhile wins (review D2): on the ground stop now, in the air land first.
            if (_dogOwner && Claimed())
            {
                if (IsAirborne) _yieldAfterLanding = true; else { CancelLaunch("claimed"); return; }   // Codex M3
            }

            if (state == StateChase) UpdateChase(now, elapsed);
            else if (state == StateJump || state == StateJumpCarry) UpdateJump(now);
            else if (state == StateLand || state == StateLandCarry) UpdateLand(now, elapsed);
            else if (state == StateRetrieve) UpdateRetrieve(elapsed);
            else if (state == StatePickup) UpdatePickup(elapsed);
            else if (state == StateReturn || state == StateDrop) UpdateReturn(elapsed);
            else if (IsWaterBusy)
            {
                // DES-DOG-005 D-4 (M3): the Wander's legs write Walk / Run; ours are not written here (no UpdateLocomotionAnim)
                UpdateWater(elapsed);
                if (IsWaterBusy) { ApplyAnimatorSpeed(); _outputValid = true; }
                return;
            }
            // DES-DOG-005 C-2: the walking gate found no ground / a step the dog cannot take. A fetch (Chase / Retrieve /
            // Pickup) gives up; Return / Drop already handled it (UpdateReturn sets the disc down there); airborne states
            // only stop the slide.
            if (_groundStop)
            {
                _groundStop = false;
                if (_dogOwner && (state == StateChase || state == StateRetrieve || state == StatePickup))
                {
                    Debug.Log("[KH_FrisbeeCatch] ground stop state=" + state + " reason=ground" + DogTag());
                    CancelLaunch("ground");
                    return;
                }
            }
            // IMP-DOG-020 段階 7: 直線移動が川の帯の手前で止まった。追いかけ・回り込み・拾いは諦め、くわえて戻る途中ならその場 (岸) に置く
            if (_riverStop)
            {
                _riverStop = false;
                // DES-DOG-005 D-4 (M3) transition table, row 1: stopped at the band while the disc is still flying = wait for it
                // (at most RiverBlockWait): a disc that comes down floating starts the water chase from UpdateChase.
                if (_dogOwner && RiverBlockWait()) { FaceToward(frisbee.transform.position - dogRoot.position); Coast(); return; }
                if (_dogOwner && state != StateIdle && !IsAirborne)
                {
                    Debug.Log("[KH_FrisbeeCatch] river stop state=" + state + " reason=river-band");
                    CancelLaunch("river-band");
                    return;
                }
            }
            if (state != StateIdle) UpdateLocomotionAnim(); // a transition above may have handed the Animator back (review D2)
        }

        // Everything that must be true for the dog to stand off for a held disc or to accept a throw.
        // Null neighbours (prototype scene) mean "no such system" (review M1/M3: one gate for both entrances).
        // DES-DOG-005 C-2: `at` is where the requester stands (thrower's launch point / holder's position), `requester`
        // who asked (ReqCatch: the thrower; Idle -> Ready: the holder). _canPlayWhy says why for the reject / cancel line.
        // BUG-DOG-007: beyond readyMaxDistance (horizontal) of this dog. 0 = no limit. Also read by the aim (KH_Frisbee.UpdateAim).
        public bool _TooFar(Vector3 at)
        {
            if (readyMaxDistance <= 0f || dogRoot == null) return false;
            Vector3 d = at - dogRoot.position; d.y = 0f;
            return d.sqrMagnitude > readyMaxDistance * readyMaxDistance;
        }

        bool CanPlay(Vector3 at, int requester)
        {
            _canPlayWhy = "cannot-play";
            if (Claimed()) { _canPlayWhy = "claimed"; return false; }
            if (fetch != null && !fetch.IsIdle) return false;
            // DES-LIV-001: no new frisbee while a live plays (same rule as the ball's KD_DogFetchController.LiveWatching)
            if (fetch != null && fetch.liveSystem != null && fetch.liveSystem.IsPlaying) return false;
            if (wander != null && (wander.IsTreatBusy || wander.IsResting || wander.IsAsleep)) return false;
            if (activities != null && activities.Mode == 2) return false;
            // BUG-DOG-007: a dog left far away on the loop neither stands off for a holder nor takes a throw (it used to run
            // hundreds of metres in Ready and then refuse the throw as implausible). Before the water ask: a far dog is not asked out.
            if (_TooFar(at)) { _canPlayWhy = "far"; return false; }
            // DES-DOG-005 D-1 (M1): a dog in the water does not take the throw (it never jumps from the water, and before
            // this the water play ended as `preempted` with the dog dropped onto the riverbed, 0-5). It is asked out
            // instead, at most once a second, turning to whoever asked (the bank return of the disc is unchanged).
            if (fetch != null && fetch.WaterPose != 0)
            {
                _canPlayWhy = "in-water";
                if (Time.time >= _waterAskNext) { _waterAskNext = Time.time + WaterAskInterval; fetch._AskWaterExitFor(requester); }
                return false;
            }
            // IMP-DOG-027: a dog being washed (a stroke within holdSeconds, synced) neither stands off nor takes a throw. After the
            // water ask so a wet dog is still asked out as before; the reason goes to the reject / cancel line like the others.
            if (wander != null && wander.WashHeld) { _canPlayWhy = "washing"; return false; }
            // DES-DOG-005: the plaza circle is gone from the decision; the requester must stand on ground the dog could
            // play on (one ray, re-shot at most every PlayableCacheSeconds or after a 1 m move).
            if (!PlayableCached(at)) return false;
            return true;
        }

        bool PlayableCached(Vector3 at)
        {
            Vector3 moved = at - _playAt; moved.y = 0f;
            if (_playNext < 0f || Time.time >= _playNext || moved.sqrMagnitude > 1f)
            {
                _playAt = at; _playNext = Time.time + PlayableCacheSeconds;
                _playOk = _PlayableAt(at);
            }
            return _playOk;
        }

        // Leash attached or gathering participant: authorities that beat the frisbee once they are accepted.
        bool Claimed()
        {
            if (hillsideLeashes != null && dogIndex >= 0 && hillsideLeashes._IsLeashed(dogIndex)) return true;
            if (activities != null && activities.Mode == 1 && activities._IsParticipant(dogIndex)) return true;
            return false;
        }

        // Are hands on the dog right now (IMP-DOG-013)? Two sources, because the decision runs on the dog's Owner
        // and the petter is usually somebody else:
        //   pet.IsPetting  - the LOCAL player's own hands, with no round trip (and the only source in the
        //                    prototype scene, where fetch is null).
        //   fetch.PetActive - the synced pet state (IMP-NET-001): KD_DogFetchController's Owner authenticates the
        //                    petter by observing their hand and publishes it, so every client agrees.
        // Nothing here writes: a false negative only means the dog leaves on time, never that it jumps mid-stroke.
        bool BeingPetted()
        {
            if (pet != null && pet.IsPetting) return true;
            if (fetch != null && fetch.PetActive) return true;
            return false;
        }

        // Where a jump that leaves `liftoff` along `fwd` at `vTake` comes down when the landing ground is the take-off
        // ground (the symmetric arc, 2 * tUp). Static, so Verify can assert on the same expression (Codex D1).
        public static Vector3 LandingOf(Vector3 liftoff, Vector3 fwd, float vTake, float tUp)
        {
            return liftoff + fwd * (vTake * 2f * tUp);
        }

        // DES-DOG-005 B: the asymmetric arc. The root rises for tUp and falls for tDown (DescentTime), so the landing is
        // vTake * (tUp + tDown) along fwd. tDown == tUp gives LandingOf above.
        public static Vector3 LandingOf(Vector3 liftoff, Vector3 fwd, float vTake, float tUp, float tDown)
        {
            return liftoff + fwd * (vTake * (tUp + tDown));
        }

        // DES-DOG-005 B-2 / B-3: seconds from the apex down to ground landDelta above the take-off ground (negative =
        // lower), for a root that rises 0.5 g tUp^2. -1 = the apex does not clear the landing by JumpApexClearance.
        public static float DescentTime(float tUp, float g, float landDelta)
        {
            float apex = 0.5f * g * tUp * tUp;
            float fall = apex - landDelta;
            if (fall < JumpApexClearance || g <= 0f) return -1f;
            return Mathf.Sqrt(2f * fall / g);
        }

        // B-3: time from lift-off to landing for a vertical speed vy from ground groundY onto ground landY
        // (ta = (vy + sqrt(vy^2 - 2 g (landY - groundY))) / g). -1 = the apex does not clear the landing ground.
        public static float AirTime(float vy, float g, float landDelta)
        {
            if (g <= 0f || vy <= 0f) return -1f;
            float apex = vy * vy / (2f * g);
            if (apex - landDelta < JumpApexClearance) return -1f;
            return (vy + Mathf.Sqrt(vy * vy - 2f * g * landDelta)) / g;
        }

        // ground normal y -> within maxDeg of flat (slope = acos(normal.y)).
        public static bool SlopeOk(float normalY, float maxDeg) { return normalY >= Mathf.Cos(maxDeg * Mathf.Deg2Rad) - 1e-4f; }
        public static float SlopeDeg(float normalY) { return Mathf.Acos(Mathf.Clamp(normalY, -1f, 1f)) * Mathf.Rad2Deg; }

        // C-1 PlayableAt, the ground half: one ray from p + up * offset, dist long; hit and no steeper than maxSlopeDeg.
        // Static so Course/93 and the harness run the very same test in the Editor (the river half is the instance's).
        public static bool PlayableGround(Vector3 p, LayerMask mask, float offset, float dist, float maxSlopeDeg)
        {
            RaycastHit hit;
            if (!Physics.Raycast(p + Vector3.up * offset, Vector3.down, out hit, dist, mask, QueryTriggerInteraction.Ignore)) return false;
            return SlopeOk(hit.normal.y, maxSlopeDeg);
        }

        // C-1 PathOk, the ground half: walk from -> to (xz) in PathStep steps over the first PathMaxLength metres. Each
        // sample casts one ray from the previous ground + top down to the previous ground - maxDrop. No hit (a hole, a drop
        // past maxDrop, the 4 m cliff) or a hit more than maxUp above the previous ground (a step, the top of a box or a
        // cliff wall) refuses. When the whole way was walked, the end's own height (to.y, a ground / disc point) must also
        // agree with the walked ground (within maxUp above / maxDrop below): a target on top of a box is not reached by
        // walking round its foot. from.y must be ground. Returns the rays cast (>= 0) when the path is fine, -(rays + 1)
        // when it is refused (the caller counts real rays, C-2). Static so Course/93 and the harness run the same rule.
        public static int PathWalk(Vector3 from, Vector3 to, LayerMask mask, float maxUp, float maxDrop, float top)
        {
            Vector3 d = to - from; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.05f) return 0;
            Vector3 dir = d / len;
            bool whole = len <= PathMaxLength;
            if (!whole) len = PathMaxLength;
            int n = Mathf.CeilToInt(len / PathStep);
            float y = from.y;
            RaycastHit hit;
            for (int i = 1; i <= n; i++)
            {
                float s = i * PathStep; if (s > len) s = len;
                Vector3 p = from + dir * s;
                if (!Physics.Raycast(new Vector3(p.x, y + top, p.z), Vector3.down, out hit, top + maxDrop, mask, QueryTriggerInteraction.Ignore)) return -(i + 1);
                if (hit.point.y - y > maxUp) return -(i + 1);
                y = hit.point.y;
            }
            if (whole && (to.y - y > maxUp || y - to.y > maxDrop)) return -(n + 1);
            return n;
        }

        // PathWalk with the default ray top (groundRayDistance 4 + groundRayOffset 0.5), true = fine. Editor callers.
        public static bool PathGround(Vector3 from, Vector3 to, LayerMask mask, float maxUp, float maxDrop)
        {
            return PathWalk(from, to, mask, maxUp, maxDrop, PathRayTop) >= 0;
        }

        // Same contract as KD_DogFetchController (IMP-NET-005 invariant 8): dogRoot and this object share an Owner.
        void WarnIfOwnerSplit()
        {
            if (_ownerSplitWarned || _dogOwner == Networking.IsOwner(gameObject)) return;
            _ownerSplitWarned = true;
            Debug.LogError("[KH_FrisbeeCatch] Ownership split: dogRoot and " + gameObject.name + " have different Owners - throws are dropped until they converge.");
        }
    }
}
