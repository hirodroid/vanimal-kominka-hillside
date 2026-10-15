using System;
using System.Linq;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
namespace KominkaHillside.Editor {
 public static partial class KH_HalloweenParadeHarnessEditor {
  static Renderer[] plazaRs, otherRs;
  static CanvasGroup[] plazaGs;
  static VRC_Pickup[] plazaPs, otherPs;
  static bool[] plazaBefore, otherBefore, plazaPickupBefore, otherPickupBefore;
  static Behaviour[] plazaRays;static Collider[] plazaCols;
  static UdonBehaviour plazaSelection;static UdonBehaviour[] otherSelections;
  static int[] otherRevisions;static bool[] otherGates;
  static T[] ArrayVar<T>(string name){return Get<Array>(nightPlayback,name).Cast<T>().ToArray();}
  static void SnapshotPlaza(){
   plazaRs=ArrayVar<Renderer>("plazaRenderers");plazaGs=ArrayVar<CanvasGroup>("plazaUiGroups");plazaPs=ArrayVar<VRC_Pickup>("plazaPickups");plazaRays=ArrayVar<Behaviour>("plazaRaycasters");plazaCols=ArrayVar<Collider>("plazaUiColliders");plazaSelection=Get<UdonBehaviour>(nightPlayback,"plazaWashSelection");
   Setup(plazaRs.Length>10&&plazaGs.Length==2,"Plaza wiring missing");plazaBefore=plazaRs.Select(r=>r.forceRenderingOff).ToArray();plazaPickupBefore=plazaPs.Select(p=>p.pickupable).ToArray();
   var other=new[]{"KH_WashStation","KH_WashStations/River","KH_WashStations/Upper"}.Select(n=>GameObject.Find(n)).ToArray();Setup(other.All(x=>x!=null),"Expected other three stations");otherRs=other.SelectMany(t=>t.GetComponentsInChildren<Renderer>(true)).ToArray();otherPs=other.SelectMany(t=>t.GetComponentsInChildren<VRC_Pickup>(true)).ToArray();otherBefore=otherRs.Select(r=>r.forceRenderingOff).ToArray();otherPickupBefore=otherPs.Select(p=>p.pickupable).ToArray();
   otherSelections=other.SelectMany(t=>t.GetComponentsInChildren<UdonBehaviour>(true)).Where(u=>u.programSource!=null&&u.programSource.name=="KH_WashSelection").ToArray();otherRevisions=otherSelections.Select(u=>Get<int>(u,"revision")).ToArray();otherGates=otherSelections.Select(u=>Get<bool>(u,"localPresentationHidden")).ToArray();
   Note("Plaza snapshot: renderers="+plazaRs.Length+" pickups="+plazaPs.Length+" UI="+plazaCols.Length+" other stations="+other.Length);
  }
  static void CheckPlaza(bool hidden){
   Need(Get<bool>(nightPlayback,"_plazaHidden")==hidden,"Plaza hidden state");Need(Get<bool>(plazaSelection,"localPresentationHidden")==hidden,"Selection local gate");
   for(int i=0;i<plazaRs.Length;i++)Need(plazaRs[i].forceRenderingOff==(hidden||plazaBefore[i]),"Plaza renderer "+i);
   Need(plazaGs.All(g=>g.alpha==(hidden?0:1)&&g.interactable==!hidden&&g.blocksRaycasts==!hidden),"Plaza UI visibility/input");Need(plazaRays.All(r=>r.enabled==!hidden)&&plazaCols.All(c=>c.enabled==!hidden),"Plaza UI raycasters/colliders");
   var managed=ArrayVar<bool>("plazaManagedPickups");var slots=Get<Array>(plazaSelection,"_slotItem").Cast<int>().ToArray();var picks=Get<Array>(plazaSelection,"pickups").Cast<VRC_Pickup>().ToArray();
   for(int i=0;i<plazaPs.Length;i++){int k=Array.IndexOf(picks,plazaPs[i]);bool expected=!hidden&&(managed[i]?slots[k]>=0:plazaPickupBefore[i]);Need(plazaPs[i].pickupable==expected,"Current-slot pickup restoration "+i);}
   for(int i=0;i<otherRs.Length;i++)Need(otherRs[i].forceRenderingOff==otherBefore[i],"Other renderer");for(int i=0;i<otherPs.Length;i++)Need(otherPs[i].pickupable==otherPickupBefore[i],"Other pickup");for(int i=0;i<otherSelections.Length;i++)Need(Get<int>(otherSelections[i],"revision")==otherRevisions[i]&&Get<bool>(otherSelections[i],"localPresentationHidden")==otherGates[i],"Other selection/gate");
   Need(GameObject.Find("KH_WashStations/Hillside")!=null,"Wash root disabled");Note("Plaza "+(hidden?"hidden":"restored")+"; current slot pickups consistent; other three stations unchanged. Held tool/busy platform/network selection not exercised.");
  }
 }
}
