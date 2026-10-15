using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using VRC.SDKBase;
using VRC.SDK3.Components;
using KominkaDogRun;

namespace KominkaHillside.Editor
{
    // Targeted additive setup. It never rebuilds the terrain, dogs or other shows.
    public static partial class KH_HalloweenParadeEditor
    {
        const string Menu="Tools/KominkaHillside/Parade/", RootName="KH_HalloweenParade", ScriptDir="Assets/KominkaHillside/Scripts/";
        [Serializable] class Route { public Vector3 stage;public float yaw,startRouteDistance;public Vector3[] points; }
        [Serializable] class Words {public Language[] languages;}
        [Serializable] class Language {public string code;public string[] words;}
        [Serializable] class Restoration {public Effect[] systems;}
        [Serializable] class Effect {public string instancePath,phraseId;public float startSeconds,endSeconds;public uint seed;public int burstCount;public bool shapeEnabled;public float[] shapeScale,shapePosition,velocity;}
        static readonly int[] Ranks={0,1,4,2,3,5};
        static bool _recordUndo;
        static void SceneTransaction(string name,Action apply)
        {
            Guard();Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName(name);
            foreach(var b in All<UdonSharpBehaviour>())
            {
                if(b is KH_HalloweenParade||b is KH_HalloweenParadeDog||b is KH_HalloweenParadePanel||b is KH_HalloweenParadePlayback||
                   b is KH_LeashManager||b is KD_GroupActivities||b is KH_WashStationCall||b is KD_LiveSystem||b is KH_DogWash||b is KD_DogWanderController||b is KD_BgmPlayer)
                {Undo.RecordObject(b,name);var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(b);if(backing!=null)Undo.RecordObject(backing,name);}
            }
            _recordUndo=true;
            try{apply();Undo.CollapseUndoOperations(group);}
            catch{Undo.RevertAllDownToGroup(group);throw;}
            finally{_recordUndo=false;}
        }
        static void Guard(bool saved=true)
        {
            var s=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode||s.path!="Assets/Scenes/VRCDefaultWorldScene.unity"||(saved&&s.isDirty))
                throw new InvalidOperationException("Saved hillside scene in Edit mode required");
        }
        static T[] All<T>() where T:Component {return UnityEngine.Object.FindObjectsOfType<T>(true).Where(x=>x.gameObject.scene==SceneManager.GetActiveScene()).ToArray();}
        static T Only<T>() where T:Component {return All<T>().Single();}
        static T U<T>(GameObject go,Networking.SyncType sync) where T:UdonSharpBehaviour
        {var b=go.GetComponent<T>();if(b==null){b=go.AddUdonSharpComponent<T>();if(_recordUndo){Undo.RegisterCreatedObjectUndo(b,"Parade component");Undo.RegisterCreatedObjectUndo(UdonSharpEditorUtility.GetBackingUdonBehaviour(b),"Parade backing");}}UdonSharpEditorUtility.GetBackingUdonBehaviour(b).SyncMethod=sync;return b;}
        static void Copy(UdonSharpBehaviour b){EditorUtility.SetDirty(b);UdonSharpEditorUtility.CopyProxyToUdon(b);}
        static Transform Child(Transform p,string name,Vector3 position)
        {var t=new GameObject(name).transform;t.SetParent(p,false);t.position=position;if(_recordUndo)Undo.RegisterCreatedObjectUndo(t.gameObject,"Parade object");return t;}
        static Route ReadRoute(){return JsonUtility.FromJson<Route>(File.ReadAllText("docs/plans/halloween-parade/production-route.json"));}

        [MenuItem(Menu+"2 - Create Program Assets")]
        public static void Programs()
        {
            Guard();
            foreach(var name in new[]{"KH_HalloweenParade","KH_HalloweenParadeDog","KH_HalloweenParadePlayback","KH_HalloweenParadePanel"})
            {
                var path=ScriptDir+name+".asset";
                if(AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path)!=null)continue;
                var source=AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptDir+name+".cs");
                if(source==null||source.GetClass()==null)throw new Exception("Compile C# before creating programs: "+name);
                var asset=ScriptableObject.CreateInstance<UdonSharpProgramAsset>();asset.sourceCsScript=source;AssetDatabase.CreateAsset(asset,path);
            }
            AssetDatabase.SaveAssets();Debug.Log("PARADE_PROGRAMS_CREATED: compile Udon before setup");
        }

        [MenuItem(Menu+"3 - Apply Runtime and Panel")]
        public static void Apply()
        {SceneTransaction("Create Halloween parade",ApplyCore);}
        static void ApplyCore()
        {
            Guard();if(GameObject.Find(RootName)!=null)throw new Exception("Parade already exists; use a targeted repair");
            var leash=Only<KH_LeashManager>();var live=Only<KD_LiveSystem>();var language=Only<KD_Language>();var treats=Only<KD_TreatBox>();
            var washes=All<KH_DogWash>();var stations=All<KH_WashStationCall>();
            var route=ReadRoute();var words=JsonUtility.FromJson<Words>(File.ReadAllText("Assets/KominkaHillside/Data/ParadeWords.json"));
            if(leash.dogRoots.Length!=6||leash.fetchers.Length!=6||route.points.Length<3||words.languages.Length!=13||words.languages.Any(x=>x.words.Length!=35))throw new Exception("Incomplete data");
            if(leash.activities==null||leash.activities.gameObject!=leash.gameObject)throw new Exception("Shared owner group mismatch");
            var wander=new KD_DogWanderController[6];var wash=new KH_DogWash[6];
            for(int i=0;i<6;i++)
            {
                var dog=leash.dogRoots[i];wander[i]=All<KD_DogWanderController>().Single(w=>w.dogRoot==dog);
                wash[i]=washes.Single(w=>w.dogRoot==dog);
                if(leash.fetchers[i]==null||leash.frisbeeCatchers[i]==null)throw new Exception("Missing dog state "+i);
            }
            var root=new GameObject(RootName).transform;
            if(_recordUndo)Undo.RegisterCreatedObjectUndo(root.gameObject,"Parade root");
            var manager=U<KH_HalloweenParade>(leash.gameObject,Networking.SyncType.Manual);
            manager.leashes=leash;manager.activities=leash.activities;manager.sleepBoard=leash.sleepBoard;
            manager.treatBox=treats;manager.washStations=stations;manager.liveSystem=live;
            manager.developmentEnabled=false;manager.durationSeconds=220;manager.consentSeconds=45;manager.preparationSeconds=15;manager.recoverySeconds=30;
            manager.marchStartSeconds=25;manager.marchEndSeconds=130;manager.entranceEndSeconds=150;manager.dogSpacing=1.8f;
            manager.startRouteDistance=route.startRouteDistance;manager.processionRanks=(int[])Ranks.Clone();
            manager.routePoints=route.points.Select((p,i)=>Child(root,"Route_"+i,p)).ToArray();
            manager.startSlots=new Transform[6];manager.finaleSlots=new Transform[6];manager.dogs=new KH_HalloweenParadeDog[6];
            var yaw=Quaternion.Euler(0,route.yaw,0);float[] x={2.8f,1.65f,.5f,-.65f,-1.8f,-2.95f},z={-.1f,-.65f,-.75f,-.5f,.7f,.9f};
            for(int i=0;i<6;i++)
            {
                Vector3 tangent;int rank=Ranks[i];
                manager.startSlots[i]=Child(root,"Start_"+i,AtDistance(route.points,route.startRouteDistance-rank*1.8f,out tangent));
                tangent.y=0;manager.startSlots[i].rotation=Quaternion.LookRotation(tangent);
                manager.finaleSlots[i]=Child(root,"Finale_"+i,route.stage+yaw*new Vector3(x[rank],0,z[rank]));
                manager.finaleSlots[i].rotation=Quaternion.Euler(0,route.yaw+180,0);
                var d=U<KH_HalloweenParadeDog>(leash.fetchers[i].gameObject,Networking.SyncType.Manual);
                d.parade=manager;d.dogIndex=i;d.root=leash.dogRoots[i];d.startSlot=manager.startSlots[i];d.recoverySlot=manager.finaleSlots[i];
                d.wander=wander[i];d.fetch=leash.fetchers[i];d.frisbee=leash.frisbeeCatchers[i];d.wash=wash[i];manager.dogs[i]=d;
                wander[i].paradeDog=d;wash[i].paradeDog=d;Copy(d);Copy(wander[i]);Copy(wash[i]);
            }
            leash.parade=manager;leash.activities.parade=manager;live.parade=manager;
            foreach(var station in stations){station.parade=manager;Copy(station);}
            Copy(leash);Copy(leash.activities);Copy(live);Copy(manager);
            var playback=U<KH_HalloweenParadePlayback>(Child(root,"LocalPlayback",Vector3.zero).gameObject,Networking.SyncType.None);
            playback.parade=manager;playback.bgmPlayer=Only<KD_BgmPlayer>();playback.audienceRoute=route.points;playback.fullVolumeDistance=14;playback.silentDistance=28;playback.baseVolume=.55f;Copy(playback);
            foreach(var bgm in All<KD_BgmPlayer>()){bgm.paradePlayback=playback;Copy(bgm);}
            BuildPanel(root,manager,playback,language,words,route);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("PARADE_APPLIED: six adapters, route, local panel. Verify before saving. Audio/Timeline binding separate.");
        }
        static Vector3 AtDistance(Vector3[] points,float distance,out Vector3 tangent)
        {
            tangent=points[1]-points[0];
            for(int i=1;i<points.Length;i++){tangent=points[i]-points[i-1];float length=tangent.magnitude;if(distance<=length)return Vector3.Lerp(points[i-1],points[i],Mathf.Clamp01(distance/length));distance-=length;}
            return points[points.Length-1];
        }
        static RectTransform Rect(Transform p,string name,Vector2 pos,Vector2 size)
        {var go=new GameObject(name,typeof(RectTransform));var r=go.GetComponent<RectTransform>();r.SetParent(p,false);r.sizeDelta=size;r.anchoredPosition=pos;if(_recordUndo)Undo.RegisterCreatedObjectUndo(go,"Parade UI");return r;}
        static TextMeshProUGUI Text(Transform p,string name,string value,Vector2 pos,Vector2 size,float fontSize)
        {
            var t=Rect(p,name,pos,size).gameObject.AddComponent<TextMeshProUGUI>();t.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/KominkaHillside/Font/KD_NotoSerif SDF.asset");
            t.text=value;t.fontSize=fontSize;t.enableAutoSizing=true;t.fontSizeMin=fontSize*.8f;t.fontSizeMax=fontSize;
            t.color=new Color(.96f,.94f,.88f);t.alignment=TextAlignmentOptions.Center;t.raycastTarget=false;return t;
        }
        static GameObject Canvas(Transform p,string name,Vector2 size)
        {
            var r=Rect(p,name,Vector2.zero,size);r.localScale=Vector3.one*.001f;
            r.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;r.gameObject.AddComponent<GraphicRaycaster>();r.gameObject.AddComponent<VRCUiShape>();
            var box=r.gameObject.AddComponent<BoxCollider>();box.size=new Vector3(size.x,size.y,10);box.isTrigger=true;
            var bg=r.gameObject.AddComponent<Image>();bg.color=new Color(.09f,.16f,.12f,.97f);bg.raycastTarget=false;return r.gameObject;
        }
        static Button Button(Transform p,string name,Vector2 pos,Vector2 size,KH_HalloweenParadePanel panel,string method,out TextMeshProUGUI text)
        {
            var r=Rect(p,name,pos,size);var image=r.gameObject.AddComponent<Image>();image.color=new Color(.25f,.31f,.23f);image.sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/KominkaHillside/Textures/UI/KD_UiRound.png");image.type=Image.Type.Sliced;var b=r.gameObject.AddComponent<Button>();b.targetGraphic=image;
            text=Text(r,"Label",name,Vector2.zero,size-new Vector2(24,8),25);
            UnityEventTools.AddStringPersistentListener(b.onClick,UdonSharpEditorUtility.GetBackingUdonBehaviour(panel).SendCustomEvent,method);return b;
        }
        static void BuildPanel(Transform root,KH_HalloweenParade manager,KH_HalloweenParadePlayback playback,KD_Language language,Words words,Route route)
        {
            var panel=U<KH_HalloweenParadePanel>(Child(root,"LocalPanel",Vector3.zero).gameObject,Networking.SyncType.None);
            panel.parade=manager;panel.playback=playback;panel.language=language;panel.languageCodes=words.languages.Select(x=>x.code).ToArray();panel.words=words.languages.SelectMany(x=>x.words).ToArray();panel.wordCount=35;
            panel.dogNameKeys=new[]{"ui.extra.010","dog.pomeranian","dog.labrador","ui.extra.012","ui.extra.008","ui.extra.009"};panel.dogNames=new[]{"Corgi","Pomeranian","Labrador","Dachshund","Shiba","Samoyed"};
            panel.surface=Canvas(panel.transform,"Surface",new Vector2(820,580));var p=panel.surface.transform;
            panel.title=Text(p,"Title",words.languages[0].words[0],new Vector2(0,241),new Vector2(760,55),32);
            panel.body=Text(p,"Body",words.languages[0].words[19],new Vector2(0,95),new Vector2(744,195),27);
            panel.progress=Text(p,"Progress","",new Vector2(0,-30),new Vector2(744,36),22);
            var labels=new List<TextMeshProUGUI>();var keys=new List<int>();TextMeshProUGUI label;
            panel.startButton=Button(p,"Start",new Vector2(0,-99),new Vector2(330,65),panel,"_Start",out label);labels.Add(label);keys.Add(10);
            panel.requestButton=Button(p,"Request",new Vector2(0,-99),new Vector2(330,65),panel,"_Send",out label);labels.Add(label);keys.Add(11);
            panel.approveButton=Button(p,"Approve",new Vector2(-190,-99),new Vector2(340,65),panel,"_Approve",out label);labels.Add(label);keys.Add(13);
            panel.declineButton=Button(p,"Decline",new Vector2(190,-99),new Vector2(340,65),panel,"_Decline",out label);labels.Add(label);keys.Add(14);
            panel.cancelButton=Button(p,"Cancel",new Vector2(0,-177),new Vector2(330,58),panel,"_Cancel",out label);labels.Add(label);keys.Add(12);
            Button(p,"Reduced",new Vector2(-265,-246),new Vector2(250,48),panel,"_Reduced",out label);labels.Add(label);keys.Add(16);
            Button(p,"Mute",new Vector2(0,-246),new Vector2(250,48),panel,"_Mute",out label);labels.Add(label);keys.Add(17);
            Button(p,"Close",new Vector2(265,-246),new Vector2(250,48),panel,"_Close",out label);labels.Add(label);keys.Add(15);
            panel.resumeSurface=null; // Reopen through Live; no persistent following button.
            panel.buttonLabels=labels.ToArray();panel.buttonWords=keys.ToArray();panel._Refresh();panel.surface.SetActive(false);Copy(panel);
        }

        [MenuItem(Menu+"4 - Verify Runtime Wiring")]
        public static void Verify()
        {
            Guard(false);var m=Only<KH_HalloweenParade>();var p=Only<KH_HalloweenParadePlayback>();var ui=Only<KH_HalloweenParadePanel>();
            var errors=new List<string>();Action<bool,string> need=(ok,msg)=>{if(!ok)errors.Add(msg);};
            var route=ReadRoute();var words=JsonUtility.FromJson<Words>(File.ReadAllText("Assets/KominkaHillside/Data/ParadeWords.json"));
            need(m.dogs!=null&&m.dogs.Length==6,"six adapters");need(m.routePoints.Length==route.points.Length,"route point count");
            for(int i=0;i<Math.Min(m.routePoints.Length,route.points.Length);i++)need(Vector3.Distance(m.routePoints[i].position,route.points[i])<.001f,"route coordinate "+i);
            need(ui.languageCodes.SequenceEqual(words.languages.Select(x=>x.code))&&ui.words.SequenceEqual(words.languages.SelectMany(x=>x.words)),"source localization equality");
            need(!m.developmentEnabled,"public preview retired on 2026-10-15");VerifyLivePreviewEntry(errors);need(m.durationSeconds==220&&m.recoverySeconds==30&&Math.Abs(m.startRouteDistance-route.startRouteDistance)<.001f,"timing/route constants");
            need(p.audienceRoute!=null&&p.audienceRoute.SequenceEqual(route.points),"audience route matches source");
            need(m.leashes.parade==m&&m.activities.parade==m&&m.liveSystem.parade==m,"manager gates");
            for(int i=0;i<6;i++){var d=m.dogs[i];need(d!=null&&d.parade==m&&d.dogIndex==i,"adapter "+i);if(d==null)continue;
                need(d.root==m.leashes.dogRoots[i]&&d.gameObject==m.leashes.fetchers[i].gameObject,"existing owner state "+i);
                need(d.wander.paradeDog==d&&d.wash.paradeDog==d,"dog gates "+i);need(d.startSlot==m.startSlots[i]&&d.recoverySlot==m.finaleSlots[i],"slots "+i);
                Vector3 tangent;need(Vector3.Distance(m.startSlots[i].position,AtDistance(route.points,route.startRouteDistance-Ranks[i]*m.dogSpacing,out tangent))<.001f,"start slot coordinate "+i);
                need(UdonSharpEditorUtility.GetBackingUdonBehaviour(d).SyncMethod==Networking.SyncType.Manual,"adapter manual sync "+i);}
            need(m.washStations.All(s=>s.parade==m),"wash station gates");need(p.parade==m&&ui.parade==m&&ui.playback==p,"local references");
            need(ui.words.Length==455&&ui.languageCodes.Length==13&&ui.wordCount==35,"localization");need(ui.resumeSurface==null,"no following parade return");need(All<KD_BgmPlayer>().All(b=>b.paradePlayback==p),"regional BGM ducking");
            need(p.bgmPlayer==Only<KD_BgmPlayer>(),"existing hillside personal music volume source");
            need(p.bgmPlayer!=null&&(p.music==null||p.bgmPlayer.source!=p.music),"parade playback is the sole audio writer");
            Directory.CreateDirectory("Temp/halloween-parade");File.WriteAllText("Temp/halloween-parade/wiring.txt",errors.Count==0?"PASS runtime wiring (audio binding checked separately)":string.Join("\n",errors));
            if(errors.Count!=0)throw new Exception(string.Join("; ",errors));Debug.Log("PARADE_RUNTIME_WIRING_OK");
        }
        [MenuItem(Menu+"5 - Save Verified Scene")]
        public static void Save(){Verify();AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());Debug.Log("PARADE_SCENE_SAVED");}

        [MenuItem(Menu+"8 - Update Route and Words")]
        public static void UpdateData(){SceneTransaction("Update parade data",()=>{
            var m=Only<KH_HalloweenParade>();var ui=Only<KH_HalloweenParadePanel>();var p=Only<KH_HalloweenParadePlayback>();var route=ReadRoute();
            if(m.routePoints.Length!=route.points.Length)throw new Exception("Route topology changed: review placement before a targeted migration");
            for(int i=0;i<route.points.Length;i++){Undo.RecordObject(m.routePoints[i],"Route coordinate");m.routePoints[i].position=route.points[i];}
            m.startRouteDistance=route.startRouteDistance;p.audienceRoute=route.points;
            for(int i=0;i<6;i++){Vector3 tangent;Undo.RecordObject(m.startSlots[i],"Start slot");m.startSlots[i].position=AtDistance(route.points,route.startRouteDistance-Ranks[i]*m.dogSpacing,out tangent);tangent.y=0;m.startSlots[i].rotation=Quaternion.LookRotation(tangent);}
            var w=JsonUtility.FromJson<Words>(File.ReadAllText("Assets/KominkaHillside/Data/ParadeWords.json"));if(w.languages.Length!=13||w.languages.Any(x=>x.words.Length!=35))throw new Exception("Words schema");
            p.bgmPlayer=Only<KD_BgmPlayer>();ui.wordCount=35;
            ui.languageCodes=w.languages.Select(x=>x.code).ToArray();ui.words=w.languages.SelectMany(x=>x.words).ToArray();ui._Refresh();Copy(m);Copy(p);Copy(ui);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());Debug.Log("PARADE_DATA_UPDATE_OK");
        });}
    }
}
