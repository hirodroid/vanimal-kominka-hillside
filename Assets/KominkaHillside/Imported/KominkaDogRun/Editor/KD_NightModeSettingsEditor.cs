using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KominkaDogRun.Editor
{
    /// <summary>設定ホームのナイトモード (Slider)。常設の構築は <see cref="KD_MenuFinishEditor.BuildNightMode"/> (Menu/11 が毎回通す)。
    /// ここはその 1 行分だけを流す Setup と、配線を数える Verify、見た目の Render。
    /// **裏山改変 (段階 0-b)**: 古民家は SleepKit (購入アセット) の SleepKitSettingsManager へ渡していた。
    /// 裏山は <c>KH_NightMode</c> (<c>KD_MenuSettings.hillsideNight</c>) へ強度 0〜1 を渡す。SleepKit 前提の検査は skip を明示する。
    /// **IMP-ENV-005**: 同じ行に [オート][マニュアル] とモードごとの注記 (BuildNightMode が作る)。共通の時計 (KH_AutoNight) は Night/1・2 が作り検査する。</summary>
    public static class KD_NightModeSettingsEditor
    {
        const string Output = "Temp/night-mode/";
        static void Guard()
        {
            if (EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().path != KD_Hillside.ScenePath /* 裏山改変: シーンの正本は KD_Hillside */)
                throw new InvalidOperationException("Open the target scene in Edit mode");
        }

        [MenuItem("Tools/KominkaHillside/Menu/15 Setup Night Mode")]
        public static void Setup()
        {
            Guard(); Directory.CreateDirectory(Output);
            if (!EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Output + "before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true))
                throw new InvalidOperationException("Scene backup failed");
            var settings = KD_SceneObjects.Find("KD_MenuSettings").GetComponent<KD_MenuSettings>();
            var pouch = settings.pouch; var language = settings.language;
            var catalog = KD_LocalizationEditor.Load(); var log = new StringBuilder();
            KD_TreatSetupEditor.RestoreFontSource(log);   // 裏山改変: クレジット板が無いので元 TTF はここで差し直す
            KD_ShopSetupEditor.AddGlyphs(KD_LocalizationEditor.BundledCharacters(catalog), log);
            KD_MenuFinishEditor.BuildNightMode(pouch, settings, pouch.sectionTitleLabel.font, log);
            KD_LocalizationEditor.Configure(language, catalog, log);
            KD_LocalizationEditor.Bind(language, catalog, log);
            KD_ProductsEditor.Copy(language); KD_ProductsEditor.Copy(settings); KD_ProductsEditor.Copy(pouch);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(pouch.gameObject.scene);
            File.WriteAllText(Output + "setup.txt", log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Menu/15a Verify Night Mode")]
        public static void Verify()
        {
            Guard(); Directory.CreateDirectory(Output); var log = new StringBuilder(); int failures = 0;
            void Check(bool ok, string label) { log.AppendLine((ok ? "PASS " : "FAIL ") + label); if (!ok) failures++; }
            var settings = KD_SceneObjects.Find("KD_MenuSettings").GetComponent<KD_MenuSettings>();
            var language = settings.language; var catalog = KD_LocalizationEditor.Load();
            KD_LocalizationEditor.ValidateScene(language, catalog);
            var home = settings.pouch.settingsHome.transform;
            // ---- 裏山改変: 渡し先は KH_NightMode (シーンに 1 個) ----
            var nights = UnityEngine.Object.FindObjectsOfType<global::KominkaHillside.KH_NightMode>(true);
            Check(nights.Length == 1 && settings.hillsideNight == nights[0], "hillsideNight -> the scene's KH_NightMode (" + nights.Length + " in scene)");
            Check(settings.nightMode != null && settings.nightMode.transform.parent == home && settings.nightMode.name == "NightMode", "slider on settings home");
            Check(settings.nightModeValue != null && settings.nightModeValue.transform.parent == home, "value label on settings home");
            Check(settings.nightMode != null && settings.nightMode.minValue == 0f && settings.nightMode.maxValue == 1f && !settings.nightMode.wholeNumbers, "slider range 0..1 (強度 0〜100% をそのまま KH_NightMode._SetDarkness へ)");
            Check(settings.nightMode != null && Mathf.Approximately(settings.nightMode.value, 0f), "slider initial value 0 = OFF (復元は実行時に KH_NightMode._Darkness から)");
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(settings);
            foreach (string field in new[] { "nightMode", "nightModeValue" })
            {
                object stored; backing.publicVariables.TryGetVariableValue(field, out stored);
                Check(stored as UnityEngine.Object == (UnityEngine.Object)typeof(KD_MenuSettings).GetField(field).GetValue(settings), "backing " + field);
            }
            {
                // U# behaviour references are stored as the backing UdonBehaviour, not the proxy component.
                object stored; backing.publicVariables.TryGetVariableValue("hillsideNight", out stored);
                Check(settings.hillsideNight != null && stored as UnityEngine.Object == UdonSharpEditorUtility.GetBackingUdonBehaviour(settings.hillsideNight), "backing hillsideNight");
            }
            Check(typeof(global::KominkaHillside.KH_NightMode).GetMethod("_SetDarkness") != null && typeof(global::KominkaHillside.KH_NightMode).GetMethod("_Darkness") != null,
                "KH_NightMode has _SetDarkness(float) / _Darkness() (契約: 強度の受け口と復元の読み口)");
            if (settings.nightMode != null)
            {
                var so = new SerializedObject(settings.nightMode);
                var calls = so.FindProperty("m_OnValueChanged.m_PersistentCalls.m_Calls");
                Check(calls.arraySize == 1 && settings.nightMode.onValueChanged.GetPersistentTarget(0) == backing && settings.nightMode.onValueChanged.GetPersistentMethodName(0) == "SendCustomEvent"
                    && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == "_NightMode", "slider -> _NightMode");
            }
            // ---- IMP-ENV-005: [オート][マニュアル] と、モードごとの注記 ----
            var night = typeof(global::KominkaHillside.KH_NightMode);
            Check(night.GetMethod("_UseAuto") != null && night.GetMethod("_UseManual") != null && night.GetMethod("_ManualDarkness") != null && night.GetProperty("IsAuto") != null,
                "KH_NightMode has _UseAuto() / _UseManual() / _ManualDarkness() / IsAuto (契約: モードの受け口と、保存するマニュアル値の読み口)");
            Check(global::KominkaHillside.KH_NightMode.SaveKey == "KH_NightMode.dark" && global::KominkaHillside.KH_NightMode.ModeKey == "KH_NightMode.mode",
                "PlayerData keys: manual value KH_NightMode.dark (unchanged since IMP-UI-001) / mode KH_NightMode.mode (absent = Auto)");
            Check(typeof(KD_MenuSettings).GetMethod("_NightAuto") != null && typeof(KD_MenuSettings).GetMethod("_NightManual") != null, "KD_MenuSettings has _NightAuto() / _NightManual()");
            foreach (var choice in new[] { new[] { KD_MenuFinishEditor.NightAutoButtonName, "_NightAuto", "nightAutoImage" }, new[] { KD_MenuFinishEditor.NightManualButtonName, "_NightManual", "nightManualImage" } })
            {
                var t = home.Find(choice[0]);
                var button = t == null ? null : t.GetComponent<Button>();
                Check(button != null && t.gameObject.activeSelf, choice[0] + " button shown on settings home");
                if (button != null)
                {
                    var calls = new SerializedObject(button).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                    Check(calls.arraySize == 1 && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Target").objectReferenceValue == backing
                        && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_MethodName").stringValue == "SendCustomEvent"
                        && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == choice[1], choice[0] + " -> " + choice[1]);
                }
                var image = t == null ? null : t.GetComponent<Image>();
                object stored; backing.publicVariables.TryGetVariableValue(choice[2], out stored);
                Check(image != null && (Image)typeof(KD_MenuSettings).GetField(choice[2]).GetValue(settings) == image && stored as UnityEngine.Object == image, choice[2] + " wired (proxy and backing)");
            }
            var autoNoteT = home.Find(KD_MenuFinishEditor.NightAutoNoteName); var manualNoteT = home.Find("NightModeNote");
            Check(autoNoteT != null && manualNoteT != null && settings.nightAutoNote == autoNoteT.gameObject && settings.nightManualNote == manualNoteT.gameObject, "nightAutoNote / nightManualNote wired");
            Check(autoNoteT != null && manualNoteT != null && autoNoteT.gameObject.activeSelf && !manualNoteT.gameObject.activeSelf
                && settings.nightAutoImage != null && settings.nightAutoImage.color == settings.pouch.equipOnColor && settings.nightManualImage != null && settings.nightManualImage.color == settings.pouch.tabOffColor,
                "built in the Auto look (initial mode = Auto; KD_MenuSettings.RefreshNightModeChoice repaints at runtime)");
            Check(settings.nightModeFill != null && settings.nightMode != null && settings.nightMode.fillRect != null && settings.nightModeFill == settings.nightMode.fillRect.GetComponent<Image>(), "nightModeFill is the slider's Fill Image (dimmed while Auto)");
            foreach (string field in new[] { "nightAutoNote", "nightManualNote", "nightModeFill" })
            {
                object stored; backing.publicVariables.TryGetVariableValue(field, out stored);
                Check(stored as UnityEngine.Object == (UnityEngine.Object)typeof(KD_MenuSettings).GetField(field).GetValue(settings) && stored != null, "backing " + field);
            }
            foreach (var pair in new[] { new[] { "NightModeLabel", "settings.night.title" }, new[] { "NightModeNote", "settings.night.note" },
                                          new[] { KD_MenuFinishEditor.NightAutoButtonName + "/Label", "settings.night.auto" }, new[] { KD_MenuFinishEditor.NightManualButtonName + "/Label", "settings.night.manual" },
                                          new[] { KD_MenuFinishEditor.NightAutoNoteName, "settings.night.autoNote" } })
            {
                var label = home.Find(pair[0])?.GetComponent<TMP_Text>();
                int i = label == null ? -1 : Array.IndexOf(language.fixedLabels, label);
                Check(i >= 0 && language.fixedKeys[i] == pair[1], pair[0] + " bound to " + pair[1]);
                foreach (string code in new[] { "ja", "en" })
                    Check(!string.IsNullOrEmpty(KD_LocalizationEditor.Text(catalog, code, pair[1])), code + " " + pair[1]);
            }
            // 裏山改変: SleepKit (購入アセット) 前提の検査は裏山では対象外
            log.AppendLine("SKIP SleepKitSettingsManager / backing sleepKit / DarkToBrightness の写像 / SleepKit のタブレット / NightMode シェーダの式 (裏山では対象外: SleepKit 無し。KH_NightMode が暗さ 0〜1 を直接受ける)");
            Check(home.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Length == 0, "no additional behaviours on settings home");
            log.AppendLine("failures=" + failures); File.WriteAllText(Output + "verify.txt", log.ToString());
            if (failures > 0) throw new InvalidOperationException("Night mode verification failed: " + failures);
        }

        [MenuItem("Tools/KominkaHillside/Menu/15b Render Night Mode")]
        public static void Render()
        {
            Guard(); Directory.CreateDirectory(Output);
            var settings = KD_SceneObjects.Find("KD_MenuSettings").GetComponent<KD_MenuSettings>();
            var pouch = settings.pouch; var language = settings.language;
            var panel = KD_SceneObjects.Find("KD_TreatPouchPanel");
            var targets = panel.GetComponentsInChildren<Transform>(true).Concat(panel.GetComponentsInParent<Transform>(true)).Distinct().ToArray();
            var active = targets.Select(t => t.gameObject.activeSelf).ToArray();
            var texts = targets.Select(t => t.GetComponent<TMP_Text>()).Where(t => t != null).ToArray();
            var strings = texts.Select(t => t.text).ToArray();
            var panelPosition = panel.transform.localPosition; var panelRotation = panel.transform.localRotation;
            try
            {
                panel.transform.SetPositionAndRotation(new Vector3(0, 1000, 0), Quaternion.identity);
                foreach (var t in panel.GetComponentsInParent<Transform>(true)) t.gameObject.SetActive(true);
                foreach (var p in pouch.tabPages) if (p != null) p.SetActive(false);
                pouch.settingsPage.SetActive(true);
                foreach (Transform child in pouch.settingsPage.transform) child.gameObject.SetActive(false);
                pouch.settingsHome.SetActive(true);
                foreach (string code in new[] { "ja", "en" })
                {
                    for (int i = 0; i < language.fixedLabels.Length; i++)
                        if (language.fixedLabels[i] != null && language.fixedLabels[i].transform.IsChildOf(panel.transform)) language.fixedLabels[i].text = language._TextForLanguage(language.fixedKeys[i], code);
                    pouch.sectionTitleLabel.text = language._TextForLanguage("KD_TreatPouch.WordSettings", code);
                    Canvas.ForceUpdateCanvases();
                    KD_DisplayGuidanceEditor.RenderPanel(panel.transform, code + "-SettingsHome", Output);
                }
            }
            finally
            {
                for (int i = 0; i < texts.Length; i++) texts[i].text = strings[i];
                for (int i = 0; i < targets.Length; i++) targets[i].gameObject.SetActive(active[i]);
                panel.transform.localPosition = panelPosition; panel.transform.localRotation = panelRotation;
            }
        }
    }
}
