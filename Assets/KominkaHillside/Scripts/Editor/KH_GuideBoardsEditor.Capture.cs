using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    // Guide/3: renders each board from the positions it is designed to be read from, plus the spawn's views, to judge legibility
    // without Play. 1200 x 800 at a 40 deg vertical field of view = 20 px per degree (about a Quest 2's centre; PCVR headsets are
    // similar or sharper). The scene is not changed (the cameras are HideAndDontSave). The labels show the saved (ja) text.
    public static partial class KH_GuideBoardsEditor
    {
        const int ShotW = 1200, ShotH = 800;
        const float ShotFov = 40f;

        [MenuItem(Menu + "3 - Capture Guide Boards")]
        public static void Capture()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var c = Resolve(false);
            if (c.root == null) throw new InvalidOperationException(RootName + " not in the scene: run Guide/1 first");
            var a = FindAnchors(c);
            string dir = OutDir + "/shots";
            Directory.CreateDirectory(dir);
            Canvas.ForceUpdateCanvases();
            var lines = new List<string> { "# " + DateTime.UtcNow.ToString("o") + "  " + ShotW + "x" + ShotH + ", vertical FOV " + ShotFov + " deg = " + (ShotH / ShotFov).ToString("F0") + " px/deg; eye standing " + EyeStanding + " m, seated " + EyeSeated + " m above the terrain" };
            foreach (var b in Boards)
            {
                var t = c.root.Find(b.name);
                if (t == null) { lines.Add(b.name + ": missing"); continue; }
                var target = t.TransformPoint(new Vector3(0f, CanvasCenterY(b), 0f));
                Shot(dir, b.name + "_read_1.8m", Eye(t, new Vector3(0f, 0f, -ReadDistance), EyeStanding), target, lines);
                Shot(dir, b.name + "_read_3.0m", Eye(t, new Vector3(0f, 0f, -3.0f), EyeStanding), target, lines);
                Shot(dir, b.name + "_seated_1.4m", Eye(t, new Vector3(0f, 0f, -1.4f), EyeSeated), target, lines);
                float s = Mathf.Sin(35f * Mathf.Deg2Rad) * 2.0f, k = Mathf.Cos(35f * Mathf.Deg2Rad) * 2.0f;
                Shot(dir, b.name + "_oblique35_2.0m", Eye(t, new Vector3(-s, 0f, -k), EyeStanding), target, lines);
                if (b == Asob)
                {
                    // the leftmost / rightmost paper up close (the body text's smallest size)
                    var left = t.TransformPoint(new Vector3(PaperX[0] * Mm, b.canvasBottom + (b.canvasMm.y * 0.5f + PaperY) * Mm, 0f));
                    var right = t.TransformPoint(new Vector3(PaperX[3] * Mm, b.canvasBottom + (b.canvasMm.y * 0.5f + PaperY) * Mm, 0f));
                    Shot(dir, b.name + "_paper_walk_1.2m", Eye(t, new Vector3(PaperX[0] * Mm, 0f, -1.2f), EyeStanding), left, lines);
                    Shot(dir, b.name + "_paper_shop_1.2m", Eye(t, new Vector3(PaperX[3] * Mm, 0f, -1.2f), EyeStanding), right, lines);
                }
                if (a.spawn != null) Shot(dir, b.name + "_from_spawn", a.spawn.position + Vector3.up * EyeStanding, target, lines);
            }
            if (a.spawn != null)
            {
                var eye = a.spawn.position + Vector3.up * EyeStanding;
                var fwd = a.spawn.forward; fwd.y = 0f; fwd.Normalize();
                Shot(dir, "context_spawn_forward", eye, eye + fwd * 10f, lines);
                Shot(dir, "context_spawn_right60", eye, eye + Quaternion.Euler(0f, 60f, 0f) * fwd * 10f, lines);
                if (a.kominka != null) Shot(dir, "context_spawn_back_farmhouse", eye, a.kominka.position + Vector3.up * 3f, lines);
            }
            if (a.route != null && a.kominka != null && a.route.Length > 8)
                for (int i = a.route.Length - 4; i <= a.route.Length - 3; i++)
                    Shot(dir, "context_return_node" + i + "_farmhouse", a.route[i] + Vector3.up * EyeStanding, a.kominka.position + Vector3.up * 3f, lines);
            File.WriteAllLines(dir + "/shots.txt", lines);
            Debug.Log("[KH Guide Boards] CAPTURED " + (lines.Count - 1) + " shots -> " + dir + " (positions in shots.txt)");
        }

        static Vector3 Eye(Transform board, Vector3 local, float eye)
        {
            var p = board.TransformPoint(local);
            p.y = Ground(p) + eye;
            return p;
        }

        static void Shot(string dir, string name, Vector3 pos, Vector3 look, List<string> lines)
        {
            string path = dir + "/" + name + ".png";
            var go = new GameObject("KH_GuideShotCam") { hideFlags = HideFlags.HideAndDontSave };
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
