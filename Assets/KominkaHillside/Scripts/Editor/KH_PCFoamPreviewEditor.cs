using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    public static class KH_PCFoamPreviewEditor
    {
        [MenuItem("Tools/KominkaHillside/PC Foam/13 - Capture Candidates")]
        public static void Capture()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
            var active=SceneManager.GetActiveScene();bool dirty=active.isDirty;
            string output="Temp/PCFoamPreview/"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");Directory.CreateDirectory(output);
            var notes=new System.Text.StringBuilder("Candidate preview only; authored pose, isolated scene, directional light 1, 640x512, not PCVR or production integration.\n");
            foreach(var wash in UnityEngine.Object.FindObjectsOfType<KH_DogWash>().Where(w=>w.name=="KH_Wash_Shiba"||w.name=="KH_Wash_Pome"||w.name=="KH_Wash_Samoyed"))
            {
                var scene=EditorSceneManager.NewPreviewScene();var owned=new List<UnityEngine.Object>();
                T Own<T>(T o) where T:UnityEngine.Object {o.hideFlags=HideFlags.HideAndDontSave;owned.Add(o);return o;}
                try
                {
                    var body=KH_PCFoamAuditEditor.Body(wash);var baked=Own(new Mesh());body.BakeMesh(baked,true);
                    GameObject Object(string name){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);return go;}
                    MeshRenderer Renderer(string name,Mesh mesh,Material[] materials)
                    {
                        var go=Object(name);go.transform.SetPositionAndRotation(body.transform.position,body.transform.rotation);go.transform.localScale=body.transform.lossyScale;
                        go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterials=materials;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;return r;
                    }
                    Renderer("Body",baked,body.sharedMaterials);
                    var old=Own(new Material(wash.shellRenderer.sharedMaterial));var thin=Own(new Material(AssetDatabase.LoadAssetAtPath<Material>(KH_PCFoamGenerateEditor.ThinPath(wash))));
                    var foamMat=Own(new Material(AssetDatabase.LoadAssetAtPath<Material>(KH_PCFoamGenerateEditor.MaterialPath(wash))));
                    var mask=Own(new Texture2D(256,256,TextureFormat.RGBA32,false,true));mask.wrapMode=TextureWrapMode.Clamp;mask.filterMode=FilterMode.Bilinear;
                    var limits=wash.partFoamLimit??Enumerable.Range(0,16).Select(i=>i==7?.35f:1f).ToArray();
                    foreach(var mat in new[]{old,thin}){mat.SetTexture("_Mask",mask);mat.SetTexture("_PosMap",wash.posMap);mat.SetFloatArray("_BoneToPart",wash.boneToPart);mat.SetFloatArray("_PartFoamLimit",limits);}
                    foamMat.SetTexture("_Mask",mask);foamMat.SetFloatArray("_PartFoamLimit",limits);
                    var overlay=Renderer("Shell",baked,new[]{old});
                    var generated=AssetDatabase.LoadAssetAtPath<Mesh>(KH_PCFoamGenerateEditor.MeshPath(wash));var posed=Own(UnityEngine.Object.Instantiate(generated));
                    var p=generated.vertices;var n=generated.normals;var weights=generated.boneWeights;var binds=generated.bindposes;var bones=body.bones;var inverse=body.transform.worldToLocalMatrix;
                    var matrices=bones.Select((bone,i)=>inverse*bone.localToWorldMatrix*binds[i]).ToArray();
                    for(int i=0;i<p.Length;i++)
                    {
                        var w=weights[i];var point=p[i];var normal=n[i];
                        p[i]=matrices[w.boneIndex0].MultiplyPoint3x4(point)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(point)*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(point)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(point)*w.weight3;
                        n[i]=matrices[w.boneIndex0].MultiplyVector(normal)*w.weight0+matrices[w.boneIndex1].MultiplyVector(normal)*w.weight1+matrices[w.boneIndex2].MultiplyVector(normal)*w.weight2+matrices[w.boneIndex3].MultiplyVector(normal)*w.weight3;
                    }
                    posed.vertices=p;posed.normals=n;var bounds=baked.bounds;bounds.Expand(.15f/wash.meshToWorldScale);posed.bounds=bounds;
                    var foam=Renderer("Foam",posed,new[]{foamMat});
                    var world=baked.vertices.Select(body.transform.TransformPoint).ToArray();var worldBounds=new Bounds(world[0],Vector3.zero);foreach(var v in world)worldBounds.Encapsulate(v);
                    var camera=Object("Camera").AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.orthographic=true;camera.orthographicSize=worldBounds.extents.magnitude*1.12f;
                    camera.nearClipPlane=.01f;camera.farClipPlane=10;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.19f,.21f,.23f);camera.useOcclusionCulling=false;
                    var light=Object("Light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.transform.rotation=Quaternion.Euler(40,-30,0);light.shadows=LightShadows.None;
                    var rt=Own(new RenderTexture(640,512,24));rt.Create();camera.targetTexture=rt;var image=Own(new Texture2D(640,512,TextureFormat.RGB24,false));
                    // Read the existing position map on the GPU into a temporary CPU buffer; authoring import settings stay untouched.
                    var posRt=Own(new RenderTexture(256,256,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear));posRt.Create();Graphics.Blit(wash.posMap,posRt);
                    var posTex=Own(new Texture2D(256,256,TextureFormat.RGBAFloat,false,true));var previous=RenderTexture.active;
                    try{RenderTexture.active=posRt;posTex.ReadPixels(new Rect(0,0,256,256),0,0);posTex.Apply();}finally{RenderTexture.active=previous;}
                    var map=posTex.GetPixels();var sourceBounds=body.sharedMesh.bounds;int axis=sourceBounds.size.x>sourceBounds.size.z?0:2;
                    foreach(string state in new[]{"old-full","new-full","new-flat","new-half","new-band","new-zero"})
                    {
                        foamMat.SetFloat("_NormalScale",state=="new-full"?1f:.85f);foamMat.SetFloat("_Inset",state=="new-full"?0:.03f);
                        float amount=state=="new-half"?.5f:state=="new-zero"?0:1;var pixels=new Color[256*256];
                        for(int i=0;i<pixels.Length;i++)
                        {
                            float f=amount;float coordinate=axis==0?map[i].r:map[i].b;float centre=axis==0?sourceBounds.center.x:sourceBounds.center.z;
                            if(state=="new-band"&&Mathf.Abs(coordinate-centre)*wash.meshToWorldScale<.055f)f=0;
                            pixels[i]=new Color(0,f,state=="new-band"?1:0,1);
                        }
                        mask.SetPixels(pixels);mask.Apply();overlay.sharedMaterial=state=="old-full"?old:thin;foam.enabled=state!="old-full";
                        foreach(string view in new[]{"side","quarter"})
                        {
                            Vector3 offset=view=="side"?new Vector3(2,.25f,0):new Vector3(1.7f,.65f,1.4f);
                            camera.transform.position=worldBounds.center+offset;camera.transform.LookAt(worldBounds.center);
                            camera.Render();previous=RenderTexture.active;
                            try{RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,640,512),0,0);image.Apply();}finally{RenderTexture.active=previous;}
                            string file=KH_PCFoamGenerateEditor.Key(wash)+"-"+state+"-"+view+".png";File.WriteAllBytes(output+"/"+file,image.EncodeToPNG());
                        }
                    }
                    notes.AppendLine(wash.name+" cameraOrtho="+camera.orthographicSize+" bodyBounds="+worldBounds+" foam="+generated.vertexCount/42);
                }
                finally{RenderTexture.active=null;EditorSceneManager.ClosePreviewScene(scene);foreach(var o in owned)if(o!=null)UnityEngine.Object.DestroyImmediate(o);}
            }
            if(active.isDirty!=dirty)throw new InvalidOperationException("Production dirty state changed");
            notes.AppendLine("Production dirty unchanged="+dirty);File.WriteAllText(output+"/capture.txt",notes.ToString());Directory.CreateDirectory("Temp/PCFoamProof");File.WriteAllText("Temp/PCFoamProof/preview-path.txt",output);
        }
    }
}
