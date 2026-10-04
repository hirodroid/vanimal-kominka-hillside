using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    /// <summary>BUG-ENV-001: the builders made their materials with new Material("VRChat/Mobile/Standard Lite") and wrote only the
    /// colour, so they kept the shader defaults _Metallic 1 / _Glossiness 1 with reflections and specular highlights off. A full
    /// metal has no diffuse, and with reflections off its only light is the scene's indirect specular colour: blue-ish on PC, black in
    /// the Android / iOS build (Quest 2026-10-04: the discs were black). Matte = _Metallic 0, low gloss, both toggles kept off, so the
    /// colour comes from the ambient light on every platform.
    ///
    /// Materials/1 repairs the existing assets under Data/ (idempotent; assets only, the scene is not touched), Materials/2 checks
    /// that none is left, Materials/3 and 4 render the discs and the leash clips for a before / after comparison (Edit mode, a
    /// temporary camera, nothing saved in the scene). The builders call Matte() where they create a material, so a rebuilt asset
    /// is born matte.</summary>
    public static class KH_MatteMaterialsEditor
    {
        const string Menu = "Tools/KominkaHillside/Materials/";
        const string Folder = "Assets/KominkaHillside/Data";
        const string LiteShader = "VRChat/Mobile/Standard Lite";
        const string FixReportPath = "Temp/kh-matte-fix.json", VerifyReportPath = "Temp/kh-matte-verify.json", CaptureDir = "Temp/kh-matte";
        public const float MatteGlossiness = 0.3f;
        const float MirrorMetallic = 0.9f;

        [Serializable] class Report
        {
            public string utc, result;
            public int scanned, mirror, changed;
            public List<string> items = new List<string>();
        }

        /// <summary>Makes a Standard Lite material matte. Safe on any material (properties it does not have are skipped).</summary>
        public static void Matte(Material m)
        {
            if (m == null) return;
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", MatteGlossiness);
            // [ToggleOff]: 0 = off, and the keyword carries the meaning. new Material() in a script does not run the drawer.
            if (m.HasProperty("_GlossyReflections")) { m.SetFloat("_GlossyReflections", 0f); m.EnableKeyword("_GLOSSYREFLECTIONS_OFF"); }
            if (m.HasProperty("_SpecularHighlights")) { m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); }
            EditorUtility.SetDirty(m);
        }

        static bool IsMirror(Material m)
        {
            if (m == null || m.shader == null || m.shader.name != LiteShader) return false;
            if (!m.HasProperty("_Metallic") || m.GetFloat("_Metallic") < MirrorMetallic) return false;
            return !m.HasProperty("_MetallicGlossMap") || m.GetTexture("_MetallicGlossMap") == null;   // a metallic map is a deliberate metal
        }

        static List<string> LitePaths(Report r)
        {
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                r.scanned++;
                if (IsMirror(m)) paths.Add(path);
            }
            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        [MenuItem(Menu + "1 - Fix Mirror Defaults")]
        public static void Fix()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("[KH Matte] Edit mode only (not compiling)");
            var r = new Report { utc = DateTime.UtcNow.ToString("o") };
            foreach (var path in LitePaths(r))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                r.mirror++;
                r.items.Add(path + " metallic " + m.GetFloat("_Metallic").ToString("F2") + " gloss " + m.GetFloat("_Glossiness").ToString("F2") + " -> 0.00 / " + MatteGlossiness.ToString("F2"));
                Matte(m);
                r.changed++;
            }
            AssetDatabase.SaveAssets();
            r.result = "FIXED";
            Directory.CreateDirectory("Temp");
            File.WriteAllText(FixReportPath, JsonUtility.ToJson(r, true));
            Debug.Log("[KH Matte] fixed " + r.changed + " of " + r.scanned + " materials under " + Folder + " -> " + FixReportPath);
        }

        [MenuItem(Menu + "2 - Verify Mirror Defaults")]
        public static void Verify()
        {
            var r = new Report { utc = DateTime.UtcNow.ToString("o") };
            foreach (var path in LitePaths(r)) { r.mirror++; r.items.Add("FAIL still a mirror: " + path); }
            // every Standard Lite material without a metallic map: matte values and both keywords
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader == null || m.shader.name != LiteShader) continue;
                if (m.HasProperty("_MetallicGlossMap") && m.GetTexture("_MetallicGlossMap") != null) continue;
                bool refl = m.IsKeywordEnabled("_GLOSSYREFLECTIONS_OFF"), spec = m.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF");
                if (!refl || !spec) r.items.Add("NOTE reflections / highlights on (lit by the reflection environment): " + path + " reflOff=" + refl + " specOff=" + spec);
            }
            r.result = r.mirror == 0 ? "PASS" : "FAIL";
            Directory.CreateDirectory("Temp");
            File.WriteAllText(VerifyReportPath, JsonUtility.ToJson(r, true));
            string msg = "[KH Matte] Verify " + r.result + " scanned=" + r.scanned + " mirror=" + r.mirror + " -> " + VerifyReportPath;
            if (r.mirror == 0) Debug.Log(msg); else Debug.LogError(msg);
        }

        [MenuItem(Menu + "3 - Capture Before")]
        public static void CaptureBefore() { Capture("before"); }

        [MenuItem(Menu + "4 - Capture After")]
        public static void CaptureAfter() { Capture("after"); }

        // Two fixed views: the six discs on the plaza and the leash clips on the gate rack. PC editor rendering only; it does not
        // show how Quest / iOS draw them.
        static void Capture(string label)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("[KH Matte] Edit mode only");
            Directory.CreateDirectory(CaptureDir);
            Shot(label + "-discs", new Vector3(75.5f, 20.3f, 130.6f), new Vector3(75.5f, 19.0f, 132.75f), 45f);
            Shot(label + "-clips", new Vector3(45.0f, 2.95f, 42.0f), new Vector3(45.0f, 2.6f, 43.0f), 45f);
            Debug.Log("[KH Matte] captured " + label + " -> " + CaptureDir);
        }

        static void Shot(string name, Vector3 from, Vector3 to, float fov)
        {
            const int W = 960, H = 540;
            var go = new GameObject("KH_MatteCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from, Vector3.up));
                cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 400f;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                File.WriteAllBytes(CaptureDir + "/" + name + ".png", tex.EncodeToPNG());
                cam.targetTexture = null;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
