using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UdonSharpEditor;
namespace KominkaHillside.Editor {
public static partial class KH_ParadePurchasedAssets {
 [MenuItem("Tools/KominkaHillside/Parade/47 - Verify Lantern Parade")]
 public static void VerifyLanterns(){var p=UnityEngine.Object.FindObjectOfType<KH_HalloweenParadePlayback>();var root=p.stageRoot.transform.Find("StoryCast");var errors=new List<string>();var log=new List<string>{DateTime.UtcNow.ToString("o")};
  var meshes=root.GetComponentsInChildren<MeshFilter>(true);int candles=meshes.Count(f=>f.name!="PumpkinShellGlow"&&new[]{Load("Candle"),Load("CandleBoo"),Load("CandlePumpkin")}.Contains(f.sharedMesh));int addedLamps=meshes.Count(f=>f.name!="PumpkinShellGlow"&&(f.sharedMesh==Load("PumpkinLamp")||f.sharedMesh==Load("BatLamp")));
  if(candles!=10||addedLamps!=4)errors.Add("Cast count "+candles+"/"+addedLamps);if(root.GetComponentsInChildren<Renderer>(true).Any(r=>r.name.StartsWith("Foot")))errors.Add("Foot renderer remains");
  if(meshes.Any(f=>AssetDatabase.GetAssetPath(f.sharedMesh).Contains("/Companions/Tree")||AssetDatabase.GetAssetPath(f.sharedMesh).Contains("Foot")))errors.Add("Old tree/feet mesh remains");
  if(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length!=28)errors.Add("Expected 14 base skins + 2 lamp emission + 12 bat accent skins");
  foreach(var r in root.GetComponentsInChildren<Renderer>(true))if(r.sharedMaterials.Any(m=>m==null||m.shader==null||!m.shader.isSupported))errors.Add("Material/shader "+r.name);
  if(p.costumes.Count(c=>c.GetComponentsInChildren<Renderer>(true).Count(r=>r.name=="LanternRibbonGlow")==1)!=6)errors.Add("6 ribbon glow renderers required");
  if(p.nightMode==null||p.nightMode.paradeDarkness!=0)errors.Add("Night idle wiring");object actual;var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(p);if(!backing.publicVariables.TryGetVariableValue("nightMode",out actual)||!Equals(actual,UdonSharpEditorUtility.GetBackingUdonBehaviour(p.nightMode)))errors.Add("Night backing reference");
  foreach(var ps in p.effects)if(ps.GetComponent<ParticleSystemRenderer>().sharedMaterial.renderQueue!=5000)errors.Add("Particle queue "+ps.name);
  var leaders=p.effects.Where(ps=>ps.transform.parent.name.Contains("guide-journey")).ToArray();if(leaders.Length!=1||leaders[0].GetComponent<ParticleSystemRenderer>().sharedMaterial.GetFloat("_SurfaceEmission")<=0)errors.Add("Leader emission missing");else log.Add("leader surface emission="+leaders[0].GetComponent<ParticleSystemRenderer>().sharedMaterial.GetFloat("_SurfaceEmission"));
  var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/KominkaHillside/Data/HalloweenParade/Companions/KH_StoryCast.anim");if(AnimationUtility.GetCurveBindings(clip).Any(b=>(b.path.Contains("Tree")||b.path.Contains("Lamp"))&&(b.path.Contains("/Foot")||b.path.Contains("/Arm"))))errors.Add("Old step/drum curves remain");
  var desc=UnityEngine.Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();if(desc.NetworkIDCollection.Any(n=>n.gameObject==null))errors.Add("NetworkID null");
  foreach(var f in meshes.Where(f=>f.name!="PumpkinShellGlow"&&(f.sharedMesh==Load("PumpkinLamp")||f.sharedMesh==Load("CandlePumpkin")))){string fp="StoryCast/"+AnimationUtility.CalculateTransformPath(f.transform,root);var yaw=AnimationUtility.GetEditorCurve(clip,EditorCurveBinding.FloatCurve(fp,typeof(Transform),"localEulerAnglesRaw.y"));if(yaw==null||yaw.keys.Any(k=>Mathf.Abs(k.value)>.01f))errors.Add("Pumpkin reversal remains: "+fp);}
  foreach(string actor in new[]{"TreeL","TreeR"})foreach(float t in new[]{95f,100f,110f,120f}){string ap="StoryCast/"+actor;var x=AnimationUtility.GetEditorCurve(clip,EditorCurveBinding.FloatCurve(ap,typeof(Transform),"m_LocalPosition.x"));var z=AnimationUtility.GetEditorCurve(clip,EditorCurveBinding.FloatCurve(ap,typeof(Transform),"m_LocalPosition.z"));var y=AnimationUtility.GetEditorCurve(clip,EditorCurveBinding.FloatCurve(ap,typeof(Transform),"localEulerAnglesRaw.y"));float heading=Mathf.Atan2(x.Evaluate(t+.125f)-x.Evaluate(t-.125f),z.Evaluate(t+.125f)-z.Evaluate(t-.125f))*Mathf.Rad2Deg;float delta=Mathf.Abs(Mathf.DeltaAngle(heading,y.Evaluate(t)+180));log.Add(actor+" travel t="+t+" faceDelta="+delta);if(delta>4)errors.Add("Walking pumpkin heading "+actor+" at "+t);}
  if(p.music.clip==null||Mathf.Abs(p.music.clip.length-220)>.01f)errors.Add("Music must remain220s");else log.Add("audio="+AssetDatabase.GetAssetPath(p.music.clip)+" duration="+p.music.clip.length);
  foreach(float t in new[]{-1f,0,2,4,100,215,217.5f,220})log.Add("darkness("+t+")="+KH_HalloweenParadePlayback.NightEnvelope(t,220));
  foreach(string name in new[]{"Candle","CandleBoo","CandlePumpkin","LampL","PumpkinLamp"})log.Add(name+" emissive bounds="+Load(name+"Glow").bounds);
  log.Add("candles="+candles+" streetlights="+(addedLamps+2)+" ribbons=6 dirty="+p.gameObject.scene.isDirty);log.Add(errors.Count==0?"PASS Edit structure only":string.Join("\n",errors));Directory.CreateDirectory(LanternEvidence);File.WriteAllText(LanternEvidence+"/verify.txt",string.Join("\n",log));if(errors.Count>0)throw new Exception(string.Join(";",errors));Debug.Log("LANTERN_VERIFIED");
 }
}
}
