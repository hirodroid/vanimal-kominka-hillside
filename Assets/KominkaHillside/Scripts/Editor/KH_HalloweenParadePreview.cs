using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // Disposable rendering copies only. This is visual evidence, not a runtime/network test.
    public static class KH_HalloweenParadePreview
    {
        [Serializable] class Entry {public string image,view;public float seconds;}
        [Serializable] class Report {public string utc,directory;public bool dirtyBefore,dirtyAfter;public List<Entry> views=new List<Entry>();public string limits="Edit-mode mono daytime. Existing dog mesh poses placed at intended targets; no walking/Animator/network/device/audio acceptance. Dedicated show Timeline and particle age evaluated on disposable copies.";}
        [MenuItem("Tools/KominkaHillside/Parade/10 - Capture Edit Views")]
        public static void Capture(){CaptureInternal(false);}
        [MenuItem("Tools/KominkaHillside/Parade/32 - Capture Story Motion")]
        public static void CaptureStory(){CaptureInternal(true);}
        [MenuItem("Tools/KominkaHillside/Parade/44 - Capture Hat Response")]
        public static void CaptureHat(){CaptureInternal(false,new[]{60.3125f,61.3125f,62.3125f,62.8125f,63.8125f,64.8125f});}
        [MenuItem("Tools/KominkaHillside/Parade/48 - Capture Lantern Details")]
        public static void CaptureLanternDetails(){CaptureInternal(false,new[]{50f,51.25f,55.8f,63.8f,190.8f},true);}
        [MenuItem("Tools/KominkaHillside/Parade/50 - Capture Lantern Responses")]
        public static void CaptureLanternResponses(){CaptureInternal(true,Enumerable.Range(0,181).Select(i=>50+i/12f).Concat(Enumerable.Range(0,121).Select(i=>145+i/12f)).ToArray());}
        [MenuItem("Tools/KominkaHillside/Parade/51 - Capture Finale Revision")]
        public static void CaptureFinaleRevision(){CaptureInternal(false,new[]{29.8f,100.8f,190.8f},true);}
        internal static void CaptureInternal(bool motion,float[] sampleTimes=null,bool details=false)
        {
            var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlayingOrWillChangePlaymode||scene.isDirty||scene.path!="Assets/Scenes/VRCDefaultWorldScene.unity")throw new Exception("Saved hillside scene required");
            var playback=UnityEngine.Object.FindObjectOfType<KH_HalloweenParadePlayback>();if(playback==null||playback.stageRoot==null)throw new Exception("Bind show first");
            var m=playback.parade;var r=new Report{utc=DateTime.UtcNow.ToString("o"),directory="Temp/halloween-parade/preview/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),dirtyBefore=scene.isDirty};Directory.CreateDirectory(r.directory);
            var temps=new GameObject("KH_ParadeCaptureCopies"){hideFlags=HideFlags.HideAndDontSave};var baked=new List<Mesh>();RenderTexture rt=null;Texture2D tex=null;var oldRT=RenderTexture.active;var oldSky=RenderSettings.skybox;Material previewSky=null,previewOverlay=null;
            try
            {
                if(KH_ParadePurchasedAssets.LanternSelected){
                    var night=playback.nightMode;if(night==null)throw new Exception("Night reference missing");
                    previewSky=new Material(oldSky);RenderSettings.skybox=previewSky;previewOverlay=new Material(night.overlayRenderer.sharedMaterial);
                    var overlay=new GameObject("NightPreview",typeof(MeshFilter),typeof(MeshRenderer));overlay.transform.SetParent(temps.transform);overlay.GetComponent<MeshFilter>().sharedMesh=night.overlayRenderer.GetComponent<MeshFilter>().sharedMesh;overlay.GetComponent<Renderer>().sharedMaterial=previewOverlay;
                    r.limits="Edit mono parade twilight at authored shared-clock envelope. Dog meshes are static snapshots; no runtime/dog-dance/audio-sync/VR/network acceptance.";
                }
                var stage=UnityEngine.Object.Instantiate(playback.stageRoot,temps.transform);stage.name="CaptureStage";stage.SetActive(true);
                var director=stage.GetComponent<PlayableDirector>();director.playOnAwake=false;
                foreach(var output in director.playableAsset.outputs)
                {
                    var original=playback.director.GetGenericBinding(output.sourceObject);Transform t=original is GameObject go?go.transform:original is Component c?c.transform:null;
                    if(t==null)continue;string path=AnimationUtility.CalculateTransformPath(t,playback.stageRoot.transform);var target=string.IsNullOrEmpty(path)?stage.transform:stage.transform.Find(path);
                    if(target==null)throw new Exception("Capture binding "+path);
                    director.SetGenericBinding(output.sourceObject,original is GameObject?(UnityEngine.Object)target.gameObject:target.GetComponent(original.GetType()));
                }
                foreach(var audio in stage.GetComponentsInChildren<AudioSource>(true)){audio.mute=true;audio.playOnAwake=false;}
                var systems=playback.effects.Select(ps=>stage.transform.Find(AnimationUtility.CalculateTransformPath(ps.transform,playback.stageRoot.transform)).GetComponent<ParticleSystem>()).ToArray();
                var dogs=new Transform[6];
                for(int i=0;i<6;i++)
                {
                    var original=m.dogs[i].root;var copy=new GameObject("DogMesh_"+i).transform;copy.SetParent(temps.transform,false);dogs[i]=copy;
                    foreach(var renderer in original.GetComponentsInChildren<Renderer>(true))
                    {
                        if(!renderer.enabled||!renderer.gameObject.activeInHierarchy||renderer is ParticleSystemRenderer||renderer is LineRenderer)continue;
                        Mesh mesh=null;var sm=renderer as SkinnedMeshRenderer;
                        if(sm!=null){mesh=new Mesh();sm.BakeMesh(mesh,true);baked.Add(mesh);}else{var filter=renderer.GetComponent<MeshFilter>();if(filter!=null)mesh=filter.sharedMesh;}
                        if(mesh==null)continue;var visual=new GameObject(renderer.name,typeof(MeshFilter),typeof(MeshRenderer));visual.transform.SetParent(copy,false);
                        var relative=original.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                        if(sm!=null)BakeTransform(mesh,relative);else SetMatrix(visual.transform,relative);
                        visual.GetComponent<MeshFilter>().sharedMesh=mesh;visual.GetComponent<MeshRenderer>().sharedMaterials=renderer.sharedMaterials;
                    }
                    if(playback.costumes!=null&&playback.costumes.Length>i)
                    {
                        var costume=playback.costumes[i];foreach(var renderer in costume.GetComponentsInChildren<MeshRenderer>(true))
                        {var visual=new GameObject("AuthoredRibbon",typeof(MeshFilter),typeof(MeshRenderer));visual.transform.SetParent(copy,false);SetMatrix(visual.transform,original.worldToLocalMatrix*renderer.transform.localToWorldMatrix);visual.GetComponent<MeshFilter>().sharedMesh=renderer.GetComponent<MeshFilter>().sharedMesh;visual.GetComponent<MeshRenderer>().sharedMaterials=renderer.sharedMaterials;}
                    }
                }
                var cameraGo=new GameObject("InspectionCamera");cameraGo.transform.SetParent(temps.transform);var camera=cameraGo.AddComponent<Camera>();var source=UnityEngine.Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>().ReferenceCamera.GetComponent<Camera>();
                camera.CopyFrom(source);camera.enabled=false;camera.stereoTargetEye=StereoTargetEyeMask.None;camera.aspect=16f/9;camera.fieldOfView=60;camera.nearClipPlane=.05f;camera.farClipPlane=2200;camera.allowHDR=false;camera.allowMSAA=false;
                rt=new RenderTexture(1280,720,24);rt.Create();camera.targetTexture=rt;tex=new Texture2D(1280,720,TextureFormat.RGB24,false);
                float[] times=sampleTimes??(motion?Enumerable.Range(0,361).Select(i=>25+i/24f).Concat(Enumerable.Range(0,121).Select(i=>95+i/12f)).Concat(Enumerable.Range(0,121).Select(i=>185+i/12f)).ToArray():new[]{.8f,15.8f,29.8f,32.8f,35.8f,43.8f,65.8f,75.8f,85.8f,100.8f,120.8f,150.8f,175.8f,185.8f,190.8f,210.8f,218});
                foreach(float seconds in times)
                {
                    if(previewSky!=null){float dark=KH_HalloweenParadePlayback.NightEnvelope(seconds,220);previewOverlay.SetFloat("_Darkness",dark);previewSky.SetFloat("_Exposure",Mathf.Lerp(KH_NightMode.DaySkyExposure,KH_NightMode.NightSkyExposure,dark));previewSky.SetColor("_Tint",Color.Lerp(new Color(.5f,.5f,.5f,.5f),new Color(.30f,.34f,.50f,.5f),dark));}
                    director.time=seconds;director.Evaluate();
                    for(int i=0;i<systems.Length;i++){var ps=systems[i];ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);if(seconds>=playback.effectStarts[i]&&seconds<playback.effectEnds[i])ps.Simulate(seconds-playback.effectStarts[i],false,true,false);}
                    for(int i=0;i<6;i++)Place(dogs[i],m,i,seconds);
                    if(details&&(seconds==29.8f||seconds==100.8f)){int index=Array.FindIndex(playback.effects,e=>e.transform.parent.name.Contains("guide-journey"));var ps=systems[index];var particles=new ParticleSystem.Particle[2];if(ps.GetParticles(particles)>0){var pos=ps.main.simulationSpace==ParticleSystemSimulationSpace.World?particles[0].position:ps.transform.TransformPoint(particles[0].position);Shot(camera,rt,tex,r,seconds,"leader-front",pos+dogs[0].forward*2.7f+Vector3.up*.25f,pos);}
                        var center=(dogs[0].position+dogs[5].position)*.5f;Shot(camera,rt,tex,r,seconds,"parade-front",dogs[0].position+dogs[0].forward*5+Vector3.up*1.62f,center+Vector3.up*1.1f);}
                    if(details&&seconds==63.8f){var lamp=stage.transform.Find("StoryCast/TreeL/Pose/LanternEnsemble/PumpkinStreetlight");for(int side=-1;side<=1;side+=2)Shot(camera,rt,tex,r,seconds,"pumpkin-lamp-"+side,lamp.TransformPoint(new Vector3(.1f,1.1f,side*3)),lamp.TransformPoint(new Vector3(0,.9f,0)));}
                    if(motion)
                    {
                        Vector3 target,eye;string view;int frame;
                        if(seconds<=40){target=m._DogTargetAt(31,0)+Vector3.up*1.7f;var forward=m._DogFacingAt(31,0);eye=target+Vector3.Cross(Vector3.up,forward).normalized*3-forward*12;view="gate";frame=Mathf.RoundToInt((seconds-25)*24);}
                        else if(seconds<=65){target=(dogs[0].position+dogs[5].position)*.5f+Vector3.up*1.3f;eye=dogs[5].position-dogs[5].forward*3+Vector3.up*1.62f;view="response";frame=Mathf.RoundToInt((seconds-50)*12);}
                        else if(seconds<110){target=(dogs[0].position+dogs[5].position)*.5f+Vector3.up*1.3f;eye=target+Vector3.Cross(Vector3.up,dogs[0].forward).normalized*9;view="ensemble";frame=Mathf.RoundToInt((seconds-95)*12);}
                        else if(seconds<=155){target=stage.transform.TransformPoint(new Vector3(0,1.2f,2));eye=stage.transform.TransformPoint(new Vector3(0,1.62f,-7));view="turn";frame=Mathf.RoundToInt((seconds-145)*12);}
                        else{target=stage.transform.TransformPoint(new Vector3(0,2,2));eye=stage.transform.TransformPoint(new Vector3(0,1.62f,-8));view="blessing";frame=Mathf.RoundToInt((seconds-185)*12);}
                        camera.fieldOfView=70;Shot(camera,rt,tex,r,seconds,view+"-"+frame.ToString("D4"),eye,target);continue;
                    }
                    if(seconds<150)
                    {
                        var last=dogs[5];Shot(camera,rt,tex,r,seconds,"follow",last.position-last.forward*3+Vector3.up*1.62f,dogs[1].position+Vector3.up*.7f);
                        var center=(dogs[0].position+dogs[5].position)*.5f;var right=Vector3.Cross(Vector3.up,dogs[0].forward).normalized;
                        Shot(camera,rt,tex,r,seconds,"roadside",center+right*7+Vector3.up*1.62f,center+Vector3.up*1.4f);
                    }
                    else
                    {
                        var origin=stage.transform;Shot(camera,rt,tex,r,seconds,"plaza-standing",origin.TransformPoint(new Vector3(0,1.62f,-7)),origin.TransformPoint(new Vector3(0,1.5f,1)));
                        if(seconds==190.8f){camera.fieldOfView=74;Shot(camera,rt,tex,r,seconds,"plaza-middle",origin.TransformPoint(new Vector3(0,1.4f,-5.5f)),origin.TransformPoint(new Vector3(0,.85f,1)));camera.fieldOfView=60;}
                        if(seconds==190.8f||seconds==210.8f){Shot(camera,rt,tex,r,seconds,"plaza-low",origin.TransformPoint(new Vector3(0,.45f,-5)),origin.TransformPoint(new Vector3(0,1.25f,1)));Shot(camera,rt,tex,r,seconds,"plaza-near",origin.TransformPoint(new Vector3(0,1.62f,-3)),origin.TransformPoint(new Vector3(0,.55f,1)));}
                    }
                }
                for(int i=0;!motion&&(sampleTimes==null||details)&&i<6;i++)
                {
                    var d=dogs[i];var local=m.dogs[i].root.InverseTransformPoint(playback.costumes[i].transform.position);var target=d.TransformPoint(local)+Vector3.up*.12f;
                    float[] distances={.75f,.65f,1f,.65f,.85f,1.05f};
                    Shot(camera,rt,tex,r,times[times.Length-1],"ribbon-"+i,target+d.forward*distances[i]+d.right*.2f+Vector3.up*.06f,target);
                }
                director.Stop();
            }
            finally
            {
                RenderSettings.skybox=oldSky;if(previewSky!=null)UnityEngine.Object.DestroyImmediate(previewSky);if(previewOverlay!=null)UnityEngine.Object.DestroyImmediate(previewOverlay);
                RenderTexture.active=oldRT;UnityEngine.Object.DestroyImmediate(temps);foreach(var mesh in baked)UnityEngine.Object.DestroyImmediate(mesh);
                if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}if(tex!=null)UnityEngine.Object.DestroyImmediate(tex);
                r.dirtyAfter=scene.isDirty;File.WriteAllText(r.directory+"/report.json",JsonUtility.ToJson(r,true));File.WriteAllText("Temp/halloween-parade/preview/latest.json",JsonUtility.ToJson(r,true));
            }
            if(r.dirtyAfter)throw new Exception("Preview changed scene dirty state; do not save before inspection");Debug.Log("PARADE_EDIT_VIEWS_OK count="+r.views.Count+" dir="+r.directory);
        }
        static void SetMatrix(Transform t,Matrix4x4 matrix){t.localPosition=matrix.GetColumn(3);t.localRotation=matrix.rotation;t.localScale=matrix.lossyScale;}
        static void BakeTransform(Mesh mesh,Matrix4x4 matrix)
        {
            var vertices=mesh.vertices;var normals=mesh.normals;var tangents=mesh.tangents;var normalMatrix=matrix.inverse.transpose;
            for(int i=0;i<vertices.Length;i++)vertices[i]=matrix.MultiplyPoint3x4(vertices[i]);
            for(int i=0;i<normals.Length;i++)normals[i]=normalMatrix.MultiplyVector(normals[i]).normalized;
            for(int i=0;i<tangents.Length;i++){var v=matrix.MultiplyVector(new Vector3(tangents[i].x,tangents[i].y,tangents[i].z)).normalized;tangents[i]=new Vector4(v.x,v.y,v.z,tangents[i].w);}
            mesh.vertices=vertices;mesh.normals=normals;mesh.tangents=tangents;mesh.RecalculateBounds();
        }
        static void Place(Transform t,KH_HalloweenParade m,int dog,float seconds)
        {
            var point=m._DogTargetAt(seconds,dog);var forward=m._DogFacingAt(seconds,dog);
            t.SetPositionAndRotation(point,Quaternion.LookRotation(forward));
        }
        static void Shot(Camera c,RenderTexture rt,Texture2D tex,Report r,float time,string view,Vector3 eye,Vector3 target)
        {c.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));c.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();var entry=new Entry{seconds=time,view=view,image=r.directory+"/"+time.ToString("000.0",System.Globalization.CultureInfo.InvariantCulture)+"-"+view+".png"};File.WriteAllBytes(entry.image,tex.EncodeToPNG());r.views.Add(entry);}
    }
}
