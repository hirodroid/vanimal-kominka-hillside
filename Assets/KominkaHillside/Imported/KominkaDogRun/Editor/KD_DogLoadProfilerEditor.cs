using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// VER-DOG-002: 多頭時の負荷を **Play 中に自動で** 実測する使い捨てハーネス。
    ///
    /// ## なぜ手動の Profiler ウィンドウではなくこれなのか
    ///
    /// 「実機で体感差が無い」は **フレーム時間が VSync の天井に張り付いている**ときにも
    /// 同じ見え方をする (90fps VR なら 11.1ms を割っている限り何匹でも 90fps)。
    /// つまり **FPS を見ても「余裕があるか」は分からない。**
    /// 見るべきは天井を外したときの **CPU 時間の傾き (1 匹あたり何 ms 増えるか)**。
    ///
    /// 加えて、手で Profiler を睨んで 1 匹版と 4 匹版を比べる方式は
    /// - 比較のたびにシーンを編集する (差分が計測以外の理由で混ざる)
    /// - 犬の状態 (待機 / 徘徊 / 追跡) がフレームごとに違うのでノイズが大きい
    /// - どのマーカーを見たか記録に残らない
    /// という弱点がある。VER-DOG-001 の教訓 (**推測で書かず実測する / 壊れた計測の
    /// 生数値を読み替えない**) をそのまま踏むので、計測手順ごとコードにした。
    ///
    /// ## やること
    ///
    /// Play に入ると隠しオブジェクトが 1 つ生えて、以下を自動で回す:
    ///
    /// 1. ウォームアップ (シェーダ / GC / Udon の初回コストを捨てる)
    /// 2. VSync と targetFrameRate を外す = **天井を外す**
    /// 3. 犬の数を 4→3→2→1→1→2→3→4 と往復させ、各条件で数百フレーム平均を採る
    ///    (往復するのは、順序による熱ドリフトや状態の偏りを打ち消して見えるようにするため)
    /// 4. 最後に **4 匹のまま U# システムだけ止めた条件** を 1 つ足す
    ///    = 「描画 + Animator + PhysBone」と「U# の Update 群」を切り分ける
    /// 5. `Logs/ver-dog-002-load.txt` へ書き出して Play を抜ける
    ///
    /// **エディタ Play の絶対値は実機と一致しない** (Editor オーバーヘッド + ClientSim を含む)。
    /// このハーネスが答えるのは絶対値ではなく **「1 匹増やすと何がいくら増えるか」** の傾き。
    ///
    /// ## 使い方
    ///
    /// 1. `Tools/KominkaHillside/Menu/Imported-Unused/Dog Load/1 - Arm Load Profiler`
    /// 2. Play を押して **約 45 秒** 放置 (途中で操作しない — 撫でも投擲もしない)
    /// 3. 自動で Play が止まり、`Logs/ver-dog-002-load.txt` が出る
    ///
    /// 計測が終わったらこのファイルは消してよい (VER-DOG-002 の完了条件に含める)。
    /// </summary>
    public static class KD_DogLoadProfilerEditor
    {
        const string ArmKey = "KD_DogLoadProfiler.Armed";

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Load/1 - Arm Load Profiler")]
        static void Arm()
        {
            SessionState.SetBool(ArmKey, true);
            EditorUtility.DisplayDialog(
                "Dog Load Profiler",
                "計測を予約しました。\n\n" +
                "Play を押して 約 45 秒 放置してください。\n" +
                "(途中で犬に触らない・ボールを投げない — 条件がぶれます)\n\n" +
                "終わると自動で Play を抜け、Logs/ver-dog-002-load.txt が出ます。",
                "OK");
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Load/2 - Disarm")]
        static void Disarm()
        {
            SessionState.SetBool(ArmKey, false);
            Debug.Log("[KD_DogLoad] 予約を取り消しました。");
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            if (!SessionState.GetBool(ArmKey, false)) return;
            SessionState.SetBool(ArmKey, false);

            var go = new GameObject("KD_DogLoadSampler");
            go.hideFlags = HideFlags.DontSave;
            go.AddComponent<KD_DogLoadSampler>();
            Debug.Log("[KD_DogLoad] 計測を開始します。約 45 秒お待ちください。");
        }
    }

    /// <summary>
    /// 実測本体。**Editor アセンブリに置いてあるのでビルドには一切入らない。**
    /// (Play 中の Editor では Editor アセンブリの MonoBehaviour も普通に動く)
    /// </summary>
    public class KD_DogLoadSampler : MonoBehaviour
    {
        // ---- 計測条件 ----

        /// <summary>KD_DogDefs.All と同じ並び。N 匹条件では先頭 N 匹を残す。
        /// IMP-DOG-020: **手書きの名簿をやめて All から導出** (6 匹化で唯一の手書き 4 だった)</summary>
        static readonly string[] DogKeys = Array.ConvertAll(KD_DogDefs.All, d => d.key);

        struct Cond
        {
            public int dogs;
            public bool systems;   // U# システム (Fetch / Pet / Wander) を動かすか
            public bool physBones; // VRCPhysBone を有効にするか
            public int sweep;      // 何往復目か。**ドリフトを可視化するための札**
            public string label;
            public Cond(int d, bool s, int w, string l)
            { dogs = d; systems = s; physBones = true; sweep = w; label = l; }
            public Cond(int d, bool s, bool pb, int w, string l)
            { dogs = d; systems = s; physBones = pb; sweep = w; label = l; }
        }

        /// <summary>
        /// **往復を 2 回する。** 初回の計測 (2026-08-15) で、最初の 4 匹ブロックだけが
        /// 2 度目の 4 匹より 0.9ms 高く出た = ウォームアップが足りていなかった。
        /// 1 往復では「ドリフトなのか匹数の効果なのか」を区別できないので、
        /// **同じ条件を 4 回踏んで、sweep ごとの傾きが一致することを確認できる形**にした。
        /// </summary>
        static readonly Cond[] Plan = BuildPlan(DogKeys.Length);

        /// <summary>頭数 n の計画を組む (IMP-DOG-020 で 4 固定から一般化。中身は 4 匹版と同じ形):
        /// sweep 0/2 = n→1 降順、sweep 1/3 = 1→n 昇順 / 両端の nosys / PhysBone A/B ×2 (n 匹固定)。</summary>
        static Cond[] BuildPlan(int n)
        {
            var plan = new List<Cond>();
            for (int w = 0; w < 4; w++)
            {
                bool desc = (w % 2 == 0);
                for (int i = 0; i < n; i++)
                {
                    int d = desc ? n - i : i + 1;
                    plan.Add(new Cond(d, true, w, d.ToString(CultureInfo.InvariantCulture)));
                }
            }

            // 描画/Animator/Constraint と U# の切り分け (両端で採る)
            plan.Add(new Cond(n, false, 9, n + "-nosys"));
            plan.Add(new Cond(1, false, 9, "1-nosys"));

            // **PhysBone の直接切り分け** (VER-DOG-002 の元の仮説 = PhysBone が律速)。
            // 名前付きマーカーには PhysBone が 1 本も出てこないので、
            // 「見えない」を「無い」と読み替えず、**切って差を測る**。
            // 直前と同条件 (n 匹 / U# 有) の対照を隣に置いて、ドリフトと区別できるようにする
            plan.Add(new Cond(n, true, true,  8, n + "-pb-on"));
            plan.Add(new Cond(n, true, false, 8, n + "-pb-off"));
            plan.Add(new Cond(n, true, true,  8, n + "-pb-on2"));
            plan.Add(new Cond(n, true, false, 8, n + "-pb-off2"));
            return plan.ToArray();
        }

        /// <summary>
        /// **15 秒。** 5 秒では足りなかった (初回計測の #0 が 0.9ms 高い)。
        /// シェーダのウォーム / GC / Udon の初回コスト / recorder 生成をここで捨て切る。
        /// </summary>
        const float WarmupSec = 15f;
        const float SettleSec = 1.2f;   // 条件切り替え直後は捨てる (VER-DOG-001 の「残留」対策)
        const float SampleSec = 3.0f;

        // ---- 収集チャネル ----

        class Channel
        {
            public string label;
            public Func<double> read;
            public string unit;
        }

        readonly List<Channel> _channels = new List<Channel>();
        readonly List<ProfilerRecorder> _recorders = new List<ProfilerRecorder>();

        class Block
        {
            public Cond cond;
            public int frames;
            public double[] sums;
            /// <summary>フレーム時間の生値。**平均だけでは VR のカクつきが見えない**ので分位も出す</summary>
            public List<float> dts = new List<float>();
        }

        readonly List<Block> _blocks = new List<Block>();

        // ---- 対象オブジェクト ----

        GameObject[] _dogVisual;          // KD_Dog_{key}
        GameObject[][] _dogSystems;       // Fetch / Pet / Wander / Ball / ResetButton
        Behaviour[] _physBones;           // VRCPhysBone (型名で引く = SDK への参照を持たない)
        readonly List<string> _missing = new List<string>();

        // ---- 進行状態 ----

        int _planIndex = -1;
        float _phaseEnd;
        bool _sampling;
        bool _done;
        Block _current;

        int _prevVSync;
        int _prevTargetFps;
        bool _channelsBuilt;
        float _buildAt;

        void Start()
        {
            ResolveTargets();

            _prevVSync = QualitySettings.vSyncCount;
            _prevTargetFps = Application.targetFrameRate;
            // **天井を外す。** これをしないと「何匹でも同じ FPS」しか観測できない
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            float now = Time.realtimeSinceStartup;
            // recorder の生成コスト自体もウォームアップに含める (初回計測で #0 が高く出た反省)。
            // マーカーは遅延登録なので、生成は開始直後ではなく少し待ってから
            _buildAt = now + 3f;
            _phaseEnd = now + WarmupSec;
        }

        void ResolveTargets()
        {
            _dogVisual = new GameObject[DogKeys.Length];
            _dogSystems = new GameObject[DogKeys.Length][];

            for (int i = 0; i < DogKeys.Length; i++)
            {
                string k = DogKeys[i];
                _dogVisual[i] = Find("KD_Dog_" + k);

                var sys = new List<GameObject>();
                AddIfFound(sys, "KD_FetchSystem_" + k);
                AddIfFound(sys, "KD_PetSystem_" + k);
                AddIfFound(sys, "KD_WanderSystem_" + k);
                AddIfFound(sys, "KD_Ball_" + k);
                // KD_BallResetButton_* は 2026-09-03 に廃止 (IMP-FET-011)。
                // 呼び出しは手元メニューへ移り、そちらは**既定で非アクティブ**なので
                // 常時アクティブを前提にしたこの列挙には最初から乗らない
                _dogSystems[i] = sys.ToArray();
            }

            // VRCPhysBone は **型名で拾う** (Editor アセンブリから SDK を参照しないで済ませる)
            var all = FindObjectsOfType<MonoBehaviour>(true);
            var pb = new List<Behaviour>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (all[i].GetType().Name != "VRCPhysBone") continue;
                pb.Add(all[i]);
            }
            _physBones = pb.ToArray();
        }

        /// <summary>**このプロジェクトに残る唯一の実行時の名前引き。**
        ///
        /// IMP-INF-003 で Editor 側は `KD_SceneObjects.Find` (非アクティブ込み・シーン全体) へ
        /// 一本化したが、ここは Play 中の MonoBehaviour なので Editor ヘルパーを使えない。
        /// `GameObject.Find` はシーン全体を見るので**階層化しても引ける**が、
        /// **非アクティブを取りこぼす**。対象 (KD_Dog_* / KD_FetchSystem_* / KD_PetSystem_* /
        /// KD_WanderSystem_* / KD_Ball_*) はいずれも常時アクティブなので
        /// 構造上は安全 — **成立していることは `_missing` が空であることで毎回確かめる。**
        ///
        /// (`KD_BallResetButton_*` は 2026-09-03 に廃止。IMP-FET-011)</summary>
        GameObject Find(string name)
        {
            var go = GameObject.Find(name);
            if (go == null) _missing.Add(name);
            return go;
        }

        void AddIfFound(List<GameObject> list, string name)
        {
            var go = GameObject.Find(name);
            if (go == null) { _missing.Add(name); return; }
            list.Add(go);
        }

        // ---- チャネルの構築 (ウォームアップ後。マーカーは遅延登録されるため) ----

        void BuildChannels()
        {
            _channels.Add(new Channel
            {
                label = "frame (unscaled dt)",
                unit = "ms",
                read = () => Time.unscaledDeltaTime * 1000.0
            });

            // 名指しの候補。存在しないものは黙って落とす (存在有無も報告に出す)
            TryAddMarker("CPU Main Thread", ProfilerCategory.Internal, "Main Thread");
            TryAddMarker("PlayerLoop", ProfilerCategory.Internal, "PlayerLoop");
            TryAddMarker("Scripts BehaviourUpdate", ProfilerCategory.Scripts, "BehaviourUpdate");
            TryAddMarker("Scripts BehaviourLateUpdate", ProfilerCategory.Scripts, "BehaviourLateUpdate");
            TryAddMarker("Animators.Update", ProfilerCategory.Animation, "Animators.Update");
            TryAddMarker("MeshSkinning.Update", ProfilerCategory.Animation, "MeshSkinning.Update");
            TryAddMarker("Camera.Render", ProfilerCategory.Render, "Camera.Render");
            TryAddMarker("Physics.Processing", ProfilerCategory.Physics, "Physics.Processing");

            // **名前を推測しない。** VRC PhysBone / Udon / Constraint のマーカー名は
            // 実測でしか分からないので、登録済みマーカーを走査して拾う
            AutoDiscover(new[] { "PhysBone", "Udon", "Constraint", "Skin", "Animator" });

            // 描画側 (エディタ統計。実機の値ではないが「匹数で何倍になるか」は読める)
            _channels.Add(new Channel { label = "stat drawCalls", unit = "", read = () => UnityStats.drawCalls });
            _channels.Add(new Channel { label = "stat batches", unit = "", read = () => UnityStats.batches });
            _channels.Add(new Channel { label = "stat setPassCalls", unit = "", read = () => UnityStats.setPassCalls });
            _channels.Add(new Channel { label = "stat triangles", unit = "", read = () => UnityStats.triangles });
            _channels.Add(new Channel { label = "stat vertices", unit = "", read = () => UnityStats.vertices });
            _channels.Add(new Channel { label = "stat shadowCasters", unit = "", read = () => UnityStats.shadowCasters });
            _channels.Add(new Channel { label = "stat visibleSkinnedMeshes", unit = "", read = () => UnityStats.visibleSkinnedMeshes });
        }

        readonly List<string> _resolved = new List<string>();
        readonly List<string> _unresolved = new List<string>();

        void TryAddMarker(string label, ProfilerCategory cat, string marker)
        {
            var rec = ProfilerRecorder.StartNew(cat, marker);
            if (!rec.Valid)
            {
                rec.Dispose();
                _unresolved.Add(label + " (" + marker + ")");
                return;
            }
            _recorders.Add(rec);
            _resolved.Add(label + " (" + marker + ")");
            var captured = rec;
            _channels.Add(new Channel
            {
                label = label,
                unit = "ms",
                read = () => captured.LastValue * 1e-6
            });
        }

        void AutoDiscover(string[] keywords)
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);

            var seen = new HashSet<string>();
            var dump = new StringBuilder();
            int added = 0;

            foreach (var h in handles)
            {
                ProfilerRecorderDescription d;
                try { d = ProfilerRecorderHandle.GetDescription(h); }
                catch { continue; }

                dump.Append(d.Name).Append('\t').Append(d.Category.Name)
                    .Append('\t').Append(d.UnitType).Append('\n');

                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;

                bool hit = false;
                for (int i = 0; i < keywords.Length; i++)
                {
                    if (d.Name.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) < 0) continue;
                    hit = true;
                    break;
                }
                if (!hit) continue;
                if (!seen.Add(d.Name)) continue;
                if (added >= 40) continue;   // 際限なく recorder を作らない

                var rec = ProfilerRecorder.StartNew(d.Category, d.Name);
                if (!rec.Valid) { rec.Dispose(); continue; }
                _recorders.Add(rec);
                _resolved.Add("auto: " + d.Name + " [" + d.Category.Name + "]");
                added++;
                var captured = rec;
                _channels.Add(new Channel
                {
                    label = "auto " + d.Name,
                    unit = "ms",
                    read = () => captured.LastValue * 1e-6
                });
            }

            WriteFile("Logs/ver-dog-002-markers.txt", dump.ToString());
        }

        // ---- 進行 ----

        void LateUpdate()
        {
            if (_done) return;
            float now = Time.realtimeSinceStartup;

            if (_planIndex < 0)
            {
                if (!_channelsBuilt && now >= _buildAt)
                {
                    BuildChannels();
                    _channelsBuilt = true;
                }
                if (now < _phaseEnd) return;
                AdvanceTo(0);
                return;
            }

            if (_sampling)
            {
                Sample();
                if (now < _phaseEnd) return;
                _blocks.Add(_current);
                AdvanceTo(_planIndex + 1);
                return;
            }

            // settle 中
            if (now < _phaseEnd) return;
            _sampling = true;
            _phaseEnd = now + SampleSec;
            _current = new Block { cond = Plan[_planIndex], frames = 0, sums = new double[_channels.Count] };
        }

        void AdvanceTo(int index)
        {
            if (index >= Plan.Length) { Finish(); return; }

            _planIndex = index;
            _sampling = false;
            Apply(Plan[index]);
            _phaseEnd = Time.realtimeSinceStartup + SettleSec;
        }

        void Apply(Cond c)
        {
            for (int i = 0; i < DogKeys.Length; i++)
            {
                bool keep = i < c.dogs;
                if (_dogVisual[i] != null && _dogVisual[i].activeSelf != keep)
                    _dogVisual[i].SetActive(keep);

                bool sysOn = keep && c.systems;
                var sys = _dogSystems[i];
                for (int j = 0; j < sys.Length; j++)
                {
                    if (sys[j] == null) continue;
                    if (sys[j].activeSelf != sysOn) sys[j].SetActive(sysOn);
                }
            }

            for (int i = 0; i < _physBones.Length; i++)
            {
                if (_physBones[i] == null) continue;
                if (_physBones[i].enabled != c.physBones) _physBones[i].enabled = c.physBones;
            }
        }

        void Sample()
        {
            _current.frames++;
            for (int i = 0; i < _channels.Count; i++)
                _current.sums[i] += _channels[i].read();
            _current.dts.Add(Time.unscaledDeltaTime * 1000f);
        }

        // ---- 出力 ----

        void Finish()
        {
            _done = true;

            // 後始末: 全部戻す (Play を止めれば元に戻るが、止め忘れた場合の保険)
            Apply(new Cond(DogKeys.Length, true, 9, "restore"));
            QualitySettings.vSyncCount = _prevVSync;
            Application.targetFrameRate = _prevTargetFps;
            for (int i = 0; i < _recorders.Count; i++) _recorders[i].Dispose();
            _recorders.Clear();

            WriteFile("Logs/ver-dog-002-load.txt", BuildReport());
            Debug.Log("[KD_DogLoad] 計測完了 -> Logs/ver-dog-002-load.txt");

            EditorApplication.isPlaying = false;
        }

        string BuildReport()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();

            sb.AppendLine("VER-DOG-002 dog load measurement");
            sb.AppendLine("Unity        : " + Application.unityVersion);
            sb.AppendLine("Platform     : editor play mode (ClientSim). "
                + "absolute values differ from the real client - read the SLOPE, not the value.");
            sb.AppendLine("VSync        : forced off during measurement (was vSyncCount="
                + _prevVSync.ToString(ci) + ", targetFrameRate=" + _prevTargetFps.ToString(ci) + ")");
            sb.AppendLine("Warmup/Settle/Sample sec : " + WarmupSec.ToString(ci) + " / "
                + SettleSec.ToString(ci) + " / " + SampleSec.ToString(ci));
            sb.AppendLine("VRCPhysBone found        : " + _physBones.Length.ToString(ci));
            sb.AppendLine();

            if (_missing.Count > 0)
            {
                sb.AppendLine("!! MISSING OBJECTS (measurement is incomplete for these):");
                for (int i = 0; i < _missing.Count; i++) sb.AppendLine("   - " + _missing[i]);
                sb.AppendLine();
            }

            sb.AppendLine("resolved markers (" + _resolved.Count + "):");
            for (int i = 0; i < _resolved.Count; i++) sb.AppendLine("   + " + _resolved[i]);
            if (_unresolved.Count > 0)
            {
                sb.AppendLine("unresolved (marker not registered in this session):");
                for (int i = 0; i < _unresolved.Count; i++) sb.AppendLine("   - " + _unresolved[i]);
            }
            sb.AppendLine();

            // ---- 1) フレーム時間の分布 (平均だけでは VR のカクつきが見えない) ----
            sb.AppendLine("== frame time distribution per block (ms, VSync off) ==");
            sb.Append("block".PadRight(16)).Append("frames".PadLeft(8)).Append("mean".PadLeft(10))
              .Append("median".PadLeft(10)).Append("p95".PadLeft(10)).Append("p99".PadLeft(10))
              .Append("max".PadLeft(10));
            sb.AppendLine();
            for (int b = 0; b < _blocks.Count; b++)
            {
                var blk = _blocks[b];
                var s = new List<float>(blk.dts);
                s.Sort();
                sb.Append(("#" + b.ToString(ci) + " s" + blk.cond.sweep.ToString(ci)
                           + " " + blk.cond.label).PadRight(16));
                sb.Append(blk.frames.ToString(ci).PadLeft(8));
                sb.Append(Fmt(Mean(blk, 0)).PadLeft(10));
                sb.Append(Fmt(Pct(s, 0.50)).PadLeft(10));
                sb.Append(Fmt(Pct(s, 0.95)).PadLeft(10));
                sb.Append(Fmt(Pct(s, 0.99)).PadLeft(10));
                sb.Append(Fmt(s.Count > 0 ? s[s.Count - 1] : 0.0).PadLeft(10));
                sb.AppendLine();
            }
            sb.AppendLine();

            // ---- 2) sweep ごとの傾き。**一致しなければドリフトが残っている** ----
            sb.AppendLine("== slope per sweep (ms or units per extra dog) ==");
            int N = DogKeys.Length;
            sb.AppendLine("sweep 0/2 = " + N + "->1 descending, sweep 1/3 = 1->" + N + " ascending.");
            sb.AppendLine("IF THE FOUR SLOPES DISAGREE, THE MEASUREMENT STILL HAS DRIFT - do not average them away.");
            sb.AppendLine();
            sb.Append("channel".PadRight(38));
            for (int w = 0; w < 4; w++) sb.Append(("sweep" + w.ToString(ci)).PadLeft(11));
            sb.Append("median".PadLeft(11)).Append("spread".PadLeft(11));
            sb.AppendLine();

            for (int c = 0; c < _channels.Count; c++)
            {
                if (!AnyNonZero(c)) continue;
                var sl = new double[4];   // 4 = sweep の数 (頭数ではない)
                for (int w = 0; w < 4; w++)
                {
                    var y = new double[N];
                    for (int n = 1; n <= N; n++) y[n - 1] = MeanFor(n, true, w, c);
                    sl[w] = Slope(y);
                }
                var sorted = new List<double>(sl);
                sorted.Sort();
                double median = (sorted[1] + sorted[2]) * 0.5;
                double spread = sorted[3] - sorted[0];

                sb.Append(Trim(_channels[c].label, 36).PadRight(38));
                for (int w = 0; w < 4; w++) sb.Append(Fmt(sl[w]).PadLeft(11));
                sb.Append(Fmt(median).PadLeft(11));
                sb.Append(Fmt(spread).PadLeft(11));
                sb.AppendLine();
            }
            sb.AppendLine();

            // ---- 3) 匹数ごとに集約 (全 sweep 平均) ----
            sb.AppendLine("== by dog count (systems on; mean over all 4 sweeps) ==");
            sb.AppendLine("nosys = same dog count with Fetch/Pet/Wander/Ball/ResetButton disabled");
            sb.AppendLine("        (i.e. draw + Animator + Constraint + PhysBone only, no Udon)");
            sb.AppendLine();
            sb.Append("channel".PadRight(38));
            for (int n = 1; n <= N; n++) sb.Append(n.ToString(ci).PadLeft(11));
            sb.Append((N + "-1").PadLeft(11)).Append("1-nosys".PadLeft(11)).Append((N + "-nosys").PadLeft(11));
            sb.AppendLine();

            for (int c = 0; c < _channels.Count; c++)
            {
                if (!AnyNonZero(c)) continue;
                var y = new double[N];
                for (int n = 1; n <= N; n++) y[n - 1] = MeanForCount(n, true, c);

                sb.Append(Trim(_channels[c].label, 36).PadRight(38));
                for (int n = 0; n < N; n++) sb.Append(Fmt(y[n]).PadLeft(11));
                sb.Append(Fmt(y[N - 1] - y[0]).PadLeft(11));
                sb.Append(Fmt(MeanForCount(1, false, c)).PadLeft(11));
                sb.Append(Fmt(MeanForCount(N, false, c)).PadLeft(11));
                sb.AppendLine();
            }
            sb.AppendLine();
            sb.AppendLine("(channels that were zero in every block are omitted)");
            sb.AppendLine();

            // ---- 3b) PhysBone の A/B (4 匹固定。on/off を 2 回ずつ交互に踏んだ) ----
            sb.AppendLine("== PhysBone A/B at " + N + " dogs (" + _physBones.Length.ToString(ci)
                + " VRCPhysBone toggled) ==");
            sb.AppendLine("interleaved on/off/on/off so drift shows up as on-vs-on disagreement.");
            sb.AppendLine();
            sb.Append("channel".PadRight(38));
            sb.Append("pb-on".PadLeft(11)).Append("pb-off".PadLeft(11))
              .Append("cost".PadLeft(11)).Append("on-spread".PadLeft(11));
            sb.AppendLine();
            for (int c = 0; c < _channels.Count; c++)
            {
                if (!AnyNonZero(c)) continue;
                double on = LabelMean(N + "-pb-on", c), on2 = LabelMean(N + "-pb-on2", c);
                double off = LabelMean(N + "-pb-off", c), off2 = LabelMean(N + "-pb-off2", c);
                double onAvg = (on + on2) * 0.5;
                double offAvg = (off + off2) * 0.5;

                sb.Append(Trim(_channels[c].label, 36).PadRight(38));
                sb.Append(Fmt(onAvg).PadLeft(11));
                sb.Append(Fmt(offAvg).PadLeft(11));
                sb.Append(Fmt(onAvg - offAvg).PadLeft(11));
                sb.Append(Fmt(Math.Abs(on - on2)).PadLeft(11));
                sb.AppendLine();
            }
            sb.AppendLine("'cost' is only believable when it is larger than 'on-spread'.");
            sb.AppendLine();

            // ---- 4) 読み方 ----
            sb.AppendLine("== how to read ==");
            sb.AppendLine("- 'frame (unscaled dt)' with VSync off is the headline CPU number.");
            sb.AppendLine("- slope/dog x N tells you how many dogs fit inside the real frame budget");
            sb.AppendLine("  (PCVR 90Hz = 11.1ms, 72Hz = 13.9ms).");
            sb.AppendLine("- if 'Scripts BehaviourUpdate' slope is small but 'Animators.Update' /");
            sb.AppendLine("  Constraint slope is large, the bottleneck is the model, not the Udon code.");
            sb.AppendLine("- compare column '" + N + "' against '" + N + "-nosys': the difference is what the U# layer costs.");
            sb.AppendLine("- p99/max vs median shows judder, which is what a VR headset actually punishes.");

            return sb.ToString();
        }

        static string Trim(string s, int n) { return s.Length <= n ? s : s.Substring(0, n); }

        static string Fmt(double v)
        {
            var ci = CultureInfo.InvariantCulture;
            double a = Math.Abs(v);
            if (a >= 1000.0) return v.ToString("F0", ci);
            if (a >= 10.0) return v.ToString("F2", ci);
            return v.ToString("F4", ci);
        }

        static double Mean(Block b, int channel)
        {
            return b.frames > 0 ? b.sums[channel] / b.frames : 0.0;
        }

        static double Pct(List<float> sorted, double q)
        {
            if (sorted.Count == 0) return 0.0;
            int i = (int)(q * (sorted.Count - 1));
            if (i < 0) i = 0;
            if (i >= sorted.Count) i = sorted.Count - 1;
            return sorted[i];
        }

        /// <summary>どのブロックでも 0 だったチャネルは報告から落とす (自動探索のノイズ除け)</summary>
        bool AnyNonZero(int channel)
        {
            for (int b = 0; b < _blocks.Count; b++)
                if (Math.Abs(Mean(_blocks[b], channel)) > 1e-9) return true;
            return false;
        }

        double LabelMean(string label, int channel)
        {
            for (int b = 0; b < _blocks.Count; b++)
                if (_blocks[b].cond.label == label) return Mean(_blocks[b], channel);
            return 0.0;
        }

        double MeanFor(int dogs, bool systems, int sweep, int channel)
        {
            for (int b = 0; b < _blocks.Count; b++)
            {
                var c = _blocks[b].cond;
                if (c.dogs != dogs || c.systems != systems || c.sweep != sweep) continue;
                return Mean(_blocks[b], channel);
            }
            return 0.0;
        }

        double MeanForCount(int dogs, bool systems, int channel)
        {
            double sum = 0.0;
            int n = 0;
            for (int b = 0; b < _blocks.Count; b++)
            {
                var c = _blocks[b].cond;
                if (c.dogs != dogs || c.systems != systems) continue;
                // PhysBone の A/B ブロック (sweep 8) は匹数の集計に混ぜない。
                // 条件が (4, systems=true) と一致してしまうので、明示的に外す
                if (!c.physBones) continue;
                if (systems ? c.sweep >= 4 : c.sweep != 9) continue;
                sum += Mean(_blocks[b], channel);
                n++;
            }
            return n > 0 ? sum / n : 0.0;
        }

        /// <summary>x = 1..n の最小二乗の傾き (IMP-DOG-020 で n 一般化。4 匹では分母 5 / 重心 2.5 だった)</summary>
        static double Slope(double[] y)
        {
            int n = y.Length;
            if (n < 2) return 0.0;
            double xbar = (n + 1) * 0.5;
            double ybar = 0.0;
            for (int i = 0; i < n; i++) ybar += y[i];
            ybar /= n;
            double num = 0.0, den = 0.0;
            for (int i = 0; i < n; i++)
            {
                double dx = (i + 1) - xbar;
                num += dx * (y[i] - ybar);
                den += dx * dx;
            }
            return den > 0 ? num / den : 0.0;
        }

        static void WriteFile(string relative, string content)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        void OnDestroy()
        {
            for (int i = 0; i < _recorders.Count; i++) _recorders[i].Dispose();
            _recorders.Clear();
        }
    }
}
