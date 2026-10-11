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
    /// call the same functions). The walk modes are in KH_VideoCamera.ModesWalk.cs, the play modes in KH_VideoCamera.ModesPlay.cs, 背中ごし / みち
    /// (§16) and 隊列 (DES-WLD-004 M2) in KH_VideoCamera.ModesPath.cs.
    /// </summary>
    public partial class KH_VideoCamera
    {
        // travel heading of the current subject (smoothed velocity; kept while slow so the camera does not spin when the subject stops)
        private Vector3 _hPrev, _hVel, _heading;
        private bool _hHavePrev;
        private float _turnUntil;

        bool ModeTarget(float dt, float now)
        {
            bool ok = ModePose(dt, now);
            if (ok) ApplyControls();   // §15.5: the user's 近く / 遠く / ↑ / ↓ / ← / → on top of the mode's own pose (KH_VideoCamera.Controls.cs)
            return ok;
        }

        bool ModePose(float dt, float now)
        {
            // per-mode options: the defaults, then the mode overrides what it needs
            _wantTau = posTau; _wantLookTau = lookTau; _wantGroundMin = groundLift; _wantFixed = false; _lostWord = 0;
            switch (_mode)
            {
                case ModeDogFollow: return DogFollow(dt);
                // W4: another person only through TryPersonPose (People.cs). §17: a pattern's じぶん is always me (a pattern never reads another person)
                case ModeSelfFollow: return _person < 0 || _patRun ? SelfFollow(dt) : PersonFollow(dt);
                case ModeTps: return Tps(dt);   // §16: always me, also while another person is chosen for じぶん
                case ModeDogEye: return DogEye(dt);
                case ModeSpot: return Spot(dt);
                case ModeDisc: return DiscFollow(dt);
                case ModeCatchWait: return CatchWait(dt, now);
                case ModeOrbit: return Orbit(dt);
                case ModePassBy: return PassBy(dt, now);
                case ModePath: return PathMove(dt, now);   // §16: always me
                case ModeConvoy: return Convoy(dt);   // DES-WLD-004 M2: my cart group (背中ごし while I do not ride), KH_VideoCamera.ModesPath.cs
                case ModeWelcome: return Welcome(dt);   // §17: internal (patterns only), KH_VideoCamera.ModesPlay.cs
            }
            return false;
        }

        /// <summary>Per-mode state that must not survive a subject change (heading is ResetHeading).</summary>
        void ResetModeState()
        {
            _discHave = false; _discVel = Vector3.zero;
            _landHave = false; _landWasFlying = false; _landSampling = false; _landMoved = false;
            _catchPlaced = false; _catchReplans = 0; _catchSignalled = false; _catchWasFlying = false;
            _welcomeMine = false;   // §17
            _orbitHave = false; _orbitEnded = false;   // §16: a new orbit starts its radius and angle again
            _tpsHave = false;
            _pathHave = false; _pathTrailAt = 0f;
            _convoyHave = false; _convoyRiding = false;   // DES-WLD-004 M2 隊列
            _passIdx = -1; _passLast = -1;   // the pass history must not survive a reopen / mode re-entry
        }

        /// <summary>On open / mode change: pick the subject the mode is about when it is obvious (the spot nearest to me, the dog I am petting).</summary>
        void AutoSubject()
        {
            if (_mode == ModeSpot) _spot = NearestSpot();
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

        /// <summary>§16: the horizontal unit direction of a yaw (degrees; 0 = +Z, 90 = +X, Unity's convention).</summary>
        public static Vector3 YawDir(float yawDeg)
        {
            float r = yawDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        /// <summary>§16: the yaw (degrees) of a horizontal direction (inverse of YawDir).</summary>
        public static float DirYaw(Vector3 d) { return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }

        /// <summary>§16: one smoothing step of a yaw toward a target along the shorter way round (time constant tau), wrapped to -180..180.
        /// A 180° turn takes the same time as a small one relative to its size (no swing through the vertical).</summary>
        public static float YawStep(float current, float target, float dt, float tau)
        {
            float d = target - current;
            while (d > 180f) d -= 360f;
            while (d < -180f) d += 360f;
            float y = current + d * BlendFactor(dt, tau);
            while (y > 180f) y -= 360f;
            while (y < -180f) y += 360f;
            return y;
        }

        /// <summary>§16 背中ごし position: behind the head (against the smoothed body facing fwd) back metres, to the right shoulder metres, up lift.</summary>
        public static Vector3 TpsPos(Vector3 head, Vector3 fwd, float back, float shoulder, float lift)
        {
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            return head - fwd * back + right * shoulder + Vector3.up * lift;
        }

        /// <summary>§16 背中ごし look point: ahead metres in front of the head, drop up (negative = down): over the shoulder, where I look.</summary>
        public static Vector3 TpsLook(Vector3 head, Vector3 fwd, float ahead, float drop) { return head + fwd * ahead + Vector3.up * drop; }

        /// <summary>DES-WLD-004 M2 隊列: the distance at which a convoy of this size (m) fills fill of the screen height at this vertical FOV,
        /// clamped distMin..distMax. Not FrameDistanceFor: that one treats a size of 14 m or more as invalid (a long convoy is not).</summary>
        public static float ConvoyDistance(float size, float fill, float fov, float distMin, float distMax)
        {
            if (!(size > 0f)) size = 0f;   // NaN-safe
            fill = Mathf.Clamp(fill, 0.06f, 0.95f);
            float half = Mathf.Clamp(fov, 10f, 120f) * 0.5f * Mathf.Deg2Rad;
            return Mathf.Clamp((size / fill) * 0.5f / Mathf.Tan(half), distMin, distMax);
        }

        /// <summary>DES-WLD-004 M2 隊列 position: from the centre, dist metres toward the back of the travel direction fwd turned backDeg to its
        /// right, elevDeg above the horizontal.</summary>
        public static Vector3 ConvoyPos(Vector3 centre, Vector3 fwd, float backDeg, float elevDeg, float dist)
        {
            Vector3 f = new Vector3(fwd.x, 0f, fwd.z);
            f = f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
            Vector3 right = new Vector3(f.z, 0f, -f.x);
            float b = backDeg * Mathf.Deg2Rad, e = elevDeg * Mathf.Deg2Rad;
            Vector3 h = -f * Mathf.Cos(b) + right * Mathf.Sin(b);
            return centre + (h * Mathf.Cos(e) + Vector3.up * Mathf.Sin(e)) * dist;
        }

        /// <summary>DES-WLD-004 M2 隊列 framing size: the carts' radius around their centre (×2) plus the dogs' margin, or the radius with the dogs
        /// (×2) when a dog is further out, plus one cart body.</summary>
        public static float ConvoySize(float cartRadius, float allRadius, float dogMargin, float cartSize)
        {
            return Mathf.Max(2f * cartRadius + dogMargin, 2f * allRadius) + cartSize;
        }

        /// <summary>§16 みち: the trail's direction turned to agree with my heading (the trail segment's sign is arbitrary).</summary>
        public static Vector3 PathTrailDir(Vector3 trail, Vector3 heading)
        {
            Vector3 t = new Vector3(trail.x, 0f, trail.z);
            t = t.sqrMagnitude > 0.0001f ? t.normalized : heading;
            return Vector3.Dot(t, heading) < 0f ? -t : t;
        }

        /// <summary>§16 みち position: from my feet, along metres in the walking direction, side metres to its right, height above the feet.</summary>
        public static Vector3 PathPos(Vector3 feet, Vector3 dir, float along, float side, float height)
        {
            Vector3 right = new Vector3(dir.z, 0f, -dir.x);
            return feet + dir * along + right * side + Vector3.up * height;
        }

        /// <summary>§16 一周: the radius after `progress` (0..1 = the first full circle) of 一定 / 近づく / 離れる from the start radius r0.
        /// 近づく ends at r0 × nearK (never below rMin, never above r0), 離れる at r0 × farK (never above rMax, never below r0); linear in the angle.</summary>
        public static float OrbitRadiusAt(float r0, int kind, float progress, float nearK, float farK, float rMin, float rMax)
        {
            float end = r0;
            if (kind == OrbitCloser) end = Mathf.Min(r0, Mathf.Max(r0 * nearK, rMin));
            else if (kind == OrbitFarther) end = Mathf.Max(r0, Mathf.Min(r0 * farK, rMax));
            return Mathf.Lerp(r0, end, Mathf.Clamp01(progress));
        }

        /// <summary>§16 一周: the look height above the centre. 近づく raises it from lookUp to the camera height over the first circle (a close
        /// orbit looking at the middle of the body lost the head; Video/2b), the others keep lookUp.</summary>
        public static float OrbitLookHeight(float lookUp, float height, int kind, float progress)
        {
            return kind == OrbitCloser ? Mathf.Lerp(lookUp, height, Mathf.Clamp01(progress)) : lookUp;
        }

        /// <summary>§17 おかえり (internal): from my feet toward the dog, dist out but never more than half the way to the dog, side metres to the right
        /// of that line, height up; the look point is the dog's head (the caller). The 10-04 formula (W2・W3 実装メモ row 7: 1.6 m toward the dog,
        /// 0.45 m high) plus two AI-reading changes from the Video/2b numbers: the dog stops returnStopDistance (0.9 m) in front of the thrower,
        /// so 1.6 m out ended BEHIND the dog (half the way keeps it in front), and on the line itself the arriving dog ran into the camera
        /// (side 0.8 m: it comes past beside the lens, the head 0.77 m away at the stop).</summary>
        public static Vector3 WelcomePos(Vector3 receiverFeet, Vector3 dogPos, float dist, float side, float height)
        {
            Vector3 d = dogPos - receiverFeet; d.y = 0f;
            float len = d.magnitude;
            d = len > 0.0001f ? d / len : Vector3.forward;
            Vector3 right = new Vector3(d.z, 0f, -d.x);
            return receiverFeet + d * Mathf.Min(dist, len * 0.5f) + right * side + Vector3.up * height;
        }

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
