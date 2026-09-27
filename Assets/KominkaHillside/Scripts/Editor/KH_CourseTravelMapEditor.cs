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

namespace KominkaHillside.Editor
{
    /// <summary>IMP-WLD-001 段階 2: 地図の副画面と暗転 (設計書 §A.1〜A.3・§D.1・§D.2・§D.7・§D.8)。
    ///
    /// Course/84 は次を行い、本番シーンを保存する (経路・目的地の行・禁止された再生成メニューには触らない):
    /// 1. 地図テクスチャ (512²) を周回路 436 ノード・KH_RiverData (無ければ生成式)・6 地点から描いて PNG にし、インポート設定を書く
    ///    (ミップマップなし・Clamp・PC DXT1・Android/iOS ASTC 6x6)。北 (+Z) が上。文字は焼かない。
    /// 2. 暗転の板 KH_TravelFade (KH_NightMode と同じ全画面 quad + KH_TravelFade.mat、queue 5001) を置き、Manager の travelFade に配線する。
    ///    KH_TravelFade の Program Asset が無ければ作ってコンパイルを始め、**シーンは変えずに止まる** (もう一度 84 を流す)。
    /// 3. さんぽタブ上段の段階 1 の 2 ボタンを [地図と移動] 1 つに置き換え、副画面 Page_Settings/TravelMapPage を作る。
    /// 4. 翻訳表 (course.travel.map.* を含む) を KD_Language の extension に入れ直す。
    /// 検査は Course/81 (KH_CourseTravelEditor.Verify) が <see cref="Validate"/> を呼んで行う。</summary>
    public static class KH_CourseTravelMapEditor
    {
        const string OutDir = "Temp/imp-wld-001";
        public const string ProgramPath = "Assets/KominkaHillside/Scripts/KH_TravelFade";
        public const string TexturePath = "Assets/KominkaHillside/Textures/Map/KH_TravelMap.png";
        public const string FadeShaderName = "KominkaHillside/KH_TravelFade";
        public const string FadeMaterialPath = "Assets/KominkaHillside/Materials/KH_TravelFade.mat";
        public const string FadeRootName = "KH_TravelFade", FadeOverlayName = "Overlay";
        public const string OpenName = "TravelMapOpen", PageName = "TravelMapPage";
        public const int TextureSize = 512;
        // 地図の正方形が覆う世界: x -60..660 / z 0..720 (コース x 0〜600・z 0〜720 を縦いっぱいに)
        public const float CenterX = 300f, CenterZ = 360f, Span = 720f;
        public const float MapSize = 380f;
        public const int FadeSortingOrder = 32767;
        static readonly Vector2 MapPos = new Vector2(-350f, -55f);
        static readonly string[] OldButtons = { "TravelUpper", "TravelRiver" };
        static readonly string[] RowEvents = { "_TravelPick0", "_TravelPick1" };
        static readonly int[] RowSpots = { KH_LeashManager.SpotUpper, KH_LeashManager.SpotRiver };
        static readonly string[] RowNameKeys = { "course.walk.upper", "course.walk.river" };

        [Serializable] class BuildReport
        {
            public string utc, scene, status, reason, river;
            public bool programAssetCreated;
            public long pngBytes;
            public int droppedNetworkIds;
        }

        [MenuItem("Tools/KominkaHillside/Course/84 - Build Travel Map and Fade")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var report = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            Directory.CreateDirectory(OutDir);
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath + ".asset") == null)
            {
                var created = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                created.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(ProgramPath + ".cs");
                if (created.sourceCsScript == null) throw new InvalidOperationException("KH_TravelFade.cs not imported yet; refresh first");
                AssetDatabase.CreateAsset(created, ProgramPath + ".asset"); AssetDatabase.SaveAssets();
                UdonSharpProgramAsset.CompileAllCsPrograms(true);
                report.status = "PROGRAM_ASSET_CREATED"; report.programAssetCreated = true;
                report.reason = "Created KH_TravelFade.asset. Wait for the U# compile (unity-compile.sh --udon), then run Course/84 again. Scene unchanged.";
                File.WriteAllText(OutDir + "/travel-map-build.json", JsonUtility.ToJson(report, true));
                Debug.Log("[KH Travel Map] " + report.reason);
                return;
            }
            var p = KH_CourseTravelEditor.FindPouch(); var m = p.manager;
            if (m.spotPositions == null || m.spotPositions.Length != 6) throw new InvalidOperationException("Travel spots missing: run Course/80 first");
            var quad = AssetDatabase.LoadAssetAtPath<Mesh>(KH_NightModeEditor.QuadMeshPath);
            if (quad == null) throw new InvalidOperationException("Full-screen quad missing (" + KH_NightModeEditor.QuadMeshPath + "); the night setup owns it");
            var shader = Shader.Find(FadeShaderName); if (shader == null) throw new InvalidOperationException("Shader " + FadeShaderName + " not found (import first)");
            var pouchBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            if (pouchBacking == null || p.settingsPage == null || p.tabPages == null) throw new InvalidOperationException("Menu backing / settings page missing");
            var font = p.sectionTitleLabel != null ? p.sectionTitleLabel.font : null;
            if (font == null) throw new InvalidOperationException("Shared UI font missing");

            // ---- 1. 地図テクスチャ (シーンの外。先に作る) ----
            var texture = WriteTexture(m, report);

            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-travel-map.unity", true);
            var log = new StringBuilder();
            try
            {
                // ---- 2. 暗転の板 ----
                var mat = AssetDatabase.LoadAssetAtPath<Material>(FadeMaterialPath);
                if (mat == null) { mat = new Material(shader) { name = "KH_TravelFade" }; AssetDatabase.CreateAsset(mat, FadeMaterialPath); }
                mat.shader = shader; mat.renderQueue = KH_TravelFade.OverlayQueue; mat.SetFloat("_Alpha", 0f); EditorUtility.SetDirty(mat);
                var scene = p.gameObject.scene;
                var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == FadeRootName) ?? new GameObject(FadeRootName);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); root.transform.localScale = Vector3.one; root.SetActive(true);
                var overlayT = root.transform.Find(FadeOverlayName);
                var overlay = overlayT != null ? overlayT.gameObject : new GameObject(FadeOverlayName);
                overlay.transform.SetParent(root.transform, false);
                var mf = overlay.GetComponent<MeshFilter>(); if (mf == null) mf = overlay.AddComponent<MeshFilter>(); mf.sharedMesh = quad;
                var mr = overlay.GetComponent<MeshRenderer>(); if (mr == null) mr = overlay.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat; mr.enabled = false;
                mr.sortingOrder = FadeSortingOrder;   // W2 D1: 同じ queue 5000 のライブの粒より後に描く (頭の位置へ寄せるのと併用)
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                var fade = root.GetComponent<KH_TravelFade>(); if (fade == null) fade = root.AddUdonSharpComponent<KH_TravelFade>();
                var fadeBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(fade);
                if (fadeBacking == null) throw new InvalidOperationException("KH_TravelFade backing missing (compile U# and run again)");
                if (fadeBacking.SyncMethod != Networking.SyncType.None) { fadeBacking.SyncMethod = Networking.SyncType.None; EditorUtility.SetDirty(fadeBacking); }
                fade.overlayRenderer = mr; fade.fadeOutSeconds = 0.2f; fade.fadeInSeconds = 0.3f; fade.blackTimeout = 3f;
                UdonSharpEditorUtility.CopyProxyToUdon(fade); EditorUtility.SetDirty(fade);
                m.travelFade = fade; UdonSharpEditorUtility.CopyProxyToUdon(m); EditorUtility.SetDirty(m);
                var descriptor = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
                if (descriptor != null) { int dropped = descriptor.NetworkIDCollection.RemoveAll(x => x.gameObject == null && (x.SerializedTypeNames == null || x.SerializedTypeNames.Count == 0)); if (dropped > 0) { report.droppedNetworkIds = dropped; EditorUtility.SetDirty(descriptor); } }

                // ---- 3. さんぽタブ上段と副画面 ----
                var walk = p.tabPages[KD_TreatPouch.TabWalk].transform;
                foreach (var name in OldButtons) { var old = walk.Find(name); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject); }
                var open = KD_ShopSetupEditor.EnsureButton(walk, OpenName, new Vector2(-272f, KH_CourseTravelEditor.TopRowY), new Vector2(530f, 40f),
                    KD_UiSpriteEditor.ActionNeutral, font, Ja("course.travel.map.open"), pouchBacking, "_TravelOpenMap", log);
                var openLabel = Label(open, 20f);
                KH_CourseTravelEditor.LowerScroll(p);

                var page = KD_ShopSetupEditor.EnsureUiChild(p.settingsPage.transform, PageName, Vector2.zero, new Vector2(KD_TreatSetupEditor.PanelWidth, KD_TreatSetupEditor.PanelHeight));
                var heading = MakeText(page.transform, "Heading", new Vector2(-150f, 164f), new Vector2(560f, 36f), 24f, TextAlignmentOptions.Left, font, Ja("course.travel.map.title"));
                var back = KD_ShopSetupEditor.EnsureButton(page.transform, "Back", new Vector2(450f, 164f), new Vector2(220f, 44f), KD_UiSpriteEditor.ActionNeutral, font, Ja("course.travel.map.back"), pouchBacking, "_TravelMapBack", log);
                var backLabel = Label(back, 20f);
                var mapGo = KD_ShopSetupEditor.EnsureUiChild(page.transform, "Map", MapPos, new Vector2(MapSize, MapSize));
                var raw = mapGo.GetComponent<RawImage>(); if (raw == null) raw = mapGo.AddComponent<RawImage>(); raw.texture = texture; raw.raycastTarget = false; raw.color = Color.white;
                // 地点の番号 (1〜6)。目印 (● / ○) はテクスチャに焼いてある
                for (int s = 0; s < 6; s++)
                {
                    var at = MapPoint(m.spotPositions[s]) + new Vector2(14f, 12f);
                    var mark = MakeText(page.transform, "Mark" + (s + 1), at, new Vector2(28f, 26f), 18f, TextAlignmentOptions.Center, font, (s + 1).ToString());
                    mark.color = new Color(.16f, .13f, .11f); mark.raycastTarget = false;
                }
                var legend = MakeText(page.transform, "Legend", new Vector2(MapPos.x, -270f), new Vector2(MapSize, 26f), 16f, TextAlignmentOptions.Center, font, Ja("course.travel.map.legend"));
                var circle = AssetDatabase.LoadAssetAtPath<Sprite>(KD_UiSpriteEditor.CircleSpritePath);
                var ring = Img(page.transform, "Selection", Vector2.zero, new Vector2(30f, 30f), new Color(1f, .82f, .2f, .75f), circle);
                var player = KD_ShopSetupEditor.EnsureUiChild(page.transform, "Player", MapPos, new Vector2(20f, 20f));
                Img(player.transform, "Dot", Vector2.zero, new Vector2(16f, 16f), new Color(.15f, .45f, .95f), circle);
                Img(player.transform, "Nose", new Vector2(0f, 11f), new Vector2(5f, 12f), Color.white, null);
                var rows = new Button[2]; var rowImages = new Image[2]; var rowLabels = new TextMeshProUGUI[2];
                for (int k = 0; k < 2; k++)
                {
                    var row = KD_ShopSetupEditor.EnsureButton(page.transform, "Row" + k, new Vector2(155f, 98f - k * 92f), new Vector2(590f, 84f),
                        KD_UiSpriteEditor.ActionNeutral, font, (RowSpots[k] + 1) + "  " + Ja(RowNameKeys[k]), pouchBacking, RowEvents[k], log);
                    rows[k] = row.GetComponent<Button>(); rowImages[k] = row.GetComponent<Image>();
                    rowLabels[k] = Label(row, 24f); rowLabels[k].alignment = TextAlignmentOptions.Left;
                }
                // W2 U10: ワープ先 / 犬の数 / 散歩先のまま の 3 行 (一覧の下 -46 〜 実行ボタンの上 -150)
                var footer = MakeText(page.transform, "Footer", new Vector2(155f, -98f), new Vector2(590f, 104f), 18f, TextAlignmentOptions.Left, font, Ja("course.travel.map.pick"));
                var go = KD_ShopSetupEditor.EnsureButton(page.transform, "Go", new Vector2(155f, -205f), new Vector2(500f, 90f), KD_UiSpriteEditor.ActionNeutral, font, Ja("course.travel.map.go"), pouchBacking, "_TravelGo", log);
                var goLabel = Label(go, 26f);
                var status = MakeText(page.transform, "Status", new Vector2(155f, -285f), new Vector2(590f, 40f), 20f, TextAlignmentOptions.Center, font, "");
                // 輪と自分は地図の上に描く (兄弟の順で後ろ)
                ring.transform.SetAsLastSibling(); player.transform.SetAsLastSibling();
                page.SetActive(false);
                var pages = new List<GameObject>();
                foreach (var e in p.extraSettingsPages ?? new GameObject[0]) if (e != null && e != page && !pages.Contains(e)) pages.Add(e);
                pages.Add(page);
                p.extraSettingsPages = pages.ToArray();

                p.travelOpenButton = open; p.travelOpenLabel = openLabel; p.travelMapPage = page; p.travelMapHeading = heading; p.travelBackLabel = backLabel;
                p.travelMapRect = (RectTransform)mapGo.transform; p.travelMapPlayer = (RectTransform)player.transform; p.travelMapSelection = (RectTransform)ring.transform;
                p.travelMapCenterX = CenterX; p.travelMapCenterZ = CenterZ; p.travelMapSpan = Span;
                p.travelButtons = rows; p.travelImages = rowImages; p.travelLabels = rowLabels;
                p.travelLegendLabel = legend;
                p.travelFooterLabel = footer; p.travelGoImage = go.GetComponent<Image>(); p.travelGoLabel = goLabel; p.travelStatusLabel = status;
                ring.gameObject.SetActive(false);

                // ---- 4. 翻訳表 ----
                KH_CourseTravelEditor.ApplyCatalog(p);
                UdonSharpEditorUtility.CopyProxyToUdon(p); EditorUtility.SetDirty(p);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Travel map build save failed");
                AssetDatabase.SaveAssets();
            }
            catch
            {
                Debug.LogError("[KH Travel Map] Build failed; the scene before this run is at " + OutDir + "/before-travel-map.unity. Reopen the scene without saving to discard partial changes.");
                throw;
            }
            report.status = "BUILT";
            File.WriteAllText(OutDir + "/travel-map-build.json", JsonUtility.ToJson(report, true));
            Debug.Log("[KH Travel Map] BUILT texture=" + TexturePath + " (" + report.pngBytes + " bytes png, river " + report.river + "). Run Course/27 (glyphs) then Course/81.");
        }

        // ============================================================
        // 地図テクスチャ (§D.8)
        // ============================================================

        static Texture2D WriteTexture(KH_LeashManager m, BuildReport report)
        {
            int n = TextureSize;
            var px = new Color[n * n];
            Color ground = new Color(.86f, .83f, .70f), outside = new Color(.72f, .70f, .60f), water = new Color(.45f, .63f, .80f);
            Color trail = new Color(.56f, .41f, .28f), wood = new Color(.38f, .26f, .16f), warp = new Color(.82f, .30f, .20f), mark = new Color(.25f, .22f, .20f);
            var riverData = UnityEngine.Object.FindObjectOfType<KH_RiverData>(true);
            bool useData = riverData != null && riverData.centerX != null && riverData.halfWidth != null && riverData.centerX.Length > 1 && riverData.halfWidth.Length == riverData.centerX.Length;
            report.river = useData ? "KH_RiverData" : "KH_CourseGroundEditor formula (KH_RiverData not built)";
            float ppm = n / Span;
            for (int y = 0; y < n; y++)
            {
                float z = WorldZ(y + .5f);
                float cx, hw;
                if (useData)
                {
                    float f = Mathf.Clamp((z - riverData.zStart) / riverData.zStep, 0f, riverData.centerX.Length - 1.001f);
                    int i = (int)f; float t = f - i;
                    cx = Mathf.Lerp(riverData.centerX[i], riverData.centerX[i + 1], t); hw = Mathf.Lerp(riverData.halfWidth[i], riverData.halfWidth[i + 1], t);
                }
                else { cx = KH_CourseGroundEditor.RiverAt(z).x; hw = KH_CourseGroundEditor.HalfWidth(z); }
                float half = Mathf.Max(hw, 1.6f / ppm);   // 地図の上で最低 3 px 強の太さ
                for (int x = 0; x < n; x++)
                {
                    float wx = WorldX(x + .5f);
                    Color c = wx < 0f || wx > 600f || z < 0f || z > 720f ? outside : ground;
                    float d = Mathf.Abs(wx - cx) - half;
                    if (d < 1f / ppm) c = Color.Lerp(water, c, Mathf.Clamp01(d * ppm));
                    px[y * n + x] = c;
                }
            }
            var nodes = m.route.nodePositions;
            for (int i = 0; i < nodes.Length; i++) Segment(px, n, nodes[i], nodes[(i + 1) % nodes.Length], 1.6f, trail);
            foreach (float z in new[] { 190f, 490f })
            {
                float x = useData ? riverData._CenterX(z) : KH_CourseGroundEditor.RiverAt(z).x;
                Segment(px, n, new Vector3(x - 9f, 0, z), new Vector3(x + 9f, 0, z), 2.2f, wood);
            }
            for (int s = 0; s < 6; s++)
            {
                var c = Pixel(m.spotPositions[s]);
                if (m.spotWarpEnabled[s]) { Disc(px, n, c, 9f, Color.white); Disc(px, n, c, 7f, warp); }
                else { Disc(px, n, c, 7.5f, mark); Disc(px, n, c, 5f, ground); }
            }
            var tex = new Texture2D(n, n, TextureFormat.RGB24, false);
            tex.SetPixels(px); tex.Apply(false);
            var png = tex.EncodeToPNG(); UnityEngine.Object.DestroyImmediate(tex);
            Directory.CreateDirectory(Path.GetDirectoryName(TexturePath));
            File.WriteAllBytes(TexturePath, png); report.pngBytes = png.Length;
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true; importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear; importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaSource = TextureImporterAlphaSource.None; importer.isReadable = false; importer.maxTextureSize = n;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Standalone", overridden = true, maxTextureSize = n, format = TextureImporterFormat.DXT1 });
            foreach (var platform in new[] { "Android", "iPhone" })
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = platform, overridden = true, maxTextureSize = n, format = TextureImporterFormat.ASTC_6x6 });
            importer.SaveAndReimport();
            var loaded = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (loaded == null) throw new IOException("Map texture import failed");
            return loaded;
        }

        static float WorldX(float pixel) { return CenterX - Span / 2f + pixel / TextureSize * Span; }
        static float WorldZ(float pixel) { return CenterZ - Span / 2f + pixel / TextureSize * Span; }
        static Vector2 Pixel(Vector3 w) { return new Vector2((w.x - (CenterX - Span / 2f)) / Span * TextureSize, (w.z - (CenterZ - Span / 2f)) / Span * TextureSize); }

        /// <summary>地図の RawImage (中心が原点) の上の位置。KD_TreatPouch.TravelMapPoint と同じ式 + 地図の位置。</summary>
        public static Vector2 MapPoint(Vector3 w)
        {
            float u = Mathf.Clamp((w.x - CenterX) / Span, -.5f, .5f), v = Mathf.Clamp((w.z - CenterZ) / Span, -.5f, .5f);
            return MapPos + new Vector2(u * MapSize, v * MapSize);
        }

        static void Segment(Color[] px, int n, Vector3 a3, Vector3 b3, float radius, Color color)
        {
            Vector2 a = Pixel(a3), b = Pixel(b3);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - radius - 1)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + radius + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - radius - 1)), y1 = Mathf.Min(n - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + radius + 1));
            Vector2 ab = b - a; float len2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                var q = new Vector2(x + .5f, y + .5f);
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / len2);
                float d = Vector2.Distance(q, a + ab * t);
                float cover = Mathf.Clamp01(radius + .5f - d);
                if (cover > 0f) px[y * n + x] = Color.Lerp(px[y * n + x], color, cover);
            }
        }

        static void Disc(Color[] px, int n, Vector2 c, float radius, Color color)
        {
            for (int y = Mathf.Max(0, (int)(c.y - radius - 2)); y <= Mathf.Min(n - 1, (int)(c.y + radius + 2)); y++)
                for (int x = Mathf.Max(0, (int)(c.x - radius - 2)); x <= Mathf.Min(n - 1, (int)(c.x + radius + 2)); x++)
                {
                    float cover = Mathf.Clamp01(radius + .5f - Vector2.Distance(new Vector2(x + .5f, y + .5f), c));
                    if (cover > 0f) px[y * n + x] = Color.Lerp(px[y * n + x], color, cover);
                }
        }

        // ============================================================
        // Course/81 から呼ぶ検査
        // ============================================================

        public static void Validate(KD_TreatPouch p, List<string> e, KH_CourseTravelEditor.Report report)
        {
            var m = p.manager;
            // 暗転の板
            var fade = UnityEngine.Object.FindObjectsOfType<KH_TravelFade>(true);
            Req(e, fade.Length == 1, "exactly one KH_TravelFade");
            if (fade.Length == 1)
            {
                var f = fade[0]; var fb = UdonSharpEditorUtility.GetBackingUdonBehaviour(f);
                Req(e, fb != null && fb.SyncMethod == Networking.SyncType.None, "KH_TravelFade sync=None");
                Req(e, f.gameObject.activeInHierarchy, "KH_TravelFade active (Update must run)");
                var r = f.overlayRenderer as MeshRenderer;
                Req(e, r != null && !r.enabled && r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh == AssetDatabase.LoadAssetAtPath<Mesh>(KH_NightModeEditor.QuadMeshPath), "fade overlay = disabled full-screen quad");
                var mat = r != null ? r.sharedMaterial : null;
                Req(e, mat != null && AssetDatabase.GetAssetPath(mat) == FadeMaterialPath && mat.shader != null && mat.shader.name == FadeShaderName && mat.renderQueue == KH_TravelFade.OverlayQueue && mat.renderQueue <= 5000, "fade material " + FadeMaterialPath + " queue " + KH_TravelFade.OverlayQueue + " (<= 5000, W2 D1)");
                Req(e, r != null && r.sortingOrder == FadeSortingOrder, "fade renderer sortingOrder " + FadeSortingOrder);
                Req(e, Mathf.Approximately(f.fadeOutSeconds, .2f) && Mathf.Approximately(f.fadeInSeconds, .3f), "fade 0.2 s / 0.3 s");
                Req(e, m.travelFade == f, "manager.travelFade wired (proxy)");
                var mb = UdonSharpEditorUtility.GetBackingUdonBehaviour(m);
                if (mb != null && mb.publicVariables.TryGetVariableValue("travelFade", out object v)) Req(e, ReferenceEquals(v, fb), "manager.travelFade wired (backing)");
                else e.Add("FAIL manager.travelFade missing in backing");
            }
            // テクスチャ
            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            Req(e, importer != null && tex != null, "map texture " + TexturePath);
            if (importer != null && tex != null)
            {
                Req(e, tex.width == TextureSize && tex.height == TextureSize, "map 512x512");
                Req(e, !importer.mipmapEnabled && importer.wrapMode == TextureWrapMode.Clamp && importer.sRGBTexture && !importer.isReadable, "map: no mipmaps, Clamp, sRGB, not readable");
                var pc = importer.GetPlatformTextureSettings("Standalone");
                Req(e, pc.overridden && pc.format == TextureImporterFormat.DXT1 && pc.maxTextureSize == TextureSize, "map PC DXT1 512");
                foreach (var platform in new[] { "Android", "iPhone" })
                {
                    var s = importer.GetPlatformTextureSettings(platform);
                    Req(e, s.overridden && s.format == TextureImporterFormat.ASTC_6x6 && s.maxTextureSize == TextureSize, "map " + platform + " ASTC 6x6 512");
                }
                report.mapPngBytes = new FileInfo(TexturePath).Length;
                report.mapGpuBytesPC = TextureSize * TextureSize / 2;                                        // DXT1 = 4 bit/px
                report.mapGpuBytesMobile = (long)Mathf.CeilToInt(TextureSize / 6f) * Mathf.CeilToInt(TextureSize / 6f) * 16; // ASTC 6x6 = 16 byte/block
                report.mapEditorMemoryBytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
                report.mapFormats = "Standalone DXT1 / Android ASTC_6x6 / iPhone ASTC_6x6, no mipmaps";
            }
            // 画面
            var walk = p.tabPages[KD_TreatPouch.TabWalk].transform;
            foreach (var name in OldButtons) Req(e, walk.Find(name) == null, "stage-1 button removed: " + name);
            var pb = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            Req(e, p.travelOpenButton != null && p.travelOpenButton.transform.parent == walk && p.travelOpenButton.name == OpenName, "open button under Page_Walk");
            if (p.travelOpenButton != null)
            {
                CheckEvent(e, p.travelOpenButton, pb, "_TravelOpenMap");
                var rt = (RectTransform)p.travelOpenButton.transform;
                Req(e, rt.anchoredPosition.y - rt.sizeDelta.y / 2 >= KH_CourseTravelEditor.ContentTopWithRow, "open button above the walk scroll");
            }
            Req(e, p.travelMapPage != null && p.travelMapPage.transform.parent == p.settingsPage.transform && p.extraSettingsPages != null && p.extraSettingsPages.Contains(p.travelMapPage) && !p.travelMapPage.activeSelf, "map page registered in extraSettingsPages, inactive at start");
            // W2 M6: backing の extraSettingsPages にも入っていること
            if (pb == null || !pb.publicVariables.TryGetVariableValue("extraSettingsPages", out object pagesObj) || !(pagesObj is GameObject[] pagesArr) || !pagesArr.Contains(p.travelMapPage))
                e.Add("FAIL map page registered in extraSettingsPages (backing)");
            Req(e, p.travelLegendLabel != null && p.travelMapPage != null && p.travelLegendLabel.transform.parent == p.travelMapPage.transform, "legend label wired (localized at runtime, W2 U9)");
            if (e.Count > 0) return;
            var page = p.travelMapPage.transform;
            Req(e, p.travelMapRect != null && p.travelMapRect.parent == page && p.travelMapRect.GetComponent<RawImage>() != null && p.travelMapRect.GetComponent<RawImage>().texture == tex, "map RawImage uses the map texture");
            Req(e, p.travelMapPlayer != null && p.travelMapPlayer.parent == page && p.travelMapSelection != null && p.travelMapSelection.parent == page, "player and selection are siblings of the map");
            Req(e, Mathf.Approximately(p.travelMapCenterX, CenterX) && Mathf.Approximately(p.travelMapCenterZ, CenterZ) && Mathf.Approximately(p.travelMapSpan, Span), "map world square");
            Req(e, p.travelButtons != null && p.travelButtons.Length == 2 && p.travelImages != null && p.travelImages.Length == 2 && p.travelLabels != null && p.travelLabels.Length == 2, "two list rows");
            if (e.Count > 0) return;
            for (int k = 0; k < 2; k++)
            {
                Req(e, p.travelButtons[k] != null && p.travelButtons[k].transform.parent == page && m.spotWarpEnabled[RowSpots[k]], "row " + k + " for a warpable spot");
                if (p.travelButtons[k] != null) CheckEvent(e, p.travelButtons[k].gameObject, pb, RowEvents[k]);
            }
            var goButton = page.Find("Go"); var backButton = page.Find("Back");
            Req(e, goButton != null && p.travelGoImage == goButton.GetComponent<Image>() && p.travelGoLabel != null, "Go button wired");
            if (goButton != null) CheckEvent(e, goButton.gameObject, pb, "_TravelGo");
            Req(e, backButton != null && p.travelBackLabel != null, "Back button wired");
            if (backButton != null) CheckEvent(e, backButton.gameObject, pb, "_TravelMapBack");
            Req(e, p.travelFooterLabel != null && p.travelStatusLabel != null && p.travelMapHeading != null && p.travelOpenLabel != null, "labels wired");
            for (int s = 0; s < 6; s++) Req(e, page.Find("Mark" + (s + 1)) != null, "map number " + (s + 1));
            foreach (var field in new[] { "travelButtons", "travelImages", "travelLabels" })
            {
                object arr;
                if (pb == null || !pb.publicVariables.TryGetVariableValue(field, out arr) || !(arr is Array a) || a.Length != 2) e.Add("FAIL backing " + field);
            }
            foreach (var field in new[] { "travelMapPage", "travelMapRect", "travelMapPlayer", "travelOpenButton", "travelGoLabel", "travelStatusLabel" })
                if (pb == null || !pb.publicVariables.TryGetVariableValue(field, out object v2) || v2 == null) e.Add("FAIL backing " + field);
            // 文言の収まり (日英)
            Measure(p, e, report);
        }

        static void Measure(KD_TreatPouch p, List<string> e, KH_CourseTravelEditor.Report report)
        {
            var go = new GameObject("TravelMapMeasure", typeof(RectTransform)); go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var sample = go.AddComponent<TextMeshProUGUI>(); var lang = p.language;
                foreach (string code in new[] { "ja", "en" })
                {
                    string T(string key) { return lang._TextForLanguage(key, code); }
                    string longest = new[] { "KD_TreatPouch.WordWalkPlaza", "KD_TreatPouch.WordWalkGate", "course.walk.forward", "course.walk.reverse", "course.walk.upper", "course.walk.river" }.Select(T).OrderByDescending(s => s.Length).First();
                    Fit(sample, p.travelOpenLabel, T("course.travel.map.open"), code + " open", e, report);
                    Fit(sample, p.travelMapHeading, T("course.travel.map.title"), code + " heading", e, report);
                    Fit(sample, p.travelBackLabel, T("course.travel.map.back"), code + " back", e, report);
                    Fit(sample, p.travelBackLabel, T("course.travel.map.backwalk"), code + " backwalk", e, report);
                    for (int k = 0; k < 2; k++)
                        Fit(sample, p.travelLabels[k], (RowSpots[k] + 1) + "  " + T(RowNameKeys[k]) + "    " + T("course.travel.map.distance").Replace("{0}", "999"), code + " row " + k, e, report);
                    Fit(sample, p.travelFooterLabel, T("course.travel.map.target").Replace("{0}", T("course.walk.upper")) + "\n" + T("course.travel.map.dogs").Replace("{0}", "6") + "\n" + T("course.travel.map.walkdest").Replace("{0}", longest), code + " footer 3 lines", e, report);
                    Fit(sample, p.travelFooterLabel, T("course.travel.map.alone"), code + " footer alone", e, report);
                    Fit(sample, p.travelFooterLabel, T("course.travel.map.pick"), code + " footer pick", e, report);
                    Fit(sample, p.travelGoLabel, T("course.travel.map.go") + " (10)", code + " go", e, report);
                    Fit(sample, p.travelLegendLabel, T("course.travel.map.legend"), code + " legend", e, report);
                    foreach (var key in new[] { "course.travel.preparing", "course.travel.dogs", "course.travel.arrived", "course.travel.stop.noanswer", "course.travel.stop.changed", "course.travel.stop.blocked", "course.travel.busy", "course.travel.cooling", "course.travel.ball", "course.travel.crowded", "course.travel.here", "course.travel.canceled", "course.travel.map.moving", "course.travel.map.pick" })
                        Fit(sample, p.travelStatusLabel, T(key).Replace("{0}", T("course.walk.upper")), code + " status " + key, e, report);
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

        // ---- UI の部品 ----
        static string Ja(string key)
        {
            var c = JsonUtility.FromJson<KD_LocalizationEditor.Catalog>(File.ReadAllText(KH_CourseMenuEditor.CatalogPath));
            return KD_LocalizationEditor.Text(c, "ja", key);
        }

        static TextMeshProUGUI Label(GameObject button, float points)
        {
            var t = button.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            t.fontSize = points; t.enableWordWrapping = false; EditorUtility.SetDirty(t);
            return t;
        }

        static TextMeshProUGUI MakeText(Transform parent, string name, Vector2 pos, Vector2 size, float points, TextAlignmentOptions align, TMP_FontAsset font, string text)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var t = go.GetComponent<TextMeshProUGUI>(); if (t == null) t = go.AddComponent<TextMeshProUGUI>();
            t.font = font; t.fontSize = points; t.alignment = align; t.enableWordWrapping = false; t.raycastTarget = false;
            t.color = Color.white; t.text = text; EditorUtility.SetDirty(t);
            return t;
        }

        static Image Img(Transform parent, string name, Vector2 pos, Vector2 size, Color color, Sprite sprite)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var i = go.GetComponent<Image>(); if (i == null) i = go.AddComponent<Image>();
            i.sprite = sprite; i.color = color; i.raycastTarget = false; EditorUtility.SetDirty(i);
            return i;
        }
    }
}
