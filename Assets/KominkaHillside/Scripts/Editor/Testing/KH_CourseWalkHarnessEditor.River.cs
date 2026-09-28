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
    //                          arrived (≤ 3 m from the anchor) within 120 s.
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
            public List<string> log = new List<string>();
        }
        [Serializable] public class RiverReport
        {
            public string runId, status = "RUNNING";
            public List<RiverCase> cases = new List<RiverCase>();
            public string limitations = "Single local ClientSim, dog 0. Throw = local _OnBallThrown after harness placement; gathering = synced heap written directly (menu permission/placement not exercised). Not network, Owner handover or device acceptance.";
        }
        static RiverReport river;
        static UdonBehaviour[] riverFetch;
        static UdonBehaviour riverActivities;
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
            report.riverOnly = true; report.deadline = EditorApplication.timeSinceStartup + 420; Save();
        }

        static void MakeRiverCases()
        {
            var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>();
            riverFetch = all.Where(u => u.programSource != null && u.programSource.name == "KD_DogFetchController").OrderBy(u => Get<int>(u, "dogIndex")).ToArray();
            if (riverFetch.Length != 6) throw new InvalidOperationException("Six fetch behaviours required");
            riverActivities = all.Single(u => u.programSource != null && u.programSource.name == "KD_GroupActivities");
            if (!all.Any(u => u.programSource != null && u.programSource.name == "KH_RiverData")) throw new InvalidOperationException("KH_RiverData missing: run Course/73 first");
            river = new RiverReport { runId = report.runId };
            foreach (var name in new[] { "w5-ball-in-river", "w5-ball-band-edge", "w5-gather-far-bank" })
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
                    bool pass = c.joined && c.arrived && c.crossedOnBridge && c.crossingsOffBridge == 0 && c.violationFrames.Sum() == 0;
                    RiverFinish(c, now, pass, pass ? "joined, crossed on the bridge, arrived " + c.arrivalSeconds.ToString("F1") + " s" :
                        "joined=" + c.joined + " arrived=" + c.arrived + " bridge=" + c.crossedOnBridge + " offBridgeCrossings=" + c.crossingsOffBridge + " bandFrames=" + c.violationFrames.Sum());
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
            End(all ? "W5_PASS" : "W5_FAIL", all ? "River band W5: three local cases passed (not network/device acceptance)" : "River band W5: see river-w5.json");
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
