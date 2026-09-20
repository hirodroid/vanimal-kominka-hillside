using UdonSharp;
using UnityEngine;

namespace KominkaHillside
{
    // Interact plate at each leash station: opens the hand menu without the right-stick gesture (one-handed VR, seated).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_MenuOpener : UdonSharpBehaviour
    {
        public KH_HandMenu menu;
        public override void Interact() { if (menu != null) menu._Toggle(); }
    }
}
