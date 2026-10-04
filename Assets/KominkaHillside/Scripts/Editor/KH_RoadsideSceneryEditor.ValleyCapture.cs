using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KominkaHillside.Editor
{
    public static partial class KH_RoadsideSceneryEditor
    {
        [Serializable] class ValleyCaptureRecord {public string name,file;public Vector3 position,euler;public float eyeHeight,darkness;public bool additions;}
        [Serializable] class ValleyCaptureReport {public string utc,scene;public List<ValleyCaptureRecord> views=new List<ValleyCaptureRecord>();public bool dirtyAfter;public string limitations="Editor mono 960x600, FOV60, near0.1/far2200. Before and after are separate actual captures at identical fixed views. Night uses cloned existing overlay/sky at darkness0.8; no Udon, stereo or device verification.";}
        [MenuItem("Tools/KominkaHillside/Course/151 - Capture River Verge After")]
        public static void CaptureValleyAfter(){ Init();CaptureValley("after"); }
        [MenuItem("Tools/KominkaHillside/Course/153 - Extend River Verge Before Views")]
        public static void ExtendValleyBefore()
        {
            Init();var addition=GameObject.Find(ValleyRoot);var originals=new List<TerrainData>();var copies=new List<TerrainData>();
            try
            {
                foreach(var t in terrains)
                {
                    var td=t.terrainData;int n=td.alphamapResolution;originals.Add(td);var copy=UnityEngine.Object.Instantiate(td);copies.Add(copy);
                    var old=new float[n,n,4];var raw=File.ReadAllBytes(ValleyEvidence+"/"+td.name+".alphas");Buffer.BlockCopy(raw,0,old,0,raw.Length);copy.SetAlphamaps(0,0,old);t.terrainData=copy;t.Flush();
                }
                addition.SetActive(false);CaptureValley("before");
            }
            finally
            {
                addition.SetActive(true);for(int i=0;i<originals.Count;i++){terrains[i].terrainData=originals[i];terrains[i].Flush();}foreach(var c in copies)UnityEngine.Object.DestroyImmediate(c);
            }
            if(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Before capture dirtied scene");
        }
        static void CaptureValley(string stage)
        {
            Init();var root=GameObject.Find(RootName);var old=GameObject.Find("KH_World/KH_Places/KH_Viewpoint/KH_Bench").GetComponent<MeshRenderer>();
            var night=UnityEngine.Object.FindObjectOfType<KH_NightMode>();var descriptor=UnityEngine.Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
            if(root==null||night==null||descriptor==null)throw new InvalidOperationException("Capture sources required");
            var plazaBench=root.transform.Find("RestBenches/UpperPlazaBench");var riverBench=root.transform.Find("RestBenches/RiverBench");
            if(plazaBench==null||riverBench==null)throw new InvalidOperationException("Rest benches required");
            string dir=ValleyImages;Directory.CreateDirectory(dir);
            var report=new ValleyCaptureReport{utc=DateTime.UtcNow.ToString("o"),scene=root.scene.path};
            var originalRT=RenderTexture.active;bool active=root.activeSelf,oldEnabled=old.enabled;
            GameObject cameraGo=null,overlayGo=null;RenderTexture rt=null;Texture2D pixels=null;Material sky=null,overlay=null;
            try
            {
                cameraGo=new GameObject("KH_TempRoadsideCamera"){hideFlags=HideFlags.HideAndDontSave};var camera=cameraGo.AddComponent<Camera>();camera.CopyFrom(descriptor.ReferenceCamera.GetComponent<Camera>());camera.enabled=false;camera.cameraType=CameraType.Game;camera.stereoTargetEye=StereoTargetEyeMask.None;camera.fieldOfView=60;camera.aspect=960f/600;camera.nearClipPlane=.1f;camera.farClipPlane=2200;camera.allowHDR=false;camera.allowMSAA=false;
                rt=new RenderTexture(960,600,24,RenderTextureFormat.ARGB32){hideFlags=HideFlags.HideAndDontSave};rt.Create();camera.targetTexture=rt;pixels=new Texture2D(960,600,TextureFormat.RGB24,false);
                sky=new Material(UnityEngine.RenderSettings.skybox){hideFlags=HideFlags.HideAndDontSave};cameraGo.AddComponent<Skybox>().material=sky;
                overlay=new Material(night.overlayRenderer.sharedMaterial){hideFlags=HideFlags.HideAndDontSave};overlayGo=new GameObject("KH_TempRoadsideNight"){hideFlags=HideFlags.HideAndDontSave,layer=night.overlayRenderer.gameObject.layer};overlayGo.AddComponent<MeshFilter>().sharedMesh=night.overlayRenderer.GetComponent<MeshFilter>().sharedMesh;
                var overlayRenderer=overlayGo.AddComponent<MeshRenderer>();overlayRenderer.sharedMaterial=overlay;overlayRenderer.shadowCastingMode=ShadowCastingMode.Off;overlayRenderer.allowOcclusionWhenDynamic=false;
                int[] nodes={280,287,292};
                for(int view=0;view<7;view++)for(int low=0;low<2;low++)for(int mode=0;mode<2;mode++)
                {
                    float eye=low==0?1.62f:.4f;Vector3 p,target;string name;
                    if(view<3){int node=nodes[view];p=route._Position(node);target=route._Position(node+3)+Vector3.up*eye;name="curve-"+node;}
                    else if(view==3){p=riverBench.position+riverBench.rotation*new Vector3(2.5f,0,-3);target=riverBench.position+Vector3.up*.3f;name="river-rest";}
                    else if(view==4){p=route._ProjectToTrail(new Vector3(490,0,305));var bank=KH_CourseGroundEditor.RiverAt(305);target=new Vector3(bank.x,bank.y,305);name="water-access";}
                    else if(view==5){p=riverBench.position+riverBench.forward;target=new Vector3(KH_CourseGroundEditor.RiverAt(285).x,10,285);name="rest-view";}
                    else{p=new Vector3(496,0,276);target=new Vector3(512,14,288);name="tree-and-bench";}
                    p.y=Ground(p)+eye;camera.transform.SetPositionAndRotation(p,Quaternion.LookRotation(target-p));
                    bool after=stage=="after";float dark=mode==1?.8f:0;overlayRenderer.enabled=dark>0;overlay.SetFloat("_Darkness",dark);
                    sky.SetFloat("_Exposure",Mathf.Lerp(KH_NightMode.DaySkyExposure,KH_NightMode.NightSkyExposure,dark));
                    sky.SetColor("_Tint",Color.Lerp(new Color(KH_NightMode.DayTintR,KH_NightMode.DayTintG,KH_NightMode.DayTintB,KH_NightMode.SkyTintA),new Color(KH_NightMode.NightTintR,KH_NightMode.NightTintG,KH_NightMode.NightTintB,KH_NightMode.SkyTintA),dark));
                    camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,960,600),0,0);pixels.Apply();
                    string file=name+(low==0?"-adult":"-low")+"-"+stage+(mode==1?"-night":"")+".png";File.WriteAllBytes(dir+"/"+file,pixels.EncodeToPNG());
                    report.views.Add(new ValleyCaptureRecord{name=name,file=file,position=p,euler=camera.transform.eulerAngles,eyeHeight=eye,darkness=dark,additions=after});
                }
            }
            finally
            {
                root.SetActive(active);old.enabled=oldEnabled;RenderTexture.active=originalRT;
                if(cameraGo!=null)UnityEngine.Object.DestroyImmediate(cameraGo);if(overlayGo!=null)UnityEngine.Object.DestroyImmediate(overlayGo);
                if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);
                if(sky!=null)UnityEngine.Object.DestroyImmediate(sky);if(overlay!=null)UnityEngine.Object.DestroyImmediate(overlay);
            }
            report.dirtyAfter=root.scene.isDirty;File.WriteAllText(dir+"/capture-"+stage+".json",JsonUtility.ToJson(report,true));
            if(report.dirtyAfter)throw new InvalidOperationException("Capture dirtied scene");Debug.Log("[Roadside] Comparison images="+report.views.Count+" state restored");
        }
    }
}
