using System.Collections.Generic;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-TRT-001 Phase 4b Step 7: **食後エフェクト 4 種と効果音**の構築と検証。
    ///
    /// **メニューを持たない。** `Treat System/2 - Setup` と `3 - Verify` から呼ばれる —
    /// おやつの一部なので、別のメニュー群にすると「どちらを流せばよいか」が増えてしまう。
    /// ファイルを分けたのは `KD_TreatSetupEditor` が既に大きいからで、責務は連続している。
    ///
    /// ## この Step の存在理由
    ///
    /// おやつ 4 種は**性能差がゼロ**で、値段差の根拠は「絵の強さ」しかない
    /// ([progression-ce.md §4](../../../docs/development/design/progression-ce.md))。
    /// **ここが無いと「なぜ 5pt のジャーキーを買うのか」を Play で判定できない。**
    ///
    /// ## 撫でと共有するのは「作り方」と「うるささの規律」だけ (設計 5)
    ///
    /// <code>
    /// 共有する    implicit 関数からのベイク (KD_ParticleBaker) / maxParticles &lt;= 30 の検査
    /// 共有しない  発火の口 (撫では持続・おやつは単発) / 同期の経路 / 付き先
    /// </code>
    /// </summary>
    public static class KD_TreatFxSetupEditor
    {
        public const string FxScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatFx.cs";
        public const string FxProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatFx.asset";

        const string TexDir = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/";
        const string SfxDir = "Assets/KominkaHillside/Imported/KominkaDogRun/Data/SE/";

        /// <summary>4 種で共通の「食べた」音。**犬の反応は全種同一**という設計に合わせる。</summary>
        const string SfxEat = SfxDir + "お菓子を食べる1.mp3";

        /// <summary>**ジャーキー (最上位) だけの追い音。**
        /// 設計表で音が書かれているのは 1 種だけで、そこが序列の印になる。</summary>
        const string SfxFlourish = SfxDir + "キラッ1.mp3";

        /// <summary>粒 1 回ぶんの数。**上限は粒側 (`maxParticles`) が持つ**ので、
        /// ここを増やしても 30 を超えては出ない。</summary>
        const int BurstCount = 10;

        // ================================================================ 品目 4 種
        //
        // **絵の違いが式の違いとして残る**のが implicit ベイクの値打ち。
        // 「なぜこの絵なのか」が後から読めるので、差し替えの判断ができる。

        struct FxDef
        {
            public string productKey;
            public string key;          // ノード名とアセット名
            public string label;        // ログ用
            public KD_ParticleBaker.Spec spec;
            public bool flourish;       // 追い音を付けるか

            public string TexPath { get { return TexDir + "KD_TreatFx_" + key + ".png"; } }
            public string MatPath { get { return TexDir + "M_KD_TreatFx_" + key + ".mat"; } }
            public string NodeName { get { return "KD_TreatFxNode_" + key; } }
        }

        /// <summary>**ハート。** 撫での ♡ と**同じ式・同じ既定値**を使う (設計 5 の唯一の共有点)。
        /// 骨型ビスケット = 基本のおやつなので、いちばん素直な絵をあてる。</summary>
        static KD_ParticleBaker.Spec HeartSpec() { return KD_ParticleBaker.HeartSpec(); }

        /// <summary>**肉球。** 円の重ね合わせ — 大きい掌球 1 + 指球 4。
        ///
        /// `f = min(各円の (距離² − 半径²))` で「どれか 1 つの内側なら内側」を作る
        /// (**和ではなく最小**。和にすると円の外側の値が足し合わさって縁が滲む)。</summary>
        static float Paw(float x, float y)
        {
            // 掌球 (下側の大きい円)
            float f = Circle(x, y, 0f, -0.30f, 0.62f);
            // 指球 4 つ。外側の 2 つは少し下げて扇に見せる
            f = Mathf.Min(f, Circle(x, y, -0.62f, 0.42f, 0.30f));
            f = Mathf.Min(f, Circle(x, y, -0.22f, 0.72f, 0.32f));
            f = Mathf.Min(f, Circle(x, y, 0.22f, 0.72f, 0.32f));
            f = Mathf.Min(f, Circle(x, y, 0.62f, 0.42f, 0.30f));
            return f;
        }

        static float Circle(float x, float y, float cx, float cy, float r)
        {
            float dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy - r * r;   // < 0 が内側
        }

        /// <summary>**星。** 極座標 `r &lt; a + b·cos(5θ)` の 5 芒星。
        ///
        /// `θ` に 5 を掛けると 5 回くびれる。`b/a` が深さで、大きくするととがる。
        /// **原点での退化を避ける**ため、中心付近は常に内側として扱う。</summary>
        static float Star(float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            if (r < 0.02f) return -1f;                       // 中心は必ず内側
            float theta = Mathf.Atan2(y, x);
            // 上を向いた星にする (π/2 の位相をずらす)
            float edge = 0.62f + 0.38f * Mathf.Cos(5f * (theta - Mathf.PI * 0.5f));
            return r - edge;                                  // < 0 が内側
        }

        /// <summary>**キラキラ。** 十字 + 対角の光条。
        ///
        /// 「軸に近いほど内側」を軸ごとに作り、**最小**で重ねる。
        /// 対角は細くして、十字が主で対角が従に見えるようにしてある
        /// (4 方向を同じ太さにすると「雪の結晶」に見えて、きらめきにならない)。</summary>
        static float Sparkle(float x, float y)
        {
            float f = Ray(x, y, 0f, 1.00f, 0.085f);            // 縦
            f = Mathf.Min(f, Ray(y, x, 0f, 1.00f, 0.085f));    // 横
            float s = 0.70710678f;                             // 45°
            float u = (x + y) * s, v = (x - y) * s;
            f = Mathf.Min(f, Ray(u, v, 0f, 0.62f, 0.045f));    // 斜め
            f = Mathf.Min(f, Ray(v, u, 0f, 0.62f, 0.045f));
            return f;
        }

        /// <summary>光条 1 本。<paramref name="along"/> 方向へ <paramref name="len"/> まで伸び、
        /// 先へ向かって細くなる紡錘。</summary>
        static float Ray(float along, float across, float from, float len, float halfWidth)
        {
            float t = Mathf.Abs(along - from);
            if (t > len) return 1f;
            float w = halfWidth * (1f - t / len);              // 先細り
            if (w <= 1e-5f) return 1f;
            return Mathf.Abs(across) - w;                      // < 0 が内側
        }

        static FxDef[] Defs
        {
            get
            {
                var list = new List<FxDef>();

                var heart = new FxDef();
                heart.productKey = "treat.bone_biscuit"; heart.key = "Heart"; heart.label = "小さなハート";
                heart.spec = HeartSpec(); heart.flourish = false;
                list.Add(heart);

                var paw = new FxDef();
                paw.productKey = "treat.paw_cookie"; paw.key = "Paw"; paw.label = "肉球";
                paw.spec = Baseline(Paw, new Color(1f, 0.62f, 0.72f), 2.9f, 0.48f, 9f);
                paw.flourish = false;
                list.Add(paw);

                var star = new FxDef();
                star.productKey = "treat.star_cookie"; star.key = "Star"; star.label = "星";
                star.spec = Baseline(Star, new Color(1f, 0.88f, 0.45f), 2.4f, 0.5f, 12f);
                star.flourish = false;
                list.Add(star);

                var sparkle = new FxDef();
                sparkle.productKey = "treat.jerky"; sparkle.key = "Sparkle"; sparkle.label = "キラキラ";
                sparkle.spec = Baseline(Sparkle, new Color(1f, 0.96f, 0.72f), 2.6f, 0.5f, 22f);
                sparkle.flourish = true;
                list.Add(sparkle);

                return list.ToArray();
            }
        }

        /// <summary>♡ 以外の共通の焼き方。**中心は白く、縁に品目の色**という型は揃える —
        /// 揃えておくと「同じワールドの粒」に見える。違うのは形と色だけ。</summary>
        static KD_ParticleBaker.Spec Baseline(KD_ParticleBaker.Field field, Color edge,
            float span, float centerV, float falloff)
        {
            var s = new KD_ParticleBaker.Spec();
            s.size = 256;
            s.span = span;
            s.centerU = 0.5f;
            s.centerV = centerV;
            s.field = field;
            s.edgeFalloff = falloff;
            s.edgeColor = edge;
            s.coreColor = Color.white;
            s.coreX = 0f;
            s.coreY = 0f;
            s.coreTightness = 1.2f;
            s.coreAlphaBase = 0.75f;
            return s;
        }

        // ================================================================ Setup

        /// <summary>`Treat System/2 - Setup` から呼ばれる。**作るだけ。**
        /// 裏山改変 (IMP-DOG-014): <paramref name="containerPath"/> を渡すと器をそこへ置き (裏山は KH_Dogs 直下)、
        /// **焼き済みのテクスチャ (古民家からコピーした同 GUID) は焼き直さない** (無いときだけ焼く)。null なら古民家どおり。</summary>
        public static bool Setup(KD_TreatBox box, KD_TreatSlot[] slots, KD_DogPack pack,
            StringBuilder log, string containerPath = null)
        {
            bool hillside = containerPath != null;
            bool ok = true;
            var defs = Defs;

            // **Program Asset が無いまま進まない。** 無いと `AddUdonSharpComponent` は
            // 器だけ作れてしまい、**配線も CopyProxyToUdon も静かに空振りする** —
            // Verify では「付いてはいるが全部 null」に見えて、原因が読めない
            // (2026-08-24 に実際に踏んだ。Setup を先に流したのが原因だった)
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(FxProgramPath) == null)
            {
                log.AppendLine("   FAIL " + FxProgramPath + " が無い。"
                    + "**先に Treat System/1 - Create Program Assets を流し、force refresh で"
                    + "コンパイルを終えてから** 2 - Setup を実行すること");
                return false;
            }

            // ---- テクスチャとマテリアル ----
            foreach (var d in defs)
            {
                // 裏山改変: 古民家から持ってきた焼き済みはそのまま使う (差分を出さない)
                if (!hillside || AssetDatabase.LoadAssetAtPath<Texture2D>(d.TexPath) == null)
                    KD_ParticleBaker.Bake(d.TexPath, d.spec);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(d.TexPath);
                if (tex == null) { log.AppendLine("   FAIL baked but not imported: " + d.TexPath); ok = false; continue; }

                var mat = AssetDatabase.LoadAssetAtPath<Material>(d.MatPath);
                if (mat == null)
                {
                    // 撫でと同じ **Legacy Shaders/Particles/Alpha Blended** (設計 5)
                    var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                    if (shader == null) { log.AppendLine("   FAIL shader not found"); ok = false; continue; }
                    mat = new Material(shader);   // (裏山ではコピー済みの M_KD_TreatFx_* があるので通らない想定)
                    AssetDatabase.CreateAsset(mat, d.MatPath);
                    log.AppendLine("   created " + d.MatPath);
                }
                mat.mainTexture = tex;
                EditorUtility.SetDirty(mat);
            }

            // ---- 器 (SyncMode None なので同居の制約は無いが、専用にしておく) ----
            int found;
            var fxGo = KD_SceneObjects.Find(KD_DogDefs.TreatFxName, out found);
            if (found > 1)
            {
                log.AppendLine("   FAIL " + KD_DogDefs.TreatFxName + $" が {found} 個ある");
                return false;
            }
            if (fxGo == null)
            {
                fxGo = new GameObject(KD_DogDefs.TreatFxName);
                KD_SceneObjects.PlaceIfNew(fxGo, hillside ? containerPath : KD_DogDefs.SystemsPath);   // 裏山改変: KH_Dogs 直下
                log.AppendLine("   created " + KD_DogDefs.TreatFxName);
            }

            var fx = fxGo.GetUdonSharpComponent<KD_TreatFx>();
            if (fx == null)
            {
                fx = fxGo.AddUdonSharpComponent<KD_TreatFx>();
                log.AppendLine("   added KD_TreatFx");
            }

            // ---- 品目ごとの粒と音源 ----
            var systems = new ParticleSystem[defs.Length];
            var audios = new AudioSource[defs.Length];
            var ids = new string[defs.Length];
            var flourishes = new AudioClip[defs.Length];
            var eatClip = AssetDatabase.LoadAssetAtPath<AudioClip>(SfxEat);
            var flourishClip = AssetDatabase.LoadAssetAtPath<AudioClip>(SfxFlourish);
            if (eatClip == null) { log.AppendLine("   FAIL eat clip not found: " + SfxEat); ok = false; }
            if (flourishClip == null) { log.AppendLine("   FAIL flourish clip not found: " + SfxFlourish); ok = false; }

            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var t = fxGo.transform.Find(d.NodeName);
                GameObject node = t != null ? t.gameObject : null;
                if (node == null)
                {
                    node = new GameObject(d.NodeName);
                    node.transform.SetParent(fxGo.transform, false);
                    log.AppendLine("   created " + d.NodeName);
                }

                var ps = node.GetComponent<ParticleSystem>();
                if (ps == null) ps = node.AddComponent<ParticleSystem>();
                ConfigureBurst(ps);

                var pr = node.GetComponent<ParticleSystemRenderer>();
                if (pr != null)
                    pr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(d.MatPath);

                var au = node.GetComponent<AudioSource>();
                if (au == null) au = node.AddComponent<AudioSource>();
                au.playOnAwake = false;
                au.loop = false;
                au.spatialBlend = 1f;      // 3D。**食べた場所から鳴る**のが要点
                au.minDistance = 1f;
                au.maxDistance = 12f;
                au.volume = 0.7f;
                global::KominkaHillside.Editor.KH_AudioSpatialEditor.Configure(au);

                systems[i] = ps;
                audios[i] = au;
                ids[i] = d.productKey;
                flourishes[i] = d.flourish ? flourishClip : null;
            }

            fx.box = box;
            fx.slots = slots;
            fx.pack = pack;
            fx.fx = systems;
            fx.fxAudio = audios;
            fx.productKeys = ids;
            fx.eatClip = eatClip;
            fx.flourishClips = flourishes;
            fx.burstCount = BurstCount;
            ok &= TryCopy(fx, log);
            log.AppendLine("   wired KD_TreatFx (" + defs.Length + " 種)");
            return ok;
        }

        /// <summary>単発の粒設定。**撫で (持続) とは別物。**
        ///
        /// `rateOverTime = 0` で「自分からは 1 粒も出さない」状態にし、
        /// `Emit(n)` で撃つときだけ出す。`loop = true` にしてあるのは
        /// **止まった ParticleSystem へ Emit しても更新されない**ため
        /// (粒が出たまま止まって見える)。</summary>
        static void ConfigureBurst(ParticleSystem ps)
        {
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;          // 出力 0 で回し続ける
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
            main.gravityModifier = -0.05f;    // 軽く上がる
            main.maxParticles = 20;           // **うるささの規律 (Verify が <= 30 を数える)**
            main.stopAction = ParticleSystemStopAction.None;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;       // **自発的には出さない。撃つときだけ**

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.06f;

            var col = ps.collision; col.enabled = false;
            var trails = ps.trails; trails.enabled = false;
            var noise = ps.noise; noise.enabled = false;
            var lights = ps.lights; lights.enabled = false;
            var sub = ps.subEmitters; sub.enabled = false;
        }

        // ================================================================ Verify

        /// <summary>`Treat System/3 - Verify` から呼ばれる。**数えるだけ。**</summary>
        public static int Verify(KD_TreatBox box, StringBuilder log)
        {
            int fail = 0;
            // **戻り値を返す。** 「これが無いなら以降を見ても意味が無い」検査があるので
            // (粒が null なら maxParticles を数えても NullReference になるだけ)
            bool Check(bool cond, string what)
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + what);
                if (!cond) fail++;
                return cond;
            }

            var pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(FxProgramPath);
            // **同期変数 0 本を型で担保する** — None なら [UdonSynced] は付けられない
            Check(pa != null && pa.behaviourSyncMode == BehaviourSyncMode.None,
                "KD_TreatFx の behaviourSyncMode == None (発火は dogEatMs から導出。同期 0 本)");

            int found;
            var fxGo = KD_SceneObjects.Find(KD_DogDefs.TreatFxName, out found);
            Check(found == 1, KD_DogDefs.TreatFxName + $" がシーン内に 1 個 (actual {found})");
            var fx = fxGo == null ? null : fxGo.GetUdonSharpComponent<KD_TreatFx>();
            Check(fx != null, "KD_TreatFx が付いている");
            if (fx == null) return fail;

            var defs = Defs;
            Check(fx.box == box && fx.box != null, "fx.box 配線 (食べ始めの正本)");
            Check(fx.slots != null && fx.slots.Length == KD_DogDefs.TreatSlotCount,
                $"fx.slots が枠数ぶんある (どの絵かは枠の product から引く)");
            Check(fx.pack != null, "fx.pack 配線");
            Check(fx.fx != null && fx.fx.Length == defs.Length,
                $"粒が {defs.Length} 本ある (actual {(fx.fx == null ? -1 : fx.fx.Length)})");
            Check(fx.productKeys != null && fx.productKeys.Length == defs.Length,
                "productKeys が同数");
            Check(fx.eatClip != null, "共通の「食べた」音がある");

            // **恒久 ID と添字の対応。** ずれると「星を買ったのに肉球が出る」になり、
            // しかも Play では品目の取り違えとしか見えない
            if (fx.productKeys != null)
                for (int i = 0; i < defs.Length && i < fx.productKeys.Length; i++)
                    Check(fx.productKeys[i] == defs[i].productKey,
                        $"productKeys[{i}] == {defs[i].productKey} ({defs[i].label})");

            // ---- うるささの規律を横展開する (設計 5「この検査ごと横展開する」) ----
            if (fx.fx != null)
                for (int i = 0; i < fx.fx.Length; i++)
                {
                    var ps = fx.fx[i];
                    string tag = (i < defs.Length ? defs[i].label : "?") + ": ";
                    if (!Check(ps != null, tag + "粒がある")) continue;
                    Check(ps.main.maxParticles <= 30,
                        tag + $"maxParticles <= 30 (actual {ps.main.maxParticles} — うるささの敵)");
                    Check(ps.main.simulationSpace == ParticleSystemSimulationSpace.World,
                        tag + "Simulation Space == World (撃つ直前に食べた場所へ移すため)");
                    // **自発的に出さない** — 撫で (持続) と混ざると出っぱなしになる
                    Check(ps.emission.rateOverTime.constant <= 0.001f,
                        tag + $"rateOverTime == 0 (単発。actual {ps.emission.rateOverTime.constant})");
                    var pr = ps.GetComponent<ParticleSystemRenderer>();
                    Check(pr != null && pr.sharedMaterial != null && pr.sharedMaterial.mainTexture != null,
                        tag + "マテリアルにテクスチャが刺さっている (null だと白い四角が出る)");
                }

            // ---- **4 種の絵が本当に違うか** ----
            //
            // この Step の存在理由が「値段差を絵の強さで正当化する」ことなので、
            // **同じ絵が 2 枚焼かれていたら Step ごと無意味になる。**
            // ベイク関数の引数を間違えると黙って同じ形が並ぶので、型で守る。
            // 見るのは**アルファだけ** — 色だけ違って形が同じものを「違う絵」と数えない
            var prints = new Dictionary<string, string>();
            int dup = 0, unreadable = 0;
            foreach (var d in defs)
            {
                string fp = KD_ParticleBaker.AlphaFingerprint(d.TexPath);
                if (fp.Length == 0) { unreadable++; continue; }
                if (prints.ContainsKey(fp)) { dup++; log.AppendLine($"       {d.label} は {prints[fp]} と同じ形"); }
                else prints[fp] = d.label;
            }
            Check(unreadable == 0, $"焼いたテクスチャが 4 種とも読める (読めない {unreadable} 件)");
            Check(dup == 0, $"**4 種の形がすべて違う** (重複 {dup} 件) — 値段差の根拠が絵の強さなので");

            return fail;
        }

        static bool TryCopy(UdonSharpBehaviour proxy, StringBuilder log)
        {
            if (proxy == null) return false;
            try
            {
                UdonSharpEditorUtility.CopyProxyToUdon(proxy);
                EditorUtility.SetDirty(proxy);
                return true;
            }
            catch (System.Exception e)
            {
                log.AppendLine("CopyProxyToUdon FAILED (" + proxy.GetType().Name + "): " + e.Message);
                return false;
            }
        }
    }
}
