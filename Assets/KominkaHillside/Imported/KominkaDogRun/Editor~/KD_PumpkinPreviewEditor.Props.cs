using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PumpkinPreviewEditor
    {
        private const string PropTrackName = "Pumpkin props - authored motion";

        private static JObject ReadPropMotion(PropSource sourceSpec = null)
        {
            string source = (sourceSpec?.Version ?? ShowVersion) + "/prop-motion.json";
            if (!File.Exists(source))
            {
                if (StageName == "LiveStage_kd-pumpkinparade-001") return null;
                throw new FileNotFoundException("This show requires authored prop motion.", source);
            }
            return JObject.Parse(File.ReadAllText(source));
        }

        private static void WirePropMotion(GameObject stage, PlayableDirector director, PropSource sourceSpec = null)
        {
            string source = (sourceSpec?.Version ?? ShowVersion) + "/prop-motion.json";
            var data = ReadPropMotion(sourceSpec);
            if (data == null) return;
            if ((string)data["audioSha256"] != (string)JObject.Parse(File.ReadAllText((sourceSpec?.Version ?? ShowVersion) + "/music-handoff.json"))["audio"]["sha256"]
                || Math.Abs((double)data["duration"] - 120) > .001)
                throw new InvalidOperationException("Prop motion music identity differs.");
            var timeline = (TimelineAsset)director.playableAsset;
            var animator = stage.GetComponent<Animator>();
            if (animator == null) throw new InvalidOperationException("Imported stage motion Animator is missing.");
            string path = (sourceSpec?.Generated ?? Generated) + "PumpkinPropMotion.anim";
            // Build in memory first so invalid source data cannot erase a saved animation.
            var clip = new AnimationClip { name = "PumpkinPropMotion", frameRate = 30 };
            var authored = JObject.Parse(File.ReadAllText(sourceSpec?.Plan ?? ShowPlanPath))["sections"].SelectMany(s => s["effects"]).ToArray();
            foreach (var prop in data["props"])
            {
                string name = (string)prop["actor"];
                var actor = stage.transform.Find(name);
                if (actor == null || actor.parent != stage.transform) throw new InvalidOperationException("Prop actor missing: " + name);
                float basis = (float)authored.First(e => (string)e["type"] + "_" + (string)e["preset"] == name)["scale"];
                var scale = prop["scaleEnvelope"].Select(k => new Keyframe((float)k[0], basis * (float)k[1])).ToArray();
                foreach (string axis in new[] { "x", "y", "z" }) SetPropCurve(clip, name, "m_LocalScale." + axis, scale);
                string kind = (string)prop["kind"];
                float start = (float)prop["start"], end = (float)prop["end"];
                if (kind == "hat")
                    SetPropRotation(clip, name, start, end, t => Quaternion.Euler(0, (float)prop["spinDegreesPerSecond"] * (t - start), 0));
                else if (kind == "bat")
                {
                    foreach (var part in prop["parts"])
                    {
                        string partName = (string)part["name"]; var child = actor.Find(partName);
                        if (child == null || child.GetComponent<MeshRenderer>() == null) throw new InvalidOperationException("Stable bat part missing.");
                        var pos = part["localPosition"].ToObject<float[]>(); child.localPosition = new Vector3(pos[0], pos[1], pos[2]);
                        if (PrefabUtility.IsPartOfPrefabInstance(child)) PrefabUtility.RecordPrefabInstancePropertyModifications(child);
                        if (!partName.StartsWith("BatWing", StringComparison.Ordinal)) continue;
                        // Native mesh X/Y maps to world -X/+Z. The wing hinge is world Z.
                        SetPropRotation(clip, name + "/" + partName, start, end, t => BatWingRotation(prop, partName, t));
                    }
                }
            }
            clip.EnsureQuaternionContinuity();
            var saved = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (saved == null) AssetDatabase.CreateAsset(clip, path);
            else { EditorUtility.CopySerialized(clip, saved); UnityEngine.Object.DestroyImmediate(clip); clip = saved; }
            EditorUtility.SetDirty(clip);
            // Unity names a standalone .anim after its filename on import. Keep
            // the input identity in importer metadata so save/reload preserves it.
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Prop animation importer missing.");
            importer.userData = "pumpkin-prop-source:" + Hash(source);
            AssetDatabase.WriteImportSettingsIfDirty(path);
            foreach (var prop in data["props"].Where(p => (string)p["kind"] == "bat"))
                foreach (var part in prop["parts"].Where(p => ((string)p["name"]).StartsWith("BatWing", StringComparison.Ordinal)))
                {
                    string childPath = (string)prop["actor"] + "/" + (string)part["name"];
                    var child = stage.transform.Find(childPath);
                    child.localRotation = EvaluatePropRotation(clip, childPath, (float)director.time);
                    if (PrefabUtility.IsPartOfPrefabInstance(child)) PrefabUtility.RecordPrefabInstancePropertyModifications(child);
                }
            var tracks = timeline.GetOutputTracks().OfType<AnimationTrack>().Where(t => t.name == PropTrackName).ToArray();
            if (tracks.Length > 1) throw new InvalidOperationException("Duplicate prop animation tracks.");
            var track = tracks.SingleOrDefault() ?? timeline.CreateTrack<AnimationTrack>(null, PropTrackName);
            foreach (var old in track.GetClips().ToArray()) timeline.DeleteClip(old);
            var entry = track.CreateClip(clip); entry.start = 0; entry.duration = 120;
            ((AnimationPlayableAsset)entry.asset).removeStartOffset = false;
            director.SetGenericBinding(track, animator);
            EditorUtility.SetDirty(track); EditorUtility.SetDirty(timeline);
        }

        private static void SetPropCurve(AnimationClip clip, string path, string property, Keyframe[] keys)
        {
            var curve = new AnimationCurve(keys);
            for (int k = 0; k < curve.length; k++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
        }

        private static Quaternion BatWingRotation(JToken prop, string partName, float time)
        {
            float t = Mathf.Clamp(time, (float)prop["start"], (float)prop["end"]);
            float sign = partName == "BatWingLeft" ? -1 : 1;
            return Quaternion.AngleAxis(sign * (float)prop["flapDegrees"] * Mathf.Sin((t - (float)prop["start"]) * 2 * Mathf.PI
                * (float)prop["flapCyclesPerSecond"] + (float)prop["phase"]), Vector3.forward) * Quaternion.Euler(-90, 180, 0);
        }

        private static Quaternion ExpectedBodyRotation(Transform body, PlayableDirector director, Quaternion basis, PropSource sourceSpec = null)
        {
            if (!body.name.StartsWith("BatWing", StringComparison.Ordinal)) return basis;
            if (ReadPropMotion(sourceSpec) == null) return basis;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>((sourceSpec?.Generated ?? Generated) + "PumpkinPropMotion.anim");
            if (clip == null) throw new InvalidOperationException("Baked prop animation missing.");
            return EvaluatePropRotation(clip, body.parent.name + "/" + body.name, (float)director.time);
        }

        private static Quaternion EvaluatePropRotation(AnimationClip clip, string path, float time)
        {
            var values = new float[4]; var axes = new[] { "x", "y", "z", "w" };
            for (int i = 0; i < 4; i++)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + axes[i]));
                if (curve == null) throw new InvalidOperationException("Missing quaternion component: " + path);
                values[i] = curve.Evaluate(time);
            }
            return new Quaternion(values[0], values[1], values[2], values[3]).normalized;
        }

        private static void SetPropRotation(AnimationClip clip, string path, float start, float end, Func<float, Quaternion> rotation)
        {
            var curves = new[] { new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>() };
            int count = Mathf.CeilToInt((end - start) * 30);
            for (int k = 0; k <= count; k++)
            {
                float t = Mathf.Lerp(start, end, (float)k / count); var q = rotation(t);
                curves[0].Add(new Keyframe(t, q.x)); curves[1].Add(new Keyframe(t, q.y));
                curves[2].Add(new Keyframe(t, q.z)); curves[3].Add(new Keyframe(t, q.w));
            }
            var axes = new[] { "x", "y", "z", "w" };
            for (int i = 0; i < 4; i++) SetPropCurve(clip, path, "m_LocalRotation." + axes[i], curves[i].ToArray());
        }

        private static void VerifyPropMotion(GameObject stage, PlayableDirector director, PropSource sourceSpec = null)
        {
            string source = (sourceSpec?.Version ?? ShowVersion) + "/prop-motion.json";
            var data = ReadPropMotion(sourceSpec);
            if (data == null) return;
            var timeline = (TimelineAsset)director.playableAsset;
            var track = timeline.GetOutputTracks().OfType<AnimationTrack>().Single(t => t.name == PropTrackName);
            var entry = track.GetClips().Single(); var clip = ((AnimationPlayableAsset)entry.asset).clip;
            if (director.GetGenericBinding(track) != stage.GetComponent<Animator>() || clip == null
                || AssetImporter.GetAtPath((sourceSpec?.Generated ?? Generated) + "PumpkinPropMotion.anim").userData != "pumpkin-prop-source:" + Hash(source) || clip.hasRootCurves
                || Math.Abs(entry.start) > .001 || Math.Abs(entry.duration - 120) > .001)
                throw new InvalidOperationException("Prop animation binding, clock or source differs: binding=" + (director.GetGenericBinding(track) == stage.GetComponent<Animator>())
                    + " name=" + (clip == null ? "NULL" : clip.name) + " expectedSource=" + Hash(source)
                    + " hasRootCurves=" + (clip != null && clip.hasRootCurves) + " start=" + entry.start + " duration=" + entry.duration);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var authored = JObject.Parse(File.ReadAllText(sourceSpec?.Plan ?? ShowPlanPath))["sections"].SelectMany(s => s["effects"]).ToArray();
            foreach (var prop in data["props"])
            {
                string actor = (string)prop["actor"];
                float basis = (float)authored.First(e => (string)e["type"] + "_" + (string)e["preset"] == actor)["scale"];
                foreach (string axis in new[] { "x", "y", "z" })
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(actor, typeof(Transform), "m_LocalScale." + axis));
                    var expected = prop["scaleEnvelope"].ToArray();
                    if (curve == null || curve.length != expected.Length) throw new InvalidOperationException("Prop scale curve missing: " + actor);
                    var knots = curve.keys;
                    for (int k = 0; k < knots.Length; k++)
                        if (Math.Abs(knots[k].time - (float)expected[k][0]) > .0001f || Math.Abs(knots[k].value - basis * (float)expected[k][1]) > .00001f)
                            throw new InvalidOperationException("Prop scale curve differs from source: " + actor);
                }
                if ((string)prop["kind"] == "hat" && bindings.Count(b => b.path == actor && b.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal)) != 4)
                    throw new InvalidOperationException("Hat turning curves missing.");
                if ((string)prop["kind"] == "hat") VerifyPropRotationKnots(clip, actor, (float)prop["start"], (float)prop["end"],
                    t => Quaternion.Euler(0, (float)prop["spinDegreesPerSecond"] * (t - (float)prop["start"]), 0));
                if ((string)prop["kind"] != "bat") continue;
                foreach (var part in prop["parts"])
                {
                    var child = stage.transform.Find(actor + "/" + (string)part["name"]);
                    var v = part["localPosition"].ToObject<float[]>();
                    if (child == null || Vector3.Distance(child.localPosition, new Vector3(v[0], v[1], v[2])) > .0001f)
                        throw new InvalidOperationException("Bat hinge placement differs.");
                    if (((string)part["name"]).StartsWith("BatWing", StringComparison.Ordinal)
                        && bindings.Count(b => b.path == actor + "/" + (string)part["name"] && b.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal)) != 4)
                        throw new InvalidOperationException("Bat flap rotation curves missing.");
                    if (((string)part["name"]).StartsWith("BatWing", StringComparison.Ordinal))
                        VerifyPropRotationKnots(clip, actor + "/" + (string)part["name"], (float)prop["start"], (float)prop["end"],
                            t => BatWingRotation(prop, (string)part["name"], t));
                }
            }
        }

        private static void VerifyPropRotationKnots(AnimationClip clip, string path, float start, float end, Func<float, Quaternion> expected)
        {
            int count = Mathf.CeilToInt((end - start) * 30);
            var curves = new[] { "x", "y", "z", "w" }.Select(axis => AnimationUtility.GetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + axis))).ToArray();
            if (curves.Any(c => c == null || c.length != count + 1)) throw new InvalidOperationException("Prop rotation sample count differs.");
            var keys = curves.Select(c => c.keys).ToArray();
            for (int k = 0; k <= count; k++)
            {
                float t = Mathf.Lerp(start, end, (float)k / count);
                if (keys.Any(a => Math.Abs(a[k].time - t) > .0001)) throw new InvalidOperationException("Prop rotation clock differs.");
                var q = new Quaternion(keys[0][k].value, keys[1][k].value, keys[2][k].value, keys[3][k].value).normalized;
                if (Quaternion.Angle(q, expected(t)) > .1f) throw new InvalidOperationException("Prop rotation differs from source: " + path);
            }
        }
    }
}
