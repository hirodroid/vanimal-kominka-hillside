using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using VirtualCampers.LiveGenerator;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PumpkinProductionEditor
    {
        const string RevisionSlug = "kd-pumpkinparade-003";
        const string RevisionSource = "C:/work/unity/vrc-live-generator/shows/kd-pumpkinparade/versions/003";
        const string RevisionEvidence = "Logs/pumpkin003-update";
        static KD_CandyDogPerformance RevisionPerformance() => KD_SceneObjects.Find("KD_PumpkinDogPerformance").GetComponent<KD_CandyDogPerformance>();
        static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
        static float[] Q(Quaternion q) => new[] { q.x, q.y, q.z, q.w };
        static string RevisionHash(string file)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(file))).Replace("-", "").ToLowerInvariant();
        }
        static JObject PreservedState()
        {
            var p = RevisionPerformance(); var live = p.liveSystem;
            // Normalize float tokens through the same JSON representation used by the on-disk baseline.
            return JObject.Parse(JsonConvert.SerializeObject(new {
                stage = p.director.gameObject.GetInstanceID(),
                stagePosition = V(p.director.transform.position), stageRotation = Q(p.director.transform.rotation),
                stageScale = V(p.director.transform.localScale),
                dogs = p.dogs.Select(d => d.GetInstanceID()).ToArray(),
                slots = p.slots.Select(t => new { id = t.GetInstanceID(), position = V(t.localPosition), rotation = Q(t.localRotation) }).ToArray(),
                costumes = p.costumeNodes.Select(n => new { id = n.GetInstanceID(), parent = n.transform.parent.GetInstanceID(),
                    position = V(n.transform.localPosition), rotation = Q(n.transform.localRotation), scale = V(n.transform.localScale) }).ToArray(),
                otherStages = live.stageRoots.Take(8).Select(g => new { id = g.GetInstanceID(), active = g.activeSelf,
                    timeline = AssetDatabase.GetAssetPath(g.GetComponent<PlayableDirector>().playableAsset) }).ToArray()
            }));
        }
        public static void CaptureRevisionBaseline()
        {
            Guard(); Directory.CreateDirectory(RevisionEvidence);
            if (File.Exists(RevisionEvidence + "/preserved.json")) throw new InvalidOperationException("Baseline already exists.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveOpenScenes();
            File.WriteAllText(RevisionEvidence + "/preserved.json", PreservedState().ToString());
        }
        public static void AdoptRevision003()
        {
            Guard();
            var before = JObject.Parse(File.ReadAllText(RevisionEvidence + "/preserved.json"));
            File.WriteAllText(RevisionEvidence + "/current.json", PreservedState().ToString());
            if (!JToken.DeepEquals(before, PreservedState())) throw new InvalidOperationException("Preserved scene values changed before adoption.");
            var p = RevisionPerformance(); var stage = p.director.gameObject;
            var imported = KD_SceneObjects.Find("LiveStage_" + RevisionSlug, out int count);
            if (imported == null || count != 1) throw new InvalidOperationException("Exactly one imported 003 required.");
            var from = imported.GetComponent<PlayableDirector>();
            var timeline = from.playableAsset as TimelineAsset;
            if (timeline == null || !AssetDatabase.GetAssetPath(timeline).StartsWith(KD_LiveSetupEditor.ShowFolderOf(RevisionSlug) + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("Wrong revision timeline.");
            var incoming = imported.GetComponentsInChildren<Transform>(true).Where(t => t.parent == imported.transform).ToArray();
            var oldActors = stage.GetComponentsInChildren<Transform>(true).Where(t => t.parent == stage.transform && t.name.StartsWith("particles_", StringComparison.Ordinal)).ToArray();
            if (incoming.Length != 95 || !incoming.Select(t => t.name).OrderBy(n => n).SequenceEqual(oldActors.Select(t => t.name).OrderBy(n => n)))
                throw new InvalidOperationException("Actor identity set differs; scoped replacement refused.");
            // Resolve every existing dog-light reference before removing any old actor.
            var lightPaths = p.dogLightLayers.Select(t => AnimationUtility.CalculateTransformPath(t, stage.transform)).ToArray();
            var lights = lightPaths.Select(s => imported.transform.Find(s)).ToArray();
            if (lights.Any(t => t == null)) throw new InvalidOperationException("Dog light reference missing in revision.");
            var tracks = timeline.GetOutputTracks().ToArray();
            var bindings = tracks.Select(t => from.GetGenericBinding(t)).ToArray();
            if (tracks.Where((t, i) => !(t is MarkerTrack) && bindings[i] == null).Any()) throw new InvalidOperationException("Imported track unbound.");
            foreach (var track in ((TimelineAsset)p.director.playableAsset).GetOutputTracks()) p.director.ClearGenericBinding(track);
            foreach (var actor in oldActors) Object.DestroyImmediate(actor.gameObject);
            foreach (var actor in incoming) actor.SetParent(stage.transform, false);
            p.director.playableAsset = timeline;
            for (int i = 0; i < tracks.Length; i++)
            {
                Object target = bindings[i];
                if (target == imported.GetComponent<Animator>()) target = stage.GetComponent<Animator>();
                else if (target == imported.GetComponent<AudioSource>()) target = stage.GetComponent<AudioSource>();
                p.director.SetGenericBinding(tracks[i], target);
            }
            var binding = stage.GetComponent<EffectBinding>();
            binding.slots = imported.GetComponent<EffectBinding>().slots;
            EditorUtility.SetDirty(binding);
            p.dogLightLayers = lights;
            Object.DestroyImmediate(imported);
            KD_PumpkinPreviewEditor.ApplyRevisionVisuals(stage, p.director, RevisionSource, RevisionSlug);
            p.director.playOnAwake = false; p.director.extrapolationMode = DirectorWrapMode.Hold;
            stage.SetActive(false);
            KD_ProductsEditor.Copy(p);
            EditorUtility.SetDirty(p.director);
            var importer = (AudioImporter)AssetImporter.GetAtPath(KD_LiveSetupEditor.ShowFolderOf(RevisionSlug) + "/audio.wav");
            var settings = importer.defaultSampleSettings; settings.loadType = AudioClipLoadType.Streaming;
            importer.defaultSampleSettings = settings;
            foreach (var platform in KD_LiveSetupEditor.AudioOverridePlatforms) importer.SetOverrideSampleSettings(platform, settings);
            importer.SaveAndReimport();
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(stage.scene);
            if (!JToken.DeepEquals(before, PreservedState())) throw new InvalidOperationException("Preservation comparison failed after adoption.");
            File.WriteAllText(RevisionEvidence + "/applied.json", JsonConvert.SerializeObject(new { revision = RevisionSlug,
                source = RevisionSource, actors = incoming.Length, preservedCostumes = p.costumeNodes.Length, dogLights = lights.Length,
                timeline = AssetDatabase.GetAssetPath(timeline), saved = false }, Formatting.Indented));
        }
        public static void VerifyRevision003()
        {
            Guard(); var p = RevisionPerformance(); var stage = p.director.gameObject;
            KD_PumpkinPreviewEditor.VerifyRevisionVisuals(stage, p.director, RevisionSource, RevisionSlug);
            var timeline = p.director.playableAsset as TimelineAsset;
            if (AssetDatabase.GetAssetPath(timeline) != KD_LiveSetupEditor.ShowFolderOf(RevisionSlug) + "/" + RevisionSlug + ".playable"
                || p.liveSystem.stageRoots[8] != stage || p.liveSystem.stageDirectors[8] != p.director
                || !p.LiveConfigurationValid() || p.duration != 120 || p.liveSystem.stageDurations[8] != 120
                || stage.activeSelf || p.director.playOnAwake || stage.GetComponentsInChildren<Light>(true).Length != 0)
                throw new InvalidOperationException("Revision registration/runtime state differs.");
            foreach (var track in timeline.GetOutputTracks())
                if (!(track is MarkerTrack) && p.director.GetGenericBinding(track) == null) throw new InvalidOperationException("Unbound track: " + track.name);
            foreach (var track in timeline.GetOutputTracks().OfType<AnimationTrack>())
            {
                var animator = p.director.GetGenericBinding(track) as Animator;
                if (animator == null) throw new InvalidOperationException("Animation target is not an Animator.");
                foreach (var clip in track.GetClips().Select(c => ((AnimationPlayableAsset)c.asset).clip))
                    foreach (var curve in AnimationUtility.GetCurveBindings(clip))
                        if (curve.path.Length > 0 && animator.transform.Find(curve.path) == null)
                            throw new InvalidOperationException("Animation path cannot resolve: " + track.name + "/" + curve.path);
            }
            var audio = timeline.GetOutputTracks().OfType<AudioTrack>().SelectMany(t => t.GetClips()).Select(c => ((AudioPlayableAsset)c.asset).clip).Single();
            if (RevisionHash(AssetDatabase.GetAssetPath(audio)) != (string)JObject.Parse(File.ReadAllText(RevisionSource + "/music-handoff.json"))["audio"]["sha256"])
                throw new InvalidOperationException("Audio hash differs.");
            if (!JToken.DeepEquals(JObject.Parse(File.ReadAllText(RevisionEvidence + "/preserved.json")), PreservedState()))
                throw new InvalidOperationException("Stage, outfits, roster or other works changed.");
            var hat = stage.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == "HatBody");
            var plan = JObject.Parse(File.ReadAllText(RevisionSource + "/" + RevisionSlug + ".showplan.json"));
            var hatEffect = plan["sections"].SelectMany(s => s["effects"]).Single(e => ((string)e["preset"]).Contains("_hat_"));
            var actor = (string)hatEffect["type"] + "_" + (string)hatEffect["preset"];
            var hatTrack = timeline.GetOutputTracks().OfType<AnimationTrack>().Single(t => t.name == actor + "_move");
            var hatEntry = hatTrack.GetClips().Single(); var hatClip = ((AnimationPlayableAsset)hatEntry.asset).clip;
            var points = hatEffect["path"].ToArray();
            for (int axis = 0; axis < 3; axis++)
            {
                var curve = AnimationUtility.GetEditorCurve(hatClip, EditorCurveBinding.FloatCurve(actor, typeof(Transform), "m_LocalPosition." + new[] { "x", "y", "z" }[axis]));
                if (curve == null || curve.length != points.Length) throw new InvalidOperationException("Hat path key count differs.");
                var keys = curve.keys;
                for (int i = 0; i < keys.Length; i++)
                    if (Math.Abs(keys[i].value - (float)points[i][axis]) > .00001f
                        || Math.Abs(keys[i].time - i * hatEntry.duration / (points.Length - 1)) > .00002)
                        throw new InvalidOperationException("Hat path differs from revision source.");
            }
            var last = points.Last(); var local = new Vector3((float)last[0], (float)last[1], (float)last[2]);
            var point = stage.transform.TransformPoint(local);
            var hits = Physics.RaycastAll(point + Vector3.up * 5, Vector3.down, 10, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => !h.transform.IsChildOf(stage.transform)).OrderBy(h => Mathf.Abs(h.point.y - point.y)).ToArray();
            var bound = hat.sharedMesh.bounds;
            File.WriteAllText(RevisionEvidence + "/verified.json", JsonConvert.SerializeObject(new {
                result = "PASS", revision = RevisionSlug, tracks = timeline.GetOutputTracks().Count(),
                stableBodies = stage.GetComponentsInChildren<MeshFilter>(true).Length, outfitsPreserved = 18,
                audioSha256 = RevisionHash(AssetDatabase.GetAssetPath(audio)),
                landingWorld = V(point), ground = hits.Take(3).Select(h => new { name = h.collider.name, point = V(h.point) }).ToArray(),
                hatMeshBounds = new { min = V(bound.min), max = V(bound.max) }, runtimePlaybackVerified = false
            }, Formatting.Indented));
        }
        public static void RebakeRevisionHatPath()
        {
            Guard(); var p = RevisionPerformance(); var timeline = p.director.playableAsset as TimelineAsset;
            if (AssetDatabase.GetAssetPath(timeline) != KD_LiveSetupEditor.ShowFolderOf(RevisionSlug) + "/" + RevisionSlug + ".playable")
                throw new InvalidOperationException("Adopt revision before rebaking its hat path.");
            string source = RevisionSource + "/" + RevisionSlug + ".showplan.json";
            var effect = JObject.Parse(File.ReadAllText(source))["sections"].SelectMany(s => s["effects"]).Single(e => ((string)e["preset"]).Contains("_hat_"));
            string actor = (string)effect["type"] + "_" + (string)effect["preset"];
            var track = timeline.GetOutputTracks().OfType<AnimationTrack>().Single(t => t.name == actor + "_move");
            var entry = track.GetClips().Single(); var clip = ((AnimationPlayableAsset)entry.asset).clip;
            if (Math.Abs(entry.start - (double)effect["startTime"]) > .00001 || Math.Abs(entry.end - (double)effect["endTime"]) > .00001)
                throw new InvalidOperationException("Path-only rebake cannot change timing.");
            var points = effect["path"].ToArray();
            for (int axis = 0; axis < 3; axis++)
            {
                var keys = points.Select((v, i) => new Keyframe((float)(entry.duration * i / (points.Length - 1)), (float)v[axis])).ToArray();
                var curve = new AnimationCurve(keys);
                for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(actor, typeof(Transform), "m_LocalPosition." + new[] { "x", "y", "z" }[axis]), curve);
            }
            EditorUtility.SetDirty(clip); AssetDatabase.SaveAssets();
            string copiedPlan = KD_LiveSetupEditor.ShowFolderOf(RevisionSlug) + "/" + RevisionSlug + ".showplan.json";
            File.Copy(source, copiedPlan, true); AssetDatabase.ImportAsset(copiedPlan);
        }
    }
}
