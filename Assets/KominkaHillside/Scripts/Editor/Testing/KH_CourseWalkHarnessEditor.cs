using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    // ClientSim walking through real menu events and runtime Udon heaps. No scene save.
    [InitializeOnLoad]
    public static partial class KH_CourseWalkHarnessEditor
    {
        const string Key="KH.CourseWalkHarness",Dir="Logs/kh-course-walk",Journal=Dir+"/journal.json";
        [Serializable] public class DogResult
        {
            public int index,routeChanges,forwardSteps,reverseSteps,forwardWraps,reverseWraps,minNext=999,maxNext=-1;
            public float travelled,endDistance,maxBridgeHeightError,maxBridgeZError,maxObservedStep;
            public bool crossedBridge,reached,choiceObserved,returnChoiceObserved,arrived;
            public int returnForwardSteps,returnReverseSteps;
            public Vector3 start,end;
            public int visitedRouteNodes;
            public bool[] visited;
        }
        [Serializable] public class Trace
        {public double seconds;public int dog,next,mode,choice,resume;public Vector3 player,position;}
        [Serializable] public class MovementFailure
        {
            public int dog,frame,next,mode,resume,groundMask,hitLayer;
            public double seconds,observationGap;
            public float deltaTime,speed,groundOffset,groundDistance;
            public Vector3 previous,current,delta,player,rayOrigin,hitPoint,hitNormal,navPoint;
            public bool groundHit,navHit;
            public string colliderPath,colliderType,diagnosticError;
        }
        [Serializable] public class Case
        {
            public string name,status="PENDING",reason;
            public int count,choice,startNode,endNode,requiredLinks,holderLinks;
            public bool bridge,returning,midwayObserved,returnObserved,reconnectObserved;
            public int returnDirection;
            public double returnStartedAt,arrivalStartedAt;
            public float bridgeZ,bridgeX,bridgeY,plannedDistance;
            public double startedAt,endedAt,maxObservationGap;
            public List<DogResult> dogs=new List<DogResult>();
            public List<Trace> trace=new List<Trace>();
            public MovementFailure movementFailure;
        }
        [Serializable] public class Report
        {
            public string runId,status="RUNNING",reason,utc,endedUtc,scene,hashBefore,hashAfter;
            public double deadline;
            public bool full,gateOnly,benchOnly,returnOnly,originalBackground,settingsRestored,sceneFileUnchanged,dirtyAfterPlay,playerDataRestored;
            public string playerDataBackup;
            public bool riverOnly; // IMP-DOG-020 W5 (KH_CourseWalkHarnessEditor.River.cs)
            public bool frisbeeOnly; // DES-DOG-005 M2 Course/94 (KH_CourseWalkHarnessEditor.Frisbee.cs)
            public bool frisbeeM3;   // DES-DOG-005 M3 Course/94b (with frisbeeOnly; KH_CourseWalkHarnessEditor.FrisbeeWater.cs)
            public bool travelOnly; // IMP-WLD-001 travel (KH_CourseWalkHarnessEditor.Travel.cs)
            public bool travelRc;   // Course/82b: 破壊監査 RC の新規ケースだけ (同じファイル)
            public bool travelCall; // IMP-WLD-002 Course/82c: 呼ぶ (KH_CourseWalkHarnessEditor.Call.cs)
            public bool waterOnly; // IMP-DOG-020 W0/W4/W6 (KH_CourseWalkHarnessEditor.Water.cs)
            public bool waterSwim; // IMP-DOG-020 step 4 W1 x6 / W2 (same file, Course/79)
            public bool waterFetch; // IMP-DOG-020 step 6 W3 / S4 / S5b (same file, Course/88)
            public int waterFetchSet; // IMP-DOG-021: 0 = every Course/88 case, 1 = the IMP-DOG-020 cases only (88a), 2 = the IMP-DOG-021 cases only (88b)
            public bool commerceOnly;     // IMP-PRG-001 / IMP-DOG-014 Course/96・96b・97 (KH_CourseWalkHarnessEditor.Commerce.cs)
            public int commerceSet;       // 0 = ledger (96), 1 = ledger re-entry (96b), 2 = treats (97)
            public string commerceSource; // 96b: the PlayerData snapshot of the 96 run that was installed before Play
            public bool washOnly;         // IMP-DOG-025 Course/100 (KH_CourseWalkHarnessEditor.Wash.cs)
            public string washGroups;     // Course/100a-e: comma-separated groups to run (empty = all). Persisted here because Play mode reloads the domain and clears statics
            public int lapChoice;
            public string selectedWalkCase,executionPurpose;
            public List<Case> cases=new List<Case>();
            public List<string> errors=new List<string>();public int clientLogErrors;
            public string limitations="Single local ClientSim. Menu Udon events exercise attachment and destination request paths; no physical UI click, pickup or real network/Owner handover. Initial actors are placed by the harness. Holder follows saved route by short TeleportTo steps at 2 m/s; dog transforms are never assigned during observation. lapChoice=0 is segment coverage only; lapChoice=2/3 is a continuous synthetic-holder lap. Neither measures user walking time or device performance. Random dog pauses remain enabled.";
        }
        static Report report;
        static UdonBehaviour manager,pouch,route;
        static UdonBehaviour[] wander;
        static Transform[] roots;
        static Vector3[] nodes;
        static int caseIndex=-1,phase=-1,attachIndex,lastFrame=-1,holderNode;
        static double phaseAt,nextAction,nextTrace,lastMoveAt,lastObserveAt,nextCheckpoint,nextHeartbeat;
        static Vector3 holder,previousHolder;
        static Vector3[] previous;
        static int[] previousNext,previousObservedNext,previousChoice;
        static bool ending,interrupted;
        static Case Current=>report.cases[caseIndex];

        static KH_CourseWalkHarnessEditor()
        {
            EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Mode;
            Application.logMessageReceived+=Log;
            if(!SessionState.GetBool(Key,false))return;
            try{report=JsonUtility.FromJson<Report>(File.ReadAllText(Journal));if(report==null)throw new IOException("Empty journal");ending=report.status!="RUNNING";interrupted=!ending&&SessionState.GetBool(Key+".entered",false);}
            catch(Exception e){Debug.LogError("[KH Course Walk] Journal unreadable: "+e.Message);EditorApplication.delayCall+=Recover;}
        }
        [MenuItem("Tools/KominkaHillside/Course/40 - Walk Harness Smoke")]
        public static void Smoke(){Begin(false);}
        [MenuItem("Tools/KominkaHillside/Course/41 - Stop Walk Harness")]
        public static void Stop(){End("STOPPED","Operator stop");}
        [MenuItem("Tools/KominkaHillside/Course/42 - Walk Harness Segments")]
        public static void Full(){Begin(true);}
        [MenuItem("Tools/KominkaHillside/Course/45 - Walk Harness Gate Boundary")]
        public static void Gate(){Begin(true,true);}
        [MenuItem("Tools/KominkaHillside/Course/53 - Walk Harness Six Dogs Full Forward Lap")]
        public static void ForwardLap(){LapPurposeWindow.Open(2);}
        [MenuItem("Tools/KominkaHillside/Course/54 - Walk Harness Six Dogs Full Reverse Lap")]
        public static void ReverseLap(){LapPurposeWindow.Open(3);}
        // Ordinary changes use a single existing segment, with exactly the same setup and assertions.
        public static void RunSegment(string caseName)
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(caseName??"",@"\A(?:bridge-(?:190|490)|gate-wrap)-choice-[23]-dogs-[136]\z"))
                throw new ArgumentException("Use bridge-190|bridge-490|gate-wrap, then -choice-2|3-dogs-1|3|6.");
            Begin(true,selectedWalkCase:caseName);
        }
        public static void RunLap(int choice,string purpose)
        {
            if(choice!=2&&choice!=3)throw new ArgumentException("Lap choice must be 2 (forward) or 3 (reverse).");
            if(string.IsNullOrWhiteSpace(purpose))throw new ArgumentException("A continuous lap needs a verification purpose. Use RunSegment for local changes.");
            Begin(true,lapChoice:choice,executionPurpose:purpose.Trim());
        }
        public class LapPurposeWindow:EditorWindow
        {
            [SerializeField] int choice;
            [SerializeField] string purpose="";
            public static void Open(int direction)
            {
                var window=GetWindow<LapPurposeWindow>(true,"Continuous lap (~20 min)",true);
                window.choice=direction;window.purpose="";window.minSize=new Vector2(440,150);
            }
            void OnGUI()
            {
                EditorGUILayout.HelpBox("Use a bridge / gate segment for ordinary changes. A full lap is for long-running defects or broad route / follow regression.",MessageType.Info);
                EditorGUILayout.LabelField(choice==2?"Six dogs: forward lap":"Six dogs: reverse lap");
                purpose=EditorGUILayout.TextField("Verification purpose",purpose);
                using(new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(purpose)||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling))
                    if(GUILayout.Button("Run continuous lap")){RunLap(choice,purpose);Close();}
            }
        }
        [MenuItem("Tools/KominkaHillside/Course/57 - Walk Harness Viewpoint Bench")]
        public static void Bench(){Begin(false,false,0,true);}
        [MenuItem("Tools/KominkaHillside/Course/63 - Walk Harness Midway Attach and Return")]
        public static void MidwayReturn(){Begin(true,false,0,false,true);}
        static void Begin(bool full,bool gateOnly=false,int lapChoice=0,bool benchOnly=false,bool returnOnly=false,string selectedWalkCase=null,string executionPurpose=null)
        {
            if(lapChoice!=0&&string.IsNullOrWhiteSpace(executionPurpose))throw new ArgumentException("Continuous lap requires a verification purpose.");
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            if(SessionState.GetBool(Key,false)||SessionState.GetBool(Key+".restorePending",false))throw new InvalidOperationException("Harness active or PlayerData restoration pending");
            report=new Report{runId=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),utc=DateTime.UtcNow.ToString("o"),scene=KH_CourseGroundEditor.ScenePath,full=full,gateOnly=gateOnly,benchOnly=benchOnly,returnOnly=returnOnly,lapChoice=lapChoice,selectedWalkCase=selectedWalkCase,executionPurpose=executionPurpose,originalBackground=Application.runInBackground};
            report.hashBefore=Hash(report.scene);report.deadline=EditorApplication.timeSinceStartup+(lapChoice!=0?1800:full&&string.IsNullOrEmpty(selectedWalkCase)?1700:150);
            BackupPlayerData();
            SessionState.SetBool(Key+".background",report.originalBackground);SessionState.SetBool(Key+".entered",false);
            Save();ending=false;interrupted=false;caseIndex=-1;phase=-1;SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void Mode(PlayModeStateChange state)
        {
            if(report==null||!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){SessionState.SetBool(Key+".entered",true);phaseAt=EditorApplication.timeSinceStartup;phase=-1;}
            if(state==PlayModeStateChange.ExitingPlayMode)
            {Restore();if(!ending){report.status="STOPPED";report.reason="Play stopped externally";ending=true;SafeSave();}}
            if(state==PlayModeStateChange.EnteredEditMode)Finish();
        }
        static void Log(string condition,string stack,LogType type)
        {
            if(report==null||!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying)return;
            // VRChat クライアントを同じ PC で動かしていると、UdonSharp のログ監視がクライアントの output_log の Udon 例外 (他のワールドのものを含む) を
            // Editor のコンソールへ Error で流す。先頭が下の文言の行はこの Play の結果ではないので、数だけ記録して失敗にしない (実測 2026-09-29)。
            if(condition!=null&&condition.Contains("Runtime error detected, but the client has not been launched with")){report.clientLogErrors++;return;}
            if((type==LogType.Exception||type==LogType.Error||type==LogType.Assert)&&report.errors.Count<20)report.errors.Add(condition);
        }
        static void Tick()
        {
            if(report==null||!SessionState.GetBool(Key,false))return;
            if(ending){if(!EditorApplication.isPlayingOrWillChangePlaymode)Finish();else if(EditorApplication.isPlaying)EditorApplication.ExitPlaymode();return;}
            if(interrupted){End("INTERRUPTED","Domain reload during observation");return;}
            if(EditorApplication.timeSinceStartup>report.deadline){End("TIMEOUT","Global watchdog");return;}
            if(!EditorApplication.isPlaying)return;
            if(EditorApplication.isPaused){End("STOPPED","Editor paused");return;}
            if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
            try
            {
                double now=EditorApplication.timeSinceStartup;
                if(phase<0)
                {
                    if(Networking.LocalPlayer==null||now-phaseAt<15)return;
                    Bind();Application.runInBackground=true;
                    var view=typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");if(view!=null)EditorWindow.GetWindow(view).Focus();
                    MakeCases();Next();return;
                }
                if(report.errors.Count>0)throw new InvalidOperationException("Runtime error: "+report.errors[0]);
                if(report.commerceOnly){CommerceTick(now);return;}
                if(report.washOnly){WashTick(now);return;}
                if(report.riverOnly){RiverTick(now);return;}
                if(report.frisbeeOnly){FrisbeeTick(now);return;}
                if(report.travelOnly){TravelTick(now);return;}
                if(report.waterOnly){WaterHarnessTick(now);return;}
                if(now-Current.startedAt>(report.returnOnly?Current.plannedDistance/2+100:report.lapChoice!=0?1500:80))throw new TimeoutException("Case deadline exceeded");
                if(phase==0) // Detach through the regular menu, then place the next case.
                {
                    if(now<nextAction)return;
                    if(attachIndex<6){pouch.SendCustomEvent("_WalkOff"+attachIndex++);nextAction=now+.3;return;}
                    if(Get<int[]>(manager,"walkers").Any(w=>w>=0))throw new InvalidOperationException("Detach did not complete");
                    Place();phase=1;attachIndex=0;nextAction=now+.5;return;
                }
                if(phase==1)
                {
                    if(now<nextAction)return;
                    if(attachIndex<Current.count)
                    {PlaceDog(attachIndex,Networking.LocalPlayer.GetRotation());pouch.SendCustomEvent((attachIndex<3?"_WalkLeft":"_WalkRight")+attachIndex);attachIndex++;nextAction=now+.5;return;}
                    var walkers=Get<int[]>(manager,"walkers");var destinations=Get<int[]>(manager,"destinations");
                    for(int i=0;i<Current.count;i++)if(walkers[i]!=Networking.LocalPlayer.playerId||destinations[i]!=Current.choice)throw new InvalidOperationException("Menu attach/destination rejected for dog "+i);
                    previous=new Vector3[Current.count];previousNext=new int[Current.count];previousObservedNext=new int[Current.count];previousChoice=new int[Current.count];
                    for(int i=0;i<Current.count;i++){previous[i]=roots[i].position;previousNext[i]=Get<int>(wander[i],"_routeNext");previousObservedNext[i]=previousNext[i];previousChoice[i]=Get<int>(wander[i],"_routeChoice");Current.dogs.Add(new DogResult{index=i,start=roots[i].position,visited=report.lapChoice!=0?new bool[nodes.Length]:null});}
                    phase=2;phaseAt=now;nextTrace=now;lastMoveAt=now;lastObserveAt=now;nextCheckpoint=now+60;nextHeartbeat=now;Save();return;
                }
                if(phase>=3&&report.returnOnly){ReturnTick(now);return;}
                if(phase==2)
                {
                    int direction=Current.returning?Current.returnDirection:Current.choice==2?1:-1;
                    previousHolder=holder;float step=Mathf.Min((float)(now-lastMoveAt),.1f)*2;lastMoveAt=now;
                    if(Current.requiredLinks>0?Current.holderLinks<Current.requiredLinks:holderNode!=Current.endNode)
                    {
                        int next=(holderNode+direction+nodes.Length)%nodes.Length;holder=Vector3.MoveTowards(holder,nodes[next],step);
                        if(Vector3.Distance(holder,nodes[next])<.001f){holderNode=next;Current.holderLinks++;}
                    }
                    var heading=holder-previousHolder;heading.y=0;
                    Networking.LocalPlayer.TeleportTo(holder,heading.sqrMagnitude>.000001f?Quaternion.LookRotation(heading):Networking.LocalPlayer.GetRotation());
                    Observe(now);
                    if(report.returnOnly&&Current.returning){CheckReturnArrival(now);return;}
                    bool all=Current.dogs.All(d=>d.reached);
                    if(report.lapChoice!=0&&now>=nextHeartbeat)
                    {nextHeartbeat=now+5;File.WriteAllText(Dir+"/"+report.runId+"/progress.json",JsonUtility.ToJson(new Progress{utc=DateTime.UtcNow.ToString("o"),runId=report.runId,holderNode=holderNode,holderLinks=Current.holderLinks,requiredLinks=Current.requiredLinks,seconds=now-phaseAt,dogs=Current.dogs},true));}
                    if(report.lapChoice!=0&&now>=nextCheckpoint){nextCheckpoint=now+60;Save();}
                    if(holderNode==Current.endNode&&(Current.requiredLinks==0||Current.holderLinks==Current.requiredLinks)&&all&&now-phaseAt>4)
                    {
                        if(report.returnOnly){StartReturn(now);return;}
                        foreach(var d in Current.dogs)
                        {
                            if(!d.choiceObserved||d.routeChanges<2||d.travelled<10)throw new InvalidOperationException("No observed route progression dog "+d.index);
                            if(Current.bridge&&(!d.crossedBridge||d.maxBridgeHeightError>.2f||d.maxBridgeZError>1.8f))throw new InvalidOperationException("Bridge traversal invalid dog "+d.index);
                            if(Current.choice==2?d.forwardSteps<2:d.reverseSteps<2)throw new InvalidOperationException("Wrong route direction dog "+d.index);
                            if(Current.choice==2?d.reverseSteps>0:d.forwardSteps>0)throw new InvalidOperationException("Observed backwards route progression dog "+d.index);
                            if(!Current.bridge&&!report.benchOnly&&(Current.choice==2?d.forwardWraps<1:d.reverseWraps<1))throw new InvalidOperationException("Gate boundary transition not observed dog "+d.index);
                            if(Current.requiredLinks>0&&(d.visitedRouteNodes!=nodes.Length||d.travelled<Current.plannedDistance*.9f||(Current.choice==2?d.forwardSteps:d.reverseSteps)<nodes.Length-5))throw new InvalidOperationException("Incomplete lap coverage dog "+d.index+" visited="+d.visitedRouteNodes+"/"+nodes.Length);
                        }
                        Current.status=Current.requiredLinks>0?"LAP_OBSERVED":"SEGMENT_OBSERVED";Current.endedAt=now;Save();
                        if(caseIndex+1==report.cases.Count)End(report.lapChoice!=0?"LAP_OBSERVED":"SEGMENTS_OBSERVED",report.lapChoice!=0?"Continuous local Udon lap observed with synthetic holder; not user walking-time, network or device acceptance":"Bounded local Udon segment checks complete; not full-lap or device acceptance");else Next();
                    }
                }
            }
            catch(Exception e)
            {if(caseIndex>=0){Current.status="FAIL";Current.reason=e.Message;}End("FAIL",e.ToString());}
        }
        static void Bind()
        {
            var all=UnityEngine.Object.FindObjectsOfType<UdonBehaviour>();
            manager=all.Single(u=>u.programSource!=null&&u.programSource.name=="KH_LeashManager");
            pouch=all.Single(u=>u.programSource!=null&&u.programSource.name=="KD_TreatPouch");
            route=all.Single(u=>u.programSource!=null&&u.programSource.name=="KH_WalkRoute");
            roots=Get<Transform[]>(manager,"dogRoots");nodes=Get<Vector3[]>(route,"nodePositions");
            if(roots.Length!=6||nodes.Length!=436||!Get<bool>(route,"closedLoop"))throw new InvalidOperationException("Full-course runtime data mismatch");
            wander=all.Where(u=>u.programSource!=null&&u.programSource.name=="KD_DogWanderController").OrderBy(u=>Get<int>(u,"dogIndex")).ToArray();
            if(wander.Length!=6)throw new InvalidOperationException("Six wander behaviours required");
            for(int i=0;i<6;i++)if(Get<Transform>(wander[i],"dogRoot")!=roots[i])throw new InvalidOperationException("Dog order mismatch");
        }
        static void MakeCases()
        {
            if(report.commerceOnly){MakeCommerceCases();return;}
            if(report.washOnly){MakeWashCases();return;}
            if(report.riverOnly){MakeRiverCases();return;}
            if(report.frisbeeOnly){MakeFrisbeeCases();return;}
            if(report.travelOnly){MakeTravelCases();return;}
            if(report.waterOnly){MakeWaterCases();return;}
            if(report.returnOnly)
            {
                var distances=Get<float[]>(route,"loopDistances");float length=Get<float>(route,"loopLength");
                if(distances.Length!=nodes.Length||float.IsNaN(length)||float.IsInfinity(length)||length<=200)throw new InvalidOperationException("Invalid return distance data");
                for(int i=0;i<distances.Length;i++)if(float.IsNaN(distances[i])||float.IsInfinity(distances[i])||distances[i]<0||distances[i]>=length||(i>0&&distances[i]<=distances[i-1]))throw new InvalidOperationException("Invalid loop distance at "+i);
                int gate=Get<int[]>(route,"destinationNodes")[0];
                if(gate!=0)throw new InvalidOperationException("Return harness expects gate node 0");
                foreach(int count in new[]{1,3,6})foreach(int direction in new[]{1,-1})
                {
                    int start=direction>0?1:nodes.Length-1;
                    int searched=0;
                    while((direction>0?distances[start]:length-distances[start])<80&&searched<nodes.Length){start=(start+direction+nodes.Length)%nodes.Length;searched++;}
                    if(searched>=nodes.Length)throw new InvalidOperationException("No midway start found within one loop");
                    int end=(start+direction*4+nodes.Length)%nodes.Length;
                    float home=direction>0?distances[end]:length-distances[end];
                    if(home>=length*.4f)throw new InvalidOperationException("Return start is too far from gate");
                    var c=new Case{name="midway-return-side-"+direction+"-dogs-"+count,count=count,choice=direction>0?2:3,startNode=start,endNode=end,plannedDistance=home};
                    for(int n=start;n!=end;n=(n+direction+nodes.Length)%nodes.Length)c.plannedDistance+=Vector3.Distance(nodes[n],nodes[(n+direction+nodes.Length)%nodes.Length]);
                    report.cases.Add(c);
                }
                return;
            }
            if(report.benchOnly)
            {
                report.cases.Add(new Case{name="viewpoint-bench-choice-2-dogs-6",count=6,choice=2,startNode=9,endNode=18});return;
            }
            if(report.lapChoice!=0)
            {
                int direction=report.lapChoice==2?1:-1;
                var c=new Case{name="continuous-lap-choice-"+report.lapChoice+"-dogs-6",count=6,choice=report.lapChoice,startNode=0,endNode=direction>0?5:nodes.Length-5,requiredLinks=nodes.Length+5};
                for(int i=0;i<c.requiredLinks;i++)c.plannedDistance+=Vector3.Distance(nodes[(i*direction+nodes.Length*2)%nodes.Length],nodes[((i+1)*direction+nodes.Length*2)%nodes.Length]);
                report.cases.Add(c);return;
            }
            if(!report.gateOnly)foreach(int count in report.full?new[]{1,3,6}:new[]{1})foreach(float z in report.full?new[]{190f,490f}:new[]{190f})foreach(int choice in report.full?new[]{2,3}:new[]{2})
            {
                int a=Closest(new Vector3(400,0,z)),b=Closest(new Vector3(430,0,z));
                bool east=choice==2?(z==490):(z==190);var bridge=GameObject.Find("KH_CourseExtension").transform.Find("Bridge_"+z);
                report.cases.Add(new Case{name="bridge-"+z+"-choice-"+choice+"-dogs-"+count,count=count,choice=choice,startNode=east?a:b,endNode=east?b:a,bridge=true,bridgeX=bridge.position.x,bridgeY=bridge.position.y,bridgeZ=z});
            }
            // The last three nodes lie inside a tight gate bend. Attaching there can advance to node 0
            // before observation starts. Use a longer approach/departure so the wrap is actually observed.
            if(report.full)foreach(int count in new[]{1,3,6})foreach(int choice in new[]{2,3})report.cases.Add(new Case{name="gate-wrap-choice-"+choice+"-dogs-"+count,count=count,choice=choice,startNode=choice==2?nodes.Length-8:5,endNode=choice==2?5:nodes.Length-8});
            if(!string.IsNullOrEmpty(report.selectedWalkCase))
            {
                report.cases=report.cases.Where(c=>c.name==report.selectedWalkCase).ToList();
                if(report.cases.Count!=1)throw new InvalidOperationException("Selected walk case was not generated: "+report.selectedWalkCase);
            }
        }
        static int Closest(Vector3 target)
        {int best=-1;float distance=float.MaxValue;for(int i=0;i<nodes.Length;i++){var delta=nodes[i]-target;delta.y=0;if(delta.sqrMagnitude<distance){distance=delta.sqrMagnitude;best=i;}}return best;}
        static void Next()
        {caseIndex++;Current.status="RUNNING";Current.startedAt=EditorApplication.timeSinceStartup;phase=0;attachIndex=0;nextAction=0;Save();}
        static void Place()
        {
            holderNode=Current.startNode;holder=nodes[holderNode];int direction=Current.choice==2?1:-1;
            var heading=nodes[(holderNode+direction+nodes.Length)%nodes.Length]-holder;heading.y=0;var rotation=Quaternion.LookRotation(heading);
            Networking.LocalPlayer.TeleportTo(holder,rotation);
            for(int i=0;i<Current.count;i++)PlaceDog(i,rotation);
            pouch.SendCustomEvent(Current.choice==2?"_WalkLoopForward":"_WalkLoopReverse");
        }
        static void PlaceDog(int i,Quaternion rotation)
        {
            // Initial setup only, including just before attach while the unconnected dog may roam.
            Vector3 point=holder+rotation*new Vector3((i<3?-1:1)*(.65f+(i%3)*.3f),0,-.4f-(i%3)*.55f);
            UnityEngine.AI.NavMeshHit hit;if(!UnityEngine.AI.NavMesh.SamplePosition(point,out hit,2,UnityEngine.AI.NavMesh.AllAreas))throw new InvalidOperationException("Initial dog placement lacks NavMesh");
            roots[i].SetPositionAndRotation(hit.position,rotation);
            var sync=roots[i].GetComponent<VRC.SDK3.Components.VRCObjectSync>();if(sync!=null)sync.FlagDiscontinuity();
        }
        static void Observe(double now)
        {
            double gap=now-lastObserveAt;lastObserveAt=now;Current.maxObservationGap=Math.Max(Current.maxObservationGap,gap);
            if(gap>.75)throw new InvalidOperationException("Observation gap too large to certify continuous movement");
            bool trace=now>=nextTrace;if(trace)nextTrace=now+.5;
            for(int i=0;i<Current.count;i++)
            {
                var d=Current.dogs[i];var p=roots[i].position;int next=Get<int>(wander[i],"_routeNext"),choice=Get<int>(wander[i],"_routeChoice");
                float movement=Vector3.Distance(previous[i],p);d.maxObservedStep=Mathf.Max(d.maxObservedStep,movement);
                // 犬は Time.deltaTime ぶん進む。観測の間隔 gap (Editor の時刻) だけで上限を決めると、フレームが伸びた回 (実測 2026-09-29: gap 0.018 s に対し
                // deltaTime 0.10〜0.33 s、速さ 4.5 m/s で 0.44〜0.46 m) の正しい歩きを不連続と数える。長い方で上限を決める (瞬間移動は 1 m 単位なので検出は保たれる)。
                if(movement>Mathf.Max(.4f,8f*Mathf.Max((float)gap,Time.deltaTime)+.15f))
                {
                    CaptureMovementFailure(i,p,now,gap);
                    throw new InvalidOperationException("Discontinuous dog movement (possible catch-up teleport), dog "+i+" step="+movement+" interval="+gap);
                }
                if(Current.returning)
                {
                    d.returnChoiceObserved|=choice==0;
                    d.arrived=choice==0&&Get<bool>(wander[i],"_arrived");
                }
                d.choiceObserved|=choice==Current.choice;d.travelled+=movement;d.end=p;d.endDistance=Vector3.Distance(p,nodes[Current.endNode]);d.reached=d.endDistance<4;
                if(d.visited!=null)for(int n=0;n<nodes.Length;n++)if(!d.visited[n]&&(p-nodes[n]).sqrMagnitude<49){d.visited[n]=true;d.visitedRouteNodes++;}
                if(next>=0){d.minNext=Math.Min(d.minNext,next);d.maxNext=Math.Max(d.maxNext,next);}
                if(next>=0&&previousNext[i]>=0&&next!=previousNext[i]&&(!Current.returning||(choice==0&&previousChoice[i]==0)))
                {
                    int delta=(next-previousNext[i]+nodes.Length)%nodes.Length;
                    if(Current.returning){if(delta<nodes.Length/2)d.returnForwardSteps+=delta;else d.returnReverseSteps+=nodes.Length-delta;}
                    else{d.routeChanges++;if(delta<nodes.Length/2)d.forwardSteps+=delta;else d.reverseSteps+=nodes.Length-delta;}
                    if(previousNext[i]==nodes.Length-1&&next==0)d.forwardWraps++;
                    if(previousNext[i]==0&&next==nodes.Length-1)d.reverseWraps++;
                }
                if(Current.bridge&&Mathf.Abs(p.x-Current.bridgeX)<4)
                {d.maxBridgeHeightError=Mathf.Max(d.maxBridgeHeightError,Mathf.Abs(p.y-Current.bridgeY));d.maxBridgeZError=Mathf.Max(d.maxBridgeZError,Mathf.Abs(p.z-Current.bridgeZ));}
                if(Current.bridge&&(previous[i].x-Current.bridgeX)*(p.x-Current.bridgeX)<=0&&Mathf.Abs(p.x-previous[i].x)>.00001f)
                {
                    var crossing=Vector3.Lerp(previous[i],p,(Current.bridgeX-previous[i].x)/(p.x-previous[i].x));
                    if(Mathf.Abs(crossing.z-Current.bridgeZ)<1.8f&&Mathf.Abs(crossing.y-Current.bridgeY)<.2f)d.crossedBridge=true;
                }
                if(trace||next!=previousObservedNext[i])Current.trace.Add(new Trace{seconds=now-Current.startedAt,dog=i,next=next,resume=Get<int>(wander[i],"_routeResume"),mode=Get<int>(wander[i],"leashMode"),choice=choice,player=Networking.LocalPlayer.GetPosition(),position=p});
                previous[i]=p;previousObservedNext[i]=next;
                // Keep the last valid target across a reentry wait (-1), so waiting cannot hide reversal.
                if(Current.returning&&choice!=previousChoice[i])previousNext[i]=-1;
                if(next>=0)previousNext[i]=next;previousChoice[i]=choice;
            }
        }
        static void StartReturn(double now)
        {
            foreach(var d in Current.dogs)
            {
                int steps=Current.choice==2?d.forwardSteps:d.reverseSteps;
                int wrong=Current.choice==2?d.reverseSteps:d.forwardSteps;
                if(!d.choiceObserved||steps<2||wrong!=0||d.travelled<10)throw new InvalidOperationException("Midway attachment did not progress dog "+d.index);
            }
            var distances=Get<float[]>(route,"loopDistances");float length=Get<float>(route,"loopLength");
            float forward=distances[0]-distances[holderNode];if(forward<0)forward+=length;
            Current.returnDirection=forward<=length*.5f?1:-1;
            if(Current.returnDirection==(Current.choice==2?1:-1))throw new InvalidOperationException("Return case must reverse its loop direction");
            Current.midwayObserved=true;Current.returning=true;Current.returnStartedAt=now;Current.endNode=0;
            pouch.SendCustomEvent("_WalkGate");
            // Retain physical continuity, but do not count destination reentry as route reversal.
            for(int i=0;i<Current.count;i++){previousNext[i]=-1;previousChoice[i]=-1;}
            Save();
        }
        static bool AllReturned()
        {
            var destinations=Get<int[]>(manager,"destinations");var walkers=Get<int[]>(manager,"walkers");
            for(int i=0;i<Current.count;i++)if(walkers[i]!=Networking.LocalPlayer.playerId||destinations[i]!=0||Get<int>(wander[i],"_routeChoice")!=0||!Get<bool>(wander[i],"_arrived")||Get<int>(wander[i],"_routeNext")>=0)return false;
            return true;
        }
        static void CheckReturnArrival(double now)
        {
            if(holderNode!=0||!AllReturned()){Current.arrivalStartedAt=0;return;}
            if(Current.arrivalStartedAt==0)Current.arrivalStartedAt=now;
            if(now-Current.arrivalStartedAt<5)return;
            foreach(var d in Current.dogs)
            {
                int steps=Current.returnDirection>0?d.returnForwardSteps:d.returnReverseSteps;
                int wrong=Current.returnDirection>0?d.returnReverseSteps:d.returnForwardSteps;
                if(!d.returnChoiceObserved||!d.arrived||steps<2||wrong!=0)throw new InvalidOperationException("Gate return progression invalid dog "+d.index);
            }
            Current.returnObserved=true;phase=3;attachIndex=0;nextAction=now;Save();
        }
        static void ReturnTick(double now)
        {
            Observe(now); // Includes detach/reconnect: no dog placement or gaps in continuity checks.
            if(now<nextAction)return;
            if(phase==3)
            {
                if(attachIndex<Current.count){pouch.SendCustomEvent("_WalkOff"+attachIndex++);nextAction=now+.3;return;}
                if(Get<int[]>(manager,"walkers").Any(w=>w>=0))throw new InvalidOperationException("Return detach rejected");
                phase=4;attachIndex=0;nextAction=now+.3;return;
            }
            if(phase==4)
            {
                if(attachIndex<Current.count){pouch.SendCustomEvent((attachIndex<3?"_WalkLeft":"_WalkRight")+attachIndex);attachIndex++;nextAction=now+.5;return;}
                var walkers=Get<int[]>(manager,"walkers");var destinations=Get<int[]>(manager,"destinations");
                for(int i=0;i<Current.count;i++)if(walkers[i]!=Networking.LocalPlayer.playerId||destinations[i]!=0)throw new InvalidOperationException("Remembered Gate choice not inherited by dog "+i);
                phase=5;phaseAt=now;return;
            }
            if(phase==5&&now-phaseAt>3)
            {
                if(!AllReturned())throw new InvalidOperationException("Reattached dogs did not retain Gate arrival");
                Current.reconnectObserved=true;Current.status="MIDWAY_RETURN_OBSERVED";Current.endedAt=now;Save();
                if(caseIndex+1==report.cases.Count)End("MIDWAY_RETURN_OBSERVED","Six local midway attach / shorter-side return / same-stay choice inheritance cases; no network or persistent-save acceptance");else Next();
            }
        }
        static void CaptureMovementFailure(int i,Vector3 p,double now,double gap)
        {
            // Evidence only: no transform, physics, runtime symbol or acceptance-threshold changes.
            var f=new MovementFailure{dog=i,frame=Time.frameCount,seconds=now-Current.startedAt,observationGap=gap,deltaTime=Time.deltaTime,previous=previous[i],current=p,delta=p-previous[i],player=Networking.LocalPlayer.GetPosition()};
            Current.movementFailure=f;
            try
            {
                f.next=Get<int>(wander[i],"_routeNext");f.mode=Get<int>(wander[i],"leashMode");f.resume=Get<int>(wander[i],"_routeResume");f.speed=Get<float>(wander[i],"_speed");
                f.groundOffset=Get<float>(wander[i],"groundRayOffset");f.groundDistance=Get<float>(wander[i],"groundRayDistance");
                object mask;if(!wander[i].TryGetProgramVariable("groundLayerMask",out mask))throw new InvalidOperationException("Ground mask missing");
                f.groundMask=mask is LayerMask?((LayerMask)mask).value:Convert.ToInt32(mask);
                // Reconstruct the vertical query at the new XZ and preceding Y; actual movement ray is not intercepted.
                f.rayOrigin=new Vector3(p.x,previous[i].y+f.groundOffset,p.z);
                RaycastHit hit;f.groundHit=Physics.Raycast(f.rayOrigin,Vector3.down,out hit,f.groundDistance,f.groundMask,QueryTriggerInteraction.Ignore);
                if(f.groundHit)
                {f.hitPoint=hit.point;f.hitNormal=hit.normal;f.hitLayer=hit.collider.gameObject.layer;f.colliderType=hit.collider.GetType().Name;f.colliderPath=hit.collider.name;for(var t=hit.collider.transform.parent;t!=null;t=t.parent)f.colliderPath=t.name+"/"+f.colliderPath;}
                UnityEngine.AI.NavMeshHit nav;f.navHit=UnityEngine.AI.NavMesh.SamplePosition(p,out nav,2,UnityEngine.AI.NavMesh.AllAreas);if(f.navHit)f.navPoint=nav.position;
            }
            catch(Exception e){f.diagnosticError=e.Message;}
        }
        static T Get<T>(UdonBehaviour u,string key)
        {object value;if(u!=null&&!EditorApplication.isPlaying&&ProxyTryGet(u,key,out value)&&value is T)return (T)value;   // Course/95 plan probe (Edit mode): the U# proxy's serialized value
         if(u==null||!u.TryGetProgramVariable(key,out value)||!(value is T))throw new InvalidOperationException("Missing runtime symbol "+key);return (T)value;}
        [Serializable] class Progress
        {public string utc,runId;public int holderNode,holderLinks,requiredLinks;public double seconds;public List<DogResult> dogs;}
        static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
        static void End(string status,string reason)
        {if(report==null||ending)return;report.status=status;report.reason=reason;ending=true;Restore();SafeSave();if(EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.ExitPlaymode();else Finish();}
        static void Restore(){if(report!=null){Application.runInBackground=report.originalBackground;report.settingsRestored=Application.runInBackground==report.originalBackground;}}
        static void Finish()
        {
            if(report==null)return;
            try{Restore();RestorePlayerData(report.playerDataBackup);report.playerDataRestored=true;report.hashAfter=Hash(report.scene);report.sceneFileUnchanged=report.hashBefore==report.hashAfter;report.dirtyAfterPlay=SceneManager.GetActiveScene().isDirty;report.endedUtc=DateTime.UtcNow.ToString("o");}
            catch(Exception e){report.status="EVIDENCE_ERROR";report.reason="Final evidence failed: "+e.Message;}
            finally
            {
                try{SafeSave();}finally{SessionState.SetBool(Key,false);SessionState.SetBool(Key+".entered",false);report=null;ending=false;interrupted=false;manager=null;pouch=null;route=null;wander=null;roots=null;nodes=null;caseIndex=-1;phase=-1;}
            }
        }
        static void Recover()
        {
            Application.runInBackground=SessionState.GetBool(Key+".background",true);SessionState.SetBool(Key,false);report=null;
            if(EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.ExitPlaymode();
            Debug.LogError("[KH Course Walk] Journal recovery stopped Play. PlayerData backup remains at "+SessionState.GetString(Key+".backup", "unknown")+"; restore it after Play is fully stopped before another run.");
        }
        static void BackupPlayerData()
        {
            report.playerDataBackup=Dir+"/"+report.runId+"/playerdata";Directory.CreateDirectory(report.playerDataBackup);
            string storage=System.IO.Path.GetFullPath("ClientSimStorage/PlayerData");
            var files=Directory.Exists(storage)?Directory.GetFiles(storage,"*_VRCDefaultWorldScene.json"):new string[0];
            foreach(var path in files)File.Copy(path,report.playerDataBackup+"/"+System.IO.Path.GetFileName(path),false);
            File.WriteAllLines(report.playerDataBackup+"/manifest.txt",files.Select(System.IO.Path.GetFileName));
            SessionState.SetString(Key+".backup",report.playerDataBackup);
            SessionState.SetBool(Key+".restorePending",true);
        }
        [MenuItem("Tools/KominkaHillside/Course/46 - Restore Walk Harness PlayerData")]
        public static void RecoverPlayerData()
        {
            if(SessionState.GetBool(Key,false)||!SessionState.GetBool(Key+".restorePending",false))throw new InvalidOperationException("Stopped harness with pending restoration required");
            RestorePlayerData(SessionState.GetString(Key+".backup",""));
        }
        static void RestorePlayerData(string backupDir)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("PlayerData restoration needs stopped Play");
            string fullBackup=System.IO.Path.GetFullPath(backupDir),ownedBase=System.IO.Path.GetFullPath(Dir)+System.IO.Path.DirectorySeparatorChar;
            if(!fullBackup.StartsWith(ownedBase,StringComparison.OrdinalIgnoreCase)||System.IO.Path.GetFileName(fullBackup)!="playerdata")throw new InvalidDataException("Backup outside harness run directory");
            var names=File.ReadAllLines(fullBackup+"/manifest.txt");string storage=System.IO.Path.GetFullPath("ClientSimStorage/PlayerData");
            Directory.CreateDirectory(storage);
            foreach(var name in names)
            {
                if(System.IO.Path.GetFileName(name)!=name||!name.EndsWith("_VRCDefaultWorldScene.json",StringComparison.Ordinal))throw new InvalidDataException("Unsafe backup entry");
                string backup=fullBackup+"/"+name,destination=System.IO.Path.Combine(storage,name);
                File.Copy(backup,destination,true);if(Hash(backup)!=Hash(destination))throw new IOException("PlayerData restoration differs");
            }
            foreach(var path in Directory.GetFiles(storage,"*_VRCDefaultWorldScene.json"))
                if(!names.Contains(System.IO.Path.GetFileName(path)))
                {
                    if(!string.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)),storage,StringComparison.OrdinalIgnoreCase))throw new IOException("PlayerData path outside storage");
                    File.Delete(path); // Only files created by this local run, never directories or another scene's save.
                }
            SessionState.SetBool(Key+".restorePending",false);
        }
        static void SafeSave(){try{Save();}catch(Exception e){Debug.LogError("[KH Course Walk] Evidence write failed: "+e.Message);}}
        static void Save(){Directory.CreateDirectory(Dir+"/"+report.runId);string json=JsonUtility.ToJson(report,true);File.WriteAllText(Dir+"/"+report.runId+"/report.json",json);File.WriteAllText(Journal,json);File.WriteAllText(Dir+"/latest.json",json);}
    }
}
