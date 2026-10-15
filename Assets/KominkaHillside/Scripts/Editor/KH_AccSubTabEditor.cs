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

namespace KominkaHillside.Editor
{
    /// <summary>DES-UI-006 かざりのサブタブ (2026-10-15 ユーザー決定「すべてはいで OK」、docs/plans/2026-10-15-menu-subtabs-and-people.md §3.2・§3.3・§8)。
    ///
    /// Acc/1 は**部品を足して配線するだけ**で、行を作り直さない・動かさない (旧メニューの再生成 Menu/7・12・13・17 は本番シーンで禁止)。
    /// - Page_Acc の上段 (y 164、高さ 48) に親 AccSubTabs (1100×48、絵なし) を作り、その子に
    ///   サブタブ 3 つ AccGroup0..2 (各 276×48。_AccGroup0..2) と、ページ送り [◀] AccPagePrev (64×48、_AccPagePrev) /
    ///   「p / q」AccPageLabel (80×48、数字の連結) / [▶] AccPageNext (64×48、_AccPageNext) を置く。
    /// - 行 (AccRow0..15 / FxHead0..1 / FxRow0..10 / CollarHead0..2 / CollarRow0..4) は Content の子のまま (文言の配線 22 本が経路で引く)。
    ///   ScrollRect・GridLayoutGroup・ContentSizeFitter はそのまま (出すセルが常に 6 以下なので Content ≤ Viewport で動かない)。
    /// - 見出し Page_Acc/Header と [もどる] Page_Acc/BackButton は触らない (A 面で見出しを隠すのは実行時の KD_TreatPouch.AccTopRow)。
    /// - 保存時の状態: 親は非表示 (実行時に A 面でだけ出す)、サブタブ 3 つは表示、ページ送り 3 つは非表示 (群の件数が 6 を超えるときだけ実行時に出す)。
    /// - 配線は同期しない参照だけ (accSubTabRoot / accGroupImages / accGroupLabels / accPagePrev* / accPageNext* / accPageLabel)。同期変数は増えない。
    /// 何度流しても同じ結果になる (位置・寸法・押し先はすべて絶対値で書き直す)。保存前に <see cref="Validate"/> を通し、通らなければ保存しない。
    /// Acc/2 は保存済みのシーンを読み直して同じ検査 + 13 言語の収まりを測り、Temp/des-ui-006/acc-subtabs-verify.json に出す (PASS は Console に ACC_SUBTABS_OK)。
    /// 実行時の絞り込み (どの行が出るか) は Edit では実行しない — 件数から「1 ページ 6 以下・ページ数」を導いて検査する。</summary>
    public static class KH_AccSubTabEditor
    {
        const string OutDir = "Temp/des-ui-006";
        public const string RootName = "AccSubTabs", PrevName = "AccPagePrev", NextName = "AccPageNext", PageLabelName = "AccPageLabel";
        public static readonly string[] GroupNames = { "AccGroup0", "AccGroup1", "AccGroup2" };
        public static readonly string[] GroupEvents = { "_AccGroup0", "_AccGroup1", "_AccGroup2" };
        public const string PrevEvent = "_AccPagePrev", NextEvent = "_AccPageNext";
        /// <summary>サブタブの文言 (既存キー。新規キー 0)。</summary>
        public static readonly string[] GroupKeys = { "KD_TreatPouch.WordAccTitle", "collar.group", "KD_TreatPouch.WordFxGroup" };

        // ---- 配置 (Page_Acc の座標。板の中心が原点。板 1140×700 は変えない) ----
        public const float TopRowY = KH_ToyTopRowEditor.TopRowY, RowHeight = KH_ToyTopRowEditor.RowHeight;   // 164 / 48 (帯 140..188 = スクロールの上端 140 に接して重ならない)
        public const float ContentTop = KH_ToyTopRowEditor.ContentTopWithRow;                                   // 140
        public const float RowLeft = -550f, RowRight = 550f;
        public static readonly Vector2 RootPos = new Vector2(0f, TopRowY), RootSize = new Vector2(1100f, RowHeight);
        public static readonly float[] GroupX = { -412f, -128f, 156f };   // 276 幅、間隔 8 (x −550..294)
        public static readonly Vector2 GroupSize = new Vector2(276f, RowHeight);
        public static readonly Vector2 PrevPos = new Vector2(342f, TopRowY), NextPos = new Vector2(498f, TopRowY), ArrowSize = new Vector2(64f, RowHeight);
        public static readonly Vector2 PageLabelPos = new Vector2(420f, TopRowY), PageLabelSize = new Vector2(80f, RowHeight);
        // 文字の大きさ (上限 / オートサイズの下限)。下限に張り付いたら「入っていない」(vrc-world-ui §10。さんぽの 2 列と同じ下限 16)
        public const float GroupPt = 22f, GroupMin = 16f, ArrowPt = 20f, ArrowMin = 14f, PagePt = 22f;
        const string Glyphs = "◀▶0123456789/ ";

        static Vector2 GroupPos(int g) { return new Vector2(GroupX[g], TopRowY); }

        /// <summary>サブタブが入っているか (Acc/1 を流した後か)。Menu/21・Menu/6f が分岐に使う。</summary>
        public static bool IsApplied(KD_TreatPouch p)
        {
            if (p == null || p.accSubTabRoot == null || p.tabPages == null || p.tabPages.Length <= KD_TreatPouch.TabAcc || p.tabPages[KD_TreatPouch.TabAcc] == null) return false;
            return p.accSubTabRoot.transform.parent == p.tabPages[KD_TreatPouch.TabAcc].transform;
        }

        [Serializable] class Report
        {
            public string utc, scene, mode, status = "RUNNING";
            public int errors;
            public List<string> findings = new List<string>();
            public List<string> targets = new List<string>();
            public List<string> fit = new List<string>();
            public List<string> info = new List<string>();
            public string limitations = "Editor structure, saved Udon references (proxy + backing heap) and TMP size measurement only. Which rows are shown per sub tab / page is NOT executed in Edit mode "
                + "(it is derived from the group sizes: <= 6 cells per page by construction). PCVR laser / Desktop mouse / Touch input, readability, colors at runtime and the harness (Course/129) are not tested (Play).";
        }

        // ============================================================
        // Acc/1 生成 (部品を足して配線)
        // ============================================================

        [MenuItem("Tools/KominkaHillside/Acc/1 - Acc Sub Tabs")]
        public static void Apply()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var p = KH_CourseTravelEditor.FindPouch();
            var report = new Report { utc = DateTime.UtcNow.ToString("o"), scene = p.gameObject.scene.path, mode = "apply" };
            // ---- 前提 (何も変えずに止まる) ----
            if (new SerializedObject(p).FindProperty("accSubTabRoot") == null) throw new InvalidOperationException("KD_TreatPouch has no accSubTabRoot field yet: compile U# first; nothing changed");
            var page = p.tabPages != null && p.tabPages.Length > KD_TreatPouch.TabAcc && p.tabPages[KD_TreatPouch.TabAcc] != null ? p.tabPages[KD_TreatPouch.TabAcc].transform : null;
            var scroll = p.tabScrolls != null && p.tabScrolls.Length > KD_TreatPouch.TabAcc ? p.tabScrolls[KD_TreatPouch.TabAcc] : null;
            if (page == null || scroll == null || scroll.content == null || scroll.viewport == null || scroll.transform.parent != page)
                throw new InvalidOperationException("Page_Acc and its Scroll/Viewport/Content required; nothing changed");
            var rowsBefore = ContentRows(scroll);
            if (!rowsBefore.SequenceEqual(ExpectedRows())) throw new InvalidOperationException("Page_Acc Content rows differ from KD_HandMenuScrollEditor.RowsFor(TabAcc) (run Collar/1 or fix first); nothing changed");
            if (p.accHeaderLabel == null || p.accHeaderLabel.transform.parent != page || p.accBackButton == null || p.accBackButton.transform.parent != page)
                throw new InvalidOperationException("Page_Acc/Header and BackButton (accHeaderLabel / accBackButton) required; nothing changed");
            if (p.collarPatterns == null || p.collarLabels == null || p.fxLabels == null || p.fxItems == null)
                throw new InvalidOperationException("Collar rows (Collar/1) and pet FX rows must be wired before the sub tabs; nothing changed");
            var font = p.sectionTitleLabel != null ? p.sectionTitleLabel.font : null;
            if (font == null) throw new InvalidOperationException("Shared UI font missing; nothing changed");
            if (p.language == null || p.language.languageCodes == null) throw new InvalidOperationException("pouch.language (KD_Language) missing; nothing changed");
            var pouchBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            if (pouchBacking == null) throw new InvalidOperationException("Menu backing behaviour missing; nothing changed");
            string missing = KD_ShopSetupEditor.MissingGlyphs(Glyphs);
            if (missing.Length > 0) throw new InvalidOperationException("Menu font lacks '" + missing + "' (◀ ▶ digits); nothing changed");

            Directory.CreateDirectory(OutDir);
            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-acc-subtabs.unity", true);
            var log = new StringBuilder();
            try
            {
                // ---- 親 (絵なし。上段の帯) ----
                var root = KD_ShopSetupEditor.EnsureUiChild(page, RootName, RootPos, RootSize);
                foreach (var g in root.GetComponents<Graphic>()) UnityEngine.Object.DestroyImmediate(g);   // 親は光線を取らない
                root.transform.SetAsLastSibling();

                // ---- サブタブ 3 つ ----
                var images = new Image[3]; var labels = new TextMeshProUGUI[3];
                for (int g = 0; g < 3; g++)
                {
                    var go = KD_ShopSetupEditor.EnsureButton(root.transform, GroupNames[g], Local(GroupPos(g)), GroupSize,
                        g == KD_TreatPouch.AccGroupDecor ? p.openOnColor : p.hillsideNeutralColor, font, p.language._TextForLanguage(GroupKeys[g], "ja"), pouchBacking, GroupEvents[g], log);
                    go.transform.SetSiblingIndex(g);
                    go.SetActive(true);
                    images[g] = go.GetComponent<Image>();
                    labels[g] = ButtonLabel(go, GroupPt, GroupMin);
                }

                // ---- ページ送り [◀] p / q [▶] (◀ ▶ はおもちゃタブのボールの柄と同じ字・同じフォント。翻訳しない) ----
                var prev = KD_ShopSetupEditor.EnsureButton(root.transform, PrevName, Local(PrevPos), ArrowSize, KD_UiSpriteEditor.ActionNeutral, font, "◀", pouchBacking, PrevEvent, log);
                var label = MakeText(root.transform, PageLabelName, Local(PageLabelPos), PageLabelSize, PagePt, font, "1 / 2");
                var next = KD_ShopSetupEditor.EnsureButton(root.transform, NextName, Local(NextPos), ArrowSize, KD_UiSpriteEditor.ActionNeutral, font, "▶", pouchBacking, NextEvent, log);
                ButtonLabel(prev, ArrowPt, ArrowMin); ButtonLabel(next, ArrowPt, ArrowMin);
                prev.transform.SetSiblingIndex(3); label.transform.SetSiblingIndex(4); next.transform.SetSiblingIndex(5);
                prev.SetActive(false); label.gameObject.SetActive(false); next.SetActive(false);   // 群の件数が 6 を超えるときだけ実行時に出す
                root.SetActive(false);   // 実行時 (A 面) にだけ出す。B / C / D の面と開く前は見出しのまま

                // ---- 配線 (同期しない参照だけ。同期変数は 0 本のまま) ----
                p.accSubTabRoot = root;
                p.accGroupImages = images; p.accGroupLabels = labels;
                p.accPagePrevButton = prev; p.accPagePrevImage = prev.GetComponent<Image>();
                p.accPageNextButton = next; p.accPageNextImage = next.GetComponent<Image>();
                p.accPageLabel = label;
                UdonSharpEditorUtility.CopyProxyToUdon(p); EditorUtility.SetDirty(p);
                EditorSceneManager.MarkSceneDirty(p.gameObject.scene);

                // ---- 保存前の検査 (部品・配線・押せる面・行の並び・13 言語の収まり。通らなければ保存しない) ----
                var errors = new List<string>();
                if (!ContentRows(scroll).SequenceEqual(rowsBefore)) errors.Add("FAIL Content rows changed during Acc/1 (must stay as they were)");
                Validate(p, errors, report.targets, report.fit, report.info);
                // backing の変数表は保存時に揃うことがある (vrc-udonsharp §4)。保存前は記録だけにして、Acc/2 (保存・再読込後) で判定する
                foreach (var w in errors.Where(x => x.StartsWith("FAIL backing")).ToList()) { report.info.Add("pre-save " + w); errors.Remove(w); }
                if (log.Length > 0) report.info.Add(log.ToString().Trim());
                if (errors.Count > 0)
                {
                    report.findings = errors; report.errors = errors.Count; report.status = "NOT_SAVED";
                    File.WriteAllText(OutDir + "/acc-subtabs-apply.json", JsonUtility.ToJson(report, true));
                    throw new InvalidOperationException("[KH Acc Sub Tabs] Check failed (" + errors.Count + "); scene NOT saved. Reopen the scene without saving to discard. First: " + errors[0]);
                }
                if (!EditorSceneManager.SaveScene(p.gameObject.scene)) throw new IOException("Acc sub tabs save failed");
            }
            catch
            {
                Debug.LogError("[KH Acc Sub Tabs] Apply failed; the scene before this run is at " + OutDir + "/before-acc-subtabs.unity. Reopen the scene without saving to discard partial changes.");
                throw;
            }
            report.status = "SAVED";
            File.WriteAllText(OutDir + "/acc-subtabs-apply.json", JsonUtility.ToJson(report, true));
            Debug.Log("[KH Acc Sub Tabs] Sub tabs and page arrows added to Page_Acc and saved. Run Acc/2 (verify after reload), then Menu/20, 21, 22, 6f, 6h, 25c.");
        }

        // ============================================================
        // Acc/2 検査 (保存済みのシーンを読み直す)
        // ============================================================

        [MenuItem("Tools/KominkaHillside/Acc/2 - Verify Acc Sub Tabs")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);   // 未保存のシーンは拒む (読み直しで変更を捨てない)
            EditorSceneManager.OpenScene(KH_CourseGroundEditor.ScenePath, OpenSceneMode.Single);
            var p = KH_CourseTravelEditor.FindPouch();
            var report = new Report { utc = DateTime.UtcNow.ToString("o"), scene = p.gameObject.scene.path, mode = "verify" };
            try { Validate(p, report.findings, report.targets, report.fit, report.info); }
            catch (Exception ex) { report.findings.Add("Exception: " + ex.Message); }
            report.errors = report.findings.Count; report.status = report.errors == 0 ? "ACC_SUBTABS_OK" : "FAIL";
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/acc-subtabs-verify.json", JsonUtility.ToJson(report, true));
            if (report.errors != 0) throw new InvalidOperationException("[KH Acc Sub Tabs] FAIL " + report.errors + ":\n" + string.Join("\n", report.findings));
            Debug.Log("[KH Acc Sub Tabs] ACC_SUBTABS_OK targets=" + report.targets.Count + " fit=" + report.fit.Count + " info=" + report.info.Count + " (Temp/des-ui-006/acc-subtabs-verify.json)");
        }

        /// <summary>部品・位置・寸法・押し先・配線 (proxy と backing)・行の並び・群の件数とページ数・押せる面・(fit が非 null なら) 13 言語の収まり。
        /// Acc/1 の保存前、Acc/2、Menu/21 が使う。</summary>
        public static void Validate(KD_TreatPouch p, List<string> e, List<string> targets, List<string> fit, List<string> info)
        {
            if (!IsApplied(p)) { e.Add("FAIL acc sub tabs not applied (run Acc/1)"); return; }
            var page = p.tabPages[KD_TreatPouch.TabAcc].transform;
            var scroll = p.tabScrolls != null && p.tabScrolls.Length > KD_TreatPouch.TabAcc ? p.tabScrolls[KD_TreatPouch.TabAcc] : null;
            var pb = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            var root = p.accSubTabRoot.transform;

            // ---- 行は Content の子のまま・並び不変・スクロールは残す ----
            Req(e, scroll != null && scroll.transform.parent == page && scroll.content != null && scroll.enabled && scroll.vertical, "accessory ScrollRect kept (enabled, vertical) under Page_Acc");
            if (scroll == null || scroll.content == null) return;
            var got = ContentRows(scroll);
            Req(e, got.SequenceEqual(ExpectedRows()), "Content rows unchanged and in order (RowsFor(TabAcc): AccRow16, FxHead2, FxRow11, CollarHead3, CollarRow5; actual " + got.Count + ")");
            Req(e, p.accRows != null && p.accRows.All(r => r != null && r.transform.parent == scroll.content), "AccRow0..15 stay in Scroll/Viewport/Content (localization bindings resolve by path)");
            Req(e, p.fxLabels != null && p.fxLabels.All(l => l != null && l.transform.parent != null && l.transform.parent.parent == scroll.content), "FxRow0..10 stay in Content");
            Req(e, p.collarLabels != null && p.collarLabels.All(l => l != null && l.transform.parent != null && l.transform.parent.parent == scroll.content), "CollarRow0..4 stay in Content");
            var grid = scroll.content.GetComponent<GridLayoutGroup>(); var fitter = scroll.content.GetComponent<ContentSizeFitter>();
            Req(e, grid != null && grid.enabled && grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount && grid.constraintCount == 2 && fitter != null && fitter.enabled,
                "GridLayoutGroup (2 columns) and ContentSizeFitter kept enabled");
            if (grid != null)
                Req(e, grid.cellSize.y * 3 + grid.spacing.y * 2 + grid.padding.vertical <= scroll.viewport.rect.height + 0.5f,
                    "one page (" + KD_TreatPouch.AccPageCells + " cells = 3 rows: " + (grid.cellSize.y * 3 + grid.spacing.y * 2 + grid.padding.vertical) + ") fits the viewport " + scroll.viewport.rect.height + " (no scrolling)");
            Req(e, KD_TreatPouch.AccPageCells == 6 && KD_TreatPouch.AccGroupCount == 3, "AccPageCells 6 / AccGroupCount 3");

            // ---- 見出しと [もどる] は触らない (A 面で見出しを隠すのは実行時) ----
            Req(e, p.accHeaderLabel != null && p.accHeaderLabel.transform.parent == page && p.accHeaderLabel.name == "Header" && p.accHeaderLabel.gameObject.activeSelf,
                "Page_Acc/Header kept, saved active (hidden at runtime on the catalog face only)");
            Req(e, p.accBackButton != null && p.accBackButton.transform.parent == page && p.accBackButton.name == "BackButton", "Page_Acc/BackButton kept");

            // ---- 親 ----
            Req(e, root.name == RootName && root.parent == page, "accSubTabRoot = Page_Acc/" + RootName);
            Req(e, Near(PageRect(page, (RectTransform)root), Center(RootPos, RootSize)), "root " + RootSize.x + "x" + RootSize.y + " at " + RootPos);
            Req(e, root.GetComponent<Graphic>() == null, "root has no graphic (catches no rays)");
            Req(e, !root.gameObject.activeSelf, "root saved hidden (shown at runtime on the catalog face only)");
            Req(e, root.childCount == 6, "root has 6 children (3 tabs + prev / page label / next), actual " + root.childCount);
            foreach (var c in page.GetComponentsInChildren<Canvas>(true)) e.Add("FAIL nested Canvas under Page_Acc changes the draw order: " + c.name);

            // ---- サブタブ 3 つ ----
            var parts = new List<(string name, RectTransform rt, bool button)>();
            for (int g = 0; g < 3; g++)
            {
                var t = root.Find(GroupNames[g]);
                if (t == null) { e.Add("FAIL " + RootName + "/" + GroupNames[g] + " missing"); continue; }
                Req(e, t.GetSiblingIndex() == g, GroupNames[g] + " sibling " + g);
                Req(e, Near(PageRect(page, (RectTransform)t), Center(GroupPos(g), GroupSize)), GroupNames[g] + " " + GroupSize.x + "x" + GroupSize.y + " at " + GroupPos(g));
                Req(e, t.gameObject.activeSelf, GroupNames[g] + " saved active");
                CheckEvent(e, t.gameObject, pb, GroupEvents[g]);
                var lbl = t.Find("Label") != null ? t.Find("Label").GetComponent<TextMeshProUGUI>() : null;
                Req(e, p.accGroupImages != null && p.accGroupImages.Length == 3 && p.accGroupImages[g] == t.GetComponent<Image>(), "accGroupImages[" + g + "] wired");
                Req(e, p.accGroupLabels != null && p.accGroupLabels.Length == 3 && lbl != null && p.accGroupLabels[g] == lbl, "accGroupLabels[" + g + "] wired");
                Req(e, lbl != null && !lbl.enableWordWrapping && lbl.enableAutoSizing && Mathf.Approximately(lbl.fontSizeMax, GroupPt) && Mathf.Approximately(lbl.fontSizeMin, GroupMin) && !lbl.raycastTarget,
                    GroupNames[g] + " label: 1 line, auto size " + GroupPt + " .. " + GroupMin + ", no rays");
                parts.Add((GroupNames[g], (RectTransform)t, true));
            }

            // ---- ページ送り ----
            var prev = root.Find(PrevName); var next = root.Find(NextName); var pageLabel = root.Find(PageLabelName);
            Req(e, prev != null && next != null && pageLabel != null, RootName + "/" + PrevName + ", " + PageLabelName + ", " + NextName + " exist");
            if (prev != null)
            {
                Req(e, Near(PageRect(page, (RectTransform)prev), Center(PrevPos, ArrowSize)), PrevName + " " + ArrowSize.x + "x" + ArrowSize.y + " at " + PrevPos);
                Req(e, !prev.gameObject.activeSelf && p.accPagePrevButton == prev.gameObject && p.accPagePrevImage == prev.GetComponent<Image>(), PrevName + " wired (accPagePrevButton / Image), saved hidden");
                Req(e, prev.Find("Label") != null && prev.Find("Label").GetComponent<TMP_Text>().text == "◀", PrevName + " shows ◀");
                CheckEvent(e, prev.gameObject, pb, PrevEvent);
                parts.Add((PrevName, (RectTransform)prev, true));
            }
            if (next != null)
            {
                Req(e, Near(PageRect(page, (RectTransform)next), Center(NextPos, ArrowSize)), NextName + " " + ArrowSize.x + "x" + ArrowSize.y + " at " + NextPos);
                Req(e, !next.gameObject.activeSelf && p.accPageNextButton == next.gameObject && p.accPageNextImage == next.GetComponent<Image>(), NextName + " wired (accPageNextButton / Image), saved hidden");
                Req(e, next.Find("Label") != null && next.Find("Label").GetComponent<TMP_Text>().text == "▶", NextName + " shows ▶");
                CheckEvent(e, next.gameObject, pb, NextEvent);
                parts.Add((NextName, (RectTransform)next, true));
            }
            if (pageLabel != null)
            {
                var tmp = pageLabel.GetComponent<TextMeshProUGUI>();
                Req(e, Near(PageRect(page, (RectTransform)pageLabel), Center(PageLabelPos, PageLabelSize)), PageLabelName + " " + PageLabelSize.x + "x" + PageLabelSize.y + " at " + PageLabelPos);
                Req(e, tmp != null && p.accPageLabel == tmp && !pageLabel.gameObject.activeSelf && !tmp.raycastTarget, PageLabelName + " wired (accPageLabel), saved hidden, no rays");
                parts.Add((PageLabelName, (RectTransform)pageLabel, false));
            }
            foreach (var m in GroupEvents.Concat(new[] { PrevEvent, NextEvent }))
                Req(e, typeof(KD_TreatPouch).GetMethod(m) != null, "KD_TreatPouch." + m + " exists");

            // ---- 上段の帯の中・互いに重ならない・スクロールと重ならない (見出しと [もどる] は同時に出ないので比べない) ----
            float tabBottom = TabBottom(p);
            var scrollRect = PageRect(page, (RectTransform)scroll.transform);
            var rects = parts.Select(x => PageRect(page, x.rt)).ToList();
            for (int a = 0; a < parts.Count; a++)
            {
                var r = rects[a];
                targets?.Add(parts[a].name + " " + r.width.ToString("0") + "x" + r.height.ToString("0") + " at (" + r.center.x.ToString("0") + ", " + r.center.y.ToString("0") + ")");
                Req(e, r.xMin >= RowLeft - 0.5f && r.xMax <= RowRight + 0.5f && r.yMin >= ContentTop - 0.5f && r.yMax <= tabBottom - 4f + 0.5f,
                    parts[a].name + " inside the top band x " + RowLeft + ".." + RowRight + ", y " + ContentTop + ".." + (tabBottom - 4f) + " (actual " + r.xMin + ".." + r.xMax + ", " + r.yMin + ".." + r.yMax + ")");
                Req(e, !r.Overlaps(Shrink(scrollRect, 0.5f)), parts[a].name + " does not overlap the accessory scroll (top " + scrollRect.yMax + ")");
                if (parts[a].button)
                {
                    Req(e, r.height >= 48f - 0.5f && r.width >= 48f - 0.5f, "touch target " + parts[a].name + " >= 48 (" + r.width + "x" + r.height + ")");
                    var img = parts[a].rt.GetComponent<Image>(); var btn = parts[a].rt.GetComponent<Button>();
                    Req(e, img != null && img.raycastTarget && btn != null && btn.targetGraphic == img, "touch target " + parts[a].name + " is its own ray target");
                    foreach (var gr in parts[a].rt.GetComponentsInChildren<Graphic>(true)) if (gr.transform != parts[a].rt && gr.raycastTarget) e.Add("FAIL decoration catches rays: " + parts[a].name + "/" + gr.name);
                }
                for (int b = a + 1; b < parts.Count; b++)
                    if (rects[a].Overlaps(Shrink(rects[b], 0.5f))) e.Add("FAIL top-row parts overlap: " + parts[a].name + " / " + parts[b].name);
            }
            // 手前 (描画順で後ろ) に光線対象があって中心を覆わない。見出し・[もどる] は A 面では出ないので除く
            {
                var exclusive = new List<Transform> { p.accHeaderLabel != null ? p.accHeaderLabel.transform : null, p.accBackButton != null ? p.accBackButton.transform : null };
                var order = page.GetComponentsInChildren<Graphic>(true).ToList();
                var mine = new HashSet<Transform>(parts.Select(x => x.rt.transform));
                for (int a = 0; a < parts.Count; a++)
                {
                    if (!parts[a].button) continue;
                    var own = parts[a].rt.GetComponent<Graphic>(); if (own == null) continue;
                    int at = order.IndexOf(own); Vector2 c = rects[a].center;
                    foreach (var g in order)
                    {
                        if (g == own || !g.raycastTarget || order.IndexOf(g) < at || g.transform.IsChildOf(parts[a].rt) || mine.Contains(g.transform)) continue;
                        if (exclusive.Any(x => x != null && g.transform.IsChildOf(x))) continue;
                        if (!ActiveUnder(g.transform, page) && !g.transform.IsChildOf(root)) continue;
                        if (PageRect(page, g.rectTransform).Contains(c)) e.Add("FAIL " + g.name + " is drawn above " + parts[a].name + " and catches its rays");
                    }
                }
            }

            // ---- 群の件数とページ数 (A 面で出すセルは 1 ページ 6 以下。描画は実行時なので件数から導く) ----
            {
                int decor = p.accProductIds == null ? 0 : p.accProductIds.Count(id => !KD_AccessorySetsEditor.HillsideSetMembers.Contains(id));
                if (p.accRows != null && decor > p.accRows.Length) decor = p.accRows.Length;
                int collar = 0;
                for (int i = 0; p.collarLabels != null && i < p.collarLabels.Length; i++) if (p.collarLabels[i] != null && p.collarPatterns != null && p.collarPatterns.ItemAt(i) != null) collar++;
                int fx = 0;
                for (int i = 0; p.fxLabels != null && i < p.fxLabels.Length; i++) if (p.fxLabels[i] != null && p.fxItems != null && i < p.fxItems.Length && p.fxItems[i] != null) fx++;
                int[] counts = { decor, collar, fx };
                for (int g = 0; g < 3; g++)
                {
                    int pages = counts[g] <= 0 ? 1 : (counts[g] + KD_TreatPouch.AccPageCells - 1) / KD_TreatPouch.AccPageCells;
                    int last = counts[g] - (pages - 1) * KD_TreatPouch.AccPageCells;
                    info?.Add("group " + g + " (" + GroupKeys[g] + "): " + counts[g] + " rows -> " + pages + " page(s), cells per page " + Math.Min(counts[g], KD_TreatPouch.AccPageCells) + (pages > 1 ? " / last " + last : "") + (pages > 1 ? " (page arrows shown)" : " (page arrows hidden)"));
                    Req(e, counts[g] >= 1, "group " + g + " (" + GroupKeys[g] + ") has rows (" + counts[g] + ")");
                }
                // 設計 (§3.2) の件数と違っても UI はページ送りで成り立つので記録だけ (商品の増減で検査を落とさない)
                info?.Add((decor == 10 && collar == 5 && fx == 6 ? "group sizes match" : "NOTE group sizes differ from") + " the design §3.2 (accessories 10 -> 2 pages, collars 5 / pet FX 6 -> 1 page): actual " + decor + " / " + collar + " / " + fx);
            }
            // 実行時の絞り込みの口がある (実行はしない)
            foreach (var m in new[] { "AccCellOnPage", "AccGroupItems", "AccTopRow", "AccClampPage", "AccShowCell" })
                Req(e, typeof(KD_TreatPouch).GetMethod(m, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null, "runtime filter KD_TreatPouch." + m + " exists");

            // ---- 字形 (◀ ▶ と数字。おもちゃタブのボールの柄と同じ判定) ----
            string missing = KD_ShopSetupEditor.MissingGlyphs(Glyphs);
            Req(e, missing.Length == 0, "◀ ▶ / digits in the menu font (missing '" + missing + "')");

            // ---- 実体 (backing) にも入っていること ----
            foreach (var field in new[] { "accSubTabRoot", "accPagePrevButton", "accPagePrevImage", "accPageNextButton", "accPageNextImage", "accPageLabel" })
                if (pb == null || !pb.publicVariables.TryGetVariableValue(field, out object v) || v == null) e.Add("FAIL backing " + field + " (rerun Acc/1)");
            foreach (var field in new[] { "accGroupImages", "accGroupLabels" })
                if (pb == null || !pb.publicVariables.TryGetVariableValue(field, out object arr) || !(arr is Array a) || a.Length != 3 || a.Cast<object>().Any(x => x == null)) e.Add("FAIL backing " + field + " length 3, no null (rerun Acc/1)");

            CheckUnderscore(e, "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatPouch.Acc.cs");

            // ---- 13 言語の収まり ----
            if (fit != null) Measure(p, e, fit, info);
        }

        // ============================================================
        // 13 言語の収まり (KH_WalkTabLayoutEditor.Measure と同じ測り方: 上限 / 設計の下限で測る。
        // 自前で焼く ja / en は溢れを FAIL。ほかの言語は、主フォントに字形が全部ある文言だけ FAIL (幅が測れる)、無い文言は端末のフォントで描くので記録だけ)
        // ============================================================

        static void Measure(KD_TreatPouch p, List<string> e, List<string> fit, List<string> info)
        {
            var lang = p.language;
            if (lang == null || lang.languageCodes == null) { e.Add("FAIL KD_Language missing (cannot measure labels)"); return; }
            var go = new GameObject("AccSubTabMeasure", typeof(RectTransform)); go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var sample = go.AddComponent<TextMeshProUGUI>();
                foreach (string code in lang.languageCodes)
                {
                    bool own = code == "ja" || code == "en";
                    for (int g = 0; g < 3; g++)
                    {
                        var target = p.accGroupLabels != null && g < p.accGroupLabels.Length ? p.accGroupLabels[g] : null;
                        if (target == null) { e.Add("FAIL sub tab label " + g + " missing (cannot measure)"); continue; }
                        string text = lang._TextForLanguage(GroupKeys[g], code);
                        if (string.IsNullOrEmpty(text)) { e.Add("FAIL text missing " + code + ":" + GroupKeys[g]); continue; }
                        string lacks = KD_ShopSetupEditor.MissingGlyphs(text);
                        int r = FitAt(sample, target, text, GroupPt, GroupMin, Size(target), out Vector2 pref);
                        fit.Add(code + " sub tab " + g + " \"" + text + "\" rect=" + Size(target) + " max=" + GroupPt + " min=" + GroupMin + " preferred=" + pref + " fit=" + r + (lacks.Length > 0 ? " (glyphs not in the menu font: client font)" : ""));
                        if (own && lacks.Length > 0) e.Add("FAIL glyphs missing in the menu font: " + code + " " + GroupKeys[g] + " '" + lacks + "' (run Course/27)");
                        if (r == 2 && (own || lacks.Length == 0)) e.Add("FAIL sub tab label does not fit at " + GroupMin + "pt: " + code + " \"" + text + "\"");
                        else if (r == 2) info?.Add("not measurable (client font) " + code + " sub tab " + g + " \"" + text + "\"");
                        else if (r == 1) info?.Add("shrinks " + code + " sub tab " + g);
                    }
                }
                if (p.accPageLabel != null)
                {
                    int r = FitAt(sample, p.accPageLabel, "2 / 2", PagePt, PagePt, Size(p.accPageLabel), out Vector2 pref);
                    fit.Add("page label \"2 / 2\" rect=" + Size(p.accPageLabel) + " preferred=" + pref + " fit=" + r);
                    if (r == 2) e.Add("FAIL page label \"2 / 2\" does not fit");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        /// <summary>0 = 上限のまま入る / 1 = 下限までのどこかで入る / 2 = 下限でも入らない (KH_WalkTabLayoutEditor.FitAt と同じ)。</summary>
        static int FitAt(TextMeshProUGUI sample, TMP_Text target, string text, float max, float min, Vector2 size, out Vector2 preferred)
        {
            preferred = Preferred(sample, target, text, max, size);
            if (Fits(target, preferred, size)) return 0;
            preferred = Preferred(sample, target, text, min, size);
            return Fits(target, preferred, size) ? 1 : 2;
        }

        static Vector2 Preferred(TextMeshProUGUI sample, TMP_Text target, string text, float points, Vector2 size)
        {
            sample.isOrthographic = true; sample.enableAutoSizing = false; sample.font = target.font; sample.fontSize = points;
            sample.enableWordWrapping = target.enableWordWrapping; sample.margin = target.margin; sample.richText = target.richText;
            sample.rectTransform.sizeDelta = size;
            return sample.GetPreferredValues(text, size.x, Mathf.Infinity);
        }

        static bool Fits(TMP_Text target, Vector2 preferred, Vector2 size)
        { return !(preferred.y > size.y + 2f || (!target.enableWordWrapping && preferred.x > size.x + 2f)); }

        static Vector2 Size(TMP_Text t) { return t.rectTransform.rect.size; }

        // ============================================================
        // 部品
        // ============================================================

        static List<string> ExpectedRows()
        {
            var want = new List<string>();
            foreach (var (prefix, count) in KD_HandMenuScrollEditor.RowsFor(KD_TreatPouch.TabAcc)) for (int r = 0; r < count; r++) want.Add(prefix + r);
            return want;
        }

        static List<string> ContentRows(ScrollRect scroll)
        {
            var got = new List<string>();
            foreach (Transform row in scroll.content) got.Add(row.name);
            return got;
        }

        /// <summary>Page_Acc の座標 → 親 (AccSubTabs、中心 (0, 164)) の中の座標。</summary>
        static Vector2 Local(Vector2 pagePos) { return pagePos - RootPos; }

        static TextMeshProUGUI ButtonLabel(GameObject button, float max, float min)
        {
            var t = button.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            t.enableAutoSizing = true; t.fontSizeMax = max; t.fontSizeMin = min; t.fontSize = max;
            t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Ellipsis; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
            EditorUtility.SetDirty(t);
            return t;
        }

        static TextMeshProUGUI MakeText(Transform parent, string name, Vector2 pos, Vector2 size, float points, TMP_FontAsset font, string text)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var t = go.GetComponent<TextMeshProUGUI>(); if (t == null) t = go.AddComponent<TextMeshProUGUI>();
            t.font = font; t.fontSize = points; t.enableAutoSizing = false; t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false; t.raycastTarget = false;
            t.overflowMode = TextOverflowModes.Overflow; t.color = Color.white; t.text = text; EditorUtility.SetDirty(t);
            return t;
        }

        static float TabBottom(KD_TreatPouch p)
        {
            var tab = (RectTransform)p.tabImages[0].transform;
            return tab.anchoredPosition.y - tab.sizeDelta.y / 2;
        }

        static bool ActiveUnder(Transform t, Transform page)
        {
            for (var x = t; x != null && x != page; x = x.parent) if (!x.gameObject.activeSelf) return false;
            return true;
        }

        /// <summary>Page_Acc の座標 (板の中心が原点) での矩形。親の入れ子を経ても正しい値になるよう四隅から作る。</summary>
        static Rect PageRect(Transform page, RectTransform r)
        {
            var corners = new Vector3[4]; r.GetWorldCorners(corners);
            Vector2 a = page.InverseTransformPoint(corners[0]), b = page.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static Rect Center(Vector2 c, Vector2 size) { return new Rect(c - size / 2f, size); }
        static Rect Shrink(Rect r, float d) { return Rect.MinMaxRect(r.xMin + d, r.yMin + d, r.xMax - d, r.yMax - d); }
        static bool Near(Rect a, Rect b) { return Vector2.Distance(a.min, b.min) < 0.6f && Vector2.Distance(a.max, b.max) < 0.6f; }

        static void CheckEvent(List<string> e, GameObject go, VRC.Udon.UdonBehaviour target, string evt)
        {
            var b = go.GetComponent<Button>();
            if (b == null || b.onClick.GetPersistentEventCount() != 1 || b.onClick.GetPersistentTarget(0) != target || b.onClick.GetPersistentMethodName(0) != "SendCustomEvent") { e.Add("FAIL button target " + go.name + " (" + evt + ")"); return; }
            var call = new SerializedObject(b).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (call.arraySize != 1 || call.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue != evt) e.Add("FAIL button event " + go.name + " != " + evt);
            if (typeof(KD_TreatPouch).GetMethod(evt) == null) e.Add("FAIL event method missing on KD_TreatPouch: " + evt);
        }

        /// <summary>公開メソッドは `_` 始まり (ネットワーク越しに呼ばれない。KH_ToyTopRowEditor.CheckUnderscore と同じ規則)。</summary>
        static void CheckUnderscore(List<string> e, string path)
        {
            if (!File.Exists(path)) { e.Add("FAIL source missing: " + path); return; }
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                var mm = Regex.Match(lines[i], @"public\s+(?!override\b)\w+(?:\[\])?\s+([A-Za-z]\w*)\s*\(");
                if (!mm.Success || mm.Groups[1].Value.StartsWith("_")) continue;
                bool callable = i > 0 && lines[i - 1].Contains("[NetworkCallable");
                if (!callable) e.Add("FAIL public method without '_' and without [NetworkCallable]: " + path + ":" + (i + 1) + " " + mm.Groups[1].Value);
            }
        }

        static void Req(List<string> e, bool ok, string what) { if (!ok) e.Add("FAIL " + what); }
    }
}
