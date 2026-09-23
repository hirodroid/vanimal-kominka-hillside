using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-DOG-005: 犬ユニット (犬定義 + 骨格解決) の診断と移行。
    ///
    /// 実行順:
    ///   1 - Diagnose Dog Rig      … KD_DogRig が現行の名前検索と同じ結果を出すかを照合
    ///   2 - Migrate Legacy Names  … 1 匹版の名前へ犬種サフィックスを付ける (**要シーン保存済み**)
    ///   3 - Verify Dog Unit Names … 移行結果の点検
    /// </summary>
    public static class KD_DogUnitEditor
    {
        // 参考実装との照合用。**この数値は IMP-DOG-005 以前のハードコード値**で、
        // 新しい導出式が同じ場所に着地することを示すためだけに残す
        static readonly Vector3 LegacyHeadZoneOffset = new Vector3(0f, 0.061f, 0.049f);
        static readonly Vector3 LegacyCheekOffsetL = new Vector3(-0.02f, 0f, 0.01f);
        static readonly Vector3 LegacyCheekOffsetR = new Vector3(0.02f, 0f, 0.01f);
        static readonly Vector3 LegacyChinOffset = new Vector3(0f, -0.035f, 0.02f);
        static readonly Vector3 LegacyMouthOffset = new Vector3(0f, -0.01f, 0.06f);
        static readonly Vector3 LegacyForeheadDogLocal = new Vector3(0f, 0.343f, 0.312f);

        // 犬種プレハブ。ここで見るのは「解決器が 1 段構え / 耳 PB の有無 / 尻尾 PB ルートの
        // 違いを正しく扱えるか」だけ (投入済みかどうかとは独立に、素のプレハブで確かめる)
        // (犬種プレハブの一覧は IMP-DOG-020 で KD_DogDefs.All へ一本化した — 手書きの名簿を 2 つ持たない)

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Unit/1 - Diagnose Dog Rig")]
        public static void DiagnoseRig()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogUnit] Diagnose dog rig (IMP-DOG-005 Phase 1)");
            int fail = 0;
            void Check(bool cond, string what)
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + what);
                if (!cond) fail++;
            }

            // ============================================================
            // A. シーンのコーギー — 新解決器 vs 現行の名前検索 (参照一致)
            // ============================================================
            log.AppendLine();
            log.AppendLine("== A. シーンのコーギー: 新解決器 vs 現行の名前検索 ==");

            var def = KD_DogDefs.Corgi;
            // IMP-INF-003: GameObject.Find は非アクティブを取りこぼすので解決器へそろえる
            var dog = KD_SceneObjects.Find(def.DogName) ?? KD_SceneObjects.Find("KD_Dog"); // 移行前は旧名
            Check(dog != null, "犬が見つかる (" + def.DogName + " または旧名 KD_Dog)");
            if (dog == null)
            {
                Finish(log, fail);
                return;
            }
            log.AppendLine("  info 対象: " + dog.name);

            var rig = KD_DogRig.Resolve(dog);
            log.AppendLine("  info " + rig.Summary());
            foreach (var p in rig.problems) log.AppendLine("  ---- problem: " + p);
            Check(rig.Ok, "解決に problem が無い");

            // 現行のロジック (ko-gi- 決め打ち) をそのまま再現して突き合わせる
            Check(rig.TwoTier, "コーギーは 2 段構えと判定される");
            Check(rig.meshSkelRoot != null && rig.meshSkelRoot.name == "ko-gi-",
                "メッシュ側骨格 == ko-gi- (actual " +
                (rig.meshSkelRoot == null ? "null" : rig.meshSkelRoot.name) + ")");

            Check(rig.head == LegacyKogi(dog, "Bone.004", "Bone.003"), "head が現行の名前検索と参照一致");
            Check(rig.chin == LegacyKogi(dog, "Bone.006", "Bone.004"), "chin が現行の名前検索と参照一致");
            Check(rig.cheekL == LegacyKogi(dog, "Bone.009_L", "Bone.004"), "cheekL が参照一致");
            Check(rig.cheekR == LegacyKogi(dog, "Bone.009_R", "Bone.004"), "cheekR が参照一致");
            Check(rig.earL == LegacyKogi(dog, "Bone.008_L", null), "earL が参照一致");
            Check(rig.earR == LegacyKogi(dog, "Bone.008_R", null), "earR が参照一致");

            // 書き先 (KD_DogPetController.sourceBone / tailSourceBone と一致すること)
            var legacyHeadSrc = LegacyConstraintSource(LegacyKogi(dog, "Bone.004", "Bone.003"));
            var legacyTailSrc = LegacyConstraintSource(LegacyKogi(dog, "Bone.019", "Bone.018"));
            Check(rig.headWriteBone != null && rig.headWriteBone == legacyHeadSrc,
                "headWriteBone == 現行 sourceBone (アーマチュア/Bone.004)");
            Check(rig.tailWriteBone != null && rig.tailWriteBone == legacyTailSrc,
                "tailWriteBone == 現行 tailSourceBone (アーマチュア/Bone.018)");

            Check(rig.hasEarPhysBone, "耳 PhysBone あり (コーギー)");
            Check(rig.face != null && rig.face.name == "karada", "face == karada");
            Check(rig.metoziIndex >= 0 && rig.egaoIndex >= 0, "metozi/egao の index が引ける");

            // 実際に配線されている値とも突き合わせる (自己整合の PASS を避ける)
            int petCount;
            var petGo = KD_SceneObjects.Find(def.PetSystemName, out petCount)
                        ?? KD_SceneObjects.Find("KD_PetSystem", out petCount);
            if (petGo != null)
            {
                var pet = petGo.GetComponent<VRC.Udon.UdonBehaviour>() != null
                    ? UdonSharpEditor.UdonSharpEditorUtility.GetProxyBehaviour(
                        petGo.GetComponent<VRC.Udon.UdonBehaviour>()) as KD_DogPetController
                    : null;
                if (pet != null)
                {
                    Check(pet.sourceBone == rig.headWriteBone, "配線済み sourceBone == headWriteBone");
                    Check(pet.tailSourceBone == rig.tailWriteBone, "配線済み tailSourceBone == tailWriteBone");
                    Check(pet.faceRenderer == rig.face, "配線済み faceRenderer == rig.face");
                }
                else
                {
                    log.AppendLine("  info KD_DogPetController proxy が取れず配線照合はスキップ");
                }
            }

            // ============================================================
            // B. 体格からのオフセット導出が旧ハードコード値を再現するか
            // ============================================================
            log.AppendLine();
            log.AppendLine("== B. オフセット導出 (headScale 比) が旧値を再現するか ==");
            log.AppendLine("  info headScale=" + rig.headScale.ToString("F5") +
                " / 基準 CorgiHeadScale=" + KD_DogRig.CorgiHeadScale.ToString("F5"));

            Check(Mathf.Abs(rig.headScale - KD_DogRig.CorgiHeadScale) < 0.0005f,
                "headScale が基準値と一致 (±0.5mm)");

            CheckOffset(Check, "headZone", rig.HeadZoneOffset, LegacyHeadZoneOffset);
            CheckOffset(Check, "cheekL", rig.CheekOffsetL, LegacyCheekOffsetL);
            CheckOffset(Check, "cheekR", rig.CheekOffsetR, LegacyCheekOffsetR);
            CheckOffset(Check, "chin", rig.ChinOffset, LegacyChinOffset);
            CheckOffset(Check, "mouth", rig.MouthOffset, LegacyMouthOffset);

            // おでこ点 (BUG-PET-001 の判定に使う): 旧ハードコードと ±1cm 以内
            var legacyForehead = dog.transform.TransformPoint(LegacyForeheadDogLocal);
            float dForehead = Vector3.Distance(rig.ForeheadPoint, legacyForehead);
            Check(dForehead < 0.01f,
                "おでこ導出点が旧ハードコード値の ±1cm 以内 (actual " +
                (dForehead * 1000f).ToString("F1") + " mm)");

            // 既存アンカーが新しい許容内に収まるか (Verify を相対化しても落ちないこと)
            var headZoneAnchor = rig.head != null ? rig.head.Find("KD_PetAnchor_HeadZone") : null;
            if (headZoneAnchor != null)
            {
                var expected = rig.head.position + dog.transform.TransformVector(rig.HeadZoneOffset);
                float d = Vector3.Distance(headZoneAnchor.position, expected);
                Check(d <= rig.PositionTolerance,
                    "既存 headZoneAnchor が導出位置の許容内 (差 " + (d * 1000f).ToString("F1") +
                    " mm <= 許容 " + (rig.PositionTolerance * 1000f).ToString("F1") + " mm)");
            }
            else
            {
                log.AppendLine("  info KD_PetAnchor_HeadZone が無いため位置照合はスキップ");
            }

            // ============================================================
            // C. 3 犬種のプレハブで解決できるか (シーンには置かない)
            // ============================================================
            log.AppendLine();
            log.AppendLine("== C. 全犬種プレハブの解決 (KD_DogDefs.All。投入は IMP-DOG-006 / 007 / 010 / 020) ==");
            foreach (var breedDef in KD_DogDefs.All)
            {
                string name = breedDef.displayName;
                string guid = breedDef.prefabGuid;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Check(false, name + ": プレハブが見つからない guid=" + guid);
                    continue;
                }
                var r = KD_DogRig.Resolve(prefab);
                log.AppendLine("  info " + name + " -> " + r.Summary());
                foreach (var p in r.problems) log.AppendLine("  ---- problem: " + p);
                Check(r.Ok, name + ": 解決に problem が無い");
                Check(r.headWriteBone != null, name + ": 頭の書き先が決まる");
                // IMP-DOG-020: 尻尾 PB を Setup が足す犬種は、素のプレハブでは書き先が決まらないのが正
                if (breedDef.addTailPhysBone)
                    Check(!r.HasTailPhysBone, name + ": 素のプレハブに尻尾 PB が無い (addTailPhysBone — Setup が足す)");
                else
                    Check(r.tailWriteBone != null, name + ": 尻尾の書き先が決まる");
                Check(r.headScale > 0.001f, name + ": headScale が測れる (" + r.headScale.ToString("F4") + ")");
                Check(r.face != null, name + ": 表情 Renderer が決まる");
            }

            Finish(log, fail);
        }

        // ================================================================
        // Phase 2: 移行 — 1 匹版の名前へ犬種サフィックスを付ける
        // ================================================================

        /// <summary>
        /// 旧名 (KD_Dog / KD_FetchSystem / …) を犬種サフィックス付きへ改名する。
        ///
        /// **配線は全て参照なので、改名で切れるものは原理上ない。**
        /// アセットは <c>AssetDatabase.RenameAsset</c> (GUID を保つ) なので
        /// Animator / プレハブ / Program Asset の参照も切れない。
        /// それでも NetworkID だけは実測で確かめる (改名の前後で件数を出す)。
        ///
        /// **冪等** — 既に新名なら skip する。2 回叩いても壊れない。
        ///
        /// 前提: **シーンを保存しておくこと。** 未保存の変更があると、
        /// 移行が気に入らなかったときに戻す先が無くなる。
        /// </summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Dog Unit/2 - Migrate Legacy Names")]
        public static void MigrateLegacyNames()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogUnit] Migrate legacy names (IMP-DOG-005 Phase 2)");

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty)
            {
                log.AppendLine("FAIL シーンが未保存 (dirty)。先に保存してから実行する —");
                log.AppendLine("     移行が気に入らなかったときに戻す先が無くなるため");
                FinishMigrate(log, 1);
                return;
            }
            log.AppendLine("scene=" + scene.name + " dirty=" + scene.isDirty);

            // 移行前の NetworkID
            var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            LogNetworkIds(log, desc, "before");

            int renamed = 0, skipped = 0, fail = 0;

            // ---- シーンオブジェクト ----
            var sceneNames = KD_DogDefs.LegacySceneNames;
            for (int i = 0; i < sceneNames.GetLength(0); i++)
            {
                string oldName = sceneNames[i, 0];
                string newName = sceneNames[i, 1];

                int oldCount, newCount;
                var oldGo = KD_SceneObjects.Find(oldName, out oldCount);
                KD_SceneObjects.Find(newName, out newCount);

                if (oldCount == 0 && newCount == 1)
                {
                    log.AppendLine("  skip  " + oldName + " -> " + newName + " (移行済み)");
                    skipped++;
                    continue;
                }
                if (oldCount != 1 || newCount != 0)
                {
                    log.AppendLine("  FAIL  " + oldName + " -> " + newName +
                        " (旧名 " + oldCount + " 個 / 新名 " + newCount + " 個。それぞれ 1 / 0 が前提)");
                    fail++;
                    continue;
                }
                Undo.RecordObject(oldGo, "Migrate dog unit names");
                oldGo.name = newName;
                EditorUtility.SetDirty(oldGo);
                log.AppendLine("  ok    " + oldName + " -> " + newName);
                renamed++;
            }

            // ---- アセット (GUID を保つ RenameAsset。参照は切れない) ----
            var assetPaths = KD_DogDefs.LegacyAssetPaths;
            for (int i = 0; i < assetPaths.GetLength(0); i++)
            {
                string oldPath = assetPaths[i, 0];
                string newPath = assetPaths[i, 1];

                bool oldExists = AssetDatabase.LoadAssetAtPath<Object>(oldPath) != null;
                bool newExists = AssetDatabase.LoadAssetAtPath<Object>(newPath) != null;

                if (!oldExists && newExists)
                {
                    log.AppendLine("  skip  " + oldPath + " -> " + newPath + " (移行済み)");
                    skipped++;
                    continue;
                }
                if (!oldExists)
                {
                    log.AppendLine("  FAIL  " + oldPath + " が無い");
                    fail++;
                    continue;
                }
                if (newExists)
                {
                    log.AppendLine("  FAIL  " + newPath + " が既にある (旧新の両方が存在)");
                    fail++;
                    continue;
                }
                string newFileName = Path.GetFileNameWithoutExtension(newPath);
                string err = AssetDatabase.RenameAsset(oldPath, newFileName);
                if (!string.IsNullOrEmpty(err))
                {
                    log.AppendLine("  FAIL  RenameAsset " + oldPath + " -> " + newFileName + " : " + err);
                    fail++;
                    continue;
                }
                log.AppendLine("  ok    " + oldPath + " -> " + newPath + " (GUID 維持)");
                renamed++;
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);

            LogNetworkIds(log, desc, "after");
            log.AppendLine("renamed=" + renamed + " skipped=" + skipped + " fail=" + fail);
            log.AppendLine(fail == 0
                ? "MIGRATE RESULT: PASS (scene not saved yet — 3 - Verify を通してから保存する)"
                : "MIGRATE RESULT: FAIL (" + fail + " 件)");
            FinishMigrate(log, fail);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Unit/3 - Verify Dog Unit Names")]
        public static void VerifyNames()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogUnit] Verify dog unit names");
            int fail = 0;
            void Check(bool cond, string what)
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + what);
                if (!cond) fail++;
            }

            // IMP-INF-003: 犬の Transform はループの外で 1 回だけ引く
            var dogTransforms = KD_SceneObjects.AllDogTransforms();

            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("-- " + def.displayName + " (" + def.key + ")");

                // IMP-INF-003: 犬ユニットのまとまり。**「犬 1 匹 = 1 フォルダ」がこの概念の実体**
                // (このメニューの名前が literally "Verify Dog Unit Names")
                int unitCount;
                var unit = KD_SceneObjects.Find(def.UnitName, out unitCount);
                Check(unitCount == 1, def.UnitName + " がシーン内にちょうど 1 個 (actual " + unitCount + ")");

                // 犬ユニットの中身。**StopAnchorName は IMP-INF-003 まで漏れていた** —
                // 重複や欠落が名前検査から見えていなかった
                foreach (var n in new[]
                {
                    def.DogName, def.FetchSystemName, def.PetSystemName,
                    def.WanderSystemName, def.StopAnchorName,
                })
                {
                    int c;
                    var go = KD_SceneObjects.Find(n, out c);
                    Check(c == 1, n + " がシーン内にちょうど 1 個 (actual " + c + ")");
                    Check(go != null && unit != null && go.transform.parent == unit.transform,
                        n + " が " + def.UnitName + " の直下 (犬 1 匹が 1 まとまり)");
                }

                // 庭に置いてあるもの。**犬ユニットには入れない** —
                // VRCObjectSync の Respawn 先が庭の world 位置そのものなので、
                // 動きうる親 (ユニットのまとまり) の下には置かない
                // (リセットボタンは 2026-09-03 に廃止。呼び出しは手元メニューへ — IMP-FET-011)
                foreach (var n in new[] { def.BallName })
                {
                    int c;
                    var go = KD_SceneObjects.Find(n, out c);
                    Check(c == 1, n + " がシーン内にちょうど 1 個 (actual " + c + ")");
                    string offender;
                    Check(go != null && !KD_SceneObjects.IsUnderAnyDog(go.transform, dogTransforms)
                            && KD_SceneObjects.HasStaticAncestry(go, out offender),
                        n + " が動かないコンテナの下にある (犬ユニットの中ではない)");
                }

                var unitAssetPaths = new System.Collections.Generic.List<string>
                {
                    def.ControllerPath, def.PickupPosePath, def.BallPrefabPath, def.BallMaterialPath,
                };
                if (def.LieDownIsDerived) unitAssetPaths.Add(def.LieDownDerivedPath); // BUG-DOG-002 の生成物
                foreach (var p in unitAssetPaths)
                {
                    Check(AssetDatabase.LoadAssetAtPath<Object>(p) != null, "アセットがある: " + p);
                }
            }

            // 旧名が残っていないこと (残っていると Setup が二重に当たる)
            var sceneNames = KD_DogDefs.LegacySceneNames;
            for (int i = 0; i < sceneNames.GetLength(0); i++)
            {
                int c;
                KD_SceneObjects.Find(sceneNames[i, 0], out c);
                Check(c == 0, "旧名が残っていない: " + sceneNames[i, 0] + " (actual " + c + ")");
            }
            var assetPaths = KD_DogDefs.LegacyAssetPaths;
            for (int i = 0; i < assetPaths.GetLength(0); i++)
            {
                Check(AssetDatabase.LoadAssetAtPath<Object>(assetPaths[i, 0]) == null,
                    "旧アセットが残っていない: " + assetPaths[i, 0]);
            }

            // 改名で参照が切れていないことの実測 (Animator の Controller 参照 + NetworkID)
            foreach (var def in KD_DogDefs.All)
            {
                int c;
                var dogGo = KD_SceneObjects.Find(def.DogName, out c);
                var animator = dogGo == null ? null : dogGo.GetComponent<Animator>();
                Check(animator != null && animator.runtimeAnimatorController != null,
                    def.key + ": Animator に Controller が付いている");
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    string path = AssetDatabase.GetAssetPath(animator.runtimeAnimatorController);
                    Check(path == def.ControllerPath,
                        def.key + ": Controller == " + def.ControllerPath + " (actual " + path + ")");
                }
            }

            var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            Check(desc != null, "VRCSceneDescriptor exists");
            if (desc != null && desc.NetworkIDCollection != null)
            {
                int broken = 0;
                foreach (var pair in desc.NetworkIDCollection)
                    if (pair == null || pair.gameObject == null) broken++;
                Check(broken == 0, "NetworkIDs broken refs == 0 (actual " + broken +
                    ", total " + desc.NetworkIDCollection.Count + ")");
            }

            log.AppendLine();
            log.AppendLine(fail == 0 ? "VERIFY RESULT: PASS" : "VERIFY RESULT: FAIL (" + fail + " checks)");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/imp-dog-005-names.txt", log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }

        /// <summary>
        /// **名前に依存しない配線チェック。** 改名の前後で同じ結果になることが、
        /// 「改名で参照が切れていない」の直接的な証拠になる。
        ///
        /// 既存の各 Verify は旧名 (KD_Dog など) を `GameObject.Find` で引くので、
        /// 移行直後は Setup Editor を書き換えるまで使えない。ここはコンポーネント型から
        /// 辿るため、改名にも多頭化にも影響されない。
        /// </summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Unit/4 - Verify Wiring Intact")]
        public static void VerifyWiring()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogUnit] Verify wiring intact (名前に依存しない)");
            int fail = 0;
            void Check(bool cond, string what)
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + what);
                if (!cond) fail++;
            }

            var fetches = FindProxies<KD_DogFetchController>();
            var pets = FindProxies<KD_DogPetController>();
            var wanders = FindProxies<KD_DogWanderController>();
            var balls = FindProxies<KD_BallController>();
            var directors = FindProxies<KD_PhotoDirector>();

            int n = KD_DogDefs.All.Length;
            log.AppendLine("  info 犬定義 " + n + " 匹 / fetch=" + fetches.Count + " pet=" + pets.Count +
                " wander=" + wanders.Count + " ball=" + balls.Count +
                " director=" + directors.Count);

            Check(fetches.Count == n, "KD_DogFetchController が " + n + " 個");
            Check(pets.Count == n, "KD_DogPetController が " + n + " 個");
            Check(wanders.Count == n, "KD_DogWanderController が " + n + " 個");
            Check(balls.Count == n, "KD_BallController が " + n + " 個");
            // **撮影は多頭対応後も 1 個のまま** (IMP-CAM-002 で維持を再確認)。
            // 犬ごとに複製すると、クールダウン・スロット・公平性が犬の数だけ分裂して
            // 「アルバム全体をどう配分するか」を誰も持たなくなる。
            // 犬は Director の中で番号で持つ
            Check(directors.Count == 1, "KD_PhotoDirector が 1 個 (犬は Director の中で番号で持つ)");

            foreach (var fetch in fetches)
            {
                string tag = "[" + fetch.gameObject.name + "] ";
                Check(fetch.dogRoot != null, tag + "fetch.dogRoot");
                Check(fetch.dogAnimator != null, tag + "fetch.dogAnimator");
                Check(fetch.mouthAnchor != null, tag + "fetch.mouthAnchor");
                Check(fetch.ballController != null, tag + "fetch.ballController");
                Check(fetch.petController != null, tag + "fetch.petController");

                if (fetch.dogRoot != null && fetch.dogAnimator != null)
                    Check(fetch.dogAnimator.transform == fetch.dogRoot,
                        tag + "dogAnimator と dogRoot が同じ犬");
                if (fetch.dogRoot != null && fetch.mouthAnchor != null)
                    Check(IsUnder(fetch.mouthAnchor, fetch.dogRoot), tag + "mouthAnchor が犬の配下");
                if (fetch.dogRoot != null && fetch.dogAnimator != null)
                    Check(fetch.dogAnimator.runtimeAnimatorController != null,
                        tag + "Animator に Controller が付いている");

                // 撫で側と同じ犬を見ているか
                var pet = fetch.petController;
                if (pet != null && fetch.dogRoot != null)
                    Check(pet.dogRoot == fetch.dogRoot, tag + "pet と fetch が同じ犬を指す");

                // ボール
                var ball = fetch.ballController;
                if (ball != null)
                {
                    Check(ball.GetComponent<VRC.SDK3.Components.VRCObjectSync>() != null,
                        tag + "ball に VRCObjectSync");
                    Check(ball.GetComponent<VRC.SDKBase.VRC_Pickup>() != null, tag + "ball に VRCPickup");
                    Check(ball.fetchController == null,
                        tag + "ball.fetchController はエディタ時 null (実行時配線)");
                }
            }

            foreach (var pet in pets)
            {
                string tag = "[" + pet.gameObject.name + "] ";
                Check(pet.dogRoot != null, tag + "pet.dogRoot");
                Check(pet.dogAnimator != null, tag + "pet.dogAnimator");
                Check(pet.headAnchor != null, tag + "pet.headAnchor");
                Check(pet.headZoneAnchor != null, tag + "pet.headZoneAnchor");
                Check(pet.cheekAnchorL != null && pet.cheekAnchorR != null, tag + "pet.cheekAnchorL/R");
                Check(pet.chinAnchor != null, tag + "pet.chinAnchor");
                Check(pet.sourceBone != null, tag + "pet.sourceBone");
                Check(pet.tailSourceBone != null, tag + "pet.tailSourceBone");
                Check(pet.faceRenderer != null, tag + "pet.faceRenderer");
                Check(pet.heartFx != null && pet.heartFx2 != null, tag + "pet.heartFx / heartFx2");

                // 骨格解決器と突き合わせる (改名しても書き先が変わっていないこと)
                if (pet.dogRoot != null)
                {
                    var rig = KD_DogRig.Resolve(pet.dogRoot.gameObject);
                    Check(rig.Ok, tag + "骨格が解決できる");
                    Check(pet.sourceBone == rig.headWriteBone, tag + "sourceBone == rig.headWriteBone");
                    Check(pet.tailSourceBone == rig.tailWriteBone, tag + "tailSourceBone == rig.tailWriteBone");
                    Check(pet.faceRenderer == rig.face, tag + "faceRenderer == rig.face");
                    foreach (var a in new[] { pet.headAnchor, pet.headZoneAnchor,
                                              pet.cheekAnchorL, pet.cheekAnchorR, pet.chinAnchor })
                        if (a != null) Check(IsUnder(a, pet.dogRoot), tag + a.name + " が犬の配下");
                }
            }

            foreach (var w in wanders)
            {
                string tag = "[" + w.gameObject.name + "] ";
                Check(w.dogRoot != null, tag + "wander.dogRoot");
                Check(w.dogAnimator != null, tag + "wander.dogAnimator");
                Check(w.fetchController != null, tag + "wander.fetchController");
                Check(w.areaCenter != null, tag + "wander.areaCenter");
                if (w.fetchController != null && w.dogRoot != null)
                    Check(w.fetchController.dogRoot == w.dogRoot, tag + "wander と fetch が同じ犬");
                // IMP-INF-001: 移動 (locomotion) の調整値は Fetch と Wander が同名で持ち、
                // **同じ犬の同じ Animator を叩くので値は 1 つしか無い** (BUG-DOG-001 の
                // 「擦って見えない速さは 1 つ」と同じ論法を 8 本へ広げた)。
                // 旧: Tooltip の「片方だけ直さないこと」だけが頼りで、実際 deceleration は
                // Play フィードバック 3 巡 (5→12→24→36) の間ずれる危険に晒され続けた。
                // コメントで約束せず機械で見る。
                // **turnSpeedDegPerSec は意図的に対象外** — 追跡 (Fetch 360) は機敏に、
                // 徘徊 (Wander 300) は落ち着いて曲がる。移動中の旋回は足が動いているので
                // 滑りの問題が無く、揃える理由が無い
                var f = w.fetchController;
                if (f != null)
                {
                    void Same(float a, float b, string name) =>
                        Check(Mathf.Approximately(a, b),
                            tag + name + " が fetch と一致 (wander " + a + " / fetch " + b + ")");
                    Same(w.runSpeed, f.runSpeed, "runSpeed");
                    Same(w.walkSpeed, f.walkSpeed, "walkSpeed");
                    Same(w.acceleration, f.acceleration, "acceleration");
                    Same(w.deceleration, f.deceleration, "deceleration");
                    Same(w.stopSpeedThreshold, f.stopSpeedThreshold, "stopSpeedThreshold");
                    Same(w.runAnimSpeed, f.runAnimSpeed, "runAnimSpeed");
                    Same(w.stopAnimHold, f.stopAnimHold, "stopAnimHold");
                    // IMP-FET-008: 静止中の旋回 (対面 / 帰りの向き直り) — 従来からの 1 本
                    Same(w.idleTurnSpeedDegPerSec, f.idleTurnSpeedDegPerSec, "idleTurnSpeedDegPerSec");
                    // 接地はレイヤーだけでなくレイの形も揃える (SnapToGround は 3 実装ある)
                    Check(w.groundLayerMask == f.groundLayerMask, tag + "groundLayerMask が fetch と一致");
                    Same(w.groundRayOffset, f.groundRayOffset, "groundRayOffset");
                    Same(w.groundRayDistance, f.groundRayDistance, "groundRayDistance");
                }
            }

            // IMP-INF-001: 接地は全犬で同じ地形を踏むので、Fetch 同士も一致していること。
            // これが成り立つから、下の Pack の比較は fetches[0] 1 匹ぶんで足りる (Codex 指摘)
            for (int fi = 1; fi < fetches.Count; fi++)
            {
                var fa = fetches[0];
                var fb = fetches[fi];
                string tag = "[" + fb.gameObject.name + "] ";
                Check(fb.groundLayerMask == fa.groundLayerMask
                        && Mathf.Approximately(fb.groundRayOffset, fa.groundRayOffset)
                        && Mathf.Approximately(fb.groundRayDistance, fa.groundRayDistance),
                    tag + "接地 3 値が fetch[0] (" + fa.gameObject.name + ") と一致");
            }

            // IMP-INF-001: KD_DogPack も接地 (SnapToGround) の 3 実装目を持つ。
            // 犬と同じレイ形状でなければ、押し出された先だけ高さの解釈が変わる
            var packs = FindProxies<KD_DogPack>();
            foreach (var pk in packs)
            {
                if (fetches.Count == 0) break;
                var f0 = fetches[0];
                string tag = "[" + pk.gameObject.name + "] ";
                Check(pk.groundLayerMask == f0.groundLayerMask, tag + "groundLayerMask が fetch と一致");
                Check(Mathf.Approximately(pk.groundRayOffset, f0.groundRayOffset),
                    tag + "groundRayOffset が fetch と一致 (pack " + pk.groundRayOffset +
                    " / fetch " + f0.groundRayOffset + ")");
                Check(Mathf.Approximately(pk.groundRayDistance, f0.groundRayDistance),
                    tag + "groundRayDistance が fetch と一致 (pack " + pk.groundRayDistance +
                    " / fetch " + f0.groundRayDistance + ")");
            }

            // IMP-INF-001 / D: クラスを跨ぐ定数の約束 (petState のビット詰め)。
            // KD_DogPetController.Zone* を KD_DogFetchController が petState の下位ビットへ
            // そのまま詰める — 型では守れない約束なので、定数を編集した瞬間にここで捕まえる
            // (Zone を増やしてビット幅が溢れると、Play では「両手の判定がたまに変」としか見えない)
            Check(KD_DogPetController.ZoneHead == 0 && KD_DogPetController.ZoneCheekL == 1
                && KD_DogPetController.ZoneCheekR == 2 && KD_DogPetController.ZoneChin == 3,
                "Zone 定数の並び (Head 0 / CheekL 1 / CheekR 2 / Chin 3)");
            Check(KD_DogPetController.ZoneChin <= KD_DogFetchController.PetZoneMask,
                "Zone が petState の下位ビット (PetZoneMask " +
                KD_DogFetchController.PetZoneMask + ") に収まる");
            Check((KD_DogFetchController.PetTwoHandsBit & KD_DogFetchController.PetZoneMask) == 0,
                "PetTwoHandsBit (" + KD_DogFetchController.PetTwoHandsBit +
                ") が PetZoneMask と重ならない");

            // (リセットボタンの検査は IMP-FET-011 で廃止した。復帰の口は手元メニューへ移り、
            //  `Fetch System/5 - Verify Ball Recovery` が「0 個であること」と
            //  「移し先が配線されていること」の両方を数える)

            foreach (var d in directors)
            {
                string tag = "[" + d.gameObject.name + "] ";
                Check(d.photoCapture != null && d.photoCamera != null && d.gallery != null,
                    tag + "director の撮影まわり");

                // IMP-CAM-002: 犬参照は 5 本の配列 (並びは KD_DogDefs.All)。
                // **長さがそろっていないと Director は Start で自分を止める** —
                // ずれたまま動くと「ポメのイベントでラブを撮る」になるため
                Check(d.dogRoots != null && d.dogRoots.Length == n, tag + "director.dogRoots が " + n + " 匹");
                Check(d.dogHeadAnchors != null && d.dogHeadAnchors.Length == n,
                    tag + "director.dogHeadAnchors が " + n + " 匹");
                Check(d.fetchControllers != null && d.fetchControllers.Length == n,
                    tag + "director.fetchControllers が " + n + " 匹");
                Check(d.petControllers != null && d.petControllers.Length == n,
                    tag + "director.petControllers が " + n + " 匹");
                Check(d.wanderControllers != null && d.wanderControllers.Length == n,
                    tag + "director.wanderControllers が " + n + " 匹");

                if (d.dogRoots == null) continue;
                for (int i = 0; i < d.dogRoots.Length; i++)
                {
                    string it = tag + "[" + i + "] ";
                    Transform root = d.dogRoots[i];
                    Check(root != null, it + "dogRoots");
                    if (root == null) continue;
                    // **同じ添字が同じ犬であること。** ここが名前に依存しない検査の要点 —
                    // 改名しても、犬定義を並べ替えても、取り違えだけは必ず落ちる
                    if (d.dogHeadAnchors != null && i < d.dogHeadAnchors.Length && d.dogHeadAnchors[i] != null)
                        Check(IsUnder(d.dogHeadAnchors[i], root), it + "dogHeadAnchors が同じ犬の配下");
                    if (d.fetchControllers != null && i < d.fetchControllers.Length)
                    {
                        var f = d.fetchControllers[i];
                        Check(f != null && f.dogRoot == root, it + "fetchControllers が同じ犬");
                        if (f != null)
                        {
                            Check(f.photoDirector == d, it + "fetch から director へ逆配線");
                            Check(f.dogIndex == i, it + "fetch.dogIndex == " + i + " (通知が渡す番号)");
                        }
                    }
                    if (d.petControllers != null && i < d.petControllers.Length)
                    {
                        var pc = d.petControllers[i];
                        Check(pc != null && pc.dogRoot == root, it + "petControllers が同じ犬");
                    }
                    if (d.wanderControllers != null && i < d.wanderControllers.Length)
                    {
                        var w = d.wanderControllers[i];
                        Check(w != null && w.dogRoot == root, it + "wanderControllers が同じ犬");
                        if (w != null)
                        {
                            Check(w.photoDirector == d, it + "wander から director へ逆配線");
                            Check(w.dogIndex == i, it + "wander.dogIndex == " + i + " (通知が渡す番号)");
                        }
                    }
                }
            }

            var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            Check(desc != null, "VRCSceneDescriptor exists");
            if (desc != null && desc.NetworkIDCollection != null)
            {
                int broken = 0;
                foreach (var pair in desc.NetworkIDCollection)
                    if (pair == null || pair.gameObject == null) broken++;
                Check(broken == 0, "NetworkIDs broken refs == 0 (actual " + broken +
                    ", total " + desc.NetworkIDCollection.Count + ")");
            }

            log.AppendLine();
            log.AppendLine(fail == 0 ? "WIRING RESULT: PASS" : "WIRING RESULT: FAIL (" + fail + " checks)");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/imp-dog-005-wiring.txt", log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }

        /// <summary>シーン内の U# プロキシを型で集める (名前に依存しない)。</summary>
        static System.Collections.Generic.List<T> FindProxies<T>() where T : UdonSharp.UdonSharpBehaviour
        {
            var list = new System.Collections.Generic.List<T>();
            foreach (var ub in Object.FindObjectsOfType<VRC.Udon.UdonBehaviour>(true))
            {
                var proxy = UdonSharpEditor.UdonSharpEditorUtility.GetProxyBehaviour(ub) as T;
                if (proxy != null) list.Add(proxy);
            }
            return list;
        }

        static bool IsUnder(Transform t, Transform ancestor)
        {
            for (var u = t; u != null; u = u.parent)
                if (u == ancestor) return true;
            return false;
        }

        /// <summary>
        /// シーンの犬を、**購入プレハブから新規に置いた個体**と突き合わせて差分を列挙する。
        ///
        /// 答えたい問い: **「この犬は購入アセットから何を変えたのか」**。
        /// プレハブインスタンスならオーバーライドとして見えるが、Unpack 済みだと見えない。
        /// これは Animal Wash へ横展開するときに最初に必要になる情報なので、
        /// Unpack 済みでも答えられるようにここで機械的に取る。
        ///
        /// 「プレハブへ紐い直すべきか」の判断材料にもなる —
        /// **差分が全部 Setup で再現できるものなら紐い直せる。そうでなければ失うものがある。**
        /// </summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Unit/5 - Diff Dog Against Prefab")]
        public static void DiffAgainstPrefab()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogUnit] Diff dog against purchased prefab");
            log.AppendLine("問い: この犬は購入アセットから何を変えたのか (横展開の移植情報)");

            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine();
                log.AppendLine("========= " + def.displayName + " (" + def.key + ") =========");

                int dogCount;
                var dog = KD_SceneObjects.Find(def.DogName, out dogCount);
                if (dog == null) { log.AppendLine("FAIL " + def.DogName + " が無い"); continue; }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    AssetDatabase.GUIDToAssetPath(def.prefabGuid));
                if (prefab == null) { log.AppendLine("FAIL プレハブが無い"); continue; }

                log.AppendLine("prefab instance = " +
                    PrefabUtility.IsPartOfPrefabInstance(dog) + " / allowUnpacked = " + def.allowUnpacked);

                var fresh = Object.Instantiate(prefab);
                fresh.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    DiffHierarchy(dog, fresh, log);
                }
                finally
                {
                    Object.DestroyImmediate(fresh);
                }
            }

            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/imp-dog-005-prefabdiff.txt", log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }

        static void DiffHierarchy(GameObject scene, GameObject fresh, StringBuilder log)
        {
            var sceneMap = PathMap(scene);
            var freshMap = PathMap(fresh);

            // ---- 構造 ----
            var added = new System.Collections.Generic.List<string>();
            var removed = new System.Collections.Generic.List<string>();
            foreach (var kv in sceneMap) if (!freshMap.ContainsKey(kv.Key)) added.Add(kv.Key);
            foreach (var kv in freshMap) if (!sceneMap.ContainsKey(kv.Key)) removed.Add(kv.Key);
            added.Sort(); removed.Sort();

            log.AppendLine("-- 構造 --");
            log.AppendLine("  追加された GameObject: " + added.Count);
            foreach (var p in added) log.AppendLine("    + " + p);
            log.AppendLine("  消えた GameObject: " + removed.Count);
            foreach (var p in removed) log.AppendLine("    - " + p);

            // ---- ルートの配置 (庭のどこに置いたか。差があって当然) ----
            log.AppendLine("-- 配置 (人間が決めたもの。Setup は触らない) --");
            log.AppendLine("    position=" + scene.transform.position.ToString("F3") +
                " rotation=" + scene.transform.eulerAngles.ToString("F1") +
                " scale=" + scene.transform.localScale.ToString("F3"));

            // ---- 共通パスの中身 ----
            int transformDiff = 0, componentDiff = 0, valueDiff = 0;
            var details = new System.Collections.Generic.List<string>();

            foreach (var kv in sceneMap)
            {
                Transform f;
                if (!freshMap.TryGetValue(kv.Key, out f)) continue;
                var s = kv.Value;
                if (s == scene.transform) continue; // ルートの配置は上で別に出した

                // Transform (ボーンを手で動かしていないか)
                if (Vector3.Distance(s.localPosition, f.localPosition) > 0.0005f ||
                    Quaternion.Angle(s.localRotation, f.localRotation) > 0.05f ||
                    Vector3.Distance(s.localScale, f.localScale) > 0.0005f)
                {
                    transformDiff++;
                    details.Add("  [transform] " + kv.Key +
                        "\n      pos " + f.localPosition.ToString("F4") + " -> " + s.localPosition.ToString("F4") +
                        "\n      rot " + f.localEulerAngles.ToString("F2") + " -> " + s.localEulerAngles.ToString("F2") +
                        "\n      scl " + f.localScale.ToString("F4") + " -> " + s.localScale.ToString("F4"));
                }

                // コンポーネントの増減
                var sTypes = ComponentTypeNames(s);
                var fTypes = ComponentTypeNames(f);
                foreach (var t in sTypes)
                    if (!fTypes.Contains(t)) { componentDiff++; details.Add("  [component +] " + kv.Key + " : " + t); }
                foreach (var t in fTypes)
                    if (!sTypes.Contains(t)) { componentDiff++; details.Add("  [component -] " + kv.Key + " : " + t); }

                // 値の差 (Setup が触る/触れるもの)
                valueDiff += DiffValues(kv.Key, s, f, details);
            }

            log.AppendLine("-- 中身の差 --");
            log.AppendLine("  transform=" + transformDiff + " component=" + componentDiff + " value=" + valueDiff);
            details.Sort();
            foreach (var d in details) log.AppendLine(d);

            log.AppendLine("-- 読み方 --");
            log.AppendLine("  Setup が再現できるもの (アンカーの新規作成 / 耳 PhysBone の値 / Animator) だけなら");
            log.AppendLine("  プレハブへ紐い直せる。それ以外が出たら、紐い直すと**その分を失う**。");
        }

        static int DiffValues(string path, Transform s, Transform f,
            System.Collections.Generic.List<string> details)
        {
            int n = 0;

            var sPb = s.GetComponent<VRCPhysBone>();
            var fPb = f.GetComponent<VRCPhysBone>();
            if (sPb != null && fPb != null)
            {
                if (!Mathf.Approximately(sPb.pull, fPb.pull))
                { n++; details.Add("  [physbone] " + path + " pull " + fPb.pull + " -> " + sPb.pull); }
                if (!Mathf.Approximately(sPb.spring, fPb.spring))
                { n++; details.Add("  [physbone] " + path + " spring " + fPb.spring + " -> " + sPb.spring); }
                if (!Mathf.Approximately(sPb.radius, fPb.radius))
                { n++; details.Add("  [physbone] " + path + " radius " + fPb.radius + " -> " + sPb.radius); }
                if (sPb.isAnimated != fPb.isAnimated)
                { n++; details.Add("  [physbone] " + path + " isAnimated " + fPb.isAnimated + " -> " + sPb.isAnimated); }
                if (sPb.allowCollision != fPb.allowCollision)
                { n++; details.Add("  [physbone] " + path + " allowCollision " + fPb.allowCollision + " -> " + sPb.allowCollision); }
                if (sPb.allowGrabbing != fPb.allowGrabbing)
                { n++; details.Add("  [physbone] " + path + " allowGrabbing " + fPb.allowGrabbing + " -> " + sPb.allowGrabbing); }
            }

            n += DiffConstraint<RotationConstraint>(path, "rotation", s, f, details);
            n += DiffConstraint<PositionConstraint>(path, "position", s, f, details);

            var sAn = s.GetComponent<Animator>();
            var fAn = f.GetComponent<Animator>();
            if (sAn != null && fAn != null)
            {
                string sc = sAn.runtimeAnimatorController == null ? "null" : sAn.runtimeAnimatorController.name;
                string fc = fAn.runtimeAnimatorController == null ? "null" : fAn.runtimeAnimatorController.name;
                if (sc != fc) { n++; details.Add("  [animator] " + path + " controller " + fc + " -> " + sc); }
                if (sAn.cullingMode != fAn.cullingMode)
                { n++; details.Add("  [animator] " + path + " cullingMode " + fAn.cullingMode + " -> " + sAn.cullingMode); }
                if (sAn.enabled != fAn.enabled)
                { n++; details.Add("  [animator] " + path + " enabled " + fAn.enabled + " -> " + sAn.enabled); }
            }

            var sSmr = s.GetComponent<SkinnedMeshRenderer>();
            var fSmr = f.GetComponent<SkinnedMeshRenderer>();
            if (sSmr != null && fSmr != null && sSmr.sharedMesh != null && fSmr.sharedMesh != null &&
                sSmr.sharedMesh == fSmr.sharedMesh)
            {
                for (int i = 0; i < sSmr.sharedMesh.blendShapeCount; i++)
                {
                    float a = sSmr.GetBlendShapeWeight(i), b = fSmr.GetBlendShapeWeight(i);
                    if (Mathf.Abs(a - b) < 0.01f) continue;
                    n++;
                    details.Add("  [blendshape] " + path + " " + sSmr.sharedMesh.GetBlendShapeName(i) +
                        " " + b + " -> " + a);
                }
            }

            return n;
        }

        static int DiffConstraint<T>(string path, string label, Transform s, Transform f,
            System.Collections.Generic.List<string> details) where T : Behaviour, IConstraint
        {
            var sc = s.GetComponent<T>();
            var fc = f.GetComponent<T>();
            if (sc == null || fc == null) return 0;
            int n = 0;
            if (sc.constraintActive != fc.constraintActive)
            {
                n++;
                details.Add("  [" + label + "constraint] " + path + " active " +
                    fc.constraintActive + " -> " + sc.constraintActive);
            }
            if (!Mathf.Approximately(sc.weight, fc.weight))
            {
                n++;
                details.Add("  [" + label + "constraint] " + path + " weight " + fc.weight + " -> " + sc.weight);
            }
            return n;
        }

        static System.Collections.Generic.Dictionary<string, Transform> PathMap(GameObject root)
        {
            var map = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string p = t.name;
                for (var u = t.parent; u != null && u != root.transform; u = u.parent) p = u.name + "/" + p;
                if (t == root.transform) p = "";
                if (!map.ContainsKey(p)) map[p] = t; // 同名兄弟は先頭のみ (差分の目安として十分)
            }
            return map;
        }

        static System.Collections.Generic.List<string> ComponentTypeNames(Transform t)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var c in t.GetComponents<Component>())
                if (c != null) list.Add(c.GetType().Name);
            list.Sort();
            return list;
        }

        /// <summary>
        /// Unpack 済みの犬を、購入プレハブのインスタンスへ置き直す。
        ///
        /// **前提: 失うものが無いこと。** 5 - Diff で出た差分のうち、Setup が再現できないものが
        /// 残っていたら実行しない (このメニューが自分で確かめて止まる)。
        ///
        /// **NetworkIDs の後片付けを含む。** 「犬に UdonBehaviour は無いから影響しない」と
        /// 見込んでいたが**外れた** (2026-08-13 実測: 置き直しで参照切れが 4 件出た。
        /// いずれも `SerializedTypeNames: []` の空エントリで、旧犬の配下を指していた)。
        /// オブジェクトを消しただけでは終わらない — `vrc-scene-edit-safety` に記録済みの
        /// 残骸パターンそのものなので、この操作の一部として掃除する。
        ///
        /// 実行後は **Setup チェーンを頭から流し直す**こと (アンカーとシステムの配線が
        /// 古い犬を指したまま切れているため)。
        /// </summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Unit/6 - Relink Dog To Prefab")]
        public static void RelinkDogToPrefab()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogUnit] Relink dog to prefab (IMP-DOG-005)");

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty)
            {
                log.AppendLine("FAIL シーンが未保存 (dirty)。先に保存してから実行する — 戻す先が要る");
                FinishRelink(log);
                return;
            }

            int relinked = 0, skipped = 0, failed = 0;
            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("-- " + def.displayName + " (" + def.key + ")");

                int dogCount;
                var dog = KD_SceneObjects.Find(def.DogName, out dogCount);
                if (dogCount != 1 || dog == null)
                {
                    log.AppendLine("   FAIL " + def.DogName + " が " + dogCount + " 個 (1 個が前提)");
                    failed++;
                    continue;
                }
                if (PrefabUtility.IsPartOfPrefabInstance(dog))
                {
                    log.AppendLine("   skip 既にプレハブインスタンス");
                    skipped++;
                    continue;
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    AssetDatabase.GUIDToAssetPath(def.prefabGuid));
                if (prefab == null)
                {
                    log.AppendLine("   FAIL プレハブが無い guid=" + def.prefabGuid);
                    failed++;
                    continue;
                }

                // ---- 失うものが無いことを確かめる ----
                // Setup が再現できない差分が残っていたら、置き直すとその分が消える
                var fresh = Object.Instantiate(prefab);
                fresh.hideFlags = HideFlags.HideAndDontSave;
                var lossy = new System.Collections.Generic.List<string>();
                try
                {
                    CollectIrreproducible(dog, fresh, def, lossy);
                }
                finally
                {
                    Object.DestroyImmediate(fresh);
                }
                if (lossy.Count > 0)
                {
                    log.AppendLine("   FAIL 置き直すと失う差分が " + lossy.Count + " 件ある。先に Setup 側へ取り込む:");
                    foreach (var l in lossy) log.AppendLine("      " + l);
                    failed++;
                    continue;
                }
                log.AppendLine("   失う差分なし (Setup が全部再現できる)");

                // ---- 置き直し ----
                var pos = dog.transform.position;
                var rot = dog.transform.rotation;
                var scl = dog.transform.localScale;
                int siblingIndex = dog.transform.GetSiblingIndex();
                // IMP-INF-003: **親も記録する。** 記録しないと置き直した犬が
                // 犬ユニットから抜けてルートへ戻り、Setup が次に流れるまで階層が崩れる
                var parent = dog.transform.parent;
                log.AppendLine($"   記録: pos={pos:F3} rot={rot.eulerAngles:F1} scale={scl:F3} " +
                    "parent=" + (parent == null ? "(ルート)" : KD_SceneObjects.PathOf(parent.gameObject)));

                Undo.DestroyObjectImmediate(dog);
                var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                placed.name = def.DogName;
                if (parent != null) placed.transform.SetParent(parent, true);
                placed.transform.position = pos;
                placed.transform.rotation = rot;
                placed.transform.localScale = scl;
                placed.transform.SetSiblingIndex(siblingIndex);
                Undo.RegisterCreatedObjectUndo(placed, "Relink dog to prefab");
                log.AppendLine("   置き直し完了 (プレハブインスタンス)");
                relinked++;
            }

            // ---- NetworkIDs の後片付け ----
            // オブジェクトを消しただけでは終わらない (vrc-scene-edit-safety)。
            // 消えた GameObject を指すエントリは `gameObject: {fileID: 0}` として
            // .unity に固定され、以後の Verify が延々 FAIL する
            int purged = PurgeBrokenNetworkIds(log);

            EditorSceneManager.MarkSceneDirty(scene);
            log.AppendLine("relinked=" + relinked + " skipped=" + skipped +
                " failed=" + failed + " networkIdsPurged=" + purged);
            log.AppendLine(failed == 0
                ? "RELINK RESULT: PASS — **次に Setup チェーンを頭から流し直すこと** (配線が切れている)"
                : "RELINK RESULT: FAIL");
            FinishRelink(log);
        }

        /// <summary>参照切れの NetworkID エントリを取り除く。取り除いた件数を返す。</summary>
        static int PurgeBrokenNetworkIds(StringBuilder log)
        {
            var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            if (desc == null || desc.NetworkIDCollection == null)
            {
                log.AppendLine("   WARN VRCSceneDescriptor / NetworkIDCollection が無い");
                return 0;
            }
            var list = desc.NetworkIDCollection;
            int before = list.Count, purged = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var p = list[i];
                if (p != null && p.gameObject != null) continue;
                // 型情報を持つエントリを消すのは危険 (同期対象を落とす) ので、
                // 空エントリだけを対象にする。型付きが残ったら人間の判断へ回す
                if (p != null && p.SerializedTypeNames != null && p.SerializedTypeNames.Count > 0)
                {
                    log.AppendLine("   WARN 参照切れだが型付きのエントリが残っている ID=" + p.ID +
                        " types=" + string.Join(",", p.SerializedTypeNames.ToArray()) +
                        " — 手で確認すること");
                    continue;
                }
                log.AppendLine("   purge NetworkID ID=" + (p == null ? -1 : p.ID) + " (参照切れ・型情報なし)");
                list.RemoveAt(i);
                purged++;
            }
            if (purged > 0) EditorUtility.SetDirty(desc);
            log.AppendLine("   NetworkIDs: " + before + " -> " + list.Count + " (purged " + purged + ")");
            return purged;
        }

        /// <summary>Setup が再現できない差分だけを集める (置き直しで失うもの)。</summary>
        static void CollectIrreproducible(GameObject scene, GameObject fresh, KD_DogDef def,
            System.Collections.Generic.List<string> lossy)
        {
            var sceneMap = PathMap(scene);
            var freshMap = PathMap(fresh);

            foreach (var kv in sceneMap)
            {
                Transform f;
                if (!freshMap.TryGetValue(kv.Key, out f)) continue; // 追加物 = Setup が作る
                var s = kv.Value;
                if (s == scene.transform) continue;                 // 配置は記録して戻す

                // PhysBone: 犬定義が持つ値 (耳 radius/pull・尻尾 pull/spring) は Setup が再適用する。
                // **それ以外の値がずれていたら、それは誰も再現できない手調整**
                var sPb = s.GetComponent<VRCPhysBone>();
                var fPb = f.GetComponent<VRCPhysBone>();
                if (sPb != null && fPb != null)
                {
                    bool isEar = s == FindEar(scene, "Bone.008_L") || s == FindEar(scene, "Bone.008_R");
                    bool isTail = sPb == KD_DogRig.Resolve(scene).tailPhysBone;

                    if (!Mathf.Approximately(sPb.pull, fPb.pull) && !isEar && !isTail)
                        lossy.Add("[physbone] " + kv.Key + " pull " + fPb.pull + " -> " + sPb.pull);
                    if (!Mathf.Approximately(sPb.spring, fPb.spring) && !isTail)
                        lossy.Add("[physbone] " + kv.Key + " spring " + fPb.spring + " -> " + sPb.spring);
                    if (!Mathf.Approximately(sPb.radius, fPb.radius) && !isEar)
                        lossy.Add("[physbone] " + kv.Key + " radius " + fPb.radius + " -> " + sPb.radius);
                    if (!Mathf.Approximately(sPb.stiffness, fPb.stiffness))
                        lossy.Add("[physbone] " + kv.Key + " stiffness " + fPb.stiffness + " -> " + sPb.stiffness);
                    if (!Mathf.Approximately(sPb.gravity, fPb.gravity))
                        lossy.Add("[physbone] " + kv.Key + " gravity " + fPb.gravity + " -> " + sPb.gravity);
                    if (sPb.allowCollision != fPb.allowCollision)
                        lossy.Add("[physbone] " + kv.Key + " allowCollision");
                    if (sPb.allowGrabbing != fPb.allowGrabbing)
                        lossy.Add("[physbone] " + kv.Key + " allowGrabbing");
                }

                // BlendShape の焼き込み (Setup は触らない)
                var sSmr = s.GetComponent<SkinnedMeshRenderer>();
                var fSmr = f.GetComponent<SkinnedMeshRenderer>();
                if (sSmr != null && fSmr != null && sSmr.sharedMesh != null &&
                    sSmr.sharedMesh == fSmr.sharedMesh)
                {
                    for (int i = 0; i < sSmr.sharedMesh.blendShapeCount; i++)
                        if (Mathf.Abs(sSmr.GetBlendShapeWeight(i) - fSmr.GetBlendShapeWeight(i)) > 0.01f)
                            lossy.Add("[blendshape] " + kv.Key + " " + sSmr.sharedMesh.GetBlendShapeName(i));
                }
            }
        }

        static Transform FindEar(GameObject dog, string name)
        {
            var rig = KD_DogRig.Resolve(dog);
            return name.EndsWith("_L") ? rig.earL : rig.earR;
        }

        static void FinishRelink(StringBuilder log)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/imp-dog-005-relink.txt", log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }

        static void LogNetworkIds(StringBuilder log, VRC.SDK3.Components.VRCSceneDescriptor desc, string when)
        {
            if (desc == null || desc.NetworkIDCollection == null)
            {
                log.AppendLine("  info NetworkIDs (" + when + "): descriptor が無い");
                return;
            }
            int broken = 0;
            foreach (var pair in desc.NetworkIDCollection)
                if (pair == null || pair.gameObject == null) broken++;
            log.AppendLine("  info NetworkIDs (" + when + "): total=" +
                desc.NetworkIDCollection.Count + " broken=" + broken);
        }

        static void FinishMigrate(StringBuilder log, int fail)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/imp-dog-005-migrate.txt", log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }

        // ---- helpers ----

        static void CheckOffset(System.Action<bool, string> check, string label,
            Vector3 derived, Vector3 legacy)
        {
            float d = Vector3.Distance(derived, legacy);
            check(d < 0.001f, label + " オフセットが旧値と一致 (差 " + (d * 1000f).ToString("F2") +
                " mm / derived " + derived.ToString("F4") + " legacy " + legacy.ToString("F4") + ")");
        }

        /// <summary>IMP-DOG-005 以前の名前検索 (ko-gi- 決め打ち) をそのまま再現する。照合用。</summary>
        static Transform LegacyKogi(GameObject dog, string name, string parentName)
        {
            if (dog == null) return null;
            foreach (var t in dog.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != name) continue;
                if (parentName != null && (t.parent == null || t.parent.name != parentName)) continue;
                for (var u = t.parent; u != null; u = u.parent)
                    if (u.name == "ko-gi-") return t;
            }
            return null;
        }

        static Transform LegacyConstraintSource(Transform bone)
        {
            if (bone == null) return null;
            var rc = bone.GetComponent<RotationConstraint>();
            if (rc == null || rc.sourceCount < 1) return null;
            return rc.GetSource(0).sourceTransform;
        }


        static void Finish(StringBuilder log, int fail)
        {
            log.AppendLine();
            log.AppendLine(fail == 0 ? "DIAGNOSE RESULT: PASS" : "DIAGNOSE RESULT: FAIL (" + fail + " checks)");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/imp-dog-005-rig.txt", log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }
    }
}
