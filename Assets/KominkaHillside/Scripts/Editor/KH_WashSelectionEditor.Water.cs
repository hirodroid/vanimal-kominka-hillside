using System;using System.IO;using System.Linq;using System.Text;using System.Reflection;using System.Collections.Generic;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using UdonSharpEditor;using VRC.SDKBase;
namespace KominkaHillside.Editor{public static partial class KH_WashSelectionEditor{
 // IMP-DOG-049 / IMP-DOG-044 stage A (2026-10-11): the three 0.4.0 items into the four existing stations. WashPremium/12 imports them,
 // 49 places them (the scene parts 48 does not make: IMP-DOG-047 step 3) and then runs the 48 body (ConfigureStation + ConfigureBoard),
 // so every per-item array grows to the 20 ids. Owned by this code, written on every run (idempotent), checked by 44.
 //  - WaterPistol (17): water slot 2, Gun (FBX WaterPistol_GunHold), one shot per press (pulse WaterPistolShot s = the cadence), a small
 //    gravity-affected burst (the "water ball"), wash ray 4 m. Stands on its grip on the tray like the foam cannon.
 //  - Hose (18): water slot 2, Gun (FBX Hose_GunHold), hold to spray (KH_WashTool.PulseHold), a thicker stream than the shower, ray 2.5 m.
 //    0.485 m tall (the short hose hangs 0.335 m under the hold), so no flat pose fits the tray (0.42 deep, 0.23 free across between the
 //    mitt and the towel): it is hung over the tray's front lip, barrel on the tray, grip / coupling / hose in front of the tray (HoseRestLocal).
 //  - PoolBath (19): under Lift/WashTable at the origin like the hinoki platform, mount depth .036. The shared rectangular walkthrough
 //    collider (layer 17) stays as it is; per-dog sizes (IMP-ENV-013) are not applied (the lab's default: the pool stays 1.3 x 0.7).
 // Sprays: the free shower's material and collision (World, Default + Environment, never floor 22) and its trigger kill on the dogs' part
 // colliders (KH_DogWashSetupEditor.Station SprayParticles). Values are provisional (Play): plan 2026-10-11 §3.2 / §3.6 budgets.
 static readonly int[] AddonItems={WaterPistolId,HoseId,PoolBathId};
 // The hose model inside its slot (slot space, identity rotation): barrel bottom (model y .074 for z > -.011) on the slot's rest height,
 // grip / coupling (z <= -.011) 1.1 cm in front of the tray's front edge (z -.18) at the tray's height. Predicted with the FBX vertices
 // (node, 2026-10-11): 0 vertices inside the tray box (3 mm margin); footprint x -1.104..-1.036, z -.286..-.067, y .411..0.896 (station).
 public static readonly Vector3 HoseRestLocal=new Vector3(0f,-.074f,-.14f);
 public const float WaterPistolShot=.4f;
 // hose stream: about 44 alive (rate x lifetime) under the plan's 48 per stream; reaches ~2.6 m (speed x lifetime) for the 2.5 m ray
 public const float HoseSpeedMin=6f,HoseSpeedMax=7f,HoseLifetime=.4f,HoseRate=110f,HoseSizeMin=.02f,HoseSizeMax=.034f,HoseGravity=.6f,HoseAngle=3f;public const int HoseMaxParticles=48;
 // pistol shot: 10 drops at once (the plan's 8-12), ~4.5 m flight, slight arc (gravity .5) for the 4 m ray
 public const float PistolSpeedMin=8f,PistolSpeedMax=8.5f,PistolLifetime=.55f,PistolSizeMin=.022f,PistolSizeMax=.03f,PistolGravity=.5f,PistolAngle=1.5f;public const int PistolDrops=10;
 // a shot lives 0.55 s and the next may come after 0.4 s: room for two overlapping bursts, or the second one starves (Codex review 2026-10-11;
 // Stop() leaves the live drops flying)
 public const int PistolMaxParticles=2*PistolDrops+4;
 const string SprayMaterialPath="Assets/KominkaHillside/Data/Wash/KH_WashParticle_Spray.mat";   // the free shower's spray (queue 3000)
 static bool HangsOverFront(int id){return id==HoseId;}
 static T[] Grow<T>(T[] a,int n){if(a!=null&&a.Length==n)return a;var b=new T[n];if(a!=null)Array.Copy(a,b,Math.Min(a.Length,n));return b;}
 /// <summary>An existing instance of the item's prefab directly under parent (by name and prefab path), else a new one at the origin.</summary>
 static GameObject PlaceModel(int id,Transform parent,StringBuilder log)
 {
  string path=Prefabs+Names[id]+".prefab";
  var found=parent.Cast<Transform>().FirstOrDefault(t=>t.name==Names[id]&&PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)&&PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject)==path);
  if(found!=null)return found.gameObject;
  if(AssetDatabase.LoadAssetAtPath<GameObject>(path)==null)throw new Exception("Missing "+path+" (run WashPremium/12 first)");
  var go=Model(id,parent);Undo.RegisterCreatedObjectUndo(go,"Place 0.4.0 item");log.AppendLine("  created "+PathOf(go.transform));return go;
 }
 static ParticleSystem WaterSpray(Transform nozzle,bool shot,KH_WashSelection sel)
 {
  var t=nozzle.Find("UseMist");var go=t!=null?t.gameObject:Child(nozzle,"UseMist",Vector3.zero).gameObject;
  go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.identity;go.transform.localScale=Vector3.one;
  var ps=go.GetComponent<ParticleSystem>();if(ps==null)ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
  var main=ps.main;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.World;main.loop=!shot;main.duration=shot?WaterPistolShot:1f;
  main.startLifetime=shot?PistolLifetime:HoseLifetime;main.startSpeed=shot?new ParticleSystem.MinMaxCurve(PistolSpeedMin,PistolSpeedMax):new ParticleSystem.MinMaxCurve(HoseSpeedMin,HoseSpeedMax);
  main.startSize=shot?new ParticleSystem.MinMaxCurve(PistolSizeMin,PistolSizeMax):new ParticleSystem.MinMaxCurve(HoseSizeMin,HoseSizeMax);
  main.gravityModifier=shot?PistolGravity:HoseGravity;main.maxParticles=shot?PistolMaxParticles:HoseMaxParticles;main.startColor=shot?new Color(.82f,.93f,.98f,.9f):new Color(.80f,.92f,.97f,.8f);
  var em=ps.emission;em.enabled=true;em.rateOverTime=shot?0f:HoseRate;em.SetBursts(shot?new[]{new ParticleSystem.Burst(0f,(short)PistolDrops)}:new ParticleSystem.Burst[0]);
  var sh=ps.shape;sh.enabled=true;sh.shapeType=ParticleSystemShapeType.Cone;sh.angle=shot?PistolAngle:HoseAngle;sh.radius=shot?.004f:.01f;sh.position=Vector3.zero;sh.rotation=Vector3.zero;
  var col=ps.collision;col.enabled=true;col.type=ParticleSystemCollisionType.World;col.mode=ParticleSystemCollisionMode.Collision3D;col.collidesWith=SprayMask;
  col.lifetimeLoss=1f;col.bounce=0f;col.dampen=1f;col.quality=ParticleSystemCollisionQuality.Medium;col.sendCollisionMessages=false;
  // the dogs' part colliders kill a drop (the free shower's trigger): the wash itself still comes from the tool's rays
  var parts=sel.slots[Slots[WaterPistolId]].washes==null?new Collider[0]:sel.slots[Slots[WaterPistolId]].washes.Where(w=>w!=null&&w.partColliders!=null).SelectMany(w=>w.partColliders).Where(c=>c!=null).ToArray();
  var tr=ps.trigger;while(tr.colliderCount>0)tr.RemoveCollider(0);foreach(var c in parts)tr.AddCollider(c);tr.enabled=parts.Length>0;
  tr.enter=ParticleSystemOverlapAction.Kill;tr.inside=ParticleSystemOverlapAction.Kill;tr.exit=ParticleSystemOverlapAction.Ignore;tr.outside=ParticleSystemOverlapAction.Ignore;tr.radiusScale=1f;
  var cl=ps.colorOverLifetime;cl.enabled=true;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0f),new GradientColorKey(Color.white,1f)},new[]{new GradientAlphaKey(shot?.9f:.8f,0f),new GradientAlphaKey(shot?.5f:.4f,1f)});cl.color=g;
  var r=go.GetComponent<ParticleSystemRenderer>();var mat=AssetDatabase.LoadAssetAtPath<Material>(SprayMaterialPath);if(mat==null)throw new Exception("Missing "+SprayMaterialPath);
  r.sharedMaterial=mat;r.renderMode=shot?ParticleSystemRenderMode.Billboard:ParticleSystemRenderMode.Stretch;r.velocityScale=.02f;r.lengthScale=1.5f;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;
  EditorUtility.SetDirty(ps);EditorUtility.SetDirty(r);return ps;
 }
 /// <summary>Places / refreshes the three 0.4.0 items on one station and grows the per-item arrays to Names.Length (existing entries kept).
 /// Does not apply: the caller runs ConfigureStation (the 48 body). Returns the number of models created.</summary>
 static int PlaceAddonItems(KH_WashSelection sel,StringBuilder log)
 {
  string label=sel.transform.parent.name;int n=Names.Length,created=0;
  if(sel.slots==null||sel.slots.Length!=SlotCategories.Length||sel.slots.Any(x=>x==null))throw new Exception(label+": expected "+SlotCategories.Length+" pickup slots");
  var table=sel.transform.parent.Find("Lift/WashTable");if(table==null)throw new Exception(label+": Lift/WashTable missing");
  sel.models=Grow(sel.models,n);sel.nozzleAnchors=Grow(sel.nozzleAnchors,n);sel.gripAnchors=Grow(sel.gripAnchors,n);sel.gunHolds=Grow(sel.gunHolds,n);
  sel.colliderCenters=Grow(sel.colliderCenters,n);sel.colliderSizes=Grow(sel.colliderSizes,n);sel.pulseDurations=Grow(sel.pulseDurations,n);sel.itemSprays=Grow(sel.itemSprays,n);sel.itemSounds=Grow(sel.itemSounds,n);
  foreach(int id in AddonItems)
  {
   bool tool=Groups[id]<KH_WashSelection.Categories;var parent=tool?sel.slots[Slots[id]].transform:table;
   int before=parent.childCount;var go=PlaceModel(id,parent,log);if(parent.childCount>before)created++;
   sel.models[id]=go;go.transform.localRotation=Quaternion.identity;go.transform.localScale=Vector3.one;
   if(!tool){go.transform.localPosition=Vector3.zero;EditorUtility.SetDirty(go.transform);log.AppendLine("  "+label+" "+Names[id]+" under "+PathOf(table)+" at the origin (mount depth "+BathDepth(id).ToString("F3")+")");continue;}
   if(id==HoseId)go.transform.localPosition=HoseRestLocal;
   else {go.transform.localPosition=Vector3.zero;var tb=TightBounds(go,parent);go.transform.localPosition=Vector3.up*Mathf.Max(0f,-tb.min.y);}   // stands on its lowest point (the grip), as 42 lifted the tools
   EditorUtility.SetDirty(go.transform);
   var noz=Anchor(go,"NozzleAnchor");var grip=Anchor(go,"GripAnchor");if(noz==null||grip==null||FbxGunHold(sel,id)==null)throw new Exception(label+": "+Names[id]+" anchors missing (NozzleAnchor / GripAnchor / GunHold)");
   sel.nozzleAnchors[id]=noz;sel.gripAnchors[id]=grip;sel.pulseDurations[id]=PulseSeconds(id);sel.itemSounds[id]=Clip("WaterJet");
   if(sel.itemSounds[id]==null)throw new Exception("Missing "+Data+"Audio/WaterJet.wav");
   sel.itemSprays[id]=WaterSpray(noz,id==WaterPistolId,sel);
   log.AppendLine("  "+label+" "+Names[id]+" in "+PathOf(parent)+" at "+go.transform.localPosition.ToString("F3")+" pulse="+sel.pulseDurations[id]+" range="+SprayRange(id)+" spray="+PathOf(sel.itemSprays[id].transform)+" triggers="+sel.itemSprays[id].trigger.colliderCount);
  }
  EditorUtility.SetDirty(sel);return created;
 }
 [MenuItem(Menu+"49 - Place 0.4.0 Items")]
 public static void PlaceAddons()
 {
  Guard();var scene=EditorSceneManager.GetActiveScene();if(scene.isDirty)throw new Exception("Save/reconcile unrelated scene edits first");
  var sels=UnityEngine.Object.FindObjectsOfType<KH_WashSelection>(true).Where(x=>x.gameObject.scene==scene).OrderBy(x=>x.transform.parent.name).ToArray();
  if(sels.Length!=4)throw new Exception("Expected four selectors, found "+sels.Length);
  foreach(int id in AddonItems){var p=AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+Names[id]+".prefab");if(p==null||p.GetComponentsInChildren<Renderer>(true).Any(r=>r.sharedMaterial==null))throw new Exception(Names[id]+": prefab missing or without materials (run WashPremium/12 first)");}
  var ws=CheckedWords();var log=new StringBuilder();int created=0,removed=0;
  CleanNetworkIds(log,"before");
  foreach(var sel in sels)
  {
   created+=PlaceAddonItems(sel,log);
   removed+=ConfigureStation(sel,log);
   var b=sel.transform.parent.GetComponentInChildren<KH_WashSelectionBoard>(true);if(b==null)throw new Exception(sel.transform.parent.name+": no PremiumSelectionBoard");ConfigureBoard(b,sel,ws);
  }
  // the inactive gallery of 2026-10-08 holds the offered items (KH_WashPremiumEditor 21 counts Order.Length): add the three
  var gallery=scene.GetRootGameObjects().FirstOrDefault(x=>x.name=="KH_WashPremiumPreview_Inactive");int galleryAdded=0;
  if(gallery!=null)foreach(int id in AddonItems)
  {
   if(gallery.transform.Cast<Transform>().Any(t=>t.name==Names[id]))continue;
   var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+Names[id]+".prefab"));go.transform.SetParent(gallery.transform,false);Undo.RegisterCreatedObjectUndo(go,"Gallery 0.4.0");
   go.transform.localPosition=id==PoolBathId?new Vector3(3f,.5f,.65f):new Vector3(id==WaterPistolId?-1.2f:-.8f,.6f,-1.1f);go.transform.localRotation=id==PoolBathId?Quaternion.identity:Quaternion.Euler(0,180,0);
   log.AppendLine("  gallery + "+PathOf(go.transform));galleryAdded++;
  }
  CleanNetworkIds(log,"after");
  EditorSceneManager.MarkSceneDirty(scene);
  Log("place-040","selectors="+sels.Length+" created models="+created+" (12 on the first run, 0 after) removed station objects="+removed+" gallery added="+galleryAdded+" offered="+string.Join(",",Order.Select(i=>Names[i]))+"\n"+log+"NOT saved: run 44 - Verify Selection and Wash/12, then save. After saving, run 49 (or 48) once more if 44 reports typeless NetworkIDs left");
 }
 // ---- 44: the tray, the 0.4.0 items per station ----
 /// <summary>Every tool's footprint inside the tray (the hung hose: across inside, back edge inside, front over the lip) and no tool mesh
 /// through the tray box (vertices at the slot's home, 3 mm margin).</summary>
 static void VerifyTray(KH_WashSelection sel,List<KeyValuePair<int,Rect>> boxes,Action<bool,string> check,StringBuilder log)
 {
  string st=sel.transform.parent.name;float x0=TrayLocal.x-TrayScale.x*.5f,x1=TrayLocal.x+TrayScale.x*.5f,y0=TrayLocal.y-TrayScale.y*.5f,y1=TrayLocal.y+TrayScale.y*.5f,z0=TrayLocal.z-TrayScale.z*.5f,z1=TrayLocal.z+TrayScale.z*.5f;const float e=1e-4f,m=.003f;
  foreach(var b in boxes)
  {
   var r=b.Value;bool hung=HangsOverFront(b.Key);
   if(!hung)check(r.xMin>=x0-e&&r.xMax<=x1+e&&r.yMin>=z0-e&&r.yMax<=z1+e,"footprint "+Names[b.Key]+" inside the tray "+st+" (x "+r.xMin.ToString("F3")+".."+r.xMax.ToString("F3")+" z "+r.yMin.ToString("F3")+".."+r.yMax.ToString("F3")+"; tray x "+x0.ToString("F3")+".."+x1.ToString("F3")+" z "+z0.ToString("F3")+".."+z1.ToString("F3")+")");
   else check(r.xMin>=x0-e&&r.xMax<=x1+e&&r.yMax<=z1+e&&r.yMin<z0,"footprint "+Names[b.Key]+" hung over the tray's front lip "+st+" (x "+r.xMin.ToString("F3")+".."+r.xMax.ToString("F3")+" z "+r.yMin.ToString("F3")+".."+r.yMax.ToString("F3")+"; front edge z "+z0.ToString("F3")+")");
  }
  foreach(int i in Order)
  {
   int c=CategoryOf(i);if(c<0||c>=KH_WashSelection.Categories||sel.models[i]==null)continue;var slot=sel.slots[Slots[i]].transform;var home=sel.categoryHomes[c].localPosition;
   int inside=0,total=0;float frontMost=float.NegativeInfinity;string err=null;
   foreach(var mf in sel.models[i].GetComponentsInChildren<MeshFilter>(true))
   {
    if(mf.sharedMesh==null)continue;Vector3[] vs;try{vs=mf.sharedMesh.vertices;}catch(Exception ex){err=ex.Message;continue;}
    foreach(var v in vs){var p=slot.InverseTransformPoint(mf.transform.TransformPoint(v))+home;total++;
     if(p.x>=x0-m&&p.x<=x1+m&&p.y>=y0-m&&p.y<=y1+m&&p.z>=z0-m&&p.z<=z1+m)inside++;
     if(p.y<=y1+m&&p.y>=y0-m&&p.x>=x0-m&&p.x<=x1+m)frontMost=Mathf.Max(frontMost,p.z);}
   }
   check(err==null&&total>0&&inside==0,"no "+Names[i]+" vertex inside the tray box "+st+" ("+inside+" / "+total+(err!=null?"; "+err:"")+")");
   if(HangsOverFront(i))log.AppendLine("hang "+st+" "+Names[i]+": nearest vertex at the tray's height z "+frontMost.ToString("F4")+" (front edge "+z0.ToString("F3")+", clearance "+((z0-frontMost)*100f).ToString("F1")+" cm); model at "+sel.models[i].transform.localPosition.ToString("F3"));
  }
 }
 /// <summary>44, per station: the 0.4.0 items' placement, Gun holds (measured), sprays, materials and the platform mount depth.</summary>
 static void VerifyAddonItems(KH_WashSelection sel,Action<bool,string> check,StringBuilder log)
 {
  string st=sel.transform.parent.name;if(sel.models==null||sel.models.Length<Names.Length)return;   // VerifyCategories already failed the lengths
  foreach(int id in AddonItems){var m=sel.models[id];check(m!=null&&PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(m)==Prefabs+Names[id]+".prefab","0.4.0 "+Names[id]+" instance of its prefab "+st);}
  if(AddonItems.Any(id=>sel.models[id]==null))return;
  // placement
  var hose=sel.models[HoseId].transform;check(hose.parent==sel.slots[Slots[HoseId]].transform&&(hose.localPosition-HoseRestLocal).sqrMagnitude<1e-8f&&Quaternion.Angle(hose.localRotation,Quaternion.identity)<.01f,"Hose hung at "+HoseRestLocal.ToString("F3")+" in slot"+Slots[HoseId]+" "+st+" (actual "+hose.localPosition.ToString("F3")+")");
  var pistol=sel.models[WaterPistolId].transform;var ptb=TightBounds(pistol.gameObject,pistol.parent);check(pistol.parent==sel.slots[Slots[WaterPistolId]].transform&&Mathf.Abs(ptb.min.y)<.001f,"WaterPistol stands on its grip in slot"+Slots[WaterPistolId]+" "+st+" (lowest point "+(ptb.min.y*1000f).ToString("F1")+" mm from the slot's rest height)");
  var pool=sel.models[PoolBathId].transform;var table=sel.transform.parent.Find("Lift/WashTable");check(pool.parent==table&&pool.localPosition.sqrMagnitude<1e-10f&&Quaternion.Angle(pool.localRotation,Quaternion.identity)<.01f&&(pool.localScale-Vector3.one).sqrMagnitude<1e-10f,"PoolBath under Lift/WashTable at the origin, scale 1 "+st);
  // the platforms' mount: the model's *_SupportMount sits where the support's top goes (the table collider's bottom - bathDepths)
  var box=table.GetComponent<BoxCollider>();
  foreach(int b in new[]{13,PoolBathId})
  {
   var mount=Anchor(sel.models[b],"SupportMount");if(box==null||mount==null){check(false,"mount anchor / table collider "+Names[b]+" "+st);continue;}
   float bottom=box.transform.TransformPoint(box.center-Vector3.up*box.size.y*.5f).y,measured=bottom-mount.position.y;
   check(Mathf.Abs(measured-sel.bathDepths[b])<.001f,"mount depth "+Names[b]+" "+st+" measured "+measured.ToString("F4")+" m = bathDepths "+sel.bathDepths[b].ToString("F3"));
  }
  // Gun holds of the water guns (the FoamCannon's own lines stay in VerifyGunHolds)
  foreach(int id in GunItems)
  {
   if(id==0)continue;var hold=sel.gunHolds[id];var model=sel.models[id].transform;var noz=sel.nozzleAnchors[id];
   bool wired=hold!=null&&hold.name==GunHoldName(id)&&hold.IsChildOf(model)&&noz!=null;
   check(wired,Names[id]+" gunHolds = "+GunHoldName(id)+" under the model "+st+" (actual "+(hold==null?"null":hold.name)+")");if(!wired)continue;
   float aimErr=Vector3.Angle(hold.right,noz.forward),upErr=Vector3.Angle(hold.forward,model.up);var nozzleInHold=hold.InverseTransformPoint(noz.position);
   var rest=Anchor(sel.models[id],"RestAnchor");
   log.AppendLine("hold "+Names[id]+" "+st+" sprayInHold="+hold.InverseTransformDirection(noz.forward).ToString("F3")+" modelUpInHold="+hold.InverseTransformDirection(model.up).ToString("F3")+" aimErr="+aimErr.ToString("F2")+"deg upErr="+upErr.ToString("F2")+"deg holdInModel="+model.InverseTransformPoint(hold.position).ToString("F3")+" nozzleInHold="+nozzleInHold.ToString("F3")+(rest!=null?" lowestInHold="+hold.InverseTransformPoint(rest.position).ToString("F3"):""));
   check(aimErr<.5f,Names[id]+" Exact Gun +X = spray direction "+st+" (measured "+aimErr.ToString("F2")+" deg)");
   check(upErr<.5f,Names[id]+" Exact Gun +Z = model up, no roll "+st+" (measured "+upErr.ToString("F2")+" deg)");
   check(nozzleInHold.x>.12f,Names[id]+" nozzle ahead of the hand along the aim "+st+" (measured "+nozzleInHold.x.ToString("F3")+" m)");
  }
  // sprays (input per item and the particles)
  foreach(int id in new[]{WaterPistolId,HoseId})
  {
   var ps=sel.itemSprays[id];bool shot=id==WaterPistolId;if(ps==null){check(false,Names[id]+" spray "+st);continue;}var main=ps.main;var col=ps.collision;var r=ps.GetComponent<ParticleSystemRenderer>();
   check(ps.transform.parent==sel.nozzleAnchors[id]&&!main.playOnAwake&&main.loop==!shot&&main.simulationSpace==ParticleSystemSimulationSpace.World&&col.enabled&&col.type==ParticleSystemCollisionType.World&&col.collidesWith.value==SprayMask&&ps.trigger.enabled&&ps.trigger.colliderCount>0&&r!=null&&r.sharedMaterial!=null&&r.sharedMaterial.renderQueue==3000,
    Names[id]+" spray "+st+": under its nozzle, "+(shot?"one burst of "+(ps.emission.burstCount>0?ps.emission.GetBurst(0).count.constant.ToString("F0"):"0"):"loop "+ps.emission.rateOverTime.constant.ToString("F0")+"/s")+", speed "+main.startSpeed.constantMin.ToString("F1")+".."+main.startSpeed.constantMax.ToString("F1")+", life "+main.startLifetime.constant.ToString("F2")+" s, gravity "+main.gravityModifier.constant.ToString("F2")+", max "+main.maxParticles+", world collision mask "+col.collidesWith.value+", dog triggers "+ps.trigger.colliderCount+", material "+(r==null||r.sharedMaterial==null?"-":r.sharedMaterial.name+" queue "+r.sharedMaterial.renderQueue));
   if(shot)check(ps.emission.burstCount==1&&Mathf.Abs(ps.emission.GetBurst(0).count.constant-PistolDrops)<.5f&&ps.emission.rateOverTime.constant==0f&&main.maxParticles>=2*PistolDrops,"WaterPistol shot = one burst of "+PistolDrops+" drops, maxParticles "+main.maxParticles+" >= two overlapping shots ("+(2*PistolDrops)+"; life "+PistolLifetime.ToString("F2")+" s > cadence "+WaterPistolShot.ToString("F2")+" s) "+st);
   else check(main.maxParticles<=HoseMaxParticles&&ps.emission.rateOverTime.constant*main.startLifetime.constant<=HoseMaxParticles,"Hose stream alive <= "+HoseMaxParticles+" particles "+st);
   check(sel.itemSounds[id]!=null&&sel.itemSounds[id].name=="WaterJet","sound "+Names[id]+" WaterJet "+st);
  }
  // transparent materials on the station instances, and the queue order against the particles / the wet shell
  var window=sel.models[WaterPistolId].GetComponentsInChildren<Renderer>(true).FirstOrDefault(x=>x.name=="WaterPistol_Window");var water=sel.models[PoolBathId].GetComponentsInChildren<Renderer>(true).FirstOrDefault(x=>x.name=="PoolBath_Water");
  var mist=AssetDatabase.LoadAssetAtPath<Material>(Data+"WashMist.mat");var spray=AssetDatabase.LoadAssetAtPath<Material>(SprayMaterialPath);
  bool queues=window!=null&&water!=null&&window.sharedMaterial!=null&&water.sharedMaterial!=null&&window.sharedMaterial.name=="PremiumWash_Window"&&water.sharedMaterial.name=="PremiumWash_Water"&&mist!=null&&spray!=null
   &&window.sharedMaterial.renderQueue<mist.renderQueue&&water.sharedMaterial.renderQueue<mist.renderQueue&&water.sharedMaterial.renderQueue<spray.renderQueue&&water.sharedMaterial.renderQueue<3010;
  check(queues,"transparent queues "+st+": PremiumWash_Water "+(water==null||water.sharedMaterial==null?"-":water.sharedMaterial.renderQueue.ToString())+" / PremiumWash_Window "+(window==null||window.sharedMaterial==null?"-":window.sharedMaterial.renderQueue.ToString())+" < WashMist "+(mist==null?"-":mist.renderQueue.ToString())+" / KH_WashParticle_Spray "+(spray==null?"-":spray.renderQueue.ToString())+" < wet shell 3010 (the sort itself: Play)");
 }
 // ---- 44: the watering can's tilt-to-pour (IMP-DOG-049, 2026-10-11) ----
 static bool SameExact(Vector3 a,Vector3 b){return a.x==b.x&&a.y==b.y&&a.z==b.z;}
 static bool SameExact(Quaternion a,Quaternion b){return a.x==b.x&&a.y==b.y&&a.z==b.z&&a.w==b.w;}
 /// <summary>Edit mode, on the proxy, while VerifyCategoryLogic has the can out on the water slot: runs the runtime's own bodies
 /// (KH_WashTool MeasuredTilt / ShowModelTilt / RestModel / FlowFraction, by reflection) and prints the actual values: the thresholds,
 /// the can's spout at rest (an upright can measures 0 although its rose points below the horizon), the VR measure of the pickup leaned
 /// by known angles (the pickup root turned about the world axis and put back), the Desktop lean of the model about its grip (the spout
 /// goes down, the grip stays) and its exact restore. Every transform it turns is put back in finally; nothing is marked dirty.</summary>
 static void VerifyCanTilt(KH_WashSelection sel,Action<bool,string> check,StringBuilder log)
 {
  string st=sel.transform.parent.name;var t=sel.slots[Slots[WateringCanId]];var model=sel.models[WateringCanId].transform;var noz=sel.nozzleAnchors[WateringCanId];var grip=sel.gripAnchors[WateringCanId];
  const BindingFlags bf=BindingFlags.Instance|BindingFlags.NonPublic;
  Func<string,object[],object> call=(name,args)=>{var mi=typeof(KH_WashTool).GetMethod(name,bf);if(mi==null)throw new Exception("KH_WashTool."+name+" not found (the tilt bodies)");return mi.Invoke(t,args);};
  float start=KH_WashTool.TiltStartDegrees,stop=KH_WashTool.TiltStopDegrees,max=KH_WashTool.TiltMaxDegrees,desk=KH_WashTool.DesktopTiltDegrees;
  check(t.pulseSeconds==KH_WashTool.PulseTilt&&t.nozzle==noz&&sel.gunHolds[WateringCanId]==null&&sel.pickups[Slots[WateringCanId]].orientation==VRC_Pickup.PickupOrientation.Grip,
   "WateringCan on slot"+Slots[WateringCanId]+" "+st+": tilt to pour (pulse "+t.pulseSeconds+" = KH_WashTool.PulseTilt), Grip, its own nozzle");
  // a press never switches the can on by itself (no toggle): it only marks Use held (Desktop / Touch lean; ignored in VR), the frame pours
  Func<string,bool> flag=name=>(bool)typeof(KH_WashTool).GetField(name,bf).GetValue(t);
  bool lpOk;try{var lp0=Networking.LocalPlayer;lpOk=true;}catch(Exception){lpOk=false;}
  if(!lpOk)log.AppendLine("NOT_TESTED WateringCan Use press in Edit mode "+st+": Networking.LocalPlayer throws outside Play (the harness P-selection-water-tilt covers the press)");
  else try{t.OnPickupUseDown();bool downOn=flag("_sprayOn"),downHeld=flag("_useHeld");t.OnPickupUseUp();bool upHeld=flag("_useHeld");
   check(!downOn&&downHeld&&!upHeld&&!flag("_sprayOn"),"WateringCan Use press does not toggle "+st+" (after Use down: spray "+downOn+", Use held "+downHeld+" (Edit mode = not VR: the Desktop lean); after Use up: held "+upHeld+")");}
  catch(Exception e){check(false,"WateringCan Use press in Edit mode threw "+(e.InnerException??e).GetType().Name+": "+(e.InnerException??e).Message+" "+st);}
  float flowStart=(float)call("FlowFraction",new object[]{start}),flowMid=(float)call("FlowFraction",new object[]{(start+max)*.5f}),flowMax=(float)call("FlowFraction",new object[]{max});
  check(0f<stop&&stop<start&&start<max&&max<=desk&&KH_WashTool.DesktopTiltSeconds>0f&&Mathf.Abs(flowStart-KH_WashTool.TiltMinFlow)<1e-5f&&Mathf.Abs(flowMax-1f)<1e-5f&&flowStart<flowMid&&flowMid<flowMax,
   "WateringCan tilt thresholds (KH_WashTool, plan 2026-10-11 §3.2, provisional): pours from "+start+" deg, stops below "+stop+" deg, full flow at "+max+" deg; flow "+flowStart.ToString("F2")+" / "+flowMid.ToString("F2")+" / "+flowMax.ToString("F2")+" x the item's rate at "+start+" / "+((start+max)*.5f)+" / "+max+" deg; Desktop / Touch leans the model to "+desk+" deg in "+KH_WashTool.DesktopTiltSeconds+" s while Use is held");
  float restPitch=Mathf.Asin(Mathf.Clamp(noz.forward.y,-1f,1f))*Mathf.Rad2Deg;float rest=(float)call("MeasuredTilt",null);
  check(Mathf.Abs(rest)<.5f,"WateringCan upright measures "+rest.ToString("F2")+" deg (its rose points "+restPitch.ToString("F1")+" deg from the horizon at rest, so the spout's own pitch is not the measure) "+st);
  var root=t.transform;var rp=root.localPosition;var rr=root.localRotation;var mp=model.localPosition;var mr=model.localRotation;
  var heading=Vector3.ProjectOnPlane(noz.forward,Vector3.up).normalized;var axis=Vector3.Cross(Vector3.up,heading);
  try
  {
   // VR: the pickup leaned toward the spout (about the world axis across the spout) by known angles, and 45 deg sideways (about the heading)
   var sb=new StringBuilder();bool ok=true;
   foreach(float a in new[]{stop,start,45f,max,120f})/* 120: past 90 must not fold back (Codex 2026-10-11) */
   {
    root.localRotation=rr;root.rotation=Quaternion.AngleAxis(a,axis)*root.rotation;float m=(float)call("MeasuredTilt",null);ok&=Mathf.Abs(m-a)<.5f;sb.Append(" "+a.ToString("F0")+" -> "+m.ToString("F2")+";");
   }
   root.localRotation=rr;root.rotation=Quaternion.AngleAxis(45f,heading)*root.rotation;float side=(float)call("MeasuredTilt",null);
   root.localRotation=rr;root.localPosition=rp;
   check(ok&&Mathf.Abs(side)<.5f,"WateringCan VR measure = the pickup's lean toward the spout "+st+" (lean deg -> measured:"+sb+" 45 deg sideways -> "+side.ToString("F2")+")");
   // Desktop / Touch: the model leans by the max about its grip
   Vector3 g0=grip.position,n0=noz.position,f0=noz.forward;
   call("ShowModelTilt",new object[]{max});
   Vector3 g1=grip.position,n1=noz.position,f1=noz.forward;bool leaned=!SameExact(model.localRotation,mr);
   float turned=Vector3.Angle(f0,f1),pitch1=Mathf.Asin(Mathf.Clamp(f1.y,-1f,1f))*Mathf.Rad2Deg,gripMoved=(g1-g0).magnitude;
   call("RestModel",null);
   bool back=SameExact(model.localPosition,mp)&&SameExact(model.localRotation,mr);float dq=Mathf.Max(Mathf.Abs(model.localRotation.x-mr.x),Mathf.Abs(model.localRotation.y-mr.y),Mathf.Abs(model.localRotation.z-mr.z),Mathf.Abs(model.localRotation.w-mr.w));
   check(leaned&&Mathf.Abs(turned-max)<.5f&&f1.y<f0.y&&n1.y<n0.y&&gripMoved<.001f,
    "WateringCan Desktop lean "+max+" deg about the grip "+st+": spout turned "+turned.ToString("F2")+" deg, pitch "+restPitch.ToString("F1")+" -> "+pitch1.ToString("F1")+" deg, rose "+((n0.y-n1.y)*100f).ToString("F1")+" cm lower, grip moved "+(gripMoved*1000f).ToString("F2")+" mm");
   check(back,"WateringCan model upright again exactly after RestModel "+st+" (max |dq| "+dq.ToString("G3")+", position "+(SameExact(model.localPosition,mp)?"same":"moved")+")");
   log.AppendLine("can tilt "+st+" spoutInSlot="+root.InverseTransformDirection(noz.forward).ToString("F3")+" gripInSlot="+root.InverseTransformPoint(grip.position).ToString("F3")+" modelLocal="+mp.ToString("F3")+" axisWorld="+axis.ToString("F3"));
  }
  finally{call("RestModel",null);root.localPosition=rp;root.localRotation=rr;if(!SameExact(model.localPosition,mp))model.localPosition=mp;if(!SameExact(model.localRotation,mr))model.localRotation=mr;}
 }
}}
