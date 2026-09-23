using System.IO;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-PET-001: KD_DogPetController のセットアップと検証。
    ///
    /// 実行順 (それぞれ別実行にする。Program Asset 作成直後の同一実行内で
    /// CopyProxyToUdon すると outdated script version で失敗する — vrc-udonsharp §4):
    ///   1 - Create Program Asset → force refresh
    ///   2 - Setup Pet System
    ///   3 - Verify Pet System
    ///
    /// 再実行しても人間の調整 (判定半径・追従の速さ等) は潰さない。上書きするのは配線だけ。
    /// </summary>
    public static class KD_PetSetupEditor
    {
        const string ScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogPetController.cs";
        const string ProgramAssetPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogPetController.asset";
        const string AnchorName = "KD_PetAnchor_Head";
        const string HeadZoneAnchorName = "KD_PetAnchor_HeadZone";

        // IMP-DOG-005: ボーンの同定・書き先・オフセットは KD_DogRig が犬ごとに実測する。
        // ko-gi- 決め打ちと絶対値のオフセットはここから撤去した
        // (元の値は KD_DogRig の headScale 比として保存されている。コーギーでは同値)。

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pet System/1 - Create Program Asset")]
        public static void CreateProgramAsset()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_PetSetup] Create program asset");

            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramAssetPath) != null)
            {
                log.AppendLine("already exists: " + ProgramAssetPath);
                log.AppendLine("RESULT: PASS (skip)");
                Finish("Logs/imp-pet-001-program.txt", log);
                return;
            }
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptPath);
            if (script == null)
            {
                log.AppendLine("FAIL script not found: " + ScriptPath);
                Finish("Logs/imp-pet-001-program.txt", log);
                return;
            }
            var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = script;
            AssetDatabase.CreateAsset(asset, ProgramAssetPath);
            AssetDatabase.SaveAssets();
            log.AppendLine("created: " + ProgramAssetPath);
            log.AppendLine("RESULT: PASS (force refresh してから Setup を実行する)");
            Finish("Logs/imp-pet-001-program.txt", log);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pet System/2 - Setup Pet System")]
        public static void Setup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_PetSetup] Setup");

            bool copyOk = true;
            int done = 0;
            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("-- " + def.displayName + " (" + def.key + ")");

                int dogCount;
                var dog = KD_SceneObjects.Find(def.DogName, out dogCount);
                if (dogCount != 1 || dog == null)
                {
                    log.AppendLine("   FAIL " + def.DogName + " がシーン内に " + dogCount + " 個 (1 個が前提)");
                    copyOk = false;
                    continue;
                }

                // IMP-DOG-005: 骨格は実測で解決する (犬種で 2 段/1 段が違う)
                var rig = KD_DogRig.Resolve(dog);
                if (!rig.Ok)
                {
                    foreach (var p in rig.problems) log.AppendLine("   FAIL rig: " + p);
                    copyOk = false;
                    continue;
                }
                log.AppendLine("   rig: " + rig.Summary());

                // IMP-PET-005 耳 (2026-08-12 決定): Udon からは動かさない。PhysBone だけに任せ、
                // 手で触れると揺れるだけにする。v1〜v3 と mimi BlendShape はすべて Play 実測で棄却。
                // ここでやるのは「耳を素の PhysBone へ戻す」後始末 —
                // v2 の挿入親 (KD_EarRoot) の撤去と、v3 で立てた isAnimated を false へ戻すこと。
                //
                // IMP-DOG-005: **耳 PhysBone を持たない犬種がある** (ポメ・ラブは実測で無し)。
                // 無い犬では後始末そのものが不要なので飛ばす (対処の判断は IMP-DOG-006/007)
                RemoveEarRootIfAny(rig.earL, "KD_EarRoot_L", log);
                RemoveEarRootIfAny(rig.earR, "KD_EarRoot_R", log);
                // IMP-DOG-006: 耳 PhysBone を持たない犬種へ足す (犬定義が明示したときだけ)。
                // 足したら hasEarPhysBone を測り直す — 以降の後始末・値そろえが同じ道を通るように
                if (!rig.hasEarPhysBone && def.addEarPhysBone)
                {
                    if (AddEarPhysBone(rig.earL, def, log) & AddEarPhysBone(rig.earR, def, log))
                        rig.hasEarPhysBone = true;
                    else
                        copyOk = false;
                }
                if (rig.hasEarPhysBone)
                {
                    bool earOkL = RevertEarToPhysBoneOnly(rig.earL, def, log); // 両方走らせる
                    bool earOkR = RevertEarToPhysBoneOnly(rig.earR, def, log);
                    if (!earOkL || !earOkR) { copyOk = false; continue; }
                }
                else
                {
                    log.AppendLine("   ear PhysBone 無し -> 後始末をスキップ (耳は揺れない。IMP-DOG-006/007 で判断)");
                }

                // IMP-DOG-020: 尻尾 PhysBone を持たない犬種 (柴 / サモエド = 素 0 本) へ足す。
                // 犬定義が明示したときだけ。足したら解決し直す (書き先 tailWriteBone は PB から導く)
                if (rig.tailPhysBone == null && def.addTailPhysBone)
                {
                    if (!AddTailPhysBone(rig, def, log)) { copyOk = false; continue; }
                    rig = KD_DogRig.Resolve(dog);
                    if (!rig.Ok)
                    {
                        foreach (var p in rig.problems) log.AppendLine("   FAIL rig (尻尾 PB 追加後): " + p);
                        copyOk = false;
                        continue;
                    }
                }
                if (rig.tailPhysBone == null || rig.tailWriteBone == null)
                {
                    // KD_DogRig は尻尾 PB 無しを problem にしない (Setup が足す前に Build を通すため)。
                    // **配線するここでは必須** — 無いと撫での振りも催促も書き先が無い
                    log.AppendLine("   FAIL 尻尾 PhysBone が無い (犬定義 addTailPhysBone=" + def.addTailPhysBone +
                        ")。尻尾の書き先が決まらない");
                    copyOk = false;
                    continue;
                }

                // IMP-DOG-005: 尻尾の揺れ具合も犬定義が正本 (IMP-PET-004 の体感調整)
                ApplyTailFeel(rig, def, log);

                int metozi = rig.metoziIndex;
                int egao = rig.egaoIndex;
                log.AppendLine("   face renderer: " + rig.face.name + " metozi=" + metozi + " egao=" + egao);

                // アンカー (メッシュ側ボーンの子 = 表示中のメッシュに追従する)。既存があれば位置は触らない
                var anchor = EnsureAnchor(rig.head, AnchorName, Vector3.zero, dog.transform, log);

                // BUG-PET-001: Head ゾーンの判定点は追従ピボットと別に置く。
                // Bone.004 の原点は頭蓋の内側 (首の付け根寄り) で、手が触れる上面から 6〜7 cm 下にある。
                // そのままだとおでこが CHEEK アンカーの方が近くなる。判定点だけ頭の上面へ上げる
                // (追従ピボットを上げてはいけない — 手が乗った瞬間に方向ベクトルが 0 に潰れる)。
                // オフセットは headScale 比 (コーギーの実測絶対値と同値 — KD_DogRig)
                var headZone = EnsureAnchor(rig.head, HeadZoneAnchorName, rig.HeadZoneOffset, dog.transform, log);

                // IMP-PET-002: 頬・顎のゾーンアンカー。オフセットは犬ローカル (+X = 右 / +Z = 前)。
                // 頬はやや外側、顎はやや下 = 実際に手が触れる面へ寄せる
                var cheekL = EnsureAnchor(rig.cheekL, "KD_PetAnchor_CheekL", rig.CheekOffsetL, dog.transform, log);
                var cheekR = EnsureAnchor(rig.cheekR, "KD_PetAnchor_CheekR", rig.CheekOffsetR, dog.transform, log);
                var chin = EnsureAnchor(rig.chin, "KD_PetAnchor_Chin", rig.ChinOffset, dog.transform, log);

                // 本体はプレハブ外の素のシーンオブジェクトに置く
                // (プレハブ内の U# フィールドへ外部参照を書くと実機ビルドで null 化する — vrc-udonsharp §3)
                // 名前だけの Find は同名・非アクティブに弱いので、ルート直下を数えて判定する
                int found;
                var sysGo = KD_SceneObjects.Find(def.PetSystemName, out found);
                if (found > 1)
                {
                    log.AppendLine($"   FAIL '{def.PetSystemName}' がシーン内に {found} 個ある。手で 1 個に整理してから再実行");
                    copyOk = false;
                    continue;
                }
                if (sysGo != null && PrefabUtility.IsPartOfPrefabInstance(sysGo))
                {
                    log.AppendLine($"   FAIL 既存の '{def.PetSystemName}' がプレハブインスタンス内にある。素のシーンオブジェクトが前提 (vrc-udonsharp §3)");
                    copyOk = false;
                    continue;
                }
                if (sysGo == null)
                {
                    sysGo = new GameObject(def.PetSystemName);
                    KD_SceneObjects.PlaceIfNew(sysGo, def.UnitPath);   // IMP-INF-003: 新規のときだけ犬ユニットへ
                    sysGo.transform.position = dog.transform.position;
                    log.AppendLine("   created " + def.PetSystemName);
                }

                var proxy = sysGo.GetUdonSharpComponent<KD_DogPetController>();
                if (proxy == null)
                {
                    proxy = sysGo.AddUdonSharpComponent<KD_DogPetController>();
                    log.AppendLine("   added KD_DogPetController");
                }

                // 上書きするのは配線だけ。判定・追従のパラメータは人間の調整を保持する
                proxy.dogAnimator = dog.GetComponent<Animator>();
                proxy.headAnchor = anchor;
                proxy.headZoneAnchor = headZone;
                proxy.cheekAnchorL = cheekL;
                proxy.cheekAnchorR = cheekR;
                proxy.chinAnchor = chin;
                proxy.sourceBone = rig.headWriteBone;
                proxy.tailSourceBone = rig.tailWriteBone;
                proxy.faceRenderer = rig.face;
                proxy.metoziIndex = metozi;
                proxy.egaoIndex = egao;
                proxy.dogRoot = dog.transform;

                try
                {
                    UdonSharpEditorUtility.CopyProxyToUdon(proxy);
                    log.AppendLine("   CopyProxyToUdon ok");
                }
                catch (System.Exception e)
                {
                    copyOk = false;
                    log.AppendLine("   CopyProxyToUdon FAILED: " + e.Message);
                    log.AppendLine("   (Program Asset 作成直後なら force refresh 後にもう一度 Setup を実行する)");
                }
                EditorUtility.SetDirty(proxy);
                done++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("dogs=" + done + "/" + KD_DogDefs.All.Length);
            log.AppendLine(copyOk
                ? "SETUP RESULT: PASS (scene not saved yet)"
                : "SETUP RESULT: FAIL (backing UdonBehaviour が古いまま。再実行が必要)");
            Finish("Logs/imp-pet-001-setup.txt", log);
        }

        // デバッグ表示は 1 個だけ。**Primary の犬に付ける** (多頭で全員に出すと画面が埋まる)

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pet System/9 - Add Debug Display")]
        public static void AddDebugDisplay()
        {
            var def = KD_DogDefs.Primary;
            int found, dogCount;
            var sysGo = KD_SceneObjects.Find(def.PetSystemName, out found);
            var proxy = sysGo != null ? sysGo.GetUdonSharpComponent<KD_DogPetController>() : null;
            if (proxy == null)
            {
                Debug.LogError("[KD_PetSetup] " + def.PetSystemName + " not found. Run Setup first.");
                return;
            }
            var go = KD_SceneObjects.Find(KD_DogDefs.PetDebugTextName);
            if (go == null)
            {
                go = new GameObject(KD_DogDefs.PetDebugTextName);
                KD_SceneObjects.PlaceIfNew(go, KD_DogDefs.DebugPath);   // IMP-INF-003: 開発専用
                var tmp = go.AddComponent<TMPro.TextMeshPro>();
                tmp.fontSize = 0.6f;
                tmp.alignment = TMPro.TextAlignmentOptions.Center;
                tmp.color = Color.black;
                tmp.rectTransform.sizeDelta = new Vector2(3f, 1f);
                tmp.text = "KD_PetDebug (waiting for Play)";
                var dog = KD_SceneObjects.Find(def.DogName, out dogCount);
                go.transform.position = (dog != null ? dog.transform.position : Vector3.zero) + Vector3.up * 0.9f;
            }
            proxy.debugText = go.GetComponent<TMPro.TextMeshPro>();
            proxy.debugTextTransform = go.transform;
            UdonSharpEditorUtility.CopyProxyToUdon(proxy);
            EditorUtility.SetDirty(proxy);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[KD_PetSetup] debug display added + wired to " + def.key +
                ". 公開前に 5 - Remove で撤去すること");
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pet System/10 - Remove Debug Display")]
        public static void RemoveDebugDisplay()
        {
            var go = KD_SceneObjects.Find(KD_DogDefs.PetDebugTextName);
            if (go != null) Object.DestroyImmediate(go);
            // 全犬から外す (どの犬に付けたか分からなくても取り残さない)
            foreach (var def in KD_DogDefs.All)
            {
                int found;
                var sysGo = KD_SceneObjects.Find(def.PetSystemName, out found);
                var proxy = sysGo != null ? sysGo.GetUdonSharpComponent<KD_DogPetController>() : null;
                if (proxy == null) continue;
                proxy.debugText = null;
                proxy.debugTextTransform = null;
                UdonSharpEditorUtility.CopyProxyToUdon(proxy);
                EditorUtility.SetDirty(proxy);
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[KD_PetSetup] debug display removed");
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Pet System/3 - Verify Pet System")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_PetSetup] Verify");
            int fail = 0;
            void Check(bool cond, string what)
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + what);
                if (!cond) fail++;
            }

            var pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramAssetPath);
            Check(pa != null, "program asset exists: " + ProgramAssetPath);
            Check(pa != null && pa.sourceCsScript != null, "program asset sourceCsScript set");

            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("-- " + def.displayName + " (" + def.key + ")");
                string tag = def.key + ": ";

                int dogCount;
                var dog = KD_SceneObjects.Find(def.DogName, out dogCount);
                Check(dogCount == 1, tag + def.DogName + " exists exactly once (actual " + dogCount + ")");
                if (dog == null) continue;

                // IMP-DOG-005: Setup と同じ解決器で引き直して参照一致を見る
                // (誤配線が自己整合で PASS するのを防ぐ)
                var rig = KD_DogRig.Resolve(dog);
                foreach (var p in rig.problems) log.AppendLine("  ---- rig problem: " + p);
                Check(rig.Ok, tag + "骨格が解決できる");
                if (!rig.Ok) continue;
                log.AppendLine("  info " + rig.Summary());

                // 犬がプレハブインスタンスか (Setup が Unpack を要求していないことの担保)。
                // 例外は犬定義で明示する — 黙認しない
                bool isInstance = PrefabUtility.IsPartOfPrefabInstance(dog);
                if (def.allowUnpacked)
                    log.AppendLine("  ok   " + tag + "prefab instance = " + isInstance +
                        " (allowUnpacked=true — 犬定義で明示された例外)");
                else
                    Check(isInstance, tag + "犬がプレハブインスタンス (Setup は Unpack を要求してはならない)");

                int foundSys;
                var sysGo = KD_SceneObjects.Find(def.PetSystemName, out foundSys);
                Check(foundSys == 1, tag + def.PetSystemName + " exists exactly once (actual " + foundSys + ")");
                if (sysGo == null) continue;

                // プレハブ外の素オブジェクトであること (U# 外部参照の null 化対策の前提)
                Check(!PrefabUtility.IsPartOfPrefabInstance(sysGo),
                    tag + def.PetSystemName + " is a plain scene object (not inside a prefab instance)");
                // 独立チャネル: backing UdonBehaviour と programSource を直接見る
                var ub = sysGo.GetComponent<VRC.Udon.UdonBehaviour>();
                Check(ub != null, tag + "backing UdonBehaviour exists");
                Check(ub != null && ub.programSource != null && ub.programSource.name == "KD_DogPetController",
                    tag + "programSource == KD_DogPetController (actual " +
                    (ub == null || ub.programSource == null ? "null" : ub.programSource.name) + ")");

                var proxy = sysGo.GetUdonSharpComponent<KD_DogPetController>();
                Check(proxy != null, tag + "proxy component exists");
                if (proxy == null) continue;

                Check(proxy.dogAnimator != null && proxy.dogAnimator.gameObject == dog,
                    tag + "dogAnimator wired to " + def.DogName);
                Check(proxy.headAnchor != null && proxy.headAnchor.parent == rig.head,
                    tag + "headAnchor is child of head bone");
                // BUG-PET-001: Head ゾーンの判定点。追従ピボットと別物であること + 頭の上面にあること
                Check(proxy.headZoneAnchor != null && proxy.headZoneAnchor.parent == rig.head,
                    tag + "headZoneAnchor is child of head bone");
                Check(proxy.headZoneAnchor != null && proxy.headZoneAnchor != proxy.headAnchor,
                    tag + "headZoneAnchor is a separate transform from headAnchor");
                if (proxy.headZoneAnchor != null)
                {
                    // IMP-DOG-005: 絶対値のしきい値をやめ、体格 (headScale) 比の導出位置からの
                    // 許容で見る。コーギーでは旧しきい値と同じ判定になる (診断で 0.0mm 差を実測)
                    var expected = rig.head.position + dog.transform.TransformVector(rig.HeadZoneOffset);
                    float d = Vector3.Distance(proxy.headZoneAnchor.position, expected);
                    Check(d <= rig.PositionTolerance,
                        tag + "headZoneAnchor が頭の上面の導出位置の許容内 (差 " +
                        (d * 1000f).ToString("F1") + " mm <= " +
                        (rig.PositionTolerance * 1000f).ToString("F1") + " mm)");
                    // Head ゾーンが CHEEK より近くなる = おでこが HEAD として拾えること (症状の直接確認)
                    var forehead = rig.ForeheadPoint;
                    float dHead = Vector3.Distance(forehead, proxy.headZoneAnchor.position);
                    float dCheek = proxy.cheekAnchorL != null
                        ? Vector3.Distance(forehead, proxy.cheekAnchorL.position) : 9f;
                    Check(dHead < dCheek,
                        tag + "forehead resolves to HEAD (head " + dHead.ToString("F3") +
                        " < cheek " + dCheek.ToString("F3") + ")");
                }
                Check(proxy.cheekAnchorL != null && proxy.cheekAnchorL.parent == rig.cheekL,
                    tag + "cheekAnchorL is child of Bone.009_L");
                Check(proxy.cheekAnchorR != null && proxy.cheekAnchorR.parent == rig.cheekR,
                    tag + "cheekAnchorR is child of Bone.009_R");
                Check(proxy.chinAnchor != null && proxy.chinAnchor.parent == rig.chin,
                    tag + "chinAnchor is child of Bone.006");

                // 書き先 (2 段構えなら Constraint ソース / 1 段構えならボーン直)
                Check(proxy.sourceBone != null && proxy.sourceBone == rig.headWriteBone,
                    tag + "sourceBone == 解決した頭の書き先" +
                    (rig.TwoTier ? " (Constraint ソース)" : " (1 段構え = ボーン直)"));
                // IMP-PET-004: 尻尾の書き先
                Check(proxy.tailSourceBone != null && proxy.tailSourceBone == rig.tailWriteBone,
                    tag + "tailSourceBone == 解決した尻尾の書き先");
                // 2 段構えなら、ソースが合っていても Constraint が無効/weight 0/複数ソースだと伝播しない
                if (rig.TwoTier && rig.tailPhysBone != null)
                {
                    var pbRoot = rig.tailPhysBone.rootTransform != null
                        ? rig.tailPhysBone.rootTransform : rig.tailPhysBone.transform;
                    var tailRc = pbRoot.parent != null ? pbRoot.parent.GetComponent<RotationConstraint>() : null;
                    Check(tailRc != null && tailRc.constraintActive && tailRc.weight > 0.99f,
                        tag + "tail constraint active with weight ~1");
                    Check(tailRc != null && tailRc.sourceCount == 1 && tailRc.GetSource(0).weight > 0.99f,
                        tag + "tail constraint has single source with weight ~1");
                }

                // IMP-PET-005 耳 (2026-08-12 決定): Udon は耳へ書かない。素の PhysBone だけ。
                // IMP-DOG-005: **耳 PhysBone を持たない犬種がある** — 期待値を犬ごとに切り替える
                var earPbL = rig.earL != null ? rig.earL.GetComponent<VRCPhysBone>() : null;
                var earPbR = rig.earR != null ? rig.earR.GetComponent<VRCPhysBone>() : null;
                // IMP-DOG-006: 「足す」と決めた犬で足し漏れていたら FAIL。
                // 黙って「耳 PB 無しの犬」として通ると、決定が消えたことに気づけない
                if (def.addEarPhysBone)
                    Check(rig.hasEarPhysBone,
                        tag + "犬定義が addEarPhysBone = true なので耳 PhysBone がある " +
                        "(actual L=" + (earPbL != null) + " R=" + (earPbR != null) + ")");
                if (rig.hasEarPhysBone)
                {
                    // 発振対策の芯: PB ルートと同じボーンの Constraint が無効 = 書き手が PB 一本
                    var earRcL = rig.earL.GetComponent<RotationConstraint>();
                    var earRcR = rig.earR.GetComponent<RotationConstraint>();
                    Check((earRcL == null || !earRcL.constraintActive) &&
                          (earRcR == null || !earRcR.constraintActive),
                        tag + "ear RotationConstraints inactive (PB と書き手が競合しない = 発振しない)");
                    Check(!earPbL.isAnimated && !earPbR.isAnimated,
                        tag + "ear PhysBones isAnimated == false (v3 の書き込み前提を撤回済み)");
                    // allowCollision はシリアライズ値 1 = True (SDK の AdvancedBool)。
                    // これが落ちていると「手で触れると揺れる」が成立しない
                    Check((int)earPbL.allowCollision == 1 && (int)earPbR.allowCollision == 1,
                        tag + "ear PhysBones allowCollision == True (手で触れて揺らせる)");
                    // IMP-DOG-005: 触り心地は犬定義が正本。素の購入値 (radius 0.01) だと
                    // 手が耳に当たらず「触れて揺れる」が成立しない — 実測で確認した落とし穴
                    Check(Mathf.Approximately(earPbL.radius, def.earPbRadius) &&
                          Mathf.Approximately(earPbR.radius, def.earPbRadius),
                        tag + $"ear radius == 犬定義 {def.earPbRadius} (actual {earPbL.radius} / {earPbR.radius})");
                    Check(Mathf.Approximately(earPbL.pull, def.earPbPull) &&
                          Mathf.Approximately(earPbR.pull, def.earPbPull),
                        tag + $"ear pull == 犬定義 {def.earPbPull} (actual {earPbL.pull} / {earPbR.pull})");
                }

                // IMP-PET-004 の尻尾の揺れ具合 (犬定義が正本)
                if (rig.tailPhysBone != null)
                {
                    Check(Mathf.Approximately(rig.tailPhysBone.pull, def.tailPbPull),
                        tag + $"tail pull == 犬定義 {def.tailPbPull} (actual {rig.tailPhysBone.pull})");
                    Check(Mathf.Approximately(rig.tailPhysBone.spring, def.tailPbSpring),
                        tag + $"tail spring == 犬定義 {def.tailPbSpring} (actual {rig.tailPhysBone.spring})");
                }
                else
                {
                    // IMP-DOG-020: KD_DogRig は尻尾 PB 無しを problem にしなくなった (Setup が足す前の
                    // プレハブでも解決できるように)。**その代わりここが数える** — 無ければ FAIL
                    Check(false, tag + "tail PhysBone exists (無い犬種は犬定義 addTailPhysBone で Setup が足す)");
                }
                if (!rig.hasEarPhysBone)
                {
                    log.AppendLine("  ok   " + tag +
                        "ear PhysBone 無し (この犬種では耳は揺れない。判断は IMP-DOG-006/007)");
                }
                // v2 の挿入親が残っていないこと (耳 PB の有無に関わらず見る)
                Check(rig.earL.parent != null && rig.earL.parent.name == "Bone.004" &&
                      rig.earR.parent != null && rig.earR.parent.name == "Bone.004",
                    tag + "v2 ear roots removed (ear bones back under Bone.004)");

                // IMP-PET-005: 表情の配線 (index は名前と一致すること)
                Check(proxy.faceRenderer != null && proxy.faceRenderer.sharedMesh != null,
                    tag + "faceRenderer wired with mesh");
                Check(proxy.faceRenderer == rig.face, tag + "faceRenderer == 解決した karada renderer");
                if (proxy.faceRenderer != null && proxy.faceRenderer.sharedMesh != null)
                {
                    var mesh = proxy.faceRenderer.sharedMesh;
                    Check(proxy.metoziIndex >= 0 && proxy.metoziIndex < mesh.blendShapeCount &&
                        mesh.GetBlendShapeName(proxy.metoziIndex) == "metozi",
                        tag + "metoziIndex resolves to blendshape 'metozi'");
                    Check(proxy.egaoIndex >= 0 && proxy.egaoIndex < mesh.blendShapeCount &&
                        mesh.GetBlendShapeName(proxy.egaoIndex) == "egao",
                        tag + "egaoIndex resolves to blendshape 'egao'");
                }
                Check(proxy.dogRoot == dog.transform, tag + "dogRoot wired");
                Check(proxy.enabled, tag + "proxy enabled");

                // アンカーが PhysBone チェーンを切っていないか (vrc-physbone-colliders)
                var anchors = new[] { proxy.headAnchor, proxy.headZoneAnchor, proxy.cheekAnchorL, proxy.cheekAnchorR, proxy.chinAnchor };
                var names = new[] { "headAnchor", "headZoneAnchor", "cheekAnchorL", "cheekAnchorR", "chinAnchor" };
                for (int i = 0; i < anchors.Length; i++)
                {
                    if (anchors[i] == null) continue;
                    // 他の犬のアンカーを掴んでいないか (多頭で最も起きやすい取り違え)
                    bool underThisDog = false;
                    for (var t = anchors[i]; t != null; t = t.parent)
                        if (t == dog.transform) { underThisDog = true; break; }
                    Check(underThisDog, tag + names[i] + " がこの犬の配下");

                    bool inside = false;
                    foreach (var pb in dog.GetComponentsInChildren<VRCPhysBone>(true))
                    {
                        var root = pb.rootTransform != null ? pb.rootTransform : pb.transform;
                        for (var t = anchors[i]; t != null; t = t.parent)
                            if (t == root) { inside = true; break; }
                    }
                    Check(!inside, tag + names[i] + " is outside all PhysBone chains");
                }

                // Animator 側の受け口 (犬種ごとの Controller)
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(def.ControllerPath);
                Check(ctrl != null, tag + "controller exists: " + def.ControllerPath);
                if (ctrl != null)
                {
                    bool hasPet = false, hasIdx = false;
                    foreach (var p in ctrl.parameters)
                    {
                        if (p.name == "Pet" && p.type == AnimatorControllerParameterType.Bool) hasPet = true;
                        if (p.name == "PetIndex" && p.type == AnimatorControllerParameterType.Float) hasIdx = true;
                    }
                    Check(hasPet, tag + "controller has Bool 'Pet'");
                    Check(hasIdx, tag + "controller has Float 'PetIndex'");
                }
            }

            // NetworkIDs の参照切れ (Udon 付きオブジェクトを足したので必ず見る)
            var desc = Object.FindObjectOfType<VRCSceneDescriptor>();
            Check(desc != null, "VRCSceneDescriptor exists");
            if (desc != null && desc.NetworkIDCollection != null)
            {
                int broken = 0;
                foreach (var pair in desc.NetworkIDCollection)
                    if (pair == null || pair.gameObject == null) broken++;
                Check(broken == 0, "NetworkIDs broken refs == 0 (actual " + broken +
                    ", total " + desc.NetworkIDCollection.Count + ")");
            }

            log.AppendLine(fail == 0 ? "VERIFY RESULT: PASS" : "VERIFY RESULT: FAIL (" + fail + " checks)");
            Finish("Logs/imp-pet-001-verify.txt", log);
        }

        // ---- helpers ----

        /// <summary>ボーンの子アンカーを取得または作成する。既存なら位置を触らない。
        /// dogLocalOffset は犬ローカル (+X = 右 / +Z = 前) で指定し、新規作成時だけ適用する。</summary>
        static Transform EnsureAnchor(Transform bone, string name, Vector3 dogLocalOffset,
            Transform dogRoot, StringBuilder log)
        {
            var anchor = bone.Find(name);
            if (anchor != null)
            {
                log.AppendLine("reused " + name + " (position untouched)");
                return anchor;
            }
            var go = new GameObject(name);
            anchor = go.transform;
            anchor.SetParent(bone, false);
            // TransformVector = 回転 + スケールを反映 (非等倍の犬でもオフセットがずれない)
            anchor.position = bone.position + dogRoot.TransformVector(dogLocalOffset);
            log.AppendLine("created " + name + " under " + bone.name);
            return anchor;
        }

        /// <summary>v2 の挿入親 (KD_EarRoot) が残っていたら、ボーンを元の親へ戻して撤去する。</summary>
        static void RemoveEarRootIfAny(Transform earBone, string name, StringBuilder log)
        {
            if (earBone.parent == null || earBone.parent.name != name) return;
            var earRoot = earBone.parent;
            earBone.SetParent(earRoot.parent, true); // ワールド姿勢を保つ
            Object.DestroyImmediate(earRoot.gameObject);
            log.AppendLine("removed " + name + " (v2 ear root — superseded by v3)");
        }

        /// <summary>耳を「PhysBone だけが書く」構図へ戻す (2026-08-12)。
        ///
        /// **発振の原因は Udon ではなく、RotationConstraint と PhysBone が同じボーンを
        /// 毎フレーム取り合っていること** (診断 `Logs/imp-pet-005-diag.txt`:
        /// `RC=active` かつ `PB root=Bone.008_L(kogi)` — 書き手が 2 つ)。
        /// 尻尾が発振しないのは PB ルート (`Bone.020`) に Constraint が無く、
        /// 駆動されるのは**親** (`Bone.019`) だから。耳も同じ形にする。
        ///
        /// Constraint を切っても耳は頭に付いてくる: 親 `ko-gi-/Bone.004` が
        /// アーマチュア `Bone.004` へ Constraint で追従しており、耳ボーンの
        /// ローカル回転は 2 骨格で一致する (下で実測して警告する) ため、
        /// 「ワールド回転をコピー」と「親から継承」が同値になる。</summary>
        static bool RevertEarToPhysBoneOnly(Transform earBone, KD_DogDef def, StringBuilder log)
        {
            var pb = earBone.GetComponent<VRCPhysBone>();
            if (pb == null)
            {
                log.AppendLine("FAIL no PhysBone on " + earBone.name);
                return false;
            }
            var rc = earBone.GetComponent<RotationConstraint>();
            if (rc != null && rc.constraintActive)
            {
                // 無効化しても姿勢は変わらない: ボーンは現在のローカル回転を保持し、
                // 以降はそれを親から継承するだけ。親 ko-gi-/Bone.004 は
                // アーマチュア Bone.004 へ追従しており、アーマチュア側の耳ボーンは
                // **どのクリップも書かない** (診断で実測) ため、継承結果は
                // Constraint の出力と恒等的に一致する
                Quaternion before = earBone.rotation;
                rc.constraintActive = false;
                EditorUtility.SetDirty(rc);
                log.AppendLine("ear RotationConstraint disabled on " + earBone.name +
                    " (PB と同じボーンを取り合う = 発振の原因。書き手を PB 一本にする)" +
                    " / world rotation delta = " + Quaternion.Angle(before, earBone.rotation).ToString("F3") + " deg");
            }
            if (pb.isAnimated)
            {
                pb.isAnimated = false;
                EditorUtility.SetDirty(pb);
                log.AppendLine("ear PhysBone isAnimated -> false on " + earBone.name + " (v3 撤回)");
            }
            // IMP-DOG-005: 揺れ具合は**犬定義が正本**。ここで毎回そろえる。
            //
            // 以前は「人間の調整を潰さない」ため pull が 0.45 のときだけ 0.2 へ戻していたが、
            // その方式だと値がシーンにしか無く、横展開にも 2 匹目にも付いていかない
            // (2026-08-13 実測。素の購入プレハブは radius=0.01 で、手が耳に当たらない)。
            // 揺れ具合を変えたいときは犬定義を直す。
            if (!Mathf.Approximately(pb.pull, def.earPbPull))
            {
                log.AppendLine("   ear pull " + pb.pull + " -> " + def.earPbPull + " on " + earBone.name);
                pb.pull = def.earPbPull;
                EditorUtility.SetDirty(pb);
            }
            if (!Mathf.Approximately(pb.radius, def.earPbRadius))
            {
                log.AppendLine("   ear radius " + pb.radius + " -> " + def.earPbRadius + " on " + earBone.name +
                    " (小さいと手が当たらず「触れて揺れる」が成立しない)");
                pb.radius = def.earPbRadius;
                EditorUtility.SetDirty(pb);
            }
            log.AppendLine("   ear " + earBone.name + ": allowCollision=" + pb.allowCollision +
                " allowGrabbing=" + pb.allowGrabbing + " radius=" + pb.radius + " pull=" + pb.pull);
            return true;
        }

        /// <summary>
        /// IMP-DOG-006: 耳 PhysBone を持たない犬へ 1 個足す (犬定義が `addEarPhysBone` で明示したときだけ)。
        ///
        /// **設定はコーギーの購入プレハブの耳 PB をテンプレートにする** (2026-08-13 実測)。
        /// 「なんとなくの既定値」ではなく、この犬パックの作者が耳に対して選んだ構成を写す:
        ///   integration=Simplified / spring 0.2 / stiffness 0.2 / gravity 0 /
        ///   immobileType=AllMotion / limitType=None / allowCollision・Grabbing・Posing=True /
        ///   maxStretch 0 / multiChild=Ignore / isAnimated=false / collider 無し
        /// **radius と pull だけは犬定義が正本** (触り心地なので体格ごとに詰める)。
        ///
        /// 冪等: 既に PB があれば何もしない (呼び元が `hasEarPhysBone` で弾いている)。
        /// プレハブインスタンスへの**コンポーネント追加オーバーライド**になるので
        /// Unpack は要らない (5 - Diff では `[component +]` として出る = Setup が再現できる差分)。
        /// </summary>
        static bool AddEarPhysBone(Transform earBone, KD_DogDef def, StringBuilder log)
        {
            if (earBone == null)
            {
                log.AppendLine("   FAIL 耳ボーンが解決できていないので PhysBone を足せない");
                return false;
            }
            if (earBone.GetComponent<VRCPhysBone>() != null) return true;
            if (earBone.childCount == 0)
            {
                // 子が無い = 揺らすチェーンが無い。付けても何も動かないので黙って通さない
                log.AppendLine("   FAIL " + earBone.name + " に子ボーンが無い (PhysBone チェーンが作れない)");
                return false;
            }

            var pb = Undo.AddComponent<VRCPhysBone>(earBone.gameObject);
            pb.rootTransform = null;                 // 自分自身が根 (コーギーと同じ)
            pb.integrationType = VRC.Dynamics.VRCPhysBoneBase.IntegrationType.Simplified;
            pb.pull = def.earPbPull;                 // 犬定義が正本
            pb.spring = 0.2f;
            pb.stiffness = 0.2f;
            pb.gravity = 0f;
            pb.gravityFalloff = 0f;
            pb.immobileType = VRC.Dynamics.VRCPhysBoneBase.ImmobileType.AllMotion;
            pb.immobile = 0f;
            pb.limitType = VRC.Dynamics.VRCPhysBoneBase.LimitType.None;
            pb.maxStretch = 0f;
            pb.multiChildType = VRC.Dynamics.VRCPhysBoneBase.MultiChildType.Ignore;
            pb.endpointPosition = Vector3.zero;
            pb.radius = def.earPbRadius;             // 犬定義が正本 (手が当たる大きさ)
            pb.allowCollision = VRC.Dynamics.VRCPhysBoneBase.AdvancedBool.True;
            pb.allowGrabbing = VRC.Dynamics.VRCPhysBoneBase.AdvancedBool.True;
            pb.allowPosing = VRC.Dynamics.VRCPhysBoneBase.AdvancedBool.True;
            pb.isAnimated = false;                   // Udon は耳へ書かない (IMP-PET-005 の決定)
            EditorUtility.SetDirty(pb);

            float boneLen = Vector3.Distance(earBone.position, earBone.GetChild(0).position);
            log.AppendLine("   added ear VRCPhysBone on " + earBone.name +
                " (radius=" + def.earPbRadius + " pull=" + def.earPbPull +
                " / 耳ボーン長 " + boneLen.ToString("F4") + "m — 短いと揺れが見えない。Play で判定する)");
            return true;
        }

        /// <summary>
        /// IMP-DOG-020: 尻尾 PhysBone を持たない犬へ 1 個足す (犬定義が `addTailPhysBone` で明示したときだけ)。
        ///
        /// **設定は Primary (コーギー) のシーン上の尻尾 PB を複製する** (`EditorUtility.CopySerialized`)。
        /// 耳 (<see cref="AddEarPhysBone"/>) は値を書き写したが、尻尾は limitType=Angle など
        /// 角度制限まで持っていて (2026-09-05 実測: Simplified / stiffness 0.2 / gravity 0.3 / limit Angle /
        /// radius 0.03 / collider 1)、書き写すと項目を落とす。複製してから**参照だけ**直す:
        /// rootTransform = null (自分が根) / colliders = 空 (コーギーの体の collider を指したままにしない) /
        /// pull・spring = 犬定義。
        ///
        /// 置き先は「尻尾チェーン (Bone.018 の子孫) の中で書き先が決まるボーン」:
        /// - 2 段構え: **RotationConstraint を持たない最初のボーン** (親は Constraint を持つ)。
        ///   コーギー・ラブは Bone.020 (親 Bone.019 に Constraint あり) — 同じ規則で決まる
        /// - 1 段構え: Bone.018 の最初の子 (ポメ・ダックスの Bone.019 と同じ)
        /// 子が無いボーンには付けない (揺らすチェーンが無い)。
        ///
        /// プレハブインスタンスへのコンポーネント追加オーバーライドなので Unpack は要らない。
        /// </summary>
        static bool AddTailPhysBone(KD_DogRig rig, KD_DogDef def, StringBuilder log)
        {
            var tailBase = rig.Bone("Bone.018", null);
            if (tailBase == null || tailBase.childCount == 0)
            {
                log.AppendLine("   FAIL 尻尾の根元 Bone.018 (と子) が解決できない — PhysBone を足せない");
                return false;
            }

            Transform target = null;
            if (rig.TwoTier)
            {
                for (var t = tailBase.GetChild(0); t != null; t = t.childCount > 0 ? t.GetChild(0) : null)
                {
                    bool selfRc = t.GetComponent<RotationConstraint>() != null;
                    bool parentRc = t.parent != null && t.parent.GetComponent<RotationConstraint>() != null;
                    if (!selfRc && parentRc) { target = t; break; }
                }
                if (target == null)
                {
                    log.AppendLine("   FAIL 2 段構えだが尻尾チェーンに「Constraint 無し (親は有り)」のボーンが無い — 書き先が決まらない");
                    return false;
                }
            }
            else
            {
                target = tailBase.GetChild(0);
            }
            if (target.childCount == 0)
            {
                log.AppendLine("   FAIL " + target.name + " に子ボーンが無い (PhysBone チェーンが作れない)");
                return false;
            }
            if (target.GetComponent<VRCPhysBone>() != null) return true;   // 冪等

            // 複製元 = Primary の尻尾 PB (値をここから複製する犬)
            int primaryCount;
            var primaryDog = KD_SceneObjects.Find(KD_DogDefs.Primary.DogName, out primaryCount);
            var primaryRig = primaryDog != null && primaryCount == 1 ? KD_DogRig.Resolve(primaryDog) : null;
            if (primaryRig == null || primaryRig.tailPhysBone == null)
            {
                log.AppendLine("   FAIL 複製元 (" + KD_DogDefs.Primary.DogName + " の尻尾 PB) が引けない");
                return false;
            }

            var pb = Undo.AddComponent<VRCPhysBone>(target.gameObject);
            EditorUtility.CopySerialized(primaryRig.tailPhysBone, pb);
            pb.rootTransform = null;                 // 自分自身が根 (コーギーと同じ)
            pb.colliders = new System.Collections.Generic.List<VRC.Dynamics.VRCPhysBoneColliderBase>();
            pb.pull = def.tailPbPull;                // 犬定義が正本
            pb.spring = def.tailPbSpring;
            pb.isAnimated = false;
            EditorUtility.SetDirty(pb);

            log.AppendLine("   added tail VRCPhysBone on " + target.name + " (複製元 " + primaryDog.name + "/" +
                primaryRig.tailPhysBone.name + " / pull=" + pb.pull + " spring=" + pb.spring +
                " stiffness=" + pb.stiffness + " gravity=" + pb.gravity + " limit=" + pb.limitType +
                " radius=" + pb.radius + " — 揺れ方は Play で判定する)");
            return true;
        }

        /// <summary>尻尾の揺れ具合を犬定義へそろえる (IMP-PET-004 の体感調整の正本)。</summary>
        static void ApplyTailFeel(KD_DogRig rig, KD_DogDef def, StringBuilder log)
        {
            var pb = rig.tailPhysBone;
            if (pb == null)
            {
                log.AppendLine("   WARN 尻尾 PhysBone が無い — 揺れ具合の適用をスキップ");
                return;
            }
            if (!Mathf.Approximately(pb.pull, def.tailPbPull))
            {
                log.AppendLine("   tail pull " + pb.pull + " -> " + def.tailPbPull);
                pb.pull = def.tailPbPull;
                EditorUtility.SetDirty(pb);
            }
            if (!Mathf.Approximately(pb.spring, def.tailPbSpring))
            {
                log.AppendLine("   tail spring " + pb.spring + " -> " + def.tailPbSpring);
                pb.spring = def.tailPbSpring;
                EditorUtility.SetDirty(pb);
            }
            log.AppendLine("   tail: pull=" + pb.pull + " spring=" + pb.spring + " radius=" + pb.radius);
        }

        static void Finish(string file, StringBuilder log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }
    }
}
