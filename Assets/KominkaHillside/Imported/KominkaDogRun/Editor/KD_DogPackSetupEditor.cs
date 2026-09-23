using System.IO;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-DOG-008: 犬の名簿 (KD_DogPack) のセットアップと検証。
    ///
    /// 実行順 (別実行にする — Program Asset 直後の CopyProxyToUdon は outdated error):
    ///   1 - Create Program Asset → force refresh
    ///   2 - Setup Dog Pack
    ///   3 - Verify Dog Pack
    ///
    /// 前提: Fetch System と Wander System が全犬種で構築済み。
    /// 体の半径は <see cref="KD_DogRig.bodyScale"/> (体幹長) から**導出する** —
    /// 犬定義には書かない (実測できるものは書かない: IMP-DOG-005 の方針)。
    ///
    /// IMP-DOG-009: じゃれ合いも同じ名簿に乗る (実装は KD_DogWanderController)。
    /// **犬をまたぐ配線はこのメニューに集約する** — 名簿・番号・じゃれ合いの調整値。
    /// 調整値は Primary の犬を正本として他へ複製し、Verify が一致を機械検査する
    /// (1 匹だけずれると「片方しか遊びに来ない」という Play で読めない壊れ方をするため)。
    /// </summary>
    public static class KD_DogPackSetupEditor
    {
        const string ScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogPack.cs";
        const string ProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogPack.asset";
        const string PackName = KD_DogDefs.DogPackName;   // IMP-INF-003: 正本は KD_DogDefs

        /// <summary>体の半径 = 体幹長 × これ。0.5 = 頭〜尻尾根元の半分を「personal space の半径」とみなす。
        /// コーギー 0.204 / ポメ 0.115 / ラブ 0.333 → コーギー + ラブ で 0.54m の間合い
        /// (Issue の実測「ラブとコーギーが並ぶと最低でも 0.5m 程度」と一致する)。</summary>
        const float BodyRadiusRatio = 0.5f;

        /// <summary>逃げ先で立ち止まって見せる間 (秒)。**ここが追いかけっこのリズム** —
        /// 逃げ役が着いて一瞬止まる → 同速の追い役が追い付く → また逃げる。
        /// 0 にすると 2 匹が一定距離で並走し続けるだけになる。</summary>
        const float FleePauseSec = 0.35f;

        /// <summary>じゃれ合いの「予定表」— **全犬で一致していなければ成立しない**フィールド。
        /// 逃げ方 (fleeHopPeriod / playFleeInnerRatio) は逃げる犬しか使わないので入れない。</summary>
        static readonly string[] ScheduleParams =
        {
            "playCyclePeriod", "playDurationMin", "playDurationMax",
            "playFaceChance", "roleSwapPeriod", "playBreakOff", "playContactSpacing",
            // IMP-DOG-012: 参加集合の導出に効くので一致が要る (float のみ。int は下で別に見る)
            "playJoinWindow",
        };

        /// <summary>予定表のうち int のもの。<see cref="ScheduleParams"/> と同じ理由で
        /// 全犬一致が要るが、backing からの引き方が float と違うので分けてある。</summary>
        static readonly string[] ScheduleParamsInt = { "playMaxDogs" };

        /// <summary>backing UdonBehaviour (= 実行時に読まれる側) から float を引く。
        /// proxy ではなくこちらを見るのが独立チャネル検証 (vrc-verification)。</summary>
        static bool TryGetBackingFloat(VRC.Udon.UdonBehaviour[] ubs, string name, out float value)
        {
            value = 0f;
            if (ubs == null || ubs.Length != 1 || ubs[0].publicVariables == null) return false;
            object raw;
            if (!ubs[0].publicVariables.TryGetVariableValue(name, out raw)) return false;
            if (!(raw is float)) return false;
            value = (float)raw;
            return true;
        }

        /// <summary>backing UdonBehaviour から int を引く (<see cref="TryGetBackingFloat"/> の int 版)。</summary>
        static bool TryGetBackingInt(VRC.Udon.UdonBehaviour[] ubs, string name, out int value)
        {
            value = 0;
            if (ubs == null || ubs.Length != 1 || ubs[0].publicVariables == null) return false;
            object raw;
            if (!ubs[0].publicVariables.TryGetVariableValue(name, out raw)) return false;
            if (!(raw is int)) return false;
            value = (int)raw;
            return true;
        }

        /// <summary>じゃれ合いの調整値の正本になる犬 (<see cref="KD_DogDefs.Primary"/>) の番号。</summary>
        static int PrimaryIndex(KD_DogDef[] defs)
        {
            for (int i = 0; i < defs.Length; i++)
                if (defs[i].key == KD_DogDefs.Primary.key) return i;
            return 0;
        }

        /// <summary>連続する逃げ先の**最長**の弦 (m)。半径 R の円上で角度 θ 離れた 2 点の距離は 2R·sin(θ/2)。
        ///
        /// **角度は固定ではない** — 実装は黄金角 `FleeTurnRad` に ±`FleeJitterRad`/2 の
        /// 揺らぎを足すので、連続ホップ間の角度差は最大 `FleeTurnRad + FleeJitterRad` になる
        /// (Codex 指摘。固定角で見積もると弦を過小評価し、**Verify が通るのに走り切れない**)。
        /// 半径も毎ホップ変わるが、最悪は両方が外周 (= areaRadius) のときなのでそれで見る。
        ///
        /// **定数は KD_DogWanderController の public const をそのまま読む** —
        /// ここに数値を書き写すと、片方だけ直したときに気づけないずれになる。</summary>
        static float FleeChord(KD_DogWanderController w)
        {
            float theta = KD_DogWanderController.FleeTurnRad + KD_DogWanderController.FleeJitterRad;
            if (theta > Mathf.PI) theta = Mathf.PI; // π を超えると弦は縮む。最悪は直径
            return 2f * w.areaRadius * Mathf.Sin(theta * 0.5f);
        }

        /// <summary>最長の弦を加減速込みで走り切る時間 (秒)。
        /// v=√(2ad) の減速で入るので、加速 v²/2a・減速 v²/2d の距離と時間を素直に足す。</summary>
        static float FleeDashSeconds(KD_DogWanderController w)
        {
            float v = Mathf.Max(w.runSpeed, 0.01f);
            float a = Mathf.Max(w.acceleration, 0.01f);
            float d = Mathf.Max(w.deceleration, 0.01f);
            float da = v * v / (2f * a);
            float dd = v * v / (2f * d);
            float cruise = FleeChord(w) - da - dd;
            if (cruise < 0f) cruise = 0f;
            return v / a + v / d + cruise / v;
        }

        /// <summary>その犬の庭を 1 ダッシュで渡り切り、少し止まって見せられる周期 (秒)。
        /// **導出値。Inspector で手調整しない** (庭を広げたら Setup 再実行で追従する)。</summary>
        static float FleeHopPeriodFor(KD_DogWanderController w)
        {
            return Mathf.Ceil((FleeDashSeconds(w) + FleePauseSec) * 10f) / 10f;
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Pack/1 - Create Program Asset")]
        public static void CreateProgramAsset()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogPackSetup] Create program asset");
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath) != null)
            {
                log.AppendLine("already exists: " + ProgramPath);
            }
            else
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptPath);
                if (script == null)
                {
                    log.AppendLine("FAIL script not found: " + ScriptPath);
                    Finish("Logs/imp-dog-008-program.txt", log);
                    return;
                }
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = script;
                AssetDatabase.CreateAsset(asset, ProgramPath);
                AssetDatabase.SaveAssets();
                log.AppendLine("created: " + ProgramPath);
            }
            log.AppendLine("RESULT: PASS (force refresh してから Setup を実行する)");
            Finish("Logs/imp-dog-008-program.txt", log);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Pack/2 - Setup Dog Pack")]
        public static void Setup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogPackSetup] Setup");

            var defs = KD_DogDefs.All;
            int n = defs.Length;
            var roots = new Transform[n];
            var radius = new float[n];
            var fetches = new KD_DogFetchController[n];
            var wanders = new KD_DogWanderController[n];
            bool ok = true;

            for (int i = 0; i < n; i++)
            {
                var def = defs[i];
                log.AppendLine("-- [" + i + "] " + def.displayName + " (" + def.key + ")");

                int dogCount, fetchCount, wanderCount;
                var dog = KD_SceneObjects.Find(def.DogName, out dogCount);
                var fetchGo = KD_SceneObjects.Find(def.FetchSystemName, out fetchCount);
                var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out wanderCount);
                if (dogCount != 1 || fetchCount != 1)
                {
                    log.AppendLine($"   FAIL {def.DogName}={dogCount} / {def.FetchSystemName}={fetchCount} (それぞれ 1 個が前提。Fetch System を先に構築する)");
                    ok = false;
                    continue;
                }
                // IMP-DOG-009: じゃれ合いは徘徊コントローラに載っているので、名簿はそこへも配る
                if (wanderCount != 1)
                {
                    log.AppendLine($"   FAIL {def.WanderSystemName}={wanderCount} (1 個が前提。Wander System を先に構築する)");
                    ok = false;
                    continue;
                }
                var fetch = fetchGo.GetUdonSharpComponent<KD_DogFetchController>();
                if (fetch == null)
                {
                    log.AppendLine("   FAIL KD_DogFetchController が無い");
                    ok = false;
                    continue;
                }
                var wander = wanderGo.GetUdonSharpComponent<KD_DogWanderController>();
                if (wander == null)
                {
                    log.AppendLine("   FAIL KD_DogWanderController が無い");
                    ok = false;
                    continue;
                }

                // 体格は犬ローカルで測られているので、犬をスケールしている場合は世界寸法へ直す
                var rig = KD_DogRig.Resolve(dog);
                if (rig.bodyScale < 0.001f)
                {
                    log.AppendLine("   FAIL bodyScale が取れない: " + string.Join(" / ", rig.problems));
                    ok = false;
                    continue;
                }
                Vector3 ls = dog.transform.lossyScale;
                float scale = (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f;
                if (scale < 0.001f) scale = 1f;

                roots[i] = dog.transform;
                radius[i] = rig.bodyScale * scale * BodyRadiusRatio;
                fetches[i] = fetch;
                wanders[i] = wander;
                log.AppendLine($"   bodyScale={rig.bodyScale:F4} lossyScale={scale:F3} → radius={radius[i]:F4} m");
            }

            if (!ok)
            {
                log.AppendLine("SETUP RESULT: FAIL (犬の解決に失敗。上の FAIL を潰してから再実行)");
                Finish("Logs/imp-dog-008-setup.txt", log);
                return;
            }

            // 間合いの実測値をログに残す (Play で「重なる」と言われたときの出発点になる)。
            // nose = 対面で鼻を突き合わせる間合い (IMP-DOG-009)。gap より広くないと押し出しと綱引きになる
            float contactSpacing = wanders[PrimaryIndex(defs)].playContactSpacing;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    log.AppendLine($"gap {defs[i].key}+{defs[j].key} = {(radius[i] + radius[j]):F3} m" +
                        $" / nose = {((radius[i] + radius[j]) * contactSpacing):F3} m");

            int found;
            var packGo = KD_SceneObjects.Find(PackName, out found);
            if (found > 1)
            {
                log.AppendLine($"FAIL '{PackName}' がシーン内に {found} 個ある。1 個に整理してから再実行");
                Finish("Logs/imp-dog-008-setup.txt", log);
                return;
            }
            if (packGo == null)
            {
                packGo = new GameObject(PackName);
                KD_SceneObjects.PlaceIfNew(packGo, KD_DogDefs.SystemsPath);   // IMP-INF-003
                log.AppendLine("created " + PackName);
            }

            var pack = packGo.GetUdonSharpComponent<KD_DogPack>();
            if (pack == null)
            {
                pack = packGo.AddUdonSharpComponent<KD_DogPack>();
                log.AppendLine("added KD_DogPack");
            }

            // 上書きするのは名簿と接地だけ。間合いの手調整 (spacing / separationSpeed) は保持する
            pack.dogRoots = roots;
            pack.dogRadius = radius;
            pack.dogFetch = fetches;   // IMP-DOG-009: 「相手は今 Fetch 中か」の引き先
            pack.dogWander = wanders;  // 同上「相手はプレイヤーが近くて止まっていないか」
            pack.groundLayerMask = fetches[0].groundLayerMask;
            pack.groundRayOffset = fetches[0].groundRayOffset;
            pack.groundRayDistance = fetches[0].groundRayDistance;
            bool copyOk = TryCopy(pack, log);

            // 各犬の Fetch へ名簿と自分の番号を配線する (帰還の「扇」がこれを読む)
            for (int i = 0; i < n; i++)
            {
                fetches[i].pack = pack;
                fetches[i].dogIndex = i;
                copyOk &= TryCopy(fetches[i], log);
                log.AppendLine($"wired {defs[i].FetchSystemName}.dogIndex = {i}");
            }

            // IMP-DOG-009: 徘徊へも名簿を配り、じゃれ合いの調整値をそろえる。
            //
            // **調整値は 2 種類ある。混ぜないこと。**
            //
            // (A) 予定表 — 一致が成立条件。**Primary から複製する。**
            //     いつ・どの型で・どちらの役か・どこで鼻を突き合わせるかは
            //     2 匹が同じ値から同じ結論を出さないと成り立たない。1 匹だけ Inspector で
            //     触られると「片方しか遊びに来ない」という Play では読めない壊れ方をする。
            //     調整したいときは Primary (コーギー) を触ってここを再実行する
            //
            // (B) 逃げ方 — **逃げる犬だけが使う**ので一致は要らない。
            //     fleeHopPeriod は「その犬の庭を 1 ダッシュで渡り切る時間」なので、
            //     areaRadius と runSpeed から**導出する** (実測できるものは書かない —
            //     IMP-DOG-005 の方針)。庭を広げたらここを再実行すれば追従する
            int primaryIdx = PrimaryIndex(defs);
            var src = wanders[primaryIdx];
            log.AppendLine($"play params (A: 予定表) 正本 = {defs[primaryIdx].WanderSystemName}" +
                $" (cycle={src.playCyclePeriod} dur={src.playDurationMin}-{src.playDurationMax}" +
                $" face={src.playFaceChance} swap={src.roleSwapPeriod}" +
                $" breakOff={src.playBreakOff} contact={src.playContactSpacing})");
            for (int i = 0; i < n; i++)
            {
                var w = wanders[i];
                w.pack = pack;
                w.dogIndex = i;
                if (i != primaryIdx)
                {
                    w.playCyclePeriod = src.playCyclePeriod;
                    w.playDurationMin = src.playDurationMin;
                    w.playDurationMax = src.playDurationMax;
                    w.playFaceChance = src.playFaceChance;
                    w.roleSwapPeriod = src.roleSwapPeriod;
                    w.playBreakOff = src.playBreakOff;
                    w.playContactSpacing = src.playContactSpacing;
                    // IMP-DOG-012: 参加人数と途中参加も予定表 (A)。
                    // **1 匹だけ値が違うと参加する集合の結論が割れる** = 片方だけ来る
                    w.playMaxDogs = src.playMaxDogs;
                    w.playJoinWindow = src.playJoinWindow;
                }
                // 逃げ方の好み。一致は要らないが 4 匹の遊び方をそろえたいので導出値にする。
                // IMP-DOG-014 の Play フィードバック (2026-08-23):
                // **Primary のコピーではなく導出値にした。** fleeHopPeriod と同じ扱いで、
                // 定数を 1 行変えて Setup を流せば 4 匹に効く。
                // 逃げ先を外周寄りに寄せるほど平均の弦が最長の弦へ近づき、
                // 「早く着いて残り時間ずっと立っている」が減る。
                // **⚠ 以前は `i != primaryIdx` の中に居た** (破壊的監査 v2 RC-18 —
                // 導出値化がコーギーにだけ効かず、1 匹だけ旧値 0.55 のままだった)。
                // fleeHopPeriod と同じく if の外 = 全犬に適用する
                w.playFleeInnerRatio = KD_DogWanderController.DefaultPlayFleeInnerRatioPublic;
                w.fleeHopPeriod = FleeHopPeriodFor(w);
                copyOk &= TryCopy(w, log);
                log.AppendLine($"wired {defs[i].WanderSystemName}.dogIndex = {i}"
                    + (i == primaryIdx ? " (A 正本)" : " (A 複製)")
                    + $" / fleeHopPeriod={w.fleeHopPeriod:F1}s ← areaRadius {w.areaRadius:F1}m"
                    + $" 弦 {FleeChord(w):F2}m を runSpeed {w.runSpeed} で渡る");
            }

            // 鼻を突き合わせる間合いが押し出しの間合いより狭いと、対面のたびに
            // 「近寄る ⇄ 押し出される」の綱引きになる (実装ではなく値の事故)
            if (src.playContactSpacing <= pack.spacing)
            {
                log.AppendLine($"WARN playContactSpacing ({src.playContactSpacing}) <= KD_DogPack.spacing ({pack.spacing})"
                    + " — 対面で押し出しと綱引きになる。Primary の犬で上げる");
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("dogs=" + n);
            log.AppendLine(copyOk
                ? "SETUP RESULT: PASS (scene not saved yet)"
                : "SETUP RESULT: FAIL (force refresh 後に再実行)");
            Finish("Logs/imp-dog-008-setup.txt", log);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Pack/3 - Verify Dog Pack")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogPackSetup] Verify");
            int fail = 0;
            void Check(bool cond, string what)
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + what);
                if (!cond) fail++;
            }

            var pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath);
            var expectedScript = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptPath);
            Check(pa != null && pa.sourceCsScript != null && pa.sourceCsScript == expectedScript,
                "program asset points at KD_DogPack.cs");

            int found;
            var packGo = KD_SceneObjects.Find(PackName, out found);
            Check(found == 1, PackName + " exists exactly once (actual " + found + ")");
            if (packGo == null)
            {
                FinishVerify(log, fail + 1);
                return;
            }
            Check(!PrefabUtility.IsPartOfPrefabInstance(packGo), PackName + " is a plain scene object");

            var ubs = packGo.GetComponents<VRC.Udon.UdonBehaviour>();
            Check(ubs.Length == 1 && ubs[0].programSource != null && ubs[0].programSource.name == "KD_DogPack",
                "exactly 1 UdonBehaviour with programSource KD_DogPack");

            var pack = packGo.GetUdonSharpComponent<KD_DogPack>();
            Check(pack != null, "proxy component exists");
            if (pack == null)
            {
                FinishVerify(log, fail + 1);
                return;
            }

            var defs = KD_DogDefs.All;
            int n = defs.Length;
            Check(pack.dogRoots != null && pack.dogRoots.Length == n,
                $"dogRoots.Length == {n} (actual {(pack.dogRoots == null ? "null" : pack.dogRoots.Length.ToString())})");
            Check(pack.dogRadius != null && pack.dogRadius.Length == n,
                $"dogRadius.Length == {n} (actual {(pack.dogRadius == null ? "null" : pack.dogRadius.Length.ToString())})");
            // IMP-DOG-009: 欠けていると じゃれ合いだけが黙って消える (徘徊は動くので気づきにくい)
            Check(pack.dogFetch != null && pack.dogFetch.Length == n,
                $"dogFetch.Length == {n} (actual {(pack.dogFetch == null ? "null" : pack.dogFetch.Length.ToString())})");
            Check(pack.dogWander != null && pack.dogWander.Length == n,
                $"dogWander.Length == {n} (actual {(pack.dogWander == null ? "null" : pack.dogWander.Length.ToString())})");
            if (pack.dogRoots == null || pack.dogRadius == null
                || pack.dogRoots.Length != n || pack.dogRadius.Length != n)
            {
                FinishVerify(log, fail);
                return;
            }

            // 押し出しが効く前提。0 や負だと無言で「重ならない」機能ごと消える
            Check(pack.spacing > 0.01f, $"spacing > 0 (actual {pack.spacing})");
            Check(pack.separationSpeed > 0.01f, $"separationSpeed > 0 (actual {pack.separationSpeed})");
            Check(pack.plantedYieldScale > 0.001f && pack.plantedYieldScale < 1f,
                $"0 < plantedYieldScale < 1 (actual {pack.plantedYieldScale}) — 0 だと 2 匹とも止まったまま重なりが解けない");
            Check(pack.movingThreshold > 0f, $"movingThreshold > 0 (actual {pack.movingThreshold})");
            // IMP-DOG-009: じゃれ合いの調整値の正本 (Primary の犬)。全犬がこれと一致すること
            int primaryCount;
            var primaryGo = KD_SceneObjects.Find(defs[PrimaryIndex(defs)].WanderSystemName, out primaryCount);
            var primary = primaryGo == null ? null : primaryGo.GetUdonSharpComponent<KD_DogWanderController>();
            var primaryUbs = primaryGo == null ? null : primaryGo.GetComponents<VRC.Udon.UdonBehaviour>();
            Check(primary != null, "Primary の KD_DogWanderController が引ける (じゃれ合いの調整値の正本)");
            Check(pack.teleportThreshold > pack.separationSpeed * 0.1f,
                "teleportThreshold が押し出し 1 フレーム分より十分大きい");
            Check(pack.enabled, "proxy enabled");

            for (int i = 0; i < n; i++)
            {
                var def = defs[i];
                log.AppendLine("-- [" + i + "] " + def.displayName + " (" + def.key + ")");
                string tag = def.key + ": ";

                int dogCount, fetchCount;
                var dog = KD_SceneObjects.Find(def.DogName, out dogCount);
                Check(dogCount == 1 && dog != null, tag + def.DogName + " exists exactly once");
                Check(dog != null && pack.dogRoots[i] == dog.transform,
                    tag + $"dogRoots[{i}] が {def.DogName} を指している");

                // 半径は体幹長から導出する。犬定義に書かないので、ずれていたら Setup 未実行
                if (dog != null)
                {
                    var rig = KD_DogRig.Resolve(dog);
                    Vector3 ls = dog.transform.lossyScale;
                    float scale = (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f;
                    if (scale < 0.001f) scale = 1f;
                    float expected = rig.bodyScale * scale * BodyRadiusRatio;
                    Check(rig.bodyScale > 0.001f, tag + "bodyScale が測れる");
                    Check(Mathf.Abs(pack.dogRadius[i] - expected) < 0.005f,
                        tag + $"dogRadius[{i}] == bodyScale × {BodyRadiusRatio} (actual {pack.dogRadius[i]:F4} / expected {expected:F4})");
                }

                var fetchGo = KD_SceneObjects.Find(def.FetchSystemName, out fetchCount);
                Check(fetchCount == 1 && fetchGo != null, tag + def.FetchSystemName + " exists exactly once");
                if (fetchGo == null) continue;
                var fetch = fetchGo.GetUdonSharpComponent<KD_DogFetchController>();
                Check(fetch != null, tag + "KD_DogFetchController exists");
                if (fetch == null) continue;

                Check(fetch.pack == pack, tag + "fetch.pack wired to " + PackName);
                Check(fetch.dogIndex == i, tag + $"fetch.dogIndex == {i} (actual {fetch.dogIndex})");
                // 別の犬の Fetch を掴んでいないか (多頭で最も起きやすい取り違え)
                Check(dog != null && fetch.dogRoot == dog.transform,
                    tag + "fetch が同じ犬を指している");
                Check(fetch.returnSpread > 0.5f, tag + $"returnSpread > 0.5 (actual {fetch.returnSpread})");
                Check(fetch.slotNeighborRange > fetch.returnStopDistance,
                    tag + $"slotNeighborRange ({fetch.slotNeighborRange}) > returnStopDistance ({fetch.returnStopDistance})");
                Check(fetch.returnSettleTimeout > 0.5f,
                    tag + $"returnSettleTimeout > 0.5s (actual {fetch.returnSettleTimeout})");

                // **proxy ではなく backing UdonBehaviour を直接見る** (vrc-verification: 独立チャネル)。
                // dogIndex は参照でなく値なので、CopyProxyToUdon が漏れても proxy 側だけ正しく見え、
                // 実行時は既定の -1 に落ちて「扇が無効 = 全員が同じ点へ帰る」に戻る。
                // 症状が旧挙動そのものなので Play では気づきにくい — ここで機械的に落とす
                var fetchUbs = fetchGo.GetComponents<VRC.Udon.UdonBehaviour>();
                if (fetchUbs.Length == 1 && fetchUbs[0].publicVariables != null)
                {
                    object raw;
                    bool got = fetchUbs[0].publicVariables.TryGetVariableValue("dogIndex", out raw);
                    Check(got, tag + "backing UdonBehaviour に dogIndex がある");
                    Check(got && raw is int && (int)raw == i,
                        tag + $"backing dogIndex == {i} (actual {(got ? raw : "missing")})");
                }

                // 接地は Fetch を正本にそろえる (別値だと押し出した先で段差の扱いが割れる)
                Check(pack.groundLayerMask == fetch.groundLayerMask
                    && Mathf.Approximately(pack.groundRayOffset, fetch.groundRayOffset)
                    && Mathf.Approximately(pack.groundRayDistance, fetch.groundRayDistance),
                    tag + "pack の接地パラメータが Fetch と一致");

                // 名簿は「その犬の Fetch」を指していること (取り違えると別の犬の忙しさを見る)
                Check(pack.dogFetch != null && pack.dogFetch.Length == n && pack.dogFetch[i] == fetch,
                    tag + $"dogFetch[{i}] が {def.FetchSystemName} を指している");

                // ---- IMP-DOG-009: じゃれ合い ----
                int wanderCount;
                var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out wanderCount);
                Check(wanderCount == 1 && wanderGo != null, tag + def.WanderSystemName + " exists exactly once");
                if (wanderGo == null) continue;
                var wander = wanderGo.GetUdonSharpComponent<KD_DogWanderController>();
                Check(wander != null, tag + "KD_DogWanderController exists");
                if (wander == null) continue;

                Check(pack.dogWander != null && pack.dogWander.Length == n && pack.dogWander[i] == wander,
                    tag + $"dogWander[{i}] が {def.WanderSystemName} を指している");
                Check(wander.pack == pack, tag + "wander.pack wired to " + PackName);
                Check(wander.dogIndex == i, tag + $"wander.dogIndex == {i} (actual {wander.dogIndex})");
                Check(dog != null && wander.dogRoot == dog.transform, tag + "wander が同じ犬を指している");

                // **proxy ではなく backing UdonBehaviour を直接見る** (vrc-verification: 独立チャネル)。
                // dogIndex は参照でなく値なので、CopyProxyToUdon が漏れても proxy 側だけ正しく見え、
                // 実行時は既定の -1 に落ちて **じゃれ合いだけが黙って消える** (徘徊は動く)
                var wanderUbs = wanderGo.GetComponents<VRC.Udon.UdonBehaviour>();
                if (wanderUbs.Length == 1 && wanderUbs[0].publicVariables != null)
                {
                    object raw;
                    bool got = wanderUbs[0].publicVariables.TryGetVariableValue("dogIndex", out raw);
                    Check(got, tag + "backing UdonBehaviour に dogIndex がある (wander)");
                    Check(got && raw is int && (int)raw == i,
                        tag + $"backing wander dogIndex == {i} (actual {(got ? raw : "missing")})");
                }

                // (A) 予定表は「全犬が同じ値から同じ結論を出す」ことが成立条件。
                // **1 匹だけ違うと片方しか遊びに来ない** — Play では「たまに変」としか見えない。
                // fleeHopPeriod / playFleeInnerRatio は逃げる犬しか使わないのでここには入れない
                if (primary != null)
                {
                    Check(Mathf.Approximately(wander.playCyclePeriod, primary.playCyclePeriod)
                        && Mathf.Approximately(wander.playDurationMin, primary.playDurationMin)
                        && Mathf.Approximately(wander.playDurationMax, primary.playDurationMax)
                        && Mathf.Approximately(wander.playFaceChance, primary.playFaceChance)
                        && Mathf.Approximately(wander.roleSwapPeriod, primary.roleSwapPeriod)
                        && Mathf.Approximately(wander.playBreakOff, primary.playBreakOff)
                        && Mathf.Approximately(wander.playContactSpacing, primary.playContactSpacing),
                        tag + "じゃれ合いの予定表 (A) が Primary と一致 (**全犬で同じでなければならない**)");
                }

                // **予定表も backing UdonBehaviour で照合する** (Codex 指摘)。
                // proxy 同士が一致していても CopyProxyToUdon が漏れていれば実行時の値は別物で、
                // そのとき起きるのは「2 匹が別のサイクル・別の組・別の役を導出する」
                // = **Play では「たまに変」としか見えない**壊れ方。dogIndex と同じ理由でここで落とす
                if (primaryUbs != null)
                {
                    bool allOk = true;
                    bool allGot = true;
                    bool allSynced = true;
                    // **「Primary と一致」だけでは穴がある** (破壊的品質監査 RC-14)。
                    // 比べているのが backing 同士なので、**全犬そろって CopyProxyToUdon が
                    // 漏れていると、全員 0 で「一致」して PASS する。**
                    // 実行時は全犬が 0 を読む = playCyclePeriod 0 で 0 除算、
                    // という最悪の状態を検査が承認してしまう。
                    //
                    // 塞ぎ方は **backing を proxy と突き合わせる** こと。
                    // proxy は Setup が Primary から複製した値なので、
                    //   backing[i] == proxy[i] かつ proxy[i] == proxy[Primary]
                    // が言えれば backing[i] == backing[Primary] は自動的に従う。
                    // **「一致」ではなく「正しい値と一致」を見るのが要点。**
                    var proxySo = new SerializedObject(wander);
                    foreach (var name in ScheduleParams)
                    {
                        float mine, theirs;
                        bool gotMine = TryGetBackingFloat(wanderUbs, name, out mine);
                        bool gotPrimary = TryGetBackingFloat(primaryUbs, name, out theirs);
                        if (!gotMine || !gotPrimary) { allGot = false; continue; }

                        var prop = proxySo.FindProperty(name);
                        if (prop == null)
                        {
                            allGot = false;
                            log.AppendLine($"       proxy に {name} が無い (改名した?)");
                            continue;
                        }
                        if (!Mathf.Approximately(mine, prop.floatValue))
                        {
                            allSynced = false;
                            log.AppendLine($"       backing {name}: {mine} != proxy {prop.floatValue}" +
                                " — CopyProxyToUdon 漏れ。Setup を流し直すこと");
                        }

                        if (Mathf.Approximately(mine, theirs)) continue;
                        allOk = false;
                        log.AppendLine($"       backing {name}: {mine} != Primary {theirs}");
                    }
                    // int の予定表も同じ 3 点 (存在 / proxy 一致 / Primary 一致) を見る
                    foreach (var name in ScheduleParamsInt)
                    {
                        int mine, theirs;
                        bool gotMine = TryGetBackingInt(wanderUbs, name, out mine);
                        bool gotPrimary = TryGetBackingInt(primaryUbs, name, out theirs);
                        if (!gotMine || !gotPrimary)
                        {
                            allGot = false;
                            log.AppendLine($"       backing に {name} が無い" +
                                " — Dog Pack Setup を流し直すこと (後から足したフィールド)");
                            continue;
                        }
                        var iprop = proxySo.FindProperty(name);
                        if (iprop == null) { allGot = false; continue; }
                        if (mine != iprop.intValue)
                        {
                            allSynced = false;
                            log.AppendLine($"       backing {name}: {mine} != proxy {iprop.intValue}" +
                                " — CopyProxyToUdon 漏れ");
                        }
                        if (mine == theirs) continue;
                        allOk = false;
                        log.AppendLine($"       backing {name}: {mine} != Primary {theirs}");
                    }

                    Check(allGot, tag + "backing UdonBehaviour に予定表のフィールドが全部ある");
                    Check(allSynced, tag + "backing が proxy と一致 " +
                        "(**全犬まとめて 0 なら『Primary と一致』は素通りする** — RC-14)");
                    Check(allOk, tag + "backing の予定表が Primary と一致 (**実行時に読まれるのはこちら**)");
                }

                // 予定表そのものが成立するか
                Check(wander.playCyclePeriod > wander.playDurationMax + wander.playBreakOff,
                    tag + $"playCyclePeriod ({wander.playCyclePeriod}) > playDurationMax + playBreakOff " +
                    $"({wander.playDurationMax + wander.playBreakOff})");
                Check(wander.playDurationMax >= wander.playDurationMin && wander.playDurationMin > 0f,
                    tag + "playDuration range valid (0 < min <= max)");
                Check(wander.roleSwapPeriod > 1f && wander.fleeHopPeriod > 0.5f,
                    tag + $"roleSwapPeriod ({wander.roleSwapPeriod}) > 1s / fleeHopPeriod ({wander.fleeHopPeriod}) > 0.5s");
                Check(wander.playBreakOff > 0.1f,
                    tag + $"playBreakOff > 0.1s (actual {wander.playBreakOff}) — 0 だと対面のまま固まって窓が終わる");
                Check(wander.playFaceChance >= 0f && wander.playFaceChance <= 1f,
                    tag + $"0 <= playFaceChance <= 1 (actual {wander.playFaceChance})");
                Check(wander.playFleeInnerRatio >= 0f && wander.playFleeInnerRatio < 1f,
                    tag + $"0 <= playFleeInnerRatio < 1 (actual {wander.playFleeInnerRatio})");

                // 鼻を突き合わせる間合いが押し出しの間合いより広いこと。
                // 逆だと対面のたびに「近寄る ⇄ 押し出される」の綱引きになる
                Check(wander.playContactSpacing > pack.spacing,
                    tag + $"playContactSpacing ({wander.playContactSpacing}) > KD_DogPack.spacing ({pack.spacing})");

                // (B) 逃げの 1 ダッシュを走り切れるか。切れないと「逃げ先へ着いて一瞬止まる →
                // 同速の追い役に追い付かれる」というじゃれ合いのリズムが出ず、
                // 2 匹が一定距離で並走し続けるだけになる。
                // **導出値なので通常は Setup 済み = 自動で通る** — ここが赤いのは
                // 手で下げたか、庭を広げたあと Setup を再実行していないかのどちらか
                float dashTime = FleeDashSeconds(wander);
                Check(wander.fleeHopPeriod >= dashTime,
                    tag + $"fleeHopPeriod ({wander.fleeHopPeriod}s) >= 逃げの 1 ダッシュ ({dashTime:F2}s)。" +
                    "赤いときは Dog Pack Setup を再実行する (導出値)");
                log.AppendLine($"       {def.key}: 逃げの弦 {FleeChord(wander):F2} m / " +
                    $"ダッシュ {dashTime:F2}s / 立ち止まる間 {(wander.fleeHopPeriod - dashTime):F2}s");
            }

            // 名簿の重複 (同じ犬を 2 回登録すると自分自身と押し合って震える)
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    Check(pack.dogRoots[i] == null || pack.dogRoots[i] != pack.dogRoots[j],
                        $"dogRoots[{i}] != dogRoots[{j}] (同じ犬を 2 回登録していない)");

            // 帰還の円周に、必要な扇が物理的に載るか。
            // 2·asin((r自+r相手)/2R) が π を超えると「反対側へ回っても足りない」= 必ず重なる
            for (int i = 0; i < n; i++)
            {
                var fetchGo = KD_SceneObjects.Find(defs[i].FetchSystemName, out found);
                var fetch = fetchGo == null ? null : fetchGo.GetUdonSharpComponent<KD_DogFetchController>();
                if (fetch == null) continue;
                float worst = 0f;
                string worstKey = "";
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    float arg = (pack.dogRadius[i] + pack.dogRadius[j]) * fetch.returnSpread
                        / (2f * Mathf.Max(fetch.returnStopDistance, 0.05f));
                    if (arg <= worst) continue;
                    worst = arg;
                    worstKey = defs[j].key;
                }
                float deg = 2f * Mathf.Asin(Mathf.Min(1f, worst)) * Mathf.Rad2Deg;
                Check(worst < 1f,
                    $"{defs[i].key}: 帰還の円周 (R={fetch.returnStopDistance:F2}) に {worstKey} との扇が載る " +
                    $"(必要 {deg:F0}° / 比 {worst:F2} < 1.0)");
                // 3 匹が同時に帰ると扇の合計が 360° を超えうる。超えても壊れはしない
                // (押し出しが引き取る) が、実測値を残しておく
                log.AppendLine($"       {defs[i].key}: 最大の扇 {deg:F0}° (対 {worstKey})");
            }

            FinishVerify(log, fail);
        }

        // ---- helpers (他の SetupEditor と同型) ----

        static bool TryCopy(UdonSharpBehaviour proxy, StringBuilder log)
        {
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


        static void FinishVerify(StringBuilder log, int fail)
        {
            log.AppendLine(fail == 0 ? "VERIFY RESULT: PASS" : $"VERIFY RESULT: FAIL ({fail} checks)");
            Finish("Logs/imp-dog-008-verify.txt", log);
        }

        static void Finish(string file, StringBuilder log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }
    }
}
