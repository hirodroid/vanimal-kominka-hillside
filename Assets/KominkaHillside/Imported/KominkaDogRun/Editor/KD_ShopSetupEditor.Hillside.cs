using System.Collections.Generic;
using System.Linq;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>裏山改変 (IMP-PRG-001 段階 1): 裏山の Menu/4 Setup Shop / 4a Verify Shop。
    ///
    /// **カタログと商品 (KD_ShopItem) だけを作る。** 裏山に棚は無く、購入は手元メニュー (KD_TreatPouch) の行と
    /// 共有の確認カードから行う。古民家版 (<see cref="Setup"/> / <see cref="Verify"/>) が作る
    /// 棚のメッシュ・棚の面ラベル・確認パネル・効果音・KD_ToyBox・KD_PetFxBox は作らない (古民家版は Imported-Unused/Shop へ移した)。
    ///
    /// 置き場所: KH_Dogs/KD_ShopCatalog と、その子の KD_ShopItem_* (<see cref="KD_ProductDefinitions.All"/> の全行。
    /// 名前は sceneName どおり)。おもちゃ・ライブ・追加体験・追加動作の行も**台帳のために残す** (画面には出ない)。
    /// 全行 menuOnly = true / Collider なし (Interact で即購入が起きない形)。
    /// 価格・区分・CE の参照は <see cref="KD_ProductDefinitions.Apply"/> (古民家と同じ表)、
    /// ledger.products / catalog.items / categoryKeys / defaultProducts は <see cref="KD_ProductsEditor.BindCatalog"/> が配線する
    /// (古民家の Shop/2 と同じ経路。台帳の _ConfigureCatalog は実行時にこの products で動く)。
    ///
    /// 同じ Setup で KH_Dogs/KD_PlayerFxTag (VRCPlayerObject + KD_PlayerFxTag, Manual) を作る
    /// (古民家では Shop/2 が撫でFXの器を作るときに一緒に作っていた — 同じ場所で作る)。
    ///
    /// 前提: Menu/3 Create Shop Programs → force refresh → Menu/3a Setup Progress。**シーンは保存しない。**</summary>
    public static partial class KD_ShopSetupEditor
    {
        const string HillsideShopSetupLog = KD_Hillside.LogDir + "setup-shop.txt";
        const string HillsideShopVerifyLog = KD_Hillside.LogDir + "verify-shop.txt";
        const string HillsideItemPrefix = "KD_ShopItem_";
        const string LedgerProgramPath = ProgressDir + "KD_ProgressLedger.asset";
        const string ShopItemProgramPath = ProgressDir + "KD_ShopItem.asset";

        /// <summary>商品キー → 表示名の文言キー。**古民家のシーン値の写し** (古民家では各 Setup と
        /// 一度きりの移行でシーンにだけ入っていた値。裏山では表をここに持つ)。文言そのものは UITranslations.json。</summary>
        static readonly Dictionary<string, string> HillsideNameKeys = new Dictionary<string, string>
        {
            { "action.belly_up", "action.belly_up" },
            { "action.chin_rest", "action.chin_rest" },
            { "action.give_paw", "action.give_paw" },
            { "action.stretch_yawn", "action.stretch_yawn" },
            { "experience.formation", "activities.home.0" },
            { "experience.hosting_play", "activities.home.1" },
            { "experience.dog_pov", "activities.home.2" },
            { "toy.basic_ball", "item.basic" },
            { "toy.tennis_ball", "item.tennis" },
            { "toy.donut", "item.donut" },
            { "toy.cloth_ball", "item.cloth" },
            { "toy.bone", "item.bone" },
            { "toy.temari_claude", "item.temariClaude" },
            { "toy.paw_cushion", "item.pawCushion" },
            { "toy.taiyaki_plush", "item.taiyakiPlush" },
            { "toy.ebiten_plush", "item.ebitenPlush" },
            { "accessory.bandana", "accessory.Bandana" },
            { "accessory.straw_hat", "accessory.StrawHat" },
            { "petfx.heart", "fx.heart" },
            { "petfx.paw_cookie", "item.paw" },
            { "petfx.sparkle", "fx.sparkle" },
            { "petfx.petal", "fx.petal" },
            { "petfx.sakura_heart", "fx.sakura" },
            { "petfx.gold_paw", "fx.goldPaw" },
            { "accessory.bow_tie", "accessory.BowTie" },
            { "accessory.tail_ribbon", "accessory.TailRibbon" },
            { "treat.bone_biscuit", "ui.extra.017" },
            { "treat.paw_cookie", "ui.extra.018" },
            { "treat.star_cookie", "ui.extra.019" },
            { "treat.jerky", "ui.extra.020" },
            { "live.happy_tails", "KD_TreatPouch.liveTitleKeys.0" },
            { "live.garden_lights", "KD_ShopItem_Live_GardenLights.displayNameKey.0" },
            { "live.petal_waltz", "KD_ShopItem_Live_PetalWaltz.displayNameKey.0" },
            { "live.after_rain", "KD_ShopItem_Live_AfterRain.displayNameKey.0" },
            { "live.kominka_pack", "KD_ShopItem_Live_26.displayNameKey.0" },
            { "live.candy_step_set", "KD_ShopItem_Live_CandyStepSet.displayNameKey.0" },
            { "live.pumpkin_parade_set", "KD_ShopItem_Live_PumpkinParadeSet.displayNameKey.0" },
            { "accessory.royal_set", "accessory.Royal" },
            { "accessory.summer_set", "accessory.Summer" },
            { "accessory.floral_set", "accessory.Floral" },
            { "accessory.top_hat", "accessory.TopHat" },
            { "accessory.cap", "accessory.Cap" },
            { "accessory.spiral_glasses", "accessory.SpiralGlasses" },
            { "accessory.star_glasses", "accessory.StarGlasses" },
            { "accessory.backpack", "accessory.Backpack" },
            { "petfx.candy", "fx.candy" },
            { "petfx.pumpkin", "fx.pumpkin" },
            { "petfx.lantern", "fx.lantern" },
            { "petfx.meteor", "fx.meteor" },
            { "petfx.confetti", "fx.confetti" },
        };

        /// <summary>表に無い行 (後から商品を足したとき) の文言キー。ライブの行と同じ `<シーン名>.displayNameKey.0` 型。</summary>
        static string HillsideNameKeyOf(KD_ProductDefinitions.Row d)
        {
            string k;
            return HillsideNameKeys.TryGetValue(d.key, out k) ? k : d.sceneName + ".displayNameKey.0";
        }

        /// <summary>裏山には棚が無いので、手元メニューの購入音 (uiSuccessClip / uiRejectClip) は棚を経由せずここから引く。
        /// 古民家では pouch がおやつ棚の buyClip / rejectClip を写していた (同じ音源: 効果音ラボ)。</summary>
        public static AudioClip HillsideBuyClip() { return AssetDatabase.LoadAssetAtPath<AudioClip>(SfxBuy); }
        public static AudioClip HillsideRejectClip() { return AssetDatabase.LoadAssetAtPath<AudioClip>(SfxReject); }

        // ================================================================ 4: Setup (裏山)

        [MenuItem("Tools/KominkaHillside/Menu/4 Setup Shop")]
        public static void SetupHillsideShop()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ShopSetup] Setup (裏山 段階 1: カタログと商品だけ)");
            if (EditorApplication.isPlaying || !KD_Hillside.InScene())
            {
                log.AppendLine("FAIL " + KD_Hillside.ScenePath + " を Edit モードで開くこと");
                KD_VerifyLog.Write(HillsideShopSetupLog, log);
                return;
            }

            // ---- Program Assets (前提) ----
            foreach (var pa in new[] { CatalogProgram, ShopItemProgramPath, LedgerProgramPath, TagProgram })
            {
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(pa) != null) continue;
                log.AppendLine("FAIL " + pa + " が無い。先に Menu/3 Create Shop Programs → force refresh");
                KD_VerifyLog.Write(HillsideShopSetupLog, log);
                return;
            }

            // ---- KH_Dogs と台帳 (前提) ----
            var dogs = KD_ProgressSetupEditor.FindHillsideRoot(log);
            if (dogs == null) { KD_VerifyLog.Write(HillsideShopSetupLog, log); return; }
            int nLedger;
            var ledgerGo = KD_SceneObjects.Find(LedgerHost, out nLedger);
            var ledger = (nLedger == 1 && ledgerGo != null) ? ledgerGo.GetComponent<KD_ProgressLedger>() : null;
            if (ledger == null)
            {
                log.AppendLine("FAIL " + LedgerHost + " の KD_ProgressLedger が " + nLedger + " 個。先に Menu/3a Setup Progress");
                KD_VerifyLog.Write(HillsideShopSetupLog, log);
                return;
            }

            // ---- 表の検査 (壊れた表で 1 行も作らない) ----
            try { KD_ProductDefinitions.Validate(KD_ProductDefinitions.All, KD_ProductDefinitions.Categories); }
            catch (System.InvalidOperationException e)
            {
                log.AppendLine("FAIL 商品表: " + e.Message);
                KD_VerifyLog.Write(HillsideShopSetupLog, log);
                return;
            }

            // ---- カタログ (KH_Dogs 直下。位置に意味は無い。既存なら親も位置も触らない) ----
            int nCat;
            var catalogGo = KD_SceneObjects.Find(CatalogName, out nCat);
            if (nCat > 1)
            {
                log.AppendLine("FAIL '" + CatalogName + "' がシーン内に " + nCat + " 個ある。1 個に整理してから再実行");
                KD_VerifyLog.Write(HillsideShopSetupLog, log);
                return;
            }
            if (catalogGo == null)
            {
                catalogGo = new GameObject(CatalogName);
                Undo.RegisterCreatedObjectUndo(catalogGo, "Create " + CatalogName);
                catalogGo.transform.SetParent(dogs, false);
                log.AppendLine("created " + KD_Hillside.DogsRootName + "/" + CatalogName);
            }
            else log.AppendLine("reused " + KD_SceneObjects.PathOf(catalogGo));
            var catalog = EnsureU<KD_ShopCatalog>(catalogGo, log);
            if (catalog == null)
            {
                log.AppendLine("SETUP RESULT: FAIL (force refresh 後に再実行)");
                KD_VerifyLog.Write(HillsideShopSetupLog, log);
                return;
            }
            SetSync(catalog, VRC.SDKBase.Networking.SyncType.None, log);

            // ---- 商品 (All 全行。カタログの子) ----
            bool ok = true;
            var translations = KD_LocalizationEditor.Load();
            var wanted = new HashSet<string>();
            foreach (var d in KD_ProductDefinitions.All)
            {
                wanted.Add(d.sceneName);
                var item = EnsureHillsideItem(catalogGo.transform, d, ledger, translations, log);
                if (item == null) ok = false;
            }
            if (!ok)
            {
                log.AppendLine("SETUP RESULT: FAIL (上の FAIL を見ること。BindCatalog は呼んでいない)");
                KD_VerifyLog.Write(HillsideShopSetupLog, log);
                return;
            }
            // **表から消えた商品を撤去する** (古民家 Shop/2 と同じ「今の定義へ収束する」)
            for (int i = catalogGo.transform.childCount - 1; i >= 0; i--)
            {
                var c = catalogGo.transform.GetChild(i);
                if (!c.name.StartsWith(HillsideItemPrefix) || wanted.Contains(c.name)) continue;
                log.AppendLine("removed 表に無い商品: " + c.name);
                Undo.DestroyObjectImmediate(c.gameObject);
            }

            // ---- 台帳とカタログを結ぶ (古民家と同じ経路: ledger.products = catalog.items、CE アセットの参照) ----
            try { KD_ProductsEditor.BindCatalog(catalog, ledger); }
            catch (System.Exception e)
            {
                log.AppendLine("FAIL BindCatalog: " + e.Message);
                KD_VerifyLog.Write(HillsideShopSetupLog, log);
                return;
            }
            // 裏山には ToyBox が無い。手元メニューはカタログを直接読む
            // 裏山改変 (IMP-DOG-015 提案): 段階 2 の撫でFX の消費側 (KH_Dogs/KD_PetFxBox) があれば通知先に残す (Menu/6g が足す。流し直しで消さない)
            var petFxT = dogs.Find(KD_DogDefs.PetFxBoxName);
            var petFx = petFxT == null ? null : petFxT.GetComponent<KD_PetFxBox>();
            catalog.consumers = petFx != null ? new UdonSharpBehaviour[] { petFx } : new UdonSharpBehaviour[0];
            catalog.consumerEvents = petFx != null ? new[] { "_OnShopSelectionChanged" } : new string[0];
            ok &= TryCopy(catalog, log);
            log.AppendLine("wired catalog.items = ledger.products = " + catalog.items.Length + " 件 / categories "
                + string.Join(",", catalog.categoryKeys) + " / consumers " + catalog.consumers.Length);

            // ---- 撫でFXの札 (古民家と同じく Shop Setup が作る。KH_Dogs 直下) ----
            EnsurePlayerFxTag(log, dogs);

            log.AppendLine("SKIP 棚 (KD_ShopShelf)・面ラベル・確認パネル・効果音・KD_ToyBox・KD_PetFxBox — 裏山では作らない (購入は手元メニュー)");
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("SETUP RESULT: " + (ok ? "PASS (scene not saved yet)" : "FAIL"));
            // 7 Build Menu の Bind が KD_ShopKeeperMenu の束縛先を要求するので、おじいさん (4d) を先に作る (Codex D-1)
            log.AppendLine("次: Menu/4d Setup Shop Keeper → Menu/7 Build Menu (段階 1 の配線) → Menu/11 → Menu/11b Build Welcome"
                + " (検査は Menu/3b Verify Progress / 4a Verify Shop をいつでも)");
            KD_VerifyLog.Write(HillsideShopSetupLog, log);
        }

        /// <summary>商品 1 行ぶん (カタログの子)。**冪等** — 既にあれば作り直さず値だけ揃える。
        /// 同名がカタログの外にあれば FAIL (BindCatalog が名前で 1 個に引くので、2 個あると壊れる)。</summary>
        static KD_ShopItem EnsureHillsideItem(Transform catalog, KD_ProductDefinitions.Row d, KD_ProgressLedger ledger,
            KD_LocalizationEditor.Catalog translations, StringBuilder log)
        {
            int n;
            var found = KD_SceneObjects.Find(d.sceneName, out n);
            GameObject go;
            if (n > 1 || (found != null && found.transform.parent != catalog))
            {
                log.AppendLine("FAIL '" + d.sceneName + "' がカタログの外にある / " + n + " 個ある (" +
                    (found == null ? "-" : KD_SceneObjects.PathOf(found)) + ")");
                return null;
            }
            if (found != null) go = found;
            else
            {
                go = new GameObject(d.sceneName);
                Undo.RegisterCreatedObjectUndo(go, "Create " + d.sceneName);
                go.transform.SetParent(catalog, false);
                log.AppendLine("created " + d.sceneName);
            }
            // 見えない行。当たりも絵も持たない (menuOnly。Interact で即購入が起きない)
            foreach (var c in go.GetComponents<Collider>()) Object.DestroyImmediate(c, true);
            var mr = go.GetComponent<MeshRenderer>(); if (mr != null) Object.DestroyImmediate(mr, true);
            var mf = go.GetComponent<MeshFilter>(); if (mf != null) Object.DestroyImmediate(mf, true);

            var item = go.GetComponent<KD_ShopItem>();
            if (item == null)
            {
                try { item = go.AddUdonSharpComponent<KD_ShopItem>(); }
                catch (System.Exception e)
                {
                    log.AppendLine("FAIL KD_ShopItem (" + d.sceneName + "): " + e.Message + " — force refresh 後に再実行");
                    return null;
                }
            }
            item.ledger = ledger;
            KD_ProductDefinitions.Apply(item, d.key);
            item.displayNameKey = HillsideNameKeyOf(d);
            try { item.displayName = KD_LocalizationEditor.Text(translations, "ja", item.displayNameKey); }
            catch (System.InvalidOperationException)
            {
                item.displayName = d.key;
                log.AppendLine("   WARN 文言キー未登録: " + item.displayNameKey + " (" + d.key + ")");
            }
            item.menuOnly = true;
            item.shelf = null;
            item.listener = null;
            SetSync(item, VRC.SDKBase.Networking.SyncType.None, log);
            EditorUtility.SetDirty(item);
            return item;
        }

        // ================================================================ 4a: Verify (裏山)

        [MenuItem("Tools/KominkaHillside/Menu/4a Verify Shop")]
        public static void VerifyHillsideShop()
        {
            var v = new KD_VerifyLog("[KD_ShopSetup] Verify (裏山 段階 1)");
            var log = v.Text;
            if (EditorApplication.isPlaying || !KD_Hillside.InScene())
            {
                v.Check(false, KD_Hillside.ScenePath + " を Edit モードで開いている");
                v.Finish(HillsideShopVerifyLog);
                return;
            }

            // ---- Program Assets ----
            var catPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(CatalogProgram);
            var itemPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ShopItemProgramPath);
            var tagPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(TagProgram);
            v.Check(catPa != null && catPa.sourceCsScript == AssetDatabase.LoadAssetAtPath<MonoScript>(CatalogScript)
                && catPa.behaviourSyncMode == BehaviourSyncMode.None, "catalog program → KD_ShopCatalog.cs / SyncMode None");
            v.Check(itemPa != null && itemPa.behaviourSyncMode == BehaviourSyncMode.None, "shop item program SyncMode None (購入は完全にローカル)");
            v.Check(tagPa != null && tagPa.sourceCsScript == AssetDatabase.LoadAssetAtPath<MonoScript>(TagScript)
                && tagPa.behaviourSyncMode == BehaviourSyncMode.Manual, "player fx tag program → KD_PlayerFxTag.cs / Manual");

            // ---- 表 ----
            bool tableOk = true;
            try { KD_ProductDefinitions.Validate(KD_ProductDefinitions.All, KD_ProductDefinitions.Categories); }
            catch (System.InvalidOperationException e) { tableOk = false; log.AppendLine("       " + e.Message); }
            v.Check(tableOk, "KD_ProductDefinitions.All が Validate を通る (" + KD_ProductDefinitions.All.Length + " 行)");
            int unnamed = KD_ProductDefinitions.All.Count(d => !HillsideNameKeys.ContainsKey(d.key));
            v.Check(unnamed == 0, "全行に文言キーの表がある (無い " + unnamed + " 行)");

            // ---- 台帳 ----
            int nLedger;
            var ledgerGo = KD_SceneObjects.Find(LedgerHost, out nLedger);
            var ledger = ledgerGo == null ? null : ledgerGo.GetComponent<KD_ProgressLedger>();
            v.Check(nLedger == 1 && ledger != null, LedgerHost + " の KD_ProgressLedger が 1 個 (actual " + nLedger + ")");

            // ---- カタログ ----
            int nCat;
            var catalogGo = KD_SceneObjects.Find(CatalogName, out nCat);
            var catalog = catalogGo == null ? null : catalogGo.GetComponent<KD_ShopCatalog>();
            string catPath = KD_Hillside.DogsRootName + "/" + CatalogName;
            v.Check(nCat == 1 && catalog != null, CatalogName + " に KD_ShopCatalog が 1 個 (actual " + nCat + ")");
            v.Check(catalogGo != null && KD_SceneObjects.PathOf(catalogGo) == catPath,
                "置き場所が " + catPath + " (actual " + (catalogGo == null ? "-" : KD_SceneObjects.PathOf(catalogGo)) + ")");
            int catalogsInScene = KD_ProductsEditor.SceneComponents<KD_ShopCatalog>().Length;
            v.Check(catalogsInScene == 1, "KD_ShopCatalog がシーンにちょうど 1 個 (actual " + catalogsInScene + ") — 選択の正本は 1 つ");
            if (catalogGo != null)
            {
                var ubs = catalogGo.GetComponents<VRC.Udon.UdonBehaviour>();
                v.Check(ubs.Length == 1 && ubs[0].SyncMethod == VRC.SDKBase.Networking.SyncType.None,
                    CatalogName + " の UdonBehaviour は 1 個で SyncMethod None");
            }

            var all = KD_ProductDefinitions.All;
            if (catalog != null)
            {
                v.Check(catalog.ledger != null && catalog.ledger == ledger, "catalog.ledger == " + LedgerHost);
                v.Check(catalog.items != null && catalog.items.Length == all.Length,
                    "catalog.items が商品表と同数 " + all.Length + " (actual " + (catalog.items == null ? 0 : catalog.items.Length) + ")");
                v.Check(catalog.items != null && catalog.items.Select(i => i == null ? null : i.productKey).SequenceEqual(all.Select(d => d.key)),
                    "catalog.items が商品表と 1:1 (同じ並び・欠け/重複なし)");
                v.Check(ledger != null && ledger.products != null && catalog.items != null && ledger.products.SequenceEqual(catalog.items),
                    "ledger.products == catalog.items (台帳の _ConfigureCatalog が同じ登録簿を見る)");
                v.Check(catalog.categoryKeys != null && catalog.categoryKeys.SequenceEqual(KD_ProductDefinitions.Categories), "catalog.categoryKeys == 表の区分");
                v.Check(catalog.defaultProducts != null && catalog.defaultProducts.SequenceEqual(KD_ProductDefinitions.Defaults), "catalog.defaultProducts == 表の既定");
                v.Check(catalog.consumers != null && catalog.consumerEvents != null && catalog.consumers.Length == catalog.consumerEvents.Length,
                    "catalog.consumers と consumerEvents が同数 (裏山段階 1 は 0 本、段階 2 は KD_PetFxBox の 1 本)");
                log.AppendLine("  NOTE catalog.consumers の KD_PetFxBox._OnShopSelectionChanged は Menu/6h Verify Pet Fx が数える (ToyBox は裏山に無い)");
            }

            // ---- 商品 (シーン中の KD_ShopItem すべて) ----
            var shops = KD_ProductsEditor.SceneComponents<KD_ShopItem>();
            v.Check(shops.Length == all.Length, "シーンの KD_ShopItem が商品表と同数 " + all.Length + " (actual " + shops.Length + ") — 表に無い商品が残っていない");
            var seen = new Dictionary<string, string>();
            int bad = 0;
            foreach (var s in shops)
            {
                string tag = s.gameObject.name + ": ";
                if (seen.ContainsKey(s.productKey)) { v.Check(false, tag + "productKey " + s.productKey + " が " + seen[s.productKey] + " と重複"); continue; }
                seen[s.productKey] = s.gameObject.name;
                var row = all.FirstOrDefault(d => d.key == s.productKey);
                var probs = new List<string>();
                if (row == null) probs.Add("表に無い productKey " + s.productKey);
                else
                {
                    if (s.gameObject.name != row.sceneName) probs.Add("名前が " + row.sceneName + " でない");
                    try { KD_ProductDefinitions.ValidateItem(s); } catch (System.Exception e) { probs.Add(e.Message); }
                    if (s.price != row.price) probs.Add("price " + s.price + " != " + row.price);
                    if (s.displayNameKey != HillsideNameKeyOf(row)) probs.Add("displayNameKey " + s.displayNameKey + " != " + HillsideNameKeyOf(row));
                }
                if (catalogGo == null || s.transform.parent != catalogGo.transform) probs.Add("カタログの子でない");
                if (s.ledger == null || s.ledger != ledger) probs.Add("ledger が " + LedgerHost + " でない");
                if (!s.menuOnly) probs.Add("menuOnly でない (Interact で即購入の口になる)");
                if (s.GetComponent<Collider>() != null) probs.Add("Collider がある");
                if (s.GetComponent<Renderer>() != null) probs.Add("描画を持つ");
                if (s.shelf != null) probs.Add("shelf が刺さっている (裏山に棚は無い)");
                if (s.listener != null) probs.Add("listener が刺さっている");
                if (string.IsNullOrEmpty(s.displayName)) probs.Add("displayName が空");
                var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(s);
                string key;
                if (ub == null || ub.SyncMethod != VRC.SDKBase.Networking.SyncType.None) probs.Add("backing が無いか SyncMethod != None");
                else if (!ub.publicVariables.TryGetVariableValue("productKey", out key) || key != s.productKey) probs.Add("backing heap の productKey が違う (CopyProxyToUdon 未了)");
                if (s.IsCE())
                {
                    VRC.Economy.UdonProduct backingProduct;
                    if (ub == null || !ub.publicVariables.TryGetVariableValue("ceProduct", out backingProduct) || backingProduct == null || backingProduct != s.ceProduct)
                        probs.Add("backing heap の ceProduct が UdonProduct アセットを指していない");
                    if (s.ceProduct == null || !AssetDatabase.GetAssetPath(s.ceProduct).StartsWith(KD_ProductDefinitions.CeAssetFolder + "/"))
                        probs.Add("ceProduct が " + KD_ProductDefinitions.CeAssetFolder + " の UdonProduct でない");
                    if (s.ceListingId.StartsWith("TODO-")) probs.Add("Listing が仮 (TODO-)");
                }
                if (probs.Count == 0) continue;
                bad++;
                v.Check(false, tag + string.Join(" / ", probs));
            }
            v.Check(bad == 0, "全商品: 表どおり (区分・価格・パック・CE の Product/Listing) / カタログの子 / menuOnly・Collider なし / 台帳配線 / backing 反映 (不一致 " + bad + " 件)");

            // ---- 初期品 (倒れ先) ----
            {
                var starters = new Dictionary<string, int>();
                foreach (var s in shops) if (s.IsStarter()) starters[s.categoryKey] = (starters.ContainsKey(s.categoryKey) ? starters[s.categoryKey] : 0) + 1;
                foreach (var c in KD_ProductDefinitions.Categories)
                {
                    int n = starters.ContainsKey(c) ? starters[c] : 0;
                    if (c == CategoryLive || c == "experience" || c == "action")
                        v.Check(n == 0, "category " + c + " に初期品が無い (actual " + n + ")");
                    else
                        v.Check(n == 1, "category " + c + " の初期品がちょうど 1 つ (actual " + n + ") — 復元に失敗したときの倒れ先");
                }
                string devNames;
                int dev = CountCeUnlocked(out devNames);
                v.Check(dev == 0, "開発用の CE 解放が掛かっていない (actual " + dev + (dev > 0 ? ": " + devNames : "") + ") — Menu/4c CE Relock で戻す");
            }

            // ---- 撫でFXの札 ----
            int nTag;
            var tagGo = KD_SceneObjects.Find(PlayerFxTagName, out nTag);
            string tagPath = KD_Hillside.DogsRootName + "/" + PlayerFxTagName;
            v.Check(nTag == 1 && tagGo != null && KD_SceneObjects.PathOf(tagGo) == tagPath,
                tagPath + " が 1 個 (actual " + nTag + (tagGo == null ? "" : " at " + KD_SceneObjects.PathOf(tagGo)) + ")");
            if (tagGo != null)
            {
                v.Check(tagGo.GetComponent<VRC.SDK3.Components.VRCPlayerObject>() != null, "KD_PlayerFxTag に VRCPlayerObject (入室ごとに複製される札)");
                var ubs = tagGo.GetComponents<VRC.Udon.UdonBehaviour>();
                v.Check(tagGo.GetComponent<KD_PlayerFxTag>() != null && ubs.Length == 1 && ubs[0].SyncMethod == VRC.SDKBase.Networking.SyncType.Manual,
                    "KD_PlayerFxTag が 1 個で SyncMethod Manual");
            }

            // ---- 棚系は裏山では対象外 ----
            int shelves = KD_ProductsEditor.SceneComponents<KD_ShopShelf>().Length;
            v.Check(shelves == 0, "KD_ShopShelf がシーンに無い (actual " + shelves + ") — 裏山に棚は無い。購入は手元メニュー");
            log.AppendLine("  SKIP 棚の面ラベル・確認パネル・効果音・陳列メッシュ・重なり・棚のグリフ — 裏山では対象外 (棚が無い)");
            log.AppendLine("  SKIP KD_ToyBox の器 (おもちゃの見た目の切り替え) — 裏山では対象外 (IMP-DOG 系の後続で扱う)");
            log.AppendLine("  SKIP おやつ陳列マテリアル — 裏山では対象外 (棚が無い。おやつの実体は IMP-DOG-014)");
            log.AppendLine("  SKIP 手元メニューの行との対応 (KD_ProductsEditor.HasMenuEntry) — 段階 1 ではおやつ・かざり等は準備中。Menu/20 Verify Pouch が見る");

            // ---- PlayerData キー / 自己テスト (古民家版と同じ) ----
            v.Check(CheckPlayerDataKeys(log), "PlayerData キー名 (KD_/KH_ の const) が重複・前方一致していない");
            if (catalog == null) { log.AppendLine("  SKIP 選択の自己テスト (catalog が無い。先に Menu/4 Setup Shop)"); v.AddFail(); }
            else v.AddFail(SelfTestSelection(catalog, log));
            if (ledger == null) { log.AppendLine("  SKIP 在庫の自己テスト (ledger が無い。先に Menu/3a Setup Progress)"); v.AddFail(); }
            else v.AddFail(SelfTestStock(ledger, log));

            v.Finish(HillsideShopVerifyLog);
        }
    }
}
