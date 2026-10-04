using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-WLD-008 W1 (MVP): the follow video camera. docs/plans/2026-10-03-video-camera.md §2-§4, §10 W1 and "W1 実装メモ".
    ///
    /// Order (the coordinator runs them in series; subagents never do):
    ///   Video/0 - Create Program Asset   KH_VideoCamera.asset → compile U# (unity-compile.sh --udon)
    ///   Video/1 - Build Video Camera     KH_VideoSystem (camera, fixed monitor + panel, RecordSnap), the mesh / materials, the entry buttons on the
    ///                                    photo page (needs Photo/1), the tuning table (code defaults never reach the scene: this menu writes them)
    ///   Video/2 - Verify                 read-only
    ///   Video/3 - Harness                ClientSim V0-V3 (Testing/KH_VideoHarnessEditor.cs)
    ///   Video/9 - Remove Video Camera    the system and the entry buttons out (assets kept)
    ///
    /// Same scene for every build target: nothing is stripped. KH_VideoCamera.Start hides everything on Android / iOS (#if) and the entry
    /// buttons are saved hidden (a PC client's Start shows [撮影カメラ]). No other object references the system, so no target has a missing reference.
    /// Texts: Japanese constants in W1 (KH_VideoCamera.Word*). W5 moves them to UITranslations.json (13 languages); no half-translated keys now.
    /// </summary>
    public static partial class KH_VideoCameraEditor
    {
        const string Menu = "Tools/KominkaHillside/Video/";
        public const string OutDir = "Temp/kh-video";
        public const string SystemName = "KH_VideoSystem";
        public const string CameraName = "VideoCamera", MonitorName = "Monitor", PanelName = "Panel", ScreenName = "Screen", SnapName = "RecordSnap";
        public const string EntryOpenName = "VideoOpen", EntryRecallName = "VideoRecall", EntryCloseName = "VideoClose";
        public const string ProgramPath = "Assets/KominkaHillside/Scripts/KH_VideoCamera";
        /// <summary>W1 手直し 3: the OnPostRender forwarder on the camera's GameObject.</summary>
        public const string HookProgramPath = "Assets/KominkaHillside/Scripts/KH_VideoCameraHook";
        /// <summary>§15.5: the camera position marker sync (a child of the mailbox PlayerObject; Video/6).</summary>
        public const string DroneProgramPath = "Assets/KominkaHillside/Scripts/KH_FilmDrone";
        public static readonly string[] Programs = { ProgramPath, HookProgramPath, DroneProgramPath };
        public const string BackName = "Back";
        const string BackMatPath = "Assets/KominkaHillside/Materials/Video/M_KH_VideoMonitorBack.mat", BackShaderName = "Unlit/Color";
        /// <summary>The back plate: a little larger than the canvas, just behind it (+Z = away from the viewer), facing away from the viewer.</summary>
        public static readonly Vector2 BackM = new Vector2(0.86f, 0.90f);
        public const float BackBehindM = 0.004f;
        const string MatDir = "Assets/KominkaHillside/Materials/Video", MeshDir = "Assets/KominkaHillside/Data/Video";
        public const string ScreenMatPath = MatDir + "/M_KH_VideoScreen.mat", SnapMatPath = MatDir + "/M_KH_VideoRecordSnap.mat";
        public const string SnapMeshPath = MeshDir + "/KH_VideoRecordSnapQuad.asset";
        const string PrintShaderName = "KominkaDogRun/KD_PhotoPrint", ScreenShaderName = "Unlit/Texture";

        // ================================================================ tuning table (trial values; Video/1 writes them, Video/2 compares)
        public const int RenderWidth = 1280, RenderHeight = 720, RenderDepth = 24;
        public const float RenderFps = 30f;
        public const float CamNear = 0.03f, CamFar = 300f, CamFov = 50f;
        public const float Clearance = 0.18f, GroundLift = 0.25f, OcclusionInterval = 0.1f, PushOutScale = 0.45f, CutDistance = 6f, LostHoldSeconds = 3f;
        public const float PosTau = 0.6f, TurnPosTau = 1.0f, LookTau = 0.25f, HeadingTau = 0.8f, FovTau = 0.5f, StillSpeed = 0.3f, TurnRateDeg = 90f;
        public const float DogFov = 50f, DogSideDeg = 40f, DogFill = 0.4f, DogDistMin = 1.2f, DogDistMax = 5f, DogLift = 0.35f, DogLookAhead = 0.5f;
        // W2: selfLookToDogs 0.4 → 0.55 and a longer distance with an own dog (the W1 probe: a dog at the feet fell below the frame; the node
        // formula of 2026-10-04 gives head / dog ≤ 0.79 of the half-frame at 3.2 m / 0.55 for dogs at the feet, beside and ahead)
        public const float SelfFov = 55f, SelfSideDeg = 35f, SelfDistance = 2.4f, SelfDistanceWithDog = 3.2f, SelfLift = -0.1f, SelfLookToDogs = 0.55f, SelfChestDrop = 0.35f, SelfDogMax = 8f;
        // W2 / W3 modes (design §4; trial values)
        public const float DogEyeFov = 60f, DogEyeSideDeg = 50f, DogEyeBack = 0.35f, DogEyeLift = 0.05f, DogEyeUserMax = 6f, DogEyeTau = 0.3f;
        public const float DogEyeSceneFov = 70f, DogEyeAhead = 0.15f, DogEyeSceneLift = 0.04f, DogEyePitch = 3f;
        public const float DroneFov = 60f, DroneBack = 6f, DroneHeight = 7f, DroneAhead = 2f, DroneTau = 1.2f, DroneGroundMin = 4f;
        public const float SpotLookTau = 0.35f, SpotDogPull = 0.3f;
        public const float DiscFov = 60f, DiscSideK = 1f, DiscSideMin = 3f, DiscSideMax = 12f, DiscUp = 0.8f, DiscTau = 0.3f, DiscLookTau = 0.1f, DiscFlySpeed = 2f, DiscApartMax = 15f, DiscVelTau = 0.3f;
        // キャッチ待ち (2026-10-04 user: the catch point; the probe: disc + jumping dog + catch point inside the inner 80 %)
        public const float CatchFov = 55f, CatchSide = 6f, CatchUp = 0.3f, CatchLookBack = 2f, CatchPlanWait = 0.6f, CatchReplanMove = 1f;
        public const float LandFov = 55f, LandSide = 5f, LandBeyond = 3f, LandUp = 1.2f, LandNear = 8f, LandMoveMax = 5f, LandStep = 0.12f, LandMaxT = 8f, LandRemoteSample = 0.25f, LandRepredict = 0.5f;
        public const float WashFov = 45f, WashDist = 0.6f, WashUp = 0.25f, WashLookTau = 0.4f;
        public const float OrbitFov = 50f, OrbitRadius = 3.5f, OrbitHeight = 1.4f, OrbitDegPerSec = 6f, OrbitCenterTau = 2f, OrbitLookUp = 0.6f, OrbitTau = 0.3f;
        public const float PassFov = 50f, PassLookUp = 0.5f, PassNear = 25f, PassFar = 120f, PassRecede = 15f, PassAfter = 8f, PassGiveUp = 60f, PassTooFar = 150f;
        // spot / pass-point data (Video/1 resolves them from the scene; Video/2 checks ground and line of sight)
        public const float SpotWashDist = 2.6f, SpotWashHeight = 1.5f, SpotWashFov = 55f, SpotRiverDist = 6f, SpotRiverHeight = 1.8f, SpotRiverLowDist = 4f, SpotRiverLowHeight = 0.6f, SpotRiverFov = 55f;
        public const float PassSide = 3f, PassSideFar = 4.5f, PassHeight = 1.4f, SpotGroundMax = 4f;
        public const float MonitorDistance = 1.3f, MonitorDrop = 0.25f;
        // §15.5 the user's camera controls (trial values; Video/1 writes them): depth × 0.6..2.5 (× 1.25 a press), height −0.6..+3.0 m (0.3 m),
        // around the subject 20° a press, tilt −30..+30° (5°, also limited to fov / 2 − 3° so the subject stays in the frame), never closer than CtlMinDist
        public const float CtlDistMin = 0.6f, CtlDistMax = 2.5f, CtlDistStep = 1.25f, CtlUpMin = -0.6f, CtlUpMax = 3f, CtlUpStep = 0.3f, CtlYawStep = 20f;
        public const float CtlPitchMin = -30f, CtlPitchMax = 30f, CtlPitchStep = 5f, CtlMinDist = 0.4f, CtlPitchTau = 0.25f;
        // §15.5 the camera drone (the marker): 5 Hz while filming another person, hidden near my head / near the subject's head
        public const float DroneSendHz = 5f, DroneHideNearMe = 0.3f;
        public const bool SnapOnOpen = true;

        // ================================================================ layers and the RecordSnap (design §3.2-3.3, revised after the Codex review)
        /// <summary>**RecordSnap's layer: 4 Water.** It must be a layer VRChat's cameras draw and our own cameras (this video camera and the auto
        /// photo camera) do not, so that "our cameras never draw it" does not depend on _VRChatCameraMode being 0 in our pass (Codex review 2026-10-03).
        /// Water: the scene's reference camera (Main Camera, VRC_SceneDescriptor.ReferenceCamera) renders Everything, Water is a normal rendering
        /// layer, nothing in this scene uses it (0 objects, grep 2026-10-03), and both our cameras already exclude it (KH_PhotoGalleryEditor.MustExcludeLayers).
        /// Default (0) is proven to reach the VRChat camera (the photo board snaps) but our cameras draw it. 17 Walkthrough is unproven (IMP-WLD-007 W3 test).
        /// **If the 5-minute test shows no full screen, set this to 0 (one constant), Video/1, and re-check that the video shows no nested video.**
        /// Video/2 checks the pair: this layer is not in the video camera's or the photo camera's culling mask (unless it is 0, which is then reported).</summary>
        public const int RecordSnapLayer = 4;
        /// <summary>The monitor (screen + panel): 17 Walkthrough, the hand menu's layer (takes the pointer; excluded from the video and the photo camera).</summary>
        public const int MonitorLayer = 17;
        public const float SnapAttachDistance = 100000f, SnapAttachMargin = -1f;
        /// <summary>Above the photo viewer (Overlay 4000/4001, ZTest Always), the photo frames (4100/4101), the night overlay (4999, would darken the
        /// video twice) and the travel fade (5000). RecordSnap writes depth at the near plane, so it wins over everything else in the VRChat camera pass.
        /// While RecordSnap is ON the board / menu snaps (2000) cannot show: turn it OFF to take ordinary photos (the panel says so).</summary>
        public const int SnapRenderQueue = 5001;
        /// <summary>The mesh's bounds (one side, m): the world sits within ±600 m of the origin, so a 4000 m box always meets any camera's frustum.</summary>
        public const float SnapBoundsSize = 4000f;

        // ================================================================ monitor layout (canvas px, 1 px = 1 mm)
        public const float Px = 0.001f;
        public static readonly Vector2 CanvasPx = new Vector2(840f, 880f);
        public static readonly Vector2 ScreenM = new Vector2(0.80f, 0.45f);
        // §15.5: 10 mode buttons (2 rows × 5, 156 × 56 px), the picker row, the control row (9 × 84 px), the snap + drone row, note, state
        public const float ScreenCenterPxY = 157f, ScreenFrontM = 0.003f;
        public static readonly Vector2 TitlePos = new Vector2(-180f, 412f), TitleSize = new Vector2(440f, 44f);
        public static readonly Vector2 ClosePos = new Vector2(380f, 412f), CloseSize = new Vector2(64f, 50f);
        public static readonly Vector2 ModeSize = new Vector2(156f, 56f);
        public const float ModePitchX = 164f, ModeRow0Y = -100f, ModeRow1Y = -160f;
        public const int ModeColumns = 5;
        public static Vector2 ModePos(int m) { int col = m % ModeColumns, row = m / ModeColumns; return new Vector2((col - (ModeColumns - 1) * 0.5f) * ModePitchX, row == 0 ? ModeRow0Y : ModeRow1Y); }
        public static readonly Vector2 PickerPos = new Vector2(0f, -222f), PickerSize = new Vector2(420f, 56f), PickerButtonSize = new Vector2(80f, 56f), PickerLabelSize = new Vector2(250f, 56f);
        public const float PickerButtonX = 170f;
        public static readonly Vector2 SnapPos = new Vector2(-205f, -336f), SnapSize = new Vector2(400f, 48f);
        public static readonly Vector2 DroneTogglePos = new Vector2(205f, -336f), DroneToggleSize = new Vector2(400f, 48f);
        public static readonly Vector2 NotePos = new Vector2(0f, -381f), NoteSize = new Vector2(800f, 30f);
        public static readonly Vector2 StatePos = new Vector2(0f, -418f), StateSize = new Vector2(800f, 32f);
        // the control row: [近く][遠く][↑][↓][←][→][向き▲][向き▼][戻す], 84 × 52 px, pitch 92 (x −368..368, inside ±420)
        public const float CtlRowY = -280f, CtlPitchX = 92f;
        public static readonly Vector2 CtlSize = new Vector2(84f, 52f);
        public static Vector2 CtlPos(int i) { return new Vector2((i - 4) * CtlPitchX, CtlRowY); }
        public static string CtlName(int i) { return "Ctl" + i; }
        public static readonly string[] CtlEvents = { "_CtlNear", "_CtlFar", "_CtlUp", "_CtlDown", "_CtlLeft", "_CtlRight", "_CtlTiltUp", "_CtlTiltDown", "_CtlReset" };
        public const string DroneToggleName = "DroneToggle";
        // entry buttons on the photo page (Page_Settings/PhotoPage, 1140 x 700): bottom-left, clear of [設定へ] (0,-205) 280 wide and the pager (360,-205)
        public static readonly Vector2 EntryOpenPos = new Vector2(-400f, -205f), EntryOpenSize = new Vector2(260f, 50f);
        public static readonly Vector2 EntryRecallPos = new Vector2(-450f, -205f), EntryRecallSize = new Vector2(170f, 50f);
        public static readonly Vector2 EntryClosePos = new Vector2(-275f, -205f), EntryCloseSize = new Vector2(150f, 50f);
        static readonly Color Ink = new Color(0.97f, 0.95f, 0.90f);
        static readonly Color FrameColor = new Color(0.16f, 0.12f, 0.09f, 1f);

        public static string[] DogNameDefaultKeys = { "activities.dog.Corgi", "activities.dog.Pome", "activities.dog.Lab", "activities.dog.Dachs", "activities.dog.Shiba", "activities.dog.Samoyed" };

        /// <summary>Every runtime / builder string of the panel (glyphs are baked from these).</summary>
        public static string AllWords()
        {
            return string.Concat(KH_VideoCamera.WordTitle, KH_VideoCamera.WordRecall, KH_VideoCamera.WordPutAway, KH_VideoCamera.WordModeDog, KH_VideoCamera.WordModeSelf,
                KH_VideoCamera.WordSnap, KH_VideoCamera.WordOn, KH_VideoCamera.WordOff, KH_VideoCamera.WordFilming, KH_VideoCamera.WordLost, KH_VideoCamera.WordFade,
                KH_VideoCamera.WordRtFailed, KH_VideoCamera.WordSnapNote, KH_VideoCamera.WordPhotoCamOn, KH_VideoCamera.WordPhotoCamOff, "×◀▶: #0123456789",
                string.Concat(Enumerable.Range(0, KH_VideoCamera.ModeCount).Select(KH_VideoCamera.ModeWord)), KH_VideoCamera.WordNoWash, KH_VideoCamera.WordNoSpot,
                SpotWordWash, SpotWordRiver, SpotWordRiverLow, "()", string.Concat(KH_VideoCamera.CtlWords()), KH_VideoCamera.WordDrone, KH_VideoCamera.WordShow, KH_VideoCamera.WordHide);
        }

        // ================================================================ context

        class Ctx
        {
            public UnityEngine.SceneManagement.Scene scene;
            public KD_TreatPouch pouch; public KD_Language language; public TMP_FontAsset font; public GameObject photoPage;
            public KH_LeashManager manager; public KH_TravelFade fade; public Camera photoCamera;
            public Transform[] dogs = new Transform[6]; public Transform[] heads = new Transform[6]; public string[] nameKeys;
            public List<string> notes = new List<string>();
        }

        static Ctx Resolve()
        {
            var c = new Ctx { scene = EditorSceneManager.GetActiveScene() };
            var pouches = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true);
            if (pouches.Length != 1) throw new InvalidOperationException("exactly one KD_TreatPouch required (" + pouches.Length + "); nothing changed");
            c.pouch = pouches[0];
            c.language = c.pouch.language;
            if (c.language == null) throw new InvalidOperationException("KD_TreatPouch.language not wired; nothing changed");
            c.font = c.pouch.sectionTitleLabel != null ? c.pouch.sectionTitleLabel.font : null;
            if (c.font == null) throw new InvalidOperationException("the menu's shared font (sectionTitleLabel.font) is missing; nothing changed");
            c.photoPage = c.pouch.photoPage;
            if (c.photoPage == null) throw new InvalidOperationException("the photo page (KD_TreatPouch.photoPage) is missing: run Photo/1 first; nothing changed");
            c.manager = c.pouch.manager;
            if (c.manager == null) c.notes.Add("WARN KD_TreatPouch.manager missing: じぶん cannot frame own dogs, dog mode cannot start on an own dog");
            var fades = UnityEngine.Object.FindObjectsOfType<KH_TravelFade>(true);
            c.fade = fades.Length == 1 ? fades[0] : null;
            if (c.fade == null) c.notes.Add("WARN KH_TravelFade x" + fades.Length + ": the camera does not pause during the travel fade");
            var capture = UnityEngine.Object.FindObjectsOfType<KD_PhotoCapture>(true);
            c.photoCamera = capture.Length == 1 ? capture[0].photoCamera : null;
            var keys = KH_PhotoGalleryEditor.Keys;
            for (int i = 0; i < keys.Length; i++)
            {
                var go = KD_SceneObjects.Find("KD_Dog_" + keys[i], out int n);
                if (go == null || n != 1) throw new InvalidOperationException("KD_Dog_" + keys[i] + " x" + n + " (exactly 1 required); nothing changed");
                c.dogs[i] = go.transform;
                var pet = KD_SceneObjects.Find("KD_PetSystem_" + keys[i], out int pn);
                var pc = pet != null && pn == 1 ? pet.GetComponent<KD_DogPetController>() : null;
                c.heads[i] = pc != null && pc.headAnchor != null ? pc.headAnchor : FindChild(c.dogs[i], "KD_PetAnchor_Head");
                if (c.heads[i] == null) c.notes.Add("WARN no head anchor for " + keys[i] + ": framing falls back to the root (+0.3 m)");
            }
            var toy = c.pouch.toyNameKeys;
            c.nameKeys = toy != null && toy.Length == 6 && toy.All(k => !string.IsNullOrEmpty(k)) ? (string[])toy.Clone() : (string[])DogNameDefaultKeys.Clone();
            return c;
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        static GameObject SceneRoot(UnityEngine.SceneManagement.Scene scene, string name, out int count)
        {
            var all = scene.GetRootGameObjects().Where(g => g.name == name).ToArray();
            count = all.Length;
            return all.Length == 1 ? all[0] : null;
        }

        // ================================================================ Video/0

        [MenuItem(Menu + "0 - Create Program Asset")]
        public static void CreateProgramAsset()
        {
            var created = new List<string>(); var existing = new List<string>();
            foreach (var path in Programs)
            {
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path + ".asset") != null) { existing.Add(path + ".asset"); continue; }
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path + ".cs");
                if (script == null) { Debug.LogError("[KH Video] VIDEO_PROGRAM_FAIL " + path + ".cs not imported yet (refresh first)"); return; }
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = script;
                AssetDatabase.CreateAsset(asset, path + ".asset");
                created.Add(path + ".asset");
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[KH Video] VIDEO_PROGRAM_OK created [" + string.Join(", ", created) + "], already [" + string.Join(", ", existing) + "]." + (created.Count > 0 ? " Compile U# (unity-compile.sh --udon) before Video/1." : ""));
        }

        static string ProgramMissing()
        {
            foreach (var path in Programs) if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path + ".asset") == null) return path + ".asset";
            return null;
        }

        // ================================================================ Video/1

        [Serializable] class BuildReport { public string utc, scene, status, backup, menu; public List<string> log = new List<string>(); }

        [MenuItem(Menu + "1 - Build Video Camera")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            string missingProgram = ProgramMissing();
            if (missingProgram != null)
            { Debug.LogError("[KH Video] VIDEO_BUILD_FAIL " + missingProgram + " missing: run Video/0, compile U#, then Video/1; nothing changed"); return; }
            var r = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, menu = "Video/1" };
            var c = Resolve();
            r.log.AddRange(c.notes);
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-video-build.unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, true);
            try
            {
                var mesh = EnsureSnapMesh(r.log);
                var screenMat = EnsureMat(ScreenMatPath, ScreenShaderName, r.log);
                screenMat.mainTexture = null; EditorUtility.SetDirty(screenMat);
                var snapMat = EnsureSnapMat(r.log);
                var root = SceneRoot(c.scene, SystemName, out int n);
                if (n > 1) throw new InvalidOperationException(n + " " + SystemName + " at the scene root (keep one)");
                KD_SceneObjects.Find(SystemName, out int anywhere);
                if (anywhere != n) throw new InvalidOperationException(SystemName + " exists below another object");
                if (root == null) { root = new GameObject(SystemName); EditorSceneManager.MoveGameObjectToScene(root, c.scene); r.log.Add("created " + SystemName); }
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); root.transform.localScale = Vector3.one;
                root.layer = 0; root.SetActive(true);
                var video = EnsureUsc<KH_VideoCamera>(root, r.log);
                var videoUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(video);
                video.enabled = true;   // Start must run (it hides everything and disables the behaviour itself)

                var cam = BuildCamera(root.transform, r.log);
                var hook = EnsureUsc<KH_VideoCameraHook>(cam.gameObject, r.log);
                hook.enabled = true; hook.rig = video;
                UdonSharpEditorUtility.CopyProxyToUdon(hook); EditorUtility.SetDirty(hook);
                r.log.Add("hook: KH_VideoCameraHook on " + cam.name + " (OnPostRender → _OnCamRendered), sync None");
                var snap = BuildSnap(root.transform, mesh, snapMat, r.log);
                var ui = BuildMonitor(root.transform, screenMat, c, videoUdon, r.log);
                var entry = BuildEntry(c, videoUdon, r.log);

                video.cam = cam; video.monitorRoot = ui.monitor; video.screenRenderer = ui.screen; video.snapRenderer = snap;
                video.entryOpen = entry[0]; video.entryRecall = entry[1]; video.entryClose = entry[2];
                video.stateLabel = ui.state; video.snapLabel = ui.snapLabel; video.dogLabel = ui.dogLabel;
                video.modeImages = ui.modeImages; video.snapImage = ui.snapImage; video.dogPicker = ui.picker;
                video.ctlImages = ui.ctlImages; video.droneImage = ui.droneImage; video.droneLabel = ui.droneLabel;
                video.language = c.language; video.manager = c.manager; video.fade = c.fade;
                video.dogRoots = (Transform[])c.dogs.Clone(); video.dogHeads = (Transform[])c.heads.Clone(); video.dogNameKeys = (string[])c.nameKeys.Clone();
                WirePlay(video, c, r.log);
                var data = BuildData(c, r.log);
                video.spotNames = data.spotNames; video.spotPositions = data.spotPositions; video.spotLooks = data.spotLooks; video.spotFovs = data.spotFovs; video.spotStations = data.spotStations;
                video.passPositions = data.passPositions;
                WriteTuning(video);
                UdonSharpEditorUtility.CopyProxyToUdon(video); EditorUtility.SetDirty(video);
                r.log.Add("wired: dogs " + string.Join(",", c.dogs.Select(d => d.name)) + ", name keys " + string.Join(",", c.nameKeys) + ", fade " + (c.fade != null) + ", manager " + (c.manager != null));

                var sb = new StringBuilder();
                string missing = KD_ShopSetupEditor.MissingGlyphs(AllWords());
                if (missing.Length > 0) { KD_ShopSetupEditor.AddGlyphs(missing, sb); r.log.Add("glyphs added: " + missing + " " + sb.ToString().Trim()); }
                EditorSceneManager.MarkSceneDirty(c.scene);
                if (!EditorSceneManager.SaveScene(c.scene)) throw new IOException("scene save failed");
                AssetDatabase.SaveAssets();
                r.status = "BUILT";
            }
            catch (Exception e)
            {
                r.status = "FAILED"; r.log.Add("FAIL " + e.Message);
                WriteJson(OutDir + "/build.json", r);
                Debug.LogError("[KH Video] VIDEO_BUILD_FAIL " + e.Message + ". The scene before this run is at " + r.backup + "; reopen the scene without saving to discard partial changes.");
                throw;
            }
            WriteJson(OutDir + "/build.json", r);
            Debug.Log("[KH Video] VIDEO_BUILD_OK video camera built (" + r.log.Count + " log lines in " + OutDir + "/build.json). Next: Video/2, then Video/3 (harness, after the pre-run consult).");
        }

        static T EnsureUsc<T>(GameObject go, List<string> log) where T : UdonSharpBehaviour
        {
            var comp = go.GetComponent<T>();
            if (comp == null) { comp = go.AddUdonSharpComponent<T>(); log.Add("added " + typeof(T).Name + " on " + go.name); }
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(comp);
            if (backing == null) throw new InvalidOperationException(typeof(T).Name + " backing missing (compile U# and run again)");
            if (backing.SyncMethod != Networking.SyncType.None) { backing.SyncMethod = Networking.SyncType.None; EditorUtility.SetDirty(backing); log.Add(typeof(T).Name + " sync -> None"); }
            return comp;
        }

        public static void WriteTuning(KH_VideoCamera v)
        {
            v.renderWidth = RenderWidth; v.renderHeight = RenderHeight; v.renderDepth = RenderDepth; v.renderFps = RenderFps;
            v.blockMask = KH_PhotoGalleryEditor.CameraBlockMask;
            v.clearance = Clearance; v.groundLift = GroundLift; v.occlusionInterval = OcclusionInterval; v.pushOutScale = PushOutScale;
            v.cutDistance = CutDistance; v.lostHoldSeconds = LostHoldSeconds;
            v.posTau = PosTau; v.turnPosTau = TurnPosTau; v.lookTau = LookTau; v.headingTau = HeadingTau; v.fovTau = FovTau; v.stillSpeed = StillSpeed; v.turnRateDeg = TurnRateDeg;
            v.dogFov = DogFov; v.dogSideDeg = DogSideDeg; v.dogFill = DogFill; v.dogDistMin = DogDistMin; v.dogDistMax = DogDistMax; v.dogLift = DogLift; v.dogLookAhead = DogLookAhead;
            v.selfFov = SelfFov; v.selfSideDeg = SelfSideDeg; v.selfDistance = SelfDistance; v.selfLift = SelfLift; v.selfLookToDogs = SelfLookToDogs; v.selfChestDrop = SelfChestDrop; v.selfDogMax = SelfDogMax;
            v.monitorDistance = MonitorDistance; v.monitorDrop = MonitorDrop; v.snapOnOpen = SnapOnOpen;
            foreach (var kv in ModeTuning()) typeof(KH_VideoCamera).GetField(kv.Key).SetValue(v, kv.Value);
        }

        /// <summary>The W2 / W3 tuning rows (field name → value). WriteTuning writes them, TuningMatches and Verify's backing check compare them.</summary>
        public static List<KeyValuePair<string, float>> ModeTuning()
        {
            var t = new List<KeyValuePair<string, float>>();
            void A(string n, float v) { t.Add(new KeyValuePair<string, float>(n, v)); }
            A("selfDistanceWithDog", SelfDistanceWithDog);
            A("dogEyeFov", DogEyeFov); A("dogEyeSideDeg", DogEyeSideDeg); A("dogEyeBack", DogEyeBack); A("dogEyeLift", DogEyeLift); A("dogEyeUserMax", DogEyeUserMax); A("dogEyeTau", DogEyeTau);
            A("dogEyeSceneFov", DogEyeSceneFov); A("dogEyeAhead", DogEyeAhead); A("dogEyeSceneLift", DogEyeSceneLift); A("dogEyePitch", DogEyePitch);
            A("droneFov", DroneFov); A("droneBack", DroneBack); A("droneHeight", DroneHeight); A("droneAhead", DroneAhead); A("droneTau", DroneTau); A("droneGroundMin", DroneGroundMin);
            A("spotLookTau", SpotLookTau); A("spotDogPull", SpotDogPull);
            A("discFov", DiscFov); A("discSideK", DiscSideK); A("discSideMin", DiscSideMin); A("discSideMax", DiscSideMax); A("discUp", DiscUp); A("discTau", DiscTau); A("discLookTau", DiscLookTau); A("discFlySpeed", DiscFlySpeed); A("discApartMax", DiscApartMax); A("discVelTau", DiscVelTau);
            A("catchFov", CatchFov); A("catchSide", CatchSide); A("catchUp", CatchUp); A("catchLookBack", CatchLookBack); A("catchPlanWait", CatchPlanWait); A("catchReplanMove", CatchReplanMove);
            A("landFov", LandFov); A("landSide", LandSide); A("landBeyond", LandBeyond); A("landUp", LandUp); A("landNear", LandNear); A("landMoveMax", LandMoveMax); A("landStep", LandStep); A("landMaxT", LandMaxT);
            A("landRemoteSample", LandRemoteSample); A("landRepredict", LandRepredict);
            A("washFov", WashFov); A("washDist", WashDist); A("washUp", WashUp); A("washLookTau", WashLookTau);
            A("orbitFov", OrbitFov); A("orbitRadius", OrbitRadius); A("orbitHeight", OrbitHeight); A("orbitDegPerSec", OrbitDegPerSec); A("orbitCenterTau", OrbitCenterTau); A("orbitLookUp", OrbitLookUp); A("orbitTau", OrbitTau);
            A("ctlDistMin", CtlDistMin); A("ctlDistMax", CtlDistMax); A("ctlDistStep", CtlDistStep); A("ctlUpMin", CtlUpMin); A("ctlUpMax", CtlUpMax); A("ctlUpStep", CtlUpStep); A("ctlYawStep", CtlYawStep);
            A("ctlPitchMin", CtlPitchMin); A("ctlPitchMax", CtlPitchMax); A("ctlPitchStep", CtlPitchStep); A("ctlMinDist", CtlMinDist); A("ctlPitchTau", CtlPitchTau);
            A("droneSendHz", DroneSendHz); A("droneHideNearMe", DroneHideNearMe);
            A("passFov", PassFov); A("passLookUp", PassLookUp); A("passNear", PassNear); A("passFar", PassFar); A("passRecede", PassRecede); A("passAfter", PassAfter); A("passGiveUp", PassGiveUp); A("passTooFar", PassTooFar);
            return t;
        }

        /// <summary>The tuning rows (name, written value, value in the behaviour) for Verify.</summary>
        public static List<KeyValuePair<string, bool>> TuningMatches(KH_VideoCamera v)
        {
            var l = new List<KeyValuePair<string, bool>>();
            void F(string n, float want, float have) { l.Add(new KeyValuePair<string, bool>(n + " " + have + " (want " + want + ")", Mathf.Abs(want - have) < 1e-4f)); }
            void I(string n, int want, int have) { l.Add(new KeyValuePair<string, bool>(n + " " + have + " (want " + want + ")", want == have)); }
            I("renderWidth", RenderWidth, v.renderWidth); I("renderHeight", RenderHeight, v.renderHeight); I("renderDepth", RenderDepth, v.renderDepth); F("renderFps", RenderFps, v.renderFps);
            I("blockMask", KH_PhotoGalleryEditor.CameraBlockMask, v.blockMask);
            F("clearance", Clearance, v.clearance); F("groundLift", GroundLift, v.groundLift); F("occlusionInterval", OcclusionInterval, v.occlusionInterval); F("pushOutScale", PushOutScale, v.pushOutScale);
            F("cutDistance", CutDistance, v.cutDistance); F("lostHoldSeconds", LostHoldSeconds, v.lostHoldSeconds);
            F("posTau", PosTau, v.posTau); F("turnPosTau", TurnPosTau, v.turnPosTau); F("lookTau", LookTau, v.lookTau); F("headingTau", HeadingTau, v.headingTau); F("fovTau", FovTau, v.fovTau);
            F("stillSpeed", StillSpeed, v.stillSpeed); F("turnRateDeg", TurnRateDeg, v.turnRateDeg);
            F("dogFov", DogFov, v.dogFov); F("dogSideDeg", DogSideDeg, v.dogSideDeg); F("dogFill", DogFill, v.dogFill); F("dogDistMin", DogDistMin, v.dogDistMin); F("dogDistMax", DogDistMax, v.dogDistMax);
            F("dogLift", DogLift, v.dogLift); F("dogLookAhead", DogLookAhead, v.dogLookAhead);
            F("selfFov", SelfFov, v.selfFov); F("selfSideDeg", SelfSideDeg, v.selfSideDeg); F("selfDistance", SelfDistance, v.selfDistance); F("selfLift", SelfLift, v.selfLift);
            F("selfLookToDogs", SelfLookToDogs, v.selfLookToDogs); F("selfChestDrop", SelfChestDrop, v.selfChestDrop); F("selfDogMax", SelfDogMax, v.selfDogMax);
            F("monitorDistance", MonitorDistance, v.monitorDistance); F("monitorDrop", MonitorDrop, v.monitorDrop);
            l.Add(new KeyValuePair<string, bool>("snapOnOpen " + v.snapOnOpen, v.snapOnOpen == SnapOnOpen));
            foreach (var kv in ModeTuning()) F(kv.Key, kv.Value, (float)typeof(KH_VideoCamera).GetField(kv.Key).GetValue(v));
            return l;
        }

        // ---------------------------------------------------------------- assets

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'), leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static Material EnsureMat(string path, string shaderName, List<string> log)
        {
            var shader = Shader.Find(shaderName); if (shader == null) throw new InvalidOperationException("shader " + shaderName + " not found");
            EnsureFolder(MatDir);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); log.Add("created " + path); }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }

        static Material EnsureSnapMat(List<string> log)
        {
            var m = EnsureMat(SnapMatPath, PrintShaderName, log);
            m.mainTexture = null;
            m.SetFloat("_AttachDistance", SnapAttachDistance); m.SetFloat("_AttachMargin", SnapAttachMargin);
            m.SetFloat("_HideFromEyes", 1f); m.SetFloat("_DebugProbe", 0f);
            m.renderQueue = SnapRenderQueue;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A ±0.5 quad (the KD_PhotoPrint contract: vertices ±0.5, the fullscreen path maps them to NDC) whose bounds are a 4000 m box,
        /// so no camera culls it. Updated in place (GUID kept). Unity keeps the saved bounds (nothing recalculates them at runtime).</summary>
        static Mesh EnsureSnapMesh(List<string> log)
        {
            EnsureFolder(MeshDir);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(SnapMeshPath);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = "KH_VideoRecordSnapQuad" };
            mesh.Clear();
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 3, 1, 3, 0, 2 };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * SnapBoundsSize);
            if (created) { AssetDatabase.CreateAsset(mesh, SnapMeshPath); log.Add("created " + SnapMeshPath); }
            EditorUtility.SetDirty(mesh);
            log.Add("RecordSnap mesh: 4 vertices ±0.5, bounds " + SnapBoundsSize + " m");
            return mesh;
        }

        // ---------------------------------------------------------------- scene parts

        static Transform Child(Transform parent, string name, int layer)
        {
            var t = parent.Find(name);
            if (t == null) { t = new GameObject(name).transform; t.SetParent(parent, false); }
            t.gameObject.layer = layer;
            return t;
        }

        static Camera BuildCamera(Transform root, List<string> log)
        {
            var t = Child(root, CameraName, 0);
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            var cam = t.GetComponent<Camera>(); if (cam == null) cam = t.gameObject.AddComponent<Camera>();
            foreach (var al in t.GetComponents<AudioListener>()) UnityEngine.Object.DestroyImmediate(al);
            cam.enabled = false; cam.targetTexture = null;
            cam.cullingMask = KH_PhotoGalleryEditor.PhotoCullingMask & ~(1 << RecordSnapLayer) & ~(1 << MonitorLayer);
            cam.fieldOfView = CamFov; cam.nearClipPlane = CamNear; cam.farClipPlane = CamFar;
            cam.allowHDR = false; cam.allowMSAA = false; cam.stereoTargetEye = StereoTargetEyeMask.None; cam.clearFlags = CameraClearFlags.Skybox;
            cam.useOcclusionCulling = true; cam.depth = -2;
            t.gameObject.tag = "Untagged";
            EditorUtility.SetDirty(cam);
            log.Add("camera: mask " + KH_PhotoGalleryEditor.DescribeMask(cam.cullingMask) + ", fov " + CamFov + ", near " + CamNear + ", far " + CamFar + ", HDR / MSAA off, disabled");
            return cam;
        }

        static MeshRenderer BuildSnap(Transform root, Mesh mesh, Material mat, List<string> log)
        {
            var t = Child(root, SnapName, RecordSnapLayer);
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            foreach (var col in t.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(col);
            var mf = t.GetComponent<MeshFilter>(); if (mf == null) mf = t.gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = t.GetComponent<MeshRenderer>(); if (mr == null) mr = t.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.allowOcclusionWhenDynamic = false; mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            t.gameObject.SetActive(false);
            EditorUtility.SetDirty(t.gameObject);
            log.Add("RecordSnap: layer " + RecordSnapLayer + " (" + LayerMask.LayerToName(RecordSnapLayer) + "), attach " + SnapAttachDistance + " m, aim off, hidden from eyes, queue " + SnapRenderQueue + ", occlusion off, hidden");
            return mr;
        }

        class Ui { public GameObject monitor, picker; public MeshRenderer screen; public TextMeshProUGUI state, snapLabel, dogLabel, droneLabel; public Image[] modeImages, ctlImages; public Image snapImage, droneImage; }

        public static string ModeButtonName(int m) { return "Mode" + m; }
        public static string ModeEvent(int m) { return "_Mode" + m; }

        static Ui BuildMonitor(Transform root, Material screenMat, Ctx c, UdonBehaviour target, List<string> log)
        {
            var u = new Ui();
            var mon = Child(root, MonitorName, MonitorLayer);
            mon.localPosition = Vector3.zero; mon.localRotation = Quaternion.identity; mon.localScale = Vector3.one;
            u.monitor = mon.gameObject;

            // ---- the picture (a plain Unlit/Texture quad in front of the canvas; the RT is set at runtime) ----
            var st = mon.Find(ScreenName);
            GameObject sgo;
            if (st == null) { sgo = GameObject.CreatePrimitive(PrimitiveType.Quad); sgo.name = ScreenName; sgo.transform.SetParent(mon, false); } else sgo = st.gameObject;
            foreach (var col in sgo.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(col);
            sgo.layer = MonitorLayer;
            sgo.transform.localPosition = new Vector3(0f, ScreenCenterPxY * Px, -ScreenFrontM);
            sgo.transform.localRotation = Quaternion.identity;
            sgo.transform.localScale = new Vector3(ScreenM.x, ScreenM.y, 1f);
            u.screen = sgo.GetComponent<MeshRenderer>();
            u.screen.sharedMaterial = screenMat;
            u.screen.shadowCastingMode = ShadowCastingMode.Off; u.screen.receiveShadows = false;
            u.screen.lightProbeUsage = LightProbeUsage.Off; u.screen.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // ---- the panel canvas ----
            var pt = mon.Find(PanelName);
            GameObject go;
            if (pt == null) { go = new GameObject(PanelName, typeof(RectTransform)); go.transform.SetParent(mon, false); } else go = pt.gameObject;
            go.layer = MonitorLayer;
            var canvas = go.GetComponent<Canvas>(); if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.overrideSorting = false;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            foreach (var cs in go.GetComponents<CanvasScaler>()) UnityEngine.Object.DestroyImmediate(cs);
            if (go.GetComponent<GraphicRaycaster>() == null) go.AddComponent<GraphicRaycaster>();
            if (go.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null) go.AddComponent<VRC.SDK3.Components.VRCUiShape>();
            var box = go.GetComponent<BoxCollider>(); if (box == null) box = go.AddComponent<BoxCollider>();
            box.isTrigger = true; box.center = Vector3.zero; box.size = new Vector3(CanvasPx.x, CanvasPx.y, 2f);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = CanvasPx;
            rt.localPosition = Vector3.zero; rt.localRotation = Quaternion.identity; rt.localScale = Vector3.one * Px;

            var frame = KD_ShopSetupEditor.EnsureUiChild(go.transform, "Frame", Vector2.zero, CanvasPx);
            foreach (var g in frame.GetComponents<Graphic>()) if (!(g is Image)) UnityEngine.Object.DestroyImmediate(g);
            var fimg = frame.GetComponent<Image>(); if (fimg == null) fimg = frame.AddComponent<Image>();
            fimg.color = FrameColor; fimg.raycastTarget = false; KD_UiSpriteEditor.ApplyFlatSkin(fimg);
            frame.transform.SetAsFirstSibling();

            Label(go.transform, "Title", TitlePos, TitleSize, KH_VideoCamera.WordTitle, c.font, 28f, 18f, TextAlignmentOptions.MidlineLeft, false).color = KD_UiSpriteEditor.AccentGold;
            Btn(go.transform, "Close", ClosePos, CloseSize, "×", c.font, target, nameof(KH_VideoCamera._Close), log);
            foreach (var old in new[] { "ModeDog", "ModeSelf" }) { var o = go.transform.Find(old); if (o != null) { UnityEngine.Object.DestroyImmediate(o.gameObject); log.Add("removed the W1 button " + old); } }
            // §15.5: 12 → 10 modes. The buttons past ModeCount (the old Mode10 / Mode11) are removed; the rest are rewritten in place
            for (int m = KH_VideoCamera.ModeCount; m < 16; m++) { var o = go.transform.Find(ModeButtonName(m)); if (o != null) { UnityEngine.Object.DestroyImmediate(o.gameObject); log.Add("removed the old button " + ModeButtonName(m)); } }
            u.modeImages = new Image[KH_VideoCamera.ModeCount];
            for (int m = 0; m < KH_VideoCamera.ModeCount; m++)
                u.modeImages[m] = Btn(go.transform, ModeButtonName(m), ModePos(m), ModeSize, KH_VideoCamera.ModeWord(m), c.font, target, ModeEvent(m), log).GetComponent<Image>();
            // §15.5: the camera controls (one row) and the drone toggle (beside the snap toggle)
            string[] ctlWords = KH_VideoCamera.CtlWords();
            u.ctlImages = new Image[ctlWords.Length];
            for (int i = 0; i < ctlWords.Length; i++)
            {
                var cb = Btn(go.transform, CtlName(i), CtlPos(i), CtlSize, ctlWords[i], c.font, target, CtlEvents[i], log);
                var cl = LabelOf(cb); if (cl != null) { cl.fontSizeMax = 24f; cl.fontSize = 24f; cl.fontSizeMin = 12f; EditorUtility.SetDirty(cl); }
                u.ctlImages[i] = cb.GetComponent<Image>();
            }
            var droneBtn = Btn(go.transform, DroneToggleName, DroneTogglePos, DroneToggleSize, KH_VideoCamera.WordDrone + ": " + KH_VideoCamera.WordShow, c.font, target, nameof(KH_VideoCamera._ToggleDrone), log);
            u.droneImage = droneBtn.GetComponent<Image>(); u.droneLabel = LabelOf(droneBtn);
            // the W4 overlays (Video/4) must stay above the new buttons (drawn last, they also take the pointer first)
            foreach (var ov in new[] { PersonListName, PersonConfirmName }) { var o = go.transform.Find(ov); if (o != null) o.SetAsLastSibling(); }
            var picker = KD_ShopSetupEditor.EnsureUiChild(go.transform, "DogPicker", PickerPos, PickerSize);
            foreach (var g in picker.GetComponents<Graphic>()) UnityEngine.Object.DestroyImmediate(g);
            Btn(picker.transform, "Prev", new Vector2(-PickerButtonX, 0f), PickerButtonSize, "◀", c.font, target, nameof(KH_VideoCamera._DogPrev), log);
            Btn(picker.transform, "Next", new Vector2(PickerButtonX, 0f), PickerButtonSize, "▶", c.font, target, nameof(KH_VideoCamera._DogNext), log);
            u.dogLabel = Label(picker.transform, "Name", Vector2.zero, PickerLabelSize, "-", c.font, 24f, 14f, TextAlignmentOptions.Center, false);
            u.picker = picker;
            var snapBtn = Btn(go.transform, "Snap", SnapPos, SnapSize, KH_VideoCamera.WordSnap + ": " + KH_VideoCamera.WordOn, c.font, target, nameof(KH_VideoCamera._ToggleSnap), log);
            u.snapImage = snapBtn.GetComponent<Image>(); u.snapLabel = LabelOf(snapBtn);
            Label(go.transform, "SnapNote", NotePos, NoteSize, KH_VideoCamera.WordSnapNote, c.font, 17f, 12f, TextAlignmentOptions.MidlineLeft, true);
            u.state = Label(go.transform, "State", StatePos, StateSize, KH_VideoCamera.WordFilming, c.font, 20f, 12f, TextAlignmentOptions.MidlineLeft, false);
            foreach (var tr in mon.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = MonitorLayer;
            BuildBack(mon, log);
            mon.gameObject.SetActive(false);
            EditorUtility.SetDirty(mon.gameObject);
            log.Add("monitor: layer " + MonitorLayer + ", screen " + ScreenM + " m, canvas " + CanvasPx + " px at " + Px + " m/px, × / " + KH_VideoCamera.ModeCount + " mode buttons " + ModeSize + " px / ◀ name ▶ / " + KH_VideoCamera.WordSnap + ", hidden");
            return u;
        }

        /// <summary>[撮影カメラ] / [モニターを呼ぶ] / [しまう] on the photo page. Saved hidden; KH_VideoCamera.Start shows [撮影カメラ] on PC only.
        /// Placed before the page's Large / HelpPanel overlays so those still cover them.</summary>
        static GameObject[] BuildEntry(Ctx c, UdonBehaviour target, List<string> log)
        {
            var page = c.photoPage.transform;
            var open = Btn(page, EntryOpenName, EntryOpenPos, EntryOpenSize, KH_VideoCamera.WordTitle, c.font, target, nameof(KH_VideoCamera._Open), log);
            var recall = Btn(page, EntryRecallName, EntryRecallPos, EntryRecallSize, KH_VideoCamera.WordRecall, c.font, target, nameof(KH_VideoCamera._Recall), log);
            var close = Btn(page, EntryCloseName, EntryClosePos, EntryCloseSize, KH_VideoCamera.WordPutAway, c.font, target, nameof(KH_VideoCamera._Close), log);
            // Every run (also after Photo/1 rebuilt the page): the buttons are found or created by name (EnsureButton), then placed just before the
            // FIRST full-page overlay (Large / HelpPanel), so the overlays always cover them. Only a button that sits after it is moved
            // (a fixed index moved a button that was already before the overlay to AFTER it on a rerun, 2026-10-03).
            foreach (var b in new[] { open, recall, close })
            {
                int first = int.MaxValue;
                foreach (var name in new[] { "Large", "HelpPanel" }) { var o = page.Find(name); if (o != null) first = Math.Min(first, o.GetSiblingIndex()); }
                if (first != int.MaxValue && b.transform.GetSiblingIndex() > first) b.transform.SetSiblingIndex(first);
                b.SetActive(false); EditorUtility.SetDirty(b);
            }
            log.Add("entry: photo page " + EntryOpenName + " " + EntryOpenPos + " / " + EntryRecallName + " / " + EntryCloseName + " (hidden; PC Start shows the open button)");
            return new[] { open, recall, close };
        }

        static TextMeshProUGUI LabelOf(GameObject button) { var t = button.transform.Find("Label"); return t != null ? t.GetComponent<TextMeshProUGUI>() : null; }

        static GameObject Btn(Transform parent, string name, Vector2 pos, Vector2 size, string text, TMP_FontAsset font, UdonBehaviour target, string evt, List<string> log)
        {
            var sb = new StringBuilder();
            var go = KD_ShopSetupEditor.EnsureButton(parent, name, pos, size, KD_UiSpriteEditor.ActionNeutral, font, text, target, evt, sb);
            if (sb.Length > 0) log.Add(sb.ToString().Trim());
            var label = LabelOf(go);
            if (label != null) { label.enableAutoSizing = true; label.fontSizeMax = 22f; label.fontSize = 22f; label.fontSizeMin = KD_LocalizationEditor.ButtonFontSizeMin; EditorUtility.SetDirty(label); }
            return go;
        }

        static TextMeshProUGUI Label(Transform parent, string name, Vector2 pos, Vector2 size, string text, TMP_FontAsset font, float max, float min, TextAlignmentOptions align, bool wrap)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            foreach (var g in go.GetComponents<Graphic>()) if (!(g is TextMeshProUGUI)) UnityEngine.Object.DestroyImmediate(g);
            var t = go.GetComponent<TextMeshProUGUI>(); if (t == null) t = go.AddComponent<TextMeshProUGUI>();
            t.font = font; t.text = text; t.color = Ink; t.alignment = align; t.raycastTarget = false;
            t.enableWordWrapping = wrap; t.overflowMode = TextOverflowModes.Overflow;
            t.enableAutoSizing = true; t.fontSizeMax = max; t.fontSizeMin = min; t.fontSize = max; t.richText = false;
            EditorUtility.SetDirty(t);
            return t;
        }

        /// <summary>W1 手直し 3: the monitor seen from behind showed the world-space UI mirrored (UI graphics draw both faces). An opaque plate in the
        /// frame colour, just behind the canvas and facing away from the viewer, hides it. It is culled from the front (Unlit/Color culls back faces),
        /// so the front view does not change. Layer 17 like the rest of the monitor, no collider, no shadows.</summary>
        static void BuildBack(Transform mon, List<string> log)
        {
            var mat = EnsureMat(BackMatPath, BackShaderName, log);
            mat.color = FrameColor; EditorUtility.SetDirty(mat);
            var t = mon.Find(BackName);
            GameObject go;
            if (t == null) { go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = BackName; go.transform.SetParent(mon, false); } else go = t.gameObject;
            foreach (var col in go.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(col);
            go.layer = MonitorLayer;
            go.transform.localPosition = new Vector3(0f, 0f, BackBehindM);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // the Quad's visible face (-Z) now points +Z: away from the viewer
            go.transform.localScale = new Vector3(BackM.x, BackM.y, 1f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            EditorUtility.SetDirty(go);
            log.Add("monitor back plate " + BackM + " m, " + BackBehindM * 1000f + " mm behind, facing away (" + BackMatPath + ")");
        }

        // ================================================================ Video/9

        [MenuItem(Menu + "9 - Remove Video Camera")]
        public static void Remove()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var scene = EditorSceneManager.GetActiveScene();
            var log = new List<string>();
            Directory.CreateDirectory(OutDir);
            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-video-remove.unity", true);
            var root = SceneRoot(scene, SystemName, out _);
            if (root != null) { UnityEngine.Object.DestroyImmediate(root); log.Add("removed " + SystemName); }
            foreach (var name in new[] { EntryOpenName, EntryRecallName, EntryCloseName })
            {
                var go = KD_SceneObjects.Find(name, out int n);
                while (go != null) { UnityEngine.Object.DestroyImmediate(go); log.Add("removed " + name); go = KD_SceneObjects.Find(name, out n); }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) { Debug.LogError("[KH Video] VIDEO_REMOVE_FAIL scene save failed"); return; }
            Debug.Log("[KH Video] VIDEO_REMOVE_OK " + string.Join(" | ", log) + " (assets kept)");
        }

        static void WriteJson(string path, object o)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(o, true));
        }
    }
}
