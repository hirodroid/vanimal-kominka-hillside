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
        static readonly Vector3 TubLocal = new Vector3(-0.95f, 0f, -0.05f);            // west of the shelf (the east side faces the leash rack)
        const float TubRadius = 0.2f, TubHeight = 0.26f;
        static readonly Vector3 SignSize = new Vector3(0.34f, 0.12f, 0.02f);
        const float SignZ = 0.19f;                                                      // at the back of the shelf top, facing the front (-Z)
        const string SignText = "WASH";                                                 // ASCII placeholder; the Japanese is typed in the Inspector (layout doc §5)
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
        };
        static readonly string[] ToolLabels = { "Sponge", "Shower", "Towel" };

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
            foreach (var st in stations) BuildStationControls(st, StationIndexOf(st), r);
            SetFloorPassthrough(r);
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
                tools.Add(BuildTool(st, spec, shared, audio, home, towelMat));
            }
            // stale tools of an earlier layout
            foreach (Transform t in st.Cast<Transform>().ToArray())
                if ((t.name.StartsWith("KH_WashSponge_") || t.name.StartsWith("KH_WashShower_") || t.name.StartsWith("KH_WashTowel_")) && !specs.Any(s => s.name == t.name))
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
            };
        }

        /// <summary>Tools of every station in the builder's order (main ToolSpecs, then CourseStations): how many
        /// KH_DogWash.tools must hold (Verify.cs line "tools: n / ToolSpecs.Length" should compare with this).</summary>
        static int AllToolCount() { return ToolSpecs.Length + CourseStations.Sum(s => s.tools.Length); }

        /// <summary>The KH_WashTool of every station present in the scene, main first, in the builder's order (missing ones skipped).</summary>
        static List<KH_WashTool> AllStationTools(UnityEngine.SceneManagement.Scene scene)
        {
            var list = new List<KH_WashTool>();
            foreach (var st in AllStationTransforms(scene))
            {
                var specs = st.name == StationName && st.parent == null ? ToolSpecs : CourseStations.Where(s => s.key == st.name).Select(s => s.tools).FirstOrDefault();
                if (specs == null) continue;
                foreach (var spec in specs) { var t = st.Find(spec.name); var tool = t != null ? t.GetComponent<KH_WashTool>() : null; if (tool != null) list.Add(tool); }
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
                    .Where(x => !(x is TerrainCollider) && x.attachedRigidbody == null && (self == null || !x.transform.IsChildOf(self)))
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
        static readonly Vector2 FloorHoleMin = new Vector2(-0.75f, -0.45f), FloorHoleMax = new Vector2(0.75f, 1.5f);
        // ---- the board: a World Space Canvas standing at the back of the shelf top, above the sign, facing the front (-Z) ----
        static readonly Vector2 BoardPixels = new Vector2(600f, 360f);
        const float BoardScale = 0.001f;   // 1 px = 1 mm: 0.60 x 0.36 m
        static readonly Vector3 BoardLocal = new Vector3(0f, ShelfHeight + 0.14f + 0.18f, ShelfDepth * 0.5f + 0.012f);   // bottom 0.94 m (above the sign), top 1.30 m
        static readonly string[] DogNamesJa = { "コーギー", "ポメ", "ラブ", "ダックス", "柴", "サモエド" };   // dogIndex order (KD_DogDefs)
        const string WordPick = "犬をえらんでよぶ", WordCalling = "を呼んでいます", WordHere = "がいます", WordBusy = "いまは来られません", WordLeashed = "ほかの人が散歩中です", WordStand = "台", WordMe = "自分";
        const string WordEnd = "おわり", WordStandUp = "台 上へ", WordStandDown = "台 下へ", WordMeUp = "自分 上へ", WordMeDown = "自分 下へ";
        class BoardButton
        {
            public string name, label, evt; public Vector2 pos, size; public int kind;   // kind 0 dog / 1 end / 2 stand / 3 me
            public BoardButton(string name, string label, string evt, Vector2 pos, Vector2 size, int kind) { this.name = name; this.label = label; this.evt = evt; this.pos = pos; this.size = size; this.kind = kind; }
        }
        static BoardButton[] BoardButtons()
        {
            var list = new List<BoardButton>();
            for (int i = 0; i < DogNamesJa.Length; i++)
                list.Add(new BoardButton("Dog_" + i, DogNamesJa[i], "_Dog" + i, new Vector2(-190f + 190f * (i % 3), i < 3 ? 78f : 18f), new Vector2(178f, 52f), 0));
            list.Add(new BoardButton("StandUp", WordStandUp, "_LiftUp", new Vector2(-210f, -48f), new Vector2(118f, 52f), 2));
            list.Add(new BoardButton("StandDown", WordStandDown, "_LiftDown", new Vector2(-86f, -48f), new Vector2(118f, 52f), 2));
            list.Add(new BoardButton("MeUp", WordMeUp, "_MeUp", new Vector2(64f, -48f), new Vector2(138f, 52f), 3));
            list.Add(new BoardButton("MeDown", WordMeDown, "_MeDown", new Vector2(208f, -48f), new Vector2(138f, 52f), 3));
            list.Add(new BoardButton("End", WordEnd, "_End", new Vector2(0f, -116f), new Vector2(260f, 52f), 1));
            return list.ToArray();
        }

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

        static void BuildStationControls(Transform st, int index, BuildReport r)
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
                new KeyValuePair<string, Rect>("West", Rect.MinMaxRect(-FloorHalf, -FloorHalf, FloorHoleMin.x, FloorHalf)),
                new KeyValuePair<string, Rect>("East", Rect.MinMaxRect(FloorHoleMax.x, -FloorHalf, FloorHalf, FloorHalf)),
                new KeyValuePair<string, Rect>("Front", Rect.MinMaxRect(FloorHoleMin.x, -FloorHalf, FloorHoleMax.x, FloorHoleMin.y)),
                new KeyValuePair<string, Rect>("Back", Rect.MinMaxRect(FloorHoleMin.x, FloorHoleMax.y, FloorHoleMax.x, FloorHalf)),
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
            var board = BuildBoard(st, call, pf, r);

            Copy(call); Copy(pf); Copy(board);
            r.log.Add(st.name + ": Call (index " + index + ", standYaw " + Mathf.Repeat(st.eulerAngles.y + StandYawOffset, 360f).ToString("F1") + "), " + FloorName + " (base " + baseY.ToString("F3") + "), " + BoardName + " wired");
        }

        static KH_WashStationBoard BuildBoard(Transform st, KH_WashStationCall call, KH_WashPersonalFloor pf, BuildReport r)
        {
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

            var statusGo = KominkaDogRun.Editor.KD_ShopSetupEditor.EnsureUiChild(go.transform, "Status", new Vector2(0f, 140f), new Vector2(560f, 64f));
            var status = statusGo.GetComponent<TextMeshProUGUI>(); if (status == null) status = statusGo.AddComponent<TextMeshProUGUI>();
            status.font = font; status.fontSize = 22f; status.alignment = TextAlignmentOptions.Center; status.color = Color.white;
            status.raycastTarget = false; status.enableWordWrapping = false; status.overflowMode = TextOverflowModes.Overflow;
            status.text = WordPick + "\n" + WordStand + " 0.0m   " + WordMe + " 0.0m";
            EditorUtility.SetDirty(status);

            var b = AddUdon<KH_WashStationBoard>(go, Networking.SyncType.None);
            b.call = call; b.personalFloor = pf; b.statusText = status;
            b.dogNames = (string[])DogNamesJa.Clone();
            b.wordPick = WordPick; b.wordCalling = WordCalling; b.wordHere = WordHere; b.wordBusy = WordBusy; b.wordLeashed = WordLeashed; b.wordStand = WordStand; b.wordMe = WordMe;
            EditorUtility.SetDirty(b);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            var log = new System.Text.StringBuilder();
            var specs = BoardButtons();
            foreach (var s in specs)
            {
                var color = s.kind == 0 ? KominkaDogRun.Editor.KD_UiSpriteEditor.ActionPrimary : s.kind == 1 ? KominkaDogRun.Editor.KD_UiSpriteEditor.ActionClose : KominkaDogRun.Editor.KD_UiSpriteEditor.ActionNeutral;
                KominkaDogRun.Editor.KD_ShopSetupEditor.EnsureButton(go.transform, s.name, s.pos, s.size, color, font, s.label, backing, s.evt, log);
            }
            foreach (Transform c in go.transform.Cast<Transform>().ToArray())
                if (c.name != "Bg" && c.name != "Status" && !specs.Any(s => s.name == c.name)) { r.log.Add("removed stale " + st.name + "/" + BoardName + "/" + c.name); UnityEngine.Object.DestroyImmediate(c.gameObject); }
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
            foreach (var col in root.GetComponents<Collider>())
            {
                bool want = spec.tool == KH_DogWash.ToolShower ? col is CapsuleCollider : col is BoxCollider;
                if (!want) UnityEngine.Object.DestroyImmediate(col);
            }
            var nozzle = Child(root, "Nozzle");
            nozzle.gameObject.layer = PickupLayer;
            Transform sprayT = null;
            if (spec.tool == KH_DogWash.ToolShower)
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
            // The shower toggles (2026-09-30). ASCII placeholders; the Japanese is typed in the Inspector (kept once changed).
            string useText = spec.tool == KH_DogWash.ToolShower ? "Shower on/off" : "Wash";
            if (string.IsNullOrEmpty(pickup.UseText) || pickup.UseText == "Use" || pickup.UseText == "Spray" || pickup.UseText == "Wash" || pickup.UseText == "Shower on/off") pickup.UseText = useText;
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
            EditorUtility.SetDirty(tool);
            ApplyToolArt(root, spec.tool);
            return tool;
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

            // ---- the duckboard (すのこ): one solid, walkable BoxCollider on Default (players, and the dogs' ground rays 0|11) + slats ----
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

            // ---- the tub (桶) ----
            var tub = Prim(station, TubName, PrimitiveType.Cylinder, true);
            tub.layer = 0;
            float tubY = MaxGround(station, TubLocal, TubRadius * 2f, TubRadius * 2f);
            tub.transform.localPosition = new Vector3(TubLocal.x, tubY + TubHeight * 0.5f, TubLocal.z);
            tub.transform.localRotation = Quaternion.identity;
            tub.transform.localScale = new Vector3(TubRadius * 2f, TubHeight * 0.5f, TubRadius * 2f);   // the Cylinder primitive is 2 units tall
            SetLook(tub, dark);

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
            // The words are the user's once written (the Japanese is typed in the Inspector): only an empty label gets the placeholder.
            if (fresh || string.IsNullOrEmpty(tmp.text)) tmp.text = SignText;
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
