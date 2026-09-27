using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    // Representative asset slice for the approved course. It builds into a COPY, never into the source scene.
    // This scenery slice is not a replacement for the full walking graph / six-dog / device gates.
    public static partial class KH_RiverRepresentativeEditor
    {
        const string Source=KH_ProductionScene.LegacyPath;
        public const string ScenePath="Assets/Scenes/KH_RiverRepresentative.unity";
        const string Dir="Assets/KominkaHillside/Data/Environment/River";
        static readonly Vector3 Origin=new Vector3(360,0,120);
        const float Width=120, Length=180, BridgeZ=70;
        static float Center(float z) { return 57.5f+7*Mathf.Sin((z-BridgeZ)/36); }
        static float WaterHeight(float z) { return 2+.018f*z; }
        internal static float HalfWidth(float z) { return 2.5f+.55f*Mathf.Sin(z*.043f)+.3f*Mathf.Sin(z*.17f+.3f); }
        static float Smooth(float a,float b,float x) { return Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,x)); }
        static float Height(float x,float z)
        {
            float d=Mathf.Abs(x-Center(z)), water=WaterHeight(z);
            float side=Mathf.Sign(x-Center(z));
            float bank=HalfWidth(z)+side*.8f*Mathf.Sin(z*.07f)+.35f*Mathf.Sin(z*.31f+x*.23f);
            float h=water-.45f+.75f*Smooth(bank*.45f,bank+1.7f,d)+.6f*Smooth(bank+2,bank+5,d);
            h+=(Mathf.PerlinNoise(x*.18f,z*.12f)-.5f)*.35f*Smooth(bank+2,bank+5,d)*(1-Smooth(14,20,d));
            h+=Mathf.Max(0,d-16)*.16f;
            h+=(Mathf.PerlinNoise(x*.036f+2,z*.029f+4)-.5f)*2.4f*Smooth(15,28,d);
            // East-bank walk has a 4 m level band; bridge approaches meet the deck without a step.
            h=Mathf.Lerp(h,water+.9f,1-Smooth(2,4,Mathf.Abs(x-Center(z)-11)));
            float approach=(1-Smooth(1.8f,3.8f,Mathf.Abs(z-BridgeZ)))*Smooth(4.4f,5,d)*(1-Smooth(10,15,d));
            h=Mathf.Lerp(h,WaterHeight(BridgeZ)+.9f,approach);
            return h;
        }
        static GameObject Child(string name,Transform parent)
        { var go=new GameObject(name); go.transform.SetParent(parent,false); return go; }

        [MenuItem("Tools/KominkaHillside/Course/3 - Build River Representative Copy")]
        public static void Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Idle Edit mode required");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Unsaved scene; nothing changed");
            if(SceneManager.GetActiveScene().path!=Source)throw new InvalidOperationException("Open the source world first");
            if(File.Exists(ScenePath)||Directory.Exists(Dir))throw new InvalidOperationException("Representative already exists; preserve artist edits instead of regenerating");
            var sourceGo=GameObject.Find("KH_Terrain");
            var sourceTerrain=sourceGo==null?null:sourceGo.GetComponent<Terrain>();
            if(sourceTerrain==null||sourceTerrain.terrainData.terrainLayers.Length!=4)throw new InvalidOperationException("Expected the existing 4-layer terrain");
            var layers=sourceTerrain.terrainData.terrainLayers;
            if(Shader.Find("KominkaHillside/River Lite")==null)throw new InvalidOperationException("River shader is not imported");
            Directory.CreateDirectory(Dir); AssetDatabase.Refresh();
            if(!AssetDatabase.CopyAsset(Source,ScenePath))throw new IOException("Could not copy scene");
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            // A review copy must not keep the production world's upload destination.
            var pipeline=UnityEngine.Object.FindObjectOfType<VRC.Core.PipelineManager>();
            if(pipeline!=null){pipeline.blueprintId=string.Empty;EditorUtility.SetDirty(pipeline);}
            var root=new GameObject("KH_RiverRepresentative"); root.transform.position=Origin;
            var td=new TerrainData { name="KH_River_RepresentativeTerrain",heightmapResolution=257,alphamapResolution=256,size=new Vector3(Width,30,Length),terrainLayers=layers };
            // Persist first: CreateAsset can reset transient alphamap textures.
            AssetDatabase.CreateAsset(td,Dir+"/KH_RiverTerrain.asset");
            var heights=new float[257,257];
            for(int z=0;z<257;z++)for(int x=0;x<257;x++)heights[z,x]=Height(x*Width/256,z*Length/256)/30;
            td.SetHeights(0,0,heights);
            PaintBanks(td);
            var terrainGo=Terrain.CreateTerrainGameObject(td); terrainGo.name="KH_RiverTerrain";terrainGo.transform.SetParent(root.transform,false);
            var terrain=terrainGo.GetComponent<Terrain>();terrain.heightmapPixelError=8;terrain.drawInstanced=true;terrain.detailObjectDistance=30;
            terrainGo.layer=LayerMask.NameToLayer("Environment");

            CreateFlowTexture();
            var waterMat=new Material(Shader.Find("KominkaHillside/River Lite")) { name="KH_RiverWater",enableInstancing=true };
            waterMat.SetTexture("_FlowTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Dir+"/KH_RiverFlow.png"));AssetDatabase.CreateAsset(waterMat,Dir+"/KH_RiverWater.mat");
            var waters=Child("WaterChunks",root.transform);
            for(int chunk=0;chunk<6;chunk++)
            {
                var mesh=WaterMesh(chunk*30,30);AssetDatabase.CreateAsset(mesh,Dir+"/KH_Water_"+chunk+".asset");
                var go=Child("Water_"+chunk,waters.transform);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=waterMat;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            }
            BuildBridge(root.transform);
            AddVegetation(root.transform);
            var spawn=Child("RepresentativeSpawn",root.transform);spawn.transform.localPosition=new Vector3(Center(BridgeZ)+11,Height(Center(BridgeZ)+11,BridgeZ)+.08f,BridgeZ-8);spawn.transform.rotation=Quaternion.Euler(0,320,0);
            var descriptor=UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
            if(descriptor!=null){var so=new SerializedObject(descriptor);var p=so.FindProperty("spawns");p.arraySize=1;p.GetArrayElementAtIndex(0).objectReferenceValue=spawn.transform;so.ApplyModifiedPropertiesWithoutUndo();}
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("Representative scene save failed");
            Debug.Log("[KH River] Created representative copy. Original scene and TerrainData unchanged. Navigation/route connection is a later gate.");
        }

        static void PaintBanks(TerrainData td)
        {
            var splat=new float[256,256,4];
            for(int z=0;z<256;z++)for(int x=0;x<256;x++)
            {
                float px=x*Width/255,pz=z*Length/255,d=Mathf.Abs(px-Center(pz));
                float edge=HalfWidth(pz)+2.2f+Mathf.Sign(px-Center(pz))*1.5f*Mathf.Sin(pz*.07f)+.8f*Mathf.Sin(pz*.083f+px*.1f);
                float sand=Mathf.Max(1-Smooth(edge,edge+2.7f,d),1-Smooth(1.6f,3.0f,Mathf.Abs(px-Center(pz)-11)));
                sand=Mathf.Max(sand,(1-Smooth(1.4f,2.4f,Mathf.Abs(pz-BridgeZ)))*(1-Smooth(10,15,d)));
                float rock=Smooth(17,28,d)*.6f;
                float gravel=(1-Smooth(4,7,d))*.4f*sand;
                splat[z,x,0]=(1-sand)*(1-rock);splat[z,x,1]=(1-sand)*rock+gravel;splat[z,x,2]=sand-gravel;
            }
            td.SetAlphamaps(0,0,splat);
            EditorUtility.SetDirty(td);
        }
        static void CreateFlowTexture()
        {
            // One small numerical mask, shared by every water chunk; no camera/depth texture or normal map.
            var tex=new Texture2D(128,128,TextureFormat.RGB24,true,true) { name="KH_RiverFlow",wrapMode=TextureWrapMode.Repeat };
            var pixels=new Color[128*128];
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                float a=x*Mathf.PI*2/128,b=y*Mathf.PI*2/128;
                float mask=Mathf.Pow(Mathf.Clamp01(.5f+.5f*Mathf.Sin(b*3+Mathf.Sin(a)*.5f)),14)*(.5f+.5f*Mathf.Sin(a+b));
                pixels[y*128+x]=new Color(mask,mask,mask,1);
            }
            tex.SetPixels(pixels);tex.Apply();File.WriteAllBytes(Dir+"/KH_RiverFlow.png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(Dir+"/KH_RiverFlow.png");
            var ti=(TextureImporter)AssetImporter.GetAtPath(Dir+"/KH_RiverFlow.png");ti.sRGBTexture=false;ti.mipmapEnabled=true;ti.isReadable=false;ti.wrapMode=TextureWrapMode.Repeat;ti.textureCompression=TextureImporterCompression.Compressed;ti.maxTextureSize=128;
            foreach(var platform in new[]{"Android","iPhone"})ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name=platform,overridden=true,maxTextureSize=128,format=TextureImporterFormat.ASTC_6x6});
            ti.SaveAndReimport();
        }

        [MenuItem("Tools/KominkaHillside/Course/5 - Refresh River Surface Assets")]
        public static void RefreshSurfaceAssets()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=ScenePath||SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Clean representative scene in Edit mode required");
            var td=AssetDatabase.LoadAssetAtPath<TerrainData>(Dir+"/KH_RiverTerrain.asset");
            if(td==null)throw new InvalidOperationException("Missing representative terrain");
            PaintBanks(td); CreateFlowTexture(); AssetDatabase.SaveAssets();
        }

        static Mesh WaterMesh(float start,float length)
        {
            const int rows=16,cols=9;var vertices=new Vector3[rows*cols];var uv=new Vector2[vertices.Length];var colors=new Color[vertices.Length];var indices=new List<int>();
            for(int z=0;z<rows;z++)for(int x=0;x<cols;x++)
            {
                float pz=start+length*z/(rows-1),u=x/(float)(cols-1),px=Center(pz)+(u-.5f)*2*(HalfWidth(pz)+1.1f);
                float shallow=Mathf.Clamp01(1-(WaterHeight(pz)-Height(px,pz))/.52f);
                float shade=(1-Smooth(1.2f,3.4f,Mathf.Abs(pz-BridgeZ)))*(1-Smooth(4,5.5f,Mathf.Abs(px-Center(BridgeZ))));
                int i=z*cols+x;vertices[i]=new Vector3(px,WaterHeight(pz),pz);uv[i]=new Vector2((px-Center(pz))*.55f,pz/3);colors[i]=new Color(shallow,shade,0,1);
                if(x<cols-1&&z<rows-1){indices.Add(i);indices.Add(i+cols);indices.Add(i+1);indices.Add(i+1);indices.Add(i+cols);indices.Add(i+cols+1);}
            }
            var mesh=new Mesh{name="KH_RiverWater"};mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        static void BuildBridge(Transform root)
        {
            var b=Child("SmallBridge",root);b.transform.localPosition=new Vector3(Center(BridgeZ),WaterHeight(BridgeZ)+.9f,BridgeZ);
            var combines=new List<CombineInstance>();
            var cube=new Mesh();var vs=new List<Vector3>();var ix=new List<int>();
            Vector3[] corners={new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(-.5f,.5f,-.5f),new Vector3(-.5f,-.5f,.5f),new Vector3(.5f,-.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,.5f,.5f)};
            int[] faces={0,3,2,1,5,6,7,4,4,7,3,0,1,2,6,5,3,7,6,2,4,0,1,5};
            for(int f=0;f<6;f++){int j=vs.Count;for(int k=0;k<4;k++)vs.Add(corners[faces[f*4+k]]);ix.AddRange(new[]{j,j+1,j+2,j,j+2,j+3});}
            cube.SetVertices(vs);cube.SetTriangles(ix,0);cube.RecalculateNormals();
            Action<Vector3,Vector3> box=(p,s)=>combines.Add(new CombineInstance{mesh=cube,transform=Matrix4x4.TRS(p,Quaternion.identity,s)});
            for(int i=0;i<24;i++)box(new Vector3(-5+(i+.5f)*10/24,-.125f,0),new Vector3(10/24f-.014f,.25f,3.5f));
            foreach(float side in new[]{-1.72f,1.72f})
            {
                box(new Vector3(0,-.32f,side*.7f),new Vector3(10,.25f,.22f));
                box(new Vector3(0,1.0f,side),new Vector3(10.3f,.16f,.18f));
                box(new Vector3(0,.48f,side),new Vector3(10.1f,.11f,.11f));
                for(int i=0;i<5;i++)box(new Vector3(-5+i*2.5f,.48f,side),new Vector3(.2f,1.3f,.2f));
            }
            var mesh=new Mesh{name="KH_SmallBridge"};mesh.CombineMeshes(combines.ToArray(),true,true);mesh.RecalculateBounds();UnityEngine.Object.DestroyImmediate(cube);
            AssetDatabase.CreateAsset(mesh,Dir+"/KH_SmallBridge.asset");b.AddComponent<MeshFilter>().sharedMesh=mesh;
            b.AddComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(KH_TerrainPrototypeConfig.ProtoMaterialDir+"/KH_Proto_Wood.mat");
            var deck=b.AddComponent<BoxCollider>();deck.center=new Vector3(0,-.125f,0);deck.size=new Vector3(10,.25f,3.5f);
            foreach(float side in new[]{-1.72f,1.72f}){var rail=b.AddComponent<BoxCollider>();rail.center=new Vector3(0,.55f,side);rail.size=new Vector3(10.2f,1.1f,.2f);}
            b.layer=LayerMask.NameToLayer("Environment");
        }
        static void AddVegetation(Transform root)
        {
            var trees=Child("RiverTrees",root);int index=0;
            foreach(var p in new[]{new Vector2(29,48),new Vector2(88,115)})
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(KH_TerrainPrototypeConfig.TreePrefabPath);
                var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root.gameObject.scene);go.name="RiverTree_"+index++;go.transform.SetParent(trees.transform,false);go.transform.localPosition=new Vector3(p.x,Height(p.x,p.y),p.y);
                foreach(var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats=r.sharedMaterials;
                    for(int m=0;m<mats.Length;m++){var autumn=AssetDatabase.LoadAssetAtPath<Material>(KH_TerrainPrototypeConfig.AutumnMaterialDir+"/"+mats[m].name+"_Autumn.mat");if(autumn!=null)mats[m]=autumn;}
                    r.sharedMaterials=mats;
                }
            }
        }
        [MenuItem("Tools/KominkaHillside/Course/4 - Verify River Representative")]
        public static void Verify()
        {
            if(SceneManager.GetActiveScene().path!=ScenePath)throw new InvalidOperationException("Open representative scene");
            var root=GameObject.Find("KH_RiverRepresentative");if(root==null)throw new InvalidOperationException("No representative root");
            var errors=new List<string>();var water=root.transform.Find("WaterChunks");
            if(water==null||water.childCount!=6)errors.Add("Expected six water chunks");
            int tris=0,renderers=0;
            foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true)){renderers++;if(r.forceRenderingOff)errors.Add("Renderer left hidden "+r.name);foreach(var mat in r.sharedMaterials)if(mat==null)errors.Add("Missing material "+r.name);}
            foreach(var f in root.GetComponentsInChildren<MeshFilter>(true)){if(f.sharedMesh==null)errors.Add("Missing mesh "+f.name);else for(int s=0;s<f.sharedMesh.subMeshCount;s++)tris+=(int)f.sharedMesh.GetIndexCount(s)/3;}
            var bridge=root.transform.Find("SmallBridge");if(bridge==null||bridge.GetComponents<BoxCollider>().Length!=3)errors.Add("Bridge deck/rails colliders");
            var terrain=root.GetComponentInChildren<Terrain>();if(terrain==null||terrain.terrainData.terrainLayers.Length!=4)errors.Add("Terrain layers");
            else if(terrain.terrainData.GetAlphamaps(145,100,1,1)[0,0,2]<.9f)errors.Add("East-bank sand path was not persisted");
            if(terrain!=null&&!terrain.drawHeightmap)errors.Add("Terrain left hidden");
            var shader=Shader.Find("KominkaHillside/River Lite");if(shader==null||ShaderUtil.ShaderHasError(shader))errors.Add("River shader compilation");
            var pipeline=UnityEngine.Object.FindObjectOfType<VRC.Core.PipelineManager>();
            if(pipeline!=null&&!string.IsNullOrEmpty(pipeline.blueprintId))errors.Add("Representative still has a world upload ID");
            string result="status="+(errors.Count==0?"STRUCTURE_OK":"FAIL")+" meshTrisExcludingTerrain="+tris+" meshRenderers="+renderers+" errors="+string.Join(";",errors)+"\nNot device/visual/navigation acceptance.";
            Directory.CreateDirectory("Temp/des-env-002");File.WriteAllText("Temp/des-env-002/representative-verify.txt",result);Debug.Log(result);
        }
    }
}
