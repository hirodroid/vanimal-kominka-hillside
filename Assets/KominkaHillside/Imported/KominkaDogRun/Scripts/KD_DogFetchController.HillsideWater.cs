using UdonSharp;
using UnityEngine;
using UnityEngine.AI;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaDogRun
{
    // IMP-DOG-020 stage 1 (DES-DOG-004 C-1): the chase and the carry back are straight moves with no path check, and the
    // water surface has no collider (SnapToGround lands on the riverbed). A step into the river band is refused and
    // the Fetch gives up (reason=river-band). The ball itself is brought back to the bank by KH_ToyRecall (C-7/8).
    //
    // The single place for later exceptions: the in-water Fetch states of DES-DOG-004 stage 2 (WaterChase / WaterReturn /
    // WaterExit) will be allowed here inside the water-play spot. Owner-only; no synced variable.
    //
    // IMP-DOG-020 steps 2-3 (DES-DOG-004 A-1): waterPose lives here because this behaviour is Manual and has the same Owner
    // as the dog root (the root itself is VRCObjectSync / Continuous). 0 land / 1 wading / 2 swimming (step 4, not yet).
    // Written only by the dog Owner (KD_DogWanderController.HillsideWater.WaterPoseRefresh: at the top of its Update, and again
    // after every water move - the water play and the Fetch's water steps, RC-14) and serialized only when it changes. Every
    // client reads it for the splash FX; the leash manager and the toy recall read it to refuse a dog that is in the water.
    public partial class KD_DogFetchController
    {
        public KominkaHillside.KH_RiverData riverData;
        const float RiverProbe = 0.1f;
        [UdonSynced] int waterPose;

        public int WaterPose { get { return waterPose; } } // net: callable-ok: read-only synced pose

        bool _BlockedByRiver(Vector3 from, Vector3 next)
        {
            if (riverData == null) return false;
            return riverData._BlocksStep(from, next, KominkaHillside.KH_RiverData.BandMargin);
        }

        void GiveUpAtRiver()
        {
            Debug.Log("[KD_DogFetchController] give-up dog=" + dogIndex + " state=" + dogState
                + " reason=river-band at=(" + dogRoot.position.x.ToString("F2") + "," + dogRoot.position.y.ToString("F2") + "," + dogRoot.position.z.ToString("F2") + ")");
            GiveUp();
        }

        /// <summary>Dog Owner only (the wander layer derives the pose). Sends only on a change.</summary>
        public void _SetWaterPose(int pose)
        {
            if (pose == waterPose || !IsDogOwner() || _debugPoseHold) return;
            waterPose = pose;
            RequestSerialization();
        }

        /// <summary>Harness only (Course/78 W4-leash-stale): keep waterPose as written, standing in for a leash arbiter whose view
        /// of the pose is older than the dog's position (RC-14). Local, never synced.</summary>
        bool _debugPoseHold;

        /// <summary>RC-14 (破壊監査 2026-09-28, FUNC-04 BUG-02): the dog's root is wet in the water-play spot (the spot's riverbed
        /// grid, the same rule as the pose). The leash arbiter asks this as well as the synced pose: the dog Owner writes the pose at
        /// the top of its Update and again after each water move, but on another client (the arbiter's) the pose can arrive
        /// later than the position, and before RC-14 the frame that stepped in showed pose 0 until the next frame.</summary>
        public bool _WetAtRoot()
        {
            KD_DogWanderController w = MyWander();
            return w != null && dogRoot != null && w._WaterWetAt(dogRoot.position);
        }

        /// <summary>Local: someone asked for this dog while it is in the water (leash / toy). The dog Owner turns to the
        /// asker and walks out; the request itself is not accepted (DES-DOG-004 B-1, acceptance after landing).</summary>
        public void _AskWaterExit()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            if (IsDogOwner()) { KD_DogWanderController w = MyWander(); if (w != null) w._WaterExitAsked(lp.playerId); return; }
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(WaterExitAsk));
        }

        /// <summary>Like _AskWaterExit, for a request someone else made (the leash arbiter refusing a clip attach): the dog turns
        /// to that player (Codex 2026-09-27 D-中).</summary>
        public void _AskWaterExitFor(int playerId)
        {
            if (IsDogOwner()) { KD_DogWanderController w = MyWander(); if (w != null) w._WaterExitAsked(playerId); return; }
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(WaterExitAskFor), playerId);
        }

        // net: callable-ok: same as WaterExitAsk; the id only chooses whom the dog looks at before it walks out.
        [NetworkCallable(maxEventsPerSecond: 4)]
        public void WaterExitAskFor(int playerId)
        {
            if (!IsDogOwner()) return;
            KD_DogWanderController w = MyWander();
            if (w != null && Utilities.IsValid(VRCPlayerApi.GetPlayerById(playerId))) w._WaterExitAsked(playerId);
        }

        // ---- a throw while the dog is in the water (DES-DOG-004 B-1) ----
        // At the throw the dog is asked out at once, as before (W4-throw: the exit starts within 1.5 s; run 20260927-112624 failed
        // at 3.02 s when the ask waited for the ball). While the ball flies (at most WaterThrowFlight) only the way of going after it
        // is open: a ball that comes down floating in the spot starts the water fetch at once (WaterPlay / its exit -> WaterChase,
        // Codex 2026-09-27 D-中); a non-swimmer takes only a ball within its wading limit and drops a deep one. A ball at rest
        // anywhere else (on land): the dog keeps walking out and the Fetch starts after landing (10 s from the throw).
        const float WaterThrowHold = 10f, WaterThrowFlight = 3f;
        int _waterThrowPlayer = -1;
        float _waterThrowAt;
        bool _waterThrowExitAsked, _waterDirect;

        /// <summary>From StartFetch (Owner). True = the throw is held; false = start as usual.</summary>
        bool WaterHoldsThrow(int playerId)
        {
            if (waterPose == 0 || _waterDirect) return false;
            if (_waterThrowPlayer < 0)
            {
                _waterThrowPlayer = playerId; _waterThrowAt = Time.time; _waterThrowExitAsked = false;
                Debug.Log("[KD_DogFetchController] throw held dog=" + dogIndex + " (in the water)");
                KD_DogWanderController w0 = MyWander();
                if (w0 != null) w0._WaterExitAsked(playerId);   // the exit starts now; the landing spot of the ball decides later
            }
            return true;
        }

        /// <summary>Top of Update (Owner): the held throw goes straight into the water fetch, or waits for landing (10 s max).</summary>
        void WaterThrowTick()
        {
            if (_waterThrowPlayer < 0) return;
            if (!_dogOwner || (_ballPickup != null && _ballPickup.IsHeld))
            {
                Debug.Log("[KD_DogFetchController] throw dropped dog=" + dogIndex);
                _waterThrowPlayer = -1; return;
            }
            KD_DogWanderController ww = MyWander();
            if (!_waterThrowExitAsked)
            {
                if (dogState == StateIdle && ww != null && ww.waterSpot != null && _ballT != null && ww.waterSpot._FloatsAt(_ballT.position))
                {
                    int thrower = _waterThrowPlayer; _waterThrowPlayer = -1;
                    if (!ww._WaterCanSwim() && ww.waterSpot._Depth(_ballT.position) > ww._WaterFetchWadeLimit())
                    {
                        Debug.Log("[KD_DogFetchController] throw dropped dog=" + dogIndex + " reason=deep-ball (in the water)");
                        return;
                    }
                    bool direct = false;
                    if (Utilities.IsValid(VRCPlayerApi.GetPlayerById(thrower)))
                    {
                        _waterDirect = true; direct = StartFetch(thrower); _waterDirect = false;
                        if (direct) { _wfRiver = false; _wfBallLast = _ballT.position; _wfBallValid = true; ww._InvalidateLeashAnimation(); TransitionTo(StateWaterChase); }
                    }
                    Debug.Log("[KD_DogFetchController] throw into the water dog=" + dogIndex + " direct=" + (direct ? "true" : "false"));
                    return;
                }
                // Decided "on land" once the ball rests elsewhere (or after the flight time): wait for the landing as before
                bool resting = _ballRb == null || _ballRb.isKinematic || _ballRb.velocity.sqrMagnitude < 0.09f;
                if ((resting && Time.time - _waterThrowAt > 0.3f) || Time.time - _waterThrowAt > WaterThrowFlight) _waterThrowExitAsked = true;
                return;
            }
            if (Time.time - _waterThrowAt > WaterThrowHold)
            {
                Debug.Log("[KD_DogFetchController] throw dropped dog=" + dogIndex);
                _waterThrowPlayer = -1; return;
            }
            if (waterPose != 0 || dogState != StateIdle) return;
            int player = _waterThrowPlayer; _waterThrowPlayer = -1;
            bool started = Utilities.IsValid(VRCPlayerApi.GetPlayerById(player)) && StartFetch(player);
            Debug.Log("[KD_DogFetchController] throw after landing dog=" + dogIndex + " started=" + (started ? "true" : "false"));
        }

        // ================================================================ IMP-DOG-020 step 6 (DES-DOG-004 stage 2)
        // Floating ball retrieval. dogState gains 6 WaterChase (swim / wade to the ball) / 7 WaterReturn (carrying, to the exit)
        // / 8 WaterExit (landed, 1.2 s) -> the land Return 4 (holding and kinematic carried over). Same responsibilities as the land
        // states (B-1 "水中の Fetch の保持と解放"): 7 takes the ball's ownership and freezes it like Return; LateUpdate glues it to
        // the mouth in 7 and 8; GiveUp releases it in 7 and 8 too, and it floats again (KH_ToyRecall). The dog moves through
        // KD_DogWanderController._WaterFetchStep, so swim height, look-ahead, depth rules and the spot exception are shared.
        // Entry (Owner): a Chase whose ball floats in the spot. Swimmers go in; the others only for a ball no deeper than their
        // wading limit, else the Fetch gives up (reason=deep-ball) and the ball keeps floating. Timeouts: 6 and 7 get
        // 1.5 x (region diagonal / swim speed) + 5 s; after landing the land Return's 20 s starts again. A thrower with no complete
        // land path from the exit (far bank) gets the ball dropped at the exit (Drop). Owner logs: water-timeout / deep-ball.
        public const int StateWaterChase = 6, StateWaterReturn = 7, StateWaterExit = 8;
        // State 8 on a thrower across the river (or out of reach): walk to the entered bank's return point and Drop there.
        const float WaterToReturnMax = 20f;
        bool _waterToReturn;
        float _waterToReturnAt;
        /// <summary>6-8: the dog's legs are the Wander's (_WaterFetchStep), so the Wander's "Fetch busy" branch must keep its
        /// _speed (run 20260927-095400: zeroed every frame, the W3 dogs crawled at one frame's acceleration, 0.05-0.09 m/s).</summary>
        public bool IsWaterFetching { get { return dogState >= StateWaterChase; } }
        const float WaterMouthReach = 0.25f, WaterMouthAboveMax = 1f, WaterExitStand = 1.2f;
        int _waterExitIdx = -1;

        // ---- IMP-DOG-021 (DES-DOG-004 §E-3 / E-4): the same states anywhere along the river ----
        // A Chase whose ball floats in the river outside the spot (KH_RiverData._FloatsAt) becomes a "river" water fetch (_wfRiver,
        // Owner-local, never synced): the way in is the bank point of the ok slice nearest the ball (the side with the shorter NavMesh
        // path from the dog), reached over the NavMesh; the dog then wades / swims to the ball under the same depth rules
        // (KD_DogWanderController._WaterRiverStep). Out: the ok slice nearest the pickup on the thrower's side (the other side only
        // when the thrower's has none within reach), then the land Return from there (8 -> 4); a thrower on the other bank still
        // gets the Drop at the return point (the existing 8 rule). Time contract: 6 = NavMesh path to the entry / run speed + water
        // distance / water speed + WaterSlack; 7 = water distance to the exit / water speed + WaterSlack; the land Return after a
        // river exit gets path to the thrower / run speed + WaterSlack when that is longer than returnTimeout. Water speed = swim
        // (swimmers) or walk speed, less the drift (the ball moves away at 0.15 m/s). Over the limit = give-up (water-timeout).
        // A ball that leaves the water more than ballTeleportThreshold from where it last floated (someone called it, E-4) is a lost
        // target: give-up (reason=target-lost) and the Wander walks the dog out (the stranded rule). Large dogs walk up to the Fetch
        // limit (KH_RiverData.fetchWadeRatio x M) in the spot and in the river; free play keeps the spot's 0.45 M.
        const float WaterSlack = 4f, WaterClaimLost = 1f;
        bool _wfRiver, _wfEntered, _wfBallValid;
        Vector3 _wfEntry, _wfExit, _wfExitReturn, _wfBallLast;
        int _wfEntryCode = -1, _wfExitCode = -1;
        float _wfLimit, _wfCarryLimit, _returnTimeoutExtra;
        // Codex review 2026-09-28: the state-6 limit is planned again when the dog reaches the water (the ball drifted while it ran
        // there; _wfLimitFirst keeps the first plan for the harness), and the carry from a river bank to the thrower goes over the
        // NavMesh in 8 (_wfLandCarry) until RiverCarryHandover, then the land Return.
        const float RiverCarryHandover = 3f;
        bool _wfLandCarry;
        float _wfLandAt, _wfLandLimit, _wfEnteredAt = -1f, _wfLimitFirst, _wfPlanLand;

        /// <summary>The dog Owner's river-wide water fetch (read by the Wander's push-apart for the river depth rules).</summary>
        public bool IsRiverWaterFetching { get { return dogState >= StateWaterChase && _wfRiver; } }

        /// <summary>From Update (after the Idle branch). True = this frame is handled here.</summary>
        bool WaterFetchTick()
        {
            if (dogState == StateChase && _dogOwner && WaterFetchIntercept()) return true;
            if (dogState < StateWaterChase) return false;
            KD_DogWanderController w = MyWander();
            if (w == null || w.waterSpot == null) { if (_dogOwner) GiveUp(); return true; }
            bool carrying = dogState != StateWaterChase;
            if (!_dogOwner) { w._WaterFetchHold(carrying); return true; }   // legs from the observed speed; the root is synced
            bool river = _wfRiver && riverData != null && riverData._FetchReady();
            if (BallHeld())
            {
                if (_claimNext >= 0) Debug.Log("[KD_DogFetchController] give-up dog=" + dogIndex + " state=" + dogState + " reason=held-while-claiming");
                GiveUp(); return true;
            }
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(attentionPlayerId);
            if (!Utilities.IsValid(p) || _ballT == null) { GiveUp(); return true; }
            if (carrying && HoldTick()) return true;   // RC-17: 7 / 8 keep the ball ours and frozen (Owner.cs)
            float elapsed = Time.time - _stateEnteredTime;
            Vector3 dog = dogRoot.position;
            if (dogState == StateWaterChase)
            {
                Vector3 ball = _ballT.position;
                // RC-17: reached the ball, waiting for its ownership (at most 2 s): stay on it, then 7 frozen, or give up.
                // IMP-DOG-021 (E-4): a ball taken away meanwhile (called to someone) is not claimed back.
                if (_claimNext >= 0)
                {
                    if (FlatDist(ball, dog) > WaterClaimLost + WaterMouthReach) { _claimNext = -1; WaterGiveUp("target-lost"); return true; }
                    if (river) w._WaterRiverHold(false); else w._WaterFetchHold(false);
                    ClaimTick(); return true;
                }
                bool floats = river ? riverData._FloatsAt(ball) : w.waterSpot._FloatsAt(ball);
                if (!floats)
                {
                    // E-4: left the water far from where it floated = called away (or reset): the dog loses it and walks out
                    if (_wfBallValid && FlatDist(ball, _wfBallLast) > ballTeleportThreshold) { WaterGiveUp("target-lost"); return true; }
                    TransitionTo(StateChase); w._InvalidateLeashAnimation(); return true;   // back on land nearby: land chase
                }
                _wfBallLast = ball; _wfBallValid = true;
                if (river)
                {
                    if (!_wfEntered && (FlatDist(dog, _wfEntry) < 0.5f || riverData._FetchWater(dog))) { _wfEntered = true; RiverReplanAtEntry(w, dog, ball, elapsed); }
                    w._WaterRiverStep(ball, false, _wfEntry, _wfEntered);
                }
                else w._WaterFetchStep(ball, false);
                // Horizontal reach (mouth, or the dog standing over the ball) and the ball not above the mouth: a wading Lab's
                // mouth is ~0.45 m above a ball at the water line, so a 3D 0.25 m never held (run 20260927-102801, W3-Lab-shallow
                // stood on the ball for 90 s). The ball then sits in the mouth (LateUpdate glue), as on land.
                Vector3 mouth = mouthAnchor != null ? mouthAnchor.position : dog;
                Vector3 dm = mouth - ball; float above = dm.y; dm.y = 0f;
                Vector3 dr = dog - ball; dr.y = 0f;
                if ((dm.magnitude <= WaterMouthReach || dr.magnitude <= WaterMouthReach) && above > -0.1f && above < WaterMouthAboveMax)
                {
                    _waterToReturn = false;
                    if (river) RiverPlanExit(w, dog, p.GetPosition()); else _waterExitIdx = w.waterSpot._NearestExit(dog);
                    BeginCarry(StateWaterReturn); return true;
                }
                if (elapsed > (river ? _wfLimit : WaterTimeout(w))) WaterGiveUp("water-timeout");
                return true;
            }
            if (dogState == StateWaterReturn)
            {
                Vector3 exit;
                if (river) exit = _wfExit;
                else
                {
                    if (_waterExitIdx < 0 || _waterExitIdx >= w.waterSpot._ExitCount()) _waterExitIdx = w.waterSpot._NearestExit(dog);
                    exit = w.waterSpot._Exit(_waterExitIdx);
                }
                if (river) w._WaterRiverStep(exit, true, exit, true); else w._WaterFetchStep(exit, true);
                Vector3 d = exit - dog; d.y = 0f;
                if (d.magnitude < 0.4f && w._WaterDepthAt(dog) <= 0.02f) { _waterToReturn = false; TransitionTo(StateWaterExit); return true; }
                if (elapsed > (river ? _wfCarryLimit : WaterTimeout(w))) WaterGiveUp("water-timeout");
                return true;
            }
            // StateWaterExit: landed with the ball, stand, then the land Return to a thrower on this bank. A thrower across the river
            // (the land Return walks straight and would stop at the band, even with a bridge path) or out of reach gets the ball
            // dropped at this bank's return point (design B-1; Codex 2026-09-27 D-中: not at the exit, not a Return).
            if (_waterToReturn)
            {
                Vector3 rp;
                if (river) rp = _wfExitReturn;
                else
                {
                    if (_waterExitIdx < 0 || _waterExitIdx >= w.waterSpot._ExitCount()) _waterExitIdx = w.waterSpot._NearestExit(dog);
                    rp = w.waterSpot._ReturnPointFor(_waterExitIdx);
                }
                w._WaterFetchWalkTo(rp);
                Vector3 dd = rp - dog; dd.y = 0f;
                if (dd.magnitude < 0.5f || Time.time - _waterToReturnAt > WaterToReturnMax)
                {
                    _waterToReturn = false; w._InvalidateLeashAnimation(); TransitionTo(StateDrop);
                }
                return true;
            }
            bool landCarry = river && _wfLandCarry;
            if (!landCarry) { if (river) w._WaterRiverHold(true); else w._WaterFetchHold(true); }
            if (elapsed < WaterExitStand) return true;
            Vector3 thrower = p.GetPosition();
            if (!landCarry)
            {
                bool farBank = WaterOtherBank(w, dog, thrower);
                if (farBank || !WaterPathToPlayer(dog, thrower))
                {
                    Debug.Log("[KD_DogFetchController] water exit dog=" + dogIndex + " thrower " + (farBank ? "across the river" : "out of reach") + ": drop at the return point");
                    _waterToReturn = true; _waterToReturnAt = Time.time;
                    return true;
                }
            }
            if (river)
            {
                // E-3 (Codex review 2026-09-28 高): from a river bank the way to the thrower is the NavMesh ("復帰点から NavMesh で持ち
                // 帰る"); the land Return walks straight and would stop at the band across a bend. Still in 8 (holding, frozen, glued);
                // within RiverCarryHandover of the thrower the land Return takes the last metres (its fan slot and the Drop).
                // Limit: path to the thrower / run speed + WaterSlack from the start of the carry (over = water-timeout).
                if (!_wfLandCarry)
                {
                    float len = NavPathLength(dog, thrower);
                    _wfLandLimit = (len >= 0f ? len : FlatDist(dog, thrower) * 1.5f) / Mathf.Max(0.5f, runSpeed) + WaterSlack;
                    _wfLandCarry = true; _wfLandAt = Time.time;
                    Debug.Log("[KD_DogFetchController] river carry dog=" + dogIndex + " path=" + len.ToString("F1") + " limit=" + _wfLandLimit.ToString("F1"));
                }
                float toThrower = FlatDist(dog, thrower);
                if (toThrower > RiverCarryHandover)
                {
                    if (Time.time - _wfLandAt > _wfLandLimit) { WaterGiveUp("water-timeout"); return true; }
                    w._WaterFetchLandTo(thrower, toThrower > returnRunDistance ? runSpeed : walkSpeed);
                    return true;
                }
            }
            w._InvalidateLeashAnimation();
            TransitionTo(StateReturn);
            return true;
        }

        /// <summary>Chase (Owner): the ball floats in the spot -> into the water, or give up for a large dog and a deep ball.
        /// IMP-DOG-021: a ball floating in the river outside the spot -> the river water fetch (entry planned here).</summary>
        bool WaterFetchIntercept()
        {
            if (_ballT == null) return false;
            KD_DogWanderController w = MyWander();
            if (w == null || w.waterSpot == null || !w.waterSpot._Ready()) return false;
            Vector3 ball = _ballT.position;
            if (w.waterSpot._FloatsAt(ball))
            {
                if (!w._WaterCanSwim() && w.waterSpot._Depth(ball) > w._WaterFetchWadeLimit())
                {
                    Debug.Log("[KD_DogFetchController] give-up dog=" + dogIndex + " state=" + dogState + " reason=deep-ball");
                    GiveUp();
                    return true;
                }
                _wfRiver = false; _wfBallLast = ball; _wfBallValid = true;   // Codex 2026-09-28 中: a call before the next update is a lost target too
                w._InvalidateLeashAnimation();   // the wander legs take over the Animator in the water states
                TransitionTo(StateWaterChase);
                return true;
            }
            if (riverData == null || !riverData._FloatsAt(ball)) return false;
            if (!w._WaterCanSwim() && riverData._DepthAt(ball) > w._WaterFetchWadeLimit())
            {
                Debug.Log("[KD_DogFetchController] give-up dog=" + dogIndex + " state=" + dogState + " reason=deep-ball (river)");
                GiveUp();
                return true;
            }
            if (!RiverPlanEntry(w, ball))
            {
                Debug.Log("[KD_DogFetchController] give-up dog=" + dogIndex + " state=" + dogState + " reason=no-bank");
                GiveUp();
                return true;
            }
            _wfRiver = true; _wfEntered = false; _wfBallLast = ball; _wfBallValid = true; _wfEnteredAt = -1f; _wfLimitFirst = _wfLimit; _wfLandCarry = false;
            w._InvalidateLeashAnimation();
            TransitionTo(StateWaterChase);
            return true;
        }

        /// <summary>E-3: the way in for a river ball. The ok slice nearest the ball on each side; the side whose bank point has the
        /// shorter complete NavMesh path from the dog (no complete path on either side: the nearer bank point to the ball). Sets the
        /// entry and the state-6 time limit. False = no usable bank within reach (KH_RiverData._FloatsAt already requires one).</summary>
        bool RiverPlanEntry(KD_DogWanderController w, Vector3 ball)
        {
            float reach = riverData._BankReach();
            int a = riverData._NearestSlice(ball, -1, reach, -1), b = riverData._NearestSlice(ball, 1, reach, -1);
            if (a < 0 && b < 0) return false;
            Vector3 dog = dogRoot.position;
            float la = a >= 0 ? NavPathLength(dog, riverData._SliceBank(a)) : -1f;
            float lb = b >= 0 ? NavPathLength(dog, riverData._SliceBank(b)) : -1f;
            int code; float land;
            if (la >= 0f && (lb < 0f || la <= lb)) { code = a; land = la; }
            else if (lb >= 0f) { code = b; land = lb; }
            else
            {
                // neither side has a complete path: the bank point nearer the ball, walked straight (as the old fallback)
                code = a < 0 ? b : b < 0 ? a : (FlatDist(riverData._SliceBank(a), ball) <= FlatDist(riverData._SliceBank(b), ball) ? a : b);
                land = FlatDist(dog, riverData._SliceBank(code)) * 1.5f;
            }
            _wfEntryCode = code; _wfEntry = riverData._SliceBank(code); _wfPlanLand = land;
            _wfLimit = land / Mathf.Max(0.5f, runSpeed) + FlatDist(_wfEntry, ball) / WaterSpeed(w) + WaterSlack;
            Debug.Log("[KD_DogFetchController] river fetch dog=" + dogIndex + " entry=" + V3(_wfEntry) + " side=" + riverData._SliceSide(code)
                + " path=" + land.ToString("F1") + " limit=" + _wfLimit.ToString("F1") + " ball=" + V3(ball));
            return true;
        }

        /// <summary>E-3: the way out, at the pickup. The ok slice nearest the dog on the thrower's side (by the centre line at the
        /// thrower's z); none within reach there: the other side (the Drop at its return point follows, as for a far thrower);
        /// none at all: back where it came in. Sets the state-7 time limit.</summary>
        void RiverPlanExit(KD_DogWanderController w, Vector3 dog, Vector3 thrower)
        {
            int side = thrower.x >= riverData._CenterX(thrower.z) ? 1 : -1;
            float reach = riverData._BankReach();
            int code = riverData._NearestSlice(dog, side, reach, -1);
            if (code < 0) code = riverData._NearestSlice(dog, -side, reach, -1);
            if (code < 0) code = _wfEntryCode;
            if (code < 0) { _wfExit = _wfEntry; _wfExitReturn = _wfEntry; }
            else { _wfExit = riverData._SliceBank(code); _wfExitReturn = riverData._SliceReturn(code); }
            _wfExitCode = code;
            _wfCarryLimit = FlatDist(dog, _wfExit) / WaterSpeed(w) + WaterSlack;
            Debug.Log("[KD_DogFetchController] river exit plan dog=" + dogIndex + " exit=" + V3(_wfExit) + " side=" + (code < 0 ? 0 : riverData._SliceSide(code))
                + " throwerSide=" + side + " limit=" + _wfCarryLimit.ToString("F1"));
        }

        /// <summary>Codex review 2026-09-28 中 (two-stage contract): at the water, the limit becomes the time spent so far + the water
        /// distance to the ball now / water speed + WaterSlack (never shorter than the first plan), so a long run to a far bank
        /// while the ball drifts on is not counted against the dog.</summary>
        void RiverReplanAtEntry(KD_DogWanderController w, Vector3 dog, Vector3 ball, float elapsed)
        {
            _wfEnteredAt = elapsed;
            float lim = elapsed + FlatDist(dog, ball) / WaterSpeed(w) + WaterSlack;
            if (lim > _wfLimit) _wfLimit = lim;
            Debug.Log("[KD_DogFetchController] river entered dog=" + dogIndex + " after=" + elapsed.ToString("F1") + " water=" + FlatDist(dog, ball).ToString("F1")
                + " limit=" + _wfLimit.ToString("F1") + " first=" + _wfLimitFirst.ToString("F1"));
        }
        /// <summary>Speed in the water for the time contract: swim (swimmers) or walk, less the drift, at least 0.2 m/s.</summary>
        float WaterSpeed(KD_DogWanderController w)
        {
            float v = w._WaterCanSwim() ? w._WaterSwimSpeedNow() : walkSpeed;
            float drift = w.waterSpot != null ? w.waterSpot.floatDrift : 0f;
            return Mathf.Max(0.2f, v - drift);
        }

        /// <summary>Length of the complete NavMesh path a -> b (both sampled within 2 m); -1 when there is none.</summary>
        float NavPathLength(Vector3 a, Vector3 b)
        {
            NavMeshHit ha, hb; NavMeshPath path = new NavMeshPath();
            if (!NavMesh.SamplePosition(a, out ha, 2f, NavMesh.AllAreas) || !NavMesh.SamplePosition(b, out hb, 2f, NavMesh.AllAreas)) return -1f;
            if (!NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return -1f;
            Vector3[] c = path.corners;
            float len = 0f;
            for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
            return len;
        }

        float FlatDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        string V3(Vector3 v) { return "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ")"; }

        // ---- a Chase that meets the river band while its ball is still in the air (IMP-DOG-021) ----
        // Thrown from the bank edge, the dog can reach the band before the ball lands; the old rule gave up at once (river-band),
        // so the ball then floated with nobody after it. While the ball is still moving inside the band, the blocked dog waits (at
        // most RiverBlockGrace) for it to come down: a ball that floats is taken over by the intercept above; a ball at rest that
        // does not float (the band edge, a bridge zone) gives up as before (W5 w5-ball-band-edge: a frozen ball, no wait).
        const float RiverBlockGrace = 3f, RiverBlockMoving = 0.3f;
        float _rbwSince = -1f, _rbwMovedAt = -1000f;
        Vector3 _rbwPrev;
        bool _rbwValid;

        bool RiverBlockWait()
        {
            if (dogState != StateChase || _ballT == null || riverData == null || !riverData._FetchReady()) return false;
            Vector3 b = _ballT.position;
            if (!riverData._InBand(b, KominkaHillside.KH_RiverData.BandMargin)) return false;
            if (_rbwSince < 0f) { _rbwSince = Time.time; _rbwValid = false; }
            if (_rbwValid && Time.deltaTime > 0.0001f && Vector3.Distance(b, _rbwPrev) / Time.deltaTime > RiverBlockMoving) _rbwMovedAt = Time.time;
            bool first = !_rbwValid;
            _rbwPrev = b; _rbwValid = true;
            if (Time.time - _rbwSince > RiverBlockGrace) return false;
            return first || Time.time - _rbwMovedAt < 0.5f;
        }

        void RiverBlockClear() { _rbwSince = -1f; _rbwValid = false; }


        /// <summary>a and b on opposite banks of the river (by the river's centre line at each z); false where the data ends.</summary>
        bool WaterOtherBank(KD_DogWanderController w, Vector3 a, Vector3 b)
        {
            KominkaHillside.KH_RiverData rd = w.riverData;
            if (rd == null || !rd._Ready() || !rd._Covers(a.z) || !rd._Covers(b.z)) return false;
            return (a.x - rd._CenterX(a.z)) * (b.x - rd._CenterX(b.z)) < 0f;
        }

        float WaterTimeout(KD_DogWanderController w)
        {
            float v = Mathf.Max(0.2f, w._WaterSwimSpeedNow());
            return 1.5f * w.waterSpot._Diagonal() / v + 5f;
        }

        bool WaterPathToPlayer(Vector3 from, Vector3 to)
        {
            NavMeshHit a, b; NavMeshPath path = new NavMeshPath();
            if (!NavMesh.SamplePosition(from, out a, 2f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out b, 2f, NavMesh.AllAreas)) return false;
            return NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        void WaterGiveUp(string reason)
        {
            Debug.Log("[KD_DogFetchController] give-up dog=" + dogIndex + " state=" + dogState + " reason=" + reason);
            GiveUp();
        }

        // net: callable-ok: anyone may ask a wading dog to come out (the dog is shared, B-1 "誰の要求でも中断する");
        // the only effect is that the dog turns to the caller and leaves the water.
        [NetworkCallable(maxEventsPerSecond: 4)]
        public void WaterExitAsk()
        {
            if (!IsDogOwner()) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            KD_DogWanderController w = MyWander();
            if (w != null && Utilities.IsValid(caller)) w._WaterExitAsked(caller.playerId);
        }
    }
}
