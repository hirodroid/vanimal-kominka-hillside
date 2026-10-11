using System;using System.IO;using System.Linq;using System.Text;using System.Reflection;using System.Collections.Generic;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using UdonSharpEditor;using VRC.SDKBase;
namespace KominkaHillside.Editor{public static partial class KH_WashSelectionEditor{
 // IMP-DOG-046 / 047 (2026-10-11, user: Q1-Q6 as recommended): three category slots (foam / water / finish, one item each, all three out
 // at once) + the platform, and the 9-item lineup of DES-DOG-008 (6 built now; hose / water gun = IMP-DOG-049, pool = IMP-DOG-044 are
 // added when built: Q4). Item ids keep the 17-item numbering of Names; a new item appends an id (17+), its name word (WordCount+) and
 // its photo. Owned by this code: 48 writes every value below on the four existing stations (scene values override code defaults),
 // 44 checks them with the measured values. The models' FBX / prefabs stay in Assets (only the scene instances of dropped items go).
 // IMP-DOG-049 / 044 (2026-10-11): + 17 WaterPistol, 18 Hose (water), 19 PoolBath (platform) = the 9 items of DES-DOG-008. They need WashPremium/49
 // (places their models) before 48 can run: 48 throws for an offered item without a model.
 public static readonly int[] Order={0,9,6,17,18,7,8,13,19};          // offered items in display order: foam, water, finish, station
 public static readonly int[] SlotCategories={0,0,1,2,2,-1,-1};      // slots 5 / 6 (brush / cologne) keep their pickup roots, never selectable
 public static readonly string[] CategoryHomeNames={"Home_Foam","Home_Water","Home_Finish"};
 // Tray beside the shelf (shelf x -0.6..0.6, z -0.225..0.225, top 0.8): foam / water / finish left to right as seen from the front (-Z).
 // Sized from the TIGHT collider boxes 48 now writes (TightBounds; = the lab's model sizes, asset-lab validation/build.json):
 // relative to its home, x / z extents: FoamCannon -.042..+.042 / -.059..+.143, PawMitt -.074..+.052 / -.093..+.100, WateringCan
 // -.075..+.075 / -.103..+.257, Dryer -.041..+.041 / -.065..+.120, PatternTowel -.165..+.165 / -.113..+.113 (2026-10-11, derived from the
 // 44 footprints of the first 48 run: those boxes were world-AABB-of-AABB, inflated by w + d*sin(2*yaw), so River (yaw 81.8) was smallest
 // and Hillside (221.5) largest). Predicted gaps, the same on every station: mitt / can 5.3 cm, can / towel 4.5 cm, towel / shelf edge
 // 2.0 cm, inside the tray by 1.6 cm (left, mitt) and 1.0 cm (right, towel). 44 prints the measured footprints; adjust here, then 48.
 public static readonly Vector3[] HomeLocal={new Vector3(-1.25f,.82f,-.04f),new Vector3(-1.07f,.82f,-.04f),new Vector3(-.785f,.82f,-.04f)};
 public static readonly Vector3 TrayCentre=new Vector3(-.975f,.82f,-.04f),TrayLocal=new Vector3(-.975f,.785f,.03f),TrayScale=new Vector3(.73f,.03f,.42f),PostLocal=new Vector3(-.975f,.38f,.03f);
 public const int WordCount=46;                                      // 42 + word 42 "held tools stay out" (Q2) + 43..45 the names of items 17..19
 // By item id (same length as Names): the word holding its name. Explicit, not 20 + id: words 37.. are other texts, so a new item
 // (id 17+) appends its name to every language in words.json (index WordCount, then WordCount += 1) and that index here.
 public static readonly int[] ItemWords={20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,43,44,45};   // 17 WaterPistol, 18 Hose, 19 PoolBath: AI-written names (13 languages)
 const int SprayMask=(1<<0)|(1<<11);                                 // as the free shower spray (KH_DogWashSetupEditor.Station.cs): Default + Environment, never floor 22
 static readonly Color OutColor=new Color(.93f,.87f,.70f);
 static bool Offered(int id){return Array.IndexOf(Order,id)>=0;}
 static int CategoryOf(int id){return Offered(id)?Groups[id]:-1;}
 // the support's mount depth under the platform collider = the model's *_SupportMount below its origin (lab handoff; PoolBath 0.4.0: .036)
 static float BathDepth(int id){return id==13?.038f:id==14?.034f:id==15?.049f:id==PoolBathId?.036f:0f;}
 // the wash ray's reach per item (KH_WashTool.sprayRange; the build wrote 1.5 m per slot, the dryer's slot 0.8 m). IMP-DOG-049 plan §3.2:
 // hose 2.5 m, water pistol 4 m (provisional, Play).
 public const float HoseRange=2.5f,WaterPistolRange=4f;
 static float SprayRange(int id){return id==7?.8f:id==HoseId?HoseRange:id==WaterPistolId?WaterPistolRange:1.5f;}
 static T At<T>(T[] a,int i){return a!=null&&i>=0&&i<a.Length?a[i]:default(T);}
 static string PathOf(Transform t){return t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;}
 /// <summary>Index of the dropped item whose prefab this scene object instantiates (-1 = none).</summary>
 static int DroppedSource(GameObject go){if(go==null||!PrefabUtility.IsAnyPrefabInstanceRoot(go))return -1;string p=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);for(int d=0;d<Names.Length;d++)if(!Offered(d)&&p==Prefabs+Names[d]+".prefab")return d;return -1;}
 // Q6 (wind streaks): thin stretched particles that fly forward fast instead of a slow mist puff. Values are provisional (Play).
 public const float DryerSpeed=2.4f,DryerLifetime=.22f,DryerSizeMin=.004f,DryerSizeMax=.008f,DryerRate=16f,DryerAlpha=.16f,DryerLengthScale=2f,DryerVelocityScale=.035f;
 /// <summary>The model's box in the slot's space from each mesh's own bounds (8 local corners), not from Renderer.bounds: the world AABB of a
 /// model on a yawed station, turned back, grows by width + depth * sin(2 * yaw) (KH_WashSelectionEditor.Build LocalBounds).</summary>
 static Bounds TightBounds(GameObject go,Transform space)
 {
  var b=new Bounds();bool first=true;
  foreach(var r in go.GetComponentsInChildren<Renderer>(true))
  {
   if(r is ParticleSystemRenderer)continue;var mf=r.GetComponent<MeshFilter>();Bounds lb;
   if(mf!=null&&mf.sharedMesh!=null)lb=mf.sharedMesh.bounds;else if(r is SkinnedMeshRenderer sk&&sk.sharedMesh!=null)lb=sk.localBounds;else continue;
   for(int a=0;a<8;a++){var p=space.InverseTransformPoint(r.transform.TransformPoint(lb.center+Vector3.Scale(lb.extents,new Vector3((a&1)==0?-1:1,(a&2)==0?-1:1,(a&4)==0?-1:1))));if(first){b=new Bounds(p,Vector3.zero);first=false;}else b.Encapsulate(p);}
  }
  if(first)throw new Exception(go.name+" has no mesh to size its pickup collider");
  return b;
 }
 static void DryerStreaks(ParticleSystem ps)
 {
  var main=ps.main;main.startSpeed=DryerSpeed;main.startLifetime=DryerLifetime;main.startSize=new ParticleSystem.MinMaxCurve(DryerSizeMin,DryerSizeMax);main.startColor=new Color(.95f,.97f,1f,DryerAlpha);main.gravityModifier=0f;main.maxParticles=32;
  var em=ps.emission;em.rateOverTime=DryerRate;var sh=ps.shape;sh.angle=5f;sh.radius=.012f;
  var r=ps.GetComponent<ParticleSystemRenderer>();r.renderMode=ParticleSystemRenderMode.Stretch;r.lengthScale=DryerLengthScale;r.velocityScale=DryerVelocityScale;
  EditorUtility.SetDirty(ps);EditorUtility.SetDirty(r);
 }
 // The private apply state of a proxy, cleared so the next Edit-mode _Apply configures every slot again (not a runtime path).
 static void ResetApplyState(KH_WashSelection sel)
 {
  var ty=typeof(KH_WashSelection);const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
  ty.GetField("_shownTools",f).SetValue(sel,null);ty.GetField("_slotItem",f).SetValue(sel,null);ty.GetField("_initialized",f).SetValue(sel,false);ty.GetField("_receivedState",f).SetValue(sel,false);
  ty.GetField("_shownBath",f).SetValue(sel,-2);ty.GetField("_seenRevision",f).SetValue(sel,-1);
 }
 /// <summary>Converts one station in place (idempotent): removes the dropped items' visuals, writes every per-item / per-slot array,
 /// the three homes and the tray, the spray masks and the dryer streaks, the free platform renderers (slats stay hidden as Wash/32 left
 /// them), clears the selection, then applies and copies proxy -> Udon. Returns the number of scene objects removed.</summary>
 static int ConfigureStation(KH_WashSelection sel,StringBuilder log)
 {
  var st=sel.transform.parent;var root=sel.transform;int n=Names.Length,k=SlotCategories.Length;string label=st.name;
  if(sel.slots==null||sel.slots.Length!=k||sel.pickups==null||sel.pickups.Length!=k||sel.pickupColliders==null||sel.pickupColliders.Length!=k||sel.slots.Any(x=>x==null))throw new Exception(label+": expected "+k+" pickup slots");
  var table=st.Find("Lift/WashTable");if(table==null)throw new Exception(label+": Lift/WashTable missing");
  // 1. the dropped items' visuals (prefab instances without scripts; their UseMist particles are inside them)
  int removed=0;var models=new GameObject[n];
  for(int i=0;i<n;i++){var m=At(sel.models,i);if(m==null)continue;if(Offered(i)){models[i]=m;continue;}log.AppendLine("  removed "+PathOf(m.transform)+" ("+Names[i]+")");UnityEngine.Object.DestroyImmediate(m);removed++;}
  foreach(var t in root.GetComponentsInChildren<Transform>(true).Concat(table.GetComponentsInChildren<Transform>(true)).ToArray()){if(t==null)continue;int d=DroppedSource(t.gameObject);if(d<0)continue;log.AppendLine("  removed stray "+PathOf(t)+" ("+Names[d]+")");UnityEngine.Object.DestroyImmediate(t.gameObject);removed++;}
  // 2. per item
  var noz=new Transform[n];var grip=new Transform[n];var gun=new Transform[n];var slotOf=new int[n];var cats=new int[n];var centers=new Vector3[n];var sizes=new Vector3[n];var pulses=new float[n];var ranges=new float[n];var sprays=new ParticleSystem[n];var sounds=new AudioClip[n];var depths=new float[n];
  for(int i=0;i<n;i++)
  {
   cats[i]=CategoryOf(i);depths[i]=BathDepth(i);bool tool=cats[i]>=0&&cats[i]<KH_WashSelection.Categories;slotOf[i]=tool?Slots[i]:-1;
   if(cats[i]>=0&&models[i]==null)throw new Exception(label+": offered item "+Names[i]+" has no model");
   if(!tool)continue;
   noz[i]=At(sel.nozzleAnchors,i);grip[i]=At(sel.gripAnchors,i);{var tb=TightBounds(models[i],sel.slots[Slots[i]].transform);var oc=At(sel.colliderCenters,i);var os=At(sel.colliderSizes,i);centers[i]=tb.center;sizes[i]=Vector3.Max(tb.size,new Vector3(.03f,.03f,.03f));if((os-sizes[i]).sqrMagnitude>1e-8f||(oc-centers[i]).sqrMagnitude>1e-8f)log.AppendLine("  collider "+label+" "+Names[i]+" size "+os.ToString("F3")+" -> "+sizes[i].ToString("F3")+" centre "+oc.ToString("F3")+" -> "+centers[i].ToString("F3"));}pulses[i]=At(sel.pulseDurations,i);ranges[i]=SprayRange(i);gun[i]=At(sel.gunHolds,i);sprays[i]=At(sel.itemSprays,i);sounds[i]=At(sel.itemSounds,i);
   if(noz[i]==null||grip[i]==null||!noz[i].IsChildOf(models[i].transform)||sizes[i].x<=.02f)throw new Exception(label+": "+Names[i]+" anchors / collider not built (run 42's data first)");
   // a Gun hold written for a kept item (GunHolds below: FoamCannon, WaterPistol, Hose) survives a re-run
   if(gun[i]!=null&&!gun[i].IsChildOf(models[i].transform)){log.AppendLine("  dropped gunHolds["+i+"] "+gun[i].name+" (not under "+Names[i]+")");gun[i]=null;}
   if(sprays[i]!=null){var c=sprays[i].collision;c.collidesWith=SprayMask;EditorUtility.SetDirty(sprays[i]);}
  }
  if(sprays[7]==null)throw new Exception(label+": the dryer has no UseMist");DryerStreaks(sprays[7]);
  if(sel.appearFx!=null){var c=sel.appearFx.collision;c.collidesWith=SprayMask;EditorUtility.SetDirty(sel.appearFx);}
  // 3. the three homes and the tray
  var homes=new Transform[KH_WashSelection.Categories];
  for(int c=0;c<homes.Length;c++){var h=root.Find(CategoryHomeNames[c]);if(h==null){h=new GameObject(CategoryHomeNames[c]).transform;h.SetParent(root,false);log.AppendLine("  created "+PathOf(h));}h.localPosition=HomeLocal[c];h.localRotation=Quaternion.identity;h.localScale=Vector3.one;homes[c]=h;}
  var tray=root.Find("TakeoutTray");if(tray!=null){tray.localPosition=TrayLocal;tray.localScale=TrayScale;}var post=root.Find("TakeoutPost");if(post!=null)post.localPosition=PostLocal;
  if(sel.toolHome==null)throw new Exception(label+": TakeoutHome missing");sel.toolHome.localPosition=TrayCentre;
  // 4. the slots: home per category; a slot with no offered item keeps its root at the tray centre, its nozzle on itself
  for(int s=0;s<k;s++)
  {
   var t=sel.slots[s];int c=SlotCategories[s];var home=c>=0?homes[c]:sel.toolHome;t.home=home;
   if(t.transform.parent!=root)throw new Exception(PathOf(t.transform)+" is not directly under PremiumTools");
   t.transform.localPosition=home.localPosition;t.transform.localRotation=Quaternion.identity;
   int first=Order.FirstOrDefault(i=>slotOf[i]==s&&cats[i]>=0&&cats[i]<KH_WashSelection.Categories);bool has=Order.Any(i=>slotOf[i]==s);
   t.nozzle=has?noz[first]:t.transform;t.spray=null;EditorUtility.SetDirty(t.transform);
   // no reference to a removed item's anchor stays on the pickup ("Missing"); the apply of an active slot writes its own hold again
   // a shared slot (water: can Grip / pistol, hose Gun) rests as Grip; _Apply sets the hold of the item it configures (IMP-DOG-049)
   var pu=sel.pickups[s];pu.ExactGrip=null;if(s!=Slots[0]){pu.ExactGun=null;pu.orientation=VRC_Pickup.PickupOrientation.Grip;}EditorUtility.SetDirty(pu);
  }
  // 5. the free platform: its renderers except the old slats (hidden by Wash/32 under the imported platform) and the premium models
  var platform=table.Find("ImportedPlatform");var premium=models.Where(m=>m!=null).Select(m=>m.transform).ToArray();int slatsHidden=0;
  foreach(Transform ch in table){if(!ch.name.StartsWith("Slat_")||platform==null)continue;var mr=ch.GetComponent<MeshRenderer>();if(mr!=null&&mr.enabled){mr.enabled=false;EditorUtility.SetDirty(mr);slatsHidden++;}}
  var free=table.GetComponentsInChildren<Renderer>(true).Where(r=>!(r is ParticleSystemRenderer)&&!(platform!=null&&r.transform.parent==table&&r.name.StartsWith("Slat_"))&&!premium.Any(p=>r.transform.IsChildOf(p))).ToArray();
  // 6. write, hold, apply, copy
  sel.models=models;sel.itemCategories=cats;sel.itemSlots=slotOf;sel.slotCategories=(int[])SlotCategories.Clone();sel.categoryHomes=homes;
  sel.nozzleAnchors=noz;sel.gripAnchors=grip;sel.gunHolds=gun;sel.colliderCenters=centers;sel.colliderSizes=sizes;sel.pulseDurations=pulses;sel.sprayRanges=ranges;sel.itemSprays=sprays;sel.itemSounds=sounds;sel.bathDepths=depths;sel.freeTableRenderers=free;
  sel.selectedFoam=-1;sel.selectedWater=-1;sel.selectedFinish=-1;sel.selectedBath=-1;
  GunHolds(sel,log);     // FoamCannon: slot 0 Gun + its Exact Gun (BUG-DOG-010); WaterPistol / Hose: gunHolds = the FBX *_GunHold (IMP-DOG-049)
  ResetApplyState(sel);sel._Apply(false);
  foreach(var p in sel.pickups)EditorUtility.SetDirty(p);foreach(var c in sel.pickupColliders)EditorUtility.SetDirty(c);
  foreach(var t in sel.slots)Copy(t);Copy(sel);
  log.AppendLine(label+": removed="+removed+" slatsHidden="+slatsHidden+" freeTableRenderers="+free.Length+" ("+string.Join(", ",free.Select(r=>r.name))+") homes="+string.Join(" ",homes.Select(h=>h.name+h.localPosition.ToString("F3"))));
  return removed;
 }
 static void ConfigureBoard(KH_WashSelectionBoard b,KH_WashSelection sel,WordsFile ws)
 {
  int n=Names.Length;
  b.selection=sel;b.languageCodes=ws.languages.Select(x=>x.code).ToArray();b.words=ws.languages.SelectMany(x=>x.words).ToArray();b.wordCount=WordCount;
  b.groups=Enumerable.Range(0,n).Select(CategoryOf).ToArray();b.order=(int[])Order.Clone();if(ItemWords.Length!=n)throw new Exception("ItemWords needs one word per item ("+ItemWords.Length+" / "+n+")");b.itemWords=(int[])ItemWords.Clone();
  b.setPhotos=Enumerable.Range(0,5).Select(i=>AssetDatabase.LoadAssetAtPath<Texture2D>(Data+"Photos/Set"+i+".png")).ToArray();
  // Only the offered items' photos stay referenced (a referenced texture ships in the world).
  b.itemPhotos=Enumerable.Range(0,n).Select(i=>Offered(i)?AssetDatabase.LoadAssetAtPath<Texture2D>(Data+"Photos/Item"+i+".png"):null).ToArray();
  b.normalColor=Paper;b.selectedColor=new Color(.77f,.85f,.73f);b.outColor=OutColor;
  // rows hidden by the new catalog kept their old photos (e.g. LargeSponge in the foam tab, row 3): clear all, _Tab0 fills the listed ones
  foreach(var im in b.itemImages)if(im!=null){im.texture=null;EditorUtility.SetDirty(im);}
  b._Tab0();Copy(b);
 }
 static WordsFile CheckedWords(){var ws=ReadWords();if(ws.languages.Length!=13||ws.languages.Any(l=>l.words.Length!=WordCount))throw new Exception("13 languages x "+WordCount+" words required in words.json");return ws;}
 /// <summary>vrc-scene-edit-safety: removes only NetworkIDs entries whose object is gone AND that carry no type; a typed one is kept
 /// and reported. A DestroyImmediate in the same call can still look alive here, so run 48 again after saving (it is idempotent).</summary>
 static void CleanNetworkIds(StringBuilder log,string when)
 {
  var desc=UnityEngine.Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();if(desc==null||desc.NetworkIDCollection==null){log.AppendLine("NetworkIDs "+when+": no descriptor");return;}
  int before=desc.NetworkIDCollection.Count,removed=0,typed=0;
  for(int i=desc.NetworkIDCollection.Count-1;i>=0;i--)
  {
   var pair=desc.NetworkIDCollection[i];if(pair!=null&&pair.gameObject!=null)continue;
   if(pair!=null&&pair.SerializedTypeNames!=null&&pair.SerializedTypeNames.Count>0){typed++;log.AppendLine("WARN NetworkID "+pair.ID+" has types ("+string.Join(",",pair.SerializedTypeNames)+") but no object; kept");continue;}
   desc.NetworkIDCollection.RemoveAt(i);removed++;
  }
  if(removed>0)EditorUtility.SetDirty(desc);
  log.AppendLine("NetworkIDs "+when+": before="+before+" removedTypelessNull="+removed+" keptTypedNull="+typed+" after="+desc.NetworkIDCollection.Count);
 }
 [MenuItem(Menu+"48 - Migrate To Category Slots")]
 public static void MigrateToCategorySlots()
 {
  Guard();var scene=EditorSceneManager.GetActiveScene();if(scene.isDirty)throw new Exception("Save/reconcile unrelated scene edits first");
  var sels=UnityEngine.Object.FindObjectsOfType<KH_WashSelection>(true).Where(x=>x.gameObject.scene==scene).OrderBy(x=>x.transform.parent.name).ToArray();
  if(sels.Length!=4)throw new Exception("Expected four selectors, found "+sels.Length);
  var ws=CheckedWords();var log=new StringBuilder();int removed=0;
  CleanNetworkIds(log,"before");
  foreach(var sel in sels)
  {
   removed+=ConfigureStation(sel,log);
   var b=sel.transform.parent.GetComponentInChildren<KH_WashSelectionBoard>(true);if(b==null)throw new Exception(sel.transform.parent.name+": no PremiumSelectionBoard");ConfigureBoard(b,sel,ws);
  }
  // The inactive material gallery of 2026-10-08 (KH_WashPremiumEditor 20) is part of the scene and ships too: drop the same items.
  var gallery=scene.GetRootGameObjects().FirstOrDefault(x=>x.name=="KH_WashPremiumPreview_Inactive");int galleryRemoved=0;
  if(gallery!=null)foreach(Transform t in gallery.transform.Cast<Transform>().ToArray())if(DroppedSource(t.gameObject)>=0||Names.Select((nm,i)=>new{nm,i}).Any(x=>!Offered(x.i)&&x.nm==t.name)){log.AppendLine("  removed "+PathOf(t));UnityEngine.Object.DestroyImmediate(t.gameObject);galleryRemoved++;}
  CleanNetworkIds(log,"after");
  EditorSceneManager.MarkSceneDirty(scene);
  Log("migrate","selectors="+sels.Length+" removed station objects="+removed+" gallery="+galleryRemoved+" offered="+string.Join(",",Order.Select(i=>Names[i]))+"\n"+log+"NOT saved: run 44 - Verify Selection and Wash/12, then save. After saving, run 48 once more if 44 reports typeless NetworkIDs left");
 }
 // ---- 44: the category-slot checks (called from VerifySelection per station, then once on the first station for the logic) ----
 static void VerifyCategories(KH_WashSelection sel,Action<bool,string> check,StringBuilder log)
 {
  string st=sel.transform.parent.name;int n=Names.Length,k=SlotCategories.Length;var back=UdonSharpEditorUtility.GetBackingUdonBehaviour(sel);
  check(sel.models!=null&&sel.models.Length==n&&sel.itemCategories!=null&&sel.itemCategories.Length==n,"per-item arrays length "+n+" "+st+" (models "+(sel.models==null?-1:sel.models.Length)+")");
  if(sel.models==null||sel.models.Length!=n||sel.itemCategories==null||sel.itemCategories.Length!=n)return;
  check(sel.itemCategories.SequenceEqual(Enumerable.Range(0,n).Select(CategoryOf)),"itemCategories "+st+" = ["+string.Join(",",sel.itemCategories)+"]");
  check(sel.itemSlots!=null&&sel.itemSlots.SequenceEqual(Enumerable.Range(0,n).Select(i=>CategoryOf(i)>=0&&CategoryOf(i)<3?Slots[i]:-1)),"itemSlots "+st+" = ["+(sel.itemSlots==null?"null":string.Join(",",sel.itemSlots))+"]");
  check(sel.slotCategories!=null&&sel.slotCategories.SequenceEqual(SlotCategories),"slotCategories "+st+" = ["+(sel.slotCategories==null?"null":string.Join(",",sel.slotCategories))+"]");
  check(sel.bathDepths!=null&&sel.bathDepths.Length==n&&Mathf.Abs(sel.bathDepths[13]-.038f)<1e-6f&&Mathf.Abs(sel.bathDepths[PoolBathId]-.036f)<1e-6f,"bathDepths "+st+" (hinoki "+(sel.bathDepths!=null&&sel.bathDepths.Length>13?sel.bathDepths[13].ToString("F3"):"-")+", pool "+(sel.bathDepths!=null&&sel.bathDepths.Length>PoolBathId?sel.bathDepths[PoolBathId].ToString("F3"):"-")+")");
  check(sel.sprayRanges!=null&&sel.sprayRanges.Length==n&&Enumerable.Range(0,n).All(i=>Mathf.Abs(sel.sprayRanges[i]-SprayRange(i))<1e-6f),"sprayRanges "+st+" (can "+(sel.sprayRanges!=null&&sel.sprayRanges.Length>6?sel.sprayRanges[6].ToString("F1"):"-")+" / pistol "+(sel.sprayRanges!=null&&sel.sprayRanges.Length>WaterPistolId?sel.sprayRanges[WaterPistolId].ToString("F1"):"-")+" / hose "+(sel.sprayRanges!=null&&sel.sprayRanges.Length>HoseId?sel.sprayRanges[HoseId].ToString("F1"):"-")+" m)");
  foreach(var name in new[]{"nozzleAnchors","gripAnchors","gunHolds","colliderCenters","colliderSizes","pulseDurations","sprayRanges","itemSprays","itemSounds"}){var a=typeof(KH_WashSelection).GetField(name).GetValue(sel) as Array;check(a!=null&&a.Length==n,name+".Length == "+n+" "+st+" (actual "+(a==null?"null":a.Length.ToString())+")");}
  // the three homes, each slot's home, and the footprints on the tray
  check(sel.categoryHomes!=null&&sel.categoryHomes.Length==3&&Enumerable.Range(0,3).All(c=>sel.categoryHomes[c]!=null&&sel.categoryHomes[c].name==CategoryHomeNames[c]&&sel.categoryHomes[c].parent==sel.transform&&(sel.categoryHomes[c].localPosition-HomeLocal[c]).sqrMagnitude<1e-8f),
   "three category homes "+st+" ("+(sel.categoryHomes==null?"null":string.Join(" ",sel.categoryHomes.Select(h=>h==null?"null":h.name+h.localPosition.ToString("F3"))))+")");
  if(sel.categoryHomes==null||sel.categoryHomes.Length!=3||sel.categoryHomes.Any(h=>h==null))return;
  string[] catNames={"foam","water","finish","platform"};
  for(int c=0;c<4;c++){int selc=sel._Selected(c);log.AppendLine("category "+st+" "+catNames[c]+": selectable ["+string.Join(", ",Order.Where(i=>CategoryOf(i)==c).Select(i=>Names[i]))+"] slots ["+string.Join(",",Enumerable.Range(0,k).Where(s=>SlotCategories[s]==c))+"] selected="+(selc<0?"none (free)":Names[selc]));}
  for(int s=0;s<k&&s<sel.slots.Length;s++){int c=SlotCategories[s];var want=c>=0?sel.categoryHomes[c]:sel.toolHome;var t=sel.slots[s];log.AppendLine("slot"+s+" "+st+" category="+(c<0?"none":catNames[c])+" items=["+string.Join(", ",Order.Where(i=>s<k&&At(sel.itemSlots,i)==s).Select(i=>Names[i]))+"] pickupable="+sel.pickups[s].pickupable+" orientation="+sel.pickups[s].orientation+" home="+(t.home==null?"null":t.home.name));check(t.home==want&&(t.transform.localPosition-want.localPosition).sqrMagnitude<1e-8f,"slot"+s+" home "+(want!=null?want.name:"null")+" "+st+" (actual "+(t.home==null?"null":t.home.name)+" at "+t.transform.localPosition.ToString("F3")+")");}
  var boxes=new List<KeyValuePair<int,Rect>>();
  foreach(int i in Order){int c=CategoryOf(i);if(c<0||c>=3)continue;var h=sel.categoryHomes[c].localPosition;var ctr=sel.colliderCenters[i];var sz=sel.colliderSizes[i];var r=UnityEngine.Rect.MinMaxRect(h.x+ctr.x-sz.x*.5f,h.z+ctr.z-sz.z*.5f,h.x+ctr.x+sz.x*.5f,h.z+ctr.z+sz.z*.5f);boxes.Add(new KeyValuePair<int,Rect>(i,r));log.AppendLine("footprint "+st+" "+Names[i]+" x "+r.xMin.ToString("F3")+".."+r.xMax.ToString("F3")+" z "+r.yMin.ToString("F3")+".."+r.yMax.ToString("F3")+" (station local, collider box at its home)");}
  foreach(var a in boxes)foreach(var b in boxes){if(a.Key>=b.Key||CategoryOf(a.Key)==CategoryOf(b.Key))continue;float gx=Mathf.Max(a.Value.xMin-b.Value.xMax,b.Value.xMin-a.Value.xMax),gz=Mathf.Max(a.Value.yMin-b.Value.yMax,b.Value.yMin-a.Value.yMax);float gap=Mathf.Max(gx,gz);check(gap>=.01f,"tray gap "+Names[a.Key]+" / "+Names[b.Key]+" "+st+" "+(gap*100f).ToString("F1")+" cm (>= 1 cm; all three can be out at once)");}
  float shelfGap=boxes.Count==0?0:-.6f-boxes.Max(x=>x.Value.xMax);log.AppendLine("shelf "+st+": nearest tray item to the shelf's left edge (x -0.600) "+(shelfGap*100f).ToString("F1")+" cm; tray x "+(TrayLocal.x-TrayScale.x*.5f).ToString("F3")+".."+(TrayLocal.x+TrayScale.x*.5f).ToString("F3"));
  check(shelfGap>=0f,"tray items stay left of the shelf "+st+" ("+(shelfGap*100f).ToString("F1")+" cm)");
  VerifyTray(sel,boxes,check,log);
  // the dropped items are gone; the kept ones remain
  for(int i=0;i<n;i++){bool offered=CategoryOf(i)>=0;check(offered==(sel.models[i]!=null),(offered?"model present ":"no model for dropped ")+Names[i]+" "+st);if(!offered)check(At(sel.itemSprays,i)==null&&At(sel.nozzleAnchors,i)==null,"no spray / anchor kept for dropped "+Names[i]+" "+st);}
  var strays=sel.transform.GetComponentsInChildren<Transform>(true).Concat(sel.transform.parent.Find("Lift/WashTable").GetComponentsInChildren<Transform>(true)).Where(t=>DroppedSource(t.gameObject)>=0).Select(t=>t.name).ToArray();
  check(strays.Length==0,"no dropped item instance under PremiumTools / WashTable "+st+" ("+string.Join(", ",strays)+")");
  // backing heap (written by CopyProxyToUdon in 48)
  object v;
  check(back.publicVariables.TryGetVariableValue("itemCategories",out v)&&v is int[] bc&&bc.SequenceEqual(sel.itemCategories),"saved backing itemCategories "+st);
  check(back.publicVariables.TryGetVariableValue("slotCategories",out v)&&v is int[] bs&&bs.SequenceEqual(SlotCategories),"saved backing slotCategories "+st);
  check(back.publicVariables.TryGetVariableValue("categoryHomes",out v)&&v is Transform[] bh&&bh.SequenceEqual(sel.categoryHomes),"saved backing categoryHomes "+st);
  // IMP-DOG-049: non-null exactly for the Gun items (FoamCannon its own child, WaterPistol / Hose their FBX *_GunHold), the same as the proxy
  check(back.publicVariables.TryGetVariableValue("gunHolds",out v)&&v is Transform[] bg&&bg.Length==n&&Enumerable.Range(0,n).All(i=>GunItems.Contains(i)&&Offered(i)?bg[i]!=null&&bg[i]==sel.gunHolds[i]&&bg[i].name==GunHoldName(i):bg[i]==null),"saved backing gunHolds: "+string.Join(", ",GunItems.Select(i=>Names[i]+" "+GunHoldName(i)))+", others Grip "+st);
  check(back.publicVariables.TryGetVariableValue("sprayRanges",out v)&&v is float[] br&&sel.sprayRanges!=null&&br.SequenceEqual(sel.sprayRanges),"saved backing sprayRanges "+st);
  check(back.publicVariables.TryGetVariableValue("pulseDurations",out v)&&v is float[] bp&&bp.SequenceEqual(sel.pulseDurations)&&Mathf.Abs(bp[WaterPistolId]-WaterPistolShot)<1e-6f&&bp[HoseId]==KH_WashTool.PulseHold&&bp[6]==0f,"saved backing pulseDurations: can toggle 0, pistol shot "+WaterPistolShot.ToString("F2")+" s, hose hold "+KH_WashTool.PulseHold+" "+st);
  foreach(var f in new[]{"selectedFoam","selectedWater","selectedFinish","selectedBath"}){int pv=(int)typeof(KH_WashSelection).GetField(f).GetValue(sel);bool ok=back.publicVariables.TryGetVariableValue(f,out v)&&v is int bv&&bv==pv;log.AppendLine("value "+st+" "+f+"="+pv+" backing="+(v??"missing"));check(ok,"saved backing "+f+" == proxy "+st);}
  log.AppendLine("value "+st+" stale backing selectedTool (the removed 1-selection field; not in the program, unused): "+(back.publicVariables.TryGetVariableValue("selectedTool",out v)?"present":"absent"));
  // the free platform: slats stay hidden under the imported platform, never listed as the free table
  var table=sel.transform.parent.Find("Lift/WashTable");var platform=table.Find("ImportedPlatform");var slats=table.Cast<Transform>().Where(t=>t.name.StartsWith("Slat_")).Select(t=>t.GetComponent<MeshRenderer>()).Where(r=>r!=null).ToArray();
  check(sel.freeTableRenderers!=null&&sel.freeTableRenderers.All(r=>r!=null&&!r.name.StartsWith("Slat_")&&!sel.models.Where(m=>m!=null).Any(m=>r.transform.IsChildOf(m.transform)))&&sel.freeTableRenderers.Length>0,"freeTableRenderers "+st+" = ["+(sel.freeTableRenderers==null?"null":string.Join(", ",sel.freeTableRenderers.Select(r=>r==null?"null":r.name)))+"] (no slat, no premium model)");
  if(platform!=null)check(slats.All(r=>!r.enabled),"old slats hidden under ImportedPlatform "+st+" ("+slats.Count(r=>r.enabled)+" / "+slats.Length+" enabled)");
  var dryer=At(sel.itemSprays,7);var dr=dryer!=null?dryer.GetComponent<ParticleSystemRenderer>():null;
  check(dr!=null&&dr.renderMode==ParticleSystemRenderMode.Stretch&&Mathf.Abs(dryer.main.startSpeed.constant-DryerSpeed)<1e-4f,"dryer wind streaks "+st+" (mode "+(dr==null?"-":dr.renderMode.ToString())+", speed "+(dryer==null?"-":dryer.main.startSpeed.constant.ToString("F2"))+")");
  foreach(var ps in sel.itemSprays.Where(x=>x!=null))check((ps.collision.collidesWith.value&(1<<22))==0,"spray "+ps.transform.parent.name+"/"+ps.name+" collidesWith leaves out floor 22 "+st+" (mask "+ps.collision.collidesWith.value+")");
 }
 /// <summary>Edit-mode logic on one station's proxy (the shared runtime bodies _Decide / _Apply): Q1 three out at once, Q3 platform and
 /// other categories while a tool is held, Q2 reset keeps held tools, held model retained until put down. Restores everything.</summary>
 static void VerifyCategoryLogic(KH_WashSelection sel,Action<bool,string> check,StringBuilder log)
 {
  string st=sel.transform.parent.name;int f0=sel.selectedFoam,w0=sel.selectedWater,n0=sel.selectedFinish,b0=sel.selectedBath,r0=sel.revision;var held=new int[sel.slots.Length];for(int s=0;s<held.Length;s++)held[s]=sel.slots[s].debugHeldOverride;
  var recv=typeof(KH_WashSelection).GetField("_receivedState",BindingFlags.Instance|BindingFlags.NonPublic);
  Func<string> state=()=>"foam="+sel.selectedFoam+" water="+sel.selectedWater+" finish="+sel.selectedFinish+" bath="+sel.selectedBath+" rev="+sel.revision;
  try
  {
   ResetApplyState(sel);sel.selectedFoam=sel.selectedWater=sel.selectedFinish=sel.selectedBath=-1;sel._Apply(false);
   // Q1: one item per category, all three out together, each on its own pickup with its own nozzle
   foreach(int item in new[]{0,6,7}){int code=sel._Decide(item,sel.revision);sel.revision++;sel._Apply(false);check(code==0,"decide "+Names[item]+" -> 0 (actual "+code+", "+state()+")");}
   var on=Enumerable.Range(0,sel.pickups.Length).Where(s=>sel.pickups[s].pickupable).ToArray();
   check(on.SequenceEqual(new[]{0,2,4})&&sel.models[0].activeSelf&&sel.models[6].activeSelf&&sel.models[7].activeSelf,"Q1 foam + water + finish out at once: pickups ["+string.Join(",",on)+"] (want 0,2,4)");
   check(sel.slots[0].nozzle==sel.nozzleAnchors[0]&&sel.slots[2].nozzle==sel.nozzleAnchors[6]&&sel.slots[4].nozzle==sel.nozzleAnchors[7]&&sel.slots[4].spray==sel.itemSprays[7],"each out slot carries its item's nozzle / spray");
   check(sel.pickups[0].orientation==VRC_Pickup.PickupOrientation.Gun&&sel.pickups[0].ExactGun==sel.gunHolds[0]&&sel.pickups[2].orientation==VRC_Pickup.PickupOrientation.Grip&&sel.pickups[2].ExactGrip==sel.gripAnchors[6],"hold per item: FoamCannon Gun / WateringCan Grip (actual "+sel.pickups[0].orientation+" / "+sel.pickups[2].orientation+")");
   // IMP-DOG-049: the water slot takes each water item's hold, nozzle, spray, input and reach (pistol / hose Gun, back to the can Grip)
   foreach(int item in new[]{WaterPistolId,HoseId,6}){int code=sel._Decide(item,sel.revision);sel.revision++;sel._Apply(false);var pu=sel.pickups[Slots[item]];var t=sel.slots[Slots[item]];bool gun=sel.gunHolds[item]!=null;
    check(code==0&&sel.selectedWater==item&&sel.models[item].activeSelf&&pu.pickupable&&t.nozzle==sel.nozzleAnchors[item]&&t.spray==sel.itemSprays[item]&&t.pulseSeconds==sel.pulseDurations[item]&&Mathf.Abs(t.sprayRange-sel.sprayRanges[item])<1e-6f&&pu.orientation==(gun?VRC_Pickup.PickupOrientation.Gun:VRC_Pickup.PickupOrientation.Grip)&&(gun?pu.ExactGun==sel.gunHolds[item]:pu.ExactGrip==sel.gripAnchors[item])&&sel.selectedFoam==0&&sel.selectedFinish==7,
     "water swap -> "+Names[item]+": slot"+Slots[item]+" "+pu.orientation+", pulse "+t.pulseSeconds+", range "+t.sprayRange+" m, foam / finish stay ("+state()+")");}
   // swap inside one category leaves the others
   int c9=sel._Decide(9,sel.revision);sel.revision++;sel._Apply(false);check(c9==0&&sel.selectedFoam==9&&sel.selectedWater==6&&sel.selectedFinish==7&&sel.pickups[1].pickupable&&!sel.pickups[0].pickupable&&sel.pickups[2].pickupable,"foam swap to PawMitt leaves water / finish ("+state()+")");
   // Q3: the water slot held -> water requests refused, the platform and the other categories accepted
   sel.slots[2].debugHeldOverride=1;
   int cw=sel._Decide(6,sel.revision);check(cw==KH_WashSelection.ResultHeld&&sel.selectedWater==6,"held water: water request -> 1 (actual "+cw+")");
   int cb=sel._Decide(13,sel.revision);sel.revision++;sel._Apply(false);check(cb==0&&sel.selectedBath==13&&sel.models[13].activeSelf&&sel.freeTableRenderers.All(r=>!r.enabled),"Q3 held water: platform -> Hinoki accepted and shown, free table hidden (code "+cb+", "+state()+")");
   // IMP-DOG-044: the pool as the platform (support mount depth .036), then back to the hinoki
   int cp=sel._Decide(PoolBathId,sel.revision);sel.revision++;sel._Apply(false);check(cp==0&&sel.selectedBath==PoolBathId&&sel.models[PoolBathId].activeSelf&&!sel.models[13].activeSelf&&sel.freeTableRenderers.All(r=>!r.enabled)&&sel.support!=null&&Mathf.Abs(sel.support.mountBelowCollider-.036f)<1e-6f,"held water: platform -> PoolBath shown, hinoki hidden, support mount "+(sel.support==null?"-":sel.support.mountBelowCollider.ToString("F3"))+" ("+state()+")");
   sel._Decide(13,sel.revision);sel.revision++;sel._Apply(false);
   int cf=sel._Decide(0,sel.revision);sel.revision++;sel._Apply(false);check(cf==0&&sel.selectedFoam==0&&sel.models[0].activeSelf,"held water: foam -> FoamCannon accepted ("+state()+")");
   int stale=sel._Decide(8,sel.revision-1);check(stale==KH_WashSelection.ResultStale,"stale revision -> 2 (actual "+stale+")");
   // Q2: reset keeps the held category, clears the rest and the platform
   int cr=sel._Decide(-1,sel.revision);sel.revision++;sel._Apply(false);
   check(cr==KH_WashSelection.ResultKeptHeld&&sel.selectedWater==6&&sel.selectedFoam==-1&&sel.selectedFinish==-1&&sel.selectedBath==-1&&sel.models[6].activeSelf&&!sel.models[0].activeSelf&&!sel.models[13].activeSelf&&sel.freeTableRenderers.All(r=>r.enabled),"Q2 reset with water held -> 3, water stays, others and platform back to free ("+state()+")");
   // a held model stays until put down (the race after the owner's decision), the platform still changes (Q3 on the display side)
   sel.slots[2].debugHeldOverride=0;sel._Decide(-1,sel.revision);sel.revision++;sel._Apply(false);
   sel._Decide(0,sel.revision);sel.revision++;sel._Apply(false);recv.SetValue(sel,true);sel.slots[0].debugHeldOverride=1;
   sel.selectedFoam=9;sel.selectedBath=13;sel.revision++;sel._Apply(false);
   check(sel.models[0].activeSelf&&!sel.models[9].activeSelf&&sel.models[13].activeSelf,"held FoamCannon kept while foam -> PawMitt waits; platform applied meanwhile");
   sel.slots[0].debugHeldOverride=0;sel._Apply(false);check(!sel.models[0].activeSelf&&sel.models[9].activeSelf&&sel.pickups[1].pickupable&&!sel.pickups[0].pickupable,"put down -> PawMitt replaces it");
   int all=sel._Decide(-1,sel.revision);check(all==0,"reset with nothing held -> 0 (actual "+all+")");
   check(sel.pickups[0].orientation==VRC_Pickup.PickupOrientation.Gun&&sel.pickups[0].ExactGun!=null&&sel.pickups[0].ExactGun.name==FoamHoldName,"FoamCannon Gun hold survives the category cycle");
   log.AppendLine("logic "+st+" (Edit mode, proxy, _Decide/_Apply shared with runtime): "+state());
  }
  finally
  {
   for(int s=0;s<held.Length;s++)sel.slots[s].debugHeldOverride=held[s];
   sel.selectedFoam=f0;sel.selectedWater=w0;sel.selectedFinish=n0;sel.selectedBath=b0;sel.revision=r0;ResetApplyState(sel);sel._Apply(false);RestHolds(sel);
  }
 }
 /// <summary>Every dropped item's instance anywhere in the open scene (stations, the gallery, anything else).</summary>
 /// <summary>44: every item photo on the board's rows (shown or hidden) is a listed item's photo; the name table points at real words.</summary>
 static void VerifyBoardRows(KH_WashSelectionBoard b,Action<bool,string> check)
 {
  string st=b.transform.parent.name;var listed=new HashSet<Texture>(Order.Select(i=>(Texture)At(b.itemPhotos,i)).Where(t=>t!=null));
  var stale=Enumerable.Range(0,b.itemImages.Length).Where(r=>b.itemImages[r]!=null&&b.itemImages[r].texture!=null&&(!listed.Contains(b.itemImages[r].texture)||!b.itemRows[r].activeSelf)).Select(r=>"row"+r+"="+b.itemImages[r].texture.name+(b.itemRows[r].activeSelf?"":" (hidden)")).ToArray();
  check(stale.Length==0,"board rows reference only listed photos, hidden rows none "+st+" ("+string.Join(", ",stale)+")");
  check(ItemWords.Length==Names.Length&&Order.All(i=>ItemWords[i]>=20&&ItemWords[i]<WordCount)&&Order.Select(i=>ItemWords[i]).Distinct().Count()==Order.Length,"ItemWords: one distinct name word per listed item, inside the "+WordCount+" words");
 }
 static string[] DroppedInScene(){var scene=EditorSceneManager.GetActiveScene();return scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>DroppedSource(t.gameObject)>=0).Select(t=>PathOf(t)).ToArray();}
 static void NetworkIdsReport(Action<bool,string> check,StringBuilder log)
 {
  var desc=UnityEngine.Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();if(desc==null||desc.NetworkIDCollection==null){check(false,"VRCSceneDescriptor NetworkIDs");return;}
  int typeless=desc.NetworkIDCollection.Count(p=>p!=null&&p.gameObject==null&&(p.SerializedTypeNames==null||p.SerializedTypeNames.Count==0));
  int typed=desc.NetworkIDCollection.Count(p=>p!=null&&p.gameObject==null&&p.SerializedTypeNames!=null&&p.SerializedTypeNames.Count>0);
  log.AppendLine("NetworkIDs total="+desc.NetworkIDCollection.Count+" typelessNull="+typeless+" typedNull="+typed+(typed>0?" (WARN: typed entries are never removed by a script; investigate)":""));
  check(typeless==0,"NetworkIDs: no typeless entry without an object ("+typeless+"; after saving, run 48 again to remove them)");
 }
}}
