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
using VRC.Udon;
using VRCStation = VRC.SDK3.Components.VRCStation;

namespace KominkaHillside.Editor
{
    /// <summary>DES-WLD-004 おさんぽカート (M1〜M3) の Setup (契約: docs/plans/auto-walk-cart/implementation.md の「Editor」「M1.1」「M2」「M3」節)。
    ///
    /// Cart/1 は Course/85 (KH_CourseTravelJoinEditor.Build) の流儀で次を行い、本番シーンを保存する:
    /// 1. Program Asset 5 本 (KH_CartManager / KH_CartSeat / KH_CartStationPanel / KH_CartDock / KH_CartControls) が無ければ作ってコンパイルを始め、
    ///    **シーンは変えずに止まる** (もう一度 Cart/1 を流す)。
    /// 2. 軌道 (.Track.cs): route の周回 436 節を写し、距離は節間の弦の累積 (節 0 が棘のときだけ除く。Course/21a の門前整形の後は除かない)。障害物の掃引で noLateral、
    ///    道幅の分類で trackClass / narrow 区間 / 待機点 (.M2.cs)、乗り場の開始距離 (乗り場 0 = 地図ボードの奥 6 m、他 = 着地点の投影)。Course/21 / 21a の後は Cart/1 を再実行。
    /// 3. ルート KH_Cart (冪等): CartManager (Manual)、Parking (0, -50, 0)、Carts/Cart_0..7、Stations (静的カート StationCart_0..4 + パネル Station_0..4)。
    /// 4. 走行カート: 子 Model は毎回作り直す (FBX → Unpack + LODGroup、無ければ仮形状)。本体に VRCStation + KH_CartSeat + BoxCollider (layer Ignore Raycast)、
    ///    残っている StopL / StopR の除去 (IMP-WLD-015: 肘掛け前端の停止ボタンは撤去。停止は操作帯だけ)、Docks (KH_CartDock)、Controls (KH_CartControls、layer 17、11 ボタン + 結果欄)、TabCanvas と 2 アンカー、カート上メニュー Panel、背面の掲示の 1 行。
    /// 5. 乗り場: 静的カート (メッシュだけ) を軌道の開始点から乗り場ごとの側へ「NavMesh 通行幅 + 0.8 m」外し、道側の肘掛けの上 0.55 m にパネル。Manager.panels = 乗り場 5 + カート上 8 = 13 枚。
    /// 6. 調整値 (Data/Cart/tuning.json) を Manager と KH_LeashManager.defaultDestination へ、犬 6 匹・撮影カメラ・KH_LeashManager に cartManager、
    ///    掲示画像 Data/Cart/KH_CartSign.png、翻訳表 (cart.*) を KH_CourseTravelEditor.ApplyCatalog で KD_Language へ。配線はすべて SerializedObject → CopyProxyToUdon。
    /// 7. NetworkIDs の残骸除去 → 保存。グリフ焼き (Course/27) は統括が実行する。
    /// 検査は Cart/2 (<see cref="Verify"/>)、純関数のプローブは Cart/3 (<see cref="Probe"/>)、調整値だけの再適用は Cart/4。
    /// 関連: Course/21a・21b (門前の整形、KH_CourseGateTurnEditor)、Course/16 (広場入口のベンチ除去、KH_CoursePlazaBenchEditor)。</summary>
    public static partial class KH_CartSetupEditor
    {
        public const string OutDir = "Temp/cart-20261011";
        public const string ProgramDir = "Assets/KominkaHillside/Scripts/Cart";
        public static readonly string[] ProgramNames = { "KH_CartManager", "KH_CartSeat", "KH_CartStationPanel", "KH_CartDock", "KH_CartControls" };
        public const string ModelDir = "Assets/KominkaHillside/Models/Cart";
        public const string FbxPath = ModelDir + "/KH_OsanpoCart.fbx";
        public const string BaseColorPath = ModelDir + "/KH_OsanpoCart_BaseColor.png", SignTexturePath = ModelDir + "/KH_OsanpoCart_Sign.png";
        public const string MaterialDir = "Assets/KominkaHillside/Materials/Cart";
        public const string BodyMaterialPath = MaterialDir + "/KH_OsanpoCart_Body.mat", SignMaterialPath = MaterialDir + "/KH_OsanpoCart_Sign.mat";
        public const string RootName = "KH_Cart", ManagerName = "CartManager", CartsName = "Carts", StationsName = "Stations", ParkingName = "Parking", ModelName = "Model", BoardName = "Board";
        // StopL / StopR: IMP-WLD-015 で撤去した肘掛け前端の停止ボタンの子の名前。Build の片付けと Verify の「無い」検査にだけ使う
        public const string StopLName = "StopL", StopRName = "StopR", StartName = "StartButton", JoinPrefix = "Join";
        public const int MaxCarts = 8, MaxGroups = 8, StationCount = 5;
        public static readonly Vector3 ParkingPosition = new Vector3(0f, -50f, 0f);
        public const string AnchorSeat = "Anchor_Seat", AnchorExitL = "Anchor_ExitL", AnchorExitR = "Anchor_ExitR", AnchorDogL = "Anchor_DogL", AnchorDogR = "Anchor_DogR",
            AnchorStopL = "Anchor_StopL", AnchorStopR = "Anchor_StopR", AnchorSignBack = "Anchor_SignBack";
        public static readonly string[] AnchorNames = { AnchorSeat, AnchorExitL, AnchorExitR, AnchorDogL, AnchorDogR, AnchorStopL, AnchorStopR, AnchorSignBack };
        public const string BodyMesh = "KH_OsanpoCart_Body", AxleFrontMesh = "KH_OsanpoCart_AxleFront", AxleRearMesh = "KH_OsanpoCart_AxleRear";
        public static readonly string[] MeshNames = { BodyMesh, AxleFrontMesh, AxleRearMesh };
        /// <summary>FBX の LOD1 (車輪なし)。LOD0 = Body + AxleFront + AxleRear (画面高 0.12 まで)、LOD1 = Body_LOD1 (0.03 まで)、以下 culled。仮形状には無い。</summary>
        public const string BodyLodMesh = "KH_OsanpoCart_Body_LOD1";
        public const float Lod0Height = 0.12f, Lod1Height = 0.03f;
        public const int Lod1MaxTriangles = 1500, Lod0MaxTriangles = 4000;
        // 仮形状のアンカー (asset-brief BASELINE: 全幅 0.82 / 全長 1.20 / 座面高 0.50 / 肘掛け上面約 0.75 / 車輪径 0.28。原点は車体中央の地面、+Z 前)
        static readonly Vector3[] PlaceholderAnchors = {
            new Vector3(0f, 0.50f, -0.05f),   // Seat
            new Vector3(-0.80f, 0f, 0.15f),   // ExitL
            new Vector3(0.80f, 0f, 0.15f),    // ExitR
            new Vector3(-0.30f, 0f, 1.30f),   // DogL
            new Vector3(0.30f, 0f, 1.30f),    // DogR
            new Vector3(-0.36f, 0.76f, 0.30f),// StopL (肘掛け前端。アンカーは M3 のパネル位置などに使う。停止ボタンは IMP-WLD-015 で撤去)
            new Vector3(0.36f, 0.76f, 0.30f), // StopR
            new Vector3(0f, 0.75f, -0.62f) }; // SignBack
        public static readonly Vector3 PlaceholderBodySize = new Vector3(0.82f, 0.6f, 1.2f);
        static readonly Vector3 PlaceholderAxleFront = new Vector3(0f, 0.14f, 0.42f), PlaceholderAxleRear = new Vector3(0f, 0.14f, -0.42f);
        public static readonly Vector3 BodyColliderCenter = new Vector3(0f, 0.3f, 0f), BodyColliderSize = new Vector3(0.82f, 0.6f, 1.2f);
        /// <summary>本体の層 = Ignore Raycast。接地 Raycast (KH_LeashManager.TravelGroundMask = 0|11) が自分の車体に当たらないように、
        /// また VRCStation のクリック着座を避ける (vrc-world-ui §2。DisableInteractive はシリアライズされないので Editor からは止められない)。</summary>
        public const int BodyLayer = 2, ButtonLayer = 0;
        public const VRCStation.Mobility StationMobility = VRCStation.Mobility.Immobilize;
        public const float PanelScale = 0.001f;
        // DES-UI-007 (2026-10-14): 合流の案内 (HTML B 案) を入れて 760×1000 → 1000×1174。合流の 8 行は 2 列 × 4 段の固定枠 (スクロールなし)。表示名は 2 行 (Codex 後)。
        // 板の下端は従来どおりパネルの基点の 0.50 m 下に保ち (肘掛けに埋めない)、増えた分は上へ伸ばす (Board の localPosition.y = BoardLift)。
        public static readonly Vector2 BoardSize = new Vector2(1000f, 1174f);   // M2: 800 → 1000、DES-UI-007: 760×1000 → 1000×1174
        public const float BoardBottomBelowPivot = 0.5f;   // m。M2 の 1000 の板の下端 (= 0.5 m 下) と同じ
        public static float BoardLift { get { return BoardSize.y * PanelScale * 0.5f - BoardBottomBelowPivot; } }
        public const string StationPhotoName = "Photo", StationPrName = "PR";
        // 下部の右列: 注意書き (上) → 写真 → PR (下)。Verify の「notice, photo, PR の順」は中心の y で見る
        public static readonly Vector2 StationPhotoPos = new Vector2(320f, -481f), StationPhotoSize = new Vector2(198f, 96f), StationPrPos = new Vector2(320f, -551f), StationPrSize = new Vector2(300f, 36f);
        // 合流の行 460×56 (2 列 × 4 段、左列の中心 x = -240 / 右列 +240、段の間隔 122、1 段目の上端 = 板の上から 316)。段の中は
        // 目的地・人数 / 状態 (+0..22)、ボタン (+24..80)、理由か結果 (+82..114)。
        // ボタンの内側 -218..218 を 表示名 -218..30 (幅 248、右寄せ、18 pt で 2 行。Runtime が目安の幅で改行し、2 行目の末尾は …) と「さんと / 一緒に走る」38..218 (幅 180) に分ける
        public const float RowPitch = 122f, FirstRowTop = 316f, RowColumnX = 240f, RowButtonOffset = 24f, RowNoteOffset = 82f, RowNoteHeight = 32f, JoinNamePoints = 18f;
        public const string JoinInfoName = "Info", JoinMetaPrefix = "JoinMeta", JoinStatePrefix = "JoinState", JoinNotePrefix = "JoinNote";
        public static readonly Vector2 JoinRowSize = new Vector2(460f, 56f);
        public static readonly Vector2 JoinNamePos = new Vector2(-94f, 0f), JoinNameSize = new Vector2(248f, 52f);
        public static readonly Vector2 JoinInfoPos = new Vector2(128f, 0f), JoinInfoSize = new Vector2(180f, 52f);
        public const float Smoothness = 0.25f, Metallic = 0f;
        public static readonly string[] CartKeys = {
            "cart.title", "cart.start", "cart.join", "cart.notice", "cart.stop", "cart.accepting", "cart.accepting.on", "cart.accepting.off",
            "cart.result.full", "cart.result.closed", "cart.result.here", "cart.result.busy", "cart.result.started", "cart.result.joined", "cart.result.left",
            "cart.menu", "cart.monitor", "cart.seat.up", "cart.seat.down", "cart.turn.left", "cart.turn.right",   // M1.1
            "cart.seat.forward", "cart.seat.back",   // M1.1 追加 (座面の前後)
            "cart.result.waiting", "cart.guide", "cart.row.closed", "cart.row.ending", "cart.sign.caption", "cart.station.pr",   // M2
            "cart.parked",   // M3
            // DES-UI-007 (2026-10-14): 合流の案内 (HTML B 案)。既存キーの文言は変えない
            "cart.board.lead", "cart.board.lead2", "cart.board.step1", "cart.board.step1.sub", "cart.board.step2", "cart.board.step2.sub",
            "cart.board.avail.yes", "cart.board.avail.no", "cart.board.avail.note",
            "cart.board.join.title", "cart.board.join.count", "cart.board.join.note", "cart.board.join.suffix", "cart.board.join.action",
            "cart.board.row.dest", "cart.board.row.closedNote", "cart.board.row.endingNote", "cart.board.empty.title", "cart.board.empty.body",
            "cart.board.start.title", "cart.board.start.note", "cart.board.start.dest", "cart.board.start.destNote", "cart.board.start.button",
            "cart.board.checking.start", "cart.board.checking.join",
            "cart.board.result.started", "cart.board.result.joined", "cart.board.result.full", "cart.board.result.closed", "cart.board.result.unavailable",
            "cart.board.camera", "cart.board.camera.pc", "cart.board.rejoin.title", "cart.board.rejoin.body" };

        [Serializable] class BuildReport
        {
            public string utc, scene, status, reason, model;
            public bool programAssetsCreated, placeholderModel;
            public int droppedNetworkIds;
            public List<string> stations = new List<string>();
            public string rerun = RerunNote;
            public int trackNodes, sweepCandidates, sweepSkippedLarge;
            public float trackLength, loopLength, closingSegment;
            public float[] spotTrackDistance, destTrackDistance, noLateralFrom, noLateralTo;
            public List<string> sweepHits = new List<string>();
            public float[] classLength = new float[3];   // M2: class 0/1/2 の延長 (m)
            public List<string> narrowZones = new List<string>();
            public string tuning;
            public List<string> log = new List<string>();
        }

        // ============================================================
        // Cart/1 Build
        // ============================================================

        [MenuItem("Tools/KominkaHillside/Cart/1 Build Carts And Stations")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            Directory.CreateDirectory(OutDir);
            var report = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };

            // ---- 1. Program Asset (作ったらシーンを変えずに止まる) ----
            bool created = false;
            foreach (var name in ProgramNames)
            {
                string path = ProgramDir + "/" + name;
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path + ".asset") != null) continue;
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path + ".cs");
                if (asset.sourceCsScript == null) throw new InvalidOperationException(path + ".cs not imported yet (Runtime files missing); refresh first");
                AssetDatabase.CreateAsset(asset, path + ".asset"); created = true;
                report.log.Add("created " + path + ".asset");
            }
            if (created)
            {
                AssetDatabase.SaveAssets(); UdonSharpProgramAsset.CompileAllCsPrograms(true);
                report.status = "PROGRAM_ASSET_CREATED"; report.programAssetsCreated = true;
                report.reason = "Created cart Program Assets. Wait for the U# compile (unity-compile.sh --udon), then run Cart/1 again. Scene unchanged.";
                File.WriteAllText(OutDir + "/build.json", JsonUtility.ToJson(report, true));
                Debug.Log("[KH Cart] " + report.reason);
                return;
            }
            foreach (var name in ProgramNames)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramDir + "/" + name + ".asset");
                if (asset.sourceCsScript == null) throw new InvalidOperationException(name + ".asset has no source script");
            }

            // ---- 2. 既存の参照先 (型で探し、手元メニューの配線と突き合わせる) ----
            var p = KH_CourseTravelEditor.FindPouch();
            var leash = Single<KH_LeashManager>("KH_LeashManager");
            if (p.manager != leash) throw new InvalidOperationException("KH_LeashManager differs from the menu's manager");
            var route = leash.route;
            if (route == null || !route.closedLoop || route.loopDistances == null || route.loopDistances.Length != route.count || route.loopLength <= 0f)
                throw new InvalidOperationException("Closed-loop KH_WalkRoute with loopDistances required");
            var language = Single<KD_Language>("KD_Language");
            if (p.language != language) throw new InvalidOperationException("KD_Language differs from the menu's language");
            var fades = UnityEngine.Object.FindObjectsOfType<KH_TravelFade>(true);
            if (fades.Length > 1) throw new InvalidOperationException("More than one KH_TravelFade");
            var fade = fades.Length == 1 ? fades[0] : null;
            if (fade == null) report.log.Add("WARN KH_TravelFade not found; manager.fade left null (join without fade)");
            var font = p.sectionTitleLabel != null ? p.sectionTitleLabel.font : null;
            if (font == null) throw new InvalidOperationException("Shared TMP font missing (KD_TreatPouch.sectionTitleLabel.font)");
            if (leash.spotPositions == null || leash.spotPositions.Length < StationCount || leash.spotYaws == null || leash.spotYaws.Length < StationCount)
                throw new InvalidOperationException("KH_LeashManager.spotPositions / spotYaws need " + StationCount + " entries (run Course/80)");
            // ---- 3. 資産側: import 設定 (再 import するので FBX はこの後に読む) と材質 (シーンより先) ----
            ConfigureImporters(report.log);
            // ---- M1.1: カート専用の軌道 (シーンを変える前に計算。掃引はシーンの Renderer を読むだけ) ----
            var track = BuildTrack(route);
            ProjectStops(track, route, leash);
            SweepObstacles(track);
            report.trackNodes = track.positions.Length; report.trackLength = track.length; report.loopLength = route.loopLength; report.closingSegment = track.closing;
            report.spotTrackDistance = track.spot; report.destTrackDistance = track.dest; report.noLateralFrom = track.noFrom; report.noLateralTo = track.noTo;
            report.sweepHits = track.sweepHits; report.sweepCandidates = track.sweepCandidates; report.sweepSkippedLarge = track.sweepSkippedLarge;
            // ---- M2: 調整 JSON と道幅の分類 (NavMesh を読むだけ) ----
            var tuning = LoadTuning(); report.tuning = JsonUtility.ToJson(tuning);
            var traffic = ClassifyTrack(track, tuning.gap);
            report.classLength = traffic.lengthByClass; report.narrowZones = traffic.zones;
            var video = SingleOrNull<KH_VideoCamera>("KH_VideoCamera", report.log);
            var photo = SingleOrNull<KD_PhotoViewer>("KD_PhotoViewer", report.log);
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbx != null)
            {
                var missing = AnchorNames.Concat(MeshNames).Concat(new[] { BodyLodMesh }).Where(n => FindDeep(fbx.transform, n) == null).ToArray();
                if (missing.Length > 0) throw new InvalidOperationException("FBX " + FbxPath + " lacks: " + string.Join(", ", missing) + ". Scene unchanged.");
            }
            report.placeholderModel = fbx == null;
            report.model = fbx != null ? FbxPath : "PLACEHOLDER (Cube 0.82x0.6x1.2 + anchors)";
            var bodyMat = EnsureMaterial(BodyMaterialPath, BaseColorPath, report.log);
            ComposeSign(report.log);   // M2: 背面の掲示 = Data/Cart/KH_CartSign.png (Set4 の文字の無い帯)
            var signMat = EnsureMaterial(SignMaterialPath, SignOutPath, report.log);
            AssetDatabase.SaveAssets();

            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-cart.unity", true);
            var ui = new StringBuilder();
            try
            {
                var scene = p.gameObject.scene;
                var rootGo = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName) ?? new GameObject(RootName);
                var root = rootGo.transform;
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity); root.localScale = Vector3.one; rootGo.SetActive(true);

                // ---- Manager ----
                var managerGo = EnsureChild(root, ManagerName); Reset(managerGo.transform);
                var manager = EnsureU<KH_CartManager>(managerGo);
                Backing(manager, Networking.SyncType.Manual);
                var parking = EnsureChild(root, ParkingName).transform; parking.localPosition = ParkingPosition; parking.localRotation = Quaternion.identity; parking.localScale = Vector3.one;
                var cartsRoot = EnsureChild(root, CartsName).transform; Reset(cartsRoot);
                var stationsRoot = EnsureChild(root, StationsName).transform; Reset(stationsRoot);

                // ---- カート 8 台 ----
                var seats = new KH_CartSeat[MaxCarts];
                for (int i = 0; i < MaxCarts; i++) seats[i] = BuildCart(scene, cartsRoot, i, manager, language, parking, fbx, bodyMat, signMat, report.log, p, video, photo, font, ui);

                // ---- 乗り場パネル 5 枚 ----
                Physics.SyncTransforms();
                // M3: 乗り場 = 静的カート (メッシュだけ) + その右肘掛けの上のパネル。Manager.panels = 乗り場 5 + カート上 8 = 13
                var panels = new KH_CartStationPanel[StationCount + MaxCarts];
                for (int s = 0; s < StationCount; s++) panels[s] = BuildStation(scene, stationsRoot, s, manager, language, leash, track, font, fbx, bodyMat, signMat, ui, report);
                report.log.Add("station 0 start: " + track.spot0Basis + " -> d=" + track.spot[0].ToString("F1"));
                for (int i = 0; i < MaxCarts; i++) panels[StationCount + i] = seats[i].GetComponentsInChildren<KH_CartStationPanel>(true).FirstOrDefault(x => x.name == CartPanelName);

                // ---- Manager の配線 ----
                var so = new SerializedObject(manager);
                SetRef(so, "route", route); SetRef(so, "leash", leash); SetRef(so, "language", language); SetRef(so, "fade", fade); SetRef(so, "parking", parking);
                SetRefs(so, "carts", seats); SetRefs(so, "panels", panels);
                so.ApplyModifiedPropertiesWithoutUndo();
                WriteTrack(manager, track);
                WriteTraffic(manager, traffic);
                WriteTuning(manager, tuning, report.log);
                UdonSharpEditorUtility.CopyProxyToUdon(manager); EditorUtility.SetDirty(manager);
                WireCartManagerRefs(manager, report.log);

                // ---- KH_LeashManager.cartManager ----
                var lso = new SerializedObject(leash);
                SetRef(lso, "cartManager", manager);
                lso.ApplyModifiedPropertiesWithoutUndo();
                UdonSharpEditorUtility.CopyProxyToUdon(leash); EditorUtility.SetDirty(leash);
                WriteDefaultDestination(leash, tuning, report.log);   // さんぽの目的地の既定 (tuning.json)

                // ---- 文言 (Course/80・85 と同じ経路で extension を入れ直す) ----
                KH_CourseTravelEditor.ApplyCatalog(p);

                var descriptor = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
                if (descriptor != null)
                {
                    int dropped = descriptor.NetworkIDCollection.RemoveAll(x => x.gameObject == null && (x.SerializedTypeNames == null || x.SerializedTypeNames.Count == 0));
                    if (dropped > 0) { report.droppedNetworkIds = dropped; EditorUtility.SetDirty(descriptor); }
                    int typed = descriptor.NetworkIDCollection.Count(x => x.gameObject == null);
                    if (typed > 0) report.log.Add("WARN NetworkIDs: " + typed + " typed entries point at missing objects (left for review)");
                }
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Cart build save failed");
            }
            catch
            {
                Debug.LogError("[KH Cart] Build failed; the scene before this run is at " + OutDir + "/before-cart.unity. Reopen the scene without saving to discard partial changes.");
                throw;
            }
            if (ui.Length > 0) report.log.AddRange(ui.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
            report.status = "BUILT";
            File.WriteAllText(OutDir + "/build.json", JsonUtility.ToJson(report, true));
            Debug.Log("[KH Cart] TRACK nodes=" + report.trackNodes + " length=" + report.trackLength.ToString("F2") + " (loop " + report.loopLength.ToString("F2") + ", closing " + report.closingSegment.ToString("F2")
                + ") noLateral=" + string.Join(",", report.noLateralFrom.Select((a, k) => a.ToString("F0") + "-" + report.noLateralTo[k].ToString("F0"))) + " hits=" + report.sweepHits.Count + ". " + RerunNote);
            Debug.Log("[KH Cart] TRAFFIC class0=" + report.classLength[0].ToString("F0") + "m class1=" + report.classLength[1].ToString("F0") + "m class2=" + report.classLength[2].ToString("F0") + "m zones=" + report.narrowZones.Count + "\n" + string.Join("\n", report.narrowZones));
            Debug.Log("[KH Cart] BUILT carts=" + MaxCarts + " stations=" + StationCount + " model=" + (report.placeholderModel ? "PLACEHOLDER (仮形状: FBX not found, rerun Cart/1 after " + FbxPath + " arrives)" : "FBX")
                + " droppedNetworkIds=" + report.droppedNetworkIds + ". Next: Cart/2 Verify, Cart/3 Probe, then run Course/27 (glyphs for cart.*) and save. Details: " + OutDir + "/build.json");
        }

        // ============================================================
        // カート
        // ============================================================

        static KH_CartSeat BuildCart(UnityEngine.SceneManagement.Scene scene, Transform cartsRoot, int i, KH_CartManager manager, KD_Language language, Transform parking,
            GameObject fbx, Material bodyMat, Material signMat, List<string> log, KD_TreatPouch menu, KH_VideoCamera video, KD_PhotoViewer photo, TMP_FontAsset font, StringBuilder ui)
        {
            var go = EnsureChild(cartsRoot, "Cart_" + i);
            go.layer = BodyLayer; go.SetActive(true);
            go.transform.SetPositionAndRotation(parking.position, parking.rotation); go.transform.localScale = Vector3.one;

            // Model は毎回作り直す (FBX が来たら再実行で差し替わる)
            var old = go.transform.Find(ModelName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            GameObject model;
            if (fbx != null)
            {
                model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, scene);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
            else model = BuildPlaceholder();
            model.name = ModelName;
            model.transform.SetParent(go.transform, false);
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity; model.transform.localScale = Vector3.one;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) { if (i == 0) log.Add("removed collider under Model: " + c.name); UnityEngine.Object.DestroyImmediate(c); }
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            AssignMaterials(renderers, bodyMat, signMat, i == 0 ? log : null);

            Transform A(string n) { var t = FindDeep(model.transform, n); if (t == null) throw new InvalidOperationException("Cart_" + i + ": " + n + " missing under Model"); return t; }
            Transform seatT = A(AnchorSeat), exitL = A(AnchorExitL), exitR = A(AnchorExitR), signBack = A(AnchorSignBack), axleFront = A(AxleFrontMesh), axleRear = A(AxleRearMesh);

            // LODGroup (Model 直下。Model ごと毎回作り直す)。仮形状は LOD1 が無いので付けない
            if (fbx != null)
            {
                Renderer R(string n) { var r = A(n).GetComponent<Renderer>(); if (r == null) throw new InvalidOperationException("Cart_" + i + ": " + n + " has no Renderer"); return r; }
                var lodGroup = model.GetComponent<LODGroup>(); if (lodGroup == null) lodGroup = model.AddComponent<LODGroup>();
                lodGroup.SetLODs(new[] {
                    new LOD(Lod0Height, new[] { R(BodyMesh), R(AxleFrontMesh), R(AxleRearMesh) }),
                    new LOD(Lod1Height, Lod1Renderers(model.transform, R(BodyLodMesh))) });
                lodGroup.fadeMode = LODFadeMode.None;
                lodGroup.RecalculateBounds();
                EditorUtility.SetDirty(lodGroup);
            }

            // 車体の当たり (Ignore Raycast)
            var col = go.GetComponent<BoxCollider>(); if (col == null) col = go.AddComponent<BoxCollider>();
            col.center = BodyColliderCenter; col.size = BodyColliderSize; col.isTrigger = false; EditorUtility.SetDirty(col);

            // 座席 (VRCStation)
            var station = go.GetComponent<VRCStation>(); if (station == null) station = go.AddComponent<VRCStation>();
            station.PlayerMobility = StationMobility; station.disableStationExit = false; station.seated = true; station.canUseStationFromStation = false;
            station.stationEnterPlayerLocation = seatT; station.stationExitPlayerLocation = exitL;
            EditorUtility.SetDirty(station);

            var seat = EnsureU<KH_CartSeat>(go);
            Backing(seat, Networking.SyncType.None);

            // IMP-WLD-015 (2026-10-13): 肘掛け前端の停止ボタン (StopL / StopR、KH_CartStopButton) は撤去。停止は操作帯の「停止」だけ。
            // StopL / StopR は Model の外 (カート直下) にあるので Model の作り直しでは消えない。前の Build の残りをここで消す
            // (Program Asset と .cs を消した後でも GameObject ごと消せる)
            for (int k = 0; k < 2; k++)
            {
                string sn = k == 0 ? StopLName : StopRName;
                for (var t = go.transform.Find(sn); t != null; t = go.transform.Find(sn))
                {
                    if (i == 0) log.Add("removed " + go.name + "/" + sn + " (IMP-WLD-015 armrest stop switch)");
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
                }
            }

            var so = new SerializedObject(seat);
            SetRef(so, "manager", manager); SetInt(so, "index", i); SetRef(so, "station", station);
            SetRef(so, "seat", seatT); SetRef(so, "exitL", exitL); SetRef(so, "exitR", exitR);
            SetRef(so, "signBack", signBack); SetRef(so, "axleFront", axleFront); SetRef(so, "axleRear", axleRear);
            SetRefs(so, "renderers", renderers);
            BuildCartUi(go, i, seatT, manager, language, menu, video, photo, font, ui, out KH_CartDock dock, out KH_CartControls controls);
            SetRef(so, "controls", controls); SetRef(so, "dock", dock);
            // M3: カート上メニュー (Parked のときだけ Runtime が出す)。右肘掛けの上 0.55 m、外側 (カートの右) を向く
            var armR = A(AnchorStopR);
            var cpPos = armR.position + go.transform.up * PanelAboveArmrest;
            var cpRot = Quaternion.LookRotation(-go.transform.right, go.transform.up);   // 表 (-Z) = カートの右
            var cartPanel = BuildPanel(go.transform, CartPanelName, cpPos, cpRot, -1, i, manager, language, font, ui, false);
            SetRefIfPresent(so, "cartPanel", cartPanel, i == 0 ? log : new List<string>());
            // M2: 背面の掲示の下の 1 行 (cart.sign.caption)。Model の中なので Model と一緒に毎回作り直す
            var caption = BuildSignCaption(go.transform, signBack, font);
            SetRefIfPresent(so, "signCaption", caption, i == 0 ? log : new List<string>());
            so.ApplyModifiedPropertiesWithoutUndo();
            UdonSharpEditorUtility.CopyProxyToUdon(seat); EditorUtility.SetDirty(seat);
            return seat;
        }

        // ---- M2: LOD1 の車軸 (Model 担当が FBX に足す。あれば LOD1 に含める) ----
        public const string AxleFrontLodMesh = "KH_OsanpoCart_AxleFront_LOD1", AxleRearLodMesh = "KH_OsanpoCart_AxleRear_LOD1";
        static Renderer[] Lod1Renderers(Transform model, Renderer body)
        {
            var list = new List<Renderer> { body };
            foreach (var n in new[] { AxleFrontLodMesh, AxleRearLodMesh }) { var t = FindDeep(model, n); var r = t != null ? t.GetComponent<Renderer>() : null; if (r != null) list.Add(r); }
            return list.ToArray();
        }

        // ---- M2: 背面の掲示の下の 1 行 ----
        public const string SignCaptionName = "SignCaption";
        public static readonly Vector2 SignCaptionPx = new Vector2(420f, 56f);
        public const float SignCaptionBelow = 0.14f, SignCaptionBehind = 0.006f;   // Anchor_SignBack から下へ / 車体の後ろへ (m)

        /// <summary>WorldSpace Canvas (scale 0.001、操作なし: Raycaster / VRCUiShape / Collider を付けない、layer Default) の TMP 1 行。
        /// 後ろから読む向き = Canvas の表 (-Z) がカートの後方 (-Z) を向く = カートと同じ回転。</summary>
        static TextMeshProUGUI BuildSignCaption(Transform cart, Transform signBack, TMP_FontAsset font)
        {
            var old = signBack.Find(SignCaptionName); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var go = new GameObject(SignCaptionName, typeof(RectTransform)); go.layer = 0;
            go.transform.SetParent(signBack, false);
            go.transform.position = signBack.position - cart.up * SignCaptionBelow - cart.forward * SignCaptionBehind;
            go.transform.rotation = cart.rotation;
            var rt = (RectTransform)go.transform; rt.sizeDelta = SignCaptionPx;
            var ls = signBack.lossyScale; rt.localScale = new Vector3(0.001f / ls.x, 0.001f / ls.y, 0.001f / ls.z);
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var plate = go.AddComponent<Image>(); plate.color = new Color(.20f, .15f, .11f, .88f); plate.raycastTarget = false; KD_UiSpriteEditor.ApplyRoundSkin(plate, KD_UiSpriteEditor.LoadRound());
            var label = MakeText(go.transform, "Label", Vector2.zero, SignCaptionPx, 34f, font, Ja("cart.sign.caption"));
            label.enableAutoSizing = true; label.fontSizeMin = 16f; label.fontSizeMax = 34f; label.color = new Color(.96f, .92f, .84f);
            EditorUtility.SetDirty(label);
            return label;
        }

        /// <summary>FBX が無いときの仮形状: Body = Cube (0.82×0.6×1.2、原点は地面中央)、車軸と 8 アンカーは空の Transform (契約と同じ名前)。</summary>
        static GameObject BuildPlaceholder()
        {
            var model = new GameObject(ModelName);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = BodyMesh;
            UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(model.transform, false);
            body.transform.localPosition = new Vector3(0f, PlaceholderBodySize.y * 0.5f, 0f); body.transform.localScale = PlaceholderBodySize;
            Empty(model.transform, AxleFrontMesh, PlaceholderAxleFront);
            Empty(model.transform, AxleRearMesh, PlaceholderAxleRear);
            for (int a = 0; a < AnchorNames.Length; a++) Empty(model.transform, AnchorNames[a], PlaceholderAnchors[a]);
            return model;
        }

        static void Empty(Transform parent, string name, Vector3 local)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = local;
        }

        /// <summary>材質の割当: 取り込み時の材質名か Renderer 名に "Sign" を含む枠 → Sign、それ以外 → Body。
        /// どの枠も Sign に当たらず、Body メッシュが 2 枠なら 2 枠目を Sign とする。</summary>
        static void AssignMaterials(Renderer[] renderers, Material bodyMat, Material signMat, List<string> log)
        {
            bool anySign = false;
            var plan = new Dictionary<Renderer, Material[]>();
            foreach (var r in renderers)
            {
                var src = r.sharedMaterials; var dst = new Material[Mathf.Max(1, src.Length)];
                for (int k = 0; k < dst.Length; k++)
                {
                    var m = k < src.Length ? src[k] : null;
                    bool sign = (m != null && m.name.IndexOf("Sign", StringComparison.OrdinalIgnoreCase) >= 0) || (dst.Length == 1 && r.name.IndexOf("Sign", StringComparison.OrdinalIgnoreCase) >= 0);
                    anySign |= sign;
                    dst[k] = sign ? signMat : bodyMat;
                }
                plan[r] = dst;
            }
            if (!anySign)
            {
                var body = renderers.FirstOrDefault(r => r.name == BodyMesh);
                if (body != null && plan[body].Length == 2) { plan[body][1] = signMat; anySign = true; if (log != null) log.Add("material: no slot named Sign; Body slot 1 -> Sign"); }
                // LOD1 は Body と同じ割当 (同じ枠数なら同じ並び)
                var lod1 = renderers.FirstOrDefault(r => r.name == BodyLodMesh);
                if (body != null && lod1 != null && plan[lod1].Length == plan[body].Length) plan[lod1] = (Material[])plan[body].Clone();
            }
            foreach (var kv in plan)
            {
                if (log != null) log.Add("material " + kv.Key.name + " [" + string.Join(", ", kv.Key.sharedMaterials.Select(m => m != null ? m.name : "null")) + "] -> [" + string.Join(", ", kv.Value.Select(m => m.name)) + "]");
                kv.Key.sharedMaterials = kv.Value; EditorUtility.SetDirty(kv.Key);
            }
            if (!anySign && log != null) log.Add("material: Sign material not assigned (no Sign slot; placeholder or single-slot FBX)");
        }

        // ============================================================
        // 乗り場パネル
        // ============================================================

        // ---- M3: カートステーション ----
        public const string StationCartPrefix = "StationCart_", CartPanelName = "Panel";
        public const float PanelAboveArmrest = 0.55f;

        /// <summary>乗り場 s: 静的カート (FBX のメッシュだけ。Udon / Station / Collider なし、layer Default、LODGroup 付き) を StationCartPoseFor の姿勢に置き、
        /// 道側の肘掛け (StationArmrest) の上 0.55 m に乗り場パネル (表 = 道の点を向く)。</summary>
        static KH_CartStationPanel BuildStation(UnityEngine.SceneManagement.Scene scene, Transform stationsRoot, int s, KH_CartManager manager, KD_Language language, KH_LeashManager leash, Track track, TMP_FontAsset font,
            GameObject fbx, Material bodyMat, Material signMat, StringBuilder log, BuildReport report)
        {
            StationCartPoseFor(s, track, leash, out Vector3 cartPos, out Quaternion cartRot, out Vector3 spot, out float groundDelta, out bool hit);
            var sc = BuildStationCart(scene, stationsRoot, s, cartPos, cartRot, fbx, bodyMat, signMat);
            var armR = FindDeep(sc.transform, StationArmrest(s));   // 道側の肘掛け (右に置いた乗り場は左肘掛け、左に置いた入口は右肘掛け)
            Vector3 pos = armR.position + sc.transform.up * PanelAboveArmrest;
            var face = pos - spot; face.y = 0f;
            Quaternion rot = Quaternion.LookRotation(face.sqrMagnitude > 1e-6f ? face.normalized : sc.transform.right, Vector3.up);   // 表 (-Z) が道の点 (aisle) を向く
            report.stations.Add("Station_" + s + " aisle=" + spot.ToString("F2") + " side=" + (StationSide[s] < 0 ? "left" : "right") + " lateral=" + StationCartLateral(s, track).ToString("F2") + " m cart=" + cartPos.ToString("F2") + " panel=" + pos.ToString("F2") + " ground=" + (hit ? "hit dy=" + groundDelta.ToString("F2") : "MISS (spot height used)"));
            return BuildPanel(stationsRoot, "Station_" + s, pos, rot, s, -1, manager, language, font, log, true);
        }

        /// <summary>乗り場 s の静的カートの姿勢と、パネルが向く通路側の点 (aisle = 軌道上の開始点 spotTrackDistance[s])。開始点から StationSide の側へ
        /// StationCartLateral (NavMesh 通行幅 + 0.8 m、2.2〜3.5 m) 外し、地面に置き、向きは軌道の進行方向 (+1)。Build と Verify が同じ式を使う。</summary>
        public static void StationCartPoseFor(int s, Track track, KH_LeashManager leash, out Vector3 pos, out Quaternion rot, out Vector3 aisle, out float groundDelta, out bool hit)
        {
            // 2026-10-12 ユーザー要望 (見本カートが道に掛かって邪魔): 軌道上の開始点 (spotTrackDistance[s]) から、乗り場ごとの側 (StationSide) へ
            // 「その側の NavMesh 通行幅 + 0.8 m」(2.2〜3.5 m) 外す。向きは軌道の進行方向 (+1)。パネルが向く通路側 = その軌道点
            aisle = TrackPointRef(track, track.spot[s]);
            var f = TrackDirectionRef(track, track.spot[s]);
            var right = new Vector3(f.z, 0f, -f.x);
            float lat = StationCartLateral(s, track);
            var flat = aisle + right * (StationSide[s] * lat);
            float groundY = aisle.y;
            hit = Physics.Raycast(new Vector3(flat.x, aisle.y + 5f, flat.z), Vector3.down, out RaycastHit h, 15f, KH_LeashManager.TravelGroundMask, QueryTriggerInteraction.Ignore);
            if (hit) groundY = h.point.y;
            groundDelta = groundY - aisle.y;
            pos = new Vector3(flat.x, groundY, flat.z);
            rot = Quaternion.LookRotation(f, Vector3.up);
        }

        /// <summary>乗り場ごとの側: -1 = 進行方向 (+1) の左、+1 = 右。乗り場 0 (入口) は地図ボード側 = 広場へ向かって左。</summary>
        public static readonly int[] StationSide = { -1, +1, +1, +1, +1 };
        public const float StationCartClearance = 0.8f, StationCartLatMin = 2.2f, StationCartLatMax = 3.5f;
        /// <summary>パネルを載せる肘掛け = 道の側 (カートが道の右なら左肘掛け、左なら右肘掛け)。</summary>
        public static string StationArmrest(int s) { return StationSide[s] > 0 ? AnchorStopL : AnchorStopR; }

        /// <summary>静的カートの横ずれ (m): 軌道中心から StationSide の側の NavMesh 通行幅 (SideWidth、0.25 m 刻み・最大 4 m) + 0.8 m、[2.2, 3.5] に収める。</summary>
        public static float StationCartLateral(int s, Track track)
        {
            var p = TrackPointRef(track, track.spot[s]); var f = TrackDirectionRef(track, track.spot[s]);
            var side = new Vector3(f.z, 0f, -f.x) * StationSide[s];
            return Mathf.Clamp(SideWidth(p, side) + StationCartClearance, StationCartLatMin, StationCartLatMax);
        }

        /// <summary>静的カート (毎回作り直す)。FBX が無ければ仮形状。Collider と Udon は付けない。</summary>
        static GameObject BuildStationCart(UnityEngine.SceneManagement.Scene scene, Transform parent, int s, Vector3 pos, Quaternion rot, GameObject fbx, Material bodyMat, Material signMat)
        {
            var old = parent.Find(StationCartPrefix + s); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            GameObject model;
            if (fbx != null) { model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, scene); PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction); }
            else model = BuildPlaceholder();
            model.name = StationCartPrefix + s;
            model.transform.SetParent(parent, false);
            model.transform.SetPositionAndRotation(pos, rot); model.transform.localScale = Vector3.one;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            AssignMaterials(renderers, bodyMat, signMat, null);
            if (fbx != null)
            {
                Renderer R(string n) { var t = FindDeep(model.transform, n); return t != null ? t.GetComponent<Renderer>() : null; }
                var lodGroup = model.GetComponent<LODGroup>(); if (lodGroup == null) lodGroup = model.AddComponent<LODGroup>();
                lodGroup.SetLODs(new[] { new LOD(Lod0Height, new[] { R(BodyMesh), R(AxleFrontMesh), R(AxleRearMesh) }), new LOD(Lod1Height, Lod1Renderers(model.transform, R(BodyLodMesh))) });
                lodGroup.fadeMode = LODFadeMode.None; lodGroup.RecalculateBounds(); EditorUtility.SetDirty(lodGroup);
            }
            return model;
        }

        /// <summary>DES-UI-007: 板の文字 1 本の置き場 (板の上端からの top と高さ、中心 x、幅、pt、オートサイズの下限 (0 = 固定)、折返し、寄せ、色)。
        /// parent が空なら Board 直下、"Empty" / "Camera" ならその入れ物 (板と同じ大きさ・位置) の下。key があるものは固定の文言 (guideLabels / guideKeys)。</summary>
        public sealed class PanelText
        {
            public string name, parent = "", key = "";
            public float x, top, w, h, pt, min;
            public bool wrap;
            public TextAlignmentOptions align = TextAlignmentOptions.Left;
            public Color color = Color.white;
            public Vector2 Pos { get { return new Vector2(x, BoardSize.y * .5f - top - h * .5f); } }
            public Vector2 Size { get { return new Vector2(w, h); } }
        }
        public const string EmptyName = "Empty", CameraName = "Camera";
        public static readonly Color TextSub = new Color(.83f, .83f, .78f), TextGold = new Color(.92f, .84f, .66f), TextCream = new Color(1f, .93f, .80f);
        /// <summary>固定の文言 (言語が変わったときだけ Runtime が書く)。並び = guideLabels / guideKeys。</summary>
        public static readonly PanelText[] GuideTexts = {
            new PanelText { name = "Lead",          key = "cart.board.lead",           x = 0f,    top = 66f,   w = 940f, h = 26f, pt = 19f, min = 13f },
            new PanelText { name = "Lead2",         key = "cart.board.lead2",          x = 0f,    top = 92f,   w = 940f, h = 26f, pt = 19f, min = 13f },
            new PanelText { name = "Step1",         key = "cart.board.step1",          x = -219f, top = 124f,  w = 418f, h = 26f, pt = 19f, min = 12f },
            new PanelText { name = "Step1Sub",      key = "cart.board.step1.sub",      x = -219f, top = 150f,  w = 418f, h = 22f, pt = 15f, min = 10f, color = TextSub },
            new PanelText { name = "Step2",         key = "cart.board.step2",          x = 261f,  top = 124f,  w = 418f, h = 26f, pt = 19f, min = 12f },
            new PanelText { name = "Step2Sub",      key = "cart.board.step2.sub",      x = 261f,  top = 150f,  w = 418f, h = 22f, pt = 15f, min = 10f, color = TextSub },
            new PanelText { name = "AvailNote",     key = "cart.board.avail.note",     x = 0f,    top = 222f,  w = 940f, h = 22f, pt = 15f, min = 10f, color = TextSub },
            new PanelText { name = "JoinTitle",     key = "cart.board.join.title",     x = -220f, top = 258f,  w = 500f, h = 32f, pt = 24f, min = 16f },
            new PanelText { name = "JoinNote",      key = "cart.board.join.note",      x = 0f,    top = 290f,  w = 940f, h = 22f, pt = 15f, min = 10f, color = TextSub },
            new PanelText { name = "EmptyTitle",    key = "cart.board.empty.title",    x = 0f,    top = 420f,  w = 860f, h = 30f, pt = 22f, min = 14f, parent = EmptyName, align = TextAlignmentOptions.Center },
            new PanelText { name = "EmptyBody",     key = "cart.board.empty.body",     x = 0f,    top = 456f,  w = 800f, h = 90f, pt = 17f, min = 12f, parent = EmptyName, align = TextAlignmentOptions.Center, wrap = true, color = TextSub },
            new PanelText { name = "StartTitle",    key = "cart.board.start.title",    x = -240f, top = 816f,  w = 460f, h = 30f, pt = 24f, min = 16f },
            new PanelText { name = "StartNote",     key = "cart.board.start.note",     x = -240f, top = 846f,  w = 460f, h = 40f, pt = 15f, min = 10f, wrap = true, color = TextSub },
            new PanelText { name = "StartDestNote", key = "cart.board.start.destNote", x = -240f, top = 912f,  w = 460f, h = 40f, pt = 14f, min = 10f, wrap = true, color = TextSub },
            new PanelText { name = "CameraTip",     key = "cart.board.camera",         x = -160f, top = 968f,  w = 620f, h = 22f, pt = 16f, min = 11f, parent = CameraName, color = TextGold },
            new PanelText { name = "CameraPc",      key = "cart.board.camera.pc",      x = -160f, top = 990f,  w = 620f, h = 22f, pt = 14f, min = 10f, parent = CameraName, color = TextSub },
            new PanelText { name = "RejoinTitle",   key = "cart.board.rejoin.title",   x = -160f, top = 1018f,  w = 620f, h = 22f, pt = 16f, min = 11f },
            new PanelText { name = "RejoinBody",    key = "cart.board.rejoin.body",    x = -160f, top = 1040f, w = 620f, h = 60f, pt = 14f, min = 10f, wrap = true, color = TextSub },
        };
        /// <summary>状態で変わる文字と、既存の名前の文字 (Title / Notice / Result / PR)。</summary>
        public static readonly PanelText TitleText  = new PanelText { name = "Title",       x = 0f,    top = 18f,   w = 940f, h = 46f, pt = 32f, min = 0f };
        public static readonly PanelText AvailText  = new PanelText { name = "Avail",       x = 0f,    top = 192f,  w = 940f, h = 30f, pt = 22f, min = 14f };
        public static readonly PanelText CountText  = new PanelText { name = "JoinCount",   x = 260f,  top = 262f,  w = 420f, h = 26f, pt = 17f, min = 11f, align = TextAlignmentOptions.Right, color = TextSub };
        public static readonly PanelText DestText   = new PanelText { name = "StartDest",   x = -240f, top = 888f,  w = 460f, h = 24f, pt = 17f, min = 11f };
        public static readonly PanelText ResultText = new PanelText { name = "Result",      x = 240f,  top = 894f,  w = 460f, h = 58f, pt = 16f, min = 10f, wrap = true, align = TextAlignmentOptions.Center, color = TextGold };
        public static readonly PanelText NoticeText = new PanelText { name = "Notice",      x = 320f,  top = 968f,  w = 300f, h = 48f, pt = 14f, min = 10f, wrap = true, color = TextCream };
        public static readonly PanelText PrText     = new PanelText { name = StationPrName, x = 320f,  top = 1120f, w = 300f, h = 36f, pt = 13f, min = 10f, wrap = true, color = TextCream };
        /// <summary>行 i の上端 (板の上から) と列の中心 x。i = 0,1 が 1 段目の左・右 (g の順に左→右、上→下)。</summary>
        public static float RowTop(int i) { return FirstRowTop + (i / 2) * RowPitch; }
        public static float RowX(int i) { return i % 2 == 0 ? -RowColumnX : RowColumnX; }
        public static PanelText RowMeta(int i)  { return new PanelText { name = JoinMetaPrefix + i,  x = RowX(i) - 65f,  top = RowTop(i),       w = 330f, h = 22f, pt = 15f, min = 10f, color = TextSub }; }
        public static PanelText RowState(int i) { return new PanelText { name = JoinStatePrefix + i, x = RowX(i) + 165f, top = RowTop(i),       w = 130f, h = 22f, pt = 15f, min = 10f, align = TextAlignmentOptions.Right, color = TextGold }; }
        public static PanelText RowNote(int i)  { return new PanelText { name = JoinNotePrefix + i,  x = RowX(i),        top = RowTop(i) + RowNoteOffset, w = 460f, h = RowNoteHeight, pt = 13f, min = 9f, wrap = true, color = TextSub }; }
        public static Vector2 RowButtonPos(int i) { return new Vector2(RowX(i), BoardSize.y * .5f - (RowTop(i) + RowButtonOffset) - JoinRowSize.y * .5f); }
        public const float StartButtonTop = 824f;
        public static readonly Vector2 StartButtonSize = new Vector2(440f, 64f);
        public static Vector2 StartButtonPos { get { return new Vector2(240f, BoardSize.y * .5f - StartButtonTop - StartButtonSize.y * .5f); } }
        public static readonly Vector2[] StepNumPos = { new Vector2(-452f, 124f), new Vector2(28f, 124f) };   // (中心 x, 上端) 36×40、30 pt
        public static readonly float[] RuleTops = { 184f, 250f, 808f, 960f };   // 区切りの細線 (940×2、押せない)
        public const string RulePrefix = "Rule", StepNumPrefix = "StepNum";

        static TextMeshProUGUI MakePanelText(Transform parent, PanelText spec, TMP_FontAsset font, string text)
        {
            var t = MakeText(parent, spec.name, spec.Pos, spec.Size, spec.pt, font, text);
            t.fontSharedMaterial = font.material;
            t.alignment = spec.align; t.color = spec.color; t.enableWordWrapping = spec.wrap; t.richText = true;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.enableAutoSizing = spec.min > 0f; t.fontSizeMax = spec.pt; t.fontSizeMin = spec.min > 0f ? spec.min : spec.pt;
            EditorUtility.SetDirty(t);
            return t;
        }

        /// <summary>板と同じ大きさの入れ物 (Graphic なし。中の文字の座標は板と同じ)。</summary>
        static GameObject PanelGroup(Transform board, string name)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(board, name, Vector2.zero, BoardSize);
            go.SetActive(true);
            return go;
        }

        /// <summary>パネル 1 枚 (乗り場: spot = s, cart = -1 / カート上: spot = -1, cart = index)。中身は Board (= content)。カート上は最初は隠す (Parked のとき Runtime が出す)。
        /// DES-UI-007: 構成は承認済み HTML B 案 (docs/plans/auto-walk-cart/join-guidance-mock.html)。レイアウトの表は DES-UI-007 の「2026-10-14 Unity 反映」。</summary>
        static KH_CartStationPanel BuildPanel(Transform parent, string name, Vector3 pos, Quaternion rot, int s, int cartIndex, KH_CartManager manager, KD_Language language, TMP_FontAsset font, StringBuilder log, bool visible)
        {
            var go = EnsureChild(parent, name);
            go.layer = 0; go.SetActive(true);
            go.transform.SetPositionAndRotation(pos, rot); go.transform.localScale = Vector3.one;
            var panel = EnsureU<KH_CartStationPanel>(go);
            var backing = Backing(panel, Networking.SyncType.None);
            // Board: KH_TravelNotice (Course/85) と同じ WorldSpace Canvas (scale 0.001、VRCUiShape、BoxCollider trigger、Layer Default)。下端を基点の 0.5 m 下に保つため BoardLift だけ上げる
            var boardT = go.transform.Find(BoardName);
            var board = boardT != null ? boardT.gameObject : new GameObject(BoardName, typeof(RectTransform));
            board.transform.SetParent(go.transform, false); board.layer = 0; board.SetActive(true);
            var brt = (RectTransform)board.transform; brt.sizeDelta = BoardSize; brt.localPosition = new Vector3(0f, BoardLift, 0f); brt.localRotation = Quaternion.identity; brt.localScale = Vector3.one * PanelScale;
            var canvas = board.GetComponent<Canvas>(); if (canvas == null) canvas = board.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            if (board.GetComponent<GraphicRaycaster>() == null) board.AddComponent<GraphicRaycaster>();
            if (board.GetComponent<VRCUiShape>() == null) board.AddComponent<VRCUiShape>();
            var col = board.GetComponent<BoxCollider>(); if (col == null) col = board.AddComponent<BoxCollider>(); col.center = Vector3.zero; col.size = new Vector3(BoardSize.x, BoardSize.y, 2f); col.isTrigger = true;
            var bg = board.GetComponent<Image>(); if (bg == null) bg = board.AddComponent<Image>(); bg.color = new Color(.14f, .12f, .11f, .96f); bg.raycastTarget = true;
            KD_UiSpriteEditor.ApplyRoundSkin(bg, KD_UiSpriteEditor.LoadRound());

            // ---- 上部: 題・説明・手順。固定の文言は GuideTexts (Empty / Camera の入れ物の中のものを含む) ----
            var title = MakePanelText(board.transform, TitleText, font, Ja("cart.title"));
            var emptyGo = PanelGroup(board.transform, EmptyName);
            var cameraGo = PanelGroup(board.transform, CameraName);
            var guide = new TextMeshProUGUI[GuideTexts.Length];
            for (int k = 0; k < GuideTexts.Length; k++)
            {
                var spec = GuideTexts[k];
                var host = spec.parent == EmptyName ? emptyGo.transform : spec.parent == CameraName ? cameraGo.transform : board.transform;
                guide[k] = MakePanelText(host, spec, font, Ja(spec.key));
            }
            for (int k = 0; k < StepNumPos.Length; k++)
            {
                var num = MakeText(board.transform, StepNumPrefix + (k + 1), new Vector2(StepNumPos[k].x, BoardSize.y * .5f - StepNumPos[k].y - 20f), new Vector2(36f, 40f), 30f, font, (k + 1).ToString());
                num.color = TextGold; num.fontSharedMaterial = font.material; EditorUtility.SetDirty(num);
            }
            for (int k = 0; k < RuleTops.Length; k++)
            {
                var rule = KD_ShopSetupEditor.EnsureUiChild(board.transform, RulePrefix + k, new Vector2(0f, BoardSize.y * .5f - RuleTops[k] - 1f), new Vector2(940f, 2f));
                var img = rule.GetComponent<Image>(); if (img == null) img = rule.AddComponent<Image>();
                img.sprite = null; img.color = new Color(1f, 1f, 1f, .16f); img.raycastTarget = false; EditorUtility.SetDirty(img);
            }
            // ---- 空きカート・受付の数・自分の目的地 (Runtime が書く。Build は ja の見本) ----
            var avail = MakePanelText(board.transform, AvailText, font, Ja("cart.board.avail.yes"));
            var count = MakePanelText(board.transform, CountText, font, "");
            var dest = MakePanelText(board.transform, DestText, font, Ja("cart.board.start.dest").Replace("{0}", Ja("course.walk.forward")));

            // ---- 新しく出発する (右列: ボタンとその下の結果。人数で動かない) ----
            var start = KD_ShopSetupEditor.EnsureButton(board.transform, StartName, StartButtonPos, StartButtonSize, KD_UiSpriteEditor.ActionNeutral * 1.2f, font, Ja("cart.board.start.button"), backing, "_PressStart", log);
            var startLabel = start.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            startLabel.fontSize = 26f; startLabel.enableAutoSizing = true; startLabel.fontSizeMin = 14f; startLabel.fontSizeMax = 26f;
            start.SetActive(true);
            var result = MakePanelText(board.transform, ResultText, font, "");

            // ---- 合流の行 (2 列 × 4 段の固定枠。ボタンの外 (Board 直下) に 目的地・人数 / 状態 / 理由か結果) ----
            var nameFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KH_CourseTravelNameFontEditor.FontPath);
            var rows = new GameObject[MaxGroups]; var rowLabels = new TextMeshProUGUI[MaxGroups]; var rowInfos = new TextMeshProUGUI[MaxGroups];
            var rowMetas = new TextMeshProUGUI[MaxGroups]; var rowStates = new TextMeshProUGUI[MaxGroups]; var rowNotes = new TextMeshProUGUI[MaxGroups];
            for (int i = 0; i < MaxGroups; i++)
            {
                // ボタン = 表示名 (Label、表示名の補助フォント、幅固定 + 省略記号、右寄せで「さんと」に付ける) と「さんと / 一緒に走る →」(Info、共通フォント) の 2 本。
                // 行き先名・操作文言はハングル・キリル文字を含むので表示名フォント (KH_DisplayName SDF) に入れない (Codex レビュー 2026-10-11)
                var row = KD_ShopSetupEditor.EnsureButton(board.transform, JoinPrefix + i, RowButtonPos(i), JoinRowSize, KD_UiSpriteEditor.ActionNeutral, font,
                    "VRChatUser12345", backing, "_PressJoin" + i, log);
                // 旧構成 (単一ラベル) や想定外の子が残らないよう、Label / Info 以外の子を消す
                for (int c = row.transform.childCount - 1; c >= 0; c--)
                {
                    var child = row.transform.GetChild(c);
                    if (child.name != "Label" && child.name != JoinInfoName) UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
                var label = row.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                var lrt = label.rectTransform; lrt.anchoredPosition = JoinNamePos; lrt.sizeDelta = JoinNameSize;
                label.fontSize = JoinNamePoints; label.alignment = TextAlignmentOptions.Right; label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Ellipsis; label.enableAutoSizing = false;
                if (nameFont != null) { label.font = nameFont; label.fontSharedMaterial = nameFont.material; }
                else { label.font = font; label.fontSharedMaterial = font.material; }
                EditorUtility.SetDirty(label);
                var info = MakeText(row.transform, JoinInfoName, JoinInfoPos, JoinInfoSize, 17f, font, Ja("cart.board.join.suffix") + "\n" + Ja("cart.board.join.action"));
                info.fontSharedMaterial = font.material; info.alignment = TextAlignmentOptions.Left; info.overflowMode = TextOverflowModes.Ellipsis; info.enableWordWrapping = false;
                info.enableAutoSizing = true; info.fontSizeMin = 11f; info.fontSizeMax = 17f;
                EditorUtility.SetDirty(info);
                row.SetActive(false);
                rows[i] = row; rowLabels[i] = label; rowInfos[i] = info;
                rowMetas[i] = MakePanelText(board.transform, RowMeta(i), font, "");
                rowStates[i] = MakePanelText(board.transform, RowState(i), font, "");
                rowNotes[i] = MakePanelText(board.transform, RowNote(i), font, "");
            }
            if (s == 0 && cartIndex < 0 && nameFont == null) log.AppendLine("WARN display-name font missing (" + KH_CourseTravelNameFontEditor.FontPath + "); join rows use the shared font");

            // ---- 下部: 撮影カメラ (PC)・別々に出発した後 (左列)、注意書き → 写真 → PR (右列) ----
            var notice = MakePanelText(board.transform, NoticeText, font, Ja(cartIndex >= 0 ? "cart.parked" : "cart.notice"));   // M3: カート上は cart.parked
            var photoGo = KD_ShopSetupEditor.EnsureUiChild(board.transform, StationPhotoName, StationPhotoPos, StationPhotoSize);
            foreach (var g in photoGo.GetComponents<Graphic>()) if (!(g is RawImage)) UnityEngine.Object.DestroyImmediate(g);
            var photoImg = photoGo.GetComponent<RawImage>(); if (photoImg == null) photoImg = photoGo.AddComponent<RawImage>();
            photoImg.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SignOutPath); photoImg.raycastTarget = false; photoImg.color = Color.white; EditorUtility.SetDirty(photoImg);
            var pr = MakePanelText(board.transform, PrText, font, Ja("cart.station.pr"));
            foreach (var t in new TMP_Text[] { title, notice, startLabel, result }) EditorUtility.SetDirty(t);

            var so = new SerializedObject(panel);
            SetRef(so, "manager", manager); SetRef(so, "language", language); SetInt(so, "spot", s);
            var m3Log = new List<string>();
            var cartProp = so.FindProperty("cart");
            if (cartProp != null && cartProp.propertyType == SerializedPropertyType.Integer) cartProp.intValue = cartIndex; else m3Log.Add("WARN KH_CartStationPanel.cart not found (Runtime M3)");
            SetRefIfPresent(so, "content", board, m3Log);
            if ((s == 0 || cartIndex == 0)) foreach (var line in m3Log) log.AppendLine(name + ": " + line);
            SetRef(so, "titleLabel", title); SetRef(so, "noticeLabel", notice); SetRef(so, "startLabel", startLabel); SetRef(so, "resultLabel", result);
            var prLog = new List<string>();
            SetRefIfPresent(so, "prLabel", pr, prLog); SetRefIfPresent(so, "photo", photoImg, prLog);
            if (s == 0 || cartIndex == 0) foreach (var line in prLog) log.AppendLine(line);
            SetRef(so, "startButton", start); SetRefs(so, "joinButtons", rows); SetRefs(so, "joinLabels", rowLabels); SetRefs(so, "joinInfoLabels", rowInfos);
            // DES-UI-007: 案内の文字・空きカート・受付の数・自分の目的地・0 件の案内・撮影カメラの案内・行ごとの 目的地 / 状態 / 理由か結果
            SetRefs(so, "guideLabels", guide); SetStrings(so, "guideKeys", GuideTexts.Select(x => x.key).ToArray());
            SetRef(so, "availLabel", avail); SetRef(so, "joinCountLabel", count); SetRef(so, "startDestLabel", dest);
            SetRef(so, "emptyState", emptyGo); SetRef(so, "cameraTip", cameraGo);
            SetRefs(so, "joinMetaLabels", rowMetas); SetRefs(so, "joinStateLabels", rowStates); SetRefs(so, "joinNoteLabels", rowNotes);
            so.ApplyModifiedPropertiesWithoutUndo();
            UdonSharpEditorUtility.CopyProxyToUdon(panel); EditorUtility.SetDirty(panel);
            board.SetActive(visible);
            return panel;
        }

        static void SetStrings(SerializedObject so, string name, string[] values)
        {
            var p = Prop(so, name);
            if (!p.isArray) throw new InvalidOperationException(so.targetObject.GetType().Name + "." + name + " is not an array");
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).stringValue = values[i];
        }

        // ============================================================
        // 資産: import 設定と材質
        // ============================================================

        static void ConfigureImporters(List<string> log)
        {
            foreach (var path in new[] { BaseColorPath, SignTexturePath })
            {
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) { log.Add("PENDING texture missing: " + path); continue; }
                bool changed = false;
                if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; changed = true; }
                if (!ti.sRGBTexture) { ti.sRGBTexture = true; changed = true; }
                if (ti.isReadable) { ti.isReadable = false; changed = true; }
                foreach (var platform in new[] { "Android", "iPhone" })
                {
                    var ps = ti.GetPlatformTextureSettings(platform);
                    if (!ps.overridden || ps.format != TextureImporterFormat.ASTC_6x6) { ps.overridden = true; ps.format = TextureImporterFormat.ASTC_6x6; ti.SetPlatformTextureSettings(ps); changed = true; }
                }
                if (changed) { ti.SaveAndReimport(); log.Add("texture import set: " + path + " (sRGB, Read/Write off, Android/iPhone ASTC 6x6)"); }
            }
            var mi = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (mi == null) { log.Add("PENDING FBX missing: " + FbxPath + " (placeholder model)"); return; }
            var before = ModelImportSummary(mi);
            bool mchanged = false;
            // 制作側の実測で最小 UV 三角形が 0.049 px² のため Mesh Compression は Off (2026-10-11 統括指示)
            if (!Mathf.Approximately(mi.globalScale, 1f)) { mi.globalScale = 1f; mchanged = true; }
            if (!mi.useFileScale) { mi.useFileScale = true; mchanged = true; }
            if (mi.importNormals != ModelImporterNormals.Import) { mi.importNormals = ModelImporterNormals.Import; mchanged = true; }
            if (mi.meshCompression != ModelImporterMeshCompression.Off) { mi.meshCompression = ModelImporterMeshCompression.Off; mchanged = true; }
            if (mi.generateSecondaryUV) { mi.generateSecondaryUV = false; mchanged = true; }
            if (mi.addCollider) { mi.addCollider = false; mchanged = true; }
            if (mi.isReadable) { mi.isReadable = false; mchanged = true; }
            if (mi.animationType != ModelImporterAnimationType.None) { mi.animationType = ModelImporterAnimationType.None; mchanged = true; }
            if (mi.importBlendShapes) { mi.importBlendShapes = false; mchanged = true; }
            if (mchanged) { mi.SaveAndReimport(); log.Add("model import set: " + FbxPath + " [" + before + "] -> [" + ModelImportSummary(mi) + "]"); }
        }

        /// <summary>Cart/1 が設定し Cart/2 が検査する ModelImporter の値 (統括指示 2026-10-11)。</summary>
        public static bool ModelImportOk(ModelImporter mi)
        {
            return Mathf.Approximately(mi.globalScale, 1f) && mi.useFileScale && mi.importNormals == ModelImporterNormals.Import && mi.meshCompression == ModelImporterMeshCompression.Off
                && !mi.generateSecondaryUV && !mi.addCollider && !mi.isReadable && mi.animationType == ModelImporterAnimationType.None && !mi.importBlendShapes;
        }

        public static string ModelImportSummary(ModelImporter mi)
        {
            return "scale " + mi.globalScale + ", useFileScale " + mi.useFileScale + ", normals " + mi.importNormals + ", compression " + mi.meshCompression + ", lightmapUV " + mi.generateSecondaryUV
                + ", colliders " + mi.addCollider + ", readWrite " + mi.isReadable + ", rig " + mi.animationType + ", blendShapes " + mi.importBlendShapes;
        }

        static Material EnsureMaterial(string path, string texturePath, List<string> log)
        {
            EnsureFolder(MaterialDir);
            var shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Standard shader not found");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) }; AssetDatabase.CreateAsset(mat, path); log.Add("created " + path); }
            if (mat.shader != shader) mat.shader = shader;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (tex == null) log.Add("PENDING " + Path.GetFileName(path) + ": texture " + texturePath + " not found (white)");
            mat.SetTexture("_MainTex", tex);
            mat.SetColor("_Color", Color.white);
            mat.SetFloat("_Glossiness", Smoothness);
            mat.SetFloat("_Metallic", Metallic);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // ============================================================
        // 共通
        // ============================================================

        static T Single<T>(string what) where T : UnityEngine.Object
        {
            var all = UnityEngine.Object.FindObjectsOfType<T>(true);
            if (all.Length != 1) throw new InvalidOperationException("Exactly one " + what + " required (found " + all.Length + ")");
            return all[0];
        }

        static GameObject EnsureChild(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null) return t.gameObject;
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            return go;
        }

        static void Reset(Transform t) { t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one; t.gameObject.SetActive(true); }

        static T EnsureU<T>(GameObject go) where T : UdonSharpBehaviour
        {
            var c = go.GetComponent<T>();
            if (c == null) c = go.AddUdonSharpComponent<T>();
            return c;
        }

        /// <summary>backing UdonBehaviour を実物で同期方式に固定する (vrc-udonsharp §2。属性だけでは焼き付いた値を直せない)。</summary>
        static UdonBehaviour Backing(UdonSharpBehaviour b, Networking.SyncType sync)
        {
            var u = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            if (u == null) throw new InvalidOperationException(b.GetType().Name + " backing missing (compile U# and run again)");
            if (u.SyncMethod != sync) { u.SyncMethod = sync; EditorUtility.SetDirty(u); }
            return u;
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++) { var r = FindDeep(root.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        static SerializedProperty Prop(SerializedObject so, string name)
        {
            var p = so.FindProperty(name);
            if (p == null) throw new InvalidOperationException("Contract field missing: " + so.targetObject.GetType().Name + "." + name + " (implementation.md; compile Runtime first)");
            return p;
        }

        static void SetRef(SerializedObject so, string name, UnityEngine.Object value)
        {
            var p = Prop(so, name);
            if (p.propertyType != SerializedPropertyType.ObjectReference) throw new InvalidOperationException(so.targetObject.GetType().Name + "." + name + " is not an object reference");
            p.objectReferenceValue = value;
            if (value != null && p.objectReferenceValue != value) throw new InvalidOperationException("Type mismatch writing " + so.targetObject.GetType().Name + "." + name + " = " + value.GetType().Name);
        }

        static void SetRefs(SerializedObject so, string name, UnityEngine.Object[] values)
        {
            var p = Prop(so, name);
            if (!p.isArray) throw new InvalidOperationException(so.targetObject.GetType().Name + "." + name + " is not an array");
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                var e = p.GetArrayElementAtIndex(i);
                e.objectReferenceValue = values[i];
                if (values[i] != null && e.objectReferenceValue != values[i]) throw new InvalidOperationException("Type mismatch writing " + so.targetObject.GetType().Name + "." + name + "[" + i + "]");
            }
        }

        static void SetInt(SerializedObject so, string name, int value)
        {
            var p = Prop(so, name);
            if (p.propertyType != SerializedPropertyType.Integer) throw new InvalidOperationException(so.targetObject.GetType().Name + "." + name + " is not an int");
            p.intValue = value;
        }

        static KD_LocalizationEditor.Catalog CourseCatalog() { return JsonUtility.FromJson<KD_LocalizationEditor.Catalog>(File.ReadAllText(KH_CourseMenuEditor.CatalogPath)); }

        static string Ja(string key) { return KD_LocalizationEditor.Text(CourseCatalog(), "ja", key); }

        static TextMeshProUGUI MakeText(Transform parent, string name, Vector2 pos, Vector2 size, float points, TMP_FontAsset font, string text)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var t = go.GetComponent<TextMeshProUGUI>(); if (t == null) t = go.AddComponent<TextMeshProUGUI>();
            t.font = font; t.fontSize = points; t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false; t.enableAutoSizing = false; t.raycastTarget = false;
            t.color = Color.white; t.text = text; EditorUtility.SetDirty(t);
            return t;
        }
    }
}
