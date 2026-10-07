using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    // IMP-DOG-020 step 3, ClientSim harness W0 / W4 / W6 (DES-DOG-004 B-3). Same run frame as the walk harness (journal,
    // watchdog, PlayerData backup/restore, runInBackground restore, no scene save, Play stop on failure). Dog 2 (Lab, the
    // widest wading band in stage 0). Every case starts the regular way: menu attach near the spot, then menu detach within
    // 12 m of the entry (the release rule). For every case after W0 the dog's per-stay "first play used" flag is cleared in
    // its heap first (standing in for a new stay), so the same release starts the play again.
    //   W0              release -> WaterPlay started (waterState >= 1) within 5 s and the first wet step (waterPose 1) within 6 s
    //                   (decision 2026-09-27: B-1's 5 s runs to wet feet; 1 s margin for the measured spread). No debug entry.
    //   W4-leash        wading, menu attach -> refused (walkers stays -1), exit starts <= 1.5 s, landed (waterPose 0) <= 10 s,
    //                   then a menu attach is accepted.
    //   W4-gather       wading, a gathering starts (written into KD_GroupActivities' synced heap, roster empty as the arbiter
    //                   would freeze it) -> exit <= 1.5 s, landed <= 10 s, the dog never joins.
    //   W4-toy          (IMP-DOG-020, replaced by IMP-DOG-021 below) wading, menu "call the ball" -> refused, exit, landed.
    //   W4-throw        wading, the dog's ball placed on the bank and "thrown" (_OnBallThrown) -> the throw is held (no Fetch
    //                   state while wet), exit <= 1.5 s, landed <= 10 s, the Fetch starts <= 10 s after landing, Pickup <= 25 s.
    //   W6-owner        wading, the Owner's local play wiped in the heap (a new Owner has no WaterPlay, C-11) -> exit <= 1.5 s,
    //                   landed <= 10 s.
    //   W6-exit-blocked wading, the saved exit moved into the deep channel (spot heap) and the stay ended -> recovery stage 2
    //                   (another exit) logged, landed <= 30 s.
    //   W6-all-blocked  every exit moved into the deep channel -> recovery stage 3 (teleport to the return point) logged,
    //                   landed <= 40 s.
    //   W4-call         last (a called dog stays out of the leash). Exit <= 1.5 s, landed <= 10 s, then the call is taken up
    //                   (_sleepCalled) <= 10 s after landing. wading, "call" through the dog's KD_SleepSlot._TryCall (the
    //                   same entry the menu uses without the pose chooser; the chooser's _Apply only re-poses a dog that is
    //                   already called, run 20260927-054541) -> the play is dropped
    //                   (waterState 0) and the dog is out (waterPose 0) <= 10 s. NOT_EXERCISED when the call is refused.
    //   W-reroll-clock  (破壊監査 2026-10-07 RC-15, 78 after W0-24m and alone in 78r) the only case whose re-entry roll clock is
    //                   NOT held at +600 s: at the placement _waterNextRoll is set to 0 (a fresh behaviour's value) with the first-play
    //                   flag cleared, so the first play of the stay starts as in W0. Once wet (1.5 s) the stay is shortened
    //                   (_waterUntil = 0, the W6 stand-in for the 20-40 s stay) and the play runs its own way out: stay-over exit ->
    //                   shake -> WaterEnd("landed"). The log line "water end dog=2 reason=" gives t_end (Time.time of that frame).
    //                   Pass: the clock before the end was in the past (< the action's game time: nothing held it), reason=landed,
    //                   t_end + 45 - 0.1 <= _waterNextRoll <= t_end + 45 + 0.001 read the next tick (WaterRollInterval 45 from the
    //                   end; the tight upper bound tells WaterEnd's write from a later roll's now + 45 and shows no +600 leaked in),
    //                   _waterFirstUsed still true, no new play (waterState 0) for
    //                   3 s of game time after the end, exit <= 1.5 s, landed <= 10 s, the per-frame rules. The 45 s are not waited.
    //                   After the case the clock is held at +600 s again (the other cases' precondition).
    // Actions start only once the dog is really in the water: waterPose 1 held for 1.5 s (run 20260927-053222 acted 2 s after
    // waterState 2, while the dog was still crossing the dry 2.4 m between the entry and the band, so every case saw a dry dog).
    // Every frame, for every case: the dog root inside the river band but outside the spot (formula band), feet deeper than
    // 0.45 M (+0.02 m), or a frame step > 0.2 m outside a stage-3 teleport = violation. Evidence: Logs/kh-course-walk/<run>/water.json
    // Swim mode (Course/79, IMP-DOG-020 step 4 = design stage 1, B-3 W1 / W2):
    //   W1-<breed> x 6 (Pome and Shiba first): released near the spot as usual; once wading, the dog is sent (heap target, the
    //                   W1 debug entry B-3 allows) to the deepest cell, then back to a shallow cell near the entry, then its stay is
    //                   ended so it walks out. Every frame, per dog (WhDogStat): the real mouth (KD_DogFetchController.mouthAnchor,
    //                   read after the Animator) - water >= 0.03 m (1 mm tolerance), feet (root) - riverbed >= -0.02 m while
    //                   wading, band outside the spot 0, step jumps 0 (except that dog's own stage-3 teleport), non-swimmers never
    //                   deeper than 0.45 M. Swimmers swim >= 1 time with <= 4 switches; Lab and Samoyed never swim. Landed on the
    //                   spot's bank <= 25 s after the stay ends. Recorded: swim seconds, longest continuous swim, returns to wading.
    //   W2           : all six released together (first-play flags cleared); every swimmer is sent to the deepest cell once when
    //                   wet (same debug entry). 45 s observed, then every stay is ended. Every dog must meet the W1 rules, all six
    //                   must get wet and land <= 30 s. Recorded: frame time avg / max, closest distance of two wet dogs, seconds
    //                   with >= 2 / all 6 dogs in the water, seconds with two wet dogs closer than 0.3 m.
    // Fetch mode (Course/88, IMP-DOG-020 step 6 = design stage 2, B-3 W3, D-1 S4 / S5b one-client stand-ins):
    //   W3-<breed>          the player stands on the spot's bank; the dog's ball is put on the water at the deepest cell (swimmers)
    //                       or a shallow cell (Lab / Samoyed "-shallow") and "thrown" (_OnBallThrown). Pass: Fetch goes 6 -> 7 -> 8
    //                       -> Return / Drop and ends with the ball within 2.5 m of the player (<= 90 s); every frame the dog's
    //                       real mouth - water >= 0.03 m, the ball's top stays above the water while carried (7 / 8), no band frame
    //                       outside the spot, no jump, feet not under the bed while wading.
    //   W3-<breed>-deep     Lab / Samoyed and a deep ball: the Fetch gives up (reason=deep-ball), the dog never swims or goes past
    //                       its wading limit, and the ball keeps floating.
    //   S4-sim              a floating ball's local float state is wiped (what a new ball Owner has): it keeps floating (does not
    //                       sink to the bed, is not sent to the bank) for 5 s.
    //   S5b-sim             dog 2 wading: menu leash and "call the ball" in the same tick -> both refused, the dog comes out; after
    //                       landing the ball call is accepted (ball within 3 m of the player).
    //   Not simulable in one client: S1 (a non-Owner's throw) and two people asking at once (first asker) -> PCVR / 2 people.
    //   Destructive audit 2026-09-28 (docs/_sessions/destructive-audit-2026-09-28/triage.md), one-client stand-ins:
    //   every carried case  the first frame of the hold (4 or 7) has the ball frozen (RC-17) and the first frame after the Drop's
    //                       release has gravity (RC-09, the ball was floated with gravity off).
    //   R09-regrab          Pome, no throw: a floating ball gets gravity on (what a pickup shorter than one 0.25 s look leaves) while the
    //                       Owner's float flag stays set -> gravity off again <= 0.6 s, still floating 3 s later.
    //   R09-owner-ABA       the same, let go 0.12 m above the water (A floated it, B picked it up and let go, A is the Owner again).
    //   R17-delay           Pome, deep ball, the ball's ownership reaches the dog 1.2 s late: the dog waits in 6, then 7 with the ball
    //                       frozen from its first frame (claim wait / ok logged, waited >= 1.1 s), and the W3 rules.
    //   R17-refuse          the ownership never comes: never 7, give-up reason=claim-timeout, the ball keeps floating, not frozen.
    //   R17-pickup          a person takes the ball while the dog waits (_debugBallHeld): give-up reason=held-while-claiming < 0.5 s,
    //                       never 7, the ball untouched (floats again once let go).
    //   R17-lost            the ownership is lost in 7: give-up reason=ball-lost <= 3 s, the ball let go and floating (gravity off).
    //   R17-land            Corgi, a ball on the bank, ownership 1.2 s late: Pickup waits, Return frozen from its first frame, the Drop's
    //                       release has gravity, the ball ends within 2.5 m of the player.
    // Water mode additions (78): W4-throw now runs on to the Drop's release (gravity on at once). W4-throw-regrab: Lab wading, its ball
    //   floating at 0.6 x its limit gets gravity on and is thrown at once -> gravity off again <= 0.6 s, direct water fetch 6 -> 7 -> ...
    //   -> Drop with gravity at the release. W4-leash-stale: the synced pose held at 0 while the dog is wet, menu attach -> refused (the
    //   depth under the root), exit <= 1.5 s, landed <= 10 s, attach after landing accepted. W4-leash-late: an accepted connection
    //   written into the arbiter while the dog is wet -> the dog Owner vetoes it (log), released <= 2.5 s, exit <= 1.5 s, landed
    //   <= 10 s, no leash move in the water (band / jump / depth rules), attach after landing accepted.
    // Water mode W0 (decision 2026-09-27, 2nd): no release step; a free dog placed within 25 m of the spot must start its play
    // within 5 s and be wet within 6 s. The other water-mode cases start the same way.
    // IMP-DOG-021 (DES-DOG-004 §E-4 / E-5, 2026-09-28):
    //   W4-toy (78, changed)  the dog's ball floating in the spot far from the wading dog, menu "call the ball" -> accepted (the ball is
    //                       in no mouth): the ball within 3 m of the player, the dog stays wet and keeps playing for 4 s, no Fetch.
    //   W4-toy-chase (78)   the same ball thrown, then called as soon as the dog is after it (6): accepted, give-up reason=target-lost,
    //                       never 7, exit <= 1.5 s and landed <= 10 s after the call, the ball by the player.
    //   S5b-sim (88, changed) the ball call in the water is accepted now (the leash is still refused and the dog comes out).
    //   W3-*-deep (88)      fetchWadeRatio (KH_RiverData heap) set to the spot's 0.45 for the case: the "shallows only" switch of
    //                       §E-3, so the deep-ball path keeps its test; the criteria are unchanged.
    //   88b (river-wide), also at the end of 88:
    //   W3-Lab-center       the Lab and the spot's deepest cell under the Fetch limit 0.85 M: the W3 rules (tooDeep at 0.85 M), no swim.
    //   RW-down-Pome / RW-down-Lab / RW-up-Pome / RW-up-Lab   the thrower on a usable bank's return point near z 150 / 400 (from the
    //                       baked slices), the ball floating in the river centre: river water fetch logged, 6 -> 7 -> 8 -> Return /
    //                       Drop, the ball within 2.5 m of the player, landed and exited on the thrower's side, no water-timeout, the
    //                       per-frame rules in the river-wide water (band frames only in a bridge zone count), the Lab never swims.
    //   RW-farside          the dog on the other bank: it goes in from its own bank (the shorter path), comes out on the thrower's.
    //   RW-bridge           the ball sunk 4 m from the z 190 bridge (its zone) and thrown: returned to the thrower's bank <= 11 s, the
    //                       dog never in the band (strict), no water fetch.
    //   RW-nobank           the same in a stretch without a usable bank within reach (a heap stand-in around z 150 when the data has
    //                       none: sliceOk cleared for the case, restored after).
    //   RW-drift-bridge     (Codex 2026-09-28) no throw: a ball floating 3 m above a bridge zone drifts down and stops 1 m before it
    //                       (the harness mirrors KH_RiverData._DriftStops on the heap to get the expected z, +-0.3 m), still for 5 s,
    //                       floating, never returned to the bank.
    //   RW-drift-nobank     the same before a stretch without a bank (heap stand-in: sliceOk cleared 70 m below z 420).
    //   RW-long             the thrower on the road (a route node, NavMesh path to the entry >= 15 m): the W3 rules, the state-6 limit
    //                       re-planned at the entry (log "river entered", limit >= the first one), the carry back over the NavMesh
    //                       (log "river carry").
    public static partial class KH_CourseWalkHarnessEditor
    {
        static int WhDog = 2;   // the dog of the current case (swim mode: per case)
        static readonly string[] WhBreeds = { "Corgi", "Pome", "Lab", "Dachs", "Shiba", "Samoyed" };
        [Serializable] public class WhCase
        {
            public string name, status = "PENDING", reason;
            public double released = -1, playStarted = -1, wading = -1, wet = -1, actionAt = -1, exitStarted = -1, landed = -1, done = -1;
            public int frames, bandOutsideSpotFrames, tooDeepFrames, bigSteps, stateChanges, recoverStage;
            public float maxDepth, maxStep, mouth;
            public bool fetchWhileWet; public double fetchAfterLanding = -1;
            public bool refusedLogged, refused, accepted, joined, ballMoved, pickedUp, calledObserved;
            public List<string> log = new List<string>();
            // W0 only: where wading began and the first target, then every 0.5 s: time, state, pose, speed, depth, position.
            public string wadingStart;
            public List<string> trace = new List<string>();
            // IMP-DOG-036 S3 (W0, W1-*): the dog's decisions / legs frames while its water play was thinned (_dbgWaterDecisions / _dbgWaterLegs)
            public int thinDecisions = -1, thinLegs = -1;
            // Swim mode (W1 / W2)
            public int dog = 2, w1Leg, swimEntries, swimToggles, mouthLowFrames, wetDogs;
            public bool swimmer, reachedDeep, landedOnSpotBank;
            public float minMouthAboveWater = 9f, minPairDistance = 99f;
            public double frameMsAvg, frameMsMax, exitAt = -1;
            public Vector3 deepTarget, shallowTarget;
            public double simultaneousWetSeconds, allSixWetSeconds, closeSeconds;
            public bool seen6, seen7, seen8, seenReturn, seenDrop, fetchEnded, ballNearPlayer, deepBallLogged, ballStillFloating, refusedToy;
            public int ballTopBelowFrames, maxFetchState;
            public bool teleportSplit;
            public float pathToEntry = -1f, wetLimit = -1f, ballEndToReturn = -1f, ballEndZ = -1f;
            public int carryMouthLowFrames;
            public float pickupDepth = -1f;   // depth under the dog when the Fetch went 6 -> 7 (it took the ball standing in water)
            public bool ballAtDownstreamEnd, gravityBack, farBankDrop;
            public float ballEndDistance = -1f;
            // 破壊監査 2026-09-28: RC-17 (ball ownership before the hold), RC-09 (gravity reconciled), RC-14 (leash vs. water)
            public int stage;
            public bool claimWaitLogged, claimOkLogged, claimTimeoutLogged, claimHeldLogged, ballLostLogged, vetoLogged, vetoReleasedLogged;
            public bool holdSeen, holdKinematic, releaseSeen, releaseGravity, seen3, seen4, seen5, injected, floatingBefore;
            public float claimWaitGame = -1f, holdGame = -1f, injectedGame = -1f, idleAfterInject = -1f, gravityOffAfter = -1f;
            public double pickupAt = -1, releasedAt = -1;
            // W0: the play's start in game time from the placement (run 20260927-212540: 1.0035 s in editor time, a long placement
            // frame counted in; the dog's own wait runs on Time.time)
            public float placedGame = -1f, playGame = -1f;
            // IMP-DOG-021: the toy called from the water (W4-toy / W4-toy-chase), the river-wide cases (RW-*, 88b)
            public double calledAt = -1, exitAfterCall = -1, landedAfterCall = -1, returnedAt = -1;
            public bool targetLostLogged, riverFetchLogged, riverExitLogged, waterTimeoutLogged, noBankLogged, standIn, ballReturned, ballOnPlayerSide, stayedWet = true, stayedPlaying = true;
            public int expectSide, entrySide, exitSide;
            public float siteZ = -1f, chaseLimit = -1f, carryLimit = -1f, ballToPlayerAfterCall = -1f;
            public float driftExpectZ = -1f, driftStartZ = -1f, limitFirst = -1f, limitAfterEntry = -1f, enteredAt = -1f, landPath = -1f;
            public bool riverCarryLogged, riverEnteredLogged, driftStill, returnLogged;
            [NonSerialized] public float driftLastZ, driftStillSince = -1f;
            public Vector3 ballAt;
            // 破壊監査 2026-10-07 RC-15 (W-reroll-clock): the re-entry roll clock (_waterNextRoll, Time.time) at the action, the game time
            // of the action and of the "water end" log line, its reason, the clock read the tick after the end, and what followed
            public float rerollBefore = -1f, rerollActGame = -1f, rerollEndGame = -1f, rerollAfter = -1f;
            public string rerollEndReason;
            public bool rerollRead, rerollFirstUsedAfter, rerollReentered;
            public List<WhDogStat> stats = new List<WhDogStat>();
        }
        [Serializable] public class WhDogStat
        {
            public int dog; public string breed;
            public bool swimmer, wet, landed, landedOnSpotBank, sentDeep, mouthBoneMissing;
            public int frames, bandOutsideSpotFrames, tooDeepFrames, bigSteps, mouthLowFrames, footLowFrames, swimEntries, swimToggles, backToShallow, recoverStage;
            public float maxStep, minMouthAboveWater = 9f, minMouthAboveWaterFromRoot = 9f, minFootAboveBed = 9f, swimSeconds, longestSwim, wetSeconds;
            public double landedAt = -1;
            [NonSerialized] public Vector3 prev; [NonSerialized] public int pose; [NonSerialized] public float swimRun, mouth, wetSince = -1, sincePose;
            [NonSerialized] public Transform mouthBone;
            [NonSerialized] public bool teleportPending;   // (kept for the log hook; the jump rule reads the dog's own teleport record)
            [NonSerialized] public int prevFrame = -1; [NonSerialized] public float prevTime;
            public string bigStepAt;
            public string bandAt;   // the first frame in the band outside the spot: positions, states, the dog's target
            public int wadeDeepFrames;   // on its feet (pose 1) and deeper than its limit (wading limit, or the swim entry for a swimmer) + 0.02   // the first counted big step: positions, states, nearest other dog   // a stage-3 teleport was logged; exempt only the next observation
            // IMP-DOG-021: river-wide cases. riverWide = the band outside the spot is allowed where the river-wide water is (outside
            // the bridge zones; the formula band), and the mouth / depth rules apply there as in the spot. wadeRatio = the non-
            // swimmer's limit for tooDeep / wadeDeep (the spot's 0.45, or the Fetch limit 0.85 in a Fetch that may use it).
            public bool riverWide; public float wadeRatio = -1f;
        }
        [Serializable] public class WhReport
        {
            public string runId, status = "RUNNING";
            public List<WhCase> cases = new List<WhCase>();
            public string limitations = "Single local ClientSim. Water mode: dog 2; the per-stay first-play flag is cleared in the heap before each case after W0; the gathering is written into the synced heap; the Owner change is a heap wipe of the local play; blocked exits are heap edits of the spot. Swim mode: W1 / W2 targets are written into the dog's heap (_waterTarget, the W1 debug entry of B-3); W2 holds the random play roll off during its setup. Not network, Owner handover, device or visual acceptance.";
        }
        static WhReport wh;
        static UdonBehaviour whFetch, whSpot, whActivities, whCallPose, whSlot, whRecall, whRiver;
        static bool[] whSliceOkBackup;     // RW-nobank stand-in: the river data's sliceOk before the heap edit (restored by WhClearDebug)
        static bool whWadeOverride;        // W3-*-deep: fetchWadeRatio held at the spot's 0.45 for the case (restored by WhClearDebug)
        static float whWadeBackup;
        static UdonBehaviour[] whFetches;
        static int whLastPose;
        static double whW2FrameSum; static int whW2Frames;
        static double whWetSince = -1, whCallApplyAt = -1, whNextTrace;
        static int whPhase, whStep, whLastState;
        static double whNext, whPhaseAt;
        static Vector3 whPrev, whBallAt;
        static Vector3[] whExitsBackup;
        static float[] whPolyX, whPolyZ;
        static bool whLogHooked;
        static WhCase WC => wh.cases[caseIndex];
        static int whPrevFrame = -1; static float whPrevTime;

        // 口の余裕 (実口 − 水面) の判定。WhObserve の mouthLowFrames と WhObserveDog の mouthLowFrames の 2 か所で使う。
        // 2026-09-28 ユーザー決定 (IMP-DOG-023 の 88b 調査): PCVR で決めた泳ぎの沈み (ポメ swimSink 0.035) を優先し、0.029 → 0.010 m に緩める。
        // 歩く一歩の沈み止め (SinkGuard: 口 − 水深 < 0.03 の一歩へ進まない) はコード側にそのまま残る。ここは「口が水面から 0.010 m 未満」の観測だけを数える。
        // 運搬中の carryMouthLow (口 − 水面 < ボール半径 + 0.01、設計 B-1) は別の基準で、この値を流用しない (Dachs の運搬基準は未決定)。
        const float MouthLowLimit = 0.010f;

        // Jump rule (decision 2026-09-27): a step between two observations counts when it is longer than 0.2 m AND faster than
        // WhMaxSpeed over the game time between them (Unity caps one frame's deltaTime at 0.333 s: the W3-Shiba 0.801 m and
        // W3-Corgi 0.639 m of run 20260927-102801 were a run at 2.4 m/s through one long editor frame, along the direction of
        // travel). A stage-3 recovery teleport between the observations (the dog's _waterRecoverFrame / From / To) splits the
        // step: the move before it and the move after it are judged on their own, the teleport itself is not a jump, every time.
        const float WhMaxSpeed = 3.5f;
        static float WhJump(int dog, Vector3 prev, Vector3 p, int prevFrame, float prevTime, out string how)
        {
            float gameDt = prevFrame < 0 ? 0f : Mathf.Max(0f, Time.time - prevTime);
            float limit = Mathf.Max(.2f, WhMaxSpeed * gameDt);
            int rf = Get<int>(wander[dog], "_waterRecoverFrame");
            if (prevFrame >= 0 && rf > prevFrame)
            {
                float a = Vector3.Distance(prev, Get<Vector3>(wander[dog], "_waterRecoverFrom")), b = Vector3.Distance(Get<Vector3>(wander[dog], "_waterRecoverTo"), p);
                how = "teleport frame=" + rf + " before=" + a.ToString("F3") + " after=" + b.ToString("F3") + " limit=" + limit.ToString("F3");
                return Mathf.Max(a, b) > limit ? Mathf.Max(a, b) : 0f;
            }
            float s = Vector3.Distance(prev, p);
            how = "dt=" + gameDt.ToString("F3") + " frames=" + (Time.frameCount - prevFrame) + " limit=" + limit.ToString("F3");
            return s > limit ? s : 0f;
        }

        [MenuItem("Tools/KominkaHillside/Course/79 - Walk Harness Water Swim W1 W2")]
        public static void WaterSwimHarness()
        {
            Begin(false);
            report.waterOnly = true; report.waterSwim = true; report.deadline = EditorApplication.timeSinceStartup + 1500; Save();
        }

        [MenuItem("Tools/KominkaHillside/Course/88 - Walk Harness Water Fetch W3")]
        public static void WaterFetchHarness() { WaterFetchSet(0, 2800); }

        // IMP-DOG-021 (82b precedent): the same run frame with part of the list. 88a = the IMP-DOG-020 cases (the 20 of 88 before
        // IMP-DOG-021), 88b = the IMP-DOG-021 cases (river-wide fetch, W3-Lab-center).
        [MenuItem("Tools/KominkaHillside/Course/88a - Walk Harness Water Fetch W3 (IMP-DOG-020 cases)")]
        public static void WaterFetchHarnessOld() { WaterFetchSet(1, 1500); }

        [MenuItem("Tools/KominkaHillside/Course/88b - Walk Harness Water Fetch river-wide (IMP-DOG-021 cases)")]
        public static void WaterFetchHarnessRiver() { WaterFetchSet(2, 1500); }

        static void WaterFetchSet(int set, double seconds)
        {
            Begin(false);
            report.waterOnly = true; report.waterFetch = true; report.waterFetchSet = set; report.deadline = EditorApplication.timeSinceStartup + seconds; Save();
        }

        [MenuItem("Tools/KominkaHillside/Course/78 - Walk Harness Water W0 W4 W6")]
        public static void WaterPlayHarness()
        {
            Begin(false);
            // EnterPlaymode takes effect after this frame, so the mode flag reaches the journal before the domain reload.
            report.waterOnly = true; report.deadline = EditorApplication.timeSinceStartup + 1060; Save();   // 15 cases (IMP-DOG-021: W4-toy-chase; audit 2026-10-07: W-reroll-clock, about 30 s)
        }

        // 破壊監査 2026-10-07 RC-15: the new case alone, in the same run frame (the 78 setup and judgement, nothing else).
        // Budget: Play entry + phase 0 (<= 45 s back on land) + wet (<= 30 s) + the case (cap 25 s).
        [MenuItem("Tools/KominkaHillside/Course/78r - Walk Harness Water W-reroll-clock (RC-15)")]
        public static void WaterRerollHarness()
        {
            Begin(false);
            report.waterOnly = true; report.waterPlaySet = 1; report.deadline = EditorApplication.timeSinceStartup + 300; Save();
        }

        static void MakeWaterCases()
        {
            var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>();
            whFetches = all.Where(u => u.programSource != null && u.programSource.name == "KD_DogFetchController").OrderBy(u => Get<int>(u, "dogIndex")).ToArray();
            if (whFetches.Length != 6) throw new InvalidOperationException("Six fetch behaviours required");
            WhDog = 2; whFetch = whFetches[WhDog];
            whSpot = all.Single(u => u.programSource != null && u.programSource.name == "KH_WaterSpot");
            whActivities = all.Single(u => u.programSource != null && u.programSource.name == "KD_GroupActivities");
            whCallPose = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KD_CallPose");
            whRecall = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_ToyRecall");
            whSlot = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KD_SleepSlot" && Get<int>(u, "dogIndex") == WhDog);
            whPolyX = Get<float[]>(whSpot, "polyX"); whPolyZ = Get<float[]>(whSpot, "polyZ");
            whExitsBackup = (Vector3[])Get<Vector3[]>(whSpot, "exits").Clone();
            wh = new WhReport { runId = report.runId };
            wh.limitations += " Jump rule (all modes, decision 2026-09-27): a step between two observations counts when it is longer than 0.2 m AND faster than 3.5 m/s over the game time between them; a stage-3 recovery teleport between them splits the step (before / after judged alone). Detection limit: Unity caps one frame's deltaTime at 0.333 s, so one long editor frame can hide a jump of up to 1.17 m; in a normal frame (16 ms) anything over 0.2 m counts (J-selftest injects 0.8 m). Fetch mode: S1 (a non-Owner's throw) and two people asking at once need two clients; S4 / S4b wipe the local float history in the heap instead of a real Owner change."
                + " Audit 2026-09-28 stand-ins (one client, never a network or ownership test): R17-* delay / refuse the ball ownership as the dog sees it (_debugClaimDelay / _debugClaimRefuse on the Fetch) and stand in for a person's pickup with _debugBallHeld (the Fetch's own held checks only); R09-regrab / R09-owner-ABA write what a pickup shorter than one 0.25 s look leaves (gravity on, the local float flag still set) instead of a real pickup or Owner change; W4-leash-stale holds the synced pose at 0 (_debugPoseHold) while the dog is wet; W4-leash-late writes an accepted connection into the arbiter's synced heap (standing in for any entry that got through, the hand clip included: a clip needs a held pickup, which ClientSim cannot make); W4-throw-regrab turns the floating ball's gravity on just before the throw. Two clients are required for S1 / S3 / S5a and for a real delayed / refused ownership transfer."
                + " Audit 2026-10-07 RC-15: W-reroll-clock is the only case with the re-entry roll clock not held at +600 s (0 at the placement); the stay is shortened (_waterUntil = 0) and the clock is read after the landing, the 45 s are not waited and the 30 % roll itself is not exercised; the Owner's local clock only (not synced, a new Owner starts at 0).";
            if (report.waterFetch)
            {
                whRiver = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_RiverData");
                var old = new[] { new[] { "W3-Pome", "1" }, new[] { "W3-Shiba", "4" }, new[] { "W3-Corgi", "0" }, new[] { "W3-Dachs", "3" },
                    new[] { "W3-Lab-shallow", "2" }, new[] { "W3-Lab-deep", "2" }, new[] { "W3-Samoyed-shallow", "5" }, new[] { "W3-Samoyed-deep", "5" },
                    new[] { "W3-Pome-farbank", "1" }, new[] { "S4-sim", "-1" }, new[] { "S4b-sim", "1" }, new[] { "S5b-sim", "2" },
                    // 破壊監査 2026-09-28 RC-09 / RC-17 (see the header of this file)
                    new[] { "R09-regrab", "1" }, new[] { "R09-owner-ABA", "1" }, new[] { "R17-delay", "1" }, new[] { "R17-refuse", "1" },
                    new[] { "R17-pickup", "1" }, new[] { "R17-lost", "1" }, new[] { "R17-land", "0" }, new[] { "J-selftest", "0" } };
                // IMP-DOG-021 (DES-DOG-004 §E-5, the header's "River-wide fetch")
                var added = new[] { new[] { "W3-Lab-center", "2" }, new[] { "RW-down-Pome", "1" }, new[] { "RW-down-Lab", "2" }, new[] { "RW-up-Pome", "1" },
                    new[] { "RW-up-Lab", "2" }, new[] { "RW-bridge", "1" }, new[] { "RW-nobank", "1" }, new[] { "RW-farside", "1" },
                    // Codex review 2026-09-28: the drift stop before a bridge zone / a stretch without a bank, and a long land approach
                    new[] { "RW-drift-bridge", "1" }, new[] { "RW-drift-nobank", "1" }, new[] { "RW-long", "1" } };
                if (added.Length > 0 && whRiver == null && report.waterFetchSet != 1) throw new InvalidOperationException("KH_RiverData missing (Course/73 / 90)");
                var list = report.waterFetchSet == 1 ? old : report.waterFetchSet == 2 ? added : old.Concat(added).ToArray();
                foreach (var pair in list)
                { report.cases.Add(new Case { name = pair[0], count = 1 }); wh.cases.Add(new WhCase { name = pair[0], dog = int.Parse(pair[1]) }); }
            }
            else if (report.waterSwim)
            {
                foreach (int dog in new[] { 1, 4, 0, 3, 2, 5 })   // Pome and Shiba first (design D-5)
                { string name = "W1-" + WhBreeds[dog]; report.cases.Add(new Case { name = name, count = 1 }); wh.cases.Add(new WhCase { name = name, dog = dog }); }
                report.cases.Add(new Case { name = "W2", count = 6 }); wh.cases.Add(new WhCase { name = "W2", dog = -1 });
            }
            else
                foreach (var name in report.waterPlaySet == 1 ? new[] { "W-reroll-clock" }
                    : new[] { "W0", "W0-24m", "W-reroll-clock", "W4-leash", "W4-gather", "W4-toy", "W4-toy-chase", "W4-throw", "W4-throw-regrab", "W4-leash-stale", "W4-leash-late",
                    "W6-owner", "W6-exit-blocked", "W6-all-blocked", "W4-call" })
                { report.cases.Add(new Case { name = name, count = 1 }); wh.cases.Add(new WhCase { name = name }); }
            whPhase = 0; whStep = 0; whNext = 0;
            if (!whLogHooked) { Application.logMessageReceived += WhLog; whLogHooked = true; }
        }

        static void WhLog(string condition, string stack, LogType type)
        {
            if (wh == null || caseIndex < 0 || caseIndex >= wh.cases.Count || !EditorApplication.isPlaying) return;
            if (condition.StartsWith("[KD_DogFetchController] give-up dog=" + WhDog + " ") || condition.StartsWith("[KD_DogFetchController] water exit dog=" + WhDog + " "))
            {
                if (condition.Contains("reason=deep-ball")) WC.deepBallLogged = true;
                if (condition.Contains("reason=claim-timeout")) WC.claimTimeoutLogged = true;
                if (condition.Contains("reason=held-while-claiming")) WC.claimHeldLogged = true;
                if (condition.Contains("reason=ball-lost")) WC.ballLostLogged = true;
                if (condition.Contains("reason=target-lost")) WC.targetLostLogged = true;       // IMP-DOG-021 E-4
                if (condition.Contains("reason=water-timeout")) WC.waterTimeoutLogged = true;   // IMP-DOG-021 time contract
                if (condition.Contains("reason=no-bank")) WC.noBankLogged = true;
                if (WC.log.Count < 40) WC.log.Add(condition); return;
            }
            // IMP-DOG-021: the river water fetch's plans (KD_DogFetchController.HillsideWater RiverPlanEntry / RiverPlanExit)
            if (condition.StartsWith("[KD_DogFetchController] river fetch dog=" + WhDog + " ")) { WC.riverFetchLogged = true; if (WC.log.Count < 40) WC.log.Add(condition); return; }
            if (condition.StartsWith("[KD_DogFetchController] river exit plan dog=" + WhDog + " ")) { WC.riverExitLogged = true; if (WC.log.Count < 40) WC.log.Add(condition); return; }
            if (condition.StartsWith("[KH_ToyRecall] river-return ball=" + WhDog + " ")) { WC.returnLogged = true; if (WC.log.Count < 40) WC.log.Add(condition); return; }
            if (condition.StartsWith("[KD_DogFetchController] river carry dog=" + WhDog + " ")) { WC.riverCarryLogged = true; if (WC.log.Count < 40) WC.log.Add(condition); return; }
            if (condition.StartsWith("[KD_DogFetchController] river entered dog=" + WhDog + " ")) { WC.riverEnteredLogged = true; if (WC.log.Count < 40) WC.log.Add(condition); return; }
            // RC-17: the ball ownership claim / hold watch (KD_DogFetchController.Owner.cs)
            if (condition.StartsWith("[KD_DogFetchController] ball ") && condition.Contains("dog=" + WhDog + " "))
            {
                if (condition.Contains("ball claim wait") && !WC.claimWaitLogged) { WC.claimWaitLogged = true; WC.claimWaitGame = Time.time; }
                if (condition.Contains("ball claim ok")) WC.claimOkLogged = true;
                if (WC.log.Count < 40) WC.log.Add(condition); return;
            }
            // RC-14: the dog Owner's leash veto (KD_DogWanderController.HillsideWater.cs WaterLeashTick)
            if (condition.StartsWith("[KD_DogWanderController] water leash veto dog=" + WhDog + " ")) WC.vetoLogged = true;
            if (condition == "[KD_DogWanderController] water leash released dog=" + WhDog) WC.vetoReleasedLogged = true;
            if (condition.StartsWith("[KD_DogWanderController] water step refused dog=" + WhDog + " ")) { if (WC.log.Count < 40) WC.log.Add(condition); return; }
            if (!condition.StartsWith("[KD_DogWanderController] water ")) return;
            if (WC.name != "W2" && !condition.Contains("dog=" + WhDog + " ") && !condition.EndsWith("dog=" + WhDog)) return;
            var c = WC; if (c.log.Count < 40) c.log.Add(condition);
            // RC-15 (W-reroll-clock): WaterEnd logs first and writes the clock right after, in the same frame, so Time.time here is its now
            if (c.name == "W-reroll-clock" && whPhase == 3 && c.rerollEndGame < 0f && condition.StartsWith("[KD_DogWanderController] water end dog=" + WhDog + " "))
            {
                c.rerollEndGame = Time.time; int at0 = condition.IndexOf("reason=");
                c.rerollEndReason = at0 >= 0 ? condition.Substring(at0 + 7) : "?";
            }
            if (condition.Contains("stage=2")) c.recoverStage = Math.Max(c.recoverStage, 2);
            if (condition.Contains("stage=3")) c.recoverStage = 3;
            int at = condition.IndexOf("dog="), n;
            if (at >= 0)
            {
                int end = condition.IndexOf(' ', at + 4); if (end < 0) end = condition.Length;
                if (int.TryParse(condition.Substring(at + 4, end - at - 4), out n))
                    foreach (var st in c.stats)
                        if (st.dog == n) { if (condition.Contains("stage=2")) st.recoverStage = Math.Max(st.recoverStage, 2); if (condition.Contains("stage=3")) { st.recoverStage = 3; st.teleportPending = true; } }
            }
        }

        static void WaterHarnessTick(double now)
        {
            try { WhStepTick(now); }
            catch { WhSave("FAIL"); throw; }
        }

        static int WhState() { return Get<int>(wander[WhDog], "waterState"); }
        static int WhPose() { return Get<int>(whFetch, "waterPose"); }

        static bool WhInSpot(Vector3 p)
        {
            bool inside = false;
            for (int i = 0, j = whPolyX.Length - 1; i < whPolyX.Length; j = i++)
                if ((whPolyZ[i] > p.z) != (whPolyZ[j] > p.z) && p.x < whPolyX[i] + (p.z - whPolyZ[i]) * (whPolyX[j] - whPolyX[i]) / (whPolyZ[j] - whPolyZ[i])) inside = !inside;
            return inside;
        }

        static void WhObserve(WhCase c)
        {
            var p = roots[WhDog].position; c.frames++;
            float step = Vector3.Distance(p, whPrev);
            string how; float jump = WhJump(WhDog, whPrev, p, whPrevFrame, whPrevTime, out how);
            whPrev = p; whPrevFrame = Time.frameCount; whPrevTime = Time.time;
            if (step > c.maxStep) c.maxStep = step;
            if (jump > 0f) { c.bigSteps++; if (c.log.Count < 40) c.log.Add("jump " + jump.ToString("F3") + " m at " + p.ToString("F2") + " " + how); }
            else if (how.StartsWith("teleport") && c.log.Count < 40) c.log.Add("recovery teleport split: " + how);
            if (RiverInBand(p) && !WhInSpot(p)) c.bandOutsideSpotFrames++;
            float surface = KH_CourseGroundEditor.RiverAt(p.z).y, depth = surface - p.y;
            int pose = WhPose();
            if (pose != whLastPose)
            {
                if (pose == 2) c.swimEntries++;
                if ((pose == 2 && whLastPose == 1) || (pose == 1 && whLastPose == 2)) c.swimToggles++;
                whLastPose = pose;
            }
            if (WhInSpot(p))
            {
                if (pose != 2)
                {
                    c.maxDepth = Mathf.Max(c.maxDepth, depth);
                    // IMP-DOG-021: while the Fetch is in the water (6 / 7 / 8) a non-swimmer may wade to fetchWadeRatio x M (0.85) while its Fetch is active (any state but Idle: chase, water 6-8, land Return/Drop right after landing); water play keeps 0.45
                    int fsNow = whFetch != null ? Get<int>(whFetch, "dogState") : 0;
                    float wadeNow = fsNow != 0 ? WhFetchWadeRatio() : .45f;
                    if (!c.swimmer && depth > wadeNow * c.mouth + .02f) { c.tooDeepFrames++; if (c.log.Count < 40 && (c.tooDeepFrames == 1 || c.tooDeepFrames % 100 == 0)) c.log.Add("tooDeep #" + c.tooDeepFrames + " frame=" + c.frames + " fetch=" + fsNow + " pose=" + pose + " depth=" + depth.ToString("F3") + " limit=" + wadeNow.ToString("F2") + " river=" + (whRiver != null)); }
                }
                if (pose != 0)
                {
                    float mouthAbove = p.y + c.mouth - surface;
                    if (mouthAbove < c.minMouthAboveWater) c.minMouthAboveWater = mouthAbove;
                    if (mouthAbove < MouthLowLimit) c.mouthLowFrames++;
                }
            }
            int s = WhState(); if (s != whLastState) { c.stateChanges++; whLastState = s; }
        }

        static void WhStepTick(double now)
        {
            var c = WC; var cc = Current;
            if (report.waterFetch && !c.name.StartsWith("S5b")) { WhFetchTick(c, now); return; }
            if (c.name == "W2") { WhW2Tick(c, now); return; }
            if (whPhase == 0 && whStep == 0) { WhDog = c.dog; whFetch = whFetches[WhDog]; }
            if (whPhase >= 2) { if (report.waterSwim && c.stats.Count > 0) WhObserveDog(c.stats[0], Time.unscaledDeltaTime); else WhObserve(c); }
            if (now < whNext) return;
            double t = now - whPhaseAt;
            var entry = Get<Vector3>(whSpot, "entry");
            switch (whPhase)
            {
                case 0: // detach everyone; wait for dog 2 out of the water, idle Fetch; place near the spot and attach
                    if (whStep < 6) { pouch.SendCustomEvent("_WalkOff" + whStep++); whNext = now + .3; return; }
                    if (!report.waterSwim) { wander[WhDog].SetProgramVariable("_waterNextRoll", Time.time + 600f); if (WhState() != 0) wander[WhDog].SetProgramVariable("_waterUntil", 0f); }
                    if (WhPose() != 0 || (!report.waterSwim && WhState() != 0) || Get<int>(whFetch, "dogState") != 0) { if (now - cc.startedAt > 45) throw new TimeoutException("Dog " + WhDog + " not back on land / idle"); whNext = now + .5; return; }
                    if (report.waterSwim) WhQuietOthers(WhDog);
                    c.mouth = Get<float>(wander[WhDog], "waterMouthHeight");
                    c.swimmer = Get<bool>(wander[WhDog], "waterSwimAllowed");
                    if (report.waterSwim && c.stats.Count == 0) c.stats.Add(WhNewStat(WhDog));
                    var nodePos = Get<Vector3[]>(route, "nodePositions");
                    var pass = nodePos[Get<int[]>(route, "destinationNodes")[5]];
                    var mid = Vector3.Lerp(pass, entry, .5f); NavMeshHit h;
                    if (!NavMesh.SamplePosition(mid, out h, 3, NavMesh.AllAreas)) throw new InvalidOperationException("No NavMesh between pass point and entry");
                    if (c.name == "W0-24m") h.position = WhPointAtRadius(entry, 24f);   // the trigger's edge (25 m) minus 1 m
                    var facing = Quaternion.LookRotation(new Vector3(entry.x - h.position.x, 0, entry.z - h.position.z));
                    Networking.LocalPlayer.TeleportTo(h.position, facing);
                    NavMeshHit d; if (!NavMesh.SamplePosition(h.position + facing * new Vector3(-.8f, 0, .3f), out d, 2, NavMesh.AllAreas)) throw new InvalidOperationException("Dog placement lacks NavMesh");
                    roots[WhDog].SetPositionAndRotation(d.position, facing);
                    var sync = roots[WhDog].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
                    whPrev = d.position; whPrevFrame = -1;
                    if (!report.waterSwim)
                    {
                        // Decision 2026-09-27 (2nd): no release step. A free dog placed within 25 m of the spot starts by itself;
                        // the first-play flag is cleared (a new stay) and everything is measured from the placement.
                        if (Flat(d.position - entry) > 25f) throw new InvalidOperationException("Dog not within 25 m of the entry");
                        c.pathToEntry = WhPathLength(d.position, entry);
                        wander[WhDog].SetProgramVariable("_waterFirstDue", 0f);
                        wander[WhDog].SetProgramVariable("_waterFirstUsed", false);
                        // RC-15: this case alone runs with the clock not held (0 = a fresh behaviour's value; the first play does not read it)
                        if (c.name == "W-reroll-clock") wander[WhDog].SetProgramVariable("_waterNextRoll", 0f);
                        c.released = now - cc.startedAt; whWetSince = -1; whNextTrace = now; whLastPose = 0; c.placedGame = Time.time;
                        if (c.name == "W0") WhThinReset(WhDog);   // IMP-DOG-036 S3
                        whPhase = 2; whPhaseAt = now; whLastState = WhState(); return;
                    }
                    pouch.SendCustomEvent("_WalkLeft" + WhDog); whPhase = 1; whNext = now + 1; return;
                case 1: // release within 12 m of the entry
                    if (Get<int[]>(manager, "walkers")[WhDog] != Networking.LocalPlayer.playerId) throw new InvalidOperationException("Menu attach rejected for dog " + WhDog);
                    if (Vector3.Distance(roots[WhDog].position, entry) > 12f) throw new InvalidOperationException("Dog not within 12 m of the entry at release");
                    if (c.name != "W0") wander[WhDog].SetProgramVariable("_waterFirstUsed", false);
                    pouch.SendCustomEvent("_WalkOff" + WhDog); c.released = now - cc.startedAt;
                    whPrev = roots[WhDog].position; whPrevFrame = -1; whWetSince = -1; whNextTrace = now; whLastPose = 0;
                    if (c.stats.Count > 0) { c.stats[0].prev = whPrev; c.stats[0].prevFrame = -1; }   // continuity starts at the release (the leashed walk before is not measured)
                    whPhase = 2; whPhaseAt = now; whLastState = WhState(); return;
                case 2: // wait for the play (W0 limits) and wading
                    if (c.playStarted < 0 && WhState() >= 1) { c.playStarted = t; if (c.placedGame >= 0f) c.playGame = Time.time - c.placedGame; }
                    if (c.wading < 0 && WhState() == 2)
                    {
                        c.wading = t; var at = roots[WhDog].position;
                        c.wadingStart = "t=" + t.ToString("F2") + " at=" + at.ToString("F2") + " depth=" + (KH_CourseGroundEditor.RiverAt(at.z).y - at.y).ToString("F3")
                            + " entry=" + entry.ToString("F2");
                    }
                    if (c.name.StartsWith("W0") && now >= whNextTrace && c.trace.Count < 80)
                    {
                        whNextTrace = now + .5; var q = roots[WhDog].position;
                        c.trace.Add("t=" + t.ToString("F2") + " state=" + WhState() + " pose=" + WhPose() + " speed=" + Get<float>(wander[WhDog], "_speed").ToString("F2")
                            + " depth=" + (KH_CourseGroundEditor.RiverAt(q.z).y - q.y).ToString("F3") + " at=" + q.ToString("F2")
                            + " target=" + Get<Vector3>(wander[WhDog], "_waterTarget").ToString("F2"));
                    }
                    bool wetNow = WhPose() >= 1 && WhState() == 2;
                    if (wetNow) { if (whWetSince < 0) whWetSince = now; if (c.wet < 0) c.wet = t; } else whWetSince = -1;
                    bool ready = whWetSince >= 0 && now - whWetSince >= 1.5;
                    if (c.name.StartsWith("W0"))
                    {
                        // Contract (Codex 2026-09-27 D-中): the play starts within the dog's first-play wait (0.2 + 0.3 x dogIndex s;
                        // dog 2: 0.8 s, judged <= 1 s); wet within start + NavMesh path to the entry / 2.4 m/s (run) + 2 s.
                        if (c.playStarted < 0 && t > 5) { WhFinish(c, now, false, "no WaterPlay within 5 s of the placement (no release)"); return; }
                        if (c.playStarted >= 0 && c.wetLimit < 0) c.wetLimit = (float)c.playStarted + c.pathToEntry / 2.4f + 2f;
                        // IMP-DOG-036 S3: W0 also requires the water play to be thinned (WhThinOk); W0-24m is unchanged
                        if (ready && t - c.wet > 3) { bool thin = c.name != "W0" || WhThinOk(c, WhDog); WhFinish(c, now, thin && WhClean(c) && c.maxDepth > .02f && c.playGame >= 0f && c.playGame <= 1.1f && c.wet <= c.wetLimit, "play " + c.playStarted.ToString("F2") + " s (game " + c.playGame.ToString("F3") + " s, limit 1.1: wait 0.2+0.3*dog + one 10 Hz selection tick + 0.1), wading " + c.wading.ToString("F2") + " s, wet " + c.wet.ToString("F2") + " s (limit " + c.wetLimit.ToString("F2") + ", path " + c.pathToEntry.ToString("F2") + " m)" + WhSummary(c) + (c.name == "W0" ? WhThinNote(c) : "")); return; }
                        if (t > 25) { WhFinish(c, now, false, "not wet (waterPose 1) within 25 s" + WhSummary(c)); return; }   // the 6 s limit is judged above
                        return;
                    }
                    if (ready) { WhAct(c, now); whPhase = 3; whPhaseAt = now; return; }
                    if (t > 30) throw new TimeoutException(c.name + ": not wet (waterPose 1) within 30 s");
                    return;
                case 3: WhEvaluate(c, now, t); return;
            }
        }

        static void WhAct(WhCase c, double now)
        {
            c.actionAt = now - Current.startedAt;
            if (c.name.StartsWith("W1-"))
            {
                c.deepTarget = WhCellBy(true); c.shallowTarget = WhCellBy(false);
                WhSetTarget(c.deepTarget); c.w1Leg = 0; c.done = 0;
                WhThinReset(WhDog);   // IMP-DOG-036 S3: counted from here to the landing (WhEvaluateW1)
                return;
            }
            switch (c.name)
            {
                case "W4-leash": pouch.SendCustomEvent("_WalkLeft" + WhDog); break;
                case "S5b-sim":   // one person stands in for both: leash and "call the ball" in the same tick
                    whBallAt = Get<Transform>(whFetch, "_ballT").position;
                    pouch.SendCustomEvent("_WalkLeft" + WhDog); pouch.SendCustomEvent("_Toy" + WhDog); break;
                case "W4-gather":
                    whActivities.SetProgramVariable("participants", 0);
                    whActivities.SetProgramVariable("formation", 0);
                    whActivities.SetProgramVariable("anchor", Networking.LocalPlayer.GetPosition());
                    whActivities.SetProgramVariable("yaw", 0f);
                    whActivities.SetProgramVariable("host", Networking.LocalPlayer.playerId);
                    whActivities.SetProgramVariable("startedMs", Networking.GetServerTimeInMilliseconds());
                    whActivities.SetProgramVariable("mode", 1);
                    whActivities.SetProgramVariable("revision", Get<int>(whActivities, "revision") + 1);
                    break;
                case "W4-toy":
                case "W4-toy-chase":
                {
                    // IMP-DOG-021 (E-4): the dog's ball floating in the spot, far from the wading dog (no drift for the case). W4-toy calls
                    // it while the dog only plays; W4-toy-chase throws it first and calls it while the dog is after it (6). WhEvaluate.
                    float limit = Get<float>(whSpot, "wadeLimitRatio") * c.mouth;
                    var cell = WhCellFar(roots[WhDog].position, .05f, .9f * limit);
                    if (c.log.Count < 40) c.log.Add("toy cell " + cell.ToString("F2") + " from dog " + Flat(cell - roots[WhDog].position).ToString("F2") + " m, limit " + limit.ToString("F3"));
                    whSpot.SetProgramVariable("floatDrift", 0f);
                    var ball = Get<Transform>(whFetch, "_ballT"); var bs = ball.GetComponent<VRC.SDK3.Components.VRCObjectSync>();
                    if (bs != null) bs.SetKinematic(false);
                    var rb = ball.GetComponent<Rigidbody>(); if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                    ball.position = new Vector3(cell.x, KH_CourseGroundEditor.RiverAt(cell.z).y + .2f, cell.z); if (bs != null) bs.FlagDiscontinuity();
                    c.stage = 0; break;
                }
                case "W4-throw":
                {
                    var entry = Get<Vector3>(whSpot, "entry"); NavMeshHit h;
                    if (!NavMesh.SamplePosition(entry + new Vector3(3, 0, 1), out h, 2, NavMesh.AllAreas)) throw new InvalidOperationException("No bank point for the throw");
                    var ball = Get<Transform>(whFetch, "_ballT"); var sync = ball.GetComponent<VRC.SDK3.Components.VRCObjectSync>();
                    if (sync != null) sync.SetKinematic(false);
                    ball.position = h.position + Vector3.up * .06f; if (sync != null) sync.FlagDiscontinuity();
                    whFetch.SendCustomEvent("_OnBallThrown");
                    break;
                }
                case "W4-throw-regrab":
                {
                    // RC-09 (audit 2026-09-28): the dog's ball floating in the spot at 0.6 x the Lab's wading limit (as W3-Lab-shallow, no
                    // drift); after 1.5 s (floating, gravity off) its gravity is turned on the way a pickup shorter than one 0.25 s look
                    // leaves it, and it is thrown at once (WhEvaluate). The floating ball must get its gravity off again at the next look.
                    // IMP-DOG-021 (run 20260928-010453): with the Fetch limit 0.85 M the Lab walks straight to a ball near it and took its
                    // ownership (kinematic) before KH_ToyRecall's next 0.25 s look, so "gravity off again" was never observable. The ball
                    // now goes to the shallow cell (<= 0.9 x the spot's wading limit) farthest from the wading dog, and WhEvaluate re-places
                    // it until the dog is >= WhRegrabClear away when the gravity is turned on (3 m at wading speed 1.1 m/s > 2.7 s).
                    whSpot.SetProgramVariable("floatDrift", 0f);
                    WhRegrabPlace(c);
                    c.stage = 0; c.exitAt = 0; c.w1Leg = 0; break;
                }
                case "W4-leash-stale":
                    // RC-14: the arbiter's view of the pose is older than the dog (pose held at 0 while the dog is wet); the menu attach
                    // must still be refused (the depth under the root) and the dog must walk out
                    whFetch.SetProgramVariable("waterPose", 0); whFetch.SetProgramVariable("_debugPoseHold", true);
                    pouch.SendCustomEvent("_WalkLeft" + WhDog); c.stage = 0; break;
                case "W4-leash-late":
                {
                    // RC-14: a connection that got through anyway (any entry, the hand clip included) reaches the wet dog: its Owner must
                    // not take it up, must have it released and keep walking out
                    var wk = (int[])Get<int[]>(manager, "walkers").Clone(); wk[WhDog] = Networking.LocalPlayer.playerId; manager.SetProgramVariable("walkers", wk);
                    var hs = (int[])Get<int[]>(manager, "hands").Clone(); hs[WhDog] = 0; manager.SetProgramVariable("hands", hs);
                    c.stage = 0; break;
                }
                case "W6-owner": wander[WhDog].SetProgramVariable("waterState", 0); break;
                case "W-reroll-clock":
                    // RC-15: the clock as the first play left it, then the stay shortened (the stay-over exit, shake and "landed" run as usual)
                    c.rerollBefore = Get<float>(wander[WhDog], "_waterNextRoll"); c.rerollActGame = Time.time;
                    wander[WhDog].SetProgramVariable("_waterUntil", 0f);
                    break;
                case "W6-exit-blocked":
                case "W6-all-blocked":
                {
                    var exits = (Vector3[])whExitsBackup.Clone(); var deep = WhDeepPoint();
                    if (c.name == "W6-all-blocked") for (int k = 0; k < exits.Length; k++) exits[k] = deep; else exits[0] = deep;
                    whSpot.SetProgramVariable("exits", exits);
                    wander[WhDog].SetProgramVariable("_waterExit", 0);
                    wander[WhDog].SetProgramVariable("_waterUntil", 0f);   // end the stay now: exit via the (blocked) saved exit
                    break;
                }
                case "W4-call":
                    // _Sleep{i} only opens the pose chooser (KD_CallPose._Open) and its _Apply only re-poses an already called dog.
                    // The call itself: KD_SleepSlot._TryCall with a base pose (reserve, then submit once the selection is ready).
                    if (whSlot == null) throw new InvalidOperationException("KD_SleepSlot of dog 2 missing");
                    whSlot.SetProgramVariable("requestedPose", 0);
                    whSlot.SendCustomEvent("_TryCall"); whCallApplyAt = -1; break;
            }
        }

        const float WhRegrabClear = 3f;

        /// <summary>W4-throw-regrab: the dog's ball floating (no drift) at the spot's shallow cell farthest from the dog.</summary>
        static void WhRegrabPlace(WhCase c)
        {
            float limit = Get<float>(whSpot, "wadeLimitRatio") * c.mouth;
            var cell = WhCellFar(roots[WhDog].position, .05f, .9f * limit);
            if (c.log.Count < 40) c.log.Add("regrab cell " + cell.ToString("F2") + " depth=" + (KH_CourseGroundEditor.RiverAt(cell.z).y - cell.y).ToString("F3") + " from dog " + Flat(cell - roots[WhDog].position).ToString("F2") + " m, limit=" + limit.ToString("F3"));
            var ball = Get<Transform>(whFetch, "_ballT"); var bs = ball.GetComponent<VRC.SDK3.Components.VRCObjectSync>();
            if (bs != null) bs.SetKinematic(false);
            var rb = ball.GetComponent<Rigidbody>(); if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            ball.position = new Vector3(cell.x, KH_CourseGroundEditor.RiverAt(cell.z).y + .2f, cell.z); if (bs != null) bs.FlagDiscontinuity();
        }

        /// <summary>A point in the deep channel beside the spot (unreachable in stage 0).</summary>
        static Vector3 WhDeepPoint()
        {
            var r = KH_CourseGroundEditor.RiverAt(285f); return new Vector3(r.x - 1f, r.y - .4f, 285f);
        }

        static void WhEvaluate(WhCase c, double now, double t)
        {
            if (c.name.StartsWith("W1-")) { WhEvaluateW1(c, now, t); return; }
            int s = WhState(), pose = WhPose();
            if (c.exitStarted < 0 && s == 3) c.exitStarted = t;
            if (c.landed < 0 && pose == 0) c.landed = t;
            switch (c.name)
            {
                case "W4-leash":
                    if (t < 1) { if (Get<int[]>(manager, "walkers")[WhDog] >= 0) c.accepted = true; return; }
                    c.refused = !c.accepted;
                    if (c.landed >= 0 && !c.accepted && c.done < 0 && t - c.landed > 1.5)
                    {
                        // Within attachReach (4 m): stand beside the landed dog, then attach the regular way.
                        c.done = t; NavMeshHit near; var dp = roots[WhDog].position;
                        if (NavMesh.SamplePosition(dp + new Vector3(1.2f, 0, 0), out near, 2, NavMesh.AllAreas)) Networking.LocalPlayer.TeleportTo(near.position, Networking.LocalPlayer.GetRotation());
                        whNext = now + .5; return;
                    }
                    if (c.done >= 0 && c.actionAt >= 0 && !c.refusedLogged) { c.refusedLogged = true; pouch.SendCustomEvent("_WalkLeft" + WhDog); return; }
                    if (c.done >= 0)
                    {
                        if (t - c.done < 1.5) return;
                        bool accepted = Get<int[]>(manager, "walkers")[WhDog] == Networking.LocalPlayer.playerId;
                        bool ok = c.refused && c.exitStarted >= 0 && c.exitStarted <= 1.5 && c.landed <= 10 && accepted && WhClean(c);
                        WhFinish(c, now, ok, "refused=" + c.refused + " exit=" + c.exitStarted.ToString("F2") + " landed=" + c.landed.ToString("F2") + " acceptedAfter=" + accepted + WhSummary(c)); return;
                    }
                    break;
                case "S5b-sim":
                {
                    var ballNow = Get<Transform>(whFetch, "_ballT").position;
                    if (c.landed < 0) { if (Get<int[]>(manager, "walkers")[WhDog] >= 0) c.accepted = true; if (Vector3.Distance(ballNow, whBallAt) > .5f) c.ballMoved = true; return; }
                    if (c.done < 0 && t - c.landed > 1.5) { c.done = t; pouch.SendCustomEvent("_Toy" + WhDog); return; }
                    if (c.done >= 0 && t - c.done > 3)
                    {
                        // IMP-DOG-021 (E-4, 2026-09-28): the ball is not in the dog's mouth, so calling it in the water is accepted now
                        // (it comes to the player while the dog is still wading); the leash stays refused and the dog comes out for it
                        float toPlayer = Vector3.Distance(ballNow, Networking.LocalPlayer.GetPosition());
                        bool ok = !c.accepted && c.ballMoved && c.exitStarted >= 0 && c.exitStarted <= 1.5 && c.landed <= 10 && toPlayer <= 3f && WhClean(c);
                        WhFinish(c, now, ok, "leashRefused=" + !c.accepted + " toyAcceptedInWater=" + c.ballMoved + " exit=" + c.exitStarted.ToString("F2") + " landed=" + c.landed.ToString("F2")
                            + " ballToPlayerAfter=" + toPlayer.ToString("F2") + WhSummary(c)); return;
                    }
                    break;
                }
                case "W4-gather":
                    if (Get<bool>(wander[WhDog], "_groupCalled")) c.joined = true;
                    if (c.landed >= 0 && t - c.landed > 2)
                    {
                        whActivities.SetProgramVariable("mode", 0); whActivities.SetProgramVariable("revision", Get<int>(whActivities, "revision") + 1);
                        bool ok = c.exitStarted >= 0 && c.exitStarted <= 1.5 && c.landed <= 10 && !c.joined && WhClean(c);
                        WhFinish(c, now, ok, "exit=" + c.exitStarted.ToString("F2") + " landed=" + c.landed.ToString("F2") + " joined=" + c.joined + WhSummary(c)); return;
                    }
                    break;
                case "W4-toy":
                case "W4-toy-chase":
                {
                    // IMP-DOG-021 (E-4). W4-toy: a floating ball the dog is not after is called: it comes to the player (<= 3 m), and
                    // the dog neither walks out nor stops playing for 4 s. W4-toy-chase: the ball is thrown, and called while the dog
                    // is after it (6, not in its mouth): accepted, the dog gives up (reason=target-lost) without ever holding it (no
                    // 7), walks out (exit <= 1.5 s after the call) and lands <= 10 s after it; the ball stays by the player.
                    bool chase = c.name == "W4-toy-chase";
                    var ballT = Get<Transform>(whFetch, "_ballT"); var rbT = ballT.GetComponent<Rigidbody>();
                    int fsT = Get<int>(whFetch, "dogState");
                    if (fsT == 6) c.seen6 = true;
                    if (fsT == 7) c.seen7 = true;
                    if (c.stage == 0)
                    {
                        if (t < 1.5) return;
                        c.floatingBefore = rbT != null && !rbT.useGravity && !rbT.isKinematic;
                        if (chase) { whFetch.SendCustomEvent("_OnBallThrown"); c.stage = 1; return; }
                        c.stage = 2; c.calledAt = t; whBallAt = ballT.position; pouch.SendCustomEvent("_Toy" + WhDog); return;
                    }
                    if (c.stage == 1)
                    {
                        // the call as soon as the dog is after the ball (the ball was put far from it, so it cannot have reached it yet)
                        if (fsT == 6) { c.stage = 2; c.calledAt = t; whBallAt = ballT.position; pouch.SendCustomEvent("_Toy" + WhDog); return; }
                        if (t > 12) { whSpot.SetProgramVariable("floatDrift", KH_WaterSpot.DefaultFloatDrift); WhFinish(c, now, false, "the throw did not start the water fetch (6) within 10 s" + WhSummary(c)); return; }
                        return;
                    }
                    double sinceCall = t - c.calledAt;
                    float toPlayer = Vector3.Distance(ballT.position, Networking.LocalPlayer.GetPosition());
                    if (Vector3.Distance(ballT.position, whBallAt) > .5f) c.ballMoved = true;
                    if (!chase)
                    {
                        if (pose == 0) c.stayedWet = false;
                        if (s != 2) c.stayedPlaying = false;
                        if (sinceCall < 4) return;
                        whSpot.SetProgramVariable("floatDrift", KH_WaterSpot.DefaultFloatDrift);
                        bool okT = c.floatingBefore && c.ballMoved && toPlayer <= 3f && c.stayedWet && c.stayedPlaying && fsT == 0 && WhClean(c);
                        WhFinish(c, now, okT, "floatingBefore=" + c.floatingBefore + " ballMoved=" + c.ballMoved + " ballToPlayer=" + toPlayer.ToString("F2") + " stayedWet=" + c.stayedWet
                            + " stayedPlaying=" + c.stayedPlaying + " fetch=" + fsT + WhSummary(c)); return;
                    }
                    if (c.exitAfterCall < 0 && s == 3) c.exitAfterCall = sinceCall;
                    if (c.landedAfterCall < 0 && pose == 0) c.landedAfterCall = sinceCall;
                    if (c.ballToPlayerAfterCall < 0 && sinceCall > 1) c.ballToPlayerAfterCall = toPlayer;
                    if ((c.landedAfterCall >= 0 && sinceCall - c.landedAfterCall > 1) || sinceCall > 15)
                    {
                        whSpot.SetProgramVariable("floatDrift", KH_WaterSpot.DefaultFloatDrift);
                        bool okC = c.floatingBefore && c.seen6 && !c.seen7 && c.targetLostLogged && c.ballMoved && c.ballToPlayerAfterCall >= 0 && c.ballToPlayerAfterCall <= 3f
                            && c.exitAfterCall >= 0 && c.exitAfterCall <= 1.5 && c.landedAfterCall >= 0 && c.landedAfterCall <= 10 && fsT == 0 && WhClean(c);
                        WhFinish(c, now, okC, "floatingBefore=" + c.floatingBefore + " seen6=" + c.seen6 + " seen7=" + c.seen7 + " targetLost=" + c.targetLostLogged + " ballMoved=" + c.ballMoved
                            + " ballToPlayer=" + c.ballToPlayerAfterCall.ToString("F2") + " exitAfterCall=" + c.exitAfterCall.ToString("F2") + " landedAfterCall=" + c.landedAfterCall.ToString("F2") + " fetch=" + fsT + WhSummary(c)); return;
                    }
                    return;
                }
                case "W4-throw":
                {
                    int fetchState = Get<int>(whFetch, "dogState");
                    if (fetchState != 0 && pose != 0) c.fetchWhileWet = true;
                    if (fetchState != 0 && c.landed >= 0 && c.fetchAfterLanding < 0) c.fetchAfterLanding = t - c.landed;
                    if (fetchState >= 3) c.pickedUp = true;
                    if (c.pickedUp && c.pickupAt < 0) c.pickupAt = t;
                    // RC-09 (audit 2026-09-28): watched on to the Drop; at the release the ball falls at once (gravity on)
                    WhReleaseWatch(c, fetchState);
                    if (fetchState == 5) c.seen5 = true;
                    if (c.pickedUp && ((c.seen5 && fetchState == 0) || t - c.pickupAt > 25))
                    {
                        bool ok = !c.fetchWhileWet && c.exitStarted >= 0 && c.exitStarted <= 1.5 && c.landed <= 10
                            && c.fetchAfterLanding >= 0 && c.fetchAfterLanding <= 10 && c.pickupAt <= 25 && c.releaseSeen && c.releaseGravity && WhClean(c);
                        WhFinish(c, now, ok, "exit=" + c.exitStarted.ToString("F2") + " landed=" + c.landed.ToString("F2") + " fetchAfterLanding=" + c.fetchAfterLanding.ToString("F2")
                            + " fetchWhileWet=" + c.fetchWhileWet + " pickup " + c.pickupAt.ToString("F2") + " s dropRelease=" + c.releaseSeen + " gravityAtRelease=" + c.releaseGravity + WhSummary(c)); return;
                    }
                    break;
                }
                case "W4-throw-regrab":
                {
                    var ball = Get<Transform>(whFetch, "_ballT"); var rb = ball.GetComponent<Rigidbody>(); var bs = ball.GetComponent<VRC.SDK3.Components.VRCObjectSync>();
                    int fs = Get<int>(whFetch, "dogState");
                    if (c.stage == 0)
                    {
                        if (t - c.exitAt < 1.5) return;
                        float away = Flat(ball.position - roots[WhDog].position);
                        if (away < WhRegrabClear && c.w1Leg < 3)
                        {
                            // the playing dog came near the ball: put the ball far from it again and wait for it to float (1.5 s)
                            c.w1Leg++; if (c.log.Count < 40) c.log.Add("regrab: dog " + away.ToString("F2") + " m from the ball, placed again (" + c.w1Leg + ")");
                            WhRegrabPlace(c); c.exitAt = t; return;
                        }
                        if (c.log.Count < 40) c.log.Add("regrab: dog " + away.ToString("F2") + " m from the ball at the gravity-on" + (away < WhRegrabClear ? " (below " + WhRegrabClear + " m after 3 tries)" : ""));
                        c.floatingBefore = rb != null && !rb.useGravity && !rb.isKinematic;
                        if (bs != null) bs.SetGravity(true);   // what a pickup shorter than one look leaves (KD_BallController.OnPickup)
                        whFetch.SendCustomEvent("_OnBallThrown");
                        c.stage = 1; c.done = t; return;
                    }
                    if (c.gravityOffAfter < 0 && rb != null && !rb.useGravity && !rb.isKinematic) c.gravityOffAfter = (float)(t - c.done);
                    if (fs == 6) c.seen6 = true;
                    if (fs == 7) c.seen7 = true;
                    if (fs == 5) c.seen5 = true;
                    WhReleaseWatch(c, fs);
                    if (c.seen5 && fs == 0) c.fetchEnded = true;
                    if (c.fetchEnded || t - c.done > 60)
                    {
                        whSpot.SetProgramVariable("floatDrift", KH_WaterSpot.DefaultFloatDrift);
                        bool ok = c.floatingBefore && c.gravityOffAfter >= 0 && c.gravityOffAfter <= .6f && c.seen6 && c.seen7 && c.fetchEnded
                            && c.releaseSeen && c.releaseGravity && WhClean(c);
                        WhFinish(c, now, ok, "floatingBefore=" + c.floatingBefore + " gravityOffAfter=" + c.gravityOffAfter.ToString("F2") + " s states 6/7/5=" + c.seen6 + "/" + c.seen7 + "/" + c.seen5
                            + " ended=" + c.fetchEnded + " dropRelease=" + c.releaseSeen + " gravityAtRelease=" + c.releaseGravity + WhSummary(c)); return;
                    }
                    return;   // its own 60 s cap (the Lab fetches from the water, then walks back)
                }
                case "W4-leash-stale":
                case "W4-leash-late":
                {
                    bool late = c.name == "W4-leash-late";
                    int holder = Get<int[]>(manager, "walkers")[WhDog];
                    if (c.stage == 0)
                    {
                        if (!late)
                        {
                            if (holder >= 0) c.accepted = true;
                            if (t < 1.0) return;
                            whFetch.SetProgramVariable("_debugPoseHold", false);   // the pose follows the dog again from here
                            c.refused = !c.accepted; c.stage = 1; return;
                        }
                        if (holder < 0) { c.releasedAt = t; c.stage = 1; return; }
                        if (t > 3) { WhFinish(c, now, false, "the connection made in the water was not released within 3 s (veto logged " + c.vetoLogged + ")" + WhSummary(c)); return; }
                        return;
                    }
                    if (c.stage == 1)
                    {
                        if (!late && t < 1.5) return;
                        if (!late) c.landed = -1;   // the held pose read 0 while wet: count landing only from the real pose
                        c.stage = 2; return;
                    }
                    if (c.stage == 2)
                    {
                        if (c.landed >= 0 && t - c.landed > 1.5)
                        {
                            // Within attachReach (4 m): stand beside the landed dog, then attach the regular way
                            NavMeshHit near; var dp = roots[WhDog].position;
                            if (NavMesh.SamplePosition(dp + new Vector3(1.2f, 0, 0), out near, 2, NavMesh.AllAreas)) Networking.LocalPlayer.TeleportTo(near.position, Networking.LocalPlayer.GetRotation());
                            c.stage = 3; whNext = now + .5; return;
                        }
                        break;
                    }
                    if (c.stage == 3) { pouch.SendCustomEvent("_WalkLeft" + WhDog); c.stage = 4; c.done = t; return; }
                    if (t - c.done < 1.5) return;
                    bool acceptedAfter = holder == Networking.LocalPlayer.playerId;
                    bool okL = c.exitStarted >= 0 && c.exitStarted <= 1.5 && c.landed >= 0 && c.landed <= 10 && acceptedAfter && WhClean(c)
                        // stale: refused by the arbiter itself, never connected-then-vetoed (Codex review 2026-09-28 中)
                        && (late ? c.vetoLogged && c.releasedAt >= 0 && c.releasedAt <= 2.5 : c.refused && !c.vetoLogged);
                    WhFinish(c, now, okL, (late ? "vetoLogged=" + c.vetoLogged + " released=" + c.releasedAt.ToString("F2") : "refusedWithStalePose=" + c.refused + " vetoLogged=" + c.vetoLogged)
                        + " exit=" + c.exitStarted.ToString("F2") + " landed=" + c.landed.ToString("F2") + " acceptedAfter=" + acceptedAfter + WhSummary(c)); return;
                }
                case "W-reroll-clock":
                {
                    if (c.rerollEndGame < 0f) break;   // until "water end" (the cap below fails the case)
                    if (!c.rerollRead)
                    {
                        // the tick after the log line: WaterEnd's write is done (nothing else writes the clock before it is due)
                        c.rerollRead = true; c.rerollAfter = Get<float>(wander[WhDog], "_waterNextRoll");
                        c.rerollFirstUsedAfter = Get<bool>(wander[WhDog], "_waterFirstUsed");
                        return;
                    }
                    if (s != 0) c.rerollReentered = true;   // a new play after the landing (the roll that RC-15 pushes 45 s out)
                    if (Time.time - c.rerollEndGame < 3f) return;
                    // Lower bound: the contract (45 s from the end, 0.1 s slack). Upper bound + 0.001: WaterEnd computes Time.time + 45 in the
                    // frame of the log line (the same float as here), while the old code's first roll after the landing writes its own
                    // later now + 45 (>= one frame later) - so a roll that slipped in before this read cannot pass, and no +600 leaked in.
                    float want = c.rerollEndGame + 45f;
                    bool clockOk = c.rerollAfter >= want - .1f && c.rerollAfter <= want + .001f;
                    bool ok = c.rerollBefore < c.rerollActGame && c.rerollEndReason == "landed" && clockOk && c.rerollFirstUsedAfter && !c.rerollReentered
                        && c.exitStarted >= 0 && c.exitStarted <= 1.5 && c.landed >= 0 && c.landed <= 10 && WhClean(c);
                    wander[WhDog].SetProgramVariable("_waterNextRoll", Time.time + 600f);   // the harness hold again (the other cases' precondition)
                    WhFinish(c, now, ok, "clockBefore=" + c.rerollBefore.ToString("F2") + " (action at game " + c.rerollActGame.ToString("F2") + ", must be earlier: not held)"
                        + " end=" + c.rerollEndGame.ToString("F2") + " reason=" + c.rerollEndReason + " clockAfter=" + c.rerollAfter.ToString("F2")
                        + " (want end+45 = " + want.ToString("F3") + ", -0.1 / +0.001, delta " + (c.rerollAfter - c.rerollEndGame).ToString("F4") + " s)"
                        + " firstUsedAfter=" + c.rerollFirstUsedAfter + " reentered3s=" + c.rerollReentered
                        + " exit=" + c.exitStarted.ToString("F2") + " landed=" + c.landed.ToString("F2") + WhSummary(c)); return;
                }
                case "W6-owner":
                    if (c.landed >= 0 && t - c.landed > 1)
                    {
                        bool ok = c.exitStarted >= 0 && c.exitStarted <= 1.5 && c.landed <= 10 && WhClean(c);
                        WhFinish(c, now, ok, "exit=" + c.exitStarted.ToString("F2") + " landed=" + c.landed.ToString("F2") + WhSummary(c)); return;
                    }
                    break;
                case "W6-exit-blocked":
                case "W6-all-blocked":
                    if (c.landed >= 0 && t - c.landed > 1)
                    {
                        whSpot.SetProgramVariable("exits", (Vector3[])whExitsBackup.Clone());
                        int want = c.name == "W6-all-blocked" ? 3 : 2; double limit = c.name == "W6-all-blocked" ? 40 : 30;
                        bool ok = c.recoverStage >= want && c.landed <= limit && WhClean(c);
                        WhFinish(c, now, ok, "recoverStage=" + c.recoverStage + " landed=" + c.landed.ToString("F2") + WhSummary(c)); return;
                    }
                    break;
                case "W4-call":
                    // In the water the call starts the exit (errand); the sleep branch takes it up once the dog has landed.
                    if (Get<bool>(wander[WhDog], "_sleepCalled")) { if (!c.calledObserved) c.done = t; c.calledObserved = true; }
                    if (c.calledObserved && c.landed >= 0)
                    {
                        bool ok = c.exitStarted >= 0 && c.exitStarted <= 1.5 && c.landed <= 10 && c.done - c.landed <= 10 && WhClean(c);
                        WhFinish(c, now, ok, "exit=" + c.exitStarted.ToString("F2") + " landed=" + c.landed.ToString("F2") + " calledAfterLanding=" + (c.done - c.landed).ToString("F2") + WhSummary(c)); return;
                    }
                    if (t > 12 && !c.calledObserved && c.exitStarted < 0)
                    {
                        bool selecting = whSlot != null && Get<int>(whSlot, "_selectionToken") != 0;
                        WhFinish(c, now, false, "the call was not accepted (KD_SleepSlot._TryCall: selection " + (selecting ? "started, no commit" : "refused, board.CanCall / reservation") + ")" + WhSummary(c), true); return;
                    }
                    if (t > 20) { WhFinish(c, now, false, "called but not out of the water in 20 s" + WhSummary(c)); return; }
                    break;
            }
            double cap = c.name == "W6-all-blocked" ? 50 : c.name == "W6-exit-blocked" ? 40 : c.name == "W4-throw" ? 60 : 25;   // W4-throw: on to the Drop's release (audit 2026-09-28 RC-09)
            if (t > cap)
            {
                if (c.name.StartsWith("W6")) whSpot.SetProgramVariable("exits", (Vector3[])whExitsBackup.Clone());
                if (c.name == "W-reroll-clock") wander[WhDog].SetProgramVariable("_waterNextRoll", Time.time + 600f);
                WhFinish(c, now, false, "timeout " + cap + " s;" + (c.name == "W-reroll-clock" ? " waterEnd=" + c.rerollEndGame.ToString("F2") + " reason=" + c.rerollEndReason + ";" : "") + " exit=" + c.exitStarted.ToString("F2") + " landed=" + c.landed.ToString("F2") + " state=" + s + WhSummary(c));
            }
        }

        static bool WhClean(WhCase c) { return c.bandOutsideSpotFrames == 0 && c.tooDeepFrames == 0 && c.bigSteps == 0 && c.mouthLowFrames == 0; }

        /// <summary>RC-09 (audit 2026-09-28): the first frame of the Drop (5) whose ball is no longer frozen = the release; the ball's
        /// gravity at that frame must already be on (the old code left a ball floated earlier weightless until Idle, ~0.9 s).</summary>
        static void WhReleaseWatch(WhCase c, int fs)
        {
            if (fs != 5 || c.releaseSeen) return;
            var rb = Get<Transform>(whFetch, "_ballT").GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic) return;
            c.releaseSeen = true; c.releaseGravity = rb.useGravity;
        }

        /// <summary>Every case ends with the audit stand-ins cleared on all six dogs (a failed case must not leak into the next).</summary>
        static void WhClearDebug()
        {
            // IMP-DOG-021 stand-ins: the river data as baked again
            if (whRiver != null && whSliceOkBackup != null) { whRiver.SetProgramVariable("sliceOk", (bool[])whSliceOkBackup.Clone()); whSliceOkBackup = null; }
            if (whRiver != null && whWadeOverride) { whRiver.SetProgramVariable("fetchWadeRatio", whWadeBackup); whWadeOverride = false; }
            if (whFetches == null) return;
            foreach (var f in whFetches)
            {
                if (f == null) continue;
                f.SetProgramVariable("_debugClaimDelay", 0f); f.SetProgramVariable("_debugClaimRefuse", false);
                f.SetProgramVariable("_debugBallHeld", false); f.SetProgramVariable("_debugPoseHold", false);
            }
        }
        /// <summary>KH_RiverData.fetchWadeRatio (IMP-DOG-021); whRiver is only bound by the fetch modes, so look it up here for 78.</summary>
        static float WhFetchWadeRatio()
        {
            if (whRiver == null) whRiver = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true).FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_RiverData");
            return whRiver != null ? Get<float>(whRiver, "fetchWadeRatio") : .45f;
        }

        static string WhSummary(WhCase c)
        { return " | frames=" + c.frames + " bandOutsideSpot=" + c.bandOutsideSpotFrames + " tooDeep=" + c.tooDeepFrames + " maxDepth=" + c.maxDepth.ToString("F3") + " maxStep=" + c.maxStep.ToString("F3") + " bigSteps=" + c.bigSteps + " stateChanges=" + c.stateChanges
            + " mouthLow=" + c.mouthLowFrames + " minMouthAboveWater=" + c.minMouthAboveWater.ToString("F3") + " swimEntries=" + c.swimEntries + " swimToggles=" + c.swimToggles; }

        // IMP-DOG-036 S3 (Codex review 2026-10-02): a released water play must actually decide every waterDecisionEvery frames and take
        // legs frames in between. Zeroed when the case's water play begins (W0: the placement; W1-*: wading, before the deep leg);
        // read when it finishes. Fails when no legs frame ran or when there were more decisions than legs frames.
        static void WhThinReset(int dog) { wander[dog].SetProgramVariable("_dbgWaterDecisions", 0); wander[dog].SetProgramVariable("_dbgWaterLegs", 0); }

        static bool WhThinOk(WhCase c, int dog)
        {
            c.thinDecisions = Get<int>(wander[dog], "_dbgWaterDecisions"); c.thinLegs = Get<int>(wander[dog], "_dbgWaterLegs");
            return c.thinLegs > 0 && c.thinDecisions <= c.thinLegs;
        }

        static string WhThinNote(WhCase c)
        { return " | thinned water decisions=" + c.thinDecisions + " legs=" + c.thinLegs + (c.thinLegs > 0 && c.thinDecisions <= c.thinLegs ? "" : " (FAIL: not thinned)"); }

        static void WhFinish(WhCase c, double now, bool pass, string reason, bool notExercised = false)
        {
            WhClearDebug();
            c.status = notExercised ? "NOT_EXERCISED" : pass ? "PASS" : "FAIL"; c.reason = reason;
            Current.status = c.status; Current.reason = reason; Current.endedAt = now;
            WhSave(null); Save();
            if (caseIndex + 1 < report.cases.Count) { Next(); whPhase = 0; whStep = 0; whNext = 0; return; }
            bool all = wh.cases.All(x => x.status == "PASS" || (x.status == "NOT_EXERCISED" && x.name == "W4-call"));
            string status = report.waterFetch ? (all ? "FETCH_PASS" : "FETCH_FAIL") : report.waterSwim ? (all ? "SWIM_PASS" : "SWIM_FAIL")
                : all ? (wh.cases.Any(x => x.status == "NOT_EXERCISED") ? "WATER_PASS_PARTIAL" : "WATER_PASS") : "WATER_FAIL";
            WhSave(status);
            End(status, all ? (report.waterFetch ? "Water fetch W3 x6 / S4 / S5b local cases" : report.waterSwim ? "Swim W1 x6 / W2 local cases" : "Water W0/W4/W6 local cases") + " (see water.json); not network/device/visual acceptance" : "Water harness: see water.json");
        }

        // ============================================================ swim mode

        /// <summary>Deepest region cell (deep = true), or the region cell 0.04-0.08 m deep nearest the entry (deep = false).</summary>
        static Vector3 WhCellBy(bool deep)
        {
            var bed = Get<float[]>(whSpot, "bed"); int nx = Get<int>(whSpot, "gridNx");
            float x0 = Get<float>(whSpot, "gridX0"), z0 = Get<float>(whSpot, "gridZ0"), step = Get<float>(whSpot, "gridStep");
            var entry = Get<Vector3>(whSpot, "entry");
            Vector3 best = entry; float bestScore = float.MinValue;
            for (int i = 0; i < bed.Length; i++)
            {
                var c = new Vector3(x0 + (i % nx) * step, bed[i], z0 + (i / nx) * step);
                if (bed[i] <= KH_WaterSpot.NoBed + 1 || !WhInSpot(c)) continue;
                float d = KH_CourseGroundEditor.RiverAt(c.z).y - bed[i];
                float score = deep ? d : (d >= .04f && d <= .08f ? -Flat(c - entry) : float.MinValue);
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        static float Flat(Vector3 v) { v.y = 0; return v.magnitude; }

        /// <summary>Depth under p from the spot's riverbed grid (nearest cell); 0 outside the grid or without a bed.</summary>
        static float WhDepthUnder(Vector3 p)
        {
            var bed = Get<float[]>(whSpot, "bed"); int nx = Get<int>(whSpot, "gridNx"), nz = bed.Length / Mathf.Max(1, nx);
            float x0 = Get<float>(whSpot, "gridX0"), z0 = Get<float>(whSpot, "gridZ0"), step = Get<float>(whSpot, "gridStep");
            int ix = Mathf.RoundToInt((p.x - x0) / step), iz = Mathf.RoundToInt((p.z - z0) / step);
            if (ix < 0 || iz < 0 || ix >= nx || iz >= nz) return 0f;
            float b = bed[iz * nx + ix];
            return b <= KH_WaterSpot.NoBed + 1 ? 0f : Mathf.Max(0f, KH_CourseGroundEditor.RiverAt(p.z).y - b);
        }

        /// <summary>Fetch mode's shallow ball. Riverbed depth (the grid, as the runtime's deep-ball rule uses) between 0.05 m and
        /// 0.9 x the dog's wading limit, and as far as possible from dry ground (a cell outside the spot or 0.02 m or shallower,
        /// searched up to 3 m); ties go to the depth nearest target. Run 20260927-115252: "every cell within 1 m wet" left only a
        /// 0.30 m cell for the Lab (limit 0.25 m), so the runtime rightly gave up (deep-ball).</summary>
        static Vector3 WhCellAtDepth(float target, float limit, out string why)
        {
            var bedA = Get<float[]>(whSpot, "bed"); int nxA = Get<int>(whSpot, "gridNx"), nzA = bedA.Length / Mathf.Max(1, nxA);
            float x0A = Get<float>(whSpot, "gridX0"), z0A = Get<float>(whSpot, "gridZ0"), stepA = Get<float>(whSpot, "gridStep");
            bool WetA(int ix, int iz)
            {
                if (ix < 0 || iz < 0 || ix >= nxA || iz >= nzA) return false;
                float b = bedA[iz * nxA + ix]; var q = new Vector3(x0A + ix * stepA, b, z0A + iz * stepA);
                return b > KH_WaterSpot.NoBed + 1 && WhInSpot(q) && KH_CourseGroundEditor.RiverAt(q.z).y - b > .02f;
            }
            int R = Mathf.CeilToInt(3f / stepA), bestI = -1; float bestDry = -1f, bestErr = float.MaxValue;
            for (int i = 0; i < bedA.Length; i++)
            {
                int cx = i % nxA, cz = i / nxA;
                if (!WetA(cx, cz)) continue;
                float depth = KH_CourseGroundEditor.RiverAt(z0A + cz * stepA).y - bedA[i];
                if (depth < .05f || depth > .9f * limit) continue;
                float dry = R * stepA;
                for (int dz = -R; dz <= R; dz++)
                    for (int dx = -R; dx <= R; dx++)
                    {
                        float dist = Mathf.Sqrt(dx * dx + dz * dz) * stepA;
                        if (dist < dry && !WetA(cx + dx, cz + dz)) dry = dist;
                    }
                float err = Mathf.Abs(depth - target);
                if (dry > bestDry + .001f || (Mathf.Abs(dry - bestDry) <= .001f && err < bestErr)) { bestDry = dry; bestErr = err; bestI = i; }
            }
            if (bestI < 0) { why = "none within 0.05-" + (.9f * limit).ToString("F3") + " m: the old nearest-depth pick"; return WhCellAtDepthOld(target); }
            var cell = new Vector3(x0A + (bestI % nxA) * stepA, bedA[bestI], z0A + (bestI / nxA) * stepA);
            why = "at=" + cell.ToString("F2") + " depth=" + (KH_CourseGroundEditor.RiverAt(cell.z).y - bedA[bestI]).ToString("F3") + " fromDry=" + bestDry.ToString("F2") + " m";
            return cell;
        }

        static Vector3 WhCellAtDepthOld(float target)
        {
            var bed = Get<float[]>(whSpot, "bed"); int nx = Get<int>(whSpot, "gridNx"), nz = bed.Length / Mathf.Max(1, nx);
            float x0 = Get<float>(whSpot, "gridX0"), z0 = Get<float>(whSpot, "gridZ0"), step = Get<float>(whSpot, "gridStep");
            Vector3 best = Get<Vector3>(whSpot, "entry"); float bestErr = float.MaxValue;
            int reach = Mathf.CeilToInt(1f / step);
            bool Wet(int ix, int iz)
            {
                if (ix < 0 || iz < 0 || ix >= nx || iz >= nz) return false;
                float b = bed[iz * nx + ix]; var q = new Vector3(x0 + ix * step, b, z0 + iz * step);
                return b > KH_WaterSpot.NoBed + 1 && WhInSpot(q) && KH_CourseGroundEditor.RiverAt(q.z).y - b > .02f;
            }
            for (int i = 0; i < bed.Length; i++)
            {
                var c = new Vector3(x0 + (i % nx) * step, bed[i], z0 + (i / nx) * step);
                if (bed[i] <= KH_WaterSpot.NoBed + 1 || !WhInSpot(c)) continue;
                bool inner = true;
                for (int dz = -reach; dz <= reach && inner; dz++)
                    for (int dx = -reach; dx <= reach && inner; dx++)
                        if (dx * dx + dz * dz <= reach * reach && !Wet(i % nx + dx, i / nx + dz)) inner = false;
                if (!inner) continue;
                float err = Mathf.Abs(KH_CourseGroundEditor.RiverAt(c.z).y - bed[i] - target);
                if (err < bestErr) { bestErr = err; best = c; }
            }
            return best;
        }

        static float WhPathLength(Vector3 a, Vector3 b)
        {
            NavMeshHit ha, hb; var path = new NavMeshPath();
            if (!NavMesh.SamplePosition(a, out ha, 2, NavMesh.AllAreas) || !NavMesh.SamplePosition(b, out hb, 2, NavMesh.AllAreas)
                || !NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return Flat(b - a) * 1.5f;
            float len = 0f; for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return len;
        }

        /// <summary>A bank NavMesh point radius m (horizontal) from the entry, outside the band, with a complete path to the entry;
        /// the one with the shortest path (W0-24m: the edge of the 25 m trigger).</summary>
        static Vector3 WhPointAtRadius(Vector3 entry, float radius)
        {
            Vector3 best = Vector3.zero; float bestLen = float.MaxValue;
            for (int a = 0; a < 36; a++)
            {
                float ang = a * 10f * Mathf.Deg2Rad; NavMeshHit h;
                var want = entry + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * radius;
                if (!NavMesh.SamplePosition(want, out h, 1f, NavMesh.AllAreas)) continue;
                float r = Flat(h.position - entry);
                if (r < radius - 1f || r > radius || RiverInBand(h.position)) continue;   // <= radius: the dog is placed up to 0.85 m off it
                float len = WhPathLength(h.position, entry);
                if (len < bestLen) { bestLen = len; best = h.position; }
            }
            if (bestLen == float.MaxValue) throw new InvalidOperationException("No bank point " + radius + " m from the entry");
            return best;
        }

        /// <summary>W1's debug entry (B-3): point the wading dog at t (the runtime keeps it until it arrives or gets stuck).</summary>
        static void WhSetTarget(Vector3 target) { WhSetTargetFor(WhDog, target); }

        static void WhSetTargetFor(int dog, Vector3 target)
        {
            var w = wander[dog];
            w.SetProgramVariable("_waterTarget", target); w.SetProgramVariable("_waterHasTarget", true);
            w.SetProgramVariable("_waterHoldUntil", 0f); w.SetProgramVariable("_waterFirstCellDone", true);
            w.SetProgramVariable("_waterFirstLeg", false); w.SetProgramVariable("_waterUntil", Time.time + 120f);
        }

        /// <summary>Swim mode: every other dog's stay is ended and its random play roll held off, so a W1 case measures one dog
        /// alone (the push-apart of a second dog in the water spoilt W1-Dachs twice).</summary>
        static void WhQuietOthers(int keep)
        {
            for (int i = 0; i < 6; i++)
            {
                if (i == keep) continue;
                wander[i].SetProgramVariable("_waterNextRoll", Time.time + 600f);
                wander[i].SetProgramVariable("_waterFirstDue", 0f);
                if (Get<int>(wander[i], "waterState") != 0) wander[i].SetProgramVariable("_waterUntil", 0f);
            }
        }

        static WhDogStat WhNewStat(int dog)
        {
            var d = new WhDogStat { dog = dog, breed = WhBreeds[dog], swimmer = Get<bool>(wander[dog], "waterSwimAllowed"), mouth = Get<float>(wander[dog], "waterMouthHeight"), prev = roots[dog].position };
            object bone; if (whFetches[dog].TryGetProgramVariable("mouthAnchor", out bone)) d.mouthBone = bone as Transform;
            d.mouthBoneMissing = d.mouthBone == null;   // the real mouth is required: no silent root + M fallback
            return d;
        }

        /// <summary>Per frame, per dog. The mouth is the real mouth anchor after the Animator (fallback: root + M); the feet are the
        /// root, measured against the riverbed under it (Terrain layer) while wading.</summary>
        static void WhObserveDog(WhDogStat d, float dt)
        {
            var p = roots[d.dog].position; d.frames++;
            Vector3 from = d.prev; float step = Vector3.Distance(p, from);
            string how; float jump = WhJump(d.dog, from, p, d.prevFrame, d.prevTime, out how);
            d.prev = p; d.prevFrame = Time.frameCount; d.prevTime = Time.time; if (step > d.maxStep) d.maxStep = step;
            if (jump > 0f)
            {
                d.bigSteps++;
                if (d.bigStepAt == null)
                {
                    // Evidence for the first jump (W3-Pome run 20260927-095400: one 0.39 m step, not visible in the 0.5 s trace)
                    float near = 99f; for (int k = 0; k < roots.Length; k++) if (k != d.dog && roots[k] != null) near = Mathf.Min(near, Vector3.Distance(roots[k].position, p));
                    d.bigStepAt = "step=" + step.ToString("F3") + " from=" + from.ToString("F2") + " to=" + p.ToString("F2") + " fetch=" + Get<int>(whFetches[d.dog], "dogState")
                        + " water=" + Get<int>(wander[d.dog], "waterState") + " pose=" + Get<int>(whFetches[d.dog], "waterPose") + " nearestDog=" + near.ToString("F2")
                        + " inSpot=" + WhInSpot(p) + " frame=" + Time.frameCount + " " + how;
                }
            }
            d.teleportPending = false;
            // IMP-DOG-021: a river-wide case may be in the band outside the spot where the river-wide water is (not a bridge zone)
            bool riverOk = d.riverWide && WhRiverWater(p);
            if (RiverInBand(p) && !WhInSpot(p) && !riverOk)
            {
                d.bandOutsideSpotFrames++;
                if (d.bandAt == null)
                {
                    float near = 99f; for (int k = 0; k < roots.Length; k++) if (k != d.dog && roots[k] != null) near = Mathf.Min(near, Vector3.Distance(roots[k].position, p));
                    d.bandAt = "at=" + p.ToString("F2") + " from=" + from.ToString("F2") + " fromInSpot=" + WhInSpot(from) + " water=" + Get<int>(wander[d.dog], "waterState")
                        + " pose=" + Get<int>(whFetches[d.dog], "waterPose") + " swimming=" + Get<bool>(wander[d.dog], "_waterSwimming") + " fetch=" + Get<int>(whFetches[d.dog], "dogState")
                        + " target=" + Get<Vector3>(wander[d.dog], "_waterTarget").ToString("F2") + " targetInSpot=" + WhInSpot(Get<Vector3>(wander[d.dog], "_waterTarget"))
                        + " nearestDog=" + near.ToString("F2") + " frame=" + Time.frameCount;
                }
            }
            int pose = Get<int>(whFetches[d.dog], "waterPose");
            if (pose != d.pose)
            {
                if (pose == 2) d.swimEntries++;
                if (pose * d.pose == 2) d.swimToggles++;
                if (d.pose == 2 && pose == 1) d.backToShallow++;
                if (d.pose == 2) d.swimRun = 0;
                d.pose = pose; d.sincePose = 0f;
            }
            d.sincePose += dt;
            if (pose == 2) { d.swimSeconds += dt; d.swimRun += dt; if (d.swimRun > d.longestSwim) d.longestSwim = d.swimRun; }
            if (pose != 0) { d.wet = true; d.wetSeconds += dt; }
            if (!(WhInSpot(p) || (riverOk && RiverInBand(p))) || pose == 0) return;
            float surface = KH_CourseGroundEditor.RiverAt(p.z).y, depth = surface - p.y;
            float wadeRatio = d.wadeRatio > 0f ? d.wadeRatio : Get<float>(whSpot, "wadeLimitRatio");
            if (pose != 2 && !d.swimmer && depth > wadeRatio * d.mouth + .02f) d.tooDeepFrames++;
            if (pose == 1 && depth > (d.swimmer ? Get<float>(whSpot, "swimEnterRatio") : wadeRatio) * d.mouth + .02f) d.wadeDeepFrames++;
            if (d.mouthBone == null) { d.mouthBoneMissing = true; return; }
            float mouthY = d.mouthBone.position.y;
            float above = mouthY - surface, fromRoot = p.y + d.mouth - surface;
            if (above < d.minMouthAboveWater) d.minMouthAboveWater = above;
            if (fromRoot < d.minMouthAboveWaterFromRoot) d.minMouthAboveWaterFromRoot = fromRoot;
            if (above < MouthLowLimit) d.mouthLowFrames++;
            // Feet only while steadily wading: afloat the root is at water + c - Ms, below the riverbed wherever d < M - c (B-2:
            // not visible under the opaque water), and it eases back after swimming (run 20260927-072549 counted those frames).
            if (pose == 1 && d.sincePose >= .5f)
            {
                RaycastHit g;
                if (Physics.Raycast(p + Vector3.up, Vector3.down, out g, 3f, 1 << 11, QueryTriggerInteraction.Ignore))
                {
                    float foot = p.y - g.point.y;
                    if (foot < d.minFootAboveBed) d.minFootAboveBed = foot;
                    // -0.03 (decision 2026-09-27): the 0.5 m riverbed grid's interpolation error; -0.022 once in W1-Shiba (run 20260927-100739)
                    if (foot < -.03f) d.footLowFrames++;
                }
            }
        }

        // Swim <-> wade toggles: W1 (one out-and-back) at most 4. W2 (a free 45 s stay, the targets move between shallow and deep
        // cells) at most 4 per 20 s wet, rounded (decision 2026-09-27: W2-Corgi run 20260927-112936 had 6 toggles in a normal stay,
        // every other count 0).
        static int WhToggleLimit(WhDogStat d, bool perStay) { return perStay ? Mathf.Max(4, Mathf.RoundToInt(4f * d.wetSeconds / 20f)) : 4; }
        static bool WhDogOk(WhDogStat d, bool perStay)
        {
            if (d.swimmer && d.swimToggles > WhToggleLimit(d, perStay)) return false;
            return WhDogOkBase(d, perStay);
        }
        static bool WhDogOk(WhDogStat d) { return WhDogOk(d, false); }
        static bool WhDogOkBase(WhDogStat d, bool perStay)
        {
            if (d.mouthBoneMissing) return false;   // reason "mouth bone missing" in the summary
            bool clean = d.bandOutsideSpotFrames == 0 && d.bigSteps == 0 && d.tooDeepFrames == 0 && d.mouthLowFrames == 0 && d.footLowFrames == 0;
            bool swimRule = d.swimmer ? d.swimEntries >= 1 && d.swimToggles <= WhToggleLimit(d, perStay) : d.swimEntries == 0;
            return clean && swimRule && d.landed && d.landedOnSpotBank;
        }

        static string WhDogSummary(WhDogStat d)
        {
            return " [" + d.breed + (d.swimmer ? " swimmer" : " wades") + " ok=" + WhDogOk(d) + " band=" + d.bandOutsideSpotFrames + " big=" + d.bigSteps + " maxStep=" + d.maxStep.ToString("F3")
                + " tooDeep=" + d.tooDeepFrames + " wadeDeep=" + d.wadeDeepFrames + " mouthLow=" + d.mouthLowFrames + " minMouth=" + d.minMouthAboveWater.ToString("F3") + " (root+M " + d.minMouthAboveWaterFromRoot.ToString("F3")
                + ") footLow=" + d.footLowFrames + " minFoot=" + d.minFootAboveBed.ToString("F3") + " swims=" + d.swimEntries + " toggles=" + d.swimToggles + " (limit W1 4 / W2 " + WhToggleLimit(d, true) + ")"
                + " swimS=" + d.swimSeconds.ToString("F1") + " longest=" + d.longestSwim.ToString("F1") + " backToWade=" + d.backToShallow + " wetS=" + d.wetSeconds.ToString("F1")
                + " landed=" + d.landed + " sameBank=" + d.landedOnSpotBank + " recover=" + d.recoverStage + (d.mouthBoneMissing ? " FAIL: mouth bone missing" : "")
                + (d.bigStepAt != null ? " firstBig{" + d.bigStepAt + "}" : "") + (d.bandAt != null ? " firstBand{" + d.bandAt + "}" : "") + "]";
        }

        static void WhEvaluateW1(WhCase c, double now, double t)
        {
            var p = roots[WhDog].position; var d = c.stats[0];
            if (c.w1Leg == 0)
            {
                if (Flat(p - c.deepTarget) < .7f) c.reachedDeep = true;
                if (c.reachedDeep || t > 20) { WhSetTarget(c.shallowTarget); c.w1Leg = 1; c.done = t; }
                return;
            }
            if (c.w1Leg == 1)
            {
                if (Flat(p - c.shallowTarget) < .7f || t - c.done > 20) { wander[WhDog].SetProgramVariable("_waterUntil", 0f); c.w1Leg = 2; c.exitAt = t; }
                return;
            }
            if (WhPose() == 0 && WhState() == 0)
            {
                c.landed = t - c.exitAt; d.landed = true; d.landedAt = c.landed;
                d.landedOnSpotBank = p.x > KH_CourseGroundEditor.RiverAt(p.z).x;
                bool thin = WhThinOk(c, WhDog);   // IMP-DOG-036 S3
                WhFinish(c, now, thin && WhDogOk(d) && c.landed <= 25, "reachedDeep=" + c.reachedDeep + " landed=" + c.landed.ToString("F2") + WhDogSummary(d) + WhThinNote(c));
                return;
            }
            if (t - c.exitAt > 25) WhFinish(c, now, false, "not landed 25 s after the stay ended" + WhDogSummary(d));
        }

        static void WhW2Tick(WhCase c, double now)
        {
            if (whPhase == 2 || whPhase == 3) WhW2Observe(c, now);
            if (now < whNext) return;
            var entry = Get<Vector3>(whSpot, "entry"); double t = now - whPhaseAt;
            switch (whPhase)
            {
                case 0:
                    if (whStep < 6) { pouch.SendCustomEvent("_WalkOff" + whStep++); whNext = now + .3; return; }
                    if (whStep == 6)
                    {
                        // Dogs released by the W1 cases roam beside the spot, and the 30 % / 45 s roll kept starting new plays
                        // there (run 20260927-070616: dogs 5, 3, 0 began, dog 0 swam past the 60 s wait). Quiet them first: end
                        // any stay now and hold the roll off for this case. The W2 release itself uses the first-play path.
                        for (int i = 0; i < 6; i++)
                        {
                            wander[i].SetProgramVariable("_waterNextRoll", Time.time + 600f);
                            wander[i].SetProgramVariable("_waterFirstDue", 0f);
                            if (Get<int>(wander[i], "waterState") != 0) wander[i].SetProgramVariable("_waterUntil", 0f);
                        }
                        whStep = 7; whNext = now + .5; return;
                    }
                    for (int i = 0; i < 6; i++)
                        if (Get<int>(whFetches[i], "waterPose") != 0 || Get<int>(whFetches[i], "dogState") != 0)
                        { if (now - Current.startedAt > 60) throw new TimeoutException("W2: dog " + i + " not back on land / idle (pose " + Get<int>(whFetches[i], "waterPose") + ", waterState " + Get<int>(wander[i], "waterState") + ", fetch " + Get<int>(whFetches[i], "dogState") + ")"); whNext = now + .5; return; }
                    var nodePos = Get<Vector3[]>(route, "nodePositions");
                    var pass = nodePos[Get<int[]>(route, "destinationNodes")[5]];
                    NavMeshHit h; if (!NavMesh.SamplePosition(Vector3.Lerp(pass, entry, .5f), out h, 3, NavMesh.AllAreas)) throw new InvalidOperationException("W2: no NavMesh near the spot");
                    var facing = Quaternion.LookRotation(new Vector3(entry.x - h.position.x, 0, entry.z - h.position.z));
                    Networking.LocalPlayer.TeleportTo(h.position, facing);
                    for (int i = 0; i < 6; i++)
                    {
                        NavMeshHit d; var off = new Vector3((i < 3 ? -1 : 1) * (.7f + (i % 3) * .35f), 0, -.3f - (i % 3) * .5f);
                        if (!NavMesh.SamplePosition(h.position + facing * off, out d, 2, NavMesh.AllAreas)) throw new InvalidOperationException("W2: dog placement lacks NavMesh");
                        roots[i].SetPositionAndRotation(d.position, facing);
                        var sync = roots[i].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
                    }
                    c.deepTarget = WhCellBy(true);
                    whPhase = 1; whStep = 0; whNext = now + .5; return;
                case 1:
                    if (whStep < 6) { pouch.SendCustomEvent((whStep < 3 ? "_WalkLeft" : "_WalkRight") + whStep); whStep++; whNext = now + .5; return; }
                    if (whStep == 6)
                    {
                        var walkers = Get<int[]>(manager, "walkers");
                        for (int i = 0; i < 6; i++) if (walkers[i] != Networking.LocalPlayer.playerId) throw new InvalidOperationException("W2: menu attach rejected for dog " + i);
                        c.stats.Clear();
                        for (int i = 0; i < 6; i++) { wander[i].SetProgramVariable("_waterFirstUsed", false); c.stats.Add(WhNewStat(i)); }
                        whW2FrameSum = 0; whW2Frames = 0; whStep = 7; whPhaseAt = now; whPhase = 2; return;
                    }
                    return;
                case 2:
                    if (whStep < 13) { pouch.SendCustomEvent("_WalkOff" + (whStep - 7)); whStep++; whNext = now + .3; return; }
                    if (t < 45) return;
                    for (int i = 0; i < 6; i++) wander[i].SetProgramVariable("_waterUntil", 0f);
                    whPhase = 3; whPhaseAt = now; return;
                case 3:
                    bool allLanded = true;
                    foreach (var d in c.stats)
                    {
                        bool on = Get<int>(whFetches[d.dog], "waterPose") == 0 && Get<int>(wander[d.dog], "waterState") == 0;
                        if (on && !d.landed) { d.landed = true; d.landedAt = t; var q = roots[d.dog].position; d.landedOnSpotBank = q.x > KH_CourseGroundEditor.RiverAt(q.z).x; }
                        if (!on) allLanded = false;
                    }
                    c.wetDogs = c.stats.Count(x => x.wet); c.frameMsAvg = whW2Frames > 0 ? whW2FrameSum / whW2Frames : 0;
                    string sum = " wetDogs=" + c.wetDogs + " frameMsAvg=" + c.frameMsAvg.ToString("F2") + " frameMsMax=" + c.frameMsMax.ToString("F2")
                        + " minPairDistance=" + c.minPairDistance.ToString("F2") + " together>=2 " + c.simultaneousWetSeconds.ToString("F1") + " s, all6 " + c.allSixWetSeconds.ToString("F1")
                        + " s, closer<0.3m " + c.closeSeconds.ToString("F1") + " s" + string.Concat(c.stats.Select(WhDogSummary));
                    if (allLanded) { c.landed = t; WhFinish(c, now, c.wetDogs == 6 && c.stats.All(x => WhDogOk(x, true)) && c.stats.All(x => x.wadeDeepFrames == 0), "all landed " + t.ToString("F2") + " s" + sum); return; }
                    if (t > 30) WhFinish(c, now, false, "not all landed 30 s after the stays ended" + sum);
                    return;
            }
        }

        static void WhW2Observe(WhCase c, double now)
        {
            if (c.stats.Count != 6) return;
            c.frames++; float dt = Time.unscaledDeltaTime;
            double ms = dt * 1000.0; whW2FrameSum += ms; whW2Frames++; if (ms > c.frameMsMax) c.frameMsMax = ms;
            int wetCount = 0; bool close = false;
            for (int i = 0; i < 6; i++)
            {
                var d = c.stats[i]; WhObserveDog(d, dt);
                if (d.pose != 0) wetCount++;
                // Swimmers: the W1 debug entry once, to the deepest cell, after 1.5 s wet and wading.
                if (d.pose != 0 && d.wetSince < 0) d.wetSince = (float)now;
                if (d.swimmer && !d.sentDeep && whPhase == 2 && d.wetSince >= 0 && now - d.wetSince > 1.5 && Get<int>(wander[i], "waterState") == 2)
                { WhSetTargetFor(i, c.deepTarget); d.sentDeep = true; }
                if (d.pose == 0) continue;
                for (int j = 0; j < i; j++)
                {
                    if (c.stats[j].pose == 0) continue;
                    float dist = Flat(roots[i].position - roots[j].position);
                    c.minPairDistance = Mathf.Min(c.minPairDistance, dist);
                    if (dist < .3f) close = true;
                }
            }
            if (wetCount >= 2) c.simultaneousWetSeconds += dt;
            if (wetCount == 6) c.allSixWetSeconds += dt;
            if (close) c.closeSeconds += dt;
        }

        // ============================================================ fetch mode (W3 / S4)

        static void WhFetchTick(WhCase c, double now)
        {
            if (whPhase >= 2 && c.stats.Count > 0) WhObserveDog(c.stats[0], Time.unscaledDeltaTime);
            if (whPhase >= 2) WhFetchObserve(c);
            if (now < whNext) return;
            double t = now - whPhaseAt; var entry = Get<Vector3>(whSpot, "entry");
            switch (whPhase)
            {
                case 0:
                    if (whStep < 6) { pouch.SendCustomEvent("_WalkOff" + whStep++); whNext = now + .3; return; }
                    if (whStep == 6)
                    {
                        for (int i = 0; i < 6; i++)
                        {
                            wander[i].SetProgramVariable("_waterNextRoll", Time.time + 600f); wander[i].SetProgramVariable("_waterFirstDue", 0f);
                            wander[i].SetProgramVariable("_waterFirstUsed", true);   // nobody starts a water play of its own in this mode
                            if (Get<int>(wander[i], "waterState") != 0) wander[i].SetProgramVariable("_waterUntil", 0f);
                        }
                        whStep = 7; whNext = now + .5; return;
                    }
                    if (c.dog >= 0) { WhDog = c.dog; whFetch = whFetches[WhDog]; }
                    for (int i = 0; i < 6; i++)
                        if (Get<int>(whFetches[i], "waterPose") != 0 || Get<int>(whFetches[i], "dogState") != 0)
                        { if (now - Current.startedAt > 60) throw new TimeoutException(c.name + ": dog " + i + " not back on land / idle"); whNext = now + .5; return; }
                    if (c.name.StartsWith("RW-")) { RwPlace(c); whPhase = 1; whStep = 0; whNext = now + 1; return; }   // IMP-DOG-021 (below)
                    NavMeshHit h; if (!NavMesh.SamplePosition(entry + new Vector3(2.5f, 0, 0), out h, 2, NavMesh.AllAreas)) throw new InvalidOperationException("No bank point for the thrower");
                    if (c.name.EndsWith("-farbank"))
                    {
                        // The thrower on the far (west) bank: the player itself, not a stand-in
                        var r0 = KH_CourseGroundEditor.RiverAt(entry.z); NavMeshHit west;
                        var want = new Vector3(r0.x - (KH_CourseGroundEditor.HalfWidth(entry.z) + 3f), entry.y, entry.z);
                        if (!NavMesh.SamplePosition(want, out west, 3, NavMesh.AllAreas) || RiverInBand(west.position)) throw new InvalidOperationException("No far-bank point for the thrower");
                        Networking.LocalPlayer.TeleportTo(west.position, Quaternion.LookRotation(new Vector3(1, 0, 0)));
                    }
                    else Networking.LocalPlayer.TeleportTo(h.position, Quaternion.LookRotation(new Vector3(-1, 0, 0)));
                    if (c.dog >= 0)
                    {
                        NavMeshHit d; if (!NavMesh.SamplePosition(h.position + new Vector3(.6f, 0, .9f), out d, 2, NavMesh.AllAreas)) throw new InvalidOperationException("Dog placement lacks NavMesh");
                        roots[WhDog].SetPositionAndRotation(d.position, Quaternion.LookRotation(new Vector3(-1, 0, 0)));
                        var sync = roots[WhDog].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
                        c.stats.Clear(); c.stats.Add(WhNewStat(WhDog)); c.mouth = Get<float>(wander[WhDog], "waterMouthHeight"); c.swimmer = Get<bool>(wander[WhDog], "waterSwimAllowed");
                        // IMP-DOG-021: W3-Lab-center wades to the channel under the Fetch limit (0.85 M), judged with that limit.
                        // W3-*-deep keep their meaning ("a large dog does not take a deep ball") with the limit set back to the spot's
                        // 0.45 M for the case: the switch of design §E-3 ("浅瀬だけ" = the ratio back to 0.45), restored at the case end.
                        if (c.name == "W3-Lab-center") c.stats[0].wadeRatio = Get<float>(whRiver, "fetchWadeRatio");
                        if (c.name.EndsWith("-deep") && whRiver != null)
                        {
                            whWadeBackup = Get<float>(whRiver, "fetchWadeRatio"); whWadeOverride = true;
                            whRiver.SetProgramVariable("fetchWadeRatio", Get<float>(whSpot, "wadeLimitRatio"));
                        }
                    }
                    whPhase = 1; whStep = 0; whNext = now + 1; return;
                case 1:
                    if (c.name.StartsWith("J-")) { c.stats[0].prev = roots[WhDog].position; c.stats[0].prevFrame = -1; whPhase = 2; whPhaseAt = now; c.done = -1; return; }
                    if (whStep == 0 && c.name.StartsWith("RW-"))
                    {
                        // IMP-DOG-021: the ball in the river at the case's site. RW-bridge / RW-nobank: put under the water (sunk, as W5)
                        // and "thrown" at once: never floats, goes back to the bank. The others: just above the water, floated by
                        // KH_ToyRecall from its next look, thrown 1.5 s later as the W3 cases.
                        whSpot.SetProgramVariable("floatDrift", KH_WaterSpot.DefaultFloatDrift);
                        var ballR = Get<Transform>(whFetch, "_ballT"); var bsR = ballR.GetComponent<VRC.SDK3.Components.VRCObjectSync>();
                        if (bsR != null) { bsR.SetKinematic(false); bsR.SetGravity(true); }
                        var rbR = ballR.GetComponent<Rigidbody>(); if (rbR != null && !rbR.isKinematic) { rbR.velocity = Vector3.zero; rbR.angularVelocity = Vector3.zero; }
                        ballR.position = c.ballAt; if (bsR != null) bsR.FlagDiscontinuity();
                        if (c.name.StartsWith("RW-drift"))
                        {
                            // no throw: only the ball drifts (the dog stays idle on the bank)
                            c.stats[0].prev = roots[WhDog].position; c.stats[0].prevFrame = -1; whNextTrace = now;
                            c.driftLastZ = c.ballAt.z; whPhase = 2; whPhaseAt = now; c.done = -1; return;
                        }
                        if (c.name == "RW-bridge" || c.name == "RW-nobank")
                        {
                            c.stats[0].prev = roots[WhDog].position; c.stats[0].prevFrame = -1; whNextTrace = now;
                            whFetch.SendCustomEvent("_OnBallThrown"); c.actionAt = now - Current.startedAt;
                            whPhase = 2; whPhaseAt = now; c.done = -1; return;
                        }
                        whStep = 1; whNext = now + 1.5; return;
                    }
                    if (whStep == 0)
                    {
                        // Shallow = 0.6 x the dog's wading limit (Codex 2026-09-27 M-中: the old 0.04-0.08 m cell at the bank edge
                        // let Lab / Samoyed take the ball from the dry strip, wetS 0)
                        float limit = Get<float>(whSpot, "wadeLimitRatio") * c.mouth; string why = "";
                        var cell = c.name.Contains("shallow") ? WhCellAtDepth(.6f * limit, limit, out why) : WhCellBy(true); c.deepTarget = cell;
                        if (why.Length > 0 && c.log.Count < 40) c.log.Add("shallow cell " + why + " limit=" + limit.ToString("F3"));
                        // Shallow: the ball must not drift to the bank edge before the dog gets there (run 20260927-113409, W3-Lab-shallow
                        // took it dry): no drift for the case; every other case runs with the spot's own value
                        whSpot.SetProgramVariable("floatDrift", c.name.Contains("shallow") ? 0f : KH_WaterSpot.DefaultFloatDrift);
                        var ball = Get<Transform>(whFetch, "_ballT"); var bs = ball.GetComponent<VRC.SDK3.Components.VRCObjectSync>();
                        if (bs != null) { bs.SetKinematic(false); bs.SetGravity(true); }
                        var rb = ball.GetComponent<Rigidbody>(); if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                        if (c.name == "R17-land")
                        {
                            // RC-17 on land (the regular Return): the ball on the bank 3 m inland of the thrower, out of the band
                            NavMeshHit lh; var pp = Networking.LocalPlayer.GetPosition();
                            if (!NavMesh.SamplePosition(pp + new Vector3(3f, 0, 2f), out lh, 2, NavMesh.AllAreas) || RiverInBand(lh.position) || WhInSpot(lh.position))
                                throw new InvalidOperationException("R17-land: no dry bank point for the ball");
                            ball.position = lh.position + Vector3.up * (WhBallRadius() + .02f);
                        }
                        else ball.position = new Vector3(cell.x, KH_CourseGroundEditor.RiverAt(cell.z).y + .2f, cell.z);
                        if (bs != null) bs.FlagDiscontinuity();
                        whStep = 1; whNext = now + 1.5; return;   // KH_ToyRecall takes it over as a floating ball
                    }
                    if (c.name.StartsWith("S4b")) { var fl = Get<bool[]>(whRecall, "_floating"); whRecall.SetProgramVariable("_floating", new bool[fl.Length]); }   // floated by "someone else"
                    if (c.stats.Count > 0) { c.stats[0].prev = roots[WhDog].position; c.stats[0].prevFrame = -1; }   // continuity starts at the throw
                    whNextTrace = now;
                    // RC-17 stand-ins (audit 2026-09-28): the ball's ownership as this dog sees it, delayed or refused from its next claim
                    if (c.name == "R17-delay" || c.name == "R17-land") whFetch.SetProgramVariable("_debugClaimDelay", 1.2f);
                    if (c.name == "R17-pickup") whFetch.SetProgramVariable("_debugClaimDelay", 6f);
                    if (c.name == "R17-refuse") whFetch.SetProgramVariable("_debugClaimRefuse", true);
                    // R09-*: no throw, the ball only floats (the dog stays idle on the bank)
                    if (c.dog >= 0 && !c.name.StartsWith("R09")) { whFetch.SendCustomEvent("_OnBallThrown"); c.actionAt = now - Current.startedAt; }
                    whPhase = 2; whPhaseAt = now; c.done = -1; return;
                case 2:
                    WhFetchEvaluate(c, now, t); return;
            }
        }

        static float WhBallRadius() { return Get<Transform>(whFetch, "_ballT").localScale.x * .5f; }

        // ============================================================ IMP-DOG-021 river-wide cases (88 / 88b)
        // Sites from the baked slices in the river data's heap (sliceOk / sliceBank / sliceReturn), so a case always stands where the
        // runtime would let a dog in: the thrower on a usable bank's return point, the dog 1.2 m further inland, the ball in the
        // river centre (x of the formula) at the slice's z. RW-farside: the dog on the other bank's return point. RW-bridge: a bank
        // site 4 m from the z 190 bridge (inside its zone). RW-nobank: a stretch with no usable bank within reach, from the data;
        // when the river has none, a heap stand-in clears sliceOk around z 150 for the case (restored by WhClearDebug).

        static bool RwBridgeZone(float z, float extra) { foreach (float b in RiverBridges) if (Mathf.Abs(z - b) <= KH_RiverData.DefaultBridgeZoneHalfZ + extra) return true; return false; }
        static int RwSection(float z) { int n = 0; foreach (float b in RiverBridges) if (z > b) n++; return n; }

        /// <summary>The slice index nearest target (searching outward 30 m) outside the bridge zones with an ok bank on one side (or on
        /// both, bothSides). side = the ok side (+1 preferred when both).</summary>
        static bool RwFindSlice(float target, bool bothSides, out int k, out int side)
        {
            var ok = Get<bool[]>(whRiver, "sliceOk"); float z0 = Get<float>(whRiver, "sliceZ0"), st = Get<float>(whRiver, "sliceStep");
            int c = Mathf.RoundToInt((target - z0) / st), count = ok.Length / 2;
            for (int o = 0; o <= 20; o++)
                foreach (int sgn in new[] { 1, -1 })
                {
                    k = c + o * sgn; if (k < 0 || k >= count) continue;
                    if (RwBridgeZone(z0 + k * st, 2f)) continue;
                    bool a = ok[2 * k], b = ok[2 * k + 1];
                    if (bothSides ? (a && b) : (a || b)) { side = b ? 1 : -1; return true; }
                }
            k = -1; side = 0; return false;
        }

        /// <summary>Has the river centre at z a usable bank within reach in its section (KH_RiverData._NearestSlice's rule, from the heap)?</summary>
        static bool RwHasBank(float z, bool[] ok, Vector3[] bank, float reach)
        {
            int sec = RwSection(z);
            for (int k = 0; k < ok.Length; k++)
                if (ok[k] && Mathf.Abs(bank[k].z - z) <= reach && RwSection(bank[k].z) == sec) return true;
            return false;
        }

        static void RwPlace(WhCase c)
        {
            var bank = Get<Vector3[]>(whRiver, "sliceBank"); var ret = Get<Vector3[]>(whRiver, "sliceReturn");
            float z0 = Get<float>(whRiver, "sliceZ0"), st = Get<float>(whRiver, "sliceStep"), reach = Get<float>(whRiver, "bankReach");
            if (reach <= .01f) reach = KH_RiverData.DefaultBankReach;
            Vector3 thrower, dogAt; int side;
            if (c.name.StartsWith("RW-drift"))
            {
                // Codex 2026-09-28: a floating ball upstream of a stop (a bridge zone, or a stretch without a bank made by the heap
                // stand-in) drifts down and must stop 1 m before it. The expected z mirrors KH_RiverData._DriftStops on the heap data.
                var okD = Get<bool[]>(whRiver, "sliceOk"); bool[] use = okD;
                float stopZ = -1f, startZ = -1f;
                if (c.name == "RW-drift-nobank")
                {
                    const float zc = 420f;
                    whSliceOkBackup = (bool[])okD.Clone(); use = (bool[])okD.Clone();
                    for (int k = 0; k < use.Length; k++) { float sz = z0 + (k / 2) * st; if (sz >= zc - 70f && sz <= zc) use[k] = false; }
                    whRiver.SetProgramVariable("sliceOk", use); c.standIn = true;
                    for (float q = zc + 5f; q > zc - 70f; q -= .05f) if (!RwHasBank(q - 1f, use, bank, reach)) { stopZ = q; break; }
                }
                else
                    foreach (float b in RiverBridges)
                    {
                        float q0 = b + KH_RiverData.DefaultBridgeZoneHalfZ + 4f;
                        if (!RwHasBank(q0, use, bank, reach)) continue;
                        for (float q = q0; q > b; q -= .05f) if (RwBridgeZone(q - 1f, 0f) || !RwHasBank(q - 1f, use, bank, reach)) { stopZ = q; break; }
                        if (stopZ > 0f) break;
                    }
                if (stopZ < 0f) throw new InvalidOperationException(c.name + ": no drift stop found in the data");
                startZ = stopZ + 2.5f;
                if (!RwHasBank(startZ, use, bank, reach) || RwBridgeZone(startZ, 0f)) throw new InvalidOperationException(c.name + ": the start " + startZ + " would not float");
                c.driftExpectZ = stopZ; c.driftStartZ = startZ; c.siteZ = startZ;
                var rs = KH_CourseGroundEditor.RiverAt(startZ);
                c.ballAt = new Vector3(rs.x, rs.y + .2f, startZ);
                int sd = 1; Vector3 siteD;
                if (!RiverTrySite(startZ, 1, 5, out siteD)) { sd = -1; if (!RiverTrySite(startZ, -1, 5, out siteD)) throw new InvalidOperationException(c.name + ": no bank site at z " + startZ); }
                NavMeshHit ddh; if (!NavMesh.SamplePosition(siteD + new Vector3(sd * 1.2f, 0, .8f), out ddh, 2, NavMesh.AllAreas) || RiverInBand(ddh.position)) throw new InvalidOperationException(c.name + ": dog placement lacks NavMesh");
                c.expectSide = sd;
                Networking.LocalPlayer.TeleportTo(siteD, Quaternion.LookRotation(new Vector3(-sd, 0, 0)));
                roots[WhDog].SetPositionAndRotation(ddh.position, Quaternion.LookRotation(new Vector3(-sd, 0, 0)));
                var syncD = roots[WhDog].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (syncD != null) syncD.FlagDiscontinuity();
                c.stats.Clear(); c.stats.Add(WhNewStat(WhDog)); c.mouth = Get<float>(wander[WhDog], "waterMouthHeight"); c.swimmer = Get<bool>(wander[WhDog], "waterSwimAllowed");
                if (c.log.Count < 40) c.log.Add("drift start z=" + startZ.ToString("F2") + " expected stop z=" + stopZ.ToString("F2") + (c.standIn ? " (heap stand-in)" : ""));
                return;
            }
            if (c.name == "RW-bridge" || c.name == "RW-nobank")
            {
                float z = -1f;
                if (c.name == "RW-bridge") z = 194f;
                else
                {
                    var ok = Get<bool[]>(whRiver, "sliceOk");
                    for (float q = 5f; q <= 715f && z < 0f; q += 1f) if (!RwBridgeZone(q, 3f) && !RwHasBank(q, ok, bank, reach)) z = q;
                    if (z < 0f)
                    {
                        // No such stretch in the river: the stand-in clears every bank within reach + 3 m of z 150 for this case
                        z = 150f; whSliceOkBackup = (bool[])ok.Clone(); var cut = (bool[])ok.Clone();
                        for (int k = 0; k < cut.Length; k++) if (Mathf.Abs(z0 + (k / 2) * st - z) <= reach + 3f) cut[k] = false;
                        whRiver.SetProgramVariable("sliceOk", cut); c.standIn = true;
                        if (c.log.Count < 40) c.log.Add("no stretch without a bank in the data: heap stand-in around z 150 (sliceOk cleared within " + (reach + 3f) + " m)");
                    }
                }
                side = 1; Vector3 site;
                if (!RiverTrySite(z, 1, 5, out site)) { side = -1; if (!RiverTrySite(z, -1, 5, out site)) throw new InvalidOperationException(c.name + ": no bank site at z " + z); }
                thrower = site; c.siteZ = z;
                var r = KH_CourseGroundEditor.RiverAt(z);
                c.ballAt = new Vector3(r.x, r.y - .25f, z);   // sunk (W5 w5-ball-in-river)
                NavMeshHit dh; if (!NavMesh.SamplePosition(site + new Vector3(side * 1.2f, 0, .8f), out dh, 2, NavMesh.AllAreas) || RiverInBand(dh.position)) throw new InvalidOperationException(c.name + ": dog placement lacks NavMesh");
                dogAt = dh.position;
            }
            else
            {
                float target = c.name.StartsWith("RW-down") ? 150f : c.name.StartsWith("RW-up") || c.name == "RW-long" ? 400f : 150f;
                int k;
                if (!RwFindSlice(target, c.name == "RW-farside", out k, out side)) throw new InvalidOperationException(c.name + ": no usable bank near z " + target + " (Course/91)");
                int codeA = 2 * k + (side > 0 ? 1 : 0);
                thrower = ret[codeA]; c.siteZ = z0 + k * st;
                var r = KH_CourseGroundEditor.RiverAt(c.siteZ);
                c.ballAt = new Vector3(r.x, r.y + .2f, c.siteZ);
                if (c.name == "RW-long") thrower = RwRoadThrower(thrower, c);   // the thrower on the road: a long NavMesh approach
                Vector3 dogBase = thrower; int dogSide = side;
                if (c.name == "RW-farside") { dogSide = -side; dogBase = ret[2 * k + (dogSide > 0 ? 1 : 0)]; }
                NavMeshHit dh; if (!NavMesh.SamplePosition(dogBase + new Vector3(dogSide * 1.2f, 0, .8f), out dh, 2, NavMesh.AllAreas) || RiverInBand(dh.position)) throw new InvalidOperationException(c.name + ": dog placement lacks NavMesh");
                dogAt = dh.position;
            }
            c.expectSide = side;
            Networking.LocalPlayer.TeleportTo(thrower, Quaternion.LookRotation(new Vector3(-side, 0, 0)));
            roots[WhDog].SetPositionAndRotation(dogAt, Quaternion.LookRotation(new Vector3(-side, 0, 0)));
            var sync = roots[WhDog].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
            c.stats.Clear(); c.stats.Add(WhNewStat(WhDog)); c.mouth = Get<float>(wander[WhDog], "waterMouthHeight"); c.swimmer = Get<bool>(wander[WhDog], "waterSwimAllowed");
            // The retrieving cases may be in the river-wide water; the bridge / no-bank cases may not (strict band rule)
            c.stats[0].riverWide = !(c.name == "RW-bridge" || c.name == "RW-nobank");
            if (!c.swimmer) c.stats[0].wadeRatio = Get<float>(whRiver, "fetchWadeRatio");
            if (c.log.Count < 40) c.log.Add("site z=" + c.siteZ.ToString("F1") + " side=" + side + " thrower=" + thrower.ToString("F2") + " dog=" + dogAt.ToString("F2") + " ball=" + c.ballAt.ToString("F2"));
        }

        /// <summary>RW-long: a route node (the road) on the same side as the bank"s return point ret, with a NavMesh path to it of
        /// 15-45 m (the one nearest 30 m); none: the nearest node on that side. Sampled on the NavMesh.</summary>
        static Vector3 RwRoadThrower(Vector3 ret, WhCase c)
        {
            int side = ret.x >= KH_CourseGroundEditor.RiverAt(ret.z).x ? 1 : -1;
            Vector3 best = ret; float bestScore = float.MaxValue, nearest = float.MaxValue; Vector3 near = ret;
            foreach (var n in nodes)
            {
                if ((n.x >= KH_CourseGroundEditor.RiverAt(n.z).x ? 1 : -1) != side || RiverInBand(n)) continue;
                float f = Flat(n - ret); if (f > 45f) continue;
                if (f < nearest) { nearest = f; near = n; }
                float len = WhPathLength(n, ret);
                if (len < 15f || len > 45f) continue;
                float score = Mathf.Abs(len - 30f);
                if (score < bestScore) { bestScore = score; best = n; }
            }
            if (bestScore == float.MaxValue) best = near;
            NavMeshHit h; if (!NavMesh.SamplePosition(best, out h, 2, NavMesh.AllAreas)) throw new InvalidOperationException(c.name + ": no NavMesh at the road point");
            if (c.log.Count < 40) c.log.Add("road thrower " + h.position.ToString("F2") + " path to the return point " + WhPathLength(h.position, ret).ToString("F1") + " m");
            return h.position;
        }

        /// <summary>RW-* evaluation (t from the throw).</summary>
        static void RwEvaluate(WhCase c, double now, double t)
        {
            var ballT = Get<Transform>(whFetch, "_ballT"); var bp = ballT.position; var st = c.stats[0];
            if (c.name.StartsWith("RW-drift"))
            {
                // The ball drifts from driftStartZ, stops at driftExpectZ +- 0.3 m, stays there (|dz| < 0.05 m) for 5 s, floating at the
                // water line, never sent to the bank; no Fetch starts (no throw).
                float want = KH_CourseGroundEditor.RiverAt(bp.z).y - WhBallRadius() * Get<float>(whSpot, "floatSink");
                bool floatingNow = Mathf.Abs(bp.y - want) < .05f && RiverInBand(bp);
                if (Mathf.Abs(bp.z - c.driftLastZ) > .05f) { c.driftLastZ = bp.z; c.driftStillSince = (float)t; }
                if (c.driftStillSince < 0f) c.driftStillSince = (float)t;
                c.ballEndZ = bp.z;
                c.driftStill = t - c.driftStillSince >= 5 && t > 6;
                if (c.driftStill || t > 60)
                {
                    int fsD = Get<int>(whFetch, "dogState");
                    bool okD = c.driftStill && Mathf.Abs(bp.z - c.driftExpectZ) <= .3f && floatingNow && !c.returnLogged && fsD == 0 && WhStatClean(st);
                    WhFinish(c, now, okD, "start z=" + c.driftStartZ.ToString("F2") + " stopped z=" + bp.z.ToString("F2") + " expected=" + c.driftExpectZ.ToString("F2")
                        + " still5s=" + c.driftStill + " floating=" + floatingNow + " returned=" + c.returnLogged + (c.standIn ? " (heap stand-in)" : "") + " fetch=" + fsD + WhDogSummary(st)); return;
                }
                return;
            }
            int fs = Get<int>(whFetch, "dogState");
            if (c.name == "RW-bridge" || c.name == "RW-nobank")
            {
                // Never floats, never entered: the ball is returned to the bank on the thrower's side within 11 s (sunk: at once), the
                // dog stays out of the band (strict rule) and never starts a water fetch.
                if (!c.ballReturned && !RiverInBand(bp) && Flat(bp - c.ballAt) > 1f)
                { c.ballReturned = true; c.returnedAt = t; c.ballOnPlayerSide = (bp.x > KH_CourseGroundEditor.RiverAt(bp.z).x ? 1 : -1) == c.expectSide; }
                if ((c.ballReturned && t - c.returnedAt > 4 && fs == 0) || t > 25)
                {
                    bool okB = c.ballReturned && c.returnedAt <= 11 && c.ballOnPlayerSide && !c.seen6 && !c.riverFetchLogged && WhStatClean(st);
                    WhFinish(c, now, okB, "z=" + c.siteZ.ToString("F1") + (c.standIn ? " (heap stand-in)" : "") + " ballReturned=" + c.ballReturned + " at " + c.returnedAt.ToString("F2")
                        + " s playerSide=" + c.ballOnPlayerSide + " seen6=" + c.seen6 + " riverFetch=" + c.riverFetchLogged + " fetch=" + fs + WhDogSummary(st)); return;
                }
                return;
            }
            // Retrieval anywhere along the river: 6 -> 7 -> 8 -> Return / Drop, the ball by the player, landed on the thrower's side,
            // the W3 per-frame rules in the river-wide water, no water-timeout (the time contract), large dogs never swim.
            if (!(c.fetchEnded || t > 120)) return;
            c.ballEndDistance = Vector3.Distance(bp, Networking.LocalPlayer.GetPosition());
            c.entrySide = (Get<int>(whFetch, "_wfEntryCode") & 1) == 1 ? 1 : -1;
            int exitCode = Get<int>(whFetch, "_wfExitCode"); c.exitSide = exitCode < 0 ? 0 : (exitCode & 1) == 1 ? 1 : -1;
            bool water = st.wetSeconds > 0f && st.minMouthAboveWater < 9f && c.pickupDepth > .02f;
            bool ok = c.seen6 && c.seen7 && c.seen8 && c.fetchEnded && c.riverFetchLogged && !c.waterTimeoutLogged && c.ballTopBelowFrames == 0 && c.carryMouthLowFrames == 0
                && WhStatClean(st) && water && st.landed && st.landedOnSpotBank && (c.seenReturn || c.seenDrop) && c.ballEndDistance <= 2.5f
                && c.holdSeen && c.holdKinematic && c.seen5 && c.releaseSeen && c.releaseGravity && c.exitSide == c.expectSide && (c.swimmer || st.swimEntries == 0);
            if (c.name == "RW-farside") ok &= c.entrySide == -c.expectSide;
            if (c.name == "RW-long")
            {
                // Codex 2026-09-28: the long approach re-plans the limit at the water (never below the first plan) and the carry back
                // goes over the NavMesh from the bank
                bool longOk = c.landPath >= 15f && c.riverEnteredLogged && c.enteredAt >= 0f && c.limitAfterEntry >= c.limitFirst && c.riverCarryLogged;
                ok &= longOk;
                if (c.log.Count < 40) c.log.Add("long: path=" + c.landPath.ToString("F1") + " entered=" + c.enteredAt.ToString("F1") + " limit first/after=" + c.limitFirst.ToString("F1") + "/" + c.limitAfterEntry.ToString("F1") + " carry=" + c.riverCarryLogged);
            }
            WhFinish(c, now, ok, "z=" + c.siteZ.ToString("F1") + " states 6/7/8/4/5=" + c.seen6 + "/" + c.seen7 + "/" + c.seen8 + "/" + c.seenReturn + "/" + c.seenDrop + " ended=" + c.fetchEnded
                + " t=" + t.ToString("F1") + " limits 6/7=" + c.chaseLimit.ToString("F1") + "/" + c.carryLimit.ToString("F1") + " waterTimeout=" + c.waterTimeoutLogged
                + " entrySide=" + c.entrySide + " exitSide=" + c.exitSide + " throwerSide=" + c.expectSide + " ballToPlayer=" + c.ballEndDistance.ToString("F2")
                + " ballTopBelow=" + c.ballTopBelowFrames + " carryMouthLow=" + c.carryMouthLowFrames + " wet=" + water + " pickupDepth=" + c.pickupDepth.ToString("F3")
                + " frozenAtHold=" + c.holdKinematic + " dropRelease=" + c.releaseSeen + " gravityAtRelease=" + c.releaseGravity + WhDogSummary(st));
        }

        static void WhFetchObserve(WhCase c)
        {
            if (c.dog < 0) return;
            int fs = Get<int>(whFetch, "dogState");
            if (EditorApplication.timeSinceStartup >= whNextTrace && c.trace.Count < 240)
            {
                // Every 0.5 s: Fetch state, water play state, pose, dog / ball, inside the spot, the last river refusal (this or
                // the previous frame): 1 sink-guard / 2 wade-limit / 3 band outside the spot.
                whNextTrace = EditorApplication.timeSinceStartup + .5;
                var q = roots[WhDog].position; var b = Get<Transform>(whFetch, "_ballT").position;
                int blockFrame = Get<int>(wander[WhDog], "_waterBlockFrame");
                int block = Time.frameCount - blockFrame <= 1 ? Get<int>(wander[WhDog], "_waterBlock") : 0;
                c.trace.Add("t=" + (EditorApplication.timeSinceStartup - whPhaseAt).ToString("F1") + " fetch=" + fs + " water=" + WhState() + " pose=" + WhPose()
                    + " dog=" + q.ToString("F2") + " dogInSpot=" + WhInSpot(q) + " dogBand=" + RiverInBand(q) + " ball=" + b.ToString("F2") + " ballInSpot=" + WhInSpot(b)
                    + " ballBelowSurface=" + (KH_CourseGroundEditor.RiverAt(b.z).y - b.y).ToString("F3") + " bedDepth=" + WhDepthUnder(b).ToString("F3") + " speed=" + Get<float>(wander[WhDog], "_speed").ToString("F2") + " block=" + block);
            }
            if (fs > c.maxFetchState) c.maxFetchState = fs;
            // RC-17 / RC-09 (audit 2026-09-28): the first frame of the hold (4 or 7) must already have the ball frozen; the first
            // frame after the Drop's release must already have gravity
            var rbH = Get<Transform>(whFetch, "_ballT").GetComponent<Rigidbody>();
            if (!c.holdSeen && (fs == 7 || fs == 4)) { c.holdSeen = true; c.holdKinematic = rbH != null && rbH.isKinematic; c.holdGame = Time.time; }
            if (fs == 3) c.seen3 = true;
            if (fs == 4) c.seen4 = true;
            if (fs == 5) c.seen5 = true;
            WhReleaseWatch(c, fs);
            if (c.name == "R17-pickup" && !c.injected && c.claimWaitGame >= 0 && Time.time - c.claimWaitGame > .3f)
            { whFetch.SetProgramVariable("_debugBallHeld", true); c.injected = true; c.injectedGame = Time.time; }   // a person takes the ball while the dog waits
            if (c.name == "R17-lost" && !c.injected && fs == 7)
            { whFetch.SetProgramVariable("_debugClaimRefuse", true); c.injected = true; c.injectedGame = Time.time; }   // ownership lost while carrying
            if (c.injected && fs == 0 && c.idleAfterInject < 0) c.idleAfterInject = Time.time - c.injectedGame;
            if (fs == 6) c.seen6 = true;
            bool rw = c.stats.Count > 0 && c.stats[0].riverWide;   // IMP-DOG-021 river-wide case
            if (fs == 6 && c.chaseLimit < 0f && rw) c.chaseLimit = Get<float>(whFetch, "_wfLimit");
            if (fs == 6 && rw) { float ea = Get<float>(whFetch, "_wfEnteredAt"); if (ea >= 0f && c.enteredAt < 0f) { c.enteredAt = ea; c.limitAfterEntry = Get<float>(whFetch, "_wfLimit"); c.limitFirst = Get<float>(whFetch, "_wfLimitFirst"); c.landPath = Get<float>(whFetch, "_wfPlanLand"); } }
            if (fs == 7 && !c.seen7)
            {
                var q7 = roots[WhDog].position; c.pickupDepth = rw ? WhDepthTerrain(q7) : WhDepthUnder(q7);
                if (rw) c.carryLimit = Get<float>(whFetch, "_wfCarryLimit");
            }
            if (fs == 7) c.seen7 = true;
            if (fs == 8) c.seen8 = true;
            if (c.seen8 && c.stats.Count > 0 && !c.stats[0].landed && Get<int>(whFetch, "waterPose") == 0)
            {
                c.stats[0].landed = true; c.stats[0].landedAt = EditorApplication.timeSinceStartup - whPhaseAt;
                var lp = roots[WhDog].position; float lcx = KH_CourseGroundEditor.RiverAt(lp.z).x;
                // the spot is on the east bank; a river-wide case lands on the thrower's side (expectSide)
                c.stats[0].landedOnSpotBank = c.expectSide == 0 ? lp.x > lcx : (lp.x > lcx ? 1 : -1) == c.expectSide;
            }
            if (fs == 4 && c.seen8) c.seenReturn = true;
            if (fs == 5 && c.seen8) c.seenDrop = true;
            if (fs == 0 && (c.seenReturn || c.seenDrop)) c.fetchEnded = true;
            if (fs == 7 || fs == 8)
            {
                var b = Get<Transform>(whFetch, "_ballT").position;
                if ((WhInSpot(b) || (rw && RiverInBand(b))) && b.y + WhBallRadius() < KH_CourseGroundEditor.RiverAt(b.z).y) c.ballTopBelowFrames++;
                // Design B-1: while carrying in the water, the real mouth stays at least ball radius + 0.01 m above the water
                var st = c.stats.Count > 0 ? c.stats[0] : null;
                var rq = roots[WhDog].position;
                if (st != null && st.mouthBone != null && Get<int>(whFetch, "waterPose") != 0 && (WhInSpot(rq) || (rw && RiverInBand(rq))))
                {
                    var m = st.mouthBone.position;
                    if (m.y - KH_CourseGroundEditor.RiverAt(m.z).y < WhBallRadius() + .01f) c.carryMouthLowFrames++;
                }
            }
        }

        /// <summary>IMP-DOG-021: the river-wide water by the generating formula (independent of the runtime data, like RiverInBand):
        /// the band outside the bridge zones (bridge z +- 8 m).</summary>
        static bool WhRiverWater(Vector3 p)
        {
            foreach (float b in RiverBridges) if (Mathf.Abs(p.z - b) <= KH_RiverData.DefaultBridgeZoneHalfZ) return false;
            return p.z >= 0 && p.z <= 720;
        }

        /// <summary>Depth under p from the saved Terrain (a ray onto the Terrain layer) and the formula's water surface; 0 when dry.</summary>
        static float WhDepthTerrain(Vector3 p)
        {
            RaycastHit g;
            if (!Physics.Raycast(new Vector3(p.x, KH_CourseGroundEditor.RiverAt(p.z).y + 3f, p.z), Vector3.down, out g, 8f, 1 << 11, QueryTriggerInteraction.Ignore)) return 0f;
            return Mathf.Max(0f, KH_CourseGroundEditor.RiverAt(p.z).y - g.point.y);
        }

        /// <summary>IMP-DOG-021 (W4-toy / W4-toy-chase): the spot cell with depth in [minD, maxD] farthest from p (inside the region).</summary>
        static Vector3 WhCellFar(Vector3 from, float minD, float maxD)
        {
            var bed = Get<float[]>(whSpot, "bed"); int nx = Get<int>(whSpot, "gridNx");
            float x0 = Get<float>(whSpot, "gridX0"), z0 = Get<float>(whSpot, "gridZ0"), step = Get<float>(whSpot, "gridStep");
            Vector3 best = WhCellBy(false); float bestD = -1f;
            for (int i = 0; i < bed.Length; i++)
            {
                if (bed[i] <= KH_WaterSpot.NoBed + 1) continue;
                var c = new Vector3(x0 + (i % nx) * step, bed[i], z0 + (i / nx) * step);
                if (!WhInSpot(c)) continue;
                float d = KH_CourseGroundEditor.RiverAt(c.z).y - bed[i];
                if (d < minD || d > maxD) continue;
                float f = Flat(c - from);
                if (f > bestD) { bestD = f; best = c; }
            }
            return best;
        }

        static bool WhStatClean(WhDogStat d)
        {
            return d != null && !d.mouthBoneMissing && d.bandOutsideSpotFrames == 0 && d.bigSteps == 0 && d.tooDeepFrames == 0 && d.mouthLowFrames == 0 && d.footLowFrames == 0;
        }

        static bool WhBallFloats()
        {
            var b = Get<Transform>(whFetch, "_ballT").position;
            float want = KH_CourseGroundEditor.RiverAt(b.z).y - WhBallRadius() * Get<float>(whSpot, "floatSink");
            return WhInSpot(b) && Mathf.Abs(b.y - want) < .05f;
        }

        static void WhFetchEvaluate(WhCase c, double now, double t)
        {
            string stat = c.stats.Count > 0 ? WhDogSummary(c.stats[0]) : "";
            if (c.name.StartsWith("RW-")) { RwEvaluate(c, now, t); return; }   // IMP-DOG-021
            if (c.name.StartsWith("S4"))
            {
                if (c.done < 0 && t > 2)
                {
                    // What a new ball Owner has: no local float state at all
                    var fl = Get<bool[]>(whRecall, "_floating"); whRecall.SetProgramVariable("_floating", new bool[fl.Length]); c.done = t; return;
                }
                if (c.done >= 0 && t - c.done > 5)
                {
                    c.ballStillFloating = WhBallFloats();
                    WhFinish(c, now, c.ballStillFloating, "ballStillFloating=" + c.ballStillFloating + " (float state wiped as for a new Owner)"); return;
                }
                return;
            }
            if (c.name.StartsWith("J-"))
            {
                // Self-test of the jump rule: move the idle dog 0.8 m in one normal frame; exactly that one step must count
                if (c.done < 0 && t > 1)
                {
                    NavMeshHit j; var from = roots[WhDog].position;
                    if (!NavMesh.SamplePosition(from + roots[WhDog].right * .8f, out j, .3f, NavMesh.AllAreas)) j.position = from + roots[WhDog].right * .8f;
                    roots[WhDog].position = new Vector3(j.position.x, from.y, j.position.z); c.done = t; return;
                }
                if (c.done >= 0 && t - c.done > 1.5)
                {
                    var d = c.stats[0];
                    WhFinish(c, now, d.bigSteps == 1, "injected 0.8 m jump counted=" + d.bigSteps + (d.bigStepAt != null ? " first{" + d.bigStepAt + "}" : "")); return;
                }
                return;
            }
            if (c.name.StartsWith("R09") || c.name.StartsWith("R17-"))
            {
                // Audit 2026-09-28 cases (header of this file). t counts from the throw (R09: from the float without a throw).
                var ballR = Get<Transform>(whFetch, "_ballT"); var rbR = ballR.GetComponent<Rigidbody>(); var bsR = ballR.GetComponent<VRC.SDK3.Components.VRCObjectSync>();
                int fsR = Get<int>(whFetch, "dogState");
                var stR = c.stats.Count > 0 ? c.stats[0] : null;
                if (c.name.StartsWith("R09"))
                {
                    // RC-09: the float flag says "floating" while a pickup shorter than one 0.25 s look (regrab) or another Owner's
                    // pickup and release (A -> B -> A: let go 0.12 m above the water) turned the gravity on. The next look must turn it off.
                    if (c.stage == 0)
                    {
                        if (t < 1) return;
                        c.floatingBefore = rbR != null && !rbR.useGravity && !rbR.isKinematic && WhBallFloats();
                        if (bsR != null) { bsR.SetKinematic(false); bsR.SetGravity(true); }
                        if (c.name.EndsWith("ABA")) { ballR.position += Vector3.up * .12f; if (bsR != null) bsR.FlagDiscontinuity(); }
                        c.stage = 1; c.done = t; return;
                    }
                    if (c.gravityOffAfter < 0 && rbR != null && !rbR.useGravity) c.gravityOffAfter = (float)(t - c.done);
                    if (t - c.done < 3) return;
                    c.ballStillFloating = WhBallFloats();
                    bool okR9 = c.floatingBefore && c.gravityOffAfter >= 0 && c.gravityOffAfter <= .6f && c.ballStillFloating && rbR != null && !rbR.useGravity;
                    WhFinish(c, now, okR9, "floatingBefore=" + c.floatingBefore + " gravityOffAfter=" + c.gravityOffAfter.ToString("F2") + " s ballStillFloating=" + c.ballStillFloating
                        + " gravityNow=" + (rbR != null && rbR.useGravity)); return;
                }
                if (c.name == "R17-refuse")
                {
                    // The claim is refused: the dog never holds, gives up within the 2 s limit, the ball keeps floating (not frozen)
                    // Judged 3 s after the give-up (the swim to the ball takes longer than a fixed 8 s: run 20260927-214343 ended
                    // at 7.6 s with the Pome still swimming in 6); 60 s cap as for the other R17 cases
                    if (c.claimTimeoutLogged && c.done < 0) c.done = t;
                    if ((c.done < 0 || t - c.done < 3) && t <= 60) return;
                    bool okRf = c.claimWaitLogged && c.claimTimeoutLogged && !c.seen7 && fsR == 0 && rbR != null && !rbR.isKinematic && WhBallFloats() && WhStatClean(stR);
                    WhFinish(c, now, okRf, "claimWait=" + c.claimWaitLogged + " claimTimeout=" + c.claimTimeoutLogged + " seen7=" + c.seen7 + " fetch=" + fsR
                        + " ballKinematic=" + (rbR != null && rbR.isKinematic) + " ballFloats=" + WhBallFloats() + (stR != null ? WhDogSummary(stR) : "")); return;
                }
                if (c.name == "R17-pickup")
                {
                    // A person takes the ball while the dog waits for its ownership: give up at once, never hold, do not touch the ball
                    if (c.injected && c.idleAfterInject >= 0 && c.stage == 0 && Time.time - c.injectedGame - c.idleAfterInject > .5f)
                    { whFetch.SetProgramVariable("_debugBallHeld", false); c.stage = 1; c.done = t; return; }
                    if (c.stage == 1 && t - c.done > 2)
                    {
                        bool okP = c.claimWaitLogged && c.claimHeldLogged && !c.seen7 && c.idleAfterInject >= 0 && c.idleAfterInject < .5f && rbR != null && !rbR.isKinematic && WhBallFloats();
                        WhFinish(c, now, okP, "claimWait=" + c.claimWaitLogged + " heldWhileClaiming=" + c.claimHeldLogged + " seen7=" + c.seen7 + " idleAfter=" + c.idleAfterInject.ToString("F2")
                            + " s ballKinematic=" + (rbR != null && rbR.isKinematic) + " ballFloats=" + WhBallFloats()); return;
                    }
                    if (t > 30) WhFinish(c, now, false, "no claim wait / give-up within 30 s (claimWait=" + c.claimWaitLogged + " injected=" + c.injected + " fetch=" + fsR + ")");
                    return;
                }
                if (c.name == "R17-lost")
                {
                    // Ownership lost while carrying (state 7): asked again, not back in 2 s -> give up; the ball is let go and floats
                    if (c.injected && c.idleAfterInject >= 0 && c.stage == 0) { whFetch.SetProgramVariable("_debugClaimRefuse", false); c.stage = 1; c.done = t; return; }
                    if (c.stage == 1 && t - c.done > 2.5)
                    {
                        bool okL = c.seen7 && c.ballLostLogged && c.idleAfterInject <= 3f && rbR != null && !rbR.isKinematic && !rbR.useGravity && WhBallFloats();
                        WhFinish(c, now, okL, "seen7=" + c.seen7 + " ballLost=" + c.ballLostLogged + " idleAfter=" + c.idleAfterInject.ToString("F2") + " s ballKinematic=" + (rbR != null && rbR.isKinematic)
                            + " gravity=" + (rbR != null && rbR.useGravity) + " ballFloats=" + WhBallFloats()); return;
                    }
                    if (t > 60) WhFinish(c, now, false, "no loss / give-up within 60 s (seen7=" + c.seen7 + " injected=" + c.injected + " fetch=" + fsR + ")");
                    return;
                }
                if (c.name == "R17-land")
                {
                    // The land Return: Pickup waits for the (delayed) ownership, Return starts frozen, the Drop's release has gravity
                    bool endedL = c.seen5 && fsR == 0;
                    if (!endedL && t <= 60) return;
                    float toP = Vector3.Distance(ballR.position, Networking.LocalPlayer.GetPosition());
                    float waitedL = c.holdGame >= 0 && c.claimWaitGame >= 0 ? c.holdGame - c.claimWaitGame : -1f;
                    bool okLd = c.seen3 && c.seen4 && c.claimWaitLogged && c.claimOkLogged && c.holdKinematic && waitedL >= 1.1f && c.releaseSeen && c.releaseGravity
                        && endedL && toP <= 2.5f && WhStatClean(stR);
                    WhFinish(c, now, okLd, "states 3/4/5=" + c.seen3 + "/" + c.seen4 + "/" + c.seen5 + " claimWait=" + c.claimWaitLogged + " claimOk=" + c.claimOkLogged + " waited=" + waitedL.ToString("F2")
                        + " s frozenAtHold=" + c.holdKinematic + " dropRelease=" + c.releaseSeen + " gravityAtRelease=" + c.releaseGravity + " ballToPlayer=" + toP.ToString("F2") + (stR != null ? WhDogSummary(stR) : "")); return;
                }
                // R17-delay falls through to the W3 rules below (plus its claim checks)
            }
            if (c.name.EndsWith("-deep"))
            {
                // Not chased (checked from 8 s), and the ball then drifts to the downstream end and stops there (z = end +- 0.5 m)
                if (t < 8) return;
                float dz = whPolyZ.Min() + .5f;
                var bp = Get<Transform>(whFetch, "_ballT").position; c.ballEndZ = bp.z;
                bool atEnd = Mathf.Abs(bp.z - dz) <= .5f && WhBallFloats();
                if (atEnd && c.done < 0) c.done = t;
                if (!atEnd) c.done = -1;
                if ((c.done >= 0 && t - c.done >= 3) || t > 75)
                {
                    c.ballStillFloating = WhBallFloats(); c.ballAtDownstreamEnd = c.done >= 0 && t - c.done >= 3;
                    var d = c.stats[0];
                    bool ok = !c.seen6 && c.deepBallLogged && d.swimEntries == 0 && WhStatClean(d) && c.ballStillFloating && c.ballAtDownstreamEnd;
                    WhFinish(c, now, ok, "entered=" + c.seen6 + " deepBallLogged=" + c.deepBallLogged + " ballStillFloating=" + c.ballStillFloating
                        + " atDownstreamEnd=" + c.ballAtDownstreamEnd + " ballZ=" + c.ballEndZ.ToString("F2") + " end=" + dz.ToString("F2") + " t=" + t.ToString("F1") + stat); return;
                }
                return;
            }
            if (c.fetchEnded && c.name.StartsWith("S4b") && c.done < 0) { c.done = t; return; }   // give KH_ToyRecall a few looks first
            if ((c.fetchEnded && (!c.name.StartsWith("S4b") || t - c.done >= 2)) || t > 90)
            {
                var ball = Get<Transform>(whFetch, "_ballT"); var bp = ball.position; var st = c.stats[0];
                c.ballEndDistance = Vector3.Distance(bp, Networking.LocalPlayer.GetPosition());
                bool water = st.wetSeconds > 0f && st.minMouthAboveWater < 9f && c.pickupDepth > .02f;   // really wet, the mouth measured, the ball taken in water
                bool ok = c.seen6 && c.seen7 && c.seen8 && c.fetchEnded && c.ballTopBelowFrames == 0 && c.carryMouthLowFrames == 0 && WhStatClean(st) && water
                    && st.landed && st.landedOnSpotBank;
                if (c.name == "W3-Lab-center") ok &= st.swimEntries == 0;   // IMP-DOG-021: walks the channel, never swims
                string extra = "";
                if (c.name.EndsWith("-farbank"))
                {
                    // Across the river: Drop at the entered bank's return point, no land Return
                    var rets = Get<Vector3[]>(whSpot, "returns"); c.ballEndToReturn = rets.Min(r => Flat(r - bp));
                    c.farBankDrop = c.seenDrop && !c.seenReturn && c.ballEndToReturn <= 1f && !RiverInBand(bp);
                    ok &= c.farBankDrop; extra = " farBankDrop=" + c.farBankDrop + " ballToReturnPoint=" + c.ballEndToReturn.ToString("F2");
                }
                else
                {
                    c.ballNearPlayer = c.ballEndDistance <= 2.5f; ok &= (c.seenReturn || c.seenDrop) && c.ballNearPlayer;
                }
                if (c.name.StartsWith("S4b"))
                {
                    var rb = ball.GetComponent<Rigidbody>(); c.gravityBack = rb != null && rb.useGravity && !rb.isKinematic;
                    ok &= c.gravityBack; extra += " gravityBack=" + c.gravityBack;
                }
                // Audit 2026-09-28 (every carried case): RC-17 the hold starts with the ball frozen; RC-09 the Drop's release has gravity
                // A completed carry must pass through the Drop and its release (Codex review 2026-09-28 中: a Return that gave up
                // also ends the Fetch, so "no release seen" no longer passes)
                ok &= c.holdSeen && c.holdKinematic && c.seen5 && c.releaseSeen && c.releaseGravity;
                extra += " frozenAtHold=" + c.holdKinematic + " drop=" + c.seen5 + " dropRelease=" + c.releaseSeen + " gravityAtRelease=" + c.releaseGravity;
                if (c.name == "R17-delay")
                {
                    float waited = c.holdGame >= 0 && c.claimWaitGame >= 0 ? c.holdGame - c.claimWaitGame : -1f;
                    ok &= c.claimWaitLogged && c.claimOkLogged && waited >= 1.1f;
                    extra += " claimWait=" + c.claimWaitLogged + " claimOk=" + c.claimOkLogged + " waited=" + waited.ToString("F2") + " s";
                }
                WhFinish(c, now, ok, "states 6/7/8/4/5=" + c.seen6 + "/" + c.seen7 + "/" + c.seen8 + "/" + c.seenReturn + "/" + c.seenDrop + " ended=" + c.fetchEnded
                    + " t=" + t.ToString("F1") + " ballToPlayer=" + c.ballEndDistance.ToString("F2") + " ballTopBelow=" + c.ballTopBelowFrames + " carryMouthLow=" + c.carryMouthLowFrames
                    + " wet=" + water + " pickupDepth=" + c.pickupDepth.ToString("F3") + extra + stat); return;
            }
        }

        static void WhSave(string status)
        {
            if (wh == null || report == null) return;
            try
            {
                if (status != null) wh.status = status;
                Directory.CreateDirectory(Dir + "/" + report.runId);
                File.WriteAllText(Dir + "/" + report.runId + "/water.json", JsonUtility.ToJson(wh, true));
            }
            catch (Exception e) { Debug.LogError("[KH Course Walk] Water evidence write failed: " + e.Message); }
        }
    }
}
