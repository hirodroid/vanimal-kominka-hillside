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
    /// <summary>DES-WLD-004 おさんぽカート M1 の Setup (契約: docs/plans/auto-walk-cart/implementation.md「Editor」節)。
    ///
    /// Cart/1 は Course/85 (KH_CourseTravelJoinEditor.Build) の流儀で次を行い、本番シーンを保存する:
    /// 1. KH_CartManager / KH_CartSeat / KH_CartStopButton / KH_CartStationPanel の Program Asset が無ければ作ってコンパイルを始め、**シーンは変えずに止まる** (もう一度 Cart/1 を流す)。
    /// 2. ルート KH_Cart (冪等。子は名前で探して無ければ作る): CartManager (Manual)、Parking (0, -50, 0)、Carts/Cart_0..7、Stations/Station_0..4。
    /// 3. カート: 子 Model は毎回作り直す (FBX があれば InstantiatePrefab → Unpack、無ければ仮の Cube とアンカー)。本体に VRCStation + KH_CartSeat + BoxCollider (layer Ignore Raycast)、
    ///    子 StopL / StopR に BoxCollider + KH_CartStopButton (kind 0 / 1)。材質は Materials/Cart に Standard で作る。
    /// 4. 乗り場パネル: KH_LeashManager.spotPositions[N] から spotYaws[N] の右手へ 2.5 m、地面 + 1.2 m。Canvas は KH_TravelNotice の Board と同じ構成。
    /// 5. 配線 (SerializedObject → CopyProxyToUdon)、KH_LeashManager.cartManager、翻訳表 (cart.*) を KH_CourseTravelEditor.ApplyCatalog で KD_Language へ。
    /// 6. NetworkIDs の残骸除去 → 保存。グリフ焼き (Course/27) は統括が実行する。
    /// 検査は Cart/2 (<see cref="Verify"/>)、純関数のプローブは Cart/3 (<see cref="Probe"/>)。</summary>
    public static partial class KH_CartSetupEditor
    {
        public const string OutDir = "Temp/cart-20261011";
        public const string ProgramDir = "Assets/KominkaHillside/Scripts/Cart";
        public static readonly string[] ProgramNames = { "KH_CartManager", "KH_CartSeat", "KH_CartStopButton", "KH_CartStationPanel", "KH_CartDock", "KH_CartControls" };
        public const string ModelDir = "Assets/KominkaHillside/Models/Cart";
        public const string FbxPath = ModelDir + "/KH_OsanpoCart.fbx";
        public const string BaseColorPath = ModelDir + "/KH_OsanpoCart_BaseColor.png", SignTexturePath = ModelDir + "/KH_OsanpoCart_Sign.png";
        public const string MaterialDir = "Assets/KominkaHillside/Materials/Cart";
        public const string BodyMaterialPath = MaterialDir + "/KH_OsanpoCart_Body.mat", SignMaterialPath = MaterialDir + "/KH_OsanpoCart_Sign.mat";
        public const string RootName = "KH_Cart", ManagerName = "CartManager", CartsName = "Carts", StationsName = "Stations", ParkingName = "Parking", ModelName = "Model", BoardName = "Board";
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
            new Vector3(-0.36f, 0.76f, 0.30f),// StopL (肘掛け前端)
            new Vector3(0.36f, 0.76f, 0.30f), // StopR
            new Vector3(0f, 0.75f, -0.62f) }; // SignBack
        public static readonly Vector3 PlaceholderBodySize = new Vector3(0.82f, 0.6f, 1.2f);
        static readonly Vector3 PlaceholderAxleFront = new Vector3(0f, 0.14f, 0.42f), PlaceholderAxleRear = new Vector3(0f, 0.14f, -0.42f);
        public static readonly Vector3 BodyColliderCenter = new Vector3(0f, 0.3f, 0f), BodyColliderSize = new Vector3(0.82f, 0.6f, 1.2f);
        public static readonly Vector3 StopColliderSize = new Vector3(0.09f, 0.03f, 0.09f);
        /// <summary>本体の層 = Ignore Raycast。接地 Raycast (KH_LeashManager.TravelGroundMask = 0|11) が自分の車体に当たらないように、
        /// また VRCStation のクリック着座を避ける (vrc-world-ui §2。DisableInteractive はシリアライズされないので Editor からは止められない)。</summary>
        public const int BodyLayer = 2, ButtonLayer = 0;
        public const VRCStation.Mobility StationMobility = VRCStation.Mobility.Immobilize;
        public const float PanelSide = 2.5f, PanelHeight = 1.2f, PanelScale = 0.001f;
        public static readonly Vector2 BoardSize = new Vector2(760f, 800f);
        const float RowPitch = 58f, FirstRowY = 90f;
        // 合流行 700×52: 内側 -338..338 を 表示名 -338..-78 (幅 260) と 行き先・人数 -66..338 (幅 404) に分ける
        public const string JoinInfoName = "Info";
        public static readonly Vector2 JoinRowSize = new Vector2(700f, 52f);
        public static readonly Vector2 JoinNamePos = new Vector2(-208f, 0f), JoinNameSize = new Vector2(260f, 48f);
        public static readonly Vector2 JoinInfoPos = new Vector2(136f, 0f), JoinInfoSize = new Vector2(404f, 48f);
        public const float Smoothness = 0.25f, Metallic = 0f;
        public static readonly string[] CartKeys = {
            "cart.title", "cart.start", "cart.join", "cart.notice", "cart.stop", "cart.accepting", "cart.accepting.on", "cart.accepting.off",
            "cart.result.full", "cart.result.closed", "cart.result.here", "cart.result.busy", "cart.result.started", "cart.result.joined", "cart.result.left",
            "cart.menu", "cart.monitor", "cart.seat.up", "cart.seat.down", "cart.turn.left", "cart.turn.right" };   // M1.1

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
            var signMat = EnsureMaterial(SignMaterialPath, SignTexturePath, report.log);
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
                var panels = new KH_CartStationPanel[StationCount];
                for (int s = 0; s < StationCount; s++) panels[s] = BuildStation(stationsRoot, s, manager, language, leash, font, ui, report);

                // ---- Manager の配線 ----
                var so = new SerializedObject(manager);
                SetRef(so, "route", route); SetRef(so, "leash", leash); SetRef(so, "language", language); SetRef(so, "fade", fade); SetRef(so, "parking", parking);
                SetRefs(so, "carts", seats); SetRefs(so, "panels", panels);
                so.ApplyModifiedPropertiesWithoutUndo();
                WriteTrack(manager, track);
                UdonSharpEditorUtility.CopyProxyToUdon(manager); EditorUtility.SetDirty(manager);

                // ---- KH_LeashManager.cartManager ----
                var lso = new SerializedObject(leash);
                SetRef(lso, "cartManager", manager);
                lso.ApplyModifiedPropertiesWithoutUndo();
                UdonSharpEditorUtility.CopyProxyToUdon(leash); EditorUtility.SetDirty(leash);

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
                    new LOD(Lod1Height, new[] { R(BodyLodMesh) }) });
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

            // 停止 / 受付切替 (肘掛け前端)
            var stops = new KH_CartStopButton[2];
            for (int k = 0; k < 2; k++)
            {
                var anchor = A(k == 0 ? AnchorStopL : AnchorStopR);
                var sgo = EnsureChild(go.transform, k == 0 ? StopLName : StopRName);
                sgo.layer = ButtonLayer; sgo.SetActive(true);
                sgo.transform.localPosition = go.transform.InverseTransformPoint(anchor.position);
                sgo.transform.localRotation = Quaternion.Inverse(go.transform.rotation) * anchor.rotation; sgo.transform.localScale = Vector3.one;
                var bc = sgo.GetComponent<BoxCollider>(); if (bc == null) bc = sgo.AddComponent<BoxCollider>();
                bc.center = Vector3.zero; bc.size = StopColliderSize; bc.isTrigger = false; EditorUtility.SetDirty(bc);
                var stop = EnsureU<KH_CartStopButton>(sgo);
                Backing(stop, Networking.SyncType.None);
                var sso = new SerializedObject(stop);
                SetRef(sso, "manager", manager); SetRef(sso, "language", language); SetInt(sso, "kind", 0);   // M1.1: 左右とも停止 (受付切替は操作帯だけ)
                sso.ApplyModifiedPropertiesWithoutUndo();
                UdonSharpEditorUtility.CopyProxyToUdon(stop); EditorUtility.SetDirty(stop);
                stops[k] = stop;
            }

            var so = new SerializedObject(seat);
            SetRef(so, "manager", manager); SetInt(so, "index", i); SetRef(so, "station", station);
            SetRef(so, "seat", seatT); SetRef(so, "exitL", exitL); SetRef(so, "exitR", exitR);
            SetRef(so, "stopL", stops[0].transform); SetRef(so, "stopR", stops[1].transform);
            SetRef(so, "signBack", signBack); SetRef(so, "axleFront", axleFront); SetRef(so, "axleRear", axleRear);
            SetRefs(so, "renderers", renderers);
            SetRef(so, "stopButton", stops[0]);
            if (so.FindProperty("acceptButton") != null) SetRef(so, "acceptButton", stops[1]);   // M1.1 で Runtime が外していれば書かない
            BuildCartUi(go, i, seatT, manager, language, menu, video, photo, font, ui, out KH_CartDock dock, out KH_CartControls controls);
            SetRef(so, "controls", controls); SetRef(so, "dock", dock);
            so.ApplyModifiedPropertiesWithoutUndo();
            UdonSharpEditorUtility.CopyProxyToUdon(seat); EditorUtility.SetDirty(seat);
            return seat;
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

        static KH_CartStationPanel BuildStation(Transform stationsRoot, int s, KH_CartManager manager, KD_Language language, KH_LeashManager leash, TMP_FontAsset font, StringBuilder log, BuildReport report)
        {
            Vector3 spot = leash.spotPositions[s];
            PanelPose(spot, leash.spotYaws[s], out Vector3 pos, out Quaternion rot, out float groundDelta, out bool hit);
            report.stations.Add("Station_" + s + " spot=" + spot.ToString("F2") + " yaw=" + leash.spotYaws[s].ToString("F1") + " panel=" + pos.ToString("F2") + " ground=" + (hit ? "hit dy=" + groundDelta.ToString("F2") : "MISS (spot height used)"));

            var go = EnsureChild(stationsRoot, "Station_" + s);
            go.layer = 0; go.SetActive(true);
            go.transform.SetPositionAndRotation(pos, rot); go.transform.localScale = Vector3.one;
            var panel = EnsureU<KH_CartStationPanel>(go);
            var backing = Backing(panel, Networking.SyncType.None);

            // Board: KH_TravelNotice (Course/85) と同じ WorldSpace Canvas (scale 0.001、VRCUiShape、BoxCollider trigger、Layer Default)
            var boardT = go.transform.Find(BoardName);
            var board = boardT != null ? boardT.gameObject : new GameObject(BoardName, typeof(RectTransform));
            board.transform.SetParent(go.transform, false); board.layer = 0; board.SetActive(true);
            var brt = (RectTransform)board.transform; brt.sizeDelta = BoardSize; brt.localPosition = Vector3.zero; brt.localRotation = Quaternion.identity; brt.localScale = Vector3.one * PanelScale;
            var canvas = board.GetComponent<Canvas>(); if (canvas == null) canvas = board.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            if (board.GetComponent<GraphicRaycaster>() == null) board.AddComponent<GraphicRaycaster>();
            if (board.GetComponent<VRCUiShape>() == null) board.AddComponent<VRCUiShape>();
            var col = board.GetComponent<BoxCollider>(); if (col == null) col = board.AddComponent<BoxCollider>(); col.center = Vector3.zero; col.size = new Vector3(BoardSize.x, BoardSize.y, 2f); col.isTrigger = true;
            var bg = board.GetComponent<Image>(); if (bg == null) bg = board.AddComponent<Image>(); bg.color = new Color(.14f, .12f, .11f, .96f); bg.raycastTarget = true;
            KD_UiSpriteEditor.ApplyRoundSkin(bg, KD_UiSpriteEditor.LoadRound());

            var title = MakeText(board.transform, "Title", new Vector2(0f, 355f), new Vector2(700f, 56f), 32f, font, Ja("cart.title"));
            var notice = MakeText(board.transform, "Notice", new Vector2(0f, 290f), new Vector2(700f, 70f), 20f, font, Ja("cart.notice"));
            notice.enableWordWrapping = true; notice.enableAutoSizing = true; notice.fontSizeMin = 14f; notice.fontSizeMax = 20f; notice.color = new Color(1f, .93f, .80f);
            var start = KD_ShopSetupEditor.EnsureButton(board.transform, StartName, new Vector2(0f, 205f), new Vector2(520f, 80f), KD_UiSpriteEditor.ActionNeutral * 1.2f, font, Ja("cart.start"), backing, "_PressStart", log);
            var startLabel = start.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            startLabel.fontSize = 26f; startLabel.enableAutoSizing = true; startLabel.fontSizeMin = 14f; startLabel.fontSizeMax = 26f;
            start.SetActive(true);
            var result = MakeText(board.transform, "Result", new Vector2(0f, 143f), new Vector2(700f, 36f), 20f, font, "");
            result.enableAutoSizing = true; result.fontSizeMin = 12f; result.fontSizeMax = 20f; result.color = new Color(1f, .82f, .55f);

            var nameFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KH_CourseTravelNameFontEditor.FontPath);
            var rows = new GameObject[MaxGroups]; var rowLabels = new TextMeshProUGUI[MaxGroups]; var rowInfos = new TextMeshProUGUI[MaxGroups];
            for (int i = 0; i < MaxGroups; i++)
            {
                // 1 行 = 表示名 (Label、表示名の補助フォント、幅固定 + 省略記号) と 行き先・人数 (Info、共通フォント) の 2 本。
                // 行き先名はハングル・キリル文字を含むので表示名フォント (KH_DisplayName SDF) に入れない (Codex レビュー 2026-10-11)
                var row = KD_ShopSetupEditor.EnsureButton(board.transform, JoinPrefix + i, new Vector2(0f, FirstRowY - i * RowPitch), JoinRowSize, KD_UiSpriteEditor.ActionNeutral, font,
                    "VRChatUser12345", backing, "_PressJoin" + i, log);
                // 旧構成 (単一ラベル) や想定外の子が残らないよう、Label / Info 以外の子を消す
                for (int c = row.transform.childCount - 1; c >= 0; c--)
                {
                    var child = row.transform.GetChild(c);
                    if (child.name != "Label" && child.name != JoinInfoName) UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
                var label = row.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                var lrt = label.rectTransform; lrt.anchoredPosition = JoinNamePos; lrt.sizeDelta = JoinNameSize;
                label.fontSize = 20f; label.alignment = TextAlignmentOptions.Left; label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Ellipsis; label.enableAutoSizing = false;
                if (nameFont != null) { label.font = nameFont; label.fontSharedMaterial = nameFont.material; }
                else { label.font = font; label.fontSharedMaterial = font.material; }
                EditorUtility.SetDirty(label);
                var info = MakeText(row.transform, JoinInfoName, JoinInfoPos, JoinInfoSize, 20f, font, Ja("course.walk.upper") + " · 2");
                info.fontSharedMaterial = font.material; info.alignment = TextAlignmentOptions.Left; info.overflowMode = TextOverflowModes.Ellipsis;
                info.enableAutoSizing = true; info.fontSizeMin = 14f; info.fontSizeMax = 20f;
                EditorUtility.SetDirty(info);
                row.SetActive(false);
                rows[i] = row; rowLabels[i] = label; rowInfos[i] = info;
            }
            if (s == 0 && nameFont == null) log.AppendLine("WARN display-name font missing (" + KH_CourseTravelNameFontEditor.FontPath + "); join rows use the shared font");
            foreach (var t in new TMP_Text[] { title, notice, startLabel, result }) EditorUtility.SetDirty(t);

            var so = new SerializedObject(panel);
            SetRef(so, "manager", manager); SetRef(so, "language", language); SetInt(so, "spot", s);
            SetRef(so, "titleLabel", title); SetRef(so, "noticeLabel", notice); SetRef(so, "startLabel", startLabel); SetRef(so, "resultLabel", result);
            SetRef(so, "startButton", start); SetRefs(so, "joinButtons", rows); SetRefs(so, "joinLabels", rowLabels); SetRefs(so, "joinInfoLabels", rowInfos);
            so.ApplyModifiedPropertiesWithoutUndo();
            UdonSharpEditorUtility.CopyProxyToUdon(panel); EditorUtility.SetDirty(panel);
            return panel;
        }

        /// <summary>パネルの姿勢: spot から spotYaws の右手 (cos yaw, 0, -sin yaw) へ 2.5 m、その地点の地面 (TravelGroundMask) + 1.2 m。
        /// Canvas の表は -Z 側なので LookRotation(パネル - spot) で spot に表を向ける。</summary>
        public static void PanelPose(Vector3 spot, float yawDegrees, out Vector3 pos, out Quaternion rot, out float groundDelta, out bool hit)
        {
            float yaw = yawDegrees * Mathf.Deg2Rad;
            var right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
            var flat = spot + right * PanelSide;
            float groundY = spot.y;
            hit = Physics.Raycast(new Vector3(flat.x, spot.y + 5f, flat.z), Vector3.down, out RaycastHit h, 15f, KH_LeashManager.TravelGroundMask, QueryTriggerInteraction.Ignore);
            if (hit) groundY = h.point.y;
            groundDelta = groundY - spot.y;
            pos = new Vector3(flat.x, groundY + PanelHeight, flat.z);
            rot = Quaternion.LookRotation(right, Vector3.up);
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
