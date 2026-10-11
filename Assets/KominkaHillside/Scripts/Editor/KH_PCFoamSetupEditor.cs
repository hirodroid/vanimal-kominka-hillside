using System;
using System.IO;
using System.Linq;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    public static class KH_PCFoamSetupEditor
    {
        internal const string ScenePath="Assets/Scenes/VRCDefaultWorldScene.unity";
        internal const string Prefix="KH_PCFoam_";
        internal static readonly string[] Fields={"pcFoamRenderer","pcFoamRunoff","pcFoamThinMaterial","pcFoamFallbackShellMaterial","pcFoamVersion"};
        internal static KH_DogWash[] Washes(Scene scene)=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<KH_DogWash>(true)).OrderBy(w=>w.dogIndex).ToArray();
        internal static Transform Root(KH_DogWash wash)=>wash.shellRenderer.transform.parent.Find(Prefix+KH_PCFoamGenerateEditor.Key(wash));

        [MenuItem("Tools/KominkaHillside/PC Foam/20 - Apply Six Dogs")]
        public static void Apply()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=ScenePath)throw new InvalidOperationException("Expected production scene in Edit mode");
            if(scene.isDirty)throw new InvalidOperationException("Unsaved scene changes: inspect/save their owner before applying PC foam");
            var washes=Washes(scene);if(washes.Length!=6)throw new InvalidOperationException("Expected six dogs");
            foreach(var w in washes)ValidateAssets(w);
            var particleMesh=ParticleMesh();var particleMaterial=ParticleMaterial();
            foreach(var wash in washes)
            {
                var body=KH_PCFoamAuditEditor.Body(wash);var root=Root(wash);
                if(root==null){root=new GameObject(Prefix+KH_PCFoamGenerateEditor.Key(wash)).transform;Undo.RegisterCreatedObjectUndo(root.gameObject,"PC foam");root.SetParent(body.transform.parent,false);}
                root.localPosition=body.transform.localPosition;root.localRotation=body.transform.localRotation;root.localScale=body.transform.localScale;
                root.gameObject.layer=body.gameObject.layer;
                var renderer=root.GetComponent<SkinnedMeshRenderer>();if(renderer==null)renderer=root.gameObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(KH_PCFoamGenerateEditor.MeshPath(wash));renderer.bones=body.bones;renderer.rootBone=body.rootBone;renderer.quality=body.quality;
                renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(KH_PCFoamGenerateEditor.MaterialPath(wash));
                var manifest=ReadManifest(wash);var scale=(body.rootBone??body.transform).lossyScale;
                float smallest=Mathf.Min(Mathf.Abs(scale.x),Mathf.Min(Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
                var bounds=body.localBounds;bounds.Expand(2f*(manifest.maxRadius+wash.shellRenderer.sharedMaterial.GetFloat("_BaseOffset"))/Mathf.Max(smallest,.00001f));
                renderer.localBounds=bounds;renderer.updateWhenOffscreen=false;renderer.skinnedMotionVectors=false;
                renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.lightProbeUsage=body.lightProbeUsage;renderer.probeAnchor=body.probeAnchor;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;renderer.enabled=false;
                var runoff=root.Find("Runoff");if(runoff==null){runoff=new GameObject("Runoff").transform;runoff.SetParent(root,false);}
                runoff.gameObject.layer=body.gameObject.layer;
                var ps=runoff.GetComponent<ParticleSystem>();if(ps==null)ps=runoff.gameObject.AddComponent<ParticleSystem>();
                ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=ps.main;main.playOnAwake=false;main.loop=false;main.maxParticles=16;main.simulationSpace=ParticleSystemSimulationSpace.World;
                main.scalingMode=ParticleSystemScalingMode.Shape;main.startSize=.015f;main.startSpeed=0;main.startLifetime=.5f;main.gravityModifier=.15f;
                var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.enabled=false;
                var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,1,1,0));
                var collision=ps.collision;collision.enabled=false;var trails=ps.trails;trails.enabled=false;var lights=ps.lights;lights.enabled=false;var sub=ps.subEmitters;sub.enabled=false;
                var particleRenderer=ps.GetComponent<ParticleSystemRenderer>();particleRenderer.renderMode=ParticleSystemRenderMode.Mesh;particleRenderer.mesh=particleMesh;particleRenderer.sharedMaterial=particleMaterial;
                particleRenderer.shadowCastingMode=ShadowCastingMode.Off;particleRenderer.receiveShadows=false;particleRenderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
                var so=new SerializedObject(wash);
                so.FindProperty(Fields[0]).objectReferenceValue=renderer;so.FindProperty(Fields[1]).objectReferenceValue=ps;
                so.FindProperty(Fields[2]).objectReferenceValue=AssetDatabase.LoadAssetAtPath<Material>(KH_PCFoamGenerateEditor.ThinPath(wash));
                so.FindProperty(Fields[3]).objectReferenceValue=wash.shellRenderer.sharedMaterial;so.FindProperty(Fields[4]).intValue=KH_PCFoamGenerateEditor.Version;
                so.ApplyModifiedPropertiesWithoutUndo();UdonSharpEditorUtility.CopyProxyToUdon(wash);
                EditorUtility.SetDirty(wash);EditorUtility.SetDirty(renderer);EditorUtility.SetDirty(ps);
            }
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory("Temp/PCFoamProof");File.WriteAllText("Temp/PCFoamProof/apply.txt","Applied six dogs. Authoring shells retain original material. Run Verify, then save.\n");
        }

        internal static KH_PCFoamGenerateEditor.Generated ReadManifest(KH_DogWash wash)=>JsonUtility.FromJson<KH_PCFoamGenerateEditor.Generated>(File.ReadAllText(KH_PCFoamGenerateEditor.ManifestPath(wash)));
        internal static void ValidateAssets(KH_DogWash wash)
        {
            var body=KH_PCFoamAuditEditor.Body(wash);var manifest=ReadManifest(wash);
            if(manifest.version!=KH_PCFoamGenerateEditor.Version||manifest.sourceHash!=AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(body.sharedMesh)).ToString())throw new InvalidOperationException(wash.name+": regenerate stale mesh");
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(body.sharedMesh,out string guid,out long fileId);
            var shell=wash.shellRenderer.sharedMaterial;string shellPath=AssetDatabase.GetAssetPath(shell);
            if(manifest.sourceGuid!=guid||manifest.sourceFileId!=fileId||manifest.shellGuid!=AssetDatabase.AssetPathToGUID(shellPath)
                ||manifest.shellHash!=AssetDatabase.GetAssetDependencyHash(shellPath).ToString()||EditorUtility.IsDirty(shell)
                ||manifest.meshScale!=wash.meshToWorldScale||manifest.partMap!=string.Join(",",wash.boneToPart.Select(v=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture))))
                throw new InvalidOperationException(wash.name+": generation input changed; regenerate PC foam");
            KH_PCFoamAuditEditor.Inspect(wash,out var excluded);
            if(manifest.anchors.Any(i=>i<0||i>=excluded.Length||excluded[i]))throw new InvalidOperationException(wash.name+": new dynamic morph affects foam anchors");
            for(int i=0;i<manifest.staticWeights.Length;i++)if(Mathf.Abs(manifest.staticWeights[i]-body.GetBlendShapeWeight(i))>.001f)throw new InvalidOperationException(wash.name+": authored morph changed");
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(KH_PCFoamGenerateEditor.MeshPath(wash));
            if(mesh==null||mesh.vertexCount!=manifest.vertices||manifest.lobes>256||manifest.byPart.Where((n,i)=>i!=7).Any(n=>n==0))throw new InvalidOperationException(wash.name+": incomplete generated coverage");
            foreach(var path in new[]{KH_PCFoamGenerateEditor.MaterialPath(wash),KH_PCFoamGenerateEditor.ThinPath(wash)})
            {var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat==null||mat.shader==null||ShaderUtil.ShaderHasError(mat.shader))throw new InvalidOperationException("Missing/invalid "+path);}
            if(wash.shellRenderer.sharedMaterial==null||wash.shellRenderer.sharedMaterial.shader.name!="KominkaHillside/Wash Shell")throw new InvalidOperationException("Authoring shell must retain original material");
        }

        [MenuItem("Tools/KominkaHillside/PC Foam/21 - Verify Six Dogs")]
        public static void Verify()
        {
            var scene=SceneManager.GetActiveScene();var log=new System.Text.StringBuilder();int passed=0;
            try
            {
                if(scene.path!=ScenePath||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Production Edit scene required");
                var washes=Washes(scene);if(washes.Length!=6)throw new InvalidOperationException("Expected six dogs");
                foreach(var wash in washes)
                {
                    ValidateWiring(wash);var body=KH_PCFoamAuditEditor.Body(wash);var root=Root(wash);var so=new SerializedObject(wash);
                    var renderer=so.FindProperty(Fields[0]).objectReferenceValue as SkinnedMeshRenderer;var ps=so.FindProperty(Fields[1]).objectReferenceValue as ParticleSystem;
                    if(root==null||renderer==null||renderer.transform!=root||renderer.enabled||renderer.updateWhenOffscreen||!renderer.bones.SequenceEqual(body.bones)||renderer.rootBone!=body.rootBone)throw new InvalidOperationException(wash.name+": renderer wiring");
                    if(ps==null||!ps.transform.IsChildOf(root)||ps.main.maxParticles!=16||ps.emission.enabled||ps.main.simulationSpace!=ParticleSystemSimulationSpace.World||ps.main.scalingMode!=ParticleSystemScalingMode.Shape)throw new InvalidOperationException(wash.name+": runoff configuration");
                    if(so.FindProperty(Fields[3]).objectReferenceValue!=wash.shellRenderer.sharedMaterial||so.FindProperty(Fields[4]).intValue!=KH_PCFoamGenerateEditor.Version)throw new InvalidOperationException(wash.name+": fallback/version");
                    var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(wash);
                    foreach(var field in Fields.Take(4))
                    {if(!backing.publicVariables.TryGetVariableValue(field,out object stored)||(UnityEngine.Object)stored!=so.FindProperty(field).objectReferenceValue)throw new InvalidOperationException(wash.name+": backing override "+field);}
                    passed++;log.AppendLine(wash.name+" PASS mesh="+renderer.sharedMesh.vertexCount+" vertices runoff=16 originalShell="+wash.shellRenderer.sharedMaterial.name);
                }
                log.AppendLine("PASS six dogs; no runtime/PCVR performance claim");
            }
            catch(Exception ex){log.AppendLine("FAIL "+ex);Debug.LogError(ex);}
            Directory.CreateDirectory("Temp/PCFoamProof");File.WriteAllText("Temp/PCFoamProof/verify.txt","checked="+passed+"\n"+log);
        }

        internal static void ValidateWiring(KH_DogWash wash)
        {
            ValidateAssets(wash);var body=KH_PCFoamAuditEditor.Body(wash);var root=Root(wash);
            if(root==null)throw new InvalidOperationException(wash.name+": missing PC foam root");
            var renderer=root.GetComponent<SkinnedMeshRenderer>();var ps=root.GetComponentInChildren<ParticleSystem>(true);
            var pr=ps!=null?ps.GetComponent<ParticleSystemRenderer>():null;
            if(renderer==null||renderer.sharedMesh!=AssetDatabase.LoadAssetAtPath<Mesh>(KH_PCFoamGenerateEditor.MeshPath(wash))
                ||renderer.sharedMaterial!=AssetDatabase.LoadAssetAtPath<Material>(KH_PCFoamGenerateEditor.MaterialPath(wash))
                ||!renderer.bones.SequenceEqual(body.bones)||renderer.rootBone!=body.rootBone||renderer.quality!=body.quality||renderer.enabled||renderer.updateWhenOffscreen
                ||root.parent!=body.transform.parent||root.localPosition!=body.transform.localPosition||root.localRotation!=body.transform.localRotation||root.localScale!=body.transform.localScale)
                throw new InvalidOperationException(wash.name+": invalid attached foam renderer");
            var scale=(body.rootBone??body.transform).lossyScale;float smallest=Mathf.Min(Mathf.Abs(scale.x),Mathf.Min(Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
            var required=body.localBounds;required.Expand(2f*(ReadManifest(wash).maxRadius+wash.shellRenderer.sharedMaterial.GetFloat("_BaseOffset"))/Mathf.Max(smallest,.00001f));
            if(!renderer.localBounds.Contains(required.min)||!renderer.localBounds.Contains(required.max))throw new InvalidOperationException(wash.name+": foam bounds too small");
            if(pr==null||pr.mesh!=AssetDatabase.LoadAssetAtPath<Mesh>(KH_PCFoamGenerateEditor.Folder+"/KH_PCFoamRunoff.asset")
                ||pr.sharedMaterial!=AssetDatabase.LoadAssetAtPath<Material>(KH_PCFoamGenerateEditor.Folder+"/KH_PCFoamRunoff.mat")||pr.renderMode!=ParticleSystemRenderMode.Mesh
                ||ps.main.maxParticles!=16||ps.main.playOnAwake||ps.emission.enabled||ps.main.simulationSpace!=ParticleSystemSimulationSpace.World||ps.main.scalingMode!=ParticleSystemScalingMode.Shape)
                throw new InvalidOperationException(wash.name+": invalid runoff renderer/configuration");
            var so=new SerializedObject(wash);
            if(so.FindProperty(Fields[0])!=null && (so.FindProperty(Fields[0]).objectReferenceValue!=renderer||so.FindProperty(Fields[1]).objectReferenceValue!=ps
                ||so.FindProperty(Fields[2]).objectReferenceValue!=AssetDatabase.LoadAssetAtPath<Material>(KH_PCFoamGenerateEditor.ThinPath(wash))
                ||so.FindProperty(Fields[3]).objectReferenceValue!=wash.shellRenderer.sharedMaterial||so.FindProperty(Fields[4]).intValue!=KH_PCFoamGenerateEditor.Version))
                throw new InvalidOperationException(wash.name+": invalid PC foam references/version");
        }

        static Mesh ParticleMesh()
        {
            string path=KH_PCFoamGenerateEditor.Folder+"/KH_PCFoamRunoff.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh!=null)return mesh;
            KH_PCFoamMesh.Sphere(out var v,out var f);mesh=new Mesh{name="KH_PCFoamRunoff",vertices=v.Select(p=>p*.5f).ToArray(),normals=v,triangles=f};AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static Material ParticleMaterial()
        {
            string path=KH_PCFoamGenerateEditor.Folder+"/KH_PCFoamRunoff.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null){var shader=Shader.Find("KominkaHillside/PC Foam Runoff");if(shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Runoff shader");mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}return mat;
        }
    }
}
