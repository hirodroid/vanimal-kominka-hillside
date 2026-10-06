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
    ///   Asobikata = the roofed notice board "あそびかた", east of the spawn beside the shopkeeper, facing the spawn. Since IMP-UI-002
    ///               wave 2 (docs/plans/2026-10-03-onsite-guides.md §3): a two-line strip (how to open the hand menu / walk, wash and map
    ///               are explained at the leash racks, the wash stations and the map board) and 3 papers: frisbee (two papers wide, the
    ///               step cuts F1-F4 with number badges and captions, the note cuts N1 / N2), ball (B1 / B2), points and trainer
    ///               (unchanged). The cuts are Guide/13's atlas (KH_PlayGuideCuts.png) read through its manifest; every word over them
    ///               is a TMP label bound to the menu's own keys (.Cuts.cs). Guide/1 refuses without the atlas / manifest;
    ///   Map       = the trail-head map board (the hand map's texture KH_TravelMap.png, the 6 spots, the wash stations, "you are
    ///               here"), west of the trail start, facing the spawn / the trail;
    ///   Kominka   = IMP-WLD-006: the 古民家ドッグラン board beside the gate. Interact (KH_PortalBoard, local only) shows the scene's one
    ///               VRCPortalMarker, taken over (moved under Kominka/PortalAnchor, never re-typed) and hidden in the saved scene.
    ///               Guide/0 creates KH_PortalBoard's Program Asset (once, before the first Guide/1).
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
        static readonly Board Asob = new Board { name = "Asobikata", createXZ = new Vector2(53.0f, 44.5f), createYaw = 55f, canvasMm = new Vector2(3240f, 1440f), canvasBottom = 0.80f };   // yaw 65 -> 55 on 2026-10-06 (faces the spawn; docs/plans/2026-10-06-spawn-area-layout.md)
        static readonly Board MapBoard = new Board { name = "Map", createXZ = new Vector2(39.2f, 46.6f), createYaw = 330f, canvasMm = new Vector2(1300f, 1200f), canvasBottom = 0.80f };
        /// <summary>IMP-WLD-006: the 古民家ドッグラン board at the gate (west of the opening, 1 m out from the wall line, facing out = -26 deg like the
        /// spawn). Readers face the wall; the portal appears on their right (root +X), against the wall.</summary>
        static readonly Board Kominka = new Board { name = "Kominka", createXZ = new Vector2(44.52f, 36.90f), createYaw = 154f, canvasMm = new Vector2(1300f, 1100f), canvasBottom = 0.80f };
        static readonly Board[] Boards = { Asob, MapBoard, Kominka };

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
        // 2026-10-03 IMP-UI-002 wave 2 (docs/plans/2026-10-03-onsite-guides.md §3): the strip has two lines (+48 mm, its top edge kept),
        // the papers are 1060 -> 1010 mm (bottom edge kept), so the canvas and the board stay the same size. The papers and the cut
        // slots are in KH_GuideBoardsEditor.Cuts.cs.
        static readonly Vector2 AsobHeaderPos = new Vector2(0f, 625f), AsobHeaderPlate = new Vector2(3080f, 170f), AsobHeaderText = new Vector2(3000f, 160f);
        static readonly Vector2 StripPos = new Vector2(0f, 446f), StripSize = new Vector2(3080f, 138f);
        static readonly Vector2 StripLine1Pos = new Vector2(0f, 32f), StripLine1Size = new Vector2(3020f, 62f);
        static readonly Vector2 StripLine2Pos = new Vector2(0f, -34f), StripLine2Size = new Vector2(3020f, 56f);
        // the points / trainer paper keeps its title / body positions; only its body box is 50 mm shorter (paper 1060 -> 1010)
        static readonly Vector2 TitlePos = new Vector2(0f, 440f), BodyPos = new Vector2(0f, -55f), BodySize = new Vector2(680f, 870f);
        const float TitleH = 100f, ShopBodyMax = 32f;
        const float HeaderMax = 130f, HeaderMin = 80f, StripMax = 36f, StripMin = 22f, PointerMax = 34f, PointerMin = 22f, TitleMax = 72f, TitleMin = 44f, BodyMin = 22f, BodyLineSpacing = 12f;

        // ---------------------------------------------------------------- Map layout (canvas 1300 x 1200; the paper's children in paper-local mm)
        // 2026-10-03: the lower third was empty -> the paper is 110 mm shorter, the map 760 -> 820, the place list 38 -> 44 mm.
        static readonly Vector2 MapHeaderPos = new Vector2(0f, 525f), MapHeaderPlate = new Vector2(1240f, 130f), MapHeaderText = new Vector2(1200f, 120f);
        static readonly Vector2 MapPaperPos = new Vector2(0f, -50f), MapPaperSize = new Vector2(1240f, 1010f);
        const float MapPx = 820f;
        static readonly Vector2 MapCenter = new Vector2(-190f, 70f);
        static readonly Vector2 NorthPos = new Vector2(165f, 445f), NorthSize = new Vector2(100f, 50f);
        static readonly Vector2 NumOffset = new Vector2(30f, 26f), NumSize = new Vector2(44f, 44f);
        const float NumFont = 38f;
        static readonly Vector2 HereSize = new Vector2(26f, 26f), HereLabelPos = new Vector2(105f, -30f), HereLabelSize = new Vector2(190f, 44f);
        static readonly Vector2 WashMarkSize = new Vector2(18f, 18f);
        const float WashMarkSkipMeters = 25f;   // the main station is beside the board: "you are here" covers it
        static readonly Vector2 LegendPos = new Vector2(425f, 260f), LegendSize = new Vector2(370f, 440f);
        static readonly Vector2 KeyPos = new Vector2(425f, -150f), KeySize = new Vector2(370f, 300f);
        static readonly Vector2 NotePos = new Vector2(0f, -425f), NoteSize = new Vector2(1180f, 150f);
        const float MapTitleMax = 96f, MapTitleMin = 60f, NorthMax = 38f, NorthMin = 24f, HereMax = 32f, HereMin = 20f, LegendMax = 44f, LegendMin = 26f, KeyMax = 38f, KeyMin = 22f, NoteMax = 38f, NoteMin = 24f, LegendLineSpacing = 10f;

        // ---------------------------------------------------------------- Kominka layout (canvas 1300 x 1100), IMP-WLD-006
        static readonly Vector2 KomHeaderPos = new Vector2(0f, 465f), KomHeaderPlate = new Vector2(1240f, 130f), KomHeaderText = new Vector2(1200f, 120f);
        static readonly Vector2 KomPaperPos = new Vector2(0f, -80f), KomPaperSize = new Vector2(1240f, 920f), KomBodySize = new Vector2(1160f, 860f);
        const float KomBodyMax = 48f, KomBodyMin = 26f, KomLineSpacing = 14f;
        const string InteractName = "Interact", PortalAnchorName = "PortalAnchor";
        /// <summary>Portal anchor in board-local: on the readers' right (+X = west-south-west), on the board's line, facing the readers.</summary>
        const float PortalSideX = 1.75f, PortalYaw = 180f;
        const float PortalShowSeconds = 60f, InteractProximity = 2.5f;
        public const string PortalProgramPath = "Assets/KominkaHillside/Scripts/KH_PortalBoard";

        // ---------------------------------------------------------------- bound labels (UILocalizationBindings.json, root KH_GuideBoards)
        class LabelSpec { public string path, key; public LabelSpec(string p, string k) { path = p; key = k; } }
        /// <summary>The fixed labels. The cut captions and the callouts come from the cut manifest (CutLabels, .Cuts.cs);
        /// Ctx.labels is the whole list the scene and UILocalizationBindings.json must agree on.</summary>
        static readonly LabelSpec[] StaticLabels =
        {
            new LabelSpec("Asobikata/Canvas/Header", "guide.board.title"),
            new LabelSpec("Asobikata/Canvas/MenuStrip/Text", "guide.menu"),
            new LabelSpec("Asobikata/Canvas/MenuStrip/Pointer", "guide.pointer"),
            new LabelSpec("Asobikata/Canvas/Paper_Frisbee/Title", "guide.frisbee.title"),
            new LabelSpec("Asobikata/Canvas/Paper_Frisbee/More", "guide.frisbee.more"),
            new LabelSpec("Asobikata/Canvas/Paper_Ball/Title", "guide.ball.title"),
            new LabelSpec("Asobikata/Canvas/Paper_Ball/More", "guide.ball.more"),
            new LabelSpec("Asobikata/Canvas/Paper_Shop/Title", "guide.shop.title"),
            new LabelSpec("Asobikata/Canvas/Paper_Shop/Body", "guide.shop.body"),
            new LabelSpec("Map/Canvas/Header", "guide.map.title"),
            new LabelSpec("Map/Canvas/Paper/Legend", "guide.map.legend"),
            new LabelSpec("Map/Canvas/Paper/Key", "guide.map.key"),
            new LabelSpec("Map/Canvas/Paper/Note", "guide.map.note"),
            new LabelSpec("Map/Canvas/Paper/Here/Label", "guide.map.here"),
            new LabelSpec("Map/Canvas/Paper/North", "guide.map.north"),
            new LabelSpec("Kominka/Canvas/Header", "guide.kominka.title"),
            new LabelSpec("Kominka/Canvas/Paper/Body", "guide.kominka.body"),
        };
        const string InteractPath = "Kominka/Interact", InteractKey = "guide.kominka.interact";

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
            public VRC.SDK3.Components.VRCPortalMarker portal; public string portalRoomId;   // IMP-WLD-006: the one world portal of the scene
            // IMP-UI-002 wave 2: Guide/13's cut atlas + manifest (board A's frisbee / ball papers), the rounded backing sprite
            public KH_PlayGuideCaptureEditor.Manifest cuts; public Texture2D cutAtlas; public Sprite round;
            public List<LabelSpec> labels = new List<LabelSpec>();   // StaticLabels + the cut captions / callouts
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
            // IMP-UI-002 wave 2: board A's cuts come from Guide/13 (atlas + manifest); without them nothing is built
            c.cuts = LoadCuts(out c.cutAtlas);
            var cutProblems = CutsProblems(c.cuts, c.cutAtlas);
            if (cutProblems.Count > 0) throw new InvalidOperationException("the play guide cuts are not usable (" + string.Join("; ", cutProblems.Take(6)) + "): run Guide/13 - Capture Play Guide Cuts first; nothing changed");
            c.round = KD_UiSpriteEditor.LoadRound();
            if (c.round == null) throw new InvalidOperationException(KD_UiSpriteEditor.RoundSpritePath + " missing (the menu's rounded sprite, baked by the menu builders); nothing changed");
            c.labels = StaticLabels.Concat(CutLabels(c.cuts)).ToList();
            var missingTexts = new List<string>();
            foreach (var lang in c.catalog.languages)
                foreach (var l in c.labels)
                {
                    var e = lang.texts.FirstOrDefault(x => x.key == l.key);
                    if (e == null || string.IsNullOrWhiteSpace(e.text)) missingTexts.Add(lang.code + ":" + l.key);
                }
            if (missingTexts.Count > 0) throw new InvalidOperationException("guide texts missing in " + KD_MenuFinishEditor.CatalogPath + " (a callout key of the cut manifest that is not in this catalog needs a mirror in CalloutMirrors): " + string.Join(", ", missingTexts.Take(12)) + "; nothing changed");
            var manifest = JsonUtility.FromJson<KD_LocalizationEditor.Manifest>(File.ReadAllText(KD_LocalizationEditor.BindingsPath));
            foreach (var l in c.labels)
                if (!manifest.labels.Any(b => b.root == RootName && b.path == l.path && b.key == l.key))
                    throw new InvalidOperationException("binding missing in " + KD_LocalizationEditor.BindingsPath + ": " + RootName + "/" + l.path + " = " + l.key
                        + " (the cut manifest's captions / callouts must match the bindings; re-captured with other callouts? update the bindings); nothing changed");
            if (!(manifest.interactions ?? new KD_LocalizationEditor.Binding[0]).Any(b => b.root == RootName && b.path == InteractPath && b.key == InteractKey))
                throw new InvalidOperationException("interaction binding missing in " + KD_LocalizationEditor.BindingsPath + ": " + RootName + "/" + InteractPath + " = " + InteractKey + "; nothing changed");
            var foreign = manifest.labels.Where(b => b.root == RootName && !c.labels.Any(l => l.path == b.path)).Select(b => b.path).ToArray();
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
            // ---- IMP-WLD-006: the existing world portal is taken over, never created or retyped ----
            var markers = UnityEngine.Object.FindObjectsOfType<VRC.SDK3.Components.VRCPortalMarker>(true);
            if (markers.Length != 1) throw new InvalidOperationException(markers.Length + " VRCPortalMarker in the scene (expected exactly the one portal to Kominka Dog Run); nothing changed");
            c.portal = markers[0];
            var roomProp = new SerializedObject(c.portal).FindProperty("roomId");
            c.portalRoomId = roomProp != null ? roomProp.stringValue : null;
            if (string.IsNullOrEmpty(c.portalRoomId) || !c.portalRoomId.StartsWith("wrld_"))
                throw new InvalidOperationException("the portal " + KD_SceneObjects.PathOf(c.portal.gameObject) + " has no world id (roomId '" + c.portalRoomId + "'); nothing changed");
            var pgo = c.portal.gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(pgo) && !PrefabUtility.IsOutermostPrefabInstanceRoot(pgo))
                throw new InvalidOperationException("the portal marker is inside another prefab instance (" + KD_SceneObjects.PathOf(pgo) + "): it cannot be re-parented; nothing changed");
            if (forBuild && AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(PortalProgramPath + ".asset") == null)
                throw new InvalidOperationException(PortalProgramPath + ".asset missing: run Guide/0, compile U# (unity-compile.sh --udon), then Guide/1; nothing changed");
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
            public string kominkaPlacement;
            public List<string> log = new List<string>();
        }

        [MenuItem(Menu + "1 - Build Guide Boards")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var c = Resolve(true);
            var r = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            // IMP-WLD-006: the Kominka board's clear pose is searched with physics before anything changes (throws = nothing changed)
            var kominkaPose = FindKominkaPose(c, out r.kominkaPlacement);
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
                    if (b == Kominka && kominkaPose.HasValue)
                    {
                        bt.position = new Vector3(kominkaPose.Value.x, 0f, kominkaPose.Value.z);
                        bt.rotation = Quaternion.Euler(0f, kominkaPose.Value.y, 0f);
                        float gy = float.NegativeInfinity;
                        foreach (var sp in SeatSamples(b)) gy = Mathf.Max(gy, Ground(bt.TransformPoint(sp)));
                        bt.position = new Vector3(bt.position.x, gy, bt.position.z);
                        r.log.Add(b.name + " re-placed: " + r.kominkaPlacement);
                    }
                    BuildStructure(bt, b, c, keep);
                    var canvas = EnsureCanvas(bt, b, keep);
                    if (b == Asob) BuildAsobikataCanvas(canvas, c, keep);
                    else if (b == MapBoard) BuildMapCanvas(canvas, bt, c, keep);
                    else { BuildKominkaCanvas(canvas, c, keep); BuildPortal(bt, b, c, keep, r); }
                    RemoveStale(bt, keep, r);
                    r.boards.Add(b.name + " at " + V(bt.position) + " yaw " + bt.eulerAngles.y.ToString("F1"));
                }

                var log = new StringBuilder();
                KD_ShopSetupEditor.AddGlyphs(GlyphText(c), log);
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

        [MenuItem(Menu + "0 - Create Portal Board Program")]
        public static void CreateProgram()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("[KH Guide Boards] Edit mode only (not compiling)");
            if (AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(PortalProgramPath + ".asset") != null) { Debug.Log("[KH Guide Boards] " + PortalProgramPath + ".asset exists; nothing changed"); return; }
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(PortalProgramPath + ".cs");
            if (script == null) throw new InvalidOperationException(PortalProgramPath + ".cs not imported yet; refresh first");
            var asset = ScriptableObject.CreateInstance<UdonSharp.UdonSharpProgramAsset>();
            asset.sourceCsScript = script;
            AssetDatabase.CreateAsset(asset, PortalProgramPath + ".asset");
            AssetDatabase.SaveAssets();
            UdonSharp.UdonSharpProgramAsset.CompileAllCsPrograms(true);
            Debug.Log("[KH Guide Boards] created " + PortalProgramPath + ".asset. Compile U# (scripts/unity-compile.sh --udon), then Guide/1.");
        }

        static void SceneManagerMove(GameObject go, UnityEngine.SceneManagement.Scene scene)
        {
            if (go.scene != scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
        }

        /// <summary>The ja / en (glyphs = self) texts of the bound keys, without rich-text tags, plus the digits (map numbers, step badges).</summary>
        static string GlyphText(Ctx c)
        {
            var s = new StringBuilder("0123456789");
            foreach (var lang in c.catalog.languages)
            {
                if (!KD_LocalizationEditor.Bundled(lang)) continue;
                foreach (var l in c.labels) s.Append(Visible(KD_LocalizationEditor.Text(c.catalog, lang.code, l.key)));
            }
            return s.ToString();
        }

        static string Visible(string text) { return System.Text.RegularExpressions.Regex.Replace(text ?? "", "<[^<>]+>", ""); }

        // ================================================================ IMP-WLD-006: a clear pose for the Kominka board

        const float KomStep = 0.1f, KomMaxOut = 3.0f;
        /// <summary>The main wash station's working area in station-local metres (shelf 1.2 x 0.45 at z 0, duckboard 1.3 x 0.7 at z -1.2,
        /// 1 m free each side of the duckboard: KH_DogWashSetupEditor.Station.cs).</summary>
        const float WorkHalfX = 1.65f, WorkMinZ = -1.55f, WorkMaxZ = 0.225f;
        /// <summary>Room a player needs in front of the portal to step in (board-local -Z) and beside it (x).</summary>
        const float PortalFrontRoom = 0.8f, PortalSideRoom = 0.3f;

        /// <summary>The board volume and, separately, the portal volume (1 x 2 x 1 m + room to step in) in board-local x / z.</summary>
        static Rect PortalRect(float side) { float x = side * PortalSideX; return Rect.MinMaxRect(x - 0.5f - PortalSideRoom, BackZ - 0.5f - PortalFrontRoom, x + 0.5f + PortalSideRoom, BackZ + 0.5f); }

        /// <summary>+1 / -1: the portal goes on the board's side away from the gate opening (board-local +X or -X).</summary>
        static float PortalSide(Ctx c, Vector3 pos, Quaternion rot)
        {
            var gate = ScenePath(c.scene, "KH_World/KH_Places/KH_GateBlock");
            if (gate == null) return 1f;
            float d = Vector3.Dot(Flat3(pos - gate.position), rot * Vector3.right);
            return d >= 0f ? 1f : -1f;
        }

        /// <summary>Start points for the Kominka board, tried in order (first that passes wins). Measured from the gate (KH_GateBlock):
        /// along = metres along the wall (gate +X; negative = west), out = metres in front of the gate line (spawn side). The board stays
        /// parallel to the wall (yaw = gate yaw + 180) and steps further out from there with physics. 2026-10-03 offline estimate in
        /// docs/plans/2026-10-02-guide-boards.md §12-2.</summary>
        static readonly Vector2[] KominkaStarts = { new Vector2(-4.2f, 1.0f), new Vector2(4.2f, 1.0f), new Vector2(-5.2f, 1.0f) };
        static readonly string[] KominkaStartNames = { "gate-west 4.2 m", "gate-east 4.2 m", "gate-west 5.2 m" };

        /// <summary>Null = keep the board where it is (it exists and both volumes are clear). Otherwise the x / yaw / z (as Vector3(x, yaw, z))
        /// to put it at: from the current position (or the design point) stepped out along the board's front normal (toward the readers,
        /// away from the wall), KomStep at a time up to KomMaxOut, until the board's volume and the portal's volume hit no solid collider
        /// players bump into (the actual colliders: the wall is inside the purchased farmhouse prefab, so it is not guessed). The other
        /// placement rules are checked at the found point. Nothing found or a rule broken: an exception before any change.</summary>
        static Vector3? FindKominkaPose(Ctx c, out string how)
        {
            var log = new List<string>();
            var existing = c.root != null ? c.root.Find(Kominka.name) : null;
            if (existing != null)
            {
                // the user's position is kept only if it passes everything at its own place (no stepping)
                string why;
                var keep = TryKominka(c, existing.position, existing.eulerAngles.y, 0f, out why);
                if (keep.HasValue) { how = "kept at " + V(existing.position) + " yaw " + existing.eulerAngles.y.ToString("F1") + " (clear, all rules pass)"; return null; }
                log.Add("current " + V(existing.position) + ": " + why);
            }
            var gate = ScenePath(c.scene, "KH_World/KH_Places/KH_GateBlock");
            for (int k = 0; k < KominkaStarts.Length; k++)
            {
                Vector3 start; float yaw;
                if (gate != null)
                {
                    var right = gate.right; right.y = 0f; right.Normalize();
                    var outward = gate.forward; outward.y = 0f; outward.Normalize();
                    var spawn = ScenePath(c.scene, "KH_World/KH_Places/KH_Entrance");
                    if (spawn != null && Vector3.Dot(spawn.position - gate.position, outward) < 0f) outward = -outward;   // "out" = the spawn's side
                    start = gate.position + right * KominkaStarts[k].x + outward * KominkaStarts[k].y;
                    yaw = Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg;   // root +Z into the wall: the readers face the wall
                }
                else { start = new Vector3(Kominka.createXZ.x, 0f, Kominka.createXZ.y); yaw = Kominka.createYaw; }
                string why;
                var got = TryKominka(c, start, yaw, KomMaxOut, out why);
                if (got.HasValue) { how = KominkaStartNames[k] + ": " + why + (log.Count > 0 ? " | rejected before: " + string.Join(" | ", log) : ""); return got; }
                log.Add(KominkaStartNames[k] + " " + V(start) + ": " + why);
            }
            throw new InvalidOperationException("Kominka board: no start point passes: " + string.Join(" | ", log) + "; nothing changed");
        }

        /// <summary>From a start point, step out along the front normal (0.1 m up to maxOut) to the first point where the board volume
        /// and the portal volume are clear of solid colliders, then check the rules there. Returns Vector3(x, yaw, z) or null (why says why).</summary>
        static Vector3? TryKominka(Ctx c, Vector3 start, float yaw, float maxOut, out string why)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 front = rot * Vector3.back;   // the readers' side (the Canvas faces -Z)
            var tried = new List<string>();
            for (float d = 0f; d <= maxOut + 1e-4f; d += KomStep)
            {
                var pos = start + front * d; pos.y = 0f;
                float gy = float.NegativeInfinity;
                foreach (var sp in SeatSamples(Kominka)) gy = Mathf.Max(gy, Ground(pos + rot * sp));
                pos.y = gy;
                float side = PortalSide(c, pos, rot);
                var hb = SolidIn(c, pos, rot, Footprint(Kominka), gy + 0.05f, gy + 2.8f);
                var portalGround = Ground(pos + rot * new Vector3(side * PortalSideX, 0f, BackZ));
                var hp = SolidIn(c, pos, rot, PortalRect(side), portalGround + 0.05f, portalGround + 2.1f);
                if (hb.Count > 0 || hp.Count > 0)
                {
                    if (tried.Count < 3) tried.Add(d.ToString("F1") + " m blocked by " + string.Join(", ", hb.Concat(hp).Distinct().Take(2)));
                    continue;
                }
                var broken = KominkaRules(c, pos, rot);
                if (broken.Count > 0) { why = "first clear point " + d.ToString("F1") + " m out " + V(pos) + " breaks " + string.Join("; ", broken); return null; }
                why = "stepped " + d.ToString("F1") + " m out from " + V(start) + " to " + V(pos) + " yaw " + yaw.ToString("F1") + "; portal at "
                    + V(pos + rot * new Vector3(side * PortalSideX, 0f, BackZ)) + (tried.Count > 0 ? " (" + string.Join("; ", tried) + ")" : "");
                return new Vector3(pos.x, yaw, pos.z);
            }
            why = "no clear point within " + maxOut + " m (" + string.Join("; ", tried) + ")";
            return null;
        }

        static List<string> SolidIn(Ctx c, Vector3 pos, Quaternion rot, Rect r, float bottom, float top)
        {
            Physics.SyncTransforms();
            var centre = pos + rot * new Vector3(r.center.x, 0f, r.center.y); centre.y = (bottom + top) * 0.5f;
            return Physics.OverlapBox(centre, new Vector3(r.width * 0.5f, (top - bottom) * 0.5f, r.height * 0.5f), rot, ~0, QueryTriggerInteraction.Ignore)
                .Where(x => !(x is TerrainCollider) && (c.root == null || !x.transform.IsChildOf(c.root)) && !Physics.GetIgnoreLayerCollision(x.gameObject.layer, 10))
                .Select(x => KD_SceneObjects.PathOf(x.gameObject)).Distinct().ToList();
        }

        /// <summary>The Guide/2 rules that a step toward the spawn could break (Guide/2 checks all of them again after the build).</summary>
        static List<string> KominkaRules(Ctx c, Vector3 pos, Quaternion rot)
        {
            var bad = new List<string>();
            var a = FindAnchors(c);
            var foot = new List<Vector3>();
            var fr = Footprint(Kominka);
            for (int i = 0; i <= 8; i++) for (int j = 0; j <= 2; j++) foot.Add(pos + rot * new Vector3(fr.xMin + fr.width * i / 8f, 0f, fr.yMin + fr.height * j / 2f));
            var portal = pos + rot * new Vector3(PortalSide(c, pos, rot) * PortalSideX, 0f, BackZ);
            var readers = new List<Vector3>();
            var rr = ReaderArea(Kominka);
            for (int i = 0; i <= 6; i++) for (int j = 0; j <= 4; j++) readers.Add(pos + rot * new Vector3(rr.xMin + rr.width * i / 6f, 0f, rr.yMin + rr.height * j / 4f));
            if (a.spawn != null)
            {
                float ds = foot.Min(q => Flat(q - a.spawn.position)), dp = Flat(portal - a.spawn.position);
                if (ds < SpawnClear) bad.Add("board " + ds.ToString("F2") + " m from the spawn (< " + SpawnClear + ")");
                if (dp < PortalSpawnClear) bad.Add("portal " + dp.ToString("F2") + " m from the spawn (< " + PortalSpawnClear + ")");
                float bb = Bearing(a.spawn.position, pos);
                foreach (var t in new[] { a.gate, a.kominka })
                    if (t != null && Mathf.Abs(Mathf.DeltaAngle(bb, Bearing(a.spawn.position, t.position))) < FarmhouseSectorMin) bad.Add("in the turn-around view to " + t.name);
                if (a.gate != null)
                {
                    // the gate line follows the gate itself (KH_GateBlock faces out along the wall's normal), not the spawn's look direction (352 deg since 2026-10-06)
                    Vector3 fwd = a.gate.forward; fwd.y = 0f; fwd.Normalize();
                    float side = foot.Min(q => Vector3.Dot(Flat3(q - a.gate.position), fwd));
                    if (side < GateSideMinKominka) bad.Add("only " + side.ToString("F2") + " m in front of the gate line");
                    // the gate opening (KH_GateBlock is 4.5 m wide): keep the footprint (roof overhang included) 0.5 m clear of its ends along the wall
                    var gr = a.gate.right; gr.y = 0f; gr.Normalize();
                    float along = foot.Min(q => Mathf.Abs(Vector3.Dot(Flat3(q - a.gate.position), gr)));
                    if (along < 2.25f + 0.5f) bad.Add("within 0.5 m of the gate opening (" + along.ToString("F2") + " m from its centre along the wall)");
                }
            }
            if (a.route != null && a.route.Length > 1)
            {
                int n = a.route.Length, segs = a.closed ? n : n - 1; float rb = float.PositiveInfinity, rp = float.PositiveInfinity;
                for (int i = 0; i < segs; i++)
                {
                    foreach (var q in foot) rb = Mathf.Min(rb, SegDist(q, a.route[i], a.route[(i + 1) % n]));
                    rp = Mathf.Min(rp, SegDist(portal, a.route[i], a.route[(i + 1) % n]));
                }
                if (rb < RouteClear) bad.Add("board " + rb.ToString("F2") + " m from the loop line");
                if (rp < 2.5f) bad.Add("portal " + rp.ToString("F2") + " m from the loop line");
            }
            if (a.washFloor != null && a.washMain != null)
            {
                if (foot.Min(q => OutsideSquare(q, a.washFloor.position, a.washMain.rotation, WashFloorHalf)) < WashFloorClear) bad.Add("board on / near the wash floor");
                if (OutsideSquare(portal, a.washFloor.position, a.washMain.rotation, WashFloorHalf) < 1.0f) bad.Add("portal on / near the wash floor");
                // Relaxed for this board (2026-10-03): its readers may stand on the edge of the 5 x 5 m personal floor (an invisible floor that
                // only rises for the washer who raises it), but not within 1 m of the working area (shelf + duckboard + 1 m each side) and not
                // within 4 m of the station (the dogs' keep-out around a station).
                var inv = Quaternion.Inverse(Quaternion.Euler(0f, a.washMain.eulerAngles.y, 0f));
                bool work = readers.Concat(new[] { portal }).Any(q => { var l = inv * (q - a.washMain.position); return Mathf.Abs(l.x) < WorkHalfX + 1f && l.z > WorkMinZ - 1f && l.z < WorkMaxZ + 1f; });
                if (work) bad.Add("readers / portal within 1 m of the wash station's working area");
                float centre = Flat(pos + rot * new Vector3(0f, 0f, rr.center.y) - a.washMain.position);
                if (centre < 4f) bad.Add("readers " + centre.ToString("F1") + " m from the wash station (< 4)");
            }
            // boards A / B have no colliders, so physics does not see them
            foreach (var other in new[] { Asob, MapBoard })
            {
                var ot = c.root != null ? c.root.Find(other.name) : null;
                if (ot == null) continue;
                var of = Footprint(other);
                float best = float.PositiveInfinity;
                foreach (var q in foot.Concat(new[] { portal }).Concat(readers))
                {
                    var l = ot.InverseTransformPoint(q);
                    float dx = Mathf.Max(Mathf.Max(of.xMin - l.x, l.x - of.xMax), 0f), dz = Mathf.Max(Mathf.Max(of.yMin - l.z, l.z - of.yMax), 0f);
                    best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
                }
                if (best < 1.5f) bad.Add("board / portal / readers " + best.ToString("F2") + " m from board " + other.name + " (< 1.5)");
            }
            foreach (var t in new[] { a.rack, a.keeper, a.welcome }.Concat(a.treats))
                if (t != null && foot.Concat(new[] { portal }).Min(q => Flat(q - t.position)) < FixtureClear) bad.Add("within " + FixtureClear + " m of " + t.name);
            return bad;
        }

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
            // two lines: how to open the hand menu (as before) / where the walk, wash and map explanations are (IMP-UI-002)
            var strip = Img(canvas, "MenuStrip", StripPos, StripSize, StripWashi, null, keep);
            Txt(strip.transform, "Text", StripLine1Pos, StripLine1Size, StripMax, StripMin, TextAlignmentOptions.Center, Sumi, false, c, keep);
            Txt(strip.transform, "Pointer", StripLine2Pos, StripLine2Size, PointerMax, PointerMin, TextAlignmentOptions.Center, Sumi, false, c, keep);
            foreach (var spec in Papers)
            {
                var paper = Img(canvas, spec.name, new Vector2(spec.x, PaperY), new Vector2(spec.width, PaperH), Washi, null, keep);
                Txt(paper.transform, "Title", TitlePos, new Vector2(spec.width - 60f, TitleH), TitleMax, TitleMin, TextAlignmentOptions.Center, spec == PShop ? Vermilion : Sumi, false, c, keep);
                if (spec == PShop)
                {
                    // DES-UI-005: unchanged content (only 50 mm shorter)
                    var body = Txt(paper.transform, "Body", BodyPos, BodySize, ShopBodyMax, BodyMin, TextAlignmentOptions.TopLeft, Sumi, true, c, keep);
                    body.lineSpacing = BodyLineSpacing; EditorUtility.SetDirty(body);
                }
                else BuildCutPaper(paper.rectTransform, spec, c, keep);
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
            var legend = Txt(paper, "Legend", LegendPos, LegendSize, LegendMax, LegendMin, TextAlignmentOptions.TopLeft, Sumi, true, c, keep);
            legend.lineSpacing = LegendLineSpacing; EditorUtility.SetDirty(legend);
            Txt(paper, "Key", KeyPos, KeySize, KeyMax, KeyMin, TextAlignmentOptions.TopLeft, Sumi, true, c, keep);
            Txt(paper, "Note", NotePos, NoteSize, NoteMax, NoteMin, TextAlignmentOptions.TopLeft, Sumi, true, c, keep);
            // draw order: map, wash squares, numbers, here, then the rest
            mapGo.transform.SetSiblingIndex(0);
        }

        static void BuildKominkaCanvas(RectTransform canvas, Ctx c, HashSet<Transform> keep)
        {
            Img(canvas, "HeaderPlate", KomHeaderPos, KomHeaderPlate, HeaderPlateColor, null, keep);
            Txt(canvas, "Header", KomHeaderPos, KomHeaderText, MapTitleMax, MapTitleMin, TextAlignmentOptions.Center, SignInk, false, c, keep);
            var paper = Img(canvas, "Paper", KomPaperPos, KomPaperSize, Washi, null, keep);
            var body = Txt(paper.transform, "Body", Vector2.zero, KomBodySize, KomBodyMax, KomBodyMin, TextAlignmentOptions.TopLeft, Sumi, true, c, keep);
            body.lineSpacing = KomLineSpacing; EditorUtility.SetDirty(body);
        }

        /// <summary>IMP-WLD-006: the Interact target (a trigger over the board's face, Default layer like the wash board's UI shape:
        /// players walk through it, the dogs' ground rays and every QueryTriggerInteraction.Ignore query skip it) with KH_PortalBoard,
        /// and the scene's one VRCPortalMarker moved under PortalAnchor (inactive in the saved scene). The marker's own values (world id,
        /// presentation) are never written; only its parent and local pose change.</summary>
        static void BuildPortal(Transform board, Board b, Ctx c, HashSet<Transform> keep, BuildReport r)
        {
            var it = Child(board, InteractName, keep);
            it.localPosition = Vector3.zero; it.localRotation = Quaternion.identity; it.localScale = Vector3.one;
            foreach (var col in it.GetComponents<Collider>()) if (!(col is BoxCollider)) UnityEngine.Object.DestroyImmediate(col);
            var box = it.GetComponent<BoxCollider>(); if (box == null) box = it.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true; box.center = new Vector3(0f, CanvasCenterY(b), -0.03f); box.size = new Vector3(BackW(b), BackH(b), 0.06f);
            EditorUtility.SetDirty(box);

            var anchor = Child(board, PortalAnchorName, keep);
            float side = PortalSide(c, board.position, board.rotation);
            var spot = board.TransformPoint(new Vector3(side * PortalSideX, 0f, BackZ));
            anchor.localPosition = new Vector3(side * PortalSideX, Ground(spot) - board.position.y, BackZ);
            anchor.localRotation = Quaternion.Euler(0f, PortalYaw, 0f); anchor.localScale = Vector3.one;
            var mt = c.portal.transform;
            if (mt.parent != anchor)
            {
                r.log.Add("took over the portal " + KD_SceneObjects.PathOf(mt.gameObject) + " (world " + c.portalRoomId + ") from " + V(mt.position) + " yaw " + mt.eulerAngles.y.ToString("F1") + "; its old parent is left as is");
                mt.SetParent(anchor, false);
            }
            mt.localPosition = Vector3.zero; mt.localRotation = Quaternion.identity; mt.localScale = Vector3.one;
            if (!mt.gameObject.activeSelf) mt.gameObject.SetActive(true);
            anchor.gameObject.SetActive(false);   // hidden for everyone at load; KH_PortalBoard shows it per player
            foreach (var t in mt.GetComponentsInChildren<Transform>(true)) keep.Add(t);

            var pb = it.GetComponent<KH_PortalBoard>();
            if (pb == null) { pb = it.gameObject.AddUdonSharpComponent<KH_PortalBoard>(); r.log.Add("added KH_PortalBoard"); }
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(pb);
            if (backing == null) throw new InvalidOperationException("KH_PortalBoard backing missing (compile U# after Guide/0, then Guide/1 again; reopen the scene without saving)");
            if (backing.SyncMethod != VRC.SDKBase.Networking.SyncType.None) { backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None; EditorUtility.SetDirty(backing); }
            backing.proximity = InteractProximity; EditorUtility.SetDirty(backing);
            pb.portal = anchor.gameObject; pb.showSeconds = PortalShowSeconds;
            UdonSharpEditorUtility.CopyProxyToUdon(pb); EditorUtility.SetDirty(pb);
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
            t.lineSpacing = 0f;   // the callers set it where it differs
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
                if (t.name != StructureName && t.name != CanvasName && !(board.name == Kominka.name && (t.name == InteractName || t.name == PortalAnchorName))) r.log.Add("WARN " + board.name + "/" + t.name + " is not made by this builder: left as is");
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
