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
    //   W4-toy          wading, menu "call the ball" -> refused (ball does not move), exit <= 1.5 s, landed <= 10 s.
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
            // Swim mode (W1 / W2)
            public int dog = 2, w1Leg, swimEntries, swimToggles, mouthLowFrames, wetDogs;
            public bool swimmer, reachedDeep, landedOnSpotBank;
            public float minMouthAboveWater = 9f, minPairDistance = 99f;
            public double frameMsAvg, frameMsMax, exitAt = -1;
            public Vector3 deepTarget, shallowTarget;
            public double simultaneousWetSeconds, allSixWetSeconds, closeSeconds;
            public List<WhDogStat> stats = new List<WhDogStat>();
        }
        [Serializable] public class WhDogStat
        {
            public int dog; public string breed;
            public bool swimmer, wet, landed, landedOnSpotBank, sentDeep;
            public int frames, bandOutsideSpotFrames, tooDeepFrames, bigSteps, mouthLowFrames, footLowFrames, swimEntries, swimToggles, backToShallow, recoverStage;
            public float maxStep, minMouthAboveWater = 9f, minMouthAboveWaterFromRoot = 9f, minFootAboveBed = 9f, swimSeconds, longestSwim, wetSeconds;
            public double landedAt = -1;
            [NonSerialized] public Vector3 prev; [NonSerialized] public int pose; [NonSerialized] public float swimRun, mouth, wetSince = -1, sincePose;
            [NonSerialized] public Transform mouthBone;
        }
        [Serializable] public class WhReport
        {
            public string runId, status = "RUNNING";
            public List<WhCase> cases = new List<WhCase>();
            public string limitations = "Single local ClientSim. Water mode: dog 2; the per-stay first-play flag is cleared in the heap before each case after W0; the gathering is written into the synced heap; the Owner change is a heap wipe of the local play; blocked exits are heap edits of the spot. Swim mode: W1 / W2 targets are written into the dog's heap (_waterTarget, the W1 debug entry of B-3); W2 holds the random play roll off during its setup. Not network, Owner handover, device or visual acceptance.";
        }
        static WhReport wh;
        static UdonBehaviour whFetch, whSpot, whActivities, whCallPose, whSlot;
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

        [MenuItem("Tools/KominkaHillside/Course/79 - Walk Harness Water Swim W1 W2")]
        public static void WaterSwimHarness()
        {
            Begin(false);
            report.waterOnly = true; report.waterSwim = true; report.deadline = EditorApplication.timeSinceStartup + 1500; Save();
        }

        [MenuItem("Tools/KominkaHillside/Course/78 - Walk Harness Water W0 W4 W6")]
        public static void WaterPlayHarness()
        {
            Begin(false);
            // EnterPlaymode takes effect after this frame, so the mode flag reaches the journal before the domain reload.
            report.waterOnly = true; report.deadline = EditorApplication.timeSinceStartup + 900; Save();
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
            whSlot = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KD_SleepSlot" && Get<int>(u, "dogIndex") == WhDog);
            whPolyX = Get<float[]>(whSpot, "polyX"); whPolyZ = Get<float[]>(whSpot, "polyZ");
            whExitsBackup = (Vector3[])Get<Vector3[]>(whSpot, "exits").Clone();
            wh = new WhReport { runId = report.runId };
            if (report.waterSwim)
            {
                foreach (int dog in new[] { 1, 4, 0, 3, 2, 5 })   // Pome and Shiba first (design D-5)
                { string name = "W1-" + WhBreeds[dog]; report.cases.Add(new Case { name = name, count = 1 }); wh.cases.Add(new WhCase { name = name, dog = dog }); }
                report.cases.Add(new Case { name = "W2", count = 6 }); wh.cases.Add(new WhCase { name = "W2", dog = -1 });
            }
            else
                foreach (var name in new[] { "W0", "W4-leash", "W4-gather", "W4-toy", "W4-throw", "W6-owner", "W6-exit-blocked", "W6-all-blocked", "W4-call" })
                { report.cases.Add(new Case { name = name, count = 1 }); wh.cases.Add(new WhCase { name = name }); }
            whPhase = 0; whStep = 0; whNext = 0;
            if (!whLogHooked) { Application.logMessageReceived += WhLog; whLogHooked = true; }
        }

        static void WhLog(string condition, string stack, LogType type)
        {
            if (wh == null || caseIndex < 0 || caseIndex >= wh.cases.Count || !EditorApplication.isPlaying) return;
            if (!condition.StartsWith("[KD_DogWanderController] water ")) return;
            if (WC.name != "W2" && !condition.Contains("dog=" + WhDog + " ") && !condition.EndsWith("dog=" + WhDog)) return;
            var c = WC; if (c.log.Count < 40) c.log.Add(condition);
            if (condition.Contains("stage=2")) c.recoverStage = Math.Max(c.recoverStage, 2);
            if (condition.Contains("stage=3")) c.recoverStage = 3;
            int at = condition.IndexOf("dog="), n;
            if (at >= 0)
            {
                int end = condition.IndexOf(' ', at + 4); if (end < 0) end = condition.Length;
                if (int.TryParse(condition.Substring(at + 4, end - at - 4), out n))
                    foreach (var st in c.stats)
                        if (st.dog == n) { if (condition.Contains("stage=2")) st.recoverStage = Math.Max(st.recoverStage, 2); if (condition.Contains("stage=3")) st.recoverStage = 3; }
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
            float step = Vector3.Distance(p, whPrev); whPrev = p;
            if (step > c.maxStep) c.maxStep = step;
            if (step > .2f && c.recoverStage < 3) c.bigSteps++;
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
                if (pose != 2) { c.maxDepth = Mathf.Max(c.maxDepth, depth); if (!c.swimmer && depth > .45f * c.mouth + .02f) c.tooDeepFrames++; }
                if (pose != 0)
                {
                    float mouthAbove = p.y + c.mouth - surface;
                    if (mouthAbove < c.minMouthAboveWater) c.minMouthAboveWater = mouthAbove;
                    if (mouthAbove < .029f) c.mouthLowFrames++;
                }
            }
            int s = WhState(); if (s != whLastState) { c.stateChanges++; whLastState = s; }
        }

        static void WhStepTick(double now)
        {
            var c = WC; var cc = Current;
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
                    if (WhPose() != 0 || Get<int>(whFetch, "dogState") != 0) { if (now - cc.startedAt > 45) throw new TimeoutException("Dog " + WhDog + " not back on land / idle"); whNext = now + .5; return; }
                    if (report.waterSwim) WhQuietOthers(WhDog);
                    c.mouth = Get<float>(wander[WhDog], "waterMouthHeight");
                    c.swimmer = Get<bool>(wander[WhDog], "waterSwimAllowed");
                    if (report.waterSwim && c.stats.Count == 0) c.stats.Add(WhNewStat(WhDog));
                    var nodePos = Get<Vector3[]>(route, "nodePositions");
                    var pass = nodePos[Get<int[]>(route, "destinationNodes")[5]];
                    var mid = Vector3.Lerp(pass, entry, .5f); NavMeshHit h;
                    if (!NavMesh.SamplePosition(mid, out h, 3, NavMesh.AllAreas)) throw new InvalidOperationException("No NavMesh between pass point and entry");
                    var facing = Quaternion.LookRotation(new Vector3(entry.x - h.position.x, 0, entry.z - h.position.z));
                    Networking.LocalPlayer.TeleportTo(h.position, facing);
                    NavMeshHit d; if (!NavMesh.SamplePosition(h.position + facing * new Vector3(-.8f, 0, .3f), out d, 2, NavMesh.AllAreas)) throw new InvalidOperationException("Dog placement lacks NavMesh");
                    roots[WhDog].SetPositionAndRotation(d.position, facing);
                    var sync = roots[WhDog].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
                    whPrev = d.position;
                    pouch.SendCustomEvent("_WalkLeft" + WhDog); whPhase = 1; whNext = now + 1; return;
                case 1: // release within 12 m of the entry
                    if (Get<int[]>(manager, "walkers")[WhDog] != Networking.LocalPlayer.playerId) throw new InvalidOperationException("Menu attach rejected for dog " + WhDog);
                    if (Vector3.Distance(roots[WhDog].position, entry) > 12f) throw new InvalidOperationException("Dog not within 12 m of the entry at release");
                    if (c.name != "W0") wander[WhDog].SetProgramVariable("_waterFirstUsed", false);
                    pouch.SendCustomEvent("_WalkOff" + WhDog); c.released = now - cc.startedAt;
                    whPrev = roots[WhDog].position; whWetSince = -1; whNextTrace = now; whLastPose = 0;
                    if (c.stats.Count > 0) c.stats[0].prev = whPrev;   // continuity starts at the release (the leashed walk before is not measured)
                    whPhase = 2; whPhaseAt = now; whLastState = WhState(); return;
                case 2: // wait for the play (W0 limits) and wading
                    if (c.playStarted < 0 && WhState() >= 1) c.playStarted = t;
                    if (c.wading < 0 && WhState() == 2)
                    {
                        c.wading = t; var at = roots[WhDog].position;
                        c.wadingStart = "t=" + t.ToString("F2") + " at=" + at.ToString("F2") + " depth=" + (KH_CourseGroundEditor.RiverAt(at.z).y - at.y).ToString("F3")
                            + " entry=" + entry.ToString("F2");
                    }
                    if (c.name == "W0" && now >= whNextTrace && c.trace.Count < 80)
                    {
                        whNextTrace = now + .5; var q = roots[WhDog].position;
                        c.trace.Add("t=" + t.ToString("F2") + " state=" + WhState() + " pose=" + WhPose() + " speed=" + Get<float>(wander[WhDog], "_speed").ToString("F2")
                            + " depth=" + (KH_CourseGroundEditor.RiverAt(q.z).y - q.y).ToString("F3") + " at=" + q.ToString("F2")
                            + " target=" + Get<Vector3>(wander[WhDog], "_waterTarget").ToString("F2"));
                    }
                    bool wetNow = WhPose() >= 1 && WhState() == 2;
                    if (wetNow) { if (whWetSince < 0) whWetSince = now; if (c.wet < 0) c.wet = t; } else whWetSince = -1;
                    bool ready = whWetSince >= 0 && now - whWetSince >= 1.5;
                    if (c.name == "W0")
                    {
                        if (c.playStarted < 0 && t > 5) { WhFinish(c, now, false, "no WaterPlay within 5 s of the release"); return; }
                        if (ready && t - c.wet > 3) { WhFinish(c, now, WhClean(c) && c.maxDepth > .02f && c.playStarted <= 5 && c.wet <= 6.0, "play " + c.playStarted.ToString("F2") + " s, wading " + c.wading.ToString("F2") + " s, wet " + c.wet.ToString("F2") + " s" + WhSummary(c)); return; }
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
                return;
            }
            switch (c.name)
            {
                case "W4-leash": pouch.SendCustomEvent("_WalkLeft" + WhDog); break;
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
                case "W4-toy": whBallAt = Get<Transform>(whFetch, "_ballT").position; pouch.SendCustomEvent("_Toy" + WhDog); break;
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
                case "W6-owner": wander[WhDog].SetProgramVariable("waterState", 0); break;
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
                    if (Vector3.Distance(Get<Transform>(whFetch, "_ballT").position, whBallAt) > .5f) c.ballMoved = true;
                    if (c.landed >= 0 && t - c.landed > 1)
                    {
                        bool ok = !c.ballMoved && c.exitStarted >= 0 && c.exitStarted <= 1.5 && c.landed <= 10 && WhClean(c);
                        WhFinish(c, now, ok, "ballMoved=" + c.ballMoved + " exit=" + c.exitStarted.ToString("F2") + " landed=" + c.landed.ToString("F2") + WhSummary(c)); return;
                    }
                    break;
                case "W4-throw":
                {
                    int fetchState = Get<int>(whFetch, "dogState");
                    if (fetchState != 0 && pose != 0) c.fetchWhileWet = true;
                    if (fetchState != 0 && c.landed >= 0 && c.fetchAfterLanding < 0) c.fetchAfterLanding = t - c.landed;
                    if (fetchState >= 3) c.pickedUp = true;
                    if (c.pickedUp)
                    {
                        bool ok = !c.fetchWhileWet && c.exitStarted >= 0 && c.exitStarted <= 1.5 && c.landed <= 10
                            && c.fetchAfterLanding >= 0 && c.fetchAfterLanding <= 10 && t <= 25 && WhClean(c);
                        WhFinish(c, now, ok, "exit=" + c.exitStarted.ToString("F2") + " landed=" + c.landed.ToString("F2") + " fetchAfterLanding=" + c.fetchAfterLanding.ToString("F2")
                            + " fetchWhileWet=" + c.fetchWhileWet + " pickup " + t.ToString("F2") + " s" + WhSummary(c)); return;
                    }
                    break;
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
            double cap = c.name == "W6-all-blocked" ? 50 : c.name == "W6-exit-blocked" ? 40 : c.name == "W4-throw" ? 35 : 25;
            if (t > cap)
            {
                if (c.name.StartsWith("W6")) whSpot.SetProgramVariable("exits", (Vector3[])whExitsBackup.Clone());
                WhFinish(c, now, false, "timeout " + cap + " s; exit=" + c.exitStarted.ToString("F2") + " landed=" + c.landed.ToString("F2") + " state=" + s + WhSummary(c));
            }
        }

        static bool WhClean(WhCase c) { return c.bandOutsideSpotFrames == 0 && c.tooDeepFrames == 0 && c.bigSteps == 0 && c.mouthLowFrames == 0; }
        static string WhSummary(WhCase c)
        { return " | frames=" + c.frames + " bandOutsideSpot=" + c.bandOutsideSpotFrames + " tooDeep=" + c.tooDeepFrames + " maxDepth=" + c.maxDepth.ToString("F3") + " maxStep=" + c.maxStep.ToString("F3") + " bigSteps=" + c.bigSteps + " stateChanges=" + c.stateChanges
            + " mouthLow=" + c.mouthLowFrames + " minMouthAboveWater=" + c.minMouthAboveWater.ToString("F3") + " swimEntries=" + c.swimEntries + " swimToggles=" + c.swimToggles; }

        static void WhFinish(WhCase c, double now, bool pass, string reason, bool notExercised = false)
        {
            c.status = notExercised ? "NOT_EXERCISED" : pass ? "PASS" : "FAIL"; c.reason = reason;
            Current.status = c.status; Current.reason = reason; Current.endedAt = now;
            WhSave(null); Save();
            if (caseIndex + 1 < report.cases.Count) { Next(); whPhase = 0; whStep = 0; whNext = 0; return; }
            bool all = wh.cases.All(x => x.status == "PASS" || (x.status == "NOT_EXERCISED" && x.name == "W4-call"));
            string status = report.waterSwim ? (all ? "SWIM_PASS" : "SWIM_FAIL")
                : all ? (wh.cases.Any(x => x.status == "NOT_EXERCISED") ? "WATER_PASS_PARTIAL" : "WATER_PASS") : "WATER_FAIL";
            WhSave(status);
            End(status, all ? (report.waterSwim ? "Swim W1 x6 / W2 local cases" : "Water W0/W4/W6 local cases") + " (see water.json); not network/device/visual acceptance" : "Water harness: see water.json");
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
            return d;
        }

        /// <summary>Per frame, per dog. The mouth is the real mouth anchor after the Animator (fallback: root + M); the feet are the
        /// root, measured against the riverbed under it (Terrain layer) while wading.</summary>
        static void WhObserveDog(WhDogStat d, float dt)
        {
            var p = roots[d.dog].position; d.frames++;
            float step = Vector3.Distance(p, d.prev); d.prev = p; if (step > d.maxStep) d.maxStep = step;
            if (step > .2f && d.recoverStage < 3) d.bigSteps++;   // only this dog's own stage-3 teleport is exempt
            if (RiverInBand(p) && !WhInSpot(p)) d.bandOutsideSpotFrames++;
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
            if (!WhInSpot(p) || pose == 0) return;
            float surface = KH_CourseGroundEditor.RiverAt(p.z).y, depth = surface - p.y;
            if (pose != 2 && !d.swimmer && depth > Get<float>(whSpot, "wadeLimitRatio") * d.mouth + .02f) d.tooDeepFrames++;
            float mouthY = d.mouthBone != null ? d.mouthBone.position.y : p.y + d.mouth;
            float above = mouthY - surface, fromRoot = p.y + d.mouth - surface;
            if (above < d.minMouthAboveWater) d.minMouthAboveWater = above;
            if (fromRoot < d.minMouthAboveWaterFromRoot) d.minMouthAboveWaterFromRoot = fromRoot;
            if (above < .029f) d.mouthLowFrames++;
            // Feet only while steadily wading: afloat the root is at water + c - Ms, below the riverbed wherever d < M - c (B-2:
            // not visible under the opaque water), and it eases back after swimming (run 20260927-072549 counted those frames).
            if (pose == 1 && d.sincePose >= .5f)
            {
                RaycastHit g;
                if (Physics.Raycast(p + Vector3.up, Vector3.down, out g, 3f, 1 << 11, QueryTriggerInteraction.Ignore))
                {
                    float foot = p.y - g.point.y;
                    if (foot < d.minFootAboveBed) d.minFootAboveBed = foot;
                    if (foot < -.02f) d.footLowFrames++;
                }
            }
        }

        static bool WhDogOk(WhDogStat d)
        {
            bool clean = d.bandOutsideSpotFrames == 0 && d.bigSteps == 0 && d.tooDeepFrames == 0 && d.mouthLowFrames == 0 && d.footLowFrames == 0;
            bool swimRule = d.swimmer ? d.swimEntries >= 1 && d.swimToggles <= 4 : d.swimEntries == 0;
            return clean && swimRule && d.landed && d.landedOnSpotBank;
        }

        static string WhDogSummary(WhDogStat d)
        {
            return " [" + d.breed + (d.swimmer ? " swimmer" : " wades") + " ok=" + WhDogOk(d) + " band=" + d.bandOutsideSpotFrames + " big=" + d.bigSteps + " maxStep=" + d.maxStep.ToString("F3")
                + " tooDeep=" + d.tooDeepFrames + " mouthLow=" + d.mouthLowFrames + " minMouth=" + d.minMouthAboveWater.ToString("F3") + " (root+M " + d.minMouthAboveWaterFromRoot.ToString("F3")
                + ") footLow=" + d.footLowFrames + " minFoot=" + d.minFootAboveBed.ToString("F3") + " swims=" + d.swimEntries + " toggles=" + d.swimToggles
                + " swimS=" + d.swimSeconds.ToString("F1") + " longest=" + d.longestSwim.ToString("F1") + " backToWade=" + d.backToShallow + " wetS=" + d.wetSeconds.ToString("F1")
                + " landed=" + d.landed + " sameBank=" + d.landedOnSpotBank + " recover=" + d.recoverStage + "]";
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
                WhFinish(c, now, WhDogOk(d) && c.landed <= 25, "reachedDeep=" + c.reachedDeep + " landed=" + c.landed.ToString("F2") + WhDogSummary(d));
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
                    if (allLanded) { c.landed = t; WhFinish(c, now, c.wetDogs == 6 && c.stats.All(WhDogOk), "all landed " + t.ToString("F2") + " s" + sum); return; }
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
