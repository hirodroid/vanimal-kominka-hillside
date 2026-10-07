using System.IO;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-DOG-004: 徘徊 (KD_DogWanderController) のセットアップと検証。
    ///
    /// 実行順 (別実行にする — Program Asset 直後の CopyProxyToUdon は outdated error):
    ///   1 - Create Program Asset → force refresh
    ///   2 - Setup Wander System
    ///   3 - Verify Wander System
    ///
    /// **じゃれ合い (IMP-DOG-009) の配線はここではなく Dog Pack Setup が行う** —
    /// 名簿 (`pack` / `dogIndex`) と調整値は「犬をまたぐ層」なので 1 箇所に集約する。
    /// ここは触らないので、Wander Setup を再実行しても消えない。
    /// **ただし Wander System を作り直した場合は Dog Pack Setup を再実行すること。**
    ///
    /// 前提: Fetch System / Photo System が構築済み。
    /// KD_DogFetchController は Manual 同期のため、None のこのコントローラは
    /// **別 GameObject (KD_WanderSystem) に置く** (同一 GameObject で同期方式を
    /// 混在できない — vrc-udonsharp §2)。
    /// </summary>
    public static class KD_WanderSetupEditor
    {
        const string ScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.cs";
        const string ProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.asset";
        const string AreaName = "KD_WanderArea";
        const string PhotoSystemName = KD_DogDefs.PhotoSystemName;   // IMP-INF-003: 正本は KD_DogDefs
        const string BarkSourceName = "KD_BarkSource"; // IMP-FET-005 (犬の口元へ置く)
        const string BreathSourceName = "KD_BreathSource"; // IMP-DOG-015 (吠えとは別。持続音)

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Wander System/1 - Create Program Asset")]
        public static void CreateProgramAsset()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_WanderSetup] Create program asset");
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
                    Finish("Logs/imp-dog-004-program.txt", log);
                    return;
                }
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = script;
                AssetDatabase.CreateAsset(asset, ProgramPath);
                AssetDatabase.SaveAssets();
                log.AppendLine("created: " + ProgramPath);
            }
            log.AppendLine("RESULT: PASS (force refresh してから Setup を実行する)");
            Finish("Logs/imp-dog-004-program.txt", log);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Wander System/2 - Setup Wander System")]
        public static void Setup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_WanderSetup] Setup");

            int photoCount;
            var photoGo = KD_SceneObjects.Find(PhotoSystemName, out photoCount);
            if (photoCount > 1)
            {
                log.AppendLine($"FAIL '{PhotoSystemName}' がシーン内に {photoCount} 個ある。1 個に整理してから再実行");
                Finish("Logs/imp-dog-004-setup.txt", log);
                return;
            }
            // IMP-CAM-002: 撮影は 1 個のままだが、**逆配線は全犬**になった。
            // かつては Primary (コーギー) だけで、他の 3 匹は徘徊の走りも
            // じゃれ合いも 1 枚も撮られなかった
            var director = photoGo == null ? null : photoGo.GetUdonSharpComponent<KD_PhotoDirector>();
            if (director == null) log.AppendLine("WARN KD_PhotoSystem not found - 徘徊の追跡ドローンは無効");

            // IMP-DOG-014 Phase 2: 水場。**無くても止めない** — 水場が無ければ
            // 犬はその場で伏せる (Phase 1 の挙動) ので、サイクル自体は成立する
            int waterCount;
            var waterGo = KD_SceneObjects.Find(KD_DogDefs.WaterPlaceName, out waterCount);
            var waterPlaceT = (waterCount == 1 && waterGo != null) ? waterGo.transform : null;
            if (waterPlaceT == null)
                log.AppendLine("WARN " + KD_DogDefs.WaterPlaceName + " が "
                    + waterCount + " 個 - 水場へは寄らず、その場で伏せるだけになる");

            bool copyOk = true;
            int done = 0;
            var defsAll = KD_DogDefs.All;
            // 撮影側の犬配列 (並びは KD_DogDefs.All)。**この Setup がここを埋める** —
            // Wander System は Photo System より後に作られるので、Photo Setup 側は
            // 長さをそろえるところまでしかできない
            var wandersForDirector = new KD_DogWanderController[defsAll.Length];
            for (int di = 0; di < defsAll.Length; di++)
            {
                var def = defsAll[di];
                log.AppendLine("-- [" + di + "] " + def.displayName + " (" + def.key + ")");

                int dogCount, fetchCount, found;
                var dog = KD_SceneObjects.Find(def.DogName, out dogCount);
                var fetchGo = KD_SceneObjects.Find(def.FetchSystemName, out fetchCount);
                if (dogCount != 1 || fetchCount != 1)
                {
                    log.AppendLine($"   FAIL {def.DogName}={dogCount} / {def.FetchSystemName}={fetchCount} (それぞれ 1 個が前提。Fetch System を先に構築する)");
                    copyOk = false;
                    continue;
                }
                var fetch = fetchGo.GetUdonSharpComponent<KD_DogFetchController>();
                if (fetch == null)
                {
                    log.AppendLine("   FAIL KD_DogFetchController が無い");
                    copyOk = false;
                    continue;
                }

                // IMP-FET-005: 催促の首と尻尾は撫でコントローラの窓口へ差し込む
                // (追加回転を書く主体を増やさない — 設計書 §10 決定 3)
                int petCount;
                var petGo = KD_SceneObjects.Find(def.PetSystemName, out petCount);
                var pet = petGo == null ? null : petGo.GetUdonSharpComponent<KD_DogPetController>();
                if (pet == null)
                    log.AppendLine("   WARN " + def.PetSystemName + " が無い - 催促の首と尻尾 (IMP-FET-005) が出ない");

                var sysGo = KD_SceneObjects.Find(def.WanderSystemName, out found);
                if (found > 1)
                {
                    log.AppendLine($"   FAIL '{def.WanderSystemName}' がシーン内に {found} 個ある");
                    copyOk = false;
                    continue;
                }
                if (sysGo == null)
                {
                    sysGo = new GameObject(def.WanderSystemName);
                    KD_SceneObjects.PlaceIfNew(sysGo, def.UnitPath);   // IMP-INF-003: 新規のときだけ犬ユニットへ
                    sysGo.transform.position = dog.transform.position;
                    log.AppendLine("   created " + def.WanderSystemName);
                }

                // 徘徊範囲の中心。既存があれば位置は触らない (庭の中心へ手で動かしてよい)
                var areaT = sysGo.transform.Find(AreaName);
                if (areaT == null)
                {
                    var areaGo = new GameObject(AreaName);
                    areaT = areaGo.transform;
                    areaT.SetParent(sysGo.transform, false);
                    areaT.position = dog.transform.position;
                    log.AppendLine($"   created {AreaName} at {areaT.position} (犬の現在地。庭の中心へ動かしてよい)");
                }
                else
                {
                    log.AppendLine("   reused " + AreaName + " (position untouched)");
                }

                var proxy = sysGo.GetUdonSharpComponent<KD_DogWanderController>();
                if (proxy == null)
                {
                    proxy = sysGo.AddUdonSharpComponent<KD_DogWanderController>();
                    log.AppendLine("   added KD_DogWanderController");
                }

                // 上書きするのは配線だけ。スケジュール・速度・半径の手調整は保持する。
                // 接地パラメータだけは Fetch 側を正本として毎回そろえる (別値だと段差で挙動が割れる)
                proxy.dogRoot = dog.transform;
                proxy.dogAnimator = dog.GetComponent<Animator>();
                proxy.fetchController = fetch;
                proxy.areaCenter = areaT;
                // IMP-CAM-002: **全犬を撮影へ繋ぐ。** 撮影側は犬を番号で持つので、
                // Director が 1 個であることと「4 匹とも撮られること」は両立する
                proxy.photoDirector = director;
                proxy.groundLayerMask = fetch.groundLayerMask;
                proxy.groundRayOffset = fetch.groundRayOffset;
                proxy.groundRayDistance = fetch.groundRayDistance;
                // IMP-DOG-005: 犬ごとに徘徊のレールをずらす (0 = 1 匹版と同一)
                proxy.seedSalt = def.seedSalt;
                log.AppendLine("   seedSalt=" + def.seedSalt);

                // ---- IMP-FET-005: 投げる前の演技 ----
                proxy.petController = pet;

                // IMP-DOG-019 Phase 4: 逆向きの配線。撫でコントローラは
                // **寝ているか (IsAsleep) を知らないと横倒しの首を捻る** —
                // 立っている前提の頭の到達ポーズをそのまま足してしまうため。
                // Pet Setup 側ではなくここで張るのは、Fetch Setup が
                // pet.fetchController を張るのと同じ理由 (どちらが後に作られても両方向そろう)
                if (pet != null)
                {
                    pet.wanderController = proxy;
                    try
                    {
                        UdonSharpEditorUtility.CopyProxyToUdon(pet);
                        log.AppendLine("   pet.wanderController wired (IMP-DOG-019 Phase 4)");
                    }
                    catch (System.Exception e)
                    {
                        copyOk = false;
                        log.AppendLine("   pet CopyProxyToUdon FAILED: " + e.Message);
                    }
                }
                proxy.barkSource = SetupBarkSource(fetch.mouthAnchor, log);
                proxy.barkClip = LoadBark(def, log);
                // IMP-DOG-015: はぁはぁは**吠えとは別の AudioSource** (単発 vs 持続)
                proxy.breathSource = SetupBreathSource(fetch.mouthAnchor, log);
                // 2026-09-04: 運動後の息は**犬定義ごと** (吠えと同じ)、寝息は**共通の 1 本**。
                // 既に同じクリップが入っていれば WireClip が何も書かない (冪等)
                proxy.breathClip = WireClip(proxy.breathClip, LoadBreath(def, log), "breathClip", log);
                proxy.breathAlternateClip = WireClip(proxy.breathAlternateClip,
                    LoadBreathClip(KD_DogDef.AlternateBreathClipGuid, "alternateBreath", log), "breathAlternateClip", log);
                proxy.breathSleepClip = WireClip(proxy.breathSleepClip, LoadSleepBreath(log),
                    "breathSleepClip", log);
                // IMP-DOG-014 Phase 2: 水場は**シーンに据えた共有の一点** (犬ごとに作らない)。
                // 置き場所の正本は KD_DogDefs.LayoutTable (Scene Layout が動かす)
                proxy.waterPlace = waterPlaceT;

                copyOk &= TryCopy(proxy, log);
                wandersForDirector[di] = proxy;
                done++;
            }

            // 撮影への逆配線 (director → wander)。**配列ごと入れ替える** —
            // 要素だけ書き換えると、犬定義を足したときに長さがそろわず
            // Director が Start で自分を止める (無言で撮影が消えるより良いが、直せる形にする)
            if (director != null)
            {
                director.wanderControllers = wandersForDirector;
                copyOk &= TryCopy(director, log);
                log.AppendLine("wired director.wanderControllers (" + wandersForDirector.Length + " 匹)");
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("dogs=" + done + "/" + defsAll.Length);
            log.AppendLine("NOTE じゃれ合い (IMP-DOG-009) の名簿・調整値はここでは触らない。"
                + "Wander System を作り直したときは Tools/KominkaHillside/Menu/Imported-Unused/Dog Pack/2 - Setup Dog Pack を再実行する");
            log.AppendLine(copyOk
                ? "SETUP RESULT: PASS (scene not saved yet)"
                : "SETUP RESULT: FAIL (force refresh 後に再実行)");
            Finish("Logs/imp-dog-004-setup.txt", log);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Wander System/3 - Verify Wander System")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_WanderSetup] Verify");
            int fail = 0;
            void Check(bool cond, string what)
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + what);
                if (!cond) fail++;
            }

            var pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath);
            var expectedScript = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptPath);
            Check(pa != null && pa.sourceCsScript != null && pa.sourceCsScript == expectedScript,
                "program asset points at KD_DogWanderController.cs");

            int photoCount;
            var photoGo = KD_SceneObjects.Find(PhotoSystemName, out photoCount);
            var director = photoGo == null ? null : photoGo.GetUdonSharpComponent<KD_PhotoDirector>();

            // 犬ごとに独立して判定する (1 匹落ちても他の結果が見えるように)
            var usedSalts = new System.Collections.Generic.List<int>();
            var defsAll = KD_DogDefs.All;
            var barkClips = new AudioClip[defsAll.Length]; // IMP-FET-005: 声の重複検査 (ループ後)
            // 2026-09-04: 息のばらつきを**数える** (吠えと違い重複検査ではない —
            // 音源が 3 本しか無く、小型犬 2 匹の共有は意図した設計)
            var breathClips = new AudioClip[defsAll.Length];
            int sleepClipWired = 0;
            for (int di = 0; di < defsAll.Length; di++)
            {
                var def = defsAll[di];
                log.AppendLine("-- [" + di + "] " + def.displayName + " (" + def.key + ")");
                string tag = def.key + ": ";

                int dogCount, found;
                var dog = KD_SceneObjects.Find(def.DogName, out dogCount);
                Check(dogCount == 1, tag + def.DogName + " exists exactly once (actual " + dogCount + ")");

                var sysGo = KD_SceneObjects.Find(def.WanderSystemName, out found);
                Check(found == 1, tag + def.WanderSystemName + " exists exactly once (actual " + found + ")");
                if (sysGo == null || dog == null) continue;

                Check(!UnityEditor.PrefabUtility.IsPartOfPrefabInstance(sysGo),
                    tag + def.WanderSystemName + " is a plain scene object");

                // Manual 同期の Fetch と同居していないか (同一 GameObject の同期方式は混在不可)
                var ubs = sysGo.GetComponents<VRC.Udon.UdonBehaviour>();
                Check(ubs.Length == 1 && ubs[0].programSource != null && ubs[0].programSource.name == "KD_DogWanderController",
                    tag + "exactly 1 UdonBehaviour with programSource KD_DogWanderController");

                var proxy = sysGo.GetUdonSharpComponent<KD_DogWanderController>();
                Check(proxy != null, tag + "proxy component exists");
                if (proxy == null) continue;

                Check(proxy.dogRoot == dog.transform, tag + "dogRoot wired");
                Check(proxy.dogAnimator != null && proxy.dogAnimator.gameObject == dog,
                    tag + "dogAnimator wired to " + def.DogName);
                Check(proxy.fetchController != null &&
                    proxy.fetchController.gameObject.name == def.FetchSystemName,
                    tag + "fetchController wired to " + def.FetchSystemName);
                // 別の犬の Fetch を掴んでいないか (多頭で最も起きやすい取り違え)
                Check(proxy.fetchController != null && proxy.fetchController.dogRoot == dog.transform,
                    tag + "fetchController が同じ犬を指している");
                Check(proxy.areaCenter != null && proxy.areaCenter.name == AreaName &&
                    proxy.areaCenter.parent == sysGo.transform, tag + "areaCenter wired to child " + AreaName);
                Check(proxy.enabled, tag + "proxy enabled");

                // IMP-DOG-005: 徘徊レールのずらし。犬定義と一致し、他の犬と重複しないこと
                Check(proxy.seedSalt == def.seedSalt,
                    tag + "seedSalt == 犬定義 (actual " + proxy.seedSalt + " / def " + def.seedSalt + ")");
                Check(!usedSalts.Contains(proxy.seedSalt),
                    tag + "seedSalt が他の犬と重複しない (actual " + proxy.seedSalt + ")");
                usedSalts.Add(proxy.seedSalt);

                // **proxy ではなく backing UdonBehaviour を直接見る** (vrc-verification: 独立チャネル)。
                // seedSalt は参照でなく値なので、CopyProxyToUdon が漏れても proxy 側だけ正しく見え、
                // 実行時は既定の 0 に落ちて「2 匹目が 1 匹目と同じレールで徘徊する」になる。
                // 症状が「たまに同じ動き」なので Play では気づきにくい — ここで機械的に落とす
                if (ubs.Length == 1 && ubs[0].publicVariables != null)
                {
                    object raw;
                    bool got = ubs[0].publicVariables.TryGetVariableValue("seedSalt", out raw);
                    Check(got, tag + "backing UdonBehaviour に seedSalt がある");
                    Check(got && raw is int && (int)raw == def.seedSalt,
                        tag + "backing seedSalt == 犬定義 (actual " + (got ? raw : "missing") + ")");
                }

                // スケジュールの前提 (窓がサイクルに収まる / ヒステリシスの向き / 正の区間)
                Check(proxy.cyclePeriod > proxy.wanderDurationMax,
                    tag + $"cyclePeriod ({proxy.cyclePeriod}) > wanderDurationMax ({proxy.wanderDurationMax})");
                Check(proxy.wanderDurationMax >= proxy.wanderDurationMin && proxy.wanderDurationMin > 0f,
                    tag + "wanderDuration range valid (0 < min <= max)");
                Check(proxy.waypointPeriod > 0.5f, tag + "waypointPeriod > 0.5s");
                Check(proxy.resumeRadius > proxy.pauseRadius,
                    tag + $"resumeRadius ({proxy.resumeRadius}) > pauseRadius ({proxy.pauseRadius})");
                Check(proxy.areaRadius > 0.5f, tag + "areaRadius > 0.5m");
                Check(proxy.arriveRadius > 0f && proxy.arriveRadius < proxy.areaRadius,
                    tag + "0 < arriveRadius < areaRadius");
                Check(proxy.runSpeed > 0f && proxy.walkSpeed > 0f && proxy.groundRayDistance > 0f,
                    tag + "speeds and ground ray distance positive");

                // 収束の担保: 最悪ホップ (円の直径) を 1 目標点の時間内に走り切れること。
                // 満たさないと「到着 = クライアント間の座標一致点」が起きず発散が残る
                float worstHopTime = (proxy.areaRadius * 2f) / Mathf.Max(proxy.runSpeed, 0.01f) + 1f;
                Check(proxy.waypointPeriod >= worstHopTime,
                    tag + $"waypointPeriod ({proxy.waypointPeriod}s) >= worst hop time + margin ({worstHopTime:F1}s)");

                // ---- IMP-FET-005: 投げる前の演技 ----
                //
                // **確率で鳴らす仕様なので、抜けていても Play では気づきにくい。**
                // 4 匹のうち 1 匹だけ吠えない/寄ってこない、が無症状で残る型なので機械検査する
                Check(proxy.petController != null, tag + "petController wired (催促の首と尻尾)");
                // IMP-DOG-019 Phase 4: 逆向き。片方向だけだと**寝ている犬を撫でたときだけ
                // 首が捻れる**という、立っている犬では絶対に出ない壊れ方をする
                Check(proxy.petController != null && proxy.petController.wanderController == proxy,
                    tag + "petController.wanderController が同じ犬の Wander を指している (IMP-DOG-019 Phase 4)");
                Check(proxy.barkSource != null, tag + "barkSource wired (吠え声)");
                Check(proxy.barkClip != null, tag + "barkClip wired (体格クラスは犬定義が正本)");
                if (proxy.barkSource != null)
                {
                    // 犬に付いていなければ「どの犬が吠えたか音の方向で分かる」が成立しない
                    bool onDog = proxy.dogRoot != null
                        && proxy.barkSource.transform.IsChildOf(proxy.dogRoot);
                    Check(onDog, tag + "barkSource is under the dog (音の方向で犬を識別する)");
                    Check(proxy.barkSource.spatialBlend > 0.99f, tag + "barkSource spatialBlend == 1 (3D)");
                    Check(!proxy.barkSource.playOnAwake, tag + "barkSource playOnAwake off");
                    // **VRChat の空間音はこれが正本** — 無いと AudioSource 側をいくら詰めても効かない
                    var spatial = proxy.barkSource.GetComponent<VRCSpatialAudioSource>();
                    Check(spatial != null, tag + "barkSource に VRCSpatialAudioSource がある");
                    if (spatial != null)
                        Check(spatial.Far > spatial.Near && spatial.Near >= 0f,
                            tag + $"spatial Far ({spatial.Far}) > Near ({spatial.Near})");
                }
                // ---- IMP-DOG-015: はぁはぁの器 ----
                Check(proxy.waterPlace != null,
                    tag + "waterPlace wired (" + KD_DogDefs.WaterPlaceName + " — 無いとその場で伏せるだけになる)");
                Check(proxy.drinkStandoff > 0.001f && proxy.drinkDuration > 0.01f
                        && proxy.waterApproachTimeout > 0.01f && proxy.waterMaxDistance > 0.01f,
                    tag + "水場のしきい値が 0 でない");
                // ---- IMP-DOG-014 Phase 2: 水場 ----
                // **水場は 4 匹で同じ 1 個**であること (犬ごとに用意しない = 識別層の制約が無い)
                Check(proxy.waterPlace != null,
                    tag + "waterPlace wired (" + KD_DogDefs.WaterPlaceName
                        + " — 無いとその場で伏せるだけになる)");
                Check(proxy.drinkStandoff > 0.001f && proxy.drinkDuration > 0.01f
                        && proxy.waterApproachTimeout > 0.01f && proxy.waterMaxDistance > 0.01f,
                    tag + "水場のしきい値が 0 でない");

                Check(proxy.breathSource != null, tag + "breathSource wired (呼吸音)");
                if (proxy.breathSource != null)
                {
                    // **吠えと同じ AudioSource を使い回していないこと。**
                    // 使い回すと PlayOneShot (吠え) と loop (呼吸) が互いを切る
                    Check(proxy.breathSource != proxy.barkSource,
                        tag + "breathSource != barkSource (単発と持続を同居させない)");
                    Check(proxy.breathSource.loop, tag + "breathSource loop == true (持続音)");
                    Check(!proxy.breathSource.playOnAwake,
                        tag + "breathSource playOnAwake off (入室した瞬間に 4 匹が息を切らさない)");
                    Check(proxy.breathSource.spatialBlend > 0.99f, tag + "breathSource spatialBlend == 1 (3D)");
                    Check(proxy.dogRoot != null
                            && proxy.breathSource.transform.IsChildOf(proxy.dogRoot),
                        tag + "breathSource is under the dog");
                    Check(proxy.breathSource.GetComponent<VRCSpatialAudioSource>() != null,
                        tag + "breathSource に VRCSpatialAudioSource がある");
                }
                // クリップは未設定でも FAIL にしない (器はクリップ無しで動くのが Phase 1 の完了条件)
                if (proxy.breathClip == null)
                    log.AppendLine("  NOTE " + tag + "breathClip 未設定 — 呼吸音は無音 (器は動く)");
                else
                {
                    Check(proxy.breathClip.channels == 1,
                        tag + "breathClip is mono (Force To Mono — 3D の定位)");
                    // 2026-09-04: **犬定義の GUID と一致していること。**
                    // 犬種ごとに音源が分かれたので、ここがずれると
                    // 「ラブがポメの息をする」= 体格が音で割れる (識別層 5 と同型の事故)。
                    // Play では 4 匹を並べて聞き比べないと気づけない
                    Check(GuidOf(proxy.breathClip) == def.breathClipGuid,
                        tag + "breathClip == 犬定義の breathClipGuid ("
                            + proxy.breathClip.name + ")");
                }

                Check(proxy.breathAlternateClip != null && proxy.breathAlternateClip.channels == 1
                    && GuidOf(proxy.breathAlternateClip) == KD_DogDef.AlternateBreathClipGuid,
                    tag + "breathAlternateClip is the shared mono alternate (IMP-DOG-023)");
                // ---- 2026-09-04: 寝息クリップ ----
                // **未設定でも FAIL にしない** — ランタイムが breathClip + ピッチの
                // 代用へ落ちるので、無言で寝息が消えることはない
                if (proxy.breathSleepClip == null)
                    log.AppendLine("  NOTE " + tag
                        + "breathSleepClip 未設定 — breathSleepPitch による代用で鳴る");
                else
                {
                    Check(proxy.breathSleepClip.channels == 1,
                        tag + "breathSleepClip is mono (Force To Mono — 3D の定位)");
                    Check(GuidOf(proxy.breathSleepClip) == KD_DogDef.SleepBreathClipGuid,
                        tag + "breathSleepClip == SleepBreathClipGuid (全犬種共通の 1 本)");
                    Check(proxy.breathSleepClip != proxy.breathClip,
                        tag + "breathSleepClip != breathClip (同じなら差し替える意味が無い)");
                    sleepClipWired++;
                }
                Check(proxy.breathRunSpeed > 0.01f && proxy.breathTriggerRunTime > 0.01f
                        && proxy.breathCalmDuration > 0.01f && proxy.breathVolume > 0.001f,
                    tag + "呼吸のしきい値が 0 でない");
                Check(proxy.breathRunSpeed > proxy.walkSpeed,
                    tag + "breathRunSpeed > walkSpeed (歩いただけで息が上がらない。期待 > "
                        + proxy.walkSpeed.ToString("F2") + " / 実際 "
                        + proxy.breathRunSpeed.ToString("F2") + ")");

                // ---- IMP-DOG-017: そばに居る間のアイドル呼吸 ----
                Check(proxy.breathIdleVolume > 0.001f && proxy.breathIdleFadeSeconds > 0.001f,
                    tag + "アイドル呼吸のしきい値が 0 でない (IMP-DOG-017 — 既存シーンの後付けフィールドは 0 で入る)");
                Check(proxy.breathIdleVolume < proxy.breathVolume,
                    tag + "breathIdleVolume < breathVolume (走った息と区別が付く)");
                Check(proxy.breathIdleFadeSeconds > proxy.breathFadeSeconds,
                    tag + "breathIdleFadeSeconds > breathFadeSeconds (近づいた瞬間にスイッチが入ったと読ませない)");
                Check(!proxy.restWakesOnApproach,
                    tag + "restWakesOnApproach == false (伏せたまま迎える。IMP-DOG-017 / 2026-08-28 ユーザー判断)");

                // ---- IMP-DOG-019 Phase 4: 寝息 ----
                // **これが 0 だと「寝ている返事」が丸ごと無音**で、しかも寝ている犬を
                // 撫でに行かないと気づけない (Issue §4.1 = 乗らないなら出さない、の対象)
                Check(proxy.breathSleepVolume > 0.001f && proxy.breathSleepPitch > 0.01f,
                    tag + "寝息のしきい値が 0 でない (IMP-DOG-019 Phase 4 — 後付けフィールドは 0 で入る)");
                Check(proxy.breathSleepVolume <= proxy.breathIdleVolume,
                    tag + "breathSleepVolume <= breathIdleVolume (寝ている犬の息が起きている犬より大きいのは嘘)");

                if (proxy.barkClip != null)
                {
                    // ステレオのままだと 3D 音源で定位が鈍る (設計書 §10 決定 6)
                    Check(proxy.barkClip.channels == 1, tag + "barkClip is mono (Force To Mono)");
                }
                if (proxy.barkClip != null) barkClips[di] = proxy.barkClip;
                if (proxy.breathClip != null) breathClips[di] = proxy.breathClip;

                // **proxy ではなく backing UdonBehaviour も見る** (seedSalt と同じ理由)。
                // proxy が正しくても CopyProxyToUdon が漏れれば実行時は null に落ち、
                // **催促の首・尻尾・吠えだけが黙って消える** — しかも吠えは確率なので
                // 「今回は鳴らなかっただけ」と区別が付かない (Codex 指摘)
                if (ubs.Length == 1 && ubs[0].publicVariables != null)
                {
                    var pv = ubs[0].publicVariables;
                    object v;
                    Check(pv.TryGetVariableValue("petController", out v) && v != null && !v.Equals(null),
                        tag + "backing petController != null");
                    Check(pv.TryGetVariableValue("barkSource", out v) && v != null && !v.Equals(null),
                        tag + "backing barkSource != null");
                    Check(pv.TryGetVariableValue("barkClip", out v) && v != null && !v.Equals(null),
                        tag + "backing barkClip != null");
                }

                // 逆向き (pet 側) の backing も見る (IMP-DOG-019 Phase 4)。
                // proxy だけ見て通すと、CopyProxyToUdon が漏れたときに
                // **実行時だけ IsAsleep が引けず、寝ている犬の首が捻れる**
                if (proxy.petController != null)
                {
                    var petUb = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy.petController);
                    object pw;
                    Check(petUb != null && petUb.publicVariables != null
                        && petUb.publicVariables.TryGetVariableValue("wanderController", out pw)
                        && pw != null && !pw.Equals(null),
                        tag + "backing pet.wanderController != null (IMP-DOG-019 Phase 4)");
                }
                Check(proxy.nudgeStandoff > 0.3f,
                    tag + $"nudgeStandoff ({proxy.nudgeStandoff}) > 0.3m (近すぎるとボールを振れない)");
                Check(proxy.holderStableTime > 0f,
                    tag + "holderStableTime > 0 (Ownership 移譲の伝播前に別人へ寄らない)");
                Check(proxy.feintTrackDuration > 0f && proxy.feintReturnDuration > 0f,
                    tag + "feint durations positive");
                Check(proxy.nudgeStepHeight > 0f && proxy.nudgeWallRadius > 0f,
                    tag + "wall check params positive");

                // 接地パラメータが Fetch と一致しているか (別値だと段差で挙動が割れる)
                if (proxy.fetchController != null)
                {
                    Check(proxy.groundLayerMask == proxy.fetchController.groundLayerMask &&
                        Mathf.Approximately(proxy.groundRayOffset, proxy.fetchController.groundRayOffset) &&
                        Mathf.Approximately(proxy.groundRayDistance, proxy.fetchController.groundRayDistance),
                        tag + "ground params match KD_DogFetchController");
                }

                // ---- 撮影 (IMP-CAM-002): **全犬が双方向に繋がる** ----
                //
                // かつてここは「非 Primary の photoDirector は null であること」を
                // **検査していた** — 単一犬の固定が Setup だけでなく Verify にも
                // 埋まっていたので、Director を直しても直らず、しかも PASS が出続けていた。
                if (director == null)
                {
                    log.AppendLine("  ok   " + tag + "(KD_PhotoSystem absent - drone wiring optional, skipped)");
                }
                else
                {
                    Check(proxy.photoDirector == director, tag + "photoDirector wired to KD_PhotoSystem");
                    // 逆向き: 撮影の犬配列の**この犬の枠**に自分が入っていること。
                    // 添字がずれていると「ダックスの走りをコーギーの状態で判定する」になる
                    var wcs = director.wanderControllers;
                    Check(wcs != null && wcs.Length == defsAll.Length,
                        tag + $"director.wanderControllers.Length == {defsAll.Length} (actual " +
                        (wcs == null ? "null" : wcs.Length.ToString()) + ")");
                    Check(wcs != null && di < wcs.Length && wcs[di] == proxy,
                        tag + $"director.wanderControllers[{di}] == {def.WanderSystemName}");
                    // 通知は dogIndex をそのまま渡すので、名簿の番号と配列の添字は同じ値でなければならない
                    Check(proxy.dogIndex == di,
                        tag + $"dogIndex == {di} (actual {proxy.dogIndex}) — 撮影の配列の添字と同じ。" +
                        "赤いときは Dog Pack Setup を流す");
                }
            }

            // IMP-FET-005: **犬ごとに違う声であること = 識別層 5。**
            // 同じクリップが 2 匹に付いていると「音の方向で分かる」が半減するので落とす。
            // 犬定義の GUID を取り違えたときにここで出る (Play では気づけない)
            //
            // IMP-DOG-020 (2026-09-05 ユーザー決定): **例外 2 組だけ共有を許す** —
            // 柴 ↔ コーギー (中型) / サモエド ↔ ラブ (大型)。吠えの音源が 4 本しか無く、
            // 6 匹目までは体格クラスが同じ犬で重ねる (息の共有 (ポメ / ダックス) と同じ理由)。
            // **音源を調達したら <see cref="KD_DogDefs.BarkSharePermitted"/> の組を消す** = 検査が元の厳しさに戻る
            for (int a = 0; a < barkClips.Length; a++)
            {
                if (barkClips[a] == null) continue;
                for (int b = a + 1; b < barkClips.Length; b++)
                {
                    if (barkClips[b] == null) continue;
                    bool permitted = KD_DogDefs.BarkSharePermitted(defsAll[a].key, defsAll[b].key);
                    Check(barkClips[a] != barkClips[b] || permitted,
                        $"bark clip unique: [{a}] {defsAll[a].displayName} != [{b}] {defsAll[b].displayName}" +
                        (permitted ? " (共有を許可した組 — IMP-DOG-020。音源調達で解消する)" : ""));
                }
            }

            // ---- 2026-09-04: 息のばらつき ----
            // **吠えのような「全部違うこと」は要求しない** — 運動後の音源が 3 本しか無いので
            // 小型犬 2 匹 (ポメ / ダックス) は意図して共有している。
            // 検査するのは「**1 本に戻っていないこと**」= 元の不満 (4 匹とも同じ音) の再発。
            // 4 本目が入って共有が解消したら、ここの期待値が 3 → 4 へ上がる
            var distinctBreath = new System.Collections.Generic.List<AudioClip>();
            foreach (var c in breathClips)
                if (c != null && !distinctBreath.Contains(c)) distinctBreath.Add(c);
            Check(distinctBreath.Count >= 3,
                $"breath clip variation: {distinctBreath.Count} 種 / {defsAll.Length} 匹 "
                    + "(>= 3。1 本に戻っていたら 2026-09-04 の要望が消えている)");
            log.AppendLine("  NOTE breath: "
                + string.Join(" / ", System.Array.ConvertAll(defsAll, d =>
                {
                    var p = AssetDatabase.GUIDToAssetPath(d.breathClipGuid);
                    return d.key + "=" + (string.IsNullOrEmpty(p) ? "(none)"
                        : System.IO.Path.GetFileNameWithoutExtension(p));
                })));
            Check(sleepClipWired == defsAll.Length,
                $"breathSleepClip wired on all dogs ({sleepClipWired}/{defsAll.Length}) — "
                    + "寝息 (2026-09-04)");

            var desc = Object.FindObjectOfType<VRCSceneDescriptor>();
            Check(desc != null, "VRCSceneDescriptor exists");
            if (desc != null && desc.NetworkIDCollection != null)
            {
                int broken = 0;
                foreach (var pair in desc.NetworkIDCollection)
                    if (pair == null || pair.gameObject == null) broken++;
                Check(broken == 0, $"NetworkIDs broken refs == 0 (actual {broken})");
            }

            FinishVerify(log, fail);
        }

        // ---- helpers (他の SetupEditor と同型) ----

        /// <summary>IMP-FET-005: 吠え声の AudioSource を犬の口元へ作る。
        ///
        /// **口元 (Fetch が作った mouthAnchor) の子に置く** — どの犬が吠えたかが
        /// 音の方向で分かることが識別層としての価値なので、System (動かない空オブジェクト) では
        /// なく犬に付いていなければ意味がない。
        ///
        /// **手で置かない。** 4 匹のうち 1 つ忘れると「その犬だけ吠えない」になり、
        /// 確率で鳴らす仕様なので気づくまでに時間がかかる。</summary>
        static AudioSource SetupBarkSource(Transform mouth, StringBuilder log)
        {
            if (mouth == null)
            {
                log.AppendLine("   WARN mouthAnchor が取れない - 吠え声の AudioSource を作れない (Fetch Setup を先に)");
                return null;
            }

            var t = mouth.Find(BarkSourceName);
            AudioSource src;
            bool created = false;
            if (t == null)
            {
                var go = new GameObject(BarkSourceName);
                go.transform.SetParent(mouth, false);
                src = go.AddComponent<AudioSource>();
                created = true;
                log.AppendLine("   created " + BarkSourceName + " under " + mouth.name);
            }
            else
            {
                src = t.GetComponent<AudioSource>();
                if (src == null)
                {
                    src = t.gameObject.AddComponent<AudioSource>();
                    created = true;
                }
                log.AppendLine("   reused " + BarkSourceName + " (音量・距離減衰は保持)");
            }

            // **毎回正すのは「壊れていたら成立しないもの」だけ** (Codex 指摘)。
            // 距離減衰と音量は Play で耳決めする対象なので、再実行で潰さない —
            // 「上書きするのは配線だけ。手調整は保持する」という Setup の慣行に合わせる
            src.playOnAwake = false; // 立てると入室時に 4 匹が一斉に吠える
            src.loop = false;        // 立てると吠え続ける
            src.spatialBlend = 1f;   // 0 だと定位が消えて識別層として成立しない

            // **VRChat の空間音は VRCSpatialAudioSource が正本** (2026-08-17 ユーザー指摘)。
            // これが無いと VRChat 側の既定の扱いになり、AudioSource の
            // rolloff / minDistance / maxDistance を詰めても意図どおりに鳴らない。
            // 距離は **Near / Far** で決まる (UseAudioSourceVolumeCurve = false のとき)
            var spatial = src.GetComponent<VRCSpatialAudioSource>();
            if (spatial == null)
            {
                spatial = src.gameObject.AddComponent<VRCSpatialAudioSource>();
                spatial.Gain = 0f;   // 素材が peak ≈ 0dBFS なので足さない
                spatial.Near = 2f;   // ここまでは減衰しない
                spatial.Far = 5f;    // ここで聞こえなくなる (庭の広さ = areaRadius 3m に対して素直な値)
                spatial.VolumetricRadius = 0f;
                log.AppendLine("   added VRCSpatialAudioSource (Gain 0 / Near 2 / Far 5)");
            }
            else
            {
                // **既にあるものは触らない** — Gain / Near / Far は耳で決める値
                log.AppendLine($"   reused VRCSpatialAudioSource (Gain {spatial.Gain} / Near {spatial.Near} / Far {spatial.Far})");
            }

            global::KominkaHillside.Editor.KH_AudioSpatialEditor.Configure(src);
            if (!created) return src;

            // ---- ここから下は新規作成時の出発点。Play で触ってよい ----
            // **距離は VRCSpatialAudioSource の Near / Far が効く**ので、こちらは保険
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 2f;
            src.maxDistance = 5f;
            src.dopplerLevel = 0f; // 走る犬の声がピッチで揺れると不自然
            return src;
        }

        /// <summary>犬定義の吠え声を読む。**ついでにインポート設定も正す** —
        /// <summary>IMP-DOG-015: はぁはぁ用の AudioSource を犬の口元へ作る。
        ///
        /// **吠えの `KD_BarkSource` に相乗りしない。** あちらは `PlayOneShot` の単発、
        /// こちらは `loop` の持続で、同じ AudioSource だと片方が片方を切る。
        /// 器の作り方は吠えと同じ (口元の子 + `VRCSpatialAudioSource`)。
        ///
        /// **Near / Far は吠えより狭く始める** — 吠えは「どの犬が鳴いたか分かる」ための音で
        /// 遠くまで届いてよいが、呼吸は**そばに居る人にだけ聞こえればいい**。
        /// 4 匹が同時にはぁはぁ言って庭が騒がしくなるのが危険 1 なので、
        /// 出発点を近くに置く (Play で耳決めする)。</summary>
        static AudioSource SetupBreathSource(Transform mouth, StringBuilder log)
        {
            if (mouth == null)
            {
                log.AppendLine("   WARN mouthAnchor が取れない - 呼吸音の AudioSource を作れない (Fetch Setup を先に)");
                return null;
            }

            var t = mouth.Find(BreathSourceName);
            AudioSource src;
            bool created = false;
            if (t == null)
            {
                var go = new GameObject(BreathSourceName);
                go.transform.SetParent(mouth, false);
                src = go.AddComponent<AudioSource>();
                created = true;
                log.AppendLine("   created " + BreathSourceName + " under " + mouth.name);
            }
            else
            {
                src = t.GetComponent<AudioSource>();
                if (src == null)
                {
                    src = t.gameObject.AddComponent<AudioSource>();
                    created = true;
                }
                log.AppendLine("   reused " + BreathSourceName + " (音量・距離減衰は保持)");
            }

            // **毎回正すのは「壊れていたら成立しないもの」だけ** (吠えと同じ規律)。
            // playOnAwake が立つと入室した瞬間に 4 匹が息を切らしている絵になる
            src.playOnAwake = false;
            src.loop = true;         // 持続音。ここが吠えとの決定的な違い
            src.spatialBlend = 1f;   // 0 だと庭のどこに居ても同じ大きさで鳴る
            if (created) src.volume = 0f; // 鳴り始めはランタイムがフェードで上げる

            var spatial = src.GetComponent<VRCSpatialAudioSource>();
            if (spatial == null)
            {
                spatial = src.gameObject.AddComponent<VRCSpatialAudioSource>();
                spatial.Gain = 0f;
                spatial.Near = 1f;   // 吠え (2) より近い — そばに居る人のための音
                spatial.Far = 3.5f;  // 吠え (5) より狭い (危険 1: 4 匹同時のうるささ)
                spatial.VolumetricRadius = 0f;
                log.AppendLine("   added VRCSpatialAudioSource (Gain 0 / Near 1 / Far 3.5)");
            }
            else
            {
                log.AppendLine($"   reused VRCSpatialAudioSource (Gain {spatial.Gain} / Near {spatial.Near} / Far {spatial.Far})");
            }

            global::KominkaHillside.Editor.KH_AudioSpatialEditor.Configure(src);
            if (!created) return src;

            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1f;
            src.maxDistance = 3.5f;
            src.dopplerLevel = 0f; // 走っている犬の呼吸がピッチで揺れると不自然
            return src;
        }

        /// <summary>アセットの GUID を引く。**Verify が「犬定義の GUID と一致するか」を
        /// 見るために使う** — 名前で照合すると、ファイル名が似た音源を取り違えても通る。</summary>
        static string GuidOf(Object asset)
        {
            if (asset == null) return null;
            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
        }

        /// <summary>クリップを配線する。**既に同じものが入っていれば何も書かない (冪等)。**
        /// 違うものが入っていたときだけ差し替えて、何から何へ変わったかをログへ残す —
        /// 定数 1 本から犬種ごとへ移すような入れ替えを、黙って起こさせないため。</summary>
        static AudioClip WireClip(AudioClip current, AudioClip want, string label, StringBuilder log)
        {
            if (current == want) return current;
            log.AppendLine("   " + label + " replaced: "
                + (current == null ? "(none)" : current.name) + " -> "
                + (want == null ? "(none)" : want.name));
            return want;
        }

        /// <summary>IMP-DOG-015: 運動後の呼吸音を読む。**2026-09-04 から犬定義ごと**
        /// (定数 1 本ではなくなった。理由は <see cref="KD_DogDef.breathClipGuid"/>)。
        /// インポート設定を正すのは吠えと同じ理由 (3D の定位)。</summary>
        static AudioClip LoadBreath(KD_DogDef def, StringBuilder log)
        {
            return LoadBreathClip(def.breathClipGuid, "breath", log);
        }

        /// <summary>IMP-DOG-019 Phase 4 追補: 寝息を読む。**全犬種で共通の 1 本**なので
        /// 犬定義を取らない (音源が 1 本しか無い。理由は
        /// <see cref="KD_DogDef.SleepBreathClipGuid"/>)。</summary>
        static AudioClip LoadSleepBreath(StringBuilder log)
        {
            return LoadBreathClip(KD_DogDef.SleepBreathClipGuid, "sleepBreath", log);
        }

        /// <summary>呼吸系クリップの共通の読み口。**インポート設定 (Force To Mono /
        /// Preload) をここで揃える** — 手作業に頼ると増えた音源のどれかが必ず抜け、
        /// ステレオのまま 3D 音源に乗って定位が鈍る。</summary>
        internal static AudioClip LoadBreathClip(string guid, string label, StringBuilder log)
        {
            string path = string.IsNullOrEmpty(guid) ? null : AssetDatabase.GUIDToAssetPath(guid);
            var clip = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                // **止めない。** 器はクリップ無しでも成立する (Phase 1 の完了条件)。
                // 寝息が無ければ breathClip + ピッチの代用へ落ちるだけ (ランタイム側)
                log.AppendLine("   WARN " + label + " clip not found: guid=" + guid
                    + " - 無音 or 代用になる (器は動く)");
                return null;
            }

            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer != null)
            {
                var sample = importer.defaultSampleSettings;
                if (!importer.forceToMono || !sample.preloadAudioData)
                {
                    importer.forceToMono = true;
                    sample.preloadAudioData = true;
                    importer.defaultSampleSettings = sample;
                    importer.SaveAndReimport();
                    log.AppendLine("   fixed import settings (forceToMono / preloadAudioData): "
                        + System.IO.Path.GetFileName(path));
                }
            }
            log.AppendLine("   " + label + "=" + System.IO.Path.GetFileName(path)
                + " (" + clip.length.ToString("F2") + "s / loop 再生)");
            return clip;
        }

        /// <summary>犬定義の吠え声を読む。**ついでにインポート設定も正す** —
        /// ステレオのままだと 3D 音源で定位が鈍り、「どの犬が吠えたか分かる」が弱まる
        /// (設計書 §10 決定 6)。手作業に頼ると 4 匹のうち 1 つが必ず抜ける。</summary>
        static AudioClip LoadBark(KD_DogDef def, StringBuilder log)
        {
            if (string.IsNullOrEmpty(def.barkClipGuid))
            {
                log.AppendLine("   WARN barkClipGuid 未設定 - この犬は吠えない");
                return null;
            }
            string path = AssetDatabase.GUIDToAssetPath(def.barkClipGuid);
            var clip = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                log.AppendLine("   WARN bark clip not found: guid=" + def.barkClipGuid);
                return null;
            }

            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer != null)
            {
                // preloadAudioData は 2022.3 で AudioImporter 直下から
                // defaultSampleSettings へ移った (直下は obsolete)
                var sample = importer.defaultSampleSettings;
                if (!importer.forceToMono || !sample.preloadAudioData)
                {
                    importer.forceToMono = true;      // 3D 音源の定位
                    sample.preloadAudioData = true;   // 初回の 1 回だけ間に合わない事故を避ける
                    importer.defaultSampleSettings = sample;
                    importer.SaveAndReimport();
                    log.AppendLine("   fixed import settings (forceToMono / preloadAudioData): "
                        + System.IO.Path.GetFileName(path));
                }
            }
            log.AppendLine("   bark=" + System.IO.Path.GetFileName(path));
            return clip;
        }

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
            Finish("Logs/imp-dog-004-verify.txt", log);
        }

        static void Finish(string file, StringBuilder log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }
    }
}
