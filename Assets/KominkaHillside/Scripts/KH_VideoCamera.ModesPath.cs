using UdonSharp;
using UnityEngine;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-WLD-012 (design §16, 2026-10-08): 背中ごし (TPS) and みち (moves along the walking direction, for a self-filmed Vlog).
    /// Both film ME only (SelfHead in Subjects.cs), also while another person is chosen for じぶん; another player is never read here.
    /// The formulas are the static functions in KH_VideoCamera.Modes.cs (YawDir / DirYaw / YawStep / TpsPos / TpsLook / PathTrailDir /
    /// PathPos), which the Edit-mode probe Video/2b calls too.
    ///
    /// 背中ごし: behind my head against my BODY facing (GetRotation, horizontal), smoothed by tpsYawTau so a quick turn does not swing the
    /// picture; to the right shoulder; looking tpsLookAhead in front of me (what I look at, over my shoulder). Controls: CtlKindAll.
    ///
    /// みち: the walking direction PathDir is the trail's direction (KH_WalkRoute, turned to agree with my heading) while my feet are within
    /// pathTrailMax of the trail, otherwise my heading (kept while I stand). The camera height is feet + pathHeight + [↑][↓]; every distance is
    /// × [近く][遠く] (CtlKindDistUp). The look point is じぶん's (head → my own dogs' centre). Five moves (the picker, _pathMove):
    ///   0 待って寄る: placed pathAheadFar ahead (the ground under it + pathHeight) and waits; when I come within pathApproachAt it glides
    ///     (τ pathGlideTau) to pathAheadNear in front of me and stays with me pathHold, then a cut to a new point ahead
    ///   1 後ずさり: pathAheadNear in front of me, backing away as I walk
    ///   2 横ならび: pathBeside to my right, walking with me
    ///   3 追いこし: from pathBehind behind me (pathPassSide to the right) it passes me at pathPassSpeed relative, holds pathHold at 2/3 of
    ///     pathAheadFar, then a cut back behind
    ///   4 寄って引く: from pathAheadNear in front it moves away at pathRecedeSpeed relative, stops (fixed in the world) at pathAheadFar for
    ///     pathHold, then a cut back to pathAheadNear
    /// [近く][遠く][↑][↓][戻す] restart 待って寄る / 追いこし / 寄って引く from their cut with the new values (Codex review, KH_VideoCamera.Controls.cs).
    /// The end of one move (the cut back) raises ShotMoveDone. The shared occlusion clip / floor / dog push-out apply to every move (on a
    /// curve the straight "ahead" can leave the trail; the clip pulls it toward me — accepted in v1, design §16).
    ///
    /// 隊列 (DES-WLD-004 M2, docs/plans/auto-walk-cart/implementation.md "Camera"): while I ride an おさんぽカート, every cart of my group (state
    /// Riding / Ending) and the leashed dogs of their riders in one frame. Read only: cartManager's synced arrays (cartGroup / cartState /
    /// cartRider), the carts' transforms, KH_LeashManager.walkers and the dogs' transforms; no player is read here (the riders are ids). The
    /// centre is the carts' centroid + convoyLift; the size is ConvoySize (the carts' spread around it + convoyDogMargin, or the dogs if further
    /// out, + one cart body); the distance is ConvoyDistance (that size in convoyFill of the screen height at convoyFov, convoyDistMin..Max);
    /// the camera is ConvoyPos: behind the travel direction (cartManager._CartForward(me), smoothed by headingTau like みち's direction),
    /// convoyBackDeg to its right and convoyElevDeg up. The feed-forward uses my cart's velocity (Heading on my cart: a cart joining the group
    /// moves the centroid, not my cart). Controls: CtlKindAll around the centre. While I do not ride (or cartManager is null) it is 背中ごし
    /// (Tps) without a word: the label stays 隊列; boarding / leaving is a cut. Never in a pattern (the cut table has no 隊列 row).
    /// </summary>
    public partial class KH_VideoCamera
    {
        // 背中ごし
        private bool _tpsHave;
        private float _tpsYaw;
        private Vector3 _tpsFwd;            // read-out: the smoothed body facing used on the last tick
        // みち
        private bool _pathHave, _pathOnTrail;
        // the picker (PathWaitApproach..PathPullAway). The CHOICE is kept for the whole stay, also across × and a reopen (like the spot and
        // 一周's way); only the move's progress (ResetModeState) and the control values (CtlResetAll on open) start again
        private int _pathMove;
        private int _pathPhase;             // 0 = placed / moving, 1 = glide + hold (待って寄る) / hold (追いこし・寄って引く)
        private float _pathPhaseAt, _pathOff, _pathYaw, _pathTrailAt;
        private Vector3 _pathAnchor, _pathCam, _pathDir, _pathTrailDir;
        private int _pathMoves;             // read-out: moves finished (ShotMoveDone raised)
        // 隊列
        private bool _convoyHave, _convoyRiding;
        private float _convoyYaw;
        private Vector3[] _convoyPos;       // the group's cart positions this tick (MaxCarts)
        private int[] _convoyRider;         // their riders' ids
        private int _convoyCarts, _convoyDogs;   // read-out: carts / dogs framed on the last tick
        private float _convoySize, _convoyDist;  // read-out: framing size and distance on the last tick
        private Vector3 _convoyCentre, _convoyMine;

        /// <summary>隊列 (DES-WLD-004 M2): my cart group and their dogs from behind; 背中ごし while I do not ride. Never lost while I am valid.</summary>
        bool Convoy(float dt)
        {
            int mine = cartManager != null ? cartManager._MyCart() : -1;
            int g = mine >= 0 ? cartManager._MyGroup() : -1;
            bool riding = g >= 0 && ConvoyGather(g, mine);
            // riding = my cart is on the track AND I am on it: the tick the cart becomes mine it may still stand at the parking place (its
            // pose is placed by the manager's next Update), and before the seat takes me I am still at the station; both stay 背中ごし
            if (riding && (!SelfHead() || FlatDist(_convoyMine, _selfFeet) > ConvoySeatMax)) riding = false;
            if (riding != _convoyRiding)
            {
                // boarding / leaving: a cut, the heading and the fallback's yaw start again (the label does not change)
                _convoyRiding = riding; _convoyHave = false; _tpsHave = false;
                ResetHeading(); _cutNext = true;
            }
            if (!riding) { _convoyCarts = 0; _convoyDogs = 0; return Tps(dt); }
            Vector3 f = cartManager._CartForward(SelfId());
            f.y = 0f;
            if (f.sqrMagnitude < 0.0001f)
            {
                f = cartManager.carts[mine].transform.forward; f.y = 0f;   // ConvoyGather checked the index and the cart
                if (f.sqrMagnitude < 0.0001f) f = _convoyHave ? YawDir(_convoyYaw) : Vector3.forward;
            }
            float target = DirYaw(f);
            if (!_convoyHave) { _convoyHave = true; _convoyYaw = target; }
            else _convoyYaw = YawStep(_convoyYaw, target, dt, headingTau);
            Vector3 fs = YawDir(_convoyYaw);
            Heading(_convoyMine, fs, dt);   // only for _hVel (the feed-forward); the direction is the cart's
            Vector3 centre = _convoyCentre + Vector3.up * convoyLift;
            _convoyDist = ConvoyDistance(_convoySize, convoyFill, convoyFov, convoyDistMin, convoyDistMax);
            _wantPos = ConvoyPos(centre, fs, convoyBackDeg, convoyElevDeg, _convoyDist) + LeadOffset(_hVel, posTau);
            _wantLook = centre + LeadOffset(_hVel, lookTau);
            _wantFov = convoyFov;
            _subject = centre; _subjectSize = _convoySize;
            return true;
        }

        /// <summary>隊列: the carts of group g in state Riding / Ending (their centroid, my cart's position, the size with the riders' dogs within
        /// convoyDogMax of their rider's cart). False when the arrays are not readable or my cart is not among them.</summary>
        bool ConvoyGather(int g, int mine)
        {
            KH_CartSeat[] cs = cartManager.carts;
            int[] cg = cartManager.cartGroup, st = cartManager.cartState, rd = cartManager.cartRider;
            if (cs == null || cg == null || st == null || rd == null) return false;
            int n = Mathf.Min(Mathf.Min(cs.Length, cg.Length), Mathf.Min(st.Length, rd.Length));
            if (mine < 0 || mine >= n || cs[mine] == null) return false;
            if (_convoyPos == null || _convoyPos.Length < n) { _convoyPos = new Vector3[n]; _convoyRider = new int[n]; }
            if (cg[mine] != g || (st[mine] != KH_CartManager.CartRiding && st[mine] != KH_CartManager.CartEnding)) return false;
            Vector3 me = cs[mine].transform.position;
            _convoyMine = me;
            int k = 0;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                if (cg[i] != g || cs[i] == null) continue;
                if (st[i] != KH_CartManager.CartRiding && st[i] != KH_CartManager.CartEnding) continue;
                Vector3 p = cs[i].transform.position;
                // a cart that is not on the track yet / any more (parked, the frame it is placed) is not part of the picture
                if (i != mine && (FlatDist(p, me) > ConvoyApartMax || Mathf.Abs(p.y - me.y) > ConvoyApartMax * 0.5f)) continue;
                _convoyPos[k] = p; _convoyRider[k] = rd[i]; k++;
                sum += p;
            }
            if (k == 0) return false;
            Vector3 c = sum / k;
            float rc = 0f;
            for (int j = 0; j < k; j++) rc = Mathf.Max(rc, FlatDist(_convoyPos[j], c));
            float ra = rc;
            int dogs = 0;
            int[] w = manager != null ? manager.walkers : null;
            if (w != null && dogRoots != null)
                for (int d = 0; d < dogRoots.Length && d < w.Length; d++)
                {
                    if (w[d] <= 0 || !DogValid(d)) continue;
                    Vector3 dp = dogRoots[d].position;
                    bool near = false;
                    for (int j = 0; j < k; j++)
                        if (_convoyRider[j] == w[d] && FlatDist(dp, _convoyPos[j]) <= convoyDogMax) { near = true; break; }
                    if (!near) continue;
                    dogs++;
                    ra = Mathf.Max(ra, FlatDist(dp, c));
                }
            _convoyCentre = c; _convoyCarts = k; _convoyDogs = dogs;
            _convoySize = ConvoySize(rc, ra, convoyDogMargin, convoyCartSize);
            return true;
        }

        /// <summary>隊列: a cart of my group further than this from mine (m) is skipped (a group of 8 at gap 3 m is 21 m long).</summary>
        const float ConvoyApartMax = 40f;
        /// <summary>隊列: I count as riding only while my feet are within this of my cart (m).</summary>
        const float ConvoySeatMax = 3f;

        static float FlatDist(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>背中ごし (§16): behind my head and over my right shoulder, looking where I look. Never lost while I am valid.</summary>
        bool Tps(float dt)
        {
            if (!SelfHead()) return false;
            Heading(_selfFeet, _selfBody, dt);   // only for _hVel (the feed-forward while walking); the facing is the body's
            float target = DirYaw(_selfBody);
            if (!_tpsHave) { _tpsHave = true; _tpsYaw = target; }
            else _tpsYaw = YawStep(_tpsYaw, target, dt, tpsYawTau);
            Vector3 f = YawDir(_tpsYaw);
            _tpsFwd = f;
            _wantPos = TpsPos(_selfHead, f, tpsBack, tpsShoulder, tpsLift) + LeadOffset(_hVel, posTau);
            _wantLook = TpsLook(_selfHead, f, tpsLookAhead, tpsLookDrop) + LeadOffset(_hVel, lookTau);
            _wantFov = tpsFov;
            _subject = _selfHead; _subjectSize = 0.5f;
            return true;
        }

        /// <summary>みち (§16): the selected move along PathDir. Never lost while I am valid.</summary>
        bool PathMove(float dt, float now)
        {
            if (!SelfHead()) return false;
            Vector3 feet = _selfFeet;
            Vector3 h = Heading(feet, _selfFwd, dt);
            Vector3 dir = PathDir(feet, h, dt, now);
            float k = CtlDist();
            float height = pathHeight + CtlUp();
            float far = pathAheadFar * k, near = pathAheadNear * k;
            Vector3 lead = LeadOffset(_hVel, posTau);
            if (!_pathHave)
            {
                _pathHave = true; _pathPhase = 0; _pathPhaseAt = now;
                if (_pathMove == PathWaitApproach) PathPlaceAhead(feet, dir, far, height);
                else if (_pathMove == PathOvertake) _pathOff = -pathBehind * k;
                else if (_pathMove == PathPullAway) _pathOff = near;
            }
            if (_pathMove == PathWaitApproach)
            {
                if (_pathPhase == 0)
                {
                    Vector3 to = _pathAnchor - feet; to.y = 0f;
                    float d = to.magnitude;
                    // walked past it or far away from it (turned back, a curve): place again ahead (not a finished move)
                    if (Vector3.Dot(to, dir) < 0f || d > far * 2f) PathPlaceAhead(feet, dir, far, height);
                    else if (d <= pathApproachAt * k) { _pathPhase = 1; _pathPhaseAt = now; _pathCam = _pathAnchor; }
                    _wantPos = _pathAnchor;
                }
                if (_pathPhase == 1)
                {
                    // the glide is this smoothing (the target moves with me), so the wanted point never jumps (no shared cut, whatever [遠く] is)
                    Vector3 target = PathPos(feet, dir, near, 0f, height) + LeadOffset(_hVel, pathGlideTau);
                    _pathCam = SmoothStep(_pathCam, target, false, dt, pathGlideTau);
                    _wantPos = _pathCam; _wantTau = 0.05f;
                    if (now - _pathPhaseAt >= pathHold + 3f * pathGlideTau) { PathMoveDone(); PathPlaceAhead(feet, dir, far, height); _wantPos = _pathAnchor; _wantTau = posTau; }
                }
            }
            else if (_pathMove == PathBackAway) _wantPos = PathPos(feet, dir, near, 0f, height) + lead;
            else if (_pathMove == PathBeside) _wantPos = PathPos(feet, dir, 0f, pathBeside * k, height) + lead;
            else if (_pathMove == PathOvertake)
            {
                float end = far * 2f / 3f;
                if (_pathPhase == 0)
                {
                    _pathOff += pathPassSpeed * dt;
                    if (_pathOff >= end) { _pathOff = end; _pathPhase = 1; _pathPhaseAt = now; }
                }
                else if (now - _pathPhaseAt >= pathHold) { PathMoveDone(); _pathOff = -pathBehind * k; _pathPhase = 0; _cutNext = true; }
                _wantPos = PathPos(feet, dir, _pathOff, pathPassSide * k, height) + lead;
            }
            else   // PathPullAway
            {
                if (_pathPhase == 0)
                {
                    _pathOff += pathRecedeSpeed * dt;
                    _wantPos = PathPos(feet, dir, Mathf.Min(_pathOff, far), 0f, height) + lead;
                    if (_pathOff >= far) { _pathOff = far; _pathPhase = 1; _pathPhaseAt = now; _pathAnchor = _wantPos; }   // stops here in the world
                }
                else
                {
                    _wantPos = _pathAnchor;
                    if (now - _pathPhaseAt >= pathHold) { PathMoveDone(); _pathOff = near; _pathPhase = 0; _cutNext = true; _wantPos = PathPos(feet, dir, near, 0f, height) + lead; }
                }
            }
            int own = OwnDogsCentre(_selfHead, selfDogMax);
            _wantLook = SelfFollowLook(_selfHead, _ownCentre, own, selfLookToDogs, selfChestDrop) + LeadOffset(_hVel, lookTau);
            _wantFov = pathFov;
            _subject = _selfHead; _subjectSize = 0.5f;
            return true;
        }

        /// <summary>みち's walking direction: on the trail (feet within pathTrailMax; rechecked 5 times a second) the trail's direction turned to
        /// agree with my heading, otherwise my heading. Smoothed by headingTau (the trail is a polyline: its direction turns at the nodes).</summary>
        Vector3 PathDir(Vector3 feet, Vector3 heading, float dt, float now)
        {
            if (route != null && now >= _pathTrailAt)
            {
                _pathTrailAt = now + 0.2f;
                _pathOnTrail = route._DistanceToTrail(feet) <= pathTrailMax;
                if (_pathOnTrail) _pathTrailDir = route._TrailDirection(feet);
            }
            Vector3 d = route != null && _pathOnTrail ? PathTrailDir(_pathTrailDir, heading) : heading;
            float y = DirYaw(d);
            _pathYaw = _pathHave ? YawStep(_pathYaw, y, dt, headingTau) : y;
            _pathDir = YawDir(_pathYaw);
            return _pathDir;
        }

        /// <summary>待って寄る: a new point `along` ahead, at the ground under it (one ray: the trail may climb or fall) + height; a cut.</summary>
        void PathPlaceAhead(Vector3 feet, Vector3 dir, float along, float height)
        {
            Vector3 p = PathPos(feet, dir, along, 0f, height);
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(p.x, feet.y + along * 0.5f + 2f, p.z), Vector3.down, out hit, along + 6f, blockMask, QueryTriggerInteraction.Ignore))
                p.y = hit.point.y + height;
            _pathAnchor = p; _pathPhase = 0; _cutNext = true;
        }

        void PathMoveDone()
        {
            _pathMoves++;
            RaiseShot(ShotMoveDone);
        }
    }
}
