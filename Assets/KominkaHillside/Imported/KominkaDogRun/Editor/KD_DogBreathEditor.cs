using System;
using System.IO;
using System.Reflection;
using System.Text;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-DOG-023: update only breath clip references, retaining tuned audio settings.</summary>
    public static class KD_DogBreathEditor
    {
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Breath/1 - Apply Clips")]
        public static void ApplyClips()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play first");
            var log = new StringBuilder();
            var sleep = KD_WanderSetupEditor.LoadBreathClip(KD_DogDef.SleepBreathClipGuid, "sleep", log);
            var alternate = KD_WanderSetupEditor.LoadBreathClip(KD_DogDef.AlternateBreathClipGuid, "alternate", log);
            if (sleep == null || alternate == null) throw new InvalidOperationException("Missing new clips");
            // Preflight all six before changing any scene object.
            foreach (var def in KD_DogDefs.All)
            {
                var go = KD_SceneObjects.Find(def.WanderSystemName);
                if (go == null || go.GetComponent<KD_DogWanderController>() == null
                    || PrefabUtility.IsPartOfPrefabInstance(go)) throw new InvalidOperationException(def.key);
            }
            foreach (var def in KD_DogDefs.All)
            {
                var dog = KD_SceneObjects.Find(def.WanderSystemName).GetComponent<KD_DogWanderController>();
                Undo.RecordObject(dog, "Update dog breath clips");
                dog.breathSleepClip = sleep;
                dog.breathAlternateClip = alternate;
                UdonSharpEditorUtility.CopyProxyToUdon(dog);
                EditorUtility.SetDirty(dog);
                log.AppendLine("APPLIED " + def.key + " primary=" + dog.breathClip.name
                    + " volume=" + dog.breathVolume + "/" + dog.breathIdleVolume + "/" + dog.breathSleepVolume);
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            File.WriteAllText("Temp/dog-breath-apply.txt", log.ToString());
            Debug.Log(log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Breath/2 - Verify Clips")]
        public static void VerifyClips()
        {
            var log = new StringBuilder(); int fails = 0;
            Action<bool, string> check = (ok, label) => { log.AppendLine((ok ? "PASS " : "FAIL ") + label); if (!ok) fails++; };
            foreach (var def in KD_DogDefs.All)
            {
                var go = KD_SceneObjects.Find(def.WanderSystemName);
                var dog = go == null ? null : go.GetComponent<KD_DogWanderController>();
                check(dog != null, def.key + " exists"); if (dog == null) continue;
                string[] fields = { "breathClip", "breathAlternateClip", "breathSleepClip" };
                string[] guids = { def.breathClipGuid, KD_DogDef.AlternateBreathClipGuid, KD_DogDef.SleepBreathClipGuid };
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(dog);
                for (int i = 0; i < fields.Length; i++)
                {
                    var clip = (AudioClip)typeof(KD_DogWanderController).GetField(fields[i]).GetValue(dog);
                    check(clip != null && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip)) == guids[i], def.key + " " + fields[i] + " GUID");
                    object value = null;
                    check(backing != null && backing.publicVariables.TryGetVariableValue(fields[i], out value) && (AudioClip)value == clip,
                        def.key + " backing " + fields[i]);
                    check(clip != null && clip.channels == 1, def.key + " mono " + fields[i]);
                    if (clip != null) log.AppendLine("CLIP " + def.key + " " + fields[i] + " seconds=" + clip.length.ToString("F3") + " samples=" + clip.samples);
                }
                check(dog.breathSource != null && dog.breathSource != dog.barkSource && dog.breathSource.loop
                    && !dog.breathSource.playOnAwake && dog.breathSource.spatialBlend > .99f, def.key + " independent 3D loop source");
            }
            VerifySelection(check);
            log.AppendLine("RESULT failures=" + fails);
            File.WriteAllText("Temp/dog-breath-verify.txt", log.ToString());
            if (fails > 0) Debug.LogError(log.ToString()); else Debug.Log(log.ToString());
        }

        static void VerifySelection(Action<bool, string> check)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            var randomState = UnityEngine.Random.state;
            AudioClip first = null, second = null, sleep = null;
            try
            {
                var go = new GameObject("BreathSelectionFixture");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, preview);
                var dog = go.AddComponent<KD_DogWanderController>();
                first = AudioClip.Create("primary", 32, 1, 8000, false);
                second = AudioClip.Create("alternate", 32, 1, 8000, false);
                sleep = AudioClip.Create("sleep", 32, 1, 8000, false);
                dog.breathClip = first; dog.breathAlternateClip = second; dog.breathSleepClip = sleep;
                var select = typeof(KD_DogWanderController).GetMethod("SelectBreathClip", BindingFlags.Instance | BindingFlags.NonPublic);
                var level = typeof(KD_DogWanderController).GetField("_breathLevel", BindingFlags.Instance | BindingFlags.NonPublic);
                Func<bool, bool, AudioClip> pick = (sleepWins, audible) => (AudioClip)select.Invoke(dog, new object[] { sleepWins, audible });
                UnityEngine.Random.InitState(230908);
                int a = 0, b = 0; bool stable = true;
                for (int i = 0; i < 64; i++)
                {
                    level.SetValue(dog, 0f); pick(false, false);
                    var clip = pick(false, true);
                    if (clip == first) a++; if (clip == second) b++;
                    for (int j = 0; j < 10; j++) stable &= pick(false, true) == clip;
                    level.SetValue(dog, .2f); stable &= pick(false, false) == clip && pick(false, true) == clip;
                }
                check(a > 0 && b > 0 && a + b == 64, "both candidates selected across episodes: " + a + "/" + b);
                check(stable, "selection stable during playback and interrupted fadeout");
                check(pick(true, true) == sleep, "sleep always uses dedicated new clip");
                check(pick(false, true) != sleep, "waking returns to panting candidates");
                dog.breathAlternateClip = null; pick(true, true);
                check(pick(false, true) == first, "missing alternate falls back to primary");
                dog.breathClip = null; dog.breathAlternateClip = second; pick(true, true);
                check(pick(false, true) == second, "missing primary uses alternate");
                dog.breathAlternateClip = null; pick(true, true);
                check(pick(false, true) == null, "missing both returns null");
            }
            finally
            {
                UnityEngine.Random.state = randomState;
                if (first != null) UnityEngine.Object.DestroyImmediate(first);
                if (second != null) UnityEngine.Object.DestroyImmediate(second);
                if (sleep != null) UnityEngine.Object.DestroyImmediate(sleep);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }
    }
}
