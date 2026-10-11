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
 static Transform FoamCannonHold(KH_WashSelection sel)
 {
  var model=sel.models[0].transform;if(model.name!=Names[0])throw new Exception("models[0] is "+model.name+", expected "+Names[0]);
  var hold=model.Find(FoamHoldName);
  if(hold==null){var g=new GameObject(FoamHoldName);Undo.RegisterCreatedObjectUndo(g,"Foam cannon hold");hold=g.transform;hold.SetParent(model,false);}
  Undo.RecordObject(hold,"Foam cannon hold");hold.localPosition=FoamHoldLocalPosition;hold.localRotation=FoamHoldLocalRotation;hold.localScale=Vector3.one;
  var pu=sel.pickups[Slots[0]];Undo.RecordObject(pu,"Foam cannon hold");pu.orientation=VRC_Pickup.PickupOrientation.Gun;pu.ExactGun=hold;
  // IMP-DOG-046 (2026-10-11): the hold is per item at runtime (KH_WashSelection.gunHolds; _Apply sets Gun + Exact Gun for it, Grip otherwise).
  if(sel.gunHolds!=null&&sel.gunHolds.Length>0){sel.gunHolds[0]=hold;EditorUtility.SetDirty(sel);}
  EditorUtility.SetDirty(hold);EditorUtility.SetDirty(pu);return hold;
 }
 [MenuItem(Menu+"47 - Apply Foam Cannon Hold")]
 public static void ApplyFoamCannonHold()
 {
  Guard();var scene=EditorSceneManager.GetActiveScene();if(scene.isDirty)throw new Exception("Save/reconcile unrelated scene edits first");
  var sels=UnityEngine.Object.FindObjectsOfType<KH_WashSelection>(true).Where(x=>x.gameObject.scene==scene).OrderBy(x=>x.transform.parent.name).ToArray();
  if(sels.Length!=4)throw new Exception("Expected four selectors, found "+sels.Length);
  var log=new StringBuilder();int created=0;
  foreach(var sel in sels)
  {
   var pu=sel.pickups[Slots[0]];bool existed=sel.models[0].transform.Find(FoamHoldName)!=null;if(!existed)created++;
   string before="orientation="+pu.orientation+" exactGun="+(pu.ExactGun==null?"null":pu.ExactGun.name)+" exactGrip="+(pu.ExactGrip==null?"null":pu.ExactGrip.name);
   var h=FoamCannonHold(sel);if(sel.gunHolds!=null)Copy(sel);
   log.AppendLine(sel.transform.parent.name+" "+pu.name+" before "+before+" after orientation="+pu.orientation+" exactGun="+h.name+" local="+h.localPosition.ToString("F3")+" rot="+h.localEulerAngles.ToString("F1")+(existed?" (updated)":" (created)"));
  }
  EditorSceneManager.MarkSceneDirty(scene);
  Log("foam-hold","selectors="+sels.Length+" created="+created+" updated="+(sels.Length-created)+"\n"+log+"NOT saved: run 44 - Verify Selection, then save the scene (KH_WashSelection.gunHolds[0] copied to Udon when the category arrays exist)");
 }
}}
