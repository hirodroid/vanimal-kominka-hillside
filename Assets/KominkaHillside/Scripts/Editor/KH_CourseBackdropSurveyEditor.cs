using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // Edit-mode ablation: capture the same view with one background part hidden, then restore it.
    public static class KH_CourseBackdropSurveyEditor
    {
        [Serializable] public class Part
        {public string name,mesh,material,shader,texture,keywords;public bool enabled;public Vector3 center,size;public int vertices;public float uvScale;}
        [Serializable] public class View
        {public int sourceIndex,node,quarter;public string variant,image;public Vector3 position,euler;}
        [Serializable] public class Report
        {public string utc,scene,directory;public bool dirtyAfter,restored;public List<Part> parts=new List<Part>();public List<View> views=new List<View>();public string limits="Editor daytime mono ablation, not stereo/device acceptance. Hiding a part identifies its contribution, not permission to remove it from the finished world.";}
        [MenuItem("Tools/KominkaHillside/Course/58 - Survey Background Boundaries")]
        public static void Capture()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var source=JsonUtility.FromJson<KH_CourseScenerySurveyEditor.Report>(File.ReadAllText("Temp/des-env-002/scenery-survey/20260926-193308/report.json"));
            var backdrop=GameObject.Find("KH_World/KH_Scenery/KH_ViewSet");
            var renderers=backdrop.GetComponentsInChildren<MeshRenderer>(true);
            var margin=GameObject.Find("KH_CourseExtension/LandscapeMargin").GetComponent<MeshRenderer>();
            var hills=renderers.Single(r=>r.name=="MS_BroadHills01");
            var forest=renderers.Single(r=>r.name=="MS_BroadHillsForest01");
            var targets=new[]{margin,hills,forest};var enabled=targets.Select(r=>r.enabled).ToArray();
            if(enabled.Any(v=>!v))throw new InvalidOperationException("Expected all three background parts visible");
            var report=new Report{utc=DateTime.UtcNow.ToString("o"),scene=SceneManager.GetActiveScene().path,directory="Temp/des-env-002/background-survey/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")};
            Directory.CreateDirectory(report.directory);
            foreach(var r in targets)
            {
                var mesh=r.GetComponent<MeshFilter>().sharedMesh;var mat=r.sharedMaterial;
                string slot=mat.HasProperty("_AlbedoTex")?"_AlbedoTex":"_MainTex";
                report.parts.Add(new Part{name=r.name,mesh=AssetDatabase.GetAssetPath(mesh),vertices=mesh.vertexCount,material=AssetDatabase.GetAssetPath(mat),shader=mat.shader.name,texture=AssetDatabase.GetAssetPath(mat.GetTexture(slot)),keywords=string.Join(",",mat.shaderKeywords),uvScale=mat.HasProperty("_SAUVScale")?mat.GetFloat("_SAUVScale"):-1,enabled=r.enabled,center=r.bounds.center,size=r.bounds.size});
            }
            GameObject go=null;RenderTexture target=null;Texture2D texture=null;var previous=RenderTexture.active;double start=EditorApplication.timeSinceStartup;
            try
            {
                var descriptor=UnityEngine.Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
                go=new GameObject("KH_TempBackdropSurvey"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();
                camera.CopyFrom(descriptor.ReferenceCamera.GetComponent<Camera>());camera.enabled=false;camera.cameraType=CameraType.Game;camera.stereoTargetEye=StereoTargetEyeMask.None;
                camera.fieldOfView=60;camera.aspect=16f/9f;camera.nearClipPlane=.3f;camera.farClipPlane=2200;camera.allowHDR=false;camera.allowMSAA=false;camera.useOcclusionCulling=true;
                target=new RenderTexture(768,432,24,RenderTextureFormat.ARGB32){hideFlags=HideFlags.HideAndDontSave};target.Create();camera.targetTexture=target;
                texture=new Texture2D(768,432,TextureFormat.RGB24,false){hideFlags=HideFlags.HideAndDontSave};
                foreach(int index in new[]{27,31,47,59,63})
                {
                    var v=source.views.Single(x=>x.index==index);camera.transform.SetPositionAndRotation(v.position,Quaternion.Euler(v.euler));
                    for(int variant=0;variant<=targets.Length;variant++)
                    {
                        if(EditorApplication.timeSinceStartup-start>90)throw new TimeoutException("Background survey exceeded 90 seconds");
                        for(int i=0;i<targets.Length;i++)targets[i].enabled=enabled[i]&&variant!=i+1;
                        camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,768,432),0,0);texture.Apply();
                        string label=variant==0?"baseline":"without-"+targets[variant-1].name;
                        string path=report.directory+"/view-"+index+"-"+label+".png";File.WriteAllBytes(path,texture.EncodeToPNG());
                        report.views.Add(new View{sourceIndex=index,node=v.node,quarter=v.quarter,variant=label,image=path,position=v.position,euler=v.euler});
                    }
                }
            }
            finally
            {
                for(int i=0;i<targets.Length;i++)targets[i].enabled=enabled[i];
                RenderTexture.active=previous;if(go!=null)UnityEngine.Object.DestroyImmediate(go);if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
                report.restored=targets.Select((r,i)=>r.enabled==enabled[i]).All(v=>v);report.dirtyAfter=SceneManager.GetActiveScene().isDirty;
                File.WriteAllText(report.directory+"/report.json",JsonUtility.ToJson(report,true));File.WriteAllText("Temp/des-env-002/background-survey/latest.json",JsonUtility.ToJson(report,true));
            }
            if(!report.restored||report.dirtyAfter)throw new InvalidOperationException("Survey did not restore clean state");
            Debug.Log("[KH Background Survey] Images="+report.views.Count+" restored=True dirty=False "+report.directory);
        }
    }
}
