using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;

namespace KominkaDogRun.Editor
{
    /// <summary>Callable steps for the CandyStep copied-scene builder. Never opens/saves a scene.</summary>
    public static partial class KD_CandyDogSetupEditor
    {
        public const string ScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_CandyDogPerformance.cs";
        public const string ProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_CandyDogPerformance.asset";
        private const string Models = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/CandyStep/";
        public static readonly string[] DogKeys = { "Lab", "Shiba", "Dachs", "Pome", "Corgi", "Samoyed" };
        private static readonly string[] ModelKeys = { "DonutBeret", "PawMedallion", "CandyClip" };
        private static readonly string[] ReferenceKeys = { "Crown", "BowTie", "TailRibbon" };
        private static readonly float[] WidthRatios = { 1.65f, 1.55f, 1.1f };

        [Serializable]
        private class CuePlan
        {
            public float[] cueTimes;
            public int[] cueMasks;
            public float[] cueYaw;
            public float[] cueSteps;
            public Vector3[] dogOrigins;
            public float sitTime = 115.2f;
            public float duration = 124.8f;
        }

        public static string CreateProgramAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath) != null) return "Program already exists";
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptPath);
            if (script == null) throw new InvalidOperationException("Import the script before creating its program");
            var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = script;
            AssetDatabase.CreateAsset(asset, ProgramPath);
            AssetDatabase.SaveAssets();
            return "Created Candy dog program. Wait for Udon compilation before Setup.";
        }

        public static KD_CandyDogPerformance Setup(GameObject previewRoot, PlayableDirector director,
            Transform formationAnchor, string cueJsonPath)
        {
            GuardPreview(previewRoot);
            if (director == null || director.gameObject.scene != previewRoot.scene || director.playableAsset == null)
                throw new InvalidOperationException("A populated director in the copied scene is required");
            if (formationAnchor == null || formationAnchor.gameObject.scene != previewRoot.scene)
                throw new InvalidOperationException("A formation anchor in the copied scene is required");
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath) == null)
                throw new InvalidOperationException("Create the program asset, compile, then run Setup");
            var plan = JsonUtility.FromJson<CuePlan>(File.ReadAllText(cueJsonPath));
            if (plan == null || plan.cueTimes == null || plan.cueTimes.Length == 0
                || plan.cueMasks == null || plan.cueYaw == null
                || plan.cueTimes.Length != plan.cueMasks.Length || plan.cueTimes.Length != plan.cueYaw.Length)
                throw new InvalidOperationException("Invalid body cue plan");
            if (plan.dogOrigins == null || plan.dogOrigins.Length != 6)
                throw new InvalidOperationException("The body cue plan must contain six shared stage-local dogOrigins");
            for (int i = 0; i < plan.cueTimes.Length; i++)
                if (plan.cueMasks[i] < 1 || plan.cueMasks[i] > 63 || plan.cueTimes[i] < 0
                    || (i > 0 && plan.cueTimes[i] < plan.cueTimes[i - 1]))
                    throw new InvalidOperationException("Cue masks/times are invalid");
            // Resolve all six before creating any scene objects.
            var dogs = new KD_DogWanderController[6];
            var rigs = new KD_DogRig[6];
            for (int i = 0; i < 6; i++)
            {
                var dog = KD_SceneObjects.Find("KD_Dog_" + DogKeys[i], out int count);
                if (count != 1 || dog == null) throw new InvalidOperationException("Expected one dog: " + DogKeys[i]);
                dogs[i] = UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true)
                    .SingleOrDefault(w => w.gameObject.scene == previewRoot.scene && w.dogRoot == dog.transform);
                if (dogs[i] == null) throw new InvalidOperationException("Missing wander controller: " + DogKeys[i]);
                rigs[i] = KD_DogRig.Resolve(dog);
                if (!rigs[i].Ok) throw new InvalidOperationException("Invalid rig: " + DogKeys[i]);
            }
            var material = EnsureMaterial();
            var models = ModelKeys.Select(k => AssetDatabase.LoadAssetAtPath<GameObject>(Models + "KD_CandyStep_" + k + ".fbx")).ToArray();
            if (models.Any(m => m == null)) throw new InvalidOperationException("All three CandyStep FBX models must be imported");
            var existing = previewRoot.GetComponentsInChildren<KD_CandyDogPerformance>(true);
            if (existing.Length > 1) throw new InvalidOperationException("Duplicate Candy coordinator");
            var performance = existing.SingleOrDefault();
            if (performance == null)
            {
                var go = new GameObject("KD_CandyDogPerformance");
                go.transform.SetParent(previewRoot.transform, false);
                performance = go.AddUdonSharpComponent<KD_CandyDogPerformance>();
            }
            var slots = new Transform[6];
            // Same local frame/origins as generated particles. Audience is at local -Z.
            for (int i = 0; i < 6; i++)
            {
                var name = "KD_CandySlot_" + DogKeys[i];
                slots[i] = formationAnchor.Find(name);
                if (slots[i] != null) continue; // Artist adjustments survive repeated Setup.
                slots[i] = new GameObject(name).transform;
                slots[i].SetParent(formationAnchor, false);
                slots[i].localPosition = plan.dogOrigins[i];
                slots[i].localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
            var costume = new GameObject[18];
            for (int p = 0; p < 3; p++)
            for (int d = 0; d < 6; d++)
            {
                var slot = p == 0 ? KD_AccessorySetupEditor.Slot.Head : p == 1 ? KD_AccessorySetupEditor.Slot.Chest : KD_AccessorySetupEditor.Slot.Tail;
                var anchor = KD_AccessorySetupEditor.AnchorFor(rigs[d], slot);
                if (anchor == null) throw new InvalidOperationException("Missing accessory anchor " + DogKeys[d] + "/" + slot);
                string name = "KD_CandyCostume_" + ModelKeys[p] + "_" + DogKeys[d];
                var node = KD_SceneObjects.Find(name, out int count);
                if (count > 1) throw new InvalidOperationException("Duplicate costume node: " + name);
                if (node == null)
                {
                    var reference = KD_SceneObjects.Find("KD_Acc_" + ReferenceKeys[p] + "_" + DogKeys[d]);
                    if (reference == null || reference.transform.parent != anchor)
                        throw new InvalidOperationException("Expected existing fitted accessory: " + ReferenceKeys[p] + "/" + DogKeys[d]);
                    node = new GameObject(name);
                    node.transform.SetParent(anchor, false);
                    node.transform.position = reference.transform.position;
                    node.transform.rotation = reference.transform.rotation;
                    // Keep imported FBX axis conversion on its own child.
                    var art = (GameObject)PrefabUtility.InstantiatePrefab(models[p]);
                    art.transform.SetParent(node.transform, false);
                    node.transform.localScale = Vector3.one;
                    Bounds bounds = BoundsIn(node.transform);
                    if (bounds.size.x <= .00001f) throw new InvalidOperationException("Empty imported model: " + ModelKeys[p]);
                    float scale = (WidthRatios[p] * rigs[d].headScale) / (bounds.size.x * Mathf.Abs(anchor.lossyScale.x));
                    node.transform.localScale = Vector3.one * scale;
                }
                if (node.transform.parent != anchor) throw new InvalidOperationException("Costume anchor changed: " + name);
                foreach (var renderer in node.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = material;
                node.SetActive(false);
                costume[p * 6 + d] = node;
            }
            performance.previewSceneOnly = true;
            performance.director = director;
            performance.dogs = dogs;
            performance.slots = slots;
            performance.costumeNodes = costume;
            performance.accessoryBox = UnityEngine.Object.FindObjectsOfType<KD_AccessoryBox>(true).Single(a => a.gameObject.scene == previewRoot.scene);
            performance.activities = UnityEngine.Object.FindObjectsOfType<KD_GroupActivities>(true).SingleOrDefault(a => a.gameObject.scene == previewRoot.scene);
            performance.cueTimes = plan.cueTimes;
            performance.cueMasks = plan.cueMasks;
            performance.cueYaw = plan.cueYaw;
            performance.cueSteps = plan.cueSteps ?? new float[0];
            performance.sitTime = plan.sitTime;
            performance.duration = plan.duration;
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.Hold;
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(performance);
            if (backing == null) throw new InvalidOperationException("Missing Udon backing after Setup");
            backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
            UdonSharpEditorUtility.CopyProxyToUdon(performance);
            EditorUtility.SetDirty(performance);
            EditorUtility.SetDirty(backing);
            EditorSceneManager.MarkSceneDirty(previewRoot.scene);
            return performance;
        }

        public static string Verify(KD_CandyDogPerformance performance)
        {
            if (performance == null) throw new ArgumentNullException(nameof(performance));
            GuardPreview(performance.gameObject);
            var report = new StringBuilder();
            var problems = new List<string>();
            if (!performance.previewSceneOnly) problems.Add("Preview opt-in is off");
            if (performance.dogs == null || performance.dogs.Length != 6 || performance.slots == null || performance.slots.Length != 6)
                problems.Add("Incomplete roster");
            else for (int i = 0; i < 6; i++)
            {
                var dog = performance.dogs[i];
                if (dog == null || performance.slots[i] == null) { problems.Add("Missing dog/slot " + i); continue; }
                if (dog.dogRoot.name != "KD_Dog_" + DogKeys[i]) problems.Add("Wrong roster order " + i);
                report.AppendLine(DogKeys[i] + " slot=" + performance.slots[i].position.ToString("F3") + " dogScale=" + dog.dogRoot.lossyScale.ToString("F3"));
            }
            if (performance.costumeNodes == null || performance.costumeNodes.Length != 18) problems.Add("Need 18 costume nodes");
            else foreach (var node in performance.costumeNodes)
            {
                if (node == null) { problems.Add("Missing costume node"); continue; }
                int triangles = node.GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh == null ? 0 : f.sharedMesh.triangles.Length / 3);
                int rendererCount = node.GetComponentsInChildren<Renderer>(true).Length;
                report.AppendLine(node.name + " renderers=" + rendererCount + " triangles=" + triangles + " worldScale=" + node.transform.lossyScale.ToString("F3"));
                if (node.activeSelf || rendererCount != 1 || triangles <= 0) problems.Add("Invalid costume node " + node.name);
            }
            if (performance.director == null || performance.director.playOnAwake) problems.Add("Director would bypass preparation");
            if (performance.accessoryBox == null) problems.Add("No ordinary outfit ledger");
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(performance);
            if (backing == null || backing.SyncMethod != VRC.SDKBase.Networking.SyncType.None) problems.Add("Incorrect preview sync mode");
            report.AppendLine("RESULT " + (problems.Count == 0 ? "PASS (construction only; appearance/playback requires runtime review)" : "FAIL"));
            foreach (var problem in problems) report.AppendLine(problem);
            return report.ToString();
        }

        /// <summary>
        /// Explicit, one-time review corrections for four fitted nodes. Setup never calls this.
        /// Baseline markers keep the pre-review transforms; an existing marker means leave the
        /// current artist-adjusted transform untouched, rather than adding offsets again.
        /// </summary>
        public static string ApplyReviewedFittings()
        {
            const string scenePath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scenes/CandyStepPreview.unity";
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                UnityEngine.SceneManagement.SceneManager.sceneCount != 1 || scene.path != scenePath)
                throw new InvalidOperationException("Reviewed Candy fittings require the stopped, compiled CandyStepPreview scene only.");
            string[] keys = { "Shiba", "Corgi", "Pome", "Corgi" };
            string[] parts = { "DonutBeret", "DonutBeret", "PawMedallion", "PawMedallion" };
            var nodes = new Transform[4]; var rigs = new KD_DogRig[4];
            // Resolve and validate the entire bounded target set before making any edits.
            for (int i = 0; i < keys.Length; i++)
            {
                string name = "KD_CandyCostume_" + parts[i] + "_" + keys[i];
                var node = KD_SceneObjects.Find(name, out int count);
                var dog = KD_SceneObjects.Find("KD_Dog_" + keys[i], out int dogCount);
                if (node == null || count != 1 || dog == null || dogCount != 1 || node.scene != scene || dog.scene != scene)
                    throw new InvalidOperationException("Expected one copied-scene fitting and dog: " + name);
                nodes[i] = node.transform; rigs[i] = KD_DogRig.Resolve(dog);
                if (nodes[i].parent == null || !rigs[i].Ok || rigs[i].headScale <= .001f || !dog.activeInHierarchy)
                    throw new InvalidOperationException("Invalid fitted rig: " + name);
                var marker = nodes[i].parent.Find("KD_CandyFitV1_" + parts[i] + "_" + keys[i]);
                if (marker != null && !marker.CompareTag("EditorOnly"))
                    throw new InvalidOperationException("Review baseline name is occupied by a non-EditorOnly object: " + marker.name);
            }
            var report = new StringBuilder("Candy reviewed fittings v1 — transforms only, no scene save\n");
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("CandyStep reviewed accessory fittings");
            int changed = 0;
            try
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    var node = nodes[i]; var dog = rigs[i].dog.transform;
                    string markerName = "KD_CandyFitV1_" + parts[i] + "_" + keys[i];
                    if (node.parent.Find(markerName) != null)
                    { report.AppendLine("PRESERVED (already applied/manual edits retained): " + node.name); continue; }
                    var baseline = new GameObject(markerName) { tag = "EditorOnly" };
                    Undo.RegisterCreatedObjectUndo(baseline, "Keep pre-review Candy fit");
                    baseline.transform.SetParent(node.parent, false);
                    baseline.transform.localPosition = node.localPosition;
                    baseline.transform.localRotation = node.localRotation;
                    baseline.transform.localScale = node.localScale;
                    Undo.RecordObject(node, "Adjust reviewed Candy fit");
                    Vector3 before = node.position; Quaternion beforeRotation = node.rotation;
                    float scale = rigs[i].headScale;
                    if (parts[i] == "DonutBeret")
                    {
                        // Twist around the model's thin axis so the pink half faces front
                        // without reversing any existing pitch. Then add a small viewerward tip.
                        var size = BoundsIn(node).size;
                        Vector3 thin = size.x < size.y && size.x < size.z ? Vector3.right :
                            size.y < size.z ? Vector3.up : Vector3.forward;
                        Vector3 normal = node.TransformDirection(thin).normalized;
                        if (Vector3.Dot(normal, dog.up) < 0) normal = -normal;
                        node.rotation = Quaternion.AngleAxis(8f, dog.right) * Quaternion.AngleAxis(180f, normal) * beforeRotation;
                        node.position = before + dog.up * Mathf.Clamp(scale * .10f, .003f, .008f) + dog.forward * (scale * .04f);
                    }
                    else
                    {
                        // Bring the existing band/medallion just outside visible chest fur.
                        // Keep size and height so the biscuit does not grow into the short legs.
                        float forward = keys[i] == "Pome" ? scale * .12f : scale * .07f;
                        node.position = before + dog.forward * Mathf.Clamp(forward, .003f, .009f);
                    }
                    changed++;
                    report.AppendLine(node.name + " world " + before.ToString("F4") + " -> " + node.position.ToString("F4") +
                        " rotationDelta=" + Quaternion.Angle(beforeRotation, node.rotation).ToString("F2") +
                        " baseline=" + baseline.name + " scale unchanged=" + node.localScale.ToString("F4"));
                }
                Undo.CollapseUndoOperations(group);
                if (changed > 0) EditorSceneManager.MarkSceneDirty(scene);
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            report.AppendLine("Changed=" + changed + "; recapture all affected views before acceptance. Save only after independent verification.");
            return report.ToString();
        }

        private static void GuardPreview(GameObject root)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Setup/Verify requires Edit mode");
            if (root == null || !root.scene.IsValid() || root.scene != EditorSceneManager.GetActiveScene()
                || root.scene.path == "Assets/Scenes/KominkaDogRun.unity"
                || (!(root.scene.path.Contains("CandyStep") && root.scene.path.Contains("Preview"))
                    && root.scene.path != KD_PumpkinPreviewEditor.ScenePath))
                throw new InvalidOperationException("CandyStep Preview scene copy required; source scene is never modified");
        }

        private static Material EnsureMaterial()
        {
            var path = Models + "M_KD_CandyStep.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var color = AssetDatabase.LoadAssetAtPath<Texture2D>(Models + "CandyStep_BaseColor.png");
            if (color == null) throw new InvalidOperationException("CandyStep base-color texture is missing");
            var normalPath = Models + "CandyStep_Normal.png";
            var importer = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            { importer.textureType = TextureImporterType.NormalMap; importer.SaveAndReimport(); }
            material = new Material(Shader.Find("Standard"));
            material.name = "M_KD_CandyStep";
            material.mainTexture = color;
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", .28f);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (normal != null) { material.SetTexture("_BumpMap", normal); material.EnableKeyword("_NORMALMAP"); }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Bounds BoundsIn(Transform root)
        {
            bool first = true;
            var result = new Bounds();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var b = filter.sharedMesh.bounds;
                var matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (first) { result = new Bounds(p, Vector3.zero); first = false; } else result.Encapsulate(p);
                }
            }
            return result;
        }
    }
}
