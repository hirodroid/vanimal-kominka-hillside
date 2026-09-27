using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UdonSharp;

namespace KominkaHillside.Editor
{
    // IMP-DOG-020 steps 2-3. CHECKPOINT (2026-09-27): only the program-asset step exists so far. The U# program asset
    // of KH_WaterSpot must exist before any U# behaviour that references the type can compile (vrc-udonsharp §2).
    // The spot build / verify come in the next edit.
    public static class KH_WaterSpotEditor
    {
        const string ProgramPath = "Assets/KominkaHillside/Scripts/KH_WaterSpot";
        const string OutDir = "Temp/imp-dog-020";

        [MenuItem("Tools/KominkaHillside/Course/76 - Build Water Spot")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath + ".asset") == null)
            {
                var created = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                created.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(ProgramPath + ".cs");
                if (created.sourceCsScript == null) throw new InvalidOperationException("KH_WaterSpot.cs not imported yet; refresh first");
                AssetDatabase.CreateAsset(created, ProgramPath + ".asset"); AssetDatabase.SaveAssets();
                UdonSharpProgramAsset.CompileAllCsPrograms(true);
                Directory.CreateDirectory(OutDir);
                File.WriteAllText(OutDir + "/water-spot-build.json", "{\"status\":\"PROGRAM_ASSET_CREATED\",\"reason\":\"Created KH_WaterSpot.asset; scene unchanged. Wait for the U# compile.\"}");
                Debug.Log("[KH Water Spot] Created KH_WaterSpot.asset; scene unchanged. Wait for the U# compile.");
                return;
            }
            throw new InvalidOperationException("Water spot build not implemented yet (IMP-DOG-020 checkpoint); nothing changed");
        }
    }
}
