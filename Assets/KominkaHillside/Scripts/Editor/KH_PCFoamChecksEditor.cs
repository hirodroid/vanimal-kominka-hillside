using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // Executes the real methods on disposable Edit objects; this does not emulate Udon VM scheduling/networking.
    public static class KH_PCFoamChecksEditor
    {
        const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        static void Set(object target,string field,object value)=>target.GetType().GetField(field,Flags).SetValue(target,value);
        static object Get(object target,string field)=>target.GetType().GetField(field,Flags).GetValue(target);
        static void Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Flags).Invoke(target,args);
        static void Require(bool pass,string name,StringBuilder log){if(!pass)throw new InvalidOperationException(name);log.AppendLine("PASS "+name);}

        [MenuItem("Tools/KominkaHillside/PC Foam/14 - Check Runtime Methods")]
        public static void Runtime()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit required");
            var production=SceneManager.GetActiveScene();bool dirty=production.isDirty;var scene=EditorSceneManager.NewPreviewScene();
            var owned=new List<UnityEngine.Object>();var log=new StringBuilder("Real C# methods in Edit; not Udon runtime or network acceptance.\n");
            T Own<T>(T value) where T:UnityEngine.Object {owned.Add(value);return value;}
            GameObject Obj(string name){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);return go;}
            KH_DogWash wash=null;
            try
            {
                var source=KH_PCFoamSetupEditor.Washes(production).First();
                wash=Obj("RuntimeProbe").AddComponent<KH_DogWash>();
                var shell=Obj("Shell").AddComponent<SkinnedMeshRenderer>();var foam=Obj("Foam").AddComponent<SkinnedMeshRenderer>();var ps=Obj("Runoff").AddComponent<ParticleSystem>();
                wash.shellRenderer=shell;foam.sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(KH_PCFoamGenerateEditor.MeshPath(source));
                foam.sharedMaterial=Own(new Material(AssetDatabase.LoadAssetAtPath<Material>(KH_PCFoamGenerateEditor.MaterialPath(source))));
                var original=Own(new Material(source.shellRenderer.sharedMaterial));var thin=Own(new Material(AssetDatabase.LoadAssetAtPath<Material>(KH_PCFoamGenerateEditor.ThinPath(source))));
                shell.sharedMaterial=original;Set(wash,"pcFoamRenderer",foam);Set(wash,"pcFoamRunoff",ps);Set(wash,"pcFoamThinMaterial",thin);Set(wash,"pcFoamFallbackShellMaterial",original);Set(wash,"pcFoamVersion",1);
                var main=ps.main;main.playOnAwake=false;main.maxParticles=16;main.simulationSpace=ParticleSystemSimulationSpace.World;main.scalingMode=ParticleSystemScalingMode.Shape;
                var emission=ps.emission;emission.enabled=false;ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                Call(wash,"PCFoamPrepare");var material=Own((Material)Get(wash,"_pcFoamMat"));
                Require((bool)Get(wash,"_pcFoamReady")&&shell.sharedMaterial==thin&&!foam.enabled,"ready before shell material cache",log);
                var a=Own(new RenderTexture(8,8,0));a.Create();var b=Own(new RenderTexture(8,8,0));b.Create();
                Set(wash,"_cur",a);Set(wash,"_next",b);Set(wash,"_idMask",Shader.PropertyToID("_Mask"));Set(wash,"_idPartFoamLimit",Shader.PropertyToID("_PartFoamLimit"));Set(wash,"_shellMat",thin);
                Call(wash,"PCFoamInitialize",new object[]{Enumerable.Repeat(1f,16).ToArray()});
                Require(material.GetTexture("_Mask")==a,"initial existing mask",log);Call(wash,"Swap");
                Require(material.GetTexture("_Mask")==b&&thin.GetTexture("_Mask")==b,"actual Swap binds both materials",log);
                Call(wash,"PCFoamTouch",KH_DogWash.ToolHand);Require(foam.enabled&&ps.particleCount==0,"replay/lather wakes without particles",log);
                Call(wash,"PCFoamEvaluate",0f);Call(wash,"PCFoamTime",Time.time+1000f);
                Require(foam.enabled,"zero coarse cells plus long washing cannot hide residual mask",log);
                Call(wash,"PCFoamRestored",false);Require(!foam.enabled,"clear restore hides",log);Call(wash,"PCFoamRestored",true);Require(foam.enabled,"foam restore wakes",log);
                Call(wash,"PCFoamContact",KH_DogWash.ToolTowel,0,Vector3.zero,.25f,1f);Require(ps.particleCount==0,"non-shower does not emit",log);
                Call(wash,"PCFoamContact",KH_DogWash.ToolShower,0,Vector3.zero,.25f,.2f);Require(ps.particleCount==0,"no runoff below foam threshold",log);
                void ParticleState(string step)
                {
                    var buffer=new ParticleSystem.Particle[16];int fetched=ps.GetParticles(buffer);
                    log.AppendLine(step+" time="+Time.time.ToString("R")+" next="+Get(wash,"_pcFoamNextEmit")+" contact="+Get(wash,"_pcFoamLastContact")+" acc="+Get(wash,"_pcFoamAcc")
                        +" count="+ps.particleCount+" fetched="+fetched+" playing="+ps.isPlaying+" stopped="+ps.isStopped+" paused="+ps.isPaused+" floatStep="+(Time.time+1f/12f>Time.time));
                }
                ps.Emit(new ParticleSystem.EmitParams{startLifetime=.5f,startSize=.015f,position=Vector3.zero},1);ParticleState("direct Emit");
                ps.Simulate(.001f,true,false,false);ParticleState("after explicit Edit simulate (no restart)");
                ps.Emit(new ParticleSystem.EmitParams{startLifetime=.5f,startSize=.015f,position=Vector3.zero},1);ParticleState("direct Emit after simulate");
                Call(wash,"PCFoamClearParticles");
                // A stopped preview-scene PS does not accept Emit until its native simulation is initialized.
                // This is only Edit fixture setup; production relies on Unity's frame simulation.
                ps.Simulate(.001f,true,false,false);
                for(int i=0;i<40;i++){Call(wash,"PCFoamContact",KH_DogWash.ToolShower,0,Vector3.zero,.25f,1f);if(i==0||i==39)ParticleState("contact "+i);}
                Require(ps.particleCount==1,"same-time burst is throttled to one particle (actual="+ps.particleCount+")",log);
                var particles=new ParticleSystem.Particle[16];ps.GetParticles(particles);
                Require(particles[0].startSize>=.008f&&particles[0].startSize<=.025f&&particles[0].startLifetime>=.25f&&particles[0].startLifetime<=.6f,"world size and lifetime bounded",log);
                for(int i=0;i<30;i++){Set(wash,"_pcFoamNextEmit",0f);Call(wash,"PCFoamContact",KH_DogWash.ToolShower,0,Vector3.zero,.25f,1f);}
                Require(ps.particleCount==16&&(float)Get(wash,"_pcFoamAcc")<1f,"capacity 16 and denied requests discarded",log);
                Call(wash,"PCFoamReset");Require(ps.particleCount==0&&foam.enabled,"epoch reset clears transient particles without premature mask hide",log);
                wash.dogRoot=Obj("Dog").transform;Call(wash,"PCFoamTime",Time.time);ps.Simulate(.001f,true,false,false);ps.Emit(1);Require(ps.particleCount>0,"warp fixture contains a live particle",log);wash.dogRoot.position=Vector3.one*10;Call(wash,"PCFoamTime",Time.time+.5f);
                Require(ps.particleCount==0,"warp clears world-space runoff",log);
                Set(wash,"_cellsDirty",false);Set(wash,"_lastWetActivity",Time.time-10000f);Set(wash,"_maskMat",Own(new Material(source.maskMaterial)));Set(wash,"_idDecay",Shader.PropertyToID("_Decay"));
                Call(wash,"Decay",4f);Require(!foam.enabled&&(bool)Get(wash,"_dryFinished"),"actual final clearing Blit disables volume",log);
                Set(wash,"pcFoamRunoff",null);shell.sharedMaterial=thin;Call(wash,"PCFoamPrepare");
                Require(!(bool)Get(wash,"_pcFoamReady")&&shell.sharedMaterial==original&&!foam.enabled,"missing dependency restores original before cache",log);
                log.AppendLine("PASS all runtime method checks");
            }
            catch(Exception ex){log.AppendLine("FAIL "+ex);Debug.LogError(ex);}
            finally
            {
                RenderTexture.active=null;EditorSceneManager.ClosePreviewScene(scene);foreach(var value in owned)if(value!=null)UnityEngine.Object.DestroyImmediate(value);
                if(production.isDirty!=dirty)log.AppendLine("FAIL production dirty changed");
                Directory.CreateDirectory("Temp/PCFoamProof");File.WriteAllText("Temp/PCFoamProof/runtime-checks.txt",log.ToString());
            }
        }

        [MenuItem("Tools/KominkaHillside/PC Foam/22 - Check Build Copies")]
        public static void Builds()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit required");
            var production=SceneManager.GetActiveScene();bool dirty=production.isDirty;var roots=production.GetRootGameObjects();var log=new StringBuilder("Build processor on disposable scene copies; not an uploaded mobile bundle.\n");
            try
            {
                foreach(string scenario in new[]{"PC","Android","iOS","renamed","wrong-mesh","stale-scale"})
                {
                    var scene=EditorSceneManager.NewPreviewScene();
                    try
                    {
                        var container=new GameObject("BuildCopy");SceneManager.MoveGameObjectToScene(container,scene);
                        foreach(var root in roots)UnityEngine.Object.Instantiate(root,container.transform,false).name=root.name;
                        var washes=KH_PCFoamSetupEditor.Washes(scene);Require(washes.Length==6,scenario+" six copied washes",log);
                        foreach(var w in washes)Require(w.shellRenderer.gameObject.scene==scene&&KH_PCFoamAuditEditor.Body(w).gameObject.scene==scene,scenario+" references stay in copy "+w.name,log);
                        var first=washes[0];bool negative=scenario=="renamed"||scenario=="wrong-mesh"||scenario=="stale-scale";
                        if(scenario=="renamed")KH_PCFoamSetupEditor.Root(first).name="RenamedFoam";
                        if(scenario=="wrong-mesh")KH_PCFoamSetupEditor.Root(first).GetComponent<SkinnedMeshRenderer>().sharedMesh=KH_PCFoamAuditEditor.Body(first).sharedMesh;
                        if(scenario=="stale-scale")first.meshToWorldScale*=1.01f;
                        var target=scenario=="PC"?BuildTarget.StandaloneWindows64:scenario=="iOS"?BuildTarget.iOS:BuildTarget.Android;
                        bool rejected=false;int count=0;
                        try{count=KH_PCFoamBuildEditor.ProcessCopy(scene,target);}catch(Exception ex){if(!negative)throw;rejected=true;log.AppendLine("Expected rejection: "+ex.Message);}
                        if(negative){Require(rejected,scenario+" rejected",log);continue;}
                        Require(count==6,scenario+" processed six",log);
                        foreach(var w in washes)
                        {
                            Require(w.shellRenderer.sharedMaterial.shader.name==(scenario=="PC"?"KominkaHillside/Wash Shell PC Thin":"KominkaHillside/Wash Shell"),scenario+" shell "+w.name,log);
                            if(scenario!="PC")
                            {
                                Require(KH_PCFoamSetupEditor.Root(w)==null,scenario+" root removed "+w.name,log);
                                var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(w);var so=new SerializedObject(w);
                                foreach(var field in KH_PCFoamSetupEditor.Fields)
                                {
                                    var p=so.FindProperty(field);Require(p==null||(p.propertyType==SerializedPropertyType.ObjectReference?p.objectReferenceValue==null:p.intValue==0),scenario+" proxy cleared "+field,log);
                                    Require(!backing.publicVariables.TryGetVariableValue(field,out object value),scenario+" backing removed "+field,log);
                                }
                            }
                        }
                    }
                    finally{EditorSceneManager.ClosePreviewScene(scene);}
                }
                log.AppendLine("PASS all build copy checks");
            }
            catch(Exception ex){log.AppendLine("FAIL "+ex);Debug.LogError(ex);}
            finally{if(production.isDirty!=dirty)log.AppendLine("FAIL production dirty changed");Directory.CreateDirectory("Temp/PCFoamProof");File.WriteAllText("Temp/PCFoamProof/build-checks.txt",log.ToString());}
        }
    }
}
