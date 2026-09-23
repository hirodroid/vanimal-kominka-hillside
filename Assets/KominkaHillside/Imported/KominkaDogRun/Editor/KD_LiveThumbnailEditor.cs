using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    /// <summary>ライブの詳細の面 (IMP-INF-006 P8) に出す参考画像を、本番のステージから撮る (2026-09-17)。
    /// 商品サムネイル (artifacts/product-thumbnails/2026-09-15) と同じ方法 — 保存済みシーンを一時複製し、犬・UI・自作の他 Renderer を隠し、
    /// Timeline を 30fps 刻みで評価して実 ParticleSystem を同じ刻みで Simulate した Editor キャプチャ。演出の追加や生成画像の補完はしない。
    ///
    /// 使い方: `Temp/live-thumbs/request.json` に {"shots":[{"slug":..,"darkness":..,"times":[..],"position":[x,y,z],"target":[x,y,z],"fov":68}]} を書き、
    /// `Archive/Live Thumbnails/1 - Capture` → `Temp/live-thumbs/<slug>-<time>.png` (正方形 640px)。良い 1 枚を選んで
    /// `Assets/KominkaHillside/Imported/KominkaDogRun/Textures/LiveThumbs/<productKey>.png` へ置き、`2 - Import Settings` で Sprite 化 → Treat System/2 が詳細の面へ配線する。</summary>
    public static class KD_LiveThumbnailEditor
    {
        public const string Output = "Temp/live-thumbs";
        public const string AssetDir = "Assets/KominkaHillside/Imported/KominkaDogRun/Textures/LiveThumbs";
        const int Size = 640;
        [Serializable] class Shot { public string slug; public float darkness = 1f; public float[] times; public float[] position; public float[] target; public float fov = 68f; }
        [Serializable] class Request { public Shot[] shots; }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Live Thumbnails/1 - Capture")]
        public static void Capture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Stopped editor required");
            if (SceneManager.GetActiveScene().isDirty) throw new Exception("Save the scene first (the capture copies the saved file)");
            Directory.CreateDirectory(Output);
            var req = JsonUtility.FromJson<Request>(File.ReadAllText(Output + "/request.json"));
            const string captureScene = "Assets/KominkaHillside/Imported/KominkaDogRun/Scenes/LiveThumbnailCaptureTemp.unity";
            if (!AssetDatabase.CopyAsset("Assets/Scenes/KominkaDogRun.unity", captureScene)) throw new Exception("Capture copy failed");
            Scene s = default(Scene);
            var mats = new List<Material>();
            var previous = RenderTexture.active;
            RenderTexture rt = null;
            var sb = new StringBuilder();
            try
            {
                AssetDatabase.ImportAsset(captureScene, ImportAssetOptions.ForceSynchronousImport);
                s = EditorSceneManager.OpenScene(captureScene, OpenSceneMode.Additive);
                EditorSceneManager.SetSceneCullingMask(s, 1UL << 60);
                rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                var ts = s.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
                foreach (var a in ts.SelectMany(t => t.GetComponents<AudioSource>())) a.enabled = false;
                foreach (var c in ts.SelectMany(t => t.GetComponents<Camera>())) c.enabled = false;
                foreach (var c in ts.SelectMany(t => t.GetComponents<Canvas>())) c.enabled = false;
                var night = ts.Select(t => t.GetComponent<KD_LiveNight>()).First(n => n);
                var custom = ts.Single(t => t.name == "====KominkaDogRun====");
                var stages = ts.Where(t => t.name.StartsWith(KD_DogDefs.LiveStagePrefix)).ToArray();
                foreach (var r in custom.GetComponentsInChildren<Renderer>(true))
                    if (!stages.Any(st => r.transform.IsChildOf(st)) && r != night.overlayRenderer) r.enabled = false;
                foreach (var r in ts.Select(t => t.GetComponent<SkinnedMeshRenderer>()).Where(r => r)) r.enabled = false;
                foreach (var r in night.hideRenderers.Where(r => r)) r.enabled = false;
                foreach (var p in night.hideParticles.Where(p => p)) p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var overlay = new Material(night.overlayRenderer.sharedMaterial); mats.Add(overlay);
                night.overlayRenderer.sharedMaterial = overlay; night.overlayRenderer.enabled = true;
                for (var t = night.overlayRenderer.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
                var go = new GameObject("KD_LiveThumbCamera"); SceneManager.MoveGameObjectToScene(go, s);
                var cam = go.AddComponent<Camera>(); cam.overrideSceneCullingMask = 1UL << 60; cam.enabled = false; cam.allowHDR = true; cam.allowMSAA = true;
                cam.nearClipPlane = .05f; cam.farClipPlane = 200; cam.cullingMask = ~(1 << 5); cam.targetTexture = rt;
                var skybox = go.AddComponent<Skybox>();
                var volumes = ts.Select(t => t.GetComponent<PostProcessVolume>()).Where(v => v && v.enabled && v.gameObject.activeInHierarchy && v.sharedProfile).ToArray();
                if (volumes.Length > 0)
                {
                    var layer = go.AddComponent<PostProcessLayer>();
                    layer.Init(AssetDatabase.LoadAssetAtPath<PostProcessResources>("Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset"));
                    layer.volumeTrigger = go.transform; int mask = 0; foreach (var v in volumes) mask |= 1 << v.gameObject.layer; layer.volumeLayer = mask;
                    layer.antialiasingMode = PostProcessLayer.Antialiasing.None;
                }
                foreach (var shot in req.shots)
                {
                    var stage = stages.Single(t => t.name == KD_DogDefs.LiveStageNameOf(shot.slug));
                    foreach (var t in stages) t.gameObject.SetActive(t == stage);
                    var sky = new Material(night.skyMaterial); mats.Add(sky);
                    sky.SetFloat("_Exposure", Mathf.Lerp(KD_LiveNight.DaySkyExposure, KD_LiveNight.NightSkyExposure, shot.darkness));
                    sky.SetColor("_Tint", Color.Lerp(new Color(.5f, .5f, .5f, .5f), new Color(.3f, .34f, .5f, .5f), shot.darkness));
                    skybox.material = sky; overlay.SetFloat("_Darkness", shot.darkness);
                    foreach (var r in stage.GetComponentsInChildren<Renderer>(true))
                    { var ms = r.sharedMaterials; for (int j = 0; j < ms.Length; j++) { if (!ms[j]) continue; ms[j] = new Material(ms[j]); mats.Add(ms[j]); } r.sharedMaterials = ms; }
                    cam.fieldOfView = shot.fov;
                    cam.transform.position = stage.TransformPoint(new Vector3(shot.position[0], shot.position[1], shot.position[2]));
                    cam.transform.LookAt(stage.TransformPoint(new Vector3(shot.target[0], shot.target[1], shot.target[2])));
                    var director = stage.GetComponent<PlayableDirector>(); director.timeUpdateMode = DirectorUpdateMode.Manual; director.RebuildGraph();
                    var particles = stage.GetComponentsInChildren<ParticleSystem>(true);
                    var wasActive = new bool[particles.Length];
                    foreach (var p in particles) { p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); p.useAutoRandomSeed = false; p.randomSeed = 12345; }
                    double evaluated = 0;
                    foreach (float time in shot.times.OrderBy(t => t))
                    {
                        while (evaluated < time - .00001)
                        {
                            float dt = (float)Math.Min(1.0 / 30, time - evaluated); evaluated += dt; director.time = evaluated; director.Evaluate();
                            for (int j = 0; j < particles.Length; j++)
                            {
                                var p = particles[j]; bool on = p.gameObject.activeInHierarchy;
                                if (on) p.Simulate(dt, false, !wasActive[j], false);
                                else if (wasActive[j]) p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                                wasActive[j] = on;
                            }
                        }
                        cam.Render(); RenderTexture.active = rt; var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                        string file = shot.slug + "-" + time.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ".png";
                        File.WriteAllBytes(Output + "/" + file, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
                        sb.AppendLine(file + " particles=" + particles.Sum(p => p.particleCount) + " activeRenderers=" + stage.GetComponentsInChildren<Renderer>().Count(r => r.enabled));
                    }
                    director.Stop();
                }
                File.WriteAllText(Output + "/capture.txt", sb.ToString());
            }
            catch (Exception e) { File.WriteAllText(Output + "/error.txt", e.ToString()); throw; }
            finally
            {
                RenderTexture.active = previous;
                if (s.IsValid() && s.isLoaded) EditorSceneManager.CloseScene(s, true);
                AssetDatabase.DeleteAsset(captureScene);
                if (rt) Object.DestroyImmediate(rt);
                foreach (var m in mats) Object.DestroyImmediate(m);
            }
        }

        /// <summary>Textures/LiveThumbs/*.png を Sprite (512px 上限、ミップ無し、圧縮) にする。ファイル名 = productKey (例 live.happy_tails.png)。</summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Live Thumbnails/2 - Import Settings")]
        public static void ImportSettings()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { AssetDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false; importer.maxTextureSize = 512; importer.textureCompression = TextureImporterCompression.Compressed;
                importer.sRGBTexture = true; importer.alphaIsTransparency = false; importer.alphaSource = TextureImporterAlphaSource.None;
                importer.SaveAndReimport(); n++;
            }
            Debug.Log("[KD_LiveThumbnail] import settings applied: " + n);
        }

        /// <summary>商品キーの Sprite (無ければ null)。Treat Setup が liveProducts の順で配線する。</summary>
        public static Sprite SpriteFor(string productKey)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(AssetDir + "/" + productKey + ".png");
        }
    }
}
