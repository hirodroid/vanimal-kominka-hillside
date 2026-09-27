using System;
using System.IO;
using System.Linq;
using UdonSharp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    public static class KH_CourseFarKominkaEditor
    {
        const string Path="KH_World/KH_Places/KH_FarKominka";
        const string ReportPath="Temp/des-env-002/far-kominka.json";
        [Serializable] public class Report
        {public string utc,scene,status;public int proxies,udon,renderers,colliders,animators;public string[] types;}
        static GameObject Root()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var root=GameObject.Find(Path);if(root==null)throw new InvalidOperationException("Owned far-building instance not found");return root;
        }
        [MenuItem("Tools/KominkaHillside/Course/43 - Survey Far Building Scripts")]
        public static void Survey(){Record(Root(),false);}
        static void Record(GameObject root,bool clean)
        {
            var proxies=root.GetComponentsInChildren<UdonSharpBehaviour>(true);
            var report=new Report{utc=DateTime.UtcNow.ToString("o"),scene=EditorSceneManager.GetActiveScene().path,proxies=proxies.Length,udon=root.GetComponentsInChildren<UdonBehaviour>(true).Length,renderers=root.GetComponentsInChildren<Renderer>(true).Length,colliders=root.GetComponentsInChildren<Collider>(true).Length,animators=root.GetComponentsInChildren<Animator>(true).Length,types=proxies.Select(p=>p.GetType().FullName).Distinct().ToArray()};
            report.status=report.proxies==0&&report.udon==0?"DECORATIVE_BUILDING_ONLY":"SCRIPT_REMNANTS";
            File.WriteAllText(ReportPath,JsonUtility.ToJson(report,true));
            if(clean&&report.status!="DECORATIVE_BUILDING_ONLY")throw new InvalidOperationException("Far-building scripts remain");
            Debug.Log("[KH Far Building] "+report.status+" proxies="+report.proxies+" udon="+report.udon);
        }
        [MenuItem("Tools/KominkaHillside/Course/44 - Remove Far Building Script Remnants")]
        public static void Remove()
        {
            var root=Root();var proxies=root.GetComponentsInChildren<UdonSharpBehaviour>(true);
            if(proxies.Length!=14||proxies.Any(p=>p.GetType().FullName!="mosangoya.GlobalSyncedAnimationBool"))throw new InvalidOperationException("Expected fourteen surveyed decorative door proxies; preserve unexpected scripts");
            var scene=EditorSceneManager.GetActiveScene();File.Copy(scene.path,"Temp/des-env-002/before-far-building-cleanup.unity",true);
            int rendererCount=root.GetComponentsInChildren<Renderer>(true).Length,colliderCount=root.GetComponentsInChildren<Collider>(true).Length;
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Remove decorative building script remnants");
            try
            {
                foreach(var proxy in proxies)Undo.DestroyObjectImmediate(proxy);
                foreach(var udon in root.GetComponentsInChildren<UdonBehaviour>(true))Undo.DestroyObjectImmediate(udon);
                if(root.GetComponentsInChildren<Renderer>(true).Length!=rendererCount||root.GetComponentsInChildren<Collider>(true).Length!=colliderCount)throw new InvalidOperationException("Decorative geometry changed");
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene))throw new IOException("Save failed");
                Undo.CollapseUndoOperations(group);
            }
            catch{Undo.RevertAllDownToGroup(group);throw;}
            Record(root,true);
        }
    }
}
