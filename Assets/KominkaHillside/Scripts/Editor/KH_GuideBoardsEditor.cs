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
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace KominkaHillside.Editor
{
    /// <summary>DES-UI-004 / DES-UI-005: the guide boards at the entrance (design: docs/plans/2026-10-02-guide-boards.md).
    ///
    /// Guide/1 Build Guide Boards: production scene in idle Edit mode (clean), everything looked up before anything changes, the
    /// scene copied to Temp/kh-guide-boards/before-guide-boards-*.unity, then under the scene root KH_GuideBoards:
    ///   Asobikata = the roofed notice board "あそびかた" (4 papers: walk / frisbee / wash / points and trainer + a strip "how to open
    ///               the hand menu"), east of the spawn beside the shopkeeper, facing the spawn;
    ///   Map       = the trail-head map board (the hand map's texture KH_TravelMap.png, the 6 spots, the wash stations, "you are
    ///               here"), west of the trail start, facing the spawn / the trail.
    /// Each board is a wooden frame (cubes, KH_RiverWood, no collider, Batching Static) + one read-only World Space Canvas (no
    /// raycaster, no VRCUiShape, no collider, no Udon). The words are the base catalog's guide.* keys (UITranslations.json, 13
    /// languages) bound through UILocalizationBindings.json; the build adds the ja / en glyphs through KD_ShopSetupEditor.AddGlyphs and
    /// rebuilds KD_Language's tables and re-binds every label (KD_LocalizationEditor.Configure + Bind, as Menu Finish does; KD_Language follows the viewer's language at run time).
    /// A board's x / z / yaw are human-owned once it exists (the build only re-seats its height on the terrain); everything below
    /// "Structure" and "Canvas" is code-owned and rebuilt on every run (stale children removed). Saves, then runs Guide/2's checks.
    /// Guide/2 Verify Guide Boards: no scene change. Temp/kh-guide-boards/verify.json, one log line GUIDE_BOARDS_OK / GUIDE_BOARDS_FAIL.
    /// Guide/3 Capture Guide Boards: PNGs from the intended viewing positions (Temp/kh-guide-boards/shots/).</summary>
    public static partial class KH_GuideBoardsEditor
    {
        const string Menu = "Tools/KominkaHillside/Guide/";
        const string OutDir = "Temp/kh-guide-boards";
        const string BuildReportPath = OutDir + "/build.json", VerifyReportPath = OutDir + "/verify.json";
        public const string RootName = "KH_GuideBoards";
        const string StructureName = "Structure", CanvasName = "Canvas";
        const string WoodMatPath = "Assets/KominkaHillside/Data/Environment/River/KH_RiverWood.mat";

        // ---------------------------------------------------------------- look (core-concept §3: dark surfaces / light ink for signs, washi + sumi for the papers)
        static readonly Color Washi = new Color(0.94f, 0.91f, 0.83f);
        static readonly Color StripWashi = new Color(0.90f, 0.86f, 0.76f);
        static readonly Color Sumi = new Color(0.17f, 0.13f, 0.10f);
        static readonly Color Vermilion = new Color(0xB5 / 255f, 0x43 / 255f, 0x2A / 255f);   // = <color=#B5432A> in the texts
        static readonly Color SignInk = new Color(0xF2 / 255f, 0xE8 / 255f, 0xD0 / 255f);     // the wash / water signs' ink
        static readonly Color HeaderPlateColor = new Color(0.20f, 0.14f, 0.09f);
        static readonly Color WashMarkColor = new Color(0.22f, 0.42f, 0.62f);                  // = <color=#386B9E> in guide.map.key

        // ---------------------------------------------------------------- boards (positions: docs/plans/2026-10-02-guide-boards.md §3)
        class Board
        {
            public string name; public Vector2 createXZ; public float createYaw; public Vector2 canvasMm; public float canvasBottom;
        }
        /// <summary>Root yaw: the root's +Z points away from the readers (they look along +Z; the Canvas's front is -Z), like the wash station.</summary>
        static readonly Board Asob = new Board { name = "Asobikata", createXZ = new Vector2(53.0f, 44.5f), createYaw = 65f, canvasMm = new Vector2(3240f, 1440f), canvasBottom = 0.80f };
        static readonly Board MapBoard = new Board { name = "Map", createXZ = new Vector2(39.2f, 46.6f), createYaw = 330f, canvasMm = new Vector2(1300f, 1300f), canvasBottom = 0.75f };
        static readonly Board[] Boards = { Asob, MapBoard };

        const float Mm = 0.001f;
        const float PostSize = 0.10f, BackDepth = 0.04f, BackZ = 0.03f, PostSink = 0.25f;
        const float RoofSlope = 25f, RoofPlankDepth = 0.36f, RoofThick = 0.03f, RoofOverhang = 0.25f, RidgeSize = 0.06f;
        static float BackW(Board b) { return b.canvasMm.x * Mm + 0.08f; }
        static float BackH(Board b) { return b.canvasMm.y * Mm + 0.08f; }
        static float CanvasCenterY(Board b) { return b.canvasBottom + b.canvasMm.y * Mm * 0.5f; }
        static float PostX(Board b) { return BackW(b) * 0.5f + 0.06f; }
        static float PostTop(Board b) { return CanvasCenterY(b) + BackH(b) * 0.5f + 0.22f; }
        static float RidgeY(Board b) { return PostTop(b) + 0.10f; }
        static float RoofLength(Board b) { return 2f * PostX(b) + PostSize + 2f * RoofOverhang; }
        /// <summary>Footprint in board-local x / z (posts, roof overhang), for the placement checks.</summary>
        static Rect Footprint(Board b)
        {
            float hx = RoofLength(b) * 0.5f, hz = Mathf.Cos(RoofSlope * Mathf.Deg2Rad) * RoofPlankDepth + 0.02f;
            return Rect.MinMaxRect(-hx, BackZ - hz, hx, BackZ + hz);
        }
        /// <summary>Where the readers stand (board-local, in front = -Z), for the placement checks and the captures.</summary>
        static Rect ReaderArea(Board b) { float hx = BackW(b) * 0.5f; return Rect.MinMaxRect(-hx, -2.0f, hx, -0.3f); }
        const float ReadDistance = 1.8f, EyeStanding = 1.6f, EyeSeated = 1.15f;

        // ---------------------------------------------------------------- Asobikata layout (canvas units = mm, centre origin; 3240 x 1440)
        static readonly Vector2 AsobHeaderPos = new Vector2(0f, 625f), AsobHeaderPlate = new Vector2(3080f, 170f), AsobHeaderText = new Vector2(3000f, 160f);
        static readonly Vector2 StripPos = new Vector2(0f, 470f), StripSize = new Vector2(3080f, 90f), StripTextSize = new Vector2(3020f, 80f);
        static readonly Vector2 PaperSize = new Vector2(740f, 1060f);
        const float PaperY = -170f;
        static readonly float[] PaperX = { -1155f, -385f, 385f, 1155f };
        static readonly string[] PaperNames = { "Paper_Walk", "Paper_Frisbee", "Paper_Wash", "Paper_Shop" };
        static readonly string[] PaperKeys = { "walk", "frisbee", "wash", "shop" };
        static readonly Vector2 TitlePos = new Vector2(0f, 470f), TitleSize = new Vector2(680f, 90f), BodyPos = new Vector2(0f, -50f), BodySize = new Vector2(680f, 900f);
        const float HeaderMax = 130f, HeaderMin = 80f, StripMax = 36f, StripMin = 22f, TitleMax = 60f, TitleMin = 40f, BodyMax = 31f, BodyMin = 22f;

        // ---------------------------------------------------------------- Map layout (canvas 1300 x 1300; the paper's children in paper-local mm)
        static readonly Vector2 MapHeaderPos = new Vector2(0f, 575f), MapHeaderPlate = new Vector2(1240f, 130f), MapHeaderText = new Vector2(1200f, 120f);
        static readonly Vector2 MapPaperPos = new Vector2(0f, -75f), MapPaperSize = new Vector2(1240f, 1120f);
        const float MapPx = 760f;
        static readonly Vector2 MapCenter = new Vector2(-220f, 160f);
        static readonly Vector2 NorthPos = new Vector2(110f, 505f), NorthSize = new Vector2(110f, 50f);
        static readonly Vector2 NumOffset = new Vector2(28f, 24f), NumSize = new Vector2(40f, 40f);
        const float NumFont = 34f;
        static readonly Vector2 HereSize = new Vector2(24f, 24f), HereLabelPos = new Vector2(100f, -28f), HereLabelSize = new Vector2(170f, 40f);
        static readonly Vector2 WashMarkSize = new Vector2(16f, 16f);
        const float WashMarkSkipMeters = 25f;   // the main station is beside the board: "you are here" covers it
        static readonly Vector2 LegendPos = new Vector2(390f, 270f), LegendSize = new Vector2(420f, 540f);
        static readonly Vector2 KeyPos = new Vector2(390f, -120f), KeySize = new Vector2(420f, 220f);
        static readonly Vector2 NotePos = new Vector2(0f, -400f), NoteSize = new Vector2(1180f, 300f);
        const float MapTitleMax = 96f, MapTitleMin = 60f, NorthMax = 34f, NorthMin = 24f, HereMax = 28f, HereMin = 18f, LegendMax = 38f, LegendMin = 24f, KeyMax = 30f, KeyMin = 20f, NoteMax = 34f, NoteMin = 22f;

        // ---------------------------------------------------------------- bound labels (UILocalizationBindings.json, root KH_GuideBoards)
        class LabelSpec { public string path, key; public LabelSpec(string p, string k) { path = p; key = k; } }
        static readonly LabelSpec[] Labels =
        {
            new LabelSpec("Asobikata/Canvas/Header", "guide.board.title"),
            new LabelSpec("Asobikata/Canvas/MenuStrip/Text", "guide.menu"),
            new LabelSpec("Asobikata/Canvas/Paper_Walk/Title", "guide.walk.title"),
            new LabelSpec("Asobikata/Canvas/Paper_Walk/Body", "guide.walk.body"),
            new LabelSpec("Asobikata/Canvas/Paper_Frisbee/Title", "guide.frisbee.title"),
            new LabelSpec("Asobikata/Canvas/Paper_Frisbee/Body", "guide.frisbee.body"),
            new LabelSpec("Asobikata/Canvas/Paper_Wash/Title", "guide.wash.title"),
            new LabelSpec("Asobikata/Canvas/Paper_Wash/Body", "guide.wash.body"),
            new LabelSpec("Asobikata/Canvas/Paper_Shop/Title", "guide.shop.title"),
            new LabelSpec("Asobikata/Canvas/Paper_Shop/Body", "guide.shop.body"),
            new LabelSpec("Map/Canvas/Header", "guide.map.title"),
            new LabelSpec("Map/Canvas/Paper/Legend", "guide.map.legend"),
            new LabelSpec("Map/Canvas/Paper/Key", "guide.map.key"),
            new LabelSpec("Map/Canvas/Paper/Note", "guide.map.note"),
            new LabelSpec("Map/Canvas/Paper/Here/Label", "guide.map.here"),
            new LabelSpec("Map/Canvas/Paper/North", "guide.map.north"),
        };

        // ================================================================ context (looked up before anything changes)

        class Ctx
        {
            public UnityEngine.SceneManagement.Scene scene;
            public KD_TreatPouch pouch; public KD_Language language; public TMP_FontAsset font;
            public KD_LocalizationEditor.Catalog catalog;
            public Material wood; public Texture2D mapTexture; public Sprite circle;
            public KH_LeashManager manager;
            public Transform root;                 // null before the first build
            public List<Transform> washStations = new List<Transform>();
        }

        static Ctx Resolve(bool forBuild)
        {
            var c = new Ctx { scene = EditorSceneManager.GetActiveScene() };
            var pouches = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true);
            if (pouches.Length != 1) throw new InvalidOperationException("exactly one KD_TreatPouch required (" + pouches.Length + "); nothing changed");
            c.pouch = pouches[0];
            c.language = c.pouch.language;
            if (c.language == null) throw new InvalidOperationException("KD_TreatPouch.language not wired (Menu/11); nothing changed");
            c.font = c.pouch.sectionTitleLabel != null ? c.pouch.sectionTitleLabel.font : null;
            if (c.font == null) throw new InvalidOperationException("the menu's shared font (KD_TreatPouch.sectionTitleLabel.font) is missing; nothing changed");
            var shared = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            if (shared != c.font) throw new InvalidOperationException("the menu font is not " + KD_ShopSetupEditor.SharedFontPath + "; nothing changed");
            c.catalog = KD_LocalizationEditor.Load();
            var missingTexts = new List<string>();
            foreach (var lang in c.catalog.languages)
                foreach (var l in Labels)
                {
                    var e = lang.texts.FirstOrDefault(x => x.key == l.key);
                    if (e == null || string.IsNullOrWhiteSpace(e.text)) missingTexts.Add(lang.code + ":" + l.key);
                }
            if (missingTexts.Count > 0) throw new InvalidOperationException("guide texts missing in " + KD_MenuFinishEditor.CatalogPath + ": " + string.Join(", ", missingTexts.Take(12)) + "; nothing changed");
            var manifest = JsonUtility.FromJson<KD_LocalizationEditor.Manifest>(File.ReadAllText(KD_LocalizationEditor.BindingsPath));
            foreach (var l in Labels)
                if (!manifest.labels.Any(b => b.root == RootName && b.path == l.path && b.key == l.key))
                    throw new InvalidOperationException("binding missing in " + KD_LocalizationEditor.BindingsPath + ": " + RootName + "/" + l.path + " = " + l.key + "; nothing changed");
            var foreign = manifest.labels.Where(b => b.root == RootName && !Labels.Any(l => l.path == b.path)).Select(b => b.path).ToArray();
            if (foreign.Length > 0) throw new InvalidOperationException("bindings under " + RootName + " that this builder does not make: " + string.Join(", ", foreign) + "; nothing changed");
            if (forBuild)
            {
                // KD_LocalizationEditor.Bind resolves every binding of the manifest: another builder's labels must already exist.
                var unresolved = new List<string>();
                Action<string, string> probe = (r, p) =>
                {
                    if (r == RootName) return;
                    try { KD_LocalizationEditor.Resolve(new KD_LocalizationEditor.Binding { root = r, path = p }); }
                    catch (Exception e) { unresolved.Add(r + "/" + p + " (" + e.Message + ")"); }
                };
                foreach (var b in manifest.labels) probe(b.root, b.path);
                foreach (var b in manifest.interactions ?? new KD_LocalizationEditor.Binding[0]) probe(b.root, b.path);
                foreach (var b in manifest.pickups ?? new KD_LocalizationEditor.Binding[0]) probe(b.root, b.path);
                foreach (var n in manifest.names ?? new KD_LocalizationEditor.Names[0]) probe(n.root, "");
                if (unresolved.Count > 0)
                    throw new InvalidOperationException("the localization manifest names objects that are not in the scene (run the builder that owns them first, then Guide/1): "
                        + string.Join("; ", unresolved.Take(8)) + "; nothing changed");
            }
            c.wood = AssetDatabase.LoadAssetAtPath<Material>(WoodMatPath);
            if (c.wood == null) throw new InvalidOperationException(WoodMatPath + " missing; nothing changed");
            c.mapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(KH_CourseTravelMapEditor.TexturePath);
            if (c.mapTexture == null) throw new InvalidOperationException(KH_CourseTravelMapEditor.TexturePath + " missing (Course/84 draws it); nothing changed");
            c.circle = AssetDatabase.LoadAssetAtPath<Sprite>(KD_UiSpriteEditor.CircleSpritePath);
            var managers = UnityEngine.Object.FindObjectsOfType<KH_LeashManager>(true);
            if (managers.Length != 1 || managers[0].spotPositions == null || managers[0].spotPositions.Length != 6 || managers[0].spotWarpEnabled == null || managers[0].spotWarpEnabled.Length != 6)
                throw new InvalidOperationException("one KH_LeashManager with 6 travel spots required (Course/80); nothing changed");
            c.manager = managers[0];
            var roots = c.scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray();
            if (roots.Length > 1) throw new InvalidOperationException(roots.Length + " " + RootName + " at the scene root (keep one); nothing changed");
            KD_SceneObjects.Find(RootName, out int anywhere);
            if (anywhere != roots.Length) throw new InvalidOperationException(RootName + " exists below another object (move it to the scene root); nothing changed");
            c.root = roots.Length == 1 ? roots[0].transform : null;
            if (c.root != null)
                foreach (var b in Boards)
                {
                    int n = c.root.Cast<Transform>().Count(t => t.name == b.name);
                    if (n > 1) throw new InvalidOperationException(n + " children named " + b.name + " under " + RootName + " (keep one); nothing changed");
                }
            var main = c.scene.GetRootGameObjects().FirstOrDefault(g => g.name == "KH_WashStation");
            if (main != null) c.washStations.Add(main.transform);
            var course = c.scene.GetRootGameObjects().FirstOrDefault(g => g.name == "KH_WashStations");
            if (course != null) foreach (Transform t in course.transform) c.washStations.Add(t);
            return c;
        }

        // ================================================================ Guide/1

        [Serializable] class BuildReport
        {
            public string utc, scene, status, backup;
            public List<string> boards = new List<string>();
            public List<string> log = new List<string>();
        }

        [MenuItem(Menu + "1 - Build Guide Boards")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var c = Resolve(true);
            var r = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-guide-boards-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, false);
            try
            {
                var root = c.root;
                if (root == null)
                {
                    root = new GameObject(RootName).transform;
                    SceneManagerMove(root.gameObject, c.scene);
                    r.log.Add("created " + RootName + " at the scene root");
                }
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.localScale = Vector3.one;
                root.gameObject.layer = 0;
                root.gameObject.SetActive(true);
                foreach (Transform t in root)
                    if (!Boards.Any(b => b.name == t.name)) r.log.Add("WARN " + RootName + "/" + t.name + " is not a board this builder makes: left as is");

                var keep = new HashSet<Transform>();
                foreach (var b in Boards)
                {
                    var bt = EnsureBoard(root, b, r);
                    BuildStructure(bt, b, c, keep);
                    var canvas = EnsureCanvas(bt, b, keep);
                    if (b == Asob) BuildAsobikataCanvas(canvas, c, keep);
                    else BuildMapCanvas(canvas, bt, c, keep);
                    RemoveStale(bt, keep, r);
                    r.boards.Add(b.name + " at " + V(bt.position) + " yaw " + bt.eulerAngles.y.ToString("F1"));
                }

                var log = new StringBuilder();
                KD_ShopSetupEditor.AddGlyphs(GlyphText(c.catalog), log);
                // the language tables (keys / texts of every language) first, then the label bindings: the same pair as Menu Finish (RebindIfPresent)
                KD_LocalizationEditor.Configure(c.language, c.catalog, log);
                KD_LocalizationEditor.Bind(c.language, c.catalog, log);
                UdonSharpEditorUtility.CopyProxyToUdon(c.language);
                EditorUtility.SetDirty(c.language);
                foreach (var line in log.ToString().Split('\n')) if (line.Trim().Length > 0 && !line.StartsWith("  AUTOSIZE")) r.log.Add(line.Trim());

                EditorSceneManager.MarkSceneDirty(c.scene);
                if (!EditorSceneManager.SaveScene(c.scene)) throw new IOException("guide boards scene save failed");
            }
            catch
            {
                Debug.LogError("[KH Guide Boards] Build failed; the scene before this run is at " + r.backup + ". Reopen the scene without saving to discard partial changes.");
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
                Debug.LogError("[KH Guide Boards] GUIDE_BOARDS_FAIL after build (" + v.failed + " failed) -> " + VerifyReportPath);
                throw new InvalidOperationException("[KH Guide Boards] Build saved but " + v.failed + " check(s) failed:\n" + string.Join("\n", v.lines.Where(l => l.StartsWith("FAIL")).Take(30)));
            }
            Debug.Log("[KH Guide Boards] BUILT and GUIDE_BOARDS_OK (" + v.lines.Count + " checks, " + v.warned + " WARN) -> " + BuildReportPath + " / " + VerifyReportPath);
        }

        static void SceneManagerMove(GameObject go, UnityEngine.SceneManagement.Scene scene)
        {
            if (go.scene != scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
        }

        /// <summary>The ja / en (glyphs = self) texts of the guide keys, without rich-text tags, plus the map's digits.</summary>
        static string GlyphText(KD_LocalizationEditor.Catalog catalog)
        {
            var s = new StringBuilder("0123456789");
            foreach (var lang in catalog.languages)
            {
                if (!KD_LocalizationEditor.Bundled(lang)) continue;
                foreach (var l in Labels) s.Append(Visible(KD_LocalizationEditor.Text(catalog, lang.code, l.key)));
            }
            return s.ToString();
        }

        static string Visible(string text) { return System.Text.RegularExpressions.Regex.Replace(text ?? "", "<[^<>]+>", ""); }

        // ================================================================ board, structure

        /// <summary>The board's root: created at the design pose the first time; afterwards x / z / yaw are the user's (only y is re-seated).</summary>
        static Transform EnsureBoard(Transform root, Board b, BuildReport r)
        {
            var t = root.Find(b.name);
            if (t == null)
            {
                t = new GameObject(b.name).transform;
                t.SetParent(root, false);
                t.position = new Vector3(b.createXZ.x, 0f, b.createXZ.y);
                t.rotation = Quaternion.Euler(0f, b.createYaw, 0f);
                r.log.Add("created " + b.name + " at the design pose (" + b.createXZ.x + ", " + b.createXZ.y + ") yaw " + b.createYaw);
            }
            else
            {
                var e = t.eulerAngles;
                if (Mathf.Abs(Mathf.DeltaAngle(e.x, 0f)) > 0.01f || Mathf.Abs(Mathf.DeltaAngle(e.z, 0f)) > 0.01f)
                {
                    t.rotation = Quaternion.Euler(0f, e.y, 0f);
                    r.log.Add(b.name + ": tilt removed (yaw " + e.y.ToString("F1") + " kept)");
                }
                r.log.Add("kept " + b.name + " x/z/yaw (human-owned): " + V(t.position) + " yaw " + t.eulerAngles.y.ToString("F1"));
            }
            t.localScale = Vector3.one;
            t.gameObject.layer = 0;
            t.gameObject.SetActive(true);
            // seat: the highest terrain under the two posts and the middle (the lower post grows downward)
            float y = float.NegativeInfinity;
            foreach (var p in SeatSamples(b)) y = Mathf.Max(y, Ground(t.TransformPoint(p)));
            var pos = t.position; pos.y = y; t.position = pos;
            return t;
        }

        static Vector3[] SeatSamples(Board b) { return new[] { new Vector3(-PostX(b), 0f, BackZ), new Vector3(PostX(b), 0f, BackZ), new Vector3(0f, 0f, BackZ) }; }

        static void BuildStructure(Transform board, Board b, Ctx c, HashSet<Transform> keep)
        {
            var s = Child(board, StructureName, keep);
            s.localPosition = Vector3.zero; s.localRotation = Quaternion.identity; s.localScale = Vector3.one;
            float bw = BackW(b), bh = BackH(b), cy = CanvasCenterY(b), top = PostTop(b), ridge = RidgeY(b), len = RoofLength(b);
            Wood(s, "Back", new Vector3(0f, cy, BackZ), Quaternion.identity, new Vector3(bw, bh, BackDepth), c, keep);
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -PostX(b) : PostX(b);
                float ground = Ground(board.TransformPoint(new Vector3(x, 0f, BackZ))) - board.position.y;
                float bottom = ground - PostSink;
                Wood(s, i == 0 ? "PostL" : "PostR", new Vector3(x, (top + bottom) * 0.5f, BackZ), Quaternion.identity, new Vector3(PostSize, top - bottom, PostSize), c, keep);
            }
            Wood(s, "Ridge", new Vector3(0f, ridge, BackZ), Quaternion.identity, new Vector3(len, RidgeSize, RidgeSize), c, keep);
            float half = RoofPlankDepth * 0.5f, lift = RidgeSize * 0.5f;
            var front = Quaternion.Euler(-RoofSlope, 0f, 0f);
            var back = Quaternion.Euler(RoofSlope, 0f, 0f);
            Wood(s, "RoofFront", new Vector3(0f, ridge + lift, BackZ) + front * new Vector3(0f, 0f, -half), front, new Vector3(len, RoofThick, RoofPlankDepth), c, keep);
            Wood(s, "RoofBack", new Vector3(0f, ridge + lift, BackZ) + back * new Vector3(0f, 0f, half), back, new Vector3(len, RoofThick, RoofPlankDepth), c, keep);
        }

        static void Wood(Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 size, Ctx c, HashSet<Transform> keep)
        {
            var t = parent.Find(name);
            GameObject go;
            if (t == null) { go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false); }
            else go = t.gameObject;
            foreach (var col in go.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(col);   // no walking / interact / dog obstacle
            go.layer = 0;
            go.transform.localPosition = pos; go.transform.localRotation = rot; go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = c.wood;
            mr.shadowCastingMode = ShadowCastingMode.On; mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.BlendProbes; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            EditorUtility.SetDirty(mr);
            // one draw call for the frames of both boards (same material); never an occluder (Course/37 allows the terrain only)
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            keep.Add(go.transform);
        }

        static Transform Child(Transform parent, string name, HashSet<Transform> keep)
        {
            var t = parent.Find(name);
            if (t == null) { t = new GameObject(name).transform; t.SetParent(parent, false); }
            t.gameObject.layer = 0;
            keep.Add(t);
            return t;
        }

        // ================================================================ canvas

        static RectTransform EnsureCanvas(Transform board, Board b, HashSet<Transform> keep)
        {
            var t = board.Find(CanvasName);
            GameObject go;
            if (t == null) { go = new GameObject(CanvasName, typeof(RectTransform)); go.transform.SetParent(board, false); }
            else go = t.gameObject;
            go.layer = 0;
            // read-only (vrc-world-ui §2): no raycaster / VRCUiShape / collider / scaler
            foreach (var comp in go.GetComponents<Component>())
                if (comp is GraphicRaycaster || comp is Collider || comp is CanvasScaler || comp is VRC.SDK3.Components.VRCUiShape) UnityEngine.Object.DestroyImmediate(comp);
            var canvas = go.GetComponent<Canvas>(); if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = false;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;   // TMP SDF
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = b.canvasMm;
            rt.localPosition = new Vector3(0f, CanvasCenterY(b), 0f);
            rt.localRotation = Quaternion.identity;   // the Canvas's front is -Z = the readers' side
            rt.localScale = Vector3.one * Mm;
            EditorUtility.SetDirty(canvas);
            keep.Add(rt);
            return rt;
        }

        static void BuildAsobikataCanvas(RectTransform canvas, Ctx c, HashSet<Transform> keep)
        {
            Img(canvas, "HeaderPlate", AsobHeaderPos, AsobHeaderPlate, HeaderPlateColor, null, keep);
            Txt(canvas, "Header", AsobHeaderPos, AsobHeaderText, HeaderMax, HeaderMin, TextAlignmentOptions.Center, SignInk, false, c, keep);
            var strip = Img(canvas, "MenuStrip", StripPos, StripSize, StripWashi, null, keep);
            Txt(strip.transform, "Text", Vector2.zero, StripTextSize, StripMax, StripMin, TextAlignmentOptions.Center, Sumi, false, c, keep);
            for (int i = 0; i < PaperNames.Length; i++)
            {
                var paper = Img(canvas, PaperNames[i], new Vector2(PaperX[i], PaperY), PaperSize, Washi, null, keep);
                Txt(paper.transform, "Title", TitlePos, TitleSize, TitleMax, TitleMin, TextAlignmentOptions.Center, PaperKeys[i] == "shop" ? Vermilion : Sumi, false, c, keep);
                Txt(paper.transform, "Body", BodyPos, BodySize, BodyMax, BodyMin, TextAlignmentOptions.TopLeft, Sumi, true, c, keep);
            }
        }

        static void BuildMapCanvas(RectTransform canvas, Transform board, Ctx c, HashSet<Transform> keep)
        {
            Img(canvas, "HeaderPlate", MapHeaderPos, MapHeaderPlate, HeaderPlateColor, null, keep);
            Txt(canvas, "Header", MapHeaderPos, MapHeaderText, MapTitleMax, MapTitleMin, TextAlignmentOptions.Center, SignInk, false, c, keep);
            var paper = Img(canvas, "Paper", MapPaperPos, MapPaperSize, Washi, null, keep).transform;

            var mapGo = KD_ShopSetupEditor.EnsureUiChild(paper, "MapImage", MapCenter, new Vector2(MapPx, MapPx));
            foreach (var g in mapGo.GetComponents<Graphic>()) if (!(g is RawImage)) UnityEngine.Object.DestroyImmediate(g);
            var raw = mapGo.GetComponent<RawImage>(); if (raw == null) raw = mapGo.AddComponent<RawImage>();
            raw.texture = c.mapTexture; raw.color = Color.white; raw.raycastTarget = false; raw.uvRect = new Rect(0f, 0f, 1f, 1f);
            EditorUtility.SetDirty(raw);
            keep.Add(mapGo.transform);

            // wash stations on the course (squares), under the numbers
            int w = 0;
            foreach (var st in c.washStations)
            {
                if (st == null || Flat(st.position - board.position) < WashMarkSkipMeters) continue;
                Img(paper, "Wash_" + w, MapLocal(st.position), WashMarkSize, WashMarkColor, null, keep);
                w++;
            }
            // the 6 spots' numbers (the discs are in the texture, as on the hand map)
            for (int s = 0; s < 6; s++)
            {
                var num = Txt(paper, "Num" + (s + 1), MapLocal(c.manager.spotPositions[s]) + NumOffset, NumSize, NumFont, NumFont, TextAlignmentOptions.Center, Sumi, false, c, keep);
                num.enableAutoSizing = false; num.fontSize = NumFont; num.fontStyle = FontStyles.Bold; num.text = (s + 1).ToString();
                EditorUtility.SetDirty(num);
            }
            // you are here = this board
            var here = Img(paper, "Here", MapLocal(board.position), HereSize, Vermilion, c.circle, keep);
            Txt(here.transform, "Label", HereLabelPos, HereLabelSize, HereMax, HereMin, TextAlignmentOptions.Left, Vermilion, false, c, keep);
            Txt(paper, "North", NorthPos, NorthSize, NorthMax, NorthMin, TextAlignmentOptions.Center, Sumi, false, c, keep);
            Txt(paper, "Legend", LegendPos, LegendSize, LegendMax, LegendMin, TextAlignmentOptions.TopLeft, Sumi, true, c, keep);
            Txt(paper, "Key", KeyPos, KeySize, KeyMax, KeyMin, TextAlignmentOptions.TopLeft, Sumi, true, c, keep);
            Txt(paper, "Note", NotePos, NoteSize, NoteMax, NoteMin, TextAlignmentOptions.TopLeft, Sumi, true, c, keep);
            // draw order: map, wash squares, numbers, here, then the rest
            mapGo.transform.SetSiblingIndex(0);
        }

        /// <summary>Paper-local position of a world point on the board's map (KH_CourseTravelMapEditor's square, north up).</summary>
        static Vector2 MapLocal(Vector3 w)
        {
            float u = Mathf.Clamp((w.x - KH_CourseTravelMapEditor.CenterX) / KH_CourseTravelMapEditor.Span, -.5f, .5f);
            float v = Mathf.Clamp((w.z - KH_CourseTravelMapEditor.CenterZ) / KH_CourseTravelMapEditor.Span, -.5f, .5f);
            return MapCenter + new Vector2(u * MapPx, v * MapPx);
        }

        static Image Img(Transform parent, string name, Vector2 pos, Vector2 size, Color color, Sprite sprite, HashSet<Transform> keep)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            go.layer = 0;
            foreach (var g in go.GetComponents<Graphic>()) if (!(g is Image)) UnityEngine.Object.DestroyImmediate(g);
            var img = go.GetComponent<Image>(); if (img == null) img = go.AddComponent<Image>();
            img.sprite = sprite; img.type = Image.Type.Simple; img.preserveAspect = false;
            img.color = color; img.raycastTarget = false;
            EditorUtility.SetDirty(img);
            keep.Add(go.transform);
            return img;
        }

        static TextMeshProUGUI Txt(Transform parent, string name, Vector2 pos, Vector2 size, float max, float min, TextAlignmentOptions align, Color color, bool wrap, Ctx c, HashSet<Transform> keep)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            go.layer = 0;
            foreach (var g in go.GetComponents<Graphic>()) if (!(g is TextMeshProUGUI)) UnityEngine.Object.DestroyImmediate(g);
            var t = go.GetComponent<TextMeshProUGUI>(); if (t == null) t = go.AddComponent<TextMeshProUGUI>();
            t.font = c.font;
            t.fontStyle = FontStyles.Normal;
            t.enableAutoSizing = true; t.fontSizeMax = max; t.fontSizeMin = min; t.fontSize = max;
            t.alignment = align;
            t.color = color;
            t.enableWordWrapping = wrap;
            t.overflowMode = TextOverflowModes.Overflow;   // never Truncate / Ellipsis (vrc-world-ui §10.1: a box too short for one line draws nothing)
            t.richText = true;
            t.margin = Vector4.zero;
            t.raycastTarget = false;
            if (string.IsNullOrEmpty(t.text)) t.text = name;   // Bind writes the ja text right after
            EditorUtility.SetDirty(t);
            keep.Add(go.transform);
            return t;
        }

        /// <summary>Everything under the board's Structure / Canvas that this run did not make is stale (an earlier layout).
        /// Other children of the board are the user's: kept, logged.</summary>
        static void RemoveStale(Transform board, HashSet<Transform> keep, BuildReport r)
        {
            foreach (Transform t in board)
                if (t.name != StructureName && t.name != CanvasName) r.log.Add("WARN " + board.name + "/" + t.name + " is not made by this builder: left as is");
            foreach (var name in new[] { StructureName, CanvasName })
            {
                var top = board.Find(name);
                if (top == null) continue;
                var stale = top.GetComponentsInChildren<Transform>(true).Where(t => !keep.Contains(t)).ToList();
                foreach (var t in stale)
                {
                    if (t == null) continue;
                    if (t.parent != null && stale.Contains(t.parent)) continue;   // removed with its parent
                    r.log.Add("removed stale " + KD_SceneObjects.PathOf(t.gameObject));
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
                }
            }
        }

        // ================================================================ ground and small helpers

        /// <summary>The terrain height under a world point (TerrainCollider hit, else the covering Terrain's SampleHeight). A rock,
        /// a board or a stand is never taken for the ground. No terrain: an exception (nothing is guessed).</summary>
        static float Ground(Vector3 world)
        {
            Physics.SyncTransforms();
            var hits = Physics.RaycastAll(new Vector3(world.x, 1000f, world.z), Vector3.down, 2000f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity, y = 0f;
            foreach (var h in hits) if (h.collider is TerrainCollider && h.distance < best) { best = h.distance; y = h.point.y; }
            if (best < float.PositiveInfinity) return y;
            foreach (var t in Terrain.activeTerrains)
            {
                if (t == null || t.terrainData == null) continue;
                var p = t.transform.position; var s = t.terrainData.size;
                if (world.x >= p.x && world.x <= p.x + s.x && world.z >= p.z && world.z <= p.z + s.z) return t.SampleHeight(world) + p.y;
            }
            throw new InvalidOperationException("no terrain under " + V(world));
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
        static string V(Vector3 p) { return "(" + p.x.ToString("F2") + ", " + p.y.ToString("F2") + ", " + p.z.ToString("F2") + ")"; }

        static void WriteJson(string path, object o)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(o, true));
        }
    }
}
