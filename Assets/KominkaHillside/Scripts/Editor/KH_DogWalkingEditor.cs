using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using Unity.AI.Navigation;
using UdonSharp;
using UdonSharpEditor;
using VRC.SDKBase;
using VRC.SDK3.Components;
using KominkaDogRun;

namespace KominkaHillside.Editor
{
    public static class KH_DogWalkingEditor
    {
        const string WorldScene = "Assets/Scenes/VRCDefaultWorldScene.unity";
        const string SourceScene = "Assets/KominkaHillside/Imported/KH_DogPortSource.unity";
        const string Data = "Assets/KominkaHillside/Data/Walking";
        static readonly string[] Keys = {"Corgi","Pome","Lab","Dachs","Shiba","Samoyed"};
        // Same Product (ownership) and Listing (purchase page) as KominkaDogRun (world-ui-spec section 6). Asset copied with its GUID.
        public const string TrainerProductPath="Assets/KominkaHillside/Data/CE/KD_Product_Trainer.asset";
        public const string TrainerProductId="prod_8c044b21-35f2-4ebb-80f9-e4f685f7bf9f", TrainerListingId="prod_81a5ac65-bd3d-40ad-8edb-2fc7d13b1cee";
        // BUG-DOG-001: balls rest on the flat upper clearing (KH_TerrainPrototypeConfig.Flats[PlazaFlat], y=19 within r=12),
        // off the trail end and away from the shade tree. VRCObjectSync respawns to this start position.
        public static readonly Vector2 BallHomeCenter = new Vector2(KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,0]+3.5f, KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,1]+3.5f);
        public const float BallSpacing=0.9f, BallRestHeight=0.07f;
        // IMP-DOG-005: discs rest in a second row behind the balls (same flat), one per dog; stage 2 wires only these breeds.
        public static readonly string[] FrisbeeKeys = {"Lab"};
        public static readonly Color[] DiscColors = {new Color(.90f,.30f,.25f),new Color(.95f,.60f,.75f),new Color(.95f,.80f,.15f),new Color(.35f,.55f,.95f),new Color(.85f,.45f,.20f),new Color(.90f,.90f,.95f)};
        public static Vector3 DiscHome(int i)
        {
            Vector3 b=BallHome(i);float z=b.z+2.2f;
            return new Vector3(b.x,Ground(b.x,z)+0.03f,z);
        }
        public static Vector3 BallHome(int i)
        {
            float x=BallHomeCenter.x+((i%3)-1)*BallSpacing, z=BallHomeCenter.y+((i/3)-0.5f)*BallSpacing;
            return new Vector3(x,Ground(x,z)+BallRestHeight,z);
        }
        static VRC_SceneDescriptor desc0() { return UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>(); }
        static void Report(string name, string text) { Directory.CreateDirectory("Temp/KH_DogPort"); File.WriteAllText("Temp/KH_DogPort/"+name+".txt",text); Debug.Log("[KH Walking] " + text); }
        static void Guard()
        {
            if (Application.isPlaying || EditorSceneManager.GetActiveScene().path != WorldScene)
                throw new InvalidOperationException("Open the hillside scene in Edit mode.");
        }

        [MenuItem("Tools/KominkaHillside/Walking/1 Create Programs")]
        public static void Programs()
        {
            foreach (string name in new[]{"KH_LeashManager","KH_LeashClip","KH_LeashCollar","KH_LeashHaptics","KH_WalkRoute","KH_WalkSign","KH_TrainerPass","KH_ToyRecall","KH_HandMenu","KH_MenuOpener"})
            {
                string path="Assets/KominkaHillside/Scripts/"+name;
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path+".asset") != null) continue;
                var asset=ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript=AssetDatabase.LoadAssetAtPath<MonoScript>(path+".cs");
                AssetDatabase.CreateAsset(asset,path+".asset");
            }
            AssetDatabase.SaveAssets(); UdonSharpProgramAsset.CompileAllCsPrograms(true);
            Report("programs","Requested compile; inspect current completion and diagnostics before Build.");
        }

        [MenuItem("Tools/KominkaHillside/Walking/2 Inspect Port")]
        public static void Inspect()
        {
            Guard(); var source=EditorSceneManager.OpenScene(SourceScene,OpenSceneMode.Additive);
            try
            {
                var log=new StringBuilder();
                foreach(var root in source.GetRootGameObjects())
                    log.AppendLine(root.name+" transforms="+root.GetComponentsInChildren<Transform>(true).Length);
                foreach(var pet in source.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<KD_DogPetController>(true)))
                    log.AppendLine("PET "+pet.name+" root="+pet.dogRoot+" head="+pet.headAnchor+" animator="+pet.dogAnimator);
                Report("inspect",log.ToString());
            }
            finally {EditorSceneManager.CloseScene(source,true);}
        }

        static T Find<T>(GameObject root,string name) where T:Component
        {
            var t=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x=>x.name==name);
            if(t==null)throw new InvalidOperationException("Missing " + name);
            var c=t.GetComponent<T>(); if(c==null)throw new InvalidOperationException("Missing "+typeof(T).Name+" at "+name); return c;
        }

        [MenuItem("Tools/KominkaHillside/Walking/7 Inspect Appearance")]
        public static void Appearance()
        {
            Guard();var root=GameObject.Find("KH_Dogs");var log=new StringBuilder();
            foreach(string key in Keys)
            {
                var dog=Find<Transform>(root,"KD_Dog_"+key);var p=Find<KD_DogPetController>(root,"KD_PetSystem_"+key);
                log.AppendLine(key+" scale="+dog.lossyScale+" head="+p.headAnchor.name+" parent="+p.headAnchor.parent.name+" source="+p.sourceBone.name);
                foreach(var t in dog.GetComponentsInChildren<Transform>(true))
                    if(t.name.ToLower().Contains("neck")||t.name.ToLower().Contains("head")||t.name.ToLower().Contains("spine"))
                        log.AppendLine("BONE "+t.name+" localToDog="+dog.InverseTransformPoint(t.position)+" rotation="+t.rotation.eulerAngles+" scale="+t.lossyScale);
                foreach(var r in dog.GetComponentsInChildren<Renderer>(true))
                {
                    log.AppendLine("RENDER "+r.name+" active="+r.gameObject.activeInHierarchy+" enabled="+r.enabled+" bounds="+r.bounds);
                    if(r is SkinnedMeshRenderer sk && sk.sharedMesh!=null)
                        for(int b=0;b<sk.sharedMesh.blendShapeCount;b++)log.AppendLine("BLEND "+sk.sharedMesh.GetBlendShapeName(b)+"="+sk.GetBlendShapeWeight(b));
                    foreach(var m in r.sharedMaterials)
                        log.AppendLine("MAT "+(m==null?"NULL":AssetDatabase.GetAssetPath(m)+" shader="+(m.shader==null?"NULL":m.shader.name)+" supported="+(m.shader!=null&&m.shader.isSupported)));
                }
            }
            Report("appearance",log.ToString());
        }
        static T Add<T>(GameObject go, bool manual=false) where T:UdonSharpBehaviour
        {
            var b=go.GetComponent<T>(); if(b==null)b=go.AddUdonSharpComponent<T>();
            var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            backing.SyncMethod=manual?Networking.SyncType.Manual:Networking.SyncType.None;
            EditorUtility.SetDirty(backing); return b;
        }
        static Material Mat(string name, Color color)
        {
            string path=Data+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find("VRChat/Mobile/Standard Lite"));m.color=color;AssetDatabase.CreateAsset(m,path);}
            return m;
        }
        static Transform Child(Transform parent,string name)
        {
            var t=parent.Find(name);if(t!=null)return t;
            var go=new GameObject(name);go.transform.SetParent(parent,false);return go.transform;
        }
        static float Ground(float x,float z)
        {
            var terrain=UnityEngine.Object.FindObjectOfType<Terrain>();
            return terrain.SampleHeight(new Vector3(x,0,z))+terrain.transform.position.y;
        }

        [MenuItem("Tools/KominkaHillside/Walking/3 Build")]
        public static void Build()
        {
            Guard();var scene=EditorSceneManager.GetActiveScene();
            if(scene.isDirty)throw new InvalidOperationException("Save scene before the initial walking build.");
            Directory.CreateDirectory("Temp/KH_DogPort/BeforeWalking");
            if(!File.Exists("Temp/KH_DogPort/BeforeWalking/VRCDefaultWorldScene.unity"))File.Copy(WorldScene,"Temp/KH_DogPort/BeforeWalking/VRCDefaultWorldScene.unity");
            Directory.CreateDirectory(Data);AssetDatabase.Refresh();
            var root=GameObject.Find("KH_Dogs");bool initial=root==null;
            if(initial)
            {
                var source=EditorSceneManager.OpenScene(SourceScene,OpenSceneMode.Additive);
                var imported=source.GetRootGameObjects();
                root=new GameObject("KH_Dogs");SceneManager.MoveGameObjectToScene(root,scene);
                foreach(var go in imported){SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(root.transform,true);}
                EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(scene);
            }
            int missingRemoved=0;
            foreach(var t in root.GetComponentsInChildren<Transform>(true))
                missingRemoved+=GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            var manager=Add<KH_LeashManager>(Child(root.transform,"KH_Leashes").gameObject,true);
            manager.dogRoots=new Transform[6];manager.collarAnchors=new Transform[6];manager.fetchers=new KD_DogFetchController[6];
            manager.roamCenters=new Transform[6];manager.localClips=new KH_LeashClip[6];
            UdonSharpEditorUtility.GetBackingUdonBehaviour(manager).publicVariables.RemoveVariable("roamRadius");
            UdonSharpEditorUtility.GetBackingUdonBehaviour(manager).publicVariables.RemoveVariable("followSpeed");
            manager.leashLength=3f;manager.catchUpDistance=10f;manager.defaultDestination=KH_WalkRouteEditor.DestinationPlaza;
            // IMP-DOG-009: the upper two of the four walking tiers. Prototype values, not yet judged in Play - Build owns them.
            manager.dashSpeed=4.5f;manager.sprintSpeed=7.2f;manager.stopoverChance=.5f;
            manager.trailOffDistance=6f;manager.trailOnDistance=4f;manager.entryMaxPath=12f;manager.inviteAfter=8f;manager.inviteInterval=14f;manager.inviteLimit=3;manager.detourChance=.25f;
            var existingRoute=root.GetComponentInChildren<KH_WalkRoute>(true);manager.route=existingRoute;
            var pack=Find<KD_DogPack>(root,"KD_DogPack");
            // Gathering (game-design §4): hillside copy of KD_GroupActivities, trainer entitlement via the shared Product/Listing.
            var pass=Add<KH_TrainerPass>(Child(root.transform,"KH_TrainerPass").gameObject);
            pass.product=AssetDatabase.LoadAssetAtPath<VRC.Economy.UdonProduct>(TrainerProductPath);pass.listingId=TrainerListingId;
            // Same GameObject as the manager: one Owner adjudicates attach and gather (review D1). Remove the earlier separate object.
            var oldAct=root.transform.Find("KH_Activities");if(oldAct!=null)UnityEngine.Object.DestroyImmediate(oldAct.gameObject);
            var act=Add<KD_GroupActivities>(manager.gameObject,true);
            act.live=null;act.photos=null;act.formationItem=null;act.playItem=null;act.pack=pack;act.hillsideLeashes=manager;act.hillsidePass=pass;
            act.minGroundNormal=.5f;act.maxSlotHeightDelta=0f;act.groundMask=(1<<0)|(1<<11);act.balls=new KD_BallController[6];
            manager.activities=act;
            // IMP-DOG-007: local-only haptics beside the manager. Build owns the thresholds the same way it owns
            // the walking tiers, so a scene built earlier cannot keep values the code no longer describes.
            var haptics=Add<KH_LeashHaptics>(Child(manager.transform,"KH_LeashHaptics").gameObject);
            haptics.manager=manager;haptics.hapticsOn=true;
            haptics.pullRatio=1f;haptics.pullFullRatio=1.25f;haptics.stillSpeed=.4f;haptics.stillPullRatio=.85f;haptics.awaySpeed=.3f;
            haptics.pulseInterval=.12f;haptics.pulseDuration=.08f;haptics.tugInterval=.5f;haptics.tugDuration=.15f;
            haptics.minAmplitude=.3f;haptics.frequency=.5f;haptics.teleportJump=1f;haptics.logPulls=true;
            var recall=Add<KH_ToyRecall>(Child(root.transform,"KH_ToyRecall").gameObject);
            recall.balls=new KD_BallController[6];recall.fetchers=new KD_DogFetchController[6];recall.activities=act;recall.groundMask=(1<<0)|(1<<11);
            var rope=Mat("KH_Rope",new Color(.29f,.16f,.08f));
            var ready=Mat("KH_ClipReady",new Color(.2f,.8f,.45f));
            var neutral=Mat("KH_ClipNeutral",new Color(.8f,.63f,.27f));
            var roots=new Transform[6];var fetch=new KD_DogFetchController[6];
            for(int i=0;i<6;i++)
            {
                string key=Keys[i];var dog=Find<Transform>(root,"KD_Dog_"+key);
                var w=Find<KD_DogWanderController>(root,"KD_WanderSystem_"+key);
                var f=Find<KD_DogFetchController>(root,"KD_FetchSystem_"+key);
                var p=Find<KD_DogPetController>(root,"KD_PetSystem_"+key);
                var b=Find<KD_BallController>(root,"KD_Ball_"+key);
                if(initial){float x=44f+(i%3)*2f,z=46f+(i/3)*2f;dog.position=new Vector3(x,Ground(x,z),z);dog.rotation=Quaternion.Euler(0,0,0);}
                // Ground casts must not hit dog/interaction colliders. Only this imported subtree changes layer.
                foreach(var t in dog.GetComponentsInChildren<Transform>(true))t.gameObject.layer=14;
                var home=Child(root.transform,"KH_Roam_"+key);
                if(initial)home.position=dog.position;
                w.areaCenter=home;w.areaRadius=3f;w.groundRayOffset=1.5f;w.groundRayDistance=6f;
                // IMP-DOG-009: the run clip's playback cap has an owner here, not just a field initializer.
                w.runAnimScaleMax=KD_DogWanderController.DefaultRunAnimScaleMax;
                w.groundLayerMask=(1<<0)|(1<<11);w.hillsideLeashes=manager;w.dogIndex=i;
                f.hillsideLeashes=manager;f.dogIndex=i;f.ballController=b;b.fetchController=f;
                w.fetchController=f;w.petController=p;p.fetchController=f;p.wanderController=w;
                f.pack=pack;w.pack=pack;
                // Omitted room/stage/services stay null in this world.
                f.sleepBoard=null;f.liveSystem=null;f.activities=act;f.photoDirector=null;w.activities=act;
                act.balls[i]=b;recall.balls[i]=b;recall.fetchers[i]=f;
                p.companionSlot=null;
                manager.dogRoots[i]=dog;manager.fetchers[i]=f;manager.roamCenters[i]=home;
                roots[i]=dog;fetch[i]=f;
                // Design-owned start position (BUG-DOG-001); rewritten on every Build.
                b.transform.position=BallHome(i);b.transform.rotation=Quaternion.identity;
                Transform neck=p.headAnchor != null?p.headAnchor.parent:dog;
                var existingCollar=dog.GetComponentsInChildren<KH_LeashCollar>(true).FirstOrDefault(x=>x.dogIndex==i);
                var collar=existingCollar!=null?existingCollar.transform:Child(neck,"KH_Collar_"+key);
                var anchor=Child(collar,"ClipAnchor");
                var visual=collar.Find("Band");
                if(visual==null || visual.GetComponent<MeshFilter>().sharedMesh.name=="Cylinder")
                {
                    KH_LeashModelsEditor.FitCollar(collar,anchor,dog,i,Data,Mat("KH_Collar_"+key,Color.HSVToRGB(i/6f,.65f,.65f)),neutral);
                }
                var hit=collar.GetComponent<SphereCollider>();if(hit==null)hit=collar.gameObject.AddComponent<SphereCollider>();hit.center=anchor.localPosition;hit.radius=.10f;hit.isTrigger=true;
                collar.gameObject.layer=14;
                var c=Add<KH_LeashCollar>(collar.gameObject);c.manager=manager;c.dogIndex=i;c.anchor=anchor;
                var line=c.line;if(line==null){var go=Child(root.transform,"KH_Lead_"+key).gameObject;line=go.GetComponent<LineRenderer>();if(line==null)line=go.AddComponent<LineRenderer>();}
                line.sharedMaterial=rope;line.positionCount=12;line.startWidth=.012f;line.endWidth=.012f;line.useWorldSpace=true;line.enabled=false;
                c.line=line;manager.collarAnchors[i]=anchor;
                UdonSharpEditorUtility.GetBackingUdonBehaviour(c).InteractionText="Release leash";
            }
            // IMP-DOG-005: one disc + catcher per breed in FrisbeeKeys, on the same dog that walks and fetches.
            manager.frisbeeCatchers=new KH_FrisbeeCatch[6];act.frisbeeCatchers=new KH_FrisbeeCatch[6];recall.discs=new KH_Frisbee[6];recall.discCatchers=new KH_FrisbeeCatch[6];
            var facts=KH_FrisbeePrototypeEditor.ReadFacts();var twistFacts=KH_FrisbeePrototypeEditor.ReadFacts("Twist"); // IMP-DOG-008 v4: both clip variants
            for(int i=0;i<6;i++)
            {
                string key=Keys[i];var w=Find<KD_DogWanderController>(root,"KD_WanderSystem_"+key);var f=Find<KD_DogFetchController>(root,"KD_FetchSystem_"+key);var p=Find<KD_DogPetController>(root,"KD_PetSystem_"+key);
                if(Array.IndexOf(FrisbeeKeys,key)<0){w.frisbeeCatch=null;f.frisbeeCatch=null;continue;}
                // The jump clip, its pose facts and the Animator Jump state exist for one breed so far; adding a key
                // here without authoring them (Frisbee/2 for that breed) must fail loudly, not copy Lab's mouth.
                if(key!=KH_FrisbeeJumpClipEditor.Breed)throw new InvalidOperationException("No jump clip / pose facts authored for "+key+" (KH_FrisbeeJumpClipEditor.Breed is "+KH_FrisbeeJumpClipEditor.Breed+").");
                bool created;var disc=KH_FrisbeePrototypeEditor.BuildDisc(root.transform,"KH_Frisbee_"+key,"KH_FrisbeeDisc_"+key,DiscColors[i],out created);
                foreach(var t in disc.GetComponentsInChildren<Transform>(true))t.gameObject.layer=13;
                if(created){disc.transform.position=DiscHome(i);disc.transform.rotation=Quaternion.identity;} // human-owned once placed (review M4)
                var c=Add<KH_FrisbeeCatch>(Child(root.transform,"KH_FrisbeeCatch_"+key).gameObject,true);
                KH_FrisbeePrototypeEditor.ConfigureCatcher(c,disc,roots[i],f.dogAnimator,f.mouthAnchor,facts,twistFacts);
                // IMP-DOG-008 wave 2: the purchased body hangs off the 追従GIMMICK ParentConstraint and trails the
                // root by up to 30 deg / 0.5 m while the dog turns and jumps, so the catch plan (root + rotation *
                // mouthOffsetCatch) misses. Only the frisbee dogs are made rigid; the other five keep the gimmick.
                KH_FrisbeeJumpClipEditor.MakeBodyRigid(roots[i].gameObject);
                c.hillsideLeashes=manager;c.fetch=f;c.wander=w;c.activities=act;c.pet=p;c.dogIndex=i;
                c.playAreaCenter=new Vector3(KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,0],KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,2],KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,1]);
                c.playAreaRadius=KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,3];
                w.frisbeeCatch=c;f.frisbeeCatch=c;manager.frisbeeCatchers[i]=c;act.frisbeeCatchers[i]=c;recall.discs[i]=disc;recall.discCatchers[i]=c;
            }
            pack.dogRoots=roots;pack.dogFetch=fetch;
            // Leash stations (game-design section 4): one rack per destination, a left and a right slot each.
            // Station placement is human-owned (written once); slots and tools are code-owned.
            var wood=Mat("KH_RackWood",new Color(.32f,.19f,.10f));
            var rack=Child(root.transform,"KH_LeashRack");
            if(initial)rack.position=new Vector3(45,Ground(45,43)+.8f,43);
            var stations=new Transform[]{rack,Child(root.transform,"KH_Station_Viewpoint"),Child(root.transform,"KH_Station_Plaza")};
            // Initial placement comes from the terrain places (same offsets as the signs), so build order does not matter.
            var placesRoot=GameObject.Find("KH_World");
            if(placesRoot==null)throw new InvalidOperationException("KH_World (terrain places) is required to place stations.");
            Vector3[] placeOffset={Vector3.zero,new Vector3(2.4f,0f,1.2f),new Vector3(-3f,0f,-2.5f)};float[] placeYaw={0f,45f,315f};string[] placeName={"","KH_Places/KH_Viewpoint","KH_Places/KH_Plaza"};
            for(int s=1;s<3;s++)
            {
                if(stations[s].Find("Crossbar")!=null)continue; // human-owned placement already exists
                var place=placesRoot.transform.Find(placeName[s]);if(place==null)throw new InvalidOperationException("Missing "+placeName[s]);
                var rot=Quaternion.Euler(0f,placeYaw[s],0f);Vector3 signPos=place.position+placeOffset[s];
                Vector3 p=signPos+rot*new Vector3(1.3f,0f,0f);stations[s].rotation=rot;stations[s].position=new Vector3(p.x,Ground(p.x,p.z)+.8f,p.z);
            }
            manager.stationSlots=new Transform[6];manager.slotHand=new int[6];manager.stationOf=new int[6];
            for(int s=0;s<3;s++)
            {
                KH_LeashModelsEditor.MakeRack(stations[s],wood);
                for(int h=0;h<2;h++){var slot=Child(stations[s],h==0?"SlotL":"SlotR");slot.localPosition=new Vector3(h==0?-.2f:.2f,0f,0f);slot.localRotation=Quaternion.identity;
                    int k=h*3+s;manager.stationSlots[k]=slot;manager.slotHand[k]=h;manager.stationOf[k]=s;}
            }
            var tools=Child(root.transform,"KH_LeashTools");
            for(int i=0;i<6;i++)
            {
                var t=tools.Find("Clip"+i);if(t==null)t=rack.Find("Clip"+i);bool newClip=t==null;
                if(newClip){var go=GameObject.CreatePrimitive(PrimitiveType.Capsule);go.name="Clip"+i;t=go.transform;t.localScale=new Vector3(.07f,.12f,.07f);}
                t.SetParent(tools,true);
                // Tool i rests at slot i: hand = i/3 (0 left, 1 right), station = i%3.
                var home=manager.stationSlots[(i/3)*3+(i%3)];t.SetPositionAndRotation(home.position,home.rotation);
                t.gameObject.layer=14;
                var rb=t.GetComponent<Rigidbody>();if(rb==null)rb=t.gameObject.AddComponent<Rigidbody>();rb.isKinematic=true;rb.useGravity=false;
                var pickup=t.GetComponent<VRC_Pickup>();if(pickup==null)pickup=t.gameObject.AddComponent<VRCPickup>();
                pickup.AutoHold=VRC_Pickup.AutoHoldMode.Yes;pickup.proximity=.35f;pickup.InteractionText=i<3?"Leash tip - left":"Leash tip - right";
                var clip=Add<KH_LeashClip>(t.gameObject);clip.manager=manager;clip.pickup=pickup;clip.tip=KH_LeashModelsEditor.MakeTip(t,Data,neutral);clip.desktopHand=i/3;
                var indicator=t.Find("ReadyIndicator");
                clip.indicator=indicator!=null?indicator.GetComponent<Renderer>():t.GetComponent<Renderer>();
                clip.readyMaterial=ready;clip.blockedMaterial=neutral;clip.indicator.sharedMaterial=neutral;
                manager.localClips[i]=clip;
            }
            foreach(var u in root.GetComponentsInChildren<UdonSharpBehaviour>(true)){UdonSharpEditorUtility.CopyProxyToUdon(u);EditorUtility.SetDirty(u);}
            // Removed temporary Udon objects leave {fileID: 0} entries in the descriptor (vrc-scene-edit-safety). Drop only typeless ones.
            var descriptor=UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
            if(descriptor!=null){int dropped=descriptor.NetworkIDCollection.RemoveAll(x=>x.gameObject==null&&(x.SerializedTypeNames==null||x.SerializedTypeNames.Count==0));if(dropped>0)EditorUtility.SetDirty(descriptor);}
            // The dog system is outside KH_World, so terrain regeneration does not erase it.
            var terrainRoot=GameObject.Find("KH_World");
            if(terrainRoot!=null){var dummies=terrainRoot.transform.Find("KH_Dummies");if(dummies!=null)dummies.gameObject.SetActive(false);}
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();
            Report("build","initial="+initial+" dogs=6 clips=6 removedMissing="+missingRemoved+"; NavMesh and independent Verify still required.");
        }

        [MenuItem("Tools/KominkaHillside/Walking/8 Refit Collars")]
        public static void RefitCollars()
        {
            Guard();var root=GameObject.Find("KH_Dogs");
            for(int i=0;i<6;i++)
            {
                var dog=Find<Transform>(root,"KD_Dog_"+Keys[i]);var c=root.GetComponentsInChildren<KH_LeashCollar>(true).First(x=>x.dogIndex==i);
                KH_LeashModelsEditor.FitCollar(c.transform,c.anchor,dog,i,Data,c.transform.Find("Band").GetComponent<Renderer>().sharedMaterial,AssetDatabase.LoadAssetAtPath<Material>(Data+"/KH_ClipNeutral.mat"));
                c.GetComponent<SphereCollider>().center=c.anchor.localPosition;
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();
            Report("refit","Six collar profiles applied. Build preserves subsequent manual fitting.");
        }

        [MenuItem("Tools/KominkaHillside/Walking/4 Bake Navigation")]
        public static void Navigation()
        {
            Guard();var root=GameObject.Find("KH_Dogs");if(root==null)throw new InvalidOperationException("Build first");
            var go=Child(root.transform,"KH_Navigation").gameObject;var surface=go.GetComponent<NavMeshSurface>();if(surface==null)surface=go.AddComponent<NavMeshSurface>();
            surface.collectObjects=CollectObjects.Volume;surface.center=new Vector3(60,25,90);surface.size=new Vector3(120,70,180);
            surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;surface.layerMask=(1<<0)|(1<<11);
            surface.overrideVoxelSize=true;surface.voxelSize=.15f;surface.BuildNavMesh();
            var old=AssetDatabase.LoadAssetAtPath<NavMeshData>(Data+"/KH_WalkingNavMesh.asset");
            if(old==null)AssetDatabase.CreateAsset(surface.navMeshData,Data+"/KH_WalkingNavMesh.asset");
            else{var fresh=surface.navMeshData;surface.RemoveData();EditorUtility.CopySerialized(fresh,old);surface.navMeshData=old;surface.AddData();EditorUtility.SetDirty(old);}
            EditorUtility.SetDirty(surface);EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();
            Report("navigation","vertices="+NavMesh.CalculateTriangulation().vertices.Length);
        }

        [MenuItem("Tools/KominkaHillside/Walking/5 Verify")]
        public static void Verify()
        {
            Guard();var log=new StringBuilder();int total=0,failed=0;
            void Check(bool ok,string what){total++;if(!ok)failed++;log.AppendLine((ok?"PASS ":"FAIL ")+what);}
            var root=GameObject.Find("KH_Dogs");Check(root!=null,"dog root exists outside terrain builder");
            if(root==null){Report("verify",log.ToString());return;}
            var m=root.GetComponentInChildren<KH_LeashManager>();Check(m!=null,"manager exists");
            Check(root.GetComponentsInChildren<KH_LeashClip>(true).Length==6,"six local pickup tips");
            Check(root.GetComponentsInChildren<KH_LeashCollar>(true).Length==6,"six collar interactions");
            Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(m).SyncMethod==Networking.SyncType.Manual,"manager backing sync Manual");
            // IMP-DOG-009: the walking tiers Build writes. These only pin the prototype numbers against drift -
            // a PASS here says the scene matches the code, never that Play has judged the pace.
            Check(m.dashSpeed==4.5f,"dash speed "+m.dashSpeed.ToString("F1")+" == 4.5 m/s");
            Check(m.sprintSpeed==7.2f,"sprint speed "+m.sprintSpeed.ToString("F1")+" == 7.2 m/s");
            Check(m.stopoverChance==.5f,"stopover chance "+m.stopoverChance.ToString("F2")+" == 0.50");
            // IMP-DOG-010 B: the dogs read the spots through the manager, so an unwired list is a silent
            // "no stopovers ever". Walking/4e checks the spots themselves (placement, paths, spacing).
            Check(m.stopovers!=null,"stopover data wired to manager");
            Check(m.stopovers!=null&&m.stopovers.count>=1,"stopover spots "+(m.stopovers==null?0:m.stopovers.count)+" >= 1");
            var pack=root.GetComponentInChildren<KD_DogPack>();
            for(int i=0;i<6;i++)
            {
                string key=Keys[i];var dog=Find<Transform>(root,"KD_Dog_"+key);var w=Find<KD_DogWanderController>(root,"KD_WanderSystem_"+key);var p=Find<KD_DogPetController>(root,"KD_PetSystem_"+key);var f=Find<KD_DogFetchController>(root,"KD_FetchSystem_"+key);
                Check(m.dogRoots[i]==dog && w.hillsideLeashes==m && f.hillsideLeashes==m,"leash wiring "+key);
                // Four distinct tiers, in order. Two live on the dog's legs, two on the walk (IMP-DOG-009).
                Check(w.walkSpeed<w.runSpeed&&w.runSpeed<m.dashSpeed&&m.dashSpeed<m.sprintSpeed,"speed tiers ordered "+key+" "+w.walkSpeed.ToString("F1")+" < "+w.runSpeed.ToString("F1")+" < "+m.dashSpeed.ToString("F1")+" < "+m.sprintSpeed.ToString("F1"));
                Check(w.runAnimScaleMax==2.5f,"run clip speed cap "+w.runAnimScaleMax.ToString("F1")+"x == 2.5 "+key);
                Check(p.headAnchor!=null&&p.sourceBone!=null&&p.dogAnimator!=null,"original pet rig "+key);
                Check(f.ballController!=null&&f.mouthAnchor!=null&&f.dogRoot==dog,"Fetch wiring "+key);
                Check(dog.GetComponent<VRCObjectSync>()!=null,"ObjectSync "+key);
                Check(dog.GetComponentsInChildren<Renderer>(true).Any(),"model renderers "+key);
                Check(dog.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).All(mat=>mat!=null&&mat.shader!=null&&mat.shader.isSupported&&mat.shader.name!="Hidden/InternalErrorShader"),"supported materials "+key);
                Check(pack.dogWander.Length==6&&pack.dogWander[i]==w&&pack.dogFetch[i]==f&&pack.dogRoots[i]==dog,"pack roster "+key);
                var clip=m.localClips[i];Check(clip!=null&&clip.manager==m&&clip.pickup!=null&&clip.tip!=null&&clip.desktopHand==(i<3?0:1),"local clip wiring "+key);
                Check(m.collarAnchors[i]!=null&&m.collarAnchors[i].IsChildOf(dog),"collar follows skeleton "+key);
                NavMeshHit hit;Check(NavMesh.SamplePosition(dog.position,out hit,1f,NavMesh.AllAreas),"dog on navigation "+key);
                NavMeshHit destination;var path=new NavMeshPath();
                bool route=NavMesh.SamplePosition(new Vector3(72,19,127),out destination,2f,NavMesh.AllAreas)
                    && NavMesh.CalculatePath(dog.position,destination.position,NavMesh.AllAreas,path) && path.status==NavMeshPathStatus.PathComplete;
                Check(route,"complete route to upper clearing "+key);
            }
            for(int i=0;i<6;i++)
            {
                var ball=Find<Transform>(root,"KD_Ball_"+Keys[i]);Vector3 home=BallHome(i);
                float plaza=Vector2.Distance(new Vector2(ball.position.x,ball.position.z),new Vector2(KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,0],KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,1]));
                float ground=Ground(ball.position.x,ball.position.z);
                Check(Vector3.Distance(ball.position,home)<.01f&&plaza<KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,3]&&Mathf.Abs(ground-KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,2])<.02f,"ball on flat clearing "+Keys[i]+" pos="+ball.position.ToString("F2")+" ground="+ground.ToString("F2"));
                NavMeshHit ballHit;Check(NavMesh.SamplePosition(ball.position,out ballHit,1f,NavMesh.AllAreas),"ball reachable on navigation "+Keys[i]);
            }
            var walkRoute=root.GetComponentInChildren<KH_WalkRoute>(true);Check(walkRoute!=null&&m.route==walkRoute,"walk route wired to manager");
            if(walkRoute!=null)
            {
                int n=walkRoute.count;Check(n>=8&&walkRoute.nodes!=null&&walkRoute.nodes.Length==n&&walkRoute.nextHop.Length==n*n&&walkRoute.routeCost.Length==n*n&&walkRoute.arriveRadius.Length==n,"route arrays sized n="+n);
                Check(walkRoute.destinationNodes!=null&&walkRoute.destinationNodes.Length==2&&walkRoute.destinationNodes[0]==0&&walkRoute.destinationNodes[1]==n-1,"route destinations gate=0 plaza=last");
                bool chain=true;int at=0,steps=0;while(at!=n-1&&steps<n+1){int nx=walkRoute._Next(at,n-1);if(nx!=at+1){chain=false;break;}at=nx;steps++;}
                Check(chain&&at==n-1,"nextHop walks gate->plaza in "+steps+" hops");
                chain=true;at=n-1;steps=0;while(at!=0&&steps<n+1){int nx=walkRoute._Next(at,0);if(nx!=at-1){chain=false;break;}at=nx;steps++;}
                Check(chain&&at==0,"nextHop walks plaza->gate in "+steps+" hops");
                bool onNav=true,spaced=true;for(int i=0;i<n;i++){NavMeshHit h;if(!NavMesh.SamplePosition(walkRoute.nodes[i].position,out h,.5f,NavMesh.AllAreas))onNav=false;if(i>0&&Vector3.Distance(walkRoute.nodes[i-1].position,walkRoute.nodes[i].position)>KH_WalkRouteEditor.NodeSpacing*1.6f)spaced=false;}
                Check(onNav,"all route nodes on navigation");
                bool linksComplete=walkRoute.linkA.Length==n-1;var linkPath=new NavMeshPath();
                for(int i=0;i<walkRoute.linkA.Length;i++){NavMeshHit a,b;if(!NavMesh.SamplePosition(walkRoute.nodes[walkRoute.linkA[i]].position,out a,.5f,NavMesh.AllAreas)||!NavMesh.SamplePosition(walkRoute.nodes[walkRoute.linkB[i]].position,out b,.5f,NavMesh.AllAreas)||!NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,linkPath)||linkPath.status!=NavMeshPathStatus.PathComplete)linksComplete=false;}
                Check(linksComplete,"every route link has a complete NavMesh path (links="+walkRoute.linkA.Length+")");Check(spaced,"route node spacing <= "+(KH_WalkRouteEditor.NodeSpacing*1.6f)+"m");
                Check(Vector3.Distance(walkRoute.nodes[n-1].position,new Vector3(72,19,127))<1f&&Vector3.Distance(walkRoute.nodes[0].position,new Vector3(47.6f,walkRoute.nodes[0].position.y,40.1f))<1.5f,"route ends at gate and plaza centre");
                var rb=UdonSharpEditorUtility.GetBackingUdonBehaviour(walkRoute);object rv;var rn=rb.publicVariables.TryGetVariableValue("nodes",out rv)?rv as Array:null;
                Check(rn!=null&&rn.Length==n&&(rn.GetValue(0) as Transform)==walkRoute.nodes[0],"route backing nodes entries="+(rn==null?0:rn.Length));
                var mb=UdonSharpEditorUtility.GetBackingUdonBehaviour(m);object mr;Check(mb.publicVariables.TryGetVariableValue("route",out mr)&&(mr as UnityEngine.Object)==UdonSharpEditorUtility.GetBackingUdonBehaviour(walkRoute),"manager backing route reference");
            }
            var act=root.GetComponentInChildren<KD_GroupActivities>(true);Check(act!=null&&m.activities==act&&UdonSharpEditorUtility.GetBackingUdonBehaviour(act).SyncMethod==Networking.SyncType.Manual,"gather authority exists, Manual, wired to manager");
            if(act!=null)
            {
                Check(act.pack==pack&&act.hillsideLeashes==m&&act.balls!=null&&act.balls.Length==6&&act.balls.All(x=>x!=null)&&act.live==null&&act.minGroundNormal<=.5f&&act.maxSlotHeightDelta==0f,"gather wiring: pack, leashes, 6 balls, no live, slope tolerant");
                var passB=root.GetComponentInChildren<KH_TrainerPass>(true);
                Check(passB!=null&&act.hillsidePass==passB&&passB.product!=null&&passB.product.ID==TrainerProductId&&passB.listingId==TrainerListingId,"trainer pass: shared Product/Listing IDs");
                bool actRefs=true;for(int i=0;i<6;i++){var w=Find<KD_DogWanderController>(root,"KD_WanderSystem_"+Keys[i]);var f=Find<KD_DogFetchController>(root,"KD_FetchSystem_"+Keys[i]);if(w.activities!=act||f.activities!=act)actRefs=false;}
                Check(actRefs,"all six Wander/Fetch reference the gather authority");
            }
            // IMP-DOG-005: disc + catcher on the walking dog (stage 2: FrisbeeKeys only), others deliberately unwired.
            var facts=KH_FrisbeePrototypeEditor.ReadFacts();var clipAsset=AssetDatabase.LoadAssetAtPath<AnimationClip>(KH_FrisbeeJumpClipEditor.ClipPath);
            // IMP-DOG-008 v4: the twist clip is the second member of the family and is checked like the jump.
            var twistFacts=KH_FrisbeePrototypeEditor.ReadFacts("Twist");var twistClip=AssetDatabase.LoadAssetAtPath<AnimationClip>(KH_FrisbeeJumpClipEditor.ClipPathFor("Twist"));
            var twistVariant=KH_FrisbeeJumpClipEditor.Variants().FirstOrDefault(v=>v.name=="Twist");
            Check(twistClip!=null&&Mathf.Abs(twistClip.length-twistVariant.EndAt)<0.02f,"twist clip exists, length "+(twistClip==null?"-":twistClip.length.ToString("F3"))+" == "+twistVariant.EndAt.ToString("F3"));
            Check(KH_FrisbeeCatch.CurveDefault>0f&&KH_FrisbeeCatch.CurveDefault<=0.5f,"flight curve default "+KH_FrisbeeCatch.CurveDefault.ToString("F2")+" in (0, 0.5] 1/m");
            for(int i=0;i<6;i++)
            {
                string key=Keys[i];bool wanted=Array.IndexOf(FrisbeeKeys,key)>=0;var w=Find<KD_DogWanderController>(root,"KD_WanderSystem_"+key);var f=Find<KD_DogFetchController>(root,"KD_FetchSystem_"+key);var p=Find<KD_DogPetController>(root,"KD_PetSystem_"+key);
                var ct=root.transform.Find("KH_FrisbeeCatch_"+key);var dt=root.transform.Find("KH_Frisbee_"+key);
                // IMP-DOG-008 wave 2: only the frisbee dogs lose the 追従GIMMICK body-follow ParentConstraint, so the
                // change stays scoped - the other five must still have it (their body trails the root as bought).
                if(!wanted){Check(ct==null&&dt==null&&w.frisbeeCatch==null&&f.frisbeeCatch==null&&(m.frisbeeCatchers==null||m.frisbeeCatchers[i]==null)&&(act.frisbeeCatchers==null||act.frisbeeCatchers[i]==null),"no disc for "+key+" yet (deliberate)");
                    Check(m.dogRoots[i]!=null&&!KH_FrisbeeJumpClipEditor.IsBodyRigid(m.dogRoots[i].gameObject),"non-frisbee dog "+key+" keeps the purchased body-follow constraint");continue;}
                Check(m.dogRoots[i]!=null&&KH_FrisbeeJumpClipEditor.IsBodyRigid(m.dogRoots[i].gameObject),"frisbee dog body is rigid to its root (追従 ParentConstraint inactive) "+key);
                // Codex D2: the change is scoped to that one constraint - anything else under the dog still runs.
                Check(m.dogRoots[i]!=null&&KH_FrisbeeJumpClipEditor.OtherConstraintsUntouched(m.dogRoots[i].gameObject),"every other ParentConstraint under "+key+" is still active (scoped to "+KH_FrisbeeJumpClipEditor.BodyFollowPath+")");
                var c=ct==null?null:ct.GetComponent<KH_FrisbeeCatch>();var d=dt==null?null:dt.GetComponent<KH_Frisbee>();
                Check(c!=null&&d!=null,"disc and catcher exist for "+key);if(c==null||d==null)continue;
                Check(c.dogRoot==m.dogRoots[i]&&c.dogAnimator==f.dogAnimator&&c.mouthAnchor==f.mouthAnchor&&c.frisbee==d&&d.catcher==c,"catcher shares the walking dog's root/Animator/mouth "+key);
                Check(c.hillsideLeashes==m&&c.fetch==f&&c.wander==w&&c.activities==act&&c.pet==p&&c.dogIndex==i,"catcher neighbours "+key);
                Check(w.frisbeeCatch==c&&f.frisbeeCatch==c&&m.frisbeeCatchers[i]==c&&act.frisbeeCatchers[i]==c,"Wander/Fetch/Manager/Gather see the catcher "+key);
                Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(c).SyncMethod==Networking.SyncType.Manual&&UdonSharpEditorUtility.GetBackingUdonBehaviour(d).SyncMethod==Networking.SyncType.None,"catcher Manual / disc None "+key);
                // Both clips play from the same minRootRise, so the floor follows the longer rise time (Codex P).
                float longestRise=Mathf.Max(facts.rise,twistFacts.rise);
                Check(Vector3.Distance(c.mouthOffsetCatch,facts.mouthOffsetCatch)<1e-4f&&Mathf.Abs(c.clipRiseDuration-facts.rise)<1e-4f&&c.minRootRise>=0.5f*d.gravity*Mathf.Pow(longestRise/1.8f,2f)-1e-3f,"pose facts match the clip JSON, rise floor from the longest rise "+longestRise.ToString("F2")+" s "+key);
                Check(Vector3.Distance(c.mouthOffsetCatchTwist,twistFacts.mouthOffsetCatch)<1e-4f&&Mathf.Abs(c.clipCrouchDurationTwist-twistFacts.crouch)<1e-4f&&Mathf.Abs(c.clipRiseDurationTwist-twistFacts.rise)<1e-4f&&Mathf.Abs(c.clipRecoverDurationTwist-twistFacts.recover)<1e-4f,"twist pose facts match the twist clip JSON "+key);
                var ac=f.dogAnimator==null?null:f.dogAnimator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                Check(ac!=null&&ac.parameters.Any(x=>x.name=="Jump")&&ac.layers[0].stateMachine.states.Any(st=>st.state.name=="Jump"&&st.state.motion==clipAsset),"Animator has Jump with the clip "+key);
                var twistState=ac==null?null:ac.layers[0].stateMachine.states.Select(st=>st.state).FirstOrDefault(st=>st.name=="Twist");
                Check(ac!=null&&ac.parameters.Any(x=>x.name=="Twist"&&x.type==AnimatorControllerParameterType.Bool)&&twistState!=null&&twistState.motion==twistClip,"Animator has Bool Twist with the twist clip "+key);
                Check(ac!=null&&twistState!=null&&twistState.transitions.Length==3&&ac.layers[0].stateMachine.anyStateTransitions.Any(t=>t.destinationState==twistState&&!t.canTransitionToSelf),"Twist state: AnyState entry and 3 exits "+key);
                float plaza=Vector2.Distance(new Vector2(d.transform.position.x,d.transform.position.z),new Vector2(KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,0],KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,1]));
                Check(plaza<KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,3]&&Mathf.Abs(Ground(d.transform.position.x,d.transform.position.z)-KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,2])<.05f,"disc rests on the flat clearing "+key);
                Check(Mathf.Abs(c.playAreaRadius-KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,3])<1e-3f&&Vector2.Distance(new Vector2(c.playAreaCenter.x,c.playAreaCenter.z),new Vector2(KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,0],KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,1]))<1e-3f,"play area = plaza flat "+key);
                Check(d.GetComponent<VRCObjectSync>()!=null&&d.GetComponent<VRCPickup>()!=null&&d.aimLine!=null&&d.aimMarker!=null&&d.discHeldMaterial!=null&&d.discRenderer!=null&&Mathf.Abs(d.respawnBelowY-desc0().RespawnHeightY)<1e-3f,"disc components, aim preview, held material, respawn height "+key);
                var cb=UdonSharpEditorUtility.GetBackingUdonBehaviour(c);object bv;
                Check(cb.publicVariables.TryGetVariableValue("dogRoot",out bv)&&(bv as Transform)==c.dogRoot,"catcher backing dogRoot "+key);
                bool factsOk=cb.publicVariables.TryGetVariableValue("mouthOffsetCatch",out bv)&&(Vector3)bv==c.mouthOffsetCatch
                    &&cb.publicVariables.TryGetVariableValue("mouthOffsetStand",out bv)&&(Vector3)bv==c.mouthOffsetStand
                    &&cb.publicVariables.TryGetVariableValue("clipCrouchDuration",out bv)&&(float)bv==c.clipCrouchDuration
                    &&cb.publicVariables.TryGetVariableValue("clipRiseDuration",out bv)&&(float)bv==c.clipRiseDuration
                    &&cb.publicVariables.TryGetVariableValue("clipRecoverDuration",out bv)&&(float)bv==c.clipRecoverDuration
                    &&cb.publicVariables.TryGetVariableValue("playAreaRadius",out bv)&&(float)bv==c.playAreaRadius
                    &&cb.publicVariables.TryGetVariableValue("playAreaCenter",out bv)&&(Vector3)bv==c.playAreaCenter;
                Check(factsOk,"catcher backing: all pose facts + play area "+key);
                bool twistOk=cb.publicVariables.TryGetVariableValue("mouthOffsetCatchTwist",out bv)&&(Vector3)bv==c.mouthOffsetCatchTwist
                    &&cb.publicVariables.TryGetVariableValue("clipCrouchDurationTwist",out bv)&&(float)bv==c.clipCrouchDurationTwist
                    &&cb.publicVariables.TryGetVariableValue("clipRiseDurationTwist",out bv)&&(float)bv==c.clipRiseDurationTwist
                    &&cb.publicVariables.TryGetVariableValue("clipRecoverDurationTwist",out bv)&&(float)bv==c.clipRecoverDurationTwist;
                Check(twistOk,"catcher backing: twist pose facts "+key);
                bool refsOk=true;foreach(var pair in new[]{new object[]{"hillsideLeashes",m},new object[]{"fetch",f},new object[]{"wander",w},new object[]{"activities",act},new object[]{"pet",p},new object[]{"frisbee",d}})
                {
                    var expect=UdonSharpEditorUtility.GetBackingUdonBehaviour((UdonSharpBehaviour)pair[1]);
                    if(!(cb.publicVariables.TryGetVariableValue((string)pair[0],out bv)&&(bv as UnityEngine.Object)==expect))refsOk=false;
                }
                Check(refsOk&&cb.publicVariables.TryGetVariableValue("dogIndex",out bv)&&(int)bv==i,"catcher backing: neighbour references and dogIndex "+key);
                // IMP-DOG-008: the prototype switch starts on the baseline and the run-in presets fit this dog, this disc and the plaza.
                Check(c.variant==KH_FrisbeeCatch.VariantNormal&&c.pendingVariant==-1&&cb.publicVariables.TryGetVariableValue("variant",out bv)&&(int)bv==KH_FrisbeeCatch.VariantNormal&&cb.publicVariables.TryGetVariableValue("pendingVariant",out bv)&&(int)bv==-1,"variant starts at Normal, proxy and backing, nothing queued (backing too) "+key);
                // Round 2 (intercept-first): the plan promises the dog will be STANDING under the catch point,
                // settled, before the crouch. Only a sprint keeps that promise.
                Check(c.chaseRunSpeed>=8f,"chase sprint "+c.chaseRunSpeed.ToString("F1")+" m/s >= 8 (the ETA the planner promises) "+key);
                // Round 4: bands, not floors - too low and the ETA fails, too high and the dog reads as a machine.
                Check(c.chaseAcceleration>=8f&&c.chaseAcceleration<=15f,"chase acceleration "+c.chaseAcceleration.ToString("F1")+" m/s2 in [8, 15] (reached, but visibly) "+key);
                Check(c.settleTime>=0.1f&&c.settleTime<=0.6f,"settle time "+c.settleTime.ToString("F2")+" s in [0.10, 0.60] "+key);
                Check(c.turnSpeedDegPerSec>=180f&&c.turnSpeedDegPerSec<=360f,"turn rate "+c.turnSpeedDegPerSec.ToString("F0")+" deg/s in [180, 360] (a dog's turn, not a turret's) "+key);
                Check(KH_FrisbeeCatch.HookLength>=1f&&KH_FrisbeeCatch.HookLength<=3f,"hook length "+KH_FrisbeeCatch.HookLength.ToString("F2")+" m in [1, 3] "+key);
                Check(KH_FrisbeeCatch.HookAngleDeg>=20f&&KH_FrisbeeCatch.HookAngleDeg<=70f,"hook angle "+KH_FrisbeeCatch.HookAngleDeg.ToString("F0")+" deg in [20, 70] "+key);
                // Codex M5: the momentum grid (HookLenAt / HookAngAt), not just the wait hook.
                Check(KH_FrisbeeCatch.HookLengthMin>=0.5f&&KH_FrisbeeCatch.HookLengthMin<=KH_FrisbeeCatch.HookLength&&KH_FrisbeeCatch.HookLengthMax>=KH_FrisbeeCatch.HookLength&&KH_FrisbeeCatch.HookLengthMax<=6f,
                    "hook loop lengths "+KH_FrisbeeCatch.HookLengthMin.ToString("F2")+" - "+KH_FrisbeeCatch.HookLengthMax.ToString("F2")+" m bracket "+KH_FrisbeeCatch.HookLength.ToString("F2")+", within [0.5, 6] "+key);
                Check(KH_FrisbeeCatch.HookAngleMin>=10f&&KH_FrisbeeCatch.HookAngleMin<=KH_FrisbeeCatch.HookAngleDeg&&KH_FrisbeeCatch.HookAngleMax>=KH_FrisbeeCatch.HookAngleDeg&&KH_FrisbeeCatch.HookAngleMax<=120f,
                    "hook loop angles "+KH_FrisbeeCatch.HookAngleMin.ToString("F0")+" - "+KH_FrisbeeCatch.HookAngleMax.ToString("F0")+" deg bracket "+KH_FrisbeeCatch.HookAngleDeg.ToString("F0")+", within [10, 120] "+key);
                Check(KH_FrisbeeCatch.HookCombos>=2&&KH_FrisbeeCatch.HookCombos<=16,"hook loop count "+KH_FrisbeeCatch.HookCombos+" in [2, 16] "+key);
                Check(c.chaseDeceleration>0f&&c.chaseDeceleration<c.deceleration,"chase braking "+c.chaseDeceleration.ToString("F1")+" m/s2 < the generic "+c.deceleration.ToString("F1")+" "+key);
                Check(c.standTurnDegPerSec<=c.walkTurnDegPerSec&&c.walkTurnDegPerSec<=c.turnSpeedDegPerSec&&c.standTurnDegPerSec>c.idleTurnSpeedDegPerSec,"turn rates stand "+c.standTurnDegPerSec.ToString("F0")+" <= walk "+c.walkTurnDegPerSec.ToString("F0")+" <= run "+c.turnSpeedDegPerSec.ToString("F0")+", all above idle "+key);
                Check(c.maxRunAnimScale>=1.5f&&c.maxRunAnimScale<=3f,"run clip speed cap "+c.maxRunAnimScale.ToString("F1")+"x in [1.5, 3.0] (higher reads as jitter) "+key);
                Check(KH_FrisbeeCatch.HoldEnterRadius>=0.2f&&KH_FrisbeeCatch.HoldEnterRadius<=0.6f,"hold band "+KH_FrisbeeCatch.HoldEnterRadius.ToString("F2")+" m in [0.20, 0.60] (must sit outside the 0.1 m arrive radius) "+key);
                Check(c.returnRunSpeed<=c.chaseRunSpeed,"return pace "+c.returnRunSpeed.ToString("F1")+" <= chase sprint "+c.chaseRunSpeed.ToString("F1")+" (only the chase has a deadline) "+key);
                // The highest jump the planner may pick still has to play the clip at a believable speed.
                // Codex M5: the slowest playback is the SHORTEST clip over the longest air time, so Max checked
                // the easier clip and let the other drop below 0.60x unnoticed.
                float topTUp=Mathf.Sqrt(2f*c.maxRootRise/d.gravity);
                float topScale=Mathf.Min(c.clipRiseDuration,c.clipRiseDurationTwist)/topTUp;
                Check(topScale>=0.6f,"maxRootRise "+c.maxRootRise.ToString("F2")+" m plays the SLOWEST clip at "+topScale.ToString("F2")+"x >= 0.60 (jump "+(c.clipRiseDuration/topTUp).ToString("F2")+"x, twist "+(c.clipRiseDurationTwist/topTUp).ToString("F2")+"x) "+key);
                Check(KH_FrisbeeCatch.RunInReadyTurnDeg>=20f&&KH_FrisbeeCatch.RunInReadyTurnDeg<=90f,"Ready turn threshold "+KH_FrisbeeCatch.RunInReadyTurnDeg.ToString("F0")+" deg in [20, 90] "+key);
                // IMP-DOG-013: the よしよし window before the stand-off run, proxy and backing, plus the rule that
                // says where 撫で may resume at all (both controllers read PetAllowed).
                Check(c.readyGraceSeconds>=1f&&c.readyGraceSeconds<=10f,"ready grace "+c.readyGraceSeconds.ToString("F1")+" s in [1, 10] "+key);
                Check(c.readyGraceDistance>=1f&&c.readyGraceDistance<=5f,"ready grace distance "+c.readyGraceDistance.ToString("F1")+" m in [1, 5] (arm's reach) "+key);
                Check(cb.publicVariables.TryGetVariableValue("readyGraceSeconds",out bv)&&Mathf.Abs((float)bv-c.readyGraceSeconds)<1e-5f
                    &&cb.publicVariables.TryGetVariableValue("readyGraceDistance",out bv)&&Mathf.Abs((float)bv-c.readyGraceDistance)<1e-5f,"catcher backing: ready grace pair "+key);
                Check(c.readyGraceDistance<c.standoffDistance,"the grace ends well before the stand-off spot ("+c.readyGraceDistance.ToString("F1")+" m < "+c.standoffDistance.ToString("F1")+" m) "+key);
                Check(KH_FrisbeePrototypeEditor.PetStatesOk(),"petting resumes in Idle / Ready / Drop and stays suppressed in every moving state (PetAllowed) "+key);
                // A Ready spot is measured from the holder, so it only ever lands inside the plaza if its own radius does.
                float[] readyAhead={KH_FrisbeeCatch.ReadyAhead1,KH_FrisbeeCatch.ReadyAhead2,KH_FrisbeeCatch.ReadyAhead3};
                float[] readyLateral={KH_FrisbeeCatch.ReadyLateral1,KH_FrisbeeCatch.ReadyLateral2,KH_FrisbeeCatch.ReadyLateral3};
                float readyMax=0f;for(int v=0;v<3;v++){float r=Mathf.Sqrt(readyAhead[v]*readyAhead[v]+readyLateral[v]*readyLateral[v]);if(r>readyMax)readyMax=r;}
                Check(readyMax<=c.playAreaRadius,"run-in Ready spots reach "+readyMax.ToString("F1")+" m <= play area radius "+c.playAreaRadius.ToString("F1")+" "+key);
                // Codex D1 高: the landing rule as pure geometry, against THIS dog's real plaza circle - the same
                // LandingOf / InCircleXZ the planner and the liftoff gate call. A jump that leaves the ground
                // 0.70 m inside the ring and travels 1.00 m must be refused; the same jump 1 m further in must not.
                {
                    Vector3 pc=c.playAreaCenter;float prad=c.playAreaRadius;
                    Vector3 pf=new Vector3(0.6f,0f,0.8f);float pv=2f,pt=0.25f;
                    Vector3 lo=pc+pf*(prad-0.7f),la=KH_FrisbeeCatch.LandingOf(lo,pf,pv,pt);
                    Vector3 lo2=pc+pf*(prad-1.7f),la2=KH_FrisbeeCatch.LandingOf(lo2,pf,pv,pt);
                    Check(Mathf.Abs(((la-pc).magnitude-prad)-0.3f)<1e-3f,"LandingOf puts a "+(pv*2f*pt).ToString("F2")+" m arc 0.30 m past the plaza edge (measured "+((la-pc).magnitude-prad).ToString("F3")+") "+key);
                    Check(KH_FrisbeeCatch.InCircleXZ(lo,pc,prad,0f)&&!KH_FrisbeeCatch.InCircleXZ(la,pc,prad,0f)&&KH_FrisbeeCatch.InCircleXZ(la2,pc,prad,0f),
                        "the play-area predicate refuses a landing 0.30 m outside the plaza although the take-off is inside, and accepts the one 0.70 m in "+key);
                }
                var wb=UdonSharpEditorUtility.GetBackingUdonBehaviour(w);var fb=UdonSharpEditorUtility.GetBackingUdonBehaviour(f);var cbb=UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
                Check(wb.publicVariables.TryGetVariableValue("frisbeeCatch",out bv)&&(bv as UnityEngine.Object)==cbb&&fb.publicVariables.TryGetVariableValue("frisbeeCatch",out bv)&&(bv as UnityEngine.Object)==cbb,"Wander/Fetch backing frisbeeCatch "+key);
                Check(f.sleepBoard==null&&f.liveSystem==null,"sleep board / live unwired for "+key+" (re-verify exclusion if reintroduced)");
            }
            var recallB=root.GetComponentInChildren<KH_ToyRecall>(true);
            Check(recallB!=null&&recallB.discs!=null&&recallB.discs.Length==6&&recallB.discCatchers.Length==6&&Enumerable.Range(0,6).All(i=>{bool want=Array.IndexOf(FrisbeeKeys,Keys[i])>=0;var dt=root.transform.Find("KH_Frisbee_"+Keys[i]);var ct=root.transform.Find("KH_FrisbeeCatch_"+Keys[i]);
                return want?(dt!=null&&ct!=null&&recallB.discs[i]==dt.GetComponent<KH_Frisbee>()&&recallB.discCatchers[i]==ct.GetComponent<KH_FrisbeeCatch>()&&m.frisbeeCatchers[i]==recallB.discCatchers[i]&&act.frisbeeCatchers[i]==recallB.discCatchers[i]):(recallB.discs[i]==null&&recallB.discCatchers[i]==null);}),"toy recall / manager / gather disc arrays point at each dog's own disc and catcher");
            Check(recallB!=null&&recallB.balls!=null&&recallB.balls.Length==6&&recallB.balls.All(x=>x!=null)&&recallB.fetchers.Length==6&&recallB.fetchers.All(x=>x!=null)&&recallB.activities==act,"toy recall wired to 6 balls/fetchers");
            // IMP-DOG-007 leash haptics: local only, one instance, and the only place in the world that shakes a controller.
            var hap=root.GetComponentsInChildren<KH_LeashHaptics>(true);
            Check(hap.Length==1,"exactly one KH_LeashHaptics ("+hap.Length+")");
            if(hap.Length==1)
            {
                var h=hap[0];var hb=UdonSharpEditorUtility.GetBackingUdonBehaviour(h);object hv;
                Check(h.manager==m,"haptics wired to the manager");
                Check(hb.publicVariables.TryGetVariableValue("manager",out hv)&&(hv as UnityEngine.Object)==UdonSharpEditorUtility.GetBackingUdonBehaviour(m),"haptics backing manager reference");
                Check(hb.SyncMethod==Networking.SyncType.None,"haptics backing sync None (local only)");
                Check(h.transform.parent==m.transform,"haptics sits beside the manager");
                Check(h.hapticsOn&&hb.publicVariables.TryGetVariableValue("hapticsOn",out hv)&&(bool)hv,"haptics enabled (proxy and backing)");
                Check(h.pullRatio<h.pullFullRatio,"taut "+h.pullRatio.ToString("F2")+" < full "+h.pullFullRatio.ToString("F2"));
                Check(h.stillPullRatio<=h.pullRatio,"standing band "+h.stillPullRatio.ToString("F2")+" <= taut "+h.pullRatio.ToString("F2"));
                Check(h.pulseInterval>h.pulseDuration&&h.tugInterval>h.tugDuration,"each haptic ends before the next (pulse "+h.pulseDuration.ToString("F2")+"<"+h.pulseInterval.ToString("F2")+", tug "+h.tugDuration.ToString("F2")+"<"+h.tugInterval.ToString("F2")+")");
                Check(h.tugInterval>h.pulseInterval&&h.tugDuration>h.pulseDuration,"the tug is slower and longer than the pulse");
                Check(h.minAmplitude>0f&&h.minAmplitude<1f&&h.frequency>0f&&h.frequency<=1f,"amplitude floor "+h.minAmplitude.ToString("F2")+" in (0,1), frequency "+h.frequency.ToString("F2")+" in (0,1]");
                Check(h.stillSpeed>0f&&h.awaySpeed>0f&&h.teleportJump>=1f,"standing "+h.stillSpeed.ToString("F2")+" m/s, leaving "+h.awaySpeed.ToString("F2")+" m/s, teleport guard "+h.teleportJump.ToString("F2")+" m >= 1");
                // The walk must never buzz from two places at once: only this script may call the haptic API.
                var callers=Directory.GetFiles("Assets/KominkaHillside","*.cs",SearchOption.AllDirectories)
                    .Where(p=>!p.Replace('\\','/').Contains("/Editor/")&&File.ReadAllText(p).Contains("PlayHaptic"+"EventInHand"))
                    .Select(Path.GetFileName).OrderBy(x=>x).ToArray();
                Check(callers.Length==1&&callers[0]=="KH_LeashHaptics.cs","PlayHapticEventInHand called from KH_LeashHaptics.cs only ("+string.Join(", ",callers)+")");
            }
            bool slotsOk=m.stationSlots!=null&&m.stationSlots.Length==6&&m.stationSlots.All(x=>x!=null)&&m.stationSlots.Distinct().Count()==6&&m.slotHand.All(h=>h==0||h==1)
                &&Enumerable.Range(0,6).Select(k=>m.stationOf[k]*2+m.slotHand[k]).Distinct().Count()==6&&m.stationOf.All(s=>s>=0&&s<3)
                &&Enumerable.Range(0,6).All(k=>m.stationSlots[k].parent==new[]{root.transform.Find("KH_LeashRack"),root.transform.Find("KH_Station_Viewpoint"),root.transform.Find("KH_Station_Plaza")}[m.stationOf[k]]);
            Check(slotsOk,"three stations, each exactly one left and one right slot, slots parented to their station");
            bool toolsHome=true;for(int i=0;i<6;i++){var c=m.localClips[i];if(c==null||c.desktopHand!=i/3||Vector3.Distance(c.transform.position,m.stationSlots[(i/3)*3+(i%3)].position)>.01f)toolsHome=false;}
            Check(toolsHome&&m.localClips.Select(c=>c.transform.position).Distinct().Count()==6,"six tools rest one per slot with matching side, no two at one spot");
            Check(act!=null&&act.gameObject==m.gameObject,"gather authority shares the manager GameObject (single Owner)");
            var desc=UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();Check(desc.NetworkIDCollection.Count(x=>x.gameObject==null)==0,"no dangling NetworkIDs");
            Check(desc.NetworkIDCollection.Select(x=>x.ID).Distinct().Count()==desc.NetworkIDCollection.Count&&desc.NetworkIDCollection.Where(x=>x.gameObject!=null).Select(x=>x.gameObject).Distinct().Count()==desc.NetworkIDCollection.Count(x=>x.gameObject!=null),"NetworkIDs unique per ID and per GameObject");
            var networked=root.GetComponentsInChildren<VRCObjectSync>(true).Select(x=>x.gameObject).Concat(root.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Where(u=>u.SyncMethod!=Networking.SyncType.None).Select(u=>u.gameObject)).Distinct().ToArray();
            Check(networked.All(g=>desc.NetworkIDCollection.Any(x=>x.gameObject==g)),"every synced object under KH_Dogs has a NetworkID ("+networked.Length+")");
            foreach(var st in new[]{"KH_LeashRack","KH_Station_Viewpoint","KH_Station_Plaza"}){var t=root.transform.Find(st);Check(t!=null&&t.Find("Crossbar")!=null&&Mathf.Abs(t.position.y-.8f-Ground(t.position.x,t.position.z))<.3f,"station "+st+" placed at rack height");}
            var signs=root.GetComponentsInChildren<KH_WalkSign>(true);Check(signs.Length==6&&signs.All(x=>x.manager==m&&x.GetComponent<BoxCollider>()!=null&&UdonSharpEditorUtility.GetBackingUdonBehaviour(x).InteractionText.Length>0),"six destination sign boards wired");
            Check(signs.Count(x=>x.destination==KH_WalkRouteEditor.DestinationPlaza)==3&&signs.Count(x=>x.destination==KH_WalkRouteEditor.DestinationEntrance)==3,"sign destinations 3 plaza + 3 gate");
            int missing=root.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            Check(missing==0,"no missing scene scripts");
            Check(NavMesh.CalculateTriangulation().vertices.Length>0,"baked navigation exists");
            var dummies=GameObject.Find("KH_World").transform.Find("KH_Dummies");Check(dummies==null||!dummies.gameObject.activeInHierarchy,"terrain placeholders hidden");
            Report("verify",log+"TOTAL "+total+" FAILED "+failed);
        }

        [MenuItem("Tools/KominkaHillside/Walking/6 Render Views")]
        public static void Views()
        {
            Guard();const string dir="Temp/KH_DogPort/Views";Directory.CreateDirectory(dir);
            var root=GameObject.Find("KH_Dogs");var rack=root.transform.Find("KH_LeashRack");
            KH_ViewShotsEditor.Shot("01-rack",rack.position+new Vector3(0,.6f,-1.6f),rack.position,55,dir);
            KH_ViewShotsEditor.Shot("02-six-dogs",new Vector3(46,Ground(46,42)+1.6f,42),new Vector3(46,Ground(46,47)+.3f,47),60,dir);
            foreach(string key in Keys)
            {
                var collar=root.GetComponentsInChildren<KH_LeashCollar>().First(c=>c.name=="KH_Collar_"+key);
                var focus=collar.anchor.position;
                KH_ViewShotsEditor.Shot("03-collar-"+key,focus+new Vector3(.6f,.25f,.8f),focus,45,dir);
            }
            Report("views","Editor views written to "+dir);
        }

        [MenuItem("Tools/KominkaHillside/Walking/9 Inspect Compilation")]
        public static void Compilation()
        {
            var flags=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;
            var cacheType=typeof(UdonSharpEditorUtility).Assembly.GetType("UdonSharp.UdonSharpEditorCache");
            var cache=cacheType.GetProperty("Instance",flags).GetValue(null);
            var diagnostics=(Array)cacheType.GetProperty("LastCompileDiagnostics",flags).GetValue(cache);
            var log=new StringBuilder();int errors=0;
            foreach(var d in diagnostics)
            {
                string severity=d.GetType().GetField("severity",flags).GetValue(d).ToString();
                if(severity=="Error")errors++;
                log.AppendLine(severity+": "+d);
            }
            log.AppendLine("Udon diagnostics="+diagnostics.Length+" errors="+errors);
            var root=GameObject.Find("KH_Dogs");
            foreach(var b in root.GetComponentsInChildren<UdonSharpBehaviour>(true))
            {
                var u=UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
                log.AppendLine(b.name+" program="+(u.programSource!=null)+" symbols="+u.publicVariables.VariableSymbols.Count());
            }
            var m=root.GetComponentInChildren<KH_LeashManager>();var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(m);
            foreach(var symbol in backing.publicVariables.VariableSymbols){object value;backing.publicVariables.TryGetVariableValue(symbol,out value);log.AppendLine("MANAGER "+symbol+"="+(value==null?"null":value.GetType().FullName));}
            void CheckArray(string symbol,UnityEngine.Object[] expected)
            {
                object value;bool exists=backing.publicVariables.TryGetVariableValue(symbol,out value);
                var array=value as Array;bool ok=exists&&array!=null&&array.Length==6&&expected.Length==6;
                if(ok)for(int i=0;i<6;i++)ok&=expected[i]!=null&&(array.GetValue(i) as UnityEngine.Object)==expected[i];
                log.AppendLine((ok?"PASS":"FAIL")+" backing "+symbol+" entries="+(array==null?0:array.Length));
            }
            CheckArray("dogRoots",m.dogRoots);CheckArray("collarAnchors",m.collarAnchors);CheckArray("roamCenters",m.roamCenters);
            CheckArray("stationSlots",m.stationSlots);
            object av;log.AppendLine((backing.publicVariables.TryGetVariableValue("activities",out av)&&(av as UnityEngine.Object)==UdonSharpEditorUtility.GetBackingUdonBehaviour(m.activities)?"PASS":"FAIL")+" backing activities reference");
            var actB=UdonSharpEditorUtility.GetBackingUdonBehaviour(root.GetComponentInChildren<KD_GroupActivities>(true));object bv;var ba=actB.publicVariables.TryGetVariableValue("balls",out bv)?bv as Array:null;
            log.AppendLine((ba!=null&&ba.Length==6?"PASS":"FAIL")+" backing activities.balls entries="+(ba==null?0:ba.Length));
            object pv;log.AppendLine((actB.publicVariables.TryGetVariableValue("hillsidePass",out pv)&&pv!=null?"PASS":"FAIL")+" backing activities.hillsidePass");
            // Synced arrays with program defaults (walkers, hands) live in the program heap, not as overrides; destinations must match that state.
            object dv,wv;bool hasD=backing.publicVariables.TryGetVariableValue("destinations",out dv),hasW=backing.publicVariables.TryGetVariableValue("walkers",out wv);
            log.AppendLine((hasD==hasW&&(!hasD||(dv is int[]&&((int[])dv).Length==6))?"PASS":"FAIL")+" backing destinations state matches walkers (override="+hasD+")");
            CheckArray("fetchers",m.fetchers.Select(f=>(UnityEngine.Object)UdonSharpEditorUtility.GetBackingUdonBehaviour(f)).ToArray());
            CheckArray("localClips",m.localClips.Select(c=>(UnityEngine.Object)UdonSharpEditorUtility.GetBackingUdonBehaviour(c)).ToArray());
            var descriptor=UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
            log.AppendLine("Network IDs="+descriptor.NetworkIDCollection.Count+" missing="+descriptor.NetworkIDCollection.Count(x=>x.gameObject==null));
            Report("compilation",log.ToString());
        }
    }
}
