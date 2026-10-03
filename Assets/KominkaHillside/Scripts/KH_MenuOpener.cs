using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    // Interact plate at each leash station: opens the hand menu without the right-stick gesture (one-handed VR, seated).
    // Menu rebuild stage 0-b (D-1 / D-6): the hand menu is KominkaDogRun's KD_TreatPouch; Interact -> pouch._Toggle().
    // IMP-UI-002 (docs/plans/2026-10-03-onsite-guides.md §2.6): the plate also picks its station's walk strip variant once at
    // Start - VR (take the tip by hand) or flat (Desktop / phone: the menu) - by showing one group and hiding the other. Local
    // only, no sync, no per-frame work. Both arrays may be null or hold nulls (stations without a strip, older scenes).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_MenuOpener : UdonSharpBehaviour
    {
        public KominkaDogRun.KD_TreatPouch pouch;
        [Tooltip("IMP-UI-002: shown only in VR (this station's WalkGuide/Canvas/Vr). Written by Guide/11")]
        public GameObject[] vrOnly;
        [Tooltip("IMP-UI-002: shown only on Desktop / phone (this station's WalkGuide/Canvas/Flat). Written by Guide/11")]
        public GameObject[] flatOnly;

        public override void Interact() { if (pouch != null) pouch._Toggle(); }

        void Start() { _ApplyDevice(); }

        /// <summary>Shows the walk strip variant of this device (VR or not; a player not valid yet counts as not VR). Local only.</summary>
        public void _ApplyDevice()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            bool vr = Utilities.IsValid(lp) && lp.IsUserInVR();
            SetAll(vrOnly, vr);
            SetAll(flatOnly, !vr);
        }

        private void SetAll(GameObject[] list, bool on)
        {
            if (list == null) return;
            for (int i = 0; i < list.Length; i++)
                if (list[i] != null && list[i].activeSelf != on) list[i].SetActive(on);
        }
    }
}
