using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UdonSharpEditor;

namespace KominkaDogRun.Editor
{
    public static class KD_CandyPreviewEditor
    {
        public const string ScenePath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scenes/CandyStepPreview.unity";
        public const string ScenePath002 = "Assets/KominkaHillside/Imported/KominkaDogRun/Scenes/CandyStepPreview002.unity";
        public static bool IsPreviewScene(string path) => path == ScenePath || path == ScenePath002;
        public static string CurrentScenePath => SceneManager.GetActiveScene().path == ScenePath002 ? ScenePath002 : ScenePath;
        public static string Revision => CurrentScenePath == ScenePath002 ? "002" : "001";
        public static string Evidence => Revision == "002" ? "Logs/candy-preview002" : "Logs/candy-preview";
        public static string ShowVersion => "C:/work/unity/vrc-live-generator/shows/kd-candystep/versions/" + Revision;
        public static string StageName => "LiveStage_kd-candystep-" + Revision;
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Candy Preview/1 - Preserve and Copy World")]
        public static void CopyWorld()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("One stopped source scene required");
            var source = SceneManager.GetActiveScene();
            if (source.path != "Assets/Scenes/KominkaDogRun.unity" || File.Exists(ScenePath))
                throw new InvalidOperationException("Expected original world and no existing preview copy");
            Directory.CreateDirectory(Evidence);
            var backup = Evidence + "/source-before-copy-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity";
            // The user authorized preserving current edits and making a full-world review copy.
            if (!EditorSceneManager.SaveScene(source, backup, true)) throw new IOException("Source backup failed");
            if (!EditorSceneManager.SaveScene(source)) throw new IOException("Source save failed");
            if (!AssetDatabase.IsValidFolder("Assets/KominkaHillside/Imported/KominkaDogRun/Scenes")) AssetDatabase.CreateFolder("Assets/KominkaHillside/Imported/KominkaDogRun", "Scenes");
            if (!AssetDatabase.CopyAsset(source.path, ScenePath)) throw new IOException("Copy failed");
            var sourcePath = source.path;
            var sourceHash = Hash(sourcePath);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            File.WriteAllText(Evidence + "/source.json", JsonConvert.SerializeObject(new { source = sourcePath, backup, sha256 = sourceHash, copy = ScenePath, copySha256 = Hash(ScenePath) }, Formatting.Indented));
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Candy Preview/5b - Copy Preserved Preview (002)")]
        public static void CopyPreview002()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("One stopped compiled scene is required");
            var current = SceneManager.GetActiveScene();
            if (current.isDirty || (current.path != ScenePath && current.path != "Assets/Scenes/KominkaDogRun.unity")
                || File.Exists(ScenePath002) || !File.Exists(ScenePath))
                throw new InvalidOperationException("Save the current source/001 scene; 002 must not already exist");
            const string evidence = "Logs/candy-preview002";
            Directory.CreateDirectory(evidence);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string parentHash = Hash(ScenePath), originalHash = Hash("Assets/Scenes/KominkaDogRun.unity");
            File.Copy(ScenePath, evidence + "/parent-preserved-" + stamp + ".unity", false);
            File.Copy("Assets/Scenes/KominkaDogRun.unity", evidence + "/world-preserved-" + stamp + ".unity", false);
            if (!AssetDatabase.CopyAsset(ScenePath, ScenePath002)) throw new IOException("Copy of preview001 failed");
            // Never save/revert the source scene here. Its current disk contents are the baseline.
            File.WriteAllText(evidence + "/source.json", JsonConvert.SerializeObject(new
            { source = ScenePath, sha256 = parentHash, originalSource = "Assets/Scenes/KominkaDogRun.unity", originalSha256 = originalHash,
                copy = ScenePath002, copySha256 = Hash(ScenePath002) }, Formatting.Indented));
            EditorSceneManager.OpenScene(ScenePath002, OpenSceneMode.Single);
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Candy Preview/2 - Create Dog Program")]
        public static void CreateProgram()
        {
            Directory.CreateDirectory(Evidence);
            File.WriteAllText(Evidence + "/program.txt", KD_CandyDogSetupEditor.CreateProgramAsset());
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Candy Preview/3 - Survey World")]
        public static void Survey()
        {
            var scene = SceneManager.GetActiveScene();
            if (!IsPreviewScene(scene.path)) throw new InvalidOperationException("Open CandyStep preview");
            var keys = KD_CandyDogSetupEditor.DogKeys;
            var dogs = keys.Select(key => {
                var go = KD_SceneObjects.Find("KD_Dog_" + key);
                if (go == null) throw new InvalidOperationException("Missing " + key);
                var rs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
                return new { key, position = V(go.transform.position), scale = V(go.transform.lossyScale), boundsCenter = V(b.center), boundsSize = V(b.size) };
            }).ToArray();
            var anchor = Matrix4x4.TRS(KD_DogDefs.LiveStagePosition, Quaternion.Euler(0, KD_DogDefs.LiveStageEulerY, 0), Vector3.one);
            var origins = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ShowVersion + "/stage-origins.json"))["dogs"];
            var grounds = keys.Select((key, i) => {
                var xyz = origins[i]["position"].ToObject<float[]>();
                var pos = anchor.MultiplyPoint3x4(new Vector3(xyz[0], xyz[1], xyz[2]));
                var hits = Physics.RaycastAll(pos + Vector3.up * 8, Vector3.down, 20).OrderBy(h => h.distance)
                    .Select(h => new { collider = h.collider.name, position = V(h.point), distance = h.distance }).ToArray();
                return new { key, expectedWorld = V(pos), hits };
            }).ToArray();
            Directory.CreateDirectory(Evidence);
            File.WriteAllText(Evidence + "/survey.json", JsonConvert.SerializeObject(new { scene = scene.path, dirty = scene.isDirty, dogs, stagePosition = V(KD_DogDefs.LiveStagePosition), yaw = KD_DogDefs.LiveStageEulerY, grounds }, Formatting.Indented));
        }
        // Explicit layout operation: Setup itself must preserve artist placement.
        public static string ApplyAuthoredFormation()
        {
            Guard();
            var p = UnityEngine.Object.FindObjectsOfType<KD_CandyDogPerformance>(true).Single(x => x.gameObject.scene == SceneManager.GetActiveScene());
            var origins = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ShowVersion + "/stage-origins.json"))["dogs"];
            if (p.slots == null || p.slots.Length != 6 || origins.Count() != 6) throw new InvalidOperationException("Six authored slots required");
            for (int i = 0; i < 6; i++)
            {
                if ((string)origins[i]["key"] != KD_CandyDogSetupEditor.DogKeys[i] || p.slots[i] == null) throw new InvalidOperationException("Slot order differs");
                var xyz = origins[i]["position"].ToObject<float[]>();
                Undo.RecordObject(p.slots[i], "Candy authored formation");
                p.slots[i].position = p.director.transform.TransformPoint(new Vector3(xyz[0], xyz[1], xyz[2]));
                p.slots[i].rotation = p.director.transform.rotation * Quaternion.Euler(0, 180, 0);
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Survey();
            return "Applied six authored positions; survey and Verify required before save.";
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Candy Preview/4 - Wire Preview")]
        public static void Wire()
        {
            Guard();
            var stage = KD_SceneObjects.Find(StageName, out int count);
            if (stage == null || count != 1) throw new InvalidOperationException("Import exactly one Candy stage first");
            var director = stage.GetComponent<PlayableDirector>();
            if (director == null || director.playableAsset == null) throw new InvalidOperationException("Timeline missing");
            var live = UnityEngine.Object.FindObjectsOfType<KD_LiveSystem>(true).Single(x => x.gameObject.scene == stage.scene);
            if (live.stageDirectors == null || live.stageDirectors.Length == 0) throw new InvalidOperationException("Original live wiring missing");
            var places = KD_SceneObjects.Find("KD_Places");
            if (places == null || !places.activeInHierarchy) throw new InvalidOperationException("Active world places container missing");
            var root = KD_SceneObjects.Find("KD_CandyPreview");
            bool fresh = root == null;
            if (fresh) root = new GameObject("KD_CandyPreview");
            if (root.transform.parent != places.transform) root.transform.SetParent(places.transform, true);
            // Preview-local placement, isolated from the original scene and live catalog.
            if (stage.transform.parent != root.transform)
            {
                stage.transform.SetParent(root.transform, true);
                stage.transform.SetPositionAndRotation(KD_DogDefs.LiveStagePosition, Quaternion.Euler(0, KD_DogDefs.LiveStageEulerY, 0));
                stage.transform.localScale = Vector3.one;
            }
            var performance = KD_CandyDogSetupEditor.Setup(root, director, stage.transform, ShowVersion + "/dog-cues.json");
            if (Revision == "002") KD_CandyDogSetupEditor.SetupChoreography(performance, ShowVersion);
            performance.autoStartPreview = false;
            performance.autoBeginPerformance = false;
            UdonSharpEditorUtility.CopyProxyToUdon(performance);
            if (live.stageRoots[0] != null && live.stageRoots[0] != stage) live.stageRoots[0].SetActive(false);
            live.stageDirectors[0] = director; live.stageRoots[0] = stage;
            live.stageDurations[0] = 124.8f; live.stageDarkness[0] = .45f;
            // Real stage lights: present during the user's playback, not capture-only correction.
            for (int i = 0; i < 2; i++)
            {
                string lightName = "KD_CandyFaceFill_" + i;
                var child = stage.transform.Find(lightName);
                if (child == null) { child = new GameObject(lightName).transform; child.SetParent(stage.transform, false); }
                child.localPosition = new Vector3(i == 0 ? -2.5f : 2.5f, 1.5f, 1.5f);
                var fill = child.GetComponent<Light>();
                if (fill == null) fill = child.gameObject.AddComponent<Light>();
                fill.type = LightType.Point; fill.color = new Color(1f, .93f, .87f);
                fill.intensity = 2f; fill.range = 5.5f; fill.shadows = LightShadows.None;
                fill.renderMode = LightRenderMode.ForcePixel;
                child.gameObject.SetActive(true);
            }
            // Same host contract as the regular Live setup: light draws above
            // the night overlay, rather than being dimmed with the world.
            foreach (var material in stage.GetComponentsInChildren<ParticleSystemRenderer>(true)
                .SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct())
            {
                var path = AssetDatabase.GetAssetPath(material);
                if (!path.StartsWith("Assets/VirtualCampers.LiveGenerator/Shows/kd-candystep-" + Revision + "/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unexpected shared particle material " + path);
                material.renderQueue = KD_LiveSetupEditor.LiveParticleQueue;
                EditorUtility.SetDirty(material);
            }
            UdonSharpEditorUtility.CopyProxyToUdon(live);
            EditorUtility.SetDirty(live); EditorUtility.SetDirty(performance);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            File.WriteAllText(Evidence + "/wired.txt", "Candy preview slot 0 wired; independent Verify required before save.");
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Candy Preview/5 - Verify and Save")]
        public static void VerifyAndSave()
        {
            Guard();
            var stage = KD_SceneObjects.Find(StageName);
            var performance = UnityEngine.Object.FindObjectsOfType<KD_CandyDogPerformance>(true).Single(p => p.gameObject.scene == stage.scene);
            var report = KD_CandyDogSetupEditor.Verify(performance);
            if (!report.Contains("RESULT PASS")) throw new InvalidOperationException(report);
            if (Revision == "002") report += "\n" + KD_CandyDogSetupEditor.VerifyChoreography(performance);
            else if (performance.useChoreography) throw new InvalidOperationException("Preserved 001 may not enable choreography");
            var live = UnityEngine.Object.FindObjectsOfType<KD_LiveSystem>(true).Single(x => x.gameObject.scene == stage.scene);
            var d = stage.GetComponent<PlayableDirector>();
            if (live.stageDirectors[0] != d || live.stageRoots[0] != stage || Math.Abs(live.stageDurations[0] - 124.8) > .001 || performance.autoStartPreview || performance.autoBeginPerformance)
                throw new InvalidOperationException("Live/coordinator binding differs");
            var timeline = d.playableAsset as TimelineAsset;
            var clips = timeline.GetOutputTracks().OfType<AudioTrack>().SelectMany(t => t.GetClips()).Select(c => c.asset as AudioPlayableAsset).Where(a => a != null && a.clip != null).ToArray();
            if (clips.Length != 1 || Math.Abs(clips[0].clip.length - 124.8) > .002) throw new InvalidOperationException("One full-length audio clip required");
            var handoff = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("C:/work/unity/vrc-music-agent/songs/KDR_CandyStep/versions/" + Revision + "/events/handoff.json"));
            var audioHash = Hash(AssetDatabase.GetAssetPath(clips[0].clip));
            if (audioHash != (string)handoff["audio"]["sha256"])
                throw new InvalidOperationException("Imported audio differs from the music handoff hash");
            var ps = stage.GetComponentsInChildren<ParticleSystem>(true);
            var containers = ps.Where(p => p.main.maxParticles == 0 && !p.emission.enabled
                && !p.GetComponent<ParticleSystemRenderer>().enabled && p.GetComponent<ParticleSystemRenderer>().renderMode == ParticleSystemRenderMode.None).ToArray();
            var materialMissing = ps.Except(containers).Count(p => p.GetComponent<ParticleSystemRenderer>().sharedMaterial == null);
            if (ps.Length == 0 || materialMissing > 0) throw new InvalidOperationException("Particle/material missing");
            var lightMaterials = stage.GetComponentsInChildren<ParticleSystemRenderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
            if (lightMaterials.Length == 0 || lightMaterials.Any(m => m.renderQueue != KD_LiveSetupEditor.LiveParticleQueue))
                throw new InvalidOperationException("Live particles would draw behind the night overlay");
            var source = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(File.ReadAllText(Evidence + "/source.json"));
            if (Hash((string)source["source"]) != (string)source["sha256"]) throw new InvalidOperationException("Parent source scene has changed since copy");
            string originalHash = (string)source["originalSha256"] ?? (string)source["sha256"];
            if (Hash("Assets/Scenes/KominkaDogRun.unity") != originalHash) throw new InvalidOperationException("Current original world changed since copy; do not restore an older world");
            report += "\nAudio=" + AssetDatabase.GetAssetPath(clips[0].clip) + " sha256=" + audioHash + " duration=" + clips[0].clip.length + " PS=" + ps.Length + " inactive container PS=" + containers.Length + " light materials=" + lightMaterials.Length + " queue=" + KD_LiveSetupEditor.LiveParticleQueue + "\nOriginal source hash unchanged.\n";
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new IOException("Preview save failed");
            File.WriteAllText(Evidence + "/construction.txt", report);
        }
        private static void Guard()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount != 1 || !IsPreviewScene(SceneManager.GetActiveScene().path))
                throw new InvalidOperationException("Stopped CandyStepPreview scene required");
            Directory.CreateDirectory(Evidence);
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Candy Preview/5c - Survey Choreography Ground")]
        public static void SurveyChoreographyGround()
        {
            Guard();
            var p = UnityEngine.Object.FindObjectsOfType<KD_CandyDogPerformance>(true).Single(x => x.gameObject.scene == SceneManager.GetActiveScene());
            File.WriteAllText(Evidence + "/choreography-ground.txt", KD_CandyDogSetupEditor.SurveyChoreographyGround(p));
        }
        private static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
        private static string Hash(string path)
        {
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
    }
}
