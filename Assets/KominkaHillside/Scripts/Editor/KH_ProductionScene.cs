using System;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaHillside.Editor
{
    // Production keeps the original filename/GUID. The old short course is a comparison source only.
    public static class KH_ProductionScene
    {
        public const string Path = "Assets/Scenes/VRCDefaultWorldScene.unity";
        public const string LegacyPath = "Assets/Scenes/Archive/KH_HillsideBeforeRiverLoop.unity";

        public static void RejectLegacyRebuild()
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "KH_CourseExtension")
                    throw new InvalidOperationException("This legacy rebuild would overwrite the river loop. Use Tools/KominkaHillside/Course for owned terrain, navigation and route updates; retain the existing 12-row menu.");
        }
    }
}
