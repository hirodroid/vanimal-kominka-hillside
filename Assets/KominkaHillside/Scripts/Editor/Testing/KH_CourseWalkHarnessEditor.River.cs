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
    // IMP-DOG-020 stage 1, ClientSim harness W5 (DES-DOG-004 B-3). Same run frame as the walk harness (journal, global
    // watchdog, PlayerData backup/restore, runInBackground restore, no scene save, Play stop on any failure); only the
    // cases differ. Dog 0 only; the other five are observed too.
    //   (IMP-DOG-021, 2026-09-28: both ball cases now stand inside a bridge zone, the one part of the river where a ball still goes
    //   back to the bank; elsewhere it floats and is fetched, which Course/88b covers.)
    //   w5-ball-in-river     : the dog's ball is put under the water at the river centre and the dog is told it was thrown.
    //                          Pass = no dog frame inside the band, ball back on the bank (player's side, out of the band)
    //                          within 10 s (it has sunk, so KH_ToyRecall returns it on its next 0.25 s look).
    //   w5-ball-band-edge    : the ball is held 0.6 m inside the band edge with its centre above the water (Rigidbody
    //                          FreezeAll, not kinematic: the idle Fetch releases a kinematic ball every frame), and "thrown".
    //                          Pass = no dog frame inside the band, the Fetch gives up with reason=river-band, and the
    //                          ball returns to the bank 10 s after it entered the band (stranded rule; 9.5..11.5 s).
    //   w5-gather-far-bank   : the dog is released on the west bank near the z 190 bridge and a gathering is placed on
    //                          the east bank. Pass = joined, no band frame, crossed the centre line on the bridge deck,
    //                          arrived (≤ 3 m from the anchor) within 120 s. Also no stall-rescue latch (DES-DOG-001):
    //                          a legitimate NavMesh walk over the bridge must not be taken for a stall.
    //   w5-gather-stall      : DES-DOG-001 stall rescue (2026-10-12). The dog stands on dry land with an obstacle between it
    //                          and its slot (the straight line leaves the NavMesh, the complete path is >= 4 m longer, no
    //                          river band on the line), a gathering is written, and the harness holds the dog's root at its
    //                          start (synthetic stall: in ClientSim nothing physically stops a dog on a NavMesh path) until the
    //                          dog latches the stall. Pass = joined on a complete path, latch after gatherStallSeconds (+1 s),
    //                          one gather-stall log, then straight to the slot (<= 0.6 m off the line) and ready there within
    //                          the 60 s of the gathering; the gathering ended by the authority (startedMs moved back 61 s)
    //                          puts the roaming home within 1 m of the reached spot (KH_LeashManager.GatherHome).
    //                          Course/75g runs this case alone.
    // Band = the generating formula (KH_CourseGroundEditor.RiverAt / HalfWidth, centre ± (half width + 1 m)), not the baked
    // KH_RiverData, so the check is independent of the runtime data. A frame counts as a violation only when the dog root
    // is more than 5 cm inside the band (the runtime interpolation differs from the formula by < 0.05 m, Course/74).
    // Synthetic parts (limitations): the throw is the Fetch's local "_OnBallThrown" event after the harness places the
    // ball; the gathering is written straight into KD_GroupActivities' synced heap (host = local player, roster = dog 0),
    // skipping the trainer-pass and placement checks of the menu. Evidence: Logs/kh-course-walk/<run>/river-w5.json.
    public static partial class KH_CourseWalkHarnessEditor
    {
        const int RiverDog = 0;
        const float RiverTolerance = .05f;
        static readonly float[] RiverBridges = { 190f, 490f };

        [Serializable] public class RiverCase
        {
            public string name, status = "PENDING", reason;
            public float z, side;
            public Vector3 site, dogStart, ball, anchor, ballReturnedTo;
            public double startedAt, ballPlacedAt, ballReturnSeconds = -1, fetchStartedAt = -1, joinedAt = -1, arrivalSeconds = -1;
            public bool ballReentered, fetchStarted, ballReturned, ballReturnedOnSide, ballReturnedOnNav, joined, crossedOnBridge, arrived;
            public int frames, riverGiveUps, riverReturnLogs, crossingsOffBridge, gatheringRefreshes;
            // Ball after the bank return: Rigidbody.IsSleeping 2 s after it and at the case end, and the drift from the return point.
            public bool ballSleeping2s, ballSleepingAtEnd, watchedBallSleepingAtEnd;
            public float ballDriftAtEnd = -1f;
            public int[] violationFrames = new int[6];
            public float[] deepestInside = new float[6];   // most negative lateral distance seen (m, negative = inside)
            // DES-DOG-001 w5-gather-stall (stallLogs is also counted on w5-gather-far-bank, where it must stay 0)
            public int stallLogs, pinTicks, offMeshFrames;
            public Vector3 slot, releasePos, legTargetAtLatch, home;
            public float straightLength, pathLength, maxPinDrift, maxDeviation, homeDistance = -1f;
            public double latchSeconds = -1, latchAfterWalk = -1, readySeconds = -1, endedSeconds = -1;
            public bool pathOkBeforeLatch, latched, ready, endRequested, ended, homeOnGround;
            public List<string> log = new List<string>();
        }
        [Serializable] public class RiverReport
        {
            public string runId, status = "RUNNING";
            public List<RiverCase> cases = new List<RiverCase>();
            public string limitations = "Single local ClientSim, dog 0. Throw = local _OnBallThrown after harness placement; gathering = synced heap written directly (menu permission/placement not exercised). w5-gather-stall: the stall is synthetic (the harness holds the root until the latch); the end is the authority's own 60 s path reached by moving startedMs back. Not network, Owner handover or device acceptance.";
        }
        static RiverReport river;
        static UdonBehaviour[] riverFetch;
        static UdonBehaviour riverActivities, riverRoute, riverPack;
        static Vector3 riverPin;   // w5-gather-stall: where the root is held until the latch
        static int riverPhase, riverStep, riverPrevSign;
        static double riverNext, riverRefreshAt;
        static Vector3 riverBallPlacedAt;
        static bool riverBallFrozen;
        // After a ball case sees its return, the ball stays watched until the harness places it again (next ball case)
        // or the run ends, including the gathering case: a re-entry or an extra river-return (e.g. the third-time park)
        // fails the case that returned it, even after that case was scored (Codex 2026-09-27 M-中).
        static int riverWatchCase = -1;
        static RigidbodyConstraints riverBallConstraints;
        static bool riverLogHooked;
        static RiverCase RC => river.cases[caseIndex];

        [MenuItem("Tools/KominkaHillside/Course/75 - Walk Harness River Band W5")]
        public static void RiverBand()
        {
            Begin(false);
            // EnterPlaymode takes effect after this frame, so the mode flag reaches the journal before the domain reload.
            report.riverOnly = true; report.deadline = EditorApplication.timeSinceStartup + 480; Save();
        }

        // DES-DOG-001 (2026-10-12): the stall-rescue case alone (the same W5 frame; selectedWalkCase filters MakeRiverCases).
        [MenuItem("Tools/KominkaHillside/Course/75g - Walk Harness Gather Stall (W5 one case)")]
        public static void RiverGatherStall()
        {
            Begin(false, selectedWalkCase: "w5-gather-stall");
            report.riverOnly = true; report.deadline = EditorApplication.timeSinceStartup + 180; Save();
        }

        static readonly string[] RiverCaseNames = { "w5-ball-in-river", "w5-ball-band-edge", "w5-gather-far-bank", "w5-gather-stall" };

        static void MakeRiverCases()
        {
            var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>();
            riverFetch = all.Where(u => u.programSource != null && u.programSource.name == "KD_DogFetchController").OrderBy(u => Get<int>(u, "dogIndex")).ToArray();
            if (riverFetch.Length != 6) throw new InvalidOperationException("Six fetch behaviours required");
            riverActivities = all.Single(u => u.programSource != null && u.programSource.name == "KD_GroupActivities");
            riverRoute = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_WalkRoute");
            riverPack = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KD_DogPack");
            if (!all.Any(u => u.programSource != null && u.programSource.name == "KH_RiverData")) throw new InvalidOperationException("KH_RiverData missing: run Course/73 first");
            river = new RiverReport { runId = report.runId };
            // report.cases and river.cases stay index-aligned (RC => river.cases[caseIndex]), so the filter is applied to the names.
            var names = string.IsNullOrEmpty(report.selectedWalkCase) ? RiverCaseNames : RiverCaseNames.Where(n => n == report.selectedWalkCase).ToArray();
            if (names.Length == 0) throw new InvalidOperationException("Selected river case was not generated: " + report.selectedWalkCase);
            foreach (var name in names)
            { report.cases.Add(new Case { name = name, count = 1 }); river.cases.Add(new RiverCase { name = name }); }
            riverPhase = 0; riverStep = 0; riverNext = 0; riverWatchCase = -1; riverBallFrozen = false;
            if (!riverLogHooked) { Application.logMessageReceived += RiverLog; riverLogHooked = true; }
        }

        static void RiverLog(string condition, string stack, LogType type)
        {
            if (river == null || caseIndex < 0 || caseIndex >= river.cases.Count || !EditorApplication.isPlaying) return;
            if (condition.StartsWith("[KH_ToyRecall] river-return ball=" + RiverDog + " "))
            {
                int target = riverWatchCase >= 0 ? riverWatchCase : riverPhase >= 4 ? caseIndex : -1;
                if (target < 0) return;
                var w = river.cases[target]; w.riverReturnLogs++; if (w.log.Count < 30) w.log.Add(condition);
                if (riverWatchCase >= 0) RiverLate(target, "extra river-return: " + condition);
                return;
            }
            if (riverPhase < 4) return;
            var c = RC;
            if (condition.StartsWith("[KD_DogFetchController] give-up dog=" + RiverDog + " ") && condition.Contains("reason=river-band")) { c.riverGiveUps++; if (c.log.Count < 30) c.log.Add(condition); }
            if (condition.StartsWith("[KD_DogWanderController] gather-stall dog=" + RiverDog + " ")) { c.stallLogs++; if (c.log.Count < 30) c.log.Add(condition); }
        }

        static void RiverTick(double now)
        {
            try { RiverStep(now); }
            catch { SaveRiverDetail("FAIL"); throw; }
        }

        static void RiverStep(double now)
        {
            var c = RC;
            if (riverWatchCase >= 0 && RiverInBand(Get<Transform>(riverFetch[RiverDog], "_ballT").position)) RiverLate(riverWatchCase, "ball back in the band");
            if (riverPhase >= 3) RiverObserve(c);
            if (now < riverNext) return;
            if (riverPhase == 0) // Regular menu detach for every dog, then wait until dog 0 has no Fetch in progress.
            {
                if (riverStep < 6) { pouch.SendCustomEvent("_WalkOff" + riverStep++); riverNext = now + .3; return; }
                if (Get<int[]>(manager, "walkers").Any(w => w >= 0)) throw new InvalidOperationException("Detach did not complete");
                if (Get<int>(riverFetch[RiverDog], "dogState") != 0)
                { if (now - Current.startedAt > 30) throw new TimeoutException("Dog 0 Fetch did not return to Idle"); riverNext = now + .5; return; }
                RiverPlace(c); riverPhase = 1; riverNext = now + .6; return;
            }
            if (riverPhase == 1) { pouch.SendCustomEvent("_WalkLeft" + RiverDog); riverPhase = 2; riverNext = now + .8; return; }
            if (riverPhase == 2) // Detach here, so the released roaming home is this site.
            {
                if (Get<int[]>(manager, "walkers")[RiverDog] != Networking.LocalPlayer.playerId) throw new InvalidOperationException("Menu attach rejected for dog 0");
                pouch.SendCustomEvent("_WalkOff" + RiverDog); riverPhase = 3; riverNext = now + 1.5; return;
            }
            if (riverPhase == 3) { RiverStart(c, now); riverPhase = 4; return; }
            if (riverPhase == 4) RiverEvaluate(c, now);
        }

        static float RiverLateral(Vector3 p)
        { var r = KH_CourseGroundEditor.RiverAt(p.z); return Mathf.Abs(p.x - r.x) - (KH_CourseGroundEditor.HalfWidth(p.z) + 1f); }
        static bool RiverOnDeck(Vector3 p)
        { float s = KH_CourseGroundEditor.RiverAt(p.z).y; foreach (float b in RiverBridges) if (Mathf.Abs(p.z - b) <= 1.75f && p.y > s + .6f) return true; return false; }
        static bool RiverInBand(Vector3 p) { return p.z >= 0 && p.z <= 720 && RiverLateral(p) < 0 && !RiverOnDeck(p); }
        static int RiverSideOf(Vector3 p) { return p.x >= KH_CourseGroundEditor.RiverAt(p.z).x ? 1 : -1; }

        static bool RiverTrySite(float z, int side, float outside, out Vector3 nav)
        {
            nav = Vector3.zero; var r = KH_CourseGroundEditor.RiverAt(z); float hw = KH_CourseGroundEditor.HalfWidth(z);
            RaycastHit g; NavMeshHit h;
            if (!Physics.Raycast(new Vector3(r.x + side * (hw + 1 + outside), r.y + 8, z), Vector3.down, out g, 30, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore)) return false;
            if (!NavMesh.SamplePosition(g.point, out h, 1f, NavMesh.AllAreas) || RiverLateral(h.position) < 3f) return false;
            nav = h.position; return true;
        }

        static void RiverPlace(RiverCase c)
        {
            Vector3 site = Vector3.zero, anchor = Vector3.zero; bool found = false;
            if (c.name == "w5-gather-stall") { StallPlace(c); return; }
            if (c.name == "w5-gather-far-bank")
            {
                // Near the z 190 bridge but clear of its deck, so the straight line crosses water and the NavMesh path uses the bridge.
                foreach (float z in new[] { 205f, 212f, 200f, 175f, 168f })
                    if (RiverTrySite(z, -1, 6, out site) && RiverTrySite(z, 1, 6, out anchor)) { c.z = z; c.side = -1; found = true; break; }
                if (!found) throw new InvalidOperationException("No west/east bank sites near the z 190 bridge");
                c.anchor = anchor;
            }
            else
            {
                // Away from the water-play spot (z 285). IMP-DOG-021 (2026-09-28): a ball anywhere else in the river now floats and is
                // fetched (DES-DOG-004 §E), so the "dog stays out of the band, the ball goes back to the bank" cases run where that is
                // still the rule: inside a bridge zone (bridge z +- 8 m), clear of the deck (+- 1.75 m). The criteria are unchanged.
                foreach (float z in new[] { 184f, 196f, 185f, 195f, 484f, 496f })
                {
                    foreach (int side in new[] { -1, 1 }) if (RiverTrySite(z, side, 5, out site)) { c.z = z; c.side = side; found = true; break; }
                    if (found) break;
                }
                if (!found) throw new InvalidOperationException("No bank site for the ball cases");
            }
            c.site = site;
            var facing = Quaternion.LookRotation(new Vector3(-c.side, 0, 0));
            Networking.LocalPlayer.TeleportTo(site, facing);
            NavMeshHit dog;
            if (!NavMesh.SamplePosition(site + new Vector3(c.side * 1.2f, 0, .8f), out dog, 2, NavMesh.AllAreas) || RiverInBand(dog.position)) throw new InvalidOperationException("Dog placement lacks NavMesh");
            roots[RiverDog].SetPositionAndRotation(dog.position, facing);
            var sync = roots[RiverDog].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
            c.dogStart = dog.position;
        }

        // freeze: hold the ball where it is placed (it would otherwise roll down the bank into the water and take the
        // "sunk" path, measured 2026-09-27 run 20260927-032848). KH_ToyRecall still moves it by transform; the harness
        // restores the constraints as soon as it sees the return (and Play exit discards them in any case).
        static void RiverPlaceBall(Vector3 p, bool freeze)
        {
            var ball = Get<Transform>(riverFetch[RiverDog], "_ballT");
            var sync = ball.GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.SetKinematic(false);
            var rb = ball.GetComponent<Rigidbody>();
            RiverUnfreezeBall();
            if (rb != null)
            {
                rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                if (freeze) { riverBallConstraints = rb.constraints; rb.constraints = RigidbodyConstraints.FreezeAll; riverBallFrozen = true; }
            }
            ball.position = p; if (sync != null) sync.FlagDiscontinuity();
            riverBallPlacedAt = p; riverWatchCase = -1;
        }

        static void RiverUnfreezeBall()
        {
            if (!riverBallFrozen) return;
            var rb = Get<Transform>(riverFetch[RiverDog], "_ballT").GetComponent<Rigidbody>();
            if (rb != null) rb.constraints = riverBallConstraints;
            riverBallFrozen = false;
        }

        static void RiverStart(RiverCase c, double now)
        {
            c.startedAt = now; riverPrevSign = RiverSideOf(roots[RiverDog].position);
            for (int i = 0; i < 6; i++) c.deepestInside[i] = float.MaxValue;
            if (c.name == "w5-gather-stall") { StallStart(c, now); return; }
            var r = KH_CourseGroundEditor.RiverAt(c.z); float hw = KH_CourseGroundEditor.HalfWidth(c.z);
            if (c.name == "w5-gather-far-bank")
            {
                // The host stands at the anchor. Gathering written as the authority would after accepting a request.
                var facing = Quaternion.LookRotation(new Vector3(-1, 0, 0));
                Networking.LocalPlayer.TeleportTo(c.anchor, facing);
                RiverWriteGathering(c.anchor, facing.eulerAngles.y); riverRefreshAt = now + 40;
                return;
            }
            if (c.name == "w5-ball-in-river") c.ball = new Vector3(r.x, r.y - .25f, c.z);
            else
            {
                float x = r.x + c.side * (hw + 1 - .6f); RaycastHit g;
                float ground = Physics.Raycast(new Vector3(x, r.y + 3, c.z), Vector3.down, out g, 6, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore) ? g.point.y : r.y;
                // Centre above both the ground and the water: not "sunk", so only the 10 s stranded rule can return it.
                c.ball = new Vector3(x, Mathf.Max(ground, r.y) + .043f + .03f, c.z);
            }
            RiverPlaceBall(c.ball, c.name == "w5-ball-band-edge");
            c.ballPlacedAt = now;
            riverFetch[RiverDog].SendCustomEvent("_OnBallThrown");
        }

        static void RiverWriteGathering(Vector3 anchor, float yaw)
        {
            var a = riverActivities;
            a.SetProgramVariable("participants", 1 << RiverDog);
            a.SetProgramVariable("formation", 0);
            a.SetProgramVariable("anchor", anchor);
            a.SetProgramVariable("yaw", yaw);
            a.SetProgramVariable("host", Networking.LocalPlayer.playerId);
            a.SetProgramVariable("startedMs", Networking.GetServerTimeInMilliseconds());
            a.SetProgramVariable("mode", 1);
            a.SetProgramVariable("revision", Get<int>(a, "revision") + 1);
        }

        static void RiverObserve(RiverCase c)
        {
            if (riverPhase < 4) return;
            c.frames++;
            for (int i = 0; i < 6; i++)
            {
                var p = roots[i].position; float lat = RiverLateral(p);
                if (p.z >= 0 && p.z <= 720 && !RiverOnDeck(p)) c.deepestInside[i] = Mathf.Min(c.deepestInside[i], lat);
                if (RiverInBand(p) && lat < -RiverTolerance) c.violationFrames[i]++;
            }
            var d = roots[RiverDog].position; int sign = RiverSideOf(d);
            if (sign != riverPrevSign) { if (RiverOnDeck(d)) c.crossedOnBridge = true; else c.crossingsOffBridge++; riverPrevSign = sign; }
        }

        static void RiverEvaluate(RiverCase c, double now)
        {
            double t = now - c.startedAt;
            if (c.name == "w5-gather-stall") { StallEvaluate(c, now, t); return; }
            if (c.name == "w5-gather-far-bank")
            {
                if (!c.joined && Get<bool>(wander[RiverDog], "_groupCalled")) { c.joined = true; c.joinedAt = t; }
                if (!c.joined && t > 6) throw new InvalidOperationException("Dog 0 did not join the gathering");
                if (now >= riverRefreshAt) { riverActivities.SetProgramVariable("startedMs", Networking.GetServerTimeInMilliseconds()); c.gatheringRefreshes++; riverRefreshAt = now + 40; }
                var d = roots[RiverDog].position; var flat = d - c.anchor; flat.y = 0;
                if (!c.arrived && flat.magnitude < 3f && RiverSideOf(d) == RiverSideOf(c.anchor)) { c.arrived = true; c.arrivalSeconds = t; }
                if (c.arrived || t > 120)
                {
                    riverActivities.SetProgramVariable("mode", 0);
                    riverActivities.SetProgramVariable("revision", Get<int>(riverActivities, "revision") + 1);
                    bool pass = c.joined && c.arrived && c.crossedOnBridge && c.crossingsOffBridge == 0 && c.violationFrames.Sum() == 0 && c.stallLogs == 0;
                    RiverFinish(c, now, pass, pass ? "joined, crossed on the bridge, arrived " + c.arrivalSeconds.ToString("F1") + " s, no stall latch" :
                        "joined=" + c.joined + " arrived=" + c.arrived + " bridge=" + c.crossedOnBridge + " offBridgeCrossings=" + c.crossingsOffBridge + " bandFrames=" + c.violationFrames.Sum() + " stallLogs=" + c.stallLogs);
                }
                return;
            }
            if (!c.fetchStarted && Get<int>(riverFetch[RiverDog], "dogState") != 0) { c.fetchStarted = true; c.fetchStartedAt = t; }
            var ball = Get<Transform>(riverFetch[RiverDog], "_ballT").position;
            if (!c.ballReturned && !RiverInBand(ball) && Vector3.Distance(ball, riverBallPlacedAt) > 1f)
            {
                RiverUnfreezeBall(); riverWatchCase = caseIndex;
                c.ballReturned = true; c.ballReturnSeconds = now - c.ballPlacedAt; c.ballReturnedTo = ball;
                c.ballReturnedOnSide = RiverSideOf(ball) == (int)c.side;
                NavMeshHit h; c.ballReturnedOnNav = NavMesh.SamplePosition(ball, out h, .6f, NavMesh.AllAreas);
            }
            // After the return the ball must stay out of the band (a ball that rolls back in loops through the river).
            if (c.ballReturned && !c.ballSleeping2s && now - c.ballPlacedAt - c.ballReturnSeconds >= 2) c.ballSleeping2s = RiverBallSleeping();
            bool edge = c.name == "w5-ball-band-edge";
            bool done = c.ballReturned && t - c.ballReturnSeconds > 6 && (!edge || c.riverGiveUps > 0);
            if (!done && t < 25) return;
            bool timing = c.ballReturned && (edge ? c.ballReturnSeconds >= 9.5 && c.ballReturnSeconds <= 11.5 : c.ballReturnSeconds <= 10);
            bool ok = c.violationFrames.Sum() == 0 && timing && c.ballReturnedOnSide && c.ballReturnedOnNav && !c.ballReentered && !c.log.Any(l => l.Contains("to=park")) && c.riverReturnLogs == 1 && (!edge || (c.fetchStarted && c.riverGiveUps > 0));
            RiverFinish(c, now, ok, "bandFrames=" + c.violationFrames.Sum() + " ballReturn=" + c.ballReturnSeconds.ToString("F2") + "s side=" + c.ballReturnedOnSide
                + " nav=" + c.ballReturnedOnNav + " fetchStarted=" + c.fetchStarted + " riverGiveUps=" + c.riverGiveUps + " returnLogs=" + c.riverReturnLogs + " reentered=" + c.ballReentered);
        }

        // ---- w5-gather-stall (DES-DOG-001, 2026-10-12) ----
        const int StallGroundMask = (1 << 0) | (1 << 11);
        const float StallMaxDeviation = .6f, StallSkipTurn = 1.5f;

        // Slot 0 of the line formation (formation 0) sits this far on the anchor's -right side: KD_GroupActivities.Offset(0, 0)
        // = RowX(0, 6, 0, gapRatio) = -(sum of the five neighbour steps) / 2, from the same roster radii and ratios.
        static float StallSlotLead()
        {
            float wr = Get<float>(riverActivities, "widthRatio"), gap = Get<float>(riverActivities, "gapRatio");
            float[] radii = riverPack != null ? Get<float[]>(riverPack, "dogRadius") : null;
            Func<int, float> width = k => (radii != null && k < radii.Length && radii[k] > .001f ? radii[k] : .25f) * wr;
            float total = 0f;
            for (int k = 0; k < 5; k++) total += (width(k) + width(k + 1)) * gap;
            return total * .5f;
        }

        // Dry enough for a straight walk: _BlockedByRiver stops a straight step at the band (centre ± (half width + 1 m)).
        static bool StallDry(Vector3 p) { return p.z < 0 || p.z > 720 || RiverLateral(p) >= 2f; }

        static float StallPathLength(NavMeshPath path)
        { float len = 0f; var c = path.corners; for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]); return len; }

        // A start S and a slot E beside the route: both on the NavMesh, the straight line S->E leaves the NavMesh (an obstacle),
        // the complete NavMesh path is >= 4 m longer (so a NavMesh walk is far off the line), no river band on the line or at
        // the anchor. Pass 0 also keeps the ground under the line within 2 m of it (a low obstacle: rock, fence, rail).
        static void StallPlace(RiverCase c)
        {
            if (riverRoute == null) throw new InvalidOperationException("KH_WalkRoute missing (its nodes seed the stall site search)");
            // Read like Walking/5 Verify reads the route's backing nodes (an Array of Transforms), not as an exact Transform[].
            var nodeArray = Get<Array>(riverRoute, "nodes");
            var nodes = new Transform[nodeArray.Length];
            for (int i = 0; i < nodes.Length; i++) nodes[i] = nodeArray.GetValue(i) as Transform;
            float lead = StallSlotLead();
            var path = new NavMeshPath();
            for (int pass = 0; pass < 2; pass++)
                foreach (var n in nodes)
                {
                    if (n == null) continue;
                    foreach (float len in new[] { 8f, 10f, 12f })
                        for (int a = 0; a < 16; a++)
                        {
                            var dir = Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward;
                            NavMeshHit hs, he, edge, hp;
                            if (!NavMesh.SamplePosition(n.position - dir * len * .5f, out hs, 1f, NavMesh.AllAreas)
                                || !NavMesh.SamplePosition(n.position + dir * len * .5f, out he, 1f, NavMesh.AllAreas)) continue;
                            Vector3 s = hs.position, e = he.position, flat = e - s; flat.y = 0f;
                            float straight = flat.magnitude;
                            if (straight < 6f) continue;
                            Vector3 d = flat / straight;
                            if (!NavMesh.Raycast(s, e, out edge, NavMesh.AllAreas)) continue;   // nothing between them
                            if (!NavMesh.CalculatePath(s, e, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                            float pathLength = StallPathLength(path);
                            if (pathLength < straight + 4f) continue;
                            bool ok = StallDry(s) && StallDry(e);
                            for (float t = 0f; t <= 1f && ok; t += .5f / straight)
                            {
                                var p = Vector3.Lerp(s, e, t);
                                if (!StallDry(p)) ok = false;
                                RaycastHit g;
                                if (ok && pass == 0 && Physics.Raycast(new Vector3(p.x, Mathf.Max(s.y, e.y) + 6f, p.z), Vector3.down, out g, 30f, StallGroundMask, QueryTriggerInteraction.Ignore)
                                    && Mathf.Abs(g.point.y - p.y) > 2f) ok = false;
                            }
                            if (!ok) continue;
                            RaycastHit ag;
                            Vector3 anchor = e + d * lead;   // slot 0 = anchor - d * lead = e (StallStart turns the formation's right to d)
                            if (!Physics.Raycast(anchor + Vector3.up * 3f, Vector3.down, out ag, 8f, StallGroundMask, QueryTriggerInteraction.Ignore) || !StallDry(ag.point)) continue;
                            if (!NavMesh.SamplePosition(s - d * 1.5f, out hp, 1f, NavMesh.AllAreas)) continue;   // the player stands behind the dog to attach
                            c.anchor = ag.point; c.slot = new Vector3(e.x, e.y, e.z); c.dogStart = s; c.site = hp.position;
                            c.straightLength = straight; c.pathLength = pathLength; c.z = s.z; c.side = RiverSideOf(s);
                            c.log.Add("site pass=" + pass + " node=" + n.name + " len=" + len + " dir=" + (a * 22.5f) + " straight=" + straight.ToString("F1") + " path=" + pathLength.ToString("F1")
                                + " edge=" + edge.position.ToString("F1") + " lead=" + lead.ToString("F2"));
                            var facing = Quaternion.LookRotation(d);
                            Networking.LocalPlayer.TeleportTo(c.site, facing);
                            roots[RiverDog].SetPositionAndRotation(s, facing);
                            var sync = roots[RiverDog].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
                            return;
                        }
                }
            throw new InvalidOperationException("No stall site: no route-side pair with an obstacle on the straight line, a complete path >= 4 m longer and no river band");
        }

        static void StallStart(RiverCase c, double now)
        {
            var d = c.slot - c.dogStart; d.y = 0f; d.Normalize();
            // The formation's right (Euler(0, yaw, 0) * right = (cos yaw, 0, -sin yaw)) along d puts slot 0 on the line at c.slot.
            float yaw = Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg;
            Networking.LocalPlayer.TeleportTo(c.anchor, Quaternion.LookRotation(-d));   // the host stands at the anchor
            riverPin = roots[RiverDog].position;
            RiverWriteGathering(c.anchor, yaw);
        }

        static void StallEvaluate(RiverCase c, double now, double t)
        {
            var w = wander[RiverDog]; var root = roots[RiverDog];
            if (!c.joined && Get<bool>(w, "_groupCalled")) { c.joined = true; c.joinedAt = t; }
            if (!c.joined && t > 6) throw new InvalidOperationException("Dog 0 did not join the gathering");
            if (!c.latched)
            {
                // _gatherPathOk is shared by every GatherStep caller and kept between calls; GatherStep re-plans every 0.5 s,
                // so it is sampled only after this gathering's own first re-plan.
                if (c.joined && t - c.joinedAt >= .6 && Get<bool>(w, "_gatherPathOk")) c.pathOkBeforeLatch = true;
                if (Get<bool>(w, "_gatherStraight"))
                {
                    c.latched = true; c.latchSeconds = t;
                    c.latchAfterWalk = Get<float>(w, "_gatherStraightAt") - Get<float>(w, "_gatherWalkSince");
                    c.releasePos = root.position; c.legTargetAtLatch = Get<Vector3>(w, "_legTarget");
                    return;
                }
                // Synthetic stall: hold the root where the gathering found it (the dog keeps deciding and stepping; each tick puts it back).
                var drift = root.position - riverPin; drift.y = 0f; c.maxPinDrift = Mathf.Max(c.maxPinDrift, drift.magnitude);
                root.position = riverPin; c.pinTicks++;
                if (t > 16) throw new InvalidOperationException("No stall latch 16 s after the gathering (pinDrift " + c.maxPinDrift.ToString("F2") + " m, pathOk " + c.pathOkBeforeLatch + ", stallLogs " + c.stallLogs + ")");
                return;
            }
            if (!c.ready)
            {
                // The line ends at the goal the dog itself walks to (the StepToward target recorded at the latch = Slot(0)), so a
                // mismatch in the harness's own slot estimate (c.slot) is a diagnostic in the reason, not a straightness failure.
                Vector3 a = c.releasePos, b = c.legTargetAtLatch, p = root.position; a.y = 0f; b.y = 0f; p.y = 0f;
                Vector3 ab = b - a; float len = ab.magnitude;
                if (len > .01f)
                {
                    Vector3 u = ab / len; float along = Vector3.Dot(p - a, u);
                    // The first metres are the turn from the NavMesh heading it had while held (turn radius ~0.46 m at the run speed).
                    if (along >= StallSkipTurn) c.maxDeviation = Mathf.Max(c.maxDeviation, along <= len ? (p - a - u * along).magnitude : (p - b).magnitude);
                }
                NavMeshHit h; if (!NavMesh.SamplePosition(root.position, out h, .3f, NavMesh.AllAreas)) c.offMeshFrames++;
                if (Get<bool>(w, "_groupReady")) { c.ready = true; c.readySeconds = t; }
                else if (t > 58) { StallFinish(c, now, "not ready at the slot 58 s after the gathering started"); }
                return;
            }
            if (!c.endRequested)
            {
                // The authority's own 60 s end: Mode turns 0 while mode is 1 -> EndGathering -> KH_LeashManager._OnGatherEnded.
                riverActivities.SetProgramVariable("startedMs", Networking.GetServerTimeInMilliseconds() - 61000);
                c.endRequested = true; c.endedSeconds = t;
                return;
            }
            if (!c.ended)
            {
                if (Get<int>(riverActivities, "mode") != 0) { if (t - c.endedSeconds > 3) StallFinish(c, now, "the authority did not end the gathering"); return; }
                c.ended = true;
                c.home = Get<Vector3[]>(manager, "releasedHomes")[RiverDog];
                var hd = c.home - root.position; hd.y = 0f; c.homeDistance = hd.magnitude;
                NavMeshHit hn; RaycastHit hg;
                c.homeOnGround = NavMesh.SamplePosition(c.home, out hn, .3f, NavMesh.AllAreas)
                    || Physics.Raycast(c.home + Vector3.up * .5f, Vector3.down, out hg, 1f, StallGroundMask, QueryTriggerInteraction.Ignore);
                StallFinish(c, now, null);
            }
        }

        static void StallFinish(RiverCase c, double now, string failure)
        {
            float window = Get<float>(manager, "gatherStallSeconds");
            bool latchOk = c.latchAfterWalk >= window - .05 && c.latchAfterWalk <= window + 1.0;
            bool pass = failure == null && c.joined && c.pathOkBeforeLatch && c.latched && latchOk && c.stallLogs == 1 && c.ready && c.readySeconds < 58
                && c.maxDeviation <= StallMaxDeviation && c.violationFrames.Sum() == 0 && c.ended && c.homeDistance >= 0f && c.homeDistance <= 1f && c.homeOnGround;
            if (Get<int>(riverActivities, "mode") != 0)
            { riverActivities.SetProgramVariable("mode", 0); riverActivities.SetProgramVariable("revision", Get<int>(riverActivities, "revision") + 1); }
            RiverFinish(c, now, pass, (failure != null ? failure + " | " : "") + "joined=" + c.joined + " pathOk=" + c.pathOkBeforeLatch + " latch=" + c.latchAfterWalk.ToString("F2") + "s (window " + window.ToString("F1")
                + ") stallLogs=" + c.stallLogs + " pinDrift=" + c.maxPinDrift.ToString("F2") + " ready=" + c.readySeconds.ToString("F1") + "s dev=" + c.maxDeviation.ToString("F2")
                + " offMesh=" + c.offMeshFrames + " straight=" + c.straightLength.ToString("F1") + "/path " + c.pathLength.ToString("F1") + " bandFrames=" + c.violationFrames.Sum()
                + " slotEstimateError=" + (c.latched ? Vector3.Distance(new Vector3(c.legTargetAtLatch.x, 0f, c.legTargetAtLatch.z), new Vector3(c.slot.x, 0f, c.slot.z)).ToString("F2") : "n/a") + "m"
                + " home=" + c.homeDistance.ToString("F2") + "m ground=" + c.homeOnGround);
        }

        static bool RiverBallSleeping()
        { var rb = Get<Transform>(riverFetch[RiverDog], "_ballT").GetComponent<Rigidbody>(); return rb != null && rb.IsSleeping(); }

        static void RiverLate(int index, string why)
        {
            var x = river.cases[index]; if (x.ballReentered) return;
            why += " sleeping=" + RiverBallSleeping();
            x.ballReentered = true;
            if (x.status == "PASS") { x.status = "FAIL"; x.reason += " | late: " + why; report.cases[index].status = "FAIL"; report.cases[index].reason = x.reason; }
            else if (x.status == "PENDING" || x.status == "RUNNING") x.reason = "late: " + why;
        }

        static void RiverFinish(RiverCase c, double now, bool pass, string reason)
        {
            RiverUnfreezeBall();
            var endBall = Get<Transform>(riverFetch[RiverDog], "_ballT").position;
            if (c.ballReturned) { c.ballSleepingAtEnd = RiverBallSleeping(); c.ballDriftAtEnd = Vector3.Distance(endBall, c.ballReturnedTo); reason += " sleep2s=" + c.ballSleeping2s + " sleepEnd=" + c.ballSleepingAtEnd + " drift=" + c.ballDriftAtEnd.ToString("F2"); }
            if (riverWatchCase >= 0 && riverWatchCase != caseIndex) c.watchedBallSleepingAtEnd = RiverBallSleeping();
            c.status = pass ? "PASS" : "FAIL"; c.reason = reason;
            Current.status = c.status; Current.reason = reason; Current.endedAt = now;
            SaveRiverDetail(null); Save();
            if (caseIndex + 1 < report.cases.Count) { Next(); riverPhase = 0; riverStep = 0; riverNext = 0; return; }
            riverWatchCase = -1;
            bool all = river.cases.All(x => x.status == "PASS");
            SaveRiverDetail(all ? "W5_PASS" : "W5_FAIL");
            End(all ? "W5_PASS" : "W5_FAIL", all ? "River band W5: " + river.cases.Count + " local case(s) passed (not network/device acceptance)" : "River band W5: see river-w5.json");
        }

        static void SaveRiverDetail(string status)
        {
            if (river == null || report == null) return;
            try
            {
                if (status != null) river.status = status;
                Directory.CreateDirectory(Dir + "/" + report.runId);
                File.WriteAllText(Dir + "/" + report.runId + "/river-w5.json", JsonUtility.ToJson(river, true));
            }
            catch (Exception e) { Debug.LogError("[KH Course Walk] River evidence write failed: " + e.Message); }
        }
    }
}
