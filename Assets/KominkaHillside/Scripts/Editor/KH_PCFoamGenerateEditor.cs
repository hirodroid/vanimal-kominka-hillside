using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    public static class KH_PCFoamGenerateEditor
    {
        internal const int Version=1;
        internal const string Folder="Assets/KominkaHillside/Data/Wash/PCFoam";
        [Serializable] public class Generated
        {
            public string dog, sourcePath, sourceHash, sourceGuid, shellGuid, shellHash, partMap;
            public long sourceFileId;public float meshScale;
            public int version=Version, requested, lobes, vertices, triangles;
            public float minRadius, maxRadius;public int[] byPart=new int[9];public float[] staticWeights;
            public int[] anchors;public int rejectedSupports;
        }
        [Serializable] public class Report {public string result,error;public List<Generated> dogs=new List<Generated>();}
        internal static string Key(KH_DogWash wash)=>wash.name.Replace("KH_Wash_","");
        internal static string MeshPath(KH_DogWash wash)=>Folder+"/KH_PCFoam_"+Key(wash)+".asset";
        internal static string MaterialPath(KH_DogWash wash)=>Folder+"/KH_PCFoam_"+Key(wash)+".mat";
        internal static string ThinPath(KH_DogWash wash)=>Folder+"/KH_PCThin_"+Key(wash)+".mat";
        internal static string ManifestPath(KH_DogWash wash)=>Folder+"/KH_PCFoam_"+Key(wash)+".json";

        [MenuItem("Tools/KominkaHillside/PC Foam/12 - Generate Six Dogs")]
        public static void Generate()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit required");
            var report=new Report();
            try
            {
                if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/KominkaHillside/Data/Wash","PCFoam");
                foreach(var wash in UnityEngine.Object.FindObjectsOfType<KH_DogWash>().OrderBy(w=>w.dogIndex))
                {
                    int count=Key(wash)=="Samoyed"||Key(wash)=="Lab"?256:Key(wash)=="Pome"?128:Key(wash)=="Shiba"?192:160;
                    report.dogs.Add(Generate(wash,count));
                }
                if(report.dogs.Count!=6)throw new InvalidOperationException("Expected six dogs");
                AssetDatabase.SaveAssets();report.result="PASS";
            }
            catch(Exception ex){report.result="FAIL";report.error=ex.ToString();Debug.LogError(ex);}
            Directory.CreateDirectory("Temp/PCFoamProof");File.WriteAllText("Temp/PCFoamProof/generation.json",JsonUtility.ToJson(report,true));
        }

        internal static Generated Generate(KH_DogWash wash,int count)
        {
            if(EditorUtility.IsDirty(wash.shellRenderer.sharedMaterial))throw new InvalidOperationException("Save shell material adjustments before generating: "+wash.name);
            var body=KH_PCFoamAuditEditor.Body(wash);var source=body.sharedMesh;
            KH_PCFoamAuditEditor.Inspect(wash,out var excluded);
            var positions=source.vertices;var normals=source.normals;var uv=source.uv;var bw=source.boneWeights;var faces=source.triangles;
            var weights=new float[source.blendShapeCount];var dp=new Vector3[source.vertexCount];var dn=new Vector3[source.vertexCount];var dt=new Vector3[source.vertexCount];
            // Only shapes that do not change on candidate vertices are baked. Dynamic-affected vertices are excluded above.
            for(int s=0;s<weights.Length;s++)
            {
                weights[s]=body.GetBlendShapeWeight(s);if(Mathf.Abs(weights[s])<.0001f)continue;
                if(source.GetBlendShapeFrameCount(s)!=1)throw new InvalidOperationException("Static multi-frame shape requires explicit interpolation: "+source.GetBlendShapeName(s));
                float frameWeight=source.GetBlendShapeFrameWeight(s,0);if(Mathf.Abs(frameWeight)<.001f)throw new InvalidOperationException("Zero morph frame weight");
                source.GetBlendShapeFrameVertices(s,0,dp,dn,dt);float factor=weights[s]/frameWeight;
                for(int i=0;i<positions.Length;i++){positions[i]+=dp[i]*factor;normals[i]+=dn[i]*factor;}
            }
            for(int i=0;i<normals.Length;i++)normals[i]=normals[i].normalized;
            float scale=wash.meshToWorldScale;
            var graph=new HashSet<int>[positions.Length];var areas=new float[positions.Length];
            for(int i=0;i<graph.Length;i++)graph[i]=new HashSet<int>();
            for(int i=0;i<faces.Length;i+=3)
            {
                int a=faces[i],b=faces[i+1],c=faces[i+2];
                graph[a].Add(b);graph[a].Add(c);graph[b].Add(a);graph[b].Add(c);graph[c].Add(a);graph[c].Add(b);
                float area=Vector3.Cross(positions[b]-positions[a],positions[c]-positions[a]).magnitude/6f;areas[a]+=area;areas[b]+=area;areas[c]+=area;
            }
            // Connect coincident UV-seam duplicates only when their normals and dominant bone agree.
            var coincident=new Dictionary<Vector3,List<int>>();
            for(int i=0;i<positions.Length;i++){if(!coincident.TryGetValue(positions[i],out var list)){list=new List<int>();coincident.Add(positions[i],list);}list.Add(i);}
            foreach(var list in coincident.Values)foreach(int a in list)foreach(int b in list)
                if(a!=b&&bw[a].boneIndex0==bw[b].boneIndex0&&Vector3.Dot(normals[a],normals[b])>.9f)graph[a].Add(b);

            float thickness=wash.shellRenderer.sharedMaterial.GetFloat("_FoamThickness");
            float minRadius=Mathf.Clamp(thickness*.45f,.009f,.026f),maxRadius=Mathf.Clamp(thickness*.8f,.014f,.04f);
            float offset=wash.shellRenderer.sharedMaterial.GetFloat("_BaseOffset");
            var lobes=new List<KH_PCFoamMesh.Lobe>();var minDistance=Enumerable.Repeat(float.PositiveInfinity,positions.Length).ToArray();
            var done=new bool[positions.Length];int rejected=0;
            // Farthest-point spacing avoids vertex-density bias; the first seed is the largest surface contribution.
            for(int attempt=0;attempt<positions.Length&&lobes.Count<count;attempt++)
            {
                int candidate=-1;float best=-1;
                for(int i=0;i<positions.Length;i++)
                {
                    if(excluded[i]||done[i]||areas[i]<=0||normals[i].sqrMagnitude<.9f)continue;
                    float score=lobes.Count==0?areas[i]:minDistance[i];
                    if(score>best){best=score;candidate=i;}
                }
                if(candidate<0)break;done[candidate]=true;
                // Deterministic low-discrepancy size variation without touching Unity's global random state.
                float seed=Mathf.Repeat(candidate*.61803398875f,.99999f);
                float radius=Mathf.Lerp(minRadius,maxRadius,Mathf.Sqrt(seed));
                int[] support=null;
                for(int trial=0;trial<3;trial++)
                {
                    support=Supports(candidate,radius/scale,positions,normals,graph,excluded);
                    if(support!=null)break;radius*=.8f;
                }
                if(support==null){rejected++;continue;}
                int part=Mathf.Clamp(Mathf.RoundToInt(wash.boneToPart[bw[candidate].boneIndex0]),0,8);
                lobes.Add(new KH_PCFoamMesh.Lobe {vertex=candidate,part=part,position=positions[candidate],normal=normals[candidate],uv=uv[candidate],
                    supportA=new Vector4(uv[support[0]].x,uv[support[0]].y,uv[support[1]].x,uv[support[1]].y),
                    supportB=new Vector4(uv[support[2]].x,uv[support[2]].y,uv[support[3]].x,uv[support[3]].y),radius=radius,offset=offset,seed=seed});
                for(int i=0;i<positions.Length;i++)minDistance[i]=Mathf.Min(minDistance[i],(positions[i]-positions[candidate]).sqrMagnitude);
            }
            if(lobes.Count<count)throw new InvalidOperationException(wash.name+": only "+lobes.Count+" / "+count+" valid lobes; no silent underfilling");
            var generated=KH_PCFoamMesh.Create(source,lobes);generated.name="KH_PCFoam_"+Key(wash);
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(wash));
            if(existing==null)AssetDatabase.CreateAsset(generated,MeshPath(wash));
            else {EditorUtility.CopySerialized(generated,existing);UnityEngine.Object.DestroyImmediate(generated);EditorUtility.SetDirty(existing);}
            var mat=EnsureMaterial(MaterialPath(wash),"KominkaHillside/PC Foam");
            mat.SetTexture("_Mask",wash.maskA);mat.SetColor("_FoamColor",wash.shellRenderer.sharedMaterial.GetColor("_FoamColor"));
            mat.SetColor("_FoamShadow",wash.shellRenderer.sharedMaterial.GetColor("_FoamShadow"));mat.SetFloat("_Diagnostic",0);EditorUtility.SetDirty(mat);
            mat.SetFloat("_NormalScale",.85f);mat.SetFloat("_Inset",.03f);
            var thin=EnsureMaterial(ThinPath(wash),"KominkaHillside/Wash Shell PC Thin");thin.CopyPropertiesFromMaterial(wash.shellRenderer.sharedMaterial);thin.renderQueue=3010;EditorUtility.SetDirty(thin);
            // The geometric crowns provide depth on white fur; a pale continuous bed avoids separate pearl-like dots.
            thin.SetFloat("_FurBrightness",.5f);thin.SetColor("_FoamShadow",Color.Lerp(wash.shellRenderer.sharedMaterial.GetColor("_FoamShadow"),wash.shellRenderer.sharedMaterial.GetColor("_FoamColor"),.75f));
            var report=new Generated {dog=wash.name,sourcePath=AssetDatabase.GetAssetPath(source),sourceHash=AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source)).ToString(),
                requested=count,lobes=lobes.Count,vertices=lobes.Count*42,triangles=lobes.Count*80,minRadius=lobes.Min(l=>l.radius),maxRadius=lobes.Max(l=>l.radius),
                staticWeights=weights,anchors=lobes.Select(l=>l.vertex).ToArray(),rejectedSupports=rejected};
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out report.sourceGuid,out report.sourceFileId);
            var shell=wash.shellRenderer.sharedMaterial;string shellPath=AssetDatabase.GetAssetPath(shell);
            report.shellGuid=AssetDatabase.AssetPathToGUID(shellPath);report.shellHash=AssetDatabase.GetAssetDependencyHash(shellPath).ToString();
            report.meshScale=wash.meshToWorldScale;
            report.partMap=string.Join(",",wash.boneToPart.Select(v=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
            foreach(var l in lobes)report.byPart[l.part]++;
            File.WriteAllText(ManifestPath(wash),JsonUtility.ToJson(report,true));AssetDatabase.ImportAsset(ManifestPath(wash));
            return report;
        }

        static int[] Supports(int anchor,float radius,Vector3[] positions,Vector3[] normals,HashSet<int>[] graph,bool[] excluded)
        {
            var distances=new Dictionary<int,float>{{anchor,0}};var pending=new List<int>{anchor};var visited=new HashSet<int>();
            while(pending.Count>0)
            {
                int index=0;for(int i=1;i<pending.Count;i++)if(distances[pending[i]]<distances[pending[index]])index=i;
                int current=pending[index];pending.RemoveAt(index);if(!visited.Add(current))continue;
                foreach(int next in graph[current])
                {
                    if(excluded[next])continue;float d=distances[current]+Vector3.Distance(positions[current],positions[next]);
                    if(d>radius*1.8f)continue;
                    if(!distances.TryGetValue(next,out float previous)||d<previous){distances[next]=d;pending.Add(next);}
                }
            }
            Vector3 normal=normals[anchor],tangent=Vector3.Cross(normal,Mathf.Abs(normal.y)<.8f?Vector3.up:Vector3.right).normalized,bitangent=Vector3.Cross(normal,tangent);
            var result=new int[4];var directions=new[]{tangent,-tangent,bitangent,-bitangent};
            for(int side=0;side<4;side++)
            {
                float best=float.PositiveInfinity;int selected=-1;var target=positions[anchor]+directions[side]*radius;
                foreach(int vertex in visited)
                {
                    if(Vector3.Dot(positions[vertex]-positions[anchor],directions[side])<radius*.3f)continue;
                    float score=(positions[vertex]-target).sqrMagnitude;if(score<best){best=score;selected=vertex;}
                }
                if(selected<0||best>radius*radius)return null;result[side]=selected;
            }
            return result.Distinct().Count()==4?result:null;
        }
        static Material EnsureMaterial(string path,string shaderName)
        {
            var shader=Shader.Find(shaderName);if(shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException(shaderName);
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}else mat.shader=shader;return mat;
        }
    }
}
