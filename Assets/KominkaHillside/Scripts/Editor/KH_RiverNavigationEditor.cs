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
    public static class KH_RiverNavigationEditor
    {
        const string AssetPath="Assets/KominkaHillside/Data/Environment/River/KH_RiverNavMesh.asset";
        static GameObject Root()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||BuildPipeline.isBuildingPlayer||SceneManager.GetActiveScene().path!=KH_RiverRepresentativeEditor.ScenePath)
                throw new InvalidOperationException("Representative scene in idle Edit mode required");
            var root=GameObject.Find("KH_RiverRepresentative");
            if(root==null)throw new InvalidOperationException("Missing representative root");
            return root;
        }

        [MenuItem("Tools/KominkaHillside/Course/8 - Bake River Navigation")]
        public static void Bake()
        {
            var root=Root();
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Unsaved scene; nothing changed");
            // Never rebuild somebody's adjusted navigation or touch the existing world navigation data.
            if(root.GetComponent<NavMeshSurface>()!=null||File.Exists(AssetPath))throw new InvalidOperationException("River navigation already exists; preserve manual settings");
            var bridge=root.transform.Find("SmallBridge");
            if(bridge==null)throw new InvalidOperationException("Missing bridge");
            var volumes=new GameObject("RiverbedNotWalkable");volumes.transform.SetParent(root.transform,false);
            for(int i=0;i<60;i++)
            {
                float z=(i+.5f)*3;
                float center=57.5f+7*Mathf.Sin((z-70)/36);
                var go=new GameObject("Bed_"+i);go.transform.SetParent(volumes.transform,false);go.transform.localPosition=new Vector3(center,2+.018f*z-.55f,z);
                var volume=go.AddComponent<NavMeshModifierVolume>();volume.area=NavMesh.GetAreaFromName("Not Walkable");
                volume.center=Vector3.zero; // Package default is (0,1,0), which would cut the bridge instead of the bed.
                volume.size=new Vector3(5.5f,1.4f,3.5f); // upper face water + 0.15; bridge deck is water + 0.9.
            }
            var surface=root.AddComponent<NavMeshSurface>();surface.collectObjects=CollectObjects.Children;
            surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;surface.layerMask=(1<<0)|(1<<11);
            surface.overrideVoxelSize=true;surface.voxelSize=.15f;surface.BuildNavMesh();
            if(surface.navMeshData==null)throw new InvalidOperationException("No generated navigation data");
            surface.navMeshData.name="KH_RiverNavMesh";
            AssetDatabase.CreateAsset(surface.navMeshData,AssetPath);AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(surface);EditorSceneManager.MarkSceneDirty(root.scene);
            if(!EditorSceneManager.SaveScene(root.scene))throw new IOException("Navigation scene save failed");
        }

        [MenuItem("Tools/KominkaHillside/Course/10 - Rebake River Navigation")]
        public static void Rebake()
        {
            var root=Root();
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Unsaved scene; nothing changed");
            var surface=root.GetComponent<NavMeshSurface>();
            var owned=AssetDatabase.LoadAssetAtPath<NavMeshData>(AssetPath);
            if(surface==null||owned==null||surface.navMeshData!=owned)throw new InvalidOperationException("Only the owned representative navigation can be updated");
            surface.BuildNavMesh();var fresh=surface.navMeshData;
            // BuildNavMesh leaves the old reference intact when generation fails.
            if(fresh==null||fresh==owned||EditorUtility.IsPersistent(fresh))throw new InvalidOperationException("No fresh navigation data; existing asset preserved");
            surface.RemoveData();EditorUtility.CopySerialized(fresh,owned);surface.navMeshData=owned;surface.AddData();
            owned.name="KH_RiverNavMesh";
            UnityEngine.Object.DestroyImmediate(fresh);EditorUtility.SetDirty(owned);AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(surface);EditorSceneManager.MarkSceneDirty(root.scene);
            if(!EditorSceneManager.SaveScene(root.scene))throw new IOException("Rebaked navigation scene save failed");
        }

        [MenuItem("Tools/KominkaHillside/Course/9 - Verify River Navigation")]
        public static void Verify()
        {
            var root=Root();var errors=new List<string>();var lines=new List<string>();
            var surface=root.GetComponent<NavMeshSurface>();
            if(surface==null||surface.navMeshData==null||AssetDatabase.GetAssetPath(surface.navMeshData)!=AssetPath)errors.Add("Owned river NavMesh missing");
            var bed=root.transform.Find("RiverbedNotWalkable");
            if(bed==null||bed.childCount!=60)errors.Add("Riverbed volumes missing");
            else foreach(var v in bed.GetComponentsInChildren<NavMeshModifierVolume>())
                if(v.center!=Vector3.zero||v.area!=NavMesh.GetAreaFromName("Not Walkable"))errors.Add("Riverbed modifier settings "+v.name);
            Physics.SyncTransforms();
            var bridge=root.transform.Find("SmallBridge");if(bridge==null)throw new InvalidOperationException("Missing bridge");
            Vector3 c=bridge.position;int mask=(1<<0)|(1<<11);float biggestStep=0;
            foreach(float lane in new[]{-.9f,0,.9f})
            {
                float last=0;
                for(int s=0;s<=100;s++)
                {
                    Vector3 p=c+new Vector3(-12.5f+s*.25f,10,lane);RaycastHit hit;
                    if(!Physics.Raycast(p,Vector3.down,out hit,15,mask,QueryTriggerInteraction.Ignore)){errors.Add("Footing gap lane="+lane+" sample="+s);continue;}
                    if(Mathf.Abs(hit.point.y-c.y)>.3f)errors.Add("Bridge approach height lane="+lane+" sample="+s+" y="+hit.point.y);
                    if(s>0)biggestStep=Mathf.Max(biggestStep,Mathf.Abs(hit.point.y-last));last=hit.point.y;
                }
                Vector3 a=c+new Vector3(-12.5f,0,lane), b=c+new Vector3(12.5f,0,lane);NavMeshHit na,nb;
                var path=new NavMeshPath();
                bool complete=NavMesh.SamplePosition(a,out na,.4f,NavMesh.AllAreas)&&NavMesh.SamplePosition(b,out nb,.4f,NavMesh.AllAreas)&&NavMesh.CalculatePath(na.position,nb.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
                float length=0;bool cross=false;
                if(complete)for(int i=1;i<path.corners.Length;i++)
                {
                    var p=path.corners[i-1];var q=path.corners[i];length+=Vector3.Distance(p,q);
                    if((p.x-c.x)*(q.x-c.x)<=0&&Mathf.Abs(q.x-p.x)>.001f)
                    {
                        var at=Vector3.Lerp(p,q,(c.x-p.x)/(q.x-p.x));cross=Mathf.Abs(at.z-c.z)<1.65f&&Mathf.Abs(at.y-c.y)<.25f;
                    }
                }
                if(!complete||!cross||length>30)errors.Add("Bridge path lane="+lane+" complete="+complete+" crossesDeck="+cross+" length="+length);
                lines.Add("lane="+lane+" complete="+complete+" length="+length+" crossesDeck="+cross);
            }
            if(biggestStep>.2f)errors.Add("Step exceeds 0.2 m: "+biggestStep);
            int riverSamples=0;
            var terrain=root.GetComponentInChildren<Terrain>();
            for(int z=10;z<180;z+=10)
            {
                if(Mathf.Abs(z-70)<5)continue;
                float x=57.5f+7*Mathf.Sin((z-70)/36f);
                var p=root.transform.TransformPoint(new Vector3(x,0,z));p.y=terrain.SampleHeight(p)+terrain.transform.position.y;
                NavMeshHit hit;if(NavMesh.SamplePosition(p,out hit,.25f,NavMesh.AllAreas))errors.Add("Riverbed walkable z="+z);
                riverSamples++;
            }
            lines.Insert(0,"status="+(errors.Count==0?"NAV_GEOMETRY_OK":"FAIL")+" errors="+errors.Count+" maxHeightStep="+biggestStep+" riverSamples="+riverSamples);
            lines.AddRange(errors);lines.Add("Static navigation/footing test only; dog behavior, leashes, players, mobile devices and full-course connection remain unverified.");
            Directory.CreateDirectory("Temp/des-env-002");File.WriteAllLines("Temp/des-env-002/river-navigation.txt",lines);Debug.Log(string.Join("\n",lines));
        }
    }
}
