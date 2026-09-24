using System.IO;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-PRG-001: Progression 台帳 (<see cref="KD_ProgressLedger"/> +
    /// <see cref="KD_ShopItem"/>) のセットアップと検証。
    ///
    /// 実行順 (別実行にする — Program Asset 直後の CopyProxyToUdon は outdated error):
    ///   1 - Create Program Assets → force refresh
    ///   2 - Setup Progress System
    ///   3 - Verify Progress System
    ///
    /// 裏山改変 (IMP-PRG-001 段階 1): 2 / 3 を Tools/KominkaHillside/Menu/3a Setup Progress / 3b Verify Progress へ付け替え。
    /// 置き場所は KH_Dogs 直下 (古民家の "====KominkaDogRun====/KD_Systems" は作らない)、額は initial 0 / keeper 200、
    /// storageNamespace は KH_Commerce_v1。**products は 3a では配線しない** — 古民家と同じく
    /// Menu/4 Setup Shop が商品を作ったあと <see cref="KD_ProductsEditor.BindCatalog"/> で ledger.products = catalog.items にする。
    /// 裏山の実行順: 3 Create Shop Programs → force refresh → 3a → 4 → 4d → 7 → 11 → 11b (検査は 3b / 4a)。
    ///
    /// **台帳は何にも配線されない。** 犬・ボール・カメラのどこも参照しないし、
    /// 誰からも参照されない (消費側の おもちゃ箱 [IMP-FET-009] / おやつ棚 [IMP-TRT-001]
    /// がまだ無い)。**それが正しい姿** — 台帳は残高と所持ビットしか知らない。
    ///
    /// ## Verify が Play を待たずに落とすもの
    ///
    /// 台帳の値打ちはほぼ全部**算術**なので、純関数の自己テストで大半が担保できる
    /// (残高の導出 / 買い切りの可否 / ビットの割り当て / 壊れた値の扱い)。
    /// Play が要るのは **Persistence の往復と、実際に押して買えること**だけ。
    /// </summary>
    public static class KD_ProgressSetupEditor
    {
        const string ScriptDir = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/Progress/";
        const string LedgerScript = ScriptDir + "KD_ProgressLedger.cs";
        const string LedgerProgram = ScriptDir + "KD_ProgressLedger.asset";
        const string ShopScript = ScriptDir + "KD_ShopItem.cs";
        const string ShopProgram = ScriptDir + "KD_ShopItem.asset";
        const string SystemName = KD_DogDefs.ProgressSystemName;   // IMP-INF-003: 正本は KD_DogDefs
        /// <summary>IMP-PRG-009: 初期配布 / おじいさんの額 (2026-09-14 ユーザー決定)。
        /// 裏山改変: 初期配布は置かない (0)、おじいさんは 200 を一度だけ (DES-UI-003 / IMP-PRG-001)。</summary>
        public const int InitialPoints = 0;
        public const int KeeperPoints = KD_ProgressLedger.HillsideKeeperPoints;   // 裏山改変: 正本は台帳の定数
        /// <summary>裏山改変: ログの置き場所 (KD_Hillside.LogDir)。</summary>
        const string HillsideSetupLog = KD_Hillside.LogDir + "setup-progress.txt";
        const string HillsideVerifyLog = KD_Hillside.LogDir + "verify-progress.txt";
        /// <summary>裏山改変: 台帳の置き場所 (KH_Dogs 直下)。</summary>
        public const string HillsidePath = KD_Hillside.DogsRootName + "/" + SystemName;

        // 旧実装 (2026-08-17 午前の「恒久解放のみ」版)。作り替えたので撤去する
        const string LegacyProgram = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_ProgressManager.asset";

        // ================================================================ 1: Program assets

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Progress System/1 - Create Program Assets")]
        public static void CreateProgramAssets()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ProgressSetup] Create program assets");

            // 旧実装の Program Asset が残っていると、消したスクリプトを指したまま
            // U# コンパイルが落ち続ける
            if (AssetDatabase.LoadMainAssetAtPath(LegacyProgram) != null)
            {
                AssetDatabase.DeleteAsset(LegacyProgram);
                log.AppendLine("deleted legacy " + LegacyProgram);
            }

            bool ok = true;
            ok &= EnsureProgram(LedgerScript, LedgerProgram, log);
            ok &= EnsureProgram(ShopScript, ShopProgram, log);
            AssetDatabase.SaveAssets();

            log.AppendLine(ok
                ? "RESULT: PASS (force refresh してから Setup を実行する)"
                : "RESULT: FAIL");
            Finish("Logs/imp-prg-001-program.txt", log);
        }

        static bool EnsureProgram(string csPath, string assetPath, StringBuilder log)
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null)
            {
                log.AppendLine("already exists: " + assetPath);
                return true;
            }
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(csPath);
            if (script == null)
            {
                log.AppendLine("FAIL script not found: " + csPath);
                return false;
            }
            var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = script;
            AssetDatabase.CreateAsset(asset, assetPath);
            log.AppendLine("created: " + assetPath);
            return true;
        }

        // ================================================================ 2: Setup

        [MenuItem("Tools/KominkaHillside/Menu/3a Setup Progress")]   // 裏山改変: Imported-Unused/Progress System/2 から付け替え
        public static void Setup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ProgressSetup] Setup (裏山 段階 1)");
            if (EditorApplication.isPlaying || !KD_Hillside.InScene())
            {
                log.AppendLine("FAIL " + KD_Hillside.ScenePath + " を Edit モードで開くこと");
                Finish(HillsideSetupLog, log);
                return;
            }

            int found;
            var sysGo = KD_SceneObjects.Find(SystemName, out found);
            if (found > 1)
            {
                log.AppendLine($"FAIL '{SystemName}' がシーン内に {found} 個ある。1 個に整理してから再実行");
                Finish(HillsideSetupLog, log);
                return;
            }
            if (sysGo == null)
            {
                // 裏山改変: KH_Dogs 直下に作る。KD_SceneObjects.PlaceIfNew は通さない
                // (EnsureContainer がルートの KH_Dogs の Transform を単位元へ揃えてしまうため)
                var dogs = FindHillsideRoot(log);
                if (dogs == null) { Finish(HillsideSetupLog, log); return; }
                // 位置は意味を持たない (音も絵も出さない台帳なので原点でよい)
                sysGo = new GameObject(SystemName);
                Undo.RegisterCreatedObjectUndo(sysGo, "Create " + SystemName);
                sysGo.transform.SetParent(dogs, false);
                log.AppendLine("created " + HillsidePath);
            }
            else
            {
                log.AppendLine("reused " + SystemName);
            }

            // 旧実装 (KD_ProgressManager) の残骸を掃除する。
            //
            // スクリプトごと消したので、シーンには **Missing Script** と
            // **programSource が null の UdonBehaviour** が残る。放置すると
            // Verify の「UdonBehaviour は 1 個」が落ち続け、SDK のビルド検証にも出る
            int deadUdon = 0;
            foreach (var ub0 in sysGo.GetComponents<VRC.Udon.UdonBehaviour>())
            {
                if (ub0 == null || ub0.programSource != null) continue;
                Object.DestroyImmediate(ub0);
                deadUdon++;
            }
            if (deadUdon > 0) log.AppendLine($"removed {deadUdon} orphaned UdonBehaviour (旧実装の残骸)");

            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(sysGo);
            if (missing > 0)
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(sysGo);
                log.AppendLine($"removed {missing} Missing Script (旧 KD_ProgressManager)");
            }

            var ledger = sysGo.GetUdonSharpComponent<KD_ProgressLedger>();
            if (ledger == null)
            {
                ledger = sysGo.AddUdonSharpComponent<KD_ProgressLedger>();
                log.AppendLine("added KD_ProgressLedger");
            }

            // **製品の時計を毎回正す。** デバッグ盤は実行時のヒープへ書くので
            // シリアライズ値は 60 のままでなければならない。ここで戻しておけば、
            // 誤って Inspector で縮めたものがコミットされても Setup 一発で直る
            if (!Mathf.Approximately(ledger.minuteSeconds, 60f))
            {
                log.AppendLine($"minuteSeconds {ledger.minuteSeconds} -> 60 (製品は 60 固定)");
                ledger.minuteSeconds = 60f;
            }
            if (ledger.tickSeconds <= 0f || ledger.tickSeconds > 60f)
            {
                log.AppendLine($"tickSeconds {ledger.tickSeconds} -> 15");
                ledger.tickSeconds = 15f;
            }
            // IMP-PRG-009: 入手経路の額も製品値へ正す。
            // 裏山改変: 初期配布 0 / おじいさん 200 (正本は IMP-PRG-001 と game-design §4)
            if (ledger.initialPoints != InitialPoints)
            {
                log.AppendLine($"initialPoints {ledger.initialPoints} -> {InitialPoints}");
                ledger.initialPoints = InitialPoints;
            }
            if (ledger.keeperPoints != KeeperPoints)
            {
                log.AppendLine($"keeperPoints {ledger.keeperPoints} -> {KeeperPoints}");
                ledger.keeperPoints = KeeperPoints;
            }

            // **backing の SyncMethod を自分でそろえる。**
            // `AddUdonSharpComponent` が作る UdonBehaviour は既定の Continuous で生まれ、
            // SyncMode None へ直すのは UdonSharpEditorManager の検証パス
            // (スクリプト再読み込み・シーン再オープン等) に任される。
            // つまり **Setup 直後の Verify は必ず赤くなる** — 待てば直るものを
            // 赤で出し続けると Verify が信用されなくなるので、ここで確定させる
            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(ledger);
            if (ub == null)
            {
                log.AppendLine("WARN backing UdonBehaviour が取れない (force refresh 後に再実行)");
            }
            else if (ub.SyncMethod != VRC.SDKBase.Networking.SyncType.None)
            {
                log.AppendLine($"SyncMethod {ub.SyncMethod} -> None");
                ub.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
                EditorUtility.SetDirty(ub);
            }

            // 裏山改変: 名前空間を毎回正す (KH_Commerce_v1)。BindCatalog も同じ値を書く
            if (ledger.storageNamespace != KD_ProgressLedger.ProductionNamespace)
            {
                log.AppendLine($"storageNamespace {ledger.storageNamespace} -> {KD_ProgressLedger.ProductionNamespace}");
                ledger.storageNamespace = KD_ProgressLedger.ProductionNamespace;
            }

            bool copyOk = TryCopy(ledger, log);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("NOTE products は Menu/4 Setup Shop が配線する (古民家と同じ順序。現在 "
                + (ledger.products == null ? 0 : ledger.products.Length) + " 件)");
            log.AppendLine("NOTE 値段は KD_ShopItem 側が正本。台帳は値段を知らないので、"
                + "値段を変えても既存プレイヤーの残高は動かない");
            log.AppendLine("NOTE 実機で残高が残るには VRChat 側でワールドの Persistence が "
                + "有効になっていること。Unity 側の設定は無い");
            log.AppendLine(copyOk
                ? "SETUP RESULT: PASS (scene not saved yet)"
                : "SETUP RESULT: FAIL (force refresh 後に再実行)");
            log.AppendLine("次: Menu/4 Setup Shop → Menu/3b Verify Progress / 4a Verify Shop");
            Finish(HillsideSetupLog, log);
        }

        /// <summary>裏山改変: ルートの KH_Dogs (1 個) を返す。無ければ FAIL を書いて null。</summary>
        internal static Transform FindHillsideRoot(StringBuilder log)
        {
            int n;
            var dogs = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out n);
            if (dogs == null || n != 1 || dogs.transform.parent != null)
            {
                log.AppendLine("FAIL " + KD_Hillside.DogsRootName + " がルートに 1 個ない (" + n + ")");
                return null;
            }
            return dogs.transform;
        }

        // ================================================================ 3: Verify

        [MenuItem("Tools/KominkaHillside/Menu/3b Verify Progress")]   // 裏山改変: Imported-Unused/Progress System/3 から付け替え
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ProgressSetup] Verify (裏山 段階 1)");
            int fail = 0;
            void Check(bool cond, string what)
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + what);
                if (!cond) fail++;
            }

            // ---- Program Assets ----
            Check(AssetDatabase.LoadMainAssetAtPath(LegacyProgram) == null,
                "旧 KD_ProgressManager.asset が消えている");

            var ledgerPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(LedgerProgram);
            var shopPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ShopProgram);
            Check(ledgerPa != null && ledgerPa.sourceCsScript != null
                && ledgerPa.sourceCsScript == AssetDatabase.LoadAssetAtPath<MonoScript>(LedgerScript),
                "ledger program asset points at KD_ProgressLedger.cs");
            Check(shopPa != null && shopPa.sourceCsScript != null
                && shopPa.sourceCsScript == AssetDatabase.LoadAssetAtPath<MonoScript>(ShopScript),
                "shop program asset points at KD_ShopItem.cs");
            // **これが「同期変数を増やしていない」の機械検査。**
            // SyncMode None は同期変数も SendCustomNetworkEvent も構造的に持てない
            Check(ledgerPa != null && ledgerPa.behaviourSyncMode == BehaviourSyncMode.None,
                "ledger behaviourSyncMode == None (同期変数を持てない)");
            Check(shopPa != null && shopPa.behaviourSyncMode == BehaviourSyncMode.None,
                "shop behaviourSyncMode == None (購入は完全にローカル)");

            // ---- シーン ----
            int found;
            var sysGo = KD_SceneObjects.Find(SystemName, out found);
            Check(found == 1, SystemName + " exists exactly once (actual " + found + ")");
            // 裏山改変: 置き場所は KH_Dogs 直下 (古民家の SystemsPath 前提は裏山では対象外)
            Check(sysGo != null && KD_SceneObjects.PathOf(sysGo) == HillsidePath,
                "置き場所が " + HillsidePath + " (actual " + (sysGo == null ? "-" : KD_SceneObjects.PathOf(sysGo)) + ")");
            log.AppendLine("  SKIP 古民家の置き場所 (" + KD_DogDefs.SystemsPath + ") の前提 — 裏山では KH_Dogs 直下");
            log.AppendLine("  SKIP ギフトコード (KD_GiftCodePanel / Bonus.gift.*) の前提 — 裏山では置かない (IMP-PRG-001 やらないこと)");
            var ledger = sysGo == null ? null : sysGo.GetUdonSharpComponent<KD_ProgressLedger>();
            Check(ledger != null, "KD_ProgressLedger on " + SystemName);

            if (sysGo != null)
            {
                int missing = 0;
                foreach (var c in sysGo.GetComponents<Component>())
                    if (c == null) missing++;
                Check(missing == 0, $"{SystemName} に Missing Script が無い (actual {missing})");
            }

            var ubs = sysGo == null
                ? new VRC.Udon.UdonBehaviour[0]
                : sysGo.GetComponents<VRC.Udon.UdonBehaviour>();
            Check(ubs.Length == 1, "backing UdonBehaviour count == 1 (actual " + ubs.Length + ")");
            if (ubs.Length == 1)
                Check(ubs[0].SyncMethod == VRC.SDKBase.Networking.SyncType.None,
                    "UdonBehaviour.SyncMethod == None");

            if (ledger != null)
            {
                Check(Mathf.Approximately(ledger.minuteSeconds, 60f),
                    $"minuteSeconds == 60 (actual {ledger.minuteSeconds}) — " +
                    "デバッグ盤は実行時に縮めるので、シリアライズ値が 60 でなければ " +
                    "**加速したまま公開される**");
                Check(ledger.tickSeconds > 0f && ledger.tickSeconds <= 60f,
                    $"0 < tickSeconds <= 60 (actual {ledger.tickSeconds})");
                Check(ledger.initialPoints == InitialPoints,
                    $"initialPoints == {InitialPoints} (actual {ledger.initialPoints}) — 裏山は初期配布なし (Bonus.initial を書かない)");
                Check(ledger.keeperPoints == KeeperPoints,
                    $"keeperPoints == {KeeperPoints} (actual {ledger.keeperPoints}) — おじいさんの一度だけ");
                // 裏山改変: 名前空間 (定数と、シーン値と、backing heap の 3 つ)
                Check(KD_ProgressLedger.ProductionNamespace == "KH_Commerce_v1" && KD_ProgressLedger.TestNamespace == "KH_TestCommerce_v1",
                    "名前空間の定数が KH_Commerce_v1 / KH_TestCommerce_v1");
                Check(ledger.storageNamespace == KD_ProgressLedger.ProductionNamespace,
                    $"storageNamespace == {KD_ProgressLedger.ProductionNamespace} (actual {ledger.storageNamespace})");
                var heapUb = UdonSharpEditorUtility.GetBackingUdonBehaviour(ledger);
                string nsBacking; int ib, kb;
                Check(heapUb != null && heapUb.publicVariables.TryGetVariableValue("storageNamespace", out nsBacking)
                    && nsBacking == KD_ProgressLedger.ProductionNamespace, "backing heap の storageNamespace も KH_Commerce_v1");
                Check(heapUb != null && heapUb.publicVariables.TryGetVariableValue("initialPoints", out ib) && ib == InitialPoints
                    && heapUb.publicVariables.TryGetVariableValue("keeperPoints", out kb) && kb == KeeperPoints,
                    $"backing heap の initialPoints / keeperPoints も {InitialPoints} / {KeeperPoints}");

                // 裏山改変: products は Menu/4 Setup Shop が配線する。カタログがあれば一致を見る
                var catGo = KD_SceneObjects.Find(KD_DogDefs.ShopCatalogName);
                var cat = catGo == null ? null : catGo.GetComponent<KD_ShopCatalog>();
                if (cat == null)
                    log.AppendLine("  SKIP products の検査 (KD_ShopCatalog が無い。先に Menu/4 Setup Shop)");
                else
                {
                    int want = KD_ProductDefinitions.All.Length;
                    Check(ledger.products != null && ledger.products.Length == want,
                        $"ledger.products が商品表と同数 {want} (actual {(ledger.products == null ? 0 : ledger.products.Length)})");
                    Check(ledger.products != null && cat.items != null && System.Linq.Enumerable.SequenceEqual(ledger.products, cat.items),
                        "ledger.products == catalog.items (同じ並び・同じ参照)");
                    object raw;
                    Check(heapUb != null && heapUb.publicVariables.TryGetVariableValue("products", out raw)
                        && raw is System.Array arr && arr.Length == want,
                        "backing heap の products も同数 (CopyProxyToUdon 済み)");
                }
            }

            // ---- シーン中の KD_ShopItem すべて ----
            //
            // **未配線・値段 0・番号無しは「押しても買えないボタン」になり、
            // Play では「残高が足りない」と区別が付かない。** ここで落とす
            var shops = Object.FindObjectsOfType<KD_ShopItem>();
            log.AppendLine($"-- KD_ShopItem: {shops.Length} 個");
            var usedIds = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var s in shops)
            {
                string tag = s.gameObject.name + ": ";
                Check(s.ledger != null, tag + "ledger wired");
                // **「price > 0」を全品へ要求しない** (2026-08-23 / IMP-PRG-002)。
                // 無料品 (`price == 0`) は**買う対象ではなく最初から所持している品**として
                // 棚に並ぶ、正当な商品の型になった。有料なのに値段が無い場合を落とすのは
                // IsConfigured() の仕事なので、そちらへ一本化する
                Check(s.IsConfigured(),
                    tag + $"IsConfigured() (price {s.price} / productKey {s.productKey})");
                // **メニューの品は Collider を持たない** (IMP-PET-006 / 2026-08-23) —
                // 撫でエフェクトのように**実物が存在しない**商品は行から選ぶので、
                // 触る対象そのものが無い。逆に付いていると腕が当たっただけでパネルが開く
                Check(s.menuOnly ? s.GetComponent<Collider>() == null
                                 : s.GetComponent<Collider>() != null,
                    tag + (s.menuOnly ? "メニューの品なので Collider を持たない"
                                      : "Collider がある (Interact に必要)"));

                // **番号の重複は最悪の壊れ方** — 別の品を買ったのに同じビットが立ち、
                // 片方を買うともう片方も所持済みになる。Play では気づきにくい。
                // **消耗品も無料品も番号を持つ**ようになったので、全品を見る
                // (恒久 ID を 1 種類へ統一した — shop-foundation §3)
                if (usedIds.ContainsKey(s.productKey))
                    Check(false, tag + $"productKey {s.productKey} が "
                        + usedIds[s.productKey] + " と重複している");
                else
                    usedIds[s.productKey] = s.gameObject.name;
            }

            // ---- デバッグ盤が名前で読む記号 ----
            //
            // GetProgramVariable は記号が無くても null を返すだけなので、
            // フィールドを 1 つ改名すると**盤が黙って 0 を並べる** (静かに嘘をつく)
            if (ubs.Length == 1) Check(CheckSymbols(ubs[0], log), "debug HUD symbols present");

            // ---- 純関数の自己テスト ----
            if (ledger == null)
            {
                log.AppendLine("  SKIP 純関数の自己テスト (ledger が無い。先に Menu/3a Setup Progress)");
                fail++;
            }
            else
            {
                fail += SelfTest(ledger, log);
            }

            log.AppendLine(fail == 0 ? "VERIFY RESULT: PASS" : $"VERIFY RESULT: FAIL ({fail} checks)");
            Finish(HillsideVerifyLog, log);
        }

        /// <summary>台帳の純関数の自己テスト。**Play を 1 秒も使わずに算術を確かめる。**
        ///
        /// プロキシのメソッドを直接呼ぶ (これらは状態を見ないので、
        /// UdonBehaviour が動いていない編集時でも正しい答えを返す)。
        /// 戻り値 = 失敗件数。</summary>
        static int SelfTest(KD_ProgressLedger m, StringBuilder log)
        {
            int fail = 0;
            int n = 0;
            void Eq(int actual, int expected, string what)
            {
                n++;
                if (actual != expected)
                {
                    fail++;
                    log.AppendLine($"  FAIL [{n}] {what}: expected {expected}, got {actual}");
                }
            }
            void Is(bool actual, bool expected, string what)
            {
                n++;
                if (actual != expected)
                {
                    fail++;
                    log.AppendLine($"  FAIL [{n}] {what}: expected {expected}, got {actual}");
                }
            }

            const int Max = KD_ProgressLedger.MaxPoints;

            // --- 残高の導出 (残高 = 稼ぎ − 支払い) ---
            Eq(m.BalanceOf(0, 0), 0, "0 分なら残高 0");
            Eq(m.BalanceOf(15, 0), 15, "何も買っていなければ残高 = 稼ぎ");
            Eq(m.BalanceOf(15, 10), 5, "10pt 払ったら**残高が 5 に減る**");
            Eq(m.BalanceOf(15, 15), 0, "使い切ると残高 0");
            Eq(m.BalanceOf(15, 30), 0, "**払いすぎの記録があっても負にしない** (値段を上げた後など)");
            Eq(m.BalanceOf(-5, 0), 0, "壊れた負の稼ぎは 0 へ");
            Eq(m.BalanceOf(15, -5), 15, "壊れた負の支払いは 0 へ");
            Eq(m.BalanceOf(Max * 2, 0), Max, "稼ぎは上限で止める (オーバーフロー防止)");
            Eq(m.BalanceOf(Max * 2, Max * 2), 0, "支払いも上限で止める");

            Eq(m.BalanceOf(20, 10), 10, "Stored spending is independent of current prices");
            log.AppendLine($"  self-test {n - fail}/{n} passed");
            return fail;
        }

        /// <summary>デバッグ盤が名前で読む記号が、コンパイル済みプログラムに
        /// 出ているかを確かめる。</summary>
        static bool CheckSymbols(VRC.Udon.UdonBehaviour ub, StringBuilder log)
        {
            string[] required = {
                "_loaded", "_earned", "_spent", "_owned", "_pendingMinutes",
                "_grantedMinutes", "_version",
                "_dbgGrantCount", "_dbgLastGrantAt", "_dbgBuyCount", "_dbgRejectCount",
                "minuteSeconds", "tickSeconds",
            };
            if (ub == null || ub.programSource == null
                || ub.programSource.SerializedProgramAsset == null)
            {
                log.AppendLine("       (program asset へ辿れないので記号は未確認)");
                return false;
            }
            var prog = ub.programSource.SerializedProgramAsset.RetrieveProgram();
            if (prog == null || prog.SymbolTable == null)
            {
                log.AppendLine("       (SymbolTable が無い。force refresh してから再実行)");
                return false;
            }
            var symbols = new System.Collections.Generic.HashSet<string>(prog.SymbolTable.GetSymbols());
            var missing = new System.Collections.Generic.List<string>();
            foreach (var s in required)
                if (!symbols.Contains(s)) missing.Add(s);
            if (missing.Count == 0) return true;
            log.AppendLine("       missing: " + string.Join(", ", missing.ToArray())
                + " — 盤はこの項目を 0 として表示してしまう "
                + "(改名したなら KD_ProgressDebugHud 側もそろえること)");
            return false;
        }

        // ---- helpers (他の SetupEditor と同型) ----

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


        static void Finish(string file, StringBuilder log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }
    }
}
