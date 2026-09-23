using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Newtonsoft.Json.Linq;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PumpkinPreviewEditor
    {
        private sealed class PropSource
        {
            public string Version, Plan, Effects, Generated, Evidence;
        }
        private static PropSource RevisionSource(string version, string slug)
        {
            return new PropSource { Version = version, Plan = version + "/" + slug + ".showplan.json",
                Effects = version + "/effects.pumpkin" + slug.Substring(slug.Length - 3) + ".json",
                Generated = KD_LiveSetupEditor.ShowFolderOf(slug) + "/", Evidence = "Logs/pumpkin003-update" };
        }
        public static void ApplyRevisionVisuals(GameObject stage, PlayableDirector director, string version, string slug)
        {
            var source = RevisionSource(version, slug);
            WireStableBodies(stage, director, source);
            var shader = Shader.Find("KominkaDogRun/Pumpkin Palette");
            if (shader == null) throw new InvalidOperationException("Pumpkin shader missing.");
            foreach (var material in stage.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct())
            {
                if (!AssetDatabase.GetAssetPath(material).StartsWith(source.Generated, StringComparison.Ordinal))
                    throw new InvalidOperationException("Revision has an external material.");
                material.renderQueue = KD_LiveSetupEditor.LiveParticleQueue;
                EditorUtility.SetDirty(material);
            }
            foreach (var material in stage.GetComponentsInChildren<MeshRenderer>(true).Where(r => StableBodyNames.Contains(r.name))
                .Select(r => r.sharedMaterial).Distinct())
            {
                var texture = material.mainTexture;
                material.shader = shader; material.shaderKeywords = new string[0];
                material.mainTexture = texture; material.color = Color.white;
                material.SetFloat("_Metallic", 0); material.SetFloat("_Glossiness", .15f);
                material.SetTexture("_EmissionMap", texture); material.SetColor("_EmissionColor", Color.white * .25f);
                material.EnableKeyword("_EMISSION"); material.renderQueue = KD_LiveSetupEditor.LiveParticleQueue;
                EditorUtility.SetDirty(material);
            }
            // Preserve the full authored arch tail instead of the importer's generic 12s cap.
            var effects = JObject.Parse(File.ReadAllText(source.Plan))["sections"].SelectMany(s => s["effects"]).ToArray();
            foreach (var track in ((TimelineAsset)director.playableAsset).GetOutputTracks().OfType<ActivationTrack>())
            {
                var bound = director.GetGenericBinding(track) as GameObject;
                if (bound == null || !bound.GetComponentsInChildren<ParticleSystem>(true).Any(p => p.name == "ArchColour")) continue;
                var authored = effects.Where(e => (string)e["type"] + "_" + (string)e["preset"] == bound.name).OrderBy(e => (double)e["startTime"]).ToArray();
                var clips = track.GetClips().OrderBy(c => c.start).ToArray();
                if (clips.Length != authored.Length) throw new InvalidOperationException("Arch intervals differ.");
                double tail = bound.GetComponentsInChildren<ParticleSystem>(true).Max(p => p.main.startLifetime.constantMax);
                for (int i = 0; i < clips.Length; i++) clips[i].duration = Math.Min(120, (double)authored[i]["endTime"] + tail) - clips[i].start;
                EditorUtility.SetDirty(track);
            }
            WirePropMotion(stage, director, source);
            EditorUtility.SetDirty(director.playableAsset);
        }
        public static void VerifyRevisionVisuals(GameObject stage, PlayableDirector director, string version, string slug)
        {
            var source = RevisionSource(version, slug);
            Directory.CreateDirectory(source.Evidence);
            VerifyStableBodies(stage, director, source);
            VerifyPropMotion(stage, director, source);
        }
    }
}
