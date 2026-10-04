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
        // Plaza station: retired 2026-09-26 morning (in the way at the plaza centre), brought back the same evening
        // at the plaza EDGE after the group playtest found the viewpoint rack too far (DES-DOG-003). Placement is
        // human-owned once the rack exists (the user nudges it by hand); Build only places it the first time.
        public const string PlazaStation = "KH_Station_Plaza";
        static readonly string[] StationNames = {"KH_LeashRack","KH_Station_Viewpoint",PlazaStation};
        static readonly string[] Keys = {"Corgi","Pome","Lab","Dachs","Shiba","Samoyed"};
        // Same Product (ownership) and Listing (purchase page) as KominkaDogRun (world-ui-spec section 6). Asset copied with its GUID.
        public const string TrainerProductPath="Assets/KominkaHillside/Data/CE/KD_Product_Trainer.asset";
        public const string TrainerProductId="prod_8c044b21-35f2-4ebb-80f9-e4f685f7bf9f", TrainerListingId="prod_81a5ac65-bd3d-40ad-8edb-2fc7d13b1cee";
        // BUG-DOG-001: balls rest on the flat upper clearing (KH_TerrainPrototypeConfig.Flats[PlazaFlat], y=19 within r=12),
        // off the trail end and away from the shade tree. VRCObjectSync respawns to this start position.
        public static readonly Vector2 BallHomeCenter = new Vector2(KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,0]+3.5f, KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,1]+3.5f);
        public const float BallSpacing=0.9f, BallRestHeight=0.07f;
        // IMP-DOG-005: discs rest in a second row behind the balls (same flat), one per dog.
        // IMP-DOG-012: all six dogs. Each breed needs its own clips + facts from Frisbee/2 (Build refuses a breed without them).
        public static readonly string[] FrisbeeKeys = {"Corgi","Pome","Lab","Dachs","Shiba","Samoyed"};
        // IMP-DOG-012 (Codex M1): every breed in FrisbeeKeys x both clip variants is read through the one typed reader
        // (KH_FrisbeePrototypeEditor.ReadAllFacts: path, name, breed, vectors, times, maxRootRise, clip length) BEFORE
        // Build touches the scene, and Build uses exactly these values. A bad or missing breed stops the build whole.
        static void RequireFrisbeeAssets(out KH_FrisbeePrototypeEditor.Facts[] jump,out KH_FrisbeePrototypeEditor.Facts[] twist)
        {
            KH_FrisbeePrototypeEditor.ReadAllFacts(FrisbeeKeys,out jump,out twist);
        }
        public static readonly Color[] DiscColors = {new Color(.90f,.30f,.25f),new Color(.95f,.60f,.75f),new Color(.95f,.80f,.15f),new Color(.35f,.55f,.95f),new Color(.85f,.45f,.20f),new Color(.90f,.90f,.95f)};
        public static Vector3 DiscHome(int i)
        {
            Vector3 b=PlazaToySpot(i);float z=b.z+2.2f;
            return new Vector3(b.x,Ground(b.x,z)+0.03f,z);
        }
        // The plaza grid where the balls rested until 2026-09-26 night (BUG-DOG-001). Only the disc row still hangs off it.
        static Vector3 PlazaToySpot(int i)
        {
            float x=BallHomeCenter.x+((i%3)-1)*BallSpacing, z=BallHomeCenter.y+((i/3)-0.5f)*BallSpacing;
            return new Vector3(x,Ground(x,z)+BallRestHeight,z);
        }
        // IMP-DOG-019 (2026-09-26 night): one toy out per dog. The ball starts in the toy park (and VRCObjectSync
        // respawns it there); [Ball] in the hand menu brings it out and sends the disc down, and back (KH_ToyRecall).
        // Park = KH_Dogs/KH_ToyPark: an invisible box (floor + low walls, no Renderer) ParkDepth below the plaza centre.
        // Layer 17 Walkthrough: collides with the toys (13) but not with players, and is outside the NavMesh
        // (layers 0|11) and the ground rays (0|11). Its floor must stay above VRC_SceneDescriptor.RespawnHeightY,
        // or VRCObjectSync would respawn a parked toy forever. Code-owned: Build rewrites it every time.
        public const string ToyParkName="KH_ToyPark";
        public const float ParkDepth=4f, ParkSpacing=.5f, ParkWallHeight=.5f;
        public const int ParkLayer=17;
        public static readonly Vector3 ParkFloorSize=new Vector3(4f,.2f,2f);
        public static Vector3 ParkOrigin()   // top of the floor, under the plaza centre
        {
            float x=KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,0],z=KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,1];
            return new Vector3(x,Ground(x,z)-ParkDepth,z);
        }
        static Vector3 ParkBallLocal(int i){return new Vector3((i-2.5f)*ParkSpacing,.15f,-.4f);}
        static Vector3 ParkDiscLocal(int i){return new Vector3((i-2.5f)*ParkSpacing,.1f,.4f);}
        public static Vector3 BallHome(int i){return ParkOrigin()+ParkBallLocal(i);}
        // Floor + four walls of the park: names, local centres, collider sizes (Build writes them, Verify compares them).
        static readonly string[] ParkBoxNames={"Floor","WallN","WallS","WallE","WallW"};
        static void ParkBoxes(out Vector3[] centres,out Vector3[] sizes)
        {
            float hx=ParkFloorSize.x*.5f,hz=ParkFloorSize.z*.5f,t=.1f;
            centres=new[]{new Vector3(0f,-ParkFloorSize.y*.5f,0f),new Vector3(0f,ParkWallHeight*.5f,hz+t*.5f),new Vector3(0f,ParkWallHeight*.5f,-hz-t*.5f),new Vector3(hx+t*.5f,ParkWallHeight*.5f,0f),new Vector3(-hx-t*.5f,ParkWallHeight*.5f,0f)};
            sizes=new[]{ParkFloorSize,new Vector3(ParkFloorSize.x+2*t,ParkWallHeight,t),new Vector3(ParkFloorSize.x+2*t,ParkWallHeight,t),new Vector3(t,ParkWallHeight,ParkFloorSize.z),new Vector3(t,ParkWallHeight,ParkFloorSize.z)};
        }
        static Transform BuildToyPark(Transform root,out Transform[] parkBall,out Transform[] parkDisc)
        {
            var park=Child(root,ToyParkName);park.position=ParkOrigin();park.rotation=Quaternion.identity;park.localScale=Vector3.one;
            var names=ParkBoxNames;Vector3[] centres,sizes;ParkBoxes(out centres,out sizes);
            for(int k=0;k<names.Length;k++)
            {
                var b=Child(park,names[k]);b.localPosition=centres[k];b.localRotation=Quaternion.identity;b.localScale=Vector3.one;
                var col=b.GetComponent<BoxCollider>();if(col==null)col=b.gameObject.AddComponent<BoxCollider>();
                col.center=Vector3.zero;col.size=sizes[k];col.isTrigger=false;col.enabled=true;
            }
            parkBall=new Transform[6];parkDisc=new Transform[6];
            for(int i=0;i<6;i++)
            {
                parkBall[i]=Child(park,"Ball"+i);parkBall[i].localPosition=ParkBallLocal(i);parkBall[i].localRotation=Quaternion.identity;
                parkDisc[i]=Child(park,"Disc"+i);parkDisc[i].localPosition=ParkDiscLocal(i);parkDisc[i].localRotation=Quaternion.identity;
            }
            foreach(var tr in park.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=ParkLayer;
            return park;
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
            // KH_HandMenu は退役 (段階 0-b。手元メニューは KD_TreatPouch = Tools/KominkaHillside/Menu/5 が Program Asset を作る)。
            // スクリプト削除後にここへ残すと、元の無い Program Asset を作ってしまう
            foreach (string name in new[]{"KH_LeashManager","KH_LeashClip","KH_LeashCollar","KH_LeashHaptics","KH_WalkRoute","KH_TrainerPass","KH_ToyRecall","KH_MenuOpener"})
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

        // DES-LIV-001: the live (Menu/26a) is optional. If the scene has a KD_LiveSystem, gathering and every dog point at it
        // (and the wander controllers at KD_LiveDogSpot); without one they stay null. Build and Verify use the same rule.
        static KD_LiveSystem SceneLive(out Transform spot)
        {
            var lives=EditorSceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<KD_LiveSystem>(true)).ToArray();
            var live=lives.Length==1?lives[0]:null;
            var spots=EditorSceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Where(t=>t.name=="KD_LiveDogSpot").ToArray();
            spot=live!=null&&spots.Length==1?spots[0]:null;
            return live;
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
            if(m==null){m=new Material(Shader.Find("VRChat/Mobile/Standard Lite"));m.color=color;KH_MatteMaterialsEditor.Matte(m);AssetDatabase.CreateAsset(m,path);}
            return m;
        }
        static Transform Child(Transform parent,string name)
        {
            var t=parent.Find(name);if(t!=null)return t;
            var go=new GameObject(name);go.transform.SetParent(parent,false);return go.transform;
        }
        static float Ground(float x,float z)
        {
            // 2026-10-03: the loop course split the ground into tiles (KH_CourseTerrain_*), so FindObjectOfType<Terrain> returned a far tile
            // and every height check (discs, stations) read a wrong number. Take the topmost TerrainCollider under (x, z) (what the player
            // stands on where tiles and the prototype KH_Terrain overlap); sample the one terrain only when no collider answers.
            var hits=Physics.RaycastAll(new Vector3(x,500f,z),Vector3.down,1000f,~0,QueryTriggerInteraction.Ignore);
            float best=float.NegativeInfinity;
            foreach(var h in hits)if(h.collider is TerrainCollider&&h.point.y>best)best=h.point.y;
            if(!float.IsNegativeInfinity(best))return best;
            var terrain=UnityEngine.Object.FindObjectOfType<Terrain>();
            return terrain.SampleHeight(new Vector3(x,0,z))+terrain.transform.position.y;
        }

        [MenuItem("Tools/KominkaHillside/Walking/3 Build")]
        public static void Build()
        {
            KominkaHillside.Editor.KH_ProductionScene.RejectLegacyRebuild();
            Guard();var scene=EditorSceneManager.GetActiveScene();
            if(scene.isDirty)throw new InvalidOperationException("Save scene before the initial walking build.");
            KH_FrisbeePrototypeEditor.Facts[] frisbeeJump,frisbeeTwist;RequireFrisbeeAssets(out frisbeeJump,out frisbeeTwist); // IMP-DOG-012: before anything in the scene changes
            {   // IMP-DOG-019: a park below RespawnHeightY would make VRCObjectSync respawn the parked toys forever
                var d=desc0();if(d==null)throw new InvalidOperationException("VRC_SceneDescriptor is required (toy park vs RespawnHeightY)");
                float floorBottom=ParkOrigin().y-ParkFloorSize.y;
                if(floorBottom-1f<=d.RespawnHeightY)throw new InvalidOperationException("Toy park floor "+floorBottom.ToString("F2")+" is not at least 1 m above RespawnHeightY "+d.RespawnHeightY.ToString("F2"));
            }
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
            // BUG-DOG-002: handle grip and ring roll. Build owns them so a rebuild cannot drop them back to
            // scene leftovers; the values still wait on PCVR judgement. Single source: KH_LeashProductionEditor
            // (Production 7 writes them on the production scene, where this Build is refused; Production 3 checks them).
            manager.handleGripOffset=KH_LeashProductionEditor.HandleGripOffset;manager.handleRollDegrees=KH_LeashProductionEditor.HandleRollDegrees;
            // IMP-DOG-009: the upper two of the four walking tiers. Prototype values, not yet judged in Play - Build owns them.
            manager.dashSpeed=4.5f;manager.sprintSpeed=7.2f;manager.stopoverChance=.5f;
            manager.trailOffDistance=6f;manager.trailOnDistance=4f;manager.entryMaxPath=12f;manager.inviteAfter=8f;manager.inviteInterval=14f;manager.inviteLimit=3;manager.detourChance=.25f;
            var existingRoute=root.GetComponentInChildren<KH_WalkRoute>(true);manager.route=existingRoute;
            var pack=Find<KD_DogPack>(root,"KD_DogPack");
            // IMP-DOG-016: keep the Call tab's sleep board if Menu/6 has built it (null before stage 2 = the old behaviour).
            var sleepBoardT=root.transform.Find("KD_SleepBoard");var sleepBoard=sleepBoardT!=null?sleepBoardT.GetComponent<KD_SleepBoard>():null;
            manager.sleepBoard=sleepBoard;
            // Gathering (game-design §4): hillside copy of KD_GroupActivities, trainer entitlement via the shared Product/Listing.
            var pass=Add<KH_TrainerPass>(Child(root.transform,"KH_TrainerPass").gameObject);
            pass.product=AssetDatabase.LoadAssetAtPath<VRC.Economy.UdonProduct>(TrainerProductPath);pass.listingId=TrainerListingId;
            // Same GameObject as the manager: one Owner adjudicates attach and gather (review D1). Remove the earlier separate object.
            var oldAct=root.transform.Find("KH_Activities");if(oldAct!=null)UnityEngine.Object.DestroyImmediate(oldAct.gameObject);
            var act=Add<KD_GroupActivities>(manager.gameObject,true);
            var sceneLive=SceneLive(out var sceneLiveSpot);
            // IMP-WLD-005: act.photos is owned by Tools/KominkaHillside/Photo/1 (kept here; null until the photo system is built)
            act.live=sceneLive;act.formationItem=null;act.playItem=null;act.pack=pack;act.hillsideLeashes=manager;act.hillsidePass=pass;
            act.minGroundNormal=.5f;act.maxSlotHeightDelta=0f;act.groundMask=(1<<0)|(1<<11);act.balls=new KD_BallController[6];
            manager.activities=act;
            // IMP-DOG-007: local-only haptics beside the manager. Build owns the thresholds the same way it owns
            // the walking tiers, so a scene built earlier cannot keep values the code no longer describes.
            var haptics=Add<KH_LeashHaptics>(Child(manager.transform,"KH_LeashHaptics").gameObject);
            haptics.manager=manager;haptics.hapticsOn=true;
            haptics.pullRatio=1f;haptics.pullFullRatio=1.25f;haptics.stillSpeed=.4f;haptics.stillPullRatio=.85f;haptics.awaySpeed=.3f;
            haptics.pulseInterval=.12f;haptics.pulseDuration=.08f;haptics.tugInterval=.5f;haptics.tugDuration=.15f;
            haptics.minAmplitude=.3f;haptics.frequency=.5f;haptics.teleportJump=1f;haptics.logPulls=false;   // IMP-DOG-034 A7: pull log off in production
            var recall=Add<KH_ToyRecall>(Child(root.transform,"KH_ToyRecall").gameObject);
            recall.balls=new KD_BallController[6];recall.fetchers=new KD_DogFetchController[6];recall.activities=act;recall.groundMask=(1<<0)|(1<<11);
            Transform[] parkBall,parkDisc;BuildToyPark(root.transform,out parkBall,out parkDisc);   // IMP-DOG-019: the toy that is not out waits here
            recall.parkBall=parkBall;recall.parkDisc=parkDisc;
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
                f.sleepBoard=sleepBoard;f.liveSystem=sceneLive;w.liveSystem=sceneLive;w.liveSpot=sceneLiveSpot;f.activities=act;w.activities=act;   // IMP-WLD-005: f/w.photoDirector are owned by Photo/1 (kept)
                if(sleepBoard!=null)w.sleepBoard=sleepBoard;
                act.balls[i]=b;recall.balls[i]=b;recall.fetchers[i]=f;
                p.companionSlot=sleepBoard!=null?sleepBoard.SlotOf(i):null;   // IMP-DOG-016: chin rest reads the dog's own slot (Menu/6j wires it too)
                manager.dogRoots[i]=dog;manager.fetchers[i]=f;manager.roamCenters[i]=home;
                roots[i]=dog;fetch[i]=f;
                // Design-owned start position, rewritten on every Build: the toy park since IMP-DOG-019 (was the plaza, BUG-DOG-001).
                b.transform.position=parkBall[i].position;b.transform.rotation=Quaternion.identity;
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
            for(int i=0;i<6;i++)
            {
                string key=Keys[i];var w=Find<KD_DogWanderController>(root,"KD_WanderSystem_"+key);var f=Find<KD_DogFetchController>(root,"KD_FetchSystem_"+key);var p=Find<KD_DogPetController>(root,"KD_PetSystem_"+key);
                if(Array.IndexOf(FrisbeeKeys,key)<0){w.frisbeeCatch=null;f.frisbeeCatch=null;continue;}
                // IMP-DOG-012: each breed's own pose facts (both clip variants, IMP-DOG-008 v4). RequireFrisbeeAssets
                // already refused a breed without them, so nothing here can fall back to Lab's mouth.
                int fk=Array.IndexOf(FrisbeeKeys,key);var facts=frisbeeJump[fk];var twistFacts=frisbeeTwist[fk]; // validated up front (Codex M1)
                bool created;var disc=KH_FrisbeePrototypeEditor.BuildDisc(root.transform,"KH_Frisbee_"+key,"KH_FrisbeeDisc_"+key,DiscColors[i],out created);
                foreach(var t in disc.GetComponentsInChildren<Transform>(true))t.gameObject.layer=13;
                if(created){disc.transform.position=DiscHome(i);disc.transform.rotation=Quaternion.identity;} // human-owned once placed (review M4)
                var c=Add<KH_FrisbeeCatch>(Child(root.transform,"KH_FrisbeeCatch_"+key).gameObject,true);
                KH_FrisbeePrototypeEditor.ConfigureCatcher(c,disc,roots[i],f.dogAnimator,f.mouthAnchor,facts,twistFacts);
                // IMP-DOG-008 wave 2: the purchased body hangs off the 追従GIMMICK ParentConstraint and trails the
                // root by up to 30 deg / 0.5 m while the dog turns and jumps, so the catch plan (root + rotation *
                // mouthOffsetCatch) misses. Only the frisbee dogs are made rigid (IMP-DOG-012: all six, FrisbeeKeys).
                KH_FrisbeeJumpClipEditor.MakeBodyRigid(roots[i].gameObject);
                c.hillsideLeashes=manager;c.fetch=f;c.wander=w;c.activities=act;c.pet=p;c.dogIndex=i;
                c.playAreaCenter=new Vector3(KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,0],KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,2],KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,1]);
                c.playAreaRadius=0f; // DES-DOG-005 M4: no play circle (the take-out gate is the player's feet); the centre stays as the plaza anchor
                w.frisbeeCatch=c;f.frisbeeCatch=c;manager.frisbeeCatchers[i]=c;act.frisbeeCatchers[i]=c;recall.discs[i]=disc;recall.discCatchers[i]=c;
            }
            pack.dogRoots=roots;pack.dogFetch=fetch;
            // Leash stations (game-design section 4): the entrance rack and the viewpoint rack, which also serves
            // the plaza (2026-09-26: the plaza-centre rack was in the way). Station placement is human-owned
            // (written once); slots and tools are code-owned.
            var wood=Mat("KH_RackWood",new Color(.32f,.19f,.10f));
            var rack=Child(root.transform,"KH_LeashRack");
            if(initial)rack.position=new Vector3(45,Ground(45,43)+.8f,43);
            var stations=new Transform[]{rack,Child(root.transform,"KH_Station_Viewpoint"),Child(root.transform,PlazaStation)};
            // Initial placement comes from the terrain places, so build order does not matter.
            var placesRoot=GameObject.Find("KH_World");
            if(placesRoot==null)throw new InvalidOperationException("KH_World (terrain places) is required to place stations.");
            if(stations[1].Find("Crossbar")==null) // human-owned placement already exists otherwise
            {
                var place=placesRoot.transform.Find("KH_Places/KH_Viewpoint");if(place==null)throw new InvalidOperationException("Missing KH_Places/KH_Viewpoint");
                var rot=Quaternion.Euler(0f,45f,0f);Vector3 p=place.position+new Vector3(2.4f,0f,1.2f)+rot*new Vector3(1.3f,0f,0f);
                stations[1].rotation=rot;stations[1].position=new Vector3(p.x,Ground(p.x,p.z)+.8f,p.z);
            }
            if(stations[2].Find("Crossbar")==null) // plaza EDGE (DES-DOG-003): 10 m from the centre (r=12) on the side away from the toys, facing inward
            {
                float cx=KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,0],cz=KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,1];
                Vector3 centre=new Vector3(cx,0f,cz);Vector3 p=centre+new Vector3(-.6f,0f,-.8f)*10f;
                stations[2].rotation=Quaternion.LookRotation(new Vector3(centre.x-p.x,0f,centre.z-p.z));stations[2].position=new Vector3(p.x,Ground(p.x,p.z)+.8f,p.z);
            }
            // The "open the menu" plate (KH_MenuOpener, human-owned since KH_HandMenuEditor retired) is copied from the viewpoint
            // rack once; KD_TreatSetupEditor (Menu) wires it to the pouch and its Verify counts OpenerStations (3).
            if(stations[2].Find("MenuPlate")==null)
            {
                var srcPlate=stations[1].Find("MenuPlate");
                if(srcPlate!=null){var copy=UnityEngine.Object.Instantiate(srcPlate.gameObject,stations[2]);copy.name="MenuPlate";copy.transform.localPosition=srcPlate.localPosition;copy.transform.localRotation=srcPlate.localRotation;copy.transform.localScale=srcPlate.localScale;}
                else Debug.LogWarning("[KH_DogWalking] viewpoint MenuPlate not found; the plaza station has no menu plate (run Menu wiring after placing one)");
            }
            // Tool i rests at slot (i/3)*3+(i%3): hand = i/3 (0 left, 1 right), station = i%3 (entrance / viewpoint / plaza), one slot per side.
            manager.stationSlots=new Transform[6];manager.slotHand=new int[6];manager.stationOf=new int[6];
            foreach(var st in stations)
            {
                KH_LeashModelsEditor.MakeRack(st,wood);
                foreach(string stale in new[]{"SlotL2","SlotR2"}){var t=st.Find(stale);if(t!=null)UnityEngine.Object.DestroyImmediate(t.gameObject);} // four-slot viewpoint layout of 27e703f
            }
            for(int h=0;h<2;h++)for(int s=0;s<3;s++)
            {
                var slot=Child(stations[s],h==0?"SlotL":"SlotR");slot.localPosition=new Vector3(h==0?-.2f:.2f,0f,0f);slot.localRotation=Quaternion.identity;
                int k=h*3+s;manager.stationSlots[k]=slot;manager.slotHand[k]=h;manager.stationOf[k]=s;
            }
            var tools=Child(root.transform,"KH_LeashTools");
            for(int i=0;i<6;i++)
            {
                var t=tools.Find("Clip"+i);if(t==null)t=rack.Find("Clip"+i);bool newClip=t==null;
                if(newClip){var go=GameObject.CreatePrimitive(PrimitiveType.Capsule);go.name="Clip"+i;t=go.transform;t.localScale=new Vector3(.07f,.12f,.07f);}
                t.SetParent(tools,true);
                // Tool i: hand = i/3 (0 left, 1 right), home slot as laid out above.
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
            KominkaHillside.Editor.KH_ProductionScene.RejectLegacyRebuild();
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
            {var sb=root.transform.Find("KD_SleepBoard")?.GetComponent<KD_SleepBoard>();Check(m.sleepBoard==sb,"leash manager asks the Call tab's sleep board before attaching (IMP-DOG-016; null before stage 2)");}
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
            // IMP-DOG-019 (2026-09-26 night): only the discs are out at start; the balls wait in the toy park under the
            // plaza (replaces BUG-DOG-001's "ball on the flat clearing" and "ball reachable on navigation").
            var park=root.transform.Find(ToyParkName);var recallP=root.GetComponentInChildren<KH_ToyRecall>(true);
            Check(park!=null,"toy park "+ToyParkName+" exists under KH_Dogs");
            if(park!=null)
            {
                float px=KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,0],pz=KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,1];
                float below=Ground(px,pz)-park.position.y;var dsc=desc0();
                Check(below>=2f,"toy park "+below.ToString("F2")+" m below the plaza ground (>= 2)");
                Check(dsc!=null&&park.position.y-ParkFloorSize.y-1f>dsc.RespawnHeightY,"toy park floor "+(park.position.y-ParkFloorSize.y).ToString("F2")+" at least 1 m above RespawnHeightY "+(dsc==null?float.NaN:dsc.RespawnHeightY).ToString("F2"));
                Check(Vector2.Distance(new Vector2(park.position.x,park.position.z),new Vector2(px,pz))<.01f,"toy park under the plaza centre");
                Check(park.rotation==Quaternion.identity&&park.lossyScale==Vector3.one,"toy park unrotated, unscaled");
                Vector3[] boxCentres,boxSizes;ParkBoxes(out boxCentres,out boxSizes);
                for(int k=0;k<ParkBoxNames.Length;k++)
                {   // Codex review 2026-09-26 night: check the real shape, not just that colliders exist
                    var t=park.Find(ParkBoxNames[k]);var c=t==null?null:t.GetComponent<BoxCollider>();
                    Check(c!=null&&!c.isTrigger&&c.enabled&&Vector3.Distance(t.localPosition,boxCentres[k])<1e-3f&&t.localRotation==Quaternion.identity&&t.localScale==Vector3.one
                        &&c.center==Vector3.zero&&Vector3.Distance(c.size,boxSizes[k])<1e-3f,"toy park "+ParkBoxNames[k]+" BoxCollider at "+boxCentres[k].ToString("F2")+" size "+boxSizes[k].ToString("F2")+" (actual "+(c==null?"-":t.localPosition.ToString("F2")+" "+c.size.ToString("F2"))+")");
                }
                var nav=root.GetComponentInChildren<NavMeshSurface>(true);
                Check(park.GetComponentsInChildren<Transform>(true).All(t=>t.gameObject.layer==ParkLayer)&&(nav==null||(nav.layerMask.value&(1<<ParkLayer))==0)&&!Physics.GetIgnoreLayerCollision(ParkLayer,13),"toy park on layer "+ParkLayer+": collides with toys (13), outside the NavMesh layers");
                Check(park.GetComponentsInChildren<Renderer>(true).Length==0,"toy park has no Renderer");
                Check(Enumerable.Range(0,6).All(i=>{var b=park.Find("Ball"+i);var d=park.Find("Disc"+i);return b!=null&&d!=null&&Vector3.Distance(b.localPosition,ParkBallLocal(i))<1e-3f&&Vector3.Distance(d.localPosition,ParkDiscLocal(i))<1e-3f;}),"toy park spots Ball0..5 / Disc0..5 on the floor inside the walls");
                Check(recallP!=null&&recallP.parkBall!=null&&recallP.parkBall.Length==6&&recallP.parkDisc!=null&&recallP.parkDisc.Length==6
                    &&Enumerable.Range(0,6).All(i=>recallP.parkBall[i]==park.Find("Ball"+i)&&recallP.parkDisc[i]==park.Find("Disc"+i)),"toy recall parkBall / parkDisc wired to the six park spots each");
                for(int i=0;i<6;i++)
                {
                    var ball=Find<Transform>(root,"KD_Ball_"+Keys[i]);var spot=park.Find("Ball"+i);
                    float gap=spot==null?float.MaxValue:Vector3.Distance(ball.position,spot.position);
                    Check(gap<=.05f,"ball starts in the toy park "+Keys[i]+" (off its spot "+(spot==null?"-":gap.ToString("F3"))+" m <= 0.05)");
                }
            }
            var walkRoute=root.GetComponentInChildren<KH_WalkRoute>(true);Check(walkRoute!=null&&m.route==walkRoute,"walk route wired to manager");
            // 2026-10-03: the production route is the river loop (436 nodes, linkA == n, no n x n nextHop table, 6 destinations). Its shape,
            // links and destinations are Course/22 / Course/26's; the prototype's open gate->plaza checks below only apply to that route.
            bool loopRoute=walkRoute!=null&&walkRoute.nextHop!=null&&walkRoute.nextHop.Length==0&&walkRoute.linkA!=null&&walkRoute.linkA.Length==walkRoute.count;
            if(loopRoute)
            {
                int n=walkRoute.count;Check(n>=8&&walkRoute.nodes!=null&&walkRoute.nodes.Length==n&&walkRoute.linkB!=null&&walkRoute.linkB.Length==n&&walkRoute.arriveRadius.Length==n,"loop route arrays sized n="+n+" (links "+walkRoute.linkA.Length+")");
                log.AppendLine("NOTE loop route (river loop): shape, NavMesh links and the 6 destinations are verified by Course/22 Verify Full Course Route and Course/26 Verify Course Destinations; the prototype gate->plaza checks are skipped");
                var rb=UdonSharpEditorUtility.GetBackingUdonBehaviour(walkRoute);object rv;var rn=rb.publicVariables.TryGetVariableValue("nodes",out rv)?rv as Array:null;
                Check(rn!=null&&rn.Length==n&&(rn.GetValue(0) as Transform)==walkRoute.nodes[0],"route backing nodes entries="+(rn==null?0:rn.Length));
                var mb=UdonSharpEditorUtility.GetBackingUdonBehaviour(m);object mr;Check(mb.publicVariables.TryGetVariableValue("route",out mr)&&(mr as UnityEngine.Object)==UdonSharpEditorUtility.GetBackingUdonBehaviour(walkRoute),"manager backing route reference");
            }
            else if(walkRoute!=null)
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
                Check(act.pack==pack&&act.hillsideLeashes==m&&act.balls!=null&&act.balls.Length==6&&act.balls.All(x=>x!=null)&&act.live==SceneLive(out _)&&act.minGroundNormal<=.5f&&act.maxSlotHeightDelta==0f,"gather wiring: pack, leashes, 6 balls, live = the scene KD_LiveSystem if any (else null), slope tolerant");
                var passB=root.GetComponentInChildren<KH_TrainerPass>(true);
                Check(passB!=null&&act.hillsidePass==passB&&passB.product!=null&&passB.product.ID==TrainerProductId&&passB.listingId==TrainerListingId,"trainer pass: shared Product/Listing IDs");
                bool actRefs=true;for(int i=0;i<6;i++){var w=Find<KD_DogWanderController>(root,"KD_WanderSystem_"+Keys[i]);var f=Find<KD_DogFetchController>(root,"KD_FetchSystem_"+Keys[i]);if(w.activities!=act||f.activities!=act)actRefs=false;}
                Check(actRefs,"all six Wander/Fetch reference the gather authority");
            }
            // IMP-DOG-005: disc + catcher on the walking dog (FrisbeeKeys; IMP-DOG-012: all six), others deliberately unwired.
            var descV=desc0();
            Check(KH_FrisbeeCatch.CurveDefault>0f&&KH_FrisbeeCatch.CurveDefault<=0.5f,"flight curve default "+KH_FrisbeeCatch.CurveDefault.ToString("F2")+" in (0, 0.5] 1/m");
            for(int i=0;i<6;i++)
            {
                string key=Keys[i];bool wanted=Array.IndexOf(FrisbeeKeys,key)>=0;var w=Find<KD_DogWanderController>(root,"KD_WanderSystem_"+key);var f=Find<KD_DogFetchController>(root,"KD_FetchSystem_"+key);var p=Find<KD_DogPetController>(root,"KD_PetSystem_"+key);
                var ct=root.transform.Find("KH_FrisbeeCatch_"+key);var dt=root.transform.Find("KH_Frisbee_"+key);
                if(wanted&&dt!=null){string faceProblems=KH_FrisbeePrototypeEditor.FaceProblems(dt.GetComponent<KH_Frisbee>(),key);Check(string.IsNullOrEmpty(faceProblems),"disc "+key+" carries the dog face top and bottom (IMP-DOG-018)"+(string.IsNullOrEmpty(faceProblems)?"":": "+faceProblems));}
                // IMP-DOG-008 wave 2: only the frisbee dogs lose the 追従GIMMICK body-follow ParentConstraint, so the
                // change stays scoped - the other five must still have it (their body trails the root as bought).
                if(!wanted){Check(ct==null&&dt==null&&w.frisbeeCatch==null&&f.frisbeeCatch==null&&(m.frisbeeCatchers==null||m.frisbeeCatchers[i]==null)&&(act.frisbeeCatchers==null||act.frisbeeCatchers[i]==null),"no disc for "+key+" yet (deliberate)");
                    Check(m.dogRoots[i]!=null&&!KH_FrisbeeJumpClipEditor.IsBodyRigid(m.dogRoots[i].gameObject),"non-frisbee dog "+key+" keeps the purchased body-follow constraint");continue;}
                Check(m.dogRoots[i]!=null&&KH_FrisbeeJumpClipEditor.IsBodyRigid(m.dogRoots[i].gameObject),"frisbee dog body is rigid to its root (追従 ParentConstraint inactive) "+key);
                // Codex D2: the change is scoped to that one constraint - anything else under the dog still runs.
                Check(m.dogRoots[i]!=null&&KH_FrisbeeJumpClipEditor.OtherConstraintsUntouched(m.dogRoots[i].gameObject),"every other ParentConstraint under "+key+" is still active (scoped to "+KH_FrisbeeJumpClipEditor.BodyFollowPath+")");
                var c=ct==null?null:ct.GetComponent<KH_FrisbeeCatch>();var d=dt==null?null:dt.GetComponent<KH_Frisbee>();
                Check(c!=null&&d!=null,"disc and catcher exist for "+key);if(c==null||d==null)continue;
                // IMP-DOG-012: this breed's own clips and facts (never Lab's), and the catcher / disc are networked objects.
                KH_FrisbeePrototypeEditor.Facts[] vj=null,vt=null;string factsErr=null;
                try{KH_FrisbeePrototypeEditor.ReadAllFacts(new[]{key},out vj,out vt);}catch(Exception e){factsErr=e.Message;}
                Check(factsErr==null,"frisbee facts JSON (Jump + Twist) valid for "+key+" (name, breed, vectors, times, maxRootRise, clip length)"+(factsErr==null?"":": "+factsErr));if(factsErr!=null)continue;
                var facts=vj[0];var clipAsset=AssetDatabase.LoadAssetAtPath<AnimationClip>(KH_FrisbeeJumpClipEditor.ClipPathFor("Jump",key));
                // IMP-DOG-008 v4: the twist clip is the second member of the family and is checked like the jump.
                var twistFacts=vt[0];var twistClip=AssetDatabase.LoadAssetAtPath<AnimationClip>(KH_FrisbeeJumpClipEditor.ClipPathFor("Twist",key));
                var jumpVariant=KH_FrisbeeJumpClipEditor.Variants(key).FirstOrDefault(v=>v.name=="Jump");var twistVariant=KH_FrisbeeJumpClipEditor.Variants(key).FirstOrDefault(v=>v.name=="Twist");
                Check(clipAsset!=null&&Mathf.Abs(clipAsset.length-jumpVariant.EndAt)<0.02f,"jump clip exists, length "+(clipAsset==null?"-":clipAsset.length.ToString("F3"))+" == "+jumpVariant.EndAt.ToString("F3")+" "+key);
                Check(twistClip!=null&&Mathf.Abs(twistClip.length-twistVariant.EndAt)<0.02f,"twist clip exists, length "+(twistClip==null?"-":twistClip.length.ToString("F3"))+" == "+twistVariant.EndAt.ToString("F3")+" "+key);
                Check(descV!=null&&descV.NetworkIDCollection.Any(x=>x.gameObject==ct.gameObject)&&descV.NetworkIDCollection.Any(x=>x.gameObject==dt.gameObject),"catcher and disc have NetworkIDs "+key);
                Check(d.GetComponent<VRCPickup>()!=null&&d.GetComponent<Renderer>()!=null&&d.GetComponent<Renderer>().sharedMaterial!=null&&d.GetComponent<Renderer>().sharedMaterial.name=="KH_FrisbeeDisc_"+key,"disc wears its own colour KH_FrisbeeDisc_"+key);
                Check(c.dogRoot==m.dogRoots[i]&&c.dogAnimator==f.dogAnimator&&c.mouthAnchor==f.mouthAnchor&&c.frisbee==d&&d.catcher==c,"catcher shares the walking dog's root/Animator/mouth "+key);
                Check(c.hillsideLeashes==m&&c.fetch==f&&c.wander==w&&c.activities==act&&c.pet==p&&c.dogIndex==i,"catcher neighbours "+key);
                Check(w.frisbeeCatch==c&&f.frisbeeCatch==c&&m.frisbeeCatchers[i]==c&&act.frisbeeCatchers[i]==c,"Wander/Fetch/Manager/Gather see the catcher "+key);
                Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(c).SyncMethod==Networking.SyncType.Manual&&UdonSharpEditorUtility.GetBackingUdonBehaviour(d).SyncMethod==Networking.SyncType.None,"catcher Manual / disc None "+key);
                // Both clips play from the same minRootRise, so the floor follows the longer rise time (Codex P).
                float longestRise=Mathf.Max(facts.rise,twistFacts.rise);
                Check(Vector3.Distance(c.mouthOffsetCatch,facts.mouthOffsetCatch)<1e-4f&&Mathf.Abs(c.clipRiseDuration-facts.rise)<1e-4f&&c.minRootRise>=0.5f*d.gravity*Mathf.Pow(longestRise/1.8f,2f)-1e-3f,"pose facts match the clip JSON, rise floor from the longest rise "+longestRise.ToString("F2")+" s "+key);
                Check(Mathf.Abs(c.maxRootRise-facts.maxRootRise)<1e-4f,"jump ceiling maxRootRise "+c.maxRootRise.ToString("F2")+" == "+key+"'s facts "+facts.maxRootRise.ToString("F2")+" (Codex D1: per breed, Lab 0.90) "+key);
                Check(Vector3.Distance(c.mouthOffsetCatchTwist,twistFacts.mouthOffsetCatch)<1e-4f&&Mathf.Abs(c.clipCrouchDurationTwist-twistFacts.crouch)<1e-4f&&Mathf.Abs(c.clipRiseDurationTwist-twistFacts.rise)<1e-4f&&Mathf.Abs(c.clipRecoverDurationTwist-twistFacts.recover)<1e-4f,"twist pose facts match the twist clip JSON "+key);
                var ac=f.dogAnimator==null?null:f.dogAnimator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                Check(ac!=null&&ac.parameters.Any(x=>x.name=="Jump")&&ac.layers[0].stateMachine.states.Any(st=>st.state.name=="Jump"&&st.state.motion==clipAsset),"Animator has Jump with the clip "+key);
                var twistState=ac==null?null:ac.layers[0].stateMachine.states.Select(st=>st.state).FirstOrDefault(st=>st.name=="Twist");
                Check(ac!=null&&ac.parameters.Any(x=>x.name=="Twist"&&x.type==AnimatorControllerParameterType.Bool)&&twistState!=null&&twistState.motion==twistClip,"Animator has Bool Twist with the twist clip "+key);
                Check(ac!=null&&twistState!=null&&twistState.transitions.Length==3&&ac.layers[0].stateMachine.anyStateTransitions.Any(t=>t.destinationState==twistState&&!t.canTransitionToSelf),"Twist state: AnyState entry and 3 exits "+key);
                float plaza=Vector2.Distance(new Vector2(d.transform.position.x,d.transform.position.z),new Vector2(KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,0],KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,1]));
                Check(plaza<KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,3]&&Mathf.Abs(Ground(d.transform.position.x,d.transform.position.z)-KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,2])<.05f,"disc rests on the flat clearing "+key);
                Check(d.transform.position.y-ParkOrigin().y>=2f,"disc starts out on the plaza, not in the toy park (IMP-DOG-019) "+key);
                // DES-DOG-005 M4 (design F / G M4): the circle is gone - radius 0 (Course/92). The centre stays the plaza's (harness anchor).
                Check(c.playAreaRadius==0f&&Vector2.Distance(new Vector2(c.playAreaCenter.x,c.playAreaCenter.z),new Vector2(KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,0],KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,1]))<1e-3f,"play area radius 0 (no circle, DES-DOG-005 M4), centre = plaza flat "+key);
                Check(d.GetComponent<VRCObjectSync>()!=null&&d.GetComponent<VRCPickup>()!=null&&d.aimLine!=null&&d.aimMarker!=null&&d.discHeldMaterial!=null&&d.discRenderer!=null&&Mathf.Abs(d.respawnBelowY-desc0().RespawnHeightY)<1e-3f,"disc components, aim preview, held material, respawn height "+key);
                var cb=UdonSharpEditorUtility.GetBackingUdonBehaviour(c);object bv;
                Check(cb.publicVariables.TryGetVariableValue("dogRoot",out bv)&&(bv as Transform)==c.dogRoot,"catcher backing dogRoot "+key);
                bool factsOk=cb.publicVariables.TryGetVariableValue("mouthOffsetCatch",out bv)&&(Vector3)bv==c.mouthOffsetCatch
                    &&cb.publicVariables.TryGetVariableValue("mouthOffsetStand",out bv)&&(Vector3)bv==c.mouthOffsetStand
                    &&cb.publicVariables.TryGetVariableValue("clipCrouchDuration",out bv)&&(float)bv==c.clipCrouchDuration
                    &&cb.publicVariables.TryGetVariableValue("clipRiseDuration",out bv)&&(float)bv==c.clipRiseDuration
                    &&cb.publicVariables.TryGetVariableValue("clipRecoverDuration",out bv)&&(float)bv==c.clipRecoverDuration
                    &&cb.publicVariables.TryGetVariableValue("maxRootRise",out bv)&&(float)bv==c.maxRootRise
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
                // Round 2 (intercept-first) promised the catch with a shared 9.5 m/s sprint. PCVR 2026-09-26 ("全体的に足早すぎ、
                // 特に小型犬"): the chase run is the breed's own now (IMP-DOG-017 table), proxy and backing.
                Check(Mathf.Abs(c.chaseRunSpeed-KH_FrisbeePrototypeEditor.ChaseRunSpeedFor(key))<1e-4f&&cb.publicVariables.TryGetVariableValue("chaseRunSpeed",out bv)&&bv is float&&Mathf.Abs((float)bv-c.chaseRunSpeed)<1e-5f,
                    "chase run "+c.chaseRunSpeed.ToString("F1")+" m/s == breed table "+KH_FrisbeePrototypeEditor.ChaseRunSpeedFor(key).ToString("F1")+" (PCVR 2026-09-26), proxy and backing "+key);
                // Round 4: bands, not floors - too low and the ETA fails, too high and the dog reads as a machine.
                Check(c.chaseAcceleration>=10f&&c.chaseAcceleration<=18f,"chase acceleration "+c.chaseAcceleration.ToString("F1")+" m/s2 in [10, 18] ("+(c.chaseRunSpeed/c.chaseAcceleration).ToString("F2")+" s to the run) "+key);
                Check(c.settleTime>=0.1f&&c.settleTime<=0.6f,"settle time "+c.settleTime.ToString("F2")+" s in [0.10, 0.60] "+key);
                Check(c.turnSpeedDegPerSec>=180f&&c.turnSpeedDegPerSec<=360f,"turn rate "+c.turnSpeedDegPerSec.ToString("F0")+" deg/s in [180, 360] (a dog's turn, not a turret's) "+key);
                // IMP-DOG-017: the hook loop grid (Hook*) is gone; the per-breed trio from the Build Scene table replaces it.
                // Sprint: B (追いつき) only, between the chase run and a real dog's burst. Launch turn: from a standstill,
                // between the running turn and a spin. Jump floor: min(0.25, 0.6 x standing mouth), below the ceiling.
                Check(c.chaseSprintSpeed>=c.chaseRunSpeed&&c.chaseSprintSpeed<=11f&&Mathf.Abs(c.chaseSprintSpeed-KH_FrisbeePrototypeEditor.ChaseSprintSpeedFor(key))<1e-4f,
                    "chase-down sprint "+c.chaseSprintSpeed.ToString("F1")+" m/s in [chaseRunSpeed "+c.chaseRunSpeed.ToString("F1")+", 11.0], == breed table "+key);
                Check(c.launchTurnDegPerSec>=240f&&c.launchTurnDegPerSec<=720f&&Mathf.Abs(c.launchTurnDegPerSec-KH_FrisbeePrototypeEditor.LaunchTurnDegPerSec)<1e-4f,"launch turn "+c.launchTurnDegPerSec.ToString("F0")+" deg/s in [240, 720], == code-owned "+KH_FrisbeePrototypeEditor.LaunchTurnDegPerSec.ToString("F0")+" (Codex 4) "+key);
                float floorRise=KH_FrisbeePrototypeEditor.JumpFloorRiseFor(facts);
                Check(Mathf.Abs(c.jumpFloorRise-floorRise)<1e-3f&&c.jumpFloorRise<c.maxRootRise,"jump floor "+c.jumpFloorRise.ToString("F3")+" == min(0.25, 0.6 x stand mouth "+facts.mouthOffsetStand.y.ToString("F3")+") = "+floorRise.ToString("F3")+", < maxRootRise "+c.maxRootRise.ToString("F2")+" "+key);
                Check(cb.publicVariables.TryGetVariableValue("chaseSprintSpeed",out bv)&&bv is float&&Mathf.Abs((float)bv-c.chaseSprintSpeed)<1e-5f
                    &&cb.publicVariables.TryGetVariableValue("launchTurnDegPerSec",out bv)&&bv is float&&Mathf.Abs((float)bv-c.launchTurnDegPerSec)<1e-5f
                    &&cb.publicVariables.TryGetVariableValue("jumpFloorRise",out bv)&&bv is float&&Mathf.Abs((float)bv-c.jumpFloorRise)<1e-5f,"catcher backing: sprint / launch turn / jump floor "+key);
                if(c.minRootRise>c.jumpFloorRise+1e-4f)log.AppendLine("INFO minRootRise "+c.minRootRise.ToString("F3")+" > jumpFloorRise "+c.jumpFloorRise.ToString("F3")+" (the playback floor decides the lowest jump) "+key);
                Check(KH_FrisbeeCatch.HookMinPace>0f&&KH_FrisbeeCatch.HookMinPace<c.chaseRunSpeed,"approach pace floor "+KH_FrisbeeCatch.HookMinPace.ToString("F1")+" m/s in (0, chaseRunSpeed) "+key);
                Check(c.chaseDeceleration>0f&&c.chaseDeceleration<c.deceleration,"chase braking "+c.chaseDeceleration.ToString("F1")+" m/s2 < the generic "+c.deceleration.ToString("F1")+" "+key);
                Check(c.standTurnDegPerSec<=c.walkTurnDegPerSec&&c.walkTurnDegPerSec<=c.turnSpeedDegPerSec&&c.standTurnDegPerSec>c.idleTurnSpeedDegPerSec,"turn rates stand "+c.standTurnDegPerSec.ToString("F0")+" <= walk "+c.walkTurnDegPerSec.ToString("F0")+" <= run "+c.turnSpeedDegPerSec.ToString("F0")+", all above idle "+key);
                // PCVR 2026-09-26 "大型犬は一歩がでかい感じの速さにしたい": the cap is the breed's own; [1.5, 3.0] stays as a sanity bound.
                Check(Mathf.Abs(c.maxRunAnimScale-KH_FrisbeePrototypeEditor.MaxRunAnimScaleFor(key))<1e-4f&&c.maxRunAnimScale>=1.5f&&c.maxRunAnimScale<=3f
                    &&cb.publicVariables.TryGetVariableValue("maxRunAnimScale",out bv)&&bv is float&&Mathf.Abs((float)bv-c.maxRunAnimScale)<1e-5f,
                    "run clip speed cap "+c.maxRunAnimScale.ToString("F1")+"x == breed table "+KH_FrisbeePrototypeEditor.MaxRunAnimScaleFor(key).ToString("F1")+", within [1.5, 3.0], proxy and backing "+key);
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
                Check(c.readyGraceSeconds>=1f&&c.readyGraceSeconds<=6f,"ready grace "+c.readyGraceSeconds.ToString("F1")+" s in [1, 6] (round 5: 2 s) "+key);
                Check(c.readyGraceDistance>=1f&&c.readyGraceDistance<=5f,"ready grace distance "+c.readyGraceDistance.ToString("F1")+" m in [1, 5] (arm's reach) "+key);
                Check(cb.publicVariables.TryGetVariableValue("readyGraceSeconds",out bv)&&Mathf.Abs((float)bv-c.readyGraceSeconds)<1e-5f
                    &&cb.publicVariables.TryGetVariableValue("readyGraceDistance",out bv)&&Mathf.Abs((float)bv-c.readyGraceDistance)<1e-5f,"catcher backing: ready grace pair "+key);
                Check(c.readyGraceDistance<c.standoffDistance,"the grace ends well before the stand-off spot ("+c.readyGraceDistance.ToString("F1")+" m < "+c.standoffDistance.ToString("F1")+" m) "+key);
                // BUG-DOG-003: the window a launch notice may wait for the pickup sync to catch up. Long enough to
                // cover the arrival-order race, short enough that the remaining flight is still worth chasing.
                Check(KH_FrisbeeCatch.PendingHoldSeconds>=0.5f&&KH_FrisbeeCatch.PendingHoldSeconds<=2f,"pending launch hold "+KH_FrisbeeCatch.PendingHoldSeconds.ToString("F2")+" s in [0.5, 2] "+key);
                // The harness-only held override must ship at 0 in every scene, in the proxy AND on the backing
                // heap (that is the value Udon actually runs). A missing symbol or a different type is a FAILURE,
                // not a zero: it would mean the field was renamed or retyped and this check stopped proving anything.
                Check(c.debugHeldOverride==0&&cb.publicVariables.TryGetVariableValue("debugHeldOverride",out bv)&&bv is int&&(int)bv==0,"held override off (harness only), proxy and backing "+key);
                Check(KH_FrisbeePrototypeEditor.PetStatesOk(),"petting resumes in Idle / Ready / Drop and stays suppressed in every moving state (PetAllowed) "+key);
                // round 5 (PCVR 2026-09-22): 跳ぶ距離の帯・全プリセットの追い抜き (IMP-DOG-017 で早着の上限と薄い立ち取りの直進は廃止)
                Check(KH_FrisbeeCatch.RunJumpMaxTakeoff>=5f&&KH_FrisbeeCatch.RunJumpMaxTakeoff<=9f
                    &&KH_FrisbeeCatch.RunJumpMinTakeoff<KH_FrisbeeCatch.RunJumpHeadOnMaxTakeoff
                    &&KH_FrisbeeCatch.RunJumpHeadOnMaxTakeoff<KH_FrisbeeCatch.RunJumpMaxTakeoff,
                    "take-off band "+KH_FrisbeeCatch.RunJumpMinTakeoff.ToString("F1")+" / head-on "+KH_FrisbeeCatch.RunJumpHeadOnMaxTakeoff.ToString("F1")+" / "+KH_FrisbeeCatch.RunJumpMaxTakeoff.ToString("F1")+" m/s ordered, ceiling in [5, 9] "+key);
                Check(KH_FrisbeeCatch.OvertakeMinPace>c.runSpeed&&KH_FrisbeeCatch.OvertakeMinPace<=c.chaseRunSpeed,"overtake pace floor "+KH_FrisbeeCatch.OvertakeMinPace.ToString("F1")+" m/s above the walking run and within the sprint "+key);
                // DES-DOG-005 M1 (0-9): the Ready-spot-within-the-radius check is retired (C-3: the spot is chosen by PlayableAt,
                // the catcher no longer reads the circle), and Codex D1's circle landing check is replaced by the asymmetric arc
                // (the same statics the planner and the liftoff gate call). M4 removed the circle itself (radius 0, checked above).
                {
                    string asym=KH_FrisbeePrototypeEditor.AsymmetricLandingProblems();
                    Check(asym.Length==0,"asymmetric landing (DescentTime / AirTime / LandingOf / SlopeOk) "+key+(asym.Length==0?"":" -- "+asym));
                    string anywhere=KH_FrisbeePrototypeEditor.AnywhereProblems(c);
                    Check(anywhere.Length==0,"DES-DOG-005 values == defaults, proxy and backing (Course/92) "+key+(anywhere.Length==0?"":" -- "+anywhere));
                }
                var wb=UdonSharpEditorUtility.GetBackingUdonBehaviour(w);var fb=UdonSharpEditorUtility.GetBackingUdonBehaviour(f);var cbb=UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
                Check(wb.publicVariables.TryGetVariableValue("frisbeeCatch",out bv)&&(bv as UnityEngine.Object)==cbb&&fb.publicVariables.TryGetVariableValue("frisbeeCatch",out bv)&&(bv as UnityEngine.Object)==cbb,"Wander/Fetch backing frisbeeCatch "+key);
                var sleepBoardV=root.transform.Find("KD_SleepBoard")?.GetComponent<KD_SleepBoard>();
                var liveV=SceneLive(out var liveSpotV);
                Check(f.sleepBoard==sleepBoardV&&f.liveSystem==liveV&&w.liveSystem==liveV&&w.liveSpot==liveSpotV,"sleep board = KH_Dogs/KD_SleepBoard when built (IMP-DOG-016, else null) / live = the scene KD_LiveSystem and KD_LiveDogSpot if any (else null) for "+key);
            }
            // IMP-DOG-012: six discs in the row behind the balls. Placement is human-owned once a disc exists (Build only
            // places a NEW one at DiscHome(i); the Lab disc keeps wherever it already rests), so this checks that no two
            // discs sit on top of each other rather than pinning them to DiscHome.
            {
                var discPos=FrisbeeKeys.Select(k=>root.transform.Find("KH_Frisbee_"+k)).Where(t=>t!=null).Select(t=>t.position).ToArray();
                float minGap=float.MaxValue;for(int a=0;a<discPos.Length;a++)for(int b=a+1;b<discPos.Length;b++)minGap=Mathf.Min(minGap,Vector3.Distance(discPos[a],discPos[b]));
                Check(discPos.Length==FrisbeeKeys.Length&&(discPos.Length<2||minGap>=0.3f),"all "+FrisbeeKeys.Length+" discs present and apart (closest pair "+(discPos.Length<2?"-":minGap.ToString("F2"))+" m >= 0.30)");
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
                // The walk must never buzz from two places at once: only this script may call the haptic API for the leash.
                // KH_WashTool.cs (IMP-DOG-024 wash tools, 2026-10-03) buzzes the hand holding a sponge / shower - a different held thing, never
                // on the leash hand at the same time (a hand holds one pickup). KH_HandWash.cs (IMP-DOG-039 bare-hand lather) buzzes only
                // an EMPTY hand touching foam (a hand holding the leash or a tool is not free), so it never shares a hand with the other two.
                var callers=Directory.GetFiles("Assets/KominkaHillside","*.cs",SearchOption.AllDirectories)
                    .Where(p=>!p.Replace('\\','/').Contains("/Editor/")&&File.ReadAllText(p).Contains("PlayHaptic"+"EventInHand"))
                    .Select(Path.GetFileName).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
                Check(callers.SequenceEqual(new[]{"KH_HandWash.cs","KH_LeashHaptics.cs","KH_WashTool.cs"}),"PlayHapticEventInHand called from KH_HandWash.cs (empty hands on foam), KH_LeashHaptics.cs (leash) and KH_WashTool.cs (wash tools) only ("+string.Join(", ",callers)+")");
            }
            bool slotsOk=m.stationSlots!=null&&m.stationSlots.Length==6&&m.stationSlots.All(x=>x!=null)&&m.stationSlots.Distinct().Count()==6&&m.slotHand.All(h=>h==0||h==1)
                &&m.stationOf.All(s=>s>=0&&s<3)&&Enumerable.Range(0,3).All(s=>Enumerable.Range(0,2).All(h=>Enumerable.Range(0,6).Count(k=>m.stationOf[k]==s&&m.slotHand[k]==h)==1))
                &&Enumerable.Range(0,6).All(k=>m.stationSlots[k].parent==root.transform.Find(StationNames[m.stationOf[k]]));
            Check(slotsOk,"three stations (entrance / viewpoint / plaza edge): 1 left + 1 right each, slots parented to their station");
            Check(StationNames.All(st=>{var t=root.transform.Find(st);return t!=null&&t.Find("SlotL2")==null&&t.Find("SlotR2")==null;}),"no stale four-slot layout (SlotL2 / SlotR2) left on any station");
            bool toolsHome=true;for(int i=0;i<6;i++){var c=m.localClips[i];if(c==null||c.desktopHand!=i/3||Vector3.Distance(c.transform.position,m.stationSlots[(i/3)*3+(i%3)].position)>.01f)toolsHome=false;}
            Check(toolsHome&&m.localClips.Select(c=>c.transform.position).Distinct().Count()==6,"six tools rest one per slot with matching side, no two at one spot");
            Check(act!=null&&act.gameObject==m.gameObject,"gather authority shares the manager GameObject (single Owner)");
            var desc=UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();Check(desc.NetworkIDCollection.Count(x=>x.gameObject==null)==0,"no dangling NetworkIDs");
            Check(desc.NetworkIDCollection.Select(x=>x.ID).Distinct().Count()==desc.NetworkIDCollection.Count&&desc.NetworkIDCollection.Where(x=>x.gameObject!=null).Select(x=>x.gameObject).Distinct().Count()==desc.NetworkIDCollection.Count(x=>x.gameObject!=null),"NetworkIDs unique per ID and per GameObject");
            var networked=root.GetComponentsInChildren<VRCObjectSync>(true).Select(x=>x.gameObject).Concat(root.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Where(u=>u.SyncMethod!=Networking.SyncType.None).Select(u=>u.gameObject)).Distinct().ToArray();
            Check(networked.All(g=>desc.NetworkIDCollection.Any(x=>x.gameObject==g)),"every synced object under KH_Dogs has a NetworkID ("+networked.Length+")");
            foreach(var st in StationNames){var t=root.transform.Find(st);Check(t!=null&&t.Find("Crossbar")!=null&&Mathf.Abs(t.position.y-.8f-Ground(t.position.x,t.position.z))<.3f,"station "+st+" placed at rack height");}
            {   // DES-DOG-003: the plaza station sits on the plaza flat but not at its centre (edge band 6..12 m; the user may nudge it within that band)
                var t=root.transform.Find(PlazaStation);float cx=KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,0],cz=KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,1];
                float d=t==null?-1f:Vector2.Distance(new Vector2(t.position.x,t.position.z),new Vector2(cx,cz));
                Check(d>=6f&&d<=12.5f,"plaza station on the plaza edge, not the centre (distance from centre "+d.ToString("F1")+" m, expected 6..12.5)");
            }
            Check(root.transform.Find(KH_WalkRouteEditor.RetiredSignsRoot)==null,"temporary destination signs removed (destinations live in the hand menu)");
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
