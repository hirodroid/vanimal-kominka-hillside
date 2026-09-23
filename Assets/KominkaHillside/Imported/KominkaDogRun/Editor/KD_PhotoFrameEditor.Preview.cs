using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TMPro;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PhotoFrameEditor
    {
        const string PreviewDir = "Logs/photo-frame-preview";
        static Texture2D Render(Camera camera, int width, int height, string name)
        {
            var rt = RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
            var old = RenderTexture.active; var target = camera.targetTexture;
            try
            {
                camera.targetTexture=rt;camera.aspect=(float)width/height;camera.Render();RenderTexture.active=rt;
                var image=new Texture2D(width,height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                if(name!=null)File.WriteAllBytes(PreviewDir+"/"+name+".png",image.EncodeToPNG());
                return image;
            }
            finally { camera.targetTexture=target;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt); }
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Photo Frames/4 - Render Inspection")]
        public static void RenderInspection()
        {
            Guard();Directory.CreateDirectory(PreviewDir);
            var frame=KD_ProductsEditor.SceneComponents<KD_PhotoFrame>().Single();
            var cameraGo=new GameObject("KD_FrameInspection",typeof(Camera)){hideFlags=HideFlags.HideAndDontSave};
            var camera=cameraGo.GetComponent<Camera>();camera.enabled=false;camera.nearClipPlane=.01f;camera.farClipPlane=1000;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.18f,.24f,.30f,1);
            var material=new Material(frame.cameraOverlay.sharedMaterial);var original=frame.cameraOverlay.sharedMaterial;
            bool enabled=frame.cameraOverlay.enabled;float mode=Shader.GetGlobalFloat("_VRChatCameraMode"),mirror=Shader.GetGlobalFloat("_VRChatMirrorMode");
            var verify=new KD_VerifyLog("Photo frame shader and render inspection (Editor simulation only)");
            try
            {
                frame.cameraOverlay.sharedMaterial=material;frame.cameraOverlay.enabled=true;
                camera.transform.position=new Vector3(120,200,120);camera.cullingMask=1<<1;
                Shader.SetGlobalFloat("_VRChatMirrorMode",0);Shader.SetGlobalFloat("_VRChatCameraMode",0);
                var baseline=Render(camera,640,360,null);var pixels=baseline.GetPixels32();
                for(int m=0;m<=3;m++) for(int reflection=0;reflection<=2;reflection++)
                {
                    Shader.SetGlobalFloat("_VRChatCameraMode",m);Shader.SetGlobalFloat("_VRChatMirrorMode",reflection);
                    var test=Render(camera,640,360,null);var data=test.GetPixels32();int changed=0;
                    for(int i=0;i<data.Length;i++)if(!data[i].Equals(pixels[i]))changed++;
                    verify.Check((changed>100)==(m>0 && reflection==0),"mode="+m+" mirror="+reflection+" changed pixels="+changed);
                    Object.DestroyImmediate(test);
                }
                Object.DestroyImmediate(baseline);
                Shader.SetGlobalFloat("_VRChatMirrorMode",0);Shader.SetGlobalFloat("_VRChatCameraMode",0);
                var dog=KD_ProductsEditor.SceneComponents<KD_DogPetController>().First().headAnchor;
                camera.cullingMask=~0;camera.transform.position=dog.position+new Vector3(.7f,.4f,-1.3f);
                camera.transform.LookAt(dog.position);camera.fieldOfView=48;
                var photo=Render(camera,1280,720,"garden-none");
                Shader.SetGlobalFloat("_VRChatCameraMode",1);
                for(int i=0;i<frame.frameKeys.Length;i++)
                {
                    material.SetTexture("_LandscapeTex",frame.landscapeTextures[i]);material.SetTexture("_PortraitTex",frame.portraitTextures[i]);
                    Object.DestroyImmediate(Render(camera,1280,720,Keys[i]+"-landscape"));
                    Object.DestroyImmediate(Render(camera,720,1280,Keys[i]+"-portrait"));
                }
                Object.DestroyImmediate(Render(camera,720,720,"square"));
                RenderAttached(camera,frame,photo,verify);
                Shader.SetGlobalFloat("_VRChatCameraMode",0);
                var panel=frame.toggleButton.GetComponentInParent<Canvas>().transform;
                camera.transform.SetPositionAndRotation(panel.position-panel.forward*.8f,panel.rotation);camera.orthographic=true;camera.orthographicSize=.14f;
                RenderPanelStates(camera,frame,verify);
                camera.orthographicSize=.65f;Object.DestroyImmediate(Render(camera,1600,900,"gallery-context"));
                RenderViewer(camera,frame,photo,verify);Object.DestroyImmediate(photo);
                verify.Check(!ShaderUtil.ShaderHasError(original.shader) && !ShaderUtil.ShaderHasError(frame.viewerOverlay.sharedMaterial.shader),"two shaders compiled without errors");
                verify.Note("Editor camera globals are injected. VRChat camera, Quest and Touch hardware remain unverified.");
                verify.Finish("Logs/imp-cam-007-render.txt");
            }
            finally
            {
                frame.cameraOverlay.sharedMaterial=original;frame.cameraOverlay.enabled=enabled;
                Shader.SetGlobalFloat("_VRChatCameraMode",mode);Shader.SetGlobalFloat("_VRChatMirrorMode",mirror);
                Object.DestroyImmediate(material);Object.DestroyImmediate(cameraGo);
            }
        }
        static void RenderAttached(Camera camera,KD_PhotoFrame frame,Texture2D photo,KD_VerifyLog verify)
        {
            var board=GameObject.CreatePrimitive(PrimitiveType.Quad);board.hideFlags=HideFlags.HideAndDontSave;
            var material=new Material(Shader.Find("KominkaDogRun/KD_PhotoPrint"));material.mainTexture=photo;
            material.SetFloat("_AttachDistance",.35f);material.SetFloat("_AttachMargin",-.01f);
            try
            {
                board.transform.position=new Vector3(120,200,120);board.transform.localScale=new Vector3(1,.5625f,1);
                board.GetComponent<Renderer>().sharedMaterial=material;
                camera.transform.SetPositionAndRotation(board.transform.position-Vector3.forward*.2f,Quaternion.identity);
                Shader.SetGlobalFloat("_VRChatCameraMode",1);camera.cullingMask=(1<<0)|(1<<1);
                frame.cameraOverlay.enabled=false;var baseline=Render(camera,1280,720,null);
                frame.cameraOverlay.enabled=true;var decorated=Render(camera,1280,720,"attached-landscape");
                var a=baseline.GetPixels32();var b=decorated.GetPixels32();int changed=0;
                for(int i=0;i<a.Length;i++)if(!a[i].Equals(b[i]))changed++;
                verify.Check(changed>100,"frame visible over fullscreen attached photo; changed="+changed);
                Object.DestroyImmediate(baseline);Object.DestroyImmediate(decorated);
                Object.DestroyImmediate(Render(camera,720,1280,"attached-portrait"));
            }
            finally { Object.DestroyImmediate(material);Object.DestroyImmediate(board); }
        }
        static void RenderPanelStates(Camera camera,KD_PhotoFrame frame,KD_VerifyLog verify)
        {
            var panel=frame.toggleButton.GetComponentInParent<Canvas>().transform;
            var labels=panel.GetComponentsInChildren<TMP_Text>();var original=labels.Select(l=>l.text).ToArray();
            var catalog=KD_LocalizationEditor.Load();
            var originalColor=frame.toggleButton.targetGraphic.color;
            var originalThumb=frame.toggleThumb.anchoredPosition;
            try
            {
                foreach(string code in new[]{"ja","en"})foreach(bool on in new[]{false,true})
                {
                    typeof(KD_PhotoFrame).GetMethod("ApplyToggleVisual",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(frame,new object[]{on});
                    verify.Check(frame.toggleThumb.anchoredPosition.x==(on?32f:-32f),"switch thumb position "+code+" "+on);
                    foreach(var label in labels)
                    {
                        string key=label==frame.toggleLabel?(on?"photo.frame.on":"photo.frame.off"):
                            label==frame.selectionLabel?(on?"photo.frame.paw":"photo.frame.none"):
                            label==frame.statusLabel?(on?"photo.frame.active":"photo.frame.inactive"):
                            label.name=="Title"?"photo.frame.title":label.transform.parent.name=="Previous"?"photo.frame.previous":"photo.frame.next";
                        label.text=KD_LocalizationEditor.Text(catalog,code,key);label.ForceMeshUpdate();
                        verify.Check(label.font.HasCharacters(label.text) && label.preferredWidth<=label.rectTransform.rect.width+1 && label.preferredHeight<=label.rectTransform.rect.height+1,"panel glyphs and fit "+code+" "+on+" "+label.name);
                    }
                    Object.DestroyImmediate(Render(camera,1280,500,"gallery-panel-"+code+(on?"-on":"-off")));
                }
            }
            finally { for(int i=0;i<labels.Length;i++)labels[i].text=original[i]; frame.toggleButton.targetGraphic.color=originalColor;frame.toggleThumb.anchoredPosition=originalThumb; }
        }
        static void RenderViewer(Camera camera,KD_PhotoFrame frame,Texture2D photo,KD_VerifyLog verify)
        {
            var viewer=KD_ProductsEditor.SceneComponents<KD_PhotoViewer>().Single();var root=viewer.transform;
            var position=root.localPosition;var rotation=root.localRotation;bool active=viewer.panelRoot.activeSelf,enabled=frame.viewerOverlay.enabled;
            var original=viewer.photoRenderer.sharedMaterial;var material=new Material(original);material.mainTexture=photo;
            var frameOriginal=frame.viewerOverlay.sharedMaterial;var frameMaterial=new Material(frameOriginal);
            try
            {
                viewer.photoRenderer.sharedMaterial=material;viewer.panelRoot.SetActive(true);frame.viewerOverlay.enabled=true;frame.viewerOverlay.sharedMaterial=frameMaterial;
                root.SetPositionAndRotation(new Vector3(0,-1000,0),Quaternion.identity);
                camera.transform.SetPositionAndRotation(root.position-Vector3.forward*2,Quaternion.identity);
                camera.orthographicSize=viewer.photoRenderer.bounds.size.x/(2*(720f/1280)) * 1.05f;camera.cullingMask=1<<5;
                float portraitSize=camera.orthographicSize;
                for(int i=0;i<frame.frameKeys.Length;i++)
                {
                    frameMaterial.mainTexture=frame.landscapeTextures[i];camera.orthographicSize=portraitSize;
                    Object.DestroyImmediate(Render(camera,720,1280,"phone-"+frame.frameKeys[i]+"-portrait"));
                    camera.orthographicSize=viewer.photoRenderer.bounds.size.y*.55f;
                    Object.DestroyImmediate(Render(camera,1280,720,"phone-"+frame.frameKeys[i]+"-landscape"));
                }
                verify.Check(frame.viewerOverlay.bounds.size.x<=viewer.photoRenderer.bounds.size.x+.001f,"viewer frame stays within photo width");
            }
            finally { root.localPosition=position;root.localRotation=rotation;viewer.panelRoot.SetActive(active);frame.viewerOverlay.enabled=enabled;viewer.photoRenderer.sharedMaterial=original;frame.viewerOverlay.sharedMaterial=frameOriginal;Object.DestroyImmediate(material);Object.DestroyImmediate(frameMaterial); }
        }
    }
}
