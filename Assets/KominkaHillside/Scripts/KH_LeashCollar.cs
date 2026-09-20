using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_LeashCollar : UdonSharpBehaviour
    {
        public KH_LeashManager manager;
        public int dogIndex;
        public LineRenderer line;
        public Transform anchor;
        private int _previousWalker = -1;
        public override void Interact() { if (manager != null) manager._RequestDetach(dogIndex); }
        public override void PostLateUpdate()
        {
            if (line == null || manager == null) return;
            bool connected = manager._IsLeashed(dogIndex);
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!connected && Utilities.IsValid(local) && _previousWalker == local.playerId)
                manager._OfferTip(anchor.position + Vector3.up * 0.1f, manager.hands[dogIndex]);
            _previousWalker = manager.walkers[dogIndex];
            VRCPlayerApi holder = connected ? VRCPlayerApi.GetPlayerById(manager.walkers[dogIndex]) : null;
            line.enabled = connected && Utilities.IsValid(holder);
            if (!line.enabled) return;
            Vector3 a = manager._HandPosition(holder, manager.hands[dogIndex]);
            Vector3 b = anchor.position;
            float slack = Mathf.Clamp(manager.leashLength - Vector3.Distance(a,b), 0f, 0.8f);
            for (int i = 0; i < 12; i++)
            {
                float t = i / 11f;
                Vector3 point = Vector3.Lerp(a,b,t) - Vector3.up * (Mathf.Sin(t * Mathf.PI) * slack * 0.3f);
                line.SetPosition(i,point);
            }
        }
    }
}
