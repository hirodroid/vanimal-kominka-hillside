using UdonSharp;
using UnityEngine;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-WLD-008: the pose of each mode. Only the selected mode's function runs (ModeTarget is one switch).
    /// A mode writes the wanted camera position / look point / FOV, the subject (for the occlusion ray) and its options (time constants, a fixed
    /// data position, the height above ground); the shared post-processing (KH_VideoCamera.ApplyPose) does the occlusion, the dog push-out,
    /// the cut or the smoothing and the roll lock. A mode returns false when its subject is lost (the caller holds this session's last framing
    /// of this subject for lostHoldSeconds, then shows black).
    /// This file: the switch, the shared heading, 犬追従 / じぶん (W1) and every static pose formula (the runtime and the Edit-mode probe Video/2b
    /// call the same functions). The walk modes are in KH_VideoCamera.ModesWalk.cs, the play / wash modes in KH_VideoCamera.ModesPlay.cs.
    /// </summary>
    public partial class KH_VideoCamera
    {
        // travel heading of the current subject (smoothed velocity; kept while slow so the camera does not spin when the subject stops)
        private Vector3 _hPrev, _hVel, _heading;
        private bool _hHavePrev;
        private float _turnUntil;

        bool ModeTarget(float dt, float now)
        {
            // per-mode options: the defaults, then the mode overrides what it needs
            _wantTau = posTau; _wantLookTau = lookTau; _wantGroundMin = groundLift; _wantFixed = false; _lostWord = 0;
            switch (_mode)
            {
                case ModeDogFollow: return DogFollow(dt);
                case ModeSelfFollow: return SelfFollow(dt);
                case ModeDogEye: return DogEye(dt);
                case ModeDrone: return Drone(dt);
                case ModeSpot: return Spot(dt);
                case ModeDisc: return DiscFollow(dt);
                case ModeCatchWait: return CatchWait(dt, now);
                case ModeWelcome: return Welcome(dt);
                case ModeFureai: return Fureai(dt, now);
                case ModeWash: return Wash(dt);
                case ModeOrbit: return Orbit(dt);
                case ModePassBy: return PassBy(dt, now);
            }
            return false;
        }

        /// <summary>Per-mode state that must not survive a subject change (heading is ResetHeading).</summary>
        void ResetModeState()
        {
            _discHave = false; _discVel = Vector3.zero;
            _landHave = false; _landWasFlying = false; _landSampling = false; _landMoved = false;
            _catchPlaced = false; _catchReplans = 0; _catchSignalled = false; _catchWasFlying = false; _wasCarrying = false;
            _orbitHave = false;
            _passIdx = -1; _passLast = -1;   // the pass history must not survive a reopen / mode re-entry
            _shakeSeen = -1; _shakeUntil = -1f;
        }

        /// <summary>On open / mode change: pick the subject the mode is about when it is obvious (the spot nearest to me, the dog I am petting).</summary>
        void AutoSubject()
        {
            if (_mode == ModeSpot) _spot = NearestSpot();
            if (_mode == ModeFureai)
            {
                int me = SelfId();
                if (fetches != null)
                    for (int i = 0; i < fetches.Length; i++)
                        if (fetches[i] != null && fetches[i].PetActive && fetches[i].PetPlayerId == me && DogValid(i)) { _dog = i; break; }
            }
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
            return DogFollowOf(_dog, dt);
        }

        bool DogFollowOf(int dog, float dt)
        {
            if (!DogValid(dog)) return false;
            Transform root = dogRoots[dog];
            Vector3 subj = DogSubject(dog);
            float size = DogSize(dog);
            Vector3 h = Heading(root.position, root.forward, dt);
            _wantPos = DogFollowPos(subj, DogHead(dog).y, h, size, dogFill, dogFov, dogSideDeg, dogDistMin, dogDistMax, dogLift) + LeadOffset(_hVel, posTau);
            _wantLook = DogFollowLook(subj, h, dogLookAhead) + LeadOffset(_hVel, lookTau);
            _wantFov = dogFov;
            _subject = subj; _subjectSize = size;
            return true;
        }

        /// <summary>じぶん (the local player only): ahead of the player at selfSideDeg from the travel direction, head + selfLift. With own leashed
        /// dogs within selfDogMax: selfDistanceWithDog away, looking selfLookToDogs of the way from the head to the dogs' centre (the W1 probe:
        /// a dog at the feet fell below the frame at 2.4 m / 0.4; 3.2 m / 0.55 keeps head and dog inside the inner 80 %). Without: selfDistance,
        /// looking at the chest.</summary>
        bool SelfFollow(float dt)
        {
            if (!SelfHead()) return false;
            Vector3 head = _selfHead;
            Vector3 h = Heading(_selfFeet, _selfFwd, dt);
            int own = OwnDogsCentre(head, selfDogMax);
            _wantPos = SelfFollowPos(head, h, selfSideDeg, own > 0 ? selfDistanceWithDog : selfDistance, selfLift) + LeadOffset(_hVel, posTau);
            _wantLook = SelfFollowLook(head, _ownCentre, own, selfLookToDogs, selfChestDrop) + LeadOffset(_hVel, lookTau);
            _wantFov = selfFov;
            _subject = head; _subjectSize = 0.5f;
            _selfOwnDogs = own;
            return true;
        }

        private int _selfOwnDogs;   // harness read-out (own dogs framed by じぶん on the last render)
        private Vector3 _ownCentre;

        /// <summary>The number of my leashed dogs within maxDist of `from`; their body centre goes to _ownCentre (from when none).</summary>
        int OwnDogsCentre(Vector3 from, float maxDist)
        {
            Vector3 sum = Vector3.zero;
            int own = 0;
            if (manager != null && dogRoots != null)
                for (int i = 0; i < dogRoots.Length; i++)
                {
                    if (!DogValid(i) || !manager._IsMine(i)) continue;
                    Vector3 c = DogSubject(i);
                    if ((c - from).magnitude > maxDist) continue;
                    sum += c; own++;
                }
            _ownCentre = own > 0 ? sum / own : from;
            return own;
        }

        // ================================================================ pure pose math
        // Static and side-effect free: the runtime and the Edit-mode probe (Video/2b, KH_VideoCameraEditor.Probe.cs) call these same
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

        /// <summary>犬目線 (live, §4.4): beside and a little behind the dog's head on the side away from the user (sideDeg off the line
        /// user → dog), back metres out, lift above the head; the look target is the user's head (the caller).</summary>
        public static Vector3 DogEyePos(Vector3 dogHead, Vector3 userHead, float sideDeg, float back, float lift)
        {
            Vector3 away = dogHead - userHead; away.y = 0f;
            away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;
            return dogHead + (Quaternion.AngleAxis(sideDeg, Vector3.up) * away) * back + Vector3.up * lift;
        }

        /// <summary>犬目線 when the user is far: in front of the dog's head (ahead), looking where the dog goes, pitched up a little.</summary>
        public static Vector3 DogEyeScenePos(Vector3 dogHead, Vector3 facing, float ahead, float lift) { return dogHead + facing * ahead + Vector3.up * lift; }

        public static Vector3 DogEyeSceneLook(Vector3 pos, Vector3 facing, float pitchDeg)
        {
            float r = pitchDeg * Mathf.Deg2Rad;
            return pos + (facing * Mathf.Cos(r) + Vector3.up * Mathf.Sin(r)) * 10f;
        }

        /// <summary>ドローン: behind (against the travel direction) and above the followed point; looks a little ahead of it.</summary>
        public static Vector3 DronePos(Vector3 point, Vector3 heading, float back, float height) { return point - heading * back + Vector3.up * height; }

        public static Vector3 DroneLook(Vector3 point, Vector3 heading, float ahead) { return point + heading * ahead; }

        /// <summary>ディスク追従: beside the disc and the chasing dog (to the right of the flight), as far out as they are apart (sideK × separation,
        /// clamped sideMin..sideMax), up above their midpoint; it moves with the disc. The disc's rotation is never used. (The design's "2.5 m
        /// behind the disc" lost the dog behind the camera: the probe of 2026-10-04 had the pair out of the inner 80 % in 61 of 65 frames.)</summary>
        public static Vector3 DiscFollowPos(Vector3 disc, Vector3 dogBody, Vector3 velocity, float sideK, float sideMin, float sideMax, float up)
        {
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            if (flat.sqrMagnitude < 0.01f) flat = new Vector3(disc.x - dogBody.x, 0f, disc.z - dogBody.z);
            flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;
            Vector3 perp = Vector3.Cross(Vector3.up, flat);
            float side = Mathf.Clamp((disc - dogBody).magnitude * sideK, sideMin, sideMax);
            return (disc + dogBody) * 0.5f + perp * side + Vector3.up * up;
        }

        /// <summary>ディスク追従 look: halfway between the disc and the chasing dog; the dog alone once they are further apart than maxApart.</summary>
        public static Vector3 DiscFollowLook(Vector3 disc, Vector3 dogBody, float maxApart)
        {
            return (disc - dogBody).magnitude > maxApart ? dogBody : (disc + dogBody) * 0.5f;
        }

        /// <summary>キャッチ待ち: side-on to the flight line at the planned catch point C (the dog's side), side metres out, up above C, so the
        /// incoming disc, the jump and the catch are in one fixed frame.</summary>
        public static Vector3 CatchWaitPos(Vector3 catchPoint, Vector3 flightDir, Vector3 dogPos, float side, float up)
        {
            Vector3 f = new Vector3(flightDir.x, 0f, flightDir.z);
            f = f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
            Vector3 perp = Vector3.Cross(Vector3.up, f);
            if (Vector3.Dot(dogPos - catchPoint, perp) < 0f) perp = -perp;
            return catchPoint + perp * side + Vector3.up * up;
        }

        /// <summary>キャッチ待ち look: lookBack metres before C along the flight (toward the incoming disc), at C's height.</summary>
        public static Vector3 CatchWaitLook(Vector3 catchPoint, Vector3 flightDir, float lookBack)
        {
            Vector3 f = new Vector3(flightDir.x, 0f, flightDir.z);
            f = f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
            return catchPoint - f * lookBack;
        }

        /// <summary>キャッチ待ち fallback (no catch plan readable): beside the predicted landing point L, side metres off the flight line on the side where the dog is and beyond metres
        /// past L (so the disc and the dog come toward the camera), up high. (beyond 0 lost the incoming disc in 4 of 90 probe frames.)</summary>
        public static Vector3 LandingWaitPos(Vector3 landing, Vector3 flightDir, Vector3 dogPos, float side, float beyond, float up)
        {
            Vector3 f = new Vector3(flightDir.x, 0f, flightDir.z);
            f = f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
            Vector3 perp = Vector3.Cross(Vector3.up, f);
            Vector3 toDog = dogPos - landing;
            if (Vector3.Dot(toDog, perp) < 0f) perp = -perp;
            return landing + perp * side + f * beyond + Vector3.up * up;
        }

        /// <summary>落下待ち look: L (+0.5 m); once the disc is within near, halfway between the disc and L.</summary>
        public static Vector3 LandingWaitLook(Vector3 landing, Vector3 disc, bool discNear) { Vector3 l = landing + Vector3.up * 0.5f; return discNear ? (l + disc) * 0.5f : l; }

        /// <summary>おかえり: dist metres from the receiver toward the returning dog, low (height above the receiver's feet).</summary>
        public static Vector3 WelcomePos(Vector3 receiverFeet, Vector3 dogPos, float dist, float height)
        {
            Vector3 d = dogPos - receiverFeet; d.y = 0f;
            d = d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
            return receiverFeet + d * dist + Vector3.up * height;
        }

        /// <summary>おかえり without me as the receiver: ahead of the returning dog, looking back at it.</summary>
        public static Vector3 WelcomeAheadPos(Vector3 dogBody, Vector3 heading, float ahead, float height) { return dogBody + heading * ahead + Vector3.up * height; }

        /// <summary>ふれあい: beside the dog's head, sideDeg off the direction away from the petter (or the dog's facing), dist out, lift up.</summary>
        public static Vector3 FureaiPos(Vector3 dogHead, Vector3 awayFromPetter, float sideDeg, float dist, float lift)
        {
            Vector3 a = new Vector3(awayFromPetter.x, 0f, awayFromPetter.z);
            a = a.sqrMagnitude > 0.0001f ? a.normalized : Vector3.forward;
            return dogHead + (Quaternion.AngleAxis(sideDeg, Vector3.up) * a) * dist + Vector3.up * lift;
        }

        /// <summary>洗浄の手元: on the far side of the stroked part from the washer, dist out and up high.</summary>
        public static Vector3 WashPos(Vector3 part, Vector3 washer, float dist, float up)
        {
            Vector3 a = part - washer; a.y = 0f;
            a = a.sqrMagnitude > 0.0001f ? a.normalized : Vector3.forward;
            return part + a * dist + Vector3.up * up;
        }

        /// <summary>ゆっくり一周: on the circle around the centre at angleDeg (0 = +X, counter-clockwise seen from above), radius, height.</summary>
        public static Vector3 OrbitPos(Vector3 centre, float angleDeg, float radius, float height)
        {
            float r = angleDeg * Mathf.Deg2Rad;
            return centre + new Vector3(Mathf.Cos(r), 0f, Mathf.Sin(r)) * radius + Vector3.up * height;
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
    }
}
