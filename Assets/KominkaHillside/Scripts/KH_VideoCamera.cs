using KominkaDogRun;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-WLD-008 W1 (MVP): the follow video camera. docs/plans/2026-10-03-video-camera.md §2-§4 and "W1 実装メモ".
    ///
    /// One local rig per client (sync None, no synced variables, no network entry). ONE Camera and ONE RenderTexture serve every mode;
    /// only the selected mode's pose function runs (KH_VideoCamera.Modes.cs). The RT exists only while the monitor is open:
    /// created in _Open, released and destroyed in _Close (no leak on repeated open / close).
    ///
    /// - Rendering: the camera component is ALWAYS disabled; at each renderFps tick PostLateUpdate sets the pose and calls Camera.Render()
    ///   (exposed: UnityEngineCamera.__Render__SystemVoid) - exactly one render per tick. W1's first harness run showed that "enable for one frame"
    ///   (the prequel's KD_DogPovCamera pattern) drew ~1.6 renders per enabled frame in the Editor (49 real / 31 enabled at 90 fps): an enabled
    ///   camera is also drawn by renders outside the player loop (Editor Game-view repaints), so its count depends on who else renders.
    ///   PostLateUpdate runs in LateUpdate (PostLateUpdater, execution order 31000) after IK: the pose is this frame's; skinned meshes show the
    ///   previous frame's deformation (UpdateAllSkinnedMeshes runs after LateUpdate; one frame, 11 ms at 90 fps).
    ///   The pose is computed only on render ticks; the occlusion rays run at 1 / occlusionInterval.
    /// - The monitor stays where it was opened until × (user decision); [モニターを呼ぶ] places it in front of the head again.
    /// - RecordSnap: a quad with the unchanged KD_PhotoPrint shader (attach 100000 m, no aim check, hidden from eyes) and a mesh whose
    ///   bounds cover the world, so every VRChat camera pass (_VRChatCameraMode > 0.5: photo / video / stream AND screenshots) shows the
    ///   live RT full screen wherever the user walks (design §3.2-3.3). Its layer (KH_VideoCameraEditor.RecordSnapLayer, Water 4) is excluded
    ///   from this camera and the auto photo camera, so it does not depend on _VRChatCameraMode being 0 in our own pass. Its renderQueue
    ///   (5001) draws above the photo viewer / frames (4000-4101), the night overlay (4999) and the travel fade (5000).
    /// - PC only: on Android / iOS builds Start hides everything and disables this behaviour; the entry buttons start hidden in the scene
    ///   and only a PC client shows [撮影カメラ]. The scene is the same for every build target.
    /// - Other players: W1 never reads another player. The only future entry for another player is TryPersonPose (W4, design §5.5);
    ///   it does not exist yet. The local player is read only in KH_VideoCamera.Subjects.cs.
    ///
    /// Every public method is local-only (`_` prefix, vrc-networking). Panel texts are Japanese constants in W1 (13 languages in W5).
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public partial class KH_VideoCamera : UdonSharpBehaviour
    {
        // ---- modes (design §4.2 numbering; W1 implements the first two) ----
        public const int ModeDogFollow = 0, ModeSelfFollow = 1;
        public const int ModeCount = 2;
        // ---- state line ----
        public const int StateNone = -1, StateFilming = 0, StateLost = 1, StateFade = 2, StateRtFailed = 3, StateUnsupported = 4;

        // ---- Japanese labels (W1. The builder writes the static ones and bakes the glyphs of all of them; W5 moves them to UITranslations.json) ----
        public const string WordTitle = "撮影カメラ";
        public const string WordRecall = "モニターを呼ぶ";
        public const string WordPutAway = "しまう";
        public const string WordModeDog = "犬追従";
        public const string WordModeSelf = "じぶん";
        public const string WordSnap = "VRChatカメラに映す";
        public const string WordOn = "ON", WordOff = "OFF";
        public const string WordFilming = "撮影中";
        public const string WordLost = "が見つかりません";
        public const string WordFade = "移動中…";
        public const string WordRtFailed = "撮影カメラを用意できませんでした";
        // _VRChatCameraMode > 0.5 is every VRChat camera pass (hand-held photo / video / stream and screenshots), not only the hand-held camera
        public const string WordSnapNote = "ONの間は、VRChatカメラの写真・動画・配信とスクリーンショットがこの映像になります。ふつうに撮るときはOFFに";
        public const string WordPhotoCamOn = "VRChatカメラ: 映しています";
        public const string WordPhotoCamOff = "VRChatカメラ: 出ていません";

        [Header("配線 (Video/1 が書く)")]
        public Camera cam;
        public GameObject monitorRoot;
        public Renderer screenRenderer;
        [Tooltip("録画モードの吸着板 (KD_PhotoPrint、bounds を広げた Quad)。ON の間だけ有効")]
        public Renderer snapRenderer;
        [Tooltip("写真の面の入口 [撮影カメラ] / [モニターを呼ぶ] / [しまう]。シーンでは隠しておき、PC の Start だけが [撮影カメラ] を出す")]
        public GameObject entryOpen, entryRecall, entryClose;
        public TMP_Text stateLabel, snapLabel, dogLabel;
        public Image modeDogImage, modeSelfImage, snapImage;
        public GameObject dogPicker;
        public KD_Language language;
        public KH_LeashManager manager;
        public KH_TravelFade fade;
        [Tooltip("犬 (KD_DogDefs.All の順 = dogIndex)。参照配列なので既定値は持たない (vrc-udonsharp §4)")]
        public Transform[] dogRoots;
        public Transform[] dogHeads;
        public string[] dogNameKeys;

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
        [Header("じぶん (§4.2 #2、自分だけ)")]
        public float selfFov = 55f, selfSideDeg = 35f, selfDistance = 2.4f, selfLift = -0.1f, selfLookToDogs = 0.4f, selfChestDrop = 0.35f, selfDogMax = 8f;

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
        private RenderTexture _rt;
        private float _nextRender, _lastRenderAt, _interval;
        private int _renders, _cuts, _opens, _closes;
        // valid poses obtained for the CURRENT subject in the CURRENT session (reset on open, mode change and dog change).
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
        // the wanted pose (the selected mode writes it)
        private Vector3 _wantPos, _wantLook, _subject;
        private float _wantFov = 50f, _subjectSize = 0.6f;
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
            cam.enabled = false;
            _open = true; _opens++;
            NewSubject();   // the picture stays detached (black) until this session has a valid pose
            _snapOn = snapOnOpen;
            if (_dog < 0 || dogRoots == null || _dog >= dogRoots.Length) _dog = 0;
            OwnDogFirst();   // start on the player's own leashed dog; keeps the last chosen dog when the player has none
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
            _open = false; _closes++;
            if (cam != null) cam.enabled = false;
            if (snapRenderer != null) snapRenderer.gameObject.SetActive(false);
            if (monitorRoot != null) monitorRoot.SetActive(false);
            ReleaseRt();   // the screen and the RecordSnap materials lose the texture before the RT is destroyed (no old picture stays visible)
            // forget every cached pose: the next open starts with a cut, never with a pose or heading from this session
            _cutNext = true; _lostSince = -1f; _subjectPoses = 0; ResetHeading();
            _camPos = Vector3.zero; _look = Vector3.zero; _wantPos = Vector3.zero; _wantLook = Vector3.zero; _subject = Vector3.zero;
            _clipDist = 1e6f; _groundValid = false; _lastRenderAt = -1f;
            _state = StateNone;
            enabled = false;
            DrawEntry();
        }

        public void _ModeDog() { SetMode(ModeDogFollow); }
        public void _ModeSelf() { SetMode(ModeSelfFollow); }
        public void _DogPrev() { StepDog(-1); }
        public void _DogNext() { StepDog(1); }

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
            float now = Time.time;
            if (now >= _nextPhotoCamCheck) { _nextPhotoCamCheck = now + 0.5f; ReadPhotoCamera(); }
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
            bool ok = ModeTarget(dt);
            if (!ok)
            {
                if (_lostSince < 0f) _lostSince = now;
                SetState(StateLost);
                // a lost DOG keeps this session's last framing for this subject a little; then the picture is detached (black).
                // Without a valid pose for this subject in this session nothing is drawn at all. W4: a lost / revoked PERMISSION must not
                // take this path (stop at once, drop the cached pose, no hold; design §13)
                if (_subjectPoses > 0 && now - _lostSince < lostHoldSeconds) RenderOnce();
                else ShowPicture(false);
                return;
            }
            if (_lostSince >= 0f) { _lostSince = -1f; _cutNext = true; }
            SetState(StateFilming);
            ApplyPose(dt, now);
            RenderOnce(); _subjectPoses++;
            ShowPicture(true);   // attached right after the first render of this subject
        }

        /// <summary>Exactly one render into the RT (the camera component stays disabled). _renders counts these ticks.</summary>
        void RenderOnce()
        {
            if (cam.enabled) cam.enabled = false;   // never left enabled: an enabled camera is drawn again by any other render path
            cam.Render();
            _renders++;
        }

        // ================================================================ shared post-processing (design §4.1)

        /// <summary>Occlusion / ground / dog push-out, then the jump cut or the smoothing, roll locked to world up.</summary>
        void ApplyPose(float dt, float now)
        {
            Vector3 pos = _wantPos;
            if (now >= _nextOcclusion) { _nextOcclusion = now + occlusionInterval; Occlusion(pos); }
            Vector3 d = pos - _subject;
            float len = d.magnitude;
            if (len > _clipDist && len > 0.01f) pos = _subject + d * (_clipDist / len);
            pos = PushOutOfDogs(pos);
            if (_groundValid && pos.y < _groundY + groundLift) pos.y = _groundY + groundLift;

            bool cut = _cutNext || (pos - _camPos).magnitude > cutDistance;
            float tau = now < _turnUntil ? turnPosTau : posTau;
            _camPos = SmoothStep(_camPos, pos, cut, dt, tau);
            _look = SmoothStep(_look, _wantLook, cut, dt, lookTau);
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
            NewSubject();
            DrawPanel();
        }

        /// <summary>A new subject (open, mode change, dog change): forget every cached pose and the hold, detach the picture until a valid pose.</summary>
        void NewSubject()
        {
            _cutNext = true; _lostSince = -1f; _subjectPoses = 0;
            ResetHeading();
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

        void StepDog(int step)
        {
            if (!_open || dogRoots == null || dogRoots.Length == 0) return;
            int n = dogRoots.Length;
            _dog = ((_dog + step) % n + n) % n;
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
            if (_state == StateLost) line = (_mode == ModeDogFollow ? DogName(_dog) : WordModeSelf) + WordLost;   // the "no subject" state (black picture)
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

        void DrawEntry()
        {
            bool show = _supported && _started;
            if (entryOpen != null) entryOpen.SetActive(show && !_open);
            if (entryRecall != null) entryRecall.SetActive(show && _open);
            if (entryClose != null) entryClose.SetActive(show && _open);
        }

        void DrawPanel()
        {
            if (modeDogImage != null) modeDogImage.color = _mode == ModeDogFollow ? modeOnColor : modeOffColor;
            if (modeSelfImage != null) modeSelfImage.color = _mode == ModeSelfFollow ? modeOnColor : modeOffColor;
            if (dogPicker != null) dogPicker.SetActive(_mode == ModeDogFollow);
            if (dogLabel != null) dogLabel.text = DogName(_dog);
            if (snapLabel != null) snapLabel.text = WordSnap + ": " + (_snapOn ? WordOn : WordOff);
            if (snapImage != null) snapImage.color = _snapOn ? modeOnColor : modeOffColor;
            _shownState = -99;
            DrawState();
        }

        void OnDisable()
        {
            // the behaviour is disabled on purpose while closed; a disabled camera never renders. Nothing else to undo here
            if (cam != null) cam.enabled = false;
        }
    }
}
