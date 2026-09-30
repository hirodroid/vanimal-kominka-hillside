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
    /// <summary>裏山改変 (IMP-UI-001 段階 3 → IMP-WLD-003): 音量の頁は **BGM + 犬の声** (ライブは DES-LIV-001 の後、寝室は裏山に無い)。
    /// 頁は古民家と同じ Page_Settings/VolumeSettings で、「外」の 1 群 = BGM の行 ([ON/OFF] 付き) + 犬の声の行 + 注記 2 行 + [設定へ戻る]。
    /// 音源は古民家と同じ集め方 (KD_DogWanderController の bark / breath、KD_DogPetController の joyAudioSource) で、犬の root を KH_Dogs に読み替える。
    /// BGM の本体 (KH_Dogs/KD_Bgm の AudioSource + KD_BgmPlayer) は Course/98 (KH_BgmEditor) が作る。ここは居れば pouch.bgmPlayer へ配線する
    /// (古民家の「両方向から張る」と同じ。どちらを先に流しても揃う)。
    /// KD_TreatPouch の liveVolumeSlider / bedroom* / sleepAudio は null のまま (Apply / Redraw は null で何もしない)。</summary>
    public static partial class KD_DogVoiceSettingsEditor
    {
        const string HillsideMenu = "Tools/KominkaHillside/Menu/";
        const string HillsideSetupLog = "Temp/menu-rebuild/volume-setup.txt";
        const string HillsideVerifyLog = "Temp/menu-rebuild/volume-verify.txt";
        /// <summary>裏山で作らない行 (古民家のライブ / 寝室の列)。以前の構築 (0-b は古民家の 6 本で作った) で残っていれば消す。
        /// IMP-WLD-003 で BGM の 3 部品 (BgmTitle / BgmValue / BgmSlider) はここから外し、作る側へ移した。</summary>
        static readonly string[] HillsideRetiredRows =
        {
            "LiveVolumeTitle", "LiveVolumeValue", "LiveVolumeSlider",
            "BedroomHeading", "BedroomBgmVolumeTitle", "BedroomBgmVolumeValue", "BedroomBgmVolumeSlider",
            "BedroomVoiceVolumeTitle", "BedroomVoiceVolumeValue", "BedroomVoiceVolumeSlider",
            "BedroomLiveVolumeTitle", "BedroomLiveVolumeValue", "BedroomLiveVolumeSlider", "BedroomNote",
        };
        /// <summary>「外」の 1 群を頁の中央の列に置く (古民家は 2 列で外が左 x=-175)。行の間隔は古民家と同じ 88 (BGM 72 → 犬の声 -16)。
        /// 注記は 2 行 (共通の断り書き / BGM はライブ中に止まる)。下端は [設定へ戻る] (-196、高さ 44) の上。</summary>
        const float HillsideColumnX = 0f, HillsideHeadingY = 112f, HillsideBgmRowY = 72f, HillsideVoiceRowY = -16f, HillsideScopeY = -100f, HillsideBgmNoteY = -132f;
        /// <summary>BGM の [ON/OFF]。スライダー (幅 290、中心 x=0) の右、スライダーと同じ高さ (行の y - 36)。高さ 52 はスライダーのつまみと同じ (VR で押せる大きさ)。</summary>
        public static readonly Vector2 HillsideBgmTogglePos = new Vector2(215f, HillsideBgmRowY - 36f), HillsideBgmToggleSize = new Vector2(100f, 52f);
        public const string HillsideBgmToggleName = "BgmToggle", HillsideBgmNoteName = "BgmNote";
        /// <summary>BGM の注記の文言キー。古民家の KD_TreatPouch.WordBgmNote「ライブ中は自動で止まります」(13 言語が文言表にある) をそのまま使う。
        /// 束縛は Data/UILocalizationBindings.json の Page_Settings/VolumeSettings/BgmNote。</summary>
        public const string HillsideBgmNoteKey = "KD_TreatPouch.WordBgmNote";
        /// <summary>注記の文言キー (keys-U1.json)。古民家の KD_TreatPouch.WordVolumeScope「自分だけ・BGMはライブ中に停止」は 0% の説明が無いので、
        /// 裏山は「自分だけに反映・0% で消音」+ BGM の注記 (上) の 2 行にする。</summary>
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
                if (old != null) { Undo.DestroyObjectImmediate(old.gameObject); log.AppendLine("   removed VolumeSettings/" + name + " (裏山: ライブ・寝室の行は無い)"); }
            }
            pouch.liveVolumeSlider = null; pouch.liveVolumeValueLabel = null;
            pouch.bedroomBgmVolumeSlider = null; pouch.bedroomBgmVolumeValueLabel = null;
            pouch.bedroomVoiceVolumeSlider = null; pouch.bedroomVoiceVolumeValueLabel = null;
            pouch.bedroomLiveVolumeSlider = null; pouch.bedroomLiveVolumeValueLabel = null;
            pouch.dogVoiceToggleImage = null; pouch.dogVoiceToggleLabel = null;
            pouch.sleepAudio = null;

            Label(audio, "OutsideHeading", new Vector2(HillsideColumnX, HillsideHeadingY), new Vector2(290f, 36f), 24f, KD_TreatPouch.WordOutside, font);
            // IMP-WLD-003: BGM の行。題字「BGM」は古民家と同じく固定表記 (束縛しない)。スライダーは犬の声と同じ工場 (既定 50%、_BgmChanged)
            pouch.bgmSlider = BuildVolumeRow(audio, "BgmTitle", "BgmValue", "BgmSlider",
                KD_TreatPouch.WordBgm, HillsideColumnX, HillsideBgmRowY, .5f, "_BgmChanged", font, round, udon, log, out var bgmValue);
            pouch.bgmValueLabel = bgmValue;
            // [ON/OFF]: KD_TreatPouch._ToggleBgm。表示 (ON / OFF と色) は実行時に RedrawBgmSettings が毎回書くので、ここは既定 (ON) の見た目だけ焼く。
            // 文字は "ON" / "OFF" の固定表記 (KD_TreatPouch.WordVoiceOn / Off。束縛しない = 言語切替で上書きされない)
            var toggle = KD_ShopSetupEditor.EnsureButton(audio, HillsideBgmToggleName, HillsideBgmTogglePos, HillsideBgmToggleSize,
                pouch.equipOnColor, font, KD_TreatPouch.WordVoiceOn, udon, "_ToggleBgm", log);
            pouch.bgmToggleImage = toggle.GetComponent<Image>();
            pouch.bgmToggleLabel = toggle.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            pouch.bgmToggleLabel.fontSize = 22f;
            EditorUtility.SetDirty(pouch.bgmToggleLabel);
            pouch.dogVoiceSlider = BuildVolumeRow(audio, "VoiceTitle", "VolumeValue", "VoiceSlider",
                KD_TreatPouch.WordDogVoice, HillsideColumnX, HillsideVoiceRowY, .5f, "_DogVoiceChanged", font, round, udon, log, out var voiceValue);
            pouch.dogVoiceValueLabel = voiceValue;
            var scope = Label(audio, "VolumeScope", new Vector2(HillsideColumnX, HillsideScopeY), new Vector2(560f, 26f), 18f,
                KD_Hillside.Text(HillsideScopeKey, "自分だけに反映・0% で消音", log), font);
            scope.enableWordWrapping = true;
            scope.alignment = TextAlignmentOptions.Center;
            var bgmNote = Label(audio, HillsideBgmNoteName, new Vector2(HillsideColumnX, HillsideBgmNoteY), new Vector2(560f, 26f), 18f,
                KD_Hillside.Text(HillsideBgmNoteKey, KD_TreatPouch.WordBgmNote, log), font);
            bgmNote.enableWordWrapping = true;
            bgmNote.alignment = TextAlignmentOptions.Center;
            // BGM の本体 (Course/98 が作る)。居なければ null のまま (スライダーは値を覚えるだけで音は無い)
            var bgmGo = KD_SceneObjects.Find(KD_DogDefs.BgmName, out int bgmCount);
            var bgmPlayer = bgmGo != null && bgmCount == 1 ? bgmGo.GetComponent<KD_BgmPlayer>() : null;
            pouch.bgmPlayer = bgmPlayer;
            if (bgmPlayer == null) log.AppendLine("   WARN " + KD_DogDefs.BgmName + " が無い (" + bgmCount + " 個) — BGM の行は作ったが音源は未配線。Tools/KominkaHillside/Course/98 - Build BGM を流す");
            else log.AppendLine("   BGM: pouch.bgmPlayer -> " + KD_SceneObjects.PathOf(bgmGo));

            CollectHillsideVoiceSources(pouch, log);
            audioGo.SetActive(wasActive);
            EditorUtility.SetDirty(pouch);
            log.AppendLine("Volume (裏山: BGM + 犬の声): bgmPlayer=" + (pouch.bgmPlayer != null));
        }

        /// <summary>犬の声の台帳 (pouch.voiceDogs / dogVoiceSources / dogVoiceBaseVolumes) を集め直す (冪等)。Menu/16 と Wash/11 (KH_DogWashSetupEditor.Dog) から呼ぶ。
        /// 音源: 古民家と同じ (吠え声 = bark、息 = breath は犬が持つ、撫でた時の喜び声 = joyAudioSource)。root を KH_Dogs に読み替える。
        /// IMP-DOG-028: 洗浄の音 (KH_DogWash の contactFur / contactFoam / scrub、KH_WashTool の sprayAudio) も犬の声へ載せる
        /// (設計書 §8 の既定: 「効果音」の行は作らない)。base はビルド時の volume。洗浄のループは volume 0 で焼かれ、実行時に
        /// KH_DogWash が「目標の音量 × 犬の localVoiceGain」を書く (台帳の Apply はその 0 × gain と mute を書くだけ)。噴射は Play / Stop だけなので
        /// base 0.7 × gain がそのまま効く。焼いた mute は常に true (KD_TreatPouch.Start → ApplyDogVoiceSettings が決める)。
        /// proxy だけ書く (backing への写しは呼び手: Menu/16 は CopyProxyToUdon、Wash/11 は Copy)。</summary>
        internal static void CollectHillsideVoiceSources(KD_TreatPouch pouch, StringBuilder log)
        {
            var dogs = new List<KD_DogWanderController>();
            var sources = new List<AudioSource>();
            int joy = 0, wash = 0;
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
            foreach (var s in HillsideWashSources(pouch.gameObject.scene))
                if (!sources.Contains(s)) { sources.Add(s); wash++; }
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
            EditorUtility.SetDirty(pouch);
            log.AppendLine("Voice ledger: dogs=" + dogs.Count + " sources=" + sources.Count + " (joy=" + joy + ", wash=" + wash + ")");
        }

        /// <summary>IMP-DOG-028: 洗浄の音源 (シーン内の KH_DogWash 3 本ずつ + KH_WashTool の sprayAudio)。null は除く。</summary>
        static List<AudioSource> HillsideWashSources(UnityEngine.SceneManagement.Scene scene)
        {
            var list = new List<AudioSource>();
            foreach (var w in Resources.FindObjectsOfTypeAll<KominkaHillside.KH_DogWash>())
            {
                if (w.gameObject.scene != scene) continue;
                foreach (var s in new[] { w.contactFur, w.contactFoam, w.scrub }) if (s != null && !list.Contains(s)) list.Add(s);
            }
            foreach (var t in Resources.FindObjectsOfTypeAll<KominkaHillside.KH_WashTool>())
                if (t.gameObject.scene == scene && t.sprayAudio != null && !list.Contains(t.sprayAudio)) list.Add(t.sprayAudio);
            return list;
        }

        /// <summary>Menu/16。前提: Menu/7 Build Menu (BGM を鳴らすには Course/98 も)。音量の頁を揃え、設定面を組み直して文言を束縛し直す (KD_MenuSettings が居れば)。シーンは保存しない。</summary>
        [MenuItem(HillsideMenu + "16 Setup Volume")]
        public static void SetupVolumeHillside()
        {
            KD_Hillside.Guard();
            var log = new StringBuilder("[KD_DogVoice] Setup Volume (裏山: BGM + 犬の声)\n");
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
        /// 裏山の条件 (6 匹・BGM の行 (スライダー / 値 / [ON/OFF] / 注記) と KD_Bgm への配線・ライブ/寝室の行と参照が無い・入口の表示と配線・注記の文言) を足す。
        /// 結果は Temp/menu-rebuild/volume-verify.txt。BGM の音源と取り込み設定そのものは Course/99 が見る。</summary>
        [MenuItem(HillsideMenu + "16a Verify Dog Voice Settings")]
        public static void VerifyHillside()
        {
            var log = new StringBuilder("[KD_DogVoice] Verify (裏山 段階 3)\nPROJECT " + Application.dataPath + "\n");
            int failed = 0;
            Action<bool, string> check = (ok, label) => { log.AppendLine((ok ? "PASS " : "FAIL ") + label); if (!ok) failed++; };
            try
            {
                // IMP-DOG-028: 古民家の検査本体は台帳 = bark + joy と数える。裏山は洗浄の音も載せるので、その 2 行だけ下の検査に置き換える
                // (本体の他の行はそのまま通す)
                Action<bool, string> core = (ok, label) =>
                {
                    if (label.StartsWith("all one-shot voice sources wired") || label.StartsWith("base volumes match sources"))
                    { log.AppendLine("NOTE (裏山: 洗浄の音を含めて下で検査) " + label); return; }
                    check(ok, label);
                };
                VerifyCore(KD_Hillside.DogsRootName, log, core);
                var pouch = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName)?.GetComponent<KD_TreatPouch>();
                if (pouch != null)
                {
                    // 台帳 = bark + joy + 洗浄 (KH_DogWash 3 本 / KH_WashTool の噴射)。proxy と backing (実行時に読む側) の両方
                    var expected = new HashSet<AudioSource>();
                    foreach (var dog in Resources.FindObjectsOfTypeAll<KD_DogWanderController>())
                        if (dog.gameObject.scene == pouch.gameObject.scene && dog.transform.root.name == KD_Hillside.DogsRootName && dog.barkSource != null) expected.Add(dog.barkSource);
                    foreach (var pet in Resources.FindObjectsOfTypeAll<KD_DogPetController>())
                        if (pet.gameObject.scene == pouch.gameObject.scene && pet.transform.root.name == KD_Hillside.DogsRootName && pet.joyAudioSource != null) expected.Add(pet.joyAudioSource);
                    var washSources = HillsideWashSources(pouch.gameObject.scene);
                    foreach (var s in washSources) expected.Add(s);
                    check(pouch.dogVoiceSources != null && new HashSet<AudioSource>(pouch.dogVoiceSources).SetEquals(expected), "voice ledger = bark + joy + wash sources (" + expected.Count + ", wash " + washSources.Count + ")");
                    check(pouch.dogVoiceBaseVolumes != null && pouch.dogVoiceSources != null && pouch.dogVoiceBaseVolumes.Length == pouch.dogVoiceSources.Length, "base volumes match sources");
                    int washRegistered = 0, washBaseOk = 0;
                    if (pouch.dogVoiceSources != null && pouch.dogVoiceBaseVolumes != null)
                        for (int i = 0; i < pouch.dogVoiceSources.Length && i < pouch.dogVoiceBaseVolumes.Length; i++)
                        {
                            var s = pouch.dogVoiceSources[i];
                            if (s == null || !washSources.Contains(s)) continue;
                            washRegistered++;
                            if (Mathf.Approximately(pouch.dogVoiceBaseVolumes[i], s.volume)) washBaseOk++;
                        }
                    check(washRegistered == washSources.Count, "every wash sound on the ledger (" + washRegistered + "/" + washSources.Count + "; 0/0 = no wash built yet)");
                    check(washBaseOk == washRegistered, "wash base volume = the baked volume (" + washBaseOk + "/" + washRegistered + "; the loops are 0, the spray 0.7)");
                    object rawSources = null;
                    var backingPouch = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
                    var stored = backingPouch != null && backingPouch.publicVariables.TryGetVariableValue("dogVoiceSources", out rawSources) ? rawSources as Array : null;
                    int storedWash = 0;
                    if (stored != null) foreach (var o in stored) if (o is AudioSource a && washSources.Contains(a)) storedWash++;
                    check(stored != null && stored.Length == (pouch.dogVoiceSources == null ? -1 : pouch.dogVoiceSources.Length) && storedWash == washSources.Count,
                        "backing Udon holds the ledger incl. wash (" + (stored == null ? "null" : stored.Length + " sources, wash " + storedWash) + ")");
                    foreach (var s in washSources) check(s.mute, "wash sound baked muted (the pouch unmutes at Start): " + KD_SceneObjects.PathOf(s.gameObject));
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
                    var pouchUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
                    if (audio != null)
                    {
                        foreach (string name in HillsideRetiredRows) check(audio.transform.Find(name) == null, "no " + name + " row (live / bedroom are not built)");
                        check(audio.GetComponentsInChildren<Slider>(true).Length == 2, "exactly two sliders on the volume page (BGM, dog voice)");
                        check(audio.transform.Find("OutsideHeading") != null && audio.transform.Find("VolumeScope") != null, "heading 外 and scope note");
                        var back = audio.transform.Find("BackSettingsButton")?.GetComponent<Button>();
                        check(back != null && ClickTarget(back, pouchUdon, "_OpenSettings"), "back -> _OpenSettings");
                        // IMP-WLD-003: BGM の行 (スライダー + 値 + [ON/OFF] + 注記)
                        var bgm = pouch.bgmSlider;
                        check(bgm != null && bgm.name == "BgmSlider" && bgm.transform.parent == audio.transform && bgm.minValue == 0f && bgm.maxValue == 1f && !bgm.wholeNumbers && Mathf.Approximately(bgm.value, .5f),
                            "BGM slider on the volume page (0..1 continuous, default 50%)");
                        check(bgm != null && SliderTarget(bgm, pouchUdon, "_BgmChanged"), "BGM slider -> _BgmChanged once");
                        check(pouch.bgmValueLabel != null && pouch.bgmValueLabel.transform.parent == audio.transform && pouch.bgmValueLabel.text == "50%", "BGM value label 50%");
                        var toggle = audio.transform.Find(HillsideBgmToggleName);
                        check(toggle != null && ClickTarget(toggle.GetComponent<Button>(), pouchUdon, "_ToggleBgm"), "BGM toggle -> _ToggleBgm");
                        check(toggle != null && pouch.bgmToggleImage == toggle.GetComponent<Image>() && pouch.bgmToggleLabel == toggle.Find("Label")?.GetComponent<TextMeshProUGUI>()
                            && pouch.bgmToggleLabel.text == KD_TreatPouch.WordVoiceOn, "BGM toggle image / label wired (ON)");
                        var note = audio.transform.Find(HillsideBgmNoteName)?.GetComponent<TMP_Text>();
                        int noteAt = pouch.language == null || note == null ? -1 : Array.IndexOf(pouch.language.fixedLabels, note);
                        check(noteAt >= 0 && pouch.language.fixedKeys[noteAt] == HillsideBgmNoteKey, "BGM note bound to " + HillsideBgmNoteKey);
                    }
                    check(pouch.liveVolumeSlider == null && pouch.liveVolumeValueLabel == null
                        && pouch.bedroomBgmVolumeSlider == null && pouch.bedroomVoiceVolumeSlider == null && pouch.bedroomLiveVolumeSlider == null
                        && pouch.bedroomBgmVolumeValueLabel == null && pouch.bedroomVoiceVolumeValueLabel == null && pouch.bedroomLiveVolumeValueLabel == null,
                        "live / bedroom references stay null on KD_TreatPouch");
                    check(pouch.sleepAudio == null, "no sleep audio (voice / BGM gain = slider value)");
                    var bgmGo = KD_SceneObjects.Find(KD_DogDefs.BgmName, out int bgmCount);
                    var bgmPlayer = bgmGo != null ? bgmGo.GetComponent<KD_BgmPlayer>() : null;
                    check(bgmCount == 1 && bgmPlayer != null && pouch.bgmPlayer == bgmPlayer, "pouch.bgmPlayer = " + KD_DogDefs.BgmName + " (count " + bgmCount + "。無ければ Course/98)");
                    object storedBgm = null;
                    check(pouchUdon != null && pouchUdon.publicVariables.TryGetVariableValue("bgmPlayer", out storedBgm) && storedBgm != null
                        && (Equals(storedBgm, bgmPlayer) || Equals(storedBgm, UdonSharpEditorUtility.GetBackingUdonBehaviour(bgmPlayer))), "backing Udon holds bgmPlayer");
                    var entry = pouch.settingsHome.transform.Find("VolumeSettingsButton");
                    check(entry != null && entry.gameObject.activeSelf && ClickTarget(entry.GetComponent<Button>(), UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch), "_OpenVolumeSettings"),
                        "settings home entry shown -> _OpenVolumeSettings");
                    check(entry != null && Vector2.Distance(((RectTransform)entry).anchoredPosition, KD_MenuFinishEditor.HillsideVolumeEntryPos) < .01f, "entry at the settings home layout (" + KD_MenuFinishEditor.HillsideVolumeEntryPos + ")");
                    var language = pouch.language;
                    var scope = audio == null ? null : audio.transform.Find("VolumeScope")?.GetComponent<TMP_Text>();
                    int bound = language == null || scope == null ? -1 : Array.IndexOf(language.fixedLabels, scope);
                    check(bound >= 0 && language.fixedKeys[bound] == HillsideScopeKey, "scope note bound to " + HillsideScopeKey);
                }
            }
            catch (InvalidOperationException e) { check(false, e.Message); }
            log.AppendLine("RESULT " + (failed == 0 ? "PASS" : "FAIL") + " failures=" + failed);
            Directory.CreateDirectory(Path.GetDirectoryName(HillsideVerifyLog));
            File.WriteAllText(HillsideVerifyLog, log.ToString());
            if (failed > 0) Debug.LogError("[KD_DogVoice] Verify (裏山) failed: " + failed + " (" + HillsideVerifyLog + ")");
            else Debug.Log("[KD_DogVoice] Verify (裏山) PASS (" + HillsideVerifyLog + ")");
        }

        static bool SliderTarget(Slider slider, VRC.Udon.UdonBehaviour target, string evt)
        {
            var calls = new SerializedObject(slider).FindProperty("m_OnValueChanged.m_PersistentCalls.m_Calls");
            return calls.arraySize == 1 && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Target").objectReferenceValue == target
                && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == evt;
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
