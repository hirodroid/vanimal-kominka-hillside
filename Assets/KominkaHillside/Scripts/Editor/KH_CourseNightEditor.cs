using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UdonSharpEditor;

namespace KominkaHillside.Editor
{
    // Offscreen Edit-mode evidence of the existing night recipe; never invokes gameplay or saves a scene.
    public static class KH_CourseNightEditor
    {
        const string Dir="Temp/des-env-002/night/";
        [Serializable] public class View
        {
            public string name,dayImage,nightImage;
            public Vector3 cameraPosition,cameraEuler;
            public float near,far,fov,dayLowerHalfLuminance,nightLowerHalfLuminance;
        }
        [Serializable] public class Report
        {
            public string utc,status,scene;
            public int errors,waterRenderers,sceneryRenderers;
            public bool sceneDirtyAfter,originalOverlayEnabled,skyMaterialPreserved;
            public List<View> views=new List<View>();
            public string[] findings;
            public string limitations="Edit-mode mono offscreen render using cloned materials and the existing night recipe. Does not exercise Udon fade, saved preferences, stereo, VRChat mirror/photo cameras or mobile devices.";
        }
        [MenuItem("Tools/KominkaHillside/Course/33 - Capture Course Day And Night")]
        public static void Capture()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var scene=EditorSceneManager.GetActiveScene();var night=UnityEngine.Object.FindObjectOfType<KH_NightMode>();
            var descriptor=UnityEngine.Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
            if(night==null||night.overlayRenderer==null||night.skyHolder==null||descriptor==null||descriptor.ReferenceCamera==null)throw new InvalidOperationException("Night and reference camera required");
            var reference=descriptor.ReferenceCamera.GetComponent<Camera>();
            if(reference==null||night.overlayRenderer.enabled||night.skyHolder.enabled)throw new InvalidOperationException("Saved daytime state required");
            var sourceSky=RenderSettings.skybox;var sourceOverlay=night.overlayRenderer.sharedMaterial;
            var sourceMesh=night.overlayRenderer.GetComponent<MeshFilter>().sharedMesh;
            var report=new Report{utc=DateTime.UtcNow.ToString("o"),scene=scene.path};var errors=new List<string>();
            var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(night);object value;
            if(backing==null||!backing.enabled||!night.enabled||!backing.publicVariables.TryGetVariableValue("overlayRenderer",out value)||value!=(object)night.overlayRenderer||!backing.publicVariables.TryGetVariableValue("skyHolder",out value)||value!=(object)night.skyHolder)errors.Add("Night Udon wiring mismatch");
            if(sourceOverlay==null||sourceOverlay.renderQueue!=KH_NightMode.OverlayQueue||sourceSky!=night.skyHolder.sharedMaterial||sourceMesh==null)throw new InvalidOperationException("Night source mismatch");
            foreach(var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())foreach(var m in r.sharedMaterials)
            {
                if(m==null)continue;
                bool water=m.shader.name=="KominkaHillside/River Lite";
                bool prop=r.transform.IsChildOf(GameObject.Find("KH_CourseScenery").transform);
                if(water)report.waterRenderers++;if(prop)report.sceneryRenderers++;
                if((water||prop)&&m.renderQueue>=sourceOverlay.renderQueue)errors.Add("Scenery draws after night overlay: "+r.name);
            }
            if(report.waterRenderers!=24||report.sceneryRenderers!=145)errors.Add("Unexpected course renderer count");
            Directory.CreateDirectory(Dir);
            GameObject cameraGo=null,overlayGo=null;Material sky=null,overlay=null;RenderTexture target=null;var previousTarget=RenderTexture.active;
            try
            {
                sky=new Material(sourceSky){hideFlags=HideFlags.HideAndDontSave};
                overlay=new Material(sourceOverlay){hideFlags=HideFlags.HideAndDontSave};
                overlayGo=new GameObject("KH_TempNightPreview"){hideFlags=HideFlags.HideAndDontSave,layer=night.overlayRenderer.gameObject.layer};
                overlayGo.AddComponent<MeshFilter>().sharedMesh=sourceMesh;var renderer=overlayGo.AddComponent<MeshRenderer>();
                renderer.sharedMaterial=overlay;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.allowOcclusionWhenDynamic=false;
                cameraGo=new GameObject("KH_TempCourseCamera"){hideFlags=HideFlags.HideAndDontSave};var camera=cameraGo.AddComponent<Camera>();
                camera.CopyFrom(reference);camera.enabled=false;camera.cameraType=CameraType.Game;camera.stereoTargetEye=StereoTargetEyeMask.None;
                camera.clearFlags=CameraClearFlags.Skybox;camera.aspect=890f/688f;camera.fieldOfView=60;camera.allowHDR=false;camera.allowMSAA=false;
                var skybox=cameraGo.AddComponent<Skybox>();skybox.material=sky;
                target=new RenderTexture(890,688,24,RenderTextureFormat.ARGB32){hideFlags=HideFlags.HideAndDontSave};target.Create();camera.targetTexture=target;
                string[] names={"bridge","river","plaza"};
                Vector3[] positions={new Vector3(432,7.42f,196),new Vector3(505,11.82f,285),new Vector3(215,41.82f,615)};
                float[] yaws={245,310,200};
                for(int i=0;i<3;i++)
                {
                    camera.transform.SetPositionAndRotation(positions[i],Quaternion.Euler(10,yaws[i],0));
                    var view=new View{name=names[i],cameraPosition=positions[i],cameraEuler=camera.transform.eulerAngles,near=camera.nearClipPlane,far=camera.farClipPlane,fov=camera.fieldOfView};
                    for(int state=0;state<2;state++)
                    {
                        renderer.enabled=state==1;overlay.SetFloat("_Darkness",state);
                        sky.SetFloat("_Exposure",state==0?KH_NightMode.DaySkyExposure:KH_NightMode.NightSkyExposure);
                        sky.SetColor("_Tint",state==0?new Color(KH_NightMode.DayTintR,KH_NightMode.DayTintG,KH_NightMode.DayTintB,KH_NightMode.SkyTintA):new Color(KH_NightMode.NightTintR,KH_NightMode.NightTintG,KH_NightMode.NightTintB,KH_NightMode.SkyTintA));
                        camera.Render();RenderTexture.active=target;var texture=new Texture2D(890,688,TextureFormat.RGB24,false);
                        try
                        {
                            texture.ReadPixels(new Rect(0,0,890,688),0,0);texture.Apply();
                            string file=Dir+names[i]+(state==0?"-day.png":"-night.png");File.WriteAllBytes(file,texture.EncodeToPNG());
                            var pixels=texture.GetPixels32();double total=0;
                            for(int p=0;p<890*344;p++){var c=pixels[p];total+=(.2126*c.r+.7152*c.g+.0722*c.b)/255.0;}
                            float luminance=(float)(total/(890*344));
                            if(state==0){view.dayImage=file;view.dayLowerHalfLuminance=luminance;}else{view.nightImage=file;view.nightLowerHalfLuminance=luminance;}
                        }
                        finally{UnityEngine.Object.DestroyImmediate(texture);}
                    }
                    if(view.dayLowerHalfLuminance<.05f||view.nightLowerHalfLuminance>=view.dayLowerHalfLuminance*.9f||view.nightLowerHalfLuminance<=0)errors.Add("No measurable night attenuation: "+view.name);
                    report.views.Add(view);
                }
            }
            finally
            {
                RenderTexture.active=previousTarget;
                if(cameraGo!=null)UnityEngine.Object.DestroyImmediate(cameraGo);
                if(overlayGo!=null)UnityEngine.Object.DestroyImmediate(overlayGo);
                if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
                if(sky!=null)UnityEngine.Object.DestroyImmediate(sky);if(overlay!=null)UnityEngine.Object.DestroyImmediate(overlay);
            }
            report.sceneDirtyAfter=scene.isDirty;report.originalOverlayEnabled=night.overlayRenderer.enabled;report.skyMaterialPreserved=RenderSettings.skybox==sourceSky&&night.skyHolder.sharedMaterial==sourceSky&&night.overlayRenderer.sharedMaterial==sourceOverlay;
            if(report.sceneDirtyAfter||report.originalOverlayEnabled||!report.skyMaterialPreserved)errors.Add("Preview state not restored");
            report.errors=errors.Count;report.findings=errors.ToArray();report.status=errors.Count==0?"NIGHT_EDITOR_RENDER_OK":"FAIL";
            File.WriteAllText(Dir+"report.json",JsonUtility.ToJson(report,true));
            if(errors.Count>0)throw new InvalidOperationException(string.Join("; ",errors));
            Debug.Log("[KH Course Night] "+report.status+" three paired views, scene and original materials unchanged");
        }
    }
}
