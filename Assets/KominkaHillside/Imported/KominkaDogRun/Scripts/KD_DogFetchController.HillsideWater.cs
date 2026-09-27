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
                    if (!ww._WaterCanSwim() && ww.waterSpot._Depth(_ballT.position) > ww._WaterWadeLimit())
                    {
                        Debug.Log("[KD_DogFetchController] throw dropped dog=" + dogIndex + " reason=deep-ball (in the water)");
                        return;
                    }
                    bool direct = false;
                    if (Utilities.IsValid(VRCPlayerApi.GetPlayerById(thrower)))
                    {
                        _waterDirect = true; direct = StartFetch(thrower); _waterDirect = false;
                        if (direct) { ww._InvalidateLeashAnimation(); TransitionTo(StateWaterChase); }
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

        /// <summary>From Update (after the Idle branch). True = this frame is handled here.</summary>
        bool WaterFetchTick()
        {
            if (dogState == StateChase && _dogOwner && WaterFetchIntercept()) return true;
            if (dogState < StateWaterChase) return false;
            KD_DogWanderController w = MyWander();
            if (w == null || w.waterSpot == null) { if (_dogOwner) GiveUp(); return true; }
            bool carrying = dogState != StateWaterChase;
            if (!_dogOwner) { w._WaterFetchHold(carrying); return true; }   // legs from the observed speed; the root is synced
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
                // RC-17: reached the ball, waiting for its ownership (at most 2 s): stay on it, then 7 frozen, or give up
                if (_claimNext >= 0) { w._WaterFetchHold(false); ClaimTick(); return true; }
                Vector3 ball = _ballT.position;
                if (!w.waterSpot._FloatsAt(ball)) { TransitionTo(StateChase); w._InvalidateLeashAnimation(); return true; }   // back on land: land chase
                w._WaterFetchStep(ball, false);
                // Horizontal reach (mouth, or the dog standing over the ball) and the ball not above the mouth: a wading Lab's
                // mouth is ~0.45 m above a ball at the water line, so a 3D 0.25 m never held (run 20260927-102801, W3-Lab-shallow
                // stood on the ball for 90 s). The ball then sits in the mouth (LateUpdate glue), as on land.
                Vector3 mouth = mouthAnchor != null ? mouthAnchor.position : dog;
                Vector3 dm = mouth - ball; float above = dm.y; dm.y = 0f;
                Vector3 dr = dog - ball; dr.y = 0f;
                if ((dm.magnitude <= WaterMouthReach || dr.magnitude <= WaterMouthReach) && above > -0.1f && above < WaterMouthAboveMax)
                { _waterExitIdx = w.waterSpot._NearestExit(dog); _waterToReturn = false; BeginCarry(StateWaterReturn); return true; }
                if (elapsed > WaterTimeout(w)) WaterGiveUp("water-timeout");
                return true;
            }
            if (dogState == StateWaterReturn)
            {
                if (_waterExitIdx < 0 || _waterExitIdx >= w.waterSpot._ExitCount()) _waterExitIdx = w.waterSpot._NearestExit(dog);
                Vector3 exit = w.waterSpot._Exit(_waterExitIdx);
                w._WaterFetchStep(exit, true);
                Vector3 d = exit - dog; d.y = 0f;
                if (d.magnitude < 0.4f && w.waterSpot._Depth(dog) <= 0.02f) { _waterToReturn = false; TransitionTo(StateWaterExit); return true; }
                if (elapsed > WaterTimeout(w)) WaterGiveUp("water-timeout");
                return true;
            }
            // StateWaterExit: landed with the ball, stand, then the land Return to a thrower on this bank. A thrower across the river
            // (the land Return walks straight and would stop at the band, even with a bridge path) or out of reach gets the ball
            // dropped at this bank's return point (design B-1; Codex 2026-09-27 D-中: not at the exit, not a Return).
            if (_waterToReturn)
            {
                if (_waterExitIdx < 0 || _waterExitIdx >= w.waterSpot._ExitCount()) _waterExitIdx = w.waterSpot._NearestExit(dog);
                Vector3 rp = w.waterSpot._ReturnPointFor(_waterExitIdx);
                w._WaterFetchWalkTo(rp);
                Vector3 dd = rp - dog; dd.y = 0f;
                if (dd.magnitude < 0.5f || Time.time - _waterToReturnAt > WaterToReturnMax)
                {
                    _waterToReturn = false; w._InvalidateLeashAnimation(); TransitionTo(StateDrop);
                }
                return true;
            }
            w._WaterFetchHold(true);
            if (elapsed < WaterExitStand) return true;
            Vector3 thrower = p.GetPosition();
            bool farBank = WaterOtherBank(w, dog, thrower);
            if (farBank || !WaterPathToPlayer(dog, thrower))
            {
                Debug.Log("[KD_DogFetchController] water exit dog=" + dogIndex + " thrower " + (farBank ? "across the river" : "out of reach") + ": drop at the return point");
                _waterToReturn = true; _waterToReturnAt = Time.time;
                return true;
            }
            w._InvalidateLeashAnimation();
            TransitionTo(StateReturn);
            return true;
        }

        /// <summary>Chase (Owner): the ball floats in the spot -> into the water, or give up for a large dog and a deep ball.</summary>
        bool WaterFetchIntercept()
        {
            if (_ballT == null) return false;
            KD_DogWanderController w = MyWander();
            if (w == null || w.waterSpot == null || !w.waterSpot._Ready()) return false;
            Vector3 ball = _ballT.position;
            if (!w.waterSpot._FloatsAt(ball)) return false;
            if (!w._WaterCanSwim() && w.waterSpot._Depth(ball) > w._WaterWadeLimit())
            {
                Debug.Log("[KD_DogFetchController] give-up dog=" + dogIndex + " state=" + dogState + " reason=deep-ball");
                GiveUp();
                return true;
            }
            w._InvalidateLeashAnimation();   // the wander legs take over the Animator in the water states
            TransitionTo(StateWaterChase);
            return true;
        }

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
