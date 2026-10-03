using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KominkaHillside.Editor
{
    // Guide/14: review shots of the three walk strips without Play (onsite-guides §7.1), as Guide/3 does for the boards: 1200 x 800
    // at a 40° vertical field of view = 20 px per degree (about a Quest 2's centre). Per station and per variant (VR = the saved
    // state; Flat = a hidden copy of the canvas showing the Flat group, 3 mm in front of the real one with an opaque background):
    // standing 1.2 m in front, seated 1.0 m, 35° oblique at 1.5 m, and from where its readers come (spawn / viewpoint / plaza
    // centre). Only HideAndDontSave objects are made and destroyed; the scene's dirty flag is compared before and after.
    public static partial class KH_WalkGuideEditor
    {
        const int ShotW = 1200, ShotH = 800;
        const float ShotFov = 40f, EyeStanding = 1.6f, EyeSeated = 1.15f;

        [MenuItem(Menu + "14 - Capture Walk Guide Strips")]
        public static void Capture()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var c = Resolve(false);
            var scene = EditorSceneManager.GetActiveScene();
            bool dirtyBefore = scene.isDirty;
            string dir = OutDir + "/shots";
            Directory.CreateDirectory(dir);
            var lines = new List<string> { "# " + DateTime.UtcNow.ToString("o") + "  " + ShotW + "x" + ShotH + ", vertical FOV " + ShotFov + " deg = " + (ShotH / ShotFov).ToString("F0") + " px/deg; eye standing " + EyeStanding + " m, seated " + EyeSeated + " m above the terrain" };
            for (int i = 0; i < Stations.Length; i++)
            {
                var spec = Stations[i];
                var canvasT = c.stations[i].Find(StripName + "/" + CanvasName);
                if (canvasT == null) { lines.Add(spec.name + ": no strip (run Guide/11)"); continue; }
                foreach (bool flat in new[] { false, true })
                {
                    GameObject copy = null;
                    try
                    {
                        if (flat) copy = FlatCopy(canvasT);
                        Canvas.ForceUpdateCanvases();
                        string tag = spec.name + (flat ? "_flat" : "_vr");
                        Vector3 front = -canvasT.forward; front.y = 0f; front.Normalize();
                        Vector3 side = Vector3.Cross(Vector3.up, front);
                        Vector3 centre = canvasT.position;
                        Shot(dir, tag + "_stand_1.2m", Eye(centre + front * 1.2f, EyeStanding), centre, lines);
                        Shot(dir, tag + "_seated_1.0m", Eye(centre + front * 1.0f, EyeSeated), centre, lines);
                        Shot(dir, tag + "_oblique35_1.5m", Eye(centre + (front * Mathf.Cos(35f * Mathf.Deg2Rad) + side * Mathf.Sin(35f * Mathf.Deg2Rad)) * 1.5f, EyeStanding), centre, lines);
                        var target = TargetOf(spec);
                        if (target.HasValue && !flat) Shot(dir, tag + "_from_" + spec.target, Eye(target.Value, EyeStanding), centre, lines);
                    }
                    finally { if (copy != null) UnityEngine.Object.DestroyImmediate(copy); }
                }
            }
            File.WriteAllLines(dir + "/shots.txt", lines);
            bool dirtyAfter = EditorSceneManager.GetActiveScene().isDirty;
            if (!dirtyBefore && dirtyAfter) Debug.LogWarning("[KH Walk Guide] the capture left the scene marked dirty (only hidden temporary objects were made): reopen it without saving");
            Debug.Log("[KH Walk Guide] CAPTURED " + (lines.Count - 1) + " shots -> " + dir + " (positions in shots.txt)");
        }

        /// <summary>A hidden copy of the canvas showing the Flat variant, 3 mm in front of the real one, its background opaque.</summary>
        static GameObject FlatCopy(Transform canvasT)
        {
            var copy = UnityEngine.Object.Instantiate(canvasT.gameObject, canvasT.position - canvasT.forward * 0.003f, canvasT.rotation);
            copy.transform.localScale = canvasT.lossyScale;
            foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            copy.transform.Find(VrName).gameObject.SetActive(false);
            copy.transform.Find(FlatName).gameObject.SetActive(true);
            var bg = copy.transform.Find(BgName)?.GetComponent<Image>();
            if (bg != null) { var col = bg.color; col.a = 1f; bg.color = col; }
            foreach (var t in copy.GetComponentsInChildren<TextMeshProUGUI>(true)) t.ForceMeshUpdate(true);
            return copy;
        }

        static Vector3 Eye(Vector3 at, float eye)
        {
            float g = Ground(at);
            return new Vector3(at.x, (float.IsNaN(g) ? at.y : g) + eye, at.z);
        }

        static void Shot(string dir, string name, Vector3 pos, Vector3 look, List<string> lines)
        {
            string path = dir + "/" + name + ".png";
            var go = new GameObject("KH_WalkGuideShotCam") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 4 };
            var tex = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            var prev = RenderTexture.active;
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.transform.position = pos;
                cam.transform.LookAt(look);
                cam.fieldOfView = ShotFov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 800f; cam.allowHDR = false;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0);
                tex.Apply(false);
                cam.targetTexture = null;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                lines.Add(name + ": eye " + V(pos) + " -> " + V(look) + " (" + Vector3.Distance(pos, look).ToString("F2") + " m)");
            }
            finally
            {
                RenderTexture.active = prev;
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
