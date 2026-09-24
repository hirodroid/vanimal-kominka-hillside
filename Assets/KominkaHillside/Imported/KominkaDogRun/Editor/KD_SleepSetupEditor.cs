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
    /// IMP-DOG-019 Phase 3a: **そばで寝るモード (裏山では「呼ぶ」) の「同期の器」を作る / 数える。**
    ///
    /// 裏山改変: IMP-DOG-016、段階 2。器は KH_Dogs 直下 (古民家の "====KominkaDogRun====/KD_Systems" は作らない)。
    /// 実行順 (別実行にする — Program Asset 直後の CopyProxyToUdon は outdated error):
    /// <code>
    ///   Menu/6a Create Sleep Programs  → force refresh (コンパイル完了まで待つ)
    ///   Menu/6  Setup Sleep            台帳 + 枠 6 + 犬・Fetch・開催許可・手元メニュー・さんぽ (KH_LeashManager) への配線
    ///   Menu/6i Setup Sleep Placement  場所決め (KD_SleepPlacement。KD_SleepPlacementEditor)
    ///   Menu/6j Setup Call Poses       ポーズ面 (KD_CallPose。KD_CallPoseEditor)。追加動作 4 種の商品を枠へ配線
    ///   Menu/6b Verify Sleep System    上 3 つの検査をまとめて流す (斜面判定の定数と法線値を出力)
    /// </code>
    ///
    /// 前提: KH_Dogs/KD_DogPack・KD_WanderSystem_*・KD_FetchSystem_*・KH_Leashes (Walking/3 Build)、
    /// KH_Dogs/KD_TreatPouch (Menu/7)、KH_Dogs/KD_LiveAccess・KD_ShopCatalog (段階 1)。
    ///
    /// シーンに置くもの (素のシーンオブジェクト。プレハブは使わない):
    /// <code>
    /// KH_Dogs/
    ///   KD_SleepBoard            台帳 + 開放フラグ (Manual。Owner は開放を切り替えたときだけ動く)
    ///     KD_SleepSlot_{key}     犬 1 匹ぶんの枠 (Manual。予約 / 占有 / ポーズ)
    /// </code>
    ///
    /// **役割分担 (IMP-INF-003)** — Setup は作るだけ・Verify は数えるだけ。既存の親も位置も触らない
    /// (<c>KD_SceneObjects.PlaceIfNew</c> と同じ規律で、新しく作ったものだけを台帳の下へ入れる)。**シーンは保存しない。**
    /// </summary>
    public static class KD_SleepSetupEditor
    {
        public const string BoardScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepBoard.cs";
        public const string BoardProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepBoard.asset";
        public const string SlotScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepSlot.cs";
        public const string SlotProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepSlot.asset";
        public const string PlacementScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepPlacement.cs";
        public const string PlacementProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepPlacement.asset";
        public const string CallPoseScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_CallPose.cs";
        public const string CallPoseProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_CallPose.asset";

        // 裏山改変: ログは段階 0-b 以降の置き場所 (Temp/menu-rebuild/)
        const string LogFile = KD_Hillside.LogDir + "setup-sleep.txt";
        const string VerifyLogFile = KD_Hillside.LogDir + "verify-sleep.txt";

        // ================================================================ 6a

        [MenuItem("Tools/KominkaHillside/Menu/6a Create Sleep Programs")]
        public static void CreateProgramAssets()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_SleepSetup] Create program assets (台帳 / 枠 / 場所決め / ポーズ面)");
            CreateProgramAsset(BoardScriptPath, BoardProgramPath, log);
            CreateProgramAsset(SlotScriptPath, SlotProgramPath, log);
            CreateProgramAsset(PlacementScriptPath, PlacementProgramPath, log);
            CreateProgramAsset(CallPoseScriptPath, CallPoseProgramPath, log);
            log.AppendLine("次: force refresh (コンパイル完了まで待つ) してから Menu/6 Setup Sleep");
            Finish(LogFile, log);
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

        // ================================================================ 6

        /// <summary>裏山の KH_Dogs を引く (Setup / Verify 共通)。無ければ null。</summary>
        internal static GameObject DogsRoot(StringBuilder log)
        {
            var dogs = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out int count);
            if (dogs == null || count != 1 || dogs.transform.parent != null)
            {
                if (log != null) log.AppendLine("FAIL " + KD_Hillside.DogsRootName + " がルートに 1 個ない (" + count + ")");
                return null;
            }
            return dogs;
        }

        internal static global::KominkaHillside.KH_LeashManager Leashes(GameObject dogs)
        {
            var t = dogs == null ? null : dogs.transform.Find(KD_Hillside.LeashesName);
            return t == null ? null : t.GetComponent<global::KominkaHillside.KH_LeashManager>();
        }

        /// <summary>KH_LeashManager.sleepBoard (patch-016 で足す欄)。**欄がまだ無ければ null を返し、書かない**
        /// (司令塔の統合前でも Setup が通るように。Verify は欄が無いことを FAIL で出す)。</summary>
        internal static System.Reflection.FieldInfo LeashSleepBoardField()
        {
            return typeof(global::KominkaHillside.KH_LeashManager).GetField("sleepBoard");
        }

        [MenuItem("Tools/KominkaHillside/Menu/6 Setup Sleep")]
        public static void Setup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_SleepSetup] Setup (裏山 IMP-DOG-016)");
            bool ok = true;
            int found;
            if (EditorApplication.isPlaying || !KD_Hillside.InScene())
            {
                log.AppendLine("FAIL " + KD_Hillside.ScenePath + " を Edit モードで開くこと");
                Finish(LogFile, log);
                return;
            }
            foreach (var pa in new[] { BoardProgramPath, SlotProgramPath })
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(pa) == null)
                {
                    log.AppendLine("FAIL " + pa + " が無い。先に Menu/6a Create Sleep Programs → force refresh");
                    Finish(LogFile, log);
                    return;
                }

            // ---- 名簿と KH_Dogs (前提) ----
            var dogs = DogsRoot(log);
            var packGo = KD_SceneObjects.Find(KD_DogDefs.DogPackName, out found);
            var pack = (found == 1 && packGo != null)
                ? packGo.GetUdonSharpComponent<KD_DogPack>() : null;
            var manager = Leashes(dogs);
            if (dogs == null || pack == null || manager == null)
            {
                log.AppendLine("FAIL 前提が無い: KH_Dogs=" + (dogs != null) + " " + KD_DogDefs.DogPackName + "=" + (pack != null)
                    + " (" + found + " 個) KH_Leashes/KH_LeashManager=" + (manager != null)
                    + "。先に Tools/KominkaHillside/Walking/3 Build");
                Finish(LogFile, log);
                return;
            }

            // ---- 台帳 (KH_Dogs 直下) ----
            //
            // **Manual なので専用 GameObject** (U# は同一 GameObject で同期方式を混在できない)。
            // Owner は開放フラグを書くときだけ動く (マスター固定ではない — おやつ台帳と違い、権限者が切り替えるたびにその人へ移り、戻さない)
            var boardGo = KD_SceneObjects.Find(KD_DogDefs.SleepBoardName, out found);
            if (found > 1)
            {
                log.AppendLine("FAIL '" + KD_DogDefs.SleepBoardName + "' がシーン内に "
                    + found + " 個ある (手で解消する)");
                Finish(LogFile, log);
                return;
            }
            if (boardGo == null)
            {
                boardGo = new GameObject(KD_DogDefs.SleepBoardName);
                Undo.RegisterCreatedObjectUndo(boardGo, "Create " + KD_DogDefs.SleepBoardName);
                KD_SceneObjects.PlaceIfNew(boardGo, KD_Hillside.DogsRootName);   // 裏山改変: KH_Dogs 直下。位置に意味が無い
                log.AppendLine("created " + KD_Hillside.DogsRootName + "/" + KD_DogDefs.SleepBoardName);
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
                // **親を張るのは「今作った」ものだけ** (Setup は既存の親を触らない)
                if (slotGo.transform.parent == null)
                    Undo.SetTransformParent(slotGo.transform, boardGo.transform, true, "Place " + slotName);

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
                log.AppendLine("   wired " + slotName + ": dogIndex=" + i + " (" + defs[i].displayName + ")");
            }

            board.slots = slots;
            board.pack = pack;
            board.hillsideLeashes = manager;   // 裏山改変: 変更 1。リード接続中・集合中の犬を呼ぶ対象から外す
            ok &= TryCopy(board, log);
            ok &= EnsureManualSync(board, KD_DogDefs.SleepBoardName, log);
            log.AppendLine("   wired " + KD_DogDefs.SleepBoardName + ".slots (" + slots.Length + ") / .pack / .hillsideLeashes");

            // ---- 各犬の徘徊層へ台帳を配線 (IMP-DOG-019 Phase 2) ----
            // **犬は台帳を読むだけ** (LiveSleeperOf / SlotOf)。配線が欠けた犬は「呼んでも来ない犬」になる
            foreach (var def in KD_DogDefs.All)
            {
                var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out found);
                var wander = (found == 1 && wanderGo != null)
                    ? wanderGo.GetUdonSharpComponent<KD_DogWanderController>() : null;
                if (wander == null)
                {
                    log.AppendLine("   FAIL " + def.WanderSystemName + " が取れない (" + found + " 個) - この犬は呼んでも来ない");
                    ok = false;
                    continue;
                }
                wander.sleepBoard = board;
                ok &= TryCopy(wander, log);
                log.AppendLine("   wired " + def.WanderSystemName + ".sleepBoard");
            }

            // ---- 各犬の Fetch へ台帳を配線 (IMP-DOG-019 Phase 5) ----
            // **寝ている犬は投擲に反応しない**。⚠ Walking/3 Build は f.sleepBoard を null に戻していた (patch-016 で台帳を保つ形に直す)
            foreach (var def in KD_DogDefs.All)
            {
                var fetchGo = KD_SceneObjects.Find(def.FetchSystemName, out found);
                var fetch = (found == 1 && fetchGo != null)
                    ? fetchGo.GetUdonSharpComponent<KD_DogFetchController>() : null;
                if (fetch == null)
                {
                    log.AppendLine("   FAIL " + def.FetchSystemName + " が取れない (" + found + " 個) - この犬は寝ていても投擲に反応する");
                    ok = false;
                    continue;
                }
                fetch.sleepBoard = board;
                ok &= TryCopy(fetch, log);
                log.AppendLine("   wired " + def.FetchSystemName + ".sleepBoard");
            }

            // ---- 開催許可 (KD_LiveAccess。段階 1 で KH_Dogs 直下に作成済み・初期 ON) ----
            // 権限者の判定 (インスタンスオーナー、不在時 Master) は KD_SleepBoard.CanControl が正本。Menu/7 の MenuFinish も同じ配線をする
            var accessGo = KD_SceneObjects.Find("KD_LiveAccess", out found);
            var access = (found == 1 && accessGo != null) ? accessGo.GetUdonSharpComponent<KD_LiveAccess>() : null;
            if (access == null) { log.AppendLine("   FAIL KD_LiveAccess が取れない (" + found + " 個)。段階 1 の Menu/11 を先に"); ok = false; }
            else { access.sleepBoard = board; ok &= TryCopy(access, log); log.AppendLine("   wired KD_LiveAccess.sleepBoard"); }

            // ---- 手元メニュー (呼ぶタブ・おもちゃタブの除外)。段階 2 の Menu/7 も同じ配線をする (patch-016)。
            // 6i / 6j は pouch.sleepBoard を前提にするので、ここでも書く ----
            var pouchGo = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName, out found);
            var pouch = (found == 1 && pouchGo != null) ? pouchGo.GetUdonSharpComponent<KD_TreatPouch>() : null;
            if (pouch == null) { log.AppendLine("   FAIL " + KD_DogDefs.TreatPouchName + " が取れない (" + found + " 個)。先に Menu/7 Build Menu"); ok = false; }
            else { pouch.sleepBoard = board; ok &= TryCopy(pouch, log); log.AppendLine("   wired " + KD_DogDefs.TreatPouchName + ".sleepBoard"); }

            // ---- さんぽ (逆方向の排他: 呼ばれて待機中の犬はリードをつなげない。KH_LeashManager.sleepBoard は patch-016) ----
            var field = LeashSleepBoardField();
            if (field == null) log.AppendLine("   NOTE KH_LeashManager.sleepBoard がまだ無い (patch-016 の統合前)。統合後に Menu/6 を流し直す");
            else
            {
                Undo.RecordObject(manager, "Wire leash sleep board");
                field.SetValue(manager, board);
                ok &= TryCopy(manager, log);
                log.AppendLine("   wired KH_LeashManager.sleepBoard");
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("SETUP RESULT: " + (ok ? "PASS" : "FAIL"));
            log.AppendLine("**シーンは保存していない。** 次: Menu/6i Setup Sleep Placement → Menu/6j Setup Call Poses → Menu/6b Verify Sleep System");
            Finish(LogFile, log);
        }

        // ================================================================ 6b

        [MenuItem("Tools/KominkaHillside/Menu/6b Verify Sleep System")]
        public static void Verify()
        {
            var v = new KD_VerifyLog("[KD_SleepSetup] Verify (裏山 IMP-DOG-016: 台帳 / 枠 / 場所決め / ポーズ面 / 追加動作 / リード除外 / 斜面判定)");
            try { VerifyBoard(v); }
            catch (System.Exception e) { v.Check(false, "台帳の検査が例外で止まった: " + e.Message); }
            v.Info("---- 場所決め (KD_SleepPlacement) ----");
            try { KD_SleepPlacementEditor.VerifyInto(v); }
            catch (System.Exception e) { v.Check(false, "場所決めの検査が例外で止まった: " + e.Message); }
            v.Info("---- ポーズ面 (KD_CallPose) と追加動作 ----");
            try { KD_CallPoseEditor.VerifyInto(v); }
            catch (System.Exception e) { v.Check(false, "ポーズ面の検査が例外で止まった: " + e.Message); }
            v.Info("---- ポーズの Animator 再生 (Preview Scene) ----");
            try { KD_CallPoseEditor.VerifyPlaybackInto(v); }
            catch (System.Exception e) { v.Check(false, "Animator 再生の検査が例外で止まった: " + e.Message); }
            v.Finish(VerifyLogFile);
        }

        static void VerifyBoard(KD_VerifyLog v)
        {
            int found;
            v.Check(!EditorApplication.isPlaying && KD_Hillside.InScene(), KD_Hillside.ScenePath + " を Edit モードで開いている");

            // ---- Program Asset (2 本・どちらも Manual) ----
            var boardPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(BoardProgramPath);
            var slotPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(SlotProgramPath);
            v.Check(boardPa != null && boardPa.sourceCsScript != null, "program asset ok: KD_SleepBoard");
            v.Check(slotPa != null && slotPa.sourceCsScript != null, "program asset ok: KD_SleepSlot");
            v.Check(boardPa != null && boardPa.behaviourSyncMode == BehaviourSyncMode.Manual, "KD_SleepBoard の behaviourSyncMode == Manual (openToAll)");
            v.Check(slotPa != null && slotPa.behaviourSyncMode == BehaviourSyncMode.Manual, "KD_SleepSlot の behaviourSyncMode == Manual (予約 / 占有)");

            // ---- 名簿 / KH_Dogs / さんぽ ----
            var dogs = DogsRoot(null);
            v.Check(dogs != null, KD_Hillside.DogsRootName + " がルートに 1 個");
            var manager = Leashes(dogs);
            v.Check(manager != null, KD_Hillside.DogsRootName + "/" + KD_Hillside.LeashesName + " に KH_LeashManager");
            var packGo = KD_SceneObjects.Find(KD_DogDefs.DogPackName, out found);
            var pack = (found == 1 && packGo != null) ? packGo.GetUdonSharpComponent<KD_DogPack>() : null;
            v.Check(pack != null, KD_DogDefs.DogPackName + " が取れる (actual " + found + " 個)");

            // ---- 台帳 1 個 (KH_Dogs 直下) ----
            var boardGo = KD_SceneObjects.Find(KD_DogDefs.SleepBoardName, out found);
            v.Check(found == 1 && boardGo != null, KD_DogDefs.SleepBoardName + " がシーン内に 1 個 (actual " + found + ")");
            var board = boardGo != null ? boardGo.GetUdonSharpComponent<KD_SleepBoard>() : null;
            v.Check(board != null, KD_DogDefs.SleepBoardName + ": KD_SleepBoard が付いている");
            if (boardGo != null)
            {
                v.Check(boardGo.GetComponents<VRC.Udon.UdonBehaviour>().Length == 1, KD_DogDefs.SleepBoardName + " の UdonBehaviour は 1 個 (Manual なので同居不可)");
                v.Check(boardGo.GetComponent<VRCObjectSync>() == null, KD_DogDefs.SleepBoardName + " に VRCObjectSync が無い (Manual 同期と同居させない)");
                v.Check(dogs != null && boardGo.transform.parent == dogs.transform,
                    KD_DogDefs.SleepBoardName + " が " + KD_Hillside.DogsRootName + " の直下 (裏山改変: actual " + ParentPathOf(boardGo) + ")");
                v.Check(IsIdentity(boardGo.transform), KD_DogDefs.SleepBoardName + " の Transform が単位元 (枠 6 つの親でもある)");
                v.Check(SceneSyncOf(boardGo) == VRC.SDKBase.Networking.SyncType.Manual,
                    KD_DogDefs.SleepBoardName + " の UdonBehaviour.SyncMethod == Manual (actual " + SceneSyncOf(boardGo) + ")");
            }
            v.Check(KD_SceneObjects.Find(KD_DogDefs.WorldRootName) == null, "古民家の器 " + KD_DogDefs.WorldRootName + " を作っていない");

            // ---- 枠 6 つ ----
            var defs = KD_DogDefs.All;
            var slotBs = new KD_SleepSlot[defs.Length];
            var seen = new bool[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                string slotName = KD_DogDefs.SleepSlotNameOf(defs[i].key);
                string tag = slotName + ": ";
                var slotGo = KD_SceneObjects.Find(slotName, out found);
                if (!v.Check(found == 1 && slotGo != null, slotName + " がシーン内に 1 個 (actual " + found + ")")) continue;
                slotBs[i] = slotGo.GetUdonSharpComponent<KD_SleepSlot>();
                v.Check(slotGo.GetComponents<VRC.Udon.UdonBehaviour>().Length == 1, tag + "UdonBehaviour は 1 個 (Manual なので同居不可)");
                v.Check(slotGo.GetComponent<VRCObjectSync>() == null, tag + "VRCObjectSync が無い");
                v.Check(boardGo != null && slotGo.transform.parent == boardGo.transform, tag + "台帳の子 (actual " + ParentPathOf(slotGo) + ")");
                v.Check(IsIdentity(slotGo.transform), tag + "Transform が単位元 (位置に意味が無い)");
                v.Check(SceneSyncOf(slotGo) == VRC.SDKBase.Networking.SyncType.Manual, tag + "UdonBehaviour.SyncMethod == Manual (actual " + SceneSyncOf(slotGo) + ")");
                if (!v.Check(slotBs[i] != null, tag + "KD_SleepSlot が付いている")) continue;
                if (v.Check(slotBs[i].dogIndex == i, tag + "dogIndex == " + i + " (actual " + slotBs[i].dogIndex + ")")
                    && slotBs[i].dogIndex >= 0 && slotBs[i].dogIndex < seen.Length)
                {
                    v.Check(!seen[slotBs[i].dogIndex], tag + "dogIndex が重複していない");
                    seen[slotBs[i].dogIndex] = true;
                }
                v.Check(slotBs[i].pack != null && slotBs[i].pack == pack, tag + "pack == " + KD_DogDefs.DogPackName);
                v.Check(slotBs[i].board != null && slotBs[i].board == board, tag + "board == " + KD_DogDefs.SleepBoardName);
            }
            int extra = 0;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("KD_SleepSlot_")) extra++;
            v.Check(extra == defs.Length, "KD_SleepSlot_* がちょうど " + defs.Length + " 個 (actual " + extra + ")");

            // ---- 台帳の配線 ----
            if (board != null)
            {
                bool slotsOk = board.slots != null && board.slots.Length == defs.Length;
                for (int i = 0; slotsOk && i < defs.Length; i++)
                    if (board.slots[i] == null || board.slots[i] != slotBs[i]) slotsOk = false;
                v.Check(slotsOk, KD_DogDefs.SleepBoardName + ".slots が " + defs.Length + " 本で添字どおり");
                v.Check(board.pack != null && board.pack == pack, KD_DogDefs.SleepBoardName + ".pack 配線");
                v.Check(pack != null && pack.dogRoots != null && pack.dogRoots.Length == defs.Length,
                    KD_DogDefs.SleepBoardName + ".pack の名簿が犬 " + defs.Length + " 匹 (actual "
                    + (pack == null || pack.dogRoots == null ? -1 : pack.dogRoots.Length) + ")");
                // 2026-09-22 ユーザー決定: 開放は初期 ON (openToAll は private の初期化子 = heap 既定)
                v.Check(board.IsOpenToAll, "「呼ぶを全員に許可」の既定が ON (openToAll = true)");
                // 裏山改変: 変更 1。リード除外の参照 (proxy と backing Udon の両方)
                v.Check(board.hillsideLeashes != null && board.hillsideLeashes == manager,
                    KD_DogDefs.SleepBoardName + ".hillsideLeashes == KH_LeashManager (リード接続中・集合中の犬を呼ぶ対象から外す)");
                object wired;
                v.Check(manager != null && UdonSharpEditorUtility.GetBackingUdonBehaviour(board).publicVariables.TryGetVariableValue("hillsideLeashes", out wired)
                    && wired as Object == UdonSharpEditorUtility.GetBackingUdonBehaviour(manager), KD_DogDefs.SleepBoardName + " の backing Udon が KH_LeashManager を持つ");
            }

            // ---- 逆方向の排他 (呼ばれて待機中の犬はリードをつなげない。patch-016 の KH_LeashManager.sleepBoard) ----
            var field = LeashSleepBoardField();
            if (v.Check(field != null, "KH_LeashManager.sleepBoard がある (patch-016 の統合。無いと呼ばれて待機中の犬にリードがつながる)") && manager != null)
                v.Check(field.GetValue(manager) as Object == board, "KH_LeashManager.sleepBoard == " + KD_DogDefs.SleepBoardName);

            // ---- 各犬の徘徊層・Fetch ----
            foreach (var def in KD_DogDefs.All)
            {
                var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out found);
                var wander = (found == 1 && wanderGo != null) ? wanderGo.GetUdonSharpComponent<KD_DogWanderController>() : null;
                if (v.Check(wander != null, def.WanderSystemName + " が取れる (actual " + found + " 個)"))
                {
                    v.Check(wander.sleepBoard != null && wander.sleepBoard == board, def.WanderSystemName + ".sleepBoard == " + KD_DogDefs.SleepBoardName + " (犬はここから占有者を読む)");
                    // 裏山改変: 変更 3。地形追従の足 (SleepStep → GatherStep) は hillsideLeashes が居るときだけ効く
                    v.Check(wander.hillsideLeashes != null && wander.hillsideLeashes == manager, def.WanderSystemName + ".hillsideLeashes == KH_LeashManager (呼ばれた犬の移動が散歩と同じ地形追従)");
                    v.Check((wander.groundLayerMask.value & KD_SleepSlot.HillsideGroundMask) == KD_SleepSlot.HillsideGroundMask,
                        def.WanderSystemName + ".groundLayerMask が地面の層 (0 / 11) を含む (actual " + wander.groundLayerMask.value + ")");
                }
                var fetchGo = KD_SceneObjects.Find(def.FetchSystemName, out found);
                var fetch = (found == 1 && fetchGo != null) ? fetchGo.GetUdonSharpComponent<KD_DogFetchController>() : null;
                if (v.Check(fetch != null, def.FetchSystemName + " が取れる (actual " + found + " 個)"))
                    v.Check(fetch.sleepBoard != null && fetch.sleepBoard == board, def.FetchSystemName + ".sleepBoard == " + KD_DogDefs.SleepBoardName
                        + " (**寝ている犬は投擲に反応しない**。Walking/3 Build が null に戻していないか = patch-016)");
            }

            // ---- 開催許可・手元メニュー ----
            var accessGo = KD_SceneObjects.Find("KD_LiveAccess", out found);
            var access = (found == 1 && accessGo != null) ? accessGo.GetUdonSharpComponent<KD_LiveAccess>() : null;
            if (v.Check(access != null, "KD_LiveAccess が取れる (actual " + found + " 個)"))
            {
                v.Check(access.sleepBoard == board, "KD_LiveAccess.sleepBoard == " + KD_DogDefs.SleepBoardName + " (権限者 = インスタンスオーナー、不在時 Master)");
                v.Check(access.IsOpenToAll, "開催許可の既定が ON");
            }
            var pouchGo = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName, out found);
            var pouch = (found == 1 && pouchGo != null) ? pouchGo.GetUdonSharpComponent<KD_TreatPouch>() : null;
            if (v.Check(pouch != null, KD_DogDefs.TreatPouchName + " が取れる (actual " + found + " 個)"))
            {
                v.Check(pouch.sleepBoard != null && pouch.sleepBoard == board, KD_DogDefs.TreatPouchName + ".sleepBoard == " + KD_DogDefs.SleepBoardName + " (呼ぶタブ + おもちゃタブの除外)");
                v.Check(pouch.sleepLabels != null && pouch.sleepLabels.Length == defs.Length && pouch.sleepButtons != null && pouch.sleepButtons.Length == defs.Length,
                    "呼ぶタブの行 SleepRow0..5 (0-b で生成済み)");
            }

            // ---- 置き場所の正本: 裏山は LayoutTable (古民家の KD_Systems 配下の表) を使わない ----
            v.Note("LayoutTable (古民家の " + KD_DogDefs.SystemsPath + " 配下) の行数検査は裏山では対象外 (器は KH_Dogs 直下。上で親を数えた)");

            // ---- 文言 (裏山のキー。keys-016.json → UITranslations は司令塔が統合) ----
            var catalog = KD_LocalizationEditor.Load();
            foreach (var key in new[] { "KD_TreatPouch.WordSleepLeashed", "KD_TreatPouch.WordSleepFrisbee", "KD_TreatPouch.WordSleepNeedFlat", "KD_TreatPouch.WordWalkStateCalled", "KD_TreatPouch.WordWalkGateCalled" })
            {
                string ja = null;
                try { ja = KD_LocalizationEditor.Text(catalog, "ja", key); } catch (System.InvalidOperationException) { }
                v.Check(!string.IsNullOrEmpty(ja), "文言キー " + key + " が UITranslations にある (ja=" + (ja ?? "無し") + ")");
            }

            // ---- 斜面判定 (変更 2) の定数と、代表地点の実測 ----
            float deg = Mathf.Acos(Mathf.Clamp01(KD_SleepSlot.HillsideMinGroundNormal)) * Mathf.Rad2Deg;
            v.Info("   斜面判定: HillsideMinGroundNormal=" + KD_SleepSlot.HillsideMinGroundNormal.ToString("F2") + " (約 " + deg.ToString("F1") + "°まで)"
                + " / 地面の層 mask=" + KD_SleepSlot.HillsideGroundMask + " (0 / 11) / probe=" + KD_SleepSlot.HillsideGroundProbe + " m / 高さの許容=" + KD_SleepSlot.HillsideGroundTolerance + " m");
            var group = manager == null ? null : manager.activities;
            if (group != null) v.Info("   参考: 集合の配置判定 KD_GroupActivities.minGroundNormal=" + group.minGroundNormal.ToString("F2") + " (呼ぶの方を厳しくする。IMP-DOG-016)");
            v.Check(KD_SleepSlot.HillsideMinGroundNormal > 0.5f && KD_SleepSlot.HillsideMinGroundNormal < 1f, "法線の下限が (0.5, 1) の範囲 (仮値 0.9)");
            if (dogs != null)
            {
                foreach (var name in new[] { "KH_LeashRack", "KH_Station_Plaza", "KH_Station_Viewpoint" })
                {
                    var t = dogs.transform.Find(name);
                    if (t == null) { v.Note(name + " が無い (実測を飛ばす)"); continue; }
                    int total, ok;
                    SampleGround(t.position, 3f, out total, out ok);
                    v.Info("   実測 " + name + " 半径 3 m: 置ける " + ok + " / " + total + " 点 (0.5 m 格子)");
                    v.Check(total > 0 && ok * 10 >= total * 9, name + " のまわり (半径 3 m) の 9 割以上に置ける (拠点で呼べないことが無い)");
                }
            }
        }

        /// <summary>半径 <paramref name="radius"/> の 0.5 m 格子で、法線が下限以上の点を数える (Verify の実測。定数は KD_SleepSlot の 1 か所)。</summary>
        internal static void SampleGround(Vector3 center, float radius, out int total, out int ok)
        {
            total = 0; ok = 0;
            int n = Mathf.CeilToInt(radius / 0.5f);
            for (int x = -n; x <= n; x++)
                for (int z = -n; z <= n; z++)
                {
                    if (x * x + z * z > n * n) continue;
                    Vector3 p = center + new Vector3(x * 0.5f, 0f, z * 0.5f);
                    RaycastHit hit;
                    if (!Physics.Raycast(p + Vector3.up * 10f, Vector3.down, out hit, 30f, KD_SleepSlot.HillsideGroundMask, QueryTriggerInteraction.Ignore)) continue;
                    total++;
                    if (hit.normal.y >= KD_SleepSlot.HillsideMinGroundNormal) ok++;
                }
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

        static void Finish(string file, StringBuilder log)
        {
            KD_VerifyLog.Write(file, log);
        }
    }
}
