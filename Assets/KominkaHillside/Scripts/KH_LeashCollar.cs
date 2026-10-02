using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Enums;

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
        // IMP-DOG-036 S6 (1): no per-frame entry while this dog has no leash. The old PostLateUpdate body is now
        // _RopeTick, a loop that reschedules itself every frame only while the dog is leashed (holder valid) and
        // stops after the frame that hides the rope. The behaviour stays enabled (Interact and method calls are
        // unaffected; enabled = false is not used). Started by _RopeWake from:
        //   Start (one tick: writes the hidden state like the old first PostLateUpdate, then stops),
        //   KH_LeashManager.ReqAttach (the Owner's own write), KH_LeashManager.OnDeserialization (every other
        //   client and late joiners), and KH_LeashManager's 0.5 s maintenance (safety net: harness writes into
        //   walkers, a holder not yet valid on this client, a lost event).
        // Timing: EventTiming.PostLateUpdate. Scheduled frame events run in UdonManager.PostLateUpdate right after
        // every behaviour's PostLateUpdate (UdonManager.cs:504-541, the same LateUpdate at execution order 31000
        // that ran the old PostLateUpdate), i.e. after the dogs moved and after the avatars' IK, before rendering:
        // the same point of the frame as before, so the rope does not lag. The scheduler runs an event at
        // CurrentFrame + Max(delayFrames, 1) (VRC.Udon.ClientBindings UdonEventScheduler.CreateScheduledFrameEvent;
        // CurrentFrame advances once per frame in UdonManager.Update), so a delay of 1 from inside the tick is
        // exactly the next frame's PostLateUpdate. A wake runs its first tick in the next frame (one frame after
        // the attach is received, where the old code drew in the same frame).
        private bool _ropeLoop;
        private int _ropeTickFrame = -1000;   // Time.frameCount of the last tick or wake (the watchdog below)
        void Start() { _RopeWake(); }
        // IMP-DOG-036 S6 (review 2): disabling the behaviour stops the rope as the old PostLateUpdate did. A scheduled
        // event still reaches a disabled UdonBehaviour (the scheduler calls SendCustomEvent -> RunProgram with no
        // enabled check, UdonBehaviour.cs:2103), so the tick checks `enabled` itself and ends the loop, leaving the
        // rope as it was drawn (frozen, like the old code). Re-enabling wakes it again (a dog that is not leashed gets
        // one tick that hides the rope, then stops). OnEnable also runs at scene start; Start's wake is then ignored by
        // the single-loop guard. A disable and re-enable before the pending tick ran keeps that one pending loop.
        void OnEnable() { _RopeWake(); }
        public void _RopeWake()
        {
            // IMP-DOG-036 S6 (review 2): a disabled collar is not started (the manager's 0.5 s wake costs one read).
            if (!enabled) return;
            // A live loop ticks every frame. A flag left set by a loop that no longer ticks (its event lost) is
            // replaced after 30 frames, so the manager's 0.5 s check can always revive it; within 30 frames a second
            // wake is ignored (its event is still pending), so there is never more than one loop.
            if (_ropeLoop && Time.frameCount - _ropeTickFrame < 30) return;
            _ropeLoop = true;
            _ropeTickFrame = Time.frameCount;
            CacheTuning();
            SendCustomEventDelayedFrames(nameof(_RopeTick), 1, EventTiming.PostLateUpdate);
        }
        public void _RopeTick()
        {
            if (!enabled) { _ropeLoop = false; return; }   // review 2: the only added per-tick extern
            int frame = Time.frameCount;
            _ropeTickFrame = frame;
            _inFrame = true;
            bool keep = RopeFrame(frame);
            _inFrame = false;
            if (keep) SendCustomEventDelayedFrames(nameof(_RopeTick), 1, EventTiming.PostLateUpdate);
            else _ropeLoop = false;
        }
        // IMP-DOG-036 S6 (review 1): solve now, whatever the phase, when another collar left this one's remote hand
        // group (see RegroupLeft). Every running collar on that hand takes part, including one still waiting hidden for
        // its first phase (it may be the new grip owner, whose handle the others now start at). A collar already inside
        // its own RopeFrame is skipped: U# methods are not re-entrant, and that is the leaver itself or a collar that
        // is leaving the same group in a chain (e.g. the holder left the instance and every collar hides).
        private bool _inFrame;
        private int _forceBy = -1, _forceHand = -1;   // the group a forced solve is for (-1 = none)
        public void _RopeSolveNow(int groupBy, int groupHand)
        {
            if (!_ropeLoop || _inFrame || !enabled) return;
            _inFrame = true; _forceBy = groupBy; _forceHand = groupHand;
            RopeFrame(Time.frameCount);   // a hidden result: the pending tick finds the same and ends the loop
            _forceBy = -1; _forceHand = -1; _inFrame = false;
        }
        // IMP-DOG-036 S6 (3): values that do not change between frames, read once per loop start instead of every
        // frame (each null test of a Unity object and each read of a manager field is an extern). The manager's
        // tuning (leashLength, handleGripOffset, handleRollDegrees, the S6 fields) is scene data written by the
        // builders, so a change made in the inspector during Play applies from the next attach.
        private bool _tuned, _ok, _modeled, _hasHandle;
        private float _leashLen, _rollDeg;
        private Vector3 _gripLocal;
        private Quaternion _handleTilt;
        private int _every = 1;
        private float _straight2, _full2;
        private VRCPlayerApi _local;
        private int _localId = -1;
        // Holder/hand group of the last full solve (-1 = not drawn in a group: hidden, or waiting for its phase).
        private int _heldBy = -1, _heldHand = -1;
        private void CacheTuning()
        {
            _ok = line != null && manager != null;
            _modeled = ropeMesh != null && ropeBones != null && ropeBones.Length == 12;
            _hasHandle = handleVisual != null;
            _handleTilt = Quaternion.Euler(-90f, 0f, 0f);
            if (_localId < 0) CacheLocal();
            if (manager == null) return;
            _leashLen = manager.leashLength;
            _gripLocal = new Vector3(0f, manager.handleGripOffset, 0f);
            _rollDeg = manager.handleRollDegrees;
            _every = Mathf.Max(1, manager.otherRopeEvery);
            float straight = manager.ropeStraightDistance;
            float full = Mathf.Min(manager.ropeFullDistance, straight);   // a reversed pair would flicker
            _straight2 = straight * straight;
            _full2 = full * full;
            _tuned = true;
        }
        private void CacheLocal()
        {
            _local = Networking.LocalPlayer;
            if (Utilities.IsValid(_local)) _localId = _local.playerId;
        }
        // The old PostLateUpdate body. Returns false when the rope is hidden (the loop then stops).
        private bool RopeFrame(int frame)
        {
            if (!_ok) return false;
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
                // IMP-DOG-036 S6 (review 1): leaving a remote hand group re-solves the rest of it in this frame.
                if (_heldBy >= 0) { int oldBy = _heldBy, oldHand = _heldHand; _heldBy = -1; RegroupLeft(oldBy, oldHand, walkers); }
                return false;
            }
            int[] hands = manager.hands;
            int hand = hands[dogIndex];
            // IMP-DOG-036 S6 (2): another player's rope is solved every _every frames and simply held in between
            // (rope, snap and handle stay where the last solve put them, so they still match each other). The phase
            // is per holder/hand (holderId + hand), so every collar on one hand solves on the same frame - the grip
            // owner's _SharedRopeStart / _poseFrame cache and the shared far state below stay consistent - and one
            // player's two hands fall on different frames. The local player's own ropes are never skipped (attached
            // to the hand in VR).
            // Review 1 - a hand group is always solved as a whole, so a grip handle never moves without its ropes:
            //  * joining (attach, or a new holder/hand): the newcomer stays hidden until the hand's phase frame, where
            //    it solves together with the group (also when it becomes the new grip owner by a lower dogIndex: the
            //    old owner then hides its handle on that same frame). It never calls _SharedRopeStart off-phase.
            //  * leaving (detach, holder gone, moved to another holder/hand): the leaver hides at once and makes the
            //    rest of its old group solve in this frame (RegroupLeft), so when the grip owner leaves, the next
            //    collar shows its handle and the others start their ropes there in the same frame. A sibling leaving
            //    changes nothing for the others, but they are re-solved the same way (rare, cheap enough).
            if (_localId < 0) CacheLocal();   // not valid yet at the loop start: until then every rope counts as remote
            bool remote = holderId != _localId;
            bool sameGroup = holderId == _heldBy && hand == _heldHand;
            if (!sameGroup && _heldBy >= 0) { int oldBy = _heldBy, oldHand = _heldHand; _heldBy = -1; RegroupLeft(oldBy, oldHand, walkers); }
            // A forced solve (RegroupLeft) only for the group being re-solved: a collar that moved to another group in
            // the same frame keeps that group's phase, so it never moves another hand's grip off-phase.
            if (remote && _every > 1 && (frame + holderId + hand) % _every != 0
                && !(_forceBy >= 0 && holderId == _forceBy && hand == _forceHand))
            {
                if (sameGroup) return true;   // hold
                // Waiting for the phase: hidden, not yet a member (_heldBy -1).
                _shownValid = false;
                if (_drawn != DrawnHidden) ApplyDrawn(DrawnHidden);
                return true;
            }
            _heldBy = holderId; _heldHand = hand;
            // One grip per holder/hand; coincident copies for 2-3 dogs otherwise flicker in VR.
            // IMP-DOG-034 C1-rope (2): remembers which collar shows the grip (the lowest dogIndex on this holder/hand).
            // IMP-DOG-036 S6 (2): also looked up for another player's rope, whose far state belongs to that collar.
            int gripOwner = -1;
            if (remote || (!lightRope && _modeled && _hasHandle))
                for (int i=0;i<dogIndex;i++)
                    if (walkers[i]==holderId && hands[i]==hand) { gripOwner = i; break; }
            KH_LeashCollar owner = gripOwner >= 0 ? PeerCollar(gripOwner) : null;
            // IMP-DOG-036 S6 (2): another player's rope beyond ropeStraightDistance from the local player is drawn
            // as the straight line (the light-rope path); back to the full rope inside ropeFullDistance. One state
            // per holder/hand (the grip owner's), so the dogs on one hand never mix a handle-less full rope with a
            // straight line whose grip owner hides the handle.
            bool far = remote && (owner != null ? owner._SharedRopeFar(holder) : RopeFar(holder, frame));
            // IMP-DOG-034 C1-rope (3): optional straight two-point line, OFF by default (lightRope false), so the
            // default look is untouched. Real 2-point path: no 12-point curve, no hand pose, no handle/snap/rope mesh.
            if (lightRope || far)
            {
                if (_drawn != DrawnLine) ApplyDrawn(DrawnLine);
                // Back in full mode the handle starts fresh (no blend from a pose shown before light mode).
                _shownValid = false;
                if (!_lineShort) { line.positionCount = 2; _lineShort = true; }
                line.SetPosition(0, manager._HandPosition(holder, hand));
                line.SetPosition(1, anchor.position);
                return true;
            }
            int drawn = _modeled ? DrawnModeled : DrawnLine;
            // IMP-DOG-034 A4: decided before writing (was: write on, then off), so the final state is the same.
            // IMP-DOG-036 S6: same rule as before (the grip owner only hides this handle on the modeled path).
            bool shared = _modeled && _hasHandle && gripOwner >= 0;
            if (shared) drawn = DrawnModeledNoHandle;
            if (_drawn != drawn) ApplyDrawn(drawn);
            Vector3 b = anchor.position;
            if (_modeled)
            {
                // IMP-DOG-034 C1-rope (2): a collar whose own handle is hidden only needs the rope start, i.e. the
                // socket of the grip that IS shown. It asks that collar (which computes its pose at most once per
                // frame, whichever of the two runs first) instead of recomputing the same hand's tracking, four
                // finger/arm bones, HandleRotation, the blend and its own hidden handle's transform. The visible
                // grip and the rope it draws are computed exactly as before; only the hidden duplicates are gone.
                Vector3 start;
                if (shared && owner != null)
                {
                    // Not the shown grip: if this collar becomes the grip owner later it starts fresh.
                    _shownValid = false;
                    start = owner._SharedRopeStart();
                }
                else if (_poseFrame == frame) start = _poseRopeStart;   // a later dog on this hand ran first
                else start = PoseHandle(holder, holderId, hand);
                UpdateRopeFrom(start, b);
                return true;
            }
            UpdateGeometry(manager._HandPosition(holder, hand),b,false);
            return true;
        }
        // IMP-DOG-036 S6 (review 1): this collar just left the hand group (oldBy, oldHand). If that group is skipped
        // frames (another player's hand, _every > 1), every remaining member solves now, in dogIndex order, so the
        // (possibly new) grip owner places its handle and the others start their ropes at it in this same frame.
        // Members that already ticked this frame solve again; the ones still to tick find themselves in the group and
        // hold. Own ropes solve every frame anyway. Runs only when membership changes.
        private void RegroupLeft(int oldBy, int oldHand, int[] walkers)
        {
            if (oldBy == _localId || _every <= 1) return;
            int[] hands = manager.hands;
            int n = walkers.Length < hands.Length ? walkers.Length : hands.Length;
            for (int i = 0; i < n; i++)
            {
                if (i == dogIndex || walkers[i] != oldBy || hands[i] != oldHand) continue;
                KH_LeashCollar peer = PeerCollar(i);
                if (peer != null) peer._RopeSolveNow(oldBy, oldHand);
            }
        }
        // IMP-DOG-036 S6 (2): far state with hysteresis, evaluated at most once per frame (feet to feet; at 15 m
        // the head height does not matter). Another collar on the same holder/hand asks through _SharedRopeFar.
        private bool _far;
        private int _farFrame = -1;
        private bool RopeFar(VRCPlayerApi holder, int frame)
        {
            if (_farFrame == frame) return _far;
            _farFrame = frame;
            if (_localId < 0) { _far = false; return false; }
            float d2 = (holder.GetPosition() - _local.GetPosition()).sqrMagnitude;
            _far = d2 > (_far ? _full2 : _straight2);
            return _far;
        }
        public bool _SharedRopeFar(VRCPlayerApi holder)
        {
            if (!_tuned) CacheTuning();
            return RopeFar(holder, Time.frameCount);
        }
        // IMP-DOG-034 C1-rope (3): straight two-point rope instead of the 12-bone curve. Default false: the default
        // look does not change. Set it on all six collars together (a full-mode rope reads its hand's grip owner):
        // IMP-DOG-034 C2-4 KH_LeashManager._ApplyLightRopeMode does that, from its Update, so every collar sees the
        // new value in the same frame's rope tick (IMP-DOG-036 S6: _RopeTick, formerly PostLateUpdate; another
        // player's rope picks it up on its next solve frame). Unleashed collars stay hidden and pick it up when leashed.
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
        // same frame as this collar's own tick (IMP-DOG-036 S6: both on the same holder/hand phase), so the pose
        // computed here is the one it would compute.
        public Vector3 _SharedRopeStart()
        {
            if (_poseFrame == Time.frameCount) return _poseRopeStart;
            if (!_tuned) CacheTuning();   // IMP-DOG-036 S6: PoseHandle reads the cached tuning
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
                // IMP-DOG-036 S6 (3): the offset vector is built once per loop start (CacheTuning).
                a = _grip - rotation * _gripLocal;
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
            // IMP-DOG-036 S6 (3): roll and the -90 degree tilt cached by CacheTuning (same values).
            q = q * Quaternion.AngleAxis(hand == 0 ? -_rollDeg : _rollDeg, Vector3.forward);
            return q * _handleTilt;
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
            // IMP-DOG-036 S6 (3): the leash length cached at loop start (the Editor check calls this without Start).
            float slack = Mathf.Clamp((_tuned ? _leashLen : manager.leashLength) - distance, 0f, 0.8f);
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
            // IMP-DOG-036 S6 (3): the sag vector up*(slack*0.3) is built once; each bone then scales it by its
            // sine / cosine (was: two float products and a vector product per term, per bone). Same values up to
            // float rounding (the product order changed), far below a visible size.
            Vector3 sag = up * (slack * 0.3f);
            for (int i = 0; i < 12; i++)
            {
                Vector3 point = Vector3.Lerp(a,b,_ropeT[i]) - sag * _ropeSin[i];
                if (modeled)
                {
                    Vector3 tangent = ab - sag * _ropeCosPi[i];
                    if (tangent.sqrMagnitude < .000001f) tangent = Vector3.forward;
                    ropeBones[i].SetPositionAndRotation(point,Quaternion.LookRotation(tangent,right));
                }
                else line.SetPosition(i,point);
            }
        }
    }
}
