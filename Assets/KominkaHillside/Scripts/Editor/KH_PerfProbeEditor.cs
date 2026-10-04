using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-ENV-010: the local-only ON/OFF probe for measuring Quest fps per group (temporary; remove before release).
    ///
    /// Mobile/20 builds (first run on a fresh checkout only creates the two Program Assets and stops without touching the
    /// scene; compile U# and run 20 again). The scene is marked dirty but NOT saved. Mobile/21 re-scans and checks the probe.
    /// Mobile/22 removes the KH_PerfProbe root.</summary>
    public static class KH_PerfProbeEditor
    {
        const string Menu = "Tools/KominkaHillside/Mobile/";
        const string ScriptsDir = "Assets/KominkaHillside/Scripts";
        static readonly string[] Programs = { "KH_PerfProbe", "KH_PerfProbeButton" };
        public const string RootName = "KH_PerfProbe";
        const string BuildReportPath = "Temp/kh-perf-probe-build.json", VerifyReportPath = "Temp/kh-perf-probe-verify.json";
        // The same wood as the wash station stand and the water-spot sign (no plate material exists under Materials/).
        const string ButtonMatPath = "Assets/KominkaHillside/Data/Environment/River/KH_RiverWood.mat";
        const string EntranceTerrainName = "KH_Terrain";

        static readonly Vector3 SpawnPos = new Vector3(47.64f, 1.61f, 40.08f);
        static readonly Vector3 SpawnFwd = new Vector3(-0.44f, 0f, 0.90f);
        // RowLeft < 0 puts the row to the right of spawn, clear of the leash rack on the left. 2026-10-04 (VER-ENV-002): the old spot
        // (2.5 m ahead, 1.6 m right) is where the shopkeeper stands now, so the buttons sit beside the spawn on its right, in two
        // rows (groups 0-5 on top, 6-10 below) to stay clear of the photo board structure and the gate.
        const float RowAhead = -0.3f, RowLeft = -2.48f, ButtonY = 1.55f, RowDrop = 0.55f, ButtonSpacing = 0.35f, ButtonSize = 0.25f;
        const int TopRowCount = 6;

        const int ButtonLayer = 17;

        public static readonly string[] Names = { "Terrain", "Scenery", "Leaves", "Entrance", "Dogs", "River", "DogUdon", "NewGrass", "NewBoards", "NewOther", "Props" };
        public static readonly string[] InteractTexts =
        {
            "Terrain ON/OFF (all tiles, collider kept)",
            "Trees/stones ON/OFF",
            "Leaves ON/OFF",
            "Entrance tile + grass ON/OFF",
            "Dog bodies ON/OFF",
            "River ON/OFF",
            "Dog Udon ON/OFF (local, dogs may desync)",
            "10-04 grass ON/OFF (roadside + river verge)",
            "10-04 board trees ON/OFF",
            "10-04 other scenery ON/OFF (shrubs, trees, stones, benches)",
            "Finished props ON/OFF (KH_PropFinish)",
        };
        const string Title = "PERF PROBE (local, remove before release)";

        static readonly string[] LeafShaders = { "KominkaHillside/Foliage Lite", "LMArtShader/NatureLeaves" };
        const string RiverShader = "KominkaHillside/River Lite";
        static readonly string[] SceneryRoots =
        {
            "KH_CourseScenery/Trees", "KH_CourseScenery/BankStones", "KH_World/KH_Trees", "KH_World/KH_BoardTrees",
            "KH_World/KH_Shrubs", "KH_World/KH_Scenery", "KH_AutumnGroves", "KH_CourseExtension",
        };
        const string DogsRoot = "KH_Dogs", DogBodyToken = "KD_Dog_";
        // VER-ENV-002: the 10-04 scenery roots, split by material name; the finished props by material path.
        static readonly string[] NewRoots = { "KH_RoadsideAutumn", "KH_RiverVerge" };
        static readonly string[] NewGrassMats = { "KH_CourseGrass", "KH_RiverGrass" };
        static readonly string[] NewBoardMats = { "KH_CourseTreeBoards" };
        const string PropsMatPath = "Assets/KominkaHillside/Models/PropFinish/KH_PropFinish.mat";

        [Serializable] class BuildReport
        {
            public string utc, scene, status, reason, material, font;
            public int terrains, scenery, leaves, dogs, river, dogUdon, newGrass, newBoards, newOther, props;
            public bool entranceTerrain;
            public Vector3 rowCenter;
            public List<string> missingRoots = new List<string>();
        }

        [Serializable] class VerifyReport
        {
            public string utc, scene, result;
            public int pass, fail;
            public List<string> checks = new List<string>();
            public List<string> notes = new List<string>();
        }

        class Scan
        {
            public List<Terrain> terrains = new List<Terrain>();
            public Terrain entrance;
            public List<Renderer> scenery = new List<Renderer>(), leaves = new List<Renderer>(), dogs = new List<Renderer>(), river = new List<Renderer>();
            public List<Renderer> newGrass = new List<Renderer>(), newBoards = new List<Renderer>(), newOther = new List<Renderer>(), props = new List<Renderer>();
            public List<VRC.Udon.UdonBehaviour> dogUdon = new List<VRC.Udon.UdonBehaviour>();
            public List<string> missingRoots = new List<string>();
            public int entranceCount;
        }

        // ============================================================ 20 - Build

        [MenuItem(Menu + "20 - Build Perf Probe")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var report = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            Directory.CreateDirectory("Temp");

            // ---- Program Assets (first run only; the scene is not touched) ----
            var created = new List<string>();
            foreach (var name in Programs)
            {
                string asset = ScriptsDir + "/" + name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(asset) != null) continue;
                var program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                program.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptsDir + "/" + name + ".cs");
                if (program.sourceCsScript == null) throw new InvalidOperationException(name + ".cs not imported yet; refresh first");
                AssetDatabase.CreateAsset(program, asset);
                created.Add(asset);
            }
            if (created.Count > 0)
            {
                AssetDatabase.SaveAssets();
                UdonSharpProgramAsset.CompileAllCsPrograms(true);
                report.status = "PROGRAM_ASSET_CREATED";
                report.reason = "Created " + string.Join(", ", created) + ". Wait for the U# compile (unity-compile.sh --udon), then run Mobile/20 again. Scene unchanged.";
                File.WriteAllText(BuildReportPath, JsonUtility.ToJson(report, true));
                Debug.Log("[KH Perf Probe] " + report.reason);
                return;
            }

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            if (font == null) throw new InvalidOperationException("Shared UI font missing: " + KD_ShopSetupEditor.SharedFontPath);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ButtonMatPath);
            report.font = KD_ShopSetupEditor.SharedFontPath;
            report.material = mat != null ? ButtonMatPath : "(missing " + ButtonMatPath + "; primitive default material)";

            var scene = EditorSceneManager.GetActiveScene();
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (root == null) { root = new GameObject(RootName); Undo.RegisterCreatedObjectUndo(root, "Build Perf Probe"); }
            root.SetActive(true);
            for (int i = root.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

            // ---- placement: a row front-left of the spawn, facing the spawn ----
            var fwd = new Vector3(SpawnFwd.x, 0f, SpawnFwd.z).normalized;
            var right = new Vector3(fwd.z, 0f, -fwd.x);
            var center = SpawnPos + fwd * RowAhead - right * RowLeft;
            center.y = GroundY(center);
            var face = center - SpawnPos; face.y = 0f;
            root.transform.SetPositionAndRotation(center, Quaternion.LookRotation(face.normalized, Vector3.up));
            root.transform.localScale = Vector3.one;
            report.rowCenter = center;

            var probe = AddUdon<KH_PerfProbe>(root);

            MakeText(root.transform, "Title", new Vector3(0f, ButtonY + 0.55f, 0f), new Vector2(2.6f, 0.16f), 0.3f, 1.2f, font, Title);
            var labels = new TextMeshPro[KH_PerfProbe.GroupCount];
            var buttons = new KH_PerfProbeButton[KH_PerfProbe.GroupCount];
            for (int g = 0; g < KH_PerfProbe.GroupCount; g++)
            {
                bool top = g < TopRowCount;
                int col = top ? g : g - TopRowCount, cols = top ? TopRowCount : KH_PerfProbe.GroupCount - TopRowCount;
                float x = (col - (cols - 1) * 0.5f) * ButtonSpacing, y = top ? ButtonY : ButtonY - RowDrop;
                var holder = new GameObject("Button" + g + "_" + Names[g]);
                holder.transform.SetParent(root.transform, false);
                holder.transform.localPosition = new Vector3(x, 0f, 0f);

                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Cube";
                // Walkthrough (17): still Interact-able, but outside the dogs ground mask (layers 0 and 11). On Default the dogs
                // SnapToGround ray hit the cube top and a dog walking under the row popped 1.24 m up onto it (found 2026-10-01:
                // Course/53 and 63 failed with "Discontinuous dog movement", collider KH_PerfProbe/Button0_Terrain/Cube).
                cube.layer = ButtonLayer;
                cube.transform.SetParent(holder.transform, false);
                cube.transform.localPosition = new Vector3(0f, y, 0f);
                cube.transform.localRotation = Quaternion.identity;
                cube.transform.localScale = Vector3.one * ButtonSize;
                var box = cube.GetComponent<BoxCollider>(); box.isTrigger = false; box.center = Vector3.zero; box.size = Vector3.one;
                var mr = cube.GetComponent<MeshRenderer>();
                if (mat != null) mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;

                var button = AddUdon<KH_PerfProbeButton>(cube);
                button.probe = probe; button.group = g;
                Copy(button);
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(button);
                backing.interactText = InteractTexts[g];
                EditorUtility.SetDirty(backing);
                buttons[g] = button;

                labels[g] = MakeText(holder.transform, "Label", new Vector3(0f, y + ButtonSize * 0.5f + 0.12f, 0f), new Vector2(ButtonSpacing - 0.02f, 0.2f), 0.3f, 1.2f, font, Names[g] + "\nON");
            }

            // ---- scan and wire ----
            var scan = ScanScene(root.transform);
            probe.terrains = scan.terrains.Cast<Behaviour>().ToArray();
            probe.entranceTerrain = scan.entrance;
            probe.scenery = scan.scenery.ToArray();
            probe.leaves = scan.leaves.ToArray();
            probe.dogs = scan.dogs.ToArray();
            probe.river = scan.river.ToArray();
            probe.dogUdon = scan.dogUdon.ToArray();
            probe.newGrass = scan.newGrass.ToArray();
            probe.newBoards = scan.newBoards.ToArray();
            probe.newOther = scan.newOther.ToArray();
            probe.props = scan.props.ToArray();
            probe.labels = labels;
            probe.names = (string[])Names.Clone();
            Copy(probe);

            EditorSceneManager.MarkSceneDirty(scene);

            report.terrains = scan.terrains.Count; report.entranceTerrain = scan.entrance != null;
            report.scenery = scan.scenery.Count; report.leaves = scan.leaves.Count; report.dogs = scan.dogs.Count;
            report.river = scan.river.Count; report.dogUdon = scan.dogUdon.Count; report.missingRoots = scan.missingRoots;
            report.newGrass = scan.newGrass.Count; report.newBoards = scan.newBoards.Count; report.newOther = scan.newOther.Count; report.props = scan.props.Count;
            report.status = "BUILT";
            report.reason = "Scene marked dirty, NOT saved. Save the scene, then run Mobile/21.";
            File.WriteAllText(BuildReportPath, JsonUtility.ToJson(report, true));
            Debug.Log("[KH Perf Probe] BUILT terrains=" + report.terrains + " entrance=" + report.entranceTerrain + " scenery=" + report.scenery + " leaves=" + report.leaves
                + " dogs=" + report.dogs + " river=" + report.river + " dogUdon=" + report.dogUdon
                + " newGrass=" + report.newGrass + " newBoards=" + report.newBoards + " newOther=" + report.newOther + " props=" + report.props
                + (scan.missingRoots.Count > 0 ? " missingRoots=" + string.Join(",", scan.missingRoots) : "") + ". Not saved.");
        }

        // ============================================================ 21 - Verify

        [MenuItem(Menu + "21 - Verify Perf Probe")]
        public static void Verify()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("[KH Perf Probe] Edit mode only (not compiling)");
            var scene = EditorSceneManager.GetActiveScene();
            var r = new VerifyReport { utc = DateTime.UtcNow.ToString("o"), scene = scene.path };
            void Req(bool ok, string what) { r.checks.Add((ok ? "PASS " : "FAIL ") + what); if (ok) r.pass++; else r.fail++; }

            Req(scene.path == KH_CourseGroundEditor.ScenePath, "active scene is " + KH_CourseGroundEditor.ScenePath);
            foreach (var name in Programs)
                Req(AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ScriptsDir + "/" + name + ".asset") != null, "program asset " + name + ".asset");

            var roots = scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray();
            Req(roots.Length == 1, "exactly one scene root " + RootName + " (found " + roots.Length + ")");
            var probes = UnityEngine.Object.FindObjectsOfType<KH_PerfProbe>(true);
            Req(probes.Length == 1, "exactly one KH_PerfProbe (found " + probes.Length + ")");
            if (roots.Length != 1 || probes.Length != 1) { Finish(r); return; }
            var root = roots[0]; var probe = probes[0];
            Req(probe.gameObject == root, "KH_PerfProbe sits on the root");
            Req(root.transform.parent == null, "root has no parent (not under a synced object)");
            Req(root.activeInHierarchy, "root active");

            // ---- no sync anywhere under the probe ----
            var udons = root.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true);
            Req(udons.Length == 1 + KH_PerfProbe.GroupCount, "UdonBehaviours under the root = " + (1 + KH_PerfProbe.GroupCount) + " (found " + udons.Length + ")");
            Req(udons.All(u => u.SyncMethod == Networking.SyncType.None), "every UdonBehaviour under the root sync=None");
            Req(root.GetComponentsInChildren<VRC.SDK3.Components.VRCObjectSync>(true).Length == 0, "no VRCObjectSync under the root");
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(RootName)) continue;
                for (var p = t.parent; p != null; p = p.parent)
                    if (p.GetComponents<VRC.Udon.UdonBehaviour>().Any(u => u.SyncMethod != Networking.SyncType.None)) { Req(false, t.name + " under synced parent " + p.name); break; }
            }
            var probeBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(probe);
            Req(probeBacking != null && probeBacking.SyncMethod == Networking.SyncType.None, "probe backing sync=None");

            // ---- stored groups == a fresh scan ----
            var scan = ScanScene(root.transform);
            SameSet(Req, "terrains", probe.terrains, scan.terrains.Cast<Behaviour>());
            Req(probe.entranceTerrain != null && probe.entranceTerrain == scan.entrance, "entranceTerrain = " + EntranceTerrainName + " (scene has " + scan.entranceCount + ")");
            SameSet(Req, "scenery", probe.scenery, scan.scenery);
            SameSet(Req, "leaves", probe.leaves, scan.leaves);
            SameSet(Req, "dogs", probe.dogs, scan.dogs);
            SameSet(Req, "river", probe.river, scan.river);
            SameSet(Req, "dogUdon", probe.dogUdon, scan.dogUdon);
            SameSet(Req, "newGrass", probe.newGrass, scan.newGrass);
            SameSet(Req, "newBoards", probe.newBoards, scan.newBoards);
            SameSet(Req, "newOther", probe.newOther, scan.newOther);
            SameSet(Req, "props", probe.props, scan.props);
            if (scan.missingRoots.Count > 0) r.notes.Add("scenery roots not found: " + string.Join(", ", scan.missingRoots));
            var renderGroups = new[] { probe.scenery, probe.leaves, probe.dogs, probe.river, probe.newGrass, probe.newBoards, probe.newOther, probe.props };
            var seen = new HashSet<Renderer>(); int overlap = 0;
            foreach (var arr in renderGroups) if (arr != null) foreach (var x in arr) if (x != null && !seen.Add(x)) overlap++;
            Req(overlap == 0, "renderer groups are disjoint (overlaps " + overlap + ")");
            Req(scan.terrains.Count > 0 && scan.scenery.Count > 0 && scan.leaves.Count > 0 && scan.dogs.Count > 0 && scan.river.Count > 0 && scan.dogUdon.Count > 0
                && scan.newGrass.Count > 0 && scan.newBoards.Count > 0 && scan.newOther.Count > 0 && scan.props.Count > 0, "every group non-empty");
            foreach (var t in scan.terrains)
            {
                var col = t.GetComponent<TerrainCollider>();
                if (col == null || !col.enabled) r.notes.Add("terrain " + t.name + " has no enabled TerrainCollider (unrelated to the probe, but walking there already fails)");
            }

            // ---- labels and names ----
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            Req(probe.names != null && probe.names.Length == KH_PerfProbe.GroupCount && probe.names.SequenceEqual(Names), "names = " + string.Join(",", Names));
            Req(probe.labels != null && probe.labels.Length == KH_PerfProbe.GroupCount && probe.labels.All(l => l != null && l.transform.IsChildOf(root.transform)), KH_PerfProbe.GroupCount + " labels under the root");
            if (probe.labels != null && probe.labels.Length == KH_PerfProbe.GroupCount)
                for (int g = 0; g < KH_PerfProbe.GroupCount; g++)
                {
                    var l = probe.labels[g];
                    if (l == null) continue;
                    Req(l.text != null && l.text.StartsWith(Names[g]), "label " + g + " text starts with " + Names[g] + " ('" + (l.text ?? "").Replace("\n", "\\n") + "')");
                    Req(font != null && l.font == font && font.HasCharacters(Names[g] + "ONF: ", out List<char> _), "label " + g + " font " + KD_ShopSetupEditor.SharedFontPath + " has the glyphs");
                }
            var title = root.transform.Find("Title");
            var titleTmp = title != null ? title.GetComponent<TextMeshPro>() : null;
            Req(titleTmp != null && titleTmp.text == Title && font != null && font.HasCharacters(Title, out List<char> _), "title label '" + Title + "' with glyphs");

            // ---- buttons ----
            var buttons = root.GetComponentsInChildren<KH_PerfProbeButton>(true);
            Req(buttons.Length == KH_PerfProbe.GroupCount, KH_PerfProbe.GroupCount + " KH_PerfProbeButton (found " + buttons.Length + ")");
            var groups = new HashSet<int>();
            foreach (var b in buttons)
            {
                string tag = b.transform.parent != null ? b.transform.parent.name : b.name;
                bool inRange = b.group >= 0 && b.group < KH_PerfProbe.GroupCount;
                Req(inRange && groups.Add(b.group), tag + ": group " + b.group + " in range and unique");
                Req(b.probe == probe, tag + ": probe wired (proxy)");
                var bb = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
                Req(bb != null && bb.SyncMethod == Networking.SyncType.None, tag + ": sync=None");
                Req(inRange && bb != null && bb.interactText == InteractTexts[b.group], tag + ": interactText '" + (bb != null ? bb.interactText : "") + "'");
                var col = b.GetComponent<BoxCollider>();
                Req(col != null && col.enabled && !col.isTrigger, tag + ": solid BoxCollider on the Interact object");
                Req(b.gameObject.layer == ButtonLayer, tag + ": layer " + ButtonLayer + " (not ground for the dogs)");
                Req(inRange && b.transform.parent != null && b.transform.parent.name == "Button" + b.group + "_" + Names[b.group], tag + ": holder name matches group");
                if (bb != null && bb.publicVariables != null)
                {
                    if (bb.publicVariables.TryGetVariableValue("probe", out object v)) Req(ReferenceEquals(v, probeBacking), tag + ": probe wired (backing)");
                    else r.notes.Add(tag + ": backing heap has no 'probe' yet (written at save/build; proxy checked instead)");
                    if (bb.publicVariables.TryGetVariableValue("group", out object gv)) Req(gv is int gi && gi == b.group, tag + ": group " + b.group + " in backing heap (found " + gv + ")");
                    else r.notes.Add(tag + ": backing heap has no 'group' yet (written at save/build; proxy checked instead)");
                }
            }
            // The backing heap is what runs in VRChat; a stale heap with the proxy right would pass a proxy-only check.
            if (probeBacking != null && probeBacking.publicVariables != null)
            {
                BackingArray(Req, r, probeBacking, "terrains", probe.terrains);
                BackingArray(Req, r, probeBacking, "scenery", probe.scenery);
                BackingArray(Req, r, probeBacking, "leaves", probe.leaves);
                BackingArray(Req, r, probeBacking, "dogs", probe.dogs);
                BackingArray(Req, r, probeBacking, "river", probe.river);
                BackingArray(Req, r, probeBacking, "dogUdon", probe.dogUdon);
                BackingArray(Req, r, probeBacking, "newGrass", probe.newGrass);
                BackingArray(Req, r, probeBacking, "newBoards", probe.newBoards);
                BackingArray(Req, r, probeBacking, "newOther", probe.newOther);
                BackingArray(Req, r, probeBacking, "props", probe.props);
                BackingArray(Req, r, probeBacking, "labels", probe.labels);
                if (probeBacking.publicVariables.TryGetVariableValue("entranceTerrain", out object ev)) Req(ReferenceEquals(ev, probe.entranceTerrain), "entranceTerrain in backing heap == proxy");
                else r.notes.Add("probe backing heap has no 'entranceTerrain' yet (written at save/build; proxy checked instead)");
                if (probeBacking.publicVariables.TryGetVariableValue("names", out object nv)) Req(nv is string[] ns && ns.SequenceEqual(Names), "names in backing heap == " + string.Join(",", Names));
                else r.notes.Add("probe backing heap has no 'names' yet (written at save/build; proxy checked instead)");
            }
            Finish(r);
        }

        static void Finish(VerifyReport r)
        {
            r.result = r.fail == 0 ? "PASS" : "FAIL";
            Directory.CreateDirectory("Temp");
            File.WriteAllText(VerifyReportPath, JsonUtility.ToJson(r, true));
            var msg = "[KH Perf Probe] Verify " + r.result + " pass=" + r.pass + " fail=" + r.fail + " -> " + VerifyReportPath;
            if (r.fail == 0) Debug.Log(msg);
            else Debug.LogError(msg + "\n" + string.Join("\n", r.checks.Where(c => c.StartsWith("FAIL"))));
        }

        // Element-wise: the heap array must hold the same references in the same order as the proxy array.
        static void BackingArray<T>(Action<bool, string> req, VerifyReport r, UdonBehaviour backing, string field, T[] proxy) where T : UnityEngine.Object
        {
            if (!backing.publicVariables.TryGetVariableValue(field, out object v) || v == null)
            {
                r.notes.Add("probe backing heap has no '" + field + "' yet (written at save/build; proxy checked instead)");
                return;
            }
            var heap = v as Array;
            bool same = heap != null && proxy != null && heap.Length == proxy.Length;
            for (int i = 0; same && i < proxy.Length; i++) same = ReferenceEquals(heap.GetValue(i), proxy[i]);
            req(same, field + " in backing heap == proxy (" + (heap != null ? heap.Length.ToString() : "null") + " vs " + (proxy != null ? proxy.Length.ToString() : "null") + ")");
        }

        static void SameSet<T>(Action<bool, string> req, string name, T[] stored, IEnumerable<T> fresh) where T : UnityEngine.Object
        {
            var want = new HashSet<T>(fresh);
            var have = new HashSet<T>((stored ?? new T[0]).Where(x => x != null));
            int nulls = stored == null ? -1 : stored.Count(x => x == null);
            req(stored != null && nulls == 0 && stored.Length == have.Count && have.SetEquals(want),
                name + ": stored " + (stored != null ? stored.Length : -1) + " (nulls " + nulls + ") == scan " + want.Count
                + " (missing " + want.Count(x => !have.Contains(x)) + ", extra " + have.Count(x => !want.Contains(x)) + ")");
        }

        // ============================================================ 22 - Remove

        [MenuItem(Menu + "22 - Remove Perf Probe")]
        public static void Remove()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var scene = EditorSceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray();
            if (roots.Length == 0) { Debug.Log("[KH Perf Probe] no " + RootName + " root; nothing changed"); return; }
            foreach (var g in roots) Undo.DestroyObjectImmediate(g);
            // The probe's UdonBehaviours got NetworkIDs on save/Play; dead entries would otherwise linger as fileID 0
            // (vrc-scene-edit-safety). Only entries whose object is gone are dropped.
            int dropped = 0;
            var descriptor = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
            if (descriptor != null && descriptor.NetworkIDCollection != null)
            {
                Undo.RecordObject(descriptor, "Remove Perf Probe");
                for (int i = descriptor.NetworkIDCollection.Count - 1; i >= 0; i--)
                    if (descriptor.NetworkIDCollection[i].gameObject == null) { descriptor.NetworkIDCollection.RemoveAt(i); dropped++; }
                if (dropped > 0) EditorUtility.SetDirty(descriptor);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[KH Perf Probe] removed " + roots.Length + " " + RootName + " root(s), dropped " + dropped + " dead NetworkIDs. Scene marked dirty, NOT saved.");
        }

        // ============================================================ scan

        static Scan ScanScene(Transform probeRoot)
        {
            var s = new Scan();
            var scene = EditorSceneManager.GetActiveScene();
            bool Skip(Component c) { return c == null || c.gameObject.scene != scene || (probeRoot != null && c.transform.IsChildOf(probeRoot)); }

            // 0 / 3: terrains
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Terrain>(true))
                if (!Skip(t)) s.terrains.Add(t);
            var entrances = s.terrains.Where(t => t.name == EntranceTerrainName).ToList();
            s.entranceCount = entrances.Count;
            s.entrance = entrances.Count == 1 ? entrances[0] : null;

            // 2 / 5: shader groups over the whole scene
            var all = UnityEngine.Object.FindObjectsOfType<Renderer>(true).Where(x => !Skip(x)).ToList();
            // VER-ENV-002: the 10-04 scenery and the finished props first; they are kept out of every older group below.
            var sceneRoots = scene.GetRootGameObjects();
            var taken = new HashSet<Renderer>();
            foreach (var rootName in NewRoots)
            {
                var nr = sceneRoots.FirstOrDefault(g => g.name == rootName);
                if (nr == null) { s.missingRoots.Add(rootName); continue; }
                foreach (var r in nr.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (Skip(r) || !taken.Add(r)) continue;
                    if (HasMaterialName(r, NewGrassMats)) s.newGrass.Add(r);
                    else if (HasMaterialName(r, NewBoardMats)) s.newBoards.Add(r);
                    else s.newOther.Add(r);
                }
            }
            var propsMat = AssetDatabase.LoadAssetAtPath<Material>(PropsMatPath);
            if (propsMat != null)
                foreach (var r in all)
                    if (!taken.Contains(r) && Array.IndexOf(r.sharedMaterials, propsMat) >= 0) { taken.Add(r); s.props.Add(r); }
            foreach (var r in all)
            {
                if (taken.Contains(r)) continue;
                if (HasShader(r, LeafShaders)) s.leaves.Add(r);
                else if (HasShader(r, RiverShader)) s.river.Add(r);
            }

            // 1: scenery roots (MeshRenderers, not leaves / river)
            var sceneryRoots = scene.GetRootGameObjects();
            var scenery = new HashSet<Renderer>();
            foreach (var path in SceneryRoots)
            {
                var t = FindPath(sceneryRoots, path);
                if (t == null) { s.missingRoots.Add(path); continue; }
                foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
                    if (!Skip(r) && !taken.Contains(r) && !HasShader(r, LeafShaders) && !HasShader(r, RiverShader) && scenery.Add(r)) s.scenery.Add(r);
            }

            // 4 / 6: dogs
            var dogsRoot = sceneryRoots.FirstOrDefault(g => g.name == DogsRoot);
            if (dogsRoot == null) s.missingRoots.Add(DogsRoot);
            else
            {
                foreach (var r in dogsRoot.GetComponentsInChildren<Renderer>(true))
                    if ((r is SkinnedMeshRenderer || r is MeshRenderer) && !Skip(r) && !taken.Contains(r) && PathOf(r.transform).Contains(DogBodyToken)
                        && !HasShader(r, LeafShaders) && !HasShader(r, RiverShader) && !s.dogs.Contains(r)) s.dogs.Add(r);
                foreach (var u in dogsRoot.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true))
                    if (!Skip(u) && !IsProbeBehaviour(u)) s.dogUdon.Add(u);
            }
            // a renderer already in the scenery group is not also a dog (keeps the groups disjoint)
            s.dogs.RemoveAll(r => scenery.Contains(r));
            return s;
        }

        static bool IsProbeBehaviour(VRC.Udon.UdonBehaviour u)
        {
            var src = u.programSource as UdonSharpProgramAsset;
            if (src == null || src.sourceCsScript == null) return false;
            var cls = src.sourceCsScript.GetClass();
            return cls == typeof(KH_PerfProbe) || cls == typeof(KH_PerfProbeButton);
        }

        static bool HasShader(Renderer r, params string[] shaders)
        {
            foreach (var m in r.sharedMaterials)
                if (m != null && m.shader != null && Array.IndexOf(shaders, m.shader.name) >= 0) return true;
            return false;
        }

        static bool HasMaterialName(Renderer r, string[] names)
        {
            foreach (var m in r.sharedMaterials)
                if (m != null && Array.IndexOf(names, m.name) >= 0) return true;
            return false;
        }

        static Transform FindPath(GameObject[] roots, string path)
        {
            int cut = path.IndexOf('/');
            string head = cut < 0 ? path : path.Substring(0, cut);
            var root = roots.FirstOrDefault(g => g.name == head);
            if (root == null) return null;
            return cut < 0 ? root.transform : root.transform.Find(path.Substring(cut + 1));
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static float GroundY(Vector3 p)
        {
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Terrain>())
            {
                var d = t.terrainData; if (d == null) continue;
                var o = t.GetPosition();
                if (p.x >= o.x && p.x <= o.x + d.size.x && p.z >= o.z && p.z <= o.z + d.size.z) return o.y + t.SampleHeight(p);
            }
            return SpawnPos.y;
        }

        // ============================================================ parts (same pattern as KH_DogWashSetupEditor)

        static T AddUdon<T>(GameObject go) where T : UdonSharpBehaviour
        {
            var b = go.GetComponent<T>();
            if (b == null) b = go.AddUdonSharpComponent<T>();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            if (backing == null) throw new InvalidOperationException(typeof(T).Name + " backing UdonBehaviour missing (compile U# and run Mobile/20 again)");
            if (backing.SyncMethod != Networking.SyncType.None) { backing.SyncMethod = Networking.SyncType.None; EditorUtility.SetDirty(backing); }
            return b;
        }

        static void Copy(UdonSharpBehaviour b)
        {
            UdonSharpEditorUtility.CopyProxyToUdon(b);
            EditorUtility.SetDirty(b);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            if (backing == null) return;
            if ((backing.hideFlags & HideFlags.HideInInspector) == 0) backing.hideFlags |= HideFlags.HideInInspector;
            EditorUtility.SetDirty(backing);
        }

        static TextMeshPro MakeText(Transform parent, string name, Vector3 local, Vector2 size, float min, float max, TMP_FontAsset font, string text)
        {
            var go = new GameObject(name);
            go.layer = 0;
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshPro>();
            t.transform.localPosition = local; t.transform.localRotation = Quaternion.identity; t.transform.localScale = Vector3.one;
            t.font = font; t.text = text;
            t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false;
            t.enableAutoSizing = true; t.fontSizeMin = min; t.fontSizeMax = max;
            t.color = new Color(1f, 0.96f, 0.85f, 1f);
            t.rectTransform.sizeDelta = size;
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null) { mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; EditorUtility.SetDirty(mr); }
            EditorUtility.SetDirty(t);
            return t;
        }
    }
}
