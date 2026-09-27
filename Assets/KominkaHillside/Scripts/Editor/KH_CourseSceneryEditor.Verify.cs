using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace KominkaHillside.Editor
{
    public static partial class KH_CourseSceneryEditor
    {
        [Serializable] public class SceneryReport
        {
            public string status,utc,scene;
            public int errors,trees,stoneClusters,grassChunks,lodGroups,renderers,colliders,materials,uniqueMeshes,nearTriangles,farTreeTriangles,grassBlades;
            public float maximumGrassRootGap,maximumTreeHeightMismatch;
            public string[] findings;
            public string limitations="Saved Edit-mode structure and static navigation only. Screen-relative LOD is camera-driven; no hill occlusion bake, platform performance or visual/device acceptance is inferred.";
        }
        [MenuItem("Tools/KominkaHillside/Course/32 - Verify Course Scenery")]
        public static void VerifyScenery()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var root=GameObject.Find(RootName);if(root==null)throw new InvalidOperationException("Scenery required");
            terrains=UnityEngine.Object.FindObjectsOfType<Terrain>();var errors=new List<string>();
            var report=new SceneryReport{utc=DateTime.UtcNow.ToString("o"),scene=root.scene.path};
            if(terrains.Length!=20||terrains.Any(t=>!t.enabled||!t.drawHeightmap||t.GetComponent<TerrainCollider>()==null||!t.GetComponent<TerrainCollider>().enabled))errors.Add("Ground visibility/collider mismatch");
            var rs=root.GetComponentsInChildren<Renderer>(true);report.renderers=rs.Length;
            var cs=root.GetComponentsInChildren<Collider>(true);report.colliders=cs.Length;
            var ms=rs.SelectMany(r=>r.sharedMaterials).Distinct().ToArray();report.materials=ms.Length;
            if(ms.Length!=5)errors.Add("Expected five shared scenery materials");
            foreach(var m in ms)
            {
                // Stone/grass use vertex colors with the shader's persistent default white texture.
                bool needsTexture=m!=null&&m.name!="KH_CourseStones"&&m.name!="KH_CourseGrass";
                if(m==null||!AssetDatabase.GetAssetPath(m).StartsWith(SceneryDir+"/")||!m.enableInstancing||(needsTexture&&m.mainTexture==null)||ShaderUtil.ShaderHasError(m.shader))errors.Add("Material/texture/shader mismatch: "+(m==null?"null":m.name));
                if(m!=null&&m.HasProperty("_VertexTint"))
                {float expected=m.name=="KH_CourseStones"||m.name=="KH_CourseGrass"?1:0;if(m.GetFloat("_VertexTint")!=expected)errors.Add("Vertex tint mismatch: "+m.name);}
            }
            foreach(var r in rs)if(!r.enabled||!r.gameObject.activeInHierarchy||r.forceRenderingOff)errors.Add("Unexpected renderer state: "+r.name);
            foreach(var c in cs)if(!c.enabled||!c.gameObject.activeInHierarchy||c.isTrigger)errors.Add("Unexpected collider state: "+c.name);
            if(root.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Length!=0)errors.Add("Scenery must not add runtime Udon");
            var groups=root.GetComponentsInChildren<LODGroup>(true);report.lodGroups=groups.Length;
            var assigned=new HashSet<Renderer>();
            foreach(var g in groups)
            {
                if(!g.enabled||g.fadeMode!=LODFadeMode.CrossFade||g.animateCrossFading)errors.Add("LOD mode mismatch: "+g.name);
                foreach(var lod in g.GetLODs())foreach(var r in lod.renderers)
                    if(r==null||!r.transform.IsChildOf(g.transform)||!assigned.Add(r))errors.Add("LOD renderer reference mismatch: "+g.name);
            }
            if(assigned.Count!=rs.Length)errors.Add("Renderer omitted from LOD groups");
            report.uniqueMeshes=root.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).Distinct().Count();
            foreach(Transform t in root.transform.Find("Trees"))
            {
                report.trees++;var lods=t.GetComponent<LODGroup>().GetLODs();var near=t.Find("Near").gameObject;var far=t.Find("Far").gameObject;
                if(lods.Length!=2||Mathf.Abs(lods[0].screenRelativeTransitionHeight-.18f)>.0001f||Mathf.Abs(lods[1].screenRelativeTransitionHeight-.005f)>.0001f)errors.Add("Tree LOD thresholds mismatch");
                if(DistanceToRoute(t.position)<8||near.GetComponentsInChildren<Collider>().Length!=1||far.GetComponentsInChildren<Collider>().Length!=0)errors.Add("Tree path/collider mismatch: "+t.name);
                var a=BoundsOf(near);var b=BoundsOf(far);float mismatch=Mathf.Max(Mathf.Abs(a.min.y-b.min.y),Mathf.Abs(a.max.y-b.max.y));
                report.maximumTreeHeightMismatch=Mathf.Max(report.maximumTreeHeightMismatch,mismatch);if(mismatch>.01f)errors.Add("Far tree vertical bounds mismatch: "+t.name);
                foreach(var f in near.GetComponentsInChildren<MeshFilter>())report.nearTriangles+=Triangles(f.sharedMesh);
                foreach(var f in far.GetComponentsInChildren<MeshFilter>())report.farTreeTriangles+=Triangles(f.sharedMesh);
            }
            foreach(Transform t in root.transform.Find("BankStones"))
            {
                report.stoneClusters++;var mesh=t.GetComponent<MeshFilter>().sharedMesh;
                report.nearTriangles+=Triangles(mesh);var modifier=t.GetComponent<NavMeshModifier>();
                if(modifier==null||!modifier.overrideArea||modifier.area!=NavMesh.GetAreaFromName("Not Walkable")||t.GetComponent<MeshCollider>().sharedMesh!=mesh)errors.Add("Stone navigation/collider mismatch: "+t.name);
                if(DistanceToRoute(t.position)<4)errors.Add("Stone too near route: "+t.name);
                foreach(var v in mesh.vertices)
                {
                    NavMeshHit hit;if(NavMesh.SamplePosition(t.TransformPoint(v),out hit,.12f,NavMesh.AllAreas))
                    {errors.Add("Walkable surface on wet-bank stone: "+t.name);break;}
                }
            }
            foreach(Transform t in root.transform.Find("Grass"))
            {
                report.grassChunks++;var mesh=t.GetComponent<MeshFilter>().sharedMesh;report.nearTriangles+=Triangles(mesh);
                var vertices=mesh.vertices;report.grassBlades+=vertices.Length/3;
                for(int i=0;i<vertices.Length;i+=3)
                {
                    var p=t.TransformPoint((vertices[i]+vertices[i+1])*.5f);float gap=Mathf.Abs(p.y-Ground(p.x,p.z)-.005f);
                    report.maximumGrassRootGap=Mathf.Max(report.maximumGrassRootGap,gap);if(gap>.015f)errors.Add("Grass root misses ground: "+t.name);
                }
                if(t.GetComponentsInChildren<Collider>().Length!=0)errors.Add("Grass should not add colliders");
            }
            if(report.trees==0||report.grassChunks!=24||report.stoneClusters==0)errors.Add("Missing scenery category");
            report.errors=errors.Count;report.findings=errors.ToArray();report.status=errors.Count==0?"SCENERY_STRUCTURE_OK":"FAIL";
            File.WriteAllText(Evidence+"full-course-scenery.json",JsonUtility.ToJson(report,true));
            if(errors.Count>0)throw new InvalidOperationException("Scenery verification failed: "+string.Join("; ",errors.Take(12)));
            Debug.Log("[KH Course Scenery] "+report.status+" trees="+report.trees+" stones="+report.stoneClusters+" grassChunks="+report.grassChunks);
        }
        static int Triangles(Mesh mesh){int n=0;for(int i=0;i<mesh.subMeshCount;i++)n+=(int)mesh.GetIndexCount(i)/3;return n;}
    }
}
