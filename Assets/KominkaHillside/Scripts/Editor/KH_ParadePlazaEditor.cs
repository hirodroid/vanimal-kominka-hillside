using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UdonSharpEditor;
using VRC.SDKBase;
using VRC.SDK3.Components;
namespace KominkaHillside.Editor {
 public static class KH_ParadePlazaEditor {
  public const string Evidence="docs/plans/halloween-parade/evidence/greetings-v8";
  static GameObject[] Roots(){return new[]{GameObject.Find("KH_ItemGuideBoards/KH_ItemGuideBoard_Hillside"),GameObject.Find("KH_WashStations/Hillside")};}
  static void Guard(){var s=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(EditorApplication.isPlayingOrWillChangePlaymode||s.path!="Assets/Scenes/VRCDefaultWorldScene.unity")throw new Exception("Idle hillside scene required");}
  [MenuItem("Tools/KominkaHillside/Parade/52 - Bind Plaza Visibility")]
  public static void Bind(){Guard();var roots=Roots();if(roots.Any(x=>x==null))throw new Exception("Missing exact plaza roots");var p=UnityEngine.Object.FindObjectOfType<KH_HalloweenParadePlayback>();
   if(roots.SelectMany(x=>x.GetComponentsInChildren<CanvasGroup>(true)).Any(g=>g.ignoreParentGroups)||roots[1].transform.Find("ResetButton")==null||roots[1].GetComponentInChildren<KH_WashSelection>(true)==null||roots[1].GetComponentInChildren<KH_WashStationCall>(true)==null)throw new Exception("Plaza preflight failed");
   p.plazaRenderers=roots.SelectMany(x=>x.GetComponentsInChildren<Renderer>(true)).ToArray();
   p.plazaPickups=roots[1].GetComponentsInChildren<VRC_Pickup>(true);
   p.plazaWashSelection=roots[1].GetComponentInChildren<KH_WashSelection>(true);p.plazaWashCall=roots[1].GetComponentInChildren<KH_WashStationCall>(true);
   if(p.plazaWashSelection==null||p.plazaWashCall==null)throw new Exception("Missing wash binding");
   p.plazaManagedPickups=p.plazaPickups.Select(x=>p.plazaWashSelection.pickups.Contains(x)).ToArray();
   p.plazaRendererTool=p.plazaRenderers.Select(r=>Array.FindIndex(p.plazaPickups,x=>r.transform.IsChildOf(x.transform))).ToArray();
   var platforms=new[]{"Lift","SupportGroundRoot","PersonalFloor"}.Select(n=>roots[1].transform.Find(n)).Where(x=>x!=null).ToArray();
   p.plazaRendererPlatform=p.plazaRenderers.Select(r=>platforms.Any(t=>r.transform.IsChildOf(t))).ToArray();
   p.plazaUiGroups=roots.Select(x=>{var g=x.GetComponent<CanvasGroup>();return g!=null?g:x.AddComponent<CanvasGroup>();}).ToArray();
   if(roots.SelectMany(x=>x.GetComponentsInChildren<CanvasGroup>(true)).Any(g=>g.ignoreParentGroups))throw new Exception("UI ignores parent visibility");
   p.plazaRaycasters=roots.SelectMany(x=>x.GetComponentsInChildren<GraphicRaycaster>(true)).Cast<Behaviour>().ToArray();
   p.plazaUiColliders=roots.SelectMany(x=>x.GetComponentsInChildren<VRCUiShape>(true)).SelectMany(x=>x.GetComponents<Collider>()).Concat(roots[1].transform.Find("ResetButton").GetComponents<Collider>()).Distinct().ToArray();
   UdonSharpEditorUtility.CopyProxyToUdon(p);EditorUtility.SetDirty(p);EditorUtility.SetDirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(p));EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
   Debug.Log("PARADE_PLAZA_BIND_OK renderers="+p.plazaRenderers.Length+" pickups="+p.plazaPickups.Length+" UI="+p.plazaUiColliders.Length);
  }
  [MenuItem("Tools/KominkaHillside/Parade/53 - Verify Plaza Visibility")]
  public static void Verify(){Guard();var roots=Roots();var p=UnityEngine.Object.FindObjectOfType<KH_HalloweenParadePlayback>();var errors=new List<string>();Action<bool,string> need=(ok,msg)=>{if(!ok)errors.Add(msg);};
   need(p.plazaRenderers!=null&&p.plazaRenderers.Length>10,"renderers wired");need(p.plazaUiGroups.Length==2&&p.plazaUiGroups.All(g=>g!=null&&g.alpha==1&&g.interactable&&g.blocksRaycasts),"two idle visible groups");
   need(p.plazaRenderers.Length==p.plazaRendererTool.Length&&p.plazaRenderers.Length==p.plazaRendererPlatform.Length,"renderer maps");need(p.plazaPickups.Length==p.plazaManagedPickups.Length,"pickup map");
   need(p.plazaRenderers.All(r=>r!=null&&roots.Any(x=>r.transform.IsChildOf(x.transform))),"exact target scope");
   need(p.plazaRenderers.Length==roots.Sum(x=>x.GetComponentsInChildren<Renderer>(true).Length),"all target renderers included");
   need(p.plazaRenderers.All(r=>!r.forceRenderingOff),"idle render overlay released");need(roots.All(x=>x.activeSelf),"network roots active");
   need(p.plazaWashSelection.transform.IsChildOf(roots[1].transform)&&p.plazaWashCall.transform.IsChildOf(roots[1].transform),"exact wash runtime");
   need(p.plazaUiColliders.All(c=>c.GetComponent<VRCUiShape>()!=null||c.name=="ResetButton"),"only UI colliders");need(p.plazaUiColliders.All(c=>c.enabled)&&p.plazaRaycasters.All(c=>c.enabled),"idle UI input active");
   var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
   foreach(var field in typeof(KH_HalloweenParadePlayback).GetFields().Where(f=>f.Name.StartsWith("plaza"))){object actual;need(backing.publicVariables.TryGetVariableValue(field.Name,out actual),"backing "+field.Name);var expected=field.GetValue(p);if(expected is Array a&&actual is Array b)need(a.Length==b.Length,"backing count "+field.Name);}
   Directory.CreateDirectory(Evidence);var rows=p.plazaRenderers.Select((r,i)=>AnimationUtility.CalculateTransformPath(r.transform,null)+" tool="+p.plazaRendererTool[i]+" platform="+p.plazaRendererPlatform[i]);
   File.WriteAllText(Evidence+"/plaza-verification.txt",DateTime.UtcNow.ToString("o")+"\nerrors="+errors.Count+"\n"+string.Join("\n",errors)+"\n"+string.Join("\n",rows));if(errors.Count>0)throw new Exception(string.Join(";",errors));Debug.Log("PARADE_PLAZA_VERIFY_OK renderers="+p.plazaRenderers.Length+" pickups="+p.plazaPickups.Length+" UI="+p.plazaUiColliders.Length);
  }
  [MenuItem("Tools/KominkaHillside/Parade/54 - Capture Clear Plaza")]
  public static void Capture(){Guard();var p=UnityEngine.Object.FindObjectOfType<KH_HalloweenParadePlayback>();var off=p.plazaRenderers.Select(x=>x.forceRenderingOff).ToArray();var alpha=p.plazaUiGroups.Select(x=>x.alpha).ToArray();try{foreach(var r in p.plazaRenderers)r.forceRenderingOff=true;foreach(var g in p.plazaUiGroups)g.alpha=0;KH_HalloweenParadePreview.CaptureInternal(false,new[]{175.8f,190.8f,210.8f});}finally{for(int i=0;i<off.Length;i++)p.plazaRenderers[i].forceRenderingOff=off[i];for(int i=0;i<alpha.Length;i++)p.plazaUiGroups[i].alpha=alpha[i];}}
 }
}
