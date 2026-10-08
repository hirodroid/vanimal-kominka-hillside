using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-WLD-008 W1 Video/3 - Harness (ClientSim, one client, about 1 minute). docs/plans/2026-10-03-video-camera.md §9.2.
    ///   V0-ready      after Start: PC build target, supported, the behaviour disabled itself, camera off, monitor / RecordSnap hidden,
    ///                 only [撮影カメラ] shown on the photo page
    ///   V1-lifecycle  _Open → renders → _Close, 10 times. Open: RT 1280x720 with depth is the camera target and the screen / RecordSnap texture,
    ///                 monitor and RecordSnap shown, behaviour enabled; render rate over 1 s ≤ 32/s and ≥ min(26, 0.85 × frame rate).
    ///                 Close (next frames): camera off, no target, RT field null and the RT object destroyed, screen texture cleared,
    ///                 monitor / RecordSnap hidden, behaviour disabled, no render while closed. After 10: the RenderTexture count and the
    ///                 system's object count equal the numbers before the first open
    ///   V2-pose-dog   a harness dog (a Transform pair put in place of dog 0 at runtime only) walks 3 m/s for 3 s on the ground in front of the player:
    ///                 the camera stays 0.3-5.3 m from the dog body (1.2-5 m when nothing clips), the body stays in the inner 80 % of the frame,
    ///                 roll 0, no per-frame jump > 0.5 m except a counted cut; a 20 m teleport of the dog produces a cut
    ///   V3-pose-self  じぶん with the player standing: the camera sits at head + rot(35°) × facing × 2.4 + up × −0.1 (when nothing clips),
    ///                 looks at the chest (no own dog), roll 0; a 15 m teleport of the player produces a cut
    /// Renders are counted with Camera.onPostRender for the rig's camera (not the rig's own _renders). The first failure stops the run after
    /// cleanup (close, restore dog 0 / _dog / _mode, destroy the harness dog); cases already finished are PASS, cases not started are SKIPPED.
    ///   V4-walk-modes (W2, §16) 背中ごし / 犬目線 / 定点 / 一周 / 通り過ぎ, each selected with its _ModeN: within 4 s at least 5 real renders, roll 0,
    ///                 the camera within a sane distance of its subject (背中ごし at TpsPos of my head and body facing and behind me, 犬目線 3 m of the
    ///                 dog's head, 一周 2.5-5 m of me, 通り過ぎ ≤ passFar + 5 m of me), 定点 exactly at its data position (a fixed point, after the cut).
    ///                 Then 一周's ways (§16): ▶ = 近づく / 離れる, the circle's progress jumped to 350° (SIMULATED), ShotOrbitEnd at the end radius
    ///                 (OrbitRadiusAt) and the camera at that radius from the centre; [→] / [←] / [戻す] change the speed within its limits
    ///   V5-play-modes (W3) one REAL throw (the disc's own _CheckThrow → _Launch → ReqCatch) with キャッチ待ち selected: the catch plan or the
    ///                 landing fallback places the camera (which one is noted); ディスク during the flight; (おかえり / ふれあい removed in §15.5,
    ///                 手元 in §16). §16 みち on the walking route (teleported onto a trail node): on the trail, the direction is the trail's; each of
    ///                 the 5 moves at its formula (PathPos), 待って寄る glides after I come within pathApproachAt (a teleport) and ends with
    ///                 ShotMoveDone; 追いこし / 寄って引く are jumped near their end (SIMULATED _pathOff) and end with ShotMoveDone (pathHold 1 s for
    ///                 the run). Reads that a run did not reach go to report.unproven (and the reason line), never to a silent PASS.
    /// **Subset**: write "V1,V3" to Temp/kh-video/harness-cases.txt before Video/3 (read and deleted; V0 always runs). Results Logs/kh-video-harness/latest.json.
    /// Simulated input (Udon events sent directly). Not judged here: VRChat camera snap (ClientSim never sets _VRChatCameraMode nor creates the
    /// photo camera), the look of the picture, the face in the picture (ClientSim's mirror copy differs), cost, recording.
    ///   V7-controls-drone (§15.5) the camera controls and the camera drone (KH_VideoHarnessEditor.Controls.cs). Run alone (--cases=V7).
    ///   V6-permit     (W4) other-person filming permission, simulated (KH_VideoHarnessEditor.Permit.cs). Run it alone (--cases=V6, about 90 s).
    ///   V8-patterns   (§17) the pattern player and board, おかえり, おまかせ with two real throws (KH_VideoHarnessEditor.Patterns.cs). Run it alone (--cases=V8).
    /// </summary>
    [InitializeOnLoad]
    public static partial class KH_VideoHarnessEditor
    {
        const string Key = "KH.VideoHarness", Dir = "Logs/kh-video-harness", Journal = Dir + "/journal.json";
        public const string CasesFile = "Temp/kh-video/harness-cases.txt";
        public static readonly string[] AllCases = { "V0-ready", "V1-lifecycle", "V2-pose-dog", "V3-pose-self", "V4-walk-modes", "V5-play-modes", "V6-permit", "V7-controls-drone", "V8-patterns" };
        const int Cycles = 10;

        public static List<string> Selected(string request)
        {
            if (string.IsNullOrWhiteSpace(request)) return AllCases.ToList();
            var want = new HashSet<string> { "V0-ready" };
            foreach (var raw in request.Split(new[] { ',', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var hit = AllCases.Where(c => c == raw || c.StartsWith(raw + "-", StringComparison.Ordinal)).ToArray();
                if (hit.Length != 1) throw new InvalidOperationException("unknown video harness case '" + raw + "' (known: " + string.Join(", ", AllCases) + ")");
                want.Add(hit[0]);
            }
            return AllCases.Where(want.Contains).ToList();
        }

        [Serializable] public class Case { public string name, status = "PENDING", reason; public List<string> notes = new List<string>(); }
        [Serializable] public class Report
        {
            public string runId, status = "RUNNING", reason, utc, endedUtc, scene, hashBefore, hashAfter, buildTarget, casesRequested = "";
            public List<string> casesRun = new List<string>();
            public bool sceneFileUnchanged, originalBackground;
            public double deadline;
            public List<Case> cases = new List<Case>();
            public List<string> errors = new List<string>();
            public List<string> unproven = new List<string>();   // V5: reads this run did not reach (left to the user's play)
            public string limitations = "Single local ClientSim, Udon events sent directly. Not judged: the VRChat camera snap (_VRChatCameraMode / the photo camera do not exist in ClientSim), "
                + "the picture's look and the face (mirror copy differs), cost, recording, other players.";
        }

        static Report report;
        static bool ending;
        static int step = -1, lastFrame = -1;
        static double stepAt;
        static List<Step> steps;
        class Step { public string caseName; public Func<bool> until; public Action action; public double timeout; public string fail; }

        static KH_VideoHarnessEditor()
        {
            EditorApplication.update += Tick; EditorApplication.playModeStateChanged += Mode;
            Application.logMessageReceived += Log;
            if (!SessionState.GetBool(Key, false)) return;
            try { report = JsonUtility.FromJson<Report>(File.ReadAllText(Journal)); ending = report.status != "RUNNING"; }
            catch (Exception e) { Debug.LogError("[KH Video Harness] journal unreadable: " + e.Message); SessionState.SetBool(Key, false); report = null; }
        }

        [MenuItem("Tools/KominkaHillside/Video/3 - Harness")]
        public static void Run()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            if (SessionState.GetBool(Key, false)) throw new InvalidOperationException("video harness already running (Video/3b stops it)");
            var target = EditorUserBuildSettings.activeBuildTarget;
            var root = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == KH_VideoCameraEditor.SystemName);
            if (root == null || root.GetComponent<KH_VideoCamera>() == null || target == BuildTarget.Android || target == BuildTarget.iOS)
            {
                Debug.LogError("[KH Video Harness] VIDEO_HARNESS_BLOCKED needs Video/1 and a PC build target (system " + (root != null) + ", target " + target + "); Play not entered");
                return;
            }
            report = new Report { runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"), utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath,
                originalBackground = Application.runInBackground, buildTarget = target.ToString() };
            report.hashBefore = Hash(report.scene);
            if (File.Exists(CasesFile)) { report.casesRequested = File.ReadAllText(CasesFile).Trim(); File.Delete(CasesFile); }
            report.casesRun = Selected(report.casesRequested);
            Debug.Log("[KH Video Harness] cases: " + string.Join(", ", report.casesRun));
            report.deadline = EditorApplication.timeSinceStartup + 240;
            Application.runInBackground = true;
            Save(); ending = false; step = -1; steps = null;
            SessionState.SetBool(Key, true);
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("Tools/KominkaHillside/Video/3b - Stop Harness")]
        public static void Stop() { End("STOPPED", "operator stop"); }

        static void Mode(PlayModeStateChange state)
        {
            if (report == null || !SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { step = -1; steps = null; stepAt = EditorApplication.timeSinceStartup; }
            if (state == PlayModeStateChange.ExitingPlayMode && !ending) { report.status = "STOPPED"; report.reason = "Play stopped externally"; ending = true; SafeSave(); }
            if (state == PlayModeStateChange.EnteredEditMode) Finish();
        }

        static void Log(string condition, string stack, LogType type)
        {
            if (report == null || !SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
            if (condition != null && condition.Contains("Runtime error detected, but the client has not been launched with")) return;
            if ((type == LogType.Exception || type == LogType.Error || type == LogType.Assert) && report.errors.Count < 20) report.errors.Add(condition);
        }

        static void Tick()
        {
            if (report == null || !SessionState.GetBool(Key, false)) return;
            if (ending) { if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish(); else if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); return; }
            if (EditorApplication.timeSinceStartup > report.deadline) { End("TIMEOUT", "global watchdog (240 s)"); return; }
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            if (lastFrame == Time.frameCount) return; lastFrame = Time.frameCount;
            // between frames (EditorApplication.update is outside the player loop) the camera must be disabled: the tick enables it in
            // PostLateUpdate and its own OnPostRender disables it within the same frame
            if (cam != null && cam.enabled) enabledBetweenFrames++;
            try
            {
                if (steps == null)
                {
                    if (EditorApplication.timeSinceStartup - stepAt < 3) return;
                    if (!Utilities.IsValid(Networking.LocalPlayer)) return;
                    Resolve(); MakeSteps(); step = 0; stepAt = EditorApplication.timeSinceStartup; Save();
                }
                while (step < steps.Count)
                {
                    var s = steps[step];
                    var cs = report.cases.First(x => x.name == s.caseName);
                    current = cs;
                    if (cs.status == "PENDING") cs.status = "RUNNING";
                    if (s.action != null) s.action();
                    else if (!s.until())
                    {
                        if (EditorApplication.timeSinceStartup - stepAt > s.timeout) Fail(s.fail + " (" + s.timeout + " s)");
                        else { if (cs.status == "FAIL") StopOnFail(cs); return; }
                    }
                    // **the first failure stops the run**: clean up (close, restore the substituted references and heap values) and leave Play
                    if (cs.status == "FAIL") { StopOnFail(cs); return; }
                    step++; stepAt = EditorApplication.timeSinceStartup;
                    if (s.action == null) return;   // a wait that ended: next step on the next frame
                }
                foreach (var cs in report.cases) if (cs.status == "PENDING" || cs.status == "RUNNING") cs.status = "PASS";
                Cleanup();
                bool pass = report.cases.All(x => x.status == "PASS") && report.errors.Count == 0;
                End(pass ? "PASS" : "FAIL", (pass ? "all video cases passed (local ClientSim, simulated input)" : "runtime errors: " + report.errors.Count)
                    + (report.unproven.Count > 0 ? " | NOT PROVEN by this run: " + string.Join("; ", report.unproven) : ""));
            }
            catch (Exception e) { Cleanup(); End("FAIL", "harness exception: " + e.Message); }
        }

        // ================================================================ runtime handles

        static UdonBehaviour uVideo;
        static GameObject root, monitor, snap;
        static Camera cam;
        static Renderer screen;
        static Case current;
        static int rtBefore, objectsBefore, rendersMark, framesMark, cutsMark, opensBefore, closesBefore;
        static float timeMark;
        static RenderTexture lastRt;
        static Transform[] savedRoots, savedHeads;
        static GameObject fakeDog;
        static Vector3 fakeDir, lastCamPos;
        static int lastCuts, poseFrames, distBad, frameBad, rollBad, jumpBad, clippedFrames;
        static float maxStep;
        static int v2Seen;
        static int realRenders, savedDog = -1, savedMode = -1, enabledMark;
        // diagnostics: renders per distinct Time.frameCount ("two renders in one frame" vs "rendered on frames it was not asked to")
        static int lastRenderFrame = -1, distinctRenderFrames, framesWithTwo, distinctMark, twoMark;

        /// <summary>The first failure stops the run: the remaining cases are SKIPPED, the rig is cleaned up, Play ends (the scene file is never saved).</summary>
        static void StopOnFail(Case cs)
        {
            foreach (var c in report.cases)
            {
                if (c == cs) continue;
                if (c.status == "RUNNING") c.status = "PASS";        // ran to its end before the failing case started (cases run in order)
                else if (c.status == "PENDING") c.status = "SKIPPED"; // never started
            }
            Cleanup();
            End("FAIL", cs.name + ": " + cs.reason);
        }

        /// <summary>Undo everything the harness changed at runtime: close the rig, give dog 0 back, restore _dog / _mode, drop the harness dog,
        /// stop counting renders. Safe to call more than once and after a partial run.</summary>
        static void Cleanup()
        {
            try
            {
                if (uVideo != null)
                {
                    if (GetRaw("_open") is bool open && open) Close();
                    if (savedRoots != null) uVideo.SetProgramVariable("dogRoots", savedRoots);
                    if (savedHeads != null) uVideo.SetProgramVariable("dogHeads", savedHeads);
                    if (savedDog >= 0) uVideo.SetProgramVariable("_dog", savedDog);
                    if (savedMode >= 0) uVideo.SetProgramVariable("_mode", savedMode);
                }
            }
            catch (Exception e) { report.errors.Add("cleanup: " + e.Message); }
            savedRoots = null; savedHeads = null;
            if (fakeDog != null) { UnityEngine.Object.Destroy(fakeDog); fakeDog = null; }
            try { if (uVideo != null) uVideo.SetProgramVariable("pathHold", KH_VideoCameraEditor.PathHold); } catch (Exception e) { report.errors.Add("cleanup V5: " + e.Message); }
            try { V6Cleanup(); } catch (Exception e) { report.errors.Add("cleanup V6: " + e.Message); }
            Camera.onPostRender -= OnPostRender;
        }

        /// <summary>Real renders of the rig's camera (built-in pipeline callback), counted independently of the rig's own _renders
        /// (which only says the camera was enabled).</summary>
        static void OnPostRender(Camera c)
        {
            if (c == null || c != cam) return;
            realRenders++;
            int f = Time.frameCount;
            if (f == lastRenderFrame) { framesWithTwo++; } else { distinctRenderFrames++; lastRenderFrame = f; }
        }

        static void Resolve()
        {
            if (report.casesRun == null || report.casesRun.Count == 0) report.casesRun = AllCases.ToList();
            var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true);
            var m = all.Where(u => u.programSource != null && u.programSource.name == "KH_VideoCamera").ToArray();
            if (m.Length != 1) throw new InvalidOperationException("KH_VideoCamera x" + m.Length);
            uVideo = m[0];
            root = uVideo.gameObject;
            cam = root.transform.Find(KH_VideoCameraEditor.CameraName).GetComponent<Camera>();
            monitor = root.transform.Find(KH_VideoCameraEditor.MonitorName).gameObject;
            snap = root.transform.Find(KH_VideoCameraEditor.SnapName).gameObject;
            screen = monitor.transform.Find(KH_VideoCameraEditor.ScreenName).GetComponent<Renderer>();
            realRenders = 0;
            Camera.onPostRender -= OnPostRender; Camera.onPostRender += OnPostRender;
            savedDog = Get<int>("_dog"); savedMode = Get<int>("_mode");
        }

        static T Get<T>(string key)
        {
            if (uVideo == null || !uVideo.TryGetProgramVariable(key, out object v) || !(v is T)) throw new InvalidOperationException("missing runtime symbol " + key + " on KH_VideoCamera");
            return (T)v;
        }
        static object GetRaw(string key) { uVideo.TryGetProgramVariable(key, out object v); return v; }
        static RenderTexture Rt() { return GetRaw("_rt") as RenderTexture; }
        /// <summary>Renders that really happened (Camera.onPostRender for the rig's camera).</summary>
        static int Renders() { return realRenders; }
        /// <summary>The rig's own count of frames it enabled the camera (for comparison only).</summary>
        static int Enabled() { return Get<int>("_ticks"); }
        /// <summary>Renders the rig saw through KH_VideoCameraHook.OnPostRender (its own count).</summary>
        static int Callbacks() { return Get<int>("_renders"); }
        static int Missed() { return Get<int>("_missedCallbacks"); }
        static int enabledBetweenFrames, enabledSeenMark, callbacksMark, missedMark;
        static int Cuts() { return Get<int>("_cuts"); }
        static GameObject Entry(string key) { return GetRaw(key) as GameObject; }

        static void Expect(bool ok, string what) { if (ok) current.notes.Add("ok " + what); else Fail(what); }
        static void Fail(string why) { if (current.status != "FAIL") { current.status = "FAIL"; current.reason = why; } current.notes.Add("FAIL " + why); }
        static void Note(string s) { current.notes.Add(s); }
        static Step Do(string c, Action a) { return new Step { caseName = c, action = a }; }
        static Step Until(string c, Func<bool> f, double t, string fail) { return new Step { caseName = c, until = f, timeout = t, fail = fail }; }
        static bool On(string name) { return report.casesRun.Contains(name); }
        static int ObjectCount() { return root.GetComponentsInChildren<Transform>(true).Length; }
        static int RtCount() { return Resources.FindObjectsOfTypeAll<RenderTexture>().Length; }
        static HashSet<int> rtIdsBefore = new HashSet<int>();
        static List<RenderTexture> ourRts = new List<RenderTexture>();
        /// <summary>Alive RenderTextures created since V0 that look like ours (RenderWidth x RenderHeight, depth, ARGB32), other systems' RTs excluded by shape.</summary>
        static int StrayVideoRts()
        {
            return Resources.FindObjectsOfTypeAll<RenderTexture>().Count(t => t != null && !rtIdsBefore.Contains(t.GetInstanceID()) && t.width == KH_VideoCameraEditor.RenderWidth
                && t.height == KH_VideoCameraEditor.RenderHeight && t.depth >= 16 && t.format == RenderTextureFormat.ARGB32 && !IsPhotoCapture(t));
        }
        /// <summary>The photo system's runtime capture RT has the same shape; read it from the Udon heap (the U# proxy keeps the template asset in Play).</summary>
        static bool IsPhotoCapture(RenderTexture t)
        {
            foreach (var u in UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true))
                if (u.programSource != null && u.programSource.name == "KD_PhotoCapture" && u.TryGetProgramVariable("captureTexture", out object v) && v as RenderTexture == t) return true;
            return false;
        }
        static float Roll() { return Mathf.Abs(Vector3.Dot(cam.transform.right, Vector3.up)); }

        /// <summary>V4 / V5: roll 0, filming, and the camera at a sane distance of the mode's subject.</summary>
        static void CheckMode(int m)
        {
            string tag = "mode " + m + " " + KH_VideoCamera.ModeWord(m) + ": ";
            Expect(Roll() < 0.01f, tag + "roll 0");
            Expect(Get<int>("_state") == KH_VideoCamera.StateFilming, tag + "filming (state " + Get<int>("_state") + ")");
            Vector3 c = cam.transform.position;
            var lp = Networking.LocalPlayer;
            Vector3 me = lp.GetPosition();
            var roots = Get<Transform[]>("dogRoots"); var heads = Get<Transform[]>("dogHeads");
            int dog = Get<int>("_dog");
            Vector3 dogHead = heads != null && dog >= 0 && dog < heads.Length && heads[dog] != null ? heads[dog].position : roots[dog].position;
            if (m == KH_VideoCamera.ModeDogEye) Expect((c - dogHead).magnitude <= 3f, tag + "within 3 m of the dog's head (" + (c - dogHead).magnitude.ToString("F2") + ")");
            if (m == KH_VideoCamera.ModeTps)
            {
                // §16: behind my head against my body facing (GetRotation), over the right shoulder; standing, so no feed-forward
                var td = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                Vector3 f = lp.GetRotation() * Vector3.forward; f.y = 0f; f = f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
                Vector3 want = KH_VideoCamera.TpsPos(td.position, f, KH_VideoCameraEditor.TpsBack, KH_VideoCameraEditor.TpsShoulder, KH_VideoCameraEditor.TpsLift);
                bool clipped = Get<float>("_clipDist") < 1e5f;
                float err = (c - want).magnitude;
                if (clipped) Note(tag + "clipped by terrain: position check relaxed (error " + err.ToString("F2") + " m)");
                else Expect(err < 0.35f, tag + "at TpsPos(head, body facing, " + KH_VideoCameraEditor.TpsBack + ", " + KH_VideoCameraEditor.TpsShoulder + ", " + KH_VideoCameraEditor.TpsLift + ") (error " + err.ToString("F2") + " m)");
                Vector3 toCam = c - td.position; toCam.y = 0f;
                Expect(Vector3.Dot(toCam.normalized, f) < -0.5f, tag + "behind me");
                Vector3 lookWant = KH_VideoCamera.TpsLook(td.position, f, KH_VideoCameraEditor.TpsLookAhead, KH_VideoCameraEditor.TpsLookDrop);
                Expect((Get<Vector3>("_look") - lookWant).magnitude < 0.2f, tag + "looks " + KH_VideoCameraEditor.TpsLookAhead + " m ahead of my head (error " + (Get<Vector3>("_look") - lookWant).magnitude.ToString("F2") + " m)");
                Expect(Vector3.Dot(Get<Vector3>("_tpsFwd"), f) > 0.99f, tag + "the smoothed facing has reached my body facing (standing)");
            }
            if (m == KH_VideoCamera.ModeOrbit) { float d = (c - me).magnitude; Expect(d >= 2.5f && d <= 5f, tag + "2.5-5 m from me (" + d.ToString("F1") + ")"); }
            // the product waits at a pass point up to passFar (120 m) ahead, or uses the じぶん framing (≈ 2-3.5 m) when none is ahead
            if (m == KH_VideoCamera.ModePassBy) { float d = (c - me).magnitude; Expect(d <= KH_VideoCameraEditor.PassFar + 5f, tag + "within passFar + 5 m of me (" + d.ToString("F1") + ")"); }
            if (m == KH_VideoCamera.ModeSpot)
            {
                var sp = Get<Vector3[]>("spotPositions"); int k = Get<int>("_spot");
                Expect(sp != null && k >= 0 && k < sp.Length && (c - sp[k]).magnitude < 0.05f, tag + "at its data position " + (sp != null && k >= 0 && k < sp.Length ? sp[k].ToString("F1") : "-"));
            }
            if (m == KH_VideoCamera.ModeDisc || m == KH_VideoCamera.ModeCatchWait)
                Expect((c - dogHead).magnitude <= 6f, tag + "no flight: the dog framing, within 6 m of the dog (" + (c - dogHead).magnitude.ToString("F2") + ")");
        }

        static void Open() { uVideo.SendCustomEvent("_Open"); }
        static void Close() { uVideo.SendCustomEvent("_Close"); }

        static void CheckOpen(string tag)
        {
            var rt = Rt();
            lastRt = rt;
            if (rt != null && !ourRts.Contains(rt)) ourRts.Add(rt);
            Expect(rt != null && rt.width == KH_VideoCameraEditor.RenderWidth && rt.height == KH_VideoCameraEditor.RenderHeight && rt.depth >= 16, tag + "RT " + (rt != null ? rt.width + "x" + rt.height + " depth " + rt.depth : "null"));
            Expect(rt != null && cam.targetTexture == rt, tag + "the RT is the camera target");
            Expect(rt != null && screen.sharedMaterial != null && screen.sharedMaterial.mainTexture == rt, tag + "the monitor screen shows the RT");
            Expect(rt != null && snap.GetComponent<Renderer>().sharedMaterial.mainTexture == rt, tag + "RecordSnap shows the RT");
            Expect(monitor.activeSelf && snap.activeSelf && uVideo.enabled && Get<bool>("_open"), tag + "monitor + RecordSnap shown, behaviour enabled, _open");
            var eo = Entry("entryOpen"); var er = Entry("entryRecall"); var ec = Entry("entryClose");
            Expect(eo != null && !eo.activeSelf && er != null && er.activeSelf && ec != null && ec.activeSelf, tag + "entry shows [モニターを呼ぶ] [しまう]");
        }

        static void CheckClosed(string tag)
        {
            Expect(!cam.enabled && cam.targetTexture == null, tag + "camera off, no target");
            Expect(Rt() == null && (lastRt == null || !lastRt), tag + "RT field null and the RT object destroyed");
            Expect(screen.sharedMaterial == null || screen.sharedMaterial.mainTexture == null, tag + "screen texture cleared");
            Expect(snap.GetComponent<Renderer>().sharedMaterial.mainTexture == null, tag + "RecordSnap texture cleared");
            Expect(!monitor.activeSelf && !snap.activeSelf && !uVideo.enabled && !Get<bool>("_open"), tag + "monitor / RecordSnap hidden, behaviour disabled");
            var eo = Entry("entryOpen");
            Expect(eo != null && eo.activeSelf, tag + "entry shows [撮影カメラ] again");
        }

        static Vector3 Ground(Vector3 p)
        {
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out hit, 10f, KH_PhotoGalleryEditor.CameraBlockMask, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
            return p;
        }

        // ================================================================ cases

        static void MakeSteps()
        {
            steps = new List<Step>();
            foreach (var name in report.casesRun) report.cases.Add(new Case { name = name });

            // ---- V0 ----
            steps.Add(Until("V0-ready", () => Get<bool>("_started"), 10, "KH_VideoCamera.Start did not run"));
            steps.Add(Do("V0-ready", () =>
            {
                Expect(Get<bool>("_supported"), "supported on " + report.buildTarget);
                Expect(!uVideo.enabled && !cam.enabled && !monitor.activeSelf && !snap.activeSelf, "closed at start: behaviour disabled, camera off, monitor / RecordSnap hidden");
                var eo = Entry("entryOpen"); var er = Entry("entryRecall"); var ec = Entry("entryClose");
                Expect(eo != null && eo.activeSelf && er != null && !er.activeSelf && ec != null && !ec.activeSelf, "photo page shows only [撮影カメラ]");
                rtBefore = RtCount(); rtIdsBefore = new HashSet<int>(Resources.FindObjectsOfTypeAll<RenderTexture>().Select(t => t.GetInstanceID())); ourRts.Clear(); objectsBefore = ObjectCount(); opensBefore = Get<int>("_opens"); closesBefore = Get<int>("_closes");
                Note("before: RenderTextures " + rtBefore + ", system objects " + objectsBefore);
            }));

            // ---- V1 ----
            if (On("V1-lifecycle"))
                for (int k = 0; k < Cycles; k++)
                {
                    int cycle = k;
                    string tag = "cycle " + (cycle + 1) + ": ";
                    steps.Add(Do("V1-lifecycle", () => { rendersMark = Renders(); Open(); }));
                    steps.Add(Until("V1-lifecycle", () => Renders() > rendersMark, 3, tag + "no render after _Open"));
                    steps.Add(Do("V1-lifecycle", () => CheckOpen(tag)));
                    if (cycle == 0)
                    {
                        steps.Add(Do("V1-lifecycle", () => { rendersMark = Renders(); enabledMark = Enabled(); callbacksMark = Callbacks(); missedMark = Missed(); enabledSeenMark = enabledBetweenFrames; distinctMark = distinctRenderFrames; twoMark = framesWithTwo; framesMark = Time.frameCount; timeMark = Time.realtimeSinceStartup; }));
                        steps.Add(Until("V1-lifecycle", () => Time.realtimeSinceStartup - timeMark >= 1.0f, 3, "timer"));
                        steps.Add(Do("V1-lifecycle", () =>
                        {
                            float secs = Time.realtimeSinceStartup - timeMark;
                            float rate = (Renders() - rendersMark) / secs, fps = (Time.frameCount - framesMark) / secs;
                            int real = Renders() - rendersMark, enabled = Enabled() - enabledMark;
                            Note("renders " + real + " on " + (distinctRenderFrames - distinctMark) + " distinct frames (" + (framesWithTwo - twoMark) + " extra renders inside an already-rendered frame), ticks " + enabled);
                            int callbacks = Callbacks() - callbacksMark, missed = Missed() - missedMark, seen = enabledBetweenFrames - enabledSeenMark;
                            Expect(missed == 0, "every tick's render reached OnPostRender (missed callbacks " + missed + ")");
                            Expect(callbacks >= enabled - 1 && callbacks <= enabled + 1, "hook callbacks = ticks ±1 (callbacks " + callbacks + ", ticks " + enabled + ")");
                            Expect(seen == 0, "the camera is disabled between frames (seen enabled " + seen + " times outside the player loop)");
                            Expect(rate <= 32f && rate >= Mathf.Min(26f, fps * 0.85f), "real render rate (onPostRender) " + rate.ToString("F1") + "/s at " + fps.ToString("F0") + " fps (≤ 32, ≥ min(26, 0.85 × fps))");
                            Expect(real >= enabled - 1 && real <= enabled + 1, "exactly one real render per tick in the normal camera phase (real " + real + ", ticks " + enabled + ")");
                        }));
                    }
                    steps.Add(Do("V1-lifecycle", () => { Close(); framesMark = Time.frameCount; }));
                    steps.Add(Until("V1-lifecycle", () => Time.frameCount >= framesMark + 2, 2, "frames"));
                    steps.Add(Do("V1-lifecycle", () => { CheckClosed(tag); rendersMark = Renders(); timeMark = Time.realtimeSinceStartup; }));
                    if (cycle == Cycles - 1)
                    {
                        steps.Add(Until("V1-lifecycle", () => Time.realtimeSinceStartup - timeMark >= 0.5f, 2, "timer"));
                        steps.Add(Do("V1-lifecycle", () =>
                        {
                            Expect(Renders() == rendersMark, "no render while closed (0.5 s)");
                            Expect(!cam.enabled, "camera disabled after the last close");
                            Expect(Get<int>("_opens") - opensBefore == Cycles && Get<int>("_closes") - closesBefore == Cycles, Cycles + " opens and closes counted");
                            Expect(ourRts.Count == Cycles && ourRts.All(t => !t), "every one of the " + ourRts.Count + " RTs made by _Open was destroyed");
                            Expect(StrayVideoRts() == 0, "no stray 1280x720 depth RT alive after " + Cycles + " cycles (" + StrayVideoRts() + ")");
                            Note("RenderTextures in memory: before " + rtBefore + ", after " + RtCount() + " (other systems may create theirs meanwhile)");
                            Expect(ObjectCount() == objectsBefore, "system objects " + ObjectCount() + " = before " + objectsBefore);
                        }));
                    }
                }

            // ---- V2 ----
            if (On("V2-pose-dog"))
            {
                steps.Add(Do("V2-pose-dog", () =>
                {
                    var lp = Networking.LocalPlayer;
                    Vector3 fwd = lp.GetRotation() * Vector3.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
                    fakeDir = Quaternion.AngleAxis(90f, Vector3.up) * fwd;
                    fakeDog = new GameObject("KH_VideoHarnessDog");
                    fakeDog.transform.position = Ground(lp.GetPosition() + fwd * 4f - fakeDir * 4f);
                    fakeDog.transform.rotation = Quaternion.LookRotation(fakeDir, Vector3.up);
                    var head = new GameObject("Head").transform; head.SetParent(fakeDog.transform, false); head.localPosition = new Vector3(0f, 0.4f, 0.25f);
                    savedRoots = Get<Transform[]>("dogRoots"); savedHeads = Get<Transform[]>("dogHeads");
                    var r2 = (Transform[])savedRoots.Clone(); var h2 = (Transform[])savedHeads.Clone();
                    r2[0] = fakeDog.transform; h2[0] = head;
                    uVideo.SetProgramVariable("dogRoots", r2); uVideo.SetProgramVariable("dogHeads", h2);
                    uVideo.SetProgramVariable("_dog", 0);
                    Open();
                    uVideo.SetProgramVariable("_dog", 0);   // _Open moves to an own leashed dog; the harness leashes none
                    uVideo.SendCustomEvent("_ModeDog");
                    poseFrames = distBad = frameBad = rollBad = jumpBad = clippedFrames = 0; maxStep = 0f;
                    lastCuts = Cuts(); lastCamPos = cam.transform.position; timeMark = Time.realtimeSinceStartup; rendersMark = Renders();
                }));
                steps.Add(Until("V2-pose-dog", () =>
                {
                    float t = Time.realtimeSinceStartup - timeMark;
                    fakeDog.transform.position = Ground(fakeDog.transform.position + fakeDir * 3f * Time.deltaTime);
                    bool rendered = Renders() != v2Seen; v2Seen = Renders();
                    if (t > 0.6f && rendered)
                    {
                        poseFrames++;
                        Vector3 body = Vector3.Lerp(fakeDog.transform.position, fakeDog.transform.Find("Head").position, 0.75f);
                        float d = (cam.transform.position - body).magnitude;
                        bool clipped = Get<float>("_clipDist") < 1e5f;
                        if (clipped) clippedFrames++;
                        if (d < 0.3f || d > 5.3f || (!clipped && (d < 1.1f || d > 5.1f))) distBad++;
                        Vector3 vp = cam.WorldToViewportPoint(body);
                        if (vp.z <= 0f || vp.x < 0.1f || vp.x > 0.9f || vp.y < 0.1f || vp.y > 0.9f) frameBad++;
                        if (Roll() > 0.01f) rollBad++;
                        float stepLen = (cam.transform.position - lastCamPos).magnitude;
                        int cuts = Cuts();
                        if (cuts == lastCuts) { maxStep = Mathf.Max(maxStep, stepLen); if (stepLen > 0.5f) jumpBad++; }
                        lastCuts = cuts;
                    }
                    lastCamPos = cam.transform.position;
                    return t >= 3.0f;
                }, 6, "the dog walk did not finish"));
                steps.Add(Do("V2-pose-dog", () =>
                {
                    Expect(poseFrames > 20, "pose samples " + poseFrames);
                    Expect(distBad == 0, "camera 0.3-5.3 m from the dog body (1.1-5.1 m unclipped): " + distBad + " bad of " + poseFrames + " (clipped " + clippedFrames + ")");
                    Expect(frameBad == 0, "dog body in the inner 80 % of the frame: " + frameBad + " bad");
                    Expect(rollBad == 0, "roll 0: " + rollBad + " bad");
                    Expect(jumpBad == 0, "no per-frame camera jump > 0.5 m without a cut: " + jumpBad + " (max " + maxStep.ToString("F2") + " m)");
                    cutsMark = Cuts();
                    fakeDog.transform.position = Ground(fakeDog.transform.position + Quaternion.AngleAxis(90f, Vector3.up) * fakeDir * 20f);
                }));
                steps.Add(Until("V2-pose-dog", () => Cuts() > cutsMark, 3, "a 20 m teleport of the dog did not cut"));
                steps.Add(Do("V2-pose-dog", () =>
                {
                    Close();
                    uVideo.SetProgramVariable("dogRoots", savedRoots); uVideo.SetProgramVariable("dogHeads", savedHeads);
                    savedRoots = null; savedHeads = null;
                    UnityEngine.Object.Destroy(fakeDog); fakeDog = null;
                }));
            }

            // ---- V3 ----
            if (On("V3-pose-self"))
            {
                steps.Add(Do("V3-pose-self", () => { Open(); uVideo.SendCustomEvent("_ModeSelf"); rendersMark = Renders(); }));
                steps.Add(Until("V3-pose-self", () => Renders() >= rendersMark + 10, 4, "no 10 renders in じぶん"));
                steps.Add(Do("V3-pose-self", () =>
                {
                    var lp = Networking.LocalPlayer;
                    var td = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                    Vector3 f = td.rotation * Vector3.forward; f.y = 0f;
                    if (f.sqrMagnitude < 0.01f) { f = lp.GetRotation() * Vector3.forward; f.y = 0f; }
                    f.Normalize();
                    Vector3 want = td.position + Quaternion.AngleAxis(KH_VideoCameraEditor.SelfSideDeg, Vector3.up) * f * KH_VideoCameraEditor.SelfDistance + Vector3.up * KH_VideoCameraEditor.SelfLift;
                    bool clipped = Get<float>("_clipDist") < 1e5f;
                    float err = (cam.transform.position - want).magnitude;
                    if (clipped) Note("clipped by terrain: position check relaxed (error " + err.ToString("F2") + " m)");
                    else Expect(err < 0.35f, "camera at head + rot(35°) × facing × 2.4 − 0.1 up (error " + err.ToString("F2") + " m)");
                    Vector3 toCam = cam.transform.position - td.position; toCam.y = 0f;
                    Expect(Vector3.Dot(toCam.normalized, f) > 0.5f, "camera in front of the player");
                    Expect(Get<int>("_selfOwnDogs") == 0, "no own dog in the harness");
                    Vector3 look = Get<Vector3>("_look");
                    Expect((look - (td.position + Vector3.down * KH_VideoCameraEditor.SelfChestDrop)).magnitude < 0.15f, "looks at the chest (no own dog)");
                    Expect(Roll() < 0.01f, "roll 0");
                    cutsMark = Cuts();
                    lp.TeleportTo(lp.GetPosition() + f * 15f, lp.GetRotation());
                }));
                steps.Add(Until("V3-pose-self", () => Cuts() > cutsMark, 4, "a 15 m teleport of the player did not cut"));
                steps.Add(Do("V3-pose-self", () => Close()));
            }

            // ---- V4 (W2 walk modes) / V5 (W3 play modes) ----
            if (On("V4-walk-modes"))
            {
                steps.Add(Do("V4-walk-modes", () => { Open(); uVideo.SetProgramVariable("_dog", 0); }));
                foreach (int mode in new[] { KH_VideoCamera.ModeTps, KH_VideoCamera.ModeDogEye, KH_VideoCamera.ModeSpot, KH_VideoCamera.ModeOrbit, KH_VideoCamera.ModePassBy })
                {
                    int m = mode;
                    steps.Add(Do("V4-walk-modes", () => { uVideo.SendCustomEvent("_Mode" + m); rendersMark = Renders(); }));
                    steps.Add(Until("V4-walk-modes", () => Renders() >= rendersMark + 5, 4, "mode " + m + " " + KH_VideoCamera.ModeWord(m) + ": no 5 real renders in 4 s"));
                    steps.Add(Do("V4-walk-modes", () => CheckMode(m)));
                }
                AddOrbitWaySteps();
                steps.Add(Do("V4-walk-modes", () => Close()));
            }
            if (On("V5-play-modes"))
            {
                // ---- 1. a real throw (the production path: _pendingThrowVelocity + _CheckThrow → _Launch → ReqCatch; ClientSim's one client owns the
                // disc and the dog, so the catch plan is made on this client) with キャッチ待ち selected at the throw ----
                steps.Add(Do("V5-play-modes", () =>
                {
                    V5Resolve();
                    Open(); uVideo.SetProgramVariable("_dog", 0); uVideo.SendCustomEvent("_Mode" + KH_VideoCamera.ModeCatchWait);
                    var root = Get<Transform[]>("dogRoots")[0];
                    Vector3 f = root.forward; f.y = 0f; f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
                    v5Dir = f;
                    Networking.LocalPlayer.TeleportTo(root.position - f * 2f, Quaternion.LookRotation(f));
                    timeMark = Time.realtimeSinceStartup;
                }));
                steps.Add(Until("V5-play-modes", () => Time.realtimeSinceStartup - timeMark > 1.5f && V5DogAtRest(), 8, "dog 0 did not come to rest (Idle / Ready) for the throw"));
                steps.Add(Do("V5-play-modes", () =>
                {
                    var lp = Networking.LocalPlayer;
                    v5Disc.transform.position = lp.GetPosition() + v5Dir * 0.5f + Vector3.up * 1.3f;
                    v5Disc.SetProgramVariable("_pendingThrowVelocity", v5Dir * 7f + Vector3.up * 2.5f);
                    v5Disc.SetProgramVariable("_hasPendingThrow", true);
                    v5Disc.SendCustomEvent("_CheckThrow");
                    Expect(V5Get<bool>(v5Disc, "_flying"), "the disc launched (real _Launch)");
                    v5LandPred = Get<int>("_landPredictions");
                    timeMark = Time.realtimeSinceStartup;
                }));
                steps.Add(Until("V5-play-modes", () => Get<bool>("_catchPlaced") || Get<int>("_landPredictions") > v5LandPred, 3, "キャッチ待ち placed nothing within 3 s of the throw"));
                steps.Add(Do("V5-play-modes", () =>
                {
                    bool plan = Get<bool>("_catchPlaced") && Get<bool>("_catchFromPlan");
                    Note("キャッチ待ち source: " + (plan ? "the dog's catch plan (PlanReadable / PlanCatchT / PlanCatchRoot / PlanP0 / PlanV0 / PlanCurve, KH_Frisbee._PositionAt / _VelocityAt)"
                        : "the landing fallback (FlightLocal " + Get<bool>("_landLocal") + ", _PositionAt via PredictLanding)"));
                    if (!plan) report.unproven.Add("KH_FrisbeeCatch.Plan* getters + _VelocityAt (this throw had no readable catch plan; the fallback ran)");
                    if (plan) report.unproven.Add("the landing fallback (FlightLocal / FlightP0 / FlightV0 / FlightCurveNow) - the plan path ran this time");
                    Expect(Roll() < 0.01f, "キャッチ待ち roll 0");
                    uVideo.SendCustomEvent("_Mode" + KH_VideoCamera.ModeDisc);
                    timeMark = Time.realtimeSinceStartup;
                }));
                // ---- 2. ディスク during the same flight (KH_FrisbeeCatch.State / IsCarrying, the disc's synced position) ----
                steps.Add(Until("V5-play-modes", () => Get<bool>("_discFlying") || Time.realtimeSinceStartup - timeMark > 3f, 4, "timer"));
                steps.Add(Do("V5-play-modes", () =>
                {
                    if (Get<bool>("_discFlying")) Note("ディスク: saw the flight (State / IsCarrying / disc velocity read)");
                    else report.unproven.Add("ディスク in flight (the flight had ended before the mode switch)");
                    Expect(Roll() < 0.01f, "ディスク roll 0");
                    // §15.5: おかえり / ふれあい were removed; §16: 手元 was removed, みち follows
                    Note("not proven by this case: another player's throw (the remote landing estimate), the catch event at a real catch with キャッチ待ち still selected");
                }));
                AddPathSteps();
            }
            if (On("V6-permit")) MakeV6Steps();
            if (On("V7-controls-drone")) MakeV7Steps();
            if (On("V8-patterns")) MakeV8Steps();
        }

        // ---- V5 handles ----
        static UdonBehaviour v5Disc, v5Catcher;
        static Vector3 v5Dir;
        static int v5LandPred;

        static TT V5Get<TT>(UdonBehaviour u, string key)
        {
            if (u == null || !u.TryGetProgramVariable(key, out object v) || !(v is TT)) return default(TT);
            return (TT)v;
        }

        static void V5Resolve()
        {
            var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true);
            v5Catcher = all.FirstOrDefault(u => u.programSource != null && u.programSource.name == "KH_FrisbeeCatch" && V5Get<int>(u, "dogIndex") == 0);
            v5Disc = v5Catcher != null ? V5Get<UdonBehaviour>(v5Catcher, "frisbee") : null;
            if (v5Disc == null && v5Catcher != null && v5Catcher.TryGetProgramVariable("frisbee", out object fo) && fo is Component fc) v5Disc = fc.GetComponent<UdonBehaviour>();
            if (v5Catcher == null || v5Disc == null)
                throw new InvalidOperationException("V5 handles: catcher " + (v5Catcher != null) + ", disc " + (v5Disc != null));
        }

        static bool V5DogAtRest() { int st = V5Get<int>(v5Catcher, "state"); return st == KH_FrisbeeCatch.StateIdle || st == KH_FrisbeeCatch.StateReady; }

        static bool V5Carrying()
        {
            int st = V5Get<int>(v5Catcher, "state");
            return st == KH_FrisbeeCatch.StateJumpCarry || st == KH_FrisbeeCatch.StateLandCarry || st == KH_FrisbeeCatch.StateReturn || st == KH_FrisbeeCatch.StateDrop
                || st == KH_FrisbeeCatch.StateWaterCarry || st == KH_FrisbeeCatch.StateWaterExit;
        }


        // ================================================================ evidence

        static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }

        static void End(string status, string reason)
        {
            if (report == null || ending) return;
            report.status = status; report.reason = reason; ending = true; SafeSave();
            Debug.Log("[KH Video Harness] VIDEO_HARNESS_" + status + " " + reason);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode(); else Finish();
        }

        static void Finish()
        {
            if (report == null) return;
            try
            {
                Camera.onPostRender -= OnPostRender;
                Application.runInBackground = report.originalBackground;
                report.hashAfter = Hash(report.scene); report.sceneFileUnchanged = report.hashBefore == report.hashAfter;
                report.endedUtc = DateTime.UtcNow.ToString("o");
                if (report.status == "PASS" && !report.sceneFileUnchanged) { report.status = "FAIL"; report.reason += " | the scene file changed"; }
            }
            catch (Exception e) { report.status = "EVIDENCE_ERROR"; report.reason = "final evidence failed: " + e.Message; }
            finally
            {
                SafeSave();
                Debug.Log("[KH Video Harness] VIDEO_HARNESS_" + report.status + " finished (" + Dir + "/latest.json) sceneUnchanged " + report.sceneFileUnchanged);
                SessionState.SetBool(Key, false); report = null; ending = false; steps = null; step = -1;
            }
        }

        static void SafeSave() { try { Save(); } catch (Exception e) { Debug.LogError("[KH Video Harness] evidence write failed: " + e.Message); } }
        static void Save()
        {
            Directory.CreateDirectory(Dir + "/" + report.runId);
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Dir + "/" + report.runId + "/report.json", json); File.WriteAllText(Journal, json); File.WriteAllText(Dir + "/latest.json", json);
        }
    }
}
