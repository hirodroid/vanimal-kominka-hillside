using System;
using System.IO;
using System.Linq;
using System.Text;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    public static class KH_PCFoamPlatformCompileEditor
    {
        [MenuItem("Tools/KominkaHillside/PC Foam/24 - Check Platform Udon")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||BuildPipeline.isBuildingPlayer)throw new InvalidOperationException("Idle Edit required");
            RequireSavedPrograms();
            var log=new StringBuilder("Compile target options only; no active-target switch or mobile scene build.\n");
            try
            {
                foreach(var target in new[]{BuildTarget.Android,BuildTarget.iOS,BuildTarget.StandaloneWindows64})
                {
                    UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions{IsEditorBuild=false,CurrentBuildTarget=target});
                    if(UdonSharpProgramAsset.AnyUdonSharpScriptHasError())throw new InvalidOperationException("Udon errors: "+target);
                    var asset=AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Assets/KominkaHillside/Scripts/KH_DogWash.asset");
                    var symbols=asset.GetRealProgram().SymbolTable.GetSymbols().Where(s=>s.IndexOf("pcFoam",StringComparison.OrdinalIgnoreCase)>=0).ToArray();
                    log.AppendLine(target+" foamSymbols="+symbols.Length+" "+string.Join(",",symbols));
                    if((target==BuildTarget.StandaloneWindows64)!=(symbols.Length>0))throw new InvalidOperationException("Unexpected PC foam symbols: "+target);
                }
                log.AppendLine("PASS platform conditional compilation");
            }
            catch(Exception ex){log.AppendLine("FAIL "+ex);Debug.LogError(ex);}
            finally
            {
                try
                {
                    RestoreEditorPrograms();
                    log.AppendLine("PASS Editor compile restored and generated assets saved");
                }
                catch(Exception ex){log.AppendLine("FAIL Editor restore "+ex);}
                Directory.CreateDirectory("Temp/PCFoamProof");File.WriteAllText("Temp/PCFoamProof/platform-compile.txt",log.ToString());
            }
        }

        internal static void RequireSavedPrograms()
        {
            var dirty=UdonSharpProgramAsset.GetAllUdonSharpPrograms().Where(p=>AssetDatabase.GetAssetPath(p).StartsWith("Assets/KominkaHillside/",StringComparison.Ordinal))
                .Where(p=>EditorUtility.IsDirty(p)||(p.GetSerializedUdonProgramAsset()!=null&&EditorUtility.IsDirty(p.GetSerializedUdonProgramAsset())))
                .Select(AssetDatabase.GetAssetPath).ToArray();
            if(dirty.Length>0)throw new InvalidOperationException("Inspect/save existing dirty generated Udon assets before a cross-target test: "+string.Join(",",dirty));
        }

        // Only compiler-owned outputs, not SaveAssets (which could save another session's scene/material changes).
        public static void RestoreEditorPrograms()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||BuildPipeline.isBuildingPlayer)throw new InvalidOperationException("Idle Edit required");
            UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions{IsEditorBuild=true,CurrentBuildTarget=EditorUserBuildSettings.activeBuildTarget});
            if(UdonSharpProgramAsset.AnyUdonSharpScriptHasError())throw new InvalidOperationException("Editor restore compile failed");
            var log=new StringBuilder();
            foreach(var program in UdonSharpProgramAsset.GetAllUdonSharpPrograms())
            {
                if(!AssetDatabase.GetAssetPath(program).StartsWith("Assets/KominkaHillside/",StringComparison.Ordinal))continue;
                AssetDatabase.SaveAssetIfDirty(program);log.AppendLine(AssetDatabase.GetAssetPath(program));
                var serialized=program.GetSerializedUdonProgramAsset();
                if(serialized!=null){AssetDatabase.SaveAssetIfDirty(serialized);log.AppendLine(AssetDatabase.GetAssetPath(serialized));}
            }
            var wash=AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Assets/KominkaHillside/Scripts/KH_DogWash.asset");
            bool pc=EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android&&EditorUserBuildSettings.activeBuildTarget!=BuildTarget.iOS;
            if(wash.GetRealProgram().SymbolTable.HasAddressForSymbol("pcFoamRenderer")!=pc||wash.fieldDefinitions.ContainsKey("pcFoamRenderer")!=pc)
                throw new InvalidOperationException("Editor field definitions/program mismatch");
            Directory.CreateDirectory("Temp/PCFoamProof");File.WriteAllText("Temp/PCFoamProof/editor-program-restore.txt","PASS Editor generated assets saved; PC="+pc+"\n"+log);
        }
    }
}
