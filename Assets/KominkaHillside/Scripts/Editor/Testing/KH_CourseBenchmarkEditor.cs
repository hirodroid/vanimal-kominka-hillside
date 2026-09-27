using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Profiling;
using VRC.SDKBase;
using VRC.SDK3.ClientSim;

namespace KominkaHillside.Editor
{
    // Editor-only baseline for DES-ENV-002. Never saves the scene or enters a different scene.
    // A sample is the latest game frame observed by Editor.update, not every frame or a device benchmark.
    [InitializeOnLoad]
    public static class KH_CourseBenchmarkEditor
    {
        const string ScenePath = "Assets/Scenes/VRCDefaultWorldScene.unity";
        const string Key = "KH.CourseBenchmark.Active";
        const string Journal = "Logs/kh-course-benchmark/journal.json";
        const double Timeout = 180;
        static Report report;
        static double phaseStart;
        static int phase = -1, lastFrame = -1;
        static bool ending, interrupted;
        static Renderer[] sceneryRenderers;
        static bool[] originalRenderingOff;
        static Terrain[] sceneryTerrains;
        static bool[] originalHeightmap;
        static Camera occlusionCamera;
        static bool originalCameraOcclusion;
        static int Variants => report.sceneryContribution||report.occlusionContribution?4:2;
        static readonly List<float> frameMs = new List<float>();
        static readonly List<float> draws = new List<float>();
        static readonly List<float> batches = new List<float>();
        static readonly List<float> passes = new List<float>();
        static readonly List<float> triangles = new List<float>();
        static readonly Vector3[] Points = { new Vector3(47.639f,1.63f,40.084f), new Vector3(80,19.03f,108), new Vector3(72,19.03f,127) };
        static readonly string[] Names = { "entrance-uphill", "viewpoint-kominka", "plaza-uphill" };
        static readonly float[] Yaws = { 350, 195, 0 };
        static readonly Vector3[] RiverPoints = { new Vector3(430.42f,4.38f,200),new Vector3(435.47f,5.28f,250),new Vector3(80,19.03f,108) };
        static readonly string[] RiverNames = { "river-bridge", "river-upstream-bank", "viewpoint-kominka" };
        static readonly float[] RiverYaws = { 230, 205, 195 };
        static readonly Vector3[] CoursePoints = { new Vector3(432,5.8f,196),new Vector3(505,10.2f,285),new Vector3(215,40.2f,615) };
        static readonly string[] CourseNames = { "course-lower-bridge", "course-river-rest", "course-upper-plaza" };
        static readonly float[] CourseYaws = { 245, 255, 200 };

        [Serializable] public class Metric { public float mean, median, p95, max; }
        [Serializable] public class Sample
        {
            public string name; public int repeat, samples;
            public bool representativeSceneryVisible=true;
            public bool cameraOcclusionEnabled;
            public Vector3 playerPosition, cameraPosition, cameraEuler;
            public int width, height, targetFrameRate, vSync;
            public float cameraNear, cameraFar, cameraFov, walkSpeedSetting, runSpeedSetting;
            public Metric observedFrameMs, drawCalls, batches, setPassCalls, triangles;
            public long editorAllocatedBytes, editorReservedBytes;
        }
        [Serializable] public class Report
        {
            public string runId, startedUtc, endedUtc, status, reason, unity, scene, cpu, gpu;
            public double startedEditorTime;
            public int originalCap, originalVsync; public bool originalBackground, restored, sceneDirtyAfter;
            public bool sceneryContribution;
            public bool occlusionContribution, cameraOcclusionRestored;
            public int restoredCap, restoredVsync;
            public List<Sample> samples = new List<Sample>();
            public string limitations = "Editor Desktop ClientSim; one local player, existing six dogs in their normal scene state; not six leashed dogs or 16 real avatars. Observed frame intervals include Editor work; UnityStats may include Editor views. Allocated/reserved memory is the entire Unity Editor process, not player memory. No GPU/CPU split or real human walking pace is measured. Compare only identical conditions; no platform PASS is inferred.";
        }

        static KH_CourseBenchmarkEditor()
        {
            EditorApplication.playModeStateChanged += OnMode;
            EditorApplication.update += Tick;
            if (SessionState.GetBool(Key, false))
            {
                try
                {
                    report = JsonUtility.FromJson<Report>(File.ReadAllText(Journal));
                    if(report==null)throw new InvalidDataException("Empty benchmark journal");
                    ending = report.status != "RUNNING";
                    // Entering Play reloads once normally. A later reload invalidates this run's sample sequence.
                    interrupted = !ending && SessionState.GetBool(Key+".entered",false);
                }
                catch(Exception e)
                {
                    Debug.LogError("[KH Course Benchmark] Cannot resume journal: "+e.Message);
                    EditorApplication.delayCall += RecoverWithoutJournal;
                }
            }
        }

        [MenuItem("Tools/KominkaHillside/Course/1 - Measure Current Scene")]
        public static void Start() { Begin(false); }

        [MenuItem("Tools/KominkaHillside/Course/11 - Measure River Scenery Contribution")]
        public static void StartContribution() { Begin(true); }
        [MenuItem("Tools/KominkaHillside/Course/31 - Measure Course Scenery Contribution")]
        public static void StartCourseContribution()
        { KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);Begin(true); }
        [MenuItem("Tools/KominkaHillside/Course/39 - Measure Course Occlusion Contribution")]
        public static void StartOcclusionContribution()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            if(StaticOcclusionCulling.umbraDataSize<=0)throw new InvalidOperationException("Saved course occlusion bake required");
            Begin(false,true);
        }

        static void Begin(bool contribution,bool occlusion=false)
        {
            if (SessionState.GetBool(Key, false) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Benchmark needs idle Edit mode.");
            string activeScene=SceneManager.GetActiveScene().path;
            if (EditorUtility.scriptCompilationFailed || (activeScene != ScenePath && activeScene != KH_RiverRepresentativeEditor.ScenePath && activeScene != KH_CourseGroundEditor.ScenePath))
                throw new InvalidOperationException("Wrong scene or compile errors.");
            if(contribution&&activeScene!=KH_RiverRepresentativeEditor.ScenePath&&activeScene!=KH_CourseGroundEditor.ScenePath)throw new InvalidOperationException("River/course required for scenery contribution");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene; not saving or discarding it.");
            report = new Report { runId=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"), startedUtc=DateTime.UtcNow.ToString("o"),
                startedEditorTime=EditorApplication.timeSinceStartup, status="RUNNING", unity=Application.unityVersion, scene=activeScene,
                cpu=SystemInfo.processorType, gpu=SystemInfo.graphicsDeviceName, originalCap=Application.targetFrameRate,
                originalVsync=QualitySettings.vSyncCount, originalBackground=Application.runInBackground,sceneryContribution=contribution,occlusionContribution=occlusion };
            phase = -1; ending = false;
            SessionState.SetBool(Key+".entered",false);
            SessionState.SetInt(Key+".cap",report.originalCap);
            SessionState.SetInt(Key+".vsync",report.originalVsync);
            SessionState.SetBool(Key+".background",report.originalBackground);
            Save(); // If output is unwritable, never set Active or enter Play.
            SessionState.SetBool(Key, true);
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("Tools/KominkaHillside/Course/2 - Stop Measurement")]
        public static void Stop() { End("STOPPED", "Stopped by operator"); }

        static void OnMode(PlayModeStateChange state)
        {
            if (report == null || !SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                SessionState.SetBool(Key+".entered",true);
                phase=-1; phaseStart=EditorApplication.timeSinceStartup; lastFrame=-1;
            }
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Restore();
                if (!ending) { report.status="STOPPED"; report.reason="Play stopped externally"; ending=true; SafeSave(); }
            }
            if (state == PlayModeStateChange.EnteredEditMode) Finish();
        }

        static void Tick()
        {
            if (report == null || !SessionState.GetBool(Key, false)) return;
            if (ending)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish();
                else if(EditorApplication.isPlaying)EditorApplication.ExitPlaymode();
                return;
            }
            if (interrupted) { End("INTERRUPTED","Domain reload during measurement; samples are partial and not combined with a restart"); return; }
            if (EditorApplication.timeSinceStartup - report.startedEditorTime > Timeout)
            { End("TIMEOUT", "180 second watchdog"); return; }
            if (!EditorApplication.isPlaying) return;
            if (EditorApplication.isPaused) { End("BLOCKED", "Editor paused during benchmark"); return; }
            try
            {
                if (phase < 0)
                {
                    if (Networking.LocalPlayer == null || EditorApplication.timeSinceStartup-phaseStart < 15) return;
                    Application.targetFrameRate=-1; QualitySettings.vSyncCount=0; Application.runInBackground=true;
                    var viewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
                    if (viewType != null) EditorWindow.GetWindow(viewType).Focus();
                    Next(); return;
                }
                if (Time.frameCount == lastFrame) return;
                lastFrame=Time.frameCount;
                double elapsed=EditorApplication.timeSinceStartup-phaseStart;
                if (elapsed < 3) return;
                frameMs.Add(Time.unscaledDeltaTime*1000); draws.Add(UnityStats.drawCalls);
                batches.Add(UnityStats.batches); passes.Add(UnityStats.setPassCalls); triangles.Add(UnityStats.triangles);
                if (elapsed >= 9)
                {
                    Collect();
                    if (phase >= 3*Variants-1) End("MEASURED", "Fixed-view samples recorded; not a runtime acceptance PASS");
                    else Next();
                }
            }
            catch (Exception e) { End("ERROR", e.ToString()); }
        }

        static void Next()
        {
            phase++; phaseStart=EditorApplication.timeSinceStartup;
            frameMs.Clear(); draws.Clear(); batches.Clear(); passes.Clear(); triangles.Clear();
            int i=phase/Variants;
            bool river=report.scene==KH_RiverRepresentativeEditor.ScenePath;
            bool course=report.scene==KH_CourseGroundEditor.ScenePath;
            if(report.sceneryContribution)SetSceneryVisible(phase%4<2);
            if(report.occlusionContribution)
            {
                var camera=Networking.LocalPlayer.GetClientSimPlayer().GetCameraProvider().GetCamera();
                if(occlusionCamera==null){occlusionCamera=camera;originalCameraOcclusion=camera.useOcclusionCulling;}
                if(camera!=occlusionCamera)throw new InvalidOperationException("Measurement camera changed");
                camera.useOcclusionCulling=phase%4<2;
            }
            Networking.LocalPlayer.TeleportTo(course?CoursePoints[i]:(river?RiverPoints[i]:Points[i]), Quaternion.Euler(0,course?CourseYaws[i]:(river?RiverYaws[i]:Yaws[i]),0));
        }

        static void Collect()
        {
            var p=Networking.LocalPlayer;
            var local=p.GetClientSimPlayer();
            Camera camera=local.GetCameraProvider().GetCamera();
            var sample = new Sample { name=(report.scene==KH_CourseGroundEditor.ScenePath?CourseNames:(report.scene==KH_RiverRepresentativeEditor.ScenePath?RiverNames:Names))[phase/Variants], repeat=phase%2+1, samples=frameMs.Count,
                representativeSceneryVisible=!report.sceneryContribution||phase%4<2,
                cameraOcclusionEnabled=camera.useOcclusionCulling,
                playerPosition=p.GetPosition(), cameraPosition=camera.transform.position, cameraEuler=camera.transform.eulerAngles,
                width=camera.pixelWidth, height=camera.pixelHeight, cameraNear=camera.nearClipPlane, cameraFar=camera.farClipPlane,
                cameraFov=camera.fieldOfView, targetFrameRate=Application.targetFrameRate, vSync=QualitySettings.vSyncCount,
                walkSpeedSetting=p.GetWalkSpeed(), runSpeedSetting=p.GetRunSpeed(),
                observedFrameMs=Summarize(frameMs), drawCalls=Summarize(draws), batches=Summarize(batches), setPassCalls=Summarize(passes), triangles=Summarize(triangles),
                editorAllocatedBytes=Profiler.GetTotalAllocatedMemoryLong(), editorReservedBytes=Profiler.GetTotalReservedMemoryLong() };
            if (sample.samples < 20 || sample.width <= 0 || sample.height <= 0) throw new InvalidOperationException("Insufficient valid samples/camera viewport");
            report.samples.Add(sample); Save();
        }

        static Metric Summarize(List<float> values)
        {
            var a=values.ToArray(); Array.Sort(a); float sum=0; foreach(float x in a)sum+=x;
            return new Metric { mean=sum/a.Length, median=a[a.Length/2], p95=a[Mathf.Min(a.Length-1,Mathf.CeilToInt(a.Length*.95f)-1)], max=a[a.Length-1] };
        }
        static void End(string status, string reason)
        {
            if (report == null || ending) return;
            report.status=status; report.reason=reason; ending=true;
            try { Restore(); SafeSave(); }
            finally
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode(); else Finish();
            }
        }
        static void Restore()
        {
            if(report==null)return;
            RestoreScenery();
            if(occlusionCamera!=null){occlusionCamera.useOcclusionCulling=originalCameraOcclusion;report.cameraOcclusionRestored=occlusionCamera.useOcclusionCulling==originalCameraOcclusion;}
            Application.targetFrameRate=report.originalCap; QualitySettings.vSyncCount=report.originalVsync;
            Application.runInBackground=report.originalBackground;
        }
        static void SetSceneryVisible(bool visible)
        {
            if(sceneryRenderers==null)
            {
                var root=GameObject.Find(report.scene==KH_CourseGroundEditor.ScenePath?"KH_CourseScenery":"KH_RiverRepresentative");
                if(root==null)throw new InvalidOperationException("Missing representative scenery");
                sceneryRenderers=root.GetComponentsInChildren<Renderer>(true);originalRenderingOff=new bool[sceneryRenderers.Length];
                for(int i=0;i<sceneryRenderers.Length;i++)originalRenderingOff[i]=sceneryRenderers[i].forceRenderingOff;
                sceneryTerrains=root.GetComponentsInChildren<Terrain>(true);originalHeightmap=new bool[sceneryTerrains.Length];
                for(int i=0;i<sceneryTerrains.Length;i++)originalHeightmap[i]=sceneryTerrains[i].drawHeightmap;
            }
            for(int i=0;i<sceneryRenderers.Length;i++)if(sceneryRenderers[i]!=null)sceneryRenderers[i].forceRenderingOff=originalRenderingOff[i]||!visible;
            for(int i=0;i<sceneryTerrains.Length;i++)if(sceneryTerrains[i]!=null)sceneryTerrains[i].drawHeightmap=originalHeightmap[i]&&visible;
            // Collider, navigation, dogs, scripts and GameObject active state are untouched.
        }
        static void RestoreScenery()
        {
            if(sceneryRenderers!=null)for(int i=0;i<sceneryRenderers.Length;i++)if(sceneryRenderers[i]!=null)sceneryRenderers[i].forceRenderingOff=originalRenderingOff[i];
            if(sceneryTerrains!=null)for(int i=0;i<sceneryTerrains.Length;i++)if(sceneryTerrains[i]!=null)sceneryTerrains[i].drawHeightmap=originalHeightmap[i];
        }
        static void Finish()
        {
            Restore(); report.restoredCap=Application.targetFrameRate; report.restoredVsync=QualitySettings.vSyncCount;
            report.restored=report.restoredCap==report.originalCap && report.restoredVsync==report.originalVsync && Application.runInBackground==report.originalBackground && (!report.occlusionContribution||report.cameraOcclusionRestored);
            report.sceneDirtyAfter=SceneManager.GetActiveScene().isDirty;
            report.endedUtc=DateTime.UtcNow.ToString("o");
            try { SafeSave(); }
            finally { SessionState.SetBool(Key,false); SessionState.SetBool(Key+".entered",false); report=null; ending=false; interrupted=false;sceneryRenderers=null;sceneryTerrains=null;originalRenderingOff=null;originalHeightmap=null;occlusionCamera=null; }
        }
        static void SafeSave()
        {
            try { Save(); }
            catch(Exception e)
            {
                report.status="ERROR"; report.reason="Output could not be saved: "+e.Message;
                Debug.LogError("[KH Course Benchmark] "+report.reason);
            }
        }
        static void RecoverWithoutJournal()
        {
            Application.targetFrameRate=SessionState.GetInt(Key+".cap",-1);
            QualitySettings.vSyncCount=SessionState.GetInt(Key+".vsync",0);
            Application.runInBackground=SessionState.GetBool(Key+".background",true);
            SessionState.SetBool(Key,false); SessionState.SetBool(Key+".entered",false);
            report=null; ending=false; interrupted=false;
            if(EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.ExitPlaymode();
        }
        static void Save()
        {
            string dir="Logs/kh-course-benchmark/"+report.runId; Directory.CreateDirectory(dir);
            string json=JsonUtility.ToJson(report,true);
            File.WriteAllText(dir+"/report.json",json); File.WriteAllText(Journal,json);
            File.WriteAllText("Logs/kh-course-benchmark/latest.json",json);
        }
    }
}
