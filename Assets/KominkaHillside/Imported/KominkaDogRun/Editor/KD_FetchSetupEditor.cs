using System.IO;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-FET-001: Fetch システム (KD_Ball + KD_DogFetchController) のセットアップと検証。
    ///
    /// 実行順 (それぞれ別実行にする。Program Asset 作成直後の同一実行内で
    /// CopyProxyToUdon すると outdated script version で失敗する — vrc-udonsharp §4):
    ///   1 - Create Program Assets → force refresh
    ///   2 - Setup Fetch System
    ///   3 - Verify Fetch System
    ///
    /// 前提: Dog Controller 側の 0 - Generate Pickup Pose と 1 - Build AC_KD_Dog を
    /// 先に済ませておく (Pickup State が無いと犬がくわえポーズを取れない)。
    ///
    /// 再実行しても人間の調整 (ボール位置・速度閾値・FSM タイミング等) は潰さない。
    /// 上書きするのは配線だけ。
    /// </summary>
    public static partial class KD_FetchSetupEditor
    {
        const string BallScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_BallController.cs";
        const string BallProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_BallController.asset";
        const string FetchScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogFetchController.cs";
        const string FetchProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogFetchController.asset";
        const string MouthAnchorName = "KD_FetchAnchor_Mouth";

        /// <summary>
        /// 返してもらったボールが**プレイヤーから何 m のところに落ちるか**。
        /// 人間の手の届く範囲の話なので**犬の体格に依存しない定数**。
        ///
        /// 0.223 はコーギーの実測値 (`returnStopDistance` 0.55 − 口元の前方オフセット 0.327)。
        /// コーギーのこの体感は Play で確認済みなので、それを 3 犬種の基準にする。
        ///
        /// **これが唯一の手調整点。** 「近すぎ / 遠すぎ」はここを動かす
        /// (犬ごとの値ではない — 犬ごとの差は下記のとおり自動で吸収される)。
        /// </summary>
        const float BallDropDistance = 0.223f;

        // IMP-DOG-005: 犬ごとのパス・名前・ボール径/色は KD_DogDefs、
        // ボーンの同定と口元オフセットは KD_DogRig が持つ

        // IMP-FET-011: リセットボタンは廃止 (2026-09-03)。
        // 復帰の口は手元メニューの「おもちゃ」タブ (KD_TreatPouch)。
        // Program Asset も作らない — 4 - Remove / 5 - Verify が消えたことを見張る

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Fetch System/1 - Create Program Assets")]
        public static void CreateProgramAssets()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_FetchSetup] Create program assets");

            bool ok = true;
            ok &= CreateProgramAsset(BallScriptPath, BallProgramPath, log);
            ok &= CreateProgramAsset(FetchScriptPath, FetchProgramPath, log);
            // (KD_BallResetButton の Program Asset は IMP-FET-011 で廃止した)

            log.AppendLine(ok
                ? "RESULT: PASS (force refresh してから Setup を実行する)"
                : "RESULT: FAIL");
            Finish("Logs/imp-fet-001-program.txt", log);
        }

        static bool CreateProgramAsset(string scriptPath, string programPath, StringBuilder log)
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath) != null)
            {
                log.AppendLine("already exists: " + programPath);
                return true;
            }
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (script == null)
            {
                log.AppendLine("FAIL script not found: " + scriptPath);
                return false;
            }
            var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = script;
            AssetDatabase.CreateAsset(asset, programPath);
            AssetDatabase.SaveAssets();
            log.AppendLine("created: " + programPath);
            return true;
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Fetch System/2 - Setup Fetch System")]
        public static void Setup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_FetchSetup] Setup");

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

                // Controller に Pickup が入っているか (Dog Controller 側の再生成を忘れると犬が反応しない)
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(def.ControllerPath);
                bool hasPickupParam = false;
                if (ctrl != null)
                    foreach (var p in ctrl.parameters)
                        if (p.name == "Pickup" && p.type == AnimatorControllerParameterType.Bool) hasPickupParam = true;
                if (!hasPickupParam)
                {
                    log.AppendLine("   FAIL " + def.ControllerPath +
                        " に Bool 'Pickup' が無い。Dog Controller 0 → 1 を先に実行する");
                    copyOk = false;
                    continue;
                }

                // 口元アンカー (顎ボーンの子)。既存があれば位置は触らない。オフセットは headScale 比
                var mouth = EnsureAnchor(rig.chin, MouthAnchorName, rig.MouthOffset, dog.transform, log);

                // マテリアル (自作物は Assets/KominkaHillside/Imported/KominkaDogRun 側へ — 購入アセットに混ぜない)
                var mat = AssetDatabase.LoadAssetAtPath<Material>(def.BallMaterialPath);
                if (mat == null)
                {
                    if (!AssetDatabase.IsValidFolder("Assets/KominkaHillside/Imported/KominkaDogRun/Materials"))
                        AssetDatabase.CreateFolder("Assets/KominkaHillside/Imported/KominkaDogRun", "Materials");
                    mat = new Material(Shader.Find("Standard"));
                    mat.color = def.ballColor; // 首輪の色とそろえる (DES-DOG-002 の識別層 2)
                    AssetDatabase.CreateAsset(mat, def.BallMaterialPath);
                    log.AppendLine("   created " + def.BallMaterialPath);
                }

                // ボールプレハブ。既存があれば作り直さない (人間の調整を潰さない)
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(def.BallPrefabPath);
                if (prefab == null)
                {
                    prefab = CreateBallPrefab(def, mat, log);
                    if (prefab == null) { copyOk = false; continue; }
                }
                else
                {
                    // 中身は修復しない (人間の調整を潰さない)。構成の妥当性は 3 - Verify が判定する
                    log.AppendLine("   reused existing prefab (untouched - run 3 - Verify)");
                }

                // シーンへ 1 個配置。既存があれば位置は触らない
                int ballCount;
                var ball = KD_SceneObjects.Find(def.BallName, out ballCount);
                if (ballCount > 1)
                {
                    log.AppendLine($"   FAIL '{def.BallName}' がシーン内に {ballCount} 個ある");
                    copyOk = false;
                    continue;
                }
                if (ball == null)
                {
                    ball = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    ball.name = def.BallName;
                    // IMP-INF-003: VRCObjectSync の Respawn 先は開始時の world 位置なので
                    // **動かないコンテナの下**へ置く (犬ユニットの中には入れない)
                    KD_SceneObjects.PlaceIfNew(ball, KD_DogDefs.PropsPath);
                    // 定位置はその犬の足元へ (DES-DOG-002 の識別層 3:
                    // 「犬に近づく → 足元のボールを取る」で対応が分かる)。手で動かしてよい
                    ball.transform.position = dog.transform.position
                        + dog.transform.right * 0.5f + dog.transform.forward * 0.4f + Vector3.up * 0.2f;
                    log.AppendLine($"   placed ball at {ball.transform.position} (この犬の足元。手で動かしてよい)");
                }
                else
                {
                    log.AppendLine("   reused existing " + def.BallName + " (position untouched)");
                }

                // 本体はプレハブ外の素のシーンオブジェクトに置く (vrc-udonsharp §3)
                int found;
                var sysGo = KD_SceneObjects.Find(def.FetchSystemName, out found);
                if (found > 1)
                {
                    log.AppendLine($"   FAIL '{def.FetchSystemName}' がシーン内に {found} 個ある");
                    copyOk = false;
                    continue;
                }
                if (sysGo != null && PrefabUtility.IsPartOfPrefabInstance(sysGo))
                {
                    log.AppendLine($"   FAIL 既存の '{def.FetchSystemName}' がプレハブインスタンス内にある (vrc-udonsharp §3)");
                    copyOk = false;
                    continue;
                }
                if (sysGo == null)
                {
                    sysGo = new GameObject(def.FetchSystemName);
                    KD_SceneObjects.PlaceIfNew(sysGo, def.UnitPath);   // IMP-INF-003: 新規のときだけ犬ユニットへ
                    sysGo.transform.position = dog.transform.position;
                    log.AppendLine("   created " + def.FetchSystemName);
                }

                var proxy = sysGo.GetUdonSharpComponent<KD_DogFetchController>();
                if (proxy == null)
                {
                    proxy = sysGo.AddUdonSharpComponent<KD_DogFetchController>();
                    log.AppendLine("   added KD_DogFetchController");
                }

                // 撫でコントローラ (Fetch 中の抑制先)。**同じ犬のものだけ**
                int petFound;
                var petGo = KD_SceneObjects.Find(def.PetSystemName, out petFound);
                var pet = petGo == null ? null : petGo.GetUdonSharpComponent<KD_DogPetController>();
                if (pet == null) log.AppendLine("   WARN " + def.PetSystemName + " not found - pet suppression will be unwired");

                // 上書きするのは配線だけ。FSM タイミング・速度は人間の調整を保持する。
                // **例外は returnStopDistance** (下で導出して毎回そろえる) —
                // 犬ごとに違って当然の値で、手で入れると必ずずれるため
                proxy.dogRoot = dog.transform;
                proxy.dogAnimator = dog.GetComponent<Animator>();
                proxy.mouthAnchor = mouth;
                proxy.ballController = ball.GetUdonSharpComponent<KD_BallController>();
                proxy.petController = pet;

                // IMP-NET-001: 逆向きの配線も張る。撫でコントローラは
                // **この Fetch コントローラを同期の器として使う** (撫での結果を渡し、
                // 他人の撫でをここから導出する)。Pet Setup 側ではなくここで張るのは、
                // Fetch System が後から作られても必ず両方向そろうようにするため
                if (pet != null)
                {
                    pet.fetchController = proxy;
                    try
                    {
                        UdonSharpEditorUtility.CopyProxyToUdon(pet);
                        log.AppendLine("   pet.fetchController wired (IMP-NET-001)");
                    }
                    catch (System.Exception e)
                    {
                        copyOk = false;
                        log.AppendLine("   pet CopyProxyToUdon FAILED: " + e.Message);
                    }
                }

                // 戻ってきて止まる距離を導出する (2026-08-14 IMP-DOG-007)。
                //
                // `returnStopDistance` は**犬のルートからプレイヤーまで**の距離だが、
                // ボールが落ちるのは**口元**なので、実際にボールが届く距離は
                //   ボール = returnStopDistance − 口元の前方オフセット
                // になる。**小さい犬ほど口が手前にある = ボールが遠くなる。**
                //
                // 全犬に 0.55 を入れていたときの実測:
                //   コーギー 口元 0.327 → ボール 0.223 (Play で OK)
                //   ポメ     口元 0.202 → ボール 0.348 (**遠すぎた** — 2026-08-14 ユーザー報告)
                //   ラブ     口元 0.464 → ボール 0.086 (**ほぼ足元**。逆方向に同じ欠陥)
                //
                // ボールの落ち先を犬種によらず一定にする。コーギーでは 0.55 に一致する
                float mouthForward = dog.transform.InverseTransformPoint(mouth.position).z;
                float stopDist = BallDropDistance + mouthForward;
                log.AppendLine("   returnStopDistance " + proxy.returnStopDistance.ToString("F3") +
                    " -> " + stopDist.ToString("F3") +
                    " (口元の前方 " + mouthForward.ToString("F3") +
                    " + ボール落下距離 " + BallDropDistance + ")");
                proxy.returnStopDistance = stopDist;

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
            Finish("Logs/imp-fet-001-setup.txt", log);
        }

        /// <summary>その犬種のボールプレハブを生成する。外部 (シーン) への参照は一切持たせない —
        /// fetchController は実行時配線 (vrc-udonsharp §3 の null 化対策)。</summary>
        static GameObject CreateBallPrefab(KD_DogDef def, Material mat, StringBuilder log)
        {
            if (!AssetDatabase.IsValidFolder("Assets/KominkaHillside/Imported/KominkaDogRun/Prefabs"))
                AssetDatabase.CreateFolder("Assets/KominkaHillside/Imported/KominkaDogRun", "Prefabs");

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = def.BallName;
            go.transform.localScale = Vector3.one * def.ballDiameter; // 体格差 = 識別層 1

            int pickupLayer = LayerMask.NameToLayer("Pickup");
            go.layer = pickupLayer >= 0 ? pickupLayer : 13; // VRChat 既定: 13 = Pickup

            go.GetComponent<Renderer>().sharedMaterial = mat;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // 小球の床抜け対策

            // 位置同期は opt-in の VRCObjectSync (Continuous 固定)。
            // 同居する U# は NoVariableSync (vrc-networking §6)
            go.AddComponent<VRCObjectSync>();
            var pickup = go.AddComponent<VRCPickup>();
            // 識別層 5: どの犬のボールかを文言でも補う (VR では読まれない前提で頼らない)
            pickup.InteractionText = "ボール (" + def.displayName + ")";
            // IMP-FET-013: 掴める距離は体格で梯子にする (正本は KD_DogDef.pickupProximity)。
            // 既存 Prefab / シーンは Toy Physics/5 - Apply Pickup Proximity で書く
            pickup.proximity = def.pickupProximity;

            // IMP-FET-012: new balls use the shared tuning. Existing prefab/scene values
            // remain untouched by Setup; migration is Toy Physics/3 - Apply Tuning.
            KD_ToyPhysicsEditor.ConfigureNewBall(go);

            go.AddUdonSharpComponent<KD_BallController>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, def.BallPrefabPath);
            Object.DestroyImmediate(go);
            if (prefab == null)
            {
                log.AppendLine("   FAIL SaveAsPrefabAsset returned null: " + def.BallPrefabPath);
                return null;
            }
            // Odin の ArgumentNullException が出ても生成物は正常なことがある (vrc-synaptic-unity)。
            // 実害は 3 - Verify で判断する
            log.AppendLine("   created prefab " + def.BallPrefabPath);
            return prefab;
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Fetch System/3 - Verify Fetch System")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_FetchSetup] Verify");
            int fail = 0;
            void Check(bool cond, string what)
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + what);
                if (!cond) fail++;
            }

            // Program Assets
            foreach (var (path, name) in new[] { (BallProgramPath, "KD_BallController"), (FetchProgramPath, "KD_DogFetchController") })
            {
                var pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
                Check(pa != null && pa.sourceCsScript != null, $"program asset ok: {name}");
            }

            // ボール径が犬種間で見分けられるか (DES-DOG-002 の識別層 1)。犬をまたぐので先に集める
            // IMP-DOG-020: key を一緒に持つ (例外の組を名指しするため。ソートすると key が消える)
            var diameters = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, float>>();

            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("-- " + def.displayName + " (" + def.key + ")");
                string tag = def.key + ": ";

                // Controller 側の受け口 (Pickup State + 自作ポーズ)
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(def.ControllerPath);
                Check(ctrl != null, tag + "controller exists: " + def.ControllerPath);
                if (ctrl != null)
                {
                    bool hasPickup = false;
                    foreach (var p in ctrl.parameters)
                        if (p.name == "Pickup" && p.type == AnimatorControllerParameterType.Bool) hasPickup = true;
                    Check(hasPickup, tag + "controller has Bool 'Pickup'");
                    bool stateOk = false;
                    if (ctrl.layers.Length > 0)
                    {
                        foreach (var cs in ctrl.layers[0].stateMachine.states)
                        {
                            if (cs.state.name != "Pickup") continue;
                            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(cs.state.motion));
                            stateOk = guid == AssetDatabase.AssetPathToGUID(def.PickupPosePath);
                        }
                    }
                    Check(stateOk, tag + "state 'Pickup' motion == " + def.PickupPosePath);
                }
                Check(AssetDatabase.LoadAssetAtPath<AnimationClip>(def.PickupPosePath) != null,
                    tag + "pickup pose clip exists: " + def.PickupPosePath);

                // プレハブの構成
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(def.BallPrefabPath);
                Check(prefab != null, tag + "ball prefab exists: " + def.BallPrefabPath);
                if (prefab != null)
                {
                    Check(prefab.GetComponent<Rigidbody>() != null, tag + "prefab has Rigidbody");
                    Check(prefab.GetComponent<SphereCollider>() != null, tag + "prefab has SphereCollider");
                    Check(prefab.GetComponent<VRCPickup>() != null, tag + "prefab has VRCPickup");
                    Check(prefab.GetComponent<VRCObjectSync>() != null, tag + "prefab has VRCObjectSync");
                    var ubs = prefab.GetComponents<VRC.Udon.UdonBehaviour>();
                    Check(ubs.Length == 1 && ubs[0].programSource != null && ubs[0].programSource.name == "KD_BallController",
                        tag + "prefab has exactly 1 UdonBehaviour with programSource KD_BallController");
                    int pickupLayer = LayerMask.NameToLayer("Pickup");
                    Check(prefab.layer == (pickupLayer >= 0 ? pickupLayer : 13), tag + "prefab layer == Pickup");
                    diameters.Add(new System.Collections.Generic.KeyValuePair<string, float>(def.key, prefab.transform.localScale.x));
                }

                int dogCount;
                var dog = KD_SceneObjects.Find(def.DogName, out dogCount);
                Check(dogCount == 1, tag + def.DogName + " exists exactly once (actual " + dogCount + ")");
                if (dog == null) continue;

                var rig = KD_DogRig.Resolve(dog);
                foreach (var p in rig.problems) log.AppendLine("  ---- rig problem: " + p);
                Check(rig.Ok, tag + "骨格が解決できる");
                if (!rig.Ok) continue;

                // シーンのボール
                int ballCount;
                var ball = KD_SceneObjects.Find(def.BallName, out ballCount);
                Check(ballCount == 1, tag + def.BallName + " exists exactly once at scene root (actual " + ballCount + ")");
                if (ball != null && prefab != null)
                {
                    var src = PrefabUtility.GetCorrespondingObjectFromSource(ball);
                    Check(src != null && AssetDatabase.GetAssetPath(src) == def.BallPrefabPath,
                        tag + def.BallName + " is an instance of " + def.BallPrefabPath);
                    var ballProxy = ball.GetUdonSharpComponent<KD_BallController>();
                    Check(ballProxy != null, tag + "ball proxy component exists");
                    // fetchController は実行時配線が前提。エディタ時点で入っていたら
                    // プレハブオーバーライド経由の null 化 (vrc-udonsharp §3) の芽なので落とす
                    Check(ballProxy != null && ballProxy.fetchController == null,
                        tag + "ball.fetchController is null in editor (runtime wiring only)");
                }

                // KD_FetchSystem
                int foundSys;
                var sysGo = KD_SceneObjects.Find(def.FetchSystemName, out foundSys);
                Check(foundSys == 1, tag + def.FetchSystemName + " exists exactly once (actual " + foundSys + ")");
                if (sysGo == null) continue;

                Check(!PrefabUtility.IsPartOfPrefabInstance(sysGo),
                    tag + def.FetchSystemName + " is a plain scene object (not inside a prefab instance)");
                var ub = sysGo.GetComponent<VRC.Udon.UdonBehaviour>();
                Check(ub != null && ub.programSource != null && ub.programSource.name == "KD_DogFetchController",
                    tag + "programSource == KD_DogFetchController (actual " +
                    (ub == null || ub.programSource == null ? "null" : ub.programSource.name) + ")");

                var proxy = sysGo.GetUdonSharpComponent<KD_DogFetchController>();
                Check(proxy != null, tag + "proxy component exists");
                if (proxy == null) continue;

                Check(proxy.dogRoot == dog.transform, tag + "dogRoot wired");
                Check(proxy.dogAnimator != null && proxy.dogAnimator.gameObject == dog,
                    tag + "dogAnimator wired to " + def.DogName);
                Check(proxy.mouthAnchor != null && proxy.mouthAnchor.parent == rig.chin,
                    tag + "mouthAnchor is child of the chin bone");
                Check(proxy.ballController != null && ball != null && proxy.ballController.gameObject == ball,
                    tag + "ballController wired to " + def.BallName);
                Check(proxy.petController != null && proxy.petController.gameObject.name == def.PetSystemName,
                    tag + "petController wired to " + def.PetSystemName);
                // 別の犬の撫でコントローラを掴んでいないか (多頭で最も起きやすい取り違え)
                Check(proxy.petController != null && proxy.petController.dogRoot == dog.transform,
                    tag + "petController が同じ犬を指している");

                // IMP-NET-001: 逆向きの配線。片方向だけだと撫でがローカル完結へ静かに戻る
                Check(proxy.petController != null && proxy.petController.fetchController == proxy,
                    tag + "petController.fetchController が同じ犬の Fetch を指している (IMP-NET-001)");
                Check(proxy.enabled, tag + "proxy enabled");

                // IMP-DOG-007: 戻ってきたボールが**プレイヤーから何 m に落ちるか**を検査する。
                // `returnStopDistance` の値そのものではなく**結果**を見る —
                // 口元アンカーが動けば同じ値でも落ち先が変わるため
                if (proxy.mouthAnchor != null)
                {
                    float mouthFwd = dog.transform.InverseTransformPoint(proxy.mouthAnchor.position).z;
                    float ballDist = proxy.returnStopDistance - mouthFwd;
                    Check(Mathf.Abs(ballDist - BallDropDistance) < 0.02f,
                        tag + $"返したボールの落ち先 == {BallDropDistance}m (actual {ballDist:F3}" +
                        $" / stopDist {proxy.returnStopDistance:F3} - 口元 {mouthFwd:F3})");
                    // **犬より手前に落ちること** = 犬の体に埋まらない
                    Check(ballDist > 0.05f, tag + $"ボールがプレイヤーの足元に埋まらない (actual {ballDist:F3})");
                }

                // 値は参照でないので backing まで届いたか実測する (seedSalt と同じ罠)
                var fetchUbs = sysGo.GetComponents<VRC.Udon.UdonBehaviour>();
                if (fetchUbs.Length == 1)
                {
                    object raw;
                    bool got = fetchUbs[0].publicVariables.TryGetVariableValue("returnStopDistance", out raw);
                    Check(got && raw is float && Mathf.Abs((float)raw - proxy.returnStopDistance) < 0.0005f,
                        tag + "backing returnStopDistance == proxy (actual " + (got ? raw : "missing") + ")");
                }

                // アンカーが PhysBone チェーンを切っていないか (vrc-physbone-colliders)
                if (proxy.mouthAnchor != null)
                {
                    bool inside = false;
                    foreach (var pb in dog.GetComponentsInChildren<VRCPhysBone>(true))
                    {
                        var root = pb.rootTransform != null ? pb.rootTransform : pb.transform;
                        for (var t = proxy.mouthAnchor; t != null; t = t.parent)
                            if (t == root) { inside = true; break; }
                    }
                    Check(!inside, tag + "mouthAnchor is outside all PhysBone chains");
                }
            }

            // DES-DOG-002 の識別層 1: 体格差がボールの大きさで非言語に伝わること。
            // 見分けるには最小でも 1.2 倍は要る (2 匹以上のときだけ判定する)
            //
            // **例外は 1 組だけ: 柴 ↔ コーギーの同径 (0.085)** — IMP-DOG-020 §5 決定 1 (2026-09-05 ユーザー決定)。
            // 5 匹目で径の梯子 (0.070 / 0.085 / 0.108 / 0.140) に空きが無くなった。柴はコーギーと
            // 同径にして色 (層 2) と拾う演技 (層 4) で分け、サモエドだけ ≥ 0.168 で層 1 を保つ。
            // **黙って 1.2 倍を外さない** — 許すのはこの組の比 1.0 だけで、他の隣接は従来どおり 1.2 倍
            if (diameters.Count >= 2)
            {
                diameters.Sort((a, b) => a.Value.CompareTo(b.Value));
                bool distinct = true;
                for (int i = 1; i < diameters.Count; i++)
                {
                    float ratio = diameters[i].Value / diameters[i - 1].Value;
                    // float の割り算は 0.168/0.140 = 1.1999999 になる。「ちょうど 1.2 倍」を落とさない
                    if (ratio + 1e-4f >= 1.2f) continue;
                    if (KD_DogDefs.BallDiameterSharePermitted(diameters[i - 1].Key, diameters[i].Key) &&
                        Mathf.Approximately(ratio, 1f)) continue;
                    distinct = false;
                }
                Check(distinct, "ボール径が犬種間で 1.2 倍以上ずつ違う (識別層 1。例外は 柴↔コーギー の同径だけ。actual " +
                    string.Join(" / ", diameters.ConvertAll(d => d.Key + "=" + d.Value.ToString("F3")).ToArray()) + ")");
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
            Finish("Logs/imp-fet-001-verify.txt", log);
        }

        // ================================================================
        // IMP-FET-011: リセットボタンの撤去 (4 - Remove / 5 - Verify)
        //
        // **ボールの復帰手段は手元メニューの「おもちゃ」タブへ移った** (2026-09-03)。
        // 旧 `KD_BallResetButton` は庭に立つ物理ボタンで、押すと
        // `VRCObjectSync.Respawn()` = **シーン開始位置**へ戻していた。
        //
        // **失われる機能は無い** — 行方不明 (茂み・屋根・池) からの復帰という目的に対して、
        // **目の前へ呼ぶのは初期位置へ戻すより強い**。「詰み」を解く 2 行
        // (`SetKinematic(false)` / `velocity = 0`。破壊的監査 RC-3) は
        // `KD_TreatPouch.Recall` がそのまま引き継いでいる。
        //
        // **床抜けの自動復帰 (RespawnHeight) はここに残す。** あれはボタンとは別経路の
        // シーン設定で、手元メニューでは代替できない。
        // ================================================================

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Archive/Fetch System/4 - Remove Reset Button")]
        public static void RemoveResetButton()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_FetchSetup] Remove reset button (IMP-FET-011)");

            // 床抜けの自動復帰: Respawn Height の挙動を Respawn に (シーン共通)。
            // **ボタンを消してもここは要る** — 池や地形の穴へ落ちたボールの最後の受け皿
            var desc = Object.FindObjectOfType<VRCSceneDescriptor>();
            if (desc != null)
            {
                log.AppendLine($"RespawnHeightY = {desc.RespawnHeightY} / ObjectBehaviourAtRespawnHeight = {desc.ObjectBehaviourAtRespawnHeight}");
                if (desc.ObjectBehaviourAtRespawnHeight != VRC.SDKBase.VRC_SceneDescriptor.RespawnHeightBehaviour.Respawn)
                {
                    desc.ObjectBehaviourAtRespawnHeight = VRC.SDKBase.VRC_SceneDescriptor.RespawnHeightBehaviour.Respawn;
                    EditorUtility.SetDirty(desc);
                    log.AppendLine("-> Respawn へ変更 (床抜けで定位置へ戻る)");
                }
            }
            else
            {
                log.AppendLine("WARN VRCSceneDescriptor が見つからない (Respawn Height 未確認)");
            }

            // **やめた物を消すのは作った側の後始末**
            // (KD_TreatSetupEditor.RemovePouchBody と同じ判断)。ここでやらないと、
            // 既にシーンへ置いた 4 個が誰にも消されずに残り、
            // **押しても何も起きないボタンが庭に立ち続ける**
            int removed = 0;
            foreach (var def in KD_DogDefs.All)
            {
                int count;
                var btn = KD_SceneObjects.Find(def.LegacyResetButtonName, out count);
                while (btn != null)
                {
                    log.AppendLine("removed " + KD_SceneObjects.PathOf(btn));
                    Object.DestroyImmediate(btn);
                    removed++;
                    btn = KD_SceneObjects.Find(def.LegacyResetButtonName, out count);
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("removed " + removed + " 個");
            log.AppendLine("RESULT: PASS (**シーンは保存していない。** 確認してから Ctrl+S)");
            log.AppendLine("呼び出しは手元メニューの「" + KD_TreatPouch.WordTabToy + "」タブへ移った");
            Finish("Logs/imp-fet-011-remove.txt", log);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Fetch System/5 - Verify Ball Recovery")]
        public static void VerifyBallRecovery()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_FetchSetup] Verify ball recovery (IMP-FET-011)");
            int fail = 0;
            void Check(bool cond, string what)
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + what);
                if (!cond) fail++;
            }

            // ---- 旧ボタンが 1 個も残っていない ----
            //
            // 残ると**復帰の口が 2 本**になり、しかも片方は初期位置へ戻すので
            // 「押す場所で結果が違う」という読めない状態になる
            foreach (var def in KD_DogDefs.All)
            {
                int count;
                KD_SceneObjects.Find(def.LegacyResetButtonName, out count);
                Check(count == 0, def.LegacyResetButtonName + " がシーンに無い (actual "
                    + count + ") — 4 - Remove Reset Button を流すこと");
            }
            Check(AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(
                    "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_BallResetButton.asset") == null,
                "KD_BallResetButton.asset が消えている");

            // ---- 復帰の口が手元メニューにある ----
            //
            // **ボタンを消しただけで移し先が無い状態を PASS にしない。**
            // 「復帰手段が存在しないこと」に誰も気付けないのが破壊的監査 RC-1 の教訓
            int pouchCount;
            var pouchGo = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName, out pouchCount);
            var pouch = (pouchCount == 1 && pouchGo != null)
                ? pouchGo.GetUdonSharpComponent<KD_TreatPouch>() : null;
            Check(pouch != null, KD_DogDefs.TreatPouchName + " がシーンに 1 個ある (復帰の移し先)");
            if (pouch != null)
            {
                int n = KD_DogDefs.All.Length;
                Check(pouch.balls != null && pouch.balls.Length == n,
                    "手元メニューが犬 " + n + " 匹ぶんのボールを持っている");
                Check(pouch.toyFetchers != null && pouch.toyFetchers.Length == n,
                    "手元メニューが犬 " + n + " 匹ぶんの Fetch を持っている (待機中の判定)");
            }

            foreach (var def in KD_DogDefs.All)
            {
                string tag = def.key + ": ";
                int fetchCount;
                var fetchGo = KD_SceneObjects.Find(def.FetchSystemName, out fetchCount);
                // 犬側のテレポート検知は**残す** — 床抜け・Respawn 等の別経路がある
                var fetch = fetchGo == null ? null : fetchGo.GetUdonSharpComponent<KD_DogFetchController>();
                Check(fetch != null && fetch.ballTeleportThreshold >= 1.5f,
                    tag + $"fetch.ballTeleportThreshold >= 1.5m (actual {(fetch == null ? -1f : fetch.ballTeleportThreshold)})");
            }

            // 床抜けの自動復帰
            var desc = Object.FindObjectOfType<VRCSceneDescriptor>();
            Check(desc != null &&
                desc.ObjectBehaviourAtRespawnHeight == VRC.SDKBase.VRC_SceneDescriptor.RespawnHeightBehaviour.Respawn,
                "SceneDescriptor ObjectBehaviourAtRespawnHeight == Respawn");

            log.AppendLine(fail == 0 ? "VERIFY RESULT: PASS" : $"VERIFY RESULT: FAIL ({fail} checks)");
            Finish("Logs/imp-fet-011-verify.txt", log);
        }

        // ---- helpers (KD_PetSetupEditor と同型。共通化は 2 例で十分になってから) ----

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
            anchor.position = bone.position + dogRoot.TransformVector(dogLocalOffset);
            log.AppendLine("created " + name + " under " + bone.name);
            return anchor;
        }

        static void Finish(string file, StringBuilder log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }
    }
}
