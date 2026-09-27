using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    public static class KH_CourseGroundVerify
    {
        [Serializable] public class Report
        {
            public string status,scene,utc;
            public int tiles,waterChunks,bridges,footSamples,protectedSamples,marginSamples,errors;
            public float loopLengthMetres,maxSeamGap,maxMarginJoinDifference,maxProtectedHeightDifference,maxFootStep,maxRouteSlope;
            public Vector3[] routeControlPoints,routeSamples,riverSamples;
            public string[] findings;
            public string limitations="Saved geometry only. Full navigation/dog route, culling, visual finishing, walking time and device/16-player acceptance remain incomplete.";
        }
        [MenuItem("Tools/KominkaHillside/Course/14 - Verify Full Course Ground")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var report=new Report{scene=SceneManager.GetActiveScene().path,utc=DateTime.UtcNow.ToString("o")};var errors=new List<string>();
            var original=AssetDatabase.LoadAssetAtPath<TerrainData>(KH_TerrainPrototypeConfig.TerrainDataPath);
            var grid=new Terrain[5,4];
            foreach(var t in UnityEngine.Object.FindObjectsOfType<Terrain>())
            {
                if(!AssetDatabase.GetAssetPath(t.terrainData).StartsWith(KH_CourseGroundEditor.Dir+"/"))continue;
                int x=Mathf.RoundToInt(t.transform.position.x/120),z=Mathf.RoundToInt(t.transform.position.z/180);
                if(x<0||x>=5||z<0||z>=4){errors.Add("Terrain outside grid "+t.name);continue;}
                if(grid[x,z]!=null)errors.Add("Duplicate terrain cell");grid[x,z]=t;report.tiles++;
                if(t.terrainData==original||t.terrainData.terrainLayers.Length!=4||t.GetComponent<TerrainCollider>().terrainData!=t.terrainData)errors.Add("Terrain ownership/layers/collider "+t.name);
                if(!t.drawHeightmap||!t.allowAutoConnect||t.groupingID!=302)errors.Add("Terrain visibility/neighbors config "+t.name);
            }
            if(report.tiles!=20)errors.Add("Expected 20 terrain tiles");
            for(int z=0;z<4;z++)for(int x=0;x<5;x++)
            {
                var a=grid[x,z];if(a==null)continue;
                if(x<4&&grid[x+1,z]!=null)
                {
                    var b=grid[x+1,z];for(int k=0;k<257;k++)report.maxSeamGap=Mathf.Max(report.maxSeamGap,Mathf.Abs(a.terrainData.GetHeight(256,k)-b.terrainData.GetHeight(0,k)));
                    if(a.rightNeighbor!=b||b.leftNeighbor!=a)errors.Add("X neighbor "+x+","+z);
                }
                if(z<3&&grid[x,z+1]!=null)
                {
                    var b=grid[x,z+1];for(int k=0;k<257;k++)report.maxSeamGap=Mathf.Max(report.maxSeamGap,Mathf.Abs(a.terrainData.GetHeight(k,256)-b.terrainData.GetHeight(k,0)));
                    if(a.topNeighbor!=b||b.bottomNeighbor!=a)errors.Add("Z neighbor "+x+","+z);
                }
            }
            if(report.maxSeamGap>.003f)errors.Add("Terrain seam gap exceeds 3 mm");
            var old=grid[0,0];if(old!=null)
            {
                var path=KH_TerrainShape.SamplePath();
                foreach(var p in path)foreach(float dx in new[]{-4f,0,4f})CompareProtected(original,old.terrainData,p.xz.x+dx,p.xz.y,report);
                for(int a=0;a<24;a++)for(int radius=0;radius<=14;radius+=2)CompareProtected(original,old.terrainData,72+Mathf.Cos(a*Mathf.PI/12)*radius,127+Mathf.Sin(a*Mathf.PI/12)*radius,report);
                if(report.maxProtectedHeightDifference>.003f)errors.Add("Old path/plaza changed by more than 3 mm");
            }
            Physics.SyncTransforms();var points=KH_CourseGroundEditor.Route;report.routeControlPoints=KH_CourseGroundEditor.ControlPoints;report.routeSamples=points;
            report.riverSamples=new Vector3[241];
            for(int i=0;i<report.riverSamples.Length;i++){float z=i*3f;var r=KH_CourseGroundEditor.RiverAt(z);report.riverSamples[i]=new Vector3(r.x,r.y,z);}
            for(int i=1;i<points.Length;i++)
            {
                var a=points[i-1];var b=points[i];var direction=(b-a);direction.y=0;var side=Vector3.Cross(Vector3.up,direction.normalized);
                int count=Mathf.CeilToInt(direction.magnitude/.5f);report.loopLengthMetres+=Vector3.Distance(a,b);
                foreach(float lane in new[]{-1f,0,1f})
                {
                    float last=0;
                    for(int k=0;k<=count;k++)
                    {
                        var p=Vector3.Lerp(a,b,k/(float)count)+side*lane;RaycastHit hit;report.footSamples++;
                        if(!Physics.Raycast(new Vector3(p.x,150,p.z),Vector3.down,out hit,160,(1<<0)|(1<<11),QueryTriggerInteraction.Ignore))
                        {errors.Add("Footing hole segment="+i+" sample="+k);continue;}
                        if(k>0)
                        {
                            float step=Mathf.Abs(hit.point.y-last);report.maxFootStep=Mathf.Max(report.maxFootStep,step);
                            float slope=Mathf.Atan2(step,direction.magnitude/count)*Mathf.Rad2Deg;report.maxRouteSlope=Mathf.Max(report.maxRouteSlope,slope);
                            if(slope>25)errors.Add("Route slope segment="+i+" lane="+lane+" sample="+k+" degrees="+slope);
                        }
                        last=hit.point.y;
                    }
                }
            }
            report.loopLengthMetres+=KH_TerrainShape.PathLength(KH_TerrainShape.SamplePath());
            var root=GameObject.Find("KH_CourseExtension");
            var margin=root.transform.Find("LandscapeMargin").GetComponent<MeshFilter>().sharedMesh;
            var vertices=margin.vertices;var used=new bool[vertices.Length];foreach(int index in margin.triangles)used[index]=true;
            for(int i=vertices.Length-4608;i<vertices.Length;i++)
            {
                var p=vertices[i];int x=Mathf.Clamp(Mathf.FloorToInt(p.x/120),0,4),z=Mathf.Clamp(Mathf.FloorToInt(p.z/180),0,3);
                var terrain=grid[x,z];if(terrain==null)continue;
                float ground=terrain.terrainData.GetInterpolatedHeight((p.x-x*120)/120,(p.z-z*180)/180);
                report.maxMarginJoinDifference=Mathf.Max(report.maxMarginJoinDifference,Mathf.Abs(p.y-ground));report.marginSamples++;
                if(!used[i])errors.Add("Unused margin boundary vertex "+i);
            }
            if(report.maxMarginJoinDifference>.005f)errors.Add("Scenic margin differs from actual terrain edge by more than 5 mm");
            report.waterChunks=root.transform.Find("RiverWater").childCount;if(report.waterChunks!=24)errors.Add("Expected 24 water chunks");
            foreach(float z in new[]{190f,490f})
            {
                var bridge=root.transform.Find("Bridge_"+z);if(bridge==null||bridge.GetComponents<BoxCollider>().Length!=3)errors.Add("Bridge missing "+z);else report.bridges++;
            }
            var pipeline=UnityEngine.Object.FindObjectOfType<VRC.Core.PipelineManager>();
            if(pipeline==null)errors.Add("Production PipelineManager missing");
            // The promoted scene intentionally retains its existing upload ID. Identity is checked during promotion.
            foreach(var r in root.GetComponentsInChildren<MeshRenderer>())foreach(var material in r.sharedMaterials)
                if(material==null||material.shader==null||ShaderUtil.ShaderHasError(material.shader))errors.Add("Missing/broken material "+r.name);
            report.errors=errors.Count;report.findings=errors.ToArray();report.status=errors.Count==0?"GROUND_STRUCTURE_OK":"FAIL";
            Directory.CreateDirectory("Temp/des-env-002");File.WriteAllText("Temp/des-env-002/full-course-ground.json",JsonUtility.ToJson(report,true));
            Debug.Log("[KH Course] "+report.status+" errors="+errors.Count+" seams="+report.maxSeamGap+" protected="+report.maxProtectedHeightDifference+" worstSlope="+report.maxRouteSlope+" length="+report.loopLengthMetres);
        }
        static void CompareProtected(TerrainData source,TerrainData current,float x,float z,Report r)
        {
            r.protectedSamples++;r.maxProtectedHeightDifference=Mathf.Max(r.maxProtectedHeightDifference,Mathf.Abs(source.GetInterpolatedHeight(x/120,z/180)-current.GetInterpolatedHeight(x/120,z/180)));
        }
    }
}
