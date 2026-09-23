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
    /// <summary>IMP-TRT-001 おやつ: 手元メニュー (KD_TreatPouch) の構築。本体は KD_TreatSetupEditor.cs。</summary>
    public static partial class KD_TreatSetupEditor
    {
        // ================================================================ 手元メニュー
        //
        // **シーンに 1 個・SyncMode None・同期変数 0 本。**
        // 各クライアントが自分のローカルプレイヤーへ追従させるので、他人には見えない
        // (KanjiFind `KF_CrownMarker` と同型 — 画面は同期せず条件と結果だけを同期する)。
        //
        // World Space Canvas を VRChat で動かす要件 (`vrc-udonsharp` §5):
        //   Canvas + CanvasScaler + GraphicRaycaster + **VRCUiShape** / layer は **Walkthrough**
        // ("UI" レイヤーだと「押せるのに反応しない」になる)

        /// <summary>おやつポーチを作って配線する。**位置は毎フレーム上書きされる**ので、
        /// ここで置く座標に意味は無い (`KD_Systems` の下に居ればよい)。</summary>
        static KD_TreatPouch EnsurePouch(KD_TreatBox box, StringBuilder log)
        {
            int found;
            var go = KD_SceneObjects.Find(PouchName, out found);
            if (found > 1)
            {
                log.AppendLine("FAIL '" + PouchName + "' がシーン内に " + found + " 個ある");
                return null;
            }
            if (go == null)
            {
                go = new GameObject(PouchName);
                KD_SceneObjects.PlaceIfNew(go, KD_DogDefs.SystemsPath);   // 位置に意味が無い
                log.AppendLine("created " + PouchName);
            }
            // **layer は Default。** VRChat のポインタは "UI" レイヤーでは動かない
            go.layer = 0;

            // ---- 実物と当たりを片付ける (2026-09-01 ユーザー決定) ----
            //
            // **手元メニューは板が出る / 消えるだけ。** 腰に提げた革のポーチは
            // VR でも Desktop でも視界の邪魔でしかなく、開く導線 (VR = 手のひらの向き /
            // Desktop = キー) を 1 つも担っていなかった。
            //
            // 当たりも一緒に外す。触る先が無いので Interact は要らないし、
            // **体に追従する当たりは物理でプレイヤーを押しのける** (2026-08-24 Play で実測。
            // trigger にして塞いでいたが、そもそも持たないのが正しい)。
            RemovePouchBody(go, log);

            var pouch = go.GetUdonSharpComponent<KD_TreatPouch>();
            if (pouch == null)
            {
                try { pouch = go.AddUdonSharpComponent<KD_TreatPouch>(); }
                catch (System.Exception e)
                {
                    log.AppendLine("FAIL KD_TreatPouch: " + e.Message
                        + " — 先に 1 - Create Program Assets を流して force refresh すること");
                    return null;
                }
                log.AppendLine("   added KD_TreatPouch");
            }
            KD_ShopSetupEditor.SetSync(pouch, VRC.SDKBase.Networking.SyncType.None, log);

            // ---- 台帳と商品 (**棚と同じ KD_ShopItem を指す** — 値段と在庫の正本を 2 つにしない) ----
            var ledgerGo = KD_SceneObjects.Find(KD_DogDefs.ProgressSystemName, out int ledgerCount);
            var ledger = (ledgerCount == 1 && ledgerGo != null)
                ? ledgerGo.GetUdonSharpComponent<KD_ProgressLedger>() : null;
            if (ledger == null)
                log.AppendLine("   FAIL 台帳が取れない (" + KD_DogDefs.ProgressSystemName
                    + " が " + ledgerCount + " 個) — 先に Tools/KominkaHillside/Menu/Imported-Unused/Progress System/2 - Setup Progress System");

            int n = KD_ShopSetupEditor.TreatProductCount;
            var items = new KD_ShopItem[n];
            for (int i = 0; i < n; i++)
            {
                string itemName = KD_ShopSetupEditor.TreatItemNameAt(i);
                var itemGo = KD_SceneObjects.Find(itemName, out int itemCount);
                items[i] = (itemCount == 1 && itemGo != null)
                    ? itemGo.GetUdonSharpComponent<KD_ShopItem>() : null;
                if (items[i] == null)
                    log.AppendLine("   FAIL 商品が取れない: " + itemName + " (" + itemCount
                        + " 個) — 先に Tools/KominkaHillside/Menu/4 Setup Shop");
            }

            // ---- おもちゃタブ (IMP-FET-011) ----
            //
            // **読むだけ。** Fetch のコードにもボールのプレハブにも 1 行も足さないので、
            // `Fetch System/3 - Verify` の「UdonBehaviour ちょうど 1 個」が
            // 無改造のまま回帰ゲートとして使える
            var defs = KD_DogDefs.All;
            var balls = new KD_BallController[defs.Length];
            var fetchers = new KD_DogFetchController[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                var ballGo = KD_SceneObjects.Find(defs[i].BallName, out int ballCount);
                balls[i] = (ballCount == 1 && ballGo != null)
                    ? ballGo.GetUdonSharpComponent<KD_BallController>() : null;
                if (balls[i] == null)
                    log.AppendLine("   FAIL ボールが取れない: " + defs[i].BallName
                        + " (" + ballCount + " 個) — 先に Tools/KominkaHillside/Menu/Imported-Unused/Fetch System/2 - Setup Fetch System");

                var fetchGo = KD_SceneObjects.Find(defs[i].FetchSystemName, out int fetchCount);
                fetchers[i] = (fetchCount == 1 && fetchGo != null)
                    ? fetchGo.GetUdonSharpComponent<KD_DogFetchController>() : null;
                if (fetchers[i] == null)
                    log.AppendLine("   FAIL Fetch が取れない: " + defs[i].FetchSystemName
                        + " (" + fetchCount + " 個) — 「犬が遊んでいるか」を読めないので "
                        + "その行は常に「よみこみ中」になる");
            }

            // ---- なでタブ (IMP-PET-007) ----
            //
            // **切り替えるだけ。買わない。** 押すとカタログの選択が変わり、
            // 既存の `KD_PetFxBox._OnShopSelectionChanged` がそのまま走る
            var catalogGo = KD_SceneObjects.Find(KD_DogDefs.ShopCatalogName, out int catCount);
            var catalog = (catCount == 1 && catalogGo != null)
                ? catalogGo.GetUdonSharpComponent<KD_ShopCatalog>() : null;
            if (catalog == null)
                log.AppendLine("   FAIL カタログが取れない (" + KD_DogDefs.ShopCatalogName
                    + " が " + catCount + " 個) — 撫でエフェクトを切り替えられない");

            var fxBoxGo = KD_SceneObjects.Find(KD_DogDefs.PetFxBoxName, out int fxBoxCount);
            var fxBox = (fxBoxCount == 1 && fxBoxGo != null)
                ? fxBoxGo.GetUdonSharpComponent<KD_PetFxBox>() : null;
            if (fxBox == null)
                log.AppendLine("   FAIL KD_PetFxBox が取れない (" + fxBoxCount
                    + " 個) — 「使用中」が出せない");

            int fxN = KD_ShopSetupEditor.PetFxProductCount;
            var fxItems = new KD_ShopItem[fxN];
            for (int i = 0; i < fxN; i++)
            {
                string fxName = KD_ShopSetupEditor.PetFxItemNameAt(i);
                var fxGo = KD_SceneObjects.Find(fxName, out int fxCount);
                fxItems[i] = (fxCount == 1 && fxGo != null)
                    ? fxGo.GetUdonSharpComponent<KD_ShopItem>() : null;
                if (fxItems[i] == null)
                    log.AppendLine("   FAIL 撫でエフェクトの商品が取れない: " + fxName
                        + " (" + fxCount + " 個) — 先に Tools/KominkaHillside/Menu/4 Setup Shop");
            }

            // ---- ねるタブ (IMP-DOG-019 Phase 5) ----
            //
            // **台帳を読むだけ。** 権限も占有も `KD_SleepBoard` が正本で、板は
            // `IsLocalAllowedToCall` / `IsLocalController` / `IsSleeping` を引くだけ。
            // 犬にも枠にも 1 行も足さないので、`Sleep System/3 - Verify` は無改造で通る。
            //
            // **配線が欠けるとねるタブが死ぬだけでは済まない** — おもちゃタブの
            // `∧ !sleeping` も一緒に効かなくなり、**寝ている犬のボールに [呼ぶ] が出る**
            var sleepGo = KD_SceneObjects.Find(KD_DogDefs.SleepBoardName, out int sleepCount);
            var sleepBoard = (sleepCount == 1 && sleepGo != null)
                ? sleepGo.GetUdonSharpComponent<KD_SleepBoard>() : null;
            if (sleepBoard == null)
                log.AppendLine("   FAIL " + KD_DogDefs.SleepBoardName + " が取れない ("
                    + sleepCount + " 個) — ねるタブが使えず、"
                    + "**寝ている犬のボールに [呼ぶ] が出る**。"
                    + "先に Tools/KominkaHillside/Menu/6 Setup Sleep");

            // ---- 板 ----
            var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(
                KD_ShopSetupEditor.SharedFontPath);
            if (font == null) log.AppendLine("   WARN font asset が無い (板が豆腐になる)");
            BuildPouchPanel(go, pouch, items, fxItems, font, log);

            pouch.box = box;
            pouch.ledger = ledger;
            pouch.items = items;
            pouch.balls = balls;
            pouch.toyFetchers = fetchers;
            pouch.catalog = catalog;
            pouch.petFxBox = fxBox;
            pouch.fxItems = fxItems;
            pouch.sleepBoard = sleepBoard;
            // **既定値をコードで変えてもシーンには効かない** (シーン保存値が勝つ) ので、
            // 設計値は Setup が毎回明示的に書く (`vrc-udonsharp` §5 の注意と同じ)
            pouch.panelDistance = 0.95f;
            pouch.recallDistance = 1.2f;
            pouch.recallCooldown = 2.5f;
            TryCopy(pouch, log);

            // **文言をフォントへ焼く。** Static フォントなので焼かないと実機で豆腐になる
            // (エディタでは Dynamic 焼きで見えてしまうので、目視では気づけない)
            KD_ShopSetupEditor.AddGlyphs(PouchWords(), log);
            return pouch;
        }

        /// <summary>IMP-PRG-005: ライブタブに出る曲名 (`KD_DogDefs.LiveShows`) と商品名 (`LiveProducts`) と Pack 札。</summary>
        static string LiveWords()
        {
            var sb = new StringBuilder();
            foreach (var s in KD_DogDefs.LiveShows) sb.Append(s.title);
            foreach (var p in KD_DogDefs.LiveProducts) sb.Append(p.displayName);
            sb.Append(KD_TreatPouch.WordLivePack);
            return sb.ToString();
        }

        /// <summary>DES-DOG-004: かざりタブに出る品目名と装着枠名。**表は KD_AccessorySetupEditor が正本。**</summary>
        static string AccessoryWords()
        {
            return KD_AccessorySetupEditor.MenuWords();
        }

        /// <summary>**フォントへ焼く字の正本。** 文言そのものは `KD_TreatPouch` の
        /// const 群が持ち、ここでは連結するだけ (書き写すと足し忘れが起きる)。</summary>
        internal static string PouchWords()
        {
            // **犬の名前もここで焼く** (IMP-FET-011)。おもちゃタブの行に出るのは
            // `KD_DogDef.displayName` そのもので、これまで板に出たことが無い字
            // (「ポメラニアン」の「ポ」等) が混じる
            var dogNames = new StringBuilder();
            foreach (var d in KD_DogDefs.All) dogNames.Append(d.displayName);

            return dogNames.ToString()
                + KD_TreatPouch.WordTitle + KD_TreatPouch.WordDispense
                + KD_TreatPouch.WordToyTitle + KD_TreatPouch.WordFxTitle + KD_TreatPouch.WordSleepTitle
                + KD_TreatPouch.WordLiveTitle + KD_TreatPouch.WordAccTitle + KD_TreatPouch.WordApplied
                + KD_TreatPouch.WordLivePending + KD_TreatPouch.WordLiveRejected
                + KD_TreatPouch.WordOpenOn + KD_TreatPouch.WordOpenOff
                + KD_ShopShelf.WordBought + KD_ShopShelf.WordCantBuy + KD_ShopShelf.WordCantSelect
                + KD_TreatPouch.WordPutAway
                + KD_TreatPouch.WordSlotsFull + KD_TreatPouch.WordAlreadyOut
                + KD_TreatPouch.WordNoStock + KD_TreatPouch.WordBuyAtShelf
                + KD_TreatPouch.WordDispensing + KD_TreatPouch.WordHolding
                + KD_TreatPouch.WordRecalling + KD_TreatPouch.WordSlotLost
                + KD_TreatPouch.WordCommitFailed
                // **Desktop の「T で開く・閉じる」。キー名の英字も焼く** —
                // `KeyCode.ToString()` が出す字なので、日本語だけ焼くとキー名が豆腐になる
                // (Tab → T へ変えた 2026-09-15 以降も、下の英字列にキー名が含まれていること)
                + KD_TreatPouch.WordToggleHint + KD_TreatPouch.WordClose
                + "TabEscapeF1234567890"
                // IMP-TRT-006: Touch の起動ボタン
                + KD_TreatPouch.WordTouchLauncher
                // ---- IMP-TRT-004 / FET-011 / PET-007 で足した文言 ----
                // **ここへ足し忘れると実機で豆腐になる。** エディタは Dynamic 焼きで
                // 見えてしまうので、目視では絶対に気づけない (`vrc-tmp-font` §3)
                + KD_TreatPouch.WordSettings + KD_TreatPouch.WordDogVoice + KD_TreatPouch.WordVoiceOn
                + KD_TreatPouch.WordVoiceOff + KD_TreatPouch.WordVoiceKinds + KD_TreatPouch.WordSoundScope
                + KD_TreatPouch.WordVoiceVolume + "%"
                // ---- IMP-ENV-009 (BGM の節) ----
                + KD_TreatPouch.WordBgm + KD_TreatPouch.WordBgmNote
                + KD_TreatPouch.WordVolumeSettings + KD_TreatPouch.WordBackSettings + KD_TreatPouch.WordLiveVolume
                + KD_DistanceFadeSettingsEditor.Words()
                + KD_SleepPlacementEditor.Words()
                + KD_TreatPouch.WordOutside + KD_TreatPouch.WordBedroom + KD_TreatPouch.WordBedroomNote + KD_TreatPouch.WordVolumeScope
                + KD_TreatPouch.WordTabTreat + KD_TreatPouch.WordTabToy
                + KD_TreatPouch.WordTabPetFx
                + KD_TreatPouch.WordRecall + KD_TreatPouch.WordHeldByOther
                + KD_TreatPouch.WordDogBusy + KD_TreatPouch.WordEquip
                + KD_TreatPouch.WordBuyHere
                // ---- IMP-DOG-019 Phase 5 (ねるタブ) ----
                + KD_TreatPouch.WordTabSleep
                + KD_TreatPouch.WordSleepCall + KD_TreatPouch.WordSleepWake
                + KD_TreatPouch.WordSleepingSelf + KD_TreatPouch.WordSleepingSuffix
                + KD_TreatPouch.WordSleepingSomeone
                + KD_TreatPouch.WordSleepOpenToAll + KD_TreatPouch.WordSleepOwnerOnly
                // ---- IMP-PRG-004 Phase 4 (ライブタブ) ----
                + KD_TreatPouch.WordTabLive + KD_TreatPouch.WordLiveHost
                + KD_TreatPouch.WordLiveHostingSuffix + KD_TreatPouch.WordLiveHostingSomeone
                + KD_TreatPouch.WordLiveRemain + KD_TreatPouch.WordLiveWait
                + KD_TreatPouch.WordLiveSeconds + KD_TreatPouch.WordLiveReady
                // ---- 行の曲名・確認帯の商品名・Pack札。日本語の新曲名も宣言文字へ含める。
                // **文言の正本 (KD_DogDefs) が変わっても焼き忘れないよう**ここで連結する
                + LiveWords()
                // ---- DES-DOG-004 (かざりタブ) ----
                // **足し忘れると実機で豆腐になる。** 2026-09-07 に 1 度踏んだ —
                // エディタは Dynamic 焼きで見えるので目視では気づけず、
                // 「文字が出ない」で初めて分かる (`vrc-tmp-font` §3)。
                // 装着枠名 (あたま / かお / むね / からだ / しっぽ) と品目名も忘れずに
                + KD_TreatPouch.WordTabAcc + KD_TreatPouch.WordAccChoose + KD_TreatPouch.WordTabPlay
                + KD_TreatPouch.WordLiveMore + KD_TreatPouch.WordLiveSeeFx + KD_TreatPouch.WordLiveSeeAcc
                + "▶"   // 残高の押せる印 ▶ (2026-09-17)
                + KD_TreatPouch.WordAccBack + KD_TreatPouch.WordAccPut
                + KD_TreatPouch.WordAccTakeOff + KD_TreatPouch.WordAccReplaces
                + KD_TreatPouch.WordAccWaitPrefix + KD_TreatPouch.WordAccWaitSuffix
                + KD_TreatPouch.WordAccNotOwned
                + AccessoryWords()
                // 切り詰めの印。**日本語 1 字ぶんの幅を持つ** ので寸法の見積りにも効く
                + KD_TreatPouch.NameEllipsis
                // ★ **ASCII 印字可能を全字焼く** (0x20〜0x7E)。
                // ねるタブは `〇〇さんと寝ている` に**プレイヤーの表示名**を出す —
                // 任意の文字列なので焼ける集合を先に決められない。だから板は
                // **全字が ASCII の名前だけを出す** (`KD_TreatPouch.AsciiNameOrNull`) 契約にし、
                // その契約が成り立つ根拠をここで焼き、Verify が数える。
                // (棚の Setup も同じ範囲を焼いているが、**実行順に依存させない** —
                // 手元メニューが出す字は手元メニューの Setup が保証する)
                + Ascii()
                // 板は棚の文言も借りる (同じ概念に別の字を当てない)
                + KD_ShopShelf.WordCount + KD_ShopShelf.WordRemain + KD_ShopShelf.WordShort + KD_ShopShelf.WordLoading
                + KD_ShopShelf.WordInUse + KD_ShopShelf.WordSpecial
                // 確認 (2 段階購入)
                + KD_ShopShelf.WordBuy + KD_ShopShelf.WordCancel
                // P13: 確認カードの固定文 (文言表が正本。焼き忘れると実機で豆腐になる)
                + ConfirmWords();
        }

        /// <summary>P13 確認カードの JP 文言 (UITranslations.json の confirm.*)。</summary>
        static string ConfirmWords()
        {
            var catalog = KD_LocalizationEditor.Load();
            var sb = new StringBuilder();
            foreach (var key in new[] { "confirm.buyTitle", "confirm.storeTitle", "confirm.storeNote", "confirm.remaining", "confirm.short", "confirm.storeButton" })
                sb.Append(KD_LocalizationEditor.Text(catalog, "ja", key));
            sb.Append("Credits");
            return sb.ToString();
        }

        /// <summary>ASCII 印字可能 (0x20〜0x7E) の全字。**表示名を出す契約の根拠。**</summary>
        static string Ascii()
        {
            var sb = new StringBuilder();
            for (char c = ' '; c <= '~'; c++) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>**廃止した実物と当たりを片付ける** (2026-09-01)。
        ///
        /// Setup は「作るだけ」が原則だが、**やめた物を消すのは作った側の後始末**で、
        /// ここでやらないと既にシーンへ置いた分が誰にも消されずに残る。
        /// 既に無ければ何もしない (再実行の冪等性は保つ)。</summary>
        static void RemovePouchBody(GameObject root, StringBuilder log)
        {
            var t = root.transform.Find(PouchBodyName);
            if (t != null)
            {
                Object.DestroyImmediate(t.gameObject, true);
                log.AppendLine("removed " + PouchBodyName + " (実物は廃止。板だけを出す)");
            }

            // 見た目を root へ直接付けていた場合も落とす (過去版の取りこぼし対策)
            foreach (var mr in root.GetComponents<MeshRenderer>()) Object.DestroyImmediate(mr, true);
            foreach (var mf in root.GetComponents<MeshFilter>()) Object.DestroyImmediate(mf, true);

            int cols = 0;
            foreach (var c in root.GetComponents<Collider>())
            {
                Object.DestroyImmediate(c, true);
                cols++;
            }
            if (cols > 0)
                log.AppendLine("removed Collider x" + cols + " from " + root.name
                    + " (触る先が無い。**体に追従する当たりはプレイヤーを押しのける**)");
        }

        // ================================================================ IMP-TRT-004
        // 2026-09-09 実機での要望: 横1.5倍、縦1.2〜1.5倍で情報量を増やす。
        // 旧760x520の上限を更新。2列3段を表示し、追加行はスクロールで受ける。
        public const float PanelWidth = 1140f;
        public const float PanelHeight = 700f;
        /// <summary>ユーザー指定範囲内で採用した高さを Verify で維持する。</summary>
        public const float PanelHeightLimit = 700f;

        /// <summary>タブの並び順に対応する見出し。**添字 = `KD_TreatPouch.Tab*`。**</summary>
        static readonly string[] TabWords = {
            KD_TreatPouch.WordTabTreat, KD_TreatPouch.WordTabToy, KD_TreatPouch.WordTabPetFx,
            KD_TreatPouch.WordTabSleep, KD_TreatPouch.WordTabLive, KD_TreatPouch.WordTabAcc, KD_TreatPouch.WordTabPlay };
        // Page_Play (あそぶ。P2) の中身は Activities/2 - Build が作る。ここでは空の面だけ用意する
        // 裏山改変: タブ 2 は さんぽ (Page_Walk)。撫でFX は かざり (Page_Acc) に合流 (D-2)
        static readonly string[] TabPageNames = {
            "Page_Treat", "Page_Toy", "Page_Walk", "Page_Sleep", "Page_Live", "Page_Acc", "Page_Play" };

        // ---- タブバーの寸法 (IMP-DOG-019 Phase 5 で 3 → 4 枚、IMP-PRG-004 Phase 4 で 5 枚) ----
        //
        // **板は横にも広げない。**タブ 1 枚を 200 → 180 → 140px へ詰め、間隔を 215 → 185 → 148 にして
        // 5 枚を 1 段に収める (5 枚 × 148 = 740px 幅 ≒ 板 760px の内側)。
        // 縦へ折り返すと**行の面が 56px 削れる** ので、詰めるのは横だけ。
        // 見出しは全部 4 字以内 (おやつ / おもちゃ / なで / ねる / ライブ) なので 140px (28pt × 4 = 112px) に入る
        // DES-DOG-004 で 6 枚目 (かざり)。**板は横にも広げない** — 140 -> 120px / 148 -> 123 へ詰め、
        // 6 枚 x 123 = 738px で 5 枚のときの 740px と同じ幅に収めた。
        // 見出しは 4 字が上限のまま (28pt x 4 = 112px < 120px)。かざりは 3 字なので余裕がある
        internal const string EventBandName = "EventBand";
        internal const float EventBandY = 270f;
        internal const float EventBandHeight = 30f;
        const float TabWidth = 120f;
        const float TabStep = 123f;
        const float TabHeight = 56f;
        const float TabBarY = 160f;

        /// <summary>板を組む。**既定は非アクティブ** — 開いたまま保存すると
        /// 入室した瞬間から板が浮き、撮影の許可レイヤーに active な Canvas が増える。
        ///
        /// **構成 (IMP-TRT-004)**:
        /// <code>
        /// ┌ もっている 12 pt                       [とじる] ┐  ← タブ外・常時
        /// ├────────────────────────────────────────────────┤
        /// │ [おやつ] [おもちゃ] [なで]                      │  ← タブバー (角あり・下線)
        /// ├────────────────────────────────────────────────┤
        /// │ 2 列グリッド (おやつ 4 / おもちゃ 4 / なで 6)   │  ← 表示中の 1 枚だけ active
        /// └────────────────────────────────────────────────┘
        ///   T で開く・閉じる                                        ← タブ外 (Desktop のみ)
        /// </code></summary>
        internal static void BuildPouchPanel(GameObject root, KD_TreatPouch pouch,
            KD_ShopItem[] items, KD_ShopItem[] fxItems, TMPro.TMP_FontAsset font, StringBuilder log)
        {
            // 裏山改変: 段階 0 は items / fxItems が無い (null)。おやつの行は器の本数 (4) だけ作り、
            // 撫でFX の行は HillsideBuildPages が かざりの Content に作る (旧 PetFx 頁には作らない)
            if (items == null || items.Length == 0) items = new KD_ShopItem[KD_ShopSetupEditor.TreatProductCount];
            fxItems = new KD_ShopItem[0];
            var t = root.transform.Find(PouchPanelName);
            GameObject go = t != null ? t.gameObject : null;
            if (go == null)
            {
                go = new GameObject(PouchPanelName, typeof(RectTransform));
                go.transform.SetParent(root.transform, false);
                log.AppendLine("created " + PouchPanelName);
            }
            go.layer = KD_HandMenuCollisionEditor.InputLayer; // Walkthrough: UI pointer without player collision.
            KD_HandMenuScrollEditor.Unwrap(go.transform);
            go.transform.localPosition = PouchPanelOffset;
            go.transform.localRotation = Quaternion.identity;

            var canvas = go.GetComponent<Canvas>();
            if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (go.GetComponent<UnityEngine.UI.CanvasScaler>() == null)
                go.AddComponent<UnityEngine.UI.CanvasScaler>();
            if (go.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            if (go.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null)
                go.AddComponent<VRC.SDK3.Components.VRCUiShape>();

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            // Canvas の 1 単位 = 1px なので、ワールドで 0.76m にするには 0.001 倍
            rect.localScale = Vector3.one * 0.001f;

            var bg = KD_ShopSetupEditor.EnsureUiChild(go.transform, "Bg",
                Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            var bgImg = bg.GetComponent<UnityEngine.UI.Image>();
            if (bgImg == null) bgImg = bg.AddComponent<UnityEngine.UI.Image>();
            bgImg.color = KD_UiSpriteEditor.SurfacePanel;
            bgImg.raycastTarget = false;
            // **背景は角丸にしない。** 丸みは押せるものだけの印 (hand-menu §5)
            KD_UiSpriteEditor.ApplyFlatSkin(bgImg);

            var pouchUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
            if (pouchUdon == null)
                log.AppendLine("   WARN backing UdonBehaviour が取れない — "
                    + "onClick を配線できない。force refresh 後に再実行");

            var round = KD_UiSpriteEditor.LoadRound();
            if (round == null)
                log.AppendLine("   WARN 角丸スプライトが無い — 先に "
                    + "Tools/KominkaHillside/Menu/1 Bake UI Sprite を流すこと "
                    + "(無くても動くが、意匠は直角矩形のまま)");
            var circle = KD_UiSpriteEditor.LoadCircle();
            if (circle == null)
                log.AppendLine("   WARN 円スプライトが無い — 先に "
                    + "Tools/KominkaHillside/Menu/1 Bake UI Sprite を流すこと "
                    + "(無いとおもちゃの色スウォッチが玉に見えない)");

            // ================================================================
            // タブの外 — **どのタブに居ても読める**
            // ================================================================

            var sectionTitle = EnsureText(go.transform, "SectionTitle", new Vector2(-222.5f, 220f),
                new Vector2(255f, 48f), 26f, TMPro.TextAlignmentOptions.Left, font);
            sectionTitle.text = KD_Hillside.Text("KD_TreatPouch.WordWalkTitle", "犬とさんぽ", log);   // 裏山改変: 既定タブは さんぽ
            var balance = EnsureText(go.transform, "Balance", new Vector2(15f, 220f),
                new Vector2(200f, 48f), 22f, TMPro.TextAlignmentOptions.Right, font);
            var feedback = EnsureText(go.transform, "Feedback", new Vector2(0f, -200f),
                new Vector2(700f, 40f), 24f, TMPro.TextAlignmentOptions.Left, font);
            feedback.gameObject.SetActive(false);

            // **[とじる] は板の右上に常設** (2026-09-03。IMP-TRT-002)。
            // VR は右スティック長押しでも閉じられるが、**板の中に閉じる道が無いと
            // 操作を覚えるまで閉じられない**。[しまう] とは別物で、枠にも在庫にも触らない
            var closeBtn = RoundButton(go.transform, "CloseButton",
                new Vector2(310f, 220f), new Vector2(110f, 48f),
                KD_UiSpriteEditor.ActionClose, font, KD_TreatPouch.WordClose,
                pouchUdon, "_Close", round, log);

            KD_DogVoiceSettingsEditor.Build(pouch, go.transform, font, pouchUdon, log);

            // **Desktop 用の「T で開く・閉じる」。VR では実行時に隠す** —
            // VR は右スティック長押しで閉じられるので、覚えるべき操作を増やさない
            var closeHint = EnsureText(go.transform, "CloseHint", new Vector2(-250f, -238f),
                new Vector2(260f, 34f), 22f, TMPro.TextAlignmentOptions.Left, font);

            // ================================================================
            // IMP-TRT-006: Touch の起動ボタン (板の兄弟。Screen-Space Overlay)
            // ================================================================
            var launcher = EnsureTouchLauncher(root.transform, font, pouchUdon, round, log);

            // ================================================================
            // タブバー — **角あり・枠なし + 下線。** 押せる面ではなく見出し
            // ================================================================

            int tabs = KD_TreatPouch.TabCount;
            var tabImages = new UnityEngine.UI.Image[tabs];
            var tabUnderlines = new GameObject[tabs];
            // **中央そろえで並べる。** 枚数を足しても左端だけが伸びる形にしない
            float tabLeft = -TabStep * (tabs - 1) * 0.5f;
            for (int i = 0; i < tabs; i++)
            {
                EnsureTab(go.transform, "Tab" + i.ToString(),
                    new Vector2(tabLeft + i * TabStep, TabBarY), new Vector2(TabWidth, TabHeight),
                    font, i == KD_TreatPouch.TabWalk ? KD_Hillside.Text("KD_TreatPouch.WordWalkTab", "さんぽ", log) : TabWords[i],   // 裏山改変
                    pouchUdon, "_Tab" + i.ToString(), log,
                    out tabImages[i], out tabUnderlines[i]);
                var tabLabel = tabImages[i].transform.Find("Label").GetComponent<TMPro.TextMeshProUGUI>();
                tabLabel.fontSize = 26f;
                tabLabel.rectTransform.sizeDelta = new Vector2(108f, 48f);
            }

            // ================================================================
            // ページ — **表示中の 1 枚だけ active。** 実行時に ApplyTab が切り替える
            // ================================================================

            var pages = new GameObject[tabs];
            for (int i = 0; i < tabs; i++)
            {
                pages[i] = KD_ShopSetupEditor.EnsureUiChild(go.transform, TabPageNames[i],
                    Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            }

            // ---- おやつ (2 列 × 2 段) ----
            int n = items == null ? 0 : items.Length;
            if (n > 4)
            {
                log.AppendLine("   FAIL おやつが " + n + " 種ある - 板の口 (_Row0.._Row3) は 4 つまで。"
                    + "増やすなら KD_TreatPouch へ _Row4 を足すこと (行を黙って落とさない)");
                n = 4;
            }
            var rowLabels = new TMPro.TextMeshProUGUI[n];
            var rowHints = new TMPro.TextMeshProUGUI[n];
            var rowButtons = new GameObject[n];
            for (int i = 0; i < n; i++)
            {
                var row = KD_ShopSetupEditor.EnsureUiChild(pages[KD_TreatPouch.TabTreat].transform,
                    "Row" + i.ToString(), GridPos(i, 68f, -42f), new Vector2(340f, 100f));
                rowLabels[i] = EnsureText(row.transform, "Label", new Vector2(0f, 26f),
                    new Vector2(330f, 44f), 25f, TMPro.TextAlignmentOptions.Left, font);
                rowHints[i] = EnsureText(row.transform, "Hint", new Vector2(75f, -26f),
                    new Vector2(180f, 40f), 20f, TMPro.TextAlignmentOptions.Left, font);
                rowButtons[i] = RoundButton(row.transform, "Button",
                    new Vector2(-100f, -26f), new Vector2(130f, 44f),
                    KD_UiSpriteEditor.ActionPrimary, font, KD_TreatPouch.WordDispense,
                    pouchUdon, "_Row" + i.ToString(), round, log);
            }

            var status = EnsureText(pages[KD_TreatPouch.TabTreat].transform, "Status",
                new Vector2(0f, -160f), new Vector2(700f, 44f), 26f,
                TMPro.TextAlignmentOptions.Left, font);

            // **[しまう] は出している間だけ出す。** 犬が来なくても枠が返せる —
            // 呼ぶのは本番と同じ KD_TreatSlot._Release() で、テスト専用の経路ではない
            var putAway = RoundButton(pages[KD_TreatPouch.TabTreat].transform, "PutAwayButton",
                new Vector2(240f, -212f), new Vector2(220f, 58f),
                KD_UiSpriteEditor.ActionNeutral, font, KD_TreatPouch.WordPutAway,
                pouchUdon, "_PutAway", round, log);

            // ---- おもちゃ (2 列 × 2 段。犬 1 匹 = 1 行) ----
            //
            // **名前は犬の名前だけ**にする — タブが「おもちゃ」なので「のボール」は要らないし、
            // 340px の行に理由まで入れると溢れる。**識別層と同じ色の丸**を頭に置く
            // (ボールの識別は色と大きさが主役 — IMP-FET-009)
            var defs = KD_DogDefs.All;
            int toyCount = defs.Length;
            // IMP-DOG-020: 口は 6 つ (_Toy0.._Toy5 / _Sleep0.._Sleep5)。2 列 × 3 段で板の縦 520px に収まる上限
            if (toyCount > 6)
            {
                log.AppendLine("   FAIL 犬が " + toyCount + " 匹いる - 板の口 (_Toy0.._Toy5 / _Sleep0.._Sleep5) は 6 つまで。"
                    + "7 匹目を足すときは KD_TreatPouch へ _Toy6 / _Sleep6 を足し、板の置き方 (2 列 × 3 段) を考え直すこと");
                toyCount = 6;
            }
            var toyLabels = new TMPro.TextMeshProUGUI[toyCount];
            var toyReasons = new TMPro.TextMeshProUGUI[toyCount];
            var toyButtons = new GameObject[toyCount];
            var toyButtonComponents = new UnityEngine.UI.Button[toyCount];
            var toyButtonImages = new UnityEngine.UI.Image[toyCount];
            var toyNames = new string[toyCount];
            for (int i = 0; i < toyCount; i++)
            {
                toyNames[i] = defs[i].displayName;
                var row = KD_ShopSetupEditor.EnsureUiChild(pages[KD_TreatPouch.TabToy].transform,
                    "ToyRow" + i.ToString(), GridPos(i, 68f, -42f), new Vector2(340f, 100f));

                // 色の丸。**実行時には触らない**ので Setup が焼き切る
                var swatch = KD_ShopSetupEditor.EnsureUiChild(row.transform, "Swatch",
                    new Vector2(-148f, 26f), new Vector2(26f, 26f));
                var swImg = swatch.GetComponent<UnityEngine.UI.Image>();
                if (swImg == null) swImg = swatch.AddComponent<UnityEngine.UI.Image>();
                swImg.color = defs[i].ballColor;
                swImg.raycastTarget = false;
                // **専用の円スプライト** (IMP-TRT-005)。角丸スプライトの流用は
                // 半径を 12px へ下げた時点で「玉」が角丸四角に化けた
                KD_UiSpriteEditor.ApplyCircleSkin(swImg, circle);

                toyLabels[i] = EnsureText(row.transform, "Label", new Vector2(20f, 26f),
                    new Vector2(270f, 42f), 24f, TMPro.TextAlignmentOptions.Left, font);
                toyButtons[i] = RoundButton(row.transform, "Button",
                    new Vector2(-100f, -26f), new Vector2(130f, 44f),
                    KD_UiSpriteEditor.ActionPrimary, font, KD_TreatPouch.WordRecall,
                    pouchUdon, "_Toy" + i.ToString(), round, log);
                toyButtonComponents[i] = toyButtons[i].GetComponent<UnityEngine.UI.Button>();
                toyButtonImages[i] = toyButtons[i].GetComponent<UnityEngine.UI.Image>();

                // **理由は別ラベル。** 名前と同じ行に押し込むと 340px を溢れる
                toyReasons[i] = EnsureText(row.transform, "Reason", new Vector2(75f, -26f),
                    new Vector2(180f, 40f), 18f, TMPro.TextAlignmentOptions.Left, font);
            }

            // ---- パーティクル (2列スクロール、既存6種＋同梱5種) ----
            int fxCount = fxItems == null ? 0 : fxItems.Length;
            if (fxCount > 11)
            {
                log.AppendLine("   FAIL 撫でエフェクトが " + fxCount + " 種ある - "
                    + "板の口 (_Fx0.._Fx10) は11まで。追加時はイベント口を増やすこと");
                fxCount = 11;
            }
            var fxLabels = new TMPro.TextMeshProUGUI[fxCount];
            var fxSubLabels = new TMPro.TextMeshProUGUI[fxCount];
            var fxButtons = new GameObject[fxCount];
            var fxImages = new UnityEngine.UI.Image[fxCount];
            var fxButtonLabels = new TMPro.TextMeshProUGUI[fxCount];
            for (int i = 0; i < fxCount; i++)
            {
                float x = (i % 2 == 0) ? -180f : 180f;
                float y = 78f - (i / 2) * 100f;
                var row = KD_ShopSetupEditor.EnsureUiChild(pages[KD_TreatPouch.TabPetFx].transform,
                    "FxRow" + i.ToString(), new Vector2(x, y), new Vector2(340f, 92f));
                fxLabels[i] = EnsureText(row.transform, "Label", new Vector2(0f, 20f),
                    new Vector2(330f, 40f), 22f, TMPro.TextAlignmentOptions.Left, font);
                fxSubLabels[i] = EnsureText(row.transform, "Sub", new Vector2(50f, -22f),
                    new Vector2(170f, 44f), 18f, TMPro.TextAlignmentOptions.Left, font);
                fxButtons[i] = RoundButton(row.transform, "Button",
                    new Vector2(-100f, -22f), new Vector2(130f, 44f),
                    KD_UiSpriteEditor.ActionEquip, font, KD_TreatPouch.WordEquip,
                    pouchUdon, "_Fx" + i.ToString(), round, log);
                fxImages[i] = fxButtons[i].GetComponent<UnityEngine.UI.Image>();
                var bl = fxButtons[i].transform.Find("Label");
                fxButtonLabels[i] = bl == null ? null
                    : bl.GetComponent<TMPro.TextMeshProUGUI>();
            }

            // ---- 撫でエフェクトの確認 ----
            //
            // **押した瞬間には払わない。** 触るのは確認を開くだけで、pt が減るのは
            // [購入] を押したときだけ — 棚の確認パネルと同じ規律を、
            // 板の形が違っても崩さない (「売り場の形はカテゴリで違ってよいが、確認の型は 1 つ」)。
            //
            // ⚠ **ここが「購入をメニューへ入れない」の唯一の例外。**
            // 撫でエフェクトの `KD_ShopItem` は `menuOnly` で Collider を持たないので、
            // 廃止した近接メニューが**唯一の購入口**だった。口が 2 本になるわけではなく、
            // **廃止した板の仕事をそのまま引き継いでいる** (2026-09-03 ユーザー決定)。
            // P13 (2026-09-16): 確認の面はタブ内の帯ではなく板の手前の共有カード (EnsureConfirmPanel)。旧帯は消す
            RetireLegacy(pages[KD_TreatPouch.TabPetFx].transform, "FxConfirm", log);

            // ---- ねる (2 列 × 3 段。犬 1 匹 = 1 行。IMP-DOG-020 で 2 段 → 3 段) ----
            //
            // **並びも行頭の丸もおもちゃタブと同じ** (`dogIndex` 順 / ボールの色) —
            // 同じ犬を指す手がかりを板の中でタブごとに変えない。
            //
            // 行が 2 行ぶんの高さを持つのはここだけ: `〇〇さんと寝ている` に
            // **表示名が入る**ので、おもちゃの理由ラベル (180px) では溢れる。
            // 状態を 2 行目に丸ごと置いて 330px 取り、ボタンを 3 行目へ落とす
            var sleepLabels = new TMPro.TextMeshProUGUI[toyCount];
            var sleepStatus = new TMPro.TextMeshProUGUI[toyCount];
            var sleepButtons = new GameObject[toyCount];
            var sleepButtonComponents = new UnityEngine.UI.Button[toyCount];
            var sleepButtonImages = new UnityEngine.UI.Image[toyCount];
            var sleepButtonLabels = new TMPro.TextMeshProUGUI[toyCount];
            for (int i = 0; i < toyCount; i++)
            {
                var row = KD_ShopSetupEditor.EnsureUiChild(pages[KD_TreatPouch.TabSleep].transform,
                    "SleepRow" + i.ToString(), SleepGridPos(i), new Vector2(340f, SleepRowHeight));

                // 行の中身は 3 段 (名前 +32 / 状態 +1 / ボタン -32)。IMP-DOG-020 で 124px → 104px に詰めた
                var swatch = KD_ShopSetupEditor.EnsureUiChild(row.transform, "Swatch",
                    new Vector2(-148f, 32f), new Vector2(26f, 26f));
                var swImg = swatch.GetComponent<UnityEngine.UI.Image>();
                if (swImg == null) swImg = swatch.AddComponent<UnityEngine.UI.Image>();
                swImg.color = defs[i].ballColor;
                swImg.raycastTarget = false;
                KD_UiSpriteEditor.ApplyCircleSkin(swImg, circle);

                sleepLabels[i] = EnsureText(row.transform, "Label", new Vector2(20f, 32f),
                    new Vector2(270f, 40f), 24f, TMPro.TextAlignmentOptions.Left, font);
                // **状態は行の 2 行目を丸ごと使う** (330px @19pt)。
                // `さんと寝ている` が 7 字 = 133px なので、表示名に 197px ≒ 半角 16 字残る
                // (`KD_TreatPouch.SleepNameBudget` がこの見積りの正本)
                sleepStatus[i] = EnsureText(row.transform, "Status", new Vector2(0f, 1f),
                    new Vector2(330f, 30f), 19f, TMPro.TextAlignmentOptions.Left, font);

                sleepButtons[i] = RoundButton(row.transform, "Button",
                    new Vector2(-105f, -32f), new Vector2(140f, 40f),
                    KD_UiSpriteEditor.ActionPrimary, font, KD_TreatPouch.WordSleepCall,
                    pouchUdon, "_Sleep" + i.ToString(), round, log);
                sleepButtonComponents[i] = sleepButtons[i].GetComponent<UnityEngine.UI.Button>();
                sleepButtonImages[i] = sleepButtons[i].GetComponent<UnityEngine.UI.Image>();
                // **[一緒に寝る] は 5 字。** 既定の 30pt (= 150px) では 140px の枠を溢れるので、
                // この行のボタンだけ落とす ([起こす] と同じラベルを使い回すため両方に効く)
                sleepButtonLabels[i] = ButtonLabelOf(sleepButtons[i], 22f);
            }

            // [みんなにも開放] は P5 (2026-09-16) で設定ホーム (ライブ開催許可の隣) へ移した (KD_MenuFinishEditor.BuildSettings)。旧ボタンは消す
            RetireLegacy(pages[KD_TreatPouch.TabSleep].transform, "OpenToAllButton", log);

            // **押せない理由の 1 行。** 4 行が暗いだけだと「壊れている」と読める
            var sleepNote = EnsureText(pages[KD_TreatPouch.TabSleep].transform, "Note",
                new Vector2(0f, -240f), new Vector2(700f, 34f), 22f,   // 3 段目 (〜-220) の下。板の底 -260 の内側
                TMPro.TextAlignmentOptions.Center, font);

            // ---- ライブ: 9 曲 (2026-09-16)。基本行を作り、Scroll Layoutが2列の縦スクロールへ配置する ----
            //
            // **台帳 (KD_LiveSystem) と夜 (KD_LiveNight) は読む / 叩くだけ。** 板は判定の式を写さない。
            // **行 = 作品 (`KD_DogDefs.LiveShows`。添字 = liveId)。商品はカタログのカテゴリ 3
            // (`KD_ShopSetupEditor.CategoryLive`) から `productKey` で引く** — Pack の6行は
            // 同じ KD_ShopItem を指す (Codex 指摘 2026-09-06: 商品数を行数にすると Pack が 1 行に潰れる)。
            // 置き方はなでタブと同じ 2 列 × 3 段 (行 92px / ピッチ 100px: 78, -22, -122)
            var liveGo = KD_SceneObjects.Find(KD_DogDefs.LiveSystemName, out int liveCount);
            var liveSys = (liveCount == 1 && liveGo != null) ? liveGo.GetUdonSharpComponent<KD_LiveSystem>() : null;
            if (liveSys == null)
                log.AppendLine("   WARN " + KD_DogDefs.LiveSystemName
                    + " が取れない — ライブタブは暗いまま。先に Tools/KominkaHillside/Menu/Imported-Unused/Live System/2 - Setup Live System");
            var liveCatalog = new System.Collections.Generic.List<KD_ShopItem>();
            {
                var catGo = KD_SceneObjects.Find(KD_DogDefs.ShopCatalogName, out int catN);
                var cat = (catN == 1 && catGo != null) ? catGo.GetUdonSharpComponent<KD_ShopCatalog>() : null;
                if (cat != null && cat.items != null)
                    foreach (var it in cat.items)
                        if (it != null && it.categoryKey == KD_ShopSetupEditor.CategoryLive) liveCatalog.Add(it);
                if (liveCatalog.Count == 0)
                    log.AppendLine("   WARN カタログに Live の商品が無い — 先に Tools/KominkaHillside/Menu/4 Setup Shop");
            }
            var liveShows = KD_DogDefs.LiveShows;
            int liveN = liveShows.Length;
            // 口は 9 つ (_Live0.._Live8。2026-09-16 パンプキン・パレードで 9 本目。RefreshLiveCatalog と同じ上限)
            if (liveN > 9)
            {
                throw new System.InvalidOperationException("Live の作品が " + liveN
                    + " 本ある - 板の口 (_Live0.._Live8) は9つ。イベント口を増やしてから再実行すること");
            }
            var liveItems = new KD_ShopItem[liveN];
            var liveTitles = new string[liveN];
            var liveLabels = new TMPro.TextMeshProUGUI[liveN];
            var liveSubLabels = new TMPro.TextMeshProUGUI[liveN];
            var liveButtons = new GameObject[liveN];
            var liveButtonComponents = new UnityEngine.UI.Button[liveN];
            var liveButtonImages = new UnityEngine.UI.Image[liveN];
            var liveButtonLabels = new TMPro.TextMeshProUGUI[liveN];
            var liveGiftButtons = new GameObject[liveN];
            var liveGiftLabels = new TMPro.TextMeshProUGUI[liveN];
            for (int i = 0; i < liveN; i++)
            {
                liveTitles[i] = liveShows[i].title;
                foreach (var it in liveCatalog)
                    if (it.productKey == liveShows[i].productKey) { liveItems[i] = it; break; }
                if (liveItems[i] == null)
                    log.AppendLine("   WARN [" + i + " " + liveShows[i].slug + "] 商品 (productKey "
                        + liveShows[i].productKey + ") がカタログに無い — この行は暗いまま。先に Shop/2 - Setup Shop");

                float x = (i % 2 == 0) ? -180f : 180f;
                float y = 78f - (i / 2) * 100f;
                var row = KD_ShopSetupEditor.EnsureUiChild(pages[KD_TreatPouch.TabLive].transform,
                    "LiveRow" + i.ToString(), new Vector2(x, y), new Vector2(340f, 92f));
                // 1 行目 = 曲名 (英語。最長 "A Sky Full of Quiet Wishes" 26 字 @22pt ≒ 290px < 330px。Verify が実測する)
                liveLabels[i] = EnsureText(row.transform, "Label", new Vector2(0f, 20f),
                    new Vector2(330f, 40f), 22f, TMPro.TextAlignmentOptions.Left, font);
                liveButtons[i] = RoundButton(row.transform, "Button",
                    new Vector2(-100f, -22f), new Vector2(130f, 44f),
                    KD_UiSpriteEditor.ActionPrimary, font, KD_TreatPouch.WordLiveHost,
                    pouchUdon, "_Live" + i.ToString(), round, log);
                liveButtonComponents[i] = liveButtons[i].GetComponent<UnityEngine.UI.Button>();
                liveButtonImages[i] = liveButtons[i].GetComponent<UnityEngine.UI.Image>();
                liveButtonLabels[i] = ButtonLabelOf(liveButtons[i], 24f);
                // [ギフト] (IMP-PRG-012): 所有済みの買い切り CE の行だけ実行時に出す。灰 = 何も消費せず別の画面を開く。位置は Hand Menu/3 が決める
                liveGiftButtons[i] = RoundButton(row.transform, "Gift",
                    new Vector2(100f, 20f), new Vector2(130f, 40f),
                    KD_UiSpriteEditor.ActionNeutral, font, KD_TreatPouch.WordGift,
                    pouchUdon, "_LiveGift" + i.ToString(), round, log);
                liveGiftLabels[i] = ButtonLabelOf(liveGiftButtons[i], 20f);
                liveGiftButtons[i].SetActive(false);
                // 2 行目 = [Pack] 札 + 値段 / あと N pt (おもちゃタブの理由ラベルと同じ置き方。ボタンの右)
                liveSubLabels[i] = EnsureText(row.transform, "Sub", new Vector2(75f, -22f),
                    new Vector2(180f, 36f), 18f, TMPro.TextAlignmentOptions.Left, font);
            }
            // 状態の 1 行 (〇〇さんが開催中 残り N 秒 / 次の開催まで あと N 秒)。3 段目 (〜-168) の下
            var liveStatus = EnsureText(pages[KD_TreatPouch.TabLive].transform, "Status",
                new Vector2(0f, -184f), new Vector2(700f, 32f), 22f,
                TMPro.TextAlignmentOptions.Center, font);
            // 「アバターも暗くする」は 2026-09-06 に廃止 (層の制限なし)。残っていれば消す
            {
                var legacy = pages[KD_TreatPouch.TabLive].transform.Find("AvatarDarkButton");
                if (legacy != null) { Object.DestroyImmediate(legacy.gameObject); log.AppendLine("   removed AvatarDarkButton (廃止)"); }
            }
            // 確認 (2 段階購入)。P13: 板の手前の共有カード (EnsureConfirmPanel)。**商品名 + 価格** (行の曲名ではない — Pack は総額)。旧帯は消す
            RetireLegacy(pages[KD_TreatPouch.TabLive].transform, "LiveConfirm", log);

            // ---- P7 / P8 (2026-09-16): 一覧の行 = 商品 (KD_DogDefs.LiveProducts の順 = 安い → 高い、CE は最後)。
            //      未所持は [くわしく] → 詳細の面 (LiveDetail)。所持は [開催] (Pack は同じ行で曲を選ぶ面) ----
            var liveProducts = new KD_ShopItem[KD_DogDefs.LiveProducts.Length];
            for (int i = 0; i < liveProducts.Length; i++)
            {
                foreach (var it in liveCatalog)
                    if (it.productKey == KD_DogDefs.LiveProducts[i].productKey) { liveProducts[i] = it; break; }
                if (liveProducts[i] == null)
                    log.AppendLine("   WARN ライブ商品 " + KD_DogDefs.LiveProducts[i].productKey + " がカタログに無い — 一覧のこの行は出ない");
            }
            if (liveProducts.Length > liveN)
                throw new System.InvalidOperationException("Live の商品が " + liveProducts.Length + " 件 — 一覧の行 (作品数 " + liveN + ") より多い");
            pouch.liveProducts = liveProducts;
            EnsureLiveDetail(pages[KD_TreatPouch.TabLive], pouch, font, pouchUdon, round, log);
            // P10 (2026-09-16): ヘッダとタブの間のインフォメーション帯。位置は LayoutFrame が決める。既定は非アクティブ (販売中だけ実行時に出す)
            {
                // 押せる面なので角丸 + 枠 (板の他のボタンと同じ規律。色だけイベントの暖色)
                var band = RoundButton(go.transform, EventBandName, new Vector2(0f, EventBandY),
                    new Vector2(1100f, EventBandHeight), new Color(0.42f, 0.26f, 0.12f, 0.9f), font, "", pouchUdon, "_EventBand", round, log);
                var bandLabel = ButtonLabelOf(band, 18f);
                bandLabel.rectTransform.sizeDelta = new Vector2(1060f, EventBandHeight);
                bandLabel.color = KD_UiSpriteEditor.AccentGold;
                bandLabel.enableWordWrapping = false;
                band.SetActive(false);
                pouch.eventBandRoot = band; pouch.eventBandLabel = bandLabel;
            }

            // ---- かざり (DES-DOG-004。2 ページを同じ 6 行で使い回す) ----
            //
            // **1 ページ目 = 飾りを選ぶ / 2 ページ目 = どの犬へ着けるか。**
            // 行の形はねるタブと同じ 3 段 (名前 +32 / 状態 +1 / ボタン -32) で 2 列 x 3 段。
            // 飾りは 4 品・犬は 6 匹なので **多いほうの 6 行**を用意し、1 ページ目では余りを畳む
            // (実行時に RedrawAcc が SetActive する)。
            var accDefs = KD_DogDefs.All;
            int accRowCount = Mathf.Max(accDefs.Length, KD_AccessorySetsEditor.MenuCount);
            var accRows = new GameObject[accRowCount];
            var accRowLabels = new TMPro.TextMeshProUGUI[accRowCount];
            var accRowSubs = new TMPro.TextMeshProUGUI[accRowCount];
            var accButtons = new GameObject[accRowCount];
            var accButtonComponents = new UnityEngine.UI.Button[accRowCount];
            var accButtonLabels = new TMPro.TextMeshProUGUI[accRowCount];
            var accGiftButtons = new GameObject[accRowCount];
            var accGiftLabels = new TMPro.TextMeshProUGUI[accRowCount];
            for (int i = 0; i < accRowCount; i++)
            {
                var accRow = KD_ShopSetupEditor.EnsureUiChild(pages[KD_TreatPouch.TabAcc].transform,
                    "AccRow" + i.ToString(), SleepGridPos(i), new Vector2(340f, SleepRowHeight));

                accRowLabels[i] = EnsureText(accRow.transform, "Label", new Vector2(0f, 32f),
                    new Vector2(330f, 40f), 24f, TMPro.TextAlignmentOptions.Left, font);
                accRowSubs[i] = EnsureText(accRow.transform, "Sub", new Vector2(0f, 1f),
                    new Vector2(330f, 30f), 19f, TMPro.TextAlignmentOptions.Left, font);

                accButtons[i] = RoundButton(accRow.transform, "Button",
                    new Vector2(-105f, -32f), new Vector2(140f, 40f),
                    KD_UiSpriteEditor.ActionPrimary, font, KD_TreatPouch.WordAccPut,
                    pouchUdon, "_Acc" + i.ToString(), round, log);
                accButtonComponents[i] = accButtons[i].GetComponent<UnityEngine.UI.Button>();
                // **[あと 42 秒] は 6 字。** 既定の 30pt (= 180px) では 140px を溢れるので落とす
                accButtonLabels[i] = ButtonLabelOf(accButtons[i], 22f);
                // [ギフト] (IMP-PRG-012): 1 ページ目の所有済み買い切り CE セットの行だけ実行時に出す。位置は Hand Menu/3 が決める
                accGiftButtons[i] = RoundButton(accRow.transform, "Gift",
                    new Vector2(105f, 32f), new Vector2(140f, 40f),
                    KD_UiSpriteEditor.ActionNeutral, font, KD_TreatPouch.WordGift,
                    pouchUdon, "_AccGift" + i.ToString(), round, log);
                accGiftLabels[i] = ButtonLabelOf(accGiftButtons[i], 20f);
                accGiftButtons[i].SetActive(false);
                accRows[i] = accRow;
            }

            // 見出しと [もどる] は行の上 (ねるタブの [みんなにも開放] と同じ段)
            var accHeader = EnsureText(pages[KD_TreatPouch.TabAcc].transform, "Header",
                new Vector2(-60f, 112f), new Vector2(400f, 40f), 24f,
                TMPro.TextAlignmentOptions.Left, font);
            var accBack = RoundButton(pages[KD_TreatPouch.TabAcc].transform, "BackButton",
                new Vector2(250f, 112f), new Vector2(140f, 40f),
                KD_UiSpriteEditor.ActionNeutral, font, KD_TreatPouch.WordAccBack,
                pouchUdon, "_AccBack", round, log);
            ButtonLabelOf(accBack, 24f);
            accBack.SetActive(false);   // 1 ページ目では出さない

            pouch.accRows = accRows;
            pouch.accRowLabels = accRowLabels;
            pouch.accRowSubLabels = accRowSubs;
            pouch.accButtons = accButtons;
            pouch.accButtonComponents = accButtonComponents;
            pouch.accButtonLabels = accButtonLabels;
            pouch.accGiftButtons = accGiftButtons;
            pouch.accGiftLabels = accGiftLabels;
            pouch.accHeaderLabel = accHeader;
            pouch.accBackButton = accBack;

            pouch.live = liveSys;
            pouch.liveItems = liveItems;
            pouch.liveTitles = liveTitles;
            pouch.liveLabels = liveLabels;
            pouch.liveSubLabels = liveSubLabels;
            pouch.liveButtons = liveButtons;
            pouch.liveButtonComponents = liveButtonComponents;
            pouch.liveButtonImages = liveButtonImages;
            pouch.liveButtonLabels = liveButtonLabels;
            pouch.liveGiftButtons = liveGiftButtons;
            pouch.liveGiftLabels = liveGiftLabels;
            pouch.liveStatusLabel = liveStatus;

            // ---- P13: 購入の確認は板の手前に重ねる 1 枚 (全売り場で共有) ----
            EnsureConfirmPanel(go, pouch, font, pouchUdon, round, log);
            // 旧 API の名残: 各売り場の *Confirm* 参照は共有カードを指す (Verify / Checks が「確認の口」として数える)
            pouch.liveConfirmRoot = pouch.confirmRoot;
            pouch.liveConfirmLabel = pouch.confirmBodyLabel;
            pouch.liveConfirmYes = pouch.confirmYes;
            pouch.liveConfirmNo = pouch.confirmNo;

            // ---- 旧レイアウトの残骸を片付ける ----
            //
            // `EnsureUiChild` は名前で探すので、**板直下に置いていた旧・行がそのまま残る。**
            // 残ると**配線の無いボタンが板に居座り、押しても何も起きない**という
            // いちばん気付けない壊れ方になる (Avatar Idle Arena `PruneLegacyBoardChildren` と同型)
            PrunePanelChildren(go, tabs, n, log);

            pouch.panelRoot = go;
            var audio = root.GetComponent<AudioSource>();
            if (audio == null) audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 0f;
            audio.volume = 0.2f;
            pouch.uiAudio = audio;
            var soundShelf = items != null && items.Length > 0 && items[0] != null ? items[0].shelf : null;
            if (soundShelf != null)
            {
                pouch.uiSuccessClip = soundShelf.buyClip;
                pouch.uiRejectClip = soundShelf.rejectClip;
            }
            else log.AppendLine("   NOTE 段階 0: おやつの棚が無いので効果音 (uiSuccessClip / uiRejectClip) は未配線 (IMP-PRG-001 / IMP-DOG-014)");   // 裏山改変
            pouch.balanceLabel = balance;
            pouch.sectionTitleLabel = sectionTitle;
            pouch.feedbackLabel = feedback;
            pouch.rowLabels = rowLabels;
            pouch.rowHintLabels = rowHints;
            pouch.rowButtons = rowButtons;
            pouch.statusLabel = status;
            pouch.closeHintLabel = closeHint;
            pouch.touchLauncher = launcher;   // IMP-TRT-006
            pouch.touchLauncherButton = (RectTransform)launcher.transform.Find(LauncherButtonName);
            pouch.putAwayButton = putAway;
            pouch.closeButton = closeBtn;
            pouch.tabPages = pages;
            pouch.tabImages = tabImages;
            pouch.tabUnderlines = tabUnderlines;
            pouch.toyLabels = toyLabels;
            pouch.toyReasonLabels = toyReasons;
            pouch.toyButtons = toyButtons;
            pouch.toyButtonComponents = toyButtonComponents;
            pouch.toyButtonImages = toyButtonImages;
            pouch.toyNames = toyNames;
            pouch.fxLabels = fxLabels;
            pouch.fxSubLabels = fxSubLabels;
            pouch.fxButtons = fxButtons;
            pouch.fxButtonImages = fxImages;
            pouch.equipOnColor = KD_UiSpriteEditor.ActionEquip;
            pouch.fxButtonLabels = fxButtonLabels;
            pouch.fxConfirmRoot = pouch.confirmRoot;
            pouch.fxConfirmLabel = pouch.confirmBodyLabel;
            pouch.fxConfirmYes = pouch.confirmYes;
            pouch.fxConfirmNo = pouch.confirmNo;
            pouch.sleepLabels = sleepLabels;
            pouch.sleepStatusLabels = sleepStatus;
            pouch.sleepButtons = sleepButtons;
            pouch.sleepButtonComponents = sleepButtonComponents;
            pouch.sleepButtonImages = sleepButtonImages;
            pouch.sleepButtonLabels = sleepButtonLabels;
            pouch.sleepNoteLabel = sleepNote;
            KD_SleepPlacementEditor.RebuildIfPresent(pouch, log);

            // **既定は「さんぽ」だけ開いておく** (裏山改変: 古民家は かざり)。実行時の ApplyTab と同じ状態にしておかないと、
            // Play を押す前のシーン上で 3 面が重なって見える
            for (int i = 0; i < tabs; i++)
                if (pages[i] != null) pages[i].SetActive(i == KD_TreatPouch.TabWalk);
            for (int i = 0; i < tabs; i++)
            {
                if (tabImages[i] != null)
                    tabImages[i].color = (i == KD_TreatPouch.TabWalk)
                        ? pouch.tabOnColor : pouch.tabOffColor;
                if (tabUnderlines[i] != null)
                    tabUnderlines[i].SetActive(i == KD_TreatPouch.TabWalk);
            }

            // 裏山改変: さんぽ / [ディスク] / かざりの FxRow / ライブ準備中。**Scroll.Build が Content へ移すので、その直前**
            HillsideBuildPages(go, pouch, pages, font, pouchUdon, round, circle, log);

            // **既定は閉じておく** (入室した瞬間から板が浮かないように)
            go.SetActive(false);
            KD_HandMenuScrollEditor.Build(pouch, log);
            KD_DogPreviewEditor.RewireMenu(pouch, log);
            // Bind localization only after all rows are back under their final scroll paths.
            // Menu Finish を先に: 設定ホームの CallAccess (P5) など、Bind が要求する面はこちらが作る (Codex 指摘 2026-09-17。旧シーンからの移行で落ちる)
            KD_MenuFinishEditor.RebuildIfPresent(pouch, log);
            KD_CallPoseEditor.RebuildIfPresent();
            KD_HandMenuCollisionEditor.Configure(go);
            // P13: 確認カードは常に最後の子 (設定 / ポイントの面より手前)。あそぶは別 Setup なので居れば配線
            if (pouch.confirmRoot != null) pouch.confirmRoot.transform.SetAsLastSibling();
            {
                var activitiesGo = KD_SceneObjects.Find("KD_Activities", out int activitiesCount);
                pouch.activitiesMenu = (activitiesCount == 1 && activitiesGo != null) ? activitiesGo.GetUdonSharpComponent<KD_ActivitiesMenu>() : null;
                if (pouch.activitiesMenu == null) log.AppendLine("   WARN KD_Activities が無い — あそぶは空の面 (先に Tools/KominkaHillside/Menu/9 Setup Activities → Menu/7 を再実行)");
            }
            log.AppendLine("   pouch panel: " + PanelWidth + "x" + PanelHeight
                + " / おやつ " + n + " / おもちゃ " + toyCount + " / なで " + fxCount
                + " / ねる " + toyCount + " / 既定は非アクティブ・さんぽタブ・2列スクロール");
        }

        /// <summary>退役した部品 (旧確認帯など) が残っていれば消す。**作った側の後始末** (再実行は冪等)。</summary>
        static void RetireLegacy(Transform parent, string name, StringBuilder log)
        {
            var t = parent.Find(name);
            if (t == null) return;
            Object.DestroyImmediate(t.gameObject);
            log.AppendLine("   removed " + name + " (P13: 確認は板の手前の共有カードへ。帯は廃止)");
        }

        public const string ConfirmPanelName = "ConfirmPanel";
        const float ConfirmCardWidth = 720f;
        const float ConfirmCardHeight = 300f;
        /// <summary>カードを板の手前へ浮かせる量 (Canvas 1px = 1mm → 15px = 1.5cm)。VR で「前に出た」と読める</summary>
        const float ConfirmCardLift = -15f;

        /// <summary>P13: 購入の確認は板の手前に重ねる 1 枚 (2026-09-16 ユーザー決定「完全にモーダルの方がいい」)。
        ///
        /// <code>
        /// ConfirmPanel (板全体)                      ← 暗幕。raycast を受けるので後ろの行は押せない。押すと [やめる]
        /// └ Card 720×300 @ z -15                    ← 商品名 / 中身 / 残り / [やめる] [購入]
        /// </code>
        /// 文言と可否は実行時 (<see cref="KD_TreatPouch._ShowItemConfirm"/>) が書く。ここは器だけ。
        /// **既定は非アクティブ。** 板と同じく開いたまま保存しない。</summary>
        /// <summary>P8 (2026-09-16): ライブの詳細の面。参考画像 (準備中の色面) + 雰囲気 + 見どころ + 曲 + 同梱の撫でFX / 飾り (各タブへの入口)。
        /// 面の高さは Scroll と同じ (上 195 〜 下 -250)。[もどる] と [買う] / [開催] は Page_Live 直下の下段。試聴は置かない。</summary>
        static void EnsureLiveDetail(GameObject page, KD_TreatPouch pouch, TMPro.TMP_FontAsset font,
            VRC.Udon.UdonBehaviour pouchUdon, Sprite round, StringBuilder log)
        {
            // 上端 = 一覧と同じ (KD_HandMenuScrollEditor.ContentTop)。2026-09-17 の上段の組み直しで -27 → -41
            var root = KD_ShopSetupEditor.EnsureUiChild(page.transform, "LiveDetail", new Vector2(0f, KD_HandMenuScrollEditor.ContentTop - 445f / 2f), new Vector2(1100f, 445f));
            var t = root.transform;
            pouch.liveDetailTitle = EnsureText(t, "Title", new Vector2(0f, 196f), new Vector2(1060f, 36f), 24f, TMPro.TextAlignmentOptions.Left, font);
            pouch.liveDetailMeta = EnsureText(t, "Meta", new Vector2(0f, 165f), new Vector2(1060f, 28f), 18f, TMPro.TextAlignmentOptions.Left, font);
            // 参考画像 (2026-09-17): 正方形 200px。Textures/LiveThumbs/<productKey>.png (CE 3 件は商品サムネイル、Point 4 件は本番ステージの撮影)。
            // 無い商品は色面 + 作品名 + 準備中 (実行時に RedrawLiveDetail が切り替える)
            var poster = KD_ShopSetupEditor.EnsureUiChild(t, "Poster", new Vector2(-430f, 50f), new Vector2(200f, 200f));
            var posterImg = poster.GetComponent<UnityEngine.UI.Image>();
            if (posterImg == null) posterImg = poster.AddComponent<UnityEngine.UI.Image>();
            posterImg.color = new Color(0.30f, 0.26f, 0.20f, 1f); posterImg.raycastTarget = false;
            posterImg.type = UnityEngine.UI.Image.Type.Simple; posterImg.preserveAspect = true; posterImg.sprite = null;
            pouch.liveDetailPosterImage = posterImg;
            pouch.liveDetailPosterLabel = EnsureText(poster.transform, "Label", Vector2.zero, new Vector2(180f, 120f), 18f, TMPro.TextAlignmentOptions.Center, font);
            pouch.liveDetailPosterLabel.enableWordWrapping = true;
            var thumbs = new Sprite[pouch.liveProducts == null ? 0 : pouch.liveProducts.Length];
            for (int i = 0; i < thumbs.Length; i++)
            {
                thumbs[i] = pouch.liveProducts[i] == null ? null : KD_LiveThumbnailEditor.SpriteFor(pouch.liveProducts[i].productKey);
                if (thumbs[i] == null) log.AppendLine("   WARN ライブの参考画像が無い: " + (pouch.liveProducts[i] == null ? "?" : pouch.liveProducts[i].productKey) + " (色面のまま)");
            }
            pouch.liveThumbs = thumbs;
            pouch.liveDetailMood = EnsureText(t, "Mood", new Vector2(185f, 112f), new Vector2(640f, 66f), 19f, TMPro.TextAlignmentOptions.TopLeft, font);
            pouch.liveDetailMood.enableWordWrapping = true;
            pouch.liveDetailScene = EnsureText(t, "Scene", new Vector2(185f, 30f), new Vector2(640f, 90f), 18f, TMPro.TextAlignmentOptions.TopLeft, font);
            pouch.liveDetailScene.enableWordWrapping = true;
            var songsRow = KD_ShopSetupEditor.EnsureUiChild(t, "SongsRow", new Vector2(0f, -76f), new Vector2(1060f, 30f));
            pouch.liveDetailSongsRow = songsRow;
            pouch.liveDetailSongs = EnsureText(songsRow.transform, "Label", Vector2.zero, new Vector2(1060f, 30f), 18f, TMPro.TextAlignmentOptions.Left, font);
            var fxRow = KD_ShopSetupEditor.EnsureUiChild(t, "FxRow", new Vector2(0f, -117f), new Vector2(1060f, 50f));
            pouch.liveDetailFxRow = fxRow;
            pouch.liveDetailFx = EnsureText(fxRow.transform, "Label", new Vector2(-115f, 0f), new Vector2(830f, 50f), 17f, TMPro.TextAlignmentOptions.Left, font);
            pouch.liveDetailFx.enableWordWrapping = true;
            var fxButton = RoundButton(fxRow.transform, "Button", new Vector2(420f, 0f), new Vector2(210f, 40f),
                KD_UiSpriteEditor.ActionNeutral, font, KD_TreatPouch.WordLiveSeeFx, pouchUdon, "_LiveSeeFx", round, log);
            ButtonLabelOf(fxButton, 20f);
            var accRow = KD_ShopSetupEditor.EnsureUiChild(t, "AccRow", new Vector2(0f, -168f), new Vector2(1060f, 50f));
            pouch.liveDetailAccRow = accRow;
            pouch.liveDetailAcc = EnsureText(accRow.transform, "Label", new Vector2(-115f, 0f), new Vector2(830f, 50f), 17f, TMPro.TextAlignmentOptions.Left, font);
            pouch.liveDetailAcc.enableWordWrapping = true;
            var accButton = RoundButton(accRow.transform, "Button", new Vector2(420f, 0f), new Vector2(210f, 40f),
                KD_UiSpriteEditor.ActionNeutral, font, KD_TreatPouch.WordLiveSeeAcc, pouchUdon, "_LiveSeeAcc", round, log);
            ButtonLabelOf(accButton, 20f);
            // 2 行 (1 行目 = 全員で見られる、2 行目 = 買い方 / 販売期限)。**下段の [もどる] と [買う/開催] の間** (root は -36.5 ずれているので
            // 面の Y=-285 に合わせる)。AccRow の下 (面の -230〜-263) には 1 行しか入らない (2026-09-17)
            pouch.liveDetailNote = EnsureText(t, "Note", new Vector2(-20f, -248.5f), new Vector2(740f, 46f), 16f, TMPro.TextAlignmentOptions.Left, font);
            pouch.liveDetailNote.enableWordWrapping = true;
            root.SetActive(false);
            // 下段: [もどる] (詳細・曲を選ぶ面) と [買う] / [開催] (詳細)
            var back = RoundButton(page.transform, "LiveBack", new Vector2(-480f, -285f), new Vector2(160f, 44f),
                KD_UiSpriteEditor.ActionNeutral, font, KD_TreatPouch.WordAccBack, pouchUdon, "_LiveBack", round, log);
            ButtonLabelOf(back, 22f); back.SetActive(false);
            var action = RoundButton(page.transform, "LiveAction", new Vector2(460f, -285f), new Vector2(200f, 44f),
                KD_UiSpriteEditor.ActionPrimary, font, KD_TreatPouch.WordBuyHere, pouchUdon, "_LiveDetailAction", round, log);
            pouch.liveDetailActionLabel = ButtonLabelOf(action, 22f); action.SetActive(false);
            pouch.liveDetailRoot = root;
            pouch.liveDetailBack = back;
            pouch.liveDetailAction = action;
            pouch.liveDetailActionButton = action.GetComponent<UnityEngine.UI.Button>();
            pouch.liveDetailActionImage = action.GetComponent<UnityEngine.UI.Image>();
            log.AppendLine("   LiveDetail (P8): 詳細の面と [もどる] [買う/開催]");
        }

        static void EnsureConfirmPanel(GameObject panel, KD_TreatPouch pouch, TMPro.TMP_FontAsset font,
            VRC.Udon.UdonBehaviour pouchUdon, Sprite round, StringBuilder log)
        {
            var rootGo = KD_ShopSetupEditor.EnsureUiChild(panel.transform, ConfirmPanelName,
                Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            // 暗幕: 押すと [やめる] (暗幕の外を押したのと同じ扱い)
            var dim = KD_ShopSetupEditor.EnsureButton(panel.transform, ConfirmPanelName, Vector2.zero,
                new Vector2(PanelWidth, PanelHeight), new Color(0.043f, 0.035f, 0.027f, 0.65f), font, "",
                pouchUdon, "_ConfirmNo", log);
            var dimImg = dim.GetComponent<UnityEngine.UI.Image>();
            dimImg.raycastTarget = true;
            KD_UiSpriteEditor.ApplyFlatSkin(dimImg);
            var dimBtn = dim.GetComponent<UnityEngine.UI.Button>();
            dimBtn.transition = UnityEngine.UI.Selectable.Transition.None;   // 暗幕は押せる面に見せない
            {
                var dimLabel = dim.transform.Find("Label");
                if (dimLabel != null) dimLabel.gameObject.SetActive(false);
            }

            var card = KD_ShopSetupEditor.EnsureUiChild(rootGo.transform, "Card",
                Vector2.zero, new Vector2(ConfirmCardWidth, ConfirmCardHeight));
            card.transform.localPosition = new Vector3(0f, 0f, ConfirmCardLift);
            var cardImg = card.GetComponent<UnityEngine.UI.Image>();
            if (cardImg == null) cardImg = card.AddComponent<UnityEngine.UI.Image>();
            var cardColor = KD_UiSpriteEditor.SurfaceRaised; cardColor.a = 1f;   // 手前の 1 枚は不透明 (後ろの行が透けると読みにくい)
            cardImg.color = cardColor;
            cardImg.raycastTarget = true;
            KD_UiSpriteEditor.ApplyFlatSkin(cardImg);
            // raycastTarget だけでは親 (暗幕 = Button) へクリックが伝わり、カードの余白を押すと [やめる] になる (Codex 指摘 2026-09-17。
            // UGUI は最初に IPointerClickHandler を持つ祖先を探す)。カード自体に何もしない Button を置いて、ここで止める
            var cardStop = card.GetComponent<UnityEngine.UI.Button>();
            if (cardStop == null) cardStop = card.AddComponent<UnityEngine.UI.Button>();
            cardStop.transition = UnityEngine.UI.Selectable.Transition.None;
            cardStop.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            var cardNav = cardStop.navigation; cardNav.mode = UnityEngine.UI.Navigation.Mode.None; cardStop.navigation = cardNav;
            // 上辺の金の線 (板の上辺と同じ印)
            var top = KD_ShopSetupEditor.EnsureUiChild(card.transform, "TopLine",
                new Vector2(0f, ConfirmCardHeight * 0.5f - 2f), new Vector2(ConfirmCardWidth, 4f));
            var topImg = top.GetComponent<UnityEngine.UI.Image>();
            if (topImg == null) topImg = top.AddComponent<UnityEngine.UI.Image>();
            topImg.color = KD_UiSpriteEditor.AccentGold;
            topImg.raycastTarget = false;
            KD_UiSpriteEditor.ApplyFlatSkin(topImg);

            // 題 (26pt) / 中身 (19pt) / 残り (16pt)。左寄せ、幅はカード − 余白 28×2
            float w = ConfirmCardWidth - 56f;
            var title = EnsureText(card.transform, "Title", new Vector2(0f, 92f), new Vector2(w, 40f), 26f,
                TMPro.TextAlignmentOptions.Left, font);
            title.enableWordWrapping = true;
            var body = EnsureText(card.transform, "Body", new Vector2(0f, 44f), new Vector2(w, 56f), 19f,
                TMPro.TextAlignmentOptions.TopLeft, font);
            body.enableWordWrapping = true;
            var note = EnsureText(card.transform, "Note", new Vector2(0f, -6f), new Vector2(w, 30f), 16f,
                TMPro.TextAlignmentOptions.Left, font);
            note.color = new Color(0.85f, 0.82f, 0.77f, 1f);

            // [やめる] [購入] は右下。やめるが左、購入が右 (確認帯・棚と同じ並び)
            var no = RoundButton(card.transform, "No", new Vector2(120f, -102f), new Vector2(136f, 48f),
                KD_UiSpriteEditor.ActionNeutral, font, KD_ShopShelf.WordCancel, pouchUdon, "_ConfirmNo", round, log);
            var yes = RoundButton(card.transform, "Yes", new Vector2(272f, -102f), new Vector2(150f, 48f),
                KD_UiSpriteEditor.ActionPrimary, font, KD_ShopShelf.WordBuy, pouchUdon, "_ConfirmYes", round, log);
            var noLabel = ButtonLabelOf(no, 20f);
            var yesLabel = ButtonLabelOf(yes, 20f);

            rootGo.transform.SetAsLastSibling();
            rootGo.SetActive(false);   // **既定は閉じている**

            pouch.confirmRoot = rootGo;
            pouch.confirmTitleLabel = title;
            pouch.confirmBodyLabel = body;
            pouch.confirmNoteLabel = note;
            pouch.confirmYes = yes;
            pouch.confirmYesLabel = yesLabel;
            pouch.confirmNo = no;
            pouch.confirmNoLabel = noLabel;
            log.AppendLine("   confirm card: " + ConfirmCardWidth + "x" + ConfirmCardHeight + " @ z" + ConfirmCardLift + " (P13。帯は廃止)");
        }

        /// <summary>2 列グリッドの座標。**行が増えても縦に伸ばさない**ための唯一の置き方
        /// (板の縦 520px 上限を守る主な仕掛け — hand-menu §4)。</summary>
        static Vector2 GridPos(int i, float topY, float bottomY)
        {
            // IMP-DOG-020: 3 段目以降も同じピッチで下へ (6 匹 = 2 列 × 3 段。おもちゃ行 100px / ピッチ 110px)
            return new Vector2((i % 2 == 0) ? -180f : 180f, topY - (i / 2) * (topY - bottomY));
        }

        /// <summary>ねるタブの 2 列グリッド。**行が背が高い**ぶん
        /// おやつ / おもちゃより詰めてある (行の上に [みんなにも開放] が入るため)。
        /// **縦に伸ばすのではなく、この 2 列 × 3 段に収める** — hand-menu §4。
        /// IMP-DOG-020 で 2 段 (124px / 30, -98) → 3 段 (行 104px / ピッチ 104px: 40, -64, -168) へ。
        /// 板の縦 520px はそのまま — 行の中身 (名前 / 状態 / ボタン) の間隔を詰めて 3 段目を作った。</summary>
        static Vector2 SleepGridPos(int i)
        {
            return new Vector2((i % 2 == 0) ? -180f : 180f, SleepRowTopY - (i / 2) * SleepRowPitch);
        }

        const float SleepRowTopY = 40f;
        const float SleepRowPitch = 104f;
        const float SleepRowHeight = 104f;

        /// <summary>ボタンの文言ラベルを取り出し、必要なら字を小さくする。
        /// **既定の 30pt では枠を溢れる文言がある** ([一緒に寝る] = 5 字 = 150px &gt; 140px)。
        /// 溢れても `Overflow` なので黙って隣へ描かれる (`vrc-tmp-font` §4) — 気付けない。</summary>
        static TMPro.TextMeshProUGUI ButtonLabelOf(GameObject button, float fontSize)
        {
            if (button == null) return null;
            var t = button.transform.Find("Label");
            if (t == null) return null;
            var tmp = t.GetComponent<TMPro.TextMeshProUGUI>();
            if (tmp != null && fontSize > 0f) tmp.fontSize = fontSize;
            return tmp;
        }

        /// <summary>配列の <paramref name="i"/> 番目 (短すぎたら null)。
        /// **検査側で長さを確かめ直さずに要素を見るため** — 長さの検査は別に立っているので、
        /// ここで落ちても「その行が未配線」として 1 件だけ出れば足りる。</summary>
        static T ArrayAt<T>(T[] a, int i) where T : class
        {
            return (a != null && i >= 0 && i < a.Length) ? a[i] : null;
        }

        /// <summary>ボタンの onClick に張られた `SendCustomEvent("<name>")` の名前と宛先。
        /// 1 本も張られていない / メソッドが違うときは null を返す。
        ///
        /// **名前だけでなく宛先も返す** — 別の Udon 宛だと、配線済みに見えて
        /// 押しても何も起きない (例外もログも出ない)。</summary>
        static string OnClickEventName(UnityEngine.UI.Button b, out UnityEngine.Object target)
        {
            target = null;
            if (b == null) return null;
            if (b.onClick.GetPersistentEventCount() < 1) return null;
            if (b.onClick.GetPersistentMethodName(0) != "SendCustomEvent") return null;
            var so = new SerializedObject(b);
            var calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (calls == null || calls.arraySize < 1) return null;
            var call = calls.GetArrayElementAtIndex(0);
            target = call.FindPropertyRelative("m_Target").objectReferenceValue;
            return call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue;
        }

        /// <summary>押せるボタン。**角丸 + 枠を当てる** (hand-menu §5)。</summary>
        /// <summary>IMP-TRT-006: Touch (スマホ) 用の起動ボタン。
        ///
        /// **Screen-Space Overlay の Canvas + ボタン 1 個。** 押すと `KD_TreatPouch._Toggle()` —
        /// 既存の板 (World-Space) を開閉するだけで、UI ロジックは 1 行も複製しない。
        /// 公式 Screen Canvas サンプルと同じ土台 (Canvas + GraphicRaycaster + VRCUiShape)。
        ///
        /// **既定は非アクティブ。** 出す判断は実行時の `KD_TreatPouch.ApplyInputMode`
        /// (入力方式 = Touch のときだけ)。VR では Screen-Space Canvas が成立しないので必ず隠れる。
        /// 非アクティブで保存する理由はもう 1 つ — ClientSim は Screen Canvas の当たりを
        /// 原点に全画面サイズで生成する (公式ドキュメントの既知の制限) ので、
        /// 有効のままだと ClientSim でワールドを丸ごと覆う。
        ///
        /// 初期配置は横画面の上端中央。実行時はOnScreenUpdateで縦画面を右中央へ切り替える。
        /// CanvasScalerは縦横比に応じて拡縮する。ボタンは220x72設計単位。</summary>
        static GameObject EnsureTouchLauncher(Transform pouchRoot, TMPro.TMP_FontAsset font,
            VRC.Udon.UdonBehaviour target, Sprite round, StringBuilder log)
        {
            var t = pouchRoot.Find(PouchLauncherName);
            GameObject go = t != null ? t.gameObject : null;
            if (go == null)
            {
                go = new GameObject(PouchLauncherName, typeof(RectTransform));
                go.transform.SetParent(pouchRoot, false);
                log.AppendLine("created " + PouchLauncherName);
            }
            go.layer = KD_HandMenuCollisionEditor.InputLayer; // BUG-ENV-004: Touch UI must not block players.
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var canvas = go.GetComponent<Canvas>();
            if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;   // VRChat 側の UI より前に出るかは実機で見る
            var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler == null) scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = LauncherRefResolution;
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            if (go.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            if (go.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null)
                go.AddComponent<VRC.SDK3.Components.VRCUiShape>();

            // **[とじる] の赤茶を使わない** (IMP-INF-006 F-06。2026-09-07 ユーザー決定) —
            // これは板を**開く**ボタンなので、「板を消す」の色を当てると
            // 板の中で色と意味が 1 対 1 でなくなる。中立の灰 ([しまう] / [やめる] と同じ)
            var btn = RoundButton(go.transform, LauncherButtonName, Vector2.zero, LauncherSize,
                KD_UiSpriteEditor.ActionNeutral, font, KD_TreatPouch.WordTouchLauncher,
                target, "_Toggle", round, log);
            // 上端中央へアンカーする (EnsureUiChild は中央アンカーで作る)
            var r = (RectTransform)btn.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -LauncherTopMargin);
            r.sizeDelta = LauncherSize;

            // Include inactive hint descendants when rebuilding an existing launcher.
            foreach (var child in go.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = KD_HandMenuCollisionEditor.InputLayer;

            // **既定は非アクティブ** (上の summary)
            if (go.activeSelf) go.SetActive(false);
            return go;
        }

        static GameObject RoundButton(Transform parent, string name, Vector2 pos, Vector2 size,
            Color color, TMPro.TMP_FontAsset font, string labelText,
            VRC.Udon.UdonBehaviour target, string customEvent, Sprite round, StringBuilder log)
        {
            var go = KD_ShopSetupEditor.EnsureButton(parent, name, pos, size, color, font,
                labelText, target, customEvent, log);
            KD_UiSpriteEditor.ApplyRoundSkin(go.GetComponent<UnityEngine.UI.Image>(), round);
            return go;
        }

        /// <summary>タブ 1 枚。**角あり・枠なし + 下線。**
        ///
        /// 丸みと枠は押せるものだけの印なので、**面を切り替える見出しには当てない**
        /// (Avatar Idle Arena `IMP-UI-009` 一般則③ — 広げると板の中で
        /// 「操作するもの」と「読むもの」が混ざる)。
        /// 選択中は**色と下線の 2 つ**で表す (`interactable` は使わない —
        /// この板では「今は押せない」の意味でおもちゃ行が既に使っている)。</summary>
        static GameObject EnsureTab(Transform parent, string name, Vector2 pos, Vector2 size,
            TMPro.TMP_FontAsset font, string labelText,
            VRC.Udon.UdonBehaviour target, string customEvent, StringBuilder log,
            out UnityEngine.UI.Image image, out GameObject underline)
        {
            var go = KD_ShopSetupEditor.EnsureButton(parent, name, pos, size,
                KD_UiSpriteEditor.TabOff, font, labelText, target, customEvent, log);
            image = go.GetComponent<UnityEngine.UI.Image>();
            KD_UiSpriteEditor.ApplyFlatSkin(image);

            underline = KD_ShopSetupEditor.EnsureUiChild(go.transform, "Underline",
                new Vector2(0f, -size.y * 0.5f + 3f), new Vector2(size.x, 6f));
            var img = underline.GetComponent<UnityEngine.UI.Image>();
            if (img == null) img = underline.AddComponent<UnityEngine.UI.Image>();
            img.color = KD_UiSpriteEditor.AccentGold;   // 棚のアクセント金と同じ
            img.raycastTarget = false;
            KD_UiSpriteEditor.ApplyFlatSkin(img);
            return go;
        }

        /// <summary>**板直下に想定した子以外を残さない。**
        ///
        /// 旧レイアウト (タブが無かった頃の `Row0..3` / `Slots` / `Status` /
        /// `PutAwayButton` が板直下に居た形) は `EnsureUiChild` では消えない —
        /// あれは名前で探して作るだけなので、**置き場所を変えると古いほうが取り残される。**
        /// 残ると押しても何も起きないボタンが板に居座る。</summary>
        static void PrunePanelChildren(GameObject panel, int tabCount, int treatRows,
            StringBuilder log)
        {
            // 裏山改変: Page_Walk は TabPageNames 経由で残る。WalkRow / Disc / FxRow は各 Page の中 (板直下ではない) なので、ここの掃除の対象外
            var keep = new System.Collections.Generic.HashSet<string>
            {
                "Bg", "Balance", "SectionTitle", "Feedback", "CloseButton", "CloseHint", "SettingsButton", "Page_Settings",
                ConfirmPanelName,   // P13: 購入の確認 (板の手前の 1 枚)
                EventBandName,      // P10: ヘッダとタブの間のインフォメーション帯
                // IMP-PRG-010: 残高の裏に置く「ポイント」の面への入口 (Gift Code/2 - Setup が作る)
                KD_GiftCodeSetupEditor.PointsButtonName,
                // IMP-INF-020: ようこその面 (Welcome Guide/1 - Build が作る)。**2026-09-21 まで keep に無く、板の再構築が面を消して
                // Menu Finish の Bind が "Localization target missing: Page_Welcome/..." で落ちていた** (IMP-PRG-012 の UI/1 - Refresh Menus で発覚)
                KD_WelcomeGuideEditor.PageName,
            };
            for (int i = 0; i < tabCount; i++)
            {
                keep.Add("Tab" + i.ToString());
                keep.Add(TabPageNames[i]);
            }
            int removed = 0;
            for (int i = panel.transform.childCount - 1; i >= 0; i--)
            {
                var c = panel.transform.GetChild(i);
                if (keep.Contains(c.name)) continue;
                log.AppendLine("   removed 旧レイアウトの残骸: " + c.name);
                Object.DestroyImmediate(c.gameObject);
                removed++;
            }
            if (removed > 0)
                log.AppendLine("   板直下の掃除: " + removed + " 件 "
                    + "(配線の無いボタンが残ると、押しても何も起きない壊れ方になる)");
        }

        static TMPro.TextMeshProUGUI EnsureText(Transform parent, string name,
            Vector2 pos, Vector2 size, float fontSize,
            TMPro.TextAlignmentOptions align, TMPro.TMP_FontAsset font)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var tmp = go.GetComponent<TMPro.TextMeshProUGUI>();
            if (tmp == null) tmp = go.AddComponent<TMPro.TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.alignment = align;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            return tmp;
        }

    }
}
