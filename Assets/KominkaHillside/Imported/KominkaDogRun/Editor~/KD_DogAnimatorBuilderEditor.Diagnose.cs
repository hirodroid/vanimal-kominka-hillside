using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-DOG-003 犬の Controller: 7/8 - Diagnose と 9 - Self-test (ルート位置検出器の回帰)。本体は KD_DogAnimatorBuilderEditor.cs。</summary>
    public static partial class KD_DogAnimatorBuilderEditor
    {
        // ================================================================ Diagnose (IMP-FET-001 移動の滑り)

        /// <summary>KD_Dog ルート〜メッシュ間の追従リグ (Constraint / PhysBone) を実測する。
        /// 「ルートを動かすとメッシュが遅れて滑る」原因の特定用。直った後も残す。</summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Controller/5 - Diagnose Follow Rig")]
        public static void DiagnoseFollowRig()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogAnimatorBuilder] Diagnose follow rig");

            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("========= " + def.displayName + " (" + def.key + ") =========");
                int dogCount;
                var dogGo = KD_SceneObjects.Find(def.DogName, out dogCount);
                if (dogGo == null)
                {
                    log.AppendLine("FAIL " + def.DogName + " not found in scene");
                    continue;
                }
                DiagnoseFollowRigFor(dogGo, log);
            }
            KD_VerifyLog.Write("Logs/imp-fet-001-followrig.txt", log);
        }

        static void DiagnoseFollowRigFor(GameObject dog, StringBuilder log)
        {
            // 骨格の中は §0 で実測済みなので、その外側 (追従リグ層) だけ見る。
            // IMP-DOG-005: 骨格ルート名は犬種で違う (ko-gi- / ラブラドール / 無し) ので実測で引く
            var rig = KD_DogRig.Resolve(dog);
            var skelRoots = new System.Collections.Generic.List<Transform>();
            if (rig.srcSkelRoot != null) skelRoots.Add(rig.srcSkelRoot);
            if (rig.meshSkelRoot != null) skelRoots.Add(rig.meshSkelRoot);

            foreach (var t in dog.GetComponentsInChildren<Transform>(true))
            {
                bool inSkeleton = false;
                foreach (var r in skelRoots)
                    if (t.IsChildOf(r)) { inSkeleton = true; break; }
                if (inSkeleton) continue;

                foreach (var con in t.GetComponents<UnityEngine.Animations.IConstraint>())
                {
                    var behaviour = (Behaviour)con;
                    log.AppendLine($"{FullPath(t, dog.transform)} : {behaviour.GetType().Name}" +
                        $" active={con.constraintActive} weight={con.weight} enabled={behaviour.enabled}");
                    for (int i = 0; i < con.sourceCount; i++)
                    {
                        var s = con.GetSource(i);
                        string srcPath = s.sourceTransform == null ? "null" : FullPath(s.sourceTransform, dog.transform);
                        bool outside = s.sourceTransform != null && !s.sourceTransform.IsChildOf(dog.transform);
                        log.AppendLine($"    source[{i}] = {srcPath} (weight {s.weight})" + (outside ? "  [OUTSIDE KD_Dog!]" : ""));
                    }
                }
                foreach (var pb in t.GetComponents<VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone>())
                {
                    var root = pb.rootTransform != null ? pb.rootTransform : pb.transform;
                    log.AppendLine($"{FullPath(t, dog.transform)} : VRCPhysBone enabled={pb.enabled}" +
                        $" root={FullPath(root, dog.transform)} pull={pb.pull} spring={pb.spring}" +
                        $" immobile={pb.immobile} immobileType={pb.immobileType}");
                }
            }

            // inu 直下と inu/Bone.001 配下の構造 (PhysBone が何を揺らしているのか)
            var inu = FindDescendant(dog.transform, "inu");
            if (inu != null)
            {
                log.AppendLine("--- inu children ---");
                foreach (Transform c in inu)
                    log.AppendLine($"  inu/{c.name} (children={c.childCount})");
                var b1 = inu.Find("Bone.001");
                if (b1 != null)
                    foreach (var d in b1.GetComponentsInChildren<Transform>(true))
                        log.AppendLine($"  subtree {FullPath(d, inu)} (children={d.childCount})");
            }

            // SkinnedMeshRenderer がどこに居て、どのボーンで描いているか
            log.AppendLine("--- renderers ---");
            foreach (var smr in dog.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string rootBone = smr.rootBone == null ? "null" : FullPath(smr.rootBone, dog.transform);
                log.AppendLine($"  {FullPath(smr.transform, dog.transform)} rootBone={rootBone} bones={smr.bones.Length}");
            }
        }

        static Transform FindDescendant(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static string FullPath(Transform t, Transform stopAt)
        {
            string s = t.name;
            for (var u = t.parent; u != null && u != stopAt; u = u.parent) s = u.name + "/" + s;
            return s;
        }


        // ================================================================ Diagnose (IMP-FET-001 犬が浮く)

        /// <summary>待機クリップの位置・スケールカーブと、ko-gi- 根元の Constraint 構成を実測する。
        /// 「流用クリップ (ookami/犬_テンプレ) のルート位置が別体格の値で、根元の
        /// Position Constraint 経由でメッシュが浮く」仮説の検証用。直った後も残す (vrc-verification §7)。</summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Controller/6 - Diagnose Idle Root Motion")]
        public static void DiagnoseIdleRootMotion()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogAnimatorBuilder] Diagnose idle root motion");

            foreach (var dogDef in KD_DogDefs.All)
            {
                log.AppendLine("========= " + dogDef.displayName + " (" + dogDef.key + ") =========");

                // Controller が使う全クリップ + 不採用の taiki (ookami) を並べて比較する。
                // 流用クリップのルート位置は体格差で浮くため、正当域と外れ値を並記すると読みやすい
                var clipRefs = new System.Collections.Generic.List<KD_ClipRef>(ControllerClipsOf(dogDef));
                clipRefs.Add(TaikiOokami);
                foreach (var def in clipRefs)
                {
                    var clip = LoadClip(def, log);
                    if (clip == null) continue;
                    int found = 0;
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    {
                        bool isPos = b.propertyName.StartsWith("m_LocalPosition");
                        bool isScale = b.propertyName.StartsWith("m_LocalScale");
                        if (!isPos && !isScale) continue;
                        var c = AnimationUtility.GetEditorCurve(clip, b);
                        if (c == null || c.keys.Length == 0) continue;
                        float min = float.MaxValue, max = float.MinValue;
                        foreach (var k in c.keys) { if (k.value < min) min = k.value; if (k.value > max) max = k.value; }
                        log.AppendLine($"  {def.label} | {ShortPath(b.path)} {b.propertyName} min={min:F4} max={max:F4} keys={c.keys.Length}");
                        found++;
                    }
                    if (found == 0) log.AppendLine($"  {def.label} | no position/scale curves");
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(dogDef.prefabGuid));
                if (prefab == null) continue;

                var temp = Object.Instantiate(prefab);
                temp.hideFlags = HideFlags.HideAndDontSave;
                var rig = KD_DogRig.Resolve(temp);
                log.AppendLine("  rig: " + rig.Summary());
                var armRoot = rig.srcSkelRoot != null ? rig.srcSkelRoot.Find("Bone") : null;
                var meshRoot = rig.meshSkelRoot != null ? rig.meshSkelRoot.Find("Bone") : null;
                if (armRoot != null)
                    log.AppendLine($"  rest アーマチュア/Bone localPos={armRoot.localPosition:F4}");
                if (meshRoot != null)
                {
                    log.AppendLine($"  rest {rig.meshSkelRoot.name}/Bone localPos={meshRoot.localPosition:F4}");
                    foreach (var con in meshRoot.GetComponents<UnityEngine.Animations.IConstraint>())
                    {
                        var behaviour = (Behaviour)con;
                        string srcName = con.sourceCount > 0 && con.GetSource(0).sourceTransform != null
                            ? con.GetSource(0).sourceTransform.name : "null";
                        log.AppendLine($"    constraint {behaviour.GetType().Name} active={con.constraintActive} weight={con.weight} src={srcName}");
                        var pc = con as UnityEngine.Animations.PositionConstraint;
                        if (pc != null) log.AppendLine($"      translationAtRest={pc.translationAtRest:F4} translationOffset={pc.translationOffset:F4} axes={pc.translationAxis}");
                    }
                }
                else
                {
                    log.AppendLine("  1 段構え — メッシュ側骨格が無いので根元 Constraint も無い");
                }
                Object.DestroyImmediate(temp);
            }

            KD_VerifyLog.Write("Logs/imp-fet-001-rootmotion.txt", log);
        }

        static string ShortPath(string path)
        {
            int i = path.LastIndexOf('/');
            int j = i > 0 ? path.LastIndexOf('/', i - 1) : -1;
            return j >= 0 ? path.Substring(j + 1) : path;
        }

        // ================================================================ Self-test (IMP-DOG-007)

        /// <summary>
        /// ルート位置検出器の回帰テスト。**この検出器は 2 度作り直している**ので、
        /// しきい値の根拠を実行できる形で残す。
        ///
        /// - 1 度目 (IMP-DOG-006): 絶対値で測っていて、ポメのレストが原点でないため自前クリップを誤検出
        /// - 2 度目 (IMP-DOG-007): 体格で変位が伸びるラブの osuwari を誤検出。
        ///   同時に、**ラブのフォルダに ookami のクリップが混入している**のを見逃していた
        ///
        /// 期待値は**すべて実測に基づく**。ここが赤くなったらしきい値を触った副作用。
        /// </summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Controller/7 - Self-test Root Position Check")]
        public static void SelfTestRootPosition()
        {
            // { プレハブ guid, クリップ guid, 期待 (ok/fail), 説明 }
            var cases = new[]
            {
                new[] { "de3f682e15a9ad74093cb1c4970f6520", "900d7453d14409b49957f6210fa1020a", "ok",   "コーギー + taiki 6 (自前)" },
                new[] { "de3f682e15a9ad74093cb1c4970f6520", "5e448255d0bd8574894ae68f76a0272d", "ok",   "コーギー + osuwari (自前。local Z 下降 0.16)" },
                new[] { "de3f682e15a9ad74093cb1c4970f6520", "5fe63cc983111d044b71f8b8158e09d5", "ok",   "コーギー + taimen 2 (自前。local Y 前進 0.089)" },
                new[] { "de3f682e15a9ad74093cb1c4970f6520", "7c68111f534b5dc428f0a33722beb5d6", "fail", "コーギー + ookami taiki (2026-08-11 に浮きで除外した実例)" },
                new[] { "e9a4544f2eda9254f965d555060479e8", "8ba19b0a510eee64cb326ab7f1c164e1", "ok",   "ポメ + osuwari (自前。レストが原点でない犬)" },
                new[] { "e9a4544f2eda9254f965d555060479e8", "a8adf6f5d7d053a418f8c9b5fdc8b404", "ok",   "ポメ + hasiru1 (自前。旧・絶対値判定はここで落ちた)" },
                new[] { "e9a4544f2eda9254f965d555060479e8", "900d7453d14409b49957f6210fa1020a", "fail", "ポメ + コーギー taiki 6 (family A→B。local Y 差 -0.153)" },
                new[] { "5736813bd5bbfe04ab60ae0f5cbeb6fd", "342fe931678d28540a84116236f11e31", "ok",   "ラブ + osuwari (自前。local Z 下降 0.393)" },
                new[] { "5736813bd5bbfe04ab60ae0f5cbeb6fd", "3ce58a3f9e2f290498064b4368f4a24c", "ok",   "ラブ + 犬_テンプレ hasiru1 (採用した走り)" },
                new[] { "5736813bd5bbfe04ab60ae0f5cbeb6fd", "6f4c4caa405ae084ea9bce9262344c66", "fail", "ラブ + 自フォルダの hasiru1 (**中身は ookami**。md5 一致)" },
                new[] { "5736813bd5bbfe04ab60ae0f5cbeb6fd", "962f8c2d6e8ed4c4292877d0f5dc6b7c", "fail", "ラブ + samoedo hasiru1 (family C。local Y 差 +0.270)" },
                new[] { "5736813bd5bbfe04ab60ae0f5cbeb6fd", "608010440a7bec04090029cbd733da68", "fail", "ラブ + SHIBA hasiru1 (family B。local Y 差 +0.153)" },

                // ---- IMP-DOG-010 (ダックス = family D。レスト 0.1660 / 体格比 1.01) ----
                new[] { "dd94fbd924e81d944a726405a15238d7", "6a4786e4120b83645a4d64306b1c2d38", "ok",   "ダックス + osuwari (自前。local Z 下降 0.128)" },
                new[] { "dd94fbd924e81d944a726405a15238d7", "c0c8b1bedc48a9c4ca465bcb2666344d", "ok",   "ダックス + aruku 1 (自前。採用した歩き)" },
                new[] { "dd94fbd924e81d944a726405a15238d7", "f81fffc17678f4b40ad27379b3966951", "fail", "ダックス + 自フォルダの aruku (**中身は samoedo**。md5 一致。local Y 差 +0.103)" },
                new[] { "dd94fbd924e81d944a726405a15238d7", "900d7453d14409b49957f6210fa1020a", "fail", "ダックス + コーギー taiki 6 (family A→D。local Y 差 -0.166 — 前後の互換範囲外)" },
                new[] { "dd94fbd924e81d944a726405a15238d7", "f4639b5e476bc8640a4b74d7292e5fc4", "ok",   "ダックス + ポメ taimen (family B→D。local Y 前進 0.013 = 採用した LookAt)" },
                new[] { "dd94fbd924e81d944a726405a15238d7", "5fe63cc983111d044b71f8b8158e09d5", "fail", "ダックス + コーギー taimen 2 (family A→D。local Y 差 -0.255 — LookAt を借りるときの外れ値)" },

            // ---- IMP-DOG-020 (柴 = family A (レスト原点) / 2 段構え / 体格比 1.135) ----
            // **プレハブは A だがフォルダ名 SHIBA のクリップは B (pome と md5 一致)** — フォルダ名は安全の根拠にならない (3 例目)
            new[] { "03f419105d1319b42bd3a1eb8b1b301f", "eef8ba6f262de2d4a904a83c23b852f0", "ok",   "柴 + osuwari (shiba_anim 自前。local Z 下降 0.290)" },
            new[] { "03f419105d1319b42bd3a1eb8b1b301f", "80fd3c844e53eb84c9a1c7c425b35529", "ok",   "柴 + taimen 2 (shiba_anim 自前。local Y 前進 0.056 = 採用した LookAt)" },
            new[] { "03f419105d1319b42bd3a1eb8b1b301f", "c05fbf9e930e8ec46bfc0090b6f29a2d", "fail", "柴 + SHIBA/anim hasiru1 (**中身は pome = family B**。local Y 差 +0.153)" },
            new[] { "03f419105d1319b42bd3a1eb8b1b301f", "962f8c2d6e8ed4c4292877d0f5dc6b7c", "fail", "柴 + samoedo hasiru1 (family C。local Y 差 +0.270)" },
            // ---- IMP-DOG-020 (サモエド = family C (レスト 0.2695) / 1 段構え / 体格比 1.568) ----
            new[] { "743ffbf56e83bb44a9c3d18337f9262c", "5bbfb1b892c55884c85e40c1a72cfa4a", "ok",   "サモエド + osuwari (自前。local Z 下降 0.289 / local Y 前進 0.011)" },
            new[] { "743ffbf56e83bb44a9c3d18337f9262c", "74afe7973b9e0cb4bb33477a5d485c85", "ok",   "サモエド + aruku (自前。**ダックスの aruku と md5 一致 = 原本はこちら**。local Z 上昇 0.024)" },
            new[] { "743ffbf56e83bb44a9c3d18337f9262c", "900d7453d14409b49957f6210fa1020a", "fail", "サモエド + コーギー taiki 6 (family A→C。local Y 差 -0.270 — 前後の互換範囲外)" },
            new[] { "743ffbf56e83bb44a9c3d18337f9262c", "f4639b5e476bc8640a4b74d7292e5fc4", "ok",   "サモエド + ポメ taimen (family B→C。local Y 前進 0.117 / 許容 0.157 — 借りられる側の境界値)" },

            // ---- BUG-DOG-002 (柴 huse の沈みが hojo_born/Bone に打たれていて アーマチュア/Bone に無い) ----
            // 「ずれ過大」では捕まらない (ルートに**無い**) ので、骨格外の位置カーブを名指しで落とす検査を足した。
            // 2 件目はクリップを GUID でなく**パス**で引く (生成物。0 - Generate Pickup Pose を先に流す)
            new[] { "03f419105d1319b42bd3a1eb8b1b301f", "aaaf58ec27f4d0a40aa5c784deb4cf41", "fail", "柴 + huse (shiba_anim 原本)。**位置カーブが骨格の外 hojo_born/Bone にある** (効かずに浮く。旧検出器は素通りした)" },
            new[] { "03f419105d1319b42bd3a1eb8b1b301f", "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/KD_LieDown_Shiba.anim", "ok", "柴 + KD_LieDown_Shiba (派生。沈み 0.127 を アーマチュア/Bone へ移した)" },
            };

            var v = new KD_VerifyLog("[KD_DogAnimatorBuilder] Self-test root position check");
            var log = v.Text;
            log.AppendLine($"local Z 上向き {RootRiseLimit} / 下向き max({RootSinkFloor}, {RootSinkRatio}×体格比) / local X,Y 面内 {RootPlaneRatio}×体格比");
            log.AppendLine();

            foreach (var c in cases)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(c[0]));
                // c[1] は GUID か、生成物 (GUID を定数で持てない) のパス
                string clipPath = c[1].StartsWith("Assets/") ? c[1] : AssetDatabase.GUIDToAssetPath(c[1]);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (prefab == null || clip == null)
                {
                    v.Check(false, "資産が引けない: " + c[3]);
                    continue;
                }

                var temp = Object.Instantiate(prefab);
                temp.hideFlags = HideFlags.HideAndDontSave;
                Vector3 rest;
                float ratio;
                try
                {
                    var rig = KD_DogRig.Resolve(temp);
                    var bone = rig.srcSkelRoot != null ? rig.srcSkelRoot.Find("Bone") : null;
                    rest = bone != null ? bone.localPosition : Vector3.zero;
                    ratio = rig.BodyRatio;
                }
                finally
                {
                    Object.DestroyImmediate(temp);
                }

                var sink = new StringBuilder();
                bool actual = RootPositionSafe(clip, c[3], rest, ratio, sink);
                bool expected = c[2] == "ok";
                bool pass = actual == expected;
                v.Check(pass, c[3] +
                    " — 期待 " + c[2] + " / 実際 " + (actual ? "ok" : "fail"));
                if (!actual) log.AppendLine("         " + sink.ToString().Trim());
            }

            // Regression cases use absolute curves around a nonzero rest, not only family A.
            var restSynthetic = new Vector3(0, .1527f, .189f);
            void Synthetic(string label, Vector3 delta, float ratio, bool expected)
            {
                var clip = new AnimationClip();
                try
                {
                    for (int axis = 0; axis < 3; axis++)
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("inu/" + RootBoneSuffix, typeof(Transform), "m_LocalPosition." + "xyz"[axis]), AnimationCurve.Constant(0, 1, restSynthetic[axis] + delta[axis]));
                    bool actual = RootPositionSafe(clip, label, restSynthetic, ratio, new StringBuilder());
                    v.Check(actual == expected, "axis regression: " + label + " expected=" + expected + " actual=" + actual);
                }
                finally { Object.DestroyImmediate(clip); }
            }
            Synthetic("Z +.30 same family must fail", new Vector3(0, 0, .3f), 1f, false);
            Synthetic("Z +.055 legitimate stretch", new Vector3(0, 0, .055f), .575f, true);
            Synthetic("Z +.061 over rise limit", new Vector3(0, 0, .061f), 1f, false);
            Synthetic("Z -.395 Lab sit", new Vector3(0, 0, -.395f), 1.63f, true);
            Synthetic("Z -.50 excessive sink", new Vector3(0, 0, -.5f), 1.63f, false);
            Synthetic("Y -.106 forward look", new Vector3(0, -.106f, 0), 1f, true);
            Synthetic("Y -.166 incompatible family remains rejected", new Vector3(0, -.166f, 0), 1.01f, false);
            Synthetic("Y +.153 incompatible family remains rejected", new Vector3(0, .153f, 0), 1.63f, false);
            foreach (bool weighted in new[]{false,true})
            {
                var clip = new AnimationClip();
                try
                {
                    var left = new Keyframe(0,restSynthetic.z,0,1);
                    var right = new Keyframe(1,restSynthetic.z,-1,0);
                    if(weighted) { left.weightedMode=WeightedMode.Out;left.outWeight=.6f;right.weightedMode=WeightedMode.In;right.inWeight=.2f; }
                    AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("inu/"+RootBoneSuffix,typeof(Transform),"m_LocalPosition.z"),new AnimationCurve(left,right));
                    v.Check(!RootPositionSafe(clip,"interior overshoot",restSynthetic,1,new StringBuilder()),"interior overshoot rejected; weighted="+weighted);
                }
                finally { Object.DestroyImmediate(clip); }
            }
            foreach (int variant in new[]{0,1,2})
            {
                var clip = new AnimationClip();
                try
                {
                    var curve = AnimationCurve.Constant(0, 1, variant == 1 ? .01f : 0f);
                    if (variant == 2) { var k = curve.keys[0]; k.outTangent = 1; curve.MoveKey(0,k); }
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("inu/アーマチュア",typeof(Transform),"m_LocalPosition.x"),curve);
                    v.Check((PositionCurveOutsideSkeleton(clip) == null) == (variant == 0),"armature neutral/nonzero/tangent regression " + variant);
                }
                finally { Object.DestroyImmediate(clip); }
            }
            v.Finish("Logs/imp-dog-007-rootcheck-selftest.txt");
        }

    }
}
