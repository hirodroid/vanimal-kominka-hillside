using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-ENV-004: おじいさん NPC の Animator Controller (`AC_KD_Npc`) を生成する。
    ///
    /// **polyperfect 同梱の Controller は使わない** (`vrc-animator-reactions` §2) —
    /// man_farm に刺さっているのはデモシーン用の override で、既定 State が移動系
    /// (呼んでもいないのに歩き出す)。しかも購入アセットは `.gitignore` 対象なので
    /// 編集してもコミットできない。**自前の最小 Controller を生成し、
    /// クリップだけを購入アセットから GUID で参照する。**
    ///
    /// State は 2 つだけ:
    /// <code>
    /// Idle (既定)  Idle_Generic 4.875s ループ
    /// Wave         Idle_Waving  3.875s — hasExitTime で 1 ループ再生し切ったら Idle へ戻る
    /// </code>
    ///
    /// **Wave は v1 では誰も叩かない** (npc-shop-menu.md §11 の非スコープ)。
    /// 駆動するときは Bool `Wave` を立て、**クリップ長以内に下ろす**こと —
    /// クリップは loopTime:1 なので、立てっぱなしだと Idle へ戻った直後に再遷移して
    /// 手を振り続ける (`vrc-animator-reactions` §3 の「true → N 秒後に false」)。
    /// </summary>
    public static class KD_NpcAnimatorBuilderEditor
    {
        public const string ControllerPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/AC_KD_Npc.controller";

        public const string WaveParam = "Wave";

        /// <summary>Idle ⇄ Wave のブレンド (秒)。挨拶の所作なので既定の 0.25 でよい。</summary>
        const float BlendSec = 0.25f;

        /// <summary>クリップ長の照合トレランス (取り違え検出 — `vrc-animator-reactions` §2.5)。</summary>
        const float LenTol = 0.05f;

        // クリップは GUID + 実測長で参照する。**同名クリップは同一物と限らない** —
        // パス・ファイル名は当てにせず、長さのずれ = 取り違えとして FAIL にする。
        // 実測 2026-09-01 (Assets/polyperfect/Low Poly Animated People/- Animations/In Place/Idles/)
        const string IdleGuid = "cfa835ec428fbc04487000a50aa4cf1b"; // Idle_Generic.fbx
        const float IdleLen = 4.875f;
        const string WaveGuid = "f8b68f5841b9ede44aafb7d0fa71dc2e"; // Idle_Waving.fbx
        const float WaveLen = 3.875f;

        // ================================================================ Build

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Npc Controller/1 - Build AC_KD_Npc")]
        public static void Build()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_NpcAnimatorBuilder] Build start");

            var idle = LoadClip(IdleGuid, IdleLen, "Idle_Generic", log);
            var wave = LoadClip(WaveGuid, WaveLen, "Idle_Waving", log);
            if (idle == null || wave == null)
            {
                log.AppendLine("BUILD RESULT: FAIL (clip resolution failed)");
                Finish("Logs/imp-env-004-npc-build.txt", log);
                return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/KominkaHillside/Imported/KominkaDogRun/Animations"))
                AssetDatabase.CreateFolder("Assets/KominkaHillside/Imported/KominkaDogRun", "Animations");

            var ctrl = GetOrResetController(ControllerPath, log);
            ctrl.AddParameter(WaveParam, AnimatorControllerParameterType.Bool);

            var sm = ctrl.layers[0].stateMachine;
            var sIdle = sm.AddState("Idle", new Vector3(0, 0));
            sIdle.motion = idle;
            var sWave = sm.AddState("Wave", new Vector3(320, 0));
            sWave.motion = wave;
            sm.defaultState = sIdle;

            // Idle → Wave (Bool)。即応させたいのでブレンドのみ・exit time 無し
            var tIn = sIdle.AddTransition(sWave);
            SetupTransition(tIn, hasExitTime: false);
            tIn.AddCondition(AnimatorConditionMode.If, 0, WaveParam);

            // Wave → Idle は **1 ループ再生し切ったら戻る** (犬の待機ローテーションと同じ形)。
            // 条件を付けない — 「振り終わったら戻る」はクリップ長が決めること。
            // **ただし Bool が true のままだと Idle へ戻った直後に再遷移して振り続ける**
            // (無条件 exit は「必ず Idle を経由する」を保証するだけで、
            // 振りっぱなしは防げない — 2026-09-01 Codex 指摘で断定を訂正)。
            // 駆動側がクリップ長以内に必ず下ろすこと (クラスコメント参照)
            var tOut = sWave.AddTransition(sIdle);
            SetupTransition(tOut, hasExitTime: true);

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();

            log.AppendLine($"controller: {ControllerPath}");
            log.AppendLine($"states={sm.states.Length} params={ctrl.parameters.Length}");
            log.AppendLine("BUILD RESULT: PASS");
            Finish("Logs/imp-env-004-npc-build.txt", log);
        }

        // ================================================================ Verify

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Npc Controller/2 - Verify AC_KD_Npc")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_NpcAnimatorBuilder] Verify");
            int fail = 0;
            void Check(bool cond, string what)
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + what);
                if (!cond) fail++;
            }

            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Check(ctrl != null, "controller asset: " + ControllerPath);
            if (ctrl != null)
            {
                Check(ctrl.parameters.Length == 1 && ctrl.parameters[0].name == WaveParam
                    && ctrl.parameters[0].type == AnimatorControllerParameterType.Bool,
                    "param: Bool " + WaveParam + " だけ");
                Check(ctrl.layers.Length == 1, "layer 1 枚だけ");
                if (ctrl.layers.Length == 0)
                {
                    // 破損 (layer 0 枚) で以降が IndexOutOfRange にならないよう打ち切る
                    log.AppendLine("VERIFY RESULT: FAIL (layer が無い — 1 - Build を再実行)");
                    Finish("Logs/imp-env-004-npc-verify.txt", log);
                    return;
                }

                var sm = ctrl.layers[0].stateMachine;
                Check(sm.states.Length == 2, "state 2 個 (Idle / Wave)");

                AnimatorState sIdle = null, sWave = null;
                foreach (var cs in sm.states)
                {
                    if (cs.state.name == "Idle") sIdle = cs.state;
                    if (cs.state.name == "Wave") sWave = cs.state;
                }
                Check(sIdle != null && sm.defaultState == sIdle, "既定 State が Idle");
                var idleClip = sIdle == null ? null : sIdle.motion as AnimationClip;
                Check(idleClip != null && Mathf.Abs(idleClip.length - IdleLen) <= LenTol
                    && idleClip.isLooping,
                    "Idle の motion が Idle_Generic (長さ一致・ループ)");
                var waveClip = sWave == null ? null : sWave.motion as AnimationClip;
                Check(waveClip != null && Mathf.Abs(waveClip.length - WaveLen) <= LenTol,
                    "Wave の motion が Idle_Waving (長さ一致)");
                Check(sIdle != null && sIdle.transitions.Length == 1
                    && !sIdle.transitions[0].hasExitTime,
                    "Idle → Wave が Bool 即応 (exit time 無し)");
                Check(sWave != null && sWave.transitions.Length == 1
                    && sWave.transitions[0].hasExitTime
                    && sWave.transitions[0].conditions.Length == 0,
                    "Wave → Idle が無条件 exit time (1 ループで戻る)");
            }

            log.AppendLine(fail == 0 ? "VERIFY RESULT: PASS" : $"VERIFY RESULT: FAIL ({fail})");
            Finish("Logs/imp-env-004-npc-verify.txt", log);
        }

        // ================================================================ 道具

        static AnimationClip LoadClip(string guid, float expectedLen, string label, StringBuilder log)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                log.AppendLine($"FAIL clip not found: {label} guid={guid}" +
                    " (polyperfect Low Poly Animated People が未導入?)");
                return null;
            }
            // FBX 内クリップなので LoadAssetAtPath<AnimationClip> ではなく全アセットから拾う
            // (__preview* の複製クリップは除く)
            AnimationClip clip = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var c = o as AnimationClip;
                if (c == null || c.name.StartsWith("__preview")) continue;
                clip = c;
                break;
            }
            if (clip == null)
            {
                log.AppendLine($"FAIL no AnimationClip in: {label} path={path}");
                return null;
            }
            if (Mathf.Abs(clip.length - expectedLen) > LenTol)
            {
                log.AppendLine($"FAIL length mismatch (取り違え疑い): {label}" +
                    $" expected={expectedLen} actual={clip.length} path={path}");
                return null;
            }
            log.AppendLine($"ok {label} len={clip.length:F3} loop={clip.isLooping} path={path}");
            return clip;
        }

        /// <summary>既存 Controller は GUID を保つため削除せず中身だけ作り直す
        /// (`KD_DogAnimatorBuilderEditor.GetOrResetController` と同じ作法)。</summary>
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

        static void SetupTransition(AnimatorStateTransition t, bool hasExitTime)
        {
            t.hasExitTime = hasExitTime;
            t.exitTime = 1f; // hasExitTime のときだけ効く (= 1 ループ再生し切る)
            t.hasFixedDuration = true;
            t.duration = BlendSec;
            t.offset = 0f;
            t.interruptionSource = TransitionInterruptionSource.None;
        }

        static void Finish(string path, StringBuilder log)
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(path, log.ToString());
            Debug.Log("[KD_NpcAnimatorBuilder] done → " + path);
        }
    }
}
