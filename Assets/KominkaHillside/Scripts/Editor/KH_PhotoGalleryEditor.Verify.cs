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
                var m = AssetDatabase.LoadAssetAtPath<Material>(PrintMatPath(i));
                Check(m != null && m.shader != null && m.shader.name == PrintShaderName && m.mainTexture == t && Mathf.Approximately(m.GetFloat("_AttachDistance"), AttachDistance)
                    && Mathf.Approximately(m.GetFloat("_AttachMargin"), AttachAimMargin) && (!m.HasProperty("_DebugProbe") || m.GetFloat("_DebugProbe") < 0.5f),
                    "material " + i + " KD_PhotoPrint, slot " + i + ", attach " + AttachDistance + " / margin " + AttachAimMargin + ", debug probe off");
            }
            Check(AttachAimMargin * 2f < (PhotoPitchX - PhotoMm.x) * Mm && AttachAimMargin * 2f < (PhotoRow0Y - PhotoRow1Y - PhotoMm.y) * Mm, "aim margin < half the gap between photos (no two photos snap at once)");

            // ---------------------------------------------------------------- memory per platform (computed; measured on Quest / Android build later)
            r.pcMiB = BudgetMiB(PcWidth, PcHeight); r.mobileMiB = BudgetMiB(MobileWidth, MobileHeight);
            Check(r.pcMiB <= PcBudgetMiB, "PC memory " + r.pcMiB.ToString("F1") + " MiB <= " + PcBudgetMiB + " (8 x " + PcWidth + "x" + PcHeight + " RGBA8 + capture colour + depth D32S8 worst case)");
            Check(r.mobileMiB <= MobileBudgetMiB, "Quest / iOS memory " + r.mobileMiB.ToString("F1") + " MiB <= " + MobileBudgetMiB + " (8 x " + MobileWidth + "x" + MobileHeight + ")");
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

            Check(s.gallery.slotTextures != null && s.gallery.slotTextures.SequenceEqual(slots) && (s.gallery.boards == null || s.gallery.boards.Length == 0) && s.gallery.slotGroupCount == 1 && (s.gallery.slotGroups == null || s.gallery.slotGroups.Length == 0),
                "gallery: 8 slot templates in order, boards null (the album owns the board), 1 category");
            Heap(gU, "slotTextures", slots, Check, Warn); Heap(gU, "slotGroupCount", 1, Check, Warn);

            var d = s.director;
            Check(d.photoCapture == s.capture && d.photoCamera == cam && d.gallery == s.gallery && d.bedroom == null, "director: capture / camera / gallery, no bedroom");
            Check(d.dogRoots != null && d.dogRoots.SequenceEqual(c.dogs) && d.dogHeadAnchors != null && d.dogHeadAnchors.SequenceEqual(c.heads)
                && d.fetchControllers != null && d.fetchControllers.SequenceEqual(c.fetch) && d.petControllers != null && d.petControllers.SequenceEqual(c.pet)
                && d.wanderControllers != null && d.wanderControllers.SequenceEqual(c.wander), "director: 6 dogs in roster order (" + string.Join(", ", Keys) + ") for roots / heads / fetch / pet / wander");
            Check(d.cameraBlockMask == CameraBlockMask && Mathf.Approximately(d.tickInterval, 0.1f) && d.frisbeeShotEnabled && Mathf.Approximately(d.frisbeeShotDelay, 0.1f) && Mathf.Approximately(d.frisbeeShotCooldown, 20f),
                "director: block mask Default+Environment, tick 0.1 s, frisbee 0.1 s / 20 s");
            Heap(dU, "tickInterval", 0.1f, Check, Warn); Heap(dU, "frisbeeShotCooldown", 20f, Check, Warn); Heap(dU, "gallery", gU, Check, Warn);
            Heap(dU, "wanderControllers", c.wander.Select(w => (object)B(w)).ToArray(), Check, Warn);

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

            // ---------------------------------------------------------------- dog hooks
            for (int i = 0; i < Keys.Length; i++)
            {
                Check(c.fetch[i].photoDirector == d && c.wander[i].photoDirector == d, "dog " + i + " " + Keys[i] + ": fetch / wander -> director");
                Heap(B(c.fetch[i]), "photoDirector", dU, Check, Warn); Heap(B(c.wander[i]), "photoDirector", dU, Check, Warn);
                Check(c.frisbee[i].wander == c.wander[i] && c.frisbee[i].dogIndex == i, "dog " + i + " frisbee hook path: KH_FrisbeeCatch.wander.photoDirector (dogIndex " + i + ")");
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
                for (int k = 0; k < SlotCount; k++)
                {
                    var t = page.transform.Find("Thumb" + k);
                    var raw = t != null ? t.Find("Photo")?.GetComponent<RawImage>() : null;
                    Check(t != null && OnClick(t, aU, "_MenuPick" + k) && raw != null && al.menuThumbs != null && al.menuThumbs.Length == SlotCount && al.menuThumbs[k] == raw && al.menuThumbButtons[k] == t.gameObject,
                        "thumbnail " + k + " -> _MenuPick" + k + ", wired to the album");
                }
                Check(al.menuPage == page && al.menuLargeRoot != null && al.menuLarge != null && al.menuHelpRoot != null && al.menuState != null && al.menuNewMark != null, "album: page / large / help / state / new mark");
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
                MeasureOne(c, al.menuNewLabel, "photo.new", "", "menu new mark", Check, Note);
            }
        }

        static void VerifyBoard(Ctx c, Sys s, Transform board, Camera cam, Action<bool, string> Check, Action<string> Warn, Action<string> Note)
        {
            var al = s.album;
            Check(board.GetComponentsInChildren<Collider>(true).All(x => x.isTrigger), "board: only trigger colliders (nobody bumps into it; Guide/2's solid check is not affected)");
            var st = board.Find("Structure");
            Check(st != null && st.GetComponentsInChildren<Collider>(true).Length == 0 && st.GetComponentsInChildren<MeshRenderer>(true).Length == 6, "board frame: 6 wood parts, no collider");
            var canvas = board.Find("Canvas");
            Check(canvas != null && canvas.GetComponent<GraphicRaycaster>() == null && canvas.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null && canvas.GetComponent<Canvas>() != null && canvas.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace,
                "board canvas: world space, read-only (no raycaster / UI shape)");
            var photos = board.Find("Photos");
            Check(photos != null && Mathf.Approximately(photos.lossyScale.x, photos.lossyScale.y) && Mathf.Approximately(photos.lossyScale.y, photos.lossyScale.z), "photos' parent has a uniform scale (the snap shader's aim test)");
            for (int k = 0; k < SlotCount; k++)
            {
                var q = photos != null ? photos.Find("Photo" + k) : null;
                var tap = q != null ? q.GetComponent<KD_PhotoBoard>() : null;
                var mr = q != null ? q.GetComponent<MeshRenderer>() : null;
                var box = q != null ? q.GetComponent<BoxCollider>() : null;
                Check(q != null && !q.gameObject.activeSelf && tap != null && tap.viewer == s.viewer && mr != null && mr.sharedMaterial == AssetDatabase.LoadAssetAtPath<Material>(PrintMatPath(k)) && box != null && box.isTrigger
                    && q.gameObject.layer == 0 && (cam == null || (cam.cullingMask & 1) != 0),
                    "board photo " + k + ": hidden until a capture, KD_PhotoBoard -> viewer, material " + k + ", trigger collider");
                if (tap != null) Check(B(tap) != null && B(tap).SyncMethod == Networking.SyncType.None, "board photo " + k + " backing, sync None");
                Check(al.boardPhotos != null && al.boardPhotos.Length == SlotCount && al.boardPhotos[k] == mr && al.boardTaps[k] == tap, "album board slot " + k + " wired (position " + k + " = " + (k < 4 ? "top" : "bottom") + " row)");
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
