using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    public static partial class KH_CourseSceneryEditor
    {
        const string Evidence="Temp/des-env-002/";
        const string Prefabs="Assets/LMArt-StylizedEnvironmentKit/BroadleafTreeField/Prefabs";
        [Serializable] class Candidate
        {
            public string path;
            public int triangles,renderers,lods,colliders;
            public string[] materials;
            public Vector3 bounds;
        }
        [Serializable] class SurveyReport { public string utc;public Candidate[] candidates; }
        [MenuItem("Tools/KominkaHillside/Course/28 - Survey Scenery Candidates")]
        public static void Survey()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var list=new List<Candidate>();
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{Prefabs+"/Trees",Prefabs+"/Natures"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);
                if(!(path.Contains("Tree")||path.Contains("Shrub")))continue;
                if(path.Contains("ViewSet")||path.Contains("Ground"))continue;
                var p=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var rs=p.GetComponentsInChildren<MeshRenderer>(true);var ms=p.GetComponentsInChildren<MeshFilter>(true);
                var bounds=new Bounds();bool any=false;int triangles=0;
                foreach(var f in ms)if(f.sharedMesh!=null)
                {for(int i=0;i<f.sharedMesh.subMeshCount;i++)triangles+=(int)f.sharedMesh.GetIndexCount(i)/3;}
                foreach(var r in rs){if(!any){bounds=r.bounds;any=true;}else bounds.Encapsulate(r.bounds);}
                list.Add(new Candidate{path=path,triangles=triangles,renderers=rs.Length,lods=p.GetComponentsInChildren<LODGroup>(true).Length,colliders=p.GetComponentsInChildren<Collider>(true).Length,bounds=bounds.size,materials=rs.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Select(m=>AssetDatabase.GetAssetPath(m)).Distinct().ToArray()});
            }
            File.WriteAllText(Evidence+"scenery-candidates.json",JsonUtility.ToJson(new SurveyReport{utc=DateTime.UtcNow.ToString("o"),candidates=list.ToArray()},true));
            Debug.Log("[KH Course Scenery] Surveyed "+list.Count+" candidates without scene changes");
        }
    }
}
