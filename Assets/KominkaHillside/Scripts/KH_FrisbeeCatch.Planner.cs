using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    // IMP-DOG-012 partial (Planner): the intercept planner. Fields, constants and synced state stay in the main file.
    //
    // IMP-DOG-017 rewrite (2026-09-26, user decision). The old planner walked the flight in 0.1 s steps and tried a
    // fixed ladder of approach shapes (hook -> run -> overtake -> wait -> stand), each with its own refusal rules
    // (arrive too early, loop too big / too small, not settled). A small dog's jump band is 50-110 ms wide, so it got
    // 0 or 1 samples, the pessimistic ETA refused those, and the next replan fell through to the stand catch. Now:
    //   Solve    once, at the throw: find WHEN the disc is inside this dog's jump band (bisection, 5 ms), sample the
    //            band finely, and for every instant work out the average speed each catch shape needs (A around,
    //            B chase, C front). Feasible = physics only (speed cap, ETA, flat / ground / lane / landing).
    //            Arriving early is never a refusal - the run is paced instead. The best-scoring candidate wins.
    //   Keep     10 Hz: the shape never changes; the pace (path left / time left) is what the dog adjusts. Only a
    //            required pace over 1.05x the cap drops the shape, and then Solve runs again without it.
    //   Fallback D stand -> D' overtake -> E ground, when no jump is feasible.
    public partial class KH_FrisbeeCatch
    {
        // IMP-DOG-017: the one ETA. Same run model as before (accelerate from rest to vMax, then cruise) with the
        // speed cap as an argument (B runs to chaseSprintSpeed), and half the turn charged at the rate the dog would
        // actually turn at: from a standstill it swings round at launchTurnDegPerSec, at speed at turnSpeedDegPerSec.
        // No correction factor: whatever the estimate is off by, the paced run absorbs.
        float EtaCorrected(float dist, float turnDeg, float vMax)
        {
            float acc = chaseAcceleration;
            float accelDist = vMax * vMax / (2f * acc);
            float run = dist < accelDist ? Mathf.Sqrt(2f * dist / acc) : vMax / acc + (dist - accelDist) / vMax;
            float rate = _speed < walkSpeed ? launchTurnDegPerSec : turnSpeedDegPerSec;
            if (rate < 1f) rate = 1f;
            return run + 0.5f * turnDeg / rate;
        }

        // The lowest root rise that still counts as a jump for THIS dog (IMP-DOG-017: proportional to its size,
        // written by Build Scene), never below the clip's playback floor.
        float JumpFloor() { return jumpFloorRise > minRootRise ? jumpFloorRise : minRootRise; }

        // Ground height under a point on the trajectory. The scan needs it per candidate (the disc may come down
        // over a different part of the flat than the dog stands on). DES-DOG-005 B-5: no hit (a drop of more than
        // groundRayDistance below the dog, a cliff) is NoGround - the sample is discarded, never the dog's own ground.
        // TODO(M3, DES-DOG-005 D-2 last line): where the disc floats (FloatsHere), return max(ray, water surface) here. Left out of
        // M2 on purpose: it would change the band scan's rise above the water, which M1 refuses anyway (InJumpWater).
        float GroundYAt(Vector3 p)
        {
            RaycastHit hit;
            float top = p.y + groundRayOffset;
            float len = top - _groundY + groundRayDistance;
            if (len < groundRayDistance) len = groundRayDistance;
            _planRays++;   // C-2: _planRays counts every planner ray (the band scan's too)
            if (Physics.Raycast(new Vector3(p.x, top, p.z), Vector3.down, out hit, len, groundLayerMask, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return NoGround;
        }

        // ---- DES-DOG-005 C-1: reachability (shared by the dog and the holder's aim; '_' = local only) ----

        // DES-DOG-005 M1 (decision 2026-09-28, Codex M1 6-2): two different water rules.
        //   InRunRiver  - where the dog may not RUN / stand for a throw: the river band (half width + BandMargin, bridge decks
        //                 excepted) OUTSIDE the water-play spot. The spot keeps its pre-M1 walking exception (RiverBlocks).
        //   InJumpWater - where the dog may not take off / land a jump (D-1 M1): the band AND the water-play spot.
        // The disc's bank return is unchanged (KH_Frisbee); floating comes in M2.
        bool InRunRiver(Vector3 p)
        {
            if (riverData == null || !riverData._InBand(p, KH_RiverData.BandMargin)) return false;
            return waterSpot == null || !waterSpot._Contains(p);
        }
        bool InJumpWater(Vector3 p)
        {
            if (riverData != null && riverData._InBand(p, KH_RiverData.BandMargin)) return true;
            return waterSpot != null && waterSpot._Contains(p);
        }

        // PlayableAt(p): ground within groundRayDistance under p + up * groundRayOffset, no steeper than runMaxSlopeDeg,
        // not in the river (InRunRiver). One ray; _playY = the ground it hit (for Reachable's end check).
        public bool _PlayableAt(Vector3 p)
        {
            if (InRunRiver(p)) return false;
            _planRays++;
            RaycastHit hit;
            if (!Physics.Raycast(p + Vector3.up * groundRayOffset, Vector3.down, out hit, groundRayDistance, groundLayerMask, QueryTriggerInteraction.Ignore)) return false;
            _playY = hit.point.y;
            return SlopeOk(hit.normal.y, runMaxSlopeDeg);
        }

        // PathOk(from, to): the ground walk (PathWalk, <= 25 rays; only the rays really cast are counted) and no sample in
        // the river (InRunRiver) on the way. from.y is the ground at from, to.y the ground (or a disc) at to.
        public bool _PathOk(Vector3 from, Vector3 to)
        {
            if (PathCrossesRiver(from, to)) return false;
            int r = PathWalk(from, to, groundLayerMask, runMaxStep, RunMaxDrop, groundRayDistance + groundRayOffset);
            _planRays += r >= 0 ? r : -r - 1;
            return r >= 0;
        }

        // Reachable(L, dog), land only in M1: PlayableAt(L) and PathOk(dog, the ground under L).
        // DES-DOG-005 M2 (decision 2026-09-28): a disc now FLOATS in the water (KH_Frisbee), but the dog does not fetch from the
        // water until M3, so floating water is never reachable: the aim keeps its cross there and a floating disc is not
        // retrieved. The river band outside the spot is already refused by PlayableAt (InRunRiver); the extra test is the
        // water-play spot's water, where the pre-M1 walking exception would otherwise send the dog to a disc on the water.
        // TODO(M3, DES-DOG-005 C-1 / D-1): replace this line with the water branch of Reachable - D-1 "floats" is true and, for
        // a dog that does not swim, _DepthAt(L) <= _WaterFetchWadeLimit() - and route it to the water chase (state 11).
        public bool _Reachable(Vector3 at)
        {
            if (waterSpot != null && waterSpot._FloatsAt(at)) return false;
            if (!_PlayableAt(at)) return false;
            return _PathOk(dogRoot.position, new Vector3(at.x, _playY, at.z));
        }

        bool PathCrossesRiver(Vector3 from, Vector3 to)
        {
            if (riverData == null) return false;
            Vector3 d = to - from; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.05f) return InRunRiver(to);
            Vector3 dir = d / len;
            if (len > PathMaxLength) len = PathMaxLength;
            int n = Mathf.CeilToInt(len / PathStep);
            for (int i = 1; i <= n; i++)
            {
                float s = i * PathStep; if (s > len) s = len;
                if (InRunRiver(from + dir * s)) return true;
            }
            return false;
        }

        // One counted ray straight down from height top, len long. Fills _rayY / _rayNy. Inside a Solve it is refused once
        // SolveRayCap placement rays have been cast (C-2: the cap is checked per ray, so a Solve never exceeds it).
        bool RayDown(Vector3 p, float top, float len)
        {
            if (_inSolve && _solveRays >= SolveRayCap) { _solveCapped = true; return false; }
            _planRays++; _solveRays++;
            RaycastHit hit;
            if (!Physics.Raycast(new Vector3(p.x, top, p.z), Vector3.down, out hit, len, groundLayerMask, QueryTriggerInteraction.Ignore)) return false;
            _rayY = hit.point.y; _rayNy = hit.normal.y;
            return true;
        }

        // B-1 JumpGroundOk(p) for one point, near refY: one ray, then slope <= jumpMaxSlopeDeg, not on a bridge deck, not in
        // the river. _jgY = the ground, _jgWhy = why it failed ("" = ok). The ray window is refY +- (jumpMaxRise + 1 m),
        // enough for the rise rule to be judged by the caller.
        bool JumpGround(Vector3 p, float refY)
        {
            _jgWhy = "";
            float span = jumpMaxRise + 1f;
            if (!RayDown(p, refY + span, 2f * span)) { _jgWhy = _solveCapped && _inSolve ? "cap" : "noground"; _jgY = NoGround; return false; }
            _jgY = _rayY; _jgSlope = SlopeDeg(_rayNy);
            if (!SlopeOk(_rayNy, jumpMaxSlopeDeg)) { _jgWhy = "slope"; return false; }
            Vector3 g = new Vector3(p.x, _jgY, p.z);
            if (riverData != null && riverData._OnBridgeDeck(g)) { _jgWhy = "bridge"; return false; }
            if (InJumpWater(g)) { _jgWhy = "river"; return false; }
            return true;
        }

        // B-2 step 3 / B-3: where a root leaving takeoff (ground takeoffY) along fwd at vTake with vertical speed vy comes
        // down. First guess: the take-off ground; ray; air time onto that ground; ray again. The two landing grounds must
        // agree within JumpIterTolerance and pass JumpGround and the rise rule. Adopted: the second air time, the landing
        // point of that time and the ground of the ray shot there (_alTime / _alPt / _alY). _jgWhy says why not.
        bool AirLanding(Vector3 takeoff, float takeoffY, Vector3 fwd, float vTake, float vy)
        {
            float g = frisbee.gravity;
            float ta0 = AirTime(vy, g, 0f);
            if (ta0 <= 0f) { _jgWhy = "apex"; return false; }
            Vector3 l0 = takeoff + fwd * (vTake * ta0);
            if (vTake < 0.01f)
            {
                // Straight up (A, D): the landing IS the take-off spot - one ray, and it is the take-off's own.
                if (!JumpGround(l0, takeoffY)) return false;
                if (Mathf.Abs(_jgY - takeoffY) > JumpIterTolerance) { _jgWhy = "iter"; return false; }
                float taS = AirTime(vy, g, _jgY - takeoffY);
                if (taS <= 0f) { _jgWhy = "apex"; return false; }
                _alTime = taS; _alPt = new Vector3(l0.x, _jgY, l0.z); _alY = _jgY;
                return true;
            }
            if (!JumpGround(l0, takeoffY)) return false;
            float land1 = _jgY;
            if (Mathf.Abs(land1 - takeoffY) > jumpMaxRise) { _jgWhy = "rise"; return false; }
            float ta1 = AirTime(vy, g, land1 - takeoffY);
            if (ta1 <= 0f) { _jgWhy = "apex"; return false; }
            Vector3 l1 = takeoff + fwd * (vTake * ta1);
            if (!JumpGround(l1, land1)) return false;
            float land2 = _jgY;
            if (Mathf.Abs(land2 - land1) > JumpIterTolerance) { _jgWhy = "iter"; return false; }
            if (Mathf.Abs(land2 - takeoffY) > jumpMaxRise) { _jgWhy = "rise"; return false; }
            if (takeoffY + vy * vy / (2f * g) < land2 + JumpApexClearance) { _jgWhy = "apex"; return false; }
            _alTime = ta1; _alPt = new Vector3(l1.x, land2, l1.z); _alY = land2;
            return true;
        }

        // The ground near refY under p (+- groundRayDistance): C0 / V before their lanes (Codex M1 1-2). NoGround = none.
        float GroundNear(Vector3 p, float refY)
        {
            float span = groundRayDistance;
            return RayDown(p, refY + span, 2f * span) ? _rayY : NoGround;
        }

        // D / D' stand spot (no jump): the same slope / bridge / river rule on the spot, and the old +-0.25 m ground match.
        bool StandGround(Vector3 spot, float groundY)
        {
            if (!JumpGround(spot, groundY)) return false;
            if (Mathf.Abs(_jgY - groundY) > 0.25f) { _jgWhy = "step"; return false; }
            return true;
        }

        // C-2: the committed plan's PathOk, not re-tested for the same failed spot every 10 Hz tick.
        bool PlanPathOk(Vector3 dogPos, Vector3 target)
        {
            if (_pathFailValid)
            {
                Vector3 off = target - _pathFailAt; off.y = 0f;
                if (off.sqrMagnitude < 0.25f) return false;
            }
            if (_PathOk(dogPos, target)) return true;
            _pathFailAt = target; _pathFailValid = true;
            return false;
        }

        // C-2 (:228): Reachable(_landing), once per predicted landing (the landing moves only when a Solve re-scans).
        void CheckLanding()
        {
            if (!_landingValid) { _landingChecked = false; _landingReachable = false; return; }
            if (_landingChecked)
            {
                Vector3 off = _landing - _landingCheckedAt; off.y = 0f;
                if (off.sqrMagnitude < 0.25f) return;
            }
            _landingChecked = true; _landingCheckedAt = _landing;
            _landingReachable = _Reachable(_landing);
        }

        // Where the root must be to meet the disc at flight time t, and the spot on the ground under it, facing INTO
        // the oncoming disc. This is shape A's stand spot (and D's). Fills the _geo* scratch; commits nothing.
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

        // 10 Hz. A plan the dog is executing is kept (KeepPlan); otherwise the jump solve runs if it has not run for
        // this throw (or a kept shape was just dropped), and then the fallbacks D -> D' -> E.
        void Plan(float tNow)
        {
            _groundY = SnapToGround(dogRoot.position).y;
            Vector3 dogPos = dogRoot.position;
            float held = _catchT - tNow;
            if ((_plan == PlanJump || _plan == PlanStandOrOvertake) && held <= -(catchWindow + 0.02f)) { if (_dropReason.Length == 0) _dropReason = "expired"; _solved = false; }   // round 3; solve again (Codex review)
            if ((_plan == PlanJump || _plan == PlanStandOrOvertake) && held > -(catchWindow + 0.02f) && KeepPlan(tNow, dogPos, held)) return;
            _planScans++;             // IMP-DOG-012 cost counter: the scans below run
            int chosen = PlanNone;
            if (!_solved)
            {
                chosen = Solve(tNow, dogPos); _solved = true;
                // DES-DOG-005 C-2: the committed jump pays PathOk(dog, L0) once. A path the dog cannot run (a step, a cliff,
                // the river) bans this shape; the next tick solves again without it, the fallbacks run now.
                if (chosen == PlanJump && !PlanPathOk(dogPos, _landPt)) { DropMode("path"); chosen = PlanNone; }
            }
            // IMP-DOG-017: the fallbacks, in this order for every preset. v3 no longer tries the overtake first -
            // its preference is expressed inside Solve (B chase scores highest), and D' is only for no-jump throws.
            if (chosen == PlanNone) { chosen = ScanStand(tNow, dogPos); if (chosen != PlanNone && !PlanPathOk(dogPos, _standRoot)) chosen = PlanNone; }
            if (chosen == PlanNone) { chosen = ScanOvertake(tNow, dogPos); if (chosen != PlanNone && !PlanPathOk(dogPos, _standRoot)) chosen = PlanNone; }
            // DES-DOG-005 C-2: the ground plan needs Reachable(_landing) (computed once per landing), not the plaza circle.
            CheckLanding();
            if (chosen == PlanNone) { _planSlack = 0f; _runningCatch = false; _planEta = -1f; _pickVReq = 0f; _pickVCap = 0f; if (_landingValid && _landingReachable) chosen = PlanRetrieve; }
            // Getting here at all means the keep rule declined and the scans ran, so whatever came back is a FRESH
            // commit: this is the moment the estimate was made, and the clock etaErr is measured against.
            _planEtaT = tNow; _etaLogged = false;
            // An overtake and a stand catch are both PlanStandOrOvertake but nothing alike, and around / chase /
            // front are all PlanJump, so the mode is part of the identity of the plan, not just of the line.
            if (chosen != _plan || _runningCatch != _loggedRunning || _mode != _loggedMode)
            {
                _settledSince = -1f; _holding = false;   // a new plan means a new spot: the hold starts over
                Debug.Log("[KH_FrisbeeCatch] plan=" + chosen + " run=" + (_runningCatch ? 1 : 0) + " mode=" + ModeName()
                    + " catchT=" + _catchT.ToString("F2") + " rise=" + _rootRise.ToString("F2")
                    + " tUp=" + _tUp.ToString("F2") + " dist=" + Vector3.Distance(dogPos, chosen == PlanRetrieve ? _landing : _standRoot).ToString("F1")
                    + " slack=" + _planSlack.ToString("F2") + " eta=" + _planEta.ToString("F2")
                    + " entry=" + EntryAngle().ToString("F0") + " v=" + variant
                    // Which pose facts this plan was aimed with: the twist set is the one under suspicion (v4).
                    + (_twist ? " facts=(" + _mouthCatch.x.ToString("F3") + "," + _mouthCatch.y.ToString("F3") + "," + _mouthCatch.z.ToString("F3") + ")" : "")
                    // IMP-DOG-017: when (flight time), where and how fast the dog was when it changed its mind.
                    + DogTag() + " tNow=" + tNow.ToString("F2") + " dogV=" + _speed.ToString("F2") + " dogPos=" + V3(dogPos)
                    + SolveTail(chosen)
                    // Harness round 2: why the previous plan was given up (empty = fresh plan). See DropMode.
                    + " drop=" + _dropReason);
                _loggedRunning = _runningCatch; _loggedMode = _mode;
            }
            _dropReason = "";
            _plan = chosen;
        }

        // A kept jump shape that no longer works: never offered again for this throw, and Solve runs again now.
        // reason goes on the next plan= line (drop=), so a changed plan can be traced to the rule that fired.
        bool DropMode(string reason)
        {
            _dropReason = reason;
            _bannedModes = _bannedModes | (1 << _mode);
            _solved = false;
            return false;
        }

        // True = the plan the dog is already executing still works, so leave it alone.
        bool KeepPlan(float tNow, Vector3 dogPos, float held)
        {
            if (_plan == PlanJump && (_mode == ModeChase || _mode == ModeFront))
            {
                // Every momentum jump is re-derived, not merely re-checked: the crouch start follows the speed the
                // dog has RIGHT NOW, so a dog that is running early pushes the crouch start further out instead of
                // arriving and stopping on it (round 2's failure, and the reason PCVR 3 saw no running jumps).
                float vTake = Mathf.Clamp(_speed, _takeoffMin, _takeoffCap);
                // PCVR 2026-09-26 (drop=gate 4.4-4.8 m short): the crouch start is derived from the speed, and the
                // speed is paced at the crouch start. When the pace eased the dog down (ahead of schedule), vTake fell,
                // the crouch start slid AWAY along -_forward, and the dog chased a receding target; the first keep
                // tick alone also reset the solve's vTake (B: 6.3) to the standing 2 m/s, moving it ~2 m further out.
                // So vTake only ratchets UP from the committed value (the target never recedes), and inside
                // RunJumpTrimDistance it is frozen. The shortening below (a dog already inside the run-up) still applies.
                if (vTake < _jumpSpeed) vTake = _jumpSpeed;
                Vector3 toCur = _standRoot - dogPos; toCur.y = 0f;
                if (toCur.magnitude <= RunJumpTrimDistance) vTake = _jumpSpeed;
                Vector3 ground = new Vector3(_catchRoot.x, _catchRoot.y - _rootRise, _catchRoot.z);
                // _forward is FIXED at the commit (the run-up is a line, not something re-aimed every tick); only
                // the crouch start slides along it, and never past the dog.
                Vector3 toGround = ground - dogPos; toGround.y = 0f;
                float fit = toGround.magnitude / (_tUp + _crouchReal);
                if (vTake > fit) vTake = fit < RunJumpShortTakeoff ? RunJumpShortTakeoff : fit;
                Vector3 crouchStart = ground - _forward * (vTake * (_tUp + _crouchReal));
                // The crouch start slides with the speed the dog HAS, so the take-off and the landing slide with it (Codex
                // D1). DES-DOG-005 B-2: the jump ground is re-shot only when the crouch start moved >= 0.5 m from where the
                // last T0 / L0 rays were shot (<= 4 rays); otherwise the committed values are reused.
                Vector3 slid = crouchStart - _jumpRayAt; slid.y = 0f;
                if (slid.sqrMagnitude >= 0.25f && !ReshootJumpGround(crouchStart, vTake)) return DropMode(_jgWhy == "rise" || _jgWhy == "iter" || _jgWhy == "apex" ? "landing" : "ground");
                // Deadband: the crouch start is derived from the speed and the speed is paced at the crouch start,
                // so rewriting it at 10 Hz makes the two chase each other. Only a real move is worth re-aiming for.
                if (Vector3.Distance(crouchStart, _standRoot) > 0.10f)
                {
                    _standRoot = crouchStart;
                    _jumpSpeed = vTake;
                }
                float availR = (_catchT - _crouchReal - _tUp) - tNow;
                Vector3 toR = crouchStart - dogPos; toR.y = 0f;
                float pathR = toR.magnitude;
                if (availR <= 0.02f) { if (pathR < 1.2f) return true; _dropReason = "late"; _solved = false; return false; }   // round 3: say why; solve again this frame (Codex review)
                // IMP-DOG-017 harness round 1: pathR / availR blows up as availR -> 0, so a dog a stride short of the
                // crouch start with a tenth of a second left read as "needs 20 m/s" and was dropped (Lab lane: chase
                // vReq 3.58 and front vReq 4.8 plans fell to overtake / ground at tNow 0.73-0.85). Close in, or late,
                // the residual belongs to the BeginJump gate (1.2 m) and the air solve, not to the pace rule.
                if (pathR <= KeepCloseRun || availR <= KeepLateSeconds) { _jumpSpeed = vTake; return true; }
                // IMP-DOG-017: otherwise the ONLY reason to give a shape up is a pace the dog cannot run.
                float vCap = _mode == ModeChase ? chaseSprintSpeed : chaseRunSpeed;
                if (pathR / availR > vCap * KeepPaceSlack) return DropMode("pace");
                _jumpSpeed = vTake;
                return true;
            }
            Vector3 toHeld = _standRoot - dogPos; toHeld.y = 0f;
            float distHeld = toHeld.magnitude;
            bool close = _holding || distHeld < HoldExitRadius;
            if (_plan == PlanJump)
            {
                // A around. Close in, the dog is only shuffling and turning, so there is no pace to judge; further
                // out the required pace over the via point decides. Settling is part of A's budget (settleTime), but
                // once that is spent the dog may still make the crouch itself, so the crouch deadline is the fallback
                // budget rather than an instant drop.
                // Harness round 1: the same blow-up as B / C - late in the approach the pace rule is not asked at all;
                // the 0.5 m gate at the crouch decides (lob pick=around vReq 4.3 was dropped at 0.3-1.1 m out).
                float availC = _catchT - _crouchReal - _tUp - tNow;
                if (!close && availC > KeepLateSeconds)
                {
                    float availA = availC - settleTime;
                    // Harness round 2 (Lab lob v1-v4, rise 0.90): budget = availA while availA was a few hundredths
                    // above zero read 0.6-1.0 m / 0.05 s = 12-20 m/s and dropped the plan (drop=pace) with the dog
                    // arriving at 4 m/s and the crouch still 0.25 s away. Settling is a preference: once less than
                    // KeepLateSeconds of it is left, the pace is judged against the crouch deadline instead.
                    float budget = availA > KeepLateSeconds ? availA : availC;
                    if (budget <= 0.02f) return DropMode("pace");
                    // PCVR 2026-09-26 ("回り込んだ後に全然跳ばない"): late in the approach the path THROUGH the via
                    // point is metres longer than the straight line, and it dropped plans the dog could walk in to
                    // (Samoyed: 1.7 m straight with 0.5 s = 3.4 m/s, dropped for pace). If the straight line fits,
                    // finish the via here and run straight in; drop only when even the straight line cannot make it.
                    float capA = chaseRunSpeed * KeepPaceSlack;
                    if (_viaActive && !_viaDone && (distHeld <= ViaFinishDistance || ViaPath(dogPos) / budget > capA) && distHeld / budget <= capA) _viaDone = true;
                    if (ViaPath(dogPos) / budget > capA) return DropMode("pace");
                }
                if (!FineTune(dogPos)) return DropMode("ground");   // DES-DOG-005 B-2: the spot's ground left the band
                return true;
            }
            // PlanStandOrOvertake.
            if (_runningCatch)
            {
                // D' overtake: running it down, facing the way it runs. Just be there in time. Harness round 1: close
                // in or late, the catch window judges it (the same residual rule as B / C).
                if (distHeld <= KeepCloseRun || held <= KeepLateSeconds) return true;
                if (EtaCorrected(distHeld, Vector3.Angle(dogRoot.forward, toHeld), chaseRunSpeed) <= held + 0.05f) return true;
                _dropReason = "eta"; return false;
            }
            // D stand. A plan the dog is already standing on is never given up (the catch window judges it), and one
            // it can still physically run to before the window shuts is kept as well (round 5, PCVR 2026-09-22).
            // Harness round 1: "committed" is the whole close band (hold, or inside HoldExitRadius), and late in the
            // flight too - the old HoldEnterRadius-only rule let a stand plan the dog was arriving on be dropped.
            bool committed = close || held <= KeepLateSeconds;
            if (!committed && EtaCorrected(distHeld, close ? 0f : Vector3.Angle(dogRoot.forward, toHeld), chaseRunSpeed) > held + catchWindow) { _dropReason = "eta"; return false; }
            if (!FineTune(dogPos)) return DropMode("ground");   // DES-DOG-005 B-2
            return true;
        }

        // DES-DOG-005 B-2 (KeepPlan): T0 and L0 of a momentum jump whose crouch start slid. The plan's rise, tUp and catch
        // root are fixed; the take-off ground may change, so the rise it implies must stay in the band, and the landing is
        // re-solved from the vertical speed the liftoff will use (B-3). False = _jgWhy says why.
        bool ReshootJumpGround(Vector3 crouchStart, float vTake)
        {
            Vector3 t0 = crouchStart + _forward * (vTake * _crouchReal);
            if (!JumpGround(t0, _takeoffY)) return false;
            float takeoffY = _jgY;
            float rise = _catchRoot.y - takeoffY;
            if (rise < JumpFloor() - 0.05f || rise > maxRootRise + 0.05f) { _jgWhy = "rise"; return false; }
            float g = frisbee.gravity;
            float vy = rise / Mathf.Max(0.05f, _tUp) + 0.5f * g * _tUp;
            if (!AirLanding(t0, takeoffY, _forward, vTake, vy)) return false;
            // No lane ray here (design B-2: <= 4 rays at 10 Hz); the lane was judged at the commit, the landing ground
            // is judged again by the liftoff gate.
            _takeoffY = takeoffY; _landY = _alY; _landPt = _alPt; _tDown = _alTime - _tUp; _jumpRayAt = crouchStart;
            return true;
        }

        // Fine-tune while waiting on the spot (A and D): the catch time is fixed, the spot is re-derived from the
        // prediction (the ground under the disc may read differently from where the dog now stands). Only a real
        // difference moves the target: a 10 Hz rewrite of the same point is what reads as jitter.
        // DES-DOG-005 B-2: the one ray is shot at the stand spot S (not under the disc); catchRoot.y = takeoffY + rise.
        // False = the rise that ground implies left the band (or the spot failed the ground rules): the caller drops it.
        bool FineTune(Vector3 dogPos)
        {
            if (!_holding || _runningCatch) return true;
            bool stand = _plan == PlanStandOrOvertake;
            Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, _catchT);
            // S's xz does not depend on the ground height (CatchGeometry offsets it horizontally only).
            CatchGeometry(_catchT, _rootRise, _takeoffY, stand, dogPos);
            if (!JumpGround(_geoStandRoot, _takeoffY)) return false;
            float takeoffY = _jgY;
            if (!stand)
            {
                float implied = d.y - takeoffY - _mouthCatch.y;
                if (implied < JumpFloor() - 0.05f || implied > maxRootRise + 0.05f) { _jgWhy = "rise"; return false; }
            }
            else if (Mathf.Abs(takeoffY - _takeoffY) > 0.25f) { _jgWhy = "step"; return false; }
            CatchGeometry(_catchT, _rootRise, takeoffY, stand, dogPos);
            if (Vector3.Distance(_geoStandRoot, _standRoot) > 0.03f)
            {
                _catchRoot = _geoCatchRoot; _standRoot = _geoStandRoot; _forward = _geoForward;
                _takeoffY = takeoffY; _landY = takeoffY; _landPt = _geoStandRoot; _tDown = _tUp; _jumpRayAt = _geoStandRoot;
            }
            return true;
        }

        // Length of what is left of the approach: via point then spot, or straight to the spot. Pure (the latch on
        // the via point is Motion's).
        float ViaPath(Vector3 dogPos)
        {
            Vector3 toS = _standRoot - dogPos; toS.y = 0f;
            if (!_viaActive || _viaDone) return toS.magnitude;
            Vector3 toV = _viaPoint - dogPos; toV.y = 0f;
            Vector3 leg = _standRoot - _viaPoint; leg.y = 0f;
            return toV.magnitude + leg.magnitude;
        }

        // DES-DOG-005 C-1: the lane FOLLOWS THE SLOPE - from the ground at `from` + 0.3 m to the ground at `to` + 0.3 m
        // (both .y must be MEASURED ground heights: Solve shoots C0 / V first, Codex M1 1-2). On level ground this is the
        // old horizontal ray exactly. Inside a Solve past SolveRayCap it reports blocked with _solveCapped set.
        bool LaneClear(Vector3 from, Vector3 to)
        {
            Vector3 flat = to - from; flat.y = 0f;
            if (flat.magnitude < 0.05f) return true;
            if (_inSolve && _solveRays >= SolveRayCap) { _solveCapped = true; return false; }
            Vector3 d = to - from;
            float dist = d.magnitude;
            _planRays++; _solveRays++;
            RaycastHit hit;
            return !Physics.Raycast(from + Vector3.up * 0.3f, d / dist, out hit, dist, frisbee.obstacleMask, QueryTriggerInteraction.Ignore);
        }

        // The rise clamp SolveBand applies to a band sample (kept identical so a level-ground plan does not change).
        float ClampRise(float h)
        {
            float lo = JumpFloor(), hi = maxRootRise;
            float riseLo = lo + BandRiseInset, riseHi = hi - BandRiseInset;
            if (riseHi < riseLo) { riseLo = 0.5f * (lo + hi); riseHi = riseLo; }
            return Mathf.Clamp(h, riseLo, riseHi);
        }

        // B-2 step 2: is a rise measured from the real take-off ground still a jump for this dog (the band sieve's own
        // +-0.05 tolerance)?
        bool RiseInBand(float raw) { return raw >= JumpFloor() - 0.05f && raw <= maxRootRise + 0.05f; }

        // ---- IMP-DOG-017 Solve ----

        // Height of the disc above the ground under it, measured from the catch-pose mouth: the root rise a jump
        // at flight time t would need.
        float BandH(float t)
        {
            Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
            return d.y - GroundYAt(d) - _mouthCatch.y;
        }

        // Where BandH crosses `level` between a and b (ha = BandH(a) - level, known). Bisection to BandTolerance.
        float BandCross(float a, float ha, float b, float level)
        {
            for (int k = 0; k < 10 && b - a > BandTolerance; k++)
            {
                float m = 0.5f * (a + b);
                float hm = BandH(m) - level;
                if ((hm >= 0f) == (ha >= 0f)) { a = m; ha = hm; } else b = m;
            }
            return 0.5f * (a + b);
        }

        // Step 1: the time intervals in which the disc is inside [lo, hi] (the dog's jump band), from now until it
        // lands. Coarse steps of BandScanStep, edges by bisection. Also owns the landing point (the ground plan).
        // Spec: "the first interval only". A lob released inside a tall dog's band (Lab, release ~1.4 m) is in band
        // for a moment at the HAND - unreachable - then climbs out and comes back down through it, so the first
        // interval alone would throw away the descent. Up to two are kept; both are sampled.
        // DES-DOG-005 B-2 step 1: lo / hi are the sieve. Solve scans with the dog's own band first; if the ground under the
        // flight is not level (_bandUneven: more than 0.05 m of spread, or no ground somewhere) it scans again with the band
        // widened by jumpMaxRise, because the rise is measured from the take-off ground, not from under the disc.
        void FindBand(float tNow, float lo, float hi)
        {
            _bandIn = -1f; _bandOut = -1f; _bandIn2 = -1f; _bandOut2 = -1f; _bandUneven = false;
            _landingValid = false;   // B-5: a scan that never finds ground to land on leaves no stale landing behind
            int found = 0;
            bool inside = false;
            float prevT = tNow, prevH = 0f, curIn = -1f;
            float gyMin = 1e9f, gyMax = -1e9f; bool gap = false;
            for (int i = 0; i < BandScanMaxSteps; i++)
            {
                _planEvaluations++;   // IMP-DOG-012 cost counter (read by the harness only)
                float t = tNow + i * BandScanStep;
                Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
                float gy = GroundYAt(d);
                if (gy == NoGround)
                {
                    // B-5: nothing under this instant (over a cliff): never in the band, never a landing.
                    gap = true; _bandUneven = true;
                    if (inside && found < 2) { found = StoreBand(found, curIn, prevT); inside = false; }
                    prevT = t; prevH = hi + 1000f;
                    continue;
                }
                if (gy < gyMin) gyMin = gy;
                if (gy > gyMax) gyMax = gy;
                _bandUneven = gap || gyMax - gyMin > 0.05f;
                bool landed = d.y - gy <= frisbee.radius;
                float h = d.y - gy - _mouthCatch.y;
                bool inNow = h >= lo && h <= hi && !landed;
                if (found < 2)
                {
                    if (i == 0) { if (inNow) { curIn = t; inside = true; } }
                    else if (!inside)
                    {
                        if (inNow) { curIn = BandCross(prevT, prevH - (prevH > hi ? hi : lo), t, prevH > hi ? hi : lo); inside = true; }
                        else if (!landed && ((prevH > hi && h < lo) || (prevH < lo && h > hi)))
                        {
                            // The whole band was crossed inside one coarse step (the 50 ms bands of a flat throw).
                            float first = prevH > hi ? hi : lo, second = prevH > hi ? lo : hi;
                            float a = BandCross(prevT, prevH - first, t, first);
                            float b = BandCross(prevT, prevH - second, t, second);
                            if (b < a) { float s = a; a = b; b = s; }
                            found = StoreBand(found, a, b);
                        }
                    }
                    else if (!inNow)
                    {
                        float level = h > hi ? hi : lo;
                        found = StoreBand(found, curIn, BandCross(prevT, prevH - level, t, level));
                        inside = false;
                    }
                }
                if (landed)
                {
                    _landing = new Vector3(d.x, gy, d.z); _landingValid = true;
                    if (inside && found < 2) { found = StoreBand(found, curIn, prevT); inside = false; }
                    return;
                }
                prevT = t; prevH = h;
            }
            if (inside && found < 2) StoreBand(found, curIn, prevT);
        }

        int StoreBand(int found, float a, float b)
        {
            if (found == 0) { _bandIn = a; _bandOut = b; }
            else { _bandIn2 = a; _bandOut2 = b; }
            return found + 1;
        }

        // Steps 2-4: sample the band, evaluate A / B / C at every instant, keep the best score. Returns PlanJump with
        // the plan committed, or PlanNone.
        // DES-DOG-005 C-2: every Solve counts (_solveRuns) and its placement rays are capped at SolveRayCap (the best
        // candidate found by then wins). B-2 step 1: a second, widened scan only when the ground under the flight is uneven.
        int Solve(float tNow, Vector3 dogPos)
        {
            float lo = JumpFloor(), hi = maxRootRise;
            _solveCount = 0; _cScore = -1f; _whyA = "-"; _whyB = "-"; _whyC = "-";
            _solveRuns++; _solveRays = 0; _solveCapped = false; _solveLaneRefused = 0; _inSolve = true;
            FindBand(tNow, lo, hi);
            if (hi <= lo) { EndSolve(); return PlanNone; }
            // The dog's own band first: on level ground this is the whole solve, as before.
            if (_bandIn >= 0f) SolveBand(tNow, dogPos, _bandIn, _bandOut, lo, hi);
            if (_bandIn2 >= 0f && _solveRays < SolveRayCap) SolveBand(tNow, dogPos, _bandIn2, _bandOut2, lo, hi);
            // B-2 step 1 (Codex M1 1-1): the band widened by jumpMaxRise, because the rise is measured from the take-off
            // ground, which may differ from the ground under the disc. Scanned when the ground under the flight is uneven,
            // and ALSO whenever the dog's band gave nothing. Its candidates only replace the first pass's on a better score.
            if ((_bandUneven || _cScore < 0f) && _solveRays < SolveRayCap)
            {
                float slo = lo - jumpMaxRise, shi = hi + jumpMaxRise;
                bool keepUneven = _bandUneven;
                FindBand(tNow, slo, shi);
                _bandUneven = _bandUneven || keepUneven;
                if (_bandIn >= 0f) SolveBand(tNow, dogPos, _bandIn, _bandOut, slo, shi);
                if (_bandIn2 >= 0f && _solveRays < SolveRayCap) SolveBand(tNow, dogPos, _bandIn2, _bandOut2, slo, shi);
            }
            EndSolve();
            if (_cScore < 0f) return PlanNone;
            CommitCandidate();
            return PlanJump;
        }

        void EndSolve()
        {
            _inSolve = false;
            if (_solveRays > _solveRayMax) _solveRayMax = _solveRays;
            // One line per Solve (never per frame): what the placement rays cost and whether the cap cut it short.
            Debug.Log("[KH_FrisbeeCatch] solve rays=" + _solveRays + " cap=" + (_solveCapped ? 1 : 0) + " runs=" + _solveRuns
                + " uneven=" + (_bandUneven ? 1 : 0) + " laneRefused=" + _solveLaneRefused + DogTag());
        }

        // lo / hi: the sieve the band was found with (the dog's own band, or widened by jumpMaxRise on uneven ground). The
        // rise a candidate is judged on is always the dog's own band (ClampRise / RiseInBand).
        void SolveBand(float tNow, Vector3 dogPos, float tIn, float tOut, float lo, float hi)
        {
            float g = frisbee.gravity;
            if (tOut < tIn) return;
            // Harness round 2: never pick ON the band edge. The edge is where the bisection tolerance and the clamp
            // meet, and a candidate there sits at exactly maxRootRise (or the floor). Inset both ends; a band
            // narrower than twice the inset is sampled once at its middle.
            // PCVR 2026-09-26: a small dog's band (<= 2 x BandInset wide) was sampled once at its MIDDLE, so every
            // Pome / Corgi / Dachs jump came out at the same mid-band rise (0.32). Now a narrow band is sampled at
            // three instants just inside the bisection tolerance and the rise clamp keeps each off the edge; the
            // score (rise x RiseScoreWeight) then takes the highest one.
            bool narrow = tOut - tIn <= 2f * BandInset;
            if (!narrow) { tIn += BandInset; tOut -= BandInset; }
            else if (tOut - tIn > 2f * BandTolerance) { tIn += BandTolerance; tOut -= BandTolerance; }
            float span = tOut - tIn;
            float bandLo = JumpFloor(), bandHi = maxRootRise;
            float step = SolveStep;
            if (span / step > SolveMaxSamples - 1) step = span / (SolveMaxSamples - 1);
            int n = (int)(span / step) + 1;
            if (n > SolveMaxSamples) n = SolveMaxSamples;
            if (narrow && span > 0.001f) { n = 3; step = span / 2f; }
            float vTakeB = Mathf.Clamp(chaseSprintSpeed * ChaseTakeoffFraction, RunJumpMinTakeoff, RunJumpMaxTakeoff);
            for (int i = 0; i < n; i++)
            {
                if (_solveRays >= SolveRayCap) { _solveCapped = true; return; }   // C-2: the best so far wins
                _planEvaluations++;   // IMP-DOG-012 cost counter (read by the harness only)
                float t = n == 1 ? tIn + 0.5f * span : tIn + i * step;
                Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
                float groundY = GroundYAt(d);
                if (groundY == NoGround) continue;   // B-5: nothing under this instant
                float h = d.y - groundY - _mouthCatch.y;
                // The bisected edges sit within a few millimetres of the band; anything further out is not a jump.
                if (h < lo - 0.05f || h > hi + 0.05f) continue;
                float rise = ClampRise(h);
                float tUp = Mathf.Sqrt(2f * rise / g);
                float scale = _riseDur / tUp;
                float crouch = _crouchDur / scale;
                float avail = t - tNow - crouch - tUp;
                if (avail <= 0.05f) { _whyA = "time"; _whyB = "time"; _whyC = "time"; continue; }
                Vector3 dv = frisbee._VelocityAt(_v0, _curve, t); dv.y = 0f;
                if (dv.sqrMagnitude < 1e-4f) continue;
                Vector3 flight = dv.normalized;
                // Higher in the band is better: the jump reads bigger (user 2026-09-26 "大げさでも跳んでほしい").
                float riseTerm = (rise - bandLo) / (bandHi - bandLo) * RiseScoreWeight;   // PCVR 2026-09-26: 3 -> 6
                if ((_bannedModes & (1 << ModeAround)) == 0) EvalAround(t, tNow, d, rise, tUp, scale, crouch, avail, groundY, dogPos, riseTerm); else _whyA = "banned";
                EvalRun(t, tNow, d, flight, rise, tUp, scale, crouch, avail, groundY, dogPos, riseTerm, vTakeB);
            }
        }

        // Preference per shape. Normal: A (around) is the default, B / C when there is no time to go round.
        // v3 (並走) is defined by the chase from behind, so B leads there.
        float Pref(int mode)
        {
            if (mode == ModeAround) return _overtake ? 2f : 3f;
            if (mode == ModeChase) return _overtake ? 3f : 2f;
            return 1f;
        }

        // A around: stand mouth-distance past the catch point, facing the thrower, and go straight up. The dog
        // runs out past the spot on its own side through one via point and comes back onto it along the catch
        // facing, so it arrives already turned. Spare time is spent by running slower, never refused.
        // DES-DOG-005 B: T0 = S, and S's xz does not depend on the rise, so A pays ONE ground ray (T0 = L0 = S).
        void EvalAround(float t, float tNow, Vector3 d, float rise, float tUp, float scale, float crouch, float avail, float groundY, Vector3 dogPos, float riseTerm)
        {
            float availA = avail - settleTime;
            if (availA <= 0.05f) { _whyA = "time"; return; }
            CatchGeometry(t, rise, groundY, false, dogPos);
            Vector3 S = _geoStandRoot, F = _geoForward;
            Vector3 flight = -F;
            Vector3 to = S - dogPos; to.y = 0f;
            float dS = to.magnitude;
            bool via = false;
            Vector3 V = S;
            // The via point only earns its detour when the dog would otherwise arrive facing the wrong way. From
            // downfield (already coming at the spot along F) or right on top of it, straight is the answer.
            // DES-DOG-005: the via point is no longer tested against the plaza circle (its lanes are, below).
            if (dS > 0.5f && Vector3.Angle(to, F) > ViaStraightAngle)
            {
                _planHookTries++;     // IMP-DOG-012 cost counter (now: A via points sized)
                Vector3 right = Vector3.Cross(Vector3.up, flight);
                Vector3 rel = dogPos - S; rel.y = 0f;
                float side = Vector3.Dot(rel, right) < 0f ? -1f : 1f;
                V = S + flight * ViaAhead + right * (side * ViaSide);
                via = true;
            }
            float path;
            Vector3 firstLeg;
            if (via)
            {
                Vector3 toV = V - dogPos; toV.y = 0f;
                Vector3 leg = S - V; leg.y = 0f;
                path = toV.magnitude + leg.magnitude;
                firstLeg = toV;
            }
            else { path = dS; firstLeg = to; }
            float vReq = path / availA;
            float eta = EtaCorrected(path, firstLeg.magnitude > 0.05f ? Vector3.Angle(dogRoot.forward, firstLeg) : 0f, chaseRunSpeed);
            // Round 7 (Pome v5 flat: 1.4 m from the spot, 1.6 s to spare, solve=0): the via detour for a dog standing
            // just behind the spot is ~5 m, and with the launch it no longer fits the budget - and there was no
            // straight alternative, so a dog next to the spot could not take A at all. When the via version fails,
            // try the straight run-in, charged like the old wait approach: the run and the about-face overlap (the
            // heading blends onto F across the run-in), so the cost is whichever of the two takes longer.
            if (via && (vReq > chaseRunSpeed || eta + settleTime > avail))
            {
                float etaS = Mathf.Max(EtaCorrected(dS, dS > 0.05f ? Vector3.Angle(dogRoot.forward, to) : 0f, chaseRunSpeed),
                    Vector3.Angle(to, F) / Mathf.Max(1f, turnSpeedDegPerSec));
                if (dS / availA <= chaseRunSpeed && etaS + settleTime <= avail)
                { via = false; V = S; path = dS; firstLeg = to; vReq = dS / availA; eta = etaS; }
            }
            if (vReq > chaseRunSpeed) { _whyA = "speed"; return; }
            if (eta + settleTime > avail) { _whyA = "eta"; return; }
            _whyA = "ok";
            _solveCount++;
            float score = Pref(ModeAround) * 10f + riseTerm + Mathf.Clamp01(1f - vReq / chaseRunSpeed) * SpeedScoreWeight;
            if (score <= _cScore) return;     // the raycasts below are only paid for a candidate that would win
            if (_solveRays >= SolveRayCap) { _solveCapped = true; _whyA = "cap"; return; }
            // DES-DOG-005 B-2: the take-off ground is the ground at S, and the rise is measured from it.
            if (!JumpGround(S, groundY)) { _whyA = _jgWhy; return; }
            float takeoffY = _jgY;
            float raw = d.y - takeoffY - _mouthCatch.y;
            if (!RiseInBand(raw)) { _whyA = "rise"; return; }
            float rise1 = ClampRise(raw);
            if (Mathf.Abs(rise1 - rise) > 1e-4f)
            {
                // The real ground moved the rise: the timing follows it, and the pace / ETA are judged again.
                float g = frisbee.gravity;
                tUp = Mathf.Sqrt(2f * rise1 / g); scale = _riseDur / tUp; crouch = _crouchDur / scale;
                avail = t - tNow - crouch - tUp; availA = avail - settleTime;
                if (availA <= 0.05f) { _whyA = "time"; return; }
                vReq = path / availA;
                if (vReq > chaseRunSpeed) { _whyA = "speed"; return; }
                if (eta + settleTime > avail) { _whyA = "eta"; return; }
                rise = rise1;
            }
            CatchGeometry(t, rise, takeoffY, false, dogPos);   // same xz, the heights from the take-off ground
            S = _geoStandRoot;
            if (via)
            {
                // Codex M1 1-2: the via point's own ground, measured, before its lanes.
                float vy0 = GroundNear(V, takeoffY);
                if (vy0 == NoGround) { _whyA = _solveCapped ? "cap" : "noground"; return; }
                V.y = vy0;
            }
            else V = S;
            if (!LaneClear(dogPos, V)) { LaneFailA(); return; }
            if (via && !LaneClear(V, S)) { LaneFailA(); return; }
            // Codex D1: straight up (vTake 0), so the landing is the spot itself - but the recover stride still
            // has to be clear.
            if (!LaneClear(S, S + F * LandingLaneMargin)) { if (_solveCapped) _whyA = "cap"; else { _whyA = "landing"; _solveLaneRefused++; } return; }
            _cScore = score; _cMode = ModeAround; _cT = t; _cRise = rise; _cTUp = tUp; _cScale = scale; _cCrouch = crouch;
            _cCatchRoot = _geoCatchRoot; _cDrive = S; _cFwd = F; _cJumpSpeed = 0f;
            _cVia = V; _cViaActive = via; _cVReq = vReq; _cVCap = chaseRunSpeed; _cEta = eta; _cPath = path;
            _cSlack = avail - (eta + settleTime);
            _cTakeMin = RunJumpMinTakeoff; _cTakeCap = RunJumpMaxTakeoff;
            _cTakeoffY = takeoffY; _cLandY = takeoffY; _cTDown = tUp; _cLand = S;
            Vector3 entry = S - V; entry.y = 0f;
            if (!via) entry = to;
            _cEntry = entry.sqrMagnitude > 1e-4f ? entry.normalized : F;
        }

        // B chase / C front: running jumps. Which one is decided by where the dog is relative to the catch point:
        // behind it (the disc is flying away from the dog) = B, running WITH the disc at up to chaseSprintSpeed;
        // in front or beside = C, running at the disc (the old running jump, head-on take-off capped).
        void EvalRun(float t, float tNow, Vector3 d, Vector3 flight, float rise, float tUp, float scale, float crouch, float avail, float groundY, Vector3 dogPos, float riseTerm, float vTakeB)
        {
            Vector3 run = d - dogPos; run.y = 0f;
            float dd = run.magnitude;
            bool behind = dd > 0.01f && Vector3.Dot(run, flight) >= 0.5f * dd;
            int mode = behind ? ModeChase : ModeFront;
            if (behind) _whyC = "side"; else _whyB = "side";   // round 7 why=: this instant belongs to the other shape
            if ((_bannedModes & (1 << mode)) != 0) { WhyRun(behind, "banned"); return; }
            Vector3 fwd;
            float cap, vTake, vCap, lo = RunJumpMinTakeoff;
            if (behind)
            {
                fwd = flight; cap = RunJumpMaxTakeoff; vTake = vTakeB; vCap = chaseSprintSpeed;
            }
            else
            {
                if (dd < 1f) { WhyRun(behind, "close"); return; }                                  // on top of it already: there is no run-up
                fwd = run / dd;                                        // it takes it facing the way it runs
                // Running INTO the disc adds both speeds to the closing rate, so the take-off is capped there.
                cap = Vector3.Dot(fwd, flight) < 0f ? RunJumpHeadOnMaxTakeoff : RunJumpMaxTakeoff;
                vTake = Mathf.Clamp(_speed, RunJumpMinTakeoff, cap);
                vCap = chaseRunSpeed;
            }
            if (vCap < 0.1f) { WhyRun(behind, "speed"); return; }
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 catchRoot = new Vector3(d.x, groundY + rise, d.z) - fwd * _mouthCatch.z - right * _mouthCatch.x;
            Vector3 standRoot = new Vector3(catchRoot.x, groundY, catchRoot.z);
            if (!behind)
            {
                // Run-up length = take-off speed x (crouch + rise). If the dog is already inside it, SHORTEN the
                // run-up by taking off slower rather than putting the crouch start behind the dog.
                Vector3 toStand = standRoot - dogPos; toStand.y = 0f;
                if (toStand.magnitude < vTake * (tUp + crouch))
                { vTake = Mathf.Clamp(toStand.magnitude / (tUp + crouch), RunJumpShortTakeoff, cap); lo = vTake; }
            }
            Vector3 crouchStart = standRoot - fwd * (vTake * (tUp + crouch));
            Vector3 to = crouchStart - dogPos; to.y = 0f;
            // B: the crouch start must be ahead of the dog - it catches up, it does not turn round for it.
            if (behind && Vector3.Dot(to, fwd) < 0f) { WhyRun(behind, "close"); return; }
            // DES-DOG-005: no plaza-circle test for the stand / crouch start any more; the ground rays below decide.
            float dist = to.magnitude;
            float vReq = dist / avail;
            if (vReq > vCap) { WhyRun(behind, "speed"); return; }
            float eta = EtaCorrected(dist, dist > 0.05f ? Vector3.Angle(dogRoot.forward, to) : 0f, vCap);
            if (avail - eta < SolveMinSlack) { WhyRun(behind, "eta"); return; }   // round 5: no zero-margin running jumps
            WhyRun(behind, "ok");
            _solveCount++;
            float score = Pref(mode) * 10f + riseTerm + Mathf.Clamp01(1f - vReq / vCap) * SpeedScoreWeight;
            if (score <= _cScore) return;
            if (_solveRays >= SolveRayCap) { _solveCapped = true; WhyRun(behind, "cap"); return; }
            // DES-DOG-005 B-2 step 2: the real take-off ground T0 (two rays when the rise it implies moves the crouch).
            // Codex M1 1-2: the lanes come after, from the MEASURED crouch-start ground.
            Vector3 t0 = crouchStart + fwd * (vTake * crouch);
            if (!JumpGround(t0, groundY)) { WhyRun(behind, _jgWhy); return; }
            float takeoffY = _jgY;
            float raw = d.y - takeoffY - _mouthCatch.y;
            if (!RiseInBand(raw)) { WhyRun(behind, "rise"); return; }
            float rise1 = ClampRise(raw);
            if (Mathf.Abs(rise1 - rise) > 1e-4f)
            {
                float g = frisbee.gravity;
                tUp = Mathf.Sqrt(2f * rise1 / g); scale = _riseDur / tUp; crouch = _crouchDur / scale;
                avail = t - tNow - crouch - tUp;
                if (avail <= 0.05f) { WhyRun(behind, "time"); return; }
                catchRoot = new Vector3(d.x, takeoffY + rise1, d.z) - fwd * _mouthCatch.z - right * _mouthCatch.x;
                standRoot = new Vector3(catchRoot.x, takeoffY, catchRoot.z);
                crouchStart = standRoot - fwd * (vTake * (tUp + crouch));
                to = crouchStart - dogPos; to.y = 0f;
                if (behind && Vector3.Dot(to, fwd) < 0f) { WhyRun(behind, "close"); return; }
                dist = to.magnitude; vReq = dist / avail;
                if (vReq > vCap) { WhyRun(behind, "speed"); return; }
                eta = EtaCorrected(dist, dist > 0.05f ? Vector3.Angle(dogRoot.forward, to) : 0f, vCap);
                if (avail - eta < SolveMinSlack) { WhyRun(behind, "eta"); return; }
                t0 = crouchStart + fwd * (vTake * crouch);
                if (!JumpGround(t0, takeoffY)) { WhyRun(behind, _jgWhy); return; }
                if (Mathf.Abs(_jgY - takeoffY) > JumpIterTolerance) { WhyRun(behind, "iter"); return; }
                rise = rise1;
            }
            else
            {
                catchRoot = new Vector3(d.x, takeoffY + rise, d.z) - fwd * _mouthCatch.z - right * _mouthCatch.x;
                standRoot.y = takeoffY;
            }
            float c0y = GroundNear(crouchStart, takeoffY);
            if (c0y == NoGround) { WhyRun(behind, _solveCapped ? "cap" : "noground"); return; }
            crouchStart.y = c0y;
            if (!LaneClear(dogPos, crouchStart)) { LaneFailRun(behind); return; }
            // B-2 step 3: the landing from the take-off, two rays (tDown from the landing ground).
            float vy = frisbee.gravity * tUp;
            // why= says which landing rule refused it (land-rise / land-iter / land-apex / land-slope ...; "landing" = its lane).
            if (!AirLanding(t0, takeoffY, fwd, vTake, vy)) { WhyRun(behind, "land-" + _jgWhy); return; }
            if (!LaneClear(crouchStart, _alPt + fwd * LandingLaneMargin)) { if (_solveCapped) WhyRun(behind, "cap"); else { WhyRun(behind, "landing"); _solveLaneRefused++; } return; }   // Codex D1 (a blocked landing lane counts in laneRefused=)
            _cScore = score; _cMode = mode; _cT = t; _cRise = rise; _cTUp = tUp; _cScale = scale; _cCrouch = crouch;
            _cCatchRoot = catchRoot; _cDrive = crouchStart; _cFwd = fwd; _cJumpSpeed = vTake;
            _cVia = crouchStart; _cViaActive = false; _cVReq = vReq; _cVCap = vCap; _cEta = eta; _cPath = dist;
            _cSlack = avail - eta;
            // The band the 10 Hz re-derive must stay inside: without it a head-on run speeds up past its cap, and
            // a shortened run-up grows back out behind the dog.
            _cTakeMin = lo; _cTakeCap = lo > cap ? lo : cap;
            _cEntry = fwd;
            _cTakeoffY = takeoffY; _cLandY = _alY; _cTDown = _alTime - tUp; _cLand = _alPt;
        }
        // Round 7 diagnostics: the last reason B / C failed at (plan= why=). Log only.
        void WhyRun(bool behind, string r) { if (behind) _whyB = r; else _whyC = r; }
        // A lane that failed: blocked (counted for laneRefused=) or refused by the ray cap.
        void LaneFailA() { if (_solveCapped) _whyA = "cap"; else { _whyA = "lane"; _solveLaneRefused++; } }
        void LaneFailRun(bool behind) { if (_solveCapped) WhyRun(behind, "cap"); else { WhyRun(behind, "lane"); _solveLaneRefused++; } }


        // Take the best candidate (_c* scratch) as the plan.
        void CommitCandidate()
        {
            _catchT = _cT; _catchRoot = _cCatchRoot; _standRoot = _cDrive; _forward = _cFwd;
            _rootRise = _cRise; _tUp = _cTUp; _animScale = _cScale; _crouchReal = _cCrouch; _jumpSpeed = _cJumpSpeed;
            _planSlack = _cSlack; _planPath = _cPath; _planEta = _cEta; _catchDecided = false; _runningCatch = false;
            _mode = _cMode; _viaActive = _cViaActive; _viaPoint = _cVia; _viaDone = !_cViaActive;
            _takeoffMin = _cTakeMin; _takeoffCap = _cTakeCap;
            _entryDir = _cEntry; _pickVReq = _cVReq; _pickVCap = _cVCap;
            _takeoffY = _cTakeoffY; _landY = _cLandY; _tDown = _cTDown; _landPt = _cLand; _jumpRayAt = _cDrive;   // DES-DOG-005
        }

        // No-jump plans (D / D'): the take-off and landing ground are the spot's own.
        void CommitGroundSpot(Vector3 spot)
        {
            _takeoffY = spot.y; _landY = spot.y; _tDown = 0f; _landPt = spot; _jumpRayAt = spot;
        }

        // ---- D: stand under it (no jump reachable). Straight in at up to the run speed, settle, face the disc. ----
        int ScanStand(float tNow, Vector3 dogPos)
        {
            float floor = JumpFloor();
            int refused = 0;
            for (int i = 0; i < BandScanMaxSteps; i++)
            {
                _planEvaluations++;   // IMP-DOG-012 cost counter (read by the harness only)
                float t = tNow + 0.2f + i * BandScanStep;
                Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
                float groundY = GroundYAt(d);
                if (groundY == NoGround) continue;                        // B-5
                float h = d.y - groundY;
                if (h <= frisbee.radius) break;
                if (h - _mouthCatch.y >= floor) continue;                 // a jump height: Solve had its turn
                if (h < mouthOffsetStand.y - tooLowBelowMouth) continue;  // below the standing mouth: wait for the ground
                CatchGeometry(t, 0f, groundY, true, dogPos);
                Vector3 to = _geoStandRoot - dogPos; to.y = 0f;
                float dist = to.magnitude;
                float avail = t - tNow;
                float availS = avail - settleTime;
                // IMP-DOG-017: the same physical test as the jumps - a pace the dog can run and an ETA that fits.
                float vReq = availS > 0.02f ? dist / availS : (dist <= HoldEnterRadius ? 0f : 999f);
                if (vReq > chaseRunSpeed) continue;
                float eta = EtaCorrected(dist, dist > 0.05f ? Vector3.Angle(dogRoot.forward, to) : 0f, chaseRunSpeed);
                if (eta + settleTime > avail) continue;
                if (!LaneClear(dogPos, _geoStandRoot)) continue;
                // DES-DOG-005 B-1: the stand spot follows the jump's slope / bridge / river rule (and the old step rule).
                if (!StandGround(_geoStandRoot, groundY)) { refused++; if (refused >= ScanGroundRefusals) break; continue; }
                _catchT = t; _catchRoot = _geoCatchRoot; _standRoot = _geoStandRoot; _forward = _geoForward;
                _rootRise = 0f; _tUp = 0f; _animScale = 1f; _crouchReal = 0f; _jumpSpeed = 0f;
                _planSlack = avail - (eta + settleTime); _planPath = dist; _planEta = eta; _catchDecided = false; _runningCatch = false;
                _mode = ModeStand; _viaActive = false; _viaDone = true; _viaPoint = _geoStandRoot;
                _takeoffMin = RunJumpMinTakeoff; _takeoffCap = RunJumpMaxTakeoff;
                _entryDir = dist > 0.05f ? to / dist : _geoForward;
                _pickVReq = vReq; _pickVCap = chaseRunSpeed;
                CommitGroundSpot(new Vector3(_geoStandRoot.x, _jgY, _geoStandRoot.z));
                return PlanStandOrOvertake;
            }
            return PlanNone;
        }

        // ---- D': catch it on the run, from behind, without ever stopping (no jump and no stand catch reachable). ----
        int ScanOvertake(float tNow, Vector3 dogPos)
        {
            int refused = 0;
            for (int i = 0; i < BandScanMaxSteps; i++)
            {
                _planEvaluations++;   // IMP-DOG-012 cost counter (read by the harness only)
                float t = tNow + 0.2f + i * BandScanStep;
                Vector3 d = frisbee._PositionAt(_p0, _v0, _curve, t);
                float groundY = GroundYAt(d);
                if (groundY == NoGround) continue;                       // B-5
                float h = d.y - groundY;
                if (h <= frisbee.radius) break;
                if (h - _mouthCatch.y >= minRootRise) continue;          // high enough to be worth a jump: not this
                if (h < mouthOffsetStand.y - tooLowBelowMouth) continue; // below the running mouth: wait for the ground
                Vector3 dv = frisbee._VelocityAt(_v0, _curve, t); dv.y = 0f;
                if (dv.sqrMagnitude < 1e-4f) continue;
                Vector3 fwd = dv.normalized;                             // running WITH the disc, not into it
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 root = new Vector3(d.x, groundY, d.z) - fwd * mouthOffsetStand.z - right * mouthOffsetStand.x;
                Vector3 to = root - dogPos; to.y = 0f;
                float dist = to.magnitude;
                if (dist < 0.5f) continue;                               // already on it: there is nothing to overtake
                Vector3 approach = to / dist;
                if (Vector3.Dot(approach, fwd) < OvertakeMinDot) continue; // crossing or meeting it, not chasing it down
                float eta = EtaCorrected(dist, Vector3.Angle(dogRoot.forward, to), chaseRunSpeed);
                float slack = (t - tNow) - eta;                          // no settle, no crouch: just be there, moving
                if (slack < 0f) continue;
                if (dist / (t - tNow) < OvertakeMinPace) continue;       // a trot-in is not an overtake
                if (!LaneClear(dogPos, root)) continue;
                if (!StandGround(root, groundY)) { refused++; if (refused >= ScanGroundRefusals) break; continue; }   // DES-DOG-005 B-1
                _catchT = t; _catchRoot = root; _standRoot = root; _forward = fwd; _entryDir = approach;
                _rootRise = 0f; _tUp = 0f; _animScale = 1f; _crouchReal = 0f; _jumpSpeed = 0f;
                _planSlack = slack; _planPath = dist; _planEta = eta; _catchDecided = false; _runningCatch = true;
                _mode = ModeOvertake; _viaActive = false; _viaDone = true; _viaPoint = root;
                _takeoffMin = RunJumpMinTakeoff; _takeoffCap = RunJumpMaxTakeoff;
                _pickVReq = dist / (t - tNow); _pickVCap = chaseRunSpeed;
                CommitGroundSpot(new Vector3(root.x, _jgY, root.z));
                return PlanStandOrOvertake;
            }
            return PlanNone;
        }
    }
}
