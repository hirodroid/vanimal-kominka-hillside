using System;using System.Linq;using System.Text;using System.Reflection;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using UdonSharpEditor;
namespace KominkaHillside.Editor{public static partial class KH_WashSelectionEditor{
 // IMP-DOG-044 (2026-10-11, orchestrator's decision, reversible): the main station's stand sits on a slope (the highest ground under it is
 // 0.185 m above the station origin, the stand at the user's y -0.028: IMP-ENV-007), so the 0.22 m pool was half buried at lift 0. While a
 // platform that would sink into the ground is selected at a station, its stand rests at a minimum height instead (KH_WashStationCall.
 // minLift, not synced: every client takes it from the synced selectedBath through KH_WashSelection.bathMinLifts). 50 measures it per station
 // and platform, 44 measures again and compares (one rule: FitMinLift).
 // Rule: at lift 0 (Lift at the station origin), pen = the highest ground under the platform's footprint - the stand's seat (Lift/WashTable,
 // both station-local y; the shells sink 3.6-3.8 cm below the table top by design, so the seat, not their lowest point, is the reference).
 // pen <= MinLiftTouch (the ground no higher than the seat, as on the course stations) -> 0, else pen + MinLiftMargin,
 // rounded up to the mm. Exact (no 0.1 step): the lift buttons step the synced lift only, the stand stands at max(lift, minLift).
 // The footprint is the platform's mesh box (TightBounds, station space) sampled on a MinLiftGrid grid, edges included - the rectangle around
 // the pool's ellipse is conservative (its corners count). Ground = KH_DogWashSetupEditor.StationGround (the terrain only, as Wash/11 / 12).
 public const float MinLiftMargin=.01f,MinLiftTouch=.001f,MinLiftGrid=.05f;
 static int[] PlatformItems(){return Order.Where(i=>CategoryOf(i)==KH_WashSelection.CategoryBath).ToArray();}
 /// <summary>This station's KH_WashStationCall (st/Call), checked: its liftRoot is st/Lift and the platforms ride it (Lift/WashTable).</summary>
 static KH_WashStationCall StationCallOf(KH_WashSelection sel)
 {
  var st=sel.transform.parent;var t=st.Find("Call");var call=t!=null?t.GetComponent<KH_WashStationCall>():null;
  if(call==null)throw new Exception(st.name+": Call (KH_WashStationCall) missing");
  if(call.liftRoot==null||call.liftRoot!=st.Find("Lift")||st.Find("Lift/WashTable")==null)throw new Exception(st.name+": Call.liftRoot is not "+st.name+"/Lift (with WashTable)");
  return call;
 }
 /// <summary>The stand's minimum height (m) for platform `item` at this station, by the rule above. Throws when the stand is not at rest
 /// (Lift off the station origin: the measure is at lift 0) or the model is missing. Edit mode only.</summary>
 static float FitMinLift(KH_WashSelection sel,int item,out string detail)
 {
  var st=sel.transform.parent;var lift=st.Find("Lift");var model=At(sel.models,item);
  if(lift==null||model==null)throw new Exception(st.name+": "+(lift==null?"Lift":Names[item]+" model")+" missing");
  if(lift.localPosition.sqrMagnitude>1e-8f)throw new Exception(st.name+": Lift is not at rest (local "+lift.localPosition.ToString("F3")+"); the minimum is measured at lift 0");
  var tb=TightBounds(model,st);
  int nx=Mathf.Max(1,Mathf.CeilToInt(tb.size.x/MinLiftGrid)),nz=Mathf.Max(1,Mathf.CeilToInt(tb.size.z/MinLiftGrid));
  float ground=float.NegativeInfinity;Vector3 at=Vector3.zero;
  for(int ix=0;ix<=nx;ix++)for(int iz=0;iz<=nz;iz++)
  {
   var p=new Vector3(tb.min.x+tb.size.x*ix/nx,0f,tb.min.z+tb.size.z*iz/nz);var w=st.TransformPoint(p);
   float gy=KH_DogWashSetupEditor.StationGround(st,w);float local=st.InverseTransformPoint(new Vector3(w.x,gy,w.z)).y;
   if(local>ground){ground=local;at=p;}
  }
  // 2026-10-11 (orchestrator): measured against the stand's seat (the WashTable, station local y at lift 0), not the platform's
  // lowest point. The premium shells are designed to sink 3.6-3.8 cm below the table top (a flat station seats the table on the
  // ground, so its shell bottom is under the ground by design - the hinoki look approved 2026-10-08). Only the slope above the
  // seat (ground higher than where the table sits) buries the platform, so only that is compensated.
  var seat=model.transform.parent;var table=st.Find("Lift/WashTable");
  if(table==null||seat!=table)throw new Exception(st.name+": "+Names[item]+" is not a direct child of Lift/WashTable (parent "+(seat==null?"none":seat.name)+"); the seat is measured from it");
  float seatY=st.InverseTransformPoint(seat.position).y;
  float pen=ground-seatY,value=pen>MinLiftTouch?Mathf.Ceil((pen+MinLiftMargin)*1000f-1e-3f)/1000f:0f;
  detail=st.name+" "+Names[item]+": lowest point y "+tb.min.y.ToString("F4")+", highest ground under its "+tb.size.x.ToString("F2")+" x "+tb.size.z.ToString("F2")+" m footprint y "+ground.ToString("F4")+" at ("+at.x.ToString("F2")+", "+at.z.ToString("F2")+") of "+((nx+1)*(nz+1))+" samples (station local, lift 0) , seat (WashTable) y "+seatY.ToString("F4")+" -> ground above the seat "+pen.ToString("F4")+" m -> minLift "+value.ToString("F3")+" m";
  return value;
 }
 [MenuItem(Menu+"50 - Fit Bath Min Lift")]
 public static void FitBathMinLift()
 {
  Guard();var scene=EditorSceneManager.GetActiveScene();if(scene.isDirty)throw new Exception("Save/reconcile unrelated scene edits first");
  var sels=UnityEngine.Object.FindObjectsOfType<KH_WashSelection>(true).Where(x=>x.gameObject.scene==scene).OrderBy(x=>x.transform.parent.name).ToArray();
  if(sels.Length!=4)throw new Exception("Expected four selectors, found "+sels.Length);
  int n=Names.Length,changed=0;var log=new StringBuilder();
  // measure all four first: nothing is written if any station cannot be measured
  var calls=new KH_WashStationCall[sels.Length];var mins=new float[sels.Length][];
  for(int s=0;s<sels.Length;s++)
  {
   var sel=sels[s];if(sel.models==null||sel.models.Length!=n)throw new Exception(sel.transform.parent.name+": models are not the "+n+" items (run 49 / 48 first)");
   calls[s]=StationCallOf(sel);mins[s]=new float[n];
   foreach(int b in PlatformItems())
   {
    mins[s][b]=FitMinLift(sel,b,out var d);log.AppendLine("  "+d);
    if(mins[s][b]>calls[s].liftMax)throw new Exception(d+" exceeds liftMax "+calls[s].liftMax+" (the stand cannot clear the ground; flatten the ground or drop the platform there)");
   }
  }
  for(int s=0;s<sels.Length;s++)
  {
   var sel=sels[s];var old=sel.bathMinLifts;
   bool same=sel.call==calls[s]&&old!=null&&old.Length==n&&Enumerable.Range(0,n).All(i=>Mathf.Abs(old[i]-mins[s][i])<1e-6f);
   log.AppendLine(sel.transform.parent.name+" (station "+calls[s].stationIndex+"): bathMinLifts "+(same?"unchanged":"["+(old==null?"null":string.Join(",",old.Select(x=>x.ToString("F3"))))+"] ->")+" ["+string.Join(",",mins[s].Select(x=>x.ToString("F3")))+"], call "+(sel.call==calls[s]?"wired":"-> "+PathOf(calls[s].transform)));
   if(!same)changed++;
   sel.call=calls[s];sel.bathMinLifts=mins[s];
   Copy(sel);   // proxy -> Udon every run (idempotent; vrc-udonsharp §4)
  }
  EditorSceneManager.MarkSceneDirty(scene);
  Log("fit-min-lift","selectors="+sels.Length+" changed="+changed+" (0 on a re-run) margin="+MinLiftMargin+" touch="+MinLiftTouch+" grid="+MinLiftGrid+" platforms="+string.Join(",",PlatformItems().Select(i=>Names[i]))+"\n"+log+"NOT saved: run 44 - Verify Selection, then save (Wash/12 refuses an unsaved scene), then Wash/12, then 44 again");
 }
 // ---- 44: the stand's minimum height per station and platform (IMP-DOG-044) ----
 /// <summary>Per station: bathMinLifts = a fresh FitMinLift (1 mm), 0 for every other item, within liftMax; call = this station's Call; both
 /// in the backing heap (written by 50's CopyProxyToUdon).</summary>
 static void VerifyMinLift(KH_WashSelection sel,Action<bool,string> check,StringBuilder log)
 {
  string st=sel.transform.parent.name;int n=Names.Length;KH_WashStationCall call=null;
  try{call=StationCallOf(sel);}catch(Exception e){check(false,"min lift: "+e.Message);return;}
  check(sel.call==call,"min lift: selector.call = "+st+"/Call (actual "+(sel.call==null?"null":PathOf(sel.call.transform))+"; run 50)");
  var m=sel.bathMinLifts;
  check(m!=null&&m.Length==n,"min lift: bathMinLifts.Length == "+n+" "+st+" (actual "+(m==null?"null; run 50":m.Length.ToString())+")");
  if(m==null||m.Length!=n)return;
  log.AppendLine("min lift "+st+" (station "+call.stationIndex+"): bathMinLifts ["+string.Join(",",m.Select(x=>x.ToString("F3")))+"]");
  var plats=PlatformItems();
  check(Enumerable.Range(0,n).All(i=>plats.Contains(i)||m[i]==0f),"min lift: 0 for every item that is not an offered platform "+st);
  foreach(int b in plats)
  {
   float fresh;string d;
   try{fresh=FitMinLift(sel,b,out d);}catch(Exception e){check(false,"min lift "+Names[b]+" "+st+": "+e.Message);continue;}
   log.AppendLine("  "+d);
   check(Mathf.Abs(m[b]-fresh)<.001f,"min lift "+Names[b]+" "+st+" "+m[b].ToString("F3")+" m = fresh measure "+fresh.ToString("F3")+" m (1 mm; "+(fresh>0f?"raised clear of the slope":"resting on the ground: 0")+")");
   check(m[b]>=0f&&m[b]<=call.liftMax,"min lift "+Names[b]+" "+st+" within 0.."+call.liftMax+" m");
  }
  var back=UdonSharpEditorUtility.GetBackingUdonBehaviour(sel);object v;
  check(back.publicVariables.TryGetVariableValue("bathMinLifts",out v)&&v is float[] bm&&bm.SequenceEqual(m),"saved backing bathMinLifts "+st);
  var cb=UdonSharpEditorUtility.GetBackingUdonBehaviour(call);
  check(back.publicVariables.TryGetVariableValue("call",out v)&&v is UnityEngine.Object o&&o!=null&&(o==call||o==cb),"saved backing call = "+st+"/Call ("+(v==null?"missing":v.GetType().Name)+")");
 }
 /// <summary>44, Edit mode, the main station (stationIndex 0): the shared runtime bodies KH_WashSelection._Apply (pushes call.minLift from
 /// selectedBath) and KH_WashStationCall._LiftRest / _LiftTarget (the height the stand eases to). No dog: rests at minLift; a held dog with
 /// the synced lift below it (0, or "台を下げる" at 0): minLift; above it: the synced lift. Restores everything.</summary>
 static void VerifyMinLiftLogic(KH_WashSelection[] sels,Action<bool,string> check,StringBuilder log)
 {
  var sel=sels.FirstOrDefault(x=>x.call!=null&&x.call.stationIndex==0);
  if(sel==null){check(false,"min lift logic: no selector wired to station 0 (run 50)");return;}
  var call=sel.call;string st=sel.transform.parent.name;const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
  var fHeld=typeof(KH_WashStationCall).GetField("heldDog",f);var fLift=typeof(KH_WashStationCall).GetField("lift",f);
  if(fHeld==null||fLift==null){check(false,"min lift logic: KH_WashStationCall heldDog / lift fields");return;}
  int f0=sel.selectedFoam,w0=sel.selectedWater,n0=sel.selectedFinish,b0=sel.selectedBath,r0=sel.revision;int h0=(int)fHeld.GetValue(call);float l0=(float)fLift.GetValue(call);
  try
  {
   foreach(int b in PlatformItems().Concat(new[]{-1}))
   {
    sel.selectedBath=b;sel.revision++;ResetApplyState(sel);sel._Apply(false);
    float want=b>=0&&sel.bathMinLifts!=null&&b<sel.bathMinLifts.Length?sel.bathMinLifts[b]:0f;string name=b>=0?Names[b]:"free table";
    check(Mathf.Abs(call.minLift-want)<1e-6f&&Mathf.Abs(call._LiftRest()-want)<1e-6f,"min lift logic "+st+": "+name+" selected -> call.minLift "+call.minLift.ToString("F3")+" = "+want.ToString("F3"));
    fHeld.SetValue(call,-1);fLift.SetValue(call,0f);
    float noDog=call._LiftTarget();
    fHeld.SetValue(call,0);
    float atZero=call._LiftTarget();
    fLift.SetValue(call,.1f);float low=call._LiftTarget();
    fLift.SetValue(call,call.liftMax);float top=call._LiftTarget();
    check(Mathf.Abs(noDog-want)<1e-6f&&Mathf.Abs(atZero-want)<1e-6f&&Mathf.Abs(low-Mathf.Max(.1f,want))<1e-6f&&Mathf.Abs(top-call.liftMax)<1e-6f,
     "min lift logic "+st+": "+name+" stand target no dog "+noDog.ToString("F3")+" / held lift 0 "+atZero.ToString("F3")+" / lift 0.1 "+low.ToString("F3")+" / lift "+call.liftMax+" "+top.ToString("F3")+" (= max(lift, "+want.ToString("F3")+"))");
    fHeld.SetValue(call,h0);fLift.SetValue(call,l0);
   }
  }
  finally
  {
   fHeld.SetValue(call,h0);fLift.SetValue(call,l0);
   sel.selectedFoam=f0;sel.selectedWater=w0;sel.selectedFinish=n0;sel.selectedBath=b0;sel.revision=r0;ResetApplyState(sel);sel._Apply(false);RestHolds(sel);
  }
  log.AppendLine("min lift logic "+st+" (Edit mode, proxy, _Apply / _LiftTarget shared with runtime; the per-frame inline copy in KH_WashStationCall.Update and the dog's ride down to _LiftRest: Play)");
 }
}}
