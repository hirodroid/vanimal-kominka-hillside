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
    /// only the selected mode's pose function runs (one switch in KH_VideoCamera.Modes.cs; the walk modes in .ModesWalk.cs, the play modes in
    /// .ModesPlay.cs, 背中ごし / みち in .ModesPath.cs). The RT exists only while the monitor is open: created in _Open, released and destroyed in _Close.
    /// §17 (2026-10-08): the pattern player and its board (KH_VideoCamera.Patterns.cs) pick the cut through the same shot entry (ShotApply);
    /// still one cut, one camera, one RT.
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
    ///   Poses are read only in KH_VideoCamera.Subjects.cs. Where another mode would need another person (a thrower who is not me) it still
    ///   falls back to a framing of the dog alone. 背中ごし and みち always film me (never another person).
    ///
    /// Every public method is local-only (`_` prefix, vrc-networking). Panel texts (W5, 2026-10-08): every label is read through Tx(key, ja) = KD_Language._Text (13 languages in UITranslations.json,
    /// video.* keys); the Word* constants are only the Japanese default when the language table is missing. The builder's static labels
    /// (textLabels / textKeys) are rewritten by TextsRefresh on open / [モニターを呼ぶ], when the language changes (2 Hz check while open) and
    /// when the photo page opens (KD_TreatPouch._PhotoOpen → _RefreshLanguage).
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public partial class KH_VideoCamera : UdonSharpBehaviour
    {
        // ---- modes (design §4.2 / §16; the panel order) ----
        // §15.5 (2026-10-04 user): おかえり and ふれあい removed, 12 → 10. §16 (IMP-WLD-012, 2026-10-08): 手元 and 見下ろす removed, 背中ごし (TPS)
        // and みち (moves along the walking direction) added, still 10. Renumbered without gaps: the numbers live only in code and in the panel's
        // button names / events (Mode0..ModeN-1 → _Mode0..), which Video/1 rewrites
        public const int ModeDogFollow = 0, ModeSelfFollow = 1, ModeTps = 2, ModeDogEye = 3, ModeSpot = 4, ModeDisc = 5,
            ModeCatchWait = 6, ModeOrbit = 7, ModePassBy = 8, ModePath = 9;
        public const int ModeCount = 10;   // the panel's buttons. §17: + the internal おかえり (ModeWelcome = 10, ModeCountAll = 11, KH_VideoCamera.Patterns.cs)
        // ---- the shot signal for a later sequencer (the user's "patterns", 2026-10-04): ShotSerial goes up on each event, ShotKind says which.
        // ShotReturned was raised by おかえり (removed in §15.5); kept as a value so a later sequencer's numbers do not move.
        // §16: ShotMoveDone = one みち move finished (back to its cut), ShotOrbitEnd = 一周 reached the end radius of 近づく / 離れる ----
        public const int ShotNone = 0, ShotLost = 1, ShotCatch = 2, ShotReturned = 3, ShotPassed = 4, ShotMoveDone = 5, ShotOrbitEnd = 6;
        // ---- §16 the pickers of みち (the move) and 一周 (how it circles) ----
        public const int PathWaitApproach = 0, PathBackAway = 1, PathBeside = 2, PathOvertake = 3, PathPullAway = 4, PathMoveCount = 5;
        public const int OrbitSteady = 0, OrbitCloser = 1, OrbitFarther = 2, OrbitKindCount = 3;
        // ---- §16 which controls a mode takes (CtlKind): none / the shared offset (distance, height, around) / the mode's own distance and height ----
        public const int CtlKindNone = 0, CtlKindAll = 1, CtlKindDistUp = 2;
        // ---- state line ----
        public const int StateNone = -1, StateFilming = 0, StateLost = 1, StateFade = 2, StateRtFailed = 3, StateUnsupported = 4;

        // ---- Japanese defaults (W5: the displayed text comes from UITranslations.json through Tx(key, ja); these are used only when the
        // language table has no such key, and Video/2 checks that they equal the table's ja) ----
        public const string WordTitle = "撮影カメラ";
        public const string WordRecall = "モニターを呼ぶ";
        public const string WordPutAway = "しまう";
        public const string WordModeDog = "犬追従";
        public const string WordModeSelf = "じぶん";
        public const string WordModeTps = "背中ごし";   // §16: TPS, behind my back
        public const string WordModeDogEye = "犬目線";
        public const string WordModeSpot = "定点";
        public const string WordModeDisc = "ディスク";
        public const string WordModeCatchWait = "キャッチ待ち";   // 2026-10-04 user: wait at the CATCH point, not the landing point
        public const string WordModeOrbit = "一周";
        public const string WordModePassBy = "通り過ぎ";
        public const string WordModePath = "みち";   // §16: moves along the walking direction (Vlog)
        public const string WordPathWaitApproach = "待って寄る", WordPathBackAway = "後ずさり", WordPathBeside = "横ならび", WordPathOvertake = "追いこし", WordPathPullAway = "寄って引く";
        public const string WordOrbitSteady = "一定", WordOrbitCloser = "近づく", WordOrbitFarther = "離れる";
        public const string WordSnap = "VRChatカメラに映す";
        public const string WordOn = "ON", WordOff = "OFF";
        public const string WordFilming = "撮影中";
        public const string WordLost = "{0}が見つかりません";   // W5: a format ({0} = the dog / mode)
        public const string WordNoSpot = "定点がありません";
        public const string WordFade = "移動中…";
        public const string WordRtFailed = "撮影カメラを用意できませんでした";
        // _VRChatCameraMode > 0.5 is every VRChat camera pass (hand-held photo / video / stream and screenshots), not only the hand-held camera
        public const string WordSnapNote = "ONの間は、VRChatカメラの写真・動画・配信とスクリーンショットがこの映像になります。ふつうに撮るときはOFFに";
        public const string WordPhotoCamOn = "VRChatカメラ: 映しています";
        public const string WordPhotoCamOff = "VRChatカメラ: 出ていません";
        // ---- W5: the UITranslations.json keys of the texts above (13 languages; design §7.3 "2026-10-08 W5 実装メモ") ----
        public const string KeyTitle = "video.open", KeyRecall = "video.recall", KeyPutAway = "video.close";
        public const string KeySnap = "video.snap", KeyOn = "video.on", KeyOff = "video.off", KeyFilming = "video.filming", KeyLost = "video.lost";
        public const string KeyNoSpot = "video.nospot", KeyFade = "video.fade", KeyRtFailed = "video.rtfailed", KeySnapNote = "video.snap.note";
        public const string KeyPhotoCamOn = "video.photocam.on", KeyPhotoCamOff = "video.photocam.off";
        public const string KeySpotWash = "video.spot.wash", KeySpotRiver = "video.spot.river", KeySpotRiverLow = "video.spot.riverlow";

        /// <summary>The panel label of a mode (the builder writes the buttons from this; the probe and the harness print it).</summary>
        public static string ModeWord(int m)
        {
            switch (m)
            {
                case ModeDogFollow: return WordModeDog;
                case ModeSelfFollow: return WordModeSelf;
                case ModeTps: return WordModeTps;
                case ModeDogEye: return WordModeDogEye;
                case ModeSpot: return WordModeSpot;
                case ModeDisc: return WordModeDisc;
                case ModeCatchWait: return WordModeCatchWait;
                case ModeOrbit: return WordModeOrbit;
                case ModePassBy: return WordModePassBy;
                case ModePath: return WordModePath;
                case ModeWelcome: return WordModeWelcome;   // §17: internal, never a button
            }
            return "?";
        }

        /// <summary>§16: the picker label of a みち move (PathWaitApproach..PathPullAway).</summary>
        public static string PathMoveWord(int k)
        {
            switch (k)
            {
                case PathWaitApproach: return WordPathWaitApproach;
                case PathBackAway: return WordPathBackAway;
                case PathBeside: return WordPathBeside;
                case PathOvertake: return WordPathOvertake;
                case PathPullAway: return WordPathPullAway;
            }
            return "?";
        }

        /// <summary>§16: the picker label of how 一周 circles (OrbitSteady / OrbitCloser / OrbitFarther).</summary>
        public static string OrbitKindWord(int k)
        {
            switch (k)
            {
                case OrbitSteady: return WordOrbitSteady;
                case OrbitCloser: return WordOrbitCloser;
                case OrbitFarther: return WordOrbitFarther;
            }
            return "?";
        }

        /// <summary>W5: the translation key of a mode's label (ModeWord is its Japanese default).</summary>
        public static string ModeKey(int m)
        {
            switch (m)
            {
                case ModeDogFollow: return "video.mode.dog";
                case ModeSelfFollow: return "video.mode.self";
                case ModeTps: return "video.mode.tps";
                case ModeDogEye: return "video.mode.dogeye";
                case ModeSpot: return "video.mode.spot";
                case ModeDisc: return "video.mode.disc";
                case ModeCatchWait: return "video.mode.catch";
                case ModeOrbit: return "video.mode.orbit";
                case ModePassBy: return "video.mode.passby";
                case ModePath: return "video.mode.path";
                case ModeWelcome: return "video.mode.welcome";
            }
            return "";
        }

        /// <summary>W5: the translation key of a みち move (PathMoveWord is its Japanese default).</summary>
        public static string PathMoveKey(int k)
        {
            switch (k)
            {
                case PathWaitApproach: return "video.path.wait";
                case PathBackAway: return "video.path.back";
                case PathBeside: return "video.path.beside";
                case PathOvertake: return "video.path.overtake";
                case PathPullAway: return "video.path.pull";
            }
            return "";
        }

        /// <summary>W5: the translation key of how 一周 circles (OrbitKindWord is its Japanese default).</summary>
        public static string OrbitKindKey(int k)
        {
            switch (k)
            {
                case OrbitSteady: return "video.orbit.steady";
                case OrbitCloser: return "video.orbit.closer";
                case OrbitFarther: return "video.orbit.farther";
            }
            return "";
        }

        /// <summary>Which picker a mode shows: 1 = dog (the disc modes use the selected dog's disc), 2 = spot, 3 = the みち move, 4 = how 一周
        /// circles, 0 = none.</summary>
        public static int PickerKind(int m)
        {
            if (m == ModeSpot) return 2;
            if (m == ModePath) return 3;
            if (m == ModeOrbit) return 4;
            if (m == ModeDogFollow || m == ModeDogEye || m == ModeDisc || m == ModeCatchWait || m == ModeWelcome) return 1;
            return 0;
        }

        /// <summary>§15.5 / §16: which of [近く][遠く][↑][↓][←][→] a mode takes. CtlKindAll: the camera is "direction / distance / height from
        /// the subject" and ApplyControls moves that offset (犬追従・じぶん / 人・背中ごし・犬目線). CtlKindDistUp: the mode scales its own
        /// distances by [近く][遠く] and adds [↑][↓] to its own height (みち・一周; in 一周 [←][→] are the speed instead, CtlStep).
        /// CtlKindNone: 定点・通り過ぎ (data positions), ディスク・キャッチ待ち (their own placement rules). 縦回転 and [戻す] work in every mode.</summary>
        public static int CtlKind(int m)
        {
            if (m == ModeDogFollow || m == ModeSelfFollow || m == ModeTps || m == ModeDogEye) return CtlKindAll;
            if (m == ModePath || m == ModeOrbit) return CtlKindDistUp;
            return CtlKindNone;
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
        [Tooltip("犬ごと (dogIndex の順): フリスビー・ディスク・撫で (Fetch)・徘徊")]
        public KH_FrisbeeCatch[] catchers;
        public KH_Frisbee[] discs;
        public KD_DogFetchController[] fetches;
        public KD_DogWanderController[] wanders;
        [Tooltip("洗い場の呼び出し (全部)")]
        public KH_WashStationCall[] washCalls;
        public KH_WaterSpot waterSpot;
        [Tooltip("定点 (データだけ。Video/1 が洗い場と水遊び場から作り、Video/2 が地面と見通しを照合する)")]
        public string[] spotNames;
        public Vector3[] spotPositions;
        public Vector3[] spotLooks;
        public float[] spotFovs;
        public int[] spotStations;   // index into washCalls (-1 = the river)
        [Tooltip("W5: 定点の名前の翻訳キー (spotNames と同じ並び。洗い場は {0} に番号 = spotStations + 1)。Video/1 が書く。無ければ spotNames (ja) を出す")]
        public string[] spotKeys;
        [Header("W5: 板の固定の文字 (Video/1・Video/4 が配線する。開く時・言語が変わった時に textKeys の文で書き直す)")]
        public TextMeshProUGUI[] textLabels;   // the array type the menu already uses in Udon (like personCellLabels)
        public string[] textKeys;
        [Tooltip("任意: ショットの出来事 (見失った・キャッチ・戻った・通り過ぎた・みちの 1 回の動き・一周の端) を _OnVideoShot で受け取る Behaviour (後で作るパターンの再生役)。null = 送らない")]
        public UdonSharpBehaviour shotListener;
        [Tooltip("通り過ぎる散歩の撮影地点 (データだけ。写真の場所から Video/1 が道の脇へずらして作る)")]
        public Vector3[] passPositions;
        [Tooltip("§16 みち: 散歩道 (KH_LeashManager.route と同じもの。Video/1 が書く)。null なら自分の進む向きだけを使う")]
        public KH_WalkRoute route;

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
        [Header("背中ごし (§16、自分だけ。頭の後ろ・肩の横から、自分の見ている方を写す)")]
        [Tooltip("tpsYawTau: 体の向き (GetRotation の水平の向き) をならす時定数 (s)。急に振り向いても画が振り回されない")]
        public float tpsFov = 60f, tpsBack = 2.2f, tpsShoulder = 0.35f, tpsLift = 0.3f, tpsLookAhead = 4f, tpsLookDrop = -0.2f, tpsYawTau = 0.8f;
        [Header("みち (§16、自分だけ。進行方向に沿った 5 つの動き)")]
        [Tooltip("足元が散歩道からこれ以内なら、道の向き (自分の進む向きに揃えた) を進行方向にする (m)")]
        public float pathTrailMax = 4f;
        public float pathHeight = 1.3f, pathFov = 55f, pathAheadFar = 12f, pathApproachAt = 7f, pathAheadNear = 2.5f, pathGlideTau = 0.8f, pathHold = 4f;
        public float pathBeside = 2.8f, pathBehind = 3f, pathPassSide = 1.5f, pathPassSpeed = 1.5f, pathRecedeSpeed = 0.8f;
        [Header("定点 (§4.5)")]
        public float spotLookTau = 0.35f, spotDogPull = 0.3f;
        [Header("ディスク追従 (§4.6)")]
        public float discFov = 60f, discSideK = 1f, discSideMin = 3f, discSideMax = 12f, discUp = 0.8f, discTau = 0.3f, discLookTau = 0.1f, discFlySpeed = 2f, discApartMax = 15f, discVelTau = 0.3f;
        [Header("落下地点の待機 (§4.7)")]
        [Tooltip("キャッチ待ち: 犬が投げた時点で決めたキャッチの予定 (犬の Owner の端末だけが持つ) の横、キャッチの高さ。無いときは着地の予測へ")]
        public float catchFov = 55f, catchSide = 6f, catchUp = 0.3f, catchLookBack = 2f, catchPlanWait = 0.6f, catchReplanMove = 1f;
        public float landFov = 55f, landSide = 5f, landBeyond = 3f, landUp = 1.2f, landNear = 8f, landMoveMax = 5f, landStep = 0.08f, landMaxT = 8f, landRemoteSample = 0.25f, landRepredict = 0.5f;
        [Header("ゆっくり一周 (§4.9、§16 の回り方と速さ)")]
        public float orbitFov = 50f, orbitRadius = 3.5f, orbitHeight = 1.4f, orbitDegPerSec = 6f, orbitCenterTau = 2f, orbitLookUp = 0.6f, orbitTau = 0.3f;
        [Tooltip("近づく: 1 周で半径 × orbitNearK (下限 orbitRadiusMin) へ。離れる: × orbitFarK (上限 orbitRadiusMax) へ。[←][→] で速さを ÷ / × orbitSpeedStep (orbitDegMin〜orbitDegMax 度/秒)")]
        public float orbitNearK = 0.4f, orbitFarK = 2.5f, orbitRadiusMin = 1.5f, orbitRadiusMax = 10f, orbitSpeedStep = 1.5f, orbitDegMin = 2f, orbitDegMax = 24f;
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
        private bool _monitorHidden;   // DES-WLD-004 M1.1: open, but the board is hidden (_HideMonitor)
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
        private int _lostWord;     // 0 = "<dog / mode> が見つかりません", 2 = no spot
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

        /// <summary>W5: the text of key in the player's language (UITranslations.json through KD_Language); ja when there is no language
        /// object or the table has no such key yet (KD_Language._Text returns the key itself then).</summary>
        string Tx(string key, string ja)
        {
            if (language == null || key == null || key.Length == 0) return ja;
            string s = language._Text(key);
            return s == null || s.Length == 0 || s == key ? ja : s;
        }

        private string _textLang;   // W5: the language TextsRefresh last wrote (null = never)

        /// <summary>W5: rewrites the builder's static labels (textLabels / textKeys: the title, the mode / control / pattern buttons, the pattern
        /// notes, the entry buttons on the photo page, …) in the player's language and makes the next DrawPanel rebuild every line. A label
        /// whose key is not in the table keeps its saved (Japanese) text. Called on Start, open, [モニターを呼ぶ], a language change (the 2 Hz
        /// check while open) and _RefreshLanguage (the photo page opening). Never per frame.</summary>
        void TextsRefresh()
        {
            int n = textLabels != null && textKeys != null ? Mathf.Min(textLabels.Length, textKeys.Length) : 0;
            for (int i = 0; i < n; i++)
            {
                if (textLabels[i] == null || textKeys[i] == null || textKeys[i].Length == 0) continue;
                string s = language != null ? language._Text(textKeys[i]) : textKeys[i];
                if (s != null && s.Length > 0 && s != textKeys[i] && textLabels[i].text != s) textLabels[i].text = s;
            }
            _textLang = language != null ? language.CurrentLanguage : "";   // after _Text (it initialises the language object)
            _patShownKey = -999;   // the pattern line is rebuilt in the new language
            _shownState = -99;
        }

        bool LanguageChanged() { return (language != null ? language.CurrentLanguage : "") != _textLang; }

        /// <summary>W5: KD_TreatPouch._PhotoOpen sends this (the entry buttons sit on the photo page and this behaviour is disabled while
        /// closed). Local only.</summary>
        public void _RefreshLanguage()
        {
            TextsRefresh();
            if (_open) DrawPanel();
        }

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
            TextsRefresh();   // W5: the entry buttons in the player's language
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
            CtlEnsure(); CtlResetAll(); _droneShow = droneShowDefault; _droneSending = false; _droneNextSend = 0f;   // §15.5: controls and the drone start from the defaults
            KH_TravelMailbox myBox = PersonMyBox();
            _filmSeenSerial = myBox != null ? myBox.filmResultSerial : -1;
            NewSubject();   // the picture stays detached (black) until this session has a valid pose
            _snapOn = snapOnOpen;
            if (_dog < 0 || dogRoots == null || _dog >= dogRoots.Length) _dog = 0;
            OwnDogFirst();   // start on the player's own leashed dog; keeps the last chosen dog when the player has none
            AutoSubject();
            PlaceMonitor();
            _monitorHidden = false;
            monitorRoot.SetActive(true);
            snapRenderer.gameObject.SetActive(_snapOn);
            _interval = renderFps > 1f ? 1f / renderFps : 1f / 30f;
            _nextRender = Time.time; _lastRenderAt = -1f;
            _state = StateFilming; _shownState = -99;
            _nextPhotoCamCheck = 0f;
            enabled = true;
            TextsRefresh();   // W5: the panel in the player's language (it may have changed while closed)
            DrawEntry();
            DrawPanel();
        }

        /// <summary>[モニターを呼ぶ]: the open monitor moves in front of the head (it never follows by itself). A hidden monitor is shown again.</summary>
        public void _Recall()
        {
            if (!_open) return;
            if (_monitorHidden) { _monitorHidden = false; monitorRoot.SetActive(true); }
            PlaceMonitor();
            TextsRefresh(); DrawPanel();   // W5: the panel in the player's language
        }

        /// <summary>DES-WLD-004 M1.1 (2026-10-12): [モニターを隠す]. Only the monitor board goes away (monitorRoot inactive: the screen, the panels and
        /// their buttons); filming keeps running. This behaviour lives on the system root and RecordSnap is a sibling of the monitor, so the
        /// render tick (PostLateUpdate), RecordSnap, the drone sends and the pattern player are untouched. × (_Close) still stops everything.</summary>
        public void _HideMonitor()
        {
            if (!_open || _monitorHidden) return;
            _monitorHidden = true;
            monitorRoot.SetActive(false);
        }

        /// <summary>Shows a hidden monitor again in front of the head (same as _Recall).</summary>
        public void _ShowMonitor() { _Recall(); }

        /// <summary>Open and hidden (filming continues without the board).</summary>
        public bool _MonitorHidden() { return _open && _monitorHidden; }

        /// <summary>× on the monitor and [しまう] on the photo page. Stops the camera, hides everything and destroys the RT.</summary>
        public void _Close()
        {
            _monitorHidden = false;
            if (!_open && _rt == null) return;
            PersonCloseAll();   // W4: × ends the film session (pending / in use) before anything else; a reopen needs a new request (§5.4)
            if (_patRun) PatternStop();   // §17: × stops the pattern (a reopen starts manual)
            DroneOff();   // §15.5: the drone leaves my screen and nothing more is sent
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
        // W1 names (the harness V1-V3 uses them)
        public void _ModeDog() { SetMode(ModeDogFollow); }
        public void _ModeSelf() { SetMode(ModeSelfFollow); }
        /// <summary>The single entry a sequencer uses: mode + subject (dog / spot; a negative value keeps the current one). Always starts a new
        /// subject (cut, no hold from the previous shot) even when the mode stays. Local only; called directly by another local behaviour.
        /// §17: takes the internal おかえり too (mode < ModeCountAll); the pattern player (KH_VideoCamera.Patterns.cs) uses the same body (ShotApply).
        /// It does not stop a running pattern (it IS the sequencer's entry).</summary>
        public void _SetShot(int mode, int dog, int spot) { ShotApply(mode, dog, spot); }

        void ShotApply(int mode, int dog, int spot)
        {
            if (!_open || mode < 0 || mode >= ModeCountAll) return;
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
            PatternOnShot(kind);   // §17: only latches; the pattern steps on the next frame
        }

        /// <summary>◀ / ▶ of the picker: the dog, the spot in 定点, the move in みち, how 一周 circles.</summary>
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
            if (now >= _nextPhotoCamCheck)
            {
                _nextPhotoCamCheck = now + 0.5f;
                if (LanguageChanged()) { TextsRefresh(); DrawPanel(); }   // W5: the language changed from the hand menu while open
                ReadPhotoCamera(); PersonTick(now);
            }
            if (_patRun) PatternFrame(now);   // §17: the pattern's 0.5 s step / the step after a shot event / a throw (before this frame's render tick)
            // W4 (Codex review 1): while another person's picture is up, check the permission EVERY frame, before the wait for the next render
            // tick and before the fade's return, and detach the picture at once when it is gone (one field test when no person is shown)
            if (_personLive && !PersonStillUsable()) PersonDrop();
            DroneSendCheck();   // §15.5 (Codex review 1): the drone's pose stops going out the frame the gate fails, also between ticks / in the fade
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
            DroneTick(now);   // §15.5: the drone at this tick's camera pose (and the 5 Hz send while filming another person)
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
            ct.rotation = ApplyTilt(LookRoll0(_camPos, _look), dt, cut);   // roll 0, then the user's tilt (§15.5 縦回転)
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
            if (_patRun) PatternStop();   // §17: a mode button stops the pattern (also the lit one: it becomes the manual choice)
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
            if (_patRun) PatternStop();   // §17: ◀ ▶ stop the pattern too
            int pk = PickerKind(_mode);
            if (pk == 2)
            {
                int ns = spotPositions != null ? spotPositions.Length : 0;
                if (ns == 0) return;
                _spot = ((_spot + step) % ns + ns) % ns;
            }
            else if (pk == 3) _pathMove = ((_pathMove + step) % PathMoveCount + PathMoveCount) % PathMoveCount;   // §16: a new move starts from its cut
            else if (pk == 4) _orbitKind = ((_orbitKind + step) % OrbitKindCount + OrbitKindCount) % OrbitKindCount;   // §16: radius and angle start again
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
                if (_lostWord == 2) line = Tx(KeyNoSpot, WordNoSpot);
                else line = Tx(KeyLost, WordLost).Replace("{0}", PickerKind(_mode) == 1 ? DogName(_dog) : ModeText(_mode));
            }
            else if (_state == StateFade) line = Tx(KeyFade, WordFade);
            else if (_state == StateRtFailed) line = Tx(KeyRtFailed, WordRtFailed);
            else line = Tx(KeyFilming, WordFilming);
            stateLabel.text = line + "   " + (_photoCamActive ? Tx(KeyPhotoCamOn, WordPhotoCamOn) : Tx(KeyPhotoCamOff, WordPhotoCamOff));
        }

        string DogName(int i)
        {
            if (dogNameKeys != null && i >= 0 && i < dogNameKeys.Length && dogNameKeys[i] != null && dogNameKeys[i].Length > 0) return L(dogNameKeys[i]);
            return "#" + (i + 1).ToString();
        }

        /// <summary>W5: a mode / みち move / 一周 way in the player's language (the static *Word is the Japanese default).</summary>
        string ModeText(int m) { return Tx(ModeKey(m), ModeWord(m)); }
        string PathMoveText(int k) { return Tx(PathMoveKey(k), PathMoveWord(k)); }
        string OrbitKindText(int k) { return Tx(OrbitKindKey(k), OrbitKindWord(k)); }

        string SpotName(int i)
        {
            string ja = spotNames != null && i >= 0 && i < spotNames.Length && spotNames[i] != null ? spotNames[i] : "-";
            // W5: the key (洗い場 {0} = its station + 1); the saved Japanese name when there is no key
            if (spotKeys == null || i < 0 || i >= spotKeys.Length || spotKeys[i] == null || spotKeys[i].Length == 0 || language == null) return ja;
            string s = language._Text(spotKeys[i]);
            if (s == null || s.Length == 0 || s == spotKeys[i]) return ja;
            int st = spotStations != null && i < spotStations.Length ? spotStations[i] : -1;
            return s.Replace("{0}", st >= 0 ? (st + 1).ToString() : "");
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
            if (dogLabel != null) dogLabel.text = pk == 2 ? SpotName(_spot) : pk == 3 ? PathMoveText(_pathMove) : pk == 4 ? OrbitKindText(_orbitKind) : DogName(_dog);
            if (snapLabel != null) snapLabel.text = Tx(KeySnap, WordSnap) + ": " + (_snapOn ? Tx(KeyOn, WordOn) : Tx(KeyOff, WordOff));
            if (snapImage != null) snapImage.color = _snapOn ? modeOnColor : modeOffColor;
            DrawPerson();   // W4: the person picker (じぶん mode), the list and the confirmation
            DrawControls();   // §15.5: the control row (dimmed where they do not apply) and the drone toggle
            DrawPattern();   // §17: the pattern board (text rebuilt only when the cut changed)
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
