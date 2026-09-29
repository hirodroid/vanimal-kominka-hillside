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
            if (!visible) { _shownValid = false; return; }
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
                    int holderId = manager.walkers[dogIndex];
                    // A new connection or a change of hand/holder starts fresh: path chosen with the entry
                    // thresholds, and the first frame is placed on the target (no blend from an old pose).
                    bool fresh = !_shownValid || holderId != _shownHolder || hand != _shownHand;
                    if (fresh) { _pathFingers = false; _blendStartAt = -1000f; }
                    rotation = HandleRotation(holder, hand, handRotation, a);
                    // Put the handle's local (0, +handleGripOffset, 0) - the rear belt of the loop - on the grip
                    // point (the knuckle line when the avatar has finger bones, else the tracked hand), so the loop
                    // and the rope socket sit in front of the fist instead of straddling it.
                    a = _grip - rotation * new Vector3(0f, manager.handleGripOffset, 0f);
                    // Path switch (finger bones <-> tracking fallback): ease from the pose shown last frame over
                    // HandlePathBlend instead of jumping in one frame. A switch during a blend starts from the
                    // blended pose, so it never jumps either.
                    if (!fresh && _pathFingers != _shownFingers)
                    { _blendFromPos = _shownPos; _blendFromRot = _shownRot; _blendStartAt = Time.time; }
                    float blend = (Time.time - _blendStartAt) / HandlePathBlend;
                    if (!fresh && blend < 1f)
                    {
                        a = Vector3.Lerp(_blendFromPos, a, blend);
                        rotation = Quaternion.Slerp(_blendFromRot, rotation, blend);
                    }
                    _shownValid = true; _shownHolder = holderId; _shownHand = hand; _shownFingers = _pathFingers;
                    _shownPos = a; _shownRot = rotation;
                }
                // Desktop/touch has no tracked hand: keep the body rotation the handle has always used.
                else { rotation = holder.GetRotation(); _shownValid = false; }
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
        // hands' reference frames are mirror images of each other.
        //
        // 2026-09-29 (BUG-DOG-002, 2nd pass): the ring plane and the grip point come from the avatar's
        // bones when it has finger bones, not from the tracking rotation. Reason: the 9/20 report ("left
        // for both hands" while the handle used the tracking rotation as is) suggests the two hands' tracking
        // bases are NOT anatomical mirror images, and which of the palm normal / knuckle line the tracking
        // +Y follows is not known, so a tracking-based ring plane could be 90 degrees off per hand.
        //   rope axis   f = LowerArm -> Hand (unchanged): the socket (local -Y) looks along it.
        //   ring plane  = span(f, knuckle line), knuckle line k = LittleProximal - IndexProximal: the rear
        //                 belt (local X) runs across the palm the way fingers close around a loop handle.
        //                 Ring normal (local +Z) = cross(f, k), i.e. the palm normal (back of the right hand,
        //                 palm of the left; the loop is symmetric in Z so the sign does not matter). k is the
        //                 mirror image between the hands, so the mirrored roll below stays correct.
        //                 Wrist flexion turns the hand about k itself, so k stays across f (no degenerate case
        //                 like hand-up had); only ulnar/radial deviation (about 30 degrees at most) tilts it.
        //   grip point  = the knuckle line's midpoint (MCP joints), so the belt sits in the closed fingers and
        //                 the loop and socket are in front of the fist, with no tracking-point offset involved.
        //                 Each client reads the bones of the avatar as it animates it locally (remote holders
        //                 through their network IK), so viewers see close but not identical poses.
        // No finger bones / k too short or nearly along f: the previous tracking-based path (hand +Y with the
        // flex blend, grip = tracked hand point) is kept as the fallback. Its GetTrackingData is the holder's own
        // trackers on the holder's client and bone-derived on everyone else's, so it is not identical across
        // viewers either. Result in _grip (U# has no out).
        //
        // Path choice has hysteresis (Codex review 2026-09-29), kept per collar = per holder/hand since a collar
        // is held by one hand at a time and a change of holder/hand resets it: enter the finger path at a knuckle
        // span > FingerEnterSpan and |dot(k, f)| < FingerEnterDot, leave it at <= FingerExitSpan or >= FingerExitDot.
        const float FingerEnterSpan = 0.025f, FingerExitSpan = 0.02f;
        const float FingerEnterDot = 0.7f, FingerExitDot = 0.8f;
        const float HandlePathBlend = 0.15f;   // s, pose ease when the path switches
        private Vector3 _grip;
        private bool _pathFingers;                 // current path (finger bones), per the hysteresis above
        private bool _shownValid, _shownFingers;   // last frame's shown VR pose and the path it came from
        private int _shownHolder = -1, _shownHand = -1;
        private Vector3 _shownPos, _blendFromPos;
        private Quaternion _shownRot, _blendFromRot;
        private float _blendStartAt = -1000f;
        private Quaternion HandleRotation(VRCPlayerApi holder, int hand, Quaternion handRotation, Vector3 trackedHand)
        {
            bool left = hand == 0;
            Vector3 handBone = holder.GetBonePosition(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Vector3 armBone = holder.GetBonePosition(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Vector3 f = handBone - armBone;
            // Same zero guard the ported dog scripts use on GetBonePosition (op_Inequality is the exposed one).
            bool haveBones = handBone != Vector3.zero && armBone != Vector3.zero;
            // No bone data on this avatar, or a forearm shorter than 5 cm: fall back to where the hand points
            // (this codebase reads hand tracking +Z as the pointing direction, see KH_Frisbee.AimDirection).
            // 5 cm is below any human forearm, so it only ever catches collapsed or absent bone data.
            if (!haveBones || f.sqrMagnitude < 0.0025f)
            {
                haveBones = false;
                f = handRotation * Vector3.forward;
            }
            Vector3 fn = f.normalized;
            _grip = trackedHand;
            Vector3 u;
            Vector3 index = haveBones ? holder.GetBonePosition(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal) : Vector3.zero;
            Vector3 little = haveBones ? holder.GetBonePosition(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal) : Vector3.zero;
            Vector3 k = little - index;
            // Spans of 2-2.5 cm are below any knuckle span (index to little is about 6-8 cm); |dot| 0.7-0.8 = within
            // about 37-46 degrees of the rope axis, which a hand cannot reach by deviation alone (odd bone data only).
            float span = k.magnitude;
            float along = span > 0.0001f ? Mathf.Abs(Vector3.Dot(k / span, fn)) : 1f;
            bool bonesOk = index != Vector3.zero && little != Vector3.zero;
            _pathFingers = bonesOk && (_pathFingers
                ? span > FingerExitSpan && along < FingerExitDot
                : span > FingerEnterSpan && along < FingerEnterDot);
            if (_pathFingers)
            {
                u = Vector3.Cross(fn, k);
                _grip = (index + little) * 0.5f;
            }
            else
            {
                u = handRotation * Vector3.up;
                // Flexing the wrist turns hand-up toward the forearm, and the part of it LookRotation can use
                // shrinks to nothing. Blend the reference toward hand-right instead of switching at a threshold:
                // a hard switch made the ring normal jump 90 degrees across the boundary. |dot| 0.9 is about
                // 64 degrees of flex, where the projection starts to get short; 0.995 is about 84 degrees, where
                // it is unusable; past about 72 degrees the reference is mostly hand-right. Both references come
                // from the same orthonormal basis, so the slerp stays unit length and cannot produce NaN.
                float parallel = Mathf.Abs(Vector3.Dot(u, fn));
                float t = Mathf.InverseLerp(0.9f, 0.995f, parallel);
                if (t > 0f) u = Vector3.Slerp(u, handRotation * Vector3.right, t);
            }
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
