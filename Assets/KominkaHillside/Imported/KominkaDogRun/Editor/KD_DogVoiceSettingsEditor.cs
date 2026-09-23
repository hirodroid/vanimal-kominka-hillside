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
    public static class KD_DogVoiceSettingsEditor
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
            KD_ShopSetupEditor.EnsureButton(home.transform, "VolumeSettingsButton", new Vector2(-180f, 82f),
                new Vector2(340f, 64f), KD_UiSpriteEditor.ActionNeutral, font, KD_TreatPouch.WordVolumeSettings,
                udon, "_OpenVolumeSettings", log);
            KD_ShopSetupEditor.EnsureButton(audio.transform, "BackSettingsButton", new Vector2(-250f, -196f),
                new Vector2(180f, 44f), KD_UiSpriteEditor.ActionNeutral, font, KD_TreatPouch.WordBackSettings,
                udon, "_OpenSettings", log);

            // Six independent sliders: outside on the left, bedroom on the right.
            pouch.dogVoiceToggleImage = null; pouch.dogVoiceToggleLabel = null;
            pouch.bgmToggleImage = null; pouch.bgmToggleLabel = null;
            Label(audio.transform, "OutsideHeading", new Vector2(-175f, 112f), new Vector2(290f, 36f), 24f, KD_TreatPouch.WordOutside, font);
            Label(audio.transform, "BedroomHeading", new Vector2(175f, 112f), new Vector2(290f, 36f), 24f, KD_TreatPouch.WordBedroom, font);
            pouch.bgmSlider = BuildVolumeRow(audio.transform, "BgmTitle", "BgmValue", "BgmSlider",
                KD_TreatPouch.WordBgm, -175f, 72f, .5f, "_BgmChanged", font, round, udon, log, out var bgmValue);
            pouch.bgmValueLabel = bgmValue;
            pouch.dogVoiceSlider = BuildVolumeRow(audio.transform, "VoiceTitle", "VolumeValue", "VoiceSlider",
                KD_TreatPouch.WordDogVoice, -175f, -16f, .5f, "_DogVoiceChanged", font, round, udon, log, out var voiceValue);
            pouch.dogVoiceValueLabel = voiceValue;
            pouch.liveVolumeSlider = BuildVolumeRow(audio.transform, "LiveVolumeTitle", "LiveVolumeValue", "LiveVolumeSlider",
                KD_TreatPouch.WordLiveVolume, -175f, -104f, .5f, "_LiveVolumeChanged", font, round, udon, log, out var liveValue);
            pouch.liveVolumeValueLabel = liveValue;
            pouch.bedroomBgmVolumeSlider = BuildVolumeRow(audio.transform, "BedroomBgmVolumeTitle", "BedroomBgmVolumeValue", "BedroomBgmVolumeSlider",
                KD_TreatPouch.WordBgm, 175f, 72f, .1f, "_BedroomBgmVolumeChanged", font, round, udon, log, out var bedroomBgmValue);
            pouch.bedroomBgmVolumeValueLabel = bedroomBgmValue;
            pouch.bedroomVoiceVolumeSlider = BuildVolumeRow(audio.transform, "BedroomVoiceVolumeTitle", "BedroomVoiceVolumeValue", "BedroomVoiceVolumeSlider",
                KD_TreatPouch.WordDogVoice, 175f, -16f, .1f, "_BedroomVoiceVolumeChanged", font, round, udon, log, out var bedroomVoiceValue);
            pouch.bedroomVoiceVolumeValueLabel = bedroomVoiceValue;
            pouch.bedroomLiveVolumeSlider = BuildVolumeRow(audio.transform, "BedroomLiveVolumeTitle", "BedroomLiveVolumeValue", "BedroomLiveVolumeSlider",
                KD_TreatPouch.WordLiveVolume, 175f, -104f, .1f, "_BedroomLiveVolumeChanged", font, round, udon, log, out var bedroomLiveValue);
            pouch.bedroomLiveVolumeValueLabel = bedroomLiveValue;
            Label(audio.transform, "BedroomNote", new Vector2(110f, -182f), new Vector2(420f, 26f),
                18f, KD_TreatPouch.WordBedroomNote, font);
            Label(audio.transform, "VolumeScope", new Vector2(110f, -205f), new Vector2(420f, 26f),
                18f, KD_TreatPouch.WordVolumeScope, font);
            int roomCount;
            var roomGo = KD_SceneObjects.Find(KD_DogDefs.SleepAudioName, out roomCount);
            pouch.sleepAudio = roomGo != null && roomCount == 1 ? roomGo.GetComponent<KD_SleepAudio>() : null;
            home.SetActive(true);
            audio.SetActive(false);
            page.SetActive(false);

            var dogs = new List<KD_DogWanderController>();
            var sources = new List<AudioSource>();
            foreach (var dog in Resources.FindObjectsOfTypeAll<KD_DogWanderController>())
            {
                if (dog.gameObject.scene != pouch.gameObject.scene || dog.transform.root.name != "====KominkaDogRun====") continue;
                dogs.Add(dog);
                if (dog.breathSource != null)
                {
                    dog.breathSource.mute = true;
                    EditorUtility.SetDirty(dog.breathSource);
                }
                if (dog.barkSource != null && !sources.Contains(dog.barkSource)) sources.Add(dog.barkSource);
            }
            foreach (var pet in Resources.FindObjectsOfTypeAll<KD_DogPetController>())
            {
                if (pet.gameObject.scene != pouch.gameObject.scene || pet.transform.root.name != "====KominkaDogRun====") continue;
                if (pet.joyAudioSource != null && !sources.Contains(pet.joyAudioSource)) sources.Add(pet.joyAudioSource);
            }
            // BGM は別 Setup が作る。**どちらの順で流しても配線が揃う**ように、
            // 在れば引き直す (BGM 側も手元メニューを引き直す)
            foreach (var bgm in Resources.FindObjectsOfTypeAll<KD_BgmPlayer>())
                if (bgm.gameObject.scene == pouch.gameObject.scene) pouch.bgmPlayer = bgm;
            log.AppendLine("Bgm player wired: " + (pouch.bgmPlayer != null));

            pouch.voiceDogs = dogs.ToArray();
            pouch.dogVoiceSources = sources.ToArray();
            pouch.dogVoiceBaseVolumes = new float[sources.Count];
            for (int i = 0; i < sources.Count; i++)
            {
                pouch.dogVoiceBaseVolumes[i] = sources[i].volume;
                sources[i].mute = true;
                EditorUtility.SetDirty(sources[i]);
            }
            log.AppendLine("Voice settings: dogs=" + dogs.Count + " oneShotSources=" + sources.Count);
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

        [MenuItem("Tools/KominkaHillside/Menu/16 Setup Volume")]
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

        [MenuItem("Tools/KominkaHillside/Menu/16a Verify Dog Voice Settings")]
        public static void Verify()
        {
            var scene = SceneManager.GetActiveScene();
            var log = new StringBuilder("PROJECT " + Application.dataPath + "\n");
            int failed = 0;
            Action<bool, string> check = (ok, label) => { log.AppendLine((ok ? "PASS " : "FAIL ") + label); if (!ok) failed++; };
            KD_TreatPouch original = null;
            var expectedDogs = new List<KD_DogWanderController>();
            var expectedSources = new HashSet<AudioSource>();
            foreach (var dog in Resources.FindObjectsOfTypeAll<KD_DogWanderController>())
            {
                if (dog.gameObject.scene != scene || dog.transform.root.name != "====KominkaDogRun====") continue;
                expectedDogs.Add(dog);
                if (dog.barkSource != null) expectedSources.Add(dog.barkSource);
                log.AppendLine("DOG " + dog.name + " bark=" + (dog.barkSource != null) + " breath=" + (dog.breathSource != null));
            }
            foreach (var pet in Resources.FindObjectsOfTypeAll<KD_DogPetController>())
                if (pet.gameObject.scene == scene && pet.transform.root.name == "====KominkaDogRun====" && pet.joyAudioSource != null)
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
            log.AppendLine("RESULT fail=" + failed);
            File.WriteAllText("Temp/ui-dog-voice-settings.txt", log.ToString());
            Debug.Log(log.ToString());
            if (failed > 0) throw new InvalidOperationException("Voice settings verification failed: " + failed);
        }
    }
}
