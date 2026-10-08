using UdonSharp;
using UnityEngine;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-WLD-008 W2: the walk modes — 犬目線, スポット定点, ゆっくり一周, 通り過ぎる散歩 (design §4.4-4.5, §4.8-4.9; ドローン俯瞰 / 見下ろす was
    /// removed in §16, IMP-WLD-012). 一周 has §16's ways to circle (一定 / 近づく / 離れる, the picker) and its speed ([←][→]).
    /// The formulas are the static functions in KH_VideoCamera.Modes.cs; this file reads the subjects and keeps the per-mode state.
    /// Only the local player is read (SelfHead / SelfId in Subjects.cs).
    ///
    /// 犬目線 here is the LIVE mode of the Issue: "犬のそばの低い位置からユーザーを見上げる" (toward the local player). It is NOT the still-photo
    /// 犬目線 of IMP-WLD-007 (the user redefined that one as "the scenery the dog faces", photo-expansion §0). When the local player is further
    /// than dogEyeUserMax from the dog, this mode shows that scenery instead (the photo definition), so the camera never looks at nobody.
    /// </summary>
    public partial class KH_VideoCamera
    {
        private bool _dogEyeUser;           // read-out: the last 犬目線 looked up at me
        // ゆっくり一周. _orbitTravel: degrees circled since this orbit started (the progress of 近づく / 離れる); _orbitRadiusNow: read-out
        private bool _orbitHave, _orbitEnded;
        private Vector3 _orbitCentre;
        private float _orbitAngle, _orbitTravel, _orbitRadiusNow;
        private int _orbitKind;           // §16 the picker: OrbitSteady / OrbitCloser / OrbitFarther. The choice is kept for the stay (also across × and a reopen, like the spot); the circle and the speed start again
        private float _orbitSpeed = 6f;   // §16 deg/s: [←][→] in 一周; orbitDegPerSec again on open and on [戻す] (KH_VideoCamera.Controls.cs)
        // 通り過ぎる散歩
        private int _passIdx = -1, _passLast = -1;
        private float _passSince, _passMin, _passClosestAt;
        private bool _passEverNear;

        /// <summary>犬目線 (§4.4): low beside the selected dog's head, looking up at me; when I am far, the scenery the dog faces.</summary>
        bool DogEye(float dt)
        {
            if (!DogValid(_dog)) return false;
            Transform root = dogRoots[_dog];
            Vector3 head = DogHead(_dog);
            Vector3 h = Heading(root.position, root.forward, dt);
            _dogEyeUser = SelfHead() && (_selfHead - head).magnitude <= dogEyeUserMax;
            if (_dogEyeUser)
            {
                _wantPos = DogEyePos(head, _selfHead, dogEyeSideDeg, dogEyeBack, dogEyeLift) + LeadOffset(_hVel, dogEyeTau);
                _wantLook = _selfHead;
                _wantFov = dogEyeFov;
            }
            else
            {
                Vector3 pos = DogEyeScenePos(head, h, dogEyeAhead, dogEyeSceneLift);
                _wantPos = pos + LeadOffset(_hVel, dogEyeTau);
                _wantLook = DogEyeSceneLook(pos, h, dogEyePitch) + LeadOffset(_hVel, lookTau);
                _wantFov = dogEyeSceneFov;
            }
            _wantTau = dogEyeTau;
            _subject = head; _subjectSize = 0.3f;
            return true;
        }

        /// <summary>スポット定点 (§4.5): a fixed data position; the look point leans spotDogPull toward the dog held at that wash station / the dog
        /// in the water play area nearest to the look point. Lost only when there is no spot.</summary>
        bool Spot(float dt)
        {
            int n = spotPositions != null ? spotPositions.Length : 0;
            if (n == 0 || spotLooks == null || spotLooks.Length != n) { _lostWord = 2; return false; }
            if (_spot < 0 || _spot >= n) _spot = 0;
            Vector3 look = spotLooks[_spot];
            int dog = -1;
            int st = spotStations != null && _spot < spotStations.Length ? spotStations[_spot] : -1;
            if (st >= 0)
            {
                if (washCalls != null && st < washCalls.Length && washCalls[st] != null && washCalls[st]._Arrived()) dog = washCalls[st]._HeldDog();
            }
            else if (waterSpot != null && dogRoots != null)
            {
                float best = 1e9f;
                for (int i = 0; i < dogRoots.Length; i++)
                {
                    if (!DogValid(i)) continue;
                    Vector3 b = DogSubject(i);
                    if (!waterSpot._Contains(b)) continue;
                    float d = (b - look).sqrMagnitude;
                    if (d < best) { best = d; dog = i; }
                }
            }
            if (DogValid(dog)) look = Vector3.Lerp(look, DogSubject(dog), spotDogPull);
            _wantFixed = true;
            _wantPos = spotPositions[_spot];
            _wantLook = look;
            _wantFov = spotFovs != null && _spot < spotFovs.Length && spotFovs[_spot] > 1f ? spotFovs[_spot] : 50f;
            _wantLookTau = spotLookTau;
            _subject = look; _subjectSize = 1f;
            return true;
        }

        int NearestSpot()
        {
            int n = spotPositions != null ? spotPositions.Length : 0;
            if (n == 0 || !SelfHead()) return 0;
            int best = 0; float bd = 1e18f;
            for (int k = 0; k < n; k++) { float d = (spotPositions[k] - _selfHead).sqrMagnitude; if (d < bd) { bd = d; best = k; } }
            return best;
        }

        /// <summary>ゆっくり一周 (§4.9): around me and my leashed dogs (centre smoothed orbitCenterTau), height orbitHeight + [↑][↓], _orbitSpeed
        /// deg/s ([←][→], §16). Starts in front of me. §16: the radius starts at R = orbitRadius × [近く][遠く] and, for 近づく / 離れる, goes
        /// linearly over the first full circle to R × orbitNearK (≥ orbitRadiusMin) / R × orbitFarK (≤ orbitRadiusMax), then stays there;
        /// ShotOrbitEnd once at that end. 近づく also raises the look point to the camera height on the way (Video/2b: at 1.5 m the head left the
        /// frame looking at the middle of the body; the feet / dog leave it below about 2.8 / 2.4 m - a close-up of the upper body by design).
        /// A new subject (mode entry, picker change, open) starts the radius and the angle again.
        /// A blocked part of the circle is shortened by the shared occlusion clip (not skipped).</summary>
        bool Orbit(float dt)
        {
            if (!SelfHead()) return false;
            int own = OwnDogsCentre(_selfHead, selfDogMax);
            Vector3 c = own > 0 ? (_selfFeet + _ownCentre * own) / (1 + own) : _selfFeet;
            c.y = _selfFeet.y;
            if (!_orbitHave)
            {
                _orbitHave = true; _orbitCentre = c; _orbitTravel = 0f; _orbitEnded = false;
                Vector3 f = new Vector3(_selfFwd.x, 0f, _selfFwd.z);
                _orbitAngle = f.sqrMagnitude > 0.0001f ? Mathf.Atan2(f.z, f.x) * Mathf.Rad2Deg : 0f;
            }
            _orbitCentre = Vector3.Lerp(_orbitCentre, c, BlendFactor(dt, orbitCenterTau));
            float step = _orbitSpeed * dt;
            _orbitAngle += step;
            if (_orbitAngle > 360f) _orbitAngle -= 360f;
            _orbitTravel = Mathf.Min(_orbitTravel + step, 720f);
            float progress = _orbitTravel / 360f;
            _orbitRadiusNow = OrbitRadiusAt(orbitRadius * CtlDist(), _orbitKind, progress, orbitNearK, orbitFarK, orbitRadiusMin, orbitRadiusMax);
            if (_orbitKind != OrbitSteady && !_orbitEnded && progress >= 1f) { _orbitEnded = true; RaiseShot(ShotOrbitEnd); }
            _wantPos = OrbitPos(_orbitCentre, _orbitAngle, _orbitRadiusNow, orbitHeight + CtlUp());
            _wantLook = _orbitCentre + Vector3.up * OrbitLookHeight(orbitLookUp, orbitHeight, _orbitKind, progress);
            _wantFov = orbitFov;
            _wantTau = orbitTau;
            _subject = _orbitCentre + Vector3.up * 0.8f; _subjectSize = 1f;
            return true;
        }

        /// <summary>通り過ぎる散歩 (§4.8): waits at the nearest pass point ahead (passNear*0.2 .. passFar in front of my dogs, or me), looks at
        /// them while they come, pass and leave; then the next point. Gives up after passGiveUp s without coming within passNear, or when they
        /// are passTooFar away. No point ahead: the じぶん framing (never lost).</summary>
        bool PassBy(float dt, float now)
        {
            if (!SelfHead()) return false;
            int own = OwnDogsCentre(_selfHead, 30f);
            Vector3 s = own > 0 ? _ownCentre : _selfFeet + Vector3.up * 0.4f;
            Vector3 h = Heading(s, _selfFwd, dt);
            int n = passPositions != null ? passPositions.Length : 0;
            if (_passIdx >= n) _passIdx = -1;
            if (_passIdx >= 0)
            {
                float d = (s - passPositions[_passIdx]).magnitude;
                if (d < _passMin) { _passMin = d; _passClosestAt = now; }
                if (d <= passNear) _passEverNear = true;
                bool done = _passEverNear && (d > _passMin + passRecede || (now - _passClosestAt > passAfter && d > _passMin + 1f));
                bool giveUp = (!_passEverNear && now - _passSince > passGiveUp) || d > passTooFar;
                if (done || giveUp) { if (done) RaiseShot(ShotPassed); _passLast = _passIdx; _passIdx = -1; _cutNext = true; }
            }
            if (_passIdx < 0)
            {
                float best = 1e9f;
                for (int k = 0; k < n; k++)
                {
                    if (k == _passLast) continue;
                    Vector3 to = passPositions[k] - s;
                    float d = to.magnitude;
                    if (d < passNear * 0.2f || d > passFar) continue;
                    to.y = 0f;
                    if (Vector3.Dot(to, h) <= 0f) continue;   // only points ahead
                    if (d < best) { best = d; _passIdx = k; }
                }
                if (_passIdx >= 0) { _passSince = now; _passMin = best; _passClosestAt = now; _passEverNear = best <= passNear; _cutNext = true; }
            }
            if (_passIdx < 0)
            {
                // nothing ahead: the じぶん framing from the same formulas (no second heading update)
                _wantPos = SelfFollowPos(_selfHead, h, selfSideDeg, own > 0 ? selfDistanceWithDog : selfDistance, selfLift) + LeadOffset(_hVel, posTau);
                _wantLook = SelfFollowLook(_selfHead, _ownCentre, own, selfLookToDogs, selfChestDrop);
                _wantFov = selfFov;
                _subject = _selfHead; _subjectSize = 0.5f;
                return true;
            }
            _wantFixed = true;
            _wantPos = passPositions[_passIdx];
            _wantLook = s + Vector3.up * passLookUp;
            _wantFov = passFov;
            _subject = s; _subjectSize = 1f;
            return true;
        }
    }
}
