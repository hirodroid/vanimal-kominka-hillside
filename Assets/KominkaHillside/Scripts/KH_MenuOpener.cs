using UdonSharp;
using UnityEngine;

namespace KominkaHillside
{
    // Interact plate at each leash station: opens the hand menu without the right-stick gesture (one-handed VR, seated).
    // Menu rebuild stage 0-b (D-1 / D-6): the hand menu is KominkaDogRun's KD_TreatPouch; Interact -> pouch._Toggle().
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_MenuOpener : UdonSharpBehaviour
    {
        public KominkaDogRun.KD_TreatPouch pouch;
        public override void Interact() { if (pouch != null) pouch._Toggle(); }
    }
}
