using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-WLD-008 W4: other-person filming permission (design §5, §13, §14). Builds and verifies the UI only; the logic is in
    /// KH_TravelMailbox.Film.cs (sync / network), KH_TravelNotice (the film notice), KD_TreatPouch.Film.cs (the subject's menu) and
    /// KH_VideoCamera.People.cs (the filmer's picker / confirmation).
    ///   Video/4 - Build Film Permit   needs Video/1 (the camera), Course/84 (the map page) and Course/85 (mailbox template + notice). Adds, in place:
    ///                                 the monitor's person picker / list / confirmation, the notice's Button6 / Button7 ([いいよ] [いまはだめ] for film),
    ///                                 the map page's 「撮影の申し込みを受ける」 next to the join toggle, Page_Settings/FilmGrantsPage (4 rows + [やめてもらう]),
    ///                                 the mailbox template's film defaults, the glyphs. Saves the scene. Never runs a forbidden builder (Walking/3・4・4b,
    ///                                 Menu/7・12・13・17) and never rebuilds Course/84・85 or Video/1: it only adds / rewires its own objects.
    ///   Video/5 - Verify Film Permit  read-only. Temp/kh-video/film-verify.json and one VIDEO_FILM_VERIFY_PASS / _FAIL line.
    /// The mailbox's network entries (7, all partial files) are checked by Course/81 (KH_CourseTravelJoinEditor.CheckNetworkEntries).
    /// </summary>
    public static partial class KH_VideoCameraEditor
    {
        public const string PersonButtonName = "PersonButton", PersonCancelName = "PersonCancel", PersonListName = "PersonList", PersonConfirmName = "PersonConfirm";
        public const string FilmAcceptName = "FilmAcceptToggle", FilmGrantsPageName = "FilmGrantsPage", FilmGrantsButtonName = "FilmGrants";
        public static readonly Vector2 PersonButtonPos = new Vector2(-100f, -244f), PersonButtonSize = new Vector2(440f, 56f);
        public static readonly Vector2 PersonCancelPos = new Vector2(240f, -244f), PersonCancelSize = new Vector2(200f, 56f);
        public static readonly Vector2 OverlayPos = new Vector2(0f, -215f), OverlaySize = new Vector2(820f, 290f);
        public static readonly Vector2 CellSize = new Vector2(396f, 48f);
        public static Vector2 CellPos(int c) { return new Vector2(c % 2 == 0 ? -202f : 202f, 70f - (c / 2) * 52f); }
        // the map page's top row: the join toggle is (-330, 164) 400 wide; the map heading is hidden in [人]; [地点] starts at x 145
        public static readonly Vector2 FilmAcceptPos = new Vector2(7.5f, 164f), FilmAcceptSize = new Vector2(262f, 44f);
        public static Vector2 GrantRowPos(int k) { return new Vector2(-150f, 90f - k * 80f); }
        public static Vector2 GrantStopPos(int k) { return new Vector2(370f, 90f - k * 80f); }
        public static readonly Vector2 GrantRowSize = new Vector2(700f, 70f), GrantStopSize = new Vector2(300f, 64f);
        static readonly Color OverlayColor = new Color(0.12f, 0.09f, 0.07f, 0.98f);
        public static readonly string[] NoticeFilmEvents = { "_FilmYes", "_FilmNo" };

        /// <summary>Every fixed W4 text (the shared font gets these glyphs).</summary>
        public static string FilmWords()
        {
            return string.Concat(KH_VideoCamera.WordPerson, KH_VideoCamera.WordPersonTitle, KH_VideoCamera.WordPersonClose, KH_VideoCamera.WordAskConfirm, KH_VideoCamera.WordAskNote,
                KH_VideoCamera.WordAskYes, KH_VideoCamera.WordAskNo, KH_VideoCamera.WordCancel, KH_VideoCamera.WordStopPerson, KH_VideoCamera.WordWaitPermit, KH_VideoCamera.WordChecking,
                KH_VideoCamera.WordWaiting, KH_VideoCamera.WordFilmingPerson, KH_VideoCamera.WordNoPermit, KH_VideoCamera.WordDeclined, KH_VideoCamera.WordPaused, KH_VideoCamera.WordBusy,
                KH_VideoCamera.WordNotAccepting, KH_VideoCamera.WordNotReady, KH_VideoCamera.WordAgain, KH_VideoCamera.WordBusyMe, KH_VideoCamera.WordAnswerFirst, KH_VideoCamera.WordLeft,
                KH_VideoCamera.WordRevoked, KH_VideoCamera.WordNoTable, KH_VideoCamera.WordCrossYield, KH_VideoCamera.WordModeSelf, KH_VideoCamera.WordFilming,
                KH_TravelNotice.WordFilmTitle, KH_TravelNotice.WordFilmPlace, KH_TravelNotice.WordFilmGranted, KH_TravelNotice.WordFilmGrantedNote,
                KD_TreatPouch.WordFilmAcceptOn, KD_TreatPouch.WordFilmAcceptOff, KD_TreatPouch.WordFilmPending, KD_TreatPouch.WordFilmGrantsButton, KD_TreatPouch.WordFilmGrantsTitle,
                KD_TreatPouch.WordFilmGrantsNote, KD_TreatPouch.WordFilmRowActive, KD_TreatPouch.WordFilmRowWaiting, KD_TreatPouch.WordFilmStop, KD_TreatPouch.WordFilmNone,
                "◀▶/0123456789()?-[]「」");
        }

        /// <summary>Fixed texts drawn with the display-name font (labels that also carry a player's name).</summary>
        public static string NameFontWords()
        {
            return string.Concat(KH_VideoCamera.WordPerson, KH_VideoCamera.WordModeSelf, KH_VideoCamera.WordAskConfirm, KH_VideoCamera.WordAskNote, KH_VideoCamera.WordWaitPermit,
                KH_VideoCamera.WordChecking, KH_VideoCamera.WordFilmingPerson, KH_VideoCamera.WordNoPermit, KH_VideoCamera.WordDeclined, KH_VideoCamera.WordPaused, KH_VideoCamera.WordBusy,
                KH_VideoCamera.WordNotAccepting, KH_VideoCamera.WordNotReady, KH_VideoCamera.WordAgain, KH_VideoCamera.WordBusyMe, KH_VideoCamera.WordAnswerFirst, KH_VideoCamera.WordLeft,
                KH_VideoCamera.WordRevoked, KH_VideoCamera.WordNoTable, KH_VideoCamera.WordCrossYield, KH_VideoCamera.WordWaiting, KH_VideoCamera.WordFilming,
                KH_TravelNotice.WordFilmTitle, KH_TravelNotice.WordFilmGranted, KD_TreatPouch.WordFilmRowActive, KD_TreatPouch.WordFilmRowWaiting, "0123456789()-");
        }

        static TMP_FontAsset NameFont() { return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KH_CourseTravelNameFontEditor.FontPath); }

        static void UseNameFont(TMP_Text t, TMP_FontAsset nameFont)
        {
            if (t == null || nameFont == null) return;
            t.font = nameFont; t.fontSharedMaterial = nameFont.material; t.overflowMode = TextOverflowModes.Ellipsis;
            EditorUtility.SetDirty(t);
        }

        static void Fit(TMP_Text t, float max, float min, bool wrap)
        {
            if (t == null) return;
            t.enableAutoSizing = true; t.fontSizeMax = max; t.fontSize = max; t.fontSizeMin = min; t.enableWordWrapping = wrap;
            EditorUtility.SetDirty(t);
        }

        // ================================================================ Video/4

        [MenuItem(Menu + "4 - Build Film Permit")]
        public static void BuildFilmPermit()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            string missingProgram = ProgramMissing();
            if (missingProgram != null) { Debug.LogError("[KH Video] VIDEO_FILM_BUILD_FAIL " + missingProgram + " missing: run Video/0 + Video/1 first; nothing changed"); return; }
            var r = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, menu = "Video/4" };
            var c = Resolve();
            var root = SceneRoot(c.scene, SystemName, out int n);
            var video = root != null ? root.GetComponent<KH_VideoCamera>() : null;
            if (video == null || video.monitorRoot == null) { Debug.LogError("[KH Video] VIDEO_FILM_BUILD_FAIL the video camera is not built (run Video/1); nothing changed"); return; }
            var notices = UnityEngine.Object.FindObjectsOfType<KH_TravelNotice>(true);
            var boxes = UnityEngine.Object.FindObjectsOfType<KH_TravelMailbox>(true);
            if (notices.Length != 1 || boxes.Length != 1 || notices[0].board == null) { Debug.LogError("[KH Video] VIDEO_FILM_BUILD_FAIL needs exactly one KH_TravelNotice (with its board) and one KH_TravelMailbox template (Course/85); nothing changed"); return; }
            if (c.pouch.travelMapPage == null || c.pouch.settingsPage == null || c.pouch.travelJoinPendingButton == null) { Debug.LogError("[KH Video] VIDEO_FILM_BUILD_FAIL the map page / settings page / pending button are missing (Course/84・85); nothing changed"); return; }
            var nameFont = NameFont();
            if (nameFont == null) r.log.Add("WARN display-name font missing (" + KH_CourseTravelNameFontEditor.FontPath + "): names fall back to the shared font (run Course/86)");
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-film-permit.unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, true);
            try
            {
                BuildPersonUi(video, c, nameFont, r.log);
                BuildNoticeFilm(notices[0], c, r.log);
                BuildMailboxFilm(boxes[0], r.log);
                BuildPouchFilm(c, nameFont, r.log);
                var sb = new StringBuilder();
                string missing = KD_ShopSetupEditor.MissingGlyphs(FilmWords());
                if (missing.Length > 0) { KD_ShopSetupEditor.AddGlyphs(missing, sb); r.log.Add("glyphs added: " + missing + " " + sb.ToString().Trim()); }
                EditorSceneManager.MarkSceneDirty(c.scene);
                if (!EditorSceneManager.SaveScene(c.scene)) throw new IOException("scene save failed");
                AssetDatabase.SaveAssets();
                r.status = "BUILT";
            }
            catch (Exception e)
            {
                r.status = "FAILED"; r.log.Add("FAIL " + e.Message);
                WriteJson(OutDir + "/film-build.json", r);
                Debug.LogError("[KH Video] VIDEO_FILM_BUILD_FAIL " + e.Message + ". The scene before this run is at " + r.backup + "; reopen the scene without saving to discard partial changes.");
                throw;
            }
            WriteJson(OutDir + "/film-build.json", r);
            Debug.Log("[KH Video] VIDEO_FILM_BUILD_OK (" + r.log.Count + " log lines in " + OutDir + "/film-build.json). Next: Video/5, Video/2, Course/81, then the harness V6 (after the pre-run consult).");
        }

        static void BuildPersonUi(KH_VideoCamera video, Ctx c, TMP_FontAsset nameFont, List<string> log)
        {
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(video);
            if (backing == null) throw new InvalidOperationException("KH_VideoCamera backing missing");
            var panel = video.monitorRoot.transform.Find(PanelName);
            if (panel == null) throw new InvalidOperationException("monitor panel missing (run Video/1)");
            // ---- the picker row (じぶん mode): [人: name] and [取り消す] ----
            var person = Btn(panel, PersonButtonName, PersonButtonPos, PersonButtonSize, KH_VideoCamera.WordPerson + KH_VideoCamera.WordModeSelf, c.font, backing, nameof(KH_VideoCamera._PersonList), log);
            var personLabel = LabelOf(person);
            UseNameFont(personLabel, nameFont); Fit(personLabel, 22f, 12f, true); personLabel.alignment = TextAlignmentOptions.Center; personLabel.lineSpacing = -8f;
            person.SetActive(false);
            var cancel = Btn(panel, PersonCancelName, PersonCancelPos, PersonCancelSize, KH_VideoCamera.WordCancel, c.font, backing, nameof(KH_VideoCamera._PersonCancel), log);
            cancel.SetActive(false);
            // ---- the list overlay (covers the mode buttons; blocks rays to them) ----
            var list = KD_ShopSetupEditor.EnsureUiChild(panel, PersonListName, OverlayPos, OverlaySize);
            var lbg = list.GetComponent<Image>(); if (lbg == null) lbg = list.AddComponent<Image>();
            lbg.color = OverlayColor; lbg.raycastTarget = true; KD_UiSpriteEditor.ApplyFlatSkin(lbg);
            var title = Label(list.transform, "Title", new Vector2(-230f, 117f), new Vector2(340f, 40f), KH_VideoCamera.WordPersonTitle, c.font, 24f, 14f, TextAlignmentOptions.MidlineLeft, false);
            title.color = KD_UiSpriteEditor.AccentGold;
            Btn(list.transform, "Prev", new Vector2(90f, 117f), new Vector2(70f, 44f), "◀", c.font, backing, nameof(KH_VideoCamera._PersonPagePrev), log);
            var page = Label(list.transform, "Page", new Vector2(165f, 117f), new Vector2(70f, 40f), "1/1", c.font, 20f, 12f, TextAlignmentOptions.Center, false);
            Btn(list.transform, "Next", new Vector2(240f, 117f), new Vector2(70f, 44f), "▶", c.font, backing, nameof(KH_VideoCamera._PersonPageNext), log);
            Btn(list.transform, "Close", new Vector2(345f, 117f), new Vector2(110f, 44f), KH_VideoCamera.WordPersonClose, c.font, backing, nameof(KH_VideoCamera._PersonListClose), log);
            var cellLabels = new TextMeshProUGUI[KH_VideoCamera.PeoplePerPage]; var cellImages = new Image[KH_VideoCamera.PeoplePerPage];
            for (int i = 0; i < KH_VideoCamera.PeoplePerPage; i++)
            {
                var cell = Btn(list.transform, "Cell" + i, CellPos(i), CellSize, "VRChatUser12345", c.font, backing, "_PersonPick" + i, log);
                cellImages[i] = cell.GetComponent<Image>();
                cellLabels[i] = LabelOf(cell);
                UseNameFont(cellLabels[i], nameFont); Fit(cellLabels[i], 18f, 11f, true); cellLabels[i].lineSpacing = -12f;
            }
            var reason = Label(list.transform, "Reason", new Vector2(0f, -125f), new Vector2(800f, 30f), "", c.font, 18f, 12f, TextAlignmentOptions.Center, false);
            reason.color = new Color(1f, .82f, .55f);
            UseNameFont(reason, nameFont);
            list.transform.SetAsLastSibling();
            list.SetActive(false);
            // ---- the confirmation overlay (§14.3) ----
            var conf = KD_ShopSetupEditor.EnsureUiChild(panel, PersonConfirmName, OverlayPos, OverlaySize);
            var cbg = conf.GetComponent<Image>(); if (cbg == null) cbg = conf.AddComponent<Image>();
            cbg.color = OverlayColor; cbg.raycastTarget = true; KD_UiSpriteEditor.ApplyFlatSkin(cbg);
            var q = Label(conf.transform, "Question", new Vector2(0f, 80f), new Vector2(780f, 70f), KH_VideoCamera.WordAskConfirm.Replace("{0}", "VRChatUser12"), c.font, 26f, 14f, TextAlignmentOptions.Center, true);
            var note = Label(conf.transform, "Note", new Vector2(0f, 25f), new Vector2(780f, 36f), KH_VideoCamera.WordAskNote.Replace("{0}", "VRChatUser12"), c.font, 20f, 12f, TextAlignmentOptions.Center, false);
            UseNameFont(q, nameFont); UseNameFont(note, nameFont);
            Btn(conf.transform, "Yes", new Vector2(-180f, -60f), new Vector2(300f, 70f), KH_VideoCamera.WordAskYes, c.font, backing, nameof(KH_VideoCamera._PersonAskYes), log);
            Btn(conf.transform, "No", new Vector2(180f, -60f), new Vector2(300f, 70f), KH_VideoCamera.WordAskNo, c.font, backing, nameof(KH_VideoCamera._PersonAskNo), log);
            conf.transform.SetAsLastSibling();
            conf.SetActive(false);
            foreach (var tr in video.monitorRoot.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = MonitorLayer;

            video.personButton = person; video.personLabel = personLabel; video.personCancel = cancel; video.personCancelLabel = LabelOf(cancel);
            video.personListRoot = list; video.personListTitle = title; video.personListReason = reason; video.personPageLabel = page;
            video.personCellLabels = cellLabels; video.personCellImages = cellImages;
            video.personConfirmRoot = conf; video.personConfirmLabel = q; video.personConfirmNote = note;
            UdonSharpEditorUtility.CopyProxyToUdon(video); EditorUtility.SetDirty(video);
            log.Add("monitor: person picker " + PersonButtonPos + " / cancel / list (" + KH_VideoCamera.PeoplePerPage + " cells) / confirmation, layer " + MonitorLayer + ", names in " + (nameFont != null ? nameFont.name : "the shared font"));
        }

        static void BuildNoticeFilm(KH_TravelNotice notice, Ctx c, List<string> log)
        {
            var nb = UdonSharpEditorUtility.GetBackingUdonBehaviour(notice);
            if (nb == null) throw new InvalidOperationException("KH_TravelNotice backing missing");
            var board = notice.board.transform;
            var sb = new StringBuilder();
            var yes = KD_ShopSetupEditor.EnsureButton(board, "Button6", new Vector2(-175f, -10f), new Vector2(300f, 80f), KD_UiSpriteEditor.ActionNeutral * 1.2f, c.font, KD_LocalizationEditor.Text(JsonUtility.FromJson<KD_LocalizationEditor.Catalog>(File.ReadAllText(KH_CourseMenuEditor.CatalogPath)), "ja", "course.travel.join.notice.yes"), nb, NoticeFilmEvents[0], sb);
            var no = KD_ShopSetupEditor.EnsureButton(board, "Button7", new Vector2(175f, -10f), new Vector2(300f, 80f), KD_UiSpriteEditor.ActionNeutral, c.font, KD_LocalizationEditor.Text(JsonUtility.FromJson<KD_LocalizationEditor.Catalog>(File.ReadAllText(KH_CourseMenuEditor.CatalogPath)), "ja", "course.travel.join.notice.no"), nb, NoticeFilmEvents[1], sb);
            if (sb.Length > 0) log.Add(sb.ToString().Trim());
            foreach (var b in new[] { yes, no })
            {
                var l = LabelOf(b); l.fontSize = 26f; l.enableAutoSizing = true; l.fontSizeMin = 14f; l.fontSizeMax = 26f; EditorUtility.SetDirty(l);
                b.SetActive(false);
            }
            var b2 = board.Find("Button2"); var b3 = board.Find("Button3");
            if (b2 == null || b3 == null) throw new InvalidOperationException("notice Button2 / Button3 missing (Course/85)");
            notice.filmButtons = new[] { yes, no };
            notice.filmYesLabel = LabelOf(yes); notice.filmNoLabel = LabelOf(no);
            notice.commonButtons = new[] { b2.gameObject, b3.gameObject };
            // the film title is long ("{0} さんが撮影カメラで…"): let the title and the place line shrink (join / call keep their size when they fit)
            Fit(notice.titleLabel, 26f, 16f, false);
            Fit(notice.placeLabel, 20f, 12f, false);
            UdonSharpEditorUtility.CopyProxyToUdon(notice); EditorUtility.SetDirty(notice);
            log.Add("notice: Button6 / Button7 (film yes / no, hidden), commonButtons = Button2 / Button3, title / place auto-size");
        }

        static void BuildMailboxFilm(KH_TravelMailbox box, List<string> log)
        {
            box.filmAccepting = true;
            box.filmGrantIds = new[] { -1, -1, -1, -1 }; box.filmGrantSeqs = new[] { 0, 0, 0, 0 };
            box.filmTargetId = -1; box.filmSeq = 0;
            UdonSharpEditorUtility.CopyProxyToUdon(box); EditorUtility.SetDirty(box);
            log.Add("mailbox template: filmAccepting ON, grant table 4 x -1, filmTargetId -1 (sync stays Manual)");
        }

        static void BuildPouchFilm(Ctx c, TMP_FontAsset nameFont, List<string> log)
        {
            var p = c.pouch;
            var pb = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            if (pb == null) throw new InvalidOperationException("KD_TreatPouch backing missing");
            var sb = new StringBuilder();
            // ---- the map page: 「撮影の申し込みを受ける」 next to the join toggle ([人] only; shown at runtime) ----
            var accept = KD_ShopSetupEditor.EnsureButton(p.travelMapPage.transform, FilmAcceptName, FilmAcceptPos, FilmAcceptSize, KD_UiSpriteEditor.ActionNeutral, c.font, KD_TreatPouch.WordFilmAcceptOn, pb, nameof(KD_TreatPouch._FilmToggleAccept), sb);
            var acceptLabel = LabelOf(accept); acceptLabel.fontSize = 20f; acceptLabel.enableAutoSizing = true; acceptLabel.fontSizeMin = 12f; acceptLabel.fontSizeMax = 20f; EditorUtility.SetDirty(acceptLabel);
            accept.SetActive(false);
            // ---- Page_Walk: [撮影を許可中 (n)], its own button (Codex review 2: reachable while a request is pending). Saved at the pending slot
            // (266x72), hidden; RedrawTravel makes the row three 174-wide buttons when both are up. The label stretches with the button ----
            if (!KH_WalkTabLayoutEditor.IsApplied(p)) throw new InvalidOperationException("the two-column walk tab (Course/120) is required for " + FilmGrantsButtonName);
            var walk = p.tabPages[KD_TreatPouch.TabWalk].transform;
            var grantsBtn = KD_ShopSetupEditor.EnsureButton(walk, FilmGrantsButtonName, KH_WalkTabLayoutEditor.PendingPos, KH_WalkTabLayoutEditor.PendingSize, new Color(.30f, .42f, .52f), c.font,
                KD_TreatPouch.WordFilmGrantsButton.Replace("{0}", "1"), pb, nameof(KD_TreatPouch._FilmGrantsOpen), sb);
            var gl = LabelOf(grantsBtn);
            var glr = gl.rectTransform; glr.anchorMin = Vector2.zero; glr.anchorMax = Vector2.one; glr.pivot = new Vector2(.5f, .5f); glr.anchoredPosition = Vector2.zero; glr.sizeDelta = new Vector2(-16f, -4f);
            gl.enableAutoSizing = true; gl.fontSizeMax = 22f; gl.fontSize = 22f; gl.fontSizeMin = 12f; gl.enableWordWrapping = true; EditorUtility.SetDirty(gl);
            if (p.travelJoinPendingButton != null && p.travelJoinPendingButton.transform.parent == walk) grantsBtn.transform.SetSiblingIndex(p.travelJoinPendingButton.transform.GetSiblingIndex() + 1);
            grantsBtn.SetActive(false);
            p.filmGrantsButton = grantsBtn; p.filmGrantsButtonLabel = gl;
            // ---- Page_Settings/FilmGrantsPage ----
            var page = KD_ShopSetupEditor.EnsureUiChild(p.settingsPage.transform, FilmGrantsPageName, Vector2.zero, new Vector2(KD_TreatSetupEditor.PanelWidth, KD_TreatSetupEditor.PanelHeight));
            var back = KD_ShopSetupEditor.EnsureButton(page.transform, "Back", new Vector2(450f, 164f), new Vector2(220f, 44f), KD_UiSpriteEditor.ActionNeutral, c.font, "もどる", pb, nameof(KD_TreatPouch._FilmGrantsBack), sb);
            var backLabel = LabelOf(back); backLabel.fontSize = 20f; EditorUtility.SetDirty(backLabel);
            var note = Label(page.transform, "Note", new Vector2(-110f, 164f), new Vector2(640f, 48f), KD_TreatPouch.WordFilmGrantsNote, c.font, 18f, 12f, TextAlignmentOptions.MidlineLeft, true);
            var labels = new TextMeshProUGUI[KH_TravelMailbox.FilmSlots]; var stops = new GameObject[KH_TravelMailbox.FilmSlots]; var stopLabels = new TextMeshProUGUI[KH_TravelMailbox.FilmSlots];
            for (int k = 0; k < KH_TravelMailbox.FilmSlots; k++)
            {
                labels[k] = Label(page.transform, "Row" + k, GrantRowPos(k), GrantRowSize, "VRChatUser12345\n" + KD_TreatPouch.WordFilmRowWaiting, c.font, 22f, 14f, TextAlignmentOptions.MidlineLeft, true);
                labels[k].lineSpacing = -8f;
                UseNameFont(labels[k], nameFont);
                labels[k].gameObject.SetActive(false);
                stops[k] = KD_ShopSetupEditor.EnsureButton(page.transform, "Stop" + k, GrantStopPos(k), GrantStopSize, KD_UiSpriteEditor.ActionNeutral, c.font, KD_TreatPouch.WordFilmStop, pb, "_FilmStop" + k, sb);
                stopLabels[k] = LabelOf(stops[k]); stopLabels[k].fontSize = 24f; stopLabels[k].enableAutoSizing = true; stopLabels[k].fontSizeMin = 14f; stopLabels[k].fontSizeMax = 24f; EditorUtility.SetDirty(stopLabels[k]);
                stops[k].SetActive(false);
            }
            var empty = Label(page.transform, "Empty", new Vector2(0f, 0f), new Vector2(800f, 40f), KD_TreatPouch.WordFilmNone, c.font, 22f, 14f, TextAlignmentOptions.Center, false);
            page.SetActive(false);
            if (sb.Length > 0) log.Add(sb.ToString().Trim());
            var pages = new List<GameObject>();
            foreach (var e in p.extraSettingsPages ?? new GameObject[0]) if (e != null && e != page && !pages.Contains(e)) pages.Add(e);
            pages.Add(page);
            p.extraSettingsPages = pages.ToArray();
            p.filmAcceptButton = accept; p.filmAcceptLabel = acceptLabel;
            p.filmGrantsPage = page; p.filmGrantsNote = note; p.filmGrantsEmpty = empty; p.filmGrantsBackLabel = backLabel;
            p.filmGrantLabels = labels; p.filmStopButtons = stops; p.filmStopLabels = stopLabels;
            UdonSharpEditorUtility.CopyProxyToUdon(p); EditorUtility.SetDirty(p);
            log.Add("pouch: " + FilmAcceptName + " on the map page " + FilmAcceptPos + " (hidden; [人] shows it), " + FilmGrantsPageName + " (4 rows + stop) registered in extraSettingsPages (" + pages.Count + ")");
        }

        // ================================================================ Video/5

        [Serializable] class FilmVerifyReport { public string utc, status; public int pass, fail, warn; public List<string> lines = new List<string>(); public List<string> fit = new List<string>(); }

        [MenuItem(Menu + "5 - Verify Film Permit")]
        public static void VerifyFilmPermit()
        {
            var r = new FilmVerifyReport { utc = DateTime.UtcNow.ToString("o") };
            void Check(bool ok, string what) { if (ok) r.pass++; else r.fail++; r.lines.Add((ok ? "PASS " : "FAIL ") + what); }
            void Warn(string what) { r.warn++; r.lines.Add("WARN " + what); }
            try { VerifyFilmInto(Check, Warn, r.fit); }
            catch (Exception e) { Check(false, "exception: " + e.Message); }
            r.status = r.fail == 0 ? "PASS" : "FAIL";
            WriteJson(OutDir + "/film-verify.json", r);
            string head = "[KH Video] VIDEO_FILM_VERIFY_" + r.status + " pass " + r.pass + " fail " + r.fail + " warn " + r.warn + " (" + OutDir + "/film-verify.json)";
            if (r.fail == 0) Debug.Log(head); else Debug.LogError(head + " first: " + r.lines.First(l => l.StartsWith("FAIL")));
        }

        static void CheckEvt(Action<bool, string> Check, GameObject b, UdonBehaviour target, string evt, string what)
        {
            var btn = b != null ? b.GetComponent<Button>() : null;
            if (btn == null) { Check(false, what + ": no Button"); return; }
            int count = btn.onClick.GetPersistentEventCount();
            var so = new SerializedObject(btn);
            var arg = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls.Array.data[0].m_Arguments.m_StringArgument");
            string sent = arg != null ? arg.stringValue : "(none)";
            Check(count == 1 && btn.onClick.GetPersistentTarget(0) == target && btn.onClick.GetPersistentMethodName(0) == "SendCustomEvent" && sent == evt,
                what + " -> " + evt + " (calls " + count + ", argument '" + sent + "')");
        }

        static void VerifyFilmInto(Action<bool, string> Check, Action<string> Warn, List<string> fit)
        {
            var scene = EditorSceneManager.GetActiveScene();
            var root = SceneRoot(scene, SystemName, out int n);
            var v = root != null ? root.GetComponent<KH_VideoCamera>() : null;
            Check(v != null, "KH_VideoCamera present (Video/1)");
            if (v == null) return;
            var vb = UdonSharpEditorUtility.GetBackingUdonBehaviour(v);
            var nameFont = NameFont();
            if (nameFont == null) Warn("display-name font missing: names use the shared font (Course/86)");
            // ---- the monitor: picker, list, confirmation ----
            var panel = v.monitorRoot != null ? v.monitorRoot.transform.Find(PanelName) : null;
            Check(panel != null, "monitor panel");
            if (panel != null)
            {
                var pbt = panel.Find(PersonButtonName); var pct = panel.Find(PersonCancelName); var lst = panel.Find(PersonListName); var cft = panel.Find(PersonConfirmName);
                Check(pbt != null && v.personButton == pbt.gameObject && !pbt.gameObject.activeSelf, "person button wired, hidden in the scene (shown in じぶん mode)");
                Check(pct != null && v.personCancel == pct.gameObject && !pct.gameObject.activeSelf, "person cancel wired, hidden");
                Check(lst != null && v.personListRoot == lst.gameObject && !lst.gameObject.activeSelf, "person list wired, hidden");
                Check(cft != null && v.personConfirmRoot == cft.gameObject && !cft.gameObject.activeSelf, "confirmation wired, hidden");
                if (pbt != null) CheckEvt(Check, pbt.gameObject, vb, "_PersonList", "person button");
                if (pct != null) CheckEvt(Check, pct.gameObject, vb, "_PersonCancel", "person cancel");
                if (lst != null)
                {
                    foreach (var (nm, evt) in new[] { ("Prev", "_PersonPagePrev"), ("Next", "_PersonPageNext"), ("Close", "_PersonListClose") }) { var t = lst.Find(nm); CheckEvt(Check, t != null ? t.gameObject : null, vb, evt, "list " + nm); }
                    for (int i = 0; i < KH_VideoCamera.PeoplePerPage; i++) { var t = lst.Find("Cell" + i); CheckEvt(Check, t != null ? t.gameObject : null, vb, "_PersonPick" + i, "list cell " + i); }
                    Check(v.personCellLabels != null && v.personCellLabels.Length == KH_VideoCamera.PeoplePerPage && v.personCellImages != null && v.personCellImages.Length == KH_VideoCamera.PeoplePerPage
                        && Enumerable.Range(0, KH_VideoCamera.PeoplePerPage).All(i => lst.Find("Cell" + i) != null && v.personCellImages[i] == lst.Find("Cell" + i).GetComponent<Image>() && v.personCellLabels[i] == LabelOf(lst.Find("Cell" + i).gameObject)), "list cells = the arrays");
                    var img = lst.GetComponent<Image>();
                    Check(img != null && img.raycastTarget && lst.GetSiblingIndex() >= panel.childCount - 2, "the list overlay catches rays and is drawn above the mode buttons");
                }
                if (cft != null)
                {
                    var yes = cft.Find("Yes"); var no = cft.Find("No");
                    CheckEvt(Check, yes != null ? yes.gameObject : null, vb, "_PersonAskYes", "confirm [依頼する]");
                    CheckEvt(Check, no != null ? no.gameObject : null, vb, "_PersonAskNo", "confirm [やめる]");
                    var img = cft.GetComponent<Image>();
                    Check(img != null && img.raycastTarget && cft.GetSiblingIndex() >= panel.childCount - 2, "the confirmation catches rays and is drawn above the mode buttons");
                    Check(v.personConfirmLabel != null && v.personConfirmNote != null, "confirmation labels wired");
                }
                var wrong = v.monitorRoot.GetComponentsInChildren<Transform>(true).Where(t => t.gameObject.layer != MonitorLayer).Select(t => t.name).ToArray();
                Check(wrong.Length == 0, "every monitor object on layer " + MonitorLayer + (wrong.Length > 0 ? " (wrong: " + string.Join(",", wrong.Take(5)) + ")" : ""));
                Check(v.personLabel != null && v.personListReason != null && v.personPageLabel != null && v.personListTitle != null && v.personCancelLabel != null, "picker / list labels wired");
                if (nameFont != null)
                {
                    var named = new TMP_Text[] { v.personLabel, v.personConfirmLabel, v.personConfirmNote, v.personListReason }.Concat(v.personCellLabels ?? new TextMeshProUGUI[0]).ToArray();
                    Check(named.All(t => t != null && t.font == nameFont), "labels that carry a player's name use the display-name font");
                }
                foreach (var field in new[] { "personButton", "personListRoot", "personConfirmRoot", "personCellLabels", "personLabel" })
                    Check(vb != null && vb.publicVariables.TryGetVariableValue(field, out object fv) && fv != null, "backing " + field);
            }
            // ---- glyphs ----
            string shared = KD_ShopSetupEditor.MissingGlyphs(FilmWords());
            Check(shared.Length == 0, "W4 texts' glyphs in the shared font" + (shared.Length > 0 ? " (missing " + shared + "; rerun Video/4)" : ""));
            if (nameFont != null)
            {
                var miss = new HashSet<char>(NameFontWords().Where(ch => ch != ' ' && ch != '\n' && !KD_LocalizationEditor.HasGlyph(nameFont, ch)));
                if (miss.Count > 0) Check(false, "W4 fixed texts drawn with the display-name font have glyphs there (missing " + new string(miss.ToArray()) + "; add them to the charset of Course/86)");
                else Check(true, "W4 fixed texts drawn with the display-name font have glyphs there");
            }
            // ---- the notice ----
            var notice = UnityEngine.Object.FindObjectsOfType<KH_TravelNotice>(true).FirstOrDefault();
            Check(notice != null && notice.board != null, "KH_TravelNotice with its board");
            if (notice != null && notice.board != null)
            {
                var nb = UdonSharpEditorUtility.GetBackingUdonBehaviour(notice);
                var b6 = notice.board.transform.Find("Button6"); var b7 = notice.board.transform.Find("Button7");
                CheckEvt(Check, b6 != null ? b6.gameObject : null, nb, "_FilmYes", "notice Button6");
                CheckEvt(Check, b7 != null ? b7.gameObject : null, nb, "_FilmNo", "notice Button7");
                Check(b6 != null && b7 != null && !b6.gameObject.activeSelf && !b7.gameObject.activeSelf, "film buttons hidden at start");
                Check(notice.filmButtons != null && notice.filmButtons.Length == 2 && b6 != null && b7 != null && notice.filmButtons[0] == b6.gameObject && notice.filmButtons[1] == b7.gameObject, "notice filmButtons = Button6/7");
                var b2 = notice.board.transform.Find("Button2"); var b3 = notice.board.transform.Find("Button3");
                Check(notice.commonButtons != null && notice.commonButtons.Length == 2 && b2 != null && b3 != null && notice.commonButtons[0] == b2.gameObject && notice.commonButtons[1] == b3.gameObject, "notice commonButtons = Button2/3 ([しばらく受けない] [この人は退室まで受けない])");
                Check(notice.filmYesLabel != null && notice.filmNoLabel != null, "notice film labels wired");
                Check(nb != null && nb.publicVariables.TryGetVariableValue("filmButtons", out object fb) && fb is Array fa && fa.Length == 2, "notice filmButtons (backing)");
                // the film title with a long name fits (auto-size)
                var go = new GameObject("FilmMeasure", typeof(RectTransform)); go.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    var sample = go.AddComponent<TextMeshProUGUI>();
                    void FitCheck(TMP_Text t, string text, string what)
                    {
                        if (t == null) { Check(false, "label missing: " + what); return; }
                        int res = KD_LocalizationEditor.MeasureLabelFit(sample, t, text, out Vector2 pref);
                        fit.Add(what + " preferred=" + pref + " rect=" + t.rectTransform.rect.size + " fit=" + res);
                        Check(res != 2, "fits: " + what);
                    }
                    FitCheck(notice.titleLabel, KH_TravelNotice.WordFilmTitle.Replace("{0}", "VRChatUser12345"), "notice film title");
                    FitCheck(notice.placeLabel, KH_TravelNotice.WordFilmPlace, "notice film place");
                    FitCheck(notice.titleLabel, KH_TravelNotice.WordFilmGranted.Replace("{0}", "VRChatUser12345"), "notice granted title");
                    FitCheck(notice.placeLabel, KH_TravelNotice.WordFilmGrantedNote, "notice granted note");
                    FitCheck(v.personLabel, KH_VideoCamera.WordPerson + "VRChatUser12345\n" + KH_VideoCamera.WordNoPermit.Replace("{0}", "VRChatUser12345"), "picker no-permit line");
                    FitCheck(v.personConfirmLabel, KH_VideoCamera.WordAskConfirm.Replace("{0}", "VRChatUser12345"), "confirmation question");
                    FitCheck(v.personConfirmNote, KH_VideoCamera.WordAskNote.Replace("{0}", "VRChatUser12345"), "confirmation note");
                    if (v.personCellLabels != null && v.personCellLabels.Length > 0) FitCheck(v.personCellLabels[0], "VRChatUser12345\n" + KH_VideoCamera.WordAnswerFirst, "list cell (longest reason)");
                    var pouch = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true).FirstOrDefault();
                    if (pouch != null)
                    {
                        FitCheck(pouch.filmAcceptLabel, KD_TreatPouch.WordFilmAcceptOff, "map page film toggle");
                        FitCheck(pouch.filmGrantsButtonLabel, KD_TreatPouch.WordFilmGrantsButton.Replace("{0}", "4"), "walk tab [撮影を許可中] (266 wide; the 174-wide three-button row is checked by the harness V6 case 21)");
                        FitCheck(pouch.filmGrantsNote, KD_TreatPouch.WordFilmGrantsNote, "grants page note");
                        if (pouch.filmGrantLabels != null && pouch.filmGrantLabels.Length > 0) FitCheck(pouch.filmGrantLabels[0], "VRChatUser12345\n" + KD_TreatPouch.WordFilmRowWaiting, "grants row");
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            // ---- the mailbox template ----
            var boxes = UnityEngine.Object.FindObjectsOfType<KH_TravelMailbox>(true);
            Check(boxes.Length == 1, "one KH_TravelMailbox template (" + boxes.Length + ")");
            if (boxes.Length == 1)
            {
                var box = boxes[0]; var bb = UdonSharpEditorUtility.GetBackingUdonBehaviour(box);
                Check(bb != null && bb.SyncMethod == Networking.SyncType.Manual, "mailbox sync Manual");
                Check(box.filmAccepting && box.filmGrantIds != null && box.filmGrantIds.Length == KH_TravelMailbox.FilmSlots && box.filmGrantIds.All(x => x == -1)
                    && box.filmGrantSeqs != null && box.filmGrantSeqs.Length == KH_TravelMailbox.FilmSlots && box.filmTargetId == -1, "mailbox film defaults: accepting ON, 4 empty rows, no declaration");
                // The heap override exists only when the proxy array differs from the declaration (ArraySerializer copies into the program
                // default when the length matches; vrc-udonsharp §4). No override = the runtime value is the declared initialiser, which the
                // source check below pins ({ -1, -1, -1, -1 } / { 0, 0, 0, 0 }). An override that exists must hold the empty table.
                object gi = null; bool hasIds = bb != null && bb.publicVariables.TryGetVariableValue("filmGrantIds", out gi);
                Check(bb != null && (!hasIds || (gi is int[] ga && ga.Length == KH_TravelMailbox.FilmSlots && ga.All(x => x == -1))), "mailbox grant table: no heap override (declared initialiser) or an override of 4 x -1 (an uninitialised synced array stops the whole behaviour's sync)");
                object gs = null; bool hasSeqs = bb != null && bb.publicVariables.TryGetVariableValue("filmGrantSeqs", out gs);
                Check(bb != null && (!hasSeqs || (gs is int[] gsa && gsa.Length == KH_TravelMailbox.FilmSlots)), "mailbox grant seqs: no heap override (declared initialiser) or an override of length 4");
            }
            // ---- the pouch ----
            var p = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true).FirstOrDefault();
            Check(p != null, "KD_TreatPouch");
            if (p != null)
            {
                var pb = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
                Check(p.filmAcceptButton != null && p.travelMapPage != null && p.filmAcceptButton.transform.parent == p.travelMapPage.transform && !p.filmAcceptButton.activeSelf, "film toggle on the map page, hidden at start");
                if (p.filmAcceptButton != null) CheckEvt(Check, p.filmAcceptButton, pb, "_FilmToggleAccept", "map page film toggle");
                var join = p.travelMapPage != null ? p.travelMapPage.transform.Find(KH_CourseTravelJoinEditor.AcceptName) as RectTransform : null;
                var spots = p.travelMapPage != null ? p.travelMapPage.transform.Find(KH_CourseTravelJoinEditor.ModeSpotsName) as RectTransform : null;
                var mine = p.filmAcceptButton != null ? (RectTransform)p.filmAcceptButton.transform : null;
                if (join != null && spots != null && mine != null)
                {
                    Rect R(RectTransform t) { return new Rect(t.anchoredPosition - t.sizeDelta / 2f, t.sizeDelta); }
                    Check(!R(mine).Overlaps(R(join)) && !R(mine).Overlaps(R(spots)), "film toggle sits between the join toggle and [地点] without overlap");
                }
                Check(p.filmGrantsPage != null && p.settingsPage != null && p.filmGrantsPage.transform.parent == p.settingsPage.transform && !p.filmGrantsPage.activeSelf, "FilmGrantsPage under Page_Settings, hidden");
                Check(p.extraSettingsPages != null && p.filmGrantsPage != null && p.extraSettingsPages.Contains(p.filmGrantsPage) && p.travelMapPage != null && p.extraSettingsPages.Contains(p.travelMapPage), "FilmGrantsPage registered in extraSettingsPages (map page kept)");
                if (p.filmGrantsPage != null)
                {
                    var back = p.filmGrantsPage.transform.Find("Back");
                    CheckEvt(Check, back != null ? back.gameObject : null, pb, "_FilmGrantsBack", "grants [もどる]");
                    for (int k = 0; k < KH_TravelMailbox.FilmSlots; k++)
                    {
                        var s = p.filmGrantsPage.transform.Find("Stop" + k);
                        CheckEvt(Check, s != null ? s.gameObject : null, pb, "_FilmStop" + k, "grants stop " + k);
                        Check(p.filmStopButtons != null && p.filmStopButtons.Length == KH_TravelMailbox.FilmSlots && s != null && p.filmStopButtons[k] == s.gameObject
                            && p.filmGrantLabels != null && p.filmGrantLabels.Length == KH_TravelMailbox.FilmSlots && p.filmGrantLabels[k] != null && p.filmGrantLabels[k].name == "Row" + k, "grants row " + k + " wired");
                    }
                }
                foreach (var field in new[] { "filmAcceptButton", "filmGrantsPage", "filmGrantLabels", "filmStopButtons", "extraSettingsPages", "filmGrantsButton", "filmGrantsButtonLabel" })
                    Check(pb != null && pb.publicVariables.TryGetVariableValue(field, out object fv) && fv != null, "pouch backing " + field);
                if (p.travelJoinPendingButton != null) CheckEvt(Check, p.travelJoinPendingButton, pb, "_TravelShowNotice", "walk tab [申し込み 1 件] (the notice only)");
                var walkPage = p.tabPages != null && p.tabPages.Length > KD_TreatPouch.TabWalk ? p.tabPages[KD_TreatPouch.TabWalk] : null;
                Check(p.filmGrantsButton != null && walkPage != null && p.filmGrantsButton.transform.parent == walkPage.transform && !p.filmGrantsButton.activeSelf && p.filmGrantsButton.name == FilmGrantsButtonName, "walk tab [撮影を許可中] is its own button under Page_Walk, hidden at start (reachable while a request is pending)");
                if (p.filmGrantsButton != null)
                {
                    CheckEvt(Check, p.filmGrantsButton, pb, "_FilmGrantsOpen", "walk tab [撮影を許可中]");
                    var gr = (RectTransform)p.filmGrantsButton.transform; var gl = p.filmGrantsButtonLabel;
                    Check(Vector2.Distance(gr.anchoredPosition, KH_WalkTabLayoutEditor.PendingPos) < 0.5f && Vector2.Distance(gr.sizeDelta, KH_WalkTabLayoutEditor.PendingSize) < 0.5f, "[撮影を許可中] saved at the pending slot " + KH_WalkTabLayoutEditor.PendingPos);
                    Check(gl != null && gl.transform.parent == gr && gl.rectTransform.anchorMin == Vector2.zero && gl.rectTransform.anchorMax == Vector2.one && gl.enableAutoSizing, "[撮影を許可中] label stretches with the button (174 / 266 at runtime) and auto-sizes");
                    Check(Mathf.Abs(KD_TreatPouch.FilmThirdX0 - KD_TreatPouch.FilmThirdWidth / 2f - 10f) < 0.5f && Mathf.Abs(KD_TreatPouch.FilmThirdX2 + KD_TreatPouch.FilmThirdWidth / 2f - 550f) < 0.5f && Mathf.Abs(KD_TreatPouch.FilmPendingX - KH_WalkTabLayoutEditor.PendingPos.x) < 0.5f, "three-button row fills the right column (x 10..550) and the pending slot constant matches the layout");
                }
            }
            // ---- source rules (the permission's logic) ----
            string film = File.Exists("Assets/KominkaHillside/Scripts/KH_TravelMailbox.Film.cs") ? File.ReadAllText("Assets/KominkaHillside/Scripts/KH_TravelMailbox.Film.cs") : "";
            string main = File.ReadAllText("Assets/KominkaHillside/Scripts/KH_TravelMailbox.cs");
            Check(film.Length > 0, "KH_TravelMailbox.Film.cs exists");
            Check(Regex.IsMatch(film, @"public bool _FilmGate\(int pid\)[\s\S]{0,200}_filmPhase != FilmInUse \|\| _filmTarget != pid"), "_FilmGate: in-use session for that player only");
            Check(Regex.IsMatch(film, @"bool FilmTableHasMe\(int pid\)[\s\S]{0,300}Networking\.GetOwner\(_filmBox\.gameObject\)[\s\S]{0,120}o\.playerId != pid"), "the gate checks that the subject's box is owned by the subject");
            Check(Regex.IsMatch(film, @"filmGrantIds\[slot\] = from; filmGrantSeqs\[slot\] = seq;[\s\S]{0,400}RequestSerialization\(\);[\s\S]{0,200}AnswerIncoming\(CodeYes\)"), "[いいよ] writes the table and serialises it before the display-only reply");
            Check(!Regex.IsMatch(film, @"HandleFilmAnswer[\s\S]{0,600}filmTargetId = to"), "a yes reply alone never declares in-use (the table must arrive first, §13.2)");
            Check(Regex.IsMatch(main, @"_JoinCancelable\(\)[\s\S]{0,160}_outKind != KindFilm") && Regex.IsMatch(main, @"_WaitingFor\(\) \{ return _outKind == KindFilm \? -1 : _outTo; \}"), "the join cancel / waiting display never treats a film request as a join (§13.6)");
            Check(Regex.IsMatch(main, @"kind == KindFilm \? !filmAccepting : !accepting"), "a film request is refused by filmAccepting (not the join toggle)");
            Check(film.Contains("[UdonSynced] public int[] filmGrantIds = new int[] { -1, -1, -1, -1 };") && film.Contains("[UdonSynced] public int[] filmGrantSeqs = new int[] { 0, 0, 0, 0 };"), "synced arrays initialised at declaration (length 4)");
            string camSrc = File.ReadAllText("Assets/KominkaHillside/Scripts/KH_VideoCamera.cs");
            Check(Regex.IsMatch(camSrc, @"if \(_personLive && !PersonStillUsable\(\)\) PersonDrop\(\);\s*if \(now < _nextRender\) return;"), "the person picture is checked every frame before the render-tick wait and the fade return (Codex review 1)");
            Check(Regex.IsMatch(film, @"void FilmClear\(\)[\s\S]{0,500}_filmListener\.SendCustomEvent\(""_OnFilmEnded""\)"), "every session end tells the camera (_OnFilmEnded)");
        }
    }
}
