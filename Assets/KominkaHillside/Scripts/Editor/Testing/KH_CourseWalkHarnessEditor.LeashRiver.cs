using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    // IMP-DOG-053 (DES-DOG-010 method B, 2026-10-12): the leashed river follow in the water harness (Course/78 run frame).
    //
    // Shared with the changed 78 / 88 cases (W4-leash / -stale / -late, S5b-sim): the water gate of the menu used to answer before the
    // distance gate, so those cases never had to stand near the wading dog. Now the attach is judged on its distance (attachReach 4 m):
    // WhStandNearDog puts the player 2 m from the dog toward the spot's entry (on the riverbed or the bank), and once the leash is
    // taken (WhLeashStage) WhStandOnBank moves the player to the spot's return point nearest the dog (3.5 m outside the band, facing
    // away from the river), so the hand is not in the band and the leash's river follow walks the dog out (Exit -> landed).
    //
    // Course/78l (waterPlaySet 2): the minimal dynamic cases of IMP-DOG-053 §3, one dog each, the others held out of the water. The
    // places are searched in Play mode on the KH_RiverData proxy (static baked data, as Course/82c's CwWetNow reads it) and written to
    // the case's lrSite. Every observation: a step that breaks the jump rule (WhJump) while the dog is wet or in the river follow
    // (pose != 0 or _lrState != 0) is a water jump (must be 0: no teleport from the water); on land it is counted apart (the leash's own
    // TeleportTo after landing is allowed). The mouth (KD_DogFetchController.mouthAnchor) above the water while wet.
    //   LR-swim-Corgi   dog 0 leashed on an ok bank slice; the player steps into the river (a point >= 0.3 m deep, <= 5 m from the
    //                   band edge, outside the spot and the bridge zones, facing the centre). Pass: the river follow starts (state >= 2),
    //                   the dog swims (pose 2) <= 25 s, floats still (pose 2, speed < 0.05) for 1 s, then the player steps back onto the
    //                   bank: walked out and landed (pose 0, state 0) <= 25 s on the same bank, no stage-3 recovery, no water jump, the
    //                   mouth >= MouthLowLimit above the water, the root never sunk into the riverbed while swimming (root - bed >= -0.04 m:
    //                   the designed float hangs the feet up to the draft below the water, and the leash river legs floor it at bed - 0.03 m;
    //                   was ">= 0.03 m" until run 20261012-030041, which found -0.155 m in the shallow tail of a swim).
    //   LR-wait-Lab     dog 2 (a non-swimmer) leashed on an ok slice 12-20 m below a bridge; the player steps into the river there, the
    //                   dog follows (state 2) <= 25 s; the player walks on to 4 m from the bridge (inside its no-entry zone, outside the
    //                   deck). Pass: the dog stops at the zone's edge (_lrWaiting, its z <= bridge - 8 + 0.3) <= 10 s and stays in the
    //                   follow (state 2, no walk out) for 3 s; then the player goes 14 m away onto the bank: the walk out starts <= 2 s,
    //                   the dog lands <= 25 s with no water jump and no stage-3 recovery, and is back within 1.25 x the leash <= 25 s
    //                   (a land TeleportTo is allowed). (A depth limit cannot be met in this scene: max 0.403 m < 0.85 M of the Lab /
    //                   Samoyed, Course/130; Course/131 probes that branch with an explicit limit.)
    //   LR-attach-swim  dog 0 free in the water-play spot sent to its deepest cell (the W1 debug entry) until it swims; the player
    //                   steps up (2 m) and attaches by the menu. Pass: accepted, "water leash taken" and "water end ... reason=leashed"
    //                   logged, the dog was afloat at the attach and for the next 1.5 s its root stays >= 0.03 m above the riverbed
    //                   (never dropped onto it), then (the player on the bank) it walks out and lands <= 20 s, no water jump.
    // Limits: one client (the dog Owner is the holder); no Owner change, late joiner, second holder or six dogs (PCVR checklist).
    public static partial class KH_CourseWalkHarnessEditor
    {
        static readonly string[] LrCaseNames = { "LR-swim-Corgi", "LR-wait-Lab", "LR-attach-swim" };
        static int LrCaseDog(string name) { return name == "LR-swim-Corgi" || name == "LR-attach-swim" ? 0 : 2; }   // W4-* / S5b-sim / LR-wait-Lab: dog 2
        // Course/78m (waterPlaySet 3): the water half of the leash river's limited run (Codex review 2026-10-12: 9 cases in all; the
        // other two, 82c C7-wet and 82b T-swimkeep-ticket-sim, are travel-mode cases: Course/82l below). Same preparation per case as
        // in 78 / 88 (phase 0 back on land, the play started, wet 1.5 s) and 78l.
        static readonly string[] LrLimitedWaterNames = { "W4-leash", "W4-leash-stale", "W4-leash-late", "S5b-sim", "LR-swim-Corgi", "LR-wait-Lab", "LR-attach-swim" };

        [MenuItem("Tools/KominkaHillside/Course/78m - Harness Leash River water (7 of 9 cases)")]
        public static void WaterLeashRiverLimited()
        {
            Begin(false);
            report.waterOnly = true; report.waterPlaySet = 3; report.deadline = EditorApplication.timeSinceStartup + 1100; Save();
        }

        // Course/82l (travel mode, travelCall + travelLeashRiver): the travel half: the 82c setup (dogs 0-2 on the left hand, one move),
        // the puppet (C0), C7-wet, T-swimkeep-ticket-sim, then the 82c cleanup (C11). The C7-wet site is searched in Edit mode first.
        [MenuItem("Tools/KominkaHillside/Course/82l - Harness Leash River travel (C7-wet, T-swimkeep; 2 of 9 cases)")]
        public static void TravelLeashRiverLimited()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start Course/82l from Edit mode");
            CwPrepare();
            BeginTravel(false, false, true);
            report.travelLeashRiver = true; report.deadline = EditorApplication.timeSinceStartup + 600; Save();
        }

        /// <summary>Course/82l's cases after the travel setup (MakeTravelCases): the same initialisation as MakeCallCases.</summary>
        static void MakeLeashRiverTravelCases()
        {
            tvNotice = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true).FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_TravelNotice");
            if (tvNotice == null) throw new InvalidOperationException("KH_TravelNotice not in the scene (run Course/85)");
            tvCallSeq = 100; tvRemote2 = null; tvCallFailed = false;
            CallSpawn();
            CallC7Wet();
            TvSwimKeep();
            CallCleanup();
        }
        const float LrLeash = 3f, LrBridgeZone = 8f;
        static KH_RiverData lrRiver;
        static Vector3 lrBank, lrHold, lrHold2, lrAway, lrPrev;
        static int lrSide, lrPrevFrame = -1, lrPrevPose, lrPrevState;
        static float lrPrevTime, lrBridge;
        static Transform lrMouth;

        // ============================================================ shared with W4-leash / -stale / -late, S5b-sim

        static float LrGround(Vector3 p)
        {
            float top = KH_CourseGroundEditor.RiverAt(p.z).y + 8f;
            var hits = Physics.RaycastAll(new Vector3(p.x, top, p.z), Vector3.down, 30f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore)
                .Where(h => roots == null || !roots.Any(r => r != null && h.transform.IsChildOf(r))).OrderBy(h => h.distance).ToArray();
            return hits.Length == 0 ? p.y : hits[0].point.y;
        }

        static Quaternion LrFace(Vector3 from, Vector3 to)
        {
            var d = to - from; d.y = 0f;
            return d.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(d) : Networking.LocalPlayer.GetRotation();
        }

        /// <summary>The player 2 m from the case's dog toward the spot's entry (within attachReach), on the ground there.</summary>
        static void WhStandNearDog()
        {
            var dp = roots[WhDog].position; var entry = Get<Vector3>(whSpot, "entry");
            var dir = entry - dp; dir.y = 0f; float d = dir.magnitude;
            var at = d > 2.2f ? dp + dir / d * 2f : entry;
            at.y = LrGround(at);
            Networking.LocalPlayer.TeleportTo(at, LrFace(at, dp));
        }

        /// <summary>The player on the spot's return point nearest the dog (on the NavMesh, 3.5 m outside the band), facing away from the
        /// river: the hand is not in the band, so the leash's river follow walks the dog out.</summary>
        static void WhStandOnBank()
        {
            var returns = Get<Vector3[]>(whSpot, "returns"); var dp = roots[WhDog].position;
            var at = returns.OrderBy(r => Flat(r - dp)).First();
            var centre = KH_CourseGroundEditor.RiverAt(at.z);
            Networking.LocalPlayer.TeleportTo(at, Quaternion.LookRotation(new Vector3(at.x >= centre.x ? 1f : -1f, 0f, 0f)));
        }

        /// <summary>Stage 0: wait for the leash (walkers = me, <= 3 s), then stand on the bank and count the landing from there (stage 1).
        /// Every call: a leash that was taken and then lost is recorded. True = this tick is handled.</summary>
        static bool WhLeashStage(WhCase c, double now, double t)
        {
            bool mine = Get<int[]>(manager, "walkers")[WhDog] == Networking.LocalPlayer.playerId;
            if (mine) c.accepted = true; else if (c.accepted && c.stage >= 1) c.lostLeash = true;
            if (c.stage != 0) return false;
            if (!c.accepted)
            {
                if (t > 3) WhFinish(c, now, false, "the leash was not taken up within 3 s (walkers[" + WhDog + "]=" + Get<int[]>(manager, "walkers")[WhDog] + ", river state " + WhLr() + ")" + WhSummary(c));
                return true;
            }
            // F4 (Codex review 2026-10-12): the leash's time in its own field. c.done is S5b-sim's "the ball called again after landing"
            // (Water.cs), which a shared write here made unreachable
            WhStandOnBank(); c.landed = -1; c.stage = 1; c.lrAttachAt = t;
            return true;
        }

        static string WhLeashNote(WhCase c)
        {
            return "accepted=" + c.accepted + " kept=" + !c.lostLeash + " leashTaken=" + c.leashTakenLogged + " playEndedLeashed=" + c.leashedEndLogged + " riverExit=" + c.lrExitLogged
                + " spotExit=" + c.lrExitSpot + " veto=" + c.vetoLogged + "/" + c.vetoReleasedLogged + " exit=" + c.exitStarted.ToString("F2") + " landed=" + c.landed.ToString("F2");
        }

        // ============================================================ 78l LR-*

        static KH_RiverData LrRiverData()
        {
            if (lrRiver != null) return lrRiver;
            var n = GameObject.Find("KH_Dogs/KH_RiverData");
            lrRiver = n == null ? null : n.GetComponent<KH_RiverData>();
            if (lrRiver == null || !lrRiver._FetchReady()) throw new InvalidOperationException("KH_Dogs/KH_RiverData (river-wide data, Course/90) missing");
            return lrRiver;
        }

        static float LrDepth(Vector3 p) { return LrRiverData()._DepthAt(p); }

        /// <summary>From the bank point toward the centre (0.25 m steps, at most maxIn m past the band edge): the first point in the
        /// river-wide water at least minDepth deep. False = none.</summary>
        static bool LrMarch(Vector3 bank, int side, float minDepth, float maxIn, out Vector3 at)
        {
            var river = LrRiverData(); at = bank;
            float edge = Mathf.Abs(bank.x - river._CenterX(bank.z)) - (river._HalfWidth(bank.z) + KH_RiverData.BandMargin);
            for (float o = .25f; o <= edge + maxIn + 1e-3f; o += .25f)
            {
                var q = new Vector3(bank.x - side * o, 0f, bank.z);
                if (!river._FetchWater(q)) continue;
                float d = river._DepthAt(q);
                if (d < minDepth) continue;
                q.y = river._BedAt(q); at = q; return true;
            }
            return false;
        }

        static bool LrSpotZone(float z) { return z > 265f && z < 315f; }   // the water-play spot (z 278-301) and 13 m around it

        /// <summary>LR-swim: an ok slice outside the spot and the bridge zones (+-12 m) whose way in reaches 0.3 m within 5 m.</summary>
        static bool LrSiteSwim(out string why)
        {
            var river = LrRiverData();
            for (float z = 60f; z <= 660f; z += 3f)
            {
                if (RwBridgeZone(z, 12f) || LrSpotZone(z)) continue;
                foreach (int side in new[] { 1, -1 })
                {
                    var probe = river._BankPoint(new Vector3(0f, 0f, z), side, KH_RiverData.BandMargin + KH_RiverData.BankExtra);
                    int code = river._NearestSlice(probe, side, 1.5f, -1);
                    if (code < 0) continue;
                    var bank = river._SliceBank(code); NavMeshHit h;
                    if (!NavMesh.SamplePosition(bank, out h, .5f, NavMesh.AllAreas)) continue;
                    Vector3 deep;
                    if (!LrMarch(bank, side, .3f, 5f, out deep)) continue;
                    lrBank = h.position; lrHold = deep; lrSide = side;
                    why = "slice " + code + " side " + side + " bank " + lrBank.ToString("F2") + " holder in the water " + deep.ToString("F2") + " depth " + LrDepth(deep).ToString("F3");
                    return true;
                }
            }
            why = "no ok slice with 0.3 m of water within 5 m of the band edge (z 60-660, off the spot and the bridges)";
            return false;
        }

        /// <summary>LR-wait: an ok slice 12-20 m below a bridge, the player's two places (in the water there, then 4 m from the bridge at the
        /// same offset from the centre) and the bank 14 m below for the leaving.</summary>
        static bool LrSiteBridge(out string why)
        {
            var river = LrRiverData();
            foreach (float b in RiverBridges)
                foreach (int side in new[] { 1, -1 })
                    for (float z = b - 12f; z >= b - 20f; z -= 1f)
                    {
                        var probe = river._BankPoint(new Vector3(0f, 0f, z), side, KH_RiverData.BandMargin + KH_RiverData.BankExtra);
                        int code = river._NearestSlice(probe, side, 1f, -1);
                        if (code < 0) continue;
                        var bank = river._SliceBank(code); NavMeshHit h;
                        if (bank.z > b - 11f || !NavMesh.SamplePosition(bank, out h, .5f, NavMesh.AllAreas)) continue;
                        Vector3 wade;
                        if (!LrMarch(bank, side, .1f, 4f, out wade)) continue;
                        float off = wade.x - river._CenterX(wade.z);
                        var zone = new Vector3(river._CenterX(b - 4f) + off, 0f, b - 4f);
                        float zb = river._BedAt(zone);
                        if (zb <= KH_RiverData.NoBed + 1f) zone.y = KH_CourseGroundEditor.RiverAt(zone.z).y - .2f; else zone.y = zb;
                        if (!river._InBand(zone, KH_RiverData.BandMargin)) continue;
                        var awayProbe = river._BankPoint(new Vector3(0f, 0f, bank.z - 14f), side, KH_RiverData.BandMargin + KH_RiverData.BankExtra);
                        int awayCode = river._NearestSlice(awayProbe, side, 3f, -1);
                        NavMeshHit ah;
                        if (awayCode < 0 || !NavMesh.SamplePosition(river._SliceReturn(awayCode), out ah, 1f, NavMesh.AllAreas)) continue;
                        lrBank = h.position; lrHold = wade; lrHold2 = zone; lrAway = ah.position; lrSide = side; lrBridge = b;
                        why = "bridge " + b + " side " + side + ": slice " + code + " bank " + lrBank.ToString("F2") + ", in the water " + wade.ToString("F2") + ", in the zone " + zone.ToString("F2")
                            + ", away " + lrAway.ToString("F2") + " (slice " + awayCode + ")";
                        return true;
                    }
            why = "no ok slice 12-20 m below a bridge with water within 4 m and a bank 14 m below";
            return false;
        }

        /// <summary>Every tick of an LR case after the setup: the jump rule split by wet / dry, the mouth, the root over the riverbed.</summary>
        static void LrObserve(WhCase c)
        {
            var p = roots[WhDog].position; int pose = WhPose(), st = WhLr();
            c.frames++;
            if (lrPrevFrame >= 0)
            {
                string how; float jump = WhJump(WhDog, lrPrev, p, lrPrevFrame, lrPrevTime, out how);
                if (jump > 0f)
                {
                    bool water = lrPrevPose != 0 || lrPrevState != 0 || pose != 0;
                    if (water) { c.waterJumps++; c.bigSteps++; } else c.landTeleports++;
                    if (c.log.Count < 40) c.log.Add((water ? "WATER jump " : "land jump ") + jump.ToString("F2") + " m at " + p.ToString("F2") + " " + how + " state " + lrPrevState + "->" + st);
                }
            }
            lrPrev = p; lrPrevFrame = Time.frameCount; lrPrevTime = Time.time; lrPrevPose = pose; lrPrevState = st;
            if (st > c.lrMaxState) c.lrMaxState = st;
            float surface = KH_CourseGroundEditor.RiverAt(p.z).y;
            if (pose != 0 && lrMouth != null) c.minMouthLR = Mathf.Min(c.minMouthLR, lrMouth.position.y - surface);
            float bed = LrRiverData()._BedAt(p);
            if (bed <= KH_RiverData.NoBed + 1f) return;
            float lift = p.y - bed, depth = LrRiverData()._DepthAt(p);
            if (c.name == "LR-attach-swim")
            {
                // F5 (Codex review 2026-10-12): after the attach, independent of the pose (a sink writes pose 1 and _waterSwimming false
                // together): wherever the water is deeper than this dog's swim start (+ 0.02 m) the root must float above the riverbed
                // and the swim flag must hold. The first such frame is kept (droppedToBed is a history, never cleared)
                // 2026-10-12 (78m run 20261012-030041): "afloat" = the root at the designed float height (LrFloatY: water + swimClear - Ms -
                // sink, never below riverbed - 0.03 m), not "0.03 m above the riverbed" - the designed float hangs the feet up to the
                // draft (Corgi 0.175 m) below the water, i.e. below the riverbed wherever the water is shallower than the draft
                if (c.lrAttachAt >= 0 && c.lrLandedAt < 0 && depth >= lrSwimEnter + .02f)
                {
                    c.minLift = Mathf.Min(c.minLift, lift);
                    bool swimming = Get<bool>(wander[WhDog], "_waterSwimming");
                    float want = LrFloatY(WhDog, p);
                    if ((p.y < want - .03f || !swimming) && !c.droppedToBed)
                    {
                        c.droppedToBed = true;
                        if (c.log.Count < 40) c.log.Add("DROPPED at " + p.ToString("F2") + " depth " + depth.ToString("F3") + " lift " + lift.ToString("F3") + " float " + (p.y - want).ToString("F3") + " swimming " + swimming + " pose " + pose);
                    }
                }
                return;
            }
            if (pose == 2 && lift < c.minLift)
            {
                // where the lowest root was seen (the leash river state: 1 approach / 2 follow / 3 exit; the depth there)
                c.minLift = lift;
                if (lift < -.02f && c.log.Count < 40) c.log.Add("low root " + lift.ToString("F3") + " m under the riverbed at " + p.ToString("F2") + " depth " + depth.ToString("F3") + " river state " + st + " float error " + (p.y - LrFloatY(WhDog, p)).ToString("F3"));
            }
        }

        /// <summary>The root height the runtime holds an afloat dog at (WaterSwimHeight: water + swimClear - Ms - the dog's swimSink, settled),
        /// with the leash river's floor (KD_DogWanderController.LeashRiverSwimFloor: never below the riverbed - 0.03 m). From the scene
        /// proxies (static tuning), so it works in the travel mode too (whSpot is bound by the water mode only).</summary>
        static float LrFloatY(int dog, Vector3 p)
        {
            var sn = GameObject.Find("KH_Dogs/KH_WaterSpot"); var spot = sn == null ? null : sn.GetComponent<KH_WaterSpot>();
            if (spot == null) throw new InvalidOperationException("KH_Dogs/KH_WaterSpot missing");
            var river = LrRiverData();
            float sink = spot.swimSink != null && dog < spot.swimSink.Length ? spot.swimSink[dog] : 0f;
            float y = river._SurfaceY(p.z) + spot.swimClear - Get<float>(wander[dog], "waterSwimMouth") - sink;
            float bed = river._BedAt(p);
            return bed > KH_RiverData.NoBed + 1f ? Mathf.Max(y, bed - LrSwimFloorM) : y;
        }

        const float LrSwimFloorM = .03f;   // = KD_DogWanderController.LrSwimFloor

        static float lrSwimEnter;

        /// <summary>The dog's swim start depth as the runtime computes it (WaterSwimEnterDepth: min(swimEnterRatio x M, Ms - wadeClear)).</summary>
        static float LrSwimEnterDepth(int dog)
        {
            float m = Get<float>(wander[dog], "waterMouthHeight"), ms = Get<float>(wander[dog], "waterSwimMouth");
            return Mathf.Min(Get<float>(whSpot, "swimEnterRatio") * m, ms - Get<float>(whSpot, "wadeClear"));
        }

        static void LrBeginObserve()
        {
            lrPrevFrame = -1; object bone; lrMouth = null;
            lrSwimEnter = LrSwimEnterDepth(WhDog);
            if (whFetches[WhDog].TryGetProgramVariable("mouthAnchor", out bone)) lrMouth = bone as Transform;
        }

        static void LrTick(WhCase c, double now)
        {
            if (whPhase == 0 && whStep == 0) { WhDog = c.dog; whFetch = whFetches[WhDog]; lrRiver = null; }
            if (whPhase >= 3) LrObserve(c);
            if (now < whNext) return;
            double t = now - whPhaseAt;
            var me = Networking.LocalPlayer.playerId;
            switch (whPhase)
            {
                case 0:   // everyone off the leash, out of the water, no new play; the case's dog dry and idle
                    if (whStep < 6) { pouch.SendCustomEvent("_WalkOff" + whStep++); whNext = now + .3; return; }
                    for (int i = 0; i < 6; i++)
                    {
                        wander[i].SetProgramVariable("_waterNextRoll", Time.time + 600f); wander[i].SetProgramVariable("_waterFirstUsed", true);
                        wander[i].SetProgramVariable("_waterFirstDue", 0f);
                        if (Get<int>(wander[i], "waterState") != 0) wander[i].SetProgramVariable("_waterUntil", 0f);
                    }
                    if (WhPose() != 0 || WhState() != 0 || Get<int>(whFetch, "dogState") != 0 || WhLr() != 0)
                    { if (now - Current.startedAt > 45) throw new TimeoutException(c.name + ": dog " + WhDog + " not back on land / idle"); whNext = now + .5; return; }
                    whPhase = 1; whPhaseAt = now; return;
                case 1:   // the place, then the dog
                {
                    string why;
                    if (c.name == "LR-attach-swim")
                    {
                        // free in the spot: the first play of the stay starts by itself (W0), then the W1 debug entry sends it deep
                        var entry = Get<Vector3>(whSpot, "entry");
                        var nodePos = Get<Vector3[]>(route, "nodePositions");
                        var pass = nodePos[Get<int[]>(route, "destinationNodes")[5]]; NavMeshHit hm;
                        if (!NavMesh.SamplePosition(Vector3.Lerp(pass, entry, .5f), out hm, 3, NavMesh.AllAreas)) throw new InvalidOperationException("No NavMesh between pass point and entry");
                        Networking.LocalPlayer.TeleportTo(hm.position, LrFace(hm.position, entry));
                        NavMeshHit d; if (!NavMesh.SamplePosition(hm.position + LrFace(hm.position, entry) * new Vector3(-.8f, 0, .3f), out d, 2, NavMesh.AllAreas)) throw new InvalidOperationException("Dog placement lacks NavMesh");
                        roots[WhDog].SetPositionAndRotation(d.position, LrFace(d.position, entry));
                        var sy = roots[WhDog].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sy != null) sy.FlagDiscontinuity();
                        wander[WhDog].SetProgramVariable("_waterFirstUsed", false); wander[WhDog].SetProgramVariable("_waterFirstDue", 0f);
                        c.lrSite = "the water-play spot (deepest cell " + WhCellBy(true).ToString("F2") + ")";
                        whPhase = 2; whPhaseAt = now; return;
                    }
                    bool found = c.name == "LR-wait-Lab" ? LrSiteBridge(out why) : LrSiteSwim(out why);
                    c.lrSite = why;
                    if (!found) { WhFinish(c, now, false, "SETUP_FAIL: " + why); return; }
                    holder = lrBank;
                    Networking.LocalPlayer.TeleportTo(lrBank, LrFace(lrBank, lrHold));
                    PlaceDog(WhDog, Networking.LocalPlayer.GetRotation());
                    pouch.SendCustomEvent("_WalkLeft" + WhDog);
                    whPhase = 2; whPhaseAt = now; whNext = now + 1; return;
                }
                case 2:   // leashed beside the player on the bank (LR-attach-swim: swimming in the spot)
                    if (c.name == "LR-attach-swim")
                    {
                        if (WhState() == 2 && WhPose() >= 1 && c.stage == 0) { WhSetTargetFor(WhDog, WhCellBy(true)); c.stage = 1; }
                        if (WhPose() == 2)
                        {
                            WhStandNearDog(); pouch.SendCustomEvent("_WalkLeft" + WhDog);
                            c.lrAttachAt = -1; c.swam = true; c.stage = 0; LrBeginObserve();   // F5: the attach time is set when the leash is seen
                            whPhase = 3; whPhaseAt = now; return;
                        }
                        if (t > 40) { WhFinish(c, now, false, "SETUP_FAIL: the free dog did not swim in the spot within 40 s (state " + WhState() + ", pose " + WhPose() + ")"); return; }
                        return;
                    }
                    if (Get<int[]>(manager, "walkers")[WhDog] != me) { if (t > 5) throw new InvalidOperationException(c.name + ": menu attach rejected on the bank"); return; }
                    if (Flat(roots[WhDog].position - Networking.LocalPlayer.GetPosition()) > 2.5f) { if (t > 15) throw new TimeoutException(c.name + ": the dog did not come to the bank"); return; }
                    Networking.LocalPlayer.TeleportTo(lrHold, LrFace(lrHold, new Vector3(KH_CourseGroundEditor.RiverAt(lrHold.z).x, lrHold.y, lrHold.z + (c.name == "LR-wait-Lab" ? 4f : 0f))));
                    LrBeginObserve(); c.stage = 0;
                    whPhase = 3; whPhaseAt = now; return;
                case 3:
                    if (c.name == "LR-swim-Corgi") LrSwim(c, now, t);
                    else if (c.name == "LR-wait-Lab") LrWait(c, now, t);
                    else LrAttach(c, now, t);
                    return;
            }
        }

        static void LrSwim(WhCase c, double now, double t)
        {
            int pose = WhPose(), st = WhLr();
            if (c.stage == 0)
            {
                if (c.lrEnteredAt < 0 && st >= 2) c.lrEnteredAt = t;
                if (c.lrSwamAt < 0 && pose == 2) { c.lrSwamAt = t; c.swam = true; }
                if (pose == 2 && Get<float>(wander[WhDog], "_speed") < .05f) { if (c.stillSince < 0f) c.stillSince = (float)t; else if (t - c.stillSince >= 1.0) c.floatStill = true; }
                else c.stillSince = -1f;
                if (c.swam && c.floatStill && t - c.lrSwamAt >= 3)
                {
                    Networking.LocalPlayer.TeleportTo(lrBank, Quaternion.LookRotation(new Vector3(lrSide, 0f, 0f)));   // back on the bank, facing away
                    c.lrLeftAt = t; c.stage = 1; return;
                }
                // F6 (Codex review 2026-10-12): the swim start is judged on its own deadline (25 s), then float-still by 30 s
                if (!c.swam && t > 25) { WhFinish(c, now, false, "no swim within 25 s of the player stepping in: river state max " + c.lrMaxState + LrNote(c)); return; }
                if (t > 30) { WhFinish(c, now, false, "no float-still within 30 s: swam at " + c.lrSwamAt.ToString("F2") + ", floatStill " + c.floatStill + LrNote(c)); return; }
                return;
            }
            if (c.lrExitAt < 0 && st == 3) c.lrExitAt = t;
            if (pose == 0 && st == 0)
            {
                if (c.lrLandedAt < 0) c.lrLandedAt = t;
                var p = roots[WhDog].position;
                c.landedSameSide = RiverSideOf(p) == lrSide && RiverLateral(p) >= 0f;
                if (t - c.lrLandedAt < 1) return;
                bool ok = c.lrMaxState >= 2 && c.swam && c.lrSwamAt >= 0 && c.lrSwamAt <= 25 && c.floatStill && c.landedSameSide && c.lrLandedAt - c.lrLeftAt <= 25 && !c.lrRecover3Logged && c.waterJumps == 0
                    && (lrMouth == null || c.minMouthLR >= MouthLowLimit) && c.minLift >= -(LrSwimFloorM + .01f) && Get<int[]>(manager, "walkers")[WhDog] == Networking.LocalPlayer.playerId;
                WhFinish(c, now, ok, "entered=" + c.lrEnteredAt.ToString("F2") + " swam=" + c.lrSwamAt.ToString("F2") + " floatStill=" + c.floatStill + " left=" + c.lrLeftAt.ToString("F2")
                    + " exit=" + c.lrExitAt.ToString("F2") + " landed=" + c.lrLandedAt.ToString("F2") + " sameBank=" + c.landedSameSide + LrNote(c)); return;
            }
            if (t - c.lrLeftAt > 25) WhFinish(c, now, false, "not landed within 25 s of the player leaving (state " + st + ", pose " + pose + ")" + LrNote(c));
        }

        static void LrWait(WhCase c, double now, double t)
        {
            int pose = WhPose(), st = WhLr();
            var p = roots[WhDog].position;
            if (c.stage == 0)   // into the river beside the player
            {
                if (c.lrEnteredAt < 0 && st == 2 && pose >= 1) c.lrEnteredAt = t;
                if (c.lrEnteredAt >= 0 && t - c.lrEnteredAt >= 2)
                {
                    Networking.LocalPlayer.TeleportTo(lrHold2, LrFace(lrHold2, p));   // into the bridge's zone (outside the deck)
                    c.stage = 1; c.lrWaitAt = t; return;
                }
                if (t > 25) WhFinish(c, now, false, "the non-swimmer did not follow into the river within 25 s (state " + st + ", pose " + pose + ")" + LrNote(c));
                return;
            }
            if (c.stage == 1)   // waits at the zone's edge, still following (not a stall)
            {
                bool waiting = Get<bool>(wander[WhDog], "_lrWaiting");
                bool atEdge = p.z <= lrBridge - LrBridgeZone + .3f;
                if (st == 3) c.walkOutDuringWait = true;   // a walk out while waiting at the limit = the wait counted as a stall
                if (waiting && atEdge && st == 2) { if (c.stillSince < 0f) c.stillSince = (float)t; else if (t - c.stillSince >= 3) { c.waited = true; } }
                else c.stillSince = -1f;
                if (c.waited)
                {
                    Networking.LocalPlayer.TeleportTo(lrAway, Quaternion.LookRotation(new Vector3(lrSide, 0f, 0f)));   // 14 m away on the bank
                    c.stage = 2; c.lrLeftAt = t; return;
                }
                if (t - c.lrWaitAt > 10) WhFinish(c, now, false, "did not wait at the zone's edge within 10 s (waiting " + waiting + ", z " + p.z.ToString("F2") + " vs edge " + (lrBridge - LrBridgeZone).ToString("F1") + ", state " + st + ")" + LrNote(c));
                return;
            }
            if (c.lrExitAt < 0 && st == 3) c.lrExitAt = t;
            if (c.lrLandedAt < 0 && pose == 0 && st == 0) c.lrLandedAt = t;
            if (c.lrLandedAt >= 0 && c.lrCaughtUpAt < 0 && Flat(p - Networking.LocalPlayer.GetPosition()) <= LrLeash * 1.25f) c.lrCaughtUpAt = t;
            if (c.lrCaughtUpAt >= 0)
            {
                bool ok = c.lrEnteredAt >= 0 && c.waited && !c.walkOutDuringWait && c.lrExitAt >= 0 && c.lrExitAt - c.lrLeftAt <= 2 && c.lrLandedAt - c.lrLeftAt <= 25 && c.lrCaughtUpAt - c.lrLandedAt <= 25
                    && c.waterJumps == 0 && !c.lrRecover3Logged && (lrMouth == null || c.minMouthLR >= MouthLowLimit);
                WhFinish(c, now, ok, "entered=" + c.lrEnteredAt.ToString("F2") + " waited=" + c.waited + " (edge z " + (lrBridge - LrBridgeZone).ToString("F1") + ") left=" + c.lrLeftAt.ToString("F2")
                    + " exit=" + c.lrExitAt.ToString("F2") + " landed=" + c.lrLandedAt.ToString("F2") + " caughtUp=" + c.lrCaughtUpAt.ToString("F2") + " walkOutDuringWait=" + c.walkOutDuringWait + LrNote(c)); return;
            }
            if (t - c.lrLeftAt > 50) WhFinish(c, now, false, "not landed / caught up within 50 s of the player leaving (state " + st + ", pose " + pose + ")" + LrNote(c));
        }

        static void LrAttach(WhCase c, double now, double t)
        {
            int pose = WhPose(), st = WhLr();
            bool mine = Get<int[]>(manager, "walkers")[WhDog] == Networking.LocalPlayer.playerId;
            if (c.stage == 0)
            {
                if (!mine) { if (t > 3) WhFinish(c, now, false, "the menu attach to the swimming dog was not taken within 3 s" + LrNote(c)); return; }
                c.accepted = true; c.lrAttachAt = t;
                c.droppedToBed = !Get<bool>(wander[WhDog], "_waterSwimming");   // afloat on the frame the leash is seen
                c.stage = 1; return;
            }
            if (c.stage == 1)
            {
                // F5: the sink is watched by LrObserve from the depth and the riverbed (not the pose), until the landing
                if (t - c.lrAttachAt < 1.5) return;
                WhStandOnBank(); c.stage = 2; c.lrLeftAt = t; return;
            }
            if (!mine) c.lostLeash = true;
            if (pose == 0 && st == 0)
            {
                if (c.lrLandedAt < 0) c.lrLandedAt = t;
                if (t - c.lrLandedAt < 1) return;
                bool ok = c.accepted && !c.lostLeash && c.leashTakenLogged && c.leashedEndLogged && !c.droppedToBed && c.minLift >= -(LrSwimFloorM + .01f) && c.lrLandedAt - c.lrLeftAt <= 20
                    && c.waterJumps == 0 && !c.lrRecover3Logged && !c.vetoLogged;
                WhFinish(c, now, ok, "attached=" + c.lrAttachAt.ToString("F2") + " leashTaken=" + c.leashTakenLogged + " playEndedLeashed=" + c.leashedEndLogged + " droppedToBed=" + c.droppedToBed
                    + " left=" + c.lrLeftAt.ToString("F2") + " landed=" + c.lrLandedAt.ToString("F2") + LrNote(c)); return;
            }
            if (t - c.lrLeftAt > 20) WhFinish(c, now, false, "not landed within 20 s of the player leaving (state " + st + ", pose " + pose + ")" + LrNote(c));
        }

        // ============================================================ 82b T-swimkeep-ticket-sim (IMP-DOG-053 §2, the reverse of T-swimresidue-*)

        /// <summary>Course/82b, after T-swimresidue-*: a legitimate float in the water (dog 0, a Corgi, swimming on the leash in the river
        /// follow) is NOT dropped by a ticket apply that does not move the dog (the dog within 1.25 x the leash of the hand). The ticket is
        /// written into the arbiter's synced heap (simulated: phase Moved, this holder, now, the holder's position as the landing centre),
        /// standing in for a warp whose landing is beside the dog. Pass: the dog applies it once (_travelApplies + 1) without a move
        /// (_travelMoves unchanged), and for 1 s after it is still afloat (_waterSwimming, waterPose 2, the river follow 2, the root >= 0.03 m
        /// above the riverbed). Then the heap ticket is cleared and the player steps back onto the bank until dog 0 has landed.</summary>
        static void TvSwimKeep()
        {
            const string tag = "T-swimkeep-ticket-sim";
            var s = TvCase(tag, true, 1);
            int applies0 = 0, moves0 = 0, req = 0; double appliedAt = -1;
            string why = null;
            s.Add(TvCooldown());
            s.Add(TvDo(() =>
            {
                lrRiver = null;
                if (!LrSiteSwim(out why)) throw new InvalidOperationException(tag + ": SETUP_FAIL: " + why);
                holder = lrBank;
                Networking.LocalPlayer.TeleportTo(lrBank, LrFace(lrBank, lrHold));
                if (Get<int[]>(manager, "walkers")[0] != TvMe()) { PlaceDog(0, Networking.LocalPlayer.GetRotation()); pouch.SendCustomEvent("_WalkLeft0"); }
                TvNote("site: " + why);
            }));
            s.Add(TvUntil(() => Get<int[]>(manager, "walkers")[0] == TvMe(), 5, tag + ": dog 0 not leashed to me"));
            s.Add(TvUntil(() => TvFlat(roots[0].position, Networking.LocalPlayer.GetPosition()) < 3f, 30, tag + ": dog 0 did not come to the bank"));
            s.Add(TvDo(() =>
            {
                var c = KH_CourseGroundEditor.RiverAt(lrHold.z);
                Networking.LocalPlayer.TeleportTo(lrHold, Quaternion.LookRotation(new Vector3(c.x - lrHold.x, 0f, 0f)));
            }));
            s.Add(TvUntil(() => CwPose(0) == 2 && Get<int>(wander[0], "_lrState") == 2 && Get<bool>(wander[0], "_waterSwimming"), 30,
                tag + ": dog 0 did not swim on the leash in the river within 30 s" + CwDogState(0)));
            s.Add(TvUntil(() => TvFlat(roots[0].position, Networking.LocalPlayer.GetPosition()) < 3f, 10, tag + ": dog 0 not within 3 m of the holder (a ticket would move it)"));
            s.Add(TvDo(() =>
            {
                applies0 = Get<int>(wander[0], "_travelApplies"); moves0 = Get<int>(wander[0], "_travelMoves");
                req = 0x7FFF0000 + (Time.frameCount & 0xFFFF);
                var rq = (int[])Get<int[]>(manager, "travelReq").Clone(); rq[0] = req; manager.SetProgramVariable("travelReq", rq);
                var hd = (int[])Get<int[]>(manager, "travelHolder").Clone(); hd[0] = TvMe(); manager.SetProgramVariable("travelHolder", hd);
                var ce = (Vector3[])Get<Vector3[]>(manager, "travelCenter").Clone(); ce[0] = Networking.LocalPlayer.GetPosition(); manager.SetProgramVariable("travelCenter", ce);
                var pt = (Vector3[])Get<Vector3[]>(manager, "travelPoint").Clone(); pt[0] = Networking.LocalPlayer.GetPosition(); manager.SetProgramVariable("travelPoint", pt);
                var at = (int[])Get<int[]>(manager, "travelAtMs").Clone(); at[0] = Networking.GetServerTimeInMilliseconds(); manager.SetProgramVariable("travelAtMs", at);
                var ph = (int[])Get<int[]>(manager, "travelPhase").Clone(); ph[0] = KH_LeashManager.TravelPhaseMoved; manager.SetProgramVariable("travelPhase", ph);
                TvNote("simulated: ticket " + req + " (phase Moved, holder me, centre = the holder) written for dog 0 swimming " + TvFlat(roots[0].position, Networking.LocalPlayer.GetPosition()).ToString("F2") + " m from the holder" + CwDogState(0));
            }));
            s.Add(TvUntil(() => { if (Get<int>(wander[0], "_travelApplies") - applies0 >= 1) { appliedAt = EditorApplication.timeSinceStartup; return true; } return false; }, 3, tag + ": dog 0 did not apply the written ticket"));
            for (int step = 0; step <= 4; step++)   // F6 (Codex review 2026-10-12): 0, 0.25, ... 1.0 s after the apply (was up to 0.75 s)
            {
                int k = step;   // one copy per lambda
                s.Add(now =>
                {
                    if (now - appliedAt < .25 * k) return false;
                    var p = roots[0].position; float bed = LrRiverData()._BedAt(p);
                    TvExpect(Get<int>(wander[0], "_travelApplies") - applies0 == 1 && Get<int>(wander[0], "_travelMoves") == moves0, tag + ": applied once without a move (applies +" + (Get<int>(wander[0], "_travelApplies") - applies0) + ", moves +" + (Get<int>(wander[0], "_travelMoves") - moves0) + ")");
                    TvExpect(Get<bool>(wander[0], "_waterSwimming") && CwPose(0) == 2 && Get<int>(wander[0], "_lrState") == 2, tag + ": still afloat in the river follow " + (.25 * k).ToString("F2") + " s after the apply" + CwDogState(0));
                    // 82l run 20261012-030307: "root >= riverbed + 0.03" was the wrong invariant (the dog floated where the water is 0.137 m, shallower
                    // than its 0.175 m draft: root - riverbed -0.038 by design). What a dropped float would change is the height: the root at the
                    // designed float height (LrFloatY, with the river legs' floor) and the hold flag still set (WaterClearFloat lowers it)
                    float want = LrFloatY(0, p);
                    TvExpect(bed > KH_RiverData.NoBed + 1f && Mathf.Abs(p.y - want) <= .03f && Get<bool>(wander[0], "_swimYValid"),
                        tag + ": the root at the float height (root - float " + (p.y - want).ToString("F3") + " m, root - riverbed " + (p.y - bed).ToString("F3") + " m, swimYValid " + Get<bool>(wander[0], "_swimYValid") + ")");
                    return true;
                });
            }
            s.Add(TvDo(() =>
            {
                var ph = (int[])Get<int[]>(manager, "travelPhase").Clone(); ph[0] = KH_LeashManager.TravelPhaseNone; manager.SetProgramVariable("travelPhase", ph);
                var rq = (int[])Get<int[]>(manager, "travelReq").Clone(); rq[0] = 0; manager.SetProgramVariable("travelReq", rq);
                var hd = (int[])Get<int[]>(manager, "travelHolder").Clone(); hd[0] = -1; manager.SetProgramVariable("travelHolder", hd);
                Networking.LocalPlayer.TeleportTo(lrBank, Quaternion.LookRotation(new Vector3(lrSide, 0f, 0f)));
                TvNote("the float survived a ticket apply that did not move the dog (IMP-DOG-053 #6); ticket cleared, holder back on the bank");
            }));
            s.Add(TvUntil(() => CwPose(0) == 0 && Get<int>(wander[0], "_lrState") == 0, 25, tag + ": dog 0 did not land after the holder left the water" + CwDogState(0)));
        }

        static string LrNote(WhCase c)
        {
            return " | site: " + c.lrSite + " | lrMax=" + c.lrMaxState + " entry=" + (c.lrEntry ?? "-") + " riverExit=" + c.lrExitLogged + " recover3=" + c.lrRecover3Logged
                + " waterJumps=" + c.waterJumps + " landTeleports=" + c.landTeleports + " minMouth=" + c.minMouthLR.ToString("F3") + " minLift=" + c.minLift.ToString("F3")
                + " waitDecisions=" + Get<int>(wander[WhDog], "_lrWaitDecisions") + " farDecisions=" + Get<int>(wander[WhDog], "_lrFarDecisions") + " frames=" + c.frames;
        }
    }
}
