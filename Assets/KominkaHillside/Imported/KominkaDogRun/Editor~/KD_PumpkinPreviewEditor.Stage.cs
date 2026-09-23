using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UdonSharpEditor;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PumpkinPreviewEditor
    {
        public const string ShowVersion = "C:/work/unity/vrc-live-generator/shows/kd-pumpkinparade/versions/002";
        public const string StageName = "LiveStage_kd-pumpkinparade-002";
        private const string Generated = "Assets/VirtualCampers.LiveGenerator/Shows/kd-pumpkinparade-002/";
        private static string ShowPlanPath => ShowVersion + "/" + StageName.Substring("LiveStage_".Length) + ".showplan.json";
        private static string EffectsPath => ShowVersion + "/effects.pumpkin" + StageName.Substring(StageName.Length - 3) + ".json";
        private static void GuardStage()
        {
            GuardProject();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
                || SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Stopped, compiled Pumpkin preview only.");
            VerifySource();
        }
        private static T One<T>() where T : Component => UnityEngine.Object.FindObjectsOfType<T>(true)
            .Single(c => c.gameObject.scene == SceneManager.GetActiveScene());

        public static void InspectBodyMeshes()
        {
            GuardStage();
            var meshes = KD_SceneObjects.Find(StageName).GetComponentsInChildren<ParticleSystemRenderer>(true)
                .Where(r => r.renderMode == ParticleSystemRenderMode.Mesh).Select(r => r.mesh).Distinct();
            var data = meshes.Select(m => new { m.name, vertices = m.vertices.Select(v => new[] { v.x, v.y, v.z }),
                normals = m.normals.Select(v => new[] { v.x, v.y, v.z }), uv = m.uv.Select(v => new[] { v.x, v.y }), triangles = m.triangles });
            File.WriteAllText(Evidence + "/body-meshes.json", JsonConvert.SerializeObject(data));
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pumpkin Preview/3 - Wire Authored Stage")]
        public static void WireStage()
        {
            GuardStage();
            var stage = KD_SceneObjects.Find(StageName, out int count);
            if (stage == null || count != 1) throw new InvalidOperationException("Import exactly one Pumpkin stage.");
            var director = stage.GetComponent<PlayableDirector>();
            if (director == null || director.playableAsset == null) throw new InvalidOperationException("Timeline missing.");
            var p = One<KD_CandyDogPerformance>(); var live = One<KD_LiveSystem>();
            var places = KD_SceneObjects.Find("KD_Places");
            if (places == null || !places.activeInHierarchy || live.stageRoots.Length == 0)
                throw new InvalidOperationException("Original host wiring missing.");
            if (p.dogs == null || p.dogs.Length != 6 || p.costumeNodes == null || p.costumeNodes.Length != 18
                || p.dogs.Any(d => d == null) || p.costumeNodes.Any(n => n == null))
                throw new InvalidOperationException("Expected preserved six-dog Candy ensemble and fitted outfits.");
            var cues = JObject.Parse(File.ReadAllText(ShowVersion + "/dog-cues.json"));
            var keys = cues["keys"].ToObject<string[]>();
            if (!keys.SequenceEqual(KD_CandyDogSetupEditor.DogKeys)) throw new InvalidOperationException("Roster order differs.");
            var positions = cues["dogOrigins"].ToObject<Vector3[]>();
            var materials = stage.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Where(m => m != null).Distinct().ToArray();
            if (materials.Length == 0 || materials.Any(m => !AssetDatabase.GetAssetPath(m).StartsWith(Generated, StringComparison.Ordinal)))
                throw new InvalidOperationException("Unscoped particle material.");
            var root = KD_SceneObjects.Find("KD_PumpkinPreview");
            if (root == null) { root = new GameObject("KD_PumpkinPreview"); root.transform.SetParent(places.transform, false); }
            if (root.transform.parent != places.transform) throw new InvalidOperationException("Preview parent differs.");
            // Explicit authored layout in the isolated copy. Reimported stages return to this anchor.
            stage.transform.SetParent(root.transform, false);
            stage.transform.SetPositionAndRotation(KD_DogDefs.LiveStagePosition, Quaternion.Euler(0, KD_DogDefs.LiveStageEulerY, 0));
            stage.transform.localScale = Vector3.one;
            var slots = new Transform[6];
            for (int i = 0; i < 6; i++)
            {
                string name = "KD_PumpkinSlot_" + keys[i];
                var slot = stage.transform.Find(name);
                if (slot == null) { slot = new GameObject(name).transform; slot.SetParent(stage.transform, false); }
                slot.localPosition = positions[i]; slot.localRotation = Quaternion.Euler(0, 180, 0); slots[i] = slot;
                if (p.dogs[i].dogRoot.name != "KD_Dog_" + keys[i]) throw new InvalidOperationException("Wrong preserved dog.");
            }
            p.previewSceneOnly = true; p.autoStartPreview = false; p.autoBeginPerformance = false;
            p.director = director; p.slots = slots;
            p.cueTimes = cues["cueTimes"].ToObject<float[]>(); p.cueMasks = cues["cueMasks"].ToObject<int[]>();
            p.cueYaw = cues["cueYaw"].ToObject<float[]>(); p.cueSteps = cues["cueSteps"].ToObject<float[]>();
            p.duration = 120f; p.sitTime = (float)cues["sitTime"];
            director.playOnAwake = false; director.extrapolationMode = DirectorWrapMode.Hold;
            // The shared importer caps activation tails at 12 s. This authored arch uses 18 s particles.
            // Recompute from the frozen plan, rather than adding time on each wiring pass.
            var authoredEffects = JObject.Parse(File.ReadAllText(ShowPlanPath))
                ["sections"].SelectMany(s => s["effects"]).ToArray();
            foreach (var track in ((TimelineAsset)director.playableAsset).GetOutputTracks().OfType<ActivationTrack>())
            {
                var bound = director.GetGenericBinding(track) as GameObject;
                if (bound == null || !bound.GetComponentsInChildren<ParticleSystem>(true).Any(s => s.name == "ArchColour")) continue;
                var authored = authoredEffects.Where(e => (string)e["type"] + "_" + (string)e["preset"] == bound.name)
                    .OrderBy(e => (double)e["startTime"]).ToArray();
                var activations = track.GetClips().OrderBy(c => c.start).ToArray();
                if (authored.Length != activations.Length) throw new InvalidOperationException("Arch activation intervals differ.");
                double tail = bound.GetComponentsInChildren<ParticleSystem>(true).Max(s => s.main.startLifetime.constantMax);
                for (int a = 0; a < activations.Length; a++)
                    activations[a].duration = Math.Min(120, (double)authored[a]["endTime"] + tail) - activations[a].start;
                EditorUtility.SetDirty(track);
            }
            EditorUtility.SetDirty(director.playableAsset);
            if (live.stageRoots[0] != null && live.stageRoots[0] != stage) live.stageRoots[0].SetActive(false);
            live.stageRoots[0] = stage; live.stageDirectors[0] = director; live.stageDurations[0] = 120f; live.stageDarkness[0] = .45f;
            KD_CandyDogSetupEditor.SetupChoreography(p, ShowVersion);
            for (int i = 0; i < 2; i++)
            {
                var tr = stage.transform.Find("KD_PumpkinFaceFill_" + i);
                if (tr == null) { tr = new GameObject("KD_PumpkinFaceFill_" + i).transform; tr.SetParent(stage.transform, false); }
                tr.localPosition = new Vector3(i == 0 ? -2.5f : 2.5f, 1.5f, 1.5f);
                var light = tr.GetComponent<Light>(); if (light == null) light = tr.gameObject.AddComponent<Light>();
                light.type = LightType.Point; light.color = new Color(1, .93f, .87f); light.intensity = 2;
                light.range = 5.5f; light.shadows = LightShadows.None; light.renderMode = LightRenderMode.ForcePixel;
            }
            foreach (var material in materials) { material.renderQueue = KD_LiveSetupEditor.LiveParticleQueue; EditorUtility.SetDirty(material); }
            // Opaque palette faces need depth writes; legacy particle blending shows the cloth through the eyes.
            WireStableBodies(stage, director);
            var bodyMaterials = stage.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => StableBodyNames.Contains(r.name)).Select(r => r.sharedMaterial).Distinct().ToArray();
            var bodyShader = Shader.Find("KominkaDogRun/Pumpkin Palette");
            if (bodyShader == null) throw new InvalidOperationException("Standard body shader missing.");
            foreach (var material in bodyMaterials)
            {
                var paletteTexture = material.mainTexture;
                material.shader = bodyShader; material.shaderKeywords = new string[0];
                material.mainTexture = paletteTexture; material.color = Color.white;
                material.SetFloat("_Metallic", 0); material.SetFloat("_Glossiness", .15f);
                material.SetTexture("_EmissionMap", paletteTexture); material.SetColor("_EmissionColor", Color.white * .25f);
                material.EnableKeyword("_EMISSION"); material.renderQueue = KD_LiveSetupEditor.LiveParticleQueue;
                EditorUtility.SetDirty(material);
            }
            WirePropMotion(stage, director);
            UdonSharpEditorUtility.CopyProxyToUdon(p); UdonSharpEditorUtility.CopyProxyToUdon(live);
            EditorUtility.SetDirty(p); EditorUtility.SetDirty(live); AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(stage.scene);
            File.WriteAllText(Evidence + "/wired.txt", "Pumpkin stage and preserved ensemble wired; verify before saving.");
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pumpkin Preview/4 - Verify and Save Preview")]
        public static void VerifyStageAndSave()
        {
            GuardStage();
            File.WriteAllText(Evidence + "/construction.json", "{\"complete\":false}");
            var p = One<KD_CandyDogPerformance>(); var live = One<KD_LiveSystem>(); var stage = KD_SceneObjects.Find(StageName);
            if (stage == null || p.director == null || p.director.gameObject != stage || live.stageRoots[0] != stage
                || live.stageDirectors[0] != p.director || Math.Abs(p.duration - 120) > .001 || p.autoStartPreview || p.autoBeginPerformance)
                throw new InvalidOperationException("Preview host/coordinator binding differs.");
            var report = KD_CandyDogSetupEditor.Verify(p);
            if (!report.Contains("RESULT PASS")) throw new InvalidOperationException(report);
            report += "\n" + KD_CandyDogSetupEditor.VerifyChoreography(p);
            var timeline = p.director.playableAsset as TimelineAsset;
            var clips = timeline.GetOutputTracks().OfType<AudioTrack>().SelectMany(t => t.GetClips())
                .Select(c => (c.asset as AudioPlayableAsset)?.clip).Where(c => c != null).ToArray();
            string expected = (string)JObject.Parse(File.ReadAllText(ShowVersion + "/music-handoff.json"))["audio"]["sha256"];
            if (clips.Length != 1 || Hash(AssetDatabase.GetAssetPath(clips[0])) != expected || Math.Abs(clips[0].length - 120) > .01)
                throw new InvalidOperationException("Imported audio differs from frozen music.");
            var ps = stage.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var particle in ps)
            {
                var renderer = particle.GetComponent<ParticleSystemRenderer>();
                if (renderer.renderMode == ParticleSystemRenderMode.None) continue;
                if (renderer.sharedMaterial == null || renderer.sharedMaterial.renderQueue != KD_LiveSetupEditor.LiveParticleQueue
                    || renderer.sharedMaterial.mainTexture == null || renderer.renderMode == ParticleSystemRenderMode.Mesh && renderer.mesh == null)
                    throw new InvalidOperationException("Particle material/mesh missing: " + particle.name);
                if (renderer.renderMode == ParticleSystemRenderMode.Mesh)
                    throw new InvalidOperationException("Body mesh must not be repeatedly emitted as particles.");
            }
            foreach (var mesh in VerifyStableBodies(stage, p.director).Where(m => m.name.Contains("ghost") || m.name.Contains("pumpkin")))
            {
                var vertices = mesh.vertices; var uv = mesh.uv; var triangles = mesh.triangles;
                int faceCount = 0;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    bool ghost = mesh.name.Contains("ghost");
                    bool face = ghost ? uv[a].x > .25f && uv[a].x < .5f && uv[b].x > .25f && uv[b].x < .5f && uv[c].x > .25f && uv[c].x < .5f
                        : uv[a].x < .25f && uv[b].x < .25f && uv[c].x < .25f;
                    if (!face) continue;
                    faceCount++;
                    if (Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).y >= 0)
                        throw new InvalidOperationException("Back-facing palette face in " + mesh.name);
                }
                if (faceCount != (mesh.name.Contains("ghost") ? 192 : 104)) throw new InvalidOperationException("Face palette count differs.");
            }
            var authoredEffects = JObject.Parse(File.ReadAllText(ShowPlanPath))["sections"].SelectMany(s => s["effects"]).ToArray();
            var presets = JObject.Parse(File.ReadAllText(EffectsPath));
            foreach (var effect in authoredEffects.Where(e => presets[(string)e["type"]][(string)e["preset"]]["layers"].Any(l => (string)l["name"] == "ArchColour")))
            {
                string actor = (string)effect["type"] + "_" + (string)effect["preset"];
                var track = timeline.GetOutputTracks().OfType<ActivationTrack>().Single(t => (p.director.GetGenericBinding(t) as GameObject)?.name == actor);
                var clip = track.GetClips().Single(c => Math.Abs(c.start - (double)effect["startTime"]) < .001);
                double tail = presets[(string)effect["type"]][(string)effect["preset"]]["layers"].Max(l => (double)l["startLifetime"]);
                if (Math.Abs(clip.end - Math.Min(120, (double)effect["endTime"] + tail)) > .001)
                    throw new InvalidOperationException("Authored arch tail differs: " + actor);
            }
            VerifyPropMotion(stage, p.director);
            if (p.costumeGlowRenderers != null && p.costumeGlowRenderers.Length > 0) VerifyCostumes();
            File.WriteAllText(Evidence + "/ground-survey.txt", KD_CandyDogSetupEditor.SurveyChoreographyGround(p));
            File.WriteAllText(Evidence + "/construction.txt", report);
            if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new IOException("Preview save failed.");
            VerifySource();
            var meshes = stage.GetComponentsInChildren<MeshFilter>(true)
                .Select(r => r.sharedMesh).Where(m => m != null).Distinct().Select(m => new { name = m.name, vertices = m.vertexCount, triangles = m.triangles.Length / 3,
                    size = new[] { m.bounds.size.x, m.bounds.size.y, m.bounds.size.z } }).ToArray();
            File.WriteAllText(Evidence + "/construction.json", JsonConvert.SerializeObject(new
            { complete = true, scope = "construction only; controlled rehearsal still required", checkedAt = DateTime.UtcNow.ToString("o"),
                scene = ScenePath, dirty = SceneManager.GetActiveScene().isDirty, audioSha256 = expected,
                sourceSha256 = Hash(SourcePath), previewSha256 = Hash(ScenePath), particles = ps.Length, meshes }, Formatting.Indented));
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Test Harness/Pumpkin Parade - Rehearse and Capture")]
        public static void Rehearse() { GuardStage(); KD_CandyRehearsalEditor.StartFromPlan(ShowVersion + "/review-plan.json", true); }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Test Harness/Pumpkin Parade - Play With Sound")]
        public static void PlayWithSound() { GuardStage(); KD_CandyRehearsalEditor.StartFromPlan(ShowVersion + "/review-plan.json", false); }
    }
}
