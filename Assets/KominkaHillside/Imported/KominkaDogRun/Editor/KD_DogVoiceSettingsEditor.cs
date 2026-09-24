using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KominkaDogRun.Editor
{
    // 裏山改変: 音量は当面 犬の声のみ (IMP-UI-001 段階 3)。裏山の構築・Menu/16・16a は KD_DogVoiceSettingsEditor.Hillside.cs (partial)
    public static partial class KD_DogVoiceSettingsEditor
    {
        internal static void Build(KD_TreatPouch pouch, Transform parent, TMP_FontAsset font,
            VRC.Udon.UdonBehaviour udon, StringBuilder log)
        {
            var round = KD_UiSpriteEditor.LoadRound();
            var entry = KD_ShopSetupEditor.EnsureButton(parent, "SettingsButton", new Vector2(185f, 220f),
                new Vector2(120f, 48f), KD_UiSpriteEditor.TabOff, font, KD_TreatPouch.WordSettings,
                udon, "_OpenSettings", log);
            KD_UiSpriteEditor.ApplyRoundSkin(entry.GetComponent<Image>(), round);
            entry.transform.Find("Label").GetComponent<TextMeshProUGUI>().fontSize = 24f;
            pouch.settingsButtonImage = entry.GetComponent<Image>();
            var page = KD_ShopSetupEditor.EnsureUiChild(parent, "Page_Settings", Vector2.zero, new Vector2(760f, 520f));
            pouch.settingsPage = page;

            // IMP-ENV-011: settings hub -> volume page. Existing controls are moved, not duplicated.
            var home = KD_ShopSetupEditor.EnsureUiChild(page.transform, "SettingsHome", Vector2.zero, new Vector2(760f, 520f));
            var audio = KD_ShopSetupEditor.EnsureUiChild(page.transform, "VolumeSettings", Vector2.zero, new Vector2(760f, 520f));
            pouch.settingsHome = home;
            pouch.volumeSettingsPage = audio;
            // 旧レイアウト (09-08 以前) の音量コントロールだけを VolumeSettings へ寄せる。
            // **面 (サブページ) は動かさない** — 名前の除外リストだと後から足した面 (Information / GiftCodePage / PhotoSettings …) を
            // 黙って VolumeSettings の下へ移し、Bind が「Localization target missing」で落ちる (2026-09-16 実測)。
            // 面は板と同じ大きさ (700 以上 × 500 以上) なので、大きさで見分ける
            var legacy = new List<Transform>();
            foreach (Transform child in page.transform)
            {
                if (child == home.transform || child == audio.transform) continue;
                var rt = child as RectTransform;
                bool isPage = rt != null && rt.sizeDelta.x >= 700f && rt.sizeDelta.y >= 500f;
                if (!isPage) legacy.Add(child);
            }
            foreach (var child in legacy) Undo.SetTransformParent(child, audio.transform, "Move volume controls");
            // Old explanatory rows are replaced by two shared footnotes.
            foreach (string name in new[] { "VoiceScope", "VolumeTitle", "VoiceKinds", "SoundDivider", "BgmVolumeTitle", "BgmNote", "VoiceToggle", "BgmToggle", "BedroomVolumeTitle", "BedroomVolumeValue", "BedroomVolumeSlider" })
            {
                var old = audio.transform.Find(name);
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            }
            // 裏山改変 (段階 3): 音量の頁は「外」の 1 群 = 犬の声だけ。BGM・ライブ (DES-WLD-002 / DES-LIV-001 後) と寝室 (裏山に無い) の行は作らず、
            // 以前の構築で残っていれば消す。入口・戻る・行・音源の配線は EnsureHillsideVolume (KD_DogVoiceSettingsEditor.Hillside.cs) が持つ
            EnsureHillsideVolume(pouch, font, log);
            home.SetActive(true);
            audio.SetActive(false);
            page.SetActive(false);
            KD_DistanceFadeSettingsEditor.RebuildIfPresent(pouch, log);
        }

        // Two columns, three rows at 88px pitch; 52px hit/handle height is retained for VR input.
        const float SliderHeight = 52f;

        internal static void SetInitialSliderValue(Slider slider, float value)
        {
            slider.SetValueWithoutNotify(value);
            // The page is inactive while building, so update the saved thumb/fill as well.
            slider.handleRect.anchorMin = new Vector2(value, 0f);
            slider.handleRect.anchorMax = new Vector2(value, 1f);
            slider.fillRect.anchorMin = Vector2.zero;
            slider.fillRect.anchorMax = new Vector2(value, 1f);
        }

        static Slider BuildVolumeRow(Transform parent, string titleName, string valueName, string sliderName,
            string title, float x, float y, float initial, string customEvent, TMP_FontAsset font, Sprite round,
            VRC.Udon.UdonBehaviour udon, StringBuilder log, out TextMeshProUGUI value)
        {
            Label(parent, titleName, new Vector2(x - 45f, y), new Vector2(200f, 32f), 22f, title, font);
            value = Label(parent, valueName, new Vector2(x + 108f, y), new Vector2(74f, 32f), 22f, Mathf.RoundToInt(initial * 100f) + "%", font);
            value.alignment = TextAlignmentOptions.Right;
            var slider = BuildSlider(parent, sliderName, y, round, udon, customEvent, log);
            slider.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, y - 36f);
            SetInitialSliderValue(slider, initial);
            return slider;
        }

        // 裏山改変: 裏山の Menu/16 は SetupVolumeHillside (.Hillside.cs)。こちらは古民家の写し (Build は裏山版を通る)
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Volume/16 - Setup Volume (Kominka)")]
        public static void SetupVolumeSettings()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            var log = new StringBuilder();
            int count;
            var go = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName, out count);
            var pouch = go != null ? go.GetComponent<KD_TreatPouch>() : null;
            if (pouch == null || count != 1 || pouch.panelRoot == null) throw new InvalidOperationException("Pouch missing");
            var font = pouch.sectionTitleLabel.font;
            KD_ShopSetupEditor.AddGlyphs(KD_TreatSetupEditor.PouchWords(), log);
            Build(pouch, pouch.panelRoot.transform, font, UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch), log);
            KD_MenuFinishEditor.RebuildIfPresent(pouch, log);
            EditorUtility.SetDirty(pouch);
            UdonSharpEditorUtility.CopyProxyToUdon(pouch);
            EditorSceneManager.MarkSceneDirty(go.scene);
            File.WriteAllText("Temp/sleep-audio-ui-setup.txt", log.ToString());
        }

        /// <summary>音量スライダー 1 本。**2 節で同じ形にする**ので工場にしてある
        /// (犬の声と BGM で押し心地が違うと、同じ面なのに別物に見える)。</summary>
        internal static Slider BuildSlider(Transform parent, string name, float y, Sprite round,
            VRC.Udon.UdonBehaviour udon, string customEvent, StringBuilder log)
        {
            var sliderGo = KD_ShopSetupEditor.EnsureUiChild(parent, name, new Vector2(0f, y), new Vector2(290f, SliderHeight));
            var slider = sliderGo.GetComponent<Slider>();
            if (slider == null) slider = sliderGo.AddComponent<Slider>();
            // An invisible hit surface makes both the full rail and the thumb draggable in VR.
            var hit = sliderGo.GetComponent<Image>();
            if (hit == null) hit = sliderGo.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            Graphic(sliderGo.transform, "Track", new Vector2(250f, 10f), KD_UiSpriteEditor.TabOff);
            var fillArea = KD_ShopSetupEditor.EnsureUiChild(sliderGo.transform, "FillArea", Vector2.zero, new Vector2(250f, 10f));
            var fill = Graphic(fillArea.transform, "Fill", Vector2.zero, KD_UiSpriteEditor.ActionEquip);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = KD_ShopSetupEditor.EnsureUiChild(sliderGo.transform, "HandleArea", Vector2.zero, new Vector2(250f, 52f));
            var handle = Graphic(handleArea.transform, "Handle", new Vector2(40f, 0f), KD_UiSpriteEditor.AccentGold);
            KD_UiSpriteEditor.ApplyRoundSkin(handle, round);
            handle.raycastTarget = true;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            SetInitialSliderValue(slider, 0.5f);
            // The panel is saved inactive, so repeated Setup cannot rely on Slider.OnEnable.
            handle.rectTransform.anchoredPosition = Vector2.zero;
            KD_UiSpriteEditor.ApplyButtonFeedback(slider);
            for (int i = slider.onValueChanged.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(slider.onValueChanged, i);
            if (udon == null) throw new InvalidOperationException("Volume slider backing Udon missing: " + name);
            UnityEventTools.AddStringPersistentListener(slider.onValueChanged, udon.SendCustomEvent, customEvent);
            log.AppendLine("   slider " + name + " -> " + customEvent);
            return slider;
        }

        static Image Graphic(Transform parent, string name, Vector2 size, Color color)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, Vector2.zero, size);
            var image = go.GetComponent<Image>();
            if (image == null) image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
        static TextMeshProUGUI Label(Transform parent, string name, Vector2 pos, Vector2 size,
            float points, string value, TMP_FontAsset font)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var text = go.GetComponent<TextMeshProUGUI>();
            if (text == null) text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = points;
            text.enableAutoSizing = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.Left;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        // 裏山改変: 裏山の Menu/16a は VerifyHillside (.Hillside.cs)。古民家の犬の root 名 "====KominkaDogRun====" を前提にした写し
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Volume/16a - Verify Dog Voice Settings (Kominka)")]
        public static void Verify()
        {
            var log = new StringBuilder("PROJECT " + Application.dataPath + "\n");
            int failed = 0;
            Action<bool, string> check = (ok, label) => { log.AppendLine((ok ? "PASS " : "FAIL ") + label); if (!ok) failed++; };
            VerifyCore("====KominkaDogRun====", log, check);
            log.AppendLine("RESULT fail=" + failed);
            File.WriteAllText("Temp/ui-dog-voice-settings.txt", log.ToString());
            Debug.Log(log.ToString());
            if (failed > 0) throw new InvalidOperationException("Voice settings verification failed: " + failed);
        }

        /// <summary>裏山改変: 古民家の検査本体 (犬の root 名を引数に)。裏山の Menu/16a (VerifyHillside) は KH_Dogs で呼ぶ。</summary>
        static void VerifyCore(string rootName, StringBuilder log, Action<bool, string> check)
        {
            var scene = SceneManager.GetActiveScene();
            KD_TreatPouch original = null;
            var expectedDogs = new List<KD_DogWanderController>();
            var expectedSources = new HashSet<AudioSource>();
            foreach (var dog in Resources.FindObjectsOfTypeAll<KD_DogWanderController>())
            {
                if (dog.gameObject.scene != scene || dog.transform.root.name != rootName) continue;
                expectedDogs.Add(dog);
                if (dog.barkSource != null) expectedSources.Add(dog.barkSource);
                log.AppendLine("DOG " + dog.name + " bark=" + (dog.barkSource != null) + " breath=" + (dog.breathSource != null));
            }
            foreach (var pet in Resources.FindObjectsOfTypeAll<KD_DogPetController>())
                if (pet.gameObject.scene == scene && pet.transform.root.name == rootName && pet.joyAudioSource != null)
                    expectedSources.Add(pet.joyAudioSource);
            foreach (var p in Resources.FindObjectsOfTypeAll<KD_TreatPouch>()) if (p.gameObject.scene == scene) original = p;
            check(original != null, "pouch exists");
            if (original == null) throw new InvalidOperationException(log.ToString());
            check(original.voiceDogs != null && new HashSet<KD_DogWanderController>(original.voiceDogs).SetEquals(expectedDogs) && expectedDogs.Count > 0, "all dogs wired " + expectedDogs.Count);
            check(original.dogVoiceSources != null && new HashSet<AudioSource>(original.dogVoiceSources).SetEquals(expectedSources), "all one-shot voice sources wired " + expectedSources.Count);
            check(original.dogVoiceBaseVolumes != null && original.dogVoiceBaseVolumes.Length == expectedSources.Count, "base volumes match sources");
            check(original.settingsPage != null && !original.settingsPage.activeSelf, "settings initially hidden");
            var slider = original.dogVoiceSlider;
            check(slider != null && slider.minValue == 0f && slider.maxValue == 1f && !slider.wholeNumbers && slider.value == 0.5f, "continuous slider defaults to 50%");
            if (slider != null)
            {
                var so = new SerializedObject(slider);
                var calls = so.FindProperty("m_OnValueChanged.m_PersistentCalls.m_Calls");
                check(calls.arraySize == 1 && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Target").objectReferenceValue == UdonSharpEditorUtility.GetBackingUdonBehaviour(original)
                    && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == "_DogVoiceChanged", "slider invokes local Udon event once");
                check(slider.handleRect != null && slider.fillRect != null && slider.GetComponent<Image>().raycastTarget, "slider handle, fill and hit area");
            }
            if (slider != null && slider.handleRect != null)
                check(Mathf.Approximately(slider.handleRect.rect.height, 52f), "slider thumb stays 52 units tall (actual " + slider.handleRect.rect.height + ")");
            object rawDogs = null;
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(original);
            bool foundDogs = backing != null && backing.publicVariables.TryGetVariableValue("voiceDogs", out rawDogs);
            if (backing == null) rawDogs = null;
            else backing.publicVariables.TryGetVariableValue("voiceDogs", out rawDogs);
            var storedDogs = rawDogs as Array;
            bool matches = foundDogs && storedDogs != null && storedDogs.Length == original.voiceDogs.Length;
            if (matches)
                for (int i = 0; i < original.voiceDogs.Length; i++)
                {
                    object stored = storedDogs.GetValue(i);
                    matches &= Equals(stored, original.voiceDogs[i]) || Equals(stored, UdonSharpEditorUtility.GetBackingUdonBehaviour(original.voiceDogs[i]));
                }
            check(matches, "voiceDogs backing Udon references match proxy");
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var host = new GameObject("VoiceSettingsTest");
                SceneManager.MoveGameObjectToScene(host, preview);
                var pouch = host.AddUdonSharpComponent<KD_TreatPouch>();
                var dogHost = new GameObject("VoiceDogTest");
                SceneManager.MoveGameObjectToScene(dogHost, preview);
                var dog = dogHost.AddUdonSharpComponent<KD_DogWanderController>();
                var bark = host.AddComponent<AudioSource>();
                var breath = dogHost.AddComponent<AudioSource>();
                dog.breathSource = breath;
                typeof(KD_DogWanderController).GetField("_breathLevel", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dog, 0.4f);
                var sliderHost = new GameObject("TestSlider", typeof(RectTransform), typeof(Slider));
                SceneManager.MoveGameObjectToScene(sliderHost, preview);
                pouch.dogVoiceSlider = sliderHost.GetComponent<Slider>();
                var panel = new GameObject("TestPanel");
                SceneManager.MoveGameObjectToScene(panel, preview);
                panel.SetActive(false);
                pouch.panelRoot = panel;
                var settings = new GameObject("Settings");
                settings.transform.SetParent(panel.transform, false);
                var treat = new GameObject("Treat");
                treat.transform.SetParent(panel.transform, false);
                pouch.settingsPage = settings;
                pouch.tabPages = new[] { treat };
                pouch._OpenSettings();
                check(settings.activeSelf && !treat.activeSelf, "settings replaces the current tab");
                pouch._Tab0();
                check(!settings.activeSelf && treat.activeSelf, "same tab returns from settings");
                pouch.voiceDogs = new[] { dog };
                pouch.dogVoiceSources = new[] { bark };
                pouch.dogVoiceBaseVolumes = new[] { 0.8f };
                foreach (float value in new[] { 1f, 0.5f, 0f, 0.75f })
                {
                    pouch.dogVoiceSlider.value = value;
                    pouch._DogVoiceChanged();
                    check(Mathf.Approximately(bark.volume, 0.8f * value) && Mathf.Approximately(breath.volume, 0.4f * value), "voice gain " + value + " preserves source balance");
                }
                pouch._ToggleDogVoice();
                check(bark.mute && breath.mute, "OFF mutes ongoing bark and breath");
                pouch.dogVoiceSlider.value = 0.25f;
                pouch._DogVoiceChanged();
                check(bark.mute && breath.mute, "slider while OFF stays muted");
                pouch._ToggleDogVoice();
                check(!bark.mute && !breath.mute && Mathf.Approximately(bark.volume, 0.2f) && Mathf.Approximately(breath.volume, 0.1f), "ON restores selected volume");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
    }
}
