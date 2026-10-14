using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-WLD-008 W5 (2026-10-08): the video camera's and the filming permission's texts in 13 languages. docs/plans/2026-10-03-video-camera.md
    /// §7.3 "2026-10-08 W5 実装メモ".
    ///   - The texts live in UITranslations.json (video.* / film.*, every language; AI-written, no native proofreading). The code keeps the
    ///     Japanese defaults (Word* / Ja* constants) only for a missing table; W5Keys pairs every key with its default and Video/2 checks they agree.
    ///   - Video/1 refreshes KD_Language's tables from the catalog (KD_LocalizationEditor.Configure, the same refresh as Photo/1; bindings untouched),
    ///     bakes the glyphs of every glyphs=self language (ja / en) into the shared font, wires the static labels (WireTexts: textLabels /
    ///     textKeys, rewritten at runtime by KH_VideoCamera.TextsRefresh), the spot keys and KD_TreatPouch.photoVideo. Video/4 rewires the
    ///     labels it adds (the person list's [とじる], the confirmation's [依頼する] [やめる]).
    ///   - Video/2 (VerifyLanguage): 13 languages × every key, the placeholder count against ja, ja = the code default, no stray video.* / film.*
    ///     key, the scene's language table, the glyphs, the static label wiring, the spot keys, the photo page hook, and (WARN only) the English
    ///     label fit. Client-glyph languages (ru, ko, zh …) are drawn by VRChat's own fallback font and are checked on a device.
    /// </summary>
    public static partial class KH_VideoCameraEditor
    {
        static KD_LocalizationEditor.Catalog _w5Catalog;

        /// <summary>UITranslations.json (validated by KD_LocalizationEditor.Load). Loaded once per menu run (Resolve resets it).</summary>
        static KD_LocalizationEditor.Catalog Cat() { if (_w5Catalog == null) _w5Catalog = KD_LocalizationEditor.Load(); return _w5Catalog; }

        /// <summary>The Japanese text of a key: what the builders write into the scene (the runtime rewrites it in the player's language).</summary>
        public static string Ja(string key) { return KD_LocalizationEditor.Text(Cat(), "ja", key); }

        /// <summary>Every W5 key with its Japanese default in the code (106 keys).</summary>
        public static List<KeyValuePair<string, string>> W5Keys()
        {
            var l = new List<KeyValuePair<string, string>>();
            void A(string k, string ja) { l.Add(new KeyValuePair<string, string>(k, ja)); }
            A(KH_VideoCamera.KeyTitle, KH_VideoCamera.WordTitle); A(KH_VideoCamera.KeyEntry, KH_VideoCamera.WordEntry);   // IMP-UI-009: the entry carries "(PCのみ)"
            A(KH_VideoCamera.KeyRecall, KH_VideoCamera.WordRecall); A(KH_VideoCamera.KeyPutAway, KH_VideoCamera.WordPutAway);
            for (int m = 0; m < KH_VideoCamera.ModeCountAll; m++) A(KH_VideoCamera.ModeKey(m), KH_VideoCamera.ModeWord(m));
            for (int k = 0; k < KH_VideoCamera.PathMoveCount; k++) A(KH_VideoCamera.PathMoveKey(k), KH_VideoCamera.PathMoveWord(k));
            for (int k = 0; k < KH_VideoCamera.OrbitKindCount; k++) A(KH_VideoCamera.OrbitKindKey(k), KH_VideoCamera.OrbitKindWord(k));
            A(KH_VideoCamera.KeySnap, KH_VideoCamera.WordSnap); A(KH_VideoCamera.KeyOn, KH_VideoCamera.WordOn); A(KH_VideoCamera.KeyOff, KH_VideoCamera.WordOff);
            A(KH_VideoCamera.KeyFilming, KH_VideoCamera.WordFilming); A(KH_VideoCamera.KeyLost, KH_VideoCamera.WordLost); A(KH_VideoCamera.KeyNoSpot, KH_VideoCamera.WordNoSpot);
            A(KH_VideoCamera.KeyFade, KH_VideoCamera.WordFade); A(KH_VideoCamera.KeyRtFailed, KH_VideoCamera.WordRtFailed); A(KH_VideoCamera.KeySnapNote, KH_VideoCamera.WordSnapNote);
            A(KH_VideoCamera.KeyPhotoCamOn, KH_VideoCamera.WordPhotoCamOn); A(KH_VideoCamera.KeyPhotoCamOff, KH_VideoCamera.WordPhotoCamOff);
            A(KH_VideoCamera.KeySpotWash, SpotWordWash + "{0}"); A(KH_VideoCamera.KeySpotRiver, SpotWordRiver); A(KH_VideoCamera.KeySpotRiverLow, SpotWordRiverLow);
            string[] ck = KH_VideoCamera.CtlKeys(), cw = KH_VideoCamera.CtlWords();
            for (int i = 0; i < ck.Length && i < cw.Length; i++) if (ck[i].Length > 0) A(ck[i], cw[i]);
            A(KeyHideMonitor, WordHideMonitor);   // DES-WLD-004 M1.1: [モニターを隠す]
            A(KH_VideoCamera.KeyDrone, KH_VideoCamera.WordDrone); A(KH_VideoCamera.KeyShow, KH_VideoCamera.WordShow); A(KH_VideoCamera.KeyHide, KH_VideoCamera.WordHide);
            // the person picker / list / confirmation (KH_VideoCamera.People.cs)
            A(KH_VideoCamera.KeyPerson, KH_VideoCamera.WordPerson); A(KH_VideoCamera.KeyPersonTitle, KH_VideoCamera.WordPersonTitle); A(KH_VideoCamera.KeyPersonClose, KH_VideoCamera.WordPersonClose);
            A(KH_VideoCamera.KeyAskConfirm, KH_VideoCamera.WordAskConfirm); A(KH_VideoCamera.KeyAskNote, KH_VideoCamera.WordAskNote); A(KH_VideoCamera.KeyAskYes, KH_VideoCamera.WordAskYes);
            A(KH_VideoCamera.KeyAskNo, KH_VideoCamera.WordAskNo); A(KH_VideoCamera.KeyCancel, KH_VideoCamera.WordCancel); A(KH_VideoCamera.KeyStopPerson, KH_VideoCamera.WordStopPerson);
            A(KH_VideoCamera.KeyWaitPermit, KH_VideoCamera.WordWaitPermit); A(KH_VideoCamera.KeyChecking, KH_VideoCamera.WordChecking); A(KH_VideoCamera.KeyWaiting, KH_VideoCamera.WordWaiting);
            A(KH_VideoCamera.KeyFilmingPerson, KH_VideoCamera.WordFilmingPerson); A(KH_VideoCamera.KeyNoPermit, KH_VideoCamera.WordNoPermit); A(KH_VideoCamera.KeyDeclined, KH_VideoCamera.WordDeclined);
            A(KH_VideoCamera.KeyPaused, KH_VideoCamera.WordPaused); A(KH_VideoCamera.KeyBusy, KH_VideoCamera.WordBusy); A(KH_VideoCamera.KeyNotAccepting, KH_VideoCamera.WordNotAccepting);
            A(KH_VideoCamera.KeyNotReady, KH_VideoCamera.WordNotReady); A(KH_VideoCamera.KeyAgain, KH_VideoCamera.WordAgain); A(KH_VideoCamera.KeyBusyMe, KH_VideoCamera.WordBusyMe);
            A(KH_VideoCamera.KeyAnswerFirst, KH_VideoCamera.WordAnswerFirst); A(KH_VideoCamera.KeyLeft, KH_VideoCamera.WordLeft); A(KH_VideoCamera.KeyRevoked, KH_VideoCamera.WordRevoked);
            A(KH_VideoCamera.KeyNoTable, KH_VideoCamera.WordNoTable); A(KH_VideoCamera.KeyCrossYield, KH_VideoCamera.WordCrossYield);
            // the pattern board (§17)
            A(KH_VideoCamera.KeyPatternTitle, KH_VideoCamera.WordPatternTitle); A(KH_VideoCamera.KeyPatternStop, KH_VideoCamera.WordPatternStop);
            A(KH_VideoCamera.KeyPatternIdle, KH_VideoCamera.WordPatternIdle); A(KH_VideoCamera.KeyPatternRetry, KH_VideoCamera.WordPatternRetry);
            for (int s = 0; s < KH_VideoCamera.SceneCount; s++) A(KH_VideoCamera.SceneKey(s), KH_VideoCamera.SceneWord(s));
            for (int p = 0; p < KH_VideoCamera.PatternCount; p++) A(KH_VideoCamera.PatternNameKey(p), KH_VideoCamera.PatternName(p));
            for (int p = 0; p < KH_VideoCamera.PatternCount; p++) A(KH_VideoCamera.PatternNoteKey(p), KH_VideoCamera.PatternNote(p));
            // the subject's side: the notice board and the hand menu (all clients)
            A(KH_TravelNotice.KeyFilmTitle, KH_TravelNotice.WordFilmTitle); A(KH_TravelNotice.KeyFilmPlace, KH_TravelNotice.WordFilmPlace);
            A(KH_TravelNotice.KeyFilmGranted, KH_TravelNotice.WordFilmGranted); A(KH_TravelNotice.KeyFilmGrantedNote, KH_TravelNotice.WordFilmGrantedNote);
            A(KD_TreatPouch.KeyFilmAcceptOn, KD_TreatPouch.WordFilmAcceptOn); A(KD_TreatPouch.KeyFilmAcceptOff, KD_TreatPouch.WordFilmAcceptOff);
            A(KD_TreatPouch.KeyFilmPending, KD_TreatPouch.JaFilmPending); A(KD_TreatPouch.KeyFilmGrantsButton, KD_TreatPouch.JaFilmGrantsButton);
            A(KD_TreatPouch.KeyFilmGrantsTitle, KD_TreatPouch.WordFilmGrantsTitle); A(KD_TreatPouch.KeyFilmGrantsNote, KD_TreatPouch.WordFilmGrantsNote);
            A(KD_TreatPouch.KeyFilmRowActive, KD_TreatPouch.WordFilmRowActive); A(KD_TreatPouch.KeyFilmRowWaiting, KD_TreatPouch.WordFilmRowWaiting);
            A(KD_TreatPouch.KeyFilmStop, KD_TreatPouch.WordFilmStop); A(KD_TreatPouch.KeyFilmNone, KD_TreatPouch.WordFilmNone);
            return l;
        }

        /// <summary>The keys whose labels also carry a player's name (drawn with the display-name font, KH_DisplayName SDF + its Latin fallback).</summary>
        public static readonly string[] NameFontKeys =
        {
            KH_VideoCamera.KeyPerson, "video.mode.self", KH_VideoCamera.KeyAskConfirm, KH_VideoCamera.KeyAskNote, KH_VideoCamera.KeyWaitPermit, KH_VideoCamera.KeyChecking,
            KH_VideoCamera.KeyFilmingPerson, KH_VideoCamera.KeyNoPermit, KH_VideoCamera.KeyDeclined, KH_VideoCamera.KeyPaused, KH_VideoCamera.KeyBusy, KH_VideoCamera.KeyNotAccepting,
            KH_VideoCamera.KeyNotReady, KH_VideoCamera.KeyAgain, KH_VideoCamera.KeyBusyMe, KH_VideoCamera.KeyAnswerFirst, KH_VideoCamera.KeyLeft, KH_VideoCamera.KeyRevoked,
            KH_VideoCamera.KeyNoTable, KH_VideoCamera.KeyCrossYield, KH_VideoCamera.KeyWaiting, KH_VideoCamera.KeyFilming,
            KH_TravelNotice.KeyFilmTitle, KH_TravelNotice.KeyFilmGranted, KD_TreatPouch.KeyFilmRowActive, KD_TreatPouch.KeyFilmRowWaiting,
        };

        static IEnumerable<string> W5KeyNames() { return W5Keys().Select(kv => kv.Key); }

        /// <summary>Every displayed character of the given keys in the glyphs=self languages (ja / en): the shared font must hold them.</summary>
        public static string BundledW5Text(IEnumerable<string> keys = null)
        {
            var cat = Cat(); var sb = new StringBuilder();
            var list = (keys ?? W5KeyNames()).ToArray();
            foreach (var lang in cat.languages)
                if (KD_LocalizationEditor.Bundled(lang))
                    foreach (var k in list) sb.Append(Regex.Replace(KD_LocalizationEditor.Text(cat, lang.code, k), "<[^<>]+>", "").Replace("{0}", "").Replace("{1}", ""));
            return sb.ToString();
        }

        /// <summary>The problems of the catalog for the W5 keys (empty = fine): a key missing / empty in a language, a placeholder count that differs
        /// from ja, ja that differs from the code default, a video.* / film.* key the code does not use.</summary>
        public static List<string> W5CatalogProblems()
        {
            var cat = Cat(); var bad = new List<string>();
            var pairs = W5Keys();
            var keys = new HashSet<string>(pairs.Select(p => p.Key));
            if (keys.Count != pairs.Count) bad.Add("duplicate keys in W5Keys");
            int Count(string s, string ph) { return Regex.Matches(s ?? "", Regex.Escape(ph)).Count; }
            var ja = cat.languages.First(l => l.code == "ja");
            foreach (var kv in pairs)
            {
                var jt = ja.texts.FirstOrDefault(t => t.key == kv.Key);
                if (jt == null || string.IsNullOrWhiteSpace(jt.text)) { bad.Add("ja:" + kv.Key + " missing"); continue; }
                if (jt.text != kv.Value) bad.Add("ja:" + kv.Key + " '" + jt.text + "' differs from the code default '" + kv.Value + "'");
                foreach (var lang in cat.languages)
                {
                    var e = lang.texts.FirstOrDefault(t => t.key == kv.Key);
                    if (e == null || string.IsNullOrWhiteSpace(e.text)) { bad.Add(lang.code + ":" + kv.Key + " missing"); continue; }
                    foreach (var ph in new[] { "{0}", "{1}" })
                        if (Count(e.text, ph) != Count(jt.text, ph)) bad.Add(lang.code + ":" + kv.Key + " has " + Count(e.text, ph) + " × " + ph + " (ja " + Count(jt.text, ph) + ")");
                }
            }
            foreach (var t in ja.texts)
                if ((t.key.StartsWith("video.") || t.key.StartsWith("film.")) && !keys.Contains(t.key)) bad.Add("ja:" + t.key + " is in the catalog but not used by the code (W5Keys)");
            return bad;
        }

        /// <summary>Video/1 and Video/4 refuse to run (nothing changed) when the catalog lacks a W5 text.</summary>
        static void W5Preflight()
        {
            _w5Catalog = null;
            var bad = W5CatalogProblems();
            if (bad.Count > 0) throw new InvalidOperationException("UITranslations.json: " + bad.Count + " W5 problems (first: " + bad[0] + "); nothing changed");
        }

        /// <summary>The static labels the runtime rewrites (label, key, what). Optional parts (Video/4's overlays) are skipped when absent.</summary>
        static List<(TextMeshProUGUI label, string key, string what)> ExpectedTexts(KH_VideoCamera v, List<string> missing)
        {
            var l = new List<(TextMeshProUGUI, string, string)>();
            Transform mon = v.monitorRoot != null ? v.monitorRoot.transform : null;
            void Add(Transform root, string path, string key, bool required)
            {
                var t = root != null ? root.Find(path) : null;
                var tmp = t != null ? t.GetComponent<TextMeshProUGUI>() : null;
                if (tmp != null) l.Add((tmp, key, path));
                else if (required && missing != null) missing.Add(path);
            }
            Add(mon, PanelName + "/Title", KH_VideoCamera.KeyTitle, true);
            for (int m = 0; m < KH_VideoCamera.ModeCount; m++) Add(mon, PanelName + "/" + ModeButtonName(m) + "/Label", KH_VideoCamera.ModeKey(m), true);
            string[] ck = KH_VideoCamera.CtlKeys();
            for (int i = 0; i < ck.Length; i++) if (ck[i].Length > 0) Add(mon, PanelName + "/" + CtlName(i) + "/Label", ck[i], true);
            Add(mon, PanelName + "/SnapNote", KH_VideoCamera.KeySnapNote, true);
            Add(mon, PanelName + "/" + HideMonitorName + "/Label", KeyHideMonitor, false);   // Video/1 が作る (無ければ Video/2 の配線検査が NG)
            Add(mon, PanelName + "/" + PersonListName + "/Close/Label", KH_VideoCamera.KeyPersonClose, false);   // Video/4
            Add(mon, PanelName + "/" + PersonConfirmName + "/Yes/Label", KH_VideoCamera.KeyAskYes, false);
            Add(mon, PanelName + "/" + PersonConfirmName + "/No/Label", KH_VideoCamera.KeyAskNo, false);
            Add(mon, PatternPanelName + "/Title", KH_VideoCamera.KeyPatternTitle, true);
            for (int p = 0; p < KH_VideoCamera.PatternCount; p++)
            {
                Add(mon, PatternPanelName + "/" + PatternButtonName(p) + "/Label", KH_VideoCamera.PatternNameKey(p), true);
                Add(mon, PatternPanelName + "/PatternNote" + p, KH_VideoCamera.PatternNoteKey(p), true);
            }
            Add(mon, PatternPanelName + "/" + PatternStopName + "/Label", KH_VideoCamera.KeyPatternStop, true);
            foreach (var (go, key) in new[] { (v.entryOpen, KH_VideoCamera.KeyEntry), (v.entryRecall, KH_VideoCamera.KeyRecall), (v.entryClose, KH_VideoCamera.KeyPutAway) })
                Add(go != null ? go.transform : null, "Label", key, true);
            return l;
        }

        /// <summary>Video/1 / Video/4: textLabels / textKeys = the static labels, each written in Japanese (the runtime rewrites them).</summary>
        static void WireTexts(KH_VideoCamera v, List<string> log)
        {
            var missing = new List<string>();
            var list = ExpectedTexts(v, missing);
            if (missing.Count > 0) throw new InvalidOperationException("static labels missing: " + string.Join(", ", missing));
            v.textLabels = list.Select(x => x.label).ToArray();
            v.textKeys = list.Select(x => x.key).ToArray();
            foreach (var x in list) { string ja = Ja(x.key); if (x.label.text != ja) { x.label.text = ja; EditorUtility.SetDirty(x.label); } }
            log.Add("W5: " + list.Count + " static labels wired (textLabels / textKeys; rewritten in the player's language on open / language change)");
        }

        /// <summary>Video/1: KD_Language's tables from the catalog (the same refresh as Photo/1 / Menu/11; bindings untouched), the glyphs of the
        /// glyphs=self languages, the photo page hook.</summary>
        static void W5RefreshLanguage(Ctx c, KH_VideoCamera v, List<string> log)
        {
            var sb = new StringBuilder();
            KD_LocalizationEditor.Configure(c.language, Cat(), sb);
            UdonSharpEditorUtility.CopyProxyToUdon(c.language); EditorUtility.SetDirty(c.language);
            log.Add("W5: language tables refreshed from UITranslations.json (Configure; bindings untouched), keys " + c.language.keys.Length);
            c.pouch.photoVideo = v;
            UdonSharpEditorUtility.CopyProxyToUdon(c.pouch); EditorUtility.SetDirty(c.pouch);
            log.Add("W5: KD_TreatPouch.photoVideo -> KH_VideoCamera (the photo page sends _RefreshLanguage)");
        }

        // ================================================================ Video/2

        static void VerifyLanguage(KH_VideoCamera v, Action<bool, string> Check, Action<string> Warn)
        {
            _w5Catalog = null;
            var cat = Cat();
            var pairs = W5Keys();
            Check(pairs.Count == 107, "W5: " + pairs.Count + " video.* / film.* keys (107 expected: 104 + video.hide (DES-WLD-004 M1.1) + video.mode.convoy (M2 隊列) + video.open.entry (IMP-UI-009); change the number here when a text is added)");
            var bad = W5CatalogProblems();
            Check(bad.Count == 0, "W5: every key in all " + cat.languages.Length + " languages, the same {0} / {1} count as ja, ja = the code default, no stray key"
                + (bad.Count > 0 ? " (" + bad.Count + " problems, first: " + string.Join(" | ", bad.Take(3)) + ")" : ""));
            // the scene's table (KD_Language) holds what the catalog says
            var lang = v.language;
            Check(lang != null && lang.keys != null && lang.texts != null, "W5: the camera's KD_Language has tables");
            if (lang != null && lang.keys != null && lang.texts != null && lang.languageCodes != null)
            {
                int stale = 0; string first = null;
                foreach (var kv in pairs)
                {
                    int at = Array.IndexOf(lang.keys, kv.Key);
                    if (at < 0) { stale++; first = first ?? kv.Key + " not in the table"; continue; }
                    for (int li = 0; li < lang.languageCodes.Length; li++)
                    {
                        int idx = li * lang.keys.Length + at;
                        string want = KD_LocalizationEditor.Text(cat, lang.languageCodes[li], kv.Key);
                        if (idx >= lang.texts.Length || lang.texts[idx] != want) { stale++; first = first ?? lang.languageCodes[li] + ":" + kv.Key + " stale"; }
                    }
                }
                Check(stale == 0, "W5: KD_Language holds the current texts of every W5 key" + (stale > 0 ? " (" + stale + " stale / missing, first " + first + "; run Video/1)" : ""));
            }
            // glyphs of the self languages (ja / en)
            string miss = KD_ShopSetupEditor.MissingGlyphs(BundledW5Text());
            Check(miss.Length == 0, "W5: the shared font has every glyph of the ja / en texts" + (miss.Length > 0 ? " (missing " + miss + "; run Video/1)" : ""));
            var nameFont = NameFont();
            if (nameFont != null)
            {
                var nm = new HashSet<char>(BundledW5Text(NameFontKeys).Where(ch => ch != ' ' && ch != '\n' && !KD_LocalizationEditor.HasGlyph(nameFont, ch)));
                Check(nm.Count == 0, "W5: the display-name font has the ja / en glyphs of the labels that carry a name" + (nm.Count > 0 ? " (missing " + new string(nm.ToArray()) + "; Course/86 charset)" : ""));
            }
            // the static labels
            var missing = new List<string>();
            var exp = ExpectedTexts(v, missing);
            Check(missing.Count == 0, "W5: the static labels exist" + (missing.Count > 0 ? " (missing " + string.Join(", ", missing) + ")" : ""));
            bool wired = v.textLabels != null && v.textKeys != null && v.textLabels.Length == exp.Count && v.textKeys.Length == exp.Count
                && exp.All(x => { int i = Array.IndexOf(v.textLabels, x.label); return i >= 0 && v.textKeys[i] == x.key; });
            Check(wired, "W5: textLabels / textKeys = the " + exp.Count + " static labels and their keys (have " + (v.textLabels != null ? v.textLabels.Length : 0) + ")");
            var wrong = exp.Where(x => x.label.text != KD_LocalizationEditor.Text(cat, "ja", x.key)).Select(x => x.what).ToArray();
            Check(wrong.Length == 0, "W5: every static label holds the catalog's ja in the scene" + (wrong.Length > 0 ? " (differs: " + string.Join(", ", wrong.Take(5)) + ")" : ""));
            var vb = UdonSharpEditorUtility.GetBackingUdonBehaviour(v);
            if (vb != null && !vb.publicVariables.TryGetVariableValue("textKeys", out object _)) Warn("W5: 'textKeys' not in the backing heap yet (a saved / built scene carries it; rerun Video/2 after a reload)");
            // spots
            int ns = v.spotNames != null ? v.spotNames.Length : 0;
            Check(v.spotKeys != null && v.spotKeys.Length == ns && v.spotKeys.All(k => k == KH_VideoCamera.KeySpotWash || k == KH_VideoCamera.KeySpotRiver || k == KH_VideoCamera.KeySpotRiverLow)
                && Enumerable.Range(0, ns).All(i => (v.spotKeys[i] == KH_VideoCamera.KeySpotWash) == (v.spotStations != null && i < v.spotStations.Length && v.spotStations[i] >= 0)),
                "W5: spotKeys (" + (v.spotKeys != null ? v.spotKeys.Length : 0) + ") match the " + ns + " spots (wash = a station)");
            // the photo page hook
            var pouch = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true).FirstOrDefault();
            Check(pouch != null && pouch.photoVideo == v, "W5: KD_TreatPouch.photoVideo = this camera (the photo page refreshes the entry buttons' language)");
            // the source rules: displayed texts go through Tx / TJa / FilmL (not a bare Japanese constant)
            string all = string.Concat(System.IO.Directory.GetFiles("Assets/KominkaHillside/Scripts", "KH_VideoCamera*.cs").Where(f => !f.EndsWith(".meta")).Select(System.IO.File.ReadAllText));
            var bare = Regex.Matches(all, @"\.text = [^;\n]*").Cast<Match>().Select(m => m.Value)
                .Where(s => Regex.IsMatch(Regex.Replace(s, @"Tx\([^()]*\)", ""), @"\bWord[A-Z]")).ToArray();
            Check(bare.Length == 0, "W5: no label of the camera is written from a bare Word* constant" + (bare.Length > 0 ? " (" + string.Join(" | ", bare.Take(3)) + ")" : ""));
            // English fit (WARN only: the client-glyph languages are drawn by VRChat's own font and checked on a device)
            var go = new GameObject("W5Measure", typeof(RectTransform)); go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var sample = go.AddComponent<TextMeshProUGUI>();
                var tight = new List<string>();
                foreach (var x in exp)
                {
                    int res = KD_LocalizationEditor.MeasureLabelFit(sample, x.label, KD_LocalizationEditor.Text(cat, "en", x.key), out Vector2 pref);
                    if (res == 2) tight.Add(x.what + " (" + pref.x.ToString("F0") + "×" + pref.y.ToString("F0") + " in " + x.label.rectTransform.rect.size + ")");
                }
                if (tight.Count > 0) Warn("W5: English static labels that do not fit at the smallest size: " + string.Join("; ", tight));
                else Check(true, "W5: every English static label fits");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
