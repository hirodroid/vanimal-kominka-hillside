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
            CheckHandWashAll(r, req, scene);   // IMP-DOG-039: bottles, the dogs' washCalls, KH_HandWash, the picture guide
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
                    + " (now " + st.position.ToString("F3") + "; moved by hand after the Build? run Wash/11 again to seat the duckboard on the new ground)");
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
        /// tools' homes and return settings, the reset button wired to this station's tools, the duckboard, the side room, no tub
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
                var premium = st.GetComponentInChildren<KH_WashSelection>(true);
                if (premium != null && premium.slots != null) want = want.Concat(premium.slots).ToArray();
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
            req(box != null && box.enabled && !box.isTrigger && table.gameObject.layer == StandLayer && table.gameObject.activeInHierarchy && table.GetComponents<Collider>().Length == 1,
                label + ": " + TableName + " one solid BoxCollider on layer " + StandLayer + " (the players pass through it; the dogs' ground rays 0|11 do not see it: a held dog's root is written by the dog side, a released one lands on the terrain)");
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
                bool onTop = Physics.Raycast(c + Vector3.up * 1f, Vector3.down, out RaycastHit hit, 2f, 1 << StandLayer, QueryTriggerInteraction.Ignore) && hit.collider == box;
                req(onTop, label + ": a downward ray (layer " + StandLayer + ") above the duckboard lands on it first (" + (hit.collider != null ? hit.collider.name : "nothing") + ")");
                int slats = table.Cast<Transform>().Count(x => x.name.StartsWith("Slat_") && x.GetComponent<Collider>() == null);
                req(slats == TableSlats, label + ": " + TableSlats + " slats without colliders (" + slats + ")");
            }

            // ---- the side room: 1 m on each side (±X) of the duckboard free of solid colliders ----
            foreach (float side in new[] { -1f, 1f })
            {
                // The zone is the 1 m strip beside the duckboard (x from its edge to 1 m beyond, z over its depth). It starts SideGap
                // off the board's edge: the strip and the board share that face, and OverlapBox reports a collider that only touches
                // the box (2026-09-30 Build: both sides failed on WashTable itself). The station's own parts (shelf, legs, sign,
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
                    .Where(x => !(x is TerrainCollider) && x.attachedRigidbody == null && HitsPlayers(x)).Select(x => KD_SceneObjects.PathOf(x.gameObject)).ToArray();
                req(hits.Length == 0, label + ": " + (side < 0 ? "west" : "east") + " side of the duckboard: " + zoneW.ToString("F2") + " m (from " + SideGap + " m off its edge) x " + TableSize.z
                    + " m free of colliders the players bump (the station's own parts too; layer " + StandLayer + " is passed through) (" + string.Join(", ", hits) + ")");
            }

            // ---- no tub (user's decision 2026-10-01), the sign; the players pass through the stand and the shelf ----
            req(st.Find(TubName) == null, label + ": no " + TubName + " (removed)");
            {
                var solid = st.GetComponentsInChildren<Collider>(true).Where(x => !x.isTrigger && x.attachedRigidbody == null && x.transform.name != ResetName
                    && !(st.Find(FloorName) != null && x.transform.IsChildOf(st.Find(FloorName)))).ToArray();
                var bump = solid.Where(x => x.gameObject.layer != StandLayer || HitsPlayers(x)).Select(x => KD_SceneObjects.PathOf(x.gameObject)).ToArray();
                req(solid.Length >= 6 && bump.Length == 0 && Physics.GetIgnoreLayerCollision(StandLayer, 10) && Physics.GetIgnoreLayerCollision(StandLayer, 9),
                    label + ": the shelf, its legs and the duckboard (" + solid.Length + " solid colliders) are on layer " + StandLayer + ", which does not collide with Player 9 / PlayerLocal 10 (" + string.Join(", ", bump) + ")");
                var resetTf = st.Find(ResetName);
                req(resetTf != null && resetTf.gameObject.layer == 0, label + ": " + ResetName + " stays on Default (Interact)");
            }
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
                req(tmp.text != SignPlaceholder, label + ": the sign no longer reads the ASCII placeholder '" + SignPlaceholder + "' (Wash/11 writes '" + SignText + "')");
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
            CheckCallRanges(r, req, scene);   // IMP-DOG-037
            CheckBoardText(r, req, scene);    // IMP-DOG-037: words in 13 languages, glyphs, fit, KD_Language

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
                // 2026-10-02: the walk out after the release (Wash/11 WireWanders)
                var wso = new SerializedObject(w);
                var lm = wso.FindProperty("stationLeaveMargin"); var lg = wso.FindProperty("stationLeaveGait");
                req(lm != null && Mathf.Abs(lm.floatValue - LeaveMargin) < 1e-4f, "controls: " + w.name + ".stationLeaveMargin " + (lm != null ? lm.floatValue.ToString() : "missing") + " = " + LeaveMargin);
                req(lg != null && lg.intValue == LeaveGait, "controls: " + w.name + ".stationLeaveGait " + (lg != null ? lg.intValue.ToString() : "missing") + " = " + LeaveGait);
                Backing(req, UdonSharpEditorUtility.GetBackingUdonBehaviour(w), "controls: " + w.name, "stationLeaveMargin", LeaveMargin);
                Backing(req, UdonSharpEditorUtility.GetBackingUdonBehaviour(w), "controls: " + w.name, "stationLeaveGait", LeaveGait);
                var lu = wso.FindProperty("stationUnleashGrace");
                req(lu != null && Mathf.Abs(lu.floatValue - UnleashGrace) < 1e-4f, "controls: " + w.name + ".stationUnleashGrace " + (lu != null ? lu.floatValue.ToString() : "missing") + " = " + UnleashGrace);
                Backing(req, UdonSharpEditorUtility.GetBackingUdonBehaviour(w), "controls: " + w.name, "stationUnleashGrace", UnleashGrace);
                var lr = wso.FindProperty("stationRestMargin");
                req(lr != null && Mathf.Abs(lr.floatValue - RestMargin) < 1e-4f, "controls: " + w.name + ".stationRestMargin " + (lr != null ? lr.floatValue.ToString() : "missing") + " = " + RestMargin);
                Backing(req, UdonSharpEditorUtility.GetBackingUdonBehaviour(w), "controls: " + w.name, "stationRestMargin", RestMargin);
                // the dogs never stand on someone's personal floor
                var gm = new SerializedObject(w).FindProperty("groundLayerMask");
                req(gm != null && (gm.intValue & (1 << FloorLayer)) == 0, "controls: " + w.name + ".groundLayerMask leaves out the floor layer " + FloorLayer + " (" + (gm != null ? gm.intValue.ToString() : "none") + ")");
            }
            CheckFloorLayer(r, req, scene);
        }

        /// <summary>IMP-DOG-037 (plan §7.2): the call ranges between stations. keepOutRadius + 1 m < callRange < the dog side's
        /// StationFar (an accepted call never needs the far-call teleport, so a far dog cannot be pulled in through it), no two
        /// stations' ranges overlap (a dog in range of this station is never held by another one in range), the River range covers
        /// the water-play entry + 3 m (the user's decision: a dog playing in the water nearby can be called). Notes: what lies
        /// inside each range (the hand-placed spots of the placement rules, the course start, the gate's waiting ring).</summary>
        static void CheckCallRanges(VerifyReport r, Action<bool, string> req, UnityEngine.SceneManagement.Scene scene)
        {
            req(CallRanges.Length == 1 + CourseStations.Length, "range: CallRanges has one value per station (" + CallRanges.Length + " / " + (1 + CourseStations.Length) + ")");
            float far = StationFarOfDogs();
            req(!float.IsNaN(far) && far > 0f, "range: KD_DogWanderController.StationFar readable (" + far + " m)");
            var calls = AllStationCalls(scene);
            foreach (var c in calls)
            {
                string n = "range: " + (c.transform.parent != null ? c.transform.parent.name : c.name);
                req(c.callRange > c.keepOutRadius + 1f, n + ": callRange " + c.callRange + " > keepOutRadius " + c.keepOutRadius + " + 1 (a dog just outside the circle can be called)");
                if (!float.IsNaN(far)) req(c.callRange < far, n + ": callRange " + c.callRange + " < StationFar " + far + " (an accepted call never takes the far-call teleport)");
            }
            for (int i = 0; i < calls.Count; i++)
                for (int j = i + 1; j < calls.Count; j++)
                {
                    float d = FlatDist(calls[i]._AnchorPosition(), calls[j]._AnchorPosition());
                    req(d > calls[i].callRange + calls[j].callRange, "range: " + calls[i].transform.parent.name + " / " + calls[j].transform.parent.name + " do not overlap (anchors " + d.ToString("F1") + " m > " + (calls[i].callRange + calls[j].callRange) + ")");
                }
            // the River station covers the water-play entry
            int riverIndex = 1 + Array.FindIndex(CourseStations, s => s.key == "River");
            var river = calls.FirstOrDefault(c => c.stationIndex == riverIndex);
            var spot = UnityEngine.Object.FindObjectsOfType<KH_WaterSpot>(true).FirstOrDefault();
            if (river != null && spot != null && spot.entry != Vector3.zero)
            {
                float de = FlatDist(river._AnchorPosition(), spot.entry);
                req(de + 3f <= river.callRange, "range: River covers the water-play entry (" + de.ToString("F1") + " m from the anchor + 3 <= " + river.callRange + ")");
            }
            else req(false, "range: the River station and the water spot's entry found (" + (river != null) + " / " + (spot != null) + ")");
            // notes: what is inside each range (for the user's review of the numbers)
            string missing;
            var cc = ResolveCourse(scene, out missing);
            var marks = new List<KeyValuePair<string, Vector3>>();
            if (cc != null)
            {
                foreach (var h in cc.hands) marks.Add(new KeyValuePair<string, Vector3>(h.name, h.pos));
                if (cc.path != null && cc.path.Length > 0) marks.Add(new KeyValuePair<string, Vector3>("course start (route node 0)", cc.path[0]));
            }
            marks.Add(new KeyValuePair<string, Vector3>("the gate's waiting ring", new Vector3(DogWaitRing.center.x, 0f, DogWaitRing.center.y)));
            foreach (var c in calls)
            {
                var a = c._AnchorPosition();
                var near = marks.Select(m => new KeyValuePair<string, float>(m.Key, FlatDist(a, m.Value))).Where(m => m.Value <= c.callRange + 15f).OrderBy(m => m.Value)
                    .Select(m => m.Key + " " + m.Value.ToString("F1") + " m" + (m.Value <= c.callRange ? "" : " (outside)"));
                r.notes.Add("range: " + c.transform.parent.name + " callRange " + c.callRange + " m from the anchor " + a.ToString("F1") + ": " + string.Join(", ", near));
            }
        }

        /// <summary>The dog side's StationFar (a private const of KD_DogWanderController.HillsideWash.cs), NaN if not found.</summary>
        static float StationFarOfDogs()
        {
            var f = typeof(KD_DogWanderController).GetField("StationFar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (f == null || !f.IsLiteral) return float.NaN;
            var v = f.GetRawConstantValue();
            return v is float x ? x : float.NaN;
        }

        /// <summary>IMP-DOG-037: the board's words. Every key in the catalog for all 13 languages (no language left untranslated:
        /// the public build guard refuses those) with the same placeholders; KD_Language's tables hold them (proxy and backing);
        /// the ja / en characters in the shared font; and each text fits its label at the autosize floor (MinPt). The fit uses the
        /// shared font's advances for ja / en (FAIL when over), an estimate for the 11 languages VRChat draws with its own font
        /// (1 em for CJK / Hangul, 0.62 em otherwise; a WARN note when over: the client font's widths are unknown here).
        /// The boards are built from one layout, so the fit is measured once; the rule line per station (its number).</summary>
        static void CheckBoardText(VerifyReport r, Action<bool, string> req, UnityEngine.SceneManagement.Scene scene)
        {
            KD_LocalizationEditor.Catalog c;
            try { c = BoardCatalog(); } catch (Exception e) { req(false, "board text: the catalog loads (" + e.Message + ")"); return; }
            var keys = BoardWordKeys.Concat(BoardDogNameKeys).ToArray();
            var gaps = new List<string>();
            foreach (var lang in c.languages)
                foreach (var k in keys)
                {
                    var e = Array.Find(lang.texts, t => t.key == k);
                    if (e == null || string.IsNullOrWhiteSpace(e.text)) gaps.Add(lang.code + ":" + k);
                }
            req(gaps.Count == 0, "board text: the " + keys.Length + " keys have a text in all " + c.languages.Length + " languages (missing: " + string.Join(", ", gaps.Take(20)) + (gaps.Count > 20 ? ", ..." : "") + ")");

            // KD_Language holds them (Wash/11 runs Configure)
            var pouches = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true);
            var language = pouches.Length == 1 ? pouches[0].language : null;
            if (language == null) req(false, "board text: KD_TreatPouch.language found");
            else
            {
                var lacking = keys.Where(k => language.keys == null || !language.keys.Contains(k)).ToArray();
                req(lacking.Length == 0 && language.texts != null && language.languageCodes != null && language.texts.Length == language.keys.Length * language.languageCodes.Length,
                    "board text: KD_Language.keys hold the board's keys (lacking: " + string.Join(", ", lacking) + "; run Wash/11, which runs Configure)");
                object v; var lb = UdonSharpEditorUtility.GetBackingUdonBehaviour(language);
                bool backed = lb != null && lb.publicVariables.TryGetVariableValue("keys", out v) && v is string[] bk && keys.All(k => bk.Contains(k));
                req(backed, "board text: KD_Language backing keys hold the board's keys");
            }

            // glyphs (ja / en: baked into the shared font)
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            if (font == null) { req(false, "board text: the shared font " + KD_ShopSetupEditor.SharedFontPath); return; }
            var lackGlyph = new HashSet<char>();
            foreach (var ch in BoardGlyphText(c)) if (KD_LocalizationEditor.Drawn(ch) && !KD_LocalizationEditor.HasGlyph(font, ch)) lackGlyph.Add(ch);
            req(lackGlyph.Count == 0, "board text: every ja / en board character is in the shared font (missing: " + string.Join(" ", lackGlyph.Select(ch => ch + " U+" + ((int)ch).ToString("X4"))) + "; Wash/11 adds them through AddGlyphs)");
            var texts = AllStationTransforms(scene).Select(st => st.Find(BoardName)).Where(x => x != null).SelectMany(x => x.GetComponentsInChildren<TMP_Text>(true)).ToArray();
            req(texts.Length > 0 && texts.All(x => x.font == font), "board text: every board text uses " + KD_ShopSetupEditor.SharedFontPath + " (" + texts.Length + ")");

            // fit at the autosize floor, per language
            Func<string, string, string> T = (code, key) => BoardText(c, code, key);
            float nameW = DogNameSize.x, stateW = DogStateSize.x, statusW = StatusSize.x, ruleW = RuleSize.x, heightsW = HeightsSize.x;
            var specs = BoardButtons();
            var fails = new List<string>(); var warns = new List<string>(); float worst = 0f; string worstWhat = "";
            foreach (var lang in c.languages)
            {
                bool baked = KD_LocalizationEditor.Bundled(lang);
                var names = BoardDogNameKeys.Select(k => T(lang.code, k)).ToArray();
                string longName = names.OrderByDescending(n => BoardEstWidth(font, n, MinPt)).First();
                var checks = new List<KeyValuePair<string, KeyValuePair<string, float>>>();   // what, (text, width)
                foreach (var n in names) checks.Add(BoardFit("dog name", n, nameW));
                for (int i = KH_WashStationBoard.WStCallable; i <= KH_WashStationBoard.WStWashedOther; i++) checks.Add(BoardFit(BoardWordKeys[i], T(lang.code, BoardWordKeys[i]), stateW));
                foreach (var s in specs) if (s.kind != 0) checks.Add(BoardFit(s.key, T(lang.code, s.key), s.size.x - 24f));
                checks.Add(BoardFit("pick", T(lang.code, BoardWordKeys[KH_WashStationBoard.WPick]), statusW));
                checks.Add(BoardFit("calling", T(lang.code, BoardWordKeys[KH_WashStationBoard.WCalling]).Replace("{0}", longName), statusW));
                checks.Add(BoardFit("here", T(lang.code, BoardWordKeys[KH_WashStationBoard.WHere]).Replace("{0}", longName), statusW));
                string longReason = Enumerable.Range(KH_WashStationBoard.WStLeashedOther, KH_WashStationBoard.WStWashedOther - KH_WashStationBoard.WStLeashedOther + 1)
                    .Select(i => T(lang.code, BoardWordKeys[i])).Concat(new[] { T(lang.code, BoardWordKeys[KH_WashStationBoard.WBusy]) })
                    .OrderByDescending(x => BoardEstWidth(font, x, MinPt)).First();
                checks.Add(BoardFit("refusal", T(lang.code, BoardWordKeys[KH_WashStationBoard.WNameReason]).Replace("{0}", longName).Replace("{1}", longReason), statusW));
                checks.Add(BoardFit("rule", T(lang.code, BoardWordKeys[KH_WashStationBoard.WRule]).Replace("{0}", Mathf.RoundToInt(CallRanges.Max()).ToString()), ruleW));
                checks.Add(BoardFit("heights", T(lang.code, BoardWordKeys[KH_WashStationBoard.WHeights]).Replace("{0}", "0.9").Replace("{1}", "1.2"), heightsW));
                foreach (var kv in checks)
                {
                    float w = BoardEstWidth(font, kv.Value.Key, MinPt), room = kv.Value.Value - 4f;
                    if (w / room > worst) { worst = w / room; worstWhat = lang.code + " " + kv.Key + " '" + kv.Value.Key + "' " + w.ToString("F0") + " / " + room.ToString("F0") + " px"; }
                    if (w <= room) continue;
                    (baked ? fails : warns).Add(lang.code + " " + kv.Key + " '" + kv.Value.Key + "' " + w.ToString("F0") + " > " + room.ToString("F0") + " px");
                }
            }
            req(fails.Count == 0, "board text: ja / en fit their labels at " + MinPt + " pt (over: " + string.Join("; ", fails) + ")");
            if (warns.Count > 0) r.notes.Add("WARN board text: estimated over the label at " + MinPt + " pt in the client-font languages (check on a device): " + string.Join("; ", warns));
            r.notes.Add("board text: the widest text at " + MinPt + " pt is " + worstWhat + " (" + (worst * 100f).ToString("F0") + "% of its label)");

            // ---- IMP-DOG-039: the picture guide's words (the same keys / KD_Language / glyph checks above cover them: they are in
            // BoardWordKeys and BoardGlyphText). Here: the arrow glyph and the fit of each text in its Guide label (Guide.cs layout),
            // both device variants of steps ② ③ and the help line (IMP-UI-003: all 14 words, each in the box it is shown in: the
            // labels' slots of GuideLabelWords for VR and for Desktop). ja / en FAIL, the client-font languages WARN (same method) ----
            req(KD_LocalizationEditor.HasGlyph(font, GuideArrowText[0]), "guide text: the arrow '" + GuideArrowText + "' U+" + ((int)GuideArrowText[0]).ToString("X4") + " is in the shared font (Wash/11 adds it through BoardGlyphText)");
            Func<int, float> slotWidth = slot => slot == 2 * GuideSteps ? GuideHelpSize.x : slot % 2 == 0 ? GuideStepSize.x : GuideSubSize.x;
            var guideFit = new[] { true, false }.SelectMany(vr => GuideLabelWords(vr).Select((word, slot) => new KeyValuePair<int, float>(word, slotWidth(slot))))
                .GroupBy(kv => kv.Key).Select(gr => gr.First()).ToArray();
            req(guideFit.Length == 14, "guide text: the fit covers the 14 guide words of both devices (" + guideFit.Length + ")");
            var gFails = new List<string>(); var gWarns = new List<string>(); float gWorst = 0f; string gWorstWhat = "";
            foreach (var lang in c.languages)
            {
                bool baked = KD_LocalizationEditor.Bundled(lang);
                foreach (var kv in guideFit)
                {
                    string key = BoardWordKeys[kv.Key], text = T(lang.code, key);
                    float w = BoardEstWidth(font, text, MinPt), room = kv.Value - 4f;
                    if (w / room > gWorst) { gWorst = w / room; gWorstWhat = lang.code + " " + key + " '" + text + "' " + w.ToString("F0") + " / " + room.ToString("F0") + " px"; }
                    if (w <= room) continue;
                    (baked ? gFails : gWarns).Add(lang.code + " " + key + " '" + text + "' " + w.ToString("F0") + " > " + room.ToString("F0") + " px");
                }
            }
            req(gFails.Count == 0, "guide text: ja / en fit the guide's labels at " + MinPt + " pt (steps " + GuideStepSize.x + " px, subs " + GuideSubSize.x + " px, help " + GuideHelpSize.x + " px; over: " + string.Join("; ", gFails) + ")");
            if (gWarns.Count > 0) r.notes.Add("WARN guide text: estimated over the label at " + MinPt + " pt in the client-font languages (check on a device): " + string.Join("; ", gWarns));
            r.notes.Add("guide text: the widest text at " + MinPt + " pt is " + gWorstWhat + " (" + (gWorst * 100f).ToString("F0") + "% of its label)");
        }

        static KeyValuePair<string, KeyValuePair<string, float>> BoardFit(string what, string text, float width)
        {
            return new KeyValuePair<string, KeyValuePair<string, float>>(what, new KeyValuePair<string, float>(text, width));
        }

        /// <summary>A one-line text's width (px) at pt: the shared font's advances for the characters it has, else 1 em for CJK /
        /// Hangul / full-width and 0.62 em for the rest (VRChat's client font draws those; an estimate).</summary>
        static float BoardEstWidth(TMP_FontAsset font, string s, float pt)
        {
            float w = 0f, scale = pt / Mathf.Max(1f, font.faceInfo.pointSize);
            foreach (char ch in s)
            {
                if (!KD_LocalizationEditor.Drawn(ch)) continue;
                TMP_Character tc;
                if (font.characterLookupTable != null && font.characterLookupTable.TryGetValue((uint)ch, out tc) && tc != null && tc.glyph != null)
                    w += tc.glyph.metrics.horizontalAdvance * scale * tc.scale;
                else w += pt * (BoardWide(ch) ? 1.0f : 0.62f);
            }
            return w;
        }

        static bool BoardWide(char ch)
        {
            return (ch >= 0x1100 && ch <= 0x115F) || (ch >= 0x2E80 && ch <= 0xA4CF) || (ch >= 0xAC00 && ch <= 0xD7A3) || (ch >= 0xF900 && ch <= 0xFAFF) || (ch >= 0xFF00 && ch <= 0xFF60);
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
                foreach (var kv in new[] { new KeyValuePair<string, float>("liftMax", CallLiftMax), new KeyValuePair<string, float>("liftStep", CallLiftStep), new KeyValuePair<string, float>("idleReleaseSeconds", CallIdleReleaseSeconds), new KeyValuePair<string, float>("callerFarMeters", CallCallerFarMeters), new KeyValuePair<string, float>("keepOutRadius", CallKeepOutRadius) })
                {
                    var p = P(kv.Key);
                    if (p == null) continue;
                    req(Mathf.Abs(p.floatValue - kv.Value) < 1e-4f, label + ": Call." + kv.Key + " " + p.floatValue + " = " + kv.Value);
                    Backing(req, cb, label + ": Call", kv.Key, kv.Value);
                }
                // IMP-DOG-037: the station's call range (the invariants between stations: CheckCallRanges)
                var pr = P("callRange");
                if (pr != null)
                {
                    float want = CallRangeOf(index);
                    req(Mathf.Abs(pr.floatValue - want) < 1e-4f, label + ": Call.callRange " + pr.floatValue + " = " + want + " m (IMP-DOG-037)");
                    Backing(req, cb, label + ": Call", "callRange", want);
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
                req(fc.Length == 1 && fc.All(c => c is BoxCollider && !c.isTrigger && !c.enabled && c.gameObject.layer == FloorLayer && boxes != null && c.transform.parent == boxes && c.GetComponent<Renderer>() == null),
                    label + ": one solid floor slab, disabled in the saved scene, invisible, layer " + FloorLayer + " (" + fc.Length + ")");
                float area = fc.OfType<BoxCollider>().Sum(c => c.size.x * c.size.z);
                float want = 4f * FloorHalf * FloorHalf - (FloorHoleMax.x - FloorHoleMin.x) * (FloorHoleMax.y - FloorHoleMin.y);
                req(Mathf.Abs(area - want) < 1e-3f && pf.holeMin == pf.holeMax, label + ": floor area " + area.ToString("F2") + " m2 = 5 x 5, no hole (the players pass through the stand and the shelf)");
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
            // ---- IMP-DOG-037: the board's wiring (the words themselves: CheckBoardText) ----
            KD_LocalizationEditor.Catalog catalog = null;
            try { catalog = BoardCatalog(); } catch (Exception e) { req(false, label + ": the localization catalog loads (" + e.Message + ")"); }
            var pouches = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true);
            var lang = pouches.Length == 1 ? pouches[0].language : null;
            req(board.call == call && board.personalFloor == pf, label + ": board.call / personalFloor wired");
            req(lang != null && board.language == lang, label + ": board.language = KD_TreatPouch.language (" + (board.language != null ? board.language.name : "null") + ")");
            Func<TMP_Text, string, bool> under = (x, n) => x != null && x.transform.parent == bt && x.name == n;
            req(under(board.statusText, StatusName) && under(board.ruleText, RuleName) && under(board.heightText, HeightsName),
                label + ": board.statusText / ruleText / heightText = " + BoardName + "/" + StatusName + ", " + RuleName + ", " + HeightsName);
            int nd = BoardDogNameKeys.Length;
            bool dogsOk = board.dogButtons != null && board.dogButtons.Length == nd && board.dogNameLabels != null && board.dogNameLabels.Length == nd && board.dogStateLabels != null && board.dogStateLabels.Length == nd;
            for (int i = 0; dogsOk && i < nd; i++)
            {
                var dt = bt.Find("Dog_" + i);
                dogsOk = dt != null && board.dogButtons[i] == dt.GetComponent<UnityEngine.UI.Image>() && board.dogButtons[i] != null
                    && board.dogNameLabels[i] != null && board.dogNameLabels[i].transform.parent == dt && board.dogNameLabels[i].name == "Label"
                    && board.dogStateLabels[i] != null && board.dogStateLabels[i].transform.parent == dt && board.dogStateLabels[i].name == DogStateName;
            }
            req(dogsOk, label + ": board.dogButtons / dogNameLabels / dogStateLabels = Dog_0..5 (Image, Label, " + DogStateName + ")");
            var fixedNames = new[] { "End", "StandUp", "StandDown", "MeUp", "MeDown" };
            bool fixedOk = board.buttonLabels != null && board.buttonLabels.Length == fixedNames.Length;
            for (int i = 0; fixedOk && i < fixedNames.Length; i++)
                fixedOk = board.buttonLabels[i] != null && board.buttonLabels[i].transform.parent != null && board.buttonLabels[i].transform.parent.name == fixedNames[i] && board.buttonLabels[i].transform.parent.parent == bt;
            req(fixedOk, label + ": board.buttonLabels = " + string.Join(", ", fixedNames) + " (the W" + "End.. order)");
            req(board.wordKeys != null && board.wordKeys.SequenceEqual(BoardWordKeys) && BoardWordKeys.Length == KH_WashStationBoard.WordCount, label + ": board.wordKeys = the " + KH_WashStationBoard.WordCount + " wash.* keys in W* order");
            req(board.dogNameKeys != null && board.dogNameKeys.SequenceEqual(BoardDogNameKeys), label + ": board.dogNameKeys = the hand menu's dog name keys (" + string.Join(", ", BoardDogNameKeys) + ")");
            string[] jaWords = null, jaNames = null;
            if (catalog != null)
            {
                try
                {
                    jaWords = BoardWordKeys.Select(k => BoardText(catalog, catalog.fallbackLanguage, k)).ToArray();
                    jaNames = BoardDogNameKeys.Select(k => BoardText(catalog, catalog.fallbackLanguage, k)).ToArray();
                }
                catch (Exception e) { req(false, label + ": every board key is in the catalog (" + e.Message + ")"); }
            }
            if (jaWords != null)
            {
                req(board.words != null && board.words.SequenceEqual(jaWords), label + ": board.words = the catalog's " + catalog.fallbackLanguage + " (" + (board.words != null ? board.words.Length : 0) + ")");
                req(board.dogNames != null && board.dogNames.SequenceEqual(jaNames), label + ": board.dogNames = " + string.Join(" / ", jaNames));
            }
            Func<Color, Color, bool> same = (a, c) => Mathf.Abs(a.r - c.r) + Mathf.Abs(a.g - c.g) + Mathf.Abs(a.b - c.b) + Mathf.Abs(a.a - c.a) < 1e-3f;
            req(same(board.colorCallable, KD_UiSpriteEditor.ActionPrimary) && same(board.colorHere, KD_UiSpriteEditor.ActionEquip) && same(board.colorUnavailable, KD_UiSpriteEditor.TabOff)
                && same(board.inkReason, KD_UiSpriteEditor.AccentGold) && same(board.inkNameDim, BoardInkDim) && same(board.inkState, BoardInkState),
                label + ": the three looks = ActionPrimary (callable) / ActionEquip (at this station) / TabOff (not now) + the reason in AccentGold");
            foreach (var f in new[] { "call", "personalFloor", "language", "statusText", "ruleText", "heightText", "dogButtons", "dogNameLabels", "dogStateLabels", "buttonLabels", "dogNameKeys", "dogNames", "wordKeys", "words" })
            {
                object v = typeof(KH_WashStationBoard).GetField(f).GetValue(board);
                Backing(req, bb, label + ": board", f, v);
            }
            var specs = BoardButtons();
            var buttons = bt.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            req(buttons.Length == specs.Length, label + ": " + specs.Length + " buttons (6 dogs, end, stand up/down, me up/down) (" + buttons.Length + ")");
            int di = 0;
            foreach (var s in specs)
            {
                var b = bt.Find(s.name);
                var btn = b != null ? b.GetComponent<UnityEngine.UI.Button>() : null;
                if (btn == null) { req(false, label + ": button " + s.name); continue; }
                var calls = new SerializedObject(btn).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                bool ok = btn.onClick.GetPersistentEventCount() == 1 && btn.onClick.GetPersistentTarget(0) == bb && btn.onClick.GetPersistentMethodName(0) == "SendCustomEvent"
                    && calls != null && calls.arraySize == 1 && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == s.evt;
                req(ok, label + ": button " + s.name + " -> board " + s.evt + " (one listener)");
                var labT = b.Find("Label");
                var lab = labT != null ? labT.GetComponent<TextMeshProUGUI>() : null;
                string want = jaWords == null ? null : s.kind == 0 ? jaNames[di] : BoardText(catalog, catalog.fallbackLanguage, s.key);
                if (want != null) req(lab != null && lab.text == want, label + ": button " + s.name + " reads '" + want + "' in the saved scene (" + (lab != null ? lab.text : "none") + ")");
                if (s.kind == 0) di++;
            }

            // ---- layout: every part inside the board, the buttons and the three lines do not overlap ----
            var parts = new List<KeyValuePair<string, Rect>>();
            foreach (var n in new[] { StatusName, RuleName, HeightsName }) { var x = bt.Find(n) as RectTransform; if (x != null) parts.Add(new KeyValuePair<string, Rect>(n, PartRect(x))); }
            foreach (var s in specs) { var x = bt.Find(s.name) as RectTransform; if (x != null) parts.Add(new KeyValuePair<string, Rect>(s.name, PartRect(x))); }
            var inside = Rect.MinMaxRect(-BoardPixels.x * 0.5f, -BoardPixels.y * 0.5f, BoardPixels.x * 0.5f, BoardPixels.y * 0.5f);
            var outside = parts.Where(p => p.Value.xMin < inside.xMin - 0.01f || p.Value.xMax > inside.xMax + 0.01f || p.Value.yMin < inside.yMin - 0.01f || p.Value.yMax > inside.yMax + 0.01f).Select(p => p.Key).ToArray();
            req(parts.Count == specs.Length + 3 && outside.Length == 0, label + ": all " + (specs.Length + 3) + " parts inside the " + BoardPixels.x + " x " + BoardPixels.y + " board (" + parts.Count + " found; outside: " + string.Join(", ", outside) + ")");
            var overlaps = new List<string>();
            for (int i = 0; i < parts.Count; i++) for (int j = i + 1; j < parts.Count; j++) if (parts[i].Value.Overlaps(parts[j].Value)) overlaps.Add(parts[i].Key + "/" + parts[j].Key);
            req(overlaps.Count == 0, label + ": no two parts overlap (" + string.Join(", ", overlaps) + ")");
            for (int i = 0; i < nd; i++)
            {
                var dt = bt.Find("Dog_" + i) as RectTransform;
                var nl = dt != null ? dt.Find("Label") as RectTransform : null; var sl = dt != null ? dt.Find(DogStateName) as RectTransform : null;
                bool ok = dt != null && nl != null && sl != null;
                if (ok)
                {
                    var own = Rect.MinMaxRect(-dt.sizeDelta.x * 0.5f - 0.01f, -dt.sizeDelta.y * 0.5f - 0.01f, dt.sizeDelta.x * 0.5f + 0.01f, dt.sizeDelta.y * 0.5f + 0.01f);
                    Rect a = PartRect(nl), c = PartRect(sl);
                    ok = !a.Overlaps(c) && a.yMin >= c.yMax - 0.01f && own.Contains(a.min) && own.Contains(a.max) && own.Contains(c.min) && own.Contains(c.max);
                }
                req(ok, label + ": Dog_" + i + " name and state lines inside the button, the name above the state");
            }
        }

        // ================================================================ IMP-DOG-039: hand washing (docs/plans/2026-10-03-hand-wash.md §8.2, §13)

        /// <summary>The bottle on every station, the dogs' washCalls, the one KH_HandWash and the picture guide above every board.
        /// What the general tool / station checks already cover for the bottle (it is in ToolSpecs / CourseToolSpecs): home, slot and
        /// rest at home, returnSeconds, debugHeldOverride (proxy + backing), Pickup / ObjectSync / Rigidbody / one solid collider,
        /// State wired both ways, tool id, nozzle child, washes, no spray, the reset board's tools and every dog's tools.</summary>
        static void CheckHandWashAll(VerifyReport r, Action<bool, string> req, UnityEngine.SceneManagement.Scene scene)
        {
            try { CheckBottles(r, req, scene); }
            catch (Exception e) { req(false, "bottle: check stopped: " + e.GetType().Name + ": " + e.Message); }
            try { CheckDogHandWiring(r, req, scene); }
            catch (Exception e) { req(false, "dog hand wash: check stopped: " + e.GetType().Name + ": " + e.Message); }
            try { CheckHandWashObject(r, req, scene); }
            catch (Exception e) { req(false, "hand wash: check stopped: " + e.GetType().Name + ": " + e.Message); }
            try { CheckGuides(r, req, scene); }
            catch (Exception e) { req(false, "guide: check stopped: " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>The builder's tool table of a station (main: ToolSpecs; course: its CourseStations row), null if unknown.</summary>
        static ToolSpec[] SpecsOfStation(Transform st)
        {
            if (st.name == StationName && st.parent == null) return ToolSpecs;
            return CourseStations.Where(s => s.key == st.name).Select(s => s.tools).FirstOrDefault();
        }

        /// <summary>Every public float field of type that pick selects is in the table, and every table entry is a public float field
        /// (a field left out of the table keeps its code default, which never reaches the scene: vrc-udonsharp §4).</summary>
        static void TuningCovers(Action<bool, string> req, Type type, Func<string, bool> pick, (string field, float value)[] table, string tableName)
        {
            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(float) && pick(f.Name)).Select(f => f.Name).ToArray();
            var missing = fields.Where(f => !table.Any(t => t.field == f)).ToArray();
            var unknown = table.Where(t => { var f = type.GetField(t.field); return f == null || f.FieldType != typeof(float); }).Select(t => t.field).ToArray();
            req(fields.Length > 0 && missing.Length == 0 && unknown.Length == 0, tableName + " covers " + type.Name + "'s " + fields.Length + " tuning fields (not in the table: " + string.Join(", ", missing)
                + "; not a public float of " + type.Name + ": " + string.Join(", ", unknown) + ")");
        }

        /// <summary>Proxy and backing of o hold the table's values.</summary>
        static void TuningMatches(Action<bool, string> req, UdonSharp.UdonSharpBehaviour o, (string field, float value)[] table, string who)
        {
            var b = UdonSharpEditorUtility.GetBackingUdonBehaviour(o);
            foreach (var t in table)
            {
                var fi = o.GetType().GetField(t.field);
                float pv = fi != null && fi.FieldType == typeof(float) ? (float)fi.GetValue(o) : float.NaN;
                req(Mathf.Abs(pv - t.value) < 1e-5f, who + "." + t.field + " " + pv + " = table " + t.value + " (run Wash/11 after changing the table)");
                Backing(req, b, who, t.field, t.value);
            }
        }

        /// <summary>A tool's footprint at its HOME pose in station-local space (the enabled mesh renderers and the root's colliders,
        /// mapped from the tool's current pose onto its home): the horizontal x / z rectangle and the lowest / highest y. False when
        /// there is no home or nothing to measure.</summary>
        static bool HomeFootprint(Transform st, KH_WashTool tool, out Rect xz, out float minY, out float maxY)
        {
            xz = new Rect(); minY = maxY = 0f;
            if (tool == null || tool.home == null) return false;
            var toHome = tool.home.localToWorldMatrix * tool.transform.worldToLocalMatrix;   // the tool's current pose -> its home pose
            var stInv = st.worldToLocalMatrix;
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity; bool any = false;
            Action<Bounds, Matrix4x4> add = (b, l2w) =>
            {
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var p = stInv.MultiplyPoint3x4(toHome.MultiplyPoint3x4(l2w.MultiplyPoint3x4(c)));
                    mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); any = true;
                }
            };
            foreach (var rd in tool.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!rd.enabled || !rd.gameObject.activeInHierarchy) continue;
                var mf = rd.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) add(mf.sharedMesh.bounds, rd.transform.localToWorldMatrix);
            }
            foreach (var col in tool.GetComponents<Collider>())
            {
                if (col is BoxCollider bx) add(new Bounds(bx.center, bx.size), tool.transform.localToWorldMatrix);
                else if (col is CapsuleCollider cp)
                {
                    var s = new Vector3(cp.radius * 2f, cp.radius * 2f, cp.radius * 2f);
                    s[cp.direction] = Mathf.Max(cp.height, cp.radius * 2f);
                    add(new Bounds(cp.center, s), tool.transform.localToWorldMatrix);
                }
            }
            if (!any) return false;
            xz = Rect.MinMaxRect(mn.x, mn.z, mx.x, mx.z); minY = mn.y; maxY = mx.y;
            return true;
        }

        /// <summary>The horizontal gap between two rectangles (0 when they touch or overlap).</summary>
        static float RectGap(Rect a, Rect b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax)), dz = Mathf.Max(0f, Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        const float BottleClearance = 0.02f;

        static void CheckBottles(VerifyReport r, Action<bool, string> req, UnityEngine.SceneManagement.Scene scene)
        {
            TuningCovers(req, typeof(KH_WashTool), n => n.StartsWith("bottle"), BottleTuning, "bottle: BottleTuning");
            req(ToolLabels.Length > KH_DogWash.ToolBottle && !string.IsNullOrEmpty(ToolLabels[KH_DogWash.ToolBottle]), "bottle: ToolLabels has the bottle's pickup text (tool id " + KH_DogWash.ToolBottle + ")");
            var stations = AllStationTransforms(scene);
            req(stations.Count == 1 + CourseStations.Length, "bottle: " + (1 + CourseStations.Length) + " wash stations present (" + stations.Count + ")");
            KD_DogWanderController[] wanders; KH_DogWash[] washes;
            DogsByIndex(out wanders, out washes);
            string shampooPath = ToolMatDir + "/" + ShampooMat + ".mat";
            var shampoo = AssetDatabase.LoadAssetAtPath<Material>(shampooPath);
            req(shampoo != null, "bottle: material " + shampooPath);
            var foam = AssetDatabase.LoadAssetAtPath<Material>(FoamMatPath);
            int count = 0;
            foreach (var st in stations)
            {
                string label = "bottle: " + st.name;
                var specs = SpecsOfStation(st);
                if (specs == null) { req(false, label + ": a station of the builder's table"); continue; }
                bool main = st.name == StationName && st.parent == null;
                string wantName = BottlePrefix + (main ? "" : st.name + "_") + "0";
                var bs = specs.Where(s => s.tool == KH_DogWash.ToolBottle).ToArray();
                req(bs.Length == 1 && bs[0].name == wantName, label + ": the tool table has one bottle " + wantName + " (" + string.Join(", ", bs.Select(s => s.name)) + ")");
                var inScene = st.Cast<Transform>().Where(x => x.name.StartsWith(BottlePrefix)).Select(x => x.name).ToArray();
                req(inScene.Length == 1 && inScene[0] == wantName, label + ": one " + BottlePrefix + "* under the station (" + string.Join(", ", inScene) + ")");

                // the station's other tools: no squirt (proxy and backing)
                foreach (var spec in specs.Where(s => s.tool != KH_DogWash.ToolBottle))
                {
                    var ot = st.Find(spec.name); var other = ot != null ? ot.GetComponent<KH_WashTool>() : null;
                    if (other == null) continue;   // reported by the tool checks
                    req(other.squirtParticles == null, label + ": " + spec.name + " squirtParticles null (not a bottle)");
                    Backing(req, UdonSharpEditorUtility.GetBackingUdonBehaviour(other), label + ": " + spec.name, "squirtParticles", null);
                }

                var rb = st.Find(ResetName);
                var reset = rb != null ? rb.GetComponent<KH_WashStationReset>() : null;
                foreach (var spec in bs)
                {
                    string who = label + "/" + spec.name;
                    var t = st.Find(spec.name);
                    var tool = t != null ? t.GetComponent<KH_WashTool>() : null;
                    req(tool != null && tool.tool == KH_DogWash.ToolBottle, who + " KH_WashTool with tool == " + KH_DogWash.ToolBottle + " (ToolBottle)");
                    if (tool == null) continue;
                    count++;
                    var tb = UdonSharpEditorUtility.GetBackingUdonBehaviour(tool);
                    var pickup = t.GetComponent<VRCPickup>();
                    req(pickup != null && pickup.InteractionText == ToolLabels[KH_DogWash.ToolBottle], who + " VRCPickup InteractionText '" + (pickup != null ? pickup.InteractionText : "none") + "' = '" + ToolLabels[KH_DogWash.ToolBottle] + "'");
                    // the pickup collider: one capsule, upright, the body's radius, its foot on the shelf top (the root sits BottleRestY above it)
                    var cols = t.GetComponents<Collider>();
                    var cap = cols.Length == 1 ? cols[0] as CapsuleCollider : null;
                    req(cap != null && !cap.isTrigger && cap.direction == 1 && Mathf.Abs(cap.radius - BottleBodyDiameter * 0.5f) < 1e-4f && Mathf.Abs(cap.center.y - cap.height * 0.5f + BottleRestY) < 1e-3f,
                        who + " one solid upright CapsuleCollider, radius " + (BottleBodyDiameter * 0.5f) + ", foot " + BottleRestY + " below the root (" + (cap != null ? "r " + cap.radius.ToString("F3") + " foot " + (cap.center.y - cap.height * 0.5f).ToString("F3") : cols.Length + " colliders") + ")");
                    // The original primitives retain dimensional contracts; PropFinish supplies the display.
                    bool finished=KH_PropFinishEditor.Ready;
                    req(t.Find("ImportedVisual") == null, who + " has no legacy ImportedVisual (not one of Wash/32's 23 models)");
                    var visual=t.Find("FinishedVisual");
                    req(!finished || (visual!=null && visual.GetComponent<MeshRenderer>()!=null && visual.GetComponent<MeshRenderer>().enabled && visual.GetComponentsInChildren<Collider>(true).Length==0 && visual.gameObject.layer==PickupLayer),who+" prop finish visual without added collider");
                    var partsOk = new List<string>();
                    foreach (var n in new[] { "Body", "Shoulder", "Cap", "Spout" })
                    {
                        var p = t.Find(n); var mr = p != null ? p.GetComponent<MeshRenderer>() : null;
                        if (mr == null || mr.enabled == finished || p.GetComponent<Collider>() != null || p.gameObject.layer != PickupLayer) partsOk.Add(n);
                    }
                    req(partsOk.Count == 0, who + " Body / Shoulder / Cap / Spout: expected visibility, no colliders, layer Pickup (wrong: " + string.Join(", ", partsOk) + ")");
                    var body = t.Find("Body"); var bodyR = body != null ? body.GetComponent<MeshRenderer>() : null;
                    req(bodyR != null && shampoo != null && bodyR.sharedMaterial == shampoo, who + " Body uses " + shampooPath);
                    // the nozzle: the spout's tip, forward = the spout (tilted BottleSpoutTilt toward the bottle's +Z)
                    var nozzle = t.Find("Nozzle");
                    var spout = t.Find("Spout");
                    req(nozzle != null && tool.nozzle == nozzle, who + ".nozzle = " + spec.name + "/Nozzle");
                    if (nozzle != null && spout != null)
                    {
                        Vector3 dir = Quaternion.Euler(BottleSpoutTilt, 0f, 0f) * Vector3.up;
                        Vector3 tip = spout.localPosition + spout.localRotation * Vector3.up * spout.localScale.y;   // the cylinder primitive is 2 units tall
                        float ang = Vector3.Angle(nozzle.localRotation * Vector3.forward, dir);
                        req((nozzle.localPosition - tip).magnitude < 1e-3f && ang < 0.5f, who + " Nozzle at the spout's tip, forward along the spout (" + (nozzle.localPosition - tip).magnitude.ToString("F4") + " m, " + ang.ToString("F2") + " deg)");
                    }
                    else req(false, who + " Nozzle and Spout children");
                    // the squirt particles
                    var sq = nozzle != null ? nozzle.Find(BottleSquirtName) : null;
                    var ps = sq != null ? sq.GetComponent<ParticleSystem>() : null;
                    req(ps != null && tool.squirtParticles == ps, who + ".squirtParticles = Nozzle/" + BottleSquirtName + " (" + (tool.squirtParticles != null ? KD_SceneObjects.PathOf(tool.squirtParticles.gameObject) : "null") + ")");
                    if (ps != null)
                    {
                        var m = ps.main; var pr = ps.GetComponent<ParticleSystemRenderer>();
                        req(m.simulationSpace == ParticleSystemSimulationSpace.World && !m.playOnAwake && !ps.emission.enabled && m.maxParticles > 0,
                            who + " squirt: World, playOnAwake false, emission off (Emit only), maxParticles " + m.maxParticles);
                        req(pr != null && foam != null && pr.sharedMaterial == foam, who + " squirt renderer uses " + FoamMatPath);
                    }
                    Backing(req, tb, who, "squirtParticles", tool.squirtParticles);
                    TuningMatches(req, tool, BottleTuning, who);
                    // reset and the dogs (also inside the general sequence checks; named here so a missing bottle reads as one)
                    req(reset != null && reset.tools != null && reset.tools.Contains(tool), who + " is in " + st.name + "/" + ResetName + ".tools");
                    var lacking = washes.Where(w => w == null || w.tools == null || !w.tools.Contains(tool)).Select(w => w != null ? w.name : "missing dog").ToArray();
                    req(lacking.Length == 0, who + " is in all " + washes.Length + " dogs' KH_DogWash.tools (lacking: " + string.Join(", ", lacking) + ")");

                    // the footprint at the home pose: on the shelf top, inside it, BottleClearance from every other tool of the station
                    Rect bxz; float bMin, bMax;
                    if (!HomeFootprint(st, tool, out bxz, out bMin, out bMax)) { req(false, who + " footprint measurable (home + renderers / collider)"); continue; }
                    req(bMin >= ShelfHeight - 1e-3f && bMin <= ShelfHeight + 0.005f, who + " stands on the shelf top: lowest point " + bMin.ToString("F4") + " m (top " + ShelfHeight + ")");
                    req(bxz.xMin >= -ShelfWidth * 0.5f && bxz.xMax <= ShelfWidth * 0.5f && bxz.yMin >= -ShelfDepth * 0.5f && bxz.yMax <= ShelfDepth * 0.5f,
                        who + " footprint inside the shelf top (x " + bxz.xMin.ToString("F3") + ".." + bxz.xMax.ToString("F3") + ", z " + bxz.yMin.ToString("F3") + ".." + bxz.yMax.ToString("F3") + ")");
                    float nearest = float.PositiveInfinity; string nearestName = "-";
                    foreach (var spec2 in specs.Where(s => s.name != spec.name))
                    {
                        var ot = st.Find(spec2.name); var other = ot != null ? ot.GetComponent<KH_WashTool>() : null;
                        Rect oxz; float oMin, oMax;
                        if (other == null || !HomeFootprint(st, other, out oxz, out oMin, out oMax)) { req(false, who + ": " + spec2.name + " footprint measurable"); continue; }
                        float gap = RectGap(bxz, oxz);
                        if (gap < nearest) { nearest = gap; nearestName = spec2.name; }
                        req(gap >= BottleClearance - 1e-4f, who + " " + (gap * 100f).ToString("F1") + " cm from " + spec2.name + " at their homes (>= " + (BottleClearance * 100f) + " cm; station-local x / z boxes of the renderers and colliders)");
                    }
                    r.bottles.Add(KD_SceneObjects.PathOf(t.gameObject) + " home " + st.InverseTransformPoint(tool.home != null ? tool.home.position : t.position).ToString("F3")
                        + ", footprint x " + bxz.xMin.ToString("F3") + ".." + bxz.xMax.ToString("F3") + " z " + bxz.yMin.ToString("F3") + ".." + bxz.yMax.ToString("F3")
                        + ", nearest " + nearestName + " " + (nearest * 100f).ToString("F1") + " cm, squirt " + (ps != null ? "yes" : "no"));
                }
            }
            req(count == 1 + CourseStations.Length, "bottle: " + (1 + CourseStations.Length) + " bottles, one per station (" + count + ")");
            int sceneBottles = UnityEngine.Object.FindObjectsOfType<KH_WashTool>(true).Count(x => x.tool == KH_DogWash.ToolBottle);
            req(sceneBottles == count, "bottle: every KH_WashTool with tool " + KH_DogWash.ToolBottle + " in the scene is a station's bottle (" + sceneBottles + " / " + count + ")");
            r.bottleCount = count;
        }

        /// <summary>The dogs' side of hand washing: washCalls = every station's Call, main first (a received hand stroke needs a held,
        /// arrived dog), and the hand / bottle fields in WashTuning (their proxy + backing values are compared in CheckDog with the rest
        /// of the table).</summary>
        static void CheckDogHandWiring(VerifyReport r, Action<bool, string> req, UnityEngine.SceneManagement.Scene scene)
        {
            TuningCovers(req, typeof(KH_DogWash), n => n.StartsWith("bottle") || n.StartsWith("hand") || n.StartsWith("ownFoam"), WashTuning, "dog hand wash: WashTuning");
            var calls = AllStationCalls(scene).ToArray();
            req(calls.Length == 1 + CourseStations.Length && calls[0] != null && calls[0].transform.parent != null && StationIndexOf(calls[0].transform.parent) == 0,
                "dog hand wash: " + (1 + CourseStations.Length) + " station Calls, the main station's first (" + string.Join(", ", calls.Select(c => c.transform.parent != null ? c.transform.parent.name : c.name)) + ")");
            KD_DogWanderController[] wanders; KH_DogWash[] washes;
            DogsByIndex(out wanders, out washes);
            for (int i = 0; i < washes.Length; i++)
            {
                var w = washes[i];
                if (w == null) { req(false, "dog hand wash: KH_DogWash dogIndex " + i + " found"); continue; }
                req(w.washCalls != null && w.washCalls.Length == calls.Length && w.washCalls.Zip(calls, (a, b) => a == b).All(x => x),
                    "dog hand wash: " + w.name + ".washCalls = the " + calls.Length + " stations' Call, main first (" + (w.washCalls != null ? w.washCalls.Length : 0) + ")");
                Backing(req, UdonSharpEditorUtility.GetBackingUdonBehaviour(w), "dog hand wash: " + w.name, "washCalls", calls);
                // Codex diff review 2026-10-03: the Owner-side station hold check (DogStationHold) rejects hand strokes when voiceDog is
                // null. The builder (Dog.cs WireWanders) writes voiceDog = the KD_DogWanderController with the same dogIndex.
                var own = wanders[i];
                req(own != null && w.voiceDog == own && own.dogIndex == w.dogIndex,
                    "dog hand wash: " + w.name + " voiceDog = its own KD_DogWanderController (dogIndex " + i + ": " + (own != null ? own.name : "missing") + "; wired "
                    + (w.voiceDog != null ? w.voiceDog.name + " dogIndex " + w.voiceDog.dogIndex : "null") + ") (the Owner-side station hold check needs it)");
                Backing(req, UdonSharpEditorUtility.GetBackingUdonBehaviour(w), "dog hand wash: " + w.name, "voiceDog", own);
            }
            r.notes.Add("dog hand wash: WashTuning's " + WashTuning.Length + " entries (incl. the hand / bottle ones) are compared proxy + backing per dog in the dog checks ('KH_DogWash.<field> = table')");
        }

        static void CheckHandWashObject(VerifyReport r, Action<bool, string> req, UnityEngine.SceneManagement.Scene scene)
        {
            const string L = "hand wash: ";
            TuningCovers(req, typeof(KH_HandWash), n => true, HandWashTuning, L + "HandWashTuning");
            string asset = ScriptsDir + "/KH_HandWash.asset";
            var program = AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(asset);
            req(program != null, L + "program asset " + asset);
            var all = UnityEngine.Object.FindObjectsOfType<KH_HandWash>(true);
            r.handWashCount = all.Length;
            var mainGo = scene.GetRootGameObjects().FirstOrDefault(g => g.name == StationName);
            var t = mainGo != null ? mainGo.transform.Find(HandWashName) : null;
            var hand = t != null ? t.GetComponent<KH_HandWash>() : null;
            req(all.Length == 1 && hand != null && all[0] == hand, L + "exactly one KH_HandWash, at " + StationName + "/" + HandWashName + " (" + string.Join(", ", all.Select(x => KD_SceneObjects.PathOf(x.gameObject))) + ")");
            if (hand == null) { r.handWash = "missing"; return; }
            var hb = UdonSharpEditorUtility.GetBackingUdonBehaviour(hand);
            req(hb != null && hb.SyncMethod == Networking.SyncType.None, L + "backing sync None (local input; the strokes go through the dogs)");
            req(hb != null && program != null && hb.programSource == program, L + "backing program = " + asset);
            req(t.gameObject.activeInHierarchy && hb != null && hb.enabled, L + StationName + "/" + HandWashName + " active and enabled (its idle loop starts in Start / OnEnable)");
            var calls = AllStationCalls(scene).ToArray();
            req(calls.Length == 1 + CourseStations.Length && hand.calls != null && hand.calls.Length == calls.Length && hand.calls.Zip(calls, (a, b) => a == b).All(x => x),
                L + "calls = the " + calls.Length + " stations' Call, main first (" + (hand.calls != null ? hand.calls.Length : 0) + ")");
            KD_DogWanderController[] wanders; KH_DogWash[] washes;
            DogsByIndex(out wanders, out washes);
            req(washes.All(w => w != null) && hand.washes != null && hand.washes.Length == washes.Length && hand.washes.Zip(washes, (a, b) => a == b).All(x => x),
                L + "washes = the " + washes.Length + " KH_DogWash by dogIndex (" + (hand.washes != null ? hand.washes.Length : 0) + ")");
            req(hand.washLayer.value == 1 << WashLayer, L + "washLayer = layer " + WashLayer + " only (" + hand.washLayer.value + ")");
            req(hand.debugHands == 0 && hand.debugLeft == Vector3.zero && hand.debugRight == Vector3.zero && !hand.debugLeftFree && !hand.debugRightFree && !hand.debugVr,
                L + "harness inputs off (debugHands " + hand.debugHands + ", debugVr " + hand.debugVr + ", debugLeft/Right zero, debug*Free false)");
            req(!hand.frameLoop && hand.contactCalls == 0, L + "harness read-outs at rest in the saved scene (frameLoop false, contactCalls 0)");
            TuningMatches(req, hand, HandWashTuning, L + "KH_HandWash");
            Backing(req, hb, L + "KH_HandWash", "calls", calls);
            Backing(req, hb, L + "KH_HandWash", "washes", washes);
            Backing(req, hb, L + "KH_HandWash", "washLayer", hand.washLayer);
            Backing(req, hb, L + "KH_HandWash", "debugHands", 0);
            Backing(req, hb, L + "KH_HandWash", "debugLeft", Vector3.zero);
            Backing(req, hb, L + "KH_HandWash", "debugRight", Vector3.zero);
            Backing(req, hb, L + "KH_HandWash", "debugLeftFree", false);
            Backing(req, hb, L + "KH_HandWash", "debugRightFree", false);
            Backing(req, hb, L + "KH_HandWash", "debugVr", false);
            Backing(req, hb, L + "KH_HandWash", "frameLoop", false);
            Backing(req, hb, L + "KH_HandWash", "contactCalls", 0);
            r.handWash = KD_SceneObjects.PathOf(t.gameObject) + " calls " + (hand.calls != null ? hand.calls.Length : 0) + ", washes " + (hand.washes != null ? hand.washes.Count(x => x != null) : 0)
                + ", washLayer " + hand.washLayer.value + ", sync " + (hb != null ? hb.SyncMethod.ToString() : "-");
        }

        static bool SameRect(Rect a, Rect b) { return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.width - b.width) + Mathf.Abs(a.height - b.height) < 1e-5f; }

        /// <summary>A RectTransform's corners in station-local space: (min y, max y, mean z).</summary>
        static Vector3 RectSpanInStation(Transform st, RectTransform t)
        {
            var w = new Vector3[4]; t.GetWorldCorners(w);
            var l = w.Select(p => st.InverseTransformPoint(p)).ToArray();
            return new Vector3(l.Min(p => p.y), l.Max(p => p.y), l.Average(p => p.z));
        }

        /// <summary>The guide picture as Wash/11 would write it now (the same drawing and encoding as EnsureGuideTexture).</summary>
        static byte[] GuidePngNow()
        {
            var rgba = KH_WashGuideArt.RenderBytes();
            var tex = new Texture2D(KH_WashGuideArt.Width, KH_WashGuideArt.Height, TextureFormat.RGBA32, false, false);
            try
            {
                var px = new Color32[KH_WashGuideArt.Width * KH_WashGuideArt.Height];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(rgba[4 * i], rgba[4 * i + 1], rgba[4 * i + 2], rgba[4 * i + 3]);
                tex.SetPixels32(px);
                tex.Apply(false, false);
                return tex.EncodeToPNG();
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        static void CheckGuides(VerifyReport r, Action<bool, string> req, UnityEngine.SceneManagement.Scene scene)
        {
            // ---- the words: W 24..37 are the 14 wash.guide.* keys (their 13 languages, KD_Language, glyphs and fit: CheckBoardText) ----
            var guideW = new[] { KH_WashStationBoard.WGuideStep1, KH_WashStationBoard.WGuideStep1Sub, KH_WashStationBoard.WGuideStep2, KH_WashStationBoard.WGuideStep2Sub,
                KH_WashStationBoard.WGuideStep2Flat, KH_WashStationBoard.WGuideStep2FlatSub, KH_WashStationBoard.WGuideStep3, KH_WashStationBoard.WGuideStep3Sub,
                KH_WashStationBoard.WGuideStep3Flat, KH_WashStationBoard.WGuideStep3FlatSub, KH_WashStationBoard.WGuideStep4, KH_WashStationBoard.WGuideStep4Sub,
                KH_WashStationBoard.WGuideHelp, KH_WashStationBoard.WGuideHelpFlat };
            int guideKeys = BoardWordKeys.Count(k => k.StartsWith("wash.guide."));
            req(guideW.Length == 14 && guideW.Distinct().Count() == guideW.Length && guideW.All(i => i >= 0 && i < BoardWordKeys.Length && BoardWordKeys[i].StartsWith("wash.guide.")) && guideKeys == guideW.Length
                && BoardWordKeys.Length == KH_WashStationBoard.WordCount,
                "guide: BoardWordKeys (" + BoardWordKeys.Length + " = WordCount " + KH_WashStationBoard.WordCount + ") holds the " + guideW.Length + " wash.guide.* keys at the board's WGuide* indices (" + guideKeys + " wash.guide.* keys)");
            // the layout tables agree (4 panels: labels 2 per panel + Help, one picture per panel, an arrow between neighbours)
            req(GuidePanelX.Length == GuideSteps && GuidePicNames.Length == GuideSteps && GuideLabelNames.Length == 2 * GuideSteps + 1 && GuideArrowNames.Length == GuideSteps - 1
                && GuideArrowX.Length == GuideSteps - 1 && GuidePicUvBuilt.Length == GuideSteps && GuideLabelWords(true).Length == GuideLabelNames.Length,
                "guide: " + GuideSteps + " panels, " + GuideLabelNames.Length + " labels, " + GuidePicNames.Length + " pictures, " + GuideArrowNames.Length + " arrows (Guide.cs tables agree)");
            // the cells: inside the texture, each one 256² cell of the 4 x 2 grid, the six used cells distinct
            var cells = new[] { GuideUvFoam, GuideUvRelease, GuideUvBothHands, GuideUvAddMore, GuideUvHoldSponge, GuideUvSponge };
            Func<Rect, bool> isCell = u => Mathf.Abs(u.width - (float)KH_WashGuideArt.CellPx / KH_WashGuideArt.Width) < 1e-5f && Mathf.Abs(u.height - (float)KH_WashGuideArt.CellPx / KH_WashGuideArt.Height) < 1e-5f
                && u.xMin >= -1e-5f && u.xMax <= 1f + 1e-5f && u.yMin >= -1e-5f && u.yMax <= 1f + 1e-5f
                && Mathf.Abs(u.x * KH_WashGuideArt.Columns - Mathf.Round(u.x * KH_WashGuideArt.Columns)) < 1e-4f && Mathf.Abs(u.y * KH_WashGuideArt.Rows - Mathf.Round(u.y * KH_WashGuideArt.Rows)) < 1e-4f;
            req(cells.All(isCell) && cells.Select(u => u.x + "," + u.y).Distinct().Count() == cells.Length,
                "guide: the 6 uvRects are distinct 256² cells of the " + KH_WashGuideArt.Width + " x " + KH_WashGuideArt.Height + " grid (" + string.Join(" ", cells.Select(u => u.ToString())) + ")");

            // ---- the texture and its import settings ----
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(GuideTexturePath);
            var ti = AssetImporter.GetAtPath(GuideTexturePath) as TextureImporter;
            req(tex != null && ti != null, "guide: texture " + GuideTexturePath + " imported");
            if (tex != null) req(tex.width == KH_WashGuideArt.Width && tex.height == KH_WashGuideArt.Height && KH_WashGuideArt.Width == 1024 && KH_WashGuideArt.Height == 512,
                "guide: texture " + tex.width + " x " + tex.height + " = " + KH_WashGuideArt.Width + " x " + KH_WashGuideArt.Height + " (1024 x 512, IMP-UI-003)");
            if (ti != null)
            {
                req(ti.textureType == TextureImporterType.Default && ti.sRGBTexture && ti.alphaIsTransparency && !ti.isReadable && ti.mipmapEnabled && ti.wrapMode == TextureWrapMode.Clamp
                    && ti.filterMode == FilterMode.Bilinear && ti.npotScale == TextureImporterNPOTScale.None && ti.maxTextureSize == GuideMaxSize,
                    "guide: import Default, sRGB, alpha is transparency, Read/Write off, mipmaps on, clamp, bilinear, no NPOT scale, max " + GuideMaxSize
                    + " (readable " + ti.isReadable + ", mips " + ti.mipmapEnabled + ", max " + ti.maxTextureSize + ")");
                foreach (var pf in new[] { new KeyValuePair<string, TextureImporterFormat>("Standalone", TextureImporterFormat.DXT5), new KeyValuePair<string, TextureImporterFormat>("Android", TextureImporterFormat.ASTC_6x6), new KeyValuePair<string, TextureImporterFormat>("iPhone", TextureImporterFormat.ASTC_6x6) })
                {
                    var ps = ti.GetPlatformTextureSettings(pf.Key);
                    req(ps.overridden && ps.format == pf.Value && ps.maxTextureSize <= GuideMaxSize, "guide: import " + pf.Key + " override " + pf.Value + ", max <= " + GuideMaxSize + " (" + (ps.overridden ? ps.format + " " + ps.maxTextureSize : "not overridden") + ")");
                }
            }
            try
            {
                bool same = File.Exists(GuideTexturePath) && File.ReadAllBytes(GuideTexturePath).SequenceEqual(GuidePngNow());
                req(same, "guide: " + GuideTexturePath + " = the current drawing (KH_WashGuideArt v" + GuideArtVersion + "; run Wash/19 or Wash/11 to rewrite it)");
            }
            catch (Exception e) { req(false, "guide: the drawing renders for the comparison (" + e.GetType().Name + ": " + e.Message + ")"); }
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);

            // ---- every station ----
            var stations = AllStationTransforms(scene);
            int built = 0;
            foreach (var st in stations)
            {
                string label = "guide: " + st.name;
                var g = st.Find(GuideName) as RectTransform;
                var bt = st.Find(BoardName) as RectTransform;
                var board = bt != null ? bt.GetComponent<KH_WashStationBoard>() : null;
                req(g != null && bt != null && g.parent == st && bt.parent == st, label + "/" + GuideName + " a sibling of " + BoardName + " (the board's builder deletes the board's unknown children)");
                if (g == null) continue;
                built++;
                var canvas = g.GetComponent<Canvas>();
                req(canvas != null && canvas.renderMode == RenderMode.WorldSpace && g.gameObject.layer == 0 && g.gameObject.activeInHierarchy, label + ": World Space Canvas on Default, active");
                req((g.sizeDelta - GuidePixels).sqrMagnitude < 1e-4f && Near(g.localScale, Vector3.one * GuideScale) && Near(g.localPosition, GuideLocal) && Quaternion.Angle(g.localRotation, Quaternion.identity) < 0.01f,
                    label + ": " + GuidePixels.x + " x " + GuidePixels.y + " px at " + GuideScale + " m/px, local " + GuideLocal.ToString("F3") + ", facing the front (" + g.sizeDelta + ", " + g.localPosition.ToString("F3") + ")");
                if (bt != null)
                {
                    var gs = RectSpanInStation(st, g); var bs = RectSpanInStation(st, bt);
                    req(gs.x >= bs.y + 0.01f - 1e-4f, label + ": the guide's bottom " + gs.x.ToString("F3") + " m >= the board's top " + bs.y.ToString("F3") + " + 0.01 (top " + gs.y.ToString("F3") + " m)");
                    req(Mathf.Abs(gs.z - bs.z) < 1e-3f && Mathf.Abs(g.localPosition.x - bt.localPosition.x) < 1e-3f, label + ": on the board's face, centred over it (z " + gs.z.ToString("F3") + " / " + bs.z.ToString("F3") + ")");
                }
                // read only
                int rays = g.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(true).Length, shapes = g.GetComponentsInChildren<VRCUiShape>(true).Length, cols = g.GetComponentsInChildren<Collider>(true).Length;
                req(rays == 0 && shapes == 0 && cols == 0, label + ": no GraphicRaycaster / VRCUiShape / Collider (read only; " + rays + " / " + shapes + " / " + cols + ")");
                var hot = g.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Where(x => x.raycastTarget).Select(x => x.name).ToArray();
                req(hot.Length == 0, label + ": every Graphic raycastTarget false (" + string.Join(", ", hot) + ")");
                // children: Bg, the 9 labels, the 4 pictures, the 3 arrows, nothing else
                var known = new[] { "Bg" }.Concat(GuideLabelNames).Concat(GuidePicNames).Concat(GuideArrowNames).ToArray();
                var kids = g.Cast<Transform>().Select(x => x.name).ToArray();
                req(kids.Length == known.Length && known.All(n => kids.Count(k => k == n) == 1), label + ": children " + string.Join(", ", known) + " (" + string.Join(", ", kids) + ")");
                var bgI = g.Find("Bg") != null ? g.Find("Bg").GetComponent<UnityEngine.UI.Image>() : null;
                req(bgI != null && g.Find("Bg").GetSiblingIndex() == 0, label + ": Bg Image drawn first");

                // layout (Guide.cs; plan §13.11): positions and sizes, inside the canvas, no two parts overlap
                var layout = new List<KeyValuePair<string, Rect>>();
                Func<Vector2, Vector2, Rect> R = (p, s) => Rect.MinMaxRect(p.x - s.x * 0.5f, p.y - s.y * 0.5f, p.x + s.x * 0.5f, p.y + s.y * 0.5f);
                for (int i = 0; i < GuideSteps; i++)
                {
                    layout.Add(new KeyValuePair<string, Rect>(GuideLabelNames[2 * i], R(new Vector2(GuidePanelX[i], GuideStepY), GuideStepSize)));
                    layout.Add(new KeyValuePair<string, Rect>(GuideLabelNames[2 * i + 1], R(new Vector2(GuidePanelX[i], GuideSubY), GuideSubSize)));
                    layout.Add(new KeyValuePair<string, Rect>(GuidePicNames[i], R(new Vector2(GuidePanelX[i], GuidePicY), GuidePicSize)));
                }
                layout.Add(new KeyValuePair<string, Rect>(GuideLabelNames[2 * GuideSteps], R(GuideHelpPos, GuideHelpSize)));
                for (int i = 0; i < GuideArrowNames.Length; i++) layout.Add(new KeyValuePair<string, Rect>(GuideArrowNames[i], R(new Vector2(GuideArrowX[i], GuideArrowY), GuideArrowSize)));
                var misplaced = new List<string>(); var placed = new List<KeyValuePair<string, Rect>>();
                foreach (var kv in layout)
                {
                    var x = g.Find(kv.Key) as RectTransform;
                    if (x == null) { misplaced.Add(kv.Key + " missing"); continue; }
                    var got = PartRect(x);
                    if (Mathf.Abs(got.xMin - kv.Value.xMin) + Mathf.Abs(got.yMin - kv.Value.yMin) + Mathf.Abs(got.width - kv.Value.width) + Mathf.Abs(got.height - kv.Value.height) > 0.01f) misplaced.Add(kv.Key + " " + got + " (want " + kv.Value + ")");
                    placed.Add(new KeyValuePair<string, Rect>(kv.Key, got));
                }
                req(misplaced.Count == 0, label + ": the " + layout.Count + " parts at the builder's layout (" + string.Join("; ", misplaced) + ")");
                var inside = Rect.MinMaxRect(-GuidePixels.x * 0.5f, -GuidePixels.y * 0.5f, GuidePixels.x * 0.5f, GuidePixels.y * 0.5f);
                var outside = placed.Where(p => p.Value.xMin < inside.xMin - 0.01f || p.Value.xMax > inside.xMax + 0.01f || p.Value.yMin < inside.yMin - 0.01f || p.Value.yMax > inside.yMax + 0.01f).Select(p => p.Key).ToArray();
                var overlaps = new List<string>();
                for (int i = 0; i < placed.Count; i++) for (int j = i + 1; j < placed.Count; j++) if (placed[i].Value.Overlaps(placed[j].Value)) overlaps.Add(placed[i].Key + "/" + placed[j].Key);
                req(outside.Length == 0 && overlaps.Count == 0, label + ": every part inside the canvas, none overlapping (outside: " + string.Join(", ", outside) + "; overlaps: " + string.Join(", ", overlaps) + ")");

                // the texts: shared font, autosize down to MinPt, one line; the labels start as the ja VR words, the arrows read GuideArrowText
                var labelW = GuideLabelWords(true);
                var tmps = GuideLabelNames.Concat(GuideArrowNames).Select(n => g.Find(n) != null ? g.Find(n).GetComponent<TextMeshProUGUI>() : null).ToArray();
                req(tmps.All(x => x != null && font != null && x.font == font && x.enableAutoSizing && Mathf.Abs(x.fontSizeMin - MinPt) < 1e-4f && !x.enableWordWrapping),
                    label + ": the " + GuideLabelNames.Length + " labels and " + GuideArrowNames.Length + " arrows are TextMeshProUGUI with " + KD_ShopSetupEditor.SharedFontPath + ", autosize down to " + MinPt + " pt, one line");
                if (board != null && board.words != null && board.words.Length == BoardWordKeys.Length)
                {
                    var wrong = Enumerable.Range(0, GuideLabelNames.Length).Where(i => tmps[i] == null || tmps[i].text != board.words[labelW[i]]).Select(i => GuideLabelNames[i]).ToArray();
                    req(wrong.Length == 0, label + ": the labels read the board's ja VR words in the saved scene (wrong: " + string.Join(", ", wrong) + ")");
                }
                var arrows = tmps.Skip(GuideLabelNames.Length).ToArray();
                req(arrows.All(x => x != null && x.text == GuideArrowText), label + ": " + string.Join(" / ", GuideArrowNames) + " read '" + GuideArrowText + "'");

                // the pictures: RawImage cells of the guide texture (② ③ start as the VR cells; the board switches them per device)
                var raws = GuidePicNames.Select(n => g.Find(n) != null ? g.Find(n).GetComponent<UnityEngine.UI.RawImage>() : null).ToArray();
                var wantUv = GuidePicUvBuilt;
                req(raws.All(x => x != null) && tex != null && raws.All(x => x.texture == tex) && Enumerable.Range(0, GuideSteps).All(i => SameRect(raws[i].uvRect, wantUv[i])),
                    label + ": " + string.Join(" / ", GuidePicNames) + " RawImage on " + GuideTexturePath + ", uvRect foam / release / both hands / add more cells (" + string.Join(" ", raws.Select(x => x != null ? x.uvRect.ToString() : "none")) + ")");

                // the board's wiring
                if (board == null) { req(false, label + ": " + BoardName + " KH_WashStationBoard (guide wiring)"); continue; }
                req(board.guideLabels != null && board.guideLabels.Length == GuideLabelNames.Length && Enumerable.Range(0, GuideLabelNames.Length).All(i => board.guideLabels[i] != null && board.guideLabels[i] == tmps[i]),
                    label + ": board.guideLabels = " + GuideName + "/" + string.Join(", ", GuideLabelNames) + " (" + (board.guideLabels != null ? board.guideLabels.Length : 0) + ")");
                req(board.guidePics != null && board.guidePics.Length == GuidePicNames.Length && Enumerable.Range(0, GuidePicNames.Length).All(i => board.guidePics[i] != null && board.guidePics[i] == raws[i]),
                    label + ": board.guidePics = " + GuideName + "/" + string.Join(", ", GuidePicNames) + " (" + (board.guidePics != null ? board.guidePics.Length : 0) + ")");
                req(SameRect(board.guideUvVr2, GuideUvRelease) && SameRect(board.guideUvFlat2, GuideUvHoldSponge) && SameRect(board.guideUvVr3, GuideUvBothHands) && SameRect(board.guideUvFlat3, GuideUvSponge),
                    label + ": board.guideUvVr2 = the release cell " + GuideUvRelease + ", guideUvFlat2 = the hold-sponge cell " + GuideUvHoldSponge
                    + ", guideUvVr3 = the both-hands cell " + GuideUvBothHands + ", guideUvFlat3 = the sponge cell " + GuideUvSponge
                    + " (" + board.guideUvVr2 + " / " + board.guideUvFlat2 + " / " + board.guideUvVr3 + " / " + board.guideUvFlat3 + ")");
                req(!board.debugVr, label + ": board.debugVr false (harness only)");
                var bb = UdonSharpEditorUtility.GetBackingUdonBehaviour(board);
                Backing(req, bb, label + ": board", "guideLabels", board.guideLabels);
                Backing(req, bb, label + ": board", "guidePics", board.guidePics);
                Backing(req, bb, label + ": board", "guideUvVr2", GuideUvRelease);
                Backing(req, bb, label + ": board", "guideUvFlat2", GuideUvHoldSponge);
                Backing(req, bb, label + ": board", "guideUvVr3", GuideUvBothHands);
                Backing(req, bb, label + ": board", "guideUvFlat3", GuideUvSponge);
                Backing(req, bb, label + ": board", "debugVr", false);
                r.guides.Add(KD_SceneObjects.PathOf(g.gameObject) + " local " + g.localPosition.ToString("F3") + ", labels " + (board.guideLabels != null ? board.guideLabels.Length : 0)
                    + ", pictures " + (board.guidePics != null ? board.guidePics.Length : 0));
            }
            req(built == 1 + CourseStations.Length, "guide: " + (1 + CourseStations.Length) + " stations have a " + GuideName + " (" + built + ")");
            r.guideCount = built;
        }

        /// <summary>A UI part's rectangle in its parent's units (anchored at the centre, as EnsureUiChild makes them).</summary>
        static Rect PartRect(RectTransform t)
        {
            var c = t.anchoredPosition; var s = t.sizeDelta;
            return Rect.MinMaxRect(c.x - s.x * 0.5f, c.y - s.y * 0.5f, c.x + s.x * 0.5f, c.y + s.y * 0.5f);
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
