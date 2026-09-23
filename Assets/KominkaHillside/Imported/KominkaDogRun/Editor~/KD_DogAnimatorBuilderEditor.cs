using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-DOG-003: 自前 Animator Controller (AC_KD_Dog) のビルダーと検証。
    ///
    /// クリップは購入アセット (Assets/TORINOSUKE) から参照するだけで編集しない。
    /// 同名クリップが犬種フォルダごとに別物 (長さ・md5 が違う) なため、
    /// 解決は名前ではなく GUID で行い、長さの照合で取り違えを検出する。
    ///
    /// 撫でクリップは未選定 (目視選定待ち)。候補 10 本を 1D BlendTree に載せ、
    /// Play 中に PetIndex (Float 0-9) で切り替えて比較できるようにしてある。
    /// 選定後はこのビルダーの PetCandidates を確定クリップ 1 本へ書き換えて再実行する。
    ///
    /// IMP-FET-001: くわえモーションはどの犬種にも無い (設計書 §5) ため、
    /// 「頭を下げるポーズ」1 枚を自作する (0 - Generate Pickup Pose)。
    /// taiki 6 の先頭フレームを全ボーンへコピーし、胸・首・頭だけ前へ倒す。
    /// 倒す回転軸の符号は推測せず、鼻先 (アーマチュア側 Bone.007) の Y が
    /// 下がる方を実測で選ぶ (目視でなく数える — vrc-verification)。
    /// ポーズ角度を変えたいときはこのファイルの PoseBonePitchDeg を書き換えて
    /// 0 → 1 の順に再実行する (Controller は GUID 維持の in-place 再生成)。
    /// </summary>
    public static partial class KD_DogAnimatorBuilderEditor
    {
        // IMP-DOG-005: パス・プレハブ・クリップは KD_DogDefs が犬ごとに持つ。
        // **構成 (State / パラメータ / 遷移) は共通、参照するクリップだけ犬種ごと。**
        // クリップを共用できない理由: ボーンパス一致率は 100% でも、
        // **ルート位置カーブは体格そのもの** — 別体格のクリップを当てると犬が浮く/沈む
        // (2026-08-11 の ookami taiki 事故と同じ構造。RootPosAbsLimit がその検出器)。
        const float BlendSec = 0.25f;

        // IMP-FET-004 (2026-08-12 ユーザーフィードバック):
        // 「アニメーションはスムーズさより最後まで動いてる方が良い。急に止まる感じで
        //  いいから、動きが止まってからストップしてほしい」
        // 移動 (Walk/Run) の出入りは**ブレンドしない**。0.25s ブレンドだと、まだ動いている
        // 犬の脚が先に待機ポーズへ寄って「滑って」見える。止まった瞬間にカットする
        const float LocoBlendSec = 0f;
        // ただし走り ⇄ 歩きは別 — これは「止まるとき」ではなく速度が変わるだけなので、
        // カットすると帰路の途中で脚が飛ぶ。短くブレンドする
        const float RunWalkBlendSec = 0.12f;

        const float LenTol = 0.05f;

        // ルート (アーマチュア/Bone) 位置カーブの許容量。**レスト姿勢からのずれ**で測る。
        // 超えたクリップは Build が FAIL する (犬が浮く事故の構造的防止。
        // GUID + 長さ照合では検出できない層 — 2026-08-11 の実測)
        //
        // BUG-DOG-003: Armature is rotated -90 degrees about X. Local Z is vertical;
        // local Y is fore/aft, and also a useful (imperfect) rig-family fingerprint.
        // 2026-09-16 audit: 106 current clips, rise <= .05492 (Pome stretch),
        // sink/bodyRatio <= .2554 (Shiba sit); no clip or controller is modified.
        const float RootRiseLimit = .06f;
        const float RootSinkRatio = .28f;
        const float RootSinkFloor = .16f;
        const float RootPlaneRatio = 1f; // broad lateral/plane guard
        // Preserve the original fore/aft compatibility envelope independently of height.
        // Legitimate look-at: local Y -.106; borrowed Pome->Samoyed: -.117.
        // Incompatible A->B/D/C: -.153/-.166/-.270; reverse mixing: positive Y.
        const float RootForeFloor = .12f;
        const float RootForeRatio = .10f;
        const float RootAftLimit = .05f;

        // ---- クリップ定義 ----
        // IMP-DOG-005: 役割ごとのクリップは KD_DogDefs (犬種フォルダの実体) が持つ。
        // GUID + 長さ照合で取り違えを検出する仕組みはそのまま。

        /// <summary>診断専用。**Controller では使わない。**
        /// taiki (ookami) は 2026-08-11 に待機ローテから除外 — ルート (アーマチュア/Bone) の
        /// 位置カーブが狼の体格 (y≈0.277 / z≈0.40) で、根元の PositionConstraint 経由で
        /// 犬が浮く (Logs/imp-fet-001-rootmotion.txt)。**パス一致率 100% + 長さ照合でも
        /// ルート位置カーブの体格差は検出できない** — 8 - Diagnose で照合する。</summary>
        static readonly KD_ClipRef TaikiOokami =
            new KD_ClipRef("taiki (ookami) [診断専用・不採用]", "7c68111f534b5dc428f0a33722beb5d6", 12.0f);

        /// <summary>その犬の撫でブレンドツリーに載せるクリップ。
        /// 現在は 1 本 (なで1 で確定 — 2026-08-10 PCVR 第 1 回。
        /// 選定の経緯と落選理由は設計書 §5「撫でクリップの選定」)。</summary>
        static KD_ClipRef[] PetCandidatesOf(KD_DogDef def)
        {
            return new[] { def.pet };
        }

        /// <summary>Controller に載る全クリップ (空の枠は除く)。診断と照合で使う。</summary>
        static KD_ClipRef[] ControllerClipsOf(KD_DogDef def)
        {
            var list = new System.Collections.Generic.List<KD_ClipRef>
            {
                def.idle, def.idleVar1, def.idleVar2, def.idleVar3,
                EffectiveSit(def), EffectiveLieDown(def), def.walk, def.run, def.lookAt, def.pet,
                def.sleepLoop, def.sleepInOut,
            };
            list.RemoveAll(c => c.IsEmpty);
            return list.ToArray();
        }
        static KD_ClipRef EffectiveSit(KD_DogDef def)
        {
            var clip=KD_CallPoseEditor.SitClip(def);
            return new KD_ClipRef(clip.name,AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip)),clip.length);
        }

        static readonly string[] BoolParams =
            { "Sit", "LieDown", "Walk", "Run", "LookAt", "Pet", "Pickup", SleepParam };
        const string PetIndexParam = "PetIndex";

        // ---- そばで寝る (IMP-DOG-019 Phase 1) ----

        /// <summary>そばで寝るモードの Bool。**立っている間の Bool とは性格が違う** —
        /// 他の Bool は「今この瞬間の動き」だが、これは**人が解除するまで続く占有**。
        /// したがって Animator 上でも最上位に置く (下記 <see cref="BuildFor"/> の遷移)。</summary>
        const string SleepParam = "Sleep";

        /// <summary>`SleepIn` → `Sleep` はブレンドしない。
        /// **`寝なで_ONOFF` の末尾フレームが `寝なでなで` の先頭フレームと完全に一致する**
        /// (4 犬種とも最大差 0.0000 — KD_DogDefs.sleepInOut の実測) ので、
        /// 繋ぎ目にブレンドを入れる理由が無い。入れるとむしろ 0.25 秒ぶん動きが鈍る。</summary>
        const float SleepInBlendSec = 0f;

        // ---- State 数の内訳 (Build と Verify が同じ数を見るためここへ集約) ----

        /// <summary>Idle 系以外のリアクション State (Pickup/Run/Walk/Sit/LieDown/LookAt/Pet)。</summary>
        const int ReactionStateCount = 7;
        /// <summary>上から Run / Walk を除いた数 (静止リアクション)。</summary>
        const int StaticReactionCount = 5;
        /// <summary>そばで寝る State (SleepIn / Sleep)。IMP-DOG-019 Phase 1。</summary>
        const int SleepStateCount = 2;

        // ---- 自作ポーズ (IMP-FET-001)。犬種ごとに生成する ----
        const float PoseLength = 1f;
        // 前へ倒すボーンと角度 (度)。合計 55° で鼻先が地面近くまで下がる想定。
        // 実際に下がったかは Generate が鼻先 Y の実測でログに出す
        static readonly string[] PoseBones = { "Bone.002", "Bone.003", "Bone.004" };
        static readonly float[] PoseBonePitchDeg = { 8f, 22f, 25f };

        // ================================================================ Build

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Controller/1 - Build AC_KD_Dog")]
        public static void Build()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogAnimatorBuilder] Build start");

            bool allOk = true;
            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("========= " + def.displayName + " (" + def.key + ") =========");
                if (!BuildFor(def, log)) allOk = false;
            }

            log.AppendLine(allOk ? "BUILD RESULT: PASS" : "BUILD RESULT: FAIL");
            KD_VerifyLog.Write("Logs/imp-dog-003-build.txt", log);
        }

        static bool BuildFor(KD_DogDef dogDef, StringBuilder log)
        {
            bool ok = true;

            // ルート位置の判定基準 (犬種でレストも体格も違う — IMP-DOG-006 / 007)
            Vector3 rootRest;
            float bodyRatio;
            if (!TryResolveRootRest(dogDef, log, out rootRest, out bodyRatio)) return false;

            AnimationClip Load(KD_ClipRef def)
            {
                if (def.IsEmpty) return null; // 任意枠 (待機バリエーション) は空でよい
                var clip = LoadClip(def, log);
                if (clip == null) { ok = false; return null; }
                if (!RootPositionSafe(clip, def.label, rootRest, bodyRatio, log)) ok = false;
                return clip;
            }

            var idle = Load(dogDef.idle);
            var osuwari = Load(EffectiveSit(dogDef));
            // BUG-DOG-002: 派生する犬は生成物 KD_LieDown_{key} (購入クリップのカーブ打ち間違いを直したもの)
            var huse = Load(EffectiveLieDown(dogDef));
            var aruku = Load(dogDef.walk);
            var hasiru = Load(dogDef.run);
            var taimen = Load(dogDef.lookAt);
            // そばで寝る (IMP-DOG-019 Phase 1)。**huse (伏せ) とは別物**で、伏せは一切触らない。
            // ルート位置は他のクリップと同じ検出器を通す — 実測では 4 犬種とも
            // local Y は前後、local Z は上下。寝姿勢の下降も通常の高さ判定へ含める
            var neLoop = Load(dogDef.sleepLoop);
            var neInOut = Load(dogDef.sleepInOut);
            // BUG-DOG-002: 姿勢を低くする 4 本にルート位置カーブが無ければ知らせる (FAIL ではなく WARN)
            WarnIfNoRootPositionCurve(osuwari, dogDef.sit.label, log);
            WarnIfNoRootPositionCurve(huse, EffectiveLieDown(dogDef).label, log);
            WarnIfNoRootPositionCurve(neLoop, dogDef.sleepLoop.label, log);
            WarnIfNoRootPositionCurve(neInOut, dogDef.sleepInOut.label, log);
            // 待機バリエーション。**別体格のクリップはルート位置カーブで弾かれる**
            // (RootPositionSafe。taiki (ookami) を除外したのと同じ検出器)
            var idleVarList = new System.Collections.Generic.List<AnimationClip>();
            foreach (var v in new[] { dogDef.idleVar1, dogDef.idleVar2, dogDef.idleVar3 })
            {
                var c = Load(v);
                if (c != null) idleVarList.Add(c);
            }
            var idleVars = idleVarList.ToArray();

            var petRefs = PetCandidatesOf(dogDef);
            var petClips = new AnimationClip[petRefs.Length];
            for (int i = 0; i < petRefs.Length; i++) petClips[i] = Load(petRefs[i]);

            // 自作のくわえポーズ (IMP-FET-001)。GUID 定数を持たない (生成物なので) — パスで解決する
            var pickupPose = AssetDatabase.LoadAssetAtPath<AnimationClip>(dogDef.PickupPosePath);
            if (pickupPose == null || Mathf.Abs(pickupPose.length - PoseLength) > LenTol)
            {
                log.AppendLine($"FAIL pickup pose missing or wrong length: {dogDef.PickupPosePath}" +
                    (pickupPose == null ? " (0 - Generate Pickup Pose を先に実行する)" : $" len={pickupPose.length}"));
                ok = false;
            }
            else if (!RootPositionSafe(pickupPose, "KD_PickupPose_" + dogDef.key, rootRest, bodyRatio, log))
            {
                ok = false;
            }

            if (!ok)
            {
                log.AppendLine("  build skipped (clip resolution failed)");
                return false;
            }

            if (!AssetDatabase.IsValidFolder("Assets/KominkaHillside/Imported/KominkaDogRun/Animations"))
                AssetDatabase.CreateFolder("Assets/KominkaHillside/Imported/KominkaDogRun", "Animations");

            var ctrl = GetOrResetController(dogDef.ControllerPath, log);

            foreach (var p in BoolParams)
                ctrl.AddParameter(p, AnimatorControllerParameterType.Bool);
            ctrl.AddParameter(PetIndexParam, AnimatorControllerParameterType.Float);

            var sm = ctrl.layers[0].stateMachine;

            // ---- 状態 ----
            var sIdle = sm.AddState("Idle", new Vector3(0, 0));
            sIdle.motion = idle;
            // 待機バリエーションは可変長 (犬種によって使える本数が違う)。
            // State 名は犬種非依存にする — **実行時は Bool パラメータしか見ないので
            // State 名を変えても挙動は変わらない** (Scripts/ に IsName / StringToHash は無い)
            var idleFamily = new AnimatorState[1 + idleVars.Length];
            idleFamily[0] = sIdle;
            for (int i = 0; i < idleVars.Length; i++)
            {
                var s = sm.AddState("Idle_var" + (i + 1), new Vector3(0, 80 * (i + 1)));
                s.motion = idleVars[i];
                idleFamily[i + 1] = s;
            }

            var sSit = sm.AddState("Sit", new Vector3(320, 0));
            sSit.motion = osuwari;
            var sLie = sm.AddState("LieDown", new Vector3(320, 80));
            sLie.motion = huse;
            var sWalk = sm.AddState("Walk", new Vector3(320, 160));
            sWalk.motion = aruku;
            var sRun = sm.AddState("Run", new Vector3(320, 240));
            sRun.motion = hasiru;
            var sLook = sm.AddState("LookAt", new Vector3(320, 320));
            sLook.motion = taimen;

            var sPickup = sm.AddState("Pickup", new Vector3(320, 480));
            sPickup.motion = pickupPose;

            var sPet = sm.AddState("Pet", new Vector3(320, 400));
            var tree = new BlendTree
            {
                name = "PetCandidates",
                blendType = BlendTreeType.Simple1D,
                blendParameter = PetIndexParam,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, ctrl);
            for (int i = 0; i < petClips.Length; i++)
                tree.AddChild(petClips[i], i);
            sPet.motion = tree;

            // ---- そばで寝る (IMP-DOG-019 Phase 1) ----
            // **2 State に割る。** 出入り (`寝なで_ONOFF` 0.58s) を本編ループの前に 1 回だけ
            // 通す形で、`SleepIn` は 1 回きり再生の中継。実測 (KD_DogDefs.sleepInOut) では
            //   ・先頭フレーム = 待機 (taiki) の先頭フレーム
            //   ・末尾フレーム = 本編 (寝なでなで) の先頭フレーム
            // なので、待機から入って本編へ抜けるまで**ポーズの飛びが 1 箇所も無い**
            var sSleepIn = sm.AddState("SleepIn", new Vector3(640, 0));
            sSleepIn.motion = neInOut;
            var sSleep = sm.AddState("Sleep", new Vector3(640, 80));
            sSleep.motion = neLoop;

            sm.defaultState = sIdle;

            // ---- Idle 系 ⇄ リアクション (どの待機 State からでも即応する) ----
            // リアクション遷移を待機ローテーションより先に追加する (同一 State 内の遷移は
            // 追加順に評価されるため。逆にするとローテーションの exit time 到達フレームで
            // リアクションが負けて最大 1 ブレンド分遅れる)。
            //
            // ここの並びは「同時に複数の Bool が立ったとき、待機 State からどれへ出るか」の
            // 評価順でしかない。リアクション同士の直接遷移は無く (Idle 経由のみ)、
            // 排他管理と「Fetch が撫でに勝つ」調停はスクリプト側の責務 (設計書 §1)。
            var reactions = new[]
            {
                (state: sPickup, param: "Pickup"), // Fetch のくわえ/置きは他のどの Bool より先に評価する
                (state: sRun, param: "Run"),
                (state: sWalk, param: "Walk"),
                (state: sSit, param: "Sit"),
                (state: sLie, param: "LieDown"),
                (state: sLook, param: "LookAt"),
                (state: sPet, param: "Pet"),
            };

            // ---- そばで寝るは最上位 (IMP-DOG-019 Phase 1) ----
            //
            // **どの State に居ても、`Sleep` が立った瞬間に SleepIn へ抜ける。**
            // 同一 State の遷移は追加順に評価されるので、**他のどの遷移より先にここで足す**
            // (Run ⇄ Walk の直結や `!Pickup` の Idle 行きより後ろに置くと、
            //  移動中や Pickup 中に呼ばれた犬が 1 手余計に回る)。
            //
            // **逆向き (Sleep から出る) の遷移は 1 本だけ**にしてある —
            // `Sleep` / `SleepIn` からリアクションへ出る道を 1 本も作らないことで、
            // 「寝ている間は Pet でも Fetch でも起きない」が**条件の書き忘れではなく
            // State グラフの形として**保証される。Phase 4 の「寝たまま撫でられる」は
            // この Sleep State の**上**で作る (`Pet=true` で Sleep から出てはいけない)。
            var sleepSources = new System.Collections.Generic.List<AnimatorState>(idleFamily);
            foreach (var (state, param) in reactions) sleepSources.Add(state);
            foreach (var src in sleepSources)
            {
                var t = src.AddTransition(sSleepIn);
                // 移動から入るときだけカット (走っている脚が寝姿勢へ寄って滑るのを断つ。
                // IMP-FET-004 と同じ理由)
                SetupTransition(t, hasExitTime: false,
                    (src == sRun || src == sWalk) ? LocoBlendSec : BlendSec);
                t.AddCondition(AnimatorConditionMode.If, 0, SleepParam);
            }

            // 出入りの途中で解除されたら、寝るのをやめて待機へ戻る。
            // **条件付きなので exit time 側より先に足す** (後だと 0.58s 立ち上がりを待たされる)
            var tSleepCancel = sSleepIn.AddTransition(sIdle);
            SetupTransition(tSleepCancel, hasExitTime: false, BlendSec);
            tSleepCancel.AddCondition(AnimatorConditionMode.IfNot, 0, SleepParam);

            // 出入りを再生し切ったら本編ループへ。**ブレンド 0** (繋ぎ目が無いため)
            var tSleepEnter = sSleepIn.AddTransition(sSleep);
            SetupTransition(tSleepEnter, hasExitTime: true, SleepInBlendSec);

            // **Sleep からの出口はこの 1 本だけ。**
            // 起き上がりの絵 (`寝なで_ONOFF` の逆再生) は Phase 1 では作らない —
            // 負の speed の State は exit time で抜けられず、条件で抜ける口も無いため、
            // 確実に動く形が Play 無しでは決められない (逆再生クリップの生成は Phase 2 以降)
            var tSleepExit = sSleep.AddTransition(sIdle);
            SetupTransition(tSleepExit, hasExitTime: false, BlendSec);
            tSleepExit.AddCondition(AnimatorConditionMode.IfNot, 0, SleepParam);

            foreach (var src in idleFamily)
            {
                foreach (var (state, param) in reactions)
                {
                    var t = src.AddTransition(state);
                    SetupTransition(t, hasExitTime: false, LocoDuration(param));
                    t.AddCondition(AnimatorConditionMode.If, 0, param);
                }
            }

            // ---- 待機ローテーション (1 ループ再生し切ったら次へ) ----
            for (int i = 0; i < idleFamily.Length; i++)
            {
                var next = idleFamily[(i + 1) % idleFamily.Length];
                var t = idleFamily[i].AddTransition(next);
                SetupTransition(t, hasExitTime: true);
            }

            // ---- 走り ⇄ 歩き は直結する (IMP-FET-004) ----
            // Idle 経由 (Run→Idle→Walk) だと待機ポーズが 1 フレーム挟まって脚が飛ぶ。
            // 帰路の減速で必ず通る経路なので直接つなぐ。**戻り遷移より先に追加する**
            // (同一 State の遷移は追加順に評価される — 後だと Run→Idle が先に勝つ)
            var tRunToWalk = sRun.AddTransition(sWalk);
            SetupTransition(tRunToWalk, hasExitTime: false, RunWalkBlendSec);
            tRunToWalk.AddCondition(AnimatorConditionMode.If, 0, "Walk");
            var tWalkToRun = sWalk.AddTransition(sRun);
            SetupTransition(tWalkToRun, hasExitTime: false, RunWalkBlendSec);
            tWalkToRun.AddCondition(AnimatorConditionMode.If, 0, "Run");

            // ---- リアクション → 移動 も直結する (破壊的監査 RC-4d) ----
            // **Run ⇄ Walk を直結した理由 (IMP-FET-004) は、静止ポーズ側にも同じだけ効く。**
            // Pickup / Pet / LookAt から歩き出すとき、経路は
            // `Pickup →(BlendSec 0.25s)→ Idle →(LocoBlendSec 0)→ Walk` になる。
            // その 0.25 秒のあいだ脚は待機ポーズへ寄っていくのに、スクリプトは
            // 減速し切った先で位置を書き続けるので、**くわえて向き直る瞬間に 17〜19cm 滑る**
            // (BUG-DOG-001 が潰した「静止ポーズなのに進む」の再発形。Fetch 1 周ごとに必ず出る)。
            // 撫で明けに歩き出す / じゃれ合いの対面から離れる、も同じ形。
            //
            // **→Idle の戻り遷移より先に追加する** — 同一 State の遷移は追加順に評価されるので、
            // 後ろに置くと `!Pickup` の Idle 行きが先に勝ってブレンドが復活する。
            foreach (var (state, param) in reactions)
            {
                if (state == sRun || state == sWalk) continue; // 移動同士は上で直結済み
                // **ブレンドは 0 (カット)。** Run ⇄ Walk の 0.12 秒は「歩き↔走りという
                // 別々の移動クリップを繋ぐ」ための値で、こちらは**静止ポーズから移動へ**
                // 出る遷移なので、0.12 でも「止まった絵のまま進む」時間がそのぶん残る。
                // Verify の ExpectedDuration も「移動の出入りは LocoBlendSec」と規定している
                var tr = state.AddTransition(sRun);
                SetupTransition(tr, hasExitTime: false, LocoBlendSec);
                tr.AddCondition(AnimatorConditionMode.If, 0, "Run");
                var tw = state.AddTransition(sWalk);
                SetupTransition(tw, hasExitTime: false, LocoBlendSec);
                tw.AddCondition(AnimatorConditionMode.If, 0, "Walk");
            }

            foreach (var (state, param) in reactions)
            {
                var t = state.AddTransition(sIdle);
                SetupTransition(t, hasExitTime: false, LocoDuration(param));
                t.AddCondition(AnimatorConditionMode.IfNot, 0, param);
            }

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();

            KD_CallPoseEditor.ConfigureAnimator(dogDef, ctrl);
            if (dogDef.key == "Pome" && AssetDatabase.LoadAssetAtPath<AnimationClip>(KD_PumpkinPreviewEditor.MountHopPath) != null)
                KD_PumpkinPreviewEditor.ConfigureMountLayer(ctrl, idle);
            AssetDatabase.SaveAssets();
            log.AppendLine($"controller: {dogDef.ControllerPath}");
            log.AppendLine($"states={sm.states.Length} params={ctrl.parameters.Length}" +
                $" idleVars={idleVars.Length} petCandidates={tree.children.Length}");
            return true;
        }

        /// <summary>移動 (Walk/Run) の出入りはブレンドしない (IMP-FET-004)。それ以外は既定。</summary>
        static float LocoDuration(string param)
        {
            return (param == "Run" || param == "Walk") ? LocoBlendSec : BlendSec;
        }

        static void SetupTransition(AnimatorStateTransition t, bool hasExitTime)
        {
            SetupTransition(t, hasExitTime, BlendSec);
        }

        static void SetupTransition(AnimatorStateTransition t, bool hasExitTime, float duration)
        {
            t.hasExitTime = hasExitTime;
            t.exitTime = 1f; // hasExitTime のときだけ効く (= 1 ループ再生し切る)
            t.hasFixedDuration = true;
            t.duration = duration;
            t.offset = 0f;
            t.interruptionSource = TransitionInterruptionSource.None;
        }

        /// <summary>ルート (アーマチュア/Bone) の位置カーブが体格違いの値を持っていないか。
        /// 2 段構えでは根元の PositionConstraint がここをメッシュへ写し、1 段構えでは
        /// ここが表示骨格そのものなので、いずれにせよ別犬種の体格値が入っていると
        /// 再生中だけ犬が浮く (実例: ookami taiki の y≈0.277)。
        /// Bone.018 (尻尾) 等は Constraint が位置を写さないので見ない。
        ///
        /// <paramref name="rootRest"/> はその犬のプレハブでの アーマチュア/Bone の
        /// レスト localPosition。**判定はここからのずれで行う** (犬種でレストが違うため)。</summary>
        static bool RootPositionSafe(AnimationClip clip, string label, Vector3 rootRest,
            float bodyRatio, StringBuilder log)
        {
            float sinkLimit = Mathf.Max(RootSinkFloor, RootSinkRatio * bodyRatio);
            float planeLimit = RootPlaneRatio * bodyRatio;

            // BUG-DOG-002: 骨格の外 (アーマチュア/ 配下でない) に位置カーブがある = ルートに打つべきカーブの打ち間違い。
            // inu の ParentConstraint に遮られて骨格へ届かないので、意図した沈みが消えて**浮く** (柴 huse の hojo_born/Bone)。
            // 「ずれ過大」の検査では**無い**カーブは見えないため、ここで名指しで落とす
            string outside = PositionCurveOutsideSkeleton(clip);
            if (outside != null)
            {
                log.AppendLine($"FAIL position curve outside skeleton: {label} path='{outside}' — " +
                    $"{RootBoneSuffix} に打つべき位置カーブが別ボーンにある (効かないので犬が浮く)。" +
                    "派生クリップで移す (KD_DogDef.lieDownRootCurveSourcePath。BUG-DOG-002)");
                return false;
            }

            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (!b.propertyName.StartsWith("m_LocalPosition")) continue;
                if (!b.path.EndsWith(RootBoneSuffix)) continue;

                bool vertical;
                float rest;
                if (b.propertyName.EndsWith(".z")) { vertical = true; rest = rootRest.z; }
                else if (b.propertyName.EndsWith(".x")) { vertical = false; rest = rootRest.x; }
                else if (b.propertyName.EndsWith(".y")) { vertical = false; rest = rootRest.y; }
                else
                {
                    // 軸が読めないカーブ (想定外の表現)。黙って通さず知らせる
                    log.AppendLine($"FAIL unexpected root position binding: {label} {b.propertyName}");
                    return false;
                }

                var c = AnimationUtility.GetEditorCurve(clip, b);
                if (c == null) continue;
                foreach (float value in RootCurveValues(c))
                {
                    float d = value - rest;
                    string why = null;
                    if (vertical && d > RootRiseLimit)
                        why = $"犬が浮く方向へ {d:F3} (許容 {RootRiseLimit:F3})。" +
                              "**別リグ family のクリップ疑い** — フォルダ名は当てにならない";
                    else if (vertical && -d > sinkLimit)
                        why = $"犬が沈む方向へ {-d:F3} (許容 {sinkLimit:F3} = 体格比)";
                    else if (b.propertyName.EndsWith(".y") && (d > RootAftLimit || -d > Mathf.Max(RootForeFloor, RootForeRatio * bodyRatio)))
                        why = $"前後(local Y)のリグ互換範囲外 {d:F3}。高さとは独立したfamily混入の疑い";
                    else if (!vertical && Mathf.Abs(d) > planeLimit)
                        why = $"接地面内へ {Mathf.Abs(d):F3} (許容 {planeLimit:F3} = 体格比)";
                    if (why == null) continue;

                    log.AppendLine($"FAIL root position out of range: {label} {b.propertyName}" +
                        $" value={value:F3} rest={rest:F3} — {why}。" +
                        "6 - Diagnose Idle Root Motion で確認する");
                    return false;
                }
            }
            return true;
        }

        // AnimationCurve segments are cubic Beziers (including weighted tangents).
        // Check their Y extrema as well as keys; safe endpoints can still overshoot between keys.
        static System.Collections.Generic.IEnumerable<float> RootCurveValues(AnimationCurve curve)
        {
            var keys = curve.keys;
            foreach (var key in keys) yield return key.value;
            for (int i = 1; i < keys.Length; i++)
            {
                var left = keys[i-1]; var right = keys[i]; double dt = right.time-left.time;
                if (dt <= 0 || float.IsInfinity(left.outTangent) || float.IsInfinity(right.inTangent)) continue;
                double p0 = left.value, p3 = right.value;
                double p1 = p0 + left.outTangent * dt * ((left.weightedMode & WeightedMode.Out) != 0 ? left.outWeight : 1.0/3);
                double p2 = p3 - right.inTangent * dt * ((right.weightedMode & WeightedMode.In) != 0 ? right.inWeight : 1.0/3);
                double a = -p0+3*p1-3*p2+p3, b = 2*(p0-2*p1+p2), c = p1-p0;
                double discriminant = b*b-4*a*c;
                if (System.Math.Abs(a) < 1e-12)
                {
                    if (System.Math.Abs(b) > 1e-12) { double t = -c/b; if (t > 0 && t < 1) yield return BezierValue(p0,p1,p2,p3,t); }
                }
                else if (discriminant >= 0)
                {
                    double root = System.Math.Sqrt(discriminant);
                    double t = (-b+root)/(2*a); if (t > 0 && t < 1) yield return BezierValue(p0,p1,p2,p3,t);
                    t = (-b-root)/(2*a); if (t > 0 && t < 1) yield return BezierValue(p0,p1,p2,p3,t);
                }
            }
        }
        static float BezierValue(double a,double b,double c,double d,double t)
        {
            double u=1-t; return (float)(u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d);
        }

        /// <summary>その犬のプレハブでの アーマチュア/Bone のレスト localPosition。
        /// 解決できなければ false を返す (**0 で代用しない** — 代用するとポメで
        /// 全クリップが体格違いと誤判定される)。</summary>
        static bool TryResolveRootRest(KD_DogDef def, StringBuilder log, out Vector3 rootRest,
            out float bodyRatio)
        {
            rootRest = Vector3.zero;
            bodyRatio = 1f;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(def.prefabGuid));
            if (prefab == null)
            {
                log.AppendLine($"FAIL dog prefab not found guid={def.prefabGuid} (root rest が測れない)");
                return false;
            }
            var temp = Object.Instantiate(prefab);
            temp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var rig = KD_DogRig.Resolve(temp);
                var bone = rig.srcSkelRoot != null ? rig.srcSkelRoot.Find("Bone") : null;
                if (bone == null)
                {
                    log.AppendLine("FAIL アーマチュア/Bone が見つからない (root rest が測れない)");
                    return false;
                }
                rootRest = bone.localPosition;
                bodyRatio = rig.BodyRatio;
                log.AppendLine($"root rest (アーマチュア/Bone) = {rootRest:F4}" +
                    $" / bodyScale = {rig.bodyScale:F4} (体格比 {bodyRatio:F2})");
                return true;
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        static AnimationClip LoadClip(KD_ClipRef def, StringBuilder log)
        {
            var path = AssetDatabase.GUIDToAssetPath(def.guid);
            if (string.IsNullOrEmpty(path))
            {
                log.AppendLine($"FAIL clip not found: {def.label} guid={def.guid}");
                return null;
            }
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                log.AppendLine($"FAIL not an AnimationClip: {def.label} path={path}");
                return null;
            }
            if (Mathf.Abs(clip.length - def.length) > LenTol)
            {
                log.AppendLine($"FAIL length mismatch (取り違え疑い): {def.label} expected={def.length} actual={clip.length} path={path}");
                return null;
            }
            log.AppendLine($"ok {def.label} len={clip.length:F2} loop={clip.isLooping} path={path}");
            return clip;
        }

        /// <summary>既存 Controller は GUID を保つため削除せず中身だけ作り直す (シーン参照を切らない)。</summary>
        static AnimatorController GetOrResetController(string path, StringBuilder log)
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ctrl == null)
            {
                log.AppendLine("creating new controller asset");
                return AnimatorController.CreateAnimatorControllerAtPath(path);
            }

            log.AppendLine("resetting existing controller in place (GUID 維持)");
            while (ctrl.layers.Length > 0) ctrl.RemoveLayer(0);
            ctrl.parameters = new AnimatorControllerParameter[0];
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o == null || o == ctrl) continue;
                Object.DestroyImmediate(o, true);
            }
            ctrl.AddLayer("Base Layer");
            return ctrl;
        }

        // ================================================================ Place dog

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Controller/3 - Place Dog In Scene")]
        public static void PlaceDog()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogAnimatorBuilder] Place dog");

            bool allOk = true;
            int placed = 0;
            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("-- " + def.displayName + " (" + def.key + ")");
                if (PlaceDogFor(def, log)) placed++;
                else allOk = false;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("dogs=" + placed + "/" + KD_DogDefs.All.Length);
            log.AppendLine(allOk
                ? "PLACE RESULT: PASS (scene not saved yet)"
                : "PLACE RESULT: FAIL");
            KD_VerifyLog.Write("Logs/imp-dog-003-place.txt", log);
        }

        static bool PlaceDogFor(KD_DogDef def, StringBuilder log)
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(def.ControllerPath);
            if (ctrl == null)
            {
                log.AppendLine("   FAIL controller missing: " + def.ControllerPath + " (Run Build first)");
                return false;
            }

            var prefabPath = AssetDatabase.GUIDToAssetPath(def.prefabGuid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                log.AppendLine($"   FAIL dog prefab not found guid={def.prefabGuid}");
                return false;
            }

            // 既存があれば位置は触らない (人間の調整を潰さない)。配線だけ揃える
            int dogCount;
            var dog = KD_SceneObjects.Find(def.DogName, out dogCount);
            if (dogCount > 1)
            {
                log.AppendLine($"   FAIL '{def.DogName}' がシーン内に {dogCount} 個ある");
                return false;
            }
            if (dog == null)
            {
                // 名前が付いていないだけの既存インスタンスを拾う。
                // **他の犬として既に使われているものは拾わない** (多頭で取り違えると悲惨)。
                // IMP-INF-003: ルート限定だと階層化後に取りこぼすのでシーン全体から探す
                dog = KD_SceneObjects.FindByPrefabSource(prefab, go => IsClaimedByAnotherDog(go, def));
                if (dog != null)
                {
                    dog.name = def.DogName;
                    log.AppendLine("   reused existing prefab instance -> " + def.DogName);
                }
            }
            else
            {
                log.AppendLine("   reused existing " + def.DogName + " (position untouched)");
            }

            if (dog == null)
            {
                dog = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                dog.name = def.DogName;
                KD_SceneObjects.PlaceIfNew(dog, def.UnitPath);   // IMP-INF-003: 犬ユニットへ

                // 実績のある配置: spawns[0] の前方 1.3m、犬の +Z をプレイヤー (スポーン) へ向ける。
                // 多頭のときは横に並べる (重ならないように)。定位置は人間が動かす前提
                var desc = Object.FindObjectOfType<VRCSceneDescriptor>();
                if (desc == null)
                {
                    log.AppendLine("   FAIL VRCSceneDescriptor not found");
                    Object.DestroyImmediate(dog);
                    return false;
                }
                var spawn = (desc.spawns != null && desc.spawns.Length > 0 && desc.spawns[0] != null)
                    ? desc.spawns[0] : desc.transform;
                var fwd = spawn.forward; fwd.y = 0f; fwd.Normalize();
                if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
                var right = Vector3.Cross(Vector3.up, fwd).normalized;
                int index = System.Array.FindIndex(KD_DogDefs.All, d => d.key == def.key);
                dog.transform.position = spawn.position + fwd * 1.3f + right * (index * 1.5f);
                dog.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
                log.AppendLine($"   placed new instance at {dog.transform.position} (spawn={spawn.name}, index={index}。手で動かしてよい)");
            }

            var animator = dog.GetComponent<Animator>();
            if (animator == null) animator = dog.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                log.AppendLine("   FAIL Animator not found on dog");
                return false;
            }
            animator.runtimeAnimatorController = ctrl;
            animator.enabled = true; // IMP-DOG-003 完了条件: 無効だと State が再生されない

            EditorUtility.SetDirty(animator);
            log.AppendLine($"   animator.controller={animator.runtimeAnimatorController.name} enabled={animator.enabled}");
            return true;
        }

        /// <summary>そのオブジェクトが別の犬定義の名前を持っているか (多頭での取り違え防止)。</summary>
        static bool IsClaimedByAnotherDog(GameObject go, KD_DogDef self)
        {
            foreach (var d in KD_DogDefs.All)
                if (d.key != self.key && go.name == d.DogName) return true;
            return false;
        }
    }
}
