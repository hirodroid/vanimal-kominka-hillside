using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

namespace KominkaHillside.Editor
{
    public static partial class KH_RoadsideSceneryEditor
    {
        const string ValleyRoot="KH_RiverVerge";
        const string ValleyDir=Dir+"/RiverVerge";
        const string ValleyEvidence="Temp/scenery-20261004/valley";
        const string ValleyImages="docs/plans/evidence/2026-10-04-river-verge";
        static readonly Vector3[] CanopyPoints={new Vector3(459,0,337),new Vector3(495,0,318),new Vector3(514,0,290),new Vector3(491,0,260)};
        [Serializable] class ValleyState { public string utc,sceneHash; public List<TileState> tiles=new List<TileState>(); public List<string> errors=new List<string>(); public int paintedPixels,changedOutside,trees,shrubs,stones,grassChunks,renderers,triangles,obstacleSamples,accessPaths; }
        [Serializable] class TileState {public string path,heightHash; public Vector3 origin;public int resolution;}
        static string Digest(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");}
        static byte[] Floats(Array values){var bytes=new byte[values.Length*4];Buffer.BlockCopy(values,0,bytes,0,bytes.Length);return bytes;}
        static string HeightHash(TerrainData td)=>Digest(Floats(td.GetHeights(0,0,td.heightmapResolution,td.heightmapResolution)));
        static float ValleyMask(float x,float z)
        {
            float river=KH_CourseGroundEditor.RiverAt(z).x;
            return KH_CourseGroundEditor.Smooth(208,232,z)*(1-KH_CourseGroundEditor.Smooth(352,375,z))*KH_CourseGroundEditor.Smooth(river-20,river-7,x)*(1-KH_CourseGroundEditor.Smooth(river+54,river+77,x));
        }
        static bool OpenBank(float z)=>Mathf.Abs(z-305)<5||Mathf.Abs(z-282)<7;
        static bool BankOpening(Vector3 p,float radius)=>
            (Mathf.Abs(p.z-305)<5+radius||Mathf.Abs(p.z-282)<7+radius)&&p.x-radius<KH_CourseGroundEditor.RiverAt(p.z).x+28;
        // Also called by the existing terrain-paint entry point once this authored region exists.
        public static void PaintValley(TerrainData td,float ox,float oz,float[,,] map)
        {
            int h=map.GetLength(0),w=map.GetLength(1);
            for(int z=0;z<h;z++)for(int x=0;x<w;x++)
            {
                float px=ox+td.size.x*x/(w-1),pz=oz+td.size.z*z/(h-1),mask=ValleyMask(px,pz);if(mask<=0)continue;
                float ignored,d=KH_CourseGroundEditor.RouteDistance(px,pz,out ignored),noise=Mathf.PerlinNoise(px*.19f+7,pz*.19f+4);
                float path=1-KH_CourseGroundEditor.Smooth(.85f,2.3f,d+(noise-.5f)*.5f),leaf=.015f;
                foreach(var tree in CanopyPoints){float dist=Vector2.Distance(new Vector2(px,pz),new Vector2(tree.x,tree.z));leaf=Mathf.Max(leaf,(1-KH_CourseGroundEditor.Smooth(3,7.8f,dist+(noise-.5f)*2))*.90f);}
                float bank=Mathf.Abs(px-KH_CourseGroundEditor.RiverAt(pz).x)-KH_CourseGroundEditor.HalfWidth(pz);
                float sand=Mathf.Max(path*.94f,(1-KH_CourseGroundEditor.Smooth(.1f,2.2f,bank))*.74f);
                float rock=KH_CourseGroundEditor.Smooth(30,52,td.GetSteepness(x/(float)(w-1),z/(float)(h-1)))*.6f;
                float[] goal={(1-sand)*(1-rock)*(1-leaf),(1-sand)*(1-rock)*leaf,sand,(1-sand)*rock};
                for(int l=0;l<4;l++)map[z,x,l]=Mathf.Lerp(map[z,x,l],goal[l],mask);
            }
        }
        [MenuItem("Tools/KominkaHillside/Course/148 - Capture River Verge Baseline")]
        public static void ValleyBaseline()
        {
            Init();Directory.CreateDirectory(ValleyEvidence);Directory.CreateDirectory(ValleyImages);
            if(File.Exists(ValleyEvidence+"/baseline.json"))throw new InvalidOperationException("Baseline already exists; preserve it");
            var report=new ValleyState{utc=DateTime.UtcNow.ToString("o"),sceneHash=Digest(File.ReadAllBytes(KH_CourseGroundEditor.ScenePath))};
            File.Copy(KH_CourseGroundEditor.ScenePath,ValleyEvidence+"/before.unity");
            foreach(var t in terrains)
            {
                var td=t.terrainData;string p=AssetDatabase.GetAssetPath(td);int n=td.alphamapResolution;
                report.tiles.Add(new TileState{path=p,origin=t.transform.position,resolution=n,heightHash=HeightHash(td)});
                File.Copy(p,ValleyEvidence+"/"+Path.GetFileName(p));File.WriteAllBytes(ValleyEvidence+"/"+td.name+".alphas",Floats(td.GetAlphamaps(0,0,n,n)));
            }
            File.WriteAllText(ValleyEvidence+"/baseline.json",JsonUtility.ToJson(report,true));CaptureValley("before");
            Debug.Log("[RiverVerge] Baseline saved: 20 terrains, scene and fixed views");
        }
        static Mesh ImportVerge(string name)
        {
            string path="Assets/KominkaHillside/Models/Environment/RiverVerge/"+name+".fbx";
            var imp=(ModelImporter)AssetImporter.GetAtPath(path);if(imp==null)throw new InvalidOperationException("Missing "+path);
            imp.importAnimation=false;imp.materialImportMode=ModelImporterMaterialImportMode.None;imp.globalScale=1;imp.bakeAxisConversion=true;imp.isReadable=true;imp.SaveAndReimport();
            var tmp=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try{var mesh=new Mesh{name=name};mesh.CombineMeshes(tmp.GetComponentsInChildren<MeshFilter>().Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=f.transform.localToWorldMatrix}).ToArray(),true,true);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,ValleyDir+"/"+name+".asset");return mesh;}
            finally{UnityEngine.Object.DestroyImmediate(tmp);}
        }
        static GameObject ValleyVisual(Transform parent,string name,Mesh mesh,Material mat,Vector3 p,Quaternion rotation,float scale,float cull)
        {
            var g=Child(parent,name);g.transform.SetPositionAndRotation(p,rotation);g.transform.localScale=Vector3.one*scale;
            g.AddComponent<MeshFilter>().sharedMesh=mesh;var r=g.AddComponent<MeshRenderer>();r.sharedMaterial=mat;RenderSettings(g);Lod(g,new Renderer[]{r},null,0,cull);return g;
        }
        static void BoxObstacle(GameObject g)
        {
            var c=g.AddComponent<BoxCollider>();var b=g.GetComponent<MeshFilter>().sharedMesh.bounds;c.center=b.center;c.size=b.size;
            var v=g.AddComponent<NavMeshModifierVolume>();v.center=b.center;v.size=b.size+new Vector3(.7f,2,.7f)/g.transform.lossyScale.x;v.area=NavMesh.GetAreaFromName("Not Walkable");
        }
        static bool ClearOfExisting(Vector3 p,float radius)
        {
            // Protect the wash station, bench and authored props, including this pass's additions.
            foreach(var c in UnityEngine.Object.FindObjectsOfType<Collider>())
            {
                if(c is TerrainCollider||c.isTrigger)continue;var b=c.bounds;
                if(b.size.x>20||b.size.z>20)continue;
                var closest=b.ClosestPoint(new Vector3(p.x,b.center.y,p.z));if(Vector2.Distance(new Vector2(p.x,p.z),new Vector2(closest.x,closest.z))<radius)return false;
            }
            return true;
        }
        [MenuItem("Tools/KominkaHillside/Course/149 - Build River Verge Study")]
        public static void BuildValley()
        {
            Init();if(GameObject.Find(ValleyRoot)!=null){Debug.Log("[RiverVerge] Authored layout preserved");return;}
            var baseline=JsonUtility.FromJson<ValleyState>(File.ReadAllText(ValleyEvidence+"/baseline.json"));
            if(Digest(File.ReadAllBytes(KH_CourseGroundEditor.ScenePath))!=baseline.sceneHash)throw new InvalidOperationException("Scene changed since baseline");
            foreach(var t in terrains)if(HeightHash(t.terrainData)!=baseline.tiles.First(v=>v.path==AssetDatabase.GetAssetPath(t.terrainData)).heightHash||t.terrainData.terrainLayers.Length!=4)throw new InvalidOperationException("Terrain baseline changed");
            if(AssetDatabase.IsValidFolder(ValleyDir))throw new InvalidOperationException("Output exists; preserve assets");
            AssetDatabase.CreateFolder(Dir,"RiverVerge");var root=new GameObject(ValleyRoot);
            var originals=terrains.ToDictionary(t=>t,t=>t.terrainData.GetAlphamaps(0,0,t.terrainData.alphamapResolution,t.terrainData.alphamapResolution));
            try
            {
                var stones=Enumerable.Range(0,3).Select(i=>ImportVerge("KH_RiverStone_"+i)).ToArray();var grasses=Enumerable.Range(0,2).Select(i=>ImportVerge("KH_RiverGrass_"+i)).ToArray();
                var grassMat=ValleyGrassMaterial();
                var stoneMat=new Material(Shader.Find("KominkaHillside/Scenery Lite")){name="KH_RiverStone",enableInstancing=true};AssetDatabase.CreateAsset(stoneMat,ValleyDir+"/KH_RiverStone.mat");
                var treeSource=GameObject.Find("KH_CourseScenery/Trees/Tree_0");var shrubSource=GameObject.Find(RootName+"/Grove_00/Shrub_00_0");
                if(treeSource==null||shrubSource==null||grassMat==null)throw new InvalidOperationException("Owned vegetation sources missing");
                for(int i=0;i<CanopyPoints.Length;i++)
                {
                    var p=CanopyPoints[i];if(route._DistanceToTrail(p)<3.6f||BankOpening(p,.5f)||!ClearOfExisting(p,1))throw new InvalidOperationException("Tree location obstructed "+p);
                    var tree=Child(root.transform,"Canopy_"+i);CopyVisual(treeSource.transform.Find("Near").gameObject,tree.transform,"Near");CopyVisual(treeSource.transform.Find("Far").gameObject,tree.transform,"Far");
                    tree.transform.position=p;tree.transform.rotation=Quaternion.Euler(0,37+i*73,0);tree.transform.localScale=Vector3.one*((i==2?10.5f:9.2f)/BoundsOf(tree).size.y);
                    tree.transform.position+=Vector3.up*(Ground(p)-.1f-BoundsOf(tree).min.y);RenderSettings(tree);Lod(tree,tree.transform.Find("Near").GetComponentsInChildren<Renderer>(),tree.transform.Find("Far").GetComponentsInChildren<Renderer>(),.20f,.012f);Obstacle(tree,.5f/tree.transform.localScale.x,4/tree.transform.localScale.x);
                }
                var rng=new System.Random(202610041);int count=0;
                for(int node=280;node<=310;node+=3)
                {
                    Vector3 p=route._Position(node),forward=(route._Position(node+1)-p).normalized,side=new Vector3(forward.z,0,-forward.x)*(node<294?1:-1);
                    for(int k=0;k<3;k++)
                    {
                        Vector3 q=p+side*(3.4f+k*.85f)+forward*(k*1.7f-2);float h=.65f+k*.20f;
                        if(route._DistanceToTrail(q)<2.65f||BankOpening(q,1.2f)||!ClearOfExisting(q,1)||Mathf.Abs(Ground(q+Vector3.right)-Ground(q-Vector3.right))>1.2f)continue;
                        var s=CopyVisual(shrubSource.transform.Find("Visual").gameObject,root.transform,"Understory_"+count++);s.transform.localScale=Vector3.one;s.transform.SetPositionAndRotation(q,Quaternion.Euler(0,node*23+k*47,0));s.transform.localScale*=h/BoundsOf(s).size.y;s.transform.position+=Vector3.up*(Ground(q)-.06f-BoundsOf(s).min.y);
                        var sb=BoundsOf(s);float radius=Mathf.Max(sb.extents.x,sb.extents.z);
                        if(route._DistanceToTrail(sb.center)-radius*1.415f<1.8f||BankOpening(sb.center,radius)){UnityEngine.Object.DestroyImmediate(s);continue;}
                        RenderSettings(s);Lod(s,s.GetComponentsInChildren<Renderer>(),null,0,.018f);Obstacle(s,radius/s.transform.localScale.x,h/s.transform.localScale.x);
                    }
                }
                count=0;
                for(float z=235;z<350;z+=3.1f)
                {
                    if(OpenBank(z)||Mathf.Sin(z*.22f)<-.55f)continue;float edge=KH_CourseGroundEditor.RiverAt(z).x+KH_CourseGroundEditor.HalfWidth(z);
                    for(int k=0;k<2;k++)
                    {
                        Vector3 p=new Vector3(edge+1.0f+k*1.3f,0,z+k*.8f);float scale=.42f+(float)rng.NextDouble()*.58f;
                        if(route._DistanceToTrail(p)<3.3f||BankOpening(p,1.2f)||!ClearOfExisting(p,1.1f))continue;
                        var mesh=stones[count%3];var rotation=Quaternion.Euler(0,(float)rng.NextDouble()*360,0);float floor=float.MaxValue,ceiling=float.MinValue;
                        foreach(var vertex in mesh.vertices)if(vertex.y<mesh.bounds.min.y+.025f){var offset=rotation*(vertex*scale);float support=Ground(p+offset)-offset.y;floor=Mathf.Min(floor,support);ceiling=Mathf.Max(ceiling,support);}
                        if(ceiling-floor>.45f*scale)continue;p.y=floor-.035f;
                        var g=ValleyVisual(root.transform,"BankStone_"+count,mesh,stoneMat,p,rotation,scale,.012f);BoxObstacle(g);count++;
                    }
                }
                BuildValleyGrass(root.transform,grasses,grassMat,rng);
                foreach(var t in terrains)
                {
                    var td=t.terrainData;var map=(float[,,])originals[t].Clone();PaintValley(td,t.transform.position.x,t.transform.position.z,map);
                    if(!Floats(map).SequenceEqual(Floats(originals[t]))){td.SetAlphamaps(0,0,map);EditorUtility.SetDirty(td);t.Flush();}
                }
                AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);if(!EditorSceneManager.SaveScene(root.scene))throw new IOException("Scene save failed");
                Debug.Log("[RiverVerge] Authored ground and near vegetation saved; navigation refresh required");
            }
            catch
            {
                foreach(var kv in originals){kv.Key.terrainData.SetAlphamaps(0,0,kv.Value);EditorUtility.SetDirty(kv.Key.terrainData);kv.Key.Flush();}
                UnityEngine.Object.DestroyImmediate(root);AssetDatabase.SaveAssets();var scene=EditorSceneManager.GetActiveScene();EditorSceneManager.MarkSceneDirty(scene);
                if(EditorSceneManager.SaveScene(scene))AssetDatabase.DeleteAsset(ValleyDir);
                else Debug.LogError("[RiverVerge] Rollback scene save failed; generated assets and baseline retained");throw;
            }
        }
        static void BuildValleyGrass(Transform parent,Mesh[] grasses,Material mat,System.Random rng)
        {
            for(int chunk=0;chunk<26;chunk++)
            {
                float z=230+chunk*5;float river=KH_CourseGroundEditor.RiverAt(z).x;var origin=new Vector3(river,0,z);var combines=new List<CombineInstance>();
                for(int k=0;k<110;k++)
                {
                    float qz=z+(float)rng.NextDouble()*9-4.5f;Vector3 q;
                    if(k<42){if(OpenBank(qz))continue;q=new Vector3(KH_CourseGroundEditor.RiverAt(qz).x+KH_CourseGroundEditor.HalfWidth(qz)+2.0f+(float)rng.NextDouble()*3,0,qz);}
                    else
                    {
                        var near=route._ProjectToTrail(new Vector3(KH_CourseGroundEditor.RiverAt(qz).x+20,0,qz));var outward=new Vector3(near.x-KH_CourseGroundEditor.RiverAt(near.z).x,0,0).normalized;
                        q=near+outward*((k%2==0?-1:1)*(2.6f+(float)rng.NextDouble()*9))+Vector3.forward*((float)rng.NextDouble()*9-4.5f);
                    }
                    if(route._DistanceToTrail(q)<2.1f||BankOpening(q,.5f)||ValleyMask(q.x,q.z)<.8f||!ClearOfExisting(q,.45f))continue;
                    if(Mathf.PerlinNoise(q.x*.26f,q.z*.26f)<.43f)continue;
                    q.y=Ground(q)+.015f;float scale=.75f+(float)rng.NextDouble()*.6f;var rotation=Quaternion.Euler(0,(float)rng.NextDouble()*360,0);
                    // Each blade's roots follow the actual slope when the chunk is combined.
                    var src=grasses[k%2];var m=UnityEngine.Object.Instantiate(src);var vs=m.vertices;var matrix=Matrix4x4.TRS(q-origin,rotation,Vector3.one*scale);
                    for(int v=0;v<vs.Length;v++){var p=matrix.MultiplyPoint3x4(vs[v]);p.y=Ground(p+origin)+.01f+vs[v].y*scale;vs[v]=p;}m.vertices=vs;m.RecalculateNormals();
                    combines.Add(new CombineInstance{mesh=m,transform=Matrix4x4.identity});
                }
                if(combines.Count==0)continue;var mesh=new Mesh{name="KH_RiverVergeGrass_"+chunk};mesh.CombineMeshes(combines.ToArray(),true,true);foreach(var c in combines)UnityEngine.Object.DestroyImmediate(c.mesh);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,ValleyDir+"/"+mesh.name+".asset");
                ValleyVisual(parent,mesh.name,mesh,mat,origin,Quaternion.identity,1,.075f);
            }
        }
        static Material ValleyGrassMaterial()
        {
            string path=ValleyDir+"/KH_RiverGrass.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat!=null)return mat;
            mat=new Material(AssetDatabase.LoadAssetAtPath<Material>(KH_CourseGroundEditor.Dir+"/Scenery/KH_CourseGrass.mat")){name="KH_RiverGrass",enableInstancing=true};
            mat.SetColor("_Color",new Color(.55f,.62f,.48f,1));AssetDatabase.CreateAsset(mat,path);return mat;
        }
        [MenuItem("Tools/KominkaHillside/Course/154 - Finish River Verge Grass Palette")]
        public static void FinishValleyGrassPalette()
        {
            Init();var root=GameObject.Find(ValleyRoot);if(root==null)throw new InvalidOperationException("Verge required");var mat=ValleyGrassMaterial();
            foreach(var r in root.GetComponentsInChildren<Renderer>())if(r.name.StartsWith("KH_RiverVergeGrass")){r.sharedMaterial=mat;EditorUtility.SetDirty(r);}
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);if(!EditorSceneManager.SaveScene(root.scene))throw new IOException("Palette save failed");
        }
    }
}
