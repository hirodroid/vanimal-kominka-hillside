using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;

namespace KominkaDogRun.Editor
{
    // UI/UiMenu require the VRChat menu to be open. Walkthrough keeps world UI
    // pointer input while excluding Player/PlayerLocal physical collisions.
    public static class KD_HandMenuCollisionEditor
    {
        public const int InputLayer = 17;
        const string ReportDir = "Temp/hand-menu-collision";

        public static void Configure(GameObject panel)
        {
            foreach (var shape in panel.GetComponentsInChildren<VRCUiShape>(true))
            {
                if (shape.gameObject.layer == InputLayer) continue;
                Undo.RecordObject(shape.gameObject, "Fix hand menu player collision");
                shape.gameObject.layer = InputLayer;
                EditorUtility.SetDirty(shape.gameObject);
            }
        }

        [MenuItem("Tools/KominkaHillside/Menu/19 Apply Collision Layers")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var panel = FindPanel();
            if (panel == null) throw new System.InvalidOperationException("Hand menu panel not found");
            Configure(panel);
            EditorSceneManager.MarkSceneDirty(panel.scene);
            // Never save the user's unrelated pending scene edits.
            Debug.Log("[HandMenuCollision] Applied to UIShape objects only; scene not saved.");
        }

        public static bool Validate(GameObject panel, StringBuilder log)
        {
            int fail = 0;
            void Check(bool ok, string label) { log.AppendLine((ok ? "PASS " : "FAIL ") + label); if (!ok) fail++; }
            Check(LayerMask.LayerToName(InputLayer) == "Walkthrough", "layer 17 = Walkthrough");
            Check(Physics.GetIgnoreLayerCollision(InputLayer, 9), "Walkthrough ignores Player");
            Check(Physics.GetIgnoreLayerCollision(InputLayer, 10), "Walkthrough ignores PlayerLocal");
            Check(panel != null, "hand menu panel exists");
            if (panel == null) return false;
            Check(panel.GetComponent<VRCUiShape>() != null, "panel UIShape exists");
            var shapes = panel.GetComponentsInChildren<VRCUiShape>(true);
            foreach (var shape in shapes)
            {
                var canvas = shape.GetComponent<Canvas>();
                Check(shape.gameObject.layer == InputLayer, shape.name + " input layer=" + shape.gameObject.layer);
                Check(canvas != null && canvas.renderMode == RenderMode.WorldSpace && shape.GetComponent<GraphicRaycaster>() != null,
                    shape.name + " WorldSpace Canvas and GraphicRaycaster retained");
            }
            var colliders = panel.GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
                Check(collider.isTrigger || (Physics.GetIgnoreLayerCollision(collider.gameObject.layer, 9)
                    && Physics.GetIgnoreLayerCollision(collider.gameObject.layer, 10)), collider.name + " collider cannot block players");
            log.AppendLine("UIShapes=" + shapes.Length + " explicitColliders=" + colliders.Length + " failures=" + fail);
            return fail == 0;
        }

        [MenuItem("Tools/KominkaHillside/Menu/24 Verify Collision")]
        public static void Verify()
        {
            var log = new StringBuilder();
            bool ok = Validate(FindPanel(), log);
            log.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));
            Directory.CreateDirectory(ReportDir);
            File.WriteAllText(ReportDir + "/verify.txt", log.ToString());
            Debug.Log(log.ToString());
        }

        internal static GameObject FindPanel()
        {
            var pouch = KD_SceneObjects.Find("KD_TreatPouch", out int pouchCount);
            var panel = KD_SceneObjects.Find("KD_TreatPouchPanel", out int panelCount);
            return pouchCount == 1 && panelCount == 1 && panel.transform.IsChildOf(pouch.transform) ? panel : null;
        }
    }
}
