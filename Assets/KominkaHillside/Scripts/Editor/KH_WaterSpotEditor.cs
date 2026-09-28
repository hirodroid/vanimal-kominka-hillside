using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KominkaDogRun;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using TMPro;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    // IMP-DOG-020 steps 2-3 (DES-DOG-004 A-1, A-3, B-3 checks 2-5). Production scene only, idle Edit mode, saved.
    //   76 Build : 1st run creates the KH_WaterSpot program asset only (compile U#, then run again).
    //              Then: KH_Dogs/KH_WaterSpot (region, entry, exits, return points, 0.5 m riverbed grid) beside the destination
    //              "川辺" on the east (outer) bank of the bend, z 285 ± 6 m; splash / ripple particle systems and their
    //              generated textures; wiring (6 dogs: waterSpot, waterMouthHeight, FX; the menu: waterSpot); the course
    //              translations re-imported (KH_CourseTravelEditor.ApplyCatalog); a trail painted from the pass point to the
    //              entry (Terrain layer 2 = sand, max with the existing weight; TerrainData backed up first). Saves the scene.
    //              Paint only: no collider, no NavMesh or occlusion change. Course/15 would repaint the trail away (run 76 again).
    //   77 Verify: checks 2-5 and wiring, never writes the scene -> Temp/imp-dog-020/water-spot-verify.json.
    public static class KH_WaterSpotEditor
    {
        const string ProgramPath = "Assets/KominkaHillside/Scripts/KH_WaterSpot";
        const string OutDir = "Temp/imp-dog-020";
        const string DogsRoot = "KH_Dogs", NodeName = "KH_WaterSpot";
        const string FxDir = "Assets/KominkaHillside/Data/Water";
        static readonly string[] Breeds = { "Corgi", "Pome", "Lab", "Dachs", "Shiba", "Samoyed" };   // dogIndex order (KH_DogWalkingEditor.Keys)
        // Step 4 breed table (decision 2026-09-27): small breeds swim, Shiba on trial (decided after the PCVR look), Lab and
        // Samoyed wade only. Written to KD_DogWanderController.waterSwimAllowed.
        static readonly bool[] Swims = { true, true, false, true, true, false };
        // Ms = M x KH_WaterSpot.swimMouthScale; the water-play tuning values have one source, KH_WaterSpot (Codex M-低).
        const int RiverbankDestination = 5;         // KH_WalkRoute.destinationNodes[5] = "川辺" (KH_CourseRouteEditor)
        const float CentreZ = 285f, EntryLimit = 15f,   // decision 2026-09-27: 12 -> 15 m (the trigger no longer depends on where the leash comes off)
                    TriggerRadius = 25f;
        // Step 5 (decision 2026-09-27, 2nd): the region runs along the bend's bank, z 270-300 as a guide, in 1.5 m slices. A slice is
        // taken only when it passes every check (water slope <= 5.05 %, outside the bridge zones, a bank exit on the NavMesh with a
        // slope <= 25 deg, a return point with a complete path from the pass point, no stone collider in its water); the longest
        // run of passing slices becomes the region. The entry is the passing exit nearest the pass point; exits every 5 m.
        const float SliceZ0 = 270f, SliceZ1 = 300f, SliceStep = 1.5f, ExitEvery = 5f;
        const float Step = .5f, InnerLat = .3f, OuterExtra = .6f, ReturnOut = 2.5f;
        const int GroundMask = (1 << 0) | (1 << 11);

        [Serializable] public class BuildReport
        {
            public string status, utc, scene, reason;
            public Vector3 passPoint, entry;
            public Vector3[] exits, returns;
            public float entryDistance, entryLateral;
            public int gridNx, gridNz, wetCells, dogsWired, paintedCells;
            public float[] mouthHeights;
            public bool pouchWired;
            public string[] bobTargets;   // per dog: the visual child that carries the swim bob, or why there is none
            public float zFrom, zTo; public int markerStones; public bool sign, toyRecallWired;
            public List<Slice> slices = new List<Slice>();
        }
        [Serializable] public class Slice
        {
            public float z, slope, lateral, exitSlopeDeg;
            public bool slopeOk, bridgeOk, exitOk, returnOk, pathOk, stonesOk, ok, adopted;
            public Vector3 exit, ret;
        }
        // swimBandOk (width >= 2.5 m for a swimming breed, B-3 check 5) belongs to step 4 (swimming): recorded, not judged here.
        [Serializable] public class Breed { public string name; public float mouth, wadeLimit, wadeCells, wadeWidth, swimCells, swimWidth; public bool swims, swimBandOk; public int blockedLines; }
        [Serializable] public class VerifyReport
        {
            public string status, utc, scene;
            public int errors, gridCells, stonesInRegion;
            public float check2MaxBedError, entryDistance, surfaceSlopeMax, surfaceSlopeMean;
            public float[] exitSlopeDeg;
            public bool[] exitsOnNav, returnsOnNav, returnsPathComplete;
            public bool entryOnNav, bridgeOverlap;
            public List<Breed> breeds = new List<Breed>();
            public List<string> stones = new List<string>(), findings = new List<string>();
            public int dogsWired; public bool pouchWired;
            public List<string> bobWiring = new List<string>();
            public List<string> signTooBig = new List<string>(); public float signWorstHeight;
            public string stage1SwimCheck = "Step 4: every swimming breed needs at least one swimmable cell (depth >= 0.75 M) in the region (judged). The design's swim band >= 2.5 m is recorded only (breeds[].swimBandOk) because the spot has no pool (decision 2026-09-27)";
            public string slopeTolerance = "Check 4 limit 5 % (design); judged at <= 5.05 %: the formula gives exactly 5.00 % here and float rounding must not flip it. Values above 5.00 % are reported in findings as a note when <= 5.05 %";
            public List<string> notes = new List<string>();
            public List<Slice> slices = new List<Slice>();
            public int markerStones; public bool signWired, toyRecallWired;
            public string limitations = "Static data only. Water play itself: ClientSim harness W0/W4/W6 (Course/78) and PCVR. Swim widths are recorded for step 4 only.";
        }

        [MenuItem("Tools/KominkaHillside/Course/76 - Build Water Spot")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath + ".asset") == null)
            {
                var created = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                created.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(ProgramPath + ".cs");
                if (created.sourceCsScript == null) throw new InvalidOperationException("KH_WaterSpot.cs not imported yet; refresh first");
                AssetDatabase.CreateAsset(created, ProgramPath + ".asset"); AssetDatabase.SaveAssets();
                UdonSharpProgramAsset.CompileAllCsPrograms(true);
                Write("water-spot-build.json", JsonUtility.ToJson(new BuildReport { status = "PROGRAM_ASSET_CREATED", reason = "Scene unchanged. Compile U#, then run 76 again." }, true));
                return;
            }
            var report = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            var dogs = GameObject.Find(DogsRoot); if (dogs == null) throw new InvalidOperationException("KH_Dogs missing");
            var riverNode = dogs.transform.Find("KH_RiverData"); var river = riverNode == null ? null : riverNode.GetComponent<KH_RiverData>();
            if (river == null || !river._Ready()) throw new InvalidOperationException("KH_RiverData missing: run Course/73 first");
            var pouches = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true);
            if (pouches.Length != 1 || pouches[0].manager == null || pouches[0].manager.route == null || pouches[0].language == null) throw new InvalidOperationException("Exactly one wired menu required");
            var pouch = pouches[0];
            var wanders = UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true).OrderBy(w => w.dogIndex).ToArray();
            if (wanders.Length != 6 || wanders.Select(w => w.dogIndex).Distinct().Count() != 6 || wanders.Any(w => w.dogIndex < 0 || w.dogIndex > 5)) throw new InvalidOperationException("Six wander behaviours with dogIndex 0-5 required");
            var mouths = Breeds.Select(ReadMouth).ToArray();
            report.mouthHeights = mouths;

            // ---- geometry: slices along the bank (step 5) ----
            var pass = PassPoint(pouch.manager.route); report.passPoint = pass;
            var slices = ComputeSlices(pass); report.slices = slices;
            var run = slices.Where(x => x.adopted).ToList();
            if (run.Count < 3) throw new InvalidOperationException("Fewer than 3 passing slices along the bank (see water-spot-build.json slices); nothing changed");
            report.zFrom = run[0].z - SliceStep * .5f; report.zTo = run[run.Count - 1].z + SliceStep * .5f;
            var entrySlice = run.OrderBy(x => Flat(x.exit - pass)).First();
            var entry = entrySlice.exit; float entryLat = entrySlice.lateral; report.entry = entry; report.entryLateral = entryLat;
            report.entryDistance = Flat(entry - pass);
            var chosen = new List<Slice> { entrySlice };
            foreach (var sl in run) if (chosen.All(c => Mathf.Abs(c.z - sl.z) >= ExitEvery - .01f)) chosen.Add(sl);
            var exits = chosen.Select(x => x.exit).ToList(); var returns = chosen.Select(x => x.ret).ToList();
            report.exits = exits.ToArray(); report.returns = returns.ToArray();
            var poly = Polygon(run);
            float minX = poly.Min(v => v.x), maxX = poly.Max(v => v.x), minZ = poly.Min(v => v.y), maxZ = poly.Max(v => v.y);
            float x0 = Mathf.Floor(minX / Step) * Step, z0 = Mathf.Floor(minZ / Step) * Step;
            int nx = Mathf.CeilToInt((maxX - x0) / Step) + 1, nz = Mathf.CeilToInt((maxZ - z0) / Step) + 1;
            var bed = new float[nx * nz];
            for (int iz = 0; iz < nz; iz++) for (int ix = 0; ix < nx; ix++)
            {
                float x = x0 + ix * Step, z = z0 + iz * Step;
                // The whole bounding box is baked, so the bilinear lookup near the region's edge never meets a missing corner
                // (Codex 2026-09-27 D-中). Inside / outside is decided by the polygon alone.
                bed[iz * nx + ix] = Ground(x, z);
                if (Inside(poly, x, z) && KH_CourseGroundEditor.RiverAt(z).y - bed[iz * nx + ix] > .02f) report.wetCells++;
            }
            report.gridNx = nx; report.gridNz = nz;

            // ---- data object ----
            var node = dogs.transform.Find(NodeName);
            if (node == null) { var go = new GameObject(NodeName); go.transform.SetParent(dogs.transform, false); node = go.transform; }
            var spot = node.GetComponent<KH_WaterSpot>(); if (spot == null) spot = node.gameObject.AddUdonSharpComponent<KH_WaterSpot>();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(spot);
            if (backing == null) throw new InvalidOperationException("KH_WaterSpot backing missing (compile U# and run again)");
            if (backing.SyncMethod != Networking.SyncType.None) { backing.SyncMethod = Networking.SyncType.None; EditorUtility.SetDirty(backing); }
            var so = new SerializedObject(spot);
            so.FindProperty("riverData").objectReferenceValue = river;
            SetFloats(so, "polyX", poly.Select(v => v.x).ToArray()); SetFloats(so, "polyZ", poly.Select(v => v.y).ToArray());
            so.FindProperty("entry").vector3Value = entry;
            SetVectors(so, "exits", exits.ToArray()); SetVectors(so, "returns", returns.ToArray());
            so.FindProperty("gridX0").floatValue = x0; so.FindProperty("gridZ0").floatValue = z0; so.FindProperty("gridStep").floatValue = Step;
            so.FindProperty("gridNx").intValue = nx; so.FindProperty("gridNz").intValue = nz;
            SetFloats(so, "bed", bed); so.FindProperty("entryRadius").floatValue = TriggerRadius;
            // Tuning: the constants in KH_WaterSpot are the single source; written every build so the scene never keeps an old value.
            so.FindProperty("wadeLimitRatio").floatValue = KH_WaterSpot.DefaultWadeLimitRatio;
            so.FindProperty("swimEnterRatio").floatValue = KH_WaterSpot.DefaultSwimEnterRatio;
            so.FindProperty("swimLeaveRatio").floatValue = KH_WaterSpot.DefaultSwimLeaveRatio;
            so.FindProperty("swimLeaveHold").floatValue = KH_WaterSpot.DefaultSwimLeaveHold;
            so.FindProperty("swimClear").floatValue = KH_WaterSpot.DefaultSwimClear;
            so.FindProperty("swimSpeedLab").floatValue = KH_WaterSpot.DefaultSwimSpeedLab;
            so.FindProperty("swimMouthScale").floatValue = KH_WaterSpot.DefaultSwimMouthScale;
            so.FindProperty("wadeClear").floatValue = KH_WaterSpot.DefaultWadeClear;
            so.FindProperty("swimBob").floatValue = KH_WaterSpot.DefaultSwimBob;
            so.FindProperty("swimBobHz").floatValue = KH_WaterSpot.DefaultSwimBobHz;
            so.FindProperty("carryExtra").floatValue = KH_WaterSpot.DefaultCarryExtra;
            // PCVR 2026-09-28: per-dog afloat sink / nose-up pitch (dogIndex order Corgi, Pome, Lab, Dachs, Shiba, Samoyed)
            SetFloats(so, "swimSink", new[] { KH_WaterSpot.DefaultSwimSinkSmall, KH_WaterSpot.DefaultSwimSinkPome, KH_WaterSpot.DefaultSwimSinkSmall, KH_WaterSpot.DefaultSwimSinkSmall, KH_WaterSpot.DefaultSwimSinkSmall, KH_WaterSpot.DefaultSwimSinkSmall });
            SetFloats(so, "swimPitch", new[] { 0f, KH_WaterSpot.DefaultSwimPitchPome, 0f, 0f, 0f, 0f });
            so.FindProperty("floatSink").floatValue = KH_WaterSpot.DefaultFloatSink;
            so.FindProperty("floatDrift").floatValue = KH_WaterSpot.DefaultFloatDrift;
            // Step 5 marker: stones beside the entry and the temporary sign (its text follows the viewer's language at run time)
            var signLabel = BuildMarkers(node, entry, pass, pouch, report);
            so.FindProperty("language").objectReferenceValue = pouch.language;
            so.FindProperty("signLabel").objectReferenceValue = signLabel;
            so.FindProperty("signShadow").objectReferenceValue = signShadowBuilt;
            so.FindProperty("signKey").stringValue = "course.water.sign";
            so.ApplyModifiedProperties(); UdonSharpEditorUtility.CopyProxyToUdon(spot); EditorUtility.SetDirty(spot);

            // ---- FX ----
            ParticleSystem splash, ripple; BuildFx(node, out splash, out ripple);

            // ---- wiring ----
            foreach (var w in wanders)
            {
                var ws = new SerializedObject(w);
                ws.FindProperty("waterSpot").objectReferenceValue = spot;
                ws.FindProperty("waterMouthHeight").floatValue = mouths[w.dogIndex];
                ws.FindProperty("waterSwimAllowed").boolValue = Swims[w.dogIndex];
                ws.FindProperty("waterSwimMouth").floatValue = SwimMouth(mouths[w.dogIndex], w.dogIndex);
                string why; var bob = BobTarget(w, out why);
                string hipsWhy = ""; var hips = bob == null ? HipsTarget(w, out hipsWhy) : null;
                ws.FindProperty("waterBob").objectReferenceValue = bob;
                ws.FindProperty("waterBobHips").objectReferenceValue = hips;
                if (report.bobTargets == null) report.bobTargets = new string[6];
                report.bobTargets[w.dogIndex] = bob != null ? bob.name : hips != null ? "hips (LateUpdate): " + hips.name : "none: " + why + "; " + hipsWhy;
                ws.FindProperty("waterSplash").objectReferenceValue = splash;
                ws.FindProperty("waterRipple").objectReferenceValue = ripple;
                ws.ApplyModifiedProperties(); UdonSharpEditorUtility.CopyProxyToUdon(w); EditorUtility.SetDirty(w); report.dogsWired++;
            }
            var ps = new SerializedObject(pouch); ps.FindProperty("waterSpot").objectReferenceValue = spot; ps.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(pouch); EditorUtility.SetDirty(pouch); report.pouchWired = true;
            // Step 6: the toy recall floats a ball that lands in the spot
            foreach (var t in UnityEngine.Object.FindObjectsOfType<KH_ToyRecall>(true))
            { var ts = new SerializedObject(t); ts.FindProperty("waterSpot").objectReferenceValue = spot; ts.ApplyModifiedProperties(); UdonSharpEditorUtility.CopyProxyToUdon(t); EditorUtility.SetDirty(t); report.toyRecallWired = true; }
            KH_CourseTravelEditor.ApplyCatalog(pouch);

            // ---- trail ----
            report.paintedCells = PaintTrail(pass, entry);

            var descriptor = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
            if (descriptor != null && descriptor.NetworkIDCollection.RemoveAll(x => x.gameObject == null && (x.SerializedTypeNames == null || x.SerializedTypeNames.Count == 0)) > 0) EditorUtility.SetDirty(descriptor);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(dogs.scene);
            if (!EditorSceneManager.SaveScene(dogs.scene)) throw new IOException("Water spot scene save failed");
            report.status = "BUILT"; report.reason = "Saved. Run 27 (glyphs) and 77 - Verify Water Spot separately.";
            Write("water-spot-build.json", JsonUtility.ToJson(report, true));
            Debug.Log("[KH Water Spot] built entry=" + entry + " distance=" + report.entryDistance.ToString("F2") + " grid=" + nx + "x" + nz + " wet=" + report.wetCells + " painted=" + report.paintedCells);
        }

        // ============================================================ geometry

        static Vector3 PassPoint(KH_WalkRoute route)
        {
            int node = route._DestinationNode(RiverbankDestination);
            if (node < 0 || route.nodePositions == null || node >= route.nodePositions.Length) throw new InvalidOperationException("川辺 destination node missing");
            return route.nodePositions[node];
        }

        static float BandEdge(float z) { return KH_CourseGroundEditor.HalfWidth(z) + KH_RiverData.BandMargin; }

        /// <summary>Step 5: the bank in 1.5 m slices, each checked on its own; the longest run of passing slices is adopted.</summary>
        static List<Slice> ComputeSlices(Vector3 pass)
        {
            NavMeshHit from; bool fromOk = NavMesh.SamplePosition(pass, out from, 1f, NavMesh.AllAreas);
            var path = new NavMeshPath(); var list = new List<Slice>();
            for (float z = SliceZ0; z <= SliceZ1 + .001f; z += SliceStep)
            {
                var sl = new Slice { z = z }; float cx = KH_CourseGroundEditor.RiverAt(z).x, edge = BandEdge(z), surface = KH_CourseGroundEditor.RiverAt(z).y;
                sl.slope = Mathf.Abs(KH_CourseGroundEditor.RiverAt(z + SliceStep * .5f).y - KH_CourseGroundEditor.RiverAt(z - SliceStep * .5f).y) / SliceStep;
                sl.slopeOk = sl.slope <= .0505f;
                sl.bridgeOk = true;
                foreach (float bz in new[] { 190f, 490f })
                {
                    float bx = KH_CourseGroundEditor.RiverAt(bz).x;
                    if (Mathf.Abs(z - bz) <= 8f + SliceStep && cx + edge + 4f >= bx - 18f && cx <= bx + 18f) sl.bridgeOk = false;
                }
                for (float o = 1f; o <= 4.001f && !sl.exitOk; o += .25f)
                {
                    Vector3 e;
                    if (OnBank(new Vector3(cx + edge + o, 0, z), .3f, out e)) { sl.exit = e; sl.lateral = e.x - cx; sl.exitOk = true; }
                }
                if (sl.exitOk)
                {
                    RaycastHit g;
                    if (Physics.Raycast(sl.exit + Vector3.up, Vector3.down, out g, 3, GroundMask, QueryTriggerInteraction.Ignore)) sl.exitSlopeDeg = Vector3.Angle(g.normal, Vector3.up);
                    if (sl.exitSlopeDeg > 25f) sl.exitOk = false;
                }
                if (sl.exitOk)
                {
                    Vector3 r; sl.returnOk = OnBank(sl.exit + new Vector3(ReturnOut, 0, 0), 1f, out r); sl.ret = r;
                    NavMeshHit rh;
                    sl.pathOk = sl.returnOk && fromOk && NavMesh.SamplePosition(r, out rh, .3f, NavMesh.AllAreas)
                        && NavMesh.CalculatePath(from.position, rh.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                }
                float width = (sl.exitOk ? sl.lateral : edge) + OuterExtra;
                var box = Physics.OverlapBox(new Vector3(cx + width * .5f, surface, z), new Vector3(width * .5f, 1.5f, SliceStep * .5f), Quaternion.identity, 1 << 11, QueryTriggerInteraction.Ignore);
                sl.stonesOk = !box.Any(c => !(c is TerrainCollider));
                sl.ok = sl.slopeOk && sl.bridgeOk && sl.exitOk && sl.returnOk && sl.pathOk && sl.stonesOk;
                list.Add(sl);
            }
            int bestStart = -1, bestLen = 0;
            for (int i = 0; i < list.Count; )
            {
                if (!list[i].ok) { i++; continue; }
                int j = i; while (j < list.Count && list[j].ok) j++;
                if (j - i > bestLen) { bestLen = j - i; bestStart = i; }
                i = j;
            }
            for (int k = 0; k < bestLen; k++) list[bestStart + k].adopted = true;
            return list;
        }

        /// <summary>NavMesh point near (x, z) at ground height, outside the river band.</summary>
        static bool OnBank(Vector3 xz, float radius, out Vector3 nav)
        {
            nav = Vector3.zero; RaycastHit g; NavMeshHit h;
            float s = KH_CourseGroundEditor.RiverAt(xz.z).y;
            if (!Physics.Raycast(new Vector3(xz.x, s + 10, xz.z), Vector3.down, out g, 30, GroundMask, QueryTriggerInteraction.Ignore)) return false;
            if (!NavMesh.SamplePosition(g.point, out h, radius, NavMesh.AllAreas)) return false;
            if (Mathf.Abs(h.position.x - KH_CourseGroundEditor.RiverAt(h.position.z).x) <= BandEdge(h.position.z)) return false;
            nav = h.position; return true;
        }

        /// <summary>Region over the adopted slices: inner edge centre + 0.3 m, outer edge that slice's bank lateral + 0.6 m, capped
        /// half a slice beyond both ends.</summary>
        static List<Vector2> Polygon(List<Slice> run)
        {
            var zs = new List<float> { run[0].z - SliceStep * .5f }; zs.AddRange(run.Select(x => x.z)); zs.Add(run[run.Count - 1].z + SliceStep * .5f);
            var inner = new List<Vector2>(); var outer = new List<Vector2>();
            foreach (float z in zs)
            {
                float cx = KH_CourseGroundEditor.RiverAt(z).x;
                var near = run.OrderBy(x => Mathf.Abs(x.z - z)).First();
                inner.Add(new Vector2(cx + InnerLat, z)); outer.Add(new Vector2(cx + near.lateral + OuterExtra, z));
            }
            outer.Reverse(); inner.AddRange(outer); return inner;
        }

        /// <summary>Step 5 marker: 3 stone clusters beside the entry (the course's own bank-stone meshes and material, no collider so
        /// the NavMesh is unchanged) and a temporary board with a WorldSpace TMP label on the path side. Rebuilt every run.</summary>
        static TextMeshPro BuildMarkers(Transform spotNode, Vector3 entry, Vector3 pass, KD_TreatPouch pouch, BuildReport report)
        {
            var old = spotNode.Find("Marker"); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var marker = new GameObject("Marker").transform; marker.SetParent(spotNode, false);
            string scenery = KH_CourseGroundEditor.Dir + "/Scenery";
            var stoneMat = AssetDatabase.LoadAssetAtPath<Material>(scenery + "/KH_CourseStones.mat");
            var offsets = new[] { new Vector3(.5f, 0, 1.3f), new Vector3(.5f, 0, -1.3f), new Vector3(1.1f, 0, -.7f) };
            for (int i = 0; i < offsets.Length; i++)
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(scenery + "/KH_StoneCluster_" + (i % 4) + ".asset");
                if (mesh == null || stoneMat == null) continue;
                var p = entry + offsets[i]; p.y = Ground(p.x, p.z) - .05f;
                var go = new GameObject("Stone_" + i); go.transform.SetParent(marker, false); go.transform.position = p;
                go.transform.rotation = Quaternion.Euler(0, 40 * i, 0); go.transform.localScale = Vector3.one * .55f; go.layer = 11;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = stoneMat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                report.markerStones++;
            }
            // The board: 1.8 m inland of the entry, facing the pass point (the path it is read from).
            var signPos = entry + new Vector3(1.8f, 0, 0); signPos.y = Ground(signPos.x, signPos.z);
            var toReader = pass - signPos; toReader.y = 0;
            var sign = new GameObject("Sign").transform; sign.SetParent(marker, false); sign.position = signPos;
            sign.rotation = Quaternion.LookRotation(-toReader.normalized, Vector3.up);   // TMP faces -z: the reader looks along +z
            var wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/KominkaHillside/Data/Environment/River/KH_RiverWood.mat");
            Transform Part(string name, Vector3 pos, Vector3 size)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube); c.name = name;
                UnityEngine.Object.DestroyImmediate(c.GetComponent<Collider>());   // no walking obstacle, no NavMesh change
                c.transform.SetParent(sign, false); c.transform.localPosition = pos; c.transform.localScale = size;
                var r = c.GetComponent<MeshRenderer>(); if (wood != null) r.sharedMaterial = wood; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                return c.transform;
            }
            // Codex 2026-09-27 U-中 (water-spot-marker-close.png: "あそび" left the board, low contrast): the board 1.4 x, the text
            // auto-sized into it (min 0.6), light unbleached #F2E8D0 over a dark copy just behind it. Course/77 checks every
            // language's sign text fits (SignFits).
            Part("Post", new Vector3(0, .55f, .03f), new Vector3(.08f, 1.1f, .08f));
            Part("Board", new Vector3(0, SignBoardY, 0), new Vector3(SignBoardW, SignBoardH, .04f));
            TextMeshPro Text(string name, Vector3 local, Color color)
            {
                var go = new GameObject(name); go.transform.SetParent(sign, false); go.transform.localPosition = local;
                var t = go.AddComponent<TextMeshPro>();
                if (pouch.sectionTitleLabel != null) t.font = pouch.sectionTitleLabel.font;
                t.text = "犬の水あそび"; t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = true;
                t.enableAutoSizing = true; t.fontSizeMin = .6f; t.fontSizeMax = 3f;
                t.color = color; t.rectTransform.sizeDelta = new Vector2(SignTextW, SignTextH);
                return t;
            }
            var shadow = Text("LabelShadow", new Vector3(.012f, SignBoardY - .012f, -.025f), new Color(.12f, .08f, .04f, 1f));
            var label = Text("Label", new Vector3(0, SignBoardY, -.03f), new Color(0xF2 / 255f, 0xE8 / 255f, 0xD0 / 255f, 1f));
            signShadowBuilt = shadow;
            report.sign = true;
            return label;
        }
        const float SignBoardW = .9f * 1.4f, SignBoardH = .42f * 1.4f, SignBoardY = 1.05f, SignTextW = SignBoardW - .08f, SignTextH = SignBoardH - .06f;
        static TextMeshPro signShadowBuilt;

        [Serializable] class SignCatalog { public SignLang[] languages; }
        [Serializable] class SignLang { public string code; public SignText[] texts; }
        [Serializable] class SignText { public string key, text; }

        /// <summary>77: every language's course.water.sign, auto-sized, stays inside the text box on the board (height and width).
        /// The label's text is put back afterwards.</summary>
        static List<string> SignFits(TextMeshPro label, out float worstHeight)
        {
            var bad = new List<string>(); worstHeight = 0f;
            var cat = JsonUtility.FromJson<SignCatalog>(File.ReadAllText(KH_CourseMenuEditor.CatalogPath));
            string keep = label.text;
            var box = label.rectTransform.rect;
            foreach (var l in cat.languages)
            {
                var e = l.texts == null ? null : l.texts.FirstOrDefault(x => x.key == "course.water.sign");
                if (e == null) { bad.Add(l.code + ": no sign text"); continue; }
                label.text = e.text; label.ForceMeshUpdate();
                var b = label.textBounds.size; worstHeight = Mathf.Max(worstHeight, b.y);
                if (b.y > box.height + .001f || b.x > box.width + .001f || label.isTextOverflowing)
                    bad.Add(l.code + ": " + b.x.ToString("F3") + " x " + b.y.ToString("F3") + " m in " + box.width.ToString("F3") + " x " + box.height.ToString("F3") + " (font " + label.fontSize.ToString("F2") + ")");
            }
            label.text = keep; label.ForceMeshUpdate();
            return bad;
        }

        static bool Inside(List<Vector2> poly, float x, float z)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > z) != (poly[j].y > z) && x < poly[i].x + (z - poly[i].y) * (poly[j].x - poly[i].x) / (poly[j].y - poly[i].y)) inside = !inside;
            return inside;
        }

        static Terrain TerrainAt(float x, float z)
        {
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Terrain>())
            {
                var p = t.transform.position; var s = t.terrainData.size;
                if (x >= p.x && x <= p.x + s.x && z >= p.z && z <= p.z + s.z) return t;
            }
            return null;
        }

        static float Ground(float x, float z)
        {
            var t = TerrainAt(x, z); if (t == null) throw new InvalidOperationException("No terrain at " + x + "," + z);
            return t.SampleHeight(new Vector3(x, 0, z)) + t.transform.position.y;
        }

        static float Flat(Vector3 v) { v.y = 0; return v.magnitude; }

        static float ReadMouth(string breed)
        {
            string path = "Assets/KominkaHillside/Data/Frisbee/KH_Jump_" + breed + ".json";
            var m = Regex.Match(File.ReadAllText(path), "\"mouthOffsetStand\"\\s*:\\s*\\[\\s*[-0-9.eE]+\\s*,\\s*([-0-9.eE]+)");
            if (!m.Success) throw new InvalidOperationException("mouthOffsetStand missing in " + path);
            float v = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (v < .05f || v > 1f) throw new InvalidOperationException("Implausible mouth height " + v + " in " + path);
            return v;
        }

        // ============================================================ FX (no asset in the project; generated, TreatFx style)

        static void BuildFx(Transform spot, out ParticleSystem splash, out ParticleSystem ripple)
        {
            Directory.CreateDirectory(FxDir);
            var dot = MakeTexture(FxDir + "/KH_WaterDot.png", r => Mathf.Clamp01(1 - r) * Mathf.Clamp01(1 - r));
            var ring = MakeTexture(FxDir + "/KH_WaterRing.png", r => Mathf.Exp(-Mathf.Pow((r - .72f) / .12f, 2)));
            var dotMat = MakeMaterial(FxDir + "/KH_WaterSplash.mat", dot, new Color(.85f, .93f, 1f, .7f));
            var ringMat = MakeMaterial(FxDir + "/KH_WaterRipple.mat", ring, new Color(.9f, .95f, 1f, .45f));
            var fx = spot.Find("Fx"); if (fx == null) { var go = new GameObject("Fx"); go.transform.SetParent(spot, false); fx = go.transform; }
            splash = MakeParticle(fx, "Splash", dotMat, true); ripple = MakeParticle(fx, "Ripple", ringMat, false);
        }

        static Texture2D MakeTexture(string path, Func<float, float> alpha)
        {
            const int n = 64; var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float r = new Vector2(x + .5f - n / 2f, y + .5f - n / 2f).magnitude / (n / 2f);
                t.SetPixel(x, y, new Color(1, 1, 1, r >= 1 ? 0 : alpha(r)));
            }
            t.Apply(); File.WriteAllBytes(path, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = false; imp.maxTextureSize = 64; imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material MakeMaterial(string path, Texture2D tex, Color tint)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                if (shader == null) throw new InvalidOperationException("Particle shader missing");
                m = new Material(shader); AssetDatabase.CreateAsset(m, path);
            }
            m.mainTexture = tex; m.SetColor("_TintColor", tint); m.enableInstancing = true; EditorUtility.SetDirty(m); return m;
        }

        static ParticleSystem MakeParticle(Transform parent, string name, Material mat, bool isSplash)
        {
            var t = parent.Find(name); if (t == null) { var go = new GameObject(name); go.transform.SetParent(parent, false); t = go.transform; }
            var ps = t.GetComponent<ParticleSystem>(); if (ps == null) ps = t.gameObject.AddComponent<ParticleSystem>();
            t.localRotation = isSplash ? Quaternion.Euler(-90, 0, 0) : Quaternion.identity;
            var main = ps.main;
            main.loop = true; main.playOnAwake = true;   // playing with no rate: particles only through Emit
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = isSplash ? 72 : 12;
            main.startLifetime = isSplash ? .5f : 1.2f;
            main.startSpeed = isSplash ? new ParticleSystem.MinMaxCurve(.6f, 1.4f) : new ParticleSystem.MinMaxCurve(0f);
            main.startSize = isSplash ? new ParticleSystem.MinMaxCurve(.03f, .07f) : new ParticleSystem.MinMaxCurve(.25f);
            main.gravityModifier = isSplash ? 1.5f : 0f;
            var em = ps.emission; em.enabled = true; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = isSplash;
            if (isSplash) { sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = .08f; }
            var size = ps.sizeOverLifetime; size.enabled = !isSplash;
            if (!isSplash) size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, .3f, 1, 1.6f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(isSplash ? .9f : .6f, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = t.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = isSplash ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.HorizontalBillboard;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            EditorUtility.SetDirty(ps); EditorUtility.SetDirty(r);
            return ps;
        }

        // ============================================================ trail (Terrain paint only)

        static string BackupStamp;
        static int PaintTrail(Vector3 a, Vector3 b)
        {
            BackupStamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            int painted = 0; const float width = 1.5f;   // step 5: a wider, denser footpath from the pass point to the entry
            float minX = Mathf.Min(a.x, b.x) - 3, maxX = Mathf.Max(a.x, b.x) + 3, minZ = Mathf.Min(a.z, b.z) - 3, maxZ = Mathf.Max(a.z, b.z) + 3;
            var seg = new Vector2(b.x - a.x, b.z - a.z);
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Terrain>())
            {
                var td = t.terrainData; var p = t.transform.position; var s = td.size;
                if (maxX < p.x || minX > p.x + s.x || maxZ < p.z || minZ > p.z + s.z) continue;
                if (td.alphamapLayers != 4) throw new InvalidOperationException("Expected 4 terrain layers on " + td.name);
                string path = AssetDatabase.GetAssetPath(td);
                // One folder per run, so running 76 again never overwrites the state before an earlier run (Codex 2026-09-27 M-中).
                string backupDir = OutDir + "/terrain-backup/" + BackupStamp; Directory.CreateDirectory(backupDir);
                File.Copy(path, backupDir + "/" + Path.GetFileName(path), false);
                int n = td.alphamapResolution;
                int ix0 = Mathf.Clamp(Mathf.FloorToInt((minX - p.x) / s.x * (n - 1)), 0, n - 1), ix1 = Mathf.Clamp(Mathf.CeilToInt((maxX - p.x) / s.x * (n - 1)), 0, n - 1);
                int iz0 = Mathf.Clamp(Mathf.FloorToInt((minZ - p.z) / s.z * (n - 1)), 0, n - 1), iz1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - p.z) / s.z * (n - 1)), 0, n - 1);
                int w = ix1 - ix0 + 1, h = iz1 - iz0 + 1; var map = td.GetAlphamaps(ix0, iz0, w, h);
                for (int z = 0; z < h; z++) for (int x = 0; x < w; x++)
                {
                    float px = p.x + (ix0 + x) * s.x / (n - 1), pz = p.z + (iz0 + z) * s.z / (n - 1);
                    var q = new Vector2(px - a.x, pz - a.z); float tt = Mathf.Clamp01(Vector2.Dot(q, seg) / seg.sqrMagnitude);
                    float d = (q - seg * tt).magnitude, fine = Mathf.PerlinNoise(px * .29f + 13, pz * .29f + 9);
                    float trail = (1 - KH_CourseGroundEditor.Smooth(width * .35f, width + .7f, d + (fine - .5f) * .75f)) * (.72f + .14f * fine);
                    float old = map[z, x, 2]; if (trail <= old + 1e-4f) continue;
                    float scale = old < .9999f ? (1 - trail) / (1 - old) : 0;
                    for (int l = 0; l < 4; l++) map[z, x, l] = l == 2 ? trail : map[z, x, l] * scale;
                    painted++;
                }
                td.SetAlphamaps(ix0, iz0, map); EditorUtility.SetDirty(td);
            }
            return painted;
        }

        // ============================================================ verify (checks 2-5)

        [MenuItem("Tools/KominkaHillside/Course/77 - Verify Water Spot")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = new VerifyReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            var node = GameObject.Find(DogsRoot + "/" + NodeName); var spot = node == null ? null : node.GetComponent<KH_WaterSpot>();
            if (spot == null || !spot._Ready()) throw new InvalidOperationException("KH_Dogs/KH_WaterSpot missing or not built (run 76)");
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(spot);
            if (backing == null || backing.SyncMethod != Networking.SyncType.None) r.findings.Add("KH_WaterSpot backing missing or not sync None");
            var pouch = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true).Single();
            var pass = PassPoint(pouch.manager.route);
            var poly = spot.polyX.Select((x, i) => new Vector2(x, spot.polyZ[i])).ToList();
            // step 5: every slice's checks and whether it is in the adopted run (recomputed from the saved scene)
            r.slices = ComputeSlices(pass);
            float polyZ0 = poly.Min(v => v.y), polyZ1 = poly.Max(v => v.y);
            foreach (var sl in r.slices) if (sl.z > polyZ0 && sl.z < polyZ1 && !sl.ok) r.findings.Add("Step 5: slice z " + sl.z + " inside the region fails its checks");
            var markerNode = node.transform.Find("Marker");
            r.markerStones = markerNode == null ? 0 : markerNode.GetComponentsInChildren<MeshRenderer>(true).Count(m => m.name.StartsWith("Stone_"));
            r.signWired = spot.signLabel != null && spot.language != null && spot.signKey == "course.water.sign" && spot.signLabel.transform.IsChildOf(node.transform);
            if (r.markerStones < 2 || !r.signWired) r.findings.Add("Step 5: marker incomplete (stones " + r.markerStones + ", sign " + r.signWired + ")");
            if (spot.signShadow == null || !spot.signShadow.transform.IsChildOf(node.transform)) r.findings.Add("Step 5: sign shadow not wired");
            if (spot.signLabel != null)
            {
                r.signTooBig = SignFits(spot.signLabel, out r.signWorstHeight);
                foreach (var b in r.signTooBig) r.findings.Add("Step 5: sign text does not fit the board: " + b);
            }
            r.toyRecallWired = UnityEngine.Object.FindObjectsOfType<KH_ToyRecall>(true).All(t => t.waterSpot == spot);
            if (!r.toyRecallWired) r.findings.Add("Step 6: KH_ToyRecall.waterSpot not wired");

            // check 2: grid vs saved Terrain
            for (int i = 0; i < spot.bed.Length; i++)
            {
                if (spot.bed[i] <= KH_WaterSpot.NoBed + 1) continue;
                var c = spot._CellPoint(i); r.gridCells++;
                r.check2MaxBedError = Mathf.Max(r.check2MaxBedError, Mathf.Abs(c.y - Ground(c.x, c.z)));
            }
            if (r.check2MaxBedError > .01f) r.findings.Add("Check 2: riverbed grid differs from Terrain by " + r.check2MaxBedError.ToString("F4") + " m (> 0.01)");

            // check 3: points on NavMesh, outside the exclusion, complete paths, entry distance
            NavMeshHit h; var path = new NavMeshPath();
            r.entryOnNav = NavMesh.SamplePosition(spot.entry, out h, .3f, NavMesh.AllAreas) && Outside(spot.entry);
            if (!r.entryOnNav) r.findings.Add("Check 3: entry not on bank NavMesh");
            r.entryDistance = Flat(spot.entry - pass);
            if (r.entryDistance > EntryLimit) r.findings.Add("Check 3: entry " + r.entryDistance.ToString("F2") + " m from the 川辺 pass point (> " + EntryLimit.ToString("F0") + ")");
            int n = spot.exits.Length; r.exitsOnNav = new bool[n]; r.returnsOnNav = new bool[n]; r.returnsPathComplete = new bool[n]; r.exitSlopeDeg = new float[n];
            NavMeshHit from; bool fromOk = NavMesh.SamplePosition(pass, out from, 1f, NavMesh.AllAreas);
            if (!fromOk) r.findings.Add("Check 3: pass point not on NavMesh");
            for (int k = 0; k < n; k++)
            {
                r.exitsOnNav[k] = NavMesh.SamplePosition(spot.exits[k], out h, .3f, NavMesh.AllAreas) && Outside(spot.exits[k]);
                r.returnsOnNav[k] = NavMesh.SamplePosition(spot.returns[k], out h, .3f, NavMesh.AllAreas) && Outside(spot.returns[k]);
                r.returnsPathComplete[k] = fromOk && r.returnsOnNav[k] && NavMesh.CalculatePath(from.position, h.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                if (!r.exitsOnNav[k]) r.findings.Add("Check 3: exit " + k + " not on bank NavMesh");
                if (!r.returnsPathComplete[k]) r.findings.Add("Check 3: return point " + k + " not on NavMesh / no complete path from the pass point");
                RaycastHit g;
                if (Physics.Raycast(spot.exits[k] + Vector3.up, Vector3.down, out g, 3, GroundMask, QueryTriggerInteraction.Ignore)) r.exitSlopeDeg[k] = Vector3.Angle(g.normal, Vector3.up);
                // check 5 (part): exit bank slope
                if (r.exitSlopeDeg[k] > 25f) r.findings.Add("Check 5: exit " + k + " slope " + r.exitSlopeDeg[k].ToString("F1") + " deg (> 25)");
            }

            // check 4: bridge no-detour zones, water-surface slope (records; limit 5 %, rounded to 0.1 point)
            float minX = poly.Min(v => v.x), maxX = poly.Max(v => v.x), minZ = poly.Min(v => v.y), maxZ = poly.Max(v => v.y);
            foreach (float bz in new[] { 190f, 490f })
            {
                float bx = KH_CourseGroundEditor.RiverAt(bz).x;
                if (maxX >= bx - 18 && minX <= bx + 18 && maxZ >= bz - 8 && minZ <= bz + 8) r.bridgeOverlap = true;
            }
            if (r.bridgeOverlap) r.findings.Add("Check 4: region overlaps a bridge no-detour zone");
            for (float z = minZ; z < maxZ; z += .5f)
                r.surfaceSlopeMax = Mathf.Max(r.surfaceSlopeMax, Mathf.Abs(KH_CourseGroundEditor.RiverAt(z + .5f).y - KH_CourseGroundEditor.RiverAt(z).y) / .5f);
            r.surfaceSlopeMean = Mathf.Abs(KH_CourseGroundEditor.RiverAt(maxZ).y - KH_CourseGroundEditor.RiverAt(minZ).y) / (maxZ - minZ);
            if (r.surfaceSlopeMax > .0505f) r.findings.Add("Check 4: water-surface slope " + (r.surfaceSlopeMax * 100).ToString("F2") + " % (> 5)");
            else if (r.surfaceSlopeMax > .05f) r.notes.Add("Check 4: water-surface slope " + (r.surfaceSlopeMax * 100).ToString("F3") + " % is above 5 % only by rounding (tolerance 0.05 point)");

            // check 5: per breed wading / swimming widths, straight lines to the exits, stones in the region
            float span = maxZ - minZ;
            for (int b = 0; b < Breeds.Length; b++)
            {
                float m = ReadMouth(Breeds[b]);
                var br = new Breed { name = Breeds[b], mouth = m, wadeLimit = spot.wadeLimitRatio * m, swims = Swims[b] };
                for (int i = 0; i < spot.bed.Length; i++)
                {
                    if (spot.bed[i] <= KH_WaterSpot.NoBed + 1) continue;
                    var c = spot._CellPoint(i); if (!Inside(poly, c.x, c.z)) continue;
                    float d = KH_CourseGroundEditor.RiverAt(c.z).y - c.y;
                    if (d > .02f && d <= br.wadeLimit)
                    {
                        br.wadeCells++;
                        foreach (var e in spot.exits)
                        {
                            RaycastHit hit;
                            if (Physics.Linecast(c + Vector3.up * .3f, e + Vector3.up * .3f, out hit, ~0, QueryTriggerInteraction.Ignore) && !(hit.collider is TerrainCollider)) br.blockedLines++;
                        }
                    }
                    if (d >= spot.swimEnterRatio * m) br.swimCells++;
                }
                br.wadeWidth = br.wadeCells * Step * Step / span; br.swimWidth = br.swimCells * Step * Step / span;
                br.swimBandOk = !br.swims || br.swimWidth >= 2.5f;
                if (!br.swimBandOk) r.notes.Add("Swim band " + br.swimWidth.ToString("F2") + " m < 2.5 m for " + br.name + " (recorded only)");
                if (br.swims && br.swimCells < 1) r.findings.Add("Stage 1: no swimmable cell (depth >= " + spot.swimEnterRatio + " M = " + (spot.swimEnterRatio * m).ToString("F3") + " m) for " + br.name);
                if (br.wadeCells == 0) r.findings.Add("Check 5: no wadeable cell for " + br.name);
                if (br.blockedLines > 0) r.findings.Add("Check 5: " + br.blockedLines + " straight lines to exits blocked for " + br.name);
                r.breeds.Add(br);
            }
            var centre = new Vector3((minX + maxX) / 2, KH_CourseGroundEditor.RiverAt((minZ + maxZ) / 2).y, (minZ + maxZ) / 2);
            foreach (var c in Physics.OverlapBox(centre, new Vector3((maxX - minX) / 2, 1.5f, span / 2), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c is TerrainCollider || !Inside(poly, c.bounds.center.x, c.bounds.center.z)) continue;
                r.stonesInRegion++; if (r.stones.Count < 30) r.stones.Add(c.transform.root.name + "/" + c.name);
            }
            if (r.stonesInRegion > 0) r.findings.Add("Check 5: " + r.stonesInRegion + " colliders inside the region (stones; dogs pass through them visually)");

            // wiring
            foreach (var w in UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true))
                if (w.waterSpot == spot && w.waterMouthHeight > .05f && w.waterSplash != null && w.waterRipple != null && w.riverData != null
                    && w.dogIndex >= 0 && w.dogIndex < Swims.Length && w.waterSwimAllowed == Swims[w.dogIndex]
                    && Mathf.Abs(w.waterSwimMouth - SwimMouth(ReadMouth(Breeds[w.dogIndex]), w.dogIndex)) < .001f) r.dogsWired++;   // Ms = the breed's expected value
            r.pouchWired = pouch.waterSpot == spot;
            // step 7: every swimming breed has exactly one bob target (the mesh skeleton, or the hips of a single-tier rig)
            foreach (var w in UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true))
            {
                if (w.waterSpot != spot || w.dogIndex < 0 || w.dogIndex >= Swims.Length) continue;
                bool one = (w.waterBob != null) != (w.waterBobHips != null);
                bool under = (w.waterBob == null || w.waterBob.IsChildOf(w.dogRoot)) && (w.waterBobHips == null || w.waterBobHips.IsChildOf(w.dogRoot));
                if (Swims[w.dogIndex] && (!one || !under)) r.findings.Add("Bob: dog " + w.dogIndex + " needs exactly one bob target under its root (waterBob / waterBobHips)");
                r.bobWiring.Add(w.dogIndex + ":" + (w.waterBob != null ? "mesh " + w.waterBob.name : w.waterBobHips != null ? "hips (LateUpdate) " + w.waterBobHips.name : "none"));
            }
            if (r.dogsWired != 6 || !r.pouchWired) r.findings.Add("Wiring incomplete dogs=" + r.dogsWired + "/6 pouch=" + r.pouchWired);

            r.errors = r.findings.Count; r.status = r.errors == 0 ? "PASS" : "FAIL";
            Write("water-spot-verify.json", JsonUtility.ToJson(r, true));
            Debug.Log("[KH Water Spot] verify " + r.status + " errors=" + r.errors + " entry=" + r.entryDistance.ToString("F2") + "m slope=" + (r.surfaceSlopeMax * 100).ToString("F2") + "% bed=" + r.check2MaxBedError.ToString("F4"));
        }

        /// <summary>The bob goes on the mesh-side skeleton root of the purchased two-tier rig (KH_DogRig.meshSkelRoot): the clips
        /// animate the driver bones under inu/アーマチュア and RotationConstraints copy only rotations onto the mesh side, so no
        /// Animator write touches that transform's position (run 20260927-072549: the Animator itself is on the synced root). It is
        /// accepted only when no clip has a position curve on its path and it carries no position/parent constraint; otherwise there
        /// is no bob (amplitude 0) and the reason is recorded.</summary>
        /// <summary>Ms: the mouth height with the walk clip = M x scale - the breed's measured head drop (WalkHeadDrop below).</summary>
        // Walk-clip head drop per breed, measured by the swim harness (run 20260927-073904: float root + M 0.060 vs the real mouth
        // anchor in W1; Lab / Samoyed from their wading frames). Dog order Corgi, Pome, Lab, Dachs, Shiba, Samoyed. Pome's 2.6 cm
        // drop left its real mouth 0.021 m above the water in W2.
        static readonly float[] WalkHeadDrop = { 0.008f, 0.026f, 0.057f, 0f, 0.008f, 0.049f };
        static float SwimMouth(float m, int dog) { return m * KH_WaterSpot.DefaultSwimMouthScale - WalkHeadDrop[dog]; }

        static Transform BobTarget(KD_DogWanderController w, out string why)
        {
            why = ""; var anim = w.dogAnimator;
            if (anim == null || w.dogRoot == null) { why = "no animator/root"; return null; }
            var rig = KH_DogRig.Resolve(w.dogRoot.gameObject, "KD_FetchAnchor_Mouth");
            var t = rig.meshSkelRoot;
            if (t == null) { why = "no two-tier mesh skeleton"; return null; }
            if (!t.IsChildOf(w.dogRoot) || t == w.dogRoot) { why = "mesh skeleton outside the root"; return null; }
            if (t.GetComponent<UnityEngine.Animations.PositionConstraint>() != null || t.GetComponent<UnityEngine.Animations.ParentConstraint>() != null) { why = "position constraint on the mesh skeleton root"; return null; }
            var ctrl = anim.runtimeAnimatorController;
            if (ctrl == null) { why = "no controller"; return null; }
            string path = AnimationUtility.CalculateTransformPath(t, anim.transform);
            foreach (var clip in ctrl.animationClips)
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    if (b.path == path && b.propertyName.StartsWith("m_LocalPosition")) { why = "clip " + clip.name + " animates " + path; return null; }
            return t;
        }
        /// <summary>Step 7: a single-tier rig (no mesh skeleton) bobs its hips in LateUpdate, after the Animator: the first child
        /// bone of inu/アーマチュア ("Bone", KH_DogRig.AnimationRoot). Clip position curves there are fine (the offset is added
        /// after the Animator's write); a position / parent constraint is not (it would be evaluated after LateUpdate).</summary>
        static Transform HipsTarget(KD_DogWanderController w, out string why)
        {
            why = "";
            if (w.dogRoot == null) { why = "no root"; return null; }
            var rig = KH_DogRig.Resolve(w.dogRoot.gameObject, "KD_FetchAnchor_Mouth");
            if (rig.srcSkelRoot == null) { why = "no inu/アーマチュア"; return null; }
            var t = rig.AnimationRoot;
            if (t == null) { why = "no hips bone under アーマチュア"; return null; }
            if (!t.IsChildOf(w.dogRoot)) { why = "hips outside the root"; return null; }
            if (t.GetComponent<UnityEngine.Animations.PositionConstraint>() != null || t.GetComponent<UnityEngine.Animations.ParentConstraint>() != null) { why = "position constraint on the hips"; return null; }
            return t;
        }

        static bool Outside(Vector3 p) { return Mathf.Abs(p.x - KH_CourseGroundEditor.RiverAt(p.z).x) > BandEdge(p.z); }

        static void SetFloats(SerializedObject so, string name, float[] v) { var p = so.FindProperty(name); p.arraySize = v.Length; for (int i = 0; i < v.Length; i++) p.GetArrayElementAtIndex(i).floatValue = v[i]; }
        static void SetVectors(SerializedObject so, string name, Vector3[] v) { var p = so.FindProperty(name); p.arraySize = v.Length; for (int i = 0; i < v.Length; i++) p.GetArrayElementAtIndex(i).vector3Value = v[i]; }
        static void Write(string name, string json) { Directory.CreateDirectory(OutDir); File.WriteAllText(OutDir + "/" + name, json); }
    }
}
