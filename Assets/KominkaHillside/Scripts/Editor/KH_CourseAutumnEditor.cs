using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace KominkaHillside.Editor
{
    public static class KH_CourseAutumnEditor
    {
        public const string Dir=KH_CourseGroundEditor.Dir+"/Autumn";
        public const string MeshPath=Dir+"/KH_BlenderBridge.asset";
        public const string MaterialPath=Dir+"/KH_BlenderBridge.mat";
        const string Textures="Assets/KominkaHillside/Textures/Environment/CourseAutumn";
        const string Fbx="Assets/KominkaHillside/Models/Environment/CourseBridge/KH_CourseBridge.fbx";
        const string Evidence="Temp/des-env-002/autumn-polish";
        [Serializable] class Report
        {
            public string utc,scene,sceneHash,geometryHash,navHash,stage;
            public int terrainCount,layers,bridgeTriangles,groveTrees,groveTriangles,groveRenderers;
            public string[] images;public List<string> errors=new List<string>();
            public string limitation="Editor daytime mono images and saved-data checks; no device or multiplayer performance claim.";
        }
        static string Hash(string path){using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
        static Terrain[] Tiles()=>UnityEngine.Object.FindObjectsOfType<Terrain>().Where(t=>AssetDatabase.GetAssetPath(t.terrainData).StartsWith(KH_CourseGroundEditor.Dir+"/KH_CourseTerrain_")).OrderBy(t=>t.name).ToArray();
        static string Heights(Terrain[] tiles)
        {
            using(var m=new MemoryStream())using(var w=new BinaryWriter(m))
            {foreach(var t in tiles){w.Write(t.name);var h=t.terrainData.GetHeights(0,0,t.terrainData.heightmapResolution,t.terrainData.heightmapResolution);foreach(float f in h)w.Write(f);}w.Flush();using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(m.ToArray())).Replace("-","");}
        }
        [MenuItem("Tools/KominkaHillside/Course/67 - Capture Autumn Baseline")]
        public static void Baseline()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);Directory.CreateDirectory(Evidence);
            if(File.Exists(Evidence+"/before.json"))throw new InvalidOperationException("Baseline exists; preserve it");
            var backup=Evidence+"/backup";Directory.CreateDirectory(backup);
            File.Copy(KH_CourseGroundEditor.ScenePath,backup+"/scene.unity",false);
            foreach(var t in Tiles()){string p=AssetDatabase.GetAssetPath(t.terrainData);File.Copy(p,backup+"/"+Path.GetFileName(p),false);}
            Record("before");
        }
        static TerrainLayer[] Layers()=>Enumerable.Range(0,4).Select(i=>AssetDatabase.LoadAssetAtPath<TerrainLayer>(Dir+"/Layer"+i+".terrainlayer")).ToArray();
        public static TerrainLayer[] AdoptedLayersOr(TerrainLayer[] fallback){var ls=Layers();return ls.All(l=>l!=null)?ls:fallback;}
        static float Smooth(float a,float b,float v){float t=Mathf.Clamp01((v-a)/(b-a));return t*t*(3-2*t);}
        // The saved layer reference is the opt-in; other scenes keep their authored palette.
        public static bool PaintIfAdopted(TerrainData td,int tx,int tz)
        {
            if(td.terrainLayers.Length!=4||AssetDatabase.GetAssetPath(td.terrainLayers[0])!=Dir+"/Layer0.terrainlayer")return false;
            Paint(td,tx,tz);return true;
        }
        static void Paint(TerrainData td,int tx,int tz)
        {
            int n=td.alphamapResolution;var map=new float[n,n,4];
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {
                float px=tx*KH_CourseGroundEditor.TileX+x*KH_CourseGroundEditor.TileX/(n-1),pz=tz*KH_CourseGroundEditor.TileZ+z*KH_CourseGroundEditor.TileZ/(n-1),road;
                float d=KH_CourseGroundEditor.RouteDistance(px,pz,out road);
                float fine=Mathf.PerlinNoise(px*.29f+13,pz*.29f+9),broad=Mathf.PerlinNoise(px*.035f,pz*.035f);
                float width=1.2f+.95f*Mathf.PerlinNoise(px*.071f+3,pz*.071f+7);
                float path=1-Smooth(width*.35f,width+.7f,d+(fine-.5f)*.75f);
                float bank=1-Smooth(KH_CourseGroundEditor.HalfWidth(pz)+.6f,KH_CourseGroundEditor.HalfWidth(pz)+3.8f,Mathf.Abs(px-KH_CourseGroundEditor.RiverAt(pz).x));
                float plaza=1-Smooth(7.5f,14,Vector2.Distance(new Vector2(px,pz),new Vector2(215,605)));
                float sand=Mathf.Max(path*(.48f+.16f*fine),bank*.75f);sand=Mathf.Max(sand,plaza*.30f);
                float rock=Smooth(26,46,td.GetSteepness(x/(float)(n-1),z/(float)(n-1)));
                float leaf=Mathf.Clamp01(.08f+broad*.53f+path*.30f+plaza*.13f);
                map[z,x,0]=(1-sand)*(1-rock)*(1-leaf);map[z,x,1]=(1-sand)*(1-rock)*leaf;map[z,x,2]=sand;map[z,x,3]=(1-sand)*rock;
            }
            if(GameObject.Find("KH_RiverVerge")!=null&&UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path==KH_CourseGroundEditor.ScenePath)
                KH_RoadsideSceneryEditor.PaintValley(td,tx*KH_CourseGroundEditor.TileX,tz*KH_CourseGroundEditor.TileZ,map);
            td.SetAlphamaps(0,0,map);EditorUtility.SetDirty(td);
        }
        [MenuItem("Tools/KominkaHillside/Course/68 - Apply Autumn Ground and Blender Bridge")]
        public static void Apply()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            if(AssetDatabase.IsValidFolder(Dir))throw new InvalidOperationException("Autumn output already exists; preserve it");
            if(AssetDatabase.LoadAssetAtPath<GameObject>(Fbx)==null)throw new InvalidOperationException("FBX missing");
            foreach(float z in new[]{190f,490f}){var b=GameObject.Find("KH_CourseExtension/Bridge_"+z);if(b==null||b.GetComponents<BoxCollider>().Length!=3)throw new InvalidOperationException("Bridge preflight");}
            if(GameObject.Find("KH_AutumnGroves")!=null||AssetDatabase.LoadAssetAtPath<Material>(KH_CourseGroundEditor.Dir+"/Scenery/KH_CourseTreeBoards.mat")==null)throw new InvalidOperationException("Grove preflight");
            foreach(var p in KH_TerrainPrototypeConfig.BoardTreePrefabs)if(AssetDatabase.LoadAssetAtPath<GameObject>(p)==null)throw new InvalidOperationException("Missing board prefab");
            var paths=new List<string>{KH_CourseGroundEditor.ScenePath,Fbx+".meta"};paths.AddRange(Tiles().Select(t=>AssetDatabase.GetAssetPath(t.terrainData)));
            foreach(string name in new[]{"LeafSoil","DryGrass","BridgeWood"})paths.Add(Textures+"/"+name+".png.meta");
            var margin=GameObject.Find("KH_CourseExtension/LandscapeMargin");if(margin!=null)paths.Add(AssetDatabase.GetAssetPath(margin.GetComponent<MeshRenderer>().sharedMaterial));
            var originals=paths.Distinct().ToDictionary(p=>p,p=>File.ReadAllBytes(p));
            foreach(var kv in originals){var p=Evidence+"/apply-inputs/"+kv.Key;Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllBytes(p,kv.Value);}
            try{ApplyCore();}
            catch
            {
                // Restore the exact baseline, including saved assets and importer settings.
                foreach(var kv in originals)File.WriteAllBytes(kv.Key,kv.Value);
                if(AssetDatabase.IsValidFolder(Dir))AssetDatabase.DeleteAsset(Dir);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
                foreach(var p in originals.Keys.Where(p=>!p.EndsWith(".meta")))AssetDatabase.ImportAsset(p,ImportAssetOptions.ForceUpdate);
                EditorSceneManager.OpenScene(KH_CourseGroundEditor.ScenePath);
                throw;
            }
        }
        static void ApplyCore()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            if(!File.Exists(Evidence+"/before.json")||File.Exists(Evidence+"/after.json"))throw new InvalidOperationException("Baseline required; apply is once per reviewed revision");
            var before=JsonUtility.FromJson<Report>(File.ReadAllText(Evidence+"/before.json"));var tiles=Tiles();
            if(tiles.Length!=20||Heights(tiles)!=before.geometryHash||Hash(KH_CourseGroundEditor.ScenePath)!=before.sceneHash)throw new InvalidOperationException("Baseline changed");
            Directory.CreateDirectory(Dir);AssetDatabase.Refresh();
            foreach(string name in new[]{"LeafSoil","DryGrass","BridgeWood"})
            {
                string p=Textures+"/"+name+".png";var ti=(TextureImporter)AssetImporter.GetAtPath(p);if(ti==null)throw new InvalidOperationException(p);
                ti.textureType=TextureImporterType.Default;ti.sRGBTexture=true;ti.mipmapEnabled=true;ti.isReadable=false;ti.alphaSource=TextureImporterAlphaSource.None;ti.wrapMode=TextureWrapMode.Repeat;ti.npotScale=TextureImporterNPOTScale.ToNearest;ti.maxTextureSize=1024;ti.anisoLevel=4;ti.textureCompression=TextureImporterCompression.Compressed;
                foreach(string platform in new[]{"Android","iPhone"})ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name=platform,overridden=true,maxTextureSize=1024,format=TextureImporterFormat.ASTC_6x6});ti.SaveAndReimport();
            }
            var original=AssetDatabase.LoadAssetAtPath<TerrainData>(KH_TerrainPrototypeConfig.TerrainDataPath);
            for(int i=0;i<4;i++)
            {
                var l=UnityEngine.Object.Instantiate(original.terrainLayers[i]);l.name="KH_CourseAutumn_"+i;
                if(i<2){l.diffuseTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Textures+(i==0?"/DryGrass.png":"/LeafSoil.png"));l.normalMapTexture=null;l.maskMapTexture=null;l.tileSize=Vector2.one*(i==0?4.3f:3.7f);l.diffuseRemapMin=Vector4.zero;l.diffuseRemapMax=Vector4.one;l.smoothness=0;l.metallic=0;}
                AssetDatabase.CreateAsset(l,Dir+"/Layer"+i+".terrainlayer");
            }
            var layers=Layers();foreach(var t in tiles){t.terrainData.terrainLayers=layers;Paint(t.terrainData,Mathf.RoundToInt(t.transform.position.x/KH_CourseGroundEditor.TileX),Mathf.RoundToInt(t.transform.position.z/KH_CourseGroundEditor.TileZ));t.Flush();}
            var importer=(ModelImporter)AssetImporter.GetAtPath(Fbx);importer.importAnimation=false;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.globalScale=1;importer.bakeAxisConversion=true;importer.generateSecondaryUV=true;importer.SaveAndReimport();
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);var temp=(GameObject)PrefabUtility.InstantiatePrefab(model);Mesh mesh;
            try{var parts=temp.GetComponentsInChildren<MeshFilter>();var combines=parts.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=f.transform.localToWorldMatrix}).ToArray();mesh=new Mesh{name="KH_BlenderBridge"};mesh.CombineMeshes(combines,true,true);mesh.RecalculateBounds();}
            finally{UnityEngine.Object.DestroyImmediate(temp);}
            if(Mathf.Abs(mesh.bounds.size.x-10.28f)>.03f||Mathf.Abs(mesh.bounds.size.y-1.78f)>.03f||Mathf.Abs(mesh.bounds.size.z-3.71f)>.03f)throw new InvalidOperationException("FBX axis/scale mismatch "+mesh.bounds);
            AssetDatabase.CreateAsset(mesh,MeshPath);var mat=new Material(Shader.Find("KominkaHillside/Scenery Lite")){name="KH_BlenderBridge",enableInstancing=true};mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Textures+"/BridgeWood.png"));mat.SetFloat("_VertexTint",0);AssetDatabase.CreateAsset(mat,MaterialPath);
            foreach(float z in new[]{190f,490f}){var b=GameObject.Find("KH_CourseExtension/Bridge_"+z);if(b==null||b.GetComponents<BoxCollider>().Length!=3)throw new InvalidOperationException("Bridge missing");b.GetComponent<MeshFilter>().sharedMesh=mesh;b.GetComponent<MeshRenderer>().sharedMaterial=mat;}
            var margin=GameObject.Find("KH_CourseExtension/LandscapeMargin");if(margin!=null){var m=margin.GetComponent<MeshRenderer>().sharedMaterial;m.SetTexture("_MainTex",layers[0].diffuseTexture);EditorUtility.SetDirty(m);}
            BuildGroves(tiles);
            if(Heights(tiles)!=before.geometryHash)throw new InvalidOperationException("Unexpected height edit");
            AssetDatabase.SaveAssets();var scene=EditorSceneManager.GetActiveScene();EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new IOException("Save failed");
            Record("after");
        }
        static float Ground(Terrain[] ts,float x,float z)
        {foreach(var t in ts){var p=t.transform.position;var s=t.terrainData.size;if(x>=p.x&&x<=p.x+s.x&&z>=p.z&&z<=p.z+s.z)return p.y+t.terrainData.GetInterpolatedHeight((x-p.x)/s.x,(z-p.z)/s.z);}throw new InvalidOperationException("No ground");}
        static void BuildGroves(Terrain[] ts)
        {
            if(GameObject.Find("KH_AutumnGroves")!=null)throw new InvalidOperationException("Groves already exist");
            var root=new GameObject("KH_AutumnGroves");var route=UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
            var material=AssetDatabase.LoadAssetAtPath<Material>(KH_CourseGroundEditor.Dir+"/Scenery/KH_CourseTreeBoards.mat");if(material==null)throw new InvalidOperationException("Board material missing");
            int count=0;foreach(int anchor in new[]{85,170,275})
            {
                var a=route._Position(anchor);var tangent=(route._Position(anchor+1)-a).normalized;var side=new Vector3(-tangent.z,0,tangent.x);
                for(int i=0;i<5;i++)
                {
                    Vector3 p=a+side*(19+(i%2)*7)+tangent*((i-2)*7);float unused;
                    if(p.x<5||p.z<185||p.x>595||p.z>710||KH_CourseGroundEditor.RouteDistance(p.x,p.z,out unused)<14||Mathf.Abs(p.x-KH_CourseGroundEditor.RiverAt(p.z).x)<KH_CourseGroundEditor.HalfWidth(p.z)+10)continue;
                    var holder=new GameObject("GroveTree_"+count++);holder.transform.SetParent(root.transform);holder.transform.position=new Vector3(p.x,Ground(ts,p.x,p.z)-.18f,p.z);
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(KH_TerrainPrototypeConfig.BoardTreePrefabs[i%3]);var renderers=new List<Renderer>();
                    for(int k=0;k<2;k++)
                    {
                        var q=(GameObject)PrefabUtility.InstantiatePrefab(prefab);q.transform.SetParent(holder.transform,false);q.transform.localRotation=Quaternion.Euler(0,25+i*17+k*90,0);
                        var rs=q.GetComponentsInChildren<MeshRenderer>();var bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);
                        float height=8+(i%3)*1.2f;q.transform.localScale*=height/bounds.size.y;
                        bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);q.transform.position+=Vector3.up*(holder.transform.position.y-bounds.min.y);
                        foreach(var r in rs){r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;GameObjectUtility.SetStaticEditorFlags(r.gameObject,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);PrefabUtility.RecordPrefabInstancePropertyModifications(r);PrefabUtility.RecordPrefabInstancePropertyModifications(r.gameObject);renderers.Add(r);}
                        foreach(var c in q.GetComponentsInChildren<Collider>())UnityEngine.Object.DestroyImmediate(c);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(q.transform);
                    }
                    var lod=holder.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.018f,renderers.ToArray())});lod.RecalculateBounds();
                }
            }
        }
        [MenuItem("Tools/KominkaHillside/Course/69 - Verify Autumn Ground and Bridge")]
        public static void Verify(){KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);Record("verified");}
        [MenuItem("Tools/KominkaHillside/Course/70 - Refine Autumn Path Paint")]
        public static void RefinePaint()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);var tiles=Tiles();var height=Heights(tiles);
            if(tiles.Length!=20||tiles.Any(t=>AssetDatabase.GetAssetPath(t.terrainData.terrainLayers[0])!=Dir+"/Layer0.terrainlayer"))throw new InvalidOperationException("Adopted autumn layers required");
            var old=tiles.ToDictionary(t=>t,t=>t.terrainData.GetAlphamaps(0,0,t.terrainData.alphamapResolution,t.terrainData.alphamapResolution));
            try{foreach(var t in tiles){Paint(t.terrainData,Mathf.RoundToInt(t.transform.position.x/KH_CourseGroundEditor.TileX),Mathf.RoundToInt(t.transform.position.z/KH_CourseGroundEditor.TileZ));t.Flush();}if(Heights(tiles)!=height)throw new InvalidOperationException("Height changed");AssetDatabase.SaveAssets();Record("refined");}
            catch{foreach(var kv in old){kv.Key.terrainData.SetAlphamaps(0,0,kv.Value);EditorUtility.SetDirty(kv.Key.terrainData);kv.Key.Flush();}AssetDatabase.SaveAssets();throw;}
        }
        static void Record(string stage)
        {
            var ts=Tiles();var report=new Report{utc=DateTime.UtcNow.ToString("o"),scene=KH_CourseGroundEditor.ScenePath,sceneHash=Hash(KH_CourseGroundEditor.ScenePath),geometryHash=Heights(ts),stage=stage,terrainCount=ts.Length,layers=ts.Select(t=>t.terrainData.terrainLayers.Length).Max()};
            var nav=AssetDatabase.FindAssets("t:NavMeshData",new[]{KH_CourseGroundEditor.Dir});report.navHash=string.Join(";",nav.Select(g=>Hash(AssetDatabase.GUIDToAssetPath(g))));
            var bridge=GameObject.Find("KH_CourseExtension/Bridge_190");report.bridgeTriangles=bridge.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3;
            var groves=GameObject.Find("KH_AutumnGroves");if(groves!=null){report.groveTrees=groves.transform.childCount;report.groveRenderers=groves.GetComponentsInChildren<Renderer>().Length;report.groveTriangles=groves.GetComponentsInChildren<MeshFilter>().Sum(m=>m.sharedMesh.triangles.Length/3);}
            if(stage!="before")
            {
                var baseline=JsonUtility.FromJson<Report>(File.ReadAllText(Evidence+"/before.json"));
                if(report.geometryHash!=baseline.geometryHash)report.errors.Add("Height changed");if(report.navHash!=baseline.navHash)report.errors.Add("Nav changed");
                if(report.layers!=4||report.terrainCount!=20)report.errors.Add("Terrain count/layers");if(report.bridgeTriangles>12000)report.errors.Add("Bridge budget");
                foreach(var t in ts)if(t.terrainData.terrainLayers[0]!=Layers()[0])report.errors.Add("Autumn layer missing");
                foreach(float z in new[]{190f,490f}){var b=GameObject.Find("KH_CourseExtension/Bridge_"+z);if(b.GetComponents<BoxCollider>().Length!=3||b.GetComponent<MeshFilter>().sharedMesh!=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath))report.errors.Add("Bridge references");}
                if(groves==null||groves.GetComponentsInChildren<Collider>().Length!=0)report.errors.Add("Groves missing or collidable");
                if(groves!=null)
                {
                    var mat=AssetDatabase.LoadAssetAtPath<Material>(KH_CourseGroundEditor.Dir+"/Scenery/KH_CourseTreeBoards.mat");
                    foreach(var r in groves.GetComponentsInChildren<MeshRenderer>())if(r.sharedMaterial!=mat||r.shadowCastingMode!=ShadowCastingMode.Off||r.receiveShadows)report.errors.Add("Grove renderer settings");
                    foreach(var l in groves.GetComponentsInChildren<LODGroup>())if(l.GetLODs().Length!=1||Mathf.Abs(l.GetLODs()[0].screenRelativeTransitionHeight-.018f)>.0001f||l.GetLODs()[0].renderers.Length!=2)report.errors.Add("Grove LOD");
                }
            }
            report.images=Capture(stage);File.WriteAllText(Evidence+"/"+stage+".json",JsonUtility.ToJson(report,true));
            if(report.errors.Count>0)throw new InvalidOperationException(string.Join(";",report.errors));Debug.Log("[KH Autumn] "+stage+" PASS bridge="+report.bridgeTriangles+" groves="+report.groveTrees);
        }
        static string[] Capture(string stage)
        {
            Directory.CreateDirectory(Evidence);var paths=new List<string>();var route=UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
            var descriptor=UnityEngine.Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();var go=new GameObject("KH_AutumnCapture"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();cam.CopyFrom(descriptor.ReferenceCamera.GetComponent<Camera>());cam.enabled=false;cam.stereoTargetEye=StereoTargetEyeMask.None;cam.allowHDR=false;cam.allowMSAA=false;cam.aspect=16f/9;cam.fieldOfView=65;cam.useOcclusionCulling=false;
            var rt=new RenderTexture(1024,576,24);var tex=new Texture2D(1024,576,TextureFormat.RGB24,false);var prev=RenderTexture.active;
            try
            {
                cam.targetTexture=rt;
                for(int i=0;i<8;i++)
                {
                    if(i==0){var b=GameObject.Find("KH_CourseExtension/Bridge_190").transform.position;cam.transform.position=b+new Vector3(-11,2.6f,-8);cam.transform.LookAt(b+Vector3.up*.4f);}
                    else if(i<5){int n=new[]{0,85,170,275,350}[i];cam.transform.position=route._Position(n)+Vector3.up*1.65f;cam.transform.LookAt(route._Position(n+4)+Vector3.up*1.05f);}
                    else{int n=new[]{85,170,275}[i-5];cam.transform.position=route._Position(n)+Vector3.up*1.65f;var grove=GameObject.Find("KH_AutumnGroves/GroveTree_"+((i-5)*5));var target=grove==null?route._Position(n)+Vector3.right*25:grove.transform.position;cam.transform.LookAt(target+Vector3.up*2);}
                    cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1024,576),0,0);tex.Apply();string p=Evidence+"/"+stage+"-"+i+".png";File.WriteAllBytes(p,tex.EncodeToPNG());paths.Add(p);
                }
            }
            finally{RenderTexture.active=prev;UnityEngine.Object.DestroyImmediate(go);rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);}
            return paths.ToArray();
        }
    }
}
