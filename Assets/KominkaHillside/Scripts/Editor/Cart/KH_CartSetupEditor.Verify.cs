using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
using VRCStation = VRC.SDK3.Components.VRCStation;

namespace KominkaHillside.Editor
{
    /// <summary>Cart/2: 保存済みのシーンを読み、契約の項目を数えて Temp/cart-20261011/verify.json に出す。シーンは変えない (SetDirty しない・文字の収まりは測らない)。
    /// NG = 契約違反。PENDING = 制作担当の FBX / PNG がまだ無いことによる未完 (仮形状のときに出る。NG には数えない)。</summary>
    public static partial class KH_CartSetupEditor
    {
        [Serializable] class VerifyReport
        {
            public string utc, scene, status;
            public int ng, pending, pass;
            public bool placeholderModel;
            public List<string> ngItems = new List<string>();
            public List<string> pendingItems = new List<string>();
            public List<string> passItems = new List<string>();
            public List<string> backingSymbolCounts = new List<string>();
            public string readingHint = "If many 'backing' NG items appear and backingSymbolCounts shows <= 1 for those behaviours, the backing heap is not populated yet: save, reopen the scene and run Cart/2 again before treating it as a wiring bug.";
            public string limitations = "Edit-mode static check of saved scene/proxy/backing values and assets only. No Play, station seating, network, VR input, label fit or glyph coverage (run Course/27, then check fit in Play). DisableInteractive is not serialized and is not checked.";
        }

        class V
        {
            public VerifyReport r;
            public void Req(bool ok, string what) { if (ok) r.passItems.Add(what); else r.ngItems.Add(what); }
            public void Pend(string what) { r.pendingItems.Add(what); }
        }

        [MenuItem("Tools/KominkaHillside/Cart/2 Verify Carts")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var report = new VerifyReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            var v = new V { r = report };
            try { Validate(v); }
            catch (Exception ex) { report.ngItems.Add("Exception: " + ex.GetType().Name + ": " + ex.Message); }
            report.ng = report.ngItems.Count; report.pending = report.pendingItems.Count; report.pass = report.passItems.Count;
            report.status = report.ng > 0 ? "NG" : report.pending > 0 ? "OK_PENDING_ASSETS" : "CART_OK";
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/verify.json", JsonUtility.ToJson(report, true));
            string summary = "[KH Cart] Verify " + report.status + " ng=" + report.ng + " pending=" + report.pending + " pass=" + report.pass + " -> " + OutDir + "/verify.json";
            if (report.ng > 0) throw new InvalidOperationException(summary + "\n" + string.Join("\n", report.ngItems.Take(40)));
            Debug.Log(summary + (report.pending > 0 ? "\nPENDING: " + string.Join("\n", report.pendingItems) : ""));
        }

        static void Validate(V v)
        {
            // ---- Program Asset ----
            for (int i = 0; i < ProgramNames.Length; i++)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramDir + "/" + ProgramNames[i] + ".asset");
                v.Req(asset != null && asset.sourceCsScript != null, "program asset " + ProgramNames[i] + " exists with source");
                if (asset != null)
                {
                    var want = i == 0 ? BehaviourSyncMode.Manual : BehaviourSyncMode.None;
                    v.Req(asset.behaviourSyncMode == want, "program asset " + ProgramNames[i] + " behaviourSyncMode=" + want + " (actual " + asset.behaviourSyncMode + ")");
                }
            }

            // ---- 件数 ----
            var p = KH_CourseTravelEditor.FindPouch();
            var leash = p.manager; var language = p.language; var route = leash.route;
            var scene = p.gameObject.scene;
            var roots = scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray();
            v.Req(roots.Length == 1, "exactly one root " + RootName + " (found " + roots.Length + ")");
            var managers = UnityEngine.Object.FindObjectsOfType<KH_CartManager>(true);
            var seats = UnityEngine.Object.FindObjectsOfType<KH_CartSeat>(true);
            var panels = UnityEngine.Object.FindObjectsOfType<KH_CartStationPanel>(true);
            var stops = UnityEngine.Object.FindObjectsOfType<KH_CartStopButton>(true);
            v.Req(managers.Length == 1, "KH_CartManager count 1 (found " + managers.Length + ")");
            v.Req(seats.Length == MaxCarts, "KH_CartSeat count " + MaxCarts + " (found " + seats.Length + ")");
            v.Req(panels.Length == StationCount, "KH_CartStationPanel count " + StationCount + " (found " + panels.Length + ")");
            v.Req(stops.Length == MaxCarts * 2, "KH_CartStopButton count " + (MaxCarts * 2) + " = 2 per cart (found " + stops.Length + ")");
            if (roots.Length != 1 || managers.Length != 1) return;
            var root = roots[0].transform; var manager = managers[0];
            var mb = SyncCheck(v, manager, Networking.SyncType.Manual, "manager");
            var parking = root.Find(ParkingName);
            v.Req(parking != null && (parking.position - ParkingPosition).sqrMagnitude < 1e-4f, "Parking at " + ParkingPosition);
            v.Req(manager.transform.parent == root && manager.name == ManagerName, "CartManager under " + RootName);

            // ---- Manager の配線 ----
            var mso = new SerializedObject(manager);
            RefCheck(v, mso, mb, "route", route, "manager");
            RefCheck(v, mso, mb, "leash", leash, "manager");
            RefCheck(v, mso, mb, "language", language, "manager");
            RefCheck(v, mso, mb, "parking", parking, "manager");
            var fade = UnityEngine.Object.FindObjectsOfType<KH_TravelFade>(true).FirstOrDefault();
            if (fade != null) RefCheck(v, mso, mb, "fade", fade, "manager"); else v.Pend("KH_TravelFade not in scene; manager.fade null (join without fade)");
            var cartsRoot = root.Find(CartsName); var stationsRoot = root.Find(StationsName);
            var expectedSeats = Enumerable.Range(0, MaxCarts).Select(i => cartsRoot != null && cartsRoot.Find("Cart_" + i) != null ? cartsRoot.Find("Cart_" + i).GetComponent<KH_CartSeat>() : null).ToArray();
            var expectedPanels = Enumerable.Range(0, StationCount).Select(s => stationsRoot != null && stationsRoot.Find("Station_" + s) != null ? stationsRoot.Find("Station_" + s).GetComponent<KH_CartStationPanel>() : null).ToArray();
            ArrayCheck(v, mso, mb, "carts", expectedSeats, "manager");
            ArrayCheck(v, mso, mb, "panels", expectedPanels, "manager");

            // ---- KH_LeashManager.cartManager ----
            var lso = new SerializedObject(leash);
            RefCheck(v, lso, UdonSharpEditorUtility.GetBackingUdonBehaviour(leash), "cartManager", manager, "leash");

            // ---- カート ----
            var bodyMat = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);
            var signMat = AssetDatabase.LoadAssetAtPath<Material>(SignMaterialPath);
            bool anyPlaceholder = false, anySignUsed = false;
            for (int i = 0; i < MaxCarts; i++)
            {
                string c = "Cart_" + i;
                var seat = expectedSeats[i];
                if (seat == null) { v.Req(false, c + " with KH_CartSeat under " + CartsName); continue; }
                var go = seat.gameObject;
                var sb = SyncCheck(v, seat, Networking.SyncType.None, c);
                v.Req(go.layer == BodyLayer, c + " layer Ignore Raycast (" + BodyLayer + ", actual " + go.layer + ")");
                v.Req(parking != null && (go.transform.position - parking.position).sqrMagnitude < 1e-4f, c + " parked at Parking");
                var col = go.GetComponent<BoxCollider>();
                v.Req(col != null && !col.isTrigger && (col.size - BodyColliderSize).sqrMagnitude < 1e-6f && (col.center - BodyColliderCenter).sqrMagnitude < 1e-6f, c + " body BoxCollider " + BodyColliderSize);
                var model = go.transform.Find(ModelName);
                v.Req(model != null, c + " has " + ModelName);
                if (model == null) continue;
                foreach (var n in AnchorNames.Concat(MeshNames)) v.Req(FindDeep(model, n) != null, c + " Model/" + n);
                v.Req(model.GetComponentsInChildren<Collider>(true).Length == 0, c + " no colliders under Model");
                var bodyT = FindDeep(model, BodyMesh);
                var mf = bodyT != null ? bodyT.GetComponent<MeshFilter>() : null;
                bool placeholder = mf != null && mf.sharedMesh != null && AssetDatabase.GetAssetPath(mf.sharedMesh) != FbxPath;
                anyPlaceholder |= placeholder;
                // FBX の姿勢: Model はルートを恒等姿勢に置くので、取り込みの回転・倍率が残っていると車体が倒れる / 縮む。寸法と接地で確かめる
                var allR = model.GetComponentsInChildren<Renderer>(true);
                if (!placeholder && allR.Length > 0)
                {
                    var wb = allR[0].bounds; foreach (var rr in allR) wb.Encapsulate(rr.bounds);   // 車輪込みの外形 (Model 全体)
                    Vector3 lmin = go.transform.InverseTransformPoint(wb.min), size = wb.size;
                    bool dims = Mathf.Abs(lmin.y) < 0.1f && Mathf.Abs(size.x - 0.82f) < 0.82f * 0.2f && Mathf.Abs(size.z - 1.2f) < 1.2f * 0.2f && Mathf.Abs(size.y - 1.05f) < 1.05f * 0.2f;
                    v.Req(dims, c + " FBX body upright and to scale: size " + size.ToString("F2") + " (expect ~0.82 x 1.05 x 1.2), bottom y " + lmin.y.ToString("F2") + " (expect 0)");
                    bool unitScale = AnchorNames.All(an => { var at = FindDeep(model, an); return at != null && (at.lossyScale - Vector3.one).sqrMagnitude < 1e-3f; });
                    v.Req(unitScale, c + " anchors have unit world scale");
                }
                if (!placeholder) LodCheck(v, model, c);

                // Station
                var st = go.GetComponent<VRCStation>();
                v.Req(st != null, c + " VRCStation");
                Transform A(string n) { return FindDeep(model, n); }
                if (st != null)
                {
                    v.Req(st.PlayerMobility == StationMobility, c + " station PlayerMobility=" + StationMobility + " (actual " + st.PlayerMobility + ")");
                    v.Req(!st.disableStationExit, c + " station disableStationExit=false (jump exits)");
                    v.Req(st.seated, c + " station seated=true");
                    v.Req(!st.canUseStationFromStation, c + " station canUseStationFromStation=false");
                    v.Req(st.stationEnterPlayerLocation != null && st.stationEnterPlayerLocation == A(AnchorSeat), c + " station enter = Anchor_Seat");
                    v.Req(st.stationExitPlayerLocation != null && (st.stationExitPlayerLocation == A(AnchorExitL) || st.stationExitPlayerLocation == A(AnchorExitR)), c + " station exit = Anchor_ExitL/R");
                }

                // 停止 / 受付切替
                var stopGo = new Transform[] { go.transform.Find(StopLName), go.transform.Find(StopRName) };
                var stopB = new KH_CartStopButton[2];
                for (int k = 0; k < 2; k++)
                {
                    string sn = c + "/" + (k == 0 ? StopLName : StopRName);
                    var t = stopGo[k];
                    stopB[k] = t != null ? t.GetComponent<KH_CartStopButton>() : null;
                    v.Req(stopB[k] != null, sn + " KH_CartStopButton");
                    if (stopB[k] == null) continue;
                    var anchor = A(k == 0 ? AnchorStopL : AnchorStopR);
                    v.Req(t.gameObject.layer == ButtonLayer, sn + " layer Default");
                    v.Req(anchor != null && (t.position - anchor.position).sqrMagnitude < 1e-6f && Quaternion.Angle(t.rotation, anchor.rotation) < 0.1f, sn + " at " + (k == 0 ? AnchorStopL : AnchorStopR) + " (position and rotation)");
                    var bc = t.GetComponent<BoxCollider>();
                    v.Req(bc != null && (bc.size - StopColliderSize).sqrMagnitude < 1e-8f, sn + " BoxCollider " + StopColliderSize);
                    var bb = SyncCheck(v, stopB[k], Networking.SyncType.None, sn);
                    var so2 = new SerializedObject(stopB[k]);
                    RefCheck(v, so2, bb, "manager", manager, sn);
                    RefCheck(v, so2, bb, "language", language, sn);
                    IntCheck(v, so2, bb, "kind", 0, sn);   // M1.1: 左右とも停止
                }

                // seat の配線
                var so = new SerializedObject(seat);
                RefCheck(v, so, sb, "manager", manager, c);
                IntCheck(v, so, sb, "index", i, c);
                RefCheck(v, so, sb, "station", st, c);
                RefCheck(v, so, sb, "seat", A(AnchorSeat), c);
                RefCheck(v, so, sb, "exitL", A(AnchorExitL), c);
                RefCheck(v, so, sb, "exitR", A(AnchorExitR), c);
                RefCheck(v, so, sb, "stopL", stopGo[0], c);
                RefCheck(v, so, sb, "stopR", stopGo[1], c);
                RefCheck(v, so, sb, "signBack", A(AnchorSignBack), c);
                RefCheck(v, so, sb, "axleFront", A(AxleFrontMesh), c);
                RefCheck(v, so, sb, "axleRear", A(AxleRearMesh), c);
                RefCheck(v, so, sb, "stopButton", stopB[0], c);
                if (so.FindProperty("acceptButton") != null) RefCheck(v, so, sb, "acceptButton", stopB[1], c);
                CartUiCheck(v, go, i, so, sb, A(AnchorSeat), manager, language, p);
                var rends = model.GetComponentsInChildren<Renderer>(true);
                ArrayCheck(v, so, sb, "renderers", rends, c);

                // 材質の割当
                foreach (var r in rends)
                {
                    v.Req(r.sharedMaterials.Length > 0 && r.sharedMaterials.All(m => m != null && (m == bodyMat || m == signMat)), c + " renderer " + r.name + " uses only Body/Sign materials");
                    anySignUsed |= r.sharedMaterials.Contains(signMat);
                }
            }
            v.r.placeholderModel = anyPlaceholder;
            if (anyPlaceholder) v.Pend("仮形状: Model is the placeholder Cube (FBX " + FbxPath + " not built in). Rerun Cart/1 after the FBX arrives.");
            if (!anyPlaceholder && !anySignUsed) v.Pend("Sign material is not assigned to any renderer (FBX has no slot named Sign)");

            // ---- M1.1: 軌道・座席の同期配列 ----
            TrackCheck(v, manager, mso, mb, route, leash);

            // ---- 材質とテクスチャ ----
            MaterialCheck(v, bodyMat, BodyMaterialPath, BaseColorPath);
            MaterialCheck(v, signMat, SignMaterialPath, SignTexturePath);
            foreach (var path in new[] { BaseColorPath, SignTexturePath })
            {
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) { v.Pend("texture missing: " + path); continue; }
                v.Req(ti.sRGBTexture && !ti.isReadable, path + " import sRGB on, Read/Write off");
                var ps = ti.GetPlatformTextureSettings("Android");
                v.Req(ps.overridden && ps.format == TextureImporterFormat.ASTC_6x6, path + " Android ASTC 6x6 (actual " + (ps.overridden ? ps.format.ToString() : "not overridden") + ")");
            }
            var mi = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (mi == null) v.Pend("FBX missing: " + FbxPath);
            else v.Req(ModelImportOk(mi), FbxPath + " import: scale 1, useFileScale, normals Import, compression Off, lightmap UV off, colliders off, Read/Write off, rig None, blend shapes off (actual " + ModelImportSummary(mi) + ")");

            // ---- 乗り場パネル ----
            for (int s = 0; s < StationCount; s++)
            {
                string n = "Station_" + s;
                var panel = expectedPanels[s];
                if (panel == null) { v.Req(false, n + " with KH_CartStationPanel under " + StationsName); continue; }
                var pb = SyncCheck(v, panel, Networking.SyncType.None, n);
                if (leash.spotPositions != null && leash.spotPositions.Length > s && leash.spotYaws != null && leash.spotYaws.Length > s)
                {
                    Vector3 spot = leash.spotPositions[s];
                    float yaw = leash.spotYaws[s] * Mathf.Deg2Rad;
                    var right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
                    var d = panel.transform.position - spot; float height = d.y; d.y = 0f;
                    v.Req(Mathf.Abs(d.magnitude - PanelSide) < 0.01f && Vector3.Dot(d.normalized, right) > 0.999f, n + " 2.5 m to the right of spot " + s + " (actual " + d.magnitude.ToString("F2") + " m)");
                    var fwd = panel.transform.forward; fwd.y = 0f;
                    v.Req(Vector3.Dot(fwd.normalized, right) > 0.999f, n + " front (-Z) faces the spot");
                    v.r.passItems.Add(n + " info: height above spot = " + height.ToString("F2") + " m (ground + " + PanelHeight + ")");
                }
                var board = panel.transform.Find(BoardName);
                v.Req(board != null, n + "/" + BoardName);
                if (board != null)
                {
                    var canvas = board.GetComponent<Canvas>(); var bcol = board.GetComponent<BoxCollider>(); var brt = (RectTransform)board;
                    v.Req(board.gameObject.layer == 0 && board.gameObject.activeSelf && canvas != null && canvas.renderMode == RenderMode.WorldSpace && board.GetComponent<VRCUiShape>() != null && board.GetComponent<GraphicRaycaster>() != null,
                        n + " board: WorldSpace Canvas + VRCUiShape + GraphicRaycaster on Default, active");
                    v.Req(bcol != null && bcol.isTrigger && Mathf.Approximately(bcol.size.x, brt.sizeDelta.x) && Mathf.Approximately(bcol.size.y, brt.sizeDelta.y) && Mathf.Approximately(brt.localScale.x, PanelScale),
                        n + " board BoxCollider trigger matches the board (scale 0.001)");
                    var start = board.Find(StartName);
                    if (start == null) v.Req(false, n + " " + StartName); else EventCheck(v, start.gameObject, pb, "_PressStart", n);
                    for (int i = 0; i < MaxGroups; i++)
                    {
                        var row = board.Find(JoinPrefix + i);
                        if (row == null) { v.Req(false, n + " " + JoinPrefix + i); continue; }
                        EventCheck(v, row.gameObject, pb, "_PressJoin" + i, n);
                        v.Req(!row.gameObject.activeSelf, n + " " + JoinPrefix + i + " hidden at start");
                    }
                    foreach (var g in board.GetComponentsInChildren<Graphic>(true))
                        if (g.raycastTarget && g.GetComponent<Button>() == null && g.transform != board) v.r.ngItems.Add(n + " decoration blocks raycast: " + g.name);
                }
                var so = new SerializedObject(panel);
                RefCheck(v, so, pb, "manager", manager, n);
                RefCheck(v, so, pb, "language", language, n);
                IntCheck(v, so, pb, "spot", s, n);
                Transform B(string path) { return board != null ? board.Find(path) : null; }
                RefCheck(v, so, pb, "titleLabel", Comp<TextMeshProUGUI>(B("Title")), n);
                RefCheck(v, so, pb, "noticeLabel", Comp<TextMeshProUGUI>(B("Notice")), n);
                RefCheck(v, so, pb, "startLabel", Comp<TextMeshProUGUI>(B(StartName + "/Label")), n);
                RefCheck(v, so, pb, "resultLabel", Comp<TextMeshProUGUI>(B("Result")), n);
                RefCheck(v, so, pb, "startButton", B(StartName) != null ? B(StartName).gameObject : null, n);
                ArrayCheck(v, so, pb, "joinButtons", Enumerable.Range(0, MaxGroups).Select(i => B(JoinPrefix + i) != null ? B(JoinPrefix + i).gameObject : null).ToArray(), n);
                var nameLabels = Enumerable.Range(0, MaxGroups).Select(i => Comp<TextMeshProUGUI>(B(JoinPrefix + i + "/Label"))).ToArray();
                var infoLabels = Enumerable.Range(0, MaxGroups).Select(i => Comp<TextMeshProUGUI>(B(JoinPrefix + i + "/" + JoinInfoName))).ToArray();
                ArrayCheck(v, so, pb, "joinLabels", nameLabels, n);
                ArrayCheck(v, so, pb, "joinInfoLabels", infoLabels, n);
                // フォント: 表示名 = KH_DisplayName SDF (無ければ共通フォント)、行き先・人数 = 共通フォント (手元メニューと同じ)
                var nameFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KH_CourseTravelNameFontEditor.FontPath);
                var sharedFont = p.sectionTitleLabel != null ? p.sectionTitleLabel.font : null;
                if (nameFont == null) v.Pend(n + " display-name font missing (" + KH_CourseTravelNameFontEditor.FontPath + "); join name labels use the shared font");
                for (int i = 0; i < MaxGroups; i++)
                {
                    var nl = nameLabels[i]; var il = infoLabels[i];
                    v.Req(nl != null && nl.font == (nameFont != null ? nameFont : sharedFont), n + " " + JoinPrefix + i + " name label font = " + (nameFont != null ? nameFont.name : "shared") + " (actual " + (nl != null && nl.font != null ? nl.font.name : "null") + ")");
                    v.Req(il != null && sharedFont != null && il.font == sharedFont, n + " " + JoinPrefix + i + " info label font = shared " + (sharedFont != null ? sharedFont.name : "null") + " (actual " + (il != null && il.font != null ? il.font.name : "null") + ")");
                    if (nl == null || il == null) continue;
                    var row = nl.transform.parent;
                    v.Req(row != null && row.childCount == 2 && il.transform.parent == row, n + " " + JoinPrefix + i + " has exactly Label + " + JoinInfoName + " (no leftover single-label layout)");
                    var nr = nl.rectTransform; var ir = il.rectTransform; float half = JoinRowSize.x * .5f;
                    float nL = nr.anchoredPosition.x - nr.sizeDelta.x * .5f, nR = nr.anchoredPosition.x + nr.sizeDelta.x * .5f, iL = ir.anchoredPosition.x - ir.sizeDelta.x * .5f, iR = ir.anchoredPosition.x + ir.sizeDelta.x * .5f;
                    v.Req(nL >= -half && nR <= iL && iR <= half && nl.overflowMode == TextOverflowModes.Ellipsis && !nl.enableAutoSizing && !nl.enableWordWrapping,
                        n + " " + JoinPrefix + i + " name (fixed width, ellipsis) and info side by side inside the button");
                }
            }

            // ---- 文言: JSON の 13 言語と、KD_Language に適用済みか ----
            var catalog = CourseCatalog();
            v.Req(catalog.languages.Length == 13, "course catalog has 13 languages (actual " + catalog.languages.Length + ")");
            foreach (var key in CartKeys)
            {
                var missing = catalog.languages.Where(l => l.texts.FirstOrDefault(t => t.key == key) == null || string.IsNullOrWhiteSpace(l.texts.First(t => t.key == key).text)).Select(l => l.code).ToArray();
                v.Req(missing.Length == 0, "catalog " + key + " present in all 13 languages" + (missing.Length > 0 ? " (missing " + string.Join(",", missing) + ")" : ""));
                bool applied = language.extensionKeys != null && language.extensionKeys.Contains(key);
                v.Req(applied, "KD_Language.extensionKeys contains " + key);
                if (!applied) continue;
                var empty = language.languageCodes.Where(code => string.IsNullOrEmpty(language._TextForLanguage(key, code)) || language._TextForLanguage(key, code) != KD_LocalizationEditor.Text(catalog, code, key)).ToArray();
                v.Req(empty.Length == 0, "KD_Language text " + key + " matches the catalog in every language" + (empty.Length > 0 ? " (stale " + string.Join(",", empty) + ")" : ""));
            }
            v.Req(language.extensionKeys != null && language.extensionTexts != null && language.extensionTexts.Length == language.extensionKeys.Length * language.languageCodes.Length,
                "KD_Language extensionTexts = keys x languages");
            var lb = UdonSharpEditorUtility.GetBackingUdonBehaviour(language);
            v.Req(lb != null && lb.publicVariables.TryGetVariableValue("extensionKeys", out object ek) && ek is string[] eks && CartKeys.All(eks.Contains), "KD_Language extensionKeys contains cart.* (backing)");
            if (lb != null) v.r.backingSymbolCounts.Add("KD_Language=" + lb.publicVariables.VariableSymbols.Count());
            // 行き先名は既存キーを使う (新設しない)
            foreach (var key in new[] { "course.walk.forward", "course.walk.reverse", "course.walk.upper", "course.walk.river" })
                v.Req(catalog.languages[0].texts.Any(t => t.key == key), "destination key " + key + " exists (reused, not added)");
            v.Req(!catalog.languages[0].texts.Any(t => t.key.StartsWith("cart.") && !CartKeys.Contains(t.key)), "no extra cart.* keys beyond the contract");

            // ---- NetworkIDs の残骸 ----
            var descriptor = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
            if (descriptor != null)
            {
                int dangling = descriptor.NetworkIDCollection.Count(x => x.gameObject == null && (x.SerializedTypeNames == null || x.SerializedTypeNames.Count == 0));
                int typed = descriptor.NetworkIDCollection.Count(x => x.gameObject == null && x.SerializedTypeNames != null && x.SerializedTypeNames.Count > 0);
                v.Req(dangling == 0, "NetworkIDs: no untyped entries pointing at missing objects (actual " + dangling + ")");
                if (typed > 0) v.Pend("NetworkIDs: " + typed + " typed entries point at missing objects (not removed automatically; review by hand)");
            }
        }

        // ============================================================
        // 検査の部品
        // ============================================================

        static T Comp<T>(Transform t) where T : Component { return t != null ? t.GetComponent<T>() : null; }

        /// <summary>LODGroup: Model 直下に 2 段 (LOD0 = Body + AxleFront + AxleRear @0.12、LOD1 = Body_LOD1 @0.03)、三角形は LOD1 ≤ 1,500・LOD0 合計 ≤ 4,000。
        /// 三角形は index 数から数える (Read/Write Off でも読める)。</summary>
        static void LodCheck(V v, Transform model, string c)
        {
            var g = model.GetComponent<LODGroup>();
            v.Req(g != null, c + " Model has LODGroup");
            if (g == null) return;
            var lods = g.GetLODs();
            v.Req(lods.Length == 2, c + " LODGroup has 2 levels (actual " + lods.Length + ")");
            if (lods.Length != 2) return;
            Renderer R(string n) { var t = FindDeep(model, n); return t != null ? t.GetComponent<Renderer>() : null; }
            var want0 = new[] { R(BodyMesh), R(AxleFrontMesh), R(AxleRearMesh) }; var want1 = new[] { R(BodyLodMesh) };
            v.Req(want0.All(r => r != null) && lods[0].renderers.Length == 3 && want0.All(r => lods[0].renderers.Contains(r)), c + " LOD0 = Body + AxleFront + AxleRear");
            v.Req(want1[0] != null && lods[1].renderers.Length == 1 && lods[1].renderers[0] == want1[0], c + " LOD1 = Body_LOD1 only");
            v.Req(Mathf.Abs(lods[0].screenRelativeTransitionHeight - Lod0Height) < 1e-4f && Mathf.Abs(lods[1].screenRelativeTransitionHeight - Lod1Height) < 1e-4f,
                c + " LOD heights 0.12 / 0.03 (actual " + lods[0].screenRelativeTransitionHeight.ToString("F3") + " / " + lods[1].screenRelativeTransitionHeight.ToString("F3") + ")");
            int t0 = lods[0].renderers.Sum(r => Triangles(r)), t1 = lods[1].renderers.Sum(r => Triangles(r));
            v.Req(t0 > 0 && t0 <= Lod0MaxTriangles, c + " LOD0 triangles " + t0 + " <= " + Lod0MaxTriangles);
            v.Req(t1 > 0 && t1 <= Lod1MaxTriangles, c + " LOD1 triangles " + t1 + " <= " + Lod1MaxTriangles);
            var body = R(BodyMesh); var lod1 = R(BodyLodMesh);
            v.Req(body != null && lod1 != null && lod1.sharedMaterials.SequenceEqual(body.sharedMaterials), c + " Body_LOD1 uses the same materials as Body");
        }

        static int Triangles(Renderer r)
        {
            var mf = r != null ? r.GetComponent<MeshFilter>() : null;
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) return 0;
            long indices = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) indices += mesh.GetIndexCount(s);
            return (int)(indices / 3);
        }

        static UdonBehaviour SyncCheck(V v, UdonSharpBehaviour b, Networking.SyncType want, string name)
        {
            var u = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            v.Req(u != null && u.SyncMethod == want, name + " backing sync=" + want + (u != null ? " (actual " + u.SyncMethod + ")" : " (no backing)"));
            if (u != null) v.r.backingSymbolCounts.Add(name + "=" + u.publicVariables.VariableSymbols.Count());
            return u;
        }

        /// <summary>backing heap での期待値: U# の参照は相手の backing UdonBehaviour、Unity の参照は同じオブジェクト (vrc-udonsharp §4)。</summary>
        static object BackingValueOf(UnityEngine.Object o)
        {
            if (o is UdonSharpBehaviour u) return UdonSharpEditorUtility.GetBackingUdonBehaviour(u);
            return o;
        }

        static void RefCheck(V v, SerializedObject so, UdonBehaviour backing, string field, UnityEngine.Object expected, string owner)
        {
            var p = so.FindProperty(field);
            if (p == null || p.propertyType != SerializedPropertyType.ObjectReference) { v.Req(false, owner + "." + field + " field exists (contract)"); return; }
            v.Req(expected != null && p.objectReferenceValue == expected, owner + "." + field + " wired (proxy)" + (p.objectReferenceValue == null ? " [null]" : p.objectReferenceValue != expected ? " [" + p.objectReferenceValue.name + "]" : ""));
            if (backing == null) return;
            bool ok = backing.publicVariables.TryGetVariableValue(field, out object bv) && bv != null && ReferenceEquals(bv, BackingValueOf(expected));
            v.Req(ok, owner + "." + field + " wired (backing)");
        }

        static void IntCheck(V v, SerializedObject so, UdonBehaviour backing, string field, int expected, string owner)
        {
            var p = so.FindProperty(field);
            if (p == null || p.propertyType != SerializedPropertyType.Integer) { v.Req(false, owner + "." + field + " int field exists (contract)"); return; }
            v.Req(p.intValue == expected, owner + "." + field + " == " + expected + " (proxy " + p.intValue + ")");
            // 既定値 0 と同じ値は backing に override が無いことがある (heap の既定値で正しい)
            if (backing != null && backing.publicVariables.TryGetVariableValue(field, out object bv)) v.Req(bv is int bi && bi == expected, owner + "." + field + " == " + expected + " (backing)");
            else if (expected != 0) v.Req(false, owner + "." + field + " == " + expected + " (backing missing)");
        }

        static void ArrayCheck(V v, SerializedObject so, UdonBehaviour backing, string field, UnityEngine.Object[] expected, string owner)
        {
            var p = so.FindProperty(field);
            if (p == null || !p.isArray) { v.Req(false, owner + "." + field + " array field exists (contract)"); return; }
            bool proxyOk = p.arraySize == expected.Length && expected.All(e => e != null);
            for (int i = 0; proxyOk && i < expected.Length; i++) proxyOk = p.GetArrayElementAtIndex(i).objectReferenceValue == expected[i];
            v.Req(proxyOk, owner + "." + field + " [" + expected.Length + "] wired (proxy, actual length " + p.arraySize + ")");
            if (backing == null) return;
            bool ok = backing.publicVariables.TryGetVariableValue(field, out object bv) && bv is Array a && a.Length == expected.Length;
            if (ok) for (int i = 0; ok && i < expected.Length; i++) ok = ReferenceEquals(((Array)bv).GetValue(i), BackingValueOf(expected[i]));
            v.Req(ok, owner + "." + field + " [" + expected.Length + "] wired (backing)");
        }

        static void EventCheck(V v, GameObject go, UdonBehaviour target, string evt, string owner)
        {
            var b = go.GetComponent<Button>();
            bool ok = b != null && b.onClick.GetPersistentEventCount() == 1 && target != null && b.onClick.GetPersistentTarget(0) == target && b.onClick.GetPersistentMethodName(0) == "SendCustomEvent";
            if (ok)
            {
                var call = new SerializedObject(b).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                ok = call.arraySize == 1 && call.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == evt;
            }
            v.Req(ok, owner + " " + go.name + " onClick -> panel backing SendCustomEvent(" + evt + ")");
        }

        static void MaterialCheck(V v, Material m, string path, string texturePath)
        {
            v.Req(m != null, "material " + path + " exists");
            if (m == null) return;
            v.Req(m.shader != null && m.shader.name == "Standard", path + " shader Standard (actual " + (m.shader != null ? m.shader.name : "null") + ")");
            v.Req(m.enableInstancing, path + " GPU Instancing on");
            v.Req(Mathf.Abs(m.GetFloat("_Glossiness") - Smoothness) < 1e-4f && Mathf.Abs(m.GetFloat("_Metallic") - Metallic) < 1e-4f, path + " Smoothness 0.25 / Metallic 0");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (tex == null) v.Pend(path + ": texture " + texturePath + " not yet delivered");
            else v.Req(m.GetTexture("_MainTex") == tex, path + " _MainTex = " + texturePath);
        }
    }
}
