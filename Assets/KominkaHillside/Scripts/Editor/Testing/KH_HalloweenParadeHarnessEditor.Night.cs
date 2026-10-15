using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase;
using VRC.Udon;
namespace KominkaHillside.Editor {
 public static partial class KH_HalloweenParadeHarnessEditor {
  static UdonBehaviour nightPlayback,nightController;
  static bool nightPlaybackEnabled;
  static float nightManual;
  static bool nightAuto;
  [MenuItem("Tools/KominkaHillside/Parade/49 - Night Lifecycle Harness")]
  public static void RunNight(){SessionState.SetBool(Key+".night",true);try{Run();}finally{SessionState.SetBool(Key+".night",false);}}
  static void CleanupNight(){if(nightPlayback!=null)nightPlayback.enabled=nightPlaybackEnabled;nightPlayback=null;nightController=null;}
  static void NightDiagnostic(string label){var type=typeof(UdonBehaviour);var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;var fields=new[]{"_isReady","_hasDoneStart","_hasError","_eventTable"};var info=string.Join(" ",fields.Select(n=>n+"="+type.GetField(n,flags)?.GetValue(nightPlayback)));var program=nightPlayback.programSource.SerializedProgramAsset.RetrieveProgram();Note(label+" enabled="+nightPlayback.enabled+" playing="+Get<bool>(nightPlayback,"_playing")+" stage="+Get<GameObject>(nightPlayback,"stageRoot").activeSelf+" dark="+Get<float>(nightController,"paradeDarkness")+" nightID="+Get<UdonBehaviour>(nightPlayback,"nightMode").GetInstanceID()+" expected="+nightController.GetInstanceID()+" export="+program.EntryPoints.GetExportedSymbols().Contains("_onDisable")+" disabledEvents="+nightPlayback.DisableEventProcessing+" "+info);}
  static void AdvanceNight(){
   if(stage==0){if(Time.time-playAt>20)throw new InvalidOperationException("Night VM startup timeout");if(Time.time-playAt<3||!Utilities.IsValid(Networking.LocalPlayer))return;
    Resolve();nightPlayback=UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true).Single(u=>u.programSource!=null&&u.programSource.name=="KH_HalloweenParadePlayback");nightController=Get<UdonBehaviour>(nightPlayback,"nightMode");nightPlaybackEnabled=nightPlayback.enabled;nightManual=Get<float>(nightController,"onDarkness");nightAuto=Get<bool>(nightController,"_auto");Setup(nightPlaybackEnabled,"Playback initially disabled");SnapshotPlaza();BeginCase(false);ToStage(1);return;}
   ObservePhase();if(Time.time-caseAt>40)throw new Exception("Night lifecycle exceeded 40s");
   if(stage==1){Need(Phase()!=0&&Phase()!=7,"Night show failed before Playing");if(Phase()!=5||Vm<float>(manager,"get_Elapsed")<4.4f)return;Need(Mathf.Abs(Get<float>(nightController,"paradeDarkness")-.72f)<.01f,"Playing darkness missing");Need(Get<float>(nightController,"_applied")>=.71f,"Night controller did not apply max input");Note("Night Playing: parade="+Get<float>(nightController,"paradeDarkness")+" applied="+Get<float>(nightController,"_applied"));CheckPlaza(true);NightDiagnostic("Before disable");nightPlayback.enabled=false;NightDiagnostic("Immediate disable");ToStage(2);return;}
   if(stage==2){if(Time.time-stageAt<.3f)return;CheckPlaza(false);NightDiagnostic("After disable wait");Need(Get<float>(nightController,"paradeDarkness")==0,"Disable did not release parade darkness");Need(Mathf.Abs(Get<float>(nightController,"_applied")-Vm<float>(nightController,"get_Darkness"))<.03f,"Disable did not return to current base/live");Note("Disabled: parade=0 applied="+Get<float>(nightController,"_applied"));nightPlayback.enabled=true;ToStage(3);return;}
   if(stage==3){if(Time.time-stageAt<.5f)return;Need(Mathf.Abs(Get<float>(nightController,"paradeDarkness")-.72f)<.01f,"Reenable did not resume at current elapsed");Need(Get<float>(nightController,"onDarkness")==nightManual&&Get<bool>(nightController,"_auto")==nightAuto,"Night user setting changed");CheckPlaza(true);Note("Reenabled at elapsed="+Vm<float>(manager,"get_Elapsed"));manager.SendCustomEvent("_Cancel");ToStage(4);return;}
   if(stage==4){if(Phase()!=0){Need(Phase()==6,"Unexpected cancel recovery");return;}if(Time.time-stageAt<.5f)return;Need(Get<float>(nightController,"paradeDarkness")==0,"Cancel did not release darkness");Need(Mathf.Abs(Get<float>(nightController,"_applied")-Vm<float>(nightController,"get_Darkness"))<.03f,"Cancel did not return to current base/live");Need(Get<int>(manager,"restoredMask")==63,"Dogs not restored");for(int i=0;i<6;i++)Need(!Vm<bool>(dogs[i],"get_BlocksNewUses")&&!Get<bool>(pets[i],"_suppressed"),"Dog gate not restored");Need(Get<float>(nightController,"onDarkness")==nightManual&&Get<bool>(nightController,"_auto")==nightAuto,"Cancel changed night preferences");Note("Cancel: parade=0 applied="+Get<float>(nightController,"_applied")+" currentMax="+Vm<float>(nightController,"get_Darkness"));CheckPlaza(false);report.normalPassed=true;report.nightPassed=true;End("PASS","Bounded night Playing/Disable/Reenable/Cancel VM lifecycle");}
  }
 }
}