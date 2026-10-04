using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaHillside.Editor
{
    public static partial class KH_RoadsideSceneryEditor
    {
        // Low, opaque blade geometry uses the existing river-grass material. No new texture,
        // terrain detail layer, collider, script or per-blade GameObject.
        [MenuItem("Tools/KominkaHillside/Course/143 - Add Sparse Roadside Grass")]
        public static void AddGrass()
        {
            Init();var root=GameObject.Find(RootName);if(root==null)throw new InvalidOperationException("Groves required");
            if(root.transform.Find("RoadsideGrass")!=null){Debug.Log("[Roadside] Existing grass preserved");return;}
            var material=AssetDatabase.LoadAssetAtPath<Material>(KH_CourseGroundEditor.Dir+"/Scenery/KH_CourseGrass.mat");
            if(material==null)throw new InvalidOperationException("Grass material required");
            var parent=Child(root.transform,"RoadsideGrass");var random=new System.Random(20261004);var created=new List<string>();int tufts=0,triangles=0;
            try
            {
                for(int node=24;node<route.count-16;node+=8)
                {
                    var p=route._Position(node);var along=route._Position(node+1)-p;along.y=0;along.Normalize();var side=new Vector3(along.z,0,-along.x)*(node%24==0?-1:1);
                    var vertices=new List<Vector3>();var colors=new List<Color>();var indices=new List<int>();var origin=p;
                    for(int tuft=0;tuft<55;tuft++)
                    {
                        var q=p+along*((float)random.NextDouble()*16-8)+side*(3.3f+(float)random.NextDouble()*4.5f);
                        if(route._DistanceToTrail(q)<2.8f)continue;
                        var river=KH_CourseGroundEditor.RiverAt(q.z);if(Mathf.Abs(q.x-river.x)<KH_CourseGroundEditor.HalfWidth(q.z)+2)continue;
                        q.y=Ground(q)+.005f;float height=.12f+(float)random.NextDouble()*.19f;float shade=.8f+(float)random.NextDouble()*.25f;
                        for(int blade=0;blade<7;blade++)
                        {
                            float angle=(float)random.NextDouble()*Mathf.PI*2;var axis=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                            var offset=new Vector3((float)random.NextDouble()*.25f-.125f,0,(float)random.NextDouble()*.25f-.125f);var pos=q+offset;pos.y=Ground(pos)+.005f;pos-=origin;
                            float width=.025f+(float)random.NextDouble()*.025f;int first=vertices.Count;
                            vertices.Add(pos-axis*width);vertices.Add(pos+axis*width);vertices.Add(pos+axis*height*.3f+Vector3.up*height);
                            colors.Add(new Color(.28f*shade,.30f*shade,.16f*shade));colors.Add(new Color(.28f*shade,.30f*shade,.16f*shade));colors.Add(new Color(.53f*shade,.48f*shade,.29f*shade));
                            indices.Add(first);indices.Add(first+2);indices.Add(first+1);
                        }
                        tufts++;
                    }
                    if(vertices.Count==0)continue;
                    var mesh=new Mesh{name="KH_RoadsideGrass_"+node};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
                    string path=Dir+"/"+mesh.name+".asset";if(File.Exists(path))throw new InvalidOperationException("Existing mesh preserved "+path);
                    AssetDatabase.CreateAsset(mesh,path);created.Add(path);var g=Child(parent.transform,mesh.name);g.transform.position=origin;
                    g.AddComponent<MeshFilter>().sharedMesh=mesh;var r=g.AddComponent<MeshRenderer>();r.sharedMaterial=material;RenderSettings(g);Lod(g,new Renderer[]{r},null,0,.10f);triangles+=indices.Count/3;
                }
                AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);if(!EditorSceneManager.SaveScene(root.scene))throw new IOException("Grass save failed");
                File.WriteAllText(Evidence+"/grass.json","{\"chunks\":"+created.Count+",\"tufts\":"+tufts+",\"triangles\":"+triangles+"}");
                Debug.Log("[Roadside] Grass chunks="+created.Count+" tufts="+tufts+" triangles="+triangles);
            }
            catch{UnityEngine.Object.DestroyImmediate(parent);EditorSceneManager.MarkSceneDirty(root.scene);if(EditorSceneManager.SaveScene(root.scene))foreach(string p in created)AssetDatabase.DeleteAsset(p);throw;}
        }
    }
}
