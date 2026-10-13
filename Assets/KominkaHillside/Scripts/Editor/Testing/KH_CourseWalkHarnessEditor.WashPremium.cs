using System;using System.Linq;using UnityEditor;using UnityEngine;using UnityEngine.UI;using VRC.SDKBase;using VRC.Udon;
using KDDefs=KominkaDogRun.Editor.KD_ProductDefinitions;using CsStore=VRC.SDK3.ClientSim.ClientSimStoreManager;
namespace KominkaHillside.Editor {public static partial class KH_CourseWalkHarnessEditor{
 // IMP-DOG-046 / 047 (2026-10-11): one selection per category (selectedFoam / selectedWater / selectedFinish) + the platform (selectedBath),
 // ReqSelect(item, revision) answered with KH_WashSelection.Result* (0 applied / 1 that category held / 2 stale / 3 reset kept a held tool).
 // Item ids keep the 17-item numbering; the offered six are KH_WashSelectionEditor.Order (PremiumBind checks these names against it and
 // against the scene's itemCategories). Board tab = category (KH_WashSelection.CategoryBath = the station tab), row = place in Order.
 // IMP-DOG-049 / 044 (2026-10-11): + 17 water pistol, 18 hose (water slot 2 with the can), 19 pool (platform). The cases below still use the
 // six ids; the hold-to-spray / one-shot inputs are P-pistol-shot / P-hose-hold in WashPremiumWater.cs (group premium-water, 100p4).
 const int PFoamCannon=0,PPawMitt=9,PWateringCan=6,PDryer=7,PTowel=8,PHinoki=13,PWaterPistol=17,PHose=18,PPool=19;
 static UdonBehaviour premiumSelection,premiumWater;static Quaternion premiumCanRest;static Transform premiumBoard;static UdonBehaviour[] premiumTools;static int premiumSerial;static bool premiumReceived;
 // IMP-DOG-056 (2026-10-14): the purchase gate is on the Owner (KH_WashSelection._Decide: Locked / Pending). Every case that expects a selection
 // to apply (premium-input / -other / -free / -water, W-height-minlift, water-play: all through PremiumBind) first owns the four sets through
 // ClientSim's own purchase path (ClientSimStoreManager.PurchaseConfirmed = OnPurchaseConfirmed to every behaviour, as a purchase in Play),
 // so their expectations stay as they were. Only the premium-store cases (PremiumBindStore) look at the gate itself.
 static void PremiumBind(){PremiumBindStore();PremiumOwnAll();}
 static void PremiumBindStore(){var st=GameObject.Find("KH_WashStation").transform;premiumSelection=st.Find("PremiumTools").GetComponent<UdonBehaviour>();premiumBoard=st.Find("PremiumSelectionBoard");premiumTools=st.Find("PremiumTools").GetComponentsInChildren<UdonBehaviour>(true).Where(u=>u.programSource!=null&&u.programSource.name=="KH_WashTool").ToArray();hwSyms.Clear();hwParams.Clear();
  WsExpect(KH_WashSelectionEditor.Order.SequenceEqual(new[]{PFoamCannon,PPawMitt,PWateringCan,PWaterPistol,PHose,PDryer,PTowel,PHinoki,PPool}),"harness item ids = catalog Order ["+string.Join(",",KH_WashSelectionEditor.Order)+"]");
  var cats=Get<int[]>(premiumSelection,"itemCategories");WsExpect(cats.Length>PPool&&cats[PFoamCannon]==0&&cats[PPawMitt]==0&&cats[PWateringCan]==1&&cats[PDryer]==2&&cats[PTowel]==2&&cats[PHinoki]==KH_WashSelection.CategoryBath&&cats[PWaterPistol]==1&&cats[PHose]==1&&cats[PPool]==KH_WashSelection.CategoryBath,"scene itemCategories match the catalog (WashPremium/49 applied): ["+string.Join(",",cats)+"]");
  premiumWater=PremiumSlotTool(PWateringCan);premiumReceived=Get<bool>(premiumSelection,"_receivedState");Networking.LocalPlayer.TeleportTo(st.TransformPoint(new Vector3(.8f,.1f,-.8f)),st.rotation);PremiumClear();PremiumWired();}
 // The scene must carry WashPremium/54's wiring: unwired, the gate counts every set as None and every case below would read Locked.
 static void PremiumWired(){var arr=WsRef(premiumSelection,"setItems") as Array;WsExpect(arr!=null&&arr.Length==4&&Enumerable.Range(0,4).All(i=>arr.GetValue(i)!=null),"KH_WashSelection.setItems wired to the four sets (WashPremium/54)");}
 static VRC.Economy.UdonProduct PremiumProduct(int c){return KDDefs.LoadCeProduct(KDDefs.Get(KDDefs.WashSetKeys[c]));}
 static bool PremiumOwned(int c){var p=PremiumProduct(c);var o=CsStore.OwnedProducts;return p!=null&&o!=null&&o.ContainsKey(p.ID);}
 /// <summary>This client owns set c from now (once: ClientSimStoreManager.Add throws on a second purchase of the same product).</summary>
 static void PremiumGrant(int c,bool purchasedNow){var p=PremiumProduct(c);WsExpect(p!=null,"UdonProduct of "+KDDefs.WashSetKeys[c]+" (Products/1)");if(!PremiumOwned(c))CsStore.PurchaseConfirmed(p,purchasedNow);WsExpect(PremiumOwned(c),"ClientSim owns "+p.name);}
 static void PremiumOwnAll(){for(int c=0;c<4;c++)PremiumGrant(c,false);}
 static int PremiumItemOf(int c){return c==0?PFoamCannon:c==1?PWateringCan:c==2?PDryer:PHinoki;}
 static string PremiumSelKey(int c){return c==0?"selectedFoam":c==1?"selectedWater":c==2?"selectedFinish":"selectedBath";}
 static bool PremiumBuyShown(){var t=premiumBoard.Find("Buy");return t!=null&&t.gameObject.activeSelf;}
 static string PremiumText(string child){var t=premiumBoard.Find(child);var tm=t!=null?t.GetComponent<TMPro.TMP_Text>():null;return tm!=null?tm.text:"(no "+child+")";}
 /// <summary>The board's word i in its current language (as KH_WashSelectionBoard.W).</summary>
 static string PremiumWord(int i){var b=premiumBoard.GetComponent<UdonBehaviour>();var w=Get<string[]>(b,"words");int k=Get<int>(b,"_lang")*Get<int>(b,"wordCount")+i;return w!=null&&k>=0&&k<w.Length?w[k]:"";}
 // Deterministic start: nothing held (injected), all three categories and the platform back to free, applied now (case order must not decide outcomes).
 static void PremiumClear(){foreach(var u in premiumTools)u.SetProgramVariable("debugHeldOverride",0);foreach(var k in new[]{"selectedFoam","selectedWater","selectedFinish","selectedBath"})premiumSelection.SetProgramVariable(k,-1);premiumSelection.SetProgramVariable("revision",Get<int>(premiumSelection,"revision")+1);HwInvoke(premiumSelection,"_Apply","animate",false);}
 /// <summary>The KH_WashTool of an item's pickup slot (itemSlots), by the selector's slots array, else by the slot root's name.</summary>
 static UdonBehaviour PremiumSlotTool(int item){int s=Get<int[]>(premiumSelection,"itemSlots")[item];var arr=WsRef(premiumSelection,"slots") as Array;var u=arr!=null&&s>=0&&s<arr.Length?arr.GetValue(s) as UdonBehaviour:null;if(u==null)u=premiumTools.FirstOrDefault(t=>t.name.EndsWith("_Slot"+s));WsExpect(u!=null,"pickup slot "+s+" of item "+item);return u;}
 static GameObject PremiumModel(int item){return Get<GameObject[]>(premiumSelection,"models")[item];}
 static bool PremiumPickupable(int item){return PremiumSlotTool(item).GetComponent<VRC_Pickup>().pickupable;}
 static int[] PremiumShown(){return Get<int[]>(premiumSelection,"_shownTools");}
 static int PremiumSel(string key){return Get<int>(premiumSelection,key);}
 static int PremiumCode(){return Get<int>(premiumSelection,"localResultCode");}
 static string PremiumState(){return "foam="+PremiumSel("selectedFoam")+" water="+PremiumSel("selectedWater")+" finish="+PremiumSel("selectedFinish")+" bath="+PremiumSel("selectedBath")+" shown=["+string.Join(",",PremiumShown())+"] shownBath="+PremiumSel("_shownBath")+" rev="+PremiumSel("revision")+" code="+PremiumCode()+" serial="+PremiumSel("localResultSerial");}
 static bool PremiumNear(){return (Networking.LocalPlayer.GetPosition()-premiumSelection.transform.position).sqrMagnitude<=36f;}
 static void PremiumClick(string name){premiumBoard.Find(name).GetComponent<Button>().onClick.Invoke();}
 // Board row of an item = its place among Order items of the same category (as KH_WashSelectionBoard._Refresh lays the tab out).
 static void PremiumChoose(int item){var cats=Get<int[]>(premiumSelection,"itemCategories");int c=cats[item];int row=KH_WashSelectionEditor.Order.Where(i=>cats[i]==c).ToList().IndexOf(item);WsExpect(row>=0&&row<6,"item "+item+" listed on tab "+c);PremiumClick("Set"+c);PremiumClick("Item"+row);}
 // Button.onClick.Invoke() bypasses interactable: a request made while the board greys Take still reaches the Owner (its _Decide is the gate).
 static void PremiumSend(){premiumSerial=PremiumSel("localResultSerial");PremiumClick("Take");}
 static void PremiumTake(int item){PremiumChoose(item);PremiumSend();}
 static void PremiumReset(){premiumSerial=PremiumSel("localResultSerial");PremiumClick("Reset");}
 // Ack by the reply serial, never by the synced value (a value can already match; a refusal changes nothing). The Owner drops a request
 // within 0.3 s of the previous one without a reply, and the board ignores clicks while pending: every request waits PremiumGap after an ack.
 static Func<double,bool> PremiumAck(string what){return WsUntil(()=>PremiumSel("localResultSerial")!=premiumSerial,5,()=>"owner reply to "+what+" ("+PremiumState()+")");}
 const double PremiumGap=.45;
 static void PremiumHold(int item,int v){PremiumSlotTool(item).SetProgramVariable("debugHeldOverride",v);}
 static void PremiumRecover(){if(premiumTools==null)return;foreach(var u in premiumTools)if(u!=null){u.SendCustomEvent("_onDrop");u.SetProgramVariable("debugHeldOverride",0);}if(premiumSelection!=null)premiumSelection.SetProgramVariable("_receivedState",premiumReceived);premiumTools=null;premiumSelection=null;premiumWater=null;premiumBoard=null;}
 static void MakeWashPremiumCases(){if(WsGroup("premium")||WsGroup("premium-store"))PremiumStoreCases();   // IMP-DOG-056: first, before any PremiumBind owns the sets
 wsReport.notCovered.Add("Premium (IMP-DOG-046/047: foam / water / finish + platform): UI persistent listeners and local Owner request/ack exercised; holding is injected with debugHeldOverride, Pickup/Use events are injected. Physical grip, a tool held on another client (two-client network), late join with three tools out, sound quality and Quest rendering remain NOT_TESTED. The water pistol (one shot) and the hose (hold to spray) inputs are P-pistol-shot / P-hose-hold (group premium-water); another player's burst is the direct _OnShot call, not a received state.");
 if(WsGroup("premium")||WsGroup("premium-input"))PremiumInputCases();if(WsGroup("premium")||WsGroup("premium-other"))PremiumOtherCases();if(WsGroup("premium")||WsGroup("premium-other")||WsGroup("premium-free"))PremiumFreeCases();
 if(WsGroup("premium")||WsGroup("premium-water"))PremiumWaterCases();}   // IMP-DOG-049 (WashPremiumWater.cs): the pistol's one shot, the hose's hold
 static void PremiumInputCases(){
  // Was P-selection-pulse-toggle (water gun 0.22 s pulse), then P-selection-water-toggle (the can as a toggle). IMP-DOG-049 (2026-10-11): the can
  // tilts to pour (KH_WashTool.PulseTilt). ClientSim is Desktop: Use held leans the can's model (eased over DesktopTiltSeconds) and it pours
  // from TiltStartDegrees; Use up eases it back and stops below TiltStopDegrees; drop stops at once and snaps the model upright. The VR hand
  // tilt is not reachable here (WashPremium/44 runs MeasuredTilt on a turned pickup in Edit mode). The foam swap and Q1 follow as before.
  var s=WsCase("P-selection-water-tilt",60);s.Add(WsDo(PremiumBind));s.Add(WsUntil(PremiumNear,3,()=>"tester within the selector's 6 m"));s.Add(WsDo(()=>PremiumTake(PWateringCan)));s.Add(PremiumAck("watering can"));
  s.Add(WsDo(()=>{WsExpect(PremiumCode()==KH_WashSelection.ResultApplied&&PremiumSel("selectedWater")==PWateringCan&&PremiumShown()[1]==PWateringCan&&PremiumPickupable(PWateringCan),"watering can via UI, local owner replied success ("+PremiumState()+")");
   WsExpect(Get<float>(premiumWater,"pulseSeconds")==KH_WashTool.PulseTilt,"water slot tilts to pour for the can (pulseSeconds "+Get<float>(premiumWater,"pulseSeconds")+")");WsExpect(WsRef(premiumWater,"nozzle") as Transform==(WsRef(premiumSelection,"nozzleAnchors") as Array).GetValue(PWateringCan) as Transform,"water slot configured with the can's nozzle");
   premiumCanRest=PremiumModel(PWateringCan).transform.localRotation;
   premiumWater.SendCustomEvent("_onPickup");premiumWater.SendCustomEvent("_onPickupUseDown");WsExpect(!Get<bool>(premiumWater,"_sprayOn"),"Use down does not switch the can on by itself (no toggle)");}));
  s.Add(WsWait(.5));   // past DesktopTiltSeconds (0.3 s)
  s.Add(WsDo(()=>{float a=Get<float>(premiumWater,"_tiltAngle");float lean=Quaternion.Angle(PremiumModel(PWateringCan).transform.localRotation,premiumCanRest);
   WsExpect(Get<bool>(premiumWater,"_sprayOn")&&Get<bool>(WsRef(premiumWater,"state") as UdonBehaviour,"spraying")&&a>=KH_WashTool.TiltStartDegrees&&lean>=KH_WashTool.TiltStartDegrees,"Use held: the can leans (tilt "+a.ToString("F1")+" deg, model "+lean.ToString("F1")+" deg) and pours");
   premiumWater.SendCustomEvent("_onPickupUseUp");}));
  s.Add(WsWait(.5));
  s.Add(WsDo(()=>{float lean=Quaternion.Angle(PremiumModel(PWateringCan).transform.localRotation,premiumCanRest);
   WsExpect(!Get<bool>(premiumWater,"_sprayOn")&&!Get<bool>(WsRef(premiumWater,"state") as UdonBehaviour,"spraying")&&lean<.01f,"Use up: the can eases back upright (model "+lean.ToString("F2")+" deg) and stops");
   premiumWater.SendCustomEvent("_onPickupUseDown");}));
  s.Add(WsWait(.5));
  s.Add(WsDo(()=>{WsExpect(Get<bool>(premiumWater,"_sprayOn"),"Use held again pours again");premiumWater.SendCustomEvent("_onDrop");float lean=Quaternion.Angle(PremiumModel(PWateringCan).transform.localRotation,premiumCanRest);
   WsExpect(!Get<bool>(premiumWater,"_sprayOn")&&!Get<bool>(WsRef(premiumWater,"state") as UdonBehaviour,"spraying")&&lean<.01f,"drop stops the can at once and snaps it upright (model "+lean.ToString("F2")+" deg)");}));
  s.Add(WsWait(PremiumGap));s.Add(WsDo(()=>PremiumTake(PFoamCannon)));s.Add(PremiumAck("foam cannon"));
  s.Add(WsDo(()=>{WsExpect(PremiumCode()==KH_WashSelection.ResultApplied&&PremiumSel("selectedFoam")==PFoamCannon&&PremiumShown()[0]==PFoamCannon&&PremiumPickupable(PFoamCannon)&&!PremiumPickupable(PPawMitt),"foam cannon out on its slot ("+PremiumState()+")");}));
  s.Add(WsWait(PremiumGap));s.Add(WsDo(()=>PremiumTake(PPawMitt)));s.Add(PremiumAck("paw mitt"));
  s.Add(WsDo(()=>{var slotItem=Get<int[]>(premiumSelection,"_slotItem");var slots=Get<int[]>(premiumSelection,"itemSlots");
   WsExpect(PremiumCode()==KH_WashSelection.ResultApplied&&PremiumSel("selectedFoam")==PPawMitt&&PremiumShown()[0]==PPawMitt&&slotItem[slots[PFoamCannon]]==-1&&slotItem[slots[PPawMitt]]==PPawMitt&&!PremiumPickupable(PFoamCannon)&&PremiumPickupable(PPawMitt)&&!PremiumModel(PFoamCannon).activeSelf&&PremiumModel(PPawMitt).activeSelf,"foam swap cannon -> mitt reconfigures the foam slots only (_slotItem ["+string.Join(",",slotItem)+"])");
   WsExpect(PremiumSel("selectedWater")==PWateringCan&&PremiumShown()[1]==PWateringCan&&slotItem[slots[PWateringCan]]==PWateringCan&&PremiumPickupable(PWateringCan),"water stays out through the foam swap");}));
  s.Add(WsWait(PremiumGap));s.Add(WsDo(()=>PremiumTake(PDryer)));s.Add(PremiumAck("dryer"));
  s.Add(WsDo(()=>{var sh=PremiumShown();WsExpect(PremiumCode()==KH_WashSelection.ResultApplied&&sh[0]==PPawMitt&&sh[1]==PWateringCan&&sh[2]==PDryer&&PremiumPickupable(PPawMitt)&&PremiumPickupable(PWateringCan)&&PremiumPickupable(PDryer),"Q1 foam + water + finish out at once ("+PremiumState()+")");
   WsNote("UI local owner request/ack per category; watering can tilt-to-pour on the water slot (Desktop lean; the VR hand tilt is not reachable in ClientSim); foam swap reconfigures slots 0/1 only; Q1 three out; simulated pickup/Use/drop events");}));
 }
 static void PremiumOtherCases(){
  // Same intent as before (the held model waits during a later race, the first received state restores even when held), on foam: the water
  // category has one item now. Added: while the held foam waits, the other category and the platform change in the same apply (Q3).
  var s=WsCase("P-held-change-and-first-restore",20);s.Add(WsDo(()=>{PremiumBind();premiumSelection.SetProgramVariable("selectedFoam",PFoamCannon);premiumSelection.SetProgramVariable("revision",PremiumSel("revision")+1);HwInvoke(premiumSelection,"_Apply","animate",false);WsExpect(PremiumShown()[0]==PFoamCannon,"foam cannon out before the race");}));
  s.Add(WsDo(()=>{premiumSelection.SetProgramVariable("_receivedState",true);PremiumHold(PFoamCannon,1);premiumSelection.SetProgramVariable("selectedFoam",PPawMitt);premiumSelection.SetProgramVariable("selectedWater",PWateringCan);premiumSelection.SetProgramVariable("selectedBath",PHinoki);premiumSelection.SetProgramVariable("revision",PremiumSel("revision")+1);HwInvoke(premiumSelection,"_Apply","animate",false);
   WsExpect(PremiumShown()[0]==PFoamCannon&&PremiumModel(PFoamCannon).activeSelf&&!PremiumModel(PPawMitt).activeSelf,"held foam model retained during later race ("+PremiumState()+")");
   WsExpect(PremiumShown()[1]==PWateringCan&&PremiumSel("_shownBath")==PHinoki&&PremiumModel(PWateringCan).activeSelf&&PremiumModel(PHinoki).activeSelf,"Q3 water and platform applied while the foam waits ("+PremiumState()+")");
   premiumSelection.SetProgramVariable("_receivedState",false);HwInvoke(premiumSelection,"_Apply","animate",false);WsExpect(PremiumShown()[0]==PPawMitt&&PremiumModel(PPawMitt).activeSelf&&!PremiumModel(PFoamCannon).activeSelf,"initial restoration applies even when held");
   PremiumHold(PFoamCannon,0);premiumSelection.SetProgramVariable("_receivedState",premiumReceived);PremiumClear();WsNote("Timing states injected to exercise shared apply body; not a real LateJoin");}));
  // Brush / cologne are not offered since IMP-DOG-047, but their slot roots (5 / 6, finishKind 1 / 2) and the cosmetic branch stay: the isolation
  // check still runs on them. The count check keeps the loop from passing on an empty set.
  s=WsCase("P-dryer-and-cosmetic-isolation",20);s.Add(WsDo(()=>{if(premiumTools==null)PremiumBind();WsExpect(Get<float[]>(wsWash,"_hTime").Length==7,"compiled wash has separate dryer channel");WsExpect(Get<int>(PremiumSlotTool(PDryer),"tool")==KH_DogWash.ToolDryer,"dryer item on the dryer-channel slot");var cosmetic=premiumTools.Where(u=>Get<int>(u,"finishKind")>0).ToArray();WsExpect(cosmetic.Length>0,"cosmetic finish slots present ("+cosmetic.Length+")");WsExpect(cosmetic.All(u=>!u.GetComponent<VRC_Pickup>().pickupable),"cosmetic slots (no offered item) are never pickupable");
   var hit=HwTop(WsTorsoCenter(),part=>part==0||part==1,"premium cosmetic contact");var point=WsAt(hit).p;Networking.LocalPlayer.TeleportTo(point+new Vector3(0,0,-.5f),Quaternion.identity);foreach(var t in cosmetic){var foam=(float[])Get<float[]>(wsWash,"_foam").Clone();var wet=(float[])Get<float[]>(wsWash,"_wet").Clone();var dirt=(float[])Get<float[]>(wsWash,"_dirt").Clone();var before=Get<float[]>(wsWash,"_hTime").ToArray();HwInvoke(t,"_DebugSquirt","origin",point+Vector3.up*.15f,"direction",Vector3.down);WsExpect(foam.SequenceEqual(Get<float[]>(wsWash,"_foam"))&&wet.SequenceEqual(Get<float[]>(wsWash,"_wet"))&&dirt.SequenceEqual(Get<float[]>(wsWash,"_dirt"))&&before.SequenceEqual(Get<float[]>(wsWash,"_hTime")),"finish ray contact never enters wash cells/history");var fx=wsWash.transform.Find("PremiumFinish").GetComponent<UdonBehaviour>();WsExpect(Get<float>(fx,"_nextSend")>Time.time,"finish request reached cosmetic branch");fx.SetProgramVariable("_nextSend",0f);}WsNote("compiled Udon separate channel and ray-to-Stroke finish branch (kept slot roots 5/6, no offered cosmetic item); dryer delta verified separately in Edit; no cosmetic visual acceptance");}));
  // (c) a request for a category whose tool is held is refused by the Owner (code 1), for the outgoing and the incoming slot; put down -> applies.
  s=WsCase("P-held-category-refused",30);s.Add(WsDo(PremiumBind));s.Add(WsUntil(PremiumNear,3,()=>"tester within the selector's 6 m"));s.Add(WsDo(()=>PremiumTake(PFoamCannon)));s.Add(PremiumAck("foam cannon"));
  int rev=0;s.Add(WsDo(()=>{WsExpect(PremiumCode()==KH_WashSelection.ResultApplied&&PremiumSel("selectedFoam")==PFoamCannon,"foam cannon out ("+PremiumState()+")");PremiumHold(PFoamCannon,1);PremiumChoose(PPawMitt);}));
  s.Add(WsWait(PremiumGap));s.Add(WsDo(()=>{WsExpect(!premiumBoard.Find("Take").GetComponent<Button>().interactable,"board greys Take while a foam tool is held");rev=PremiumSel("revision");PremiumSend();}));s.Add(PremiumAck("paw mitt while the cannon is held"));
  s.Add(WsDo(()=>{WsExpect(PremiumCode()==KH_WashSelection.ResultHeld&&PremiumSel("selectedFoam")==PFoamCannon&&PremiumSel("revision")==rev&&PremiumShown()[0]==PFoamCannon,"owner refuses foam while the outgoing cannon is held: code 1, nothing changed ("+PremiumState()+")");PremiumHold(PFoamCannon,0);PremiumHold(PPawMitt,1);}));
  s.Add(WsWait(PremiumGap));s.Add(WsDo(PremiumSend));s.Add(PremiumAck("paw mitt while its own slot is held"));
  s.Add(WsDo(()=>{WsExpect(PremiumCode()==KH_WashSelection.ResultHeld&&PremiumSel("selectedFoam")==PFoamCannon&&PremiumSel("revision")==rev,"owner refuses foam while the incoming mitt slot is held: code 1 ("+PremiumState()+")");PremiumHold(PPawMitt,0);}));
  s.Add(WsWait(PremiumGap));s.Add(WsDo(PremiumSend));s.Add(PremiumAck("paw mitt after put down"));
  s.Add(WsDo(()=>{WsExpect(PremiumCode()==KH_WashSelection.ResultApplied&&PremiumSel("selectedFoam")==PPawMitt&&PremiumShown()[0]==PPawMitt,"positive control: put down, the same request applies ("+PremiumState()+")");PremiumClear();WsNote("held injected by debugHeldOverride; request via board onClick (bypasses the greyed button), refusal decided by the local Owner");}));
  // (a) Q3: a held foam tool does not block the platform or another category.
  s=WsCase("P-held-foam-platform-switch",30);s.Add(WsDo(PremiumBind));s.Add(WsUntil(PremiumNear,3,()=>"tester within the selector's 6 m"));s.Add(WsDo(()=>PremiumTake(PFoamCannon)));s.Add(PremiumAck("foam cannon"));
  s.Add(WsDo(()=>{WsExpect(PremiumCode()==KH_WashSelection.ResultApplied&&PremiumSel("selectedFoam")==PFoamCannon,"foam cannon out ("+PremiumState()+")");PremiumHold(PFoamCannon,1);}));
  s.Add(WsWait(PremiumGap));s.Add(WsDo(()=>PremiumTake(PHinoki)));s.Add(PremiumAck("hinoki while the cannon is held"));
  s.Add(WsDo(()=>{WsExpect(PremiumCode()==KH_WashSelection.ResultApplied&&PremiumSel("selectedBath")==PHinoki&&PremiumSel("_shownBath")==PHinoki&&PremiumModel(PHinoki).activeSelf,"Q3 platform switches while the foam cannon is held: code 0 ("+PremiumState()+")");WsExpect(PremiumSel("selectedFoam")==PFoamCannon&&PremiumShown()[0]==PFoamCannon&&PremiumModel(PFoamCannon).activeSelf,"held cannon untouched by the platform switch");}));
  s.Add(WsWait(PremiumGap));s.Add(WsDo(()=>PremiumTake(PWateringCan)));s.Add(PremiumAck("watering can while the cannon is held"));
  s.Add(WsDo(()=>{WsExpect(PremiumCode()==KH_WashSelection.ResultApplied&&PremiumSel("selectedWater")==PWateringCan&&PremiumShown()[1]==PWateringCan&&PremiumShown()[0]==PFoamCannon,"another category switches while foam is held ("+PremiumState()+")");PremiumHold(PFoamCannon,0);PremiumClear();WsNote("held injected by debugHeldOverride; platform and water requests through the board and the local Owner");}));
  // (b) Q2: reset while a foam tool is held keeps foam, clears the unheld categories and the platform (code 3); after put down, reset clears all (code 0).
  s=WsCase("P-reset-keeps-held",40);s.Add(WsDo(PremiumBind));s.Add(WsUntil(PremiumNear,3,()=>"tester within the selector's 6 m"));
  foreach(int item in new[]{PFoamCannon,PWateringCan,PTowel,PHinoki}){int it=item;s.Add(WsDo(()=>PremiumTake(it)));s.Add(PremiumAck("item "+it));s.Add(WsDo(()=>WsExpect(PremiumCode()==KH_WashSelection.ResultApplied,"item "+it+" applied ("+PremiumState()+")")));s.Add(WsWait(PremiumGap));}
  s.Add(WsDo(()=>{WsExpect(PremiumSel("selectedFoam")==PFoamCannon&&PremiumSel("selectedWater")==PWateringCan&&PremiumSel("selectedFinish")==PTowel&&PremiumSel("selectedBath")==PHinoki,"three tools and the platform out before reset ("+PremiumState()+")");PremiumHold(PFoamCannon,1);PremiumReset();}));s.Add(PremiumAck("reset while the cannon is held"));
  s.Add(WsDo(()=>{var sh=PremiumShown();WsExpect(PremiumCode()==KH_WashSelection.ResultKeptHeld&&PremiumSel("selectedFoam")==PFoamCannon&&sh[0]==PFoamCannon&&PremiumModel(PFoamCannon).activeSelf,"Q2 reset keeps the held foam cannon: code 3 ("+PremiumState()+")");
   WsExpect(PremiumSel("selectedWater")==-1&&PremiumSel("selectedFinish")==-1&&PremiumSel("selectedBath")==-1&&sh[1]==-1&&sh[2]==-1&&!PremiumModel(PWateringCan).activeSelf&&!PremiumModel(PTowel).activeSelf&&!PremiumModel(PHinoki).activeSelf,"Q2 reset clears the unheld water / finish and the platform");PremiumHold(PFoamCannon,0);}));
  s.Add(WsWait(PremiumGap));s.Add(WsDo(PremiumReset));s.Add(PremiumAck("reset after put down"));
  s.Add(WsDo(()=>{WsExpect(PremiumCode()==KH_WashSelection.ResultApplied&&PremiumSel("selectedFoam")==-1&&PremiumSel("selectedWater")==-1&&PremiumSel("selectedFinish")==-1&&PremiumSel("selectedBath")==-1&&PremiumShown()[0]==-1,"reset after put down clears foam too: code 0 ("+PremiumState()+")");WsNote("held injected by debugHeldOverride; reset through the board and the local Owner");PremiumRecover();}));
 }
 static void PremiumFreeCases(){
  // Same intent: a premium selection is out (positive control), the free shower still toggles and stops on drop, and reset from inside 6 m
  // clears the premium selection with a success reply. The one selectedTool + selectedBath expectation is now all three categories + the platform.
  var s=WsCase("P-free-shower-regression",25);s.Add(WsDo(PremiumBind));s.Add(WsUntil(PremiumNear,3,()=>"tester within the selector's 6 m"));s.Add(WsDo(()=>PremiumTake(PWateringCan)));s.Add(PremiumAck("positive control take"));UdonBehaviour free=null;
  s.Add(WsDo(()=>{WsExpect(PremiumCode()==KH_WashSelection.ResultApplied&&PremiumSel("selectedWater")==PWateringCan,"positive control: premium selected before reset ("+PremiumState()+")");free=GameObject.Find("KH_WashShower_0").GetComponent<UdonBehaviour>();free.SendCustomEvent("_onPickup");free.SendCustomEvent("_onPickupUseDown");free.SendCustomEvent("_onPickupUseUp");}));s.Add(WsWait(PremiumGap));
  s.Add(WsDo(()=>{WsExpect(Get<bool>(free,"_sprayOn"),"free shower still toggles");free.SendCustomEvent("_onDrop");WsExpect(!Get<bool>(free,"_sprayOn"),"free shower stops on drop");WsExpect(PremiumNear(),"reset sender is within station range");PremiumReset();}));s.Add(PremiumAck("reset"));
  s.Add(WsDo(()=>{WsExpect(PremiumSel("selectedFoam")==-1&&PremiumSel("selectedWater")==-1&&PremiumSel("selectedFinish")==-1&&PremiumSel("selectedBath")==-1&&PremiumCode()==KH_WashSelection.ResultApplied,"reset clears premium selection with success reply ("+PremiumState()+")");PremiumRecover();}));
 }
 // ---- IMP-DOG-056 group premium-store (Course/100p5): the purchase gate with ClientSim's Store (this client only) ----
 // 100p5 marks KD_Product_WashFoam Purchased before Play (ClientSimStoreManager then sends OnPurchaseConfirmed / OnPurchasesLoaded with it to
 // the local player when it joins) and puts it back after Play (PremiumStoreRestore, also on the next editor load if Play never ended here).
 const string PremiumStoreFlag="KH.CourseWalk.PremiumStore.run",PremiumStoreAsset="KH.CourseWalk.PremiumStore.purchasedAsset";
 public static void PremiumStoreBegin()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
  for(int c=1;c<4;c++){var o=PremiumProduct(c);if(o==null)throw new InvalidOperationException("UdonProduct of "+KDDefs.WashSetKeys[c]+" missing (Products/1)");if(o.Purchased)throw new InvalidOperationException(o.name+" is marked Purchased (ClientSim): clear it first, P-store-unowned-locked needs it unowned");}
  var foam=PremiumProduct(0);if(foam==null)throw new InvalidOperationException("KD_Product_WashFoam missing (Products/1)");
  if(!foam.Purchased){foam.Purchased=true;SessionState.SetString(PremiumStoreAsset,AssetDatabase.GetAssetPath(foam));}
  SessionState.SetBool(PremiumStoreFlag,true);
  try{WashHarnessGroups(new[]{"premium-store"});}catch{PremiumStoreRestore();throw;}
 }
 [InitializeOnLoadMethod] static void PremiumStoreHook(){EditorApplication.playModeStateChanged+=st=>{if(st==PlayModeStateChange.EnteredEditMode)PremiumStoreRestore();};EditorApplication.delayCall+=()=>{if(!EditorApplication.isPlayingOrWillChangePlaymode)PremiumStoreRestore();};}
 static void PremiumStoreRestore(){string path=SessionState.GetString(PremiumStoreAsset,"");SessionState.EraseString(PremiumStoreAsset);SessionState.EraseBool(PremiumStoreFlag);if(path.Length==0)return;var p=AssetDatabase.LoadAssetAtPath<VRC.Economy.UdonProduct>(path);if(p!=null&&p.Purchased){p.Purchased=false;Debug.Log("[KH Course Walk] premium-store: "+path+" Purchased put back to false");}}
 static void PremiumStoreCases(){
  wsReport.notCovered.Add("premium-store (IMP-DOG-056): ClientSim's Store is this client only. Another player's purchase opening the sets for everyone, Unknown (someone's purchases not loaded yet), the owner leaving and the 60 s revert after the wash (_Reevaluate / _StationIdle in Play), a late joiner, real Credits and the Storefront page opened by the purchase button -> 2 clients / the user's PCVR (IMP-DOG-056 §2.4). WashPremium/44 runs _Decide x 3 states and _StationIdle in Edit mode");
  // (1) no owner of the requested set: Locked, nothing changed; the board shows the price line and the purchase button. Picks a set this Play
  // does not own yet (100p5: water; an umbrella run may have granted all of them already -> SKIP).
  int cat=-1,rev=0;
  var s=WsCase("P-store-unowned-locked",30);
  s.Add(WsDo(()=>{PremiumBindStore();cat=new[]{1,2,3,0}.FirstOrDefault(c=>!PremiumOwned(c)&&!PremiumProduct(c).Purchased);if(PremiumOwned(cat)||PremiumProduct(cat).Purchased)throw new WsSkip("every wash set is owned in this Play already (an earlier group owned them; run 100p5 alone)");}));
  s.Add(WsUntil(PremiumNear,3,()=>"tester within the selector's 6 m"));
  s.Add(WsDo(()=>PremiumChoose(PremiumItemOf(cat))));
  s.Add(WsUntil(PremiumBuyShown,3,()=>"purchase button shown for the unowned set "+cat+" (notice '"+PremiumText("Notice")+"')"));
  s.Add(WsDo(()=>{WsExpect(PremiumText("Notice").EndsWith(PremiumWord(48)),"notice: price + 'purchase needed' ('"+PremiumText("Notice")+"')");rev=PremiumSel("revision");PremiumSend();}));
  s.Add(PremiumAck("an item of the unowned set "+cat));
  s.Add(WsDo(()=>WsExpect(PremiumCode()==KH_WashSelection.ResultLocked&&PremiumSel(PremiumSelKey(cat))==-1&&PremiumSel("revision")==rev,"owner refuses an item of an unowned set: code 4 Locked, selection and revision unchanged ("+PremiumState()+")")));
  s.Add(WsUntil(()=>PremiumText("Status")==PremiumWord(48),2,()=>"status shows the Locked word ('"+PremiumText("Status")+"')"));
  s.Add(WsDo(()=>{WsNote("set "+cat+" unowned (ClientSim Store: local player loaded, nobody owns it); request through the board and the local Owner");PremiumClear();}));
  // (2) the foam set marked Purchased before Play (100p5): its item applies, another set's item stays Locked; the board does not offer a purchase
  s=WsCase("P-store-purchased-before-play",40);
  s.Add(WsDo(()=>{if(!SessionState.GetBool(PremiumStoreFlag,false))throw new WsSkip("not started from Course/100p5 (only it marks the foam set Purchased before Play)");PremiumBindStore();
   var foam=PremiumProduct(0);WsExpect(foam.Purchased,"KD_Product_WashFoam.Purchased is still true in Play (it did not survive the domain reload otherwise)");WsExpect(PremiumOwned(0),"ClientSim delivered the pre-Play purchase of "+foam.name+" (OwnedProducts)");}));
  s.Add(WsUntil(PremiumNear,3,()=>"tester within the selector's 6 m"));
  s.Add(WsDo(()=>PremiumChoose(PFoamCannon)));
  s.Add(WsUntil(()=>!PremiumBuyShown()&&PremiumText("Notice").StartsWith(PremiumWord(47)),3,()=>"owned line, no purchase button ('"+PremiumText("Notice")+"', button "+PremiumBuyShown()+")"));
  s.Add(WsDo(PremiumSend));s.Add(PremiumAck("foam cannon (owned before Play)"));
  s.Add(WsDo(()=>WsExpect(PremiumCode()==KH_WashSelection.ResultApplied&&PremiumSel("selectedFoam")==PFoamCannon&&PremiumShown()[0]==PFoamCannon,"owned set: foam cannon applied ("+PremiumState()+")")));
  s.Add(WsWait(PremiumGap));
  s.Add(WsDo(()=>{if(PremiumOwned(1))throw new WsSkip("the water set is owned already");rev=PremiumSel("revision");PremiumTake(PWateringCan);}));s.Add(PremiumAck("watering can (water set not owned)"));
  s.Add(WsDo(()=>{WsExpect(PremiumCode()==KH_WashSelection.ResultLocked&&PremiumSel("selectedWater")==-1&&PremiumSel("selectedFoam")==PFoamCannon&&PremiumSel("revision")==rev,"another set stays Locked, the foam stays ("+PremiumState()+")");PremiumClear();}));
  // (3) a purchase during Play (the Store's purchase notification): the board drops the purchase button and the item applies
  int cat3=-1;
  s=WsCase("P-store-purchase-in-play",40);
  s.Add(WsDo(()=>{PremiumBindStore();cat3=new[]{1,2,3}.FirstOrDefault(c=>!PremiumOwned(c));if(cat3==0||PremiumOwned(cat3))throw new WsSkip("water / finish / station all owned in this Play already");}));
  s.Add(WsUntil(PremiumNear,3,()=>"tester within the selector's 6 m"));
  s.Add(WsDo(()=>PremiumChoose(PremiumItemOf(cat3))));
  s.Add(WsUntil(PremiumBuyShown,3,()=>"purchase button shown before the purchase (set "+cat3+")"));
  s.Add(WsDo(()=>PremiumGrant(cat3,true)));
  s.Add(WsUntil(()=>!PremiumBuyShown()&&PremiumText("Notice").StartsWith(PremiumWord(47)),3,()=>"the board follows the purchase ('"+PremiumText("Notice")+"', button "+PremiumBuyShown()+")"));
  s.Add(WsDo(PremiumSend));s.Add(PremiumAck("an item of the set bought in Play"));
  s.Add(WsDo(()=>{WsExpect(PremiumCode()==KH_WashSelection.ResultApplied&&PremiumSel(PremiumSelKey(cat3))==PremiumItemOf(cat3),"bought in Play: applied ("+PremiumState()+")");WsNote("purchase = ClientSimStoreManager.PurchaseConfirmed(product, purchasedNow true) during Play");PremiumClear();PremiumRecover();}));
 }
}}
