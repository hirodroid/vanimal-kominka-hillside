using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    // DES-DOG-005 M3 (docs/plans/2026-09-28-frisbee-anywhere.md §D-3 / §D-4): the disc in the water.
    //
    //   ドボン (D-3)  a jump may come DOWN in the water (never take off from it): JumpLand (Planner) accepts a landing point where
    //                 the disc floats (D-1: the spot's water or the river's, with a usable bank within reach), inside the river-wide
    //                 water (_FetchWater), and - for a dog that does not swim - no deeper than its Fetch wading limit (0.85 M) and
    //                 its sink guard. The root lands afloat (a swimmer where the water is deep enough) or on the riverbed
    //                 (Wander._WaterLandY), and the touchdown goes straight to 12 (with the disc) or 11 (missed), never Land.
    //   11 WaterChase to the floating disc: from the bank point chosen by KH_RiverData._EntrySlice (the NavMesh there), then under
    //                 the river rules (Wander._WaterRiverStep). At the mouth (0.25 m, -0.1 < mouth - disc < 1.0 m) the disc's
    //                 ownership is asked for and the dog stays in 11 until it is ours (<= DiscClaimTimeout, asked again once a
    //                 second): then _Caught -> 12; not ours in time = claim-failed (the disc floats on untouched). A disc that
    //                 leaves the water near where it floated = the land Retrieve once the dog is out of the water (it walks /
    //                 swims out on the water legs first); farther (called away) or a hand / recall notice = target-lost.
    //   12 WaterCarry to the exit bank (KH_RiverData._ExitSlice: the thrower's side first, E-3), -> 13 on dry ground.
    //   13 WaterExit  1.2 s on the bank, then over the NavMesh to within 3 m of a thrower on this bank (-> Return), or to the
    //                 exit's return point and Drop there (thrower across the river / no path).
    //   Hold watch    12 / 13 / Return / Drop: the disc must stay ours; lost after it was ours = hold-lost (GiveUp WITHOUT touching
    //                 the disc: its new Owner's KH_Frisbee.CheckRiver frees / floats it). A ドボン catch whose claim is still on its
    //                 way stands in 12 until it arrives (<= DiscClaimTimeout, else claim-failed), so 13 / Return / Drop always
    //                 start with a confirmed disc. Land carries whose claim was never confirmed keep the old rule
    //                 (design I-7: the land claim wait is the M3 review's call).
    //   Owner change  the existing owner-change -> GiveUp (Update): the new Owner releases a disc in the mouth (it floats in the
    //                 water) and the Wander's stranded rule walks the wet dog out.
    //   Display       Owner and non-Owner: the Wander's legs write Walk / Run (non-Owners: _WaterFetchHold from the observed speed);
    //                 Animator.speed stays ours (ApplyAnimatorSpeed from ObserveSpeed, on every client); the disc sits in the mouth
    //                 through PostLateUpdate (IsCarrying includes 12 / 13).
    // Nothing new is synced (state 11-13 in `state`; waterPose is the Wander legs'). No new network-callable method. Every other
    // Owner-local value below is reset where the sequence starts. The prototype scene has no riverData: WaterFetchOn is false and
    // none of this runs.
    public partial class KH_FrisbeeCatch
    {
        // D-3: a landing in the water may be this far below the take-off ground (the bank above the water; land keeps jumpMaxRise).
        public const float DobonMaxDrop = 1.0f;
        // A wading (non-swimming) dog never lands where its walk-clip mouth would be within this of the water (the Wander's sink guard).
        public const float DobonSinkGuard = 0.03f;
        // The ball's numbers (KD_DogFetchController.HillsideWater / Owner.cs), the same for the disc (design D-1 "ボールと同じ").
        const float WaterMouthReach = 0.25f, WaterMouthAboveMax = 1f, WaterClaimLost = 1f, WaterExitStand = 1.2f;
        const float WaterSlack = 4f, RiverCarryHandover = 3f, WaterToReturnMax = 20f;
        const float DiscClaimTimeout = 2f, DiscClaimRetry = 1f;
        // D-4 table: the dog stopped at the band while the disc still flies waits this long; a disc that left the water farther than
        // WaterLostDistance from where it last floated is a lost target.
        public const float RiverBlockWaitSeconds = 3f, WaterLostDistance = 2f;

        // ---- DES-DOG-005 M3, fixed take-off for a momentum jump into the water (decision 2026-09-28, Codex consult) ----
        // 94c run 20260928-112611: the committed take-off T0 (on the bank, rays checked) moved 0.5-0.64 m toward the water while the
        // dog ran in: the dog reached the crouch start PaceLeadRun early and ran past it, and KeepPlan's fit (the distance left / (tUp
        // + crouch)) lowered the take-off speed and re-derived T0 = G - f v tUp from it, onto the bank's steep face ("liftoff refused:
        // take-off (slope)", "drop=ground"). For a jump into the water (_landWater, B / C) the verified T0, its ground, catchRoot, catchT,
        // tUp and the facing stay as committed; only the crouch start C0 = T0 - f vC crouch follows the dog's speed vC, and the crouch
        // glides the dog onto T0 (the air velocity then comes from T0 -> catchRoot through the existing cap / forward rules). A dog
        // that cannot make T0 (past it, or a crouch speed outside the limits) is not rescued: the plan is dropped (re-plan). The land
        // plans are unchanged. animSpeed is not touched (the clip keeps its own timing).
        const float FixedCrouchMaxV = RunJumpMaxTakeoff + 1f;   // the crouch glide may not be faster than this (m/s)
        const float FixedCrouchMaxDv = 3.5f;                    // nor differ from the dog's own speed by more than this (m/s)
        private bool _fxOn;
        private Vector3 _fxT0, _fxFrom, _fxVec;
        private float _fxE0;
        // Diagnostics (log and the Course/95 probe): forward distance to T0 and the lateral error at the last KeepFixedT0 / at the crouch,
        // the crouch glide speed and its difference from the dog's speed, and the glide's end error on T0.
        private float _fxAhead = -1f, _fxLat, _fxCrouchV = -1f, _fxDv, _fxEndErr = -1f;

        // KeepPlan for a momentum jump into the water (10 Hz). s = dot(T0 - dog, f) is the SIGNED distance ahead to T0; C0 must stay ahead
        // of the dog: s >= vC crouch. vC is the dog's speed within this plan's band; when that would put C0 behind the dog, vC is lowered
        // to s / crouch (a slower crouch), and below RunJumpShortTakeoff (or with T0 behind the dog) the plan is dropped (DropMode).
        bool KeepFixedT0(float tNow, Vector3 dogPos)
        {
            Vector3 d = _fxT0 - dogPos; d.y = 0f;
            float s = Vector3.Dot(d, _forward);
            Vector3 lat = d - _forward * s;
            _fxAhead = s; _fxLat = lat.magnitude;
            float c = _crouchReal;
            float vC = Mathf.Clamp(_speed, _takeoffMin, _takeoffCap);
            if (s < vC * c) vC = c > 1e-3f ? s / c : 0f;
            if (vC < RunJumpShortTakeoff) return DropMode("t0");   // past T0 (or on it with no crouch left to run): not rescued
            Vector3 c0 = _fxT0 - _forward * (vC * c); c0.y = _standRoot.y;
            if (Vector3.Distance(c0, _standRoot) > 0.10f) { _standRoot = c0; _jumpSpeed = vC; }
            float availR = (_catchT - _crouchReal - _tUp) - tNow;
            Vector3 toR = c0 - dogPos; toR.y = 0f;
            float pathR = toR.magnitude;
            if (availR <= 0.02f) { if (pathR < 1.2f) return true; _dropReason = "late"; _solved = false; return false; }
            if (pathR <= KeepCloseRun || availR <= KeepLateSeconds) return true;
            float vCap = _mode == ModeChase ? chaseSprintSpeed : chaseRunSpeed;
            if (pathR / availR > vCap * KeepPaceSlack) return DropMode("pace");
            return true;
        }

        // BeginJump (Owner): the crouch glide from here onto T0 in the crouch time left (the jump may start a little behind the arc:
        // _jumpStart is anchored to the planned instant). False = not possible (T0 behind, or the glide too fast / too different from the
        // dog's speed): the plan is dropped and Chase solves again (the same way a refused liftoff returns).
        bool FixedCrouchBegin(double now)
        {
            float e0 = (float)(now - _jumpStart); if (e0 < 0f) e0 = 0f;
            float rc = _crouchReal - e0;
            Vector3 from = dogRoot.position;
            Vector3 v = _fxT0 - from; v.y = 0f;
            float ahead = Vector3.Dot(v, _forward);
            _fxAhead = ahead; _fxLat = (v - _forward * ahead).magnitude;
            string why = "";
            // Codex M3b review 1: only a numerical tolerance for "passed" and "no time", and the speed / speed-difference limits are
            // checked whenever there is crouch time left (a short remainder used to skip them)
            if (ahead < -0.02f) why = "passed";
            else if (rc < 0.01f) why = "no-time";
            else
            {
                _fxCrouchV = v.magnitude / rc; _fxDv = _fxCrouchV - _speed;
                if (_fxCrouchV > FixedCrouchMaxV) why = "fast";
                else if (Mathf.Abs(_fxDv) > FixedCrouchMaxDv) why = "dv";
            }
            Debug.Log("[KH_FrisbeeCatch] fixed takeoff crouch v=" + _fxCrouchV.ToString("F2") + " dogV=" + _speed.ToString("F2") + " dv=" + _fxDv.ToString("F2")
                + " ahead=" + ahead.ToString("F2") + " lateral=" + _fxLat.ToString("F2") + " left=" + rc.ToString("F2") + (why.Length > 0 ? " refused=" + why : "") + DogTag());
            if (why.Length > 0)
            {
                _plan = PlanNone; _nextPlan = 0f; _settledSince = -1f; _holding = false; _solved = false; _dropReason = "crouch-" + why; _fxOn = false;
                return false;
            }
            _fxFrom = from; _fxVec = v; _fxE0 = e0; _fxEndErr = -1f;
            return true;
        }

        Vector3 FixedCrouchAt(float e)
        {
            float span = _crouchReal - _fxE0;
            float k = span > 1e-3f ? Mathf.Clamp01((e - _fxE0) / span) : 1f;
            return _fxFrom + _fxVec * k;
        }

        // The first air frame: the root is put on T0 AT THE PLANNED TAKE-OFF HEIGHT (the ground the plan's ray measured), and the error
        // the last crouch frame had is recorded. 94c run 20260928-114922 (Lab, z 135): re-snapping here read the ground 0.068 m lower
        // than the plan's ray (the snap ray starts at the dog, the plan's 1.5 m above: a thin top such as a stone is seen by one only);
        // the liftoff's vertical speed grew with it and the landing's two rays straddled the far water's edge (liftoff refused: landing
        // outside, iter). The take-off height is part of what is fixed (Codex consult); the liftoff gate still re-checks T0.
        void FixedCrouchEnd()
        {
            Vector3 cur = dogRoot.position;
            Vector3 err = _fxT0 - cur; err.y = 0f; _fxEndErr = err.magnitude;
            float snapY = SnapToGround(new Vector3(_fxT0.x, cur.y, _fxT0.z)).y;
            dogRoot.position = _fxT0; // net: owner-only
            _groundY = _fxT0.y;
            Debug.Log("[KH_FrisbeeCatch] fixed takeoff end err=" + _fxEndErr.ToString("F3") + " t0=" + V3(_fxT0) + " snapDy=" + (snapY - _fxT0.y).ToString("F3") + DogTag());
        }

        // Planner scratch (JumpLand / AirLanding) and the committed plan's landing in the water.
        private bool _jgWater, _alWater, _cLandWater, _landWater, _airLandWater;
        // Owner-local water sequence.
        private float _riverWaitSince = -1f;
        private bool _discConfirmed;   // the disc has been ours during this carry (the hold watch arms only then)
        private bool _keepDisc;        // GiveUp leaves the disc alone (it is not ours)
        private bool _ownerChangeGiveUp;   // this GiveUp is the owner-change recovery (it releases the mouth's disc, design D-4)
        private Vector3 _wfEntry, _wfExit, _wfExitReturn, _wfDiscLast;
        private int _wfEntryCode = -1, _wfExitCode = -1;
        private bool _wfEntered, _wfDiscValid, _wfWaitFlight, _wfLandCarry, _wfToReturn, _wfToLand;
        private float _wfLimit, _wfLimitFirst, _wfCarryLimit, _wfLandLimit, _wfLandAt, _wfToReturnAt, _wfWaitSince;
        private float _wfClaimAt = -1f, _wfClaimSentAt = -1000f, _wfHoldLostAt = -1f;
        // Harness only (Course/94, never set by the runtime; Owner-local heap values, reset by stopping Play):
        //   _debugWaterFetchOff  the M2 rules (a floating disc is not fetched: cannot-play / river-band), so the M2 suite keeps testing
        //                        the float alone
        //   _debugClaimRefuse    the disc's ownership never reads as ours (F14 claim-failed; the ball's _debugClaimRefuse)
        private bool _debugWaterFetchOff, _debugClaimRefuse;

        public int DebugWaterEntryCode { get { return _wfEntryCode; } }
        public int DebugWaterExitCode { get { return _wfExitCode; } }

        // ---- gates ----

        // The water states can run: the river data (with the river-wide fetch grid) and the Wander's water legs are wired.
        bool WaterFetchOn()
        {
            if (_debugWaterFetchOff || riverData == null || wander == null) return false;
            if (wander.riverData == null || wander.waterSpot == null) return false;
            return riverData._FetchReady();
        }

        // C-1 water branch: a swimmer anywhere it floats; a non-swimmer no deeper than its Fetch wading limit (0.85 M).
        bool WaterDepthOk(Vector3 p)
        {
            return wander._WaterCanSwim() || wander._WaterDepthAt(p) <= wander._WaterFetchWadeLimit();
        }

        // D-1 "ドボンの着地": the disc floats here (at the water line), the river-wide water, a usable bank slice within reach, and a
        // non-swimmer can stand in it.
        bool DobonAt(Vector3 g)
        {
            if (!WaterFetchOn()) return false;
            Vector3 w = new Vector3(g.x, riverData._SurfaceY(g.z), g.z);
            if (!riverData._FetchWater(w) || !DiscInFloatWater(w)) return false;
            // Codex M3 review 4: the spot's float test has no bank rule, and the water states leave by the river's slices - a usable
            // slice within reach in the same bridge section is required here whatever floated it (the river's _FloatsAt has it too)
            if (riverData._NearestSlice(w, 0, riverData._BankReach(), -1) < 0) return false;
            if (wander._WaterCanSwim()) return true;
            float depth = wander._WaterDepthAt(w);
            return depth <= wander._WaterFetchWadeLimit() && wander._WaterWalkMouth() - depth >= DobonSinkGuard;
        }

        // D-4 table row 1 (Update, on a river stop): true = keep this Chase standing at the band - the disc is still flying.
        bool RiverBlockWait()
        {
            if (!WaterFetchOn() || state != StateChase || _flightEnded) return false;
            if (_riverWaitSince < 0f)
            {
                _riverWaitSince = Time.time;
                Debug.Log("[KH_FrisbeeCatch] river wait: stopped at the band, the disc is still flying" + DogTag());
            }
            return Time.time - _riverWaitSince <= RiverBlockWaitSeconds;
        }

        // Water speed for the time contract: swim (swimmers) or walk, less the drift, at least 0.2 m/s (as the ball).
        float WaterSpeed()
        {
            float v = wander._WaterCanSwim() ? wander._WaterSwimSpeedNow() : walkSpeed;
            float drift = waterSpot != null ? waterSpot.floatDrift : KH_WaterSpot.DefaultFloatDrift;
            return Mathf.Max(0.2f, v - drift);
        }

        // The dog is still in the water: its pose (the Wander writes it after every leg), or afloat / easing down.
        bool DogWet()
        {
            if (fetch != null && fetch.WaterPose != 0) return true;
            return wander._WaterFloating() || wander._WaterDepthAt(dogRoot.position) > 0.02f;
        }

        float FlatDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        // ---- entries ----

        // Chase / Land / Retrieve / Pickup (Owner): the disc rests where it floats. True = handled here (the water chase started,
        // or the dog gave up: deep-disc / no-bank). False = the water states are off, or it does not float (the land rules go on).
        bool WaterIntercept(Vector3 disc)
        {
            if (!WaterFetchOn() || !DiscInFloatWater(disc)) return false;
            BeginWaterChase(disc);
            return true;
        }

        void BeginWaterChase(Vector3 disc)
        {
            if (!WaterDepthOk(disc))
            {
                Debug.Log("[KH_FrisbeeCatch] water give-up reason=deep-disc depth=" + wander._WaterDepthAt(disc).ToString("F2") + " limit=" + wander._WaterFetchWadeLimit().ToString("F2") + DogTag());
                CancelLaunch("deep-disc");
                return;
            }
            Vector3 dog = dogRoot.position;
            int code = riverData._EntrySlice(dog, disc);
            if (code < 0) { CancelLaunch("no-bank"); return; }
            float land = riverData._LastEntryPath();
            _wfEntryCode = code; _wfEntry = riverData._SliceBank(code); _wfEntered = false;
            _wfLimit = land / Mathf.Max(0.5f, runSpeed) + FlatDist(_wfEntry, disc) / WaterSpeed() + WaterSlack;
            _wfLimitFirst = _wfLimit;
            _wfDiscLast = disc; _wfDiscValid = true; _wfWaitFlight = false; _wfClaimAt = -1f; _wfExitCode = -1; _wfToLand = false;
            Debug.Log("[KH_FrisbeeCatch] water chase entry=" + V3(_wfEntry) + " side=" + riverData._SliceSide(code) + " path=" + land.ToString("F1")
                + " limit=" + _wfLimit.ToString("F1") + " disc=" + V3(disc) + DogTag());
            TransitionTo(StateWaterChase);
        }

        // D-3 touchdown (UpdateJump, Owner): the root is already at the planned height in the water.
        void EnterWaterFromJump()
        {
            Vector3 pos = dogRoot.position;
            bool carrying = state == StateJumpCarry;
            wander._WaterBeginAt(pos, carrying);
            // The way back in the worst case is the take-off spot on the bank (no entry slice: the dog jumped in)
            _wfEntry = _airStart; _wfEntryCode = -1; _wfEntered = true; _wfExitCode = -1; _wfToLand = false;
            Debug.Log("[KH_FrisbeeCatch] dobon carrying=" + (carrying ? "true" : "false") + " at=" + V3(pos) + " takeoff=" + V3(_airStart)
                + " depth=" + wander._WaterDepthAt(pos).ToString("F2") + " pose=" + (fetch != null ? fetch.WaterPose : -1) + DogTag());
            if (_yieldAfterLanding) { CancelLaunch("claimed"); return; }   // leash / gathering while airborne (GiveUp lets a caught disc go: it floats)
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(throwerId);
            if (!Utilities.IsValid(p)) { CancelLaunch("thrower-left"); return; }
            if (carrying)
            {
                // JumpCarry already asked for the disc (ClaimDisc); WaterHoldTick waits for it if it has not arrived yet
                _wfHoldLostAt = -1f;
                PlanWaterExit(pos, p.GetPosition());
                TransitionTo(StateWaterCarry);
                return;
            }
            // Missed: stay in the water and see where the disc comes down (<= RiverBlockWaitSeconds): floating -> the chase from here
            _wfWaitFlight = true; _wfWaitSince = Time.time; _wfDiscValid = false; _wfClaimAt = -1f;
            _wfLimit = RiverBlockWaitSeconds + WaterSlack; _wfLimitFirst = _wfLimit;
            TransitionTo(StateWaterChase);
        }

        // ---- the states ----

        void UpdateWater(float elapsed)
        {
            bool carrying = state != StateWaterChase;
            if (!_dogOwner) { if (wander != null) wander._WaterFetchHold(carrying); return; }   // legs from the observed speed
            if (!WaterFetchOn()) { CancelLaunch("water-off"); return; }
            if (!_haveLaunch) { GiveUp(); return; }
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(throwerId);
            if (!Utilities.IsValid(p)) { CancelLaunch("thrower-left"); return; }
            if (carrying && WaterHoldTick()) return;
            if (state == StateWaterChase) WaterChaseTick(elapsed, p);
            else if (state == StateWaterCarry) WaterCarryTick(elapsed);
            else WaterExitTick(elapsed, p);
        }

        void WaterChaseTick(float elapsed, VRCPlayerApi p)
        {
            Vector3 disc = frisbee.transform.position;
            Vector3 dog = dogRoot.position;
            if (_wfWaitFlight)
            {
                // A missed ドボン: the disc may still be in the air
                if (!_flightEnded)
                {
                    wander._WaterRiverHold(false);
                    TurnAt(disc - dog, standTurnDegPerSec); // net: owner-only
                    if (Time.time - _wfWaitSince > RiverBlockWaitSeconds) CancelLaunch("water-wait");
                    return;
                }
                _wfWaitFlight = false;
                if (!Retrievable()) { CancelLaunch("flight-ended:" + _flightEndReason); return; }
                // Came down on land (or where it does not float): the dog has nothing to swim to - it walks out (the Wander's
                // stranded exit after GiveUp) and the disc waits there
                if (!DiscInFloatWater(disc)) { CancelLaunch("missed-water"); return; }
                if (!WaterDepthOk(disc)) { CancelLaunch("deep-disc"); return; }
                _wfDiscLast = disc; _wfDiscValid = true;
                _wfLimit = elapsed + FlatDist(dog, disc) / WaterSpeed() + WaterSlack;
                Debug.Log("[KH_FrisbeeCatch] water chase (missed catch) limit=" + _wfLimit.ToString("F1") + " disc=" + V3(disc) + DogTag());
            }
            // Codex M3 review 2: a hand or a recall ended the flight (EndGrabbed / timeout) - checked BEFORE the claim wait, so a
            // recall whose notice beats the disc's position never gets the disc asked for again or taken into the mouth
            if (_flightEnded && !Retrievable()) { _wfClaimAt = -1f; CancelLaunch("target-lost"); return; }
            if (_wfClaimAt >= 0f)
            {
                // At the disc, waiting for its ownership (the ball's RC-17): stay on it; taken away meanwhile = lost target
                if (FlatDist(disc, dog) > WaterClaimLost + WaterMouthReach) { _wfClaimAt = -1f; CancelLaunch("target-lost"); return; }
                wander._WaterRiverHold(false);
                if (DiscOwned()) { TakeDiscInWater(p); return; }
                if (Time.time - _wfClaimAt > DiscClaimTimeout)
                {
                    Debug.Log("[KH_FrisbeeCatch] disc claim failed after " + (Time.time - _wfClaimAt).ToString("F2") + " s" + DogTag());
                    _wfClaimAt = -1f; CancelLaunch("claim-failed");   // not in the mouth: GiveUp does not touch the disc
                    return;
                }
                RequestDisc();
                return;
            }
            if (!DiscInFloatWater(disc))
            {
                // D-4 table: left the water. Farther than WaterLostDistance from where it last floated (called away, sent to the bank)
                // = lost; nearby (it drifted onto the bank) = the land retrieve
                if (_wfDiscValid && FlatDist(disc, _wfDiscLast) > WaterLostDistance) { CancelLaunch("target-lost"); return; }
                // Codex M3 review 3: the land Retrieve moves by MoveToward (ground snap), which would drop a swimming dog onto the
                // riverbed - so a wet dog first walks / swims out toward the disc on the Wander's water legs, then the land retrieve
                if (DogWet())
                {
                    if (!_wfToLand) { _wfToLand = true; Debug.Log("[KH_FrisbeeCatch] water chase: the disc left the water nearby; walking out first" + DogTag()); }
                    wander._WaterRiverStep(disc, false, _wfEntry, true);
                    if (elapsed > _wfLimit + WaterSlack) CancelLaunch("water-timeout");
                    return;
                }
                Debug.Log("[KH_FrisbeeCatch] water chase: the disc left the water nearby; land retrieve" + DogTag());
                TransitionTo(StateRetrieve);
                return;
            }
            _wfDiscLast = disc; _wfDiscValid = true; _wfToLand = false;
            if (!_wfEntered && (FlatDist(dog, _wfEntry) < 0.5f || riverData._FetchWater(dog)))
            {
                // The ball's two-stage contract: at the water the limit is the time so far + the water distance now (never shorter)
                _wfEntered = true;
                float lim = elapsed + FlatDist(dog, disc) / WaterSpeed() + WaterSlack;
                if (lim > _wfLimit) _wfLimit = lim;
                Debug.Log("[KH_FrisbeeCatch] water entered after=" + elapsed.ToString("F1") + " limit=" + _wfLimit.ToString("F1") + " first=" + _wfLimitFirst.ToString("F1") + DogTag());
            }
            wander._WaterRiverStep(disc, false, _wfEntry, _wfEntered);
            // Reach (as the ball, :262): the mouth or the dog over the disc horizontally, and the disc not above the mouth
            Vector3 dm = mouthAnchor.position - disc; float above = dm.y; dm.y = 0f;
            Vector3 dr = dogRoot.position - disc; dr.y = 0f;
            if ((dm.magnitude <= WaterMouthReach || dr.magnitude <= WaterMouthReach) && above > -0.1f && above < WaterMouthAboveMax)
            {
                _wfClaimAt = Time.time;
                if (DiscOwned()) { TakeDiscInWater(p); return; }
                Debug.Log("[KH_FrisbeeCatch] disc claim wait (in the water)" + DogTag());
                RequestDisc();
                return;
            }
            if (elapsed > _wfLimit) CancelLaunch("water-timeout");
        }

        void TakeDiscInWater(VRCPlayerApi p)
        {
            float waited = _wfClaimAt >= 0f ? Time.time - _wfClaimAt : 0f;
            _wfClaimAt = -1f;
            frisbee._Caught();   // ours now: frozen, gravity for later (KH_Frisbee._Caught writes both as the Owner)
            _discConfirmed = true;
            if (waited > 0.05f) Debug.Log("[KH_FrisbeeCatch] disc claim ok after=" + waited.ToString("F2") + DogTag());
            PlanWaterExit(dogRoot.position, p.GetPosition());
            TransitionTo(StateWaterCarry);
        }

        void PlanWaterExit(Vector3 dog, Vector3 thrower)
        {
            int code = riverData._ExitSlice(dog, thrower, _wfEntryCode);
            if (code < 0) { _wfExit = _wfEntry; _wfExitReturn = _wfEntry; }   // no slice at all: back where it came in (the take-off)
            else { _wfExit = riverData._SliceBank(code); _wfExitReturn = riverData._SliceReturn(code); }
            _wfExitCode = code; _wfLandCarry = false; _wfToReturn = false;
            _wfCarryLimit = FlatDist(dog, _wfExit) / WaterSpeed() + WaterSlack;
            int throwerSide = thrower.x >= riverData._CenterX(thrower.z) ? 1 : -1;
            Debug.Log("[KH_FrisbeeCatch] water exit plan exit=" + V3(_wfExit) + " side=" + (code < 0 ? 0 : riverData._SliceSide(code))
                + " throwerSide=" + throwerSide + " limit=" + _wfCarryLimit.ToString("F1") + DogTag());
        }

        void WaterCarryTick(float elapsed)
        {
            Vector3 dog = dogRoot.position;
            wander._WaterRiverStep(_wfExit, true, _wfExit, true);
            if (FlatDist(_wfExit, dog) < 0.4f && wander._WaterDepthAt(dog) <= 0.02f) { TransitionTo(StateWaterExit); return; }
            if (elapsed > _wfCarryLimit) CancelLaunch("water-timeout");   // GiveUp lets the disc go (it floats in the water)
        }

        void WaterExitTick(float elapsed, VRCPlayerApi p)
        {
            Vector3 dog = dogRoot.position;
            if (_wfToReturn)
            {
                // A thrower across the river (or no path): carried to this bank's return point and set down there (the ball's rule)
                wander._WaterFetchWalkTo(_wfExitReturn);
                if (FlatDist(_wfExitReturn, dog) < 0.5f || Time.time - _wfToReturnAt > WaterToReturnMax) TransitionTo(StateDrop);
                return;
            }
            if (!_wfLandCarry) wander._WaterRiverHold(true);
            if (elapsed < WaterExitStand) return;
            Vector3 thrower = p.GetPosition();
            if (!_wfLandCarry)
            {
                bool farBank = riverData._OtherBank(dog, thrower);
                float len = farBank ? -1f : riverData._NavPathLength(dog, thrower);
                if (farBank || len < 0f)
                {
                    Debug.Log("[KH_FrisbeeCatch] water exit: thrower " + (farBank ? "across the river" : "out of reach") + "; drop at the return point" + DogTag());
                    _wfToReturn = true; _wfToReturnAt = Time.time;
                    return;
                }
                _wfLandLimit = len / Mathf.Max(0.5f, runSpeed) + WaterSlack;
                _wfLandCarry = true; _wfLandAt = Time.time;
                Debug.Log("[KH_FrisbeeCatch] water carry path=" + len.ToString("F1") + " limit=" + _wfLandLimit.ToString("F1") + DogTag());
            }
            float toThrower = FlatDist(dog, thrower);
            if (toThrower > RiverCarryHandover)
            {
                if (Time.time - _wfLandAt > _wfLandLimit) { CancelLaunch("water-timeout"); return; }
                wander._WaterFetchLandTo(thrower, toThrower > returnRunDistance ? runSpeed : walkSpeed);
                return;
            }
            TransitionTo(StateReturn);   // the last metres, the fan-out and the Drop are the land Return's (ClaimDisc: already ours)
        }

        // ---- the disc's ownership (the ball's RC-17 for the disc; design 0-7 / D-4) ----

        bool DiscOwned() { return !_debugClaimRefuse && Networking.IsOwner(frisbee.gameObject); }

        // Asked at most once per DiscClaimRetry (never every frame).
        void RequestDisc()
        {
            if (Time.time - _wfClaimSentAt < DiscClaimRetry) return;
            _wfClaimSentAt = Time.time;
            AskDisc("water");   // BUG-DOG-006: skipped while held; the claim wait's timeout (claim-failed) ends it
        }

        // 12 / 13, every frame (Owner). True = this frame is handled (gave up, or still waiting). A disc that was ours and is not any
        // more (someone took it) = hold-lost; a ドボン catch whose claim has not arrived yet stands where it is (no 12 -> 13 -> Return
        // before the disc is ours: Codex M3 review 1) for at most DiscClaimTimeout, asked again once a second = claim-failed. Either
        // way GiveUp does not touch the disc (_keepDisc). So 13, Return and Drop are only ever reached with a confirmed disc.
        bool WaterHoldTick()
        {
            if (DiscOwned())
            {
                if (!_discConfirmed) { _discConfirmed = true; frisbee._Caught(); }   // a late claim: freeze it now that we may
                _wfHoldLostAt = -1f;
                return false;
            }
            if (_discConfirmed)
            {
                Debug.Log("[KH_FrisbeeCatch] disc ownership lost state=" + state + DogTag());
                _keepDisc = true; CancelLaunch("hold-lost");
                return true;
            }
            if (_wfHoldLostAt < 0f) { _wfHoldLostAt = Time.time; Debug.Log("[KH_FrisbeeCatch] disc claim wait (carrying) state=" + state + DogTag()); }
            if (Time.time - _wfHoldLostAt > DiscClaimTimeout) { _keepDisc = true; CancelLaunch("claim-failed"); return true; }
            RequestDisc();
            wander._WaterRiverHold(true);   // wait here, in the water, with the disc at the mouth
            return true;
        }

        // Return / Drop (Owner, before the release). Armed only once the disc has been ours in this carry: a land claim that has not
        // arrived yet keeps the pre-M3 behaviour (design I-7). True = gave up (hold-lost, the disc left as it is).
        bool HoldWatch()
        {
            if (DiscOwned())
            {
                if (!_discConfirmed) { _discConfirmed = true; frisbee._Caught(); }
                return false;
            }
            if (!_discConfirmed) return false;
            Debug.Log("[KH_FrisbeeCatch] disc ownership lost state=" + state + DogTag());
            _keepDisc = true; CancelLaunch("hold-lost");
            return true;
        }
    }
}
