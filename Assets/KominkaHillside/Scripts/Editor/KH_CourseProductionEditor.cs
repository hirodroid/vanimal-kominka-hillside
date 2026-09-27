using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaHillside.Editor
{
    public static class KH_CourseProductionEditor
    {
        const string Candidate = "Assets/Scenes/KH_RiverLoopCourse.unity";
        const string Record = "Logs/kh-course-promotion";
        static string Hash(string path)
        {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");
        }
        [MenuItem("Tools/KominkaHillside/Course/71 - Promote Saved Course To Production")]
        public static void Promote()
        {
            KH_CourseGroundEditor.Idle(Candidate);
            if(File.Exists(KH_ProductionScene.LegacyPath)||Directory.Exists(Record))throw new InvalidOperationException("Promotion already started; inspect the preserved files before retrying.");
            var pipeline=UnityEngine.Object.FindObjectOfType<VRC.Core.PipelineManager>();
            if(pipeline==null||string.IsNullOrEmpty(pipeline.blueprintId)||!File.ReadAllText(KH_ProductionScene.Path).Contains("blueprintId: "+pipeline.blueprintId))throw new InvalidOperationException("Production upload identity differs");
            if(GameObject.Find("KH_CourseExtension")==null)throw new InvalidOperationException("Candidate has no river loop");
            Directory.CreateDirectory(Record);
            File.Copy(KH_ProductionScene.Path,Record+"/previous-production.unity");
            File.Copy(KH_ProductionScene.Path+".meta",Record+"/production.meta");
            File.Copy(Candidate,Record+"/accepted-course.unity");
            if(File.Exists("ProjectSettings/EditorBuildSettings.asset"))File.Copy("ProjectSettings/EditorBuildSettings.asset",Record+"/EditorBuildSettings.asset");
            string oldHash=Hash(KH_ProductionScene.Path),newHash=Hash(Candidate),metaHash=Hash(KH_ProductionScene.Path+".meta");
            if(!AssetDatabase.IsValidFolder("Assets/Scenes/Archive"))AssetDatabase.CreateFolder("Assets/Scenes","Archive");
            if(!AssetDatabase.CopyAsset(KH_ProductionScene.Path,KH_ProductionScene.LegacyPath)||Hash(KH_ProductionScene.LegacyPath)!=oldHash)throw new IOException("Legacy backup mismatch");
            try
            {
                // Neither source scene is dirty. Close before replacing serialized content, keeping production GUID.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                File.Copy(Candidate,KH_ProductionScene.Path,true);
                AssetDatabase.ImportAsset(KH_ProductionScene.Path,ImportAssetOptions.ForceUpdate);
                if(Hash(KH_ProductionScene.Path)!=newHash||Hash(KH_ProductionScene.Path+".meta")!=metaHash)throw new IOException("Production copy/GUID mismatch");
                EditorSceneManager.OpenScene(KH_ProductionScene.Path,OpenSceneMode.Single);
                EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(KH_ProductionScene.Path,true)};
                AssetDatabase.SaveAssets();
                File.WriteAllText(Record+"/promotion.txt","old="+oldHash+"\naccepted="+newHash+"\nmeta="+metaHash+"\nNo upload performed. Verify before removing the candidate scene.\n");
            }
            catch
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                File.Copy(Record+"/previous-production.unity",KH_ProductionScene.Path,true);
                File.Copy(Record+"/production.meta",KH_ProductionScene.Path+".meta",true);
                if(File.Exists(Record+"/EditorBuildSettings.asset"))File.Copy(Record+"/EditorBuildSettings.asset","ProjectSettings/EditorBuildSettings.asset",true);
                AssetDatabase.Refresh();
                EditorSceneManager.OpenScene(Candidate,OpenSceneMode.Single);
                throw;
            }
            Debug.Log("[KH Production] Saved course promoted; production GUID and upload ID retained. Run 72 separately.");
        }
        [MenuItem("Tools/KominkaHillside/Course/72 - Verify Production Promotion")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_ProductionScene.Path);
            if(Hash(KH_ProductionScene.Path)!=Hash(Record+"/accepted-course.unity"))throw new InvalidOperationException("Production differs from accepted course snapshot");
            if(Hash(KH_ProductionScene.LegacyPath)!=Hash(Record+"/previous-production.unity"))throw new InvalidOperationException("Legacy backup changed");
            if(Hash(KH_ProductionScene.Path+".meta")!=Hash(Record+"/production.meta"))throw new InvalidOperationException("Production GUID changed");
            var builds=EditorBuildSettings.scenes;
            if(builds.Length!=1||!builds[0].enabled||builds[0].path!=KH_ProductionScene.Path)throw new InvalidOperationException("Build scene mismatch");
            var route=UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
            if(route==null||!route.closedLoop||route.count!=436)throw new InvalidOperationException("Production route mismatch");
            bool rejected=false;
            try{KH_ProductionScene.RejectLegacyRebuild();}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new InvalidOperationException("Legacy rebuild guard missing");
            File.WriteAllLines(Record+"/dependencies.txt",AssetDatabase.GetDependencies(KH_ProductionScene.Path,true));
            File.WriteAllText(Record+"/verified.txt","PRODUCTION_PROMOTION_OK\nProduction scene bytes equal accepted course; legacy scene bytes equal previous production; production meta unchanged; sole enabled build scene; closed route 436 nodes; legacy rebuild rejected.\n");
            Debug.Log("[KH Production] PRODUCTION_PROMOTION_OK");
        }
    }
}
