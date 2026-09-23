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
        /// <summary>IMP-PRG-009: 初期配布 / おじいさんの額 (2026-09-14 ユーザー決定)。</summary>
        const int InitialPoints = 100;
        const int KeeperPoints = 100;

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

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Progress System/2 - Setup Progress System")]
        public static void Setup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ProgressSetup] Setup");

            int found;
            var sysGo = KD_SceneObjects.Find(SystemName, out found);
            if (found > 1)
            {
                log.AppendLine($"FAIL '{SystemName}' がシーン内に {found} 個ある。1 個に整理してから再実行");
                Finish("Logs/imp-prg-001-setup.txt", log);
                return;
            }
            if (sysGo == null)
            {
                // 位置は意味を持たない (音も絵も出さない台帳なので原点でよい)
                sysGo = new GameObject(SystemName);
                KD_SceneObjects.PlaceIfNew(sysGo, KD_DogDefs.SystemsPath);   // IMP-INF-003
                log.AppendLine("created " + SystemName);
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
            // IMP-PRG-009: 入手経路の額も製品値へ正す (初期配布 100 / おじいさん 100。
            // 正本は business-strategy §5.5 と IMP-PRG-009)
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

            bool copyOk = TryCopy(ledger, log);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("NOTE 配線は 1 本も無い。消費側 (IMP-FET-009 / IMP-TRT-001) が "
                + "KD_ShopItem を棚に並べて listener を受ける形で後から繋がる");
            log.AppendLine("NOTE 値段は KD_ShopItem 側が正本。台帳は値段を知らないので、"
                + "値段を変えても既存プレイヤーの残高は動かない");
            log.AppendLine("NOTE 実機で残高が残るには VRChat 側でワールドの Persistence が "
                + "有効になっていること。Unity 側の設定は無い");
            log.AppendLine(copyOk
                ? "SETUP RESULT: PASS (scene not saved yet)"
                : "SETUP RESULT: FAIL (force refresh 後に再実行)");
            Finish("Logs/imp-prg-001-setup.txt", log);
        }

        // ================================================================ 3: Verify

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Progress System/3 - Verify Progress System")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ProgressSetup] Verify");
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
                    $"initialPoints == {InitialPoints} (actual {ledger.initialPoints}) — 初期配布 (IMP-PRG-009)");
                Check(ledger.keeperPoints == KeeperPoints,
                    $"keeperPoints == {KeeperPoints} (actual {ledger.keeperPoints}) — おじいさん (IMP-PRG-009)");
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
                log.AppendLine("  SKIP 純関数の自己テスト (ledger が無い。先に 2 - Setup)");
                fail++;
            }
            else
            {
                fail += SelfTest(ledger, log);
            }

            log.AppendLine(fail == 0 ? "VERIFY RESULT: PASS" : $"VERIFY RESULT: FAIL ({fail} checks)");
            Finish("Logs/imp-prg-001-verify.txt", log);
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
