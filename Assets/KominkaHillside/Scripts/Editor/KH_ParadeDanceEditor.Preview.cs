using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using KominkaDogRun.Editor;
using Object = UnityEngine.Object;

namespace KominkaHillside.Editor
{
    public static partial class KH_ParadeDanceEditor
    {
        [MenuItem("Tools/KominkaHillside/Parade/26 - Verify Paw Transparency")]
        public static void VerifyPawTransparency()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit only");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VirtualCampers.LiveGenerator/Shows/kh-halloween-parade/Prefabs/particles_trail_khp_paw__note-track-02-event-1_0.prefab");
            var scene=EditorSceneManager.NewPreviewScene();var old=RenderTexture.active;RenderTexture rt=null;Texture2D tex=null;
            try
            {
                var clone=Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(clone,scene);clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                var ps=clone.GetComponentsInChildren<ParticleSystem>(true).Single(p=>p.name=="Silhouette");var material=ps.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                if(material.shader.name!="Legacy Shaders/Particles/Alpha Blended"||material.mainTexture==null)throw new Exception("Paw straight-alpha material missing");
                clone.SetActive(true);ps.gameObject.SetActive(true);ps.Simulate(.2f,false,true,true);
                var go=new GameObject("PawTransparencyCamera");SceneManager.MoveGameObjectToScene(go,scene);var camera=go.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.08f,.18f,1);camera.orthographic=true;camera.orthographicSize=.18f;camera.nearClipPlane=.01f;camera.farClipPlane=5;camera.transform.position=new Vector3(0,0,-1);
                rt=new RenderTexture(512,512,24);tex=new Texture2D(512,512,TextureFormat.RGB24,false);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,512,512),0,0);tex.Apply();
                Directory.CreateDirectory(Evidence);File.WriteAllBytes(Evidence+"/paw-transparency.png",tex.EncodeToPNG());
                var bg=tex.GetPixel(4,4);var corner=tex.GetPixel(80,80);var paw=tex.GetPixel(256,220);
                float border=Mathf.Abs(bg.r-corner.r)+Mathf.Abs(bg.g-corner.g)+Mathf.Abs(bg.b-corner.b),inside=Mathf.Abs(bg.r-paw.r)+Mathf.Abs(bg.g-paw.g)+Mathf.Abs(bg.b-paw.b);
                File.WriteAllText(Evidence+"/paw-transparency.txt","shader="+material.shader.name+" borderDifference="+border+" pawDifference="+inside+"; Edit particle render, not runtime replay.\n");
                if(border>.03f||inside<.2f)throw new Exception("Paw mask render failed");Debug.Log("PARADE_PAW_TRANSPARENCY_OK");
            }
            finally{RenderTexture.active=old;EditorSceneManager.ClosePreviewScene(scene);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(tex!=null)Object.DestroyImmediate(tex);}
        }
        [MenuItem("Tools/KominkaHillside/Parade/22 - Verify Dance Clips")]
        public static void Verify()
        {
            var scene=EditorSceneManager.NewPreviewScene();var log=new StringBuilder();int bad=0;
            try
            {
                foreach(var def in KD_DogDefs.All)using(var c=new Rig(def,scene))foreach(var move in Moves)
                {
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(PathFor(def.key,move));
                    if(clip==null){bad++;log.AppendLine(def.key+" "+move+" missing");continue;}
                    float maxSupport=0,maxBelow=0,maxRoot=0;
                    var endpoints=new Vector3[c.bones.Length];var rotations=new Quaternion[c.bones.Length];
                    clip.SampleAnimation(c.dog,0);c.Apply();
                    for(int i=0;i<c.bones.Length;i++){endpoints[i]=c.bones[i].localPosition;rotations[i]=c.bones[i].localRotation;}
                    for(int f=0;f<=Mathf.RoundToInt(clip.length*64);f++)
                    {
                        float t=f/64f;c.Reset();clip.SampleAnimation(c.dog,t);c.Apply();
                        for(int l=0;l<4;l++)
                        {
                            var goal=c.feet[l];bool support=true;
                            if(move=="Groove")
                            {
                                int order=l==0?1:l==1?0:l==2?3:2;float a=(t-order*.625f)/.3125f,b=(t-(4+order)*.625f)/.3125f;
                                support=!(a>0&&a<1)&&!(b>0&&b<1);goal.x+=c.step*(Smooth(a)-Smooth(b));
                            }
                            if(move=="Reply")support=!(l==1&&t>0&&t<.625f)&&!(l==0&&t>1.25f&&t<1.875f);
                            if(move=="Hop")support=!(t>.625f&&t<1.25f);
                            var p=c.joints[l][3].position;
                            if(support)maxSupport=Mathf.Max(maxSupport,Vector3.Distance(p,goal));
                            maxBelow=Mathf.Max(maxBelow,goal.y-p.y);
                        }
                        maxRoot=Mathf.Max(maxRoot,c.dog.transform.position.magnitude);
                    }
                    float seam=0,angle=0;for(int i=0;i<c.bones.Length;i++){seam=Mathf.Max(seam,Vector3.Distance(endpoints[i],c.bones[i].localPosition));angle=Mathf.Max(angle,Quaternion.Angle(rotations[i],c.bones[i].localRotation));}
                    bool bindings=AnimationUtility.GetCurveBindings(clip).All(b=>b.path!=""&&!b.propertyName.Contains("Scale")&&c.paths.Contains(b.path));
                    bool ok=maxSupport<.025f&&maxBelow<.02f&&maxRoot<.0001f&&seam<.001f&&angle<.15f&&bindings;
                    if(!ok)bad++;log.AppendLine((ok?"PASS ":"FAIL ")+def.key+" "+move+" sampledSupportGoalError="+maxSupport.ToString("F4")+" belowIdlePaw="+maxBelow.ToString("F4")+" seam="+seam.ToString("F5")+" angle="+angle.ToString("F3")+" root="+maxRoot+" bindings="+bindings);
                }
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);Directory.CreateDirectory(Evidence);File.WriteAllText(Evidence+"/verify.txt",log.ToString()+"Not evidence of runtime, multiplayer, anatomy, body mesh clearance or entertainment acceptance.\n");}
            if(bad!=0)throw new Exception("Dance clip checks failed "+bad+"; see "+Evidence+"/verify.txt");
            Debug.Log("PARADE_DANCE_VERIFY_OK\n"+log);
        }
        // Offline continuity preview, with real clip samples and source dog materials. Does not enter Play.
        [MenuItem("Tools/KominkaHillside/Parade/23 - Render Dance Ensemble")]
        public static void RenderEnsemble()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit only");
            var scene=EditorSceneManager.NewPreviewScene();var rigs=new System.Collections.Generic.List<Rig>();
            var old=RenderTexture.active;RenderTexture rt=null;Texture2D tex=null;
            Directory.CreateDirectory(Evidence+"/frames");
            try
            {
                var defs=KD_DogDefs.All;var clips=new AnimationClip[6,Moves.Length];
                for(int i=0;i<6;i++){var r=new Rig(defs[i],scene);rigs.Add(r);for(int j=0;j<Moves.Length;j++)clips[i,j]=AssetDatabase.LoadAssetAtPath<AnimationClip>(PathFor(defs[i].key,Moves[j]));foreach(var smr in r.dog.GetComponentsInChildren<SkinnedMeshRenderer>(true))smr.updateWhenOffscreen=true;}
                var cameraGo=new GameObject("DancePreviewCamera");SceneManager.MoveGameObjectToScene(cameraGo,scene);
                var cam=cameraGo.AddComponent<Camera>();cam.enabled=false;cam.scene=scene;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.16f,.12f,.20f);cam.fieldOfView=42;cam.nearClipPlane=.05f;cam.farClipPlane=30;cam.useOcclusionCulling=false;
                cam.transform.position=new Vector3(.6f,1.4f,5.8f);cam.transform.LookAt(new Vector3(0,.35f,0));
                var lightGo=new GameObject("DancePreviewLight");SceneManager.MoveGameObjectToScene(lightGo,scene);var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;lightGo.transform.rotation=Quaternion.Euler(40,-25,0);
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,scene);floor.transform.position=new Vector3(0,-.06f,0);floor.transform.localScale=new Vector3(9,.1f,8);
                var mat=new Material(Shader.Find("Standard"));mat.color=new Color(.32f,.25f,.20f);floor.GetComponent<Renderer>().sharedMaterial=mat;
                rt=new RenderTexture(960,540,24);tex=new Texture2D(960,540,TextureFormat.RGB24,false);cam.targetTexture=rt;
                for(int f=0;f<600;f++)
                {
                    float time=f/24f;
                    for(int i=0;i<6;i++)
                    {
                        var r=rigs[i];r.dog.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);r.Reset();
                        // Studio vocabulary sample: groove, call/listen, response/listen, hop, bow.
                        bool reply=time<10?i%2==0:i%2!=0;
                        int move=time<5?0:time<15?(reply?1:(i%2==0?3:4)):time<20?5:2;
                        float phase=move==3||move==4?1.25f:Mathf.Repeat(time,Duration(Moves[move]));
                        if(time>=22.5f)phase=0;
                        clips[i,move].SampleAnimation(r.dog,phase);r.Apply();
                        string key=defs[i].key;bool small=key=="Corgi"||key=="Pome"||key=="Dachs";
                        int column=key=="Corgi"||key=="Shiba"?-1:key=="Pome"||key=="Lab"?0:1;
                        r.dog.transform.SetPositionAndRotation(new Vector3(column*(small?1.2f:1.7f),0,small?1f:-.7f),Quaternion.Euler(0,small?-12:12,0));
                    }
                    cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,960,540),0,0);tex.Apply();File.WriteAllBytes(Evidence+"/frames/"+f.ToString("D4")+".png",tex.EncodeToPNG());
                }
                Object.DestroyImmediate(mat);
            }
            finally{RenderTexture.active=old;foreach(var r in rigs)r.Dispose();EditorSceneManager.ClosePreviewScene(scene);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(tex!=null)Object.DestroyImmediate(tex);}
            File.WriteAllText(Evidence+"/preview.txt","25 seconds, 24 fps, offline actual clip samples. Studio stage, not the world route. No Udon, runtime walking, synchronization or audio acceptance.\n");
            Debug.Log("PARADE_DANCE_ENSEMBLE_OK frames=600");
        }
    }
}
