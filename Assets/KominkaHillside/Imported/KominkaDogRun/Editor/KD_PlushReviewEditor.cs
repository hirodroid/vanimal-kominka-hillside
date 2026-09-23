using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    // Development-only review. No product registration, saved scene override, or runtime behaviour.
    public class KD_PlushReviewEditor : EditorWindow
    {
        const string Root = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Toys/";
        const string Menu = "Tools/KominkaHillside/Menu/Imported-Unused/Plush Review/";
        static readonly string[] Keys = { "PawCushion", "TaiyakiPlush", "EbiTenPlush" };
        static readonly string[] Labels = { "肉球クッション", "たい焼きぬいぐるみ", "エビ天ぬいぐるみ" };
        static readonly float[] Scales = { 1.05f, 1.5f, 1.6f };
        static readonly List<GameObject> Ghosts = new List<GameObject>();
        static readonly List<Material> TemporaryMaterials = new List<Material>();
        static bool _dogTint;

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.playModeStateChanged += OnPlayState;
        }

        static void OnPlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode) Clear();
        }

        [MenuItem(Menu + "1 - Open Comparison")]
        public static void Open() { GetWindow<KD_PlushReviewEditor>("ぬいぐるみ比較"); }

        void OnDisable() { Clear(); }

        void OnGUI()
        {
            EditorGUILayout.LabelField("ぬいぐるみ V3", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("口元へ固定して大きさと材質を比較します。咥え中の実際の向き・動作は商品棚から選んでPlayで確認してください。", MessageType.Info);
            _dogTint = EditorGUILayout.Toggle("既存の犬色を重ねて比較", _dogTint);
            for (int i = 0; i < Keys.Length; i++)
                if (GUILayout.Button(Labels[i]))
                {
                    if (!EditorApplication.isPlaying) ShowInMouth(i);
                }
            if (GUILayout.Button("比較を解除")) Clear();
            EditorGUILayout.HelpBox("比較はこのEditor内だけです。商品棚・価格・保存データは変更しません。", MessageType.None);
        }

        static void Guard()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/KominkaDogRun.unity")
                throw new InvalidOperationException("Open KominkaDogRun scene first.");
        }

        [MenuItem(Menu + "2 - Configure Imported Assets")]
        public static void ConfigureAssets()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only.");
            foreach (string key in Keys)
            {
                foreach (string suffix in new[] { "Diffuse", "Normal", "Ao" })
                {
                    var importer = AssetImporter.GetAtPath(Root + key + "/" + key + "_" + suffix + ".png") as TextureImporter;
                    if (importer == null) throw new InvalidOperationException("Missing texture " + key + suffix);
                    importer.textureType = suffix == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    importer.sRGBTexture = suffix == "Diffuse";
                    importer.alphaSource = TextureImporterAlphaSource.None;
                    importer.maxTextureSize = 1024;
                    importer.mipmapEnabled = true;
                    importer.isReadable = false;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.anisoLevel = 2;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.SaveAndReimport();
                }
                var model = AssetImporter.GetAtPath(Root + key + "/" + key + ".fbx") as ModelImporter;
                if (model == null) throw new InvalidOperationException("Missing model " + key);
                model.importAnimation = false;
                model.animationType = ModelImporterAnimationType.None;
                model.importCameras = false;
                model.importLights = false;
                model.addCollider = false;
                model.isReadable = false;
                model.materialImportMode = ModelImporterMaterialImportMode.None;
                model.importNormals = ModelImporterNormals.Import;
                model.importTangents = ModelImporterTangents.CalculateMikk;
                model.SaveAndReimport();
                string path = Root + key + "/M_KD_Toy_" + key + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(Shader.Find("Standard"));
                    AssetDatabase.CreateAsset(mat, path);
                    mat.color = Color.white;
                    mat.SetFloat("_Metallic", 0f);
                    mat.SetFloat("_Glossiness", 0.12f);
                    mat.SetFloat("_BumpScale", 0.65f);
                    mat.SetFloat("_OcclusionStrength", 0.4f);
                }
                mat.SetTexture("_MainTex", Texture(key, "Diffuse"));
                mat.SetTexture("_BumpMap", Texture(key, "Normal"));
                mat.SetTexture("_OcclusionMap", Texture(key, "Ao"));
                mat.EnableKeyword("_NORMALMAP");
                EditorUtility.SetDirty(mat);
                var prefab = MakeVisual(Array.IndexOf(Keys, key), 1f, Color.white, false);
                try
                {
                    prefab.hideFlags = HideFlags.None;
                    PrefabUtility.SaveAsPrefabAsset(prefab, Root + key + "/KD_" + key + ".prefab", out bool saved);
                    if (!saved) throw new InvalidOperationException("Prefab save failed " + key);
                }
                finally { Object.DestroyImmediate(prefab); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[PlushV3] Imported assets configured; scene untouched.");
        }

        static Texture2D Texture(string key, string suffix)
        { return AssetDatabase.LoadAssetAtPath<Texture2D>(Root + key + "/" + key + "_" + suffix + ".png"); }

        static GameObject MakeVisual(int index, float length, Color tint, bool temporaryMaterial)
        {
            string key = Keys[index];
            if (!KD_ModelNodeUtil.ResolveMeshFromFbx(Root + key + "/" + key + ".fbx", out Mesh mesh, out Matrix4x4 matrix, out string problem))
                throw new InvalidOperationException(problem);
            var root = new GameObject("KD_" + key + "_V3") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Root + key + "/M_KD_Toy_" + key + ".mat");
            if (temporaryMaterial)
            {
                material = new Material(material) { hideFlags = HideFlags.HideAndDontSave, color = tint };
                TemporaryMaterials.Add(material);
            }
            if (!KD_ModelNodeUtil.BuildMeshNode(root, "Visual", mesh, matrix, length, material, null))
            throw new InvalidOperationException("Invalid FBX transform " + key);
            var renderer = root.GetComponentInChildren<MeshRenderer>();
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            return root;
            }
            catch { Object.DestroyImmediate(root); throw; }
        }

        static KD_DogFetchController Fetch(KD_DogDef dog)
        {
            var go = KD_SceneObjects.Find(dog.FetchSystemName);
            var fetch = go != null ? go.GetComponent<KD_DogFetchController>() : null;
            if (fetch == null || fetch.mouthAnchor == null || fetch.dogRoot == null)
                throw new InvalidOperationException("Missing fetch reference " + dog.key);
            return fetch;
        }

        static void ShowInMouth(int index)
        {
            Guard(); Clear();
            foreach (var dog in KD_DogDefs.All)
            {
                var fetch = Fetch(dog);
                var visual = MakeVisual(index, dog.ballDiameter * Scales[index], _dogTint ? Color.Lerp(Color.white, dog.ballColor, KD_PlushShelfEditor.TintStrength) : Color.white, true);
                // The Blender front (+Z) imports upward. Rotate the illustration to face forward.
                visual.transform.SetPositionAndRotation(fetch.mouthAnchor.position, fetch.dogRoot.rotation * Quaternion.Euler(90f, 0f, 0f));
                Ghosts.Add(visual);
            }
            SceneView.RepaintAll();
        }

        [MenuItem(Menu + "5 - Clear Comparison")]
        public static void Clear()
        {
            foreach (var ghost in Ghosts) if (ghost != null) Object.DestroyImmediate(ghost);
            Ghosts.Clear();
            foreach (var material in TemporaryMaterials) if (material != null) Object.DestroyImmediate(material);
            TemporaryMaterials.Clear();
        }

        [MenuItem(Menu + "3 - Verify Imported Assets")]
        public static void VerifyAssets()
        {
            var log = new StringBuilder(); int failures = 0;
            foreach (string key in Keys)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + key + "/KD_" + key + ".prefab");
                var meshFilter = prefab != null ? prefab.GetComponentInChildren<MeshFilter>(true) : null;
                if (meshFilter == null) { failures++; log.AppendLine("FAIL " + key + " prefab"); continue; }
                Mesh mesh = meshFilter.sharedMesh;
                int triangles = (int)mesh.GetIndexCount(0) / 3;
                var renderer = meshFilter.GetComponent<MeshRenderer>();
                bool ok = triangles > 0 && triangles <= 5000 && mesh.subMeshCount == 1
                    && mesh.uv.Length == mesh.vertexCount && mesh.normals.Length == mesh.vertexCount && mesh.tangents.Length == mesh.vertexCount
                    && renderer.sharedMaterials.Length == 1 && prefab.GetComponentsInChildren<Collider>(true).Length == 0
                    && prefab.GetComponentsInChildren<MonoBehaviour>(true).Length == 0;
                foreach (string suffix in new[] { "Diffuse", "Normal", "Ao" })
                {
                    var texture = Texture(key, suffix);
                    var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
                    ok &= texture != null && imp != null && imp.maxTextureSize == 1024 && !imp.isReadable
                        && imp.mipmapEnabled && imp.sRGBTexture == (suffix == "Diffuse");
                    if (suffix == "Normal") ok &= imp != null && imp.textureType == TextureImporterType.NormalMap;
                }
                if (!ok) failures++;
                log.AppendLine((ok ? "PASS " : "FAIL ") + key + " tris=" + triangles + " vertices=" + mesh.vertexCount
                    + " bounds=" + renderer.bounds.size + " material=" + renderer.sharedMaterial.name);
            }
            log.AppendLine("failures=" + failures);
            Directory.CreateDirectory("Logs/plush-v3"); File.WriteAllText("Logs/plush-v3/verify.txt", log.ToString());
            if (failures != 0) throw new InvalidOperationException(log.ToString());
            Debug.Log(log.ToString());
        }

        [MenuItem(Menu + "4 - Capture Mouth Comparisons")]
        public static void Capture()
        {
            Guard();
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only.");
            Directory.CreateDirectory("Logs/plush-v3");
            var log = new StringBuilder();
            try
            {
                for (int index = 0; index < Keys.Length; index++)
                {
                    ShowInMouth(index);
                    foreach (var dog in KD_DogDefs.All)
                    {
                        var fetch = Fetch(dog);
                        Vector3 center = fetch.mouthAnchor.position;
                        string path = "Logs/plush-v3/" + dog.key + "-" + Keys[index] + (_dogTint ? "-tint" : "") + ".png";
                        CaptureAt(center, center + fetch.dogRoot.rotation * new Vector3(0.27f, 0.15f, 0.5f), path);
                        log.AppendLine(dog.key + " " + Keys[index] + " length=" + (dog.ballDiameter * Scales[index]).ToString("F3")
                            + " mouth=" + center.ToString("F3") + " image=" + path);
                        if (dog.key == "Corgi")
                        {
                            CaptureAt(center, center + fetch.dogRoot.rotation * new Vector3(.14f, .06f, .26f), path.Replace(".png", "-close.png"));
                            CaptureAt(center, center + fetch.dogRoot.rotation * new Vector3(.42f, .28f, .86f), path.Replace(".png", "-far.png"));
                        }
                    }
                }
            }
            finally { Clear(); }
            File.WriteAllText("Logs/plush-v3/mouth-comparison" + (_dogTint ? "-tint" : "") + ".txt", log.ToString());
        }

        [MenuItem(Menu + "4b - Capture Existing Dog Tint")]
        public static void CaptureDogTint()
        {
            bool previous = _dogTint;
            try { _dogTint = true; Capture(); }
            finally { _dogTint = previous; }
        }

        [MenuItem(Menu + "4c - Capture Shelf")]
        public static void CaptureShelf()
        {
            Guard();
            var shelf = KD_SceneObjects.Find(KD_DogDefs.ShopShelfName).transform;
            CaptureAt(shelf.TransformPoint(new Vector3(-.1f, -.30f, .2f)), shelf.TransformPoint(new Vector3(.5f, .7f, 1.7f)), "Logs/plush-v3/shelf.png");
        }

        static void CaptureAt(Vector3 center, Vector3 position, string path)
        {
            var go = new GameObject("KD_PlushReviewCamera") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(1100, 850, 24); var previous = RenderTexture.active; Texture2D image = null;
            try
            {
                var camera = go.AddComponent<Camera>(); camera.enabled = false;
                camera.nearClipPlane = .01f; camera.farClipPlane = 100f; camera.fieldOfView = 48f;
                camera.transform.position = position; camera.transform.LookAt(center);
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(go); Object.DestroyImmediate(rt); if (image != null) Object.DestroyImmediate(image); }
        }
    }
}
