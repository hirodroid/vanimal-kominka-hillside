using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    /// <summary>おもちゃタブの上段 (本番シーンの局所編集。旧メニュー Menu/7・12・13・17 は使わない)。
    ///
    /// Course/122 (DES-UI-002 投げ方、docs/plans/2026-10-03-frisbee-settings.md §3.2 / §6):
    /// - Page_Toy の上段 (y 164) 左半分に「投げ方」ラベルと [まっすぐ] [曲げる] (各 200×48、下線つき) を作り、pouch へ配線する
    /// - おもちゃのスクロールの上端を 186 → 140 に下げる (呼ぶ・ライブ・かざり・さんぽと同じ ContentTopWithRow)。6 行 = 2 列 × 3 段 = 376 ≤ 390
    /// - 6 枚の KH_Frisbee.throwStyle に KH_ToyRecall を配線する (投げる人のローカルの選択を _Launch が読む)
    /// - コースの翻訳表 (course.toy.throw.* / course.frisbee.*) を KD_Language の extension に入れ直す (Course/80 と同じ式)
    /// Course/123 は保存済みの状態を読み直して検査し、Temp/des-ui-002/toy-row-verify.json に出す。
    /// 右半分 (ボールの柄、DES-DOG-002) は Course/124 / 125 (このファイルの後半)。
    /// どちらの Build も Course/80 と同じく、実行前にシーンを Temp へ写してから保存する (Idle は未保存のシーンを拒む)。</summary>
    public static partial class KH_ToyTopRowEditor
    {
        const string OutDir = "Temp/des-ui-002";
        // 上段の帯 (y 164)。高さは 48 (設計書の 52 から 4 下げた: 帯の下端 140 = スクロールの上端 140 に接して重ならない)
        public const float TopRowY = 164f, RowHeight = 48f, ContentTopWithRow = 140f, PageBottom = -250f;
        public const float LeftEdge = -550f, Middle = -10f, RightEdge = 550f;
        public static readonly Vector2 ThrowLabelPos = new Vector2(-490f, TopRowY), ThrowLabelSize = new Vector2(110f, RowHeight);
        public static readonly Vector2 StraightPos = new Vector2(-325f, TopRowY), CurvePos = new Vector2(-115f, TopRowY), ThrowButtonSize = new Vector2(200f, RowHeight);
        public const string ThrowLabelName = "ThrowStyleLabel", StraightName = "ThrowStraight", CurveName = "ThrowCurve", UnderlineName = "Underline";
        public const string StraightEvent = "_ThrowStraight", CurveEvent = "_ThrowCurve";
        static readonly string[] ThrowKeys = { "course.toy.throw.label", "course.toy.throw.straight", "course.toy.throw.curve", "course.frisbee.allDogs", "course.frisbee.mixed" };

        [Serializable] public class Report
        {
            public string utc, scene, status = "RUNNING", mode;
            public int errors;
            public List<string> findings = new List<string>();
            public List<string> notes = new List<string>();
            public List<string> labelFit = new List<string>();
            public string limitations = "Editor: saved scene objects, proxy values and backing heap where present, catalog text, glyphs and label fit (ja/en fail, client-font languages info). No Play, input, network or device.";
        }

        // ============================================================
        // Course/122 生成 (投げ方)
        // ============================================================

        [MenuItem("Tools/KominkaHillside/Course/122 - Build Toy Tab Throw Style Row")]
        public static void BuildThrowStyle()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var p = KH_CourseTravelEditor.FindPouch();
            var recall = p.recall;
            if (recall == null) throw new InvalidOperationException("pouch.recall (KH_ToyRecall) missing: the world is not wired");
            if (p.tabPages == null || p.tabPages.Length <= KD_TreatPouch.TabToy || p.tabPages[KD_TreatPouch.TabToy] == null) throw new InvalidOperationException("Page_Toy missing");
            var pouchBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            var recallBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(recall);
            if (pouchBacking == null || recallBacking == null) throw new InvalidOperationException("Missing backing behaviours (compile U# first)");
            if (new SerializedObject(p).FindProperty("throwStraightImage") == null) throw new InvalidOperationException("KD_TreatPouch has no throw-style fields yet: compile first");
            var font = p.sectionTitleLabel.font;
            var log = new StringBuilder();
            Directory.CreateDirectory(OutDir);
            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-throw-style.unity", true);
            try
            {
                var page = p.tabPages[KD_TreatPouch.TabToy].transform;
                var label = MakeText(page, ThrowLabelName, ThrowLabelPos, ThrowLabelSize, 22f, TextAlignmentOptions.MidlineLeft, font, Ja("course.toy.throw.label"));
                label.enableAutoSizing = true; label.fontSizeMax = 22f; label.fontSizeMin = 14f; EditorUtility.SetDirty(label);
                var straight = KD_ShopSetupEditor.EnsureButton(page, StraightName, StraightPos, ThrowButtonSize, KD_UiSpriteEditor.ActionEquip, font, Ja("course.toy.throw.straight"), pouchBacking, StraightEvent, log);
                var curve = KD_ShopSetupEditor.EnsureButton(page, CurveName, CurvePos, ThrowButtonSize, KD_UiSpriteEditor.ActionNeutral, font, Ja("course.toy.throw.curve"), pouchBacking, CurveEvent, log);
                var straightLabel = ButtonLabel(straight, 20f); var curveLabel = ButtonLabel(curve, 20f);
                var straightLine = Underline(straight); var curveLine = Underline(curve);
                straightLine.SetActive(true); curveLine.SetActive(false);   // 初期値は「まっすぐ」(実行時は RedrawToyTopRow が描き直す)
                LowerToyScroll(p);

                p.throwStyleLabel = label;
                p.throwStraightImage = straight.GetComponent<Image>(); p.throwCurveImage = curve.GetComponent<Image>();
                p.throwStraightLabel = straightLabel; p.throwCurveLabel = curveLabel;
                p.throwStraightUnderline = straightLine; p.throwCurveUnderline = curveLine;

                // 6 枚の円盤へ「投げる人の選択」の持ち先を配線 (proxy と backing)
                int wired = 0;
                if (recall.discs != null)
                    foreach (var disc in recall.discs)
                    {
                        if (disc == null) continue;
                        disc.throwStyle = recall; UdonSharpEditorUtility.CopyProxyToUdon(disc); EditorUtility.SetDirty(disc); wired++;
                    }
                log.AppendLine("discs wired to KH_ToyRecall.throwStyle: " + wired);

                KH_CourseTravelEditor.ApplyCatalog(p);
                UdonSharpEditorUtility.CopyProxyToUdon(p); EditorUtility.SetDirty(p);
                EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
                if (!EditorSceneManager.SaveScene(p.gameObject.scene)) throw new IOException("Throw style build save failed");
            }
            catch
            {
                Debug.LogError("[KH ToyRow] Build failed; the scene before this run is at " + OutDir + "/before-throw-style.unity. Reopen the scene without saving to discard partial changes.");
                throw;
            }
            File.WriteAllText(OutDir + "/throw-style-build.txt", log.ToString());
            Debug.Log("[KH ToyRow] Built the throw style row (saved). Run Course/27 (glyphs) and then Course/123 Verify.");
        }

        /// <summary>おもちゃのスクロールの上端を 140 へ (KH_CourseTravelEditor.LowerScroll のおもちゃタブ版)。冪等。</summary>
        public static void LowerToyScroll(KD_TreatPouch p)
        {
            var scroll = p.tabScrolls != null && p.tabScrolls.Length > KD_TreatPouch.TabToy ? p.tabScrolls[KD_TreatPouch.TabToy] : null;
            if (scroll == null || scroll.viewport == null || scroll.verticalScrollbar == null) throw new InvalidOperationException("Toy scroll missing");
            float height = ContentTopWithRow - PageBottom;
            var root = (RectTransform)scroll.transform;
            root.anchoredPosition = new Vector2(root.anchoredPosition.x, (ContentTopWithRow + PageBottom) / 2); root.sizeDelta = new Vector2(root.sizeDelta.x, height);
            scroll.viewport.sizeDelta = new Vector2(scroll.viewport.sizeDelta.x, height);
            var bar = (RectTransform)scroll.verticalScrollbar.transform; bar.sizeDelta = new Vector2(bar.sizeDelta.x, height);
            var track = bar.Find("SlidingArea") as RectTransform; if (track != null) track.sizeDelta = new Vector2(track.sizeDelta.x, height - 8);
            EditorUtility.SetDirty(root.gameObject);
        }

        // ============================================================
        // Course/123 検査 (投げ方)
        // ============================================================

        [MenuItem("Tools/KominkaHillside/Course/123 - Verify Toy Tab Throw Style Row")]
        public static void VerifyThrowStyle()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var p = KH_CourseTravelEditor.FindPouch();
            var report = new Report { utc = DateTime.UtcNow.ToString("o"), scene = p.gameObject.scene.path, mode = "verify-throw" };
            var e = report.findings;
            try { ValidateThrowStyle(p, e, report); }
            catch (Exception ex) { e.Add("Exception: " + ex.Message); }
            report.errors = e.Count; report.status = e.Count == 0 ? "TOY_THROW_ROW_OK" : "FAIL";
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/toy-row-verify.json", JsonUtility.ToJson(report, true));
            if (e.Count != 0) throw new InvalidOperationException("[KH ToyRow] FAIL " + e.Count + ":\n" + string.Join("\n", e));
            Debug.Log("[KH ToyRow] TOY_THROW_ROW_OK (" + report.notes.Count + " notes)");
        }

        public static void ValidateThrowStyle(KD_TreatPouch p, List<string> e, Report report)
        {
            var page = p.tabPages[KD_TreatPouch.TabToy].transform;
            var pouchBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            // 部品と配線
            Req(e, p.throwStyleLabel != null && p.throwStyleLabel.transform.parent == page && p.throwStyleLabel.name == ThrowLabelName, "throwStyleLabel = Page_Toy/" + ThrowLabelName);
            Req(e, p.throwStraightImage != null && p.throwStraightImage.name == StraightName && p.throwStraightImage.transform.parent == page, "throwStraightImage = Page_Toy/" + StraightName);
            Req(e, p.throwCurveImage != null && p.throwCurveImage.name == CurveName && p.throwCurveImage.transform.parent == page, "throwCurveImage = Page_Toy/" + CurveName);
            Req(e, p.throwStraightLabel != null && p.throwStraightImage != null && p.throwStraightLabel.transform.parent == p.throwStraightImage.transform, "straight label inside the button");
            Req(e, p.throwCurveLabel != null && p.throwCurveImage != null && p.throwCurveLabel.transform.parent == p.throwCurveImage.transform, "curve label inside the button");
            Req(e, p.throwStraightUnderline != null && p.throwStraightImage != null && p.throwStraightUnderline.transform.parent == p.throwStraightImage.transform && p.throwStraightUnderline.GetComponent<Image>() != null && !p.throwStraightUnderline.GetComponent<Image>().raycastTarget, "straight underline (non-raycast Image) inside the button");
            Req(e, p.throwCurveUnderline != null && p.throwCurveImage != null && p.throwCurveUnderline.transform.parent == p.throwCurveImage.transform && p.throwCurveUnderline.GetComponent<Image>() != null && !p.throwCurveUnderline.GetComponent<Image>().raycastTarget, "curve underline (non-raycast Image) inside the button");
            if (p.throwStraightImage != null) CheckEvent(e, p.throwStraightImage.gameObject, pouchBacking, StraightEvent);
            if (p.throwCurveImage != null) CheckEvent(e, p.throwCurveImage.gameObject, pouchBacking, CurveEvent);
            Req(e, typeof(KD_TreatPouch).GetMethod(StraightEvent) != null && typeof(KD_TreatPouch).GetMethod(CurveEvent) != null, "KD_TreatPouch has " + StraightEvent + " / " + CurveEvent);
            // 寸法: 左半分に収まり、帯はスクロールの上端より上・タブの下端より下
            foreach (var r in new[] { p.throwStyleLabel == null ? null : p.throwStyleLabel.rectTransform, p.throwStraightImage == null ? null : p.throwStraightImage.rectTransform, p.throwCurveImage == null ? null : p.throwCurveImage.rectTransform })
            {
                if (r == null) continue;
                float l = r.anchoredPosition.x - r.sizeDelta.x / 2, rr = r.anchoredPosition.x + r.sizeDelta.x / 2, b = r.anchoredPosition.y - r.sizeDelta.y / 2, t = r.anchoredPosition.y + r.sizeDelta.y / 2;
                Req(e, l >= LeftEdge - .5f && rr <= Middle + .5f, r.name + " in the left half x " + l + ".." + rr + " (" + LeftEdge + ".." + Middle + ")");
                Req(e, b >= ContentTopWithRow - .5f && t <= TabBottom(p) - 4f, r.name + " between the toy scroll (140) and the tabs: y " + b + ".." + t);
                report.notes.Add(r.name + " x " + l + ".." + rr + " y " + b + ".." + t);
            }
            Overlaps(e, new[] { p.throwStyleLabel == null ? null : p.throwStyleLabel.rectTransform, p.throwStraightImage == null ? null : p.throwStraightImage.rectTransform, p.throwCurveImage == null ? null : p.throwCurveImage.rectTransform });
            var scroll = (RectTransform)p.tabScrolls[KD_TreatPouch.TabToy].transform;
            Req(e, Mathf.Abs(scroll.anchoredPosition.y + scroll.sizeDelta.y / 2 - ContentTopWithRow) < .5f, "toy scroll top = 140 (below the top row)");
            var grid = p.tabScrolls[KD_TreatPouch.TabToy].content.GetComponent<GridLayoutGroup>();
            Req(e, grid != null && grid.cellSize.y * 3 + grid.spacing.y * 2 + grid.padding.vertical <= ContentTopWithRow - PageBottom, "six toy rows (2 x 3) fit the lowered scroll: " + (grid == null ? -1 : grid.cellSize.y * 3 + grid.spacing.y * 2 + grid.padding.vertical) + " <= " + (ContentTopWithRow - PageBottom));
            // 円盤 6 枚 → KH_ToyRecall (proxy、backing は入っていれば)
            var recall = p.recall;
            Req(e, recall != null && recall.discs != null && recall.discs.Length == 6, "recall.discs has 6 entries");
            if (recall != null && recall.discs != null)
            {
                var recallBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(recall);
                int n = 0;
                foreach (var disc in recall.discs)
                {
                    if (disc == null) continue; n++;
                    Req(e, disc.throwStyle == recall, disc.name + ".throwStyle = KH_ToyRecall (proxy)");
                    var b = UdonSharpEditorUtility.GetBackingUdonBehaviour(disc);
                    if (b != null && b.publicVariables.TryGetVariableValue("throwStyle", out object v)) Req(e, ReferenceEquals(v, recallBacking), disc.name + " backing throwStyle = KH_ToyRecall's backing");
                    else report.notes.Add(disc.name + ": backing heap has no throwStyle entry yet (written at save / build; proxy checked)");
                }
                report.notes.Add("discs checked: " + n);
            }
            // DES-UI-002 §4: 試作の切替台は本番に無い / 取り方の巡回は 0 → 3 → 4 → 0 (5 を通らない)
            int launchers = UnityEngine.Object.FindObjectsOfType<KH_FrisbeeLauncher>(true).Length;
            Req(e, launchers == 0, "no KH_FrisbeeLauncher (prototype variant cube) in the production scene (actual " + launchers + ")");
            int a = KH_FrisbeeCatch.NextMenuVariant(KH_FrisbeeCatch.VariantNormal), b2 = KH_FrisbeeCatch.NextMenuVariant(a), c2 = KH_FrisbeeCatch.NextMenuVariant(b2);
            Req(e, a == KH_FrisbeeCatch.VariantRunInBeside && b2 == KH_FrisbeeCatch.VariantTwist && c2 == KH_FrisbeeCatch.VariantNormal
                && KH_FrisbeeCatch.NextMenuVariant(KH_FrisbeeCatch.VariantCurve) == KH_FrisbeeCatch.VariantNormal && KH_FrisbeeCatch.NextMenuVariant(KH_FrisbeeCatch.VariantRunInSide) == KH_FrisbeeCatch.VariantRunInBeside,
                "menu catch-style cycle 0 -> 3 -> 4 -> 0 (" + a + "," + b2 + "," + c2 + "), 5 and the retired 1 / 2 lead back into it");
            foreach (var c in UnityEngine.Object.FindObjectsOfType<KH_FrisbeeCatch>(true))
                Req(e, c.activities != null, c.name + ".activities wired (the receiver's trainer check and the world's no-5 rule need it)");
            // 文言・字形・収まり
            ValidateCourseText(p, e, report, ThrowKeys);
            var sample = SampleText();
            try
            {
                foreach (string code in p.language.languageCodes)
                {
                    bool own = code == "ja" || code == "en";
                    Fit(sample, p.throwStyleLabel, p.language._TextForLanguage("course.toy.throw.label", code), code + " throw label", own, e, report);
                    Fit(sample, p.throwStraightLabel, p.language._TextForLanguage("course.toy.throw.straight", code), code + " straight", own, e, report);
                    Fit(sample, p.throwCurveLabel, p.language._TextForLanguage("course.toy.throw.curve", code), code + " curve", own, e, report);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(sample.gameObject); }
            CheckUnderscore(e, "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatPouch.ToyTop.cs");
        }

        // ============================================================
        // 共通の部品
        // ============================================================

        static float TabBottom(KD_TreatPouch p)
        {
            var tab = (RectTransform)p.tabImages[0].transform;
            return tab.anchoredPosition.y - tab.sizeDelta.y / 2;
        }

        static void Overlaps(List<string> e, RectTransform[] rects)
        {
            for (int i = 0; i < rects.Length; i++)
                for (int j = i + 1; j < rects.Length; j++)
                {
                    var a = rects[i]; var b = rects[j];
                    if (a == null || b == null) continue;
                    bool overlapX = Mathf.Abs(a.anchoredPosition.x - b.anchoredPosition.x) < (a.sizeDelta.x + b.sizeDelta.x) / 2 - .5f;
                    bool overlapY = Mathf.Abs(a.anchoredPosition.y - b.anchoredPosition.y) < (a.sizeDelta.y + b.sizeDelta.y) / 2 - .5f;
                    Req(e, !(overlapX && overlapY), a.name + " and " + b.name + " do not overlap");
                }
        }

        static void ValidateCourseText(KD_TreatPouch p, List<string> e, Report report, string[] keys)
        {
            var catalog = JsonUtility.FromJson<KD_LocalizationEditor.Catalog>(File.ReadAllText(KH_CourseMenuEditor.CatalogPath));
            KD_LocalizationEditor.Validate(catalog);
            foreach (var key in keys)
                foreach (var lang in catalog.languages)
                {
                    var entry = lang.texts.FirstOrDefault(x => x.key == key);
                    if (entry == null || string.IsNullOrEmpty(entry.text)) { e.Add("FAIL course text missing " + lang.code + ":" + key); continue; }
                    if (p.language._TextForLanguage(key, lang.code) != entry.text) { e.Add("FAIL language extension differs " + lang.code + ":" + key + " (rerun the Build to apply the course catalog)"); break; }
                }
            try { KD_LocalizationEditor.ValidateGlyphs(catalog, p.sectionTitleLabel.font); }
            catch (Exception ex) { e.Add("Glyphs: " + ex.Message + " (run Course/27 - Add Course UI Glyphs)"); }
        }

        static TextMeshProUGUI SampleText()
        {
            var go = new GameObject("ToyRowMeasure", typeof(RectTransform)); go.hideFlags = HideFlags.HideAndDontSave;
            return go.AddComponent<TextMeshProUGUI>();
        }

        /// <summary>自前で焼く言語 (ja / en) は溢れを FAIL、端末のフォントで描く言語は記録だけ (字形を持たないので幅が測れない)。</summary>
        static void Fit(TextMeshProUGUI sample, TMP_Text target, string text, string name, bool fail, List<string> e, Report report)
        {
            if (target == null) { e.Add("FAIL label missing: " + name); return; }
            int result = KD_LocalizationEditor.MeasureLabelFit(sample, target, text, out Vector2 preferred);
            report.labelFit.Add(name + " preferred=" + preferred + " rect=" + target.rectTransform.rect.size + " fit=" + result + (fail ? "" : " (info: client font)"));
            if (result == 2 && fail) e.Add("Label overflow: " + name + " '" + text + "'");
        }

        static void CheckEvent(List<string> e, GameObject go, VRC.Udon.UdonBehaviour target, string evt)
        {
            var b = go.GetComponent<Button>();
            if (b == null || b.onClick.GetPersistentEventCount() != 1 || b.onClick.GetPersistentTarget(0) != target || b.onClick.GetPersistentMethodName(0) != "SendCustomEvent") { e.Add("FAIL button target " + go.name); return; }
            var call = new SerializedObject(b).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (call.arraySize != 1 || call.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue != evt) e.Add("FAIL button event " + go.name + " != " + evt);
        }

        static void CheckUnderscore(List<string> e, string path)
        {
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                var mm = Regex.Match(lines[i], @"public\s+(?!override\b)\w+(?:\[\])?\s+([A-Za-z]\w*)\s*\(");
                if (!mm.Success || mm.Groups[1].Value.StartsWith("_")) continue;
                bool callable = i > 0 && lines[i - 1].Contains("[NetworkCallable");
                if (!callable) e.Add("Public method without '_' and without [NetworkCallable]: " + path + ":" + (i + 1) + " " + mm.Groups[1].Value);
            }
        }

        static void Req(List<string> e, bool ok, string what) { if (!ok) e.Add("FAIL " + what); }

        static TextMeshProUGUI MakeText(Transform parent, string name, Vector2 pos, Vector2 size, float points, TextAlignmentOptions align, TMP_FontAsset font, string text)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var t = go.GetComponent<TextMeshProUGUI>(); if (t == null) t = go.AddComponent<TextMeshProUGUI>();
            t.font = font; t.fontSize = points; t.alignment = align; t.enableWordWrapping = false; t.raycastTarget = false;
            t.overflowMode = TextOverflowModes.Overflow; t.color = Color.white; t.text = text; EditorUtility.SetDirty(t);
            return t;
        }

        static TextMeshProUGUI ButtonLabel(GameObject button, float points)
        {
            var t = button.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            t.fontSize = points; t.enableWordWrapping = false; t.enableAutoSizing = true; t.fontSizeMax = points; t.fontSizeMin = 14f; EditorUtility.SetDirty(t);
            return t;
        }

        /// <summary>選んでいる方を示す下線 (ボタンの子、押せない面。AccentGold)。冪等。</summary>
        static GameObject Underline(GameObject button)
        {
            var size = ((RectTransform)button.transform).sizeDelta;
            var go = KD_ShopSetupEditor.EnsureUiChild(button.transform, UnderlineName, new Vector2(0f, -size.y / 2 + 4f), new Vector2(size.x - 28f, 4f));
            var img = go.GetComponent<Image>(); if (img == null) img = go.AddComponent<Image>();
            img.sprite = null; img.color = KD_UiSpriteEditor.AccentGold; img.raycastTarget = false; EditorUtility.SetDirty(img);
            go.transform.SetAsLastSibling();
            return go;
        }

        static string Ja(string key)
        {
            var c = JsonUtility.FromJson<KD_LocalizationEditor.Catalog>(File.ReadAllText(KH_CourseMenuEditor.CatalogPath));
            return KD_LocalizationEditor.Text(c, "ja", key);
        }
    }
}
