using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Components;

namespace KominkaHillside.Editor
{
    /// <summary>Owned world audio only. Keep playback/mix values; make spatial intent explicit.
    /// Existing VRC Near/Far/Gain remain author-controlled. AudioSource curves own curve-mode ranges.</summary>
    public static class KH_AudioSpatialEditor
    {
        public static void Configure(AudioSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            bool is3D = source.spatialBlend >= .999f;
            if (!is3D && source.spatialBlend > .001f)
                throw new InvalidOperationException("Mixed 2D/3D requires manual review: " + source.name);
            Undo.RecordObject(source, "Configure world audio");
            var spatial = source.GetComponent<VRCSpatialAudioSource>();
            if (spatial == null)
            {
                spatial = Undo.AddComponent<VRCSpatialAudioSource>(source.gameObject);
                spatial.Gain = 0f;
                spatial.Near = source.minDistance;
                spatial.Far = source.maxDistance;
                spatial.VolumetricRadius = 0f;
                spatial.UseAudioSourceVolumeCurve = true;
            }
            else Undo.RecordObject(spatial, "Configure world audio");
            spatial.enabled = true;
            spatial.EnableSpatialization = is3D;
            source.spatialize = is3D;
            source.dopplerLevel = 0f;
            if (!is3D) spatial.UseAudioSourceVolumeCurve = true;
            else if (spatial.UseAudioSourceVolumeCurve)
            {
                // Unity's logarithmic curve never reaches silence at maxDistance.
                if (source.rolloffMode == AudioRolloffMode.Logarithmic)
                {
                    source.SetCustomCurve(AudioSourceCurveType.CustomRolloff,
                        FiniteRolloff(source.minDistance, source.maxDistance));
                    source.rolloffMode = AudioRolloffMode.Custom;
                }
                spatial.Near = source.minDistance;
                spatial.Far = source.maxDistance;
            }
            else
            {
                // VRC owns these distances. Match the Editor fallback without replacing VRC falloff.
                source.minDistance = spatial.Near;
                source.maxDistance = spatial.Far;
                source.rolloffMode = AudioRolloffMode.Linear;
            }
            EditorUtility.SetDirty(source);
            EditorUtility.SetDirty(spatial);
            if (PrefabUtility.IsPartOfPrefabInstance(source))
                PrefabUtility.RecordPrefabInstancePropertyModifications(source);
            if (PrefabUtility.IsPartOfPrefabInstance(spatial))
                PrefabUtility.RecordPrefabInstancePropertyModifications(spatial);
        }

        public static AnimationCurve FiniteRolloff(float near, float far)
        {
            if (near <= 0f || far <= near) throw new ArgumentException("Require 0 < near < far");
            var keys = new List<Keyframe> { new Keyframe(0f, 1f), new Keyframe(near / far, 1f) };
            for (float d = near * 1.5f; d < far; d *= 1.5f)
                keys.Add(new Keyframe(d / far, near / d * (far - d) / (far - near)));
            keys.Add(new Keyframe(1f, 0f));
            var curve = new AnimationCurve(keys.ToArray());
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            return curve;
        }

        static string PathOf(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        static bool Owned(AudioSource s)
        {
            string p = PathOf(s.transform);
            return p == "KH_Dogs/KD_TreatPouch" || p == "KH_Dogs/KD_ShopKeeper"
                || p == "KH_Dogs/KD_Bgm" || p == "KH_TravelNotice"
                || p.StartsWith("KH_Dogs/KD_TreatFx/KD_TreatFxNode_", StringComparison.Ordinal)
                || p.StartsWith("KH_Dogs/KH_WaterSpot/Fx/WaterAudio", StringComparison.Ordinal)
                || p.StartsWith("KH_Dogs/KH_Wash_", StringComparison.Ordinal)
                || p.StartsWith("KH_WashStation/KH_WashShower_", StringComparison.Ordinal)
                || p.StartsWith("KH_WashStations/", StringComparison.Ordinal) && p.EndsWith("/Nozzle", StringComparison.Ordinal)
                || p.StartsWith("KH_RiverAmbience/River_", StringComparison.Ordinal)
                || p.StartsWith("KH_Dogs/KH_Live/KD_LiveStage_", StringComparison.Ordinal)
                || p.StartsWith("KH_Dogs/KD_Dog_", StringComparison.Ordinal)
                    && (s.name == "KD_BarkSource" || s.name == "KD_BreathSource");
        }

        static Scene CheckedScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != KH_CourseGroundEditor.ScenePath)
                throw new InvalidOperationException("Expected production scene in Edit mode");
            return scene;
        }

        [MenuItem("Tools/KominkaHillside/Audio/1 - Apply Spatial Settings")]
        public static void Apply()
        {
            var scene = CheckedScene();
            if (scene.isDirty) throw new InvalidOperationException("Save or resolve existing scene edits first");
            int count = 0, added = 0;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
            {
                if (!Owned(source)) continue;
                if (source.GetComponent<VRCSpatialAudioSource>() == null) added++;
                Configure(source);
                count++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[KH Audio] configured=" + count + " added=" + added + ". Verify, then save.");
        }

        [MenuItem("Tools/KominkaHillside/Audio/2 - Verify Spatial Settings")]
        public static void Verify()
        {
            var scene = CheckedScene();
            var report = new StringBuilder();
            int total = 0, checkedCount = 0, errors = 0;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
            {
                total++;
                string path = PathOf(source.transform);
                if (!Owned(source)) { report.AppendLine("UNMANAGED " + path); continue; }
                checkedCount++;
                string problem = Problem(source);
                if (problem != null) errors++;
                var spatial = source.GetComponent<VRCSpatialAudioSource>();
                report.AppendLine((problem == null ? "OK " : "FAIL " + problem + " ") + path
                    + " blend=" + source.spatialBlend + " volume=" + source.volume
                    + " range=" + source.minDistance + ".." + source.maxDistance
                    + " rolloff=" + source.rolloffMode + " VRC=" + (spatial != null)
                    + (spatial == null ? "" : " gain=" + spatial.Gain + " curve=" + spatial.UseAudioSourceVolumeCurve));
            }
            if (checkedCount == 0) errors++;
            report.Insert(0, "total=" + total + " checked=" + checkedCount + " unmanaged=" + (total - checkedCount) + " errors=" + errors + "\n");
            Directory.CreateDirectory("Temp/audio-audit");
            File.WriteAllText("Temp/audio-audit/verify.txt", report.ToString());
            if (errors > 0) throw new InvalidOperationException(report.ToString());
            Debug.Log("[KH Audio] " + report.ToString().Split('\n')[0]);
        }

        public static string Problem(AudioSource source)
        {
            if (source == null) return "missing AudioSource";
            var spatial = source.GetComponent<VRCSpatialAudioSource>();
            if (spatial == null || !spatial.enabled) return "missing/disabled VRC";
            bool is3D = source.spatialBlend >= .999f;
            if (source.spatialize != is3D || spatial.EnableSpatialization != is3D || source.dopplerLevel != 0f)
                return "spatialization/doppler";
            if (!is3D) return source.spatialBlend == 0f && spatial.UseAudioSourceVolumeCurve ? null : "2D mode";
            if (spatial.Near < 0f || spatial.Far <= spatial.Near || spatial.VolumetricRadius >= spatial.Far
                || !Mathf.Approximately(source.minDistance, spatial.Near) || !Mathf.Approximately(source.maxDistance, spatial.Far))
                return "range mismatch";
            if (!spatial.UseAudioSourceVolumeCurve) return null;
            if (source.rolloffMode == AudioRolloffMode.Linear) return null;
            if (source.rolloffMode != AudioRolloffMode.Custom) return "unbounded logarithmic falloff";
            var curve = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            if (curve == null || Mathf.Abs(curve.Evaluate(1f)) > .0001f || Mathf.Abs(curve.Evaluate(0f) - 1f) > .0001f
                || Mathf.Abs(curve.Evaluate(source.minDistance / source.maxDistance) - 1f) > .0001f
                || Mathf.Abs(curve.Evaluate(2f)) > .0001f) return "curve endpoints";
            float previous = 1f;
            for (int i = 0; i <= 1000; i++)
            {
                float value = curve.Evaluate(i / 1000f);
                if (value < -.0001f || value > previous + .0001f) return "curve overshoot/nonmonotonic";
                previous = value;
            }
            return null;
        }
    }
}
