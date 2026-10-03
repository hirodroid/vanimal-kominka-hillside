using UdonSharp;
using UnityEngine;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-WLD-008: the pose of each mode. Only the selected mode's function runs (ModeTarget is one switch).
    /// A mode writes the wanted camera position / look point / FOV and the subject (for the occlusion ray); the shared post-processing
    /// (KH_VideoCamera.ApplyPose) does the occlusion, the dog push-out, the cut or the smoothing and the roll lock.
    /// A mode returns false when its subject is lost (the caller holds the last framing for lostHoldSeconds, then stops rendering).
    /// W1: 犬追従 (design §4.2 #1, §4.3) and じぶん (#2, the local player only). W2 / W3 add their modes in their own partial files.
    /// </summary>
    public partial class KH_VideoCamera
    {
        // travel heading of the current subject (smoothed velocity; kept while slow so the camera does not spin when the subject stops)
        private Vector3 _hPrev, _hVel, _heading;
        private bool _hHavePrev;
        private float _turnUntil;

        bool ModeTarget(float dt)
        {
            switch (_mode)
            {
                case ModeDogFollow: return DogFollow(dt);
                case ModeSelfFollow: return SelfFollow(dt);
            }
            return false;
        }

        void ResetHeading()
        {
            _hHavePrev = false; _hVel = Vector3.zero; _heading = Vector3.zero; _turnUntil = 0f;
        }

        /// <summary>The subject's horizontal travel direction: smoothed velocity (headingTau); below stillSpeed the last direction is kept;
        /// before the first movement, `fallback` (the dog root's / the body's facing). A turn faster than turnRateDeg/s lengthens the position
        /// time constant for 0.5 s (the camera swings round the outside of a corner instead of being dragged).</summary>
        Vector3 Heading(Vector3 p, Vector3 fallback, float dt)
        {
            if (!_hHavePrev) { _hPrev = p; _hHavePrev = true; _hVel = Vector3.zero; }
            Vector3 v = (p - _hPrev) / Mathf.Max(dt, 0.001f);
            v.y = 0f;
            _hPrev = p;
            if (v.magnitude > 20f) v = Vector3.zero;   // a teleport is not a speed
            _hVel = Vector3.Lerp(_hVel, v, BlendFactor(dt, headingTau));
            float speed = _hVel.magnitude;
            Vector3 next = _heading;
            if (speed >= stillSpeed) next = _hVel / speed;
            if (next.sqrMagnitude < 0.0001f)
            {
                next = new Vector3(fallback.x, 0f, fallback.z);
                next = next.sqrMagnitude > 0.0001f ? next.normalized : Vector3.forward;
            }
            if (_heading.sqrMagnitude > 0.0001f && Vector3.Angle(_heading, next) > turnRateDeg * dt) _turnUntil = Time.time + 0.5f;
            _heading = next;
            return _heading;
        }

        /// <summary>犬追従: in front of the dog at dogSideDeg from its travel direction, at the distance where the dog fills dogFill of the
        /// screen height (clamped dogDistMin..dogDistMax), at head height + dogLift, looking at the body a little ahead.</summary>
        bool DogFollow(float dt)
        {
            if (!DogValid(_dog)) return false;
            Transform root = dogRoots[_dog];
            Vector3 subj = DogSubject(_dog);
            float size = DogSize(_dog);
            Vector3 h = Heading(root.position, root.forward, dt);
            _wantPos = DogFollowPos(subj, DogHead(_dog).y, h, size, dogFill, dogFov, dogSideDeg, dogDistMin, dogDistMax, dogLift) + LeadOffset(_hVel, posTau);
            _wantLook = DogFollowLook(subj, h, dogLookAhead) + LeadOffset(_hVel, lookTau);
            _wantFov = dogFov;
            _subject = subj; _subjectSize = size;
            return true;
        }

        /// <summary>じぶん (the local player only in W1): ahead of the player at selfSideDeg from the travel direction, selfDistance away,
        /// head + selfLift, looking at the point selfLookToDogs of the way from the head to the player's own leashed dogs (within selfDogMax),
        /// or at the chest when no own dog is near.</summary>
        bool SelfFollow(float dt)
        {
            if (!SelfHead()) return false;
            Vector3 head = _selfHead;
            Vector3 h = Heading(_selfFeet, _selfFwd, dt);
            _wantPos = SelfFollowPos(head, h, selfSideDeg, selfDistance, selfLift) + LeadOffset(_hVel, posTau);
            Vector3 sum = Vector3.zero;
            int own = 0;
            if (manager != null && dogRoots != null)
                for (int i = 0; i < dogRoots.Length; i++)
                {
                    if (!DogValid(i) || !manager._IsMine(i)) continue;
                    Vector3 c = DogSubject(i);
                    if ((c - head).magnitude > selfDogMax) continue;
                    sum += c; own++;
                }
            _wantLook = SelfFollowLook(head, own > 0 ? sum / own : head, own, selfLookToDogs, selfChestDrop) + LeadOffset(_hVel, lookTau);
            _wantFov = selfFov;
            _subject = head; _subjectSize = 0.5f;
            _selfOwnDogs = own;
            return true;
        }

        // ================================================================ pure pose math
        // Static and side-effect free: the runtime above and the Edit-mode probe (Video/2b, KH_VideoCameraEditor.Probe.cs) call these same
        // functions, so the formulas exist once. U# supports static methods (precedent: KH_FrisbeeCatch.LandingOf).

        /// <summary>犬追従 position: from the body, sideDeg off the travel direction, at the framing distance, at head height + lift.</summary>
        public static Vector3 DogFollowPos(Vector3 body, float headY, Vector3 heading, float size, float fill, float fov, float sideDeg, float distMin, float distMax, float lift)
        {
            Vector3 side = Quaternion.AngleAxis(sideDeg, Vector3.up) * heading;
            float dist = Mathf.Clamp(FrameDistanceFor(size, fill, fov), distMin, distMax);
            Vector3 pos = body + side * dist;
            pos.y = headY + lift;
            return pos;
        }

        public static Vector3 DogFollowLook(Vector3 body, Vector3 heading, float ahead) { return body + heading * ahead; }

        /// <summary>じぶん position: from the head, sideDeg off the travel direction, distance away, head + lift.</summary>
        public static Vector3 SelfFollowPos(Vector3 head, Vector3 heading, float sideDeg, float distance, float lift)
        {
            return head + (Quaternion.AngleAxis(sideDeg, Vector3.up) * heading) * distance + Vector3.up * lift;
        }

        /// <summary>じぶん look point: toDogs of the way from the head to the centre of the own dogs, or the chest when there is none.</summary>
        public static Vector3 SelfFollowLook(Vector3 head, Vector3 dogCentre, int ownDogs, float toDogs, float chestDrop)
        {
            return ownDogs > 0 ? head + (dogCentre - head) * toDogs : head + Vector3.down * chestDrop;
        }

        /// <summary>Feed-forward: exponential smoothing trails a subject moving at v by v × tau, so the wanted point leads by the same amount
        /// (a dog trotting at 3 m/s would otherwise drag a posTau 0.6 s camera 1.8 m behind its framing). Horizontal velocity only.</summary>
        public static Vector3 LeadOffset(Vector3 velocity, float tau) { return velocity * tau; }

        /// <summary>The distance at which a subject of this size fills fill of the screen height at this FOV (KD_PhotoDirector.FrameDistance without jitter).</summary>
        public static float FrameDistanceFor(float size, float fill, float fov)
        {
            if (!(size > 0.05f) || !(size < 14f)) size = 0.6f;
            fill = Mathf.Clamp(fill, 0.06f, 0.95f);
            float half = Mathf.Clamp(fov, 10f, 120f) * 0.5f * Mathf.Deg2Rad;
            return (size / fill) * 0.5f / Mathf.Tan(half);
        }

        /// <summary>Exponential smoothing factor for a time constant (frame-rate independent).</summary>
        public static float BlendFactor(float dt, float tau) { return tau <= 0.0001f ? 1f : 1f - Mathf.Exp(-dt / tau); }

        /// <summary>One smoothing step: a cut jumps to the target, otherwise move by BlendFactor.</summary>
        public static Vector3 SmoothStep(Vector3 current, Vector3 target, bool cut, float dt, float tau)
        {
            return cut ? target : Vector3.Lerp(current, target, BlendFactor(dt, tau));
        }

        /// <summary>The camera rotation: looking at the point with world up (roll 0).</summary>
        public static Quaternion LookRoll0(Vector3 from, Vector3 at)
        {
            Vector3 dir = at - from;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        private int _selfOwnDogs;   // harness read-out (own dogs framed by じぶん on the last render)
    }
}
