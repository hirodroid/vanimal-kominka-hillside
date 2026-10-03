using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.SDKBase.Editor.BuildPipeline;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-WLD-001 段階 1: 地点 ID の解決・共通の検査 1〜4・仮のワープボタン (設計書 §D.3・§A.1)。
    ///
    /// Course/80 は本番シーンの局所編集だけを行う (経路 436 点・目的地 12 行 / 6 目的地には触らない。禁止された再生成メニューも呼ばない):
    /// - 周回路のノードから意味付き地点 6 つを引き直し、Manager の spotPositions / spotYaws / spotWarpEnabled / spotNodes / spotIds に書く
    ///   (IMP-WLD-004: 洗い場・案内板・ポータルの「着地点を置かない円」の外で、接続犬 6 匹の置き場所が通るノードを採り、円を travelKeepOut* に書く)
    /// - 共通の検査 4 に使う川の中心線 (2 m ごと) と橋の区域を Manager に書く
    /// - Manager の切符の同期配列 7 本を長さ 6 の初期値で書く (宣言時の初期化と同じ値。proxy と backing をそろえる)
    /// - さんぽタブ (Page_Walk) の上段に [奥の広場へワープ] [川辺へワープ] を作り、スクロールの上端を 186 → 140 に下げる
    /// - コースの翻訳表 (course.travel.*) を KD_Language の extension に入れ直す
    /// Course/81 は保存済みの状態を読み直して検査し、Temp/imp-wld-001/travel-verify.json に出す。</summary>
    public static class KH_CourseTravelEditor
    {
        const string OutDir = "Temp/imp-wld-001";
        public static readonly string[] SpotIds = { "spot.gate", "spot.plaza", "spot.upper", "spot.bridgeUp", "spot.river", "spot.bridgeDown" };
        // 2026-09-27 の初期実装は奥広場と川辺だけ。IMP-WLD-004 (ユーザー指定 2026-10-02「1〜5 のポイント全部」、初期公開に入れる 2026-10-03):
        // 地図の番号 1〜5 = 古民家側の入口・広場・奥の広場・上流の橋・川辺。6 (下流の橋) は対象外のまま (追加は別判断)。
        // 変えるときはここを変えて Course/80 → 84 → 81 を流す (地図の ●/○ は 84 が描き直す)
        public static readonly bool[] WarpEnabled = { true, true, true, true, true, false };
        // IMP-WLD-004: 着地点を置かない円の余白 = 接続犬の並び (TravelPlacement: 横 0.75 + 0.45 × 2 = 1.65 m・前 0.5 + 0.35 × 2 = 1.2 m で約 2.0 m)
        // + 置き場所の NavMesh への寄せ (最大 1 m)。洗い場の犬よけ 4 m・個人の床 5 × 5 m・案内板とポータルの外形にこれを足した半径を実行時に避ける
        public const float KeepOutDogPad = 3f;
        const float BoardMargin = 0.5f, PortalRadius = 1.0f, DogRiverClear = 5f;
        const string GuideBoardsRoot = "KH_GuideBoards", PortalAnchorName = "PortalAnchor";   // KH_GuideBoardsEditor の名前 (別作業のファイルなので文字列で持つ)
        // 着いたときの向き: true = 遠景の古民家の方向、false = 道の正方向 (ノード k → k+1)。
        // 設計書 §D.1: 古民家向きは既存広場だけ。奥広場を含むほかは道の正方向
        public static readonly bool[] FaceKominka = { false, true, false, false, false, false };
        static readonly Vector3 BridgeUpBase = new Vector3(403.5f, 21.45f, 502.3f), BridgeDownBase = new Vector3(436.0f, 6.51f, 208.0f);
        static readonly float[] BridgeZs = { 190f, 490f };
        const float RiverZ0 = -30f, RiverZ1 = 740f, RiverStep = 2f;
        const string KominkaPath = "KH_World/KH_Places/KH_FarKominka";
        // 段階 2 (2026-09-27): 上段の仮ボタン 2 つは [地図と移動] 1 つに置き換えた。上段とスクロールの寸法は KH_CourseTravelMapEditor と共有する
        public const float TopRowY = 164, ContentTopWithRow = 140, PageBottom = -250;

        [Serializable] public class SpotResult
        {
            public string id, why = "";
            public int node = -1, baseNode = -1, candidatesPassed, candidatesTotal;
            public Vector3 position;
            public float yaw, riverDistance;
            public bool warpEnabled, basePassed;
            public string nearestKeepOut = "";       // IMP-WLD-004: いちばん近い「着地点を置かない円」と、その縁までの距離 (負なら中)
            public float nearestKeepOutClearance;
            public int dogRoom;                      // 6 匹の置き場所 (TravelPlacement と同じ式) のうち NavMesh・高さ・川・円の外を通った数
        }
        /// <summary>IMP-WLD-004: 地点ワープの着地点を置かない円。core = 物の半径、radius = core + KeepOutDogPad (実行時が比べる値)。</summary>
        [Serializable] public class KeepOut
        {
            public string name;
            public Vector3 center;
            public float core, radius;
        }
        [Serializable] public class Report
        {
            public string utc, scene, status = "RUNNING", mode;
            public int errors;
            public List<string> findings = new List<string>();
            public List<SpotResult> spots = new List<SpotResult>();
            public List<KeepOut> keepOuts = new List<KeepOut>();
            public List<string> labelFit = new List<string>();
            public long mapPngBytes, mapGpuBytesPC, mapGpuBytesMobile, mapEditorMemoryBytes;   // 段階 2: 地図テクスチャの容量 (GPU は形式からの計算値)
            public string mapFormats;
            public string[] nameFontCoverage;                          // 段階 4: 表示名の見本ごとの欠字 (対象外は記録だけ)
            public int nameFontCharset, nameFontGlyphs;                 // 対象の文字数・焼いたグリフ数 (本体 + Latin)
            public string nameFontLacking, nameFontUsage, nameFontBuildRefs;               // 元のフォントに無い字・アトラスの使用率
            public long nameFontAtlasBytes, nameFontAssetBytes;         // アトラス (Alpha8 = 幅 × 高さ) の合計・保存したアセット
            public string limitations = "Editor static data, saved Udon values and physics against scene colliders only. Check 5 (other players 1 m) runs only at request time. No Play, network, VR/Touch input or device font fallback is tested.";
        }

        // ============================================================
        // Course/80 生成
        // ============================================================

        [MenuItem("Tools/KominkaHillside/Course/80 - Build Travel Spots")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var p = Pouch(); var m = p.manager; var route = m.route;
            var catalog = CourseCatalog(p.language);
            var report = new Report { utc = DateTime.UtcNow.ToString("o"), scene = p.gameObject.scene.path, mode = "build" };
            var water = GeneratedWater();
            var keep = GatherKeepOuts();
            report.keepOuts = keep;
            var spots = Resolve(route, water, keep, report);
            foreach (var s in spots) if (s.warpEnabled && s.node < 0) throw new InvalidOperationException("Warp spot failed common checks 1-4: " + s.id + " " + s.why);
            var managerBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(m);
            var pouchBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            if (managerBacking == null || pouchBacking == null) throw new InvalidOperationException("Missing backing behaviours");
            Directory.CreateDirectory(OutDir);
            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-travel.unity", true);
            try
            {
                // ---- Manager: 地点・川・切符 (SerializedObject) ----
                var so = new SerializedObject(m);
                SetStrings(so, "spotIds", SpotIds);
                SetVectors(so, "spotPositions", spots.Select(s => s.position).ToArray());
                SetFloats(so, "spotYaws", spots.Select(s => s.yaw).ToArray());
                SetBools(so, "spotWarpEnabled", spots.Select(s => s.warpEnabled).ToArray());
                SetInts(so, "spotNodes", spots.Select(s => s.node).ToArray());
                SetVectors(so, "travelKeepOutCenters", keep.Select(k => k.center).ToArray());   // IMP-WLD-004
                SetFloats(so, "travelKeepOutRadii", keep.Select(k => k.radius).ToArray());
                SetFloats(so, "travelRiverXs", water.xs);
                so.FindProperty("travelRiverZ0").floatValue = RiverZ0;
                so.FindProperty("travelRiverStep").floatValue = RiverStep;
                SetFloats(so, "travelBridgeZs", water.bridgeZs);
                SetFloats(so, "travelBridgeXs", water.bridgeXs);
                SetInts(so, "travelReq", new int[6]);
                SetInts(so, "travelHolder", Enumerable.Repeat(-1, 6).ToArray());
                SetVectors(so, "travelCenter", new Vector3[6]);
                SetVectors(so, "travelPoint", new Vector3[6]);
                SetFloats(so, "travelYaw", new float[6]);
                SetInts(so, "travelAtMs", new int[6]);
                SetInts(so, "travelPhase", new int[6]);
                foreach (var name in new[] { "debugDropTravel", "debugDropTravelDone", "debugSkipTravelTeleport", "debugForgeTravelMask", "debugDropTravelMoved" }) so.FindProperty(name).intValue = 0;
                so.FindProperty("debugDelayTravel").floatValue = 0f;
                so.FindProperty("debugDelayTravelMoved").floatValue = 0f;
                so.ApplyModifiedPropertiesWithoutUndo();
                if (managerBacking.SyncMethod != Networking.SyncType.Manual) { managerBacking.SyncMethod = Networking.SyncType.Manual; EditorUtility.SetDirty(managerBacking); }

                // ---- 翻訳表を extension に入れ直す (Course/25 と同じ式) ----
                ApplyCatalog(p);
                UdonSharpEditorUtility.CopyProxyToUdon(m); EditorUtility.SetDirty(m);
                EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
                if (!EditorSceneManager.SaveScene(p.gameObject.scene)) throw new IOException("Travel build save failed");
            }
            catch
            {
                Debug.LogError("[KH Travel] Build failed; the scene before this run is at " + OutDir + "/before-travel.unity. Reopen the scene without saving to discard partial changes.");
                throw;
            }
            report.status = "BUILT";
            File.WriteAllText(OutDir + "/travel-build.json", JsonUtility.ToJson(report, true));
            Debug.Log("[KH Travel] Built spots=" + string.Join(",", spots.Select(s => s.id + ":" + s.node + (s.warpEnabled ? "(warp)" : ""))) + " keepOuts=" + keep.Count + ". Run Course/84 (map and fade), Course/27 (glyphs) and then Course/81 Verify.");
        }

        /// <summary>コースの翻訳表 (CourseUITranslations.json) を KD_Language の extension に入れ直す (Course/25 と同じ式)。Course/80・84 が使う。</summary>
        public static void ApplyCatalog(KD_TreatPouch p)
        {
            var catalog = CourseCatalog(p.language);
            var language = p.language;
            language.extensionKeys = catalog.languages.First(l => l.code == catalog.fallbackLanguage).texts.Select(e => e.key).ToArray();
            language.extensionTexts = catalog.languages.SelectMany(l => language.extensionKeys.Select(k => KD_LocalizationEditor.Text(catalog, l.code, k))).ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(language); EditorUtility.SetDirty(language);
        }

        public static void LowerScroll(KD_TreatPouch p)
        {
            var scroll = p.tabScrolls != null && p.tabScrolls.Length > KD_TreatPouch.TabWalk ? p.tabScrolls[KD_TreatPouch.TabWalk] : null;
            if (scroll == null || scroll.viewport == null || scroll.verticalScrollbar == null) throw new InvalidOperationException("Walk scroll missing");
            float height = ContentTopWithRow - PageBottom;
            var root = (RectTransform)scroll.transform;
            root.anchoredPosition = new Vector2(root.anchoredPosition.x, (ContentTopWithRow + PageBottom) / 2); root.sizeDelta = new Vector2(root.sizeDelta.x, height);
            scroll.viewport.sizeDelta = new Vector2(scroll.viewport.sizeDelta.x, height);
            var bar = (RectTransform)scroll.verticalScrollbar.transform; bar.sizeDelta = new Vector2(bar.sizeDelta.x, height);
            var track = bar.Find("SlidingArea") as RectTransform; if (track != null) track.sizeDelta = new Vector2(track.sizeDelta.x, height - 8);
            EditorUtility.SetDirty(root.gameObject);
        }

        // ============================================================
        // 地点の解決と共通の検査 1〜4 (実行時の KH_LeashManager._TravelLandingOk と同じ数値)
        // ============================================================

        static List<SpotResult> Resolve(KH_WalkRoute route, Water river, List<KeepOut> keep, Report report)
        {
            if (route == null || !route.closedLoop || route.nodePositions == null || route.nodePositions.Length != 436 || route.destinationNodes == null || route.destinationNodes.Length != 6)
                throw new InvalidOperationException("Closed 436-node route with 6 destinations required (production.md)");
            var n = route.nodePositions;
            int[] baseNodes = {
                route.destinationNodes[0], route.destinationNodes[1], route.destinationNodes[4],
                Nearest(n, BridgeUpBase), route.destinationNodes[5], Nearest(n, BridgeDownBase) };
            var kominka = GameObject.Find(KominkaPath);
            var list = new List<SpotResult>();
            for (int s = 0; s < 6; s++)
            {
                var r = new SpotResult { id = SpotIds[s], baseNode = baseNodes[s], warpEnabled = WarpEnabled[s] };
                if (FaceKominka[s] && kominka == null) report.findings.Add("KH_FarKominka not found; " + r.id + " faces the trail instead");
                // 基準のノードが通らなければ前後 ±1〜5 のノードから通るものを採る (橋・水際から離れる側を優先はしない。近い順)。
                // IMP-WLD-004: 共通の検査 1〜4 に加えて、着地点を置かない円 (洗い場・案内板・ポータル + 犬の並び) の外であること、
                // 接続犬 6 匹の置き場所 (実行時の TravelPlacement と同じ式) が全部通ること
                foreach (int d in new[] { 0, 1, -1, 2, -2, 3, -3, 4, -4, 5, -5 })
                {
                    int node = (baseNodes[s] + d + n.Length) % n.Length;
                    if (!LandingSpot(n[node], river, keep, out Vector3 foot, out string why)) { if (d == 0) r.why = why; continue; }
                    float yaw = SpotYaw(n, node, FaceKominka[s] ? kominka : null);
                    int room = DogRoom(foot, yaw, river, keep, out string roomWhy);
                    if (room < 6) { if (d == 0) r.why = "6 dogs room " + room + "/6 (" + roomWhy + ")"; continue; }
                    r.node = node; r.position = foot; r.basePassed = d == 0; r.yaw = yaw; r.dogRoom = room; break;
                }
                if (r.node < 0)
                {
                    r.position = n[baseNodes[s]]; r.node = -1; r.yaw = SpotYaw(n, baseNodes[s], FaceKominka[s] ? kominka : null);
                    report.findings.Add("Spot " + r.id + " has no passing node near " + baseNodes[s] + " (" + r.why + ")");
                }
                r.riverDistance = RiverDistance(r.position, river);
                NearestKeepOut(r.position, keep, out r.nearestKeepOut, out r.nearestKeepOutClearance);
                list.Add(r);
            }
            report.spots = list;
            return list;
        }

        /// <summary>着いたときの向き: 古民家向きなら遠景の古民家の方向、それ以外は道の正方向 (ノード k → k+1)。</summary>
        static float SpotYaw(Vector3[] n, int k, GameObject kominka)
        {
            Vector3 dir = kominka != null ? kominka.transform.position - n[k] : n[(k + 1) % n.Length] - n[k];
            dir.y = 0f;
            return dir.sqrMagnitude > 1e-6f ? Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg : 0f;
        }

        // ============================================================
        // IMP-WLD-004: 着地点を置かない円と、6 匹の置き場所
        // ============================================================

        /// <summary>シーンから「地点ワープの着地点を置かない円」を集める (Course/80 が Manager に書き、81 が保存値と照合する)。
        /// - 洗い場の犬よけの円: KH_WashStationCall の anchor (無ければ自分) を中心に keepOutRadius
        /// - 洗い場の個人の床: KH_WashPersonalFloor の位置を中心に outerHalf の対角の半分 (5 × 5 m なら 3.54 m)
        /// - 案内板: KH_GuideBoards の直下の板ごとに、有効な Renderer の外接の水平半径 + 0.5 m (板は Collider を持たないので頭上の検査で弾けない)
        /// - ポータル: KH_GuideBoards 内の PortalAnchor と、シーンの VRCPortalMarker に 1.0 + 0.5 m
        /// 半径 (実行時が比べる値) = 上の値 + KeepOutDogPad。並びは中心の x, z の順 (保存値との照合を順序に依らせない)。</summary>
        public static List<KeepOut> GatherKeepOuts()
        {
            var list = new List<KeepOut>();
            foreach (var call in UnityEngine.Object.FindObjectsOfType<KH_WashStationCall>(true))
            {
                var at = call.anchor != null ? call.anchor.position : call.transform.position;
                list.Add(new KeepOut { name = "wash keep-out " + PathOf(call.transform), center = at, core = Mathf.Max(0f, call.keepOutRadius) });
            }
            foreach (var floor in UnityEngine.Object.FindObjectsOfType<KH_WashPersonalFloor>(true))
                list.Add(new KeepOut { name = "wash floor " + PathOf(floor.transform), center = floor.transform.position, core = floor.outerHalf.magnitude });
            var scene = KH_CourseTravelEditor.FindPouch().gameObject.scene;
            var boards = scene.GetRootGameObjects().FirstOrDefault(g => g.name == GuideBoardsRoot);
            if (boards != null)
            {
                foreach (Transform b in boards.transform)
                {
                    var rs = b.GetComponentsInChildren<Renderer>(false).Where(x => x.enabled).ToArray();
                    if (rs.Length == 0) { list.Add(new KeepOut { name = "board " + PathOf(b) + " (no renderer)", center = b.position, core = 1f + BoardMargin }); continue; }
                    var bounds = rs[0].bounds; foreach (var x in rs) bounds.Encapsulate(x.bounds);
                    float half = new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
                    list.Add(new KeepOut { name = "board " + PathOf(b), center = bounds.center, core = half + BoardMargin });
                }
                foreach (var t in boards.GetComponentsInChildren<Transform>(true))
                    if (t.name == PortalAnchorName) list.Add(new KeepOut { name = "portal " + PathOf(t), center = t.position, core = PortalRadius + BoardMargin });
            }
            foreach (var portal in UnityEngine.Object.FindObjectsOfType<VRC.SDK3.Components.VRCPortalMarker>(true))
                list.Add(new KeepOut { name = "portal marker " + PathOf(portal.transform), center = portal.transform.position, core = PortalRadius + BoardMargin });
            foreach (var k in list) { k.center.y = 0f; k.radius = k.core + KeepOutDogPad; }
            return list.OrderBy(k => Mathf.Round(k.center.x * 100f)).ThenBy(k => Mathf.Round(k.center.z * 100f)).ThenBy(k => k.radius).ToList();
        }

        static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }

        /// <summary>実行時の TravelInKeepOut と同じ式 (水平距離 &lt; 半径)。</summary>
        public static bool InKeepOut(Vector3 p, List<KeepOut> keep, bool core, out string which)
        {
            which = "";
            foreach (var k in keep)
            {
                var d = p - k.center; d.y = 0f;
                if (d.magnitude < (core ? k.core : k.radius)) { which = k.name; return true; }
            }
            return false;
        }

        static void NearestKeepOut(Vector3 p, List<KeepOut> keep, out string name, out float clearance)
        {
            name = ""; clearance = float.MaxValue;
            foreach (var k in keep)
            {
                var d = p - k.center; d.y = 0f;
                float c = d.magnitude - k.radius;
                if (c < clearance) { clearance = c; name = k.name; }
            }
            if (keep.Count == 0) clearance = 0f;
        }

        /// <summary>地点ワープの候補 1 点 (実行時の TravelSpotLandingOk と同じ順): 円の外 → 共通の検査 1〜4。</summary>
        public static bool LandingSpot(Vector3 p, Water w, List<KeepOut> keep, out Vector3 foot, out string why)
        {
            foot = p;
            if (InKeepOut(p, keep, false, out string which)) { why = "keep-out " + which; return false; }
            return Landing(p, w, out foot, out why);
        }

        /// <summary>接続犬 6 匹 (左 3・右 3) の置き場所を、実行時の KH_LeashManager.TravelPlacement と同じ式で作り、
        /// NavMesh 1 m・高さの差 0.6 m 以内 (全体か半分の位置。どちらもだめなら飼い主の足元に重なる = 失敗)、
        /// 川の中心線から 5 m 以上・橋の区域の外、洗い場・案内板・ポータルの円 (余白なし) の外、を数える。</summary>
        public static int DogRoom(Vector3 center, float yaw, Water w, List<KeepOut> keep, out string why)
        {
            why = ""; int ok = 0;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            for (int hand = 0; hand < 2; hand++)
                for (int slot = 0; slot < 3; slot++)
                {
                    float side = hand == 0 ? -1f : 1f;
                    var offset = new Vector3(side * (0.75f + slot * 0.45f), 0f, 0.5f + slot * 0.35f);
                    var want = center + rot * offset;
                    string tag = (hand == 0 ? "L" : "R") + slot;
                    Vector3 at;
                    if (NavMesh.SamplePosition(want, out NavMeshHit hit, 1f, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - center.y) <= 0.6f) at = hit.position;
                    else if (NavMesh.SamplePosition(Vector3.Lerp(center, want, 0.5f), out hit, 1f, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - center.y) <= 0.6f) at = hit.position;
                    else { why += tag + " no navmesh; "; continue; }
                    if (RiverDistance(at, w) < DogRiverClear) { why += tag + " river; "; continue; }
                    if (w.bridgeZs != null && w.bridgeXs != null)
                    {
                        bool onBridge = false;
                        for (int i = 0; i < w.bridgeZs.Length && i < w.bridgeXs.Length; i++)
                            if (Mathf.Abs(at.z - w.bridgeZs[i]) <= KH_LeashManager.TravelBridgeHalfZ && Mathf.Abs(at.x - w.bridgeXs[i]) <= KH_LeashManager.TravelBridgeHalfX) onBridge = true;
                        if (onBridge) { why += tag + " bridge; "; continue; }
                    }
                    if (InKeepOut(at, keep, true, out string which)) { why += tag + " in " + which + "; "; continue; }
                    ok++;
                }
            return ok;
        }

        static int Nearest(Vector3[] n, Vector3 p)
        {
            int best = 0;
            for (int i = 1; i < n.Length; i++) { var a = n[i] - p; var b = n[best] - p; a.y = 0; b.y = 0; if (a.sqrMagnitude < b.sqrMagnitude) best = i; }
            return best;
        }

        static float[] RiverSamples()
        {
            int count = Mathf.RoundToInt((RiverZ1 - RiverZ0) / RiverStep) + 1;
            var xs = new float[count];
            for (int k = 0; k < count; k++) xs[k] = KH_CourseGroundEditor.RiverAt(RiverZ0 + k * RiverStep).x;
            return xs;
        }

        /// <summary>検査 4 に使う川と橋のデータ。Build は生成式から作り、Verify は **Manager に保存された値** から作る (レビュー M4)。</summary>
        public class Water { public float[] xs, bridgeZs, bridgeXs; public float z0, step; }
        public static Water GeneratedWater()
        {
            return new Water { xs = RiverSamples(), z0 = RiverZ0, step = RiverStep, bridgeZs = (float[])BridgeZs.Clone(), bridgeXs = BridgeZs.Select(z => KH_CourseGroundEditor.RiverAt(z).x).ToArray() };
        }
        public static Water SavedWater(KH_LeashManager m)
        {
            return new Water { xs = m.travelRiverXs, z0 = m.travelRiverZ0, step = m.travelRiverStep, bridgeZs = m.travelBridgeZs, bridgeXs = m.travelBridgeXs };
        }

        static float RiverDistance(Vector3 p, Water w)
        {
            float best = float.MaxValue;
            for (int k = 0; k < w.xs.Length; k++) { float dx = p.x - w.xs[k], dz = p.z - (w.z0 + k * w.step); best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz)); }
            return best;
        }

        /// <summary>KH_LeashManager._TravelWaterOk と同じ式・同じ定数。</summary>
        public static bool WaterOk(Vector3 p, Water w)
        {
            if (w.xs == null || w.xs.Length == 0 || w.step <= 0f) return false;
            int k0 = Mathf.Max(0, Mathf.FloorToInt((p.z - KH_LeashManager.TravelRiverWindow - w.z0) / w.step));
            int k1 = Mathf.Min(w.xs.Length - 1, Mathf.CeilToInt((p.z + KH_LeashManager.TravelRiverWindow - w.z0) / w.step));
            float clear = KH_LeashManager.TravelRiverClear * KH_LeashManager.TravelRiverClear;
            for (int k = k0; k <= k1; k++) { float dx = p.x - w.xs[k], dz = p.z - (w.z0 + k * w.step); if (dx * dx + dz * dz < clear) return false; }
            if (w.bridgeZs != null && w.bridgeXs != null)
                for (int i = 0; i < w.bridgeZs.Length && i < w.bridgeXs.Length; i++)
                    if (Mathf.Abs(p.z - w.bridgeZs[i]) <= KH_LeashManager.TravelBridgeHalfZ && Mathf.Abs(p.x - w.bridgeXs[i]) <= KH_LeashManager.TravelBridgeHalfX) return false;
            return true;
        }

        /// <summary>共通の検査 1〜4 (KH_LeashManager._TravelLandingOk と同じ式・同じ定数)。5 (人) は要求の瞬間だけ実行時が行う。</summary>
        public static bool Landing(Vector3 p, Water w, out Vector3 foot, out string why)
        {
            foot = p; why = "";
            int mask = KH_LeashManager.TravelGroundMask;
            if (!Physics.Raycast(p + Vector3.up * KH_LeashManager.TravelRayUp, Vector3.down, out RaycastHit hit, KH_LeashManager.TravelRayLength, mask, QueryTriggerInteraction.Ignore)) { why = "1 footing"; return false; }
            if (hit.normal.y < KH_LeashManager.TravelSlopeMinY) { why = "2 slope normal.y=" + hit.normal.y.ToString("F3"); return false; }
            foot = hit.point;
            if (!NavMesh.SamplePosition(foot, out NavMeshHit nav, KH_LeashManager.TravelNavReach, NavMesh.AllAreas)) { why = "1 navmesh"; return false; }
            if (Physics.CheckCapsule(foot + Vector3.up * KH_LeashManager.TravelHeadLow, foot + Vector3.up * KH_LeashManager.TravelHeadHigh, KH_LeashManager.TravelHeadRadius, mask, QueryTriggerInteraction.Ignore)) { why = "3 headroom"; return false; }
            if (!WaterOk(foot, w)) { why = "4 water/bridge"; return false; }
            return true;
        }

        // ============================================================
        // Course/81 Verify
        // ============================================================

        [MenuItem("Tools/KominkaHillside/Course/81 - Verify Travel Spots and Warp Buttons")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var p = Pouch(); var m = p.manager;
            var report = new Report { utc = DateTime.UtcNow.ToString("o"), scene = p.gameObject.scene.path, mode = "verify" };
            var e = report.findings;
            try { Validate(p, e, report); }
            catch (Exception ex) { e.Add("Exception: " + ex.Message); }
            report.errors = e.Count; report.status = e.Count == 0 ? "TRAVEL_OK" : "FAIL";
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/travel-verify.json", JsonUtility.ToJson(report, true));
            if (e.Count != 0) throw new InvalidOperationException("[KH Travel] FAIL " + e.Count + ":\n" + string.Join("\n", e));
            Debug.Log("[KH Travel] TRAVEL_OK spots=6 warp=" + string.Join(",", report.spots.Where(s => s.warpEnabled).Select(s => s.id)));
        }

        public static void Validate(KD_TreatPouch p, List<string> e, Report report)
        {
            var m = p.manager;
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(m);
            Require(e, backing != null && backing.SyncMethod == Networking.SyncType.Manual, "Manager backing sync=Manual");
            // 切符の同期配列: 長さ 6 と初期値 (proxy と、backing に入っていれば backing も)
            CheckInts(e, backing, "travelReq", m.travelReq, 0);
            CheckInts(e, backing, "travelHolder", m.travelHolder, -1);
            CheckInts(e, backing, "travelAtMs", m.travelAtMs, 0);
            CheckInts(e, backing, "travelPhase", m.travelPhase, 0);
            Require(e, m.travelCenter != null && m.travelCenter.Length == 6 && m.travelCenter.All(v => v == Vector3.zero), "travelCenter length 6 zero");
            Require(e, m.travelPoint != null && m.travelPoint.Length == 6 && m.travelPoint.All(v => v == Vector3.zero), "travelPoint length 6 zero");
            Require(e, m.travelYaw != null && m.travelYaw.Length == 6 && m.travelYaw.All(v => v == 0f), "travelYaw length 6 zero");
            // 試験用の値は 0
            Require(e, m.debugDropTravel == 0 && m.debugDropTravelDone == 0 && m.debugSkipTravelTeleport == 0 && m.debugForgeTravelMask == 0 && m.debugDelayTravel == 0f && m.debugDelayTravelMoved == 0f && m.debugDropTravelMoved == 0, "debug travel hooks are 0 (proxy)");
            foreach (var name in new[] { "debugDropTravel", "debugDropTravelDone", "debugSkipTravelTeleport", "debugForgeTravelMask" })
                if (backing != null && backing.publicVariables.TryGetVariableValue(name, out object v) && v is int iv && iv != 0) e.Add("debug " + name + " nonzero in backing");
            // 地点
            Require(e, m.spotIds != null && m.spotIds.SequenceEqual(SpotIds), "spotIds");
            Require(e, m.spotWarpEnabled != null && m.spotWarpEnabled.SequenceEqual(WarpEnabled), "spotWarpEnabled = " + string.Join(",", SpotIds.Where((id, i) => WarpEnabled[i])) + " (IMP-WLD-004: map numbers 1-5)");
            Require(e, m.spotPositions != null && m.spotPositions.Length == 6 && m.spotYaws != null && m.spotYaws.Length == 6 && m.spotNodes != null && m.spotNodes.Length == 6, "spot arrays length 6");
            // IMP-WLD-004: 着地点を置かない円。保存値 (proxy と backing) = 今のシーンから集めた値 (洗い場・案内板・ポータルを動かしたら Course/80 を流し直す)
            var keep = GatherKeepOuts();
            report.keepOuts = keep;
            Require(e, keep.Count > 0, "keep-out circles found (wash stations / guide boards)");
            Require(e, m.travelKeepOutCenters != null && m.travelKeepOutRadii != null && m.travelKeepOutCenters.Length == keep.Count && m.travelKeepOutRadii.Length == keep.Count
                && Enumerable.Range(0, keep.Count).All(i => Vector3.Distance(m.travelKeepOutCenters[i], keep[i].center) < 0.05f && Mathf.Abs(m.travelKeepOutRadii[i] - keep[i].radius) < 0.01f),
                "travelKeepOutCenters / Radii match the scene (" + keep.Count + " circles; rerun Course/80 after moving a wash station, board or portal)");
            CheckFloats(e, backing, "travelKeepOutRadii", m.travelKeepOutRadii, keep.Select(k => k.radius).ToArray());
            if (backing == null || !backing.publicVariables.TryGetVariableValue("travelKeepOutCenters", out object kc) || !(kc is Vector3[] kca) || kca.Length != keep.Count
                || Enumerable.Range(0, keep.Count).Any(i => Vector3.Distance(kca[i], keep[i].center) >= 0.05f)) e.Add("FAIL backing travelKeepOutCenters missing or differs (rerun Course/80)");
            // 川と橋: 保存値の全要素を生成式と照合する (proxy と backing)。以降の着地点の検査は保存値で行う (レビュー M4)
            var gen = GeneratedWater(); var river = SavedWater(m);
            Require(e, Mathf.Approximately(river.z0, gen.z0) && Mathf.Approximately(river.step, gen.step), "river z0/step");
            CheckFloats(e, backing, "travelRiverXs", river.xs, gen.xs);
            CheckFloats(e, backing, "travelBridgeZs", river.bridgeZs, gen.bridgeZs);
            CheckFloats(e, backing, "travelBridgeXs", river.bridgeXs, gen.bridgeXs);
            if (e.Count > 0) return;
            // 保存値の再解決と共通の検査 1〜4 + 円の外 + 6 匹の置き場所 (全 6 地点)
            var fresh = Resolve(m.route, river, keep, new Report());
            var n = m.route.nodePositions;
            for (int s = 0; s < 6; s++)
            {
                var r = new SpotResult { id = SpotIds[s], node = m.spotNodes[s], position = m.spotPositions[s], yaw = m.spotYaws[s], warpEnabled = m.spotWarpEnabled[s] };
                r.basePassed = LandingSpot(m.spotPositions[s], river, keep, out _, out r.why);
                r.riverDistance = RiverDistance(m.spotPositions[s], river);
                NearestKeepOut(m.spotPositions[s], keep, out r.nearestKeepOut, out r.nearestKeepOutClearance);
                r.dogRoom = DogRoom(m.spotPositions[s], m.spotYaws[s], river, keep, out string roomWhy);
                if (!r.basePassed) e.Add("Spot " + r.id + " fails common checks / keep-out: " + r.why);
                if (r.warpEnabled && r.dogRoom < 6) e.Add("Spot " + r.id + " has room for " + r.dogRoom + "/6 leashed dogs (" + roomWhy + ")");
                if (fresh[s].node != m.spotNodes[s] || Vector3.Distance(fresh[s].position, m.spotPositions[s]) > 0.05f || Mathf.Abs(Mathf.DeltaAngle(fresh[s].yaw, m.spotYaws[s])) > 0.5f)
                    e.Add("Spot " + r.id + " stale: route changed? saved node " + m.spotNodes[s] + " fresh " + fresh[s].node + " (rerun Course/80)");
                // 実行時と同じ候補の並び (基準 → 1.5 m × 6 → 3 m × 12 → ノード ±1〜5)。開始角は playerId 1 の場合
                var c = m.spotPositions[s]; float start = 137.5f;
                var cand = new List<Vector3>();
                for (int k = 0; k < 6; k++) { float a = (start + k * 60f) * Mathf.Deg2Rad; cand.Add(c + new Vector3(Mathf.Sin(a) * 1.5f, 0, Mathf.Cos(a) * 1.5f)); }
                for (int k = 0; k < 12; k++) { float a = (start + k * 30f) * Mathf.Deg2Rad; cand.Add(c + new Vector3(Mathf.Sin(a) * 3f, 0, Mathf.Cos(a) * 3f)); }
                if (r.node >= 0) for (int d = 1; d <= 5; d++) { cand.Add(n[(r.node + d) % n.Length]); cand.Add(n[(r.node - d + n.Length) % n.Length]); }
                r.candidatesTotal = cand.Count; r.candidatesPassed = cand.Count(q => LandingSpot(q, river, keep, out _, out _));
                if (r.warpEnabled && r.candidatesPassed < 3) e.Add("Spot " + r.id + " has only " + r.candidatesPassed + " passing alternatives");
                report.spots.Add(r);
            }
            // 上段・副画面・暗転・地図テクスチャ (段階 2)
            var scroll = (RectTransform)p.tabScrolls[KD_TreatPouch.TabWalk].transform;
            // DES-UI-006: 2 列 (Course/120) では上段の帯が無く、枠の上端は 190 (配置の詳細は Course/121)
            if (KH_WalkTabLayoutEditor.IsApplied(p))
                Require(e, Mathf.Abs(scroll.anchoredPosition.y + scroll.sizeDelta.y / 2 - KH_WalkTabLayoutEditor.AreaTop) < 0.5f, "walk frame top = " + KH_WalkTabLayoutEditor.AreaTop + " (two-column walk tab, no map row above)");
            else
                Require(e, Mathf.Abs(scroll.anchoredPosition.y + scroll.sizeDelta.y / 2 - ContentTopWithRow) < 0.5f, "walk scroll top = 140 (below the map row)");
            KH_CourseTravelMapEditor.Validate(p, e, report);
            KH_CourseTravelJoinEditor.Validate(p, e, report);   // 段階 3: 受付箱・通知板・人の一覧
            KH_CourseTravelNameFontEditor.Validate(p, e, report);   // 段階 4: 表示名の補助フォント
            // 翻訳・字形・収まり
            var catalog = CourseCatalog(p.language);
            foreach (var lang in catalog.languages) foreach (var entry in lang.texts)
                if (p.language._TextForLanguage(entry.key, lang.code) != entry.text) { e.Add("Translation differs " + lang.code + ":" + entry.key + " (rerun Course/80)"); break; }
            try { KD_LocalizationEditor.ValidateGlyphs(catalog, p.sectionTitleLabel.font); }
            catch (Exception ex) { e.Add("Glyphs: " + ex.Message + " (run Course/27 - Add Course UI Glyphs)"); }
            MeasureLabels(p, e, report);
            // `_` の規約 (新設の 3 ファイル)
            CheckUnderscore(e);
        }

        static void MeasureLabels(KD_TreatPouch p, List<string> e, Report report)
        {
            var go = new GameObject("TravelMeasure", typeof(RectTransform)); go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var sample = go.AddComponent<TextMeshProUGUI>();
                foreach (string code in new[] { "ja", "en" })
                {
                    foreach (var key in new[] { "waiting", "arrived", "noanswer", "changed", "busy", "cooling", "ball", "crowded", "here", "unavailable", "stayed", "canceled", "preparing", "dogs", "stop.noanswer", "stop.changed", "stop.blocked" })
                        Measure(sample, p.feedbackLabel, p.language._TextForLanguage("course.travel." + key, code).Replace("{0}", p.language._TextForLanguage("course.walk.upper", code)), code + " note " + key, e, report);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static void Measure(TextMeshProUGUI sample, TMP_Text target, string text, string name, List<string> e, Report report)
        {
            int result = KD_LocalizationEditor.MeasureLabelFit(sample, target, text, out Vector2 preferred);
            report.labelFit.Add(name + " preferred=" + preferred + " rect=" + target.rectTransform.rect.size + " fit=" + result);
            if (result == 2) e.Add("Label overflow: " + name);
        }

        static void CheckUnderscore(List<string> e)
        {
            foreach (var path in new[] {
                "Assets/KominkaHillside/Scripts/KH_LeashManager.Travel.cs",
                "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.HillsideTravel.cs",
                "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatPouch.Travel.cs",
                "Assets/KominkaHillside/Scripts/KH_TravelFade.cs",
                "Assets/KominkaHillside/Scripts/KH_TravelMailbox.cs",
                "Assets/KominkaHillside/Scripts/KH_TravelNotice.cs" })
            {
                var lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    // VRChat のイベントの override (OnPlayerRestored など) は対象外
                    var mm = Regex.Match(lines[i], @"public\s+(?!override\b)\w+(?:\[\])?\s+([A-Za-z]\w*)\s*\(");
                    if (!mm.Success || mm.Groups[1].Value.StartsWith("_")) continue;
                    bool callable = i > 0 && lines[i - 1].Contains("[NetworkCallable");
                    if (!callable) e.Add("Public method without '_' and without [NetworkCallable]: " + path + ":" + (i + 1) + " " + mm.Groups[1].Value);
                }
            }
        }

        // ============================================================
        // 共通
        // ============================================================

        public static KD_TreatPouch FindPouch() { return Pouch(); }

        static KD_TreatPouch Pouch()
        {
            var all = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true);
            if (all.Length != 1) throw new InvalidOperationException("Exactly one menu required");
            var p = all[0];
            if (p.manager == null || p.manager.route == null || !p.manager.route.closedLoop || p.language == null || p.tabPages == null)
                throw new InvalidOperationException("Course route/language/menu not wired");
            return p;
        }

        static KD_LocalizationEditor.Catalog CourseCatalog(KD_Language language)
        {
            var c = JsonUtility.FromJson<KD_LocalizationEditor.Catalog>(File.ReadAllText(KH_CourseMenuEditor.CatalogPath));
            KD_LocalizationEditor.Validate(c);
            if (!language.languageCodes.SequenceEqual(c.languages.Select(l => l.code))) throw new InvalidOperationException("Course/base language order differs");
            if (KD_LocalizationEditor.IncompleteLanguages(c).Length != 0) throw new InvalidOperationException("Incomplete course translations");
            var keys = c.languages.First(l => l.code == c.fallbackLanguage).texts.Select(x => x.key).ToArray();
            if (keys.Any(k => language.keys.Contains(k))) throw new InvalidOperationException("Course catalog cannot override base keys");
            return c;
        }

        static void Require(List<string> e, bool ok, string what) { if (!ok) e.Add("FAIL " + what); }

        static void CheckInts(List<string> e, VRC.Udon.UdonBehaviour b, string field, int[] proxy, int initial)
        {
            if (proxy == null || proxy.Length != 6 || proxy.Any(v => v != initial)) e.Add("proxy " + field + " must be length 6 of " + initial);
            if (b != null && b.publicVariables.TryGetVariableValue(field, out object v))
                if (!(v is int[] a) || a.Length != 6 || a.Any(x => x != initial)) e.Add("backing " + field + " must be length 6 of " + initial);
        }

        static void CheckFloats(List<string> e, VRC.Udon.UdonBehaviour b, string field, float[] proxy, float[] expected)
        {
            if (proxy == null || proxy.Length != expected.Length) { e.Add("proxy " + field + " length " + (proxy == null ? -1 : proxy.Length) + " != " + expected.Length); return; }
            for (int i = 0; i < expected.Length; i++) if (Mathf.Abs(proxy[i] - expected[i]) > 0.001f) { e.Add("proxy " + field + "[" + i + "] differs from the generator"); return; }
            if (b == null || !b.publicVariables.TryGetVariableValue(field, out object v) || !(v is float[] a) || a.Length != expected.Length) { e.Add("backing " + field + " missing or wrong length"); return; }
            for (int i = 0; i < expected.Length; i++) if (Mathf.Abs(a[i] - expected[i]) > 0.001f) { e.Add("backing " + field + "[" + i + "] differs from the generator"); return; }
        }

        static void CheckArray(List<string> e, VRC.Udon.UdonBehaviour b, string field, Array expected)
        {
            if (b == null || !b.publicVariables.TryGetVariableValue(field, out object value) || !(value is Array actual) || actual.Length != expected.Length) { e.Add("Backing array " + field); return; }
            for (int i = 0; i < expected.Length; i++) if (!Equals(expected.GetValue(i), actual.GetValue(i))) { e.Add("Backing value " + field + "[" + i + "]"); return; }
        }

        static void SetInts(SerializedObject so, string name, int[] v) { var a = Prop(so, name, v.Length); for (int i = 0; i < v.Length; i++) a.GetArrayElementAtIndex(i).intValue = v[i]; }
        static void SetFloats(SerializedObject so, string name, float[] v) { var a = Prop(so, name, v.Length); for (int i = 0; i < v.Length; i++) a.GetArrayElementAtIndex(i).floatValue = v[i]; }
        static void SetBools(SerializedObject so, string name, bool[] v) { var a = Prop(so, name, v.Length); for (int i = 0; i < v.Length; i++) a.GetArrayElementAtIndex(i).boolValue = v[i]; }
        static void SetStrings(SerializedObject so, string name, string[] v) { var a = Prop(so, name, v.Length); for (int i = 0; i < v.Length; i++) a.GetArrayElementAtIndex(i).stringValue = v[i]; }
        static void SetVectors(SerializedObject so, string name, Vector3[] v) { var a = Prop(so, name, v.Length); for (int i = 0; i < v.Length; i++) a.GetArrayElementAtIndex(i).vector3Value = v[i]; }
        static SerializedProperty Prop(SerializedObject so, string name, int length)
        {
            var a = so.FindProperty(name);
            if (a == null || !a.isArray) throw new InvalidOperationException("Missing serialized array " + name + " (compile first)");
            a.arraySize = length; return a;
        }
    }

    /// <summary>試験用の値 (debug*) が 0 でない Manager、地点のデータが無い Manager をビルドさせない。</summary>
    public class KH_CourseTravelBuildGuard : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => 12;
        public bool OnBuildRequested(VRCSDKRequestedBuildType type)
        {
            if (type == VRCSDKRequestedBuildType.Avatar) return true;
            foreach (var m in UnityEngine.Object.FindObjectsOfType<KH_LeashManager>(true))
            {
                if (m.route == null || !m.route.closedLoop) continue;
                bool debugOff = m.debugDropTravel == 0 && m.debugDropTravelDone == 0 && m.debugSkipTravelTeleport == 0 && m.debugForgeTravelMask == 0 && m.debugDelayTravel == 0f && m.debugDelayTravelMoved == 0f && m.debugDropTravelMoved == 0
                    && UnityEngine.Object.FindObjectsOfType<KH_TravelMailbox>(true).All(b => b.debugJoinLoopback == 0 && b.debugAnswerCode == 0 && b.debugIncomingFrom == 0 && b.debugIncomingKind == 0 && b.debugIncomingSeq == 0 && b.debugCrossInvert == 0);   // IMP-WLD-002 の試験用の入口も
                bool data = m.spotPositions != null && m.spotPositions.Length == 6 && m.spotWarpEnabled != null && m.spotWarpEnabled.Length == 6 && m.travelRiverXs != null && m.travelRiverXs.Length > 0
                    && m.travelKeepOutCenters != null && m.travelKeepOutRadii != null && m.travelKeepOutCenters.Length > 0 && m.travelKeepOutCenters.Length == m.travelKeepOutRadii.Length;   // IMP-WLD-004
                if (!debugOff || !data)
                {
                    Debug.LogError("[KH Travel] Build blocked: " + (!debugOff ? "debug travel hooks must be 0. " : "") + (!data ? "travel spots missing - run Course/80 and Course/81." : ""));
                    return false;
                }
            }
            return true;
        }
    }
}
