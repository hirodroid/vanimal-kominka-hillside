using System.IO;
using System.Text;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-PET-003: 撫でハート (♡ パーティクル) のセットアップと検証。
    ///
    /// パーティクル設定は WashDog IMP-DW-003 の ♡ (実機確認済み) を踏襲:
    /// 少量 (4 粒/s・最大 20)・ふわっと上がる・Simulation Space=World
    /// (犬が動いても出た ♡ が引きずられない)。
    /// ♡ テクスチャは WashDog の素材を流用せず、ハート形の implicit 関数から
    /// エディタで焼く (このプロジェクトの資産をランタイム外部依存ゼロで完結させる)。
    ///
    /// **FX はボーンの子に置かない** — PhysBone チェーンが切れる (設計書 §3)。
    /// KD_PetSystem 直下に置き、位置は KD_DogPetController.LateUpdate が頭アンカーへ
    /// 追従させる。
    ///
    /// 実行順: 1 - Setup Pet Hearts → 2 - Verify Pet Hearts
    /// (KD_DogPetController は既存 Program Asset のため作成手順は不要)
    /// </summary>
    public static class KD_PetHeartsSetupEditor
    {
        const string HeartTexPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Textures/KD_HeartParticle.png";
        const string HeartMatPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_Heart.mat";
        const string FxName = "KD_PetHearts";
        // 2 種目 (中抜きハート)。2026-08-11 ユーザーが手で複製・調整したものを配線だけする
        // (Setup は作らない。無ければ 1 種のまま動く)。
        // **シーンのオブジェクト名と必ず一致させる** — ずれると Setup が heartFx2 を null で
        // 上書きし、しかも Verify は「無いなら null が正しい」と判定して PASS してしまう
        const string FxName2 = "KD_PetHearts2";

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pet System/4 - Setup Pet Hearts")]
        public static void Setup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_PetHearts] Setup");

            // ---- マテリアル (全犬で共用) ----
            // テクスチャはベイク版を**初期値としてだけ**使う。既にテクスチャが入っていれば
            // 触らない (2026-08-11 ユーザーが自作 HartParticle.png へ差し替え済み —
            // 再実行でベイク版へ戻さない)
            var mat = AssetDatabase.LoadAssetAtPath<Material>(HeartMatPath);
            if (mat == null)
            {
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(HeartTexPath) == null)
                {
                    BakeHeartTexture();
                    log.AppendLine("baked heart texture: " + HeartTexPath);
                }
                // WashDog の Heart.mat と同じ Alpha Blended (加算だと白飛びして ♡ の形が消える)
                mat = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
                mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(HeartTexPath);
                AssetDatabase.CreateAsset(mat, HeartMatPath);
                log.AppendLine("created " + HeartMatPath);
            }
            else
            {
                log.AppendLine("material reused (テクスチャ含め手調整を保持)");
            }

            // Primary の 2 種目を「見本」にする (後述の複製元)
            int primaryPetCount;
            var primaryPetGo = KD_SceneObjects.Find(KD_DogDefs.Primary.PetSystemName, out primaryPetCount);
            var templateFx2 = primaryPetGo == null ? null : primaryPetGo.transform.Find(FxName2);

            bool copyOk = true;
            int done = 0;
            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("-- " + def.displayName + " (" + def.key + ")");

                int petCount;
                var petGo = KD_SceneObjects.Find(def.PetSystemName, out petCount);
                if (petCount != 1 || petGo == null)
                {
                    log.AppendLine($"   FAIL {def.PetSystemName} がシーン内に {petCount} 個 (Pet System を先に構築する)");
                    copyOk = false;
                    continue;
                }
                var pet = petGo.GetUdonSharpComponent<KD_DogPetController>();
                if (pet == null || pet.headAnchor == null)
                {
                    log.AppendLine("   FAIL KD_DogPetController / headAnchor が無い (Pet System を先に構築する)");
                    copyOk = false;
                    continue;
                }

                // ---- FX GameObject (その犬の KD_PetSystem 直下 — 犬の階層には置かない) ----
                var fxT = petGo.transform.Find(FxName);
                bool created = fxT == null;
                if (created)
                {
                    var fxGo = new GameObject(FxName);
                    fxT = fxGo.transform;
                    fxT.SetParent(petGo.transform, false);
                    fxT.position = pet.headAnchor.position + Vector3.up * pet.heartOffsetY;
                    log.AppendLine("   created " + FxName + " under " + def.PetSystemName);
                }
                else
                {
                    log.AppendLine("   reused " + FxName);
                }

                var ps = fxT.GetComponent<ParticleSystem>();
                if (ps == null) ps = fxT.gameObject.AddComponent<ParticleSystem>();
                if (created) ConfigureHearts(ps, log); // 既存の手調整は潰さない (WashDog と同じ方針)
                var renderer = fxT.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                pet.heartFx = ps;

                // ---- 2 種目 (中抜きハート) ----
                // 元は 2026-08-11 にユーザーが手で複製・調整したもの。**設定は自動生成できない**ので、
                // 2 匹目以降は Primary の実物を複製する (手調整をそのまま引き継ぐ)。
                // 既にあるものは絶対に触らない
                var fxT2 = petGo.transform.Find(FxName2);
                if (fxT2 == null && templateFx2 != null && templateFx2.parent != petGo.transform)
                {
                    var clone = Object.Instantiate(templateFx2.gameObject, petGo.transform);
                    clone.name = FxName2;
                    fxT2 = clone.transform;
                    fxT2.position = pet.headAnchor.position + Vector3.up * pet.heartOffsetY;
                    log.AppendLine("   created " + FxName2 + " (" + KD_DogDefs.Primary.key +
                        " の実物を複製 — 手調整をそのまま引き継ぐ)");
                }
                var ps2 = fxT2 != null ? fxT2.GetComponent<ParticleSystem>() : null;
                pet.heartFx2 = ps2;
                log.AppendLine(ps2 != null
                    ? "   wired heartFx2 = " + FxName2
                    : "   " + FxName2 + " not found - heartFx2 = null (1 種で動く)");

                try
                {
                    UdonSharpEditorUtility.CopyProxyToUdon(pet);
                    EditorUtility.SetDirty(pet);
                }
                catch (System.Exception e)
                {
                    log.AppendLine("   CopyProxyToUdon FAILED: " + e.Message);
                    copyOk = false;
                }
                done++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("dogs=" + done + "/" + KD_DogDefs.All.Length);
            log.AppendLine(copyOk
                ? "SETUP RESULT: PASS (scene not saved yet)"
                : "SETUP RESULT: FAIL (force refresh 後に再実行)");
            Finish("Logs/imp-pet-003-setup.txt", log);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pet System/5 - Verify Pet Hearts")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_PetHearts] Verify");
            int fail = 0;
            void Check(bool cond, string what)
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + what);
                if (!cond) fail++;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(HeartMatPath);
            Check(mat != null && mat.mainTexture != null, "heart material exists with texture");

            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("-- " + def.displayName + " (" + def.key + ")");
                string tag = def.key + ": ";

                int petCount, dogCount;
                var petGo = KD_SceneObjects.Find(def.PetSystemName, out petCount);
                var dog = KD_SceneObjects.Find(def.DogName, out dogCount);
                Check(petCount == 1 && petGo != null,
                    tag + def.PetSystemName + " exists exactly once (actual " + petCount + ")");
                if (petGo == null) continue;

                var pet = petGo.GetUdonSharpComponent<KD_DogPetController>();
                var fxT = petGo.transform.Find(FxName);
                Check(pet != null, tag + "KD_DogPetController exists");
                Check(fxT != null, tag + FxName + " exists under " + def.PetSystemName);
                if (pet == null || fxT == null) continue;

                var ps = fxT.GetComponent<ParticleSystem>();
                Check(ps != null, tag + FxName + " has ParticleSystem");
                Check(pet.heartFx == ps, tag + "pet.heartFx wired");
                if (ps != null)
                {
                    Check(!ps.main.playOnAwake, tag + "playOnAwake off (撫でが成立するまで出ない)");
                    Check(ps.main.simulationSpace == ParticleSystemSimulationSpace.World,
                        tag + "Simulation Space == World (犬の移動で ♡ が引きずられない)");
                    Check(ps.main.maxParticles <= 30,
                        tag + $"maxParticles <= 30 (actual {ps.main.maxParticles} — うるささの敵)");
                    // 上限 16/s: 2026-08-11 のユーザー調整が 12/s (寿命 0.5-0.8s = 滞留約 8 粒)。
                    // これを超えて増やすときは PCVR で 1 分撫でて鬱陶しくないか確認してから
                    Check(ps.emission.rateOverTime.constant <= 16f,
                        tag + $"emission rate <= 16/s (actual {ps.emission.rateOverTime.constant})");
                    var r = fxT.GetComponent<ParticleSystemRenderer>();
                    Check(r != null && r.sharedMaterial == mat, tag + "renderer uses M_KD_Heart");
                }

                // 2 種目 (任意)。存在するなら配線と粒設定を 1 種目と同じ基準で検査する
                var fxT2 = petGo.transform.Find(FxName2);
                if (fxT2 != null)
                {
                    var ps2 = fxT2.GetComponent<ParticleSystem>();
                    Check(ps2 != null, tag + FxName2 + " has ParticleSystem");
                    Check(pet.heartFx2 == ps2, tag + "pet.heartFx2 wired");
                    if (ps2 != null)
                    {
                        Check(!ps2.main.playOnAwake, tag + FxName2 + ": playOnAwake off");
                        Check(ps2.main.simulationSpace == ParticleSystemSimulationSpace.World,
                            tag + FxName2 + ": Simulation Space == World");
                        Check(ps2.main.maxParticles <= 30,
                            tag + FxName2 + $": maxParticles <= 30 (actual {ps2.main.maxParticles})");
                        Check(ps2.emission.rateOverTime.constant <= 16f,
                            tag + FxName2 + $": emission rate <= 16/s (actual {ps2.emission.rateOverTime.constant})");
                        var r2 = fxT2.GetComponent<ParticleSystemRenderer>();
                        Check(r2 != null && r2.sharedMaterial != null, tag + FxName2 + ": renderer has material");
                    }
                }
                else
                {
                    Check(pet.heartFx2 == null, tag + FxName2 + " absent -> heartFx2 is null");
                }

                // 他の犬の FX を掴んでいないか (多頭で起きやすい取り違え)
                Check(pet.heartFx == null || pet.heartFx.transform.parent == petGo.transform,
                    tag + "heartFx がこの犬の PetSystem 配下");
                Check(pet.heartFx2 == null || pet.heartFx2.transform.parent == petGo.transform,
                    tag + "heartFx2 がこの犬の PetSystem 配下");

                // 犬の階層 (=ボーンの子) に置かれていないか + PhysBone チェーン外か (設計書 §3)
                var fxRoots = new[] { fxT, fxT2 };
                var fxNames = new[] { FxName, FxName2 };
                for (int i = 0; i < fxRoots.Length; i++)
                {
                    if (fxRoots[i] == null || dog == null) continue;
                    bool underDog = false;
                    for (var t = fxRoots[i]; t != null; t = t.parent)
                        if (t.gameObject == dog) underDog = true;
                    Check(!underDog, tag + fxNames[i] + " is NOT under " + def.DogName +
                        " (PhysBone チェーンを切らない)");
                    bool inChain = false;
                    foreach (var pb in dog.GetComponentsInChildren<VRCPhysBone>(true))
                    {
                        var root = pb.rootTransform != null ? pb.rootTransform : pb.transform;
                        for (var t = fxRoots[i]; t != null; t = t.parent)
                            if (t == root) { inChain = true; break; }
                    }
                    Check(!inChain, tag + fxNames[i] + " is outside all PhysBone chains");
                }
            }

            FinishVerify(log, fail);
        }

        /// <summary>♡ の粒設定 (WashDog IMP-DW-003 の実機確認済みの値)。</summary>
        internal static void ConfigureHearts(ParticleSystem ps, StringBuilder log)
        {
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.gravityModifier = -0.08f; // ふわっと上がる
            main.maxParticles = 20;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 4f; // 少量を漂わせる程度から (Play で足す — Issue の方針)

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;

            // 余計なモジュールを落とす (WashDog DisableExtras と同じ)
            var col = ps.collision; col.enabled = false;
            var trails = ps.trails; trails.enabled = false;
            var noise = ps.noise; noise.enabled = false;
            var lights = ps.lights; lights.enabled = false;
            var sub = ps.subEmitters; sub.enabled = false;

            log.AppendLine("configured hearts (rate 4/s, max 20, World space)");
        }

        /// <summary>♡ テクスチャを implicit 関数 (x²+y²−1)³ − x²y³ から焼く。
        /// 中心が白く縁がピンクに抜ける柔らかいグロー。
        ///
        /// **中身は <see cref="KD_ParticleBaker"/> へ移した** (IMP-TRT-001 設計 5
        /// 「撫でエフェクトとはテクスチャの作り方だけ共有する」)。
        /// **コピーではなく移動**なので、おやつ側が式を変えても ♡ は変わらないし、
        /// ♡ の焼き上がりも移動前と 1 ピクセルも変わらない
        /// (<see cref="KD_ParticleBaker.HeartSpec"/> が旧コードの定数をそのまま持っている)。</summary>
        static void BakeHeartTexture()
        {
            KD_ParticleBaker.Bake(HeartTexPath, KD_ParticleBaker.HeartSpec());
        }


        static void FinishVerify(StringBuilder log, int fail)
        {
            log.AppendLine(fail == 0 ? "VERIFY RESULT: PASS" : $"VERIFY RESULT: FAIL ({fail} checks)");
            Finish("Logs/imp-pet-003-verify.txt", log);
        }

        static void Finish(string file, StringBuilder log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }

        // =====================================================================
        // IMP-PET-006 / IMP-PET-008: 撫でエフェクトを 6 種にする
        //
        // **既存の ♡ (KD_PetHearts / KD_PetHearts2) は触らない。** 2026-08-11 にユーザーが
        // 手調整し PCVR で評価が出ている実物。**これを無料枠に据え、有料枠だけを作る。**
        //
        // 有料 5 種は **レシピからの生成物** (IMP-PET-008 で確定)。
        // 以前は「♡ を複製して材質だけ差し替える」だったが、それだと
        //   ・粒設定がハート専用のまま 6 種に流用される (花びらが上へ昇り、キラが漂う)
        //   ・複製元の startColor (Corgi はピンク) が金の肉球に乗って橙に濁る
        //   ・複製元が犬ごとに違う (Corgi = 手調整 / 他 3 匹 = Setup 既定) ので 4 匹で見た目が割れる
        // ので、**有料枠は Setup が毎回レシピを書く** (冪等。手で触った値は保持しない —
        // 手調整の聖域は無料 ♡ だけ)。
        //
        // Point の複合2種は「主モチーフ + 小さなきらめき」の2層。飾りは独立した粒で撒く
        // (1枚絵のまま撒くと同じ星座が毎秒コピーされる)。桜ハートは独立素材へ更新済み。
        // 元の商品サムネイルからcutoutを再実行して、この主モチーフを上書きしない。
        // =====================================================================

        const string PetFxMatDir = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/";
        const string ParticleTexDir = "Assets/KominkaHillside/Imported/KominkaDogRun/Textures/Particle/";
        const string PetFxPrefix = "KD_PetFx_";
        /// <summary>粒テクスチャの上限解像度。粒は画面上 5〜9cm なので 256 で足りる
        /// (元絵 1254px / mipmap 無しだと遠景でジャギる — IMP-PET-008 症状 4)。</summary>
        const int CoreTexSize = 256;
        const int AccentTexSize = 128;

        /// <summary>粒のレシピ。**無料 ♡ の手調整値 (寿命 0.5〜0.8 / 速さ 0.05〜0.1 /
        /// 大きさ 0.05〜0.09 / 重力 −0.08 / 半径 0.2) を基準に品目ごとにずらす。**</summary>
        struct FxRecipe
        {
            public float lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax;
            public float gravity, radius;
            /// <summary>0 = 回さない / 360 = 全周ランダム / それ以外 = ±deg のランダム。</summary>
            public float startRotDeg;
            /// <summary>0 = 無し / ±deg/s のランダムな自転 (花びら・キラ)。</summary>
            public float spinDegPerSec;
            /// <summary>大きさ 0→1→0 (キラの明滅)。</summary>
            public bool popShrink;
            /// <summary>0 = 無し / Noise の強さ (花びらの揺れ)。</summary>
            public float noise;
            /// <summary>startColor。**材質は白のまま、色は粒で付ける** (白い星を金に染める等)。</summary>
            public Color tint;

            public static FxRecipe Base()
            {
                var r = new FxRecipe();
                r.lifeMin = 0.5f; r.lifeMax = 0.8f;
                r.speedMin = 0.05f; r.speedMax = 0.1f;
                r.sizeMin = 0.05f; r.sizeMax = 0.09f;
                r.gravity = -0.08f; r.radius = 0.2f;
                r.tint = Color.white;
                return r;
            }
        }

        /// <summary>撫でエフェクトの品ぞろえ。**恒久 ID は追記のみ。並べ替えない。**
        ///
        /// 恒久 ID は**カテゴリを横断して一意**なので、おもちゃ (0〜5) と
        /// テスト予約 (29 / 30) を避けて 10 番台を使う。
        ///
        /// **無料枠 (10) だけは既存の ♡ をそのまま使う** (`tex` が空)。</summary>
        struct PetFxDef
        {
            public string productKey;
            public string key;      // ノード名と材質名に使う
            public string label;    // 棚の表示
            public string tex;      // 元絵 (棚向け)。空 = 既存の ♡ を使う (新規に作らない)
            public string coreTex;  // 粒に貼る絵。空 = tex をそのまま (単体絵)
            public string tex2;     // 2 層目の絵。空 = 1 層
            public int price;       // 0 = 無料 / CE は pt を持たない
            public bool ce;
            public float accentRateMultiplier;
            public FxRecipe layer1, layer2;
            public PetFxDef(string id, string key, string label, string tex)
            {
                productKey = id; this.key = key; this.label = label; this.tex = tex; var d=KD_ProductDefinitions.Get(id); this.price=d.price; this.ce=!string.IsNullOrEmpty(d.ceListingId);
                coreTex = ""; tex2 = ""; layer1 = FxRecipe.Base(); layer2 = FxRecipe.Base(); accentRateMultiplier = 0.5f;
            }
        }

        static PetFxDef[] PetFx
        {
            get
            {
                // 無料 = 既存の 2 層ハート。**新規に作らない (tex が空)**
                //
                // **値段はおもちゃ (10/20/30) とずらして 8/16/24** (2026-08-28)。
                // 同額だと同じ残高で両カテゴリが同時に買えるようになり、
                // 「あと N pt」の完了イベントが重なる (AvatarIdleArena IMP-FBK-017:
                // 節目の同時到達は通知も体感も殺す)。一律 20% 安 =
                // 「撫でエフェクトは少し気軽な商品」という扱いも兼ねる (演出差の範囲)。
                // §9 の体感検証対象 — Play で違和感が出たら 10/20/30 へ戻してよい
                var heart = new PetFxDef("petfx.heart", "Heart", "ハート", "");

                // 肉球クッキー: ♡ と同じ漂い方。少しだけ傾けて「同じ判子」に見せない
                var paw = new PetFxDef("petfx.paw_cookie", "PawCookie", "肉球", "KD_Particle_PawCookie");
                paw.layer1.startRotDeg = 20f;
                paw.layer1.gravity = -0.06f;

                // キラキラ: 短命でその場で明滅 (漂わせない)。占有率 7% の細い星なので小さめでよい
                var spark = new PetFxDef("petfx.sparkle", "Sparkle", "キラキラ", "KD_Particle_Sparkle");
                spark.layer1.lifeMin = 0.35f; spark.layer1.lifeMax = 0.6f;
                spark.layer1.speedMin = 0.02f; spark.layer1.speedMax = 0.05f;
                spark.layer1.sizeMin = 0.035f; spark.layer1.sizeMax = 0.07f;
                spark.layer1.gravity = 0f; spark.layer1.radius = 0.22f;
                spark.layer1.startRotDeg = 360f; spark.layer1.spinDegPerSec = 60f;
                spark.layer1.popShrink = true;

                // 花びら: **落ちる**。自転しながら揺れる (vc-live の confetti / stream の型)
                var petal = new PetFxDef("petfx.petal", "Petal", "花びら", "KD_Particle_Petal");
                petal.layer1.lifeMin = 1.1f; petal.layer1.lifeMax = 1.6f;
                petal.layer1.speedMin = 0.06f; petal.layer1.speedMax = 0.12f;
                petal.layer1.sizeMin = 0.045f; petal.layer1.sizeMax = 0.08f;
                petal.layer1.gravity = 0.03f; petal.layer1.radius = 0.2f;
                petal.layer1.startRotDeg = 360f; petal.layer1.spinDegPerSec = 90f;
                petal.layer1.noise = 0.06f;

                // 桜ハート (32pt): 主モチーフと小さなきらめきを別々の寿命・軌道で描く。
                var sakura = new PetFxDef("petfx.sakura_heart", "SakuraHeart", "桜ハート", "KD_Particle_SakuraHeart");
                sakura.coreTex = "KD_Particle_SakuraHeart_Core";
                sakura.layer1.lifeMin = 0.6f; sakura.layer1.lifeMax = 0.9f;
                sakura.layer1.sizeMin = 0.06f; sakura.layer1.sizeMax = 0.1f;
                sakura.layer1.startRotDeg = 10f;
                sakura.tex2 = "KD_Particle_SparkWhite";
                sakura.accentRateMultiplier = 2f;
                sakura.layer2.lifeMin = 0.7f; sakura.layer2.lifeMax = 1.15f;
                sakura.layer2.speedMin = 0.025f; sakura.layer2.speedMax = 0.07f;
                sakura.layer2.sizeMin = 0.015f; sakura.layer2.sizeMax = 0.03f;
                sakura.layer2.gravity = -0.008f; sakura.layer2.radius = 0.16f;
                sakura.layer2.startRotDeg = 360f; sakura.layer2.spinDegPerSec = 45f;
                sakura.layer2.noise = 0.025f;
                sakura.layer2.popShrink = true;
                sakura.layer2.tint = new Color(1f, 0.78f, 0.86f);

                // 金の肉球 (40pt): 肉球本体の周囲を小さな金色の星が舞う。
                var gold = new PetFxDef("petfx.gold_paw", "GoldPaw", "金の肉球", "KD_Particle_GoldPaw");
                gold.coreTex = "KD_Particle_GoldPaw_Core";
                gold.layer1.lifeMin = 0.6f; gold.layer1.lifeMax = 0.9f;
                gold.layer1.sizeMin = 0.06f; gold.layer1.sizeMax = 0.1f;
                gold.layer1.startRotDeg = 10f;
                gold.tex2 = "KD_Particle_SparkWhite";
                gold.accentRateMultiplier = 2f;
                gold.layer2.lifeMin = 0.7f; gold.layer2.lifeMax = 1.15f;
                gold.layer2.speedMin = 0.025f; gold.layer2.speedMax = 0.07f;
                gold.layer2.sizeMin = 0.015f; gold.layer2.sizeMax = 0.03f;
                gold.layer2.gravity = -0.008f; gold.layer2.radius = 0.16f;
                gold.layer2.startRotDeg = 360f; gold.layer2.spinDegPerSec = 45f;
                gold.layer2.noise = 0.025f;
                gold.layer2.popShrink = true;
                gold.layer2.tint = new Color(1f, 0.85f, 0.35f);

                var candy = BundleFx("petfx.candy", "Candy", "キャンディ", "KD_Particle_Candy", new Color(1f, 0.86f, 0.57f));
                candy.layer1.startRotDeg = 22f; candy.layer1.spinDegPerSec = 22f;
                var pumpkin = BundleFx("petfx.pumpkin", "Pumpkin", "かぼちゃ", "KD_Particle_PumpkinSimple", Color.white);
                pumpkin.tex2 = "KD_Particle_BatSilhouette";
                pumpkin.accentRateMultiplier = 1.25f;
                pumpkin.layer2.sizeMin = 0.022f; pumpkin.layer2.sizeMax = 0.034f;
                pumpkin.layer2.startRotDeg = 18f; pumpkin.layer2.spinDegPerSec = 20f;
                pumpkin.layer2.lifeMin = 0.85f; pumpkin.layer2.lifeMax = 1.3f;
                pumpkin.layer2.noise = 0.045f;
                var lantern = BundleFx("petfx.lantern", "Lantern", "提灯", "KD_Particle_Lantern", new Color(1f, 0.8f, 0.36f));
                lantern.layer1.startRotDeg = 8f;
                lantern.layer1.lifeMin = 0.9f; lantern.layer1.lifeMax = 1.25f;
                lantern.layer1.gravity = -0.018f; lantern.layer1.noise = 0.016f;
                var meteor = BundleFx("petfx.meteor", "Meteor", "流れ星", "KD_Particle_StarSimple", new Color(0.78f, 0.87f, 1f));
                meteor.tex2 = "KD_Particle_TwinkleSimple";
                meteor.layer1.startRotDeg = 25f; meteor.layer1.speedMin = 0.09f; meteor.layer1.speedMax = 0.16f;
                meteor.layer1.gravity = 0.008f;
                var confetti = BundleFx("petfx.confetti", "Confetti", "紙吹雪", "KD_Particle_Confetti", new Color(1f, 0.83f, 0.42f));
                confetti.layer1.sizeMin = 0.035f; confetti.layer1.sizeMax = 0.065f;
                confetti.layer1.lifeMin = 1.05f; confetti.layer1.lifeMax = 1.5f;
                confetti.layer1.startRotDeg = 360f; confetti.layer1.spinDegPerSec = 110f;
                confetti.layer1.gravity = 0.025f; confetti.layer1.noise = 0.04f;
                confetti.layer1.tint = new Color(1f, 0.63f, 0.79f);

                return new[] { heart, paw, spark, petal, sakura, gold, candy, pumpkin, lantern, meteor, confetti };
            }
        }

        /// <summary>棚 (`KD_ShopSetupEditor`) が読む品ぞろえ。
        /// **品目の正本はこちら** — パーティクルの器を持っている側が知っている。</summary>
        public static void GetShopEntries(out string[] ids, out string[] labels,
            out int[] prices, out bool[] ce, out string[] texNames)
        {
            var defs = PetFx;
            ids = new string[defs.Length]; labels = new string[defs.Length];
            prices = new int[defs.Length]; ce = new bool[defs.Length];
            texNames = new string[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                ids[i] = defs[i].productKey; labels[i] = defs[i].label;
                prices[i] = defs[i].price; ce[i] = defs[i].ce; texNames[i] = defs[i].tex;
            }
        }

        static PetFxDef BundleFx(string productKey, string key, string label, string texture, Color accentTint)
        {
            var d = new PetFxDef(productKey, key, label, texture);
            d.layer1.lifeMin = 0.65f; d.layer1.lifeMax = 1f;
            d.layer1.sizeMin = 0.065f; d.layer1.sizeMax = 0.105f;
            d.layer1.startRotDeg = 12f; d.layer1.gravity = -0.035f;
            d.tex2 = "KD_Particle_SparkWhite"; d.accentRateMultiplier = 2f;
            d.layer2.lifeMin = 0.7f; d.layer2.lifeMax = 1.15f;
            d.layer2.speedMin = 0.025f; d.layer2.speedMax = 0.07f;
            d.layer2.sizeMin = 0.015f; d.layer2.sizeMax = 0.03f;
            d.layer2.gravity = -0.008f; d.layer2.radius = 0.16f;
            d.layer2.startRotDeg = 360f; d.layer2.spinDegPerSec = 45f;
            d.layer2.noise = 0.025f; d.layer2.popShrink = true; d.layer2.tint = accentTint;
            return d;
        }

        static bool AppliesRecipe(PetFxDef d, bool layeredOnly, bool bundledOnly, bool simplifiedOnly = false)
        { return simplifiedOnly ? d.productKey == "petfx.pumpkin" || d.productKey == "petfx.meteor" : bundledOnly ? d.ce : !layeredOnly || (!d.ce && d.coreTex.Length > 0); }

        static string Layer1Tex(PetFxDef d) { return d.coreTex.Length > 0 ? d.coreTex : d.tex; }
        static string Layer1MatPath(PetFxDef d) { return PetFxMatDir + "M_KD_PetFx_" + d.key + ".mat"; }
        static string Layer2MatPath(PetFxDef d) { return PetFxMatDir + "M_KD_PetFx_" + d.key + "_L2.mat"; }
        static string TexPath(string texName) { return ParticleTexDir + texName + ".png"; }

        /// <summary>この Setup が面倒を見る粒テクスチャと上限解像度の一覧 (Verify も同じ表を見る)。</summary>
        static void ParticleTextures(out string[] names, out int[] maxSizes, bool layeredOnly = false, bool bundledOnly = false, bool simplifiedOnly = false)
        {
            var n = new System.Collections.Generic.List<string>();
            var s = new System.Collections.Generic.List<int>();
            foreach (var d in PetFx)
            {
                if (d.tex.Length == 0) continue;
                if (!AppliesRecipe(d, layeredOnly, bundledOnly, simplifiedOnly)) continue;
                if (!n.Contains(d.tex)) { n.Add(d.tex); s.Add(CoreTexSize); }
                if (d.coreTex.Length > 0 && !n.Contains(d.coreTex)) { n.Add(d.coreTex); s.Add(CoreTexSize); }
                if (d.tex2.Length > 0 && !n.Contains(d.tex2)) { n.Add(d.tex2); s.Add(d.tex2 == "KD_Particle_SparkWhite" ? AccentTexSize : CoreTexSize); }
            }
            names = n.ToArray(); maxSizes = s.ToArray();
        }

        /// <summary>粒向けのインポート設定を揃える (mipmap ON / alphaIsTransparency / 上限解像度 / Clamp)。
        /// 変わったときだけ再インポートする。</summary>
        static bool EnsureParticleImport(string path, int maxSize, StringBuilder log)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { log.AppendLine("FAIL importer が無い: " + path); return false; }
            bool changed = false;
            if (imp.textureType != TextureImporterType.Default) { imp.textureType = TextureImporterType.Default; changed = true; }
            if (!imp.mipmapEnabled) { imp.mipmapEnabled = true; changed = true; }
            if (!imp.alphaIsTransparency) { imp.alphaIsTransparency = true; changed = true; }
            if (imp.maxTextureSize != maxSize) { imp.maxTextureSize = maxSize; changed = true; }
            if (imp.wrapMode != TextureWrapMode.Clamp) { imp.wrapMode = TextureWrapMode.Clamp; changed = true; }
            if (changed) { imp.SaveAndReimport(); log.AppendLine("import fixed: " + path + " (mipmap / alphaIsTransparency / max " + maxSize + " / clamp)"); }
            return true;
        }

        static bool ParticleImportOk(string path, int maxSize)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            return imp != null && imp.mipmapEnabled && imp.alphaIsTransparency
                && imp.maxTextureSize <= maxSize && imp.wrapMode == TextureWrapMode.Clamp;
        }

        /// <summary>材質を用意し、**指す先のテクスチャを毎回そろえる** (材質は Setup の生成物)。</summary>
        static Material EnsureMaterial(string matPath, string texName, StringBuilder log)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath(texName));
            if (tex == null) { log.AppendLine("FAIL テクスチャが無い: " + TexPath(texName)); return null; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null)
            {
                // ♡ と同じ Alpha Blended。加算だと白飛びして形が消えるし、
                // **夏の昼下がりの屋外では加算の光は背景に負けてほぼ見えない**
                m = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
                m.mainTexture = tex;
                AssetDatabase.CreateAsset(m, matPath);
                log.AppendLine("created " + matPath);
            }
            else if (m.mainTexture != tex)
            {
                m.mainTexture = tex;
                EditorUtility.SetDirty(m);
                log.AppendLine("retextured " + matPath + " -> " + texName);
            }
            return m;
        }

        /// <summary>レシピを ParticleSystem へ書く。**毎回すべて書く** (冪等)。</summary>
        static void ApplyRecipe(ParticleSystem ps, FxRecipe r, Material mat)
        {
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(r.lifeMin, r.lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(r.speedMin, r.speedMax);
            main.startSize3D = false;
            main.startSize = new ParticleSystem.MinMaxCurve(r.sizeMin, r.sizeMax);
            main.startRotation3D = false;
            if (r.startRotDeg <= 0f) main.startRotation = new ParticleSystem.MinMaxCurve(0f);
            else if (r.startRotDeg >= 360f) main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            else main.startRotation = new ParticleSystem.MinMaxCurve(-r.startRotDeg * Mathf.Deg2Rad, r.startRotDeg * Mathf.Deg2Rad);
            main.startColor = new ParticleSystem.MinMaxGradient(r.tint);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(r.gravity);
            main.maxParticles = 20;
            main.stopAction = ParticleSystemStopAction.None;
            main.scalingMode = ParticleSystemScalingMode.Shape;

            // 量は実行時に KD_DogPetController が書く (主役2/6、飾りは品目ごとの倍率)。
            // ここに書く値は Verify の上限検査 (<= 16) のための既定
            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(6f);
            em.rateOverDistance = new ParticleSystem.MinMaxCurve(0f);
            em.SetBursts(new ParticleSystem.Burst[0]);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = r.radius;

            // 消え方: ぱっと消えず、寿命の後半 35% で透ける
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.65f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var sol = ps.sizeOverLifetime;
            sol.enabled = r.popShrink;
            if (r.popShrink)
            {
                sol.separateAxes = false;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0f), new Keyframe(0.2f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f)));
            }

            var rot = ps.rotationOverLifetime;
            rot.enabled = r.spinDegPerSec > 0f;
            if (rot.enabled)
            {
                rot.separateAxes = false;
                rot.z = new ParticleSystem.MinMaxCurve(-r.spinDegPerSec * Mathf.Deg2Rad, r.spinDegPerSec * Mathf.Deg2Rad);
            }

            var noise = ps.noise;
            noise.enabled = r.noise > 0f;
            if (noise.enabled)
            {
                noise.separateAxes = false;
                noise.strength = new ParticleSystem.MinMaxCurve(r.noise);
                noise.frequency = 0.6f;
                noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.3f);
                noise.quality = ParticleSystemNoiseQuality.Low;
            }

            // 余計なモジュールを落とす (複製元から何が乗っていても同じ器にする)
            var m1 = ps.collision; m1.enabled = false;
            var m2 = ps.trails; m2.enabled = false;
            var m3 = ps.lights; m3.enabled = false;
            var m4 = ps.subEmitters; m4.enabled = false;
            var m5 = ps.velocityOverLifetime; m5.enabled = false;
            var m6 = ps.limitVelocityOverLifetime; m6.enabled = false;
            var m7 = ps.inheritVelocity; m7.enabled = false;
            var m8 = ps.forceOverLifetime; m8.enabled = false;
            var m9 = ps.textureSheetAnimation; m9.enabled = false;
            var m10 = ps.externalForces; m10.enabled = false;
            var m11 = ps.colorBySpeed; m11.enabled = false;
            var m12 = ps.sizeBySpeed; m12.enabled = false;
            var m13 = ps.rotationBySpeed; m13.enabled = false;
            var m14 = ps.trigger; m14.enabled = false;

            var rd = ps.GetComponent<ParticleSystemRenderer>();
            if (rd != null)
            {
                rd.renderMode = ParticleSystemRenderMode.Billboard;
                rd.alignment = ParticleSystemRenderSpace.View;
                rd.sortMode = ParticleSystemSortMode.None;
                rd.sharedMaterial = mat;
                rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rd.receiveShadows = false;
            }

            // **出しっぱなしにしない。** 複製元が Play 中だと粒が出続ける
            ps.Stop();
            ps.Clear();
        }

        /// <summary>ノードを用意する (無ければ template を複製)。**既存の親も位置も触らない。**</summary>
        static Transform EnsureFxNode(Transform parent, string name, Transform template, Vector3 pos, StringBuilder log, string tag)
        {
            var t = parent.Find(name);
            if (t != null) return t;
            var clone = Object.Instantiate(template.gameObject, parent);
            clone.name = name;
            t = clone.transform;
            t.position = pos;
            // 複製元に子 (旧い 2 層目など) がぶら下がっていても持ち込まない
            for (int i = t.childCount - 1; i >= 0; i--) Object.DestroyImmediate(t.GetChild(i).gameObject);
            log.AppendLine("   " + tag + ": created " + name);
            return t;
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pet System/6 - Setup Pet Effects")]
        public static void SetupEffects()
        {
            SetupEffects(false);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pet System/8 - Setup Layered Point Effects")]
        public static void SetupLayeredEffects()
        {
            SetupEffects(true);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pet System/9 - Setup Bundled Effects")]
        public static void SetupBundledEffects() { SetupEffects(false, true); }
        public static void SetupSimplifiedEffects() { SetupEffects(false, true, true); }

        static void SetupEffects(bool layeredOnly, bool bundledOnly = false, bool simplifiedOnly = false)
        {
            if (EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().path != "Assets/Scenes/KominkaDogRun.unity")
                throw new System.InvalidOperationException("Target Edit scene required");
            var log = new StringBuilder();
            log.AppendLine("[KD_PetFx] Setup — 撫でエフェクト " + PetFx.Length + " 種");
            log.AppendLine(simplifiedOnly ? "IMP-PET-010: pumpkin and meteor only; all other recipes retained." : bundledOnly ? "同梱5種だけを更新。既存6種の設定・配線を保持。" : layeredOnly ? "複合Point2種だけを更新。無料・他のPoint品の粒設定は保持。"
                : "無料ハートを保持。有料枠はレシピを適用。");

            var defs = PetFx;
            bool ok = true;

            // 限定更新は保持対象を全犬で先に検査する。途中でnull配列を焼いてから失敗しない。
            if (layeredOnly || bundledOnly)
            {
                foreach (var dog in KD_DogDefs.All)
                {
                    var host = KD_SceneObjects.Find(dog.PetSystemName, out int count);
                    var controller = host == null ? null : host.GetComponent<KD_DogPetController>();
                    if (count != 1 || controller == null || controller.fxProductKeys == null
                        || controller.fxVariants == null || controller.fxVariants2 == null || controller.fxAccentRateMultipliers == null
                        || controller.fxVariants.Length != controller.fxProductKeys.Length
                        || controller.fxVariants2.Length != controller.fxProductKeys.Length
                        || controller.fxAccentRateMultipliers.Length != controller.fxProductKeys.Length)
                        throw new System.InvalidOperationException("Existing effect arrays incomplete: " + dog.key);
                    foreach (var effect in defs)
                    {
                        if (AppliesRecipe(effect, layeredOnly, bundledOnly, simplifiedOnly)) continue;
                        int previous = System.Array.IndexOf(controller.fxProductKeys, effect.productKey);
                        if (previous < 0 || controller.fxVariants[previous] == null
                            || (effect.tex2.Length > 0 && controller.fxVariants2[previous] == null))
                            throw new System.InvalidOperationException("Preserved effect missing: " + dog.key + "/" + effect.productKey);
                    }
                }
            }

            // ---- テクスチャのインポート設定 (全部そろえる) ----
            string[] texNames; int[] texSizes;
            ParticleTextures(out texNames, out texSizes, layeredOnly, bundledOnly, simplifiedOnly);
            for (int i = 0; i < texNames.Length; i++)
                if (!EnsureParticleImport(TexPath(texNames[i]), texSizes[i], log)) ok = false;

            // ---- 材質 (全犬で共用) ----
            var mats1 = new Material[defs.Length];
            var mats2 = new Material[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                if (defs[i].tex.Length == 0) continue;              // 無料枠は既存の材質を使う
                if (!AppliesRecipe(defs[i], layeredOnly, bundledOnly, simplifiedOnly)) continue;
                mats1[i] = EnsureMaterial(Layer1MatPath(defs[i]), Layer1Tex(defs[i]), log);
                if (mats1[i] == null) ok = false;
                if (defs[i].tex2.Length > 0)
                {
                    mats2[i] = EnsureMaterial(Layer2MatPath(defs[i]), defs[i].tex2, log);
                    if (mats2[i] == null) ok = false;
                }
            }
            AssetDatabase.SaveAssets();
            if (!ok)
            {
                log.AppendLine("SETUP RESULT: FAIL (素材が揃っていない — シーンは触っていない)");
                Finish("Logs/imp-pet-006-setup.txt", log);
                return;
            }

            // ---- 犬ごとに器を作る ----
            int done = 0;
            foreach (var def in KD_DogDefs.All)
            {
                int found;
                var petGo = KD_SceneObjects.Find(def.PetSystemName, out found);
                if (petGo == null || found != 1)
                {
                    log.AppendLine("FAIL " + def.PetSystemName + " が " + found + " 個");
                    ok = false; continue;
                }
                var pet = petGo.GetComponent<KD_DogPetController>();
                if (pet == null) { log.AppendLine("FAIL controller 無し: " + def.PetSystemName); ok = false; continue; }

                var baseFx = petGo.transform.Find(FxName);
                if (baseFx == null)
                {
                    log.AppendLine("FAIL " + FxName + " が無い。先に 4 - Setup Pet Hearts");
                    ok = false; continue;
                }

                var variants = new ParticleSystem[defs.Length];
                var variants2 = new ParticleSystem[defs.Length];
                var ids = new string[defs.Length];
                var accentRates = new float[defs.Length];
                if ((layeredOnly || bundledOnly) && (pet.fxVariants == null || pet.fxVariants.Length < 6
                    || pet.fxVariants2 == null || pet.fxVariants2.Length != pet.fxVariants.Length
                    || pet.fxProductKeys == null || pet.fxProductKeys.Length != pet.fxVariants.Length
                    || pet.fxAccentRateMultipliers == null || pet.fxAccentRateMultipliers.Length != pet.fxVariants.Length))
                {
                    log.AppendLine("FAIL existing effect arrays missing: " + def.key);
                    ok = false; continue;
                }

                for (int i = 0; i < defs.Length; i++)
                {
                    ids[i] = defs[i].productKey;
                    accentRates[i] = defs[i].accentRateMultiplier;
                    if (!AppliesRecipe(defs[i], layeredOnly, bundledOnly, simplifiedOnly))
                    {
                        int previous = System.Array.IndexOf(pet.fxProductKeys, defs[i].productKey);
                        if (previous < 0) { log.AppendLine("FAIL preserved effect missing: " + defs[i].productKey); ok = false; continue; }
                        variants[i] = pet.fxVariants[previous];
                        variants2[i] = pet.fxVariants2[previous];
                        accentRates[i] = pet.fxAccentRateMultipliers[previous];
                        continue;
                    }
                    if (defs[i].tex.Length == 0)
                    {
                        // 無料枠 = 既存の ♡ (2 層のまま。**1 行も触らない**)
                        variants[i] = baseFx.GetComponent<ParticleSystem>();
                        var b2 = petGo.transform.Find(FxName2);
                        variants2[i] = b2 != null ? b2.GetComponent<ParticleSystem>() : null;
                        continue;
                    }

                    string node1 = PetFxPrefix + defs[i].key;
                    var t1 = EnsureFxNode(petGo.transform, node1, baseFx, baseFx.position, log, def.key);
                    var ps1 = t1.GetComponent<ParticleSystem>();
                    if (ps1 == null) ps1 = t1.gameObject.AddComponent<ParticleSystem>();
                    ApplyRecipe(ps1, defs[i].layer1, mats1[i]);
                    variants[i] = ps1;

                    if (defs[i].tex2.Length > 0)
                    {
                        string node2 = node1 + "2";
                        var t2 = EnsureFxNode(petGo.transform, node2, t1, baseFx.position, log, def.key);
                        var ps2 = t2.GetComponent<ParticleSystem>();
                        if (ps2 == null) ps2 = t2.gameObject.AddComponent<ParticleSystem>();
                        ApplyRecipe(ps2, defs[i].layer2, mats2[i]);
                        variants2[i] = ps2;
                    }
                    else
                    {
                        variants2[i] = null;
                    }
                }
                log.AppendLine("   " + def.key + ": recipe applied to " + (simplifiedOnly ? 2 : bundledOnly ? 5 : layeredOnly ? 2 : defs.Length - 1) + " variants");

                pet.fxVariants = variants;
                pet.fxVariants2 = variants2;
                pet.fxProductKeys = ids;
                pet.fxAccentRateMultipliers = accentRates;
                try { UdonSharpEditorUtility.CopyProxyToUdon(pet); EditorUtility.SetDirty(pet); }
                catch (System.Exception e) { log.AppendLine("   CopyProxyToUdon FAILED: " + e.Message); ok = false; }
                done++;
            }

            if (layeredOnly)
            {
                foreach (var d in defs)
                {
                    if (d.coreTex.Length == 0) continue;
                    var itemGo = KD_SceneObjects.Find(KD_ProductDefinitions.SceneName(d.productKey));
                    var item = itemGo != null ? itemGo.GetComponent<KD_ShopItem>() : null;
                    if (item == null) { log.AppendLine("FAIL shop item missing: " + d.productKey); ok = false; continue; }
                    KD_ProductDefinitions.Apply(item, d.productKey);
                    KD_ProductsEditor.Copy(item);
                    log.AppendLine(d.productKey + ": Point " + d.price);
                }
            }

            if (ok && !layeredOnly) KD_BundledPetFxPrefabs.Apply();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("dogs=" + done + "/" + KD_DogDefs.All.Length);
            log.AppendLine(ok ? "SETUP RESULT: PASS (scene not saved yet)" : "SETUP RESULT: FAIL");
            Finish("Logs/imp-pet-006-setup.txt", log);
        }

        static bool Approx(float a, float b) { return Mathf.Abs(a - b) < 0.0005f; }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pet System/7 - Verify Pet Effects")]
        public static void VerifyEffects()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_PetFx] Verify");
            int fail = 0;
            void Check(bool cond, string what)
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + what);
                if (!cond) fail++;
            }

            var defs = PetFx;

            // ---- 素材 (IMP-PET-008 症状 4: 1254px / mipmap 無し / alphaIsTransparency 不揃い) ----
            string[] texNames; int[] texSizes;
            ParticleTextures(out texNames, out texSizes);
            for (int i = 0; i < texNames.Length; i++)
                Check(ParticleImportOk(TexPath(texNames[i]), texSizes[i]),
                    texNames[i] + ": mipmap ON / alphaIsTransparency / max <= " + texSizes[i] + " / Clamp");
            for (int i = 0; i < defs.Length; i++)
            {
                if (defs[i].tex.Length == 0) continue;
                var m1 = AssetDatabase.LoadAssetAtPath<Material>(Layer1MatPath(defs[i]));
                Check(m1 != null && m1.mainTexture != null && m1.mainTexture.name == Layer1Tex(defs[i]),
                    defs[i].label + ": 材質が " + Layer1Tex(defs[i]) + " を指す (1 枚絵のままではない)");
                if (defs[i].tex2.Length > 0)
                {
                    var m2 = AssetDatabase.LoadAssetAtPath<Material>(Layer2MatPath(defs[i]));
                    Check(m2 != null && m2.mainTexture != null && m2.mainTexture.name == defs[i].tex2,
                        defs[i].label + ": 2 層目の材質が " + defs[i].tex2 + " を指す");
                }
            }

            foreach (var def in KD_DogDefs.All)
            {
                string tag = "[" + def.key + "] ";
                int found;
                var petGo = KD_SceneObjects.Find(def.PetSystemName, out found);
                var pet = petGo == null ? null : petGo.GetComponent<KD_DogPetController>();
                Check(pet != null, tag + "controller");
                if (pet == null) continue;

                Check(pet.fxVariants != null && pet.fxVariants.Length == defs.Length,
                    tag + $"fxVariants が {defs.Length} 個 (actual "
                    + (pet.fxVariants == null ? "null" : pet.fxVariants.Length.ToString()) + ")");
                Check(pet.fxVariants2 != null && pet.fxVariants2.Length == defs.Length,
                    tag + "fxVariants2 が同数");
                Check(pet.fxProductKeys != null && pet.fxProductKeys.Length == defs.Length,
                    tag + "fxProductKeys が同数");
                Check(pet.fxAccentRateMultipliers != null && pet.fxAccentRateMultipliers.Length == defs.Length,
                    tag + "fxAccentRateMultipliers が同数");
                float[] backingRates = null;
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(pet);
                Check(backing != null && backing.publicVariables.TryGetVariableValue("fxAccentRateMultipliers", out backingRates)
                    && backingRates != null && backingRates.Length == defs.Length
                    && Approx(backingRates[0], 0.5f) && Approx(backingRates[4], 2f) && Approx(backingRates[5], 2f),
                    tag + "実行用Udonにも無料0.5/複合2の倍率を反映");
                if (pet.fxVariants == null || pet.fxVariants2 == null || pet.fxProductKeys == null
                    || pet.fxVariants.Length != defs.Length || pet.fxVariants2.Length != defs.Length) continue;

                for (int i = 0; i < defs.Length; i++)
                {
                    Check(backingRates != null && i < backingRates.Length && Approx(backingRates[i], defs[i].accentRateMultiplier),
                        tag + defs[i].label + ": 実行用Udonの飾り倍率も一致");
                    Check(pet.fxProductKeys[i] == defs[i].productKey,
                        tag + $"[{i}] productKey == {defs[i].productKey}");
                    Check(pet.fxAccentRateMultipliers != null && i < pet.fxAccentRateMultipliers.Length
                        && Approx(pet.fxAccentRateMultipliers[i], defs[i].accentRateMultiplier),
                        tag + defs[i].label + ": 飾り層の発生量倍率 == " + defs[i].accentRateMultiplier);
                    var ps = pet.fxVariants[i];
                    Check(ps != null, tag + defs[i].label + ": ParticleSystem がある");
                    if (ps == null) continue;
                    // **うるささの規律。** ♡ から横展開する (TRT-001 設計 5)
                    Check(ps.main.maxParticles <= 30,
                        tag + defs[i].label + $": maxParticles <= 30 (actual {ps.main.maxParticles}"
                        + " — うるささの敵)");
                    Check(ps.emission.rateOverTime.constant <= 16f,
                        tag + defs[i].label + $": emission rate <= 16/s (actual {ps.emission.rateOverTime.constant})");
                    Check(ps.transform.parent == petGo.transform,
                        tag + defs[i].label + ": この犬の PetSystem 配下");
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    Check(r != null && r.sharedMaterial != null,
                        tag + defs[i].label + ": 材質がある");
                    // **既定で粒を出していないこと** — 複製元が Play 中だと出しっぱなしになる
                    Check(!ps.isPlaying, tag + defs[i].label + ": 既定で再生していない");

                    if (defs[i].tex.Length == 0) continue;   // 無料枠のレシピは検査しない (手調整の聖域)

                    // ---- 有料枠: レシピどおりか (IMP-PET-008) ----
                    var rc = defs[i].layer1;
                    Check(Approx(ps.main.startSize.constantMin, rc.sizeMin) && Approx(ps.main.startSize.constantMax, rc.sizeMax),
                        tag + defs[i].label + $": 大きさがレシピどおり ({rc.sizeMin}〜{rc.sizeMax})");
                    Check(Approx(ps.main.gravityModifier.constant, rc.gravity),
                        tag + defs[i].label + $": 重力がレシピどおり ({rc.gravity})");
                    Check(Approx(ps.main.startLifetime.constantMin, rc.lifeMin) && Approx(ps.main.startLifetime.constantMax, rc.lifeMax),
                        tag + defs[i].label + $": 寿命がレシピどおり ({rc.lifeMin}〜{rc.lifeMax})");
                    var sc = ps.main.startColor.color;
                    Check(Approx(sc.r, rc.tint.r) && Approx(sc.g, rc.tint.g) && Approx(sc.b, rc.tint.b),
                        tag + defs[i].label + ": startColor がレシピの色 (複製元のピンクが乗っていない)");
                    Check(r != null && r.sharedMaterial == AssetDatabase.LoadAssetAtPath<Material>(Layer1MatPath(defs[i])),
                        tag + defs[i].label + ": 材質が M_KD_PetFx_" + defs[i].key);
                    Check(ps.colorOverLifetime.enabled, tag + defs[i].label + ": 寿命で透けて消える");

                    var ps2 = pet.fxVariants2[i];
                    if (defs[i].tex2.Length > 0)
                    {
                        Check(ps2 != null, tag + defs[i].label + ": 独立した2層目がある");
                        if (ps2 == null) continue;
                        var rc2 = defs[i].layer2;
                        Check(ps2.transform.parent == petGo.transform, tag + defs[i].label + ": 2 層目がこの犬の PetSystem 配下");
                        Check(ps2.main.maxParticles <= 30, tag + defs[i].label + ": 2 層目 maxParticles <= 30");
                        Check(ps2.emission.rateOverTime.constant <= 16f, tag + defs[i].label + ": 2 層目 rate <= 16/s");
                        Check(!ps2.isPlaying, tag + defs[i].label + ": 2 層目が既定で再生していない");
                        Check(Approx(ps2.main.startSize.constantMin, rc2.sizeMin) && Approx(ps2.main.startSize.constantMax, rc2.sizeMax),
                            tag + defs[i].label + $": 2 層目の大きさがレシピどおり ({rc2.sizeMin}〜{rc2.sizeMax})");
                        Check(Approx(ps2.main.startLifetime.constantMin, rc2.lifeMin) && Approx(ps2.main.startLifetime.constantMax, rc2.lifeMax),
                            tag + defs[i].label + $": 2 層目の寿命がレシピどおり ({rc2.lifeMin}〜{rc2.lifeMax})");
                        var sc2 = ps2.main.startColor.color;
                        Check(Approx(sc2.r, rc2.tint.r) && Approx(sc2.g, rc2.tint.g) && Approx(sc2.b, rc2.tint.b),
                            tag + defs[i].label + ": 2 層目の startColor がレシピの色");
                        var r2 = ps2.GetComponent<ParticleSystemRenderer>();
                        Check(r2 != null && r2.sharedMaterial == AssetDatabase.LoadAssetAtPath<Material>(Layer2MatPath(defs[i])),
                            tag + defs[i].label + ": 2 層目の材質が M_KD_PetFx_" + defs[i].key + "_L2");
                    }
                    else
                    {
                        Check(ps2 == null, tag + defs[i].label + ": 1 層 (fxVariants2 が null)");
                        // レシピから 2 層目を外したのにノードだけ残る事故 (配線の無い PS が黙って残る)
                        Check(petGo.transform.Find(PetFxPrefix + defs[i].key + "2") == null,
                            tag + defs[i].label + ": 使わない 2 層目ノードが残っていない");
                    }
                }

                // **無料枠は既存の ♡ そのものであること** (非破壊の担保)
                var baseFx = petGo.transform.Find(FxName);
                Check(baseFx != null && pet.fxVariants[0] == baseFx.GetComponent<ParticleSystem>(),
                    tag + "無料枠が既存の " + FxName + " そのもの (手調整を壊していない)");
            }

            // 恒久 ID の重複 (カテゴリ横断で一意)
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var d in defs)
                Check(seen.Add(d.productKey), $"productKey {d.productKey} が重複していない");

            log.AppendLine(fail == 0 ? "VERIFY RESULT: PASS" : $"VERIFY RESULT: FAIL ({fail} checks)");
            Finish("Logs/imp-pet-006-verify.txt", log);
        }
    }
}
