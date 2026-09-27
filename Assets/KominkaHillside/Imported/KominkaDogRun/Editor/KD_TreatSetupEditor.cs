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
    /// IMP-TRT-001: おやつのセットアップと検証。**Phase 4b Step 4 で「枠 3 つ」になった。**
    ///
    /// 実行順 (別実行にする — Program Asset 直後の CopyProxyToUdon は outdated error):
    ///   1 - Create Program Assets → force refresh
    ///   **Scene Layout/2 - Apply** (旧名 `KD_Treat` / `KD_TreatHome` を 0 番へ改名する)
    ///   2 - Setup Treat System
    ///   3 - Verify Treat System
    ///
    /// 前提: Dog Pack / Wander System / Shop (おやつ 4 種) が構築済み。
    ///
    /// シーンに置くもの (すべて素のシーンオブジェクト。プレハブは使わない):
    /// <code>
    /// KD_Systems/
    ///   KD_TreatBox        ルールの正本 + Claim の配り手 (Manual・**マスター固定**)
    ///   KD_TreatSlot_{i}   枠 1 つぶんの状態 (Manual。giverId / product / outMs)
    ///   KD_TreatPouch      手元メニュー (None・同期 0)
    /// KD_Props/
    ///   KD_Treat_{i}       実体 (Pickup + Rigidbody + VRCObjectSync + 4 種のメッシュ)
    ///   KD_TreatHome_{i}   定位置の目印。**見た目は持たない** (共有の皿は廃止された)
    /// </code>
    ///
    /// **Respawn 先 = シーン開始時の実体の位置**なので、定位置を動かしたら
    /// 実体もその上へ置き直すこと (Verify が枠ごとに距離を検査する)。
    /// </summary>
    public static partial class KD_TreatSetupEditor
    {
        const string ControllerScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatController.cs";
        const string ControllerProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatController.asset";
        const string BoxScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatBox.cs";
        const string BoxProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatBox.asset";
        const string SlotScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatSlot.cs";
        const string SlotProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatSlot.asset";
        const string PouchScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatPouch.cs";
        const string PouchProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatPouch.asset";

        /// <summary>Phase 1 の進行の正本。**Step 6 で畳んだので、あるとしたら残骸。**
        /// Verify がシーン内 0 個・プログラムアセット無しを数える。</summary>
        const string LegacySystemName = KD_DogDefs.LegacyTreatSystemName;
        const string LegacySystemProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatSystem.asset";

        const string BoxName = KD_DogDefs.TreatBoxName;   // IMP-INF-003: 正本は KD_DogDefs
        const string PouchName = KD_DogDefs.TreatPouchName;

        // ---- IMP-TRT-006: Touch (スマホ) 用の起動ボタン ----
        //
        // Screen-Space Overlay の Canvas にボタン 1 個。**KD_TreatPouch の子** (板と同じ置き方。
        // Transform に意味が無いので KD_Systems の下で足りる — LayoutTable に行は足さない)。
        // **既定は非アクティブ**で、実行時に入力方式が Touch のときだけ KD_TreatPouch が出す。
        // 置き場所は画面上端の中央 — VRChat Mobile 自身のボタン (移動スティック = 左下 /
        // ジャンプ等 = 右下) と重ねないため。**被らないかは実機で見る** (公式の VRCButtonLayout は
        // Example Central から取るものでプロジェクトに無い)
        const string PouchLauncherName = "KD_TouchLauncher";
        const string LauncherButtonName = "LauncherButton";
        static readonly Vector2 LauncherRefResolution = new Vector2(1920f, 1080f);
        static readonly Vector2 LauncherSize = new Vector2(220f, 72f);
        /// <summary>画面上端からの下げ量 (px @1080)。ノッチ・ステータスバーを避ける</summary>
        const float LauncherTopMargin = KD_TreatPouch.TouchLauncherEdgeMargin;
        const string PackName = KD_DogDefs.DogPackName;
        const int SlotCount = KD_DogDefs.TreatSlotCount;

        // ---- 手元メニュー (Phase 4b Step 5) ----
        /// <summary>**廃止した実物 (2026-09-01)。** 名前だけ残してあるのは
        /// **消すため** — Setup は既にシーンに居るこれを毎回片付ける。</summary>
        const string PouchBodyName = "KD_TreatPouch_Body";
        const string PouchPanelName = "KD_TreatPouchPanel";

        /// <summary>板の置き場所 (root のローカル)。**手 (VR) から前へ出して上げる。**
        /// 実物が消えても板の相対位置は変えない — Play で決めた見え方をそのまま残す。</summary>
        static readonly Vector3 PouchPanelOffset = new Vector3(0.22f, 0.30f, 0.32f);

        /// <summary>実体の見た目の長軸 (m)。棚の陳列 (0.08) より少し小さい —
        /// **手の中に収まって見えること**が要件で、棚は「見せる」ための寸法だから。
        /// Phase 1 の円盤 (直径 0.07) とおおむね同じ背丈にしてある。</summary>
        const float TreatBodySize = 0.07f;

        /// <summary>掴むための当たりの厚み (m)。**実寸のクッキーより厚い。**
        /// 薄い板そのままだと VR で狙って掴むのが急に難しくなる (棚側と同じ判断)。</summary>
        const float TreatGrabThickness = 0.05f;

        /// <summary>新しい枠 (1・2) を仮置きするときの間隔 (m)。**0 番は動かさない。**</summary>
        const float NewParkingStep = 0.28f;

        /// <summary>Program Assets / Setup / Verify の記録先 (3 つとも同じファイルへ書く。従来どおり)。</summary>
        const string TreatLogFile = "Logs/imp-trt-001-treat.txt";

        [MenuItem("Tools/KominkaHillside/Menu/5 Create Pouch Programs")]
        public static void CreateProgramAssets()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_TreatSetup] Create program assets");
            CreateProgramAsset(ControllerScriptPath, ControllerProgramPath, log);
            CreateProgramAsset(BoxScriptPath, BoxProgramPath, log);
            CreateProgramAsset(SlotScriptPath, SlotProgramPath, log);
            CreateProgramAsset(KD_TreatFxSetupEditor.FxScriptPath,
                KD_TreatFxSetupEditor.FxProgramPath, log);
            CreateProgramAsset(PouchScriptPath, PouchProgramPath, log);
            log.AppendLine("次: force refresh してから Tools/KominkaHillside/Menu/7 Build Menu");
            KD_VerifyLog.Write(TreatLogFile, log);
        }

        static void CreateProgramAsset(string scriptPath, string programPath, StringBuilder log)
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath) != null)
            {
                log.AppendLine("already exists: " + programPath);
                return;
            }
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (script == null)
            {
                log.AppendLine("FAIL script not found: " + scriptPath);
                return;
            }
            var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = script;
            AssetDatabase.CreateAsset(asset, programPath);
            AssetDatabase.SaveAssets();
            log.AppendLine("created: " + programPath);
        }

        // 裏山改変: 古民家のおやつ一式の Setup (棚・台帳・枠を要求する)。裏山の入口は Menu/7 Build Menu = SetupHillside (KD_TreatSetupEditor.Hillside.cs)
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Treat System/2 - Setup Treat System (Kominka)")]
        public static void Setup()
        {
            KominkaHillside.Editor.KH_ProductionScene.RejectLegacyRebuild();
            var log = new StringBuilder();
            log.AppendLine("[KD_TreatSetup] Setup");
            bool ok = true;

            // ---- 名簿 (前提) ----
            int found;
            var packGo = KD_SceneObjects.Find(PackName, out found);
            var pack = (found == 1 && packGo != null) ? packGo.GetUdonSharpComponent<KD_DogPack>() : null;
            if (pack == null)
            {
                log.AppendLine("FAIL " + PackName + " が無い。先に Tools/KominkaHillside/Menu/Imported-Unused/Dog Pack/2 - Setup Dog Pack を実行");
                KD_VerifyLog.Write(TreatLogFile, log);
                return;
            }

            // ---- 旧名が残っていないか (**ここで止めるのが要点**) ----
            //
            // `KD_Treat` → `KD_Treat_0` の改名は `Scene Layout/2 - Apply` の担当で、
            // **fileID を保つ**からこそ Phase 1 の配線も NetworkID も生き残る。
            // 先に Setup を流すと「`KD_Treat_0` が無い」と読んで**新しく作ってしまい**、
            // 旧 `KD_Treat` が孤児として残る (参照も NetworkID もそちらに付いたまま)。
            foreach (var (legacy, want) in new[] {
                (KD_DogDefs.LegacyTreatName, KD_DogDefs.TreatNameOf(0)),
                (KD_DogDefs.LegacyTreatHomeName, KD_DogDefs.TreatHomeNameOf(0)) })
            {
                KD_SceneObjects.Find(legacy, out int legacyCount);
                if (legacyCount == 0) continue;
                log.AppendLine($"FAIL 旧名 '{legacy}' がまだシーンにある ({legacyCount} 個)。");
                log.AppendLine("     **先に Tools/KominkaHillside/Menu/Imported-Unused/Scene Layout/2 - Apply Scene Layout** を実行して "
                    + want + " へ改名すること (fileID を保つので参照と NetworkID が無傷で移る)。");
                KD_VerifyLog.Write(TreatLogFile, log);
                return;
            }

            // ---- 枠 3 つぶんの器 ----
            var slots = new KD_TreatSlot[SlotCount];
            var bodies = new KD_TreatController[SlotCount];
            var parkings = new Transform[SlotCount];

            for (int i = 0; i < SlotCount; i++)
            {
                string homeName = KD_DogDefs.TreatHomeNameOf(i);
                string bodyName = KD_DogDefs.TreatNameOf(i);
                string slotName = KD_DogDefs.TreatSlotNameOf(i);

                // ---- 定位置 (Respawn 先の目印)。**位置は人間の作業 — 既存は触らない** ----
                var homeGo = KD_SceneObjects.Find(homeName, out found);
                if (found > 1)
                {
                    log.AppendLine($"FAIL '{homeName}' がシーン内に {found} 個ある");
                    KD_VerifyLog.Write(TreatLogFile, log);
                    return;
                }
                if (homeGo == null)
                {
                    homeGo = new GameObject(homeName);
                    KD_SceneObjects.PlaceIfNew(homeGo, KD_DogDefs.PropsPath);   // IMP-INF-003: 備品
                    // 仮置き: 0 番の脇。0 番も無ければコーギーのボールの脇 (縁側の想定)
                    Vector3 pos;
                    if (i > 0 && parkings[0] != null)
                        pos = parkings[0].position + new Vector3(NewParkingStep * i, 0f, 0f);
                    else
                    {
                        pos = Vector3.zero;
                        var anyBall = KD_SceneObjects.Find(KD_DogDefs.All[0].BallName, out int ballCount);
                        if (ballCount >= 1 && anyBall != null)
                            pos = anyBall.transform.position + new Vector3(0.35f, 0.02f, 0f);
                    }
                    homeGo.transform.position = pos;
                    log.AppendLine("created " + homeName + " (仮置き " + pos.ToString("F2")
                        + " — **縁側の好きな場所へ動かすこと**)");
                }
                // **見た目を落とす。** 共有の皿は廃止された (設計 3) ので、
                // 皿が見えていると「みんなの皿」という消えた設計が絵として残り続ける。
                // 位置は 1 mm も触らない — ここが「Respawn 先の目印」であることは変わらない
                foreach (var r in homeGo.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled) { r.enabled = false; log.AppendLine("   " + homeName + ": renderer off"); }
                foreach (var c in homeGo.GetComponentsInChildren<Collider>(true))
                    if (c.enabled) { c.enabled = false; log.AppendLine("   " + homeName + ": collider off"); }
                parkings[i] = homeGo.transform;

                // ---- 実体 ----
                var treatGo = KD_SceneObjects.Find(bodyName, out found);
                if (found > 1)
                {
                    log.AppendLine($"FAIL '{bodyName}' がシーン内に {found} 個ある");
                    KD_VerifyLog.Write(TreatLogFile, log);
                    return;
                }
                if (treatGo == null)
                {
                    treatGo = new GameObject(bodyName);
                    KD_SceneObjects.PlaceIfNew(treatGo, KD_DogDefs.PropsPath);   // VRCObjectSync を持つので動かない親の下
                    treatGo.transform.position = homeGo.transform.position + Vector3.up * 0.03f;
                    log.AppendLine("created " + bodyName + " (" + homeName + " の上)");
                }

                int pickupLayer = LayerMask.NameToLayer("Pickup");
                treatGo.layer = pickupLayer >= 0 ? pickupLayer : 13; // VRChat 既定: 13 = Pickup

                // **root は「見た目を持たない器」にする。** 4 種のメッシュを子に持つので、
                // Phase 1 の円柱が root に残っていると 5 個目の絵として重なって出る。
                // あわせて **localScale を単位元へ戻す** — 子のメッシュは
                // KD_ModelNodeUtil が実寸で正規化するので、root が非等方だと全部つぶれる。
                // (位置は触らない。**Respawn 先は位置と回転で、スケールは関わらない**)
                var rootMr = treatGo.GetComponent<MeshRenderer>();
                var rootMf = treatGo.GetComponent<MeshFilter>();
                if (rootMr != null) Object.DestroyImmediate(rootMr, true);
                if (rootMf != null) Object.DestroyImmediate(rootMf, true);
                if (treatGo.transform.localScale != Vector3.one)
                {
                    log.AppendLine("   " + bodyName + ": localScale "
                        + treatGo.transform.localScale.ToString("F3") + " -> 1 (メッシュ側で実寸を作る)");
                    treatGo.transform.localScale = Vector3.one;
                }

                // 当たりは箱 1 個だけ (Phase 1 の Capsule が残っていたら畳む)
                foreach (var extra in treatGo.GetComponents<Collider>())
                    if (!(extra is BoxCollider)) Object.DestroyImmediate(extra, true);
                var bodyBox = treatGo.GetComponent<BoxCollider>();
                if (bodyBox == null) bodyBox = treatGo.AddComponent<BoxCollider>();
                bodyBox.center = Vector3.zero;
                bodyBox.size = new Vector3(TreatBodySize, TreatGrabThickness, TreatBodySize);
                bodyBox.isTrigger = false;

                var rb = treatGo.GetComponent<Rigidbody>();
                if (rb == null) rb = treatGo.AddComponent<Rigidbody>();
                rb.mass = 0.1f;
                rb.isKinematic = false;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // 小物の床抜け対策

                if (treatGo.GetComponent<VRCObjectSync>() == null) treatGo.AddComponent<VRCObjectSync>();
                var pickup = treatGo.GetComponent<VRCPickup>();
                if (pickup == null) pickup = treatGo.AddComponent<VRCPickup>();
                var promptCatalog = KD_LocalizationEditor.Load();
                pickup.InteractionText = KD_LocalizationEditor.Text(promptCatalog, promptCatalog.fallbackLanguage, "interact.treat");

                // ---- 4 種のメッシュ (**棚と同じ表・同じモデル・同じマテリアル**) ----
                int n = KD_ShopSetupEditor.TreatProductCount;
                var ids = new string[n];
                var nodes = new GameObject[n];
                for (int p = 0; p < n; p++)
                {
                    ids[p] = KD_ShopSetupEditor.TreatProductKeyAt(p);
                    string nodeName = KD_ShopSetupEditor.TreatMeshNodeAt(p);
                    if (!KD_ShopSetupEditor.BuildTreatMeshNodeOn(treatGo, p, TreatBodySize, log,
                            out string meshProblem))
                    {
                        log.AppendLine("   FAIL メッシュを作れない (" + nodeName + "): " + meshProblem);
                        ok = false;
                        continue;
                    }
                    var nodeT = treatGo.transform.Find(nodeName);
                    nodes[p] = nodeT != null ? nodeT.gameObject : null;
                    // **初期は全部 false** = 枠が Free の見た目。
                    // 実行時は KD_TreatController が枠の状態から作り直す
                    if (nodes[p] != null && nodes[p].activeSelf) nodes[p].SetActive(false);
                }

                var treatCtrl = treatGo.GetUdonSharpComponent<KD_TreatController>();
                if (treatCtrl == null)
                {
                    treatCtrl = treatGo.AddUdonSharpComponent<KD_TreatController>();
                    log.AppendLine("   added KD_TreatController (" + bodyName + ")");
                }
                treatCtrl.slotIndex = i;
                treatCtrl.parking = homeGo.transform;
                treatCtrl.productKeys = ids;
                treatCtrl.meshNodes = nodes;
                bodies[i] = treatCtrl;

                // ---- 枠の状態 (**Manual なので専用 GameObject**) ----
                var slotGo = KD_SceneObjects.Find(slotName, out found);
                if (found > 1)
                {
                    log.AppendLine($"FAIL '{slotName}' がシーン内に {found} 個ある");
                    KD_VerifyLog.Write(TreatLogFile, log);
                    return;
                }
                if (slotGo == null)
                {
                    slotGo = new GameObject(slotName);
                    KD_SceneObjects.PlaceIfNew(slotGo, KD_DogDefs.SystemsPath);   // 位置に意味が無い
                    log.AppendLine("created " + slotName);
                }
                var slot = slotGo.GetUdonSharpComponent<KD_TreatSlot>();
                if (slot == null)
                {
                    slot = slotGo.AddUdonSharpComponent<KD_TreatSlot>();
                    log.AppendLine("   added KD_TreatSlot (" + slotName + ")");
                }
                slot.slotIndex = i;
                slot.body = treatCtrl;
                slots[i] = slot;

                treatCtrl.slot = slot;
            }

            // ---- Phase 1 の進行の正本が残っていないか (**Step 6 で畳んだ**) ----
            //
            // 同居させると進行の正本が 2 つになり、とくに `_SetPickupable(!EatingNow)` が
            // 枠の「giver 以外は掴めない」を毎フレーム打ち消す (Step 4 の記録)。
            // **Setup は消さない** (作るだけ) ので、見つけたら止めて人へ返す
            KD_SceneObjects.Find(LegacySystemName, out int legacySysCount);
            if (legacySysCount > 0)
            {
                log.AppendLine($"FAIL 旧 '{LegacySystemName}' がまだシーンにある ({legacySysCount} 個)。");
                log.AppendLine("     Phase 4b Step 6 で畳んだ層なので、手で削除すること "
                    + "(進行の正本が 2 つあると掴めるかの導出が打ち消し合う)。");
                KD_VerifyLog.Write(TreatLogFile, log);
                return;
            }

            // ---- ルールの正本 + Claim の配り手 ----
            //
            // **Manual なので専用 GameObject** (U# は同一 GameObject で同期方式を混在できない)。
            // **マスター固定** — `Networking.SetOwner` をここへ呼ばないことが仕様で、
            // `check-sync-anchors.js` が機械検査する (犬と同じ規律)。
            var boxGo = KD_SceneObjects.Find(BoxName, out found);
            if (found > 1)
            {
                log.AppendLine($"FAIL '{BoxName}' がシーン内に {found} 個ある");
                KD_VerifyLog.Write(TreatLogFile, log);
                return;
            }
            if (boxGo == null)
            {
                boxGo = new GameObject(BoxName);
                KD_SceneObjects.PlaceIfNew(boxGo, KD_DogDefs.SystemsPath);   // IMP-INF-003
                log.AppendLine("created " + BoxName);
            }
            var treatBox = boxGo.GetUdonSharpComponent<KD_TreatBox>();
            if (treatBox == null)
            {
                treatBox = boxGo.AddUdonSharpComponent<KD_TreatBox>();
                log.AppendLine("   added KD_TreatBox");
            }
            treatBox.slots = slots;
            treatBox.pack = pack;
            ok &= TryCopy(treatBox, log);

            // ---- 手元メニュー (Phase 4b Step 5) ----
            var pouch = EnsurePouch(treatBox, log);
            if (pouch == null) ok = false;

            // 枠 → 箱 (**箱を作ったあとでないと配線できない**ので 2 周目でやる)
            for (int i = 0; i < SlotCount; i++)
            {
                if (slots[i] == null) { ok = false; continue; }
                slots[i].box = treatBox;
                // 移譲が済んだことを出す側へ知らせる先 (**枠の移譲イベントは
                // ポーチ側では発火しない**)。null でも壊れないが、あると往復が速い
                slots[i].pouch = pouch;
                ok &= TryCopy(slots[i], log);
                ok &= TryCopy(bodies[i], log);
                log.AppendLine("   wired slot " + i + ": "
                    + KD_DogDefs.TreatSlotNameOf(i) + " <-> " + KD_DogDefs.TreatNameOf(i));
            }

            // ---- 各犬の徘徊層へ配線 ----
            foreach (var def in KD_DogDefs.All)
            {
                var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out int wCount);
                var wander = (wCount == 1 && wanderGo != null)
                    ? wanderGo.GetUdonSharpComponent<KD_DogWanderController>() : null;
                if (wander == null)
                {
                    log.AppendLine("   FAIL " + def.WanderSystemName + " が取れない (" + wCount + " 個)");
                    ok = false;
                    continue;
                }
                wander.treatBox = treatBox;
                ok &= TryCopy(wander, log);
                log.AppendLine("   wired " + def.WanderSystemName + ".treatBox");
            }

            // ---- 食後エフェクトと効果音 (Step 7。中身は KD_TreatFxSetupEditor) ----
            //
            // おやつの一部なので別メニューにしない — 「どちらを流せばよいか」が増える。
            // ファイルを分けたのはこのファイルが既に大きいからで、責務は連続している
            log.AppendLine("-- 食後エフェクト --");
            ok &= KD_TreatFxSetupEditor.Setup(treatBox, slots, pack, log);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("SETUP RESULT: " + (ok ? "PASS" : "FAIL"));
            log.AppendLine("**シーンは保存していない。** 確認してから Ctrl+S");
            log.AppendLine("定位置を動かしたら、実体もその上へ置き直すこと (Respawn 先 = シーン開始位置)");
            KD_VerifyLog.Write(TreatLogFile, log);
        }

        // ---- helpers (他の Setup Editor と同型) ----

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
    }
}
