using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.Persistence;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaDogRun
{
    /// <summary>Personal, key-based localization. Language-major flattened tables are generated in the Editor.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_Language : UdonSharpBehaviour
    {
        // Keep v1: stored values were already language codes, not array indices.
        public const string SaveKey = "KD_Language_v1";
        public string[] keys;
        public string[] languageCodes;
        public string[] languageNames;
        // 別名 (IMP-INF-010): languageAliasCodes[i] を受け取ったら languageAliasTargets[i] の言語として解決する。
        // VRChat が返す RFC 5646 は端末で揺れる (繁体字は zh-TW / zh-Hant / zh-HK)。上位コード探索だけでは
        // zh-Hant から zh-hk へ辿れないので、文言表の aliases を 2 本の配列で配線する。Udon に Dictionary は無く、
        // 件数は言語数程度なので線形探索でよい。保存するのは常に正規の languageCodes 側。
        public string[] languageAliasCodes;
        public string[] languageAliasTargets;
        public string[] texts;
        public string defaultLanguage = "en";
        public string fallbackLanguage = "ja";
        public TMP_Text[] fixedLabels;
        public string[] fixedKeys;
        public UdonBehaviour[] interactTargets;
        public string[] interactKeys;
        public VRC_Pickup[] pickupTargets;
        public string[] pickupKeys;
        public TMP_InputField shareField;
        public string shareKey = "share.post";
        public TextMeshProUGUI choiceLabel;
        public TextMeshProUGUI welcomeChoiceLabel, introductionChoiceLabel;
        public UnityEngine.UI.ScrollRect welcomeScroll, introductionScroll;
        public KD_TreatPouch pouch;
        public KD_ShopKeeper keeper;
        public KD_ToyBox toyBox;
        public KD_ShopShelf[] shelves;
        private DataDictionary _lookup;
        private bool _initialized;
        private bool _restored;
        private bool _chosen;
        private int _languageIndex;
        // net: callable-ok: personal display state only
        public string CurrentLanguage { get { return languageCodes[_languageIndex]; } }

        private void Start() { Initialize(); Apply(); }
        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            if (!_chosen) _languageIndex = ResolveLanguage(VRCPlayerApi.GetCurrentLanguage());
        }
        // 解決順: 完全一致 → 別名一致 → 上位コードで完全一致 → 上位コードで別名一致 → defaultLanguage → 先頭
        // net: callable-ok: resolves a language tag without changing preferences
        public int ResolveLanguage(string code)
        {
            string candidate = string.IsNullOrEmpty(code) ? "" : code.Trim().ToLowerInvariant().Replace("_", "-");
            while (candidate.Length > 0)
            {
                for (int i = 0; languageCodes != null && i < languageCodes.Length; i++)
                    if (languageCodes[i] == candidate) return i;
                int alias = AliasIndex(candidate);
                if (alias >= 0) return alias;
                int separator = candidate.LastIndexOf("-");
                if (separator < 0) break;
                candidate = candidate.Substring(0, separator);
            }
            for (int i = 0; languageCodes != null && i < languageCodes.Length; i++)
                if (languageCodes[i] == defaultLanguage) return i;
            return 0;
        }
        /// <summary>別名 1 つを言語番号へ。未登録なら -1。配列は Editor の Configure が文言表から配線する。</summary>
        private int AliasIndex(string candidate)
        {
            if (languageAliasCodes == null || languageAliasTargets == null || languageCodes == null) return -1;
            for (int a = 0; a < languageAliasCodes.Length && a < languageAliasTargets.Length; a++)
            {
                if (languageAliasCodes[a] != candidate) continue;
                for (int i = 0; i < languageCodes.Length; i++)
                    if (languageCodes[i] == languageAliasTargets[a]) return i;
            }
            return -1;
        }
        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal || _restored) return;
            Initialize(); _restored = true;
            if (_chosen) Save();
            else
            {
                string value;
                if (PlayerData.TryGetString(player, SaveKey, out value) && !string.IsNullOrEmpty(value))
                    _languageIndex = ResolveLanguage(value);
            }
            Apply();
        }
        public void _NextLanguage() { Initialize(); Choose((_languageIndex + 1) % languageCodes.Length); }
        public void _PreviousLanguage() { Initialize(); Choose((_languageIndex + languageCodes.Length - 1) % languageCodes.Length); }
        public void _SelectLanguage(string code) { Initialize(); Choose(ResolveLanguage(code)); }
        private void Choose(int index)
        {
            _chosen = true; _languageIndex = index;
            if (_restored) Save();
            Apply();
        }
        private void Save() { PlayerData.SetString(SaveKey, languageCodes[_languageIndex]); }
        public string _Text(string key) { Initialize(); return TextAt(key, _languageIndex); }
        // Also used by Editor verification; does not query the VRChat language or write preferences.
        public string _TextForLanguage(string key, string code) { return TextAt(key, ResolveLanguage(code)); }
        private string TextAt(string key, int languageIndex)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (_lookup == null || keys != null && _lookup.Count != keys.Length)
            {
                _lookup = new DataDictionary();
                for (int i = 0; keys != null && i < keys.Length; i++) _lookup[keys[i]] = i;
            }
            DataToken token;
            if (!_lookup.TryGetValue(key, out token)) return key;
            int index = token.Int;
            string value = texts[languageIndex * keys.Length + index];
            if (!string.IsNullOrEmpty(value)) return value;
            for (int i = 0; i < languageCodes.Length; i++)
                if (languageCodes[i] == fallbackLanguage) return texts[i * keys.Length + index];
            return key;
        }
        private void Apply()
        {
            for (int i = 0; fixedLabels != null && i < fixedLabels.Length; i++)
                if (fixedLabels[i] != null) fixedLabels[i].text = _Text(fixedKeys[i]);
            for (int i = 0; interactTargets != null && i < interactTargets.Length; i++)
                if (interactTargets[i] != null) interactTargets[i].InteractionText = _Text(interactKeys[i]);
            for (int i = 0; pickupTargets != null && i < pickupTargets.Length; i++)
                if (pickupTargets[i] != null) pickupTargets[i].InteractionText = _Text(pickupKeys[i]);
            if (shareField != null) shareField.text = _Text(shareKey);
            if (choiceLabel != null) choiceLabel.text = _Text("language.selection").Replace("{0}", languageNames[_languageIndex]);
            if (welcomeChoiceLabel != null) welcomeChoiceLabel.text = languageNames[_languageIndex];
            if (introductionChoiceLabel != null) introductionChoiceLabel.text = languageNames[_languageIndex];
            if (welcomeScroll != null) welcomeScroll.verticalNormalizedPosition = 1;
            if (introductionScroll != null) introductionScroll.verticalNormalizedPosition = 1;
            if (pouch != null) pouch._RefreshLanguage();
            if (keeper != null) keeper._RefreshLanguage();
            if (toyBox != null) toyBox._RefreshPickupText();
            for (int i = 0; shelves != null && i < shelves.Length; i++)
                if (shelves[i] != null) shelves[i]._RefreshLanguage();
        }
    }
}
