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
        public override void Interact() { if (manager != null) manager._RequestDetach(dogIndex); }
        public override void PostLateUpdate()
        {
            if (line == null || manager == null) return;
            bool connected = manager._IsLeashed(dogIndex);
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
                Quaternion rotation;
                if (holder.IsUserInVR())
                {
                    int hand = manager.hands[dogIndex];
                    Quaternion handRotation = holder.GetTrackingData(hand == 0
                        ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand).rotation;
                    rotation = HandleRotation(holder, hand, handRotation);
                    // Put the handle's local (0, +handleGripOffset, 0) - the rear belt of the loop - on the hand
                    // point, so the loop and the rope socket sit in front of the fist instead of straddling it.
                    a = a - rotation * new Vector3(0f, manager.handleGripOffset, 0f);
                }
                // Desktop/touch has no tracked hand: keep the body rotation the handle has always used.
                else rotation = holder.GetRotation();
                UpdateModeledGeometry(a,b,rotation);
                return;
            }
            UpdateGeometry(a,b,false);
        }
        // Orients the handle so the rope leaves it along the forearm, in front of the hand (BUG-DOG-002).
        // Axis map of Quaternion.Euler(-90,0,0) (checked numerically): local +Y -> -Z, local -Y -> +Z,
        // local +Z -> +Y, local +X -> +X. The mesh keeps the rope socket on local -Y and the ring plane
        // normal on local +Z, so after LookRotation(f, u) the socket looks along f, and the ring normal
        // lands on u projected onto the plane perpendicular to f (LookRotation keeps only that part of
        // the up reference); the roll is then applied about f itself. The loop is symmetric in X and Z,
        // so the mesh needs no left/right mirroring - only the roll's sign flips, which assumes the two
        // hands' tracking bases are mirror images of each other (if a hand comes out rolled the wrong
        // way in PCVR, that assumption is what to question, not the mesh).
        private Quaternion HandleRotation(VRCPlayerApi holder, int hand, Quaternion handRotation)
        {
            Vector3 handBone = holder.GetBonePosition(hand == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Vector3 armBone = holder.GetBonePosition(hand == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Vector3 f = handBone - armBone;
            // Same zero guard the ported dog scripts use on GetBonePosition (op_Inequality is the exposed one).
            bool haveBones = handBone != Vector3.zero && armBone != Vector3.zero;
            // No bone data on this avatar, or a forearm shorter than 5 cm: fall back to where the hand points
            // (this codebase reads hand tracking +Z as the pointing direction, see KH_Frisbee.AimDirection).
            // 5 cm is below any human forearm, so it only ever catches collapsed or absent bone data.
            if (!haveBones || f.sqrMagnitude < 0.0025f)
                f = handRotation * Vector3.forward;
            Vector3 fn = f.normalized;
            Vector3 u = handRotation * Vector3.up;
            // Flexing the wrist turns hand-up toward the forearm, and the part of it LookRotation can use
            // shrinks to nothing. Blend the reference toward hand-right instead of switching at a threshold:
            // a hard switch made the ring normal jump 90 degrees across the boundary. |dot| 0.9 is about
            // 64 degrees of flex, where the projection starts to get short; 0.995 is about 84 degrees, where
            // it is unusable; past about 72 degrees the reference is mostly hand-right. Both references come
            // from the same orthonormal basis, so the slerp stays unit length and cannot produce NaN.
            float parallel = Mathf.Abs(Vector3.Dot(u, fn));
            float t = Mathf.InverseLerp(0.9f, 0.995f, parallel);
            if (t > 0f) u = Vector3.Slerp(u, handRotation * Vector3.right, t);
            Quaternion q = Quaternion.LookRotation(fn, u);
            // Roll of the ring plane about the rope axis (q's +Z = fn), mirrored so both hands feel the same.
            q = q * Quaternion.AngleAxis(hand == 0 ? -manager.handleRollDegrees : manager.handleRollDegrees, Vector3.forward);
            return q * Quaternion.Euler(-90f, 0f, 0f);
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
