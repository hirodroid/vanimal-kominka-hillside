using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-ENV-006: 川のせせらぎ音 (Adobe Firefly 生成、採用版 middle) の取り込みと、川沿いのループ 3D 環境音の配置。
    ///
    /// Course/110 は次を行い、本番シーンを保存する (再実行しても同じ結果。root の下だけを触る):
    /// 1. Assets/KominkaHillside/Audio/Ambience/River/Firefly_audio_小川の水音_middle.wav の SHA256 を Issue 記載の値と照合 (違えば何も変えずに止まる)。
    /// 2. 取り込み設定: mono (stereo は L/R 相関 0.96 で 3D 音源には不要)・Normalize 切 (mono 化で peak 0 dBFS へ +11.4 dB 持ち上がるのを止める)・
    ///    Vorbis 品質 <see cref="Quality"/>・Decompress On Load・Preload・サンプルレート維持。既定 + Standalone / Android / iOS を同じ形に。
    /// 3. 本番シーンのルート <see cref="RootName"/> の下に、川の中心線 (KH_CourseGroundEditor.RiverAt) に沿って <see cref="ZStart"/>〜<see cref="ZEnd"/> を
    ///    弧長 約 <see cref="Spacing"/> m おきに AudioSource (3D・loop・PlayOnAwake) + VRCSpatialAudioSource を置く。数と位置は <see cref="Plan"/> が決める。
    ///    Udon なし・同期なし (ローカル)。音量・Near / Far・ピッチは **このファイルの定数が正本** (毎回書く。耳で詰めるときは定数を直して流し直す)。
    /// 4. 保存後に Course/111 と同じ検査を行う。
    ///
    /// ループ開始のずらしは Udon なしの方法で行う: 隣り合う音源のピッチを ±1〜5% ずつ変える (<see cref="Pitches"/>)。
    /// 広帯域ノイズは位相が ms ずれれば無相関になるので、再生開始から ~0.1 s で隣の音源と足し算が「電力和」になり、
    /// ループ長も音源ごとに違って 8 s の繰り返しが揃わない。
    ///
    /// 音量操作・ライブ中の扱いは配線しない (設定メニューに行を足さず、固定音量)。理由と選択肢は制作票 docs/plans/2026-10-02-river-ambience-production.md。</summary>
    public static class KH_RiverAmbienceEditor
    {
        public const string ClipPath = "Assets/KominkaHillside/Audio/Ambience/River/Firefly_audio_小川の水音_middle.wav";
        /// <summary>IMP-ENV-006 記載の SHA256 (middle)。</summary>
        public const string ClipSha256 = "c440dbd630e1b884e29c936b980ae46b405a5b2941a408d3af1d4c9e0029e84a";
        /// <summary>音源の事実 (2026-10-02 Node で実測): 8.000 s = 384,000 フレーム、48 kHz、16 bit stereo、peak -11.4 dBFS / RMS -29.2 dBFS。</summary>
        public const int ClipFrequency = 48000, ClipSamples = 384000;

        public const string RootName = "KH_RiverAmbience";
        const string OutDir = "Temp/imp-env-006";

        // ---- 配置 (根拠は制作票。川の中心線から Node で試算: 弧長 544 m を 28.6 m おき = 20 本、散歩道の川沿い区間で常に 3〜5 本が聞こえ、川から 50 m 以上では 0 本) ----
        /// <summary>音源を置く川の z 範囲。散歩道が川から 40 m 以内にいる区間 (z 約 150〜548) を覆う。範囲の外 (川下 z &lt; 135・川上 z &gt; 550) には置かない。</summary>
        public const float ZStart = 135f, ZEnd = 550f;
        /// <summary>隣り合う音源の弧長間隔の目安 (実際は両端を含めて等分した値)。</summary>
        public const float Spacing = 28f;
        /// <summary>水面からの高さ (m)。</summary>
        public const float SourceHeight = 0.5f;

        // ---- 音 ----
        /// <summary>作者の基準音量。ファイルは RMS -29 dBFS と小さい (Normalize 切)。0.8 で音源から Near (4 m) 以内が約 -31 dBFS RMS、
        /// 散歩道 (川から 8〜16 m、近似モデルで合計 -6〜-14 dB) で約 -37〜-45 dBFS RMS。BGM (RMS -23 dBFS × 0.35 × 個人 50% = 約 -38 dBFS) と同じ桁で、
        /// ボイスや犬の声 (0 dBFS 近く) の下に入る。実機・Play の耳で決める (変えるならここだけ)。</summary>
        public const float BaseVolume = 0.8f;
        /// <summary>VRCSpatialAudioSource: Near までは減衰しない / Far で無音。Gain は 0 dB (他の 3D 音と同じ)。</summary>
        public const float Near = 4f, Far = 48f, Gain = 0f, VolumetricRadius = 0f;
        public const int Priority = 160;
        /// <summary>隣り合う音源のピッチ (1 周期 5 本。隣との差は 1.25〜5%)。±2.5% は水音では音程として気にならない範囲。</summary>
        public static readonly float[] Pitches = { 1.000f, 1.025f, 0.975f, 1.0125f, 0.9875f };
        public const float MinPitchGap = 0.01f;

        // ---- 取り込み ----
        /// <summary>Vorbis 品質 (全プラットフォーム同じ)。mono 8 s なので容量はどの値でも小さい (予想 約 50〜120 KB)。実測はビルドサイズ計測で。</summary>
        public const float Quality = 0.7f;
        static readonly string[] Platforms = { "Standalone", "Android", "iOS" };

        // ---- 検査の閾値 ----
        const float CoverageLateral = 25f, CoverageDistance = 31f, HeadHeight = 1.6f;
        const int MaxVoicesAudible = 6, MaxSources = 24;
        const float ModelMaxDb = 2f;
        const float PositionTolerance = .05f;

        [Serializable] class Report
        {
            public string utc, scene, status, clip, sha256, importer;
            public int sources, errors, routePoints, routeCovered, routeSilentChecked, maxAudibleSources;
            public float clipSeconds, baseVolume, arcLength, modelMaxDb, modelMinDbCovered;
            public List<string> findings = new List<string>();
            public List<string> notes = new List<string>();
            public List<string> notCovered = new List<string>(NotCovered);
        }

        static readonly string[] NotCovered =
        {
            "実際の出音・音量バランス (BGM・ボイス・犬の声に対して)・水の自然さ (Play / 実機の耳)",
            "ループの継ぎ目 (8 s ごと。数値上は 99.9 パーセンタイルのステップで、他の音に隠れる見込み。耳で確認)",
            "VRChat の実際の距離減衰曲線 (モデルはこのファイルの Model* の近似。Near / Far は VRCSpatialAudioSource が正本)",
            "Quest 単体・iOS の聞こえ方・CPU / メモリ (Decompress On Load で 0.77 MB、同時に聞こえるのは最大 5 本の想定)",
            "ライブ中の扱い (固定音量で、ライブには連動しない。ライブ未配置)",
        };

        // ================================================================ 110

        [MenuItem("Tools/KominkaHillside/Course/110 - Build River Ambience")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            Directory.CreateDirectory(OutDir);
            var log = new StringBuilder("[KH River Ambience] Build (IMP-ENV-006)\n");
            if (!File.Exists(ClipPath)) throw new InvalidOperationException(ClipPath + " missing");
            string sha = Sha256(ClipPath);
            if (sha != ClipSha256) throw new InvalidOperationException(ClipPath + " SHA256 " + sha + " != " + ClipSha256 + " (big / little と取り違えていないか)。何も変えていない");
            EnsureImport(log);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
            if (clip == null) throw new InvalidOperationException("AudioClip not imported: " + ClipPath);

            var scene = SceneManager.GetActiveScene();
            var roots = FindRoots(scene);
            if (roots.Count > 1) throw new InvalidOperationException(RootName + " が " + roots.Count + " 個ある (手で 1 つにしてから再実行)");
            var plan = Plan();
            if (plan.Count < 2 || plan.Count > MaxSources) throw new InvalidOperationException("source count " + plan.Count + " outside 2.." + MaxSources);

            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-river-ambience.unity", true);
            try
            {
                var root = roots.Count == 1 ? roots[0] : new GameObject(RootName);
                if (roots.Count == 0) log.AppendLine("   created " + RootName);
                if (root.transform.parent != null) root.transform.SetParent(null, true);
                root.transform.position = Vector3.zero; root.transform.rotation = Quaternion.identity; root.transform.localScale = Vector3.one;
                root.SetActive(true);

                // 計画にない子 (旧い本数・名前違い) は消す
                var wanted = new HashSet<string>();
                for (int i = 0; i < plan.Count; i++) wanted.Add(NameOf(i));
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    var child = root.transform.GetChild(i);
                    if (wanted.Contains(child.name)) continue;
                    log.AppendLine("   removed " + child.name);
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }

                for (int i = 0; i < plan.Count; i++)
                {
                    var t = root.transform.Find(NameOf(i));
                    if (t == null) { var go = new GameObject(NameOf(i)); go.transform.SetParent(root.transform, false); t = go.transform; log.AppendLine("   created " + NameOf(i)); }
                    t.gameObject.SetActive(true);
                    t.position = plan[i].pos; t.rotation = Quaternion.identity; t.localScale = Vector3.one;

                    var source = t.GetComponent<AudioSource>(); if (source == null) source = t.gameObject.AddComponent<AudioSource>();
                    source.enabled = true;
                    source.clip = clip; source.loop = true; source.playOnAwake = true; source.mute = false;
                    source.volume = BaseVolume; source.pitch = plan[i].pitch; source.priority = Priority;
                    source.spatialBlend = 1f; source.dopplerLevel = 0f; source.bypassReverbZones = true; source.outputAudioMixerGroup = null;
                    // VRCSpatialAudioSource が距離の正本 (UseAudioSourceVolumeCurve 切)。AudioSource 側は保険 (他の 3D 音と同じ)
                    source.rolloffMode = AudioRolloffMode.Linear; source.minDistance = Near; source.maxDistance = Far;
                    EditorUtility.SetDirty(source);

                    var spatial = t.GetComponent<VRCSpatialAudioSource>(); if (spatial == null) spatial = t.gameObject.AddComponent<VRCSpatialAudioSource>();
                    spatial.EnableSpatialization = true; spatial.UseAudioSourceVolumeCurve = false;
                    spatial.Gain = Gain; spatial.Near = Near; spatial.Far = Far; spatial.VolumetricRadius = VolumetricRadius;
                    KH_AudioSpatialEditor.Configure(source);
                    EditorUtility.SetDirty(spatial);
                    EditorUtility.SetDirty(t.gameObject);
                }
                EditorUtility.SetDirty(root);

                // Udon なしの AudioSource でも空の NetworkID が残ることがある (vrc-scene-edit-safety)。型情報つきは触らない
                var descriptor = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
                if (descriptor != null)
                {
                    int dropped = descriptor.NetworkIDCollection.RemoveAll(x => x.gameObject == null && (x.SerializedTypeNames == null || x.SerializedTypeNames.Count == 0));
                    if (dropped > 0) { EditorUtility.SetDirty(descriptor); log.AppendLine("   dropped empty NetworkIDs " + dropped); }
                }

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("River ambience save failed");
            }
            catch
            {
                Debug.LogError("[KH River Ambience] Build failed; the scene before this run is at " + OutDir + "/before-river-ambience.unity. Reopen the scene without saving to discard partial changes.");
                File.WriteAllText(OutDir + "/river-ambience-build-log.txt", log.ToString());
                throw;
            }
            File.WriteAllText(OutDir + "/river-ambience-build-log.txt", log.ToString());
            var r = Check(false);
            r.status = r.findings.Count == 0 ? "RIVER_AMBIENCE_BUILT" : "RIVER_AMBIENCE_FAIL";
            Write(r, "river-ambience-build.json");
            if (r.findings.Count > 0) throw new InvalidOperationException("[KH River Ambience] RIVER_AMBIENCE_FAIL (build saved but checks failed):\n" + string.Join("\n", r.findings));
            Debug.Log("[KH River Ambience] RIVER_AMBIENCE_BUILT " + r.sources + " sources, arc " + r.arcLength.ToString("F0") + " m. Run Course/111 after reopening the scene.");
        }

        // ================================================================ 111

        [MenuItem("Tools/KominkaHillside/Course/111 - Verify River Ambience")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = Check(true);
            r.status = r.findings.Count == 0 ? "RIVER_AMBIENCE_OK" : "RIVER_AMBIENCE_FAIL";
            Write(r, "river-ambience-verify.json");
            if (r.findings.Count > 0) throw new InvalidOperationException("[KH River Ambience] RIVER_AMBIENCE_FAIL (" + r.findings.Count + "):\n" + string.Join("\n", r.findings));
            Debug.Log("[KH River Ambience] RIVER_AMBIENCE_OK sources=" + r.sources + " voices<=" + r.maxAudibleSources + " (" + OutDir + "/river-ambience-verify.json). Not covered: real sound, loop seam, devices, Live (notCovered).");
        }

        // ================================================================ 計画

        struct Src { public Vector3 pos; public float pitch; }

        static string NameOf(int i) { return "River_" + i.ToString("00"); }

        /// <summary>川の中心線を z 0.25 m 刻みで引き、水平の弧長で端を含めて等分した位置に置く。y は水面 + <see cref="SourceHeight"/>。</summary>
        static List<Src> Plan()
        {
            const float step = .25f;
            int steps = Mathf.RoundToInt((ZEnd - ZStart) / step);
            var zs = new float[steps + 1]; var xs = new float[steps + 1]; var ys = new float[steps + 1]; var arc = new float[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float z = ZStart + i * step; var c = KH_CourseGroundEditor.RiverAt(z);
                zs[i] = z; xs[i] = c.x; ys[i] = c.y;
                if (i > 0) arc[i] = arc[i - 1] + Mathf.Sqrt((xs[i] - xs[i - 1]) * (xs[i] - xs[i - 1]) + step * step);
            }
            float total = arc[steps];
            int n = Mathf.Max(1, Mathf.RoundToInt(total / Spacing));
            var list = new List<Src>(); int at = 0;
            for (int k = 0; k <= n; k++)
            {
                float t = total * k / n;
                while (at < steps && arc[at] < t) at++;
                list.Add(new Src { pos = new Vector3(xs[at], ys[at] + SourceHeight, zs[at]), pitch = Pitches[k % Pitches.Length] });
            }
            return list;
        }

        static float ArcLength()
        {
            const float step = .25f; int steps = Mathf.RoundToInt((ZEnd - ZStart) / step); float total = 0f; var prev = KH_CourseGroundEditor.RiverAt(ZStart);
            for (int i = 1; i <= steps; i++) { var c = KH_CourseGroundEditor.RiverAt(ZStart + i * step); total += Mathf.Sqrt((c.x - prev.x) * (c.x - prev.x) + step * step); prev = c; }
            return total;
        }

        // ================================================================ 検査

        static Report Check(bool hash)
        {
            var r = new Report { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, baseVolume = BaseVolume };
            var e = r.findings;
            // ---- 音源と取り込み設定 ----
            if (hash) { r.sha256 = File.Exists(ClipPath) ? Sha256(ClipPath) : "(missing)"; Req(e, r.sha256 == ClipSha256, "clip SHA256 = Issue value (" + r.sha256 + ")"); }
            else r.sha256 = "(checked before build)";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
            Req(e, clip != null, "clip imported " + ClipPath);
            if (clip != null)
            {
                r.clip = clip.name; r.clipSeconds = clip.length;
                Req(e, clip.channels == 1 && clip.frequency == ClipFrequency, "clip mono 48 kHz (channels " + clip.channels + ", " + clip.frequency + " Hz)");
                Req(e, clip.samples == ClipSamples, "clip keeps 384,000 frames after import (samples " + clip.samples + "; a difference moves the loop seam)");
                Req(e, clip.loadType == AudioClipLoadType.DecompressOnLoad, "clip load type DecompressOnLoad (" + clip.loadType + ")");
            }
            r.importer = ImporterSummary(e);

            // ---- 器 ----
            var scene = SceneManager.GetActiveScene();
            var roots = FindRoots(scene);
            Req(e, roots.Count == 1, RootName + " exactly once at the scene root (" + roots.Count + ")");
            if (roots.Count != 1) return r;
            var root = roots[0];
            Req(e, root.activeInHierarchy && root.transform.parent == null, "root active at the scene root");
            Req(e, root.GetComponents<Component>().Length == 1, "root has only a Transform");
            Req(e, root.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Length == 0, "no UdonBehaviour (local, no sync)");

            var plan = Plan();
            r.arcLength = ArcLength();
            r.sources = root.transform.childCount;
            Req(e, plan.Count >= 2 && plan.Count <= MaxSources, "planned count 2.." + MaxSources + " (" + plan.Count + ")");
            Req(e, root.transform.childCount == plan.Count, "child count = plan (" + root.transform.childCount + " / " + plan.Count + ")");
            Req(e, root.GetComponentsInChildren<AudioSource>(true).Length == plan.Count, "AudioSource count = plan (" + root.GetComponentsInChildren<AudioSource>(true).Length + ")");

            var positions = new List<Vector3>();
            for (int i = 0; i < plan.Count; i++)
            {
                var t = root.transform.Find(NameOf(i));
                if (t == null) { e.Add("FAIL " + NameOf(i) + " missing"); continue; }
                string n = NameOf(i);
                positions.Add(t.position);
                Req(e, t.gameObject.activeInHierarchy && t.parent == root.transform && t.childCount == 0, n + " active, direct child, no children");
                Req(e, (t.position - plan[i].pos).magnitude <= PositionTolerance, n + " at planned position (" + t.position + " vs " + plan[i].pos + "; river formula changed -> rebuild)");
                // 川の上にある: 中心線から半幅以内、水面 + SourceHeight
                var c = KH_CourseGroundEditor.RiverAt(t.position.z);
                Req(e, Mathf.Abs(t.position.x - c.x) <= KH_CourseGroundEditor.HalfWidth(t.position.z), n + " over the water (|x - centre| " + Mathf.Abs(t.position.x - c.x).ToString("F2") + " <= half width)");
                Req(e, Mathf.Abs(t.position.y - (c.y + SourceHeight)) <= PositionTolerance, n + " y = water surface + " + SourceHeight + " (" + t.position.y.ToString("F2") + " vs " + (c.y + SourceHeight).ToString("F2") + ")");
                var s = t.GetComponent<AudioSource>();
                Req(e, s != null && s.enabled && clip != null && s.clip == clip, n + " AudioSource enabled, clip = middle");
                if (s != null)
                {
                    Req(e, s.loop && s.playOnAwake && !s.mute, n + " loop + PlayOnAwake, not muted");
                    Req(e, s.spatialBlend == 1f && s.spatialize && s.dopplerLevel == 0f, n + " 3D (spatialBlend 1, spatialize, no doppler)");
                    Req(e, Mathf.Approximately(s.volume, BaseVolume) && s.priority == Priority, n + " volume " + s.volume + " / priority " + s.priority);
                    Req(e, Mathf.Abs(s.pitch - plan[i].pitch) < 1e-4f, n + " pitch " + s.pitch + " = " + plan[i].pitch);
                    Req(e, s.outputAudioMixerGroup == null, n + " no mixer group");
                    Req(e, s.rolloffMode == AudioRolloffMode.Linear && Mathf.Approximately(s.minDistance, Near) && Mathf.Approximately(s.maxDistance, Far), n + " AudioSource rolloff Linear " + s.minDistance + ".." + s.maxDistance + " (insurance)");
                }
                var sp = t.GetComponent<VRCSpatialAudioSource>();
                Req(e, sp != null && sp.EnableSpatialization && !sp.UseAudioSourceVolumeCurve && Mathf.Approximately(sp.Gain, Gain) && Mathf.Approximately(sp.Near, Near)
                    && Mathf.Approximately(sp.Far, Far) && Mathf.Approximately(sp.VolumetricRadius, VolumetricRadius),
                    n + " VRCSpatialAudioSource Gain " + Gain + " / Near " + Near + " / Far " + Far + " / Volumetric " + VolumetricRadius + (sp == null ? " (missing)" : " (actual " + sp.Gain + "/" + sp.Near + "/" + sp.Far + "/" + sp.VolumetricRadius + ")"));
            }
            // 隣り合う音源: 間隔 (弦) が弧長の目安の 0.5〜1.1 倍、ピッチが 1% 以上違う
            for (int i = 1; i < positions.Count; i++)
            {
                float d = Vector3.Distance(new Vector3(positions[i].x, 0, positions[i].z), new Vector3(positions[i - 1].x, 0, positions[i - 1].z));
                Req(e, d >= Spacing * .5f && d <= Spacing * 1.1f, NameOf(i - 1) + " -> " + NameOf(i) + " spacing " + d.ToString("F1") + " m within " + (Spacing * .5f) + ".." + (Spacing * 1.1f));
                Req(e, Mathf.Abs(plan[i].pitch - plan[i - 1].pitch) >= MinPitchGap, NameOf(i - 1) + " / " + NameOf(i) + " pitch differs by >= " + MinPitchGap);
            }

            if (positions.Count == plan.Count) CheckRoute(e, r, positions);
            r.notes.Add("音量・ライブ: 設定メニューに行を足さない固定音量 (BGM の個人音量にもライブの掛け金にも連動しない)。選択肢は制作票");
            r.notes.Add("ループ開始のずらし: Udon なし。隣の音源とピッチを 1.25〜5% 変えて無相関にする (Udon で AudioSource.time をずらす案は制作票)");
            r.errors = e.Count;
            return r;
        }

        /// <summary>散歩道 (KH_CourseGroundEditor.Route) に沿って: 川に近い所は聞こえ、川から遠い所は 0 本、同時に聞こえる本数と合計の大きさ (近似モデル) が過大でない。</summary>
        static void CheckRoute(List<string> e, Report r, List<Vector3> src)
        {
            var route = KH_CourseGroundEditor.Route;
            var river = new List<Vector2>(); for (float z = -30f; z <= 740f; z += 1f) { var c = KH_CourseGroundEditor.RiverAt(z); river.Add(new Vector2(c.x, z)); }
            float minDb = float.MaxValue, maxDb = float.MinValue; int maxVoices = 0;
            for (int i = 1; i < route.Length; i++)
            {
                float len = Vector3.Distance(route[i - 1], route[i]); int m = Mathf.Max(1, Mathf.CeilToInt(len / 2f));
                for (int k = 0; k < m; k++)
                {
                    var p = Vector3.Lerp(route[i - 1], route[i], k / (float)m); r.routePoints++;
                    var head = p + Vector3.up * HeadHeight;
                    float lateral = float.MaxValue; foreach (var q in river) lateral = Mathf.Min(lateral, Vector2.Distance(new Vector2(p.x, p.z), q));
                    float nearest = float.MaxValue, power = 0f; int audible = 0;
                    foreach (var s in src)
                    {
                        float d = Vector3.Distance(head, s); nearest = Mathf.Min(nearest, d);
                        float a = Amp(d); if (a > 0f) { audible++; power += a * a; }
                    }
                    maxVoices = Mathf.Max(maxVoices, audible);
                    if (lateral <= CoverageLateral && p.z >= ZStart && p.z <= ZEnd)
                    {
                        r.routeCovered++;
                        if (nearest > CoverageDistance) e.Add("FAIL route " + p + " is " + lateral.ToString("F0") + " m from the river but " + nearest.ToString("F0") + " m from the nearest source (> " + CoverageDistance + ")");
                        float db = 10f * Mathf.Log10(power + 1e-12f); minDb = Mathf.Min(minDb, db); maxDb = Mathf.Max(maxDb, db);
                    }
                    else if (lateral > Far + 2f)
                    {
                        r.routeSilentChecked++;
                        if (audible > 0) e.Add("FAIL route " + p + " is " + lateral.ToString("F0") + " m from the river but " + audible + " source(s) are within Far (silent away from the river)");
                    }
                }
            }
            r.maxAudibleSources = maxVoices;
            r.modelMaxDb = maxDb == float.MinValue ? 0f : maxDb; r.modelMinDbCovered = minDb == float.MaxValue ? 0f : minDb;
            Req(e, r.routeCovered > 100, "route points near the river found (" + r.routeCovered + ")");
            Req(e, r.routeSilentChecked > 100, "route points far from the river found (" + r.routeSilentChecked + ")");
            Req(e, maxVoices <= MaxVoicesAudible, "at most " + MaxVoicesAudible + " sources audible at once along the route (" + maxVoices + ")");
            Req(e, r.modelMaxDb <= ModelMaxDb, "model total level along the river <= " + ModelMaxDb + " dB relative to one source at Near (" + r.modelMaxDb.ToString("F1") + ")");
        }

        /// <summary>近似モデル: Near までは 1、その先は Near / d、Far の手前 35% で 0 へ。VRChat の実際の曲線ではない (検査は「重なっても過大にならない」の目安)。</summary>
        static float Amp(float d)
        {
            if (d >= Far) return 0f;
            return Mathf.Min(1f, Near / Mathf.Max(d, .01f)) * Mathf.Min(1f, (Far - d) / (.35f * Far));
        }

        // ================================================================ 取り込み設定

        static void EnsureImport(StringBuilder log)
        {
            var imp = AssetImporter.GetAtPath(ClipPath) as AudioImporter;
            if (imp == null) { AssetDatabase.ImportAsset(ClipPath, ImportAssetOptions.ForceSynchronousImport); imp = AssetImporter.GetAtPath(ClipPath) as AudioImporter; }
            if (imp == null) throw new InvalidOperationException("AudioImporter missing: " + ClipPath + " (refresh first)");
            var so = new SerializedObject(imp);
            var normalize = so.FindProperty("m_Normalize");
            if (normalize == null) throw new InvalidOperationException("AudioImporter has no m_Normalize property (Unity version changed?). mono + Normalize で +11 dB 持ち上がるので止める。何も変えていない");
            bool changed = false;
            // SerializedObject の書き戻しを先に済ませる (後から imp の property を書くと古い写しで上書きされないように)
            if (normalize.boolValue) { normalize.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); changed = true; }
            if (!imp.forceToMono) { imp.forceToMono = true; changed = true; }
            if (imp.ambisonic) { imp.ambisonic = false; changed = true; }
            if (imp.loadInBackground) { imp.loadInBackground = false; changed = true; }
            var def = imp.defaultSampleSettings;
            if (!Matches(def)) { imp.defaultSampleSettings = Wanted(def); changed = true; }
            foreach (var platform in Platforms)
                if (!imp.ContainsSampleSettingsOverride(platform) || !Matches(imp.GetOverrideSampleSettings(platform)))
                { imp.SetOverrideSampleSettings(platform, Wanted(imp.GetOverrideSampleSettings(platform))); changed = true; }
            if (changed) { imp.SaveAndReimport(); log.AppendLine("   import " + ClipPath + " -> mono, Normalize off, Vorbis q" + Quality + " DecompressOnLoad, preload (reimported)"); }
            else log.AppendLine("   import " + ClipPath + " unchanged");
        }

        static AudioImporterSampleSettings Wanted(AudioImporterSampleSettings s)
        {
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.preloadAudioData = true;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = Quality;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            return s;
        }

        static bool Matches(AudioImporterSampleSettings s)
        {
            return s.loadType == AudioClipLoadType.DecompressOnLoad && s.preloadAudioData && s.compressionFormat == AudioCompressionFormat.Vorbis
                && Mathf.Abs(s.quality - Quality) < .001f && s.sampleRateSetting == AudioSampleRateSetting.PreserveSampleRate;
        }

        static string ImporterSummary(List<string> e)
        {
            var imp = AssetImporter.GetAtPath(ClipPath) as AudioImporter;
            if (imp == null) { e.Add("FAIL AudioImporter missing"); return "(none)"; }
            var sb = new StringBuilder("default " + Describe(imp.defaultSampleSettings));
            Req(e, Matches(imp.defaultSampleSettings), "import default Vorbis q" + Quality + " DecompressOnLoad preload preserve rate");
            var normalize = new SerializedObject(imp).FindProperty("m_Normalize");
            Req(e, imp.forceToMono && !imp.ambisonic && !imp.loadInBackground, "import force-to-mono, not ambisonic, no load-in-background");
            Req(e, normalize != null && !normalize.boolValue, "import Normalize off (mono would otherwise gain +11.4 dB)");
            foreach (var platform in Platforms)
            {
                bool has = imp.ContainsSampleSettingsOverride(platform);
                var s = imp.GetOverrideSampleSettings(platform);
                Req(e, has && Matches(s), "import override " + platform + " Vorbis q" + Quality + " DecompressOnLoad preload");
                sb.Append("; " + platform + " " + (has ? Describe(s) : "(no override)"));
            }
            sb.Append("; mono " + imp.forceToMono + ", normalize " + (normalize != null ? normalize.boolValue.ToString() : "(n/a)"));
            return sb.ToString();
        }

        static string Describe(AudioImporterSampleSettings s)
        {
            return s.compressionFormat + " q" + s.quality.ToString("0.##") + " " + s.loadType + (s.preloadAudioData ? " preload" : "") + " " + s.sampleRateSetting;
        }

        // ================================================================ 部品

        static List<GameObject> FindRoots(Scene scene)
        {
            var list = new List<GameObject>();
            foreach (var go in scene.GetRootGameObjects()) if (go.name == RootName) list.Add(go);
            return list;
        }

        static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var bytes = sha.ComputeHash(stream);
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static void Req(List<string> e, bool ok, string what) { if (!ok) e.Add("FAIL " + what); }

        static void Write(Report r, string name)
        {
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/" + name, JsonUtility.ToJson(r, true));
        }
    }
}
