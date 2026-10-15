using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UdonSharpEditor;
namespace KominkaHillside.Editor {
public static partial class KH_ParadePurchasedAssets {
 const string Source="Assets/IKA 3DCG art studio/Halloween Decoration_2022/";
 const string Output="Assets/KominkaHillside/Data/HalloweenParade/IKA2022";
 const string Evidence="docs/plans/halloween-parade/evidence/ika-v5";
 static string[] Names={"Hat-shaped shelves","Witch","Hat","Bat","Ornamental Pumpkin_01","Ornamental Pumpkin_02","Pumpkin_01","Pumpkin_02","Pumpkin_03","Halloween Outside Lamp_01","Halloween Outside Lamp_02","Halloween Outside Lamp_03","Halloween Outside Lamp_04"};
 static void Guard(){if(EditorApplication.isPlayingOrWillChangePlaymode||UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new Exception("Saved Edit scene required");}
 [MenuItem("Tools/KominkaHillside/Parade/40 - Inspect IKA Assets")]
 public static void Inspect(){InspectModels(Names,Evidence);}
 [MenuItem("Tools/KominkaHillside/Parade/45 - Inspect Lantern Assets")]
 public static void InspectLanterns(){InspectModels(new[]{"Candles","Candles_B","Candles_O1","Candles_O2","Candle_Pumpkin","Halloween Outside Lamp_01","Halloween Outside Lamp_04"},"docs/plans/halloween-parade/evidence/lantern-v6");}
 static void InspectModels(string[] names,string evidence){Guard();Directory.CreateDirectory(evidence+"/source");var lines=new List<string>();
 foreach(var name in names){var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Source+name+".prefab");if(prefab==null)throw new Exception(name);var go=UnityEngine.Object.Instantiate(prefab);go.hideFlags=HideFlags.HideAndDontSave;go.transform.position=new Vector3(10000,10000,10000);var rs=go.GetComponentsInChildren<MeshRenderer>(true);var bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);
 lines.Add(name+" size="+bounds.size.ToString("F4")+" center="+(bounds.center-go.transform.position).ToString("F4"));foreach(var f in go.GetComponentsInChildren<MeshFilter>(true))lines.Add("  "+f.name+" mesh="+f.sharedMesh.name+" tri="+f.sharedMesh.triangles.Length/3+" bounds="+f.sharedMesh.bounds+" scale="+f.transform.localScale+" mats="+string.Join(",",f.GetComponent<Renderer>().sharedMaterials.Select(m=>m.name+"/"+m.shader.name)));
 var camGo=new GameObject("InspectionCamera"){hideFlags=HideFlags.HideAndDontSave};var cam=camGo.AddComponent<Camera>();cam.enabled=false;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.2f,.23f,.28f);cam.fieldOfView=35;cam.nearClipPlane=.001f;cam.farClipPlane=100;var max=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);cam.transform.position=bounds.center+new Vector3(.22f,.15f,name.StartsWith("Ornamental")||name=="Halloween Outside Lamp_04"||name=="Candle_Pumpkin"?1:-1)*max*2.2f;cam.transform.LookAt(bounds.center);var rt=new RenderTexture(600,600,24);var tex=new Texture2D(600,600,TextureFormat.RGB24,false);var old=RenderTexture.active;try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,600,600),0,0);tex.Apply();File.WriteAllBytes(evidence+"/source/"+name+".png",tex.EncodeToPNG());}finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(tex);rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(camGo);UnityEngine.Object.DestroyImmediate(go);}}
 File.WriteAllText(evidence+"/source/inspection.txt",DateTime.UtcNow.ToString("o")+"\n"+string.Join("\n",lines));Debug.Log("IKA_INSPECTED "+names.Length);
 }
}
}
