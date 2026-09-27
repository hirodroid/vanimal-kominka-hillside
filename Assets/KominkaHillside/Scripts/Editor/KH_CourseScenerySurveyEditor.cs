using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // Editor-only four-direction survey. Temporary camera, no saved camera or scene mutations.
    public static class KH_CourseScenerySurveyEditor
    {
        const int Width=512,Height=288,Across=3,Down=4;
        [Serializable] public class View
        {public int index,node,quarter;public Vector3 position,euler;public string image;}
        [Serializable] public class Report
        {
            public string utc,scene,directory;
            public bool dirtyAfter;
            public List<View> views=new List<View>();
            public List<string> sheets=new List<string>();
            public string limitations="Edit-mode daytime mono survey, 512x288, eye height 1.62m, FOV60, near0.3/far2200. Every twenty route nodes in four directions. Sparse images require visual inspection; no aesthetic or stereo/device PASS is inferred.";
        }
        [MenuItem("Tools/KominkaHillside/Course/48 - Survey Course Scenery Images")]
        public static void Capture()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var route=UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
            var descriptor=UnityEngine.Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
            if(route==null||!route.closedLoop||descriptor==null||descriptor.ReferenceCamera==null)throw new InvalidOperationException("Course/reference camera required");
            var report=new Report{utc=DateTime.UtcNow.ToString("o"),scene=SceneManager.GetActiveScene().path,
                directory="Temp/des-env-002/scenery-survey/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")};
            Directory.CreateDirectory(report.directory);
            GameObject go=null;RenderTexture target=null;Texture2D texture=null,sheet=null;var previous=RenderTexture.active;
            double started=EditorApplication.timeSinceStartup;
            try
            {
                go=new GameObject("KH_TempScenerySurvey"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();
                camera.CopyFrom(descriptor.ReferenceCamera.GetComponent<Camera>());camera.enabled=false;camera.cameraType=CameraType.Game;
                camera.stereoTargetEye=StereoTargetEyeMask.None;camera.fieldOfView=60;camera.aspect=(float)Width/Height;
                camera.nearClipPlane=.3f;camera.farClipPlane=2200;camera.allowHDR=false;camera.allowMSAA=false;camera.useOcclusionCulling=true;
                target=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32){hideFlags=HideFlags.HideAndDontSave};target.Create();camera.targetTexture=target;
                texture=new Texture2D(Width,Height,TextureFormat.RGB24,false){hideFlags=HideFlags.HideAndDontSave};
                sheet=new Texture2D(Width*Across,Height*Down,TextureFormat.RGB24,false){hideFlags=HideFlags.HideAndDontSave};
                var blank=new Color32[Width*Across*Height*Down];sheet.SetPixels32(blank);
                for(int node=0;node<route.count;node+=20)
                {
                    Vector3 direction=route._Position((node+1)%route.count)-route._Position(node);direction.y=0;
                    float yaw=Quaternion.LookRotation(direction).eulerAngles.y;
                    for(int quarter=0;quarter<4;quarter++)
                    {
                        if(EditorApplication.timeSinceStartup-started>90)throw new TimeoutException("Scenery survey exceeded 90 seconds");
                        int index=report.views.Count;
                        var view=new View{index=index,node=node,quarter=quarter,position=route._Position(node)+Vector3.up*1.62f,euler=new Vector3(8,yaw+quarter*90,0)};
                        camera.transform.SetPositionAndRotation(view.position,Quaternion.Euler(view.euler));camera.Render();RenderTexture.active=target;
                        texture.ReadPixels(new Rect(0,0,Width,Height),0,0);texture.Apply();
                        view.image=report.directory+"/view-"+index.ToString("D3")+"-node-"+node+".png";File.WriteAllBytes(view.image,texture.EncodeToPNG());
                        int slot=index%(Across*Down);sheet.SetPixels((slot%Across)*Width,(Down-1-slot/Across)*Height,Width,Height,texture.GetPixels());
                        report.views.Add(view);
                        if(slot==Across*Down-1){SaveSheet(sheet,report);sheet.SetPixels32(blank);}
                    }
                }
                if(report.views.Count%(Across*Down)!=0)SaveSheet(sheet,report);
            }
            finally
            {
                RenderTexture.active=previous;if(go!=null)UnityEngine.Object.DestroyImmediate(go);
                if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);if(sheet!=null)UnityEngine.Object.DestroyImmediate(sheet);
            }
            report.dirtyAfter=SceneManager.GetActiveScene().isDirty;
            File.WriteAllText(report.directory+"/report.json",JsonUtility.ToJson(report,true));
            File.WriteAllText("Temp/des-env-002/scenery-survey/latest.json",JsonUtility.ToJson(report,true));
            if(report.dirtyAfter)throw new InvalidOperationException("Survey dirtied scene");
            Debug.Log("[KH Course Scenery Survey] images="+report.views.Count+" directory="+report.directory);
        }
        static void SaveSheet(Texture2D sheet,Report report)
        {sheet.Apply();string path=report.directory+"/sheet-"+report.sheets.Count.ToString("D2")+".png";File.WriteAllBytes(path,sheet.EncodeToPNG());report.sheets.Add(path);}
    }
}
