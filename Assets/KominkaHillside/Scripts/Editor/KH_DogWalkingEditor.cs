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
        // BUG-DOG-001: balls rest on the flat upper clearing (KH_TerrainPrototypeConfig.Flats[PlazaFlat], y=19 within r=12),
        // off the trail end and away from the shade tree. VRCObjectSync respawns to this start position.
        public static readonly Vector2 BallHomeCenter = new Vector2(KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,0]+3.5f, KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat,1]+3.5f);
        public const float BallSpacing=0.9f, BallRestHeight=0.07f;
        public static Vector3 BallHome(int i)
        {
            float x=BallHomeCenter.x+((i%3)-1)*BallSpacing, z=BallHomeCenter.y+((i/3)-0.5f)*BallSpacing;
            return new Vector3(x,Ground(x,z)+BallRestHeight,z);
        }
        static void Report(string name, string text) { Directory.CreateDirectory("Temp/KH_DogPort"); File.WriteAllText("Temp/KH_DogPort/"+name+".txt",text); Debug.Log("[KH Walking] " + text); }
        static void Guard()
        {
            if (Application.isPlaying || EditorSceneManager.GetActiveScene().path != WorldScene)
                throw new InvalidOperationException("Open the hillside scene in Edit mode.");
        }

        [MenuItem("Tools/KominkaHillside/Walking/1 Create Programs")]
        public static void Programs()
        {
            foreach (string name in new[]{"KH_LeashManager","KH_LeashClip","KH_LeashCollar","KH_WalkRoute","KH_WalkSign"})
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
            manager.leashLength=3f;manager.followSpeed=4.8f;manager.catchUpDistance=10f;manager.defaultDestination=KH_WalkRouteEditor.DestinationPlaza;
            manager.trailOffDistance=6f;manager.trailOnDistance=4f;manager.entryMaxPath=12f;manager.inviteAfter=8f;manager.inviteInterval=14f;manager.inviteLimit=3;manager.detourChance=.25f;
            var existingRoute=root.GetComponentInChildren<KH_WalkRoute>(true);manager.route=existingRoute;
            var pack=Find<KD_DogPack>(root,"KD_DogPack");
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
                w.groundLayerMask=(1<<0)|(1<<11);w.hillsideLeashes=manager;w.dogIndex=i;
                f.hillsideLeashes=manager;f.dogIndex=i;f.ballController=b;b.fetchController=f;
                w.fetchController=f;w.petController=p;p.fetchController=f;p.wanderController=w;
                f.pack=pack;w.pack=pack;
                // Omitted room/stage/services stay null in this world.
                f.sleepBoard=null;f.liveSystem=null;f.activities=null;f.photoDirector=null;
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
            pack.dogRoots=roots;pack.dogFetch=fetch;
            var rack=Child(root.transform,"KH_LeashRack");
            if(initial)rack.position=new Vector3(45,Ground(45,43)+.8f,43);
            KH_LeashModelsEditor.MakeRack(rack,Mat("KH_RackWood",new Color(.32f,.19f,.10f)));
            for(int i=0;i<6;i++)
            {
                var t=rack.Find("Clip"+i);bool newClip=t==null;
                if(newClip){var go=GameObject.CreatePrimitive(PrimitiveType.Capsule);go.name="Clip"+i;t=go.transform;t.SetParent(rack,false);t.localPosition=new Vector3((i-2.5f)*.18f,0,0);t.localScale=new Vector3(.07f,.12f,.07f);}
                t.gameObject.layer=14;
                var rb=t.GetComponent<Rigidbody>();if(rb==null)rb=t.gameObject.AddComponent<Rigidbody>();rb.isKinematic=true;rb.useGravity=false;
                var pickup=t.GetComponent<VRC_Pickup>();if(pickup==null)pickup=t.gameObject.AddComponent<VRCPickup>();
                pickup.AutoHold=VRC_Pickup.AutoHoldMode.Yes;pickup.proximity=.35f;pickup.InteractionText=i<3?"Leash tip - left":"Leash tip - right";
                var clip=Add<KH_LeashClip>(t.gameObject);clip.manager=manager;clip.pickup=pickup;clip.tip=KH_LeashModelsEditor.MakeTip(t,Data,neutral);clip.desktopHand=i<3?0:1;
                var indicator=t.Find("ReadyIndicator");
                clip.indicator=indicator!=null?indicator.GetComponent<Renderer>():t.GetComponent<Renderer>();
                clip.readyMaterial=ready;clip.blockedMaterial=neutral;clip.indicator.sharedMaterial=neutral;
                manager.localClips[i]=clip;
            }
            foreach(var u in root.GetComponentsInChildren<UdonSharpBehaviour>(true)){UdonSharpEditorUtility.CopyProxyToUdon(u);EditorUtility.SetDirty(u);}
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
            var pack=root.GetComponentInChildren<KD_DogPack>();
            for(int i=0;i<6;i++)
            {
                string key=Keys[i];var dog=Find<Transform>(root,"KD_Dog_"+key);var w=Find<KD_DogWanderController>(root,"KD_WanderSystem_"+key);var p=Find<KD_DogPetController>(root,"KD_PetSystem_"+key);var f=Find<KD_DogFetchController>(root,"KD_FetchSystem_"+key);
                Check(m.dogRoots[i]==dog && w.hillsideLeashes==m && f.hillsideLeashes==m,"leash wiring "+key);
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
