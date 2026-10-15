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
 const string LanternMarker="Assets/KominkaHillside/Data/HalloweenParade/Finale007.json";
 const string LanternEvidence="docs/plans/halloween-parade/evidence/finale-v7";
 static bool buildingLanterns;
 public static bool LanternSelected=>buildingLanterns||File.Exists(LanternMarker)||File.Exists("Assets/KominkaHillside/Data/HalloweenParade/Lantern006.json");
 static Material GlowMaterial(string key,Texture tex,Color color,float gain,bool gold=false,bool halo=false){
  string path=Output+"/Glow_"+key+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
  if(m==null){m=new Material(Shader.Find("KominkaHillside/Parade Lantern Glow"));AssetDatabase.CreateAsset(m,path);}
  m.shader=Shader.Find("KominkaHillside/Parade Lantern Glow");m.mainTexture=tex;m.SetColor("_Color",color);m.SetFloat("_Gain",gain);m.SetFloat("_GoldMask",gold?1:0);m.SetFloat("_Halo",halo?1:0);m.renderQueue=5000;EditorUtility.SetDirty(m);return m;
 }
 static Mesh EmissiveMesh(string key,Mesh source){if(source.subMeshCount<2)throw new Exception("No emissive submesh: "+key);var m=UnityEngine.Object.Instantiate(source);var tris=source.GetTriangles(1);m.subMeshCount=1;m.SetTriangles(tris,0);var points=source.vertices;var bounds=new Bounds(points[tris[0]],Vector3.zero);foreach(int i in tris)bounds.Encapsulate(points[i]);m.bounds=bounds;return SaveMesh(key+"Glow",m);}
 static void PrepareLanterns(){
  Directory.CreateDirectory(LanternEvidence);var tex=SourceMaterials("Candles")[0].mainTexture;
  foreach(var pair in new[]{new[]{"Candle","Candles"},new[]{"CandleBoo","Candles_B"},new[]{"CandlePumpkin","Candle_Pumpkin"},new[]{"PumpkinLamp","Halloween Outside Lamp_04"},new[]{"BatLamp","Halloween Outside Lamp_01"}}){
   var mesh=SaveMesh(pair[0],Normalized(pair[1],0,pair[0].Contains("Lamp")?1.75f:1f,false,pair[0]=="PumpkinLamp"||pair[0]=="CandlePumpkin"));EmissiveMesh(pair[0],mesh);
  }
  EmissiveMesh("LampL",Load("LampL"));EmissiveMesh("LampR",Load("LampR"));
  GlowMaterial("Flame",tex,new Color(1f,.78f,.38f),.85f);
  GlowMaterial("PumpkinCore",tex,new Color(1f,.75f,.25f),3.2f);
  GlowMaterial("PumpkinInterior",Texture2D.whiteTexture,new Color(1f,.55f,.12f),1.65f);
  GlowMaterial("PumpkinShell",tex,new Color(1f,.55f,.18f),.55f,true);
  var hat=GlowMaterial("HatAccent",Texture2D.whiteTexture,new Color(.75f,.38f,1f),.28f);hat.SetFloat("_AccentMode",1);
  var bat=GlowMaterial("BatAccent",Texture2D.whiteTexture,new Color(.38f,.22f,1f),.32f);bat.SetFloat("_AccentMode",2);
  GlowMaterial("Halo",Texture2D.whiteTexture,new Color(1f,.45f,.08f),.55f,false,true);
  var ribbon=AssetDatabase.LoadAssetAtPath<Material>("Assets/KominkaHillside/Data/HalloweenParade/Ribbon/KH_HalloweenRibbon.mat");
  GlowMaterial("Ribbon",ribbon.mainTexture,new Color(1f,.82f,.42f),.85f,true);
  var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);try{SaveMesh("LanternCore",UnityEngine.Object.Instantiate(sphere.GetComponent<MeshFilter>().sharedMesh));}finally{UnityEngine.Object.DestroyImmediate(sphere);}
  // Reusable camera-facing quad, emitted as a small halo, never a realtime Light.
  var q=new Mesh();q.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};q.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};q.triangles=new[]{0,2,1,0,3,2};q.RecalculateNormals();q.RecalculateBounds();SaveMesh("HaloQuad",q);AssetDatabase.SaveAssets();
 }
 static Transform AddNode(string name,Transform parent,Vector3 pos){var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=pos;return t;}
 static MeshRenderer RenderMesh(Transform t,Mesh mesh,Material[] mats){t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;var r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterials=mats;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;return r;}
 static void ClearGeometry(Transform t){foreach(var r in t.GetComponentsInChildren<Renderer>(true))UnityEngine.Object.DestroyImmediate(r);foreach(var f in t.GetComponentsInChildren<MeshFilter>(true))UnityEngine.Object.DestroyImmediate(f);}
 static void DeleteChild(Transform t,string name){var old=t.Find(name);if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);}
 static Transform Halo(Transform parent,Vector3 pos,float radius,string name){var t=AddNode(name,parent,pos);t.localScale=Vector3.one*radius;RenderMesh(t,Load("HaloQuad"),new[]{AssetDatabase.LoadAssetAtPath<Material>(Output+"/Glow_Halo.mat")});return t;}
 static void PumpkinInterior(Transform parent,Vector3 pos,Vector3 radius,string name){var t=AddNode(name,parent,pos);t.localScale=radius*2;RenderMesh(t,Load("LanternCore"),new[]{AssetDatabase.LoadAssetAtPath<Material>(Output+"/Glow_PumpkinInterior.mat")});}
 static void Prop(Transform parent,string name,string meshName,string prefab,Vector3 pos,float scale){
  var t=AddNode(name,parent,pos);t.localScale=Vector3.one*scale;RenderMesh(t,Load(meshName),SourceMaterials(prefab));
  var glow=AddNode("Emission",t,Vector3.zero);RenderMesh(glow,Load(meshName+"Glow"),new[]{AssetDatabase.LoadAssetAtPath<Material>(Output+(meshName.Contains("Pumpkin")?"/Glow_PumpkinCore.mat":"/Glow_Flame.mat"))});
  if(meshName.Contains("Pumpkin")){var shell=AddNode("PumpkinShellGlow",t,Vector3.zero);RenderMesh(shell,Load(meshName),new[]{AssetDatabase.LoadAssetAtPath<Material>(Output+"/Glow_PumpkinShell.mat"),AssetDatabase.LoadAssetAtPath<Material>(Output+"/Glow_PumpkinShell.mat")});}
  if(meshName=="PumpkinLamp"){PumpkinInterior(t,new Vector3(0,1.50f,0),new Vector3(.17f,.085f,.09f),"CoreTop");PumpkinInterior(t,new Vector3(-.48f,.92f,0),new Vector3(.105f,.045f,.06f),"CoreLeft");PumpkinInterior(t,new Vector3(.48f,.92f,0),new Vector3(.105f,.045f,.06f),"CoreRight");}
  if(meshName=="CandlePumpkin")PumpkinInterior(t,new Vector3(0,.78f,0),new Vector3(.16f,.09f,.10f),"Core");
  if(meshName!="PumpkinLamp")Halo(t,Load(meshName+"Glow").bounds.center,.16f,"Halo");
  else {Halo(t,new Vector3(0,1.48f,-.04f),.23f,"HaloTop");Halo(t,new Vector3(-.48f,.92f,-.04f),.17f,"HaloLeft");Halo(t,new Vector3(.48f,.92f,-.04f),.17f,"HaloRight");}
 }
 public static void ApplyLanternCast(Transform root){if(!LanternSelected)return;
  foreach(Transform a in root){var pose=a.Find("Pose");if(a.name.StartsWith("Tree")){
   ClearGeometry(a);DeleteChild(pose,"LanternEnsemble");var group=AddNode("LanternEnsemble",pose,Vector3.zero);float side=a.name.EndsWith("L")?-1:1;
   Prop(group,"CandleTall","CandleBoo","Candles_B",new Vector3(0,.08f,0),1f);
   Prop(group,"CandleLow","Candle","Candles",new Vector3(-.4f,.02f,.25f),.72f);
   Prop(group,"CandlePumpkin","CandlePumpkin","Candle_Pumpkin",new Vector3(.4f,.04f,.15f),.82f);
   Prop(group,"PumpkinStreetlight","PumpkinLamp","Halloween Outside Lamp_04",new Vector3(side*.85f,.04f,-.85f),1f);
  }else if(a.name.StartsWith("Lamp")){
   foreach(string foot in new[]{"FootL","FootR"}){var f=a.Find(foot);if(f!=null)ClearGeometry(f);}
   DeleteChild(pose,"LanternEnsemble");var group=AddNode("LanternEnsemble",pose,Vector3.zero);float side=a.name.EndsWith("L")?-1:1;
   Prop(group,"BatStreetlight","BatLamp","Halloween Outside Lamp_01",new Vector3(side*.95f,.02f,-1.15f),1f);
   Prop(group,"CandleFront","Candle","Candles",new Vector3(side*.55f,.02f,.7f),.65f);
   Prop(group,"CandleRear","CandleBoo","Candles_B",new Vector3(side*.55f,.05f,-.45f),.85f);
   var body=pose.Find("Body");DeleteChild(body,"LanternEmission");var src=body.GetComponent<SkinnedMeshRenderer>();var glow=AddNode("LanternEmission",body,Vector3.zero).gameObject.AddComponent<SkinnedMeshRenderer>();glow.sharedMesh=Load(a.name+"Glow");glow.bones=src.bones;glow.rootBone=src.rootBone;glow.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Output+"/Glow_Flame.mat");glow.localBounds=src.localBounds;glow.updateWhenOffscreen=true;glow.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
   var head=pose.Find("Head");DeleteChild(head,"LanternHalo");Halo(head,Load(a.name+"Glow").bounds.center-new Vector3(.04f,1.12f,0),.22f,"LanternHalo");
  }}
  foreach(Transform a in root){if(!a.name.StartsWith("Hat")&&!a.name.StartsWith("Bat"))continue;var body=a.Find("Pose/Body");DeleteChild(body,"CompanionAccent");var t=AddNode("CompanionAccent",body,Vector3.zero);bool bat=a.name.StartsWith("Bat");var mat=AssetDatabase.LoadAssetAtPath<Material>(Output+(bat?"/Glow_BatAccent.mat":"/Glow_HatAccent.mat"));
   if(bat){var src=body.GetComponent<SkinnedMeshRenderer>();var sm=t.gameObject.AddComponent<SkinnedMeshRenderer>();sm.sharedMesh=src.sharedMesh;sm.sharedMaterial=mat;sm.bones=src.bones;sm.rootBone=src.rootBone;sm.localBounds=src.localBounds;sm.updateWhenOffscreen=true;sm.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;}
   else RenderMesh(t,Load("Hat"),new[]{mat});
  }
 }
 static AnimationCurve Curve(Func<float,float> value){var keys=new List<Keyframe>();for(int i=0;i<=880;i++){float t=i*.25f;keys.Add(new Keyframe(t,value(t)));}var c=new AnimationCurve(keys.ToArray());for(int i=0;i<c.length;i++){AnimationUtility.SetKeyLeftTangentMode(c,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(c,i,AnimationUtility.TangentMode.Linear);}return c;}
 public static void AnimateLanterns(Transform root){if(!LanternSelected)return;var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/KominkaHillside/Data/HalloweenParade/Companions/KH_StoryCast.anim");
  foreach(var b in AnimationUtility.GetCurveBindings(clip))if((b.path.Contains("Tree")||b.path.Contains("Lamp"))&&(b.path.Contains("/Foot")||b.path.Contains("/Arm")||b.path.EndsWith("/Pose")))AnimationUtility.SetEditorCurve(clip,b,null);
  foreach(Transform a in root){if(!a.name.StartsWith("Tree")&&!a.name.StartsWith("Lamp"))continue;float side=a.name.EndsWith("L")?-1:1;string path="StoryCast/"+a.name+"/Pose";
   clip.SetCurve(path,typeof(Transform),"m_LocalPosition.y",Curve(t=>.04f+.035f*Mathf.Sin(t*1.6f+side)));
   clip.SetCurve(path,typeof(Transform),"localEulerAnglesRaw.z",Curve(t=>2*Mathf.Sin(t*1.4f+side)));
   clip.SetCurve(path,typeof(Transform),"localEulerAnglesRaw.x",AnimationCurve.Constant(0,220,0));
   // The normalized pumpkin face points -Z, matching the cast's authored walking front.
   foreach(var f in a.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name!="PumpkinShellGlow"&&(f.sharedMesh==Load("PumpkinLamp")||f.sharedMesh==Load("CandlePumpkin")))){string fp="StoryCast/"+AnimationUtility.CalculateTransformPath(f.transform,root);clip.SetCurve(fp,typeof(Transform),"localEulerAnglesRaw.y",AnimationCurve.Constant(0,220,0));}
   foreach(var f in a.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh==Load("HaloQuad"))){float radius=f.transform.localScale.x;string hp="StoryCast/"+AnimationUtility.CalculateTransformPath(f.transform,root);for(int axis=0;axis<3;axis++)clip.SetCurve(hp,typeof(Transform),"m_LocalScale."+"xyz"[axis],Curve(t=>radius*(1+.12f*Mathf.Sin(t*6+side)+(t>50&&t<65?.55f*Mathf.Pow(Mathf.Max(0,Mathf.Sin((t-(side<0?50:51.25f))*Mathf.PI/1.25f)),6):0)+(t>=185&&t<=200?.25f:0))));}
  }EditorUtility.SetDirty(clip);
 }
 public static void ApplyLanternFinish(KH_HalloweenParadePlayback p){if(!LanternSelected)return;
  var pumpkin=AssetDatabase.LoadAssetAtPath<Material>(Output+"/Pumpkin.mat");pumpkin.renderQueue=5000;pumpkin.SetFloat("_Body",.55f);pumpkin.SetFloat("_Emission",1.7f);EditorUtility.SetDirty(pumpkin);
  string leaderPath=Output+"/LeaderPumpkin.mat";var leader=AssetDatabase.LoadAssetAtPath<Material>(leaderPath);if(leader==null){leader=new Material(pumpkin);AssetDatabase.CreateAsset(leader,leaderPath);}EditorUtility.CopySerialized(pumpkin,leader);leader.name="LeaderPumpkin";leader.SetFloat("_SurfaceEmission",.6f);leader.SetFloat("_Emission",3.2f);leader.renderQueue=5000;EditorUtility.SetDirty(leader);
  // Bake a small warm core into the leader mesh: the opaque shell hides it except through eye/mouth openings.
  var core=UnityEngine.Object.Instantiate(Load("LanternCore"));core.uv=Enumerable.Repeat(new Vector2(.75f,.70f),core.vertexCount).ToArray();core.colors32=Enumerable.Repeat(new Color32(255,255,255,255),core.vertexCount).ToArray();
  var leaderMesh=new Mesh();leaderMesh.CombineMeshes(new[]{new CombineInstance{mesh=Load("Pumpkin"),transform=Matrix4x4.identity},new CombineInstance{mesh=core,transform=Matrix4x4.TRS(new Vector3(0,-.01f,-.03f),Quaternion.identity,new Vector3(.67f,.5f,.6f))}},true,true);UnityEngine.Object.DestroyImmediate(core);leaderMesh.colors32=Enumerable.Repeat(new Color32(255,255,255,255),leaderMesh.vertexCount).ToArray();leaderMesh=SaveMesh("LeaderPumpkin",leaderMesh);
  foreach(var ps in p.effects)if(ps.transform.parent.name.Contains("guide-journey")){var r=ps.GetComponent<ParticleSystemRenderer>();r.mesh=leaderMesh;r.sharedMaterial=leader;}
  foreach(var ps in p.effects){var r=ps.GetComponent<ParticleSystemRenderer>();if(r.mesh==Load("Pumpkin")||r.sharedMaterial==leader)continue;var source=r.sharedMaterial;string sourcePath=AssetDatabase.GetAssetPath(source);if(sourcePath.StartsWith(Output+"/Particle_")){source.renderQueue=5000;continue;}string path=Output+"/Particle_"+AssetDatabase.AssetPathToGUID(sourcePath)+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null){m=new Material(source);AssetDatabase.CreateAsset(m,path);}m.renderQueue=5000;r.sharedMaterial=m;EditorUtility.SetDirty(m);}
  foreach(var costume in p.costumes){foreach(var old in costume.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="LanternRibbonGlow").ToArray())UnityEngine.Object.DestroyImmediate(old.gameObject);var src=costume.GetComponentsInChildren<MeshFilter>(true).First();var t=AddNode("LanternRibbonGlow",src.transform,Vector3.zero);RenderMesh(t,src.sharedMesh,new[]{AssetDatabase.LoadAssetAtPath<Material>(Output+"/Glow_Ribbon.mat")});}
  p.nightMode=UnityEngine.Object.FindObjectOfType<KH_NightMode>();p.nightMode.paradeDarkness=0;UdonSharpEditorUtility.CopyProxyToUdon(p);UdonSharpEditorUtility.CopyProxyToUdon(p.nightMode);EditorUtility.SetDirty(p);EditorUtility.SetDirty(p.nightMode);
 }
 static void PreflightLanterns(KH_HalloweenParadePlayback p,Transform root){
  foreach(string name in new[]{"Candles","Candles_B","Candle_Pumpkin","Halloween Outside Lamp_01","Halloween Outside Lamp_04"}){var prefab=SourcePrefab(name);var mesh=prefab.GetComponent<MeshFilter>().sharedMesh;var mats=SourceMaterials(name);if(mesh==null||mesh.subMeshCount<2||mesh.GetTriangles(1).Length==0||mesh.uv.Length!=mesh.vertexCount||mats.Length<2||mats.Any(m=>m==null||m.mainTexture==null||!m.shader.isSupported))throw new Exception("Invalid lantern source "+name);}
  if(Shader.Find("KominkaHillside/Parade Lantern Glow")==null||!Shader.Find("KominkaHillside/Parade Lantern Glow").isSupported)throw new Exception("Glow shader unavailable");
  var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/KominkaHillside/Data/HalloweenParade/Companions/KH_StoryCast.anim");if(clip==null)throw new Exception("Cast animation missing");
  var night=UnityEngine.Object.FindObjectOfType<KH_NightMode>();if(night==null||night.overlayRenderer==null||night.skyHolder==null)throw new Exception("Night references missing");
  foreach(string name in new[]{"TreeL","TreeR","LampL","LampR"}){var a=root.Find(name);var pose=a==null?null:a.Find("Pose");if(pose==null)throw new Exception("Missing pose "+name);if(name.StartsWith("Lamp")){var body=pose.Find("Body");var skin=body==null?null:body.GetComponent<SkinnedMeshRenderer>();if(skin==null||skin.sharedMesh==null||skin.bones.Length!=2||skin.bones.Any(b=>b==null)||pose.Find("Head")==null)throw new Exception("Missing lamp skin "+name);if(Load(name).subMeshCount<2||Load(name).GetTriangles(1).Length==0)throw new Exception("Lamp emission missing");}}
  Load("Hat");foreach(Transform a in root){if(!a.name.StartsWith("Hat")&&!a.name.StartsWith("Bat"))continue;var body=a.Find("Pose/Body");if(body==null)throw new Exception("Missing accent body "+a.name);if(a.name.StartsWith("Bat")){var skin=body.GetComponent<SkinnedMeshRenderer>();if(skin==null||skin.sharedMesh==null||skin.rootBone==null||skin.bones.Length!=3||skin.bones.Any(b=>b==null))throw new Exception("Missing bat accent skin "+a.name);}}
  if(p.costumes.Any(c=>c==null||!c.GetComponentsInChildren<MeshFilter>(true).Any(f=>f.sharedMesh!=null)))throw new Exception("Ribbon mesh missing");
  if(p.effects==null||p.effects.Any(e=>e==null||e.GetComponent<ParticleSystemRenderer>()==null||e.GetComponent<ParticleSystemRenderer>().sharedMaterial==null))throw new Exception("Effect material missing");
  if(p.effects.Count(e=>e.transform.parent.name.Contains("guide-journey"))!=1)throw new Exception("Exactly one leader pumpkin required");
  var ribbon=AssetDatabase.LoadAssetAtPath<Material>("Assets/KominkaHillside/Data/HalloweenParade/Ribbon/KH_HalloweenRibbon.mat");if(ribbon==null||ribbon.mainTexture==null)throw new Exception("Ribbon atlas missing");Load("Pumpkin");
 }
 [MenuItem("Tools/KominkaHillside/Parade/46 - Apply Lantern Parade")]
 public static void ApplyLanternParade(){Guard();var p=UnityEngine.Object.FindObjectOfType<KH_HalloweenParadePlayback>();var root=p.stageRoot.transform.Find("StoryCast");if(root==null||root.childCount!=18||p.costumes.Length!=6||UnityEngine.Object.FindObjectOfType<KH_NightMode>()==null)throw new Exception("Parade wiring incomplete");
  PreflightLanterns(p,root);PrepareLanterns();buildingLanterns=true;try{ApplyLanternCast(root);AnimateLanterns(root);ApplyLanternFinish(p);AssetDatabase.SaveAssets();File.WriteAllText(LanternMarker,"{\"revision\":7,\"darkness\":0.72,\"candles\":10,\"streetlights\":6}\n");}finally{buildingLanterns=false;}EditorSceneManager.MarkSceneDirty(p.gameObject.scene);File.WriteAllText(LanternEvidence+"/apply.txt",DateTime.UtcNow.ToString("o")+"\n10 candles, 6 streetlights, no tree geometry/no feet; emission after night overlay, ribbon gold patterns; night dedicated max input.\n");Debug.Log("LANTERN_PARADE_APPLIED");
 }
}
}
