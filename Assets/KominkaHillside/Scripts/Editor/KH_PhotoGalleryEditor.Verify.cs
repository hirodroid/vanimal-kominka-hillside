using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using Object = UnityEngine.Object;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-WLD-005 Photo/2 - Verify (読むだけ。シーンも資産も変えない)。Photo/1 の後でも Photo/4 の後でも通る。
    /// プロキシと backing (実行時に読まれる heap) の両方、cullingMask を裏山のレイヤー名ごと、端末ごとのメモリの計算値、任意の段 (ボード・ボタン・設定の行) を見る。
    /// 結果: Temp/kh-photo/verify.json と Editor.log の 1 行 (PHOTO_VERIFY_PASS / PHOTO_VERIFY_FAIL)。</summary>
    public static partial class KH_PhotoGalleryEditor
    {
        [Serializable] class VerifyReport
        {
            public string utc, scene, status;
            public int fails, warns;
            public float pcMiB, mobileMiB;
            public string cullingMask;
            public bool boardPlaced, headerButton, settingsRow;
            public List<string> lines = new List<string>();
        }

        [MenuItem(Menu + "2 - Verify")]
        public static void Verify()
        {
            var r = new VerifyReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            void Check(bool ok, string what) { r.lines.Add((ok ? "ok   " : "FAIL ") + what); if (!ok) r.fails++; }
            void Warn(string what) { r.lines.Add("WARN " + what); r.warns++; }
            void Note(string what) { r.lines.Add("     " + what); }
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode only");
                if (EditorSceneManager.GetActiveScene().path != KH_CourseGroundEditor.ScenePath) throw new InvalidOperationException("open " + KH_CourseGroundEditor.ScenePath);
                VerifyBody(r, Check, Warn, Note);
            }
            catch (Exception e) { Check(false, "verify stopped: " + e.Message); }
            r.status = r.fails == 0 ? "PASS" : "FAIL";
            WriteJson(OutDir + "/verify.json", r);
            string head = r.fails == 0 ? "PHOTO_VERIFY_PASS" : "PHOTO_VERIFY_FAIL";
            string msg = "[KH Photo] " + head + " fails=" + r.fails + " warns=" + r.warns + " memory PC " + r.pcMiB.ToString("F1") + " MiB / Quest・iOS " + r.mobileMiB.ToString("F1")
                + " MiB, board " + r.boardPlaced + ", header button " + r.headerButton + ", settings row " + r.settingsRow + " (" + OutDir + "/verify.json)"
                + (r.fails > 0 ? " first: " + string.Join(" | ", r.lines.Where(l => l.StartsWith("FAIL")).Take(4)) : "");
            if (r.fails == 0) Debug.Log(msg); else Debug.LogError(msg);
        }

        static void VerifyBody(VerifyReport r, Action<bool, string> Check, Action<string> Warn, Action<string> Note)
        {
            var c = Resolve();
            foreach (var n in c.notes) Note(n);
            Check(AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AlbumProgramPath + ".asset") != null, "program asset " + AlbumProgramPath + ".asset");
            var s = FindSystem(c);
            Check(s != null, SystemName + " at the scene root (Photo/1)");
            if (s == null)
            {
                for (int i = 0; i < Keys.Length; i++) Check(c.fetch[i].photoDirector == null && c.wander[i].photoDirector == null, "no photo system: dog " + Keys[i] + " hooks are null");
                Check(SceneRoot(c.scene, BoardRootName, out _) == null && c.pouch.photoPage == null, "no photo system: no board, no PhotoPage");
                return;
            }

            // ---------------------------------------------------------------- assets
            var cap = AssetDatabase.LoadAssetAtPath<RenderTexture>(CaptureRTPath);
            Check(cap != null && cap.width == TemplateWidth && cap.height == TemplateHeight && cap.depth >= CaptureDepthBits && cap.antiAliasing == 1 && !cap.useMipMap,
                "capture RT template " + TemplateWidth + "x" + TemplateHeight + " depth>=" + CaptureDepthBits + (cap == null ? " (missing)" : " (" + cap.width + "x" + cap.height + " depth " + cap.depth + " aa " + cap.antiAliasing + ")"));
            var slots = new RenderTexture[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                slots[i] = AssetDatabase.LoadAssetAtPath<RenderTexture>(SlotRTPath(i));
                var t = slots[i];
                Check(t != null && t.width == TemplateWidth && t.height == TemplateHeight && t.depth == 0 && t.antiAliasing == 1 && !t.useMipMap, "slot RT template " + i + " " + TemplateWidth + "x" + TemplateHeight + " depth 0");
                // the memory computation assumes 4 bytes per colour pixel: read it back from the real asset format
                Check(t != null && Mathf.Approximately(FormatBytes(t.format), ColorBytes), "slot RT template " + i + " format " + (t != null ? t.format.ToString() : "?") + " = " + ColorBytes + " bytes/px (the memory limit assumes it)");
            }
            Check(cap != null && Mathf.Approximately(FormatBytes(cap.format), ColorBytes), "capture RT colour format " + (cap != null ? cap.format.ToString() : "?") + " = " + ColorBytes + " bytes/px (+ depth counted as " + DepthBytesWorst + ")");
            for (int i = 0; i < PageSize; i++)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(PrintMatPath(i));
                Check(m != null && m.shader != null && m.shader.name == PrintShaderName && m.mainTexture == slots[i] && Mathf.Approximately(m.GetFloat("_AttachDistance"), AttachDistance)
                    && Mathf.Approximately(m.GetFloat("_AttachMargin"), AttachAimMargin) && (!m.HasProperty("_DebugProbe") || m.GetFloat("_DebugProbe") < 0.5f),
                    "board material " + i + " KD_PhotoPrint, slot " + i + " until a capture, attach " + AttachDistance + " / margin " + AttachAimMargin + ", debug probe off");
            }
            Note("board materials: " + PageSize + " (one per board position; the album rebinds them to the shown page's RT)");
            Check(GroupSizes.Sum() == SlotCount && GroupSizes.Length == KD_PhotoDirector.HillsideGroupCount, "categories " + string.Join(" / ", GroupNames.Select((n, g) => n + " " + GroupSizes[g])) + " = " + SlotCount + " slots, " + KD_PhotoDirector.HillsideGroupCount + " event groups");
            Check(AttachAimMargin * 2f < (PhotoPitchX - PhotoMm.x) * Mm && AttachAimMargin * 2f < (PhotoRow0Y - PhotoRow1Y - PhotoMm.y) * Mm, "aim margin < half the gap between photos (no two photos snap at once)");

            // ---------------------------------------------------------------- memory per platform (computed; measured on Quest / Android build later)
            r.pcMiB = BudgetMiB(PcWidth, PcHeight); r.mobileMiB = BudgetMiB(MobileWidth, MobileHeight);
            Check(r.pcMiB <= PcBudgetMiB, "PC memory " + r.pcMiB.ToString("F1") + " MiB <= " + PcBudgetMiB + " (" + SlotCount + " x " + PcWidth + "x" + PcHeight + " RGBA8 + capture colour + depth D32S8 worst case)");
            Check(r.mobileMiB <= MobileBudgetMiB, "Quest / iOS memory " + r.mobileMiB.ToString("F1") + " MiB <= " + MobileBudgetMiB + " (" + SlotCount + " x " + MobileWidth + "x" + MobileHeight + ")");
            Note("templates (" + TemplateWidth + "x" + TemplateHeight + ") are released by KD_PhotoSettings after the runtime copies exist and are never drawn before that (the photos are hidden until a capture)");

            // ---------------------------------------------------------------- camera
            var cam = s.camera;
            Check(cam != null && !cam.enabled, "photo camera exists and is disabled in the scene (only the capture frame enables it)");
            if (cam != null)
            {
                Check(cam.targetTexture == cap, "camera renders into the capture RT");
                Check(Mathf.Approximately(cam.fieldOfView, 50f) && Mathf.Approximately(cam.farClipPlane, 60f) && !cam.allowMSAA && !cam.allowHDR, "camera FOV 50 / far 60 / MSAA off / HDR off");
                Check(cam.GetComponent<AudioListener>() == null, "no AudioListener on the photo camera");
                r.cullingMask = "0x" + cam.cullingMask.ToString("X8") + " (" + DescribeMask(cam.cullingMask) + ")";
                Check(cam.cullingMask == PhotoCullingMask, "cullingMask == allow-list " + DescribeMask(PhotoCullingMask) + " (actual " + DescribeMask(cam.cullingMask) + ")");
                for (int i = 0; i < PhotoLayers.Length; i++) Check(LayerMask.LayerToName(PhotoLayers[i]) == PhotoLayerNames[i], "layer " + PhotoLayers[i] + " is '" + PhotoLayerNames[i] + "' in this project (actual '" + LayerMask.LayerToName(PhotoLayers[i]) + "')");
                foreach (int l in MustExcludeLayers) Check((cam.cullingMask & (1 << l)) == 0, "layer " + l + " (" + LayerMask.LayerToName(l) + ") is not photographed");
                // UI that must never be in a photo: every Canvas of the hand menu / hints / sleep placement on an excluded layer
                foreach (var cv in Object.FindObjectsOfType<Canvas>(true))
                {
                    if (cv.gameObject.scene != c.scene || !cv.isRootCanvas && !cv.overrideSorting) continue;
                    string path = KD_SceneObjects.PathOf(cv.gameObject);
                    bool shown = (cam.cullingMask & (1 << cv.gameObject.layer)) != 0;
                    bool personal = path.Contains("KD_TreatPouch") || path.Contains("KD_HintDirector") || path.Contains("KD_SleepPlacement") || path.Contains(SystemName);
                    if (personal) Check(!shown || cv.renderMode != RenderMode.WorldSpace && cv.renderMode != RenderMode.ScreenSpaceCamera, "personal UI not photographed: " + path + " (layer " + cv.gameObject.layer + ")");
                    else if (shown && cv.renderMode == RenderMode.WorldSpace) Note("world sign may appear in photos (allowed): " + path + " layer " + cv.gameObject.layer);
                }
                if (s.viewer != null && s.viewer.panelRoot != null)
                    foreach (var rr in s.viewer.panelRoot.GetComponentsInChildren<Renderer>(true)) Check((cam.cullingMask & (1 << rr.gameObject.layer)) == 0, "viewer renderer " + rr.name + " on a layer the photos skip");
                Check(c.pouch.photoPage != null && (cam.cullingMask & (1 << c.pouch.photoPage.layer)) == 0, "menu photo page layer " + (c.pouch.photoPage != null ? c.pouch.photoPage.layer.ToString() : "?") + " is not photographed");
            }

            // ---------------------------------------------------------------- behaviours: proxy + backing
            var cU = B(s.capture); var gU = B(s.gallery); var dU = B(s.director); var sU = B(s.settings); var vU = B(s.viewer); var aU = B(s.album);
            foreach (var pair in new (UdonSharpBehaviour b, UdonBehaviour u)[] { (s.capture, cU), (s.gallery, gU), (s.director, dU), (s.settings, sU), (s.viewer, vU), (s.album, aU) })
                Check(pair.b != null && pair.u != null && pair.u.SyncMethod == Networking.SyncType.None, (pair.b != null ? pair.b.GetType().Name : "?") + " present, backing present, sync None");
            if (s.capture == null || s.gallery == null || s.director == null || s.settings == null || s.viewer == null || s.album == null) return;

            Check(s.capture.photoCamera == cam && s.capture.captureTexture == cap && s.capture.settings == s.settings && s.capture.capturedListener == s.album, "capture: camera / RT / settings / listener = album");
            Heap(cU, "capturedListener", aU, Check, Warn); Heap(cU, "captureTexture", cap, Check, Warn); Heap(cU, "photoCamera", cam, Check, Warn);

            var groups = SlotGroups();
            Check(s.gallery.slotTextures != null && s.gallery.slotTextures.SequenceEqual(slots) && (s.gallery.boards == null || s.gallery.boards.Length == 0)
                && s.gallery.slotGroupCount == GroupSizes.Length && s.gallery.slotGroups != null && s.gallery.slotGroups.SequenceEqual(groups),
                "gallery: " + SlotCount + " slot templates in order, boards null (the album owns the board), " + GroupSizes.Length + " categories [" + string.Join(",", groups) + "]");
            Heap(gU, "slotTextures", slots, Check, Warn); Heap(gU, "slotGroupCount", GroupSizes.Length, Check, Warn); Heap(gU, "slotGroups", groups.Cast<object>().ToArray(), Check, Warn);

            var d = s.director;
            Check(d.photoCapture == s.capture && d.photoCamera == cam && d.gallery == s.gallery && d.bedroom == null, "director: capture / camera / gallery, no bedroom");
            Check(d.dogRoots != null && d.dogRoots.SequenceEqual(c.dogs) && d.dogHeadAnchors != null && d.dogHeadAnchors.SequenceEqual(c.heads)
                && d.fetchControllers != null && d.fetchControllers.SequenceEqual(c.fetch) && d.petControllers != null && d.petControllers.SequenceEqual(c.pet)
                && d.wanderControllers != null && d.wanderControllers.SequenceEqual(c.wander), "director: 6 dogs in roster order (" + string.Join(", ", Keys) + ") for roots / heads / fetch / pet / wander");
            Check(d.cameraBlockMask == CameraBlockMask && Mathf.Approximately(d.tickInterval, 0.1f) && d.frisbeeShotEnabled && Mathf.Approximately(d.frisbeeShotDelay, 0.1f) && Mathf.Approximately(d.frisbeeShotCooldown, 20f),
                "director: block mask Default+Environment, tick 0.1 s, frisbee 0.1 s / 20 s");
            Check(d.hillsideEventGroups, "director: event groups on (pet burst -> pet, other events -> play; IMP-WLD-007)");
            Heap(dU, "hillsideEventGroups", true, Check, Warn);
            Heap(dU, "tickInterval", 0.1f, Check, Warn); Heap(dU, "frisbeeShotCooldown", 20f, Check, Warn); Heap(dU, "gallery", gU, Check, Warn);
            Heap(dU, "wanderControllers", c.wander.Select(w => (object)B(w)).ToArray(), Check, Warn);
            // ---- IMP-WLD-011: the wash category, its photos and the shorter pet burst ----
            Check(KD_PhotoDirector.HillsideGroupWash == 3 && GroupNames[KD_PhotoDirector.HillsideGroupWash] == "wash" && GroupSizes[KD_PhotoDirector.HillsideGroupWash] == 6
                && GroupSizes[KD_PhotoDirector.HillsideGroupWalk] == 10 && GroupSizes[KD_PhotoDirector.HillsideGroupPet] == 4 && GroupSizes[KD_PhotoDirector.HillsideGroupPlay] == 4,
                "IMP-WLD-011 layout: walk 10 / pet 4 / play 4 / wash 6 (slots " + string.Join(",", groups.Select((g, i) => g == KD_PhotoDirector.HillsideGroupWash ? i.ToString() : null).Where(x => x != null)) + " = wash)");
            Check(d.petBurstLength == PetBurstLength, "director: pet burst " + PetBurstLength + " photos (actual " + d.petBurstLength + "; the pet category holds " + GroupSizes[KD_PhotoDirector.HillsideGroupPet] + ")");
            Heap(dU, "petBurstLength", PetBurstLength, Check, Warn);
            var fade = c.pouch.manager != null ? c.pouch.manager.travelFade : null;
            Check(d.washShotEnabled && Mathf.Approximately(d.washShotDoneDelay, WashDoneDelay) && d.washFade == fade && d.washSleepPlacement == c.pouch.sleepPlacement,
                "director: wash photos on, done +" + WashDoneDelay + " s, fade = KH_LeashManager.travelFade (" + (fade != null ? fade.name : "missing") + "), sleep placement = the pouch's (" + (c.pouch.sleepPlacement != null ? c.pouch.sleepPlacement.name : "missing") + ")");
            if (fade == null) Warn("no KH_TravelFade: wash photos are not paused during the travel fade");
            Heap(dU, "washShotEnabled", true, Check, Warn); Heap(dU, "washShotDoneDelay", WashDoneDelay, Check, Warn);
            if (fade != null) Heap(dU, "washFade", B(fade), Check, Warn);
            if (c.pouch.sleepPlacement != null) Heap(dU, "washSleepPlacement", B(c.pouch.sleepPlacement), Check, Warn);

            var st = s.settings;
            Check(!st.highEnabled && st.standardWidth == PcWidth && st.standardHeight == PcHeight && st.mobileWidth == MobileWidth && st.mobileHeight == MobileHeight,
                "settings: 2K off, PC " + PcWidth + "x" + PcHeight + ", Android/iOS " + MobileWidth + "x" + MobileHeight);
            Check(st.capture == s.capture && st.slots != null && st.slots.SequenceEqual(slots) && (st.boards == null || st.boards.Length == 0) && st.galleries != null && st.galleries.Length == 1 && st.galleries[0] == s.gallery
                && st.viewer == s.viewer && st.stateListener == s.album && st.language == c.language, "settings: capture / slots / galleries / viewer / language / listener = album, boards none (null or empty: a saved scene turns null into [] and KD_PhotoSettings treats both as none)");
            Check(st.stateLabel == null && st.highButton == null && st.standardButton == null && st.offButton == null && (st.boardButtons == null || st.boardButtons.Length == 0), "settings: none of the prequel's buttons (the album draws the row)");
            Heap(sU, "highEnabled", false, Check, Warn); Heap(sU, "mobileWidth", MobileWidth, Check, Warn); Heap(sU, "mobileHeight", MobileHeight, Check, Warn); Heap(sU, "stateListener", aU, Check, Warn);
            Note("PlayerData key " + KD_PhotoSettings.SaveKey + " (0 = OFF, 1 = standard; a saved 2 falls back to standard)");

            var v = s.viewer;
            Check(v.slotTextures != null && v.slotTextures.SequenceEqual(slots) && v.photoRenderer != null && v.panelRoot != null && !v.panelRoot.activeSelf && v.closeCollider != null && v.closeCollider.isTrigger && !v.closeCollider.enabled && v.pouch == c.pouch,
                "viewer: slots, photo quad, panel hidden, close trigger off, pouch (hides the touch launcher while open)");

            var al = s.album;
            Check(al.gallery == s.gallery && al.settings == s.settings && al.capture == s.capture && al.directorUdon == dU && al.language == c.language && al.viewerUdon == vU, "album: gallery / settings / capture / director backing / language / viewer backing");
            Heap(aU, "directorUdon", dU, Check, Warn); Heap(aU, "gallery", gU, Check, Warn);

            VerifyWalk(c, s, Check, Warn, Note);

            // ---------------------------------------------------------------- dog hooks
            for (int i = 0; i < Keys.Length; i++)
            {
                Check(c.fetch[i].photoDirector == d && c.wander[i].photoDirector == d, "dog " + i + " " + Keys[i] + ": fetch / wander -> director");
                Heap(B(c.fetch[i]), "photoDirector", dU, Check, Warn); Heap(B(c.wander[i]), "photoDirector", dU, Check, Warn);
                Check(c.frisbee[i].wander == c.wander[i] && c.frisbee[i].dogIndex == i, "dog " + i + " frisbee hook path: KH_FrisbeeCatch.wander.photoDirector (dogIndex " + i + ")");
            }
            // IMP-WLD-011: the wash photo path KH_DogWash.voiceDog.photoDirector (voiceDog is written by Wash/11, the director by Photo/1)
            var washes = WashesByDog(c);
            for (int i = 0; i < Keys.Length; i++)
            {
                var wsh = washes[i];
                Check(wsh != null && wsh.voiceDog == c.wander[i], "dog " + i + " " + Keys[i] + " wash photo path: one KH_DogWash (dogIndex " + i + ") whose voiceDog is its own KD_DogWanderController"
                    + (wsh == null ? " (no KH_DogWash: run Wash/11)" : wsh.voiceDog != c.wander[i] ? " (voiceDog " + (wsh.voiceDog != null ? wsh.voiceDog.name : "null") + ": run Wash/11)" : ""));
                if (wsh != null) Heap(B(wsh), "voiceDog", B(c.wander[i]), Check, Warn);
            }
            if (c.activities != null) { Check(c.activities.photos == d, "KD_GroupActivities.photos -> director (trainer group photo)"); Heap(B(c.activities), "photos", dU, Check, Warn); }
            else Warn("KD_GroupActivities not found: no group photo");

            // ---------------------------------------------------------------- menu page
            var p = c.pouch; var pU = c.pouchUdon; var page = p.photoPage;
            Check(page != null && page.name == PageName && page.transform.parent == p.settingsPage.transform && !page.activeSelf, "Page_Settings/" + PageName + " exists and is hidden");
            Check(p.extraSettingsPages != null && page != null && p.extraSettingsPages.Contains(page), "page registered in extraSettingsPages (proxy)");
            if (pU.publicVariables.TryGetVariableValue("extraSettingsPages", out object pagesObj)) Check(pagesObj is GameObject[] arr && arr.Contains(page), "page registered in extraSettingsPages (backing)");
            else Warn("pouch backing has no extraSettingsPages yet (heap populated on save / reload)");
            Check(p.photoAlbum == al, "pouch.photoAlbum = album"); Heap(pU, "photoAlbum", aU, Check, Warn); Heap(pU, "photoPage", page, Check, Warn);
            if (page != null)
            {
                Check(OnClick(page.transform.Find("Back"), pU, "_PhotoBack"), "page [back] -> _PhotoBack");
                Check(OnClick(page.transform.Find("Help"), aU, "_MenuHelpOpen"), "page [?] -> album _MenuHelpOpen");
                Check(OnClick(page.transform.Find("SettingsButton"), pU, "_PhotoToSettings"), "page [settings] -> _PhotoToSettings");
                Check(OnClick(page.transform.Find("Large/Close"), aU, "_MenuLargeClose") && OnClick(page.transform.Find("HelpPanel/Close"), aU, "_MenuHelpClose"), "large / help close buttons");
                for (int k = 0; k < PageSize; k++)
                {
                    var t = page.transform.Find("Thumb" + k);
                    var raw = t != null ? t.Find("Photo")?.GetComponent<RawImage>() : null;
                    Check(t != null && OnClick(t, aU, "_MenuPick" + k) && raw != null && al.menuThumbs != null && al.menuThumbs.Length == PageSize && al.menuThumbs[k] == raw && al.menuThumbButtons[k] == t.gameObject,
                        "thumbnail " + k + " -> _MenuPick" + k + " (position " + k + " of the shown page), wired to the album");
                }
                Check(page.transform.Find("Thumb" + PageSize) == null, "no stale thumbnail beyond " + PageSize + " on the page");
                // ---- IMP-WLD-007 W1: pager ◀ 1/3 ▶ ----
                var pager = page.transform.Find("Pager");
                Check(pager != null && !pager.gameObject.activeSelf && al.menuPager == pager.gameObject && al.menuPageLabel != null && al.menuPageLabel.transform.parent == pager
                    && OnClick(pager.Find("Prev"), aU, "_MenuPagePrev") && OnClick(pager.Find("Next"), aU, "_MenuPageNext"),
                    "menu pager: hidden until 9 photos, ◀ -> _MenuPagePrev, ▶ -> _MenuPageNext, label wired");
                if (pager != null)
                {
                    var pr = RectOf((RectTransform)pager); var sr = RectOf((RectTransform)page.transform.Find("SettingsButton"));
                    bool clear = !pr.Overlaps(sr);
                    for (int k = 0; k < PageSize && clear; k++) { var tt = page.transform.Find("Thumb" + k) as RectTransform; if (tt != null && pr.Overlaps(RectOf(tt))) clear = false; }
                    Check(clear && pr.xMax <= KD_TreatSetupEditor.PanelWidth * 0.5f, "menu pager " + pr + " overlaps neither the thumbnails nor [settings], inside the panel");
                    Check(pager.gameObject.layer == page.layer, "menu pager on the page's layer " + page.layer + " (not photographed)");
                }
                Heap(aU, "menuPager", pager != null ? pager.gameObject : null, Check, Warn);
                VerifyMenuSnap(c, s, page.transform, cam, Check, Warn, Note);
                VerifyThumbSnaps(s, page.transform, cam, Check, Warn, Note);
                Check(al.menuPage == page && al.menuLargeRoot != null && al.menuLarge != null && al.menuHelpRoot != null && al.menuState != null, "album: page / large / help / state");
                Check(page.transform.Find(RetiredNewMarkName) == null, "no " + RetiredNewMarkName + " on the page (the 'new' mark is retired, IMP-WLD-010; Photo/1 removes an old one)");
                var large = page.transform.Find("Large"); var help = page.transform.Find("HelpPanel");
                Check(large != null && help != null && large.GetSiblingIndex() > page.transform.Find("Thumb7").GetSiblingIndex() && help.GetSiblingIndex() > page.transform.Find("Thumb7").GetSiblingIndex(), "large view and help draw over the thumbnails");
            }

            // ---------------------------------------------------------------- language tables and glyphs
            foreach (var key in NeededKeys)
            {
                string want = KD_LocalizationEditor.Text(c.catalog, "ja", key);
                string got = c.language.keys != null && Array.IndexOf(c.language.keys, key) >= 0 ? c.language._TextForLanguage(key, "ja") : null;
                Check(got == want, "KD_Language has the current text of " + key + (got == null ? " (missing: run Photo/1 or Menu/11)" : ""));
            }
            var glyphs = new StringBuilder();
            foreach (var lang in c.catalog.languages)
                if (KD_LocalizationEditor.Bundled(lang)) foreach (var key in NeededKeys) glyphs.Append(System.Text.RegularExpressions.Regex.Replace(KD_LocalizationEditor.Text(c.catalog, lang.code, key), "<[^<>]+>", ""));
            string missing = KD_ShopSetupEditor.MissingGlyphs(glyphs.ToString());
            Check(missing.Length == 0, "shared font has every glyph of the photo texts (ja / en)" + (missing.Length > 0 ? ": missing " + missing + " (run Course/27)" : ""));
            string pagerMissing = KD_ShopSetupEditor.MissingGlyphs(PagerGlyphs);
            Check(pagerMissing.Length == 0, "shared font has the pager glyphs " + PagerGlyphs + (pagerMissing.Length > 0 ? ": missing " + pagerMissing + " (run Photo/1, which adds them)" : ""));

            // ---------------------------------------------------------------- optional surfaces
            var board = SceneRoot(c.scene, BoardRootName, out int boards);
            var button = p.panelRoot.transform.Find(KD_HandMenuScrollEditor.PhotoHeaderButtonName);
            var toggle = p.settingsHome.transform.Find(KD_MenuFinishEditor.PhotoToggleName);
            r.boardPlaced = board != null; r.headerButton = button != null; r.settingsRow = toggle != null;
            Check(boards <= 1, "at most one " + BoardRootName);
            Check(al.surfacesPlaced == (board != null || button != null), "album.surfacesPlaced (" + al.surfacesPlaced + ") matches the scene (board " + (board != null) + ", button " + (button != null) + "): false = no capture");
            Heap(aU, "surfacesPlaced", al.surfacesPlaced, Check, Warn);
            if (board == null && button == null && toggle == null) Note("optional surfaces not placed (Photo/4 not run or removed by Photo/5): photos are not taken");
            if (board != null) VerifyBoard(c, s, board.transform, cam, Check, Warn, Note);
            if (button != null)
            {
                Check(OnClick(button, pU, "_PhotoOpen"), "header [写真] -> _PhotoOpen");
                var bt = (RectTransform)button; var title = (RectTransform)p.sectionTitleLabel.transform;
                Check(Vector2.Distance(bt.anchoredPosition, HeaderButtonPos) < 0.5f && Vector2.Distance(bt.sizeDelta, HeaderButtonSize) < 0.5f, "header [写真] at " + HeaderButtonPos + " " + HeaderButtonSize);
                Check(Mathf.Abs(title.anchoredPosition.x - TitleWithButtonX) < 0.5f && Mathf.Abs(title.sizeDelta.x - TitleWithButtonWidth) < 0.5f, "title box " + TitleWithButtonWidth + " wide at x " + TitleWithButtonX + " (actual " + title.anchoredPosition.x + " / " + title.sizeDelta.x + ")");
                var points = p.panelRoot.transform.Find(KD_GiftCodeSetupEditor.PointsButtonName) as RectTransform;
                Check(!RectOf(bt).Overlaps(RectOf(title)) && (points == null || !RectOf(bt).Overlaps(RectOf(points))), "header [写真] overlaps neither the title nor the points");
                Check(al.headerLabel == LabelOf(button.gameObject), "album draws the header label");
                MeasureTitles(c, title, Warn, Note);
                MeasureOne(c, LabelOf(button.gameObject), "photo.menu", "", "header [写真]", Check, Note);
            }
            if (toggle != null)
            {
                Check(OnClick(toggle, aU, "_ToggleCapture"), "settings [写真: ON/OFF] -> album _ToggleCapture");
                Check(al.settingsToggleButton == toggle.GetComponent<Button>() && al.settingsToggleLabel == LabelOf(toggle.gameObject), "album draws the settings row");
                MeasureOne(c, LabelOf(toggle.gameObject), "photo.menu", ": OFF", "settings [写真: OFF]", Check, Note);
                Note("Verify 22 (Menu Finish) counts " + KD_MenuFinishEditor.PhotoToggleName + " in the group 'self' and checks it overlaps no other control");
            }
            if (page != null)
            {
                foreach (var key in new[] { "photo.empty", "photo.settings.off", "photo.settings.loading", "photo.settings.failed" }) MeasureOne(c, al.menuState, key, "", "menu state " + key, Check, Note);
                MeasureOne(c, al.menuHelpBody, "photo.help.body", "", "menu help body", Check, Note);
            }
        }

        /// <summary>IMP-WLD-007 W3: 大きく出した写真の吸着 (Quad 1 枚)。材質の値、大きさと位置、[とじる] と重ならない、レイヤー (撮影カメラは写さない)、
        /// 手元メニューのスケールが一様 (シェーダーの狙いの判定は親にせん断が無いことが前提)、album の配線。VRChat カメラが 17 を描くかはユーザーの確認。</summary>
        static void VerifyMenuSnap(Ctx c, Sys s, Transform page, Camera cam, Action<bool, string> Check, Action<string> Warn, Action<string> Note)
        {
            var large = page.Find("Large");
            var photo = large != null ? large.Find("Photo") as RectTransform : null;
            var close = large != null ? large.Find("Close") as RectTransform : null;
            var q = large != null ? large.Find(MenuSnapName) : null;
            var mr = q != null ? q.GetComponent<MeshRenderer>() : null;
            var m = mr != null ? mr.sharedMaterial : null;
            Check(photo != null && Vector2.Distance(photo.anchoredPosition, LargePhotoPos) < 0.5f && Vector2.Distance(photo.sizeDelta, LargePhotoSize) < 0.5f,
                "enlarged photo " + LargePhotoSize + " at " + LargePhotoPos);
            Check(photo != null && close != null && !RectOf(photo).Overlaps(RectOf(close)), "enlarged photo " + (photo != null ? RectOf(photo).ToString() : "?") + " does not overlap [とじる] " + (close != null ? RectOf(close).ToString() : "?"));
            var bd = large != null ? large.Find("Backdrop") as RectTransform : null;
            if (photo != null && bd != null) { var pr = RectOf(photo); var br = RectOf(bd); Check(br.Contains(pr.min) && br.Contains(pr.max), "the enlarged photo sits on its backdrop"); }
            Check(q != null && mr != null && q.GetComponents<Collider>().Length == 0 && mr.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off, MenuSnapName + " under Large: MeshRenderer, no collider, no shadow");
            if (q == null || mr == null) return;
            Check(q.GetComponent<MeshFilter>() != null && q.GetComponent<MeshFilter>().sharedMesh != null && q.GetComponent<MeshFilter>().sharedMesh.name == "Quad", "snap uses Unity's Quad (vertices at +-0.5, the shader's assumption)");
            Check(m != null && m.shader != null && m.shader.name == PrintShaderName && AssetDatabase.GetAssetPath(m) == MenuSnapMatPath
                && Mathf.Approximately(m.GetFloat("_AttachDistance"), MenuAttachDistance) && Mathf.Approximately(m.GetFloat("_AttachMargin"), MenuAttachMargin)
                && (!m.HasProperty("_DebugProbe") || m.GetFloat("_DebugProbe") < 0.5f) && (!m.HasProperty("_HideFromEyes") || m.GetFloat("_HideFromEyes") < 0.5f),
                "snap material " + MenuSnapMatPath + ": KD_PhotoPrint, attach " + MenuAttachDistance + " m, margin " + MenuAttachMargin + " m, debug probe off");
            float pxToM = Mathf.Abs(large.lossyScale.x);
            Check(Vector2.Distance(new Vector2(q.localPosition.x, q.localPosition.y), LargePhotoPos) < 0.5f && Mathf.Abs(q.localScale.x - LargePhotoSize.x) < 0.5f && Mathf.Abs(q.localScale.y - LargePhotoSize.y) < 0.5f
                && q.localRotation == Quaternion.identity && pxToM > 0f && Mathf.Abs(-q.localPosition.z * pxToM - MenuSnapFront) < 0.0005f,
                "snap quad covers the enlarged photo exactly, " + (pxToM > 0f ? (-q.localPosition.z * pxToM * 1000f).ToString("F1") : "?") + " mm in front of it");
            Check(q.gameObject.layer == MenuSnapLayer, "snap quad on layer " + MenuSnapLayer + " (" + LayerMask.LayerToName(MenuSnapLayer) + "; the builder constant MenuSnapLayer)");
            Check(cam == null || (cam.cullingMask & (1 << q.gameObject.layer)) == 0, "our photo camera does not render the snap quad's layer");
            // the shader takes the photo's axes from the object matrix and needs them orthogonal (no shear from a non-uniform, rotated parent)
            var mtx = q.localToWorldMatrix;
            Vector3 ax = ((Vector3)mtx.GetColumn(0)).normalized, ay = ((Vector3)mtx.GetColumn(1)).normalized, az = ((Vector3)mtx.GetColumn(2)).normalized;
            float shear = Mathf.Max(Mathf.Abs(Vector3.Dot(ax, ay)), Mathf.Max(Mathf.Abs(Vector3.Dot(ax, az)), Mathf.Abs(Vector3.Dot(ay, az))));
            Check(shear < 0.001f, "the snap quad's world axes are orthogonal (max |cos| " + shear.ToString("F5") + "): the hand menu above it adds no shear");
            var pl = large.lossyScale;
            Check(Mathf.Abs(pl.x - pl.y) <= 0.001f * Mathf.Max(Mathf.Abs(pl.x), 1e-6f), "the hand menu's scale is uniform in its plane at the enlarged photo (lossy " + pl.ToString("F5") + ")");
            Note("KD_TreatPouch scales the menu by eye height at runtime with one number (_scale): it stays uniform");
            Check(s.album.menuSnapPhoto == mr, "album.menuSnapPhoto = the snap quad (ShowLarge binds the photo, phones not in VR turn the snap off)");
            Heap(B(s.album), "menuSnapPhoto", mr, Check, Warn);
            Check(!RectOf(close).Overlaps(new Rect(LargePhotoPos - LargePhotoSize * 0.5f, LargePhotoSize)), "the snap quad does not hide [とじる]");
            Note("OPEN (user): does the VRChat camera render layer " + MenuSnapLayer + "? If not, change MenuSnapLayer (KH_PhotoGalleryEditor.cs) and run Photo/1");
        }

        /// <summary>IMP-WLD-009: 縮小表示 8 枚の吸着の板。位置・大きさ (縮小表示の写真ちょうど、2 mm 手前)、レイヤー、材質の値 (メニュー専用の 8 つ)、Collider なし、
        /// 8 枚が同じ平面 (シェーダーの排他の前提)、余白 × 2 < 写真の隙間 (隣が同時に吸着しない)、せん断なし・一様なスケール、ページ送りなどのボタンと重ならない、album の配線。</summary>
        static void VerifyThumbSnaps(Sys s, Transform page, Camera cam, Action<bool, string> Check, Action<string> Warn, Action<string> Note)
        {
            var al = s.album;
            float pxToM = Mathf.Abs(page.lossyScale.x);
            var quads = new Transform[PageSize]; var renderers = new Renderer[PageSize];
            var boardMats = Enumerable.Range(0, PageSize).Select(i => AssetDatabase.LoadAssetAtPath<Material>(PrintMatPath(i))).Where(x => x != null).ToArray();
            for (int k = 0; k < PageSize; k++)
            {
                var thumb = page.Find("Thumb" + k) as RectTransform;
                var q = thumb != null ? thumb.Find(MenuSnapName) : null;
                var mr = q != null ? q.GetComponent<MeshRenderer>() : null;
                var mf = q != null ? q.GetComponent<MeshFilter>() : null;
                quads[k] = q; renderers[k] = mr;
                string what = "thumbnail " + k + " snap";
                Check(q != null && mr != null && mf != null && mf.sharedMesh != null && mf.sharedMesh.name == "Quad" && q.GetComponents<Collider>().Length == 0
                    && mr.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off && !mr.receiveShadows,
                    what + ": Thumb" + k + "/" + MenuSnapName + ", Unity Quad, no collider (the button's raycast stays free), no shadow" + (q == null ? " (missing: run Photo/1)" : ""));
                if (q == null || mr == null) continue;
                Check(!q.gameObject.activeSelf, what + " hidden in the scene (the album shows it under a photo)");
                var m = mr.sharedMaterial;
                Check(m != null && m.shader != null && m.shader.name == PrintShaderName && AssetDatabase.GetAssetPath(m) == MenuThumbMatPath(k) && !boardMats.Contains(m)
                    && Mathf.Approximately(m.GetFloat("_AttachDistance"), MenuThumbAttachDistance) && Mathf.Approximately(m.GetFloat("_AttachMargin"), MenuThumbAttachMargin)
                    && (!m.HasProperty("_DebugProbe") || m.GetFloat("_DebugProbe") < 0.5f) && (!m.HasProperty("_HideFromEyes") || m.GetFloat("_HideFromEyes") < 0.5f),
                    what + " material " + MenuThumbMatPath(k) + " (not a board material): KD_PhotoPrint, attach " + MenuThumbAttachDistance + " m, margin " + MenuThumbAttachMargin + " m, debug probe off");
                Check(Vector2.Distance(new Vector2(q.localPosition.x, q.localPosition.y), Vector2.zero) < 0.5f && Mathf.Abs(q.localScale.x - ThumbSize.x) < 0.5f && Mathf.Abs(q.localScale.y - ThumbSize.y) < 0.5f
                    && q.localRotation == Quaternion.identity && pxToM > 0f && Mathf.Abs(-q.localPosition.z * pxToM - MenuSnapFront) < 0.0005f,
                    what + " covers the thumbnail photo " + ThumbSize + " exactly, " + (pxToM > 0f ? (-q.localPosition.z * pxToM * 1000f).ToString("F1") : "?") + " mm in front of it");
                var photo = thumb.Find("Photo") as RectTransform;
                Check(photo != null && Vector2.Distance(photo.anchoredPosition, Vector2.zero) < 0.5f && Vector2.Distance(photo.sizeDelta, ThumbSize) < 0.5f
                    && thumb.sizeDelta.x >= ThumbSize.x && thumb.sizeDelta.y >= ThumbSize.y, what + ": the photo under it is " + ThumbSize + " at the button's centre, inside the frame");
                Check(q.gameObject.layer == MenuSnapLayer, what + " on layer " + MenuSnapLayer + " (" + LayerMask.LayerToName(MenuSnapLayer) + ", MenuSnapLayer)");
                Check(cam == null || (cam.cullingMask & (1 << q.gameObject.layer)) == 0, what + ": our photo camera does not render its layer");
                var mtx = q.localToWorldMatrix;
                Vector3 ax = ((Vector3)mtx.GetColumn(0)).normalized, ay = ((Vector3)mtx.GetColumn(1)).normalized, az = ((Vector3)mtx.GetColumn(2)).normalized;
                float shear = Mathf.Max(Mathf.Abs(Vector3.Dot(ax, ay)), Mathf.Max(Mathf.Abs(Vector3.Dot(ax, az)), Mathf.Abs(Vector3.Dot(ay, az))));
                var pl = thumb.lossyScale;
                Check(shear < 0.001f && Mathf.Abs(pl.x - pl.y) <= 0.001f * Mathf.Max(Mathf.Abs(pl.x), 1e-6f), what + ": world axes orthogonal (max |cos| " + shear.ToString("F5") + "), the menu's scale uniform in its plane (lossy " + pl.ToString("F5") + ")");
                // ---- the quad's rect in page px must leave the controls next to the list alone (the quad writes depth and would hide them) ----
                var qr = new Rect(thumb.anchoredPosition - ThumbSize * 0.5f, ThumbSize);
                var blocked = new List<string>();
                foreach (var name in new[] { "Pager", "SettingsButton", "Back", "Help", "State" })
                {
                    var other = page.Find(name) as RectTransform;
                    if (other != null && qr.Overlaps(RectOf(other))) blocked.Add(name + " " + RectOf(other));
                }
                Check(blocked.Count == 0, what + " " + qr + " overlaps none of pager / [settings] / [back] / [?] / state" + (blocked.Count > 0 ? ": " + string.Join(", ", blocked) : ""));
            }
            // ---- one plane, and the aim margin keeps neighbours apart ----
            var present = quads.Where(x => x != null).ToArray();
            if (present.Length == PageSize)
            {
                Vector3 n = quads[0].forward; float off = 0f;
                for (int k = 1; k < PageSize; k++) off = Mathf.Max(off, Mathf.Abs(Vector3.Dot(quads[k].position - quads[0].position, n)));
                Check(off < 0.0001f, "the 8 thumbnail snaps lie in one plane (max offset " + (off * 1000f).ToString("F3") + " mm): the shader's hit point lands in one photo at most");
            }
            float gapX = ThumbGapX * pxToM, gapY = ThumbGapY * pxToM;
            Check(pxToM > 0f && MenuThumbAttachMargin * 2f < gapX && MenuThumbAttachMargin * 2f < gapY,
                "thumbnail aim margin " + (MenuThumbAttachMargin * 1000f).ToString("F1") + " mm x 2 < the gap between photos (" + (gapX * 1000f).ToString("F1") + " x " + (gapY * 1000f).ToString("F1") + " mm): no two neighbours snap at once");
            Check(MenuThumbAttachMargin >= 0f, "thumbnail aim test on (margin >= 0; negative would snap every photo within reach)");
            // ---- the album ----
            Check(al.menuThumbSnaps != null && al.menuThumbSnaps.Length == PageSize && Enumerable.Range(0, PageSize).All(k => al.menuThumbSnaps[k] == renderers[k] && renderers[k] != null),
                "album.menuThumbSnaps = the 8 snaps in position order (Rebind binds the shown page, the large view / help hide them, phones not in VR turn them off)");
            Heap(B(al), "menuThumbSnaps", renderers.Cast<object>().ToArray(), Check, Warn);
            Note("thumbnail snaps: attach " + MenuThumbAttachDistance + " m (enlarged photo " + MenuAttachDistance + " m, board " + AttachDistance + " m); the real reach and aim are a user check with the VRChat camera (PCVR / Desktop / Quest)");
        }

        /// <summary>IMP-WLD-007 W2: 散歩の見回りの配線 (proxy + backing)、写真の場所 (NavMesh・地面)、試作値の表、つなぎ外しの知らせ。</summary>
        static void VerifyWalk(Ctx c, Sys s, Action<bool, string> Check, Action<string> Warn, Action<string> Note)
        {
            foreach (var path in OwnPrograms) Check(AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path + ".asset") != null, "program asset " + path + ".asset (Photo/0)");
            var w = s.walk; var wU = B(w);
            Check(w != null && wU != null && wU.SyncMethod == Networking.SyncType.None, "KH_PhotoWalkWatcher on " + SystemName + ", backing present, sync None (Photo/1)");
            if (w == null || wU == null) return;
            var m = c.pouch.manager; var mU = B(m);
            Check(w.director == s.director && w.settings == s.settings && w.album == s.album && w.manager == m && w.sleepPlacement == c.pouch.sleepPlacement
                && w.wanderControllers != null && w.wanderControllers.SequenceEqual(c.wander), "walk watcher: director / settings / album / leash manager / sleep placement / 6 wander controllers in roster order");
            Heap(wU, "director", B(s.director), Check, Warn); Heap(wU, "album", B(s.album), Check, Warn); Heap(wU, "manager", mU, Check, Warn);
            Heap(wU, "wanderControllers", c.wander.Select(x => (object)B(x)).ToArray(), Check, Warn);
            Check(m != null && m.photoWalk == w, "KH_LeashManager.photoWalk -> the walk watcher (local own-dog notice)");
            if (mU != null) Heap(mU, "photoWalk", wU, Check, Warn);
            // ---- tuning table ----
            Check(Mathf.Approximately(w.tickSeconds, WalkTick) && Mathf.Approximately(w.stopoverDelay, WalkStopoverDelay) && Mathf.Approximately(w.timeInterval, WalkTimeInterval)
                && Mathf.Approximately(w.distanceInterval, WalkDistanceInterval) && Mathf.Approximately(w.minGap, WalkMinGap) && Mathf.Approximately(w.fadeGrace, WalkFadeGrace)
                && Mathf.Approximately(w.spotRepeat, WalkSpotRepeat) && Mathf.Approximately(w.jumpMeters, WalkJumpMeters) && Mathf.Approximately(w.spotHeight, WalkSpotHeight),
                "walk watcher tuning: tick " + WalkTick + " s, stopover +" + WalkStopoverDelay + " s, " + WalkTimeInterval + " s and " + WalkDistanceInterval + " m, gap " + WalkMinGap + " s, fade +" + WalkFadeGrace + " s, spot every " + WalkSpotRepeat + " s, jump " + WalkJumpMeters + " m");
            Heap(wU, "timeInterval", WalkTimeInterval, Check, Warn); Heap(wU, "minGap", WalkMinGap, Check, Warn);
            var d = s.director;
            Check(Mathf.Approximately(d.walkDogEyeFov, DogEyeFov) && Mathf.Approximately(d.walkDogEyePitch, DogEyePitch) && Mathf.Approximately(d.walkDogEyeAheadScale, DogEyeAheadScale)
                && Mathf.Approximately(d.walkDogEyeAheadMin, DogEyeAheadMin) && Mathf.Approximately(d.walkDogEyeAheadMax, DogEyeAheadMax) && Mathf.Approximately(d.walkDogEyeLift, DogEyeLift)
                && Mathf.Approximately(d.walkDogEyeClearAhead, DogEyeClearAhead) && Mathf.Approximately(d.walkDogEyeGroundProbe, DogEyeGroundProbe) && Mathf.Approximately(d.walkDogEyeGroundBelow, DogEyeGroundBelow),
                "dog's-eye: FOV " + DogEyeFov + ", +" + DogEyePitch + "°, ahead clamp(size x " + DogEyeAheadScale + ", " + DogEyeAheadMin + ", " + DogEyeAheadMax + ") +" + DogEyeLift + " up, clear " + DogEyeClearAhead + " m, ground " + DogEyeGroundBelow + " m at " + DogEyeGroundProbe + " m");
            Check(Mathf.Approximately(d.walkSelfieFov, SelfieFov) && Mathf.Approximately(d.walkSelfieReach, SelfieReach) && Mathf.Approximately(d.walkSelfieLift, SelfieLift)
                && Mathf.Approximately(d.walkSelfieLookToDogs, SelfieLookToDogs) && Mathf.Approximately(d.walkSelfieInner, SelfieInner) && Mathf.Approximately(d.walkSelfieDogMax, SelfieDogMax),
                "selfie: " + SelfieReach + " m, +" + SelfieLift + " m, FOV " + SelfieFov + ", look " + SelfieLookToDogs + " toward the dogs, inner " + SelfieInner + ", dogs within " + SelfieDogMax + " m");
            var dU = B(d);
            Heap(dU, "walkDogEyeFov", DogEyeFov, Check, Warn); Heap(dU, "walkSelfieReach", SelfieReach, Check, Warn);
            Check(Mathf.Approximately(s.camera != null ? s.camera.fieldOfView : 0f, 50f), "the scene camera stays at FOV 50 (walk shots set 70 / 60 per shot and every other shot resets to 50)");
            // ---- spots ----
            WalkSpots want = null;
            try { want = ResolveWalkSpots(m); } catch (Exception e) { Check(false, "walk spots resolvable: " + e.Message); }
            int n = WalkSpotNames.Length;
            Check(w.spotNames != null && w.spotNames.Length == n && w.spotPositions != null && w.spotPositions.Length == n && w.spotRadii != null && w.spotRadii.Length == n
                && w.spotHasBackdrop != null && w.spotHasBackdrop.Length == n && w.spotBackdrops != null && w.spotBackdrops.Length == n, n + " walk photo spots, arrays aligned");
            if (w.spotPositions == null || w.spotPositions.Length != n || w.spotNames == null || w.spotNames.Length != n) return;
            Check(w.spotHasBackdrop.Any(x => x), "at least one spot has the farmhouse backdrop");
            for (int k = 0; k < n; k++)
            {
                var p = w.spotPositions[k];
                if (want != null) Check(w.spotNames[k] == want.names[k] && Vector3.Distance(p, want.positions[k]) < 0.05f && w.spotHasBackdrop[k] == want.hasBackdrop[k]
                    && (!want.hasBackdrop[k] || Vector3.Distance(w.spotBackdrops[k], want.backdrops[k]) < 0.05f) && Mathf.Approximately(w.spotRadii[k], WalkSpotRadius),
                    "spot " + w.spotNames[k] + " = the scene's place " + V(p) + " r " + WalkSpotRadius + " (run Photo/1 if the place moved)");
                bool nav = UnityEngine.AI.NavMesh.SamplePosition(p, out UnityEngine.AI.NavMeshHit hit, 2f, UnityEngine.AI.NavMesh.AllAreas);
                Check(nav, "spot " + w.spotNames[k] + " is on the NavMesh within 2 m" + (nav ? " (" + Vector3.Distance(p, hit.position).ToString("F2") + " m)" : ""));
                bool ground = Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit gh, 6f, ~0, QueryTriggerInteraction.Ignore);
                Check(ground, "spot " + w.spotNames[k] + " has ground within 3 m" + (ground ? " (" + (p.y - gh.point.y).ToString("F2") + " m above " + gh.collider.name + ")" : ""));
                for (int j = 0; j < k; j++)
                    Check(Vector3.Distance(p, w.spotPositions[j]) > 2f * WalkSpotRadius, "spots " + w.spotNames[j] + " / " + w.spotNames[k] + " do not overlap");
            }
            Note("walk triggers are local (no synced variable): stopover pose start (KD_DogWanderController.PoseSpotIndex), spot arrival, time + distance; category walk (" + GroupSizes[0] + " slots)");
        }

        static void VerifyBoard(Ctx c, Sys s, Transform board, Camera cam, Action<bool, string> Check, Action<string> Warn, Action<string> Note)
        {
            var al = s.album;
            Check(board.GetComponentsInChildren<Collider>(true).All(x => x.isTrigger), "board: only trigger colliders (nobody bumps into it; Guide/2's solid check is not affected)");
            var st = board.Find("Structure");
            var frameRenderers=st==null?new MeshRenderer[0]:st.GetComponentsInChildren<MeshRenderer>(true);
            bool finishedFrame=KH_PropFinishEditor.Ready;
            Check(st != null && st.GetComponentsInChildren<Collider>(true).Length == 0 && frameRenderers.Length == (finishedFrame?7:6)
                && frameRenderers.All(r=>r.enabled==(!finishedFrame || r.name=="FinishedVisual")), "board frame: expected wood visuals, no collider");
            var canvas = board.Find("Canvas");
            Check(canvas != null && canvas.GetComponent<GraphicRaycaster>() == null && canvas.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null && canvas.GetComponent<Canvas>() != null && canvas.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace,
                "board canvas: world space, read-only (no raycaster / UI shape)");
            var photos = board.Find("Photos");
            // BUG-ENV-005: photos flush with the canvas, no Graphic over a photo within 30 mm (the backings are BoardBackingMm behind)
            if (canvas != null && photos != null)
            {
                var over = new List<string>();
                foreach (var g in canvas.GetComponentsInChildren<Graphic>(true))
                {
                    var wc = new Vector3[4]; g.rectTransform.GetWorldCorners(wc);
                    var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var hi = -lo;
                    foreach (var w in wc) { var p = canvas.InverseTransformPoint(w); lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
                    if (lo.z > 30f) continue;
                    for (int k = 0; k < PageSize; k++)
                    {
                        var cp = CellPos(k);
                        float ox = Mathf.Min(hi.x, cp.x + PhotoMm.x * 0.5f) - Mathf.Max(lo.x, cp.x - PhotoMm.x * 0.5f), oy = Mathf.Min(hi.y, cp.y + PhotoMm.y * 0.5f) - Mathf.Max(lo.y, cp.y - PhotoMm.y * 0.5f);
                        if (ox > 0.5f && oy > 0.5f) over.Add(g.name + " over photo " + k);
                    }
                }
                bool flush = Enumerable.Range(0, PageSize).All(k => photos.Find("Photo" + k) != null && Mathf.Abs(photos.Find("Photo" + k).localPosition.z - PhotoFrontZ) < 1e-5f);
                Check(over.Count == 0 && flush, "board photos in the canvas plane (z " + PhotoFrontZ + "), no Graphic over a photo (BUG-ENV-005)" + (over.Count > 0 ? ": " + string.Join(", ", over.Take(6)) : "") + (flush ? "" : "; photo z off: run Photo/4 again"));
            }
            Check(photos != null && Mathf.Approximately(photos.lossyScale.x, photos.lossyScale.y) && Mathf.Approximately(photos.lossyScale.y, photos.lossyScale.z), "photos' parent has a uniform scale (the snap shader's aim test)");
            for (int k = 0; k < PageSize; k++)
            {
                var q = photos != null ? photos.Find("Photo" + k) : null;
                var tap = q != null ? q.GetComponent<KD_PhotoBoard>() : null;
                var mr = q != null ? q.GetComponent<MeshRenderer>() : null;
                var box = q != null ? q.GetComponent<BoxCollider>() : null;
                Check(q != null && !q.gameObject.activeSelf && tap != null && tap.viewer == s.viewer && mr != null && mr.sharedMaterial == AssetDatabase.LoadAssetAtPath<Material>(PrintMatPath(k)) && box != null && box.isTrigger
                    && q.gameObject.layer == 0 && (cam == null || (cam.cullingMask & 1) != 0),
                    "board photo " + k + ": hidden until a capture, KD_PhotoBoard -> viewer, material " + k + ", trigger collider");
                if (tap != null) Check(B(tap) != null && B(tap).SyncMethod == Networking.SyncType.None, "board photo " + k + " backing, sync None");
                Check(al.boardPhotos != null && al.boardPhotos.Length == PageSize && al.boardPhotos[k] == mr && al.boardTaps[k] == tap, "album board position " + k + " wired (" + (k < 4 ? "top" : "bottom") + " row of the shown page)");
            }
            // ---- IMP-WLD-007 W1: the board pager (its own pressable canvas; the photo canvas stays read-only) ----
            var pager = board.Find(BoardPagerName);
            var pc = pager != null ? pager.GetComponent<Canvas>() : null;
            var pbox = pager != null ? pager.GetComponent<BoxCollider>() : null;
            Check(pager != null && pc != null && pc.renderMode == RenderMode.WorldSpace && pager.gameObject.layer == 0 && pager.GetComponent<GraphicRaycaster>() != null
                && pager.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null && pbox != null && pbox.isTrigger,
                "board pager: World Space Canvas on Default + GraphicRaycaster + VRCUiShape + trigger BoxCollider" + (pager == null ? " (missing: run Photo/4 again)" : ""));
            if (pager != null)
            {
                Check(!pager.gameObject.activeSelf && al.boardPager == pager.gameObject && al.boardPageLabel != null && al.boardPageLabel.transform.parent == pager, "board pager hidden until 9 photos, wired to the album");
                Check(OnClick(pager.Find("Prev"), B(s.album), "_BoardPagePrev") && OnClick(pager.Find("Next"), B(s.album), "_BoardPageNext"), "board ◀ -> _BoardPagePrev, ▶ -> _BoardPageNext");
                Check(pager.GetComponentsInChildren<Transform>(true).All(x => x.gameObject.layer == 0), "board pager parts all on Default");
                // inside the canvas strip under the bottom row, in front of the paper
                float bottomCell = PhotoRow1Y - PhotoMm.y * 0.5f - CellBorderMm, top = BoardPagerY + BoardPagerMm.y * 0.5f, bottom = BoardPagerY - BoardPagerMm.y * 0.5f;
                Check(top <= bottomCell && bottom >= -BoardCanvasMm.y * 0.5f, "board pager (" + bottom + ".." + top + " mm) between the bottom row (" + bottomCell + ") and the canvas edge (" + (-BoardCanvasMm.y * 0.5f) + ")");
                Check(Mathf.Approximately(s.album.boardPageResetSeconds, 60f), "board returns to page 1 after 60 s (" + s.album.boardPageResetSeconds + ")");
            }
            Check(al.boardCaption != null && al.boardState != null, "album draws the board caption and state");
            var broken = Rules(c, board.position, board.eulerAngles.y, board);
            Check(broken.Count == 0, "board placement rules at " + V(board.position) + " yaw " + board.eulerAngles.y.ToString("F1") + (broken.Count > 0 ? ": " + string.Join("; ", broken) : ""));
            if (al.boardCaption != null) MeasureOne(c, al.boardCaption, "gallery.caption", "", "board caption", Check, Note);
            if (al.boardState != null) foreach (var key in new[] { "photo.empty", "photo.settings.off" }) MeasureOne(c, al.boardState, key, "", "board state " + key, Check, Note);
        }

        // ---------------------------------------------------------------- text fit (vrc-world-ui §10: GetPreferredValues with the box width; autosize at its minimum)

        static void MeasureOne(Ctx c, TMP_Text target, string key, string suffix, string what, Action<bool, string> Check, Action<string> Note)
        {
            if (target == null) { Check(false, what + ": label missing"); return; }
            var go = new GameObject("PhotoMeasure", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var sample = go.AddComponent<TextMeshProUGUI>();
                var bad = new List<string>(); int shrink = 0;
                foreach (var lang in c.catalog.languages)
                {
                    if (!KD_LocalizationEditor.Bundled(lang)) continue;   // client languages are drawn by VRChat's own fallback font (device check)
                    string text = KD_LocalizationEditor.Text(c.catalog, lang.code, key) + suffix;
                    int fit = KD_LocalizationEditor.MeasureLabelFit(sample, target, text, out Vector2 pref);
                    if (fit == 2) bad.Add(lang.code + " " + pref.x.ToString("F0") + "x" + pref.y.ToString("F0") + " in " + target.rectTransform.rect.size);
                    if (fit == 1) shrink++;
                }
                Check(bad.Count == 0, what + " fits (ja / en)" + (bad.Count > 0 ? ": " + string.Join(", ", bad) : "") + (shrink > 0 ? " (" + shrink + " shrink)" : ""));
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>設計 §4.2: 見出しは 400 に縮む。全タブ・副画面の見出し × 自前で焼く言語 (ja / en) で 1 行に入るか。入らなければボタンを 100 幅にする判断材料 (WARN)。</summary>
        static void MeasureTitles(Ctx c, RectTransform title, Action<string> Warn, Action<string> Note)
        {
            var label = title.GetComponent<TextMeshProUGUI>();
            if (label == null) return;
            string[] keys = { "welcome.title", "KD_TreatPouch.WordSettings", "KD_DistanceFadeSettings.Title", "KD_TreatPouch.WordVolumeSettings", "KD_TreatPouch.WordToyTitle", "KD_TreatPouch.WordWalkTitle",
                "KD_TreatPouch.WordSleepTitle", "KD_TreatPouch.WordAccTitle", "activities.title", "KD_TreatPouch.WordTitle", "gift.title", "information.title", "information.ai.title",
                "information.vanimal.title", "information.trainer.title", "welcome.reread.title", "ui.extra.022", "photo.menu" };
            var go = new GameObject("PhotoTitleMeasure", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var sample = go.AddComponent<TextMeshProUGUI>();
                var over = new List<string>(); int checkedCount = 0;
                foreach (var lang in c.catalog.languages)
                {
                    if (!KD_LocalizationEditor.Bundled(lang)) continue;
                    foreach (var key in keys)
                    {
                        if (!c.catalog.languages.First(l => l.code == c.catalog.fallbackLanguage).texts.Any(e => e.key == key)) continue;
                        checkedCount++;
                        int fit = KD_LocalizationEditor.MeasureLabelFit(sample, label, KD_LocalizationEditor.Text(c.catalog, lang.code, key), out Vector2 pref);
                        if (fit == 2) over.Add(lang.code + ":" + key + " " + pref.x.ToString("F0"));
                    }
                }
                if (over.Count > 0) Warn("TITLE_OVERFLOW at 400 wide (design §4.2: then make [写真] 100 wide): " + string.Join(", ", over));
                else Note("section titles fit 400 wide (" + checkedCount + " ja / en titles; the course titles and the 11 client languages are a device check)");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---------------------------------------------------------------- helpers

        static UdonBehaviour B(UdonSharpBehaviour b) { return b != null ? UdonSharpEditorUtility.GetBackingUdonBehaviour(b) : null; }

        /// <summary>backing heap の値 (実行時に読まれる側)。無いときは WARN (保存・再読込の前は heap が空のことがある。vrc-udonsharp §4)。</summary>
        static void Heap(UdonBehaviour u, string field, object want, Action<bool, string> Check, Action<string> Warn)
        {
            if (u == null) { Check(false, "backing missing for " + field); return; }
            if (!u.publicVariables.TryGetVariableValue(field, out object got)) { Warn("backing heap of " + (u.programSource != null ? u.programSource.name : "?") + " has no " + field + " yet (populated on save / reload)"); return; }
            bool ok;
            if (want is Array wa) ok = got is Array ga && ga.Length == wa.Length && Enumerable.Range(0, wa.Length).All(i => Equals(ga.GetValue(i), wa.GetValue(i)));
            else ok = Equals(got, want) || (got is Object go && want is Object wo && go == wo);
            Check(ok, "backing " + (u.programSource != null ? u.programSource.name : "?") + "." + field + " = " + Describe(want) + (ok ? "" : " (actual " + Describe(got) + ")"));
        }

        static string Describe(object o) { return o == null ? "null" : o is Object uo ? (uo == null ? "null" : uo.name) : o is Array a ? "[" + a.Length + "]" : o.ToString(); }

        static bool OnClick(Transform t, UdonBehaviour target, string evt)
        {
            var button = t != null ? t.GetComponent<Button>() : null;
            if (button == null || target == null) return false;
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
