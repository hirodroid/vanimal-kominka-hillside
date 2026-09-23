using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UdonSharp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PumpkinProductionEditor
    {
        const string Main = "Assets/Scenes/KominkaDogRun.unity";
        const string Source = "Assets/KominkaHillside/Imported/KominkaDogRun/Scenes/PumpkinParadePreview001.unity";
        const string Slug = "kd-pumpkinparade-002";
        const string Evidence = "Logs/pumpkin-production";
        static void Guard()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount != 1
                || SceneManager.GetActiveScene().path != Main)
                throw new InvalidOperationException("Stopped production scene alone required.");
        }
        public static string CompileRuntime()
        {
            Guard();
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
            var cacheType = typeof(UdonSharpProgramAsset).Assembly.GetType("UdonSharp.UdonSharpEditorCache", true);
            var cache = cacheType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            bool failed = (bool)cacheType.GetMethod("HasUdonSharpCompileError", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(cache, null);
            if (failed) throw new InvalidOperationException("Current UdonSharp diagnostics contain errors.");
            foreach (var pair in new[] { new[] { "KD_LiveSystem", "dogPerformances" }, new[] { "KD_LiveSystem", "dogMountState" },
                new[] { "KD_DogWanderController", "candyMountLive" } })
            {
                var a = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/" + pair[0] + ".asset");
                var p = a == null || a.SerializedProgramAsset == null ? null : a.SerializedProgramAsset.RetrieveProgram();
                if (p == null || !p.SymbolTable.GetSymbols().Contains(pair[1])) throw new InvalidOperationException("Missing compiled symbol " + pair[1]);
            }
            Directory.CreateDirectory(Evidence);
            string report = DateTime.UtcNow.ToString("o") + " current UdonSharp errors=0; registration and mount program symbols verified.";
            File.WriteAllText(Evidence + "/compile.txt", report);
            return report;
        }
    }
}
