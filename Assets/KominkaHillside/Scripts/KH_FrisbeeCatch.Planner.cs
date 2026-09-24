using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    // IMP-DOG-012 partial (Planner): the intercept planner. Plan / KeepPlan, the approach costs (Eta, Hook*, Approach*),
    // the ground and lane tests, and the candidate scans (Pass 1-4 + overtake). Moved verbatim from KH_FrisbeeCatch.cs;
    // fields, constants and synced state stay in the main file.
    public partial class KH_FrisbeeCatch
    {
        // Earliest flight time at which the mouth can be brought to the disc. See the class comment.
        // Time for the dog to bring its nose onto a line: turn, accelerate, run (review of the first Plays:
        // dist/speed alone made a chosen candidate infeasible on the very next replan, and the plan fell apart).
        float Eta(float dist, float turnDeg)
        {
            float v = chaseRunSpeed, acc = chaseAcceleration;
            float accelDist = v * v / (2f * acc);
            float run = dist < accelDist ? Mathf.Sqrt(2f * dist / acc) : v / acc + (dist - accelDist) / v;
            // Round 4: HALF the turn, because a dog turns while it is getting under way, not before it. The turn
            // was charged in full when it cost 720 deg/s (0.25 s for an about-face and nobody noticed); at a dog's
            // 240 deg/s the same sum bills 0.75 s and refuses every candidate on a throw that goes over its head
            // (measured: every mode fell through to the ground plan). MoveToward also translates along the target
            // direction whatever the body is doing, so a fully serial turn was never what the run actually costs.
            return run + 0.5f * turnDeg / turnSpeedDegPerSec;
        }

        // Ground height under a point on the trajectory. The scan needs it per candidate (the disc may come down
        // over a different part of the flat than the dog stands on); no hit keeps the dog's own ground.
        float GroundYAt(Vector3 p)
        {
            RaycastHit hit;
            float top = p.y + groundRayOffset;
            float len = top - _groundY + groundRayDistance;
            if (len < groundRayDistance) len = groundRayDistance;
            if (Physics.Raycast(new Vector3(p.x, top, p.z), Vector3.down, out hit, len, groundLayerMask, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return _groundY;
        }

        // Where the root must be to meet the disc at flight time t, and the spot on the ground under it.
        // The dog faces INTO the oncoming disc (round 2: it waits for the disc, it no longer runs along with it),
        // so the mouth offset is taken off that facing. Fills the _geo* scratch; commits nothing.
        void CatchGeometry(float t, float rise, float groundY, bool stand, Vector3 dogPos)
        {
            Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
            Vector3 dv = frisbee._VelocityAt(_v0, _curve, t); dv.y = 0f;
            Vector3 fwd;
            if (dv.sqrMagnitude > 1e-4f) fwd = -dv.normalized;
            else { fwd = d - dogPos; fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = dogRoot.forward; }
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd = fwd.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 mouth = stand ? mouthOffsetStand : _mouthCatch;
            _geoForward = fwd;
            _geoCatchRoot = new Vector3(d.x, groundY + rise, d.z) - fwd * mouth.z - right * mouth.x;
            _geoStandRoot = new Vector3(_geoCatchRoot.x, groundY, _geoCatchRoot.z);
        }

        // Intercept-first (IMP-DOG-008 round 2). The trajectory is known at the throw, so the dog picks the point
        // it wants and goes to stand under it: the scan walks the flight forward and takes the FIRST candidate it
        // can reach in time, which - since the disc is coming down by then - is the HIGHEST one it can catch.
        // A candidate is feasible only if the dog can be standing there, settled, before the crouch has to begin.
        // A plan already chosen is kept while it stays reachable, so replanning cannot flap.
        //
        // Round 4 order (the preference, not just the fallback): hook jump AT SPEED -> running jump -> jump from a
        // wait (the round-3 shape, kept only for throws whose spare time even the longest loop cannot use) ->
        // stand catch -> v3 overtake -> ground. A jump is taken wherever one is feasible, because PCVR 3's
        // complaint was that the dog barely jumped at all.
        void Plan(float tNow)
        {
            _groundY = SnapToGround(dogRoot.position).y;
            Vector3 dogPos = dogRoot.position;
            float held = _catchT - tNow;
            if ((_plan == PlanJump || _plan == PlanStandOrOvertake) && held > -(catchWindow + 0.02f) && KeepPlan(tNow, dogPos, held)) return;
            _planScans++;             // IMP-DOG-012 cost counter: the scans below run
            // Round 5 order. v3 (並走) IS the overtake, so it asks for one before anything else. Every other preset
            // asks for it after both jumps and before anything that stands still: a jump is still preferred
            // wherever one is reachable, but a throw the dog started too late for (beside the thrower, after a
            // よしよし) is now run down from behind instead of being abandoned to the ground plan.
            int chosen = _overtake ? ScanOvertake(tNow, dogPos) : PlanNone;
            if (chosen == PlanNone) chosen = ScanJump(tNow, dogPos);
            if (chosen == PlanNone) chosen = ScanRunJump(tNow, dogPos);
            if (chosen == PlanNone && !_overtake) chosen = ScanOvertake(tNow, dogPos);
            if (chosen == PlanNone) chosen = ScanWaitJump(tNow, dogPos);
            if (chosen == PlanNone) chosen = ScanStand(tNow, dogPos);
            if (chosen == PlanNone) { _planSlack = 0f; _runningCatch = false; _planEta = -1f; if (_landingValid && DogMayGo(_landing)) chosen = PlanRetrieve; }
            // Getting here at all means the keep rule declined and the scans ran, so whatever came back is a FRESH
            // commit: this is the moment the estimate was made, and the clock etaErr is measured against.
            _planEtaT = tNow; _etaLogged = false;
            // An overtake and a stand catch are both PlanStandOrOvertake but nothing alike, and a hook and a running jump are
            // both PlanJump, so the mode is part of the identity of the plan, not just of the line.
            if (chosen != _plan || _runningCatch != _loggedRunning || _mode != _loggedMode)
            {
                _settledSince = -1f; _holding = false;   // a new plan means a new spot: the hold starts over
                Debug.Log("[KH_FrisbeeCatch] plan=" + chosen + " run=" + (_runningCatch ? 1 : 0) + " mode=" + ModeName()
                    + " catchT=" + _catchT.ToString("F2") + " rise=" + _rootRise.ToString("F2")
                    + " tUp=" + _tUp.ToString("F2") + " dist=" + Vector3.Distance(dogPos, chosen == PlanRetrieve ? _landing : _standRoot).ToString("F1")
                    + " slack=" + _planSlack.ToString("F2") + " eta=" + _planEta.ToString("F2")
                    + " entry=" + EntryAngle().ToString("F0") + " v=" + variant
                    // Which pose facts this plan was aimed with: the twist set is the one under suspicion (v4).
                    + (_twist ? " facts=(" + _mouthCatch.x.ToString("F3") + "," + _mouthCatch.y.ToString("F3") + "," + _mouthCatch.z.ToString("F3") + ")" : ""));
                _loggedRunning = _runningCatch; _loggedMode = _mode;
            }
            _plan = chosen;
        }

        // True = the plan the dog is already executing still works, so leave it alone.
        bool KeepPlan(float tNow, Vector3 dogPos, float held)
        {
            if (_mode == ModeHook || _mode == ModeRun || _mode == ModeStraight)
            {
                // Every momentum jump is re-derived, not merely re-checked: the crouch start follows the speed the
                // dog has RIGHT NOW, so a dog that is running early pushes the crouch start further out instead of
                // arriving and stopping on it (round 2's failure, and the reason PCVR 3 saw no running jumps).
                float vTake = Mathf.Clamp(_speed, _takeoffMin, _takeoffCap);
                Vector3 ground = new Vector3(_catchRoot.x, _catchRoot.y - _rootRise, _catchRoot.z);
                // _forward is FIXED at the commit (the run-up is a line, not something re-aimed every tick); only
                // the crouch start slides along it, and never past the dog - a run-up the dog would have to reverse
                // out of is what dropped every twist plan.
                Vector3 toGround = ground - dogPos; toGround.y = 0f;
                float fit = toGround.magnitude / (_tUp + _crouchReal);
                if (vTake > fit) vTake = fit < RunJumpShortTakeoff ? RunJumpShortTakeoff : fit;
                Vector3 crouchStart = ground - _forward * (vTake * (_tUp + _crouchReal));
                if (!DogMayGo(crouchStart)) return false;
                // The crouch start slides with the speed the dog HAS, so the landing slides with it (Codex D1).
                // Only the cheap half of the test at 10 Hz; the raycasts are done at the commit and at the liftoff.
                if (!DogMayGo(LandingOf(crouchStart + _forward * (vTake * _crouchReal), _forward, vTake, _tUp))) return false;
                // Deadband. The crouch start is derived from the speed, and the speed is paced at the crouch start:
                // rewriting it at 10 Hz makes the two chase each other (the dog speeds up, the point comes toward
                // it, it slows down, the point runs away). Only a real move is worth re-aiming for.
                if (Vector3.Distance(crouchStart, _standRoot) > 0.10f)
                {
                    // The loop is attached to the run-up line, so it travels with it instead of being re-solved.
                    if (_hookActive && !_hookDone) _hookPoint += crouchStart - _standRoot;
                    _standRoot = crouchStart;
                    _jumpSpeed = vTake;
                }
                float availR = (_catchT - _crouchReal - _tUp) - tNow;
                Vector3 toR = crouchStart - dogPos; toR.y = 0f;
                float pathR = _hookActive && !_hookDone ? HookPath(dogPos, _hookPoint, crouchStart) : toR.magnitude;
                if (availR <= 0.02f) return pathR < 1.2f;
                // A little over the run speed is not a dropped plan: the dog cuts the corner at A, so the measured
                // path always overstates what is left. Dropping on the nose made the lob fall to the ground plan.
                if (pathR / availR > chaseRunSpeed * 1.15f) return false;   // only the sprint is the hard limit
                _jumpSpeed = vTake;
                return true;
            }
            // Settling is not required to KEEP a plan: once the dog is on its way, arriving at all beats swapping to
            // a candidate it has not started for. The facing IS required - it is the thing the dog was skipping -
            // except on an overtake, which is caught facing the way it runs.
            Vector3 toHeld = _standRoot - dogPos; toHeld.y = 0f;
            float distHeld = toHeld.magnitude;
            // Close in, the dog is not running anywhere: it shuffles centimetres and turns on the spot, so the cost
            // of the plan is its heading residual, not a run plus a turn onto some target direction. Charging
            // Angle(dog->spot, _forward) here read 180 deg for a dog standing just PAST the spot and cost 0.25 s +
            // Eta, which dropped every stand catch onto the ground plan a moment before its window (ClientSim
            // regression, Desktop 5-6 m/s throws: plan=3 slack=0.32 -> plan=2, 0 catches).
            bool close = _holding || distHeld < HoldExitRadius;
            float cost;
            if (_runningCatch) cost = Eta(distHeld, Vector3.Angle(dogRoot.forward, toHeld));
            else if (close) cost = Eta(distHeld, 0f) + Vector3.Angle(dogRoot.forward, _forward) / standTurnDegPerSec;
            else if (_hookActive && !_hookDone) cost = HookEta(dogPos, _hookPoint, _standRoot);
            else cost = Mathf.Max(Eta(distHeld, Vector3.Angle(dogRoot.forward, toHeld)),   // same overlap as ApproachHold
                Vector3.Angle(distHeld > 0.05f ? toHeld / distHeld : dogRoot.forward, _forward) / turnSpeedDegPerSec);
            // A plan the dog is already standing on is never given up. Success is decided by the catch window
            // (stand catch) or by the 0.5 m gate at the crouch (jump) - not by a feasibility sum that can go
            // negative while the dog is in exactly the right place.
            bool committed = _holding && !_runningCatch && distHeld <= HoldEnterRadius
                && (_plan == PlanStandOrOvertake || _catchT - _tUp - _crouchReal - tNow <= 0.5f);
            // Round 5 (PCVR 2026-09-22): a STAND catch is not dropped while the dog can still physically get to the
            // spot before the catch window shuts. `cost` above charges a settle and a facing residual the catch
            // does not actually require - a stand catch succeeds if the mouth is within catchTolerance at the
            // instant, full stop - and three v1 stand catches were dropped onto the ground plan with the dog still
            // 1.1-3.1 m out and seconds of flight left. The run itself is the only hard constraint; let the window
            // be the judge of the rest.
            if (!committed && _plan == PlanStandOrOvertake && !_runningCatch
                && Eta(distHeld, Vector3.Angle(dogRoot.forward, toHeld)) <= held + catchWindow) committed = true;
            if (!committed && cost + _crouchReal + _tUp > held + 0.05f) return false;
            // Fine-tune while waiting: the catch time is fixed, the spot is re-derived from the prediction (the
            // ground under the disc may read differently from where the dog now stands). Only a real difference
            // moves the target: a 10 Hz rewrite of the same point is what reads as jitter.
            if (_holding && !_runningCatch)
            {
                CatchGeometry(_catchT, _rootRise, GroundYAt(frisbee._PositionAt(_p0, _v0, _curve, _catchT)), _plan == PlanStandOrOvertake, dogPos);
                if (DogMayGo(_geoStandRoot) && Vector3.Distance(_geoStandRoot, _standRoot) > 0.03f)
                { _catchRoot = _geoCatchRoot; _standRoot = _geoStandRoot; _forward = _geoForward; }
            }
            return true;
        }

        // The six loops, smallest to largest. Spare time is spent here, on the ground, at speed.
        float HookLenAt(int k) { return HookLengthMin + (HookLengthMax - HookLengthMin) * (k / (float)(HookCombos - 1)); }
        float HookAngAt(int k) { return HookAngleMin + (HookAngleMax - HookAngleMin) * (k / (float)(HookCombos - 1)); }

        float HookPath(Vector3 dogPos, Vector3 A, Vector3 C)
        {
            Vector3 toA = A - dogPos; toA.y = 0f;
            Vector3 toC = C - A; toC.y = 0f;
            return toA.magnitude + toC.magnitude;
        }

        // The whole path run as one (the dog never stops on it), plus the turn onto it and half the corner at A -
        // the other half is turned while still running, which is the entire point of the hook.
        float HookEta(Vector3 dogPos, Vector3 A, Vector3 C)
        {
            Vector3 toA = A - dogPos; toA.y = 0f;
            float dA = toA.magnitude;
            Vector3 dirA = dA > 0.05f ? toA / dA : dogRoot.forward;
            float turnIn = Vector3.Angle(dogRoot.forward, dirA);
            Vector3 toC = C - A; toC.y = 0f;
            float legLen = toC.magnitude;
            float corner = legLen > 0.01f ? Vector3.Angle(dirA, toC / legLen) : 0f;
            return Eta(dA + legLen, turnIn) + 0.5f * corner / turnSpeedDegPerSec;
        }

        // How the dog reaches the crouch start C on the line F, STILL RUNNING, inside `avail` seconds. Fills
        // _apPoint / _apMode / _apEta / _apLen / _apPath / _apHook. It takes the BIGGEST loop that still fits, so a
        // throw with time to spare is answered by running further, not by arriving early and standing there.
        // _apMode comes back ModeStraight when no loop fits but a straight run-up would (the dog is already coming
        // from a direction it can straighten out), and ModeWait when the only way to be there is to be there early.
        void Approach(Vector3 C, Vector3 F, Vector3 dogPos, float avail)
        {
            Vector3 rel = dogPos - C; rel.y = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, F);
            float side = Vector3.Dot(rel, right) < 0f ? -1f : 1f;
            bool found = false;
            avail -= HookEtaMargin;    // the loop is sized to land inside the budget, not exactly on it
            for (int k = 0; k < HookCombos; k++)
            {
                _planHookTries++;     // IMP-DOG-012 cost counter
                float len = HookLenAt(k), deg = HookAngAt(k);
                // -F swung `deg` toward +right for side = +1 (AngleAxis about up turns +right toward -F).
                Vector3 A = C + (Quaternion.AngleAxis(-side * deg, Vector3.up) * (-F)) * len;
                if (!DogMayGo(A)) A = C + (Quaternion.AngleAxis(side * deg, Vector3.up) * (-F)) * len;
                if (!DogMayGo(A)) continue;
                float e = HookEta(dogPos, A, C);
                if (found && e > avail) break;      // every loop after this one is only longer
                if (!found || e <= avail)
                { _apPoint = A; _apEta = e; _apLen = len; _apPath = HookPath(dogPos, A, C); _apHook = true; _apMode = ModeHook; found = true; }
            }
            if (found && _apEta <= avail) return;
            // No loop fits. A straight run-up still takes off at speed, but only if the dog is coming from a
            // direction it can straighten out; otherwise it would have to arrive backwards, and that is a wait.
            Vector3 to = C - dogPos; to.y = 0f;
            float d = to.magnitude;
            Vector3 app = d > 0.05f ? to / d : F;
            _apPoint = C; _apLen = 0f; _apHook = false; _apPath = d;
            _apEta = Eta(d, d > 0.05f ? Vector3.Angle(dogRoot.forward, to) : 0f);
            _apMode = Vector3.Angle(app, F) <= StraightMaxOffAngle ? ModeStraight : ModeWait;
        }

        // The approach for the plans that really do have to be there early (a high lob, a stand catch): one small
        // hook to deliver the facing, or the straight line - whichever is QUICKER. A dog standing 2 m from the spot
        // should not run a 4.8 m loop to face the right way, and a dog that would have to arrive backwards should
        // not stand there grinding round at the standing rate; charging both and taking the cheaper one is the only
        // version that keeps the near cases (the ClientSim stand catches) and the far ones at the same time.
        // Either way the heading is rolled onto _forward across the last metres of the run-in (HookOrSprint), so
        // the residual turn is charged at the running rate, not at the standing one.
        void ApproachHold(Vector3 T, Vector3 F, Vector3 dogPos) { ApproachHold(T, F, dogPos, true); }

        // allowHook = false: the straight line, whatever the estimate says. Round 5 (see StandStraightSlack).
        void ApproachHold(Vector3 T, Vector3 F, Vector3 dogPos, bool allowHook)
        {
            Vector3 to = T - dogPos; to.y = 0f;
            float d = to.magnitude;
            Vector3 app = d > 0.05f ? to / d : F;
            float turn = d > 0.05f ? Vector3.Angle(dogRoot.forward, to) : Vector3.Angle(dogRoot.forward, F);
            // max, not sum: the heading is rolled onto F ACROSS the run-in (HoldBlendLength), so the turn and the
            // run happen at the same time. Adding them charged the lob twice and left the jump with 0.03 s of
            // slack, which the next replan then dropped onto a stand catch (measured). The dog still has to be
            // there, and it still has to have turned - whichever of the two takes longer is the cost.
            float straightEta = Mathf.Max(Eta(d, turn), Vector3.Angle(app, F) / turnSpeedDegPerSec);
            Vector3 rel = dogPos - T; rel.y = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, F);
            float side = Vector3.Dot(rel, right) < 0f ? -1f : 1f;
            for (int k = 0; allowHook && k < 2; k++)
            {
                float s = k == 1 ? -side : side;
                Vector3 A = T + (Quaternion.AngleAxis(-s * HookAngleDeg, Vector3.up) * (-F)) * HookLength;
                if (!DogMayGo(A)) continue;
                float e = HookEta(dogPos, A, T);
                if (e > straightEta) break;
                _apPoint = A; _apMode = ModeWait; _apLen = HookLength; _apHook = true;
                _apPath = HookPath(dogPos, A, T); _apEta = e;
                return;
            }
            _apPoint = T; _apMode = ModeWait; _apLen = 0f; _apHook = false; _apPath = d; _apEta = straightEta;
        }

        bool LaneClear(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from; d.y = 0f;
            float dist = d.magnitude;
            if (dist < 0.05f) return true;
            RaycastHit hit;
            return !Physics.Raycast(from + Vector3.up * 0.3f, d / dist, out hit, dist, frisbee.obstacleMask, QueryTriggerInteraction.Ignore);
        }

        // Codex D1 高: round 2 dropped the landing test the old TryJump did (DogMayGo + ground within 0.25 m + the
        // lane). A candidate whose catch point and crouch start are both inside the flat can still come DOWN
        // outside it, or on a step, or through the fence - and the recover slide then carries the dog on out. So
        // every jump candidate is judged on where it lands as well as on where it leaves the ground.
        // crouchStart is where the crouch begins; the feet leave the ground vTake * crouch further along fwd.
        bool LandingOk(Vector3 crouchStart, Vector3 fwd, float vTake, float tUp, float crouch, float groundY)
        {
            Vector3 liftoff = crouchStart + fwd * (vTake * crouch);
            Vector3 landing = LandingOf(liftoff, fwd, vTake, tUp);
            if (!DogMayGo(landing)) return false;
            if (!GroundOk(landing, groundY)) return false;
            return LaneClear(crouchStart, landing + fwd * LandingLaneMargin);
        }

        // A step or a hole where the dog would stand or take off.
        bool GroundOk(Vector3 spot, float groundY)
        {
            RaycastHit hit;
            if (!Physics.Raycast(spot + Vector3.up * groundRayOffset, Vector3.down, out hit, groundRayDistance, groundLayerMask, QueryTriggerInteraction.Ignore)) return false;
            return Mathf.Abs(hit.point.y - groundY) <= 0.25f;
        }

        // ---- Pass 1: the jump the dog hooks into AND TAKES OFF FROM AT SPEED. The run-up is the hook's last leg:
        // the dog crosses the crouch start still running and carries that speed through the jump (user, round 4
        // amendment). The first scan also owns the landing point. ----
        int ScanJump(float tNow, Vector3 dogPos)
        {
            float g = frisbee.gravity;
            float floor = minRootRise > PreferJumpMinRise ? minRootRise : PreferJumpMinRise;
            float vTake = Mathf.Clamp(_speed, RunJumpMinTakeoff, RunJumpMaxTakeoff);
            for (int i = 0; i < PlanSamples; i++)
            {
                _planEvaluations++;   // IMP-DOG-012 cost counter (read by the harness only)
                float t = tNow + 0.2f + i * PlanStep;
                Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
                float groundY = GroundYAt(d);
                float h = d.y - groundY;
                if (h <= frisbee.radius) { _landing = new Vector3(d.x, groundY, d.z); _landingValid = true; return PlanNone; }
                float rise = h - _mouthCatch.y;
                if (rise > maxRootRise) continue;         // still out of reach; it comes down later
                if (rise < floor) continue;               // not a jump: a later pass may stand under it
                CatchGeometry(t, rise, groundY, false, dogPos);
                if (!DogMayGo(_geoStandRoot)) continue;
                float tUp = Mathf.Sqrt(2f * rise / g);
                float scale = _riseDur / tUp;
                float crouch = _crouchDur / scale;
                Vector3 crouchStart = _geoStandRoot - _geoForward * (vTake * (tUp + crouch));
                if (!DogMayGo(crouchStart)) continue;
                float avail = (t - crouch - tUp) - tNow;
                if (avail <= 0.05f) continue;
                Approach(crouchStart, _geoForward, dogPos, avail);
                if (_apMode == ModeWait) continue;             // only reachable by being there early: pass 3's job
                if (_apEta > avail - HookEtaMargin) continue;  // cannot get onto the line in time with room to spare
                if (_apPath / avail < HookWaitPace) continue;  // even the longest loop leaves it loitering: pass 3
                if (!LaneClear(dogPos, _apPoint) || !LaneClear(_apPoint, crouchStart)) continue;
                if (!GroundOk(_geoStandRoot, groundY) || !GroundOk(crouchStart, groundY)) continue;
                if (!LandingOk(crouchStart, _geoForward, vTake, tUp, crouch, groundY)) continue;   // Codex D1
                CommitApproach(t, rise, tUp, scale, crouch, avail - _apEta, dogPos, crouchStart, vTake);
                return PlanJump;
            }
            return PlanNone;
        }

        // ---- Pass 3: the round-3 jump - be under the point early, settle, go up from there. Kept only for the
        // throws whose spare time even the biggest loop cannot spend (a high lob), because the user is right that
        // a dog does not brake to a halt and then hop. Logged mode=wait so the harness can tell the two apart. ----
        int ScanWaitJump(float tNow, Vector3 dogPos)
        {
            float g = frisbee.gravity;
            float floor = minRootRise > PreferJumpMinRise ? minRootRise : PreferJumpMinRise;
            for (int i = 0; i < PlanSamples; i++)
            {
                _planEvaluations++;   // IMP-DOG-012 cost counter (read by the harness only)
                float t = tNow + 0.2f + i * PlanStep;
                Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
                float groundY = GroundYAt(d);
                float h = d.y - groundY;
                if (h <= frisbee.radius) break;
                float rise = h - _mouthCatch.y;
                if (rise > maxRootRise || rise < floor) continue;
                CatchGeometry(t, rise, groundY, false, dogPos);
                if (!DogMayGo(_geoStandRoot)) continue;
                float tUp = Mathf.Sqrt(2f * rise / g);
                float scale = _riseDur / tUp;
                float crouch = _crouchDur / scale;
                ApproachHold(_geoStandRoot, _geoForward, dogPos);
                float slack = (t - tNow) - (_apEta + settleTime + crouch + tUp);
                if (slack < 0f) continue;                 // cannot be standing there, already turned, in time
                if (!LaneClear(dogPos, _apPoint) || !LaneClear(_apPoint, _geoStandRoot)) continue;
                if (!GroundOk(_geoStandRoot, groundY)) continue;
                // Codex D1: the wait jump goes straight up (vTake 0), so the landing is the spot itself - but the
                // stride the recover slides out still has to be inside the flat and clear.
                if (!LandingOk(_geoStandRoot, _geoForward, 0f, tUp, crouch, groundY)) continue;
                CommitApproach(t, rise, tUp, scale, crouch, slack, dogPos, _geoStandRoot, 0f);
                return PlanJump;
            }
            return PlanNone;
        }

        // ---- Pass 4: stand under it. Same small hook, so the dog is already facing the disc when it arrives. ----
        int ScanStand(float tNow, Vector3 dogPos)
        {
            float floor = minRootRise > PreferJumpMinRise ? minRootRise : PreferJumpMinRise;
            for (int i = 0; i < PlanSamples; i++)
            {
                _planEvaluations++;   // IMP-DOG-012 cost counter (read by the harness only)
                float t = tNow + 0.2f + i * PlanStep;
                Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
                float groundY = GroundYAt(d);
                float h = d.y - groundY;
                if (h <= frisbee.radius) break;
                if (h - _mouthCatch.y >= floor) continue;                 // a jump candidate: passes 1-3 had their turn
                if (h < mouthOffsetStand.y - tooLowBelowMouth) continue;  // below the standing mouth: wait for the ground
                CatchGeometry(t, 0f, groundY, true, dogPos);
                if (!DogMayGo(_geoStandRoot)) continue;
                ApproachHold(_geoStandRoot, _geoForward, dogPos);
                float slack = (t - tNow) - (_apEta + settleTime);
                // Round 5: a thin stand catch runs the straight line. The loop only ever wins on the ESTIMATE, and
                // a plan with less than StandStraightSlack to give has nothing to pay the estimate's error with.
                // Re-costing it straight usually costs slack; a candidate that then no longer fits is refused here
                // and the scan offers a later (lower) one, which is the honest answer.
                if (_apHook && slack < StandStraightSlack)
                {
                    ApproachHold(_geoStandRoot, _geoForward, dogPos, false);
                    slack = (t - tNow) - (_apEta + settleTime);
                }
                if (slack < 0f) continue;
                if (!LaneClear(dogPos, _apPoint) || !LaneClear(_apPoint, _geoStandRoot)) continue;
                if (!GroundOk(_geoStandRoot, groundY)) continue;
                CommitApproach(t, 0f, 0f, 1f, 0f, slack, dogPos, _geoStandRoot, 0f);
                return PlanStandOrOvertake;
            }
            return PlanNone;
        }

        // Shared tail of passes 1, 3 and 4: take the _geo* / _ap* scratch as the plan. standRoot is the point the
        // chase drives at - the crouch start for a momentum jump, the catch spot itself for a wait.
        void CommitApproach(float t, float rise, float tUp, float scale, float crouch, float slack, Vector3 dogPos, Vector3 standRoot, float jumpSpeed)
        {
            _catchT = t; _catchRoot = _geoCatchRoot; _standRoot = standRoot; _forward = _geoForward;
            _rootRise = rise; _tUp = tUp; _animScale = scale; _crouchReal = crouch; _jumpSpeed = jumpSpeed;
            _planSlack = slack; _planPath = _apPath; _planEta = _apEta; _catchDecided = false; _runningCatch = false;
            _mode = _apMode; _hookActive = _apHook; _hookPoint = _apPoint; _hookLen = _apLen; _hookDone = false;
            _takeoffMin = RunJumpMinTakeoff; _takeoffCap = RunJumpMaxTakeoff;
            // entry = the direction the dog will actually arrive from (the hook's second leg), not the beeline.
            Vector3 entry = _apHook ? standRoot - _apPoint : standRoot - dogPos; entry.y = 0f;
            _entryDir = entry.sqrMagnitude > 1e-4f ? entry.normalized : _geoForward;
        }

        // ---- Pass 2: the running jump. Run at the point along the dog's own heading and push off at the speed it
        // already has - from behind, across, or straight into the oncoming disc. Nothing here waits, so it is the
        // answer whenever the hook cannot be finished in time, and it is tried BEFORE any plan that stands still. ----
        int ScanRunJump(float tNow, Vector3 dogPos)
        {
            float g = frisbee.gravity;
            float floor = minRootRise > PreferJumpMinRise ? minRootRise : PreferJumpMinRise;
            for (int i = 0; i < PlanSamples; i++)
            {
                _planEvaluations++;   // IMP-DOG-012 cost counter (read by the harness only)
                float t = tNow + 0.2f + i * PlanStep;
                Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
                float groundY = GroundYAt(d);
                float h = d.y - groundY;
                if (h <= frisbee.radius) break;
                float rise = h - _mouthCatch.y;
                if (rise > maxRootRise || rise < floor) continue;
                Vector3 run = d - dogPos; run.y = 0f;
                if (run.sqrMagnitude < 1f) continue;                   // on top of it already: there is no run-up
                Vector3 fwd = run.normalized;                          // it takes it facing the way it runs
                Vector3 dv = frisbee._VelocityAt(_v0, _curve, t); dv.y = 0f;
                if (dv.sqrMagnitude < 1e-4f) continue;
                float closing = Vector3.Dot(fwd, dv.normalized);
                if (closing < RunJumpMinDot) continue;
                // Running INTO the disc adds both speeds to the closing rate, so the take-off is capped there; the
                // catch window is unchanged and the occasional miss is the price of the dog looking alive.
                float cap = closing < 0f ? RunJumpHeadOnMaxTakeoff : RunJumpMaxTakeoff;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 catchRoot = new Vector3(d.x, groundY + rise, d.z) - fwd * _mouthCatch.z - right * _mouthCatch.x;
                Vector3 standRoot = new Vector3(catchRoot.x, groundY, catchRoot.z);
                float tUp = Mathf.Sqrt(2f * rise / g);
                float scale = _riseDur / tUp;
                float crouch = _crouchDur / scale;
                // Run-up length = take-off speed x (crouch + rise). If the dog is already inside it, SHORTEN the
                // run-up by taking off slower rather than putting the crouch start behind the dog.
                Vector3 toStand = standRoot - dogPos; toStand.y = 0f;
                float vTake = Mathf.Clamp(_speed, RunJumpMinTakeoff, cap);
                float lo = RunJumpMinTakeoff;
                if (toStand.magnitude < vTake * (tUp + crouch))
                { vTake = Mathf.Clamp(toStand.magnitude / (tUp + crouch), RunJumpShortTakeoff, cap); lo = vTake; }
                Vector3 crouchStart = standRoot - fwd * (vTake * (tUp + crouch));
                if (!DogMayGo(standRoot) || !DogMayGo(crouchStart)) continue;
                Vector3 to = crouchStart - dogPos; to.y = 0f;
                float dist = to.magnitude;
                float avail = (t - crouch - tUp) - tNow;
                if (avail <= 0.05f) continue;
                // Reachable at the chase speed; the take-off ceiling is applied over the last RunJumpTrimDistance
                // metres instead, so a candidate is not refused just because the run-up is long.
                if (dist / avail > chaseRunSpeed) continue;
                float eta = Eta(dist, dist > 0.05f ? Vector3.Angle(dogRoot.forward, to) : 0f);
                // Same margin as the hook: a running jump committed with 0.04 s to spare is dropped by the next
                // replan and ends on the ground plan (measured on the twist preset). A dog that is ALREADY running
                // needs half of it - the ETA it is being judged against no longer includes a standing start, and
                // this is the scan that has to re-form a plan after one was dropped mid-flight.
                if (eta > avail - (_speed > runSpeed ? HookEtaMargin * 0.5f : HookEtaMargin)) continue;
                if (avail - eta > RunJumpMaxLoiter) continue;   // round 5: it would arrive and stand there - pass 3's job
                if (!LaneClear(dogPos, crouchStart) || !GroundOk(standRoot, groundY)) continue;
                if (!LandingOk(crouchStart, fwd, vTake, tUp, crouch, groundY)) continue;   // Codex D1
                _catchT = t; _catchRoot = catchRoot; _standRoot = crouchStart; _forward = fwd; _entryDir = fwd;
                _rootRise = rise; _tUp = tUp; _animScale = scale; _crouchReal = crouch; _jumpSpeed = vTake;
                _planSlack = avail - eta; _planPath = dist; _planEta = eta; _catchDecided = false; _runningCatch = false;
                _mode = ModeRun; _hookActive = false; _hookDone = true; _hookPoint = crouchStart; _hookLen = 0f;
                // The band the 10 Hz re-derive must stay inside: without it a head-on run speeds up past its cap,
                // and a shortened run-up grows back out behind the dog.
                _takeoffMin = lo; _takeoffCap = lo > cap ? lo : cap;
                return PlanJump;
            }
            return PlanNone;
        }

        // ---- Overtake pass (round 5: every preset; v3 tries it FIRST, the others after both running jumps - see Plan).
        // Catch it on the run, from behind, without ever stopping. (Was also headed "Pass 4" before IMP-DOG-012.) ----
        int ScanOvertake(float tNow, Vector3 dogPos)
        {
            for (int i = 0; i < PlanSamples; i++)
            {
                _planEvaluations++;   // IMP-DOG-012 cost counter (read by the harness only)
                float t = tNow + 0.2f + i * PlanStep;
                Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
                float groundY = GroundYAt(d);
                float h = d.y - groundY;
                if (h <= frisbee.radius) break;
                if (h - _mouthCatch.y >= minRootRise) continue;          // high enough to be worth a jump: not this
                if (h < mouthOffsetStand.y - tooLowBelowMouth) continue; // below the running mouth: wait for the ground
                Vector3 dv = frisbee._VelocityAt(_v0, _curve, t); dv.y = 0f;
                if (dv.sqrMagnitude < 1e-4f) continue;
                Vector3 fwd = dv.normalized;                             // running WITH the disc, not into it
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 root = new Vector3(d.x, groundY, d.z) - fwd * mouthOffsetStand.z - right * mouthOffsetStand.x;
                if (!DogMayGo(root)) continue;
                Vector3 to = root - dogPos; to.y = 0f;
                float dist = to.magnitude;
                if (dist < 0.5f) continue;                               // already on it: there is nothing to overtake
                Vector3 approach = to / dist;
                if (Vector3.Dot(approach, fwd) < OvertakeMinDot) continue; // crossing or meeting it, not chasing it down
                float turn = Vector3.Angle(dogRoot.forward, to);
                float eta = Eta(dist, turn);
                float slack = (t - tNow) - eta;                          // no settle, no crouch: just be there, moving
                if (slack < 0f) continue;
                if (dist / (t - tNow) < OvertakeMinPace) continue;       // a trot-in is not an overtake
                if (!LaneClear(dogPos, root) || !GroundOk(root, groundY)) continue;
                _catchT = t; _catchRoot = root; _standRoot = root; _forward = fwd; _entryDir = approach;
                _rootRise = 0f; _tUp = 0f; _animScale = 1f; _crouchReal = 0f; _jumpSpeed = 0f;
                _planSlack = slack; _planPath = dist; _planEta = eta; _catchDecided = false; _runningCatch = true;
                _mode = ModeOvertake; _hookActive = false; _hookDone = true; _hookPoint = root; _hookLen = 0f;
                _takeoffMin = RunJumpMinTakeoff; _takeoffCap = RunJumpMaxTakeoff;
                return PlanStandOrOvertake;
            }
            return PlanNone;
        }
    }
}
