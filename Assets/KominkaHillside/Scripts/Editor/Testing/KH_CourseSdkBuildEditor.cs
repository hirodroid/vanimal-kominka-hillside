using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Editor;
using VRC.SDKBase.Editor;

namespace KominkaHillside.Editor
{
    // Local SDK Build() only. Does not upload or launch a client. Source restoration is explicit.
    [InitializeOnLoad]
    public static class KH_CourseSdkBuildEditor
    {
        const string Key="KH.CourseSdkBuild",Root="Temp/des-env-002/sdk-build";
        [Serializable] public class Input { public string path,sha256; }
        [Serializable] public class MemoryInput { public string path,type,name,jsonFile; }
        [Serializable] public class Report
        {
            public string runId,utc,endedUtc,status,scene,target,bundle,bundleSHA256,reason;
            public long bundleBytes;
            public List<Input> inputs=new List<Input>();
            public List<MemoryInput> memoryInputs=new List<MemoryInput>();
            public List<string> changedInputs=new List<string>(),addedInputs=new List<string>(),errors=new List<string>(),warnings=new List<string>();
            public bool editorPreferenceRestored,originalHadStripPref,originalStripPref;
            public int originalAndroidTextureSubtarget;
        }
        static Report report;
        [Serializable] public class MemoryCheck { public string path,afterFile,error;public bool unchanged; }
        [Serializable] public class MemoryChecks { public string utc;public List<MemoryCheck> entries=new List<MemoryCheck>(); }
        [MenuItem("Tools/KominkaHillside/Course/66 - Inspect SDK Settings Memory")]
        public static void InspectSettingsMemory()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||BuildPipeline.isBuildingPlayer||SessionState.GetBool(Key,false))throw new InvalidOperationException("Completed SDK build required");
            string dir=SessionState.GetString(Key+".run","");
            if(!Path.GetFullPath(dir).StartsWith(Path.GetFullPath(Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("No owned SDK run");
            var saved=JsonUtility.FromJson<Report>(File.ReadAllText(dir+"/report.json"));
            var result=new MemoryChecks{utc=DateTime.UtcNow.ToString("o")};
            var objects=Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
            foreach(var input in saved.memoryInputs)
            {
                var check=new MemoryCheck{path=input.path,afterFile=input.jsonFile+".after"};result.entries.Add(check);
                try
                {
                    var matches=objects.Where(o=>o.GetType().AssemblyQualifiedName==input.type&&o.name==input.name&&AssetDatabase.GetAssetPath(o)==input.path).ToArray();
                    if(matches.Length!=1)throw new InvalidOperationException("Expected one settings object, got "+matches.Length);
                    string json=EditorJsonUtility.ToJson(matches[0],true);File.WriteAllText(check.afterFile,json);check.unchanged=json==File.ReadAllText(input.jsonFile);
                }
                catch(Exception e){check.error=e.Message;}
            }
            File.WriteAllText(dir+"/memory-inspection.json",JsonUtility.ToJson(result,true));
            Debug.Log("[KH Course SDK] Settings memory unchanged="+result.entries.Count(e=>e.unchanged)+"/"+result.entries.Count);
        }
        [Serializable] public class PreflightReport
        {
            public string utc,project,scene,target;
            public bool sceneDirty,playing,compiling,building,multiPlatformBuild;
            public List<string> dirtyAssets=new List<string>();
        }
        [MenuItem("Tools/KominkaHillside/Course/65 - Inspect Local SDK Preconditions")]
        public static void InspectPreconditions()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var result=new PreflightReport{utc=DateTime.UtcNow.ToString("o"),project=Application.dataPath,scene=scene.path,target=EditorUserBuildSettings.activeBuildTarget.ToString(),sceneDirty=scene.isDirty,playing=EditorApplication.isPlaying,compiling=EditorApplication.isCompiling,building=BuildPipeline.isBuildingPlayer,multiPlatformBuild=SessionState.GetBool("VRCSdkControlPanel.MultiPlatformBuild",false)};
            result.dirtyAssets=Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Where(o=>AssetDatabase.Contains(o)&&EditorUtility.IsDirty(o)).Select(AssetDatabase.GetAssetPath).Distinct().OrderBy(p=>p).ToList();
            Directory.CreateDirectory(Root);File.WriteAllText(Root+"/preflight.json",JsonUtility.ToJson(result,true));
            Debug.Log("[KH Course SDK] Preflight at "+Root+"/preflight.json; dirty assets="+result.dirtyAssets.Count);
        }
        static KH_CourseSdkBuildEditor()
        {
            if(!SessionState.GetBool(Key,false))return;
            string dir=SessionState.GetString(Key+".run","");
            try
            {
                report=JsonUtility.FromJson<Report>(File.ReadAllText(dir+"/report.json"));
                report.status="DOMAIN_RELOAD_REQUIRES_EVIDENCE_REVIEW";
                report.reason="Async observer lost on domain reload. Inspect SDK output and build state before recovery or restart. Original preferences and input backups are retained.";
                InspectInputs();Save();
            }
            catch(Exception e){Debug.LogError("[KH Course SDK] Interrupted observer journal: "+e);}
            Debug.LogWarning("[KH Course SDK] Inspect "+dir+"; do not restart based only on domain reload.");
        }
        [MenuItem("Tools/KominkaHillside/Course/64 - Build Local SDK Bundle")]
        public static async void BuildLocal()
        {
            if(SessionState.GetBool(Key,false))throw new InvalidOperationException("Previous SDK build is active or requires evidence review");
            if(SessionState.GetBool("VRCSdkControlPanel.MultiPlatformBuild",false))throw new InvalidOperationException("SDK multi-platform upload chain is pending; do not mount the panel for a local-only check");
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            if(BuildPipeline.isBuildingPlayer)throw new InvalidOperationException("Build already active");
            if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.StandaloneWindows64)throw new InvalidOperationException("This final check is for the currently configured Windows64 target; no target switching");
            report=new Report{runId=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),utc=DateTime.UtcNow.ToString("o"),status="PREPARING",scene=KH_CourseGroundEditor.ScenePath,target=EditorUserBuildSettings.activeBuildTarget.ToString()};
            string dir=Root+"/"+report.runId;
            Directory.CreateDirectory(dir);
            Capture(report.scene);Capture(report.scene+".meta");
            foreach(string path in Directory.GetFiles("ProjectSettings"))Capture(path);
            // SDK may update XR configuration. Snapshot existing files before opening its panel.
            foreach(string path in XRPaths())Capture(path);
            // SaveAssets in SDK preprocessing also touches already-dirty persistent objects.
            // Preserve their disk inputs and unsaved project-setting values for explicit recovery.
            foreach(var asset in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                if(!AssetDatabase.Contains(asset)||!EditorUtility.IsDirty(asset))continue;
                string path=AssetDatabase.GetAssetPath(asset);
                if(!File.Exists(path))continue;
                Capture(path);Capture(path+".meta");
                if(path.StartsWith("ProjectSettings/",StringComparison.Ordinal)&&!(asset is AssetImporter))
                {
                    string json=dir+"/memory-"+report.memoryInputs.Count+".json";
                    File.WriteAllText(json,EditorJsonUtility.ToJson(asset,true));
                    report.memoryInputs.Add(new MemoryInput{path=path,type=asset.GetType().AssemblyQualifiedName,name=asset.name,jsonFile=json});
                }
            }
            foreach(string query in new[]{"VRCCore-Standalone","VRCCore-Editor","AudioPluginOculusSpatializer"})
                foreach(string guid in AssetDatabase.FindAssets(query))
                {
                    string path=AssetDatabase.GUIDToAssetPath(guid);var importer=AssetImporter.GetAtPath(path) as PluginImporter;
                    if(importer==null)continue;
                    Capture(path+".meta");
                    if(query=="AudioPluginOculusSpatializer"&&path.Contains("com.vrchat.base")&&new SerializedObject(importer).FindProperty("m_IsOverridable").boolValue)
                        throw new InvalidOperationException("SDK spatializer would require a restart dialog; resolve separately before local build");
                }
            report.originalHadStripPref=EditorPrefs.HasKey("VRC.SDKBase_StripAllShaders");report.originalStripPref=EditorPrefs.GetBool("VRC.SDKBase_StripAllShaders");
            report.originalAndroidTextureSubtarget=(int)EditorUserBuildSettings.androidBuildSubtarget;
            SessionState.SetBool(Key,true);SessionState.SetString(Key+".run",dir);
            Save();Application.logMessageReceived+=Log;
            try
            {
                // Only mounts the normal SDK panel. Build() validates and produces a local bundle.
                if(!EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel"))throw new InvalidOperationException("SDK panel menu is unavailable");
                for(int attempt=0;VRCSdkControlPanel.window==null&&attempt<100;attempt++)await System.Threading.Tasks.Task.Delay(100);
                IVRCSdkWorldBuilderApi builder;
                if(!VRCSdkControlPanel.TryGetBuilder(out builder))throw new InvalidOperationException("SDK panel builder is not available yet");
                string message;
                if(!((VRCSdkControlPanelWorldBuilder)builder).IsValidBuilder(out message))throw new InvalidOperationException(message);
                report.status="BUILDING";Save();
                string result=await builder.Build();
                if(string.IsNullOrEmpty(result)||!File.Exists(result))throw new IOException("SDK returned no bundle");
                var info=new FileInfo(result);
                if(info.LastWriteTimeUtc<DateTime.Parse(report.utc).ToUniversalTime()||info.Length==0)throw new IOException("SDK bundle is stale or empty");
                string owned=dir+"/course.vrcw";File.Copy(result,owned,false);
                report.bundle=owned;report.bundleBytes=new FileInfo(owned).Length;report.bundleSHA256=Hash(owned);report.status="BUILT_REQUIRES_SOURCE_CHECK";Save();
            }
            catch(ValidationException e){report.status="VALIDATION_FAILED";report.reason=e.ToString();report.errors.AddRange(e.Errors);}
            catch(Exception e){report.status="FAILED";report.reason=e.ToString();}
            finally
            {
                Application.logMessageReceived-=Log;
                try{EditorUserBuildSettings.androidBuildSubtarget=(MobileTextureSubtarget)report.originalAndroidTextureSubtarget;}catch(Exception e){report.errors.Add("Texture subtarget restore: "+e);}
                try
                {
                    if(report.originalHadStripPref)EditorPrefs.SetBool("VRC.SDKBase_StripAllShaders",report.originalStripPref);else EditorPrefs.DeleteKey("VRC.SDKBase_StripAllShaders");
                    report.editorPreferenceRestored=EditorPrefs.HasKey("VRC.SDKBase_StripAllShaders")==report.originalHadStripPref&&(!report.originalHadStripPref||EditorPrefs.GetBool("VRC.SDKBase_StripAllShaders")==report.originalStripPref);
                }
                catch(Exception e){report.errors.Add("Shader preference restore: "+e);}
                InspectInputs();report.endedUtc=DateTime.UtcNow.ToString("o");
                try{Save();SessionState.SetBool(Key,false);}catch(Exception e){Debug.LogError("[KH Course SDK] Final journal write failed; backups retained: "+e);}
                Debug.Log("[KH Course SDK] "+report.status+" at "+dir+"; changed inputs="+report.changedInputs.Count);
            }
        }
        static void Capture(string path)
        {
            path=path.Replace('\\','/');if(!File.Exists(path)||report.inputs.Any(i=>string.Equals(i.path,path,StringComparison.OrdinalIgnoreCase)))return;
            string copy=Root+"/"+report.runId+"/inputs/"+path;Directory.CreateDirectory(Path.GetDirectoryName(copy));File.Copy(path,copy,false);
            report.inputs.Add(new Input{path=path,sha256=Hash(path)});
        }
        static IEnumerable<string> XRPaths()
        {
            foreach(string root in new[]{"Packages/com.vrchat.base/Editor/XR","Assets/XR"})
            {
                if(File.Exists(root+".meta"))yield return root+".meta";
                if(Directory.Exists(root))foreach(string path in Directory.GetFiles(root,"*",SearchOption.AllDirectories))yield return path.Replace('\\','/');
            }
        }
        static void InspectInputs()
        {
            report.changedInputs.Clear();report.addedInputs.Clear();
            foreach(var input in report.inputs)
                try{if(!File.Exists(input.path)||Hash(input.path)!=input.sha256)report.changedInputs.Add(input.path);}
                catch(Exception e){report.errors.Add("Inspect "+input.path+": "+e.Message);}
            try
            {
                var before=new HashSet<string>(report.inputs.Select(i=>i.path),StringComparer.OrdinalIgnoreCase);
                foreach(string path in XRPaths().Concat(Directory.GetFiles("ProjectSettings").Select(p=>p.Replace('\\','/'))))if(!before.Contains(path))report.addedInputs.Add(path);
            }
            catch(Exception e){report.errors.Add("Enumerate added inputs: "+e.Message);}
        }
        static void Log(string message,string stack,LogType type)
        {
            if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){if(report.errors.Count<100)report.errors.Add(message);}
            else if(type==LogType.Warning&&report.warnings.Count<100)report.warnings.Add(message);
        }
        static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
        static void Save(){File.WriteAllText(Root+"/"+report.runId+"/report.json",JsonUtility.ToJson(report,true));}
    }
}
