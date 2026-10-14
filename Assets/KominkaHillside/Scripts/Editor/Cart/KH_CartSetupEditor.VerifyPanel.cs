using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>DES-UI-007 の Verify (Cart/2 の一部、Validate の末尾の 1 行から): 乗り場 5 + 残ったカート 8 の共通パネルの合流の案内 (HTML B 案)。
    /// 13 枚すべて: 板の寸法と持ち上げ、文字・行の置き場 (表と一致、板の内側、互いに重ならない)、配線 (proxy / backing)、固定の文言の並びとキー、
    /// 撮影カメラの案内の参照と Android / iOS で隠す分岐、空きカートの 30 秒の遅延イベントと読み取り API。
    /// Station_0 と Cart_0/Panel: 13 言語の文言が枠に収まるか (ja / en = 共通フォントで TMP 実測 (KD_LocalizationEditor.MeasureLabelFit)、
    /// 他 11 言語 = VRChat の内蔵フォントで描くので字幅の目安で推定)。シーンは変えない (測定は HideAndDontSave の見本で行う)。
    /// 証明しないもの: 実寸の見え方・レイ操作・Android の非表示の実挙動 (コンパイル時の分岐)・初見の複数人の操作・client 言語の実際の字形と改行。</summary>
    public static partial class KH_CartSetupEditor
    {
        const string PanelSourcePath = ProgramDir + "/KH_CartStationPanel.cs", ControlsSourcePath = ProgramDir + "/KH_CartControls.cs", ManagerSourcePath = ProgramDir + "/KH_CartManager.cs";
        static readonly string[] PanelDestKeys = { "KD_TreatPouch.WordWalkGate", "KD_TreatPouch.WordWalkPlaza", "course.walk.forward", "course.walk.reverse", "course.walk.upper", "course.walk.river" };   // KH_CartManager._destKeys と同じ
        const float PanelInset = 10f, PanelMinBottomAboveGround = 0.3f, PanelTopPend = 2.1f;

        static void PanelGuideCheck(V v, KH_CartStationPanel[] panels, KD_Language language, TMP_FontAsset sharedFont)
        {
            // ---- Runtime のソース (Android / iOS で隠す分岐・空きカートの遅延イベント・読み取り API) ----
            string src = File.Exists(PanelSourcePath) ? File.ReadAllText(PanelSourcePath) : "";
            string ctl = File.Exists(ControlsSourcePath) ? File.ReadAllText(ControlsSourcePath) : "";
            string mgr = File.Exists(ManagerSourcePath) ? File.ReadAllText(ManagerSourcePath) : "";
            int hide = src.IndexOf("#if UNITY_ANDROID || UNITY_IOS", StringComparison.Ordinal);
            v.Req(hide >= 0 && src.IndexOf("cameraTip.SetActive(false)", hide, StringComparison.Ordinal) > hide && ctl.Contains("#if UNITY_ANDROID || UNITY_IOS"),
                "DES-UI-007 panel hides cameraTip under #if UNITY_ANDROID || UNITY_IOS (same branch as KH_CartControls' camera monitor)");
            v.Req(src.Contains("SendCustomEventDelayedSeconds(nameof(_AvailabilityTick), AvailabilitySeconds)") && src.Contains("AvailabilitySeconds = 30f") && src.Contains("_availArmed"),
                "DES-UI-007 availability re-check = one delayed event every 30 s (armed flag, no Update)");
            v.Req(!src.Contains("void Update(") && !src.Contains("void LateUpdate("), "DES-UI-007 panel has no per-frame method");
            v.Req(src.Contains("manager._CartAvailable(spot, cart)") && mgr.Contains("public bool _CartAvailable(int spot, int cart)"), "DES-UI-007 availability via read-only KH_CartManager._CartAvailable(spot, cart)");

            // ---- 文言: 板が使うキーが契約 (CartKeys) にあり、13 言語で {n} の数が ja と同じ ----
            var catalog = CourseCatalog();
            var used = GuideTexts.Select(x => x.key).Concat(new[] { "cart.board.avail.yes", "cart.board.avail.no", "cart.board.join.count", "cart.board.join.suffix", "cart.board.join.action",
                "cart.board.row.dest", "cart.board.row.closedNote", "cart.board.row.endingNote", "cart.board.start.dest", "cart.board.start.button",
                KH_CartStationPanel.KeyCheckingStart, KH_CartStationPanel.KeyCheckingJoin,
                "cart.board.result.started", "cart.board.result.joined", "cart.board.result.full", "cart.board.result.closed", "cart.board.result.unavailable" }).Distinct().ToArray();
            v.Req(used.All(CartKeys.Contains), "DES-UI-007 every board key is in CartKeys (" + used.Length + " keys)");
            var ja = catalog.languages.First(l => l.code == "ja");
            foreach (var key in used)
            {
                var jaT = ja.texts.FirstOrDefault(t => t.key == key);
                int want = jaT != null ? Placeholders(jaT.text) : -1;
                var bad = catalog.languages.Where(l => { var t = l.texts.FirstOrDefault(x => x.key == key); return t == null || string.IsNullOrWhiteSpace(t.text) || Placeholders(t.text) != want; }).Select(l => l.code).ToArray();
                v.Req(want >= 0 && bad.Length == 0, "DES-UI-007 " + key + " in 13 languages with " + want + " placeholder(s)" + (bad.Length > 0 ? " (bad " + string.Join(",", bad) + ")" : ""));
            }

            // ---- 13 枚 ----
            GameObject sampleGo = null;
            try
            {
                for (int i = 0; i < panels.Length; i++)
                {
                    var panel = panels[i];
                    string n = i < StationCount ? "Station_" + i : "Cart_" + (i - StationCount) + "/" + CartPanelName;
                    if (panel == null) { v.Req(false, "DES-UI-007 " + n + " panel exists"); continue; }
                    PanelOneCheck(v, panel, n, sharedFont);
                    if (i == 0 || i == StationCount)
                    {
                        if (sampleGo == null) { sampleGo = new GameObject("PanelMeasure", typeof(RectTransform)); sampleGo.hideFlags = HideFlags.HideAndDontSave; sampleGo.AddComponent<TextMeshProUGUI>(); }
                        PanelFitCheck(v, panel, n, language, catalog, sampleGo.GetComponent<TextMeshProUGUI>(), i >= StationCount);
                    }
                }
            }
            finally { if (sampleGo != null) UnityEngine.Object.DestroyImmediate(sampleGo); }
        }

        /// <summary>行の要素 (Join i・Join i/…・JoinMeta i・JoinState i・JoinNote i)。固定の JoinTitle / JoinCount / JoinNote は含めない。</summary>
        static bool IsRowElement(string name) { return Regex.IsMatch(name, "^" + JoinPrefix + "(Meta|State|Note)?[0-9]+(/|$)"); }
        /// <summary>行の要素のうち 1〜7 番 (0 番と同じ寸法なので測定を省く)。</summary>
        static bool IsRepeatedRow(string name) { var m = Regex.Match(name, "^" + JoinPrefix + "(Meta|State|Note)?([0-9]+)(/|$)"); return m.Success && m.Groups[2].Value != "0"; }

        static int Placeholders(string s) { int n = 0; for (int k = 0; k < 10; k++) if (s.Contains("{" + k + "}")) n++; return n; }

        static void PanelOneCheck(V v, KH_CartStationPanel panel, string n, TMP_FontAsset sharedFont)
        {
            string tag = "DES-UI-007 " + n;
            var board = panel.transform.Find(BoardName);
            if (board == null) { v.Req(false, tag + " Board"); return; }
            var brt = (RectTransform)board;
            v.Req((brt.sizeDelta - BoardSize).sqrMagnitude < 1e-4f && Mathf.Abs(brt.localPosition.y - BoardLift) < 1e-4f && Mathf.Abs(brt.localPosition.x) < 1e-4f,
                tag + " board " + BoardSize.x + "x" + BoardSize.y + " lifted " + BoardLift.ToString("F3") + " m (bottom stays " + BoardBottomBelowPivot + " m below the pivot)");
            // 高さ: 下端が肘掛け (基点の PanelAboveArmrest 下) より上、地面から十分上。上端は数値で残す (乗り場だけ。カートは Parking にいる)
            Vector3 bottom = board.TransformPoint(new Vector3(0f, -BoardSize.y * .5f, 0f)), top = board.TransformPoint(new Vector3(0f, BoardSize.y * .5f, 0f));
            Vector3 armrest = panel.transform.position - panel.transform.up * PanelAboveArmrest;
            v.Req(bottom.y >= armrest.y + 0.03f, tag + " board bottom " + (bottom.y - armrest.y).ToString("F2") + " m above the armrest anchor (>= 0.03)");
            if (n.StartsWith("Station_"))
            {
                bool hit = Physics.Raycast(bottom + Vector3.up * 0.5f, Vector3.down, out RaycastHit h, 10f, KH_LeashManager.TravelGroundMask, QueryTriggerInteraction.Ignore);
                if (!hit) v.Pend(tag + " no ground under the board (height not measured)");
                else
                {
                    float b = bottom.y - h.point.y, t = top.y - h.point.y;
                    v.Req(b >= PanelMinBottomAboveGround, tag + " board bottom " + b.ToString("F2") + " m above ground (>= " + PanelMinBottomAboveGround + ")");
                    v.r.passItems.Add(tag + " info: board spans " + b.ToString("F2") + ".." + t.ToString("F2") + " m above ground (pivot " + (panel.transform.position.y - h.point.y).ToString("F2") + " m)");
                    if (t > PanelTopPend) v.Pend(tag + " board top " + t.ToString("F2") + " m above ground (> " + PanelTopPend + "; check readability in Play)");
                }
            }

            var pb = UdonSharpEditorUtility.GetBackingUdonBehaviour(panel);
            var so = new SerializedObject(panel);
            Transform B(string path) { return board.Find(path); }
            var rects = new List<KeyValuePair<string, Rect>>();
            TextMeshProUGUI Lbl(PanelText spec, bool track)
            {
                string path = string.IsNullOrEmpty(spec.parent) ? spec.name : spec.parent + "/" + spec.name;
                var t = Comp<TextMeshProUGUI>(B(path));
                bool ok = t != null && (t.rectTransform.anchoredPosition - spec.Pos).sqrMagnitude < 0.25f && (t.rectTransform.sizeDelta - spec.Size).sqrMagnitude < 0.25f
                    && !t.raycastTarget && t.enableWordWrapping == spec.wrap && t.alignment == spec.align && t.enableAutoSizing == (spec.min > 0f) && t.font == sharedFont;
                v.Req(ok, tag + " " + path + " at (" + spec.Pos.x + "," + spec.Pos.y + ") " + spec.w + "x" + spec.h + ", shared font, not a raycast target");
                if (t != null && track) rects.Add(new KeyValuePair<string, Rect>(path, RectOf(t.rectTransform)));
                return t;
            }
            // 固定の文言
            var guide = GuideTexts.Select(spec => Lbl(spec, spec.parent != EmptyName)).ToArray();
            ArrayCheck(v, so, pb, "guideLabels", guide, tag);
            var keysProp = so.FindProperty("guideKeys");
            bool keysOk = keysProp != null && keysProp.isArray && keysProp.arraySize == GuideTexts.Length;
            for (int k = 0; keysOk && k < GuideTexts.Length; k++) keysOk = keysProp.GetArrayElementAtIndex(k).stringValue == GuideTexts[k].key;
            v.Req(keysOk, tag + ".guideKeys [" + GuideTexts.Length + "] = GuideTexts keys (proxy)");
            v.Req(pb != null && pb.publicVariables.TryGetVariableValue("guideKeys", out object gk) && gk is string[] gks && gks.SequenceEqual(GuideTexts.Select(x => x.key)), tag + ".guideKeys (backing)");
            // 状態で変わる文字と既存の文字
            Lbl(TitleText, true); Lbl(NoticeText, true); Lbl(PrText, true);
            RefCheck(v, so, pb, "availLabel", Lbl(AvailText, true), tag);
            RefCheck(v, so, pb, "joinCountLabel", Lbl(CountText, true), tag);
            RefCheck(v, so, pb, "startDestLabel", Lbl(DestText, true), tag);
            v.Req(so.FindProperty("resultLabel") != null && so.FindProperty("resultLabel").objectReferenceValue == Lbl(ResultText, true), tag + " resultLabel = Result (start result under the start button)");
            // 入れ物 (Graphic なし) と参照
            var empty = B(EmptyName); var camera = B(CameraName);
            v.Req(empty != null && empty.GetComponent<Graphic>() == null && camera != null && camera.GetComponent<Graphic>() == null, tag + " Empty / Camera groups have no Graphic");
            RefCheck(v, so, pb, "emptyState", empty != null ? empty.gameObject : null, tag);
            RefCheck(v, so, pb, "cameraTip", camera != null ? camera.gameObject : null, tag);
            // 出発ボタン・写真・手順の番号
            var start = B(StartName);
            v.Req(start != null && (((RectTransform)start).anchoredPosition - StartButtonPos).sqrMagnitude < 0.25f && (((RectTransform)start).sizeDelta - StartButtonSize).sqrMagnitude < 0.25f,
                tag + " " + StartName + " fixed at (" + StartButtonPos.x + "," + StartButtonPos.y + ") " + StartButtonSize.x + "x" + StartButtonSize.y + " (does not move with the number of groups)");
            if (start != null) rects.Add(new KeyValuePair<string, Rect>(StartName, RectOf((RectTransform)start)));
            var photo = B(StationPhotoName);
            if (photo != null) rects.Add(new KeyValuePair<string, Rect>(StationPhotoName, RectOf((RectTransform)photo)));
            for (int k = 0; k < StepNumPos.Length; k++) { var sn = B(StepNumPrefix + (k + 1)); v.Req(sn != null, tag + " " + StepNumPrefix + (k + 1)); if (sn != null) rects.Add(new KeyValuePair<string, Rect>(sn.name, RectOf((RectTransform)sn))); }
            for (int k = 0; k < RuleTops.Length; k++) { var rule = Comp<Image>(B(RulePrefix + k)); v.Req(rule != null && !rule.raycastTarget, tag + " " + RulePrefix + k + " divider (not a raycast target)"); }
            // 合流の行: ボタン (2 列 × 4 段) と、ボタンの外 (Board 直下) の 目的地・人数 / 状態 / 理由か結果
            var metas = new TextMeshProUGUI[MaxGroups]; var states = new TextMeshProUGUI[MaxGroups]; var notes = new TextMeshProUGUI[MaxGroups];
            for (int i = 0; i < MaxGroups; i++)
            {
                var row = B(JoinPrefix + i);
                v.Req(row != null && (((RectTransform)row).anchoredPosition - RowButtonPos(i)).sqrMagnitude < 0.25f && (((RectTransform)row).sizeDelta - JoinRowSize).sqrMagnitude < 0.25f,
                    tag + " " + JoinPrefix + i + " at row " + (i / 2) + " column " + (i % 2));
                if (row != null) rects.Add(new KeyValuePair<string, Rect>(row.name, RectOf((RectTransform)row)));
                var nl = Comp<TextMeshProUGUI>(row != null ? row.Find("Label") : null);
                v.Req(nl != null && nl.alignment == TextAlignmentOptions.Right, tag + " " + JoinPrefix + i + " name label right-aligned (next to the suffix)");
                metas[i] = Lbl(RowMeta(i), true); states[i] = Lbl(RowState(i), true); notes[i] = Lbl(RowNote(i), true);
            }
            ArrayCheck(v, so, pb, "joinMetaLabels", metas, tag);
            ArrayCheck(v, so, pb, "joinStateLabels", states, tag);
            ArrayCheck(v, so, pb, "joinNoteLabels", notes, tag);
            // 置き場: 板の内側、互いに重ならない。0 件の案内は行の領域の中で、行以外とは重ならない
            var inner = new Rect(-BoardSize.x * .5f + PanelInset, -BoardSize.y * .5f + PanelInset, BoardSize.x - 2f * PanelInset, BoardSize.y - 2f * PanelInset);
            var outside = rects.Where(r => !Inside(inner, r.Value)).Select(r => r.Key).ToArray();
            v.Req(outside.Length == 0, tag + " all " + rects.Count + " elements inside the board (inset " + PanelInset + ")" + (outside.Length > 0 ? " (outside: " + string.Join(", ", outside) + ")" : ""));
            var overlaps = new List<string>();
            for (int a = 0; a < rects.Count; a++) for (int b = a + 1; b < rects.Count; b++) if (Overlap(rects[a].Value, rects[b].Value)) overlaps.Add(rects[a].Key + " x " + rects[b].Key);
            v.Req(overlaps.Count == 0, tag + " no overlapping elements" + (overlaps.Count > 0 ? " (" + string.Join(", ", overlaps.Take(10)) + ")" : ""));
            float rowsTop = BoardSize.y * .5f - FirstRowTop, rowsBottom = BoardSize.y * .5f - (RowTop(MaxGroups - 1) + RowNoteOffset + RowNoteHeight);
            var rowArea = new Rect(-BoardSize.x * .5f, rowsBottom, BoardSize.x, rowsTop - rowsBottom);
            foreach (var spec in GuideTexts.Where(x => x.parent == EmptyName))
            {
                var r = new Rect(spec.Pos - spec.Size * .5f, spec.Size);
                bool clash = rects.Any(o => !IsRowElement(o.Key) && Overlap(o.Value, r));
                v.Req(Inside(rowArea, r) && !clash, tag + " " + EmptyName + "/" + spec.name + " inside the join rows area and clear of the rest");
            }
        }

        static Rect RectOf(RectTransform t) { return new Rect(t.anchoredPosition - t.sizeDelta * .5f, t.sizeDelta); }
        static bool Inside(Rect outer, Rect r) { return r.xMin >= outer.xMin - 0.5f && r.xMax <= outer.xMax + 0.5f && r.yMin >= outer.yMin - 0.5f && r.yMax <= outer.yMax + 0.5f; }
        static bool Overlap(Rect a, Rect b) { return a.xMin < b.xMax - 0.5f && b.xMin < a.xMax - 0.5f && a.yMin < b.yMax - 0.5f && b.yMin < a.yMax - 0.5f; }

        /// <summary>13 言語の文言が枠に収まるか。self 言語 (ja / en、共通フォントに焼いてある) は TMP で実測、client 言語は字幅の目安で推定
        /// (CJK・ハングル・全角 = 1 em、キリル 0.62、大文字 0.66、M/W 0.8、空白 0.3、他 0.55。折返しは幅の 90 % で行数を数え、行の高さはフォントの lineHeight)。
        /// オートサイズは最大 pt → 最小 pt の順に見て、最小でも入らなければ NG。</summary>
        static void PanelFitCheck(V v, KH_CartStationPanel panel, string n, KD_Language language, KD_LocalizationEditor.Catalog catalog, TextMeshProUGUI sample, bool onCart)
        {
            string tag = "DES-UI-007 " + n + " fit";
            var board = panel.transform.Find(BoardName);
            if (board == null) return;
            TextMeshProUGUI L(string path) { return Comp<TextMeshProUGUI>(board.Find(path)); }
            var checks = new List<KeyValuePair<TextMeshProUGUI, string[]>>();   // ラベルと、そこに出る文言のキー (先頭が "=" なら組み立て済みの合成キー)
            void Add(TextMeshProUGUI label, params string[] keys) { if (label != null) checks.Add(new KeyValuePair<TextMeshProUGUI, string[]>(label, keys)); else v.Req(false, tag + " label missing for " + string.Join(",", keys)); }
            foreach (var spec in GuideTexts) Add(L(string.IsNullOrEmpty(spec.parent) ? spec.name : spec.parent + "/" + spec.name), spec.key);
            Add(L(TitleText.name), "cart.title");
            Add(L(NoticeText.name), onCart ? "cart.parked" : "cart.notice");
            Add(L(PrText.name), "cart.station.pr");
            Add(L(AvailText.name), "cart.board.avail.yes", "cart.board.avail.no");
            Add(L(CountText.name), "=count");
            Add(L(DestText.name), "=startdest");
            Add(L(StartName + "/Label"), "cart.board.start.button");
            Add(L(ResultText.name), KH_CartStationPanel.KeyCheckingStart, "cart.board.result.started", "cart.board.result.full", "cart.result.here", "cart.result.busy", "cart.result.closed",
                "cart.board.result.joined", "cart.board.result.unavailable");   // 合流の行が消えたときは合流の結果もここ
            for (int i = 0; i < MaxGroups; i++)
            {
                Add(L(JoinPrefix + i + "/" + JoinInfoName), "=info");
                Add(L(JoinMetaPrefix + i), "=rowdest");
                Add(L(JoinStatePrefix + i), "cart.accepting.on", "cart.row.closed", "cart.row.ending");
                Add(L(JoinNotePrefix + i), "cart.board.row.closedNote", "cart.board.row.endingNote", KH_CartStationPanel.KeyCheckingJoin, "cart.board.result.joined", "cart.board.result.full",
                    "cart.board.result.closed", "cart.board.result.unavailable", "cart.result.busy", "cart.result.here");
            }
            int measured = 0, estimated = 0, autosized = 0;
            var ng = new List<string>(); var ngEstimate = new List<string>();
            foreach (var lang in catalog.languages)
            {
                string code = lang.code;
                bool self = lang.glyphs == KD_LocalizationEditor.GlyphsSelf;
                string Tx(string key) { var s = language._TextForLanguage(key, code); return string.IsNullOrEmpty(s) ? KD_LocalizationEditor.Text(catalog, code, key) ?? key : s; }
                string longestDest = PanelDestKeys.Select(Tx).OrderByDescending(s => EstimateWidth(s, 1f)).First();
                foreach (var c in checks)
                {
                    // 合流の行と結果は 8 枠とも同じ寸法なので 0 番だけ測る
                    string name = c.Key.transform.parent == board ? c.Key.name : c.Key.transform.parent.name + "/" + c.Key.name;
                    if (IsRepeatedRow(name)) continue;   // 固定の JoinTitle / JoinCount / JoinNote は必ず測る (Codex 2026-10-14)
                    foreach (var key in c.Value)
                    {
                        string text = key == "=count" ? Tx("cart.board.join.count").Replace("{0}", "8").Replace("{1}", "8")
                            : key == "=startdest" ? Tx("cart.board.start.dest").Replace("{0}", longestDest)
                            : key == "=rowdest" ? Tx("cart.board.row.dest").Replace("{0}", longestDest).Replace("{1}", "8")
                            : key == "=info" ? Tx("cart.board.join.suffix") + "\n" + Tx("cart.board.join.action")
                            : Tx(key);
                        int fit;
                        if (self) { fit = KD_LocalizationEditor.MeasureLabelFit(sample, c.Key, text, out Vector2 _); measured++; }
                        else { fit = EstimateFit(c.Key, text); estimated++; }
                        if (fit == 1) autosized++;
                        if (fit == 2) (self ? ng : ngEstimate).Add(code + " " + name + " " + key);
                    }
                }
            }
            // ja / en は実測なので合否。client 11 言語は字幅の目安による概算で、合格とは書かない (超過の見込みは PENDING、無ければ「概算で超過なし」の info)
            v.Req(ng.Count == 0, tag + ": ja/en fit by TMP measurement (" + measured + " texts, " + autosized + " incl. client fit only after auto-size)"
                + (ng.Count > 0 ? " (overflow: " + string.Join("; ", ng.Take(20)) + ")" : ""));
            if (ngEstimate.Count > 0) v.Pend(tag + ": client languages (approximate estimate, not measured) may overflow: " + string.Join("; ", ngEstimate.Take(20)));
            else v.r.passItems.Add(tag + " info: client languages approximate estimate, no overflow expected (" + estimated + " texts; NOT a fit PASS - check in Play)");

            // 表示名 (2 行。Runtime の _TwoLineName と同じ分け方): 1 行目は幅に収まり、2 行が高さに収まる。2 行目の末尾は省略記号でよい
            var nl = Comp<TextMeshProUGUI>(board.Find(JoinPrefix + "0/Label"));
            v.Req(nl != null && Mathf.Approximately(nl.fontSize, JoinNamePoints) && (nl.rectTransform.sizeDelta - JoinNameSize).sqrMagnitude < 0.25f
                && KH_CartStationPanel.NameLineEm * JoinNamePoints <= JoinNameSize.x * 0.95f,
                tag + " name label " + JoinNamePoints + " pt " + JoinNameSize.x + "x" + JoinNameSize.y + ", line budget " + KH_CartStationPanel.NameLineEm + " em <= 95 % of the width");
            if (nl != null)
            {
                var badNames = new List<string>();
                foreach (var sampleName in new[] { "Kominka_Hillside_Friend_とても長い表示名_01", "WWWWWWWWWWWWWWWWWWWWWWWWWWWW", "あいうえおかきくけこさしすせそたちつてとなにぬねの", "mmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmm", "Hana" })
                {
                    string split = panel._TwoLineName(sampleName);
                    string first = split.Split('\n')[0];
                    int w = KD_LocalizationEditor.MeasureLabelFit(sample, nl, first, out Vector2 _);
                    int h = KD_LocalizationEditor.MeasureLabelFit(sample, nl, first + "\n" + first, out Vector2 _);
                    if (w == 2 || h == 2) badNames.Add(sampleName + (w == 2 ? " (line 1 too wide)" : " (2 lines too tall)"));
                }
                v.Req(badNames.Count == 0, tag + " display name: line 1 fits the width and 2 lines fit the height (5 samples)" + (badNames.Count > 0 ? " (" + string.Join("; ", badNames) + ")" : ""));
            }
        }

        static float EstimateWidth(string s, float size)
        {
            float w = 0f;
            foreach (char c in s)
            {
                int u = c;
                float em = (u >= 0x3000 && u <= 0x9FFF) || (u >= 0xAC00 && u <= 0xD7AF) || (u >= 0xFF00 && u <= 0xFFEF) || (u >= 0x1100 && u <= 0x11FF) ? 1f
                    : c == ' ' ? 0.3f : (u >= 0x0400 && u <= 0x04FF) ? 0.62f : "MWmw".IndexOf(c) >= 0 ? 0.8f : (c >= 'A' && c <= 'Z') ? 0.66f : 0.55f;
                w += em * size;
            }
            return w;
        }

        /// <summary>0 = 最大 pt で入る、1 = オートサイズで入る、2 = 入らない (MeasureLabelFit と同じ値)。</summary>
        static int EstimateFit(TextMeshProUGUI target, string text)
        {
            var size = target.rectTransform.rect.size;
            float lineRatio = target.font != null && target.font.faceInfo.pointSize > 0f ? target.font.faceInfo.lineHeight / target.font.faceInfo.pointSize : 1.3f;
            if (lineRatio <= 0f) lineRatio = 1.3f;
            var sizes = target.enableAutoSizing ? new[] { target.fontSizeMax, target.fontSizeMin } : new[] { target.fontSize };
            for (int k = 0; k < sizes.Length; k++)
            {
                int lines = 0; float maxW = 0f;
                foreach (var part in text.Split('\n'))
                {
                    float w = EstimateWidth(part, sizes[k]);
                    maxW = Mathf.Max(maxW, w);
                    lines += target.enableWordWrapping ? Mathf.Max(1, Mathf.CeilToInt(w / (size.x * 0.9f))) : 1;
                }
                bool ok = (target.enableWordWrapping || maxW <= size.x + 2f) && lines * sizes[k] * lineRatio <= size.y + 2f;
                if (ok) return k == 0 ? 0 : 1;
            }
            return 2;
        }
    }
}
