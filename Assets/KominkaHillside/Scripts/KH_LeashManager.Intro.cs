using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    public partial class KH_LeashManager
    {
        public UdonSharpBehaviour walkIntro;
        private bool _introReceived;
        public bool _IntroStateReady() { return _introReceived || (Networking.IsOwner(gameObject) && homesInitialized); }
        private void IntroChanged() { if (walkIntro != null) walkIntro.SendCustomEvent("_OnLeashChanged"); }

        // Read-only mirror of ReqAttach's gates. In particular, asking this must NOT summon a swimming dog.
        public bool _CanIntroAttach(int dog, int hand)
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local) || hand < 0 || hand > 1 || !_IntroDogAvailable(dog)) return false;
            if (Vector3.Distance(local.GetPosition(), dogRoots[dog].position) > attachReach) return false;
            return _MyCount(hand) < 3;
        }
        // Availability around the entrance is wider than a player's immediate attachment reach.
        public bool _IntroDogAvailable(int dog)
        {
            if (dogRoots == null || dog < 0 || dog >= dogRoots.Length) return false;
            if (dogRoots[dog] == null || collarAnchors == null || dog >= collarAnchors.Length || collarAnchors[dog] == null || _IsLeashed(dog)) return false;
            if (fetchers == null || dog >= fetchers.Length || fetchers[dog] == null || !fetchers[dog].IsIdle || _IsGathering(dog) || _InWater(dog)) return false;
            if (frisbeeCatchers != null && dog < frisbeeCatchers.Length && frisbeeCatchers[dog] != null && frisbeeCatchers[dog].IsEngaged) return false;
            if (sleepBoard != null && sleepBoard._IsCalled(dog)) return false;
            if (WashHeld(dog)) return false;
            return true;
        }
    }
}
