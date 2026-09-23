using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-PRG-010: ギフトコード — 手元メニュー「ポイント」の面 (IMP-INF-006 P11)。
    ///
    /// 0 Hash Tool (平文 → ハッシュ。ログに平文を残さない) / 1 Program Asset / 2 Setup / 3 Verify。
    /// Setup は KD_GiftCodePanel をシステムとして置き、板のヘッダの残高の裏に入口ボタン、
    /// 設定ページの下に面を作り、<see cref="KD_GiftCodeDefinitions"/> のハッシュ・額・codeKey を配線する。
    /// 平文はどこにも書かない。</summary>
    public static class KD_GiftCodeSetupEditor
    {
        public const string SystemName = "KD_GiftCodePanel";
        public const string PageName = "GiftCodePage";
        public const string PointsButtonName = "PointsButton";
        const string ScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/Progress/KD_GiftCodePanel.cs";
        const string ProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/Progress/KD_GiftCodePanel.asset";
        const string SetupLog = "Logs/gift-code-setup.txt", VerifyLog = "Logs/gift-code-verify.txt";
        static readonly string[] RequiredKeys =
        {
            "gift.title", "gift.heading", "gift.source.initial", "gift.source.keeper", "gift.source.stay", "gift.source.code",
            "gift.received", "gift.pending", "gift.input.title", "gift.input.placeholder", "gift.use", "gift.back",
            "gift.result.ok", "gift.result.invalid", "gift.result.used", "gift.result.failed", "gift.history", "gift.history.none",
        };

        // ================================================================ 0: Hash Tool

        class HashWindow : EditorWindow
        {
            string _plain = "";
            void OnGUI()
            {
                EditorGUILayout.HelpBox("平文を貼る → 正規化 (英数字のみ・大文字) → ハッシュ。値を KD_GiftCodeDefinitions へ貼り、Setup を再実行する。\n平文はこの窓の外に出ない (ログ・ファイルへ書かない)。", MessageType.Info);
                _plain = EditorGUILayout.TextField("平文", _plain);
                string normalized = KD_GiftCodeDefinitions.Normalize(_plain);
                EditorGUILayout.LabelField("正規化", normalized);
                EditorGUILayout.LabelField("ハッシュ", normalized.Length == 0 ? "-" : KD_GiftCodeDefinitions.Hash(_plain).ToString());
                if (normalized.Length > 0 && normalized.Length < 4) EditorGUILayout.HelpBox("4 文字未満のコードは面が照合しない", MessageType.Warning);
            }
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Gift Code/0 - Hash Tool")]
        public static void HashTool() { GetWindowTyped(); }
        static void GetWindowTyped() { var w = EditorWindow.GetWindow<HashWindow>(true, "Gift Code Hash"); w.minSize = new Vector2(420, 140); }

        // ================================================================ 1: Program asset

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Gift Code/1 - Create Program Assets")]
        public static void CreateProgramAssets()
        {
            var log = new StringBuilder();
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath) != null) log.AppendLine("already exists: " + ProgramPath);
            else
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptPath);
                if (script == null) throw new InvalidOperationException("script not found: " + ScriptPath);
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = script;
                AssetDatabase.CreateAsset(asset, ProgramPath); AssetDatabase.SaveAssets();
                log.AppendLine("created: " + ProgramPath + " (force refresh してから Setup)");
            }
            KD_VerifyLog.Write("Logs/gift-code-program.txt", log);
        }

        // ================================================================ 2: Setup

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Gift Code/2 - Setup Gift Code Panel")]
        public static void Setup()
        {
            Guard();
            var log = new StringBuilder(); log.AppendLine("[KD_GiftCodeSetup] Setup");
            KD_GiftCodeDefinitions.Validate();
            var pouch = Find<KD_TreatPouch>(); var language = Find<KD_Language>();
            var ledger = KD_ProductsEditor.SceneComponents<KD_ProgressLedger>().Single(l => l.storageNamespace == KD_ProgressLedger.ProductionNamespace);
            var catalog = KD_LocalizationEditor.Load();
            foreach (var key in RequiredKeys.Concat(KD_GiftCodeDefinitions.All.Select(r => r.nameKey)))
                KD_LocalizationEditor.Text(catalog, catalog.fallbackLanguage, key); // throws when the catalog lacks it
            KD_LocalizationEditor.Configure(language, catalog, log);
            KD_ShopSetupEditor.AddGlyphs(KD_LocalizationEditor.BundledCharacters(catalog) + "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-+[]!?", log);

            int found; var host = KD_SceneObjects.Find(SystemName, out found);
            if (found > 1) throw new InvalidOperationException(SystemName + " が " + found + " 個ある");
            if (host == null) { host = new GameObject(SystemName); KD_SceneObjects.PlaceIfNew(host, KD_DogDefs.SystemsPath); log.AppendLine("created " + SystemName); }
            var panel = host.GetComponent<KD_GiftCodePanel>() ?? host.AddUdonSharpComponent<KD_GiftCodePanel>();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(panel);
            backing.SyncMethod = Networking.SyncType.None; EditorUtility.SetDirty(backing);
            panel.pouch = pouch; panel.ledger = ledger; panel.language = language;
            var rows = KD_GiftCodeDefinitions.All;
            panel.codeKeys = rows.Select(r => r.codeKey).ToArray(); panel.codeHashes = rows.Select(r => r.hash).ToArray();
            panel.codePoints = rows.Select(r => r.points).ToArray(); panel.codeNameKeys = rows.Select(r => r.nameKey).ToArray();

            var font = pouch.sectionTitleLabel.font;
            string T(string key) { return KD_LocalizationEditor.Text(catalog, catalog.fallbackLanguage, key); }

            // ---- 入口: ヘッダの残高をボタンにする (残高ラベルの裏に同じ枠の透明寄りボタン。ラベルは直下の子のまま) ----
            var balance = pouch.balanceLabel; var balanceRect = balance.rectTransform;
            var entry = KD_ShopSetupEditor.EnsureButton(pouch.panelRoot.transform, PointsButtonName, balanceRect.anchoredPosition,
                balanceRect.sizeDelta + new Vector2(16f, 6f), new Color(.34f, .30f, .28f, .55f), font, "", backing, "_Open", log);
            // 順序を決め打ちにする (再実行で相対順がずれない): 入口 → 残高ラベルの順で末尾へ。ラベルが上に描かれる
            entry.transform.SetAsLastSibling(); balance.transform.SetAsLastSibling();
            if (pouch.confirmRoot != null) pouch.confirmRoot.transform.SetAsLastSibling();   // P13: 確認カードは常に最後の子 (最前面)
            balance.raycastTarget = false;

            // ---- 面 ----
            var page = KD_ShopSetupEditor.EnsureUiChild(pouch.settingsPage.transform, PageName, Vector2.zero, new Vector2(KD_TreatSetupEditor.PanelWidth, KD_TreatSetupEditor.PanelHeight));
            pouch.extraSettingsPages = (pouch.extraSettingsPages ?? new GameObject[0]).Where(p => p != null && p != page).Concat(new[] { page }).ToArray();
            panel.page = page;
            panel.heading = Label(page.transform, "Heading", new Vector2(0, 170), new Vector2(1040, 44), T("gift.heading"), font, 28, TextAlignmentOptions.Left);   // 2026-09-17: タブが 224 (下端 201) へ下がったので 180 → 170
            panel.sourcesLabel = Label(page.transform, "Sources", new Vector2(0, 82), new Vector2(1040, 150), "", font, 24, TextAlignmentOptions.TopLeft);
            panel.sourcesLabel.enableWordWrapping = true; panel.sourcesLabel.lineSpacing = 8f;
            panel.inputTitle = Label(page.transform, "InputTitle", new Vector2(-330, -14), new Vector2(380, 40), T("gift.input.title"), font, 26, TextAlignmentOptions.Left);
            panel.input = Input(page.transform, "CodeInput", new Vector2(-130, -64), new Vector2(780, 64), font, backing, out var placeholder);
            panel.placeholder = placeholder;
            var use = KD_ShopSetupEditor.EnsureButton(page.transform, "UseButton", new Vector2(390, -64), new Vector2(240, 64), KD_UiSpriteEditor.ActionEquip, font, T("gift.use"), backing, "_Use", log);
            panel.useButton = use.GetComponent<Button>(); panel.useLabel = use.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            panel.band = Label(page.transform, "Band", new Vector2(0, -116), new Vector2(1040, 40), "", font, 24, TextAlignmentOptions.Left);
            panel.band.color = new Color(1f, .92f, .6f);
            panel.historyTitle = Label(page.transform, "HistoryTitle", new Vector2(0, -156), new Vector2(1040, 32), T("gift.history"), font, 22, TextAlignmentOptions.Left);
            panel.historyLabel = Label(page.transform, "History", new Vector2(0, -216), new Vector2(1040, 84), T("gift.history.none"), font, 20, TextAlignmentOptions.TopLeft);
            panel.historyLabel.enableWordWrapping = true;
            var back = KD_ShopSetupEditor.EnsureButton(page.transform, "Back", new Vector2(-430, -285), new Vector2(200, 48), KD_UiSpriteEditor.ActionNeutral, font, T("gift.back"), backing, "_Back", log);
            panel.backLabel = back.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            page.SetActive(false);

            foreach (var c in new UdonSharpBehaviour[] { panel, pouch, language }) { EditorUtility.SetDirty(c); KD_ProductsEditor.Copy(c); }
            EditorSceneManager.MarkSceneDirty(host.scene); AssetDatabase.SaveAssets();
            log.AppendLine("codes=" + rows.Length + " page=" + PageName + " entry=" + PointsButtonName + " (not saved)");
            KD_VerifyLog.Write(SetupLog, log);
        }

        static TMP_InputField Input(Transform parent, string name, Vector2 pos, Vector2 size, TMP_FontAsset font, VRC.Udon.UdonBehaviour target, out TextMeshProUGUI placeholder)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var bg = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            bg.color = new Color(.20f, .18f, .16f, 1f); bg.raycastTarget = true;
            KD_UiSpriteEditor.ApplyRoundSkin(bg, KD_UiSpriteEditor.LoadRound());
            var field = go.GetComponent<TMP_InputField>() ?? go.AddComponent<TMP_InputField>();
            var area = KD_ShopSetupEditor.EnsureUiChild(go.transform, "TextArea", Vector2.zero, size - new Vector2(28f, 8f));
            if (area.GetComponent<RectMask2D>() == null) area.AddComponent<RectMask2D>();
            var text = Label(area.transform, "Text", Vector2.zero, size - new Vector2(28f, 8f), "", font, 28, TextAlignmentOptions.MidlineLeft);
            placeholder = Label(area.transform, "Placeholder", Vector2.zero, size - new Vector2(28f, 8f), "", font, 28, TextAlignmentOptions.MidlineLeft);
            placeholder.color = new Color(.7f, .66f, .6f, .8f); placeholder.fontStyle = FontStyles.Italic;
            field.textViewport = area.GetComponent<RectTransform>(); field.textComponent = text; field.placeholder = placeholder;
            field.targetGraphic = bg; field.contentType = TMP_InputField.ContentType.Alphanumeric;
            field.lineType = TMP_InputField.LineType.SingleLine; field.characterLimit = 32;
            field.richText = false; text.richText = false; field.onFocusSelectAll = true;
            field.readOnly = false; field.interactable = false;
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            field.SetTextWithoutNotify("");
            while (field.onEndEdit.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(field.onEndEdit, 0);
            while (field.onSubmit.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(field.onSubmit, 0);
            while (field.onValueChanged.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(field.onValueChanged, 0);
            UnityEventTools.AddStringPersistentListener(field.onValueChanged, target.SendCustomEvent, "_DraftChanged");
            return field;
        }

        static TextMeshProUGUI Label(Transform parent, string name, Vector2 pos, Vector2 size, string value, TMP_FontAsset font, float points, TextAlignmentOptions align)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var text = go.GetComponent<TextMeshProUGUI>() ?? go.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = points; text.text = value; text.color = new Color(.97f, .95f, .90f);
            text.enableAutoSizing = false; text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = align; text.raycastTarget = false; text.richText = false;
            return text;
        }

        // ================================================================ 3: Verify

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Gift Code/3 - Verify Gift Code Panel")]
        public static void Verify()
        {
            Guard();
            var v = new KD_VerifyLog("[KD_GiftCodeSetup] Verify"); var log = v.Text;
            Action<bool, string> Check = (ok, label) => v.Check(ok, label);

            // ---- 定義表 ----
            bool tableOk = true; try { KD_GiftCodeDefinitions.Validate(); } catch (Exception e) { tableOk = false; log.AppendLine("  " + e.Message); }
            Check(tableOk, "definition table valid (keys / hashes / points)");
            Check(KD_GiftCodeDefinitions.Fnv1a31("") == 0x011c9dc5 && KD_GiftCodeDefinitions.Fnv1a31("a") == 0x640c292c, "FNV-1a 31bit self test");
            Check(KD_GiftCodeDefinitions.Normalize("kominka-10k-AbCd ") == "KOMINKA10KABCD" && KD_GiftCodeDefinitions.Normalize("あ-1") == "1", "normalize self test");
            Check(KD_GiftCodeDefinitions.Salt == KD_GiftCodePanel.HashSalt, "salt matches runtime");

            var panel = KD_ProductsEditor.SceneComponents<KD_GiftCodePanel>().SingleOrDefault();
            Check(panel != null, "single " + SystemName);
            if (panel == null) { v.Finish(VerifyLog); return; }
            var pouch = Find<KD_TreatPouch>(); var language = Find<KD_Language>();
            var ledger = KD_ProductsEditor.SceneComponents<KD_ProgressLedger>().Single(l => l.storageNamespace == KD_ProgressLedger.ProductionNamespace);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(panel);
            Check(backing != null && backing.SyncMethod == Networking.SyncType.None, "sync=None");
            Check(!PrefabUtility.IsPartOfPrefabInstance(panel) && KD_SceneObjects.HasStaticAncestry(panel.gameObject, out _), "static standalone system");
            Check(panel.pouch == pouch && panel.ledger == ledger && panel.language == language, "runtime references");
            // Udon 側の式が Editor 側と一致する (実行せず、プロキシで呼ぶ = 同じ C#)
            Check(ledger.Fnv1a31("") == KD_GiftCodeDefinitions.Fnv1a31("") && ledger.Fnv1a31("KD_Gift_v1:ABC123") == KD_GiftCodeDefinitions.Fnv1a31("KD_Gift_v1:ABC123")
                && ledger.NormalizeCode("kominka-10k-AbCd") == KD_GiftCodeDefinitions.Normalize("kominka-10k-AbCd"), "runtime hash/normalize equals editor formula");

            // ---- 配線が定義表と一致 ----
            var rows = KD_GiftCodeDefinitions.All;
            bool arrays = panel.codeKeys != null && panel.codeHashes != null && panel.codePoints != null && panel.codeNameKeys != null
                && panel.codeKeys.Length == rows.Length && panel.codeHashes.Length == rows.Length && panel.codePoints.Length == rows.Length && panel.codeNameKeys.Length == rows.Length;
            if (arrays) for (int i = 0; i < rows.Length; i++)
                arrays &= panel.codeKeys[i] == rows[i].codeKey && panel.codeHashes[i] == rows[i].hash && panel.codePoints[i] == rows[i].points && panel.codeNameKeys[i] == rows[i].nameKey;
            Check(arrays, "panel arrays equal definition table (" + rows.Length + " codes)");
            foreach (string symbol in new[] { "codeKeys", "codeHashes", "codePoints", "codeNameKeys" })
            {
                object value; backing.publicVariables.TryGetVariableValue(symbol, out value);
                var actual = value as Array; var expected = (Array)typeof(KD_GiftCodePanel).GetField(symbol).GetValue(panel);
                bool matches = actual != null && expected != null && actual.Length == expected.Length;
                if (matches) for (int i = 0; i < actual.Length; i++) matches &= Equals(actual.GetValue(i), expected.GetValue(i));
                Check(matches, "backing array " + symbol);
            }
            var catalog = KD_LocalizationEditor.Load();
            foreach (var key in RequiredKeys.Concat(rows.Select(r => r.nameKey)))
            {
                // IMP-INF-010: 完訳を要求するのは self 言語 (ja/en)。client 言語の未訳は代替言語で出し、部分訳のままの公開はビルドガードが止める
                foreach (var lang in catalog.languages.Where(l => !KD_LocalizationEditor.Bundled(l))) log.AppendLine("CLIENT_UNMEASURED " + lang.code + " gift labels + sources/history worst case (Editor cannot measure client-language widths; verify on device)");
                bool present = true; foreach (var lang in catalog.languages.Where(KD_LocalizationEditor.Bundled)) { var e = Array.Find(lang.texts, t => t.key == key); present &= e != null && !string.IsNullOrEmpty(e.text); }
                Check(present, "catalog key in every bundled language: " + key);
            }

            // ---- 面と入口 ----
            var page = panel.page;
            Check(page != null && page.name == PageName && page.transform.parent == pouch.settingsPage.transform && !page.activeSelf, "page under settings, hidden");
            Check(pouch.extraSettingsPages != null && pouch.extraSettingsPages.Contains(page), "page registered in extraSettingsPages");
            var entryT = pouch.panelRoot.transform.Find(PointsButtonName);
            Check(entryT != null, "entry button on panel root");
            if (entryT != null)
            {
                CheckButton(entryT.GetComponent<Button>(), backing, "_Open", Check);
                var er = (RectTransform)entryT; var br = pouch.balanceLabel.rectTransform;
                Check((er.anchoredPosition - br.anchoredPosition).magnitude < .5f && er.sizeDelta.x >= br.sizeDelta.x && er.sizeDelta.y >= br.sizeDelta.y, "entry covers the balance label");
                Check(entryT.GetSiblingIndex() < br.GetSiblingIndex() && !pouch.balanceLabel.raycastTarget, "balance label drawn above entry and does not block clicks");
            }
            CheckButton(panel.useButton, backing, "_Use", Check);
            CheckButton(page.transform.Find("Back").GetComponent<Button>(), backing, "_Back", Check);
            var field = panel.input;
            Check(field != null && field.contentType == TMP_InputField.ContentType.Alphanumeric && field.characterLimit == 32 && !field.richText
                && field.textViewport.GetComponent<RectMask2D>() != null && field.placeholder == panel.placeholder, "input field alphanumeric / limit / mask / placeholder");
            if (field != null)
            {
                var so = new SerializedObject(field);
                Check(so.FindProperty("m_OnEndEdit.m_PersistentCalls.m_Calls").arraySize == 0 && field.onSubmit.GetPersistentEventCount() == 0, "no automatic commit binding");
                var changes = so.FindProperty("m_OnValueChanged.m_PersistentCalls.m_Calls");
                Check(changes.arraySize == 1 && changes.GetArrayElementAtIndex(0).FindPropertyRelative("m_Target").objectReferenceValue == backing
                    && changes.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == "_DraftChanged", "draft binding");
            }
            Check(page.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Length == 0, "no network behaviours inside the page");
            var font = pouch.sectionTitleLabel.font;
            string all = string.Concat(catalog.languages.Where(KD_LocalizationEditor.Bundled).SelectMany(l => l.texts.Where(t => t.key.StartsWith("gift.")).Select(t => t.text))) + "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ+-[]";
            bool glyphs = true; foreach (char c in all) if (!char.IsWhiteSpace(c) && !c.Equals('\n') && !KD_LocalizationEditor.HasGlyph(font, c)) { glyphs = false; log.AppendLine("  missing glyph U+" + ((int)c).ToString("X4")); }
            Check(glyphs, "font has every glyph used by the page (bundled languages + code alphabet)");
            foreach (var pair in new (string key, TMP_Text text)[] { ("gift.heading", panel.heading), ("gift.input.title", panel.inputTitle), ("gift.history", panel.historyTitle), ("gift.use", panel.useLabel), ("gift.back", panel.backLabel) })
                foreach (var lang in catalog.languages.Where(KD_LocalizationEditor.Bundled)) // client 言語の幅は Editor で測れない (IMP-INF-010)
                {
                    string s = KD_LocalizationEditor.Text(catalog, lang.code, pair.key);
                    var probe = new GameObject("TextMeasure", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)); probe.hideFlags = HideFlags.HideAndDontSave;
                    var t = probe.GetComponent<TextMeshProUGUI>(); t.font = font; t.fontSize = pair.text.fontSize; t.isOrthographic = true; t.enableWordWrapping = false;
                    Vector2 preferred = t.GetPreferredValues(s, 10000, 0);
                    Check(preferred.x <= pair.text.rectTransform.rect.width + 1, lang.code + " fits " + pair.text.name + " (" + preferred.x.ToString("F0") + "/" + pair.text.rectTransform.rect.width + ")");
                    UnityEngine.Object.DestroyImmediate(probe);
                }

            // ---- 折り返す 2 欄の最悪ケースが枠の高さに収まる (入手経路 4 行 / 受け取ったコード 7 本) ----
            foreach (var lang in catalog.languages.Where(KD_LocalizationEditor.Bundled))
            {
                string S(string key) { return KD_LocalizationEditor.Text(catalog, lang.code, key); }
                string sources = S("gift.source.initial") + "  +" + ledger.initialPoints + " pt  [" + S("gift.received") + "]\n"
                    + S("gift.source.keeper") + "  +" + ledger.keeperPoints + " pt  [" + S("gift.received") + "]\n" + S("gift.source.stay") + "\n" + S("gift.source.code");
                string history = string.Join("  /  ", rows.Select(r => S(r.nameKey) + " +" + r.points + " pt"));
                foreach (var pair in new (string label, string text, TMP_Text target)[] { ("sources", sources, panel.sourcesLabel), ("history", history, panel.historyLabel) })
                {
                    var probe = new GameObject("TextMeasure", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)); probe.hideFlags = HideFlags.HideAndDontSave;
                    var t = probe.GetComponent<TextMeshProUGUI>(); t.font = font; t.fontSize = pair.target.fontSize; t.isOrthographic = true; t.enableWordWrapping = true; t.lineSpacing = pair.target.lineSpacing;
                    Vector2 preferred = t.GetPreferredValues(pair.text, pair.target.rectTransform.rect.width, 0);
                    Check(preferred.y <= pair.target.rectTransform.rect.height + 1, lang.code + " worst-case " + pair.label + " fits (" + preferred.y.ToString("F0") + "/" + pair.target.rectTransform.rect.height + ")");
                    UnityEngine.Object.DestroyImmediate(probe);
                }
            }
            // 面の部品がタブ列 (y ≧ 223) と Desktop の閉じるヒント (y ≦ -314) に掛からない
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (Transform child in page.transform) { var r = (RectTransform)child; top = Mathf.Max(top, r.anchoredPosition.y + r.sizeDelta.y / 2f); bottom = Mathf.Min(bottom, r.anchoredPosition.y - r.sizeDelta.y / 2f); }
            var tabTop = (RectTransform)pouch.panelRoot.transform.Find("Tab0"); var hint = pouch.closeHintLabel.rectTransform;
            Check(top < tabTop.anchoredPosition.y - tabTop.sizeDelta.y / 2f && bottom > hint.anchoredPosition.y + hint.sizeDelta.y / 2f,
                "page content stays between the tab row and the close hint (top=" + top.ToString("F0") + " bottom=" + bottom.ToString("F0") + ")");

            // ---- 平文がどこにも無い: 英数字とハイフンの並び (8 字以上) をすべて拾い、定義表のハッシュと突き合わせる ----
            // 形の推測 (KOMINKA-XXXX-XXXX 型) に頼らず、実際の平文なら必ず一致する。ログには場所だけを残す
            var suspicious = new List<string>();
            var issued = new HashSet<int>(rows.Select(r => r.hash));
            var token = new System.Text.RegularExpressions.Regex(@"[A-Za-z0-9][A-Za-z0-9-]{6,}[A-Za-z0-9]");
            void Scan(string where, string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                foreach (System.Text.RegularExpressions.Match m in token.Matches(text)) if (issued.Contains(KD_GiftCodeDefinitions.Hash(m.Value))) { suspicious.Add(where); return; }
            }
            foreach (var text in Resources.FindObjectsOfTypeAll<TMP_Text>()) if (text.gameObject.scene == pouch.gameObject.scene) Scan("scene:" + text.name, text.text);
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_InputField>()) if (f.gameObject.scene == pouch.gameObject.scene) Scan("inputfield:" + f.name, f.text);
            foreach (var lang in catalog.languages) foreach (var e in lang.texts) Scan("catalog:" + lang.code + ":" + e.key, e.text);
            foreach (string path in Directory.GetFiles("Assets/KominkaHillside/Imported/KominkaDogRun", "*.cs", SearchOption.AllDirectories).Concat(Directory.GetFiles("Assets/KominkaHillside/Imported/KominkaDogRun/Data", "*.json", SearchOption.AllDirectories)))
                Scan(path.Replace(System.IO.Path.DirectorySeparatorChar, '/'), File.ReadAllText(path));
            Check(suspicious.Count == 0, "no issued plaintext code in scene / input fields / catalog / sources (hash match)" + (suspicious.Count > 0 ? " -> " + string.Join(", ", suspicious) : ""));
            v.Finish(VerifyLog);
            if (v.Fail > 0) Debug.LogError("[KD_GiftCodeSetup] verify failed: " + v.Fail);
        }

        static void CheckButton(Button button, VRC.Udon.UdonBehaviour target, string method, Action<bool, string> check)
        {
            if (button == null) { check(false, "button missing for " + method); return; }
            var calls = new SerializedObject(button).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            check(calls.arraySize == 1 && button.onClick.GetPersistentTarget(0) == target && button.onClick.GetPersistentMethodName(0) == "SendCustomEvent"
                && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == method, button.name + " -> " + method);
        }
        static void Guard()
        {
            if (EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().path != "Assets/Scenes/KominkaDogRun.unity")
                throw new InvalidOperationException("Open the target scene in Edit mode");
        }
        static T Find<T>() where T : Component { return KD_ProductsEditor.SceneComponents<T>().Single(); }
    }
}
