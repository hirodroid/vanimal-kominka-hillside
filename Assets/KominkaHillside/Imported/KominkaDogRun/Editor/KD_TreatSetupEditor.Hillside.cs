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
        public static readonly string[] OpenerStations = { "KH_LeashRack", "KH_Station_Viewpoint", "KH_Station_Plaza" };
        public const string OpenerPlateName = "MenuPlate";
        /// <summary>段階 0-b。<c>KD_TreatPouch.hillsideStage</c> へ Setup が毎回書く値 (計画書 D-1)。</summary>
        public const int Stage = 0;
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
    /// は**探さず、触らない** (新規なら null のまま。段階 1・2 のビルダーが配線する)。</summary>
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

        [MenuItem("Tools/KominkaHillside/Menu/7 Build Menu")]
        public static void SetupHillside()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_TreatSetup] Setup (裏山 段階 0-b)");
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
            BuildPouchPanel(go, pouch, pouch.items, pouch.fxItems, font, log);

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
            log.AppendLine("   段階 0: box / ledger / items / balls / toyFetchers / catalog / petFxBox / fxItems / sleepBoard / live は探さない"
                + " (現在 box=" + (pouch.box != null) + " ledger=" + (pouch.ledger != null) + " catalog=" + (pouch.catalog != null)
                + " sleepBoard=" + (pouch.sleepBoard != null) + " live=" + (pouch.live != null) + ")");
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
            log.AppendLine("**シーンは保存していない。** 次: Menu/8・9 (あそぶ) → Menu/11 → Menu/20・21・24 の Verify");
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
            pouch.fxLabels = fxLabels; pouch.fxSubLabels = fxSubs; pouch.fxButtons = fxButtons;
            pouch.fxButtonImages = fxImages; pouch.fxButtonLabels = fxButtonLabels;
            // 撫でFX 専用のタブは無い。確認カードは共有 1 枚 (kind Fx はそのまま)
            pouch.fxConfirmRoot = pouch.confirmRoot; pouch.fxConfirmLabel = pouch.confirmBodyLabel;
            // 裏山改変 (司令塔 2026-09-23): かざりの確認も共有 1 枚を指す。古民家では KD_DogPreviewEditor.RewireMenu (段階 2) が配線するが、Verify (Menu/20・21) は段階 0 から共有カードを要求する
            pouch.accConfirmRoot = pouch.confirmRoot; pouch.accConfirmLabel = pouch.confirmBodyLabel; pouch.accConfirmYes = pouch.confirmYes;
            pouch.fxConfirmYes = pouch.confirmYes; pouch.fxConfirmNo = pouch.confirmNo;

            // ---- ライブ (Live4) は準備中 1 行。**器 (LiveRow0..8) はそのまま**残し、行 0 に準備中の初期文を置く
            // (実行時は KD_TreatPouch.HillsidePreparing が同じ文言で描き直す) ----
            if (pouch.liveLabels != null && pouch.liveLabels.Length > 0 && pouch.liveLabels[0] != null)
                pouch.liveLabels[0].text = KD_Hillside.Text("KD_TreatPouch.WordLivePreparing", "裏山のライブは準備中", log);
            if (pouch.liveSubLabels != null && pouch.liveSubLabels.Length > 0 && pouch.liveSubLabels[0] != null)
                pouch.liveSubLabels[0].text = KD_Hillside.Text("KD_TreatPouch.WordLivePreparingSub", "演目と開催の仕組みは、これから決めます。", log);

            log.AppendLine("   裏山: Page_Walk " + WalkRowCount + " 行 (犬 " + WalkDogRows + " + 行き先 2) / おもちゃ [ディスク] " + toyRows
                + " / かざりの FxRow " + HillsideFxRowCount + " (既定非表示) / ライブは準備中");
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
