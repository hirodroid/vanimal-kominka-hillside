using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace KominkaHillside.Editor
{
    // Edit-only checks on disposable copies of the actual imported systems.
    public static class KH_ParadeFestivalVerification
    {
        // Narrow update of generated show assets. Matches the source preset; no world/dog rebuild.
        [MenuItem("Tools/KominkaHillside/Parade/28 - Refine Festival Readability")]
        public static void Refine()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new Exception("Saved Edit scene required");
            var shader=Shader.Find("VirtualCampers/KH Pop Silhouette");if(shader==null||!shader.isSupported)throw new Exception("Pop shader missing");
            int banks=0;var mats=new HashSet<Material>();
            const string folder="Assets/VirtualCampers.LiveGenerator/Shows/kh-halloween-parade/Prefabs";
            foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{folder}))
            {
                var path=AssetDatabase.GUIDToAssetPath(guid);if(!Path.GetFileName(path).StartsWith("particles_trail_khp_star_fountain__pop-"))continue;
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var ps=root.transform.Find("Silhouette").GetComponent<ParticleSystem>();var main=ps.main;main.startSize=new ParticleSystem.MinMaxCurve(.08f,.19f);
                    var shape=ps.shape;shape.scale=new Vector3(1.3f,2.4f,5);shape.position=new Vector3(0,1.4f,0);
                    PrefabUtility.SaveAsPrefabAsset(root,path);banks++;
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            var playback=UnityEngine.Object.FindObjectOfType<KH_HalloweenParadePlayback>();
            foreach(var ps in playback.effects)if(ps.transform.parent.name.Contains("__pop-"))
            {
                var renderer=ps.GetComponent<ParticleSystemRenderer>();if(renderer.renderMode==ParticleSystemRenderMode.Mesh)continue;
                var mat=renderer.sharedMaterial;if(mat==null||!AssetDatabase.GetAssetPath(mat).StartsWith("Assets/VirtualCampers.LiveGenerator/Shows/kh-halloween-parade/"))throw new Exception("Unexpected material owner");
                if(mats.Add(mat)){mat.shader=shader;EditorUtility.SetDirty(mat);}
            }
            if(banks!=80)throw new Exception("Expected80 fountain banks, found "+banks);
            AssetDatabase.SaveAssets();Debug.Log("PARADE_FESTIVAL_READABILITY_OK banks="+banks+" materials="+mats.Count);
        }
        [Serializable] class Sample {public string preset;public int burst,observed,continuousCount;public float age,positionError,velocityError,rotationError,lifetimeError,sizeError,colorError;public bool pass,exactLifetimeBoundary;}
        [Serializable] class Peak {public float time;public int particles,systems,renderers;}
        [Serializable] class Report {public string utc,error;public bool pass,sceneClean;public List<Sample> samples=new List<Sample>();public List<Peak> peaks=new List<Peak>();public string limits="Edit Local linear modules: variable-step versus one-call seek/manual continuation. Exact lifetime +/-1ms count boundary is recorded separately; after that zero is mandatory. Play+manual Simulate is not normal frames/Timeline Activation. Not VR fps, multiplayer, audio, Animator or entertainment acceptance.";}
        [MenuItem("Tools/KominkaHillside/Parade/27 - Verify Festival Banks")]
        public static void Verify()
        {
            var original=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(original.isDirty||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Saved Edit scene required");
            var playback=UnityEngine.Object.FindObjectOfType<KH_HalloweenParadePlayback>();
            var preview=EditorSceneManager.NewPreviewScene();var report=new Report{utc=DateTime.UtcNow.ToString("o"),pass=true};
            try
            {
                foreach(var source in playback.effects.Where(p=>p.main.maxParticles>1).GroupBy(p=>p.transform.parent.name.Split(new[]{"__"},StringSplitOptions.None)[0]).Select(g=>g.First()))
                {
                    float life=source.main.startLifetime.constant,step=Time.fixedDeltaTime;
                    foreach(float requested in new[]{0f,step,step-.003f,step+.003f,life*.25f+.007f,life*.5f,life*.9f,life-.003f,life,life+.003f})
                    {
                        float age=requested;
                        var a=Clone(source,preview);var b=Clone(source,preview);
                        a.Simulate(0,false,true,false);Advance(a,age);
                        b.transform.position=a.transform.position;b.Simulate(age,false,true,false);
                        var result=Compare(a,b,source.transform.parent.name,age);report.samples.Add(result);report.pass&=result.pass;
                        // Manual continuation after Play; this is not normal-frame/Activation evidence.
                        a.Play(false);b.Play(false);
                        Advance(a,.5);b.Simulate(.5f,false,false,false);
                        var resumed=Compare(a,b,source.transform.parent.name+"/resume",age+.5f);report.samples.Add(resumed);report.pass&=resumed.pass;
                        UnityEngine.Object.DestroyImmediate(a.gameObject);UnityEngine.Object.DestroyImmediate(b.gameObject);
                    }
                }
                foreach(float time in new[]{.8f,35.8f,65.8f,75.8f,175.8f,185.8f,190.8f,210.8f,220.1f})
                {
                    var peak=new Peak{time=time};
                    for(int i=0;i<playback.effects.Length;i++)if(time>=playback.effectStarts[i]&&time<playback.effectEnds[i])
                    {
                        var ps=Clone(playback.effects[i],preview);ps.Simulate(time-playback.effectStarts[i],false,true,false);
                        peak.particles+=ps.particleCount;peak.systems++;if(ps.particleCount>0&&ps.GetComponent<ParticleSystemRenderer>().enabled)peak.renderers++;
                        UnityEngine.Object.DestroyImmediate(ps.gameObject);
                    }
                    report.peaks.Add(peak);
                }
                report.pass&=report.samples.Count>0;
            }
            catch(Exception e){report.pass=false;report.error=e.ToString();throw;}
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);report.sceneClean=!original.isDirty;
                Directory.CreateDirectory("docs/plans/halloween-parade/evidence/festival-v3");
                File.WriteAllText("docs/plans/halloween-parade/evidence/festival-v3/banks.json",JsonUtility.ToJson(report,true));
            }
            if(!report.pass||!report.sceneClean)throw new Exception("Festival bank comparison failed; inspect banks.json");
            Debug.Log("PARADE_FESTIVAL_BANKS_OK samples="+report.samples.Count+" peak="+report.peaks.Max(x=>x.particles));
        }
        static ParticleSystem Clone(ParticleSystem source,UnityEngine.SceneManagement.Scene scene)
        {
            var go=UnityEngine.Object.Instantiate(source.gameObject);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);go.SetActive(true);
            go.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);go.transform.localScale=source.transform.lossyScale;
            var ps=go.GetComponent<ParticleSystem>();ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);ps.useAutoRandomSeed=false;ps.randomSeed=source.randomSeed;return ps;
        }
        static void Advance(ParticleSystem ps,double duration)
        {
            double t=0;int k=0;double[] steps={1.0/60,1.0/30,.017};
            while(t<duration-1e-9){double dt=Math.Min(steps[k++%steps.Length],duration-t);ps.transform.position+=Vector3.right*(float)(dt*.2);ps.Simulate((float)dt,false,false,false);t+=dt;}
        }
        // Separate visual evidence; never changes the strict comparison's thresholds/result.
        [Serializable] class VisualSample {public string preset,method,image;public float age,distance,maxAlpha,maxSize;public int count;}
        [Serializable] class VisualReport {public string utc,limits="Edit images of the actual bank/material on the hillside; manual advancement, not normal Play/network acceptance. Strict banks.json remains independent.";public bool sceneClean;public List<VisualSample> samples=new List<VisualSample>();}
        [MenuItem("Tools/KominkaHillside/Parade/29 - Capture Seek Differences")]
        public static void CaptureSeekDifferences()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.isDirty||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Saved Edit scene required");
            var playback=UnityEngine.Object.FindObjectOfType<KH_HalloweenParadePlayback>();
            const string folder="docs/plans/halloween-parade/evidence/festival-v3/seek-visual";Directory.CreateDirectory(folder);
            var report=new VisualReport{utc=DateTime.UtcNow.ToString("o")};
            var root=new GameObject("KH_SeekEvidence"){hideFlags=HideFlags.HideAndDontSave};
            var go=new GameObject("Camera");go.transform.SetParent(root.transform);var camera=go.AddComponent<Camera>();
            var reference=UnityEngine.Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>().ReferenceCamera.GetComponent<Camera>();
            camera.CopyFrom(reference);camera.enabled=false;camera.stereoTargetEye=StereoTargetEyeMask.None;camera.fieldOfView=70;camera.aspect=16f/9;camera.nearClipPlane=.03f;camera.allowHDR=false;camera.allowMSAA=false;
            var rt=new RenderTexture(1280,720,24);rt.Create();camera.targetTexture=rt;var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            string[] names={"star_fountain","bat_flock","confetti_rain","bat_flock"};float[] ages={2.5f,3f,6.997f,5.997f};
            try
            {
                for(int c=0;c<names.Length;c++)foreach(float distance in new[]{1f,3f})foreach(float offset in new[]{-.05f,0,.05f})foreach(bool seek in new[]{false,true})
                {
                    float age=ages[c]+offset;var source=playback.effects.First(p=>p.transform.parent.name.StartsWith("particles_trail_khp_"+names[c]+"__"));
                    var clone=UnityEngine.Object.Instantiate(source.gameObject,root.transform);clone.SetActive(true);clone.transform.localScale=source.transform.lossyScale;
                    clone.transform.position=playback.stageRoot.transform.TransformPoint(new Vector3(0,1,2));clone.transform.rotation=source.transform.rotation;
                    var ps=clone.GetComponent<ParticleSystem>();ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);ps.useAutoRandomSeed=false;ps.randomSeed=source.randomSeed;
                    if(seek)ps.Simulate(age,false,true,false);else{ps.Simulate(0,false,true,false);Advance(ps,age);clone.transform.position-=Vector3.right*(age*.2f);}
                    Vector3 center=clone.transform.TransformPoint(ps.shape.position+ps.velocityOverLifetime.y.constant*age*Vector3.up);
                    camera.transform.position=center-playback.stageRoot.transform.forward*distance;camera.transform.LookAt(center);
                    var sample=new VisualSample{preset=names[c],method=seek?"seek":"continuous",age=age,distance=distance};
                    var particles=new ParticleSystem.Particle[ps.main.maxParticles];sample.count=ps.GetParticles(particles);
                    for(int i=0;i<sample.count;i++){sample.maxAlpha=Mathf.Max(sample.maxAlpha,particles[i].GetCurrentColor(ps).a/255f);sample.maxSize=Mathf.Max(sample.maxSize,particles[i].GetCurrentSize(ps));}
                    sample.image=c+"-"+distance+"m-"+age.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"-"+sample.method+".png";
                    camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();File.WriteAllBytes(folder+"/"+sample.image,tex.EncodeToPNG());report.samples.Add(sample);
                    UnityEngine.Object.DestroyImmediate(clone);
                }
            }
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(root);rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);report.sceneClean=!scene.isDirty;File.WriteAllText(folder+"/report.json",JsonUtility.ToJson(report,true));}
            if(!report.sceneClean)throw new Exception("Seek evidence changed scene");Debug.Log("PARADE_SEEK_VISUAL_CAPTURED count="+report.samples.Count);
        }
        static Sample Compare(ParticleSystem a,ParticleSystem b,string preset,float age)
        {
            var aa=new ParticleSystem.Particle[a.main.maxParticles];var bb=new ParticleSystem.Particle[b.main.maxParticles];int ac=a.GetParticles(aa),bc=b.GetParticles(bb);
            bool boundary=Mathf.Abs(age-a.main.startLifetime.constant)<.001f;
            var result=new Sample{preset=preset,burst=a.main.maxParticles,observed=bc,continuousCount=ac,age=age,pass=ac==bc||boundary,exactLifetimeBoundary=boundary};
            var lookup=new Dictionary<uint,ParticleSystem.Particle>();for(int i=0;i<bc;i++){if(lookup.ContainsKey(bb[i].randomSeed))result.pass=false;else lookup.Add(bb[i].randomSeed,bb[i]);}
            for(int i=0;i<ac;i++)
            {
                var x=aa[i];if(!lookup.TryGetValue(x.randomSeed,out var y)){if(!boundary)result.pass=false;continue;}
                result.positionError=Mathf.Max(result.positionError,Vector3.Distance(x.position,y.position));
                result.velocityError=Mathf.Max(result.velocityError,Vector3.Distance(x.velocity,y.velocity));
                for(int axis=0;axis<3;axis++)result.rotationError=Mathf.Max(result.rotationError,Mathf.Abs(Mathf.DeltaAngle(x.rotation3D[axis],y.rotation3D[axis])));
                result.lifetimeError=Mathf.Max(result.lifetimeError,Mathf.Abs(x.remainingLifetime-y.remainingLifetime));
                result.sizeError=Mathf.Max(result.sizeError,Mathf.Abs(x.GetCurrentSize(a)-y.GetCurrentSize(b)));
                Color cx=x.GetCurrentColor(a),cy=y.GetCurrentColor(b);result.colorError=Mathf.Max(result.colorError,Mathf.Abs(cx.r-cy.r)+Mathf.Abs(cx.g-cy.g)+Mathf.Abs(cx.b-cy.b)+Mathf.Abs(cx.a-cy.a));
            }
            result.pass&=result.positionError<.005f&&result.velocityError<.005f&&result.rotationError<.1f&&result.lifetimeError<.005f&&result.sizeError<.005f&&result.colorError<.02f;
            if(age>0&&age<a.main.startLifetime.constant-.01f)result.pass&=bc==a.main.maxParticles;
            if(age>a.main.startLifetime.constant+.001f)result.pass&=ac==0&&bc==0;
            return result;
        }
    }
}
