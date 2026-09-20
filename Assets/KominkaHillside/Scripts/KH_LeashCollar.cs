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
        public SkinnedMeshRenderer ropeMesh;
        public Transform[] ropeBones;
        public Renderer snapVisual;
        public Renderer handleVisual;
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
            bool visible = connected && Utilities.IsValid(holder);
            bool modeled = ropeMesh != null && ropeBones != null && ropeBones.Length == 12;
            line.enabled = visible && !modeled;
            if (ropeMesh != null) ropeMesh.enabled = visible && modeled;
            if (snapVisual != null) snapVisual.enabled = visible && modeled;
            if (handleVisual != null) handleVisual.enabled = visible && modeled;
            if (!visible) return;
            // One grip per holder/hand; coincident copies for 2-3 dogs otherwise flicker in VR.
            if (handleVisual != null)
                for (int i=0;i<dogIndex;i++)
                    if (manager.walkers[i]==manager.walkers[dogIndex] && manager.hands[i]==manager.hands[dogIndex])
                        handleVisual.enabled=false;
            Vector3 a = manager._HandPosition(holder, manager.hands[dogIndex]);
            Vector3 b = anchor.position;
            if (modeled)
            {
                Quaternion handRotation = holder.IsUserInVR()
                    ? holder.GetTrackingData(manager.hands[dogIndex] == 0 ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand).rotation
                    : holder.GetRotation();
                UpdateModeledGeometry(a,b,handRotation);
                return;
            }
            UpdateGeometry(a,b,false);
        }
        private void UpdateModeledGeometry(Vector3 a, Vector3 b, Quaternion handRotation)
        {
                if (handleVisual != null)
                {
                    handleVisual.transform.SetPositionAndRotation(a, handRotation);
                    a = handleVisual.transform.TransformPoint(new Vector3(0f, -0.077f, 0f));
                }
                Vector3 direction = b-a;
                if (direction.sqrMagnitude < 0.000001f) direction = Vector3.up;
                if (snapVisual != null)
                {
                    Quaternion rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
                    snapVisual.transform.SetPositionAndRotation(b-rotation*new Vector3(0f,.045f,0f),rotation);
                    b=snapVisual.transform.TransformPoint(new Vector3(0f,-.047f,0f));
                }
            UpdateGeometry(a,b,true);
        }
        private void UpdateGeometry(Vector3 a, Vector3 b, bool modeled)
        {
            if(modeled)
            {
                ropeMesh.transform.position=(a+b)*.5f;
                ropeMesh.localBounds=new Bounds(Vector3.zero,Vector3.one*(Vector3.Distance(a,b)+1f));
            }
            float slack = Mathf.Clamp(manager.leashLength - Vector3.Distance(a,b), 0f, 0.8f);
            for (int i = 0; i < 12; i++)
            {
                float t = i / 11f;
                Vector3 point = Vector3.Lerp(a,b,t) - Vector3.up * (Mathf.Sin(t * Mathf.PI) * slack * 0.3f);
                if (modeled)
                {
                    Vector3 tangent = b-a-Vector3.up*(Mathf.Cos(t*Mathf.PI)*Mathf.PI*slack*.3f);
                    if (tangent.sqrMagnitude < .000001f) tangent = Vector3.forward;
                    ropeBones[i].SetPositionAndRotation(point,Quaternion.LookRotation(tangent,Vector3.right));
                }
                else line.SetPosition(i,point);
            }
        }
    }
}
