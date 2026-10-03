using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-WLD-008 Video/2b - Probe Poses (Edit mode, no Play, no scene change). Settles the numbers the V2 / V3 harness cases would assert
    /// by formula first (Codex pre-run consult 2026-10-03).
    ///
    /// It calls the SAME static pose functions the runtime calls (KH_VideoCamera.DogFollowPos / DogFollowLook / SelfFollowPos / SelfFollowLook /
    /// LeadOffset / FrameDistanceFor / BlendFactor / SmoothStep / LookRoll0) with the builder's tuning table (the values Video/1 writes), on
    /// synthetic subjects stepped at 30 fps. Mirrored here (not shared): the travel-velocity filter (one Lerp with BlendFactor, as in
    /// KH_VideoCamera.Heading) and the cut rule (distance > cutDistance), each one line. NOT modelled: terrain / wall clipping, the floor lift,
    /// the dog push-out, the turn time constant, frame-time jitter, the photo camera. Results: Temp/kh-video/probe.json and one VIDEO_PROBE line.
    /// </summary>
    public static partial class KH_VideoCameraEditor
    {
        [Serializable] class ProbeRow { public string scenario; public int frames; public float distMin, distMax, worstFrame, rollMax, stepMax; public int outOfFrame; public bool cutOnJump; public string note; }
        [Serializable] class ProbeReport { public string utc, status; public float frameDistanceSmall, frameDistanceMid, frameDistanceLarge; public List<ProbeRow> rows = new List<ProbeRow>(); public List<string> lines = new List<string>(); }

        const float ProbeDt = 1f / 30f, Aspect = 16f / 9f, Inner = 0.8f;

        [MenuItem(Menu + "2b - Probe Poses")]
        public static void ProbePoses()
        {
            var r = new ProbeReport { utc = DateTime.UtcNow.ToString("o") };
            r.frameDistanceSmall = KH_VideoCamera.FrameDistanceFor(0.375f, DogFill, DogFov);
            r.frameDistanceMid = KH_VideoCamera.FrameDistanceFor(0.6f, DogFill, DogFov);
            r.frameDistanceLarge = KH_VideoCamera.FrameDistanceFor(0.9f, DogFill, DogFov);
            foreach (float headH in new[] { 0.25f, 0.4f, 0.6f })
                foreach (float speed in new[] { 0f, 1.5f, 3f })
                    r.rows.Add(DogWalk(headH, speed, false));
            r.rows.Add(DogWalk(0.4f, 3f, true));
            r.rows.Add(SelfStand(false));
            r.rows.Add(SelfStand(true));
            r.rows.Add(SelfWalk(1.4f));

            bool ok = true;
            foreach (var row in r.rows)
            {
                bool dog = row.scenario.StartsWith("dog");
                bool turn = row.scenario.Contains("turn") || row.scenario.Contains("(informative)");
                // dogs: 0.8 m is the hard floor (the wanted distance is DogDistMin; while a dog speeds up the camera trails into it for a moment because the
                // lead follows the filtered velocity (headingTau) more slowly than the lag builds (posTau). The node formula run of 2026-10-03 gives 0.91 m
                // for the smallest dog at 3 m/s). Rows below DogDistMin are flagged in the note
                bool distOk = dog ? row.distMin >= 0.8f && row.distMax <= 5.1f : row.distMin >= 1.5f && row.distMax <= 3.2f;
                if (dog && row.distMin < DogDistMin) row.note = (row.note ?? "") + " closer than DogDistMin " + DogDistMin + " while speeding up";
                bool rowOk = distOk && row.rollMax < 0.01f && (turn || row.outOfFrame == 0) && row.cutOnJump && (turn || row.stepMax <= 0.5f);
                if (!rowOk && !turn) ok = false;
                r.lines.Add((turn ? "info " : rowOk ? "ok   " : "FAIL ") + row.scenario + ": distance " + row.distMin.ToString("F2") + "-" + row.distMax.ToString("F2") + " m, out of inner 80% "
                    + row.outOfFrame + "/" + row.frames + " (worst |ndc| " + row.worstFrame.ToString("F2") + "), roll " + row.rollMax.ToString("F4") + ", max step " + row.stepMax.ToString("F2")
                    + " m, cut on jump " + row.cutOnJump + (string.IsNullOrEmpty(row.note) ? "" : " — " + row.note));
            }
            r.status = ok ? "PASS" : "FAIL";
            WriteJson(OutDir + "/probe.json", r);
            string head = "[KH Video] VIDEO_PROBE_" + r.status + " frame distance (fill " + DogFill + ", fov " + DogFov + "): size 0.375 → " + r.frameDistanceSmall.ToString("F2")
                + " m, 0.6 → " + r.frameDistanceMid.ToString("F2") + " m, 0.9 → " + r.frameDistanceLarge.ToString("F2") + " m (" + OutDir + "/probe.json)\n" + string.Join("\n", r.lines);
            if (ok) Debug.Log(head); else Debug.LogError(head);
        }

        /// <summary>A dog walking straight along +X (or turning 90° half way) at `speed` for 3 s, then a 20 m jump.</summary>
        static ProbeRow DogWalk(float headH, float speed, bool turnHalfway)
        {
            var row = new ProbeRow { scenario = "dog head " + headH + " m, " + speed + " m/s" + (turnHalfway ? ", 90° turn at 1.5 s" : ""), distMin = 1e9f };
            float size = headH * 1.5f;
            Vector3 root = Vector3.zero, dir = Vector3.right, vel = Vector3.zero, heading = Vector3.right;
            Vector3 cam = Vector3.zero, look = Vector3.zero;
            bool first = true;
            int frames = Mathf.RoundToInt(3f / ProbeDt);
            for (int f = 0; f <= frames; f++)
            {
                if (turnHalfway && f == frames / 2) dir = Vector3.forward;
                Vector3 prev = root;
                root += dir * speed * ProbeDt;
                Vector3 v = (root - prev) / ProbeDt; v.y = 0f;
                vel = Vector3.Lerp(vel, v, KH_VideoCamera.BlendFactor(ProbeDt, HeadingTau));            // mirror of KH_VideoCamera.Heading (velocity filter)
                if (vel.magnitude >= StillSpeed) heading = vel.normalized;
                Vector3 headPos = root + Vector3.up * headH, body = Vector3.Lerp(root, headPos, 0.75f);
                Vector3 want = KH_VideoCamera.DogFollowPos(body, headPos.y, heading, size, DogFill, DogFov, DogSideDeg, DogDistMin, DogDistMax, DogLift) + KH_VideoCamera.LeadOffset(vel, PosTau);
                Vector3 wantLook = KH_VideoCamera.DogFollowLook(body, heading, DogLookAhead) + KH_VideoCamera.LeadOffset(vel, LookTau);
                bool cut = first || (want - cam).magnitude > CutDistance;                                   // mirror of the cut rule in ApplyPose
                Vector3 nextCam = KH_VideoCamera.SmoothStep(cam, want, cut, ProbeDt, PosTau);
                look = KH_VideoCamera.SmoothStep(look, wantLook, cut, ProbeDt, LookTau);
                if (!first) row.stepMax = Mathf.Max(row.stepMax, (nextCam - cam).magnitude);
                cam = nextCam; first = false;
                if (f * ProbeDt < 0.6f) continue;   // the harness also starts sampling after 0.6 s
                Sample(row, cam, look, DogFov, body);
            }
            row.cutOnJump = JumpCuts(cam, root + Vector3.forward * 20f, headH, size, heading, vel);
            if (turnHalfway) row.note = "informative: the turn is not held to the step / framing limits (the runtime also lengthens the time constant for 0.5 s)";
            return row;
        }

        static bool JumpCuts(Vector3 cam, Vector3 root, float headH, float size, Vector3 heading, Vector3 vel)
        {
            Vector3 headPos = root + Vector3.up * headH, body = Vector3.Lerp(root, headPos, 0.75f);
            Vector3 want = KH_VideoCamera.DogFollowPos(body, headPos.y, heading, size, DogFill, DogFov, DogSideDeg, DogDistMin, DogDistMax, DogLift) + KH_VideoCamera.LeadOffset(vel, PosTau);
            return (want - cam).magnitude > CutDistance;
        }

        /// <summary>じぶん: standing (facing +Z), with or without one own dog beside; then a 15 m teleport.</summary>
        static ProbeRow SelfStand(bool withDog)
        {
            var row = new ProbeRow { scenario = "self standing" + (withDog ? ", one own dog at the feet 1.2 m ahead-right (informative)" : ", no own dog"), distMin = 1e9f };
            Vector3 head = new Vector3(0f, 1.6f, 0f), fwd = Vector3.forward;
            Vector3 dog = new Vector3(0.6f, 0.3f, 1.0f);
            Vector3 cam = Vector3.zero, look = Vector3.zero;
            bool first = true;
            for (int f = 0; f < 60; f++)
            {
                Vector3 want = KH_VideoCamera.SelfFollowPos(head, fwd, SelfSideDeg, SelfDistance, SelfLift);
                Vector3 wantLook = KH_VideoCamera.SelfFollowLook(head, dog, withDog ? 1 : 0, SelfLookToDogs, SelfChestDrop);
                bool cut = first || (want - cam).magnitude > CutDistance;
                Vector3 nextCam = KH_VideoCamera.SmoothStep(cam, want, cut, ProbeDt, PosTau);
                look = KH_VideoCamera.SmoothStep(look, wantLook, cut, ProbeDt, LookTau);
                if (!first) row.stepMax = Mathf.Max(row.stepMax, (nextCam - cam).magnitude);
                cam = nextCam; first = false;
                Sample(row, cam, look, SelfFov, head);
                if (withDog) Sample(row, cam, look, SelfFov, dog, countDistance: false);
            }
            Vector3 jumped = KH_VideoCamera.SelfFollowPos(head + fwd * 15f, fwd, SelfSideDeg, SelfDistance, SelfLift);
            row.cutOnJump = (jumped - cam).magnitude > CutDistance;
            var want0 = KH_VideoCamera.SelfFollowPos(head, fwd, SelfSideDeg, SelfDistance, SelfLift);
            if (withDog) row.note = "a dog at the feet falls below the frame with selfLookToDogs " + SelfLookToDogs + " / fov " + SelfFov + " at " + SelfDistance + " m (formula 2026-10-03: |ndc y| about 1.2; 0.6 brings it to about 0.8 with the head at about 0.86). Decide before W2; V3 does not leash a dog. "; else row.note = "camera at " + want0.ToString("F2") + " for head " + head + " facing +Z (V3 asserts this within 0.35 m when nothing clips)";
            return row;
        }

        /// <summary>じぶん walking straight at `speed` (no dog): the lead keeps the framing; distance is to the head.</summary>
        static ProbeRow SelfWalk(float speed)
        {
            var row = new ProbeRow { scenario = "self walking " + speed + " m/s, no own dog", distMin = 1e9f };
            Vector3 feet = Vector3.zero, vel = Vector3.zero, heading = Vector3.forward, cam = Vector3.zero, look = Vector3.zero;
            bool first = true;
            for (int f = 0; f < 90; f++)
            {
                Vector3 prev = feet; feet += Vector3.forward * speed * ProbeDt;
                vel = Vector3.Lerp(vel, (feet - prev) / ProbeDt, KH_VideoCamera.BlendFactor(ProbeDt, HeadingTau));
                if (vel.magnitude >= StillSpeed) heading = vel.normalized;
                Vector3 head = feet + Vector3.up * 1.6f;
                Vector3 want = KH_VideoCamera.SelfFollowPos(head, heading, SelfSideDeg, SelfDistance, SelfLift) + KH_VideoCamera.LeadOffset(vel, PosTau);
                Vector3 wantLook = KH_VideoCamera.SelfFollowLook(head, head, 0, SelfLookToDogs, SelfChestDrop) + KH_VideoCamera.LeadOffset(vel, LookTau);
                bool cut = first || (want - cam).magnitude > CutDistance;
                Vector3 nextCam = KH_VideoCamera.SmoothStep(cam, want, cut, ProbeDt, PosTau);
                look = KH_VideoCamera.SmoothStep(look, wantLook, cut, ProbeDt, LookTau);
                if (!first) row.stepMax = Mathf.Max(row.stepMax, (nextCam - cam).magnitude);
                cam = nextCam; first = false;
                if (f * ProbeDt < 0.6f) continue;
                Sample(row, cam, look, SelfFov, head);
            }
            row.cutOnJump = (KH_VideoCamera.SelfFollowPos(feet + Vector3.up * 1.6f + Vector3.forward * 15f, heading, SelfSideDeg, SelfDistance, SelfLift) - cam).magnitude > CutDistance;
            return row;
        }

        /// <summary>Distance, roll and whether `subject` lies in the inner 80 % of a 16:9 frame (the harness's viewport 0.1-0.9).</summary>
        static void Sample(ProbeRow row, Vector3 cam, Vector3 look, float fov, Vector3 subject, bool countDistance = true)
        {
            Quaternion rot = KH_VideoCamera.LookRoll0(cam, look);
            row.frames++;
            if (countDistance)
            {
                float d = (subject - cam).magnitude;
                row.distMin = Mathf.Min(row.distMin, d); row.distMax = Mathf.Max(row.distMax, d);
            }
            row.rollMax = Mathf.Max(row.rollMax, Mathf.Abs(Vector3.Dot(rot * Vector3.right, Vector3.up)));
            Vector3 l = Quaternion.Inverse(rot) * (subject - cam);
            float tanV = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), tanH = tanV * Aspect;
            float ndc = l.z <= 0.01f ? 99f : Mathf.Max(Mathf.Abs(l.x / l.z) / tanH, Mathf.Abs(l.y / l.z) / tanV);
            row.worstFrame = Mathf.Max(row.worstFrame, ndc);
            if (ndc > Inner) row.outOfFrame++;
        }
    }
}
