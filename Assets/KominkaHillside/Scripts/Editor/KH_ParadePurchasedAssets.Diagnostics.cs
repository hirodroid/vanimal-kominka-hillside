using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace KominkaHillside.Editor {
public static partial class KH_ParadePurchasedAssets {
 [MenuItem("Tools/KominkaHillside/Parade/43 - Diagnose IKA Pumpkin")]
 public static void Diagnose(){Guard();var p=UnityEngine.Object.FindObjectOfType<KH_HalloweenParadePlayback>();var ps=p.effects.First(s=>s.transform.parent.name.Contains("guide-journey"));var report=new List<string>();
 for(int mode=0;mode<4;mode++){GameObject go;ParticleSystem copy=null;
 if(mode==0){go=new GameObject("static",typeof(MeshFilter),typeof(MeshRenderer));go.GetComponent<MeshFilter>().sharedMesh=Load("Pumpkin");go.GetComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Output+"/Pumpkin.mat");}
 else{go=UnityEngine.Object.Instantiate(ps.gameObject);copy=go.GetComponent<ParticleSystem>();var main=copy.main;main.startSize=1;main.startColor=Color.white;main.startRotation3D=false;main.startRotation=0;var r=copy.GetComponent<ParticleSystemRenderer>();var streams=new List<ParticleSystemVertexStream>();r.GetActiveVertexStreams(streams);report.Add("mode="+mode+" streams="+string.Join(",",streams)+" GPU="+r.enableGPUInstancing+" sheet="+copy.textureSheetAnimation.enabled+" mesh="+r.mesh.name+" mat="+r.sharedMaterial.name);if(mode==2)r.enableGPUInstancing=false;if(mode==3){r.enableGPUInstancing=false;r.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Normal,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV});}copy.Simulate(10,false,true,false);var particles=new ParticleSystem.Particle[8];int n=copy.GetParticles(particles);if(n>0)report.Add("particle color="+particles[0].GetCurrentColor(copy));}
 go.hideFlags=HideFlags.HideAndDontSave;go.SetActive(true);go.transform.SetPositionAndRotation(new Vector3(10000,10000,10000),Quaternion.identity);go.transform.localScale=Vector3.one;if(copy!=null)copy.Simulate(10,false,true,false);var cameraGo=new GameObject("ProbeCamera"){hideFlags=HideFlags.HideAndDontSave};var cam=cameraGo.AddComponent<Camera>();cam.enabled=false;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.2f,.23f,.28f);cam.fieldOfView=35;cam.nearClipPlane=.01f;cam.farClipPlane=20;cam.transform.position=go.transform.position+new Vector3(.1f,.05f,-2.2f);cam.transform.LookAt(go.transform.position);var rt=new RenderTexture(600,600,24);var tex=new Texture2D(600,600,TextureFormat.RGB24,false);var old=RenderTexture.active;try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,600,600),0,0);tex.Apply();File.WriteAllBytes(Evidence+"/pumpkin-probe-"+mode+".png",tex.EncodeToPNG());}finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(tex);rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(cameraGo);UnityEngine.Object.DestroyImmediate(go);}}
 var mesh=Load("Pumpkin");report.Add("uv="+mesh.uv.Length+" vertex="+mesh.vertexCount+" UV min="+new Vector2(mesh.uv.Min(v=>v.x),mesh.uv.Min(v=>v.y))+" max="+new Vector2(mesh.uv.Max(v=>v.x),mesh.uv.Max(v=>v.y)));File.WriteAllText(Evidence+"/pumpkin-probe.txt",string.Join("\n",report));Debug.Log("IKA_PROBE_DONE");
 }
}
}
