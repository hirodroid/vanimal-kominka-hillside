using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-WLD-008 Video/2b - Probe Poses (Edit mode, no Play, no scene change). Settles the framing numbers of every mode by formula.
    ///
    /// It calls the SAME static pose functions the runtime calls (KH_VideoCamera.*Pos / *Look / LeadOffset / FrameDistanceFor / BlendFactor /
    /// SmoothStep / LookRoll0) with the builder's tuning table (the values Video/1 writes), on synthetic subjects stepped at 30 fps.
    /// The frisbee rows fly a synthetic disc with KH_Frisbee._PositionAt of a disc in the open scene (the flight's own formula).
    /// Mirrored here (not shared), each one or a few lines: the travel-velocity filter (KH_VideoCamera.Heading), the cut rule (distance > cutDistance),
    /// the landing loop of KH_VideoCamera.PredictLanding (here against a flat ground y = 0 instead of the scene colliders), §16: the phase
    /// machine of みち (KH_VideoCamera.PathMove, on a straight walk without a trail; the anchor's ground ray is the flat ground) and the orbit's
    /// progress (KH_VideoCamera.Orbit). A cut the runtime makes on purpose (Pose.cut, a new みち move) is a cut here too, not a step;
    /// planned cuts must equal the row's cutsWant and cuts caused only by the distance rule must be 0 (Codex review 2026-10-08).
    /// NOT modelled: terrain / wall clipping, the floor lift, the dog push-out, the turn time constant, frame-time jitter, the mode-selection logic
    /// of 通り過ぎ (only its framing from a fixed point), the photo camera. Results: Temp/kh-video/probe.json and one VIDEO_PROBE line.
    /// </summary>
    public static partial class KH_VideoCameraEditor
    {
        [Serializable] class ProbeRow
        {
            public string scenario; public int frames; public float distMin, distMax, worstFrame, rollMax, stepMax, wantMin, wantMax, stepLimit = 0.5f;
            public int outOfFrame; public bool cutOnJump, jumpApplies = true, informative; public string note;
            // §16: cuts inside the run, みち moves finished (want ≥ movesWant). Codex review (2026-10-08): a cut the mode asked for (Pose.cut) is
            // planCuts and must equal cutsWant (0 unless the row says so: みち = one per finished move); a cut only because the wanted point ran
            // more than cutDistance away from the camera is distCuts and must be 0 (it was hidden from stepMax before, so a sudden jump passed)
            public int cuts, planCuts, distCuts, cutsWant, movesWant = -1, movesDone;
        }
        [Serializable] class ProbeReport { public string utc, status; public float frameDistanceSmall, frameDistanceMid, frameDistanceLarge; public List<ProbeRow> rows = new List<ProbeRow>(); public List<string> lines = new List<string>(); }

        const float ProbeDt = 1f / 30f, Aspect = 16f / 9f, Inner = 0.8f;

        /// <summary>One frame of a mode: the wanted camera point and look point, the FOV, the time constants, what must be inside the inner 80 %,
        /// and the point the distance is measured to.</summary>
        class Pose { public Vector3 want, look, distTo; public float fov, tau = PosTau, lookTau = LookTau; public Vector3[] mustSee = new Vector3[0]; public bool sample = true, cut; }

        /// <summary>Steps a mode for `frames` frames (pose(f) gives the wanted pose of frame f), smoothing / cutting like ApplyPose, and measures.
        /// jump(f) (optional) gives the wanted point after the subject jumped; the row records whether that is a cut.</summary>
        static ProbeRow Sim(string name, int frames, Func<int, Pose> pose, float wantMin, float wantMax, Func<Vector3> jump = null, float skipSeconds = 0.6f)
        {
            var row = new ProbeRow { scenario = name, distMin = 1e9f, wantMin = wantMin, wantMax = wantMax, jumpApplies = jump != null };
            Vector3 cam = Vector3.zero, look = Vector3.zero; bool first = true; float fov = 50f;
            for (int f = 0; f < frames; f++)
            {
                var p = pose(f);
                bool cut = first || p.cut || (p.want - cam).magnitude > CutDistance;   // mirror of the cut rule in ApplyPose (_cutNext or the distance)
                if (cut && !first) { row.cuts++; if (p.cut) row.planCuts++; else row.distCuts++; }
                Vector3 next = KH_VideoCamera.SmoothStep(cam, p.want, cut, ProbeDt, p.tau);
                look = KH_VideoCamera.SmoothStep(look, p.look, cut, ProbeDt, p.lookTau);
                fov = cut ? p.fov : Mathf.Lerp(fov, p.fov, KH_VideoCamera.BlendFactor(ProbeDt, FovTau));
                if (!first && !cut) row.stepMax = Mathf.Max(row.stepMax, (next - cam).magnitude);
                cam = next; first = false;
                if (f * ProbeDt < skipSeconds || !p.sample) continue;
                Quaternion rot = KH_VideoCamera.LookRoll0(cam, look);
                row.frames++;
                float d = (p.distTo - cam).magnitude;
                row.distMin = Mathf.Min(row.distMin, d); row.distMax = Mathf.Max(row.distMax, d);
                row.rollMax = Mathf.Max(row.rollMax, Mathf.Abs(Vector3.Dot(rot * Vector3.right, Vector3.up)));
                bool inside = true;
                foreach (var s in p.mustSee) { float ndc = Ndc(cam, rot, fov, s); row.worstFrame = Mathf.Max(row.worstFrame, ndc); if (ndc > Inner) inside = false; }
                if (!inside) row.outOfFrame++;
            }
            if (jump != null) row.cutOnJump = (jump() - cam).magnitude > CutDistance;
            return row;
        }

        static float Ndc(Vector3 cam, Quaternion rot, float fov, Vector3 p)
        {
            Vector3 l = Quaternion.Inverse(rot) * (p - cam);
            float tanV = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), tanH = tanV * Aspect;
            return l.z <= 0.01f ? 99f : Mathf.Max(Mathf.Abs(l.x / l.z) / tanH, Mathf.Abs(l.y / l.z) / tanV);
        }

        /// <summary>The travel-velocity filter of KH_VideoCamera.Heading (mirror, one line) for a subject moving `step` per frame.</summary>
        static Vector3 Filter(Vector3 vel, Vector3 step) { return Vector3.Lerp(vel, step / ProbeDt, KH_VideoCamera.BlendFactor(ProbeDt, HeadingTau)); }

        static Vector3 Head(float h) { return Vector3.up * h; }

        [MenuItem(Menu + "2b - Probe Poses")]
        public static void ProbePoses()
        {
            var r = new ProbeReport { utc = DateTime.UtcNow.ToString("o") };
            r.frameDistanceSmall = KH_VideoCamera.FrameDistanceFor(0.375f, DogFill, DogFov);
            r.frameDistanceMid = KH_VideoCamera.FrameDistanceFor(0.6f, DogFill, DogFov);
            r.frameDistanceLarge = KH_VideoCamera.FrameDistanceFor(0.9f, DogFill, DogFov);
            var discProxy = UnityEngine.Object.FindObjectsOfType<KH_Frisbee>(true).FirstOrDefault();

            // ---- 0 犬追従 ----
            foreach (float headH in new[] { 0.25f, 0.4f, 0.6f })
                foreach (float speed in new[] { 0f, 1.5f, 3f })
                    r.rows.Add(DogWalk(headH, speed));
            // ---- 1 じぶん ----
            r.rows.Add(SelfStand(null, "no own dog"));
            r.rows.Add(SelfStand(new Vector3(0.6f, 0.3f, 1.0f), "own dog at the feet ahead-right"));
            r.rows.Add(SelfStand(new Vector3(-0.8f, 0.3f, 0.5f), "own dog at the feet left"));
            r.rows.Add(SelfStand(new Vector3(0.3f, 0.3f, 2.0f), "own dog 2 m ahead"));
            r.rows.Add(SelfWalk(1.4f));
            // ---- 2 背中ごし (§16) ----
            r.rows.Add(TpsStand()); r.rows.Add(TpsWalk(1.4f)); r.rows.Add(TpsTurn(180f)); r.rows.Add(TpsTurn(90f));
            // ---- 3 犬目線 ----
            r.rows.Add(DogEyeWalk(true)); r.rows.Add(DogEyeWalk(false));
            // ---- 4 定点 ----
            r.rows.Add(SpotWash());
            // ---- 5 / 6 フリスビー ----
            if (discProxy != null) { r.rows.Add(DiscFlight(discProxy)); r.rows.Add(CatchFlight(discProxy)); r.rows.Add(LandingFlight(discProxy)); }
            else r.lines.Add("info disc rows skipped: no KH_Frisbee in the open scene (they use its _PositionAt)");
            // ---- 7 一周 (§16: 一定 / 近づく / 離れる at 6 and 24 °/s), 8 通り過ぎ (§15.5: おかえり / ふれあい removed, §16: 手元 / 見下ろす removed) ----
            r.rows.Add(OrbitStand());
            foreach (int way in new[] { KH_VideoCamera.OrbitCloser, KH_VideoCamera.OrbitFarther })
            { r.rows.Add(OrbitWay(way, OrbitDegPerSec, 70f)); r.rows.Add(OrbitWay(way, OrbitDegMax, 20f)); }
            r.rows.Add(PassWalk());
            // ---- 10 おかえり (§17, internal: the patterns only) ----
            r.rows.Add(WelcomeReturn());
            // ---- 9 みち (§16): every move on a straight 1.4 m/s walk, then 待って寄る / 追いこし standing ----
            r.rows.Add(PathWalk(KH_VideoCamera.PathWaitApproach, 15f, 1.4f, 2f, 12.5f, 1));
            r.rows.Add(PathWalk(KH_VideoCamera.PathBackAway, 10f, 1.4f, 2f, 2.8f, -1));
            r.rows.Add(PathWalk(KH_VideoCamera.PathBeside, 10f, 1.4f, 2.6f, 3.1f, -1));
            r.rows.Add(PathWalk(KH_VideoCamera.PathOvertake, 13f, 1.4f, 1.3f, 8.6f, 1));
            r.rows.Add(PathWalk(KH_VideoCamera.PathPullAway, 18f, 1.4f, 2f, 12.5f, 1));
            r.rows.Add(PathWalk(KH_VideoCamera.PathWaitApproach, 15f, 0f, 11.5f, 12.5f, -1));
            r.rows.Add(PathWalk(KH_VideoCamera.PathOvertake, 13f, 0f, 1.3f, 8.6f, 1));

            bool ok = true;
            foreach (var row in r.rows)
            {
                bool distOk = row.distMin >= row.wantMin && row.distMax <= row.wantMax;
                bool rowOk = distOk && row.rollMax < 0.01f && row.outOfFrame == 0 && (!row.jumpApplies || row.cutOnJump) && row.stepMax <= row.stepLimit
                    && (row.movesWant < 0 || row.movesDone >= row.movesWant)
                    && row.distCuts == 0 && row.planCuts == row.cutsWant;
                if (!rowOk && !row.informative) ok = false;
                r.lines.Add((row.informative ? "info " : rowOk ? "ok   " : "FAIL ") + row.scenario + ": distance " + row.distMin.ToString("F2") + "-" + row.distMax.ToString("F2")
                    + " m (want " + row.wantMin + "-" + row.wantMax + "), out of inner 80% " + row.outOfFrame + "/" + row.frames + " (worst |ndc| " + row.worstFrame.ToString("F2") + "), roll "
                    + row.rollMax.ToString("F4") + ", max step " + row.stepMax.ToString("F2") + " m, cut on jump " + (row.jumpApplies ? row.cutOnJump.ToString() : "n/a")
                    + ", cuts: planned " + row.planCuts + " (want " + row.cutsWant + "), by distance " + row.distCuts + " (want 0)" + (row.movesWant >= 0 ? ", moves " + row.movesDone + " (want ≥ " + row.movesWant + ")" : "")
                    + (string.IsNullOrEmpty(row.note) ? "" : " — " + row.note));
            }
            r.status = ok ? "PASS" : "FAIL";
            WriteJson(OutDir + "/probe.json", r);
            string head = "[KH Video] VIDEO_PROBE_" + r.status + " frame distance (fill " + DogFill + ", fov " + DogFov + "): size 0.375 → " + r.frameDistanceSmall.ToString("F2")
                + " m, 0.6 → " + r.frameDistanceMid.ToString("F2") + " m, 0.9 → " + r.frameDistanceLarge.ToString("F2") + " m (" + OutDir + "/probe.json)\n" + string.Join("\n", r.lines);
            if (ok) Debug.Log(head); else Debug.LogError(head);
        }

        // ================================================================ scenarios

        /// <summary>犬追従: a dog walking +X at `speed` for 3 s, then a 20 m jump. Hard floor 0.8 m (a small dog speeding up comes closer than
        /// DogDistMin for a moment: the lead follows the filtered velocity more slowly than the lag builds).</summary>
        static ProbeRow DogWalk(float headH, float speed)
        {
            Vector3 root = Vector3.zero, vel = Vector3.zero, h = Vector3.right; float size = headH * 1.5f;
            Pose At(Vector3 rt)
            {
                Vector3 head = rt + Head(headH), body = Vector3.Lerp(rt, head, 0.75f);
                return new Pose
                {
                    want = KH_VideoCamera.DogFollowPos(body, head.y, h, size, DogFill, DogFov, DogSideDeg, DogDistMin, DogDistMax, DogLift) + KH_VideoCamera.LeadOffset(vel, PosTau),
                    look = KH_VideoCamera.DogFollowLook(body, h, DogLookAhead) + KH_VideoCamera.LeadOffset(vel, LookTau),
                    fov = DogFov, distTo = body, mustSee = new[] { body },
                };
            }
            var row = Sim("0 犬追従: dog head " + headH + " m, " + speed + " m/s", 91, f =>
            {
                Vector3 step = Vector3.right * speed * ProbeDt; root += step; vel = Filter(vel, step);
                if (vel.magnitude >= StillSpeed) h = vel.normalized;
                return At(root);
            }, 0.8f, 5.1f, () => At(root + Vector3.forward * 20f).want);
            if (row.distMin < DogDistMin) row.note = "closer than DogDistMin " + DogDistMin + " while speeding up";
            return row;
        }

        /// <summary>じぶん standing (facing +Z), with or without one own dog; then a 15 m teleport.</summary>
        static ProbeRow SelfStand(Vector3? dog, string what)
        {
            Vector3 head = new Vector3(0f, 1.6f, 0f), fwd = Vector3.forward;
            int own = dog.HasValue ? 1 : 0; Vector3 d = dog ?? head;
            float dist = own > 0 ? SelfDistanceWithDog : SelfDistance;
            Pose At(Vector3 hd) => new Pose
            {
                want = KH_VideoCamera.SelfFollowPos(hd, fwd, SelfSideDeg, dist, SelfLift),
                look = KH_VideoCamera.SelfFollowLook(hd, d + (hd - head), own, SelfLookToDogs, SelfChestDrop),
                fov = SelfFov, distTo = hd, mustSee = own > 0 ? new[] { hd, d } : new[] { hd },
            };
            return Sim("1 じぶん: standing, " + what, 60, f => At(head), own > 0 ? 2.9f : 2.2f, own > 0 ? 3.5f : 2.7f, () => At(head + fwd * 15f).want, 0f);
        }

        static ProbeRow SelfWalk(float speed)
        {
            Vector3 feet = Vector3.zero, vel = Vector3.zero, h = Vector3.forward;
            Pose At(Vector3 ft)
            {
                Vector3 head = ft + Head(1.6f);
                return new Pose
                {
                    want = KH_VideoCamera.SelfFollowPos(head, h, SelfSideDeg, SelfDistance, SelfLift) + KH_VideoCamera.LeadOffset(vel, PosTau),
                    look = KH_VideoCamera.SelfFollowLook(head, head, 0, SelfLookToDogs, SelfChestDrop) + KH_VideoCamera.LeadOffset(vel, LookTau),
                    fov = SelfFov, distTo = head, mustSee = new[] { head },
                };
            }
            return Sim("1 じぶん: walking " + speed + " m/s, no own dog", 90, f =>
            {
                Vector3 step = Vector3.forward * speed * ProbeDt; feet += step; vel = Filter(vel, step);
                if (vel.magnitude >= StillSpeed) h = vel.normalized;
                return At(feet);
            }, 1.5f, 3.2f, () => At(feet + h * 15f).want);
        }

        /// <summary>犬目線: a dog (head 0.4 m) walking +Z at 1 m/s beside me (my head 1.6 m, 1.2 m to its right, 1 m ahead): it looks up at me.
        /// Far (user = false): the scenery ahead of the dog.</summary>
        static ProbeRow DogEyeWalk(bool user)
        {
            Vector3 root = Vector3.zero, vel = Vector3.zero, h = Vector3.forward;
            Pose At(Vector3 rt)
            {
                Vector3 head = rt + Head(0.4f), me = rt + new Vector3(1.2f, 1.6f, 1f);
                if (user)
                    return new Pose { want = KH_VideoCamera.DogEyePos(head, me, DogEyeSideDeg, DogEyeBack, DogEyeLift) + KH_VideoCamera.LeadOffset(vel, DogEyeTau), look = me, fov = DogEyeFov,
                        tau = DogEyeTau, distTo = me, mustSee = new[] { me } };
                Vector3 pos = KH_VideoCamera.DogEyeScenePos(head, h, DogEyeAhead, DogEyeSceneLift);
                Vector3 ahead = pos + h * 10f;
                return new Pose { want = pos + KH_VideoCamera.LeadOffset(vel, DogEyeTau), look = KH_VideoCamera.DogEyeSceneLook(pos, h, DogEyePitch) + KH_VideoCamera.LeadOffset(vel, LookTau),
                    fov = DogEyeSceneFov, tau = DogEyeTau, distTo = head, mustSee = new[] { ahead } };
            }
            return Sim("3 犬目線: " + (user ? "looking up at me beside the dog" : "I am far: the scenery ahead"), 90, f =>
            {
                Vector3 step = Vector3.forward * 1f * ProbeDt; root += step; vel = Filter(vel, step);
                if (vel.magnitude >= StillSpeed) h = vel.normalized;
                return At(root);
            }, user ? 1.2f : 0f, user ? 2.4f : 0.5f, () => At(root + Vector3.right * 20f).want);
        }

        /// <summary>§16 背中ごし standing (body facing +Z): the camera 2.25 m from the head; then a 15 m teleport.</summary>
        static ProbeRow TpsStand()
        {
            Vector3 head = new Vector3(0f, 1.6f, 0f), fwd = Vector3.forward;
            Pose At(Vector3 hd) => new Pose { want = KH_VideoCamera.TpsPos(hd, fwd, TpsBack, TpsShoulder, TpsLift), look = KH_VideoCamera.TpsLook(hd, fwd, TpsLookAhead, TpsLookDrop),
                fov = TpsFov, distTo = hd, mustSee = new[] { hd } };
            var row = Sim("2 背中ごし: standing", 60, f => At(head), 2.2f, 2.3f, () => At(head + fwd * 15f).want, 0f);
            row.note = "the head sits a little left of and below the centre (the shoulder offset is small)";
            return row;
        }

        /// <summary>§16 背中ごし walking +Z at `speed` with the feed-forward (the filtered velocity): the distance grows a little while speeding up.</summary>
        static ProbeRow TpsWalk(float speed)
        {
            Vector3 feet = Vector3.zero, vel = Vector3.zero, fwd = Vector3.forward;
            return Sim("2 背中ごし: walking " + speed + " m/s", 120, f =>
            {
                Vector3 step = fwd * speed * ProbeDt; feet += step; vel = Filter(vel, step);
                Vector3 head = feet + Head(1.6f);
                return new Pose { want = KH_VideoCamera.TpsPos(head, fwd, TpsBack, TpsShoulder, TpsLift) + KH_VideoCamera.LeadOffset(vel, PosTau),
                    look = KH_VideoCamera.TpsLook(head, fwd, TpsLookAhead, TpsLookDrop) + KH_VideoCamera.LeadOffset(vel, LookTau), fov = TpsFov, distTo = head, mustSee = new[] { head } };
            }, 2.0f, 2.6f, null, 1.0f);
        }

        /// <summary>§16 背中ごし: standing, the body turns `deg` at once at 0.5 s; the facing eases with TpsYawTau (KH_VideoCamera.YawStep). The head
        /// stays in the frame and the camera swings round without a jump.</summary>
        static ProbeRow TpsTurn(float deg)
        {
            Vector3 head = new Vector3(0f, 1.6f, 0f);
            float yaw = 0f;
            return Sim("2 背中ごし: turning " + deg + "° in place", 120, f =>
            {
                yaw = KH_VideoCamera.YawStep(yaw, f * ProbeDt >= 0.5f ? deg : 0f, ProbeDt, TpsYawTau);
                Vector3 fwd = KH_VideoCamera.YawDir(yaw);
                return new Pose { want = KH_VideoCamera.TpsPos(head, fwd, TpsBack, TpsShoulder, TpsLift), look = KH_VideoCamera.TpsLook(head, fwd, TpsLookAhead, TpsLookDrop),
                    fov = TpsFov, distTo = head, mustSee = new[] { head } };
            }, 1.5f, 2.3f, null, 0f);
        }

        /// <summary>定点 (wash): the camera SpotWashDist out and SpotWashHeight up from the stand, looking at the stand +0.4 m, leaning SpotDogPull
        /// toward a dog (head 0.4 m) standing on it.</summary>
        static ProbeRow SpotWash()
        {
            Vector3 stand = Vector3.zero, look0 = stand + Vector3.up * 0.4f;
            Vector3 cam = stand + Quaternion.AngleAxis(35f, Vector3.up) * Vector3.forward * SpotWashDist + Vector3.up * SpotWashHeight;
            Vector3 body = stand + Vector3.up * 0.3f, head = stand + Vector3.up * 0.4f + Vector3.forward * 0.3f;
            var row = Sim("4 定点: wash stand with a dog on it", 45, f => new Pose { want = cam, look = Vector3.Lerp(look0, body, SpotDogPull), fov = SpotWashFov, lookTau = SpotLookTau,
                distTo = body, mustSee = new[] { body, head, body + Vector3.right * 0.9f } }, 2.5f, 3.5f, null, 0f);
            row.note = "a washer 0.9 m beside the dog is also inside";
            return row;
        }

        /// <summary>A synthetic throw: from (0,1.2,0) at (0,2.5,9) m/s, straight, with the scene disc's own flight formula; the dog starts 2 m
        /// behind the thrower and runs 7 m/s along the line.</summary>
        static ProbeRow DiscFlight(KH_Frisbee d)
        {
            Vector3 p0 = new Vector3(0f, 1.2f, 0f), v0 = new Vector3(0f, 2.5f, 9f);
            Vector3 dog = new Vector3(0.5f, 0f, -2f), discVel = Vector3.zero, prev = p0;
            var row = Sim("5 ディスク: a 9 m/s throw, the dog chasing at 7 m/s", 75, f =>
            {
                float t = f * ProbeDt;
                Vector3 p = d._PositionAt(p0, v0, 0f, t);
                if (p.y < 0.1f) p.y = 0.1f;
                discVel = Vector3.Lerp(discVel, (p - prev) / ProbeDt, KH_VideoCamera.BlendFactor(ProbeDt, DiscVelTau)); prev = p;   // mirror of UpdateDiscVel
                dog += Vector3.forward * 7f * ProbeDt;
                Vector3 body = dog + Vector3.up * 0.3f;
                bool near = (p - body).magnitude <= DiscApartMax;
                return new Pose { want = KH_VideoCamera.DiscFollowPos(p, body, discVel, DiscSideK, DiscSideMin, DiscSideMax, DiscUp) + KH_VideoCamera.LeadOffset(new Vector3(discVel.x, 0f, discVel.z), DiscTau),
                    look = KH_VideoCamera.DiscFollowLook(p, body, DiscApartMax), fov = DiscFov, tau = DiscTau, lookTau = DiscLookTau,
                    distTo = p, mustSee = near ? new[] { p, body } : new[] { body }, sample = p.y > 0.15f };
            }, 1.5f, 12f, () => d._PositionAt(p0, v0, 0f, 1f) + Vector3.right * 20f, 0.3f);
            row.stepLimit = 0.9f;   // the disc flies 9 m/s = 0.3 m per frame; the camera may move faster than for a walking dog
            return row;
        }

        /// <summary>落下待ち: the landing of the same throw (the PredictLanding loop against y = 0), the camera placed LandSide off the line on the
        /// dog's side; the disc arrives and the dog runs in.</summary>
        /// <summary>キャッチ待ち with a plan: the same throw, a synthetic plan catching it on the way down at mouth height 1.0 m (catch root 0.55 m
        /// below), the dog running in from 2 m behind the thrower and arriving at the catch time. The camera is placed at the throw (CatchWaitPos /
        /// CatchWaitLook, the runtime functions). In frame: the disc for the last catchLookBack-worth of flight, the catch point, the dog's root and
        /// head for the last 0.8 s (the run-in and the jump; earlier the dog is still 10 m+ back).</summary>
        static ProbeRow CatchFlight(KH_Frisbee d)
        {
            Vector3 p0 = new Vector3(0f, 1.2f, 0f), v0 = new Vector3(0f, 2.5f, 9f);
            float ct = 0f; Vector3 prev = p0;
            for (float t = 0.02f; t < 8f; t += 0.01f) { Vector3 q = d._PositionAt(p0, v0, 0f, t); if (q.y < 1.0f && q.y < prev.y) { ct = t; break; } prev = q; }
            Vector3 C = d._PositionAt(p0, v0, 0f, ct), dir = d._VelocityAt(v0, 0f, ct);
            Vector3 root = C - Vector3.up * 0.55f, start = new Vector3(0.8f, 0f, -2f);
            Vector3 cam = KH_VideoCamera.CatchWaitPos(C, dir, start, CatchSide, CatchUp), look = KH_VideoCamera.CatchWaitLook(C, dir, CatchLookBack);
            int frames = Mathf.CeilToInt((ct + 1.5f) / ProbeDt);
            var row = Sim("6 キャッチ待ち: planned catch at " + ct.ToString("F2") + " s, " + C.ToString("F1"), frames, f =>
            {
                float t = f * ProbeDt;
                Vector3 p = t <= ct ? d._PositionAt(p0, v0, 0f, t) : C;
                float k = Mathf.Clamp01(t / ct);
                Vector3 groundAt = Vector3.Lerp(start, new Vector3(root.x, 0f, root.z), k);
                Vector3 dogRoot = t < ct - 0.4f ? groundAt : Vector3.Lerp(groundAt, root, Mathf.Clamp01((t - (ct - 0.4f)) / 0.4f));   // the jump: the last 0.4 s
                var must = new List<Vector3> { C };
                if (t >= ct - 0.6f) must.Add(p);
                if (t >= ct - 0.8f) { must.Add(dogRoot + Vector3.up * 0.3f); must.Add(dogRoot + Vector3.up * 0.55f); }
                return new Pose { want = cam, look = look, fov = CatchFov, distTo = C, mustSee = must.ToArray() };
            }, 4.5f, 7f, null, 0f);
            row.note = "catch " + (C - p0).magnitude.ToString("F1") + " m from the throw";
            return row;
        }

        static ProbeRow LandingFlight(KH_Frisbee d)
        {
            Vector3 p0 = new Vector3(0f, 1.2f, 0f), v0 = new Vector3(0f, 2.5f, 9f);
            Vector3 L = p0, prevP = p0;
            for (int i = 1; i <= Mathf.RoundToInt(LandMaxT / LandStep); i++) { Vector3 q = d._PositionAt(p0, v0, 0f, i * LandStep); if (q.y <= 0f) { L = Vector3.Lerp(prevP, q, prevP.y / Mathf.Max(prevP.y - q.y, 1e-4f)); break; } prevP = q; L = q; }
            Vector3 dog = new Vector3(0.8f, 0f, -2f);
            Vector3 cam = KH_VideoCamera.LandingWaitPos(L, v0, dog, LandSide, LandBeyond, LandUp);
            var row = Sim("6 キャッチ待ち (fallback, no plan): landing " + L.ToString("F1"), 90, f =>
            {
                Vector3 p = d._PositionAt(p0, v0, 0f, f * ProbeDt); if (p.y < 0f) p = L;
                dog = Vector3.MoveTowards(dog, L, 7f * ProbeDt);
                bool near = (p - L).magnitude < LandNear;
                var must = new List<Vector3> { L };
                if (near) must.Add(p);
                if ((dog - L).magnitude < 6f) must.Add(dog + Vector3.up * 0.3f);   // the dog once it is close to the landing
                return new Pose { want = cam, look = KH_VideoCamera.LandingWaitLook(L, p, near), fov = LandFov, distTo = L, mustSee = must.ToArray() };
            }, 3f, 8f, null, 0f);
            row.note = "landing " + (L - p0).magnitude.ToString("F1") + " m from the throw";
            return row;
        }

        /// <summary>一周: me standing with one own dog 1 m ahead; 20 s of the orbit.</summary>
        static ProbeRow OrbitStand()
        {
            Vector3 feet = Vector3.zero, dog = new Vector3(0f, 0f, 1f);
            Vector3 centre = (feet + dog) * 0.5f;
            float angle = 90f;
            return Sim("7 一周 一定: me and one dog, 20 s", 600, f =>
            {
                angle += OrbitDegPerSec * ProbeDt;
                return new Pose { want = KH_VideoCamera.OrbitPos(centre, angle, OrbitRadius, OrbitHeight), look = centre + Vector3.up * OrbitLookUp, fov = OrbitFov, tau = OrbitTau,
                    distTo = centre, mustSee = new[] { feet + Head(1.6f), feet, dog + Head(0.3f) } };
            }, 3.4f, 4.0f, null, 0.5f);
        }

        /// <summary>§16 一周 近づく / 離れる at `speed` °/s for `seconds`: the radius (KH_VideoCamera.OrbitRadiusAt) goes from OrbitRadius to its end over
        /// the first circle, then stays; the look height (OrbitLookHeight) rises for 近づく. Must see: 離れる my head, feet and the dog;
        /// 近づく my head and chest (the feet / dog leave the frame below about 2.8 / 2.4 m: a close-up of the upper body).</summary>
        static ProbeRow OrbitWay(int way, float speed, float seconds)
        {
            Vector3 feet = Vector3.zero, dog = new Vector3(0f, 0f, 1f);
            Vector3 centre = (feet + dog) * 0.5f;
            float angle = 90f, travel = 0f, rMin = 1e9f, rMax = 0f;
            bool closer = way == KH_VideoCamera.OrbitCloser;
            var row = Sim("7 一周 " + KH_VideoCamera.OrbitKindWord(way) + ": " + speed + "°/s, " + seconds + " s", Mathf.RoundToInt(seconds / ProbeDt), f =>
            {
                float step = speed * ProbeDt;
                angle += step; travel = Mathf.Min(travel + step, 720f);
                float rad = KH_VideoCamera.OrbitRadiusAt(OrbitRadius, way, travel / 360f, OrbitNearK, OrbitFarK, OrbitRadiusMin, OrbitRadiusMax);
                rMin = Mathf.Min(rMin, rad); rMax = Mathf.Max(rMax, rad);
                return new Pose { want = KH_VideoCamera.OrbitPos(centre, angle, rad, OrbitHeight), look = centre + Vector3.up * KH_VideoCamera.OrbitLookHeight(OrbitLookUp, OrbitHeight, way, travel / 360f),
                    fov = OrbitFov, tau = OrbitTau, distTo = centre, mustSee = closer ? new[] { feet + Head(1.6f), feet + Head(1.25f) } : new[] { feet + Head(1.6f), feet, dog + Head(0.3f) } };
            }, closer ? 1.9f : 3.4f, closer ? 3.9f : 9.1f, null, 0.5f);
            row.note = "radius " + rMin.ToString("F2") + "-" + rMax.ToString("F2") + " m" + (speed > OrbitDegPerSec ? " (a circle in " + (360f / speed).ToString("F0") + " s)" : "");
            return row;
        }

        /// <summary>§16 みち: me walking +Z at `speed` (flat, no trail, so PathDir = +Z) for `seconds` with one move; the phase machine mirrors
        /// KH_VideoCamera.PathMove (the ground ray of 待って寄る finds the flat ground). Must see: my head. A finished move counts; walking rows want ≥ 1.</summary>
        static ProbeRow PathWalk(int move, float seconds, float speed, float wantMin, float wantMax, int movesWant)
        {
            Vector3 feet = Vector3.zero, vel = Vector3.zero, dir = Vector3.forward;
            bool have = false; int phase = 0, done = 0; float phaseAt = 0f, off = 0f;
            Vector3 anchor = Vector3.zero, pcam = Vector3.zero;
            float far = PathAheadFar, near = PathAheadNear, h = PathHeight;
            var row = Sim("9 みち " + KH_VideoCamera.PathMoveWord(move) + ": " + (speed > 0f ? "walking " + speed + " m/s" : "standing") + ", " + seconds + " s", Mathf.RoundToInt(seconds / ProbeDt), f =>
            {
                float now = f * ProbeDt;
                Vector3 step = dir * speed * ProbeDt; feet += step; vel = Filter(vel, step);
                Vector3 head = feet + Head(1.6f), lead = KH_VideoCamera.LeadOffset(vel, PosTau);
                Vector3 want = Vector3.zero; float tau = PosTau; bool cut = false;
                if (!have)
                {
                    have = true; phase = 0; phaseAt = now;
                    if (move == KH_VideoCamera.PathWaitApproach) anchor = KH_VideoCamera.PathPos(feet, dir, far, 0f, h);
                    else if (move == KH_VideoCamera.PathOvertake) off = -PathBehind;
                    else if (move == KH_VideoCamera.PathPullAway) off = near;
                }
                if (move == KH_VideoCamera.PathWaitApproach)
                {
                    if (phase == 0)
                    {
                        Vector3 to = anchor - feet; to.y = 0f;
                        float d = to.magnitude;
                        if (Vector3.Dot(to, dir) < 0f || d > far * 2f) { anchor = KH_VideoCamera.PathPos(feet, dir, far, 0f, h); cut = true; }
                        else if (d <= PathApproachAt) { phase = 1; phaseAt = now; pcam = anchor; }
                        want = anchor;
                    }
                    if (phase == 1)
                    {
                        Vector3 target = KH_VideoCamera.PathPos(feet, dir, near, 0f, h) + KH_VideoCamera.LeadOffset(vel, PathGlideTau);
                        pcam = KH_VideoCamera.SmoothStep(pcam, target, false, ProbeDt, PathGlideTau);
                        want = pcam; tau = 0.05f;
                        if (now - phaseAt >= PathHold + 3f * PathGlideTau) { done++; anchor = KH_VideoCamera.PathPos(feet, dir, far, 0f, h); phase = 0; cut = true; want = anchor; tau = PosTau; }
                    }
                }
                else if (move == KH_VideoCamera.PathBackAway) want = KH_VideoCamera.PathPos(feet, dir, near, 0f, h) + lead;
                else if (move == KH_VideoCamera.PathBeside) want = KH_VideoCamera.PathPos(feet, dir, 0f, PathBeside, h) + lead;
                else if (move == KH_VideoCamera.PathOvertake)
                {
                    float end = far * 2f / 3f;
                    if (phase == 0) { off += PathPassSpeed * ProbeDt; if (off >= end) { off = end; phase = 1; phaseAt = now; } }
                    else if (now - phaseAt >= PathHold) { done++; off = -PathBehind; phase = 0; cut = true; }
                    want = KH_VideoCamera.PathPos(feet, dir, off, PathPassSide, h) + lead;
                }
                else
                {
                    if (phase == 0)
                    {
                        off += PathRecedeSpeed * ProbeDt;
                        want = KH_VideoCamera.PathPos(feet, dir, Mathf.Min(off, far), 0f, h) + lead;
                        if (off >= far) { off = far; phase = 1; phaseAt = now; anchor = want; }
                    }
                    else
                    {
                        want = anchor;
                        if (now - phaseAt >= PathHold) { done++; off = near; phase = 0; cut = true; want = KH_VideoCamera.PathPos(feet, dir, near, 0f, h) + lead; }
                    }
                }
                Vector3 look = KH_VideoCamera.SelfFollowLook(head, head, 0, SelfLookToDogs, SelfChestDrop) + KH_VideoCamera.LeadOffset(vel, LookTau);
                return new Pose { want = want, look = look, fov = PathFov, tau = tau, distTo = head, mustSee = new[] { head }, cut = cut };
            }, wantMin, wantMax, null, 0.6f);
            row.movesWant = movesWant; row.movesDone = done;
            row.cutsWant = done;   // straight walk, flat ground: the only planned cut is the one at the end of each finished move (no re-placement)
            return row;
        }

        /// <summary>§17 おかえり: I stand at the origin facing +Z; the dog brings the disc back from 15 m at 3 m/s and stops returnStopDistance 0.9 m in front of
        /// me (the Drop). The camera: KH_VideoCamera.WelcomePos (welcomeDist toward the dog, at most half the way, welcomeSide to the right, welcomeHeight up), looking at the dog's
        /// head. Must see: the dog's head and body all the way in (the dog comes toward the camera).</summary>
        static ProbeRow WelcomeReturn()
        {
            Vector3 feet = Vector3.zero, dog = new Vector3(0.3f, 0f, 15f);
            var row = Sim("10 おかえり (internal): the dog returns from 15 m at 3 m/s and stops 0.9 m in front of me", 180, f =>
            {
                Vector3 stop = new Vector3(0.1f, 0f, 0.9f);
                dog = Vector3.MoveTowards(dog, stop, 3f * ProbeDt);
                Vector3 toMe = feet - dog; toMe.y = 0f;
                Vector3 body = dog + Vector3.up * 0.3f, head = dog + Vector3.up * 0.45f + toMe.normalized * 0.25f;
                return new Pose { want = KH_VideoCamera.WelcomePos(feet, body, WelcomeDist, WelcomeSide, WelcomeHeight), look = head, fov = WelcomeFov, lookTau = WelcomeLookTau, distTo = head, mustSee = new[] { head, body } };
            }, 0.6f, 12.5f, null, 0.3f);
            row.note = "the camera stays " + WelcomeDist + " m out (" + WelcomeSide + " m to the side) until the dog is closer than " + (WelcomeDist * 2f) + " m, then half the way";
            return row;
        }

        /// <summary>通り過ぎ: a fixed point PassSide m beside the path, PassHeight up; I walk along the path past it at 1.4 m/s from 30 m before
        /// to 20 m after. Framing measured while within PassNear.</summary>
        static ProbeRow PassWalk()
        {
            Vector3 cam = new Vector3(PassSide, PassHeight, 0f), me = new Vector3(0f, 0.4f, -30f);
            Vector3 look = me;
            return Sim("8 通り過ぎ: walking past a point " + PassSide + " m beside the path", 1100, f =>
            {
                me += Vector3.forward * 1.4f * ProbeDt;
                bool near = (me - cam).magnitude <= PassNear;
                return new Pose { want = cam, look = me + Vector3.up * PassLookUp, fov = PassFov, distTo = me, mustSee = new[] { me, me + Head(1.2f) }, sample = near && (me - cam).magnitude > 2.5f };
            }, 2.5f, 25.5f, null, 0f);
        }
    }
}
