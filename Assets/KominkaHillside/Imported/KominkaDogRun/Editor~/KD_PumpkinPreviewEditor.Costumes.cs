using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UdonSharpEditor;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PumpkinPreviewEditor
    {
        private const string CostumeAssets = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/PumpkinParade/";
        private static readonly string[] CostumeKeys = { "WitchHat", "PumpkinBrooch", "BatTail" };
        private static readonly string[] OldCostumeKeys = { "DonutBeret", "PawMedallion", "CandyClip" };

        public static void SurveyCostumes()
        {
            GuardStage();
            var p = One<KD_CandyDogPerformance>();
            File.WriteAllText(Evidence + "/costume-before.json", JsonConvert.SerializeObject(new {
                scene = ScenePath, dirty = SceneManager.GetActiveScene().isDirty,
                sourceSha256 = Hash(SourcePath), previewSha256 = Hash(ScenePath),
                lights = UnityEngine.Object.FindObjectsOfType<Light>(true).Count(l => l.gameObject.scene == SceneManager.GetActiveScene()),
                costumes = p.costumeNodes.Select(n => new { n.name, parent = n.transform.parent.name,
                    position = Vec(n.transform.position), rotation = Vec(n.transform.eulerAngles), scale = Vec(n.transform.lossyScale),
                    bounds = CostumeBounds(n), material = AssetDatabase.GetAssetPath(n.GetComponentInChildren<Renderer>(true).sharedMaterial) })
            }, Formatting.Indented));
        }

        public static void PrepareCostumeAssets()
        {
            GuardProject();
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stopped editor required.");
            foreach (var key in CostumeKeys)
            {
                var importer = AssetImporter.GetAtPath(CostumeAssets + "KD_Pumpkin_" + key + ".fbx") as ModelImporter;
                if (importer == null) throw new InvalidOperationException("Costume FBX missing: " + key);
                importer.importAnimation = false; importer.importCameras = false; importer.importLights = false;
                importer.animationType = ModelImporterAnimationType.None; importer.addCollider = false;
                importer.isReadable = true; // Editor fitting/geometry verification; no runtime mesh writes.
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.SaveAndReimport();
            }
            foreach (string file in new[] { "CostumeBase.png", "CostumeEmission.png" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(CostumeAssets + file);
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.None;
                ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear;
                ti.textureCompression = TextureImporterCompression.Uncompressed; ti.maxTextureSize = 256;
                ti.SaveAndReimport();
            }
            var shader = Shader.Find("KominkaDogRun/Pumpkin Costume");
            if (shader == null) throw new InvalidOperationException("Import the costume shader first.");
            string matPath = CostumeAssets + "KD_PumpkinCostume.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "KD_PumpkinCostume" };
                mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(CostumeAssets + "CostumeBase.png"));
                mat.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CostumeAssets + "CostumeEmission.png"));
                mat.SetFloat("_EmissionGain", 0); mat.SetFloat("_Glossiness", .12f);
                mat.renderQueue = KD_LiveSetupEditor.LiveParticleQueue;
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                AssetDatabase.CreateAsset(mat, matPath);
            }
            AssetDatabase.SaveAssets();
        }

        public static void WireCostumes()
        {
            GuardStage();
            var scene = SceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("Save the current preview before the first costume installation.");
            if (!File.Exists(Evidence + "/costume-before.json")) SurveyCostumes();
            if (!File.Exists(Evidence + "/before-costumes.unity")) File.Copy(ScenePath, Evidence + "/before-costumes.unity");
            var p = One<KD_CandyDogPerformance>();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(CostumeAssets + "KD_PumpkinCostume.mat");
            if (mat == null) throw new InvalidOperationException("Prepare costume assets first.");
            var nodes = new GameObject[18]; var renderers = new Renderer[18];
            for (int part = 0; part < 3; part++)
            for (int d = 0; d < 6; d++)
            {
                string dogKey = KD_CandyDogSetupEditor.DogKeys[d], name = "KD_PumpkinCostume_" + CostumeKeys[part] + "_" + dogKey;
                var old = KD_SceneObjects.Find("KD_CandyCostume_" + OldCostumeKeys[part] + "_" + dogKey);
                if (old == null) throw new InvalidOperationException("Missing original fitted reference: " + dogKey);
                var node = KD_SceneObjects.Find(name, out int count);
                if (count > 1) throw new InvalidOperationException("Duplicate Pumpkin costume.");
                if (node == null)
                {
                    var dog = p.dogs[d].dogRoot; var rig = KD_DogRig.Resolve(dog.gameObject);
                    if (!rig.Ok) throw new InvalidOperationException("Rig missing: " + dogKey);
                    Vector3 up = dog.up, forward = Vector3.ProjectOnPlane(rig.nose.position - rig.head.position, up).normalized;
                    var oldBounds = WorldCostumeBounds(old);
                    node = new GameObject(name); node.transform.SetParent(old.transform.parent, false);
                    node.transform.SetPositionAndRotation(oldBounds.center, Quaternion.LookRotation(part == 2 ? -forward : forward, up));
                    node.transform.localScale = Vector3.one;
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CostumeAssets + "KD_Pumpkin_" + CostumeKeys[part] + ".fbx");
                    if (prefab == null) throw new InvalidOperationException("Missing model " + CostumeKeys[part]);
                    var art = (GameObject)PrefabUtility.InstantiatePrefab(prefab); art.transform.SetParent(node.transform, false);
                    float oldWidth = ExtentAlong(old, dog.right);
                    float width = oldWidth * (part == 0 ? 1.08f : part == 1 ? 1.05f : 1.45f);
                    float nativeWidth = ExtentAlong(node, dog.right);
                    if (nativeWidth < .0001f) throw new InvalidOperationException("Empty model bounds");
                    node.transform.localScale *= width / nativeWidth;
                    var bounds = WorldCostumeBounds(node);
                    Vector3 target = oldBounds.center;
                    if (part == 0) target += up * (bounds.extents.y - oldBounds.extents.y * .7f);
                    if (part == 1) target += forward * .006f;
                    if (part == 2) target += up * .008f - forward * .012f;
                    node.transform.position += target - bounds.center;
                }
                if (node.transform.parent != old.transform.parent) throw new InvalidOperationException("Costume anchor differs.");
                var renderer = node.GetComponentsInChildren<Renderer>(true).Single();
                renderer.sharedMaterial = mat; renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true; renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                node.SetActive(false); old.SetActive(false);
                nodes[part * 6 + d] = node; renderers[part * 6 + d] = renderer;
            }
            p.costumeNodes = nodes; p.costumeGlowRenderers = renderers;
            // The ordinary ledger still owns Candy products. Its overlay hides them;
            // this preview coordinator alone enables the separate Halloween nodes.
            p.accessoryBox.candyPerformanceNodes = nodes;
            UdonSharpEditorUtility.CopyProxyToUdon(p.accessoryBox);
            UdonSharpEditorUtility.CopyProxyToUdon(p);
            EditorUtility.SetDirty(p.accessoryBox); EditorUtility.SetDirty(p);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        }

        public static void InspectCostumeMeshes()
        {
            GuardProject();
            foreach (var key in CostumeKeys)
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(CostumeAssets + "KD_Pumpkin_" + key + ".fbx");
                var f = root.GetComponentInChildren<MeshFilter>(true); var m = f.sharedMesh;
                File.WriteAllText(Evidence + "/mesh-" + key + ".json", JsonConvert.SerializeObject(new {
                    vertices = m.vertices.Select(Vec), normals = m.normals.Select(Vec),
                    uv = m.uv.Select(v => new[] { v.x, v.y }), triangles = m.triangles,
                    rotation = Vec(f.transform.localEulerAngles), scale = Vec(f.transform.localScale)
                }));
            }
        }

        public static void FitCostumeBrooches()
        {
            GuardStage();
            var p = One<KD_CandyDogPerformance>();
            for (int d = 0; d < 6; d++)
            {
                var dog = p.dogs[d].dogRoot; var rig = KD_DogRig.Resolve(dog.gameObject);
                var node = p.costumeNodes[6 + d];
                var old = KD_SceneObjects.Find("KD_CandyCostume_PawMedallion_" + KD_CandyDogSetupEditor.DogKeys[d]);
                Vector3 forward = Vector3.ProjectOnPlane(rig.nose.position - rig.head.position, dog.up).normalized;
                var center = WorldCostumeBounds(node).center;
                float width = ExtentAlong(old, dog.right) * .82f;
                node.transform.localScale *= width / ExtentAlong(node, dog.right);
                node.transform.position += center - WorldCostumeBounds(node).center;
                KD_AccessorySetsEditor.LiftFromSurface(node.transform, dog.gameObject, forward, dog.up, .012f);
                EditorUtility.SetDirty(node.transform);
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        // Fit to the chest at the brooch centre, not the muzzle/chin above its footprint.
        public static string FitFloatingBrooches(bool apply = false)
        {
            GuardStage();
            var p = One<KD_CandyDogPerformance>();
            var records = new List<object>();
            foreach (int d in new[] { 1, 2, 4 })
            {
                var dog = p.dogs[d].dogRoot;
                var rig = KD_DogRig.Resolve(dog.gameObject);
                var node = p.costumeNodes[6 + d];
                var forward = Vector3.ProjectOnPlane(rig.nose.position - rig.head.position, dog.up).normalized;
                var right = Vector3.Cross(dog.up, forward);
                var center = WorldCostumeBounds(node).center;
                float halfWidth = ExtentAlong(node, right) * .22f;
                float halfHeight = ExtentAlong(node, dog.up) * .12f;
                var candidates = new List<Vector3>();
                foreach (var skin in dog.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh == null) continue;
                    var mesh = new Mesh();
                    try
                    {
                        // Imported 50x/100x rigs require the same scale handling as BakedBodyBounds.
                        skin.BakeMesh(mesh, true);
                        var vertices = mesh.vertices.Select(v => skin.transform.TransformPoint(v)).ToArray();
                        var triangles = mesh.triangles;
                        for (int x = -1; x <= 1; x++)
                        for (int y = -1; y <= 1; y++)
                        {
                            var origin = center + right * halfWidth * x + dog.up * halfHeight * y + forward;
                            float closest = 2f;
                            for (int i = 0; i < triangles.Length; i += 3)
                            {
                                float hit = BroochRayTriangle(origin, -forward, vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]);
                                if (hit >= 0 && hit < closest) closest = hit;
                            }
                            if (closest < 2f) candidates.Add(origin - forward * closest);
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(mesh); }
                }
                if (candidates.Count < 3) throw new InvalidOperationException("Insufficient chest surface samples: " + dog.name);
                float surface = candidates.Max(v => Vector3.Dot(v, forward));
                float back = node.GetComponentsInChildren<MeshFilter>(true)
                    .SelectMany(f => f.sharedMesh.vertices.Select(v => Vector3.Dot(f.transform.TransformPoint(v), forward))).Min();
                float shift = surface - .004f - back;
                if (Mathf.Abs(shift) > .3f) throw new InvalidOperationException("Chest correction exceeds fitting limit: " + dog.name);
                var before = node.transform.localPosition;
                if (apply)
                {
                    Undo.RecordObject(node.transform, "Fit Pumpkin chest brooch");
                    node.transform.position += forward * shift;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(node.transform);
                    EditorUtility.SetDirty(node.transform);
                }
                records.Add(new { dog = dog.name, samples = candidates.Count, shift, before = Vec(before), after = Vec(node.transform.localPosition) });
            }
            if (apply) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            string json = JsonConvert.SerializeObject(new { apply, records }, Formatting.Indented);
            File.WriteAllText(Evidence + (apply ? "/brooch-fit-applied.json" : "/brooch-fit-survey.json"), json);
            return json;
        }

        private static float BroochRayTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
        {
            var e1 = b - a; var e2 = c - a; var h = Vector3.Cross(direction, e2);
            float det = Vector3.Dot(e1, h);
            if (Mathf.Abs(det) < .0000001f) return -1;
            float inv = 1f / det; var s = origin - a;
            float u = inv * Vector3.Dot(s, h);
            if (u < 0 || u > 1) return -1;
            var q = Vector3.Cross(s, e1); float v = inv * Vector3.Dot(direction, q);
            if (v < 0 || u + v > 1) return -1;
            return inv * Vector3.Dot(e2, q);
        }

        public static void VerifyCostumes()
        {
            GuardStage();
            var p = One<KD_CandyDogPerformance>();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(CostumeAssets + "KD_PumpkinCostume.mat");
            if (p.costumeNodes.Length != 18 || p.costumeGlowRenderers.Length != 18 || mat == null || mat.GetFloat("_EmissionGain") != 0)
                throw new InvalidOperationException("Eighteen separate costumes and a non-emitting base material required.");
            int lights = UnityEngine.Object.FindObjectsOfType<Light>(true).Count(l => l.gameObject.scene == SceneManager.GetActiveScene());
            int expected = (int)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Evidence + "/costume-before.json"))["lights"];
            if (lights != expected) throw new InvalidOperationException("Light count changed during costume production.");
            for (int i = 0; i < 18; i++)
            {
                var n = p.costumeNodes[i]; var r = p.costumeGlowRenderers[i];
                if (n.name != "KD_PumpkinCostume_" + CostumeKeys[i / 6] + "_" + KD_CandyDogSetupEditor.DogKeys[i % 6]
                    || r == null || !r.transform.IsChildOf(n.transform) || r.sharedMaterial != mat || n.activeSelf
                    || n.GetComponentsInChildren<Light>(true).Length != 0 || n.GetComponentsInChildren<Renderer>(true).Length != 1)
                    throw new InvalidOperationException("Costume wiring differs at " + i);
            }
            if (!p.accessoryBox.candyPerformanceNodes.SequenceEqual(p.costumeNodes)) throw new InvalidOperationException("Overlay differs");
            File.WriteAllText(Evidence + "/costume-construction.json", JsonConvert.SerializeObject(new {
                checkedAt = DateTime.UtcNow, complete = true, scope = "construction only; runtime fitting/emission reviewed separately",
                lightsBefore = expected, lightsAfter = lights, addedLights = 0, rendererCount = 18,
                material = CostumeAssets + "KD_PumpkinCostume.mat", baseEmission = mat.GetFloat("_EmissionGain"),
                costumes = p.costumeNodes.Select(n => new { n.name, parent = n.transform.parent.name, position = Vec(n.transform.localPosition),
                    rotation = Vec(n.transform.localEulerAngles), scale = Vec(n.transform.localScale), bounds = CostumeBounds(n),
                    triangles = n.GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh.triangles.Length / 3) })
            }, Formatting.Indented));
        }

        private static float[] Vec(Vector3 v) => new[] { v.x, v.y, v.z };
        private static object CostumeBounds(GameObject n) { var b = WorldCostumeBounds(n); return new { center = Vec(b.center), size = Vec(b.size) }; }
        private static Bounds WorldCostumeBounds(GameObject n)
        {
            bool first = true; Bounds b = new Bounds();
            foreach (var f in n.GetComponentsInChildren<MeshFilter>(true))
            foreach (var v in f.sharedMesh.vertices) { var w = f.transform.TransformPoint(v); if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w); }
            return b;
        }
        private static float ExtentAlong(GameObject n, Vector3 axis)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (var f in n.GetComponentsInChildren<MeshFilter>(true))
            foreach (var v in f.sharedMesh.vertices) { float a = Vector3.Dot(f.transform.TransformPoint(v), axis); min = Mathf.Min(min, a); max = Mathf.Max(max, a); }
            return max - min;
        }
    }
}
