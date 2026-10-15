using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    /// 4. 副画面に [地点] / [人] の切替・人の一覧・「合流の申し込みを受ける」、さんぽタブ上段に「申し込み 1 件」を足す。
    ///    DES-UI-006 (2026-10-15): 人の一覧は 16 行のスクロールから 3 列 × 3 段のセル (Person0..8、1 行目 Label = 表示名 / 2 行目 Info / セル 0・8 の Pager = ◀ ▶) へ。
    ///    旧スクロール (ScrollRect / Viewport / Content / Person0..15) は撤去する (外から参照されていたら変えずに止まる)。[人] のとき地図を隠すため Mark1〜6 を配線する。
    ///    退避は Temp/des-ui-006/before-people-grid.unity。何度流しても同じ結果。
    /// 5. 翻訳表 (course.travel.join.*) を入れ直す。
    /// IMP-WLD-002 (設計書 §F.2): 通知板に呼ぶ用の [行く] (Button4) / [今は行かない] (Button5) と理由の行、副画面の [人] のフッターに
    /// [合流をたのむ] (JoinAsk → _TravelGo) / [来てもらう] (JoinCall → _TravelCall) を横に 2 つ (245×90、間隔 10) 足す。
    /// 検査は Course/81 が <see cref="Validate"/> を呼ぶ。</summary>
    public static class KH_CourseTravelJoinEditor
    {
        const string OutDir = "Temp/imp-wld-001";
        public const string MailboxProgram = "Assets/KominkaHillside/Scripts/KH_TravelMailbox", NoticeProgram = "Assets/KominkaHillside/Scripts/KH_TravelNotice";
        public const string MailboxName = "KH_TravelMailbox", NoticeName = "KH_TravelNotice", BoardName = "Board";
        public const string PendingName = "TravelJoinPending", PeopleName = "People", AcceptName = "AcceptToggle", ModeSpotsName = "ModeSpots", ModePeopleName = "ModePeople";
        public const string AskName = "JoinAsk", CallName = "JoinCall", ReasonName = "Reason";
        // [人] のフッター: Course/84 の Go (中心 155, -205、500×90) を左右に分ける
        public static readonly Vector2 AskPos = new Vector2(27.5f, -205f), CallPos = new Vector2(282.5f, -205f), HalfSize = new Vector2(245f, 90f);
        // DES-UI-006 §5.2 案 C: [人] のとき地図を隠した帯 (x -540〜450 / y 140〜-160。上 = 上段の行の下端 142、下 = 2 ボタンの上端 -160) に 3 列 × 3 段、間隔 10
        public const int PeopleCells = KD_TreatPouch.PeopleCells;
        public const float BandLeft = -540f, BandRight = 450f, BandTop = 140f, BandBottom = -160f, CellGap = 10f;
        public static readonly Vector2 CellSize = new Vector2((BandRight - BandLeft - 2f * CellGap) / 3f, (BandTop - BandBottom - 2f * CellGap) / 3f);   // 323.33 × 93.33
        public static readonly Vector2 PeoplePos = new Vector2((BandLeft + BandRight) / 2f, (BandTop + BandBottom) / 2f), PeopleSize = new Vector2(BandRight - BandLeft, BandTop - BandBottom);
        /// <summary>セル c の中心 (副画面の座標。c = 段 × 3 + 列)。セルは People (帯の中心) の子なので、置くときは PeoplePos を引く。</summary>
        public static Vector2 CellPos(int c)
        {
            int col = c % 3, line = c / 3;
            return new Vector2(BandLeft + CellSize.x * (col + .5f) + CellGap * col, BandTop - CellSize.y * (line + .5f) - CellGap * line);
        }
        // セルの中: 1 行目 = 表示名 20pt (補助フォント、… で切る)、2 行目 = 地点と距離か状態 16pt (共通フォント、収まらなければ 12pt まで縮める)、
        // セル 0 / 8 の Pager = ◀ / ▶ と「p / q」24pt (共通フォント)。高さは §10.1 の 1 行 (ascent − descent ≒ 1.45 em) に余裕を持たせる
        const float CellPadX = 24f, NamePoints = 20f, InfoPoints = 16f, InfoMinPoints = 12f, PagerPoints = 24f, NameHeight = 40f, InfoHeight = 34f;
        static float NameY { get { return CellSize.y / 2f - 6f - NameHeight / 2f; } }
        static float InfoY { get { return -CellSize.y / 2f + 6f + InfoHeight / 2f; } }
        // 1 ページ 7 人で 80 人なら 12 ページ (ページ送りのセルの測定に使う最長の「p / q」)
        static string LongestPage { get { int q = (KD_TreatPouch.JoinMax + KD_TreatPouch.PeoplePerPage - 1) / KD_TreatPouch.PeoplePerPage; return q + " / " + q; } }
        const string GridBackupDir = "Temp/des-ui-006";
        const string SharedFontPath = "Assets/KominkaHillside/Font/KD_NotoSerif SDF.asset";   // = KD_ShopSetupEditor.FontAssetPath (private)
        static readonly Vector2 BoardSize = new Vector2(760f, 380f);
        static readonly string[] NoticeEvents = { "_Yes", "_No", "_Pause", "_Block", "_Go", "_NotNow" };
        // ネットワークから呼べる入口 (§F.3)。名前と引数の型をこの 7 つに固定する (IMP-WLD-008 W4: 撮影の ReqFilm / EndFilm / FilmRevoked は KH_TravelMailbox.Film.cs)
        static readonly string[] NetworkEntries = { "ReqJoin(int seq)", "ReqCall(int seq)", "AnsJoin(int seq, int code)", "CancelJoin(int seq)", "ReqFilm(int seq)", "EndFilm(int seq)", "FilmRevoked(int seq)" };

        [Serializable] class BuildReport { public string utc, scene, status, reason; public bool programAssetsCreated; public int droppedNetworkIds; public List<string> findings = new List<string>(), info = new List<string>(); }

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
            Directory.CreateDirectory(GridBackupDir);
            File.Copy(KH_CourseGroundEditor.ScenePath, GridBackupDir + "/before-people-grid.unity", true);
            // DES-UI-006: 撤去する旧スクロールの部品を、手元メニュー以外が参照していないこと (消すと相手の参照が黙って null になる。vrc-scene-edit-safety)。シーンを変える前に止める
            var oldPeople = p.travelMapPage.transform.Find(PeopleName);
            if (oldPeople != null)
            {
                var refs = OldScrollRefs(oldPeople, p, pouchBacking);
                if (refs.Count > 0) throw new InvalidOperationException("The old people scroll is referenced from outside the menu; scene unchanged: " + string.Join("; ", refs.Take(10)));
            }
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
                KH_AudioSpatialEditor.Configure(chime);
                notice.chime = chime;
                notice.titleLabel = MakeText(board.transform, "Title", new Vector2(-30f, 130f), new Vector2(660f, 50f), 26f, font, Ja("course.travel.join.notice.title").Replace("{0}", "VRChatUser"));
                notice.countLabel = MakeText(board.transform, "Count", new Vector2(330f, 130f), new Vector2(60f, 44f), 26f, font, "20");
                notice.placeLabel = MakeText(board.transform, "Place", new Vector2(0f, 78f), new Vector2(720f, 36f), 20f, font, Ja("course.travel.join.near").Replace("{0}", Ja("course.walk.upper")));
                var labels = new TextMeshProUGUI[6]; var buttons = new GameObject[6];
                // 4・5 は呼ぶ用の別の実体 (§F.2)。合流の 0・1 と同じ位置に置き、種類で出し分ける
                Vector2[] pos = { new Vector2(-175f, -10f), new Vector2(175f, -10f), new Vector2(0f, -90f), new Vector2(0f, -148f), new Vector2(-175f, -10f), new Vector2(175f, -10f) };
                Vector2[] size = { new Vector2(300f, 80f), new Vector2(300f, 80f), new Vector2(700f, 48f), new Vector2(700f, 48f), new Vector2(300f, 80f), new Vector2(300f, 80f) };
                string[] keys = { "course.travel.join.notice.yes", "course.travel.join.notice.no", "course.travel.join.notice.pause", "course.travel.join.notice.block2", "course.travel.call.notice.go", "course.travel.call.notice.notnow" };
                for (int i = 0; i < 6; i++)
                {
                    bool primary = i == 0 || i == 4;
                    var b = KD_ShopSetupEditor.EnsureButton(board.transform, "Button" + i, pos[i], size[i], primary ? KD_UiSpriteEditor.ActionNeutral * 1.2f : KD_UiSpriteEditor.ActionNeutral, font, Ja(keys[i]), noticeBacking, NoticeEvents[i], log);
                    labels[i] = b.transform.Find("Label").GetComponent<TextMeshProUGUI>(); labels[i].fontSize = (i < 2 || i >= 4) ? 26f : 18f;
                    labels[i].enableAutoSizing = true; labels[i].fontSizeMin = 14f; labels[i].fontSizeMax = labels[i].fontSize; EditorUtility.SetDirty(labels[i]);
                    buttons[i] = b;
                    b.SetActive(i < 4);
                }
                notice.yesLabel = labels[0]; notice.noLabel = labels[1]; notice.pauseLabel = labels[2]; notice.blockLabel = labels[3];
                notice.goLabel = labels[4]; notice.notNowLabel = labels[5];
                notice.joinButtons = new[] { buttons[0], buttons[1] }; notice.callButtons = new[] { buttons[4], buttons[5] };
                notice.reasonLabel = MakeText(board.transform, ReasonName, new Vector2(0f, 45f), new Vector2(720f, 28f), 18f, font, Ja("course.travel.join.nosafe"));
                notice.reasonLabel.enableAutoSizing = true; notice.reasonLabel.fontSizeMin = 12f; notice.reasonLabel.fontSizeMax = 18f; notice.reasonLabel.color = new Color(1f, .82f, .55f); notice.reasonLabel.text = "";
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
                box.debugIncomingFrom = 0; box.debugIncomingKind = 0; box.debugIncomingSeq = 0; box.debugCrossInvert = 0;
                UdonSharpEditorUtility.CopyProxyToUdon(box); EditorUtility.SetDirty(box);

                // ---- さんぽタブ上段: [地図と移動] は左、「申し込み 1 件」は右 ----
                var walk = p.tabPages[KD_TreatPouch.TabWalk].transform;
                // DES-UI-006: 2 列 (Course/120) を流した後なら、右列の下 ([地図と移動] 540×72 / 保留中は右半分に「申し込み 1 件」266×72) に置き直す (上段へ戻さない)
                bool walkTwoColumn = KH_WalkTabLayoutEditor.IsApplied(p);
                if (!walkTwoColumn) { var openRt = (RectTransform)p.travelOpenButton.transform; openRt.anchoredPosition = new Vector2(-272f, KH_CourseTravelEditor.TopRowY); }
                var pending = KD_ShopSetupEditor.EnsureButton(walk, PendingName,
                    walkTwoColumn ? KH_WalkTabLayoutEditor.PendingPos : new Vector2(272f, KH_CourseTravelEditor.TopRowY),
                    walkTwoColumn ? KH_WalkTabLayoutEditor.PendingSize : new Vector2(530f, 40f),
                    new Color(.62f, .38f, .18f), font, Ja("course.travel.join.pending"), pouchBacking, "_TravelShowNotice", log);
                var pendingLabel = pending.transform.Find("Label").GetComponent<TextMeshProUGUI>(); pendingLabel.fontSize = 20f;
                if (walkTwoColumn) KH_WalkTabLayoutEditor.PlaceTravelButtons(p.travelOpenButton, pending);
                pending.SetActive(false);

                // ---- 副画面: 切替・受け付け・人の一覧 ----
                var page = p.travelMapPage.transform;
                var modeSpots = KD_ShopSetupEditor.EnsureButton(page, ModeSpotsName, new Vector2(190f, 164f), new Vector2(90f, 44f), KD_UiSpriteEditor.ActionNeutral, font, Ja("course.travel.join.spots"), pouchBacking, "_TravelModeSpots", log);
                var modePeople = KD_ShopSetupEditor.EnsureButton(page, ModePeopleName, new Vector2(290f, 164f), new Vector2(90f, 44f), KD_UiSpriteEditor.ActionNeutral, font, Ja("course.travel.join.people"), pouchBacking, "_TravelModePeople", log);
                var accept = KD_ShopSetupEditor.EnsureButton(page, AcceptName, new Vector2(-330f, 164f), new Vector2(400f, 44f), KD_UiSpriteEditor.ActionNeutral, font, Ja("course.travel.join.accept.on"), pouchBacking, "_TravelToggleAccept", log);
                var acceptLabel = accept.transform.Find("Label").GetComponent<TextMeshProUGUI>(); acceptLabel.fontSize = 20f;
                acceptLabel.enableAutoSizing = true; acceptLabel.fontSizeMin = 14f; acceptLabel.fontSizeMax = 20f; EditorUtility.SetDirty(acceptLabel);
                accept.SetActive(false);
                // [人] のフッター: [合流をたのむ] [来てもらう] (§F.2)。[地点] のときは Course/84 の Go を出し、これらは隠す
                var ask = KD_ShopSetupEditor.EnsureButton(page, AskName, AskPos, HalfSize, KD_UiSpriteEditor.ActionNeutral, font, Ja("course.travel.join.ask"), pouchBacking, "_TravelGo", log);
                var call = KD_ShopSetupEditor.EnsureButton(page, CallName, CallPos, HalfSize, KD_UiSpriteEditor.ActionNeutral, font, Ja("course.travel.call.ask"), pouchBacking, "_TravelCall", log);
                foreach (var hb in new[] { ask, call })
                {
                    var hl = hb.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                    hl.fontSize = 26f; hl.enableWordWrapping = false; hl.enableAutoSizing = true; hl.fontSizeMin = 14f; hl.fontSizeMax = 26f; EditorUtility.SetDirty(hl);
                    hb.SetActive(false);
                }
                // ---- 人の一覧 (DES-UI-006: 3 列 × 3 段のセル。旧スクロールの部品は撤去する) ----
                var people = KD_ShopSetupEditor.EnsureUiChild(page, PeopleName, PeoplePos, PeopleSize);
                var oldScroll = people.GetComponent<ScrollRect>();
                if (oldScroll != null) { UnityEngine.Object.DestroyImmediate(oldScroll); log.AppendLine("removed People ScrollRect"); }
                var oldViewport = people.transform.Find("Viewport");
                if (oldViewport != null) { UnityEngine.Object.DestroyImmediate(oldViewport.gameObject); log.AppendLine("removed People/Viewport (Content, Person0..15)"); }
                for (int i = people.transform.childCount - 1; i >= 0; i--)
                {
                    var ch = people.transform.GetChild(i);
                    if (ch.name.StartsWith("Person") && int.TryParse(ch.name.Substring(6), out int k) && k >= PeopleCells) { log.AppendLine("removed stale " + ch.name); UnityEngine.Object.DestroyImmediate(ch.gameObject); }
                }
                var rows = new Button[PeopleCells]; var rowImages = new Image[PeopleCells]; var rowLabels = new TextMeshProUGUI[PeopleCells]; var rowInfos = new TextMeshProUGUI[PeopleCells];
                var pagers = new TextMeshProUGUI[2];
                float innerW = CellSize.x - CellPadX;
                for (int c = 0; c < PeopleCells; c++)
                {
                    var cell = KD_ShopSetupEditor.EnsureButton(people.transform, "Person" + c, CellPos(c) - PeoplePos, CellSize,
                        KD_UiSpriteEditor.ActionNeutral, font, "VRChatUser", pouchBacking, "_JoinPick" + c, log);
                    rows[c] = cell.GetComponent<Button>(); rowImages[c] = cell.GetComponent<Image>();
                    // 1 行目 = 表示名 (EnsureButton の Label を上の帯へ置き直す。毎回 EnsureButton が中央・全面に戻すので、その後で書く)
                    var name = cell.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                    var nrt = name.rectTransform; nrt.anchoredPosition = new Vector2(0f, NameY); nrt.sizeDelta = new Vector2(innerW, NameHeight);
                    name.fontSize = NamePoints; name.enableAutoSizing = false; name.alignment = TextAlignmentOptions.Left; name.overflowMode = TextOverflowModes.Ellipsis; name.lineSpacing = 0f;
                    name.richText = false;   // 表示名に < があってもタグとして読ませない (TMP の既定は true。EnsureButton / ApplyIfPresent はここを触らない)
                    name.text = "VRChatUser"; EditorUtility.SetDirty(name);
                    rowLabels[c] = name;
                    // 2 行目 = 地点と距離か状態 (共通フォント。13 言語の字形は Course/27 が共通フォントへ焼く)
                    var info = MakeText(cell.transform, "Info", new Vector2(0f, InfoY), new Vector2(innerW, InfoHeight), InfoPoints, font, Ja("course.travel.join.near").Replace("{0}", Ja("course.walk.upper")) + "  " + Ja("course.travel.map.distance").Replace("{0}", "35"));
                    info.alignment = TextAlignmentOptions.Left; info.overflowMode = TextOverflowModes.Ellipsis;
                    info.enableAutoSizing = true; info.fontSizeMax = InfoPoints; info.fontSizeMin = InfoMinPoints; EditorUtility.SetDirty(info);
                    rowInfos[c] = info;
                    // ページ送りの記号 (セル 0 = ◀、セル 8 = ▶ と「p / q」)。10 人以上のときだけ実行時に出す
                    var oldPager = cell.transform.Find("Pager");
                    if (c == 0 || c == PeopleCells - 1)
                    {
                        var pager = MakeText(cell.transform, "Pager", Vector2.zero, new Vector2(innerW, CellSize.y - 8f), PagerPoints, font, c == 0 ? "◀" : "▶\n2 / 3");
                        pager.alignment = TextAlignmentOptions.Center; EditorUtility.SetDirty(pager);
                        pager.gameObject.SetActive(false);
                        pagers[c == 0 ? 0 : 1] = pager;
                    }
                    else if (oldPager != null) UnityEngine.Object.DestroyImmediate(oldPager.gameObject);
                    cell.SetActive(false);
                }
                var empty = MakeText(people.transform, "Empty", Vector2.zero, new Vector2(PeopleSize.x, 40f), 20f, font, Ja("course.travel.join.alone"));
                people.SetActive(false);
                // [人] のとき地図と一緒に隠す番号 (Course/84 が作る Mark1〜6)
                var marks = new GameObject[6];
                for (int s = 0; s < 6; s++)
                {
                    var mt = page.Find("Mark" + (s + 1));
                    if (mt == null) throw new InvalidOperationException("Map number Mark" + (s + 1) + " missing: run Course/84 first");
                    marks[s] = mt.gameObject;
                }
                // ◀ ▶ と数字の字形 (写真のページ送りと同じ定数・同じ焼き方。入っていれば何もしない)
                string pagerMissing = KD_ShopSetupEditor.MissingGlyphs(KH_PhotoGalleryEditor.PagerGlyphs);
                if (pagerMissing.Length > 0) { KD_ShopSetupEditor.AddGlyphs(pagerMissing, log); log.AppendLine("pager glyphs added: " + pagerMissing); }

                p.travelModeSpotsImage = modeSpots.GetComponent<Image>(); p.travelModePeopleImage = modePeople.GetComponent<Image>();
                p.travelModeSpotsLabel = modeSpots.transform.Find("Label").GetComponent<TextMeshProUGUI>(); p.travelModePeopleLabel = modePeople.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                p.travelModeSpotsLabel.fontSize = 20f; p.travelModePeopleLabel.fontSize = 20f;
                p.travelPeopleContent = null;   // DES-UI-006: スクロールの中身は撤去した
                p.travelPeopleRoot = people; p.travelPeopleButtons = rows; p.travelPeopleImages = rowImages; p.travelPeopleLabels = rowLabels; p.travelPeopleEmpty = empty;
                p.travelPeopleInfoLabels = rowInfos; p.travelPeoplePagerLabels = pagers; p.travelMapMarks = marks;
                KH_CourseTravelMapEditor.ApplyFooterFit(p.travelFooterLabel);   // 84 を流し直さなくても自動縮小 (18→14pt) を付ける
                p.travelAcceptButton = accept; p.travelAcceptLabel = acceptLabel;
                p.travelJoinPendingButton = pending; p.travelJoinPendingLabel = pendingLabel;
                p.travelNotice = notice;
                p.travelAskButton = ask; p.travelAskImage = ask.GetComponent<Image>(); p.travelAskLabel = ask.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                p.travelCallButton = call; p.travelCallImage = call.GetComponent<Image>(); p.travelCallLabel = call.transform.Find("Label").GetComponent<TextMeshProUGUI>();

                var descriptor = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
                if (descriptor != null) { int dropped = descriptor.NetworkIDCollection.RemoveAll(x => x.gameObject == null && (x.SerializedTypeNames == null || x.SerializedTypeNames.Count == 0)); if (dropped > 0) { report.droppedNetworkIds = dropped; EditorUtility.SetDirty(descriptor); } }
                KH_CourseTravelEditor.ApplyCatalog(p);
                KH_CourseTravelNameFontEditor.ApplyIfPresent(p, notice);   // 85 を流し直しても表示名の補助フォントを戻す (段階 4)
                UdonSharpEditorUtility.CopyProxyToUdon(p); EditorUtility.SetDirty(p);
                EditorSceneManager.MarkSceneDirty(scene);
                // ---- 保存前の検査 (DES-UI-006: 人の一覧のグリッドのプロキシ側・寸法・重なり・対応表・13 言語の収まり。通らなければ保存しない) ----
                // backing の変数表は保存時に揃うことがあるので (vrc-udonsharp §4)、backing の検査は Course/81 の Validate に残す (Acc/1・2 と同じ分け方)
                var errors = new List<string>();
                var fit = new KH_CourseTravelEditor.Report();
                ValidatePeopleGrid(p, pouchBacking, errors);
                MeasurePeopleGrid(p, errors, fit);
                report.info = fit.labelFit.Where(x => x.StartsWith("people ")).ToList();   // 縮んだ数の要約 (people grid / people footer summary)
                if (errors.Count > 0)
                {
                    report.findings = errors; report.status = "NOT_SAVED";
                    report.reason = "People grid check failed (" + errors.Count + "); scene NOT saved. Reopen the scene without saving to discard.";
                    File.WriteAllText(OutDir + "/travel-join-build.json", JsonUtility.ToJson(report, true));
                    throw new InvalidOperationException("[KH Travel Join] " + report.reason + " First: " + errors[0]);
                }
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
            Req(e, box.accepting && box.filmAccepting && box.debugJoinLoopback == 0 && box.debugAnswerCode == 0 && box.debugIncomingFrom == 0 && box.debugIncomingKind == 0 && box.debugIncomingSeq == 0 && box.debugCrossInvert == 0, "mailbox defaults: accepting ON, film accepting ON, every debug hook 0");
            CheckNetworkEntries(e);
            Req(e, box.manager == m && box.notice == notice, "mailbox wired (proxy)");
            if (bb != null)
            {
                Req(e, bb.publicVariables.TryGetVariableValue("notice", out object v1) && ReferenceEquals(v1, nb), "mailbox.notice wired (backing)");
                Req(e, bb.publicVariables.TryGetVariableValue("debugJoinLoopback", out object v2) && v2 is int d2 && d2 == 0, "mailbox debugJoinLoopback 0 (backing)");
                foreach (var hook in new[] { "debugAnswerCode", "debugIncomingFrom", "debugIncomingKind", "debugIncomingSeq", "debugCrossInvert" })
                    if (bb.publicVariables.TryGetVariableValue(hook, out object hv) && hv is int hi && hi != 0) e.Add("FAIL mailbox " + hook + " nonzero (backing)");
            }
            Req(e, nb != null && nb.SyncMethod == Networking.SyncType.None, "notice sync=None");
            var board = notice.board;
            Req(e, board != null && board.transform.parent == notice.transform && !board.activeSelf, "notice board child, inactive at start");
            if (board != null)
            {
                Req(e, board.layer == 0 && board.GetComponent<Canvas>() != null && board.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace
                    && board.GetComponent<VRCUiShape>() != null && board.GetComponent<BoxCollider>() != null && board.GetComponent<GraphicRaycaster>() != null, "notice board: WorldSpace Canvas + VRCUiShape + BoxCollider + Raycaster on Default");
                for (int i = 0; i < 6; i++) { var b = board.transform.Find("Button" + i); if (b == null) e.Add("FAIL notice button " + i); else CheckEvent(e, b.gameObject, nb, NoticeEvents[i]); }
                // 呼ぶの別の実体 (§F.2): 種類ごとに 2 つ。呼ぶの 2 つは最初は隠す
                var b4 = board.transform.Find("Button4"); var b5 = board.transform.Find("Button5");
                Req(e, notice.joinButtons != null && notice.joinButtons.Length == 2 && notice.joinButtons[0] == board.transform.Find("Button0")?.gameObject && notice.joinButtons[1] == board.transform.Find("Button1")?.gameObject, "notice joinButtons = Button0/1");
                Req(e, notice.callButtons != null && notice.callButtons.Length == 2 && b4 != null && b5 != null && notice.callButtons[0] == b4.gameObject && notice.callButtons[1] == b5.gameObject, "notice callButtons = Button4/5");
                Req(e, b4 != null && b5 != null && !b4.gameObject.activeSelf && !b5.gameObject.activeSelf, "call buttons hidden at start");
                Req(e, notice.goLabel != null && b4 != null && notice.goLabel == b4.Find("Label").GetComponent<TextMeshProUGUI>() && notice.notNowLabel != null && b5 != null && notice.notNowLabel == b5.Find("Label").GetComponent<TextMeshProUGUI>(), "notice go / not-now labels wired");
                Req(e, notice.reasonLabel != null && notice.reasonLabel.transform.parent == board.transform && !notice.reasonLabel.raycastTarget, "notice reason label (no raycast)");
                if (nb != null)
                {
                    Req(e, nb.publicVariables.TryGetVariableValue("callButtons", out object cbv) && cbv is Array cba && cba.Length == 2, "notice callButtons (backing)");
                    Req(e, nb.publicVariables.TryGetVariableValue("reasonLabel", out object rlv) && rlv != null, "notice reasonLabel (backing)");
                }
            }
            // W3 レビュー 10: 配置の値・Collider・通知音 (一覧のセルは ValidatePeopleGrid)
            Req(e, Mathf.Approximately(notice.distance, 1.1f) && Mathf.Approximately(notice.lowerDegrees, 15f) && Mathf.Approximately(notice.replaceMeters, 3f), "notice placement 1.1 m / 15 deg / 3 m");
            Req(e, notice.chime != null && notice.chime.clip != null && !notice.chime.playOnAwake && !notice.chime.loop, "notice chime wired (one shot)");
            if (board != null)
            {
                var bcol = board.GetComponent<BoxCollider>(); var brect = (RectTransform)board.transform;
                Req(e, bcol != null && Mathf.Approximately(bcol.size.x, brect.sizeDelta.x) && Mathf.Approximately(bcol.size.y, brect.sizeDelta.y) && Mathf.Approximately(brect.localScale.x, 0.001f), "notice collider matches the board (scale 0.001)");
            }
            Req(e, notice.language == p.language && notice.manager == m && notice.titleLabel != null && notice.placeLabel != null && notice.countLabel != null && notice.yesLabel != null && notice.noLabel != null && notice.pauseLabel != null && notice.blockLabel != null, "notice wired");
            // 手元メニュー
            var pb = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            var walk = p.tabPages[KD_TreatPouch.TabWalk].transform;
            Req(e, p.travelJoinPendingButton != null && p.travelJoinPendingButton.transform.parent == walk && !p.travelJoinPendingButton.activeSelf, "pending button under Page_Walk, hidden at start");
            if (p.travelJoinPendingButton != null) CheckEvent(e, p.travelJoinPendingButton, pb, "_TravelShowNotice");
            var page = p.travelMapPage != null ? p.travelMapPage.transform : null;
            Req(e, page != null && p.travelPeopleRoot != null && p.travelPeopleRoot.transform.parent == page && !p.travelPeopleRoot.activeSelf, "people list on the map page, hidden at start");
            ValidatePeopleGrid(p, pb, e);   // DES-UI-006: 9 セル・ページ送り・地図を隠す配線・フッターの 2 つの配置・重なり・対応表
            if (page != null)
            {
                foreach (var (name, evt) in new[] { (ModeSpotsName, "_TravelModeSpots"), (ModePeopleName, "_TravelModePeople"), (AcceptName, "_TravelToggleAccept") })
                { var t = page.Find(name); if (t == null) e.Add("FAIL " + name); else CheckEvent(e, t.gameObject, pb, evt); }
            }
            Req(e, p.travelNotice == notice && p.travelAcceptLabel != null && p.travelPeopleEmpty != null && p.travelModeSpotsLabel != null && p.travelModePeopleLabel != null, "pouch join fields wired");
            // [人] のフッター (§F.2): 2 つの実体・イベント・大きさと並び・最初は隠す
            if (page != null)
            {
                var askT = page.Find(AskName); var callT = page.Find(CallName);
                if (askT == null || callT == null) e.Add("FAIL join footer buttons missing (run Course/85)");
                else
                {
                    CheckEvent(e, askT.gameObject, pb, "_TravelGo"); CheckEvent(e, callT.gameObject, pb, "_TravelCall");
                    var ar = (RectTransform)askT; var cr = (RectTransform)callT;
                    Req(e, ar.sizeDelta == HalfSize && cr.sizeDelta == HalfSize && ar.anchoredPosition == AskPos && cr.anchoredPosition == CallPos, "ask / call 245x90 side by side (gap 10)");
                    Req(e, !askT.gameObject.activeSelf && !callT.gameObject.activeSelf, "ask / call hidden at start");
                    Req(e, p.travelAskButton == askT.gameObject && p.travelCallButton == callT.gameObject && p.travelAskImage == askT.GetComponent<Image>() && p.travelCallImage == callT.GetComponent<Image>()
                        && p.travelAskLabel == askT.Find("Label").GetComponent<TextMeshProUGUI>() && p.travelCallLabel == callT.Find("Label").GetComponent<TextMeshProUGUI>(), "pouch ask / call fields wired");
                    foreach (var t in new[] { askT, callT })
                        foreach (var g in t.GetComponentsInChildren<Graphic>(true)) if (g.transform != t && g.raycastTarget) e.Add("FAIL decoration blocks raycast: " + t.name + "/" + g.name);
                    // 押せる面の Canvas: VRCUiShape を持つ祖先が WorldSpace Canvas + GraphicRaycaster (vrc-world-ui §2)。
                    // 手元メニューの UIShape は Collider を持たない (VRChat が実行時に整える。層と Collider の規則は KD_HandMenuCollisionEditor / Menu/24)
                    var shape = page.GetComponentInParent<VRCUiShape>(true);
                    var canvas = shape != null ? shape.GetComponent<Canvas>() : null;
                    Req(e, canvas != null && canvas.renderMode == RenderMode.WorldSpace && shape.GetComponent<GraphicRaycaster>() != null, "map page canvas: WorldSpace + VRCUiShape + GraphicRaycaster");
                }
            }
            foreach (var field in new[] { "travelAskButton", "travelCallButton", "travelAskLabel", "travelCallLabel" })
                if (pb == null || !pb.publicVariables.TryGetVariableValue(field, out object fv) || fv == null) e.Add("FAIL backing " + field);
            foreach (var field in new[] { "travelNotice", "travelPeopleRoot", "travelAcceptButton", "travelJoinPendingButton" })
                if (pb == null || !pb.publicVariables.TryGetVariableValue(field, out object v) || v == null) e.Add("FAIL backing " + field);
            foreach (var (field, len) in new[] { ("travelPeopleButtons", PeopleCells), ("travelPeopleImages", PeopleCells), ("travelPeopleLabels", PeopleCells), ("travelPeopleInfoLabels", PeopleCells), ("travelPeoplePagerLabels", 2), ("travelMapMarks", 6) })
                if (pb == null || !pb.publicVariables.TryGetVariableValue(field, out object arr) || !(arr is Array a) || a.Length != len || a.Cast<object>().Any(x => x == null)) e.Add("FAIL backing " + field + " (length " + len + ", no null)");
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
            MeasureCall(p, notice, e, report);
            MeasurePeopleGrid(p, e, report);   // DES-UI-006: セルの全状態・ページ送り・[人] のフッター (13 言語)
        }

        /// <summary>呼ぶの文言と設定の文言の収まり (13 言語全部。§F.2)。</summary>
        static void MeasureCall(KD_TreatPouch p, KH_TravelNotice notice, List<string> e, KH_CourseTravelEditor.Report report)
        {
            var go = new GameObject("TravelCallMeasure", typeof(RectTransform)); go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var sample = go.AddComponent<TextMeshProUGUI>(); var lang = p.language;
                foreach (string code in lang.languageCodes)
                {
                    string T(string key) { return lang._TextForLanguage(key, code); }
                    Fit(sample, p.travelAcceptLabel, T("course.travel.join.accept.on"), code + " accept on", e, report);
                    Fit(sample, p.travelAcceptLabel, T("course.travel.join.accept.off"), code + " accept off", e, report);
                    Fit(sample, p.travelAskLabel, T("course.travel.join.ask"), code + " ask", e, report);
                    Fit(sample, p.travelAskLabel, T("course.travel.join.cancel").Replace("{0}", "20"), code + " ask cancel", e, report);
                    Fit(sample, p.travelCallLabel, T("course.travel.call.ask"), code + " call", e, report);
                    Fit(sample, p.travelCallLabel, T("course.travel.join.cancel").Replace("{0}", "20"), code + " call cancel", e, report);
                    Fit(sample, p.travelJoinPendingLabel, T("course.travel.call.pending"), code + " call pending", e, report);
                    Fit(sample, notice.titleLabel, T("course.travel.call.notice.title").Replace("{0}", "VRChatUser12"), code + " call notice title", e, report);
                    Fit(sample, notice.goLabel, T("course.travel.call.notice.go"), code + " notice go", e, report);
                    Fit(sample, notice.notNowLabel, T("course.travel.call.notice.notnow"), code + " notice not-now", e, report);
                    Fit(sample, notice.placeLabel, T("course.travel.join.near").Replace("{0}", T("course.travel.join.spot.bridgeDown")) + "  " + T("course.travel.map.dogs").Replace("{0}", "6"), code + " call notice place", e, report);
                    foreach (var key in new[] { "course.travel.join.nosafe", "course.travel.busy", "course.travel.ball", "course.travel.unavailable", "course.travel.map.moving" })
                        Fit(sample, notice.reasonLabel, T(key), code + " reason " + key, e, report);
                    Fit(sample, notice.reasonLabel, T("course.travel.cooling").Replace("{0}", "10"), code + " reason cooling", e, report);
                    foreach (var key in new[] { "coming", "arrived", "notcoming", "unknown", "canceledby", "crossyield", "answerfirst" })
                        Fit(sample, p.travelStatusLabel, T("course.travel.call." + key).Replace("{0}", "VRChatUser12"), code + " status call." + key, e, report);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // ============================================================
        // DES-UI-006: 人の一覧のグリッド (3 列 × 3 段 + ページ送り)
        // ============================================================

        /// <summary>Course/85 が撤去する旧スクロールの部品 (People の ScrollRect、Viewport 以下、Person9 以降) を、
        /// 手元メニュー (KD_TreatPouch のプロキシと backing。配列ごと張り直す) と撤去する部品自身の外から参照している箇所。Transform の親子は数えない。</summary>
        static List<string> OldScrollRefs(Transform people, KD_TreatPouch p, VRC.Udon.UdonBehaviour pouchBacking)
        {
            var doomed = new HashSet<UnityEngine.Object>();
            void AddTree(Transform t)
            {
                foreach (var x in t.GetComponentsInChildren<Transform>(true)) { doomed.Add(x.gameObject); foreach (var c in x.GetComponents<Component>()) if (c != null) doomed.Add(c); }
            }
            var scroll = people.GetComponent<ScrollRect>(); if (scroll != null) doomed.Add(scroll);
            var vp = people.Find("Viewport"); if (vp != null) AddTree(vp);
            for (int i = 0; i < people.childCount; i++)
            {
                var ch = people.GetChild(i);
                if (ch.name.StartsWith("Person") && int.TryParse(ch.name.Substring(6), out int k) && k >= PeopleCells) AddTree(ch);
            }
            var found = new List<string>();
            if (doomed.Count == 0) return found;
            foreach (var root in people.gameObject.scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || doomed.Contains(c) || c == p || c == pouchBacking) continue;
                    var it = new SerializedObject(c).GetIterator();
                    while (it.Next(true))
                        if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null && doomed.Contains(it.objectReferenceValue))
                            found.Add(c.GetType().Name + "(" + c.name + ")." + it.propertyPath + " -> " + it.objectReferenceValue.name);
                }
            return found;
        }

        /// <summary>Course/81: 9 セルの位置・大きさ・イベント・配列、ページ送りのラベル、地図を隠す配線、フッターの保存値 ([地点] の配置)、重なり、対応表。</summary>
        static void ValidatePeopleGrid(KD_TreatPouch p, VRC.Udon.UdonBehaviour pb, List<string> e)
        {
            var page = p.travelMapPage != null ? p.travelMapPage.transform : null;
            var root = p.travelPeopleRoot;
            if (page == null || root == null) { e.Add("FAIL people grid: map page / people root missing (run Course/85)"); return; }
            var rrt = (RectTransform)root.transform;
            Req(e, root.GetComponent<ScrollRect>() == null && root.transform.Find("Viewport") == null, "people grid: the old ScrollRect / Viewport are gone (DES-UI-006; run Course/85)");
            Req(e, Near(rrt.anchoredPosition, PeoplePos) && Near(rrt.sizeDelta, PeopleSize), "people grid container on the band " + PeoplePos + " " + PeopleSize + " (is " + rrt.anchoredPosition + " " + rrt.sizeDelta + ")");
            for (int i = 0; i < root.transform.childCount; i++)
            {
                var ch = root.transform.GetChild(i);
                if (ch.name.StartsWith("Person") && int.TryParse(ch.name.Substring(6), out int k) && k >= PeopleCells) e.Add("FAIL stale people cell " + ch.name + " (run Course/85)");
            }
            Req(e, p.travelPeopleContent == null, "travelPeopleContent cleared (the scroll content is gone)");
            Req(e, KD_TreatPouch.PeopleCells == 9 && KD_TreatPouch.PeoplePerPage == 7 && KD_TreatPouch.PeoplePagedFrom == 10 && KD_TreatPouch.JoinMax == 80
                && KD_TreatPouch.JoinCellEmpty == -1 && KD_TreatPouch.JoinCellPrev == -2 && KD_TreatPouch.JoinCellNext == -3, "paging constants: 9 cells, 7 per page, paged from 10, 80 max (DES-UI-006 §5.3)");
            bool arrays = p.travelPeopleButtons != null && p.travelPeopleButtons.Length == PeopleCells && p.travelPeopleImages != null && p.travelPeopleImages.Length == PeopleCells
                && p.travelPeopleLabels != null && p.travelPeopleLabels.Length == PeopleCells && p.travelPeopleInfoLabels != null && p.travelPeopleInfoLabels.Length == PeopleCells
                && p.travelPeoplePagerLabels != null && p.travelPeoplePagerLabels.Length == 2;
            Req(e, arrays, PeopleCells + " people cells (buttons / images / name labels / info labels) + 2 pager labels");
            var shared = p.sectionTitleLabel != null ? p.sectionTitleLabel.font : null;
            var cellRects = new List<Rect>();
            if (arrays)
                for (int c = 0; c < PeopleCells; c++)
                {
                    var b = p.travelPeopleButtons[c];
                    if (b == null) { e.Add("FAIL people cell " + c + " missing"); continue; }
                    var nameT = b.transform.Find("Label"); var infoT = b.transform.Find("Info");
                    Req(e, b.transform.parent == root.transform && b.name == "Person" + c && p.travelPeopleImages[c] == b.GetComponent<Image>()
                        && nameT != null && p.travelPeopleLabels[c] == nameT.GetComponent<TextMeshProUGUI>() && infoT != null && p.travelPeopleInfoLabels[c] == infoT.GetComponent<TextMeshProUGUI>(),
                        "people cell " + c + " matches the arrays (Label = name, Info = 2nd line)");
                    CheckEvent(e, b.gameObject, pb, "_JoinPick" + c);
                    var rt = (RectTransform)b.transform;
                    var at = rrt.anchoredPosition + rt.anchoredPosition;
                    Req(e, Near(at, CellPos(c)) && Near(rt.sizeDelta, CellSize), "people cell " + c + " at " + CellPos(c) + " size " + CellSize + " (is " + at + " " + rt.sizeDelta + ")");
                    var r = new Rect(at - rt.sizeDelta / 2f, rt.sizeDelta); cellRects.Add(r);
                    Req(e, r.xMin >= BandLeft - .5f && r.xMax <= BandRight + .5f && r.yMax <= BandTop + .5f && r.yMin >= BandBottom - .5f, "people cell " + c + " inside the band x " + BandLeft + ".." + BandRight + " / y " + BandTop + ".." + BandBottom);
                    Req(e, !b.gameObject.activeSelf, "people cell " + c + " hidden at start");
                    foreach (var g in b.GetComponentsInChildren<Graphic>(true)) if (g.transform != b.transform && g.raycastTarget) e.Add("FAIL decoration blocks raycast: " + b.name + "/" + g.name);
                    var info = p.travelPeopleInfoLabels[c];
                    Req(e, info != null && shared != null && info.font == shared && info.overflowMode == TextOverflowModes.Ellipsis && !info.enableWordWrapping, "people cell " + c + " info: shared font (13-language glyphs), one line, ellipsis");
                    var nm = p.travelPeopleLabels[c];
                    Req(e, nm != null && nm.overflowMode == TextOverflowModes.Ellipsis && !nm.enableWordWrapping && !nm.enableAutoSizing && !nm.richText, "people cell " + c + " name: one line, ellipsis, fixed size, rich text off");
                }
            for (int k = 0; arrays && k < 2; k++)
            {
                var t = p.travelPeoplePagerLabels[k]; int cell = k == 0 ? 0 : PeopleCells - 1;
                Req(e, t != null && p.travelPeopleButtons[cell] != null && t.transform.parent == p.travelPeopleButtons[cell].transform && t.name == "Pager" && t.font == shared && !t.raycastTarget && !t.gameObject.activeSelf,
                    "pager label " + k + " under Person" + cell + " (shared font, no raycast, hidden at start)");
            }
            // MissingGlyphs / AddGlyphs は KD_ShopSetupEditor の FontAssetPath (private) を見る。セルの共通フォントがそれと同じであること
            Req(e, shared != null && AssetDatabase.GetAssetPath(shared) == SharedFontPath, "people cells' shared font is " + SharedFontPath + " (the font MissingGlyphs / AddGlyphs bake; is " + (shared != null ? AssetDatabase.GetAssetPath(shared) : "none") + ")");
            string glyphs = KD_ShopSetupEditor.MissingGlyphs(KH_PhotoGalleryEditor.PagerGlyphs);
            Req(e, glyphs.Length == 0, "shared font has the pager glyphs " + KH_PhotoGalleryEditor.PagerGlyphs + (glyphs.Length > 0 ? " (missing " + glyphs + "; run Course/85)" : ""));
            // [人] のとき地図と一緒に隠す番号
            Req(e, p.travelMapMarks != null && p.travelMapMarks.Length == 6 && Enumerable.Range(0, 6).All(s => p.travelMapMarks[s] != null && page.Find("Mark" + (s + 1)) != null && p.travelMapMarks[s] == page.Find("Mark" + (s + 1)).gameObject),
                "travelMapMarks = Mark1..6 (hidden with the map in [人])");
            // フッターの文: 保存値は [地点] の配置 (実行時に [人] のときだけ 2 ボタンの左へ移す)
            var footer = p.travelFooterLabel;
            if (footer == null) e.Add("FAIL footer label missing");
            else
            {
                var frt = footer.rectTransform;
                Req(e, Near(frt.anchoredPosition, KH_CourseTravelMapEditor.FooterPos) && Near(frt.sizeDelta, KH_CourseTravelMapEditor.FooterSize) && footer.enableAutoSizing
                    && Mathf.Approximately(footer.fontSizeMax, KH_CourseTravelMapEditor.FooterPoints) && Mathf.Approximately(footer.fontSizeMin, KH_CourseTravelMapEditor.FooterMinPoints),
                    "footer saved at the [地点] layout " + KH_CourseTravelMapEditor.FooterPos + " " + KH_CourseTravelMapEditor.FooterSize + " autosize " + KH_CourseTravelMapEditor.FooterMinPoints + "-" + KH_CourseTravelMapEditor.FooterPoints + "pt (is " + frt.anchoredPosition + " " + frt.sizeDelta + " autosize=" + footer.enableAutoSizing + " " + footer.fontSizeMin + "-" + footer.fontSizeMax + "pt)");
            }
            var footerPeople = new Rect(KD_TreatPouch.FooterPeopleX - KD_TreatPouch.FooterPeopleW / 2f, KD_TreatPouch.FooterPeopleY - KD_TreatPouch.FooterPeopleH / 2f, KD_TreatPouch.FooterPeopleW, KD_TreatPouch.FooterPeopleH);
            Req(e, footerPeople.xMin >= -KD_TreatSetupEditor.PanelWidth / 2f && footerPeople.yMin >= -KD_TreatSetupEditor.PanelHeight / 2f, "[人] footer inside the board");
            // 重なり: セル・[人] のフッター ↔ 上段の行 (合流 / 撮影の受付・[地点] [人]・[もどる])・2 ボタン・状態の 1 行。端が接するのは可
            var others = new List<(string name, Rect rect)>();
            void Add(string n, Transform t, bool required)
            {
                if (t is RectTransform r) others.Add((n, new Rect(r.anchoredPosition - r.sizeDelta / 2f, r.sizeDelta)));
                else if (required) e.Add("FAIL overlap check: " + n + " missing");
            }
            Add(AcceptName, page.Find(AcceptName), true); Add("FilmAcceptToggle", p.filmAcceptButton != null ? p.filmAcceptButton.transform : null, false);
            Add(ModeSpotsName, page.Find(ModeSpotsName), true); Add(ModePeopleName, page.Find(ModePeopleName), true); Add("Back", page.Find("Back"), true);
            Add(AskName, page.Find(AskName), true); Add(CallName, page.Find(CallName), true); Add("Status", p.travelStatusLabel != null ? p.travelStatusLabel.transform : null, true);
            for (int c = 0; c < cellRects.Count; c++)
            {
                foreach (var (n, r) in others) if (Overlap(cellRects[c], r)) e.Add("FAIL people cell " + c + " overlaps " + n);
                if (Overlap(cellRects[c], footerPeople)) e.Add("FAIL people cell " + c + " overlaps the [人] footer");
            }
            foreach (var (n, r) in others) if (Overlap(footerPeople, r)) e.Add("FAIL the [人] footer overlaps " + n);
            CheckPeoplePaging(p, e);
        }

        static bool Near(Vector2 a, Vector2 b) { return Vector2.Distance(a, b) < .5f; }
        /// <summary>重なり (0.5 未満のはみ出しと、端が接するのは重なりにしない)。</summary>
        static bool Overlap(Rect a, Rect b) { const float t = .5f; return a.xMin + t < b.xMax && b.xMin + t < a.xMax && a.yMin + t < b.yMax && b.yMin + t < a.yMax; }

        /// <summary>対応表 (KD_TreatPouch の private メソッド JoinCellIndex / JoinPageCount / JoinClampedPage) を、Editor 側に独立に書いた §5.3 の規則
        /// (<see cref="SpecCellIndex"/>) と n = 1〜80 の全ページ・全セルで突き合わせる。全員に重複・欠落なく届くこと、空のページが無いこと、
        /// セル 0 / 8 がページ送りになるのは 10 人以上だけ、退出で最後のページが空になれば 1 つ前へ戻ることも見る。
        /// プロキシは普通の C# のクラスなので Editor では反射で呼べる (C# としての計算の確認。Udon VM での実行は Play / 実機)。</summary>
        static void CheckPeoplePaging(KD_TreatPouch p, List<string> e)
        {
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
            var cellIndex = typeof(KD_TreatPouch).GetMethod("JoinCellIndex", F);
            var pageCount = typeof(KD_TreatPouch).GetMethod("JoinPageCount", F);
            var clamp = typeof(KD_TreatPouch).GetMethod("JoinClampedPage", F);
            if (cellIndex == null || pageCount == null || clamp == null) { e.Add("FAIL paging methods JoinCellIndex / JoinPageCount / JoinClampedPage not found on KD_TreatPouch"); return; }
            int Idx(int n, int pg, int c) { return (int)cellIndex.Invoke(p, new object[] { n, pg, c }); }
            int Pages(int n) { return (int)pageCount.Invoke(p, new object[] { n }); }
            int Clamp(int pg, int n) { return (int)clamp.Invoke(p, new object[] { pg, n }); }
            var bad = new List<string>();
            for (int n = 1; n <= KD_TreatPouch.JoinMax; n++)
            {
                int pages = Pages(n);
                if (pages != SpecPageCount(n)) { bad.Add("n=" + n + " pages " + pages + " != " + SpecPageCount(n)); continue; }
                var seen = new int[n];
                for (int pg = 0; pg < pages; pg++)
                {
                    int onPage = 0;
                    for (int c = 0; c < PeopleCells; c++)
                    {
                        int got = Idx(n, pg, c), want = SpecCellIndex(n, pg, c);
                        if (got != want) { bad.Add("n=" + n + " page " + pg + " cell " + c + " -> " + got + " (rule " + want + ")"); continue; }
                        if (got >= 0) { if (got >= n) bad.Add("n=" + n + " page " + pg + " cell " + c + " index " + got + " out of range"); else { seen[got]++; onPage++; } }
                        bool pager = got == KD_TreatPouch.JoinCellPrev || got == KD_TreatPouch.JoinCellNext;
                        if (pager != (n >= 10 && (c == 0 || c == 8))) bad.Add("n=" + n + " page " + pg + " cell " + c + " pager=" + pager);
                    }
                    if (onPage == 0) bad.Add("n=" + n + " page " + pg + " has nobody");
                }
                for (int i = 0; i < n; i++) if (seen[i] != 1) bad.Add("n=" + n + " index " + i + " shown " + seen[i] + " times");
                if (Clamp(pages, n) != pages - 1 || Clamp(-1, n) != 0 || Clamp(pages - 1, n) != pages - 1) bad.Add("n=" + n + " page clamp");
                if (n >= 2 && Clamp(pages - 1, n - 1) != SpecPageCount(n - 1) - 1) bad.Add("n=" + n + " -> " + (n - 1) + " on the last page: clamped to " + Clamp(pages - 1, n - 1) + " (rule " + (SpecPageCount(n - 1) - 1) + ")");
                if (Idx(n, 0, -1) != KD_TreatPouch.JoinCellEmpty || Idx(n, 0, PeopleCells) != KD_TreatPouch.JoinCellEmpty) bad.Add("n=" + n + " out-of-range cell not empty");
            }
            if (bad.Count > 0) e.Add("FAIL people paging (" + bad.Count + " mismatches): " + string.Join("; ", bad.Take(8)));
        }

        /// <summary>§5.3 の規則の Editor 側の写し (U# の定数を使わずに書く。突き合わせで U# 側の変更を検出する)。
        /// 自分以外が 9 人以下: 1 ページ、セル c = 添字 c。10 人以上: 1 ページ 7 人、セル 0 = 前 (-2)、セル 1〜7 = 添字 page × 7 + (c − 1)、セル 8 = 次 (-3)。空 = -1。</summary>
        public static int SpecPageCount(int n) { return n <= 9 ? 1 : (n + 6) / 7; }
        public static int SpecCellIndex(int n, int page, int cell)
        {
            if (cell < 0 || cell > 8) return -1;
            if (n <= 9) return cell < n ? cell : -1;
            if (cell == 0) return -2;
            if (cell == 8) return -3;
            int idx = page * 7 + cell - 1;
            return idx < n ? idx : -1;
        }

        /// <summary>セルの全状態とページ送り、[人] のフッター (435×90・18pt、収まらなければ自動縮小で 14pt まで。保存値は [地点] の配置なので、同じ設定の測定用の枠で測る) の収まり (13 言語)。</summary>
        static void MeasurePeopleGrid(KD_TreatPouch p, List<string> e, KH_CourseTravelEditor.Report report)
        {
            if (p.travelPeopleLabels == null || p.travelPeopleLabels.Length == 0 || p.travelPeopleLabels[0] == null || p.travelPeopleInfoLabels == null || p.travelPeopleInfoLabels.Length == 0 || p.travelPeopleInfoLabels[0] == null || p.travelFooterLabel == null)
            { e.Add("FAIL people grid labels missing for the fit check (run Course/85)"); return; }
            var go = new GameObject("TravelPeopleMeasure", typeof(RectTransform)); go.hideFlags = HideFlags.HideAndDontSave;
            var fgo = new GameObject("TravelPeopleFooterMeasure", typeof(RectTransform)); fgo.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var sample = go.AddComponent<TextMeshProUGUI>(); var lang = p.language;
                var src = p.travelFooterLabel; var footer = fgo.AddComponent<TextMeshProUGUI>();
                footer.font = src.font; footer.fontSize = KD_TreatPouch.FooterPeoplePoints; footer.enableAutoSizing = true; footer.fontSizeMax = KD_TreatPouch.FooterPeoplePoints; footer.fontSizeMin = KH_CourseTravelMapEditor.FooterMinPoints; footer.enableWordWrapping = src.enableWordWrapping; footer.margin = src.margin; footer.richText = src.richText;
                footer.rectTransform.sizeDelta = new Vector2(KD_TreatPouch.FooterPeopleW, KD_TreatPouch.FooterPeopleH);
                var name = p.travelPeopleLabels[0]; var info = p.travelPeopleInfoLabels[0];
                // 表示名は VRChat の上限に近い 15 字 (英数) で 1 行が入ること。長い名前 (日本語 15 字など) は … で切る設計なので、ここでは測らない
                Fit(sample, name, "VRChatUser12345", "person name 15", e, report);
                foreach (var pager in p.travelPeoplePagerLabels ?? new TextMeshProUGUI[0])
                {
                    if (pager == null) continue;
                    Fit(sample, pager, "◀", "pager prev", e, report);
                    Fit(sample, pager, "▶\n" + LongestPage, "pager next " + LongestPage, e, report);
                }
                string[] spotKeys = { "KD_TreatPouch.WordWalkGate", "KD_TreatPouch.WordWalkPlaza", "course.walk.upper", "course.travel.join.spot.bridgeUp", "course.walk.river", "course.travel.join.spot.bridgeDown" };
                string[] walkKeys = { "KD_TreatPouch.WordWalkPlaza", "KD_TreatPouch.WordWalkGate", "course.walk.forward", "course.walk.reverse", "course.walk.upper", "course.walk.river" };
                string[] reasons = { "reason.near", "reason.behind", "reason.bridge", "reason.water", "reason.offtrail" };
                foreach (string code in lang.languageCodes)
                {
                    string T(string key) { return lang._TextForLanguage(key, code); }
                    // セルの 2 行目: 通常 (6 地点の呼び名 × 3 桁の距離) / 受付 OFF / 準備中 / 返事待ち / 向かっている (状態のときは状態の文だけ)
                    foreach (var sk in spotKeys)
                        Fit(sample, info, T("course.travel.join.near").Replace("{0}", T(sk)) + "  " + T("course.travel.map.distance").Replace("{0}", "999"), code + " cell info normal " + sk, e, report);
                    Fit(sample, info, T("course.travel.join.notAccepting"), code + " cell info not accepting", e, report);
                    Fit(sample, info, T("course.travel.join.notReady"), code + " cell info not ready", e, report);
                    Fit(sample, info, T("course.travel.join.waiting").Replace("{0}", "20"), code + " cell info waiting", e, report);
                    Fit(sample, info, T("course.travel.call.onway"), code + " cell info on the way", e, report);
                    // [人] のフッター: 1 行目 = 合流先か着地点の理由、2 行目 = 犬の数、3 行目 = 散歩先 (折り返さないので、行ごとの幅と 3 行の高さ)
                    Fit(sample, footer, T("course.travel.join.target").Replace("{0}", "VRChatUser12345"), code + " people footer target", e, report);
                    foreach (var r in reasons) Fit(sample, footer, T("course.travel.join." + r), code + " people footer " + r, e, report);
                    Fit(sample, footer, T("course.travel.map.dogs").Replace("{0}", "6"), code + " people footer dogs", e, report);
                    Fit(sample, footer, T("course.travel.map.alone"), code + " people footer alone", e, report);
                    foreach (var wk in walkKeys) Fit(sample, footer, T("course.travel.map.walkdest").Replace("{0}", T(wk)), code + " people footer walkdest " + wk, e, report);
                    Fit(sample, footer, T("course.travel.join.pick"), code + " people footer pick", e, report);
                    string longestWalk = walkKeys.Select(T).OrderByDescending(x => x.Length).First();
                    string longestFirst = reasons.Select(r => T("course.travel.join." + r)).OrderByDescending(x => x.Length).First();
                    Fit(sample, footer, longestFirst + "\n" + T("course.travel.map.dogs").Replace("{0}", "6") + "\n" + T("course.travel.map.walkdest").Replace("{0}", longestWalk), code + " people footer 3 lines", e, report);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(fgo); }
            // 2 行目は 16pt から 12pt まで縮めて収める (設計の 16pt を下回る)。縮んだ文の数を 1 行にまとめて残す (fit=1。fit=2 は上で Label overflow)
            var shrunkFooter = report.labelFit.Where(x => x.Contains(" people footer ") && x.EndsWith("fit=1")).ToList();
            report.labelFit.Add("people footer summary: " + shrunkFooter.Count + " [人] footer texts shrink below " + KD_TreatPouch.FooterPeoplePoints + "pt (autosize to " + KH_CourseTravelMapEditor.FooterMinPoints + "pt)"
                + (shrunkFooter.Count > 0 ? ": " + string.Join(" | ", shrunkFooter.Take(12).Select(x => x.Substring(0, Math.Min(60, x.Length)))) : ""));
            var shrunk = report.labelFit.Where(x => x.Contains(" cell info ") && x.EndsWith("fit=1")).ToList();
            report.labelFit.Add("people grid summary: " + shrunk.Count + " cell-info texts shrink below " + InfoPoints + "pt (autosize to " + InfoMinPoints + "pt)"
                + (shrunk.Count > 0 ? ": " + string.Join(" | ", shrunk.Take(12).Select(x => x.Substring(0, Math.Min(60, x.Length)))) : ""));
        }

        /// <summary>受付箱のネットワーク入口は 7 つだけで、名前と引数の型が決めたとおり (§F.3。属性の有無だけでは足りない)。
        /// 動画カメラ §13.9: partial のファイル (KH_TravelMailbox*.cs) を全部読む。どの口も本文の最初で IsOwner を見ること、
        /// 送り主を NetworkCalling.CallingPlayer から取ること、引数なしの public メソッド (旧形式のネットワークイベント) が `_` 始まりであることも見る。</summary>
        static void CheckNetworkEntries(List<string> e)
        {
            var found = new List<string>();
            var files = Directory.GetFiles(Path.GetDirectoryName(MailboxProgram + ".cs"), "KH_TravelMailbox*.cs").Where(f => !f.EndsWith(".meta")).ToArray();
            if (files.Length < 2) e.Add("FAIL mailbox partial files: " + string.Join(", ", files.Select(Path.GetFileName)) + " (expected KH_TravelMailbox.cs + .Film.cs)");
            foreach (var file in files)
            {
                var lines = File.ReadAllLines(file);
                string fname = Path.GetFileName(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var pub = System.Text.RegularExpressions.Regex.Match(lines[i], @"^\s*public\s+(?:override\s+)?void\s+(\w+)\s*\(\s*\)");
                    if (pub.Success && !pub.Groups[1].Value.StartsWith("_") && pub.Groups[1].Value != "OnPlayerLeft" && pub.Groups[1].Value != "OnPlayerRestored")
                        e.Add("FAIL public parameterless method " + pub.Groups[1].Value + " at " + fname + ":" + (i + 1) + " is callable over the network (use the _ prefix)");
                    if (!lines[i].Contains("[NetworkCallable")) continue;
                    var m = System.Text.RegularExpressions.Regex.Match(i + 1 < lines.Length ? lines[i + 1] : "", @"public\s+void\s+(\w+)\s*\(([^)]*)\)");
                    if (!m.Success) { e.Add("FAIL [NetworkCallable] not followed by a public void method at " + fname + ":" + (i + 2)); continue; }
                    found.Add(m.Groups[1].Value + "(" + System.Text.RegularExpressions.Regex.Replace(m.Groups[2].Value.Trim(), @"\s+", " ") + ")");
                    // 本文の最初の 2 行: IsOwner と CallingPlayer
                    string body = string.Join(" ", lines.Skip(i + 2).Take(4));
                    if (!body.Contains("if (!Networking.IsOwner(gameObject)) return;")) e.Add("FAIL " + m.Groups[1].Value + " does not check IsOwner first (" + fname + ":" + (i + 2) + ")");
                    if (!body.Contains("NetworkCalling.CallingPlayer")) e.Add("FAIL " + m.Groups[1].Value + " does not take the sender from NetworkCalling.CallingPlayer (" + fname + ":" + (i + 2) + ")");
                }
            }
            var expected = new HashSet<string>(NetworkEntries);
            if (found.Count != expected.Count || !found.All(expected.Contains)) e.Add("FAIL mailbox network entries = " + string.Join(", ", found) + " (expected " + string.Join(", ", NetworkEntries) + ")");
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
