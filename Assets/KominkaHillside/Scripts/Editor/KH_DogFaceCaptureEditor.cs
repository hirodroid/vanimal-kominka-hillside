using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // IMP-DOG-018 (user 2026-09-26 "いま使っている犬のモデルのスクリーンショットでよい"): one face picture per dog for
    // its frisbee disc. The six dogs are photographed where they stand in the world scene (Edit mode, no Play), from
    // a little in front of and above the head, with a temporary orthographic camera that sees the dog layer only.
    // The background is the dog's disc colour, so the round face print on the disc (KH_FrisbeePrototypeEditor
    // BuildDisc: FaceTop / FaceBottom) blends into the rim. No circular mask: the disc mesh cuts the circle.
    // Source: renders of the purchased TORINOSUKE models; the rights record is in the IMP-DOG-018 issue.
    public static class KH_DogFaceCaptureEditor
    {
        public const string TextureDir = "Assets/KominkaHillside/Textures/Frisbee";
        public const int Size = 256;
        const string WorldScene = "Assets/Scenes/VRCDefaultWorldScene.unity";
        const int DogLayer = 14;
        const string LogDir = "Temp/KH_Frisbee";

        // Framing, all in units of the dog's head size (|nose - head bone|, KH_DogRig), so one rule fits every breed.
        // Provisional until the six PNGs have been looked at; ears and fluffy heads (Pome / Samoyed) may want more room.
        const float ElevationDeg = 15f;      // camera a little above the muzzle line
        const float FrameHalfHeight = 1.9f;  // orthographic size / headScale
        const float CameraDistance = 8f;     // camera distance / headScale (orthographic: only clipping depends on it)
        const float NearDepth = 3f;          // clip everything more than this in front of the face centre (other dogs, grass)
        const float FarDepth = 4f;           // and more than this behind it (the body, the ground)
        const float TargetAlongMuzzle = .35f;// face centre = head + (nose - head) * this ...
        const float TargetUp = .35f;         // ... + up * headScale * this (eye height)
        const float OwnPartsRadius = .6f;    // layer-14 renderers outside the dog but this close to its head (its collar) stay visible

        public static string TexturePathFor(string key) { return TextureDir + "/KH_DogFace_" + key + ".png"; }

        [MenuItem("Tools/KominkaHillside/Frisbee/6 Capture Dog Faces")]
        public static void CaptureDogFaces()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != WorldScene) throw new InvalidOperationException("Open the hillside scene (" + WorldScene + ") as the active scene first.");
            string[] keys = KH_DogWalkingEditor.FrisbeeKeys;

            // Resolve every dog before rendering anything, so a missing dog stops the run with no half-written set.
            var dogs = new Transform[keys.Length]; var heads = new Transform[keys.Length]; var noses = new Transform[keys.Length];
            var bad = new List<string>();
            for (int i = 0; i < keys.Length; i++)
            {
                dogs[i] = FindDog(scene, "KD_Dog_" + keys[i], bad);
                if (dogs[i] == null) continue;
                var rig = KH_DogRig.Resolve(dogs[i].gameObject, KH_FrisbeeJumpClipEditor.MouthAnchorName);
                if (rig.head == null || rig.nose == null) { bad.Add(keys[i] + ": head/nose bone not resolved (" + string.Join("; ", rig.problems) + ")"); continue; }
                heads[i] = rig.head; noses[i] = rig.nose;
            }
            if (bad.Count > 0) throw new InvalidOperationException("Capture Dog Faces: " + string.Join(" | ", bad));

            EnsureFolder(TextureDir);
            var log = new StringBuilder();
            log.AppendLine("# dog face capture " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " scene=" + scene.path);

            // Lighting: the scene's own lights. Only when no enabled directional light reaches the dog layer is a
            // temporary one added (and removed again below).
            GameObject tempLight = null;
            bool lit = false;
            foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>())
                if (l.enabled && l.gameObject.activeInHierarchy && l.type == LightType.Directional && (l.cullingMask & (1 << DogLayer)) != 0) { lit = true; break; }
            if (!lit)
            {
                tempLight = new GameObject("KH_DogFaceCapture_Light") { hideFlags = HideFlags.HideAndDontSave };
                var dl = tempLight.AddComponent<Light>(); dl.type = LightType.Directional; dl.intensity = 1f; dl.shadows = LightShadows.None;
                tempLight.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                log.AppendLine("INFO no directional light reaches layer " + DogLayer + "; a temporary one was added for the capture");
            }

            var camGo = new GameObject("KH_DogFaceCapture_Camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false; cam.orthographic = true; cam.cullingMask = 1 << DogLayer;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.allowHDR = false; cam.allowMSAA = true;
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 8 };
            var hidden = new List<Renderer>();
            var prevActive = RenderTexture.active;
            try
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    string key = keys[i];
                    var dog = dogs[i]; Vector3 head = heads[i].position, nose = noses[i].position;
                    float hs = Mathf.Max(.02f, (nose - head).magnitude);   // world head size (KD_DogRig measures it dog-local; the dogs are unscaled)

                    // Facing: the muzzle line (head bone -> nose bone) flattened; the dog root's forward if that is degenerate.
                    Vector3 muzzle = nose - head, flat = Vector3.ProjectOnPlane(muzzle, Vector3.up);
                    string axis = "muzzle";
                    if (flat.sqrMagnitude < 1e-6f) { flat = Vector3.ProjectOnPlane(dog.forward, Vector3.up); axis = "root forward"; }
                    flat.Normalize();
                    float rootAgree = Vector3.Dot(flat, Vector3.ProjectOnPlane(dog.forward, Vector3.up).normalized);

                    Vector3 target = head + muzzle * TargetAlongMuzzle + Vector3.up * hs * TargetUp;
                    float el = ElevationDeg * Mathf.Deg2Rad;
                    Vector3 toCam = flat * Mathf.Cos(el) + Vector3.up * Mathf.Sin(el);
                    float dist = hs * CameraDistance;
                    cam.transform.position = target + toCam * dist;
                    cam.transform.rotation = Quaternion.LookRotation(-toCam, Vector3.up);
                    cam.orthographicSize = hs * FrameHalfHeight;
                    cam.nearClipPlane = Mathf.Max(.01f, dist - hs * NearDepth);
                    cam.farClipPlane = dist + hs * FarDepth;
                    int ci = Array.IndexOf(KH_DogWalkingEditor.FrisbeeKeys, key);
                    Color bg = KH_DogWalkingEditor.DiscColors[ci < 0 ? 0 : ci]; bg.a = 1f;
                    cam.backgroundColor = bg;

                    // Other dogs (and their collars) share the layer: switch their rendering off for this shot only.
                    hidden.Clear();
                    foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                    {
                        if (r.gameObject.layer != DogLayer || r.forceRenderingOff || r.transform.IsChildOf(dog)) continue;
                        if ((r.bounds.center - head).magnitude < OwnPartsRadius) continue;   // its own collar / accessory
                        r.forceRenderingOff = true; hidden.Add(r);
                    }
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                    foreach (var r in hidden) r.forceRenderingOff = false;
                    int hiddenCount = hidden.Count; hidden.Clear();

                    RenderTexture.active = rt;
                    var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false, false);
                    tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); tex.Apply();
                    RenderTexture.active = prevActive;
                    // A frame that is all background means the camera missed the dog: report it, do not hide it.
                    var px = tex.GetPixels32(); var bg32 = (Color32)bg; int differ = 0;
                    for (int p = 0; p < px.Length; p++)
                        if (Mathf.Abs(px[p].r - bg32.r) + Mathf.Abs(px[p].g - bg32.g) + Mathf.Abs(px[p].b - bg32.b) > 12) differ++;
                    string path = TexturePathFor(key);
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                    float cover = differ / (float)px.Length;
                    log.AppendLine((cover < .05f ? "WARN " : "OK   ") + path + " " + Size + "x" + Size + " dog=" + dog.name
                        + " headScale=" + hs.ToString("F3") + " facing=" + axis + " (muzzle . rootForward=" + rootAgree.ToString("F2") + ")"
                        + " dogPixels=" + (cover * 100f).ToString("F0") + "% hiddenOtherRenderers=" + hiddenCount
                        + (cover < .05f ? "  <- almost no dog in the frame; check framing" : ""));
                }
            }
            finally
            {
                foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
                RenderTexture.active = prevActive;
                cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(camGo);
                if (tempLight != null) UnityEngine.Object.DestroyImmediate(tempLight);
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            }

            AssetDatabase.Refresh();
            foreach (string key in keys) ConfigureImporter(TexturePathFor(key), log);
            AssetDatabase.SaveAssets();
            log.AppendLine("next: Tools/KominkaHillside/Walking/3 Build (world discs) and Frisbee/4 Build Scene (prototype), then Frisbee/5 Verify");
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(LogDir + "/dog-faces.txt", log.ToString());
            Debug.Log("[KH Frisbee] " + log);
        }

        // Same rule as Build Size/7 for our own Textures/ (compressed on every platform, BC7 on PC), with the size
        // fixed at 256 on Standalone / Android / iOS. sRGB colour, clamped, mipmaps kept (the disc is seen from afar).
        public static void ConfigureImporter(string path, StringBuilder log)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { log.AppendLine("FAIL no TextureImporter for " + path); return; }
            imp.textureType = TextureImporterType.Default; imp.sRGBTexture = true; imp.alphaSource = TextureImporterAlphaSource.None;
            imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true; imp.isReadable = false;
            imp.maxTextureSize = Size; imp.textureCompression = TextureImporterCompression.CompressedHQ;
            foreach (string platform in new[] { "Standalone", "Android", "iPhone" })
            {
                var s = imp.GetPlatformTextureSettings(platform);
                s.overridden = true; s.maxTextureSize = Size; s.format = TextureImporterFormat.Automatic;
                s.textureCompression = platform == "Standalone" ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Compressed;
                imp.SetPlatformTextureSettings(s);
            }
            imp.SaveAndReimport();
            log.AppendLine("importer " + path + ": sRGB, max " + Size + " (Standalone BC7 / Android, iPhone compressed)");
        }

        public static bool ImporterOk(string path, out string why)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { why = "no importer"; return false; }
            var problems = new List<string>();
            if (!imp.sRGBTexture) problems.Add("not sRGB");
            if (imp.maxTextureSize > Size) problems.Add("default max " + imp.maxTextureSize);
            foreach (string platform in new[] { "Standalone", "Android", "iPhone" })
            {
                var s = imp.GetPlatformTextureSettings(platform);
                if (s.overridden && s.maxTextureSize > Size) problems.Add(platform + " max " + s.maxTextureSize);
                if (s.overridden && s.textureCompression == TextureImporterCompression.Uncompressed) problems.Add(platform + " uncompressed");
            }
            if (imp.textureCompression == TextureImporterCompression.Uncompressed) problems.Add("default uncompressed");
            why = problems.Count == 0 ? "ok" : string.Join(", ", problems);
            return problems.Count == 0;
        }

        static Transform FindDog(Scene scene, string name, List<string> bad)
        {
            Transform found = null; int count = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) { count++; if (found == null) found = t; }
            if (count == 0) bad.Add(name + " not found in " + scene.path);
            else if (count > 1) bad.Add(name + " found " + count + " times (expect 1)");
            else if (!found.gameObject.activeInHierarchy) bad.Add(name + " is inactive (it would not render)");
            return count == 1 ? found : null;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
