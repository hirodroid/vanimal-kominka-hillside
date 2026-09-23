using System;
using System.IO;
using System.Text;
using KominkaDogRun;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Platform;
using VRC.SDKBase.Platform;

namespace KominkaDogRun.Editor
{
    public static class KD_TouchLauncherVerifyEditor
    {
        [MenuItem("Tools/KominkaHillside/Menu/17b Verify Touch Orientation")]
        public static void Verify()
        {
            var original = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewPreviewScene();
            var log = new StringBuilder();
            int fail = 0;
            try
            {
                var host = new GameObject("OrientationProbe");
                SceneManager.MoveGameObjectToScene(host, scene);
                var pouch = host.AddUdonSharpComponent<KD_TreatPouch>();
                var canvas = new GameObject("LayoutViewport", typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(canvas, scene);
                var root = (RectTransform)canvas.transform;
                var button = new GameObject("Launcher", typeof(RectTransform));
                button.transform.SetParent(root, false);
                var rect = (RectTransform)button.transform;
                rect.sizeDelta = new Vector2(220f, 72f);
                pouch.touchLauncher = canvas;
                pouch.touchLauncherButton = rect;
                foreach (var resolution in new[] { new Vector2(1080,1920), new Vector2(1920,1080),
                    new Vector2(1080,2400), new Vector2(2400,1080), new Vector2(768,1024), new Vector2(1024,768) })
                {
                    // CanvasScaler Match=.5, reference 1920x1080. Test in its design units.
                    float scale = Mathf.Sqrt((resolution.x / 1920f) * (resolution.y / 1080f));
                    root.sizeDelta = resolution / scale;
                    bool portrait = resolution.y > resolution.x;
                    pouch.OnScreenUpdate(new ScreenUpdateData { type = ScreenUpdateType.OrientationChanged,
                        orientation = portrait ? VRCOrientation.Portrait : VRCOrientation.Landscape,
                        resolution = resolution });
                    Vector2 anchor = portrait ? new Vector2(1f,.5f) : new Vector2(.5f,1f);
                    Vector2 offset = portrait ? new Vector2(-24f,0f) : new Vector2(0f,-24f);
                    Vector3[] corners = new Vector3[4];
                    rect.GetWorldCorners(corners);
                    bool inside = true;
                    foreach (var c in corners) if (!root.rect.Contains((Vector2)root.InverseTransformPoint(c))) inside = false;
                    bool ok = rect.anchorMin == anchor && rect.anchorMax == anchor && rect.pivot == anchor
                        && rect.anchoredPosition == offset && inside;
                    if (!ok) fail++;
                    log.AppendLine((ok ? "PASS " : "FAIL ") + resolution + " anchor=" + rect.anchorMin
                        + " offset=" + rect.anchoredPosition + " buttonWithinViewport=" + inside);
                }
            }
            catch (Exception e) { fail++; log.AppendLine("FAIL " + e); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            log.AppendLine("RESULT fail=" + fail + " originalScene=" + original.path);
            File.WriteAllText("Temp/ui-touch-orientation.txt", log.ToString());
            Debug.Log(log.ToString());
        }
    }
}
