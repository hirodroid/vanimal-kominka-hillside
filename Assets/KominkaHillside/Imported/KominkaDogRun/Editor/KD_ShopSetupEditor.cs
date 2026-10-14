using System.Collections.Generic;
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
    /// IMP-PRG-002 Phase 1: ショップ基盤 v1 のセットアップと検証。
    /// 正本は [shop-foundation.md](../../../docs/development/design/shop-foundation.md)。
    ///
    /// 実行順 (別実行にする — Program Asset 直後の CopyProxyToUdon は outdated error):
    ///   1 - Create Program Assets → force refresh
    ///   2 - Setup Shop
    ///   3 - Verify Shop
    ///
    /// ## `Progress System/1〜3` へ足さない理由
    ///
    /// あちらは**「台帳は何にも配線されない」ことが成立の証拠**になっている
    /// (犬・ボール・カメラのどこも参照しない)。**配線を持つものを混ぜると
    /// Verify の意味が変わる。**
    ///
    /// ## 縦切りの品ぞろえ (Phase 1)
    ///
    /// おもちゃ 2 品だけ。**新規に作るアセットがゼロ**なのがこの 2 品を選んだ理由で、
    /// `KD_ToyMesh_BasicBall` / `KD_ToyMesh_TennisBall` は
    /// [IMP-FET-009](../../../docs/development/issues/IMP-FET-009-toy-box.md) Phase 1 が
    /// 4 犬種のプレハブへ既に入れてある。どちらも回転不変なので咥え向きの問題も踏まない。
    /// </summary>
    public static partial class KD_ShopSetupEditor
    {
        const string ScriptDir = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/";
        const string ProgressDir = ScriptDir + "Progress/";
        const string CatalogScript = ProgressDir + "KD_ShopCatalog.cs";
        const string CatalogProgram = ProgressDir + "KD_ShopCatalog.asset";
        const string ShelfScript = ProgressDir + "KD_ShopShelf.cs";
        const string ShelfProgram = ProgressDir + "KD_ShopShelf.asset";
        const string ToyBoxScript = ScriptDir + "KD_ToyBox.cs";
        const string ToyBoxProgram = ScriptDir + "KD_ToyBox.asset";

        // ---- 効果音 (2026-08-23 ユーザー判断: 漢字間違い探しのシステム音を持ってくる) ----
        //
        // **効果音ラボ (https://soundeffect-lab.info/)。規約上クレジット表記は不要**
        // (KanjiFind `IMP-UI-004` で 2026-08-05 に確認済み。あちらは任意で掲載している)。
        // **システム音はワールドをまたいで同じでよい** — 「決定した / 断られた」は
        // 世界観ではなく操作の意味なので、シリーズで揃っているほうがむしろ自然。
        //
        // 自前で焼く案 (正弦波 + 指数減衰の WAV 生成) は**破棄した** —
        // 既に持っていて権利も確認済みの音があるのに、わざわざ合成音を作る理由が無い。
        const string SfxDir = "Assets/KominkaHillside/Imported/KominkaDogRun/Data/SE/";
        /// <summary>購入の確定。**ファンファーレ (クイズ正解2) は採らなかった** —
        /// 癒しワールドの棚には強すぎる。もっと祝いたくなったらここを差し替える。</summary>
        const string SfxBuy = SfxDir + "決定ボタンを押す8.mp3";
        /// <summary>選択・確認の 1 タッチ目 (軽いクリック)。</summary>
        const string SfxSelect = SfxDir + "決定ボタンを押す2.mp3";
        /// <summary>断られた。**無言にしない** — 「押しても無反応」と区別が付かなくなる。</summary>
        const string SfxReject = SfxDir + "クイズ不正解1.mp3";

        const string CatalogName = KD_DogDefs.ShopCatalogName;
        const string ToyBoxName = KD_DogDefs.ToyBoxName;
        const string ShelfName = KD_DogDefs.ShopShelfName;
        const string TreatShelfName = KD_DogDefs.ShopShelfTreatName;
        const string LedgerHost = KD_DogDefs.ProgressSystemName;

        /// <summary>おもちゃカテゴリの枠番号。**`KD_ShopItem.categoryKey` に意味は無い** —
        /// 何番が何かを決めるのはここ (古民家側) の仕事。</summary>
        const string CategoryToy = "toy";

        /// <summary>棚の名前。**フォントへ焼く字の正本でもある。**</summary>
        const string ShelfTitle = "犬のおもちゃ";

        /// <summary>陳列 1 個の大きさ (長軸 m)。実物のボール径は 0.070〜0.140 なので、
        /// **いちばん大きい犬のボールとほぼ同じ**に見える。
        /// 実寸より大きく見せると「思ったより小さい」が買った後に来る。</summary>
        const float DisplaySize = 0.14f;

        /// <summary>陳列の間隔 (m)。**必ず `DisplaySize` より大きく** —
        /// 下回ると商品同士が重なって押し分けられない (Verify が検査する)。</summary>
        const float DisplayStep = 0.22f;

        /// <summary>陳列オブジェクトの名前。**名前の作り方は 1 箇所に持つ** —
        /// 撤去する側と作る側で食い違うと、消せないゴースト商品が残る。</summary>
        const string ShopItemPrefix = "KD_ShopItem_Toy_";
        static string ShopItemName(ToyDef def) { return KD_ProductDefinitions.SceneName(def.productKey); }

        // ---- 縦切りの品ぞろえ。**恒久 ID は追記のみ。並べ替えない** ----
        //
        // 29 / 30 はテスト盤が予約済みなので使わない (KD_ProgressDebugHudEditor)。
        /// <summary>CE の Listing ID。**1 品 1 Listing・各 100 Credits / Permanent**
        /// (2026-08-23 ユーザー決定。旧案のセット 2 本 200C から変更)。
        ///
        /// **個別売りにしたのは実験のため** — これは収益ではなく
        /// 「共有型 vs 個人装備型で購入行動が違うか」の実験で、
        /// KanjiFind の実績が**購入 23 件 / Visits 約 8,665 (≒0.25%)** と n が小さい。
        /// 100C なら障壁が下がって n が増え、**「どの絵が売れるか」の軸も付いてくる**。
        /// 型ごとに 2 品ずつ・同価格なので**対称性は保たれる**。
        ///
        /// **まだ本物ではない。** VRChat の Listing は
        /// [IMP-FET-010](../../../docs/development/issues/IMP-FET-010-ce-two-products.md) で作る。
        /// 空にすると CE 品が「無料の初期品」として並んでしまうので、
        /// **並びと 🔒 表示を先に成立させるための仮 ID**を入れておく。
        /// `Shop/3 - Verify` が `TODO-` を見つけたら WARN を出す。
        ///
        /// **所有判定には別途 `VRCEconomyUdonProduct` のアセットが要る** —
        /// `Store.DoesPlayerOwnProduct` は文字列ではなく `IProduct` を取る (2026-08-23 実測)。
        /// 「開く」は文字列・「持っているか」はアセット、の二本立てになる。</summary>
        const string CeListingPawPlush = "TODO-IMP-FET-010-paw-plush";
        const string CeListingClothBall = "TODO-IMP-FET-010-cloth-ball";
        const string CeListingSakuraHeart = "TODO-IMP-FET-010-sakura-heart";
        const string CeListingGoldPaw = "TODO-IMP-FET-010-gold-paw";
        /// <summary>VER-FET-001 の手毬 2 案。**比較のための仮 Listing** — どちらかが合格したら
        /// IMP-FET-010 の本物へ差し替え、落選側は行ごと消す (恒久 ID 6 / 7 は追記のみの規則どおり欠番にする)。</summary>
        const string CeListingTemariClaude = "TODO-VER-FET-001-temari-claude";
        const string CeListingTemariCodex = "TODO-VER-FET-001-temari-codex";

        /// <summary>撫でエフェクトの恒久 ID → Listing。**表を 1 箇所に閉じ込める** —
        /// 品ぞろえの正本は `KD_PetHeartsSetupEditor` だが、Listing は CE の都合なのでここ。
        /// 知らない ID が来たら空を返す (= CE ではない扱い)。</summary>
        static string CeListingForEffect(string key) { return KD_ProductDefinitions.Get(key).ceListingId; }

        /// <summary>恒久 ID → **本来あるべき** Listing (おもちゃと撫でエフェクトを横断)。
        /// 空 = そもそも CE 品ではない。
        ///
        /// **「CE であるべきか」の正本はこの関数**で、シーン上の `ceListingId` は
        /// その写し。両者を突き合わせれば「開発用の CE 解放が掛かったままか」が分かる
        /// (<see cref="CeUnlocked"/>)。</summary>
        static string ExpectedCeListing(string productKey)
        {
            foreach (var d in KD_ProductDefinitions.All) if (d.key == productKey) return d.ceListingId;
            return ""; // Unknown/test keys fail product validation; CE diagnostics must continue.
        }

        /// <summary>**開発用の CE 解放が掛かっている品**
        /// (本来 CE なのに `ceListingId` が空 = 無料品として振る舞っている)。
        ///
        /// この状態のまま公開すると**課金品が無料で配られる**ので、
        /// `Release Check/1` が FAIL にする。</summary>
        static bool CeUnlocked(KD_ShopItem s)
        {
            return s != null && ExpectedCeListing(s.productKey).Length > 0 && !s.IsCE();
        }

        /// <summary>シーン全体で開発用 CE 解放が掛かっている品の数 (Release Check が読む)。</summary>
        public static int CountCeUnlocked(out string names)
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var s in Object.FindObjectsOfType<KD_ShopItem>())
            {
                if (!CeUnlocked(s)) continue;
                if (n > 0) sb.Append(" / ");
                sb.Append(s.displayName);
                n++;
            }
            names = sb.ToString();
            return n;
        }

        // =====================================================================
        // 開発用の CE 解放 (2026-09-04 ユーザー依頼「一旦購入しなくても使えるように」)
        //
        // **`ceListingId` を空にするだけ。** そうすると `IsCE()` が false になり、
        // `price == 0` なので `IsFree()` → `Owned()` が true になって、
        // 棚でも手元メニューでも「つかう / つける」が出る。
        //
        // **ランタイムのコードは 1 行も変えない** — 触るのはシーン上のデータだけ。
        // 戻すのは `5 - CE Relock` か、**`2 - Setup Shop` の再実行** (表から書き直すので
        // 黙って元へ戻る = 解放が居座りにくい)。
        // =====================================================================

        [MenuItem("Tools/KominkaHillside/Menu/4b CE Unlock (dev)")]
        public static void CeUnlockDev() { SetCeLocked(false); }

        [MenuItem("Tools/KominkaHillside/Menu/4c CE Relock")]
        public static void CeRelock() { SetCeLocked(true); }

        static void SetCeLocked(bool locked)
        {
            var log = new StringBuilder();
            log.AppendLine(locked
                ? "[KD_ShopSetup] CE Relock — Listing を表から書き戻す"
                : "[KD_ShopSetup] CE Unlock (dev) — **公開前に 5 - CE Relock で必ず戻すこと**");

            int n = 0;
            foreach (var s in Object.FindObjectsOfType<KD_ShopItem>())
            {
                string want = ExpectedCeListing(s.productKey);
                if (want.Length == 0) continue;             // CE 品ではない
                // 裏山改変 IMP-PRG-002 (2026-10-14): 公開しない CE 行は開発用の解放でも無料品にしない (Relock は従来どおり表へ戻す)
                if (!locked && KD_ProductDefinitions.IsWithheldKey(s.productKey)) continue;
                string next = locked ? want : "";
                if (s.ceListingId == next) continue;
                s.ceListingId = next;
                try { UdonSharpEditorUtility.CopyProxyToUdon(s); }
                catch (System.Exception e) { log.AppendLine("   CopyProxyToUdon FAILED: " + e.Message); }
                EditorUtility.SetDirty(s);
                log.AppendLine("   " + s.displayName + " (id " + s.productKey + "): "
                    + (locked ? "→ " + want : "→ 解放 (無料品として振る舞う)"));
                n++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("changed=" + n);
            if (!locked)
                log.AppendLine("NOTE 解放中は Shop/3 - Verify が「初期品がちょうど 1 つ」を "
                    + "解放ぶん多く数える (検査側が承知している)。"
                    + "**Release Check/1 は FAIL する** — 公開前に気づくための設計");
            log.AppendLine("RESULT: PASS (scene not saved yet)");
            KD_VerifyLog.Write("Logs/kd-shop-ce-lock.txt", log);
        }

        /// <summary>撫でエフェクトのカテゴリ枠番号。**個人装備**なので
        /// おもちゃ (共有物) とは同期の性格が正反対。</summary>
        const string CategoryPetFx = "petfx";

        const string PetFxBoxName = KD_DogDefs.PetFxBoxName;
        const string MenuName = "KD_ShopMenu_PetFx";
        const string MenuTitleName = "KD_ShopMenu_PetFx_Title";
        const string MenuTitle = "撫でエフェクト";

        // ---- 撫でFXメニュー一式の基準位置 (棚ローカル) ----
        //
        // **Canvas root / 見出し / 商品アンカー 6 個の 3 者がここから導出される。**
        // 別々の直書きにすると、メニューを動かしたとき 1 箇所だけ取り残されて
        // 黙って位置ずれする (2026-08-28 に見出しで実際に起きた)。動かすのはここ 1 箇所
        static readonly Vector3 MenuBasePos = new Vector3(0.95f, 0.30f, 0f);
        /// <summary>見出しの持ち上げ量 = Canvas 高さ 700px×0.001 の半分 0.35 + 余白 0.07。</summary>
        const float MenuTitleLift = 0.42f;
        /// <summary>商品アンカー先頭行の基準位置 (Canvas 中心からの上方向オフセット)。
        /// 確認パネルはアンカーの**真横** (confirmMenuSideShift) に出る。</summary>
        const float MenuAnchorTop = 0.12f;
        const float MenuAnchorStep = 0.11f;

        /// <summary>メニュー行の確認パネルを逃がす横方向の量 (m)。
        ///
        /// **行は縦に並ぶので、真上に出すと上の行を覆う** — 何を確認しているのか
        /// 分からなくなる。メニュー幅の半分 (460px×0.001 ÷ 2 = 0.23) +
        /// パネル幅の半分 (420px×0.001 ÷ 2 = 0.21) + 余白 0.06。
        ///
        /// **符号は「見る側の左」。棚の面の向き (<see cref="ShelfFaceYaw"/>) に連動する** —
        /// Canvas ローカルの -X は常に見る人の左だが、これを**棚ローカルで表した符号**は
        /// yaw で反転する (yaw 0 なら -X、yaw 180 なら +X)。
        /// この棚は 180 なので **+X が見る側の左**。</summary>
        const float MenuConfirmSideShift = 0.50f;

        /// <summary>おもちゃ棚 (撫でFXメニューを載せている棚) の**面の向き** (Y 回転)。
        ///
        /// **この棚は -Z 側からではなく +Z 側から見る。** 棚の面ラベル
        /// (`KD_ShopShelf_Label`) が 180 で置かれているのがその実体で、
        /// **メニュー / 見出し / 確認パネルもそれに揃える**。
        ///
        /// Canvas・TMP は文字が +X 方向へ進むので、**localRotation 恒等の板は
        /// -Z 側からしか読めない** — 逆側に立つと鏡文字になる。
        /// ここを Setup が毎回書くようになるまでは、手で回しても
        /// Setup を流すたびに恒等へ戻されていた (2026-09-01)。
        /// **向きを変えるならこの 1 行**。</summary>
        const float ShelfFaceYaw = 180f;

        /// <summary>おやつ棚の面の向き。**おもちゃ棚とは逆側から見る**
        /// (こちらの面ラベルは 0 のまま)。棚ごとに違うので定数を分けてある —
        /// 片方を直したらもう片方も動く、にはしない。</summary>
        const float TreatShelfFaceYaw = 0f;

        /// <summary>棚の面ラベルの位置 (棚ローカル)。**実機で見ながら手で決めた値**
        /// (2026-09-01 ユーザー調整。おもちゃ棚 = 商品の上、おやつ棚 = 商品の右下)。
        ///
        /// **シーンにしか無い値にしない。** ラベルを消して Setup を流し直すと
        /// 作り直しになるので、正本がシーンだけだとそこで黙って既定へ落ちる。
        /// 見え方を変えるならこの 2 行 (`MenuBasePos` と同じ扱い)。</summary>
        static readonly Vector3 ShelfLabelPos = new Vector3(0f, 0.42f, 0f);
        static readonly Vector3 TreatShelfLabelPos = new Vector3(0.532f, -0.378f, 0f);

        /// <summary>確認パネルを商品の**どれだけ上**に出すか (m)。棚ごとに違う。
        ///
        /// おもちゃ棚は 0.42 (商品が大きく、腕を伸ばして触るのでボタンを遠ざける)。
        /// **おやつ棚は 0.16** — 商品が小さい (0.08m) ので同じ高さだと
        /// 「どのおやつの話なのか」が切れる。パネル高さの半分 0.075 +
        /// おやつの半分 0.04 + 余白 0.045 = おやつの上端から 4.5cm 上に浮く
        /// (2026-09-01 ユーザー調整)。</summary>
        const float ShelfConfirmHeight = 0.42f;
        const float TreatShelfConfirmHeight = 0.22f;

        /// <summary>おやつの陳列 4 個をまとめてずらす量 (棚ローカル)。
        ///
        /// **並び (間隔と中心合わせ) は品目数から毎回引き直す**ので、手で動かした値は
        /// そのままでは残らない。「どこに並べるか」だけをここで持って、
        /// 並び自体の計算は触らない (2026-09-01 ユーザー調整)。</summary>
        static readonly Vector3 TreatDisplayOrigin = new Vector3(0.025f, 0.212f, 0f);
        // KD_ShopMenu は IMP-PET-007 でクラスごと削除した (2026-09-03)。
        // 撫でエフェクトの売り場は手元メニューの「なで」タブ (KD_TreatPouch)
        const string PlayerFxTagName = KD_DogDefs.PlayerFxTagName;
        const string TagScript = ProgressDir + "KD_PlayerFxTag.cs";
        const string TagProgram = ProgressDir + "KD_PlayerFxTag.asset";
        const string PetFxBoxScript = ScriptDir + "KD_PetFxBox.cs";
        const string PetFxBoxProgram = ScriptDir + "KD_PetFxBox.asset";

        struct ToyDef
        {
            public string productKey;
            public string meshKey;    // おもちゃの key (KD_ToySetupEditor と共通)
            public string label;      // 棚の表示用
            public int price;         // 0 = 初期品 (無料) / CE は pt を持たない
            public string ceListing;  // 空でなければ CE 品
            public ToyDef(string id, string key, string label)
            { productKey = id; meshKey = key; this.label = label; var d = KD_ProductDefinitions.Get(id); this.price = d.price; ceListing = d.ceListingId; }

            /// <summary>ボールプレハブ側の子ノード名。**名前の作り方は 1 箇所に持つ。**</summary>
            public string MeshNode { get { return "KD_ToyMesh_" + meshKey; } }
        }

        /// <summary>棚の品ぞろえ。**恒久 ID は追記のみ。並べ替えない。**
        ///
        /// | | 品 | 入手 |
        /// |---|---|---|
        /// | 0 | 基本ボール | 無料 (初期品・フォールバック先) |
        /// | 1 | テニスボール | 10pt |
        /// | 2 | ドーナツ | 20pt |
        /// | 3 | 肉球 | CE |
        /// | 4 | 布ボール | CE |
        /// | 6 | 手毬 (Claude) | CE 候補 (VER-FET-001 比較中) |
        /// | 7 | 手毬 (Codex) | CE 候補 (VER-FET-001 比較中) |
        ///
        /// [progression-ce.md §4](../../../docs/development/design/progression-ce.md) の
        /// 素案は「無料 1 + Point 3 + CE 2」で、**Point 枠が 1 つ空いている** (30pt の席)。
        /// Bone / Rope は咥え向きが未実装なので、そこが埋まるのはその後。
        ///
        /// **`KD_ToySetupEditor.ShopToys` と品目をそろえること** (Verify が突き合わせる)。</summary>
        static ToyDef[] Toys
        {
            get
            {
                return new[]
                {
                    new ToyDef("toy.basic_ball", "BasicBall", "基本ボール"),
                    new ToyDef("toy.tennis_ball", "TennisBall", "テニスボール"),
                    new ToyDef("toy.donut", "Donut", "ドーナツ"),
                    // 恒久 ID 5 は**後から足したので末尾**。並びは配列順が決めるので、
                    // 表示は「骨 = 30pt」として 2 と 3 の間に見える (番号は動かさない)
                    new ToyDef("toy.bone", "Bone", "骨"),
                    new ToyDef("toy.cloth_ball", "ClothBall", "布ボール"),
                    // VER-FET-001 (2026-09-05): 手毬 2 案。Play で選び比べるための CE 候補
                    new ToyDef("toy.temari_claude", "Temari_Claude", "手毬"),
                    new ToyDef("toy.paw_cushion", "PawCushion", "肉球クッション"),
                    new ToyDef("toy.taiyaki_plush", "TaiyakiPlush", "たい焼きぬいぐるみ"),
                    new ToyDef("toy.ebiten_plush", "EbiTenPlush", "エビ天ぬいぐるみ"),
                };
            }
        }

        // Targeted addition: preserve existing shelf placement, UI and catalog consumers.
        public static void AddPlushCandidates()
        {
            KD_PlushShelfEditor.Guard();
            var shelf = KD_SceneObjects.Find(ShelfName).GetComponent<KD_ShopShelf>();
            var catalog = shelf.catalog; var ledger = catalog.ledger; var toyBox = shelf.toyBox;
            var log = new StringBuilder(); var items = new List<KD_ShopItem>(shelf.items);
            var allItems = new List<KD_ShopItem>(catalog.items);
            var defs = Toys;
            for (int i = 0; i < defs.Length; i++)
            {
                if (System.Array.IndexOf(KD_PlushShelfEditor.MeshKeys, defs[i].meshKey) < 0) continue;
                var item = EnsureShopItem(shelf.transform, defs[i], ledger, i, defs.Length, log);
                if (item == null) throw new System.InvalidOperationException(log.ToString());
                item.shelf = shelf;
                if (!items.Contains(item)) items.Add(item);
                if (!allItems.Contains(item)) allItems.Add(item);
                KD_ProductsEditor.Copy(item);
            }
            shelf.items = items.ToArray();
            catalog.items = allItems.ToArray(); ledger.products = catalog.items;
            KD_ProductsEditor.Copy(catalog); KD_ProductsEditor.Copy(ledger);
            var dogs = KD_DogDefs.All;
            toyBox.productKeys = new string[defs.Length];
            toyBox.meshNodes = new GameObject[defs.Length * dogs.Length];
            toyBox.dogCount = dogs.Length;
            for (int i = 0; i < defs.Length; i++)
            {
                toyBox.productKeys[i] = defs[i].productKey;
                for (int d = 0; d < dogs.Length; d++)
                {
                    var node = KD_SceneObjects.Find(dogs[d].BallName).transform.Find(defs[i].MeshNode);
                    if (node == null) throw new System.InvalidOperationException("Missing toy visual " + dogs[d].key + "/" + defs[i].meshKey);
                    toyBox.meshNodes[i * dogs.Length + d] = node.gameObject;
                }
            }
            KD_ProductsEditor.Copy(shelf); KD_ProductsEditor.Copy(toyBox);
            var language = KD_SceneObjects.Find("KD_Language").GetComponent<KD_Language>();
            var translations = KD_LocalizationEditor.Load();
            KD_LocalizationEditor.Configure(language, translations, log);
            AddGlyphs(KD_LocalizationEditor.BundledCharacters(translations), log);
            KD_LocalizationEditor.Bind(language, translations, log);
            KD_ProductsEditor.Copy(language);
            KD_ToyCatalogEditor.WirePickupText(toyBox);
            EditorSceneManager.MarkSceneDirty(shelf.gameObject.scene);
            Directory.CreateDirectory("Logs/plush-v3"); File.WriteAllText("Logs/plush-v3/shelf-apply.txt", log.ToString());
        }

        // ================================================================ おやつ (IMP-TRT-001 Phase 4b)
        //
        // **ショップ基盤 v1 の 3 型目 = 消耗品と在庫。** これが通ると基盤の検証範囲が閉じる。
        //
        // 売り場は**実物棚** (2026-08-24 ユーザー決定で「メニュー」から移した) —
        // 判断基準は変わっておらず「プレビューできる形が正解」のままで、
        // **クッキーには実物がある**ことに気づいたのが変更点 (shop-foundation §7)。

        /// <summary>おやつカテゴリの枠番号。**0 = おもちゃ / 1 = 撫でエフェクトは使用済み。**
        /// `KD_ShopItem.categoryKey` 自体に意味は無く、何番が何かを決めるのはここ (古民家側)。</summary>
        const string CategoryTreat = "treat";

        /// <summary>IMP-PRG-004 Phase 3: ParticleLive (開催権) のカテゴリ。**MaxCategories (4) の最後の枠。**
        /// 型は「買い切り・個人の発動権」で、選択 (カーソル) に意味は無い —
        /// 手元メニューのライブタブが `Owned()` を読んで [開催] を出すだけ。
        /// 品ぞろえの正本は `KD_DogDefs.LiveProducts` (`KD_LiveSetupEditor.GetShopEntries` は**商品順**。
        /// 作品との対応は `KD_DogDefs.LiveShows[i].productKey` — IMP-PRG-005)。</summary>
        public const string CategoryLive = "live";   // 手元メニューの Setup / Verify が読む

        /// <summary>おやつ棚の名前。**フォントへ焼く字の正本でもある。**</summary>
        const string TreatShelfTitle = "犬のおやつ";

        const string TreatModelDir = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Treat/";
        const string TreatMaterialDir = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/";

        /// <summary>おやつの陳列ノード名の接頭辞。**おもちゃ (`KD_ToyMesh_`) と分ける** —
        /// 同じ接頭辞にすると、片方の「期待する 1 個以外を畳む」処理がもう片方を巻き込む。</summary>
        const string TreatMeshPrefix = "KD_TreatMesh_";

        /// <summary>おやつの陳列 1 個の大きさ (長軸 m)。
        ///
        /// **おもちゃ (<see cref="DisplaySize"/> = 0.14) より小さい。**
        /// クッキーはボールより小さい食べ物で、同じ寸法で並べると
        /// 「ボールくらいのビスケット」に見えて実物のプレビューにならない。
        /// ポメのボール (0.070) と同じか少し上、という見当で 0.08。
        /// **実寸より大きく見せない** — 「思ったより小さい」が買った後に来る。</summary>
        const float TreatDisplaySize = 0.08f;

        /// <summary>おやつの陳列の間隔 (m)。**必ず <see cref="TreatDisplaySize"/> より大きく** —
        /// 下回ると商品同士が重なって押し分けられない (Verify が検査する)。
        /// おもちゃ側より詰まって見えないよう、比 (0.22/0.14 ≒ 1.57) をおよそ保つ。</summary>
        const float TreatDisplayStep = 0.13f;

        const string ShopItemTreatPrefix = "KD_ShopItem_Treat_";
        static string TreatItemName(TreatDef def) { return KD_ProductDefinitions.SceneName(def.productKey); }

        struct TreatDef
        {
            public string productKey;       // Stable product key; scene names only retain their original suffix.
            public string meshKey;     // モデルのフォルダ名 = FBX 名
            public string label;       // 棚の表示用
            public int price;          // 1 パックの値段 (pt)
            public int packCount;      // 1 パックの個数
            public int initialStock;   // 入室時に一回限り配る個数 (基本のおやつだけ 5)
            public int stockCap;       // 所持上限。**超過は許す** (残り 18 個でも 5 個入りを買える)

            public TreatDef(string id, string key, string label)
            {
                productKey = id; meshKey = key; this.label = label; var d=KD_ProductDefinitions.Get(id); this.price=d.price; this.packCount=d.packCount; this.initialStock=d.initialStock; this.stockCap=d.stockCap;
            }

            public string FbxPath { get { return TreatModelDir + meshKey + "/" + meshKey + ".fbx"; } }
            public string BaseColorPath
            {
                get { return TreatModelDir + meshKey + "/" + meshKey + ".fbm/" + meshKey + "_basecolor.JPEG"; }
            }
            public string MaterialPath { get { return TreatMaterialDir + "M_KD_Treat_" + meshKey + ".mat"; } }
            /// <summary>陳列ノード名。**名前の作り方は 1 箇所に持つ。**</summary>
            public string MeshNode { get { return TreatMeshPrefix + meshKey; } }
        }

        /// <summary>おやつの品ぞろえ。**恒久 ID は追記のみ。並べ替えない。**
        ///
        /// | id | 品 | 価格 | 1 パック | 初期在庫 | 上限 |
        /// |---:|---|---:|---:|---:|---:|
        /// | 20 | 骨型ビスケット (**基本**) | 1pt | 5 | **5** | 20 |
        /// | 21 | 肉球クッキー | 2pt | 5 | 0 | 20 |
        /// | 22 | 星型クッキー | 3pt | 5 | 0 | 20 |
        /// | 23 | ジャーキー | 5pt | 5 | 0 | 20 |
        ///
        /// 使用済み**だから ([IMP-TRT-001](../../../docs/development/issues/IMP-TRT-001-treat-system.md))。
        ///
        /// **無料品が 1 つも無い** — 2026-08-22 に無料枠を撤回したため。
        /// 倒れ先は `KD_ShopItem.IsStarter()` (= 初期在庫を配る品) が担う。
        /// **`price == 0` の品を足して埋めようとしないこと** — 撤回した無料枠が戻る。
        ///
        /// **CE にはしない** (`ceListingId` は空のまま)。リアルマネーで買った消耗品が
        /// 「他人に取られる / 犬が食べて一瞬でなくなる」に晒されると課金体験がシビアになる。</summary>
        static TreatDef[] Treats
        {
            get
            {
                return new[]
                {
                    new TreatDef("treat.bone_biscuit", "BoneCookie", "骨型ビスケット"),
                    new TreatDef("treat.paw_cookie", "PawCookie", "肉球クッキー"),
                    new TreatDef("treat.star_cookie", "StarCookie", "星型クッキー"),
                    new TreatDef("treat.jerky", "Jerky", "ジャーキー"),
                };
            }
        }

        // ================================================================ 1: Program assets

        [MenuItem("Tools/KominkaHillside/Menu/3 Create Shop Programs")]
        public static void CreateProgramAssets()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ShopSetup] Create program assets");

            bool ok = true;
            ok &= EnsureProgram(CatalogScript, CatalogProgram, log);
            ok &= EnsureProgram(ShelfScript, ShelfProgram, log);
            // (KD_ShopMenu の Program Asset は IMP-PET-007 で廃止した)
            ok &= EnsureProgram(ToyBoxScript, ToyBoxProgram, log);
            ok &= EnsureProgram(PetFxBoxScript, PetFxBoxProgram, log);
            ok &= EnsureProgram(TagScript, TagProgram, log);
            AssetDatabase.SaveAssets();

            log.AppendLine(ok
                ? "RESULT: PASS (force refresh してから 2 - Setup を実行する)"
                : "RESULT: FAIL");
            KD_VerifyLog.Write("Logs/imp-prg-002-program.txt", log);
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

        // ---- helpers (他の SetupEditor と同型) ----

        static bool TryCopy(UdonSharpBehaviour proxy, StringBuilder log)
        {
            if (proxy == null) return false;
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
