using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace KominkaHillside.Editor
{
    public static partial class KH_CourseSceneryEditor
    {
        const string SceneryDir=KH_CourseGroundEditor.Dir+"/Scenery";
        const string RootName="KH_CourseScenery";
        static Terrain[] terrains;
        static float Ground(float x,float z)
        {
            foreach(var t in terrains)
            {
                Vector3 p=t.transform.position,s=t.terrainData.size;
                if(x>=p.x&&x<=p.x+s.x&&z>=p.z&&z<=p.z+s.z)
                    return p.y+t.terrainData.GetInterpolatedHeight((x-p.x)/s.x,(z-p.z)/s.z);
            }
            throw new InvalidOperationException("No course ground at "+x+","+z);
        }
        static float DistanceToRoute(Vector3 p)
        {
            float h;return KH_CourseGroundEditor.RouteDistance(p.x,p.z,out h);
        }
        static float Random(System.Random r,float min,float max){return Mathf.Lerp(min,max,(float)r.NextDouble());}
        static GameObject Child(Transform parent,string name)
        {var g=new GameObject(name);g.transform.SetParent(parent,false);g.layer=11;return g;}
        static Material Material(string name,string shaderName,Texture texture,bool vertexTint=false)
        {
            var shader=Shader.Find(shaderName);if(shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Shader unavailable: "+shaderName);
            var m=new Material(shader){name=name,enableInstancing=true};m.SetTexture("_MainTex",texture);m.SetColor("_Color",Color.white);
            if(m.HasProperty("_VertexTint"))m.SetFloat("_VertexTint",vertexTint?1:0);
            AssetDatabase.CreateAsset(m,SceneryDir+"/"+name+".mat");return m;
        }
        static Bounds BoundsOf(GameObject g)
        {var rs=g.GetComponentsInChildren<Renderer>(true);if(rs.Length==0)throw new InvalidOperationException("No renderers: "+g.name);var b=rs[0].bounds;for(int i=1;i<rs.Length;i++)b.Encapsulate(rs[i].bounds);return b;}
        static void RenderSetup(Renderer r,Material material,bool shadow)
        {
            r.sharedMaterial=material;r.shadowCastingMode=shadow?ShadowCastingMode.On:ShadowCastingMode.Off;r.receiveShadows=false;
            r.allowOcclusionWhenDynamic=true;
            // Keep transforms unbatched so repeated meshes can instance and LOD fades remain per object.
            GameObjectUtility.SetStaticEditorFlags(r.gameObject,StaticEditorFlags.OccludeeStatic);
            if(PrefabUtility.IsPartOfPrefabInstance(r))PrefabUtility.RecordPrefabInstancePropertyModifications(r);
        }
        static void Lods(GameObject root,Renderer[] near,Renderer[] far,float transition,float cull)
        {
            var group=root.AddComponent<LODGroup>();group.fadeMode=LODFadeMode.CrossFade;group.animateCrossFading=false;
            var lods=far==null?new[]{new LOD(cull,near){fadeTransitionWidth=.3f}}:
                new[]{new LOD(transition,near){fadeTransitionWidth=.35f},new LOD(cull,far){fadeTransitionWidth=.25f}};
            group.SetLODs(lods);group.RecalculateBounds();
        }
        [MenuItem("Tools/KominkaHillside/Course/29 - Build Course Scenery")]
        public static void BuildScenery()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            if(GameObject.Find(RootName)!=null||Directory.Exists(SceneryDir))throw new InvalidOperationException("Scenery already exists; preserve authored work");
            terrains=UnityEngine.Object.FindObjectsOfType<Terrain>();if(terrains.Length!=20)throw new InvalidOperationException("Expected 20 course tiles");
            var tree=AssetDatabase.LoadAssetAtPath<GameObject>(KH_TerrainPrototypeConfig.TreePrefabPath);
            var board=AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+"/Natures/Board_TreeB01.prefab");
            var leaves=AssetDatabase.LoadAssetAtPath<Material>(KH_TerrainPrototypeConfig.AutumnMaterialDir+"/Mat_BroadLeaves02_Autumn.mat");
            var boards=AssetDatabase.LoadAssetAtPath<Material>(KH_TerrainPrototypeConfig.AutumnMaterialDir+"/Mat_BoardTrees01_Autumn.mat");
            if(tree==null||board==null||leaves==null||boards==null)throw new InvalidOperationException("Existing tree/board/autumn assets required");
            var bark=tree.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials).FirstOrDefault(m=>m!=null&&m.name=="Mat_BroadBarkB01");
            if(bark==null)throw new InvalidOperationException("Bark source missing");
            foreach(string shaderName in new[]{"KominkaHillside/Foliage Lite","KominkaHillside/Scenery Lite"})
                if(Shader.Find(shaderName)==null||ShaderUtil.ShaderHasError(Shader.Find(shaderName)))throw new InvalidOperationException("Shader error: "+shaderName);
            File.Copy(KH_CourseGroundEditor.ScenePath,Evidence+"before-course-scenery.unity",true);
            var scene=EditorSceneManager.GetActiveScene();GameObject root=null;
            try
            {
                Directory.CreateDirectory(SceneryDir);AssetDatabase.Refresh();root=new GameObject(RootName);
                var leafMat=Material("KH_CourseLeaves","KominkaHillside/Foliage Lite",leaves.GetTexture("_AlbedoTex"));
                var boardMat=Material("KH_CourseTreeBoards","KominkaHillside/Foliage Lite",boards.GetTexture("_AlbedoTex"));
                var barkMat=Material("KH_CourseBark","KominkaHillside/Scenery Lite",bark.GetTexture("_AlbedoTex"));
                var stoneMat=Material("KH_CourseStones","KominkaHillside/Scenery Lite",null,true);
                var grassMat=Material("KH_CourseGrass","KominkaHillside/Foliage Lite",null,true);
                var random=new System.Random(2026092701);
                var treeRoot=Child(root.transform,"Trees");
                var route=KH_CourseGroundEditor.Route;float next=25,walked=0;int treeCount=0;
                for(int i=1;i<route.Length;i++)
                {
                    var a=route[i-1];var b=route[i];float length=Vector3.Distance(a,b);
                    while(next<=walked+length)
                    {
                        var p=Vector3.Lerp(a,b,(next-walked)/length);next+=Random(random,62,96);
                        var side=new Vector3(b.z-a.z,0,a.x-b.x).normalized*(treeCount%2==0?-1:1);
                        p+=side*Random(random,9,15);
                        if(p.x<15||p.x>580||p.z<220||p.z>700||DistanceToRoute(p)<8||Vector2.Distance(new Vector2(p.x,p.z),new Vector2(215,605))<24)continue;
                        var river=KH_CourseGroundEditor.RiverAt(p.z);
                        if(Mathf.Abs(p.x-river.x)<KH_CourseGroundEditor.HalfWidth(p.z)+10)continue;
                        p.y=Ground(p.x,p.z)-.12f;
                        if(Mathf.Abs(Ground(p.x+2,p.z)-Ground(p.x-2,p.z))>2||Mathf.Abs(Ground(p.x,p.z+2)-Ground(p.x,p.z-2))>2)continue;
                        var holder=Child(treeRoot.transform,"Tree_"+treeCount++);holder.transform.position=p;holder.transform.localScale=Vector3.one*Random(random,.82f,1.05f);
                        var near=(GameObject)PrefabUtility.InstantiatePrefab(tree,scene);near.transform.SetParent(holder.transform,false);near.name="Near";near.transform.localRotation=Quaternion.Euler(0,Random(random,0,360),0);
                        foreach(var t in near.GetComponentsInChildren<Transform>(true)){t.gameObject.layer=11;PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);}
                        foreach(var r in near.GetComponentsInChildren<MeshRenderer>(true))RenderSetup(r,r.sharedMaterial.name.Contains("Leaves")?leafMat:barkMat,true);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(near.transform);
                        var nearBounds=BoundsOf(near);var farRoot=Child(holder.transform,"Far");
                        for(int k=0;k<2;k++)
                        {
                            var far=(GameObject)PrefabUtility.InstantiatePrefab(board,scene);far.transform.SetParent(farRoot.transform,false);far.name="Board_"+k;
                            float ratio=nearBounds.size.y/BoundsOf(far).size.y;far.transform.localScale*=ratio;
                            far.transform.localRotation=Quaternion.Euler(0,45+k*90,0);
                            far.transform.position+=Vector3.up*(nearBounds.min.y-BoundsOf(far).min.y);
                            foreach(var r in far.GetComponentsInChildren<MeshRenderer>(true))RenderSetup(r,boardMat,false);
                            PrefabUtility.RecordPrefabInstancePropertyModifications(far.transform);
                        }
                        Lods(holder,near.GetComponentsInChildren<Renderer>(),farRoot.GetComponentsInChildren<Renderer>(),.18f,.005f);
                    }
                    walked+=length;
                }
                BuildBankStones(root.transform,stoneMat,random);
                BuildGrass(root.transform,grassMat,random);
                AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene))throw new IOException("Scenery save failed");
                Debug.Log("[KH Course Scenery] Saved "+treeCount+" LOD trees plus bank stones and grass. Navigation refresh and saved-state verification required.");
            }
            catch
            {
                if(root!=null)UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.MarkSceneDirty(scene);
                if(EditorSceneManager.SaveScene(scene))AssetDatabase.DeleteAsset(SceneryDir);
                else Debug.LogError("[KH Course Scenery] Rollback save failed; retained assets and Temp scene backup");
                throw;
            }
        }

        static Mesh StoneCluster(int seed)
        {
            var random=new System.Random(seed);var vertices=new List<Vector3>();var colors=new List<Color>();var indices=new List<int>();
            for(int s=0;s<3;s++)
            {
                float r=Random(random,.23f,.53f),tint=Random(random,.85f,1.1f);var center=new Vector3((s-1)*.65f,0,Random(random,-.23f,.23f));int first=vertices.Count;
                for(int ring=0;ring<3;ring++)for(int k=0;k<8;k++)
                {
                    float angle=k*Mathf.PI/4,rr=r*(ring==1?1:.55f)*Random(random,.9f,1.08f);
                    vertices.Add(center+new Vector3(Mathf.Cos(angle)*rr,ring*r*.42f,Mathf.Sin(angle)*rr*.8f));
                    colors.Add(new Color(.48f*tint,.46f*tint,.4f*tint,1));
                }
                for(int ring=0;ring<2;ring++)for(int k=0;k<8;k++)
                {int a=first+ring*8+k,b=first+ring*8+(k+1)%8;indices.AddRange(new[]{a,a+8,b,b,a+8,b+8});}
                for(int k=1;k<7;k++){indices.AddRange(new[]{first+16,first+16+k+1,first+16+k});indices.AddRange(new[]{first,first+k,first+k+1});}
            }
            var mesh=new Mesh{name="KH_StoneCluster_"+seed};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        static void BuildBankStones(Transform root,Material material,System.Random random)
        {
            var parent=Child(root,"BankStones");var meshes=new Mesh[4];
            for(int i=0;i<4;i++){meshes[i]=StoneCluster(910+i);AssetDatabase.CreateAsset(meshes[i],SceneryDir+"/KH_StoneCluster_"+i+".asset");}
            int count=0;
            for(float z=12;z<707;z+=Random(random,8,14))
            {
                if(Mathf.Abs(z-190)<10||Mathf.Abs(z-490)<10)continue;
                var river=KH_CourseGroundEditor.RiverAt(z);float side=count%2==0?-1:1;
                var p=new Vector3(river.x+side*KH_CourseGroundEditor.HalfWidth(z)*.77f,river.y-.14f,z);
                if(DistanceToRoute(p)<4)continue;
                // Keep clusters in already excluded wet ground, rather than introduce hidden walking obstacles.
                bool clear=true;
                for(int k=-1;k<=1;k++)
                {
                    float x=p.x+k*.9f;NavMeshHit hit;
                    if(NavMesh.SamplePosition(new Vector3(x,Ground(x,z),z),out hit,.4f,NavMesh.AllAreas))clear=false;
                }
                if(!clear)continue;
                var go=Child(parent.transform,"StoneCluster_"+count);go.transform.position=p;go.transform.localRotation=Quaternion.Euler(0,Random(random,-18,18),0);
                var mesh=meshes[count%4];go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();RenderSetup(r,material,false);
                go.AddComponent<MeshCollider>().sharedMesh=mesh;
                var modifier=go.AddComponent<NavMeshModifier>();modifier.overrideArea=true;modifier.area=NavMesh.GetAreaFromName("Not Walkable");
                Lods(go,new Renderer[]{r},null,0,.025f);count++;
            }
        }
        static void BuildGrass(Transform root,Material material,System.Random random)
        {
            var parent=Child(root,"Grass");int count=0;
            for(int section=0;section<24;section++)
            {
                var vertices=new List<Vector3>();var colors=new List<Color>();var indices=new List<int>();
                float centerZ=section*30+15;var rv=KH_CourseGroundEditor.RiverAt(centerZ);var origin=new Vector3(rv.x,rv.y,centerZ);
                for(int tuft=0;tuft<24;tuft++)
                {
                    float z=section*30+Random(random,1,29),side=tuft%2==0?-1:1;var river=KH_CourseGroundEditor.RiverAt(z);
                    float x=river.x+side*(KH_CourseGroundEditor.HalfWidth(z)+Random(random,1.3f,4.6f));
                    var p=new Vector3(x,Ground(x,z),z);
                    if(DistanceToRoute(p)<3.3f||Mathf.Abs(z-190)<5||Mathf.Abs(z-490)<5)continue;
                    for(int blade=0;blade<5;blade++)
                    {
                        float angle=Random(random,0,Mathf.PI*2),height=Random(random,.16f,.4f),width=Random(random,.018f,.035f);var offset=new Vector3(Random(random,-.15f,.15f),.005f,Random(random,-.15f,.15f));
                        offset.y=Ground(x+offset.x,z+offset.z)-p.y+.005f;
                        var sideVec=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));var pos=p-origin+offset;int first=vertices.Count;
                        vertices.Add(pos-sideVec*width);vertices.Add(pos+sideVec*width);vertices.Add(pos+sideVec*height*.25f+Vector3.up*height);
                        colors.Add(new Color(.3f,.33f,.16f));colors.Add(new Color(.3f,.33f,.16f));colors.Add(new Color(.56f,.51f,.28f));
                        indices.AddRange(new[]{first,first+2,first+1});
                    }
                }
                if(vertices.Count==0)continue;
                var mesh=new Mesh{name="KH_BankGrass_"+section};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh,SceneryDir+"/KH_BankGrass_"+section+".asset");var go=Child(parent.transform,"Grass_"+section);go.transform.position=origin;
                go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();RenderSetup(r,material,false);
                Lods(go,new Renderer[]{r},null,0,.35f);count++;
            }
        }
    }
}
