using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // Ground construction stage of IMP-ENV-004. The production scene is never regenerated.
    public static class KH_CourseGroundEditor
    {
        public const string ScenePath=KH_ProductionScene.Path;
        public const string Dir="Assets/KominkaHillside/Data/Environment/Course";
        const string Source=KH_ProductionScene.LegacyPath;
        const string RiverDir="Assets/KominkaHillside/Data/Environment/River";
        const int Resolution=257, Columns=5, Rows=4;
        public const float TileX=120, TileZ=180;
        const float Vertical=100;
        static TerrainData original;
        // Adopted layout. Heights/distances remain adjustable pending actual dog-walking measurements.
        public static readonly Vector3[] ControlPoints={
            new Vector3(72,19.04f,127),
            new Vector3(82,21,180),
            new Vector3(140,23,205),
            new Vector3(70,25,250),
            new Vector3(165,28,280),
            new Vector3(90,30,330),
            new Vector3(175,33,365),
            new Vector3(95,35.5f,415),
            new Vector3(165,38,465),
            new Vector3(115,39.5f,515),
            new Vector3(175,40,550),
            new Vector3(215,40,605),
            new Vector3(240,40,665),
            new Vector3(335,38,660),
            new Vector3(390,34,610),
            new Vector3(340,30,560),
            new Vector3(400,25,545),
            new Vector3(410,23,525),
            new Vector3(400,20.4f,490),
            new Vector3(430,20.4f,490),
            new Vector3(435,19,475),
            new Vector3(380,16,410),
            new Vector3(460,13,345),
            new Vector3(505,10,285),
            new Vector3(440,7,220),
            new Vector3(430,5.5f,190),
            new Vector3(400,5.5f,190),
            new Vector3(375,5,150),
            new Vector3(325,4.7f,160),
            new Vector3(280,4.2f,250),
            new Vector3(225,3.6f,150),
            new Vector3(260,3.3f,110),
            new Vector3(215,2.7f,80),
            new Vector3(145,2.2f,45),
            new Vector3(100,1.8f,55),
            new Vector3(75,3.6f,53),
            new Vector3(57,3.4f,52),
            new Vector3(43.49f,2.34f,45.77f),
            new Vector3(47.6f,1.63f,40.1f)
        };
        public static readonly Vector3[] Route=RoundCorners(ControlPoints);
        static readonly List<int>[,] RouteCells=IndexRoute();
        // Ascending z, centre x and water height. Extends beyond both ends of the playable terrain.
        static readonly Vector3[] River={
            new Vector3(295,.1f,-30),new Vector3(295,.45f,5),new Vector3(300,.8f,35),new Vector3(335,1.8f,75),
            new Vector3(350,2.6f,115),new Vector3(390,3.5f,140),new Vector3(415,4.2f,170),new Vector3(417.5f,4.6f,190),
            new Vector3(420,6.1f,220),new Vector3(485,9.1f,285),new Vector3(440,12.1f,345),new Vector3(360,15.1f,410),
            new Vector3(415,18.5f,475),new Vector3(420,20.5f,505),new Vector3(440,23,535),new Vector3(435,31,590),
            new Vector3(470,43,680),new Vector3(490,48,740)
        };
        public static float Smooth(float a,float b,float v){return Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,v));}
        public static Vector2 RiverAt(float z)
        {
            int i=0;while(i<River.Length-2&&z>River[i+1].z)i++;
            var a=River[i];var b=River[i+1];float t=Mathf.Clamp01((z-a.z)/(b.z-a.z));
            var prev=River[Mathf.Max(0,i-1)];var next=River[Mathf.Min(River.Length-1,i+2)];
            float ma=(b.x-prev.x)/(b.z-prev.z),mb=(next.x-a.x)/(next.z-a.z),len=b.z-a.z;
            float x=(2*t*t*t-3*t*t+1)*a.x+(t*t*t-2*t*t+t)*ma*len+(-2*t*t*t+3*t*t)*b.x+(t*t*t-t*t)*mb*len;
            return new Vector2(x,Mathf.Lerp(a.y,b.y,t));
        }
        public static float HalfWidth(float z){return 2.6f+.55f*Mathf.Sin(z*.043f)+.3f*Mathf.Sin(z*.17f+.3f);}
        static Vector3[] RoundCorners(Vector3[] controls)
        {
            var points=new List<Vector3>{controls[0]};
            for(int i=1;i<controls.Length-1;i++)
            {
                var p=controls[i];
                // Keep both bridge crossings exactly straight and aligned with the decks.
                if((p.z==190||p.z==490)&&(p.x==400||p.x==430)){points.Add(p);continue;}
                float before=Vector3.Distance(p,controls[i-1]),after=Vector3.Distance(p,controls[i+1]);
                float trim=Mathf.Min(8,Mathf.Min(before,after)*.2f);
                var a=Vector3.Lerp(p,controls[i-1],trim/before);var b=Vector3.Lerp(p,controls[i+1],trim/after);
                for(int k=0;k<=4;k++){float t=k/4f;points.Add(a*(1-t)*(1-t)+p*2*t*(1-t)+b*t*t);}
            }
            points.Add(controls[controls.Length-1]);return points.ToArray();
        }
        static List<int>[,] IndexRoute()
        {
            var cells=new List<int>[20,24];for(int z=0;z<24;z++)for(int x=0;x<20;x++)cells[x,z]=new List<int>();
            for(int i=1;i<Route.Length;i++)
            {
                var a=Route[i-1];var b=Route[i];
                int minX=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x,b.x)-32)/32),0,19),maxX=Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.x,b.x)+32)/32),0,19);
                int minZ=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.z,b.z)-32)/32),0,23),maxZ=Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.z,b.z)+32)/32),0,23);
                for(int z=minZ;z<=maxZ;z++)for(int x=minX;x<=maxX;x++)cells[x,z].Add(i);
            }
            return cells;
        }
        // Only a road within 32 m can affect the ground/paint. Empty cells return infinity.
        public static float RouteDistance(float x,float z,out float height)
        {
            var p=new Vector2(x,z);float best=float.MaxValue;height=0;
            var candidates=RouteCells[Mathf.Clamp(Mathf.FloorToInt(x/32),0,19),Mathf.Clamp(Mathf.FloorToInt(z/32),0,23)];
            foreach(int i in candidates)
            {
                Vector2 a=new Vector2(Route[i-1].x,Route[i-1].z),b=new Vector2(Route[i].x,Route[i].z),ab=b-a;
                float t=Mathf.Clamp01(Vector2.Dot(p-a,ab)/ab.sqrMagnitude),d=(p-a-ab*t).sqrMagnitude;
                if(d<best){best=d;height=Mathf.Lerp(Route[i-1].y,Route[i].y,t);}
            }
            return Mathf.Sqrt(best);
        }
        // Selecting one nearest segment creates a vertical seam where two switchbacks tie.
        // Distance weighting keeps road centres close without reintroducing that selection.
        static float ShoulderRoadHeight(float x,float z,float fallback,bool favorNearest=false)
        {
            var p=new Vector2(x,z);float weighted=0,total=0;
            var candidates=RouteCells[Mathf.Clamp(Mathf.FloorToInt(x/32),0,19),Mathf.Clamp(Mathf.FloorToInt(z/32),0,23)];
            foreach(int i in candidates)
            {
                var a=new Vector2(Route[i-1].x,Route[i-1].z);var b=new Vector2(Route[i].x,Route[i].z);var ab=b-a;
                float length=ab.magnitude;if(length<.0001f)continue;
                float t=Mathf.Clamp01(Vector2.Dot(p-a,ab)/(length*length));
                float distanceSquared=(p-a-ab*t).sqrMagnitude;
                float q=Mathf.Clamp01(1-distanceSquared/(32*32));
                float weight=q*q*q*length;
                if(favorNearest){float denominator=Mathf.Max(.01f,distanceSquared);weight/=denominator*denominator;}
                total+=weight;weighted+=weight*Mathf.Lerp(Route[i-1].y,Route[i].y,t);
            }
            return total>(favorNearest?0:.00001f)?weighted/total:fallback;
        }
        static float ExtensionHeight(float x,float z,int shoulderVersion)
        {
            float h=2+Mathf.Max(0,z-80)*.055f;
            h+=15*Mathf.Exp(-Mathf.Pow((x-220)/115,2)-Mathf.Pow((z-380)/230,2));
            h+=(Mathf.PerlinNoise(x*.013f+12,z*.012f+6)-.5f)*14;
            h+=15*Smooth(40,0,x)+12*Smooth(535,600,x)+14*Smooth(660,720,z);
            float road;float d=RouteDistance(x,z,out road);
            float shoulder=road;
            if(shoulderVersion==1&&d>6&&d<32)
                shoulder=Mathf.Lerp(road,ShoulderRoadHeight(x,z,road),Smooth(6,12,d));
            // A nearest-segment blend still tears at the inside of tight bends.
            // Continuous inverse-distance weights keep the centre close to the authored
            // road while blending both arms where their distances are similar.
            if(shoulderVersion>=2&&d<32)shoulder=ShoulderRoadHeight(x,z,road,true);
            h=Mathf.Lerp(shoulder,h,Smooth(3.2f,32,d));
            float plaza=Vector2.Distance(new Vector2(x,z),new Vector2(215,605));
            h=Mathf.Lerp(40,h,Smooth(13,28,plaza));
            var river=RiverAt(z);float bankD=Mathf.Abs(x-river.x),side=Mathf.Sign(x-river.x);
            float width=HalfWidth(z)+side*.8f*Mathf.Sin(z*.07f)+.35f*Mathf.Sin(z*.31f+x*.23f);
            float bank=river.y-.4f+.75f*Smooth(width*.45f,width+1.7f,bankD)+.55f*Smooth(width+2,width+5,bankD);
            // Fixed flat bridge abutments. River bed remains below the deck.
            for(int bridgeIndex=0;bridgeIndex<2;bridgeIndex++)
            {
                float bridgeZ=bridgeIndex==0?190:490;
                float approach=(1-Smooth(1.8f,4.2f,Mathf.Abs(z-bridgeZ)))*Smooth(4.1f,4.8f,bankD)*(1-Smooth(12,20,bankD));
                bank=Mathf.Lerp(bank,RiverAt(bridgeZ).y+.9f,approach);
            }
            h=Mathf.Lerp(bank,h,Smooth(width+5,width+20,bankD));
            // Restore the walk band on either bank, while keeping the wet channel carved out.
            h=Mathf.Lerp(h,shoulderVersion>=2?shoulder:road,(1-Smooth(2.5f,5.5f,d))*Smooth(width+3,width+6,bankD));
            return Mathf.Max(.01f,h);
        }
        static float OldTileHeight(float x,float z)
        {
            float old=original.GetInterpolatedHeight(x/TileX,z/TileZ),road;
            float d=RouteDistance(x,z,out road);
            float exit=Mathf.Max(Smooth(143,158,z),Smooth(89,105,x)*(1-Smooth(30,40,Mathf.Abs(z-54))));
            // Old plaza and old path remain outside these two narrow connection corridors.
            float w=exit*(1-Smooth(5,15,d));
            return Mathf.Lerp(old,road,w);
        }
        public static float Height(float x,float z)
        {return HeightForRoadShoulderComparison(x,z,2);}
        public static float HeightForRoadShoulderComparison(float x,float z,int shoulderVersion)
        {
            float cx=Mathf.Clamp(x,0,TileX),cz=Mathf.Clamp(z,0,TileZ);
            float distance=Vector2.Distance(new Vector2(x,z),new Vector2(cx,cz));
            if(distance<.0001f)return OldTileHeight(x,z);
            float roadHeight;float roadDistance=RouteDistance(x,z,out roadHeight);
            float roadJoin=(1-Smooth(5,15,roadDistance))*Smooth(0,8,distance);
            return Mathf.Lerp(OldTileHeight(cx,cz),ExtensionHeight(x,z,shoulderVersion),Mathf.Max(Smooth(0,35,distance),roadJoin));
        }
        static GameObject Child(string name,Transform parent)
        {var go=new GameObject(name);go.transform.SetParent(parent,false);return go;}

        [MenuItem("Tools/KominkaHillside/Course/13 - Build Full Course Ground Copy")]
        public static void Build()
        {
            Idle(Source);
            if(File.Exists(ScenePath)||Directory.Exists(Dir))throw new InvalidOperationException("Course exists; do not overwrite artist work");
            original=AssetDatabase.LoadAssetAtPath<TerrainData>(KH_TerrainPrototypeConfig.TerrainDataPath);
            var bridgeMesh=AssetDatabase.LoadAssetAtPath<Mesh>(RiverDir+"/KH_SmallBridge.asset");
            var bridgeMat=AssetDatabase.LoadAssetAtPath<Material>(RiverDir+"/KH_RiverWood.mat");
            var waterMat=AssetDatabase.LoadAssetAtPath<Material>(RiverDir+"/KH_RiverWater.mat");
            if(original==null||original.terrainLayers.Length!=4||bridgeMesh==null||bridgeMat==null||waterMat==null)throw new InvalidOperationException("Source / representative assets missing");
            Directory.CreateDirectory(Dir);AssetDatabase.Refresh();
            if(!AssetDatabase.CopyAsset(Source,ScenePath))throw new IOException("Course scene copy failed");
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            var pipeline=UnityEngine.Object.FindObjectOfType<VRC.Core.PipelineManager>();
            if(pipeline!=null){pipeline.blueprintId=string.Empty;EditorUtility.SetDirty(pipeline);}
            var root=new GameObject("KH_CourseExtension");
            var tiles=new Terrain[Columns,Rows];
            for(int tz=0;tz<Rows;tz++)for(int tx=0;tx<Columns;tx++)
            {
                string path=Dir+"/KH_CourseTerrain_"+tx+"_"+tz+".asset";
                TerrainData td;GameObject go;
                if(tx==0&&tz==0)
                {
                    if(!AssetDatabase.CopyAsset(KH_TerrainPrototypeConfig.TerrainDataPath,path))throw new IOException("Terrain copy failed");
                    td=AssetDatabase.LoadAssetAtPath<TerrainData>(path);go=GameObject.Find("KH_Terrain");
                    go.GetComponent<Terrain>().terrainData=td;go.GetComponent<TerrainCollider>().terrainData=td;
                }
                else
                {
                    td=new TerrainData{name="KH_CourseTerrain_"+tx+"_"+tz,heightmapResolution=Resolution,alphamapResolution=256,terrainLayers=original.terrainLayers};
                    AssetDatabase.CreateAsset(td,path);go=Terrain.CreateTerrainGameObject(td);go.name=td.name;go.transform.SetParent(root.transform,false);
                }
                td.size=new Vector3(TileX,Vertical,TileZ);go.transform.position=new Vector3(tx*TileX,0,tz*TileZ);go.layer=11;
                var h=new float[Resolution,Resolution];
                for(int z=0;z<Resolution;z++)for(int x=0;x<Resolution;x++)h[z,x]=Height(tx*TileX+x*TileX/256,tz*TileZ+z*TileZ/256)/Vertical;
                td.SetHeights(0,0,h);Paint(td,tx,tz);
                var t=go.GetComponent<Terrain>();t.drawInstanced=true;t.heightmapPixelError=8;t.detailObjectDistance=30;t.groupingID=302;t.allowAutoConnect=true;tiles[tx,tz]=t;
                EditorUtility.SetDirty(td);
            }
            for(int z=0;z<Rows;z++)for(int x=0;x<Columns;x++)tiles[x,z].SetNeighbors(x>0?tiles[x-1,z]:null,z<Rows-1?tiles[x,z+1]:null,x<Columns-1?tiles[x+1,z]:null,z>0?tiles[x,z-1]:null);
            var waterRoot=Child("RiverWater",root.transform);
            for(int i=0;i<24;i++)
            {
                var mesh=WaterMesh(i*30,30);AssetDatabase.CreateAsset(mesh,Dir+"/KH_CourseWater_"+i+".asset");
                var go=Child("Water_"+i,waterRoot.transform);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=waterMat;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            }
            foreach(float z in new[]{190f,490f})
            {
                var rv=RiverAt(z);var b=Child("Bridge_"+z,root.transform);b.transform.position=new Vector3(rv.x,rv.y+.9f,z);b.layer=11;
                b.AddComponent<MeshFilter>().sharedMesh=bridgeMesh;b.AddComponent<MeshRenderer>().sharedMaterial=bridgeMat;
                var deck=b.AddComponent<BoxCollider>();deck.center=new Vector3(0,-.125f,0);deck.size=new Vector3(10,.25f,3.5f);
                foreach(float side in new[]{-1.72f,1.72f}){var rail=b.AddComponent<BoxCollider>();rail.center=new Vector3(0,.55f,side);rail.size=new Vector3(10.2f,1.1f,.2f);}
            }
            OpenConnections(root.transform);BuildLandscape(root.transform);MoveBackdrop();
            // Reference camera far plane must include the expanded landscape. Near remains as authored.
            var descriptor=UnityEngine.Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            if(descriptor!=null&&descriptor.ReferenceCamera!=null)
            {var camera=descriptor.ReferenceCamera.GetComponent<Camera>();if(camera!=null){camera.farClipPlane=Mathf.Max(camera.farClipPlane,2200);EditorUtility.SetDirty(camera);}}
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("Course save failed");
            Debug.Log("[KH Course] Saved full ground copy: 20 terrain tiles, 24 water chunks, 2 bridges. Verify ground separately; dog route, full navigation, scenery culling and devices are NOT complete.");
        }
        public static void Idle(string path)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||BuildPipeline.isBuildingPlayer||StaticOcclusionCulling.isRunning||SceneManager.GetActiveScene().path!=path)throw new InvalidOperationException("Expected scene in idle Edit mode");
            if(SceneManager.sceneCount!=1)throw new InvalidOperationException("Exactly one loaded scene required; close additive scenes before course operations");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Unsaved scene; nothing changed");
        }
        [MenuItem("Tools/KominkaHillside/Course/15 - Refresh Owned Course Ground")]
        public static void Refresh()
        {RefreshGround(true);}
        public static void RefreshHeightsAndPaintOnly()
        {RefreshGround(false);}
        static void RefreshGround(bool refreshBackground)
        {
            Idle(ScenePath);original=AssetDatabase.LoadAssetAtPath<TerrainData>(KH_TerrainPrototypeConfig.TerrainDataPath);
            var root=GameObject.Find("KH_CourseExtension");if(root==null||original==null)throw new InvalidOperationException("Course/source missing");
            var tiles=new Terrain[Columns,Rows];
            foreach(var t in UnityEngine.Object.FindObjectsOfType<Terrain>())
            {
                int x=Mathf.RoundToInt(t.transform.position.x/TileX),z=Mathf.RoundToInt(t.transform.position.z/TileZ);
                if(x<0||x>=Columns||z<0||z>=Rows)continue;
                if(AssetDatabase.GetAssetPath(t.terrainData)==Dir+"/KH_CourseTerrain_"+x+"_"+z+".asset")tiles[x,z]=t;
            }
            for(int z=0;z<Rows;z++)for(int x=0;x<Columns;x++)if(tiles[x,z]==null)throw new InvalidOperationException("Owned course grid incomplete");
            for(int tz=0;tz<Rows;tz++)for(int tx=0;tx<Columns;tx++)
            {
                var td=tiles[tx,tz].terrainData;var h=new float[Resolution,Resolution];
                for(int z=0;z<Resolution;z++)for(int x=0;x<Resolution;x++)h[z,x]=Height(tx*TileX+x*TileX/256,tz*TileZ+z*TileZ/256)/Vertical;
                td.SetHeights(0,0,h);Paint(td,tx,tz);EditorUtility.SetDirty(td);tiles[tx,tz].Flush();
            }
            if(refreshBackground){BuildLandscape(root.transform);MoveBackdrop();}
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);
            if(!EditorSceneManager.SaveScene(root.scene))throw new IOException("Course refresh save failed");
        }
        static void Paint(TerrainData td,int tx,int tz)
        {
            if(KH_CourseAutumnEditor.PaintIfAdopted(td,tx,tz))return;
            var map=new float[256,256,4];var source=tx==0&&tz==0?original.GetAlphamaps(0,0,256,256):null;
            for(int iz=0;iz<256;iz++)for(int ix=0;ix<256;ix++)
            {
                float x=tx*TileX+ix*TileX/255,z=tz*TileZ+iz*TileZ/255,road;
                float distance=RouteDistance(x,z,out road),path=1-Smooth(1.5f,3.3f,distance);
                float riverD=Mathf.Abs(x-RiverAt(z).x),sand=Mathf.Max(path,(1-Smooth(HalfWidth(z)+2,HalfWidth(z)+6,riverD))*.9f);
                float plaza=1-Smooth(10,15,Vector2.Distance(new Vector2(x,z),new Vector2(215,605)));sand=Mathf.Max(sand,plaza*.9f);
                float rock=Smooth(24,44,td.GetSteepness(ix/255f,iz/255f));
                float mottling=Mathf.Clamp01((Mathf.PerlinNoise(x*.09f,z*.09f)-.35f)*.5f);
                map[iz,ix,0]=(1-sand)*(1-rock)*(1-mottling);map[iz,ix,1]=(1-sand)*(1-rock)*mottling;
                map[iz,ix,2]=sand;map[iz,ix,3]=(1-sand)*rock;
                if(source!=null)
                {
                    float exit=Mathf.Max(Smooth(143,158,z),Smooth(89,105,x)*(1-Smooth(30,40,Mathf.Abs(z-54))));
                    float returnPaint=Smooth(48,58,x)*(1-Smooth(2,4,distance))*(1-Smooth(62,75,z));
                    float w=Mathf.Max(exit*(1-Smooth(5,15,distance)),returnPaint);for(int l=0;l<4;l++)map[iz,ix,l]=Mathf.Lerp(source[iz,ix,l],map[iz,ix,l],w);
                }
            }
            td.SetAlphamaps(0,0,map);
        }
        static Mesh WaterMesh(float start,float length)
        {
            const int rows=21,cols=9;var v=new Vector3[rows*cols];var uv=new Vector2[v.Length];var color=new Color[v.Length];var tris=new List<int>();
            for(int z=0;z<rows;z++)for(int x=0;x<cols;x++)
            {
                float pz=start+length*z/(rows-1),u=x/(float)(cols-1);var river=RiverAt(pz);
                float px=river.x+(u-.5f)*2*(HalfWidth(pz)+1.8f);int i=z*cols+x;
                v[i]=new Vector3(px,river.y,pz);uv[i]=new Vector2((px-river.x)*.55f,pz/3);
                float shade=Mathf.Max(1-Smooth(1.2f,3.4f,Mathf.Abs(pz-190)),1-Smooth(1.2f,3.4f,Mathf.Abs(pz-490)));
                color[i]=new Color(Mathf.Clamp01(1-(river.y-Height(px,pz))/.52f),shade,0,1);
                if(x<cols-1&&z<rows-1)tris.AddRange(new[]{i,i+cols,i+1,i+1,i+cols,i+cols+1});
            }
            var m=new Mesh{name="KH_CourseWater"};m.vertices=v;m.uv=uv;m.colors=color;m.SetTriangles(tris,0);m.RecalculateNormals();m.RecalculateBounds();return m;
        }
        static void OpenConnections(Transform root)
        {
            var n=GameObject.Find("KH_Bound_North").GetComponent<BoxCollider>();var e=GameObject.Find("KH_Bound_East").GetComponent<BoxCollider>();
            n.enabled=false;e.enabled=false;EditorUtility.SetDirty(n);EditorUtility.SetDirty(e);
            Boundary(root,"NorthWest",new Vector2(10,168),new Vector2(69,168));Boundary(root,"NorthEast",new Vector2(93,168),new Vector2(110,168));
            Boundary(root,"EastSouth",new Vector2(108,10),new Vector2(108,35));Boundary(root,"EastNorth",new Vector2(108,73),new Vector2(108,170));
            // Outside the complete loop, with enough scenic ground beyond the invisible wall.
            for(int x=0;x<600;x+=20){Boundary(root,"OuterSouth_"+x,new Vector2(x,8),new Vector2(x+20,8));Boundary(root,"OuterNorth_"+x,new Vector2(x,712),new Vector2(x+20,712));}
            for(int z=0;z<720;z+=20){Boundary(root,"OuterWest_"+z,new Vector2(8,z),new Vector2(8,z+20));Boundary(root,"OuterEast_"+z,new Vector2(592,z),new Vector2(592,z+20));}
        }
        static void Boundary(Transform root,string name,Vector2 a,Vector2 b)
        {
            float low=float.MaxValue,high=float.MinValue;for(int i=0;i<=8;i++){var p=Vector2.Lerp(a,b,i/8f);float h=Height(p.x,p.y);low=Mathf.Min(low,h);high=Mathf.Max(high,h);}
            var go=Child(name,root);go.layer=11;var mid=(a+b)*.5f;go.transform.position=new Vector3(mid.x,(low+high+3)/2,mid.y);
            var box=go.AddComponent<BoxCollider>();box.size=new Vector3(Mathf.Max(.5f,Mathf.Abs(b.x-a.x)+.4f),high-low+5,Mathf.Max(.5f,Mathf.Abs(b.y-a.y)+.4f));
        }
        static void BuildLandscape(Transform root)
        {
            const int sideSteps=64,rings=17,perimeter=sideSteps*4,innerCount=1280*2+1024*2;
            var v=new Vector3[rings*perimeter+innerCount];var colors=new Color[v.Length];var uv=new Vector2[v.Length];var tris=new List<int>();
            for(int ring=0;ring<rings;ring++)for(int i=0;i<perimeter;i++)
            {
                float d=ring*38f,t=(i%sideSteps)/(float)sideSteps,x,z;
                if(i<sideSteps){x=Mathf.Lerp(-d,600+d,t);z=-d;}
                else if(i<sideSteps*2){x=600+d;z=Mathf.Lerp(-d,720+d,t);}
                else if(i<sideSteps*3){x=Mathf.Lerp(600+d,-d,t);z=720+d;}
                else{x=-d;z=Mathf.Lerp(720+d,-d,t);}
                float cx=Mathf.Clamp(x,0,600),cz=Mathf.Clamp(z,0,720),edge=Height(cx,cz);
                // Fall gently away from the playable rim. The previous extra 15-50 m ridge
                // hid the authored distant forest behind an unbroken bare strip.
                float h=edge-Smooth(0,280,d)*(20-8*Mathf.PerlinNoise(x*.004f+4,z*.004f+1));
                int at=ring*perimeter+i;v[at]=new Vector3(x,h-.06f,z);uv[at]=new Vector2(x*.5f,z*.5f);
                float tint=.9f+.1f*Mathf.PerlinNoise(x*.015f,z*.015f);colors[at]=new Color(tint,tint,tint,1);
                if(ring>0&&ring<rings-1){int next=ring*perimeter+(i+1)%perimeter;tris.AddRange(new[]{at,next,at+perimeter,next,next+perimeter,at+perimeter});}
            }
            // Match every Terrain edge sample, then fan out to the coarse next ring.
            // A coarse inner edge cuts across small valleys and can visibly dam the river mouth.
            int innerStart=rings*perimeter,offset=0;
            for(int side=0;side<4;side++)
            {
                int count=side%2==0?1280:1024,perSegment=count/sideSteps;
                for(int j=0;j<count;j++)
                {
                    float t=j/(float)count,x,z;
                    if(side==0){x=t*600;z=0;}else if(side==1){x=600;z=t*720;}
                    else if(side==2){x=600-t*600;z=720;}else{x=0;z=720-t*720;}
                    int at=innerStart+offset+j,next=innerStart+(offset+j+1)%innerCount;
                    v[at]=new Vector3(x,Height(x,z)-.003f,z);uv[at]=new Vector2(x*.5f,z*.5f);
                    float tint=.9f+.1f*Mathf.PerlinNoise(x*.015f,z*.015f);colors[at]=new Color(tint,tint,tint,1);
                    int coarse=side*sideSteps+j/perSegment,outer=perimeter+coarse;
                    tris.AddRange(new[]{at,next,outer});
                    if(j%perSegment==perSegment-1)tris.AddRange(new[]{next,perimeter+(coarse+1)%perimeter,outer});
                }
                offset+=count;
            }
            var mesh=new Mesh{name="KH_CourseLandscape"};mesh.vertices=v;mesh.uv=uv;mesh.colors=colors;mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            var owned=AssetDatabase.LoadAssetAtPath<Mesh>(Dir+"/KH_CourseLandscape.asset");
            if(owned==null)AssetDatabase.CreateAsset(mesh,Dir+"/KH_CourseLandscape.asset");
            else{owned.Clear();owned.vertices=mesh.vertices;owned.normals=mesh.normals;owned.colors=mesh.colors;owned.uv=mesh.uv;owned.triangles=mesh.triangles;owned.RecalculateBounds();EditorUtility.SetDirty(owned);UnityEngine.Object.DestroyImmediate(mesh);mesh=owned;}
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Dir+"/KH_CourseLandscape.mat");
            if(mat==null){mat=new Material(Shader.Find("KominkaHillside/Scenery Lite")){name="KH_CourseLandscape",enableInstancing=true};AssetDatabase.CreateAsset(mat,Dir+"/KH_CourseLandscape.mat");}
            mat.SetTexture("_MainTex",KH_CourseAutumnEditor.AdoptedLayersOr(original.terrainLayers)[0].diffuseTexture);EditorUtility.SetDirty(mat);
            var existing=root.Find("LandscapeMargin");var go=existing==null?Child("LandscapeMargin",root):existing.gameObject;
            var filter=go.GetComponent<MeshFilter>();if(filter==null)filter=go.AddComponent<MeshFilter>();filter.sharedMesh=mesh;
            var r=go.GetComponent<MeshRenderer>();if(r==null)r=go.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=ShadowCastingMode.Off;
        }
        [MenuItem("Tools/KominkaHillside/Course/59 - Soften Owned Landscape Margin")]
        public static void SoftenLandscape()
        {
            Idle(ScenePath);original=AssetDatabase.LoadAssetAtPath<TerrainData>(KH_TerrainPrototypeConfig.TerrainDataPath);
            var root=GameObject.Find("KH_CourseExtension");var t=root==null?null:root.transform.Find("LandscapeMargin");
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Dir+"/KH_CourseLandscape.asset");
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Dir+"/KH_CourseLandscape.mat");
            if(original==null||t==null||mesh==null||mat==null||t.GetComponent<MeshFilter>().sharedMesh!=mesh||t.GetComponent<MeshRenderer>().sharedMaterial!=mat||t.GetComponent<Collider>()!=null||mesh.vertexCount!=8960)
                throw new InvalidOperationException("Expected owned non-colliding margin required");
            string backupDir="Temp/des-env-002/landscape-polish/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(backupDir);
            File.Copy(ScenePath,backupDir+"/before.unity");File.Copy(Dir+"/KH_CourseLandscape.asset",backupDir+"/before-mesh.asset");File.Copy(Dir+"/KH_CourseLandscape.mat",backupDir+"/before-material.mat");
            var backup=UnityEngine.Object.Instantiate(mesh);var oldTexture=mat.GetTexture("_MainTex");var oldVertices=mesh.vertices;
            try
            {
                BuildLandscape(root.transform);var vertices=mesh.vertices;
                if(vertices.Length!=oldVertices.Length)throw new InvalidOperationException("Margin topology changed");
                for(int i=0;i<vertices.Length;i++)
                {
                    var p=oldVertices[i];if(vertices[i].x!=p.x||vertices[i].z!=p.z)throw new InvalidOperationException("Margin footprint changed");
                    if(p.x>=0&&p.x<=600&&p.z>=0&&p.z<=720&&vertices[i]!=p)throw new InvalidOperationException("Terrain join changed");
                    if(vertices[i].y>p.y+.001f)throw new InvalidOperationException("Margin unexpectedly raised");
                }
                AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);
                if(!EditorSceneManager.SaveScene(root.scene))throw new IOException("Margin save failed");
            }
            catch
            {
                EditorUtility.CopySerialized(backup,mesh);EditorUtility.SetDirty(mesh);mat.SetTexture("_MainTex",oldTexture);EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);throw;
            }
            finally{UnityEngine.Object.DestroyImmediate(backup);}
            Debug.Log("[KH Landscape] Lowered only the owned scenic margin; footprint/join unchanged. Backup="+backupDir+". Rebake PVS and inspect saved-state images.");
        }
        static void MoveBackdrop()
        {
            var go=GameObject.Find("KH_World/KH_Scenery/KH_ViewSet");if(go==null)throw new InvalidOperationException("Original backdrop missing");
            go.transform.position=new Vector3(300,0,360);go.transform.localScale=Vector3.one*3.6f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            // These inner pieces were sized for the old 120 x 180 m world. The new landscape replaces them.
            foreach(var renderer in go.GetComponentsInChildren<MeshRenderer>(true))
                if(renderer.name=="MS_BroadGround02"||renderer.name=="MS_BoardTrees01")
                {renderer.enabled=false;PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);}
        }
    }
}
