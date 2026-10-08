using System;
using System.Collections.Generic;
using System.Linq;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    // IMP-DOG-025 builder: the tool shelf KH_WashStation (scene root) with 2 sponges, 2 showers and 1 towel.
    // IMP-ENV-007 (main wash station, docs/plans/2026-09-30-wash-station-layout.md): a low wooden stand (top "Shelf" +
    // 4 legs), the duckboard "WashTable" 1.2 m in front of it (station local -Z, toward the spawn), a tub, a small board
    // sign, and a home Transform per tool under "Homes" (KH_WashTool.home), and the reset button "ResetButton" (KH_WashStationReset: sends every tool nobody holds
    // home; the automatic return is off by the user's decision 2026-09-30, returnSeconds 0).
    // Everything is a child of KH_WashStation with code-owned local transforms, so it moves with the station. The
    // station's own position and rotation are human-owned once it exists (never written here after creation); the
    // duckboard and the tub are seated on the ground under them on every Build (run 11 again after moving the station).
    //
    // IMP-ENV-008 (stations along the course, docs/plans/2026-09-30-wash-station-layout.md §6): the table CourseStations
    // (River / Upper / Hillside) builds the same parts (BuildStationParts: stand, duckboard, tub, sign, homes, tools, reset)
    // under KH_WashStations/{key} with 1 sponge + 1 shower + 1 towel (KH_Wash{Tool}_{key}_{n}). A station that exists keeps
    // its position and rotation (human-owned); a missing one is placed at the first candidate that passes PlacementChecks
    // (slope, the path, the river, the dirt zones, the other stations, the hand-placed spots, obstacles, NavMesh), or is left
    // out with a WARN in the Build log (Wash/12 then fails on it). BuildStation returns the tools of every station (the main
    // station's first, in ToolSpecs order), so the main Build wires them all to every dog (KH_WashTool.washes,
    // KH_DogWash.tools, the spray triggers, the proxy copy) without a change there. KH_DogWash.washStations gets every
    // station (main first) here; the main Build copies the dogs afterwards.
    public static partial class KH_DogWashSetupEditor
    {
        class ToolSpec
        {
            public string name; public int tool; public Vector3 slot; public Vector3 euler;
            public ToolSpec(string name, int tool, Vector3 slot, Vector3 euler) { this.name = name; this.tool = tool; this.slot = slot; this.euler = euler; }
        }

        const float ShelfHeight = 0.8f, ShelfWidth = 1.2f, ShelfDepth = 0.45f;
        // ---- IMP-ENV-007 layout (station local; +X east, -Z = the front, toward the spawn) ----
        const float ShelfTopThickness = 0.04f, ShelfLegSize = 0.05f;
        static readonly Vector2 ShelfLegInset = new Vector2(0.05f, 0.04f);             // leg centre from the top's corner (x, z)
        const float TableFront = 1.2f;                                                  // duckboard centre: 1.2 m in front of the shelf centre
        // IMP-ENV-011 (2026-10-01, coordinator): the called dog stands parallel to the shelf (along X), so the board is 1.3 m along X
        // (a Lab / Samoyed is 0.9-1.0 m long) x 0.7 m deep; its back edge is 0.625 m from the shelf front (>= 0.5: people stand there too).
        // Was 0.6 x 0.9 (the dog along Z). Top 0.03 m above the ground.
        static readonly Vector3 TableSize = new Vector3(1.3f, 0.03f, 0.7f);
        const int TableSlats = 5;
        const float SideMargin = 1.0f;                                                  // free standing room on each side (±X) of the duckboard
        static readonly Vector3 SignSize = new Vector3(0.34f, 0.12f, 0.02f);
        const float SignZ = 0.19f;                                                      // at the back of the shelf top, facing the front (-Z)
        const string SignText = "あらいば";                                             // 2026-10-03 user: 「あらいば」 (glyphs are checked by Wash/12)
        const string SignPlaceholder = "WASH";                                          // the former ASCII placeholder, overwritten by the builder
        const string HomesName = "Homes", TableName = "WashTable", TubName = "Tub", SignName = "Sign", SignTextName = "SignText";
        // 2026-09-30 (user): no automatic return; the reset button sends the tools home. 0 = never returns by itself.
        const float ReturnSeconds = 0f, HomeTolerance = 0.05f;
        // ---- the reset button: a small board hanging under the top's front edge (east), below the towels ----
        const string ResetName = "ResetButton", ResetTextName = "ResetText", ResetProgram = "KH_WashStationReset";
        static readonly Vector3 ResetLocal = new Vector3(0.40f, 0.72f, -(ShelfDepth * 0.5f) - 0.012f);
        static readonly Vector3 ResetSize = new Vector3(0.22f, 0.10f, 0.02f);
        const string ResetInteractText = "Reset tools", ResetLabel = "RESET";   // ASCII placeholders; the Japanese is typed in the Inspector
        /// <summary>Not the tool's own name (a scene-wide search by the tool's name stays unique).</summary>
        static string HomeName(ToolSpec s) { return "Home_" + s.name; }
        const string WoodMatPath = "Assets/KominkaHillside/Data/Environment/River/KH_RiverWood.mat";
        // Sponge yellow and shower-head silver are the shared tool materials (Dog.cs); the towel gets its own unbleached one here.
        static readonly Color TowelKinari = new Color(0.93f, 0.89f, 0.80f);
        const string TowelKinariMat = "KH_WashTool_TowelKinari";
        static readonly Color SignInk = new Color(0xF2 / 255f, 0xE8 / 255f, 0xD0 / 255f, 1f);
        static readonly ToolSpec[] ToolSpecs =
        {
            // IMP-ENV-007: 2 sets (design §2). Towels turned 90° (0.20 m along X, 0.30 m along Z) so both fit the 1.2 m top.
            new ToolSpec("KH_WashSponge_0", KH_DogWash.ToolSponge, new Vector3(-0.52f, ShelfHeight + 0.031f, 0f), Vector3.zero),
            new ToolSpec("KH_WashSponge_1", KH_DogWash.ToolSponge, new Vector3(-0.36f, ShelfHeight + 0.031f, 0f), Vector3.zero),
            new ToolSpec("KH_WashShower_0", KH_DogWash.ToolShower, new Vector3(-0.18f, ShelfHeight + 0.081f, 0f), Vector3.zero),
            new ToolSpec("KH_WashShower_1", KH_DogWash.ToolShower, new Vector3(-0.02f, ShelfHeight + 0.081f, 0f), Vector3.zero),
            new ToolSpec("KH_WashTowel_0", KH_DogWash.ToolTowel, new Vector3(0.22f, ShelfHeight + 0.011f, 0f), new Vector3(0f, 90f, 0f)),
            new ToolSpec("KH_WashTowel_1", KH_DogWash.ToolTowel, new Vector3(0.46f, ShelfHeight + 0.011f, 0f), new Vector3(0f, 90f, 0f)),
            // IMP-DOG-039 (docs/plans/2026-10-03-hand-wash.md §2.1): one shampoo bottle at the front-left of the top (easy to take), in
            // front of the gap between the sponges. Not in front of the sign (|x| <= 0.17: it hid 「あらいば」, 2026-10-03 render).
            // Foot box x -0.47..-0.41, z -0.15..-0.09: 5 cm in front of both sponges (z -0.04..0.04), 19 cm from shower _0, 24 cm
            // from the sign's span, 13 cm / 7.5 cm inside the top's left / front edges. The spout leans toward -X (BottleYaw).
            new ToolSpec(BottlePrefix + "0", KH_DogWash.ToolBottle, new Vector3(-0.44f, ShelfHeight + BottleRestY, -0.12f), new Vector3(0f, BottleYaw, 0f)),
        };
        /// <summary>VRCPickup.InteractionText by tool id (KH_DogWash.Tool*: 0 sponge, 1 shower, 2 towel, 3 = ToolDirt (not a tool), 4 bottle).
        /// ASCII placeholders like the others (the 13-language tool words are out of scope, design §2.1).</summary>
        static readonly string[] ToolLabels = { "Sponge", "Shower", "Towel", "", "Shampoo" };
        /// <summary>VRCPickup.UseText by tool id (written only over a placeholder: ToolUsePlaceholders).</summary>
        static readonly string[] ToolUseTexts = { "Wash", "Shower on/off", "Wash", "", "Foam" };
        static readonly string[] ToolUsePlaceholders = { "Use", "Spray", "Wash", "Shower on/off", "Foam" };
        /// <summary>The name prefixes of every kind of station tool (stale-tool cleanup in BuildStationParts).</summary>
        static readonly string[] ToolPrefixes = { "KH_WashSponge_", "KH_WashShower_", "KH_WashTowel_", BottlePrefix };

        // ---- IMP-DOG-039: the shampoo bottle (§2.1), built from primitives (no imported art; Art.cs leaves it alone) ----
        const string BottlePrefix = "KH_WashBottle_";
        const string ShampooMat = "KH_WashTool_Shampoo";                         // under ToolMatDir, like the other tool materials
        static readonly Color ShampooPink = new Color(0.98f, 0.78f, 0.84f);      // pale pink
        const string ShampooCapMat = "KH_WashTool_ShampooCap";                   // the cap and the spout
        static readonly Color ShampooCapWhite = new Color(0.96f, 0.96f, 0.95f);
        /// <summary>The bottle's materials are matte plastic. VRChat/Mobile/Standard Lite defaults to _Metallic 1 / _Glossiness 1,
        /// which mirrors the sky: the first build rendered the whole bottle saturated blue (2026-10-03). Wash/11 rewrites these on
        /// the existing assets every run.</summary>
        const float BottleMetallic = 0f, BottleGlossiness = 0.35f;
        /// <summary>Root height above the shelf top: the body's foot (0.085 below the root) sits on the top.</summary>
        const float BottleRestY = 0.085f;
        /// <summary>Resting yaw: the spout (bottle local +Z) points along the shelf (-X), so its tilt reads in profile from the front.</summary>
        const float BottleYaw = -90f;
        const float BottleBodyDiameter = 0.06f, BottleBodyHeight = 0.15f, BottleCapDiameter = 0.03f, BottleCapHeight = 0.02f;
        const float BottleSpoutDiameter = 0.012f, BottleSpoutLength = 0.03f, BottleSpoutTilt = 30f;   // tilted forward (+Z)
        const string BottleSquirtName = "Squirt";
        /// <summary>KH_WashTool's bottle fields (tool 4 only; design §8.1). Wash/11 writes them on every bottle, Wash/12 compares proxy and backing.</summary>
        static readonly (string field, float value)[] BottleTuning =
        {
            ("bottleReach", 0.6f),
            ("bottleAimRadius", 0.02f),
            ("bottleSquirtSeconds", 0.25f),
            ("bottleRepeatSeconds", 0.4f),
            ("bottleHapticDuration", 0.06f),
            ("bottleHapticAmplitude", 0.35f),
        };

        static List<KH_WashTool> BuildStation(SceneCtx c, Shared shared, Audio audio, BuildReport r)
        {
            var scene = c.dogsRoot.gameObject.scene;
            var roots = scene.GetRootGameObjects().Where(g => g.name == StationName).ToArray();
            if (roots.Length > 1) throw new InvalidOperationException(roots.Length + " " + StationName + " at the scene root (make it one, then run again)");
            if (KominkaDogRun.Editor.KD_SceneObjects.Find(StationName, out int anywhere) != null && anywhere != roots.Length)
                throw new InvalidOperationException(StationName + " exists below another object (move it to the scene root, then run again)");
            GameObject station;
            if (roots.Length == 1) { station = roots[0]; r.stationKept = true; }
            else
            {
                station = new GameObject(StationName);
                var p = c.rack.position + new Vector3(StationOffsetX, 0f, 0f);
                p.y = Ground(p.x, p.z, c.rack.position.y);
                station.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, c.rack.eulerAngles.y, 0f));
                r.log.Add("created " + StationName + " at " + p.ToString("F2") + " (" + RackName + " x " + StationOffsetX + " m, ground)");
            }
            station.transform.localScale = Vector3.one;
            r.stationPosition = station.transform.position;

            var wood = AssetDatabase.LoadAssetAtPath<Material>(WoodMatPath);
            if (wood == null) { wood = shared.shelf; r.log.Add("WARN " + WoodMatPath + " missing: the stand uses " + (shared.shelf != null ? shared.shelf.name : "null")); }
            var tools = BuildStationParts(station.transform, ToolSpecs, wood, shared, audio, r);

            // ---- IMP-ENV-008: the stations along the course (same parts, same tools, wired to the same dogs by the caller) ----
            var stations = new List<Transform> { station.transform };
            tools.AddRange(BuildCourseStations(scene, stations, wood, shared, audio, r));
            WireStationList(stations, r);
            // ---- IMP-ENV-011 / IMP-DOG-035: Call, PersonalFloor and the board on every station (the dogs' washCalls: WireWanders) ----
            // IMP-DOG-037: the board's words come from the catalog (13 languages) through KD_Language (KD_TreatPouch.language)
            var catalog = BoardCatalog();
            var language = BoardLanguage(r);
            var guideTex = EnsureGuideTexture(r);   // IMP-DOG-039: the guide's picture (Guide.cs), before any station wires it
            foreach (var st in stations) BuildStationControls(st, StationIndexOf(st), catalog, language, guideTex, r);
            ApplyBoardLanguage(catalog, language, r);
            SetFloorPassthrough(r);
            foreach (var st in stations) SetStandLayers(st, r);   // last: BuildStand / Prim put the parts back on Default
            return tools;
        }

        /// <summary>The parts every wash station has (the main one and those along the course): the stand, the duckboard, the
        /// tub, the sign, a home per tool under Homes, the tools, and the reset button wired to this station's tools only.</summary>
        static List<KH_WashTool> BuildStationParts(Transform st, ToolSpec[] specs, Material wood, Shared shared, Audio audio, BuildReport r)
        {
            BuildStand(st, wood, shared.shelf, r);

            // ---- homes (one per tool; the tools rest there and return there) ----
            var homes = Child(st, HomesName);
            homes.gameObject.layer = 0;
            homes.localPosition = Vector3.zero; homes.localRotation = Quaternion.identity; homes.localScale = Vector3.one;
            foreach (Transform h in homes.Cast<Transform>().ToArray())
                if (!specs.Any(s => HomeName(s) == h.name)) { r.log.Add("removed stale home " + st.name + "/" + h.name); UnityEngine.Object.DestroyImmediate(h.gameObject); }
            var towelMat = ToolMaterial(TowelKinariMat, TowelKinari);

            var tools = new List<KH_WashTool>();
            foreach (var spec in specs)
            {
                var home = Child(homes, HomeName(spec));
                home.gameObject.layer = 0;
                home.localPosition = spec.slot; home.localRotation = Quaternion.Euler(spec.euler); home.localScale = Vector3.one;
                foreach (var comp in home.GetComponents<Component>()) if (!(comp is Transform)) UnityEngine.Object.DestroyImmediate(comp);   // a bare marker
                var built = BuildTool(st, spec, shared, audio, home, towelMat);
                tools.Add(built);
                if (spec.tool == KH_DogWash.ToolBottle)
                    r.bottles.Add(st.name + "/" + spec.name + " home " + spec.slot.ToString("F3") + " yaw " + spec.euler.y + ", nozzle " + built.nozzle.localPosition.ToString("F3")
                        + ", squirt " + (built.squirtParticles != null ? "yes" : "MISSING") + ", tuning " + string.Join(" ", BottleTuning.Select(b => b.field + "=" + b.value)));
            }
            // stale tools of an earlier layout
            foreach (Transform t in st.Cast<Transform>().ToArray())
                if (ToolPrefixes.Any(p => t.name.StartsWith(p)) && !specs.Any(s => s.name == t.name))
                { r.log.Add("removed stale tool " + st.name + "/" + t.name); UnityEngine.Object.DestroyImmediate(t.gameObject); }
            BuildReset(st, tools, shared.shelf, r);
            return tools;
        }

        // ================================================================ IMP-ENV-008: the stations along the course

        const string CourseRootName = "KH_WashStations";
        const int PlaceRiver = 0, PlaceUpper = 1, PlaceHillside = 2;
        // placement rules (docs/plans/2026-09-30-wash-station-layout.md §6-2)
        const float CourseSlopeMax = 5f;          // the plane fitted over the working area (shelf + duckboard + side room), degrees
        const float CourseRouteClear = 2f;        // shelf / duckboard / tub from the loop's centre line (KH_WalkRoute.nodePositions)
        const float CourseDirtClear = 15f;        // station origin from each KH_DirtZones child (horizontal)
        const float CourseStationSpacing = 20f;   // between two stations' origins (dirtStationRadius 6 each, never overlapping)
        const float CourseHandClear = 1.5f;       // from the hand-placed things (water sign and stones, bench, stopovers), as Wash/12 for the main one
        const float CourseEntryClear = 3f;        // from the water-play entry (KH_WaterSpot.entry)
        const float CourseCorridorClear = 2f;     // from the walk to the water: the 川辺 node -> the entry
        const float CourseViewClear = 1.5f;       // from the bench's view line toward KH_FarKominka (15 m)
        const float CourseViewLength = 15f;
        const float CourseRiverBand = 3f;         // no part of the station within the river band + this margin (origin radius)
        const float CourseNavReach = 1f;          // the duckboard centre within this of the NavMesh (the dogs can reach it)
        const float RiverAnchorMin = 5f, RiverAnchorMax = 8f;   // River: origin from the 川辺 destination node

        class StationSpec
        {
            public string key, label, why; public int place; public float warpClear, baseClear; public ToolSpec[] tools;
            public StationSpec(string key, string label, int place, float warpClear, float baseClear, string why)
            { this.key = key; this.label = label; this.place = place; this.warpClear = warpClear; this.baseClear = baseClear; this.why = why; tools = CourseToolSpecs(key); }
        }

        /// <summary>道中の洗い場の表。道具は軽い構成 (スポンジ 1・シャワー 1・タオル 1 + リセット)。warpClear / baseClear = ワープ地点 /
        /// リード拠点から外形までの最小距離 (m)。候補は CourseCandidates (上から順に試し、最初に全部の条件を通ったもの)。</summary>
        static readonly StationSpec[] CourseStations =
        {
            new StationSpec("River", "川辺", PlaceRiver, 3.5f, CourseHandClear, "水遊びの後にそのまま洗える。目的地「川辺」から 5〜8 m、川と反対側、入水の導線・看板・石の外"),
            new StationSpec("Upper", "奥広場", PlaceUpper, 5f, 5f, "ワープで着いて遊んだ後に洗える。ワープ地点と拠点から 5 m 以上、泥の地点 (奥広場の縁) の反対側"),
            new StationSpec("Hillside", "展望のベンチ", PlaceHillside, 3.5f, CourseHandClear, "長い散歩の途中の休憩点。ベンチの横 (道に沿って)、ベンチから古民家への眺めを塞がない"),
        };

        static ToolSpec[] CourseToolSpecs(string key)
        {
            // one of each on the same 1.2 m top; the towel turned 90° like the main one's. The reset board hangs at x 0.40 below the edge.
            return new[]
            {
                new ToolSpec("KH_WashSponge_" + key + "_0", KH_DogWash.ToolSponge, new Vector3(-0.40f, ShelfHeight + 0.031f, 0f), Vector3.zero),
                new ToolSpec("KH_WashShower_" + key + "_0", KH_DogWash.ToolShower, new Vector3(-0.12f, ShelfHeight + 0.081f, 0f), Vector3.zero),
                new ToolSpec("KH_WashTowel_" + key + "_0", KH_DogWash.ToolTowel, new Vector3(0.22f, ShelfHeight + 0.011f, 0f), new Vector3(0f, 90f, 0f)),
                // IMP-DOG-039 §2.1: one bottle at the front, between the sponge and the shower, clear of the sign (|x| <= 0.17).
                // Foot box x -0.29..-0.23, z -0.15..-0.09: 5 cm from the sponge (x -0.46..-0.34), 6 cm from the sign's span
                new ToolSpec(BottlePrefix + key + "_0", KH_DogWash.ToolBottle, new Vector3(-0.26f, ShelfHeight + BottleRestY, -0.12f), new Vector3(0f, BottleYaw, 0f)),
            };
        }

        /// <summary>Tools of every station in the builder's order (main ToolSpecs, then CourseStations): how many
        /// KH_DogWash.tools must hold (Verify.cs line "tools: n / ToolSpecs.Length" should compare with this).</summary>
        static int AllToolCount() { return ToolSpecs.Length + CourseStations.Sum(s => s.tools.Length) + UnityEngine.Object.FindObjectsOfType<KH_WashSelection>(true).Where(x => x.gameObject.scene.path == KH_CourseGroundEditor.ScenePath).Sum(x => x.slots != null ? x.slots.Length : 0); }

        /// <summary>The KH_WashTool of every station present in the scene, main first, in the builder's order (missing ones skipped).</summary>
        static List<KH_WashTool> AllStationTools(UnityEngine.SceneManagement.Scene scene)
        {
            var list = new List<KH_WashTool>();
            foreach (var st in AllStationTransforms(scene))
            {
                var specs = st.name == StationName && st.parent == null ? ToolSpecs : CourseStations.Where(s => s.key == st.name).Select(s => s.tools).FirstOrDefault();
                if (specs == null) continue;
                foreach (var spec in specs) { var t = st.Find(spec.name); var tool = t != null ? t.GetComponent<KH_WashTool>() : null; if (tool != null) list.Add(tool); }
                var premium = st.GetComponentInChildren<KH_WashSelection>(true);
                if (premium != null && premium.slots != null) list.AddRange(premium.slots.Where(t => t != null));
            }
            return list;
        }

        /// <summary>KH_WashStation, then KH_WashStations/{key} in the table's order (the ones present).</summary>
        static List<Transform> AllStationTransforms(UnityEngine.SceneManagement.Scene scene)
        {
            var list = new List<Transform>();
            var main = scene.GetRootGameObjects().FirstOrDefault(g => g.name == StationName);
            if (main != null) list.Add(main.transform);
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == CourseRootName);
            if (root != null) foreach (var s in CourseStations) { var t = root.transform.Find(s.key); if (t != null) list.Add(t); }
            return list;
        }

        static List<KH_WashTool> BuildCourseStations(UnityEngine.SceneManagement.Scene scene, List<Transform> stations, Material wood, Shared shared, Audio audio, BuildReport r)
        {
            var tools = new List<KH_WashTool>();
            var roots = scene.GetRootGameObjects().Where(g => g.name == CourseRootName).ToArray();
            if (roots.Length > 1) throw new InvalidOperationException(roots.Length + " " + CourseRootName + " at the scene root (make it one, then run again)");
            if (KominkaDogRun.Editor.KD_SceneObjects.Find(CourseRootName, out int anywhere) != null && anywhere != roots.Length)
                throw new InvalidOperationException(CourseRootName + " exists below another object (move it to the scene root, then run again)");
            Transform root;
            if (roots.Length == 1) root = roots[0].transform;
            else
            {
                root = new GameObject(CourseRootName).transform;
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                r.log.Add("created " + CourseRootName + " at the scene root (identity)");
            }
            root.localScale = Vector3.one;
            foreach (Transform t in root)
                if (!CourseStations.Any(s => s.key == t.name)) r.log.Add("WARN " + CourseRootName + "/" + t.name + " is not in the station table: left as is (Wash/12 fails on it)");

            string missing;
            var cc = ResolveCourse(scene, out missing);
            // Spacing uses every station already in the scene (the main one, every KH_WashStations child, the ones later in the
            // table too), plus each one created below; `stations` stays the wiring order (Codex review: a re-created River must
            // not ignore the existing Upper / Hillside).
            var spacing = new List<Transform>(stations);
            foreach (Transform t in root) if (!spacing.Contains(t)) spacing.Add(t);
            foreach (var spec in CourseStations)
            {
                var st = root.Find(spec.key);
                if (st != null) r.log.Add("kept " + CourseRootName + "/" + spec.key + " at " + st.position.ToString("F2") + " yaw " + st.eulerAngles.y.ToString("F1") + " (human-owned)");
                else
                {
                    if (cc == null) { r.log.Add("WARN " + spec.key + " (" + spec.label + ") not placed: " + missing); continue; }
                    var tried = new List<string>();
                    foreach (var cand in CourseCandidates(cc, spec))
                    {
                        Vector3 pos; Quaternion rot;
                        try { CoursePose(cc, cand.Value, out pos, out rot); }
                        catch (Exception e) { tried.Add(cand.Key + ": " + e.Message); continue; }
                        var fails = PlacementChecks(cc, spec, pos, rot, null, spacing).Where(x => !x.Key).Select(x => x.Value).ToList();
                        if (fails.Count > 0) { tried.Add(cand.Key + ": " + string.Join("; ", fails)); continue; }
                        st = new GameObject(spec.key).transform;
                        st.SetParent(root, false);
                        st.SetPositionAndRotation(pos, rot);
                        spacing.Add(st);
                        r.log.Add("created " + CourseRootName + "/" + spec.key + " (" + spec.label + ") at " + pos.ToString("F2") + " yaw " + rot.eulerAngles.y.ToString("F1") + ", candidate " + cand.Key
                            + (tried.Count > 0 ? " (rejected before it: " + string.Join(" | ", tried) + ")" : ""));
                        break;
                    }
                    if (st == null) { r.log.Add("WARN " + spec.key + " (" + spec.label + ") not placed, no candidate passed: " + string.Join(" | ", tried)); continue; }
                }
                st.localScale = Vector3.one;
                stations.Add(st);
                tools.AddRange(BuildStationParts(st, spec.tools, wood, shared, audio, r));
            }
            return tools;
        }

        /// <summary>KH_DogWash.washStations = every station (main first) on every KH_DogWash in the scene (the main Build copies the
        /// built dogs to Udon afterwards; Wash/20 keeps washStation = the main one).</summary>
        static void WireStationList(List<Transform> stations, BuildReport r)
        {
            var all = stations.ToArray();
            int n = 0;
            foreach (var w in UnityEngine.Object.FindObjectsOfType<KH_DogWash>(true))
            {
                w.washStations = (Transform[])all.Clone();
                EditorUtility.SetDirty(w);
                n++;
            }
            r.log.Add("washStations (" + all.Length + ": " + string.Join(", ", all.Select(t => t.name)) + ") written on " + n + " KH_DogWash");
        }

        // ---------------------------------------------------------------- the course context (read only; never throws)

        class HandSpot { public string name; public Vector3 pos; public int kind; public HandSpot(string n, Vector3 p, int k) { name = n; pos = p; kind = k; } }
        const int HandOther = 0, HandWarp = 1, HandBase = 2;

        class CourseCtx
        {
            public Vector3[] path; public bool closed;
            public KH_RiverData river; public KH_WaterSpot spot;
            public bool hasRiverNode, hasUpperNode, hasPlazaNode;
            public Vector3 riverNode, upperNode, plazaNode, entry;
            public Transform bench, kominka;
            public List<HandSpot> hands = new List<HandSpot>();
            public List<Transform> dirtZones = new List<Transform>();
            public string notes = "";
        }

        static CourseCtx ResolveCourse(UnityEngine.SceneManagement.Scene scene, out string missing)
        {
            missing = "";
            var routes = UnityEngine.Object.FindObjectsOfType<KH_WalkRoute>(true);
            if (routes.Length != 1 || routes[0].nodePositions == null || routes[0].nodePositions.Length < 2) { missing = "KH_WalkRoute with nodePositions not found once (" + routes.Length + ")"; return null; }
            var route = routes[0];
            var c = new CourseCtx { path = route.nodePositions, closed = route.closedLoop };
            c.hasRiverNode = NodeOf(route, 5, out c.riverNode);
            c.hasUpperNode = NodeOf(route, 4, out c.upperNode);
            c.hasPlazaNode = NodeOf(route, 1, out c.plazaNode);
            c.river = UnityEngine.Object.FindObjectsOfType<KH_RiverData>(true).FirstOrDefault(x => x != null && x._Ready());
            c.spot = UnityEngine.Object.FindObjectsOfType<KH_WaterSpot>(true).FirstOrDefault();
            if (c.spot != null && c.spot.entry != Vector3.zero)
            {
                c.entry = c.spot.entry;
                c.hands.Add(new HandSpot("water-play entry", c.entry, HandOther));
                var marker = c.spot.transform.Find("Marker");
                if (marker != null) foreach (Transform m in marker) c.hands.Add(new HandSpot("water-play " + m.name, m.position, HandOther));
            }
            else c.notes += "no KH_WaterSpot entry; ";
            var managers = UnityEngine.Object.FindObjectsOfType<KH_LeashManager>(true);
            if (managers.Length == 1 && managers[0].spotPositions != null)
            {
                var m = managers[0];
                for (int i = 0; i < m.spotPositions.Length; i++)
                    if (m.spotWarpEnabled != null && i < m.spotWarpEnabled.Length && m.spotWarpEnabled[i])
                        c.hands.Add(new HandSpot("warp " + (m.spotIds != null && i < m.spotIds.Length ? m.spotIds[i] : i.ToString()), m.spotPositions[i], HandWarp));
            }
            else c.notes += "no KH_LeashManager travel spots; ";
            var dogs = scene.GetRootGameObjects().FirstOrDefault(g => g.name == KominkaDogRun.Editor.KD_Hillside.DogsRootName);
            if (dogs != null)
            {
                foreach (var n in new[] { RackName, "KH_Station_Viewpoint", KH_DogWalkingEditor.PlazaStation })
                { var t = dogs.transform.Find(n); if (t != null) c.hands.Add(new HandSpot("leash base " + n, t.position, HandBase)); }
                var stops = dogs.transform.Find(KH_WalkStopoverEditor.RootName);
                if (stops != null) foreach (Transform s in stops) c.hands.Add(new HandSpot("stopover " + s.name, s.position, HandOther));
            }
            var world = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "KH_World");
            if (world != null) { c.bench = world.transform.Find("KH_Places/KH_Viewpoint/KH_Bench"); c.kominka = world.transform.Find("KH_Places/KH_FarKominka"); }
            if (c.bench != null) c.hands.Add(new HandSpot("bench KH_Bench", c.bench.position, HandOther));
            var zones = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "KH_DirtZones");
            if (zones != null) foreach (Transform z in zones.transform) c.dirtZones.Add(z);
            else c.notes += "no KH_DirtZones yet (Wash/20; run Wash/12 again after it); ";
            return c;
        }

        static bool NodeOf(KH_WalkRoute route, int destination, out Vector3 p)
        {
            p = Vector3.zero;
            int node = route._DestinationNode(destination);
            if (node < 0 || node >= route.nodePositions.Length) return false;
            p = route.nodePositions[node];
            return true;
        }

        // ---------------------------------------------------------------- candidates

        /// <summary>World XZ points to try in order (y is filled by CoursePose). (along, away) are metres in the frame of an anchor:
        /// along = the path's direction there, away = off the path (River: away from the river; Upper: away from the upper dirt
        /// zone; Hillside: from the path toward the bench).</summary>
        static List<KeyValuePair<string, Vector3>> CourseCandidates(CourseCtx c, StationSpec spec)
        {
            var list = new List<KeyValuePair<string, Vector3>>();
            Action<string, Vector3, Vector3, Vector3, float[,]> add = (tag, anchor, along, away, offs) =>
            {
                for (int i = 0; i < offs.GetLength(0); i++)
                    list.Add(new KeyValuePair<string, Vector3>(tag + " (" + offs[i, 0] + ", " + offs[i, 1] + ")", anchor + along * offs[i, 0] + away * offs[i, 1]));
            };
            Vector3 t, q; float d;
            switch (spec.place)
            {
                case PlaceRiver:
                    if (!c.hasRiverNode) break;
                    q = NearestOnPath(c, c.riverNode, out t, out d);
                    {
                        float side = c.river != null && c.river._Covers(c.riverNode.z) ? Mathf.Sign(c.riverNode.x - c.river._CenterX(c.riverNode.z)) : 1f;
                        if (side == 0f) side = 1f;
                        Vector3 away = new Vector3(-t.z, 0f, t.x);
                        if (away.x * side < 0f) away = -away;   // the river runs along z (its centre is an x per z): away = the side without the river
                        add("川辺 node", c.riverNode, t, away, new float[,] { { 0f, 6f }, { 4f, 5f }, { -4f, 5f }, { 0f, 7.5f }, { 6f, 4f }, { -6f, 4f } });
                    }
                    break;
                case PlaceUpper:
                    if (!c.hasUpperNode) break;
                    q = NearestOnPath(c, c.upperNode, out t, out d);
                    {
                        Vector3 away = new Vector3(-t.z, 0f, t.x);
                        var zone = c.dirtZones.FirstOrDefault(z => z.name == "KH_DirtZone_Upper");
                        if (zone != null && Vector3.Dot(zone.position - c.upperNode, away) > 0f) away = -away;   // the side opposite the dirt zone
                        add("奥広場 node", c.upperNode, t, away, new float[,] { { 0f, 11f }, { 8f, 9f }, { -8f, 9f }, { 15f, 3f }, { -15f, 3f }, { 0f, 13f } });
                    }
                    break;
                case PlaceHillside:
                    if (c.bench != null)
                    {
                        q = NearestOnPath(c, c.bench.position, out t, out d);
                        Vector3 away = Flat3(c.bench.position - q);
                        if (away.sqrMagnitude < 1e-4f) away = new Vector3(-t.z, 0f, t.x);
                        add("bench", c.bench.position, t, away.normalized, new float[,] { { 5f, 0f }, { -5f, 0f }, { 6f, 1.5f }, { -6f, 1.5f } });
                    }
                    if (c.hasPlazaNode)
                    {
                        // fallback: the edge of the existing plaza (flat), both sides of the path
                        q = NearestOnPath(c, c.plazaNode, out t, out d);
                        add("広場 node", c.plazaNode, t, new Vector3(-t.z, 0f, t.x), new float[,] { { 0f, 8f }, { 0f, -8f }, { 6f, 8f }, { -6f, -8f } });
                    }
                    break;
            }
            return list;
        }

        /// <summary>The station pose at a world XZ: y = the terrain there, yaw so that the front (-Z, the duckboard) faces the nearest
        /// point of the path (people come from the path). Throws when there is no terrain.</summary>
        static void CoursePose(CourseCtx c, Vector3 xz, out Vector3 pos, out Quaternion rot)
        {
            pos = new Vector3(xz.x, Ground(xz.x, xz.z, xz.y), xz.z);
            Vector3 t; float d;
            var q = NearestOnPath(c, pos, out t, out d);
            var back = Flat3(pos - q);
            rot = back.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(back.normalized, Vector3.up) : Quaternion.identity;
        }

        static Vector3 Flat3(Vector3 v) { v.y = 0f; return v; }
        static float FlatDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return (a - b).magnitude; }

        /// <summary>The nearest point of the loop's centre line (flat), the path's direction there (flat, unit), and the distance.</summary>
        static Vector3 NearestOnPath(CourseCtx c, Vector3 p, out Vector3 tangent, out float dist)
        {
            int n = c.path.Length, segs = c.closed ? n : n - 1;
            dist = float.PositiveInfinity; tangent = Vector3.forward; Vector3 best = c.path[0];
            for (int i = 0; i < segs; i++)
            {
                Vector3 a = c.path[i], b = c.path[(i + 1) % n];
                Vector3 ab = Flat3(b - a); float len2 = ab.sqrMagnitude;
                float u = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(Flat3(p - a), ab) / len2) : 0f;
                Vector3 x = a + (b - a) * u;
                float dd = FlatDist(p, x);
                if (dd < dist) { dist = dd; best = x; if (len2 > 1e-8f) tangent = ab.normalized; }
            }
            return best;
        }

        // ---------------------------------------------------------------- placement checks (Build picks with them, Wash/12 repeats them)

        /// <summary>Every placement rule at a pose (true = passes). self = the station being checked (its own colliders are not
        /// obstacles; null while choosing). stations = the stations already there (the main one first); self is skipped.</summary>
        static List<KeyValuePair<bool, string>> PlacementChecks(CourseCtx c, StationSpec spec, Vector3 pos, Quaternion rot, Transform self, List<Transform> stations)
        {
            var o = new List<KeyValuePair<bool, string>>();
            Action<bool, string> add = (ok, what) => o.Add(new KeyValuePair<bool, string>(ok, what));
            var prints = StationFootprints();

            float plane, steep, hmin, hmax;
            try { SlopeMeasure(pos, rot, out plane, out steep, out hmin, out hmax); }
            catch (Exception e) { add(false, "ground: " + e.Message); return o; }
            add(plane < CourseSlopeMax, "slope " + plane.ToString("F1") + " deg < " + CourseSlopeMax + " (plane over the working area; steepest neighbour step " + steep.ToString("F1") + " deg)");

            float route = float.PositiveInfinity; string near = "";
            {
                int n = c.path.Length, segs = c.closed ? n : n - 1;
                for (int i = 0; i < segs; i++)
                {
                    Vector3 a = c.path[i], b = c.path[(i + 1) % n];
                    if (FlatDist(a, pos) > 25f && FlatDist(b, pos) > 25f) continue;
                    string nm; float dd = SegmentFootDist(pos, rot, prints, a, b, out nm);
                    if (dd < route) { route = dd; near = nm + ", segment " + i; }
                }
            }
            add(route >= CourseRouteClear, "the loop's centre line " + route.ToString("F2") + " m from the " + near + " (>= " + CourseRouteClear + ")");

            if (c.river != null)
                add(!c.river._InBand(pos, CourseRiverBand), "out of the river band + " + CourseRiverBand + " m (lateral " + c.river._Lateral(pos, 0f).ToString("F2") + " m from the band edge)");
            if (spec.place == PlaceRiver)
            {
                float dn = c.hasRiverNode ? FlatDist(pos, c.riverNode) : float.NaN;
                add(dn >= RiverAnchorMin && dn <= RiverAnchorMax, "the 川辺 destination node " + dn.ToString("F2") + " m away (" + RiverAnchorMin + ".." + RiverAnchorMax + ")");
                if (c.hasRiverNode && c.entry != Vector3.zero)
                {
                    string nm; float dd = SegmentFootDist(pos, rot, prints, c.riverNode, c.entry, out nm);
                    add(dd >= CourseCorridorClear, "the walk to the water (川辺 node -> entry) " + dd.ToString("F2") + " m from the " + nm + " (>= " + CourseCorridorClear + ")");
                }
            }
            if (spec.place == PlaceHillside && c.bench != null && c.kominka != null)
            {
                Vector3 dir = Flat3(c.kominka.position - c.bench.position);
                if (dir.sqrMagnitude > 1e-4f)
                {
                    string nm; float dd = SegmentFootDist(pos, rot, prints, c.bench.position, c.bench.position + dir.normalized * CourseViewLength, out nm);
                    add(dd >= CourseViewClear, "the bench's view toward KH_FarKominka (" + CourseViewLength + " m) " + dd.ToString("F2") + " m from the " + nm + " (>= " + CourseViewClear + ")");
                }
            }

            foreach (var z in c.dirtZones)
            {
                if (z == null) continue;
                float dz = FlatDist(pos, z.position);
                add(dz >= CourseDirtClear, "dirt zone " + z.name + " " + dz.ToString("F1") + " m from the origin (>= " + CourseDirtClear + ")");
            }
            foreach (var s in stations)
            {
                if (s == null || s == self) continue;
                float ds = FlatDist(pos, s.position);
                add(ds >= CourseStationSpacing, "wash station " + s.name + " " + ds.ToString("F1") + " m away (>= " + CourseStationSpacing + ")");
            }
            foreach (var h in c.hands)
            {
                if (FlatDist(h.pos, pos) > 40f) continue;   // only what is near matters
                float want = h.kind == HandWarp ? spec.warpClear : h.kind == HandBase ? spec.baseClear : (h.name == "water-play entry" ? CourseEntryClear : CourseHandClear);
                string nm; float dd = FootDist(pos, rot, prints, h.pos, out nm);
                add(dd >= want, h.name + " " + dd.ToString("F2") + " m from the " + nm + " (>= " + want + ")");
            }

            // obstacles over the working area (shelf, duckboard, tub and 1 m each side of the duckboard), terrain + 0.02 .. + 1.8 m
            {
                float halfX = TableSize.x * 0.5f + SideMargin, z0 = -TableFront - TableSize.z * 0.5f, z1 = ShelfDepth * 0.5f;
                float bottom = hmin + 0.02f, top = hmax + 1.8f;
                var centre = pos + rot * new Vector3(0f, 0f, (z0 + z1) * 0.5f); centre.y = (bottom + top) * 0.5f;
                var hits = Physics.OverlapBox(centre, new Vector3(halfX, (top - bottom) * 0.5f, (z1 - z0) * 0.5f), rot, ~0, QueryTriggerInteraction.Ignore)
                    .Where(x => !(x is TerrainCollider) && x.attachedRigidbody == null && HitsPlayers(x) && (self == null || !x.transform.IsChildOf(self)))
                    .Select(x => KominkaDogRun.Editor.KD_SceneObjects.PathOf(x.gameObject)).ToArray();
                add(hits.Length == 0, "no solid collider over the working area " + (2f * halfX).ToString("F1") + " x " + (z1 - z0).ToString("F2") + " m (the station's own parts aside: " + string.Join(", ", hits) + ")");
            }
            {
                var board = pos + rot * new Vector3(0f, 0f, -TableFront);
                UnityEngine.AI.NavMeshHit hit;
                bool nav = UnityEngine.AI.NavMesh.SamplePosition(board, out hit, CourseNavReach + 2f, UnityEngine.AI.NavMesh.AllAreas) && FlatDist(hit.position, board) <= CourseNavReach;
                add(nav, "the duckboard centre within " + CourseNavReach + " m of the NavMesh (the dogs can reach it" + (nav ? ", " + FlatDist(hit.position, board).ToString("F2") + " m" : "") + ")");
            }
            return o;
        }

        /// <summary>Footprint (station local rectangles) distance to a world point, for a pose (no Transform needed).</summary>
        static float FootDist(Vector3 pos, Quaternion rot, Footprint[] prints, Vector3 world, out string nearest)
        {
            var l = Quaternion.Inverse(rot) * (world - pos);
            float best = float.PositiveInfinity; nearest = "";
            foreach (var f in prints)
            {
                float dx = Mathf.Max(Mathf.Abs(l.x - f.centre.x) - f.size.x * 0.5f, 0f), dz = Mathf.Max(Mathf.Abs(l.z - f.centre.y) - f.size.y * 0.5f, 0f);
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d < best) { best = d; nearest = f.name; }
            }
            return best;
        }

        /// <summary>Footprint distance to a world segment (sampled every 0.1 m).</summary>
        static float SegmentFootDist(Vector3 pos, Quaternion rot, Footprint[] prints, Vector3 a, Vector3 b, out string nearest)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(FlatDist(a, b) / 0.1f));
            float best = float.PositiveInfinity; nearest = "";
            for (int i = 0; i <= steps; i++)
            {
                string nm; float d = FootDist(pos, rot, prints, Vector3.Lerp(a, b, i / (float)steps), out nm);
                if (d < best) { best = d; nearest = nm; }
            }
            return best;
        }

        // ================================================================ IMP-ENV-007 / 011: the station programs (10b)

        /// <summary>The station's own programs: the reset button (IMP-ENV-007), and the call / my floor / the board (IMP-ENV-011,
        /// IMP-DOG-035; KH_WashStationCall.cs is written by the dog side's owner).</summary>
        static readonly string[] StationPrograms = { ResetProgram, CallProgram, FloorProgram, BoardProgram };

        [MenuItem(Menu + "10b - Create Station Programs")]
        public static void CreateStationPrograms()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("[KH Wash Setup] Edit mode only (not compiling)");
            var made = new List<string>();
            foreach (var name in StationPrograms)
            {
                string asset = ScriptsDir + "/" + name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(asset) != null) continue;
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptsDir + "/" + name + ".cs");
                if (script == null) throw new InvalidOperationException(name + ".cs not imported yet; refresh first (created so far: " + string.Join(", ", made) + ")");
                var created = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                created.sourceCsScript = script;
                AssetDatabase.CreateAsset(created, asset);
                made.Add(name);
            }
            if (made.Count == 0) { Debug.Log("[KH Wash Setup] station program assets exist (" + string.Join(", ", StationPrograms) + "); nothing changed"); return; }
            AssetDatabase.SaveAssets();
            UdonSharpProgramAsset.CompileAllCsPrograms(true);
            Debug.Log("[KH Wash Setup] created " + string.Join(", ", made) + ". Compile U# (scripts/unity-compile.sh --udon), then run 11.");
        }

        static void RequireStationProgram(string name)
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ScriptsDir + "/" + name + ".asset") == null)
                throw new InvalidOperationException(name + ".asset missing: run Wash/10b, compile U#, then 11 again (reopen the scene without saving to drop this run)");
        }

        // ================================================================ IMP-ENV-011 / IMP-DOG-035: call, my floor, the board

        const string LiftName = "Lift", AnchorName = "Anchor", CallName = "Call", FloorName = "PersonalFloor", FloorBoxesName = "Floor", BoardName = "Board";
        const string CallProgram = "KH_WashStationCall", FloorProgram = "KH_WashPersonalFloor", BoardProgram = "KH_WashStationBoard";
        /// <summary>The called dog faces the station's +X: parallel to the shelf (docs/plans/2026-10-01-wash-call-and-height.md §2-3).
        /// The duckboard is 1.3 (X) x 0.7 (Z) for it (TableSize).</summary>
        const float StandYawOffset = 90f;
        // the contract's tuning values (§4)
        const float CallLiftMax = 0.9f, CallLiftStep = 0.1f, CallIdleReleaseSeconds = 120f, CallCallerFarMeters = 10f;
        // 2026-10-02 (user: other dogs keep away from a wash station, never rest on the stand): the keep-out circle around the anchor.
        // The 5 x 5 m personal floor's corners are 2.5 x sqrt 2 = 3.54 m from the duckboard's centre, the shelf 1.2 m behind it: 4 m
        // covers both with 0.46 m to spare and stays far below the 20 m station spacing (CourseStationSpacing).
        const float CallKeepOutRadius = 4f;
        // ... and where the released dog walks to (KD_DogWanderController, written by WireWanders, checked with washCalls)
        const float LeaveMargin = 1.0f;   // m beyond CallKeepOutRadius
        const int LeaveGait = 1;          // 0 walk, 1 the slowest Run (round 2: 「トコトコ」= brisk), 2 runSpeed
        const float UnleashGrace = 20f;
        const float RestMargin = 1.5f;    // m outside a circle's rim where a free dog does not start / keep a rest (round 7)   // s a dog unleashed inside a circle may stand there (round 2: time to pick up the sponge)
        // ---- my floor (KH_WashPersonalFloor): 5 x 5 m around the duckboard, a hole over the duckboard + the shelf ----
        /// <summary>User layer 22 (coordinator's decision 2026-10-01, Codex review 8): the players collide with it (its row in
        /// DynamicsManager is all on; ProjectSettings unchanged), the dogs' ground rays (0|11), the frisbee's masks, the spray (0|11)
        /// and the wash rays (2) do not see it, and the Build adds it to VRCSceneDescriptor.interactThruLayers (bit 0 = layer 22)
        /// so the interact ray passes through the floor (Water 4 blocked it).</summary>
        const int FloorLayer = 22, FirstUserLayer = 22;
        const int FloorPassBit = 1 << (FloorLayer - FirstUserLayer);
        const float FloorHalf = 2.5f, FloorThickness = 0.1f, FloorMaxHeight = 1.2f, FloorStep = 0.1f, FloorSpeed = 0.5f, FloorLeaveMeters = 8f;
        // the hole, in PersonalFloor local (origin = the duckboard centre on the station's ground plane): the duckboard (±0.65, ±0.35) and the
        // shelf / board (station z -0.225..0.245 = local 0.975..1.445) with a margin
        // No hole any more (2026-10-01): the players pass through the stand and the shelf (StandLayer), so the floor is one 5 x 5 m slab
        // (a hole made a raised player fall beside the stand). The interact ray passes it (layer 22 in interactThruLayers).
        static readonly Vector2 FloorHoleMin = Vector2.zero, FloorHoleMax = Vector2.zero;
        // ---- the board: a World Space Canvas standing at the back of the shelf top, above the sign, facing the front (-Z) ----
        // IMP-DOG-037 (plan §7): 0.42 m tall (was 0.36) for the two-line dog buttons, the range rule and the height line; the bottom
        // stays at 0.94 m (above the sign), the top is 1.36 m.
        static readonly Vector2 BoardPixels = new Vector2(600f, 420f);
        const float BoardScale = 0.001f;   // 1 px = 1 mm: 0.60 x 0.42 m
        static readonly Vector3 BoardLocal = new Vector3(0f, ShelfHeight + 0.14f + 0.21f, ShelfDepth * 0.5f + 0.012f);   // bottom 0.94 m, top 1.36 m
        // Rows (px from the board's centre; the board spans x -300..300, y -210..210)
        static readonly Vector2 StatusPos = new Vector2(0f, 180f), StatusSize = new Vector2(560f, 36f);
        static readonly Vector2 RulePos = new Vector2(0f, 150f), RuleSize = new Vector2(560f, 24f);
        static readonly Vector2 HeightsPos = new Vector2(0f, -178f), HeightsSize = new Vector2(560f, 30f);
        static readonly Vector2 DogButtonSize = new Vector2(182f, 70f);
        static readonly Vector2 DogNamePos = new Vector2(0f, 14f), DogNameSize = new Vector2(166f, 34f);
        static readonly Vector2 DogStatePos = new Vector2(0f, -18f), DogStateSize = new Vector2(170f, 26f);
        const float StatusPt = 22f, RulePt = 18f, HeightsPt = 18f, DogNamePt = 22f, DogStatePt = 17f, ButtonPt = 22f, MinPt = 14f;
        const string StatusName = "Status", RuleName = "Rule", HeightsName = "Heights", DogStateName = "State";
        static readonly Color BoardInkDim = new Color(0.78f, 0.75f, 0.70f), BoardInkState = new Color(0.92f, 0.92f, 0.88f), BoardInkRule = new Color(0.86f, 0.83f, 0.76f);

        /// <summary>The dog names: the hand menu's keys (KD_TreatPouch.toyNameKeys, dogIndex order: KD_DogDefs).</summary>
        static readonly string[] BoardDogNameKeys = { "ui.extra.010", "dog.pomeranian", "dog.labrador", "ui.extra.012", "ui.extra.008", "ui.extra.009" };
        /// <summary>The board's words in KH_WashStationBoard.W* order (UITranslations.json, 13 languages). The Japanese written
        /// into the board (and its labels in the saved scene) is the catalog's ja text, so the two never differ.</summary>
        static readonly string[] BoardWordKeys =
        {
            "wash.board.pick", "wash.board.rule", "wash.board.calling", "wash.board.here", "wash.board.nameReason", "wash.board.busy", "wash.board.heights",
            "wash.state.callable", "wash.state.coming", "wash.state.here", "wash.state.calling", "wash.state.leashedOther", "wash.state.otherStation",
            "wash.state.tooFar", "wash.state.fetch", "wash.state.frisbee", "wash.state.gathering", "wash.state.sleepCall", "wash.state.washedOther",
            "wash.button.end", "wash.button.standUp", "wash.button.standDown", "wash.button.meUp", "wash.button.meDown",
            // IMP-DOG-039 / IMP-UI-003 the picture guide above the board (W 24..37, docs/plans/2026-10-03-hand-wash.md §13.11; written by _ApplyGuide)
            "wash.guide.step1", "wash.guide.step1.sub", "wash.guide.step2", "wash.guide.step2.sub", "wash.guide.step2.flat", "wash.guide.step2.flat.sub",
            "wash.guide.step3", "wash.guide.step3.sub", "wash.guide.step3.flat", "wash.guide.step3.flat.sub", "wash.guide.step4", "wash.guide.step4.sub",
            "wash.guide.help", "wash.guide.help.flat",
        };

        /// <summary>IMP-DOG-037: each station's call range (m, horizontal from the anchor), by station index (main, then
        /// CourseStations: River, Upper, Hillside). Plan §7.2 has the reasons. Wash/12 checks keepOutRadius + 1 < range <
        /// the dog side's StationFar, no two stations' ranges overlap, and the River range covers the water-play entry.</summary>
        static readonly float[] CallRanges = { 25f, 28f, 25f, 25f };
        static float CallRangeOf(int index) { return index >= 0 && index < CallRanges.Length ? CallRanges[index] : CallRanges[0]; }

        class BoardButton
        {
            public string name, key, evt; public Vector2 pos, size; public int kind;   // kind 0 dog / 1 end / 2 stand / 3 me
            public BoardButton(string name, string key, string evt, Vector2 pos, Vector2 size, int kind) { this.name = name; this.key = key; this.evt = evt; this.pos = pos; this.size = size; this.kind = kind; }
        }
        static BoardButton[] BoardButtons()
        {
            var list = new List<BoardButton>();
            for (int i = 0; i < BoardDogNameKeys.Length; i++)
                list.Add(new BoardButton("Dog_" + i, BoardDogNameKeys[i], "_Dog" + i, new Vector2(-190f + 190f * (i % 3), i < 3 ? 98f : 20f), DogButtonSize, 0));
            // the fixed buttons in KH_WashStationBoard.buttonLabels order (WEnd, WStandUp, WStandDown, WMeUp, WMeDown)
            list.Add(new BoardButton("End", BoardWordKeys[KH_WashStationBoard.WEnd], "_End", new Vector2(0f, -118f), new Vector2(260f, 50f), 1));
            list.Add(new BoardButton("StandUp", BoardWordKeys[KH_WashStationBoard.WStandUp], "_LiftUp", new Vector2(-219f, -54f), new Vector2(136f, 50f), 2));
            list.Add(new BoardButton("StandDown", BoardWordKeys[KH_WashStationBoard.WStandDown], "_LiftDown", new Vector2(-73f, -54f), new Vector2(136f, 50f), 2));
            list.Add(new BoardButton("MeUp", BoardWordKeys[KH_WashStationBoard.WMeUp], "_MeUp", new Vector2(73f, -54f), new Vector2(136f, 50f), 3));
            list.Add(new BoardButton("MeDown", BoardWordKeys[KH_WashStationBoard.WMeDown], "_MeDown", new Vector2(219f, -54f), new Vector2(136f, 50f), 3));
            return list.ToArray();
        }

        /// <summary>The localization catalog (UITranslations.json) and a key's text in one language (the fallback language's
        /// text when that one is empty). Throws on an unknown key (the keys are added to the catalog with this change).</summary>
        static KD_LocalizationEditor.Catalog BoardCatalog() { return KD_LocalizationEditor.Load(); }
        static string BoardText(KD_LocalizationEditor.Catalog c, string code, string key) { return KD_LocalizationEditor.Text(c, code, key); }

        /// <summary>The duckboard (under Lift since IMP-ENV-011; directly under the station before it).</summary>
        static Transform FindTable(Transform st)
        {
            var t = st.Find(LiftName + "/" + TableName);
            return t != null ? t : st.Find(TableName);
        }

        /// <summary>0 = the main station, 1.. = CourseStations in the table's order (fixed, whichever are present).</summary>
        static int StationIndexOf(Transform st)
        {
            if (st.name == StationName && st.parent == null) return 0;
            int i = Array.FindIndex(CourseStations, s => s.key == st.name);
            return i >= 0 ? 1 + i : -1;
        }

        /// <summary>The scene's KD_DogWanderController / KH_DogWash by dogIndex (length 6, null where missing).</summary>
        static void DogsByIndex(out KD_DogWanderController[] wanders, out KH_DogWash[] washes)
        {
            int n = KD_DogDefs.All.Length;
            wanders = new KD_DogWanderController[n]; washes = new KH_DogWash[n];
            foreach (var w in UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true)) if (w.dogIndex >= 0 && w.dogIndex < n) wanders[w.dogIndex] = w;
            foreach (var w in UnityEngine.Object.FindObjectsOfType<KH_DogWash>(true)) if (w.dogIndex >= 0 && w.dogIndex < n) washes[w.dogIndex] = w;
        }

        /// <summary>The Call of every station, main first (what KD_DogWanderController.washCalls holds).</summary>
        static List<KH_WashStationCall> AllStationCalls(UnityEngine.SceneManagement.Scene scene)
        {
            var list = new List<KH_WashStationCall>();
            foreach (var st in AllStationTransforms(scene)) { var t = st.Find(CallName); var c = t != null ? t.GetComponent<KH_WashStationCall>() : null; if (c != null) list.Add(c); }
            return list;
        }

        static SerializedProperty Prop(SerializedObject so, string field)
        {
            var p = so.FindProperty(field);
            if (p == null) throw new InvalidOperationException(so.targetObject.GetType().Name + "." + field + " is not serialized (docs/plans/2026-10-01-wash-call-and-height.md §4; compile first)");
            return p;
        }

        static void SetRefs(SerializedObject so, string field, UnityEngine.Object[] values)
        {
            var p = Prop(so, field);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        /// <summary>Walkthrough (17): the players (9, 10) pass through it, everything else hits it (user's decision 2026-10-01: the
        /// stand and the shelf must not bump the players). Checked against the collision matrix at Build and in Wash/12.</summary>
        const int StandLayer = 17;

        /// <summary>Every solid collider of a station on StandLayer, except the tools (rigidbody: Pickup), the reset board (Interact
        /// target, Default), the board's UI shape (a trigger) and my floor (layer 22, the players stand on it).</summary>
        static void SetStandLayers(Transform st, BuildReport r)
        {
            if (!Physics.GetIgnoreLayerCollision(StandLayer, 10) || !Physics.GetIgnoreLayerCollision(StandLayer, 9))
                throw new InvalidOperationException("layer " + StandLayer + " collides with the players in this project's collision matrix (expected Walkthrough)");
            var pf = st.Find(FloorName);
            int n = 0;
            foreach (var c in st.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || c.attachedRigidbody != null || c.transform.name == ResetName || (pf != null && c.transform.IsChildOf(pf))) continue;
                if (c.gameObject.layer != StandLayer) { c.gameObject.layer = StandLayer; n++; }
            }
            if (n > 0) r.log.Add(st.name + ": " + n + " solid colliders on layer " + StandLayer + " (players pass through)");
        }

        static bool HitsPlayers(Collider c) { return !Physics.GetIgnoreLayerCollision(c.gameObject.layer, 10); }

        /// <summary>The interact ray passes through my floor: the floor layer's bit in VRCSceneDescriptor.interactThruLayers (the other bits kept).</summary>
        static void SetFloorPassthrough(BuildReport r)
        {
            var descs = UnityEngine.Object.FindObjectsOfType<VRCSceneDescriptor>(true);
            if (descs.Length != 1) throw new InvalidOperationException(descs.Length + " VRCSceneDescriptor in the scene (expected 1)");
            var so = new SerializedObject(descs[0]);
            var p = Prop(so, "interactThruLayers");
            if ((p.intValue & FloorPassBit) != 0) return;
            int before = p.intValue;
            p.intValue = before | FloorPassBit;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(descs[0]);
            r.log.Add("VRCSceneDescriptor.interactThruLayers " + before + " -> " + p.intValue + " (layer " + FloorLayer + ": my floor lets the interact ray through)");
        }

        static void BuildStationControls(Transform st, int index, KD_LocalizationEditor.Catalog catalog, KD_Language language, Texture2D guideTex, BuildReport r)
        {
            RequireStationProgram(CallProgram); RequireStationProgram(FloorProgram); RequireStationProgram(BoardProgram);
            if (index < 0) throw new InvalidOperationException(st.name + " has no station index");
            var lift = st.Find(LiftName);
            var anchor = lift != null ? lift.Find(AnchorName) : null;
            var table = FindTable(st);
            if (lift == null || anchor == null || table == null || table.parent != lift) throw new InvalidOperationException(st.name + ": " + LiftName + "/" + TableName + " and " + LiftName + "/" + AnchorName + " missing (BuildStand runs first)");
            KD_DogWanderController[] wanders; KH_DogWash[] washes;
            DogsByIndex(out wanders, out washes);

            // ---- Call (KH_WashStationCall, Manual): the contract's wiring (§4) through SerializedObject (the script is the dog side's) ----
            var callT = Child(st, CallName);
            callT.gameObject.layer = 0;
            callT.localPosition = Vector3.zero; callT.localRotation = Quaternion.identity; callT.localScale = Vector3.one;
            var call = AddUdon<KH_WashStationCall>(callT.gameObject, Networking.SyncType.Manual);
            var so = new SerializedObject(call);
            Prop(so, "stationIndex").intValue = index;
            Prop(so, "anchor").objectReferenceValue = anchor;
            Prop(so, "liftRoot").objectReferenceValue = lift;
            Prop(so, "standYaw").floatValue = Mathf.Repeat(st.eulerAngles.y + StandYawOffset, 360f);
            SetRefs(so, "dogs", wanders);
            SetRefs(so, "washes", washes);
            Prop(so, "liftMax").floatValue = CallLiftMax;
            Prop(so, "liftStep").floatValue = CallLiftStep;
            Prop(so, "idleReleaseSeconds").floatValue = CallIdleReleaseSeconds;
            Prop(so, "callerFarMeters").floatValue = CallCallerFarMeters;
            Prop(so, "keepOutRadius").floatValue = CallKeepOutRadius;
            Prop(so, "callRange").floatValue = CallRangeOf(index);   // IMP-DOG-037
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(call);

            // ---- my floor (KH_WashPersonalFloor, None): 4 boxes around a hole, disabled until raised ----
            var pfT = Child(st, FloorName);
            pfT.gameObject.layer = 0;
            pfT.localPosition = new Vector3(0f, 0f, -TableFront); pfT.localRotation = Quaternion.identity; pfT.localScale = Vector3.one;
            float baseY = table.localPosition.y;   // the highest ground under the duckboard (Lift is at the station's origin)
            var boxesT = Child(pfT, FloorBoxesName);
            boxesT.gameObject.layer = FloorLayer;
            boxesT.localPosition = new Vector3(0f, baseY, 0f); boxesT.localRotation = Quaternion.identity; boxesT.localScale = Vector3.one;
            var rects = new[]
            {
                new KeyValuePair<string, Rect>("All", Rect.MinMaxRect(-FloorHalf, -FloorHalf, FloorHalf, FloorHalf)),
            };
            var cols = new List<Collider>();
            foreach (var kv in rects)
            {
                var bt = Child(boxesT, "Floor_" + kv.Key);
                bt.gameObject.layer = FloorLayer;
                bt.localPosition = new Vector3(kv.Value.center.x, 0f, kv.Value.center.y); bt.localRotation = Quaternion.identity; bt.localScale = Vector3.one;
                foreach (var c in bt.GetComponents<Component>()) if (!(c is Transform) && !(c is BoxCollider)) UnityEngine.Object.DestroyImmediate(c);   // invisible: no renderer
                var bc = bt.GetComponent<BoxCollider>(); if (bc == null) bc = bt.gameObject.AddComponent<BoxCollider>();
                bc.isTrigger = false; bc.size = new Vector3(kv.Value.width, FloorThickness, kv.Value.height); bc.center = new Vector3(0f, -FloorThickness * 0.5f, 0f);   // the top at the floor's y
                bc.enabled = false;   // KH_WashPersonalFloor turns them on above 0 (the saved scene has no floor)
                EditorUtility.SetDirty(bc);
                cols.Add(bc);
            }
            foreach (Transform t in boxesT.Cast<Transform>().ToArray())
                if (!rects.Any(kv => "Floor_" + kv.Key == t.name)) { r.log.Add("removed stale " + st.name + "/" + FloorName + "/" + t.name); UnityEngine.Object.DestroyImmediate(t.gameObject); }
            var pf = AddUdon<KH_WashPersonalFloor>(pfT.gameObject, Networking.SyncType.None);
            pf.floor = boxesT; pf.floorColliders = cols.ToArray(); pf.baseY = baseY;
            pf.outerHalf = new Vector2(FloorHalf, FloorHalf); pf.holeMin = FloorHoleMin; pf.holeMax = FloorHoleMax;
            pf.maxHeight = FloorMaxHeight; pf.step = FloorStep; pf.speed = FloorSpeed; pf.leaveMeters = FloorLeaveMeters;
            EditorUtility.SetDirty(pf);

            // ---- the board ----
            var board = BuildBoard(st, call, pf, catalog, language, r);
            // ---- IMP-DOG-039: the picture guide above the board (a sibling Canvas; wired into the board before the copy) ----
            BuildGuide(st, board, guideTex, r);

            Copy(call); Copy(pf); Copy(board);
            r.log.Add(st.name + ": Call (index " + index + ", standYaw " + Mathf.Repeat(st.eulerAngles.y + StandYawOffset, 360f).ToString("F1") + ", callRange " + CallRangeOf(index) + " m), " + FloorName + " (base " + baseY.ToString("F3") + "), " + BoardName + " wired");
        }

        // ================================================================ IMP-DOG-039: KH_HandWash (bare-hand lather, design §3)

        const string HandWashName = "HandWash";
        /// <summary>KH_HandWash's tuning (design §8.1). Wash/11 writes it, Wash/12 compares proxy and backing.</summary>
        static readonly (string field, float value)[] HandWashTuning =
        {
            ("handContactRadius", 0.07f),
            ("sampleInterval", 0.0333f),
            ("nearMeters", 2.5f),
            ("idleInterval", 0.5f),
            ("hapticInterval", 0.1f),
            ("hapticDuration", 0.03f),
            ("hapticAmplitude", 0.15f),
            ("hapticFrequency", 120f),
        };

        /// <summary>One KH_HandWash (sync None) at KH_WashStation/HandWash: calls = every station's Call (main first), washes = the
        /// six KH_DogWash by dogIndex, washLayer = layer 2, the tuning table, and the harness inputs / read-outs at their off values.
        /// Not copied here (the main Build copies it with the dogs).</summary>
        static KH_HandWash BuildHandWash(UnityEngine.SceneManagement.Scene scene, KH_WashStationCall[] calls, BuildReport r)
        {
            var main = scene.GetRootGameObjects().FirstOrDefault(g => g.name == StationName);
            if (main == null) throw new InvalidOperationException(StationName + " missing at the scene root (BuildStation runs first)");
            var t = Child(main.transform, HandWashName);
            t.gameObject.layer = 0;
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            foreach (var comp in t.GetComponents<Component>())
                if (!(comp is Transform) && !(comp is KH_HandWash) && !(comp is VRC.Udon.UdonBehaviour)) UnityEngine.Object.DestroyImmediate(comp);
            var hand = AddUdon<KH_HandWash>(t.gameObject, Networking.SyncType.None);
            KD_DogWanderController[] wanders; KH_DogWash[] washes;
            DogsByIndex(out wanders, out washes);
            if (washes.Any(w => w == null)) r.log.Add("WARN " + HandWashName + ".washes has a missing dog (index " + string.Join(", ", Enumerable.Range(0, washes.Length).Where(i => washes[i] == null)) + "); Wash/12 fails on it");
            hand.calls = (KH_WashStationCall[])calls.Clone();
            hand.washes = washes;
            hand.washLayer = 1 << WashLayer;
            // harness only: off (Course/100g turns them on in the heap, never in the saved scene)
            hand.debugHands = 0; hand.debugLeft = Vector3.zero; hand.debugRight = Vector3.zero;
            hand.debugLeftFree = false; hand.debugRightFree = false; hand.debugVr = false;
            hand.frameLoop = false; hand.contactCalls = 0;
            EditorUtility.SetDirty(hand);
            WriteFloats(hand, HandWashTuning);
            var others = UnityEngine.Object.FindObjectsOfType<KH_HandWash>(true).Where(x => x != hand).Select(x => KominkaDogRun.Editor.KD_SceneObjects.PathOf(x.gameObject)).ToArray();
            if (others.Length > 0) r.log.Add("WARN other KH_HandWash in the scene (left as is; Wash/12 expects one): " + string.Join(", ", others));
            r.handWash = StationName + "/" + HandWashName + " calls " + calls.Length + ", washes " + washes.Count(w => w != null) + " / " + washes.Length + ", washLayer " + (1 << WashLayer);
            r.log.Add(StationName + "/" + HandWashName + ": KH_HandWash calls " + calls.Length + " (" + string.Join(", ", calls.Select(c => c.transform.parent != null ? c.transform.parent.name : "?")) + "), washes " + washes.Length + " by dogIndex, layer " + WashLayer);
            return hand;
        }

        /// <summary>IMP-DOG-037: the KD_Language the boards read their words from (KD_TreatPouch.language, as the guide boards).
        /// Missing: a WARN and null (the boards keep the builder's Japanese; Wash/12 fails on it).</summary>
        static KD_Language BoardLanguage(BuildReport r)
        {
            var pouches = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true);
            if (pouches.Length == 1 && pouches[0].language != null) return pouches[0].language;
            r.log.Add("WARN the boards' KD_Language not found (KD_TreatPouch x" + pouches.Length + (pouches.Length == 1 ? ", language not wired (Menu/11)" : "") + "): the boards stay Japanese; Wash/12 fails on it");
            return null;
        }

        /// <summary>IMP-DOG-037: the board's ja / en glyphs into the shared font (its only entry, AddGlyphs), then KD_Language's
        /// tables from the catalog (Configure, as the other builders that add keys do; the label bindings are not touched) and
        /// the proxy copied to the backing.</summary>
        static void ApplyBoardLanguage(KD_LocalizationEditor.Catalog catalog, KD_Language language, BuildReport r)
        {
            var log = new System.Text.StringBuilder();
            KD_ShopSetupEditor.AddGlyphs(BoardGlyphText(catalog), log);
            if (language != null)
            {
                KD_LocalizationEditor.Configure(language, catalog, log);
                EditorUtility.SetDirty(language);
                UdonSharpEditorUtility.CopyProxyToUdon(language);
            }
            foreach (var line in log.ToString().Split('\n')) if (line.Trim().Length > 0) r.log.Add("board language: " + line.Trim());
        }

        /// <summary>Every character the boards draw in a language whose glyphs are baked (ja, en): the words, the dog names, the
        /// numbers the board writes (placeholders removed).</summary>
        static string BoardGlyphText(KD_LocalizationEditor.Catalog catalog)
        {
            var s = new System.Text.StringBuilder("0123456789.m -" + GuideArrowText);   // + the guide's arrow (IMP-DOG-039)
            foreach (var lang in catalog.languages)
            {
                if (!KD_LocalizationEditor.Bundled(lang)) continue;
                foreach (var k in BoardWordKeys.Concat(BoardDogNameKeys)) s.Append(System.Text.RegularExpressions.Regex.Replace(BoardText(catalog, lang.code, k), @"\{\d+\}", ""));
            }
            return s.ToString();
        }

        /// <summary>A text on the board: one line, autosize from pt down to MinPt (14, the hand menu's floor for Quest / phones).</summary>
        static TextMeshProUGUI EnsureBoardLabel(Transform parent, string name, Vector2 pos, Vector2 size, TMP_FontAsset font, float pt, Color ink, string text)
        {
            var g = KominkaDogRun.Editor.KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var t = g.GetComponent<TextMeshProUGUI>(); if (t == null) t = g.AddComponent<TextMeshProUGUI>();
            t.font = font; t.fontSize = pt; t.enableAutoSizing = true; t.fontSizeMax = pt; t.fontSizeMin = MinPt;
            t.alignment = TextAlignmentOptions.Center; t.color = ink;
            t.raycastTarget = false; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow;
            t.text = text;
            EditorUtility.SetDirty(t);
            return t;
        }

        static KH_WashStationBoard BuildBoard(Transform st, KH_WashStationCall call, KH_WashPersonalFloor pf, KD_LocalizationEditor.Catalog catalog, KD_Language language, BuildReport r)
        {
            string ja = catalog.fallbackLanguage;
            var words = BoardWordKeys.Select(k => BoardText(catalog, ja, k)).ToArray();
            var names = BoardDogNameKeys.Select(k => BoardText(catalog, ja, k)).ToArray();
            int index = StationIndexOf(st);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KominkaDogRun.Editor.KD_ShopSetupEditor.SharedFontPath);
            if (font == null) throw new InvalidOperationException("Shared UI font missing: " + KominkaDogRun.Editor.KD_ShopSetupEditor.SharedFontPath);
            var t = st.Find(BoardName);
            GameObject go;
            if (t == null) { go = new GameObject(BoardName, typeof(RectTransform)); go.transform.SetParent(st, false); }
            else go = t.gameObject;
            go.layer = 0;   // VRChat's pointer does not work on the "UI" layer (vrc-world-ui §2)
            var canvas = go.GetComponent<Canvas>(); if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (go.GetComponent<UnityEngine.UI.CanvasScaler>() == null) go.AddComponent<UnityEngine.UI.CanvasScaler>();
            if (go.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null) go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            if (go.GetComponent<VRCUiShape>() == null) go.AddComponent<VRCUiShape>();
            var box = go.GetComponent<BoxCollider>(); if (box == null) box = go.AddComponent<BoxCollider>();
            box.isTrigger = true; box.center = Vector3.zero; box.size = new Vector3(BoardPixels.x, BoardPixels.y, 2f);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = BoardPixels;
            rect.localScale = Vector3.one * BoardScale;
            rect.localPosition = BoardLocal;
            rect.localRotation = Quaternion.identity;   // the Canvas's front is -Z = the station's front

            var bg = KominkaDogRun.Editor.KD_ShopSetupEditor.EnsureUiChild(go.transform, "Bg", Vector2.zero, BoardPixels);
            bg.transform.SetAsFirstSibling();
            var bgImg = bg.GetComponent<UnityEngine.UI.Image>(); if (bgImg == null) bgImg = bg.AddComponent<UnityEngine.UI.Image>();
            bgImg.color = KominkaDogRun.Editor.KD_UiSpriteEditor.SurfacePanel;
            bgImg.raycastTarget = false;

            // ---- the three lines: what is happening (status), the range rule, the heights (the saved scene shows the Japanese) ----
            var status = EnsureBoardLabel(go.transform, StatusName, StatusPos, StatusSize, font, StatusPt, Color.white, words[KH_WashStationBoard.WPick]);
            var rule = EnsureBoardLabel(go.transform, RuleName, RulePos, RuleSize, font, RulePt, BoardInkRule, words[KH_WashStationBoard.WRule].Replace("{0}", Mathf.RoundToInt(CallRangeOf(index)).ToString()));
            var heights = EnsureBoardLabel(go.transform, HeightsName, HeightsPos, HeightsSize, font, HeightsPt, BoardInkRule, words[KH_WashStationBoard.WHeights].Replace("{0}", "0.0").Replace("{1}", "0.0"));

            var b = AddUdon<KH_WashStationBoard>(go, Networking.SyncType.None);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            // IMP-DOG-037: the fields the board no longer has can stay in the backing's variable table after the script reload
            // (as KD_PhotoSetupEditor.RemoveLegacyGalleryReferences does for the gallery's)
            bool removedOld = false;
            foreach (var old in new[] { "wordPick", "wordCalling", "wordHere", "wordBusy", "wordLeashed", "wordStand", "wordMe" })
                if (backing.publicVariables.RemoveVariable(old)) removedOld = true;
            if (removedOld) { EditorUtility.SetDirty(backing); r.log.Add(st.name + "/" + BoardName + ": removed the old word variables from the backing"); }
            var log = new System.Text.StringBuilder();
            var specs = BoardButtons();
            var dogImages = new UnityEngine.UI.Image[BoardDogNameKeys.Length];
            var dogNameLabels = new TextMeshProUGUI[BoardDogNameKeys.Length];
            var dogStateLabels = new TextMeshProUGUI[BoardDogNameKeys.Length];
            var fixedLabels = new List<TextMeshProUGUI>();
            int dogI = 0;
            foreach (var s in specs)
            {
                var color = s.kind == 0 ? KominkaDogRun.Editor.KD_UiSpriteEditor.ActionPrimary : s.kind == 1 ? KominkaDogRun.Editor.KD_UiSpriteEditor.ActionClose : KominkaDogRun.Editor.KD_UiSpriteEditor.ActionNeutral;
                string label = s.kind == 0 ? names[dogI] : BoardText(catalog, ja, s.key);
                var bgo = KominkaDogRun.Editor.KD_ShopSetupEditor.EnsureButton(go.transform, s.name, s.pos, s.size, color, font, label, backing, s.evt, log);
                var lab = bgo.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                if (s.kind == 0)
                {
                    // two lines: the name (top) and what the dog is doing / why it cannot come (bottom). IMP-DOG-037
                    var lr = (RectTransform)lab.transform; lr.anchoredPosition = DogNamePos; lr.sizeDelta = DogNameSize;
                    lab.fontSize = DogNamePt; lab.enableAutoSizing = true; lab.fontSizeMax = DogNamePt; lab.fontSizeMin = MinPt; lab.color = Color.white;
                    EditorUtility.SetDirty(lab);
                    dogImages[dogI] = bgo.GetComponent<UnityEngine.UI.Image>();
                    dogNameLabels[dogI] = lab;
                    dogStateLabels[dogI] = EnsureBoardLabel(bgo.transform, DogStateName, DogStatePos, DogStateSize, font, DogStatePt, BoardInkState, words[KH_WashStationBoard.WStCallable]);
                    dogI++;
                }
                else
                {
                    lab.fontSize = ButtonPt; lab.enableAutoSizing = true; lab.fontSizeMax = ButtonPt; lab.fontSizeMin = MinPt;
                    EditorUtility.SetDirty(lab);
                    var stray = bgo.transform.Find(DogStateName); if (stray != null) UnityEngine.Object.DestroyImmediate(stray.gameObject);
                    fixedLabels.Add(lab);
                }
            }
            foreach (Transform c in go.transform.Cast<Transform>().ToArray())
                if (c.name != "Bg" && c.name != StatusName && c.name != RuleName && c.name != HeightsName && !specs.Any(s => s.name == c.name))
                { r.log.Add("removed stale " + st.name + "/" + BoardName + "/" + c.name); UnityEngine.Object.DestroyImmediate(c.gameObject); }

            b.call = call; b.personalFloor = pf; b.language = language;
            b.statusText = status; b.ruleText = rule; b.heightText = heights;
            b.dogButtons = dogImages; b.dogNameLabels = dogNameLabels; b.dogStateLabels = dogStateLabels; b.buttonLabels = fixedLabels.ToArray();
            b.dogNameKeys = (string[])BoardDogNameKeys.Clone(); b.dogNames = names;
            b.wordKeys = (string[])BoardWordKeys.Clone(); b.words = words;
            b.colorCallable = KominkaDogRun.Editor.KD_UiSpriteEditor.ActionPrimary;
            b.colorHere = KominkaDogRun.Editor.KD_UiSpriteEditor.ActionEquip;
            b.colorUnavailable = KominkaDogRun.Editor.KD_UiSpriteEditor.TabOff;
            b.inkName = Color.white; b.inkNameDim = BoardInkDim; b.inkState = BoardInkState;
            b.inkReason = KominkaDogRun.Editor.KD_UiSpriteEditor.AccentGold;
            EditorUtility.SetDirty(b);
            if (log.Length > 0) r.log.Add(st.name + "/" + BoardName + ": " + log.ToString().Trim().Replace("\n", " | "));
            return b;
        }

        static void BuildReset(Transform station, List<KH_WashTool> tools, Material dark, BuildReport r)
        {
            RequireStationProgram(ResetProgram);
            var board = Prim(station, ResetName, PrimitiveType.Cube, true);   // the collider is what Interact aims at
            board.layer = 0;
            board.transform.localPosition = ResetLocal;
            board.transform.localRotation = Quaternion.identity;
            board.transform.localScale = ResetSize;
            UnitBox(board);
            SetLook(board, dark);
            var reset = AddUdon<KH_WashStationReset>(board, Networking.SyncType.None);
            reset.tools = tools.ToArray();
            EditorUtility.SetDirty(reset);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(reset);
            // The words are the user's once written: only the UdonBehaviour default ("Use") or an empty text gets the placeholder.
            if (string.IsNullOrEmpty(backing.interactText) || backing.interactText == "Use") { backing.interactText = ResetInteractText; EditorUtility.SetDirty(backing); }
            if (backing.proximity < 1.5f) { backing.proximity = 1.5f; EditorUtility.SetDirty(backing); }

            // the label on the board's front (-Z), like the sign
            var textT = Child(station, ResetTextName);
            textT.gameObject.layer = 0;
            var tmp = textT.GetComponent<TextMeshPro>();
            bool fresh = tmp == null;
            if (fresh) tmp = textT.gameObject.AddComponent<TextMeshPro>();
            textT = tmp.transform;
            textT.localPosition = new Vector3(ResetLocal.x, ResetLocal.y, ResetLocal.z - ResetSize.z * 0.5f - 0.002f);
            textT.localRotation = Quaternion.identity; textT.localScale = Vector3.one;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KominkaDogRun.Editor.KD_ShopSetupEditor.SharedFontPath);
            if (font == null) throw new InvalidOperationException("Shared UI font missing: " + KominkaDogRun.Editor.KD_ShopSetupEditor.SharedFontPath);
            tmp.font = font;
            if (fresh || string.IsNullOrEmpty(tmp.text)) tmp.text = ResetLabel;
            tmp.alignment = TextAlignmentOptions.Center; tmp.enableWordWrapping = false;
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.15f; tmp.fontSizeMax = 0.7f;
            tmp.color = SignInk;
            tmp.rectTransform.sizeDelta = new Vector2(ResetSize.x - 0.03f, ResetSize.y - 0.02f);
            var mr = tmp.GetComponent<MeshRenderer>();
            if (mr != null) { mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; EditorUtility.SetDirty(mr); }
            EditorUtility.SetDirty(tmp);

            Copy(reset);   // the main Build copies the tools; this behaviour is the station's own (its tools are wired above)
            r.log.Add(ResetName + " wired to " + tools.Count + " tools");
        }

        static KH_WashTool BuildTool(Transform station, ToolSpec spec, Shared shared, Audio audio, Transform home, Material towelMat)
        {
            var root = Child(station, spec.name);
            root.gameObject.layer = PickupLayer;
            root.localPosition = spec.slot; root.localRotation = Quaternion.Euler(spec.euler); root.localScale = Vector3.one;   // code-owned resting slot

            // ---- the one collider on the tool root (Pickup) ----
            bool capsule = spec.tool == KH_DogWash.ToolShower || spec.tool == KH_DogWash.ToolBottle;
            foreach (var col in root.GetComponents<Collider>())
            {
                bool want = capsule ? col is CapsuleCollider : col is BoxCollider;
                if (!want) UnityEngine.Object.DestroyImmediate(col);
            }
            var nozzle = Child(root, "Nozzle");
            nozzle.gameObject.layer = PickupLayer;
            Transform sprayT = null;
            ParticleSystem squirt = null;
            if (spec.tool != KH_DogWash.ToolBottle) { var oldSquirt = nozzle.Find(BottleSquirtName); if (oldSquirt != null) UnityEngine.Object.DestroyImmediate(oldSquirt.gameObject); }
            if (spec.tool == KH_DogWash.ToolBottle)
            {
                squirt = BuildBottleBody(root, nozzle, shared);   // body, nozzle, the capsule and the particles
            }
            else if (spec.tool == KH_DogWash.ToolShower)
            {
                var cap = root.GetComponent<CapsuleCollider>(); if (cap == null) cap = root.gameObject.AddComponent<CapsuleCollider>();
                cap.isTrigger = false; cap.direction = 1; cap.radius = 0.028f; cap.height = 0.20f; cap.center = new Vector3(0f, 0.01f, 0f);
                var handle = Prim(root, "Handle", PrimitiveType.Cylinder, false);
                handle.transform.localPosition = Vector3.zero; handle.transform.localRotation = Quaternion.identity; handle.transform.localScale = new Vector3(0.036f, 0.08f, 0.036f);   // 0.16 m tall
                SetLook(handle, shared.showerBody);
                var head = Prim(root, "Head", PrimitiveType.Cylinder, false);
                head.transform.localPosition = new Vector3(0f, 0.09f, 0.015f); head.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); head.transform.localScale = new Vector3(0.07f, 0.015f, 0.07f);   // face along +Z
                SetLook(head, shared.showerHead);
                nozzle.localPosition = new Vector3(0f, 0.09f, 0.035f); nozzle.localRotation = Quaternion.identity; nozzle.localScale = Vector3.one;   // spray along +Z
                sprayT = Child(nozzle, "Spray");
                sprayT.gameObject.layer = PickupLayer;
                sprayT.localPosition = Vector3.zero; sprayT.localRotation = Quaternion.identity; sprayT.localScale = Vector3.one;
            }
            else
            {
                var box = root.GetComponent<BoxCollider>(); if (box == null) box = root.gameObject.AddComponent<BoxCollider>();
                box.isTrigger = false; box.center = Vector3.zero;
                var look = Prim(root, "Visual", PrimitiveType.Cube, false);
                look.transform.localPosition = Vector3.zero; look.transform.localRotation = Quaternion.identity;
                if (spec.tool == KH_DogWash.ToolSponge)
                {
                    box.size = new Vector3(0.12f, 0.06f, 0.08f);
                    look.transform.localScale = box.size;
                    SetLook(look, shared.sponge);
                    nozzle.localPosition = new Vector3(0f, -0.02f, 0f);
                }
                else
                {
                    box.size = new Vector3(0.30f, 0.02f, 0.20f);
                    look.transform.localScale = box.size;
                    SetLook(look, towelMat != null ? towelMat : shared.towel);
                    nozzle.localPosition = Vector3.zero;
                }
                nozzle.localRotation = Quaternion.identity; nozzle.localScale = Vector3.one;
                var oldSpray = nozzle.Find("Spray"); if (oldSpray != null) UnityEngine.Object.DestroyImmediate(oldSpray.gameObject);
            }

            // ---- physics / pickup / sync (the disc and the leash tips are the precedent) ----
            var rb = root.GetComponent<Rigidbody>(); if (rb == null) rb = root.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.None;
            var pickup = root.GetComponent<VRCPickup>(); if (pickup == null) pickup = root.gameObject.AddComponent<VRCPickup>();
            pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;   // Desktop: the pickup stays in hand and Use reaches OnPickupUseDown
            pickup.orientation = VRC_Pickup.PickupOrientation.Any;
            pickup.proximity = 0.3f; pickup.pickupable = true; pickup.allowManipulationWhenEquipped = false;
            pickup.InteractionText = ToolLabels[spec.tool];
            // The shower toggles (2026-09-30), the bottle foams (IMP-DOG-039). ASCII placeholders; the Japanese is typed in the
            // Inspector (kept once changed: only an empty text or one of the placeholders is overwritten).
            string useText = ToolUseTexts[spec.tool];
            if (string.IsNullOrEmpty(pickup.UseText) || ToolUsePlaceholders.Contains(pickup.UseText)) pickup.UseText = useText;
            if (root.GetComponent<VRCObjectSync>() == null) root.gameObject.AddComponent<VRCObjectSync>();
            EditorUtility.SetDirty(rb); EditorUtility.SetDirty(pickup);

            // ---- KH_WashTool (NoVariableSync = None) + State (Manual, a child: VRCObjectSync is Continuous) ----
            var tool = AddUdon<KH_WashTool>(root.gameObject, Networking.SyncType.None);
            var stateT = Child(root, "State");
            stateT.gameObject.layer = PickupLayer;
            stateT.localPosition = Vector3.zero; stateT.localRotation = Quaternion.identity; stateT.localScale = Vector3.one;
            var state = AddUdon<KH_WashToolState>(stateT.gameObject, Networking.SyncType.Manual);
            state.washTool = tool; EditorUtility.SetDirty(state);

            tool.tool = spec.tool;
            tool.nozzle = nozzle;
            tool.state = state;
            tool.washLayer = 1 << WashLayer;
            tool.home = home;                       // IMP-ENV-007: the return target (moves with the station)
            tool.returnSeconds = ReturnSeconds;
            tool.homeTolerance = HomeTolerance;
            tool.debugHeldOverride = 0;             // harness only
            if (spec.tool == KH_DogWash.ToolShower)
            {
                tool.spray = SprayParticles(sprayT.gameObject, shared.spray);
                tool.sprayAudio = LoopSource(nozzle.gameObject, audio.clips[ClipSpray], 0.7f);
            }
            else
            {
                tool.spray = null;
                var src = nozzle.GetComponent<AudioSource>(); if (src != null) UnityEngine.Object.DestroyImmediate(src);
                tool.sprayAudio = null;
            }
            tool.squirtParticles = squirt;          // the bottle's Squirt (null on the other tools)
            EditorUtility.SetDirty(tool);
            // IMP-DOG-039: the bottle's tuning (design §8.1, BottleTuning); the other tools keep the code defaults (unused there)
            if (spec.tool == KH_DogWash.ToolBottle) WriteFloats(tool, BottleTuning);
            ApplyToolArt(root, spec.tool);
            return tool;
        }

        /// <summary>Writes a (field, value) tuning table on a proxy through SerializedObject (the builder copies it to Udon later).</summary>
        static void WriteFloats(UnityEngine.Object o, (string field, float value)[] table)
        {
            var so = new SerializedObject(o);
            foreach (var t in table) Prop(so, t.field).floatValue = t.value;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(o);
        }

        /// <summary>IMP-DOG-039 §2.1: the bottle's look from primitives (bottle local; the root sits BottleRestY above the shelf top):
        /// body cylinder φ0.06 x 0.15 (pale pink), a squashed sphere for the shoulder, a white cap φ0.03 x 0.02, and the spout φ0.012 x 0.03
        /// tilted BottleSpoutTilt toward +Z. Nozzle at the spout's tip, forward = the spout's direction. One CapsuleCollider on the root
        /// (the primitives' own colliders are removed by Prim). The Squirt particles hang under the nozzle.</summary>
        static ParticleSystem BuildBottleBody(Transform root, Transform nozzle, Shared shared)
        {
            var pink = BottleMaterial(ShampooMat, ShampooPink);
            var white = BottleMaterial(ShampooCapMat, ShampooCapWhite);   // its own near-white (no other tool's material)
            float foot = -BottleRestY;
            float bodyTop = foot + BottleBodyHeight;

            var body = Prim(root, "Body", PrimitiveType.Cylinder, false);   // the cylinder primitive is 2 units tall: y scale = half height
            body.transform.localPosition = new Vector3(0f, foot + BottleBodyHeight * 0.5f, 0f);
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = new Vector3(BottleBodyDiameter, BottleBodyHeight * 0.5f, BottleBodyDiameter);
            SetLook(body, pink);

            const float shoulderHeight = 0.03f;
            var shoulder = Prim(root, "Shoulder", PrimitiveType.Sphere, false);
            shoulder.transform.localPosition = new Vector3(0f, bodyTop, 0f);
            shoulder.transform.localRotation = Quaternion.identity;
            shoulder.transform.localScale = new Vector3(BottleBodyDiameter, shoulderHeight, BottleBodyDiameter);
            SetLook(shoulder, pink);

            float capBase = bodyTop + shoulderHeight * 0.5f - 0.005f;   // slightly sunk into the shoulder
            var cap = Prim(root, "Cap", PrimitiveType.Cylinder, false);
            cap.transform.localPosition = new Vector3(0f, capBase + BottleCapHeight * 0.5f, 0f);
            cap.transform.localRotation = Quaternion.identity;
            cap.transform.localScale = new Vector3(BottleCapDiameter, BottleCapHeight * 0.5f, BottleCapDiameter);
            SetLook(cap, white);

            var tilt = Quaternion.Euler(BottleSpoutTilt, 0f, 0f);            // up -> (0, cos, sin): leans toward +Z
            Vector3 dir = tilt * Vector3.up;
            Vector3 spoutBase = new Vector3(0f, capBase + BottleCapHeight - 0.003f, 0f);
            var spout = Prim(root, "Spout", PrimitiveType.Cylinder, false);
            spout.transform.localPosition = spoutBase + dir * (BottleSpoutLength * 0.5f);
            spout.transform.localRotation = tilt;
            spout.transform.localScale = new Vector3(BottleSpoutDiameter, BottleSpoutLength * 0.5f, BottleSpoutDiameter);
            SetLook(spout, white);
            foreach (var g in new[] { body, shoulder, cap, spout }) g.layer = PickupLayer;

            nozzle.localPosition = spoutBase + dir * BottleSpoutLength;
            nozzle.localRotation = Quaternion.LookRotation(dir, Vector3.back);   // forward = the spout
            nozzle.localScale = Vector3.one;
            var oldSpray = nozzle.Find("Spray"); if (oldSpray != null) UnityEngine.Object.DestroyImmediate(oldSpray.gameObject);

            // the pickup collider: one capsule from the foot to the spout's tip
            float topY = nozzle.localPosition.y + BottleSpoutDiameter * 0.5f;
            var capCol = root.GetComponent<CapsuleCollider>(); if (capCol == null) capCol = root.gameObject.AddComponent<CapsuleCollider>();
            capCol.isTrigger = false; capCol.direction = 1;
            capCol.radius = BottleBodyDiameter * 0.5f;
            capCol.height = topY - foot;
            capCol.center = new Vector3(0f, (topY + foot) * 0.5f, 0f);
            EditorUtility.SetDirty(capCol);

            var sq = Child(nozzle, BottleSquirtName);
            sq.gameObject.layer = PickupLayer;
            sq.localPosition = Vector3.zero; sq.localRotation = Quaternion.identity; sq.localScale = Vector3.one;
            return SquirtParticles(sq.gameObject, shared.foam);
        }

        /// <summary>A bottle material (ToolMaterial: the same shader, folder and colour) made matte: no albedo texture, _Metallic 0,
        /// low gloss. Applied to an existing asset too (idempotent repair).</summary>
        static Material BottleMaterial(string name, Color color)
        {
            var m = ToolMaterial(name, color);
            m.mainTexture = null;
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", BottleMetallic);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", BottleGlossiness);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>IMP-DOG-039 §2.5: the bottle's squirt: a few foam blobs along the nozzle's forward (cone), World, no Play On Awake,
        /// emission off (KH_WashTool.Squirt calls Emit(8); the holder's screen only).</summary>
        static ParticleSystem SquirtParticles(GameObject go, Material mat)
        {
            var ps = go.GetComponent<ParticleSystem>(); if (ps == null) ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false; main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.018f);
            main.gravityModifier = 0.6f;
            main.maxParticles = 48;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6f; sh.radius = 0.003f;
            sh.rotation = Vector3.zero; sh.position = Vector3.zero;
            var colMod = ps.collision; colMod.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(.95f, 0f), new GradientAlphaKey(.8f, .6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = mat; rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.shadowCastingMode = ShadowCastingMode.Off; rend.receiveShadows = false;
            EditorUtility.SetDirty(ps); EditorUtility.SetDirty(rend);
            return ps;
        }

        // ================================================================ IMP-ENV-007: the stand, the duckboard, the tub, the sign

        static void BuildStand(Transform station, Material wood, Material dark, BuildReport r)
        {
            // ---- the stand: the top is "Shelf" (the tools rest on it; Wash/12 checks its solid collider) + 4 legs ----
            var top = Prim(station, "Shelf", PrimitiveType.Cube, true);
            top.layer = 0;
            top.transform.localPosition = new Vector3(0f, ShelfHeight - ShelfTopThickness * 0.5f, 0f);
            top.transform.localRotation = Quaternion.identity;
            top.transform.localScale = new Vector3(ShelfWidth, ShelfTopThickness, ShelfDepth);
            UnitBox(top);
            SetLook(top, wood);
            float legH = ShelfHeight - ShelfTopThickness;
            for (int i = 0; i < 4; i++)
            {
                var leg = Prim(station, "ShelfLeg_" + i, PrimitiveType.Cube, true);
                leg.layer = 0;
                float sx = (i & 1) == 0 ? -1f : 1f, sz = (i & 2) == 0 ? -1f : 1f;
                leg.transform.localPosition = new Vector3(sx * (ShelfWidth * 0.5f - ShelfLegInset.x), legH * 0.5f, sz * (ShelfDepth * 0.5f - ShelfLegInset.y));
                leg.transform.localRotation = Quaternion.identity;
                leg.transform.localScale = new Vector3(ShelfLegSize, legH, ShelfLegSize);
                UnitBox(leg);
                SetLook(leg, wood);
            }

            // ---- the duckboard (すのこ): one solid BoxCollider (StandLayer at the end of the Build: the players pass through it) + slats ----
            // IMP-ENV-011: the duckboard lives under "Lift" (the stand KH_WashStationCall raises: liftRoot). Lift sits at the
            // station's origin with no rotation, so the board keeps the same local numbers as before; an older duckboard
            // directly under the station is moved under it (same world pose).
            var lift = Child(station, LiftName);
            lift.gameObject.layer = 0;
            lift.localPosition = Vector3.zero; lift.localRotation = Quaternion.identity; lift.localScale = Vector3.one;
            var oldTable = station.Find(TableName);
            if (oldTable != null) { oldTable.SetParent(lift, false); r.log.Add(station.name + "/" + TableName + " moved under " + LiftName); }
            var table = Child(lift, TableName);
            table.gameObject.layer = 0;
            float tableY = MaxGround(station, new Vector3(0f, 0f, -TableFront), TableSize.x, TableSize.z);
            table.localPosition = new Vector3(0f, tableY, -TableFront);
            table.localRotation = Quaternion.identity; table.localScale = Vector3.one;
            foreach (var c in table.GetComponents<Collider>()) if (!(c is BoxCollider)) UnityEngine.Object.DestroyImmediate(c);
            var box = table.GetComponent<BoxCollider>(); if (box == null) box = table.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = false; box.enabled = true; box.size = TableSize; box.center = new Vector3(0f, TableSize.y * 0.5f, 0f);
            EditorUtility.SetDirty(box);
            float slatDepth = 0.15f, pitch = (TableSize.z - slatDepth) / (TableSlats - 1);
            for (int i = 0; i < TableSlats; i++)
            {
                var slat = Prim(table, "Slat_" + i, PrimitiveType.Cube, false);
                slat.transform.localPosition = new Vector3(0f, TableSize.y * 0.5f, -TableSize.z * 0.5f + slatDepth * 0.5f + pitch * i);
                slat.transform.localRotation = Quaternion.identity;
                slat.transform.localScale = new Vector3(TableSize.x, TableSize.y, slatDepth);
                SetLook(slat, wood);
            }
            foreach (Transform t in table.Cast<Transform>().ToArray())
                if (t.name.StartsWith("Slat_") && (!int.TryParse(t.name.Substring(5), out int k) || k >= TableSlats)) UnityEngine.Object.DestroyImmediate(t.gameObject);
            r.log.Add(station.name + "/" + TableName + " local y " + tableY.ToString("F3") + " (highest ground under the board)");
            // the point the called dog stands on: the duckboard's top centre, under Lift (moves with the stand)
            var anchor = Child(lift, AnchorName);
            anchor.gameObject.layer = 0;
            anchor.localPosition = new Vector3(0f, tableY + TableSize.y, -TableFront); anchor.localRotation = Quaternion.identity; anchor.localScale = Vector3.one;
            foreach (var comp in anchor.GetComponents<Component>()) if (!(comp is Transform)) UnityEngine.Object.DestroyImmediate(comp);

            // ---- the tub (桶): removed (user's decision 2026-10-01: a decoration with no function). An older one is deleted. ----
            var oldTub = station.Find(TubName);
            if (oldTub != null) { UnityEngine.Object.DestroyImmediate(oldTub.gameObject); r.log.Add("removed " + station.name + "/" + TubName + " (no tub any more)"); }

            // ---- the sign: a small board at the back of the top, text facing the front (-Z; TMP faces -Z) ----
            var sign = Prim(station, SignName, PrimitiveType.Cube, false);   // no collider: never blocks a pickup ray
            sign.layer = 0;
            float signY = ShelfHeight + SignSize.y * 0.5f;
            sign.transform.localPosition = new Vector3(0f, signY, SignZ);
            sign.transform.localRotation = Quaternion.identity;
            sign.transform.localScale = SignSize;
            SetLook(sign, dark);
            var textT = Child(station, SignTextName);
            textT.gameObject.layer = 0;
            var tmp = textT.GetComponent<TextMeshPro>();
            bool fresh = tmp == null;
            if (fresh) tmp = textT.gameObject.AddComponent<TextMeshPro>();
            textT = tmp.transform;
            textT.localPosition = new Vector3(0f, signY, SignZ - SignSize.z * 0.5f - 0.002f);
            textT.localRotation = Quaternion.identity; textT.localScale = Vector3.one;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KominkaDogRun.Editor.KD_ShopSetupEditor.SharedFontPath);
            if (font == null) throw new InvalidOperationException("Shared UI font missing: " + KominkaDogRun.Editor.KD_ShopSetupEditor.SharedFontPath);
            tmp.font = font;
            // 2026-10-03 (user): the sign reads 「あらいば」. The old ASCII placeholder is replaced; any other words typed in the Inspector are kept.
            if (fresh || string.IsNullOrEmpty(tmp.text) || tmp.text == SignPlaceholder) tmp.text = SignText;
            tmp.alignment = TextAlignmentOptions.Center; tmp.enableWordWrapping = false;
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.2f; tmp.fontSizeMax = 0.9f;
            tmp.color = SignInk;
            tmp.rectTransform.sizeDelta = new Vector2(SignSize.x - 0.03f, SignSize.y - 0.02f);
            var tr = tmp.GetComponent<MeshRenderer>();
            if (tr != null) { tr.shadowCastingMode = ShadowCastingMode.Off; tr.receiveShadows = false; EditorUtility.SetDirty(tr); }
            EditorUtility.SetDirty(tmp);
            ApplyStationArt(station);
        }

        /// <summary>A primitive whose BoxCollider is the unit box (the transform's scale sizes it).</summary>
        static void UnitBox(GameObject go)
        {
            var b = go.GetComponent<BoxCollider>();
            if (b == null) return;
            b.isTrigger = false; b.center = Vector3.zero; b.size = Vector3.one; EditorUtility.SetDirty(b);
        }

        /// <summary>The highest ground (station local y) under a rectangle (centre + corners) around a station-local point.</summary>
        static float MaxGround(Transform station, Vector3 local, float sizeX, float sizeZ)
        {
            float best = float.NegativeInfinity;
            foreach (var p in RectSamples(local, sizeX, sizeZ))
                best = Mathf.Max(best, StationGround(station, station.TransformPoint(p)) - station.position.y);
            return best;
        }

        static Vector3[] RectSamples(Vector3 c, float sx, float sz)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            return new[] { c, c + new Vector3(-hx, 0f, -hz), c + new Vector3(hx, 0f, -hz), c + new Vector3(-hx, 0f, hz), c + new Vector3(hx, 0f, hz) };
        }

        /// <summary>The ground under a world point = the terrain only (the course tiles are Terrains on layer 11). A rock,
        /// a stand or a board is never taken for the ground (Codex 2026-09-30 7-1: the side-room check lifted its box onto
        /// whatever solid top it found). The TerrainCollider hit is used when there is one (what the players stand on),
        /// otherwise the covering Terrain's SampleHeight. No Terrain under the point: an exception (nothing is guessed).</summary>
        static float StationGround(Transform station, Vector3 world)
        {
            Physics.SyncTransforms();
            var hits = Physics.RaycastAll(new Vector3(world.x, world.y + 3f, world.z), Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);
            float bestDist = float.PositiveInfinity, y = 0f;
            foreach (var h in hits)
            {
                if (!(h.collider is TerrainCollider)) continue;
                if (h.distance < bestDist) { bestDist = h.distance; y = h.point.y; }
            }
            if (bestDist < float.PositiveInfinity) return y;
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Terrain>())
            {
                var p = t.transform.position; var s = t.terrainData.size;
                if (world.x >= p.x && world.x <= p.x + s.x && world.z >= p.z && world.z <= p.z + s.z) return t.SampleHeight(world) + p.y;
            }
            throw new InvalidOperationException("No Terrain under " + world.ToString("F2") + " (the wash station is seated on the terrain only)");
        }

        /// <summary>放水: 円錐、3〜4 m/s、寿命 0.4 s、重力あり、World。Collision (World) で地面・環境に当たって消え、Trigger で部位 Collider に入ると消える
        /// (Collision モジュールは Trigger の Collider に当たらないため)。当たった位置の飛沫は KH_DogWash が出すのでサブエミッターは使わない。</summary>
        static ParticleSystem SprayParticles(GameObject go, Material mat)
        {
            var ps = go.GetComponent<ParticleSystem>(); if (ps == null) ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false; main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.4f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.028f);
            main.gravityModifier = 1f;
            main.maxParticles = 160;
            var em = ps.emission; em.enabled = true; em.rateOverTime = 140f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 7f; sh.radius = 0.012f;
            sh.rotation = Vector3.zero; sh.position = Vector3.zero;
            var colMod = ps.collision;
            colMod.enabled = true; colMod.type = ParticleSystemCollisionType.World; colMod.mode = ParticleSystemCollisionMode.Collision3D;
            colMod.collidesWith = (1 << 0) | (1 << 11);   // Default + Environment (the terrain and the course)
            colMod.lifetimeLoss = 1f; colMod.bounce = 0f; colMod.dampen = 1f;
            colMod.quality = ParticleSystemCollisionQuality.Medium; colMod.sendCollisionMessages = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(.8f, 0f), new GradientAlphaKey(.4f, 1f) });
            col.color = g;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = mat; rend.renderMode = ParticleSystemRenderMode.Stretch; rend.velocityScale = 0.02f; rend.lengthScale = 1.5f;
            rend.shadowCastingMode = ShadowCastingMode.Off; rend.receiveShadows = false;
            EditorUtility.SetDirty(ps); EditorUtility.SetDirty(rend);
            return ps;
        }

        /// <summary>Trigger モジュール: 部位 Collider (全部の犬) に入った粒を消す。</summary>
        static void SetSprayTriggers(ParticleSystem ps, Collider[] colliders)
        {
            var tr = ps.trigger;
            while (tr.colliderCount > 0) tr.RemoveCollider(0);
            foreach (var c in colliders) if (c != null) tr.AddCollider(c);
            tr.enabled = colliders.Length > 0;
            tr.enter = ParticleSystemOverlapAction.Kill; tr.inside = ParticleSystemOverlapAction.Kill;
            tr.exit = ParticleSystemOverlapAction.Ignore; tr.outside = ParticleSystemOverlapAction.Ignore;
            tr.radiusScale = 1f;
            EditorUtility.SetDirty(ps);
        }

        static GameObject Prim(Transform parent, string name, PrimitiveType type, bool keepCollider)
        {
            var t = parent.Find(name);
            GameObject go;
            if (t == null) { go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false); }
            else go = t.gameObject;
            if (go.GetComponent<MeshFilter>() == null) go.AddComponent<MeshFilter>();
            if (go.GetComponent<MeshRenderer>() == null) go.AddComponent<MeshRenderer>();
            if (!keepCollider) foreach (var c in go.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            else if (go.GetComponent<Collider>() == null) go.AddComponent<BoxCollider>();
            go.layer = parent.gameObject.layer;
            return go;
        }

        static void SetLook(GameObject go, Material mat)
        {
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
            EditorUtility.SetDirty(r);
        }

        /// <summary>地面の高さ: その点を覆う Terrain の SampleHeight、無ければ下向きの Raycast (Default + Environment、Trigger 無視)。</summary>
        static float Ground(float x, float z, float yHint)
        {
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Terrain>())
            {
                var p = t.transform.position; var s = t.terrainData.size;
                if (x >= p.x && x <= p.x + s.x && z >= p.z && z <= p.z + s.z) return t.SampleHeight(new Vector3(x, 0f, z)) + p.y;
            }
            if (Physics.Raycast(new Vector3(x, yHint + 20f, z), Vector3.down, out RaycastHit hit, 60f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore)) return hit.point.y;
            throw new InvalidOperationException("No ground at " + x + ", " + z);
        }
    }
}
