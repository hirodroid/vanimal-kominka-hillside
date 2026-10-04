using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
namespace KominkaHillside.Editor
{
    public static partial class KH_PropFinishEditor
    {
        [Serializable] class Shot { public string name,file;public Vector3 position,euler; }
        [Serializable] class Shots {public List<Shot> views=new List<Shot>();public bool dirtyAfter;}
        [MenuItem("Tools/KominkaHillside/Course/161 - Capture Props Before")]
        public static void Before()
        {
            Idle();var scene=UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();if(scene.isDirty)throw new Exception("Save scene first");Directory.CreateDirectory(Evidence);
            if(!File.Exists(Evidence+"/before.unity"))File.Copy(scene.path,Evidence+"/before.unity");
            var renderers=UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true).Where(r=>r.gameObject.scene==scene&&Target(r.transform)).ToArray();var states=renderers.Select(r=>r.enabled).ToArray();
            var original=JsonUtility.FromJson<SurveyReport>(File.ReadAllText(Evidence+"/survey.json")).parts.ToDictionary(p=>p.path,p=>p.enabled);
            try{foreach(var r in renderers){if(original.TryGetValue(PathOf(r.transform),out bool enabled))r.enabled=enabled;else if(r.name==Visual)r.enabled=false;}Capture("before");}
            finally{for(int i=0;i<renderers.Length;i++)renderers[i].enabled=states[i];}
        }
        [MenuItem("Tools/KominkaHillside/Course/164 - Capture Props After")]
        public static void After(){Idle();Capture("after");}
        static void Capture(string stage)
        {
            Directory.CreateDirectory(Images);var report=new Shots();var previous=RenderTexture.active;
            var go=new GameObject("KH_TempPropCamera"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();
            cam.fieldOfView=48;cam.nearClipPlane=.01f;cam.farClipPlane=1500;cam.clearFlags=CameraClearFlags.Skybox;cam.cullingMask=~0;cam.allowHDR=false;cam.allowMSAA=false;
            var rt=new RenderTexture(1400,1000,24);var tex=new Texture2D(1400,1000,TextureFormat.RGB24,false);cam.targetTexture=rt;
            try{
                var st=GameObject.Find("KH_WashStation").transform;var rack=GameObject.Find("KH_Dogs/KH_LeashRack").transform;var board=GameObject.Find("KH_PhotoBoard").transform;var bottle=st.Find("KH_WashBottle_0");
                Action<string,Transform,Vector3,Vector3> shot=(name,t,p,target)=>{cam.transform.position=t.TransformPoint(p);cam.transform.LookAt(t.TransformPoint(target));cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1400,1000),0,0);tex.Apply();string file=name+"-"+stage+".png";File.WriteAllBytes(Images+"/"+file,tex.EncodeToPNG());report.views.Add(new Shot{name=name,file=file,position=cam.transform.position,euler=cam.transform.eulerAngles});};
                shot("shelf",st,new Vector3(1.1f,1.25f,-1.85f),new Vector3(0,.48f,0));
                shot("tools",st,new Vector3(.6f,1.25f,-.85f),new Vector3(0,.87f,0));
                shot("rack",rack,new Vector3(1.15f,.65f,-2.2f),new Vector3(0,.06f,.04f));
                shot("rack-detail",rack,new Vector3(.1f,.35f,-.8f),new Vector3(0,.06f,.04f));
                shot("board",board,new Vector3(2.1f,2.15f,-3.5f),new Vector3(0,1.2f,0));
                shot("board-detail",board,new Vector3(1.85f,2.4f,-1),new Vector3(1.02f,1.88f,0));
                shot("bottle",bottle,new Vector3(-.35f,.10f,.20f),new Vector3(0,.015f,0));
            }finally{RenderTexture.active=previous;cam.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(go);}
            report.dirtyAfter=UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty;File.WriteAllText(Images+"/capture-"+stage+".json",JsonUtility.ToJson(report,true));if(report.dirtyAfter)throw new Exception("Capture dirtied scene");Debug.Log("[PropFinish] captured "+stage+" views="+report.views.Count);
        }
    }
}
