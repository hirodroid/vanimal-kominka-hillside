using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace KominkaHillside.Editor
{
    public static class KH_RoadsideSurveyEditor
    {
        [Serializable] public class Item { public string path; public Vector3 position, size; public int triangles, colliders; public string[] materials; }
        [Serializable] public class Report { public List<Item> objects = new List<Item>(); public Vector3[] route; public int navVertices, navTriangles; }
        [MenuItem("Tools/KominkaHillside/Course/140 - Survey Roadside Sources")]
        public static void Survey()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var report = new Report();
            var route = UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
            report.route = Enumerable.Range(0, route.count).Select(route._Position).ToArray();
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                if (!(t.name.Contains("Bench") || t.name.Contains("Viewpoint") || t.name.Contains("Station") ||
                    (t.parent != null && (t.parent.name == "KH_Shrubs" || t.parent.name == "Trees" || t.parent.name == "KH_Trees")))) continue;
                var rs=t.GetComponentsInChildren<Renderer>(); if(rs.Length==0)continue;
                var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
                string path=t.name;var p=t.parent;while(p!=null){path=p.name+"/"+path;p=p.parent;}
                report.objects.Add(new Item{path=path,position=t.position,size=b.size,colliders=t.GetComponentsInChildren<Collider>().Length,
                    triangles=t.GetComponentsInChildren<MeshFilter>().Where(f=>f.sharedMesh!=null).Sum(f=>Enumerable.Range(0,f.sharedMesh.subMeshCount).Sum(i=>(int)f.sharedMesh.GetIndexCount(i)/3)),
                    materials=rs.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Select(AssetDatabase.GetAssetPath).Distinct().ToArray()});
            }
            var nav=NavMesh.CalculateTriangulation();report.navVertices=nav.vertices.Length;report.navTriangles=nav.indices.Length/3;
            Directory.CreateDirectory("Temp/scenery-20261004");File.WriteAllText("Temp/scenery-20261004/inventory.json",JsonUtility.ToJson(report,true));
            Debug.Log("[Roadside] Survey objects="+report.objects.Count);
        }
    }
}
