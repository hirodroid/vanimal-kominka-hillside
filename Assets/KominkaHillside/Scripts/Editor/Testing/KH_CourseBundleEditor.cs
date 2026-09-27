using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // Saved representative scene only. Proxy bundles are never uploaded or added to Build Settings.
    public static class KH_CourseBundleEditor
    {
        [Serializable] public class Report
        {
            public string scene, platform, startedUtc, endedUtc, status, error, output;
            public long downloadBytes=-1, uncompressedBytes=-1;
            public string limitations="Saved-scene AssetBundle proxy, not SDK upload size or resident/device memory. Compare with the same build target and compression. Does not certify shader appearance, VR stereo, or runtime performance.";
        }

        [MenuItem("Tools/KominkaHillside/Course/6 - River Bundle Android")]
        public static void Android() { Measure(BuildTarget.Android); }
        [MenuItem("Tools/KominkaHillside/Course/7 - River Bundle iOS")]
        public static void IOS() { Measure(BuildTarget.iOS); }
        [MenuItem("Tools/KominkaHillside/Course/17 - Full Course Bundle Android")]
        public static void FullAndroid() { Measure(BuildTarget.Android,KH_CourseGroundEditor.ScenePath); }
        [MenuItem("Tools/KominkaHillside/Course/18 - Full Course Bundle iOS")]
        public static void FullIOS() { Measure(BuildTarget.iOS,KH_CourseGroundEditor.ScenePath); }

        static void Measure(BuildTarget target,string scene=KH_RiverRepresentativeEditor.ScenePath)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Idle Edit mode required");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Unsaved scene; not saving or discarding it");
            if(SceneManager.GetActiveScene().path!=scene||!File.Exists(scene))throw new InvalidOperationException("Open saved representative scene");
            string folder=scene==KH_RiverRepresentativeEditor.ScenePath?"bundles":"course-bundles";
            string dir="Temp/des-env-002/"+folder+"/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+target;
            Directory.CreateDirectory(dir+"/compressed");Directory.CreateDirectory(dir+"/raw");
            var report=new Report{scene=scene,platform=target.ToString(),startedUtc=DateTime.UtcNow.ToString("o"),status="RUNNING",output=dir};
            var originalSubtarget=EditorUserBuildSettings.androidBuildSubtarget;
            File.WriteAllText(dir+"/report.json",JsonUtility.ToJson(report,true));
            try
            {
                if(target==BuildTarget.Android)EditorUserBuildSettings.androidBuildSubtarget=MobileTextureSubtarget.ASTC;
                var build=new[]{new AssetBundleBuild{assetBundleName="course",assetNames=new[]{scene}}};
                var manifest=BuildPipeline.BuildAssetBundles(dir+"/compressed",build,BuildAssetBundleOptions.None,target);
                if(manifest==null||!File.Exists(dir+"/compressed/course"))throw new IOException("Compressed bundle did not complete");
                report.downloadBytes=new FileInfo(dir+"/compressed/course").Length;
                manifest=BuildPipeline.BuildAssetBundles(dir+"/raw",build,BuildAssetBundleOptions.UncompressedAssetBundle,target);
                if(manifest==null||!File.Exists(dir+"/raw/course"))throw new IOException("Uncompressed bundle did not complete");
                report.uncompressedBytes=new FileInfo(dir+"/raw/course").Length;
                report.status="MEASURED";
            }
            catch(Exception e){report.status="ERROR";report.error=e.ToString();}
            finally
            {
                EditorUserBuildSettings.androidBuildSubtarget=originalSubtarget;
                report.endedUtc=DateTime.UtcNow.ToString("o");
                File.WriteAllText(dir+"/report.json",JsonUtility.ToJson(report,true));
                Debug.Log("[KH Course Bundle] "+report.status+" "+dir+" download="+report.downloadBytes+" raw="+report.uncompressedBytes+" "+report.error);
            }
        }
    }
}
