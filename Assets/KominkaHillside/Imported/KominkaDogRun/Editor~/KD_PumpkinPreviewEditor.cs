using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace KominkaDogRun.Editor
{
    /// <summary>Isolated Halloween preview; the source world is never saved by this tool.</summary>
    public static partial class KD_PumpkinPreviewEditor
    {
        public const string SourcePath = "Assets/Scenes/KominkaDogRun.unity";
        public const string ScenePath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scenes/PumpkinParadePreview001.unity";
        public const string Evidence = "Logs/pumpkin-preview001";

        // A new checkout has the authored copy but none of the original local audit files.
        // Establish a NEW preservation baseline; never replace an existing session's evidence.
        public static void InitializeRestoredCopyBaseline()
        {
            GuardProject();
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
                || SceneManager.sceneCount != 1 || scene.path != ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open the single saved, stopped restored Pumpkin copy.");
            if (File.Exists(Evidence + "/source.json") || File.Exists(Evidence + "/costume-before.json"))
                throw new InvalidOperationException("Preservation evidence already exists; never replace it.");
            var p = One<KD_CandyDogPerformance>();
            if (p.costumeNodes == null || p.costumeNodes.Length != 18)
                throw new InvalidOperationException("Restore the eighteen costume references first.");
            for (int i = 0; i < 18; i++)
                if (p.costumeNodes[i] == null) throw new InvalidOperationException("A restored costume is missing.");
            int lights = 0;
            foreach (var light in UnityEngine.Object.FindObjectsOfType<UnityEngine.Light>(true))
                if (light.gameObject.scene == scene) lights++;
            if (lights != 36) throw new InvalidOperationException("Restore the accepted scene assets first: expected 36 existing Lights.");
            string hash = Hash(SourcePath), copyHash = Hash(ScenePath);
            Directory.CreateDirectory(Evidence);
            string backup = Evidence + "/world-restored-baseline-" + Guid.NewGuid().ToString("N") + ".unity";
            File.Copy(SourcePath, backup, false);
            if (Hash(backup) != hash || Hash(SourcePath) != hash) throw new IOException("Restored source backup hash mismatch.");
            const string scope = "New checkout baseline only; not the original production-session preservation evidence.";
            File.WriteAllText(Evidence + "/costume-before.json", JsonConvert.SerializeObject(new
            { scope, scene = ScenePath, sourceSha256 = hash, previewSha256 = copyHash, lights }, Formatting.Indented));
            File.WriteAllText(Evidence + "/source.json", JsonConvert.SerializeObject(new
            { scope, checkedAt = DateTime.UtcNow, source = SourcePath, sha256 = hash, backup, copy = ScenePath, copySha256 = copyHash }, Formatting.Indented));
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pumpkin Preview/1 - Copy World")]
        public static void CopyWorld()
        {
            GuardProject();
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
                || SceneManager.sceneCount != 1 || scene.path != SourcePath || scene.isDirty)
                throw new InvalidOperationException("One stopped, saved source world is required.");
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Preview exists; do not overwrite it.");
            Directory.CreateDirectory(Evidence);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string backup = Evidence + "/world-preserved-" + stamp + ".unity";
            string hash = Hash(SourcePath);
            File.Copy(SourcePath, backup, false);
            if (!AssetDatabase.IsValidFolder("Assets/KominkaHillside/Imported/KominkaDogRun/Scenes"))
                AssetDatabase.CreateFolder("Assets/KominkaHillside/Imported/KominkaDogRun", "Scenes");
            if (!AssetDatabase.CopyAsset(SourcePath, ScenePath)) throw new IOException("Preview copy failed.");
            if (Hash(ScenePath) != hash || Hash(SourcePath) != hash) throw new IOException("Copy hash mismatch.");
            File.WriteAllText(Evidence + "/source.json", JsonConvert.SerializeObject(new
            { source = SourcePath, sha256 = hash, backup, copy = ScenePath, copySha256 = Hash(ScenePath) }, Formatting.Indented));
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pumpkin Preview/2 - Verify Source Preservation")]
        public static void VerifySource()
        {
            GuardProject();
            Directory.CreateDirectory(Evidence);
            var started = DateTime.UtcNow.ToString("o");
            File.WriteAllText(Evidence + "/source-verification.json", JsonConvert.SerializeObject(new
            { sourceUnchanged = false, state = "verification-incomplete", checkedAt = started }, Formatting.Indented));
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open the stopped Pumpkin preview.");
            var record = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Evidence + "/source.json"));
            if (Hash(SourcePath) != (string)record["sha256"] || Hash((string)record["backup"]) != (string)record["sha256"])
                throw new InvalidOperationException("Original world differs from the baseline; do not restore an older scene.");
            File.WriteAllText(Evidence + "/source-verification.json", JsonConvert.SerializeObject(new
            { sourceUnchanged = true, checkedAt = started, sourceSha256 = Hash(SourcePath), scene = scene.path, dirty = scene.isDirty, previewSha256 = Hash(ScenePath),
                stage = "source-preservation check only; see construction.json for installation" }, Formatting.Indented));
        }

        private static void GuardProject()
        {
            if (!string.Equals(UnityEngine.Application.dataPath.Replace('\\', '/'),
                "C:/work/unity/vcc/KominkaDogRun/Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This preview belongs to the KominkaDogRun workspace.");
        }

        private static string Hash(string path)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
    }
}
