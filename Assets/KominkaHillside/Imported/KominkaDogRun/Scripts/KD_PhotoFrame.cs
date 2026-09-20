using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace KominkaDogRun
{
    /// <summary>Free, local photo decoration. Stable keys survive catalog reordering.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_PhotoFrame : UdonSharpBehaviour
    {
        public const string SaveKey = "KD_PhotoFrame_v1";
        public string[] frameKeys;
        public string[] nameKeys;
        public Texture2D[] landscapeTextures;
        public Texture2D[] portraitTextures;
        public Renderer cameraOverlay;
        public Renderer viewerOverlay;
        public RawImage preview;
        public TMP_Text selectionLabel;
        public TMP_Text toggleLabel;
        public TMP_Text statusLabel;
        public Button toggleButton;
        public RectTransform toggleThumb;
        public Button previousButton;
        public Button nextButton;
        public KD_Language language;
        Material _cameraMat, _viewerMat;
        bool _initialized, _restored, _enabled;
        int _index;
        string _lastLanguage;

        void Start() { Initialize(); Apply(); }
        void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            if (cameraOverlay != null) _cameraMat = cameraOverlay.material;
            if (viewerOverlay != null) _viewerMat = viewerOverlay.material;
        }
        // net: callable-ok: read-only catalog and readiness check
        public bool Ready()
        {
            int n = frameKeys == null ? 0 : frameKeys.Length;
            return _restored && n > 0 && nameKeys != null && nameKeys.Length == n && landscapeTextures != null && landscapeTextures.Length == n
                && portraitTextures != null && portraitTextures.Length == n;
        }
        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal || _restored) return;
            Initialize(); _restored = true;
            string saved;
            if (PlayerData.TryGetString(player, SaveKey, out saved)) Restore(saved);
            Apply();
        }
        void Restore(string saved)
        {
            _enabled = false; _index = 0;
            if (string.IsNullOrEmpty(saved) || saved.Length < 3 || saved.Substring(1, 1) != "|") return;
            string prefix = saved.Substring(0, 1);
            if (prefix != "0" && prefix != "1") return;
            string key = saved.Substring(2);
            for (int i = 0; frameKeys != null && i < frameKeys.Length; i++)
                if (frameKeys[i] == key) { _index = i; _enabled = prefix == "1"; return; }
            // Removed seasonal item: OFF + first valid item. Never pick a different enabled frame silently.
        }
        public void _Toggle() { if (!Ready()) return; _enabled = !_enabled; Save(); Apply(); }
        public void _Previous() { Select(-1); }
        public void _Next() { Select(1); }
        void Select(int step)
        {
            if (!Ready()) return;
            _index = (_index + step + frameKeys.Length) % frameKeys.Length;
            Save(); Apply(); // Selecting does not turn an OFF frame on.
        }
        void Save() { if (_restored) PlayerData.SetString(SaveKey, (_enabled ? "1|" : "0|") + frameKeys[_index]); }
        public void _RefreshLanguage() { Apply(); }
        void Update()
        {
            // Settings can be changed from another panel; refresh text only when the language changes.
            string marker = language != null ? language.CurrentLanguage : "";
            if (_lastLanguage != marker) { _lastLanguage = marker; Apply(); }
        }
        string Text(string key) { return language != null ? language._Text(key) : key; }
        void Apply()
        {
            bool ready = Ready();
            bool valid = ready && _index >= 0 && _index < frameKeys.Length && landscapeTextures[_index] != null && portraitTextures[_index] != null;
            bool show = valid && _enabled;
            if (_cameraMat != null && valid) { _cameraMat.SetTexture("_LandscapeTex", landscapeTextures[_index]); _cameraMat.SetTexture("_PortraitTex", portraitTextures[_index]); }
            if (_viewerMat != null && valid) _viewerMat.mainTexture = landscapeTextures[_index];
            if (cameraOverlay != null) cameraOverlay.enabled = show;
            if (viewerOverlay != null) viewerOverlay.enabled = show;
            if (preview != null) { preview.texture = valid ? landscapeTextures[_index] : null; preview.enabled = valid; }
            if (selectionLabel != null) selectionLabel.text = show ? Text(nameKeys[_index]) : Text("photo.frame.none");
            if (toggleLabel != null) toggleLabel.text = Text(show ? "photo.frame.on" : "photo.frame.off");
            if (statusLabel != null) statusLabel.text = Text(!ready ? "photo.frame.loading" : show ? "photo.frame.active" : "photo.frame.inactive");
            ApplyToggleVisual(show);
            if (toggleButton != null) toggleButton.interactable = valid;
            if (previousButton != null) previousButton.interactable = ready && frameKeys.Length > 1;
            if (nextButton != null) nextButton.interactable = ready && frameKeys.Length > 1;
        }
        void ApplyToggleVisual(bool show)
        {
            // Persistent state is separate from Selectable hover/press feedback.
            if (toggleButton != null && toggleButton.targetGraphic != null)
                toggleButton.targetGraphic.color = show ? new Color(.62f, .50f, .24f) : new Color(.30f, .28f, .24f);
            if (toggleThumb != null) toggleThumb.anchoredPosition = new Vector2(show ? 32f : -32f, 0f);
        }
    }
}
