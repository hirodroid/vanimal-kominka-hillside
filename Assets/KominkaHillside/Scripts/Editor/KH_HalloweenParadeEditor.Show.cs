using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEditor;
using UnityEditor.SceneManagement;
using KominkaDogRun;

namespace KominkaHillside.Editor
{
    public static partial class KH_HalloweenParadeEditor
    {
        const string RestorationPath="docs/plans/halloween-parade/runtime-restoration.json";
        [MenuItem(Menu+"7 - Bind Authored Show")]
        public static void BindShow()
        {
            Guard();var p=Only<KH_HalloweenParadePlayback>();var route=ReadRoute();
            var director=All<PlayableDirector>().Single(d=>d.name=="LiveStage_kh-halloween-parade");
            var data=JsonUtility.FromJson<Restoration>(File.ReadAllText(RestorationPath));
            var timeline=director.playableAsset as TimelineAsset;if(timeline==null)throw new Exception("Timeline missing");
            var audioTracks=timeline.GetOutputTracks().OfType<AudioTrack>().ToArray();
            if(audioTracks.Length!=1)throw new Exception("Expected single authored audio track");
            var music=director.GetGenericBinding(audioTracks[0]) as AudioSource;
            var clips=audioTracks[0].GetClips().ToArray();
            if(music==null||clips.Length!=1||!(((AudioPlayableAsset)clips[0].asset).clip is AudioClip audio)||Math.Abs(audio.length-220)>.02)throw new Exception("Expected 220-second authored music");
            var effects=new List<ParticleSystem>();var details=new List<Renderer>();
            foreach(var item in data.systems)
            {
                var t=director.transform.Find(item.instancePath);var ps=t!=null?t.GetComponent<ParticleSystem>():null;
                if(ps==null)throw new Exception("Missing authored system "+item.instancePath);
                RequireRestorable(ps,item);
                effects.Add(ps);if(item.phraseId!="guide-journey"&&!item.phraseId.StartsWith("lantern-"))details.Add(ps.GetComponent<ParticleSystemRenderer>());
            }
            if(effects.Distinct().Count()!=effects.Count||director.GetComponentsInChildren<ParticleSystem>(true).Count(ps=>ps.emission.enabled)!=effects.Count)throw new Exception("Restoration must cover each emitting system once");
            var importer=(AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(audio));
            var settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.Streaming;settings.compressionFormat=AudioCompressionFormat.Vorbis;settings.quality=.65f;
            importer.defaultSampleSettings=settings;importer.forceToMono=false;importer.SaveAndReimport();
            director.transform.SetPositionAndRotation(route.stage,Quaternion.Euler(0,route.yaw,0));director.playOnAwake=false;director.extrapolationMode=DirectorWrapMode.None;director.timeUpdateMode=DirectorUpdateMode.GameTime;
            music.playOnAwake=false;music.loop=false;music.spatialBlend=0;music.volume=0;music.dopplerLevel=0;
            p.stageRoot=director.gameObject;p.director=director;p.music=music;p.bgmPlayer=Only<KD_BgmPlayer>();p.effects=effects.ToArray();p.detailRenderers=details.ToArray();
            p.nightMode=Only<KH_NightMode>();
            p.effectStarts=data.systems.Select(x=>x.startSeconds).ToArray();p.effectEnds=data.systems.Select(x=>x.endSeconds).ToArray();
            for(int i=0;i<p.effects.Length;i++){p.effects[i].useAutoRandomSeed=false;p.effects[i].randomSeed=data.systems[i].seed;p.effects[i].Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);if(p.effects[i].useAutoRandomSeed||p.effects[i].randomSeed!=data.systems[i].seed)throw new Exception("Seed not retained");}
            KH_ParadePurchasedAssets.ApplyPumpkins(director.transform);
            KH_ParadePurchasedAssets.ApplyLanternFinish(p);
            director.gameObject.SetActive(false);Copy(p);EditorUtility.SetDirty(director);EditorUtility.SetDirty(music);
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("PARADE_SHOW_BIND_OK effects="+effects.Count+" detail="+details.Count+" audio="+audio.length);
        }
        static void RequireRestorable(ParticleSystem ps,Effect item)
        {
            var main=ps.main;var em=ps.emission;var shape=ps.shape;var velocity=ps.velocityOverLifetime;
            bool ok=main.simulationSpace==ParticleSystemSimulationSpace.Local&&main.startSpeed.mode==ParticleSystemCurveMode.Constant&&main.startSpeed.constant==0
                &&main.startLifetime.mode==ParticleSystemCurveMode.Constant&&main.startDelay.mode==ParticleSystemCurveMode.Constant&&main.gravityModifier.mode==ParticleSystemCurveMode.Constant&&!main.prewarm
                &&main.startDelay.constantMax==0&&!main.loop&&main.simulationSpeed==1&&main.gravityModifier.constantMax==0
                &&main.maxParticles==item.burstCount&&em.rateOverTime.constantMax==0&&em.rateOverDistance.constantMax==0&&em.burstCount==1
                &&em.rateOverTime.mode==ParticleSystemCurveMode.Constant&&em.rateOverDistance.mode==ParticleSystemCurveMode.Constant&&!ps.limitVelocityOverLifetime.enabled
                &&Mathf.Abs(main.startLifetime.constant-(item.endSeconds-item.startSeconds))<.002f
                &&!ps.collision.enabled&&!ps.trigger.enabled&&!ps.subEmitters.enabled&&!ps.trails.enabled&&!ps.externalForces.enabled&&!ps.forceOverLifetime.enabled&&!ps.inheritVelocity.enabled&&!ps.noise.enabled;
            if(em.burstCount==1){var b=em.GetBurst(0);ok&=b.time==0&&((b.count.mode==ParticleSystemCurveMode.Constant&&b.count.constant==item.burstCount)||(b.count.mode==ParticleSystemCurveMode.TwoConstants&&b.count.constantMin==item.burstCount&&b.count.constantMax==item.burstCount))&&b.cycleCount==1&&b.probability==1;}
            ok&=shape.enabled==item.shapeEnabled;
            if(shape.enabled)ok&=shape.shapeType==ParticleSystemShapeType.Box&&Vector3.Distance(shape.scale,new Vector3(item.shapeScale[0],item.shapeScale[1],item.shapeScale[2]))<.001f&&Vector3.Distance(shape.position,new Vector3(item.shapePosition[0],item.shapePosition[1],item.shapePosition[2]))<.001f&&shape.rotation==Vector3.zero;
            if(velocity.enabled)ok&=velocity.space==ParticleSystemSimulationSpace.Local&&velocity.x.mode==ParticleSystemCurveMode.Constant&&velocity.y.mode==ParticleSystemCurveMode.Constant&&velocity.z.mode==ParticleSystemCurveMode.Constant&&velocity.orbitalX.constantMax==0&&velocity.orbitalY.constantMax==0&&velocity.orbitalZ.constantMax==0&&velocity.radial.constantMax==0&&velocity.speedModifier.constant==1&&Vector3.Distance(new Vector3(velocity.x.constant,velocity.y.constant,velocity.z.constant),new Vector3(item.velocity[0],item.velocity[1],item.velocity[2]))<.001f;
            else ok&=item.velocity.All(x=>x==0);
            if(!ok)throw new Exception("Unsupported seek restoration "+item.instancePath);
        }
        [MenuItem(Menu+"9 - Verify Show and Backing Values")]
        public static void VerifyShow()
        {
            Verify();var p=Only<KH_HalloweenParadePlayback>();var m=p.parade;var errors=new List<string>();Action<bool,string> need=(ok,msg)=>{if(!ok)errors.Add(msg);};
            need(p.stageRoot!=null&&p.director!=null&&p.music!=null&&p.nightMode!=null,"playback/night references");
            if(p.director!=null){need(!p.director.playOnAwake&&p.director.extrapolationMode==DirectorWrapMode.None,"no autonomous looping");need(Vector3.Distance(p.director.transform.position,ReadRoute().stage)<.001f,"stage transform");}
            need(p.effects!=null&&p.effects.Length==p.effectStarts.Length&&p.effects.Length==p.effectEnds.Length,"restore arrays");
            need(p.music!=null&&!p.music.playOnAwake&&!p.music.loop&&p.music.spatialBlend==0,"single local audio");
            need(p.costumes!=null&&p.costumes.Length==6&&p.costumes.All(c=>c!=null&&!c.activeSelf),"six hidden ribbons");
            var subjects=new List<UdonSharp.UdonSharpBehaviour>{m,p,Only<KH_HalloweenParadePanel>(),m.leashes,m.activities,m.liveSystem};subjects.AddRange(m.dogs);subjects.AddRange(m.washStations);
            foreach(var subject in subjects)
            {
                var backing=UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(subject);
                need(backing!=null&&backing.programSource!=null,"backing source "+subject.name);if(backing==null)continue;
                foreach(var field in subject.GetType().GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public))
                {
                    if(field.DeclaringType!=subject.GetType()||field.IsStatic||field.IsInitOnly)continue;
                    // Existing behaviours retain their own synchronized runtime defaults; this setup
                    // owns only their newly added parade reference, not their unrelated state arrays.
                    if(!subject.GetType().Name.StartsWith("KH_HalloweenParade")&&field.Name!="parade")continue;
                    var expected=field.GetValue(subject);object actual;
                    if(expected==null||(expected is UnityEngine.Object unityObject&&unityObject==null))continue;
                    if(!backing.publicVariables.TryGetVariableValue(field.Name,out actual))
                    {need(false,"backing missing "+subject.GetType().Name+"."+field.Name);continue;}
                    need(BackingEqual(expected,actual),"backing mismatch "+subject.GetType().Name+"."+field.Name);
                }
            }
            Directory.CreateDirectory("Temp/halloween-parade");File.WriteAllText("Temp/halloween-parade/show-wiring.txt",errors.Count==0?"PASS show/backing references (not runtime or visual acceptance)":string.Join("\n",errors));
            if(errors.Count>0)throw new Exception(string.Join("; ",errors.Take(12)));Debug.Log("PARADE_SHOW_BACKING_OK subjects="+subjects.Count);
        }
        static bool BackingEqual(object expected,object actual)
        {
            if(expected is UdonSharp.UdonSharpBehaviour b)expected=UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            if(expected is Array a){if(!(actual is Array b2)||a.Length!=b2.Length)return false;for(int i=0;i<a.Length;i++)if(!BackingEqual(a.GetValue(i),b2.GetValue(i)))return false;return true;}
            return Equals(expected,actual);
        }
    }
}
