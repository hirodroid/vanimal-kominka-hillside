using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    public static class KH_PCFoamGenerateEditor
    {
        internal const int Version=2;
        internal const string Folder="Assets/KominkaHillside/Data/Wash/PCFoam";
        [Serializable] public class Generated
        {
            public string dog, sourcePath, sourceHash, sourceGuid, shellGuid, shellHash, partMap, generatorHash;
            public long sourceFileId; public float meshScale;
            public int version=Version, hillCount, vertices, triangles;
            public float maxOffset, seamGap; public int[] byPart=new int[9]; public float[] staticWeights;
            public int[] anchors, surfaceVertices, parentA, parentB;
        }
        [Serializable] public class Report {public string result,error;public List<Generated> dogs=new List<Generated>();}
        internal static string Key(KH_DogWash wash)=>wash.name.Replace("KH_Wash_","");
        internal static string MeshPath(KH_DogWash wash)=>Folder+"/KH_PCFoam_"+Key(wash)+".asset";
        internal static string MaterialPath(KH_DogWash wash)=>Folder+"/KH_PCFoam_"+Key(wash)+".mat";
        internal static string ThinPath(KH_DogWash wash)=>Folder+"/KH_PCThin_"+Key(wash)+".mat";
        internal static string ManifestPath(KH_DogWash wash)=>Folder+"/KH_PCFoam_"+Key(wash)+".json";
        internal static string Fingerprint()
        {
            using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                File.ReadAllText("Assets/KominkaHillside/Scripts/Editor/KH_PCFoamGenerateEditor.cs")+File.ReadAllText("Assets/KominkaHillside/Scripts/Editor/KH_PCFoamContinuousEditor.cs")))).Replace("-","");
        }
        [MenuItem("Tools/KominkaHillside/PC Foam/12 - Generate Six Dogs")]
        public static void Generate() => GenerateSelection(false);
        [MenuItem("Tools/KominkaHillside/PC Foam/31 - Generate Shiba Continuous")]
        public static void GenerateShiba() => GenerateSelection(true);
        static void GenerateSelection(bool shibaOnly)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit required");
            var report=new Report();
            try
            {
                if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/KominkaHillside/Data/Wash","PCFoam");
                foreach(var wash in KH_PCFoamSetupEditor.Washes(UnityEngine.SceneManagement.SceneManager.GetActiveScene()).Where(w=>!shibaOnly||Key(w)=="Shiba"))report.dogs.Add(Generate(wash));
                if(report.dogs.Count!=(shibaOnly?1:6))throw new InvalidOperationException("Unexpected dog count");
                report.result="PASS";
            }
            catch(Exception ex){report.result="FAIL";report.error=ex.ToString();Debug.LogError(ex);}
            Directory.CreateDirectory("Temp/PCFoamV3");File.WriteAllText("Temp/PCFoamV3/generation.json",JsonUtility.ToJson(report,true));
        }
        internal static Generated Generate(KH_DogWash wash)
        {
            if(EditorUtility.IsDirty(wash.shellRenderer.sharedMaterial))throw new InvalidOperationException("Unsaved shell material: "+wash.name);
            var body=KH_PCFoamAuditEditor.Body(wash);var source=body.sharedMesh;var shell=wash.shellRenderer.sharedMaterial;
            KH_PCFoamAuditEditor.Inspect(wash,out var excluded);
            var positions=source.vertices;var normals=source.normals;
            var weights=new float[source.blendShapeCount];var dp=new Vector3[source.vertexCount];var dn=new Vector3[source.vertexCount];var dt=new Vector3[source.vertexCount];
            for(int s=0;s<weights.Length;s++)
            {
                weights[s]=body.GetBlendShapeWeight(s);if(Mathf.Abs(weights[s])<.0001f)continue;
                if(source.GetBlendShapeFrameCount(s)!=1)throw new InvalidOperationException("Static multi-frame shape requires explicit interpolation: "+source.GetBlendShapeName(s));
                float frameWeight=source.GetBlendShapeFrameWeight(s,0);if(Mathf.Abs(frameWeight)<.001f)throw new InvalidOperationException("Zero morph frame weight");
                source.GetBlendShapeFrameVertices(s,0,dp,dn,dt);float factor=weights[s]/frameWeight;
                for(int i=0;i<positions.Length;i++){positions[i]+=dp[i]*factor;normals[i]+=dn[i]*factor;}
            }
            for(int i=0;i<normals.Length;i++)normals[i]=normals[i].normalized;
            var surface=KH_PCFoamContinuousEditor.Create(wash,positions,normals,excluded);
            var generated=surface.mesh;generated.name="KH_PCFoam_"+Key(wash);
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(wash));
            if(existing==null){AssetDatabase.CreateAsset(generated,MeshPath(wash));existing=generated;}
            else
            {
                // Reset the native layout as well as serialized data when replacing an older mesh schema.
                // CopySerialized left the previous skin buffer layout alive in Unity 2022.3.
                existing.Clear(false);existing.indexFormat=generated.indexFormat;
                existing.vertices=generated.vertices;existing.normals=generated.normals;existing.tangents=generated.tangents;existing.uv=generated.uv;
                var config=new List<Vector4>();generated.GetUVs(1,config);existing.SetUVs(1,config);
                var parts=new List<Vector2>();generated.GetUVs(2,parts);existing.SetUVs(2,parts);
                existing.boneWeights=generated.boneWeights;existing.bindposes=generated.bindposes;existing.triangles=generated.triangles;existing.bounds=generated.bounds;
                UnityEngine.Object.DestroyImmediate(generated);EditorUtility.SetDirty(existing);
            }
            AssetDatabase.SaveAssetIfDirty(existing);
            var mat=EnsureMaterial(MaterialPath(wash),"KominkaHillside/PC Foam");
            mat.SetTexture("_Mask",wash.maskA);mat.SetColor("_FoamColor",shell.GetColor("_FoamColor"));mat.SetFloat("_HillScale",1);
            var thin=EnsureMaterial(ThinPath(wash),"KominkaHillside/Wash Shell PC Thin");thin.CopyPropertiesFromMaterial(shell);thin.renderQueue=3010;
            thin.SetFloat("_CoatOffset",KH_PCFoamContinuousEditor.Coat(wash));
            thin.SetFloat("_FurCoatFloor",KH_PCFoamContinuousEditor.FurCoatFloor(wash));
            var bw=source.boneWeights;var head=Enumerable.Range(0,positions.Length).Where(i=>Mathf.RoundToInt(wash.boneToPart[bw[i].boneIndex0])==7).ToArray();
            var bounds=new Bounds(positions[head[0]],Vector3.zero);foreach(int i in head)bounds.Encapsulate(positions[i]);
            thin.SetVector("_HeadMin",bounds.min);thin.SetVector("_HeadInvSize",new Vector4(1/bounds.size.x,1/bounds.size.y,1/bounds.size.z,0));
            foreach(var material in new[]{mat,thin}){EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);}
            var report=new Generated {dog=wash.name,sourcePath=AssetDatabase.GetAssetPath(source),sourceHash=AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source)).ToString(),
                hillCount=3,vertices=existing.vertexCount,triangles=existing.triangles.Length/3,maxOffset=surface.maxHeight,seamGap=surface.maxSeamGap,
                staticWeights=weights,anchors=surface.anchors,surfaceVertices=surface.used,parentA=surface.parentA,parentB=surface.parentB,generatorHash=Fingerprint()};
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out report.sourceGuid,out report.sourceFileId);
            string shellPath=AssetDatabase.GetAssetPath(shell);report.shellGuid=AssetDatabase.AssetPathToGUID(shellPath);report.shellHash=AssetDatabase.GetAssetDependencyHash(shellPath).ToString();
            report.meshScale=wash.meshToWorldScale;report.partMap=string.Join(",",wash.boneToPart.Select(v=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
            foreach(int i in surface.used)report.byPart[Mathf.Clamp(Mathf.RoundToInt(wash.boneToPart[bw[i].boneIndex0]),0,8)]++;
            File.WriteAllText(ManifestPath(wash),JsonUtility.ToJson(report,true));AssetDatabase.ImportAsset(ManifestPath(wash));
            return report;
        }
        static Material EnsureMaterial(string path,string shaderName)
        {
            var shader=Shader.Find(shaderName);if(shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException(shaderName);
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}else mat.shader=shader;return mat;
        }
    }
}
