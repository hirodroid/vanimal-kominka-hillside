using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-DOG-003 犬の Controller: 2 - Verify AC_KD_Dog と 4 - Verify Dog Scene Setup。本体は KD_DogAnimatorBuilderEditor.cs。</summary>
    public static partial class KD_DogAnimatorBuilderEditor
    {
        // ================================================================ Verify (controller)

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Controller/2 - Verify AC_KD_Dog")]
        public static void VerifyController()
        {
            var v = new KD_VerifyLog("[KD_DogAnimatorBuilder] Verify controller");

            foreach (var dogDef in KD_DogDefs.All)
            {
                v.Info("-- " + dogDef.displayName + " (" + dogDef.key + ")");
                VerifyControllerFor(dogDef, v);
            }

            v.Finish("Logs/imp-dog-003-verify-controller.txt");
        }

        static void VerifyControllerFor(KD_DogDef dogDef, KD_VerifyLog v)
        {
            string tag = dogDef.key + ": ";

            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(dogDef.ControllerPath);
            v.Check(ctrl != null, tag + $"controller exists at {dogDef.ControllerPath}");
            if (ctrl == null) return;

            bool pumpkin = dogDef.key == "Pome" && AssetDatabase.LoadAssetAtPath<AnimationClip>(KD_PumpkinPreviewEditor.MountHopPath) != null;
            v.Check(ctrl.layers.Length == (pumpkin ? 2 : 1), tag + $"layer count matches normal/mount configuration (actual {ctrl.layers.Length})");
            // layer 0 本の壊れた Controller で以降の検査が例外落ちしないように打ち切る
            if (ctrl.layers.Length < 1) return;

            v.Check(ctrl.parameters.Length == BoolParams.Length + 4 + (pumpkin ? 3 : 0),
                tag + $"param count includes optional mount controls (actual {ctrl.parameters.Length})");
            if (pumpkin)
            {
                v.Check(ctrl.layers.Length > 1 && ctrl.layers[1].name == KD_PumpkinPreviewEditor.MountLayerName
                    && ctrl.layers[1].defaultWeight == 0, tag + "mount layer is idle by default");
                v.Check(HasParam(ctrl, "PumpkinMountAir", AnimatorControllerParameterType.Bool)
                    && HasParam(ctrl, "PumpkinMountTime", AnimatorControllerParameterType.Float)
                    && HasParam(ctrl, "PumpkinMountHoldTime", AnimatorControllerParameterType.Float), tag + "mount parameter types");
            }
            foreach (var p in BoolParams)
                v.Check(HasParam(ctrl, p, AnimatorControllerParameterType.Bool), tag + $"bool param '{p}'");
            v.Check(HasParam(ctrl, PetIndexParam, AnimatorControllerParameterType.Float),
                tag + $"float param '{PetIndexParam}'");

            // 待機バリエーションは犬種で本数が違う。期待 State 数は定義から数える
            var idleVarRefs = new System.Collections.Generic.List<KD_ClipRef>();
            foreach (var idleVariant in new[] { dogDef.idleVar1, dogDef.idleVar2, dogDef.idleVar3 })
                if (!idleVariant.IsEmpty) idleVarRefs.Add(idleVariant);
            // Idle + バリエーション + Sit/LieDown/Walk/Run/LookAt/Pickup/Pet + SleepIn/Sleep
            int expectedStates = 1 + idleVarRefs.Count + ReactionStateCount + SleepStateCount + 7;

            var sm = ctrl.layers[0].stateMachine;
            v.Check(sm.states.Length == expectedStates,
                tag + $"state count == {expectedStates} (actual {sm.states.Length})");
            v.Check(sm.defaultState != null && sm.defaultState.name == "Idle",
                tag + $"default state == Idle (actual {(sm.defaultState == null ? "null" : sm.defaultState.name)})");

            // 既定 State のクリップが待機の主 (GUID 照合)
            if (sm.defaultState != null)
            {
                var motionPath = AssetDatabase.GetAssetPath(sm.defaultState.motion);
                var guid = AssetDatabase.AssetPathToGUID(motionPath);
                v.Check(guid == dogDef.idle.guid,
                    tag + $"default motion guid == {dogDef.idle.label} (actual {guid} {motionPath})");
            }

            // 各 State の motion / クリップ対応 (GUID 照合)
            // loops: そのクリップがループ設定であるべきか。
            // **SleepIn だけ false** — `寝なで_ONOFF` は 1 回きりの出入りで、
            // ループさせると横になる動きを永久に繰り返す。**「false であること」も検査する**
            // (購入アセット側の設定が変わったら気づけるように)
            var expectList = new System.Collections.Generic.List<(string state, string guid, bool loops)>
            {
                ("Idle", dogDef.idle.guid, true),
            };
            for (int i = 0; i < idleVarRefs.Count; i++)
                expectList.Add(("Idle_var" + (i + 1), idleVarRefs[i].guid, true));
            expectList.Add(("Sit", EffectiveSit(dogDef).guid, true));
            // BUG-DOG-002: 派生する犬は KD_LieDown_{key} の GUID (原本 huse が当たっていたら FAIL)
            expectList.Add(("LieDown", EffectiveLieDown(dogDef).guid, true));
            expectList.Add(("Walk", dogDef.walk.guid, true));
            expectList.Add(("Run", dogDef.run.guid, true));
            expectList.Add(("LookAt", dogDef.lookAt.guid, true));
            expectList.Add(("Pickup", AssetDatabase.AssetPathToGUID(dogDef.PickupPosePath), true));
            expectList.Add(("SleepIn", dogDef.sleepInOut.guid, false));
            expectList.Add(("Sleep", dogDef.sleepLoop.guid, true));
            var expectMotion = expectList.ToArray();
            int transitionTotal = 0;
            foreach (var cs in sm.states)
            {
                transitionTotal += cs.state.transitions.Length;
                v.Check(cs.state.motion != null, tag + $"state '{cs.state.name}' has motion");
            }
            foreach (var (stateName, guid, loops) in expectMotion)
            {
                var st = FindState(sm, stateName);
                if (st == null) { v.Check(false, tag + $"state '{stateName}' exists"); continue; }
                var actual = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(st.motion));
                v.Check(actual == guid, tag + $"state '{stateName}' motion guid ({actual})");
                var clip = st.motion as AnimationClip;
                v.Check(clip != null && clip.isLooping == loops,
                    tag + $"state '{stateName}' clip loops == {loops}" +
                    (clip == null ? " (no clip)" : $" (actual {clip.isLooping})"));
                // BUG-DOG-002: 骨格の外に位置カーブを持つクリップが Controller に載っていない
                // (ルートに打つべき沈みが別ボーンにあると効かずに浮く。柴 huse の hojo_born/Bone)
                if (clip != null)
                {
                    string outside = PositionCurveOutsideSkeleton(clip);
                    v.Check(outside == null,
                        tag + $"state '{stateName}' has no position curve outside skeleton" +
                        (outside == null ? "" : $" (found '{outside}')"));
                }
            }
            // BUG-DOG-002: 派生した伏せは アーマチュア/Bone に位置カーブを持つ (移し先が正しい)
            if (dogDef.LieDownIsDerived)
            {
                var derived = AssetDatabase.LoadAssetAtPath<AnimationClip>(dogDef.LieDownDerivedPath);
                v.Check(derived != null && HasRootPositionCurve(derived),
                    tag + $"derived LieDown has {RootBoneSuffix} position curve ({dogDef.LieDownDerivedPath})");
            }

            // Pet = BlendTree
            var petRefs = PetCandidatesOf(dogDef);
            var pet = FindState(sm, "Pet");
            v.Check(pet != null, tag + "state 'Pet' exists");
            var tree = pet == null ? null : pet.motion as BlendTree;
            v.Check(tree != null, tag + "Pet motion is BlendTree");
            if (tree != null)
            {
                v.Check(tree.blendParameter == PetIndexParam, tag + $"blendParameter == {PetIndexParam}");
                v.Check(tree.children.Length == petRefs.Length,
                    tag + $"pet candidates == {petRefs.Length} (actual {tree.children.Length})");
                for (int i = 0; i < tree.children.Length && i < petRefs.Length; i++)
                {
                    var c = tree.children[i];
                    var actual = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.motion));
                    v.Check(c.motion != null && actual == petRefs[i].guid && Mathf.Approximately(c.threshold, i),
                        tag + $"pet candidate [{i}] {petRefs[i].label}");
                }
            }

            // 遷移数: 待機 N State × (ローテ 1 + リアクション 7) + リアクション 7 State × 戻り 1
            //         + 走り ⇄ 歩きの直結 2 (IMP-FET-004)
            //         + **静止リアクション 5 State (Sit/LieDown/LookAt/Pickup/Pet) × 移動への直結 2**
            //           (破壊的監査 RC-4d — Idle 経由の 0.25 秒ブレンド中に滑るのを断つ)
            //         + **全 State (待機 N + リアクション 7) から SleepIn への 1 本** と
            //           寝る側の 3 本 (SleepIn→Idle / SleepIn→Sleep / Sleep→Idle)。IMP-DOG-019
            //         コーギー (待機 4) では 51 + 11 + 3 = 65
            int idleCount = 1 + idleVarRefs.Count;
            int expectedTransitions = idleCount * (1 + ReactionStateCount)
                + ReactionStateCount + 2 + StaticReactionCount * 2
                + (idleCount + ReactionStateCount) + 3 + 21;
            v.Check(transitionTotal == expectedTransitions,
                tag + $"transition total == {expectedTransitions} (actual {transitionTotal})");
            // 静止リアクションから移動へ Idle を経由しないこと (破壊的監査 RC-4d)。
            // **「存在する」だけでなく「Idle への戻りより先に評価される」まで見る** —
            // 順序が逆だと `!Pickup` の Idle 行きが勝ち、0.25 秒のブレンドが黙って復活する
            // (追加順が評価順という Animator の仕様に依存しているので、機械検査で固定する)
            foreach (var name in new[] { "Sit", "LieDown", "LookAt", "Pickup", "Pet" })
            {
                var st = FindState(sm, name);
                if (st == null) continue; // State 自体の有無は上の検査が見ている
                foreach (var loco in new[] { "Run", "Walk" })
                {
                    int iLoco = TransitionIndexTo(st, loco);
                    int iIdle = TransitionIndexTo(st, "Idle");
                    v.Check(iLoco >= 0, tag + name + " -> " + loco + " direct transition exists");
                    v.Check(iLoco >= 0 && iIdle >= 0 && iLoco < iIdle,
                        tag + name + " -> " + loco + " is evaluated before " + name + " -> Idle");
                }
            }

            // 走り ⇄ 歩きが Idle を経由しないこと (IMP-FET-004)
            var runSt = FindState(sm, "Run");
            var walkSt = FindState(sm, "Walk");
            v.Check(runSt != null && HasTransitionTo(runSt, "Walk"), tag + "Run -> Walk direct transition exists");
            v.Check(walkSt != null && HasTransitionTo(walkSt, "Run"), tag + "Walk -> Run direct transition exists");
            // 評価順: 直結が Idle への戻りより先にあること (後だと Idle が勝って脚が飛ぶ)
            v.Check(runSt != null && TransitionIndexTo(runSt, "Walk") >= 0
                    && TransitionIndexTo(runSt, "Walk") < TransitionIndexTo(runSt, "Idle"),
                tag + "Run -> Walk is evaluated before Run -> Idle");
            v.Check(walkSt != null && TransitionIndexTo(walkSt, "Run") >= 0
                    && TransitionIndexTo(walkSt, "Run") < TransitionIndexTo(walkSt, "Idle"),
                tag + "Walk -> Run is evaluated before Walk -> Idle");

            // ---- そばで寝るが最上位であること (IMP-DOG-019 Phase 1) ----
            //
            // ① どの State からも SleepIn へ出る道があり、**その State の 0 番目に評価される**
            //    (順序が後ろだと、移動中や Pickup 中の犬が呼ばれても 1 手回り道をする)
            var sleepSourceNames = new System.Collections.Generic.List<string> { "Idle" };
            for (int i = 0; i < idleVarRefs.Count; i++) sleepSourceNames.Add("Idle_var" + (i + 1));
            sleepSourceNames.AddRange(new[] { "Sit", "LieDown", "Walk", "Run", "LookAt", "Pickup", "Pet" });
            foreach (var name in sleepSourceNames)
            {
                var st = FindState(sm, name);
                if (st == null) continue; // State 自体の有無は上の検査が見ている
                v.Check(TransitionIndexTo(st, "SleepIn") == 0,
                    tag + name + " -> SleepIn is the first transition evaluated (actual index " +
                    TransitionIndexTo(st, "SleepIn") + ")");
            }

            // ② **Sleep / SleepIn からリアクションへ出る道が 1 本も無い。**
            //    これが「寝ている間は Pet でも Fetch でも起きない」の実体で、
            //    条件の書き足しではなく State グラフの形で保証している。
            //    Phase 4 の「寝たまま撫でられる」は Sleep State の**上**で作ること
            var sleepIn = FindState(sm, "SleepIn");
            var sleep = FindState(sm, "Sleep");
            v.Check(sleepIn != null && sleep != null, tag + "states 'SleepIn' / 'Sleep' exist");
            if (sleepIn != null && sleep != null)
            {
                v.Check(sleepIn.transitions.Length == 2, tag + $"SleepIn has exactly 2 transitions (actual {sleepIn.transitions.Length})");
                v.Check(TransitionIndexTo(sleepIn, "Idle") == 0,
                    tag + "SleepIn -> Idle (!Sleep) is evaluated before SleepIn -> Sleep");
                v.Check(TransitionIndexTo(sleepIn, "Sleep") >= 0, tag + "SleepIn -> Sleep exists");

                v.Check(sleep.transitions.Length == 1, tag + $"Sleep has exactly 1 transition (actual {sleep.transitions.Length})");
                v.Check(TransitionIndexTo(sleep, "Idle") == 0, tag + "Sleep -> Idle is the only exit");
                foreach (var t in sleep.transitions)
                    v.Check(t.conditions.Length == 1 && t.conditions[0].parameter == SleepParam
                            && t.conditions[0].mode == AnimatorConditionMode.IfNot,
                        tag + "Sleep -> Idle fires only on Sleep == false");
            }

            // ブレンド長は経路ごと (IMP-FET-004): 移動の出入り 0s / 走り ⇄ 歩き 0.12s / 他 0.25s。
            // リアクション遷移は exit time 無し (例外は SleepIn -> Sleep。IMP-DOG-019)
            foreach (var cs in sm.states)
            {
                foreach (var t in cs.state.transitions)
                {
                    if (t.destinationState == null)
                    {
                        v.Check(false, $"transition from {cs.state.name} has null destination");
                        continue;
                    }
                    float want = ExpectedDuration(cs.state.name, t.destinationState.name);
                    bool dur = t.hasFixedDuration && Mathf.Approximately(t.duration, want);
                    if (!dur) v.Check(false, tag + $"transition {cs.state.name} -> {t.destinationState.name} duration {want}s (actual {t.duration})");
                    // 待機ローテの環 = Idle 系 State の条件なし遷移 (State 名は Idle / Idle_varN)
                    bool isIdleState = cs.state.name == "Idle" || cs.state.name.StartsWith("Idle_var");
                    bool isRing = isIdleState && t.conditions.Length == 0;
                    // SleepIn -> Sleep は「出入りを再生し切ってから本編へ」なので exit time で抜ける。
                    // **待機ローテと同じく exitTime 1.0 を要求する** (1 未満だと横になり切る前に
                    // 本編へ移り、繋ぎ目が飛ぶ)
                    bool isSleepEnter = cs.state.name == "SleepIn" && t.destinationState.name == "Sleep";
                    if (isRing && (!t.hasExitTime || !Mathf.Approximately(t.exitTime, 1f)))
                        v.Check(false, tag + $"ring transition {cs.state.name} -> {t.destinationState.name} exitTime 1.0");
                    if (isSleepEnter && (!t.hasExitTime || !Mathf.Approximately(t.exitTime, 1f)))
                        v.Check(false, tag + $"SleepIn -> Sleep must play the whole 寝なで_ONOFF (exitTime 1.0)");
                    if (!isRing && !isSleepEnter && t.hasExitTime)
                        v.Check(false, tag + $"reaction transition {cs.state.name} -> {t.destinationState.name} must not use exit time");
                }
            }
        }

        static bool HasParam(AnimatorController ctrl, string name, AnimatorControllerParameterType type)
        {
            foreach (var p in ctrl.parameters)
                if (p.name == name && p.type == type) return true;
            return false;
        }

        /// <summary>IMP-FET-004: 経路ごとの期待ブレンド長。</summary>
        static float ExpectedDuration(string from, string to)
        {
            if (from.StartsWith("Call")) return to == "Run" || to == "Walk" ? 0f : .2f;
            // IMP-DOG-019: 出入り → 本編は繋ぎ目が無いのでブレンドしない
            if (from == "SleepIn" && to == "Sleep") return SleepInBlendSec;
            bool fromLoco = from == "Run" || from == "Walk";
            bool toLoco = to == "Run" || to == "Walk";
            if (fromLoco && toLoco) return RunWalkBlendSec; // 走り ⇄ 歩き
            if (fromLoco || toLoco) return LocoBlendSec;    // 移動の出入り (カット)
            return BlendSec;
        }

        static int TransitionIndexTo(AnimatorState st, string destName)
        {
            if (st == null) return -1;
            for (int i = 0; i < st.transitions.Length; i++)
                if (st.transitions[i].destinationState != null
                    && st.transitions[i].destinationState.name == destName) return i;
            return -1;
        }

        static bool HasTransitionTo(AnimatorState st, string destName)
        {
            return TransitionIndexTo(st, destName) >= 0;
        }

        static AnimatorState FindState(AnimatorStateMachine sm, string name)
        {
            foreach (var cs in sm.states)
                if (cs.state.name == name) return cs.state;
            return null;
        }

        // ================================================================ Verify (scene)

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Controller/4 - Verify Dog Scene Setup")]
        public static void VerifyScene()
        {
            var v = new KD_VerifyLog("[KD_DogAnimatorBuilder] Verify scene");
            var log = v.Text;

            var scene = EditorSceneManager.GetActiveScene();
            log.AppendLine($"scene={scene.name} dirty={scene.isDirty}");

            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("-- " + def.displayName + " (" + def.key + ")");
                string tag = def.key + ": ";

                int dogCount;
                var dog = KD_SceneObjects.Find(def.DogName, out dogCount);
                v.Check(dogCount == 1, tag + $"'{def.DogName}' exists exactly once (actual {dogCount})");
                if (dog == null) continue;

                // プレハブインスタンスであること = Setup が Unpack を要求していないこと。
                // 例外は犬定義の allowUnpacked で**明示**する (黙認しない —
                // この仕組みは Animal Wash へ横展開する前提で、再現性が資産そのもの)
                var prefabPath = AssetDatabase.GUIDToAssetPath(def.prefabGuid);
                var src = PrefabUtility.GetCorrespondingObjectFromSource(dog);
                bool isInstance = src != null && AssetDatabase.GetAssetPath(src) == prefabPath;
                if (def.allowUnpacked)
                    log.AppendLine("  ok   " + tag + "prefab instance = " + isInstance +
                        " (allowUnpacked=true — 犬定義で明示された例外)");
                else
                    v.Check(isInstance, tag + "犬が " + System.IO.Path.GetFileName(prefabPath) +
                        " のインスタンス (Setup は Unpack を要求してはならない)");

                var animator = dog.GetComponent<Animator>();
                if (animator == null) animator = dog.GetComponentInChildren<Animator>(true);
                v.Check(animator != null, tag + "Animator found");
                if (animator != null)
                {
                    v.Check(animator.enabled, tag + "Animator.enabled == true");
                    var ctrlPath = AssetDatabase.GetAssetPath(animator.runtimeAnimatorController);
                    v.Check(ctrlPath == def.ControllerPath,
                        tag + $"controller == {def.ControllerPath} (actual {ctrlPath})");
                    v.Check(animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
                        tag + $"cullingMode == AlwaysAnimate (actual {animator.cullingMode})");
                }
                log.AppendLine($"  info {tag}position={dog.transform.position} yaw={dog.transform.eulerAngles.y:F1}");
            }

            // NetworkIDs の参照切れ (vrc-scene-edit-safety: 一時オブジェクト削除後の残骸検出)
            var desc = Object.FindObjectOfType<VRCSceneDescriptor>();
            v.Check(desc != null, "VRCSceneDescriptor exists");
            if (desc != null && desc.NetworkIDCollection != null)
            {
                int broken = 0;
                foreach (var pair in desc.NetworkIDCollection)
                    if (pair == null || pair.gameObject == null) broken++;
                v.Check(broken == 0, $"NetworkIDs broken refs == 0 (actual {broken}, total {desc.NetworkIDCollection.Count})");
            }
            else if (desc != null)
            {
                log.AppendLine("  info NetworkIDCollection is null (no entries)");
            }

            v.Finish("Logs/imp-dog-003-verify-scene.txt");
        }

    }
}
