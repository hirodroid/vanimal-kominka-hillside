using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // A saved production-scene bundle proves callback reachability; asset-only bundles measure group cost separately.
    public static class KH_PCFoamBundleProofEditor
    {
        [Serializable] public class Report
        {
            public string run, status, error, target, sceneHashBefore, sceneHashAfter;
            public int processed;
            public string processedTarget;
            public bool mobileDependenciesRemoved;
            public long sceneDownloadBytes, addedGroupDownloadBytes, addedGroupRawBytes;
            public string limitations="Local scene AssetBundle with explicit target, not SDK upload or device test. PC group bytes include referenced dependencies, not whole-world delta or resident memory. No CPU/GPU or PCVR acceptance.";
        }
        static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
        [MenuItem("Tools/KominkaHillside/PC Foam/23 - Prove Scene Bundle")]
        public static void Run()=>RunTarget(BuildTarget.StandaloneWindows64);
        [MenuItem("Tools/KominkaHillside/PC Foam/25 - Prove Android Scene Bundle")]
        public static void Android()=>RunTarget(BuildTarget.Android);
        [MenuItem("Tools/KominkaHillside/PC Foam/26 - Prove iOS Scene Bundle")]
        public static void IOS()=>RunTarget(BuildTarget.iOS);
        static void RunTarget(BuildTarget target)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||BuildPipeline.isBuildingPlayer)throw new InvalidOperationException("Idle Edit required");
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=KH_PCFoamSetupEditor.ScenePath||scene.isDirty||EditorUserBuildSettings.activeBuildTarget!=BuildTarget.StandaloneWindows64)throw new InvalidOperationException("Saved production scene on Windows required");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Unsaved scene");
            KH_PCFoamSetupEditor.Verify();
            KH_PCFoamPlatformCompileEditor.RequireSavedPrograms();
            var report=new Report{run=Guid.NewGuid().ToString("N"),target=target.ToString(),status="RUNNING",sceneHashBefore=Hash(scene.path)};
            string output="Temp/PCFoamProof/bundles/"+report.run;
            Directory.CreateDirectory(output);File.WriteAllText(output+"/report.json",JsonUtility.ToJson(report,true));
            var subtarget=EditorUserBuildSettings.androidBuildSubtarget;
            try
            {
                UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions{IsEditorBuild=false,CurrentBuildTarget=target});
                if(UdonSharpProgramAsset.AnyUdonSharpScriptHasError())throw new InvalidOperationException("Target Udon compile failed");
                if(target==BuildTarget.Android)EditorUserBuildSettings.androidBuildSubtarget=MobileTextureSubtarget.ASTC;
                KH_PCFoamBuildEditor.ProofRun=report.run;KH_PCFoamBuildEditor.ProofProcessed=0;
                KH_PCFoamBuildEditor.ProofProcessedTarget=BuildTarget.NoTarget;KH_PCFoamBuildEditor.ProofMobileStripped=false;
                report.sceneDownloadBytes=Bundle(output+"/scene",new[]{scene.path},BuildAssetBundleOptions.ForceRebuildAssetBundle,target);
                report.processed=KH_PCFoamBuildEditor.ProofProcessed;
                report.processedTarget=KH_PCFoamBuildEditor.ProofProcessedTarget.ToString();report.mobileDependenciesRemoved=KH_PCFoamBuildEditor.ProofMobileStripped;
                if(report.processed!=6||KH_PCFoamBuildEditor.ProofProcessedTarget!=target
                    ||(target!=BuildTarget.StandaloneWindows64&&!report.mobileDependenciesRemoved))
                    throw new InvalidOperationException("Actual scene callback did not process six washes for requested target: "+report.processed+" "+report.processedTarget);
                KH_PCFoamBuildEditor.ProofRun=null;
                if(target==BuildTarget.StandaloneWindows64)
                {
                    var assets=AssetDatabase.FindAssets("",new[]{KH_PCFoamGenerateEditor.Folder}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>p.EndsWith(".asset")||p.EndsWith(".mat")).ToArray();
                    report.addedGroupDownloadBytes=Bundle(output+"/group",assets,BuildAssetBundleOptions.None,target);
                    report.addedGroupRawBytes=Bundle(output+"/raw",assets,BuildAssetBundleOptions.UncompressedAssetBundle,target);
                }
                report.status="PASS";
            }
            catch(Exception ex){report.status="FAIL";report.error=ex.ToString();Debug.LogError(ex);}
            finally
            {
                EditorUserBuildSettings.androidBuildSubtarget=subtarget;
                KH_PCFoamBuildEditor.ProofRun=null;report.sceneHashAfter=Hash(scene.path);
                if(report.sceneHashBefore!=report.sceneHashAfter||scene.isDirty){report.status="FAIL";report.error+=" Authoring scene changed";}
                try
                {
                    KH_PCFoamPlatformCompileEditor.RestoreEditorPrograms();
                }
                catch(Exception ex){report.status="FAIL";report.error+=" "+ex;}
                report.sceneHashAfter=Hash(scene.path);
                if(report.sceneHashBefore!=report.sceneHashAfter||scene.isDirty){report.status="FAIL";report.error+=" Authoring scene changed during restore";}
                File.WriteAllText(output+"/report.json",JsonUtility.ToJson(report,true));
                File.WriteAllText("Temp/PCFoamProof/bundle-report-"+target+".json",JsonUtility.ToJson(report,true));
                Debug.Log("[KH PC Foam Bundle] "+report.status+" "+output);
            }
        }
        static long Bundle(string folder,string[] assets,BuildAssetBundleOptions options,BuildTarget target)
        {
            Directory.CreateDirectory(folder);
            var started=DateTime.UtcNow;
            var manifest=BuildPipeline.BuildAssetBundles(folder,new[]{new AssetBundleBuild{assetBundleName="foam-proof",assetNames=assets}},options,target);
            // GetLatestReport is not public in this project's 2022.3.22f1 API.
            var report=UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget("Library/LastBuild.buildreport").OfType<BuildReport>().FirstOrDefault();
            if(report==null)throw new IOException("No build report: "+folder);
            var summary=report.summary;
            var log=new System.Text.StringBuilder();
            log.AppendLine("result="+summary.result+" errors="+summary.totalErrors+" startedRaw="+summary.buildStartedAt.ToString("O")+" dateKind="+summary.buildStartedAt.Kind+" endedRaw="+summary.buildEndedAt.ToString("O")+" platform="+summary.platform+" output="+summary.outputPath);
            foreach(var step in report.steps)foreach(var message in step.messages)
                if(message.type==LogType.Error||message.type==LogType.Exception||message.type==LogType.Assert)log.AppendLine(step.name+" "+message.type+" "+message.content);
            File.WriteAllText(folder+"/build-report.txt",log.ToString());
            // 2022.3.22 returns native UTC ticks as an Unspecified DateTime; do not apply the local UTC offset twice.
            // A fresh report file plus the unique run output path identifies this invocation independently of that API quirk.
            if(File.GetLastWriteTimeUtc("Library/LastBuild.buildreport")<started.AddSeconds(-2)
                ||!string.Equals(Path.GetFullPath(summary.outputPath).TrimEnd('/','\\'),Path.GetFullPath(folder).TrimEnd('/','\\'),StringComparison.OrdinalIgnoreCase)
                ||summary.platform!=target||summary.result!=BuildResult.Succeeded||summary.totalErrors!=0)
                throw new IOException("Build report not successful/current: "+log);
            string path=folder+"/foam-proof";
            if(manifest==null||!File.Exists(path))throw new IOException("Bundle failed: "+folder);
            if(target==BuildTarget.StandaloneWindows64&&assets.Length==1&&assets[0].EndsWith(".unity"))
            {
                var bundle=AssetBundle.LoadFromFile(path);
                if(bundle==null)throw new IOException("Scene bundle cannot load");
                try{if(!bundle.isStreamedSceneAssetBundle||!bundle.GetAllScenePaths().Contains(assets[0]))throw new IOException("Wrong scene in bundle");}
                finally{bundle.Unload(true);}
            }
            return new FileInfo(path).Length;
        }
    }
}
