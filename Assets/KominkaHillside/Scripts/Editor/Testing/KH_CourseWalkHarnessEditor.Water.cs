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
    public static partial class KH_CourseWalkHarnessEditor
    {
        const int WhDog = 2;
        [Serializable] public class WhCase
        {
            public string name, status = "PENDING", reason;
            public double released = -1, playStarted = -1, wading = -1, wet = -1, actionAt = -1, exitStarted = -1, landed = -1, done = -1;
            public int frames, bandOutsideSpotFrames, tooDeepFrames, bigSteps, stateChanges, recoverStage;
            public float maxDepth, maxStep, mouth;
            public bool fetchWhileWet; public double fetchAfterLanding = -1;
            public bool refusedLogged, refused, accepted, joined, ballMoved, pickedUp, calledObserved;
            public List<string> log = new List<string>();
        }
        [Serializable] public class WhReport
        {
            public string runId, status = "RUNNING";
            public List<WhCase> cases = new List<WhCase>();
            public string limitations = "Single local ClientSim, dog 2. The per-stay first-play flag is cleared in the heap before each case after W0; the gathering is written into the synced heap; the Owner change is a heap wipe of the local play; blocked exits are heap edits of the spot. Not network, Owner handover, device or visual acceptance.";
        }
        static WhReport wh;
        static UdonBehaviour whFetch, whSpot, whActivities, whCallPose, whSlot;
        static double whWetSince = -1, whCallApplyAt = -1;
        static int whPhase, whStep, whLastState;
        static double whNext, whPhaseAt;
        static Vector3 whPrev, whBallAt;
        static Vector3[] whExitsBackup;
        static float[] whPolyX, whPolyZ;
        static bool whLogHooked;
        static WhCase WC => wh.cases[caseIndex];

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
            whFetch = all.Where(u => u.programSource != null && u.programSource.name == "KD_DogFetchController").Single(u => Get<int>(u, "dogIndex") == WhDog);
            whSpot = all.Single(u => u.programSource != null && u.programSource.name == "KH_WaterSpot");
            whActivities = all.Single(u => u.programSource != null && u.programSource.name == "KD_GroupActivities");
            whCallPose = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KD_CallPose");
            whSlot = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KD_SleepSlot" && Get<int>(u, "dogIndex") == WhDog);
            whPolyX = Get<float[]>(whSpot, "polyX"); whPolyZ = Get<float[]>(whSpot, "polyZ");
            whExitsBackup = (Vector3[])Get<Vector3[]>(whSpot, "exits").Clone();
            wh = new WhReport { runId = report.runId };
            foreach (var name in new[] { "W0", "W4-leash", "W4-gather", "W4-toy", "W4-throw", "W6-owner", "W6-exit-blocked", "W6-all-blocked", "W4-call" })
            { report.cases.Add(new Case { name = name, count = 1 }); wh.cases.Add(new WhCase { name = name }); }
            whPhase = 0; whStep = 0; whNext = 0;
            if (!whLogHooked) { Application.logMessageReceived += WhLog; whLogHooked = true; }
        }

        static void WhLog(string condition, string stack, LogType type)
        {
            if (wh == null || caseIndex < 0 || caseIndex >= wh.cases.Count || !EditorApplication.isPlaying) return;
            if (!condition.StartsWith("[KD_DogWanderController] water ") || !condition.Contains("dog=" + WhDog + " ")) return;
            var c = WC; if (c.log.Count < 40) c.log.Add(condition);
            if (condition.Contains("stage=2")) c.recoverStage = Math.Max(c.recoverStage, 2);
            if (condition.Contains("stage=3")) c.recoverStage = 3;
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
            float depth = KH_CourseGroundEditor.RiverAt(p.z).y - p.y;
            if (WhInSpot(p)) { c.maxDepth = Mathf.Max(c.maxDepth, depth); if (depth > .45f * c.mouth + .02f) c.tooDeepFrames++; }
            int s = WhState(); if (s != whLastState) { c.stateChanges++; whLastState = s; }
        }

        static void WhStepTick(double now)
        {
            var c = WC; var cc = Current;
            if (whPhase >= 2) WhObserve(c);
            if (now < whNext) return;
            double t = now - whPhaseAt;
            var entry = Get<Vector3>(whSpot, "entry");
            switch (whPhase)
            {
                case 0: // detach everyone; wait for dog 2 out of the water, idle Fetch; place near the spot and attach
                    if (whStep < 6) { pouch.SendCustomEvent("_WalkOff" + whStep++); whNext = now + .3; return; }
                    if (WhPose() != 0 || Get<int>(whFetch, "dogState") != 0) { if (now - cc.startedAt > 45) throw new TimeoutException("Dog 2 not back on land / idle"); whNext = now + .5; return; }
                    c.mouth = Get<float>(wander[WhDog], "waterMouthHeight");
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
                    if (Get<int[]>(manager, "walkers")[WhDog] != Networking.LocalPlayer.playerId) throw new InvalidOperationException("Menu attach rejected for dog 2");
                    if (Vector3.Distance(roots[WhDog].position, entry) > 12f) throw new InvalidOperationException("Dog not within 12 m of the entry at release");
                    if (c.name != "W0") wander[WhDog].SetProgramVariable("_waterFirstUsed", false);
                    pouch.SendCustomEvent("_WalkOff" + WhDog); c.released = now - cc.startedAt;
                    whPrev = roots[WhDog].position; whWetSince = -1;   // continuity starts at the release (the leashed walk before is not measured)
                    whPhase = 2; whPhaseAt = now; whLastState = WhState(); return;
                case 2: // wait for the play (W0 limits) and wading
                    if (c.playStarted < 0 && WhState() >= 1) c.playStarted = t;
                    if (c.wading < 0 && WhState() == 2) c.wading = t;
                    bool wetNow = WhPose() == 1 && WhState() == 2;
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

        static bool WhClean(WhCase c) { return c.bandOutsideSpotFrames == 0 && c.tooDeepFrames == 0 && c.bigSteps == 0; }
        static string WhSummary(WhCase c)
        { return " | frames=" + c.frames + " bandOutsideSpot=" + c.bandOutsideSpotFrames + " tooDeep=" + c.tooDeepFrames + " maxDepth=" + c.maxDepth.ToString("F3") + " maxStep=" + c.maxStep.ToString("F3") + " bigSteps=" + c.bigSteps + " stateChanges=" + c.stateChanges; }

        static void WhFinish(WhCase c, double now, bool pass, string reason, bool notExercised = false)
        {
            c.status = notExercised ? "NOT_EXERCISED" : pass ? "PASS" : "FAIL"; c.reason = reason;
            Current.status = c.status; Current.reason = reason; Current.endedAt = now;
            WhSave(null); Save();
            if (caseIndex + 1 < report.cases.Count) { Next(); whPhase = 0; whStep = 0; whNext = 0; return; }
            bool all = wh.cases.All(x => x.status == "PASS" || (x.status == "NOT_EXERCISED" && x.name == "W4-call"));
            string status = all ? (wh.cases.Any(x => x.status == "NOT_EXERCISED") ? "WATER_PASS_PARTIAL" : "WATER_PASS") : "WATER_FAIL";
            WhSave(status);
            End(status, all ? "Water W0/W4/W6 local cases (see water.json); not network/device/visual acceptance" : "Water harness: see water.json");
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
