---
id: IMP-PRG-002
type: IMP
system: PRG
status: wip
priority: High
summary: 古民家から移植した CE アイテムを今回の公開対象から外し、選択・購入・案内の入口と旧商品 ID の参照を整理する
next: "ソース反映済み・シーン適用済み (2026-10-14。4a・3b・6h・20 PASS、6f の FAIL 2 件は首輪の作り直し後の既存問題で本票と無関係)、看板 A 4 枚目の文言も削除済み。残り: 古民家で買った人の入室・途中参加・他人の値の実機 2 人確認、トレーナー・洗い場・首輪の Play 確認"
updated: 2026-10-14
decided: 2026-10-14
---

# IMP-PRG-002 移植した CE アイテムを今回の公開対象から外す

## ユーザー決定 (2026-10-14 再確認)

「移植したアイテム類は今回外すように決定したんだけど、それのイシューはなかったっけ？なかったらイシュー立てといてください。」

既に外すと決めた旨を今回再確認した。以前の決定日は特定していない。専用 Issue が見当たらず、商品台帳には ID 差し替え／除外の選択が残っていたため、本票で追跡する。**今回は外す方針であり、裏山用 ID の作成や販売可否の再決定を待つものではない。**

## 対象

直前の未実装整理で話題にした [CE 商品台帳 §2 B](../design/ce-listings.md) の移植商品を対象の起点とする。商品と、その権利で解放されるアイテム・演目・撫で FX を照合する。

| 移植商品 | 主な接続先 |
|---|---|
| 小さな王様セット | `accessory.royal_set` |
| 夏休みセット | `accessory.summer_set` |
| キャンディ・ステップ | `live.candy_step_set` / `petfx.candy` |
| ハロウィンセット | `live.pumpkin_parade_set` / `petfx.pumpkin` |
| Kominka Live Pack | `live.kominka_pack` / `petfx.lantern` / `petfx.meteor` / `petfx.confetti` |
| ぬいぐるみセット | `toy.paw_cushion` / `toy.taiyaki_plush` / `toy.ebiten_plush` (既に初期公開対象外。除外漏れを点検) |

共通トレーナー、裏山専用の洗浄商品、首輪・リードの Point 商品は本票で除外しない。Point／無料の移植品まで一律に削除する決定とは扱わない。裏山専用ライブ・パレードの企画は DES-LIV-001／002 で別に管理し、同名の古民家版商品と混同しない。

## 作業範囲

- 商品定義、各カタログ、メニュー、購入確認、ギフト、棚・看板・紹介文から対象商品へ至る入口を一覧にする。
- 今回の公開版で対象商品が選択・購入・利用できないようにする。移植元の Product / Listing ID のまま古民家の商品ページを開く入口を残さない。
- 古民家側の購入情報や既存のローカル保存値を受け取っても、除外品が再表示・自動装着・再選択されないことを確認する。空の分類や無効なボタンが残る場合も整理する。
- 共有のトレーナー判定や基本機能、採用中の商品へ影響しないよう依存を確認する。ファイルの物理削除はこの起票だけで前提にせず、公開対象・配線を外す範囲を決める。
- 商品台帳・販売案内・関連 Issue の古い採用／ID 待ち記述を最新の除外方針へ揃える。古民家側の Storefront 商品や元プロジェクトは変更しない。

## 棚卸し (2026-10-14、実装担当 Opus C)

対象は 6 アセット・13 行: `accessory.royal_set` / `accessory.summer_set` / `live.candy_step_set` + `petfx.candy` / `live.pumpkin_parade_set` + `petfx.pumpkin` / `live.kominka_pack` + `petfx.lantern` / `petfx.meteor` / `petfx.confetti` / `toy.paw_cushion` / `toy.taiyaki_plush` / `toy.ebiten_plush`。パスは `Assets/KominkaHillside/Imported/KominkaDogRun/` からの相対、行番号は変更後。

| 入口 | ファイル・行 | 変更前の状態 | 除外の方法 |
|---|---|---|---|
| 商品定義 (行と古民家の ID) | `Editor/KD_ProductDefinitions.cs` 35〜40 (ID 定数)・70〜72 / 92〜98 / 106〜110 (行) | 13 行が古民家の Product / Listing ID を持つ | 行・キー・ID 定数は残す (キーは再利用しない)。公開しない表 `HillsideWithheldCeAssets` (140 行) を足し、`Validate` が「実在する CE 行・既定品 / 洗い場 / トレーナーを含まない」を見る |
| UdonProduct アセット | `Assets/KominkaHillside/Data/CE/KD_Product_{RoyalSet,SummerSet,CandyStep,PumpkinParade,KominkaLivePack,PlushSet}.asset` | 13 個の `KD_ShopItem.ceProduct` から参照 (シーンの GUID 参照 2/2/4/4/8/6) | `Apply` (192 行) が公開しない行に刺さない → シーンから参照 0 → ワールドに入らない。ファイルは残す |
| カタログ・台帳 | `Editor/KD_ShopSetupEditor.Hillside.cs` (Menu/4) → `KD_ProductsEditor.BindCatalog` | 13 行が `catalog.items` / `ledger.products` に CE として登録 | 行は登録したまま (おもちゃ・古民家ライブと同じ「台帳のために残す」)。UdonProduct は登録されない |
| 所有 (購入情報の読込・購入通知) | `Scripts/Progress/KD_ShopItem.cs` 88 (`OnPurchasesLoaded`)・148 (`NoteCePurchase`)・252 (`CeOwnedBy`) | 古民家で買った人は Store の購入情報で所有が付く (Product ID が同じ) | `ceProduct == null` の既存の早期 return で所有が付かない (`Owned` / `OwnedBy` は常に偽) |
| 購入画面 (Store.OpenListing) | `KD_ShopItem.cs` 264 (`_OpenStore`)。呼び手: `KD_TreatPouch.Acc.cs` 137 / 149、`.PetFx.cs` 77、`.Live.cs` 203 / 210 | 古民家の Listing を開く | `_OpenStore` は `CeWithheld()` (387 行 = `IsCE() && ceProduct == null`) なら開かない。呼び手の一覧からも外す |
| 手元メニュー: かざり | `Editor/KD_AccessorySetsEditor.Hillside.cs` (`HillsideWire`) → `pouch.accProductIds` / `accItems` ほか。`Scripts/KD_TreatPouch.Acc.cs` 412 (`AccProductRow`) | セット 4 件 (100 / 101 / 103 / 104) が 200C / 300C の行で出る。ギフトも | 一覧 (表示用・非同期) を `HillsidePublishedSelections` にする: 29 → 13 件、見える行 14 → 10 (偶数のまま。撫でFX の見出しが段の左端) |
| 手元メニュー: 撫でFX | `pouch.fxItems` (書き手 Menu/7 は周回版で拒否 → Menu/6g でも書く)、`Scripts/KD_TreatPouch.PetFx.cs` 133・162 | 同梱 5 種が「○○同梱 300 C」で出る | 5 枠を null にする。`RedrawFxRows` は商品の無い枠を行ごと隠す (既存の扱い) |
| 手元メニュー: ライブ・ハロウィン帯 | `Editor/KD_DogDefs.cs` `LiveShows` / `LiveProducts`、`KD_TreatPouch.Live.cs` 107 (`HalloweenProductKey`) | 裏山はもともと `live.hillside_dusk` だけ (DES-LIV-001 D1)。ハロウィン帯は liveItems に無いので出ない | 変更なし (Menu/4a の参照検査で、liveItems / hostingItems が公開しない行を指さないことを確かめる) |
| おもちゃ (ぬいぐるみ) | `KD_ToyBox` (Course/124 のボールの柄)、`Editor/KD_ToyCatalogEditor.cs` 19・`KD_PlushShelfEditor.cs` 15 (古民家版・Imported-Unused) | 裏山はカタログの行だけで、棚もおもちゃの選択肢も無い (ハーネスの ToyRow は `toy.paw_cushion` を偽装キーとして拒否を確かめている) | 変更なし (他の行と同じく Product を外す) |
| 購入確認・ギフト | `KD_TreatPouch.Confirm.cs` (`_ShowItemConfirm` / `_ShowGiftConfirm`)、`.Live.cs` 62 (`CanGift`) | CE セットの購入確認と所有者のギフト | 一覧に無いので開けない。`CanGift` は `Owned` が偽で不成立 |
| 選択の保存 (PlayerData) | `Scripts/Progress/KD_ShopCatalog.cs` 43 (`Sel.{分類}`)・125 (`ResolveSelection`) | 撫でFX 等の選択キーを文字列で保存。所有があれば復元 | 所有が付かないので既定 (♡ など) へ倒れる。保存値は書き換えない (ユーザーが選び直すまで残る)。CE は `Own` / `StockB` / `StockE` を書かない (台帳は CE を読まない) |
| 飾りの同期 (他人の画面) | `Scripts/KD_AccessoryBox.cs` 100 (`_slotProduct` = 恒久 ID)、264 (`PrepareSelection`)・327 (`ProductOwnedBy`)、424 (`Apply`) | 恒久 ID 0..23 / 100..104。Owner が所有で検証 | 恒久 ID・nodes の並びは変えない。Owner は所有が無いので拒否。`Apply` は `CeWithheld` の品目を同期値が指していても着けない (何も着けない = 既定) |
| 撫でFX の同期 (他人の撫で) | `Scripts/Progress/KD_PlayerFxTag.cs` 12 (`_selectedFx` = 商品キー)、`Scripts/KD_PetFxBox.cs` 154・171 | 撫で主の札のキーをカタログで引き、所有を見て描く | 所有が偽なので既定 ♡ で描く (コード変更なし) |
| 開発用の CE 解放 | `Editor/KD_ShopSetupEditor.cs` (Menu/4b CE Unlock) | Listing を空にして無料品として振る舞わせる | 公開しない行は解放しない (Relock は従来どおり) |
| 看板・紹介文 | `Data/UITranslations.json` の `guide.shop.body` (看板 A 4 枚目、`KH_GuideBoardsEditor.cs` 150) | 末尾に「Credits のかざり / 「かざり」の一部のセットは VRChat の購入画面で (ギフトも)。」 | **2026-10-14 統括が末尾 2 行を 13 言語で削除済み**。`accessory.Royal` / `live.detail.*` / `event.halloween.*` / `fx.bundle.*` などの文言は残るが、表示する入口が無い |
| 見た目の実体 (容量) | 飾りのノード 12 品 × 6 匹 = 72 (`KD_Acc_{Crown,RoyalNecklace,RoyalTail,Sunglasses,SunflowerNecklace,MarineCap}_*`、`KD_CandyCostume_*`、`KD_PumpkinCostume_*`) と試着のミニチュア、同梱の粒 5 種 × 2 層 × 6 匹 = 60 | シーンにあり、ワールドに入る | 残す (削除は別件)。ノードは既定で外れている (6f が検査)。素材の元ファイルは飾り約 2.0 MB (Crown / ThemeSets の 5 品 / CandyStep / PumpkinParade)、粒のテクスチャ約 1.7 MB (Candy 564 KB / Confetti 476 KB / Lantern 640 KB ほか)。ビルド後の容量ではない (Build Size/7 → 2 で実測) |

## 除外の方法 (2026-10-14)

- **一覧は 1 か所**: `KD_ProductDefinitions.HillsideWithheldCeAssets` (UdonProduct アセット名 6 つ)。13 行はアセットから導く (`IsWithheld` / `IsWithheldKey` / `WithheldRows`)。新しいフラグは KD_ShopItem に足さない。実行時の判定 `KD_ShopItem.CeWithheld()` は既存の 2 値 (`ceListingId` がある・`ceProduct` が無い) から導く。
- **行を抜かない**: カタログ・台帳・飾りの台帳の配列から要素を抜くと、添字や恒久 ID が別の品に化けうるので抜かない。`ceListingId` は表の値のまま (空にすると `IsFree()` が真になり、全員の所有になる)。
- **ゲート**: (1) UdonProduct を刺さない → 所有が付かない (2) `_OpenStore` が開かない (3) 表示用の一覧 (かざり `accProductIds` ほか、撫でFX `fxItems`) から外す (4) 飾りの台帳は同期値が指していても着けない。撫でFX は所有の検査で既定に倒れる。
- **文言の束縛表も合わせる**: `Data/UILocalizationBindings.json` の `names` の `KD_TreatPouch:accNameKeys` と `KD_TreatPouch:accSlotNameKeys` の 2 つも 29 → 13 に縮めた (2026-10-14 統括。accNameKeys は公開する 13 キー Bandana, TailRibbon, BowTie, Cap, SpiralGlasses, StrawHat, TopHat, StarGlasses, Backpack, Floral, FlowerCrown, FlowerCorsage, FlowerTail、accSlotNameKeys は同じ並びの先頭 13)。Menu/11 Setup Localization がこの表をシーンへ書き戻すため、29 件のままだと accItems 13 と食い違い Guide/2 (`KD_LocalizationEditor.ValidateScene`) が「Stale name binding」で FAIL する。**6e の一覧 (`HillsidePublishedSelections`) を変える時はここも合わせる**。
- **触らないもの**: 共通トレーナー (A-1 の 2 行)、洗い場の 4 セット (C-1〜C-4) と全部入り、Point / 無料の移植品 (飾り 9 品 + お花のおめかし、撫でFX 6 種、おもちゃ 6 種、追加動作、おやつ、首輪とリード)、犬の基本の遊び。`Validate` が既定品・洗い場・トレーナーを公開しない表に入れられないようにした。
- **検査**: Menu/4a の IMP-PRG-002 の節 (6 アセット / 13 行、UdonProduct を参照する KD_ShopItem が 0、公開しない行は `CeWithheld` で無料・初期品に化けていない、シーンの U# の `KD_ShopItem` / `KD_ShopItem[]` フィールドで公開しない行を指すのは台帳・カタログ・飾りの台帳だけ、行は台帳・カタログに残る)。6f は一覧 13 件・見える行 10 (偶数)・公開しない品のノード 72 個が外れている、6h と 20 は撫でFX の 5 枠が null。差分の一覧は [kominka-dog-system](../imports/kominka-dog-system.md#2026-10-14-移植した-ce-6-商品を公開しない-imp-prg-002)。

### 統括が流す Unity の手順と期待値

1. `bash scripts/unity-compile.sh --udon` → C# 0 / U# 0 (`KD_ShopItem` / `KD_AccessoryBox` を変更)
2. `Menu/4 Setup Shop` → `SETUP RESULT: PASS`、ログに「IMP-PRG-002: 公開しない CE 6 アセット / 13 行は ceProduct なし」
3. `Menu/6e Setup Accessories` → PASS、「IMP-PRG-002: 一覧から公開しない 16 件を外した (100,0,5,6,101,9,7,8,103,18,19,20,104,21,22,23)」「手元メニューの一覧 13 件 (見える行 10)」。既存ノードの位置は触らない (首輪の (a)(b) は `ApplyCollarShift` が古民家の基準 + shift の絶対値で置くので再適用で同値。手で詰めた位置は保持)
4. `Menu/6g Setup Pet Fx` → PASS、「pouch.fxItems: 6 件 + 公開しない 5 枠は null」
5. 検査: `Menu/4a` (IMP-PRG-002 の節が全 PASS、他の参照 0 件)、`3b` (products 数は不変)、`6f`、`6h`、`20`、`22`、`25`。保存前は Menu/20 の NetworkIDs が変わらない (オブジェクトを消していない)
6. シーン保存 → 独立チャネル: `.unity` で 6 アセットの GUID (RoyalSet `76bab010…` / SummerSet `e17aa336…` / CandyStep `fc944da2…` / PumpkinParade `201e16aa…` / KominkaLivePack `35bae113…` / PlushSet `89ef0338…`) がそれぞれ 0 件 (変更前 2/2/4/4/8/6)。古民家の Listing ID 6 種の文字列は `ceListingId` として 13 行に残る (変更前と同数。経路は `_OpenStore` と一覧で閉じた)

## 完了条件

- [x] 対象 6 商品と関連するアイテム・表示・購入経路の一覧があり、公開版からの除外方法を記録している。(2026-10-14 上の棚卸しと除外の方法)
- [ ] 手元メニュー・現地の案内に選択／購入の入口がなく、移植元の商品 ID へ到達しない。— **ソース反映済み・シーン適用済み** (2026-10-14 統括: Menu/4a・6h・20 PASS)。看板 A 4 枚目の文言も削除済み。実機での確認待ち
- [ ] 購入情報の読み込み・保存値の復元・再入室・途中参加で除外品が復活しない。— コード上の経路は上の表のとおり閉じた。ClientSim は自分の購入しか模擬できず、**古民家で買った人の入室・途中参加・他人の札・同期値は実機 2 人** (未確認)
- [ ] トレーナー・洗浄商品・首輪セットと基本の犬の遊びが維持されている。— 表の行・Product・配線は不変 (`Validate` が除外できないようにした)。Menu/4a・WashPremium/44・Collar の Verify と Play で確認する (未実行)
- [ ] 仕様・台帳・紹介文・関連 Issue が除外方針と一致し、実機確認の結果と未確認条件を記録している。— ce-listings §2 B・§4-1、game-design §4、world-ui-spec の deco.buy / petfx.buy (CE)、DES-UI-005、IMP-DOG-015、kominka-dog-system を更新。紹介文 (`guide.shop.body`) は統括が削除済み。実機の結果が残る

## 関連・起票時の状態

- [DES-UI-005](DES-UI-005-trainer-and-paid-items-purchase-flow.md): 販売案内と購入導線。移植品の紹介を外す。
- [IMP-DOG-015](IMP-DOG-015-accessories-and-petfx-port.md): 飾り・撫で FX の移植記録。対象の CE 品を本票で除外する。
- [DES-LIV-001](DES-LIV-001-hillside-live.md) / [DES-LIV-002](DES-LIV-002-live-lineup-and-parade.md): 裏山向けライブ企画と販売範囲。
- [IMP-DOG-056](IMP-DOG-056-wash-premium-purchase.md): 洗浄商品の購入接続。移植品の整理は同票の範囲外で、本票が引き取る。

(起票時点 2026-10-14 の記録) 今回の作業は既存記録の検索、起票と文書への決定反映のみ。コード・シーン・販売設定の変更、参照の網羅確認と実機検証は未着手。→ 同日の進捗は下の記録。

## 記録

- 2026-10-14: 棚卸しと除外をコードへ反映 (実装 Opus C、統括 Fable)。変更: `KD_ProductDefinitions.cs` (公開しない表・`Apply`・`Validate`・`ValidateItem`)、`KD_ShopItem.cs` (`CeWithheld`・`_OpenStore`)、`KD_AccessoryBox.cs` (`Apply`)、`KD_AccessorySetsEditor.Hillside.cs` (一覧と 6f の検査)、`KD_BundledPetFxEditor.Hillside.cs` (6g が fxItems を書く・6h)、`KD_TreatSetupEditor.Hillside.cs` / `.Verify.cs` (Menu/7 の探索・Menu/20)、`KD_ShopSetupEditor.Hillside.cs` (Menu/4 のログ・4a の IMP-PRG-002 の節)、`KD_ShopSetupEditor.cs` (4b)、`KD_ProductsEditor.cs` (Imported-Unused の Products/2)。コンパイル・メニュー実行・保存は未 (統括)。Imported-Unused の `Archive/Accessories/Sets - Verify Integration` (古民家の 29 件を前提) は保守しない。
- 影響する既存検査: Menu/4a (IMP-PRG-002 の節を追加。公開しない行は proxy・backing とも ceProduct なしが正)、3b (products 数は不変)、6f (一覧 13 件・見える行 10・ノード 72 個が外れている)、6h・20 (撫でFX の 5 枠は null が正。残っていれば FAIL)。Imported-Unused の Products/2 は公開しない行を除いて検査。`Archive/Accessories/Sets - Verify Integration` (29 件前提) と Menu/21a のプレビュー (撫でFX の枠を null 検査なしで読む箇所がある) は対象外・未保守。4a の参照検査で `KD_ToyBox.productItems` が引っかかったら、Course/124 の柄にぬいぐるみが入っているという意味なので許可表に足さずに報告する。
- 2026-10-14 (統括): `guide.shop.body` の末尾 2 行「Credits のかざり」「「かざり」の一部のセットは VRChat の購入画面で (ギフトも)。」を 13 言語で削除 (反映済み)。Credits で買えるのはトレーナーと洗い場のセット。
- 2026-10-14 (統括) シーン適用: Menu/4 → 6e → 6g → 4a / 3b / 6f / 6h / 20 / 25。4a・3b・6h・20 PASS。6f の FAIL 2 件 (首輪の対策 (b) の 5 組が置いた位置でも首輪に入り解消 0、案 C の外で当たる胸の品 9 件) は IMP-DOG-050 で首輪を作り直した後に (b) の実測値 (2026-09-24) が古くなった既存の問題で、本票の変更とは無関係 (別件。IMP-DOG-015 に記録)。Codex 独立レビュー: 高・中なし、低 2 件を反映 (Products/2 Verify が公開しない行の backing の ceProduct も null か見る、本票の状態の書き分け)。
- 別件候補: (1) 公開しない飾り 12 品・粒 5 種の実体 (ノード・試着のミニチュア・粒のインスタンスと素材) の容量整理は別 Issue 候補。外すかは容量の実測 (Build Size/7 → 2) を見て決める。本票では削除しない。(2) Imported-Unused の `Archive/Accessories/Sets - Verify Integration` は 29 要素前提で失敗する (未保守。一覧は 13 件になった)。
