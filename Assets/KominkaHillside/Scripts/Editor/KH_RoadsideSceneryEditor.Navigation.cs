using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

namespace KominkaHillside.Editor
{
    public static partial class KH_RoadsideSceneryEditor
    {
        [Serializable] class ObstacleReport {public string utc;public int colliders,samples,benchApproaches;public List<string> errors=new List<string>();}
        [MenuItem("Tools/KominkaHillside/Course/147 - Verify Roadside Obstacle Navigation")]
        public static void VerifyObstacleNavigation()
        {
            Init();var root=GameObject.Find(RootName);if(root==null)throw new InvalidOperationException("Roadside required");Physics.SyncTransforms();
            var report=new ObstacleReport{utc=DateTime.UtcNow.ToString("o")};
            foreach(var c in root.GetComponentsInChildren<Collider>())
            {
                var v=c.GetComponent<NavMeshModifierVolume>();if(v==null||v.area!=NavMesh.GetAreaFromName("Not Walkable")){report.errors.Add(c.name+" exclusion missing");continue;}
                report.colliders++;var bounds=c.bounds;
                // Query terrain level, the collider's top, and vertical steps between them.
                // Each position is checked against the actual collider to avoid false positives
                // from corners outside a rotated box or capsule.
                for(int x=0;x<5;x++)for(int z=0;z<5;z++)
                {
                    float px=Mathf.Lerp(bounds.min.x,bounds.max.x,(x+.5f)/5),pz=Mathf.Lerp(bounds.min.z,bounds.max.z,(z+.5f)/5);
                    float bottom=Ground(new Vector3(px,0,pz));
                    for(float y=bottom-.15f;y<=bounds.max.y+.18f;y+=.10f)
                    {
                        var p=new Vector3(px,y,pz);var closest=c.ClosestPoint(p);
                        if(Mathf.Abs(closest.x-p.x)>.03f||Mathf.Abs(closest.z-p.z)>.03f)continue;
                        NavMeshHit hit;report.samples++;
                        if(NavMesh.SamplePosition(p,out hit,.075f,NavMesh.AllAreas))report.errors.Add(c.name+" walkable inside/above collider at "+hit.position);
                    }
                }
            }
            var benches=root.transform.Find("RestBenches");
            foreach(Transform t in benches)
            {
                var p=t.position+t.forward*1.5f;p.y=Ground(p);var on=route._ProjectToTrail(p);NavMeshHit a,b;var path=new NavMeshPath();
                if(!NavMesh.SamplePosition(p,out a,.5f,NavMesh.AllAreas)||!NavMesh.SamplePosition(on,out b,.5f,NavMesh.AllAreas)||!NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)report.errors.Add(t.name+" approach disconnected");
                else report.benchApproaches++;
            }
            File.WriteAllText(Evidence+"/obstacle-navigation.json",JsonUtility.ToJson(report,true));
            if(report.errors.Count>0)throw new InvalidOperationException(string.Join("; ",report.errors));
            Debug.Log("[Roadside] Obstacle Nav PASS colliders="+report.colliders+" samples="+report.samples+" benchApproaches="+report.benchApproaches);
        }
    }
}
