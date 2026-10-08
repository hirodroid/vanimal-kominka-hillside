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
