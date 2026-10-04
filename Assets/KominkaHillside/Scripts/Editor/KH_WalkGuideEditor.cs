using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// IMP-UI-002 (docs/plans/2026-10-03-onsite-guides.md §2): the walk guide strip above each of the three leash stations
    /// (KH_LeashRack, KH_Station_Viewpoint, KH_Station_Plaza), modelled on the wash strip (KH_DogWashSetupEditor.Guide.cs).
    ///
    /// Per station a code-owned child `WalkGuide` (rebuilt every run; the station's own transform, rack, tips and plate are never
    /// touched, so a station the user moved keeps its place):
    ///   WalkGuide (station local (0, 0, 0.055) = the crossbar's centre plane; yaw 0, or 180 where the strip reads from +Z)
    ///     GuidePostL / GuidePostR  0.04 x 0.665 x 0.04 m cubes standing on the crossbar (x ±0.43, y 0.125..0.79), KH_RackWood, no collider
    ///     GuideBack                0.82 x 0.28 x 0.018 m board (y 0.49..0.77) hiding the canvas's mirrored back, KH_RackWood, no collider
    ///     Canvas                   World Space 800 x 260 px at 1 mm/px, 2 mm in front of the board, read only (no raycaster /
    ///                              VRCUiShape / collider, every raycastTarget false). Bg + Arrow1..3 shared,
    ///       Vr / Flat              the two device variants: Step1..4, Sub1..4, Help (TMP, bound to walk.guide.* through
    ///                              UILocalizationBindings.json) and Pic1..4 (RawImage cells of KH_WalkGuide.png)
    /// The station's plate (MenuPlate, KH_MenuOpener) gets vrOnly = [Vr], flatOnly = [Flat] and shows one at Start. Saved state:
    /// Vr active, Flat inactive (KD_Language writes the texts of inactive labels too).
    /// The ground under the station is the station origin - 0.8 m (KH_DogWalkingEditor builds the rack origin there).
    /// Menus: Guide/10 art (Art.cs), 11 build (here), 12 verify (Verify.cs), 14 review shots (Capture.cs).
    /// </summary>
    public static partial class KH_WalkGuideEditor
    {
        const string Menu = "Tools/KominkaHillside/Guide/";
        const string OutDir = "Temp/kh-walk-guide";
        const string BuildReportPath = OutDir + "/build.json", VerifyReportPath = OutDir + "/verify.json";
        const string WoodMatPath = "Assets/KominkaHillside/Data/Walking/KH_RackWood.mat";
        const string PlateName = "MenuPlate", CrossbarName = "Crossbar";
        public const string StripName = "WalkGuide";
        const string PostLName = "GuidePostL", PostRName = "GuidePostR", BackName = "GuideBack", CanvasName = "Canvas", BgName = "Bg";
        const string VrName = "Vr", FlatName = "Flat";

        /// <summary>The three stations: name, whether the strip reads from the station's +Z side (WalkGuide turned 180°), and what
        /// a reader comes from (the facing check). Entrance: from the spawn (-Z, south). Viewpoint: -Z faces the viewpoint place.
        /// Plaza: the station faces the plaza centre with +Z (KH_DogWalkingEditor.cs:375).</summary>
        struct StationSpec { public string name; public bool readsFromPlusZ; public string target; }
        static readonly StationSpec[] Stations =
        {
            new StationSpec { name = "KH_LeashRack", readsFromPlusZ = false, target = "spawn" },
            new StationSpec { name = "KH_Station_Viewpoint", readsFromPlusZ = false, target = "viewpoint" },
            new StationSpec { name = "KH_Station_Plaza", readsFromPlusZ = true, target = "plaza" },
        };

        // ---- geometry (station-local metres; WalkGuide sits at z CrossbarZ) ----
        const float CrossbarZ = 0.055f, CrossbarTop = 0.125f, StationGroundBelow = 0.8f;
        static readonly Vector3 PostScale = new Vector3(0.04f, 0.665f, 0.04f);
        const float PostX = 0.43f;
        static float PostY { get { return CrossbarTop + PostScale.y * 0.5f; } }   // 0.4575: from the crossbar's top to 0.79
        static readonly Vector3 BackScale = new Vector3(0.82f, 0.28f, 0.018f);
        const float StripBottom = 0.50f;                     // station local: ground + 1.30 m
        static readonly Vector2 CanvasPixels = new Vector2(800f, 260f);
        const float CanvasScale = 0.001f;
        static float CanvasY { get { return StripBottom + CanvasPixels.y * CanvasScale * 0.5f; } }   // 0.63
        static float BackY { get { return CanvasY; } }
        const float CanvasFront = 0.002f;                    // the canvas 2 mm in front of the board's face
        static float CanvasZ { get { return -(BackScale.z * 0.5f + CanvasFront); } }   // -0.011 (front = -Z of WalkGuide)

        // ---- the canvas layout (px from its centre): the wash strip's numbers (KH_DogWashSetupEditor.Guide.cs:46-52) ----
        static readonly float[] PanelX = { -300f, -100f, 100f, 300f };
        const float StepY = 106f, PicY = 24f, SubY = -56f, ArrowY = 24f;
        static readonly Vector2 StepSize = new Vector2(178f, 40f), PicSize = new Vector2(170f, 120f), SubSize = new Vector2(178f, 30f);
        static readonly float[] ArrowX = { -200f, 0f, 200f };
        static readonly Vector2 ArrowSize = new Vector2(24f, 24f);
        static readonly Vector2 HelpPos = new Vector2(0f, -110f), HelpSize = new Vector2(760f, 28f);
        const float StepPt = 22f, SubPt = 17f, HelpPt = 17f, ArrowPt = 20f, MinPt = 14f;
        const string ArrowText = "▶";
        static readonly Color InkStep = Color.white, InkSub = new Color(0.86f, 0.83f, 0.76f), InkDim = new Color(0.78f, 0.75f, 0.70f);   // the wash board's inks

        static readonly string[] LabelNames = { "Step1", "Sub1", "Step2", "Sub2", "Step3", "Sub3", "Step4", "Sub4", "Help" };
        static readonly string[] PicNames = { "Pic1", "Pic2", "Pic3", "Pic4" };
        static readonly string[] ArrowNames = { "Arrow1", "Arrow2", "Arrow3" };
        /// <summary>The keys of the 9 labels (LabelNames order) per variant. ③ is shared.</summary>
        static readonly string[] VrKeys = { "walk.guide.vr.step1", "walk.guide.vr.sub1", "walk.guide.vr.step2", "walk.guide.vr.sub2", "walk.guide.step3", "walk.guide.sub3", "walk.guide.vr.step4", "walk.guide.vr.sub4", "walk.guide.vr.help" };
        static readonly string[] FlatKeys = { "walk.guide.flat.step1", "walk.guide.flat.sub1", "walk.guide.flat.step2", "walk.guide.flat.sub2", "walk.guide.step3", "walk.guide.sub3", "walk.guide.flat.step4", "walk.guide.flat.sub4", "walk.guide.flat.help" };
        static IEnumerable<string> AllKeys { get { return VrKeys.Concat(FlatKeys).Distinct(); } }

        // ================================================================ resolve (nothing changes before everything is found)

        class Ctx
        {
            public UnityEngine.SceneManagement.Scene scene;
            public KD_Language language; public TMP_FontAsset font; public KD_LocalizationEditor.Catalog catalog;
            public Material wood;
            public Transform[] stations = new Transform[3];
            public KH_MenuOpener[] openers = new KH_MenuOpener[3];
        }

        static Ctx Resolve(bool forBuild)
        {
            var c = new Ctx { scene = EditorSceneManager.GetActiveScene() };
            var pouches = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true);
            if (pouches.Length != 1) throw new InvalidOperationException("exactly one KD_TreatPouch required (" + pouches.Length + "); nothing changed");
            c.language = pouches[0].language;
            if (c.language == null) throw new InvalidOperationException("KD_TreatPouch.language not wired (Menu/11); nothing changed");
            c.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            if (c.font == null) throw new InvalidOperationException("shared UI font missing: " + KD_ShopSetupEditor.SharedFontPath + "; nothing changed");
            c.catalog = KD_LocalizationEditor.Load();
            var missing = new List<string>();
            foreach (var lang in c.catalog.languages)
                foreach (var k in AllKeys)
                {
                    var e = lang.texts.FirstOrDefault(x => x.key == k);
                    if (e == null || string.IsNullOrWhiteSpace(e.text)) missing.Add(lang.code + ":" + k);
                }
            if (missing.Count > 0) throw new InvalidOperationException("walk guide texts missing in " + KD_MenuFinishEditor.CatalogPath + ": " + string.Join(", ", missing.Take(12)) + "; nothing changed");
            var manifest = JsonUtility.FromJson<KD_LocalizationEditor.Manifest>(File.ReadAllText(KD_LocalizationEditor.BindingsPath));
            foreach (var st in Stations)
            {
                foreach (var (group, keys) in new[] { (VrName, VrKeys), (FlatName, FlatKeys) })
                    for (int i = 0; i < LabelNames.Length; i++)
                    {
                        string path = LabelPath(group, LabelNames[i]);
                        if (!manifest.labels.Any(b => b.root == st.name && b.path == path && b.key == keys[i]))
                            throw new InvalidOperationException("binding missing in " + KD_LocalizationEditor.BindingsPath + ": " + st.name + "/" + path + " = " + keys[i] + "; nothing changed");
                    }
                var foreign = manifest.labels.Where(b => b.root == st.name && b.path.StartsWith(StripName + "/") && !IsOurPath(b.path)).Select(b => b.path).ToArray();
                if (foreign.Length > 0) throw new InvalidOperationException("bindings under " + st.name + "/" + StripName + " that this builder does not make: " + string.Join(", ", foreign) + "; nothing changed");
            }
            for (int i = 0; i < Stations.Length; i++)
            {
                var go = KD_SceneObjects.Find(Stations[i].name, out int count);
                if (go == null || count != 1) throw new InvalidOperationException(Stations[i].name + " found " + count + " times (expected 1; the localization bindings use it as their anchor); nothing changed");
                c.stations[i] = go.transform;
                if (c.stations[i].Find(CrossbarName) == null) throw new InvalidOperationException(Stations[i].name + " has no " + CrossbarName + " (not a leash station built by Walking/3); nothing changed");
                var plate = c.stations[i].Find(PlateName);
                c.openers[i] = plate != null ? plate.GetComponent<KH_MenuOpener>() : null;
                if (c.openers[i] == null) throw new InvalidOperationException(Stations[i].name + "/" + PlateName + " with KH_MenuOpener missing; nothing changed");
            }
            if (forBuild)
            {
                // KD_LocalizationEditor.Bind resolves every binding of the manifest: another builder's labels must already exist.
                var unresolved = new List<string>();
                Action<string, string> probe = (r, p) =>
                {
                    if (Stations.Any(s => s.name == r) && p.StartsWith(StripName + "/")) return;   // made by this run
                    try { KD_LocalizationEditor.Resolve(new KD_LocalizationEditor.Binding { root = r, path = p }); }
                    catch (Exception e) { unresolved.Add(r + "/" + p + " (" + e.Message + ")"); }
                };
                foreach (var b in manifest.labels) probe(b.root, b.path);
                foreach (var b in manifest.interactions ?? new KD_LocalizationEditor.Binding[0]) probe(b.root, b.path);
                foreach (var b in manifest.pickups ?? new KD_LocalizationEditor.Binding[0]) probe(b.root, b.path);
                foreach (var n in manifest.names ?? new KD_LocalizationEditor.Names[0]) probe(n.root, "");
                if (unresolved.Count > 0)
                    throw new InvalidOperationException("the localization manifest names objects that are not in the scene (run the builder that owns them first, then Guide/11): "
                        + string.Join("; ", unresolved.Take(8)) + "; nothing changed");
            }
            c.wood = AssetDatabase.LoadAssetAtPath<Material>(WoodMatPath);
            if (c.wood == null) throw new InvalidOperationException(WoodMatPath + " missing; nothing changed");
            return c;
        }

        static string LabelPath(string group, string label) { return StripName + "/" + CanvasName + "/" + group + "/" + label; }
        static bool IsOurPath(string path)
        {
            foreach (var g in new[] { VrName, FlatName }) foreach (var l in LabelNames) if (path == LabelPath(g, l)) return true;
            return false;
        }

        // ================================================================ Guide/11

        [Serializable] class BuildReport
        {
            public string utc, scene, status, backup;
            public int artVersion; public bool artWritten;
            public List<string> stations = new List<string>();
            public List<string> log = new List<string>();
        }

        [MenuItem(Menu + "11 - Build Walk Guide Strips")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var c = Resolve(true);
            var r = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, artVersion = WalkArtVersion };
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-walk-guide-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, false);
            try
            {
                var tex = EnsureArt(r.log, out r.artWritten);
                for (int i = 0; i < Stations.Length; i++)
                {
                    var st = c.stations[i];
                    var strip = BuildStrip(st, Stations[i], c, tex, r);
                    var vr = strip.Find(CanvasName + "/" + VrName).gameObject;
                    var flat = strip.Find(CanvasName + "/" + FlatName).gameObject;
                    var opener = c.openers[i];
                    opener.vrOnly = new[] { vr };
                    opener.flatOnly = new[] { flat };
                    EditorUtility.SetDirty(opener);
                    UdonSharpEditorUtility.CopyProxyToUdon(opener);
                    var canvas = strip.Find(CanvasName);
                    r.stations.Add(Stations[i].name + ": station " + V(st.position) + " yaw " + st.eulerAngles.y.ToString("F1")
                        + ", strip reads from " + (Stations[i].readsFromPlusZ ? "+Z" : "-Z") + ", canvas centre " + V(canvas.position)
                        + " (ground + " + (canvas.position.y - (st.position.y - StationGroundBelow)).ToString("F2") + " m), facing yaw " + Quaternion.LookRotation(-canvas.forward).eulerAngles.y.ToString("F1"));
                }
                var log = new StringBuilder();
                KD_ShopSetupEditor.AddGlyphs(GlyphText(c.catalog), log);
                // the language tables first, then the label bindings: the same pair as Menu Finish / Guide/1
                KD_LocalizationEditor.Configure(c.language, c.catalog, log);
                KD_LocalizationEditor.Bind(c.language, c.catalog, log);
                UdonSharpEditorUtility.CopyProxyToUdon(c.language);
                EditorUtility.SetDirty(c.language);
                foreach (var line in log.ToString().Split('\n')) if (line.Trim().Length > 0 && !line.StartsWith("  AUTOSIZE")) r.log.Add(line.Trim());
                EditorSceneManager.MarkSceneDirty(c.scene);
                if (!EditorSceneManager.SaveScene(c.scene)) throw new IOException("walk guide scene save failed");
            }
            catch
            {
                Debug.LogError("[KH Walk Guide] Build failed; the scene before this run is at " + r.backup + ". Reopen the scene without saving to discard partial changes.");
                r.status = "FAIL (exception, not saved)";
                WriteJson(BuildReportPath, r);
                throw;
            }
            r.status = "BUILT";
            WriteJson(BuildReportPath, r);
            var v = Check();
            WriteJson(VerifyReportPath, v);
            if (v.failed > 0)
            {
                Debug.LogError("[KH Walk Guide] WALK_GUIDE_FAIL after build (" + v.failed + " failed) -> " + VerifyReportPath);
                throw new InvalidOperationException("[KH Walk Guide] Build saved but " + v.failed + " check(s) failed:\n" + string.Join("\n", v.lines.Where(l => l.StartsWith("FAIL")).Take(30)));
            }
            Debug.Log("[KH Walk Guide] BUILT and WALK_GUIDE_OK (" + v.lines.Count + " checks, " + v.warned + " WARN) -> " + BuildReportPath + " / " + VerifyReportPath);
        }

        /// <summary>One station's strip: WalkGuide and everything under it (code-owned: unknown children are removed).</summary>
        static Transform BuildStrip(Transform st, StationSpec spec, Ctx c, Texture2D tex, BuildReport r)
        {
            var strip = Child(st, StripName, r);
            strip.localPosition = new Vector3(0f, 0f, CrossbarZ);
            strip.localRotation = Quaternion.Euler(0f, spec.readsFromPlusZ ? 180f : 0f, 0f);
            strip.localScale = Vector3.one;
            strip.gameObject.layer = 0;
            strip.gameObject.SetActive(true);

            Block(strip, PostLName, new Vector3(-PostX, PostY, 0f), PostScale, c.wood);
            Block(strip, PostRName, new Vector3(PostX, PostY, 0f), PostScale, c.wood);
            Block(strip, BackName, new Vector3(0f, BackY, 0f), BackScale, c.wood);

            var cgo = Child(strip, CanvasName, r, true).gameObject;
            cgo.layer = 0;
            var canvas = cgo.GetComponent<Canvas>(); if (canvas == null) canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            foreach (var gr in cgo.GetComponents<GraphicRaycaster>()) UnityEngine.Object.DestroyImmediate(gr);
            foreach (var shape in cgo.GetComponents<VRCUiShape>()) UnityEngine.Object.DestroyImmediate(shape);
            foreach (var col in cgo.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(col);
            var rect = (RectTransform)cgo.transform;
            rect.sizeDelta = CanvasPixels;
            rect.localScale = Vector3.one * CanvasScale;
            rect.localPosition = new Vector3(0f, CanvasY, CanvasZ);
            rect.localRotation = Quaternion.identity;   // the Canvas's front is -Z = WalkGuide's reading side
            EditorUtility.SetDirty(canvas);

            var bg = KD_ShopSetupEditor.EnsureUiChild(cgo.transform, BgName, Vector2.zero, CanvasPixels);
            bg.transform.SetAsFirstSibling();
            var bgImg = bg.GetComponent<Image>(); if (bgImg == null) bgImg = bg.AddComponent<Image>();
            bgImg.color = KD_UiSpriteEditor.SurfacePanel;   // the wash strip's dark surface
            bgImg.raycastTarget = false;
            EditorUtility.SetDirty(bgImg);
            for (int i = 0; i < ArrowNames.Length; i++)
                Label(cgo.transform, ArrowNames[i], new Vector2(ArrowX[i], ArrowY), ArrowSize, c.font, ArrowPt, InkDim, ArrowText);

            BuildVariant(cgo.transform, VrName, VrKeys, VrCells, c, tex, r);
            BuildVariant(cgo.transform, FlatName, FlatKeys, FlatCells, c, tex, r);
            cgo.transform.Find(VrName).gameObject.SetActive(true);     // saved state: VR shown; KH_MenuOpener picks at Start
            cgo.transform.Find(FlatName).gameObject.SetActive(false);

            Prune(cgo.transform, new[] { BgName, VrName, FlatName }.Concat(ArrowNames), st.name + "/" + StripName + "/" + CanvasName, r);
            Prune(strip, new[] { PostLName, PostRName, BackName, CanvasName, "FinishedVisual" }, st.name + "/" + StripName, r);
            KH_PropFinishEditor.FinishRackGuide(strip);
            foreach (var g in cgo.GetComponentsInChildren<Graphic>(true)) if (g.raycastTarget) { g.raycastTarget = false; EditorUtility.SetDirty(g); }
            return strip;
        }

        static void BuildVariant(Transform canvas, string name, string[] keys, int[] cells, Ctx c, Texture2D tex, BuildReport r)
        {
            var g = KD_ShopSetupEditor.EnsureUiChild(canvas, name, Vector2.zero, CanvasPixels).transform;
            foreach (var img in g.GetComponents<Graphic>()) UnityEngine.Object.DestroyImmediate(img);
            string ja(int i) { return KD_LocalizationEditor.Text(c.catalog, c.catalog.fallbackLanguage, keys[i]); }
            for (int i = 0; i < 4; i++)
            {
                Label(g, LabelNames[2 * i], new Vector2(PanelX[i], StepY), StepSize, c.font, StepPt, InkStep, ja(2 * i));
                Label(g, LabelNames[2 * i + 1], new Vector2(PanelX[i], SubY), SubSize, c.font, SubPt, InkSub, ja(2 * i + 1));
                var pg = KD_ShopSetupEditor.EnsureUiChild(g, PicNames[i], new Vector2(PanelX[i], PicY), PicSize);
                foreach (var img in pg.GetComponents<Image>()) UnityEngine.Object.DestroyImmediate(img);
                var raw = pg.GetComponent<RawImage>(); if (raw == null) raw = pg.AddComponent<RawImage>();
                raw.texture = tex; raw.uvRect = CellUv(cells[i]); raw.color = Color.white; raw.raycastTarget = false;
                EditorUtility.SetDirty(raw);
            }
            Label(g, LabelNames[8], HelpPos, HelpSize, c.font, HelpPt, InkDim, ja(8));
            Prune(g, LabelNames.Concat(PicNames), g.parent.name + "/" + name, r);
        }

        // ================================================================ small helpers

        static Transform Child(Transform parent, string name, BuildReport r, bool rect = false)
        {
            var found = parent.Cast<Transform>().Where(t => t.name == name).ToArray();
            for (int i = 1; i < found.Length; i++) { r.log.Add("removed duplicate " + parent.name + "/" + name); UnityEngine.Object.DestroyImmediate(found[i].gameObject); }
            if (found.Length > 0) return found[0];
            var go = rect ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
            go.transform.SetParent(parent, false);
            r.log.Add("created " + parent.name + "/" + name);
            return go.transform;
        }

        /// <summary>A wooden cube like KH_LeashModelsEditor.MakeRack's (layer 14, no collider), placed every run.</summary>
        static void Block(Transform parent, string name, Vector3 pos, Vector3 scale, Material wood)
        {
            var t = parent.Find(name);
            if (t == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name; t = go.transform; t.SetParent(parent, false);
            }
            t.gameObject.layer = 14;
            foreach (var col in t.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(col);
            t.localPosition = pos; t.localRotation = Quaternion.identity; t.localScale = scale;
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null) { mr.sharedMaterial = wood; EditorUtility.SetDirty(mr); }
        }

        /// <summary>A one-line label: autosize from pt down to MinPt (14), centred, no wrap, overflow (the wash board's EnsureBoardLabel).</summary>
        static TextMeshProUGUI Label(Transform parent, string name, Vector2 pos, Vector2 size, TMP_FontAsset font, float pt, Color ink, string text)
        {
            var g = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var t = g.GetComponent<TextMeshProUGUI>(); if (t == null) t = g.AddComponent<TextMeshProUGUI>();
            t.font = font; t.fontSize = pt; t.enableAutoSizing = true; t.fontSizeMax = pt; t.fontSizeMin = MinPt;
            t.alignment = TextAlignmentOptions.Center; t.color = ink;
            t.raycastTarget = false; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow;
            t.text = text;
            EditorUtility.SetDirty(t);
            return t;
        }

        static void Prune(Transform parent, IEnumerable<string> keep, string where, BuildReport r)
        {
            var known = new HashSet<string>(keep);
            foreach (Transform t in parent.Cast<Transform>().ToArray())
                if (!known.Contains(t.name)) { r.log.Add("removed stale " + where + "/" + t.name); UnityEngine.Object.DestroyImmediate(t.gameObject); }
        }

        /// <summary>The ja / en (glyphs = self) texts of the walk guide keys, plus the arrow.</summary>
        static string GlyphText(KD_LocalizationEditor.Catalog catalog)
        {
            var s = new StringBuilder(ArrowText);
            foreach (var lang in catalog.languages)
            {
                if (!KD_LocalizationEditor.Bundled(lang)) continue;
                foreach (var k in AllKeys) s.Append(KD_LocalizationEditor.Text(catalog, lang.code, k));
            }
            return s.ToString();
        }

        /// <summary>The terrain height under a world point (TerrainCollider hit, else the covering Terrain's SampleHeight); NaN without terrain.</summary>
        static float Ground(Vector3 world)
        {
            Physics.SyncTransforms();
            var hits = Physics.RaycastAll(new Vector3(world.x, 1000f, world.z), Vector3.down, 2000f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity, y = float.NaN;
            foreach (var h in hits) if (h.collider is TerrainCollider && h.distance < best) { best = h.distance; y = h.point.y; }
            if (!float.IsNaN(y)) return y;
            foreach (var t in Terrain.activeTerrains)
            {
                if (t == null || t.terrainData == null) continue;
                var p = t.transform.position; var s = t.terrainData.size;
                if (world.x >= p.x && world.x <= p.x + s.x && world.z >= p.z && world.z <= p.z + s.z) return t.SampleHeight(world) + p.y;
            }
            return float.NaN;
        }

        /// <summary>Where a reader of this station comes from (the facing check): the spawn, the viewpoint place, the plaza centre.</summary>
        static Vector3? TargetOf(StationSpec spec)
        {
            switch (spec.target)
            {
                case "spawn": { var t = GameObject.Find("KH_World/KH_Places/KH_Entrance"); return t != null ? t.transform.position : (Vector3?)null; }
                case "viewpoint": { var t = GameObject.Find("KH_World/KH_Places/KH_Viewpoint"); return t != null ? t.transform.position : (Vector3?)null; }
                case "plaza":
                    return new Vector3(KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat, 0], KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat, 2],
                        KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat, 1]);
            }
            return null;
        }

        static string V(Vector3 v) { return "(" + v.x.ToString("F2") + ", " + v.y.ToString("F2") + ", " + v.z.ToString("F2") + ")"; }

        static void WriteJson(string path, object o)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(o, true));
        }
    }
}
