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
        // IMP-DOG-034 A4: which renderers were last switched on (-1 = not written yet, so the first frame always writes).
        private const int DrawnHidden = 0, DrawnLine = 1, DrawnModeled = 2, DrawnModeledNoHandle = 3;
        private int _drawn = -1;
        public override void PostLateUpdate()
        {
            if (line == null || manager == null) return;
            // IMP-DOG-034 A4: the manager's synced arrays are read once per frame (each cross-behaviour read is an
            // extern). Same array references as _IsLeashed / walkers[dogIndex] / hands[dogIndex], so same values.
            int[] walkers = manager.walkers;
            int holderId = dogIndex >= 0 && dogIndex < walkers.Length ? walkers[dogIndex] : -1;
            VRCPlayerApi holder = holderId >= 0 ? VRCPlayerApi.GetPlayerById(holderId) : null;
            bool visible = holderId >= 0 && Utilities.IsValid(holder);
            if (!visible)
            {
                _shownValid = false;
                // IMP-DOG-034 A4: all four renderers off, written only when that differs from the last write.
                if (_drawn != DrawnHidden) ApplyDrawn(DrawnHidden);
                return;
            }
            int[] hands = manager.hands;
            int hand = hands[dogIndex];
            // IMP-DOG-034 C1-rope (3): optional straight two-point line, OFF by default (lightRope false), so the
            // default look is untouched. Real 2-point path: no 12-point curve, no hand pose, no handle/snap/rope mesh.
            if (lightRope)
            {
                if (_drawn != DrawnLine) ApplyDrawn(DrawnLine);
                // Back in full mode the handle starts fresh (no blend from a pose shown before light mode).
                _shownValid = false;
                if (!_lineShort) { line.positionCount = 2; _lineShort = true; }
                line.SetPosition(0, manager._HandPosition(holder, hand));
                line.SetPosition(1, anchor.position);
                return;
            }
            bool modeled = ropeMesh != null && ropeBones != null && ropeBones.Length == 12;
            int drawn = modeled ? DrawnModeled : DrawnLine;
            // One grip per holder/hand; coincident copies for 2-3 dogs otherwise flicker in VR.
            // IMP-DOG-034 A4: decided before writing (was: write on, then off), so the final state is the same.
            // IMP-DOG-034 C1-rope (2): remembers which collar shows the grip (the lowest dogIndex on this holder/hand).
            int gripOwner = -1;
            if (modeled && handleVisual != null)
                for (int i=0;i<dogIndex;i++)
                    if (walkers[i]==holderId && hands[i]==hand) { drawn = DrawnModeledNoHandle; gripOwner = i; break; }
            if (_drawn != drawn) ApplyDrawn(drawn);
            Vector3 b = anchor.position;
            if (modeled)
            {
                // IMP-DOG-034 C1-rope (2): a collar whose own handle is hidden only needs the rope start, i.e. the
                // socket of the grip that IS shown. It asks that collar (which computes its pose at most once per
                // frame, whichever of the two runs first) instead of recomputing the same hand's tracking, four
                // finger/arm bones, HandleRotation, the blend and its own hidden handle's transform. The visible
                // grip and the rope it draws are computed exactly as before; only the hidden duplicates are gone.
                KH_LeashCollar owner = null;
                if (gripOwner >= 0) owner = PeerCollar(gripOwner);
                Vector3 start;
                if (owner != null)
                {
                    // Not the shown grip: if this collar becomes the grip owner later it starts fresh.
                    _shownValid = false;
                    start = owner._SharedRopeStart();
                }
                else if (_poseFrame == Time.frameCount) start = _poseRopeStart;   // a later dog on this hand ran first
                else start = PoseHandle(holder, holderId, hand);
                UpdateRopeFrom(start, b);
                return;
            }
            UpdateGeometry(manager._HandPosition(holder, hand),b,false);
        }
        // IMP-DOG-034 C1-rope (3): straight two-point rope instead of the 12-bone curve. Default false: the default
        // look does not change. Set it on all six collars together (a full-mode rope reads its hand's grip owner).
        // Turning it off restores the full rope on the next frame: ApplyDrawn re-enables the rope/snap/handle,
        // every bone, the rope centre and bounds are rewritten each frame, and the handle pose starts fresh.
        public bool lightRope;
        private bool _lineShort;   // the LineRenderer was cut to 2 points by lightRope (the builder leaves 12)
        // IMP-DOG-034 C1-rope (2): this collar's grip pose for the current frame (frame key, rope start = socket).
        private int _poseFrame = -1;
        private Vector3 _poseRopeStart;
        private KH_LeashCollar[] _peers;
        // The collar of dog `index`, looked up once from manager.collarAnchors (anchor = child "ClipAnchor" of the
        // collar object, see KH_DogWalkingEditor). null (lookup failed) -> the caller computes its own pose as before.
        private KH_LeashCollar PeerCollar(int index)
        {
            if (_peers == null)
            {
                Transform[] anchors = manager.collarAnchors;
                int n = anchors != null ? anchors.Length : 0;
                _peers = new KH_LeashCollar[n];
                for (int i = 0; i < n; i++)
                {
                    if (anchors[i] == null || anchors[i].parent == null) continue;
                    KH_LeashCollar c = anchors[i].parent.GetComponent<KH_LeashCollar>();
                    if (c != null && c.dogIndex == i) _peers[i] = c;
                }
            }
            return index < _peers.Length ? _peers[index] : null;
        }
        // IMP-DOG-034 C1-rope (2): called by the other collars on this collar's holder/hand. Same synced arrays and
        // same frame as this collar's own PostLateUpdate, so the pose computed here is the one it would compute.
        public Vector3 _SharedRopeStart()
        {
            if (_poseFrame == Time.frameCount) return _poseRopeStart;
            int[] walkers = manager.walkers;
            int holderId = dogIndex >= 0 && dogIndex < walkers.Length ? walkers[dogIndex] : -1;
            VRCPlayerApi holder = holderId >= 0 ? VRCPlayerApi.GetPlayerById(holderId) : null;
            if (holderId < 0 || !Utilities.IsValid(holder)) return _poseRopeStart;
            return PoseHandle(holder, holderId, manager.hands[dogIndex]);
        }
        // The grip pose (unchanged from the inline code it was moved out of), the handle placed there, and the rope
        // start (the handle's socket) cached for this frame.
        private Vector3 PoseHandle(VRCPlayerApi holder, int holderId, int hand)
        {
            Quaternion rotation;
            Vector3 a;
            if (holder.IsUserInVR())
            {
                // IMP-DOG-034 A4: one GetTrackingData for both the position (was manager._HandPosition, which
                // returns this same .position for a VR player) and the rotation; same player, same frame.
                VRCPlayerApi.TrackingData handData = holder.GetTrackingData(hand == 0
                    ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand);
                a = handData.position;
                Quaternion handRotation = handData.rotation;
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
                // IMP-DOG-034 A4: the blend is only evaluated when not fresh (it was computed and discarded) and
                // Time.time is read once; same frame, same value.
                if (!fresh)
                {
                    float now = Time.time;
                    if (_pathFingers != _shownFingers)
                    { _blendFromPos = _shownPos; _blendFromRot = _shownRot; _blendStartAt = now; }
                    float blend = (now - _blendStartAt) / HandlePathBlend;
                    if (blend < 1f)
                    {
                        a = Vector3.Lerp(_blendFromPos, a, blend);
                        rotation = Quaternion.Slerp(_blendFromRot, rotation, blend);
                    }
                }
                _shownValid = true; _shownHolder = holderId; _shownHand = hand; _shownFingers = _pathFingers;
                _shownPos = a; _shownRot = rotation;
            }
            // Desktop/touch has no tracked hand: keep the body rotation the handle has always used.
            else { a = manager._HandPosition(holder, hand); rotation = holder.GetRotation(); _shownValid = false; }
            _poseRopeStart = PlaceHandle(a, rotation);
            _poseFrame = Time.frameCount;
            return _poseRopeStart;
        }
        // IMP-DOG-034 A4: the four enabled writes the frame used to repeat, now done only on a change of state.
        // Hidden = all off; Line = LineRenderer only; Modeled = rope, snap and handle; NoHandle = the handle of a
        // later dog on the same holder/hand stays off (same rule as before, see the loop above).
        private void ApplyDrawn(int state)
        {
            _drawn = state;
            line.enabled = state == DrawnLine;
            if (ropeMesh != null) ropeMesh.enabled = state >= DrawnModeled;
            if (snapVisual != null) snapVisual.enabled = state >= DrawnModeled;
            if (handleVisual != null) handleVisual.enabled = state == DrawnModeled;
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
        // IMP-DOG-034 C1-rope (2): split into PlaceHandle + UpdateRopeFrom (same statements, same order) so a collar can
        // start its rope at another collar's grip. Kept with this signature for the Editor check (reflection).
        private void UpdateModeledGeometry(Vector3 a, Vector3 b, Quaternion handRotation)
        {
            UpdateRopeFrom(PlaceHandle(a, handRotation), b);
        }
        // Places the handle and returns its rope socket (a itself when there is no handle).
        private Vector3 PlaceHandle(Vector3 a, Quaternion handRotation)
        {
                if (handleVisual != null)
                {
                    // IMP-DOG-034 A4: .transform fetched once (same component, same Transform).
                    Transform handle = handleVisual.transform;
                    handle.SetPositionAndRotation(a, handRotation);
                    a = handle.TransformPoint(new Vector3(0f, -0.077f, 0f));
                }
                return a;
        }
        private void UpdateRopeFrom(Vector3 a, Vector3 b)
        {
                Vector3 direction = b-a;
                if (direction.sqrMagnitude < 0.000001f) direction = Vector3.up;
                if (snapVisual != null)
                {
                    Transform snap = snapVisual.transform;   // IMP-DOG-034 A4: fetched once
                    Quaternion rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
                    snap.SetPositionAndRotation(b-rotation*new Vector3(0f,.045f,0f),rotation);
                    b=snap.TransformPoint(new Vector3(0f,-.047f,0f));
                }
            UpdateGeometry(a,b,true);
        }
        // IMP-DOG-034 A4: per-bone constants of the rope curve, filled on first use (also when the Editor check calls
        // UpdateGeometry by reflection without Start). Each entry is the exact sub-expression the loop used to compute
        // every frame - t = i/11, Sin(t*PI), Cos(t*PI)*PI - so the products below keep the old evaluation order and
        // give the same floats.
        private bool _ropeReady;
        private float[] _ropeT, _ropeSin, _ropeCosPi;
        private void PrepareRope()
        {
            _ropeT = new float[12]; _ropeSin = new float[12]; _ropeCosPi = new float[12];
            for (int i = 0; i < 12; i++)
            {
                float t = i / 11f;
                _ropeT[i] = t;
                _ropeSin[i] = Mathf.Sin(t * Mathf.PI);
                _ropeCosPi[i] = Mathf.Cos(t * Mathf.PI) * Mathf.PI;
            }
            _ropeReady = true;
        }
        private void UpdateGeometry(Vector3 a, Vector3 b, bool modeled)
        {
            if (!_ropeReady) PrepareRope();
            // IMP-DOG-034 A4: the distance is computed once (it was computed twice from the same a, b).
            float distance = Vector3.Distance(a,b);
            if(modeled)
            {
                ropeMesh.transform.position=(a+b)*.5f;
                ropeMesh.localBounds=new Bounds(Vector3.zero,Vector3.one*(distance+1f));
            }
            // IMP-DOG-034 C1-rope (3): the LineRenderer path needs its 12 points back after lightRope cut it to 2.
            else if (_lineShort) { line.positionCount = 12; _lineShort = false; }
            float slack = Mathf.Clamp(manager.leashLength - distance, 0f, 0.8f);
            // IMP-DOG-034 A4: loop-invariant values read once (Vector3.up/right are constants, b-a does not change).
            Vector3 up = Vector3.up, right = Vector3.right, ab = b-a;
            // IMP-DOG-034 C1-rope (1): taut rope (Clamp returned exactly 0, i.e. distance >= leashLength). The general
            // loop below then subtracts up*(x*0*0.3) = a (+/-)0 vector from each point and from ab, which leaves
            // Lerp(a,b,t) and ab unchanged, so every bone has the same tangent ab and the same LookRotation(ab, right).
            // Same positions (same Lerp call) and the same rotation computed once: the look does not change.
            if (slack == 0f)
            {
                Quaternion straight = Quaternion.identity;
                if (modeled)
                {
                    Vector3 tangent = ab;
                    if (tangent.sqrMagnitude < .000001f) tangent = Vector3.forward;
                    straight = Quaternion.LookRotation(tangent, right);
                }
                for (int i = 0; i < 12; i++)
                {
                    Vector3 point = Vector3.Lerp(a,b,_ropeT[i]);
                    if (modeled) ropeBones[i].SetPositionAndRotation(point,straight);
                    else line.SetPosition(i,point);
                }
                return;
            }
            for (int i = 0; i < 12; i++)
            {
                Vector3 point = Vector3.Lerp(a,b,_ropeT[i]) - up * (_ropeSin[i] * slack * 0.3f);
                if (modeled)
                {
                    Vector3 tangent = ab-up*(_ropeCosPi[i]*slack*.3f);
                    if (tangent.sqrMagnitude < .000001f) tangent = Vector3.forward;
                    ropeBones[i].SetPositionAndRotation(point,Quaternion.LookRotation(tangent,right));
                }
                else line.SetPosition(i,point);
            }
        }
    }
}
