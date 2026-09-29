using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    /// <summary>裏山改変 (IMP-UI-001 段階 3): 設定ホームの 3 群 (自分の見え方・聞こえ方 / この部屋のみんな / 読む)、
    /// 入口 4 つ (音量 / 距離フェード (PCのみ) / クレジット / インフォメーション)、クレジット頁 (板は置かない)、
    /// インフォメーション (はじめに / 生成AI / VAnimal / トレーナー。写真設定なし) と Verify 22 の裏山検査。
    /// **設定ホームの配置の正本はこのファイルの定数**。音量と距離フェードの入口は各 Setup (Menu/16 / 14) も作るが、位置はここを読む
    /// (古民家は「位置はそれぞれの Setup が持つ」だったが、裏山は 3 群に並べ替えるので 1 か所に寄せた)。</summary>
    public static partial class KD_MenuFinishEditor
    {
        // ---------------------------------------------------------------- 設定ホームの配置 (1140x700 の板、上端はタブの下端 201)
        // 群「自分の見え方・聞こえ方」: 見出し / [音量設定][距離フェード (PCのみ)] / ナイトモード (Slider + 注記) / 言語
        // 群「この部屋のみんな」    : 見出し (右に補足 ui.extra.048) / [呼ぶを全員に許可][ライブ開催を全員に許可]
        // 群「読む」                : 見出し / [クレジット][インフォメーション]
        // 下端は Feedback (y=-285、高さ 44) の文字に掛からない -250 まで。CloseHint (-400,-328) とも重ならない
        public const float HillsideGroupSelfY = 180f, HillsideEntryRowY = 128f, HillsideNightY = 70f, HillsideNightNoteY = 32f, HillsideLanguageY = -22f;
        public const float HillsideGroupRoomY = -74f, HillsideAccessY = -114f;
        public const float HillsideGroupReadY = -168f, HillsideReadRowY = -214f;
        public static readonly Vector2 HillsideEntrySize = new Vector2(340f, 60f);
        public static readonly Vector2 HillsideVolumeEntryPos = new Vector2(-180f, HillsideEntryRowY);
        public static readonly Vector2 HillsideFadeEntryPos = new Vector2(180f, HillsideEntryRowY);
        public static readonly Vector2 HillsideCreditEntryPos = new Vector2(-180f, HillsideReadRowY);
        public static readonly Vector2 HillsideInformationEntryPos = new Vector2(180f, HillsideReadRowY);
        public static readonly Vector2 HillsideAccessNotePos = new Vector2(190f, HillsideGroupRoomY);
        public static readonly Vector2 HillsideAccessNoteSize = new Vector2(700f, 30f);
        static readonly Vector2 GroupHeadingSize = new Vector2(1080f, 30f);
        const float GroupHeadingPoints = 20f;
        const float HomeTop = 195f, HomeBottom = -250f;

        /// <summary>群見出しの (名前, 文言キー, y, 仮の ja)。文言キーは keys-U1.json (司令塔が UITranslations.json へ統合)。</summary>
        public static readonly (string name, string key, float y, string ja)[] HillsideGroups =
        {
            ("GroupSelf", "settings.group.self", HillsideGroupSelfY, "自分の見え方・聞こえ方"),
            ("GroupRoom", "settings.group.room", HillsideGroupRoomY, "この部屋のみんな"),
            ("GroupRead", "settings.group.read", HillsideGroupReadY, "読む"),
        };

        // ---------------------------------------------------------------- クレジット (設定面の頁。古民家の KD_CreditBoard は置かない)
        public const string CreditPageName = "CreditPage";
        public const string CreditButtonName = "CreditButton";
        public const string CreditRowsName = "CreditRows";
        /// <summary>行の正本 (契約書 段階 3: 犬モデル / 舞台 2 件 / 空 / おじいさん / フォント + OFL の表示)。動画プレイヤー (VizVid) の行は裏山に無い。
        /// key が null の行は翻訳しない固定表記 (ライセンス表示)。**url が空の行は「URL 未確認」**: 入力欄を作らず、Verify 22 が CREDIT_URL_PENDING として数える
        /// (購入元の URL は人が購入履歴で確かめてここへ書く。推測で埋めない)。</summary>
        public static readonly (string key, string ja, string url)[] HillsideCredits =
        {
            ("credit.dogs", "犬モデル\nTORINOSUKEの家", "https://torinosuke05.booth.pm/items"),
            ("credit.environment.kit", "舞台モデル\nLMArt Stylized Environment Kit", ""),
            ("credit.environment", "舞台モデル\nモサンゴ屋「庭付き古民家」", "https://booth.pm/ja/items/6364620"),
            ("credit.sky", "空\nAllsky", ""),
            ("credit.keeper", "おじいさん\npolyperfect Low Poly Animated People", ""),
            ("credit.font", "フォント\nNoto Serif JP", "https://fonts.google.com/noto/specimen/Noto+Serif+JP"),
            (null, "SIL Open Font License 1.1\nCopyright 2012 Google Inc. All Rights Reserved.", "https://openfontlicense.org"),
        };
        const float CreditLeadHeight = 44f, CreditRowPitch = 84f, CreditTopPad = 12f;
        static readonly Vector2 CreditNameSize = new Vector2(420f, 76f);
        static readonly Vector2 CreditUrlSize = new Vector2(470f, 48f);
        const float CreditNameX = -236f, CreditUrlX = 226f;

        /// <summary>インフォメーションの行の並び (モック・契約書の順: はじめに / 生成AI / VAnimal / トレーナー)。古民家は はじめに / VAnimal / トレーナー / 生成AI。</summary>
        public static readonly (string name, float y)[] HillsideInformationRows =
        {
            (KD_WelcomeGuideEditor.RereadButtonName, 140f),
            ("AiInformationButton", 40f),
            ("VanimalInformationButton", -60f),
            ("TrainerInformationButton", -160f),
        };

        static string HT(string key, string fallback, StringBuilder log) { return KD_Hillside.Text(key, fallback, log); }

        /// <summary>BuildSettings の末尾から (Menu/11 と Menu/7 の RebuildIfPresent の両方)。冪等。
        /// 音量の頁 (Menu/16 の本体) と距離フェードの系 (Menu/14 の本体) もここで揃える —
        /// 文言の束縛 (UILocalizationBindings) がそれらの部品を要求するので、Menu/11 を先に流しても Bind が落ちないようにする。</summary>
        internal static void BuildHillsideSettings(KD_TreatPouch pouch, KD_MenuSettings settings, TMP_FontAsset font, StringBuilder log)
        {
            var home = pouch.settingsHome.transform;
            // 1) 音量 (BGM + 犬の声。IMP-WLD-003)。入口 VolumeSettingsButton もここで表示に戻る
            KD_DogVoiceSettingsEditor.EnsureHillsideVolume(pouch, font, log);
            // 2) 距離フェード (PCのみ)。系 KH_Dogs/KD_DistanceFadeSettings が無ければ作る
            KD_DistanceFadeSettingsEditor.EnsureHillside(pouch, log);
            // 3) クレジットの頁と入口
            BuildCreditPage(pouch, settings, font, log);
            // 4) インフォメーション (入口 + はじめに / 生成AI / VAnimal / トレーナー)
            BuildInformation(pouch, settings, font, log);
            // 5) 群の見出し
            foreach (var g in HillsideGroups)
            {
                var label = Label(home, g.name, new Vector2(0f, g.y), GroupHeadingSize, HT(g.key, g.ja, log), font, GroupHeadingPoints);
                label.alignment = TextAlignmentOptions.MidlineLeft; label.color = KD_UiSpriteEditor.AccentGold;
                label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Overflow;
            }
            // 6) 入口 4 つを表示 (0-b で非表示にしたものを戻す)。位置と大きさは上の定数
            foreach (var entry in new[] { ("VolumeSettingsButton", HillsideVolumeEntryPos), ("DistanceFadeSettingsButton", HillsideFadeEntryPos),
                                           (CreditButtonName, HillsideCreditEntryPos), ("InformationButton", HillsideInformationEntryPos) })
            {
                var t = home.Find(entry.Item1) as RectTransform;
                if (t == null) { log.AppendLine("FAIL settings entry missing: " + entry.Item1); continue; }
                t.anchoredPosition = entry.Item2; t.sizeDelta = HillsideEntrySize;
                var label = t.Find("Label") as RectTransform; if (label != null) label.sizeDelta = HillsideEntrySize - new Vector2(24f, 4f);
                if (!t.gameObject.activeSelf) { t.gameObject.SetActive(true); log.AppendLine("Settings: " + entry.Item1 + " shown (段階 3)"); }
            }
            // 7) 頁の登録を掃除 (消した頁の null を落とす)。音量・距離フェードの頁は KD_TreatPouch の専用フィールドで開くので登録しない
            var pages = new List<GameObject>();
            foreach (var p in pouch.extraSettingsPages ?? Array.Empty<GameObject>()) if (p != null && !pages.Contains(p)) pages.Add(p);
            pouch.extraSettingsPages = pages.ToArray();
            if (pouch.confirmRoot != null) pouch.confirmRoot.transform.SetAsLastSibling();   // 確認カードは常に最後の子 (P13)
            log.AppendLine("Settings home (裏山 段階 3): groups self y=" + HillsideGroupSelfY + " / room y=" + HillsideGroupRoomY + " / read y=" + HillsideGroupReadY);
        }

        /// <summary>設定面のクレジット頁 (Page_Settings/CreditPage) と入口 [クレジット]。行は <see cref="HillsideCredits"/>。
        /// 見出しは頁の外 (板の SectionTitle に ui.extra.022)。先頭に ui.extra.021「このワールドで使わせていただいた素材」、右下にコピーの案内 ui.extra.036。</summary>
        internal static void BuildCreditPage(KD_TreatPouch pouch, KD_MenuSettings settings, TMP_FontAsset font, StringBuilder log)
        {
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(settings);
            Button(pouch.settingsHome.transform, CreditButtonName, HillsideCreditEntryPos, HillsideEntrySize, HT("ui.extra.022", "クレジット", log), font, udon, "_OpenCredits", log);
            var page = KD_ShopSetupEditor.EnsureUiChild(pouch.settingsPage.transform, CreditPageName, Vector2.zero, new Vector2(KD_TreatSetupEditor.PanelWidth, KD_TreatSetupEditor.PanelHeight));
            var existing = page.transform.Find("ScrollView");
            var scroll = existing != null ? existing.GetComponent<ScrollRect>() : KD_CreditSetupEditor.BuildScrollArea(page.transform, "ScrollView", Vector2.zero, new Vector2(1000, 400), 36,
                new Color(.12f, .11f, .10f, .8f), new Color(.20f, .18f, .16f, 1), KD_UiSpriteEditor.ActionNeutral, out _);
            scroll.inertia = false;
            // URL の入力欄の上ではドラッグが ScrollRect に渡らない (vrc-udonsharp §5)。右のバーは常時表示 (BuildScrollArea が Permanent)
            float height = CreditTopPad + CreditLeadHeight + HillsideCredits.Length * CreditRowPitch + CreditTopPad;
            var content = KD_ShopSetupEditor.EnsureUiChild(scroll.viewport, CreditRowsName, Vector2.zero, new Vector2(0f, height));
            var rect = (RectTransform)content.transform;
            rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(1f, 1f); rect.pivot = new Vector2(.5f, 1f);
            rect.sizeDelta = new Vector2(0f, height); rect.anchoredPosition = Vector2.zero;
            var lead = Label(content.transform, "Lead", new Vector2(0f, -(CreditTopPad + CreditLeadHeight * .5f)), new Vector2(900f, CreditLeadHeight), HT("ui.extra.021", "このワールドで使わせていただいた素材", log), font, 22);
            TopAnchor(lead.rectTransform); lead.alignment = TextAlignmentOptions.MidlineLeft;
            var glyphs = new StringBuilder();
            int pending = 0;
            for (int i = 0; i < HillsideCredits.Length; i++)
            {
                var row = HillsideCredits[i];
                float y = -(CreditTopPad + CreditLeadHeight + CreditRowPitch * i + CreditRowPitch * .5f);
                string name = row.key == null ? row.ja : HT(row.key, row.ja, log);
                var label = Label(content.transform, "Name" + i, new Vector2(CreditNameX, y), CreditNameSize, name, font, 20);
                TopAnchor(label.rectTransform); label.alignment = TextAlignmentOptions.MidlineLeft; label.richText = false;
                if (row.key == null) glyphs.Append(row.ja);
                var old = content.transform.Find("Url" + i);
                if (string.IsNullOrEmpty(row.url))
                {
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                    pending++; log.AppendLine("NOTE credit row " + i + " (" + row.key + "): URL 未確認 — 入力欄を作らない (KD_MenuFinishEditor.HillsideCredits に人が記入)");
                    continue;
                }
                var field = CopyField(content.transform, "Url" + i, new Vector2(CreditUrlX, y), CreditUrlSize, row.url, font, false);
                field.pointSize = 20;
                TopAnchor((RectTransform)field.transform);
                glyphs.Append(row.url);
            }
            scroll.content = rect; scroll.verticalNormalizedPosition = 1f;
            Back(page.transform, pouch, font, log);
            ((RectTransform)page.transform.Find("BackSettings")).anchoredPosition = new Vector2(-380, -250);
            var hint = Label(page.transform, "CopyHint", new Vector2(250, -250), new Vector2(560, 36), HT("ui.extra.036", "キーボード: 選択後 Ctrl+C", log), font, 18);
            hint.alignment = TextAlignmentOptions.MidlineRight;
            // 固定表記 (URL・ライセンス表示) は文言表に無いので、ここで字形を焼く (翻訳文は Menu/11 の BundledCharacters が焼く)
            var missing = KD_ShopSetupEditor.MissingGlyphs(glyphs.ToString());
            if (missing.Length > 0) KD_ShopSetupEditor.AddGlyphs(missing, log);
            settings.creditPage = page; settings.creditScroll = scroll;
            var pages = new List<GameObject>(pouch.extraSettingsPages ?? Array.Empty<GameObject>());
            if (!pages.Contains(page)) pages.Add(page);
            pouch.extraSettingsPages = pages.ToArray();
            page.SetActive(false);
            log.AppendLine("Credit page: Page_Settings/" + CreditPageName + " rows=" + HillsideCredits.Length + " urlPending=" + pending + " (no VizVid row, no credit board)");
        }

        static void TopAnchor(RectTransform rect)
        {
            Vector2 pos = rect.anchoredPosition;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.anchoredPosition = pos;
        }

        /// <summary>インフォメーションの 4 行 (写真設定なし)。はじめに は KD_WelcomeGuideEditor.BuildReread (本文 welcome.reread.body は U2)、
        /// VAnimal / トレーナーの読み物は KD_PhotoInformationEditor.Reading (古民家と同じ部品と名前)。</summary>
        static void BuildInformationPagesHillside(KD_TreatPouch pouch, KD_MenuSettings settings, TMP_FontAsset font, StringBuilder log)
        {
            var catalog = KD_LocalizationEditor.Load();
            string T(string key) { return KD_LocalizationEditor.Text(catalog, catalog.fallbackLanguage, key); }
            var target = UdonSharpEditorUtility.GetBackingUdonBehaviour(settings);
            var info = settings.informationPage.transform;
            InfoButton(info, "VanimalInformationButton", T("information.vanimal.title"), font, target, "_OpenVanimalInformation", log);
            InfoButton(info, "TrainerInformationButton", T("information.trainer.title"), font, target, "_OpenTrainerInformation", log);
            KD_WelcomeGuideEditor.BuildReread(pouch, settings, font, log);
            foreach (var row in HillsideInformationRows) ((RectTransform)info.Find(row.name)).anchoredPosition = new Vector2(0f, row.y);
            settings.vanimalInformationScroll = KD_PhotoInformationEditor.Reading(pouch, settings, "VanimalInformation", "information.vanimal.body", font, log);
            settings.trainerInformationScroll = KD_PhotoInformationEditor.Reading(pouch, settings, "TrainerInformation", "information.trainer.body", font, log);
            settings.vanimalInformationPage = settings.vanimalInformationScroll.transform.parent.gameObject;
            settings.trainerInformationPage = settings.trainerInformationScroll.transform.parent.gameObject;
            var pages = new List<GameObject>();
            foreach (var p in pouch.extraSettingsPages ?? Array.Empty<GameObject>()) if (p != null && !pages.Contains(p)) pages.Add(p);
            foreach (var p in new[] { settings.vanimalInformationPage, settings.trainerInformationPage }) if (!pages.Contains(p)) pages.Add(p);
            pouch.extraSettingsPages = pages.ToArray();
            log.AppendLine("Information (裏山): はじめに 140 / 生成AI 40 / VAnimal -60 / トレーナー -160 (写真設定なし)");
        }

        static void InfoButton(Transform parent, string name, string text, TMP_FontAsset font, VRC.Udon.UdonBehaviour target, string method, StringBuilder log)
        {
            // 古民家の KD_PhotoInformationEditor.Button と同じ見た目 (640x70、24pt、自動縮小は Bind 後の共通処理に任せる)
            var go = KD_ShopSetupEditor.EnsureButton(parent, name, Vector2.zero, new Vector2(640, 70), KD_UiSpriteEditor.ActionNeutral, font, text, target, method, log);
            var label = go.GetComponentInChildren<TMP_Text>(); label.fontSize = 24; label.enableAutoSizing = false;
        }

        // ================================================================ Verify 22 (裏山 段階 3)

        static void VerifyHillsideSettings(KD_TreatPouch pouch, KD_MenuSettings settings, KD_Language language, Action<bool, string> check, StringBuilder log)
        {
            var home = pouch.settingsHome.transform;
            var pouchUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
            var settingsUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(settings);
            bool Bound(TMP_Text label, string key) { int i = label == null ? -1 : Array.IndexOf(language.fixedLabels, label); return i >= 0 && language.fixedKeys[i] == key; }

            // ---- 3 群の見出しと並び (上から self → room → read)
            float lastY = float.MaxValue;
            foreach (var g in HillsideGroups)
            {
                var t = home.Find(g.name) as RectTransform;
                var label = t == null ? null : t.GetComponent<TextMeshProUGUI>();
                check(label != null && t.gameObject.activeSelf && Mathf.Approximately(t.anchoredPosition.y, g.y), "group heading " + g.name + " at y=" + g.y);
                check(Bound(label, g.key), "group heading " + g.name + " bound to " + g.key);
                check(g.y < lastY, "group order top-down: " + g.name); lastY = g.y;
            }
            // 各群の項目は自分の見出しの下・次の見出しの上
            string[][] members =
            {
                new[] { "VolumeSettingsButton", "DistanceFadeSettingsButton", "NightModeLabel", "NightMode", "NightModeValue", "NightModeNote", "Language", "PreviousLanguage", "NextLanguage" },
                new[] { "CallAccess", "LiveAccess" },
                new[] { CreditButtonName, "InformationButton" },
            };
            for (int gi = 0; gi < members.Length; gi++)
            {
                float top = HillsideGroups[gi].y, bottom = gi + 1 < HillsideGroups.Length ? HillsideGroups[gi + 1].y : HomeBottom;
                foreach (string name in members[gi])
                {
                    var t = home.Find(name) as RectTransform;
                    bool inside = t != null && t.gameObject.activeSelf && t.anchoredPosition.y < top && t.anchoredPosition.y > bottom;
                    check(inside, "group " + HillsideGroups[gi].name + " holds " + name + (t == null ? " (missing)" : " y=" + t.anchoredPosition.y));
                }
            }
            var note = home.Find("LiveAccessNote") as RectTransform;
            check(note != null && Mathf.Approximately(note.anchoredPosition.y, HillsideGroupRoomY), "live access note sits on the room heading line (subtitle)");
            // 押せる部品どうしが重ならず、板の上下 (タブの下端〜Feedback の上) に収まる
            var hits = new List<RectTransform>();
            foreach (Transform child in home)
                if (child.gameObject.activeSelf && (child.GetComponent<Button>() != null || child.GetComponent<Slider>() != null)) hits.Add((RectTransform)child);
            for (int a = 0; a < hits.Count; a++)
            {
                Rect ra = RectOf(hits[a]);
                check(ra.yMax <= HomeTop && ra.yMin >= HomeBottom - 1f, "settings control inside home bounds: " + hits[a].name + " y=" + ra.yMin + ".." + ra.yMax);
                for (int b = a + 1; b < hits.Count; b++)
                    if (ra.Overlaps(RectOf(hits[b]))) check(false, "settings controls overlap: " + hits[a].name + " / " + hits[b].name);
            }
            check(hits.Count >= 9, "settings home interactive controls counted=" + hits.Count + " (volume, fade, night, <, >, call, live, credit, information)");

            // ---- 入口 4 つ (表示・配線)
            CheckEntry(home, "VolumeSettingsButton", pouchUdon, "_OpenVolumeSettings", language, "KD_TreatPouch.WordVolumeSettings", check);
            CheckEntry(home, "DistanceFadeSettingsButton", pouchUdon, "_OpenDistanceFadeSettings", language, "KD_DistanceFadeSettings.EntryButton", check);
            CheckEntry(home, CreditButtonName, settingsUdon, "_OpenCredits", language, "ui.extra.022", check);
            CheckEntry(home, "InformationButton", settingsUdon, "_OpenInformation", language, "information.title", check);
            check(home.Find("PhotoSettingsButton") == null && pouch.settingsPage.transform.Find("PhotoSettings") == null && settings.photoSettingsPage == null, "photo settings entry and page are not built (裏山: 写真なし)");

            // ---- 距離フェード (PC のみ。押せるかは KD_DistanceFadeSettings.Start が端末で決める — Android / iOS は interactable=false、文言は全端末)
            var fade = pouch.distanceFadeSettings;
            check(fade != null && fade.transform.parent != null && fade.transform.parent.name == KD_Hillside.DogsRootName, "distance fade system under " + KD_Hillside.DogsRootName);
            check(KD_DistanceFadeSettings.SaveKey == "KH_DistanceFade_v1", "distance fade PlayerData key is Hillside-only (KH_DistanceFade_v1)");
            check(fade != null && fade.entryButton != null && fade.entryButton.transform == home.Find("DistanceFadeSettingsButton"), "distance fade entryButton (interactable is decided on device by Start)");
            check(fade != null && fade.language == language, "distance fade language wired");

            // ---- 音量 (BGM + 犬の声。IMP-WLD-003。行の細部は Menu/16a、音源は Course/99)
            check(pouch.dogVoiceSlider != null && pouch.volumeSettingsPage != null && pouch.dogVoiceSlider.transform.parent == pouch.volumeSettingsPage.transform, "volume page has the dog voice slider");
            check(pouch.bgmSlider != null && pouch.volumeSettingsPage != null && pouch.bgmSlider.transform.parent == pouch.volumeSettingsPage.transform
                && pouch.bgmValueLabel != null && pouch.bgmToggleLabel != null && pouch.bgmToggleImage != null && pouch.bgmPlayer != null, "volume page has the BGM row (slider / value / ON-OFF) and the BGM player");
            check(pouch.liveVolumeSlider == null && pouch.bedroomBgmVolumeSlider == null && pouch.bedroomVoiceVolumeSlider == null && pouch.bedroomLiveVolumeSlider == null,
                "no live / bedroom sliders");

            // ---- クレジット頁
            var page = settings.creditPage;
            check(page != null && page.name == CreditPageName && page.transform.parent == pouch.settingsPage.transform && !page.activeSelf
                && Array.IndexOf(pouch.extraSettingsPages, page) >= 0, "credit page Page_Settings/" + CreditPageName + " (hidden, registered in extraSettingsPages)");
            object wired;
            check(settingsUdon.publicVariables.TryGetVariableValue("creditPage", out wired) && wired as Object == page, "settings backing Udon holds creditPage");
            if (page != null)
            {
                var rows = page.transform.Find("ScrollView/Viewport/" + CreditRowsName);
                check(rows != null && settings.creditScroll != null && settings.creditScroll.content == (RectTransform)rows
                    && settings.creditScroll.verticalScrollbarVisibility == ScrollRect.ScrollbarVisibility.Permanent, "credit rows are the scroll content (permanent scrollbar)");
                check(rows != null && Bound(rows.Find("Lead")?.GetComponent<TMP_Text>(), "ui.extra.021"), "credit lead bound to ui.extra.021");
                int pending = 0;
                for (int i = 0; rows != null && i < HillsideCredits.Length; i++)
                {
                    var row = HillsideCredits[i];
                    var name = rows.Find("Name" + i)?.GetComponent<TMP_Text>();
                    check(name != null && (row.key == null ? name.text == row.ja : Bound(name, row.key)), "credit row " + i + " name " + (row.key ?? "(fixed license notice)"));
                    var url = rows.Find("Url" + i)?.GetComponent<TMP_InputField>();
                    if (string.IsNullOrEmpty(row.url)) { pending++; check(url == null, "credit row " + i + " has no URL field while the URL is unconfirmed"); continue; }
                    check(url != null && url.readOnly && !url.richText && url.text == row.url && url.textViewport != null && url.textViewport.GetComponent<RectMask2D>() != null,
                        "credit row " + i + " URL read-only field " + row.url);
                }
                check(rows != null && rows.Find("Name" + HillsideCredits.Length) == null && rows.Find("Url" + HillsideCredits.Length) == null, "no extra credit rows (no VizVid)");
                foreach (var t in page.GetComponentsInChildren<TMP_Text>(true))
                    check(t.text.IndexOf("VizVid", StringComparison.OrdinalIgnoreCase) < 0, "no video player credit: " + t.name);
                log.AppendLine("CREDIT_URL_PENDING " + pending + " (LMArt / Allsky / polyperfect: 購入元の URL を人が確かめて KD_MenuFinishEditor.HillsideCredits に記入)");
                var back = page.transform.Find("BackSettings")?.GetComponent<Button>();
                check(back != null && OnClick(back, pouchUdon, "_OpenSettings"), "credit page back -> _OpenSettings");
            }

            // ---- インフォメーション (はじめに / 生成AI / VAnimal / トレーナー、写真設定なし)
            var info = settings.informationPage;
            check(info != null && !info.activeSelf && Array.IndexOf(pouch.extraSettingsPages, info) >= 0, "information page registered and hidden");
            if (info != null)
            {
                string[] events = { "_OpenWelcomeInformation", "_OpenAiInformation", "_OpenVanimalInformation", "_OpenTrainerInformation" };
                for (int i = 0; i < HillsideInformationRows.Length; i++)
                {
                    var b = info.transform.Find(HillsideInformationRows[i].name) as RectTransform;
                    check(b != null && Mathf.Approximately(b.anchoredPosition.y, HillsideInformationRows[i].y) && OnClick(b.GetComponent<Button>(), settingsUdon, events[i]),
                        "information row " + i + " " + HillsideInformationRows[i].name + " y=" + HillsideInformationRows[i].y + " -> " + events[i]);
                }
                var back = info.transform.Find("BackSettings")?.GetComponent<Button>();
                check(back != null && OnClick(back, pouchUdon, "_OpenSettings"), "information back -> _OpenSettings");
            }
            foreach (var reading in new[] { settings.aiInformationPage, settings.welcomeInformationPage, settings.vanimalInformationPage, settings.trainerInformationPage })
                check(reading != null && !reading.activeSelf && Array.IndexOf(pouch.extraSettingsPages, reading) >= 0, "reading page registered and hidden: " + (reading == null ? "(null)" : reading.name));
            check(settings.welcomeInformationScroll != null && settings.aiInformationScroll != null && settings.vanimalInformationScroll != null && settings.trainerInformationScroll != null, "reading scrolls wired");
        }

        static void CheckEntry(Transform home, string name, VRC.Udon.UdonBehaviour target, string evt, KD_Language language, string key, Action<bool, string> check)
        {
            var t = home.Find(name);
            var button = t == null ? null : t.GetComponent<Button>();
            check(button != null && t.gameObject.activeSelf, "settings entry shown: " + name);
            check(button != null && OnClick(button, target, evt), "settings entry " + name + " -> " + evt);
            var label = t == null ? null : t.Find("Label")?.GetComponent<TMP_Text>();
            int i = label == null ? -1 : Array.IndexOf(language.fixedLabels, label);
            check(i >= 0 && language.fixedKeys[i] == key, "settings entry " + name + " label bound to " + key);
        }

        static bool OnClick(Button button, VRC.Udon.UdonBehaviour target, string evt)
        {
            if (button == null) return false;
            var calls = new SerializedObject(button).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            return calls.arraySize == 1 && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Target").objectReferenceValue == target
                && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_MethodName").stringValue == "SendCustomEvent"
                && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == evt;
        }

        static Rect RectOf(RectTransform t)
        {
            var size = t.sizeDelta; var pos = t.anchoredPosition;
            return new Rect(pos.x - size.x * t.pivot.x, pos.y - size.y * t.pivot.y, size.x, size.y);
        }
    }
}
