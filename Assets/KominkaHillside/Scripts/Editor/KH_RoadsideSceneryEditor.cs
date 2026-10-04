using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.AI.Navigation;

namespace KominkaHillside.Editor
{
    // Additive authoring: existing terrain, furniture and vegetation are not regenerated.
    public static partial class KH_RoadsideSceneryEditor
    {
        public const string RootName="KH_RoadsideAutumn";
        const string Dir=KH_CourseGroundEditor.Dir+"/Roadside";
        const string Evidence="Temp/scenery-20261004";
        static readonly Vector2[] Centers={
            new Vector2(130,68),new Vector2(177,89),new Vector2(231,98),
            new Vector2(209,169),new Vector2(268,183),new Vector2(317,207),new Vector2(352,131),
            new Vector2(453,173),new Vector2(474,228),new Vector2(528,294),
            new Vector2(481,351),new Vector2(419,394),new Vector2(433,445),
            new Vector2(380,512),new Vector2(419,565),new Vector2(358,620),
            new Vector2(300,685),new Vector2(246,625),new Vector2(180,570),
            new Vector2(139,477),new Vector2(128,393),new Vector2(122,295),new Vector2(105,228)
        };
        [Serializable] public class Entry {public string name,kind; public Vector3 position;public float routeClearance;}
        [Serializable] public class Report {public string utc;public int groups,trees,boards,shrubs,renderers;public List<Entry> entries=new List<Entry>();public List<string> omitted=new List<string>();public List<string> errors=new List<string>();}
        static Terrain[] terrains;
        static KH_WalkRoute route;
        static float Ground(Vector3 p)
        {
            foreach(var t in terrains){var o=t.transform.position;var s=t.terrainData.size;
                if(p.x>=o.x&&p.x<=o.x+s.x&&p.z>=o.z&&p.z<=o.z+s.z)return o.y+t.terrainData.GetInterpolatedHeight((p.x-o.x)/s.x,(p.z-o.z)/s.z);}
            throw new InvalidOperationException("Outside terrain "+p);
        }
        static Bounds BoundsOf(GameObject g){var rs=g.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b;}
        static void Init(){KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);terrains=UnityEngine.Object.FindObjectsOfType<Terrain>();route=UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();if(terrains.Length!=20||route==null)throw new InvalidOperationException("Course required");}
        static GameObject Child(Transform parent,string name){var g=new GameObject(name);g.layer=11;g.transform.SetParent(parent,false);return g;}
        static void RenderSettings(GameObject go)
        {
            foreach(var t in go.GetComponentsInChildren<Transform>(true)){t.gameObject.layer=11;GameObjectUtility.SetStaticEditorFlags(t.gameObject,0);}
            foreach(var r in go.GetComponentsInChildren<Renderer>())
            {r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.allowOcclusionWhenDynamic=true;r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;}
        }
        // Copy only render data. No hidden behaviours, prefab overrides, old colliders or static flags.
        static GameObject CopyVisual(GameObject source,Transform parent,string name)
        {
            var g=Child(parent,name);g.transform.localPosition=source.transform.localPosition;g.transform.localRotation=source.transform.localRotation;g.transform.localScale=source.transform.localScale;
            var f=source.GetComponent<MeshFilter>();var r=source.GetComponent<MeshRenderer>();
            if(f!=null&&r!=null){g.AddComponent<MeshFilter>().sharedMesh=f.sharedMesh;g.AddComponent<MeshRenderer>().sharedMaterials=r.sharedMaterials;}
            foreach(Transform t in source.transform)CopyVisual(t.gameObject,g.transform,t.name);
            return g;
        }
        static void Lod(GameObject g,Renderer[] near,Renderer[] far,float transition,float cull)
        {var l=g.AddComponent<LODGroup>();l.fadeMode=LODFadeMode.None;l.SetLODs(far==null?new[]{new LOD(cull,near)}:new[]{new LOD(transition,near),new LOD(cull,far)});l.RecalculateBounds();}
        static void Obstacle(GameObject g,float radius,float height)
        {
            var c=g.AddComponent<CapsuleCollider>();c.radius=radius;c.height=Mathf.Max(height,radius*2);c.center=new Vector3(0,c.height*.5f,0);
            // Explicit volume excludes the trunk and low canopy; no runtime carving.
            var v=g.AddComponent<NavMeshModifierVolume>();v.center=c.center;
            v.size=new Vector3(radius*2+.6f/g.transform.lossyScale.x,c.height+1/g.transform.lossyScale.y,radius*2+.6f/g.transform.lossyScale.z);v.area=UnityEngine.AI.NavMesh.GetAreaFromName("Not Walkable");
        }
        static bool Place(GameObject g,Vector3 p,float height,string kind,Report report)
        {
            g.transform.position=p;var b=BoundsOf(g);g.transform.localScale*=height/b.size.y;
            b=BoundsOf(g);p.y=Ground(p)-.08f;g.transform.position+=Vector3.up*(p.y-b.min.y);
            b=BoundsOf(g);float radius=new Vector2(b.extents.x,b.extents.z).magnitude;
            float clearance=route._DistanceToTrail(b.center)-radius;
            var river=KH_CourseGroundEditor.RiverAt(p.z);
            bool protectedSpot=Vector2.Distance(new Vector2(p.x,p.z),new Vector2(215,605))<27 ||
                Vector2.Distance(new Vector2(p.x,p.z),new Vector2(415,190))<20 ||
                Vector2.Distance(new Vector2(p.x,p.z),new Vector2(415,490))<20;
            float slope=Mathf.Max(Mathf.Abs(Ground(p+Vector3.right)-Ground(p-Vector3.right)),Mathf.Abs(Ground(p+Vector3.forward)-Ground(p-Vector3.forward)));
            if(clearance<5 || protectedSpot || Mathf.Abs(p.x-river.x)<KH_CourseGroundEditor.HalfWidth(p.z)+radius+3 || slope>1.25f)
            {report.omitted.Add(g.name+" clearance="+clearance.ToString("F2")+" slope="+slope.ToString("F2"));UnityEngine.Object.DestroyImmediate(g);return false;}
            report.entries.Add(new Entry{name=g.name,kind=kind,position=g.transform.position,routeClearance=clearance});return true;
        }
        static void Tree(GameObject source,Transform parent,Vector3 p,float height,float yaw,Report report,string name)
        {
            var g=Child(parent,name);CopyVisual(source.transform.Find("Near").gameObject,g.transform,"Near");CopyVisual(source.transform.Find("Far").gameObject,g.transform,"Far");
            g.transform.rotation=Quaternion.Euler(0,yaw,0);
            if(!Place(g,p,height,"tree",report))return;
            RenderSettings(g);Lod(g,g.transform.Find("Near").GetComponentsInChildren<Renderer>(),g.transform.Find("Far").GetComponentsInChildren<Renderer>(),.24f,.012f);
            Obstacle(g,.65f,4);report.trees++;
        }
        static void Board(GameObject source,Transform parent,Vector3 p,float height,float yaw,Material material,Report report,string name)
        {
            var g=Child(parent,name);
            for(int i=0;i<2;i++){var q=CopyVisual(source,g.transform,"Plane_"+i);q.transform.localPosition=Vector3.zero;q.transform.localRotation=Quaternion.Euler(0,yaw+i*90,0);}
            if(!Place(g,p,height,"board",report))return;
            foreach(var r in g.GetComponentsInChildren<Renderer>())r.sharedMaterial=material;
            RenderSettings(g);Lod(g,g.GetComponentsInChildren<Renderer>(),null,0,.018f);Obstacle(g,.45f,3);report.boards++;
        }
        static void Shrub(GameObject source,Transform parent,Vector3 p,float height,float yaw,Material leaf,Material bark,Report report,string name)
        {
            var g=Child(parent,name);var q=CopyVisual(source,g.transform,"Visual");q.transform.localPosition=Vector3.zero;q.transform.localRotation=Quaternion.Euler(0,yaw,0);q.transform.localScale=Vector3.one;
            if(!Place(g,p,height,"shrub",report))return;
            foreach(var r in g.GetComponentsInChildren<Renderer>())r.sharedMaterials=r.sharedMaterials.Select(m=>m.name.Contains("Leaves")?leaf:bark).ToArray();
            RenderSettings(g);Lod(g,g.GetComponentsInChildren<Renderer>(),null,0,.035f);
            var b=BoundsOf(g);Obstacle(g,Mathf.Max(b.extents.x,b.extents.z)/g.transform.localScale.x,.8f/g.transform.localScale.y);report.shrubs++;
        }
        static Material ShrubMaterial(GameObject shrub,string part,string shader,string filename)
        {
            string path=Dir+"/"+filename+".mat";var existing=AssetDatabase.LoadAssetAtPath<Material>(path);if(existing!=null)return existing;
            var src=shrub.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).First(m=>m.name.Contains(part));
            var m=new Material(Shader.Find(shader)){name=filename,enableInstancing=true};
            m.SetTexture("_MainTex",src.GetTexture("_AlbedoTex"));m.SetColor("_Color",part=="Leaves"?new Color(.85f,.66f,.38f,1):Color.white);
            if(m.HasProperty("_VertexTint"))m.SetFloat("_VertexTint",0);
            AssetDatabase.CreateAsset(m,path);return m;
        }
        [MenuItem("Tools/KominkaHillside/Course/141 - Add Roadside Autumn Groves")]
        public static void Apply()
        {
            Init();if(GameObject.Find(RootName)!=null){Debug.Log("[Roadside] Existing authored layout preserved; no changes");return;}
            var source=GameObject.Find("KH_CourseScenery/Trees/Tree_0");var board=AssetDatabase.LoadAssetAtPath<GameObject>(KH_TerrainPrototypeConfig.BoardTreePrefabs[0]);
            var shrub=AssetDatabase.LoadAssetAtPath<GameObject>(KH_TerrainPrototypeConfig.ShrubPrefabPaths[0]);
            var material=AssetDatabase.LoadAssetAtPath<Material>(KH_CourseGroundEditor.Dir+"/Scenery/KH_CourseTreeBoards.mat");
            if(source==null||board==null||shrub==null||material==null)throw new InvalidOperationException("Existing sources required");
            Directory.CreateDirectory(Evidence);string backup=Evidence+"/apply-before-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".unity";File.Copy(KH_CourseGroundEditor.ScenePath,backup);
            if(!AssetDatabase.IsValidFolder(Dir))AssetDatabase.CreateFolder(KH_CourseGroundEditor.Dir,"Roadside");
            var leaf=ShrubMaterial(shrub,"Leaves","KominkaHillside/Foliage Lite","KH_RoadsideShrubLeaves");var bark=ShrubMaterial(shrub,"Bark","KominkaHillside/Scenery Lite","KH_RoadsideShrubBark");
            var report=new Report{utc=DateTime.UtcNow.ToString("o")};var root=new GameObject(RootName);
            try
            {
                for(int i=0;i<Centers.Length;i++)
                {
                    var c=Centers[i];var p=new Vector3(c.x,0,c.y);var group=Child(root.transform,"Grove_"+i.ToString("D2"));
                    float yaw=27+i*53;var outwards=p-route._ProjectToTrail(p);outwards.y=0;outwards.Normalize();var along=new Vector3(outwards.z,0,-outwards.x);
                    Tree(source,group.transform,p,7+(i%3)*.65f,yaw,report,"Tree_"+i.ToString("D2"));
                    Board(board,group.transform,p+outwards*7+along*6,6.4f+(i%3),yaw,material,report,"Board_"+i.ToString("D2")+"_a");
                    Board(board,group.transform,p+outwards*11-along*7,8.3f-(i%3)*.6f,yaw+32,material,report,"Board_"+i.ToString("D2")+"_b");
                    for(int j=0;j<3;j++)Shrub(shrub,group.transform,p+along*(j*3.1f-3)-outwards*(3+(j%2)*1.5f),1.1f+j*.3f,yaw+j*39,leaf,bark,report,"Shrub_"+i.ToString("D2")+"_"+j);
                    if(group.transform.childCount==0)UnityEngine.Object.DestroyImmediate(group);else report.groups++;
                }
                report.renderers=root.GetComponentsInChildren<Renderer>().Length;AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(root.scene);if(!EditorSceneManager.SaveScene(root.scene))throw new IOException("Scene not saved");
                File.WriteAllText(Evidence+"/applied.json",JsonUtility.ToJson(report,true));Debug.Log("[Roadside] Added groups="+report.groups+" trees="+report.trees+" boards="+report.boards+" shrubs="+report.shrubs+"; navigation refresh required");
            }
            catch{UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveOpenScenes();throw;}
        }
        [MenuItem("Tools/KominkaHillside/Course/142 - Verify Roadside Autumn Groves")]
        public static void Verify()
        {
            Init();var root=GameObject.Find(RootName);if(root==null)throw new InvalidOperationException("Roadside missing");var report=new Report{utc=DateTime.UtcNow.ToString("o")};
            foreach(Transform group in root.transform)foreach(Transform t in group)
            {
                if(group.name=="RestBenches")
                {
                    var mesh=t.GetComponent<MeshFilter>().sharedMesh;
                    if(Vector3.Distance(mesh.bounds.size,new Vector3(1.6f,.45f,.5f))>.01f||mesh.triangles.Length/3!=528)report.errors.Add(t.name+" bench mesh");
                    if(t.name!="ViewpointBench"&&(route._DistanceToTrail(t.position)<4||t.GetComponent<BoxCollider>()==null||t.GetComponent<NavMeshModifierVolume>()==null))report.errors.Add(t.name+" bench clearance/collider");
                    continue;
                }
                if(group.name=="RoadsideGrass")
                {
                    var mesh=t.GetComponent<MeshFilter>().sharedMesh;
                    for(int i=0;i<mesh.vertexCount;i+=3){var p=t.TransformPoint(mesh.vertices[i]);if(route._DistanceToTrail(p)<2.5f||Mathf.Abs(p.y-Ground(p))>.10f)report.errors.Add(t.name+" grass root/clearance");}
                    if(t.GetComponentsInChildren<Collider>().Length!=0||t.GetComponent<LODGroup>()==null)report.errors.Add(t.name+" grass structure");
                    continue;
                }
                var b=BoundsOf(t.gameObject);float clearance=route._DistanceToTrail(b.center)-new Vector2(b.extents.x,b.extents.z).magnitude;
                if(clearance<4.99f)report.errors.Add(t.name+" intrudes protected walking band");
                float ground=Ground(t.position);if(Mathf.Abs(b.min.y-(ground-.08f))>.02f)report.errors.Add(t.name+" ground mismatch");
                var lod=t.GetComponent<LODGroup>();if(lod==null||lod.GetLODs().Any(l=>l.renderers.Length==0||l.renderers.Any(r=>r==null)))report.errors.Add(t.name+" LOD reference");
                if(t.GetComponent<CapsuleCollider>()==null||t.GetComponent<NavMeshModifierVolume>()==null)report.errors.Add(t.name+" obstacle missing");
                report.entries.Add(new Entry{name=t.name,position=t.position,routeClearance=clearance});
            }
            foreach(var r in root.GetComponentsInChildren<Renderer>())
            {if(GameObjectUtility.GetStaticEditorFlags(r.gameObject)!=0||r.shadowCastingMode!=ShadowCastingMode.Off||r.receiveShadows||!r.allowOcclusionWhenDynamic)report.errors.Add(r.name+" renderer flags");
                foreach(var m in r.sharedMaterials)if(m==null||!m.enableInstancing||ShaderUtil.ShaderHasError(m.shader))report.errors.Add(r.name+" material");}
            foreach(var c in root.GetComponentsInChildren<Component>())if(c==null||!(c is Transform||c is MeshFilter||c is MeshRenderer||c is LODGroup||c is CapsuleCollider||c is BoxCollider||c is NavMeshModifierVolume))report.errors.Add("Unexpected component "+c);
            report.groups=root.transform.childCount;report.renderers=root.GetComponentsInChildren<Renderer>().Length;File.WriteAllText(Evidence+"/verified.json",JsonUtility.ToJson(report,true));
            if(report.errors.Count>0)throw new InvalidOperationException(string.Join("; ",report.errors));Debug.Log("[Roadside] Geometry and references verified objects="+report.entries.Count+"; navigation verified separately");
        }
        [MenuItem("Tools/KominkaHillside/Course/144 - Refine Owned Roadside Clearances")]
        public static void RefineClearances()
        {
            Init();var root=GameObject.Find(RootName);if(root==null)throw new InvalidOperationException("Missing roadside");
            foreach(Transform group in root.transform)foreach(Transform t in group)
            {
                if(group.name=="RoadsideGrass"||group.name=="RestBenches")continue;
                for(int iteration=0;iteration<6;iteration++)
                {
                    var b=BoundsOf(t.gameObject);float clearance=route._DistanceToTrail(b.center)-new Vector2(b.extents.x,b.extents.z).magnitude;
                    if(clearance>=5.01f)break;
                    var away=b.center-route._ProjectToTrail(b.center);away.y=0;away.Normalize();t.position+=away*(5.1f-clearance);
                    b=BoundsOf(t.gameObject);t.position+=Vector3.up*(Ground(t.position)-.08f-b.min.y);
                }
                var c=t.GetComponent<CapsuleCollider>();c.height=Mathf.Max(c.height,c.radius*2);c.center=new Vector3(0,c.height*.5f,0);
                var v=t.GetComponent<NavMeshModifierVolume>();v.center=c.center;
                v.size=new Vector3(c.radius*2+.6f/t.lossyScale.x,c.height+1/t.lossyScale.y,c.radius*2+.6f/t.lossyScale.z);v.area=UnityEngine.AI.NavMesh.GetAreaFromName("Not Walkable");
            }
            EditorSceneManager.MarkSceneDirty(root.scene);if(!EditorSceneManager.SaveScene(root.scene))throw new IOException("Refinement save failed");
        }
    }
}
