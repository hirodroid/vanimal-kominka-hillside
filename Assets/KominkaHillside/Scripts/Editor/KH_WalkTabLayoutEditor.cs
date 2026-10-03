using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// <summary>DES-UI-006 (2026-10-03 ユーザー決定): さんぽタブをスクロールの無い 2 列にする。設計書 docs/plans/2026-10-03-walk-tab-layout.md。
    ///
    /// Course/120 は**既存の部品をその場で並べ直すだけ**で、行を作り直さない (旧メニューの再生成 Menu/7・12・13・17 は本番シーンで禁止)。
    /// - 行 WalkRow0..11 は **Page_Walk/Scroll/Viewport/Content の中に残す** (文言の配線 UILocalizationBindings.json が
    ///   この経路で 22 本のラベルを引くため。設計書の「WalkDogs / WalkDest へ移す」は経路が変わるのでやめた)。
    ///   スクロールは止めて固定の枠にする: ScrollRect・GridLayoutGroup・ContentSizeFitter を無効、スクロールバーを非表示、
    ///   枠を内容の範囲 (y 190 〜 −258、幅 1100) に広げ、行を絶対位置に置く。tabScrolls[2] の参照はそのまま。
    /// - 左列 (x −550〜−10): 犬 6 行 540×68、間隔 8。行の地 (Image、押せない)、[左につなぐ][右につなぐ] 各 128×52、[はずす] 264×52 は同じ場所。
    /// - 右列 (x 10〜550): 見出し・まとめ (新しい TMP 2 つ。Page_Walk 直下)、行き先 6 タイル 266×72 (2 列 × 3 段、間隔 8)、
    ///   [地図と移動] 540×72 (保留中は 266×72 にして右に [申し込み 1 件] 266×72。実行時 RedrawTravel が幅を変える)。
    /// - タイルは既存の [ここへ] (Here) を行いっぱいに広げて一番後ろへ回し、名前 (Label) をその上に重ねる。Here/Label と状態 (Reason) は隠す。
    /// 何度流しても同じ結果になる (値はすべて絶対値で書く)。保存前に <see cref="Validate"/> を通し、通らなければ保存しない。
    /// Course/121 は保存済みの状態を読み直して同じ検査 + 13 言語の収まりを測り、Temp/des-ui-006/walk-tab-verify.json に出す。
    /// Course/84・85 を後から流しても、この配置を見つけたら [地図と移動] / [申し込み 1 件] を <see cref="PlaceTravelButtons"/> で同じ位置へ置き直す。</summary>
    public static class KH_WalkTabLayoutEditor
    {
        const string OutDir = "Temp/des-ui-006";
        public const string HeadingName = "WalkDestHeading", SummaryName = "WalkDestSummary";

        // ---- 配置 (Canvas 単位。板の中心が原点。板 1140×700 は変えない) ----
        public const float AreaTop = 190f, AreaBottom = -258f;
        public const float AreaHeight = AreaTop - AreaBottom, AreaMidY = (AreaTop + AreaBottom) / 2f;   // 448 / −34
        public const float AreaWidth = 1100f, ColumnWidth = 540f, LeftX = -280f, RightX = 280f;
        public const float DogRowHeight = 68f, DogRowPitch = 76f;
        public static readonly Vector2 AttachSize = new Vector2(128f, 52f), OffSize = new Vector2(264f, 52f);
        public const float LeftButtonX = 66f, RightButtonX = 202f, OffButtonX = 134f;   // 行の中の x (行の中心が原点)
        public static readonly Vector2 SwatchPos = new Vector2(-256f, 12f), SwatchSize = new Vector2(22f, 22f);
        public static readonly Vector2 NamePos = new Vector2(-123f, 14f), NameSize = new Vector2(234f, 32f);
        public static readonly Vector2 StatePos = new Vector2(-136f, -17f), StateSize = new Vector2(260f, 28f);
        public static readonly Vector2 TileSize = new Vector2(266f, 72f);
        public const float TileTop = 100f, TilePitchX = 274f, TilePitchY = 80f;
        public const float HeadingY = 175f, HeadingHeight = 30f, SummaryY = 134f, SummaryHeight = 52f;
        public const float MapY = -184f, MapHeight = 72f;
        // 文字の大きさ (上限 / オートサイズの下限)。下限に張り付いたら「入っていない」(vrc-world-ui §10)
        public const float NamePt = 22f, NameMin = 16f, StatePt = 18f, StateMin = 15f, ActionPt = 20f, ActionMin = 16f;
        public const float TilePt = 22f, TileMin = 15f, HeadingPt = 20f, HeadingMin = 16f, SummaryPt = 18f, SummaryMin = 15f, MapPt = 22f, MapMin = 16f;
        /// <summary>行き先のタイルの順 = KH_CourseMenuEditor の行き先の順 (WalkRow6..11)。</summary>
        static readonly string[] DestEvents = { "_WalkPlaza", "_WalkGate", "_WalkLoopForward", "_WalkLoopReverse", "_WalkUpperPlaza", "_WalkRiverbank" };
        static readonly string[] DestKeys = { "KD_TreatPouch.WordWalkPlaza", "KD_TreatPouch.WordWalkGate", "course.walk.forward", "course.walk.reverse", "course.walk.upper", "course.walk.river" };
        static readonly string[] DefaultDogKeys = { "activities.dog.Corgi", "activities.dog.Pome", "activities.dog.Lab", "activities.dog.Dachs", "activities.dog.Shiba", "activities.dog.Samoyed" };
        static readonly Color HeadingColor = new Color(0.85f, 0.70f, 0.35f);
        static readonly Color RowColor = new Color(0.184f, 0.161f, 0.145f), RowMineColor = new Color(0.184f, 0.290f, 0.220f);

        public static Vector2 DogRowPos(int i) { return new Vector2(LeftX, AreaTop - DogRowHeight / 2f - i * DogRowPitch); }
        public static Vector2 TilePos(int k) { return new Vector2(RightX + (k % 2 == 0 ? -0.5f : 0.5f) * TilePitchX, TileTop - TileSize.y / 2f - (k / 2) * TilePitchY); }
        public static Vector2 OpenPos(bool pending) { return new Vector2(pending ? KD_TreatPouch.WalkOpenPendingX : KD_TreatPouch.WalkOpenX, MapY); }
        public static Vector2 OpenSize(bool pending) { return new Vector2(pending ? KD_TreatPouch.WalkOpenPendingWidth : KD_TreatPouch.WalkOpenWidth, MapHeight); }
        public static Vector2 PendingPos { get { return new Vector2(RightX + (ColumnWidth - KD_TreatPouch.WalkOpenPendingWidth) / 2f, MapY); } }
        public static Vector2 PendingSize { get { return new Vector2(KD_TreatPouch.WalkOpenPendingWidth, MapHeight); } }

        /// <summary>2 列の配置が入っているか (Course/120 を流した後か)。Course/26・81・84・85、Menu/20・21 が分岐に使う。</summary>
        public static bool IsApplied(KD_TreatPouch p)
        {
            if (p == null || p.walkSummaryLabel == null || p.tabPages == null || p.tabPages.Length <= KD_TreatPouch.TabWalk || p.tabPages[KD_TreatPouch.TabWalk] == null) return false;
            return p.walkSummaryLabel.transform.parent == p.tabPages[KD_TreatPouch.TabWalk].transform;
        }

        [Serializable] class Report
        {
            public string utc, scene, mode, status = "RUNNING";
            public int errors;
            public List<string> findings = new List<string>();
            public List<string> targets = new List<string>();
            public List<string> fit = new List<string>();
            public List<string> info = new List<string>();
            public string limitations = "Editor structure, saved Udon references and TMP size measurement only. PCVR laser / Desktop mouse / Touch input, readability in shade or at night, and the runtime 4-second notes are not tested (Play).";
        }

        // ============================================================
        // Course/120 生成 (その場で並べ直す)
        // ============================================================

        [MenuItem("Tools/KominkaHillside/Course/120 - Walk Tab Layout")]
        public static void Apply()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var p = KH_CourseTravelEditor.FindPouch();
            var report = new Report { utc = DateTime.UtcNow.ToString("o"), scene = p.gameObject.scene.path, mode = "apply" };
            // ---- 前提 (何も変えずに止まる) ----
            var page = p.tabPages[KD_TreatPouch.TabWalk] != null ? p.tabPages[KD_TreatPouch.TabWalk].transform : null;
            var scroll = p.tabScrolls != null && p.tabScrolls.Length > KD_TreatPouch.TabWalk ? p.tabScrolls[KD_TreatPouch.TabWalk] : null;
            if (page == null || scroll == null || scroll.content == null || scroll.viewport == null || scroll.transform.parent != page)
                throw new InvalidOperationException("Walk page and its Scroll/Viewport/Content required; nothing changed");
            var content = scroll.content;
            if (content.childCount != 12 || p.walkLabels == null || p.walkLabels.Length != 12 || p.walkStateLabels == null || p.walkStateLabels.Length != 12
                || p.walkHereButtons == null || p.walkHereButtons.Length != 6 || p.walkDestinationIds == null || p.walkDestinationIds.Length != 6)
                throw new InvalidOperationException("Course menu with 6 dogs + 6 destinations required (Course/25); nothing changed");
            for (int i = 0; i < 12; i++)
            {
                var row = content.Find("WalkRow" + i);
                if (row == null || row.GetSiblingIndex() != i || row.Find("Label") == null || row.Find("Reason") == null) throw new InvalidOperationException("WalkRow" + i + " missing or reordered; nothing changed");
                foreach (var n in i < 6 ? new[] { "Left", "Right", "Off", "Left/Label", "Right/Label", "Off/Label" } : new[] { "Here", "Here/Label" })
                    if (row.Find(n) == null) throw new InvalidOperationException("WalkRow" + i + "/" + n + " missing; nothing changed");
            }
            if (p.travelOpenButton == null || p.travelOpenButton.transform.parent != page) throw new InvalidOperationException("[地図と移動] missing (run Course/84 first); nothing changed");
            if (p.travelJoinPendingButton == null || p.travelJoinPendingButton.transform.parent != page) throw new InvalidOperationException("[申し込み 1 件] missing (run Course/85 first); nothing changed");
            var font = p.sectionTitleLabel != null ? p.sectionTitleLabel.font : null;
            if (font == null) throw new InvalidOperationException("Shared UI font missing; nothing changed");
            if (UdonSharpEditorUtility.GetBackingUdonBehaviour(p) == null) throw new InvalidOperationException("Menu backing behaviour missing; nothing changed");

            Directory.CreateDirectory(OutDir);
            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-walk-tab-layout.unity", true);
            try
            {
                // ---- スクロールを止めて固定の枠にする (経路は変えない) ----
                var root = (RectTransform)scroll.transform;
                Place(root, new Vector2(0f, AreaMidY), new Vector2(AreaWidth, AreaHeight));
                Place(scroll.viewport, Vector2.zero, new Vector2(AreaWidth, AreaHeight));
                var viewImage = scroll.viewport.GetComponent<Image>(); if (viewImage != null) { viewImage.raycastTarget = false; EditorUtility.SetDirty(viewImage); }
                Place(content, Vector2.zero, new Vector2(AreaWidth, AreaHeight));
                content.anchoredPosition = Vector2.zero;
                var grid = content.GetComponent<GridLayoutGroup>(); if (grid != null) { grid.enabled = false; EditorUtility.SetDirty(grid); }
                var fitter = content.GetComponent<ContentSizeFitter>(); if (fitter != null) { fitter.enabled = false; EditorUtility.SetDirty(fitter); }
                scroll.enabled = false; EditorUtility.SetDirty(scroll);
                if (scroll.verticalScrollbar != null) scroll.verticalScrollbar.gameObject.SetActive(false);

                // ---- 左列: 犬 6 行 ----
                var rowImages = new Image[6];
                for (int i = 0; i < 6; i++)
                {
                    var row = (RectTransform)content.Find("WalkRow" + i);
                    Place(row, InContent(DogRowPos(i)), new Vector2(ColumnWidth, DogRowHeight));
                    var bg = row.GetComponent<Image>(); if (bg == null) bg = row.gameObject.AddComponent<Image>();
                    KD_UiSpriteEditor.ApplyFlatSkin(bg); bg.color = RowColor; bg.raycastTarget = false; EditorUtility.SetDirty(bg);
                    rowImages[i] = bg;
                    var swatch = row.Find("Swatch");
                    if (swatch != null) { Place(swatch, SwatchPos, SwatchSize); var g = swatch.GetComponent<Graphic>(); if (g != null) g.raycastTarget = false; }
                    Text(row.Find("Label"), NamePos, NameSize, NamePt, NameMin, false, TextAlignmentOptions.MidlineLeft);
                    Text(row.Find("Reason"), StatePos, StateSize, StatePt, StateMin, false, TextAlignmentOptions.MidlineLeft);
                    row.Find("Reason").gameObject.SetActive(true);
                    Button(row.Find("Left"), new Vector2(LeftButtonX, 0f), AttachSize);
                    Button(row.Find("Right"), new Vector2(RightButtonX, 0f), AttachSize);
                    Button(row.Find("Off"), new Vector2(OffButtonX, 0f), OffSize);
                    // 開いた直後 (Redraw 前) に 3 つが重ならないよう、保存時は左右だけを出す (実行時は RedrawWalk が自分の犬の行だけ [はずす] にする)
                    row.Find("Left").gameObject.SetActive(true); row.Find("Right").gameObject.SetActive(true); row.Find("Off").gameObject.SetActive(false);
                }

                // ---- 右列: 行き先 6 タイル ----
                for (int k = 0; k < 6; k++)
                {
                    var row = (RectTransform)content.Find("WalkRow" + (6 + k));
                    Place(row, InContent(TilePos(k)), TileSize);
                    var here = (RectTransform)row.Find("Here");
                    Place(here, Vector2.zero, TileSize);
                    here.SetSiblingIndex(0);   // 名前を上に重ねる (兄弟の順で後ろが手前)
                    here.gameObject.SetActive(true);
                    var hereImage = here.GetComponent<Image>(); if (hereImage != null) { hereImage.raycastTarget = true; EditorUtility.SetDirty(hereImage); }
                    var hereLabel = here.Find("Label");
                    hereLabel.gameObject.SetActive(false);   // 「ここへ」は出さない (文言の配線は残る)
                    var hereText = hereLabel.GetComponent<Graphic>(); if (hereText != null) { hereText.raycastTarget = false; EditorUtility.SetDirty(hereText); }
                    Text(row.Find("Label"), Vector2.zero, TileSize - new Vector2(16f, 6f), TilePt, TileMin, true, TextAlignmentOptions.Center);
                    row.Find("Label").SetAsLastSibling();
                    row.Find("Reason").gameObject.SetActive(false);   // 「いまの行き先」はタイルの名前の下に出す (RedrawWalk)
                }

                // ---- 右列: 見出し・まとめ・[地図と移動]・[申し込み 1 件] ----
                var baseCatalog = KD_LocalizationEditor.Load();
                var heading = MakeText(page, HeadingName, new Vector2(RightX, HeadingY), new Vector2(ColumnWidth, HeadingHeight), HeadingPt, HeadingMin, false, font,
                    KD_LocalizationEditor.Text(baseCatalog, "ja", "KD_TreatPouch.WordWalkStateDest"));
                heading.color = HeadingColor;
                var summary = MakeText(page, SummaryName, new Vector2(RightX, SummaryY), new Vector2(ColumnWidth, SummaryHeight), SummaryPt, SummaryMin, true, font,
                    KD_LocalizationEditor.Text(baseCatalog, "ja", "KD_TreatPouch.WordWalkSummaryNone"));
                PlaceTravelButtons(p.travelOpenButton, p.travelJoinPendingButton);

                // ---- 配線 (同期しない参照だけ。同期変数は 0 本のまま) ----
                p.walkDestHeading = heading; p.walkSummaryLabel = summary;
                p.walkRowImages = rowImages; p.walkOpenRect = (RectTransform)p.travelOpenButton.transform;
                p.walkRowColor = RowColor; p.walkRowMineColor = RowMineColor;
                UdonSharpEditorUtility.CopyProxyToUdon(p); EditorUtility.SetDirty(p);
                EditorSceneManager.MarkSceneDirty(p.gameObject.scene);

                // ---- 保存前の検査 (配置・配線・押せる面・13 言語の収まり。通らなければ保存しない) ----
                var errors = new List<string>();
                Validate(p, errors, report.targets, report.fit, report.info);
                // backing の変数表は保存時に揃うことがある (vrc-udonsharp §4)。保存前は記録だけにして、Course/121 (保存・再読込後) で判定する
                foreach (var w in errors.Where(x => x.StartsWith("FAIL backing")).ToList()) { report.info.Add("pre-save " + w); errors.Remove(w); }
                if (errors.Count > 0)
                {
                    report.findings = errors; report.errors = errors.Count; report.status = "NOT_SAVED";
                    File.WriteAllText(OutDir + "/walk-tab-apply.json", JsonUtility.ToJson(report, true));
                    throw new InvalidOperationException("[KH Walk Tab] Layout check failed (" + errors.Count + "); scene NOT saved. Reopen the scene without saving to discard. First: " + errors[0]);
                }
                if (!EditorSceneManager.SaveScene(p.gameObject.scene)) throw new IOException("Walk tab layout save failed");
            }
            catch
            {
                Debug.LogError("[KH Walk Tab] Apply failed; the scene before this run is at " + OutDir + "/before-walk-tab-layout.unity. Reopen the scene without saving to discard partial changes.");
                throw;
            }
            report.status = "SAVED";
            File.WriteAllText(OutDir + "/walk-tab-apply.json", JsonUtility.ToJson(report, true));
            Debug.Log("[KH Walk Tab] Two-column walk tab applied and saved. Run Course/121 (verify after reload), then Course/26, Course/81, Menu/20, Menu/21.");
        }

        /// <summary>[地図と移動] を右列の下 (540×72)、[申し込み 1 件] をその右半分 (266×72) に置く。ラベルはボタンに合わせて伸びる。
        /// Course/120 と、配置を見つけたときの Course/84・85 が呼ぶ (再実行で上段へ戻さない)。保留中の幅は実行時 RedrawTravel が変える。</summary>
        public static void PlaceTravelButtons(GameObject open, GameObject pending)
        {
            if (open != null)
            {
                Place(open.transform, OpenPos(false), OpenSize(false));
                ButtonLabel(open.transform.Find("Label"), MapPt, MapMin);
                open.transform.SetAsLastSibling();
            }
            if (pending != null)
            {
                Place(pending.transform, PendingPos, PendingSize);
                ButtonLabel(pending.transform.Find("Label"), MapPt, MapMin);
                pending.transform.SetAsLastSibling();
            }
        }

        // ============================================================
        // Course/121 検査
        // ============================================================

        [MenuItem("Tools/KominkaHillside/Course/121 - Verify Walk Tab Layout")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var p = KH_CourseTravelEditor.FindPouch();
            var report = new Report { utc = DateTime.UtcNow.ToString("o"), scene = p.gameObject.scene.path, mode = "verify" };
            try { Validate(p, report.findings, report.targets, report.fit, report.info); }
            catch (Exception ex) { report.findings.Add("Exception: " + ex.Message); }
            report.errors = report.findings.Count; report.status = report.errors == 0 ? "WALK_TAB_OK" : "FAIL";
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/walk-tab-verify.json", JsonUtility.ToJson(report, true));
            if (report.errors != 0) throw new InvalidOperationException("[KH Walk Tab] FAIL " + report.errors + ":\n" + string.Join("\n", report.findings));
            Debug.Log("[KH Walk Tab] WALK_TAB_OK targets=" + report.targets.Count + " fit=" + report.fit.Count + " (Temp/des-ui-006/walk-tab-verify.json)");
        }

        /// <summary>配置・配線・押せる面の重なり・(fit が非 null なら) 13 言語の収まり。Course/120 の保存前、Course/121、Course/26、Menu/21 が使う。</summary>
        public static void Validate(KD_TreatPouch p, List<string> e, List<string> targets, List<string> fit, List<string> info)
        {
            if (!IsApplied(p)) { e.Add("FAIL walk tab two-column layout not applied (run Course/120)"); return; }
            var page = p.tabPages[KD_TreatPouch.TabWalk].transform;
            var scroll = p.tabScrolls[KD_TreatPouch.TabWalk];
            var pb = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            // ---- 固定の枠 (スクロールしない) ----
            Req(e, scroll != null && scroll.transform.parent == page && !scroll.enabled, "walk ScrollRect disabled (no drag / no scroll), tabScrolls[2] kept");
            if (scroll == null) return;
            var content = scroll.content;
            Req(e, scroll.verticalScrollbar == null || !scroll.verticalScrollbar.gameObject.activeSelf, "walk scrollbar hidden");
            var grid = content.GetComponent<GridLayoutGroup>(); var fitter = content.GetComponent<ContentSizeFitter>();
            Req(e, (grid == null || !grid.enabled) && (fitter == null || !fitter.enabled), "walk grid / size fitter disabled (rows are placed absolutely)");
            var viewImage = scroll.viewport.GetComponent<Image>();
            Req(e, viewImage == null || !viewImage.raycastTarget, "walk viewport does not catch rays");
            Req(e, Near(PageRect(page, (RectTransform)scroll.transform), Center(new Vector2(0f, AreaMidY), new Vector2(AreaWidth, AreaHeight))), "walk frame y " + AreaTop + " .. " + AreaBottom + " (width " + AreaWidth + ")");
            Req(e, content.childCount == 12, "12 rows stay in Scroll/Viewport/Content (localization bindings resolve by path)");
            foreach (var c in page.GetComponentsInChildren<Canvas>(true)) e.Add("FAIL nested Canvas under Page_Walk changes the draw order: " + c.name);
            // ---- 犬の行 ----
            for (int i = 0; i < 6 && i < content.childCount; i++)
            {
                var row = content.Find("WalkRow" + i);
                if (row == null || row.GetSiblingIndex() != i) { e.Add("FAIL WalkRow" + i + " missing or reordered"); continue; }
                Req(e, Near(PageRect(page, (RectTransform)row), Center(DogRowPos(i), new Vector2(ColumnWidth, DogRowHeight))), "dog row " + i + " at " + DogRowPos(i) + " 540x68");
                var bg = row.GetComponent<Image>();
                Req(e, bg != null && !bg.raycastTarget && p.walkRowImages != null && p.walkRowImages.Length == 6 && p.walkRowImages[i] == bg, "dog row " + i + " background wired (walkRowImages), not a ray target");
                Req(e, p.walkLabels[i] == row.Find("Label").GetComponent<TextMeshProUGUI>() && p.walkStateLabels[i] == row.Find("Reason").GetComponent<TextMeshProUGUI>(), "dog row " + i + " name / state labels wired");
                foreach (var (name, pos, size) in new[] { ("Left", new Vector2(LeftButtonX, 0f), AttachSize), ("Right", new Vector2(RightButtonX, 0f), AttachSize), ("Off", new Vector2(OffButtonX, 0f), OffSize) })
                {
                    var b = row.Find(name);
                    Req(e, b != null && Near(PageRect(page, (RectTransform)b), Center(DogRowPos(i) + pos, size)), "dog row " + i + " [" + name + "] " + size.x + "x" + size.y + " at row x " + pos.x);
                    if (b != null) CheckEvent(e, b.gameObject, pb, "_Walk" + name + i);
                }
                Req(e, p.walkLeftButtons[i] != null && p.walkLeftButtons[i].transform == row.Find("Left") && p.walkRightButtons[i].transform == row.Find("Right") && p.walkOffButtons[i].transform == row.Find("Off"), "dog row " + i + " button arrays");
                Req(e, row.Find("Left").gameObject.activeSelf && row.Find("Right").gameObject.activeSelf && !row.Find("Off").gameObject.activeSelf, "dog row " + i + " saved with [left][right] shown and [unleash] hidden (RedrawWalk swaps them for your own dog)");
            }
            // ---- 行き先のタイル ----
            for (int k = 0; k < 6; k++)
            {
                var row = content.Find("WalkRow" + (6 + k));
                if (row == null || row.GetSiblingIndex() != 6 + k) { e.Add("FAIL WalkRow" + (6 + k) + " missing or reordered"); continue; }
                Req(e, Near(PageRect(page, (RectTransform)row), Center(TilePos(k), TileSize)), "tile " + k + " at " + TilePos(k) + " 266x72");
                var here = row.Find("Here");
                Req(e, here != null && here.gameObject.activeSelf && here.GetSiblingIndex() == 0 && Near(PageRect(page, (RectTransform)here), Center(TilePos(k), TileSize)), "tile " + k + ": the whole tile is the button (Here first sibling, 266x72)");
                if (here == null) continue;
                CheckEvent(e, here.gameObject, pb, DestEvents[k]);
                Req(e, p.walkHereButtons[k] == here.GetComponent<Button>() && p.walkHereImages[k] == here.GetComponent<Image>(), "tile " + k + " wired (walkHereButtons / Images)");
                Req(e, !here.Find("Label").gameObject.activeSelf, "tile " + k + " hides the old [ここへ] label");
                var label = row.Find("Label").GetComponent<TextMeshProUGUI>();
                Req(e, label == p.walkLabels[6 + k] && label.enableWordWrapping && label.richText && label.transform.GetSiblingIndex() > here.GetSiblingIndex(), "tile " + k + " name label above the button, wraps, rich text (size tag)");
                Req(e, !row.Find("Reason").gameObject.activeSelf, "tile " + k + " hides the old state line");
                Req(e, p.walkDestinationIds[k] == new[] { 1, 0, 2, 3, 4, 5 }[k], "tile " + k + " destination id");
            }
            // ---- 見出し・まとめ・地図・申し込み ----
            var heading = page.Find(HeadingName); var summary = page.Find(SummaryName);
            Req(e, heading != null && p.walkDestHeading == heading.GetComponent<TextMeshProUGUI>() && Near(PageRect(page, (RectTransform)heading), Center(new Vector2(RightX, HeadingY), new Vector2(ColumnWidth, HeadingHeight))), "heading at y " + HeadingY + " wired");
            Req(e, summary != null && p.walkSummaryLabel == summary.GetComponent<TextMeshProUGUI>() && p.walkSummaryLabel.enableWordWrapping && Near(PageRect(page, (RectTransform)summary), Center(new Vector2(RightX, SummaryY), new Vector2(ColumnWidth, SummaryHeight))), "summary at y " + SummaryY + " (2 lines) wired");
            Req(e, p.travelOpenButton != null && p.travelOpenButton.transform.parent == page && p.walkOpenRect == (RectTransform)p.travelOpenButton.transform
                && Near(PageRect(page, p.walkOpenRect), Center(OpenPos(false), OpenSize(false))), "[地図と移動] 540x72 at " + OpenPos(false) + " (walkOpenRect wired)");
            if (p.travelOpenButton != null) { CheckEvent(e, p.travelOpenButton, pb, "_TravelOpenMap"); Req(e, Stretched(p.travelOpenButton.transform.Find("Label")), "[地図と移動] label follows the button width (pending: 266)"); }
            Req(e, p.travelJoinPendingButton != null && p.travelJoinPendingButton.transform.parent == page && !p.travelJoinPendingButton.activeSelf
                && Near(PageRect(page, (RectTransform)p.travelJoinPendingButton.transform), Center(PendingPos, PendingSize)), "[申し込み 1 件] 266x72 at " + PendingPos + ", hidden at start");
            if (p.travelJoinPendingButton != null) { CheckEvent(e, p.travelJoinPendingButton, pb, "_TravelShowNotice"); Req(e, Stretched(p.travelJoinPendingButton.transform.Find("Label")), "[申し込み 1 件] label follows the button"); }
            // 保留中の [地図と移動] の位置 (実行時の定数) が [申し込み 1 件] と 8 あけて並ぶ
            {
                Rect half = Center(OpenPos(true), OpenSize(true)), pend = Center(PendingPos, PendingSize);
                Req(e, Mathf.Abs(pend.xMin - half.xMax - 8f) < 0.5f && Mathf.Abs(half.xMin - (RightX - ColumnWidth / 2f)) < 0.5f && Mathf.Abs(pend.xMax - (RightX + ColumnWidth / 2f)) < 0.5f, "pending: [地図と移動] 266 + gap 8 + [申し込み 1 件] 266 fill the right column");
            }
            // ---- 実体 (backing) にも入っていること ----
            foreach (var field in new[] { "walkDestHeading", "walkSummaryLabel", "walkOpenRect" })
                if (pb == null || !pb.publicVariables.TryGetVariableValue(field, out object v) || v == null) e.Add("FAIL backing " + field + " (rerun Course/120)");
            if (pb == null || !pb.publicVariables.TryGetVariableValue("walkRowImages", out object arr) || !(arr is Array a) || a.Length != 6) e.Add("FAIL backing walkRowImages length 6 (rerun Course/120)");
            // ---- 押せる面: 大きさ・板の中・重なり・手前に光線を奪うものが無い ----
            CheckTargets(p, page, e, targets, info);
            // ---- 13 言語の収まり ----
            if (fit != null) Measure(p, e, fit, info);
        }

        /// <summary>押せる面 26 個 (左右 12・はずす 6・タイル 6・地図・申し込み)。高さ 48 以上 (資料 XR-03 の試作初期値)、内容の範囲の中、
        /// 互いに重ならない (同じ行の [はずす] と左右は同時に出ないので除く)、中心を後ろ (手前に描かれる) の光線対象が覆わない。</summary>
        static void CheckTargets(KD_TreatPouch p, Transform page, List<string> e, List<string> targets, List<string> info)
        {
            var content = p.tabScrolls[KD_TreatPouch.TabWalk].content;
            var list = new List<(string name, RectTransform rt, int group)>();
            for (int i = 0; i < 6; i++)
            {
                var row = content.Find("WalkRow" + i); if (row == null) continue;
                list.Add(("dog" + i + " Left", (RectTransform)row.Find("Left"), 100 + i));
                list.Add(("dog" + i + " Right", (RectTransform)row.Find("Right"), 100 + i));
                list.Add(("dog" + i + " Off", (RectTransform)row.Find("Off"), 200 + i));
            }
            for (int k = 0; k < 6; k++) { var row = content.Find("WalkRow" + (6 + k)); if (row != null) list.Add(("tile" + k, (RectTransform)row.Find("Here"), 300 + k)); }
            if (p.travelOpenButton != null) list.Add(("map", (RectTransform)p.travelOpenButton.transform, 400));
            if (p.travelJoinPendingButton != null) list.Add(("pending", (RectTransform)p.travelJoinPendingButton.transform, 401));
            var area = new Rect(-AreaWidth / 2f, AreaBottom, AreaWidth, AreaHeight);
            var rects = list.Select(x => PageRect(page, x.rt)).ToList();
            for (int a = 0; a < list.Count; a++)
            {
                var r = rects[a];
                targets?.Add(list[a].name + " " + r.width.ToString("0") + "x" + r.height.ToString("0") + " at (" + r.center.x.ToString("0") + ", " + r.center.y.ToString("0") + ")");
                Req(e, r.height >= 48f - 0.5f && r.width >= 48f - 0.5f, "touch target " + list[a].name + " >= 48 (" + r.width + "x" + r.height + ")");
                Req(e, r.xMin >= area.xMin - 0.5f && r.xMax <= area.xMax + 0.5f && r.yMin >= area.yMin - 0.5f && r.yMax <= area.yMax + 0.5f, "touch target " + list[a].name + " inside the walk area");
                var img = list[a].rt.GetComponent<Image>();
                Req(e, img != null && img.raycastTarget && list[a].rt.GetComponent<Button>() != null && list[a].rt.GetComponent<Button>().targetGraphic == img, "touch target " + list[a].name + " is its own ray target");
                foreach (var g in list[a].rt.GetComponentsInChildren<Graphic>(true)) if (g.transform != list[a].rt && g.raycastTarget) e.Add("FAIL decoration catches rays: " + list[a].name + "/" + g.name);
                for (int b = a + 1; b < list.Count; b++)
                {
                    bool exclusive = (list[a].group % 100 == list[b].group % 100) && ((list[a].group / 100 == 1 && list[b].group / 100 == 2) || (list[a].group / 100 == 2 && list[b].group / 100 == 1));
                    if (exclusive || (list[a].group == 400 && list[b].group == 401)) continue;   // 保留中は [地図と移動] が縮む (実行時)
                    if (rects[a].Overlaps(Shrink(rects[b], 0.5f))) e.Add("FAIL touch targets overlap: " + list[a].name + " / " + list[b].name);
                }
            }
            // 手前 (兄弟順で後ろ) に描かれる光線対象が中心を覆わない。実行時に出し入れするもの (はずす・左右・申し込み) も含めて見る
            var toggled = new HashSet<Transform>(list.Select(x => x.rt.transform));
            var graphics = page.GetComponentsInChildren<Graphic>(true).Where(g => g.raycastTarget && (ActiveUnder(g.transform, page) || toggled.Contains(g.transform))).ToList();
            var order = page.GetComponentsInChildren<Graphic>(true).ToList();
            for (int a = 0; a < list.Count; a++)
            {
                var own = list[a].rt.GetComponent<Graphic>(); if (own == null) continue;
                Vector2 c = rects[a].center; int mine = order.IndexOf(own);
                foreach (var g in graphics)
                {
                    if (g == own || g.transform.IsChildOf(list[a].rt) || order.IndexOf(g) < mine) continue;
                    int other = list.FindIndex(x => x.rt.transform == g.transform);
                    if (other >= 0) continue;   // ほかの押せる面との重なりは上で数えた
                    if (PageRect(page, g.rectTransform).Contains(c)) e.Add("FAIL " + g.name + " is drawn above " + list[a].name + " and catches its rays");
                }
            }
            // 隙間 (記録): 左右 8・上下 (行どうし) 24・タイル 8・タイルと地図 16
            info?.Add("gaps: left/right " + (RightButtonX - LeftButtonX - AttachSize.x) + ", dog rows vertical " + (DogRowPitch - AttachSize.y) + ", tiles " + (TilePitchX - TileSize.x) + "/" + (TilePitchY - TileSize.y) + ", tiles-to-map " + (TileTop - 2f * TilePitchY - TileSize.y - (MapY + MapHeight / 2f)));
        }

        // ============================================================
        // 13 言語の収まり (KD_LocalizationEditor.MeasureLabelFit と同じ測り方。下限は配置の定数で固定:
        // Bind (ApplyButtonAutoSize) がボタンの下限を 14pt に下げても、ここでは設計の下限 16 で測る)
        // ============================================================

        public static void Measure(KD_TreatPouch p, List<string> e, List<string> fit, List<string> info)
        {
            var lang = p.language;
            var content = p.tabScrolls[KD_TreatPouch.TabWalk].content;
            var go = new GameObject("WalkTabMeasure", typeof(RectTransform)); go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var sample = go.AddComponent<TextMeshProUGUI>();
                var row0 = content.Find("WalkRow0");
                var leftLabel = row0.Find("Left/Label").GetComponent<TextMeshProUGUI>();
                var offLabel = row0.Find("Off/Label").GetComponent<TextMeshProUGUI>();
                var nameLabel = p.walkLabels[0]; var stateLabel = p.walkStateLabels[0];
                var tileLabel = p.walkLabels[6];
                var openLabel = p.travelOpenButton.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                var pendingLabel = p.travelJoinPendingButton.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                // 保留中の [地図と移動] のラベル幅 (ボタン 266 − 余白 24)
                Vector2 openHalf = new Vector2(KD_TreatPouch.WalkOpenPendingWidth - 24f, MapHeight - 4f);
                string[] states = { "KD_TreatPouch.WordWalkStateNear", "KD_TreatPouch.WordWalkStateLeft", "KD_TreatPouch.WordWalkStateRight", "KD_TreatPouch.WordWalkStateOther", "KD_TreatPouch.WordWalkStateGathering", "KD_TreatPouch.WordWalkStateCalled", "KD_TreatPouch.WordWalkStateBall" };
                // 行に 4 秒出す結果・理由 (長いものは行では … で切れ、下端の 1 行に全文が出る。記録だけ)
                string[] rowNotes = { "KD_TreatPouch.WordWalkAttachingLeft", "KD_TreatPouch.WordWalkAttachingRight", "KD_TreatPouch.WordWalkAttachedLeft", "KD_TreatPouch.WordWalkAttachedRight", "KD_TreatPouch.WordWalkDetaching", "KD_TreatPouch.WordWalkDetached",
                    "KD_TreatPouch.WordWalkDetachMineOnly", "KD_TreatPouch.WordWalkGateAlready", "KD_TreatPouch.WordWalkGateOther", "KD_TreatPouch.WordWalkGateGathering", "KD_TreatPouch.WordWalkGateBall", "KD_TreatPouch.WordWalkGateFullLeft", "KD_TreatPouch.WordWalkGateFullRight",
                    "KD_TreatPouch.WordWalkGateFar", "KD_TreatPouch.WordWalkGateCalled", "KD_TreatPouch.WordWalkGateUnavailable", "course.water.busy", "KD_TreatPouch.WordWalkUnknown" };
                string[] summaryNotes = { "KD_TreatPouch.WordWalkDestRemembered", "KD_TreatPouch.WordWalkDestTelling", "KD_TreatPouch.WordWalkToPlaza", "KD_TreatPouch.WordWalkToGate", "course.walk.confirmed", "KD_TreatPouch.WordWalkUnknown" };
                foreach (string code in lang.languageCodes)
                {
                    string T(string key) { return lang._TextForLanguage(key, code); }
                    void Must(TMP_Text target, string text, float max, float min, Vector2 size, string what)
                    {
                        int r = FitAt(sample, target, text, max, min, size, out Vector2 pref);
                        fit.Add(code + " " + what + " rect=" + size + " max=" + max + " min=" + min + " preferred=" + pref + " fit=" + r);
                        if (r == 2) e.Add("FAIL label does not fit at " + min + "pt: " + code + " " + what + " \"" + text.Replace("\n", " / ") + "\"");
                        else if (r == 1) info?.Add("shrinks " + code + " " + what);
                    }
                    void Note(TMP_Text target, string text, float max, float min, Vector2 size, string what)
                    {
                        int r = FitAt(sample, target, text, max, min, size, out Vector2 pref);
                        fit.Add(code + " " + what + " rect=" + size + " preferred=" + pref + " fit=" + r + (r == 2 ? " (ellipsis here; full text in the footer)" : ""));
                        if (r == 2) info?.Add("ellipsis " + code + " " + what);
                    }
                    Must(leftLabel, T("KD_TreatPouch.WordWalkLeft"), ActionPt, ActionMin, Size(leftLabel), "[left]");
                    Must(leftLabel, T("KD_TreatPouch.WordWalkRight"), ActionPt, ActionMin, Size(leftLabel), "[right]");
                    Must(offLabel, T("KD_TreatPouch.WordWalkOff"), ActionPt, ActionMin, Size(offLabel), "[unleash]");
                    for (int i = 0; i < 6; i++)
                    {
                        string key = p.toyNameKeys != null && i < p.toyNameKeys.Length && !string.IsNullOrEmpty(p.toyNameKeys[i]) ? p.toyNameKeys[i] : DefaultDogKeys[i];
                        Must(nameLabel, T(key), NamePt, NameMin, Size(nameLabel), "dog name " + i);
                    }
                    foreach (var key in states) Must(stateLabel, T(key), StatePt, StateMin, Size(stateLabel), "state " + key);
                    Must(stateLabel, T("KD_TreatPouch.WordWalkStateFar").Replace("{0}", "999"), StatePt, StateMin, Size(stateLabel), "state far 999 m");
                    foreach (var key in rowNotes) Note(stateLabel, T(key), StatePt, StateMin, Size(stateLabel), "row note " + key);
                    for (int k = 0; k < 6; k++)
                    {
                        string name = T(DestKeys[k]);
                        Must(tileLabel, name, TilePt, TileMin, Size(tileLabel), "tile " + k);
                        Must(tileLabel, name + "\n<size=73%>" + T("KD_TreatPouch.WordWalkStateDestChosen") + "</size>", TilePt, TileMin, Size(tileLabel), "tile " + k + " chosen");
                        Must(p.walkSummaryLabel, T("course.walk.summary").Replace("{0}", "6").Replace("{1}", name), SummaryPt, SummaryMin, Size(p.walkSummaryLabel), "summary " + k);
                    }
                    Must(p.walkDestHeading, T("KD_TreatPouch.WordWalkStateDest"), HeadingPt, HeadingMin, Size(p.walkDestHeading), "heading");
                    Must(p.walkSummaryLabel, T("KD_TreatPouch.WordWalkSummaryNone"), SummaryPt, SummaryMin, Size(p.walkSummaryLabel), "summary none");
                    Must(p.walkSummaryLabel, T("course.walk.mixed").Replace("{0}", "6"), SummaryPt, SummaryMin, Size(p.walkSummaryLabel), "summary mixed");
                    foreach (var key in summaryNotes) Note(p.walkSummaryLabel, T(key).Replace("{0}", "6"), SummaryPt, SummaryMin, Size(p.walkSummaryLabel), "summary note " + key);
                    Must(openLabel, T("course.travel.map.open"), MapPt, MapMin, Size(openLabel), "[map] 540");
                    Must(openLabel, T("course.travel.map.open"), MapPt, MapMin, openHalf, "[map] 266 (pending)");
                    Must(pendingLabel, T("course.travel.join.pending"), MapPt, MapMin, Size(pendingLabel), "[join pending]");
                    Must(pendingLabel, T("course.travel.call.pending"), MapPt, MapMin, Size(pendingLabel), "[call pending]");
                    // 下端の 1 行 (全文が出る場所)
                    if (p.feedbackLabel != null)
                        foreach (var key in rowNotes.Concat(summaryNotes))
                        {
                            int r = KD_LocalizationEditor.MeasureLabelFit(sample, p.feedbackLabel, T(key).Replace("{0}", "6"), out Vector2 pref);
                            fit.Add(code + " footer " + key + " rect=" + p.feedbackLabel.rectTransform.rect.size + " preferred=" + pref + " fit=" + r);
                            if (r == 2) e.Add("FAIL footer overflow: " + code + " " + key);
                        }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        /// <summary>0 = 上限のまま入る / 1 = 下限までのどこかで入る / 2 = 下限でも入らない。幅は size で与える (保留中の幅を、シーンを変えずに測るため)。</summary>
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

        static Vector2 InContent(Vector2 panel) { return panel - new Vector2(0f, AreaMidY); }

        static void Place(Transform t, Vector2 pos, Vector2 size)
        {
            var r = (RectTransform)t;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = pos; r.sizeDelta = size; r.localScale = Vector3.one; r.localRotation = Quaternion.identity;
            EditorUtility.SetDirty(r);
        }

        static void Text(Transform t, Vector2 pos, Vector2 size, float max, float min, bool wrap, TextAlignmentOptions align)
        {
            Place(t, pos, size);
            var label = t.GetComponent<TextMeshProUGUI>();
            label.enableAutoSizing = true; label.fontSizeMax = max; label.fontSizeMin = min; label.fontSize = max;
            label.enableWordWrapping = wrap; label.overflowMode = TextOverflowModes.Ellipsis; label.alignment = align;
            label.richText = true; label.raycastTarget = false;
            EditorUtility.SetDirty(label);
        }

        static void Button(Transform t, Vector2 pos, Vector2 size)
        {
            Place(t, pos, size);
            t.gameObject.layer = t.parent.gameObject.layer;
            var label = t.Find("Label");
            Place(label, Vector2.zero, size - new Vector2(12f, 4f));
            var tmp = label.GetComponent<TextMeshProUGUI>();
            tmp.enableAutoSizing = true; tmp.fontSizeMax = ActionPt; tmp.fontSizeMin = ActionMin; tmp.fontSize = ActionPt;
            tmp.enableWordWrapping = false; tmp.alignment = TextAlignmentOptions.Center; tmp.raycastTarget = false;
            EditorUtility.SetDirty(tmp);
        }

        /// <summary>ボタンのラベルをボタンの枠に合わせて伸ばす (左右 12・上下 2 の余白)。幅が実行時に変わるボタン用。</summary>
        static void ButtonLabel(Transform t, float max, float min)
        {
            if (t == null) return;
            var r = (RectTransform)t;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(12f, 2f); r.offsetMax = new Vector2(-12f, -2f); r.localScale = Vector3.one;
            var tmp = t.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.enableAutoSizing = true; tmp.fontSizeMax = max; tmp.fontSizeMin = min; tmp.fontSize = max;
                tmp.enableWordWrapping = false; tmp.alignment = TextAlignmentOptions.Center; tmp.raycastTarget = false;
                EditorUtility.SetDirty(tmp);
            }
            EditorUtility.SetDirty(r);
        }

        static bool Stretched(Transform t)
        {
            if (t == null) return false;
            var r = (RectTransform)t;
            return r.anchorMin == Vector2.zero && r.anchorMax == Vector2.one;
        }

        static TextMeshProUGUI MakeText(Transform parent, string name, Vector2 pos, Vector2 size, float max, float min, bool wrap, TMP_FontAsset font, string text)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var t = go.GetComponent<TextMeshProUGUI>(); if (t == null) t = go.AddComponent<TextMeshProUGUI>();
            t.font = font; t.color = Color.white; t.text = text;
            Text(go.transform, pos, size, max, min, wrap, TextAlignmentOptions.MidlineLeft);
            return t;
        }

        static bool ActiveUnder(Transform t, Transform page)
        {
            for (var x = t; x != null && x != page; x = x.parent) if (!x.gameObject.activeSelf) return false;
            return true;
        }

        /// <summary>Page_Walk の座標 (板の中心が原点) での矩形。親の入れ子 (Scroll/Viewport/Content) を経ても正しい値になるよう四隅から作る。</summary>
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

        static void Req(List<string> e, bool ok, string what) { if (!ok) e.Add("FAIL " + what); }
    }
}
