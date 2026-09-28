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
    // DES-DOG-005 M3 (docs/plans/2026-09-28-frisbee-anywhere.md §D-3 / §D-4 / §D-5 / §G "M3"), Course/94b ClientSim harness on the
    // main scene. Same run frame as Course/94 (journal, watchdog, PlayerData backup/restore, no scene save, Play stop on failure) and
    // the same throw (the disc's own _CheckThrow after the harness writes the aimed velocity), but per case the dog of the breed the
    // case names and ITS disc. A throw does not always produce the shape a case is about (the planner chooses the catch), so a case
    // may take several attempts (different aims); an attempt that took another shape is RETRY, and only the shape's own outcome
    // decides PASS / FAIL. Sites: a usable bank slice of the baked river data (Course/90) near z 150 unless said otherwise; the
    // thrower on that slice's return point, the dog 1.2 m behind (away from the water) as Course/88 RW-*.
    //   F2-dobon-Pome   Pome, the aims searched before the Play (see "the ドボン aims" below): a catch that lands in the water (log "dobon carrying=true"),
    //                   then 12 -> 13 -> Return / Drop, out on the thrower's side, the disc by the player (<= 2.5 m), no cancel.
    //   F3-dobon-Lab    the same with the Lab in the channel: it lands and walks (waterPose never 2).
    //   F4-deep-Lab     the Lab, a site whose centre is deeper than its Fetch limit (0.85 M): no landing in the water (no dobon line),
    //                   the disc floats, cancel reason=deep-disc. An attempt the Lab caught on land is RETRY. No such site in the river
    //                   data (2026-09-28) = SKIP (N/A, not a failure; deep water is PCVR 41).
    //   The ドボン cases (F2, F3, F14b, F2m-*) take the aims searched in Edit mode before the Play (the dog 2.5 m outside the band
    //   edge; the real planner confirms each aim); none for a dog = FRISBEE_M3_BLOCKED and no Play. Course/94c runs F2 / F3 only.
    //   F5-float-Pome   Pome, a flat throw into the water: 11 -> 12 -> 13 -> Return / Drop, the disc by the player, no cancel.
    //   F11-recall-11   as F5; in 11 the player goes to the plaza (the stage-1 take-out gate) and calls the disc (_Disc1): cancel
    //                   reason=target-lost, the disc by the player with gravity, the dog back on dry ground within 30 s.
    //   F12-recall-12   as F5; in 12 the player goes to the plaza and calls the disc: refused (KH_ToyRecall._pendingRow stays -1 and
    //                   the pouch's _hsToyRow is not set: InWater), the player goes back, the fetch completes to the player.
    //   F13-inwater     the Lab placed wading in the river (the site: the usable bank nearest z 150 with a 0.10-0.22 m wading spot on
    //                   its own side, searched in the river data; none = SKIP), a throw onto the bank: req reject reason=in-water, the catcher never leaves
    //                   Idle / Ready, the dog back on dry ground within 25 s (the ask to come out).
    //   F14-claim       Pome, as F5 with the disc's ownership never reading as ours (heap _debugClaimRefuse): the claim wait of 11
    //                   (log "disc claim wait (in the water)"), cancel reason=claim-failed, never 12, the disc floating 3 s after rest.
    //                   A ドボン attempt is RETRY (that is F14b).
    //   F14b-claim-dobon  Pome, a ドボン catch with the claim refused: it waits in 12 and gives up (claim-failed), never 13 / Return / Drop,
    //                   the disc floating 3 s after rest.
    //   F2m-miss-float / F2m-miss-land  Pome, a jump that will come down in the water is made to miss (heap _catchDecided at liftoff)
    //                   and the disc is put in the water at the planned landing / on dry ground at the dog's start (the catcher told
    //                   EndLanded): float -> dobon carrying=false, 11 -> 12 -> 13 -> Return / Drop, by the player; land -> missed-water,
    //                   the dog dry within 30 s, never 12.
    //   F12 also reads the pouch's _hsLastGate: the refusal must be KH_ToyRecall.InWater.
    // Not simulable here: another player / Owner handover in 11-13 (PCVR 45), a late joiner (44), real hands, device timing.
    // Evidence: Logs/kh-course-walk/<run>/frisbee-m3.json. Final status FRISBEE_M3_PASS / FRISBEE_M3_FAIL.
    public static partial class KH_CourseWalkHarnessEditor
    {
        const int F3MaxAttempts = 6;
        [Serializable] public class F3Case
        {
            public string name, status = "PENDING", reason;
            public int dog, attempts, gateAfterPress = -99, side, stateMask, maxState = -1, poseMax, rejectInWater, catchOk, exitSide = -2, stateAtPress = -1;
            public float z, landError = -1f, discEnd = -1f, elMin, elMax;
            public Vector3 thrower, target, p0, v0, dogAt, wadeAt;
            public double startedAt = -1, launchedAt = -1, restAt = -1, pressAt = -1, checkAt = -1, dryAt = -1, enter11 = -1;
            public bool launched, dobonCarry, dobonMiss, teleported, pressed, gateTested, recallRefusedOk, floatingAfter, discGravity, claimWait11, waterJump, missForced;
            public List<string> retries = new List<string>();   // one line per RETRY (the attempt, its aim and why); attempts = retries.Count
            public List<string> cancels = new List<string>();
            public List<string> log = new List<string>();
        }
        [Serializable] public class F3Report
        {
            public string runId, status = "RUNNING";
            public List<F3Case> cases = new List<F3Case>();
            public string limitations = "Single local ClientSim; each case uses one dog and its disc. The throw is the disc's _CheckThrow after the harness writes the aimed velocity; the planner picks the catch, so a case may need several attempts (other shapes are RETRY). The claim failure is a heap stand-in (_debugClaimRefuse). Not network, Owner handover (PCVR 45), late join (44) or device acceptance.";
        }
        static F3Report f3;
        static UdonBehaviour f3Catcher, f3Disc, f3Fetch, f3River, f3Spot, f3Recall;
        static int f3Phase, f3Step;
        static double f3Next;
        static bool f3LogHooked;
        static F3Case F3C => f3.cases[caseIndex];

        [MenuItem("Tools/KominkaHillside/Course/94b - Walk Harness Frisbee Anywhere (M3 water)")]
        public static void FrisbeeAnywhereWater() { F3Start(null); }

        [MenuItem("Tools/KominkaHillside/Course/94c - Walk Harness Frisbee Anywhere (M3 dobon F2 F3 only)")]
        public static void FrisbeeAnywhereWaterDobon() { F3Start("F2-dobon-Pome,F3-dobon-Lab"); }

        const string F3OnlyKey = Key + ".f3Only";

        // Edit mode, before the Play: the ドボン aims are searched here (F3SearchAims, the real planner decides each one) and written to
        // F3AimFile for the Play to read. A dog without an aim = FRISBEE_M3_BLOCKED with the reason, and no Play.
        static void F3Start(string only)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start Course/94b from Edit mode");
            f3AimSet = null;
            string blocked = F3PrepareAims();
            if (blocked != null) { Debug.LogError("[KH Course Walk] FRISBEE_M3_BLOCKED " + blocked); return; }
            SessionState.SetString(F3OnlyKey, only ?? "");
            CoursePlanPreflight(false, true);   // Course/95 plan probe on these cases first: PROBE_MISMATCH before the Play (warning only)
            Begin(false);
            report.frisbeeOnly = true; report.frisbeeM3 = true; report.deadline = EditorApplication.timeSinceStartup + (only == null ? 2400 : 900); Save();
        }

        // The cases and their dog (shared with the Course/95 plan probe, KH_CourseWalkHarnessEditor.PlanProbe.cs).
        static readonly object[][] F3Defs = { new object[] { "F2-dobon-Pome", 1 }, new object[] { "F3-dobon-Lab", 2 }, new object[] { "F4-deep-Lab", 2 }, new object[] { "F5-float-Pome", 1 },
            new object[] { "F11-recall-11", 1 }, new object[] { "F12-recall-12", 1 }, new object[] { "F13-inwater", 2 }, new object[] { "F14-claim", 1 },
            new object[] { "F14b-claim-dobon", 1 }, new object[] { "F2m-miss-float", 1 }, new object[] { "F2m-miss-land", 1 } };

        static void MakeFrisbeeM3Cases()
        {
            var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>();
            f3River = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_RiverData");
            f3Spot = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_WaterSpot");
            f3Recall = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_ToyRecall");
            if (f3River == null || f3Spot == null || f3Recall == null) throw new InvalidOperationException("KH_RiverData / KH_WaterSpot / KH_ToyRecall missing");
            if (Get<bool[]>(f3River, "sliceOk").Length < 2) throw new InvalidOperationException("River fetch data missing: run Course/90 first");
            f3 = new F3Report { runId = report.runId };
            f3AimSet = null;   // the Play reads the aims the Edit-mode search wrote (F3AimFile)
            string only = SessionState.GetString(F3OnlyKey, "");
            foreach (var d in F3Defs)
            {
                if (only.Length > 0 && !only.Split(',').Contains((string)d[0])) continue;   // Course/94c: the named cases only
                report.cases.Add(new Case { name = (string)d[0], count = 1 });
                f3.cases.Add(new F3Case { name = (string)d[0], dog = (int)d[1] });
            }
            f3Phase = 0; f3Step = 0; f3Next = 0;
            if (!f3LogHooked) { Application.logMessageReceived += F3Log; f3LogHooked = true; }
        }

        // Bind the case's dog: its catcher (dogRoot), its disc (the catcher's frisbee) and its Fetch (the ball's, for waterPose).
        static void F3Bind(F3Case c)
        {
            var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>();
            f3Catcher = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_FrisbeeCatch" && Get<Transform>(u, "dogRoot") == roots[c.dog]);
            if (f3Catcher == null) throw new InvalidOperationException("No KH_FrisbeeCatch for dog " + c.dog);
            object dv; f3Disc = f3Catcher.TryGetProgramVariable("frisbee", out dv) ? dv as UdonBehaviour : null;
            if (f3Disc == null) throw new InvalidOperationException("Dog " + c.dog + "'s catcher has no disc");
            f3Fetch = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KD_DogFetchController" && Get<Transform>(u, "dogRoot") == roots[c.dog]);
            if (f3Fetch == null) throw new InvalidOperationException("No KD_DogFetchController for dog " + c.dog);
            f3Catcher.SetProgramVariable("_debugWaterFetchOff", false);
            f3Catcher.SetProgramVariable("_debugClaimRefuse", false);
        }

        static void F3Log(string condition, string stack, LogType type)
        {
            if (f3 == null || report == null || !report.frisbeeM3 || caseIndex < 0 || caseIndex >= f3.cases.Count || !EditorApplication.isPlaying) return;
            var c = F3C;
            if (c.launchedAt < 0 || !condition.StartsWith("[KH_FrisbeeCatch] ") || !condition.Contains(" dog=" + roots[c.dog].name)) return;
            if (condition.Contains("dobon carrying=true")) c.dobonCarry = true;
            else if (condition.Contains("dobon carrying=false")) c.dobonMiss = true;
            if (condition.Contains("catch OK")) c.catchOk++;
            if (condition.Contains("disc claim wait (in the water)")) c.claimWait11 = true;
            if (condition.Contains("req reject") && condition.Contains("reason=in-water")) c.rejectInWater++;
            if (condition.Contains("req cancel"))
            {
                int i = condition.IndexOf("reason=", StringComparison.Ordinal);
                if (i >= 0) { int j = condition.IndexOf(' ', i); c.cancels.Add(j < 0 ? condition.Substring(i + 7) : condition.Substring(i + 7, j - i - 7)); }
            }
            if (c.log.Count < 60 && (condition.Contains("req ") || condition.Contains("dobon") || condition.Contains("water ") || condition.Contains("disc ") || condition.Contains("river ") || condition.Contains("catch ")))
                c.log.Add(condition);
        }

        static void FrisbeeM3Tick(double now)
        {
            try { F3StepCase(now); }
            catch { SaveF3Detail("FAIL"); throw; }
        }

        static bool F3Rest(int cs) { return cs == KH_FrisbeeCatch.StateIdle || cs == KH_FrisbeeCatch.StateReady; }
        static bool F3Has(F3Case c, int s) { return (c.stateMask & (1 << s)) != 0; }

        static void F3StepCase(double now)
        {
            var c = F3C;
            if (f3Phase == 4) F3Observe(c, now);
            if (now < f3Next) return;
            if (f3Phase == 0) // bind, detach every dog through the menu, wait for this dog's disc play to be at rest and the dog dry
            {
                if (f3Step == 0) { F3Bind(c); if (c.startedAt < 0) c.startedAt = now; }
                if (f3Step < 6) { pouch.SendCustomEvent("_WalkOff" + f3Step++); f3Next = now + .3; return; }
                if (Get<int[]>(manager, "walkers").Any(w => w >= 0)) throw new InvalidOperationException("Detach did not complete");
                int st = Get<int>(f3Catcher, "state");
                if (!F3Rest(st) || Get<int>(f3Fetch, "waterPose") != 0)
                { if (now - c.startedAt > 60 * (c.attempts + 1)) { F3Finish(c, now, false, "dog " + c.dog + " did not come back to rest / dry (catcher " + st + ", waterPose " + Get<int>(f3Fetch, "waterPose") + ")"); return; } f3Next = now + .5; return; }
                string err = F3Place(c);
                if (err != null)
                {
                    if (err.StartsWith("RETRY ")) F3Retry(c, now, err.Substring(6));
                    else if (err.StartsWith("SKIP ")) F3Skip(c, now, err.Substring(5));
                    else F3Finish(c, now, false, err);
                    return;
                }
                f3Phase = 1; f3Next = now + .6; return;
            }
            if (f3Phase == 1) { pouch.SendCustomEvent("_WalkLeft" + c.dog); f3Phase = 2; f3Next = now + .8; return; }
            if (f3Phase == 2) // detach here, so the released roaming home is this site
            {
                if (Get<int[]>(manager, "walkers")[c.dog] != Networking.LocalPlayer.playerId) throw new InvalidOperationException("Menu attach rejected for dog " + c.dog);
                pouch.SendCustomEvent("_WalkOff" + c.dog); f3Phase = 3; f3Next = now + 1.5; return;
            }
            if (f3Phase == 3) // F13: the dog is put in the water first and the throw waits for its pose
            {
                if (c.name == "F13-inwater")
                {
                    if (c.pressAt < 0) { string perr = F3PutInWater(c); if (perr != null) { F3Finish(c, now, false, perr); return; } c.pressAt = now; f3Next = now + .1; return; }
                    if (Get<int>(f3Fetch, "waterPose") == 0) { if (now - c.pressAt > 3) F3Finish(c, now, false, "F13: the placed dog never read as wet"); else f3Next = now + .05; return; }
                    c.pressAt = -1;
                }
                if (c.name == "F14-claim" || c.name == "F14b-claim-dobon") f3Catcher.SetProgramVariable("_debugClaimRefuse", true);
                if (c.name != "F13-inwater")
                {
                    // The aim was chosen for the dog standing at dogAt facing the water (the attach / detach may have moved it): put it back
                    roots[c.dog].SetPositionAndRotation(c.dogAt, Quaternion.LookRotation(new Vector3(-c.side, 0, 0)));
                    var syncT = roots[c.dog].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (syncT != null) syncT.FlagDiscontinuity();
                }
                F3Throw(c, now); f3Phase = 4; return;
            }
            if (f3Phase == 4) F3Evaluate(c, now);
        }

        // ---- sites ----

        static float F3Limit(int dog)
        {
            float ratio = Get<float>(f3River, "fetchWadeRatio"); if (ratio <= .01f) ratio = KH_RiverData.DefaultFetchWadeRatio;
            return ratio * Get<float>(wander[dog], "waterMouthHeight");
        }

        /// <summary>The ok bank slice nearest zTarget (searched outward over the whole river, outside the bridge zones, 30 m or more
        /// from the spot's entry) whose river centre passes accept(z); the +x side first. Returns the slice code, -1 none.</summary>
        static int F3FindSlice(float zTarget, Func<float, bool> accept)
        {
            var ok = Get<bool[]>(f3River, "sliceOk"); float z0 = Get<float>(f3River, "sliceZ0"), st = Get<float>(f3River, "sliceStep");
            var entry = Get<Vector3>(f3Spot, "entry");
            int c = Mathf.RoundToInt((zTarget - z0) / st), count = ok.Length / 2;
            for (int o = 0; o <= count; o++)
                foreach (int sgn in new[] { 1, -1 })
                {
                    int k = c + o * sgn; if (k < 0 || k >= count) continue;
                    float z = z0 + k * st;
                    if (RwBridgeZone(z, 3f) || Mathf.Abs(z - entry.z) < 30f || !accept(z)) continue;
                    if (ok[2 * k + 1]) return 2 * k + 1;
                    if (ok[2 * k]) return 2 * k;
                }
            return -1;
        }

        /// <summary>null = placed; "RETRY ..." = this aim does not work (another attempt); anything else = the case cannot be set up here.</summary>
        static string F3Place(F3Case c)
        {
            string err = F3PlaceGeometry(c);
            if (err != null) return err;
            var facing = Quaternion.LookRotation(new Vector3(-c.side, 0, 0));
            Networking.LocalPlayer.TeleportTo(c.thrower, facing);
            roots[c.dog].SetPositionAndRotation(c.dogAt, facing);
            var sync = roots[c.dog].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
            var r = KH_CourseGroundEditor.RiverAt(c.z); float hw = KH_CourseGroundEditor.HalfWidth(c.z);
            if (c.log.Count < 60) c.log.Add("attempt " + (c.attempts + 1) + " site z=" + c.z.ToString("F1") + " side=" + c.side + " hw=" + hw.ToString("F2") + " thrower=" + c.thrower.ToString("F2")
                + " target=" + c.target.ToString("F2") + " el=[" + c.elMin + "," + c.elMax + "] v0=" + c.v0.ToString("F2") + " err=" + c.landError.ToString("F2")
                + " centreDepth=" + WhDepthTerrain(r).ToString("F2") + " labLimit=" + F3Limit(2).ToString("F2"));
            return null;
        }

        /// <summary>The placement of this attempt without touching the scene (Course/95 plan probe calls it in Edit mode): c.side / z /
        /// thrower / target / dogAt (the dog faces -side) / p0 / v0. null = placed; "RETRY ..." / anything else as F3Place.</summary>
        static string F3PlaceGeometry(F3Case c)
        {
            var bank = Get<Vector3[]>(f3River, "sliceBank"); var ret = Get<Vector3[]>(f3River, "sliceReturn");
            float z0 = Get<float>(f3River, "sliceZ0"), st = Get<float>(f3River, "sliceStep");
            int code;
            if (c.name == "F4-deep-Lab")
            {
                float lim = F3Limit(c.dog);
                code = F3FindSlice(150f, z => WhDepthTerrain(KH_CourseGroundEditor.RiverAt(z)) > lim + .05f);
                // Not in the river data (2026-09-28: the Lab's limit 0.47 m): N/A, not a failure. The refusal of deep water stays PCVR 41.
                if (code < 0) return "SKIP F4: no usable bank next to water deeper than the Lab's Fetch limit " + lim.ToString("F2") + " m in the river data (deep-disc is checked on PCVR 41)";
            }
            else if (F3IsDobon(c.name))
            {
                // The ドボン cases share the aims found before the Play (F3SearchAims): attempt k takes the k-th
                var aims = F3DobonAims(c.dog);
                if (aims.Count == 0) return "BLOCKED no ドボン aim for dog " + c.dog + " (" + string.Join("; ", f3AimSet.notes.ToArray()) + ")";
                if (c.attempts >= aims.Count) return "RETRY every precomputed aim tried (" + aims.Count + ")";
                var a = aims[c.attempts];
                c.side = a.side; c.z = a.z; c.thrower = a.thrower; c.dogAt = a.dogAt; c.p0 = a.p0; c.v0 = a.v0; c.target = a.landPt; c.landError = 0f;
                c.elMin = c.elMax = Mathf.Atan2(a.v0.y, new Vector2(a.v0.x, a.v0.z).magnitude) * Mathf.Rad2Deg;
                return null;
            }
            else if (c.name == "F13-inwater")
            {
                // 94b runs 20260928-115916 / 121049: F13's site conditions failed one after the other in the Play. The whole site (bank
                // slice, wading spot, the dog reading wet there, the dog's bank point, dry ground 5 m along for the throw, the aim) is
                // now searched in Edit mode with the ドボン aims (F3SearchF13) and used as it is; none = SKIP with the failed conditions.
                F3EnsureAims();
                if (!f3AimSet.f13Ok) return "SKIP F13: " + f3AimSet.f13Why;
                c.side = f3AimSet.f13Side; c.z = f3AimSet.f13Z; c.thrower = f3AimSet.f13Thrower; c.dogAt = f3AimSet.f13DogAt; c.wadeAt = f3AimSet.f13Wade;
                c.target = f3AimSet.f13Target; c.p0 = f3AimSet.f13P0; c.v0 = f3AimSet.f13V0; c.landError = 0f; c.elMin = 5f; c.elMax = 25f;
                return null;
            }
            else
            {
                code = F3FindSlice(150f, z => WhDepthTerrain(KH_CourseGroundEditor.RiverAt(z)) > .05f);
                if (code < 0) return c.name + ": no usable bank near z 150 (Course/91)";
            }
            c.side = (code & 1) == 1 ? 1 : -1; c.z = z0 + (code / 2) * st; c.thrower = ret[code];
            var r = KH_CourseGroundEditor.RiverAt(c.z); float hw = KH_CourseGroundEditor.HalfWidth(c.z);
            // The float cases' aim of this attempt (a landing across the water; the ドボン cases returned above with a searched aim)
            float[] offsFloat = { 0f, -hw * .4f, hw * .3f, -hw * .6f, 0f, hw * .15f };
            float off = offsFloat[c.attempts % 6];
            if (c.name != "F4-deep-Lab" && hw < 1.5f) off = 0f;
            float elMin = c.attempts < 3 ? -5f : 0f, elMax = c.attempts < 3 ? 6f : 14f;
            if (c.name == "F13-inwater")
            {
                // A throw onto the bank 5 m along the river (it must not float), from the same site
                RaycastHit g; var along = c.thrower + new Vector3(0f, 0f, c.z > 360f ? -5f : 5f);
                if (!Physics.Raycast(along + Vector3.up * 4f, Vector3.down, out g, 10f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore) || RiverInBand(g.point))
                    return "F13: no dry ground 5 m along the bank from " + c.thrower;
                c.target = g.point; elMin = 5f; elMax = 25f;
            }
            else c.target = new Vector3(r.x + c.side * off, r.y, c.z);
            NavMeshHit dh;
            if (!NavMesh.SamplePosition(c.thrower + new Vector3(c.side * 1.2f, 0, .8f), out dh, 2, NavMesh.AllAreas) || RiverInBand(dh.position)) return c.name + ": dog placement lacks NavMesh";
            c.dogAt = dh.position; c.elMin = elMin; c.elMax = elMax;
            Vector3 toT = c.target - c.thrower; toT.y = 0f; toT.Normalize();
            c.p0 = c.thrower + Vector3.up * 1.2f + toT * .4f;
            if (!F3Aim(c.p0, c.target, elMin, elMax, out c.v0, out c.landError)) return "RETRY no throw reaches " + c.target + " (best error " + c.landError.ToString("F2") + " m) el=[" + elMin + "," + elMax + "]";
            return null;
        }

        /// <summary>FbAim with an elevation window (a lob or a flat throw), same landing rule (first collider, or the first descending
        /// crossing of the target's height: the water surface has no collider).</summary>
        static bool F3Aim(Vector3 p0, Vector3 target, float elMin, float elMax, out Vector3 best, out float bestErr)
        {
            float g = Get<float>(f3Disc, "gravity"), k = Get<float>(f3Disc, "drag"), lift = Get<float>(f3Disc, "lift"), cap = Get<float>(f3Disc, "liftCap");
            float curve = Get<int>(f3Catcher, "variant") == KH_FrisbeeCatch.VariantCurve ? KH_FrisbeeCatch.CurveDefault : 0f;
            Vector3 dir = target - p0; dir.y = 0f; dir.Normalize();
            best = Vector3.zero; bestErr = float.MaxValue;
            int mask = (1 << 0) | (1 << 11);
            for (float el = elMin; el <= elMax; el += 1f)
                for (float sp = 3f; sp <= 11f; sp += .2f)
                {
                    float rad = el * Mathf.Deg2Rad;
                    var v0 = dir * (sp * Mathf.Cos(rad)) + Vector3.up * (sp * Mathf.Sin(rad));
                    Vector3 prev = p0, land = Vector3.zero; bool landed = false;
                    for (float t = .03f; t <= 8f; t += .03f)
                    {
                        var p = FbPositionAt(p0, v0, curve, t, g, k, lift, cap);
                        var d = p - prev; float len = d.magnitude; RaycastHit hit;
                        if (len > 1e-4f && Physics.Raycast(prev, d / len, out hit, len, mask, QueryTriggerInteraction.Ignore)) { land = hit.point; landed = true; break; }
                        if (p.y < target.y && p.y < prev.y) { land = prev.y > target.y ? Vector3.Lerp(prev, p, (prev.y - target.y) / (prev.y - p.y)) : p; landed = true; break; }
                        prev = p;
                    }
                    if (!landed) continue;
                    var e = land - target; e.y = 0f;
                    if (e.magnitude < bestErr) { bestErr = e.magnitude; best = v0; }
                }
            return bestErr < .6f;
        }

        // F13: the Lab wading in the river on the thrower's side (depth 0.10-0.22 m, at least 1 m inside the water's edge).
        /// <summary>F13: a wading spot (depth 0.10-0.22 m by the Terrain, 1 m or more inside the water) on the given side at z; false = none.</summary>
        static bool F3WadeSpot(float z, int side, out Vector3 at)
        {
            at = Vector3.zero;
            var r = KH_CourseGroundEditor.RiverAt(z); float hw = KH_CourseGroundEditor.HalfWidth(z);
            for (float lat = hw - 1f; lat >= 0f; lat -= .1f)
            {
                var q = new Vector3(r.x + side * lat, r.y, z);
                float d = WhDepthTerrain(q);
                if (d < .10f || d > .22f) continue;
                RaycastHit g;
                if (Physics.Raycast(new Vector3(q.x, r.y + 2f, q.z), Vector3.down, out g, 6f, 1 << 11, QueryTriggerInteraction.Ignore)) { at = g.point; return true; }
            }
            return false;
        }

        /// <summary>F13: the usable bank slice nearest z 150 (F3FindSlice's filters) with a wading spot on its own side; -1 = none.</summary>
        static int F3FindWadeSlice(out Vector3 wade)
        {
            wade = Vector3.zero;
            var ok = Get<bool[]>(f3River, "sliceOk"); float z0 = Get<float>(f3River, "sliceZ0"), st = Get<float>(f3River, "sliceStep");
            var entry = Get<Vector3>(f3Spot, "entry");
            int c0 = Mathf.RoundToInt((150f - z0) / st), count = ok.Length / 2;
            for (int o = 0; o <= count; o++)
                foreach (int sgn in new[] { 1, -1 })
                {
                    int k = c0 + o * sgn; if (k < 0 || k >= count || (o == 0 && sgn < 0)) continue;
                    float z = z0 + k * st;
                    if (RwBridgeZone(z, 3f) || Mathf.Abs(z - entry.z) < 30f) continue;
                    for (int s = 1; s >= 0; s--)
                        if (ok[2 * k + s] && F3WadeSpot(z, s == 1 ? 1 : -1, out wade)) return 2 * k + s;
                }
            return -1;
        }

        static string F3PutInWater(F3Case c)
        {
            Vector3 at;
            at = c.wadeAt;   // searched before the Play (F3SearchF13); never searched again here
            if (at == Vector3.zero) return "F13: no searched wading spot (F3SearchF13)";
            roots[c.dog].SetPositionAndRotation(at, Quaternion.LookRotation(new Vector3(-c.side, 0, 0)));
            var sync = roots[c.dog].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
            if (c.log.Count < 60) c.log.Add("dog put in the water at " + at.ToString("F2") + " depth=" + WhDepthTerrain(at).ToString("F2"));
            return null;
        }

        static void F3Throw(F3Case c, double now)
        {
            var t = f3Disc.transform;
            var rb = t.GetComponent<Rigidbody>(); if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            t.position = c.p0;
            f3Disc.SetProgramVariable("_pendingThrowVelocity", c.v0);
            f3Disc.SetProgramVariable("_hasPendingThrow", true);
            c.launchedAt = now;   // before the event: ReqCatch (accept / reject) logs inside it
            f3Disc.SendCustomEvent("_CheckThrow");
            c.launched = Get<bool>(f3Disc, "_flying");
            if (!c.launched) throw new InvalidOperationException(c.name + ": the disc did not launch (_CheckThrow)");
        }

        // ---- observation / decision ----

        static bool F3DiscFloating() { var rb = f3Disc.transform.GetComponent<Rigidbody>(); return rb != null && !rb.isKinematic && !rb.useGravity; }
        static float F3DiscToPlayer() { var d = f3Disc.transform.position - Networking.LocalPlayer.GetPosition(); d.y = 0f; return d.magnitude; }
        static Vector3 F3Plaza()
        {
            var centre = Get<Vector3>(f3Catcher, "playAreaCenter"); RaycastHit g;
            if (Physics.Raycast(centre + Vector3.up * 3f, Vector3.down, out g, 8f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore)) centre = g.point;
            return centre;
        }

        static void F3Observe(F3Case c, double now)
        {
            int cs = Get<int>(f3Catcher, "state");
            c.stateMask |= 1 << cs; if (cs > c.maxState) c.maxState = cs;
            int pose = Get<int>(f3Fetch, "waterPose"); if (pose > c.poseMax) c.poseMax = pose;
            if (cs == KH_FrisbeeCatch.StateWaterChase && c.enter11 < 0) c.enter11 = now;
            if (cs == KH_FrisbeeCatch.StateWaterExit && c.exitSide == -2) c.exitSide = RiverSideOf(roots[c.dog].position);
            bool air = (cs == KH_FrisbeeCatch.StateJump || cs == KH_FrisbeeCatch.StateJumpCarry) && Get<bool>(f3Catcher, "_airborne");
            if (air && Get<bool>(f3Catcher, "_airLandWater")) c.waterJump = true;
            // F2m (Codex M3 review 6): a jump that will come down in the water is made to miss (its catch marked decided before the
            // window), and the disc is stopped at once and put where the case needs it (the water at the planned landing / dry ground
            // at the dog's start) with the catcher told the flight landed there, so the touchdown meets a known disc
            if (c.name.StartsWith("F2m") && !c.missForced && cs == KH_FrisbeeCatch.StateJump && air && Get<bool>(f3Catcher, "_airLandWater"))
            {
                c.missForced = true;
                f3Catcher.SetProgramVariable("_catchDecided", true);
                var land = Get<Vector3>(f3Catcher, "_landPt");
                Vector3 at;
                if (c.name == "F2m-miss-float") at = new Vector3(land.x, KH_CourseGroundEditor.RiverAt(land.z).y - .03f, land.z);
                else at = c.dogAt + Vector3.up * .15f;
                f3Disc.SetProgramVariable("_flying", false);
                var tr = f3Disc.transform; tr.position = at;
                var ds = tr.GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (ds != null) ds.FlagDiscontinuity();
                f3Catcher.SetProgramVariable("_flightEnded", true);
                f3Catcher.SetProgramVariable("_flightEndReason", KH_Frisbee.EndLanded);
                if (c.log.Count < 60) c.log.Add("forced a miss mid-air; disc put at " + at.ToString("F2") + " (landing " + land.ToString("F2") + ")");
            }
        }

        static void F3Evaluate(F3Case c, double now)
        {
            double t = now - c.launchedAt;
            int cs = Get<int>(f3Catcher, "state");
            bool rest = F3Rest(cs) && t > 1.5;
            if (rest && c.restAt < 0) { c.restAt = now; c.discEnd = F3DiscToPlayer(); c.discGravity = f3Disc.transform.GetComponent<Rigidbody>().useGravity; }
            if (!rest) c.restAt = -1;
            bool atRest = c.restAt >= 0 && now - c.restAt > .5;
            string sum = " states=" + Convert.ToString(c.stateMask, 2) + " cancels=[" + string.Join(",", c.cancels) + "] dobon=" + c.dobonCarry + "/" + c.dobonMiss
                + " catchOk=" + c.catchOk + " poseMax=" + c.poseMax + " exitSide=" + c.exitSide + " throwerSide=" + c.side + " discToPlayer=" + (c.discEnd < 0 ? F3DiscToPlayer() : c.discEnd).ToString("F2");
            bool completed = (F3Has(c, KH_FrisbeeCatch.StateReturn) || F3Has(c, KH_FrisbeeCatch.StateDrop)) && c.cancels.Count == 0;
            switch (c.name)
            {
                case "F2-dobon-Pome":
                case "F3-dobon-Lab":
                {
                    if (!c.dobonCarry)
                    {
                        // Another shape (a land catch, the ground, a water fetch, a missed ドボン): try another aim once it is over
                        if (atRest || t > 90) { F3Retry(c, now, "no ドボン catch" + sum); return; }
                        return;
                    }
                    if (!atRest && t < 150) return;
                    bool ok = atRest && F3Has(c, KH_FrisbeeCatch.StateWaterCarry) && F3Has(c, KH_FrisbeeCatch.StateWaterExit) && completed
                        && c.discEnd <= 2.5f && c.exitSide == c.side && (c.name == "F2-dobon-Pome" || c.poseMax < 2);
                    F3Finish(c, now, ok, "dobon catch" + sum);
                    return;
                }
                case "F4-deep-Lab":
                {
                    if (c.dobonCarry || c.dobonMiss) { if (atRest || t > 90) F3Finish(c, now, false, "the Lab landed in water deeper than its limit" + sum); return; }
                    if (!atRest && t < 90) return;
                    if (c.cancels.Contains("deep-disc"))
                    {
                        if (now - c.restAt < 2) return;
                        c.floatingAfter = F3DiscFloating();
                        F3Finish(c, now, c.floatingAfter && !F3Has(c, KH_FrisbeeCatch.StateWaterChase), "deep-disc floatingAfter=" + c.floatingAfter + sum);
                        return;
                    }
                    F3Retry(c, now, "no deep-disc (another shape)" + sum);
                    return;
                }
                case "F14b-claim-dobon":
                {
                    // Codex M3 review 5: a ドボン catch whose claim never arrives waits in 12 and gives up (never 13 / Return / Drop)
                    if (!c.dobonCarry) { if (atRest || t > 90) F3Retry(c, now, "no dobon catch" + sum); return; }
                    if (!atRest && t < 60) return;
                    if (c.restAt >= 0 && now - c.restAt < 3) return;
                    c.floatingAfter = F3DiscFloating();
                    f3Catcher.SetProgramVariable("_debugClaimRefuse", false);
                    bool okB = atRest && c.cancels.Contains("claim-failed") && !F3Has(c, KH_FrisbeeCatch.StateWaterExit) && !F3Has(c, KH_FrisbeeCatch.StateReturn)
                        && !F3Has(c, KH_FrisbeeCatch.StateDrop) && c.floatingAfter;
                    F3Finish(c, now, okB, "dobon claim refused floatingAfter=" + c.floatingAfter + sum);
                    return;
                }
                case "F2m-miss-float":
                case "F2m-miss-land":
                {
                    if (!c.missForced) { if (atRest || t > 90) F3Retry(c, now, "no jump that lands in the water" + sum); return; }
                    if (!atRest && t < 150) return;
                    if (c.name == "F2m-miss-float")
                    {
                        bool okF = atRest && c.dobonMiss && F3Has(c, KH_FrisbeeCatch.StateWaterChase) && F3Has(c, KH_FrisbeeCatch.StateWaterCarry)
                            && F3Has(c, KH_FrisbeeCatch.StateWaterExit) && completed && c.discEnd <= 2.5f;
                        F3Finish(c, now, okF, "missed dobon, disc in the water" + sum);
                        return;
                    }
                    if (c.dryAt < 0 && Get<int>(f3Fetch, "waterPose") == 0) c.dryAt = now;
                    if (c.dryAt < 0 && now - c.restAt < 30) return;
                    bool okL = atRest && c.dobonMiss && c.cancels.Contains("missed-water") && c.dryAt >= 0 && !F3Has(c, KH_FrisbeeCatch.StateWaterCarry);
                    F3Finish(c, now, okL, "missed dobon, disc on land dry=" + (c.dryAt >= 0 ? (c.dryAt - c.restAt).ToString("F1") + "s" : "no") + sum);
                    return;
                }
                case "F5-float-Pome":
                case "F14-claim":
                {
                    if (!atRest && t < 150) return;
                    if (!F3Has(c, KH_FrisbeeCatch.StateWaterChase)) { F3Retry(c, now, "no water chase (another shape)" + sum); return; }
                    if (c.dobonCarry || c.dobonMiss) { F3Retry(c, now, "a dobon (the F2 / F14b shape)" + sum); return; }
                    // Codex M3 review 5: F14 is the claim wait of 11 itself
                    if (c.name == "F5-float-Pome")
                    {
                        bool ok5 = atRest && F3Has(c, KH_FrisbeeCatch.StateWaterCarry) && F3Has(c, KH_FrisbeeCatch.StateWaterExit) && completed && c.discEnd <= 2.5f && c.exitSide == c.side;
                        F3Finish(c, now, ok5, "float fetch" + sum);
                        return;
                    }
                    if (now - c.restAt < 3) return;
                    c.floatingAfter = F3DiscFloating();
                    f3Catcher.SetProgramVariable("_debugClaimRefuse", false);
                    F3Finish(c, now, atRest && c.claimWait11 && c.cancels.Contains("claim-failed") && c.floatingAfter && !F3Has(c, KH_FrisbeeCatch.StateWaterCarry),
                        "claim refused in 11 claimWait=" + c.claimWait11 + " floatingAfter=" + c.floatingAfter + sum);
                    return;
                }
                case "F11-recall-11":
                {
                    if (!c.pressed)
                    {
                        if (!c.teleported)
                        {
                            if (cs == KH_FrisbeeCatch.StateWaterChase && now - c.enter11 > .8)
                            {
                                var dd = f3Disc.transform.position - roots[c.dog].position; dd.y = 0f;
                                if (dd.magnitude > 1.5f) { Networking.LocalPlayer.TeleportTo(F3Plaza(), Quaternion.identity); c.teleported = true; c.pressAt = now + .5; }
                            }
                            if (!c.teleported && (atRest || t > 90)) F3Retry(c, now, "never in 11 far enough from the disc" + sum);
                            return;
                        }
                        if (c.gateTested) { F3RetryWhenRest(c, now, atRest, "not in 11 at the press (" + c.stateAtPress + ")" + sum); return; }
                        if (now < c.pressAt) return;
                        c.stateAtPress = cs;
                        if (cs != KH_FrisbeeCatch.StateWaterChase) { c.gateTested = true; F3Back(c); return; }   // gateTested = abandoned attempt (F11)
                        pouch.SendCustomEvent("_Disc" + c.dog); c.pressed = true; c.checkAt = now;
                        if (c.log.Count < 60) c.log.Add("menu _Disc" + c.dog + " in 11 at " + t.ToString("F2") + "s");
                        return;
                    }
                    if (!atRest && now - c.checkAt < 30) return;
                    if (c.dryAt < 0 && Get<int>(f3Fetch, "waterPose") == 0) c.dryAt = now;
                    if (c.dryAt < 0 && now - c.checkAt < 30) return;
                    bool ok11 = atRest && c.cancels.Contains("target-lost") && c.discEnd <= 2.5f && c.discGravity && c.dryAt >= 0;
                    F3Finish(c, now, ok11, "recall in 11 dry=" + (c.dryAt >= 0 ? (c.dryAt - c.checkAt).ToString("F1") + "s" : "no") + " gravity=" + c.discGravity + sum);
                    return;
                }
                case "F12-recall-12":
                {
                    if (!c.pressed)
                    {
                        if (!c.teleported)
                        {
                            if (cs == KH_FrisbeeCatch.StateWaterCarry) { Networking.LocalPlayer.TeleportTo(F3Plaza(), Quaternion.identity); c.teleported = true; c.pressAt = now + .5; return; }
                            if (atRest || t > 90) F3Retry(c, now, "never in 12" + sum);
                            return;
                        }
                        if (now < c.pressAt) return;
                        c.stateAtPress = cs;
                        pouch.SetProgramVariable("_hsToyRow", -1);
                        pouch.SetProgramVariable("_hsLastGate", -99);
                        pouch.SendCustomEvent("_Disc" + c.dog); c.pressed = true; c.checkAt = now + .3;
                        if (c.log.Count < 60) c.log.Add("menu _Disc" + c.dog + " in state " + cs + " at " + t.ToString("F2") + "s");
                        return;
                    }
                    if (!c.gateTested)
                    {
                        if (now < c.checkAt) return;
                        int pending = Get<int>(f3Recall, "_pendingRow"), hs = Get<int>(pouch, "_hsToyRow");
                        c.gateAfterPress = Get<int>(pouch, "_hsLastGate");   // Codex M3 review 7: the refusal must be InWater (not DogBusy)
                        var dp = f3Disc.transform.position - roots[c.dog].position;
                        c.recallRefusedOk = c.gateAfterPress == KH_ToyRecall.InWater && pending < 0 && hs < 0 && (cs == KH_FrisbeeCatch.StateWaterCarry || cs == KH_FrisbeeCatch.StateWaterExit || cs == KH_FrisbeeCatch.StateReturn || cs == KH_FrisbeeCatch.StateDrop) && dp.magnitude < 1f;
                        c.gateTested = true;
                        if (c.log.Count < 60) c.log.Add("after the press: gate=" + c.gateAfterPress + " pendingRow=" + pending + " hsToyRow=" + hs + " state=" + cs + " discToMouth=" + dp.magnitude.ToString("F2"));
                        F3Back(c);
                        return;
                    }
                    if (c.stateAtPress != KH_FrisbeeCatch.StateWaterCarry && c.stateAtPress != KH_FrisbeeCatch.StateWaterExit) { F3RetryWhenRest(c, now, atRest, "not in 12 / 13 at the press (" + c.stateAtPress + ")" + sum); return; }
                    if (!atRest && t < 150) return;
                    bool ok12 = atRest && c.recallRefusedOk && completed && c.discEnd <= 2.5f;
                    F3Finish(c, now, ok12, "recall in " + c.stateAtPress + " refused=" + c.recallRefusedOk + sum);
                    return;
                }
                case "F13-inwater":
                {
                    if (c.dryAt < 0 && Get<int>(f3Fetch, "waterPose") == 0) c.dryAt = now;
                    if ((c.dryAt < 0 || t < 4) && t < 25) return;
                    bool never = (c.stateMask & ~((1 << KH_FrisbeeCatch.StateIdle) | (1 << KH_FrisbeeCatch.StateReady))) == 0;
                    F3Finish(c, now, c.rejectInWater >= 1 && never && c.dryAt >= 0, "rejectInWater=" + c.rejectInWater + " catcherStayedIdle=" + never
                        + " dry=" + (c.dryAt >= 0 ? (c.dryAt - c.launchedAt).ToString("F1") + "s" : "no") + sum);
                    return;
                }
            }
        }

        // The player back at the thrower's site (F11 / F12 teleported to the plaza for the stage-1 take-out gate).
        static void F3Back(F3Case c) { Networking.LocalPlayer.TeleportTo(c.thrower, Quaternion.LookRotation(new Vector3(-c.side, 0, 0))); }

        static void F3RetryWhenRest(F3Case c, double now, bool atRest, string why)
        {
            if (!atRest && now - c.launchedAt < 150) return;
            F3Retry(c, now, why);
        }

        static void F3Retry(F3Case c, double now, string why)
        {
            c.retries.Add("attempt " + (c.retries.Count + 1) + " target=" + c.target.ToString("F2") + " v0=" + c.v0.ToString("F2") + ": " + why);
            c.attempts = c.retries.Count;
            if (c.attempts >= F3MaxAttempts) { F3Finish(c, now, false, "no attempt produced the case's shape (" + c.attempts + " attempts, all RETRY; see retries): " + why); return; }
            // Per-attempt facts start over; the case's log and the attempt count stay
            c.stateMask = 0; c.maxState = -1; c.poseMax = 0; c.rejectInWater = 0; c.catchOk = 0; c.exitSide = -2; c.stateAtPress = -1; c.discEnd = -1f;
            c.launchedAt = -1; c.restAt = -1; c.pressAt = -1; c.checkAt = -1; c.dryAt = -1; c.enter11 = -1;
            c.launched = c.dobonCarry = c.dobonMiss = c.teleported = c.pressed = c.gateTested = c.recallRefusedOk = c.floatingAfter = c.discGravity = false;
            c.claimWait11 = c.waterJump = c.missForced = false; c.gateAfterPress = -99;
            c.cancels.Clear();
            f3Catcher.SetProgramVariable("_debugClaimRefuse", false);
            f3Phase = 0; f3Step = 0; f3Next = now + 1; SaveF3Detail(null);
        }

        // N/A for this world's data (F4): not a failure of the run.
        static void F3Skip(F3Case c, double now, string reason)
        {
            c.status = "SKIP"; c.reason = reason;
            Current.status = "SKIP"; Current.reason = reason; Current.endedAt = now;
            F3After(now);
        }

        static void F3Finish(F3Case c, double now, bool pass, string reason)
        {
            if (f3Catcher != null) f3Catcher.SetProgramVariable("_debugClaimRefuse", false);
            c.status = pass ? "PASS" : "FAIL";
            // attempts = the RETRY count; the deciding attempt is the next one, unless every attempt was a RETRY
            c.reason = (c.attempts >= F3MaxAttempts ? "" : "attempt " + (c.attempts + 1) + " (after " + c.attempts + " RETRY): ") + reason;
            Current.status = c.status; Current.reason = c.reason; Current.endedAt = now;
            F3After(now);
        }

        static void F3After(double now)
        {
            SaveF3Detail(null); Save();
            if (caseIndex + 1 < report.cases.Count) { Next(); f3Phase = 0; f3Step = 0; f3Next = 0; return; }
            bool all = f3.cases.All(x => x.status == "PASS" || x.status == "SKIP");
            int n = f3.cases.Count(x => x.status == "PASS"), sk = f3.cases.Count(x => x.status == "SKIP");
            SaveF3Detail(all ? "FRISBEE_M3_PASS" : "FRISBEE_M3_FAIL");
            End(all ? "FRISBEE_M3_PASS" : "FRISBEE_M3_FAIL", all ? "Frisbee M3 water: " + n + " local cases passed, " + sk + " N/A (not network/device acceptance)" : "Frisbee M3 water: see frisbee-m3.json");
        }

        // ================================================================ the ドボン aims (2026-09-28, Codex consult + desk analysis)
        // 94b's first F2 / F3 run: every aim was a landing point near the water, with the dog 4.8 m back from the band edge - the jump
        // into the water existed geometrically but was 0.06-0.33 s late every time (B:eta), and where the disc crossed the dry bank in
        // the band the A (around) catch outscored it. The aims are now SEARCHED before the Play, in Edit mode:
        //   site      a usable bank slice (outside the bridge zones, 30 m or more from the spot's entry), from z 150 outward;
        //   dog       on the thrower's side, 2.5 m outside the band edge (lateral hw + 3.5), facing the water; the thrower 1.2 m
        //             behind it (lateral hw + 4.7, 0.8 m along the river), the release 1.2 m up and 0.4 m toward the water;
        //   prefilter the throw straight across the river (speed 3-9 m/s, elevation 5-40 deg; a hand's range) and the ground profile
        //             across the river measured with rays: a B (chase) jump whose take-off T0 is 0.2 m outside the band, whose landing
        //             L0 is 0.3 m inside the water, whose rise is in the dog's band (the Pome also near 0.25 m), with >= 0.15 s spare
        //             on the planner's own ETA;
        //   confirm   the real planner on the dog's proxy (KH_FrisbeeCatch._ProbePlan, the first Plan tick): kept only when it commits
        //             a jump that lands in the water with a plan slack >= F3AimMinSlack (so A / C / the ground never win there).
        // At most F3AimsPerDog aims per dog, F3AimsPerSite per site; the Play takes attempt k = aim k.
        [Serializable] public class F3AimRec
        {
            public int dog, side, code; public float z, catchT, rise, slack, dY, estSlack; public string mode, next;
            public Vector3 thrower, dogAt, p0, v0, standRoot, landPt;
        }
        [Serializable] public class F3AimSet
        {
            public string utc, scene; public List<F3AimRec> aims = new List<F3AimRec>(); public List<string> notes = new List<string>();
            // F13-inwater's site, searched with the aims (F3SearchF13): every condition the case uses in the Play, checked here once
            public bool f13Ok; public string f13Why = ""; public int f13Code = -1, f13Side; public float f13Z;
            public Vector3 f13Thrower, f13DogAt, f13Wade, f13Target, f13P0, f13V0;
        }
        const string F3AimFile = "Temp/frisbee-m3-aims.json";
        const float F3AimMinSlack = .15f;
        // The take-off the dog really reaches may be this far from the planned one (94c run 20260928-112611: 0.50-0.64 m), and the first
        // Plan may run this much late (the first frame past noticeDelay; T-up10 saw 0.33 s against 0.10): an aim must survive both.
        // (F3TakeoffDrift: back to 0.2 m on 2026-09-28 - the jump into the water now keeps its verified take-off, KeepFixedT0)
        const float F3TakeoffDrift = .2f, F3LateStart = .15f, F3YawOff = 25f;

        /// <summary>Edit mode: the same throw under the live differences the first Plan can meet - a late first frame (tNow + F3LateStart)
        /// and the dog turned F3YawOff either way. null = a jump into the water every time; else what one of them committed.
        /// Shared by the aim search and the Course/95 probe.</summary>
        public static string F3LiveOk(KH_FrisbeeCatch catcher, Vector3 p0, Vector3 v0, Vector3 dogAt, int side)
        {
            var fwd = new Vector3(-side, 0f, 0f);
            var tries = new[] {
                new object[] { "late", fwd, catcher.noticeDelay + F3LateStart },
                new object[] { "yaw+", Quaternion.AngleAxis(F3YawOff, Vector3.up) * fwd, -1f },
                new object[] { "yaw-", Quaternion.AngleAxis(-F3YawOff, Vector3.up) * fwd, -1f } };
            foreach (var t in tries)
            {
                var r = catcher._ProbePlan(p0, v0, dogAt, (Vector3)t[1], -1, -99f, (float)t[2], 0f, false);
                if (r == null || !r.ok) return (string)t[0] + " error";
                if (!(r.kind == "jump" && r.landWater)) return (string)t[0] + " " + r.kind + (r.kind == "jump" ? "/" + r.mode + "/land" : "") + " [B:" + r.whyB + "]";
            }
            return null;
        }
        const int F3AimsPerDog = 6, F3AimsPerSite = 2, F3AimSites = 120, F3AimConfirm = 10;   // sites 40 -> 120 (2026-09-28: the Lab found 2 in 40; the Pome stops at 6 well before)
        static F3AimSet f3AimSet;
        static readonly int[] F3DobonDogs = { 1, 2 };   // Pome (F2, F14b, F2m-*), Lab (F3)

        static bool F3IsDobon(string n) { return n == "F2-dobon-Pome" || n == "F3-dobon-Lab" || n == "F14b-claim-dobon" || n.StartsWith("F2m"); }

        static void F3EnsureAims() { F3DobonAims(1); }

        static List<F3AimRec> F3DobonAims(int dog)
        {
            if (f3AimSet == null)
            {
                if (EditorApplication.isPlaying)
                {
                    if (!File.Exists(F3AimFile)) f3AimSet = new F3AimSet { notes = new List<string> { F3AimFile + " missing: start from the Course/94b / 94c menu" } };
                    else f3AimSet = JsonUtility.FromJson<F3AimSet>(File.ReadAllText(F3AimFile));
                }
                else
                {
                    f3AimSet = F3SearchAims();
                    Directory.CreateDirectory("Temp");
                    File.WriteAllText(F3AimFile, JsonUtility.ToJson(f3AimSet, true));
                }
            }
            return f3AimSet.aims.Where(a => a.dog == dog).ToList();
        }

        /// <summary>Edit mode (the 94b / 94c menu): bind, search, write. null = every ドボン dog has an aim; else the reason.</summary>
        static string F3PrepareAims()
        {
            try
            {
                Bind();
                var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>();
                f3River = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_RiverData");
                f3Spot = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_WaterSpot");
                if (f3River == null || f3Spot == null) return "KH_RiverData / KH_WaterSpot missing";
                var bad = new List<string>();
                foreach (int dog in F3DobonDogs) if (F3DobonAims(dog).Count == 0) bad.Add("dog " + dog);
                foreach (var n in f3AimSet.notes) Debug.Log("[KH Course Walk] F3 aims: " + n);
                return bad.Count == 0 ? null : "no ドボン aim for " + string.Join(", ", bad.ToArray()) + " (" + string.Join("; ", f3AimSet.notes.ToArray()) + ")";
            }
            finally { manager = null; pouch = null; route = null; wander = null; roots = null; nodes = null; f3River = f3Spot = null; }
        }

        struct F3Cand { public float sp, el, t, rise, slack; }

        static F3AimSet F3SearchAims()
        {
            var set = new F3AimSet { utc = DateTime.UtcNow.ToString("o"), scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path };
            var ok = Get<bool[]>(f3River, "sliceOk"); float z0 = Get<float>(f3River, "sliceZ0"), st = Get<float>(f3River, "sliceStep");
            var entry = Get<Vector3>(f3Spot, "entry");
            var codes = new List<int>(); int count = ok.Length / 2;
            for (int k = 0; k < count; k++)
            {
                float z = z0 + k * st;
                if (RwBridgeZone(z, 3f) || Mathf.Abs(z - entry.z) < 30f) continue;
                if (ok[2 * k + 1]) codes.Add(2 * k + 1);
                if (ok[2 * k]) codes.Add(2 * k);
            }
            codes = codes.OrderBy(c => Mathf.Abs(z0 + (c / 2) * st - 150f)).ToList();
            foreach (int dog in F3DobonDogs)
            {
                var catcher = ProbeCatcherOf(dog);
                if (catcher == null) { set.notes.Add("dog " + dog + ": no KH_FrisbeeCatch proxy"); continue; }
                int found = 0, sites = 0, prefiltered = 0, confirmed = 0; var why = new Dictionary<string, int>();
                foreach (int code in codes)
                {
                    if (found >= F3AimsPerDog || sites >= F3AimSites) break;
                    sites++;
                    int side = (code & 1) == 1 ? 1 : -1; float z = z0 + (code / 2) * st;
                    Vector3 dogAt, thrower, p0;
                    var cands = F3Prefilter(catcher, dog, side, z, out dogAt, out thrower, out p0);
                    if (cands == null) { why["site"] = (why.ContainsKey("site") ? why["site"] : 0) + 1; continue; }
                    prefiltered += cands.Count;
                    // the best estimated slack first, and (Pome) the ones nearest a 0.25 m rise
                    var pick = cands.OrderByDescending(x => x.slack).Take(F3AimConfirm / 2)
                        .Concat(cands.OrderBy(x => Mathf.Abs(x.rise - .25f)).Take(F3AimConfirm / 2)).Distinct().ToList();
                    int perSite = 0;
                    foreach (var cd in pick)
                    {
                        if (perSite >= F3AimsPerSite || found >= F3AimsPerDog) break;
                        float rad = cd.el * Mathf.Deg2Rad;
                        var v0 = new Vector3(-side * cd.sp * Mathf.Cos(rad), cd.sp * Mathf.Sin(rad), 0f);
                        var r = catcher._ProbePlan(p0, v0, dogAt, new Vector3(-side, 0f, 0f), -1, -99f, -1f, 0f, false);
                        // The live differences (see F3LiveOk): the first Plan a frame late, and the dog turned 25 deg either way
                        // 94c run 20260928-114922 (Lab, z 162): the throw itself was refused (ReqCatch -> CanPlay -> PlayableAt of the release
                        // point), so the planner never ran in the Play. An aim must be a throw the dog accepts.
                        if (r != null && r.ok && !r.p0Playable) { const string pk = "p0-not-playable"; why[pk] = (why.ContainsKey(pk) ? why[pk] : 0) + 1; continue; }
                        string live = r != null && r.ok && r.kind == "jump" && r.landWater && r.slack >= F3AimMinSlack ? F3LiveOk(catcher, p0, r.v0, dogAt, side) : null;
                        if (live != null) { string lk = "live:" + live; why[lk] = (why.ContainsKey(lk) ? why[lk] : 0) + 1; continue; }
                        confirmed++;
                        string key = r == null || !r.ok ? "error" : r.kind + (r.kind == "jump" ? "/" + r.mode + (r.landWater ? "/water" : "/land") : "") + (r.kind == "jump" && r.landWater && r.slack < F3AimMinSlack ? "/slack" : "");
                        // The refusal is tallied with the planner's own reasons (the last instant each of A / B / C failed at: eta, rise,
                        // iter, slope, land-river, land-rise, land-iter, lane, landing ...) and whether the band scan found the disc at all
                        if (r != null && r.ok && !(r.kind == "jump" && r.landWater)) key += " [A:" + r.whyA + " B:" + r.whyB + " C:" + r.whyC + (r.bandIn < 0f ? " noband" : "") + (r.laneRefused > 0 ? " laneRefused" : "") + "]";
                        if (!(r != null && r.ok && r.kind == "jump" && r.landWater && r.slack >= F3AimMinSlack)) { why[key] = (why.ContainsKey(key) ? why[key] : 0) + 1; continue; }
                        set.aims.Add(new F3AimRec { dog = dog, side = side, code = code, z = z, thrower = thrower, dogAt = dogAt, p0 = p0, v0 = r.v0, catchT = r.catchT, rise = r.rise,
                            slack = r.slack, dY = r.dY, estSlack = cd.slack, mode = r.mode, next = r.runtimeNext, standRoot = r.standRoot, landPt = r.landPt });
                        found++; perSite++;
                    }
                }
                set.notes.Add("dog " + dog + ": " + found + " aims from " + sites + " sites (prefilter " + prefiltered + ", planner runs " + confirmed + ", refused "
                    + string.Join(" | ", why.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " = " + kv.Value).ToArray()) + ")");
            }
            F3SearchF13(set);
            set.notes.Add("F13: " + (set.f13Ok ? "site z=" + set.f13Z.ToString("F1") + " side=" + set.f13Side + " wade=" + set.f13Wade.ToString("F2") + " target=" + set.f13Target.ToString("F2") : "none (" + set.f13Why + ")"));
            return set;
        }

        /// <summary>Edit mode: F13-inwater's site, every condition the case meets in the Play checked in order, the first slice (nearest z 150,
        /// F3FindSlice's filters, its own side first) that passes all of them is kept:
        ///   wade     a 0.10-0.22 m wading spot 1 m or more inside the water on the slice's side (F3WadeSpot, Terrain);
        ///   wet      the runtime reads the dog wet there: in the river-wide water (_FetchWater) with more than 0.05 m by its grid (_DepthAt);
        ///   dog      the dog's bank point by the thrower on the NavMesh, outside the band (F3PlaceGeometry's rule);
        ///   along    dry ground 5 m along the bank from the thrower, outside the band (the throw's target: it must not float);
        ///   aim      a throw of 5-25 deg reaches it (F3Aim, error < 0.6 m).
        /// The first failed condition of each slice is tallied into f13Why when none passes.</summary>
        static void F3SearchF13(F3AimSet set)
        {
            var ok = Get<bool[]>(f3River, "sliceOk"); var ret = Get<Vector3[]>(f3River, "sliceReturn");
            float z0 = Get<float>(f3River, "sliceZ0"), st = Get<float>(f3River, "sliceStep");
            var entry = Get<Vector3>(f3Spot, "entry");
            var rd = UdonSharpEditor.UdonSharpEditorUtility.GetProxyBehaviour(f3River) as KH_RiverData;
            var lab = F3CatcherBehaviour(2);
            var keepC = f3Catcher; var keepD = f3Disc;
            f3Catcher = lab; f3Disc = lab != null ? Get<UdonBehaviour>(lab, "frisbee") : null;
            var why = new Dictionary<string, int>();
            try
            {
                if (rd == null || lab == null || f3Disc == null) { set.f13Why = "no KH_RiverData proxy / Lab catcher / disc"; return; }
                int c0 = Mathf.RoundToInt((150f - z0) / st), count = ok.Length / 2;
                for (int o = 0; o <= count; o++)
                    foreach (int sgn in new[] { 1, -1 })
                    {
                        int k = c0 + o * sgn; if (k < 0 || k >= count || (o == 0 && sgn < 0)) continue;
                        float z = z0 + k * st;
                        if (RwBridgeZone(z, 3f) || Mathf.Abs(z - entry.z) < 30f) continue;
                        for (int s = 1; s >= 0; s--)
                        {
                            int code = 2 * k + s; if (!ok[code]) continue;
                            int side = s == 1 ? 1 : -1;
                            string fail = null;
                            Vector3 wade, dogAt = Vector3.zero, target = Vector3.zero, p0 = Vector3.zero, v0 = Vector3.zero;
                            if (!F3WadeSpot(z, side, out wade)) fail = "wade";
                            else if (!rd._FetchWater(wade) || rd._DepthAt(wade) <= .05f) fail = "wet";
                            else
                            {
                                var thrower = ret[code];
                                NavMeshHit dh;
                                if (!NavMesh.SamplePosition(thrower + new Vector3(side * 1.2f, 0, .8f), out dh, 2, NavMesh.AllAreas) || RiverInBand(dh.position)) fail = "dog";
                                else
                                {
                                    dogAt = dh.position;
                                    RaycastHit g; var along = thrower + new Vector3(0f, 0f, z > 360f ? -5f : 5f);
                                    if (!Physics.Raycast(along + Vector3.up * 4f, Vector3.down, out g, 10f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore) || RiverInBand(g.point)) fail = "along";
                                    else
                                    {
                                        target = g.point;
                                        Vector3 toT = target - thrower; toT.y = 0f; toT.Normalize();
                                        p0 = thrower + Vector3.up * 1.2f + toT * .4f;
                                        float err;
                                        if (!F3Aim(p0, target, 5f, 25f, out v0, out err)) fail = "aim";
                                    }
                                }
                                if (fail == null)
                                {
                                    set.f13Ok = true; set.f13Code = code; set.f13Side = side; set.f13Z = z; set.f13Thrower = thrower; set.f13DogAt = dogAt;
                                    set.f13Wade = wade; set.f13Target = target; set.f13P0 = p0; set.f13V0 = v0;
                                    return;
                                }
                            }
                            why[fail] = (why.ContainsKey(fail) ? why[fail] : 0) + 1;
                        }
                    }
                set.f13Why = "no slice passes every condition (first failure per slice: " + string.Join(" ", why.Select(kv => kv.Key + "=" + kv.Value).ToArray()) + ")";
            }
            finally { f3Catcher = keepC; f3Disc = keepD; }
        }

        /// <summary>One site: the dog / thrower / release point and the aims (speed, elevation) that pass the ドボン geometry on the measured
        /// ground across the river (see the section header). null = the site cannot take the dog / thrower (NavMesh, band).</summary>
        static List<F3Cand> F3Prefilter(KH_FrisbeeCatch k, int dog, int side, float z, out Vector3 dogAt, out Vector3 thrower, out Vector3 p0)
        {
            dogAt = thrower = p0 = Vector3.zero;
            var r = KH_CourseGroundEditor.RiverAt(z); float hw = KH_CourseGroundEditor.HalfWidth(z), surf = r.y;
            NavMeshHit h;
            if (!NavMesh.SamplePosition(new Vector3(r.x + side * (hw + 3.5f), r.y, z + .8f), out h, 1f, NavMesh.AllAreas) || RiverLateral(h.position) < 2f) return null;
            dogAt = h.position;
            if (!NavMesh.SamplePosition(new Vector3(r.x + side * (hw + 4.7f), r.y, z), out h, 1f, NavMesh.AllAreas) || RiverLateral(h.position) < 3f) return null;
            thrower = h.position;
            p0 = thrower + Vector3.up * 1.2f + new Vector3(-side * .4f, 0f, 0f);
            // Ground across the river at this z, every 0.05 m of lateral offset L from the centre (L < hw: the riverbed under the water)
            const float step = .05f; float l0 = -hw, l1 = hw + 10f; int n = Mathf.CeilToInt((l1 - l0) / step) + 1;
            var gy = new float[n]; var ny = new float[n]; RaycastHit g;
            for (int i = 0; i < n; i++)
            {
                float L = l0 + i * step;
                bool hit = Physics.Raycast(new Vector3(r.x + side * L, surf + 8f, z), Vector3.down, out g, 20f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore);
                gy[i] = hit ? g.point.y : float.NaN; ny[i] = hit ? g.normal.y : 0f;
            }
            Func<float, float> G = L => { int i = Mathf.Clamp(Mathf.RoundToInt((L - l0) / step), 0, n - 1); return gy[i]; };
            // 94c run 20260928-112611: the first plan was the probe's (chase, water, slack 0.19-0.35), but the take-off the dog really
            // reached was 0.5-0.64 m off the planned one (KeepPlan re-derives the crouch start from the speed the dog has: 5.4-5.8 m/s
            // against the planned 4.8) and landed on the bank's steep face: "liftoff refused: take-off (slope)" / KeepPlan "drop=ground".
            // So the take-off must be jumpable ground over the whole stretch the dog may take off from, not at one point.
            float cosMax = Mathf.Cos(k.jumpMaxSlopeDeg * Mathf.Deg2Rad) - 1e-4f;
            Func<float, float, bool> TakeoffStretchOk = (a, b) =>
            {
                for (float L = a; L <= b + 1e-4f; L += step)
                {
                    int i = Mathf.Clamp(Mathf.RoundToInt((L - l0) / step), 0, n - 1);
                    if (float.IsNaN(gy[i]) || ny[i] < cosMax) return false;
                }
                return true;
            };
            bool tw = k.variant == KH_FrisbeeCatch.VariantTwist;
            Vector3 mouth = tw ? k.mouthOffsetCatchTwist : k.mouthOffsetCatch;
            float crouchDur = tw ? k.clipCrouchDurationTwist : k.clipCrouchDuration, riseDur = tw ? k.clipRiseDurationTwist : k.clipRiseDuration;
            float lo = Mathf.Max(k.jumpFloorRise, k.minRootRise), hi = k.maxRootRise, vT = Mathf.Clamp(k.chaseSprintSpeed * KH_FrisbeeCatch.ChaseTakeoffFraction, KH_FrisbeeCatch.RunJumpMinTakeoff, KH_FrisbeeCatch.RunJumpMaxTakeoff);
            var disc = Get<UdonBehaviour>(F3CatcherBehaviour(dog), "frisbee");
            float gr = Get<float>(disc, "gravity"), dk = Get<float>(disc, "drag"), lift = Get<float>(disc, "lift"), cap = Get<float>(disc, "liftCap");
            float curve = k.variant == KH_FrisbeeCatch.VariantCurve ? KH_FrisbeeCatch.CurveDefault : 0f;
            float dogL = (dogAt.x - r.x) * side, tNow = k.noticeDelay, band = hw + KH_RiverData.BandMargin;
            bool canSwim = Get<bool>(wander[dog], "waterSwimAllowed");
            float ratio = Get<float>(f3River, "fetchWadeRatio"); if (ratio <= .01f) ratio = KH_RiverData.DefaultFetchWadeRatio;
            float wadeLimit = ratio * Get<float>(wander[dog], "waterMouthHeight"), walkMouth = Get<float>(wander[dog], "waterSwimMouth");
            var outList = new List<F3Cand>();
            for (float sp = 3f; sp <= 9.001f; sp += .25f)
                for (float el = 5f; el <= 40.001f; el += 2f)
                {
                    float rad = el * Mathf.Deg2Rad;
                    var v0 = new Vector3(-side * sp * Mathf.Cos(rad), sp * Mathf.Sin(rad), 0f);
                    var best = new F3Cand { slack = -9f };
                    for (float t = tNow + .1f; t <= 4f; t += .01f)
                    {
                        var p = FbPositionAt(p0, v0, curve, t, gr, dk, lift, cap);
                        float L = (p.x - r.x) * side, gL = G(L);
                        if (L < hw ? p.y <= surf : (!float.IsNaN(gL) && p.y <= gL)) break;   // landed (on the water / on the bank)
                        // B chase: T0 = the catch root back along the flight by vT tUp; the rise from T0's ground (two passes)
                        float rise = Mathf.Clamp(p.y - (float.IsNaN(gL) ? p.y : gL) - mouth.y, lo, hi);
                        for (int it = 0; it < 2; it++)
                        {
                            float tU = Mathf.Sqrt(2f * rise / gr), gT = G(L + mouth.z + vT * tU);
                            if (float.IsNaN(gT)) { rise = -1f; break; }
                            rise = p.y - gT - mouth.y;
                            if (rise < lo || rise > hi) break;
                        }
                        if (rise < lo + .01f || rise > hi - .01f) continue;
                        float tUp = Mathf.Sqrt(2f * rise / gr), cr = crouchDur * tUp / riseDur;
                        float T0 = L + mouth.z + vT * tUp;   // (the catch root sits mouth.z behind the disc along the run)
                        if (T0 < band + F3TakeoffDrift) continue;                                                   // never in the band, even off by the drift
                        if (!TakeoffStretchOk(T0, T0)) continue;   // the take-off point itself jumpable (the planner checks it again)
                        float gT0 = G(T0), landY = G(T0 - vT * 2f * tUp);
                        if (float.IsNaN(gT0) || float.IsNaN(landY)) continue;
                        float tDown = Mathf.Sqrt(Mathf.Max(0f, 2f * (.5f * gr * tUp * tUp + gT0 - landY) / gr));
                        float L0 = T0 - vT * (tUp + tDown);
                        if (L0 > hw - .3f || L0 < -hw + .3f) continue;
                        // A dog that does not swim lands only where it can stand (DobonAt: the Fetch limit 0.85 M and the walk mouth's sink guard)
                        if (!canSwim) { float dep = surf - G(L0); if (float.IsNaN(dep) || dep > wadeLimit || walkMouth - dep < KH_FrisbeeCatch.DobonSinkGuard) continue; }
                        float cs = T0 + vT * cr, dL = dogL - cs; if (dL < .3f) continue;
                        float dist = Mathf.Sqrt(dL * dL + .64f);
                        float acc = k.chaseAcceleration, vm = k.chaseSprintSpeed, ad = vm * vm / (2f * acc);
                        float eta = dist < ad ? Mathf.Sqrt(2f * dist / acc) : vm / acc + (dist - ad) / vm;
                        float slack = t - tNow - cr - tUp - eta;
                        if (slack > best.slack) best = new F3Cand { sp = sp, el = el, t = t, rise = rise, slack = slack };
                    }
                    if (best.slack >= F3AimMinSlack) outList.Add(best);
                }
            return outList;
        }

        static UdonBehaviour F3CatcherBehaviour(int dog)
        {
            return UnityEngine.Object.FindObjectsOfType<UdonBehaviour>().FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_FrisbeeCatch" && Get<Transform>(u, "dogRoot") == roots[dog]);
        }

        static void SaveF3Detail(string status)
        {
            if (f3 == null || report == null) return;
            try
            {
                if (status != null) f3.status = status;
                Directory.CreateDirectory(Dir + "/" + report.runId);
                File.WriteAllText(Dir + "/" + report.runId + "/frisbee-m3.json", JsonUtility.ToJson(f3, true));
            }
            catch (Exception e) { Debug.LogError("[KH Course Walk] Frisbee M3 evidence write failed: " + e.Message); }
        }
    }
}
