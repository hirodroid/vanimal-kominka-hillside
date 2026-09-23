using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-DOG-003 犬の Controller: 0 - Generate Pickup Pose と派生クリップ (BUG-DOG-002)。本体は KD_DogAnimatorBuilderEditor.cs。</summary>
    public static partial class KD_DogAnimatorBuilderEditor
    {
        // ================================================================ Pickup pose (IMP-FET-001)

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Controller/0 - Generate Pickup Pose")]
        public static void GeneratePickupPose()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogAnimatorBuilder] Generate pickup pose (+ derived clips: BUG-DOG-002)");
            bool allOk = true;

            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("-- " + def.displayName + " (" + def.key + ")");
                if (!GeneratePickupPoseFor(def, log)) allOk = false;
                // BUG-DOG-002: 購入クリップのカーブ打ち間違いを直した派生クリップ (定義が指定した犬だけ)
                if (!GenerateLieDownDerivedFor(def, log)) allOk = false;
            }

            log.AppendLine(allOk
                ? "POSE RESULT: PASS (次は 1 - Build)"
                : "POSE RESULT: FAIL");
            KD_VerifyLog.Write("Logs/imp-fet-001-pose.txt", log);
        }

        static bool GeneratePickupPoseFor(KD_DogDef def, StringBuilder log)
        {
            var taiki = LoadClip(def.idle, log);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(def.prefabGuid));
            if (taiki == null || prefab == null)
            {
                log.AppendLine("   FAIL 待機クリップまたはプレハブが無い");
                return false;
            }

            // 回転軸の符号は推測しない: 両符号で作り、鼻先が下がる方を採用する
            var plus = BuildPoseClip(taiki, 1f, log);
            var minus = BuildPoseClip(taiki, -1f, log);
            if (plus == null || minus == null)
            {
                log.AppendLine("   FAIL curve copy");
                return false;
            }

            // 測定: アーマチュア側の骨は全て position=(0,0,0) の回転専用骨格 (2026-08-10 実測 —
            // Logs/imp-fet-001-pose-debug.txt) なので位置では測れない。
            // RotationConstraint はワールド回転を写すため、
            // 「ko-gi- 側の頭→鼻オフセットへ、アーマチュア頭骨のワールド回転差分を適用した Y 低下」
            // が実際の見た目の鼻の下がりに一致する
            var temp = Object.Instantiate(prefab);
            temp.hideFlags = HideFlags.HideAndDontSave;
            // IMP-DOG-005: 骨格を実測で解決する (2 段構えなら書き先は Constraint ソース、
            // 1 段構えならボーン直。ポメには ko-gi- 相当のメッシュ側骨格が無い)
            var rig = KD_DogRig.Resolve(temp);
            if (!rig.Ok)
            {
                foreach (var p in rig.problems) log.AppendLine("   FAIL rig: " + p);
                Object.DestroyImmediate(temp);
                Object.DestroyImmediate(plus);
                Object.DestroyImmediate(minus);
                return false;
            }
            var meshHead = rig.head;
            var meshNose = rig.nose;
            var armHead = rig.headWriteBone; // 1 段構えなら meshHead と同一

            Vector3 noseVec = meshNose.position - meshHead.position; // レスト時の頭→鼻 (≈ +Z 前方)
            taiki.SampleAnimation(temp, 0f);
            Quaternion qRest = armHead.rotation;
            plus.SampleAnimation(temp, 0f);
            float dropPlus = noseVec.y - ((armHead.rotation * Quaternion.Inverse(qRest)) * noseVec).y;
            minus.SampleAnimation(temp, 0f);
            float dropMinus = noseVec.y - ((armHead.rotation * Quaternion.Inverse(qRest)) * noseVec).y;
            Object.DestroyImmediate(temp);
            log.AppendLine($"   nose drop (m, head->nose {noseVec:F3}): plus={dropPlus:F3} minus={dropMinus:F3}");

            bool pickPlus = dropPlus >= dropMinus;
            var chosen = pickPlus ? plus : minus;
            var rejected = pickPlus ? minus : plus;
            float chosenDrop = pickPlus ? dropPlus : dropMinus;
            log.AppendLine($"   picked sign={(pickPlus ? "+" : "-")} (nose drop {chosenDrop:F3} m)");

            // 鼻が下がる量は体格に比例する。コーギーの 0.02m を headScale 比で換算する
            float minDrop = 0.02f * (rig.headScale / KD_DogRig.CorgiHeadScale);
            if (chosenDrop < minDrop)
            {
                // どちらの符号でも鼻が下がらない = 回転軸の仮定 (ローカル X = ピッチ) が外れている
                log.AppendLine($"   FAIL nose did not drop (< {minDrop:F3} m) - pitch axis assumption is wrong");
                Object.DestroyImmediate(plus);
                Object.DestroyImmediate(minus);
                return false;
            }

            // 既存アセットは GUID を保つため in-place でカーブだけ入れ替える (Controller 参照を切らない)
            if (!AssetDatabase.IsValidFolder("Assets/KominkaHillside/Imported/KominkaDogRun/Animations"))
                AssetDatabase.CreateFolder("Assets/KominkaHillside/Imported/KominkaDogRun", "Animations");
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(def.PickupPosePath);
            AnimationClip saved;
            if (existing == null)
            {
                AssetDatabase.CreateAsset(chosen, def.PickupPosePath);
                saved = chosen;
                log.AppendLine("   created " + def.PickupPosePath);
            }
            else
            {
                existing.ClearCurves();
                foreach (var b in AnimationUtility.GetCurveBindings(chosen))
                    AnimationUtility.SetEditorCurve(existing, b, AnimationUtility.GetEditorCurve(chosen, b));
                Object.DestroyImmediate(chosen);
                saved = existing;
                log.AppendLine("   updated in place (GUID 維持): " + def.PickupPosePath);
            }
            Object.DestroyImmediate(rejected);

            var settings = AnimationUtility.GetAnimationClipSettings(saved);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(saved, settings);
            EditorUtility.SetDirty(saved);
            AssetDatabase.SaveAssets();

            log.AppendLine($"   length={saved.length:F2} curves={AnimationUtility.GetCurveBindings(saved).Length}");
            return true;
        }

        // ================================================================ Derived clips (BUG-DOG-002)

        /// <summary>骨格 (アーマチュア) のパス片。**位置カーブはこの配下にしか無いはず** —
        /// 外にあれば「ルートに打つべきカーブを別ボーンに打った」打ち間違い
        /// (6 匹 × 全採用クリップの実測で、外にあったのは柴 huse の hojo_born/Bone だけ)。</summary>
        const string SkeletonSegment = "/アーマチュア/";
        /// <summary>体の高さを決める唯一のボーン (アーマチュア/Bone) のパス末尾</summary>
        const string RootBoneSuffix = "アーマチュア/Bone";

        /// <summary>Controller に載せる LieDown のクリップ。
        /// 派生する犬 (<see cref="KD_DogDef.LieDownIsDerived"/>) は生成物 <c>KD_LieDown_{key}</c> を指す。
        /// **生成前は guid が空** = LoadClip が FAIL する (0 - Generate Pickup Pose を先に)。</summary>
        static KD_ClipRef EffectiveLieDown(KD_DogDef def)
        {
            if (!def.LieDownIsDerived) return def.lieDown;
            return new KD_ClipRef("KD_LieDown_" + def.key + " (派生: " + def.lieDown.label + ")",
                AssetDatabase.AssetPathToGUID(def.LieDownDerivedPath), def.lieDown.length);
        }

        /// <summary>
        /// BUG-DOG-002: <see cref="KD_DogDef.lieDown"/> を複製し、
        /// <see cref="KD_DogDef.lieDownRootCurveSourcePath"/> に打たれている位置カーブを
        /// アーマチュア/Bone のパスへ**移す** (値はそのまま = 作者の意図した沈み量を保つ)。
        /// 購入クリップは触らない。生成物は GUID 維持で in-place 更新 (KD_PickupPose と同じ作法)。
        ///
        /// ルートのパスは決め打ちせずプレハブから実測する (Animator はプレハブのルートに載っている —
        /// <c>hojo_born/Bone/Bone_end/PB3/inu/アーマチュア/Bone</c>。2026-09-06 実測)。
        /// </summary>
        static bool GenerateLieDownDerivedFor(KD_DogDef def, StringBuilder log)
        {
            if (!def.LieDownIsDerived) return true;

            var source = LoadClip(def.lieDown, log);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(def.prefabGuid));
            if (source == null || prefab == null)
            {
                log.AppendLine("   FAIL 派生元の伏せクリップまたはプレハブが無い");
                return false;
            }

            string rootPath;
            var temp = Object.Instantiate(prefab);
            temp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var rig = KD_DogRig.Resolve(temp);
                var root = rig.srcSkelRoot != null ? rig.srcSkelRoot.Find("Bone") : null;
                if (root == null)
                {
                    log.AppendLine("   FAIL アーマチュア/Bone が解決できない (派生クリップの移し先が決まらない)");
                    return false;
                }
                rootPath = AnimationUtility.CalculateTransformPath(root, temp.transform);
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }

            var derived = new AnimationClip { frameRate = source.frameRate };
            int moved = 0;
            bool rootAlreadyHasPosition = false;
            float sink = 0f;
            foreach (var b in AnimationUtility.GetCurveBindings(source))
            {
                var nb = b;
                var curve = AnimationUtility.GetEditorCurve(source, b);
                if (b.propertyName.StartsWith("m_LocalPosition"))
                {
                    if (b.path == rootPath) rootAlreadyHasPosition = true;
                    if (b.path == def.lieDownRootCurveSourcePath)
                    {
                        nb.path = rootPath;
                        moved++;
                        // local z が world の上下 (アーマチュア が -90° X)。沈み量のログ用
                        if (b.propertyName.EndsWith(".z") && curve.keys.Length > 0) sink = curve.keys[0].value;
                    }
                }
                AnimationUtility.SetEditorCurve(derived, nb, curve);
            }
            if (AnimationUtility.GetObjectReferenceCurveBindings(source).Length > 0)
                log.AppendLine("   WARN 原本にオブジェクト参照カーブがある (派生クリップには写していない)");

            if (moved == 0)
            {
                log.AppendLine($"   FAIL 移す位置カーブが無い: {def.lieDown.label} に '{def.lieDownRootCurveSourcePath}' の位置カーブが見当たらない" +
                    " (原本が直った? なら定義の lieDownRootCurveSourcePath を消す)");
                Object.DestroyImmediate(derived);
                return false;
            }
            if (rootAlreadyHasPosition)
            {
                log.AppendLine($"   FAIL 原本に既に {RootBoneSuffix} の位置カーブがある — 移すと 2 重になる。定義を見直す");
                Object.DestroyImmediate(derived);
                return false;
            }
            AnimationUtility.SetAnimationClipSettings(derived, AnimationUtility.GetAnimationClipSettings(source));

            if (!AssetDatabase.IsValidFolder("Assets/KominkaHillside/Imported/KominkaDogRun/Animations"))
                AssetDatabase.CreateFolder("Assets/KominkaHillside/Imported/KominkaDogRun", "Animations");
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(def.LieDownDerivedPath);
            AnimationClip saved;
            if (existing == null)
            {
                AssetDatabase.CreateAsset(derived, def.LieDownDerivedPath);
                saved = derived;
                log.AppendLine("   created " + def.LieDownDerivedPath);
            }
            else
            {
                existing.ClearCurves();
                foreach (var b in AnimationUtility.GetCurveBindings(derived))
                    AnimationUtility.SetEditorCurve(existing, b, AnimationUtility.GetEditorCurve(derived, b));
                AnimationUtility.SetAnimationClipSettings(existing, AnimationUtility.GetAnimationClipSettings(derived));
                Object.DestroyImmediate(derived);
                saved = existing;
                log.AppendLine("   updated in place (GUID 維持): " + def.LieDownDerivedPath);
            }
            EditorUtility.SetDirty(saved);
            AssetDatabase.SaveAssets();

            int srcCount = AnimationUtility.GetCurveBindings(source).Length;
            int dstCount = AnimationUtility.GetCurveBindings(saved).Length;
            log.AppendLine($"   moved {moved} position curves '{def.lieDownRootCurveSourcePath}' -> '{rootPath}'" +
                $" (沈み z={sink:F3}) curves {srcCount} -> {dstCount} len={saved.length:F3} loop={saved.isLooping}");
            if (srcCount != dstCount)
            {
                log.AppendLine("   FAIL カーブ本数が変わった (移すだけのはず)");
                return false;
            }
            if (!HasRootPositionCurve(saved) || PositionCurveOutsideSkeleton(saved) != null)
            {
                log.AppendLine("   FAIL 派生クリップが検出器を通らない (ルート位置カーブ無し / 骨格外に位置カーブ)");
                return false;
            }
            return true;
        }

        /// <summary>骨格 (アーマチュア/ 配下) の**外**に位置カーブがあれば、その最初のパスを返す。
        /// 無ければ null。BUG-DOG-002 の検出器 — <see cref="RootPositionSafe"/> と Verify が使う。</summary>
        static string PositionCurveOutsideSkeleton(AnimationClip clip)
        {
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (!b.propertyName.StartsWith("m_LocalPosition")) continue;
                if (b.path.Contains(SkeletonSegment)) continue;
                // Companion pose capture includes the armature object itself at neutral zero.
                // Accept only that no-op curve; animated/nonzero offsets outside Bone still fail.
                if (b.path.EndsWith("/アーマチュア") && b.type == typeof(Transform))
                {
                    var c = AnimationUtility.GetEditorCurve(clip, b);
                    bool neutral = c != null && c.length > 0;
                    if (c != null) foreach (var k in c.keys)
                        if (k.value != 0f || k.inTangent != 0f || k.outTangent != 0f) neutral = false;
                    if (neutral) continue;
                }
                return b.path;
            }
            return null;
        }

        /// <summary>アーマチュア/Bone に位置カーブを持つか (体の高さを動かせるのはこれだけ)</summary>
        static bool HasRootPositionCurve(AnimationClip clip)
        {
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (!b.propertyName.StartsWith("m_LocalPosition")) continue;
                if (b.path.EndsWith(RootBoneSuffix)) return true;
            }
            return false;
        }

        /// <summary>BUG-DOG-002: 姿勢を低くするクリップ (Sit / LieDown / Sleep) にルート位置カーブが無い =
        /// 「立位の高さのまま脚だけ折れる」疑い。位置カーブ無しで成立する伏せもあり得るので FAIL にせず WARN。
        /// 柴 huse は**別ボーンに打っていた**のでこれだけでは捕まらず、骨格外検査 (FAIL) と組で見る。</summary>
        static void WarnIfNoRootPositionCurve(AnimationClip clip, string label, StringBuilder log)
        {
            if (clip == null) return;
            if (HasRootPositionCurve(clip)) return;
            log.AppendLine($"WARN no root position curve: {label} — 姿勢を低くするクリップなのに {RootBoneSuffix} の位置カーブが無い。" +
                "立位の高さのまま脚だけ折れて浮かないか Play で見る (BUG-DOG-002)");
        }

        /// <summary>taiki 6 の先頭フレームを定数カーブとして全コピーし、
        /// PoseBones だけローカル X 軸 (sign 付き) で前へ倒したポーズクリップを作る。
        /// このモデルのクリップの回転は localEulerAnglesRaw (2026-08-10 実測 —
        /// Logs/imp-fet-001-pose-debug.txt)。対象ボーンの Y/Z 回転は実測 0 なので
        /// X 成分への加算 = ローカルピッチになる。</summary>
        static AnimationClip BuildPoseClip(AnimationClip src, float sign, StringBuilder log)
        {
            var clip = new AnimationClip { frameRate = 60f };
            bool[] adjusted = new bool[PoseBones.Length];

            foreach (var b in AnimationUtility.GetCurveBindings(src))
            {
                var curve = AnimationUtility.GetEditorCurve(src, b);
                if (curve == null) continue;
                float v0 = curve.Evaluate(0f);

                int poseIdx = PoseBoneIndex(b.path);
                if (poseIdx >= 0 && b.propertyName.StartsWith("m_LocalRotation"))
                {
                    // 想定外の回転表現 (クォータニオンカーブ)。加算方法が変わるため作り直しを要求する
                    log.AppendLine($"FAIL unexpected quaternion curve on pose bone: {b.path}");
                    Object.DestroyImmediate(clip);
                    return null;
                }
                if (poseIdx >= 0 && b.propertyName == "localEulerAnglesRaw.x")
                {
                    v0 += sign * PoseBonePitchDeg[poseIdx];
                    adjusted[poseIdx] = true;
                }
                WriteConstant(clip, b, v0);
            }

            for (int i = 0; i < PoseBones.Length; i++)
            {
                if (adjusted[i]) continue;
                log.AppendLine($"FAIL pose bone not found in source clip: {PoseBones[i]}");
                Object.DestroyImmediate(clip);
                return null;
            }
            return clip;
        }

        static int PoseBoneIndex(string path)
        {
            for (int i = 0; i < PoseBones.Length; i++)
                if (path.EndsWith("/" + PoseBones[i])) return i;
            return -1;
        }

        static void WriteConstant(AnimationClip clip, EditorCurveBinding b, float v)
        {
            var c = new AnimationCurve(new Keyframe(0f, v), new Keyframe(PoseLength, v));
            AnimationUtility.SetEditorCurve(clip, b, c);
        }

    }
}
