using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KominkaDogRun.Editor
{
    /// <summary>裏山改変 (IMP-UI-001 段階 3): 音量の頁は当面 **犬の声のみ** (BGM・ライブは DES-WLD-002 / DES-LIV-001 の後、寝室は裏山に無い)。
    /// 頁は古民家と同じ Page_Settings/VolumeSettings で、「外」の 1 群 = 犬の声のスライダー + 値 + 注記 + [設定へ戻る]。
    /// 音源は古民家と同じ集め方 (KD_DogWanderController の bark / breath、KD_DogPetController の joyAudioSource) で、犬の root を KH_Dogs に読み替える。
    /// KD_TreatPouch の bgmSlider / liveVolumeSlider / bedroom* は null のまま (Apply / Redraw は null で何もしない)。</summary>
    public static partial class KD_DogVoiceSettingsEditor
    {
        const string HillsideMenu = "Tools/KominkaHillside/Menu/";
        const string HillsideSetupLog = "Temp/menu-rebuild/volume-setup.txt";
        const string HillsideVerifyLog = "Temp/menu-rebuild/volume-verify.txt";
        /// <summary>裏山で作らない行 (古民家の BGM / ライブ / 寝室の列)。以前の構築 (0-b は古民家の 6 本で作った) で残っていれば消す。</summary>
        static readonly string[] HillsideRetiredRows =
        {
            "BgmTitle", "BgmValue", "BgmSlider", "LiveVolumeTitle", "LiveVolumeValue", "LiveVolumeSlider",
            "BedroomHeading", "BedroomBgmVolumeTitle", "BedroomBgmVolumeValue", "BedroomBgmVolumeSlider",
            "BedroomVoiceVolumeTitle", "BedroomVoiceVolumeValue", "BedroomVoiceVolumeSlider",
            "BedroomLiveVolumeTitle", "BedroomLiveVolumeValue", "BedroomLiveVolumeSlider", "BedroomNote",
        };
        /// <summary>「外」の 1 群を頁の中央の列に置く (古民家は 2 列で外が左 x=-175)。行は古民家の 1 行目の高さ。</summary>
        const float HillsideColumnX = 0f, HillsideHeadingY = 112f, HillsideVoiceRowY = 72f, HillsideScopeY = -14f;
        /// <summary>注記の文言キー (keys-U1.json)。古民家の KD_TreatPouch.WordVolumeScope「自分だけ・BGMはライブ中に停止」は BGM の無い裏山に合わない。</summary>
        public const string HillsideScopeKey = "settings.volume.scope";

        /// <summary>音量の頁を裏山の形に揃える (冪等)。Menu/7 (Build 経由)・Menu/11 (設定面の再構築)・Menu/16 から呼ぶ。
        /// 前提: KD_TreatPouch の Page_Settings / SettingsHome が居る (Menu/7 の Build が作る)。</summary>
        internal static void EnsureHillsideVolume(KD_TreatPouch pouch, TMP_FontAsset font, StringBuilder log)
        {
            if (pouch == null || pouch.settingsPage == null || pouch.settingsHome == null) throw new InvalidOperationException("Settings page missing (先に Tools/KominkaHillside/Menu/7 Build Menu)");
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
            if (udon == null) throw new InvalidOperationException("KD_TreatPouch backing Udon missing");
            var round = KD_UiSpriteEditor.LoadRound();
            var home = pouch.settingsHome.transform;
            var audioGo = KD_ShopSetupEditor.EnsureUiChild(pouch.settingsPage.transform, "VolumeSettings", Vector2.zero, new Vector2(760f, 520f));
            bool wasActive = audioGo.activeSelf;
            pouch.volumeSettingsPage = audioGo;
            var audio = audioGo.transform;
            KD_ShopSetupEditor.EnsureButton(home, "VolumeSettingsButton", KD_MenuFinishEditor.HillsideVolumeEntryPos /* 位置の正本は KD_MenuFinishEditor.Hillside */,
                KD_MenuFinishEditor.HillsideEntrySize, KD_UiSpriteEditor.ActionNeutral, font, KD_TreatPouch.WordVolumeSettings, udon, "_OpenVolumeSettings", log);
            KD_ShopSetupEditor.EnsureButton(audio, "BackSettingsButton", new Vector2(-250f, -196f),
                new Vector2(180f, 44f), KD_UiSpriteEditor.ActionNeutral, font, KD_TreatPouch.WordBackSettings, udon, "_OpenSettings", log);
            foreach (string name in HillsideRetiredRows)
            {
                var old = audio.Find(name);
                if (old != null) { Undo.DestroyObjectImmediate(old.gameObject); log.AppendLine("   removed VolumeSettings/" + name + " (裏山: 犬の声のみ)"); }
            }
            pouch.bgmSlider = null; pouch.bgmValueLabel = null; pouch.bgmToggleImage = null; pouch.bgmToggleLabel = null;
            pouch.liveVolumeSlider = null; pouch.liveVolumeValueLabel = null;
            pouch.bedroomBgmVolumeSlider = null; pouch.bedroomBgmVolumeValueLabel = null;
            pouch.bedroomVoiceVolumeSlider = null; pouch.bedroomVoiceVolumeValueLabel = null;
            pouch.bedroomLiveVolumeSlider = null; pouch.bedroomLiveVolumeValueLabel = null;
            pouch.dogVoiceToggleImage = null; pouch.dogVoiceToggleLabel = null;
            pouch.sleepAudio = null; pouch.bgmPlayer = null;

            Label(audio, "OutsideHeading", new Vector2(HillsideColumnX, HillsideHeadingY), new Vector2(290f, 36f), 24f, KD_TreatPouch.WordOutside, font);
            pouch.dogVoiceSlider = BuildVolumeRow(audio, "VoiceTitle", "VolumeValue", "VoiceSlider",
                KD_TreatPouch.WordDogVoice, HillsideColumnX, HillsideVoiceRowY, .5f, "_DogVoiceChanged", font, round, udon, log, out var voiceValue);
            pouch.dogVoiceValueLabel = voiceValue;
            var scope = Label(audio, "VolumeScope", new Vector2(HillsideColumnX, HillsideScopeY), new Vector2(290f, 26f), 18f,
                KD_Hillside.Text(HillsideScopeKey, "自分だけに反映・0% で消音", log), font);
            scope.enableWordWrapping = true;

            // 音源: 古民家と同じ (吠え声 = bark、息 = breath は犬が持つ、撫でた時の喜び声 = joyAudioSource)。root を KH_Dogs に読み替える
            var dogs = new List<KD_DogWanderController>();
            var sources = new List<AudioSource>();
            int joy = 0;
            foreach (var dog in Resources.FindObjectsOfTypeAll<KD_DogWanderController>())
            {
                if (dog.gameObject.scene != pouch.gameObject.scene || dog.transform.root.name != KD_Hillside.DogsRootName) continue;
                dogs.Add(dog);
                if (dog.breathSource != null) { dog.breathSource.mute = true; EditorUtility.SetDirty(dog.breathSource); }
                if (dog.barkSource != null && !sources.Contains(dog.barkSource)) sources.Add(dog.barkSource);
            }
            foreach (var pet in Resources.FindObjectsOfTypeAll<KD_DogPetController>())
            {
                if (pet.gameObject.scene != pouch.gameObject.scene || pet.transform.root.name != KD_Hillside.DogsRootName) continue;
                if (pet.joyAudioSource != null && !sources.Contains(pet.joyAudioSource)) { sources.Add(pet.joyAudioSource); joy++; }
            }
            pouch.voiceDogs = dogs.ToArray();
            pouch.dogVoiceSources = sources.ToArray();
            pouch.dogVoiceBaseVolumes = new float[sources.Count];
            for (int i = 0; i < sources.Count; i++)
            {
                pouch.dogVoiceBaseVolumes[i] = sources[i].volume;
                // 実行時の出音は KD_TreatPouch.Start → ApplyDogVoiceSettings が決める (古民家と同じ。焼いた値は常に消音)
                sources[i].mute = true;
                EditorUtility.SetDirty(sources[i]);
            }
            audioGo.SetActive(wasActive);
            EditorUtility.SetDirty(pouch);
            log.AppendLine("Volume (裏山: 犬の声のみ): dogs=" + dogs.Count + " sources=" + sources.Count + " (joy=" + joy + ")");
        }

        /// <summary>Menu/16。前提: Menu/7 Build Menu。音量の頁を揃え、設定面を組み直して文言を束縛し直す (KD_MenuSettings が居れば)。シーンは保存しない。</summary>
        [MenuItem(HillsideMenu + "16 Setup Volume")]
        public static void SetupVolumeHillside()
        {
            KD_Hillside.Guard();
            var log = new StringBuilder("[KD_DogVoice] Setup Volume (裏山 段階 3: 犬の声のみ)\n");
            var go = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName, out int count);
            var pouch = go != null ? go.GetComponent<KD_TreatPouch>() : null;
            if (pouch == null || count != 1 || pouch.panelRoot == null || pouch.sectionTitleLabel == null) throw new InvalidOperationException("KD_TreatPouch missing (先に Tools/KominkaHillside/Menu/7 Build Menu)");
            var font = pouch.sectionTitleLabel.font;
            KD_ShopSetupEditor.AddGlyphs(KD_TreatSetupEditor.PouchWords(), log);
            EnsureHillsideVolume(pouch, font, log);
            KD_MenuFinishEditor.RebuildIfPresent(pouch, log);
            EditorUtility.SetDirty(pouch);
            UdonSharpEditorUtility.CopyProxyToUdon(pouch);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Directory.CreateDirectory(Path.GetDirectoryName(HillsideSetupLog));
            File.WriteAllText(HillsideSetupLog, log.ToString());
            Debug.Log(log.ToString());
        }

        /// <summary>Menu/16a。古民家の検査本体 (犬と音源の配線・既定 50%・Slider のイベント・音量の比の保存・OFF/ON) を root = KH_Dogs で通し、
        /// 裏山の条件 (6 匹・BGM/ライブ/寝室の行と参照が無い・入口の表示と配線・注記の文言) を足す。結果は Temp/menu-rebuild/volume-verify.txt。</summary>
        [MenuItem(HillsideMenu + "16a Verify Dog Voice Settings")]
        public static void VerifyHillside()
        {
            var log = new StringBuilder("[KD_DogVoice] Verify (裏山 段階 3)\nPROJECT " + Application.dataPath + "\n");
            int failed = 0;
            Action<bool, string> check = (ok, label) => { log.AppendLine((ok ? "PASS " : "FAIL ") + label); if (!ok) failed++; };
            try
            {
                VerifyCore(KD_Hillside.DogsRootName, log, check);
                var pouch = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName)?.GetComponent<KD_TreatPouch>();
                if (pouch != null)
                {
                    check(pouch.voiceDogs != null && pouch.voiceDogs.Length == KD_DogDefs.All.Length, "six dogs wired (" + (pouch.voiceDogs == null ? 0 : pouch.voiceDogs.Length) + ")");
                    int barks = 0;
                    if (pouch.voiceDogs != null) foreach (var dog in pouch.voiceDogs) if (dog != null && dog.barkSource != null) barks++;
                    check(barks == KD_DogDefs.All.Length && pouch.dogVoiceSources != null && pouch.dogVoiceSources.Length >= barks, "bark source per dog (" + barks + "), sources=" + (pouch.dogVoiceSources == null ? 0 : pouch.dogVoiceSources.Length));
                    int joys = 0;
                    foreach (var pet in Resources.FindObjectsOfTypeAll<KD_DogPetController>())
                        if (pet.gameObject.scene == pouch.gameObject.scene && pet.transform.root.name == KD_Hillside.DogsRootName && pet.joyAudioSource != null) joys++;
                    log.AppendLine("NOTE KD_PetSystem_* joyAudioSource wired=" + joys + " (古民家の構築も joyAudioSource を配らない。null の犬は撫での声が出ないのではなく、声は bark 経由)");
                    var audio = pouch.volumeSettingsPage;
                    check(audio != null && audio.name == "VolumeSettings" && audio.transform.parent == pouch.settingsPage.transform && !audio.activeSelf, "volume page Page_Settings/VolumeSettings (hidden)");
                    if (audio != null)
                    {
                        foreach (string name in HillsideRetiredRows) check(audio.transform.Find(name) == null, "no " + name + " row (BGM / live / bedroom are not built)");
                        check(audio.GetComponentsInChildren<Slider>(true).Length == 1, "exactly one slider on the volume page (dog voice)");
                        check(audio.transform.Find("OutsideHeading") != null && audio.transform.Find("VolumeScope") != null, "heading 外 and scope note");
                        var back = audio.transform.Find("BackSettingsButton")?.GetComponent<Button>();
                        check(back != null && ClickTarget(back, UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch), "_OpenSettings"), "back -> _OpenSettings");
                    }
                    check(pouch.bgmSlider == null && pouch.bgmValueLabel == null && pouch.liveVolumeSlider == null && pouch.liveVolumeValueLabel == null
                        && pouch.bedroomBgmVolumeSlider == null && pouch.bedroomVoiceVolumeSlider == null && pouch.bedroomLiveVolumeSlider == null
                        && pouch.bedroomBgmVolumeValueLabel == null && pouch.bedroomVoiceVolumeValueLabel == null && pouch.bedroomLiveVolumeValueLabel == null,
                        "BGM / live / bedroom references stay null on KD_TreatPouch");
                    check(pouch.sleepAudio == null && pouch.bgmPlayer == null, "no sleep audio / BGM player (voice gain = slider value)");
                    var entry = pouch.settingsHome.transform.Find("VolumeSettingsButton");
                    check(entry != null && entry.gameObject.activeSelf && ClickTarget(entry.GetComponent<Button>(), UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch), "_OpenVolumeSettings"),
                        "settings home entry shown -> _OpenVolumeSettings");
                    check(entry != null && Vector2.Distance(((RectTransform)entry).anchoredPosition, KD_MenuFinishEditor.HillsideVolumeEntryPos) < .01f, "entry at the settings home layout (" + KD_MenuFinishEditor.HillsideVolumeEntryPos + ")");
                    var language = pouch.language;
                    var scope = audio == null ? null : audio.transform.Find("VolumeScope")?.GetComponent<TMP_Text>();
                    int bound = language == null || scope == null ? -1 : Array.IndexOf(language.fixedLabels, scope);
                    check(bound >= 0 && language.fixedKeys[bound] == HillsideScopeKey, "scope note bound to " + HillsideScopeKey + " (BGM の無い文言)");
                }
            }
            catch (InvalidOperationException e) { check(false, e.Message); }
            log.AppendLine("RESULT " + (failed == 0 ? "PASS" : "FAIL") + " failures=" + failed);
            Directory.CreateDirectory(Path.GetDirectoryName(HillsideVerifyLog));
            File.WriteAllText(HillsideVerifyLog, log.ToString());
            if (failed > 0) Debug.LogError("[KD_DogVoice] Verify (裏山) failed: " + failed + " (" + HillsideVerifyLog + ")");
            else Debug.Log("[KD_DogVoice] Verify (裏山) PASS (" + HillsideVerifyLog + ")");
        }

        static bool ClickTarget(Button button, VRC.Udon.UdonBehaviour target, string evt)
        {
            if (button == null) return false;
            var calls = new SerializedObject(button).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            return calls.arraySize == 1 && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Target").objectReferenceValue == target
                && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == evt;
        }
    }
}
