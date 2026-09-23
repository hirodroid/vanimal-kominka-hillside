using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.Udon;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-INF-020 (2026-09-17): 初回の庭メニュー (案 1) + ようこその面 (案 2) + 手元ヒント (案 4)。
    ///
    /// 作るもの:
    /// <code>
    /// KD_TreatPouchPanel/Page_Welcome            ようこその面 (本文 / 足元の 1 行 / [あとで] [メニューを見る])。既定は非アクティブ
    /// KD_TreatPouchPanel/Page_Settings/WelcomeInformation   「はじめに」(同じ本文 + 端末ごとの呼び方)。設定 → インフォメーション → はじめに
    /// KD_Places/KD_WelcomeSpot                   庭に開いた状態で置く位置と向き。**位置は人が Inspector で詰める** (新規作成時だけ書く)
    /// KD_HintDirector/HandHint                   VR: 右手に追従する小さなラベル
    /// KD_HintDirector/KD_MenuHintOverlay         Desktop: 画面上端のラベル (Screen Space Overlay)
    /// KD_TouchLauncher/LauncherButton/Hint       Touch: [メニュー] の下の吹き出し
    /// </code>
    /// 文言は UITranslations.json (welcome.* / hint.hand.*)、固定ラベルの束縛は UILocalizationBindings.json。
    /// 板の枠 (ヘッダ / 帯 / タブ / 一覧の上下端) は KD_HandMenuScrollEditor が正本なので、ここは面の中身だけ置く。</summary>
    public static class KD_WelcomeGuideEditor
    {
        const string Menu = "Tools/KominkaHillside/Menu/Imported-Unused/Welcome Guide/";
        const string Output = "Temp/welcome-guide/";
        public const string SpotName = "KD_WelcomeSpot";
        public const string PageName = "Page_Welcome";
        public const string RereadName = "WelcomeInformation";
        public const string RereadButtonName = "WelcomeInformationButton";
        const string HandHintName = "HandHint";
        const string ScreenHintName = "KD_MenuHintOverlay";
        const string TouchHintName = "Hint";
        const string HintDirectorName = "KD_HintDirector";

        // ---- 面の寸法 (板 1140×700。一覧の上端 186 / 下端 -250 は KD_HandMenuScrollEditor.ContentTop と同じ) ----
        static readonly Vector2 BodyPos = new Vector2(0, -30), BodySize = new Vector2(1060, 276);
        const string BodyPath = "ScrollView/Viewport/Body";
        const float LanguageY = 154;
        static readonly Vector2 FooterPos = new Vector2(0, -212), FooterSize = new Vector2(1060, 60);
        static readonly Vector2 LaterPos = new Vector2(158, -290), LaterSize = new Vector2(200, 50);
        static readonly Vector2 MenuPos = new Vector2(400, -290), MenuSize = new Vector2(260, 50);
        const float BodyPoints = 22f, FooterPoints = 20f;
        // ---- 庭の置き場の初期値 (新規作成時だけ)。縁側 (x≈-3.3) を降りて 1.5m ほど、導線 (z≈-0.9) の右手。板の中心 = 地面 (-0.39) + 1.3m ----
        static readonly Vector3 DefaultSpot = new Vector3(-5.2f, 0.91f, 0.4f);
        // ---- 手元ヒント ----
        static readonly Vector2 HandHintSize = new Vector2(560, 72);
        const float HandHintScale = .0006f;   // 0.336 × 0.043 m。手の上に浮かぶ札
        static readonly Vector2 ScreenHintSize = new Vector2(320, 48);
        static readonly Vector2 TouchHintSize = new Vector2(240, 64);

        static void Guard()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().path != KD_Hillside.ScenePath /* 裏山改変: シーンの正本は KD_Hillside */)
                throw new InvalidOperationException("Stopped KominkaDogRun scene required");
        }
        static void Write(string name, StringBuilder log) { Directory.CreateDirectory(Output); File.WriteAllText(Output + name + ".txt", log.ToString()); Debug.Log(log.ToString()); }
        static T Find<T>(string name) where T : Component { var go = KD_SceneObjects.Find(name); if (go == null) throw new InvalidOperationException("Missing " + name); return go.GetComponent<T>(); }
        static void Copy(UdonSharp.UdonSharpBehaviour b) { EditorUtility.SetDirty(b); UdonSharpEditorUtility.CopyProxyToUdon(b); }

        static TextMeshProUGUI Label(Transform parent, string name, Vector2 pos, Vector2 size, string text, TMP_FontAsset font, float points, TextAlignmentOptions align)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var tmp = go.GetComponent<TextMeshProUGUI>() ?? go.AddComponent<TextMeshProUGUI>();
            tmp.font = font; tmp.fontSize = points; tmp.enableAutoSizing = false; tmp.isOrthographic = true;
            tmp.text = text; tmp.color = Color.white; tmp.alignment = align;
            tmp.enableWordWrapping = true; tmp.richText = false; tmp.raycastTarget = false;
            return tmp;
        }

        static GameObject Button(Transform parent, string name, Vector2 pos, Vector2 size, Color color, string text, TMP_FontAsset font, UdonBehaviour target, string method, StringBuilder log)
        {
            var go = KD_ShopSetupEditor.EnsureButton(parent, name, pos, size, color, font, text, target, method, log);
            var tmp = go.GetComponentInChildren<TMP_Text>(); tmp.fontSize = 24; tmp.enableAutoSizing = false;
            return go;
        }

        static TextMeshProUGUI LanguageRow(Transform page, KD_Language language, TMP_FontAsset font, StringBuilder log)
        {
            var target = UdonSharpEditorUtility.GetBackingUdonBehaviour(language);
            var catalog = KD_LocalizationEditor.Load();
            Label(page, "LanguageHeading", new Vector2(-325, LanguageY), new Vector2(410, 48),
                KD_LocalizationEditor.Text(catalog, catalog.fallbackLanguage, "welcome.language"), font, 24, TextAlignmentOptions.Left);
            var choice = Label(page, "LanguageChoice", new Vector2(35, LanguageY), new Vector2(290, 48),
                catalog.languages.Single(l => l.code == catalog.fallbackLanguage).displayName, font, 24, TextAlignmentOptions.Center);
            choice.enableWordWrapping = false;
            choice.enableAutoSizing = true; choice.fontSizeMin = 18; choice.fontSizeMax = 24;
            Button(page, "PreviousLanguage", new Vector2(290, LanguageY), new Vector2(100, 58), KD_UiSpriteEditor.ActionNeutral, "<", font, target, "_PreviousLanguage", log);
            Button(page, "NextLanguage", new Vector2(440, LanguageY), new Vector2(100, 58), KD_UiSpriteEditor.ActionNeutral, ">", font, target, "_NextLanguage", log);
            return choice;
        }

        static ScrollRect WelcomeBody(Transform page, TMP_FontAsset font, string text)
        {
            var existing = page.Find("ScrollView");
            var scroll = existing != null ? existing.GetComponent<ScrollRect>() : KD_CreditSetupEditor.BuildScrollArea(page, "ScrollView", BodyPos, BodySize, 36,
                new Color(.12f, .11f, .10f, .8f), new Color(.20f, .18f, .16f, 1), KD_UiSpriteEditor.ActionNeutral, out _);
            ResizeScroll(scroll, BodyPos, BodySize);
            // Preserve the existing label identity when migrating the fixed body into its viewport.
            var old = page.Find("Body"); if (old != null) old.SetParent(scroll.viewport, false);
            var body = Label(scroll.viewport, "Body", Vector2.zero, new Vector2(994, 276), text, font, BodyPoints, TextAlignmentOptions.TopLeft);
            body.margin = new Vector4(12, 8, 12, 8); body.raycastTarget = true;
            var rect = body.rectTransform; rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(.5f, 1); rect.sizeDelta = new Vector2(0, 276);
            var fitter = body.GetComponent<ContentSizeFitter>() ?? body.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = rect; scroll.inertia = false; scroll.verticalNormalizedPosition = 1;
            return scroll;
        }

        static void ResizeScroll(ScrollRect scroll, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)scroll.transform; rect.anchoredPosition = position; rect.sizeDelta = size;
            // BuildScrollArea uses fixed-size children, so resizing the parent alone leaves the old viewport/track.
            scroll.viewport.sizeDelta = new Vector2(size.x - 42, size.y);
            ((RectTransform)scroll.verticalScrollbar.transform).sizeDelta = new Vector2(36, size.y);
        }

        /// <summary>「はじめに」— インフォメーションの先頭の行と、読む面。**Menu Finish / Photo and Information の Setup からも呼ばれる**
        /// (KD_PhotoInformationEditor.BuildPages) ので、あちらを再実行しても行の並びが戻らない。</summary>
        internal static void BuildReread(KD_TreatPouch pouch, KD_MenuSettings settings, TMP_FontAsset font, StringBuilder log)
        {
            var catalog = KD_LocalizationEditor.Load();
            string T(string key) { return KD_LocalizationEditor.Text(catalog, catalog.fallbackLanguage, key); }
            var settingsUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(settings);
            var info = settings.informationPage.transform;
            // 先頭 (y=140)。既存の 3 行は KD_PhotoInformationEditor.BuildPages が 40 / -60 / -160 へ下げる
            Button(info, RereadButtonName, new Vector2(0, 140), new Vector2(640, 70), KD_UiSpriteEditor.ActionNeutral, T("welcome.reread.title"), font, settingsUdon, "_OpenWelcomeInformation", log);
            var page = KD_ShopSetupEditor.EnsureUiChild(pouch.settingsPage.transform, RereadName, Vector2.zero, new Vector2(KD_TreatSetupEditor.PanelWidth, KD_TreatSetupEditor.PanelHeight));
            // Replace only the two obsolete owned labels; the page and navigation keep their identities.
            foreach (string name in new[] { "Body", "Ways" })
            {
                var old = page.transform.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            settings.welcomeInformationScroll = KD_PhotoInformationEditor.Reading(pouch, settings, RereadName, "welcome.reread.body", font, log);
            ResizeScroll(settings.welcomeInformationScroll, new Vector2(0, -46), new Vector2(1000, 308));
            pouch.language.introductionChoiceLabel = LanguageRow(page.transform, pouch.language, font, log);
            pouch.language.introductionScroll = settings.welcomeInformationScroll;
            var body = settings.welcomeInformationScroll.content.GetComponent<TMP_Text>();
            body.richText = true;
            body.lineSpacing = 4;
            body.paragraphSpacing = 8;
            settings.welcomeInformationPage = page;
            var pages = new List<GameObject>(pouch.extraSettingsPages ?? Array.Empty<GameObject>());
            if (!pages.Contains(page)) pages.Add(page);
            pouch.extraSettingsPages = pages.ToArray();
            page.SetActive(false);
            log.AppendLine("reread: " + RereadButtonName + " (y=140) + Page_Settings/" + RereadName);
        }

        /// <summary>World Space の札 (KD_BedroomOnboardingEditor.HintPanel と同じ作り。倍率だけ小さい)。</summary>
        static TextMeshProUGUI WorldLabel(Transform parent, string name, TMP_FontAsset font, Vector2 size, float scale, float points)
        {
            var t = parent.Find(name); GameObject go;
            if (t == null) { go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); }
            else go = t.gameObject;
            go.layer = 5;
            var canvas = go.GetComponent<Canvas>(); if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform; rect.sizeDelta = size; rect.localScale = Vector3.one * scale;
            var bg = go.GetComponent<Image>(); if (bg == null) bg = go.AddComponent<Image>();
            bg.color = KD_UiSpriteEditor.SurfacePanel; bg.raycastTarget = false;
            var labelGo = KD_ShopSetupEditor.EnsureUiChild(go.transform, "Label", Vector2.zero, size - new Vector2(24, 12));
            labelGo.layer = 5;
            var label = labelGo.GetComponent<TextMeshProUGUI>(); if (label == null) label = labelGo.AddComponent<TextMeshProUGUI>();
            label.font = font; label.fontSize = points; label.color = Color.white; label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = true; label.raycastTarget = false; label.richText = false;
            go.SetActive(false); return label;
        }

        /// <summary>Screen Space Overlay の札 (Desktop)。KD_TouchLauncher と同じ基準解像度。押せないので Raycaster も UiShape も付けない。</summary>
        static TextMeshProUGUI ScreenLabel(Transform parent, string name, TMP_FontAsset font, Vector2 size, float points)
        {
            var t = parent.Find(name); GameObject go;
            if (t == null) { go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); }
            else go = t.gameObject;
            go.layer = 5;
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            var canvas = go.GetComponent<Canvas>(); if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 99;   // 起動ボタン (100) より後ろ
            var scaler = go.GetComponent<CanvasScaler>(); if (scaler == null) scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = .5f;
            foreach (var extra in new Component[] { go.GetComponent<GraphicRaycaster>(), go.GetComponent<VRCUiShape>(), go.GetComponent<BoxCollider>() })
                if (extra != null) Object.DestroyImmediate(extra);
            var plate = KD_ShopSetupEditor.EnsureUiChild(go.transform, "Plate", Vector2.zero, size);
            var r = (RectTransform)plate.transform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, 1f);
            r.anchoredPosition = new Vector2(0, -KD_TreatPouch.TouchLauncherEdgeMargin);
            var bg = plate.GetComponent<Image>(); if (bg == null) bg = plate.AddComponent<Image>();
            bg.color = KD_UiSpriteEditor.SurfacePanel; bg.raycastTarget = false; KD_UiSpriteEditor.ApplyFlatSkin(bg);
            var label = Label(plate.transform, "Label", Vector2.zero, size - new Vector2(24, 8), "", font, points, TextAlignmentOptions.Center);
            label.enableWordWrapping = false;
            go.SetActive(false); return label;
        }

        /// <summary>Touch: [メニュー] の下の吹き出し。**起動ボタンの子** — ボタンの位置 (横 = 上中央 / 縦 = 右中央) に付いて回る。</summary>
        static TextMeshProUGUI TouchLabel(RectTransform button, TMP_FontAsset font, Vector2 size, float points)
        {
            var plate = KD_ShopSetupEditor.EnsureUiChild(button, TouchHintName, Vector2.zero, size);
            var r = (RectTransform)plate.transform;
            r.anchorMin = r.anchorMax = new Vector2(.5f, 0f); r.pivot = new Vector2(.5f, 1f);
            r.anchoredPosition = new Vector2(0, -8);
            var bg = plate.GetComponent<Image>(); if (bg == null) bg = plate.AddComponent<Image>();
            bg.color = KD_UiSpriteEditor.SurfacePanel; bg.raycastTarget = false; KD_UiSpriteEditor.ApplyFlatSkin(bg);
            var label = Label(plate.transform, "Label", Vector2.zero, size - new Vector2(16, 8), "", font, points, TextAlignmentOptions.Center);
            plate.SetActive(false); return label;
        }

        [MenuItem(Menu + "1 - Build")]
        public static void Build()
        {
            Guard(); var log = new StringBuilder();
            var pouch = Find<KD_TreatPouch>(KD_DogDefs.TreatPouchName);
            var settings = KD_ProductsEditor.SceneComponents<KD_MenuSettings>().Single();
            var hints = Find<KD_HintDirector>(HintDirectorName);
            var language = pouch.language;
            var font = pouch.sectionTitleLabel.font;
            var catalog = KD_LocalizationEditor.Load();
            string T(string key) { return KD_LocalizationEditor.Text(catalog, catalog.fallbackLanguage, key); }
            var missing = KD_ShopSetupEditor.MissingGlyphs(KD_LocalizationEditor.BundledCharacters(catalog));
            if (missing.Length > 0) KD_ShopSetupEditor.AddGlyphs(missing, log);
            var pouchUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
            if (pouchUdon == null) throw new InvalidOperationException("KD_TreatPouch backing UdonBehaviour missing (force refresh first)");

            // ---- 案 2: ようこその面 ----
            var panel = pouch.panelRoot.transform;
            var page = KD_ShopSetupEditor.EnsureUiChild(panel, PageName, Vector2.zero, new Vector2(KD_TreatSetupEditor.PanelWidth, KD_TreatSetupEditor.PanelHeight));
            language.welcomeScroll = WelcomeBody(page.transform, font, T("welcome.body"));
            language.welcomeChoiceLabel = LanguageRow(page.transform, language, font, log);
            pouch.welcomeFooterLabel = Label(page.transform, "Footer", FooterPos, FooterSize, T("welcome.footer.vr"), font, FooterPoints, TextAlignmentOptions.TopLeft);
            Button(page.transform, "LaterButton", LaterPos, LaterSize, KD_UiSpriteEditor.ActionNeutral, T("welcome.later"), font, pouchUdon, "_WelcomeLater", log);
            Button(page.transform, "MenuButton", MenuPos, MenuSize, KD_UiSpriteEditor.ActionPrimary, T("welcome.menu"), font, pouchUdon, "_WelcomeMenu", log);
            // 確認カードは常に最後の子 (P13)。この面はタブの面と同列 = カードより手前に出ない
            if (pouch.confirmRoot != null) pouch.confirmRoot.transform.SetAsLastSibling();
            page.SetActive(false);
            pouch.welcomePage = page;
            log.AppendLine("welcome page: " + PageName + " body " + BodySize + " footer " + FooterSize + " buttons [あとで] [メニューを見る]");

            // ---- 「はじめに」(再読) ----
            BuildReread(pouch, settings, font, log);
            KD_PhotoInformationEditor.ShiftInformationRows(settings.informationPage.transform);

            // ---- 案 1: 庭の置き場 ----
            var places = KD_SceneObjects.Find("KD_Places").transform;
            var spotT = places.Find(SpotName);
            GameObject spot;
            if (spotT == null)
            {
                spot = new GameObject(SpotName); spot.transform.SetParent(places, false);
                var descriptor = Object.FindObjectOfType<VRCSceneDescriptor>();
                var spawn = descriptor != null && descriptor.spawns != null && descriptor.spawns.Length > 0 ? descriptor.spawns[0].position : Vector3.zero;
                Vector3 toward = DefaultSpot - spawn; toward.y = 0f;
                spot.transform.position = DefaultSpot;
                spot.transform.rotation = Quaternion.LookRotation(toward.sqrMagnitude > .001f ? toward.normalized : Vector3.left, Vector3.up);
                log.AppendLine("spot created at " + DefaultSpot + " facing from spawn " + spawn + " (人が Inspector で詰める)");
            }
            else { spot = spotT.gameObject; log.AppendLine("spot kept at " + spot.transform.position + " rot=" + spot.transform.eulerAngles); }
            pouch.welcomeSpot = spot.transform;
            // 置き場の障害物 (板 1.14×0.7m を置く空間に当たりがあるか) — 位置決めの参考。当たっていても止めない
            var hits = Physics.OverlapBox(spot.transform.position, new Vector3(.6f, .4f, .15f), spot.transform.rotation, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => !c.transform.IsChildOf(pouch.transform)).Select(c => c.name).Distinct().ToArray();
            log.AppendLine("spot overlaps: " + (hits.Length == 0 ? "none" : string.Join(", ", hits)));

            // ---- 案 4: 手元ヒント (端末で 1 面) ----
            hints.handHintLabel = WorldLabel(hints.transform, HandHintName, font, HandHintSize, HandHintScale, 28f);
            hints.handHintRoot = hints.handHintLabel.transform.parent.gameObject;
            hints.handHintLabel.text = T("hint.hand.vr");
            hints.screenHintLabel = ScreenLabel(hints.transform, ScreenHintName, font, ScreenHintSize, 24f);
            hints.screenHintRoot = hints.screenHintLabel.transform.parent.parent.gameObject;
            hints.screenHintLabel.text = T("hint.hand.desktop");
            if (pouch.touchLauncherButton == null) throw new InvalidOperationException("KD_TreatPouch.touchLauncherButton missing (run Treat System/2 first)");
            hints.touchHintLabel = TouchLabel(pouch.touchLauncherButton, font, TouchHintSize, 20f);
            hints.touchHintRoot = hints.touchHintLabel.transform.parent.gameObject;
            hints.touchHintLabel.text = T("hint.hand.touch");
            // 設計値 (IMP-INF-020 09-16 夜の既定): 手元ヒントが出る間は N1 を外す。庭の板は ON
            hints.menuNoticeWithHandHint = false;
            hints.welcomeEnabled = true;
            if (hints.pouch != pouch) hints.pouch = pouch;
            if (pouch.hints != hints) pouch.hints = hints;
            log.AppendLine("hand hints: VR " + HandHintSize + "×" + HandHintScale + " / Desktop overlay " + ScreenHintSize + " / Touch bubble " + TouchHintSize);

            // ---- 文言の束縛 ----
            KD_LocalizationEditor.Configure(language, catalog, log);
            KD_LocalizationEditor.Bind(language, catalog, log);
            foreach (var b in new UdonSharp.UdonSharpBehaviour[] { pouch, settings, hints, language }) Copy(b);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(pouch.gameObject.scene);
            Write("build", log);
        }

        [MenuItem(Menu + "2 - Verify")]
        public static void Verify()
        {
            var log = new StringBuilder(); int checks = 0, failures = 0;
            Action<bool, string> check = (ok, label) => { checks++; if (!ok) failures++; log.AppendLine((ok ? "PASS " : "FAIL ") + label); };
            var pouch = Find<KD_TreatPouch>(KD_DogDefs.TreatPouchName);
            var settings = KD_ProductsEditor.SceneComponents<KD_MenuSettings>().Single();
            var hints = Find<KD_HintDirector>(HintDirectorName);
            var catalog = KD_LocalizationEditor.Load();
            var panel = pouch.panelRoot.transform;

            // 案 2
            var page = panel.Find(PageName);
            check(page != null && pouch.welcomePage == page.gameObject, "welcomePage = " + PageName);
            check(page != null && !page.gameObject.activeSelf && !pouch.panelRoot.activeSelf, "welcome page and panel inactive by default");
            check(page != null && page.Find(BodyPath) != null && pouch.welcomeFooterLabel != null && pouch.welcomeFooterLabel.transform.parent == page, "scrolling body + fixed footer on the page");
            check(page != null && Wired(page.Find("LaterButton"), pouch, "_WelcomeLater") && Wired(page.Find("MenuButton"), pouch, "_WelcomeMenu"), "[あとで] → _WelcomeLater / [メニューを見る] → _WelcomeMenu");
            check(page != null && pouch.language.welcomeScroll != null && pouch.language.welcomeScroll.content == page.Find(BodyPath), "welcome scrolling body wired");
            check(pouch.confirmRoot == null || pouch.confirmRoot.transform.GetSiblingIndex() == panel.childCount - 1, "confirm card still the last child (P13)");
            // 再読
            var reread = pouch.settingsPage.transform.Find(RereadName);
            VerifyLanguageRow(page, pouch.language.welcomeChoiceLabel, pouch.language.welcomeScroll, pouch.language, check);
            VerifyLanguageRow(reread, pouch.language.introductionChoiceLabel, pouch.language.introductionScroll, pouch.language, check);
            check(pouch.language.introductionScroll == settings.welcomeInformationScroll, "introduction language reset uses the reading scroll");
            check(reread != null && settings.welcomeInformationPage == reread.gameObject && (pouch.extraSettingsPages ?? Array.Empty<GameObject>()).Contains(reread.gameObject), "reread page managed as extra settings page");
            check(reread != null && settings.welcomeInformationScroll != null && settings.welcomeInformationScroll.transform.parent == reread
                && settings.welcomeInformationScroll.content.GetComponent<TMP_Text>().fontSize == 24
                && settings.welcomeInformationScroll.verticalScrollbar != null
                && Wired(reread.Find("BackInformation"), settings, "_OpenInformation"), "reread: 24pt reading area + permanent scrollbar + back");
            var info = settings.informationPage.transform;
            check(Wired(info.Find(RereadButtonName), settings, "_OpenWelcomeInformation") && ((RectTransform)info.Find(RereadButtonName)).anchoredPosition.y == 140f, "information: はじめに first (y=140)");
            check(Y(info, "VanimalInformationButton") == 40f && Y(info, "TrainerInformationButton") == -60f && Y(info, "AiInformationButton") == -160f, "information: other rows shifted (40 / -60 / -160)");
            // 案 1
            var spot = KD_SceneObjects.Find(SpotName);
            check(spot != null && pouch.welcomeSpot == spot.transform && spot.transform.parent != null && spot.transform.parent.name == "KD_Places", SpotName + " under KD_Places, wired");
            string offender; check(spot != null && !PrefabUtility.IsPartOfPrefabInstance(spot) && KD_SceneObjects.HasStaticAncestry(spot, out offender), "spot is a plain scene object with static ancestry");
            check(hints.welcomeEnabled && hints.pouch == pouch && pouch.hints == hints, "hints ↔ pouch wired, welcomeEnabled");
            // 案 4
            check(hints.handHintRoot != null && hints.handHintLabel != null && hints.handHintRoot.transform.parent == hints.transform && !hints.handHintRoot.activeSelf
                && hints.handHintRoot.GetComponent<Canvas>() != null && hints.handHintRoot.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace, "VR hand hint: world-space canvas under KD_HintDirector, inactive");
            check(hints.screenHintRoot != null && hints.screenHintLabel != null && !hints.screenHintRoot.activeSelf && hints.screenHintRoot.GetComponent<Canvas>() != null
                && hints.screenHintRoot.GetComponent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay && hints.screenHintRoot.GetComponent<GraphicRaycaster>() == null, "Desktop hint: overlay canvas, no raycaster, inactive");
            check(hints.touchHintRoot != null && hints.touchHintLabel != null && pouch.touchLauncherButton != null && hints.touchHintRoot.transform.parent == pouch.touchLauncherButton
                && !hints.touchHintRoot.activeSelf && hints.touchHintRoot.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), "Touch hint: child of the launcher button, no raycast targets, inactive");
            check(!hints.menuNoticeWithHandHint, "N1 notice suppressed while the hand hint shows");
            // 文言
            var keys = new[] { "welcome.title", "welcome.body", "welcome.footer.vr", "welcome.footer.desktop", "welcome.footer.touch", "welcome.later", "welcome.menu", "welcome.reread.title", "welcome.ways", "hint.hand.vr", "hint.hand.desktop", "hint.hand.touch" };
            check(catalog.languages.Where(KD_LocalizationEditor.Bundled).All(l => keys.All(k => l.texts.Any(t => t.key == k && !string.IsNullOrEmpty(t.text)))), "catalog has all welcome/hint.hand keys in every bundled language"); // client 言語の未訳は代替言語で出る (IMP-INF-010)
            check(pouch.language.keys != null && keys.All(k => pouch.language.keys.Contains(k)), "KD_Language tables carry the keys (Configure ran)");
            check(pouch.language.fixedKeys != null && pouch.language.fixedKeys.Contains("welcome.body") && pouch.language.fixedKeys.Contains("welcome.reread.body") && pouch.language.fixedKeys.Contains("welcome.reread.title"), "fixed label bindings include the welcome labels");
            try { KD_LocalizationEditor.ValidateScene(pouch.language, catalog); check(true, "localization scene validation"); }
            catch (Exception e) { check(false, "localization scene validation: " + e.Message); }
            // 純関数 (既存の Verify と同じ入口): 手元ヒントが出ている間は menuUsed=true として渡す → N1 は出ない
            check(hints.NoticeCandidate(180, true, 0, true, false, 0, 1000) == 0 && hints.NoticeCandidate(180, true, 0, false, false, 0, 1000) == 1, "NoticeCandidate: N1 only when the menu is not covered");
            VerifyLanguageSwitch(catalog, pouch.sectionTitleLabel.font, check);
            log.Insert(0, "checks=" + checks + " failures=" + failures + "\n");
            Write("verify", log);
            if (failures > 0) Debug.LogError("Welcome Guide verify: " + failures + " failure(s). See " + Output + "verify.txt");
        }

        static float Y(Transform parent, string name) { var t = parent.Find(name); return t == null ? float.NaN : ((RectTransform)t).anchoredPosition.y; }

        static void VerifyLanguageRow(Transform page, TMP_Text choice, ScrollRect scroll, KD_Language language, Action<bool, string> check)
        {
            check(page != null && choice != null && choice.transform.parent == page, "fixed language choice on " + (page == null ? "missing page" : page.name));
            if (page == null) return;
            check(Wired(page.Find("PreviousLanguage"), language, "_PreviousLanguage") && Wired(page.Find("NextLanguage"), language, "_NextLanguage"), page.name + ": buttons only change local language");
            var heading = page.Find("LanguageHeading");
            check(heading != null && heading.GetComponent<TMP_Text>().text == "Language / 言語", page.name + ": bilingual heading");
            check(scroll != null && scroll.transform.parent == page && scroll.verticalScrollbar != null && scroll.verticalScrollbarVisibility == ScrollRect.ScrollbarVisibility.Permanent
                && scroll.content.GetComponent<ContentSizeFitter>() != null && scroll.viewport.GetComponent<RectMask2D>() != null, page.name + ": masked body with permanent scrollbar");
            if (scroll != null)
            {
                var r = (RectTransform)scroll.transform;
                check(Mathf.Abs(scroll.viewport.rect.height - r.rect.height) < 1 && Mathf.Abs(((RectTransform)scroll.verticalScrollbar.transform).rect.height - r.rect.height) < 1, page.name + ": viewport and scrollbar match resized body");
                check(r.anchoredPosition.y + r.rect.yMax <= LanguageY - 29 - 12, page.name + ": body separated from language controls by >=12px");
                check(r.anchoredPosition.y + r.rect.yMin >= (page.name == PageName ? -170 : -201), page.name + ": body above fixed footer/back");
            }
            check(LanguageY + 29 <= KD_HandMenuScrollEditor.ContentTop, page.name + ": controls below tabs");
        }

        static void VerifyLanguageSwitch(KD_LocalizationEditor.Catalog catalog, TMP_FontAsset font, Action<bool, string> check)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("KD_WelcomeLanguageFixture"); SceneManager.MoveGameObjectToScene(root, scene);
                var language = root.AddUdonSharpComponent<KD_Language>();
                KD_LocalizationEditor.Configure(language, catalog, new StringBuilder());
                language.choiceLabel = Label(root.transform, "SettingsChoice", Vector2.zero, new Vector2(370,48), "", font, 24, TextAlignmentOptions.Center);
                language.welcomeChoiceLabel = Label(root.transform, "WelcomeChoice", Vector2.zero, new Vector2(290,48), "", font, 24, TextAlignmentOptions.Center);
                language.introductionChoiceLabel = Label(root.transform, "IntroductionChoice", Vector2.zero, new Vector2(290,48), "", font, 24, TextAlignmentOptions.Center);
                var body = Label(root.transform, "Body", Vector2.zero, new Vector2(1000,400), "", font, 22, TextAlignmentOptions.TopLeft);
                language.fixedLabels = new TMP_Text[] { body }; language.fixedKeys = new[] { "welcome.body" };
                typeof(KD_Language).GetField("_initialized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(language, true);
                // No player restore/pouch/keeper: exercise real Apply without writing PlayerData or touching the live scene.
                foreach (var definition in catalog.languages)
                {
                    language._SelectLanguage(definition.code);
                    check(language.welcomeChoiceLabel.text == definition.displayName && language.introductionChoiceLabel.text == definition.displayName
                        && language.choiceLabel.text == KD_LocalizationEditor.Text(catalog, definition.code, "language.selection").Replace("{0}", definition.displayName)
                        && body.text == KD_LocalizationEditor.Text(catalog, definition.code, "welcome.body"), "language switch updates all three choices and body: " + definition.code);
                }
                language._NextLanguage(); check(language.CurrentLanguage == catalog.languages[0].code, "next language wraps from last to first");
                language._PreviousLanguage(); check(language.CurrentLanguage == catalog.languages[catalog.languages.Length-1].code, "previous language wraps from first to last");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        /// <summary>onClick が **ちょうど 1 本**、有効 (RuntimeOnly)、指定の Udon へ指定の文字列で配線されているか。
        /// 余分な接続・二重・Off を PASS にしない (Codex 指摘 2026-09-17)。</summary>
        static bool Wired(Transform button, UdonSharp.UdonSharpBehaviour target, string method)
        {
            if (button == null) return false;
            var b = button.GetComponent<Button>(); if (b == null) return false;
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(target);
            if (b.onClick.GetPersistentEventCount() != 1) return false;
            if (b.onClick.GetPersistentTarget(0) != udon || b.onClick.GetPersistentMethodName(0) != "SendCustomEvent") return false;
            // 引数文字列と呼び出し状態は SerializedObject から読む (UnityEvent に公開 API が無い)
            var so = new SerializedObject(b);
            var call = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls").GetArrayElementAtIndex(0);
            return call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == method
                && call.FindPropertyRelative("m_CallState").intValue == (int)UnityEngine.Events.UnityEventCallState.RuntimeOnly;
        }

        /// <summary>ようこそ / はじめに を JP / EN で描いて PNG に出す (Codex の見た目レビュー用)。板の複製を Preview Scene で撮る (Menu Finish/4 と同じ作り)。</summary>
        [MenuItem(Menu + "3 - Preview")]
        public static void Preview()
        {
            var pouch = Find<KD_TreatPouch>(KD_DogDefs.TreatPouchName);
            var catalog = KD_LocalizationEditor.Load();
            Directory.CreateDirectory(Output);
            var scene = EditorSceneManager.NewPreviewScene(); var previous = RenderTexture.active;
            int width = (int)KD_TreatSetupEditor.PanelWidth, height = (int)KD_TreatSetupEditor.PanelHeight;
            try
            {
                foreach (var definition in catalog.languages) foreach (string pageName in new[] { "welcome-vr", "welcome-desktop", "welcome-touch", "reread", "reread-middle", "reread-bottom", "ai", "ai-bottom" })
                {
                    string lang = definition.code; bool reread = pageName.StartsWith("reread"), ai = pageName.StartsWith("ai");
                    bool reading = reread || ai;
                    string readingName = ai ? KD_ProductsEditor.SceneComponents<KD_MenuSettings>().Single().aiInformationPage.name : RereadName;
                    var source = pouch.panelRoot;
                    var clone = Object.Instantiate(source); clone.SetActive(false); SceneManager.MoveGameObjectToScene(clone, scene);
                    foreach (var b in clone.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true)) Object.DestroyImmediate(b);
                    foreach (var b in clone.GetComponentsInChildren<UdonBehaviour>(true)) Object.DestroyImmediate(b);
                    foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                    clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); clone.transform.localScale = Vector3.one * .001f;
                    clone.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                    for (int i = 0; i < pouch.tabPages.Length; i++) clone.transform.Find(pouch.tabPages[i].name).gameObject.SetActive(false);
                    var settingsPage = clone.transform.Find("Page_Settings"); settingsPage.gameObject.SetActive(reading);
                    if (reading) foreach (Transform child in settingsPage) child.gameObject.SetActive(child.name == readingName);
                    clone.transform.Find(PageName).gameObject.SetActive(!reading);
                    var band = clone.transform.Find(KD_TreatSetupEditor.EventBandName); if (band != null) band.gameObject.SetActive(false);
                    var confirm = pouch.confirmRoot != null ? clone.transform.Find(pouch.confirmRoot.name) : null; if (confirm != null) confirm.gameObject.SetActive(false);
                    // タブは見せて非選択
                    for (int i = 0; i < pouch.tabImages.Length; i++)
                    {
                        var tab = clone.transform.Find(pouch.tabImages[i].name); if (tab == null) continue;
                        tab.GetComponent<Image>().color = pouch.tabOffColor;
                        var line = tab.Find(pouch.tabUnderlines[i].name); if (line != null) line.gameObject.SetActive(false);
                    }
                    for (int i = 0; i < pouch.language.fixedLabels.Length; i++)
                    {
                        var original = pouch.language.fixedLabels[i];
                        if (original == null || !original.transform.IsChildOf(source.transform)) continue;
                        clone.transform.Find(Relative(source.transform, original.transform)).GetComponent<TMP_Text>().text = pouch.language._TextForLanguage(pouch.language.fixedKeys[i], lang);
                    }
                    clone.transform.Find("Balance").GetComponent<TMP_Text>().text = pouch.language._TextForLanguage("KD_ShopShelf.WordRemain", lang) + " 100 pt <size=60%>▶</size>";
                    clone.transform.Find("SectionTitle").GetComponent<TMP_Text>().text = pouch.language._TextForLanguage(ai ? "information.ai.title" : reread ? "welcome.reread.title" : "welcome.title", lang);
                    foreach (string path in new[] { PageName, "Page_Settings/" + RereadName })
                        clone.transform.Find(path + "/LanguageChoice").GetComponent<TMP_Text>().text = definition.displayName;
                    var closeHint = clone.transform.Find("CloseHint"); if (closeHint != null) closeHint.gameObject.SetActive(pageName == "welcome-desktop");
                    if (!reading)
                        clone.transform.Find(PageName + "/Footer").GetComponent<TMP_Text>().text = pouch.language._TextForLanguage("welcome.footer." + pageName.Substring("welcome-".Length), lang);
                    clone.SetActive(true); Canvas.ForceUpdateCanvases();
                    foreach (var text in clone.GetComponentsInChildren<TextMeshProUGUI>(true)) text.ForceMeshUpdate(true);
                    Canvas.ForceUpdateCanvases();
                    // Every previewed page now has a scrollable body.
                    {
                        var scroll = reading ? settingsPage.Find(readingName).GetComponentInChildren<ScrollRect>() : clone.transform.Find(PageName).GetComponentInChildren<ScrollRect>();
                        LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
                        scroll.verticalNormalizedPosition = pageName.EndsWith("bottom") ? 0 : pageName.EndsWith("middle") ? .5f : 1;
                        Canvas.ForceUpdateCanvases();
                    }
                    var cameraGo = new GameObject("PreviewCamera"); SceneManager.MoveGameObjectToScene(cameraGo, scene); cameraGo.transform.position = new Vector3(0, 0, -2);
                    var camera = cameraGo.AddComponent<Camera>(); camera.scene = scene; camera.orthographic = true; camera.orthographicSize = height * .0005f; camera.aspect = (float)width / height;
                    camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.1f, .1f, .1f);
                    var rt = new RenderTexture(width, height, 24); camera.targetTexture = rt;
                    var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                    try
                    {
                        camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                        File.WriteAllBytes(Output + "preview-" + lang + "-" + pageName + ".png", image.EncodeToPNG());
                    }
                    finally { camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(rt); Object.DestroyImmediate(image); Object.DestroyImmediate(cameraGo); Object.DestroyImmediate(clone); }
                }
            }
            finally { RenderTexture.active = previous; EditorSceneManager.ClosePreviewScene(scene); }
            // 本文の溢れ (fitting) — 一覧の下端 -250 より下へ出ていないか
            var log = new StringBuilder();
            foreach (var definition in catalog.languages)
            {
                foreach (var pair in new[] { new[] { PageName + "/" + BodyPath, "welcome.body" }, new[] { PageName + "/Footer", "welcome.footer.vr" } })
                {
                    if (!KD_LocalizationEditor.Bundled(definition)) { log.AppendLine("CLIENT_UNMEASURED " + definition.code + " " + pair[0]); continue; } // Editor では測れない (IMP-INF-010)
                    var label = pouch.panelRoot.transform.Find(pair[0]).GetComponent<TextMeshProUGUI>();
                    string keep = label.text; label.text = pouch.language._TextForLanguage(pair[1], definition.code);
                    label.ForceMeshUpdate(true);
                    float needed = label.GetPreferredValues(label.text, label.rectTransform.rect.width, 0).y, box = label.rectTransform.rect.height;
                    label.text = keep; label.ForceMeshUpdate(true);
                    log.AppendLine((pair[1] == "welcome.body" ? "SCROLL " : needed <= box + .5f ? "FIT " : "FIT_OVERFLOWS ") + definition.code + " " + pair[0] + " needed=" + needed.ToString("F0") + " box=" + box.ToString("F0"));
                }
            }
            Write("preview", log);
        }
        static string Relative(Transform parent, Transform child) { string path = child.name; while (child.parent != parent) { child = child.parent; path = child.name + "/" + path; } return path; }
    }
}
