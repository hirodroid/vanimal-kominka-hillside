using UdonSharp;
using UnityEngine;
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
