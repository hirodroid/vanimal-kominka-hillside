using UdonSharp;
using UnityEngine;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-WLD-008 W3: the play / wash modes — フリスビー追従, 落下地点の待機, 洗浄の手元アップ (design §4.6-4.7, §4.10). おかえり / ふれあい were removed (§15.5).
    /// The disc is the selected dog's disc (one disc per dog). Synced facts are read as ids / states only (the thrower, the petter, the
    /// washer, the wash station's caller) and compared with my id; another player's pose is never read. When the person in a framing is not
    /// me, the mode frames the dog alone.
    /// </summary>
    public partial class KH_VideoCamera
    {
        // disc velocity (from positions; the disc's rotation is never used)
        private bool _discHave;
        private Vector3 _discPrev, _discVel;
        private bool _discFlying;           // read-out
        // landing wait
        private bool _landHave, _landWasFlying, _landSampling, _landMoved, _landLocal;
        private Vector3 _land, _landCam, _landDir, _landS0;
        private float _landS0At, _landNextRepredict;
        private int _landPredictions;       // read-out
        // the catch plan (キャッチ待ち)
        private bool _catchPlaced, _catchSignalled, _catchFromPlan, _catchWasFlying;
        private int _catchReplans;
        private float _catchWaitUntil;
        private Vector3 _catchPoint, _catchRootSeen, _catchCam, _catchLookPt;
        private int _washDog = -1;          // read-out

        KH_Frisbee DiscOf(int dog)
        {
            return discs != null && dog >= 0 && dog < discs.Length ? discs[dog] : null;
        }

        KH_FrisbeeCatch CatcherOf(int dog)
        {
            return catchers != null && dog >= 0 && dog < catchers.Length ? catchers[dog] : null;
        }

        /// <summary>The disc's smoothed velocity from its synced position (every client sees the same VRCObjectSync transform).</summary>
        void UpdateDiscVel(Vector3 p, float dt)
        {
            if (!_discHave) { _discPrev = p; _discHave = true; _discVel = Vector3.zero; }
            Vector3 v = (p - _discPrev) / Mathf.Max(dt, 0.001f);
            _discPrev = p;
            if (v.magnitude > 40f) v = Vector3.zero;   // a respawn / recall jump
            _discVel = Vector3.Lerp(_discVel, v, BlendFactor(dt, discVelTau));
        }

        /// <summary>Flying = the dog chases / jumps / lands for it (synced state), or the disc moves faster than discFlySpeed while not carried.
        /// KH_Frisbee.IsFlying is only right on the thrower's client, so it is not used.</summary>
        bool DiscFlying(int dog)
        {
            KH_FrisbeeCatch c = CatcherOf(dog);
            if (c == null) return _discVel.magnitude > discFlySpeed;
            int s = c.State;
            if (s == KH_FrisbeeCatch.StateChase || s == KH_FrisbeeCatch.StateJump || s == KH_FrisbeeCatch.StateLand) return true;
            return !c.IsCarrying && _discVel.magnitude > discFlySpeed;
        }

        /// <summary>フリスビー追従 (§4.6): beside the flying disc and the chasing dog, moving with the disc, framing both (the dog alone when they are
        /// further apart than discApartMax). Not flying (held, lying, carried): the 犬追従 framing of the dog.</summary>
        bool DiscFollow(float dt)
        {
            KH_Frisbee disc = DiscOf(_dog);
            if (!DogValid(_dog) || disc == null) return false;
            Vector3 p = disc.transform.position;
            UpdateDiscVel(p, dt);
            _discFlying = DiscFlying(_dog);
            if (!_discFlying) return DogFollowOf(_dog, dt);
            Vector3 body = DogSubject(_dog);
            _wantPos = DiscFollowPos(p, body, _discVel, discSideK, discSideMin, discSideMax, discUp) + LeadOffset(new Vector3(_discVel.x, 0f, _discVel.z), discTau);
            _wantLook = DiscFollowLook(p, body, discApartMax);
            _wantFov = discFov;
            _wantTau = discTau; _wantLookTau = discLookTau;
            _subject = p; _subjectSize = 0.3f;
            return true;
        }

        /// <summary>キャッチ待ち (§4.7, changed by the user 2026-10-04: "着地地点ではなくキャッチ地点"): when the throw starts, the dog's committed
        /// catch plan (KH_FrisbeeCatch.PlanReadable / PlanCatchT / PlanCatchRoot / PlanP0 / PlanV0 / PlanCurve) gives the catch point C =
        /// the disc at the catch time (KH_Frisbee._PositionAt). The camera is placed once, side-on to the flight at C's height, and waits; it
        /// is placed again only if the plan's catch root moves more than catchReplanMove (at most twice). The plan exists only on the dog's
        /// Owner: on any other client (or when no catch is planned: a blocked throw, an unreachable disc) after catchPlanWait the predicted
        /// LANDING is used instead (the previous 落下待ち). Raises ShotCatch once when the dog has the disc. Before the first throw: 犬追従.</summary>
        bool CatchWait(float dt, float now)
        {
            KH_Frisbee cd = DiscOf(_dog);
            KH_FrisbeeCatch cc = CatcherOf(_dog);
            if (DogValid(_dog) && cd != null)
            {
                bool fly = DiscFlying(_dog);
                if (fly && !_catchWasFlying) { _catchPlaced = false; _catchReplans = 0; _catchSignalled = false; _catchWaitUntil = now + catchPlanWait; }
                if (fly && cc != null && cc.PlanReadable)
                {
                    Vector3 root = cc.PlanCatchRoot;
                    if (!_catchPlaced || ((root - _catchRootSeen).magnitude > catchReplanMove && _catchReplans < 2))
                    {
                        if (_catchPlaced) _catchReplans++;
                        Vector3 v0 = cc.PlanV0;
                        float ct = cc.PlanCatchT;
                        Vector3 c = cd._PositionAt(cc.PlanP0, v0, cc.PlanCurve, ct);
                        Vector3 dir = cd._VelocityAt(v0, cc.PlanCurve, ct);
                        _catchPoint = c; _catchRootSeen = root; _catchPlaced = true; _catchFromPlan = true;
                        _catchCam = CatchWaitPos(c, dir, DogSubject(_dog), catchSide, catchUp);
                        _catchLookPt = CatchWaitLook(c, dir, catchLookBack);
                        _cutNext = true;
                    }
                }
                if (cc != null && !_catchSignalled && (_catchPlaced || _landHave) && (cc.IsCarrying || cc.State == KH_FrisbeeCatch.StatePickup))
                { _catchSignalled = true; RaiseShot(ShotCatch); }
                if (_catchPlaced)
                {
                    UpdateDiscVel(cd.transform.position, dt);
                    _catchWasFlying = fly;
                    _wantPos = _catchCam;
                    _wantLook = _catchLookPt;
                    _wantFov = catchFov;
                    _subject = _catchPoint; _subjectSize = 1f;
                    return true;
                }
                if (fly && now < _catchWaitUntil)
                {
                    // waiting for the plan (it is made a few frames after the throw on the dog's Owner): keep the dog framing meanwhile
                    UpdateDiscVel(cd.transform.position, dt);
                    _catchWasFlying = fly;
                    return DogFollowOf(_dog, dt);
                }
            }
            _catchFromPlan = false;
            if (DogValid(_dog) && cd != null) _catchWasFlying = DiscFlying(_dog);
            return Landing(dt, now);   // Landing keeps its own flight edge (_landWasFlying), so its prediction starts when the fallback begins
        }

        /// <summary>The fallback of キャッチ待ち (§4.7 as designed): at the start of a flight the landing L is predicted with KH_Frisbee._PositionAt —
        /// from the launch facts when I threw it (FlightLocal, the same curve as the aim line), otherwise from two synced positions
        /// landRemoteSample apart (curve 0). The camera is placed once beside L on the dog's side and waits; it moves once more only if a later
        /// prediction moves L by more than landMoveMax. Before the first throw: the 犬追従 framing.</summary>
        bool Landing(float dt, float now)
        {
            KH_Frisbee disc = DiscOf(_dog);
            if (!DogValid(_dog) || disc == null) return false;
            Vector3 p = disc.transform.position;
            UpdateDiscVel(p, dt);
            bool flying = DiscFlying(_dog);
            _discFlying = flying;
            Vector3 body = DogSubject(_dog);
            if (flying && !_landWasFlying)
            {
                _landMoved = false;
                if (disc.FlightLocal)
                {
                    _landLocal = true;
                    PlaceLanding(PredictLanding(disc, disc.FlightP0, disc.FlightV0, disc.FlightCurveNow), disc.FlightV0, body);
                }
                else { _landLocal = false; _landSampling = true; _landS0 = p; _landS0At = now; }
                _landNextRepredict = now + landRepredict;
            }
            if (_landSampling && now - _landS0At >= landRemoteSample)
            {
                _landSampling = false;
                Vector3 v = (p - _landS0) / Mathf.Max(now - _landS0At, 0.01f);
                PlaceLanding(PredictLanding(disc, p, v, 0f), v, body);
            }
            if (flying && _landHave && !_landMoved && !_landSampling && now >= _landNextRepredict)
            {
                _landNextRepredict = now + landRepredict;
                Vector3 l2 = PredictLanding(disc, p, _discVel, 0f);
                if ((l2 - _land).magnitude > landMoveMax) { _landMoved = true; PlaceLanding(l2, _discVel, body); }
            }
            _landWasFlying = flying;
            if (!_landHave) return DogFollowOf(_dog, dt);
            _wantPos = _landCam;
            _wantLook = LandingWaitLook(_land, p, (p - _land).magnitude < landNear);
            _wantFov = landFov;
            _subject = _land + Vector3.up * 0.5f; _subjectSize = 1f;
            return true;
        }

        void PlaceLanding(Vector3 landing, Vector3 dir, Vector3 dogBody)
        {
            _land = landing; _landDir = dir; _landHave = true; _landPredictions++;
            _landCam = LandingWaitPos(landing, dir, dogBody, landSide, landBeyond, landUp);
            _cutNext = true;
        }

        /// <summary>Steps the disc's own closed-form flight (KH_Frisbee._PositionAt) every landStep s up to landMaxT, stopping at the first hit
        /// on blockMask (the aim line's rule). About landMaxT / landStep calls, once per throw (plus at most one re-placement).</summary>
        Vector3 PredictLanding(KH_Frisbee disc, Vector3 p0, Vector3 v0, float curve)
        {
            Vector3 prev = p0;
            int steps = Mathf.Clamp(Mathf.RoundToInt(landMaxT / Mathf.Max(landStep, 0.02f)), 1, 400);
            for (int i = 1; i <= steps; i++)
            {
                Vector3 q = disc._PositionAt(p0, v0, curve, i * landStep);
                Vector3 seg = q - prev;
                float len = seg.magnitude;
                RaycastHit hit;
                if (len > 0.0001f && Physics.Raycast(prev, seg / len, out hit, len, blockMask, QueryTriggerInteraction.Ignore)) return hit.point;
                prev = q;
                if (q.y < p0.y - 60f) break;
            }
            return prev;
        }

        /// <summary>The world point of a wash PART (0..KH_DogWash.PartCount-1, what _LastStrokePart returns): the mean of the bones of every collider
        /// mapped to that part. KH_DogWash.partBones is indexed by COLLIDER (parallel to partColliders / partOfCollider, KH_DogWash.cs:194-196;
        /// the stroke stores partOfCollider[colliderIndex], :1183 / :1196), so partBones[part] was the wrong bone (W3 harness V5, 2026-10-04).
        /// No stroke yet (-1) or no collider of that part: the fallback (the dog's body).</summary>
        Vector3 PartPoint(KH_DogWash w, int part, Vector3 fallback)
        {
            if (part < 0) return fallback;
            Transform[] bones = w.partBones;
            int[] map = w.partOfCollider;
            if (bones == null || map == null) return fallback;
            Vector3 sum = Vector3.zero;
            int n = 0;
            int len = Mathf.Min(bones.Length, map.Length);
            for (int i = 0; i < len; i++)
                if (map[i] == part && bones[i] != null) { sum += bones[i].position; n++; }
            return n > 0 ? sum / n : fallback;
        }

        /// <summary>洗浄の手元アップ (§4.10): the dog held and arrived at the wash station I called (else the arrived station nearest to me within
        /// 6 m); the camera is on the far side of the last stroked part from the washer — me if I am the washer, otherwise the dog's left side
        /// is assumed (the washer is not read). No washing dog: lost ("洗っている犬がいません").</summary>
        bool Wash(float dt)
        {
            int me = SelfId();
            int dog = -1;
            if (washCalls != null)
            {
                for (int k = 0; k < washCalls.Length && dog < 0; k++)
                    if (washCalls[k] != null && washCalls[k]._Arrived() && washCalls[k]._CallerId() == me) dog = washCalls[k]._HeldDog();
                if (dog < 0 && SelfHead())
                {
                    float best = 36f;
                    for (int k = 0; k < washCalls.Length; k++)
                    {
                        if (washCalls[k] == null || !washCalls[k]._Arrived()) continue;
                        float d = (washCalls[k]._AnchorPosition() - _selfHead).sqrMagnitude;
                        if (d < best) { best = d; dog = washCalls[k]._HeldDog(); }
                    }
                }
            }
            _washDog = dog;
            if (!DogValid(dog)) { _lostWord = 1; return false; }
            KH_DogWash w = washes != null && dog < washes.Length ? washes[dog] : null;
            Vector3 body = DogSubject(dog);
            Vector3 part = w != null ? PartPoint(w, w._LastStrokePart(), body) : body;
            Vector3 washer = body - dogRoots[dog].right;
            if (w != null && w._WasherPlayerId() == me && SelfHead()) washer = _selfHead;
            _wantPos = WashPos(part, washer, washDist, washUp);
            _wantLook = part;
            _wantFov = washFov;
            _wantLookTau = washLookTau;
            _subject = part; _subjectSize = 0.3f;
            return true;
        }
    }
}
