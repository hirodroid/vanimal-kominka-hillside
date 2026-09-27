using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    // IMP-DOG-020 step 3 (DES-DOG-004 B-1): a wading dog is not leashed. The arbiter (this Owner) reads the synced
    // waterPose, because WaterPlay itself is local to the dog's Owner. The menu shows "水遊び中 — 上がったらつなげます" and
    // asks the dog to come out (KD_TreatPouch.Water). A clip attached by hand reaches ReqAttach only, so the refusal there
    // also asks the dog to come out, turning to that player (_AskWaterExitFor). Attaching again after landing is accepted.
    //
    // RC-14 (破壊監査 2026-09-28, FUNC-04 BUG-02): the pose alone let an attach through in the frame the dog stepped in (the pose
    // was written before the move) and on another client whose pose arrived late. The arbiter now also asks the depth under
    // the dog's synced root (the spot's riverbed grid, KD_DogFetchController._WetAtRoot). An attach that still gets through
    // is caught by the dog Owner, which asks for its release here (ReqWaterRelease) and walks out.
    public partial class KH_LeashManager
    {
        public bool _InWater(int dog)
        {
            if (fetchers == null || dog < 0 || dog >= fetchers.Length || fetchers[dog] == null) return false;
            return fetchers[dog].WaterPose != 0 || fetchers[dog]._WetAtRoot();
        }

        /// <summary>Dog Owner (KD_DogWanderController.WaterLeashTick): the connection it first saw while the dog was in the water.</summary>
        public void _RequestWaterRelease(int dog, int holder)
        {
            if (!Utilities.IsValid(Networking.LocalPlayer)) return;
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ReqWaterRelease), dog, holder);
        }

        // net: callable-ok: only the dog's own Owner is accepted (the one who moves it and saw it wet), and the only effect is
        // releasing that one dog, which the water rule refuses anyway (a wading dog is not leashed, B-1).
        // Codex review 2026-09-28 高: a request names the connection it vetoed (holder) and counts only while the dog is still in
        // the water, so a late duplicate cannot release a new connection made after landing (even by the same player).
        [NetworkCallable(maxEventsPerSecond: 6)]
        public void ReqWaterRelease(int dog, int holder)
        {
            if (!Networking.IsOwner(gameObject) || dog < 0 || dog >= walkers.Length || dog >= dogRoots.Length || dogRoots[dog] == null) return;
            if (walkers[dog] < 0 || walkers[dog] != holder || !_InWater(dog)) return;   // the dog retries once a second while it is wet
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            VRCPlayerApi dogOwner = Networking.GetOwner(dogRoots[dog].gameObject);
            if (!Utilities.IsValid(caller) || !Utilities.IsValid(dogOwner) || caller.playerId != dogOwner.playerId) return;
            Debug.Log("[KH_Leash] ReqWaterRelease dog" + dog + " holder=" + walkers[dog] + " by dog owner " + caller.playerId);
            // Release() makes the dog's current position its roaming home; here that is in the water, so keep the home it had
            // (where it was last let go, beside the spot), and the dog roams from the bank once it has walked out
            Vector3 home = releasedHomes[dog];
            Release(dog);
            releasedHomes[dog] = home;
            ApplyHomes();
            RequestSerialization();
        }
    }
}
