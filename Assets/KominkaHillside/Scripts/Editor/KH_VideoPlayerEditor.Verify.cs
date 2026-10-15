using System;
using System.Collections.Generic;
using System.Linq;
using KominkaDogRun;
using KominkaDogRun.Editor;
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
using VCore = JLChnToZ.VRC.VVMW.Core;
using VFrontend = JLChnToZ.VRC.VVMW.FrontendHandler;
using VPlayerHandler = JLChnToZ.VRC.VVMW.VideoPlayerHandler;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-UI-011 Player/2 - Verify (読むだけ。シーンも資産も変えない)。Player/1 は保存の前に同じ検査を走らせ、FAIL があれば保存しない。
    /// 結果: Temp/imp-ui-011/verify.json と Editor.log の 1 行 (PLAYER_VERIFY_OK / PLAYER_VERIFY_FAIL)。</summary>
    public static partial class KH_VideoPlayerEditor
    {
        [Serializable] class VerifyReport
        {
            public string utc, scene, status, header;
            public int fails, warns;
            public List<string> lines = new List<string>();
        }

        [MenuItem(Menu + "2 - Verify")]
        public static void Verify()
        {
            var r = RunVerify(null);
            WriteJson(OutDir + "/verify.json", r);
            string head = r.fails == 0 ? "PLAYER_VERIFY_OK" : "PLAYER_VERIFY_FAIL";
            string msg = "[KH Player] " + head + " fails=" + r.fails + " warns=" + r.warns + " header " + r.header + " (" + OutDir + "/verify.json)"
                + (r.fails > 0 ? " first: " + string.Join(" | ", r.lines.Where(l => l.StartsWith("FAIL")).Take(4)) : "");
            if (r.fails == 0) Debug.Log(msg); else Debug.LogError(msg);
        }

        /// <summary>baselineNull: Player/1 が変更の前に控えた「GameObject を失った NetworkIDs の ID」。ここに入っている残骸は今回の作業のものではないので WARN に落とす (Player/2 単独では null = 全部 FAIL)。</summary>
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
                VerifyBody(r, baselineNull, Check, Warn, Note);
            }
            catch (Exception e) { Check(false, "verify stopped: " + e.Message); }
            r.status = r.fails == 0 ? "PASS" : "FAIL";
            return r;
        }

        static void VerifyBody(VerifyReport r, HashSet<int> baselineNull, Action<bool, string> Check, Action<string> Warn, Action<string> Note)
        {
            var c = Resolve();
            foreach (var n in c.notes) Note(n);
            Check(c.missingKeys.Count == 0, "catalog " + KD_MenuFinishEditor.CatalogPath + " has every video.player.* text (" + c.catalog.fallbackLanguage + ")" + (c.missingKeys.Count > 0 ? ": missing " + string.Join(", ", c.missingKeys) : ""));
            Check(AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath + ".asset") != null, "program asset " + ProgramPath + ".asset (Player/0)");
            var p = c.pouch; var pU = c.pouchUdon;
            var panel = p.panelRoot.transform;

            // ---------------------------------------------------------------- header (4 layouts; the current one against the scene)
            VerifyHeader(c, r, Check, Note);

            // ---------------------------------------------------------------- VizVid root
            var roots = c.scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray();
            KD_SceneObjects.Find(RootName, out int anywhere);
            Check(roots.Length == 1 && anywhere == 1, "exactly one " + RootName + " at the scene root (root " + roots.Length + ", anywhere " + anywhere + ")");
            var page = p.settingsPage.transform.Find(PageName);
            if (roots.Length != 1)
            {
                Check(page == null && p.videoPage == null && panel.Find(KD_HandMenuScrollEditor.VideoHeaderButtonName) == null, "no VizVid: no Page_Settings/" + PageName + ", no header [動画], pouch.videoPage null");
                VerifyNetworkIds(baselineNull, Check, Warn, Note);
                return;
            }
            var root = roots[0];
            var prefabbed = root.GetComponentsInChildren<Transform>(true).Where(t => PrefabUtility.IsPartOfPrefabInstance(t.gameObject)).Select(t => t.name).ToArray();
            Check(prefabbed.Length == 0, RootName + " is unpacked completely (no prefab instance inside; vrc-udonsharp §3)" + (prefabbed.Length > 0 ? ": " + string.Join(", ", prefabbed.Take(5)) : ""));
            Check(root.activeSelf && root.transform.position.sqrMagnitude < 1e-6f, RootName + " active at the origin (the audio is 2D, so its position does not matter)");
            var cores = Object.FindObjectsOfType<VCore>(true).Where(x => x.gameObject.scene == c.scene).ToArray();
            var fronts = Object.FindObjectsOfType<VFrontend>(true).Where(x => x.gameObject.scene == c.scene).ToArray();
            var handlers = root.GetComponentsInChildren<VPlayerHandler>(true);
            Check(cores.Length == 1 && cores[0].transform.IsChildOf(root.transform), "one VizVid Core in the scene, inside " + RootName + " (" + cores.Length + ")");
            Check(fronts.Length == 1 && fronts[0].transform.IsChildOf(root.transform), "one VizVid FrontendHandler in the scene, inside " + RootName + " (" + fronts.Length + ")");
            Check(handlers.Length >= 1, "VideoPlayerHandler present (" + handlers.Length + ": " + string.Join(", ", handlers.Select(h => h.name)) + ")");
            var core = cores.Length == 1 ? cores[0] : null; var front = fronts.Length == 1 ? fronts[0] : null;
            if (core == null || front == null) { VerifyNetworkIds(baselineNull, Check, Warn, Note); return; }

            // ---------------------------------------------------------------- Core.screenTargets (7 parallel arrays) and the page's RawImage
            var screen = page != null ? page.Find("Screen") : null;
            var raw = screen != null ? screen.GetComponent<RawImage>() : null;
            using (var so = new SerializedObject(core))
            {
                var props = ScreenArrays.Select(n => so.FindProperty(n)).ToArray();
                bool found = props.All(x => x != null && x.isArray);
                Check(found, "Core has the 7 screen arrays " + string.Join(" / ", ScreenArrays));
                if (found)
                {
                    int n0 = props[0].arraySize;
                    Check(props.All(x => x.arraySize == n0), "Core screen arrays are the same length (" + string.Join(" / ", props.Select(x => x.arraySize)) + ")");
                    var hits = Enumerable.Range(0, n0).Where(i => raw != null && props[0].GetArrayElementAtIndex(i).objectReferenceValue == raw).ToArray();
                    Check(raw != null && hits.Length == 1, "Core.screenTargets holds " + PageName + "/Screen (RawImage) exactly once (" + hits.Length + ")");
                    if (hits.Length == 1 && props.All(x => x.arraySize == n0))
                    {
                        int i = hits[0];
                        Check(props[1].GetArrayElementAtIndex(i).intValue == RawImageMode && props[2].GetArrayElementAtIndex(i).intValue == -1
                            && props[3].GetArrayElementAtIndex(i).stringValue == "_MainTex" && props[5].GetArrayElementAtIndex(i).stringValue == "_IsAVProVideo"
                            && props[6].GetArrayElementAtIndex(i).vector4Value == Vector4.zero,
                            "screen " + i + ": mode " + RawImageMode + " (UI RawImage; AVPro flips uvRect), index -1, _MainTex, _IsAVProVideo, ST 0 (actual mode " + props[1].GetArrayElementAtIndex(i).intValue + ")");
                    }
                    Note("Core screens: " + n0 + " (the default texture of an empty slot falls back to Core.defaultTexture)");
                }
            }
            Check(front.core == core, "FrontendHandler.core = the Core (SingletonCoreControl is an inspector mark; the wiring is checked here)");
            using (var so = new SerializedObject(front))
            {
                var q = so.FindProperty("enableQueueList");
                Check(q != null && !q.boolValue, "FrontendHandler.enableQueueList = false (a new URL plays at once)" + (q == null ? " (field not found)" : ""));
            }
            Heap(B(front), "enableQueueList", false, Check, Warn);

            // ---------------------------------------------------------------- audio (2D)
            var problems = new List<string>();
            var sources = HandlerAudio(handlers, problems);
            Check(problems.Count == 0 && sources.Count > 0, "every VideoPlayerHandler has a primaryAudioSource (" + string.Join(", ", sources.Select(a => a.name)) + ")" + (problems.Count > 0 ? ": " + string.Join("; ", problems) : ""));
            using (var so = new SerializedObject(core))
            {
                var audioArr = so.FindProperty("audioSources");
                var inCore = new List<Object>();
                if (audioArr != null) for (int i = 0; i < audioArr.arraySize; i++) inCore.Add(audioArr.GetArrayElementAtIndex(i).objectReferenceValue);
                foreach (var a in sources)
                {
                    var sp = a.GetComponent<VRCSpatialAudioSource>();
                    Check(a.spatialBlend <= 0.001f && !a.spatialize, a.name + ": spatialBlend 0, spatialize off (actual " + a.spatialBlend + ")");
                    Check(sp != null && !sp.EnableSpatialization, a.name + ": VRCSpatialAudioSource.EnableSpatialization off" + (sp == null ? " (component missing)" : " (Gain " + sp.Gain + " / Near " + sp.Near + " / Far " + sp.Far + " kept)"));
                    Check(inCore.Contains(a), a.name + " is in Core.audioSources (Core.Volume / Muted reach it)");
                }
            }

            // ---------------------------------------------------------------- KH_VideoPage
            var vpT = root.transform.Find(PageBehaviourName);
            var vp = vpT != null ? vpT.GetComponent<KH_VideoPage>() : null;
            var vpU = B(vp);
            Check(vp != null && vpU != null && vpU.SyncMethod == Networking.SyncType.None && vpT.gameObject.activeSelf && vp.enabled,
                RootName + "/" + PageBehaviourName + ": KH_VideoPage, backing present, sync None, active");
            Check(page != null && page.parent == p.settingsPage.transform && !page.gameObject.activeSelf, "Page_Settings/" + PageName + " exists and is hidden in the scene");
            if (page == null || vp == null || vpU == null) { VerifyNetworkIds(baselineNull, Check, Warn, Note); return; }
            var header = panel.Find(KD_HandMenuScrollEditor.VideoHeaderButtonName);
            Check(header != null, "header [動画] " + KD_HandMenuScrollEditor.VideoHeaderButtonName + " exists (Player/1)");
            Button Bt(string name) { var t = page.Find(name); return t != null ? t.GetComponent<Button>() : null; }
            TextMeshProUGUI Lb(string name) { var t = page.Find(name); return t != null ? LabelOf(t.gameObject) : null; }
            TextMeshProUGUI Tx(string name) { var t = page.Find(name); return t != null ? t.GetComponent<TextMeshProUGUI>() : null; }
            var url = page.Find("UrlInput") != null ? page.Find("UrlInput").GetComponent<VRCUrlInputField>() : null;
            var slider = page.Find("VolumeSlider") != null ? page.Find("VolumeSlider").GetComponent<Slider>() : null;
            var placeholder = page.Find("UrlInput/Placeholder") != null ? page.Find("UrlInput/Placeholder").GetComponent<TextMeshProUGUI>() : null;
            var fields = new (string name, Object got, Object want)[]
            {
                ("frontend", vp.frontend, front), ("core", vp.core, core), ("language", vp.language, c.language), ("urlInput", vp.urlInput, url), ("screen", vp.screen, raw),
                ("submitButton", vp.submitButton, Bt("Submit")), ("playButton", vp.playButton, Bt("Play")), ("pauseButton", vp.pauseButton, Bt("Pause")),
                ("stopButton", vp.stopButton, Bt("Stop")), ("resyncButton", vp.resyncButton, Bt("Resync")), ("muteButton", vp.muteButton, Bt("Mute")),
                ("volumeSlider", vp.volumeSlider, slider), ("stateLabel", vp.stateLabel, Tx("State")), ("noteLabel", vp.noteLabel, Tx("Note")),
                ("volumeLabel", vp.volumeLabel, Tx("VolumeLabel")), ("urlPlaceholder", vp.urlPlaceholder, placeholder),
                ("submitLabel", vp.submitLabel, Lb("Submit")), ("playLabel", vp.playLabel, Lb("Play")), ("pauseLabel", vp.pauseLabel, Lb("Pause")),
                ("stopLabel", vp.stopLabel, Lb("Stop")), ("resyncLabel", vp.resyncLabel, Lb("Resync")), ("muteLabel", vp.muteLabel, Lb("Mute")), ("backLabel", vp.backLabel, Lb("Back")),
                ("page", vp.page, page.gameObject), ("headerLabel", vp.headerLabel, header != null ? LabelOf(header.gameObject) : null),
            };
            foreach (var f in fields) Check(f.got != null && f.want != null && f.got == f.want, "KH_VideoPage." + f.name + " = " + (f.want != null ? f.want.name : "(expected object missing)") + (f.got == null ? " (null)" : f.got != f.want ? " (actual " + f.got.name + ")" : ""));
            Heap(vpU, "core", B(core), Check, Warn); Heap(vpU, "frontend", B(front), Check, Warn); Heap(vpU, "screen", raw, Check, Warn);
            Heap(vpU, "urlInput", url, Check, Warn); Heap(vpU, "page", page.gameObject, Check, Warn); Heap(vpU, "language", B(c.language), Check, Warn);

            // ---------------------------------------------------------------- events (persistent, static string -> SendCustomEvent)
            for (int i = 0; i < ControlNames.Length; i++) CheckEvent(page.Find(ControlNames[i]), "m_OnClick", vpU, ControlEvents[i], typeof(KH_VideoPage), Check);
            CheckEvent(page.Find("Submit"), "m_OnClick", vpU, "_SubmitUrl", typeof(KH_VideoPage), Check);
            CheckEvent(page.Find("Mute"), "m_OnClick", vpU, "_ToggleMute", typeof(KH_VideoPage), Check);
            CheckEvent(page.Find("UrlInput"), "m_OnEndEdit", vpU, "_SubmitUrl", typeof(KH_VideoPage), Check);
            CheckEvent(page.Find("VolumeSlider"), "m_OnValueChanged", vpU, "_VolumeChanged", typeof(KH_VideoPage), Check);
            CheckEvent(page.Find("Back"), "m_OnClick", pU, "_VideoBack", typeof(KD_TreatPouch), Check);
            CheckEvent(header, "m_OnClick", pU, "_VideoOpen", typeof(KD_TreatPouch), Check);
            foreach (var evt in new[] { "_PageOpened", "_RefreshLanguage" }) Check(typeof(KH_VideoPage).GetMethod(evt) != null, "KH_VideoPage." + evt + " exists (the pouch sends it)");

            // ---------------------------------------------------------------- pouch / BGM
            Check(p.extraSettingsPages != null && p.extraSettingsPages.Contains(page.gameObject), "page registered in extraSettingsPages (proxy)");
            if (pU.publicVariables.TryGetVariableValue("extraSettingsPages", out object pagesObj)) Check(pagesObj is GameObject[] arr && arr.Contains(page.gameObject), "page registered in extraSettingsPages (backing)");
            else Warn("pouch backing has no extraSettingsPages yet (heap populated on save / reload)");
            Check(p.videoPage == page.gameObject && p.videoPageBehaviour == vp, "pouch.videoPage = the page, pouch.videoPageBehaviour = KH_VideoPage");
            Heap(pU, "videoPage", page.gameObject, Check, Warn); Heap(pU, "videoPageBehaviour", vpU, Check, Warn);
            foreach (var b in c.bgms) { Check(b.videoCore == core, "KD_BgmPlayer " + b.name + ".videoCore = the Core (BGM ducks while a video plays)"); Heap(B(b), "videoCore", B(core), Check, Warn); }

            // ---------------------------------------------------------------- page layout (band Y 148..-232, right column x 110..550, no overlaps)
            VerifyPageLayout(page, Check, Note);
            Check(page.gameObject.layer == p.settingsPage.layer && page.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == page.gameObject.layer),
                "page and all its parts on the settings page's layer " + p.settingsPage.layer + " (" + LayerMask.LayerToName(p.settingsPage.layer) + ")");
            var frame = page.Find("ScreenFrame");
            Check(frame != null && screen != null && frame.GetSiblingIndex() < screen.GetSiblingIndex() && raw != null && !raw.raycastTarget, "the black frame draws behind the screen; the screen takes no pointer");

            // ---------------------------------------------------------------- URL field and slider
            if (url != null)
            {
                using (var so = new SerializedObject(url))
                {
                    var tc = so.FindProperty("m_TextComponent"); var ph = so.FindProperty("m_Placeholder"); var lt = so.FindProperty("m_LineType");
                    var legacy = tc != null ? tc.objectReferenceValue as UnityEngine.UI.Text : null;
                    Check(legacy != null && legacy.font != null && legacy.transform.parent == url.transform, "URL field text = legacy UI.Text child with a font (" + (legacy != null && legacy.font != null ? legacy.font.name : "none") + ")");
                    Check(ph != null && ph.objectReferenceValue == placeholder && placeholder != null, "URL field placeholder = the TMP Placeholder (KH_VideoPage.urlPlaceholder)");
                    Check(lt != null && lt.intValue == 0, "URL field single line");
                }
                Check(url.GetComponent<RectMask2D>() != null, "URL field clips its text (RectMask2D)");
            }
            else Check(false, "UrlInput has a VRCUrlInputField");
            if (slider != null)
            {
                var ha = slider.transform.Find("HandleArea") as RectTransform;
                Check(slider.fillRect != null && slider.handleRect != null && Mathf.Approximately(slider.minValue, 0f) && Mathf.Approximately(slider.maxValue, 1f) && !slider.wholeNumbers,
                    "volume slider 0..1, fill / handle wired");
                // vrc-world-ui §10: the handle's anchors are driven and may not be saved; check the movable area instead
                Check(ha != null && ha.sizeDelta.y > 0f && ha.sizeDelta.x > 0f && ha.sizeDelta.x <= SliderSize.x, "volume slider handle area " + (ha != null ? ha.sizeDelta.ToString() : "missing") + " inside the slider " + SliderSize);
            }

            // ---------------------------------------------------------------- language tables, glyphs, text fit
            foreach (var key in NeededKeys)
            {
                string want = TextOrNull(c.catalog, "ja", key);
                if (want == null) continue;   // reported above
                string got = c.language.keys != null && Array.IndexOf(c.language.keys, key) >= 0 ? c.language._TextForLanguage(key, "ja") : null;
                Check(got == want, "KD_Language has the current text of " + key + (got == null ? " (missing: run Player/1 or Menu/11)" : ""));
            }
            string missingGlyphs = KD_ShopSetupEditor.MissingGlyphs(BundledGlyphs(c));
            Check(missingGlyphs.Length == 0, "shared font has every glyph of the video texts (bundled languages)" + (missingGlyphs.Length > 0 ? ": missing " + missingGlyphs + " (run Player/1)" : ""));
            VerifyTextFit(c, page, header, Check, Warn, Note);

            VerifyNetworkIds(baselineNull, Check, Warn, Note);
            Note("not checked here (device): playback, the AVPro flip, sync between 2 players, Quest load, iOS (IMP-UI-011 §5)");
        }

        // ---------------------------------------------------------------- header

        static void VerifyHeader(Ctx c, VerifyReport r, Action<bool, string> Check, Action<string> Note)
        {
            const float half = 570f;   // KD_TreatSetupEditor.PanelWidth / 2
            float y = KD_HandMenuScrollEditor.HeaderRowY, h = KD_HandMenuScrollEditor.HeaderRowHeight;
            // ---- the 4 layouts analytically (title / [写真] / [動画] / points frame / [設定] / [閉じる]) ----
            foreach (var combo in new[] { (false, false), (true, false), (false, true), (true, true) })
            {
                KD_HandMenuScrollEditor.HeaderLayout(combo.Item1, combo.Item2, out Vector2 tp, out Vector2 ts, out float px, out float vx, out float bx, out float bw, out float pw);
                var rects = new List<(string, Rect)> { ("title", Box(tp, ts)) };
                if (combo.Item1) rects.Add(("photo", Box(new Vector2(px, y), new Vector2(KD_HandMenuScrollEditor.HeaderMediaButtonWidth, h))));
                if (combo.Item2) rects.Add(("video", Box(new Vector2(vx, y), new Vector2(KD_HandMenuScrollEditor.HeaderMediaButtonWidth, h))));
                rects.Add(("points", Box(new Vector2(bx, y), new Vector2(pw, h + KD_HandMenuScrollEditor.PointsExtraHeight))));
                rects.Add(("settings", Box(new Vector2(KD_HandMenuScrollEditor.HeaderSettingsX, y), new Vector2(KD_HandMenuScrollEditor.HeaderSettingsWidth, h))));
                rects.Add(("close", Box(new Vector2(KD_HandMenuScrollEditor.HeaderCloseX, y), new Vector2(KD_HandMenuScrollEditor.HeaderCloseWidth, h))));
                var hits = Overlaps(rects);
                bool inside = rects.All(x => x.Item2.xMin >= -half && x.Item2.xMax <= half);
                Check(hits.Count == 0 && inside && bw <= pw, "header layout photo=" + combo.Item1 + " video=" + combo.Item2 + ": no overlap, inside the panel, balance text " + bw + " <= frame " + pw
                    + (hits.Count > 0 ? ": " + string.Join(", ", hits) : ""));
            }
            // ---- the scene's current layout ----
            var panel = c.pouch.panelRoot.transform;
            var photoT = panel.Find(KD_HandMenuScrollEditor.PhotoHeaderButtonName) as RectTransform;
            var videoT = panel.Find(KD_HandMenuScrollEditor.VideoHeaderButtonName) as RectTransform;
            r.header = "photo=" + (photoT != null) + " video=" + (videoT != null);
            KD_HandMenuScrollEditor.HeaderLayout(photoT != null, videoT != null, out Vector2 wtp, out Vector2 wts, out float wpx, out float wvx, out float wbx, out float wbw, out float wpw);
            var title = (RectTransform)c.pouch.sectionTitleLabel.transform;
            Check(Vector2.Distance(title.anchoredPosition, wtp) < 0.5f && Mathf.Abs(title.sizeDelta.x - wts.x) < 0.5f, "title box at " + wtp + " " + wts.x + " wide (actual " + title.anchoredPosition + " / " + title.sizeDelta.x + ")");
            if (photoT != null) Check(Mathf.Abs(photoT.anchoredPosition.x - wpx) < 0.5f && Mathf.Abs(photoT.anchoredPosition.y - y) < 0.5f, "[写真] at x " + wpx + " (actual " + photoT.anchoredPosition + ")");
            if (videoT != null) Check(Mathf.Abs(videoT.anchoredPosition.x - wvx) < 0.5f && Mathf.Abs(videoT.anchoredPosition.y - y) < 0.5f
                && Vector2.Distance(videoT.sizeDelta, new Vector2(KD_HandMenuScrollEditor.HeaderMediaButtonWidth, h)) < 0.5f, "[動画] at x " + wvx + " 120x44 (actual " + videoT.anchoredPosition + " " + videoT.sizeDelta + ")");
            var points = panel.Find(KD_GiftCodeSetupEditor.PointsButtonName) as RectTransform;
            var balance = c.pouch.balanceLabel != null ? c.pouch.balanceLabel.rectTransform : null;
            if (points != null) Check(Mathf.Abs(points.anchoredPosition.x - wbx) < 0.5f && Mathf.Abs(points.sizeDelta.x - wpw) < 0.5f, "points frame at x " + wbx + " " + wpw + " wide (actual " + points.anchoredPosition.x + " / " + points.sizeDelta.x + ")");
            if (balance != null) Check(Mathf.Abs(balance.anchoredPosition.x - wbx) < 0.5f && Mathf.Abs(balance.sizeDelta.x - wbw) < 0.5f, "balance text at x " + wbx + " " + wbw + " wide (actual " + balance.anchoredPosition.x + " / " + balance.sizeDelta.x + ")");
            var scene = new List<(string, Rect)> { ("title", RectOf(title)) };
            if (photoT != null) scene.Add(("photo", RectOf(photoT)));
            if (videoT != null) scene.Add(("video", RectOf(videoT)));
            if (points != null) scene.Add(("points", RectOf(points))); else if (balance != null) scene.Add(("balance", RectOf(balance)));
            foreach (var n in new[] { "SettingsButton", "CloseButton" }) { var t = panel.Find(n) as RectTransform; if (t != null) scene.Add((n, RectOf(t))); }
            var sceneHits = Overlaps(scene);
            Check(sceneHits.Count == 0, "header in the scene (" + r.header + "): no two of " + string.Join(" / ", scene.Select(x => x.Item1)) + " overlap" + (sceneHits.Count > 0 ? ": " + string.Join(", ", sceneHits) : ""));
        }

        // ---------------------------------------------------------------- page layout

        static void VerifyPageLayout(Transform page, Action<bool, string> Check, Action<string> Note)
        {
            var rects = new List<(string, Rect)>();
            foreach (var spec in PageLayout())
            {
                var t = page.Find(spec.name) as RectTransform;
                Check(t != null && Vector2.Distance(t.anchoredPosition, spec.pos) < 0.5f && Vector2.Distance(t.sizeDelta, spec.size) < 0.5f,
                    "page " + spec.name + " " + spec.size + " at " + spec.pos + (t == null ? " (missing)" : " (actual " + t.sizeDelta + " at " + t.anchoredPosition + ")"));
                if (t == null) continue;
                var rc = RectOf(t);
                rects.Add((spec.name, rc));
                bool top = spec.name == "State" || spec.name == "Back";
                if (!top) Check(rc.yMax <= BandTop + 0.01f && rc.yMin >= BandBottom - 0.01f, "page " + spec.name + " inside the body band Y " + BandTop + ".." + BandBottom + " (" + rc.yMax + ".." + rc.yMin + ")");
                bool column = !top && spec.name != "Screen" && spec.name != "ScreenFrame";
                if (column) Check(rc.xMin >= ColumnLeft - 0.01f && rc.xMax <= ColumnRight + 0.01f, "page " + spec.name + " inside the right column x " + ColumnLeft + ".." + ColumnRight + " (" + rc.xMin + ".." + rc.xMax + ")");
            }
            var hits = Overlaps(rects.Where(x => x.Item1 != "ScreenFrame").ToList());
            var frame = rects.Where(x => x.Item1 == "ScreenFrame").Select(x => x.Item2).ToArray();
            if (frame.Length == 1) foreach (var x in rects) if (x.Item1 != "ScreenFrame" && x.Item1 != "Screen" && x.Item2.Overlaps(frame[0])) hits.Add("ScreenFrame x " + x.Item1);
            Check(hits.Count == 0, "page parts overlap each other nowhere (" + rects.Count + " parts)" + (hits.Count > 0 ? ": " + string.Join(", ", hits) : ""));
            var extra = new List<string>();
            foreach (Transform child in page) if (!PageLayout().Any(s => s.name == child.name)) extra.Add(child.name);
            Check(extra.Count == 0, "no unexpected child on the page" + (extra.Count > 0 ? ": " + string.Join(", ", extra) : ""));
        }

        // ---------------------------------------------------------------- text fit (vrc-world-ui §10; bundled languages only)

        static void VerifyTextFit(Ctx c, Transform page, Transform header, Action<bool, string> Check, Action<string> Warn, Action<string> Note)
        {
            if (header != null) MeasureOne(c, LabelOf(header.gameObject), code => TextOrNull(c.catalog, code, HeaderKey), "header [動画] (120 px)", Check);
            TextMeshProUGUI Lb(string name) { var t = page.Find(name); return t != null ? LabelOf(t.gameObject) : null; }
            TextMeshProUGUI Tx(string name) { var t = page.Find(name); return t != null ? t.GetComponent<TextMeshProUGUI>() : null; }
            MeasureOne(c, Lb("Submit"), code => TextOrNull(c.catalog, code, "video.player.submit"), "[submit]", Check);
            for (int i = 0; i < ControlNames.Length; i++) { string key = ControlKeys[i]; MeasureOne(c, Lb(ControlNames[i]), code => TextOrNull(c.catalog, code, key), "[" + ControlNames[i] + "]", Check); }
            MeasureOne(c, Lb("Mute"), code => TextOrNull(c.catalog, code, "video.player.mute"), "[mute]", Check);
            MeasureOne(c, Lb("Mute"), code => TextOrNull(c.catalog, code, "video.player.unmute"), "[unmute]", Check);
            MeasureOne(c, Lb("Back"), code => TextOrNull(c.catalog, code, "gift.back"), "[back]", Check);
            MeasureOne(c, Tx("VolumeLabel"), code => { var t = TextOrNull(c.catalog, code, "video.player.volume"); return t != null ? t + " 100%" : null; }, "volume label (worst '<volume> 100%')", Check);
            MeasureOne(c, Tx("Note"), code => TextOrNull(c.catalog, code, "video.player.note"), "note (3 short lines, wraps, " + NoteFontMax + " -> " + NoteFontMin + "pt in " + NoteSize + ")", Check);
            { var note = Tx("Note"); Check(note != null && note.enableWordWrapping && note.enableAutoSizing && Mathf.Approximately(note.fontSizeMax, NoteFontMax) && Mathf.Approximately(note.fontSizeMin, NoteFontMin),
                "note wraps and auto-sizes " + NoteFontMax + " -> " + NoteFontMin + "pt"); }
            var ph = page.Find("UrlInput/Placeholder");
            MeasureOne(c, ph != null ? ph.GetComponent<TextMeshProUGUI>() : null, code => TextOrNull(c.catalog, code, "video.player.url"), "URL placeholder", Check);
            foreach (var key in StateKeys)
            {
                string k = key;
                if (TextOrNull(c.catalog, c.catalog.fallbackLanguage, k) == null) { Note("state " + k + ": not in the catalog yet (optional), not measured"); continue; }
                MeasureOne(c, Tx("State"), code => { var t = TextOrNull(c.catalog, code, k); return t?.Replace("{0}", k.EndsWith(".wait") ? "5" : LongPlayerName); }, "state " + key, Check);
            }
            // the section title shrinks to 320 when both [写真] and [動画] are in the header: every title (and the page's own) at the current width
            var title = c.pouch.sectionTitleLabel;
            string[] titles = { "welcome.title", "KD_TreatPouch.WordSettings", "KD_DistanceFadeSettings.Title", "KD_TreatPouch.WordVolumeSettings", "KD_TreatPouch.WordToyTitle", "KD_TreatPouch.WordWalkTitle",
                "KD_TreatPouch.WordSleepTitle", "KD_TreatPouch.WordAccTitle", "activities.title", "KD_TreatPouch.WordTitle", "gift.title", "information.title", "information.ai.title",
                "information.vanimal.title", "information.trainer.title", "welcome.reread.title", "ui.extra.022", "photo.menu", TitleKey };
            var over = new List<string>(); int measured = 0;
            var go = new GameObject("VideoTitleMeasure", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var sample = go.AddComponent<TextMeshProUGUI>();
                foreach (var lang in c.catalog.languages)
                {
                    if (!KD_LocalizationEditor.Bundled(lang)) continue;
                    foreach (var key in titles)
                    {
                        var text = TextOrNull(c.catalog, lang.code, key);
                        if (text == null) continue;
                        measured++;
                        if (!KD_HandMenuScrollEditor.TitleFits(sample, title, text, out Vector2 pref)) over.Add(lang.code + ":" + key + " " + pref.x.ToString("F0"));
                    }
                }
            }
            finally { Object.DestroyImmediate(go); }
            Check(title.enableAutoSizing && Mathf.Approximately(title.fontSizeMax, KD_HandMenuScrollEditor.TitleFontMax) && Mathf.Approximately(title.fontSizeMin, KD_HandMenuScrollEditor.TitleFontMin) && !title.enableWordWrapping,
                "section title auto-sizes " + KD_HandMenuScrollEditor.TitleFontMax + " -> " + KD_HandMenuScrollEditor.TitleFontMin + "pt on one line (LayoutFrame; actual auto " + title.enableAutoSizing + " " + title.fontSizeMax + " -> " + title.fontSizeMin + ")");
            if (over.Count > 0) Warn("TITLE_OVERFLOW at " + title.rectTransform.sizeDelta.x + " wide even at " + KD_HandMenuScrollEditor.TitleFontMin + "pt (the header with both [写真] and [動画] leaves 320): " + string.Join(", ", over));
            else Note("section titles fit " + title.rectTransform.sizeDelta.x + " wide at " + KD_HandMenuScrollEditor.TitleFontMin + "pt or more (" + measured + " bundled texts)");
            Note("text fit is measured for the bundled languages (glyphs=self) only; the client languages are drawn by VRChat's fallback font (device check)");
        }

        static void MeasureOne(Ctx c, TMP_Text target, Func<string, string> textFor, string what, Action<bool, string> Check)
        {
            if (target == null) { Check(false, what + ": label missing"); return; }
            var go = new GameObject("VideoMeasure", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
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

        static void VerifyNetworkIds(HashSet<int> baselineNull, Action<bool, string> Check, Action<string> Warn, Action<string> Note)
        {
            var d = Descriptor();
            if (d == null || d.NetworkIDCollection == null) { Warn("no VRCSceneDescriptor / NetworkIDs to check"); return; }
            var dead = d.NetworkIDCollection.Where(x => x != null && x.gameObject == null).ToList();
            var fresh = dead.Where(x => baselineNull == null || !baselineNull.Contains(x.ID)).ToList();
            var old = dead.Where(x => baselineNull != null && baselineNull.Contains(x.ID)).ToList();
            Check(fresh.Count == 0, "NetworkIDs: no entry without its GameObject" + (fresh.Count > 0 ? ": " + string.Join(", ", fresh.Take(8).Select(x => x.ID + (x.SerializedTypeNames != null && x.SerializedTypeNames.Count > 0 ? "(" + string.Join("/", x.SerializedTypeNames) + ")" : "(untyped)"))) : ""));
            if (old.Count > 0) Warn("NetworkIDs: " + old.Count + " entries had no GameObject before this run (not this builder's): " + string.Join(", ", old.Take(8).Select(x => x.ID)));
            int vizvid = d.NetworkIDCollection.Count(x => x != null && x.gameObject != null && x.gameObject.transform.root.name == RootName);
            Note("NetworkIDs: " + d.NetworkIDCollection.Count + " entries, " + vizvid + " in " + RootName + " (the SDK assigns the rest at build)");
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

        static string Describe(object o) { return o == null ? "null" : o is Object uo ? (uo == null ? "null" : uo.name) : o.ToString(); }

        /// <summary>UnityEvent (m_OnClick / m_OnEndEdit / m_OnValueChanged) が 1 本だけ、target.SendCustomEvent(固定の文字列 evt) で、その口が型に実在する。</summary>
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

        static Rect Box(Vector2 center, Vector2 size) { return new Rect(center - size * 0.5f, size); }

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
