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
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-WLD-001 段階 3: 合流 (設計書 §A.4〜A.7・§B.5・§D.3〜D.5)。
    ///
    /// Course/85 は次を行い、本番シーンを保存する (経路・目的地・禁止された再生成メニューには触らない):
    /// 1. KH_TravelMailbox / KH_TravelNotice の Program Asset が無ければ作ってコンパイルを始め、**シーンは変えずに止まる** (もう一度 85 を流す)。
    /// 2. 受付箱の雛形 KH_TravelMailbox (VRCPlayerObject + Manual、同期は accepting 1 本) をシーンの一番上に置く (入室ごとに複製される)。
    /// 3. 通知板 KH_TravelNotice (WorldSpace Canvas + VRCUiShape + BoxCollider、Layer Default) を置く。
    /// 4. 副画面に [地点] / [人] の切替・人の一覧 (16 行、スクロール)・「合流の申し込みを受ける」、さんぽタブ上段に「申し込み 1 件」を足す。
    /// 5. 翻訳表 (course.travel.join.*) を入れ直す。
    /// 検査は Course/81 が <see cref="Validate"/> を呼ぶ。</summary>
    public static class KH_CourseTravelJoinEditor
    {
        const string OutDir = "Temp/imp-wld-001";
        public const string MailboxProgram = "Assets/KominkaHillside/Scripts/KH_TravelMailbox", NoticeProgram = "Assets/KominkaHillside/Scripts/KH_TravelNotice";
        public const string MailboxName = "KH_TravelMailbox", NoticeName = "KH_TravelNotice", BoardName = "Board";
        public const string PendingName = "TravelJoinPending", PeopleName = "People", AcceptName = "AcceptToggle", ModeSpotsName = "ModeSpots", ModePeopleName = "ModePeople";
        public const int PeopleRows = 16;
        const float RowHeight = 56f, RowPitch = 60f, PeopleWidth = 590f, PeopleHeight = 176f;
        static readonly Vector2 PeoplePos = new Vector2(155f, 52f);
        static readonly Vector2 BoardSize = new Vector2(760f, 380f);
        static readonly string[] NoticeEvents = { "_Yes", "_No", "_Pause", "_Block" };

        [Serializable] class BuildReport { public string utc, scene, status, reason; public bool programAssetsCreated; public int droppedNetworkIds; }

        [MenuItem("Tools/KominkaHillside/Course/85 - Build Travel Join")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var report = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            Directory.CreateDirectory(OutDir);
            bool created = false;
            foreach (var path in new[] { MailboxProgram, NoticeProgram })
            {
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path + ".asset") != null) continue;
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path + ".cs");
                if (asset.sourceCsScript == null) throw new InvalidOperationException(path + ".cs not imported yet; refresh first");
                AssetDatabase.CreateAsset(asset, path + ".asset"); created = true;
            }
            if (created)
            {
                AssetDatabase.SaveAssets(); UdonSharpProgramAsset.CompileAllCsPrograms(true);
                report.status = "PROGRAM_ASSET_CREATED"; report.programAssetsCreated = true;
                report.reason = "Created KH_TravelMailbox.asset / KH_TravelNotice.asset. Wait for the U# compile (unity-compile.sh --udon), then run Course/85 again. Scene unchanged.";
                File.WriteAllText(OutDir + "/travel-join-build.json", JsonUtility.ToJson(report, true));
                Debug.Log("[KH Travel Join] " + report.reason);
                return;
            }
            var p = KH_CourseTravelEditor.FindPouch(); var m = p.manager;
            if (p.travelMapPage == null || p.travelOpenButton == null) throw new InvalidOperationException("Map page missing: run Course/84 first");
            var pouchBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            var font = p.sectionTitleLabel != null ? p.sectionTitleLabel.font : null;
            if (pouchBacking == null || font == null) throw new InvalidOperationException("Menu backing / font missing");
            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-travel-join.unity", true);
            var log = new StringBuilder();
            try
            {
                var scene = p.gameObject.scene;
                // ---- 通知板 ----
                var noticeRoot = scene.GetRootGameObjects().FirstOrDefault(g => g.name == NoticeName) ?? new GameObject(NoticeName);
                noticeRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); noticeRoot.transform.localScale = Vector3.one; noticeRoot.SetActive(true);
                var notice = noticeRoot.GetComponent<KH_TravelNotice>(); if (notice == null) notice = noticeRoot.AddUdonSharpComponent<KH_TravelNotice>();
                var noticeBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(notice);
                if (noticeBacking == null) throw new InvalidOperationException("KH_TravelNotice backing missing (compile U# and run again)");
                if (noticeBacking.SyncMethod != Networking.SyncType.None) { noticeBacking.SyncMethod = Networking.SyncType.None; EditorUtility.SetDirty(noticeBacking); }
                var boardT = noticeRoot.transform.Find(BoardName);
                GameObject board = boardT != null ? boardT.gameObject : new GameObject(BoardName, typeof(RectTransform));
                board.transform.SetParent(noticeRoot.transform, false); board.layer = 0;
                var brt = (RectTransform)board.transform; brt.sizeDelta = BoardSize; brt.localPosition = Vector3.zero; brt.localRotation = Quaternion.identity; brt.localScale = Vector3.one * 0.001f;
                var canvas = board.GetComponent<Canvas>(); if (canvas == null) canvas = board.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
                if (board.GetComponent<GraphicRaycaster>() == null) board.AddComponent<GraphicRaycaster>();
                if (board.GetComponent<VRCUiShape>() == null) board.AddComponent<VRCUiShape>();
                var col = board.GetComponent<BoxCollider>(); if (col == null) col = board.AddComponent<BoxCollider>(); col.size = new Vector3(BoardSize.x, BoardSize.y, 2f); col.isTrigger = true;
                var bg = board.GetComponent<Image>(); if (bg == null) bg = board.AddComponent<Image>(); bg.color = new Color(.14f, .12f, .11f, .96f); bg.raycastTarget = true;
                KD_UiSpriteEditor.ApplyRoundSkin(bg, KD_UiSpriteEditor.LoadRound());
                notice.board = board; notice.language = p.language; notice.manager = m;
                notice.distance = 1.1f; notice.lowerDegrees = 15f; notice.replaceMeters = 3f;
                // 通知音 (§A.4「音 1 回」。W3 レビュー 15): 手元メニューの成功音を流用。2D、1 回だけ鳴らす
                var chime = noticeRoot.GetComponent<AudioSource>(); if (chime == null) chime = noticeRoot.AddComponent<AudioSource>();
                chime.clip = p.uiSuccessClip; chime.playOnAwake = false; chime.loop = false; chime.spatialBlend = 0f; chime.volume = 0.6f;
                notice.chime = chime;
                notice.titleLabel = MakeText(board.transform, "Title", new Vector2(-30f, 130f), new Vector2(660f, 50f), 26f, font, Ja("course.travel.join.notice.title").Replace("{0}", "VRChatUser"));
                notice.countLabel = MakeText(board.transform, "Count", new Vector2(330f, 130f), new Vector2(60f, 44f), 26f, font, "20");
                notice.placeLabel = MakeText(board.transform, "Place", new Vector2(0f, 78f), new Vector2(720f, 36f), 20f, font, Ja("course.travel.join.near").Replace("{0}", Ja("course.walk.upper")));
                var labels = new TextMeshProUGUI[4];
                Vector2[] pos = { new Vector2(-175f, -10f), new Vector2(175f, -10f), new Vector2(0f, -90f), new Vector2(0f, -148f) };
                Vector2[] size = { new Vector2(300f, 80f), new Vector2(300f, 80f), new Vector2(700f, 48f), new Vector2(700f, 48f) };
                string[] keys = { "course.travel.join.notice.yes", "course.travel.join.notice.no", "course.travel.join.notice.pause", "course.travel.join.notice.block2" };
                for (int i = 0; i < 4; i++)
                {
                    var b = KD_ShopSetupEditor.EnsureButton(board.transform, "Button" + i, pos[i], size[i], i == 0 ? KD_UiSpriteEditor.ActionNeutral * 1.2f : KD_UiSpriteEditor.ActionNeutral, font, Ja(keys[i]), noticeBacking, NoticeEvents[i], log);
                    labels[i] = b.transform.Find("Label").GetComponent<TextMeshProUGUI>(); labels[i].fontSize = i < 2 ? 26f : 18f;
                }
                notice.yesLabel = labels[0]; notice.noLabel = labels[1]; notice.pauseLabel = labels[2]; notice.blockLabel = labels[3];
                board.SetActive(false);
                UdonSharpEditorUtility.CopyProxyToUdon(notice); EditorUtility.SetDirty(notice);

                // ---- 受付箱の雛形 (VRCPlayerObject) ----
                var boxGo = scene.GetRootGameObjects().FirstOrDefault(g => g.name == MailboxName) ?? new GameObject(MailboxName);
                boxGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); boxGo.SetActive(true);
                if (boxGo.GetComponent<VRCPlayerObject>() == null) boxGo.AddComponent<VRCPlayerObject>();
                var box = boxGo.GetComponent<KH_TravelMailbox>(); if (box == null) box = boxGo.AddUdonSharpComponent<KH_TravelMailbox>();
                var boxBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(box);
                if (boxBacking == null) throw new InvalidOperationException("KH_TravelMailbox backing missing (compile U# and run again)");
                if (boxBacking.SyncMethod != Networking.SyncType.Manual) { boxBacking.SyncMethod = Networking.SyncType.Manual; EditorUtility.SetDirty(boxBacking); }
                box.manager = m; box.notice = notice; box.accepting = true; box.debugJoinLoopback = 0; box.debugAnswerCode = 0;
                UdonSharpEditorUtility.CopyProxyToUdon(box); EditorUtility.SetDirty(box);

                // ---- さんぽタブ上段: [地図と移動] は左、「申し込み 1 件」は右 ----
                var walk = p.tabPages[KD_TreatPouch.TabWalk].transform;
                var openRt = (RectTransform)p.travelOpenButton.transform; openRt.anchoredPosition = new Vector2(-272f, KH_CourseTravelEditor.TopRowY);
                var pending = KD_ShopSetupEditor.EnsureButton(walk, PendingName, new Vector2(272f, KH_CourseTravelEditor.TopRowY), new Vector2(530f, 40f),
                    new Color(.62f, .38f, .18f), font, Ja("course.travel.join.pending"), pouchBacking, "_TravelShowNotice", log);
                var pendingLabel = pending.transform.Find("Label").GetComponent<TextMeshProUGUI>(); pendingLabel.fontSize = 20f;
                pending.SetActive(false);

                // ---- 副画面: 切替・受け付け・人の一覧 ----
                var page = p.travelMapPage.transform;
                var modeSpots = KD_ShopSetupEditor.EnsureButton(page, ModeSpotsName, new Vector2(190f, 164f), new Vector2(90f, 44f), KD_UiSpriteEditor.ActionNeutral, font, Ja("course.travel.join.spots"), pouchBacking, "_TravelModeSpots", log);
                var modePeople = KD_ShopSetupEditor.EnsureButton(page, ModePeopleName, new Vector2(290f, 164f), new Vector2(90f, 44f), KD_UiSpriteEditor.ActionNeutral, font, Ja("course.travel.join.people"), pouchBacking, "_TravelModePeople", log);
                var accept = KD_ShopSetupEditor.EnsureButton(page, AcceptName, new Vector2(-330f, 164f), new Vector2(400f, 44f), KD_UiSpriteEditor.ActionNeutral, font, Ja("course.travel.join.accept.on"), pouchBacking, "_TravelToggleAccept", log);
                var acceptLabel = accept.transform.Find("Label").GetComponent<TextMeshProUGUI>(); acceptLabel.fontSize = 20f;
                accept.SetActive(false);
                var people = KD_ShopSetupEditor.EnsureUiChild(page, PeopleName, PeoplePos, new Vector2(PeopleWidth, PeopleHeight));
                var viewport = KD_ShopSetupEditor.EnsureUiChild(people.transform, "Viewport", Vector2.zero, new Vector2(PeopleWidth, PeopleHeight));
                if (viewport.GetComponent<RectMask2D>() == null) viewport.AddComponent<RectMask2D>();
                var vImg = viewport.GetComponent<Image>(); if (vImg == null) vImg = viewport.AddComponent<Image>(); vImg.color = new Color(0f, 0f, 0f, 0f); vImg.raycastTarget = true;
                float contentH = PeopleRows * RowPitch;
                var content = KD_ShopSetupEditor.EnsureUiChild(viewport.transform, "Content", Vector2.zero, new Vector2(PeopleWidth, contentH));
                var crt = (RectTransform)content.transform; crt.anchorMin = crt.anchorMax = new Vector2(.5f, 1f); crt.pivot = new Vector2(.5f, 1f); crt.anchoredPosition = Vector2.zero;
                var scroll = people.GetComponent<ScrollRect>(); if (scroll == null) scroll = people.AddComponent<ScrollRect>();
                scroll.viewport = (RectTransform)viewport.transform; scroll.content = crt; scroll.horizontal = false; scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped; scroll.inertia = false; scroll.scrollSensitivity = 35f;
                var rows = new Button[PeopleRows]; var rowImages = new Image[PeopleRows]; var rowLabels = new TextMeshProUGUI[PeopleRows];
                for (int i = 0; i < PeopleRows; i++)
                {
                    var row = KD_ShopSetupEditor.EnsureButton(content.transform, "Person" + i, Vector2.zero, new Vector2(PeopleWidth - 20f, RowHeight),
                        KD_UiSpriteEditor.ActionNeutral, font, "VRChatUser    " + Ja("course.travel.join.near").Replace("{0}", Ja("course.walk.upper")) + "  35 m", pouchBacking, "_JoinPick" + i, log);
                    rows[i] = row.GetComponent<Button>(); rowImages[i] = row.GetComponent<Image>();
                    rowLabels[i] = row.transform.Find("Label").GetComponent<TextMeshProUGUI>(); rowLabels[i].fontSize = 18f; rowLabels[i].alignment = TextAlignmentOptions.Left; rowLabels[i].overflowMode = TextOverflowModes.Ellipsis; rowLabels[i].lineSpacing = -10f;
                    // 行は content の上端から並べる (人数で content の高さを変えても行が動かない。W3 レビュー 13)
                    var rrt = (RectTransform)row.transform; rrt.anchorMin = rrt.anchorMax = new Vector2(.5f, 1f); rrt.pivot = new Vector2(.5f, 1f);
                    rrt.anchoredPosition = new Vector2(0f, -2f - i * RowPitch);
                    row.SetActive(false);
                }
                var empty = MakeText(people.transform, "Empty", Vector2.zero, new Vector2(PeopleWidth, 40f), 20f, font, Ja("course.travel.join.alone"));
                people.SetActive(false);

                p.travelModeSpotsImage = modeSpots.GetComponent<Image>(); p.travelModePeopleImage = modePeople.GetComponent<Image>();
                p.travelModeSpotsLabel = modeSpots.transform.Find("Label").GetComponent<TextMeshProUGUI>(); p.travelModePeopleLabel = modePeople.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                p.travelModeSpotsLabel.fontSize = 20f; p.travelModePeopleLabel.fontSize = 20f;
                p.travelPeopleContent = crt;
                p.travelPeopleRoot = people; p.travelPeopleButtons = rows; p.travelPeopleImages = rowImages; p.travelPeopleLabels = rowLabels; p.travelPeopleEmpty = empty;
                p.travelAcceptButton = accept; p.travelAcceptLabel = acceptLabel;
                p.travelJoinPendingButton = pending; p.travelJoinPendingLabel = pendingLabel;
                p.travelNotice = notice;

                var descriptor = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
                if (descriptor != null) { int dropped = descriptor.NetworkIDCollection.RemoveAll(x => x.gameObject == null && (x.SerializedTypeNames == null || x.SerializedTypeNames.Count == 0)); if (dropped > 0) { report.droppedNetworkIds = dropped; EditorUtility.SetDirty(descriptor); } }
                KH_CourseTravelEditor.ApplyCatalog(p);
                UdonSharpEditorUtility.CopyProxyToUdon(p); EditorUtility.SetDirty(p);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Travel join build save failed");
            }
            catch
            {
                Debug.LogError("[KH Travel Join] Build failed; the scene before this run is at " + OutDir + "/before-travel-join.unity. Reopen the scene without saving to discard partial changes.");
                throw;
            }
            report.status = "BUILT";
            File.WriteAllText(OutDir + "/travel-join-build.json", JsonUtility.ToJson(report, true));
            Debug.Log("[KH Travel Join] BUILT mailbox template + notice + people list. Run Course/27 (glyphs) then Course/81.");
        }

        // ============================================================
        // Course/81 から呼ぶ検査
        // ============================================================

        public static void Validate(KD_TreatPouch p, List<string> e, KH_CourseTravelEditor.Report report)
        {
            var m = p.manager;
            var boxes = UnityEngine.Object.FindObjectsOfType<KH_TravelMailbox>(true);
            Req(e, boxes.Length == 1, "exactly one KH_TravelMailbox template");
            var notices = UnityEngine.Object.FindObjectsOfType<KH_TravelNotice>(true);
            Req(e, notices.Length == 1, "exactly one KH_TravelNotice");
            if (boxes.Length != 1 || notices.Length != 1) return;
            var box = boxes[0]; var notice = notices[0];
            var bb = UdonSharpEditorUtility.GetBackingUdonBehaviour(box); var nb = UdonSharpEditorUtility.GetBackingUdonBehaviour(notice);
            Req(e, box.GetComponent<VRCPlayerObject>() != null && box.gameObject.activeInHierarchy, "mailbox template has VRCPlayerObject and is active");
            Req(e, bb != null && bb.SyncMethod == Networking.SyncType.Manual, "mailbox sync=Manual");
            Req(e, box.accepting && box.debugJoinLoopback == 0 && box.debugAnswerCode == 0, "mailbox defaults: accepting ON, debug 0");
            Req(e, box.manager == m && box.notice == notice, "mailbox wired (proxy)");
            if (bb != null)
            {
                Req(e, bb.publicVariables.TryGetVariableValue("notice", out object v1) && ReferenceEquals(v1, nb), "mailbox.notice wired (backing)");
                Req(e, bb.publicVariables.TryGetVariableValue("debugJoinLoopback", out object v2) && v2 is int d2 && d2 == 0, "mailbox debugJoinLoopback 0 (backing)");
            }
            Req(e, nb != null && nb.SyncMethod == Networking.SyncType.None, "notice sync=None");
            var board = notice.board;
            Req(e, board != null && board.transform.parent == notice.transform && !board.activeSelf, "notice board child, inactive at start");
            if (board != null)
            {
                Req(e, board.layer == 0 && board.GetComponent<Canvas>() != null && board.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace
                    && board.GetComponent<VRCUiShape>() != null && board.GetComponent<BoxCollider>() != null && board.GetComponent<GraphicRaycaster>() != null, "notice board: WorldSpace Canvas + VRCUiShape + BoxCollider + Raycaster on Default");
                for (int i = 0; i < 4; i++) { var b = board.transform.Find("Button" + i); if (b == null) e.Add("FAIL notice button " + i); else CheckEvent(e, b.gameObject, nb, NoticeEvents[i]); }
            }
            // W3 レビュー 10: 配置の値・Collider・通知音・一覧のスクロールと行の対応
            Req(e, Mathf.Approximately(notice.distance, 1.1f) && Mathf.Approximately(notice.lowerDegrees, 15f) && Mathf.Approximately(notice.replaceMeters, 3f), "notice placement 1.1 m / 15 deg / 3 m");
            Req(e, notice.chime != null && notice.chime.clip != null && !notice.chime.playOnAwake && !notice.chime.loop, "notice chime wired (one shot)");
            if (board != null)
            {
                var bcol = board.GetComponent<BoxCollider>(); var brect = (RectTransform)board.transform;
                Req(e, bcol != null && Mathf.Approximately(bcol.size.x, brect.sizeDelta.x) && Mathf.Approximately(bcol.size.y, brect.sizeDelta.y) && Mathf.Approximately(brect.localScale.x, 0.001f), "notice collider matches the board (scale 0.001)");
            }
            var scroll = p.travelPeopleRoot != null ? p.travelPeopleRoot.GetComponent<ScrollRect>() : null;
            Req(e, scroll != null && scroll.content == p.travelPeopleContent && scroll.viewport != null && scroll.viewport.GetComponent<RectMask2D>() != null && !scroll.horizontal && scroll.vertical, "people ScrollRect wired (viewport mask, content)");
            if (p.travelPeopleButtons != null && p.travelPeopleContent != null)
                for (int i = 0; i < p.travelPeopleButtons.Length; i++)
                {
                    var b = p.travelPeopleButtons[i];
                    Req(e, b != null && b.transform.parent == p.travelPeopleContent && b.name == "Person" + i && p.travelPeopleImages[i] == b.GetComponent<Image>() && p.travelPeopleLabels[i] == b.transform.Find("Label").GetComponent<TextMeshProUGUI>(), "people row " + i + " matches the arrays");
                    var rt = b != null ? (RectTransform)b.transform : null;
                    Req(e, rt != null && Mathf.Approximately(rt.anchorMin.y, 1f) && Mathf.Approximately(rt.anchoredPosition.y, -2f - i * RowPitch), "people row " + i + " anchored to the content top");
                }
            Req(e, notice.language == p.language && notice.manager == m && notice.titleLabel != null && notice.placeLabel != null && notice.countLabel != null && notice.yesLabel != null && notice.noLabel != null && notice.pauseLabel != null && notice.blockLabel != null, "notice wired");
            // 手元メニュー
            var pb = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            var walk = p.tabPages[KD_TreatPouch.TabWalk].transform;
            Req(e, p.travelJoinPendingButton != null && p.travelJoinPendingButton.transform.parent == walk && !p.travelJoinPendingButton.activeSelf, "pending button under Page_Walk, hidden at start");
            if (p.travelJoinPendingButton != null) CheckEvent(e, p.travelJoinPendingButton, pb, "_TravelShowNotice");
            var page = p.travelMapPage != null ? p.travelMapPage.transform : null;
            Req(e, page != null && p.travelPeopleRoot != null && p.travelPeopleRoot.transform.parent == page && !p.travelPeopleRoot.activeSelf, "people list on the map page, hidden at start");
            Req(e, p.travelPeopleButtons != null && p.travelPeopleButtons.Length == PeopleRows && p.travelPeopleImages != null && p.travelPeopleImages.Length == PeopleRows && p.travelPeopleLabels != null && p.travelPeopleLabels.Length == PeopleRows, "16 people rows");
            if (p.travelPeopleButtons != null) for (int i = 0; i < p.travelPeopleButtons.Length; i++) if (p.travelPeopleButtons[i] != null) CheckEvent(e, p.travelPeopleButtons[i].gameObject, pb, "_JoinPick" + i);
            if (page != null)
            {
                foreach (var (name, evt) in new[] { (ModeSpotsName, "_TravelModeSpots"), (ModePeopleName, "_TravelModePeople"), (AcceptName, "_TravelToggleAccept") })
                { var t = page.Find(name); if (t == null) e.Add("FAIL " + name); else CheckEvent(e, t.gameObject, pb, evt); }
            }
            Req(e, p.travelNotice == notice && p.travelAcceptLabel != null && p.travelPeopleEmpty != null && p.travelModeSpotsLabel != null && p.travelModePeopleLabel != null, "pouch join fields wired");
            foreach (var field in new[] { "travelNotice", "travelPeopleRoot", "travelAcceptButton", "travelJoinPendingButton" })
                if (pb == null || !pb.publicVariables.TryGetVariableValue(field, out object v) || v == null) e.Add("FAIL backing " + field);
            if (pb == null || !pb.publicVariables.TryGetVariableValue("travelPeopleButtons", out object arr) || !(arr is Array a) || a.Length != PeopleRows) e.Add("FAIL backing travelPeopleButtons");
            // 文言の収まり (日英)
            var go = new GameObject("TravelJoinMeasure", typeof(RectTransform)); go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var sample = go.AddComponent<TextMeshProUGUI>(); var lang = p.language;
                foreach (string code in new[] { "ja", "en" })
                {
                    string T(string key) { return lang._TextForLanguage(key, code); }
                    Fit(sample, p.travelAcceptLabel, T("course.travel.join.accept.off"), code + " accept", e, report);
                    Fit(sample, p.travelModeSpotsLabel, T("course.travel.join.spots"), code + " mode spots", e, report);
                    Fit(sample, p.travelModePeopleLabel, T("course.travel.join.people"), code + " mode people", e, report);
                    Fit(sample, p.travelJoinPendingLabel, T("course.travel.join.pending"), code + " pending", e, report);
                    // 表示名は VRChat の上限に近い 15 字で測る (2 段。1 行目 = 名前、2 行目 = 地点・距離・状態)
                    Fit(sample, p.travelPeopleLabels[0], "VRChatUser12345\n" + T("course.travel.join.near").Replace("{0}", T("course.travel.join.spot.bridgeDown")) + "  999 m  (" + T("course.travel.join.notAccepting") + ")", code + " person row", e, report);
                    Fit(sample, p.travelGoLabel, T("course.travel.join.cancel").Replace("{0}", "20"), code + " go cancel", e, report);
                    Fit(sample, p.travelGoLabel, T("course.travel.join.ask"), code + " go ask", e, report);
                    Fit(sample, notice.titleLabel, T("course.travel.join.notice.title").Replace("{0}", "VRChatUser12"), code + " notice title", e, report);
                    Fit(sample, notice.yesLabel, T("course.travel.join.notice.yes"), code + " notice yes", e, report);
                    Fit(sample, notice.noLabel, T("course.travel.join.notice.no"), code + " notice no", e, report);
                    Fit(sample, notice.pauseLabel, T("course.travel.join.notice.pause"), code + " notice pause", e, report);
                    Fit(sample, notice.blockLabel, T("course.travel.join.notice.block2"), code + " notice block", e, report);
                    foreach (var key in new[] { "code0", "code2", "code3", "left", "again", "nosafe", "canceled", "busy", "notReady", "reason.near", "reason.behind", "reason.bridge", "reason.water", "reason.offtrail", "arrived", "dogs" })
                        Fit(sample, p.travelStatusLabel, T("course.travel.join." + key).Replace("{0}", "20"), code + " status join." + key, e, report);
                    Fit(sample, p.travelStatusLabel, T("course.travel.join.waiting").Replace("{0}", "20"), code + " status join.waiting", e, report);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static void Fit(TextMeshProUGUI sample, TMP_Text target, string text, string name, List<string> e, KH_CourseTravelEditor.Report report)
        {
            if (target == null) { e.Add("FAIL label missing: " + name); return; }
            int result = KD_LocalizationEditor.MeasureLabelFit(sample, target, text, out Vector2 preferred);
            report.labelFit.Add(name + " preferred=" + preferred + " rect=" + target.rectTransform.rect.size + " fit=" + result);
            if (result == 2) e.Add("Label overflow: " + name);
        }

        static void CheckEvent(List<string> e, GameObject go, VRC.Udon.UdonBehaviour target, string evt)
        {
            var b = go.GetComponent<Button>();
            if (b == null || b.onClick.GetPersistentEventCount() != 1 || b.onClick.GetPersistentTarget(0) != target || b.onClick.GetPersistentMethodName(0) != "SendCustomEvent") { e.Add("FAIL button target " + go.name); return; }
            var call = new SerializedObject(b).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (call.arraySize != 1 || call.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue != evt) e.Add("FAIL button event " + go.name + " != " + evt);
        }

        static void Req(List<string> e, bool ok, string what) { if (!ok) e.Add("FAIL " + what); }

        static string Ja(string key)
        {
            var c = JsonUtility.FromJson<KD_LocalizationEditor.Catalog>(File.ReadAllText(KH_CourseMenuEditor.CatalogPath));
            return KD_LocalizationEditor.Text(c, "ja", key);
        }

        static TextMeshProUGUI MakeText(Transform parent, string name, Vector2 pos, Vector2 size, float points, TMP_FontAsset font, string text)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var t = go.GetComponent<TextMeshProUGUI>(); if (t == null) t = go.AddComponent<TextMeshProUGUI>();
            t.font = font; t.fontSize = points; t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false; t.raycastTarget = false;
            t.color = Color.white; t.text = text; EditorUtility.SetDirty(t);
            return t;
        }
    }
}
