using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UdonSharp;
using UdonSharpEditor;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    // IMP-DOG-020 stage 1 (DES-DOG-004 A-1, B-3 checks 1 and 6). Production scene only, idle Edit mode, saved.
    //   73 Build : bakes KH_CourseGroundEditor.RiverAt / HalfWidth at 1 m (z 0..720, 721 samples) into
    //              KH_Dogs/KH_RiverData and wires it to the 6 KD_DogWanderController, 6 KD_DogFetchController and
    //              KH_ToyRecall (SerializedObject + CopyProxyToUdon, vrc-udonsharp §4). Saves the scene.
    //              First run only creates the U# program asset and returns: run it again after the U# compile.
    //   74 Verify: never writes the scene. Check 1 = baked arrays against the saved water-surface meshes
    //              (KH_CourseExtension/RiverWater/Water_0..23): water y ≤ 0.01 m, centre x ≤ 0.05 m, half width ≤ 0.05 m.
    //              Check 6 = NavMesh points below the water surface along the exclusion edge (0 = PASS).
    //              Also: array vs formula (samples and mid-points) and the wiring.
    // Results: Temp/imp-dog-020/river-data-build.json, river-data-verify.json.
    public static class KH_RiverDataEditor
    {
        const string ProgramPath = "Assets/KominkaHillside/Scripts/KH_RiverData";
        const string OutDir = "Temp/imp-dog-020";
        const string DogsRoot = "KH_Dogs", NodeName = "KH_RiverData";
        const float ZStart = 0f, ZStep = 1f; const int Count = 721;   // terrain z 0..720
        // Design B-3 check 1 (DES-DOG-004): the surface y is piecewise linear in z with integer control points, so the
        // 1 m array reproduces it exactly; x is a Hermite curve, so only the linear interpolation between 1 m samples
        // differs from the mesh rows (C-4). Tolerances from B-3.
        const float TolSurfaceY = .01f, TolCenterX = .05f, TolHalfWidth = .05f;
        const float WaterMeshMargin = 1.8f;   // KH_CourseGroundEditor.WaterMesh: half width + 1.8 m
        const float DeckHalfZ = 1.75f;        // bridge deck BoxCollider size.z 3.5

        [Serializable] public class BuildReport
        {
            public string status, utc, scene, reason;
            public bool programAssetCreated;
            public int samples, wanderWired, fetchWired, toyRecallWired, droppedNetworkIds;
            public float[] bridgeZ;
        }
        [Serializable] public class Worst { public string what; public float error; public Vector3 at; }
        [Serializable] public class EdgePoint { public Vector3 nav; public float surfaceY, below; public int side; public bool nearBridge; }
        [Serializable] public class VerifyReport
        {
            public string status, utc, scene;
            public int samples, waterChunks, waterVertices, errors;
            public float maxSurfaceYError, maxCenterXError, maxHalfWidthError;
            public Worst worstSurfaceY, worstCenterX, worstHalfWidth;
            public float formulaMaxCenterXAtSamples, formulaMaxSurfaceYAtSamples, formulaMaxHalfWidthAtSamples;
            public float formulaMaxCenterXAtMidpoints, formulaMaxSurfaceYAtMidpoints, formulaMaxHalfWidthAtMidpoints;
            public int edgeQueries, edgeNavPoints, edgeUnderwater;
            public float edgeMinAboveSurface = float.MaxValue;
            public List<EdgePoint> underwater = new List<EdgePoint>();
            public int wanderWired, fetchWired, toyRecallWired;
            public List<string> findings = new List<string>();
            public string tolerances = "check 1: surface y <= 0.01 m, centre x <= 0.05 m, half width <= 0.05 m (DES-DOG-004 B-3 / C-4). check 6: NavMesh point below the water surface (0 = PASS).";
            public string limitations = "Static data and saved navigation only. Runtime avoidance is ClientSim harness W5 (Course/75); devices and real network unverified.";
        }

        [MenuItem("Tools/KominkaHillside/Course/73 - Build River Data")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var report = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath + ".asset") == null)
            {
                var created = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                created.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(ProgramPath + ".cs");
                if (created.sourceCsScript == null) throw new InvalidOperationException("KH_RiverData.cs not imported yet; refresh first");
                AssetDatabase.CreateAsset(created, ProgramPath + ".asset"); AssetDatabase.SaveAssets();
                UdonSharpProgramAsset.CompileAllCsPrograms(true);
                report.status = "PROGRAM_ASSET_CREATED"; report.programAssetCreated = true;
                report.reason = "Created KH_RiverData.asset. Wait for the U# compile (unity-compile.sh --udon), then run 73 again. Scene unchanged.";
                Write("river-data-build.json", JsonUtility.ToJson(report, true)); Debug.Log("[KH River Data] " + report.reason);
                return;
            }
            var dogs = GameObject.Find(DogsRoot);
            if (dogs == null) throw new InvalidOperationException("KH_Dogs missing");
            var wanders = UnityEngine.Object.FindObjectsOfType<KominkaDogRun.KD_DogWanderController>(true);
            var fetches = UnityEngine.Object.FindObjectsOfType<KominkaDogRun.KD_DogFetchController>(true);
            var recalls = UnityEngine.Object.FindObjectsOfType<KH_ToyRecall>(true);
            if (wanders.Length != 6 || fetches.Length != 6 || recalls.Length != 1)
                throw new InvalidOperationException("Expected 6 wander / 6 fetch / 1 toy recall, found " + wanders.Length + "/" + fetches.Length + "/" + recalls.Length + "; nothing changed");
            var bridges = BridgeZ();

            var node = dogs.transform.Find(NodeName);
            if (node == null) { var go = new GameObject(NodeName); go.transform.SetParent(dogs.transform, false); node = go.transform; }
            var data = node.GetComponent<KH_RiverData>();
            if (data == null) data = node.gameObject.AddUdonSharpComponent<KH_RiverData>();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(data);
            if (backing == null) throw new InvalidOperationException("KH_RiverData backing UdonBehaviour missing (compile U# and run again)");
            if (backing.SyncMethod != Networking.SyncType.None) { backing.SyncMethod = Networking.SyncType.None; EditorUtility.SetDirty(backing); }

            var so = new SerializedObject(data);
            so.FindProperty("zStart").floatValue = ZStart;
            so.FindProperty("zStep").floatValue = ZStep;
            so.FindProperty("bridgeHalfZ").floatValue = DeckHalfZ;
            so.FindProperty("deckRise").floatValue = .6f;
            var cx = so.FindProperty("centerX"); var sy = so.FindProperty("surfaceY"); var hw = so.FindProperty("halfWidth");
            cx.arraySize = Count; sy.arraySize = Count; hw.arraySize = Count;
            for (int i = 0; i < Count; i++)
            {
                float z = ZStart + i * ZStep; var r = KH_CourseGroundEditor.RiverAt(z);
                cx.GetArrayElementAtIndex(i).floatValue = r.x;
                sy.GetArrayElementAtIndex(i).floatValue = r.y;
                hw.GetArrayElementAtIndex(i).floatValue = KH_CourseGroundEditor.HalfWidth(z);
            }
            var bz = so.FindProperty("bridgeZ"); bz.arraySize = bridges.Length;
            for (int i = 0; i < bridges.Length; i++) bz.GetArrayElementAtIndex(i).floatValue = bridges[i];
            so.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(data); EditorUtility.SetDirty(data);

            foreach (var w in wanders) report.wanderWired += Wire(w, data);
            foreach (var f in fetches) report.fetchWired += Wire(f, data);
            foreach (var t in recalls) report.toyRecallWired += Wire(t, data);

            // Adding an Udon object can leave dangling NetworkID entries (vrc-scene-edit-safety).
            var descriptor = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
            if (descriptor != null)
            {
                report.droppedNetworkIds = descriptor.NetworkIDCollection.RemoveAll(x => x.gameObject == null && (x.SerializedTypeNames == null || x.SerializedTypeNames.Count == 0));
                if (report.droppedNetworkIds > 0) EditorUtility.SetDirty(descriptor);
            }
            EditorSceneManager.MarkSceneDirty(dogs.scene);
            if (!EditorSceneManager.SaveScene(dogs.scene)) throw new IOException("River data scene save failed");
            report.status = "BUILT"; report.samples = Count; report.bridgeZ = bridges;
            report.reason = "Saved. Run 74 - Verify River Data (saved state) separately.";
            Write("river-data-build.json", JsonUtility.ToJson(report, true));
            Debug.Log("[KH River Data] built samples=" + Count + " bridges=" + string.Join(",", bridges) + " wired wander=" + report.wanderWired + " fetch=" + report.fetchWired + " toyRecall=" + report.toyRecallWired);
        }

        static int Wire(UdonSharpBehaviour target, KH_RiverData data)
        {
            var so = new SerializedObject(target); var p = so.FindProperty("riverData");
            if (p == null) throw new InvalidOperationException(target.GetType().Name + ".riverData field missing (compile first)");
            p.objectReferenceValue = data; so.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(target); EditorUtility.SetDirty(target);
            return 1;
        }

        static float[] BridgeZ()
        {
            var ext = GameObject.Find("KH_CourseExtension");
            if (ext == null) throw new InvalidOperationException("KH_CourseExtension missing");
            var list = new List<float>();
            foreach (Transform c in ext.transform) if (c.name.StartsWith("Bridge_")) list.Add(c.position.z);
            if (list.Count != 2) throw new InvalidOperationException("Expected 2 bridges, found " + list.Count);
            list.Sort(); return list.ToArray();
        }

        [MenuItem("Tools/KominkaHillside/Course/74 - Verify River Data")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = new VerifyReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            var node = GameObject.Find(DogsRoot + "/" + NodeName);
            var data = node == null ? null : node.GetComponent<KH_RiverData>();
            if (data == null) throw new InvalidOperationException("KH_Dogs/KH_RiverData missing; run 73 - Build River Data");
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(data);
            if (backing == null || backing.SyncMethod != Networking.SyncType.None) r.findings.Add("KH_RiverData backing missing or not sync None");
            if (!data._Ready() || data.centerX.Length != Count || Mathf.Abs(data.zStart - ZStart) > 1e-4f || Mathf.Abs(data.zStep - ZStep) > 1e-4f)
                throw new InvalidOperationException("KH_RiverData arrays not built (expected " + Count + " samples at 1 m from z 0)");
            r.samples = data.centerX.Length;
            for (int i = 0; i < Count; i++)
                if (float.IsNaN(data.centerX[i]) || float.IsNaN(data.surfaceY[i]) || float.IsNaN(data.halfWidth[i]) || data.halfWidth[i] <= 0f) { r.findings.Add("Invalid sample " + i); break; }

            // Array vs generating formula (the baked values, and the linear interpolation between them).
            for (int i = 0; i < Count; i++)
            {
                float z = ZStart + i * ZStep; var f = KH_CourseGroundEditor.RiverAt(z);
                r.formulaMaxCenterXAtSamples = Mathf.Max(r.formulaMaxCenterXAtSamples, Mathf.Abs(data.centerX[i] - f.x));
                r.formulaMaxSurfaceYAtSamples = Mathf.Max(r.formulaMaxSurfaceYAtSamples, Mathf.Abs(data.surfaceY[i] - f.y));
                r.formulaMaxHalfWidthAtSamples = Mathf.Max(r.formulaMaxHalfWidthAtSamples, Mathf.Abs(data.halfWidth[i] - KH_CourseGroundEditor.HalfWidth(z)));
                if (i == Count - 1) continue;
                float m = z + ZStep * .5f; var g = KH_CourseGroundEditor.RiverAt(m);
                r.formulaMaxCenterXAtMidpoints = Mathf.Max(r.formulaMaxCenterXAtMidpoints, Mathf.Abs(data._CenterX(m) - g.x));
                r.formulaMaxSurfaceYAtMidpoints = Mathf.Max(r.formulaMaxSurfaceYAtMidpoints, Mathf.Abs(data._SurfaceY(m) - g.y));
                r.formulaMaxHalfWidthAtMidpoints = Mathf.Max(r.formulaMaxHalfWidthAtMidpoints, Mathf.Abs(data._HalfWidth(m) - KH_CourseGroundEditor.HalfWidth(m)));
            }
            if (r.formulaMaxCenterXAtSamples > 1e-3f || r.formulaMaxSurfaceYAtSamples > 1e-3f || r.formulaMaxHalfWidthAtSamples > 1e-3f)
                r.findings.Add("Baked samples differ from the current formula (rebuild with 73 or the formula changed)");

            // Check 1: saved water-surface meshes.
            var water = GameObject.Find("KH_CourseExtension/RiverWater");
            if (water == null) throw new InvalidOperationException("KH_CourseExtension/RiverWater missing");
            r.waterChunks = water.transform.childCount;
            if (r.waterChunks != 24) r.findings.Add("Expected 24 water chunks, found " + r.waterChunks);
            foreach (Transform chunk in water.transform)
            {
                var mf = chunk.GetComponent<MeshFilter>(); var mesh = mf == null ? null : mf.sharedMesh;
                if (mesh == null) { r.findings.Add("Water chunk without mesh " + chunk.name); continue; }
                var v = mesh.vertices; const int cols = 9;
                if (v.Length % cols != 0) { r.findings.Add("Unexpected water topology " + chunk.name + " vertices=" + v.Length); continue; }
                for (int row = 0; row < v.Length / cols; row++)
                {
                    var centre = chunk.TransformPoint(v[row * cols + cols / 2]);
                    for (int c = 0; c < cols; c++)
                    {
                        var p = chunk.TransformPoint(v[row * cols + c]); r.waterVertices++;
                        if (!data._Covers(p.z)) { r.findings.Add("Water vertex outside baked z " + p); continue; }
                        Track(ref r.maxSurfaceYError, ref r.worstSurfaceY, Mathf.Abs(p.y - data._SurfaceY(p.z)), "surfaceY " + chunk.name, p);
                        if (c == cols / 2) Track(ref r.maxCenterXError, ref r.worstCenterX, Mathf.Abs(p.x - data._CenterX(p.z)), "centerX " + chunk.name, p);
                        if (c == 0 || c == cols - 1)
                            Track(ref r.maxHalfWidthError, ref r.worstHalfWidth, Mathf.Abs(Mathf.Abs(p.x - centre.x) - WaterMeshMargin - data._HalfWidth(p.z)), "halfWidth " + chunk.name, p);
                    }
                }
            }
            if (r.maxSurfaceYError > TolSurfaceY) r.findings.Add("Check 1: surface y error " + r.maxSurfaceYError.ToString("F4") + " > " + TolSurfaceY);
            if (r.maxCenterXError > TolCenterX) r.findings.Add("Check 1: centre x error " + r.maxCenterXError.ToString("F4") + " > " + TolCenterX);
            if (r.maxHalfWidthError > TolHalfWidth) r.findings.Add("Check 1: half width error " + r.maxHalfWidthError.ToString("F4") + " > " + TolHalfWidth);

            // Check 6: along the riverbed exclusion edge (centre ± (half width + 1 m)), from 0.5 m inside to 2 m outside,
            // is there NavMesh below the water surface? Query at the ground under each point; only a NavMesh hit at that
            // same x/z (within 0.1 m) counts as "the NavMesh point here".
            const int mask = (1 << 0) | (1 << 11);
            for (float z = ZStart; z <= ZStart + (Count - 1) * ZStep + 1e-3f; z += 1f)
            {
                float surface = data._SurfaceY(z), centre = data._CenterX(z), edge = data._HalfWidth(z) + KH_RiverData.BandMargin;
                bool nearBridge = data.bridgeZ != null && data.bridgeZ.Any(b => Mathf.Abs(z - b) < 6f);
                foreach (int side in new[] { -1, 1 })
                    for (float o = -.5f; o <= 2.0001f; o += .25f)
                    {
                        var q = new Vector3(centre + side * (edge + o), surface + 5f, z); RaycastHit ground;
                        if (!Physics.Raycast(q, Vector3.down, out ground, 12f, mask, QueryTriggerInteraction.Ignore)) continue;
                        r.edgeQueries++; NavMeshHit hit;
                        if (!NavMesh.SamplePosition(ground.point, out hit, .5f, NavMesh.AllAreas)) continue;
                        var d = hit.position - ground.point; d.y = 0; if (d.magnitude > .1f) continue;
                        r.edgeNavPoints++;
                        float above = hit.position.y - data._SurfaceY(hit.position.z);
                        r.edgeMinAboveSurface = Mathf.Min(r.edgeMinAboveSurface, above);
                        if (above < 0f)
                        {
                            r.edgeUnderwater++;
                            if (r.underwater.Count < 200) r.underwater.Add(new EdgePoint { nav = hit.position, surfaceY = data._SurfaceY(hit.position.z), below = -above, side = side, nearBridge = nearBridge });
                        }
                    }
            }
            if (r.edgeUnderwater > 0) r.findings.Add("Check 6: " + r.edgeUnderwater + " NavMesh points below the water surface at the exclusion edge (see underwater[])");

            // Wiring (proxy side: vrc-udonsharp §4, heap may be empty until save/build).
            foreach (var w in UnityEngine.Object.FindObjectsOfType<KominkaDogRun.KD_DogWanderController>(true)) if (w.riverData == data) r.wanderWired++;
            foreach (var f in UnityEngine.Object.FindObjectsOfType<KominkaDogRun.KD_DogFetchController>(true)) if (f.riverData == data) r.fetchWired++;
            foreach (var t in UnityEngine.Object.FindObjectsOfType<KH_ToyRecall>(true)) if (t.riverData == data) r.toyRecallWired++;
            if (r.wanderWired != 6 || r.fetchWired != 6 || r.toyRecallWired != 1)
                r.findings.Add("Wiring incomplete wander=" + r.wanderWired + "/6 fetch=" + r.fetchWired + "/6 toyRecall=" + r.toyRecallWired + "/1");

            r.errors = r.findings.Count; r.status = r.errors == 0 ? "PASS" : "FAIL";
            Write("river-data-verify.json", JsonUtility.ToJson(r, true));
            Debug.Log("[KH River Data] verify " + r.status + " errors=" + r.errors + " check1 y=" + r.maxSurfaceYError.ToString("F4") + " x=" + r.maxCenterXError.ToString("F4")
                + " hw=" + r.maxHalfWidthError.ToString("F4") + " check6 navPoints=" + r.edgeNavPoints + " underwater=" + r.edgeUnderwater);
        }

        static void Track(ref float max, ref Worst worst, float error, string what, Vector3 at)
        { if (error > max) { max = error; worst = new Worst { what = what, error = error, at = at }; } }

        static void Write(string name, string json) { Directory.CreateDirectory(OutDir); File.WriteAllText(OutDir + "/" + name, json); }
    }
}
