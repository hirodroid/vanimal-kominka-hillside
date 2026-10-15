using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // Read-only scene evidence for the route and three representative audience views.
    public static class KH_HalloweenParadeSurvey
    {
        [Serializable] public class Sample
        {
            public string label; public Vector3 requested, navPosition; public bool onNav, completePath;
            public float navOffset, edgeDistance, pathLength; public Vector3[] corners;
        }
        [Serializable] public class View
        { public string label, image; public Vector3 position, target; public float fov = 60; }
        [Serializable] public class Report
        {
            public string utc, scene, directory;
            public bool dirtyBefore, dirtyAfter; public Vector3 stage; public float yaw=190, routeLength;
            public List<Sample> samples=new List<Sample>(); public List<View> views=new List<View>();
            public string limitations="Edit-mode mono daylight survey. NavMesh clearance is not painted road width or cart/player collision clearance. No runtime motion, stereo, network or device performance PASS.";
        }
        [MenuItem("Tools/KominkaHillside/Parade/1 - Survey Route and Views")]
        public static void Survey()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode||scene.isDirty||scene.path!="Assets/Scenes/VRCDefaultWorldScene.unity")
                throw new InvalidOperationException("Saved hillside scene in Edit mode required; nothing changed");
            var route=UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
            var descriptor=UnityEngine.Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
            if(route==null||route.nodePositions==null||route.nodePositions.Length<17||descriptor==null||descriptor.ReferenceCamera==null)
                throw new InvalidOperationException("Route and reference camera required");
            var r=new Report{utc=DateTime.UtcNow.ToString("o"),scene=scene.path,stage=route.nodePositions[16],
                directory="Temp/halloween-parade/survey/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")};
            Directory.CreateDirectory(r.directory);
            for(int i=3;i<=16;i++)
            {
                var a=route.nodePositions[i]; var b=route.nodePositions[i==16?16:i+1];
                if(i>=5&&i<16)r.routeLength+=Vector3.Distance(a,b);
                r.samples.Add(Probe("node-"+i,a,b));
                if(i<16)for(int j=1;j<4;j++)r.samples.Add(Probe("link-"+i+"-"+j,Vector3.Lerp(a,b,j/4f),b));
            }
            var rotation=Quaternion.Euler(0,r.yaw,0);
            float[] xs={3.5f,2.1f,.7f,-.7f,-2.1f,-3.5f};float[] zs={0,1.2f,2,2,1.2f,0};
            for(int i=0;i<6;i++)r.samples.Add(Probe("finale-dog-"+i,r.stage+rotation*new Vector3(xs[i],0,zs[i]),r.stage));
            r.views.Add(new View{label="start-follow",position=route.nodePositions[4]+Vector3.up*1.62f,target=route.nodePositions[6]+Vector3.up*.5f});
            r.views.Add(new View{label="slope-follow",position=route.nodePositions[10]+Vector3.up*1.62f,target=route.nodePositions[12]+Vector3.up*.5f});
            r.views.Add(new View{label="plaza-standing",position=r.stage+rotation*new Vector3(0,1.62f,-7),target=r.stage+Vector3.up*1.5f});
            r.views.Add(new View{label="plaza-low",position=r.stage+rotation*new Vector3(0,.4f,-5),target=r.stage+Vector3.up*1.3f});
            Capture(r,descriptor.ReferenceCamera.GetComponent<Camera>());
            r.dirtyAfter=scene.isDirty;
            File.WriteAllText(r.directory+"/report.json",JsonUtility.ToJson(r,true));
            File.WriteAllText("Temp/halloween-parade/survey/latest.json",JsonUtility.ToJson(r,true));
            if(r.dirtyAfter)throw new InvalidOperationException("Survey changed scene dirty state");
            Debug.Log("PARADE_SURVEY_OK samples="+r.samples.Count+" views="+r.views.Count+" path="+r.directory);
        }
        static Sample Probe(string label,Vector3 point,Vector3 destination)
        {
            var s=new Sample{label=label,requested=point};NavMeshHit hit,edge,target;
            s.onNav=NavMesh.SamplePosition(point,out hit,.6f,NavMesh.AllAreas);
            if(s.onNav)
            {
                s.navPosition=hit.position;s.navOffset=Vector3.Distance(point,hit.position);
                s.edgeDistance=NavMesh.FindClosestEdge(hit.position,out edge,NavMesh.AllAreas)?edge.distance:-1;
                var p=new NavMeshPath();
                s.completePath=NavMesh.SamplePosition(destination,out target,.6f,NavMesh.AllAreas)&&NavMesh.CalculatePath(hit.position,target.position,NavMesh.AllAreas,p)&&p.status==NavMeshPathStatus.PathComplete;
                s.corners=p.corners;for(int i=1;i<p.corners.Length;i++)s.pathLength+=Vector3.Distance(p.corners[i-1],p.corners[i]);
            }
            return s;
        }
        static void Capture(Report r,Camera source)
        {
            GameObject go=null;RenderTexture rt=null;Texture2D tex=null;var previous=RenderTexture.active;
            try
            {
                go=new GameObject("KH_TempParadeSurvey"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();
                camera.CopyFrom(source);camera.enabled=false;camera.stereoTargetEye=StereoTargetEyeMask.None;
                camera.aspect=16f/9f;camera.nearClipPlane=.1f;camera.farClipPlane=2200;camera.allowHDR=false;camera.allowMSAA=false;
                rt=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32){hideFlags=HideFlags.HideAndDontSave};rt.Create();camera.targetTexture=rt;
                tex=new Texture2D(1280,720,TextureFormat.RGB24,false){hideFlags=HideFlags.HideAndDontSave};
                foreach(var v in r.views)
                {
                    camera.fieldOfView=v.fov;camera.transform.SetPositionAndRotation(v.position,Quaternion.LookRotation(v.target-v.position));
                    camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();
                    v.image=r.directory+"/"+v.label+".png";File.WriteAllBytes(v.image,tex.EncodeToPNG());
                }
            }
            finally
            {
                RenderTexture.active=previous;if(go!=null)UnityEngine.Object.DestroyImmediate(go);
                if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}if(tex!=null)UnityEngine.Object.DestroyImmediate(tex);
            }
        }
    }
}
