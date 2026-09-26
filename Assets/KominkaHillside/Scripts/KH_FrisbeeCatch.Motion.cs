using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    // IMP-DOG-012 partial (Motion): what the Owner does with a plan - Ready, Chase, the approach steps, the jump,
    // landing, ground pickup, return, locomotion and the carried-disc pose. Moved verbatim from KH_FrisbeeCatch.cs.
    public partial class KH_FrisbeeCatch
    {
        // ---- Ready: the disc is in someone's hand. Take a stand-off spot in front of them and watch. ----
        // Display needs nothing synced beyond the state: every client turns the dog by the same synced facts
        // (VRCObjectSync carries position; the facing is written by the Owner).
        void UpdateReady()
        {
            if (!_dogOwner) return;
            VRCPlayerApi holder = HolderNow();
            if (holder == null) { TransitionTo(StateIdle); return; } // dropped without a throw, or thrown: ReqCatch overrides
            Vector3 holderPos = holder.GetPosition();
            if (!CanPlay(holderPos)) { TransitionTo(StateIdle); return; } // Ready never takes the dog from anyone (review M1)
            if (!_graceEnded && ReadyGrace(holderPos)) return;
            if (!_standoffValid || Time.time >= _nextStandoff)
            {
                // Target refreshed at 10 Hz (review D5); the walk toward it is still per frame.
                _nextStandoff = Time.time + 0.1f;
                VRCPlayerApi.TrackingData head = holder.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                Vector3 fwd = head.rotation * Vector3.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.0001f) { fwd = holder.GetRotation() * Vector3.forward; fwd.y = 0f; }
                if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
                fwd = fwd.normalized;
                bool first = !_standoffValid;
                // Which side to wait on is latched once per Ready entry (IMP-DOG-008): the side the dog is already
                // on, so a run-in preset never sends it across the throwing line and never flips left/right.
                if (first)
                {
                    Vector3 rel = dogRoot.position - holderPos; rel.y = 0f;
                    _readySide = Vector3.Dot(rel, Vector3.Cross(Vector3.up, fwd)) < 0f ? -1f : 1f;
                }
                if (_runIn)
                {
                    // A spot metres to the side swings just as far when the holder glances at the dog, so the
                    // hysteresis is on the cause, not on the spot: move only for a real turn or a real walk
                    // (Codex review). Variant 0 keeps the distance-based slack below.
                    Vector3 moved = holderPos - _standoffHolderPos; moved.y = 0f;
                    if (first || Vector3.Angle(fwd, _standoffFwd) > RunInReadyTurnDeg || moved.magnitude > _readySlack)
                    { _standoffSpot = ReadySpot(holderPos, fwd); _standoffFwd = fwd; _standoffHolderPos = holderPos; _standoffValid = true; }
                }
                else
                {
                    Vector3 spot = ReadySpot(holderPos, fwd);
                    if (first || Vector3.Distance(spot, _standoffSpot) > _readySlack)
                    { _standoffSpot = spot; _standoffFwd = fwd; _standoffHolderPos = holderPos; _standoffValid = true; }
                }
            }
            Vector3 to = _standoffSpot - dogRoot.position; to.y = 0f;
            float dist = to.magnitude;
            if (dist > arriveRadius)
            {
                MoveToward(_standoffSpot, dist > returnRunDistance ? runSpeed : walkSpeed, 0f);
                return;
            }
            Coast();
            // "Beside" (v3) waits looking the way the throw will go, so the disc comes past from behind; the rest
            // watch the holder as before.
            if (_readyFaceForward) FaceToward(_standoffFwd);
            else FaceToward(holderPos - dogRoot.position);
        }

        // IMP-DOG-013: the よしよし window at the head of Ready. Until now the disc leaving the mouth put the dog
        // straight into Ready, and Ready's first act is an 8 m sprint - the dog you had just caught bolted the
        // instant you took the disc, which is the whole of the user's complaint. So for the first few seconds,
        // while the holder is still within arm's reach, the dog simply stays and looks at them; and it will not
        // leave AT ALL while hands are on it, however long the grace has run. True = the grace is holding the dog
        // this frame (the caller returns and never reaches the stand-off run).
        //
        // Everything else about Ready is untouched: the presets, the geometry, the slack and IsEngaged are the
        // same once the grace is over, and a throw during the window still works, because ReqCatch overrides
        // Ready from Update and never consults this.
        bool ReadyGrace(Vector3 holderPos)
        {
            Vector3 toHolder = holderPos - dogRoot.position; toHolder.y = 0f;
            float holderDist = toHolder.magnitude;
            bool petting = BeingPetted();
            if (!_graceLogged)
            {
                _graceLogged = true;
                Debug.Log("[KH_FrisbeeCatch] ready grace: holder=" + holderDist.ToString("F1") + " m petting=" + (petting ? "true" : "false"));
            }
            bool near = holderDist < readyGraceDistance;
            bool within = _heldSince >= 0f && Time.time - _heldSince < readyGraceSeconds;
            if ((near && within) || petting)
            {
                // Stay put and watch them. _standoffValid is dropped so the spot (and the side it sits on) is
                // built fresh from where everyone actually is once the dog does set off.
                _standoffValid = false;
                Coast();
                FaceToward(toHolder);
                return true;
            }
            _graceEnded = true;
            Debug.Log("[KH_FrisbeeCatch] ready go (reason=" + (near ? "time" : "distance") + ")");
            return false;
        }

        // spot = holder + head direction * ahead + right * (lateral * side), pulled back inside the flat.
        // For variant 0 (ahead = standoffDistance, lateral = 0) this is exactly the stage-2 formula.
        Vector3 ReadySpot(Vector3 holderPos, Vector3 fwd)
        {
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 spot = holderPos + fwd * _readyAhead + right * (_readyLateral * _readySide);
            if (playAreaRadius > 0f)
            {
                // Keep the stand-off spot inside the flat (review P2): pull it toward the centre if needed.
                Vector3 c = spot - playAreaCenter; c.y = 0f;
                if (c.magnitude > playAreaRadius) spot = playAreaCenter + c.normalized * playAreaRadius + Vector3.up * (spot.y - playAreaCenter.y);
            }
            return spot;
        }

        // ---- Chase: sprint to the spot under the catch point, settle there, replanning ----
        void UpdateChase(double now, float elapsed)
        {
            if (!_dogOwner) { UpdateLocomotionAnim(); return; }
            SampleMotion();   // round 4: the last second of yaw rate / speed is the evidence for "machine or dog"
            if (!_haveLaunch) { GiveUp(); return; }   // nothing to cancel: this sequence has no launch facts (no line)
            if (elapsed > chaseTimeout) { CancelLaunch("chase-timeout"); return; }
            if (_flightEnded)
            {
                // Landed or stopped by a wall: fetch from the ground if it is within reach; never re-arm the jump.
                // Grabbed / recalled: nothing to fetch.
                if (Retrievable() && DogMayGo(frisbee.transform.position) && Vector3.Distance(dogRoot.position, frisbee.transform.position) <= retrieveMaxDistance) TransitionTo(StateRetrieve);
                else CancelLaunch("flight-ended:" + _flightEndReason);   // Codex M3: grabbed / lost / out of reach, said once
                return;
            }
            if (elapsed < noticeDelay) { Coast(); FaceToward(frisbee.transform.position - dogRoot.position); return; }
            float tNow = frisbee._ElapsedSince(_t0);
            if (Time.time >= _nextPlan) { _nextPlan = Time.time + replanInterval; Plan(tNow); }
            if (_plan == PlanJump && _mode != ModeAround)
            {
                // Momentum jump (B chase / C front). The crouch start moves with the dog's own speed (Plan
                // recomputes it at 10 Hz) and the pace is whatever covers the remaining path in the remaining time,
                // floored at a jog: the dog is never standing still when the crouch begins.
                float availC = _catchT - _tUp - _crouchReal - tNow;
                Vector3 toC = _standRoot - dogRoot.position; toC.y = 0f;
                float distC = toC.magnitude;
                RunApproach(availC);
                // Round 5: a momentum jump ARRIVES when the dog reaches the crouch start, not when the crouch
                // timer fires - the timer is fixed by _catchT, so measuring there made etaErr identical to the
                // plan's slack and said nothing about the estimate (first 36-throw run: etaErr == slack on every
                // hook/run plan). BeginJump still logs it as a fallback, tagged at=crouch with the distance, so a
                // plan the dog never actually reached is visible rather than silently scored as on time.
                if (distC <= HoldEnterRadius) LogEtaErr("reach");
                // IMP-DOG-017: a missed slot lets the solve run again (_solved = false) on what is left of the band.
                if (availC < -0.10f) { _plan = PlanNone; _nextPlan = 0f; _settledSince = -1f; _holding = false; _solved = false; _dropReason = "slot"; }
                else if (availC <= 0.02f)
                {
                    if (distC <= 1.2f) { BeginJump(now); return; }
                    _plan = PlanNone; _nextPlan = 0f; _settledSince = -1f; _holding = false; _solved = false; _dropReason = "gate";
                }
            }
            else if (_plan == PlanJump)
            {
                // A around: out through the via point, back onto the spot facing the thrower, settle, straight up.
                // IMP-DOG-017: paced to arrive settleTime before the crouch, so spare time is spent running slower
                // instead of standing on the spot.
                Vector3 to = _standRoot - dogRoot.position; to.y = 0f;
                float dist = to.magnitude;
                UpdateHold(dist);
                float crouchAt = _catchT - _tUp - _crouchReal;
                float avail = crouchAt - tNow;
                if (avail < -0.08f) { _plan = PlanNone; _nextPlan = 0f; _settledSince = -1f; _holding = false; _solved = false; _dropReason = "slot"; }   // missed the slot: solve again
                else if (avail <= 0.02f)
                {
                    // Never early: 0.08 s early puts the disc 0.5 m away. Too far from the spot to jump from here,
                    // so drop the plan and let the solve offer a later candidate, or the fallbacks.
                    if (dist <= 0.5f) { BeginJump(now); return; }
                    _plan = PlanNone; _nextPlan = 0f; _settledSince = -1f; _holding = false; _solved = false; _dropReason = "gate";
                }
                else HoldOrApproach(dist, avail - settleTime > 0.05f ? avail - settleTime : 0.05f);
            }
            else if (_plan == PlanStandOrOvertake)
            {
                // Two shapes share PlanStandOrOvertake: the stand catch waits on the spot facing the disc, the v3 overtake runs
                // straight through the point along the flight and snatches it without ever stopping.
                Vector3 to = _standRoot - dogRoot.position; to.y = 0f;
                float dist = to.magnitude;
                float avail = _catchT - tNow;
                if (_runningCatch)
                {
                    // No arrive radius and no braking: aim past the point once close, so the dog is still at speed
                    // when the mouth gets there (the catch window does the rest). Paced, unlike the intercept:
                    // arriving early is the whole point there and exactly wrong here - a sprint would carry the
                    // dog through the spot before the disc reaches it.
                    float pace = avail > 0.05f ? Mathf.Clamp(dist / avail, runSpeed, chaseRunSpeed) : chaseRunSpeed;
                    if (dist < 0.5f) LogEtaErr("overtake");   // an overtake never holds: reaching the point IS arriving
                    MoveToward(dist < 0.5f ? _standRoot + _forward * 1.5f : _standRoot, pace, 0f, chaseAcceleration, true, chaseDeceleration);
                }
                else { UpdateHold(dist); HoldOrApproach(dist, -1f); }   // D stand: sprint in and wait (unpaced)
                if (avail <= catchWindow)
                {
                    if (!_catchDecided) { _catchDecided = true; _bestGap = 99f; }
                    float gap = EdgeGap(tNow);
                    if (gap < _bestGap) _bestGap = gap;
                    if (gap <= catchTolerance && !HeldNow())
                    {
                        Debug.Log("[KH_FrisbeeCatch] " + (_runningCatch ? "overtake" : "stand") + " catch OK mouth-edge=" + gap.ToString("F3") + " at " + avail.ToString("F2") + "s" + VariantTail());
                        TransitionTo(StateReturn); return;
                    }
                    if (avail < -catchWindow)
                    {
                        Debug.Log("[KH_FrisbeeCatch] " + (_runningCatch ? "overtake" : "stand") + " catch MISS best mouth-edge=" + _bestGap.ToString("F3") + VariantTail());
                        _plan = PlanNone; _nextPlan = 0f; _catchDecided = false; _settledSince = -1f; _holding = false; _dropReason = "miss";
                    }
                }
            }
            else if (_plan == PlanRetrieve)
            {
                MoveToward(_landing, chaseRunSpeed, arriveRadius * 0.7f, chaseAcceleration, true, chaseDeceleration);
            }
            else
            {
                // Nothing reachable yet: follow the disc from behind until the prediction offers a point,
                // but never out of the flat (review D1): a throw leaving the plaza is simply not chased.
                Vector3 disc = frisbee._PositionAt(_p0, _v0, _curve, tNow);
                if (!DogMayGo(disc) || (_landingValid && !DogMayGo(_landing))) { CancelLaunch("out-of-area"); return; }
                MoveToward(disc, chaseRunSpeed, 1.0f, chaseAcceleration, true, chaseDeceleration);
            }
            ObserveFlight(tNow);
        }

        // Hold band bookkeeping (round 2b). The sprint's arrive radius is 0.1 m, so the hold must start well
        // outside it or it never starts at all; once holding, only a real displacement puts the dog back on the
        // sprint, otherwise it flickers on the band edge (the "jitter" in the PCVR notes).
        void UpdateHold(float dist)
        {
            if (!_holding) { if (dist < HoldEnterRadius) { _holding = true; _settledSince = Time.time; LogEtaErr("hold"); } }
            else if (dist > HoldExitRadius) { _holding = false; _settledSince = -1f; }
        }

        // Turn rate for the chase approach (IMP-DOG-017). From a standstill / below a walk the dog swings round at
        // launchTurnDegPerSec (the start of a chase); once running it keeps the dog's running rate.
        float ChaseTurnRate() { return _speed < walkSpeed ? launchTurnDegPerSec : turnSpeedDegPerSec; }

        // IMP-DOG-017 harness round 3 (Pome v0 beside): the cruise speed that covers d in exactly T under the SAME
        // model the solve's ETA uses - ramp at chaseAcceleration, then cruise. Pacing at the plain average d / T
        // made a dog starting from rest ask for 7 m/s while ramping, fall behind its own plan (5.7 m/s average over
        // the first 1.2 s) and lose the chase. From rest: T = v / (2a) + d / v, so v = a (T - sqrt(T^2 - 2d/a)).
        // Already moving at s: that is the from-rest case started s/a earlier, i.e. T' = T + s/a, d' = d + s^2/(2a).
        // No real root = unreachable -> vCap. A root below s means the dog is ahead of schedule -> ease to d / T.
        float PaceToArrive(float d, float T, float vCap)
        {
            if (T <= 0.05f) return vCap;
            float a = chaseAcceleration;
            float sp = _speed > 0f ? _speed : 0f;
            float tp = T + sp / a, dp = d + sp * sp / (2f * a);
            float disc = tp * tp - 2f * dp / a;
            if (disc < 0f) return vCap;
            float v = a * (tp - Mathf.Sqrt(disc));
            if (v < sp) v = d / T;
            return v > vCap ? vCap : v;
        }

        // One step of a MOMENTUM approach (B chase / C front): straight at the crouch start, paced so the dog crosses
        // it exactly on time and still running. Nothing here ever brakes to a stop.
        void RunApproach(float avail)
        {
            Vector3 dogPos = dogRoot.position;
            Vector3 toC = _standRoot - dogPos; toC.y = 0f;
            float dT = toC.magnitude;
            // IMP-DOG-017: B's cap is the breed's sprint, everything else runs at most chaseRunSpeed.
            float vCap = _mode == ModeChase ? chaseSprintSpeed : chaseRunSpeed;
            // Paced to arrive PaceLeadRun before the crouch (harness round 1: exactly on time left every stride of
            // error as a late arrival). The jog floor holds until the last RunPaceFloorPath metres, so the dog is
            // never standing on the crouch start (round 1: v0 mid took off with speedMin 0, off 0.39).
            float pace = avail > 0.05f ? PaceToArrive(dT, Mathf.Max(0.05f, avail - PaceLeadRun), vCap) : vCap;
            if (dT > RunPaceFloorPath && pace < HookMinPace) pace = HookMinPace;
            if (pace > vCap) pace = vCap;
            // The last few metres are held at the take-off ceiling, so the speed the dog crosses the crouch start
            // with IS the speed it jumps at - no braking stride between the two. Further out it may run flat out.
            // B's trim ceiling is never below its planned take-off (0.6 x chaseSprintSpeed, <= RunJumpMaxTakeoff).
            float trimCap = _takeoffCap;
            if (_mode == ModeChase)
            {
                float vTakeB = Mathf.Clamp(chaseSprintSpeed * ChaseTakeoffFraction, RunJumpMinTakeoff, RunJumpMaxTakeoff);
                if (trimCap < vTakeB) trimCap = vTakeB;
            }
            float ceiling = dT > RunJumpTrimDistance ? vCap : trimCap;
            if (pace > ceiling) pace = ceiling;
            // Aim past the crouch start once it is under the nose: braking onto a point is exactly what must not
            // happen here, and the air solve absorbs the last few centimetres anyway.
            // Arriving early means the dog can run past the crouch start before the 10 Hz replan slides it on;
            // once past it, keep aiming ahead along the run-up rather than turning back for it.
            bool past = Vector3.Dot(toC, _forward) < 0f;
            MoveToward(dT < 0.6f || past ? _standRoot + _forward * 2f : _standRoot, pace, 0f, chaseAcceleration, false, chaseDeceleration);
            if (dT > 0.05f && !past)
            {
                // The heading rolls onto the take-off facing over the last HoldBlendLength metres (B may come in up
                // to 60 deg off the flight line), so the last stride is already on it.
                float w = Mathf.Clamp01(1f - dT / HoldBlendLength);
                Vector3 steer = Vector3.Lerp(toC / dT, _forward, w);
                TurnAt(steer, ChaseTurnRate()); // net: owner-only
            }
            else TurnAt(_forward, ChaseTurnRate());
        }

        // One step of an A (around) or D (stand) approach toward _standRoot. Outside the hold band: A runs to its
        // via point, then onto the spot with the heading blending into the catch facing, so the last stride is
        // already along _forward; D runs straight in. avail > 0 paces the run to use that many seconds (A: arrive
        // settleTime before the crouch - spare time is spent running slower, IMP-DOG-017); avail <= 0 sprints (D).
        // Inside the band it holds, turns off the residual at a dog's standing rate and shuffles the last centimetres.
        void HoldOrApproach(float dist, float avail)
        {
            if (_holding)
            {
                // Round 4: the approach delivers the facing, so what is left is small and 120 deg/s is enough - and
                // it is a turn, not a snap. (Not the launch rate: this is a dog settling, not setting off.)
                TurnAt(_forward, standTurnDegPerSec); // net: owner-only
                // Deadband on the residual, and no facing from the walk, so the two do not fight each other.
                if (dist > 0.08f) MoveToward(_standRoot, walkSpeed, 0f, acceleration, false);
                else Coast();
                return;
            }
            Vector3 dogPos = dogRoot.position;
            if (_viaActive && !_viaDone)
            {
                // The corner is a blend, not a waypoint: inside ViaCornerRadius the dog is already steering at the
                // spot, so it rounds the via point instead of touching it. Latched, so it can never be re-entered.
                Vector3 toV0 = _viaPoint - dogPos; toV0.y = 0f;
                if (toV0.magnitude <= ViaCornerRadius) _viaDone = true;
            }
            float pace = chaseRunSpeed;
            if (avail > 0f)
            {
                float pathLeft = ViaPath(dogPos);
                // Harness round 1: aim to be there PaceLeadAround before the settle is due, not exactly on it.
                pace = PaceToArrive(pathLeft, Mathf.Max(0.05f, avail - PaceLeadAround), chaseRunSpeed);   // round 3: same model as the ETA
                if (pathLeft > MotionMinSpeed * MotionWindow && pace < HookMinPace) pace = HookMinPace;
                if (pace > chaseRunSpeed) pace = chaseRunSpeed;
            }
            if (_viaActive && !_viaDone)
            {
                Vector3 toV = _viaPoint - dogPos; toV.y = 0f;
                MoveToward(_viaPoint, pace, 0f, chaseAcceleration, false, chaseDeceleration);
                TurnAt(toV, ChaseTurnRate()); // net: owner-only
                return;
            }
            // Second leg. MoveToward's own brake (chaseDeceleration) eases the run down into the spot - at the hold
            // band it is still rolling at a jog - and the heading is steered separately so it can arrive on _forward
            // rather than on the line it travelled.
            Vector3 toT = _standRoot - dogPos; toT.y = 0f;
            float dT = toT.magnitude;
            MoveToward(_standRoot, pace, 0f, chaseAcceleration, false, chaseDeceleration);
            if (dT > 0.05f)
            {
                // The heading rolls onto the catch facing across the last leg (the via leg, or HoldBlendLength): that
                // is what lets the ETA charge the facing at the running rate instead of billing a spin on arrival.
                float blend = HoldBlendLength;
                if (_viaActive) { Vector3 leg = _standRoot - _viaPoint; leg.y = 0f; blend = leg.magnitude; }
                float w = Mathf.Clamp01(1f - dT / Mathf.Max(0.5f, blend));
                Vector3 steer = Vector3.Lerp(toT / dT, _forward, w);
                TurnAt(steer, ChaseTurnRate()); // net: owner-only
            }
        }

        // Turn at an explicit rate (FaceToward picks one from the gait; the approach wants to choose).
        void TurnAt(Vector3 dir, float ratePerSec)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0004f) return;
            dogRoot.rotation = Quaternion.RotateTowards(dogRoot.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up), ratePerSec * Time.deltaTime); // net: owner-only
        }

        void BeginJump(double now)
        {
            LogEtaErr("crouch");   // round 5: a momentum jump "arrives" when the crouch begins
            // The arc is anchored to the planned instant so the mouth meets the prediction on time; the clip
            // starts now, at most a frame or two behind the arc (starts more than 0.1 s late are replanned above).
            _jumpStart = _t0 + (_catchT - _tUp - _crouchReal);
            if (_jumpStart > now) _jumpStart = now;
            _jumpFrom = dogRoot.position; _jumpFrom.y = _groundY; _catchDecided = false; _airborne = false; _bestGap = 99f;
            animSpeed = _animScale;
            // settled = seconds spent standing on the spot before the crouch (round 2 wants this >= settleTime),
            // off = how far from it the dog actually was when the crouch began (the air solve absorbs the rest),
            // face = how many degrees of the about-face were still missing (round 2b: the twist misses came from
            // a mouth offset of 0.63 m applied in the wrong frame, so this is the number that explains them).
            Vector3 offTo = _standRoot - dogRoot.position; offTo.y = 0f;
            _faceErrAtTakeoff = Vector3.Angle(dogRoot.forward, _forward);
            // Round 4 amendment: B / C take off with the speed they actually have - the crouch is a stride, not a
            // stop ("実際跳ぶときは減速しない、トップスピードでは?"). A goes up from its settled stance. The air solve absorbs the rest.
            _jumpSpeed = _mode == ModeAround ? Mathf.Min(_speed, HookMinPace) : Mathf.Min(_speed, _takeoffCap);
            _vTake = _jumpSpeed;
            ComputeMotion(); _motionLatched = true;   // the approach is what these two numbers describe
            Debug.Log("[KH_FrisbeeCatch] takeoff v=" + variant
                + " settled=" + (_settledSince < 0f ? -1f : Time.time - _settledSince).ToString("F2")
                + " off=" + offTo.magnitude.ToString("F2")
                + " face=" + _faceErrAtTakeoff.ToString("F0") + MotionTail() + DogTag());
            TransitionTo(StateJump);
        }

        // ---- Jump: crouch on the ground, then a fixed parabola. Mouth meets the disc at tUp. ----
        void UpdateJump(double now)
        {
            if (!_dogOwner) { UpdateLocomotionAnim(); return; }
            float e = (float)(now - _jumpStart);
            Quaternion look = Quaternion.LookRotation(_forward, Vector3.up);
            dogRoot.rotation = Quaternion.RotateTowards(dogRoot.rotation, look, turnSpeedDegPerSec * Time.deltaTime); // net: owner-only
            _speed = _jumpSpeed;
            if (e < _crouchReal)
            {
                Vector3 p = _jumpFrom + _forward * (_jumpSpeed * e);
                dogRoot.position = SnapToGround(p); // net: owner-only
                _jumpFrom.y = dogRoot.position.y; _groundY = _jumpFrom.y;
                return;
            }
            float ta = e - _crouchReal;
            float g = frisbee.gravity;
            float vy = g * _tUp;
            if (!_airborne)
            {
                // Horizontal air velocity is whatever brings the root from here to the planned catch root at tUp.
                // Round 2: the takeoff is from a standstill, so this only ever corrects the few centimetres the
                // fine-tune left over (the cap stays, for the case where the dog had to jump from up to 0.5 m off).
                Vector3 start = dogRoot.position; start.y = _groundY;
                Vector3 need = _catchRoot - start; need.y = 0f;
                Vector3 vel = need / Mathf.Max(0.05f, _tUp);
                float cap = chaseRunSpeed * 1.2f;
                if (vel.magnitude > cap) vel = vel.normalized * cap;
                if (Vector3.Dot(vel, _forward) < 0f) vel = Vector3.zero; // never fly backwards
                // Codex D1: last gate before the feet leave the ground. The crouch start has been sliding with the
                // dog's own speed for the last 10 Hz replans and the air solve has just corrected the residual, so
                // the arc that is about to be flown is only final HERE. A jump that would come down outside the
                // flat (or off the ground) is not taken at all: drop the plan and let the scan offer a later
                // candidate, or the ground retrieve.
                Vector3 landing = start + vel * (2f * _tUp);
                if (!DogMayGo(landing) || !GroundOk(landing, _groundY))
                {
                    Debug.Log("[KH_FrisbeeCatch] liftoff refused: landing outside");
                    _plan = PlanNone; _nextPlan = 0f; _settledSince = -1f; _holding = false; _solved = false; _dropReason = "liftoff";   // IMP-DOG-017: solve again
                    _catchDecided = false; _runningCatch = false; _airborne = false; animSpeed = 1f;
                    dogRoot.position = SnapToGround(start); // net: owner-only
                    TransitionTo(StateChase);
                    return;
                }
                _airborne = true; _airStart = start; _airVel = vel;
            }
            Vector3 pos = _airStart + _airVel * ta;
            pos.y = _groundY + vy * ta - 0.5f * g * ta * ta;
            if (ta >= 2f * _tUp) pos.y = _groundY;
            dogRoot.position = pos; // net: owner-only
            if (!_catchDecided && ta >= _tUp - catchWindow)
            {
                float tNow = frisbee._ElapsedSince(_t0);
                float gap = EdgeGap(tNow);
                if (ta < _tUp - catchWindow + 0.001f) _bestGap = 99f;
                if (gap < _bestGap) _bestGap = gap;
                bool ok = gap <= catchTolerance && !_flightEnded && !_yieldAfterLanding && !HeldNow(); // yielding: no new catch (review D4)
                if (ok)
                {
                    _catchDecided = true;
                    float syncGap = Vector3.Distance(frisbee.transform.position, frisbee._PositionAt(_p0, _v0, _curve, tNow));
                    Debug.Log("[KH_FrisbeeCatch] catch OK mouth-edge=" + gap.ToString("F3") + " at " + (ta - _tUp).ToString("F2") + "s observed-predicted=" + syncGap.ToString("F3") + " rise=" + _rootRise.ToString("F2") + " scale=" + _animScale.ToString("F2") + VariantTail());
                    TransitionTo(StateJumpCarry); return;
                }
                if (ta >= _tUp + catchWindow)
                {
                    _catchDecided = true;
                    Vector3 predicted = frisbee._PositionAt(_p0, _v0, _curve, tNow);
                    Vector3 err = predicted - mouthAnchor.position; float dy = err.y; err.y = 0f;
                    bool inJump = dogAnimator.GetCurrentAnimatorStateInfo(0).IsName(_twist ? "Twist" : "Jump");
                    Debug.Log("[KH_FrisbeeCatch] catch MISS best mouth-edge=" + _bestGap.ToString("F3") + " now: dxz=" + err.magnitude.ToString("F2") + " dy=" + dy.ToString("F2")
                        + " yawErr=" + Vector3.Angle(dogRoot.forward, _forward).ToString("F0") + " face=" + _faceErrAtTakeoff.ToString("F0")
                        + " inJumpState=" + inJump + " rise=" + _rootRise.ToString("F2") + " scale=" + _animScale.ToString("F2") + " flightEnded=" + _flightEnded + VariantTail());
                }
            }
            if (ta >= 2f * _tUp)
            {
                // IMP-DOG-017 (Codex review): B's run-through is only taken when the whole run (recover + RunThroughSeconds
                // at chaseSprintSpeed * ChaseTakeoffFraction) is clear of obstacles; otherwise it lands and stops as usual.
                if (state == StateJumpCarry && _mode == ModeChase)
                {
                    float runLen = chaseSprintSpeed * ChaseTakeoffFraction * (_recoverDur / Mathf.Max(0.1f, _animScale) + RunThroughSeconds);
                    _runThroughClear = LaneClear(dogRoot.position, dogRoot.position + dogRoot.forward * runLen);
                }
                else _runThroughClear = false;
                TransitionTo(state == StateJumpCarry ? StateLandCarry : StateLand);
            }
        }

        void UpdateLand(double now, float elapsed)
        {
            if (!_dogOwner) { UpdateLocomotionAnim(); return; }
            float recover = _recoverDur / Mathf.Max(0.1f, _animScale);
            // Round 2: a standing jump lands on the spot and simply stops. The lerp is kept so a jump with residual
            // speed still eases out.
            float desired = Mathf.Lerp(_jumpSpeed, 0f, Mathf.Clamp01(elapsed / recover));
            // IMP-DOG-017: B with the disc does NOT stop - it runs on through the landing (and on for
            // RunThroughSeconds of the Return, see UpdateReturn) and then arcs back to the thrower.
            bool runThrough = _mode == ModeChase && state == StateLandCarry && !_yieldAfterLanding && _runThroughClear;
            // One stride ahead is re-checked every frame; a blocked stride ends the run-through for good (Codex review).
            if (runThrough && !LaneClear(dogRoot.position, dogRoot.position + _forward * LandingLaneMargin)) { _runThroughClear = false; runThrough = false; }
            if (runThrough) desired = chaseSprintSpeed * ChaseTakeoffFraction;
            // Every preset: the slide never carries the root off the flat. At the edge the dog holds its ground and
            // the clip plays on, so the landing still reads as a landing (Codex D).
            float reach = _speed > desired ? _speed : desired;
            if (DogMayGo(dogRoot.position + _forward * (reach * Time.deltaTime))) MoveToward(dogRoot.position + _forward * 2f, desired, 0f);
            else _speed = 0f;
            if (elapsed < recover) return;
            if (_yieldAfterLanding) { CancelLaunch("claimed"); return; } // leash / gathering took the dog while airborne
            if (state == StateLandCarry) { _runThrough = runThrough && _speed > 0f; TransitionTo(StateReturn); return; }
            if (HeldNow()) { CancelLaunch("held-regrab"); return; }
            if (_flightEnded)
            {
                if (Retrievable() && DogMayGo(frisbee.transform.position) && Vector3.Distance(dogRoot.position, frisbee.transform.position) <= retrieveMaxDistance) TransitionTo(StateRetrieve);
                else CancelLaunch("flight-ended:" + _flightEndReason);
            }
            else { _plan = PlanNone; _nextPlan = 0f; _solved = false; _dropReason = "miss"; TransitionTo(StateChase); } // missed while still flying: solve again
        }

        // ---- Ground pickup: walk to the resting disc, lower the head, take it ----
        void UpdateRetrieve(float elapsed)
        {
            if (!_dogOwner) { UpdateLocomotionAnim(); return; }
            if (elapsed > chaseTimeout) { CancelLaunch("retrieve-timeout"); return; }
            if (_flightEnded && !Retrievable()) { CancelLaunch("flight-ended:" + _flightEndReason); return; }
            Vector3 target = frisbee.transform.position;
            if (!DogMayGo(target)) { CancelLaunch("out-of-area"); return; } // rolled or was carried out of the flat
            if (!_flightEnded) target = _landingValid ? _landing : frisbee._PositionAt(_p0, _v0, _curve, frisbee._ElapsedSince(_t0));
            Vector3 to = target - dogRoot.position; to.y = 0f;
            float dist = to.magnitude;
            // Still flying: sprint to where it will land. On the ground: bring it back at the carry pace, not at
            // the sprint (round 2: only the chase has a deadline).
            MoveToward(target, !_flightEnded ? chaseRunSpeed : (dist > returnRunDistance ? returnRunSpeed : walkSpeed), arriveRadius * 0.7f);
            if (_flightEnded && dist <= arriveRadius) TransitionTo(StatePickup);
        }

        void UpdatePickup(float elapsed)
        {
            if (!_dogOwner) { UpdateLocomotionAnim(); return; }
            Coast();
            if (elapsed < pickupDuration) return;
            Vector3 to = frisbee.transform.position - dogRoot.position; to.y = 0f;
            if (to.magnitude > arriveRadius * 1.5f) { TransitionTo(StateRetrieve); return; } // it moved while the head was down
            TransitionTo(StateReturn);
        }

        // ---- Return to the thrower, wait so they can take it from the mouth, else set it down ----
        void UpdateReturn(float elapsed)
        {
            if (!_dogOwner) { UpdateLocomotionAnim(); return; }
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(throwerId);
            if (!Utilities.IsValid(p)) { CancelLaunch("thrower-left"); return; }
            Vector3 playerPos = p.GetPosition();
            Vector3 to = playerPos - dogRoot.position; to.y = 0f;
            float dist = to.magnitude;
            if (state == StateReturn)
            {
                if (elapsed > returnTimeout) { CancelLaunch("return-timeout"); return; }
                if (_runThrough)
                {
                    // IMP-DOG-017 B: the run-through IS the arc home. The dog keeps the landing's pace along its OWN
                    // heading while turning toward the thrower at the running rate, so the path bends round instead
                    // of the old straight run followed by MoveToward's sideways slide (Codex review). Same flat clamp
                    // as the landing, plus one stride of lane: at the edge or an obstacle the run-through ends.
                    float keep = chaseSprintSpeed * ChaseTakeoffFraction;
                    float reach = _speed > keep ? _speed : keep;
                    Vector3 head = dogRoot.forward; head.y = 0f;
                    if (elapsed < RunThroughSeconds && head.sqrMagnitude > 1e-4f
                        && DogMayGo(dogRoot.position + head * (reach * Time.deltaTime))
                        && LaneClear(dogRoot.position, dogRoot.position + head.normalized * LandingLaneMargin))
                    {
                        MoveToward(dogRoot.position + head.normalized * 2f, keep, 0f, chaseAcceleration, false, chaseDeceleration);
                        TurnAt(to, turnSpeedDegPerSec); // net: owner-only
                        return;
                    }
                    _runThrough = false;
                }
                MoveToward(playerPos, dist > returnRunDistance ? returnRunSpeed : walkSpeed, returnStopDistance);
                if (dist <= returnStopDistance + 0.05f && _speed <= stopSpeedThreshold) TransitionTo(StateDrop);
                return;
            }
            Coast();
            FaceToward(to);
            // IMP-DOG-013: the dog has arrived, is standing still and has the disc in its mouth - ApplyState
            // resumed petting on the way into Drop (PetAllowed). While hands are on it the drop timer STOPS, so
            // the disc is never set down in the middle of a stroke; it resumes where it left off when they leave.
            // The head is written by the petting controller in LateUpdate and the disc is placed from the mouth
            // anchor in PostLateUpdate, which runs after it, so the disc follows the petted head rather than
            // fighting it.
            if (BeingPetted())
            {
                _dropPetHold += Time.deltaTime;
                if (!_dropPetLogged) { _dropPetLogged = true; Debug.Log("[KH_FrisbeeCatch] drop wait paused: being petted"); }
            }
            if (!_released && elapsed - _dropPetHold >= dropWaitDuration)
            {
                _released = true;
                ReleaseDisc(dogRoot.forward * 0.3f);
                TransitionTo(StateIdle);
            }
        }

        // Mouth anchor to the disc edge, using the prediction (== flight on the thrower's client).
        float EdgeGap(float tNow)
        {
            float gap = Vector3.Distance(mouthAnchor.position, frisbee._PositionAt(_p0, _v0, _curve, tNow)) - frisbee.radius;
            return gap < 0f ? 0f : gap;
        }

        // ---- Locomotion (compact port of KD_DogFetchController's walk/run/turn/snap) ----
        void MoveToward(Vector3 target, float desiredSpeed, float stopDist) { MoveToward(target, desiredSpeed, stopDist, acceleration, true); }

        void MoveToward(Vector3 target, float desiredSpeed, float stopDist, float accel) { MoveToward(target, desiredSpeed, stopDist, accel, true, deceleration); }

        // face = false: move without steering the body (the hold phase owns the facing itself).
        void MoveToward(Vector3 target, float desiredSpeed, float stopDist, float accel, bool face) { MoveToward(target, desiredSpeed, stopDist, accel, face, deceleration); }

        // decel: round 4. The generic 36 m/s2 is a hand-brake; the chase passes chaseDeceleration so the run rolls
        // down into the catch spot instead of stopping on it (the "ピュッと移動" of PCVR 3).
        void MoveToward(Vector3 target, float desiredSpeed, float stopDist, float accel, bool face, float decel)
        {
            Vector3 pos = dogRoot.position;
            Vector3 to = target - pos; to.y = 0f;
            float dist = to.magnitude;
            float remain = dist - stopDist; if (remain < 0f) remain = 0f;
            float brake = Mathf.Sqrt(2f * decel * remain);
            if (desiredSpeed > brake) desiredSpeed = brake;
            float rate = (desiredSpeed > _speed ? accel : decel) * Time.deltaTime;
            _speed = Mathf.MoveTowards(_speed, desiredSpeed, rate);
            if (remain <= 0f) _speed = 0f;
            if (_speed <= stopSpeedThreshold && desiredSpeed <= stopSpeedThreshold) _speed = 0f;
            if (face && dist > 0.02f) FaceToward(to);
            if (_speed > 0f && dist > 0.0001f)
            {
                float step = _speed * Time.deltaTime; if (step > remain) step = remain;
                dogRoot.position = SnapToGround(pos + (to / dist) * step); // net: owner-only
            }
        }

        void FaceToward(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0004f) return;
            // Round 4: one rate per gait instead of "idle or 720". Standing still inside a catch state is not the
            // same as standing still while wandering - a dog watching a disc turns its whole body at a dog's pace.
            float turnRate;
            if (_speed <= stopSpeedThreshold) turnRate = (state == StateChase || IsAirborne) ? standTurnDegPerSec : idleTurnSpeedDegPerSec;
            else if (_speed <= walkSpeed + 0.2f) turnRate = walkTurnDegPerSec;
            else turnRate = turnSpeedDegPerSec;
            dogRoot.rotation = Quaternion.RotateTowards(dogRoot.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up), turnRate * Time.deltaTime); // net: owner-only
        }

        void Coast()
        {
            if (_dogOwner) _speed = Mathf.MoveTowards(_speed, 0f, deceleration * Time.deltaTime);
            UpdateLocomotionAnim();
        }

        void ObserveSpeed()
        {
            Vector3 p = dogRoot.position;
            float dt = Time.deltaTime;
            // Anything faster than the sprint is a network jump, not a run (the ceiling follows chaseRunSpeed:
            // round 2 sprints at 10 m/s, and a fixed 8 would have blanked the run clip on every observer).
            if (_obsValid && dt > 0.0001f) { Vector3 d = p - _obsPrev; d.y = 0f; _speed = d.magnitude / dt; if (_speed > chaseRunSpeed * 1.5f) _speed = 0f; }
            _obsPrev = p; _obsValid = true;
        }

        void UpdateLocomotionAnim()
        {
            bool moving = _speed > stopSpeedThreshold;
            bool running = moving && _speed >= runAnimSpeed;
            bool walking = moving && !running;
            if (running != _wroteRun || !_outputValid) { dogAnimator.SetBool("Run", running); _wroteRun = running; }
            if (walking != _wroteWalk || !_outputValid) { dogAnimator.SetBool("Walk", walking); _wroteWalk = walking; }
            ApplyAnimatorSpeed();
            _outputValid = true;
        }

        // Jump states play the clip at the arc pace; a sprint above runSpeed plays the run clip faster
        // (every client derives it from its own _speed, observed or written).
        void ApplyAnimatorSpeed()
        {
            float spd = 1f;
            if (IsAirborne) spd = animSpeed;
            else if (_speed > runSpeed) spd = Mathf.Min(maxRunAnimScale, _speed / runSpeed);
            if (Mathf.Abs(spd - _wroteSpeed) > 0.01f || _lastApplied < 0 || !_outputValid) { dogAnimator.speed = spd; _wroteSpeed = spd; }
        }

        // No ground under the point: keep the height (the flat prototype ground has an edge; see review U4).
        Vector3 SnapToGround(Vector3 pos)
        {
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * groundRayOffset, Vector3.down, out hit, groundRayDistance, groundLayerMask, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y;
            return pos;
        }

        // Observed disc far from the prediction for a while means the flight was interrupted without an event.
        void ObserveFlight(float tNow)
        {
            Vector3 predicted = frisbee._PositionAt(_p0, _v0, _curve, tNow);
            float gap = Vector3.Distance(frisbee.transform.position, predicted);
            if (gap < 3f) { _deviationSince = -1f; return; }
            if (_deviationSince < 0f) { _deviationSince = Time.time; return; }
            if (Time.time - _deviationSince > 0.5f) { _flightEnded = true; _flightEndReason = KH_Frisbee.EndUnknown; }
        }

        // Disc at the mouth while carried (display on every client). PostLateUpdate runs after the
        // constraints that place the mesh-side mouth bone, so the disc follows the drawn mouth (review D6).
        public override void PostLateUpdate()
        {
            if (!IsCarrying) return;
            if (HeldNow()) return;
            Vector3 fwd = dogRoot.forward, up = dogRoot.up;
            frisbee.transform.position = mouthAnchor.position + fwd * carryForward + up * carryUp;
            frisbee.transform.rotation = Quaternion.LookRotation(fwd, up) * Quaternion.Euler(carryTiltDeg, 0f, 0f);
        }
    }
}
