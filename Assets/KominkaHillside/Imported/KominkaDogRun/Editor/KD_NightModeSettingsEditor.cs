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
    /// <summary>IMP-ENV-015: SleepKit のナイトモードを設定ホームの Slider から操作する。
    /// 常設の構築は <see cref="KD_MenuFinishEditor.BuildNightMode"/> (Menu Finish/2 が毎回通す)。
    /// ここはその 1 行分だけを流す Setup と、配線を数える Verify、見た目の Render。</summary>
    public static class KD_NightModeSettingsEditor
    {
        const string Output = "Temp/night-mode/";
        static void Guard()
        {
            if (EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().path != "Assets/Scenes/KominkaDogRun.unity")
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
            // 裏山改変: 購入アセット (HoboRadio / SleepKit) は裏山に無い
#if KH_WITH_SLEEPKIT
            var managers = Resources.FindObjectsOfTypeAll<RBS.SleepKit2.Udon.SleepKitSettingsManager>().Where(m => m.gameObject.scene == settings.gameObject.scene).ToArray();
            Check(managers.Length == 1 && settings.sleepKit == managers[0], "sleepKit -> the scene's SleepKitSettingsManager");
#else
            Debug.Log("[KD_NightMode] 裏山では KH_NightMode に置き換える (段階 0-b)。SleepKit 前提の検査は行わない"); return;
#endif
            Check(settings.nightMode != null && settings.nightMode.transform.parent == home && settings.nightMode.name == "NightMode", "slider on settings home");
            Check(settings.nightModeValue != null && settings.nightModeValue.transform.parent == home, "value label on settings home");
            Check(settings.nightMode != null && settings.nightMode.minValue == 0f && settings.nightMode.maxValue == 1f && !settings.nightMode.wholeNumbers, "slider range 0..1");
            // BUG-ENV-002: 暗さ 0..1 -> 明るさ 1..MinNightBrightness の写像を両端・往復・下限未満で見る (片端だけだと常に 1 を返す誤実装が通る)。
            float floor = KD_MenuSettings.MinNightBrightness;
            Check(floor >= 0.3f && floor <= 0.5f, "MinNightBrightness within 0.3..0.5 (pow 2.5 -> dimming factor 4.9..18%): " + floor);
            Check(Mathf.Approximately(settings.DarkToBrightness(0f), 1f), "dark 0 (OFF) -> brightness 1");
            Check(settings.DarkToBrightness(1f) == floor, "dark 1 (100%) -> brightness exactly the floor (no rounding below it, or Update saves once more)");
            Check(settings.DarkToBrightness(0.999f) >= floor && settings.DarkToBrightness(0.001f) <= 1f, "brightness stays within floor..1 near the ends");
            Check(Mathf.Approximately(settings.DarkToBrightness(2f), floor) && Mathf.Approximately(settings.DarkToBrightness(-1f), 1f), "dark outside 0..1 clamps");
            Check(Mathf.Abs(settings.BrightnessToDark(settings.DarkToBrightness(0.5f)) - 0.5f) < 1e-4f, "dark 0.5 round trips");
            Check(Mathf.Approximately(settings.BrightnessToDark(1f), 0f), "brightness 1 -> dark 0");
            Check(Mathf.Approximately(settings.BrightnessToDark(0f), 1f) && Mathf.Approximately(settings.BrightnessToDark(0.2f), 1f), "brightness below floor (saved 0 / RBS board) -> dark 1 = Slider 100%");
            Check(Mathf.Pow(floor, 2.5f) >= Mathf.Pow(0.3f, 2.5f), "dimming factor at the floor >= 0.3^2.5 (4.9%): " + Mathf.Pow(floor, 2.5f).ToString("0.000"));
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(settings);
            foreach (string field in new[] { "nightMode", "nightModeValue" })
            {
                object stored; backing.publicVariables.TryGetVariableValue(field, out stored);
                Check(stored as UnityEngine.Object == (UnityEngine.Object)typeof(KD_MenuSettings).GetField(field).GetValue(settings), "backing " + field);
            }
            // 裏山改変: 購入アセット (HoboRadio / SleepKit) は裏山に無い
#if KH_WITH_SLEEPKIT
            {
                // U# behaviour references are stored as the backing UdonBehaviour, not the proxy component.
                object stored; backing.publicVariables.TryGetVariableValue("sleepKit", out stored);
                Check(settings.sleepKit != null && stored as UnityEngine.Object == UdonSharpEditorUtility.GetBackingUdonBehaviour(settings.sleepKit), "backing sleepKit");
            }
#endif
            if (settings.nightMode != null)
            {
                var so = new SerializedObject(settings.nightMode);
                var calls = so.FindProperty("m_OnValueChanged.m_PersistentCalls.m_Calls");
                Check(calls.arraySize == 1 && settings.nightMode.onValueChanged.GetPersistentTarget(0) == backing && settings.nightMode.onValueChanged.GetPersistentMethodName(0) == "SendCustomEvent"
                    && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == "_NightMode", "slider -> _NightMode");
            }
            foreach (var pair in new[] { new[] { "NightModeLabel", "settings.night.title" }, new[] { "NightModeNote", "settings.night.note" } })
            {
                var label = home.Find(pair[0])?.GetComponent<TMP_Text>();
                int i = label == null ? -1 : Array.IndexOf(language.fixedLabels, label);
                Check(i >= 0 && language.fixedKeys[i] == pair[1], pair[0] + " bound to " + pair[1]);
                foreach (string code in new[] { "ja", "en" })
                    Check(!string.IsNullOrEmpty(KD_LocalizationEditor.Text(catalog, code, pair[1])), code + " " + pair[1]);
            }
            // The purchased tablet stays unreachable: this slider is the hand-menu entry (user decision 2026-09-12).
            // The RBS bed board keeps its own slider; BUG-ENV-002 clamps that path too, so it is not required to be off.
            var tablet = KD_SceneObjects.Find("タブレットの呼び出し", out int tablets);
            Check(tablets == 1 && tablet != null && !tablet.activeSelf, "SleepKit tablet call stays inactive");
            // 補正の前提: SleepKit のシェーダが pow 2.5 で減光し、PC 版は明るさ >= 0.95 で覆いを描かない (個票に書いた読みが現物と一致しているか)。
            // 既知の式との文字列一致に限る検査。SleepKit を更新して落ちたら、シェーダを読み直して下限と個票を見直す合図。
            string pcShader = File.ReadAllText("Assets/RBS/SleepKit2/Shader/NightMode.shader");
            string questShader = File.ReadAllText("Assets/RBS/SleepKit2/Shader/QuestNightMode.shader");
            Check(pcShader.Contains("pow(_Brightness, 2.5)") && pcShader.Contains("_Brightness < 0.95"), "PC NightMode shader still pow 2.5 with the 0.95 cutoff");
            Check(questShader.Contains("pow(_Brightness, 2.5)") && !questShader.Contains("0.95"), "Quest NightMode shader still pow 2.5 without a cutoff");
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
