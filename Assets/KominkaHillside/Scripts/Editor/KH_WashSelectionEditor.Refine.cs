using System;using System.Linq;using System.Text;using UnityEngine;using UnityEngine.UI;using UnityEditor;using UnityEditor.SceneManagement;using TMPro;using UdonSharpEditor;using VRC.SDKBase;
namespace KominkaHillside.Editor{public static partial class KH_WashSelectionEditor{
 [MenuItem(Menu+"46 - Refine Panel Layout")]
 public static void Refine(){Guard();foreach(var f in UnityEngine.Object.FindObjectsOfType<KH_WashFinishFx>(true)){UdonSharpEditorUtility.GetBackingUdonBehaviour(f).SyncMethod=Networking.SyncType.Manual;Copy(f);foreach(var a in f.GetComponentsInChildren<AudioSource>(true))KH_AudioSpatialEditor.Configure(a);}foreach(var sel in UnityEngine.Object.FindObjectsOfType<KH_WashSelection>(true)){var st=sel.transform.parent;for(int k=0;k<sel.slots.Length;k++)sel.slots[k].name="Premium_"+st.name+"_Slot"+k;var b=st.GetComponentInChildren<KH_WashSelectionBoard>(true);foreach(var a in sel.GetComponentsInChildren<AudioSource>(true).Concat(b.GetComponentsInChildren<AudioSource>(true)))KH_AudioSpatialEditor.Configure(a);b.hero.rectTransform.anchoredPosition=new Vector2(-92,112);b.hero.rectTransform.sizeDelta=new Vector2(180,180);b.setTitle.rectTransform.anchoredPosition=new Vector2(218,181);b.setTitle.rectTransform.sizeDelta=new Vector2(374,46);b.description.rectTransform.anchoredPosition=new Vector2(218,95);b.description.rectTransform.sizeDelta=new Vector2(374,136);for(int i=0;i<6;i++){var rt=b.itemRows[i].GetComponent<RectTransform>();rt.anchoredPosition=new Vector2(-95+(i%3)*208,-55-(i/3)*116);rt.sizeDelta=new Vector2(198,108);}b.status.rectTransform.anchoredPosition=new Vector2(0,-246);b.status.rectTransform.sizeDelta=new Vector2(824,30);b.status.fontSizeMax=17;b.status.fontSizeMin=13.6f;b.actionButton.GetComponent<RectTransform>().anchoredPosition=new Vector2(224,-296);b.resetLabel.transform.parent.GetComponent<RectTransform>().anchoredPosition=new Vector2(-292,-296);b._Tab0();Copy(b);foreach(var t in sel.slots){UdonSharpEditorUtility.GetBackingUdonBehaviour(t).SyncMethod=Networking.SyncType.Continuous;Copy(t);Copy(t.state);}Copy(sel);if(st.Find("SelectionPosts")==null){var posts=Child(st,"SelectionPosts",Vector3.zero);var mat=AssetDatabase.LoadAssetAtPath<Material>("Assets/KominkaHillside/Data/Environment/River/KH_RiverWood.mat");for(int i=0;i<2;i++){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name="Post"+i;g.transform.SetParent(posts,false);g.transform.localPosition=new Vector3(.95f+(i==0?-.36f:.36f),.45f,.27f);g.transform.localScale=new Vector3(.045f,.95f,.045f);g.GetComponent<Renderer>().sharedMaterial=mat;UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());}}}EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveOpenScenes();Log("refine","layout spacing / status row / wooden posts / unique slot names saved");}

 // BUG-DOG-010 (2026-10-11): FoamCannon hold. The FBX GripAnchor (+Z = spray, +Y = gun top; build_premium.py common()) was
 // used as ExactGrip with orientation Grip. The official VRC Pickup page (creators.vrchat.com/worlds/components/vrc_pickup,
 // read 2026-10-11) says Grip turns +X of Exact Grip forward and +Z up, then pitches 30 deg forward (VR), and +Z forward /
 // +X down (Desktop): the spray axis went up/sideways in VR and the gun lay on its side on Desktop. Gun turns +X of Exact
 // Gun to where the player aims and +Z up (the doc gives no VR/non-VR split for Gun; the pickup Version 1.0/1.1 split is about
 // Auto Hold only), which is the "nozzle along elbow -> hand" hold.
 // So slot 0 (FoamCannon only, Slots[0]) becomes Gun with its own hold point: X = spray (model +Z), Z = gun top (model +Y),
 // Y = model +X. Slots 1-6 stay Grip. Left/right hand behaviour is not stated by the doc (PCVR check pending).
 // Owned by this code (Setup writes it every time): adjust the two constants below, then 47 and 44.
 public const string FoamHoldName="KH_FoamCannonGunHold";
 // Handle centre: models_v02.py grip('Cannon handle') loft centre (0,.035,-.012) Blender -> model space (-x, z, -y) =
 // (0,-.012,-.035); the handle spans model Y -.072..+.048 and Z -.059..-.012 (front face), the trigger is above at Y +.033.
 public static readonly Vector3 FoamHoldLocalPosition=new Vector3(0f,-.012f,-.035f);
 public static Quaternion FoamHoldLocalRotation{get{return Quaternion.LookRotation(Vector3.up,Vector3.right);}}
 static Transform FoamCannonHold(KH_WashSelection sel){return CodeGunHold(sel,0,FoamHoldName,FoamHoldLocalPosition,FoamHoldLocalRotation);}
 // BUG-DOG-015 (2026-10-15): the dryer, the same cause as the foam cannon. FBX measured with the lab's check_fbx_axes.cjs (Unity axes):
 // Dryer_GripAnchor at the model origin with identity rotation (+Z = airflow, +Y = up, +X = right), Dryer_NozzleAnchor (0,.082,.122) with
 // +Z = airflow; the handle (models_v02.py grip('Dryer ivory handle',(0,.025,-.013),0) loft) spans model x -.02..+.02, y -.07..+.045,
 // z -.049..-.002 (below the housing, slightly behind), centre (0,-.013,-.025). As Grip (VR: Exact Grip +X forward, +Z up, pitched 30 deg)
 // the airflow pointed up and the hand sat at the handle's front face (the origin). Slot 4 (Slots[7]) carries the dryer only, so it is
 // Gun at rest like slot 0. Owned by this code: adjust the two constants below after a PCVR check, then 48 (or 47) and 44.
 public const int DryerId=7;   // as WateringCanId / WaterPistolId / HoseId (KH_WashSelectionEditor.cs)
 public const string DryerHoldName="KH_DryerGunHold";
 public static readonly Vector3 DryerHoldLocalPosition=new Vector3(0f,-.013f,-.025f);
 // the same frame as KH_FoamCannonGunHold: +X = airflow (model +Z), +Z = up (model +Y), +Y = model +X
 public static Quaternion DryerHoldLocalRotation{get{return Quaternion.LookRotation(Vector3.up,Vector3.right);}}
 static Transform DryerHold(KH_WashSelection sel){return CodeGunHold(sel,DryerId,DryerHoldName,DryerHoldLocalPosition,DryerHoldLocalRotation);}
 /// <summary>Items whose Exact Gun is a child made by this code (not an FBX *_GunHold) and whose slot carries only them (rests as Gun).</summary>
 static bool CodeGunItem(int id){return id==0||id==DryerId;}
 static bool DedicatedGunSlot(int s){return s==Slots[0]||s==Slots[DryerId];}
 /// <summary>Creates / updates the child holdName directly under item id's model (local position / rotation, scale 1) and writes that item's
 /// slot pickup to orientation Gun with Exact Gun = the child, and gunHolds[id] = the child. Idempotent. Only for CodeGunItem items.</summary>
 static Transform CodeGunHold(KH_WashSelection sel,int id,string holdName,Vector3 localPosition,Quaternion localRotation)
 {
  var m=At(sel.models,id);if(m==null||m.name!=Names[id])throw new Exception(sel.transform.parent.name+": models["+id+"] is "+(m==null?"null":m.name)+", expected "+Names[id]);var model=m.transform;
  var hold=model.Find(holdName);
  if(hold==null){var g=new GameObject(holdName);Undo.RegisterCreatedObjectUndo(g,Names[id]+" gun hold");hold=g.transform;hold.SetParent(model,false);}
  Undo.RecordObject(hold,Names[id]+" gun hold");hold.localPosition=localPosition;hold.localRotation=localRotation;hold.localScale=Vector3.one;
  var pu=sel.pickups[Slots[id]];Undo.RecordObject(pu,Names[id]+" gun hold");pu.orientation=VRC_Pickup.PickupOrientation.Gun;pu.ExactGun=hold;
  // IMP-DOG-046 (2026-10-11): the hold is per item at runtime (KH_WashSelection.gunHolds; _Apply sets Gun + Exact Gun for it, Grip otherwise).
  if(sel.gunHolds!=null&&sel.gunHolds.Length>id){sel.gunHolds[id]=hold;EditorUtility.SetDirty(sel);}
  EditorUtility.SetDirty(hold);EditorUtility.SetDirty(pu);return hold;
 }
 // IMP-DOG-049 (2026-10-11): every Gun item. FoamCannon keeps its own child (above, slot 0 only carries it, so the pickup is written here
 // too; BUG-DOG-015 2026-10-15: the Dryer the same on slot 4); WaterPistol and Hose share the water slot with the watering can (Grip), so only gunHolds[item] is written and _Apply sets
 // Gun + Exact Gun when it configures the slot for them. Their Exact Gun is the FBX child *_GunHold as it is (lab handoff 0.4.0: +X =
 // spray, +Z = up, the same frame as KH_FoamCannonGunHold; WashPremium/12 measured it within 1 deg in the prefab, 44 per station).
 // If a PCVR check asks for a different hand pose, add a child under the model as FoamCannonHold does and point gunHolds at it.
 public static readonly int[] GunItems={0,WaterPistolId,HoseId,DryerId};
 static string GunHoldName(int id){return id==0?FoamHoldName:id==DryerId?DryerHoldName:Names[id]+"_GunHold";}
 static Transform FbxGunHold(KH_WashSelection sel,int id){var m=At(sel.models,id);return m==null?null:m.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==GunHoldName(id));}
 static void GunHolds(KH_WashSelection sel,StringBuilder log)
 {
  FoamCannonHold(sel);DryerHold(sel);   // code-made children (the Dryer FBX has no Dryer_GunHold)
  foreach(int id in GunItems)
  {
   if(CodeGunItem(id))continue;
   if(At(sel.models,id)==null){if(log!=null)log.AppendLine("  gun hold "+Names[id]+" "+sel.transform.parent.name+": no model yet (run 49)");continue;}
   var h=FbxGunHold(sel,id);if(h==null)throw new Exception(sel.transform.parent.name+": "+Names[id]+" has no "+GunHoldName(id)+" (re-import with 12)");
   if(sel.gunHolds==null||sel.gunHolds.Length<=id)throw new Exception(sel.transform.parent.name+": gunHolds shorter than item "+id+" (run 49)");
   sel.gunHolds[id]=h;
  }
  EditorUtility.SetDirty(sel);
 }
 [MenuItem(Menu+"47 - Apply Gun Holds")]
 public static void ApplyFoamCannonHold()
 {
  Guard();var scene=EditorSceneManager.GetActiveScene();if(scene.isDirty)throw new Exception("Save/reconcile unrelated scene edits first");
  var sels=UnityEngine.Object.FindObjectsOfType<KH_WashSelection>(true).Where(x=>x.gameObject.scene==scene).OrderBy(x=>x.transform.parent.name).ToArray();
  if(sels.Length!=4)throw new Exception("Expected four selectors, found "+sels.Length);
  var log=new StringBuilder();int created=0;
  foreach(var sel in sels)
  {
   var pu=sel.pickups[Slots[0]];bool existed=sel.models[0].transform.Find(FoamHoldName)!=null;if(!existed)created++;bool dryerExisted=sel.models[DryerId].transform.Find(DryerHoldName)!=null;
   string before="orientation="+pu.orientation+" exactGun="+(pu.ExactGun==null?"null":pu.ExactGun.name)+" exactGrip="+(pu.ExactGrip==null?"null":pu.ExactGrip.name);
   GunHolds(sel,log);var h=sel.models[0].transform.Find(FoamHoldName);if(sel.gunHolds!=null)Copy(sel);
   log.AppendLine(sel.transform.parent.name+" "+pu.name+" before "+before+" after orientation="+pu.orientation+" exactGun="+h.name+" local="+h.localPosition.ToString("F3")+" rot="+h.localEulerAngles.ToString("F1")+(existed?" (updated)":" (created)"));
   var dp=sel.pickups[Slots[DryerId]];var dh=sel.models[DryerId].transform.Find(DryerHoldName);log.AppendLine("  "+dp.name+" Dryer orientation="+dp.orientation+" exactGun="+(dp.ExactGun==null?"null":dp.ExactGun.name)+" local="+dh.localPosition.ToString("F3")+" rot="+dh.localEulerAngles.ToString("F1")+(dryerExisted?" (updated)":" (created)"));
   foreach(int id in GunItems)if(id!=0)log.AppendLine("  gunHolds["+id+"] "+Names[id]+" = "+(sel.gunHolds==null||sel.gunHolds.Length<=id||sel.gunHolds[id]==null?"null":sel.gunHolds[id].name));
  }
  EditorSceneManager.MarkSceneDirty(scene);
  Log("foam-hold","selectors="+sels.Length+" foam hold created="+created+" updated="+(sels.Length-created)+" (dryer hold per line)\n"+log+"NOT saved: run 44 - Verify Selection, then save the scene (KH_WashSelection.gunHolds copied to Udon when the category arrays exist). 48 / 49 run the same holds");
 }
}}
