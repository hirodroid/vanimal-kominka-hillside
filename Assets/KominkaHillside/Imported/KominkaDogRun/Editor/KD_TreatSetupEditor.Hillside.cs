using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace KominkaDogRun.Editor
{
    /// <summary>裏山 (KominkaHillside) の定数。**シーンのガードと器の置き場所の正本はここ 1 か所** (段階 0-b)。
    /// 古民家の Editor が持っていたシーンの判定 (古民家のシーンパス) と "====KominkaDogRun====" はここを参照する。</summary>
    public static class KD_Hillside
    {
        /// <summary>作業対象シーン (CLAUDE.md。改名するときはここだけ直す)。</summary>
        public const string ScenePath = "Assets/Scenes/VRCDefaultWorldScene.unity";
        /// <summary>裏山の犬まわりの器。古民家の "====KominkaDogRun====/KD_Systems" は作らない。</summary>
        public const string DogsRootName = "KH_Dogs";
        public const string LeashesName = "KH_Leashes";
        public const string ToyRecallName = "KH_ToyRecall";
        public const string LegacyHandMenuName = "KH_HandMenu";
        /// <summary>拠点の「メニューをひらく」板の親 (KH_Dogs 直下)。子の名前は MenuPlate。</summary>
        public static readonly string[] OpenerStations = { "KH_LeashRack", "KH_Station_Viewpoint", "KH_Station_Plaza" }; // 2026-09-26 夜: 広場の縁の拠点を復活 (DES-DOG-003)。KH_DogWalkingEditor.StationNames と同じ並び
        public const string OpenerPlateName = "MenuPlate";
        /// <summary>段階 2 (IMP-DOG-014 おやつ / 015 かざり・撫でFX・ミニチュア / 016 呼ぶ・ポーズ・場所決め)。
        /// 1 = IMP-PRG-001 (台帳・カタログ・残高・ポイントの面・ようこそ最小版)、0 = 段階 0-b。
        /// <c>KD_TreatPouch.hillsideStage</c> へ Setup が毎回書く値 (計画書 D-1)。</summary>
        public const int Stage = 2;
        /// <summary>段階 1 の台帳と商品の器 (KH_Dogs 直下。P1 の Menu/3a・4 が作る)。</summary>
        public const string ProgressSystemName = "KD_ProgressSystem";
        public const string ShopCatalogName = "KD_ShopCatalog";
        /// <summary>段階 1 の ポイントの面 (Page_Settings の下。ギフトコードの器 GiftCodePage の代わり)。</summary>
        public const string PointsPageName = "PointsPage";
        /// <summary>おじいさんの支度ポイントの額 (Codex M-2: 200 を二重に持たない)。正本は P1 の KD_ProgressLedger.HillsideKeeperPoints。</summary>
        // TODO(P1 の定数が入ったら): = KD_ProgressLedger.HillsideKeeperPoints; に差し替える (それまでの仮の値)
        public const int KeeperPoints = KD_ProgressLedger.HillsideKeeperPoints; // 正本は台帳 (Codex M-2、司令塔 2026-09-24 差替)
        public const string LogDir = "Temp/menu-rebuild/";

        public static bool InScene()
        {
            return EditorSceneManager.GetActiveScene().path == ScenePath;
        }

        public static void Guard()
        {
            if (EditorApplication.isPlaying || !InScene())
                throw new System.InvalidOperationException("Open " + ScenePath + " in Edit mode");
        }

        /// <summary>文言表の ja を引く。**裏山キーは R (runtime) が UITranslations.json へ足す**ので、
        /// 未登録の間は <paramref name="fallback"/> を返し、log へ WARN を残す (黙って別の字を焼かない)。</summary>
        public static string Text(string key, string fallback, StringBuilder log = null)
        {
            try { return KD_LocalizationEditor.Text(KD_LocalizationEditor.Load(), "ja", key); }
            catch (System.InvalidOperationException)
            {
                if (log != null) log.AppendLine("   WARN 文言キー未登録: " + key + " (仮に \"" + fallback + "\")");
                return fallback;
            }
        }
    }

    /// <summary>段階 0-b (IMP-UI-001): 裏山の手元メニュー (KD_TreatPouch) を KH_Dogs 直下に作る入口。
    /// 古民家の <see cref="Setup"/> (おやつの器一式を要求する) は Imported-Unused へ移した。
    ///
    /// 流れ: KD_DogPack 必須 → pouch を KH_Dogs/KD_TreatPouch に作る → BuildPouchPanel
    /// (中で Scroll.Build / Menu Finish の再構築 / Collision を通す。さんぽ・ディスク・かざりの FxRow は
    /// <see cref="HillsideBuildPages"/> が Scroll.Build の直前に作る) → manager / recall / hillsideStage →
    /// 拠点板 3 枚 → CopyProxyToUdon → MarkSceneDirty。**保存しない。**
    ///
    /// 段階 0 の参照 (box / ledger / items / balls / toyFetchers / catalog / petFxBox / fxItems / sleepBoard / live)
    /// は**探さず、触らない** (新規なら null のまま。段階 1・2 のビルダーが配線する)。
    /// 段階 1 は ledger / catalog / items / fxItems、段階 2 は box (+ 枠) / accessories (+ 一覧) / petFxBox / sleepBoard / sleepPlacement / callPose を
    /// 探して配線する (<see cref="HillsideFindStage1"/> / <see cref="HillsideFindStage2"/>。**1 つでも無ければ何も書かずに FAIL**)。live は探さない。</summary>
    public static partial class KD_TreatSetupEditor
    {
        const string HillsideBuildLog = KD_Hillside.LogDir + "build-menu.txt";
        const string HillsideFontLog = KD_Hillside.LogDir + "rebake-font.txt";
        /// <summary>さんぽの行 = 犬 6 + 行き先 2 (広場 / 古民家側の入口)。</summary>
        public const int WalkDogRows = 6, WalkRowCount = 8;
        /// <summary>撫でFX の行 (_Fx0.._Fx10)。かざりの Content に AccRow の後ろへ並べる (D-2)。</summary>
        public const int HillsideFxRowCount = 11;
        const string WalkRowPrefix = "WalkRow";
        const string DiscButtonName = "Disc";
        /// <summary>撫でFX の群見出しのセル (FxHead0 = 見出し / FxHead1 = 右の空き)。かざりの Content で AccRow の後ろ・FxRow の前 (D-2)。</summary>
        internal const string FxHeadPrefix = "FxHead";

        [MenuItem("Tools/KominkaHillside/Menu/7 Build Menu")]
        public static void SetupHillside()
        {
            KominkaHillside.Editor.KH_ProductionScene.RejectLegacyRebuild();
            var log = new StringBuilder();
            log.AppendLine("[KD_TreatSetup] Setup (裏山 段階 " + KD_Hillside.Stage + ")");
            bool ok = true;
            if (EditorApplication.isPlaying || !KD_Hillside.InScene())
            {
                log.AppendLine("FAIL " + KD_Hillside.ScenePath + " を Edit モードで開くこと");
                KD_VerifyLog.Write(HillsideBuildLog, log);
                return;
            }

            // ---- 名簿 (前提) ----
            var packGo = KD_SceneObjects.Find(PackName, out int packCount);
            var pack = (packCount == 1 && packGo != null) ? packGo.GetUdonSharpComponent<KD_DogPack>() : null;
            if (pack == null)
            {
                log.AppendLine("FAIL " + PackName + " が " + packCount + " 個。先に Tools/KominkaHillside/Walking/3 Build");
                KD_VerifyLog.Write(HillsideBuildLog, log);
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(PouchProgramPath) == null)
            {
                log.AppendLine("FAIL " + PouchProgramPath + " が無い。先に Tools/KominkaHillside/Menu/5 Create Pouch Programs → force refresh");
                KD_VerifyLog.Write(HillsideBuildLog, log);
                return;
            }

            // ---- 叩く先 (段階 0 から必須) ----
            var dogs = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out int dogsCount);
            if (dogs == null || dogsCount != 1 || dogs.transform.parent != null)
            {
                log.AppendLine("FAIL " + KD_Hillside.DogsRootName + " がルートに 1 個ない (" + dogsCount + ")");
                KD_VerifyLog.Write(HillsideBuildLog, log);
                return;
            }
            var leashT = dogs.transform.Find(KD_Hillside.LeashesName);
            var manager = leashT == null ? null : leashT.GetComponent<global::KominkaHillside.KH_LeashManager>();
            var recallT = dogs.transform.Find(KD_Hillside.ToyRecallName);
            var recall = recallT == null ? null : recallT.GetComponent<global::KominkaHillside.KH_ToyRecall>();
            if (manager == null) { log.AppendLine("FAIL KH_Dogs/KH_Leashes に KH_LeashManager が無い (さんぽの叩く先)"); ok = false; }
            if (recall == null) { log.AppendLine("FAIL KH_Dogs/KH_ToyRecall に KH_ToyRecall が無い (おもちゃの叩く先)"); ok = false; }

            // ---- 段階 1 (IMP-PRG-001): 台帳・カタログ・商品。**無ければ何も書かずに FAIL** (P1 の Menu/3a Setup Progress・4 Setup Shop が先) ----
            KD_ProgressLedger stageLedger = null; KD_ShopCatalog stageCatalog = null;
            KD_ShopItem[] stageItems = null, stageFxItems = null;
            if (KD_Hillside.Stage >= 1 && !HillsideFindStage1(dogs.transform, log, out stageLedger, out stageCatalog, out stageItems, out stageFxItems))
            {
                log.AppendLine("SETUP RESULT: FAIL (段階 1 の前提が無い。先に Tools/KominkaHillside/Menu/3a Setup Progress → 4 Setup Shop。pouch は触っていない)");
                KD_VerifyLog.Write(HillsideBuildLog, log);
                return;
            }

            // ---- 段階 2 (IMP-DOG-014 / 015 / 016): おやつの箱・かざり・撫でFX・呼ぶ。**1 つでも無ければ何も書かずに FAIL**
            // (Menu/6c Setup Treats・6e Setup Accessories・6g Setup Pet Fx・6 Setup Sleep → 6i → 6j が先) ----
            KD_TreatBox stageBox = null; KD_AccessoryBox stageAccessories = null; KD_PetFxBox stagePetFxBox = null;
            KD_SleepBoard stageSleepBoard = null; KD_SleepPlacement stageSleepPlacement = null; KD_CallPose stageCallPose = null;
            if (KD_Hillside.Stage >= 2 && !HillsideFindStage2(dogs.transform, log, out stageBox, out stageAccessories, out stagePetFxBox,
                out stageSleepBoard, out stageSleepPlacement, out stageCallPose))
            {
                log.AppendLine("SETUP RESULT: FAIL (段階 2 の前提が無い。上の FAIL の Setup を先に。pouch は触っていない)");
                KD_VerifyLog.Write(HillsideBuildLog, log);
                return;
            }

            // ---- 文言の束縛先 (Codex D-1 2026-09-24): 板の再構築の中の Bind (Menu Finish の RebuildIfPresent) が
            // Bindings の束縛先 (例 KD_ShopKeeperMenu) を要求する。途中で例外になると板だけ作り直した途中状態が残るので、**何も変える前に**確かめる ----
            if (!HillsideBindTargetsReady(log))
            {
                log.AppendLine("SETUP RESULT: FAIL (文言の束縛先が足りない。上の手順を先に。pouch は触っていない)");
                KD_VerifyLog.Write(HillsideBuildLog, log);
                return;
            }

            // ---- pouch (KH_Dogs 直下。**位置に意味は無い**。既存なら親も位置も触らない) ----
            var go = KD_SceneObjects.Find(PouchName, out int found);
            if (found > 1)
            {
                log.AppendLine("FAIL '" + PouchName + "' がシーン内に " + found + " 個ある");
                KD_VerifyLog.Write(HillsideBuildLog, log);
                return;
            }
            if (go == null)
            {
                go = new GameObject(PouchName);
                Undo.RegisterCreatedObjectUndo(go, "Create " + PouchName);
                KD_SceneObjects.PlaceIfNew(go, KD_Hillside.DogsRootName);
                log.AppendLine("created " + KD_Hillside.DogsRootName + "/" + PouchName);
            }
            go.layer = 0;
            RemovePouchBody(go, log);
            var pouch = go.GetUdonSharpComponent<KD_TreatPouch>();
            if (pouch == null)
            {
                try { pouch = go.AddUdonSharpComponent<KD_TreatPouch>(); }
                catch (System.Exception e)
                {
                    log.AppendLine("FAIL KD_TreatPouch: " + e.Message + " — Menu/5 の後に force refresh してから再実行");
                    KD_VerifyLog.Write(HillsideBuildLog, log);
                    return;
                }
                log.AppendLine("   added KD_TreatPouch");
            }
            KD_ShopSetupEditor.SetSync(pouch, VRC.SDKBase.Networking.SyncType.None, log);

            // ---- 板 (items / fxItems は段階 0 では無い。BuildPouchPanel が器の行数へ読み替える) ----
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            if (font == null) { log.AppendLine("   FAIL font asset が無い (" + KD_ShopSetupEditor.SharedFontPath + ")"); ok = false; }
            // 段階 2 (IMP-DOG-016): BuildPouchPanel の途中で KD_SleepPlacementEditor / KD_CallPoseEditor の RebuildIfPresent が pouch.sleepBoard を読むので、先に渡す
            if (KD_Hillside.Stage >= 2) pouch.sleepBoard = stageSleepBoard;
            BuildPouchPanel(go, pouch, KD_Hillside.Stage >= 1 ? stageItems : pouch.items, KD_Hillside.Stage >= 1 ? stageFxItems : pouch.fxItems, font, log);
            if (KD_Hillside.Stage >= 1)
            {
                // 段階 1: 台帳とカタログを配線 (live は探さない。box / accessories / petFxBox / sleepBoard は段階 2)
                pouch.ledger = stageLedger;
                pouch.catalog = stageCatalog;
                pouch.items = stageItems;
                pouch.fxItems = stageFxItems;
                // 裏山には棚が無い (items[0].shelf は常に null) ので、購入の効果音は P1 の音源 (古民家と同じ Data/SE) を直接配線する
                pouch.uiSuccessClip = KD_ShopSetupEditor.HillsideBuyClip();
                pouch.uiRejectClip = KD_ShopSetupEditor.HillsideRejectClip();
                if (pouch.uiSuccessClip == null || pouch.uiRejectClip == null)
                    log.AppendLine("   WARN 購入の効果音が取れない (success=" + (pouch.uiSuccessClip != null) + " reject=" + (pouch.uiRejectClip != null) + ")。P1 の Data/SE を確認");
            }
            if (KD_Hillside.Stage >= 2)
            {
                // 段階 2 (IMP-DOG-014): おやつの箱と枠。枠 → pouch は「移譲が済んだ」の知らせ先 (6c も書くが、7 を後に流しても揃うようにここでも書く)
                pouch.box = stageBox;
                foreach (var s in stageBox.slots)
                {
                    s.pouch = pouch;
                    ok &= TryCopy(s, log);
                }
                // 段階 2 (IMP-DOG-015): かざりの台帳 + 一覧 (accessories / accProductIds / accItems / accNameKeys ほか) と撫でFX。6e・6g も同じ値を書く (べき等)。
                // 試着のミニチュア (accPreviewDogs / accPreviewItems) は Menu/18 Build Dog Previews が書く。確認カードの付け替えは BuildPouchPanel の中の KD_DogPreviewEditor.RewireMenu
                if (!KD_AccessorySetsEditor.HillsideWire(stageAccessories, pouch, log)) ok = false;
                ok &= TryCopy(stageAccessories, log);
                pouch.petFxBox = stagePetFxBox;
                // 段階 2 (IMP-DOG-016): 呼ぶタブ (犬 → ポーズ → 場所)。Rebuild でも同じ値が入るが、正本はここ
                pouch.sleepBoard = stageSleepBoard;
                pouch.sleepPlacement = stageSleepPlacement;
                pouch.callPose = stageCallPose;
                log.AppendLine("   段階 2: box = " + KD_Hillside.DogsRootName + "/" + BoxName + " (枠 " + stageBox.slots.Length + " 本の pouch を配線) / accessories="
                    + (pouch.accessories != null) + " petFxBox=" + (pouch.petFxBox != null) + " / sleepBoard / sleepPlacement / callPose を配線"
                    + " / 試着ミニチュア=" + (pouch.accPreviewDogs != null ? pouch.accPreviewDogs.Length : 0) + " 匹 (無ければ Menu/18)");
            }

            // ---- 設計値 (**コードの既定値はシーンに効かない**ので毎回書く。古民家値 D-6) ----
            pouch.panelDistance = 0.95f;
            pouch.recallDistance = 1.2f;
            pouch.recallCooldown = 2.5f;
            pouch.openStickSign = -1;
            pouch.openStickThreshold = 0.8f;
            pouch.openHoldSeconds = 0.5f;
            pouch.hillsideStage = KD_Hillside.Stage;
            pouch.manager = manager;
            pouch.recall = recall;
            log.AppendLine("   hillsideStage=" + KD_Hillside.Stage + " manager=" + (manager != null) + " recall=" + (recall != null)
                + " activitiesMenu=" + (pouch.activitiesMenu != null));
            log.AppendLine("   段階 " + KD_Hillside.Stage + ": " + (KD_Hillside.Stage >= 1 ? "ledger / catalog / items / fxItems を配線。" : "ledger / catalog / items / fxItems は探さない。")
                + (KD_Hillside.Stage >= 2 ? "box / accessories / petFxBox / sleepBoard / sleepPlacement / callPose を配線。balls / toyFetchers / live は探さない"
                    : "box / balls / toyFetchers / petFxBox / sleepBoard / live は探さない")
                + " (現在 box=" + (pouch.box != null) + " ledger=" + (pouch.ledger != null) + " catalog=" + (pouch.catalog != null)
                + " items=" + CountSet(pouch.items) + " fxItems=" + CountSet(pouch.fxItems)
                + " accessories=" + (pouch.accessories != null) + " petFxBox=" + (pouch.petFxBox != null)
                + " sleepBoard=" + (pouch.sleepBoard != null) + " sleepPlacement=" + (pouch.sleepPlacement != null) + " callPose=" + (pouch.callPose != null)
                + " live=" + (pouch.live != null) + ")");
            ok &= TryCopy(pouch, log);

            // ---- 拠点板 3 枚 → pouch._Toggle (配線だけ。位置・Interact 文言・近さは触らない) ----
            ok &= WireOpeners(dogs.transform, pouch, log);

            // ---- 文言をフォントへ焼く (PouchWords + HillsideWords。Static なので焼かないと実機で豆腐) ----
            RestoreFontSource(log);
            KD_ShopSetupEditor.AddGlyphs(PouchWords() + HillsideWords(), log);
            string missing = KD_ShopSetupEditor.MissingGlyphs(PouchWords() + HillsideWords());
            if (missing.Length > 0)
            {
                log.AppendLine("   FAIL 焼けていない字 " + missing.Length + " 字 — 先に Menu/2b Rebake Font 48pt");
                ok = false;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("SETUP RESULT: " + (ok ? "PASS" : "FAIL"));
            log.AppendLine("**シーンは保存していない。** 次: Menu/8・9 (あそぶ) → Menu/11 → Menu/11b Build Welcome → Menu/20・21・22・11c の Verify");
            KD_VerifyLog.Write(HillsideBuildLog, log);
        }

        /// <summary>拠点の「メニューをひらく」板 3 枚 (KH_MenuOpener) を pouch へ向ける。**配線だけ** —
        /// 板の位置・当たり・Interact の文言と近さは Walking 側の持ち物なので触らない。</summary>
        static bool WireOpeners(Transform dogs, KD_TreatPouch pouch, StringBuilder log)
        {
            int wired = 0;
            foreach (string st in KD_Hillside.OpenerStations)
            {
                var plate = dogs.Find(st + "/" + KD_Hillside.OpenerPlateName);
                var opener = plate == null ? null : plate.GetComponent<global::KominkaHillside.KH_MenuOpener>();
                if (opener == null) { log.AppendLine("   FAIL " + KD_Hillside.DogsRootName + "/" + st + "/" + KD_Hillside.OpenerPlateName + " に KH_MenuOpener が無い"); continue; }
                Undo.RecordObject(opener, "Wire menu opener");
                opener.pouch = pouch;
                if (TryCopy(opener, log)) wired++;
            }
            log.AppendLine("   拠点板: " + wired + " / " + KD_Hillside.OpenerStations.Length + " 枚を pouch._Toggle へ");
            return wired == KD_Hillside.OpenerStations.Length;
        }

        /// <summary>段階 1 の参照を探す。ledger = KH_Dogs/KD_ProgressSystem、catalog = KH_Dogs/KD_ShopCatalog、
        /// items = おやつ 4 (<see cref="KD_ShopSetupEditor.TreatItemNameAt"/>)、fxItems = 撫でFX 11 (<see cref="KD_ShopSetupEditor.PetFxItemNameAt"/>。
        /// 無い分は WARN で null)。ledger / catalog / おやつが 1 つでも無ければ false (呼び手は何も書かずに止める)。</summary>
        static bool HillsideFindStage1(Transform dogs, StringBuilder log, out KD_ProgressLedger ledger, out KD_ShopCatalog catalog,
            out KD_ShopItem[] items, out KD_ShopItem[] fxItems)
        {
            bool ok = true;
            var ledgerT = dogs.Find(KD_Hillside.ProgressSystemName);
            ledger = ledgerT == null ? null : ledgerT.GetComponent<KD_ProgressLedger>();
            KD_SceneObjects.Find(KD_Hillside.ProgressSystemName, out int ledgerCount);
            if (ledger == null || ledgerCount != 1)
            { log.AppendLine("FAIL " + KD_Hillside.DogsRootName + "/" + KD_Hillside.ProgressSystemName + " に KD_ProgressLedger が 1 個ない (シーン内 " + ledgerCount + ")。先に Menu/3a Setup Progress"); ok = false; ledger = null; }
            var catalogT = dogs.Find(KD_Hillside.ShopCatalogName);
            catalog = catalogT == null ? null : catalogT.GetComponent<KD_ShopCatalog>();
            KD_SceneObjects.Find(KD_Hillside.ShopCatalogName, out int catalogCount);
            if (catalog == null || catalogCount != 1)
            { log.AppendLine("FAIL " + KD_Hillside.DogsRootName + "/" + KD_Hillside.ShopCatalogName + " に KD_ShopCatalog が 1 個ない (シーン内 " + catalogCount + ")。先に Menu/4 Setup Shop"); ok = false; catalog = null; }

            int n = KD_ShopSetupEditor.TreatProductCount;
            items = new KD_ShopItem[n];
            for (int i = 0; i < n; i++)
            {
                string name = KD_ShopSetupEditor.TreatItemNameAt(i);
                var itemGo = KD_SceneObjects.Find(name, out int count);
                items[i] = (count == 1 && itemGo != null) ? itemGo.GetComponent<KD_ShopItem>() : null;
                if (items[i] == null) { log.AppendLine("FAIL おやつの商品 " + name + " が取れない (" + count + " 個)。先に Menu/4 Setup Shop"); ok = false; }
            }
            int fxN = KD_ShopSetupEditor.PetFxProductCount;
            fxItems = new KD_ShopItem[fxN];
            int fxFound = 0;
            for (int i = 0; i < fxN; i++)
            {
                string name = KD_ShopSetupEditor.PetFxItemNameAt(i);
                var fxGo = KD_SceneObjects.Find(name, out int count);
                fxItems[i] = (count == 1 && fxGo != null) ? fxGo.GetComponent<KD_ShopItem>() : null;
                if (fxItems[i] == null) log.AppendLine("   WARN 撫でFX の商品 " + name + " が取れない (" + count + " 個)。null のまま (描画・購入は段階 2 IMP-DOG-015)");
                else fxFound++;
            }
            log.AppendLine("   段階 1: ledger=" + (ledger != null) + " catalog=" + (catalog != null) + " items=" + CountSet(items) + "/" + n + " fxItems=" + fxFound + "/" + fxN);
            return ok;
        }

        /// <summary>段階 2 の参照を探す (計画書 D-1 の段階表。探索と FAIL 判定はここ 1 か所)。
        /// box = KH_Dogs/KD_TreatBox (枠 3 本。Menu/6c)、accessories = KH_Dogs/KD_AccessorySystem (Menu/6e)、petFxBox = KH_Dogs/KD_PetFxBox (Menu/6g)、
        /// sleepBoard = KH_Dogs/KD_SleepBoard (Menu/6)、sleepPlacement = KH_Dogs/KD_SleepPlacement (Menu/6i)、callPose = KH_Dogs/KD_CallPose (Menu/6j)。
        /// どれか 1 つでも無ければ false (呼び手は何も書かずに止める)。</summary>
        static bool HillsideFindStage2(Transform dogs, StringBuilder log, out KD_TreatBox box, out KD_AccessoryBox accessories, out KD_PetFxBox petFxBox,
            out KD_SleepBoard sleepBoard, out KD_SleepPlacement sleepPlacement, out KD_CallPose callPose)
        {
            bool ok = true;
            // ---- IMP-DOG-014 おやつ ----
            var boxT = dogs.Find(BoxName);
            box = boxT == null ? null : boxT.GetComponent<KD_TreatBox>();
            KD_SceneObjects.Find(BoxName, out int boxCount);
            var slots = box == null ? null : box.slots;
            if (box == null || boxCount != 1 || slots == null || slots.Length != SlotCount || !System.Array.TrueForAll(slots, s => s != null))
            {
                log.AppendLine("FAIL " + KD_Hillside.DogsRootName + "/" + BoxName + " が無いか枠が " + SlotCount + " 本そろっていない (シーン内 " + boxCount + ")。先に Menu/6c Setup Treats");
                ok = false; box = null;
            }
            // ---- IMP-DOG-015 かざり・撫でFX ----
            var accT = dogs.Find(KD_DogDefs.AccessoryBoxName);
            accessories = accT == null ? null : accT.GetComponent<KD_AccessoryBox>();
            KD_SceneObjects.Find(KD_DogDefs.AccessoryBoxName, out int accCount);
            if (accessories == null || accCount != 1)
            {
                log.AppendLine("FAIL " + KD_Hillside.DogsRootName + "/" + KD_DogDefs.AccessoryBoxName + " に KD_AccessoryBox が 1 個ない (シーン内 " + accCount + ")。先に Menu/6e Setup Accessories");
                ok = false; accessories = null;
            }
            petFxBox = KD_BundledPetFxEditor.HillsideFindBox(dogs, log);   // 無ければ FAIL を書いて null (Menu/6g)
            if (petFxBox == null) ok = false;
            // ---- IMP-DOG-016 呼ぶ・ポーズ・場所決め ----
            var boardT = dogs.Find(KD_DogDefs.SleepBoardName);
            sleepBoard = boardT == null ? null : boardT.GetComponent<KD_SleepBoard>();
            KD_SceneObjects.Find(KD_DogDefs.SleepBoardName, out int boardCount);
            if (sleepBoard == null || boardCount != 1)
            {
                log.AppendLine("FAIL " + KD_Hillside.DogsRootName + "/" + KD_DogDefs.SleepBoardName + " が 1 個ない (シーン内 " + boardCount + ")。先に Menu/6a → refresh → 6 Setup Sleep");
                ok = false; sleepBoard = null;
            }
            var placementT = dogs.Find(KD_SleepPlacementEditor.SystemName);
            sleepPlacement = placementT == null ? null : placementT.GetComponent<KD_SleepPlacement>();
            if (sleepPlacement == null) { log.AppendLine("FAIL " + KD_Hillside.DogsRootName + "/" + KD_SleepPlacementEditor.SystemName + " が無い。先に Menu/6i Setup Sleep Placement"); ok = false; }
            var callT = dogs.Find("KD_CallPose");
            callPose = callT == null ? null : callT.GetComponent<KD_CallPose>();
            if (callPose == null) { log.AppendLine("FAIL " + KD_Hillside.DogsRootName + "/KD_CallPose が無い。先に Menu/6j Setup Call Poses"); ok = false; }
            log.AppendLine("   段階 2: box=" + (box != null) + " accessories=" + (accessories != null) + " petFxBox=" + (petFxBox != null)
                + " sleepBoard=" + (sleepBoard != null) + " sleepPlacement=" + (sleepPlacement != null) + " callPose=" + (callPose != null));
            return ok;
        }

        /// <summary>Menu/7 が中で走らせる Bind (KD_MenuSettings が居るときだけ) の前提を、変更の前に確かめる。
        /// UILocalizationBindings の束縛先のうち **7 自身が作るもの (板 KD_TreatPouchPanel / KD_TouchLauncher / KD_TreatPouch) 以外**が
        /// シーンに解決でき、束縛の文言キーが文言表にあること。足りなければ手順を log に書いて false。</summary>
        static bool HillsideBindTargetsReady(StringBuilder log)
        {
            if (KD_SceneObjects.Find("KD_MenuSettings") == null) { log.AppendLine("   束縛先の事前検査: KD_MenuSettings が無い = 7 の中で Bind は走らない (skip)"); return true; }
            var m = JsonUtility.FromJson<KD_LocalizationEditor.Manifest>(File.ReadAllText(KD_LocalizationEditor.BindingsPath));
            var catalog = KD_LocalizationEditor.Load();
            var own = new System.Collections.Generic.HashSet<string> { PouchPanelName, PouchLauncherName, PouchName };
            var all = new System.Collections.Generic.List<KD_LocalizationEditor.Binding>();
            if (m.labels != null) all.AddRange(m.labels);
            if (m.interactions != null) all.AddRange(m.interactions);
            if (m.pickups != null) all.AddRange(m.pickups);
            int missing = 0, missingKeys = 0;
            var reported = new System.Collections.Generic.HashSet<string>();
            foreach (var b in all)
            {
                try { KD_LocalizationEditor.Text(catalog, catalog.fallbackLanguage, b.key); }
                catch (System.InvalidOperationException) { missingKeys++; log.AppendLine("   FAIL 束縛の文言キーが文言表に無い: " + b.key + " (文言の統合を先に)"); }
                if (own.Contains(b.root)) continue;
                try { KD_LocalizationEditor.Resolve(b); }
                catch (System.InvalidOperationException e)
                {
                    missing++;
                    if (reported.Add(b.root)) log.AppendLine("   FAIL " + e.Message + " — " + HillsideBindHint(b.root));
                }
            }
            if (m.names != null)
                foreach (var n in m.names)
                    if (!own.Contains(n.root) && KD_SceneObjects.Find(n.root) == null) { missing++; log.AppendLine("   FAIL 名前の束縛先 " + n.root + " が無い — " + HillsideBindHint(n.root)); }
            log.AppendLine("   束縛先の事前検査: 束縛 " + all.Count + " 件 / 束縛先の不足 " + missing + " / 文言キーの不足 " + missingKeys);
            return missing == 0 && missingKeys == 0;
        }

        static string HillsideBindHint(string root)
        {
            if (root.StartsWith("KD_ShopKeeper")) return "先に Tools/KominkaHillside/Menu/4d Setup Shop Keeper";
            return "UILocalizationBindings.json の束縛先を作る Setup を先に実行";
        }

        static int CountSet<T>(T[] a) where T : Object
        {
            int c = 0;
            if (a != null) foreach (var x in a) if (x != null) c++;
            return c;
        }

        // ================================================================ 板の裏山差分 (BuildPouchPanel から呼ぶ)

        /// <summary>BuildPouchPanel の中、**Scroll.Build の直前**に呼ぶ (行は Page 直下に作り、Scroll が Content へ移す)。
        /// さんぽ (Page_Walk の WalkRow0..7) / おもちゃ行の [ディスク] / かざりの FxRow0..10 / ライブの準備中の初期文。
        /// 行の座標は Scroll.Build が決める (ここの座標は仮置き)。</summary>
        static void HillsideBuildPages(GameObject panel, KD_TreatPouch pouch, GameObject[] pages, TMP_FontAsset font,
            VRC.Udon.UdonBehaviour udon, Sprite round, Sprite circle, StringBuilder log)
        {
            var defs = KD_DogDefs.All;

            // ---- さんぽ (Walk2。KH_HandMenu の さんぽ頁を古民家の行の型へ) ----
            var walk = pages[KD_TreatPouch.TabWalk].transform;
            var walkLabels = new TextMeshProUGUI[WalkRowCount];
            var walkStates = new TextMeshProUGUI[WalkRowCount];
            var leftButtons = new UnityEngine.UI.Button[WalkDogRows];
            var rightButtons = new UnityEngine.UI.Button[WalkDogRows];
            var offButtons = new UnityEngine.UI.Button[WalkDogRows];
            var leftImages = new UnityEngine.UI.Image[WalkDogRows];
            var rightImages = new UnityEngine.UI.Image[WalkDogRows];
            var offImages = new UnityEngine.UI.Image[WalkDogRows];
            var hereButtons = new UnityEngine.UI.Button[WalkRowCount - WalkDogRows];
            var hereImages = new UnityEngine.UI.Image[WalkRowCount - WalkDogRows];
            string wLeft = KD_Hillside.Text("KD_TreatPouch.WordWalkLeft", "左につなぐ", log);
            string wRight = KD_Hillside.Text("KD_TreatPouch.WordWalkRight", "右につなぐ", log);
            string wOff = KD_Hillside.Text("KD_TreatPouch.WordWalkOff", "はずす", log);
            string wHere = KD_Hillside.Text("KD_TreatPouch.WordWalkHere", "ここへ", log);
            string[] spots = {
                KD_Hillside.Text("KD_TreatPouch.WordWalkPlaza", "広場", log),
                KD_Hillside.Text("KD_TreatPouch.WordWalkGate", "古民家側の入口", log) };
            string[] spotEvents = { "_WalkPlaza", "_WalkGate" };
            for (int i = 0; i < WalkRowCount; i++)
            {
                var row = KD_ShopSetupEditor.EnsureUiChild(walk, WalkRowPrefix + i, GridPos(i, 68f, -42f), new Vector2(340f, 100f));
                if (i < WalkDogRows)
                {
                    var swatch = KD_ShopSetupEditor.EnsureUiChild(row.transform, "Swatch", new Vector2(-148f, 26f), new Vector2(26f, 26f));
                    var swImg = swatch.GetComponent<UnityEngine.UI.Image>();
                    if (swImg == null) swImg = swatch.AddComponent<UnityEngine.UI.Image>();
                    swImg.color = defs[i].ballColor; swImg.raycastTarget = false;
                    KD_UiSpriteEditor.ApplyCircleSkin(swImg, circle);
                    walkLabels[i] = EnsureText(row.transform, "Label", new Vector2(20f, 26f), new Vector2(270f, 42f), 24f, TextAlignmentOptions.Left, font);
                    walkLabels[i].text = defs[i].displayName;
                    walkStates[i] = EnsureText(row.transform, "Reason", new Vector2(0f, 0f), new Vector2(330f, 30f), 18f, TextAlignmentOptions.Left, font);
                    var l = RoundButton(row.transform, "Left", new Vector2(-110f, -30f), new Vector2(110f, 40f), KD_UiSpriteEditor.ActionEquip, font, wLeft, udon, "_WalkLeft" + i, round, log);
                    var r = RoundButton(row.transform, "Right", new Vector2(0f, -30f), new Vector2(110f, 40f), KD_UiSpriteEditor.ActionEquip, font, wRight, udon, "_WalkRight" + i, round, log);
                    var o = RoundButton(row.transform, "Off", new Vector2(110f, -30f), new Vector2(110f, 40f), KD_UiSpriteEditor.ActionNeutral, font, wOff, udon, "_WalkOff" + i, round, log);
                    ButtonLabelOf(l, 18f); ButtonLabelOf(r, 18f); ButtonLabelOf(o, 18f);
                    leftButtons[i] = l.GetComponent<UnityEngine.UI.Button>(); leftImages[i] = l.GetComponent<UnityEngine.UI.Image>();
                    rightButtons[i] = r.GetComponent<UnityEngine.UI.Button>(); rightImages[i] = r.GetComponent<UnityEngine.UI.Image>();
                    offButtons[i] = o.GetComponent<UnityEngine.UI.Button>(); offImages[i] = o.GetComponent<UnityEngine.UI.Image>();
                }
                else
                {
                    int s = i - WalkDogRows;
                    walkLabels[i] = EnsureText(row.transform, "Label", new Vector2(0f, 26f), new Vector2(330f, 42f), 24f, TextAlignmentOptions.Left, font);
                    walkLabels[i].text = spots[s];
                    walkStates[i] = EnsureText(row.transform, "Reason", new Vector2(75f, -26f), new Vector2(180f, 40f), 18f, TextAlignmentOptions.Left, font);
                    var h = RoundButton(row.transform, "Here", new Vector2(-100f, -26f), new Vector2(130f, 44f), KD_UiSpriteEditor.ActionPrimary, font, wHere, udon, spotEvents[s], round, log);
                    ButtonLabelOf(h, 20f);
                    hereButtons[s] = h.GetComponent<UnityEngine.UI.Button>(); hereImages[s] = h.GetComponent<UnityEngine.UI.Image>();
                }
            }
            pouch.walkLabels = walkLabels; pouch.walkStateLabels = walkStates;
            pouch.walkLeftButtons = leftButtons; pouch.walkRightButtons = rightButtons; pouch.walkOffButtons = offButtons;
            pouch.walkLeftImages = leftImages; pouch.walkRightImages = rightImages; pouch.walkOffImages = offImages;
            pouch.walkHereButtons = hereButtons; pouch.walkHereImages = hereImages;

            // ---- おもちゃ (Toy1。行に [ボール] [ディスク]。叩く先は KH_ToyRecall = 実行時) ----
            var toy = pages[KD_TreatPouch.TabToy].transform;
            string wBall = KD_Hillside.Text("KD_TreatPouch.WordToyBall", "ボール", log);
            string wDisc = KD_Hillside.Text("KD_TreatPouch.WordToyDisc", "ディスク", log);
            int toyRows = Mathf.Min(defs.Length, 6);
            var discButtons = new GameObject[toyRows];
            var discComponents = new UnityEngine.UI.Button[toyRows];
            var discImages = new UnityEngine.UI.Image[toyRows];
            var discLabels = new TextMeshProUGUI[toyRows];
            for (int i = 0; i < toyRows; i++)
            {
                var row = toy.Find("ToyRow" + i);
                if (row == null) { log.AppendLine("   FAIL ToyRow" + i + " が無い (おもちゃの器)"); continue; }
                var ball = row.Find("Button");
                var ballLabel = ball == null ? null : ball.Find("Label");
                if (ballLabel != null) ballLabel.GetComponent<TextMeshProUGUI>().text = wBall;
                discButtons[i] = RoundButton(row, DiscButtonName, new Vector2(40f, -26f), new Vector2(130f, 44f),
                    KD_UiSpriteEditor.ActionPrimary, font, wDisc, udon, "_Disc" + i, round, log);
                discComponents[i] = discButtons[i].GetComponent<UnityEngine.UI.Button>();
                discImages[i] = discButtons[i].GetComponent<UnityEngine.UI.Image>();
                discLabels[i] = ButtonLabelOf(discButtons[i], 20f);
            }
            pouch.discButtons = discButtons; pouch.discButtonComponents = discComponents;
            pouch.discButtonImages = discImages; pouch.discButtonLabels = discLabels;

            // ---- かざり (Acc5) の Content に撫でFX の行を AccRow の後ろへ (D-2 (d))。描画・購入は IMP-DOG-015 ----
            //
            // 0-b では**既定で非表示** — 描画 (RedrawFxRows) が入るまで空の行を見せない
            var acc = pages[KD_TreatPouch.TabAcc].transform;
            var fxLabels = new TextMeshProUGUI[HillsideFxRowCount];
            var fxSubs = new TextMeshProUGUI[HillsideFxRowCount];
            var fxButtons = new GameObject[HillsideFxRowCount];
            var fxImages = new UnityEngine.UI.Image[HillsideFxRowCount];
            var fxButtonLabels = new TextMeshProUGUI[HillsideFxRowCount];
            for (int i = 0; i < HillsideFxRowCount; i++)
            {
                var row = KD_ShopSetupEditor.EnsureUiChild(acc, "FxRow" + i, SleepGridPos(i), new Vector2(340f, 92f));
                fxLabels[i] = EnsureText(row.transform, "Label", new Vector2(0f, 20f), new Vector2(330f, 40f), 22f, TextAlignmentOptions.Left, font);
                fxSubs[i] = EnsureText(row.transform, "Sub", new Vector2(50f, -22f), new Vector2(170f, 44f), 18f, TextAlignmentOptions.Left, font);
                fxButtons[i] = RoundButton(row.transform, "Button", new Vector2(-100f, -22f), new Vector2(130f, 44f),
                    KD_UiSpriteEditor.ActionEquip, font, KD_TreatPouch.WordEquip, udon, "_Fx" + i, round, log);
                fxImages[i] = fxButtons[i].GetComponent<UnityEngine.UI.Image>();
                fxButtonLabels[i] = ButtonLabelOf(fxButtons[i], 0f);
                row.SetActive(false);
            }
            // 裏山改変 (IMP-DOG-015): 撫でFX の群見出し (計画書 D-2)。2 列の Grid なので見出し 1 セル + 空きセル 1 つ。
            // Scroll.Build が RowsFor の順 (AccRow → FxHead → FxRow) で Content へ移す。カタログの見える行は 14 = 偶数なので、見出しは必ず新しい段の左端
            var fxHeads = new GameObject[2];
            for (int h = 0; h < 2; h++)
            {
                fxHeads[h] = KD_ShopSetupEditor.EnsureUiChild(acc, FxHeadPrefix + h, SleepGridPos(h), new Vector2(340f, 92f));
                fxHeads[h].SetActive(false);   // 実行時 RedrawFxRows がカタログ階層で出す
            }
            var fxHeadLabel = EnsureText(fxHeads[0].transform, "Label", new Vector2(0f, 20f), new Vector2(330f, 40f), 22f, TextAlignmentOptions.Left, font);
            fxHeadLabel.text = KD_Hillside.Text("KD_TreatPouch.WordFxGroup", "撫でエフェクト", log);
            pouch.hillsideFxHeadRows = fxHeads;
            pouch.hillsideFxHeadLabel = fxHeadLabel;
            pouch.fxLabels = fxLabels; pouch.fxSubLabels = fxSubs; pouch.fxButtons = fxButtons;
            pouch.fxButtonImages = fxImages; pouch.fxButtonLabels = fxButtonLabels;
            // 撫でFX 専用のタブは無い。確認カードは共有 1 枚 (kind Fx はそのまま)
            pouch.fxConfirmRoot = pouch.confirmRoot; pouch.fxConfirmLabel = pouch.confirmBodyLabel;
            // 裏山改変 (司令塔 2026-09-23): かざりの確認も共有 1 枚を指す。古民家では KD_DogPreviewEditor.RewireMenu (段階 2) が配線するが、Verify (Menu/20・21) は段階 0 から共有カードを要求する
            pouch.accConfirmRoot = pouch.confirmRoot; pouch.accConfirmLabel = pouch.confirmBodyLabel; pouch.accConfirmYes = pouch.confirmYes;
            pouch.fxConfirmYes = pouch.confirmYes; pouch.fxConfirmNo = pouch.confirmNo;

            // ---- ライブ (Live4)。裏山改変 (DES-LIV-001): 行は KD_DogDefs.LiveShows の本数だけ (裏山は 1 曲)。
            // 古民家の 9 曲ぶんの器 LiveRow{n..} は Scroll.Build が Content へ移さず Page 直下に残ってしまうので、ここで消す
            // (Scroll.Build と Bind の前。Data/UILocalizationBindings.json の LiveRow1..7 の束縛も同時に外した)。
            // live が無い間は行 0 に準備中の初期文を置く (実行時は KD_TreatPouch.HillsidePreparing が同じ文言で描き直す)。
            // live があれば行の文字は実行時の RedrawLive が描く ----
            int removedLiveRows = HillsideRemoveExtraLiveRows(pages[KD_TreatPouch.TabLive].transform, log);
            if (pouch.live == null)
            {
                if (pouch.liveLabels != null && pouch.liveLabels.Length > 0 && pouch.liveLabels[0] != null)
                    pouch.liveLabels[0].text = KD_Hillside.Text("KD_TreatPouch.WordLivePreparing", "裏山のライブは準備中", log);
                if (pouch.liveSubLabels != null && pouch.liveSubLabels.Length > 0 && pouch.liveSubLabels[0] != null)
                    pouch.liveSubLabels[0].text = KD_Hillside.Text("KD_TreatPouch.WordLivePreparingSub", "演目と開催の仕組みは、これから決めます。", log);
            }

            log.AppendLine("   裏山: Page_Walk " + WalkRowCount + " 行 (犬 " + WalkDogRows + " + 行き先 2) / おもちゃ [ディスク] " + toyRows
                + " / かざりの群見出し + FxRow " + HillsideFxRowCount + " (既定非表示) / ライブ "
                + (pouch.live != null ? KD_DogDefs.LiveShows.Length + " 曲 (live 配線あり)" : "は準備中 (live なし)")
                + " / 余った LiveRow を " + removedLiveRows + " 行削除");

            // ---- 段階 1: ヘッダの残高の入口 + ポイントの面 (入手経路 2 行 + [もどる]) / ようこその面 ----
            if (KD_Hillside.Stage >= 1)
            {
                HillsideBuildPoints(panel, pouch, font, udon, round, log);
                // ようこその面の器は板の再構築 (ここ) で作る — Bind (Menu Finish) が Page_Welcome の束縛を要求するので、
                // 11b Build Welcome より前の Menu/7・11 でも面が居る必要がある。庭の置き場・手元ヒントは 11b
                KD_WelcomeGuideEditor.BuildPage(pouch, panel.transform, font, udon, log);
            }

            // ---- 段階 2 (IMP-DOG-014): おやつの行に [買う] (Row{i}/Buy -> _TreatBuy{i})。実体は KD_TreatSetupEditor.Treats.cs ----
            if (KD_Hillside.Stage >= 2)
                HillsideBuildTreatBuy(pages, pouch, font, udon, round, log);
        }

        /// <summary>段階 1 のポイントの面 (計画書 D-5「残高・ポイント面」)。古民家の KD_GiftCodeSetupEditor.Setup の入口と面から
        /// **ギフトコードの器 (KD_GiftCodePanel・入力欄・受け取ったコード) を外した**もの。入口は残高ラベルの裏の PointsButton
        /// (位置と大きさは KD_HandMenuScrollEditor.LayoutFrame が決める)、面は Page_Settings/PointsPage (extraSettingsPages に登録)。
        /// 文字は実行時に KD_TreatPouch.HillsideRedrawPoints が書く (ここの字は Editor 上の仮)。</summary>
        static void HillsideBuildPoints(GameObject panel, KD_TreatPouch pouch, TMP_FontAsset font, VRC.Udon.UdonBehaviour udon, Sprite round, StringBuilder log)
        {
            var balance = pouch.balanceLabel;
            if (balance == null || pouch.settingsPage == null) { log.AppendLine("   FAIL ポイントの面: balanceLabel / settingsPage が無い"); return; }
            var br = balance.rectTransform;
            var entry = KD_ShopSetupEditor.EnsureButton(panel.transform, KD_GiftCodeSetupEditor.PointsButtonName, br.anchoredPosition,
                br.sizeDelta + new Vector2(16f, 6f), new Color(.34f, .30f, .28f, .55f), font, "", udon, "_HillsidePoints", log);
            // 入口 → 残高ラベルの順で末尾へ (ラベルが上に描かれる)。確認カードは BuildPouchPanel が後で最後の子へ戻す
            entry.transform.SetAsLastSibling(); balance.transform.SetAsLastSibling();
            balance.raycastTarget = false;
            if (!balance.gameObject.activeSelf) balance.gameObject.SetActive(true);
            if (!entry.activeSelf) entry.SetActive(true);
            pouch.hillsidePointsButton = entry;

            // 古民家のギフトコードの面 (GiftCodePage) が残っていれば片付ける (裏山ではギフトコードの器を作らない)
            var gift = pouch.settingsPage.transform.Find(KD_GiftCodeSetupEditor.PageName);
            if (gift != null) { log.AppendLine("   removed " + KD_GiftCodeSetupEditor.PageName + " (裏山ではギフトコードの器を作らない)"); Object.DestroyImmediate(gift.gameObject); }

            var page = KD_ShopSetupEditor.EnsureUiChild(pouch.settingsPage.transform, KD_Hillside.PointsPageName, Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            string T(string key, string fallback) { return KD_Hillside.Text(key, fallback, log); }
            var heading = EnsureText(page.transform, "Heading", new Vector2(0f, 170f), new Vector2(1040f, 44f), 28f, TextAlignmentOptions.Left, font);
            heading.text = T("gift.heading", "ポイントの入手経路");
            var rows = new TextMeshProUGUI[2];
            var subs = new TextMeshProUGUI[2];
            float[] ys = { 100f, 4f };
            for (int i = 0; i < 2; i++)
            {
                rows[i] = EnsureText(page.transform, "Row" + i, new Vector2(0f, ys[i]), new Vector2(1040f, 40f), 26f, TextAlignmentOptions.Left, font);
                subs[i] = EnsureText(page.transform, "Sub" + i, new Vector2(0f, ys[i] - 40f), new Vector2(1040f, 32f), 20f, TextAlignmentOptions.Left, font);
                subs[i].color = new Color(.85f, .82f, .76f);
            }
            rows[0].text = T("KD_TreatPouch.WordPointsKeeper", "おじいさんの支度ポイント") + "  +" + KD_Hillside.KeeperPoints + " pt";
            subs[0].text = T("KD_TreatPouch.WordPointsKeeperSub", "一度だけ。入口のおじいさんが渡してくれます");
            rows[1].text = T("gift.source.stay", "滞在 1 分ごとに +1 pt");
            subs[1].text = T("KD_TreatPouch.WordPointsStaySub", "ここで過ごすだけでたまります");
            var back = RoundButton(page.transform, "Back", new Vector2(-430f, -285f), new Vector2(200f, 48f), KD_UiSpriteEditor.ActionNeutral, font,
                T("gift.back", "戻る"), udon, "_HillsidePointsBack", round, log);
            pouch.hillsidePointsPage = page;
            pouch.hillsidePointsHeading = heading;
            pouch.hillsidePointsRows = rows;
            pouch.hillsidePointsSubs = subs;
            pouch.hillsidePointsBackLabel = ButtonLabelOf(back, 24f);
            var pages = new System.Collections.Generic.List<GameObject>();
            foreach (var p in pouch.extraSettingsPages ?? new GameObject[0]) if (p != null && p != page && !pages.Contains(p)) pages.Add(p);
            pages.Add(page);
            pouch.extraSettingsPages = pages.ToArray();
            page.SetActive(false);
            log.AppendLine("   段階 1: ポイントの面 " + KD_Hillside.PointsPageName + " (入手経路 2 行 + [もどる]。ギフトコードの器なし) / 入口 " + KD_GiftCodeSetupEditor.PointsButtonName + " -> _HillsidePoints");
        }

        // ================================================================ ライブタブ (裏山改変 DES-LIV-001)

        /// <summary>古民家の器の最大行 (LiveRow0..8) より少し広く掃く上限。</summary>
        const int HillsideLiveRowSweep = 16;

        /// <summary>シーンの KD_LiveSystem (無ければ null / count 0)。Menu/26a が KH_Dogs/KH_Live に置く。</summary>
        internal static KD_LiveSystem HillsideSceneLive(out int count)
        {
            var go = KD_SceneObjects.Find(KD_DogDefs.LiveSystemName, out count);
            return (count == 1 && go != null) ? go.GetUdonSharpComponent<KD_LiveSystem>() : null;
        }

        /// <summary>Page_Live の下 (直下 / Scroll の Content) にある <c>LiveRow{n}</c> のうち n ≥ LiveShows.Length を消す。戻り値は消した数。
        /// プレハブの中にあれば消さずに FAIL を書く (裏山の板はシーン直置き)。</summary>
        static int HillsideRemoveExtraLiveRows(Transform livePage, StringBuilder log)
        {
            int removed = 0;
            if (livePage == null) return 0;
            var content = livePage.Find("Scroll/Viewport/Content");
            for (int i = KD_DogDefs.LiveShows.Length; i < HillsideLiveRowSweep; i++)
            {
                foreach (var parent in new[] { livePage, content })
                {
                    if (parent == null) continue;
                    Transform row;
                    while ((row = parent.Find("LiveRow" + i)) != null)
                    {
                        if (PrefabUtility.IsPartOfPrefabInstance(row.gameObject))
                        {
                            log.AppendLine("   FAIL " + row.name + " がプレハブの中にあり消せない (手で Unpack するか、プレハブ側で消す)");
                            break;
                        }
                        Undo.DestroyObjectImmediate(row.gameObject);
                        removed++;
                    }
                }
            }
            if (removed > 0) log.AppendLine("   removed 余った LiveRow " + removed + " 行 (LiveShows " + KD_DogDefs.LiveShows.Length + " 本より後ろ)");
            return removed;
        }

        /// <summary>Menu/20: Page_Live の下に LiveRow{n ≥ LiveShows.Length} が残っていない (古民家の 9 曲の器の残り)。</summary>
        static void HillsideCheckNoExtraLiveRows(KD_VerifyLog v, KD_TreatPouch pouch)
        {
            var page = pouch.tabPages != null && pouch.tabPages.Length > KD_TreatPouch.TabLive ? pouch.tabPages[KD_TreatPouch.TabLive] : null;
            if (!v.Check(page != null, "Page_Live (tabPages[" + KD_TreatPouch.TabLive + "]) が配線")) return;
            int extra = 0;
            var names = new StringBuilder();
            foreach (var t in page.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("LiveRow")) continue;
                int n;
                if (!int.TryParse(t.name.Substring("LiveRow".Length), out n) || n < KD_DogDefs.LiveShows.Length) continue;
                extra++; names.Append(t.name + " ");
            }
            v.Check(extra == 0, "Page_Live に余った LiveRow が無い (LiveShows " + KD_DogDefs.LiveShows.Length + " 本より後ろ。actual " + extra + " " + names + "— Menu/7 が消す)");
        }

        /// <summary>Menu/20 の裏山ライブ検査 (live があるときだけ呼ぶ)。古民家の Verify (KD_TreatSetupEditor.Verify.cs の「ライブタブ」節) から、
        /// 裏山で意味のある項目だけを写した: 行 = 作品 (添字 = liveId) の中身・押し先、一覧の行 = 商品、詳細の面の配線、文言キー。
        /// 参考画像 (Textures/LiveThumbs/&lt;productKey&gt;.png) は任意 (無ければ色面 + 準備中) なので NOTE。</summary>
        static void HillsideVerifyLive(KD_VerifyLog v, KD_TreatPouch pouch, StringBuilder log)
        {
            var shows = KD_DogDefs.LiveShows;
            int n = shows.Length;
            var liveUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
            v.Check(n >= 1 && n <= 9, "LiveShows が 1〜9 本 (板の口 _Live0.._Live8。actual " + n + ")");
            v.Check(pouch.live.stageDirectors != null && pouch.live.stageDirectors.Length == n,
                "KD_LiveSystem.stageDirectors の本数 == LiveShows " + n + " (添字 = liveId がずれない。Menu/26a)");
            v.Check(pouch.liveItems != null && pouch.liveItems.Length == n, PouchName + ".liveItems が " + n + " 本 (行 = 作品)");
            v.Check(pouch.liveTitles != null && pouch.liveTitles.Length == n, PouchName + ".liveTitles が " + n + " 本");
            v.Check(pouch.liveTitleKeys != null && pouch.liveTitleKeys.Length == n,
                PouchName + ".liveTitleKeys が " + n + " 本 (Data/UILocalizationBindings.json の names。Menu/11 が書く。actual "
                + (pouch.liveTitleKeys == null ? -1 : pouch.liveTitleKeys.Length) + ")");
            v.Check(pouch.liveSubLabels != null && pouch.liveSubLabels.Length == n
                && pouch.liveButtons != null && pouch.liveButtons.Length == n
                && pouch.liveButtonComponents != null && pouch.liveButtonComponents.Length == n
                && pouch.liveButtonImages != null && pouch.liveButtonImages.Length == n
                && pouch.liveButtonLabels != null && pouch.liveButtonLabels.Length == n
                && pouch.liveGiftButtons != null && pouch.liveGiftButtons.Length == n
                && pouch.liveGiftLabels != null && pouch.liveGiftLabels.Length == n,
                PouchName + ".liveSubLabels / liveButton* / liveGift* が " + n + " 本ずつ");
            var catalog = KD_LocalizationEditor.Load();
            for (int i = 0; i < n; i++)
            {
                string tag = "ライブ行 " + i + " (" + shows[i].slug + ") ";
                var it = ArrayAt(pouch.liveItems, i);
                v.Check(it != null && it.categoryKey == KD_ShopSetupEditor.CategoryLive && it.productKey == shows[i].productKey,
                    tag + "の liveItems.productKey == " + shows[i].productKey + " (actual " + (it == null ? "null" : it.productKey) + ")");
                v.Check(ArrayAt(pouch.liveTitles, i) == shows[i].title, tag + "の liveTitles == \"" + shows[i].title + "\"");
                string wantKey = "KD_TreatPouch.liveTitleKeys." + i;
                v.Check(ArrayAt(pouch.liveTitleKeys, i) == wantKey, tag + "の liveTitleKeys == " + wantKey);
                var btn = ArrayAt(pouch.liveButtonComponents, i);
                Object tgt;
                string want = "_Live" + i;
                v.Check(OnClickEventName(btn, out tgt) == want && liveUdon != null && tgt == (Object)liveUdon
                    && typeof(KD_TreatPouch).GetMethod(want) != null, tag + "の onClick が SendCustomEvent(\"" + want + "\") で pouch 宛 (口が実在)");
                var gift = ArrayAt(pouch.liveGiftButtons, i);
                var giftBtn = gift == null ? null : gift.GetComponent<UnityEngine.UI.Button>();
                string wantGift = "_LiveGift" + i;
                v.Check(gift != null && !gift.activeSelf && OnClickEventName(giftBtn, out tgt) == wantGift && tgt == (Object)liveUdon
                    && typeof(KD_TreatPouch).GetMethod(wantGift) != null, tag + "の [ギフト] が既定非表示で " + wantGift);
                // 曲名の枠 (ja / en の文言。Overflow は黙って隣へ描かれる)
                var lbl = ArrayAt(pouch.liveLabels, i);
                if (lbl != null && lbl.font != null)
                    foreach (var lang in new[] { "ja", "en" })
                    {
                        string text;
                        try { text = KD_LocalizationEditor.Text(catalog, lang, wantKey); }
                        catch (System.InvalidOperationException) { v.Check(false, tag + "の文言キー " + wantKey + " が UITranslations.json に無い"); continue; }
                        float need = PreferredUiWidth(lbl, text);
                        v.Check(need <= lbl.rectTransform.sizeDelta.x, tag + "の曲名 " + lang + " \"" + text + "\" が枠に収まる (要 " + need.ToString("F0") + "px / 枠 " + lbl.rectTransform.sizeDelta.x.ToString("F0") + "px)");
                    }
            }
            v.Check(pouch.liveStatusLabel != null, PouchName + ".liveStatusLabel 配線 (残り秒の出し先)");
            // 一覧の行 = 商品 (KD_DogDefs.LiveProducts)
            var products = KD_DogDefs.LiveProducts;
            v.Check(pouch.liveProducts != null && pouch.liveProducts.Length == products.Length, PouchName + ".liveProducts が " + products.Length + " 本");
            for (int i = 0; pouch.liveProducts != null && i < pouch.liveProducts.Length && i < products.Length; i++)
            {
                var it = pouch.liveProducts[i];
                v.Check(it != null && it.productKey == products[i].productKey && System.Array.IndexOf(pouch.liveItems ?? new KD_ShopItem[0], it) >= 0,
                    "ライブ商品行 " + i + " が " + products[i].productKey + " で、作品の表 (liveItems) に居る");
                if (it == null) continue;
                string k = it.productKey.StartsWith("live.") ? it.productKey.Substring(5) : it.productKey;
                int missingKeys = 0;
                var missingNames = new StringBuilder();
                foreach (var key in new[] { it.displayNameKey, "live.detail." + k + ".jp", "live.detail." + k + ".meta", "live.detail." + k + ".mood", "live.detail." + k + ".scene" })
                    foreach (var lang in new[] { "ja", "en" })
                    {
                        try { KD_LocalizationEditor.Text(catalog, lang, key); }
                        catch (System.InvalidOperationException) { missingKeys++; missingNames.Append(lang + ":" + key + " "); }
                    }
                v.Check(missingKeys == 0, "ライブ商品 " + it.productKey + " の名前と詳細 4 本の文言キーが ja / en にある (欠け " + missingKeys + " " + missingNames + ")");
                if (pouch.liveThumbs == null || i >= pouch.liveThumbs.Length || pouch.liveThumbs[i] == null)
                    v.Note("参考画像なし: " + it.productKey + " (Textures/LiveThumbs/" + it.productKey + ".png。任意。無ければ色面 + 準備中)");
            }
            v.Check(pouch.liveDetailRoot != null && pouch.liveDetailTitle != null && pouch.liveDetailMeta != null && pouch.liveDetailMood != null
                && pouch.liveDetailScene != null && pouch.liveDetailNote != null && pouch.liveDetailPosterLabel != null,
                PouchName + ".liveDetail* の面と文字が配線 (無いと [くわしく] が空の面を出す)");
            v.Check(pouch.liveDetailRoot != null && !pouch.liveDetailRoot.activeSelf, "LiveDetail は既定で非アクティブ (一覧から開く)");
            v.Check(pouch.liveDetailBack != null && pouch.liveDetailAction != null && pouch.liveDetailActionButton != null
                && !pouch.liveDetailBack.activeSelf && !pouch.liveDetailAction.activeSelf, PouchName + ".liveDetailBack / Action 配線 (既定で非アクティブ)");
            {
                Object tgt;
                var backBtn = pouch.liveDetailBack == null ? null : pouch.liveDetailBack.GetComponent<UnityEngine.UI.Button>();
                v.Check(backBtn != null && OnClickEventName(backBtn, out tgt) == "_LiveBack" && tgt == (Object)liveUdon, "[もどる] が _LiveBack へ配線");
                v.Check(pouch.liveDetailActionButton != null && OnClickEventName(pouch.liveDetailActionButton, out tgt) == "_LiveDetailAction" && tgt == (Object)liveUdon,
                    "[買う / 開催] が _LiveDetailAction へ配線");
            }
            log.AppendLine("  NOTE 開催許可 (live.access) と hostingItems は Menu/11 と Menu/26b が見る。ステージ・犬・夜の配線は Menu/26b");
        }

        // ================================================================ 文言 (字形の宣言)

        /// <summary>裏山の実行時 partial が持つ文字列リテラル (KH_HandMenu から移した状態語など) も焼く。
        /// **キー表から集める方式 (D-4)** の補い: 文言表に載る前のリテラルも取りこぼさない。</summary>
        static readonly string[] HillsideRuntimeSources = {
            "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatPouch.Hillside.cs",
            "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatPouch.Walk.cs",
            "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatPouch.Toy.cs",
            "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatPouch.Live.cs",
            "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_ActivitiesMenu.cs",
            "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_MenuSettings.cs",
        };

        /// <summary>**裏山で板が出す字の宣言** (PouchWords の相方)。
        /// 文言表の self 言語 (ja / en) の全文 + 裏山の実行時 partial のリテラル + 同じフォントを使うフリスビーの案内。
        /// KH_HandMenuEditor.Charset (Legacy) の代わり — Charset は KH_HandMenu.cs を読むので退役後は使えない。</summary>
        internal static string HillsideWords()
        {
            var sb = new StringBuilder();
            sb.Append(KD_LocalizationEditor.BundledCharacters(KD_LocalizationEditor.Load()));
            foreach (string path in HillsideRuntimeSources)
            {
                if (!File.Exists(path)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(path), "\"((?:[^\"\\\\]|\\\\.)*)\""))
                    sb.Append(m.Groups[1].Value.Replace("\\n", "").Replace("\\\"", "\""));
            }
            sb.Append(global::KominkaHillside.Editor.KH_FrisbeePrototypeEditor.AimHint);
            sb.Append(global::KominkaHillside.Editor.KH_FrisbeePrototypeEditor.AimFarHint);   // BUG-DOG-007
            // 拠点板の Interact 文言は VRChat の UI が描くので焼かない。制御文字・ZWSP は描かれないので除く
            var drawn = new StringBuilder();
            foreach (char c in sb.ToString()) if (KD_LocalizationEditor.Drawn(c)) drawn.Append(c);
            return drawn.ToString();
        }

        // ================================================================ フォント (48 pt 再ベイク)

        const string FontSourcePath = "Assets/KominkaHillside/Font/Noto_Serif_JP/static/NotoSerifJP-Regular.ttf";
        public const int HillsidePointSize = 48;

        /// <summary>Static で保存すると元 TTF の参照が外れるので、焼く前に差し直す (vrc-tmp-font §2)。GUID は変えない。</summary>
        internal static void RestoreFontSource(StringBuilder log)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            var source = AssetDatabase.LoadAssetAtPath<Font>(FontSourcePath);
            if (font == null || source == null) { log.AppendLine("   WARN フォントか元 TTF が無い (" + FontSourcePath + ")"); return; }
            var so = new SerializedObject(font);
            if (so.FindProperty("m_SourceFontFile").objectReferenceValue == source
                && so.FindProperty("m_SourceFontFile_EditorRef").objectReferenceValue == source) return;
            so.FindProperty("m_SourceFontFile").objectReferenceValue = source;
            so.FindProperty("m_SourceFontFile_EditorRef").objectReferenceValue = source;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("   font: 元 TTF の参照を差し直した");
        }

        /// <summary>共有フォント (KD_NotoSerif SDF、古民家と同 GUID) を **48 pt で焼き直す** (計画書 D-4。779/784 で満杯だったため)。
        /// 2048² / padding 6 のまま: floor(2048 / (48 + 12))² = 1156 字。焼く字 = 既に入っている字 + PouchWords + HillsideWords。
        /// KH_HandMenuEditor の Legacy/2 Ensure Glyphs が持っていた Rebake 経路の移植 (faceInfo も 48 pt で取り直す)。
        /// **アセットは作り直さない** (GUID が変わると全参照が切れる)。既に 48 pt なら欠けだけ足す。</summary>
        [MenuItem("Tools/KominkaHillside/Menu/2b Rebake Font 48pt")]
        public static void RebakeFont48()
        {
            var log = new StringBuilder("[KD_TreatSetup] Rebake font " + HillsidePointSize + "pt\n");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            var source = AssetDatabase.LoadAssetAtPath<Font>(FontSourcePath);
            if (font == null || source == null)
            {
                log.AppendLine("FAIL フォント (" + KD_ShopSetupEditor.SharedFontPath + ") か元 TTF (" + FontSourcePath + ") が無い");
                KD_VerifyLog.Write(HillsideFontLog, log);
                return;
            }
            RestoreFontSource(log);
            var all = new StringBuilder();
            foreach (var c in font.characterTable) all.Append(char.ConvertFromUtf32((int)c.unicode));
            all.Append(PouchWords()).Append(HillsideWords());
            var seen = new System.Collections.Generic.HashSet<char>();
            var distinct = new StringBuilder();
            foreach (char c in all.ToString()) if (KD_LocalizationEditor.Drawn(c) && seen.Add(c)) distinct.Append(c);
            var so = new SerializedObject(font);
            int atlas = so.FindProperty("m_AtlasWidth").intValue, pad = so.FindProperty("m_AtlasPadding").intValue;
            int capacity = (int)Mathf.Pow(Mathf.Floor(atlas / (float)(HillsidePointSize + 2 * pad)), 2);
            log.AppendLine("before: pointSize=" + font.faceInfo.pointSize + " atlas=" + atlas + " padding=" + pad + " glyphs=" + font.glyphTable.Count
                + " chars=" + font.characterTable.Count + " / wanted distinct=" + distinct.Length + " capacity@" + HillsidePointSize + "pt=" + capacity
                + (distinct.Length > capacity * 0.95f ? " WARN >95%" : ""));
            if (distinct.Length > capacity)
            {
                log.AppendLine("FAIL 焼く字 " + distinct.Length + " が容量 " + capacity + " を超える — pt を下げるかアトラスを広げる");
                KD_VerifyLog.Write(HillsideFontLog, log);
                return;
            }
            string absent = "";
            var prev = font.atlasPopulationMode;
            try
            {
                if (Mathf.RoundToInt(font.faceInfo.pointSize) != HillsidePointSize)
                {
                    FontEngine.InitializeFontEngine();
                    if (FontEngine.LoadFontFace(source, HillsidePointSize) != FontEngineError.Success)
                        throw new System.InvalidOperationException("LoadFontFace failed");
                    font.faceInfo = FontEngine.GetFaceInfo();   // 行の高さ・ascent も 48 pt の値へ (pointSize だけ書き換えると行が 60 pt のまま)
                    font.ClearFontAssetData(false);             // アトラス寸法は保つ (2048²)
                    log.AppendLine("cleared atlas; faceInfo pointSize=" + font.faceInfo.pointSize + " lineHeight=" + font.faceInfo.lineHeight);
                }
                font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                string need = KD_ShopSetupEditor.MissingGlyphs(distinct.ToString());
                if (need.Length > 0) font.TryAddCharacters(need, out absent);
            }
            finally
            {
                font.atlasPopulationMode = AtlasPopulationMode.Static;   // Dynamic のまま残さない (vrc-tmp-font §2)
                if (prev != AtlasPopulationMode.Static) log.AppendLine("WARN 実行前が " + prev + " だった (Static へ戻した)");
                EditorUtility.SetDirty(font);
                AssetDatabase.SaveAssets();
            }
            string still = KD_ShopSetupEditor.MissingGlyphs(distinct.ToString());
            log.AppendLine("after: pointSize=" + font.faceInfo.pointSize + " glyphs=" + font.glyphTable.Count + " chars=" + font.characterTable.Count
                + " missing=" + still.Length + (still.Length == 0 ? "" : " [" + still + "]") + " absentFromTtf=[" + absent + "] mode=" + font.atlasPopulationMode);
            log.AppendLine("RESULT: " + (still.Length == 0 && Mathf.RoundToInt(font.faceInfo.pointSize) == HillsidePointSize ? "PASS" : "FAIL"));
            KD_VerifyLog.Write(HillsideFontLog, log);
        }

        // ============================================================
        // NetworkID (司令塔 2026-09-23、段階 0-b): 同期する器 (KD_LiveAccess など) を KH_Dogs に足したあと、
        // SDK と同じ手順で NetworkID を振る。Walking/5 Verify「every synced object under KH_Dogs has a NetworkID」の前提。
        // SDK はビルド時に AssignSceneNetworkIDs で同じ ConfigureNetworkIDs を呼ぶ。ここでは保存しない。
        // ============================================================
        [MenuItem("Tools/KominkaHillside/Menu/19b Assign Network IDs")]
        public static void AssignNetworkIds()
        {
            KD_Hillside.Guard(); // Codex D-3 (2026-09-23): 裏山シーン以外の Descriptor を書き換えない
            var log = new StringBuilder();
            log.AppendLine("[KD_TreatSetup] Assign Network IDs (VRC.SDKBase.Network.NetworkIDAssignment.ConfigureNetworkIDs)");
            VRC.SDKBase.VRC_SceneDescriptor descriptor = null; int descriptors = 0;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var d in root.GetComponentsInChildren<VRC.SDKBase.VRC_SceneDescriptor>(true)) { descriptors++; descriptor = d; }
            if (descriptors != 1) { log.AppendLine("FAIL 対象シーンの VRC_SceneDescriptor が 1 個でない (" + descriptors + ")"); KD_VerifyLog.Write("Temp/menu-rebuild/network-ids.txt", log); return; }
            int before = descriptor.NetworkIDCollection.Count;
            System.Collections.Generic.List<VRC.SDKBase.Network.NetworkIDAssignment.SetErrorLocation> errors;
            var result = VRC.SDKBase.Network.NetworkIDAssignment.ConfigureNetworkIDs(descriptor, out errors, new VRC.SDKBase.Network.NetworkIDAssignment.SetError[0]);
            if (errors == null) errors = new System.Collections.Generic.List<VRC.SDKBase.Network.NetworkIDAssignment.SetErrorLocation>();
            // 実測 (2026-09-23): Item1 = 割当済みの全ペア、Item2 = 今回新しく振ったペア (初回は KD_TreatPouch / KD_Activities / KD_Language / KD_MenuSettings / KD_LiveAccess の 5 件)
            int assigned = 0, added = 0;
            foreach (var pair in result.Item1) assigned++;
            foreach (var pair in result.Item2) { added++; log.AppendLine("   new " + pair.ID + " " + (pair.gameObject != null ? pair.gameObject.name : "(null)")); }
            foreach (var e in errors) log.AppendLine("   ERROR " + e.ToString());
            EditorUtility.SetDirty(descriptor);
            EditorSceneManager.MarkSceneDirty(descriptor.gameObject.scene);
            log.AppendLine("NetworkIDs: " + before + " -> " + descriptor.NetworkIDCollection.Count + " (assigned " + assigned + " / new " + added + " / errors " + errors.Count + "。InvalidObject は消えた器の残骸で、この呼び出しが取り除く)");
            log.AppendLine("RESULT: " + (errors.Count == 0 ? "PASS" : "FAIL") + " (シーンは保存していない)");
            KD_VerifyLog.Write("Temp/menu-rebuild/network-ids.txt", log);
        }
    }
}
