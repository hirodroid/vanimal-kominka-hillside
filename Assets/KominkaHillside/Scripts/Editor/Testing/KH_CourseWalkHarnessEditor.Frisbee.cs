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
    // DES-DOG-005 M2 (docs/plans/2026-09-28-frisbee-anywhere.md §D-2 / §D-6 / §G "M2"), Course/94 ClientSim harness on the main
    // scene. Same run frame as the walk harness (journal, watchdog, PlayerData backup/restore, runInBackground restore, no scene
    // save, Play stop on any failure). Dog 0 (Corgi) and its own disc (the KH_FrisbeeCatch whose dogRoot is dog 0, its `frisbee`).
    // The throw is the disc's own path from a drop: the harness puts the disc at the hand point, writes the aimed velocity into
    // _pendingThrowVelocity / _hasPendingThrow and sends _CheckThrow (what OnDrop does a frame after a Use in aim mode), so the
    // real _Launch runs, ReqCatch reaches the dog and the flight is the real one. The velocity is solved in the Editor with a
    // copy of KH_Frisbee._PositionAt (the disc's heap values, the catcher's curve preset) against the saved colliders.
    //   F5a-river   thrown into the river centre 3 m above the stop before the z 190 bridge zone (the stop mirrors
    //               KH_RiverData._DriftStops on the heap data, as 88's RW-drift-bridge): lands on the water (log "landed in the
    //               water"), floats (gravity off, not kinematic, y = formula surface - radius x floatSink +- 0.06 m), drifts and
    //               stops at the expected z +- 0.3 m, still for 5 s, never sent to the bank; the dog never inside the band (formula,
    //               outside the spot) and back to Idle / Ready (it cancels: river-band / flight-ended - no water fetch in M2).
    //   F5c-grab    the disc floating from F5a: OnPickup (Udon event _onPickup, then _onDrop; no real hand) -> gravity on at once and
    //               the float flag cleared; nobody holds it, so the next look (<= 0.6 s) floats it again (the RC-09 reconcile).
    //   F5c-recall  the player on the plaza (the stage-1 take-out gate), menu "call the disc" (_Disc0): the disc by the player
    //               (<= 2.5 m) within 4 s with gravity on, and still with gravity 2 s later.
    //   F5b-spot    thrown into the water-play spot 2 m above its downstream end: floats and stops at the end (+- 0.5 m, KH_WaterSpot
    //               ._DownstreamZ), still for 5 s, never sent to the bank. The dog may be in its own water play (not checked here).
    //   F5d-roll    (Codex M2 review 5) thrown short onto the bank 35 m below the spot (out of its 25 m entry radius); once the dog
    //               is in Retrieve the harness moves the resting disc into the spot's water (the "rolled into the water" stand-in):
    //               cancel reason=cannot-play, never Return / Drop / a carry after the move, the disc still floating 4 s later.
    //   Every case: a dog frame in the spot's water while the catcher is in Chase / Retrieve / Pickup fails it (dogSpotChaseFrames;
    //               the dog's own water play in the spot is not counted).
    //   F6-bridge   thrown into the water 4 m from the z 190 bridge (its zone): bank return (log) within 2 s of the landing, then out
    //               of the band with gravity for 6 s, never floated; once the dog is back at Idle / Ready the disc must be free
    //               (not kinematic) with gravity (Codex M2 review 7).
    //   F7-nobank   the same where no usable bank is within reach (from the data; else a heap stand-in clears sliceOk within reach +
    //               3 m of z 150 for the case and restores it).
    //   F7b-edge    thrown onto the band-edge slope (half width + 0.6 m, the thrower's side): bank return, then out of the band with
    //               gravity for 8 s (no roll back into the water and float, design I-5).
    // Not simulable here: a real hand (VRC_Pickup), another Owner / a late joiner (VRCObjectSync carries the gravity), PCVR 42-43.
    // Evidence: Logs/kh-course-walk/<run>/frisbee-m2.json. Final status FRISBEE_M2_PASS / FRISBEE_M2_FAIL.
    public static partial class KH_CourseWalkHarnessEditor
    {
        const int FbDog = 0;
        const float FbFloatTol = .06f;
        [Serializable] public class FbCase
        {
            public string name, status = "PENDING", reason;
            public float z, side, expectStopZ = -1f, landError = -1f, floatDyMin = 999f, floatDyMax = -999f, stopZ = -1f;
            public Vector3 thrower, p0, v0, target, landedAt, bankAt;
            public double startedAt = -1, launchedAt = -1, landedT = -1, bankT = -1, decidedT = -1;
            public bool launched, floated, standIn, gravityAfterPickup, flagClearedAfterPickup, refloated, recalledNear, gravityStill;
            public int frames, dogBandFrames, dogSpotChaseFrames, bankLogs, waterLogs, floatLogs, cannotPlayLogs, catcherEnd = -1;
            public Vector3 injectAt;
            public double injectedT = -1;
            public bool retrieveSeen, carriedAfterInject;
            public List<string> log = new List<string>();
            [NonSerialized] public float lastZ; [NonSerialized] public double stillSince = -1, outSince = -1;
        }
        [Serializable] public class FbReport
        {
            public string runId, status = "RUNNING";
            public List<FbCase> cases = new List<FbCase>();
            public string limitations = "Single local ClientSim, dog 0 and its disc. The throw is the disc's _CheckThrow after the harness writes the aimed velocity (no hand, no Use input); the pickup is the Udon event only (no VRC_Pickup hold); the no-bank case may be a heap stand-in. Not network, Owner handover, late join or device acceptance (PCVR 42-43).";
        }
        static FbReport fb;
        static UdonBehaviour fbDisc, fbCatcher, fbRiver, fbSpot;
        static int fbPhase, fbStep;
        static double fbNext;
        static bool fbLogHooked;
        static bool[] fbSliceOkBackup;
        static FbCase FC => fb.cases[caseIndex];

        [MenuItem("Tools/KominkaHillside/Course/94 - Walk Harness Frisbee Anywhere (M2 float)")]
        public static void FrisbeeAnywhere()
        {
            Begin(false);
            report.frisbeeOnly = true; report.deadline = EditorApplication.timeSinceStartup + 720; Save();
        }

        static void MakeFrisbeeCases()
        {
            var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>();
            fbRiver = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_RiverData");
            fbSpot = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_WaterSpot");
            if (fbRiver == null || fbSpot == null) throw new InvalidOperationException("KH_RiverData / KH_WaterSpot missing");
            fbCatcher = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_FrisbeeCatch" && Get<Transform>(u, "dogRoot") == roots[FbDog]);
            if (fbCatcher == null) throw new InvalidOperationException("No KH_FrisbeeCatch for dog 0");
            object dv; fbDisc = fbCatcher.TryGetProgramVariable("frisbee", out dv) ? dv as UdonBehaviour : null;
            if (fbDisc == null) throw new InvalidOperationException("Dog 0's catcher has no disc");
            object rv; if (!fbDisc.TryGetProgramVariable("riverData", out rv) || rv == null) throw new InvalidOperationException("The disc has no riverData: run Course/89 first");
            if (Get<bool[]>(fbRiver, "sliceOk").Length < 2) throw new InvalidOperationException("River fetch data missing: run Course/90 first");
            fb = new FbReport { runId = report.runId };
            foreach (var name in new[] { "F5a-river", "F5c-grab", "F5c-recall", "F5b-spot", "F5d-roll", "F6-bridge", "F7-nobank", "F7b-edge" })
            { report.cases.Add(new Case { name = name, count = 1 }); fb.cases.Add(new FbCase { name = name }); }
            fbPhase = 0; fbStep = 0; fbNext = 0; fbSliceOkBackup = null;
            if (!fbLogHooked) { Application.logMessageReceived += FbLog; fbLogHooked = true; }
        }

        static void FbLog(string condition, string stack, LogType type)
        {
            if (fb == null || report == null || !report.frisbeeOnly || caseIndex < 0 || caseIndex >= fb.cases.Count || !EditorApplication.isPlaying || fbDisc == null) return;
            var c = FC;
            if (condition.StartsWith("[KH_Frisbee] "))
            {
                if (!condition.Contains(" disc=" + fbDisc.gameObject.name)) return;
                if (condition.Contains("moved to the bank")) { c.bankLogs++; if (c.bankT < 0) c.bankT = EditorApplication.timeSinceStartup - c.startedAt; }
                else if (condition.StartsWith("[KH_Frisbee] landed in the water")) c.waterLogs++;
                else if (condition.StartsWith("[KH_Frisbee] floating at")) c.floatLogs++;
                if (c.log.Count < 40) c.log.Add(condition);
                return;
            }
            if (condition.StartsWith("[KH_FrisbeeCatch] ") && condition.Contains(" dog=" + roots[FbDog].name)
                && (condition.Contains("req cancel") || condition.Contains("req reject") || condition.Contains("river stop") || condition.Contains("req accept")))
            {
                if (condition.Contains("req cancel") && condition.Contains("reason=cannot-play")) c.cannotPlayLogs++;
                if (c.log.Count < 40) c.log.Add(condition);
            }
        }

        static void FrisbeeTick(double now)
        {
            try { FbStepCase(now); }
            catch { FbRestoreSlices(); SaveFbDetail("FAIL"); throw; }
        }

        static bool FbSetupCase(string n) { return n != "F5c-grab" && n != "F5c-recall"; }

        static void FbStepCase(double now)
        {
            var c = FC;
            if (fbPhase >= 3) FbObserve(c);
            if (now < fbNext) return;
            if (fbPhase == 0) // menu detach for every dog, then wait until dog 0's disc play is at rest
            {
                if (fbStep < 6) { pouch.SendCustomEvent("_WalkOff" + fbStep++); fbNext = now + .3; return; }
                if (Get<int[]>(manager, "walkers").Any(w => w >= 0)) throw new InvalidOperationException("Detach did not complete");
                int st = Get<int>(fbCatcher, "state");
                if (st != KH_FrisbeeCatch.StateIdle && st != KH_FrisbeeCatch.StateReady)
                { if (now - Current.startedAt > 40) throw new TimeoutException("Dog 0's catcher did not return to Idle"); fbNext = now + .5; return; }
                if (!FbSetupCase(c.name)) { fbPhase = 3; return; }
                FbPlace(c); fbPhase = 1; fbNext = now + .6; return;
            }
            if (fbPhase == 1) { pouch.SendCustomEvent("_WalkLeft" + FbDog); fbPhase = 2; fbNext = now + .8; return; }
            if (fbPhase == 2) // detach here, so the released roaming home is this site
            {
                if (Get<int[]>(manager, "walkers")[FbDog] != Networking.LocalPlayer.playerId) throw new InvalidOperationException("Menu attach rejected for dog 0");
                pouch.SendCustomEvent("_WalkOff" + FbDog); fbPhase = 3; fbNext = now + 1.5; return;
            }
            if (fbPhase == 3) { fbPhase = 4; FbStart(c, now); return; }   // FbStart may finish the case (and reset fbPhase)
            if (fbPhase == 4) FbEvaluate(c, now);
        }

        // ---- geometry (formula river, independent of the baked data, as W5) ----
        static bool FbInSpot(Vector3 p)
        {
            var px = Get<float[]>(fbSpot, "polyX"); var pz = Get<float[]>(fbSpot, "polyZ");
            bool inside = false; int n = px.Length, j = n - 1;
            for (int i = 0; i < n; i++)
            {
                if ((pz[i] > p.z) != (pz[j] > p.z) && p.x < px[i] + (p.z - pz[i]) * (px[j] - px[i]) / (pz[j] - pz[i])) inside = !inside;
                j = i;
            }
            return inside;
        }
        static float FbSpotEnd() { return Get<float[]>(fbSpot, "polyZ").Min() + .5f; }
        static float FbFloatY(float z)
        {
            float sink = Get<float>(fbSpot, "floatSink"); if (sink <= 0f) sink = KH_WaterSpot.DefaultFloatSink;
            return KH_CourseGroundEditor.RiverAt(z).y - Get<float>(fbDisc, "radius") * sink;
        }
        static Rigidbody FbRb() { return fbDisc.transform.GetComponent<Rigidbody>(); }
        static bool FbFloatingNow() { var rb = FbRb(); return rb != null && !rb.isKinematic && !rb.useGravity; }

        static void FbPlace(FbCase c)
        {
            var bank = Get<Vector3[]>(fbRiver, "sliceBank"); var ok = Get<bool[]>(fbRiver, "sliceOk");
            float z0 = Get<float>(fbRiver, "sliceZ0"), st = Get<float>(fbRiver, "sliceStep"), reach = Get<float>(fbRiver, "bankReach");
            if (reach <= .01f) reach = KH_RiverData.DefaultBankReach;
            float z = -1f; int preferSide = 1; bool edge = false;
            if (c.name == "F5a-river")
            {
                // The drift stop before the z 190 bridge zone (RW-drift-bridge's mirror of _DriftStops), the landing 3 m above it
                float stop = -1f;
                foreach (float b in RiverBridges)
                {
                    float q0 = b + KH_RiverData.DefaultBridgeZoneHalfZ + 4f;
                    if (!RwHasBank(q0, ok, bank, reach)) continue;
                    for (float q = q0; q > b; q -= .05f) if (RwBridgeZone(q - 1f, 0f) || !RwHasBank(q - 1f, ok, bank, reach)) { stop = q; break; }
                    if (stop > 0f) break;
                }
                if (stop < 0f) throw new InvalidOperationException("F5a: no drift stop found in the data");
                z = stop + 3f; c.expectStopZ = stop;
                if (!RwHasBank(z, ok, bank, reach) || RwBridgeZone(z, 0f)) throw new InvalidOperationException("F5a: the landing z " + z + " would not float");
            }
            else if (c.name == "F5b-spot")
            {
                float end = FbSpotEnd(); z = end + 2f; c.expectStopZ = end;
                float mean = Get<float[]>(fbSpot, "polyX").Average();
                preferSide = mean >= KH_CourseGroundEditor.RiverAt(z).x ? 1 : -1;
            }
            else if (c.name == "F5d-roll")
            {
                // 35 m below the spot's entry (a free dog within 25 m of it starts its water play), on the spot's bank
                var entry = Get<Vector3>(fbSpot, "entry");
                z = entry.z - 35f;
                float mean = Get<float[]>(fbSpot, "polyX").Average();
                preferSide = mean >= KH_CourseGroundEditor.RiverAt(z).x ? 1 : -1;
            }
            else if (c.name == "F6-bridge") z = 194f;
            else if (c.name == "F7-nobank")
            {
                for (float q = 5f; q <= 715f && z < 0f; q += 1f) if (!RwBridgeZone(q, 3f) && !RwHasBank(q, ok, bank, reach) && !FbInSpot(new Vector3(KH_CourseGroundEditor.RiverAt(q).x, 0f, q))) z = q;
                if (z < 0f)
                {
                    z = 150f; fbSliceOkBackup = (bool[])ok.Clone(); var cut = (bool[])ok.Clone();
                    for (int k = 0; k < cut.Length; k++) if (Mathf.Abs(z0 + (k / 2) * st - z) <= reach + 3f) cut[k] = false;
                    fbRiver.SetProgramVariable("sliceOk", cut); c.standIn = true;
                    c.log.Add("no stretch without a bank in the data: heap stand-in around z 150 (sliceOk cleared within " + (reach + 3f) + " m)");
                }
            }
            else if (c.name == "F7b-edge") { z = 120f; edge = true; }
            c.z = z;
            var r = KH_CourseGroundEditor.RiverAt(z); float hw = KH_CourseGroundEditor.HalfWidth(z);
            int side = preferSide; Vector3 site = Vector3.zero; bool found = false;
            foreach (int s in new[] { preferSide, -preferSide })
            {
                foreach (float outside in new[] { 3f, 4f, 5f, 6f }) if (RiverTrySite(z, s, outside, out site)) { found = true; break; }
                if (found) { side = s; break; }
            }
            if (!found) throw new InvalidOperationException(c.name + ": no bank site at z " + z);
            c.side = side; c.thrower = site;
            if (c.name == "F5b-spot")
            {
                // The water point of the spot at this z nearest the river centre (inside the polygon and the formula water)
                float bestX = float.NaN;
                for (float dx = 0f; dx <= hw && float.IsNaN(bestX); dx += .25f)
                    foreach (float sg in new[] { 1f, -1f })
                    { var q = new Vector3(r.x + sg * dx, r.y, z); if (Mathf.Abs(q.x - r.x) <= hw - .3f && FbInSpot(q)) { bestX = q.x; break; } }
                if (float.IsNaN(bestX)) throw new InvalidOperationException("F5b: no spot water at z " + z);
                c.target = new Vector3(bestX, r.y, z);
            }
            else if (edge)
            {
                float x = r.x + side * (hw + .6f); RaycastHit g;
                if (!Physics.Raycast(new Vector3(x, r.y + 4f, z), Vector3.down, out g, 10f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore)) throw new InvalidOperationException("F7b: no ground on the band edge");
                c.target = g.point;
                if (RiverLateral(c.target) >= 0f) throw new InvalidOperationException("F7b: the edge target is not in the band");
            }
            else c.target = new Vector3(r.x, r.y, z);
            if (c.name == "F5d-roll")
            {
                // A short flat throw down the bank (away from the dog, which stands 0.8 m up the river): it lands in about 0.5 s,
                // too soon to be caught, so the dog retrieves it from the ground. The move target is F5b's spot water point.
                RaycastHit gl;
                var aim = site + new Vector3(0f, 0f, -2.5f);
                if (!Physics.Raycast(aim + Vector3.up * 3f, Vector3.down, out gl, 8f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore) || RiverLateral(gl.point) < 1f)
                    throw new InvalidOperationException("F5d: no dry ground 2.5 m down the bank from " + site);
                c.target = gl.point;
                float end = FbSpotEnd(), wz = end + 2f; var wr = KH_CourseGroundEditor.RiverAt(wz); float whw = KH_CourseGroundEditor.HalfWidth(wz);
                float wx = float.NaN;
                for (float dx = 0f; dx <= whw && float.IsNaN(wx); dx += .25f)
                    foreach (float sg in new[] { 1f, -1f })
                    { var q = new Vector3(wr.x + sg * dx, wr.y, wz); if (Mathf.Abs(q.x - wr.x) <= whw - .3f && FbInSpot(q)) { wx = q.x; break; } }
                if (float.IsNaN(wx)) throw new InvalidOperationException("F5d: no spot water at z " + wz);
                c.injectAt = new Vector3(wx, FbFloatY(wz), wz);
            }
            var facing = Quaternion.LookRotation(new Vector3(-side, 0, 0));
            Networking.LocalPlayer.TeleportTo(site, facing);
            NavMeshHit dog;
            if (!NavMesh.SamplePosition(site + new Vector3(side * 1.2f, 0, .8f), out dog, 2, NavMesh.AllAreas) || RiverInBand(dog.position)) throw new InvalidOperationException(c.name + ": dog placement lacks NavMesh");
            roots[FbDog].SetPositionAndRotation(dog.position, facing);
            var sync = roots[FbDog].GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (sync != null) sync.FlagDiscontinuity();
            // The hand point and the aimed velocity (the disc's own flight, the catcher's curve preset)
            Vector3 toT = c.target - site; toT.y = 0f; toT.Normalize();
            c.p0 = site + Vector3.up * 1.2f + toT * .4f;
            if (c.name == "F5d-roll") { c.v0 = toT * 3.5f; c.log.Add("site z=" + z.ToString("F2") + " side=" + side + " thrower=" + site.ToString("F2") + " v0=" + c.v0.ToString("F2") + " moveTo=" + c.injectAt.ToString("F2")); return; }
            if (!FbAim(c.p0, c.target, out c.v0, out c.landError)) throw new InvalidOperationException(c.name + ": no throw reaches " + c.target + " (best error " + c.landError.ToString("F2") + " m)");
            c.log.Add("site z=" + z.ToString("F2") + " side=" + side + " thrower=" + site.ToString("F2") + " target=" + c.target.ToString("F2") + " v0=" + c.v0.ToString("F2")
                + " predictedError=" + c.landError.ToString("F2") + (c.expectStopZ > 0f ? " expectStop=" + c.expectStopZ.ToString("F2") : ""));
        }

        // A copy of KH_Frisbee._PositionAt (closed form) with the disc's heap values.
        static Vector3 FbPositionAt(Vector3 p0, Vector3 v0, float curve, float t, float g, float k, float lift, float liftCap)
        {
            float vh2 = v0.x * v0.x + v0.z * v0.z, cap = lift > 1e-6f ? liftCap * g / lift : 0f; if (vh2 > cap) vh2 = cap;
            float s = k > 1e-4f ? (1f - Mathf.Exp(-k * t)) / k : t, liftTerm, sideTerm;
            if (k > 1e-4f)
            {
                liftTerm = lift * vh2 * (t / (2f * k) - (1f - Mathf.Exp(-2f * k * t)) / (4f * k * k));
                sideTerm = curve * vh2 / k * ((1f - Mathf.Exp(-k * t)) / k - (1f - Mathf.Exp(-2f * k * t)) / (2f * k));
            }
            else { liftTerm = .5f * lift * vh2 * t * t; sideTerm = .5f * curve * vh2 * t * t; }
            var flat = new Vector3(v0.x, 0f, v0.z); var side = flat.magnitude < 1e-4f ? Vector3.zero : Vector3.Cross(Vector3.up, flat.normalized);
            return new Vector3(p0.x + v0.x * s + side.x * sideTerm, p0.y + v0.y * t - .5f * g * t * t + liftTerm, p0.z + v0.z * s + side.z * sideTerm);
        }

        /// <summary>Speed 3..11 m/s and elevation -5..40 deg toward the target; the landing = the first collider on the path or the
        /// first descending crossing of the target's height. Best = the smallest horizontal error; ok when it is under 0.6 m.</summary>
        static bool FbAim(Vector3 p0, Vector3 target, out Vector3 best, out float bestErr)
        {
            float g = Get<float>(fbDisc, "gravity"), k = Get<float>(fbDisc, "drag"), lift = Get<float>(fbDisc, "lift"), cap = Get<float>(fbDisc, "liftCap");
            float curve = Get<int>(fbCatcher, "variant") == KH_FrisbeeCatch.VariantCurve ? KH_FrisbeeCatch.CurveDefault : 0f;
            Vector3 dir = target - p0; dir.y = 0f; dir.Normalize();
            best = Vector3.zero; bestErr = float.MaxValue;
            int mask = (1 << 0) | (1 << 11);
            for (float el = -5f; el <= 40f; el += 1.5f)
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

        static void FbThrow(FbCase c, double now)
        {
            var t = fbDisc.transform;
            t.position = c.p0;
            fbDisc.SetProgramVariable("_pendingThrowVelocity", c.v0);
            fbDisc.SetProgramVariable("_hasPendingThrow", true);
            fbDisc.SendCustomEvent("_CheckThrow");
            c.launched = Get<bool>(fbDisc, "_flying"); c.launchedAt = now - c.startedAt;
            if (!c.launched) throw new InvalidOperationException(c.name + ": the disc did not launch (_CheckThrow)");
        }

        static void FbStart(FbCase c, double now)
        {
            c.startedAt = now;
            if (c.name == "F5c-grab")
            {
                if (!FbFloatingNow()) { FbFinish(c, now, false, "no floating disc to take (F5a did not leave one)"); return; }
                fbDisc.SendCustomEvent("_onPickup");
                var rb = FbRb();
                c.gravityAfterPickup = rb != null && rb.useGravity;
                c.flagClearedAfterPickup = !Get<bool>(fbDisc, "_floating");
                fbDisc.SendCustomEvent("_onDrop");
                c.log.Add("after _onPickup: gravity=" + c.gravityAfterPickup + " floatingFlag=" + !c.flagClearedAfterPickup);
                return;
            }
            if (c.name == "F5c-recall")
            {
                if (!FbFloatingNow()) { FbFinish(c, now, false, "no floating disc to call (F5a / F5c-grab did not leave one)"); return; }
                var centre = Get<Vector3>(fbCatcher, "playAreaCenter"); RaycastHit g;
                if (Physics.Raycast(centre + Vector3.up * 3f, Vector3.down, out g, 8f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore)) centre = g.point;
                Networking.LocalPlayer.TeleportTo(centre, Quaternion.identity);
                c.thrower = centre;
                fbNext = now + 1.0;   // the teleport lands first; the menu press is in FbEvaluate
                return;
            }
            FbThrow(c, now);
        }

        static void FbObserve(FbCase c)
        {
            if (c.startedAt < 0) return;
            c.frames++;
            var p = roots[FbDog].position;
            if (RiverInBand(p) && RiverLateral(p) < -RiverTolerance)
            {
                if (!FbInSpot(p)) c.dogBandFrames++;
                else
                {
                    // In the spot's water: the dog's own water play is allowed, a disc chase is not (Codex M2 review 7)
                    int st = Get<int>(fbCatcher, "state");
                    if (st == KH_FrisbeeCatch.StateChase || st == KH_FrisbeeCatch.StateRetrieve || st == KH_FrisbeeCatch.StatePickup) c.dogSpotChaseFrames++;
                }
            }
        }

        static void FbEvaluate(FbCase c, double now)
        {
            double t = now - c.startedAt;
            var dp = fbDisc.transform.position;
            int cs = Get<int>(fbCatcher, "state");
            bool catcherRest = cs == KH_FrisbeeCatch.StateIdle || cs == KH_FrisbeeCatch.StateReady;
            if (c.name == "F5c-grab")
            {
                if (!c.refloated && FbFloatingNow()) { c.refloated = true; c.decidedT = t; }
                if (!c.refloated && t < .6) return;
                bool okG = c.gravityAfterPickup && c.flagClearedAfterPickup && c.refloated && c.bankLogs == 0;
                FbFinish(c, now, okG, "gravityAfterPickup=" + c.gravityAfterPickup + " flagCleared=" + c.flagClearedAfterPickup + " refloated=" + c.refloated
                    + (c.refloated ? " at " + c.decidedT.ToString("F2") + "s" : "") + " bankLogs=" + c.bankLogs);
                return;
            }
            if (c.name == "F5c-recall")
            {
                if (c.launchedAt < 0) { pouch.SendCustomEvent("_Disc" + FbDog); c.launchedAt = t; c.log.Add("menu _Disc" + FbDog + " at " + t.ToString("F2") + "s"); return; }
                var pl = Networking.LocalPlayer.GetPosition(); var d = dp - pl; d.y = 0f;
                var rb = FbRb();
                if (!c.recalledNear && d.magnitude <= 2.5f && rb != null && rb.useGravity) { c.recalledNear = true; c.decidedT = t; }
                if (!c.recalledNear) { if (t - c.launchedAt > 4) FbFinish(c, now, false, "not brought by the player with gravity within 4 s: distance=" + d.magnitude.ToString("F2") + " gravity=" + (rb != null && rb.useGravity)); return; }
                if (t - c.decidedT < 2) return;
                c.gravityStill = rb != null && rb.useGravity && !Get<bool>(fbDisc, "_floating");
                FbFinish(c, now, c.gravityStill && c.bankLogs == 0, "near=" + c.recalledNear + " at " + (c.decidedT - c.launchedAt).ToString("F2") + "s gravity2s=" + c.gravityStill + " bankLogs=" + c.bankLogs);
                return;
            }
            bool flying = Get<bool>(fbDisc, "_flying");
            if (c.name == "F5d-roll") { FbEvaluateRoll(c, now, t, flying, cs, catcherRest); return; }
            if (c.landedT < 0)
            {
                if (flying) { if (t > 10) FbFinish(c, now, false, "still flying after 10 s"); return; }
                c.landedT = t; c.landedAt = dp;
                c.log.Add("flight over at " + t.ToString("F2") + "s disc=" + dp.ToString("F2"));
            }
            bool floatCase = c.name == "F5a-river" || c.name == "F5b-spot";
            if (floatCase)
            {
                if (FbFloatingNow()) c.floated = true;
                if (c.floated && t - c.landedT > 1.5)
                {
                    float dy = dp.y - FbFloatY(dp.z);
                    c.floatDyMin = Mathf.Min(c.floatDyMin, dy); c.floatDyMax = Mathf.Max(c.floatDyMax, dy);
                    if (Mathf.Abs(dp.z - c.lastZ) > .05f) { c.lastZ = dp.z; c.stillSince = t; }
                    if (c.stillSince < 0) c.stillSince = t;
                }
                bool still = c.floated && c.stillSince >= 0 && t - c.stillSince >= 5 && t - c.landedT > 6;
                if (!still && t < 75) return;
                if (still && !catcherRest && t < 90) return;   // let the dog finish its cancel
                float tolZ = c.name == "F5a-river" ? .3f : .5f;
                c.stopZ = dp.z; c.catcherEnd = cs;
                var landErr = c.landedAt - c.target; landErr.y = 0f;
                bool ok = c.waterLogs >= 1 && c.floated && FbFloatingNow() && still && Mathf.Abs(dp.z - c.expectStopZ) <= tolZ
                    && c.floatDyMin >= -FbFloatTol && c.floatDyMax <= FbFloatTol && c.bankLogs == 0 && c.dogBandFrames == 0 && c.dogSpotChaseFrames == 0 && catcherRest;
                FbFinish(c, now, ok, "waterLog=" + c.waterLogs + " floated=" + c.floated + " floatingNow=" + FbFloatingNow() + " still5s=" + still
                    + " stop z=" + dp.z.ToString("F2") + " expected=" + c.expectStopZ.ToString("F2") + " dy=[" + c.floatDyMin.ToString("F3") + "," + c.floatDyMax.ToString("F3") + "]"
                    + " landErr=" + landErr.magnitude.ToString("F2") + " bankLogs=" + c.bankLogs + " dogBandFrames=" + c.dogBandFrames + " spotChaseFrames=" + c.dogSpotChaseFrames + " catcher=" + cs);
                return;
            }
            // Bank-return cases (F6 / F7 / F7b)
            if (FbFloatingNow() || c.floatLogs > 0) c.floated = true;
            if (c.bankT < 0) { if (t - c.landedT > 2.5) FbFinish(c, now, false, "no bank return within 2 s of the landing (floated=" + c.floated + " disc=" + dp.ToString("F2") + ")"); return; }
            if (c.bankAt == Vector3.zero) c.bankAt = dp;
            bool outNow = !RiverInBand(dp);
            // In a mouth (the dog fetched it from the bank) it is kinematic; once the dog is back at Idle / Ready it must be free
            // with gravity (Codex M2 review 7: a frozen disc is not a pass)
            var rbb = FbRb();
            bool free = rbb != null && !rbb.isKinematic && rbb.useGravity;
            bool gravity = catcherRest ? free : rbb != null && (rbb.isKinematic || rbb.useGravity);
            if (outNow && gravity && !c.floated) { if (c.outSince < 0) c.outSince = t; }
            else c.outSince = -1;
            double hold = c.name == "F7b-edge" ? 8 : 6;
            bool held = c.outSince >= 0 && t - c.outSince >= hold;
            if (!held && t - c.bankT < 20) return;
            if (held && !catcherRest && t - c.bankT < 40) return;
            c.catcherEnd = cs;
            bool okB = held && c.bankLogs >= 1 && c.waterLogs == 0 && !c.floated && c.dogBandFrames == 0 && c.dogSpotChaseFrames == 0 && catcherRest && free;
            FbFinish(c, now, okB, "bankLogs=" + c.bankLogs + " at " + (c.bankT - c.landedT).ToString("F2") + "s after the landing, out+gravity " + hold + "s=" + held
                + " waterLogs=" + c.waterLogs + " floated=" + c.floated + " disc=" + dp.ToString("F2") + " dogBandFrames=" + c.dogBandFrames + " spotChaseFrames=" + c.dogSpotChaseFrames + " freeAtEnd=" + free + " catcher=" + cs + (c.standIn ? " (heap stand-in)" : ""));
        }

        // F5d-roll: land on the bank -> Retrieve -> the harness moves the resting disc into the spot's water -> the dog gives up.
        static void FbEvaluateRoll(FbCase c, double now, double t, bool flying, int cs, bool catcherRest)
        {
            if (c.injectedT < 0)
            {
                if (flying) { if (t > 10) FbFinish(c, now, false, "still flying after 10 s"); return; }
                if (c.landedT < 0) { c.landedT = t; c.landedAt = fbDisc.transform.position; c.log.Add("flight over at " + t.ToString("F2") + "s disc=" + c.landedAt.ToString("F2")); }
                if (cs == KH_FrisbeeCatch.StateRetrieve)
                {
                    c.retrieveSeen = true;
                    var tr = fbDisc.transform; var rb = FbRb();
                    if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                    tr.position = c.injectAt;
                    var ds = tr.GetComponent<VRC.SDK3.Components.VRCObjectSync>(); if (ds != null) ds.FlagDiscontinuity();
                    c.injectedT = t; c.log.Add("moved the resting disc into the spot water at " + t.ToString("F2") + "s (catcher Retrieve)");
                    return;
                }
                bool carrying = cs == KH_FrisbeeCatch.StateJumpCarry || cs == KH_FrisbeeCatch.StateLandCarry || cs == KH_FrisbeeCatch.StateReturn || cs == KH_FrisbeeCatch.StateDrop;
                if (carrying || t - c.landedT > 8)
                    FbFinish(c, now, false, "the dog never retrieved the disc from the ground (state " + cs + (carrying ? ", caught it" : "") + "); the case could not set up its move");
                return;
            }
            if (cs == KH_FrisbeeCatch.StateJumpCarry || cs == KH_FrisbeeCatch.StateLandCarry || cs == KH_FrisbeeCatch.StateReturn || cs == KH_FrisbeeCatch.StateDrop) c.carriedAfterInject = true;
            if (FbFloatingNow()) c.floated = true;
            double since = t - c.injectedT;
            if (since < 4 || (!catcherRest && since < 15)) return;
            c.catcherEnd = cs;
            bool ok = c.cannotPlayLogs >= 1 && !c.carriedAfterInject && c.floated && FbFloatingNow() && catcherRest && c.dogSpotChaseFrames == 0 && c.dogBandFrames == 0;
            FbFinish(c, now, ok, "cannotPlayLogs=" + c.cannotPlayLogs + " carriedAfterMove=" + c.carriedAfterInject + " floated=" + c.floated + " floatingNow=" + FbFloatingNow()
                + " catcher=" + cs + " spotChaseFrames=" + c.dogSpotChaseFrames + " dogBandFrames=" + c.dogBandFrames);
        }

        static void FbRestoreSlices()
        {
            if (fbSliceOkBackup == null || fbRiver == null) return;
            try { fbRiver.SetProgramVariable("sliceOk", fbSliceOkBackup); } catch { }
            fbSliceOkBackup = null;
        }

        static void FbFinish(FbCase c, double now, bool pass, string reason)
        {
            FbRestoreSlices();
            c.status = pass ? "PASS" : "FAIL"; c.reason = reason;
            Current.status = c.status; Current.reason = reason; Current.endedAt = now;
            SaveFbDetail(null); Save();
            if (caseIndex + 1 < report.cases.Count) { Next(); fbPhase = 0; fbStep = 0; fbNext = 0; return; }
            bool all = fb.cases.All(x => x.status == "PASS");
            SaveFbDetail(all ? "FRISBEE_M2_PASS" : "FRISBEE_M2_FAIL");
            End(all ? "FRISBEE_M2_PASS" : "FRISBEE_M2_FAIL", all ? "Frisbee M2 float: eight local cases passed (not network/device acceptance)" : "Frisbee M2 float: see frisbee-m2.json");
        }

        static void SaveFbDetail(string status)
        {
            if (fb == null || report == null) return;
            try
            {
                if (status != null) fb.status = status;
                Directory.CreateDirectory(Dir + "/" + report.runId);
                File.WriteAllText(Dir + "/" + report.runId + "/frisbee-m2.json", JsonUtility.ToJson(fb, true));
            }
            catch (Exception e) { Debug.LogError("[KH Course Walk] Frisbee evidence write failed: " + e.Message); }
        }
    }
}
