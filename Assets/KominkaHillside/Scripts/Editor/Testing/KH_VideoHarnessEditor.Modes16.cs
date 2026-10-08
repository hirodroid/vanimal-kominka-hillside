using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-WLD-012 (design §16) ClientSim steps of the video harness (one client, simulated input):
    ///   V4-walk-modes (tail) 一周's speed ([→] × orbitSpeedStep up to orbitDegMax, [←] down to orbitDegMin, [戻す] = orbitDegPerSec; the around offset
    ///                 is untouched) and its ways ▶ 近づく / 離れる: the progress jumped to 350° (SIMULATED), ShotOrbitEnd, the end radius
    ///                 (KH_VideoCamera.OrbitRadiusAt) and the camera at that radius from the centre (unless clipped); ▶ wraps back to 一定
    ///   V5-play-modes (tail) みち, teleported onto the walking route (node 0 → node 1 of KH_WalkRoute.nodePositions): on the trail and the trail's
    ///                 direction, agreeing with my facing; 待って寄る placed pathAheadFar ahead, glides after a 6 m teleport toward it (inside
    ///                 pathApproachAt), comes close and ends with ShotMoveDone (pathHold 1 s for the run, SIMULATED); 後ずさり / 横ならび at PathPos;
    ///                 追いこし moving at pathPassSpeed, then jumped near its end (SIMULATED _pathOff) → hold → ShotMoveDone and a cut back behind;
    ///                 寄って引く moving at pathRecedeSpeed, jumped near its end → stops in the world → ShotMoveDone back to pathAheadNear.
    /// Not proven here (the user's PC): walking for real (ClientSim teleports), the direction through a trail node / a curve, the look of each move.
    /// </summary>
    public static partial class KH_VideoHarnessEditor
    {
        const string C4 = "V4-walk-modes", C5 = "V5-play-modes";
        static int v16Serial, v16Moves, v16CutsMark;
        static bool v16OnTrail;
        static float v16MinDist;

        static Vector3 PathDirNow() { return Get<Vector3>("_pathDir"); }
        static Vector3 Feet() { return Networking.LocalPlayer.GetPosition(); }

        static void AddOrbitWaySteps()
        {
            steps.Add(Do(C4, () =>
            {
                uVideo.SendCustomEvent("_Mode" + KH_VideoCamera.ModeOrbit);
                for (int i = 0; i < KH_VideoCamera.OrbitKindCount && Get<int>("_orbitKind") != KH_VideoCamera.OrbitSteady; i++) uVideo.SendCustomEvent("_DogNext");
                Expect(Get<int>("_orbitKind") == KH_VideoCamera.OrbitSteady, "一周: starts 一定");
                // the speed: [→] faster, [←] slower, within the limits; [戻す] gives the default; the around offset stays 0
                int m = KH_VideoCamera.ModeOrbit;
                Expect(Mathf.Abs(Get<float>("_orbitSpeed") - KH_VideoCameraEditor.OrbitDegPerSec) < 1e-3f, "一周: speed starts at " + KH_VideoCameraEditor.OrbitDegPerSec + "°/s");
                uVideo.SendCustomEvent("_CtlRight");
                Expect(Mathf.Abs(Get<float>("_orbitSpeed") - KH_VideoCameraEditor.OrbitDegPerSec * KH_VideoCameraEditor.OrbitSpeedStep) < 1e-3f, "一周: [→] × " + KH_VideoCameraEditor.OrbitSpeedStep);
                Press("_CtlRight", 10); Expect(Mathf.Abs(Get<float>("_orbitSpeed") - KH_VideoCameraEditor.OrbitDegMax) < 1e-3f, "一周: [→] stops at " + KH_VideoCameraEditor.OrbitDegMax + "°/s");
                Press("_CtlLeft", 20); Expect(Mathf.Abs(Get<float>("_orbitSpeed") - KH_VideoCameraEditor.OrbitDegMin) < 1e-3f, "一周: [←] stops at " + KH_VideoCameraEditor.OrbitDegMin + "°/s");
                Expect(Cv("_cvYaw")[m] == 0f, "一周: [←][→] did not turn the camera around (they are the speed here)");
                uVideo.SendCustomEvent("_CtlReset");
                Expect(Mathf.Abs(Get<float>("_orbitSpeed") - KH_VideoCameraEditor.OrbitDegPerSec) < 1e-3f, "一周: [戻す] gives " + KH_VideoCameraEditor.OrbitDegPerSec + "°/s again");
            }));
            foreach (int kind in new[] { KH_VideoCamera.OrbitCloser, KH_VideoCamera.OrbitFarther })
            {
                int way = kind;
                string tag = "一周 " + KH_VideoCamera.OrbitKindWord(way) + ": ";
                steps.Add(Do(C4, () =>
                {
                    uVideo.SendCustomEvent("_DogNext");
                    Expect(Get<int>("_orbitKind") == way, tag + "▶ selected it");
                    rendersMark = Renders();
                }));
                steps.Add(Until(C4, () => Renders() >= rendersMark + 3, 3, tag + "no renders"));
                steps.Add(Do(C4, () =>
                {
                    Expect(Get<float>("_orbitTravel") < 30f, tag + "a new way starts the circle again (" + Get<float>("_orbitTravel").ToString("F1") + "°)");
                    v16Serial = Get<int>("_shotSerial");
                    uVideo.SetProgramVariable("_orbitTravel", 350f);
                    Note(tag + "simulated: the circle's progress jumped to 350°");
                }));
                steps.Add(Until(C4, () => Get<int>("_shotSerial") > v16Serial && Get<int>("_shotKind") == KH_VideoCamera.ShotOrbitEnd, 5, tag + "no ShotOrbitEnd at the end of the first circle"));
                steps.Add(Do(C4, () => { timeMark = Time.realtimeSinceStartup; }));
                steps.Add(Until(C4, () => Time.realtimeSinceStartup - timeMark > 1.5f, 3, "timer"));
                steps.Add(Do(C4, () =>
                {
                    float want = KH_VideoCamera.OrbitRadiusAt(KH_VideoCameraEditor.OrbitRadius, way, 1f, KH_VideoCameraEditor.OrbitNearK, KH_VideoCameraEditor.OrbitFarK,
                        KH_VideoCameraEditor.OrbitRadiusMin, KH_VideoCameraEditor.OrbitRadiusMax);
                    float now = Get<float>("_orbitRadiusNow");
                    // Codex review (2026-10-08): the end is raised ONCE (a per-frame repeat would also have passed "it went up")
                    Expect(Get<int>("_shotSerial") == v16Serial + 1, tag + "ShotOrbitEnd raised once (serial " + Get<int>("_shotSerial") + ", want " + (v16Serial + 1) + ", 1.5 s after)");
                    Expect(Mathf.Abs(now - want) < 0.01f, tag + "the radius is its end " + want.ToString("F2") + " m (" + now.ToString("F2") + ")");
                    Vector3 d = cam.transform.position - Get<Vector3>("_orbitCentre"); d.y = 0f;
                    bool clipped = Get<float>("_clipDist") < 1e5f;
                    if (clipped) Note(tag + "clipped by terrain: the camera " + d.magnitude.ToString("F2") + " m from the centre (radius check relaxed)");
                    else Expect(Mathf.Abs(d.magnitude - want) < 0.5f, tag + "the camera is " + d.magnitude.ToString("F2") + " m from the centre (want " + want.ToString("F2") + ")");
                    Expect(Roll() < 0.01f, tag + "roll 0");
                }));
            }
            steps.Add(Do(C4, () =>
            {
                uVideo.SendCustomEvent("_DogNext");
                Expect(Get<int>("_orbitKind") == KH_VideoCamera.OrbitSteady, "一周: ▶ after 離れる wraps to 一定");
            }));
        }

        /// <summary>The wanted position of a CtlKindDistUp みち move standing still (no feed-forward), the controls at their defaults.</summary>
        static void ExpectPathAt(string tag, float along, float side)
        {
            Vector3 want = KH_VideoCamera.PathPos(Feet(), PathDirNow(), along, side, KH_VideoCameraEditor.PathHeight);
            float err = (Get<Vector3>("_wantPos") - want).magnitude;
            Expect(err < 0.15f, tag + "at PathPos(feet, dir, " + along.ToString("F2") + ", " + side.ToString("F2") + ", " + KH_VideoCameraEditor.PathHeight + ") (error " + err.ToString("F3") + " m)");
        }

        /// <summary>Codex review (2026-10-08): 0.5 s after a move's ShotMoveDone the serial must be exactly one above the mark (raised once, not every frame).</summary>
        static void AddOnceCheck(string tag)
        {
            steps.Add(Do(C5, () => { timeMark = Time.realtimeSinceStartup; }));
            steps.Add(Until(C5, () => Time.realtimeSinceStartup - timeMark >= 0.5f, 2, "timer"));
            steps.Add(Do(C5, () => Expect(Get<int>("_shotSerial") == v16Serial + 1, tag + "ShotMoveDone raised once (serial " + Get<int>("_shotSerial") + ", want " + (v16Serial + 1) + ", 0.5 s after)")));
        }

        static void AddPathSteps()
        {
            steps.Add(Do(C5, () =>
            {
                uVideo.SetProgramVariable("pathHold", 1f);
                Note("simulated: pathHold 1 s for the run (restored at the end / in cleanup)");
                var route = GetRaw("route") as UdonBehaviour;
                var nodes = route != null ? V5Get<Vector3[]>(route, "nodePositions") : null;
                v16OnTrail = nodes != null && nodes.Length >= 2;
                var lp = Networking.LocalPlayer;
                if (v16OnTrail)
                {
                    Vector3 f = nodes[1] - nodes[0]; f.y = 0f; f = f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
                    lp.TeleportTo(Ground(nodes[0] + f * 1f), Quaternion.LookRotation(f));
                    Note("みち: teleported onto the route between nodes 0 and 1 (" + nodes[0].ToString("F1") + " → " + nodes[1].ToString("F1") + ")");
                }
                else report.unproven.Add("みち on the trail (the rig has no route / the route has no nodePositions): the heading path only");
                uVideo.SendCustomEvent("_Mode" + KH_VideoCamera.ModePath);
                for (int i = 0; i < KH_VideoCamera.PathMoveCount && Get<int>("_pathMove") != KH_VideoCamera.PathWaitApproach; i++) uVideo.SendCustomEvent("_DogNext");
                rendersMark = Renders();
            }));
            steps.Add(Until(C5, () => Renders() >= rendersMark + 5, 4, "みち: no 5 real renders in 4 s"));
            steps.Add(Do(C5, () =>
            {
                var lp = Networking.LocalPlayer;
                Expect(Get<int>("_state") == KH_VideoCamera.StateFilming && Get<int>("_pathMove") == KH_VideoCamera.PathWaitApproach, "みち 待って寄る: filming");
                Vector3 dir = PathDirNow();
                Expect(Mathf.Abs(dir.y) < 1e-3f && Mathf.Abs(dir.magnitude - 1f) < 1e-3f, "みち: the walking direction is horizontal and unit");
                if (v16OnTrail)
                {
                    Expect(Get<bool>("_pathOnTrail"), "みち: my feet are on the trail (within pathTrailMax)");
                    Expect(Mathf.Abs(Vector3.Dot(dir, Get<Vector3>("_pathTrailDir"))) > 0.98f, "みち: the walking direction is the trail's (KH_WalkRoute._TrailDirection)");
                }
                Vector3 facing = lp.GetRotation() * Vector3.forward; facing.y = 0f;
                Expect(Vector3.Dot(dir, facing.normalized) > 0f, "みち: the direction agrees with my facing (standing: the heading falls back to it)");
                Vector3 feet = Feet(), anchor = Get<Vector3>("_pathAnchor"), to = anchor - feet; to.y = 0f;
                Expect(Get<int>("_pathPhase") == 0 && Mathf.Abs(to.magnitude - KH_VideoCameraEditor.PathAheadFar) < 0.5f && Vector3.Dot(to.normalized, dir) > 0.98f,
                    "待って寄る: placed " + to.magnitude.ToString("F2") + " m ahead (want " + KH_VideoCameraEditor.PathAheadFar + ")");
                Note("待って寄る: the point is " + (anchor.y - feet.y).ToString("F2") + " m above my feet (the ground under it + " + KH_VideoCameraEditor.PathHeight + ")");
                Expect(Mathf.Abs(anchor.y - feet.y) < 8f, "待って寄る: the point's height is sane");
                Expect((Get<Vector3>("_wantPos") - anchor).magnitude < 0.01f, "待って寄る: waits at the point");
                v16Serial = Get<int>("_shotSerial"); v16Moves = Get<int>("_pathMoves"); v16MinDist = 1e9f;
                lp.TeleportTo(Ground(feet + dir * 6f), lp.GetRotation());   // inside pathApproachAt (12 → 6 m)
                Note("待って寄る: teleported 6 m toward the point");
            }));
            steps.Add(Until(C5, () => Get<int>("_pathPhase") == 1, 3, "待って寄る did not start gliding after I came within pathApproachAt"));
            steps.Add(Until(C5, () =>
            {
                Vector3 d = cam.transform.position - Feet(); d.y = 0f;
                v16MinDist = Mathf.Min(v16MinDist, d.magnitude);
                return Get<int>("_pathMoves") > v16Moves;
            }, 1f + 3f * KH_VideoCameraEditor.PathGlideTau + 3f, "待って寄る did not finish (no ShotMoveDone)"));
            AddOnceCheck("待って寄る: ");
            steps.Add(Do(C5, () =>
            {
                Expect(Get<int>("_shotSerial") > v16Serial && Get<int>("_shotKind") == KH_VideoCamera.ShotMoveDone, "待って寄る: ShotMoveDone at the end of the move");
                Expect(v16MinDist < KH_VideoCameraEditor.PathAheadNear + 1.5f, "待って寄る: the camera came to " + v16MinDist.ToString("F2") + " m of me (pathAheadNear " + KH_VideoCameraEditor.PathAheadNear + ")");
                Vector3 to = Get<Vector3>("_pathAnchor") - Feet(); to.y = 0f;
                Expect(Get<int>("_pathPhase") == 0 && Mathf.Abs(to.magnitude - KH_VideoCameraEditor.PathAheadFar) < 0.5f, "待って寄る: a new point " + to.magnitude.ToString("F2") + " m ahead");
                uVideo.SendCustomEvent("_DogNext");
                Expect(Get<int>("_pathMove") == KH_VideoCamera.PathBackAway, "▶ 後ずさり");
                rendersMark = Renders();
            }));
            steps.Add(Until(C5, () => Renders() >= rendersMark + 3, 3, "後ずさり: no renders"));
            steps.Add(Do(C5, () =>
            {
                ExpectPathAt("後ずさり: ", KH_VideoCameraEditor.PathAheadNear, 0f);
                uVideo.SendCustomEvent("_DogNext");
                Expect(Get<int>("_pathMove") == KH_VideoCamera.PathBeside, "▶ 横ならび");
                rendersMark = Renders();
            }));
            steps.Add(Until(C5, () => Renders() >= rendersMark + 3, 3, "横ならび: no renders"));
            steps.Add(Do(C5, () =>
            {
                ExpectPathAt("横ならび: ", 0f, KH_VideoCameraEditor.PathBeside);
                uVideo.SendCustomEvent("_DogNext");
                Expect(Get<int>("_pathMove") == KH_VideoCamera.PathOvertake, "▶ 追いこし");
                timeMark = Time.realtimeSinceStartup;
            }));
            steps.Add(Until(C5, () => Time.realtimeSinceStartup - timeMark >= 1f, 3, "timer"));
            steps.Add(Do(C5, () =>
            {
                float off = Get<float>("_pathOff"), from = -KH_VideoCameraEditor.PathBehind;
                Expect(off > from + 0.8f && off < from + 2.4f, "追いこし: moved forward from " + from + " m at about " + KH_VideoCameraEditor.PathPassSpeed + " m/s (now " + off.ToString("F2") + " m)");
                ExpectPathAt("追いこし: ", off, KH_VideoCameraEditor.PathPassSide);
                v16Moves = Get<int>("_pathMoves"); v16CutsMark = Cuts(); v16Serial = Get<int>("_shotSerial");
                uVideo.SetProgramVariable("_pathOff", KH_VideoCameraEditor.PathAheadFar * 2f / 3f - 0.3f);
                Note("追いこし: simulated: jumped to 0.3 m before its end");
            }));
            steps.Add(Until(C5, () => Get<int>("_pathMoves") > v16Moves, 4, "追いこし did not finish (no ShotMoveDone)"));
            steps.Add(Do(C5, () =>
            {
                Expect(Get<int>("_shotKind") == KH_VideoCamera.ShotMoveDone, "追いこし: ShotMoveDone");
                Expect(Mathf.Abs(Get<float>("_pathOff") + KH_VideoCameraEditor.PathBehind) < 0.3f && Cuts() > v16CutsMark, "追いこし: a cut back to " + KH_VideoCameraEditor.PathBehind + " m behind me");
            }));
            AddOnceCheck("追いこし: ");   // after the immediate checks above (the offset moves on meanwhile; the next end is ~7 s away)
            steps.Add(Do(C5, () =>
            {
                uVideo.SendCustomEvent("_DogNext");
                Expect(Get<int>("_pathMove") == KH_VideoCamera.PathPullAway, "▶ 寄って引く");
                timeMark = Time.realtimeSinceStartup;
            }));
            steps.Add(Until(C5, () => Time.realtimeSinceStartup - timeMark >= 1f, 3, "timer"));
            steps.Add(Do(C5, () =>
            {
                float off = Get<float>("_pathOff"), from = KH_VideoCameraEditor.PathAheadNear;
                Expect(off > from + 0.4f && off < from + 1.4f, "寄って引く: moved away from " + from + " m at about " + KH_VideoCameraEditor.PathRecedeSpeed + " m/s (now " + off.ToString("F2") + " m)");
                ExpectPathAt("寄って引く: ", off, 0f);
                v16Moves = Get<int>("_pathMoves"); v16Serial = Get<int>("_shotSerial");
                uVideo.SetProgramVariable("_pathOff", KH_VideoCameraEditor.PathAheadFar - 0.1f);
                Note("寄って引く: simulated: jumped to 0.1 m before its far end");
            }));
            steps.Add(Until(C5, () => Get<int>("_pathPhase") == 1, 2, "寄って引く did not stop at its far end"));
            steps.Add(Do(C5, () => Expect((Get<Vector3>("_wantPos") - Get<Vector3>("_pathAnchor")).magnitude < 0.01f, "寄って引く: stops in the world at its far end")));
            steps.Add(Until(C5, () => Get<int>("_pathMoves") > v16Moves, 3, "寄って引く did not finish (no ShotMoveDone)"));
            steps.Add(Do(C5, () =>
            {
                Expect(Get<int>("_shotKind") == KH_VideoCamera.ShotMoveDone, "寄って引く: ShotMoveDone");
                Expect(Mathf.Abs(Get<float>("_pathOff") - KH_VideoCameraEditor.PathAheadNear) < 0.3f, "寄って引く: back to " + KH_VideoCameraEditor.PathAheadNear + " m in front");
                uVideo.SendCustomEvent("_DogNext");
                Expect(Get<int>("_pathMove") == KH_VideoCamera.PathWaitApproach, "▶ after 寄って引く wraps to 待って寄る");
                Expect(Roll() < 0.01f, "みち: roll 0");
                uVideo.SetProgramVariable("pathHold", KH_VideoCameraEditor.PathHold);
                Close();
                Note("not proven by this case: walking for real (ClientSim teleports), the direction through a trail node / a curve, the look of each move (the user's PC)");
            }));
            AddOnceCheck("寄って引く: ");   // after the immediate checks above (closed meanwhile: nothing more can be raised)
        }
    }
}
