using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    // Five standalone Point goods. Fitting only runs when a node is first created.
    public static class KD_AccessoryEverydayEditor
    {
        public const string Root = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/Everyday";
        public const string MaterialPath = Root + "/Shared/M_KD_AccEveryday.mat";
        public static string MaterialFor(string key) { return key == "SpiralGlasses" || key == "Backpack" || key == "Cap" ? Root + "/" + key + "/M_KD_" + key + ".mat" : MaterialPath; }
        static string SourceMaterialFor(string key) { return key == "SpiralGlasses" || key == "Backpack" || key == "Cap" ? "KD_" + key + "_Surface" : "KD_AccEveryday_Palette"; }
        const string Output = "Temp/acc-five";
        public static readonly string[] Keys = { "TopHat", "Cap", "SpiralGlasses", "StarGlasses", "Backpack" };
        public static string ModelPath(string key) { return Root + "/" + key + "/KD_" + key + "_Everyday.fbx"; }
        static void Guard()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != KD_Hillside.ScenePath /* 裏山改変: シーンの正本は KD_Hillside */)
                throw new InvalidOperationException("Target world in Edit mode required");
            Directory.CreateDirectory(Output);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Everyday - Import")]
        public static void Import()
        {
            Guard();
            ImportSpiralSurface();
            ImportBackpackSurface();
            ImportCapSurface();
            foreach (string suffix in new[] { "BaseColor", "MetallicSmoothness" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(Root + "/Shared/AccEveryday_" + suffix + ".png");
                ti.sRGBTexture = suffix == "BaseColor"; ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.isReadable = false; ti.mipmapEnabled = true; ti.maxTextureSize = 256;
                ti.textureCompression = TextureImporterCompression.Uncompressed; ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard"));
                mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Shared/AccEveryday_BaseColor.png");
                mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Shared/AccEveryday_MetallicSmoothness.png"));
                mat.SetFloat("_GlossMapScale", 1); mat.EnableKeyword("_METALLICGLOSSMAP"); mat.enableInstancing = true;
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            foreach (var key in Keys)
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath(ModelPath(key));
                mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
                mi.importCameras = false; mi.importLights = false; mi.addCollider = false;
                mi.isReadable = false; mi.generateSecondaryUV = false; mi.importNormals = ModelImporterNormals.Import;
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), SourceMaterialFor(key)), AssetDatabase.LoadAssetAtPath<Material>(MaterialFor(key)));
                mi.SaveAndReimport();
            }
            AssetDatabase.SaveAssets(); File.WriteAllText(Output + "/import.txt", "everyday-v1 import complete");
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Spiral - Import Surface")]
        public static void ImportSpiralSurface()
        {
            ImportSurface("SpiralGlasses");
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Backpack - Import Surface")]
        public static void ImportBackpackSurface()
        {
            ImportSurface("Backpack");
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Cap - Import Surface")]
        public static void ImportCapSurface()
        {
            ImportSurface("Cap");
        }

        static void ImportSurface(string key)
        {
            Guard();
            string folder = Root + "/" + key + "/";
            foreach (string suffix in key == "Cap" ? new[] { "BaseColor" } : new[] { "BaseColor", "MetallicSmoothness" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(folder + key + "_" + suffix + ".png");
                ti.sRGBTexture = suffix == "BaseColor";
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.isReadable = false; ti.mipmapEnabled = true; ti.maxTextureSize = 512;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear;
                ti.SaveAndReimport();
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFor(key));
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(mat, MaterialFor(key));
            }
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + key + "_BaseColor.png");
            if (key == "Cap")
            {
                mat.SetTexture("_MetallicGlossMap", null); mat.DisableKeyword("_METALLICGLOSSMAP");
                mat.SetFloat("_Metallic", 0); mat.SetFloat("_Glossiness", .18f);
            }
            else
            {
                mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + key + "_MetallicSmoothness.png"));
                mat.SetFloat("_GlossMapScale", 1); mat.EnableKeyword("_METALLICGLOSSMAP");
            }
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            var mi = (ModelImporter)AssetImporter.GetAtPath(ModelPath(key));
            mi.RemoveRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "KD_AccEveryday_Palette"));
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), SourceMaterialFor(key)), mat);
            mi.SaveAndReimport(); AssetDatabase.SaveAssets();
        }

        public static void PlaceNew(Transform holder, GameObject dog, KD_DogRig rig, string key, Vector3 size)
        {
            string dogKey = KD_DogDefs.All.First(d => d.DogName == dog.name).key;
            Vector3 up = dog.transform.up;
            Vector3 forward = Vector3.ProjectOnPlane(rig.nose.position - rig.head.position, up).normalized;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            bool face = key == "SpiralGlasses" || key == "StarGlasses";
            float scale;
            if (key == "Backpack")
            {
                var neck = rig.head.parent;
                var tail = KD_AccessorySetupEditor.AnchorFor(rig, KD_AccessorySetupEditor.Slot.Tail);
                Vector3 mid = Vector3.Lerp(neck.position, tail.position, .46f);
                float length = Mathf.Abs(Vector3.Dot(neck.position - tail.position, forward));
                scale = Mathf.Min(rig.headScale * 1.65f / size.x, length * .58f / size.z);
                float surface = Vector3.Dot(mid, up);
                foreach (var r in dog.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    var mesh = new Mesh();
                    try
                    {
                        r.BakeMesh(mesh, true);
                        foreach (Vector3 v in mesh.vertices)
                        {
                            Vector3 w = r.transform.TransformPoint(v), delta = w - mid;
                            if (Mathf.Abs(Vector3.Dot(delta, right)) < size.x * scale * .35f && Mathf.Abs(Vector3.Dot(delta, forward)) < size.z * scale * .28f)
                                surface = Mathf.Max(surface, Vector3.Dot(w, up));
                        }
                    }
                    finally { Object.DestroyImmediate(mesh); }
                }
                mid += up * (surface - Vector3.Dot(mid, up) + .002f);
                holder.SetPositionAndRotation(mid, Quaternion.LookRotation(forward, up));
            }
            else
            {
                var source = KD_SceneObjects.Find("KD_Acc_" + (face ? "Sunglasses" : "Crown") + "_" + dogKey).transform;
                holder.SetPositionAndRotation(source.position, source.rotation);
                float width = ProjectedWidth(source, right);
                if (!face) width *= key == "Cap" ? 1.30f : 1.18f;
                if (key == "Cap" && dogKey == "Dachs")
                {
                    width *= .90f;
                    holder.position += up * rig.headScale * .12f - forward * rig.headScale * .12f;
                }
                scale = width / size.x;
            }
            Vector3 inherited = holder.parent.lossyScale;
            holder.localScale = new Vector3(scale / inherited.x, scale / inherited.y, scale / inherited.z);
        }

        static float ProjectedWidth(Transform root, Vector3 axis)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true)) foreach (var v in f.sharedMesh.vertices)
            { float p = Vector3.Dot(f.transform.TransformPoint(v), axis); min = Mathf.Min(min, p); max = Mathf.Max(max, p); }
            return max - min;
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Everyday - Adjust Dachs Cap")]
        public static void AdjustDachsCap()
        {
            Guard(); var dog = KD_SceneObjects.Find(KD_DogDefs.All.First(d => d.key == "Dachs").DogName);
            var holder = KD_SceneObjects.Find("KD_Acc_Cap_Dachs").transform;
            var source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath("Cap")));
            try { PlaceNew(holder,dog,KD_DogRig.Resolve(dog),"Cap",BoundsOf(source).size); }
            finally { Object.DestroyImmediate(source); }
            EditorSceneManager.MarkSceneDirty(dog.scene);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Everyday - Build")]
        public static void Build()
        {
            Guard();
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Output + "/before-" + stamp + ".unity", true);
            KD_AccessorySetsEditor.Build();
            var catalog = KD_ProductsEditor.SceneComponents<KD_ShopCatalog>().Single(c => !c.name.StartsWith("Test"));
            var items = catalog.items.ToList();
            foreach (var key in Keys)
            {
                var item = KD_SceneObjects.Find("KD_ShopItem_Acc_" + key).GetComponent<KD_ShopItem>();
                if (!items.Contains(item)) items.Add(item);
            }
            catalog.items = items.ToArray(); catalog.ledger.products = catalog.items;
            KD_ProductsEditor.Copy(catalog); KD_ProductsEditor.Copy(catalog.ledger);
            KD_DogPreviewEditor.Build();
            File.WriteAllText(Output + "/build.txt", "everyday-v1: 5 additions, 18 types x 6 dogs, catalog bound");
        }

        static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true); var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds); return bounds;
        }
        static Texture2D Render(PreviewRenderUtility preview, Bounds bounds, Vector3 direction, int size)
        {
            preview.camera.nearClipPlane = .001f; preview.camera.farClipPlane = 50;
            preview.camera.fieldOfView = 32; preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.21f,.24f,.24f);
            preview.ambientColor = new Color(.4f,.4f,.4f);
            preview.lights[0].intensity = 1.1f; preview.lights[0].transform.rotation = Quaternion.Euler(40,30,0);
            preview.lights[1].intensity = .7f; preview.lights[1].transform.rotation = Quaternion.Euler(320,210,0);
            preview.camera.transform.position = bounds.center + direction.normalized * bounds.size.magnitude * 1.8f;
            preview.camera.transform.rotation = Quaternion.LookRotation(-direction.normalized, Vector3.up);
            preview.BeginStaticPreview(new Rect(0,0,size,size)); preview.Render(true); return preview.EndStaticPreview();
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Everyday - Verify Models")]
        public static void VerifyModels()
        {
            Guard(); var log = new StringBuilder(); int failures = 0;
            foreach (var key in Keys)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(key));
                var filters = source.GetComponentsInChildren<MeshFilter>(true); int tris = 0, degenerate = 0, zeroUv = 0;
                foreach (var f in filters)
                {
                    var mesh = f.sharedMesh; var v = mesh.vertices; var uv = mesh.uv; var t = mesh.triangles; tris += t.Length / 3;
                    for (int i = 0; i < t.Length; i += 3)
                    {
                        int a = t[i], b = t[i+1], c = t[i+2];
                        if (Vector3.Cross(v[b]-v[a],v[c]-v[a]).sqrMagnitude < 1e-18f) { degenerate++; log.AppendLine("degenerate " + a + "/" + b + "/" + c + " " + v[a].ToString("F6") + v[b].ToString("F6") + v[c].ToString("F6")); }
                        if (uv.Length != v.Length || Mathf.Abs((uv[b].x-uv[a].x)*(uv[c].y-uv[a].y)-(uv[b].y-uv[a].y)*(uv[c].x-uv[a].x)) < 1e-10f) zeroUv++;
                    }
                }
                var renderers = source.GetComponentsInChildren<Renderer>(true);
                bool ok = filters.Length == 1 && filters[0].sharedMesh.subMeshCount == 1 && renderers.Length == 1
                    && renderers[0].sharedMaterials.Length == 1 && AssetDatabase.GetAssetPath(renderers[0].sharedMaterial) == MaterialFor(key)
                    && tris <= 4500 && degenerate == 0 && zeroUv == 0
                    && !AssetDatabase.LoadAllAssetsAtPath(ModelPath(key)).Any(a => a is AnimationClip);
                log.AppendLine((ok ? "PASS " : "FAIL ") + key + " tris=" + tris + " degenerate=" + degenerate + " zeroUV=" + zeroUv);
                if (!ok) failures++;
                var preview = new PreviewRenderUtility();
                try
                {
                    var go = Object.Instantiate(source); preview.AddSingleGO(go); var bounds = BoundsOf(go);
                    log.AppendLine("size=" + bounds.size.ToString("F4") + " root=" + go.transform.localEulerAngles);
                    int i = 0;
                    foreach (var direction in new[] { new Vector3(.6f,.5f,1), new Vector3(0,0,1), new Vector3(-.6f,.5f,-1) })
                    {
                        var image = Render(preview,bounds,direction,600);
                        File.WriteAllBytes(Output + "/model-" + key + "-" + i++ + ".png",image.EncodeToPNG()); Object.DestroyImmediate(image);
                    }
                }
                finally { preview.Cleanup(); }
            }
            log.AppendLine("FAILURES=" + failures); File.WriteAllText(Output + "/models.txt",log.ToString());
            if (failures > 0) throw new InvalidOperationException("Everyday models failed: " + failures);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Everyday - Render Fits")]
        public static void RenderFits()
        {
            Guard(); var log = new StringBuilder();
            foreach (var def in KD_DogDefs.All)
            {
                var source = KD_SceneObjects.Find(def.DogName);
                foreach (var pose in new[] { "idle", "sleep", "sit" })
                {
                    var scene = EditorSceneManager.NewPreviewScene();
                    try
                    {
                        var clone = Object.Instantiate(source); SceneManager.MoveGameObjectToScene(clone,scene);
                        clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity); clone.SetActive(true);
                        KD_DogPreviewEditor.SamplePose(clone, pose == "idle" ? def.idle : pose == "sit" ? def.sit : def.sleepLoop);
                        var rig = KD_DogRig.Resolve(clone);
                        Vector3 up = clone.transform.up, forward = Vector3.ProjectOnPlane(rig.nose.position-rig.head.position,up).normalized;
                        Vector3 right = Vector3.Cross(up,forward).normalized;
                        var preview = new PreviewRenderUtility(); var meshes = new List<Mesh>();
                        try
                        {
                            foreach (var r in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                            {
                                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                                var mesh = new Mesh(); r.BakeMesh(mesh,true); meshes.Add(mesh);
                                var g = new GameObject("Body"); g.AddComponent<MeshFilter>().sharedMesh = mesh;
                                g.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials.Select(KD_DogPreviewEditor.PreviewMaterial).ToArray();
                                g.transform.SetPositionAndRotation(r.transform.position,r.transform.rotation); g.transform.localScale = r.transform.lossyScale;
                                preview.AddSingleGO(g);
                            }
                            var all = new List<GameObject>();
                            foreach (var key in Keys)
                            {
                                var node = clone.GetComponentsInChildren<Transform>(true).Single(t => t.name == "KD_Acc_"+key+"_"+def.key);
                                var g = new GameObject(key); g.transform.SetPositionAndRotation(node.position,node.rotation); g.transform.localScale = node.lossyScale;
                                var art = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(key))); art.transform.SetParent(g.transform,false);
                                preview.AddSingleGO(g); all.Add(g);
                                log.AppendLine(def.key+" "+pose+" "+key+" bone="+node.parent.name+" local="+node.localPosition.ToString("F4")+" rotation="+node.localEulerAngles.ToString("F2")+" scale="+node.localScale.ToString("F4"));
                            }
                            for (int outfit = 0; outfit < 2; outfit++)
                            {
                                for (int k = 0; k < all.Count; k++) all[k].SetActive(k == 4 || k == outfit || k == outfit + 2);
                                var bounds = new Bounds(rig.head.position,Vector3.one*.05f);
                                foreach (var r in preview.camera.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>())) bounds.Encapsulate(r.bounds);
                                for (int view = 0; view < 2; view++)
                                {
                                    var image = Render(preview,bounds,(view == 0 ? forward : -forward)*1.5f + right*.7f + up*.65f,650);
                                    File.WriteAllBytes(Output+"/fit-"+def.key+"-"+pose+"-"+outfit+"-"+view+".png",image.EncodeToPNG()); Object.DestroyImmediate(image);
                                }
                            }
                        }
                        finally { preview.Cleanup(); foreach (var mesh in meshes) Object.DestroyImmediate(mesh); }
                    }
                    finally { EditorSceneManager.ClosePreviewScene(scene); }
                }
            }
            File.WriteAllText(Output+"/fits.txt",log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Accessories/Everyday - Render World")]
        public static void RenderWorld()
        {
            Guard(); var cameraGo = new GameObject("KD_EverydayInspectionCamera");
            var cam = cameraGo.AddComponent<Camera>(); var rt = new RenderTexture(1000,800,24);
            var previous = RenderTexture.active; var states = new Dictionary<GameObject,bool>();
            var image = new Texture2D(1000,800,TextureFormat.RGB24,false);
            try
            {
                cam.targetTexture = rt; cam.fieldOfView = 32; cam.nearClipPlane = .02f; cam.farClipPlane = 150;
                cam.clearFlags = CameraClearFlags.Skybox;
                foreach (var def in KD_DogDefs.All)
                {
                    var dog = KD_SceneObjects.Find(def.DogName); var rig = KD_DogRig.Resolve(dog);
                    Vector3 forward = Vector3.ProjectOnPlane(rig.nose.position-rig.head.position,dog.transform.up).normalized;
                    Vector3 right = Vector3.Cross(dog.transform.up,forward).normalized;
                    var renderers = dog.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled).ToArray();
                    var bounds = new Bounds(rig.head.position,Vector3.zero);
                    foreach (var r in renderers)
                    {
                        var mesh = new Mesh();
                        try { r.BakeMesh(mesh,true); foreach (var v in mesh.vertices) bounds.Encapsulate(r.transform.TransformPoint(v)); }
                        finally { Object.DestroyImmediate(mesh); }
                    }
                    var nodes = dog.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("KD_Acc_")).Select(t=>t.gameObject).ToArray();
                    foreach (var node in nodes) { states[node] = node.activeSelf; node.SetActive(false); }
                    for (int outfit = 0; outfit < 2; outfit++)
                    {
                        for (int k = 0; k < Keys.Length; k++) KD_SceneObjects.Find("KD_Acc_"+Keys[k]+"_"+def.key).SetActive(k == 4 || k == outfit || k == outfit+2);
                        for (int view = 0; view < 2; view++)
                        {
                            Vector3 direction = ((view == 0 ? forward : -forward)*1.5f + right*.75f + dog.transform.up*.65f).normalized;
                            Vector3 target = bounds.center + dog.transform.up * bounds.size.y * .15f;
                            cam.transform.position = target + direction*bounds.size.magnitude*1.55f;
                            cam.transform.rotation = Quaternion.LookRotation(target-cam.transform.position,dog.transform.up);
                            cam.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0,0,1000,800),0,0); image.Apply();
                            File.WriteAllBytes(Output+"/world-"+def.key+"-"+outfit+"-"+view+".png",image.EncodeToPNG());
                        }
                    }
                    foreach (var node in nodes) node.SetActive(states[node]);
                }
            }
            finally
            {
                foreach (var entry in states) if (entry.Key) entry.Key.SetActive(entry.Value);
                RenderTexture.active = previous; Object.DestroyImmediate(image); Object.DestroyImmediate(cameraGo); rt.Release(); Object.DestroyImmediate(rt);
            }
            File.WriteAllText(Output+"/world-render.txt","24 images: existing world positions/lighting, 6 breeds x 2 outfits x front/rear. Temporary activation restored; no Play.");
        }
    }
}
