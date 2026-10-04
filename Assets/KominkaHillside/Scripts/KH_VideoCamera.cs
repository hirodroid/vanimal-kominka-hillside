using KominkaDogRun;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-WLD-008: the follow video camera. docs/plans/2026-10-03-video-camera.md §2-§4, "W1 実装メモ" and "W2・W3 実装メモ".
    ///
    /// One local rig per client (sync None, no synced variables, no network entry). ONE Camera and ONE RenderTexture serve every mode;
    /// only the selected mode's pose function runs (one switch in KH_VideoCamera.Modes.cs; the walk modes in .ModesWalk.cs, the play / wash
    /// modes in .ModesPlay.cs). The RT exists only while the monitor is open: created in _Open, released and destroyed in _Close.
    ///
    /// - Rendering (W1 手直し 3): the camera renders in the NORMAL camera phase, like KD_PhotoCapture (enabled, then drawn by Unity with the
    ///   other cameras), because a manual Camera.Render call inside PostLateUpdate (LateUpdate, order 31000) drew the LOCAL avatar before VRChat's
    ///   final placement of it (a real PCVR client with an OVR Advanced Settings playspace offset: the avatar floated ~1 m up in the video only).
    ///   Exactly one render per tick: the tick enables the camera (EnableForTick, the only place that enables it); KH_VideoCameraHook on the
    ///   camera's GameObject receives Unity's OnPostRender (Udon event _onPostRender) and calls _OnCamRendered, which disables it at once.
    ///   Safety net: if no callback arrived, the next PostLateUpdate disables it (_missedCallbacks counts that).
    ///   _ticks counts ticks, _renders counts real renders (callbacks). The pose is computed only on ticks; occlusion rays at 1 / occlusionInterval.
    /// - The monitor stays where it was opened until × (user decision); [モニターを呼ぶ] places it in front of the head again.
    /// - RecordSnap: a quad with the unchanged KD_PhotoPrint shader (attach 100000 m, no aim check, hidden from eyes) and a mesh whose
    ///   bounds cover the world, so every VRChat camera pass (_VRChatCameraMode > 0.5: photo / video / stream AND screenshots) shows the
    ///   live RT full screen wherever the user walks (design §3.2-3.3). Its layer (KH_VideoCameraEditor.RecordSnapLayer, Water 4) is excluded
    ///   from this camera and the auto photo camera. Its renderQueue (5001) draws above the photo viewer / frames, the night overlay and the fade.
    /// - PC only: on Android / iOS builds Start hides everything and disables this behaviour; the entry buttons start hidden in the scene
    ///   and only a PC client shows [撮影カメラ]. The scene is the same for every build target.
    /// - Other players: read only through TryPersonPose (W4, design §5.5 / §13, KH_VideoCamera.Subjects.cs), and only by じぶん mode when another
    ///   person was chosen and that person's synced grant table allows it (KH_VideoCamera.People.cs: picker, §14.3 confirmation, fallback).
    ///   Poses are read only in KH_VideoCamera.Subjects.cs. Where another mode would need another person (the petting hand of someone else,
    ///   a thrower who is not me, a washer who is not me) it still falls back to a framing of the dog alone.
    ///
    /// Every public method is local-only (`_` prefix, vrc-networking). Panel texts are Japanese constants (13 languages in W5).
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public partial class KH_VideoCamera : UdonSharpBehaviour
    {
        // ---- modes (design §4.2; the panel order) ----
        public const int ModeDogFollow = 0, ModeSelfFollow = 1, ModeDogEye = 2, ModeDrone = 3, ModeSpot = 4, ModeDisc = 5,
            ModeCatchWait = 6, ModeWelcome = 7, ModeFureai = 8, ModeWash = 9, ModeOrbit = 10, ModePassBy = 11;
        public const int ModeCount = 12;
        // ---- the shot signal for a later sequencer (the user's "patterns", 2026-10-04): ShotSerial goes up on each event, ShotKind says which ----
        public const int ShotNone = 0, ShotLost = 1, ShotCatch = 2, ShotReturned = 3, ShotPassed = 4;
        // ---- state line ----
        public const int StateNone = -1, StateFilming = 0, StateLost = 1, StateFade = 2, StateRtFailed = 3, StateUnsupported = 4;

        // ---- Japanese labels (the builder writes the static ones and bakes the glyphs of all of them; W5 moves them to UITranslations.json) ----
        public const string WordTitle = "撮影カメラ";
        public const string WordRecall = "モニターを呼ぶ";
        public const string WordPutAway = "しまう";
        public const string WordModeDog = "犬追従";
        public const string WordModeSelf = "じぶん";
        public const string WordModeDogEye = "犬目線";
        public const string WordModeDrone = "ドローン";
        public const string WordModeSpot = "定点";
        public const string WordModeDisc = "ディスク";
        public const string WordModeCatchWait = "キャッチ待ち";   // 2026-10-04 user: wait at the CATCH point, not the landing point
        public const string WordModeWelcome = "おかえり";
        public const string WordModeFureai = "ふれあい";
        public const string WordModeWash = "手元";
        public const string WordModeOrbit = "一周";
        public const string WordModePassBy = "通り過ぎ";
        public const string WordSnap = "VRChatカメラに映す";
        public const string WordOn = "ON", WordOff = "OFF";
        public const string WordFilming = "撮影中";
        public const string WordLost = "が見つかりません";
        public const string WordNoWash = "洗っている犬がいません";
        public const string WordNoSpot = "定点がありません";
        public const string WordFade = "移動中…";
        public const string WordRtFailed = "撮影カメラを用意できませんでした";
        // _VRChatCameraMode > 0.5 is every VRChat camera pass (hand-held photo / video / stream and screenshots), not only the hand-held camera
        public const string WordSnapNote = "ONの間は、VRChatカメラの写真・動画・配信とスクリーンショットがこの映像になります。ふつうに撮るときはOFFに";
        public const string WordPhotoCamOn = "VRChatカメラ: 映しています";
        public const string WordPhotoCamOff = "VRChatカメラ: 出ていません";

        /// <summary>The panel label of a mode (the builder writes the buttons from this; the probe and the harness print it).</summary>
        public static string ModeWord(int m)
        {
            switch (m)
            {
                case ModeDogFollow: return WordModeDog;
                case ModeSelfFollow: return WordModeSelf;
                case ModeDogEye: return WordModeDogEye;
                case ModeDrone: return WordModeDrone;
                case ModeSpot: return WordModeSpot;
                case ModeDisc: return WordModeDisc;
                case ModeCatchWait: return WordModeCatchWait;
                case ModeWelcome: return WordModeWelcome;
                case ModeFureai: return WordModeFureai;
                case ModeWash: return WordModeWash;
                case ModeOrbit: return WordModeOrbit;
                case ModePassBy: return WordModePassBy;
            }
            return "?";
        }

        /// <summary>Which picker a mode shows: 1 = dog (the disc modes use the selected dog's disc), 2 = spot, 0 = none.</summary>
        public static int PickerKind(int m)
        {
            if (m == ModeSpot) return 2;
            if (m == ModeDogFollow || m == ModeDogEye || m == ModeDisc || m == ModeCatchWait || m == ModeWelcome || m == ModeFureai) return 1;
            return 0;
        }

        [Header("配線 (Video/1 が書く)")]
        public Camera cam;
        public GameObject monitorRoot;
        public Renderer screenRenderer;
        [Tooltip("録画モードの吸着板 (KD_PhotoPrint、bounds を広げた Quad)。ON の間だけ有効")]
        public Renderer snapRenderer;
        [Tooltip("写真の面の入口 [撮影カメラ] / [モニターを呼ぶ] / [しまう]。シーンでは隠しておき、PC の Start だけが [撮影カメラ] を出す")]
        public GameObject entryOpen, entryRecall, entryClose;
        public TMP_Text stateLabel, snapLabel, dogLabel;
        [Tooltip("モードのボタンの背景 (ModeCount 個、モードの番号順)")]
        public Image[] modeImages;
        public Image snapImage;
        public GameObject dogPicker;
        public KD_Language language;
        public KH_LeashManager manager;
        public KH_TravelFade fade;
        [Tooltip("犬 (KD_DogDefs.All の順 = dogIndex)。参照配列なので既定値は持たない (vrc-udonsharp §4)")]
        public Transform[] dogRoots;
        public Transform[] dogHeads;
        public string[] dogNameKeys;
        [Tooltip("犬ごと (dogIndex の順): フリスビー・ディスク・撫で (Fetch)・徘徊・洗い")]
        public KH_FrisbeeCatch[] catchers;
        public KH_Frisbee[] discs;
        public KD_DogFetchController[] fetches;
        public KD_DogWanderController[] wanders;
        public KH_DogWash[] washes;
        [Tooltip("洗い場の呼び出し (全部)")]
        public KH_WashStationCall[] washCalls;
        public KH_WaterSpot waterSpot;
        [Tooltip("定点 (データだけ。Video/1 が洗い場と水遊び場から作り、Video/2 が地面と見通しを照合する)")]
        public string[] spotNames;
        public Vector3[] spotPositions;
        public Vector3[] spotLooks;
        public float[] spotFovs;
        public int[] spotStations;   // index into washCalls (-1 = the river)
        [Tooltip("任意: ショットの出来事 (見失った・キャッチ・戻った・通り過ぎた) を _OnVideoShot で受け取る Behaviour (後で作るパターンの再生役)。null = 送らない")]
        public UdonSharpBehaviour shotListener;
        [Tooltip("通り過ぎる散歩の撮影地点 (データだけ。写真の場所から Video/1 が道の脇へずらして作る)")]
        public Vector3[] passPositions;

        [Header("描画 (試作値。Video/1 が書き、Video/2 が照合する)")]
        public int renderWidth = 1280, renderHeight = 720, renderDepth = 24;
        public float renderFps = 30f;
        public int blockMask = 1 | (1 << 11);
        public float clearance = 0.18f, groundLift = 0.25f, occlusionInterval = 0.1f, pushOutScale = 0.45f;
        public float cutDistance = 6f, lostHoldSeconds = 3f;
        [Tooltip("なめらかさの時定数 (s)。位置・注視点・向きを決める速度・FOV")]
        public float posTau = 0.6f, turnPosTau = 1.0f, lookTau = 0.25f, headingTau = 0.8f, fovTau = 0.5f;
        [Tooltip("これより遅いときは進む向きを更新しない (止まっても回らない)")]
        public float stillSpeed = 0.3f;
        [Tooltip("1 秒あたりこれ以上向きが変わったら位置の時定数を turnPosTau にする (度/秒)")]
        public float turnRateDeg = 90f;

        [Header("犬追従 (§4.2 #1)")]
        public float dogFov = 50f, dogSideDeg = 40f, dogFill = 0.4f, dogDistMin = 1.2f, dogDistMax = 5f, dogLift = 0.35f, dogLookAhead = 0.5f;
        [Header("じぶん (§4.2 #2、自分だけ。W2: 自分の犬がいるときは引いて、頭と犬の間を見る)")]
        public float selfFov = 55f, selfSideDeg = 35f, selfDistance = 2.4f, selfDistanceWithDog = 3.2f, selfLift = -0.1f, selfLookToDogs = 0.55f, selfChestDrop = 0.35f, selfDogMax = 8f;
        [Header("犬目線 (§4.4: 犬のそばの低い位置から自分を見上げる。自分が遠いときは犬の前の景色)")]
        public float dogEyeFov = 60f, dogEyeSideDeg = 50f, dogEyeBack = 0.35f, dogEyeLift = 0.05f, dogEyeUserMax = 6f, dogEyeTau = 0.3f;
        public float dogEyeSceneFov = 70f, dogEyeAhead = 0.15f, dogEyeSceneLift = 0.04f, dogEyePitch = 3f;
        [Header("ドローン俯瞰 (§4.2 #4、自分を追う)")]
        public float droneFov = 60f, droneBack = 6f, droneHeight = 7f, droneAhead = 2f, droneTau = 1.2f, droneGroundMin = 4f;
        [Header("定点 (§4.5)")]
        public float spotLookTau = 0.35f, spotDogPull = 0.3f;
        [Header("ディスク追従 (§4.6)")]
        public float discFov = 60f, discSideK = 1f, discSideMin = 3f, discSideMax = 12f, discUp = 0.8f, discTau = 0.3f, discLookTau = 0.1f, discFlySpeed = 2f, discApartMax = 15f, discVelTau = 0.3f;
        [Header("落下地点の待機 (§4.7)")]
        [Tooltip("キャッチ待ち: 犬が投げた時点で決めたキャッチの予定 (犬の Owner の端末だけが持つ) の横、キャッチの高さ。無いときは着地の予測へ")]
        public float catchFov = 55f, catchSide = 6f, catchUp = 0.3f, catchLookBack = 2f, catchPlanWait = 0.6f, catchReplanMove = 1f;
        public float landFov = 55f, landSide = 5f, landBeyond = 3f, landUp = 1.2f, landNear = 8f, landMoveMax = 5f, landStep = 0.08f, landMaxT = 8f, landRemoteSample = 0.25f, landRepredict = 0.5f;
        [Header("おかえり (§4.2 #8)")]
        public float welcomeFov = 55f, welcomeDist = 1.6f, welcomeHeight = 0.45f, welcomeAhead = 3f;
        [Header("ふれあい (§4.10)")]
        public float fureaiFov = 45f, fureaiDist = 0.55f, fureaiSideDeg = 60f, fureaiLift = 0.05f, fureaiShakeSeconds = 3f, fureaiShakeDist = 1.2f;
        [Header("洗浄の手元 (§4.10)")]
        public float washFov = 45f, washDist = 0.6f, washUp = 0.25f, washLookTau = 0.4f;
        [Header("ゆっくり一周 (§4.9)")]
        public float orbitFov = 50f, orbitRadius = 3.5f, orbitHeight = 1.4f, orbitDegPerSec = 6f, orbitCenterTau = 2f, orbitLookUp = 0.6f, orbitTau = 0.3f;
        [Header("通り過ぎる散歩 (§4.8)")]
        public float passFov = 50f, passLookUp = 0.5f, passNear = 25f, passFar = 120f, passRecede = 15f, passAfter = 8f, passGiveUp = 60f, passTooFar = 150f;

        [Header("モニター")]
        public float monitorDistance = 1.3f, monitorDrop = 0.25f;
        public bool snapOnOpen = true;
        public Color modeOnColor = new Color(0.30f, 0.42f, 0.52f);
        public Color modeOffColor = new Color(0.34f, 0.30f, 0.28f);

        // ---- runtime (local). The harness reads several of these by name ----
        private bool _supported, _started;
        private bool _open;
        private bool _snapOn;
        private int _mode = ModeDogFollow;
        private int _dog;
        private int _spot;
        private RenderTexture _rt;
        private float _nextRender, _lastRenderAt, _interval;
        private int _renders, _ticks, _missedCallbacks, _cuts, _opens, _closes;
        private bool _camOn;   // enabled by the tick, waiting for its OnPostRender
        // valid poses obtained for the CURRENT subject in the CURRENT session (reset on open, mode change and dog / spot change).
        // Nothing is drawn and the picture stays detached until this is > 0, so a reopen never shows an earlier session's framing
        private int _subjectPoses;
        private bool _pictureShown;
        private int _state = StateNone;
        private int _shownState = -99;
        private float _lostSince = -1f;
        private bool _cutNext = true;
        // the camera pose (smoothed)
        private Vector3 _camPos, _look;
        private float _fov = 50f;
        // the wanted pose (the selected mode writes it; ModeTarget resets the per-mode options first)
        private Vector3 _wantPos, _wantLook, _subject;
        private float _wantFov = 50f, _subjectSize = 0.6f;
        private float _wantTau = 0.6f, _wantLookTau = 0.25f, _wantGroundMin = 0.25f;
        private bool _wantFixed;   // a data position (spot / pass-by point): no occlusion clip, no ground lift
        private int _lostWord;     // 0 = "<dog / mode> が見つかりません", 1 = no washing dog, 2 = no spot
        private int _shotSerial, _shotKind;

        // ---- read-only state for a sequencer (and the harness) ----
        public bool IsOpen { get { return _open; } }
        public int CurrentMode { get { return _mode; } }
        public int CurrentDog { get { return _dog; } }
        public int ShotSerial { get { return _shotSerial; } }
        public int ShotKind { get { return _shotKind; } }
        // occlusion cache
        private float _nextOcclusion, _clipDist = 1e6f, _groundY;
        private bool _groundValid;
        // photo camera readout (2 Hz)
        private float _nextPhotoCamCheck;
        private bool _photoCamActive, _photoCamLogged;

        private string L(string key) { return language != null ? language._Text(key) : key; }

        void Start()
        {
            bool supported = true;
#if UNITY_ANDROID || UNITY_IOS
            supported = false;   // PC only (IMP-WLD-008). Quest and phones never show the entry and never allocate anything
#endif
            _supported = supported;
            _started = true;
            _interval = renderFps > 1f ? 1f / renderFps : 1f / 30f;
            if (cam != null) { cam.enabled = false; cam.targetTexture = null; }
            if (monitorRoot != null) monitorRoot.SetActive(false);
            if (snapRenderer != null) snapRenderer.gameObject.SetActive(false);
            if (cam == null || monitorRoot == null || screenRenderer == null || snapRenderer == null)
            {
                Debug.LogError("[KH_VideoCamera] wiring incomplete. Run Tools/KominkaHillside/Video/1.");
                _supported = false;
            }
            DrawEntry();
            enabled = false;   // nothing runs per frame until the monitor opens
        }

        // ================================================================ entry (photo page) and monitor buttons. Local only

        /// <summary>[撮影カメラ] on the photo page. Opens the monitor in front of the head, or moves it there when already open.</summary>
        public void _Open()
        {
            if (!_started || !_supported) return;
            if (_open) { _Recall(); return; }
            if (_rt != null) ReleaseRt();
            RenderTexture rt = new RenderTexture(renderWidth, renderHeight, renderDepth, RenderTextureFormat.ARGB32);
            if (!rt.Create())
            {
                Destroy(rt);
                _state = StateRtFailed;
                Debug.LogError("[KH_VideoCamera] RenderTexture " + renderWidth + "x" + renderHeight + " could not be created");
                DrawEntry();
                return;
            }
            _rt = rt;
            cam.targetTexture = _rt;
            cam.enabled = false; _camOn = false;
            _open = true; _opens++;
            _person = -1; _personLive = false; PersonUiClose();   // W4: じぶん starts with me; an earlier film session is never resumed
            KH_TravelMailbox myBox = PersonMyBox();
            _filmSeenSerial = myBox != null ? myBox.filmResultSerial : -1;
            NewSubject();   // the picture stays detached (black) until this session has a valid pose
            _snapOn = snapOnOpen;
            if (_dog < 0 || dogRoots == null || _dog >= dogRoots.Length) _dog = 0;
            OwnDogFirst();   // start on the player's own leashed dog; keeps the last chosen dog when the player has none
            AutoSubject();
            PlaceMonitor();
            monitorRoot.SetActive(true);
            snapRenderer.gameObject.SetActive(_snapOn);
            _interval = renderFps > 1f ? 1f / renderFps : 1f / 30f;
            _nextRender = Time.time; _lastRenderAt = -1f;
            _state = StateFilming; _shownState = -99;
            _nextPhotoCamCheck = 0f;
            enabled = true;
            DrawEntry();
            DrawPanel();
        }

        /// <summary>[モニターを呼ぶ]: the open monitor moves in front of the head (it never follows by itself).</summary>
        public void _Recall()
        {
            if (!_open) return;
            PlaceMonitor();
        }

        /// <summary>× on the monitor and [しまう] on the photo page. Stops the camera, hides everything and destroys the RT.</summary>
        public void _Close()
        {
            if (!_open && _rt == null) return;
            PersonCloseAll();   // W4: × ends the film session (pending / in use) before anything else; a reopen needs a new request (§5.4)
            _open = false; _closes++;
            if (cam != null) cam.enabled = false;
            _camOn = false;
            if (snapRenderer != null) snapRenderer.gameObject.SetActive(false);
            if (monitorRoot != null) monitorRoot.SetActive(false);
            ReleaseRt();   // the screen and the RecordSnap materials lose the texture before the RT is destroyed (no old picture stays visible)
            // forget every cached pose: the next open starts with a cut, never with a pose or heading from this session
            _cutNext = true; _lostSince = -1f; _subjectPoses = 0; ResetHeading(); ResetModeState();
            _camPos = Vector3.zero; _look = Vector3.zero; _wantPos = Vector3.zero; _wantLook = Vector3.zero; _subject = Vector3.zero;
            _clipDist = 1e6f; _groundValid = false; _lastRenderAt = -1f;
            _state = StateNone;
            enabled = false;
            DrawEntry();
        }

        // ---- the mode buttons (onClick → SendCustomEvent; no parameters) ----
        public void _Mode0() { SetMode(0); }
        public void _Mode1() { SetMode(1); }
        public void _Mode2() { SetMode(2); }
        public void _Mode3() { SetMode(3); }
        public void _Mode4() { SetMode(4); }
        public void _Mode5() { SetMode(5); }
        public void _Mode6() { SetMode(6); }
        public void _Mode7() { SetMode(7); }
        public void _Mode8() { SetMode(8); }
        public void _Mode9() { SetMode(9); }
        public void _Mode10() { SetMode(10); }
        public void _Mode11() { SetMode(11); }
        // W1 names (the harness V1-V3 uses them)
        public void _ModeDog() { SetMode(ModeDogFollow); }
        public void _ModeSelf() { SetMode(ModeSelfFollow); }
        /// <summary>The single entry a sequencer uses: mode + subject (dog / spot; a negative value keeps the current one). Always starts a new
        /// subject (cut, no hold from the previous shot) even when the mode stays. Local only; called directly by another local behaviour.</summary>
        public void _SetShot(int mode, int dog, int spot)
        {
            if (!_open || mode < 0 || mode >= ModeCount) return;
            if (dog >= 0 && dogRoots != null && dog < dogRoots.Length) _dog = dog;
            if (spot >= 0 && spotPositions != null && spot < spotPositions.Length) _spot = spot;
            _mode = mode;
            PersonUiClose(); _personLive = false;   // W4: a shot change closes the person list / confirmation (never sends anything)
            NewSubject();
            DrawPanel();
        }

        void RaiseShot(int kind)
        {
            _shotSerial++; _shotKind = kind;
            if (shotListener != null) shotListener.SendCustomEvent("_OnVideoShot");
        }

        /// <summary>◀ / ▶ of the picker: the dog, or the spot in 定点.</summary>
        public void _DogPrev() { StepPick(-1); }
        public void _DogNext() { StepPick(1); }

        /// <summary>[VRChatカメラに映す: ON / OFF] (the RecordSnap quad).</summary>
        public void _ToggleSnap()
        {
            if (!_open) return;
            _snapOn = !_snapOn;
            snapRenderer.gameObject.SetActive(_snapOn);
            DrawPanel();
        }

        // ================================================================ per frame (only while open)

        public override void PostLateUpdate()
        {
            if (!_open) { enabled = false; return; }
            // safety net: the tick's render normally disables the camera from OnPostRender; if that callback never came, disable it here
            // (the original "enable for one frame" timing: the camera then stays enabled until here, so extra draws inside that window are NOT ruled out - _missedCallbacks tells when this path ran)
            if (_camOn) { cam.enabled = false; _camOn = false; _missedCallbacks++; }
            float now = Time.time;
            if (now >= _nextPhotoCamCheck) { _nextPhotoCamCheck = now + 0.5f; ReadPhotoCamera(); PersonTick(now); }
            // W4 (Codex review 1): while another person's picture is up, check the permission EVERY frame, before the wait for the next render
            // tick and before the fade's return, and detach the picture at once when it is gone (one field test when no person is shown)
            if (_personLive && !PersonStillUsable()) PersonDrop();
            if (now < _nextRender) return;
            _nextRender += _interval;
            if (_nextRender < now) _nextRender = now + _interval;
            float dt = _lastRenderAt < 0f ? _interval : Mathf.Clamp(now - _lastRenderAt, 0.001f, 0.5f);
            _lastRenderAt = now;

            if (fade != null && fade._Busy())
            {
                _cutNext = true; ResetHeading();
                SetState(StateFade);
                return;   // no render during the travel fade; the cut after it does not smooth across the jump
            }
            bool ok = ModeTarget(dt, now);
            if (!ok)
            {
                if (_lostSince < 0f) { _lostSince = now; RaiseShot(ShotLost); }
                SetState(StateLost);
                // a lost DOG keeps this session's last framing for this subject a little; then the picture is detached (black).
                // Without a valid pose for this subject in this session nothing is drawn at all. W4: a lost / revoked PERMISSION must not
                // take this path (stop at once, drop the cached pose, no hold; design §13)
                if (_subjectPoses > 0 && now - _lostSince < lostHoldSeconds) EnableForTick();
                else ShowPicture(false);
                return;
            }
            if (_lostSince >= 0f) { _lostSince = -1f; _cutNext = true; }
            SetState(StateFilming);
            ApplyPose(dt, now);
            // W4 §13.3: the check that the person may be drawn, again right before the camera is enabled (separate from the pose read)
            if (_mode == ModeSelfFollow && _personLive && !PersonStillUsable()) { PersonDrop(); return; }
            EnableForTick(); _subjectPoses++;   // the picture is attached by _OnCamRendered after this subject's first real render
        }

        /// <summary>The only place that enables the camera: Unity draws it in this frame's normal camera phase (after VRChat placed the local
        /// avatar), then KH_VideoCameraHook.OnPostRender → _OnCamRendered disables it.</summary>
        void EnableForTick()
        {
            cam.enabled = true;
            _camOn = true;
            _ticks++;
        }

        /// <summary>KH_VideoCameraHook (on the camera's GameObject) forwards Unity's OnPostRender for THIS camera. Disables the camera so it
        /// renders exactly once per tick (no extra render from any other render path). A render that was not asked for is not counted.
        /// Local only (_ prefix).</summary>
        public void _OnCamRendered()
        {
            if (!_camOn) return;
            cam.enabled = false;
            _camOn = false;
            _renders++;
            if (_subjectPoses > 0) ShowPicture(true);   // the RT now holds this subject's picture
        }

        // ================================================================ shared post-processing (design §4.1)

        /// <summary>Occlusion / ground / dog push-out, then the jump cut or the smoothing, roll locked to world up.
        /// Per mode (ModeTarget): the position / look time constants, a fixed data position (no clip, no ground lift), the height above ground.</summary>
        void ApplyPose(float dt, float now)
        {
            Vector3 pos = _wantPos;
            if (!_wantFixed)
            {
                if (now >= _nextOcclusion) { _nextOcclusion = now + occlusionInterval; Occlusion(pos); }
                Vector3 d = pos - _subject;
                float len = d.magnitude;
                if (len > _clipDist && len > 0.01f) pos = _subject + d * (_clipDist / len);
            }
            pos = PushOutOfDogs(pos);
            float above = Mathf.Max(groundLift, _wantGroundMin);
            if (!_wantFixed && _groundValid && pos.y < _groundY + above) pos.y = _groundY + above;

            bool cut = _cutNext || (pos - _camPos).magnitude > cutDistance;
            float tau = now < _turnUntil ? Mathf.Max(turnPosTau, _wantTau) : _wantTau;
            _camPos = SmoothStep(_camPos, pos, cut, dt, tau);
            _look = SmoothStep(_look, _wantLook, cut, dt, _wantLookTau);
            _fov = cut ? _wantFov : Mathf.Lerp(_fov, _wantFov, BlendFactor(dt, fovTau));
            if (cut) { _cutNext = false; _cuts++; }
            Transform ct = cam.transform;
            ct.position = _camPos;
            ct.rotation = LookRoll0(_camPos, _look);   // roll 0
            cam.fieldOfView = Mathf.Clamp(_fov, 20f, 100f);
        }


        /// <summary>10 Hz: how far from the subject the camera may stand (terrain / walls, the photo director's rule), and the floor below it.
        /// A surface higher than the subject is not a floor (the eaves rule, KD_PhotoDirector.ClearCameraPos).</summary>
        void Occlusion(Vector3 pos)
        {
            Vector3 d = pos - _subject;
            float len = d.magnitude;
            _clipDist = 1e6f;
            if (len > 0.05f)
            {
                Vector3 n = d / len;
                float skip = Mathf.Min(_subjectSize * 0.6f, len * 0.5f);
                float span = len - skip;
                RaycastHit hit;
                if (span > 0.01f && Physics.Raycast(_subject + n * skip, n, out hit, span, blockMask, QueryTriggerInteraction.Ignore))
                    _clipDist = Mathf.Max(skip + hit.distance - clearance, 0.3f);
            }
            RaycastHit down;
            _groundValid = Physics.Raycast(new Vector3(pos.x, _subject.y + 0.5f, pos.z), Vector3.down, out down, 6f, blockMask, QueryTriggerInteraction.Ignore)
                && down.point.y <= _subject.y + clearance;
            if (_groundValid) _groundY = down.point.y;
        }

        /// <summary>Never inside a dog: push out horizontally from any dog body closer than DogSize × pushOutScale.</summary>
        Vector3 PushOutOfDogs(Vector3 pos)
        {
            if (dogRoots == null) return pos;
            for (int i = 0; i < dogRoots.Length; i++)
            {
                if (!DogValid(i)) continue;
                Vector3 c = DogSubject(i);
                float r = DogSize(i) * pushOutScale;
                Vector3 v = pos - c; v.y = 0f;
                float m = v.magnitude;
                if (m >= r || Mathf.Abs(pos.y - c.y) > r) continue;
                if (m < 0.001f) { v = Vector3.right; m = 1f; }
                Vector3 o = c + v * (r / m);
                pos.x = o.x; pos.z = o.z;
            }
            return pos;
        }

        // ================================================================ helpers

        void SetMode(int mode)
        {
            if (!_open || mode < 0 || mode >= ModeCount || mode == _mode) { DrawPanel(); return; }
            _mode = mode;
            PersonUiClose(); _personLive = false;   // W4 §14.3: another mode closes the confirmation (nothing sent); the session ends after 60 s away
            NewSubject();
            AutoSubject();
            DrawPanel();
        }

        /// <summary>A new subject (open, mode change, dog / spot change): forget every cached pose and the hold, detach the picture until a valid pose.</summary>
        void NewSubject()
        {
            _cutNext = true; _lostSince = -1f; _subjectPoses = 0;
            ResetHeading();
            ResetModeState();
            _nextOcclusion = 0f; _clipDist = 1e6f; _groundValid = false;
            ShowPicture(false);
        }

        /// <summary>The monitor screen and RecordSnap show the RT only while this subject has been drawn; otherwise black (no texture).</summary>
        void ShowPicture(bool show)
        {
            show = show && _open && _rt != null;
            if (show == _pictureShown) return;
            _pictureShown = show;
            screenRenderer.material.mainTexture = show ? _rt : null;
            snapRenderer.material.mainTexture = show ? _rt : null;
        }

        void StepPick(int step)
        {
            if (!_open) return;
            if (PickerKind(_mode) == 2)
            {
                int ns = spotPositions != null ? spotPositions.Length : 0;
                if (ns == 0) return;
                _spot = ((_spot + step) % ns + ns) % ns;
            }
            else
            {
                if (dogRoots == null || dogRoots.Length == 0) return;
                int n = dogRoots.Length;
                _dog = ((_dog + step) % n + n) % n;
            }
            NewSubject();
            DrawPanel();
        }

        /// <summary>On open in dog mode: start on the player's own leashed dog, if any. Returns whether one was found.</summary>
        bool OwnDogFirst()
        {
            if (manager == null || dogRoots == null) return false;
            for (int i = 0; i < dogRoots.Length; i++)
                if (DogValid(i) && manager._IsMine(i)) { _dog = i; return true; }
            return false;
        }

        void PlaceMonitor()
        {
            if (!SelfHead()) return;
            Vector3 head = _selfHead, fwd = _selfFwd;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();
            Transform m = monitorRoot.transform;
            m.position = head + fwd * monitorDistance + Vector3.down * monitorDrop;
            m.rotation = Quaternion.LookRotation(fwd, Vector3.up);   // +Z away from the viewer: the canvas and the screen face the player
        }

        void ReleaseRt()
        {
            if (cam != null) cam.targetTexture = null;
            if (screenRenderer != null) screenRenderer.material.mainTexture = null;
            if (snapRenderer != null) snapRenderer.material.mainTexture = null;
            _pictureShown = false;
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }
            _rt = null;
        }

        /// <summary>2 Hz: whether VRChat's photo camera is out (state line only; the snap never depends on it).
        /// VRCCameraSettings.PhotoCamera / Active are exposed to Udon in SDK 3.10.5 (design §1.3); ClientSim never creates the photo camera.</summary>
        void ReadPhotoCamera()
        {
            VRC.SDK3.Rendering.VRCCameraSettings pc = VRC.SDK3.Rendering.VRCCameraSettings.PhotoCamera;
            bool active = pc != null && pc.Active;
            if (active && !_photoCamLogged) { _photoCamLogged = true; Debug.Log("[KH_VideoCamera] VRChat photo camera is out (RecordSnap " + (_snapOn ? "ON" : "OFF") + ")"); }
            if (active != _photoCamActive) { _photoCamActive = active; _shownState = -99; DrawState(); }
        }

        void SetState(int s)
        {
            _state = s;
            if (s != _shownState) DrawState();
        }

        void DrawState()
        {
            _shownState = _state;
            if (stateLabel == null) return;
            string line;
            if (_state == StateLost)   // the "no subject" state (black picture once the hold is over)
            {
                if (_lostWord == 1) line = WordNoWash;
                else if (_lostWord == 2) line = WordNoSpot;
                else line = (PickerKind(_mode) == 1 ? DogName(_dog) : ModeWord(_mode)) + WordLost;
            }
            else if (_state == StateFade) line = WordFade;
            else if (_state == StateRtFailed) line = WordRtFailed;
            else line = WordFilming;
            stateLabel.text = line + "   " + (_photoCamActive ? WordPhotoCamOn : WordPhotoCamOff);
        }

        string DogName(int i)
        {
            if (dogNameKeys != null && i >= 0 && i < dogNameKeys.Length && dogNameKeys[i] != null && dogNameKeys[i].Length > 0) return L(dogNameKeys[i]);
            return "#" + (i + 1).ToString();
        }

        string SpotName(int i)
        {
            if (spotNames != null && i >= 0 && i < spotNames.Length && spotNames[i] != null) return spotNames[i];
            return "-";
        }

        void DrawEntry()
        {
            bool show = _supported && _started;
            if (entryOpen != null) entryOpen.SetActive(show && !_open);
            if (entryRecall != null) entryRecall.SetActive(show && _open);
            if (entryClose != null) entryClose.SetActive(show && _open);
        }

        void DrawPanel()
        {
            if (modeImages != null)
                for (int m = 0; m < modeImages.Length; m++)
                    if (modeImages[m] != null) modeImages[m].color = m == _mode ? modeOnColor : modeOffColor;
            int pk = PickerKind(_mode);
            if (dogPicker != null) dogPicker.SetActive(pk != 0);
            if (dogLabel != null) dogLabel.text = pk == 2 ? SpotName(_spot) : DogName(_dog);
            if (snapLabel != null) snapLabel.text = WordSnap + ": " + (_snapOn ? WordOn : WordOff);
            if (snapImage != null) snapImage.color = _snapOn ? modeOnColor : modeOffColor;
            DrawPerson();   // W4: the person picker (じぶん mode), the list and the confirmation
            _shownState = -99;
            DrawState();
        }

        void OnDisable()
        {
            // the behaviour is disabled on purpose while closed; a disabled camera never renders. Nothing else to undo here
            if (cam != null) cam.enabled = false;
            _camOn = false;
        }
    }
}
