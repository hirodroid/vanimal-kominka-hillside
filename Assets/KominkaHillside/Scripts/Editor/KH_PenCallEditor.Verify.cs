using System;
using System.Collections.Generic;
using System.Linq;
using KominkaDogRun;
using KominkaDogRun.Editor;
using QvPen.UdonScript;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
using Object = UnityEngine.Object;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-WLD-017 Pen/2 - Verify (読むだけ。シーンも資産も変えない)。Pen/1 は保存の前に同じ検査を走らせ、FAIL があれば保存しない。
    /// 結果: Temp/imp-wld-017/pen-verify.json と Console の 1 行 (PEN_VERIFY_OK / PEN_VERIFY_FAIL)。
    /// 設定ホーム全体の重なり・群の並びは Verify 22 (Menu Finish) も見る ([筆ペン] は置いたときだけ群「読む」に数える)。</summary>
    public static partial class KH_PenCallEditor
    {
        [Serializable] class VerifyReport
        {
            public string utc, scene, status;
            public int fails, warns;
            public List<string> lines = new List<string>();
        }

        [MenuItem(Menu + "2 - Verify")]
        public static void Verify()
        {
            var r = RunVerify(null);
            WriteJson(OutDir + "/pen-verify.json", r);
            string head = r.fails == 0 ? "PEN_VERIFY_OK" : "PEN_VERIFY_FAIL";
            string msg = "[KH Pen] " + head + " fails=" + r.fails + " warns=" + r.warns + " (" + OutDir + "/pen-verify.json)"
                + (r.fails > 0 ? " first: " + string.Join(" | ", r.lines.Where(l => l.StartsWith("FAIL")).Take(4)) : "");
            if (r.fails == 0) Debug.Log(msg); else Debug.LogError(msg);
        }

        /// <summary>baselineNull: Pen/1 が変更の前に控えた「GameObject を失った NetworkIDs の ID」。ここに入っている残骸は今回の作業のものではないので WARN に落とす (Pen/2 単独では null = 全部 FAIL)。</summary>
        static VerifyReport RunVerify(HashSet<int> baselineNull)
        {
            var r = new VerifyReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            void Check(bool ok, string what) { r.lines.Add((ok ? "ok   " : "FAIL ") + what); if (!ok) r.fails++; }
            void Warn(string what) { r.lines.Add("WARN " + what); r.warns++; }
            void Note(string what) { r.lines.Add("     " + what); }
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode only");
                if (EditorSceneManager.GetActiveScene().path != KH_CourseGroundEditor.ScenePath) throw new InvalidOperationException("open " + KH_CourseGroundEditor.ScenePath);
                VerifyBody(baselineNull, Check, Warn, Note);
            }
            catch (Exception e) { Check(false, "verify stopped: " + e.Message); }
            r.status = r.fails == 0 ? "PASS" : "FAIL";
            return r;
        }

        static void VerifyBody(HashSet<int> baselineNull, Action<bool, string> Check, Action<string> Warn, Action<string> Note)
        {
            var c = Resolve();
            foreach (var n in c.notes) Note(n);
            Check(c.missingKeys.Count == 0, "catalog " + KD_MenuFinishEditor.CatalogPath + " has every pen text (" + c.catalog.fallbackLanguage + ": " + string.Join(", ", NeededKeys) + ")"
                + (c.missingKeys.Count > 0 ? ": missing " + string.Join(", ", c.missingKeys) : ""));
            Check(AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath + ".asset") != null, "program asset " + ProgramPath + ".asset (Pen/0)");
            var p = c.pouch; var pU = c.pouchUdon;
            var home = p.settingsHome.transform;

            // ---------------------------------------------------------------- the rack (purchased instance; read only)
            var problems = new List<string>(); var warnings = new List<string>();
            var rack = ReadRack(c.scene, problems, warnings);
            foreach (var x in problems) Check(false, "rack: " + x);
            foreach (var x in warnings) Warn("rack: " + x);
            if (rack != null)
            {
                Check(true, "rack " + RackName + ": pensParent " + rack.pensParent.name + " -> PenManager (0)..(5), each serialized pen distinct, inside the rack, with VRCPickup / VRCObjectSync / kinematic Rigidbody");
                VerifyRackUntouched(rack, Warn, Note);
            }

            // ---------------------------------------------------------------- built or not
            var calls = Object.FindObjectsOfType<KH_PenCall>(true).Where(x => x.gameObject.scene == c.scene).ToArray();
            var page = p.settingsPage.transform.Find(PageName);
            var entry = home.Find(KD_MenuFinishEditor.PenCallButtonName);
            if (calls.Length == 0 && page == null && entry == null)
            {
                // Player/2 と同じく、作っていないシーンは合格にしない (Issue §5 の「Pen/2 で PASS」を空のシーンで満たさない)
                Check(false, "not built: no KH_PenCall, no Page_Settings/" + PageName + ", no " + KD_MenuFinishEditor.PenCallButtonName + " (run Pen/1)");
                bool clean = p.penPage == null && p.penPageBehaviour == null && !(p.extraSettingsPages ?? new GameObject[0]).Any(e => e != null && e.name == PageName);
                if (clean) Note("leftovers: none (pouch.penPage / penPageBehaviour null, no " + PageName + " in extraSettingsPages)");
                else Warn("leftovers after removal: pouch.penPage / penPageBehaviour or an extraSettingsPages entry named " + PageName + " still set");
                VerifyNetworkIds(baselineNull, null, Check, Warn, Note);
                return;
            }

            // ---------------------------------------------------------------- KH_PenCall (beside KH_ToyRecall, outside the menu)
            Check(calls.Length == 1, "exactly one KH_PenCall in the scene (" + calls.Length + ")");
            var pc = calls.Length == 1 ? calls[0] : null;
            var pcU = B(pc);
            var parentProblems = new List<string>();
            var parent = PenCallParent(c, parentProblems, out var toy);
            foreach (var x in parentProblems) Check(false, x);
            if (pc != null)
            {
                Check(pc.name == BehaviourName && OwnObject(pc.transform), BehaviourName + " on its own object (no children, only KH_PenCall and its backing)");
                Check(toy != null && pc.transform.parent == parent, BehaviourName + " is a sibling of KH_ToyRecall (parent " + (parent != null ? parent.name : "scene root") + "; actual " + (pc.transform.parent != null ? pc.transform.parent.name : "scene root") + ")");
                Check(!pc.transform.IsChildOf(p.panelRoot.transform) && !pc.transform.IsChildOf(p.transform), BehaviourName + " is outside the hand menu (keeps running while the page is hidden)");
                Check(pc.gameObject.activeInHierarchy && pc.enabled, BehaviourName + " active in the hierarchy and enabled");
                Check(pcU != null && pcU.SyncMethod == Networking.SyncType.None, BehaviourName + " backing present, sync None" + (pcU != null ? " (actual " + pcU.SyncMethod + ")" : ""));
            }

            // ---------------------------------------------------------------- the page
            Check(page != null && page.parent == p.settingsPage.transform && !page.gameObject.activeSelf, "Page_Settings/" + PageName + " exists and is hidden in the scene");
            Check(entry != null, "settings home " + KD_MenuFinishEditor.PenCallButtonName + " exists (Pen/1)");
            if (page == null || pc == null || pcU == null || rack == null) { VerifyNetworkIds(baselineNull, pc, Check, Warn, Note); return; }
            Button Bt(string name) { var t = page.Find(name); return t != null ? t.GetComponent<Button>() : null; }
            TextMeshProUGUI Lb(string name) { var t = page.Find(name); return t != null ? LabelOf(t.gameObject) : null; }
            var state = page.Find("State") != null ? page.Find("State").GetComponent<TextMeshProUGUI>() : null;

            // ---------------------------------------------------------------- KH_PenCall fields (proxy) and the backing heap
            Check(pc.penManagers != null && pc.penManagers.Length == PenCount && Enumerable.Range(0, PenCount).All(i => pc.penManagers[i] == rack.managers[i]),
                "KH_PenCall.penManagers = PenManager (0)..(5) in order" + (pc.penManagers == null ? " (null)" : " (" + string.Join(", ", pc.penManagers.Select(m => m != null ? m.name : "null")) + ")"));
            Check(pc.pens != null && pc.pens.Length == PenCount && Enumerable.Range(0, PenCount).All(i => pc.pens[i] == rack.pens[i]) && pc.pens.Distinct().Count() == PenCount,
                "KH_PenCall.pens = each manager's serialized pen, in order, distinct" + (pc.pens == null ? " (null)" : ""));
            Check(pc.pens != null && pc.pens.All(x => x != null && x.transform.IsChildOf(rack.root.transform)) && pc.penManagers != null && pc.penManagers.All(x => x != null && x.transform.IsChildOf(rack.root.transform)),
                "all 6 managers and pens are inside " + RackName);
            var fields = new (string name, Object got, Object want)[]
            {
                ("language", pc.language, c.language), ("page", pc.page, page.gameObject), ("stateLabel", pc.stateLabel, state), ("backLabel", pc.backLabel, Lb("Back")),
                ("entryLabel", pc.entryLabel, entry != null ? LabelOf(entry.gameObject) : null),
            };
            foreach (var f in fields) Check(f.got != null && f.want != null && f.got == f.want, "KH_PenCall." + f.name + " = " + (f.want != null ? f.want.name : "(expected object missing)") + (f.got == null ? " (null)" : f.got != f.want ? " (actual " + f.got.name + ")" : ""));
            Check(pc.colorButtons != null && pc.colorButtons.Length == PenCount && Enumerable.Range(0, PenCount).All(i => pc.colorButtons[i] != null && pc.colorButtons[i] == Bt(ColorName(i))),
                "KH_PenCall.colorButtons = PenColor0..5");
            Check(pc.colorLabels != null && pc.colorLabels.Length == PenCount && Enumerable.Range(0, PenCount).All(i => pc.colorLabels[i] != null && pc.colorLabels[i] == Lb(ColorName(i))),
                "KH_PenCall.colorLabels = PenColor0..5/Label");
            HeapArray(pcU, "penManagers", rack.managers.Select(m => (Object)B(m)).ToArray(), rack.managers.Cast<Object>().ToArray(), Check, Warn);
            HeapArray(pcU, "pens", rack.pens.Select(x => (Object)B(x)).ToArray(), rack.pens.Cast<Object>().ToArray(), Check, Warn);
            HeapArray(pcU, "colorButtons", Enumerable.Range(0, PenCount).Select(i => (Object)Bt(ColorName(i))).ToArray(), null, Check, Warn);
            HeapArray(pcU, "colorLabels", Enumerable.Range(0, PenCount).Select(i => (Object)Lb(ColorName(i))).ToArray(), null, Check, Warn);   // Codex 独立レビュー (低): 実行時の「使用中」表示の配線
            Heap(pcU, "language", B(c.language), Check, Warn); Heap(pcU, "page", page.gameObject, Check, Warn);
            Heap(pcU, "stateLabel", state, Check, Warn); Heap(pcU, "backLabel", Lb("Back"), Check, Warn); Heap(pcU, "entryLabel", entry != null ? LabelOf(entry.gameObject) : null, Check, Warn);

            // ---------------------------------------------------------------- events (persistent, static string -> SendCustomEvent)
            for (int i = 0; i < PenCount; i++) CheckEvent(page.Find(ColorName(i)), "m_OnClick", pcU, "_Call" + i, typeof(KH_PenCall), Check);
            CheckEvent(page.Find("Back"), "m_OnClick", pU, "_PenBack", typeof(KD_TreatPouch), Check);
            CheckEvent(entry, "m_OnClick", pU, "_PenOpen", typeof(KD_TreatPouch), Check);
            foreach (var evt in new[] { "_PageOpened", "_RefreshLanguage" }) Check(typeof(KH_PenCall).GetMethod(evt) != null, "KH_PenCall." + evt + " exists (the pouch sends it)");

            // ---------------------------------------------------------------- colours (button = gradient.Evaluate(0), readable label)
            for (int i = 0; i < PenCount; i++)
            {
                var t = page.Find(ColorName(i));
                var img = t != null ? t.GetComponent<Image>() : null;
                var btn = t != null ? t.GetComponent<Button>() : null;
                var lbl = t != null ? LabelOf(t.gameObject) : null;
                var want = rack.inks[i];
                bool same = img != null && Mathf.Abs(img.color.r - want.r) <= 0.01f && Mathf.Abs(img.color.g - want.g) <= 0.01f && Mathf.Abs(img.color.b - want.b) <= 0.01f && Mathf.Abs(img.color.a - 1f) <= 0.01f;
                Check(same, ColorName(i) + " colour = " + rack.managers[i].name + ".colorGradient.Evaluate(0) #" + ColorUtility.ToHtmlStringRGBA(want) + (img != null && !same ? " (actual #" + ColorUtility.ToHtmlStringRGBA(img.color) + ")" : img == null ? " (no Image)" : ""));
                // ApplyButtonFeedback の normalColor は白 (Image の色がそのまま見える)
                Check(btn != null && btn.targetGraphic == img && btn.transition == Selectable.Transition.ColorTint && btn.colors.normalColor == Color.white, ColorName(i) + ": the Image is the target graphic, normal tint white (the ink shows as is)");
                var wantInk = LabelColorFor(want);
                Check(lbl != null && Vector4.Distance(lbl.color, wantInk) < 0.01f, ColorName(i) + " label colour " + (Luminance(want) > 0.5f ? "dark" : "white") + " on luminance " + Luminance(want).ToString("F2"));
            }
            var inks = rack.inks.Select(k => ColorUtility.ToHtmlStringRGB(k)).ToArray();
            if (inks.Distinct().Count() != PenCount) Warn("two or more pens share the same first ink colour: " + string.Join(", ", inks));

            // ---------------------------------------------------------------- page layout (band Y 148..-232, no overlaps among the 8 controls)
            VerifyPageLayout(page, Check);
            Check(page.gameObject.layer == p.settingsPage.layer && page.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == page.gameObject.layer),
                "page and all its parts on the settings page's layer " + p.settingsPage.layer + " (" + LayerMask.LayerToName(p.settingsPage.layer) + ")");

            // ---------------------------------------------------------------- settings home [筆ペン]
            VerifyEntry(c, entry, Check);

            // ---------------------------------------------------------------- pouch
            int count = (p.extraSettingsPages ?? new GameObject[0]).Count(e => e == page.gameObject);
            Check(count == 1, "page registered in extraSettingsPages once (proxy; " + count + ")");
            if (pU.publicVariables.TryGetVariableValue("extraSettingsPages", out object pagesObj))
            {
                int bc = pagesObj is GameObject[] arr ? arr.Count(e => e == page.gameObject) : -1;
                Check(bc == 1, "page registered in extraSettingsPages once (backing; " + bc + ")");
            }
            else Warn("pouch backing has no extraSettingsPages yet (heap populated on save / reload)");
            Check(p.penPage == page.gameObject && p.penPageBehaviour == pc, "pouch.penPage = the page, pouch.penPageBehaviour = KH_PenCall");
            Heap(pU, "penPage", page.gameObject, Check, Warn); Heap(pU, "penPageBehaviour", pcU, Check, Warn);
            Check(typeof(KD_TreatPouch).GetMethod("_PenOpen") != null && typeof(KD_TreatPouch).GetMethod("_PenBack") != null, "KD_TreatPouch._PenOpen / _PenBack exist");

            // ---------------------------------------------------------------- language tables, glyphs, text fit
            foreach (var key in NeededKeys)
            {
                string want = TextOrNull(c.catalog, c.catalog.fallbackLanguage, key);
                if (want == null) continue;   // reported above
                string got = c.language.keys != null && Array.IndexOf(c.language.keys, key) >= 0 ? c.language._TextForLanguage(key, c.catalog.fallbackLanguage) : null;
                Check(got == want, "KD_Language has the current text of " + key + (got == null ? " (missing: run Pen/1 or Menu/11)" : got != want ? " (stale: '" + got + "')" : ""));
            }
            string missingGlyphs = KD_ShopSetupEditor.MissingGlyphs(BundledGlyphs(c));
            Check(missingGlyphs.Length == 0, "shared font has every glyph of the pen texts incl. the heading " + HeadingKey + " (bundled languages)" + (missingGlyphs.Length > 0 ? ": missing " + missingGlyphs + " (run Pen/1)" : ""));
            VerifyTextFit(c, page, entry, Check, Warn, Note);

            VerifyNetworkIds(baselineNull, pc, Check, Warn, Note);
            Note("not checked here (device): calling a pen in VR / Desktop, the 20 s return to the rack, 'in use' for a pen another player holds, late joiners, Quest load (IMP-WLD-017 §5)");
        }

        // ---------------------------------------------------------------- the rack is the purchased instance: report anything beyond name / transform overrides

        static void VerifyRackUntouched(Rack rack, Action<string> Warn, Action<string> Note)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(rack.root)) { Warn(RackName + " is not a prefab instance (unpacked?)"); return; }
            var mods = PrefabUtility.GetPropertyModifications(rack.root) ?? new PropertyModification[0];
            // UdonSharp writes its own overrides on every prefab instance (serializationData.* on the proxies, _syncMethod on the backings): not edits, left out of the count
            bool Own(PropertyModification m) { return m.propertyPath.StartsWith("serializationData") || m.propertyPath == "_syncMethod"; }
            int usharp = mods.Count(m => m != null && Own(m));
            var other = mods.Where(m => m != null && !Own(m) && !(m.target is Transform) && m.propertyPath != "m_Name" && m.propertyPath != "m_IsActive")
                .Select(m => (m.target != null ? m.target.GetType().Name : "?") + "." + m.propertyPath).Distinct().ToArray();
            var added = PrefabUtility.GetAddedComponents(rack.root).Count + PrefabUtility.GetAddedGameObjects(rack.root).Count;
            // a Note, not a WARN (2026-10-16): Pen/1 never writes into the rack; these lines only say what the scene instance carries
            if (other.Length > 0 || added > 0) Note(RackName + ": scene overrides beyond name / transform / UdonSharp's own (" + other.Length + " properties" + (other.Length > 0 ? ": " + string.Join(", ", other.Take(6)) : "")
                + ", " + added + " added components / objects). Pen/1 never writes there");
            else Note(RackName + ": scene overrides are name / transform (" + (mods.Length - usharp) + ") plus UdonSharp's own serialization (" + usharp + "); Pen/1 reads it and writes nothing");
        }

        // ---------------------------------------------------------------- page layout

        static void VerifyPageLayout(Transform page, Action<bool, string> Check)
        {
            var rects = new List<(string, Rect)>();
            Rect panel = new Rect(-KD_TreatSetupEditor.PanelWidth * 0.5f, -KD_TreatSetupEditor.PanelHeight * 0.5f, KD_TreatSetupEditor.PanelWidth, KD_TreatSetupEditor.PanelHeight);
            foreach (var spec in PageLayout())
            {
                var t = page.Find(spec.name) as RectTransform;
                Check(t != null && Vector2.Distance(t.anchoredPosition, spec.pos) < 0.5f && Vector2.Distance(t.sizeDelta, spec.size) < 0.5f,
                    "page " + spec.name + " " + spec.size + " at " + spec.pos + (t == null ? " (missing)" : " (actual " + t.sizeDelta + " at " + t.anchoredPosition + ")"));
                if (t == null) continue;
                var rc = RectOf(t);
                rects.Add((spec.name, rc));
                bool top = spec.name == "State" || spec.name == "Back";
                if (top) Check(rc.yMin >= BandTop - 0.01f && Inside(rc, panel), "page " + spec.name + " above the body band (y " + rc.yMin + ".." + rc.yMax + ") and inside the panel");
                else Check(rc.yMax <= BandTop + 0.01f && rc.yMin >= BandBottom - 0.01f && Inside(rc, panel), "page " + spec.name + " inside the body band Y " + BandTop + ".." + BandBottom + " (" + rc.yMax + ".." + rc.yMin + ") and the panel");
            }
            var hits = Overlaps(rects);
            Check(rects.Count == 2 + PenCount && hits.Count == 0, "page controls overlap each other nowhere (" + rects.Count + " of " + (2 + PenCount) + ")" + (hits.Count > 0 ? ": " + string.Join(", ", hits) : ""));
            var extra = new List<string>();
            foreach (Transform child in page) if (!PageLayout().Any(s => s.name == child.name)) extra.Add(child.name);
            Check(extra.Count == 0, "no unexpected child on the page" + (extra.Count > 0 ? ": " + string.Join(", ", extra) : ""));
        }

        // ---------------------------------------------------------------- settings home [筆ペン]

        static void VerifyEntry(Ctx c, Transform entry, Action<bool, string> Check)
        {
            if (entry == null) return;
            var home = c.pouch.settingsHome.transform;
            var rt = (RectTransform)entry;
            Check(entry.parent == home && entry.gameObject.activeSelf && Vector2.Distance(rt.anchoredPosition, KD_MenuFinishEditor.HillsidePenCallPos) < 0.5f && Vector2.Distance(rt.sizeDelta, KD_MenuFinishEditor.HillsidePenCallSize) < 0.5f,
                KD_MenuFinishEditor.PenCallButtonName + " on the settings home, shown, " + KD_MenuFinishEditor.HillsidePenCallSize + " at " + KD_MenuFinishEditor.HillsidePenCallPos + " (actual " + rt.sizeDelta + " at " + rt.anchoredPosition + ")");
            Check(rt.anchoredPosition.y < KD_MenuFinishEditor.HillsideGroupReadY && rt.anchoredPosition.y > KD_MenuFinishEditor.HomeBottom,
                KD_MenuFinishEditor.PenCallButtonName + " inside the group read/write (y " + rt.anchoredPosition.y + " between " + KD_MenuFinishEditor.HillsideGroupReadY + " and " + KD_MenuFinishEditor.HomeBottom + ")");
            var rc = RectOf(rt);
            float half = KD_TreatSetupEditor.PanelWidth * 0.5f;
            Check(rc.yMax <= KD_MenuFinishEditor.HomeTop && rc.yMin >= KD_MenuFinishEditor.HomeBottom - 1f && rc.xMin >= -half && rc.xMax <= half,
                KD_MenuFinishEditor.PenCallButtonName + " inside the home bounds (" + rc.xMin + ".." + rc.xMax + ", " + rc.yMin + ".." + rc.yMax + ")");
            var hits = new List<string>();
            foreach (Transform child in home)
            {
                if (child == entry || !child.gameObject.activeSelf) continue;
                if (child.GetComponent<Button>() == null && child.GetComponent<Slider>() == null) continue;
                if (RectOf((RectTransform)child).Overlaps(rc)) hits.Add(child.name);
            }
            Check(hits.Count == 0, KD_MenuFinishEditor.PenCallButtonName + " overlaps no settings-home Button / Slider" + (hits.Count > 0 ? ": " + string.Join(", ", hits) : ""));
            var img = entry.GetComponent<Image>();
            Check(img != null && entry.GetComponent<Button>() != null && entry.GetComponent<Button>().targetGraphic == img, KD_MenuFinishEditor.PenCallButtonName + " is a Button with its Image as the target");

            // ---- the read row (読む・書く): 4 x 260 at the constants (KD_MenuFinishEditor.HillsideReadRow), no overlap among them, inside the panel
            var row = new List<(string, Rect)>();
            foreach (var e in KD_MenuFinishEditor.HillsideReadRow)
            {
                var t = home.Find(e.name) as RectTransform;
                if (t == null) { Check(e.optional, "read row " + e.name + (e.optional ? " not placed (optional; its seat stays empty)" : " missing (Menu/11)")); continue; }
                var label = t.Find("Label") as RectTransform;
                Check(Vector2.Distance(t.anchoredPosition, e.pos) < 0.5f && Vector2.Distance(t.sizeDelta, KD_MenuFinishEditor.HillsideReadEntrySize) < 0.5f
                    && label != null && Vector2.Distance(label.sizeDelta, KD_MenuFinishEditor.HillsideReadEntrySize - new Vector2(24f, 4f)) < 0.5f,
                    "read row " + e.name + " " + KD_MenuFinishEditor.HillsideReadEntrySize + " at " + e.pos + ", Label " + (KD_MenuFinishEditor.HillsideReadEntrySize - new Vector2(24f, 4f))
                    + " (actual " + t.sizeDelta + " at " + t.anchoredPosition + (label != null ? ", Label " + label.sizeDelta : ", no Label") + ")");
                var er = RectOf(t);
                Check(er.xMin >= -half && er.xMax <= half, "read row " + e.name + " inside the panel (" + er.xMin + ".." + er.xMax + ")");
                row.Add((e.name, er));
            }
            var rowHits = Overlaps(row);
            Check(rowHits.Count == 0, "read row: no two of " + string.Join(" / ", row.Select(x => x.Item1)) + " overlap" + (rowHits.Count > 0 ? ": " + string.Join(", ", rowHits) : ""));
        }

        // ---------------------------------------------------------------- text fit (vrc-world-ui §10; bundled languages only)

        static void VerifyTextFit(Ctx c, Transform page, Transform entry, Action<bool, string> Check, Action<string> Warn, Action<string> Note)
        {
            TextMeshProUGUI Lb(string name) { var t = page.Find(name); return t != null ? LabelOf(t.gameObject) : null; }
            if (entry != null) MeasureOne(c, LabelOf(entry.gameObject), code => TextOrNull(c.catalog, code, OpenKey), "settings [筆ペン] " + OpenKey + " (" + KD_MenuFinishEditor.HillsidePenCallSize + ")", Check);
            // the other read-row labels at 260 wide (2026-10-16: the row went from 340 / 340 / 190 to 4 x 260). WalkIntro's title is walkIntro.title in the shared catalog
            // (KH_WalkIntroEditor.T(catalog, "title") = KD_LocalizationEditor.Text(c, fallback, "walkIntro." + key))
            foreach (var e in new[] { (KD_MenuFinishEditor.CreditButtonName, "ui.extra.022"), ("InformationButton", "information.title"), (KD_MenuFinishEditor.WalkIntroButtonName, "walkIntro.title") })
            {
                var t = c.pouch.settingsHome.transform.Find(e.Item1);
                if (t == null) { if (e.Item1 != KD_MenuFinishEditor.WalkIntroButtonName) Check(false, "read row " + e.Item1 + " missing: label not measured"); else Note("read row " + e.Item1 + " not placed: label not measured"); continue; }
                string key = e.Item2;
                if (TextOrNull(c.catalog, c.catalog.fallbackLanguage, key) == null) { Note("read row " + e.Item1 + ": " + key + " not in the catalog, not measured"); continue; }
                MeasureOne(c, LabelOf(t.gameObject), code => TextOrNull(c.catalog, code, key), "read row " + e.Item1 + " " + key + " (" + KD_MenuFinishEditor.HillsideReadEntrySize + ")", Check);
            }
            for (int i = 0; i < PenCount; i++)
            {
                var label = Lb(ColorName(i));
                Check(label != null && label.enableAutoSizing && Mathf.Approximately(label.fontSizeMax, ColorFontMax) && Mathf.Approximately(label.fontSizeMin, KD_LocalizationEditor.ButtonFontSizeMin) && label.font == c.font,
                    ColorName(i) + " label: the menu font, auto-size " + ColorFontMax + " -> " + KD_LocalizationEditor.ButtonFontSizeMin + "pt");
            }
            // the six labels share one size: measure the first with both words it shows
            MeasureOne(c, Lb(ColorName(0)), code => TextOrNull(c.catalog, code, OpenKey), "colour button " + OpenKey + " (" + ColorSize + ")", Check);
            MeasureOne(c, Lb(ColorName(0)), code => TextOrNull(c.catalog, code, InUseKey), "colour button " + InUseKey + " (" + ColorSize + ")", Check);
            MeasureOne(c, Lb("Back"), code => TextOrNull(c.catalog, code, BackKey), "[back] " + BackKey, Check);
            var state = page.Find("State") != null ? page.Find("State").GetComponent<TextMeshProUGUI>() : null;
            foreach (var key in StateKeys) { string k = key; MeasureOne(c, state, code => TextOrNull(c.catalog, code, k), "state " + k, Check); }
            // the changed heading 読む・書く (bound label GroupRead, 1080 x 30, 20pt, one line)
            var heading = c.pouch.settingsHome.transform.Find("GroupRead");
            var headingLabel = heading != null ? heading.GetComponent<TextMeshProUGUI>() : null;
            if (headingLabel == null) Check(false, "settings heading GroupRead exists (Menu/22)");
            else MeasureOne(c, headingLabel, code => TextOrNull(c.catalog, code, HeadingKey), "heading GroupRead " + HeadingKey, Check);
            // the page title (_OpenExtraSettings(penPage, pen.call.open)) in the section title box at its current width, 18pt (KD_HandMenuScrollEditor.TitleFits)
            var title = c.pouch.sectionTitleLabel;
            var over = new List<string>(); int measured = 0;
            var go = new GameObject("PenTitleMeasure", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var sample = go.AddComponent<TextMeshProUGUI>();
                foreach (var lang in c.catalog.languages)
                {
                    if (!KD_LocalizationEditor.Bundled(lang)) continue;
                    var text = TextOrNull(c.catalog, lang.code, OpenKey);
                    if (text == null) continue;
                    measured++;
                    if (!KD_HandMenuScrollEditor.TitleFits(sample, title, text, out Vector2 pref)) over.Add(lang.code + " " + pref.x.ToString("F0"));
                }
            }
            finally { Object.DestroyImmediate(go); }
            Check(over.Count == 0 && measured > 0, "page title " + OpenKey + " fits the section title " + title.rectTransform.sizeDelta.x + " wide at " + KD_HandMenuScrollEditor.TitleFontMin + "pt (" + measured + " bundled languages)"
                + (over.Count > 0 ? ": " + string.Join(", ", over) : ""));
            var client = c.catalog.languages.Where(l => !KD_LocalizationEditor.Bundled(l)).Select(l => l.code).ToArray();
            var untranslated = client.Where(code => { var t = TextOrNull(c.catalog, code, OpenKey); return string.IsNullOrWhiteSpace(t); }).ToArray();
            if (untranslated.Length > 0) Warn(OpenKey + " has no text in " + string.Join(", ", untranslated) + " (falls back to " + c.catalog.fallbackLanguage + ")");
            Note("text fit is measured for the bundled languages (glyphs=self: " + string.Join(", ", c.catalog.languages.Where(KD_LocalizationEditor.Bundled).Select(l => l.code))
                + ") only; the " + client.Length + " client languages (" + string.Join(", ", client) + ") are drawn by VRChat's fallback font (device check)");
        }

        static void MeasureOne(Ctx c, TMP_Text target, Func<string, string> textFor, string what, Action<bool, string> Check)
        {
            if (target == null) { Check(false, what + ": label missing"); return; }
            var go = new GameObject("PenMeasure", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var sample = go.AddComponent<TextMeshProUGUI>();
                var bad = new List<string>(); int shrink = 0, measured = 0;
                foreach (var lang in c.catalog.languages)
                {
                    if (!KD_LocalizationEditor.Bundled(lang)) continue;
                    string text = textFor(lang.code);
                    if (text == null) continue;
                    measured++;
                    int fit = KD_LocalizationEditor.MeasureLabelFit(sample, target, text, out Vector2 pref);
                    if (fit == 2) bad.Add(lang.code + " " + pref.x.ToString("F0") + "x" + pref.y.ToString("F0") + " in " + target.rectTransform.rect.size);
                    if (fit == 1) shrink++;
                }
                Check(bad.Count == 0 && measured > 0, what + " fits (" + measured + " bundled languages)" + (bad.Count > 0 ? ": " + string.Join(", ", bad) : "") + (shrink > 0 ? " (" + shrink + " shrink)" : "") + (measured == 0 ? ": no text" : ""));
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---------------------------------------------------------------- NetworkIDs (vrc-scene-edit-safety)

        static void VerifyNetworkIds(HashSet<int> baselineNull, KH_PenCall pc, Action<bool, string> Check, Action<string> Warn, Action<string> Note)
        {
            var d = Descriptor();
            if (d == null || d.NetworkIDCollection == null) { Warn("no VRCSceneDescriptor / NetworkIDs to check"); return; }
            var dead = d.NetworkIDCollection.Where(x => x != null && x.gameObject == null).ToList();
            var fresh = dead.Where(x => baselineNull == null || !baselineNull.Contains(x.ID)).ToList();
            var old = dead.Where(x => baselineNull != null && baselineNull.Contains(x.ID)).ToList();
            Check(fresh.Count == 0, "NetworkIDs: no entry without its GameObject" + (baselineNull != null ? " created by this run" : "")
                + (fresh.Count > 0 ? ": " + string.Join(", ", fresh.Take(8).Select(x => x.ID + (x.SerializedTypeNames != null && x.SerializedTypeNames.Count > 0 ? "(" + string.Join("/", x.SerializedTypeNames) + ")" : "(untyped)"))) : ""));
            if (old.Count > 0) Warn("NetworkIDs: " + old.Count + " entries had no GameObject before this run (not this builder's; left alone): " + string.Join(", ", old.Take(8).Select(x => x.ID)));
            if (pc != null)
            {
                int ours = d.NetworkIDCollection.Count(x => x != null && x.gameObject == pc.gameObject);
                Check(ours <= 1, "NetworkIDs: at most one entry for " + BehaviourName + " (" + ours + "; the SDK assigns it at the latest at build)");
            }
            Note("NetworkIDs: " + d.NetworkIDCollection.Count + " entries");
        }

        // ---------------------------------------------------------------- helpers

        static UdonBehaviour B(UdonSharpBehaviour b) { return b != null ? UdonSharpEditorUtility.GetBackingUdonBehaviour(b) : null; }

        /// <summary>backing heap の値 (実行時に読まれる側)。無いときは WARN (保存・再読込の前は heap が空のことがある。vrc-udonsharp §4)。</summary>
        static void Heap(UdonBehaviour u, string field, object want, Action<bool, string> Check, Action<string> Warn)
        {
            if (u == null) { Check(false, "backing missing for " + field); return; }
            if (!u.publicVariables.TryGetVariableValue(field, out object got)) { Warn("backing heap of " + (u.programSource != null ? u.programSource.name : "?") + " has no " + field + " yet (populated on save / reload)"); return; }
            bool ok = Equals(got, want) || (got is Object go && want is Object wo && go == wo) || (got == null && want is Object nw && nw == null);
            Check(ok, "backing " + (u.programSource != null ? u.programSource.name : "?") + "." + field + " = " + Describe(want) + (ok ? "" : " (actual " + Describe(got) + ")"));
        }

        /// <summary>backing heap の参照配列 (U# 参照は相手の backing。proxy でも一致とみなす)。長さと全要素を見る。heap に無ければ WARN。</summary>
        static void HeapArray(UdonBehaviour u, string field, Object[] want, Object[] alt, Action<bool, string> Check, Action<string> Warn)
        {
            if (u == null) { Check(false, "backing missing for " + field); return; }
            if (!u.publicVariables.TryGetVariableValue(field, out object got)) { Warn("backing heap of " + (u.programSource != null ? u.programSource.name : "?") + " has no " + field + " yet (populated on save / reload)"); return; }
            var arr = got as Array;
            bool ok = arr != null && arr.Length == want.Length;
            var bad = new List<int>();
            if (ok)
                for (int i = 0; i < want.Length; i++)
                {
                    var g = arr.GetValue(i) as Object;
                    bool hit = (g != null && want[i] != null && g == want[i]) || (alt != null && g != null && alt[i] != null && g == alt[i]);
                    if (!hit) bad.Add(i);
                }
            Check(ok && bad.Count == 0, "backing " + (u.programSource != null ? u.programSource.name : "?") + "." + field + " holds the " + want.Length + " references in order"
                + (arr == null ? " (not an array: " + Describe(got) + ")" : arr.Length != want.Length ? " (length " + arr.Length + ")" : bad.Count > 0 ? " (wrong at " + string.Join(",", bad) + ")" : ""));
        }

        static string Describe(object o) { return o == null ? "null" : o is Object uo ? (uo == null ? "null" : uo.name) : o.ToString(); }

        /// <summary>UnityEvent (m_OnClick) が 1 本だけ、target.SendCustomEvent(固定の文字列 evt) で、その口が型に実在する。</summary>
        static void CheckEvent(Transform t, string eventProperty, UdonBehaviour target, string evt, Type owner, Action<bool, string> Check)
        {
            string what = (t != null ? t.name : "?") + "." + eventProperty + " -> " + (owner != null ? owner.Name : "?") + "." + evt;
            if (t == null || target == null) { Check(false, what + " (object or target missing)"); return; }
            Component host = null;
            foreach (var comp in t.GetComponents<Component>())
            {
                if (comp == null) continue;
                using (var probe = new SerializedObject(comp)) if (probe.FindProperty(eventProperty + ".m_PersistentCalls.m_Calls") != null) { host = comp; break; }
            }
            if (host == null) { Check(false, what + " (no component with " + eventProperty + ")"); return; }
            using (var so = new SerializedObject(host))
            {
                var calls = so.FindProperty(eventProperty + ".m_PersistentCalls.m_Calls");
                bool ok = calls.arraySize == 1;
                if (ok)
                {
                    var call = calls.GetArrayElementAtIndex(0);
                    ok = call.FindPropertyRelative("m_Target").objectReferenceValue == target
                        && call.FindPropertyRelative("m_MethodName").stringValue == "SendCustomEvent"
                        && call.FindPropertyRelative("m_Mode").intValue == (int)UnityEngine.Events.PersistentListenerMode.String
                        && call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == evt;
                }
                bool exists = owner == null || owner.GetMethod(evt) != null;
                Check(ok && exists, what + " (1 call, static string)" + (exists ? "" : " — " + evt + " not found on " + owner.Name) + (calls.arraySize != 1 ? " (" + calls.arraySize + " calls)" : ""));
            }
        }

        static Rect RectOf(RectTransform t)
        {
            var size = t.sizeDelta; var pos = t.anchoredPosition;
            return new Rect(pos.x - size.x * t.pivot.x, pos.y - size.y * t.pivot.y, size.x, size.y);
        }

        /// <summary>r が outer の内側か (端が等しくてもよい。0.5 px の許容)。</summary>
        static bool Inside(Rect r, Rect outer)
        {
            return r.xMin >= outer.xMin - 0.5f && r.xMax <= outer.xMax + 0.5f && r.yMin >= outer.yMin - 0.5f && r.yMax <= outer.yMax + 0.5f;
        }

        static List<string> Overlaps(List<(string, Rect)> rects)
        {
            var hits = new List<string>();
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                    if (rects[i].Item2.Overlaps(rects[j].Item2)) hits.Add(rects[i].Item1 + " x " + rects[j].Item1);
            return hits;
        }
    }
}
