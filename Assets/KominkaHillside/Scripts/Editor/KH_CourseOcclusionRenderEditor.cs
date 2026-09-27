using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // Sparse mono image comparison, not an exhaustive visibility or device acceptance test.
    public static class KH_CourseOcclusionRenderEditor
    {
        const string Dir="Temp/des-env-002/occlusion-render/";
        const int Width=384,Height=216;
        [Serializable] public class View
        {
            public string name;
            public Vector3 position,euler;
            public int changedPixels,controlChangedPixels,maxChannelDifference;
            public double meanChannelDifference;
            public string offImage,onImage,controlImage;
        }
        [Serializable] public class Report
        {
            public string status,utc,scene;
            public int width=Width,height=Height,pairs,changedViews,unstableViews;
            public bool sceneDirtyAfter;
            public List<View> views=new List<View>();
            public string limitations="Sparse Edit-mode mono images, 384x216, FOV 60, near 0.3/far 2200. Route nodes every ten steps in both directions plus three key views and two bridge eye heights. OFF/ON/OFF rendered in the same Editor callback. Pixel differences over 2/255 require inspection; identical images do not prove every viewpoint, stereo, mirror or device. Does not measure speed.";
        }
        [MenuItem("Tools/KominkaHillside/Course/38 - Compare Course Occlusion Images")]
        public static void Capture()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            if(StaticOcclusionCulling.umbraDataSize<=0)throw new InvalidOperationException("Saved bake required");
            var route=UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
            var descriptor=UnityEngine.Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
            if(route==null||route.count!=436||descriptor==null||descriptor.ReferenceCamera==null)throw new InvalidOperationException("Full course and reference camera required");
            var report=new Report{utc=DateTime.UtcNow.ToString("o"),scene=SceneManager.GetActiveScene().path};
            var poses=new List<View>();
            for(int i=0;i<route.count;i+=10)
            {
                var direction=route.nodes[(i+1)%route.count].position-route.nodes[i].position;direction.y=0;
                float yaw=Quaternion.LookRotation(direction).eulerAngles.y;
                for(int reverse=0;reverse<2;reverse++)poses.Add(new View{name="node-"+i+"-"+reverse,position=route.nodes[i].position+Vector3.up*1.62f,euler=new Vector3(10,yaw+reverse*180,0)});
            }
            poses.Add(new View{name="bridge",position=new Vector3(432,7.42f,196),euler=new Vector3(10,245,0)});
            poses.Add(new View{name="river",position=new Vector3(505,11.82f,285),euler=new Vector3(10,310,0)});
            poses.Add(new View{name="plaza",position=new Vector3(215,41.82f,615),euler=new Vector3(10,200,0)});
            foreach(float z in new[]{190f,490f})foreach(float eye in new[]{.6f,3f})
            {
                var bridge=GameObject.Find("KH_CourseExtension").transform.Find("Bridge_"+z);
                if(bridge==null)throw new InvalidOperationException("Bridge missing");
                poses.Add(new View{name="bridge-"+z+"-eye-"+eye,position=bridge.position+Vector3.up*eye,euler=new Vector3(10,90,0)});
            }
            Directory.CreateDirectory(Dir);GameObject go=null;RenderTexture target=null;Texture2D texture=null;var previous=RenderTexture.active;
            double started=EditorApplication.timeSinceStartup;int savedDifferences=0;
            try
            {
                go=new GameObject("KH_TempOcclusionCamera"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();
                camera.CopyFrom(descriptor.ReferenceCamera.GetComponent<Camera>());camera.enabled=false;camera.cameraType=CameraType.Game;
                camera.stereoTargetEye=StereoTargetEyeMask.None;camera.fieldOfView=60;camera.aspect=(float)Width/Height;
                camera.nearClipPlane=.3f;camera.farClipPlane=2200;camera.allowHDR=false;camera.allowMSAA=false;
                target=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32){hideFlags=HideFlags.HideAndDontSave};target.Create();camera.targetTexture=target;
                texture=new Texture2D(Width,Height,TextureFormat.RGB24,false){hideFlags=HideFlags.HideAndDontSave};
                foreach(var view in poses)
                {
                    if(EditorApplication.timeSinceStartup-started>90)throw new TimeoutException("Image comparison exceeded 90 seconds");
                    camera.transform.SetPositionAndRotation(view.position,Quaternion.Euler(view.euler));
                    var off=Pixels(camera,false,target,texture);var on=Pixels(camera,true,target,texture);var control=Pixels(camera,false,target,texture);
                    long total=0;
                    for(int p=0;p<off.Length;p++)
                    {
                        int dr=Math.Abs(off[p].r-on[p].r),dg=Math.Abs(off[p].g-on[p].g),db=Math.Abs(off[p].b-on[p].b);
                        int max=Math.Max(dr,Math.Max(dg,db));total+=dr+dg+db;
                        view.maxChannelDifference=Math.Max(view.maxChannelDifference,max);if(max>2)view.changedPixels++;
                        if(Math.Abs(off[p].r-control[p].r)>2||Math.Abs(off[p].g-control[p].g)>2||Math.Abs(off[p].b-control[p].b)>2)view.controlChangedPixels++;
                    }
                    view.meanChannelDifference=(double)total/(off.Length*3);
                    if(view.changedPixels>0)report.changedViews++;if(view.controlChangedPixels>0)report.unstableViews++;
                    bool key=view.name=="bridge"||view.name=="river"||view.name=="plaza";
                    if(key||(view.changedPixels>0&&savedDifferences++<6))
                    {
                        view.offImage=Save(texture,off,view.name+"-off");view.onImage=Save(texture,on,view.name+"-on");view.controlImage=Save(texture,control,view.name+"-control");
                    }
                    report.views.Add(view);report.pairs++;
                }
            }
            finally
            {
                RenderTexture.active=previous;if(go!=null)UnityEngine.Object.DestroyImmediate(go);
                if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);
            }
            report.sceneDirtyAfter=SceneManager.GetActiveScene().isDirty;
            report.status=report.changedViews==0&&report.unstableViews==0&&!report.sceneDirtyAfter?"SAMPLED_IMAGES_MATCH":"REVIEW_REQUIRED";
            File.WriteAllText(Dir+"report.json",JsonUtility.ToJson(report,true));Debug.Log("[KH Course Occlusion Images] "+report.status+" pairs="+report.pairs+" changed="+report.changedViews+" unstable="+report.unstableViews);
        }
        static Color32[] Pixels(Camera camera,bool occlusion,RenderTexture target,Texture2D texture)
        {camera.useOcclusionCulling=occlusion;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,Width,Height),0,0);texture.Apply();return texture.GetPixels32();}
        static string Save(Texture2D texture,Color32[] pixels,string name)
        {texture.SetPixels32(pixels);texture.Apply();string path=Dir+name+".png";File.WriteAllBytes(path,texture.EncodeToPNG());return path;}
    }
}
