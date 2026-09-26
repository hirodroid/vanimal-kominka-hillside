using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    // IMP-DOG-012 partial (Diagnostics): log lines (req / throw / etaErr / takeoff tails) and the motion-quality
    // ring the harness judges. Nothing here decides anything. Moved verbatim from KH_FrisbeeCatch.cs.
    public partial class KH_FrisbeeCatch
    {
        // Where the dog stands in the throw's own frame, so a run-in preset can be judged from the log alone
        // (ahead = along the throw, lateral = to its left/right, signed).
        void LogThrow(Vector3 p0, Vector3 v0, float curve)
        {
            Vector3 flat = v0; flat.y = 0f;
            Vector3 dir = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 rel = dogRoot.position - p0; rel.y = 0f;
            float lateral = Vector3.Dot(rel, side);
            Debug.Log("[KH_FrisbeeCatch] throw v=" + variant + " speed=" + v0.magnitude.ToString("F2")
                + " ahead=" + Vector3.Dot(rel, dir).ToString("F1")
                + " lateral=" + (lateral >= 0f ? "+" : "") + lateral.ToString("F1")
                + " dogDist=" + Vector3.Distance(dogRoot.position, p0).ToString("F1")
                + " curve=" + curve.ToString("F2")    // the throw's own bend; every prediction below uses this value
                // IMP-DOG-017: enough to replay the throw offline. p0 / v0 are the launch facts every prediction uses;
                // relH is the release height above the ground under the hand (0 = no ground hit), elev the launch
                // angle above horizontal, dogHdg the dog's heading against the throw (0 = facing downfield,
                // +-180 = facing the thrower). Appended at the end: the harness reads the columns before it.
                + DogTag() + " gen=" + _gen + " p0=" + V3(p0) + " v0=" + V3(v0)
                + " relH=" + (p0.y - SnapToGround(p0).y).ToString("F2")
                + " elev=" + (Mathf.Atan2(v0.y, flat.magnitude) * Mathf.Rad2Deg).ToString("F1")
                + " dogPos=" + V3(dogRoot.position) + " dogHdg=" + HeadingAgainst(dir).ToString("F0"));
        }

        // IMP-DOG-017: which dog wrote the line. Several dogs chase at once, and without this the lines of two
        // throws in flight interleave with nothing to tell them apart (PCVR 2026-09-25).
        string DogTag() { return " dog=" + dogRoot.name; }

        string V3(Vector3 v) { return "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3") + ")"; }

        // Signed horizontal angle from dir to the dog's forward, degrees (positive = the dog is turned to the right).
        float HeadingAgainst(Vector3 dir)
        {
            Vector3 f = dogRoot.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) return 0f;
            f = f.normalized;
            return Mathf.Atan2(Vector3.Dot(Vector3.Cross(dir, f), Vector3.up), Vector3.Dot(dir, f)) * Mathf.Rad2Deg;
        }

        // BUG-DOG-003. One line per event, never per frame. kind carries its own padding so the columns line up.
        // heldFor < 0 drops the pending-only columns (this line is not about a parked notice).
        void LogReq(string kind, string reason, bool local, int thrower, int gen, double age, double ageAtReceipt, float heldFor, bool held, bool ended, int endReason)
        {
            string s = "[KH_FrisbeeCatch] req " + kind;
            if (reason.Length > 0) s = s + "reason=" + reason + " ";
            s = s + "path=" + (local ? "local" : "net") + " thrower=" + thrower + " gen=" + gen + " age=" + ((float)age).ToString("F2");
            if (heldFor >= 0f) s = s + " ageAtReceipt=" + ((float)ageAtReceipt).ToString("F2") + " heldFor=" + heldFor.ToString("F2");
            s = s + " held=" + (held ? "true" : "false") + " ended=" + (ended ? endReason : 0) + " state=" + state
                + " owner=" + OwnerIdOf(dogRoot.gameObject) + "/" + OwnerIdOf(gameObject) + "/" + OwnerIdOf(frisbee.gameObject) + DogTag();
            Debug.Log(s);
        }

        int OwnerIdOf(GameObject go)
        {
            if (go == null) return -1;
            VRCPlayerApi o = Networking.GetOwner(go);
            return Utilities.IsValid(o) ? o.playerId : -1;
        }

        // The parked notice's own numbers (its thrower, its generation, how long it has waited).
        void LogPending(string kind, string reason)
        {
            LogReq(kind, reason, _pendingLocal, _pendingThrower, _pendingGen, Networking.GetServerTimeInSeconds() - _pendingT0,
                _pendingAgeAtReceipt, Time.time - _pendingSince, HeldNow(), _pendingEnded, _pendingEndReason);
        }

        // A launch that was already accepted (no pending columns).
        void LogLaunchCancel(string reason)
        {
            LogReq("cancel   ", reason, _launchLocal, throwerId, _gen, Networking.GetServerTimeInSeconds() - _t0,
                0.0, -1f, HeldNow(), _flightEnded, _flightEndReason);
        }

        // BUG-DOG-003 (Codex M4). One line per end notice actually received, so a flight that ended for a reason
        // nobody expected can be traced to its sender. NotifyEnd fires once per flight, so this is never per frame.
        void LogEnd(int reason, int sender, int gen, double opTime, string target)
        {
            Debug.Log("[KH_FrisbeeCatch] req end      reason=" + reason + " sender=" + sender + " gen=" + gen
                + " opAge=" + ((float)(Networking.GetServerTimeInSeconds() - opTime)).ToString("F2") + " target=" + target + DogTag());
        }

        void LogReject(string reason, bool local, int thrower, int gen, double age)
        {
            LogReq("reject   ", reason, local, thrower, gen, age, 0.0, -1f, HeldNow(), false, 0);
        }

        // Round 5: what the approach ACTUALLY cost against what Eta promised, once per committed plan, at the first
        // moment the dog can be said to have arrived (the hold band for a wait / stand, the crouch for a momentum
        // jump). Positive = the estimate was optimistic. This is a measurement, not a correction: nothing reads it,
        // and the harness collects the distribution so a calibration factor can be argued for from numbers rather
        // than by widening a margin until the symptom goes away (IMP-DOG-017: the paced run now absorbs it).
        void LogEtaErr(string at)
        {
            if (_etaLogged || _planEta < 0f) return;
            _etaLogged = true;
            float actual = frisbee._ElapsedSince(_t0) - _planEtaT;
            float err = actual - _planEta;
            Vector3 off = _standRoot - dogRoot.position; off.y = 0f;
            Debug.Log("[KH_FrisbeeCatch] etaErr=" + (err >= 0f ? "+" : "") + err.ToString("F2")
                + " est=" + _planEta.ToString("F2") + " actual=" + actual.ToString("F2")
                + " at=" + at + " mode=" + ModeName() + " path=" + _planPath.ToString("F1")
                + " slack=" + _planSlack.ToString("F2") + " dist=" + off.magnitude.ToString("F2") + " v=" + variant + DogTag());
        }

        // ---- Motion quality (round 4). Owner-only, Chase only; the harness reads the two numbers off the log. ----
        void SampleMotion()
        {
            float dt = Time.deltaTime;
            Vector3 fwd = dogRoot.forward;
            if (_mPrimed && dt > 0.0005f)
            {
                // Bucketed, not per frame: a single frame's angle divided by a single frame's dt reads 1368 deg/s
                // on a hitch (measured) even though every writer is capped at 240, because the rotation and the
                // clock do not come from the same interval. Over MotionBucket the two agree, and a REAL snap turn
                // lasts far longer than one bucket, so nothing that matters is smoothed away.
                // Pair the angle with the dt of the frame that TURNED it, not of the frame reading it. The
                // rotation between the previous sample and now was applied with the PREVIOUS frame's deltaTime;
                // summing this frame's instead let a varying frame time report 312 deg/s from a 240 deg/s cap.
                _mAccAngle += Vector3.Angle(_mPrevFwd, fwd);
                _mAccTime += _mPrevDt;
                if (_mSlowest < 0f || _speed < _mSlowest) _mSlowest = _speed;
                if (_mRanSince < 0f && _speed >= MotionMinSpeed) _mRanSince = Time.time;   // latched per throw (AcceptLaunch resets)
                if (_mAccTime >= MotionBucket)
                {
                    _mYaw[_mIdx] = _mAccAngle / _mAccTime;
                    _mSpeed[_mIdx] = _mSlowest;
                    _mTime[_mIdx] = Time.time;
                    _mIdx++; if (_mIdx >= MotionSamples) _mIdx = 0;
                    if (_mCount < MotionSamples) _mCount++;
                    // Round 5: the bucket that CONTAINS the latch also holds the ramp below it; remember when it closed
                    // so only the buckets after it count toward speedMin.
                    if (_mRanSince >= 0f && _mRanBucketEnd < 0f) _mRanBucketEnd = Time.time;
                    _mAccAngle = 0f; _mAccTime = 0f; _mSlowest = -1f;
                }
            }
            _mPrevFwd = fwd; _mPrevDt = dt; _mPrimed = true;
        }

        // Worst turn rate and slowest moment of the last MotionWindow seconds. Latched at the takeoff so the catch
        // line reports the approach, not the (already finished) flight.
        //
        // IMP-DOG-017 harness round 1: the chase now starts with a launch spin (launchTurnDegPerSec 480) and on a short
        // flight that spin and the standing start fall inside the window. Neither is the machine the rules were
        // written for (a snap turn AT SPEED, a stop BEFORE THE TAKE-OFF), so:
        //   yawRateMax counts only buckets whose slowest speed is >= walkSpeed; the rest is reported as launchYaw;
        //   speedMin counts only window samples at or after _mRanSince, the first moment THIS THROW the dog reached
        //   MotionMinSpeed (latched per throw, Codex review: a per-window "first reached" exempted a run -> stop ->
        //   run whose first run fell outside the window). Round 5: the bucket containing the latch is excluded too
        //   (its slowest sample is the ramp), so counting starts with the bucket after it. Never ran -> speedMin 0.
        void ComputeMotion()
        {
            float yaw = 0f, launch = 0f, slow = -1f, cut = Time.time - MotionWindow;
            int start = _mIdx - _mCount; if (start < 0) start += MotionSamples;
            for (int k = 0; k < _mCount; k++)
            {
                int i = start + k; if (i >= MotionSamples) i -= MotionSamples;   // oldest first
                if (_mTime[i] < cut) continue;
                if (_mSpeed[i] >= walkSpeed) { if (_mYaw[i] > yaw) yaw = _mYaw[i]; }
                else if (_mYaw[i] > launch) launch = _mYaw[i];
                if (_mRanBucketEnd >= 0f && _mTime[i] > _mRanBucketEnd && (slow < 0f || _mSpeed[i] < slow)) slow = _mSpeed[i];
            }
            // The bucket still being filled (Codex M3). Up to MotionBucket seconds - the very moments before the
            // crouch, where a stop or a snap would show - used to be invisible to the verdict because only closed
            // buckets were read. The angle is divided by at least half a bucket so a one-frame remainder cannot
            // report a rate no writer can produce (the whole reason the samples are bucketed at all).
            if (_mAccTime > 0f)
            {
                float partial = _mAccAngle / (_mAccTime > MotionBucket * 0.5f ? _mAccTime : MotionBucket * 0.5f);
                if (_mSlowest >= walkSpeed) { if (partial > yaw) yaw = partial; }
                else if (partial > launch) launch = partial;
                if (_mRanBucketEnd >= 0f && _mSlowest >= 0f && (slow < 0f || _mSlowest < slow)) slow = _mSlowest;
            }
            _yawRateMax = yaw; _launchYaw = launch; _speedMin = slow < 0f ? 0f : slow;
        }

        // IMP-DOG-017 log names (the harness contract): around / chase / front / stand / overtake. The old hook / run /
        // wait names are gone with the shapes they described.
        string ModeName()
        {
            if (_mode == ModeChase) return "chase";
            if (_mode == ModeFront) return "front";
            if (_mode == ModeStand) return "stand";
            if (_mode == ModeOvertake) return "overtake";
            return "around";
        }

        // IMP-DOG-017: the tail of the plan= line. solve = feasible (instant, shape) pairs the last Solve found (speed
        // and time tests passed; placement raycasts are only paid by candidates that would win). pick = what this line
        // commits (ground / none when no catch shape). vReq / vCap = the committed pace and its cap. band = the
        // flight-time interval(s) of the jump band from the last Solve, none when the disc never passes through it.
        string SolveTail(int chosen)
        {
            string pick = chosen == PlanRetrieve ? "ground" : (chosen == PlanNone ? "none" : ModeName());
            string band = _bandIn < 0f ? "none" : _bandIn.ToString("F2") + "-" + _bandOut.ToString("F2");
            if (_bandIn2 >= 0f) band = band + "+" + _bandIn2.ToString("F2") + "-" + _bandOut2.ToString("F2");
            return " solve=" + _solveCount + " pick=" + pick + " vReq=" + _pickVReq.ToString("F2")
                + " vCap=" + _pickVCap.ToString("F1") + " band=" + band;
        }

        string MotionTail()
        {
            if (!_motionLatched) ComputeMotion();
            return " mode=" + ModeName() + " yawRateMax=" + _yawRateMax.ToString("F0")
                + " speedMin=" + _speedMin.ToString("F2") + " vTake=" + _vTake.ToString("F2")
                // How far the approach was. Under MotionMinSpeed * MotionWindow the dog COULD NOT have been running
                // for the whole window, however lively it is: the disc simply came down next to it.
                + " path=" + _planPath.ToString("F1")
                // IMP-DOG-017: the turn rate left out of yawRateMax because the dog was below walkSpeed (launch spin).
                + " launchYaw=" + _launchYaw.ToString("F0");
        }

        // Horizontal angle between the way the dog RAN IN and the way the disc flies at the catch: 0 = came from
        // behind the disc, 90 = crossed the line, 180 = ran at it head-on. The number the presets are judged by.
        // Round 2 measures the run-in, not the facing: the dog always ends up facing into the disc (_forward),
        // so the facing would read 180 for every throw.
        float EntryAngle()
        {
            Vector3 dv = frisbee._VelocityAt(_v0, _curve, _catchT); dv.y = 0f;
            if (dv.sqrMagnitude < 1e-4f || _entryDir.sqrMagnitude < 1e-4f) return 0f;
            return Vector3.Angle(_entryDir, dv);
        }

        // Tail shared by the catch / stand-catch lines (IMP-DOG-008 log format). Round 2 drops vTake (the takeoff
        // is always from a standstill) and pass (there is one scan).
        // mouthLocal is the ONE number that separates the v4 twist hypotheses: the planner places the root so that
        // root + rotation * _mouthCatch lands on the disc, while the decision measures the real mouthAnchor. If the
        // clip's yaw reaches the runtime rig, mouthLocal == the facts; if it does not, it reads like the untwisted
        // jump mouth; if the frame or a sign is wrong, it is the facts with a component negated. Measured the same
        // way the clip editor measures the facts (rotation only, no scale), so the two are directly comparable.
        string VariantTail()
        {
            Vector3 ml = Quaternion.Inverse(dogRoot.rotation) * (mouthAnchor.position - dogRoot.position);
            // clipT = where the Animator actually is inside the jump clip. The plan puts the catch at
            // clipCrouchDuration + clipRiseDuration (0.60 s for both clips) by construction, so a clipT that is
            // not 0.60 means the pose is out of phase; a clipT of 0.60 with a mouth that is not the facts means
            // the rig is not reproducing the authored pose. Those are different bugs in different files.
            AnimatorStateInfo si = dogAnimator.GetCurrentAnimatorStateInfo(0);
            // Round 4: every catch / miss line also carries how the dog got there and the two motion numbers, so a
            // throw can be failed for LOOKING like a machine even when the mouth met the disc.
            return MotionTail() + " v=" + variant + " entry=" + EntryAngle().ToString("F0")
                + " mouthLocal=(" + ml.x.ToString("F3") + "," + ml.y.ToString("F3") + "," + ml.z.ToString("F3") + ")"
                + " rootFwd=" + Vector3.Angle(dogRoot.forward, _forward).ToString("F0")
                + " clipT=" + (si.normalizedTime * si.length).ToString("F3") + " animSpd=" + dogAnimator.speed.ToString("F2") + DogTag();
        }

        // ---- Harness read-outs (IMP-DOG-012). Read-only views of the planner cost counters (declared in the main file,
        // incremented in the Planner). The ClientSim harness reads the backing fields off the Udon heap by name
        // (_planScans / _planEvaluations / _planHookTries); these properties are for Editor-side / U# callers. ----
        public int PlanScans { get { return _planScans; } }
        public int PlanEvaluations { get { return _planEvaluations; } }
        public int PlanHookTries { get { return _planHookTries; } }
    }
}
