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
    // Written only by the dog Owner (KD_DogWanderController.HillsideWater) and serialized only when it changes. Every
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
            if (pose == waterPose || !IsDogOwner()) return;
            waterPose = pose;
            RequestSerialization();
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

        // ---- a throw while the dog is in the water (DES-DOG-004 B-1: exit first, the Fetch starts after landing, 10 s max) ----
        const float WaterThrowHold = 10f;
        int _waterThrowPlayer = -1;
        float _waterThrowAt;

        /// <summary>From StartFetch (Owner). True = the throw is held (the dog walks out first); false = start as usual.</summary>
        bool WaterHoldsThrow(int playerId)
        {
            if (waterPose == 0) return false;
            if (_waterThrowPlayer < 0)
            {
                _waterThrowPlayer = playerId; _waterThrowAt = Time.time;
                Debug.Log("[KD_DogFetchController] throw held dog=" + dogIndex + " (in the water)");
            }
            KD_DogWanderController w = MyWander();
            if (w != null) w._WaterExitAsked(playerId);
            return true;
        }

        /// <summary>Top of Update (Owner): start the held throw once landed, drop it after 10 s or when the ball was taken.</summary>
        void WaterThrowTick()
        {
            if (_waterThrowPlayer < 0) return;
            if (!_dogOwner || Time.time - _waterThrowAt > WaterThrowHold || (_ballPickup != null && _ballPickup.IsHeld))
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
        /// <summary>6-8: the dog's legs are the Wander's (_WaterFetchStep), so the Wander's "Fetch busy" branch must keep its
        /// _speed (run 20260927-095400: zeroed every frame, the W3 dogs crawled at one frame's acceleration, 0.05-0.09 m/s).</summary>
        public bool IsWaterFetching { get { return dogState >= StateWaterChase; } }
        const float WaterMouthReach = 0.25f, WaterExitStand = 1.2f;
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
            if (_ballPickup != null && _ballPickup.IsHeld) { GiveUp(); return true; }
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(attentionPlayerId);
            if (!Utilities.IsValid(p) || _ballT == null) { GiveUp(); return true; }
            float elapsed = Time.time - _stateEnteredTime;
            Vector3 dog = dogRoot.position;
            if (dogState == StateWaterChase)
            {
                Vector3 ball = _ballT.position;
                if (!w.waterSpot._FloatsAt(ball)) { TransitionTo(StateChase); w._InvalidateLeashAnimation(); return true; }   // back on land: land chase
                w._WaterFetchStep(ball, false);
                Vector3 mouth = mouthAnchor != null ? mouthAnchor.position : dog;
                if ((mouth - ball).magnitude <= WaterMouthReach) { _waterExitIdx = w.waterSpot._NearestExit(dog); TransitionTo(StateWaterReturn); return true; }
                if (elapsed > WaterTimeout(w)) WaterGiveUp("water-timeout");
                return true;
            }
            if (dogState == StateWaterReturn)
            {
                if (_waterExitIdx < 0 || _waterExitIdx >= w.waterSpot._ExitCount()) _waterExitIdx = w.waterSpot._NearestExit(dog);
                Vector3 exit = w.waterSpot._Exit(_waterExitIdx);
                w._WaterFetchStep(exit, true);
                Vector3 d = exit - dog; d.y = 0f;
                if (d.magnitude < 0.4f && w.waterSpot._Depth(dog) <= 0.02f) { TransitionTo(StateWaterExit); return true; }
                if (elapsed > WaterTimeout(w)) WaterGiveUp("water-timeout");
                return true;
            }
            // StateWaterExit: landed with the ball, stand, then the land Return (or a Drop here when the thrower is out of reach)
            w._WaterFetchHold(true);
            if (elapsed < WaterExitStand) return true;
            bool reachable = WaterPathToPlayer(dog, p.GetPosition());
            if (!reachable) Debug.Log("[KD_DogFetchController] water exit dog=" + dogIndex + " thrower out of reach: drop at the exit");
            w._InvalidateLeashAnimation();
            TransitionTo(reachable ? StateReturn : StateDrop);
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
