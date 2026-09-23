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
    /// IMP-DOG-019 Phase 3a: **そばで寝るモードの「同期の器」を作る / 数える。**
    ///
    /// 実行順 (別実行にする — Program Asset 直後の CopyProxyToUdon は outdated error):
    /// <code>
    ///   1 - Create Program Assets  → force refresh (コンパイル完了まで待つ)
    ///   2 - Setup Sleep System
    ///   3 - Verify Sleep System
    /// </code>
    ///
    /// 前提: Dog Pack (`KD_DogPack`) が構築済み。
    ///
    /// シーンに置くもの (素のシーンオブジェクト。プレハブは使わない):
    /// <code>
    /// KD_Systems/
    ///   KD_SleepBoard            台帳 + 開放フラグ (Manual。Owner は開放を切り替えたときだけ動く)
    ///     KD_SleepSlot_{key}     犬 1 匹ぶんの枠 (Manual。sleeperId / calledMs)
    /// </code>
    ///
    /// **枠を台帳の子にしてある**のは Issue §3 の図のとおり。位置に意味は無く
    /// (どちらも `KD_Systems` の下)、階層は「読み手は台帳を通す」を表しているだけ。
    ///
    /// **役割分担 (IMP-INF-003)** — Setup は作るだけ・Layout は動かすだけ・Verify は数えるだけ。
    /// ここは既存の親も位置も触らない (`KD_SceneObjects.PlaceIfNew` と同じ規律で、
    /// 新しく作ったものだけを台帳の下へ入れる)。
    /// </summary>
    public static class KD_SleepSetupEditor
    {
        public const string BoardScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepBoard.cs";
        public const string BoardProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepBoard.asset";
        public const string SlotScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepSlot.cs";
        public const string SlotProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepSlot.asset";

        const string LogFile = "Logs/imp-dog-019-sleep.txt";

        // ================================================================ 1

        [MenuItem("Tools/KominkaHillside/Menu/6a Create Sleep Programs")]
        public static void CreateProgramAssets()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_SleepSetup] Create program assets");
            CreateProgramAsset(BoardScriptPath, BoardProgramPath, log);
            CreateProgramAsset(SlotScriptPath, SlotProgramPath, log);
            log.AppendLine("次: force refresh (コンパイル完了まで待つ) してから 2 - Setup Sleep System");
            Finish(log);
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

        // ================================================================ 2

        [MenuItem("Tools/KominkaHillside/Menu/6 Setup Sleep")]
        public static void Setup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_SleepSetup] Setup");
            bool ok = true;
            int found;

            // ---- 名簿 (前提) ----
            var packGo = KD_SceneObjects.Find(KD_DogDefs.DogPackName, out found);
            var pack = (found == 1 && packGo != null)
                ? packGo.GetUdonSharpComponent<KD_DogPack>() : null;
            if (pack == null)
            {
                log.AppendLine("FAIL " + KD_DogDefs.DogPackName + " が取れない (" + found + " 個)。"
                    + "先に Tools/KominkaHillside/Menu/Imported-Unused/Dog Pack/2 - Setup Dog Pack を実行");
                Finish(log);
                return;
            }

            // ---- 台帳 ----
            //
            // **Manual なので専用 GameObject** (U# は同一 GameObject で同期方式を混在できない)。
            // Owner は開放フラグを書くときだけ動く (マスター固定ではない — おやつ台帳と違い、権限者が切り替えるたびにその人へ移り、戻さない)
            var boardGo = KD_SceneObjects.Find(KD_DogDefs.SleepBoardName, out found);
            if (found > 1)
            {
                log.AppendLine("FAIL '" + KD_DogDefs.SleepBoardName + "' がシーン内に "
                    + found + " 個ある (手で解消する)");
                Finish(log);
                return;
            }
            if (boardGo == null)
            {
                boardGo = new GameObject(KD_DogDefs.SleepBoardName);
                Undo.RegisterCreatedObjectUndo(boardGo, "Create " + KD_DogDefs.SleepBoardName);
                KD_SceneObjects.PlaceIfNew(boardGo, KD_DogDefs.SystemsPath);   // 位置に意味が無い
                log.AppendLine("created " + KD_DogDefs.SleepBoardName);
            }
            var board = boardGo.GetUdonSharpComponent<KD_SleepBoard>();
            if (board == null)
            {
                board = boardGo.AddUdonSharpComponent<KD_SleepBoard>();
                log.AppendLine("   added KD_SleepBoard");
            }

            // ---- 枠 (犬 1 匹に 1 つ。**台帳の子**) ----
            var defs = KD_DogDefs.All;
            var slots = new KD_SleepSlot[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                string slotName = KD_DogDefs.SleepSlotNameOf(defs[i].key);
                var slotGo = KD_SceneObjects.Find(slotName, out found);
                if (found > 1)
                {
                    log.AppendLine("FAIL '" + slotName + "' がシーン内に " + found + " 個ある");
                    ok = false;
                    continue;
                }
                if (slotGo == null)
                {
                    slotGo = new GameObject(slotName);
                    Undo.RegisterCreatedObjectUndo(slotGo, "Create " + slotName);
                    log.AppendLine("created " + slotName);
                }
                // **親を張るのは「今作った」ものだけ** (Setup は既存の親を触らない)。
                // パス経由 (EnsureContainer) にしないのは、台帳が別の場所へ移されていた場合に
                // **2 個目の KD_SleepBoard を掘ってしまう**ため — 台帳の実体は 1 つと数えてある
                if (slotGo.transform.parent == null)
                    Undo.SetTransformParent(slotGo.transform, boardGo.transform, true,
                        "Place " + slotName);

                var slot = slotGo.GetUdonSharpComponent<KD_SleepSlot>();
                if (slot == null)
                {
                    slot = slotGo.AddUdonSharpComponent<KD_SleepSlot>();
                    log.AppendLine("   added KD_SleepSlot (" + slotName + ")");
                }
                slot.dogIndex = i;
                slot.pack = pack;
                slot.board = board;
                slots[i] = slot;
                ok &= TryCopy(slot, log);
                ok &= EnsureManualSync(slot, slotName, log);
                log.AppendLine("   wired " + slotName + ": dogIndex=" + i
                    + " (" + defs[i].displayName + ")");
            }

            board.slots = slots;
            board.pack = pack;
            ok &= TryCopy(board, log);
            ok &= EnsureManualSync(board, KD_DogDefs.SleepBoardName, log);
            log.AppendLine("   wired " + KD_DogDefs.SleepBoardName + ".slots (" + slots.Length
                + ") / .pack");

            // ---- 各犬の徘徊層へ台帳を配線 (IMP-DOG-019 Phase 2) ----
            //
            // **犬は台帳を読むだけ** (`LiveSleeperOf` / `SlotOf`)。おやつの
            // `wander.treatBox` とまったく同じ形で、犬側の同期変数は 1 本も増えない。
            // 配線が欠けた犬は「呼んでも来ない犬」になる (無言で 1 匹だけ機能が無い) ので、
            // 3 - Verify が 4 匹ぶん数える
            foreach (var def in KD_DogDefs.All)
            {
                var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out found);
                var wander = (found == 1 && wanderGo != null)
                    ? wanderGo.GetUdonSharpComponent<KD_DogWanderController>() : null;
                if (wander == null)
                {
                    log.AppendLine("   FAIL " + def.WanderSystemName + " が取れない ("
                        + found + " 個) - この犬は呼んでも来ない");
                    ok = false;
                    continue;
                }
                if (wander.sleepBoard == board)
                {
                    log.AppendLine("   " + def.WanderSystemName + ".sleepBoard は配線済み (触らない)");
                    continue;
                }
                wander.sleepBoard = board;
                ok &= TryCopy(wander, log);
                log.AppendLine("   wired " + def.WanderSystemName + ".sleepBoard");
            }

            // ---- 各犬の Fetch へ台帳を配線 (IMP-DOG-019 Phase 5) ----
            //
            // **寝ている犬は投擲に反応しない** (割り込みの順位では寝るが最上位)。
            // Fetch も**読むだけ**で、同期変数も Ownership も 1 つも動かない。
            //
            // ⚠ **`dogState` では代用できない** — 寝ている犬の `dogState` は Idle のままなので、
            // 既存の `dogState != StateIdle` ガードは 1 件も弾かない。
            // 配線が欠けた犬は「寝ていても他人の投擲で走り出す犬」になる (占有が破れる)
            foreach (var def in KD_DogDefs.All)
            {
                var fetchGo = KD_SceneObjects.Find(def.FetchSystemName, out found);
                var fetch = (found == 1 && fetchGo != null)
                    ? fetchGo.GetUdonSharpComponent<KD_DogFetchController>() : null;
                if (fetch == null)
                {
                    log.AppendLine("   FAIL " + def.FetchSystemName + " が取れない ("
                        + found + " 個) - この犬は寝ていても投擲に反応する");
                    ok = false;
                    continue;
                }
                if (fetch.sleepBoard == board)
                {
                    log.AppendLine("   " + def.FetchSystemName + ".sleepBoard は配線済み (触らない)");
                    continue;
                }
                fetch.sleepBoard = board;
                ok &= TryCopy(fetch, log);
                log.AppendLine("   wired " + def.FetchSystemName + ".sleepBoard");
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("SETUP RESULT: " + (ok ? "PASS" : "FAIL"));
            log.AppendLine("**シーンは保存していない。** 3 - Verify Sleep System と "
                + "Scene Layout/3 - Verify を通してから保存する");
            Finish(log);
        }

        // ================================================================ 3

        [MenuItem("Tools/KominkaHillside/Menu/6b Verify Sleep System")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_SleepSetup] Verify");
            int fail = 0;
            int found;

            // ---- Program Asset (2 本・どちらも Manual) ----
            var boardPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(BoardProgramPath);
            var slotPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(SlotProgramPath);
            Check(ref fail, log, boardPa != null && boardPa.sourceCsScript != null,
                "program asset ok: KD_SleepBoard");
            Check(ref fail, log, slotPa != null && slotPa.sourceCsScript != null,
                "program asset ok: KD_SleepSlot");
            Check(ref fail, log,
                boardPa != null && boardPa.behaviourSyncMode == BehaviourSyncMode.Manual,
                "KD_SleepBoard の behaviourSyncMode == Manual (openToAll)");
            Check(ref fail, log,
                slotPa != null && slotPa.behaviourSyncMode == BehaviourSyncMode.Manual,
                "KD_SleepSlot の behaviourSyncMode == Manual (sleeperId / calledMs)");

            // ---- 名簿 ----
            var packGo = KD_SceneObjects.Find(KD_DogDefs.DogPackName, out found);
            var pack = (found == 1 && packGo != null)
                ? packGo.GetUdonSharpComponent<KD_DogPack>() : null;
            Check(ref fail, log, pack != null,
                KD_DogDefs.DogPackName + " が取れる (actual " + found + " 個)");

            // ---- 台帳 1 個 ----
            var boardGo = KD_SceneObjects.Find(KD_DogDefs.SleepBoardName, out found);
            Check(ref fail, log, found == 1 && boardGo != null,
                KD_DogDefs.SleepBoardName + " がシーン内に 1 個 (actual " + found + ")");
            var board = boardGo != null ? boardGo.GetUdonSharpComponent<KD_SleepBoard>() : null;
            Check(ref fail, log, board != null, KD_DogDefs.SleepBoardName + ": KD_SleepBoard が付いている");
            if (boardGo != null)
            {
                Check(ref fail, log, boardGo.GetComponents<VRC.Udon.UdonBehaviour>().Length == 1,
                    KD_DogDefs.SleepBoardName + " の UdonBehaviour は 1 個 (Manual なので同居不可)");
                Check(ref fail, log, boardGo.GetComponent<VRCObjectSync>() == null,
                    KD_DogDefs.SleepBoardName + " に VRCObjectSync が無い (Manual 同期と同居させない)");
                Check(ref fail, log, ParentPathOf(boardGo) == KD_DogDefs.SystemsPath,
                    KD_DogDefs.SleepBoardName + " が " + KD_DogDefs.SystemsPath
                    + " の直下 (actual " + ParentPathOf(boardGo) + ")");
                Check(ref fail, log, IsIdentity(boardGo.transform),
                    KD_DogDefs.SleepBoardName + " の Transform が単位元 (枠 4 つの親でもある)");
                // **Program Asset の宣言ではなくシーンの実物を見る** (足したては Continuous)
                Check(ref fail, log, SceneSyncOf(boardGo) == VRC.SDKBase.Networking.SyncType.Manual,
                    KD_DogDefs.SleepBoardName + " の UdonBehaviour.SyncMethod == Manual (actual "
                    + SceneSyncOf(boardGo) + ")");
            }

            // ---- 枠 4 つ ----
            var defs = KD_DogDefs.All;
            var slotGos = new GameObject[defs.Length];
            var slotBs = new KD_SleepSlot[defs.Length];
            var seen = new bool[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                string slotName = KD_DogDefs.SleepSlotNameOf(defs[i].key);
                string tag = slotName + ": ";
                slotGos[i] = KD_SceneObjects.Find(slotName, out found);
                if (!Check(ref fail, log, found == 1 && slotGos[i] != null,
                        slotName + " がシーン内に 1 個 (actual " + found + ")")) continue;

                slotBs[i] = slotGos[i].GetUdonSharpComponent<KD_SleepSlot>();
                Check(ref fail, log, slotGos[i].GetComponents<VRC.Udon.UdonBehaviour>().Length == 1,
                    tag + "UdonBehaviour は 1 個 (Manual なので同居不可)");
                Check(ref fail, log, slotGos[i].GetComponent<VRCObjectSync>() == null,
                    tag + "VRCObjectSync が無い (Manual 同期と同居させない)");
                Check(ref fail, log,
                    boardGo != null && slotGos[i].transform.parent == boardGo.transform,
                    tag + "台帳 (" + KD_DogDefs.SleepBoardName + ") の子 (actual "
                    + ParentPathOf(slotGos[i]) + ")");
                Check(ref fail, log, IsIdentity(slotGos[i].transform),
                    tag + "Transform が単位元 (位置に意味が無い)");
                Check(ref fail, log,
                    SceneSyncOf(slotGos[i]) == VRC.SDKBase.Networking.SyncType.Manual,
                    tag + "UdonBehaviour.SyncMethod == Manual (actual " + SceneSyncOf(slotGos[i]) + ")");

                if (!Check(ref fail, log, slotBs[i] != null, tag + "KD_SleepSlot が付いている")) continue;
                // **添字と自己申告がずれていると、別の犬が寝る**
                if (Check(ref fail, log, slotBs[i].dogIndex == i,
                        tag + "dogIndex == " + i + " (actual " + slotBs[i].dogIndex + ")"))
                {
                    if (slotBs[i].dogIndex >= 0 && slotBs[i].dogIndex < seen.Length)
                    {
                        Check(ref fail, log, !seen[slotBs[i].dogIndex],
                            tag + "dogIndex が重複していない");
                        seen[slotBs[i].dogIndex] = true;
                    }
                }
                Check(ref fail, log, slotBs[i].pack != null && slotBs[i].pack == pack,
                    tag + "pack == " + KD_DogDefs.DogPackName);
                Check(ref fail, log, slotBs[i].board != null && slotBs[i].board == board,
                    tag + "board == " + KD_DogDefs.SleepBoardName);
            }

            // **5 匹目の枠を作っていない** (犬を増やしたときに Setup を流し忘れる、の逆側)
            int extra = 0;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("KD_SleepSlot_")) extra++;
            Check(ref fail, log, extra == defs.Length,
                "KD_SleepSlot_* がちょうど " + defs.Length + " 個 (actual " + extra + ")");

            // ---- 台帳の配線 ----
            if (board != null)
            {
                bool slotsOk = board.slots != null && board.slots.Length == defs.Length;
                for (int i = 0; slotsOk && i < defs.Length; i++)
                    if (board.slots[i] == null || board.slots[i] != slotBs[i]) slotsOk = false;
                Check(ref fail, log, slotsOk,
                    KD_DogDefs.SleepBoardName + ".slots が " + defs.Length + " 本で添字どおり");
                Check(ref fail, log, board.pack != null && board.pack == pack,
                    KD_DogDefs.SleepBoardName + ".pack 配線");
                // 名簿の長さがそろっていないと `LiveSleeperOf(dogIndex)` が犬とずれる
                Check(ref fail, log, pack != null && pack.dogRoots != null
                    && pack.dogRoots.Length == defs.Length,
                    KD_DogDefs.SleepBoardName + ".pack の名簿が犬 " + defs.Length + " 匹 (actual "
                    + (pack == null || pack.dogRoots == null ? -1 : pack.dogRoots.Length) + ")");
            }

            // ---- 各犬の徘徊層への配線 (IMP-DOG-019 Phase 2) ----
            //
            // **1 匹でも欠けると、その犬だけ呼んでも来ない** (しかも無言)。
            // おやつの `wander.treatBox` を Verify が数えているのと同じ理由で 4 匹ぶん数える
            foreach (var def in KD_DogDefs.All)
            {
                var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out found);
                var wander = (found == 1 && wanderGo != null)
                    ? wanderGo.GetUdonSharpComponent<KD_DogWanderController>() : null;
                if (!Check(ref fail, log, wander != null,
                        def.WanderSystemName + " が取れる (actual " + found + " 個)")) continue;
                Check(ref fail, log, wander.sleepBoard != null && wander.sleepBoard == board,
                    def.WanderSystemName + ".sleepBoard == " + KD_DogDefs.SleepBoardName
                    + " (犬はここから占有者を読む)");
            }

            // ---- 各犬の Fetch への配線 (IMP-DOG-019 Phase 5) ----
            //
            // **1 匹でも欠けると、その犬だけ寝ていても投擲で走り出す** (しかも無言)。
            // `dogState` は Idle のままなので既存のガードでは弾けない
            foreach (var def in KD_DogDefs.All)
            {
                var fetchGo = KD_SceneObjects.Find(def.FetchSystemName, out found);
                var fetch = (found == 1 && fetchGo != null)
                    ? fetchGo.GetUdonSharpComponent<KD_DogFetchController>() : null;
                if (!Check(ref fail, log, fetch != null,
                        def.FetchSystemName + " が取れる (actual " + found + " 個)")) continue;
                Check(ref fail, log, fetch.sleepBoard != null && fetch.sleepBoard == board,
                    def.FetchSystemName + ".sleepBoard == " + KD_DogDefs.SleepBoardName
                    + " (**寝ている犬は投擲に反応しない**)");
            }

            // ---- 手元メニュー (IMP-DOG-019 Phase 5) ----
            //
            // ねるタブの呼び口と、おもちゃタブの `∧ !sleeping` はどちらも
            // `KD_TreatPouch.sleepBoard` が入口。**中身の検査は Treat System/3 が持つ**ので、
            // ここでは「同じ台帳を指しているか」だけを数える (正本を 2 つにしない)
            var pouchGo = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName, out found);
            var pouch = (found == 1 && pouchGo != null)
                ? pouchGo.GetUdonSharpComponent<KD_TreatPouch>() : null;
            if (Check(ref fail, log, pouch != null,
                    KD_DogDefs.TreatPouchName + " が取れる (actual " + found + " 個)"))
                Check(ref fail, log, pouch.sleepBoard != null && pouch.sleepBoard == board,
                    KD_DogDefs.TreatPouchName + ".sleepBoard == " + KD_DogDefs.SleepBoardName
                    + " (ねるタブ + おもちゃタブの `∧ !sleeping`)");

            // ---- 置き場所の正本 (LayoutTable) に載っていること ----
            int rows = 0;
            foreach (var r in KD_DogDefs.LayoutTable)
            {
                if (r.name == KD_DogDefs.SleepBoardName && r.containerPath == KD_DogDefs.SystemsPath)
                    rows++;
                foreach (var d in defs)
                    if (r.name == KD_DogDefs.SleepSlotNameOf(d.key)
                        && r.containerPath == KD_DogDefs.SleepSlotContainerPath) rows++;
            }
            Check(ref fail, log, rows == defs.Length + 1,
                "LayoutTable に " + (defs.Length + 1) + " 行 (台帳 1 + 枠 " + defs.Length
                + ") がある (actual " + rows + ")");

            log.AppendLine(fail == 0 ? "VERIFY RESULT: PASS" : "VERIFY RESULT: FAIL (" + fail + " checks)");
            Finish(log);
        }

        // ---- helpers (他の Setup Editor と同型) ----

        /// <summary>**シーンの `UdonBehaviour` の SyncMethod を Manual へそろえる。**
        ///
        /// `AddUdonSharpComponent` で足した直後の UdonBehaviour は **`Continuous` のまま**で、
        /// Program Asset の `behaviourSyncMode` (= `[UdonBehaviourSyncMode(Manual)]`) は
        /// 自動では乗らない (実測 2026-09-04 — 既存の `KD_TreatSlot` は Manual なのに
        /// 足したての枠 4 つと台帳が全部 Continuous だった)。
        ///
        /// UdonSharp 側にも直す経路はある (`UdonSharpEditorManager.UpdateSyncModes` が
        /// シーンロード時などに走る) が、**「いつか直る」に頼ると
        /// 直る前にビルドした版が Continuous のまま出る。**
        /// 直った後に Verify を流して PASS を見ると、なお悪いことに気づけない。
        /// だから Setup がその場でそろえ、Verify がシーンの実物を数える。</summary>
        static bool EnsureManualSync(UdonSharpBehaviour proxy, string label, StringBuilder log)
        {
            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            if (ub == null)
            {
                log.AppendLine("   FAIL " + label + ": backing UdonBehaviour が取れない");
                return false;
            }
            if (ub.SyncMethod == VRC.SDKBase.Networking.SyncType.Manual) return true;
            log.AppendLine("   " + label + ": SyncMethod " + ub.SyncMethod + " -> Manual");
            ub.SyncMethod = VRC.SDKBase.Networking.SyncType.Manual;
            EditorUtility.SetDirty(ub);
            return true;
        }

        /// <summary>シーンに実際に載っている `UdonBehaviour` の SyncMethod
        /// (**Program Asset の宣言ではない**)。UdonBehaviour が無ければ `None` を返す。</summary>
        static VRC.SDKBase.Networking.SyncType SceneSyncOf(GameObject go)
        {
            if (go == null) return VRC.SDKBase.Networking.SyncType.None;
            var ub = go.GetComponent<VRC.Udon.UdonBehaviour>();
            return ub != null ? ub.SyncMethod : VRC.SDKBase.Networking.SyncType.None;
        }

        static bool IsIdentity(Transform t)
        {
            return t.localPosition == Vector3.zero
                && t.localRotation == Quaternion.identity
                && t.localScale == Vector3.one;
        }

        static string ParentPathOf(GameObject go)
        {
            if (go == null || go.transform.parent == null) return "(シーン直下)";
            return KD_SceneObjects.PathOf(go.transform.parent.gameObject);
        }

        static bool Check(ref int fail, StringBuilder log, bool ok, string label)
        {
            log.AppendLine((ok ? "  ok   " : "  FAIL ") + label);
            if (!ok) fail++;
            return ok;
        }

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

        static void Finish(StringBuilder log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile));
            File.WriteAllText(LogFile, log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }
    }
}
