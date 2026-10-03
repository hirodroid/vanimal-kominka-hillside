using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace KominkaDogRun
{
    /// <summary>Local capture preference. Resize runtime RTs while preserving photographs and authoring assets.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_PhotoSettings : UdonSharpBehaviour
    {
        public const string SaveKey = "KD_PhotoSettings_v1";
        public KD_PhotoCapture capture;
        public RenderTexture[] slots;
        public GameObject[] boards;
        public KD_GalleryController[] galleries;
        public KD_PhotoViewer viewer;
        public KD_Language language;
        public TMP_Text stateLabel;
        public Button highButton, standardButton, offButton;
        // Each board contributes High, Standard, OFF in this order; all share one preference.
        public Button[] boardButtons;
        public TMP_Text[] boardStateLabels;
        // ---- 裏山改変 (IMP-WLD-005、小さい版。docs/plans/2026-10-03-auto-photo-gallery.md §4.3) ----
        // 2K の段階を外す (false で HighAvailable() が常に false = 選べず、保存値 2 も標準へ落ちる)。古民家は true のまま
        [Tooltip("2K (2560x1440) を出すか。裏山は false (Photo/1 が書く)")]
        public bool highEnabled = true;
        // 標準の解像度。Android / iOS ビルド (Quest を含む) だけ mobileWidth / mobileHeight を使う (0 なら PC と同じ)。
        // 実行時に端末ごとの RT を 1 組だけ作る (KD_PhotoSettings の既存の方式。2 組をビルドへ入れない)
        public int standardWidth = 1280, standardHeight = 720;
        public int mobileWidth = 0, mobileHeight = 0;
        // 状態が変わるたび (復元・切替・作り直しの完了・失敗・言語) に _OnPhotoStateChanged を送る先 (裏山: KH_PhotoAlbum)。null なら送らない
        [Tooltip("状態の変化を知らせる先 (裏山: KH_PhotoAlbum)。Photo/1 が配線")]
        public UdonSharpBehaviour stateListener;
        private bool _restored, _busy, _failed;
        private int _mode = 2; // 0=OFF, 1=720p, 2=1440p. Stable persistence values.
        private int _cursor, _width, _height;
        private RenderTexture _scratch;
        private RenderTexture[] _runtimeSlots;
        private RenderTexture _runtimeCapture;
        private float _nextRefresh;
        private string _lastLanguage;
        // net: callable-ok: read-only local capture gate
        public bool CaptureAllowed { get { return _restored && !_busy && !_failed && _mode != 0; } }
        // net: callable-ok: read-only platform capability
        public bool HighAvailable()
        {
            bool available = highEnabled;   // 裏山改変: 2K を外す
#if UNITY_ANDROID || UNITY_IOS
            available = false;
#endif
            return available;
        }
        // ---- 裏山改変 (IMP-WLD-005): 設定の行と写真の面が読む口 (読み取りのみ) ----
        // net: callable-ok: read-only local preference (0 = OFF, 1 = standard, 2 = 2K)
        public int CurrentMode { get { return _mode; } }
        // net: callable-ok: read-only (restored and not resizing)
        public bool Ready { get { return _restored && !_busy; } }
        // net: callable-ok: read-only
        public bool Failed { get { return _failed; } }
        private bool MobileBuild()
        {
            bool mobile = false;
#if UNITY_ANDROID || UNITY_IOS
            mobile = true;
#endif
            return mobile;
        }
        private void Start() { Refresh(); }
        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal || _restored) return;
            _restored = true;
            _mode = HighAvailable() ? 2 : 1;
            int saved;
            if (PlayerData.TryGetInt(player, SaveKey, out saved) && saved >= 0 && saved <= 2) _mode = saved;
            if (!HighAvailable() && _mode == 2) _mode = 1;
            BeginResize();
        }
        public void _High() { if (HighAvailable()) Select(2); }
        public void _Standard() { Select(1); }
        public void _Off() { Select(0); }
        private void Select(int mode)
        {
            if (!_restored || _busy) return;
            _mode = mode; _failed = false;
            PlayerData.SetInt(SaveKey, _mode);
            BeginResize();
        }
        private void BeginResize()
        {
            if (_mode == 0) { Refresh(); return; } // Existing photographs remain viewable.
            _width = _mode == 2 ? 2560 : 1280; _height = _mode == 2 ? 1440 : 720;
            // 裏山改変 (IMP-WLD-005): 標準は端末で分ける (PC 1280x720 / Quest・iOS 640x360)
            if (_mode == 1 && standardWidth > 0 && standardHeight > 0) { _width = standardWidth; _height = standardHeight; }
            if (_mode == 1 && MobileBuild() && mobileWidth > 0 && mobileHeight > 0) { _width = mobileWidth; _height = mobileHeight; }
            if (_runtimeSlots == null && slots != null) _runtimeSlots = new RenderTexture[slots.Length];
            _cursor = 0; _busy = true;
            Refresh(); SendCustomEventDelayedFrames("_ResizeNext", 1);
        }
        public void _ResizeNext()
        {
            if (!_busy) return;
            // 裏山改変 (IMP-WLD-005): boards は任意 (null / 空 = 板の材質は張り替えない。裏山は KH_PhotoAlbum が撮るたびに張り直す)。
            // **空配列も「無し」として扱う** — Unity はシーン保存で null の配列を空配列にして焼くので、null だけを見ると
            // 長さ 0 != 8 で Fail() し、撮影が永久に止まる (2026-10-03 ハーネス H0 で実測)
            bool hasBoards = boards != null && boards.Length > 0;
            if (capture == null || capture.photoCamera == null || capture.captureTexture == null || slots == null
                || (hasBoards && boards.Length != slots.Length) || galleries == null || viewer == null
                || viewer.slotTextures == null || viewer.slotTextures.Length != slots.Length) { Fail(); return; }
            // Let an already exposed frame finish before touching its source or destination.
            if (capture.photoCamera.enabled) { SendCustomEventDelayedFrames("_ResizeNext", 1); return; }
            if (_cursor < slots.Length)
            {
                RenderTexture rt = slots[_cursor];
                bool used = hasBoards && _cursor < boards.Length && boards[_cursor] != null && boards[_cursor].activeSelf;
                if (rt == null) { Fail(); return; }
                // Asset references survive Editor Play. Never resize those objects: all consumers
                // must share a runtime copy before the first capture, including initially hidden boards.
                if (_runtimeSlots[_cursor] == null)
                {
                    Renderer boardRenderer = hasBoards && boards[_cursor] != null ? boards[_cursor].GetComponent<Renderer>() : null;
                    if (boardRenderer == null && hasBoards) { Fail(); return; }
                    RenderTexture runtime = CreateRuntimeTexture(rt);
                    if (runtime == null) { Fail(); return; }
                    bool bound = false;
                    for (int g = 0; g < galleries.Length; g++)
                    {
                        if (galleries[g] == null || galleries[g].slotTextures == null) continue;
                        RenderTexture[] targets = galleries[g].slotTextures;
                        for (int i = 0; i < targets.Length; i++)
                            if (targets[i] == rt) { targets[i] = runtime; bound = true; }
                        galleries[g].slotTextures = targets;
                    }
                    if (!bound) { runtime.Release(); Destroy(runtime); Fail(); return; }
                    _runtimeSlots[_cursor] = runtime;
                    RenderTexture[] viewing = viewer.slotTextures;
                    viewing[_cursor] = runtime; viewer.slotTextures = viewing;
                    if (boardRenderer != null) boardRenderer.material.mainTexture = runtime;
                    slots[_cursor] = runtime;
                    // Loaded RT assets can already own GPU storage. Once every consumer is rebound,
                    // release that unused storage without changing any serialized template settings.
                    rt.Release();
                    rt = runtime;
                }
                if (rt.width != _width || rt.height != _height || !rt.IsCreated())
                {
                    if (used)
                    {
                        if (_scratch == null)
                        {
                            _scratch = new RenderTexture(_width, _height, 0, RenderTextureFormat.ARGB32);
                            _scratch.useMipMap = false; _scratch.filterMode = FilterMode.Bilinear;
                            if (!_scratch.Create()) { Fail(); return; }
                        }
                        VRCGraphics.Blit(rt, _scratch);
                    }
                    int previousWidth = rt.width, previousHeight = rt.height;
                    rt.Release(); rt.width = _width; rt.height = _height;
                    if (!rt.Create())
                    {
                        // Keep the photograph at its old size if the larger allocation fails.
                        rt.Release(); rt.width = previousWidth; rt.height = previousHeight;
                        if (rt.Create() && used) VRCGraphics.Blit(_scratch, rt);
                        Fail(); return;
                    }
                    if (used) VRCGraphics.Blit(_scratch, rt);
                }
                _cursor++;
                SendCustomEventDelayedFrames("_ResizeNext", 1);
                return;
            }
            RenderTexture source = capture.captureTexture;
            if (_runtimeCapture == null)
            {
                _runtimeCapture = CreateRuntimeTexture(source);
                if (_runtimeCapture == null) { Fail(); return; }
                capture.captureTexture = _runtimeCapture;
                capture.photoCamera.targetTexture = _runtimeCapture;
                source.Release();
                source = _runtimeCapture;
            }
            if (source.width != _width || source.height != _height || !source.IsCreated())
            {
                source.Release(); source.width = _width; source.height = _height;
                if (!source.Create()) { Fail(); return; }
            }
            Cleanup(); _busy = false; Refresh();
        }
        private void Fail() { Cleanup(); _busy = false; _failed = true; Refresh(); }
        private void Cleanup() { if (_scratch != null) { _scratch.Release(); Destroy(_scratch); _scratch = null; } }
        private RenderTexture CreateRuntimeTexture(RenderTexture template)
        {
            RenderTexture rt = new RenderTexture(_width, _height, template.depth, template.format,
                template.sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            rt.antiAliasing = template.antiAliasing;
            rt.useMipMap = template.useMipMap; rt.autoGenerateMips = template.autoGenerateMips;
            rt.filterMode = template.filterMode; rt.wrapMode = template.wrapMode;
            rt.anisoLevel = template.anisoLevel;
            if (rt.Create()) return rt;
            rt.Release(); Destroy(rt); return null;
        }
        private void OnDestroy()
        {
            Cleanup();
            if (_runtimeSlots != null)
                for (int i = 0; i < _runtimeSlots.Length; i++)
                    if (_runtimeSlots[i] != null) { _runtimeSlots[i].Release(); Destroy(_runtimeSlots[i]); }
            if (_runtimeCapture != null)
            {
                if (capture != null && capture.photoCamera != null && capture.photoCamera.targetTexture == _runtimeCapture)
                    capture.photoCamera.targetTexture = null;
                _runtimeCapture.Release(); Destroy(_runtimeCapture);
            }
        }
        private void Update()
        {
            if (Time.time < _nextRefresh) return;
            _nextRefresh = Time.time + .25f;
            string current = language != null ? language.CurrentLanguage : "";
            if (current != _lastLanguage) { _lastLanguage = current; Refresh(); }
        }
        private void Refresh()
        {
            bool ready = _restored && !_busy;
            if (highButton != null) { highButton.gameObject.SetActive(HighAvailable()); highButton.interactable = ready && (_failed || _mode != 2); }
            if (standardButton != null) standardButton.interactable = ready && (_failed || _mode != 1);
            if (offButton != null) offButton.interactable = ready && (_failed || _mode != 0);
            string key = !_restored ? "photo.settings.loading" : _busy ? "photo.settings.changing" : _failed ? "photo.settings.failed" : _mode == 0 ? "photo.settings.off" : _mode == 1 ? "photo.settings.standard" : "photo.settings.high";
            if (stateLabel != null) stateLabel.text = language != null ? language._Text(key) : key;
            if (boardStateLabels != null)
                for (int i = 0; i < boardStateLabels.Length; i++)
                    if (boardStateLabels[i] != null) boardStateLabels[i].text = language != null ? language._Text(key) : key;
            if (boardButtons != null)
                for (int i = 0; i < boardButtons.Length; i++)
                {
                    Button button = boardButtons[i];
                    if (button == null) continue;
                    int mode = i % 3 == 0 ? 2 : i % 3 == 1 ? 1 : 0;
                    bool visible = mode != 2 || HighAvailable();
                    button.gameObject.SetActive(visible);
                    // The visible plate is on a sibling UI canvas; the input Image stays transparent.
                    if (button.targetGraphic != null) button.targetGraphic.gameObject.SetActive(visible);
                    button.interactable = ready && (_failed || _mode != mode);
                }
            // 裏山改変 (IMP-WLD-005): 表示と撮影の可否は受け手 (KH_PhotoAlbum) が描き直す
            if (stateListener != null) stateListener.SendCustomEvent("_OnPhotoStateChanged");
        }
    }
}
