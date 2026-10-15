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
    // Bounded local VM test only. No scene save, proxy method invocation, ownership transfer or fabricated ACK.
    [InitializeOnLoad]
    public static partial class KH_HalloweenParadeHarnessEditor
    {
        const string Key = "KH.HalloweenParadeHarness", Dir = "Logs/kh-halloween-parade-harness", Journal = Dir + "/journal.json";
        const int FaultDog = 5, HealthyMask = 31;
        [Serializable] public class Report
        {
            public string runId, status = "RUNNING", reason, utc, endedUtc, scene, hashBefore, hashAfter, sourceHashBefore, sourceHashAfter, assembly;
            public string playerDataBackup;
            public bool originalBackground, sceneFileUnchanged, sourceUnchanged, sceneCleanAfter, playerDataRestored, settingsRestored, runtimeOverridesRestored;
            public bool normalPassed, failurePassed, faultWindowCaptured, petBeforePoseRecovered;
            public bool danceCapture, originalDevelopment, nightCheck, nightPassed;
            public Vector3 captureEye,captureTarget,captureFollowEye;
            public int capturedFrames;
            public int normalRequest, failureRequest, faultDog = FaultDog, failedMask, externalClientErrors;
            public double deadline;
            public List<string> observations = new List<string>();
            public List<string> errors = new List<string>();
            public string limitations = "One local ClientSim / actual Udon VM. Idle dogs only; runtime development gate enabled for this run. "
                + "Disabled adapter injection models missing end/restore ACK, not real ownership transfer. Getter VM results prove admission gates, not successful starts of every game. "
                + "No multi-user consent, washing users, real networking, late join, full 220-second performance, audiovisual appearance or device certification. "
                + "Stable cleanup counters/receipts do not count every internal idempotent method call.";
        }
        static Report report;
        static bool ending;
        static int stage, lastFrame = -1, lastPhase = -1;
        static float playAt, caseAt, stageAt;
        static UdonBehaviour manager;
        static UdonBehaviour[] dogs, wanders, washes, pets;
        static bool faultDisabled, faultOriginalEnabled;
        static string terminalCounters;

        static KH_HalloweenParadeHarnessEditor()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Mode;
            Application.logMessageReceived += Log;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            if (!SessionState.GetBool(Key, false)) return;
            try { report = JsonUtility.FromJson<Report>(File.ReadAllText(Journal)); ending = report.status != "RUNNING"; }
            catch (Exception e) { Debug.LogError("[KH Parade Harness] unreadable journal: " + e); SessionState.SetBool(Key, false); }
        }
        [MenuItem("Tools/KominkaHillside/Parade/10 - Bounded Harness")]
        public static void Run()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            if (SessionState.GetBool(Key, false)) throw new InvalidOperationException("Parade harness already active");
            var configured = UnityEngine.Object.FindObjectsOfType<KH_HalloweenParade>(true);
            if (configured.Length != 1)
                throw new InvalidOperationException("Expected one configured parade");
            if(configured[0].hostingProduct==null||configured[0].hostingProduct.ceProduct==null)
                throw new InvalidOperationException("Paid parade requires dedicated CE IDs and an owned ClientSim fixture. Public preview was retired.");
            report = new Report { runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                utc = DateTime.UtcNow.ToString("o"), scene = SceneManager.GetActiveScene().path, originalBackground = Application.runInBackground,
                deadline = EditorApplication.timeSinceStartup + 150, assembly = typeof(KH_HalloweenParade).Assembly.ManifestModule.ModuleVersionId.ToString(),
                originalDevelopment=configured[0].developmentEnabled,danceCapture=SessionState.GetBool(Key+".capture",false),nightCheck=SessionState.GetBool(Key+".night",false) };
            SessionState.SetBool(Key+".capture",false);
            if(report.danceCapture)
            {
                var m=configured[0];var centre=(m._DogTargetAt(65,0)+m._DogTargetAt(65,5))*.5f;var forward=m._DogFacingAt(54,0);
                report.captureEye=centre+Vector3.Cross(Vector3.up,forward)*7.2f-forward*2.8f+Vector3.up*1.6f;
                report.captureFollowEye=centre-forward*5.5f+Vector3.Cross(Vector3.up,forward)*.8f+Vector3.up*1.6f;
                report.captureTarget=centre+Vector3.up*.45f;
                report.limitations="One local ClientSim / actual Udon VM. Real start and 0-82 s progression, no seek. Captures 55-80 s from fixed roadside and following eyes. Not full 220 s, multiplayer, device or entertainment acceptance. Audio mux is source aligned to frames.csv, not loopback recording.";
            }
            if(report.nightCheck)report.limitations="Bounded local Udon VM: actual start 0-6s, playback disable/reenable and cancel. No full 220s endpoint, real late join, device/visual/audio acceptance. Night Auto/manual preferences checked unchanged.";
            report.hashBefore = Hash(report.scene); report.sourceHashBefore = SourceHash();
            BackupPlayerData();
            ending = false; stage = 0; manager = null; dogs = null; lastFrame = -1; lastPhase = -1; faultDisabled = false;
            Application.runInBackground = true;
            Save(); SessionState.SetBool(Key, true);
            try { EditorApplication.EnterPlaymode(); }
            catch (Exception e) { End("INVALID_SETUP", e.ToString()); }
        }
        [MenuItem("Tools/KominkaHillside/Parade/10b - Stop Harness")]
        public static void Stop() { End("STOPPED", "operator stop"); }
        [MenuItem("Tools/KominkaHillside/Parade/25 - Dance Capture Harness")]
        public static void RunDance(){SessionState.SetBool(Key+".capture",true);try{Run();}finally{SessionState.SetBool(Key+".capture",false);}}
        static void BeforeReload()
        {
            if (report != null && SessionState.GetBool(Key, false) && EditorApplication.isPlaying)
                End("INVALID_SETUP", "Assembly reload during the observed Play run");
        }
        static void Mode(PlayModeStateChange state)
        {
            if (report == null || !SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { playAt = Time.time; stageAt = Time.time; stage = 0; }
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                CleanupRuntime();
                if (!ending) { ending = true; report.status = "STOPPED"; report.reason = "Play stopped externally"; SafeSave(); }
            }
            if (state == PlayModeStateChange.EnteredEditMode) Finish();
        }
        static void Tick()
        {
            if (report == null || !SessionState.GetBool(Key, false)) return;
            if (ending) { if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish(); else if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); return; }
            if (EditorApplication.timeSinceStartup > report.deadline) { End("TIMEOUT", "150-second Editor watchdog"); return; }
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try
            {
                if (report.errors.Count > 0) { End("FAIL", "Runtime errors: " + report.errors[0]); return; }
                Advance();
            }
            catch (InvalidOperationException e) { End("INVALID_SETUP", e.ToString()); }
            catch (Exception e) { End("FAIL", e.ToString()); }
        }
        static void Log(string condition, string stack, LogType type)
        {
            if (report == null || !SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
            if (condition != null && condition.Contains("Runtime error detected, but the client has not been launched with")) { report.externalClientErrors++; return; }
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && report.errors.Count < 15)
                report.errors.Add(condition + "\n" + stack);
        }
        static void Note(string text)
        { report.observations.Add("t=" + Time.time.ToString("F3") + " frame=" + Time.frameCount + " " + text); Save(); }
        static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Setup(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void ToStage(int next) { stage = next; stageAt = Time.time; }
        static void End(string status, string reason)
        {
            if (report == null || ending) return;
            if (status == "PASS" && report.errors.Count > 0) { status = "FAIL"; reason += " | runtime errors observed"; }
            ending = true; report.status = status; report.reason = reason;
            CleanupRuntime(); SafeSave();
            Debug.Log("[KH Parade Harness] PARADE_HARNESS_" + status + " " + reason);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode(); else Finish();
        }
        static void CleanupRuntime()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                if (faultDisabled && dogs != null && dogs.Length == 6 && dogs[FaultDog] != null)
                { dogs[FaultDog].enabled = faultOriginalEnabled; faultDisabled = false; }
                DisposeDanceCapture();CleanupNight();
                if (manager != null) { manager.SetProgramVariable("developmentEnabled", report.originalDevelopment); manager.SendCustomEvent("_Cancel"); }
                report.runtimeOverridesRestored = !faultDisabled && (manager == null || Get<bool>(manager, "developmentEnabled")==report.originalDevelopment)
                    && (!report.faultWindowCaptured || (dogs != null && dogs[FaultDog] != null && dogs[FaultDog].enabled == faultOriginalEnabled));
            }
            catch (Exception e) { report.runtimeOverridesRestored = false; report.errors.Add("cleanup: " + e); }
        }
        static void Finish()
        {
            if (report == null) return;
            try
            {
                Application.runInBackground = report.originalBackground;
                report.settingsRestored = Application.runInBackground == report.originalBackground;
                RestorePlayerData(); report.playerDataRestored = true;
                report.hashAfter = Hash(report.scene); report.sceneFileUnchanged = report.hashBefore == report.hashAfter;
                report.sourceHashAfter = SourceHash(); report.sourceUnchanged = report.sourceHashBefore == report.sourceHashAfter;
                report.sceneCleanAfter = !SceneManager.GetActiveScene().isDirty;
                var configured = UnityEngine.Object.FindObjectsOfType<KH_HalloweenParade>(true);
                report.settingsRestored &= configured.Length == 1 && configured[0].developmentEnabled==report.originalDevelopment;
                if (report.status == "PASS" && (!report.sceneFileUnchanged || !report.sourceUnchanged || !report.sceneCleanAfter
                    || !report.settingsRestored || !report.runtimeOverridesRestored)) { report.status = "FAIL"; report.reason += " | preservation check failed"; }
                report.endedUtc = DateTime.UtcNow.ToString("o");
            }
            catch (Exception e) { report.status = "EVIDENCE_ERROR"; report.reason = e.ToString(); report.endedUtc = DateTime.UtcNow.ToString("o"); }
            finally
            {
                SafeSave(); SessionState.SetBool(Key, false);
                Debug.Log("[KH Parade Harness] PARADE_HARNESS_" + report.status + " finished " + report.runId);
                report = null; ending = false; manager = null; dogs = null;
            }
        }
        static string Hash(string file)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-", ""); }
        static string SourceHash()
        {
            var paths = Directory.GetFiles("Assets/KominkaHillside/Scripts", "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles("Assets/KominkaHillside/Imported/KominkaDogRun/Scripts", "*.cs"))
                .OrderBy(p => p, StringComparer.Ordinal).Select(p => p + ":" + Hash(p));
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", paths)))).Replace("-", "");
        }
        static void BackupPlayerData()
        {
            report.playerDataBackup = Dir + "/" + report.runId + "/playerdata"; Directory.CreateDirectory(report.playerDataBackup);
            string storage = Path.GetFullPath("ClientSimStorage/PlayerData");
            var files = Directory.Exists(storage) ? Directory.GetFiles(storage, "*_VRCDefaultWorldScene.json") : new string[0];
            foreach (var path in files) File.Copy(path, report.playerDataBackup + "/" + Path.GetFileName(path), true);
            File.WriteAllLines(report.playerDataBackup + "/manifest.txt", files.Select(Path.GetFileName));
        }
        static void RestorePlayerData()
        {
            if (string.IsNullOrEmpty(report.playerDataBackup)) return;
            string storage = Path.GetFullPath("ClientSimStorage/PlayerData"); Directory.CreateDirectory(storage);
            var names = File.ReadAllLines(report.playerDataBackup + "/manifest.txt");
            foreach (var name in names)
            {
                if (Path.GetFileName(name) != name || !name.EndsWith("_VRCDefaultWorldScene.json", StringComparison.Ordinal)) throw new InvalidDataException("unsafe backup name");
                File.Copy(report.playerDataBackup + "/" + name, Path.Combine(storage, name), true);
            }
            foreach (var path in Directory.GetFiles(storage, "*_VRCDefaultWorldScene.json"))
                if (!names.Contains(Path.GetFileName(path))) File.Delete(path);
        }
        static void Save()
        {
            Directory.CreateDirectory(Dir + "/" + report.runId); string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Dir + "/" + report.runId + "/report.json", json); File.WriteAllText(Journal, json); File.WriteAllText(Dir + "/latest.json", json);
        }
        static void SafeSave() { try { Save(); } catch (Exception e) { Debug.LogError("[KH Parade Harness] evidence: " + e); } }
    }
}
