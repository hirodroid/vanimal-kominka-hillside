using System;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaDogRun.Editor
{
    // Rebuild only the approved UI. Full system Setup also edits gameplay objects.
    public static class KD_UiRefreshEditor
    {
        [MenuItem("Tools/KominkaHillside/Menu/17 Refresh Menus")]
        public static void Apply()
        {
            KominkaHillside.Editor.KH_ProductionScene.RejectLegacyRebuild();
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != KD_Hillside.ScenePath /* 裏山改変: シーンの正本は KD_Hillside */)
                throw new InvalidOperationException("Open " + KD_Hillside.ScenePath + " in Edit mode.");
            string backup = "Temp/ui-before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity";
            if (!EditorSceneManager.SaveScene(scene, backup, true))
                throw new InvalidOperationException("Could not preserve the current unsaved scene.");
            var log = new StringBuilder("UI only refresh. Backup: " + backup + "\n");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            if (font == null || KD_UiSpriteEditor.LoadRound() == null)
                throw new InvalidOperationException("Shared UI font/sprite missing.");
            // Bake all declared UI words; use the source constants, never a second copy of the text.
            // 裏山改変: 字の宣言は PouchWords + HillsideWords に一本化 (D-4。古民家は 3 型の const を全部焼いていた)
            KD_TreatSetupEditor.RestoreFontSource(log);
            KD_ShopSetupEditor.AddGlyphs(KD_TreatSetupEditor.PouchWords() + KD_TreatSetupEditor.HillsideWords(), log);
            foreach (var shelf in Resources.FindObjectsOfTypeAll<KD_ShopShelf>())
            {
                if (shelf.gameObject.scene != scene || shelf.confirmRoot == null) continue;
                CheckCustom(shelf);
                var rot = shelf.confirmRoot.transform.localEulerAngles.y;
                float height = shelf.confirmHeight;
                if (shelf.items != null && shelf.items.Length > 0 && shelf.items[0] != null && shelf.items[0].IsStocked()) height = 0.22f;
                KD_ShopSetupEditor.EnsureConfirmPanel(shelf.gameObject, shelf, font, rot, height, log);
                Copy(shelf);
                log.AppendLine("shelf=" + shelf.name);
            }
            foreach (var pouch in Resources.FindObjectsOfTypeAll<KD_TreatPouch>())
            {
                if (pouch.gameObject.scene != scene) continue;
                CheckCustom(pouch);
                KD_TreatSetupEditor.BuildPouchPanel(pouch.gameObject, pouch, pouch.items, pouch.fxItems, font, log);
                Copy(pouch);
                log.AppendLine("pouch=" + pouch.name);
            }
            foreach (var keeper in Resources.FindObjectsOfTypeAll<KD_ShopKeeper>())
            {
                if (keeper.gameObject.scene != scene) continue;
                CheckCustom(keeper);
                // The placement was adjusted by the user. Keep the existing pivot pose.
                var pivot = keeper.menuRoot != null ? keeper.menuRoot.transform.parent : null;
                Vector3 pos = pivot != null ? pivot.localPosition : Vector3.zero;
                Quaternion rot = pivot != null ? pivot.localRotation : Quaternion.identity;
                KD_ShopKeeperSetupEditor.BuildMenu(keeper.gameObject, keeper,
                    UdonSharpEditorUtility.GetBackingUdonBehaviour(keeper), font, log);
                if (pivot != null) { pivot.localPosition = pos; pivot.localRotation = rot; }
                Copy(keeper);
                log.AppendLine("keeper=" + keeper.name);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/ui-refresh.txt", log.ToString());
            Debug.Log(log.ToString());
        }

        static void CheckCustom(UdonSharpBehaviour behaviour)
        {
            // 裏山改変: 自作の器は KH_Dogs の下 (古民家は "====KominkaDogRun====")
            if (behaviour.transform.root.name != KD_Hillside.DogsRootName || PrefabUtility.IsPartOfPrefabInstance(behaviour))
                throw new InvalidOperationException("Outside custom scene objects: " + behaviour.name);
        }
        static void Copy(UdonSharpBehaviour behaviour)
        {
            UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
            EditorUtility.SetDirty(behaviour);
            EditorUtility.SetDirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour));
        }
    }
}
