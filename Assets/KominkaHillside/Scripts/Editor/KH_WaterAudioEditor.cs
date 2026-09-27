using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KominkaDogRun;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-020 段階 7: 水音 SE (合成。scripts/kh-gen-water-se.js) の取り込みと配線。
    ///
    /// Course/87 は次を行い、本番シーンを保存する:
    /// 1. Assets/KominkaHillside/Audio/Water/*.wav のインポート設定: Vorbis 品質 0.4・Load In Background・mono。Android / iOS も同じ値を上書きで固定。
    /// 2. KH_Dogs/KH_WaterSpot/Fx の下に、犬ごとの AudioSource (WaterAudio0..5。3D、Logarithmic、最大距離 20 m、playOnAwake なし) を置く。
    ///    KD_DogWanderController.HillsideWater が鳴らす位置を毎回その犬の足元へ動かすので、犬 1 匹に 1 つ。
    /// 3. 各犬の `waterAudio` / `waterStepClip` (HillsideWater の既存の欄) へ、その AudioSource と「ぱしゃ」(KH_WaterStep) を配線する。
    /// 再生の呼び出しは HillsideWater (別担当) にある。大きい水音 (KH_WaterSplash) と犬かき (KH_WaterPaddle) は取り込むだけで、
    /// 受ける欄がまだ無い (配線は、欄が足されたら同じ作法で足す)。Course/76 (KH_WaterSpotEditor) はここの配線を触らない。</summary>
    public static class KH_WaterAudioEditor
    {
        public const string Dir = "Assets/KominkaHillside/Audio/Water";
        public const string StepClip = Dir + "/KH_WaterStep.wav", SplashClip = Dir + "/KH_WaterSplash.wav", PaddleClip = Dir + "/KH_WaterPaddle.wav";
        const string SpotPath = "KH_Dogs/KH_WaterSpot", OutDir = "Temp/imp-dog-020";
        public const float Quality = 0.4f, MaxDistance = 20f, MinDistance = 1f;

        [Serializable] class Report
        {
            public string utc, scene, status;
            public int sources, dogsWired, errors;
            public long wavBytes;
            public List<string> findings = new List<string>();
            public string rights = "AI 制作 (Node による合成。外部の音源・サンプルなし)。制作票 docs/plans/2026-09-27-water-se-production.md";
        }

        [MenuItem("Tools/KominkaHillside/Course/87 - Build Water Audio")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = new Report { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            foreach (var path in new[] { StepClip, SplashClip, PaddleClip })
            {
                if (!File.Exists(path)) throw new InvalidOperationException(path + " missing: run node scripts/kh-gen-water-se.js, then refresh");
                r.wavBytes += new FileInfo(path).Length;
                Import(path);
            }
            var step = AssetDatabase.LoadAssetAtPath<AudioClip>(StepClip);
            if (step == null) throw new InvalidOperationException("Step clip not imported");
            var spot = GameObject.Find(SpotPath);
            var fx = spot != null ? spot.transform.Find("Fx") : null;
            if (fx == null) throw new InvalidOperationException(SpotPath + "/Fx missing: run Course/76 (water spot) first");
            var wanders = UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true).OrderBy(w => w.dogIndex).ToArray();
            if (wanders.Length != 6) throw new InvalidOperationException("Expected 6 wander controllers, found " + wanders.Length);
            Directory.CreateDirectory(OutDir);
            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-water-audio.unity", true);
            for (int i = 0; i < wanders.Length; i++)
            {
                var t = fx.Find("WaterAudio" + i);
                var go = t != null ? t.gameObject : new GameObject("WaterAudio" + i);
                go.transform.SetParent(fx, false);
                var src = go.GetComponent<AudioSource>(); if (src == null) src = go.AddComponent<AudioSource>();
                src.playOnAwake = false; src.loop = false; src.clip = null; src.volume = 0.8f;
                src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Logarithmic; src.minDistance = MinDistance; src.maxDistance = MaxDistance; src.dopplerLevel = 0f;
                EditorUtility.SetDirty(src); r.sources++;
                var so = new SerializedObject(wanders[i]);
                var a = so.FindProperty("waterAudio"); var c = so.FindProperty("waterStepClip");
                if (a == null || c == null) throw new InvalidOperationException("KD_DogWanderController.waterAudio / waterStepClip missing (compile first)");
                a.objectReferenceValue = src; c.objectReferenceValue = step; so.ApplyModifiedProperties();
                UdonSharpEditorUtility.CopyProxyToUdon(wanders[i]); EditorUtility.SetDirty(wanders[i]); r.dogsWired++;
            }
            EditorSceneManager.MarkSceneDirty(fx.gameObject.scene);
            if (!EditorSceneManager.SaveScene(fx.gameObject.scene)) throw new IOException("Water audio save failed");
            Check(r, fx, wanders, step);
            r.status = r.findings.Count == 0 ? "BUILT" : "FAIL"; r.errors = r.findings.Count;
            File.WriteAllText(OutDir + "/water-audio.json", JsonUtility.ToJson(r, true));
            if (r.findings.Count > 0) throw new InvalidOperationException("[KH Water Audio] " + string.Join("\n", r.findings));
            Debug.Log("[KH Water Audio] BUILT sources=" + r.sources + " dogs=" + r.dogsWired + " wav=" + r.wavBytes + " bytes");
        }

        static void Import(string path)
        {
            var imp = (AudioImporter)AssetImporter.GetAtPath(path);
            if (imp == null) { AssetDatabase.ImportAsset(path); imp = (AudioImporter)AssetImporter.GetAtPath(path); }
            var s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = Quality;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            imp.defaultSampleSettings = s; imp.forceToMono = true; imp.loadInBackground = true;
            foreach (var platform in new[] { "Android", "iOS" }) imp.SetOverrideSampleSettings(platform, s);
            imp.SaveAndReimport();
        }

        static void Check(Report r, Transform fx, KD_DogWanderController[] wanders, AudioClip step)
        {
            foreach (var path in new[] { StepClip, SplashClip, PaddleClip })
            {
                var imp = (AudioImporter)AssetImporter.GetAtPath(path);
                var s = imp.defaultSampleSettings;
                if (s.compressionFormat != AudioCompressionFormat.Vorbis || Mathf.Abs(s.quality - Quality) > 0.001f || !imp.loadInBackground) r.findings.Add("import settings " + path);
                foreach (var platform in new[] { "Android", "iOS" })
                {
                    if (!imp.ContainsSampleSettingsOverride(platform)) { r.findings.Add("no " + platform + " override " + path); continue; }
                    var o = imp.GetOverrideSampleSettings(platform);
                    if (o.compressionFormat != AudioCompressionFormat.Vorbis || Mathf.Abs(o.quality - Quality) > 0.001f) r.findings.Add(platform + " override " + path);
                }
            }
            for (int i = 0; i < wanders.Length; i++)
            {
                var src = fx.Find("WaterAudio" + i)?.GetComponent<AudioSource>();
                if (src == null || src.playOnAwake || src.spatialBlend < 0.99f || src.rolloffMode != AudioRolloffMode.Logarithmic || Mathf.Abs(src.maxDistance - MaxDistance) > 0.01f) r.findings.Add("audio source " + i);
                if (wanders[i].waterAudio != src || wanders[i].waterStepClip != step) r.findings.Add("dog " + i + " not wired");
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(wanders[i]);
                if (backing == null || !backing.publicVariables.TryGetVariableValue("waterStepClip", out object v) || !ReferenceEquals(v, step)) r.findings.Add("dog " + i + " backing waterStepClip");
            }
        }
    }
}
