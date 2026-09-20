using System.Globalization;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>IMP-DOG-024: personal rendering override. No network events or per-frame writes.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_DistanceFadeSettings : UdonSharpBehaviour
    {
        public const string Title = "距離フェード設定";
        // IMP-DOG-036: ボタンは全端末で「(PCのみ)」付き。面の見出しは Title のまま (「設定」を含めると Label 316px を 4px 超える)
        public const string EntryButton = "距離フェード (PCのみ)";
        public const string Standard = "標準";
        public const string Deep = "深め";
        public const string Soft = "やわらかめ";
        public const string Custom = "カスタム";
        public const string ColorLabel = "色 (#RRGGBB)";
        public const string StartLabel = "開始距離 (m)";
        public const string EndLabel = "終了距離 (m)";
        public const string StrengthLabel = "強度";
        public const string ScopeNote = "自分だけに反映・次回も復元";
        public const string InputNote = "確定前の入力は切り替えると破棄";
        public const string Confirm = "確定して保存";
        public const string Saved = "カスタムを保存しました";
        public const string Draft = "未確定: 「確定して保存」を押してください";
        public const string Loading = "設定をよみこみ中";
        public const string ColorError = "色は # と6桁の16進数で入力";
        public const string NumberError = "数値は半角・小数点は . で入力";
        public const string RangeError = "開始距離 > 終了距離 >= 0、強度 >= 0";
        public const string RestoreError = "保存値を読めないため標準を表示";
        public const string SaveKey = "KD_DistanceFade_v1";

        public Renderer[] targetRenderers;
        public int[] targetSlots;
        public Shader targetShader;
        public Color standardColor;
        public Vector3 standardValues;
        public TMP_InputField colorInput;
        public TMP_InputField startInput;
        public TMP_InputField endInput;
        public TMP_InputField strengthInput;
        public TextMeshProUGUI errorLabel;
        public TextMeshProUGUI selectionLabel;
        public Button[] presetButtons;
        public Image[] presetImages;
        public Button confirmButton;
        public Color selectedColor;
        public Color unselectedColor;
        // IMP-DOG-036: 設定ホームの入口。毛材質 (Uzumore) は SubShader 全体が geometry 必須で、Android / iOS では
        // Fallback (Unlit/Texture) になる可能性があり距離フェードが効く保証が無い (描画経路は未確認) ので、設定だけ閉じる。本体・復元・適用は残す
        public Button entryButton;

        private bool _initialized;
        private bool _loaded;
        private int _selection;
        private Color _customColor;
        private Vector3 _customValues;
        private MaterialPropertyBlock _block;
        private string _shownColor;
        private string _shownStart;
        private string _shownEnd;
        private string _shownStrength;

        public KD_Language language;
        private string L(string source) { return language != null ? language._Text(source) : source; }

        // net: callable-ok: read-only platform capability
        public bool Available()
        {
            bool available = true;
#if UNITY_ANDROID || UNITY_IOS
            available = false;
#endif
            return available;
        }

        private void Start()
        {
            Initialize();
            if (entryButton != null) entryButton.interactable = Available();
            // OnPlayerRestored may precede Start; never reset restored state here.
            _RefreshUI();
        }

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _customColor = standardColor;
            _customValues = standardValues;
            _block = new MaterialPropertyBlock();
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal || _loaded) return;
            Initialize();
            string saved;
            bool valid = true;
            if (PlayerData.TryGetString(player, SaveKey, out saved)) valid = _RestoreRecord(saved);
            _loaded = true;
            _ApplyCurrent();
            _RefreshUI();
            if (!valid && errorLabel != null) errorLabel.text = L("KD_DistanceFadeSettings.RestoreError");
        }

        // A single record prevents selection and custom values being restored from different writes.
        // Public for the Editor's independent fixture checks; local-only, never network callable.
        public bool _RestoreRecord(string saved)
        {
            Initialize();
            if (string.IsNullOrEmpty(saved)) return false;
            string[] parts = saved.Split('|');
            if (parts.Length != 6 || parts[0] != "1") return false;
            int choice;
            Color color;
            Vector3 values;
            if (!int.TryParse(parts[1], out choice) || choice < 0 || choice > 3
                || !_TryColor(parts[2], out color)
                || !_TryValues(parts[3], parts[4], parts[5], out values)) return false;
            _selection = choice;
            _customColor = color;
            _customValues = values;
            return true;
        }

        public void _SelectStandard() { Select(0); }
        public void _SelectDeep() { Select(1); }
        public void _SelectSoft() { Select(2); }
        public void _SelectCustom() { Select(3); }

        private void Select(int choice)
        {
            if (!_loaded) return;
            _selection = choice;
            _ApplyCurrent();
            Save();
            _RefreshUI();
        }

        public void _DraftChanged()
        {
            if (_loaded && _selection == 3 && errorLabel != null) errorLabel.text = L("KD_DistanceFadeSettings.Draft");
        }

        public void _ConfirmCustom()
        {
            if (!_loaded || _selection != 3 || colorInput == null || startInput == null || endInput == null || strengthInput == null) return;
            Color color;
            Vector3 values;
            if (!_TryColor(colorInput.text, out color)) { errorLabel.text = L("KD_DistanceFadeSettings.ColorError"); return; }
            float a, b, c;
            if (!TryNumber(startInput.text, out a) || !TryNumber(endInput.text, out b)
                || !TryNumber(strengthInput.text, out c)) { errorLabel.text = L("KD_DistanceFadeSettings.NumberError"); return; }
            if (!_TryValues(startInput.text, endInput.text, strengthInput.text, out values))
            { errorLabel.text = L("KD_DistanceFadeSettings.RangeError"); return; }
            // Do not quantize untouched baseline fields through their display representation.
            _customColor = colorInput.text == _shownColor ? CurrentColor() : color;
            _customValues = values;
            _selection = 3;
            _ApplyCurrent();
            Save();
            _RefreshUI();
            if (errorLabel != null) errorLabel.text = L("KD_DistanceFadeSettings.Saved");
        }

        public bool _TryColor(string raw, out Color color)
        {
            color = Color.black;
            if (raw == null) return false;
            string s = raw.Trim().ToUpperInvariant();
            if (s.StartsWith("#")) s = s.Substring(1);
            if (s.Length != 6) return false;
            int packed = 0;
            for (int i = 0; i < 6; i++)
            {
                int digit = "0123456789ABCDEF".IndexOf(s.Substring(i, 1));
                if (digit < 0) return false;
                packed = packed * 16 + digit;
            }
            color = new Color(((packed >> 16) & 255) / 255f, ((packed >> 8) & 255) / 255f, (packed & 255) / 255f, 1f);
            return true;
        }

        private bool TryNumber(string raw, out float value)
        {
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public bool _TryValues(string start, string end, string strength, out Vector3 values)
        {
            values = Vector3.zero;
            float a, b, c;
            if (!TryNumber(start, out a) || !TryNumber(end, out b) || !TryNumber(strength, out c)) return false;
            if (b < 0f || a <= b || c < 0f) return false;
            values = new Vector3(a, b, c);
            return true;
        }

        private string Hex(Color color)
        {
            return "#" + Mathf.RoundToInt(color.r * 255f).ToString("X2")
                + Mathf.RoundToInt(color.g * 255f).ToString("X2") + Mathf.RoundToInt(color.b * 255f).ToString("X2");
        }

        public string _CreateRecord()
        {
            Initialize();
            return "1|" + _selection.ToString() + "|" + Hex(_customColor) + "|"
                + Number(_customValues.x) + "|" + Number(_customValues.y) + "|" + Number(_customValues.z);
        }

        private void Save()
        {
            if (_loaded && Networking.LocalPlayer != null) PlayerData.SetString(SaveKey, _CreateRecord());
        }

        private string Number(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private Color CurrentColor() { return _selection == 3 ? _customColor : _selection == 1 ? new Color(29f/255f, 27f/255f, 25f/255f, 1f) : standardColor; }
        private Vector3 CurrentValues() { return _selection == 3 ? _customValues : _selection == 1 ? new Vector3(.18f, .05f, 1.10f) : _selection == 2 ? new Vector3(.18f, .05f, .75f) : standardValues; }

        /// <summary>Also the explicit hook for future renderer/material replacement code.
        /// Indexed blocks survive SetActive and material swaps on the same renderer/slot.</summary>
        public void _ApplyCurrent()
        {
            Initialize();
            Color color = CurrentColor();
            Vector3 values = CurrentValues();
            if (targetRenderers == null || targetSlots == null) return;
            for (int i = 0; i < targetRenderers.Length && i < targetSlots.Length; i++)
            {
                Renderer target = targetRenderers[i];
                if (target == null) continue;
                Material[] mats = target.sharedMaterials;
                int slot = targetSlots[i];
                if (slot < 0 || slot >= mats.Length || mats[slot] == null || mats[slot].shader != targetShader) continue;
                Material mat = mats[slot];
                _block.Clear();
                target.GetPropertyBlock(_block, slot);
                // Only these two properties. Keep w (backface handling), alpha, rim and mode.
                Vector4 fade = mat.GetVector("_DistanceFade");
                color.a = mat.GetColor("_DistanceFadeColor").a;
                _block.SetVector("_DistanceFade", new Vector4(values.x, values.y, values.z, fade.w));
                _block.SetColor("_DistanceFadeColor", color); // sRGB, converted once by Unity in Linear projects.
                target.SetPropertyBlock(_block, slot);
            }
        }

        public void _RefreshUI()
        {
            Initialize();
            Vector3 values = CurrentValues();
            _shownColor = Hex(CurrentColor());
            _shownStart = Number(values.x); _shownEnd = Number(values.y); _shownStrength = Number(values.z);
            bool editing = _loaded && _selection == 3;
            if (colorInput != null) { colorInput.SetTextWithoutNotify(_shownColor); colorInput.interactable = editing; }
            if (startInput != null) { startInput.SetTextWithoutNotify(_shownStart); startInput.interactable = editing; }
            if (endInput != null) { endInput.SetTextWithoutNotify(_shownEnd); endInput.interactable = editing; }
            if (strengthInput != null) { strengthInput.SetTextWithoutNotify(_shownStrength); strengthInput.interactable = editing; }
            if (confirmButton != null) { confirmButton.gameObject.SetActive(_selection == 3); confirmButton.interactable = editing; }
            if (presetButtons != null) for (int i = 0; i < presetButtons.Length; i++) if (presetButtons[i] != null) presetButtons[i].interactable = _loaded;
            if (presetImages != null) for (int i = 0; i < presetImages.Length; i++) if (presetImages[i] != null) presetImages[i].color = i == _selection ? selectedColor : unselectedColor;
            if (selectionLabel != null) selectionLabel.text = L("ui.extra.025") + (_selection == 0 ? L("KD_DistanceFadeSettings.Standard") : _selection == 1 ? L("KD_DistanceFadeSettings.Deep") : _selection == 2 ? L("KD_DistanceFadeSettings.Soft") : L("KD_DistanceFadeSettings.Custom"));
            if (errorLabel != null) errorLabel.text = _loaded ? "" : L("KD_DistanceFadeSettings.Loading");
        }
    }
}
