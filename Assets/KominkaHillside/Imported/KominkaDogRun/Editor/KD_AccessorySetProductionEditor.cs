using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    // Model-only production and inspection. Does not write the world scene or existing fit nodes.
    public static class KD_AccessorySetProductionEditor
    {
        const string Root = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/ThemeSets/";
        const string Output = "C:/work/unity/vrc-world-agent/artifacts/KominkaDogRun/accessory-sets-20260909/Validation";
        static readonly string[] Keys = { "Bandana", "RoyalNecklace", "RoyalTail", "Sunglasses", "SunflowerNecklace", "MarineCap", "FlowerCrown", "FlowerCorsage", "FlowerTail" };
        static string ModelPath(string key) { return Root + key + "/KD_" + key + "_Set.fbx"; }
        public static string MaterialFor(string key) { return key == "Bandana" ? Root + "Bandana/M_KD_Bandana.mat" : key == "SunflowerNecklace" ? Root + "SunflowerNecklace/M_KD_Sunflower.mat" : key == "MarineCap" ? Root + "MarineCap/M_KD_Marine.mat" : (key == "FlowerCrown" || key == "FlowerCorsage" || key == "FlowerTail") ? Root + "SharedFlowers/M_KD_Flowers.mat" : Root + "Shared/M_KD_AccSets.mat"; }

        static Material ImportBandanaMaterial()
        {
            foreach (string suffix in new[] { "BaseColor", "Normal" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(Root + "Bandana/Bandana_" + suffix + ".png");
                ti.textureType = suffix == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = suffix == "BaseColor"; ti.alphaSource = TextureImporterAlphaSource.None;
                ti.isReadable = false; ti.mipmapEnabled = true; ti.maxTextureSize = 512;
                ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            string path = MaterialFor("Bandana");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, path); }
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Bandana/Bandana_BaseColor.png");
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Bandana/Bandana_Normal.png"));
            mat.SetFloat("_Metallic", 0); mat.SetFloat("_Glossiness", .24f); mat.SetFloat("_BumpScale", .55f);
            mat.EnableKeyword("_NORMALMAP"); mat.enableInstancing = true; EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material ImportSunflowerMaterial()
        {
            const string folder = Root + "SunflowerNecklace/";
            foreach (string suffix in new[] { "BaseColor", "Normal" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(folder + "Sunflower_" + suffix + ".png");
                ti.textureType = suffix == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = suffix == "BaseColor"; ti.alphaSource = TextureImporterAlphaSource.None;
                ti.isReadable = false; ti.mipmapEnabled = true; ti.maxTextureSize = 512;
                ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            string path = MaterialFor("SunflowerNecklace");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, path); }
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "Sunflower_BaseColor.png");
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "Sunflower_Normal.png"));
            mat.SetFloat("_Metallic", 0); mat.SetFloat("_Glossiness", .28f); mat.SetFloat("_BumpScale", .45f);
            mat.EnableKeyword("_NORMALMAP"); mat.enableInstancing = true; EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material ImportMarineMaterial()
        {
            const string folder = Root + "MarineCap/";
            foreach (string suffix in new[] { "BaseColor", "Normal" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(folder + "Marine_" + suffix + ".png");
                ti.textureType = suffix == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = suffix == "BaseColor"; ti.alphaSource = TextureImporterAlphaSource.None;
                ti.isReadable = false; ti.mipmapEnabled = true; ti.maxTextureSize = 512;
                ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            string path = MaterialFor("MarineCap");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, path); }
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "Marine_BaseColor.png");
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "Marine_Normal.png"));
            mat.SetFloat("_Metallic", 0); mat.SetFloat("_Glossiness", .3f); mat.SetFloat("_BumpScale", .4f);
            mat.EnableKeyword("_NORMALMAP"); mat.enableInstancing = true; EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material ImportFlowersMaterial()
        {
            const string folder = Root + "SharedFlowers/";
            foreach (string suffix in new[] { "BaseColor", "Normal" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(folder + "Flowers_" + suffix + ".png");
                ti.textureType = suffix == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = suffix == "BaseColor"; ti.alphaSource = TextureImporterAlphaSource.None;
                ti.isReadable = false; ti.mipmapEnabled = true; ti.maxTextureSize = 512;
                ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            string path = MaterialFor("FlowerCrown");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, path); }
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "Flowers_BaseColor.png");
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "Flowers_Normal.png"));
            mat.SetFloat("_Metallic", 0); mat.SetFloat("_Glossiness", .3f); mat.SetFloat("_BumpScale", .35f);
            mat.EnableKeyword("_NORMALMAP"); mat.enableInstancing = true; EditorUtility.SetDirty(mat);
            return mat;
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Sets - Import Models")]
        public static void ImportModels()
        {
            foreach (string suffix in new[] { "BaseColor", "MetallicSmoothness" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(Root + "Shared/AccSets_" + suffix + ".png");
                ti.sRGBTexture = suffix == "BaseColor"; ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.isReadable = false; ti.mipmapEnabled = true; ti.maxTextureSize = 256;
                ti.textureCompression = TextureImporterCompression.Uncompressed; ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            string matPath = Root + "Shared/M_KD_AccSets.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat)
            {
                mat = new Material(Shader.Find("Standard"));
                mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Shared/AccSets_BaseColor.png");
                mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Shared/AccSets_MetallicSmoothness.png"));
                mat.SetFloat("_Metallic", 1); mat.SetFloat("_GlossMapScale", 1); mat.SetFloat("_SmoothnessTextureChannel", 0);
                mat.EnableKeyword("_METALLICGLOSSMAP"); mat.enableInstancing = true;
                AssetDatabase.CreateAsset(mat, matPath);
            }
            var bandanaMaterial = ImportBandanaMaterial();
            var sunflowerMaterial = ImportSunflowerMaterial();
            var marineMaterial = ImportMarineMaterial();
            var flowersMaterial = ImportFlowersMaterial();
            foreach (string key in Keys)
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath(ModelPath(key));
                mi.animationType = ModelImporterAnimationType.None; mi.importAnimation = false;
                mi.importCameras = false; mi.importLights = false; mi.isReadable = false;
                mi.addCollider = false; mi.generateSecondaryUV = false; mi.importNormals = ModelImporterNormals.Import;
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "KD_AccSets_Palette"), key == "Bandana" ? bandanaMaterial : key == "SunflowerNecklace" ? sunflowerMaterial : key == "MarineCap" ? marineMaterial : (key == "FlowerCrown" || key == "FlowerCorsage" || key == "FlowerTail") ? flowersMaterial : mat);
                if (key == "Bandana") mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "KD_Bandana_Fabric"), bandanaMaterial);
                if (key == "RoyalNecklace") mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "KD_RoyalNecklace_Palette"), mat);
                if (key == "RoyalTail") mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "KD_RoyalTail_Palette"), mat);
                if (key == "Sunglasses") mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "KD_Sunglasses_Palette"), mat);
                if (key == "SunflowerNecklace") mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "KD_Sunflower_Fabric"), sunflowerMaterial);
                if (key == "MarineCap") mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "KD_Marine_Fabric"), marineMaterial);
                if (key == "FlowerCrown" || key == "FlowerCorsage" || key == "FlowerTail") mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "KD_Flowers_Fabric"), flowersMaterial);
                mi.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Output); File.WriteAllText(Output + "/import-complete.txt", DateTime.UtcNow.ToString("o"));
        }

        [Serializable] class Item { public string key; public int tris, vertices, meshes, renderers, materialSlots, submeshes, clips, degenerate, zeroUv; public Vector3 size; public string shader, material; public bool pass; }
        [Serializable] class Report { public string revision = "accessory-sets-v1"; public bool sceneDirty; public List<Item> items = new List<Item>(); }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Sets - Verify Models")]
        public static void VerifyModels()
        {
            Directory.CreateDirectory(Output);
            var report = new Report { sceneDirty = UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty };
            foreach (string key in Keys)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(key));
                var item = new Item { key = key }; Bounds bounds = new Bounds(); bool first = true;
                foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = filter.sharedMesh; var vertices = mesh.vertices; var triangles = mesh.triangles; var uv = mesh.uv;
                    item.meshes++; item.tris += triangles.Length / 3; item.vertices += mesh.vertexCount; item.submeshes += mesh.subMeshCount;
                    foreach (var p in vertices) { Vector3 v = filter.transform.TransformPoint(p); if (first) { bounds = new Bounds(v, Vector3.zero); first = false; } else bounds.Encapsulate(v); }
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                        if (Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).sqrMagnitude < 1e-18f) item.degenerate++;
                        if (uv.Length != vertices.Length || Mathf.Abs((uv[b].x-uv[a].x)*(uv[c].y-uv[a].y)-(uv[b].y-uv[a].y)*(uv[c].x-uv[a].x)) < 1e-10f) item.zeroUv++;
                    }
                }
                item.size = bounds.size;
                foreach (var r in source.GetComponentsInChildren<Renderer>(true)) { item.renderers++; item.materialSlots += r.sharedMaterials.Length; item.shader = r.sharedMaterial.shader.name; item.material = AssetDatabase.GetAssetPath(r.sharedMaterial); }
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(ModelPath(key))) if (a is AnimationClip) item.clips++;
                item.pass = item.tris <= 3000 && item.meshes == 1 && item.renderers == 1 && item.materialSlots == 1 && item.submeshes == 1 && item.clips == 0 && item.degenerate == 0 && item.zeroUv == 0 && item.material == MaterialFor(key);
                report.items.Add(item); RenderViews(source, key, bounds);
            }
            File.WriteAllText(Output + "/unity-model-audit.json", JsonUtility.ToJson(report, true));
        }

        static void RenderViews(GameObject source, string key, Bounds bounds)
        {
            var preview = new PreviewRenderUtility(); var gray = new Material(Shader.Find("Standard")) { color = new Color(.55f,.55f,.55f) };
            try
            {
                var model = UnityEngine.Object.Instantiate(source); preview.AddSingleGO(model);
                preview.camera.nearClipPlane = .001f; preview.camera.farClipPlane = 20; preview.camera.fieldOfView = 35;
                preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(.17f,.20f,.19f);
                preview.ambientColor = new Color(.35f,.35f,.35f);
                preview.lights[0].intensity = 1.1f; preview.lights[0].transform.rotation = Quaternion.Euler(40,30,0);
                preview.lights[1].intensity = .7f; preview.lights[1].transform.rotation = Quaternion.Euler(320,210,0);
                var dirs = new[] { Vector3.back, Vector3.right, Vector3.forward, Vector3.down, new Vector3(.5f,.4f,-1) };
                for (int pass = 0; pass < 2; pass++)
                {
                    if (pass == 1) foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = gray;
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        Vector3 dir = dirs[i].normalized; preview.camera.transform.position = bounds.center + dir * bounds.size.magnitude * 1.85f;
                        preview.camera.transform.rotation = Quaternion.LookRotation(-dir, Mathf.Abs(dir.y) > .9f ? Vector3.forward : Vector3.up);
                        preview.BeginStaticPreview(new Rect(0,0,600,600)); preview.Render(true); var tex = preview.EndStaticPreview();
                        File.WriteAllBytes(Output + "/" + key + "-" + (pass == 0 ? "material" : "gray") + "-" + i + ".png", tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
                    }
                }
            }
            finally { preview.Cleanup(); UnityEngine.Object.DestroyImmediate(gray); }
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Sets - Render Corgi Draft")]
        public static void RenderCorgiDraft()
        {
            Directory.CreateDirectory(Output);
            var dog = KD_SceneObjects.Find(KD_DogDefs.Corgi.DogName);
            if (!dog) throw new InvalidOperationException("Corgi is not in the open world");
            var rig = KD_DogRig.Resolve(dog);
            if (!rig.Ok) throw new InvalidOperationException("Corgi rig unresolved");
            var sets = new[] { new[] { "Crown", "RoyalNecklace", "RoyalTail" }, new[] { "MarineCap", "Sunglasses", "SunflowerNecklace" }, new[] { "FlowerCrown", "FlowerCorsage", "FlowerTail" } };
            string[] labels = { "Royal", "Summer", "Floral" };
            Vector3 up = dog.transform.up;
            Vector3 forward = Vector3.ProjectOnPlane(rig.nose.position - rig.head.position, up).normalized;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            for (int setIndex = 0; setIndex < sets.Length; setIndex++)
            {
                var preview = new PreviewRenderUtility(); var baked = new List<Mesh>();
                try
                {
                    Bounds bounds = new Bounds(); bool first = true;
                    foreach (var r in dog.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                        // Same scale contract as KD_DogPreviewEditor.BakeRenderers: these rigs
                        // require useScale=true before localToWorld (verified there by bone weights).
                        var mesh = new Mesh(); r.BakeMesh(mesh, true); baked.Add(mesh);
                        var go = new GameObject("KD_DraftDogMesh"); go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        go.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                        go.transform.SetPositionAndRotation(r.transform.position - dog.transform.position, r.transform.rotation); go.transform.localScale = r.transform.lossyScale;
                        preview.AddSingleGO(go); var copiedBounds = go.GetComponent<Renderer>().bounds;
                        if (first) { bounds = copiedBounds; first = false; } else bounds.Encapsulate(copiedBounds);
                    }
                    foreach (string key in sets[setIndex])
                    {
                        string slotKey = key == "Crown" || key == "MarineCap" || key == "FlowerCrown" ? "Crown" : key.EndsWith("Tail") ? "TailRibbon" : "BowTie";
                        Transform anchor = null;
                        foreach (var t in dog.GetComponentsInChildren<Transform>(true)) if (t.name == "KD_Acc_" + slotKey + "_Corgi") anchor = t;
                        if (!anchor) throw new InvalidOperationException("Missing fit anchor " + slotKey);
                        string assetPath = key == "Crown" ? "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/BlenderOptimized/Crown/KD_Crown_Optimized.fbx" : ModelPath(key);
                        var art = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath));
                        var wrap = new GameObject("KD_Draft_" + key); art.transform.SetParent(wrap.transform, false);
                        wrap.transform.SetPositionAndRotation(anchor.position - dog.transform.position, anchor.rotation); wrap.transform.localScale = anchor.lossyScale;
                        if (key == "Sunglasses")
                        {
                            float worldHead = Vector3.Distance(rig.nose.position, rig.head.position);
                            wrap.transform.SetPositionAndRotation(rig.head.position - dog.transform.position + forward * worldHead * 1.05f + up * worldHead * .32f, Quaternion.LookRotation(forward, up));
                            wrap.transform.localScale = Vector3.one * (worldHead * 2.3f / .192f);
                        }
                        preview.AddSingleGO(wrap);
                        foreach (var r in art.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                    }
                    preview.camera.fieldOfView = 30; preview.camera.nearClipPlane = .001f; preview.camera.farClipPlane = 100;
                    preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(.20f,.24f,.22f);
                    preview.ambientColor = new Color(.4f,.4f,.4f);
                    preview.lights[0].intensity = 1.1f; preview.lights[0].transform.rotation = Quaternion.LookRotation(-forward - up);
                    preview.lights[1].intensity = .65f; preview.lights[1].transform.rotation = Quaternion.LookRotation(forward - right - up);
                    var dirs = new[] { (forward * .8f + right * .6f + up * .22f).normalized, (right + up * .12f).normalized };
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        preview.camera.transform.position = bounds.center + dirs[i] * bounds.size.magnitude * 1.9f;
                        preview.camera.transform.rotation = Quaternion.LookRotation(bounds.center - preview.camera.transform.position, up);
                        preview.BeginStaticPreview(new Rect(0,0,1100,850)); preview.Render(true); var image = preview.EndStaticPreview();
                        File.WriteAllBytes(Output + "/" + labels[setIndex] + "-Corgi-draft-" + i + ".png", image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
                    }
                }
                finally { preview.Cleanup(); foreach (var mesh in baked) UnityEngine.Object.DestroyImmediate(mesh); }
            }
            File.WriteAllText(Output + "/corgi-draft-note.txt", "Draft only: current baked Corgi pose, existing accessory anchors reused without changing the scene; new shapes have not been fitted to all six breeds. Sunglasses use an initial estimated eye position.");
        }
    }
}
