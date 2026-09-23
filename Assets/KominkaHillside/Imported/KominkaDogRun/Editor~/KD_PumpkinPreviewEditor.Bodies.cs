using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PumpkinPreviewEditor
    {
        private static readonly string[] StableBodyNames = { "GhostBody", "PumpkinBody", "HatBody", "BatBody", "BatWingLeft", "BatWingRight" };
        // Actor motion belongs to the imported Timeline. Only the visible body layer is replaced.
        private static void WireStableBodies(GameObject stage, PlayableDirector director, PropSource sourceSpec = null)
        {
            var timeline = director.playableAsset as TimelineAsset;
            var effects = JObject.Parse(File.ReadAllText(sourceSpec?.Plan ?? ShowPlanPath))
                ["sections"].SelectMany(s => s["effects"]).ToArray();
            var presets = JObject.Parse(File.ReadAllText(sourceSpec?.Effects ?? EffectsPath));
            foreach (var actor in stage.GetComponentsInChildren<Transform>(true).Where(t => t.parent == stage.transform))
            {
                foreach (string bodyName in StableBodyNames)
                {
                    var body = actor.Find(bodyName);
                    if (body == null) continue;
                    var intervals = effects.Where(e => (string)e["type"] + "_" + (string)e["preset"] == actor.name).ToArray();
                    if (intervals.Length == 0) throw new InvalidOperationException("Body lacks authored visibility: " + actor.name);
                    var spec = presets[(string)intervals[0]["type"]][(string)intervals[0]["preset"]]["layers"]
                        .Single(l => (string)l["name"] == bodyName);
                    var angles = spec["startRotation3D"].ToObject<float[]>();
                    var rotation = Quaternion.Euler(angles[0] * Mathf.Rad2Deg, angles[1] * Mathf.Rad2Deg, angles[2] * Mathf.Rad2Deg);
                    float size = (float)spec["startSize"];
                    CheckBodyCurves(director, body, true);
                    var particle = body.GetComponent<ParticleSystem>();
                    if (particle != null)
                    {
                        var renderer = body.GetComponent<ParticleSystemRenderer>();
                        if (renderer == null || renderer.renderMode != ParticleSystemRenderMode.Mesh || renderer.mesh == null
                            || renderer.sharedMaterial == null || !particle.main.startRotation3D)
                            throw new InvalidOperationException("Expected a textured mesh body: " + body.name);
                        var mesh = renderer.mesh;
                        var material = renderer.sharedMaterial;
                        UnityEngine.Object.DestroyImmediate(renderer);
                        UnityEngine.Object.DestroyImmediate(particle);
                        var filter = body.GetComponent<MeshFilter>();
                        if (filter == null) filter = body.gameObject.AddComponent<MeshFilter>();
                        filter.sharedMesh = mesh;
                        var stable = body.GetComponent<MeshRenderer>();
                        if (stable == null) stable = body.gameObject.AddComponent<MeshRenderer>();
                        stable.sharedMaterial = material;
                        stable.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        stable.receiveShadows = false;
                    }
                    body.localRotation = rotation;
                    body.localScale = Vector3.one * size;
                    if (PrefabUtility.IsPartOfPrefabInstance(body)) PrefabUtility.RecordPrefabInstancePropertyModifications(body);
                    var existing = timeline.GetOutputTracks().OfType<ActivationTrack>()
                        .Where(t => director.GetGenericBinding(t) == body.gameObject).ToArray();
                    if (existing.Length > 1) throw new InvalidOperationException("Duplicate body activation: " + actor.name);
                    var visibility = existing.SingleOrDefault() ?? timeline.CreateTrack<ActivationTrack>(null, "Body - " + actor.name);
                    foreach (var clip in visibility.GetClips().ToArray()) timeline.DeleteClip(clip);
                    foreach (var interval in intervals)
                    {
                        var clip = visibility.CreateDefaultClip();
                        clip.start = (double)interval["startTime"];
                        clip.duration = (double)interval["endTime"] - clip.start;
                    }
                    visibility.postPlaybackState = ActivationTrack.PostPlaybackState.Inactive;
                    director.SetGenericBinding(visibility, body.gameObject);
                    body.gameObject.SetActive(false);
                    if (PrefabUtility.IsPartOfPrefabInstance(body.gameObject)) PrefabUtility.RecordPrefabInstancePropertyModifications(body.gameObject);
                    EditorUtility.SetDirty(visibility);
                }
            }
            EditorUtility.SetDirty(timeline);
        }

        private static Mesh[] VerifyStableBodies(GameObject stage, PlayableDirector director, PropSource sourceSpec = null)
        {
            var timeline = (TimelineAsset)director.playableAsset;
            var effects = JObject.Parse(File.ReadAllText(sourceSpec?.Plan ?? ShowPlanPath))
                ["sections"].SelectMany(s => s["effects"]).ToArray();
            var presets = JObject.Parse(File.ReadAllText(sourceSpec?.Effects ?? EffectsPath));
            var bodies = stage.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => StableBodyNames.Contains(f.name)).ToArray();
            var expectedBodies = effects.SelectMany(e => presets[(string)e["type"]][(string)e["preset"]]["layers"]
                .Where(l => (string)l["rendererMode"] == "Mesh")
                .Select(l => (string)e["type"] + "_" + (string)e["preset"] + "/" + (string)l["name"]))
                .Distinct().OrderBy(s => s).ToArray();
            if (!expectedBodies.SequenceEqual(bodies.Select(b => b.transform.parent.name + "/" + b.name).OrderBy(s => s)))
                throw new InvalidOperationException("Stable mesh body coverage differs from the authored effects.");
            if (bodies.Count(f => f.name == "GhostBody") != 2 || bodies.Count(f => f.name == "PumpkinBody") != 18)
                throw new InvalidOperationException("Expected two stable ghosts and eighteen stable lanterns.");
            foreach (var body in bodies)
            {
                var renderer = body.GetComponent<MeshRenderer>();
                if (body.sharedMesh == null || body.GetComponent<ParticleSystem>() != null || body.GetComponent<ParticleSystemRenderer>() != null
                    || renderer == null || !renderer.enabled || renderer.sharedMaterial == null || renderer.sharedMaterial.mainTexture == null
                    || renderer.sharedMaterial.shader.name != "KominkaDogRun/Pumpkin Palette"
                    || renderer.sharedMaterial.renderQueue != KD_LiveSetupEditor.LiveParticleQueue)
                    throw new InvalidOperationException("Stable body renderer differs: " + body.name);
                var tracks = timeline.GetOutputTracks().OfType<ActivationTrack>()
                    .Where(t => director.GetGenericBinding(t) == body.gameObject).ToArray();
                if (tracks.Length != 1 || tracks[0].postPlaybackState != ActivationTrack.PostPlaybackState.Inactive)
                    throw new InvalidOperationException("Body visibility binding or end state differs.");
                var clips = tracks[0].GetClips().OrderBy(c => c.start).ToArray();
                var intervals = effects.Where(e => (string)e["type"] + "_" + (string)e["preset"] == body.transform.parent.name)
                    .OrderBy(e => (double)e["startTime"]).ToArray();
                if (intervals.Length == 0) throw new InvalidOperationException("Body has no authored intervals.");
                var spec = presets[(string)intervals[0]["type"]][(string)intervals[0]["preset"]]["layers"]
                    .Single(l => (string)l["name"] == body.name);
                var angles = spec["startRotation3D"].ToObject<float[]>();
                var rotation = Quaternion.Euler(angles[0] * Mathf.Rad2Deg, angles[1] * Mathf.Rad2Deg, angles[2] * Mathf.Rad2Deg);
                if (Quaternion.Angle(body.transform.localRotation, ExpectedBodyRotation(body.transform, director, rotation, sourceSpec)) > .1f
                    || Vector3.Distance(body.transform.localScale, Vector3.one * (float)spec["startSize"]) > .00001f)
                    throw new InvalidOperationException("Body mesh orientation or authored size differs.");
                CheckBodyCurves(director, body.transform, false);
                if (clips.Length != intervals.Length) throw new InvalidOperationException("Body visibility interval count differs.");
                for (int i = 0; i < clips.Length; i++)
                    if (Math.Abs(clips[i].start - (double)intervals[i]["startTime"]) > .00001
                        || Math.Abs(clips[i].end - (double)intervals[i]["endTime"]) > .00001)
                        throw new InvalidOperationException("Body inherited a particle tail instead of its authored lifetime.");
            }
            File.WriteAllText((sourceSpec?.Evidence ?? Evidence) + "/stable-bodies.json", JsonConvert.SerializeObject(new
            {
                checkedAt = DateTime.UtcNow.ToString("o"), bodies = bodies.Length,
                bodyParticleSystems = bodies.Count(f => f.GetComponent<ParticleSystem>() != null),
                scope = "Static components and Timeline visibility intervals; continuous visual review pending",
                actors = bodies.Select(f => new { actor = f.transform.parent.name, layer = f.name, mesh = f.sharedMesh.name,
                    rotation = new[] { f.transform.localEulerAngles.x, f.transform.localEulerAngles.y, f.transform.localEulerAngles.z },
                    scale = new[] { f.transform.localScale.x, f.transform.localScale.y, f.transform.localScale.z } })
            }, Formatting.Indented));
            return bodies.Select(f => f.sharedMesh).Distinct().ToArray();
        }

        private static void CheckBodyCurves(PlayableDirector director, Transform body, bool remove)
        {
            // Run after reimport even when the scene already retains removed-component overrides.
            foreach (var track in ((TimelineAsset)director.playableAsset).GetOutputTracks().OfType<AnimationTrack>())
            {
                UnityEngine.Object bound = director.GetGenericBinding(track);
                for (var parent = track.parent as TrackAsset; bound == null && parent != null; parent = parent.parent as TrackAsset)
                    bound = director.GetGenericBinding(parent);
                Transform root = bound is GameObject go ? go.transform : (bound as Component)?.transform;
                if (root == null) continue;
                var clips = track.GetClips().Select(c => (c.asset as AnimationPlayableAsset)?.clip).Where(c => c != null).ToList();
                if (track.infiniteClip != null) clips.Add(track.infiniteClip);
                foreach (var clip in clips.Distinct())
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        var target = string.IsNullOrEmpty(binding.path) ? root : root.Find(binding.path);
                        if (target != body || binding.type != typeof(ParticleSystem) && binding.type != typeof(ParticleSystemRenderer)) continue;
                        if (!remove) throw new InvalidOperationException("Obsolete body particle curve: " + binding.path + " " + binding.propertyName);
                        AnimationUtility.SetEditorCurve(clip, binding, null);
                        EditorUtility.SetDirty(clip);
                    }
            }
        }
    }
}
