using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.AI.Navigation;

namespace KominkaHillside.Editor
{
    public static partial class KH_RoadsideSceneryEditor
    {
        /// <summary>2026-10-12 ユーザー要望: 広場入口のベンチ (通路上、旧 KH_Bench と見た目の ViewpointBench) は Course/16 で除去済み。再実行しても作り直さない。</summary>
        public const bool ExcludePlazaBench = true;

        [MenuItem("Tools/KominkaHillside/Course/145 - Finish Rest Benches")]
        public static void Benches()
        {
            Init();var root=GameObject.Find(RootName);if(root==null)throw new InvalidOperationException("Roadside required");
            if(root.transform.Find("RestBenches")!=null){Debug.Log("[Roadside] Authored benches preserved");return;}
            var upperSeat=SeatGround(new Vector3(209,0,610),210);var riverSeat=SeatGround(new Vector3(511,0,285),245);
            const string fbx="Assets/KominkaHillside/Models/Environment/RoadsideBench/KH_RoadsideBench.fbx";
            var importer=AssetImporter.GetAtPath(fbx) as ModelImporter;if(importer==null)throw new InvalidOperationException("Bench FBX required");
            importer.importAnimation=false;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.globalScale=1;importer.bakeAxisConversion=true;importer.isReadable=false;importer.importCameras=false;importer.importLights=false;importer.SaveAndReimport();
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(fbx);var temp=UnityEngine.Object.Instantiate(asset);Mesh mesh;
            try{mesh=new Mesh{name="KH_RoadsideBench"};mesh.CombineMeshes(temp.GetComponentsInChildren<MeshFilter>().Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=f.transform.localToWorldMatrix}).ToArray(),true,true);mesh.RecalculateBounds();}
            finally{UnityEngine.Object.DestroyImmediate(temp);}
            var size=mesh.bounds.size;if(Vector3.Distance(size,new Vector3(1.6f,.45f,.5f))>.01f)throw new InvalidOperationException("Bench dimensions "+size);
            string path=Dir+"/KH_RoadsideBench.asset";if(File.Exists(path))throw new InvalidOperationException("Preserve existing bench mesh");AssetDatabase.CreateAsset(mesh,path);
            var material=AssetDatabase.LoadAssetAtPath<Material>(KH_CourseAutumnEditor.MaterialPath);if(material==null)throw new InvalidOperationException("Bridge wood required");
            var old=ExcludePlazaBench?null:GameObject.Find("KH_World/KH_Places/KH_Viewpoint/KH_Bench");var oldRenderer=old!=null?old.GetComponent<MeshRenderer>():null;var oldCollider=old!=null?old.GetComponent<BoxCollider>():null;
            var parent=Child(root.transform,"RestBenches");
            try
            {
                if(!ExcludePlazaBench&&old!=null)
                {
                    MakeBench(parent.transform,"ViewpointBench",new Vector3(old.transform.position.x,oldCollider.bounds.min.y,old.transform.position.z),old.transform.eulerAngles.y,mesh,material,false);
                    oldRenderer.enabled=false;EditorUtility.SetDirty(oldRenderer);
                }
                MakeBench(parent.transform,"UpperPlazaBench",upperSeat,210,mesh,material,true);
                MakeBench(parent.transform,"RiverBench",riverSeat,245,mesh,material,true);
                AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);if(!EditorSceneManager.SaveScene(root.scene))throw new IOException("Bench save failed");
                Debug.Log("[Roadside] Three timber benches, original viewpoint collider preserved; new benches require Nav refresh");
            }
            catch{if(oldRenderer!=null)oldRenderer.enabled=true;UnityEngine.Object.DestroyImmediate(parent);EditorSceneManager.MarkSceneDirty(root.scene);if(EditorSceneManager.SaveScene(root.scene))AssetDatabase.DeleteAsset(path);throw;}
        }
        static Vector3 SeatGround(Vector3 preferred,float yaw)
        {
            // Search only a small neighbourhood for a level patch, preserving route and open play centre.
            for(int ring=0;ring<5;ring++)for(int d=0;d<(ring==0?1:8);d++)
            {
                var p=preferred+new Vector3(Mathf.Cos(d*Mathf.PI/4),0,Mathf.Sin(d*Mathf.PI/4))*ring;
                if(route._DistanceToTrail(p)<5)continue;
                float low=float.MaxValue,high=float.MinValue;
                var rotation=Quaternion.Euler(0,yaw,0);bool clear=true;
                foreach(float x in new[]{-1.3f,1.3f})foreach(float z in new[]{-.75f,.75f})if(route._DistanceToTrail(p+rotation*new Vector3(x,0,z))<3.5f)clear=false;
                if(!clear)continue;
                foreach(float x in new[]{-.8f,-.62f,-.52f,.52f,.62f,.8f})foreach(float z in new[]{-.25f,-.21f,-.11f,.11f,.21f,.25f}){float h=Ground(p+rotation*new Vector3(x,0,z));low=Mathf.Min(low,h);high=Mathf.Max(high,h);}
                if(high-low>.12f)continue;p.y=low-.01f;return p;
            }
            throw new InvalidOperationException("No level seating patch near "+preferred);
        }
        static GameObject MakeBench(Transform parent,string name,Vector3 p,float yaw,Mesh mesh,Material material,bool collider)
        {
            var g=Child(parent,name);g.transform.SetPositionAndRotation(p,Quaternion.Euler(0,yaw,0));g.AddComponent<MeshFilter>().sharedMesh=mesh;var r=g.AddComponent<MeshRenderer>();r.sharedMaterial=material;RenderSettings(g);Lod(g,new Renderer[]{r},null,0,.006f);
            if(collider){var c=g.AddComponent<BoxCollider>();c.center=new Vector3(0,.225f,0);c.size=new Vector3(1.6f,.45f,.5f);var v=g.AddComponent<NavMeshModifierVolume>();v.center=c.center;v.size=new Vector3(2.6f,4,1.5f);v.area=UnityEngine.AI.NavMesh.GetAreaFromName("Not Walkable");}
            return g;
        }
    }
}
