using System;
using System.Collections.Generic;
using System.IO;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace KominkaHillside.Editor
{
    public static class KH_CourseRouteEditor
    {
        const string OriginalRecord="docs/plans/des-env-002/original-route.json";
        [Serializable] public class Baseline
        {
            public Vector3[] nodes;
            public float[] radii;
            public string scene,utc;
            public int testedPairs;
        }
        [Serializable] public class Report
        {
            public string status,utc,scene;
            public int nodes,links,errors,ringSteps,shortestPairs,spatialQueries,entryQueries,loopEntryCases,maxLinkCandidates,maxEntryCandidates;
            public float length,maxProjectionDifference;
            public string[] findings;
            public string limitations="Editor execution of route methods and serialized Udon values. Actual Udon VM dog walking, UI selection, Owner handover and device performance require separate verification.";
        }

        [MenuItem("Tools/KominkaHillside/Course/23 - Verify Original Route Compatibility")]
        public static void Original()
        {
            KH_CourseGroundEditor.Idle(KH_ProductionScene.LegacyPath);
            var route=UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
            if(route==null||route.count!=17||route.closedLoop)throw new InvalidOperationException("Expected unchanged 17-node route");
            var b=new Baseline{scene=EditorSceneManager.GetActiveScene().path,utc=DateTime.UtcNow.ToString("o"),nodes=new Vector3[17],radii=new float[17]};
            for(int i=0;i<17;i++)
            {
                b.nodes[i]=route.nodes[i].position;b.radii[i]=route.arriveRadius[i];
                for(int j=0;j<17;j++)
                {
                    if(route._Next(i,j)!=route.nextHop[i*17+j]||route._Cost(i,j)!=route.routeCost[i*17+j])throw new InvalidOperationException("Legacy routing changed");
                    b.testedPairs++;
                }
            }
            foreach(int choice in new[]{0,1,2,3})if(route._LoopDirection(choice)!=0)throw new InvalidOperationException("Loop mode enabled in original");
            if(route._DestinationNode(2)!=-1||route._Advance(0,1)!=1||route._Advance(16,0)!=15)throw new InvalidOperationException("Original destination semantics differ");
            var errors=new List<string>();var report=new Report();CheckSpatial(route,report,errors);
            if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
            File.WriteAllText(OriginalRecord,JsonUtility.ToJson(b,true));
            Debug.Log("[KH Course Route] ORIGINAL_COMPATIBLE pairs="+b.testedPairs+" spatial="+report.spatialQueries);
        }

        [MenuItem("Tools/KominkaHillside/Course/21 - Build Full Course Route")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var route=UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
            if(route==null||route.closedLoop||route.count!=17)throw new InvalidOperationException("Initial 17-node course copy required; existing route preserved");
            var baseline=ReadBaseline();
            if(GameObject.Find("KH_CourseNavigation")==null)throw new InvalidOperationException("Build full-course navigation first");
            var points=new List<Vector3>();
            for(int i=0;i<17;i++)
            {
                if(route.nodes[i].position!=baseline.nodes[i]||route.arriveRadius[i]!=baseline.radii[i])throw new InvalidOperationException("Original node changed before build "+i);
                points.Add(route.nodes[i].position);
            }
            var control=KH_CourseGroundEditor.Route;
            for(int i=1;i<control.Length;i++)
            {
                int samples=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(control[i-1],control[i])/7f));
                for(int k=1;k<=samples;k++)
                {
                    if(i==control.Length-1&&k==samples)continue; // close to original node 0, not a duplicate gate
                    var p=Vector3.Lerp(control[i-1],control[i],k/(float)samples);RaycastHit hit;
                    if(!Physics.Raycast(new Vector3(p.x,150,p.z),Vector3.down,out hit,160,(1<<0)|(1<<11),QueryTriggerInteraction.Ignore))throw new InvalidOperationException("No route footing at "+p);
                    p.y=hit.point.y;NavMeshHit nav;
                    if(!NavMesh.SamplePosition(p,out nav,.5f,NavMesh.AllAreas))throw new InvalidOperationException("No navigation at "+p);
                    p=nav.position;if(Vector3.Distance(points[points.Count-1],p)>.8f)points.Add(p);
                }
            }
            int n=points.Count;var cumulative=new float[n];float total=0;var path=new NavMeshPath();
            for(int i=0;i<n;i++)
            {
                int j=(i+1)%n;
                if(!NavMesh.CalculatePath(points[i],points[j],NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)throw new InvalidOperationException("Disconnected route link "+i);
                float length=0;for(int k=1;k<path.corners.Length;k++)length+=Vector3.Distance(path.corners[k-1],path.corners[k]);
                if(length>Vector3.Distance(points[i],points[j])*1.5f+1||length<.01f)throw new InvalidOperationException("Invalid route link length "+i+" "+length);
                total+=length;if(j>0)cumulative[j]=total;
            }
            // Everything above is read-only preflight. Save a recovery snapshot before changing the copy.
            Directory.CreateDirectory("Temp/des-env-002");
            File.Copy(KH_CourseGroundEditor.ScenePath,"Temp/des-env-002/before-course-route.unity",true);
            var nodes=new Transform[n];var radii=new float[n];
            for(int i=0;i<n;i++)
            {
                if(i<17){nodes[i]=route.nodes[i];radii[i]=route.arriveRadius[i];continue;}
                var go=new GameObject("LoopNode"+i.ToString("000"));go.transform.SetParent(route.transform,false);go.transform.position=points[i];nodes[i]=go.transform;radii[i]=2.2f;
            }
            route.nodes=nodes;route.arriveRadius=radii;route.nodePositions=points.ToArray();route.count=n;
            route.linkA=new int[n];route.linkB=new int[n];route.linkAllowsDetour=new bool[n];
            for(int i=0;i<n;i++)
            {
                int j=(i+1)%n;route.linkA[i]=i;route.linkB[i]=j;
                route.linkAllowsDetour[i]=!AtBridge(points[i],points[j],190)&&!AtBridge(points[i],points[j],490);
            }
            route.destinationNodes=new[]{0,16,0,0,Nearest(points,new Vector3(215,40,605)),Nearest(points,new Vector3(505,10,285))};
            route.loopDistances=cumulative;route.loopLength=total;route.closedLoop=true;
            route.loopForwardDestination=2;route.loopReverseDestination=3;
            route.nextHop=new int[0];route.routeCost=new float[0];
            Index(route);
            UdonSharpEditorUtility.CopyProxyToUdon(route);EditorUtility.SetDirty(route);
            EditorSceneManager.MarkSceneDirty(route.gameObject.scene);
            if(!EditorSceneManager.SaveScene(route.gameObject.scene))throw new IOException("Course route scene save failed; recovery snapshot retained");
            Debug.Log("[KH Course Route] Built nodes="+n+" length="+total+". Verify after reopening; dog/menu behavior not yet certified.");
        }

        static int Nearest(List<Vector3> points,Vector3 p){int best=0;for(int i=1;i<points.Count;i++)if((points[i]-p).sqrMagnitude<(points[best]-p).sqrMagnitude)best=i;return best;}
        static bool AtBridge(Vector3 a,Vector3 b,float z)
        {
            float x=KH_CourseGroundEditor.RiverAt(z).x;
            return Mathf.Min(a.x,b.x)<=x+18&&Mathf.Max(a.x,b.x)>=x-18&&Mathf.Min(a.z,b.z)<=z+8&&Mathf.Max(a.z,b.z)>=z-8;
        }
        static Baseline ReadBaseline()
        {
            if(!File.Exists(OriginalRecord))throw new InvalidOperationException("Run original compatibility verification first");
            var b=JsonUtility.FromJson<Baseline>(File.ReadAllText(OriginalRecord));
            if(b.nodes==null||b.nodes.Length!=17)throw new InvalidOperationException("Invalid original route record");return b;
        }
        static void Index(KH_WalkRoute route)
        {
            route.cellOrigin=new Vector2(-32,-32);route.cellSize=32;route.cellColumns=21;route.cellRows=25;
            int cells=route.cellColumns*route.cellRows;var link=new List<int>();var entry=new List<int>();
            route.linkCellStarts=new int[cells+1];route.entryCellStarts=new int[cells+1];
            for(int cell=0;cell<cells;cell++)
            {
                route.linkCellStarts[cell]=link.Count;route.entryCellStarts[cell]=entry.Count;
                float x=route.cellOrigin.x+(cell%route.cellColumns)*32,z=route.cellOrigin.y+(cell/route.cellColumns)*32;
                for(int i=0;i<route.count;i++)
                {
                    var a=route.nodePositions[i];var b=route.nodePositions[(i+1)%route.count];
                    if(Mathf.Min(a.x,b.x)-32<=x+32&&Mathf.Max(a.x,b.x)+32>=x&&Mathf.Min(a.z,b.z)-32<=z+32&&Mathf.Max(a.z,b.z)+32>=z)link.Add(i);
                    if(a.x-30<=x+32&&a.x+30>=x&&a.z-30<=z+32&&a.z+30>=z)entry.Add(i);
                }
            }
            route.linkCellStarts[cells]=link.Count;route.entryCellStarts[cells]=entry.Count;
            route.linkCellIndices=link.ToArray();route.entryCellNodes=entry.ToArray();
        }

        [MenuItem("Tools/KominkaHillside/Course/22 - Verify Full Course Route")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);var r=UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
            if(r==null||!r.closedLoop||r.count<=17)throw new InvalidOperationException("Full course route required");
            var b=ReadBaseline();var errors=new List<string>();
            var report=new Report{utc=DateTime.UtcNow.ToString("o"),scene=EditorSceneManager.GetActiveScene().path,nodes=r.count,links=r.linkA.Length,length=r.loopLength};
            for(int i=0;i<17;i++)if(r.nodes[i].position!=b.nodes[i]||r.arriveRadius[i]!=b.radii[i])errors.Add("Original node/radius changed "+i);
            for(int i=0;i<r.count;i++)if(r.nodes[i].parent!=r.transform||r._Position(i)!=r.nodes[i].position||r.linkA[i]!=i||r.linkB[i]!=(i+1)%r.count)errors.Add("Node/link ownership or order "+i);
            foreach(int direction in new[]{2,3})
                for(int start=0;start<r.count;start+=Mathf.Max(1,r.count/19))
                {
                    int at=start;var seen=new HashSet<int>();
                    for(int step=0;step<r.count;step++){if(!seen.Add(at))errors.Add("Early ring repetition");at=r._Advance(at,direction);report.ringSteps++;}
                    if(at!=start||seen.Count!=r.count)errors.Add("Incomplete ring from "+start);
                }
            for(int from=0;from<r.count;from++)foreach(int dest in new[]{0,1,4,5})
            {
                int to=r._DestinationNode(dest),at=from;float sum=0;int steps=0;
                while(at!=to&&steps++<r.count)
                {
                    int next=r._Advance(at,dest);if(next<0||next==at){errors.Add("Broken shortest route");break;}
                    sum+=r._Cost(at,next);at=next;
                }
                if(at!=to||Mathf.Abs(sum-r._Cost(from,to))>.1f)errors.Add("Shortest route distance mismatch "+from+"->"+to);
                report.shortestPairs++;
            }
            CheckSpatial(r,report,errors);
            for(int i=17;i<r.count-3;i++)
            {
                int next=(i+1)%r.count,after=(i+2)%r.count,before=(i+r.count-1)%r.count;
                var midpoint=(r._Position(i)+r._Position(next))*.5f;
                if(r._LoopEntry(midpoint,2)!=next||r._LoopEntry(midpoint,3)!=i)errors.Add("Directional entry mismatch "+i);
                if(r._LoopReentry((r._Position(next)+r._Position(after))*.5f,2,next)!=after)errors.Add("Forward catch-up reentry is behind "+i);
                if(r._LoopReentry((r._Position(before)+r._Position(i))*.5f,3,i)!=before)errors.Add("Reverse catch-up reentry is behind "+i);
                report.loopEntryCases+=4;
            }
            for(int i=0;i<r.linkA.Length;i++)if(!r.linkAllowsDetour[i]&&r._AllowsDetour((r._Position(i)+r._Position((i+1)%r.count))*.5f))errors.Add("Bridge detour flag ignored "+i);
            var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(r);
            object value;if(!backing.publicVariables.TryGetVariableValue("closedLoop",out value)||!(value is bool)||!(bool)value)errors.Add("Saved Udon loop flag missing");
            foreach(string field in new[]{"nodes","nodePositions","loopDistances","linkA","linkB","linkAllowsDetour","destinationNodes","linkCellStarts","linkCellIndices","entryCellStarts","entryCellNodes"})
            {
                object saved;var expected=(Array)typeof(KH_WalkRoute).GetField(field).GetValue(r);
                if(!backing.publicVariables.TryGetVariableValue(field,out saved)||!(saved is Array)||((Array)saved).Length!=expected.Length){errors.Add("Saved Udon array mismatch "+field);continue;}
                var actual=(Array)saved;for(int i=0;i<expected.Length;i++)if(!Equals(actual.GetValue(i),expected.GetValue(i))){errors.Add("Saved Udon element mismatch "+field+"["+i+"]");break;}
            }
            if(backing.SyncMethod!=VRC.SDKBase.Networking.SyncType.None)errors.Add("Static route should not synchronize");
            report.errors=errors.Count;report.findings=errors.ToArray();report.status=errors.Count==0?"ROUTE_DATA_OK":"FAIL";
            File.WriteAllText("Temp/des-env-002/full-course-route-verification.json",JsonUtility.ToJson(report,true));
            Debug.Log("[KH Course Route] "+report.status+" nodes="+report.nodes+" errors="+report.errors+" ringSteps="+report.ringSteps+" spatial="+report.spatialQueries+" maxCandidates="+report.maxLinkCandidates);
        }

        static void CheckSpatial(KH_WalkRoute r,Report report,List<string> errors)
        {
            var queries=new List<Vector3>();
            for(int x=-45;x<=645;x+=30)for(int z=-45;z<=765;z+=30)queries.Add(new Vector3(x,30,z));
            for(int i=0;i<r.count;i++){queries.Add(r._Position(i)+new Vector3(1,3,2));queries.Add((r._Position(i)+r._Position((i+1)%r.count))*.5f);}
            foreach(var p in queries)
            {
                Vector3 closest=p;float best=float.MaxValue;
                for(int j=0;j<r.linkA.Length;j++)
                {
                    var a=r.nodes[r.linkA[j]].position;var b=r.nodes[r.linkB[j]].position;var ab=b-a;ab.y=0;var d=p-a;d.y=0;
                    float t=ab.sqrMagnitude<.0001f?0:Mathf.Clamp01(Vector3.Dot(d,ab)/ab.sqrMagnitude);
                    var q=Vector3.Lerp(a,b,t);float distance=new Vector2(p.x-q.x,p.z-q.z).sqrMagnitude;
                    if(distance<best){best=distance;closest=q;}
                }
                var actual=r._ProjectToTrail(p);float delta=Vector3.Distance(actual,closest);report.maxProjectionDifference=Mathf.Max(report.maxProjectionDifference,delta);
                // Shared gate stem can have exactly tied projections at slightly different heights.
                float actualDistance=new Vector2(p.x-actual.x,p.z-actual.z).magnitude;
                if(Mathf.Abs(actualDistance-Mathf.Sqrt(best))>.002f||Mathf.Abs(r._DistanceToTrail(p)-Mathf.Sqrt(best))>.002f)errors.Add("Spatial search differs at "+p);
                int cell=r._EntryCell(p);
                if(cell>=0)
                {
                    int start=r.entryCellStarts[cell],end=r.entryCellStarts[cell+1];
                    report.maxEntryCandidates=Mathf.Max(report.maxEntryCandidates,end-start);
                    report.maxLinkCandidates=Mathf.Max(report.maxLinkCandidates,r.linkCellStarts[cell+1]-r.linkCellStarts[cell]);
                    for(int i=0;i<r.count;i++)if(new Vector2(p.x-r._Position(i).x,p.z-r._Position(i).z).magnitude<=30)
                    {
                        bool found=false;for(int j=start;j<end;j++)if(r.entryCellNodes[j]==i)found=true;
                        if(!found)errors.Add("Entry candidate omitted "+i);report.entryQueries++;
                    }
                }
                report.spatialQueries++;
            }
        }

        [MenuItem("Tools/KominkaHillside/Course/24 - Verify Additive Scene Guard")]
        public static void VerifyAdditiveGuard()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var target=EditorSceneManager.GetActiveScene();
            var other=EditorSceneManager.OpenScene(KH_ProductionScene.LegacyPath,OpenSceneMode.Additive);
            bool rejected=false;
            try
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(target);
                try{KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);}catch(InvalidOperationException){rejected=true;}
                if(!rejected||target.isDirty||other.isDirty)throw new InvalidOperationException("Additive guard failed or scene became dirty");
            }
            finally{EditorSceneManager.CloseScene(other,true);UnityEngine.SceneManagement.SceneManager.SetActiveScene(target);}
            Debug.Log("[KH Course Route] ADDITIVE_GUARD_OK; no builder mutations executed");
        }
    }
}
