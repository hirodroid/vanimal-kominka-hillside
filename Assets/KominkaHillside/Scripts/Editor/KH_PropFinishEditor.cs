using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace KominkaHillside.Editor
{
    public static partial class KH_PropFinishEditor
    {
        const string Evidence="Temp/prop-finish-20261004";
        const string Images="docs/plans/evidence/2026-10-04-prop-finish";
        const string AssetsDir="Assets/KominkaHillside/Models/PropFinish";
        [Serializable] public class Part {public string path,mesh;public Vector3 localPosition,localEuler,localScale,worldPosition,size,center;public int triangles;public bool enabled;public string[] materials,components;}
        [Serializable] public class SurveyReport {public List<Part> parts=new List<Part>();}
        static string PathOf(Transform t){return t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;}
        static bool Target(Transform t){string p=PathOf(t);return p.StartsWith("KH_WashStation/")||p.StartsWith("KH_WashStations/")||p.StartsWith("KH_PhotoBoard/")||p.Contains("KH_LeashRack/")||p.Contains("KH_Station_Viewpoint/")||p.Contains("KH_Station_Plaza/");}
        static void Idle(){KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);}
        [MenuItem("Tools/KominkaHillside/Course/160 - Survey Close Props")]
        public static void Survey()
        {
            Idle();Directory.CreateDirectory(Evidence);var report=new SurveyReport();
            foreach(var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true).Where(r=>Target(r.transform)))
            {
                var t=r.transform;var f=r.GetComponent<MeshFilter>();if(f==null||f.sharedMesh==null)continue;var m=f.sharedMesh;
                report.parts.Add(new Part{path=PathOf(t),mesh=AssetDatabase.GetAssetPath(m)+":"+m.name,localPosition=t.localPosition,localEuler=t.localEulerAngles,localScale=t.localScale,worldPosition=t.position,size=r.bounds.size,center=r.bounds.center,triangles=m.triangles.Length/3,enabled=r.enabled,materials=r.sharedMaterials.Select(mat=>mat==null?"null":AssetDatabase.GetAssetPath(mat)+":"+mat.shader.name).ToArray(),components=t.GetComponents<Component>().Select(c=>c.GetType().Name).ToArray()});
            }
            File.WriteAllText(Evidence+"/survey.json",JsonUtility.ToJson(report,true));Debug.Log("[PropFinish] survey parts="+report.parts.Count);
        }
    }
}
