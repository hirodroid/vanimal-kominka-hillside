using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    // IMP-ENV-007 checks (Wash/12 and the end of Wash/11): the main wash station's layout, the tools' homes and return
    // settings, the duckboard's collider, the distances to the hand-placed spots, the ground slope. Reads only.
    // Called from Check() in Verify.cs after CheckStation: CheckStationLayout(r, req, dogsGo.scene);
    // IMP-ENV-008: the same body checks (CheckStationBody) for every KH_WashStations/{key}, then their tools' components and
    // wiring, the placement rules of the Build (PlacementChecks) and every KH_DogWash.washStations (CheckCourseStations).
    public static partial class KH_DogWashSetupEditor
    {
        const float LayoutClearance = 1.5f, SlopeWarnDegrees = 5f;
        /// <summary>The dogs' waiting ring at the gate (KH_DogWalkingEditor initial placement x 44..48, z 46..48).</summary>
        static readonly Rect DogWaitRing = Rect.MinMaxRect(44f, 46f, 48f, 48f);

        struct Footprint { public string name; public Vector2 centre, size; public Footprint(string n, Vector2 c, Vector2 s) { name = n; centre = c; size = s; } }

        static Footprint[] StationFootprints()
        {
            return new[]
            {
                new Footprint("shelf", Vector2.zero, new Vector2(ShelfWidth, ShelfDepth)),
                new Footprint("duckboard", new Vector2(0f, -TableFront), new Vector2(TableSize.x, TableSize.z)),
                new Footprint("tub", new Vector2(TubLocal.x, TubLocal.z), new Vector2(TubRadius * 2f, TubRadius * 2f)),
            };
        }

        static void CheckStationLayout(VerifyReport r, Action<bool, string> req, UnityEngine.SceneManagement.Scene scene)
        {
            try { CheckStationLayoutInner(r, req, scene); }
            catch (Exception e) { req(false, "layout: check stopped: " + e.GetType().Name + ": " + e.Message); }
            try { CheckCourseStations(r, req, scene); }
            catch (Exception e) { req(false, "course stations: check stopped: " + e.GetType().Name + ": " + e.Message); }
        }

        static void CheckStationLayoutInner(VerifyReport r, Action<bool, string> req, UnityEngine.SceneManagement.Scene scene)
        {
            var roots = scene.GetRootGameObjects().Where(g => g.name == StationName).ToArray();
            if (roots.Length != 1) { req(false, "layout: " + StationName + " once at the scene root (" + roots.Length + ")"); return; }
            var st = roots[0].transform;
            Physics.SyncTransforms();

            // ---- where the last Build left it (human-owned; Build never moves it) ----
            BuildReport built = null;
            try { if (File.Exists(BuildReportPath)) built = JsonUtility.FromJson<BuildReport>(File.ReadAllText(BuildReportPath)); } catch (Exception) { built = null; }
            if (built != null && built.status == "BUILT")
                req((built.stationPosition - st.position).magnitude < 1e-3f, "layout: " + StationName + " at the position the last Build kept/created " + built.stationPosition.ToString("F3")
                    + " (now " + st.position.ToString("F3") + "; moved by hand after the Build? run Wash/11 again to seat the duckboard and the tub on the new ground)");
            else r.notes.Add("layout: no BUILT report in " + BuildReportPath + " (Temp is cleared when Unity closes): the station position was not compared with the last Build");

            CheckStationBody(r, req, st, ToolSpecs, "layout: " + StationName);
            var table = st.Find(TableName);

            // ---- distances to the hand-placed spots (IMP-ENV-003: never moved here), from the footprints ----
            var prints = StationFootprints();
            var spots = new List<KeyValuePair<string, Vector3>>();
            var desc = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
            if (desc != null && desc.spawns != null) for (int i = 0; i < desc.spawns.Length; i++) if (desc.spawns[i] != null) spots.Add(new KeyValuePair<string, Vector3>("spawn " + i + " (" + desc.spawns[i].name + ")", desc.spawns[i].position));
            foreach (var n in new[] { "KH_Entrance", "KD_ShopKeeper", "KD_WelcomeSpot", RackName })
            {
                var go = KD_SceneObjects.Find(n, out int count);
                req(go != null && count == 1, "layout: " + n + " found once (" + count + ")");
                if (go != null) spots.Add(new KeyValuePair<string, Vector3>(n, go.transform.position));
            }
            var treat = new Regex("^KD_Treat_\\d+$");
            var treats = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Where(x => treat.IsMatch(x.name)).ToArray();
            req(treats.Length > 0, "layout: KD_Treat_* found (" + treats.Length + ")");
            foreach (var x in treats) spots.Add(new KeyValuePair<string, Vector3>(x.name, x.position));
            var route = UnityEngine.Object.FindObjectsOfType<KH_WalkRoute>(true);
            req(route.Length == 1 && route[0].nodes != null && route[0].nodes.Length > 0 && route[0].nodes[0] != null, "layout: the walk route's first node (the course start) found");
            if (route.Length == 1 && route[0].nodes != null && route[0].nodes.Length > 0 && route[0].nodes[0] != null) spots.Add(new KeyValuePair<string, Vector3>("course start (route node 0)", route[0].nodes[0].position));
            foreach (var s in spots)
            {
                string nearest; float d = MinFootprintDistance(st, prints, s.Value, out nearest);
                req(d >= LayoutClearance, "layout: " + s.Key + " " + d.ToString("F2") + " m from the " + nearest + " (>= " + LayoutClearance + ")");
            }
            {
                string nearest; float d = RingDistance(st, prints, out nearest);
                req(d >= LayoutClearance, "layout: the dogs' waiting ring x " + DogWaitRing.xMin + ".." + DogWaitRing.xMax + " z " + DogWaitRing.yMin + ".." + DogWaitRing.yMax + " " + d.ToString("F2") + " m from the " + nearest + " (>= " + LayoutClearance + ")");
            }
            // the standing room (duckboard + 1 m each side) is recorded, not judged: people pass there
            var room = new[] { new Footprint("standing room", new Vector2(0f, -TableFront), new Vector2(TableSize.x + 2f * SideMargin, TableSize.z)) };
            foreach (var s in spots) { string n; r.notes.Add("layout: " + s.Key + " " + MinFootprintDistance(st, room, s.Value, out n).ToString("F2") + " m from the standing room around the duckboard"); }

            // ---- the ground under the working area: plane fit and the steepest step between neighbours ----
            SlopeNote(r, st);
            r.notes.Add("layout: " + StationName + " " + st.position.ToString("F2") + " yaw " + st.eulerAngles.y.ToString("F1") + "; duckboard centre " + (table != null ? table.position.ToString("F2") : "-")
                + "; terrain at the origin " + StationGround(st, st.position).ToString("F3") + " (station y " + st.position.y.ToString("F3") + ")");
        }

        /// <summary>What every wash station has (the main one and those along the course, IMP-ENV-008): yaw only, the stand, the
        /// tools' homes and return settings, the reset button wired to this station's tools, the duckboard, the side room, the tub
        /// and the sign. label prefixes every line.</summary>
        static void CheckStationBody(VerifyReport r, Action<bool, string> req, Transform st, ToolSpec[] specs, string label)
        {
            var e = st.eulerAngles;
            req(Mathf.Abs(Mathf.DeltaAngle(e.x, 0f)) < 0.01f && Mathf.Abs(Mathf.DeltaAngle(e.z, 0f)) < 0.01f && (st.localScale - Vector3.one).sqrMagnitude < 1e-8f,
                label + " yaw only, scale 1 (euler " + e.ToString("F2") + ", scale " + st.localScale.ToString("F3") + ")");

            // ---- the stand ----
            var top = st.Find("Shelf");
            req(top != null && Near(top.localPosition, new Vector3(0f, ShelfHeight - ShelfTopThickness * 0.5f, 0f)) && Near(top.localScale, new Vector3(ShelfWidth, ShelfTopThickness, ShelfDepth)),
                label + ": Shelf is the stand's top (" + ShelfWidth + " x " + ShelfTopThickness + " x " + ShelfDepth + ", top at " + ShelfHeight + " m)");
            int legs = Enumerable.Range(0, 4).Count(i => { var l = st.Find("ShelfLeg_" + i); return l != null && l.GetComponent<MeshRenderer>() != null; });
            req(legs == 4, label + ": 4 legs ShelfLeg_0..3 (" + legs + ")");

            // ---- tools: home under Homes, on the top, the tool resting there, the return settings ----
            var homes = st.Find(HomesName);
            req(homes != null && homes.parent == st, label + "/" + HomesName + " (the tools' homes move with the station)");
            foreach (var spec in specs)
            {
                string who = label + ": " + spec.name;
                var t = st.Find(spec.name);
                var tool = t != null ? t.GetComponent<KH_WashTool>() : null;
                if (tool == null) { req(false, who + " KH_WashTool (see the tool checks above)"); continue; }
                var home = homes != null ? homes.Find(HomeName(spec)) : null;
                req(home != null && tool.home == home, who + ".home = " + HomesName + "/" + HomeName(spec) + " ("+ (tool.home != null ? KD_SceneObjects.PathOf(tool.home.gameObject) : "null") + ")");
                if (home != null)
                {
                    req(home.GetComponents<Component>().Length == 1, who + " home is a bare Transform (no collider / renderer)");
                    req(Near(home.localPosition, spec.slot) && Quaternion.Angle(home.localRotation, Quaternion.Euler(spec.euler)) < 0.1f, who + " home local " + home.localPosition.ToString("F3") + " = slot " + spec.slot.ToString("F3"));
                    var hl = st.InverseTransformPoint(home.position);
                    req(Mathf.Abs(hl.x) <= ShelfWidth * 0.5f && Mathf.Abs(hl.z) <= ShelfDepth * 0.5f && hl.y >= ShelfHeight - 1e-3f && hl.y <= ShelfHeight + 0.2f,
                        who + " home on the shelf top (station local " + hl.ToString("F3") + ")");
                    req((t.position - home.position).magnitude < 1e-3f && Quaternion.Angle(t.rotation, home.rotation) < 0.1f, who + " rests at its home in the saved scene (" + (t.position - home.position).magnitude.ToString("F4") + " m)");
                }
                req(Mathf.Abs(tool.returnSeconds - ReturnSeconds) < 1e-4f, who + " returnSeconds " + tool.returnSeconds + " = " + ReturnSeconds);
                req(Mathf.Abs(tool.homeTolerance - HomeTolerance) < 1e-4f, who + " homeTolerance " + tool.homeTolerance + " = " + HomeTolerance);
                req(tool.debugHeldOverride == 0, who + " debugHeldOverride 0 (harness only)");
                var tb = UdonSharpEditorUtility.GetBackingUdonBehaviour(tool);
                Backing(req, tb, who, "home", tool.home);
                Backing(req, tb, who, "returnSeconds", tool.returnSeconds);
                Backing(req, tb, who, "homeTolerance", tool.homeTolerance);
                Backing(req, tb, who, "debugHeldOverride", 0);
            }

            // ---- the reset button: Interact board wired to every tool of this station, in the builder's order ----
            var rb = st.Find(ResetName);
            var reset = rb != null ? rb.GetComponent<KH_WashStationReset>() : null;
            req(reset != null, label + "/" + ResetName + " with KH_WashStationReset");
            if (reset != null)
            {
                var want = specs.Select(s => { var t = st.Find(s.name); return t != null ? t.GetComponent<KH_WashTool>() : null; }).ToArray();
                req(want.All(x => x != null) && reset.tools != null && reset.tools.Length == want.Length && reset.tools.Zip(want, (a, b) => a == b).All(x => x),
                    label + ": reset.tools = the " + want.Length + " tools of " + st.name + " (" + (reset.tools != null ? reset.tools.Length : 0) + ")");
                var col = rb.GetComponent<Collider>();
                req(col != null && col.enabled && rb.gameObject.activeInHierarchy && rb.gameObject.layer == 0, label + ": " + ResetName + " has a collider for Interact, active, Default layer");
                var rbk = UdonSharpEditorUtility.GetBackingUdonBehaviour(reset);
                req(rbk != null && rbk.SyncMethod == Networking.SyncType.None, label + ": KH_WashStationReset backing sync None (local only; the tools' VRCObjectSync carries the result)");
                req(rbk != null && !string.IsNullOrEmpty(rbk.interactText) && rbk.interactText != "Use", label + ": " + ResetName + " interaction text '" + (rbk != null ? rbk.interactText : "") + "'");
                if (rbk != null && rbk.interactText == ResetInteractText) r.notes.Add(label + ": the reset button still reads the ASCII placeholder '" + ResetInteractText + "' (Japanese in the Inspector)");
                Backing(req, rbk, label + ": KH_WashStationReset", "tools", reset.tools);
                var rt = st.Find(ResetTextName);
                var rtm = rt != null ? rt.GetComponent<TextMeshPro>() : null;
                req(rtm != null && !string.IsNullOrEmpty(rtm.text), label + ": " + ResetTextName + " label (" + (rtm != null ? "'" + rtm.text + "'" : "none") + ")");
                var fnt = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
                if (rtm != null && fnt != null && !string.IsNullOrEmpty(rtm.text))
                {
                    List<char> miss; bool ok = rtm.font == fnt && fnt.HasCharacters(rtm.text, out miss);
                    req(ok, label + ": the reset label uses the shared font and every character is in it");
                }
            }

            // ---- the duckboard: one solid walkable box, 0.6 x 0.03 x 0.9, seated on the ground, 1.2 m in front ----
            var table = st.Find(TableName);
            var box = table != null ? table.GetComponent<BoxCollider>() : null;
            req(box != null && box.enabled && !box.isTrigger && table.gameObject.layer == 0 && table.gameObject.activeInHierarchy && table.GetComponents<Collider>().Length == 1,
                label + ": " + TableName + " one solid BoxCollider on Default (players walk on it; the dogs' ground rays 0|11 see it)");
            if (box != null)
            {
                var size = Vector3.Scale(box.size, table.lossyScale);
                req(Near(size, TableSize) && Near(box.center, new Vector3(0f, TableSize.y * 0.5f, 0f)), label + ": " + TableName + " collider " + size.ToString("F3") + " = " + TableSize.ToString("F3"));
                req(Mathf.Abs(table.localPosition.x) < 1e-3f && Mathf.Abs(table.localPosition.z + TableFront) < 1e-3f && Quaternion.Angle(table.localRotation, Quaternion.identity) < 0.1f,
                    label + ": " + TableName + " " + TableFront + " m in front of the shelf (local " + table.localPosition.ToString("F3") + ")");
                float want = MaxGround(st, new Vector3(0f, 0f, -TableFront), TableSize.x, TableSize.z);
                req(Mathf.Abs(table.localPosition.y - want) < 0.005f, label + ": " + TableName + " seated on the ground (local y " + table.localPosition.y.ToString("F3") + ", highest ground under it " + want.ToString("F3") + "; top " + TableSize.y + " m above it)");
                var c = table.TransformPoint(box.center);
                bool onTop = Physics.Raycast(c + Vector3.up * 1f, Vector3.down, out RaycastHit hit, 2f, (1 << 0) | (1 << 11), QueryTriggerInteraction.Ignore) && hit.collider == box;
                req(onTop, label + ": a downward ray (0|11) above the duckboard lands on it first (" + (hit.collider != null ? hit.collider.name : "nothing") + ")");
                int slats = table.Cast<Transform>().Count(x => x.name.StartsWith("Slat_") && x.GetComponent<Collider>() == null);
                req(slats == TableSlats, label + ": " + TableSlats + " slats without colliders (" + slats + ")");
            }

            // ---- the side room: 1 m on each side (±X) of the duckboard free of solid colliders ----
            foreach (float side in new[] { -1f, 1f })
            {
                // The zone is the 1 m strip beside the duckboard (x from its edge 0.3 to 1.3 m, z over its 0.9 m). It starts SideGap
                // off the board's edge: the strip and the board share that face, and OverlapBox reports a collider that only touches
                // the box (2026-09-30 Build: both sides failed on WashTable itself). The station's own parts (shelf, legs, tub, sign,
                // the board) are NOT excluded: none of them may reach into the room people stand in, so they count like anything else.
                const float SideGap = 0.02f;
                var local = new Vector3(side * (TableSize.x * 0.5f + SideGap + (SideMargin - SideGap) * 0.5f), 0f, -TableFront);
                float zoneW = SideMargin - SideGap;
                // The box's bottom comes from the terrain alone (its lowest point under the zone + 2 cm), not from any solid top, and the
                // terrain itself is left out of the hits: a rock or a stand in the zone is always an obstacle (Codex 2026-09-30 7-1).
                float bottom = MinTerrain(st, local, zoneW, TableSize.z) + 0.02f;
                float h = MaxGround(st, local, zoneW, TableSize.z) - bottom + 1.8f;   // up to head height above the highest terrain
                var centre = st.TransformPoint(new Vector3(local.x, bottom + h * 0.5f, local.z));
                var hits = Physics.OverlapBox(centre, new Vector3(zoneW * 0.5f, h * 0.5f, TableSize.z * 0.5f), st.rotation, ~0, QueryTriggerInteraction.Ignore)
                    .Where(x => !(x is TerrainCollider) && x.attachedRigidbody == null).Select(x => KD_SceneObjects.PathOf(x.gameObject)).ToArray();
                req(hits.Length == 0, label + ": " + (side < 0 ? "west" : "east") + " side of the duckboard: " + zoneW.ToString("F2") + " m (from " + SideGap + " m off its edge) x " + TableSize.z
                    + " m free of solid colliders, the station's own parts included (" + string.Join(", ", hits) + ")");
            }

            // ---- the tub and the sign ----
            var tub = st.Find(TubName);
            req(tub != null && tub.GetComponent<Collider>() != null && !tub.GetComponent<Collider>().isTrigger && tub.GetComponent<MeshRenderer>() != null, label + ": " + TubName + " with a solid collider");
            var sign = st.Find(SignName);
            req(sign != null && sign.GetComponent<Collider>() == null && sign.GetComponent<MeshRenderer>() != null, label + ": " + SignName + " board without a collider (does not block a pickup ray)");
            var tt = st.Find(SignTextName);
            var tmp = tt != null ? tt.GetComponent<TextMeshPro>() : null;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            req(tmp != null && font != null && tmp.font == font && !string.IsNullOrEmpty(tmp.text), label + ": " + SignTextName + " TextMeshPro with " + KD_ShopSetupEditor.SharedFontPath + " and a text (" + (tmp != null ? "'" + tmp.text + "'" : "none") + ")");
            if (tmp != null && font != null && !string.IsNullOrEmpty(tmp.text))
            {
                List<char> missing;
                bool all = font.HasCharacters(tmp.text, out missing);
                req(all, label + ": every sign character is in the shared font (missing: " + (missing != null ? string.Join(" ", missing.Select(ch => "U+" + ((int)ch).ToString("X4"))) : "") + "; KD_NotoSerif is Static: add glyphs with its AddGlyphs entry)");
                if (tmp.text == SignText) r.notes.Add(label + ": the sign still reads the ASCII placeholder '" + SignText + "' (the Japanese is typed in the Inspector, then Wash/12 checks its glyphs)");
                var lp = st.InverseTransformPoint(tt.position);
                req(lp.z < SignZ - SignSize.z * 0.5f && Quaternion.Angle(tt.localRotation, Quaternion.identity) < 0.1f, label + ": the sign text is on the board's front face and faces the front (-Z)");
            }

        }

        // ================================================================ IMP-ENV-008: the stations along the course

        static void CheckCourseStations(VerifyReport r, Action<bool, string> req, UnityEngine.SceneManagement.Scene scene)
        {
            Physics.SyncTransforms();
            var roots = scene.GetRootGameObjects().Where(g => g.name == CourseRootName).ToArray();
            req(roots.Length == 1, "course: " + CourseRootName + " once at the scene root (" + roots.Length + ")");
            if (roots.Length != 1) return;
            var root = roots[0].transform;
            req(root.position.sqrMagnitude < 1e-6f && Quaternion.Angle(root.rotation, Quaternion.identity) < 0.01f && (root.localScale - Vector3.one).sqrMagnitude < 1e-8f,
                "course: " + CourseRootName + " at the origin, no rotation, scale 1 (the stations are placed in world space; move a station, not the root)");
            foreach (Transform t in root) req(CourseStations.Any(s => s.key == t.name), "course: " + CourseRootName + "/" + t.name + " is in the station table (" + string.Join(", ", CourseStations.Select(s => s.key)) + ")");

            var stations = AllStationTransforms(scene);
            string missing;
            var cc = ResolveCourse(scene, out missing);
            req(cc != null, "course: the course context resolved (" + (cc != null ? "route " + cc.path.Length + " nodes" : missing) + ")");
            if (cc != null && cc.notes != "") r.notes.Add("course: " + cc.notes);
            if (cc != null) r.notes.Add("course: hand-placed spots: " + string.Join(", ", cc.hands.Select(h => h.name)) + "; dirt zones: " + string.Join(", ", cc.dirtZones.Select(z => z.name)));

            var dogs = UnityEngine.Object.FindObjectsOfType<KH_DogWash>(true).OrderBy(w => w.dogIndex).ToArray();
            var mainGo = scene.GetRootGameObjects().FirstOrDefault(g => g.name == StationName);
            var mainT = mainGo != null ? mainGo.transform.Find(ToolSpecs[0].name) : null;
            var mainTool = mainT != null ? mainT.GetComponent<KH_WashTool>() : null;
            var refWashes = mainTool != null ? mainTool.washes : null;
            int colliderCount = dogs.Sum(w => w.partColliders != null ? w.partColliders.Count(x => x != null) : 0);

            var courseTools = new List<KH_WashTool>();
            foreach (var spec in CourseStations)
            {
                string label = "course: " + CourseRootName + "/" + spec.key + " (" + spec.label + ")";
                var st = root.Find(spec.key);
                req(st != null, label + " exists (when missing, the Wash/11 log in " + BuildReportPath + " has every candidate's reasons)");
                if (st == null) continue;
                CheckStationBody(r, req, st, spec.tools, label);
                courseTools.AddRange(CheckCourseTools(req, st, spec.tools, label, dogs, refWashes, colliderCount));
                if (cc != null) foreach (var kv in PlacementChecks(cc, spec, st.position, st.rotation, st, stations)) req(kv.Key, label + " placement: " + kv.Value);
                SlopeNote(r, st);
                var table = st.Find(TableName);
                r.notes.Add(label + " at " + st.position.ToString("F2") + " yaw " + st.eulerAngles.y.ToString("F1") + "; duckboard centre " + (table != null ? table.position.ToString("F2") : "-")
                    + (cc != null && cc.hasRiverNode && spec.place == PlaceRiver ? "; 川辺 node " + FlatDist(st.position, cc.riverNode).ToString("F2") + " m" : "")
                    + (cc != null && cc.hasUpperNode && spec.place == PlaceUpper ? "; 奥広場 node " + FlatDist(st.position, cc.upperNode).ToString("F2") + " m" : "")
                    + (cc != null && cc.bench != null && spec.place == PlaceHillside ? "; bench " + FlatDist(st.position, cc.bench.position).ToString("F2") + " m" : ""));
            }
            // one set of tools per station, all of the same kind (no copy of the dogs or of the wash logic per station)
            var allTools = AllStationTools(scene);
            int sceneTools = UnityEngine.Object.FindObjectsOfType<KH_WashTool>(true).Length;
            req(allTools.Distinct().Count() == allTools.Count && allTools.Select(t => t.name).Distinct().Count() == allTools.Count, "course: every station's tool has its own name (" + allTools.Count + ")");
            req(sceneTools == allTools.Count, "course: every KH_WashTool in the scene belongs to a station of the table (" + sceneTools + " / " + allTools.Count + ")");

            // ---- the dogs: every station is a no-dirt place, every tool counts as held (HoldsTool) ----
            var want = stations.ToArray();
            foreach (var w in dogs)
            {
                req(w.washStations != null && w.washStations.Length == want.Length && w.washStations.Zip(want, (a, b) => a == b).All(x => x),
                    w.name + ".washStations = the " + want.Length + " stations, main first (" + (w.washStations != null ? string.Join(", ", w.washStations.Select(x => x != null ? x.name : "null")) : "null") + ")");
                req(w.tools != null && courseTools.All(x => w.tools.Contains(x)), w.name + ".tools include the " + courseTools.Count + " tools along the course");
                req(w.tools != null && w.tools.Length == AllToolCount() && allTools.All(x => w.tools.Contains(x)), w.name + ".tools = every station's tool (" + (w.tools != null ? w.tools.Length : 0) + " / " + AllToolCount() + ")");
                var wb = UdonSharpEditorUtility.GetBackingUdonBehaviour(w);
                Backing(req, wb, w.name, "washStations", w.washStations);
                Backing(req, wb, w.name, "tools", w.tools);
            }
            r.notes.Add("course: " + courseTools.Count + " tools along the course: +" + courseTools.Count + " VRCObjectSync (tool roots) and +" + courseTools.Count
                + " Manual KH_WashToolState (State children) = +" + (2 * courseTools.Count) + " network objects; the " + CourseStations.Length + " reset boards are sync None");
        }

        static List<KH_WashTool> CheckCourseTools(Action<bool, string> req, Transform st, ToolSpec[] specs, string label, KH_DogWash[] dogs, KH_DogWash[] refWashes, int colliderCount)
        {
            var list = new List<KH_WashTool>();
            foreach (var spec in specs)
            {
                string who = label + ": " + spec.name;
                var t = st.Find(spec.name);
                if (t == null) { req(false, who + " exists"); continue; }
                var tool = t.GetComponent<KH_WashTool>();
                var pickup = t.GetComponent<VRCPickup>();
                var rb = t.GetComponent<Rigidbody>();
                var cols = t.GetComponents<Collider>();
                req(t.gameObject.layer == PickupLayer, who + " layer Pickup (" + t.gameObject.layer + ")");
                req(pickup != null && pickup.AutoHold == VRC_Pickup.AutoHoldMode.Yes && pickup.pickupable, who + " VRCPickup (AutoHold Yes)");
                req(t.GetComponent<VRCObjectSync>() != null, who + " VRCObjectSync");
                req(rb != null && rb.isKinematic && !rb.useGravity, who + " Rigidbody kinematic, no gravity");
                req(cols.Length == 1 && !cols[0].isTrigger, who + " one solid collider for the pickup (" + cols.Length + ")");
                req(tool != null, who + " KH_WashTool");
                if (tool == null) continue;
                list.Add(tool);
                var tb = UdonSharpEditorUtility.GetBackingUdonBehaviour(tool);
                req(tb != null && tb.SyncMethod == Networking.SyncType.None, who + " KH_WashTool backing sync None");
                req(tool.tool == spec.tool, who + " tool id " + tool.tool + " = " + spec.tool);
                req(tool.nozzle != null && tool.nozzle.IsChildOf(t) && tool.nozzle != t, who + " nozzle child");
                req(tool.washes != null && tool.washes.Length == dogs.Length && dogs.All(d => tool.washes.Contains(d)),
                    who + " washes = every KH_DogWash in the scene (" + (tool.washes != null ? tool.washes.Length : 0) + " / " + dogs.Length + ")");
                if (refWashes != null) req(tool.washes != null && tool.washes.SequenceEqual(refWashes), who + " washes = the main " + ToolSpecs[0].name + ".washes (the same dogs in the same order)");
                req(tool.washLayer.value == 1 << WashLayer, who + " washLayer = layer " + WashLayer + " only (" + tool.washLayer.value + ")");
                var stateT = t.Find("State");
                var state = stateT != null ? stateT.GetComponent<KH_WashToolState>() : null;
                req(state != null && tool.state == state && state.washTool == tool, who + " State child: KH_WashToolState wired both ways");
                if (state != null)
                {
                    var sb = UdonSharpEditorUtility.GetBackingUdonBehaviour(state);
                    req(sb != null && sb.SyncMethod == Networking.SyncType.Manual, who + " State backing sync Manual");
                    Backing(req, sb, who + " State", "washTool", tool);
                }
                if (spec.tool == KH_DogWash.ToolShower)
                {
                    var ps = tool.spray;
                    req(ps != null && ps.transform.IsChildOf(tool.nozzle) && ps.trigger.enabled && ps.trigger.colliderCount == colliderCount,
                        who + " spray under the nozzle, its trigger module on the " + colliderCount + " part colliders (" + (ps != null ? ps.trigger.colliderCount : 0) + ")");
                    var a = tool.sprayAudio;
                    req(a != null && a.clip != null && a.clip.name == ClipSpray && a.loop && !a.playOnAwake && a.spatialBlend >= 0.99f, who + " sprayAudio: " + ClipSpray + ", loop, 3D, no playOnAwake");
                }
                else req(tool.spray == null && tool.sprayAudio == null, who + " no spray / sprayAudio (not a shower)");
                Backing(req, tb, who, "tool", tool.tool);
                Backing(req, tb, who, "nozzle", tool.nozzle);
                Backing(req, tb, who, "washes", tool.washes);
                Backing(req, tb, who, "washLayer", tool.washLayer);
                Backing(req, tb, who, "spray", tool.spray);
                Backing(req, tb, who, "sprayAudio", tool.sprayAudio);
                Backing(req, tb, who, "state", tool.state);
            }
            return list;
        }

        static bool Near(Vector3 a, Vector3 b) { return (a - b).sqrMagnitude < 1e-6f; }

        /// <summary>The lowest terrain (station local y) on a 5 x 5 grid over a rectangle around a station-local point.</summary>
        static float MinTerrain(Transform st, Vector3 local, float sizeX, float sizeZ)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 5; j++)
                {
                    var p = local + new Vector3((i / 4f - 0.5f) * sizeX, 0f, (j / 4f - 0.5f) * sizeZ);
                    best = Mathf.Min(best, StationGround(st, st.TransformPoint(p)) - st.position.y);
                }
            return best;
        }

        static float MinFootprintDistance(Transform st, Footprint[] prints, Vector3 world, out string nearest)
        {
            var l = st.InverseTransformPoint(world);
            float best = float.PositiveInfinity; nearest = "";
            foreach (var f in prints)
            {
                float dx = Mathf.Max(Mathf.Abs(l.x - f.centre.x) - f.size.x * 0.5f, 0f), dz = Mathf.Max(Mathf.Abs(l.z - f.centre.y) - f.size.y * 0.5f, 0f);
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d < best) { best = d; nearest = f.name; }
            }
            return best;
        }

        /// <summary>Convex polygons that do not cross: the distance is reached at a corner of one of them.</summary>
        static float RingDistance(Transform st, Footprint[] prints, out string nearest)
        {
            float best = float.PositiveInfinity; nearest = "";
            var ringCorners = new[] { new Vector2(DogWaitRing.xMin, DogWaitRing.yMin), new Vector2(DogWaitRing.xMax, DogWaitRing.yMin), new Vector2(DogWaitRing.xMin, DogWaitRing.yMax), new Vector2(DogWaitRing.xMax, DogWaitRing.yMax) };
            foreach (var f in prints)
            {
                float d = float.PositiveInfinity;
                foreach (var c in ringCorners) { string n; d = Mathf.Min(d, MinFootprintDistance(st, new[] { f }, new Vector3(c.x, st.position.y, c.y), out n)); }
                for (int i = 0; i < 4; i++)
                {
                    var lc = new Vector3(f.centre.x + ((i & 1) == 0 ? -0.5f : 0.5f) * f.size.x, 0f, f.centre.y + ((i & 2) == 0 ? -0.5f : 0.5f) * f.size.y);
                    var w = st.TransformPoint(lc);
                    float dx = Mathf.Max(DogWaitRing.xMin - w.x, 0f, w.x - DogWaitRing.xMax), dz = Mathf.Max(DogWaitRing.yMin - w.z, 0f, w.z - DogWaitRing.yMax);
                    d = Mathf.Min(d, Mathf.Sqrt(dx * dx + dz * dz));
                }
                if (d < best) { best = d; nearest = f.name; }
            }
            return best;
        }

        /// <summary>The ground (as players stand on it) on a grid over the shelf + duckboard + side room: the least-squares plane's
        /// slope, and the steepest step between grid neighbours. Over 5° is reported (WARN), not failed (the user decides).</summary>
        static void SlopeNote(VerifyReport r, Transform st)
        {
            float plane, steep, hmin, hmax;
            SlopeMeasure(st.position, st.rotation, out plane, out steep, out hmin, out hmax);
            float x0 = -(TableSize.x * 0.5f + SideMargin), x1 = -x0, z0 = -TableFront - TableSize.z * 0.5f, z1 = ShelfDepth * 0.5f;
            bool warn = !(plane <= SlopeWarnDegrees) || steep > SlopeWarnDegrees;
            r.notes.Add((warn ? "WARN " : "") + "layout: ground over " + st.name + " (" + (x1 - x0).ToString("F1") + " x " + (z1 - z0).ToString("F2") + " m, " + (SlopeNx * SlopeNz) + " points): plane slope "
                + plane.ToString("F1") + " deg, steepest neighbour step " + steep.ToString("F1") + " deg, height " + hmin.ToString("F3") + ".." + hmax.ToString("F3") + " (report over " + SlopeWarnDegrees + " deg to the user)");
        }

        const int SlopeNx = 9, SlopeNz = 7;

        /// <summary>The terrain on a 9 x 7 grid over the shelf + duckboard + side room of a station pose (no Transform needed, so the
        /// Build can test a candidate): the least-squares plane's slope, the steepest step between grid neighbours (degrees), and the
        /// lowest / highest terrain (world y). Throws when a point has no terrain under it.</summary>
        static void SlopeMeasure(Vector3 pos, Quaternion rot, out float plane, out float steep, out float hmin, out float hmax)
        {
            float x0 = -(TableSize.x * 0.5f + SideMargin), x1 = -x0, z0 = -TableFront - TableSize.z * 0.5f, z1 = ShelfDepth * 0.5f;
            int nx = SlopeNx, nz = SlopeNz;
            var h = new float[nx, nz]; var p = new Vector3[nx, nz];
            double sxx = 0, sxz = 0, szz = 0, sx = 0, sz = 0, sh = 0, sxh = 0, szh = 0; int n = 0;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    var w = pos + rot * new Vector3(Mathf.Lerp(x0, x1, i / (nx - 1f)), 0f, Mathf.Lerp(z0, z1, j / (nz - 1f)));
                    float y = StationGround(null, w);
                    h[i, j] = y; p[i, j] = w;
                    double ux = w.x - pos.x, uz = w.z - pos.z, uy = y - pos.y;   // centred on the station (better conditioned far from the origin)
                    sxx += ux * ux; sxz += ux * uz; szz += uz * uz; sx += ux; sz += uz; sh += uy; sxh += ux * uy; szh += uz * uy; n++;
                }
            // normal equations for y = a x + b z + c
            double[,] m = { { sxx, sxz, sx }, { sxz, szz, sz }, { sx, sz, n } };
            double[] v = { sxh, szh, sh };
            double det = Det3(m);
            plane = float.NaN;
            if (Math.Abs(det) > 1e-9)
            {
                double a = Det3(Replace(m, 0, v)) / det, b = Det3(Replace(m, 1, v)) / det;
                plane = (float)(Math.Atan(Math.Sqrt(a * a + b * b)) * 180.0 / Math.PI);
            }
            steep = 0f; hmin = float.PositiveInfinity; hmax = float.NegativeInfinity;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    hmin = Mathf.Min(hmin, h[i, j]); hmax = Mathf.Max(hmax, h[i, j]);
                    if (i + 1 < nx) steep = Mathf.Max(steep, StepDeg(p[i, j], h[i, j], p[i + 1, j], h[i + 1, j]));
                    if (j + 1 < nz) steep = Mathf.Max(steep, StepDeg(p[i, j], h[i, j], p[i, j + 1], h[i, j + 1]));
                }
        }

        static float StepDeg(Vector3 a, float ha, Vector3 b, float hb)
        {
            float d = new Vector2(a.x - b.x, a.z - b.z).magnitude;
            return d > 1e-4f ? Mathf.Atan(Mathf.Abs(ha - hb) / d) * Mathf.Rad2Deg : 0f;
        }

        static double Det3(double[,] m)
        {
            return m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
        }

        static double[,] Replace(double[,] m, int col, double[] v)
        {
            var c = (double[,])m.Clone();
            for (int i = 0; i < 3; i++) c[i, col] = v[i];
            return c;
        }
    }
}
