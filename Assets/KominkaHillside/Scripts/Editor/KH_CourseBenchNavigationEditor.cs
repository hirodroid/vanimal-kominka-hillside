using System;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace KominkaHillside.Editor
{
    // Baked exclusion only: retain the existing bench, player collider and original route nodes.
    public static class KH_CourseBenchNavigationEditor
    {
        const string Name="ViewpointBench_NoWalk",Bench="KH_World/KH_Places/KH_Viewpoint/KH_Bench";
        static Bounds BenchBounds()
        {
            var go=GameObject.Find(Bench);var collider=go==null?null:go.GetComponent<BoxCollider>();
            if(collider==null||!collider.enabled||collider.isTrigger)throw new InvalidOperationException("Existing viewpoint bench collider required");
            return collider.bounds;
        }
        public static GameObject Add(Transform parent)
        {
            if(parent.Find(Name)!=null)throw new InvalidOperationException("Bench exclusion already exists; preserve artist edits");
            var bounds=BenchBounds();var go=new GameObject(Name);go.transform.SetParent(parent,false);go.transform.position=bounds.center;
            var volume=go.AddComponent<NavMeshModifierVolume>();volume.center=Vector3.zero;
            // More than the dog's 0.35 m path-corner advance; include terrain below the seat.
            volume.size=new Vector3(bounds.size.x+1f,4f,bounds.size.z+1f);volume.area=NavMesh.GetAreaFromName("Not Walkable");
            return go;
        }
        public static void VerifyConfiguration(Transform parent)
        {
            var t=parent.Find(Name);var v=t==null?null:t.GetComponent<NavMeshModifierVolume>();var b=BenchBounds();
            if(v==null||!v.enabled||v.center!=Vector3.zero||v.area!=NavMesh.GetAreaFromName("Not Walkable")||
                Vector3.Distance(t.position,b.center)>.001f||Vector3.Distance(v.size,new Vector3(b.size.x+1f,4f,b.size.z+1f))>.001f)
                throw new InvalidOperationException("Bench exclusion differs from collider bounds");
        }
        [MenuItem("Tools/KominkaHillside/Course/55 - Exclude Viewpoint Bench From Navigation")]
        public static void Apply()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);var root=GameObject.Find("KH_CourseNavigation");
            if(root==null||root.transform.Find(Name)!=null)throw new InvalidOperationException("Owned navigation without bench exclusion required");
            string dir="Temp/des-env-002/bench-navigation/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(dir);
            File.Copy(KH_CourseGroundEditor.ScenePath,dir+"/before.unity");File.Copy(KH_CourseNavigationEditor.AssetPath,dir+"/before-nav.asset");
            GameObject added=null;
            try
            {
                added=Add(root.transform);EditorSceneManager.MarkSceneDirty(root.scene);
                if(!EditorSceneManager.SaveScene(root.scene))throw new IOException("Bench exclusion save failed");
                KH_CourseNavigationEditor.Refresh();
            }
            catch
            {
                if(added!=null)UnityEngine.Object.DestroyImmediate(added);
                EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);throw;
            }
            Debug.Log("[KH Course Bench] Saved exclusion and refreshed owned navigation. Backup="+dir+". Run 56 and full navigation checks.");
        }
        [MenuItem("Tools/KominkaHillside/Course/56 - Verify Viewpoint Bench Navigation")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);var root=GameObject.Find("KH_CourseNavigation");VerifyConfiguration(root.transform);
            var b=BenchBounds();int samples=0;
            for(int x=0;x<5;x++)for(int z=0;z<3;z++)for(int y=0;y<2;y++)
            {
                var p=new Vector3(Mathf.Lerp(b.min.x,b.max.x,x/4f),y==0?b.min.y:b.max.y,Mathf.Lerp(b.min.z,b.max.z,z/2f));NavMeshHit hit;
                if(NavMesh.SamplePosition(p,out hit,.25f,NavMesh.AllAreas))throw new InvalidOperationException("Bench footprint remains walkable: "+p);
                samples++;
            }
            var path=new NavMeshPath();NavMeshHit a,c;
            if(!NavMesh.SamplePosition(new Vector3(80.34f,19.1f,108.78f),out a,.5f,NavMesh.AllAreas)||
                !NavMesh.SamplePosition(new Vector3(80.69f,19.03f,115.67f),out c,.5f,NavMesh.AllAreas)||
                !NavMesh.CalculatePath(a.position,c.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)
                throw new InvalidOperationException("Route around bench incomplete");
            for(int i=0;i+1<path.corners.Length;i++)for(int j=0;j<=20;j++)
            {var p=Vector3.Lerp(path.corners[i],path.corners[i+1],j/20f);if(p.x>=b.min.x&&p.x<=b.max.x&&p.z>=b.min.z&&p.z<=b.max.z)throw new InvalidOperationException("Route intersects bench");}
            Debug.Log("[KH Course Bench] FOOTPRINT_EXCLUDED samples="+samples+" completePathCorners="+path.corners.Length+". Runtime dog traversal remains separate.");
        }
    }
}
