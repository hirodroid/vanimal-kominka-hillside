using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    public static class KH_PCFoamContinuousChecksEditor
    {
        [MenuItem("Tools/KominkaHillside/PC Foam/35 - Audit Body Materials")]
        static void Materials()
        {
            var log=new System.Text.StringBuilder();
            foreach(var w in KH_PCFoamSetupEditor.Washes(SceneManager.GetActiveScene()))
                foreach(var m in KH_PCFoamAuditEditor.Body(w).sharedMaterials)
                {
                    log.AppendLine(w.name+" "+AssetDatabase.GetAssetPath(m)+" "+m.shader.name);
                    foreach(var p in m.shader.GetPropertyCount()>0?Enumerable.Range(0,m.shader.GetPropertyCount()):Enumerable.Empty<int>())
                        if(m.shader.GetPropertyName(p).ToLowerInvariant().Contains("fur"))
                        {var n=m.shader.GetPropertyName(p);var t=m.shader.GetPropertyType(p);log.AppendLine(n+" "+(t==ShaderPropertyType.Float||t==ShaderPropertyType.Range?m.GetFloat(n).ToString():t==ShaderPropertyType.Vector?m.GetVector(n).ToString():t==ShaderPropertyType.Texture?AssetDatabase.GetAssetPath(m.GetTexture(n)):t.ToString()));}
                }
            File.WriteAllText("Temp/PCFoamV3/materials.txt",log.ToString());
        }
        [Serializable] public class Dog
        {
            public string name; public int vertices,triangles,sourceTriangles,thinPasses;
            public float skinError,rotatedSkinError,seamGap,maximumOffset;
            public List<int> coverage=new List<int>(); public int bandCoverage;
        }
        [Serializable] public class Report { public string version="continuous-v3",result,error;public List<Dog> dogs=new List<Dog>(); }
        static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}

        [MenuItem("Tools/KominkaHillside/PC Foam/33 - Check Continuous Geometry")]
        public static void Run()=>Run(null);
        [MenuItem("Tools/KominkaHillside/PC Foam/36 - Check Bundled Geometry")]
        public static void Bundle()
        {
            var proof=JsonUtility.FromJson<KH_PCFoamBundleProofEditor.Report>(File.ReadAllText("Temp/PCFoamProof/bundle-report-StandaloneWindows64.json"));
            Require(proof.status=="PASS","Current Windows bundle required");
            var bundle=AssetBundle.LoadFromFile("Temp/PCFoamProof/bundles/"+proof.run+"/group/foam-proof");
            Require(bundle!=null,"Group bundle loads");
            try{Run(bundle);}finally{bundle.Unload(true);}
        }
        static void Run(AssetBundle bundle)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit required");
            var production=SceneManager.GetActiveScene();bool dirty=production.isDirty;var report=new Report();
            try
            {
                foreach(var wash in KH_PCFoamSetupEditor.Washes(production))report.dogs.Add(Check(wash,bundle));
                Require(report.dogs.Count==6,"Six dogs required");report.result="PASS";
            }
            catch(Exception ex){report.result="FAIL";report.error=ex.ToString();Debug.LogError(ex);}
            if(production.isDirty!=dirty){report.result="FAIL";report.error+=" Production dirty changed";}
            Directory.CreateDirectory("Temp/PCFoamV3");File.WriteAllText("Temp/PCFoamV3/"+(bundle==null?"geometry":"geometry-bundle")+".json",JsonUtility.ToJson(report,true));
        }

        static Dog Check(KH_DogWash wash,AssetBundle bundle)
        {
            KH_PCFoamSetupEditor.ValidateAssets(wash);
            var body=KH_PCFoamAuditEditor.Body(wash);var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(KH_PCFoamGenerateEditor.MeshPath(wash));
            if(bundle!=null)mesh=bundle.LoadAsset<Mesh>(KH_PCFoamGenerateEditor.MeshPath(wash));
            Require(mesh!=null,"Foam mesh retained");
            var manifest=KH_PCFoamSetupEditor.ReadManifest(wash);
            var result=new Dog{name=wash.name,vertices=mesh.vertexCount,triangles=mesh.triangles.Length/3,sourceTriangles=body.sharedMesh.triangles.Length/3,maximumOffset=manifest.maxOffset};
            var config=new List<Vector4>();mesh.GetUVs(1,config);var parts=new List<Vector2>();mesh.GetUVs(2,parts);
            Require(config.Count==mesh.vertexCount&&mesh.tangents.Length==mesh.vertexCount&&parts.Count==mesh.vertexCount,"Saved mesh streams");
            if(bundle!=null)
            {
                var authored=AssetDatabase.LoadAssetAtPath<Mesh>(KH_PCFoamGenerateEditor.MeshPath(wash));
                var authoredConfig=new List<Vector4>();authored.GetUVs(1,authoredConfig);var authoredParts=new List<Vector2>();authored.GetUVs(2,authoredParts);
                Require(mesh.vertices.SequenceEqual(authored.vertices)&&mesh.normals.SequenceEqual(authored.normals)&&mesh.tangents.SequenceEqual(authored.tangents)
                    &&mesh.uv.SequenceEqual(authored.uv)&&mesh.triangles.SequenceEqual(authored.triangles)&&config.SequenceEqual(authoredConfig)&&parts.SequenceEqual(authoredParts),"Bundle matches current authored streams");
            }
            var p=mesh.vertices;var n=mesh.normals;var weights=mesh.boneWeights;
            foreach(int i in mesh.triangles)
            {
                Require(parts[i].x!=7,"No thick face geometry");
                Require(config[i].z>=0&&config[i].z+config[i].w<=manifest.maxOffset+.000001f,"Displacement bounds");
                Require(n[i].sqrMagnitude>.99f&&n[i].sqrMagnitude<1.01f,"Unit displacement direction");
            }
            var used=mesh.triangles.Distinct().ToArray();
            var sourceWeights=body.sharedMesh.boneWeights;
            string WeightKey(BoneWeight w)
            {
                var bones=new[]{w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};var amount=new[]{w.weight0,w.weight1,w.weight2,w.weight3};
                return string.Join("/",Enumerable.Range(0,4).Where(i=>amount[i]>0).OrderBy(i=>bones[i]).Select(i=>bones[i]+":"+amount[i].ToString("R")));
            }
            foreach(int i in used)
            {
                int a=manifest.parentA[i],b=manifest.parentB[i];
                Require(a>=0&&a<body.sharedMesh.vertexCount&&b>=0&&b<body.sharedMesh.vertexCount,"Source edge mapping");
                Require(WeightKey(weights[i])==WeightKey(sourceWeights[a])&&WeightKey(weights[i])==WeightKey(sourceWeights[b]),"Preserve source effective weights");
            }
            // Read actual saved streams. A stale/incorrect manifest seamGap cannot make this pass.
            var groups=used.GroupBy(i=>p[i].x.ToString("R")+":"+p[i].y.ToString("R")+":"+p[i].z.ToString("R")+":"+WeightKey(weights[i]));
            foreach(var group in groups)
            {
                int reference=group.First();
                foreach(int i in group)
                {
                    Require(config[i]==config[reference]&&parts[i]==parts[reference],"Seam canonical UV/config/part agree");
                    foreach(float amount in new[]{0,.15f,.3f,.5f,.75f,1})
                    {
                        float f=Mathf.InverseLerp(.12f,.75f,amount);float growth=f*f*(3-2*f);
                        var a=p[i]+n[i]*((config[i].w+config[i].z*growth)/wash.meshToWorldScale);
                        var b=p[reference]+n[reference]*((config[reference].w+config[reference].z*growth)/wash.meshToWorldScale);
                        result.seamGap=Mathf.Max(result.seamGap,Vector3.Distance(a,b)*wash.meshToWorldScale);
                    }
                }
            }
            Require(result.seamGap<.00001f,"Saved stream seam agreement at all growth steps (band shares canonical sample)");
            var scene=EditorSceneManager.NewPreviewScene();var owned=new List<UnityEngine.Object>();
            T Own<T>(T o) where T:UnityEngine.Object{o.hideFlags=HideFlags.HideAndDontSave;owned.Add(o);return o;}
            GameObject Obj(string name){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);return go;}
            try
            {
                var clones=new Dictionary<Transform,Transform>();
                Transform Clone(Transform original)
                {
                    if(original==null)return null;if(clones.TryGetValue(original,out var found))return found;
                    var parent=Clone(original.parent);var t=Obj(original.name).transform;t.SetParent(parent,false);
                    t.localPosition=original.localPosition;t.localRotation=original.localRotation;t.localScale=original.localScale;clones.Add(original,t);return t;
                }
                var renderer=Clone(body.transform).gameObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh=mesh;renderer.bones=body.bones.Select(Clone).ToArray();renderer.rootBone=Clone(body.rootBone);
                renderer.quality=body.quality;renderer.localBounds=body.localBounds;renderer.updateWhenOffscreen=true;renderer.forceMatrixRecalculationPerRender=true;
                renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                var referenceGo=Obj("OriginalBodyReference");referenceGo.transform.SetParent(renderer.transform.parent,false);
                referenceGo.transform.localPosition=renderer.transform.localPosition;referenceGo.transform.localRotation=renderer.transform.localRotation;referenceGo.transform.localScale=renderer.transform.localScale;
                var referenceBody=referenceGo.AddComponent<SkinnedMeshRenderer>();referenceBody.sharedMesh=body.sharedMesh;referenceBody.bones=renderer.bones;referenceBody.rootBone=renderer.rootBone;
                referenceBody.quality=body.quality;referenceBody.enabled=false;
                for(int s=0;s<body.sharedMesh.blendShapeCount;s++)referenceBody.SetBlendShapeWeight(s,body.GetBlendShapeWeight(s));
                var sourceMaterial=bundle==null?AssetDatabase.LoadAssetAtPath<Material>(KH_PCFoamGenerateEditor.MaterialPath(wash)):bundle.LoadAsset<Material>(KH_PCFoamGenerateEditor.MaterialPath(wash));
                Require(sourceMaterial!=null,"Foam material retained");
                var mat=Own(new Material(sourceMaterial));renderer.sharedMaterial=mat;
                var thinSource=bundle==null?AssetDatabase.LoadAssetAtPath<Material>(KH_PCFoamGenerateEditor.ThinPath(wash)):bundle.LoadAsset<Material>(KH_PCFoamGenerateEditor.ThinPath(wash));
                Require(thinSource!=null,"Thin material retained");
                var thin=Own(new Material(thinSource));result.thinPasses=thin.passCount;
                Require(thin.passCount==2&&thin.GetPassName(0)=="BASE"&&thin.GetPassName(1)=="LATHER","No granular legacy passes");
                Require(!ShaderUtil.ShaderHasError(mat.shader)&&!ShaderUtil.ShaderHasError(thin.shader),"Shader compile");
                var bake=Own(new Mesh());var originalBake=Own(new Mesh());
                float SkinError()
                {
                    renderer.BakeMesh(bake,true);referenceBody.BakeMesh(originalBake,true);float error=0;var actual=bake.vertices;var original=originalBake.vertices;
                    foreach(int i in used)
                    {
                        var expected=referenceBody.transform.TransformPoint((original[manifest.parentA[i]]+original[manifest.parentB[i]])*.5f);
                        error=Mathf.Max(error,Vector3.Distance(expected,renderer.transform.TransformPoint(actual[i])));
                    }
                    return error;
                }
                result.skinError=SkinError();Require(result.skinError<.0001f,"Actual skin matches original body and source edges");
                var rotated=renderer.bones[weights[manifest.anchors[0]].boneIndex0];var before=rotated.localRotation;
                rotated.localRotation=before*Quaternion.Euler(13,-8,7);result.rotatedSkinError=SkinError();rotated.localRotation=before;
                Require(result.rotatedSkinError<.0001f,"Rotated bone skin");renderer.BakeMesh(bake,true);
                var actualWorld=bake.vertices.Select(renderer.transform.TransformPoint).ToArray();var bounds=new Bounds(actualWorld[0],Vector3.zero);foreach(var v in actualWorld)bounds.Encapsulate(v);
                var camera=Obj("Camera").AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.orthographic=true;camera.orthographicSize=bounds.extents.magnitude*1.12f;
                camera.transform.position=bounds.center+new Vector3(2,.3f,1);camera.transform.LookAt(bounds.center);camera.nearClipPlane=.01f;camera.farClipPlane=10;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                var light=Obj("Light").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(40,-30,0);
                var rt=Own(new RenderTexture(256,256,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear));rt.Create();camera.targetTexture=rt;
                var read=Own(new Texture2D(256,256,TextureFormat.RGBA32,false,true));var mask=Own(new Texture2D(64,64,TextureFormat.RGBA32,false,true));mask.wrapMode=TextureWrapMode.Clamp;mask.filterMode=FilterMode.Bilinear;
                mat.SetTexture("_Mask",mask);mat.SetFloatArray("_PartFoamLimit",Enumerable.Repeat(1f,16).ToArray());
                int Render(float amount,bool band=false)
                {
                    var pixels=new Color[64*64];for(int y=0;y<64;y++)for(int x=0;x<64;x++)pixels[y*64+x]=new Color(0,band&&x>20&&x<42?0:amount,0,1);
                    mask.SetPixels(pixels);mask.Apply();camera.Render();var previous=RenderTexture.active;
                    try{RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,256,256),0,0);read.Apply();}finally{RenderTexture.active=previous;}
                    return read.GetPixels32().Count(c=>c.r>8||c.g>8||c.b>8);
                }
                foreach(float amount in new[]{0,.05f,.15f,.3f,.5f,.75f,1})result.coverage.Add(Render(amount));
                Require(result.coverage[0]==0&&result.coverage.Last()>100,"Zero clears, full renders");
                for(int i=1;i<result.coverage.Count;i++)Require(result.coverage[i]>=result.coverage[i-1],"Monotonic coverage at mask step "+i);
                result.bandCoverage=Render(1,true);Require(result.bandCoverage>0&&result.bandCoverage<result.coverage.Last(),"Rinsing band removes surface pixels");
            }
            finally{RenderTexture.active=null;EditorSceneManager.ClosePreviewScene(scene);foreach(var o in owned)if(o!=null)UnityEngine.Object.DestroyImmediate(o);}
            return result;
        }
    }
}
