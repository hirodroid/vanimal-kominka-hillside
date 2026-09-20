using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    // Interact board: tells the dogs I am leashing where to go. Provisional until DES-UI-001's menu exists.
    // Feedback is local: the board flashes only when the request had at least one of my dogs to apply to.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_WalkSign : UdonSharpBehaviour
    {
        public KH_LeashManager manager;
        public int destination; // index into KH_WalkRoute.destinationNodes
        public Renderer board;
        public Material normalMaterial;
        public Material flashMaterial;

        public override void Interact()
        {
            if (manager == null) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;
            int mine = 0;
            for (int i = 0; i < manager.walkers.Length; i++) if (manager.walkers[i] == local.playerId) mine++;
            if (mine == 0) return; // nothing to tell; no flash, so the miss is visible
            manager._RequestDestination(destination);
            if (board != null && flashMaterial != null) { board.sharedMaterial = flashMaterial; SendCustomEventDelayedSeconds(nameof(_EndFlash), 0.5f); }
        }

        public void _EndFlash() { if (board != null && normalMaterial != null) board.sharedMaterial = normalMaterial; }
    }
}
