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
            try { CheckStationControlsAll(r, req, scene); }
            catch (Exception e) { req(false, "controls: check stopped: " + e.GetType().Name + ": " + e.Message); }
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
            var table = FindTable(st);

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

            // ---- the duckboard: one solid walkable box, TableSize (1.3 x 0.03 x 0.7 since IMP-ENV-011), seated on the ground, 1.2 m in front ----
            var table = FindTable(st);
            var box = table != null ? table.GetComponent<BoxCollider>() : null;
            req(box != null && box.enabled && !box.isTrigger && table.gameObject.layer == 0 && table.gameObject.activeInHierarchy && table.GetComponents<Collider>().Length == 1,
                label + ": " + TableName + " one solid BoxCollider on Default (players walk on it; the dogs' ground rays 0|11 see it)");
            if (box != null)
            {
                var size = Vector3.Scale(box.size, table.lossyScale);
                req(Near(size, TableSize) && Near(box.center, new Vector3(0f, TableSize.y * 0.5f, 0f)), label + ": " + TableName + " collider " + size.ToString("F3") + " = " + TableSize.ToString("F3"));
                req(Mathf.Abs(table.localPosition.x) < 1e-3f && Mathf.Abs(table.localPosition.z + TableFront) < 1e-3f && Quaternion.Angle(table.localRotation, Quaternion.identity) < 0.1f,
                    label + ": " + TableName + " " + TableFront + " m in front of the shelf (local " + table.localPosition.ToString("F3") + ")");
                float gap = (TableFront - TableSize.z * 0.5f) - ShelfDepth * 0.5f;
                req(gap >= 0.5f - 1e-4f, label + ": " + gap.ToString("F3") + " m between the duckboard and the shelf (>= 0.5: people stand on the shelf side too)");
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
                // The zone is the 1 m strip beside the duckboard (x from its edge to 1 m beyond, z over its depth). It starts SideGap
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
                var table = FindTable(st);
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

        // ================================================================ IMP-ENV-011 / IMP-DOG-035: Call, my floor, the board, washCalls

        static void CheckStationControlsAll(VerifyReport r, Action<bool, string> req, UnityEngine.SceneManagement.Scene scene)
        {
            var stations = AllStationTransforms(scene);
            req(stations.Count == 1 + CourseStations.Length, "controls: " + (1 + CourseStations.Length) + " wash stations present (" + stations.Count + ")");
            KD_DogWanderController[] wanders; KH_DogWash[] washes;
            DogsByIndex(out wanders, out washes);
            foreach (var st in stations) CheckStationControls(r, req, st, StationIndexOf(st), "controls: " + st.name, wanders, washes);

            // ---- every dog knows every station's Call (main first) ----
            var calls = AllStationCalls(scene).ToArray();
            for (int i = 0; i < wanders.Length; i++)
            {
                var w = wanders[i];
                if (w == null) { req(false, "controls: KD_DogWanderController dogIndex " + i + " found"); continue; }
                var p = new SerializedObject(w).FindProperty("washCalls");
                req(p != null && p.isArray, "controls: " + w.name + ".washCalls is serialized (the dog side's field; compile first)");
                if (p == null || !p.isArray) continue;
                var got = Enumerable.Range(0, p.arraySize).Select(k => p.GetArrayElementAtIndex(k).objectReferenceValue).ToArray();
                req(got.Length == calls.Length && got.Zip(calls, (a, b) => a == (UnityEngine.Object)b).All(x => x),
                    "controls: " + w.name + ".washCalls = the " + calls.Length + " stations' Call, main first (" + got.Length + ")");
                Backing(req, UdonSharpEditorUtility.GetBackingUdonBehaviour(w), "controls: " + w.name, "washCalls", calls);
                // the dogs never stand on someone's personal floor
                var gm = new SerializedObject(w).FindProperty("groundLayerMask");
                req(gm != null && (gm.intValue & (1 << FloorLayer)) == 0, "controls: " + w.name + ".groundLayerMask leaves out the floor layer " + FloorLayer + " (" + (gm != null ? gm.intValue.ToString() : "none") + ")");
            }
            CheckFloorLayer(r, req, scene);
        }

        /// <summary>Codex review 8 / coordinator 2026-10-01: my floor's user layer 22. The interact ray passes it, the players stand
        /// on it, nothing else in the world sees it, and nothing else uses it.</summary>
        static void CheckFloorLayer(VerifyReport r, Action<bool, string> req, UnityEngine.SceneManagement.Scene scene)
        {
            Func<int, bool> has = m => (m & (1 << FloorLayer)) != 0;
            // 1) the interact ray passes through it
            var descs = UnityEngine.Object.FindObjectsOfType<VRCSceneDescriptor>(true);
            var pt = descs.Length == 1 ? new SerializedObject(descs[0]).FindProperty("interactThruLayers") : null;
            req(pt != null && (pt.intValue & FloorPassBit) != 0, "floor layer: VRCSceneDescriptor.interactThruLayers has layer " + FloorLayer + " (bit " + (FloorLayer - FirstUserLayer) + "; "
                + (pt != null ? pt.intValue.ToString() : descs.Length + " descriptors") + ")");
            // 2) the players stand on it
            req(!Physics.GetIgnoreLayerCollision(FloorLayer, 10) && !Physics.GetIgnoreLayerCollision(FloorLayer, 9), "floor layer: " + FloorLayer + " collides with PlayerLocal (10) and Player (9)");
            // 3) nothing else sees it: the dogs' ground rays, the frisbee, the spray, the wash rays
            foreach (var w in UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true))
            {
                var p = new SerializedObject(w).FindProperty("groundLayerMask");
                req(p != null && !has(p.intValue), "floor layer: " + w.name + " (wander).groundLayerMask leaves out " + FloorLayer);
            }
            foreach (var f in UnityEngine.Object.FindObjectsOfType<KH_FrisbeeCatch>(true))
            {
                var p = new SerializedObject(f).FindProperty("groundLayerMask");
                req(p != null && !has(p.intValue), "floor layer: " + f.name + " (KH_FrisbeeCatch).groundLayerMask leaves out " + FloorLayer);
            }
            foreach (var f in UnityEngine.Object.FindObjectsOfType<KH_Frisbee>(true))
            {
                var p = new SerializedObject(f).FindProperty("obstacleMask");
                req(p != null && !has(p.intValue), "floor layer: " + f.name + " (KH_Frisbee).obstacleMask leaves out " + FloorLayer);
            }
            foreach (var t in UnityEngine.Object.FindObjectsOfType<KH_WashTool>(true))
            {
                req(!has(t.washLayer.value), "floor layer: " + t.name + ".washLayer leaves out " + FloorLayer);
                if (t.spray != null) req(!has(t.spray.collision.collidesWith.value), "floor layer: " + t.name + " spray collision leaves out " + FloorLayer);
            }
            // other LayerMask fields that include it (Everything etc.): recorded, not judged
            var others = new List<string>();
            foreach (var mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (mb == null || mb is KD_DogWanderController || mb is KH_FrisbeeCatch || mb is KH_Frisbee || mb is KH_WashTool) continue;
                var it = new SerializedObject(mb).GetIterator();
                while (it.NextVisible(true))
                    if (it.propertyType == SerializedPropertyType.LayerMask && has(it.intValue)) others.Add(mb.GetType().Name + "." + it.propertyPath + " on " + mb.name);
            }
            if (others.Count > 0) r.notes.Add("floor layer: other LayerMask fields that include layer " + FloorLayer + " (they would see my floor): " + string.Join(", ", others.Distinct()));
            // 4) nothing else uses it
            var floors = AllStationTransforms(scene).Select(st => st.Find(FloorName + "/" + FloorBoxesName)).Where(x => x != null).ToArray();
            var strays = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Where(x => x.gameObject.layer == FloorLayer && !floors.Any(f => x == f || x.IsChildOf(f))).Select(x => KD_SceneObjects.PathOf(x.gameObject)).ToArray();
            req(strays.Length == 0, "floor layer: only the personal floors use layer " + FloorLayer + " (" + string.Join(", ", strays.Take(10)) + (strays.Length > 10 ? ", ..." : "") + ")");
        }

        static void CheckStationControls(VerifyReport r, Action<bool, string> req, Transform st, int index, string label, KD_DogWanderController[] wanders, KH_DogWash[] washes)
        {
            // ---- Lift (liftRoot) and Anchor ----
            var lift = st.Find(LiftName);
            req(lift != null && lift.localPosition.sqrMagnitude < 1e-8f && Quaternion.Angle(lift.localRotation, Quaternion.identity) < 0.01f && (lift.localScale - Vector3.one).sqrMagnitude < 1e-8f,
                label + "/" + LiftName + " at the station's origin, no rotation, scale 1 (the stand KH_WashStationCall raises)");
            var table = FindTable(st);
            req(st.Find(TableName) == null, label + ": no " + TableName + " left directly under the station");
            req(table != null && lift != null && table.parent == lift, label + ": " + LiftName + "/" + TableName + " (the duckboard rides the lift)");
            var cols = table != null ? table.GetComponentsInChildren<Collider>(true) : new Collider[0];
            req(cols.Length > 0 && lift != null && cols.All(c => c.transform.IsChildOf(lift)), label + ": the duckboard's collider is under " + LiftName + " (" + cols.Length + ")");
            var anchor = lift != null ? lift.Find(AnchorName) : null;
            req(anchor != null && anchor.GetComponents<Component>().Length == 1, label + "/" + LiftName + "/" + AnchorName + " a bare Transform");
            if (anchor != null && table != null)
                req(Near(anchor.localPosition, new Vector3(0f, table.localPosition.y + TableSize.y, -TableFront)), label + ": anchor at the duckboard's top centre (" + anchor.localPosition.ToString("F3") + ")");

            // ---- Call ----
            var callT = st.Find(CallName);
            var call = callT != null ? callT.GetComponent<KH_WashStationCall>() : null;
            req(call != null, label + "/" + CallName + " with KH_WashStationCall");
            if (call != null)
            {
                var cb = UdonSharpEditorUtility.GetBackingUdonBehaviour(call);
                req(cb != null && cb.SyncMethod == Networking.SyncType.Manual, label + ": Call backing sync Manual");
                var so = new SerializedObject(call);
                Func<string, SerializedProperty> P = f => { var p = so.FindProperty(f); req(p != null, label + ": Call." + f + " serialized"); return p; };
                var pi = P("stationIndex"); if (pi != null) { req(pi.intValue == index, label + ": Call.stationIndex " + pi.intValue + " = " + index); Backing(req, cb, label + ": Call", "stationIndex", index); }
                var pa = P("anchor"); if (pa != null) { req(anchor != null && pa.objectReferenceValue == anchor, label + ": Call.anchor = " + LiftName + "/" + AnchorName); Backing(req, cb, label + ": Call", "anchor", anchor); }
                var pl = P("liftRoot"); if (pl != null) { req(lift != null && pl.objectReferenceValue == lift, label + ": Call.liftRoot = " + LiftName); Backing(req, cb, label + ": Call", "liftRoot", lift); }
                float yaw = Mathf.Repeat(st.eulerAngles.y + StandYawOffset, 360f);
                var py = P("standYaw"); if (py != null) { req(Mathf.Abs(Mathf.DeltaAngle(py.floatValue, yaw)) < 0.05f, label + ": Call.standYaw " + py.floatValue.ToString("F1") + " = station yaw + " + StandYawOffset + " (parallel to the shelf; rerun 11 after turning the station)"); Backing(req, cb, label + ": Call", "standYaw", py.floatValue); }
                foreach (var kv in new[] { new KeyValuePair<string, UnityEngine.Object[]>("dogs", wanders), new KeyValuePair<string, UnityEngine.Object[]>("washes", washes) })
                {
                    var p = P(kv.Key);
                    if (p == null) continue;
                    var got = Enumerable.Range(0, p.isArray ? p.arraySize : 0).Select(k => p.GetArrayElementAtIndex(k).objectReferenceValue).ToArray();
                    req(got.Length == kv.Value.Length && kv.Value.All(x => x != null) && got.Zip(kv.Value, (a, b) => a == b).All(x => x), label + ": Call." + kv.Key + " = the " + kv.Value.Length + " dogs by dogIndex (" + got.Length + ")");
                    Backing(req, cb, label + ": Call", kv.Key, kv.Value);
                }
                foreach (var kv in new[] { new KeyValuePair<string, float>("liftMax", CallLiftMax), new KeyValuePair<string, float>("liftStep", CallLiftStep), new KeyValuePair<string, float>("idleReleaseSeconds", CallIdleReleaseSeconds), new KeyValuePair<string, float>("callerFarMeters", CallCallerFarMeters) })
                {
                    var p = P(kv.Key);
                    if (p == null) continue;
                    req(Mathf.Abs(p.floatValue - kv.Value) < 1e-4f, label + ": Call." + kv.Key + " " + p.floatValue + " = " + kv.Value);
                    Backing(req, cb, label + ": Call", kv.Key, kv.Value);
                }
            }

            // ---- my floor ----
            var pfT = st.Find(FloorName);
            var pf = pfT != null ? pfT.GetComponent<KH_WashPersonalFloor>() : null;
            req(pf != null, label + "/" + FloorName + " with KH_WashPersonalFloor");
            if (pf != null)
            {
                var pb = UdonSharpEditorUtility.GetBackingUdonBehaviour(pf);
                req(pb != null && pb.SyncMethod == Networking.SyncType.None, label + ": PersonalFloor backing sync None (mine only)");
                req(Near(pfT.localPosition, new Vector3(0f, 0f, -TableFront)) && Quaternion.Angle(pfT.localRotation, Quaternion.identity) < 0.01f, label + ": PersonalFloor centred on the duckboard (x z)");
                var boxes = pf.floor;
                req(boxes != null && boxes.parent == pfT && boxes.gameObject.layer == FloorLayer, label + ": PersonalFloor.floor = " + FloorName + "/" + FloorBoxesName + " on layer " + FloorLayer);
                req(table != null && Mathf.Abs(pf.baseY - table.localPosition.y) < 1e-4f && boxes != null && Mathf.Abs(boxes.localPosition.y - pf.baseY) < 1e-4f, label + ": floor base = the ground under the duckboard (" + pf.baseY.ToString("F3") + "), resting at height 0");
                var fc = pf.floorColliders ?? new Collider[0];
                req(fc.Length == 4 && fc.All(c => c is BoxCollider && !c.isTrigger && !c.enabled && c.gameObject.layer == FloorLayer && boxes != null && c.transform.parent == boxes && c.GetComponent<Renderer>() == null),
                    label + ": 4 solid floor boxes, disabled in the saved scene, invisible, layer " + FloorLayer + " (" + fc.Length + ")");
                float area = fc.OfType<BoxCollider>().Sum(c => c.size.x * c.size.z);
                float want = 4f * FloorHalf * FloorHalf - (FloorHoleMax.x - FloorHoleMin.x) * (FloorHoleMax.y - FloorHoleMin.y);
                req(Mathf.Abs(area - want) < 1e-3f, label + ": floor area " + area.ToString("F2") + " m2 = 5 x 5 minus the hole over the duckboard and the shelf (" + want.ToString("F2") + ")");
                // the hole covers the duckboard, the shelf and the board (no floor collider over them at any height)
                var hole = Rect.MinMaxRect(FloorHoleMin.x, FloorHoleMin.y, FloorHoleMax.x, FloorHoleMax.y);
                foreach (var nm in new[] { "Shelf", BoardName })
                {
                    var o = st.Find(nm); if (o == null) continue;
                    var l = pfT.InverseTransformPoint(o.position);
                    req(hole.Contains(new Vector2(l.x, l.z)), label + ": " + nm + " inside the floor's hole (" + l.x.ToString("F2") + ", " + l.z.ToString("F2") + ")");
                }
                req(Mathf.Abs(pf.maxHeight - FloorMaxHeight) < 1e-4f && Mathf.Abs(pf.step - FloorStep) < 1e-4f && Mathf.Abs(pf.speed - FloorSpeed) < 1e-4f && Mathf.Abs(pf.leaveMeters - FloorLeaveMeters) < 1e-4f,
                    label + ": floor 0.." + FloorMaxHeight + " m, step " + FloorStep + ", " + FloorSpeed + " m/s, back to 0 beyond " + FloorLeaveMeters + " m");
                foreach (var f in new[] { "floor", "floorColliders", "baseY", "maxHeight", "step", "speed", "leaveMeters" })
                {
                    object v = f == "floor" ? (object)pf.floor : f == "floorColliders" ? (object)pf.floorColliders : f == "baseY" ? (object)pf.baseY : f == "maxHeight" ? (object)pf.maxHeight : f == "step" ? (object)pf.step : f == "speed" ? (object)pf.speed : (object)pf.leaveMeters;
                    Backing(req, pb, label + ": PersonalFloor", f, v);
                }
            }

            // ---- the board ----
            var bt = st.Find(BoardName);
            var board = bt != null ? bt.GetComponent<KH_WashStationBoard>() : null;
            req(board != null, label + "/" + BoardName + " with KH_WashStationBoard");
            if (board == null) return;
            var bb = UdonSharpEditorUtility.GetBackingUdonBehaviour(board);
            req(bb != null && bb.SyncMethod == Networking.SyncType.None, label + ": board backing sync None");
            var canvas = bt.GetComponent<Canvas>();
            var box = bt.GetComponent<BoxCollider>();
            req(canvas != null && canvas.renderMode == RenderMode.WorldSpace && bt.gameObject.layer == 0 && bt.GetComponent<VRCUiShape>() != null && bt.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null
                && box != null && box.isTrigger, label + ": board = World Space Canvas on Default + GraphicRaycaster + VRCUiShape + trigger BoxCollider");
            req(Near(bt.localPosition, BoardLocal) && Quaternion.Angle(bt.localRotation, Quaternion.identity) < 0.01f, label + ": board at the back of the shelf top, facing the front (" + bt.localPosition.ToString("F3") + ")");
            req(board.call == call && board.personalFloor == pf && board.statusText != null && board.statusText.transform.IsChildOf(bt), label + ": board.call / personalFloor / statusText wired");
            req(board.dogNames != null && board.dogNames.SequenceEqual(DogNamesJa), label + ": board.dogNames = " + string.Join(" / ", DogNamesJa));
            req(board.wordPick == WordPick && board.wordCalling == WordCalling && board.wordHere == WordHere && board.wordBusy == WordBusy && board.wordLeashed == WordLeashed && board.wordStand == WordStand && board.wordMe == WordMe,
                label + ": board words = the builder's (" + WordPick + " / " + WordCalling + " / " + WordHere + " / " + WordBusy + " / " + WordLeashed + ")");
            Backing(req, bb, label + ": board", "call", board.call);
            Backing(req, bb, label + ": board", "personalFloor", board.personalFloor);
            Backing(req, bb, label + ": board", "statusText", board.statusText);
            Backing(req, bb, label + ": board", "dogNames", board.dogNames);
            var specs = BoardButtons();
            var buttons = bt.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            req(buttons.Length == specs.Length, label + ": " + specs.Length + " buttons (6 dogs, end, stand up/down, me up/down) (" + buttons.Length + ")");
            foreach (var s in specs)
            {
                var b = bt.Find(s.name);
                var btn = b != null ? b.GetComponent<UnityEngine.UI.Button>() : null;
                if (btn == null) { req(false, label + ": button " + s.name); continue; }
                var calls = new SerializedObject(btn).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                bool ok = btn.onClick.GetPersistentEventCount() == 1 && btn.onClick.GetPersistentTarget(0) == bb && btn.onClick.GetPersistentMethodName(0) == "SendCustomEvent"
                    && calls != null && calls.arraySize == 1 && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == s.evt;
                req(ok, label + ": button " + s.name + " -> board " + s.evt + " (one listener)");
                var lab = b.GetComponentInChildren<TextMeshProUGUI>(true);
                req(lab != null && lab.text == s.label, label + ": button " + s.name + " reads '" + s.label + "' (" + (lab != null ? lab.text : "none") + ")");
            }

            // ---- glyphs: every text on the board, and the words the board writes at run time ----
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            var texts = bt.GetComponentsInChildren<TMP_Text>(true);
            req(font != null && texts.All(x => x.font == font), label + ": every board text uses " + KD_ShopSetupEditor.SharedFontPath);
            if (font != null)
            {
                string all = string.Concat(texts.Select(x => x.text)) + string.Concat(DogNamesJa) + WordPick + WordCalling + WordHere + WordBusy + WordLeashed + WordStand + WordMe + "0123456789.m ";
                var need = new string(all.Where(ch => !char.IsControl(ch)).Distinct().ToArray());
                List<char> missing;
                bool okAll = font.HasCharacters(need, out missing);
                req(okAll, label + ": every board character is in the shared font (missing: " + (missing != null && missing.Count > 0 ? string.Join(" ", missing.Distinct().Select(ch => ch + " U+" + ((int)ch).ToString("X4"))) : "none")
                    + "; KD_NotoSerif is Static: add them with its AddGlyphs entry)");
            }
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
