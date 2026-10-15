using Catalog = KominkaDogRun.Editor.KD_LocalizationEditor.Catalog;
using System;
using Object = UnityEngine.Object;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.Udon;

namespace KominkaDogRun.Editor
{
    // IMP-INF-009 / menu finishing: read-only inventory is deliberately separate from setup.
    // 裏山改変: 設定面の段階 3 (群の見出し・並び・クレジット頁・インフォメーション・Verify 22 の裏山検査) は KD_MenuFinishEditor.Hillside.cs (partial)
    public static partial class KD_MenuFinishEditor
    {
        public const string CatalogPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Data/UITranslations.json";
        public const string RadioXUrl = "https://x.com/hobo_radioVRC";
        static readonly string[] Systems = { "KD_Language", "KD_MenuSettings", "KD_LiveAccess" };
        [Serializable] public class Entry { public string key; public string text; }
        [Serializable] public class Inventory { public Entry[] entries; }

        [MenuItem("Tools/KominkaHillside/Menu/10 Collect Words")]
        public static void CreatePrograms()
        {
            foreach (string name in Systems)
            {
                string path = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/" + name;
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path + ".asset") != null) continue;
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path + ".cs");
                AssetDatabase.CreateAsset(asset, path + ".asset");
            }
            AssetDatabase.SaveAssets();
        }

        static T System<T>(string name, VRC.SDKBase.Networking.SyncType sync) where T : UdonSharpBehaviour
        {
            var go = KD_SceneObjects.Find(name, out int count);
            if (count > 1) throw new InvalidOperationException("Duplicate " + name);
            // 裏山改変: 器は KH_Dogs 直下 ("====KominkaDogRun====/KD_Systems" は作らない)
            if (go == null) { go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Create " + name); KD_SceneObjects.PlaceIfNew(go, KD_Hillside.DogsRootName); }
            var component = go.GetComponent<T>();
            if (component == null) component = go.AddUdonSharpComponent<T>();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(component);
            backing.SyncMethod = sync;
            EditorUtility.SetDirty(backing);
            return component;
        }

        [MenuItem("Tools/KominkaHillside/Menu/11 Setup Localization")]
        public static void Setup()
        {
            if (EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().path != KD_Hillside.ScenePath /* 裏山改変: シーンの正本は KD_Hillside */)
                throw new InvalidOperationException("Open the target scene in Edit mode");
            var pouch = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            if (pouch.settingsHome == null) throw new InvalidOperationException("Settings home missing");
            // 裏山改変: 購入アセット (HoboRadio / SleepKit) は裏山に無い
#if KH_WITH_HOBORADIO
            HoboRadio_Controller radio = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<HoboRadio_Controller>())
                if (candidate.gameObject.scene == pouch.gameObject.scene)
                { if (radio != null) throw new InvalidOperationException("Multiple radios"); radio = candidate; }
            if (radio == null || new SerializedObject(radio).FindProperty("isGlobal").boolValue)
                throw new InvalidOperationException("Exactly one Local radio is required");
            VolumeSlider volume = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<VolumeSlider>())
                if (candidate.gameObject.scene == pouch.gameObject.scene && candidate.targetAudio != null && candidate.targetAudio.transform.IsChildOf(radio.transform))
                { if (volume != null) throw new InvalidOperationException("Multiple radio volume controls"); volume = candidate; }
            if (volume == null || volume.targetAudio == null || volume.knob == null) throw new InvalidOperationException("Radio volume wiring missing");
#else
            Debug.Log("[KD_MenuFinish] ラジオ未導入 (裏山): HoboRadio / VolumeSlider の配線を飛ばす");
#endif
            var catalog = KD_LocalizationEditor.Load();
            var log = new StringBuilder();
            if (pouch.sectionTitleLabel == null) throw new InvalidOperationException("Pouch panel missing: run Tools/KominkaHillside/Menu/7 Build Menu first");
            var language = System<KD_Language>(Systems[0], VRC.SDKBase.Networking.SyncType.None);
            var settings = System<KD_MenuSettings>(Systems[1], VRC.SDKBase.Networking.SyncType.None);
            var access = System<KD_LiveAccess>(Systems[2], VRC.SDKBase.Networking.SyncType.Manual);
            KD_LocalizationEditor.Configure(language,catalog,log);
            // Credit restores the source font before adding; self-language catalog glyphs are explicitly declared (client languages are drawn by the VRChat fallback).
            // 裏山改変: クレジット板は裏山に無い (段階 3)。元 TTF の差し直しだけ行う
            KD_TreatSetupEditor.RestoreFontSource(log);
            KD_ShopSetupEditor.AddGlyphs(KD_LocalizationEditor.BundledCharacters(catalog), log);
            var font = pouch.sectionTitleLabel.font;
            language.pouch = pouch; settings.pouch = pouch; settings.language = language; settings.liveAccess = access;
            // IMP-NET-007: 開催許可の変更権限は KD_SleepBoard.CanControl (オーナー優先、不在時だけ Master) を共通の正本にする。
            // 台帳が無ければ配線せず失敗させる (Master 判定で埋めない。Verify も同じ参照を数える)
            var board = KD_SceneObjects.Find(KD_DogDefs.SleepBoardName)?.GetComponent<KD_SleepBoard>();
            // 裏山改変: 段階 0 は KD_SleepBoard が意図的な未導入 (IMP-DOG-016 で必須に戻す)。無ければ配線せず、許可トグルは暗いまま
            if (board == null) log.AppendLine("NOTE 段階 0: " + KD_DogDefs.SleepBoardName + " は未導入 (Tools/KominkaHillside/Menu/6 Setup Sleep で入る)。access.sleepBoard は null");
            access.sleepBoard = board;
            pouch.language = language;
            // 裏山改変: 購入アセット (HoboRadio / SleepKit) は裏山に無い
#if KH_WITH_HOBORADIO
            settings.radio = UdonSharpEditorUtility.GetBackingUdonBehaviour(radio);
            settings.radioAudio = volume.targetAudio; settings.radioKnob = volume.knob; settings.knobMin = volume.minZ; settings.knobMax = volume.maxZ;
#else
            settings.radio = null; settings.radioAudio = null; settings.radioKnob = null;
#endif
            if (pouch.live != null) { pouch.live.access = access; pouch.live.hostingItems = pouch.liveItems; Copy(pouch.live); }
            foreach (var b in Resources.FindObjectsOfTypeAll<UdonSharpBehaviour>())
            {
                if (b.gameObject.scene != pouch.gameObject.scene || b.GetType().Namespace != "KominkaDogRun") continue;
                var field = b.GetType().GetField("language");
                if (field != null && field.FieldType == typeof(KD_Language)) { field.SetValue(b, language); Copy(b); }
                if (b is KD_ShopKeeper keeper) language.keeper = keeper;
                if (b is KD_ToyBox toyBox) language.toyBox = toyBox;
            }
            var shelves = new List<KD_ShopShelf>();
            foreach (var shelf in Resources.FindObjectsOfTypeAll<KD_ShopShelf>()) if (shelf.gameObject.scene == pouch.gameObject.scene) shelves.Add(shelf);
            language.shelves = shelves.ToArray();
            pouch.refreshSeconds = KD_ShopItem.ConfirmRefreshSeconds;
            foreach (var shelf in shelves) { shelf.refreshSeconds = KD_ShopItem.ConfirmRefreshSeconds; Copy(shelf); }
            BuildSettings(pouch, settings, language, font, log);
            // 裏山改変: クレジット板と写真ギャラリーは裏山に無い (BuildCreditRows / BuildGallery は呼ばない)
            KD_LocalizationEditor.Bind(language, catalog, log);
            Copy(pouch); Copy(settings); Copy(language); Copy(access);
            EditorSceneManager.MarkSceneDirty(pouch.gameObject.scene);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Temp/menu-finish");
            File.WriteAllText("Temp/menu-finish/setup.txt", log.ToString());
        }

        internal static void RebuildIfPresent(KD_TreatPouch pouch, StringBuilder log)
        {
            var go = KD_SceneObjects.Find("KD_MenuSettings");
            if (go == null) return;
            var settings = go.GetComponent<KD_MenuSettings>();
            BuildSettings(pouch, settings, settings.language, pouch.sectionTitleLabel.font, log);
            var catalog = KD_LocalizationEditor.Load();
            // 裏山改変: クレジット板と写真ギャラリーは裏山に無い
            KD_LocalizationEditor.Configure(settings.language,catalog,log);
            KD_LocalizationEditor.Bind(settings.language,catalog,log);
            Copy(settings); Copy(settings.language);
        }

        static void BuildSettings(KD_TreatPouch pouch, KD_MenuSettings settings, KD_Language language, TMP_FontAsset font, StringBuilder log)
        {
            KD_HandMenuScrollEditor.LayoutFrame(pouch);
            var parent = pouch.settingsHome.transform;
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(settings);
            var langUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(language);
            // 音量 / 距離フェードの入口は左列 (-180, 82) / (-180, 0)。位置はそれぞれの Setup が持つ
            // (Dog Voice Settings / Distance Fade Settings)。ここで上書きすると 2 本目の正本になり、
            // 片方だけ再実行したときに中央へ戻る (2026-09-22 実測: 距離フェードが X=0 へずれていた)
            // IMP-ENV-019: 文言は全端末で「ラジオ設定 (PCのみ)」(radio.settings.button)。押せるかは KD_MenuSettings.Start() が端末で決める
            // 裏山改変: ラジオは裏山に無い (D-5)。入口も頁も作らない (以前の構築で残っていれば片付ける)
            Retire(parent, "RadioSettingsButton", log);
            settings.radioButton = null;
            var retiredEntry=parent.Find("ShareSettingsButton");
            if(retiredEntry!=null)UnityEngine.Object.DestroyImmediate(retiredEntry.gameObject);
            // 裏山改変: 行の y は設定ホームの 3 群の配置 (KD_MenuFinishEditor.Hillside の Hillside*Y) が正本
            language.choiceLabel = Label(parent, "Language", new Vector2(-115,HillsideLanguageY), new Vector2(370,48), "", font, 24);
            var previous=parent.Find("Japanese");if(previous!=null)previous.name="PreviousLanguage";
            var next=parent.Find("English");if(next!=null)next.name="NextLanguage";
            Button(parent, "PreviousLanguage", new Vector2(145,HillsideLanguageY), new Vector2(100,58), "<", font, langUdon, "_PreviousLanguage", log);
            Button(parent, "NextLanguage", new Vector2(275,HillsideLanguageY), new Vector2(100,58), ">", font, langUdon, "_NextLanguage", log);
            // 裏山改変: ラジオ設定頁 (RadioSettings) は作らない。写真の共有 (BuildPhotoShare) も裏山に無い
            Retire(pouch.settingsPage.transform, "RadioSettings", log);
            settings.radioPage = null; settings.radioState = null; settings.radioVolume = null; settings.radioVolumeValue = null;
            var catalog=KD_LocalizationEditor.Load();
            // Keep pages other builders registered (GiftCodePage / PhotoSettings …). Resetting the array here
            // silently emptied the あそぶ・みる page (2026-09-13、当時は設定の面): KD_TreatPouch only activates pages listed here.
            var kept = new List<GameObject>();
            foreach (var p in pouch.extraSettingsPages ?? Array.Empty<GameObject>()) if (p != null && !kept.Contains(p)) kept.Add(p);
            pouch.extraSettingsPages = kept.ToArray();
            // 裏山改変 (段階 3): 0-b で非表示にした 音量 / 距離フェード / インフォメーションの入口を戻す。写真設定は裏山に無い (作らない・残っていれば消す)
            Retire(parent, "PhotoSettingsButton", log);
            Retire(pouch.settingsPage.transform, "PhotoSettings", log);
            settings.photoSettingsPage = null;
            var livePage = pouch.settingsHome.transform;
            // P5 (2026-09-16 ユーザー決定): 呼ぶの開放も「この部屋のみんな」の設定。ライブ開催許可と横に並べ、権限者は同じ規則 (オーナー優先、不在時 Master)
            var pouchUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
            var callOpen = Button(livePage,"CallAccess",new Vector2(-285,HillsideAccessY),new Vector2(540,44),KD_TreatPouch.WordOpenOn,font,pouchUdon,"_OpenToAll",log);
            pouch.openToAllButton = callOpen;
            pouch.openToAllButtonComponent = callOpen.GetComponent<Button>();
            pouch.openToAllImage = callOpen.GetComponent<Image>();
            pouch.openToAllLabel = callOpen.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            pouch.openToAllLabel.fontSize = 22;
            var open = Button(livePage,"LiveAccess",new Vector2(285,HillsideAccessY),new Vector2(540,44),KD_LocalizationEditor.Text(catalog,catalog.fallbackLanguage,"ui.extra.046"),font,udon,"_ToggleLiveAccess",log);
            settings.liveAccessButton = open.GetComponent<Button>();
            settings.liveAccessLabel = open.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            settings.liveAccessLabel.fontSize = 22;
            // IMP-NET-007: 補足は 1 行だけ (通常文言は「オーナー」。不在時の代行はここでしか説明しない)。文言は ui.extra.048 (Bind が差し替える)
            // 裏山改変: 補足は群「この部屋のみんな」の見出しの行の右 (モックの群見出しの副題)。文言・束縛は古民家のまま
            Label(livePage,"LiveAccessNote",HillsideAccessNotePos,HillsideAccessNoteSize,KD_LocalizationEditor.Text(catalog,catalog.fallbackLanguage,"ui.extra.048"),font,16).alignment = TextAlignmentOptions.Right;
            BuildNightMode(pouch, settings, font, log);
            // 裏山改変 (段階 3): 音量 (犬の声のみ)・距離フェード・クレジット・インフォメーションの入口と頁、3 群の見出しと並び
            BuildHillsideSettings(pouch, settings, font, log);
            log.AppendLine("Settings (裏山 段階 3): 3 groups (self / room / read): volume (dog voice only), distance fade (PC only), night mode -> KH_NightMode, language | call/live access | credits, information (radio / share / photo are not built)");
        }

        /// <summary>IMP-ENV-015: SleepKit のナイトモードを設定ホームの最下段に Slider で置く。
        /// SleepKit のタブレット呼び出しは止めてあるので、ここが唯一の入口。
        /// 値・保存は SleepKitSettingsManager (購入アセット) のまま — 自作側は Slider から値を渡すだけ。</summary>
        internal static void BuildNightMode(KD_TreatPouch pouch, KD_MenuSettings settings, TMP_FontAsset font, StringBuilder log)
        {
            // 裏山改変: 購入アセット (HoboRadio / SleepKit) は裏山に無い
#if KH_WITH_SLEEPKIT
            RBS.SleepKit2.Udon.SleepKitSettingsManager sleepKit = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<RBS.SleepKit2.Udon.SleepKitSettingsManager>())
                if (candidate.gameObject.scene == pouch.gameObject.scene)
                { if (sleepKit != null) throw new InvalidOperationException("Multiple SleepKitSettingsManager"); sleepKit = candidate; }
            if (sleepKit == null) throw new InvalidOperationException("Exactly one SleepKitSettingsManager is required");
#endif
            var catalog = KD_LocalizationEditor.Load();
            string T(string key) { return KD_LocalizationEditor.Text(catalog, catalog.fallbackLanguage, key); }
            var home = pouch.settingsHome.transform;
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(settings);
            // 裏山改変: 購入アセット (HoboRadio / SleepKit) は裏山に無い
#if KH_WITH_SLEEPKIT
            settings.sleepKit = sleepKit;
#endif
            // 裏山改変: 群「自分の見え方・聞こえ方」の 2 行目 (古民家は最下段 -262 / -306)。y と x の正本は KD_MenuFinishEditor.Hillside
            // IMP-ENV-005: 右に [オート][マニュアル] を足すため、題字・スライダー・値を左へ寄せた (HillsideNight*X)
            Label(home, "NightModeLabel", new Vector2(HillsideNightLabelX, HillsideNightY), HillsideNightLabelSize, T("settings.night.title"), font, 24).alignment = TextAlignmentOptions.Right;
            settings.nightMode = KD_DogVoiceSettingsEditor.BuildSlider(home, "NightMode", HillsideNightY, KD_UiSpriteEditor.LoadRound(), udon, "_NightMode", log);
            ((RectTransform)settings.nightMode.transform).anchoredPosition = new Vector2(HillsideNightSliderX, HillsideNightY);
            KD_DogVoiceSettingsEditor.SetInitialSliderValue(settings.nightMode, 0f);
            settings.nightModeValue = Label(home, "NightModeValue", new Vector2(HillsideNightValueX, HillsideNightY), HillsideNightValueSize, "OFF", font, 24);
            settings.nightModeValue.alignment = TextAlignmentOptions.Left;
            var manualNote = Label(home, "NightModeNote", new Vector2(0, HillsideNightNoteY), new Vector2(700, 26), T("settings.night.note"), font, 18);
            // IMP-ENV-005: モードの選択と、モードごとの注記 (同じ位置に 2 行。実行時は KD_MenuSettings.RefreshNightModeChoice が片方だけ出す)。
            // 初回のモードはオート (KH_NightMode._auto の既定) なので、構築時もオートの見た目を焼く
            var autoButton = KD_ShopSetupEditor.EnsureButton(home, NightAutoButtonName, new Vector2(HillsideNightAutoX, HillsideNightY), HillsideNightChoiceSize,
                pouch.equipOnColor, font, T("settings.night.auto"), udon, "_NightAuto", log);
            var manualButton = KD_ShopSetupEditor.EnsureButton(home, NightManualButtonName, new Vector2(HillsideNightManualX, HillsideNightY), HillsideNightChoiceSize,
                pouch.tabOffColor, font, T("settings.night.manual"), udon, "_NightManual", log);
            foreach (var b in new[] { autoButton, manualButton }) b.transform.Find("Label").GetComponent<TextMeshProUGUI>().fontSize = 22f;
            var autoNote = Label(home, NightAutoNoteName, new Vector2(0, HillsideNightNoteY), new Vector2(700, 26), T("settings.night.autoNote"), font, 18);
            autoNote.enableWordWrapping = false; autoNote.enableAutoSizing = true; autoNote.fontSizeMax = 18f; autoNote.fontSizeMin = KD_LocalizationEditor.ButtonFontSizeMin;
            autoNote.gameObject.SetActive(true); manualNote.gameObject.SetActive(false);
            settings.nightAutoImage = autoButton.GetComponent<Image>();
            settings.nightManualImage = manualButton.GetComponent<Image>();
            settings.nightAutoNote = autoNote.gameObject;
            settings.nightManualNote = manualNote.gameObject;
            settings.nightModeFill = settings.nightMode.fillRect != null ? settings.nightMode.fillRect.GetComponent<Image>() : null;
            if (settings.nightModeFill == null) throw new InvalidOperationException("Night mode slider fill Image missing (KD_DogVoiceSettingsEditor.BuildSlider)");
            // 裏山改変: スライダーの先は KH_NightMode (シーンに 1 個)。強度 0〜1 を KD_MenuSettings._NightMode が _SetDarkness で渡す
            settings.hillsideNight = HillsideNight(pouch.gameObject.scene);
            // 裏山改変: 購入アセット (HoboRadio / SleepKit) は裏山に無い
#if KH_WITH_SLEEPKIT
            log.AppendLine("Night mode: slider on settings home -> SleepKitSettingsManager (" + sleepKit.name + ")");
#else
            log.AppendLine("Night mode: slider on settings home -> KH_NightMode (" + settings.hillsideNight.name + "), [Auto][Manual] -> _NightAuto / _NightManual, notes " + NightAutoNoteName + " / NightModeNote (IMP-ENV-005)");
#endif
        }

        /// <summary>裏山改変: シーンにちょうど 1 個の KH_NightMode (Night/1 Setup が作る)。無い・複数なら止める (段階 0-b から必須)。</summary>
        internal static global::KominkaHillside.KH_NightMode HillsideNight(Scene scene)
        {
            global::KominkaHillside.KH_NightMode found = null; int count = 0;
            foreach (var n in Resources.FindObjectsOfTypeAll<global::KominkaHillside.KH_NightMode>())
                if (n.gameObject.scene == scene) { found = n; count++; }
            if (count != 1) throw new InvalidOperationException("Exactly one KH_NightMode is required in the scene (found " + count + "). Run Tools/KominkaHillside/Night/1 Setup Sky And Night");
            return found;
        }

        internal static void BuildInformation(KD_TreatPouch pouch, KD_MenuSettings settings, TMP_FontAsset font, StringBuilder log)
        {
            var catalog = KD_LocalizationEditor.Load();
            string T(string key) { return KD_LocalizationEditor.Text(catalog, catalog.fallbackLanguage, key); }
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(settings);
            // 裏山改変: 群「読む」の右 (古民家は (180,0) 320x64)。位置の正本は KD_MenuFinishEditor.Hillside
            Button(pouch.settingsHome.transform, "InformationButton", HillsideInformationEntryPos, HillsideReadEntrySize /* 裏山改変 IMP-WLD-017: 読む・書く の行は 260 */, T("information.title"), font, udon, "_OpenInformation", log);
            var info = KD_ShopSetupEditor.EnsureUiChild(pouch.settingsPage.transform, "Information", Vector2.zero, new Vector2(KD_TreatSetupEditor.PanelWidth,KD_TreatSetupEditor.PanelHeight));
            var ai = KD_ShopSetupEditor.EnsureUiChild(pouch.settingsPage.transform, "AiInformation", Vector2.zero, new Vector2(KD_TreatSetupEditor.PanelWidth,KD_TreatSetupEditor.PanelHeight));
            settings.informationPage = info; settings.aiInformationPage = ai;
            Button(info.transform, "AiInformationButton", new Vector2(0,100), new Vector2(640,70), T("information.ai.title"), font, udon, "_OpenAiInformation", log);
            Back(info.transform, pouch, font, log);
            ((RectTransform)info.transform.Find("BackSettings")).anchoredPosition = new Vector2(-380,-250);
            Button(ai.transform, "BackInformation", new Vector2(-310,-250), new Vector2(380,50), T("information.back"), font, udon, "_OpenInformation", log);
            var existing = ai.transform.Find("ScrollView");
            var scroll = existing != null ? existing.GetComponent<ScrollRect>() : KD_CreditSetupEditor.BuildScrollArea(ai.transform, "ScrollView", new Vector2(0,0), new Vector2(1000,400), 36,
                new Color(.12f,.11f,.10f,.8f), new Color(.20f,.18f,.16f,1), KD_UiSpriteEditor.ActionNeutral, out _);
            scroll.inertia = false;
            var body = Label(scroll.viewport, "Content", Vector2.zero, new Vector2(934,400), T("information.ai.body"), font, 24);
            body.alignment = TextAlignmentOptions.TopLeft; body.richText = false; body.raycastTarget = true;
            body.margin = new Vector4(12,12,12,12); body.isOrthographic = true;
            var rect = body.rectTransform; rect.anchorMin = new Vector2(0,1); rect.anchorMax = new Vector2(1,1); rect.pivot = new Vector2(.5f,1); rect.sizeDelta = new Vector2(0,400);
            var fitter = body.GetComponent<ContentSizeFitter>() ?? body.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = rect; scroll.verticalNormalizedPosition = 1f;
            settings.aiInformationScroll = scroll;
            var pages = new List<GameObject>(pouch.extraSettingsPages ?? Array.Empty<GameObject>());
            if (!pages.Contains(info)) pages.Add(info); if (!pages.Contains(ai)) pages.Add(ai);
            pouch.extraSettingsPages = pages.ToArray();
            info.SetActive(false); ai.SetActive(false);
            log.AppendLine("Information: optional AI disclosure page, 24pt scroll text");
            // 裏山改変: 写真設定の無い版 (はじめに / 生成AI / VAnimal / トレーナー)。古民家の KD_PhotoInformationEditor.BuildPages は写真設定の頁と入口を作るので呼ばない
            BuildInformationPagesHillside(pouch, settings, font, log);
        }

        internal static void BuildPhotoShare(KD_TreatPouch pouch,KD_Language language,TMP_FontAsset font,StringBuilder log)
        {
            var gallery=KD_SceneObjects.Find(KD_DogDefs.PhotoSystemName).transform.Find("Gallery");
            var caption=gallery.Find("Caption");
            if(caption==null)throw new InvalidOperationException("Photo caption missing");
            var canvasT=gallery.Find("ShareCanvas");
            var canvasGo=canvasT!=null?canvasT.gameObject:new GameObject("ShareCanvas",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster),typeof(VRC.SDK3.Components.VRCUiShape),typeof(BoxCollider));
            canvasGo.transform.SetParent(gallery,false);
            var rect=(RectTransform)canvasGo.transform;rect.sizeDelta=new Vector2(1280,360);
            rect.localScale=Vector3.one*.001f;rect.localRotation=Quaternion.identity;
            rect.localPosition=new Vector3(caption.localPosition.x+caption.localScale.x*.5f+.08f+.64f,caption.localPosition.y,-.025f);
            canvasGo.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            var collider=canvasGo.GetComponent<BoxCollider>();collider.isTrigger=true;collider.center=Vector3.zero;collider.size=new Vector3(1280,360,1);
            var legacy=pouch.settingsPage.transform.Find("ShareSettings");
            if(legacy!=null){legacy.SetParent(rect,false);legacy.name="Content";}
            var share=KD_ShopSetupEditor.EnsureUiChild(rect,"Content",Vector2.zero,new Vector2(1280,360));
            var back=share.transform.Find("BackSettings");if(back!=null)UnityEngine.Object.DestroyImmediate(back.gameObject);
            var bg=share.GetComponent<Image>()??share.AddComponent<Image>();bg.color=KD_UiSpriteEditor.SurfacePanel;bg.raycastTarget=false;KD_UiSpriteEditor.ApplyFlatSkin(bg);
            language.shareKey="share.post";var catalog=KD_LocalizationEditor.Load();
            Label(share.transform,"Title",new Vector2(0,136),new Vector2(1232,48),KD_LocalizationEditor.Text(catalog,catalog.fallbackLanguage,"ui.extra.034"),font,32).alignment=TextAlignmentOptions.Left;
            language.shareField=CopyField(share.transform,"Post",new Vector2(0,10),new Vector2(1232,152),KD_LocalizationEditor.Text(catalog,catalog.fallbackLanguage,"share.post"),font,true);
            language.shareField.pointSize=28;
            Label(share.transform,"CopyHint",new Vector2(0,-105),new Vector2(1232,36),KD_LocalizationEditor.Text(catalog,catalog.fallbackLanguage,"ui.extra.036"),font,24).alignment=TextAlignmentOptions.Left;
            Label(share.transform,"DeviceHint",new Vector2(0,-147),new Vector2(1232,32),KD_LocalizationEditor.Text(catalog,catalog.fallbackLanguage,"ui.extra.038"),font,22).alignment=TextAlignmentOptions.Left;
            foreach(var t in canvasGo.GetComponentsInChildren<Transform>(true))t.gameObject.layer=0;
            canvasGo.SetActive(true);share.SetActive(true);
            log.AppendLine("Photo share: right of Caption local="+rect.localPosition+" world size="+Vector3.Scale(new Vector3(1280,360,0),rect.lossyScale));
        }

        /// <summary>裏山改変: 作らないと決めた部品が以前の構築で残っていれば消す (作った側の後始末。冪等)。</summary>
        static void Retire(Transform parent, string name, StringBuilder log)
        {
            var t = parent == null ? null : parent.Find(name);
            if (t == null) return;
            Object.DestroyImmediate(t.gameObject);
            log.AppendLine("removed " + name + " (裏山では作らない)");
        }

        static void Back(Transform parent, KD_TreatPouch pouch, TMP_FontAsset font, StringBuilder log)
        { Button(parent,"BackSettings",new Vector2(-250,-196),new Vector2(180,44),"設定へ戻る",font,UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch),"_OpenSettings",log); }
        static GameObject Button(Transform parent,string name,Vector2 pos,Vector2 size,string text,TMP_FontAsset font,UdonBehaviour target,string evt,StringBuilder log)
        { return KD_ShopSetupEditor.EnsureButton(parent,name,pos,size,KD_UiSpriteEditor.ActionNeutral,font,text,target,evt,log); }
        static TextMeshProUGUI Label(Transform parent,string name,Vector2 pos,Vector2 size,string text,TMP_FontAsset font,float points)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent,name,pos,size);
            var label = go.GetComponent<TextMeshProUGUI>() ?? go.AddComponent<TextMeshProUGUI>();
            label.font = font; label.fontSize = points; label.text = text; label.color = new Color(.97f,.95f,.90f);
            label.alignment = TextAlignmentOptions.Center; label.enableWordWrapping = true; label.raycastTarget = false;
            return label;
        }
        public static TMP_InputField CopyField(Transform parent,string name,Vector2 pos,Vector2 size,string text,TMP_FontAsset font,bool multiline)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent,name,pos,size);
            var image = go.GetComponent<Image>() ?? go.AddComponent<Image>(); image.color = KD_UiSpriteEditor.SurfaceRaised; image.raycastTarget = true;
            var area = KD_ShopSetupEditor.EnsureUiChild(go.transform,"TextArea",Vector2.zero,size-new Vector2(28,12));
            if (area.GetComponent<RectMask2D>() == null) area.AddComponent<RectMask2D>();
            var label = Label(area.transform,"Text",Vector2.zero,size-new Vector2(28,12),text,font,24);
            label.alignment = TextAlignmentOptions.TopLeft; label.richText = false;
            var field = go.GetComponent<TMP_InputField>() ?? go.AddComponent<TMP_InputField>();
            field.targetGraphic = image; field.textViewport = (RectTransform)area.transform; field.textComponent = label; field.fontAsset = font;
            field.pointSize = 24; field.readOnly = true; field.richText = false; field.onFocusSelectAll = true;
            field.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            field.selectionColor = new Color(.30f,.55f,1f,.75f); field.customCaretColor = true; field.caretColor = Color.white;
            field.navigation = new Navigation { mode = Navigation.Mode.None }; field.text = text;
            return field;
        }

        public static void BuildCreditRows(StringBuilder log)
        {
            var board = KD_SceneObjects.Find(KD_DogDefs.CreditBoardName);
            if (board == null) throw new InvalidOperationException("Credit board missing");
            var canvas = board.transform.Find("Canvas");
            var scroll = canvas.Find("ScrollView").GetComponent<ScrollRect>();
            var old = scroll.content;
            if (old != null && old.name == "Body") UnityEngine.Object.DestroyImmediate(old.gameObject);
            var font = canvas.Find("Title").GetComponent<TMP_Text>().font;
            string[] names = { "犬モデル\nTORINOSUKEの家", "舞台モデル\nモサンゴ屋「庭付き古民家」", "フォント\nNoto Serif JP", "SIL Open Font License 1.1\nCopyright 2012 Google Inc. All Rights Reserved.", "動画プレイヤー\nVizVid (JLChnToZ, MIT License)" };
            string[] urls = { "https://torinosuke05.booth.pm/items", "https://booth.pm/ja/items/6364620", "https://fonts.google.com/noto/specimen/Noto+Serif+JP", "https://openfontlicense.org", "https://github.com/JLChnToZ/VVMW" };
            var content = KD_ShopSetupEditor.EnsureUiChild(scroll.viewport,"CreditRows",Vector2.zero,new Vector2(586,1050));
            var rect = (RectTransform)content.transform; rect.anchorMin = new Vector2(0,1); rect.anchorMax = new Vector2(1,1); rect.pivot = new Vector2(.5f,1); rect.sizeDelta = new Vector2(0,1050);
            scroll.content = rect; scroll.inertia = false;
            var viewportRect = scroll.viewport; viewportRect.sizeDelta = new Vector2(586,viewportRect.sizeDelta.y); viewportRect.anchoredPosition = new Vector2(-27,viewportRect.anchoredPosition.y);
            scroll.verticalScrollbar.GetComponent<RectTransform>().sizeDelta = new Vector2(40,scroll.GetComponent<RectTransform>().sizeDelta.y);
            scroll.verticalScrollbar.GetComponent<RectTransform>().anchoredPosition = new Vector2(300,0);
            scroll.verticalScrollbar.handleRect.sizeDelta = Vector2.zero;
            for (int i=0;i<names.Length;i++)
            {
                var label = Label(content.transform,"Name"+i,new Vector2(0,-60-i*200),new Vector2(558,100),names[i],font,26);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f,1);
                var field = CopyField(content.transform,"Url"+i,new Vector2(0,-145-i*200),new Vector2(558,52),urls[i],font,false);
                ((RectTransform)field.transform).anchorMin = ((RectTransform)field.transform).anchorMax = new Vector2(.5f,1);
            }
            var hint = Label(canvas,"CopyHint",new Vector2(0,272),new Vector2(640,40),"スクロールは右のバーで操作",font,22);
            scroll.GetComponent<RectTransform>().sizeDelta = new Vector2(640,660);
            scroll.GetComponent<RectTransform>().anchoredPosition = new Vector2(0,-90);
            viewportRect.sizeDelta = new Vector2(586,660);
            scroll.verticalScrollbar.GetComponent<RectTransform>().sizeDelta = new Vector2(40,660);
            scroll.verticalNormalizedPosition = 1;
            rect.anchoredPosition = Vector2.zero;
            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/KominkaHillside/Font/Noto_Serif_JP/static/NotoSerifJP-Regular.ttf");
            if (sourceFont == null) throw new InvalidOperationException("Credit source font missing");
            var so = new SerializedObject(font);
            so.FindProperty("m_SourceFontFile").objectReferenceValue = sourceFont;
            so.FindProperty("m_SourceFontFile_EditorRef").objectReferenceValue = sourceFont;
            so.ApplyModifiedPropertiesWithoutUndo();
            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            var characters = new StringBuilder();
            // IMP-INF-010: 文言表に接続済みのラベルには最後に Editor で適用した言語 (client 言語のこともある) の文が残る。
            // シーンから拾うのは未接続の固定文 (作者指定の表記・URL) だけにし、翻訳文は self 言語のカタログから集める
            var bound = new HashSet<TMP_Text>();
            var languageObject = KD_SceneObjects.Find("KD_Language");
            var boundLanguage = languageObject == null ? null : languageObject.GetComponent<KD_Language>();
            if (boundLanguage != null && boundLanguage.fixedLabels != null) foreach (var label in boundLanguage.fixedLabels) if (label != null) bound.Add(label);
            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>(true)) if (!bound.Contains(text)) characters.Append(text.text);
            characters.Append(KD_LocalizationEditor.BundledCharacters(KD_LocalizationEditor.Load()));
            var needed = new StringBuilder(); var seen = new HashSet<char>();
            foreach (char c in characters.ToString())
                if (!char.IsControl(c) && c != '\u200b' && seen.Add(c) && !KD_LocalizationEditor.HasGlyph(font,c)) needed.Append(c);
            log.AppendLine("Credit font: " + font.atlasWidth + "x" + font.atlasHeight + " glyphs=" + font.glyphTable.Count + " missing before add=" + needed.Length);
            try
            {
                if (needed.Length > 0)
                {
                    font.TryAddCharacters(needed.ToString(), out string missing);
                    if (!string.IsNullOrEmpty(missing)) throw new InvalidOperationException("Credit glyphs missing: " + missing);
                }
            }
            finally { font.atlasPopulationMode = AtlasPopulationMode.Static; }
            EditorUtility.SetDirty(font);
            log.AppendLine("Credit: five attribution/URL rows, 40px permanent scrollbar; placement retained");
        }

        public static void RebindIfPresent(StringBuilder log)
        {
            var language=KD_SceneObjects.Find("KD_Language")?.GetComponent<KD_Language>();
            if(language==null)return;
            var catalog=KD_LocalizationEditor.Load();
            // 裏山改変: クレジット板と写真ギャラリーは裏山に無い
            KD_LocalizationEditor.Configure(language,catalog,log);
            KD_LocalizationEditor.Bind(language,catalog,log);Copy(language);
        }
        static void Copy(UdonSharpBehaviour behaviour)
        { EditorUtility.SetDirty(behaviour); UdonSharpEditorUtility.CopyProxyToUdon(behaviour); }

        static void BuildGallery(KD_Language language,Catalog catalog,StringBuilder log)
        {
            var gallery=KD_SceneObjects.Find(KD_DogDefs.PhotoSystemName).transform.Find("Gallery");
            KD_PhotoSetupEditor.BuildGalleryText(gallery,language.pouch.sectionTitleLabel.font,catalog,log);
        }

        [MenuItem("Tools/KominkaHillside/Menu/10a Survey Menu Finish")]
        public static void Survey()
        {
            var entries = new List<Entry>();
            foreach (var type in typeof(KD_TreatPouch).Assembly.GetTypes())
            {
                if (type.Namespace != "KominkaDogRun" || type.Name.Contains("Debug")) continue;
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
                    var value = (string)field.GetRawConstantValue();
                    if (string.IsNullOrEmpty(value) || !HasJapanese(value)) continue;
                    entries.Add(new Entry { key = type.Name + "." + field.Name, text = value });
                }
            }
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (t.gameObject.scene != scene || t.transform.root.name != "====KominkaDogRun====" || !HasJapanese(t.text)) continue;
                entries.Add(new Entry { key = "scene." + PathOf(t.transform), text = t.text });
            }
            File.WriteAllText("Temp/menu-finish/inventory.json", JsonUtility.ToJson(new Inventory { entries = entries.ToArray() }, true));
            var log = new System.Text.StringBuilder();
            log.AppendLine(Application.dataPath + " scene=" + scene.path + " dirty=" + scene.isDirty);
            // 裏山改変: 購入アセット (HoboRadio / SleepKit) は裏山に無い
#if KH_WITH_HOBORADIO
            foreach (var radio in Resources.FindObjectsOfTypeAll<HoboRadio_Controller>())
            {
                if (radio.gameObject.scene != scene) continue;
                var so = new SerializedObject(radio);
                log.AppendLine("RADIO " + PathOf(radio.transform) + " global=" + so.FindProperty("isGlobal").boolValue);
                foreach (var source in radio.transform.root.GetComponentsInChildren<AudioSource>(true))
                    log.AppendLine("AUDIO " + PathOf(source.transform) + " volume=" + source.volume);
            }
#else
            log.AppendLine("RADIO none (裏山: HoboRadio 未導入)");
#endif
            File.WriteAllText("Temp/menu-finish/survey.txt", log.ToString());
        }

        static bool HasJapanese(string text)
        {
            if (text == null) return false;
            foreach (char c in text) if (c >= '\u3000' && c <= '\u9fff') return true;
            return false;
        }

        static string PathOf(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    
        // ---- KD_MenuFinishPreviewEditor から併合 (IMP-INF-019 C3、2026-09-13) ----
        [MenuItem("Tools/KominkaHillside/Menu/22d Preview Photo Share Context")]
        public static void PreviewPhotoContext()
        {
            if(EditorApplication.isPlaying)throw new System.InvalidOperationException("Edit mode only");
            var photo=KD_SceneObjects.Find(KD_DogDefs.PhotoSystemName);
            var gallery=photo.transform.Find("Gallery");var caption=gallery.Find("Caption");
            var scene=EditorSceneManager.NewPreviewScene();var previous=RenderTexture.active;
            var rt=new RenderTexture(1536,864,24);var image=new Texture2D(1536,864,TextureFormat.RGB24,false);
            try
            {
                var cameraGo=new GameObject("PhotoShareContextCamera");SceneManager.MoveGameObjectToScene(cameraGo,scene);
                cameraGo.transform.SetPositionAndRotation(gallery.TransformPoint(new Vector3(.35f,caption.localPosition.y-.5f,-3f)),gallery.rotation);
                var camera=cameraGo.AddComponent<Camera>();camera.scene=photo.scene;camera.fieldOfView=50;camera.aspect=1536f/864f;
                camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.cullingMask=~0;camera.targetTexture=rt;
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1536,864),0,0);image.Apply();
                File.WriteAllBytes("Temp/menu-finish/preview-share-context.png",image.EncodeToPNG());
                camera.targetTexture=null;
            }
            finally { RenderTexture.active=previous;Object.DestroyImmediate(rt);Object.DestroyImmediate(image);EditorSceneManager.ClosePreviewScene(scene); }
        }

        [MenuItem("Tools/KominkaHillside/Menu/22c Preview Menu Finish")]
        public static void Preview()
        {
            var p=KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            var catalog=KD_LocalizationEditor.Load();
            var scene=EditorSceneManager.NewPreviewScene();var previous=RenderTexture.active;
            try
            {
                foreach(var definition in catalog.languages)foreach(string pageName in new[]{"home"})   // 裏山改変: ラジオ・写真の共有・クレジット板は裏山に無い
                {
                    string lang=definition.code;
                    bool credit=pageName=="credit",sharing=pageName=="share";int width=credit?840:sharing?1280:(int)KD_TreatSetupEditor.PanelWidth,height=credit?1080:sharing?360:(int)KD_TreatSetupEditor.PanelHeight;
                    var source=credit?KD_SceneObjects.Find(KD_DogDefs.CreditBoardName).transform.Find("Canvas").gameObject:sharing?KD_SceneObjects.Find(KD_DogDefs.PhotoSystemName).transform.Find("Gallery/ShareCanvas").gameObject:p.panelRoot;
                    var clone=Object.Instantiate(source);clone.SetActive(false);SceneManager.MoveGameObjectToScene(clone,scene);
                    foreach(var b in clone.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true))Object.DestroyImmediate(b);
                    foreach(var b in clone.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true))Object.DestroyImmediate(b);
                    foreach(var t in clone.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
                    clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);clone.transform.localScale=Vector3.one*.001f;
                    clone.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
                    if(!credit && !sharing)
                    {
                        for(int i=0;i<p.tabPages.Length;i++)clone.transform.Find(p.tabPages[i].name).gameObject.SetActive(false);
                        var settings=clone.transform.Find("Page_Settings");settings.gameObject.SetActive(true);
                        foreach(Transform child in settings)child.gameObject.SetActive(child.name==(pageName=="home"?"SettingsHome":pageName=="radio"?"RadioSettings":"ShareSettings"));
                        clone.transform.Find("Balance").GetComponent<TMP_Text>().text=p.language._TextForLanguage("KD_ShopShelf.WordRemain",lang)+" 100 pt";
                        clone.transform.Find("SectionTitle").GetComponent<TMP_Text>().text=p.language._TextForLanguage(pageName=="home"?"KD_TreatPouch.WordSettings":pageName=="radio"?"ui.extra.028":"ui.extra.034",lang);
                    }
                    for(int i=0;i<p.language.fixedLabels.Length;i++)
                    {
                        var original=p.language.fixedLabels[i];
                        if(original==null || !original.transform.IsChildOf(source.transform))continue;
                        string path=Relative(source.transform,original.transform);
                        clone.transform.Find(path).GetComponent<TMP_Text>().text=p.language._TextForLanguage(p.language.fixedKeys[i],lang);
                    }
                    if(!credit && !sharing)
                    {
                        clone.transform.Find("Balance").GetComponent<TMP_Text>().text=p.language._TextForLanguage("KD_ShopShelf.WordRemain",lang)+" 100 pt";
                        clone.transform.Find("SectionTitle").GetComponent<TMP_Text>().text=p.language._TextForLanguage(pageName=="home"?"KD_TreatPouch.WordSettings":pageName=="radio"?"ui.extra.028":"ui.extra.034",lang);
                        clone.transform.Find("Page_Settings/SettingsHome/Language").GetComponent<TMP_Text>().text=p.language._TextForLanguage("language.selection",lang).Replace("{0}",definition.displayName);
                    }
                    if(sharing)clone.transform.Find("Content/Post").GetComponent<TMP_InputField>().text=KD_LocalizationEditor.Text(catalog,lang,"share.post");
                    clone.SetActive(true);Canvas.ForceUpdateCanvases();
                    if(credit)clone.GetComponentInChildren<ScrollRect>().verticalNormalizedPosition=1;
                    if(pageName=="radio")
                    {
                        clone.transform.Find("Page_Settings/RadioSettings/State").GetComponent<TMP_Text>().text=p.language._TextForLanguage("ui.extra.031",lang)+": ON   CH 1";
                        clone.transform.Find("Page_Settings/RadioSettings/Volume").GetComponent<Slider>().SetValueWithoutNotify(1);
                    }
                    foreach(var text in clone.GetComponentsInChildren<TextMeshProUGUI>(true))text.ForceMeshUpdate(true);
                    Canvas.ForceUpdateCanvases();foreach(var mask in clone.GetComponentsInChildren<RectMask2D>())mask.PerformClipping();
                    var cameraGo=new GameObject("PreviewCamera");SceneManager.MoveGameObjectToScene(cameraGo,scene);cameraGo.transform.position=new Vector3(0,0,-2);
                    var camera=cameraGo.AddComponent<Camera>();camera.scene=scene;camera.orthographic=true;camera.orthographicSize=credit?.45f:height*.0005f;camera.aspect=(float)width/height;
                    camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.1f,.1f,.1f);
                    var rt=new RenderTexture(width,height,24);camera.targetTexture=rt;
                    var image=new Texture2D(width,height,TextureFormat.RGB24,false);
                    try
                    {
                        camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                        File.WriteAllBytes("Temp/menu-finish/preview-"+lang+"-"+pageName+".png",image.EncodeToPNG());
                    }
                    finally{camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(cameraGo);Object.DestroyImmediate(clone);}
                }
            }
            finally{RenderTexture.active=previous;EditorSceneManager.ClosePreviewScene(scene);}
        }
        static string Relative(Transform parent,Transform child) { string path=child.name;while(child.parent!=parent){child=child.parent;path=child.name+"/"+path;}return path; }

        // ---- KD_MenuFinishVerifyEditor から併合 (IMP-INF-019 C3、2026-09-13) ----
        [MenuItem("Tools/KominkaHillside/Menu/22 Verify Localization")]
        public static void Verify()
        {
            Directory.CreateDirectory("Temp/menu-finish");
            int fail=0; var log=new StringBuilder();
            void Check(bool value,string what) { log.AppendLine((value?"PASS ":"FAIL ")+what); if(!value)fail++; }
            var language=KD_SceneObjects.Find("KD_Language")?.GetComponent<KD_Language>();
            var settings=KD_SceneObjects.Find("KD_MenuSettings")?.GetComponent<KD_MenuSettings>();
            var access=KD_SceneObjects.Find("KD_LiveAccess")?.GetComponent<KD_LiveAccess>();
            Check(language!=null && settings!=null && access!=null,"three systems");
            if(language==null||settings==null||access==null) { File.WriteAllText("Temp/menu-finish/verify.txt",log.ToString());return; }
            var pouch=language.pouch;
            var handPanel=KD_HandMenuCollisionEditor.FindPanel();
            Check(KD_HandMenuCollisionEditor.Validate(handPanel,log),"hand menu does not block players");
            Check(pouch!=null && pouch.language==language,"menu language reference");
            Check(pouch.refreshSeconds==KD_ShopItem.ConfirmRefreshSeconds,"menu confirmation refresh 0.5s");
            foreach(var shelf in language.shelves)Check(shelf.refreshSeconds==KD_ShopItem.ConfirmRefreshSeconds,"shelf confirmation refresh 0.5s: "+shelf.name);
            // 裏山改変: 写真ギャラリーは裏山に無い
            log.AppendLine("SKIP gallery TMP labels (裏山では対象外: 前作のギャラリーの文字は無い。写真 IMP-WLD-005 は KH_PhotoSystem で、Tools/KominkaHillside/Photo/2 が検査する)");
            Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(language).SyncMethod==VRC.SDKBase.Networking.SyncType.None,"language sync None");
            Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(settings).SyncMethod==VRC.SDKBase.Networking.SyncType.None,"settings sync None");
            Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(access).SyncMethod==VRC.SDKBase.Networking.SyncType.Manual,"hosting permission sync Manual");
            // 2026-09-22 ユーザー決定: 開放は初期 ON。実行時の値は U# プログラムの heap 既定 (openToAll = true) で決まる。
            // openToAll は [SerializeField] の無い private なのでシーンにも publicVariables にも焼かれず、ここで読む proxy の値は
            // C# のフィールド初期化子そのもの (heap 既定と同じ出どころ)。既定を OFF へ戻す変更が入ったらここで止まる
            // (2026-09-22 実測: 両 Program Asset の heap 既定も openToAll=True)
            Check(access.IsOpenToAll,"new instance hosting access defaults ON");
            // IMP-NET-007: 開催許可の管理者判定は寝る台帳の CanControl (オーナー優先、不在時だけ Master) が唯一の正本。実参照と backing Udon の両方を見る
            var board=KD_SceneObjects.Find(KD_DogDefs.SleepBoardName)?.GetComponent<KD_SleepBoard>();
            // 裏山改変: 段階表 (D-1)。KD_SleepBoard は段階 2 (IMP-DOG-016) まで意図的な未導入 = null が正
            if(board==null) Check(access.sleepBoard==null,"段階 0: KD_SleepBoard は意図的な未導入 (access.sleepBoard null。許可トグルは暗い。IMP-DOG-016 で必須)");
            else
            {
                Check(access.sleepBoard==board,"hosting permission asks KD_SleepBoard.CanControl (owner first, Master only while absent)");
                object wiredBoard;
                Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(access).publicVariables.TryGetVariableValue("sleepBoard",out wiredBoard)
                    && wiredBoard as Object==UdonSharpEditorUtility.GetBackingUdonBehaviour(board),"hosting permission backing Udon holds the sleep board");
            }
            var note=pouch.settingsHome.transform.Find("LiveAccessNote");
            Check(note!=null && Array.IndexOf(language.fixedLabels,note.GetComponent<TextMeshProUGUI>())>=0,"live access note label bound (single line about the Master standing in)");
            // 裏山改変: ライブは DES-LIV-001 まで準備中 (KD_LiveSystem を置かない)
            if(pouch.live==null) log.AppendLine("SKIP live hosting work / cooldown (裏山では対象外: DES-LIV-001 まで KD_LiveSystem を置かない)");
            else
            {
                Check(pouch.live.access==access && pouch.live.hostingItems.Length==pouch.liveItems.Length,"live checks access and own work");
                for(int i=0;i<pouch.liveItems.Length;i++)Check(pouch.live.hostingItems[i]==pouch.liveItems[i],"hosting work "+i);
            }
            // 裏山改変: ラジオは裏山に無い (D-5)。入口・頁とも作らないことを数える
            Check(settings.radio==null && settings.radioButton==null && settings.radioPage==null
                && pouch.settingsHome.transform.Find("RadioSettingsButton")==null && pouch.settingsPage.transform.Find("RadioSettings")==null,
                "radio settings entry and page are not built (裏山: ラジオ無し)");
            // 裏山改変 (段階 3): 0-b の SKIP を解除。3 群の見出し・並び・入口 4 つ・クレジット頁・インフォメーション (写真設定なし)・音量 (犬の声のみ)・距離フェード
            VerifyHillsideSettings(pouch, settings, language, Check, log);
            // 裏山改変: 購入アセット (HoboRadio / SleepKit) は裏山に無い
#if KH_WITH_SLEEPKIT
            Check(settings.nightMode!=null && settings.sleepKit!=null && settings.nightMode.transform.parent==pouch.settingsHome.transform,"night mode slider on settings home (IMP-ENV-015)");
#else
            Check(settings.nightMode!=null && settings.nightMode.transform.parent==pouch.settingsHome.transform,"night mode slider on settings home (IMP-ENV-015; 裏山: SleepKit 無し)");
#endif
            // 裏山改変: スライダーの先は KH_NightMode (段階 0-b から必須)
            {
                global::KominkaHillside.KH_NightMode night=null; try{night=HillsideNight(pouch.gameObject.scene);}catch(InvalidOperationException e){log.AppendLine("  "+e.Message);}
                Check(night!=null && settings.hillsideNight==night,"night mode slider drives the scene's KH_NightMode (settings.hillsideNight)");
                object wiredNight;
                Check(night!=null && UdonSharpEditorUtility.GetBackingUdonBehaviour(settings).publicVariables.TryGetVariableValue("hillsideNight",out wiredNight)
                    && wiredNight as Object==UdonSharpEditorUtility.GetBackingUdonBehaviour(night),"settings backing Udon holds KH_NightMode (hillsideNight)");
            }
            Check(pouch.settingsHome.transform.Find("ShareSettingsButton")==null && pouch.settingsPage.transform.Find("ShareSettings")==null,"share removed from hand menu");
            // 裏山改変: 写真の共有・ラジオの X URL は裏山に無い
            log.AppendLine("SKIP photo share board / radio X URL / share hashtags (裏山では対象外: 写真・ラジオ未採用)");
            var catalog=KD_LocalizationEditor.Load();
            // IMP-ENV-020: おじいさんの const (字形宣言・行幅検査の対象) は文言表 ja の写しでなければならない
            var keeperMismatch=KD_ShopKeeperSetupEditor.ConstMismatches(catalog);
            Check(keeperMismatch.Length==0,"KD_ShopKeeper const matches catalog ja ("+KD_ShopKeeperSetupEditor.CatalogKeys.Length+" keys)"+(keeperMismatch.Length==0?"":": "+string.Join(" ",keeperMismatch)));
            try { KD_LocalizationEditor.ValidateScene(language,catalog); Check(true,"scene matches catalog codes/order/names/texts/materials/bindings"); }
            catch(InvalidOperationException e) { Check(false,e.Message); }
            Check(language.texts.Length==language.keys.Length*language.languageCodes.Length,"language-major text table aligned");
            var unique=new HashSet<string>(language.keys);
            Check(unique.Count==language.keys.Length,"keys unique");
            for(int l=0;l<language.languageCodes.Length;l++)for(int k=0;k<language.keys.Length;k++)
                Check(language._TextForLanguage(language.keys[k],language.languageCodes[l])==KD_LocalizationEditor.Text(catalog,language.languageCodes[l],language.keys[k]),"text "+language.languageCodes[l]+":"+language.keys[k]);
            // 裏山改変: クレジット板 (KD_CreditBoard) は裏山に無い。設定面のクレジット頁は VerifyHillsideSettings が見る
            Check(KD_SceneObjects.Find(KD_DogDefs.CreditBoardName)==null,"no credit board in the world (裏山: クレジットは設定面の頁)");
            var measure=new GameObject("KD_TextMeasure",typeof(RectTransform),typeof(CanvasRenderer),typeof(TextMeshProUGUI));
            measure.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                var sample=measure.GetComponent<TextMeshProUGUI>();sample.isOrthographic=true;
                var overflows=new StringBuilder();int missing=0,overflow=0,delegated=0,unmeasured=0,shrink=0;
                for(int i=0;i<language.fixedLabels.Length;i++)
                {
                    var target=language.fixedLabels[i];string key=language.fixedKeys[i];
                    if(target==null || !unique.Contains(key)) { Check(false,"label binding "+i);continue; }
                    foreach(string code in language.languageCodes)
                    {
                        string value=language._TextForLanguage(key,code);
                        // IMP-INF-010: glyphs=client の言語は VRChat 内蔵 fallback が描くので欠字を FAIL にせず、実機確認の対象として数える
                        bool bundled=KD_LocalizationEditor.Bundled(Array.Find(catalog.languages,l=>l.code==code));
                        // 未訳のラベルは代替言語の文がそのまま出る (その言語の行で計測済み) ので、client 言語では訳済みのラベルだけを対象にする
                        if(!bundled && value==language._TextForLanguage(key,language.fallbackLanguage))continue;
                        foreach(char c in value)if(KD_LocalizationEditor.Drawn(c) && !KD_LocalizationEditor.HasGlyph(target.font,c)) { if(bundled){missing++;overflows.AppendLine("GLYPH "+target.name+" U+"+((int)c).ToString("X4"));} else {delegated++;overflows.AppendLine("CLIENT "+code+" "+target.name+" U+"+((int)c).ToString("X4"));} }
                        // client 言語の枠収まりは Editor では測れない (未収録字は TMP Settings 既定の LiberationSans の □ や空白幅で測られる)。実機確認の対象として記録だけする
                        if(!bundled) { unmeasured++;overflows.AppendLine("CLIENT_UNMEASURED "+code+" "+Path(target.transform)+" text="+value.Replace("\n"," / "));continue; }
                        // IMP-INF-010: 自動縮小の入ったラベルは fontSizeMax → fontSizeMin の 2 段で測る。
                        // 上限で入れば従来どおり、下限までのどこかで入れば AUTOSIZE_SHRINK (縮んで収まる = 合格)、
                        // 下限でも入らなければ「下限に張り付いて溢れている」ので FAIL (vrc-tmp-font §4)
                        int fit=KD_LocalizationEditor.MeasureLabelFit(sample,target,value,out var preferred);
                        if(fit==1) { shrink++;overflows.AppendLine("AUTOSIZE_SHRINK "+Path(target.transform)+" rect="+target.rectTransform.rect.size+" max="+target.fontSizeMax+" min="+target.fontSizeMin+" preferred@min="+preferred+" text="+value.Replace("\n"," / ")); }
                        if(fit==2) { overflow++;overflows.AppendLine("FIT "+Path(target.transform)+" rect="+target.rectTransform.rect.size+" preferred="+preferred+" text="+value.Replace("\n"," / ")); }
                        // 自動縮小を付けたラベルが下限でも入らないのは設計値の破綻なので、self 言語は FAIL にする
                        // (自動縮小の無いラベルは従来どおり FIT_WARNINGS の警告だけ。スクロール内容や動的な差し込みがあるため)
                        if(KD_LocalizationEditor.ButtonAutoSizeTarget(target))Check(fit!=2,"one-line button label fits by "+target.fontSizeMin+"pt ("+code+") "+Path(target.transform)+" preferred="+preferred);
                        // IMP-ENV-019 / IMP-DOG-036: 「(PCのみ)」を足した 320x64・340x64 のボタンは個別にも見張る (縮小での収まりは可)
                        if(key=="radio.settings.button")Check(fit!=2,"radio settings button label fits one line ("+code+") fit="+fit+" preferred="+preferred);
                        if(key=="KD_DistanceFadeSettings.EntryButton")Check(fit!=2,"distance fade entry button label fits one line ("+code+") fit="+fit+" preferred="+preferred);
                    }
                }
                Check(missing==0,"fixed bundled-language glyphs missing="+missing);
                log.AppendLine("CLIENT_GLYPH_HITS "+delegated+" (glyphs=client languages; drawn by the VRChat built-in fallback, verify on device)");
                foreach(var lang in catalog.languages)if(!KD_LocalizationEditor.Bundled(lang))
                {
                    log.AppendLine("CLIENT_GLYPHS "+lang.code+" "+KD_LocalizationEditor.ClientGlyphs(catalog,pouch.sectionTitleLabel.font,lang.code));
                    log.AppendLine("CLIENT_MIX "+lang.code+" "+KD_LocalizationEditor.ClientGlyphReport(catalog,pouch.sectionTitleLabel.font,lang.code));
                }
                log.AppendLine("CLIENT_UNMEASURED "+unmeasured+" (client-language label fits are not measurable in the Editor; verify on device)");
                log.AppendLine("INCOMPLETE_LANGUAGES "+string.Join(" ",KD_LocalizationEditor.IncompleteLanguages(catalog))+" (build guard blocks these unless Menu Finish/7 allows a test build)");
                log.AppendLine("AUTOSIZE_SHRINK "+shrink+" (one-line button labels that fit only by shrinking toward "+KD_LocalizationEditor.ButtonFontSizeMin+"pt; not a failure)");
                log.AppendLine("FIT_WARNINGS "+overflow+" (scroll content and dynamic placeholders reviewed separately)");
                File.WriteAllText("Temp/menu-finish/text-fit.txt",overflows.ToString());
            }
            finally { UnityEngine.Object.DestroyImmediate(measure); }
            foreach(var descriptor in Resources.FindObjectsOfTypeAll<VRCSceneDescriptor>())
            {
                if(descriptor.gameObject.scene!=pouch.gameObject.scene || descriptor.NetworkIDCollection==null)continue;
                int broken=0,typed=0;
                foreach(var entry in descriptor.NetworkIDCollection)
                    if(entry==null || entry.gameObject==null) { broken++; if(entry!=null && entry.SerializedTypeNames!=null && entry.SerializedTypeNames.Count>0)typed++; }
                log.AppendLine("NETWORK_IDS total="+descriptor.NetworkIDCollection.Count+" null="+broken+" typedNull="+typed+"; historical issue: 13 null entries, audit only");
            }
            log.AppendLine("RESULT "+(fail==0?"PASS":"FAIL")+" failures="+fail);
            File.WriteAllText("Temp/menu-finish/verify.txt",log.ToString());
        }
        static string Path(Transform t) { string value=t.name;while(t.parent!=null){t=t.parent;value=t.name+"/"+value;}return value; }

        // ---- KD_LocalizationVerifyEditor から併合 (IMP-INF-019 C3、2026-09-13) ----
  const string AllowPartialMenu="Tools/KominkaHillside/Menu/11a Allow Partial Translations (Next Build Only)";
  [MenuItem("Tools/KominkaHillside/Menu/11a Allow Partial Translations (Next Build Only)")]
  public static void ToggleAllowPartialTranslations()
  {
   bool next=!SessionState.GetBool(KD_LocalizationBuildGuard.AllowPartialKey,false);
   SessionState.SetBool(KD_LocalizationBuildGuard.AllowPartialKey,next);
   Menu.SetChecked(AllowPartialMenu,next);
   Debug.Log("[KD_Localization] Partial translations: "+(next?"ALLOWED for the next build only (Build & Test, do not upload). The build guard clears this after one build.":"blocked"));
  }
  // メニュー再構築 (ドメインリロード) 後もチェック表示を SessionState に合わせる (検証関数だと check-menu-items が重複と数える)
  [InitializeOnLoadMethod]
  static void SyncAllowPartialTranslationsCheck() { EditorApplication.delayCall+=()=>Menu.SetChecked(AllowPartialMenu,SessionState.GetBool(KD_LocalizationBuildGuard.AllowPartialKey,false)); }
  [MenuItem("Tools/KominkaHillside/Menu/22b Verify Language Extension")]
  public static void VerifyLanguageExtension()
  {
   var log=new StringBuilder();int failures=0;
   void Check(bool ok,string label){log.AppendLine((ok?"PASS ":"FAIL ")+label);if(!ok)failures++;}
   var original=KD_LocalizationEditor.Load();
   Check(KD_ShopKeeperSetupEditor.ConstMismatches(original).Length==0,"KD_ShopKeeper const matches catalog ja (IMP-ENV-020): "+string.Join(" ",KD_ShopKeeperSetupEditor.ConstMismatches(original)));
   KD_LocalizationEditor.Catalog Clone(){return JsonUtility.FromJson<KD_LocalizationEditor.Catalog>(JsonUtility.ToJson(original));}
   void Reject(KD_LocalizationEditor.Catalog c,string label){bool rejected=false;try{KD_LocalizationEditor.Validate(c);}catch(InvalidOperationException){rejected=true;}Check(rejected,label);}
   var fixture=Clone();var list=new System.Collections.Generic.List<KD_LocalizationEditor.Language>(fixture.languages);
   // 出荷しない検証専用コード qq を使う (zz は下の別名検査が使用中)。実カタログの言語を足しても Validate が duplicate で落ちない
   list.Add(new KD_LocalizationEditor.Language{code="qq",displayName="QQ",glyphs=KD_LocalizationEditor.GlyphsSelf,texts=new[]{new KD_LocalizationEditor.TextEntry{key="KD_TreatPouch.WordSettings",text="Parametres"}}});fixture.languages=list.ToArray();
   KD_LocalizationEditor.Validate(fixture);
   var preview=EditorSceneManager.NewPreviewScene();GameObject root=null;
   try
   {
    root=new GameObject("KD_LanguageFixture");SceneManager.MoveGameObjectToScene(root,preview);
    var language=root.AddUdonSharpComponent<KD_Language>();KD_LocalizationEditor.Configure(language,fixture,log);
    Check(language._TextForLanguage("KD_TreatPouch.WordSettings","qq-ca")=="Parametres","third language and region fallback");
    Check(language._TextForLanguage("share.post","qq")==KD_LocalizationEditor.Text(fixture,fixture.fallbackLanguage,"share.post"),"missing third-language entry falls back");
    Check(language.ResolveLanguage("xx")==Array.IndexOf(language.languageCodes,fixture.defaultLanguage),"unsupported initial code uses default");
    Check(language.ResolveLanguage("JA_jp")==Array.IndexOf(language.languageCodes,"ja"),"tag case and separator normalization");
    var ja=Array.Find(fixture.languages,l=>l.code=="ja");var text=Array.Find(ja.texts,t=>t.key=="KD_TreatPouch.WordSettings");text.text="changed Japanese source";
    KD_LocalizationEditor.Configure(language,fixture,log);
    Check(language._TextForLanguage("KD_TreatPouch.WordSettings","qq")=="Parametres","editing Japanese text does not change key binding");
    Array.Reverse(fixture.languages);KD_LocalizationEditor.Configure(language,fixture,log);
    Check(language._TextForLanguage("KD_TreatPouch.WordSettings","qq")=="Parametres","reordering languages keeps key/text association");
    Check(language.languageCodes[language.ResolveLanguage("ja")]=="ja" && language.languageCodes[language.ResolveLanguage("en")]=="en" && KD_Language.SaveKey=="KD_Language_v1","old saved codes resolve after reorder with unchanged save key");
    typeof(KD_Language).GetField("_initialized",global::System.Reflection.BindingFlags.Instance|global::System.Reflection.BindingFlags.NonPublic).SetValue(language,true);
    language._SelectLanguage("qq");Check(language.CurrentLanguage=="qq","select by code");
    int extraIndex=Array.IndexOf(language.languageCodes,"qq");int count=language.languageCodes.Length;
    language._NextLanguage();Check(language.CurrentLanguage==language.languageCodes[(extraIndex+1)%count],"next button follows configured order");
    language._PreviousLanguage();Check(language.CurrentLanguage=="qq","previous button follows configured order");
    language._PreviousLanguage();Check(extraIndex==0 && language.CurrentLanguage==language.languageCodes[count-1],"previous button wraps (qq is first after reverse, previous lands on the last code)");
    // IMP-INF-010: 別名 (aliases)。zh-hk を登録しても端末が zh-TW / zh-Hant を返す場合に英語へ落ちないこと。
    // 出荷しない検証コード zz を使い、実カタログには別名を書かない (zh-hk 追加時に ["zh-tw","zh-hant"] を付ける)
    var aliasFixture=Clone();var aliasList=new System.Collections.Generic.List<KD_LocalizationEditor.Language>(aliasFixture.languages);
    aliasList.Add(new KD_LocalizationEditor.Language{code="zz",displayName="ZZ",glyphs=KD_LocalizationEditor.GlyphsClient,aliases=new[]{"zz-tw","zz-hant"},texts=new[]{new KD_LocalizationEditor.TextEntry{key="KD_TreatPouch.WordSettings",text="Settings ZZ"}}});
    aliasFixture.languages=aliasList.ToArray();KD_LocalizationEditor.Validate(aliasFixture);
    KD_LocalizationEditor.Configure(language,aliasFixture,log);
    int zz=Array.IndexOf(language.languageCodes,"zz");
    // 期待値は「出荷カタログの別名 (zh-hk の 2 件) + フィクスチャの zz の 2 件」。定数 2 で書くと実カタログに別名が増えた時点で崩れる
    KD_LocalizationEditor.AliasTable(original,out var baseAliasCodes,out _);int expectedAliases=baseAliasCodes.Length+2;
    Check(zz>=0 && language.languageAliasCodes.Length==expectedAliases && language.languageAliasTargets.Length==expectedAliases,"alias arrays wired from the catalog (aliasCodes="+language.languageAliasCodes.Length+" expected="+expectedAliases+")");
    Check(language.ResolveLanguage("zz-TW")==zz,"alias resolves a regional tag (zz-TW)");
    Check(language.ResolveLanguage("zz-Hant")==zz,"alias resolves a script tag (zz-Hant)");
    Check(language.ResolveLanguage("ZZ_tw")==zz,"alias resolves after case and separator normalization (ZZ_tw)");
    Check(language.ResolveLanguage("zz-hant-x-test")==zz,"unknown subtag drops to the alias one level up");
    Check(language.ResolveLanguage("zz")==zz,"the canonical code still resolves exactly");
    Check(language.ResolveLanguage("zy-tw")==Array.IndexOf(language.languageCodes,aliasFixture.defaultLanguage),"an alias does not capture an unrelated language");
    Check(language._TextForLanguage("KD_TreatPouch.WordSettings","zz-Hant")=="Settings ZZ","text lookup by alias returns the aliased language");
    language._SelectLanguage("zz-Hant");
    Check(language.CurrentLanguage=="zz","selecting by alias keeps the canonical code (Save writes CurrentLanguage, never the alias)");
   }
   finally{if(root!=null)UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(preview);}
   var bad=Clone();bad.languages[1].code=bad.languages[0].code;Reject(bad,"duplicate codes rejected");
   bad=Clone();bad.defaultLanguage="xx";Reject(bad,"missing default rejected");
   bad=Clone();bad.fallbackLanguage="xx";Reject(bad,"missing fallback rejected");
   bad=Clone();Array.Find(bad.languages[1].texts,t=>t.key=="language.selection").text="Language without placeholder";Reject(bad,"placeholder mismatch rejected");
   bad=Clone();bad.languages[0].texts[0].text="";Reject(bad,"empty fallback text rejected");
   // IMP-INF-010: 別名の書式と衝突 (aliases は任意項目なので、省略・空配列は通ること自体も見る)
   var noAlias=Clone();foreach(var l in noAlias.languages)l.aliases=null;KD_LocalizationEditor.Validate(noAlias);Check(true,"aliases omitted (null) accepted");
   var emptyAlias=Clone();foreach(var l in emptyAlias.languages)l.aliases=new string[0];KD_LocalizationEditor.Validate(emptyAlias);Check(true,"empty aliases array accepted");
   var okAlias=Clone();okAlias.languages[0].aliases=new[]{"zz-tw","zz-hant"};KD_LocalizationEditor.Validate(okAlias);Check(true,"well-formed aliases accepted");
   bad=Clone();bad.languages[0].aliases=new[]{bad.languages[1].code};Reject(bad,"alias colliding with another language code rejected");
   bad=Clone();bad.languages[0].aliases=new[]{bad.languages[0].code};Reject(bad,"alias equal to its own code rejected");
   bad=Clone();bad.languages[0].aliases=new[]{"zz-tw"};bad.languages[1].aliases=new[]{"zz-tw"};Reject(bad,"alias colliding with another alias rejected");
   bad=Clone();bad.languages[0].aliases=new[]{"ZZ-TW"};Reject(bad,"upper-case alias rejected (codes and aliases are lower-case)");
   bad=Clone();bad.languages[0].aliases=new[]{"zz_tw"};Reject(bad,"underscore alias rejected (RFC 5646 uses -)");
   bad=Clone();bad.languages[0].aliases=new[]{""};Reject(bad,"empty alias string rejected");
   // IMP-INF-010: 字形の方針 (self = 共通フォントへ焼く / client = VRChat 内蔵 fallback に委ねる)
   var sharedFont=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
   // 共通フォントに無い字で「client の欠字は許容し self の欠字は止める」検査を試す (内蔵 fallback の描画そのものは実機でしか確認できない)。
   // 候補 (Ґ ウクライナ語 / ฮ タイ語 / ע ヘブライ語) から未収録の最初の 1 字を使う。全部が焼かれたら候補を足すこと
   char sample='\0';foreach(char candidate in new[]{'Ґ','ฮ','ע'})if(sharedFont!=null && !KD_LocalizationEditor.HasGlyph(sharedFont,candidate)){sample=candidate;break;}
   Check(sample!='\0',"a sample character absent from the shared font exists (extend the candidate list if all are baked)");
   bool shippedBaked=true;try{KD_LocalizationEditor.ValidateGlyphs(original,sharedFont);}catch(InvalidOperationException){shippedBaked=false;}Check(shippedBaked,"precondition: shipped self languages are fully baked into the shared font");
   const string testCode="zz"; // 出荷しない検証専用コード。ru/uk 等を実カタログへ登録しても衝突しない
   Check(Array.FindIndex(original.languages,l=>l.code==testCode)<0,"test language code is not in the shipped catalog");
   KD_LocalizationEditor.Catalog WithTestLanguage(string glyphs){var c=Clone();var l=new System.Collections.Generic.List<KD_LocalizationEditor.Language>(c.languages);l.Add(new KD_LocalizationEditor.Language{code=testCode,displayName="ZZ",glyphs=glyphs,texts=new[]{new KD_LocalizationEditor.TextEntry{key="KD_TreatPouch.WordSettings",text="Налаштування "+sample}}});c.languages=l.ToArray();return c;}
   var clientFixture=WithTestLanguage(KD_LocalizationEditor.GlyphsClient);
   KD_LocalizationEditor.Validate(clientFixture);Check(true,"client-glyph language accepted by catalog validation");
   Check(KD_LocalizationEditor.BundledCharacters(clientFixture).IndexOf(sample)<0,"client-glyph text is excluded from the characters baked into the shared font");
   bool clientPasses=true;try{KD_LocalizationEditor.ValidateGlyphs(clientFixture,sharedFont);}catch(InvalidOperationException){clientPasses=false;}Check(clientPasses,"client-glyph language passes the bundled glyph gate");
   Check(KD_LocalizationEditor.ClientGlyphs(clientFixture,sharedFont,testCode).IndexOf(sample)>=0,"client glyph report lists the sample character for on-device verification");
   Check(KD_LocalizationEditor.ClientGlyphReport(clientFixture,sharedFont,testCode).Contains("client=") && KD_LocalizationEditor.ClientGlyphReport(clientFixture,sharedFont,"ja")=="","client mix report exists for client languages only");
   log.AppendLine("INCOMPLETE_LANGUAGES "+string.Join(" ",KD_LocalizationEditor.IncompleteLanguages(original))+" (must be empty before upload; the build guard enforces it, so a partial-translation test does not FAIL here)");
   Check(KD_LocalizationEditor.IncompleteLanguages(clientFixture).Any(s=>s.StartsWith(testCode+"(")),"partial test language is reported as incomplete (build guard input)");
   Check(KD_LocalizationEditor.ClientGlyphs(clientFixture,sharedFont,"ja").Length==0,"client glyph report is empty for a bundled language");
   var selfFixture=WithTestLanguage(KD_LocalizationEditor.GlyphsSelf);
   bool selfRejected=false;try{KD_LocalizationEditor.ValidateGlyphs(selfFixture,sharedFont);}catch(InvalidOperationException){selfRejected=true;}Check(selfRejected,"self-glyph language with a missing glyph is rejected");
   bad=Clone();Array.Find(bad.languages,l=>l.code==bad.fallbackLanguage).glyphs=KD_LocalizationEditor.GlyphsClient;Reject(bad,"fallback language delegating glyphs rejected");
   bad=Clone();Array.Find(bad.languages,l=>l.code==bad.defaultLanguage).glyphs=KD_LocalizationEditor.GlyphsClient;Reject(bad,"default language delegating glyphs rejected");
   bad=Clone();bad.languages[0].glyphs="";Reject(bad,"empty glyph policy rejected (glyphs written as empty string)");
   bad=Clone();bad.languages[0].glyphs=null;Reject(bad,"unset glyph policy rejected (null)");
   bad=Clone();bad.languages[0].glyphs="bundled";Reject(bad,"unknown glyph policy rejected");
   // 公式手順の前提 (害を避ける条件)。クライアント側の TMP Settings が使われるので、内蔵 fallback が効く証拠にはならない
   bool tmpSettingsReadable=true,tmpFallbackEmpty=false;
   try{tmpFallbackEmpty=TMP_Settings.fallbackFontAssets==null || TMP_Settings.fallbackFontAssets.Count==0;}catch(Exception){tmpSettingsReadable=false;}
   Check(tmpSettingsReadable,"TMP Settings asset readable (otherwise import TMP Essential Resources; Assets/TextMesh Pro is not in git)");
   Check(tmpSettingsReadable && tmpFallbackEmpty,"TMP Settings fallback font list is empty (VRChat built-in fallback prerequisite)");
   Check(sharedFont!=null && sharedFont.atlasPopulationMode==AtlasPopulationMode.Static,"shared font stays Static");
   var sceneLanguage=KD_SceneObjects.Find("KD_Language").GetComponent<KD_Language>();
   bool staleRejected=false;try{KD_LocalizationEditor.ValidateScene(sceneLanguage,fixture);}catch(InvalidOperationException){staleRejected=true;}Check(staleRejected,"catalog change without setup rejected");
   KD_LocalizationEditor.ValidateScene(sceneLanguage,original);Check(true,"current scene matches complete catalog and binding manifest");
   {
    // 別名を足して Setup を流し忘れた状態は止める (文言表と実行時配線の不一致)
    var aliasCatalog=Clone();aliasCatalog.languages[0].aliases=new[]{"zz-tw"};
    bool aliasStale=false;try{KD_LocalizationEditor.ValidateScene(sceneLanguage,aliasCatalog);}catch(InvalidOperationException){aliasStale=true;}
    Check(aliasStale,"alias table change without setup rejected");
    KD_LocalizationEditor.AliasTable(original,out var shippedAliasCodes,out var shippedAliasTargets);
    Check((sceneLanguage.languageAliasCodes??Array.Empty<string>()).Length==shippedAliasCodes.Length && shippedAliasCodes.Length==shippedAliasTargets.Length,
     "scene alias arrays match the shipped catalog (aliases="+shippedAliasCodes.Length+")");
    foreach(string alias in sceneLanguage.languageAliasCodes??Array.Empty<string>())
     Check(Array.IndexOf(sceneLanguage.languageCodes,alias)<0,"alias is not also a language code: "+alias);
   }
   {
    var incompleteNow=KD_LocalizationEditor.IncompleteLanguages(original);
    bool allowBefore=SessionState.GetBool(KD_LocalizationBuildGuard.AllowPartialKey,false);
    try
    {
     SessionState.SetBool(KD_LocalizationBuildGuard.AllowPartialKey,false);
     bool gate=new KD_LocalizationBuildGuard().OnBuildRequested(VRC.SDKBase.Editor.BuildPipeline.VRCSDKRequestedBuildType.Scene);
     if(incompleteNow.Length==0)Check(gate,"VRChat build gate accepts current scene (no partial translations)");
     else
     {
      Check(!gate,"VRChat build gate rejects partial translations without the one-shot allowance: "+string.Join(" ",incompleteNow));
      SessionState.SetBool(KD_LocalizationBuildGuard.AllowPartialKey,true);
      Check(new KD_LocalizationBuildGuard().OnBuildRequested(VRC.SDKBase.Editor.BuildPipeline.VRCSDKRequestedBuildType.Scene),"build gate accepts partial translations once Menu Finish/7 allows the next build");
      Check(!SessionState.GetBool(KD_LocalizationBuildGuard.AllowPartialKey,false),"the allowance is consumed by that build (a later upload is blocked again)");
     }
    }
    finally { SessionState.SetBool(KD_LocalizationBuildGuard.AllowPartialKey,allowBefore); }
   }
   Check(Array.IndexOf(sceneLanguage.languageCodes,"qq")<0,"test language never added to shipped scene");
   Check(sceneLanguage.fixedLabels.Length==sceneLanguage.fixedKeys.Length && sceneLanguage.interactTargets.Length==sceneLanguage.interactKeys.Length,"key binding arrays aligned");
   foreach(string key in sceneLanguage.fixedKeys)Check(Array.IndexOf(sceneLanguage.keys,key)>=0,"bound key "+key);
   // IMP-INF-010: 短い 1 行ボタンの自動縮小 (上限 = 元の pt、下限 14pt)。Setup が付け忘れた面と、本文へ広がった事故の両方を見る
   {
    int targets=0,wrapped=0;var missingAuto=new List<string>();var wrongBounds=new List<string>();var wrappedAuto=new List<string>();
    for(int i=0;i<sceneLanguage.fixedLabels.Length;i++)
    {
     var label=sceneLanguage.fixedLabels[i];if(label==null)continue;
     if(KD_LocalizationEditor.ButtonAutoSizeTarget(label))
     {
      targets++;
      if(!label.enableAutoSizing)missingAuto.Add(sceneLanguage.fixedKeys[i]);
      // 上限は元サイズ。autosize 中の fontSize には描画後の縮小値が入るので上限の照合には使えず、
      // 役割トークンの範囲 (world-ui-spec §3 は 16〜32pt) に収まっていることで裏を取る
      else if(label.fontSizeMin!=KD_LocalizationEditor.ButtonFontSizeMin || label.fontSizeMax<=label.fontSizeMin || label.fontSizeMax>48f)
       wrongBounds.Add(sceneLanguage.fixedKeys[i]+"(min="+label.fontSizeMin+" max="+label.fontSizeMax+")");
     }
     // 対象外: 折返しのある本文・注記。ここへ autosize が広がると 1 行に潰れて読み味が変わる (ギャラリーは Button の外で自前の上下限を持つので対象外の判定にも入れない)
     else if(label.enableWordWrapping) { wrapped++; if(label.enableAutoSizing)wrappedAuto.Add(sceneLanguage.fixedKeys[i]); }
    }
    Check(targets>0,"one-line button labels exist to auto-size (targets="+targets+")");
    Check(missingAuto.Count==0,"every one-line button label is auto-sized: missing="+string.Join(" ",missingAuto));
    Check(wrongBounds.Count==0,"auto-sized button labels keep min "+KD_LocalizationEditor.ButtonFontSizeMin+"pt and the original size as max: "+string.Join(" ",wrongBounds));
    Check(wrappedAuto.Count==0,"wrapping body/note labels are not auto-sized (checked="+wrapped+"): "+string.Join(" ",wrappedAuto));
   }
   log.AppendLine("RESULT "+(failures==0?"PASS":"FAIL")+" failures="+failures);
   Directory.CreateDirectory("Temp/language-extension");File.WriteAllText("Temp/language-extension/verify.txt",log.ToString());
  }
}
}
