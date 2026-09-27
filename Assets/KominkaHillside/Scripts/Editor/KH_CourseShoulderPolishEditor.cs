using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaHillside.Editor
{
    public static class KH_CourseShoulderPolishEditor
    {
        const string Root="Temp/des-env-002/shoulder-polish-v2";
        [Serializable] public class Sample { public float z,before,after; }
        [Serializable] public class TreeMove { public string name; public Vector3 before,after; public float offset; }
        [Serializable] public class Report
        {
            public string utc,status,backup; public int samples,changedSamples,protectedSamples;
            public float maxDelta,maxSavedDifference,maxProtectedDelta,maxEdgeDelta,maxOldCliffSlope,maxNewCliffSlope;
            public float maxOldEdgeSlope,maxNewEdgeSlope;
            public float maxWalkingBandDelta,maxCenterlineDelta;
            public float maxInterpolatedCenterDelta,maxInterpolatedWalkingDelta;
            public List<Sample> profile=new List<Sample>(); public List<TreeMove> trees=new List<TreeMove>();
            public string limits="Editor terrain generation comparison; navigation, PVS, visual and device checks remain separate.";
        }
        static Terrain[] Terrains()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var source=AssetDatabase.LoadAssetAtPath<TerrainData>(KH_TerrainPrototypeConfig.TerrainDataPath);
            typeof(KH_CourseGroundEditor).GetField("original",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,source);
            var terrain=UnityEngine.Object.FindObjectsOfType<Terrain>();
            if(terrain.Length!=20||terrain.Any(t=>AssetDatabase.GetAssetPath(t.terrainData)!=KH_CourseGroundEditor.Dir+"/KH_CourseTerrain_"+Mathf.RoundToInt(t.transform.position.x/120)+"_"+Mathf.RoundToInt(t.transform.position.z/180)+".asset"))
                throw new InvalidOperationException("Expected exactly 20 owned terrain tiles");
            return terrain;
        }
        static float Height(float x,float z,bool smooth) { return KH_CourseGroundEditor.HeightForRoadShoulderComparison(x,z,smooth?2:1); }
        static float Ground(Terrain[] terrain,Vector3 p)
        {
            foreach(var t in terrain)
            {var a=t.transform.position;var size=t.terrainData.size;if(p.x>=a.x&&p.x<=a.x+size.x&&p.z>=a.z&&p.z<=a.z+size.z)return a.y+t.terrainData.GetInterpolatedHeight((p.x-a.x)/size.x,(p.z-a.z)/size.z);}
            throw new InvalidOperationException("Ground missing");
        }
        static Report Analyze(Terrain[] terrain)
        {
            var report=new Report{utc=DateTime.UtcNow.ToString("o"),status="SURVEY"};
            var planned=new List<TerrainData>();
            try
            {
            foreach(var t in terrain)
            {
                var data=t.terrainData;var a=t.transform.position;var saved=data.GetHeights(0,0,257,257);
                var nextHeights=new float[257,257];
                for(int z=0;z<257;z++)for(int x=0;x<257;x++)
                {
                    float px=a.x+x*120f/256,pz=a.z+z*180f/256;
                    float old=Height(px,pz,false),next=Height(px,pz,true),delta=Mathf.Abs(next-old),road;
                    nextHeights[z,x]=next/100;
                    float distance=KH_CourseGroundEditor.RouteDistance(px,pz,out road);report.samples++;
                    report.maxSavedDifference=Mathf.Max(report.maxSavedDifference,Mathf.Abs(saved[z,x]*100-old));
                    report.maxDelta=Mathf.Max(report.maxDelta,delta);if(delta>.002f)report.changedSamples++;
                    if(px<=120&&pz<=180){report.protectedSamples++;report.maxProtectedDelta=Mathf.Max(report.maxProtectedDelta,delta);}
                    if(distance<=1)report.maxWalkingBandDelta=Mathf.Max(report.maxWalkingBandDelta,delta);
                    if(px==0||px==600||pz==0||pz==720)report.maxEdgeDelta=Mathf.Max(report.maxEdgeDelta,delta);
                }
                var preview=new TerrainData{heightmapResolution=257,size=data.size,hideFlags=HideFlags.HideAndDontSave};
                planned.Add(preview);preview.SetHeights(0,0,nextHeights);
            }
            var route=KH_CourseGroundEditor.Route;
            for(int segment=1;segment<route.Length;segment++)
            {
                int steps=Mathf.CeilToInt(Vector3.Distance(route[segment-1],route[segment])/.5f);
                var direction=route[segment]-route[segment-1];direction.y=0;var side=Vector3.Cross(Vector3.up,direction.normalized);
                for(int k=0;k<=steps;k++)
                {
                    var p=Vector3.Lerp(route[segment-1],route[segment],k/(float)steps);
                    report.maxCenterlineDelta=Mathf.Max(report.maxCenterlineDelta,Mathf.Abs(Height(p.x,p.z,true)-Height(p.x,p.z,false)));
                    foreach(float lane in new[]{-1f,0,1f})
                    {
                        var q=p+side*lane;float before=Ground(terrain,q),after=PlannedGround(terrain,planned,q),delta=Mathf.Abs(before-after);
                        report.maxInterpolatedWalkingDelta=Mathf.Max(report.maxInterpolatedWalkingDelta,delta);
                        if(lane==0)report.maxInterpolatedCenterDelta=Mathf.Max(report.maxInterpolatedCenterDelta,delta);
                    }
                }
            }
            float previousOld=0,previousNew=0;
            for(int i=0;i<=120;i++)
            {
                float z=550+i*.5f,old=Height(369.434f,z,false),next=Height(369.434f,z,true);
                report.profile.Add(new Sample{z=z,before=old,after=next});
                if(i>0){report.maxOldCliffSlope=Mathf.Max(report.maxOldCliffSlope,Mathf.Atan2(Mathf.Abs(old-previousOld),.5f)*Mathf.Rad2Deg);report.maxNewCliffSlope=Mathf.Max(report.maxNewCliffSlope,Mathf.Atan2(Mathf.Abs(next-previousNew),.5f)*Mathf.Rad2Deg);}
                previousOld=old;previousNew=next;
            }
            // Inspect the off-road left edge as well as the original centre section.
            for(int x=348;x<=370;x++)for(int i=1;i<=80;i++)
            {
                float z=550+i*.5f;
                report.maxOldEdgeSlope=Mathf.Max(report.maxOldEdgeSlope,Mathf.Atan2(Mathf.Abs(Height(x,z,false)-Height(x,z-.5f,false)),.5f)*Mathf.Rad2Deg);
                report.maxNewEdgeSlope=Mathf.Max(report.maxNewEdgeSlope,Mathf.Atan2(Mathf.Abs(Height(x,z,true)-Height(x,z-.5f,true)),.5f)*Mathf.Rad2Deg);
            }
            return report;
            }
            finally{foreach(var preview in planned)UnityEngine.Object.DestroyImmediate(preview);}
        }
        static float PlannedGround(Terrain[] terrain,List<TerrainData> planned,Vector3 p)
        {
            for(int i=0;i<terrain.Length;i++)
            {var a=terrain[i].transform.position;var size=planned[i].size;if(p.x>=a.x&&p.x<=a.x+size.x&&p.z>=a.z&&p.z<=a.z+size.z)return a.y+planned[i].GetInterpolatedHeight((p.x-a.x)/size.x,(p.z-a.z)/size.z);}
            throw new InvalidOperationException("Planned terrain missing");
        }
        [MenuItem("Tools/KominkaHillside/Course/60 - Survey Road Shoulder Smoothing")]
        public static void Survey()
        {
            var report=Analyze(Terrains());Directory.CreateDirectory(Root);File.WriteAllText(Root+"/survey.json",JsonUtility.ToJson(report,true));
            Debug.Log("[KH Shoulders] saved="+report.maxSavedDifference+" protected="+report.maxProtectedDelta+" edge="+report.maxEdgeDelta+" delta="+report.maxDelta+" cliff="+report.maxOldCliffSlope+" -> "+report.maxNewCliffSlope+" leftEdge="+report.maxOldEdgeSlope+" -> "+report.maxNewEdgeSlope+" interpolatedCenter="+report.maxInterpolatedCenterDelta+" interpolatedWalk="+report.maxInterpolatedWalkingDelta);
        }
        [MenuItem("Tools/KominkaHillside/Course/61 - Apply Road Shoulder Smoothing")]
        public static void Apply()
        {
            if(File.Exists(Root+"/applied.json"))throw new InvalidOperationException("Already applied; preserve existing result");
            var terrain=Terrains();var report=Analyze(terrain);
            // Both off-road sections may become continuous slopes up to 30 degrees.
            // The actual walk band retains its separate 25-degree footing verifier.
            if(report.maxSavedDifference>.0035f||report.maxProtectedDelta>.00001f||report.maxEdgeDelta>.00001f||report.maxNewCliffSlope>30||report.maxNewEdgeSlope>30||report.maxInterpolatedCenterDelta>.03f||report.maxInterpolatedWalkingDelta>.15f)
                throw new InvalidOperationException("Terrain changed independently or smoothing invariant failed; inspect survey");
            var trees=GameObject.Find("KH_CourseScenery/Trees").transform.Cast<Transform>().ToArray();
            if(trees.Length!=15)throw new InvalidOperationException("Expected 15 owned trees");
            foreach(var tree in trees)report.trees.Add(new TreeMove{name=tree.name,before=tree.position,offset=tree.position.y-Ground(terrain,tree.position)});
            report.backup=Root+"/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(report.backup);
            File.Copy(KH_CourseGroundEditor.ScenePath,report.backup+"/before.unity");
            var heights=terrain.Select(t=>t.terrainData.GetHeights(0,0,257,257)).ToArray();
            var paint=terrain.Select(t=>t.terrainData.GetAlphamaps(0,0,256,256)).ToArray();
            foreach(var t in terrain)File.Copy(AssetDatabase.GetAssetPath(t.terrainData),report.backup+"/"+t.terrainData.name+".asset");
            try
            {
                KH_CourseGroundEditor.RefreshHeightsAndPaintOnly();
                for(int i=0;i<trees.Length;i++)
                {var p=trees[i].position;p.y=Ground(terrain,p)+report.trees[i].offset;trees[i].position=p;report.trees[i].after=p;EditorUtility.SetDirty(trees[i]);}
                EditorSceneManager.MarkSceneDirty(terrain[0].gameObject.scene);
                if(!EditorSceneManager.SaveScene(terrain[0].gameObject.scene))throw new IOException("Scene save failed");
                report.status="APPLIED_REQUIRES_NAV_PVS_VISUAL_CHECK";
                File.WriteAllText(Root+"/applied.json",JsonUtility.ToJson(report,true));
            }
            catch
            {
                // Restore in-memory placements before any I/O which can fail again.
                for(int i=0;i<trees.Length;i++){trees[i].position=report.trees[i].before;EditorUtility.SetDirty(trees[i]);}
                for(int i=0;i<terrain.Length;i++){var d=terrain[i].terrainData;d.SetHeights(0,0,heights[i]);d.SetAlphamaps(0,0,paint[i]);EditorUtility.SetDirty(d);terrain[i].Flush();}
                EditorSceneManager.MarkSceneDirty(terrain[0].gameObject.scene);
                bool assetsRestored=false;
                try{AssetDatabase.SaveAssets();assetsRestored=true;}catch(Exception e){Debug.LogError("[KH Shoulders] Restored terrain remains in memory; asset save failed: "+e.Message);}
                try
                {
                    if(assetsRestored)
                    {
                        File.Copy(report.backup+"/before.unity",KH_CourseGroundEditor.ScenePath,true);
                        AssetDatabase.ImportAsset(KH_CourseGroundEditor.ScenePath,ImportAssetOptions.ForceUpdate);
                        EditorSceneManager.OpenScene(KH_CourseGroundEditor.ScenePath,OpenSceneMode.Single);
                    }
                }
                catch(Exception e){Debug.LogError("[KH Shoulders] Scene restore I/O failed; memory and backup retained: "+e.Message);}
                throw;
            }
            Debug.Log("[KH Shoulders] Applied; original tile preserved and road height deltas checked. Refresh navigation/PVS and verify separately.");
        }
        [MenuItem("Tools/KominkaHillside/Course/62 - Verify Road Shoulder Smoothing")]
        public static void Verify()
        {
            var terrain=Terrains();var applied=JsonUtility.FromJson<Report>(File.ReadAllText(Root+"/applied.json"));float maxError=0,maxProtected=0;
            foreach(var t in terrain)
            {
                var h=t.terrainData.GetHeights(0,0,257,257);var a=t.transform.position;
                for(int z=0;z<257;z++)for(int x=0;x<257;x++)
                {
                    float px=a.x+x*120f/256,pz=a.z+z*180f/256,road,actual=h[z,x]*100;
                    maxError=Mathf.Max(maxError,Mathf.Abs(actual-Height(px,pz,true)));
                    if(px<=120&&pz<=180)maxProtected=Mathf.Max(maxProtected,Mathf.Abs(actual-Height(px,pz,false)));
                }
            }
            foreach(var move in applied.trees)
            {var tree=GameObject.Find("KH_CourseScenery/Trees/"+move.name).transform;if(Vector3.Distance(tree.position,move.after)>.001f||Mathf.Abs(tree.position.y-Ground(terrain,tree.position)-move.offset)>.002f)throw new InvalidOperationException("Tree grounding mismatch "+move.name);}
            if(maxError>.0035f||maxProtected>.0035f)throw new InvalidOperationException("Saved heights differ from expected surface");
            File.WriteAllText(Root+"/verified.json","{\"status\":\"SAVED_HEIGHTS_AND_TREES_OK\",\"maxError\":"+maxError.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"maxProtectedError\":"+maxProtected.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
            Debug.Log("[KH Shoulders] SAVED_HEIGHTS_AND_TREES_OK error="+maxError+" protected="+maxProtected);
        }
    }
}
