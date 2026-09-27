using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // Course-copy navigation only. Never rebuild or overwrite the original walking asset.
    public static class KH_CourseNavigationEditor
    {
        public const string AssetPath=KH_CourseGroundEditor.Dir+"/KH_CourseNavMesh.asset";
        const string RootName="KH_CourseNavigation";
        [MenuItem("Tools/KominkaHillside/Course/30 - Refresh Course Navigation")]
        public static void Refresh()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var root=GameObject.Find(RootName);var surface=root==null?null:root.GetComponent<NavMeshSurface>();
            var owned=AssetDatabase.LoadAssetAtPath<NavMeshData>(AssetPath);
            if(surface==null||!surface.enabled||owned==null||surface.navMeshData!=owned)throw new InvalidOperationException("Owned navigation required");
            var backup=UnityEngine.Object.Instantiate(owned);NavMeshData fresh=null;bool changed=false;
            try
            {
                Physics.SyncTransforms();surface.BuildNavMesh();fresh=surface.navMeshData;
                if(fresh==null||fresh==owned||EditorUtility.IsPersistent(fresh))throw new InvalidOperationException("No fresh navigation generated");
                var route=UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
                var path=new NavMeshPath();
                for(int i=0;i<route.count;i++)
                {
                    NavMeshHit a,b;int j=(i+1)%route.count;
                    if(!NavMesh.SamplePosition(route.nodes[i].position,out a,.5f,NavMesh.AllAreas)||!NavMesh.SamplePosition(route.nodes[j].position,out b,.5f,NavMesh.AllAreas)||
                        !NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete||PathLength(path)>Vector3.Distance(a.position,b.position)*1.5f+1)
                        throw new InvalidOperationException("New navigation diverts route link "+i);
                }
                surface.RemoveData();changed=true;EditorUtility.CopySerialized(fresh,owned);owned.name="KH_CourseNavMesh";
                surface.navMeshData=owned;surface.AddData();EditorUtility.SetDirty(owned);EditorUtility.SetDirty(surface);
                AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);
                if(!EditorSceneManager.SaveScene(root.scene))throw new IOException("Refreshed navigation save failed");
            }
            catch(Exception failure)
            {
                surface.RemoveData();
                try
                {
                    if(changed){EditorUtility.CopySerialized(backup,owned);owned.name="KH_CourseNavMesh";EditorUtility.SetDirty(owned);}
                }
                finally
                {
                    // Reattach before any disk operation, even when restoration of data throws.
                    surface.navMeshData=owned;surface.AddData();EditorUtility.SetDirty(surface);EditorSceneManager.MarkSceneDirty(root.scene);
                }
                bool restored=false;
                try{AssetDatabase.SaveAssets();restored=EditorSceneManager.SaveScene(root.scene);}
                catch(Exception saveError){Debug.LogError("[KH Course Nav] Rollback save failed: "+saveError.Message);}
                if(!restored)Debug.LogError("[KH Course Nav] Owned navigation reattached in memory; rollback not saved");
                throw new InvalidOperationException("Course navigation refresh failed; rollback saved="+restored,failure);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(backup);
                if(fresh!=null&&!EditorUtility.IsPersistent(fresh))UnityEngine.Object.DestroyImmediate(fresh);
            }
            Debug.Log("[KH Course Nav] Refreshed owned asset; GUID preserved and all route links connected. Run saved-state Verify separately.");
        }
        [Serializable] public class Report
        {
            public string status,utc,scene;
            public int errors,vertices,triangles,routeSamples,completedSegments,bedSamples,bridgePaths;
            public float longestLocalDetour,maximumSnapDistance;
            public string[] findings;
            public string limitations="Static navigation geometry only. Dog route graph/loop intention, leashes, animation, runtime cost and devices remain unverified.";
        }

        [MenuItem("Tools/KominkaHillside/Course/19 - Bake Full Course Navigation")]
        public static void Bake()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            if(GameObject.Find(RootName)!=null||File.Exists(AssetPath))throw new InvalidOperationException("Existing course navigation preserved; first build only");
            var oldGo=GameObject.Find("KH_Dogs/KH_Navigation");
            var old=oldGo==null?null:oldGo.GetComponent<NavMeshSurface>();
            if(old==null||old.navMeshData==null||AssetDatabase.GetAssetPath(old.navMeshData)!="Assets/KominkaHillside/Data/Walking/KH_WalkingNavMesh.asset")
                throw new InvalidOperationException("Expected original navigation reference required");
            var scene=SceneManager.GetActiveScene();bool originalEnabled=old.enabled;
            GameObject root=null;NavMeshData fresh=null;bool createdAsset=false;
            try
            {
            root=new GameObject(RootName);var surface=root.AddComponent<NavMeshSurface>();
            surface.agentTypeID=old.agentTypeID;surface.collectObjects=CollectObjects.Volume;
            surface.center=new Vector3(300,45,360);surface.size=new Vector3(600,110,720);
            surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;surface.layerMask=(1<<0)|(1<<11);
            surface.overrideVoxelSize=true;surface.voxelSize=.15f;
            surface.overrideTileSize=true;surface.tileSize=256;
            int area=NavMesh.GetAreaFromName("Not Walkable");
            for(int i=0;i<240;i++)
            {
                float z=i*3f;var a=KH_CourseGroundEditor.RiverAt(z);var b=KH_CourseGroundEditor.RiverAt(z+3);
                float width=Mathf.Max(KH_CourseGroundEditor.HalfWidth(z),KH_CourseGroundEditor.HalfWidth(z+3))+1f;
                float left=Mathf.Min(a.x,b.x)-width,right=Mathf.Max(a.x,b.x)+width;
                float bottom=Mathf.Min(a.y,b.y)-1.2f,top=Mathf.Max(a.y,b.y)+.25f;
                var go=new GameObject("Riverbed_"+i);go.transform.SetParent(root.transform,false);
                go.transform.position=new Vector3((left+right)*.5f,(bottom+top)*.5f,z+1.5f);
                var volume=go.AddComponent<NavMeshModifierVolume>();volume.center=Vector3.zero;
                volume.size=new Vector3(right-left,top-bottom,3.5f);volume.area=area;
            }
            KH_CourseBenchNavigationEditor.Add(root.transform);
            // Build first, validate fresh data, then switch the copy to the new surface.
            // A failed build leaves the original reference untouched and does not save the scene.
            surface.BuildNavMesh();
            fresh=surface.navMeshData;
            if(fresh==null||EditorUtility.IsPersistent(fresh))
                throw new InvalidOperationException("Fresh full-course navigation generation failed");
            surface.navMeshData.name="KH_CourseNavMesh";
            AssetDatabase.CreateAsset(fresh,AssetPath);createdAsset=true;AssetDatabase.SaveAssets();
            old.enabled=false;EditorUtility.SetDirty(old);EditorUtility.SetDirty(surface);
            EditorSceneManager.MarkSceneDirty(root.scene);
            if(!EditorSceneManager.SaveScene(root.scene))throw new IOException("Course navigation scene save failed");
            Debug.Log("[KH Course Nav] Built owned navigation; original asset preserved. Run separate Verify.");
            }
            catch(Exception failure)
            {
                old.enabled=originalEnabled;EditorUtility.SetDirty(old);
                if(root!=null)UnityEngine.Object.DestroyImmediate(root);
                // Persist restored references before deleting the newly created asset. If storage
                // is failing too, retain that asset so a partially written scene cannot lose it.
                EditorSceneManager.MarkSceneDirty(scene);bool restored=false;
                try{restored=EditorSceneManager.SaveScene(scene);}catch(Exception e){Debug.LogError("[KH Course Nav] Rollback save failed: "+e.Message);}
                if(restored&&createdAsset&&AssetDatabase.LoadAssetAtPath<NavMeshData>(AssetPath)==fresh)AssetDatabase.DeleteAsset(AssetPath);
                if(fresh!=null&&!EditorUtility.IsPersistent(fresh))UnityEngine.Object.DestroyImmediate(fresh);
                if(!restored)Debug.LogError("[KH Course Nav] Old surface restored in memory; scene save failed. Preserve this scene and retry saving before removing any new course asset.");
                throw new InvalidOperationException("Course navigation failed; rollback saved="+restored,failure);
            }
        }

        [MenuItem("Tools/KominkaHillside/Course/20 - Verify Full Course Navigation")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var report=new Report{utc=DateTime.UtcNow.ToString("o"),scene=SceneManager.GetActiveScene().path};
            var errors=new List<string>();var root=GameObject.Find(RootName);
            var surface=root==null?null:root.GetComponent<NavMeshSurface>();
            if(surface==null||!surface.enabled||surface.navMeshData==null||AssetDatabase.GetAssetPath(surface.navMeshData)!=AssetPath)
                throw new InvalidOperationException("Owned full-course surface required");
            var old=GameObject.Find("KH_Dogs/KH_Navigation").GetComponent<NavMeshSurface>();
            if(old.enabled||AssetDatabase.GetAssetPath(old.navMeshData)!="Assets/KominkaHillside/Data/Walking/KH_WalkingNavMesh.asset")errors.Add("Original navigation reference/state changed");
            int active=0;foreach(var s in UnityEngine.Object.FindObjectsOfType<NavMeshSurface>())if(s.isActiveAndEnabled)active++;
            if(active!=1||surface.agentTypeID!=old.agentTypeID)errors.Add("Unexpected active surface/agent type");
            if(surface.collectObjects!=CollectObjects.Volume||surface.useGeometry!=NavMeshCollectGeometry.PhysicsColliders||surface.layerMask.value!=((1<<0)|(1<<11))||!surface.overrideVoxelSize||Mathf.Abs(surface.voxelSize-.15f)>.001f)errors.Add("Surface settings differ");
            var volumes=root.GetComponentsInChildren<NavMeshModifierVolume>();
            if(volumes.Length!=241)errors.Add("Riverbed/bench volume count differs");
            KH_CourseBenchNavigationEditor.VerifyConfiguration(root.transform);
            foreach(var v in volumes)if(v.center!=Vector3.zero||v.area!=NavMesh.GetAreaFromName("Not Walkable"))errors.Add("Riverbed modifier mismatch "+v.name);
            Physics.SyncTransforms();
            var route=UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
            if(route==null||route.nodes==null||route.nodes.Length<17)throw new InvalidOperationException("Original trail nodes required");
            var points=new List<Vector3>();for(int i=0;i<17;i++)points.Add(route.nodes[i].position);
            var added=KH_CourseGroundEditor.Route;for(int i=1;i<added.Length;i++)points.Add(added[i]);
            NavMeshHit previous=new NavMeshHit();bool previousValid=false;var path=new NavMeshPath();
            for(int i=1;i<points.Count;i++)
            {
                int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(points[i-1],points[i])/5));
                for(int k=i==1?0:1;k<=count;k++)
                {
                    Vector3 p=Vector3.Lerp(points[i-1],points[i],k/(float)count);RaycastHit ground;
                    if(!Physics.Raycast(new Vector3(p.x,150,p.z),Vector3.down,out ground,160,(1<<0)|(1<<11),QueryTriggerInteraction.Ignore)){errors.Add("Missing ground at "+p);previousValid=false;continue;}
                    p.y=ground.point.y;NavMeshHit hit;bool valid=NavMesh.SamplePosition(p,out hit,.5f,NavMesh.AllAreas);report.routeSamples++;
                    if(!valid){errors.Add("Off navigation at "+p);previousValid=false;continue;}
                    report.maximumSnapDistance=Mathf.Max(report.maximumSnapDistance,Vector3.Distance(p,hit.position));
                    if(previousValid)
                    {
                        bool complete=NavMesh.CalculatePath(previous.position,hit.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
                        float length=PathLength(path),direct=Vector3.Distance(previous.position,hit.position);
                        report.longestLocalDetour=Mathf.Max(report.longestLocalDetour,length-direct);
                        if(!complete||length>direct*1.5f+1)errors.Add("Incomplete or diverted trail at "+p+" length="+length);
                        else report.completedSegments++;
                    }
                    previous=hit;previousValid=true;
                }
            }
            foreach(float z in new[]{190f,490f})
            {
                var river=KH_CourseGroundEditor.RiverAt(z);float deck=river.y+.9f;
                foreach(float lane in new[]{-.9f,0,.9f})
                {
                    NavMeshHit a,b;bool complete=NavMesh.SamplePosition(new Vector3(river.x-12.5f,deck,z+lane),out a,.4f,NavMesh.AllAreas)&&NavMesh.SamplePosition(new Vector3(river.x+12.5f,deck,z+lane),out b,.4f,NavMesh.AllAreas)&&NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
                    bool crossing=false;
                    if(complete)for(int i=1;i<path.corners.Length;i++)
                    {
                        var p=path.corners[i-1];var q=path.corners[i];
                        if((p.x-river.x)*(q.x-river.x)<=0&&Mathf.Abs(q.x-p.x)>.001f)
                        {var at=Vector3.Lerp(p,q,(river.x-p.x)/(q.x-p.x));crossing=Mathf.Abs(at.z-z)<1.65f&&Mathf.Abs(at.y-deck)<.25f;}
                    }
                    if(!complete||!crossing||PathLength(path)>30)errors.Add("Bridge path invalid at z="+z+" lane="+lane);
                    else report.bridgePaths++;
                }
            }
            for(float z=6;z<720;z+=6)
            {
                if(Mathf.Abs(z-190)<4||Mathf.Abs(z-490)<4)continue;
                var river=KH_CourseGroundEditor.RiverAt(z);NavMeshHit hit;
                if(NavMesh.SamplePosition(new Vector3(river.x,river.y-.4f,z),out hit,.25f,NavMesh.AllAreas))errors.Add("Riverbed walkable z="+z);
                report.bedSamples++;
            }
            var mesh=NavMesh.CalculateTriangulation();report.vertices=mesh.vertices.Length;report.triangles=mesh.indices.Length/3;
            report.errors=errors.Count;report.findings=errors.ToArray();report.status=errors.Count==0?"NAV_GEOMETRY_OK":"FAIL";
            Directory.CreateDirectory("Temp/des-env-002");File.WriteAllText("Temp/des-env-002/full-course-navigation.json",JsonUtility.ToJson(report,true));
            Debug.Log("[KH Course Nav] "+report.status+" errors="+report.errors+" routeSamples="+report.routeSamples+" bridgePaths="+report.bridgePaths+" bedSamples="+report.bedSamples+" triangles="+report.triangles);
        }
        static float PathLength(NavMeshPath p){float length=0;for(int i=1;i<p.corners.Length;i++)length+=Vector3.Distance(p.corners[i-1],p.corners[i]);return length;}
    }
}
