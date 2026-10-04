using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Unity.AI.Navigation;

namespace KominkaHillside.Editor
{
    public static partial class KH_RoadsideSceneryEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/150 - Verify River Verge Study")]
        public static void VerifyValley()
        {
            Init();Physics.SyncTransforms();var root=GameObject.Find(ValleyRoot);if(root==null)throw new InvalidOperationException("Verge missing");
            var baseline=JsonUtility.FromJson<ValleyState>(File.ReadAllText(ValleyEvidence+"/baseline.json"));var report=new ValleyState{utc=DateTime.UtcNow.ToString("o"),sceneHash=Digest(File.ReadAllBytes(KH_CourseGroundEditor.ScenePath))};
            foreach(var t in terrains)
            {
                var td=t.terrainData;var before=baseline.tiles.First(v=>v.path==AssetDatabase.GetAssetPath(td));int n=td.alphamapResolution;
                if(HeightHash(td)!=before.heightHash||td.terrainLayers.Length!=4)report.errors.Add(t.name+" height/layers");
                for(int l=0;l<td.terrainLayers.Length;l++)if(AssetDatabase.GetAssetPath(td.terrainLayers[l])!=KH_CourseAutumnEditor.Dir+"/Layer"+l+".terrainlayer")report.errors.Add(t.name+" changed layer reference");
                var old=new float[n,n,4];byte[] raw=File.ReadAllBytes(ValleyEvidence+"/"+td.name+".alphas");Buffer.BlockCopy(raw,0,old,0,raw.Length);var now=td.GetAlphamaps(0,0,n,n);
                for(int z=0;z<n;z++)for(int x=0;x<n;x++)
                {
                    float sum=0;bool changed=false;for(int l=0;l<4;l++){sum+=now[z,x,l];if(Mathf.Abs(old[z,x,l]-now[z,x,l])>.0001f)changed=true;}
                    if(Mathf.Abs(sum-1)>.016f)report.errors.Add(t.name+" invalid alpha sum");
                    if(!changed)continue;report.paintedPixels++;float px=t.transform.position.x+x*td.size.x/(n-1),pz=t.transform.position.z+z*td.size.z/(n-1);
                    if(ValleyMask(px,pz)<=0)report.changedOutside++;
                }
                report.tiles.Add(new TileState{path=before.path,heightHash=HeightHash(td),origin=t.transform.position,resolution=n});
            }
            if(report.changedOutside!=0||report.paintedPixels==0)report.errors.Add("Paint outside region or absent "+report.changedOutside);
            foreach(var left in terrains)foreach(var right in terrains)
            {
                if(Mathf.Abs(left.transform.position.x+left.terrainData.size.x-right.transform.position.x)>.01f||Mathf.Abs(left.transform.position.z-right.transform.position.z)>.01f)continue;
                int n=left.terrainData.alphamapResolution;var a=left.terrainData.GetAlphamaps(n-1,0,1,n);var b=right.terrainData.GetAlphamaps(0,0,1,n);
                for(int z=0;z<n;z++)
                {
                    float pz=left.transform.position.z+z*left.terrainData.size.z/(n-1);if(ValleyMask(right.transform.position.x,pz)<.99f)continue;
                    for(int l=0;l<4;l++)if(Mathf.Abs(a[z,0,l]-b[z,0,l])>.02f)report.errors.Add(left.name+" paint seam "+pz);
                }
            }
            foreach(Transform t in root.transform)
            {
                var lod=t.GetComponent<LODGroup>();if(lod==null||lod.GetLODs().Any(l=>l.renderers.Length==0||l.renderers.Any(r=>r==null)))report.errors.Add(t.name+" LOD");
                if(t.name.StartsWith("Canopy")){report.trees++;if(route._DistanceToTrail(t.position)<3.6f)report.errors.Add(t.name+" trunk clearance");if(Mathf.Abs(BoundsOf(t.gameObject).min.y-Ground(t.position)+.1f)>.02f)report.errors.Add(t.name+" root support");}
                if(t.name.StartsWith("Understory")){report.shrubs++;if(Mathf.Abs(BoundsOf(t.gameObject).min.y-Ground(t.position)+.06f)>.02f)report.errors.Add(t.name+" root support");}
                if(t.name.StartsWith("BankStone"))
                {
                    report.stones++;var mesh=t.GetComponent<MeshFilter>().sharedMesh;
                    foreach(var vertex in mesh.vertices)if(vertex.y<mesh.bounds.min.y+.025f){var p=t.TransformPoint(vertex);if(p.y>Ground(p)+.015f)report.errors.Add(t.name+" floating stone foot");}
                }
                if(t.name.StartsWith("KH_RiverVergeGrass"))
                {
                    report.grassChunks++;var mesh=t.GetComponent<MeshFilter>().sharedMesh;if(t.GetComponents<Collider>().Length!=0)report.errors.Add(t.name+" grass collider");
                    foreach(var v in mesh.vertices){var p=t.TransformPoint(v);if(p.y<Ground(p)-.01f||p.y>Ground(p)+.9f)report.errors.Add(t.name+" grass ground");if(BankOpening(p,0))report.errors.Add(t.name+" grass in bank opening");}
                }
            }
            foreach(var r in root.GetComponentsInChildren<Renderer>())
            {
                report.renderers++;
                if(r.shadowCastingMode!=ShadowCastingMode.Off||r.receiveShadows||GameObjectUtility.GetStaticEditorFlags(r.gameObject)!=0)report.errors.Add(r.name+" renderer flags");
                if(r.sharedMaterials.Any(m=>m==null||!m.enableInstancing||ShaderUtil.ShaderHasError(m.shader)))report.errors.Add(r.name+" materials");
            }
            foreach(var m in root.GetComponentsInChildren<MeshFilter>())report.triangles+=m.sharedMesh.triangles.Length/3;
            foreach(var c in root.GetComponentsInChildren<Collider>())
            {
                var v=c.GetComponent<NavMeshModifierVolume>();if(v==null||v.area!=NavMesh.GetAreaFromName("Not Walkable"))report.errors.Add(c.name+" volume");
                var b=c.bounds;
                if(BankOpening(b.center,Mathf.Max(b.extents.x,b.extents.z)))report.errors.Add(c.name+" collider in bank opening");
                if(route._DistanceToTrail(b.center)-new Vector2(b.extents.x,b.extents.z).magnitude<1.8f)report.errors.Add(c.name+" walking clearance");
                for(int x=0;x<5;x++)for(int z=0;z<5;z++)
                {
                    float px=Mathf.Lerp(b.min.x,b.max.x,(x+.5f)/5),pz=Mathf.Lerp(b.min.z,b.max.z,(z+.5f)/5);float ground=Ground(new Vector3(px,0,pz));
                    for(float y=ground-.1f;y<b.max.y+.15f;y+=.10f)
                    {
                        var p=new Vector3(px,y,pz);var closest=c.ClosestPoint(p);if(Mathf.Abs(closest.x-p.x)>.03f||Mathf.Abs(closest.z-p.z)>.03f)continue;
                        report.obstacleSamples++;NavMeshHit hit;if(NavMesh.SamplePosition(p,out hit,.075f,NavMesh.AllAreas))report.errors.Add(c.name+" nav occupancy "+p);
                    }
                }
            }
            var bench=GameObject.Find(RootName+"/RestBenches/RiverBench").transform;
            var endpoints=new[]{bench.position+bench.forward*1.5f,new Vector3(KH_CourseGroundEditor.RiverAt(305).x+KH_CourseGroundEditor.HalfWidth(305)+3.5f,0,305),new Vector3(KH_CourseGroundEditor.RiverAt(282).x+KH_CourseGroundEditor.HalfWidth(282)+3.5f,0,282)};
            foreach(var pos in endpoints)
            {
                var p=pos;p.y=Ground(p);var on=route._ProjectToTrail(p);NavMeshHit a,b;var path=new NavMeshPath();
                if(!NavMesh.SamplePosition(p,out a,.75f,NavMesh.AllAreas)||!NavMesh.SamplePosition(on,out b,.5f,NavMesh.AllAreas)||!NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)report.errors.Add("Rest/bank access "+p);else report.accessPaths++;
            }
            File.WriteAllText(ValleyEvidence+"/verified.json",JsonUtility.ToJson(report,true));
            if(report.errors.Count>0)throw new InvalidOperationException(string.Join("; ",report.errors.Take(20)));
            Debug.Log("[RiverVerge] PASS paint="+report.paintedPixels+" trees="+report.trees+" shrubs="+report.shrubs+" stones="+report.stones+" grass="+report.grassChunks+" access="+report.accessPaths);
        }
    }
}
