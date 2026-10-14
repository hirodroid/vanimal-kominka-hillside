# CE 商品台帳 — 裏山の Product ID / Listing ID を貼る場所

**2026-10-14 起票 (ユーザー依頼「古民家ドッグランを参考に課金アイテムの ListingId / ProductId を置く場所を作る」)。**
Storefront で作った ID を **ここに貼るだけで配線に進める**ためのひな形。古民家の
[`docs/development/design/ce-listings.md`](../../../../KominkaDogRun/docs/development/design/ce-listings.md) と同じ役割・同じ置き場所
(裏山に移植したコード [KD_ProductDefinitions.cs](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Editor/KD_ProductDefinitions.cs) 12・32 行と
[KD_ShopItem.cs](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/Progress/KD_ShopItem.cs) 47 行は最初からこのパスを台帳として参照している)。

**本書は ID と Listing 文面の置き場で、価格・商品構成・売り方を決める場所ではない。** 正本は次のとおり:

| 決めごと | 正本 |
|---|---|
| 無料 / Point / Credits の区分、価格、引き継ぎなし、共通購入権はトレーナーだけ | [game-design §4・§5](../../game-design.md) (2026-10-12 ユーザー訂正) |
| トレーナーの共通購入権 (古民家と同じ Product / Listing) と検証条件 | [world-ui-spec §6「トレーナーの共通購入権」](../../world-ui-spec.md#トレーナーの共通購入権-2026-09-20) |
| 洗い場の追加道具のセット構成・価格・インスタンス共有 | [DES-DOG-008](../issues/DES-DOG-008-wash-paid-tools-and-effects.md) (2026-10-05 決定、2026-10-11 Q5) |
| 洗い場の購入画面・所有判定・共有の実装 | [IMP-DOG-056](../issues/IMP-DOG-056-wash-premium-purchase.md) |
| 裏山の課金ライブの商品対応・権利判定 | [DES-LIV-001](../issues/DES-LIV-001-hillside-live.md) |
| 説明・見せ方・購入導線 | [DES-UI-005](../issues/DES-UI-005-trainer-and-paid-items-purchase-flow.md) |

**ここに載せるのは Credits (CE) 商品だけ。** Point 商品 (首輪とリードのセット `collar.*`、裏山専用ライブ `live.hillside_dusk`、飾り・おもちゃ・動作・撫で FX の Point 行) は台帳の `own.{productKey}` で解放し、Storefront の ID を持たないのでここには書かない。

## 1. 作業手順 (古民家 §1 の実測を裏山向けに写した)

1. `vrchat.com/home` → Marketplace → Storefront → My Products → **Create Udon Product** (名前は下表の Listing 名と同じでよい)。発行された `prod_…` が **Product ID** (所有判定用。UdonProduct アセットへ焼く)
2. My Listings → Create Listing → 種別 **Permanent** (買い切り) と価格、画像、説明文を入れ、1 で作った Udon Product を含める。発行された `prod_…` が **Listing ID** (`Store.OpenListing` に渡す)
   - 公式 ([Listings](https://creators.vrchat.com/economy/listings/)、2026-10-14 確認) では 1 つの Listing に複数の Product を入れられる (1〜15。Permanent は Udon / アバター商品、Instant と Temporary は Udon 商品だけ)。古民家の「1 つだけ」は運用上の決まりで、全部入り (§2 C-5) の作り方は §4 で決める
   - **Instant で作らない** (古民家は 2026-09-16 に 6 件を Permanent で作り直した)。**Permanent Listing は購入者が出ると削除できない。** 価格・画像・説明は後から変えられるが、含める Product は慎重に
   - トレーナー (Subscription) は裏山で新しく作らない (古民家の ID をそのまま使う。§2 A)
3. 下表の空欄 2 つに貼る。**両方 `prod_` 始まりなので、取り違えないよう Product → Listing の順で貼る**
4. [KD_ProductDefinitions.cs](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Editor/KD_ProductDefinitions.cs) の定数 (`PlushProduct` / `PlushListing` …) へ同じ値を写す。**表とコードの両方が同じ ID を持つ** (コードが実行時の正本、表は貼る場所)。裏山専用の行は末尾に足す (既存行の並びを動かさない。前例 `live.hillside_dusk`、`collar.*`)
5. Unity (統括が `scripts/unity-api.sh` 経由で実行する。サブエージェントにはさせない。Play・実機・Storefront はユーザー):
   - UdonProduct アセット (`Assets/KominkaHillside/Data/CE/KD_Product_*.asset`) を作って Product ID を焼くのは `KD_ProductDefinitions.EnsureCeAssets` = `Tools/KominkaHillside/Menu/Imported-Unused/Products/1 - Apply Metadata` (撫で FX の `KD_BundledPetFxEditor` からも呼ばれる)。新しい CE 行を足したら先にこれを 1 回
   - 各 `KD_ShopItem` への `ceProduct` / `ceListingId` / `ceCredits` の配線は裏山の正本 `Tools/KominkaHillside/Menu/4 Setup Shop` → `4a Verify Shop` (表の `Validate` / `ValidateItem` がアセットの ID と突き合わせる。`Imported-Unused/Shop/2・3` は古民家版で使わない)
6. 審査は無い (Seller 登録時のみ)。作成後すぐ購入できるので、**Listing の公開スイッチは配線・検証が終わるまで入れない**
7. 検証は ID の一致だけで合格にしない。所有判定は購入情報の読込完了後、読込中 / 未所持 / 取得失敗を分ける ([world-ui-spec §6](../../world-ui-spec.md#トレーナーの共通購入権-2026-09-20)、deco.buy / petfx.buy の契約)。実課金を伴う購入はユーザー確認で行う

## 2. ID 台帳 (ここに貼る)

### A. ワールド共通 (トレーナーだけ。古民家の値をそのまま使う)

| # | Listing 名 | 種別 / 価格 | **Product ID** | **Listing ID** | Unity アセット | 接続先 productKey |
|---|---|---|---|---|---|---|
| A-1 | VAnimal｜ばにまるトレーナー | **Subscription** / 500C・1 か月 (3 / 6 / 12 か月は月 450 / 425 / 400C) | `prod_8c044b21-35f2-4ebb-80f9-e4f685f7bf9f` | `prod_81a5ac65-bd3d-40ad-8edb-2fc7d13b1cee` | `KD_Product_Trainer.asset` | `experience.formation` / `experience.hosting_play` (2 行が同じ ID を共有) |

Product は Listing に含めた「udon」種別の ID。Role 商品の ID を入れるとアップロードが `Unrecognized udon product ID` で失敗する (古民家 2026-09-15 実測)。裏山では芸 (バーン・ジャンプ、[IMP-DOG-054](../issues/IMP-DOG-054-tricks-bang-jump.md)) もトレーナー特典だが、提供前は Listing 文面に載せない (DES-UI-005 2026-10-13 決定)。

### B. 古民家から移植した CE 商品 (裏山用の別 ID が要る。2026-10-14 時点ですべて空欄)

game-design §5 (2026-10-12 ユーザー訂正) により、トレーナー以外は**ワールド別購入**。移植したコードは古民家の Product / Listing ID を定数として持ったままなので (`PlushProduct` … `PumpkinListing`)、このままだと古民家で買った人が裏山でも所有扱いになり、訂正と食い違う (§4-1)。**裏山で売る商品は Storefront で別の Udon Product と Listing を作って下表へ貼り、定数を差し替える。** 売らない商品は行を残しても画面に出さない (`KD_DogDefs.LiveProducts` / 各カタログの掲載が別にある)。

| # | Listing 名 (案。古民家と区別する接尾は未決) | 種別 / 価格 (古民家の現行値) | **Product ID** (裏山) | **Listing ID** (裏山) | Unity アセット | 接続先 productKey | 裏山で売るか |
|---|---|---|---|---|---|---|---|
| B-1 | VAnimal｜小さな王様セット | Permanent / 200C |  |  | `KD_Product_RoyalSet.asset` | `accessory.royal_set` | 初期公開の飾り CE (game-design §5)。要 ID |
| B-2 | VAnimal｜夏休みセット | Permanent / 200C |  |  | `KD_Product_SummerSet.asset` | `accessory.summer_set` | 同上。要 ID |
| B-3 | VAnimal｜キャンディ・ステップ | Permanent / 300C |  |  | `KD_Product_CandyStep.asset` | `live.candy_step_set` / `petfx.candy` | 演目範囲は DES-LIV-001 で未決 |
| B-4 | VAnimal｜ハロウィンセット | Permanent / 300C (古民家は 2026-10-31 23:59 JST まで) |  |  | `KD_Product_PumpkinParade.asset` | `live.pumpkin_parade_set` / `petfx.pumpkin` | 同上。裏山のハロウィンは [パレード企画](../../plans/halloween-parade/plan-a.html) が別にある |
| B-5 | VAnimal｜Kominka Live Pack | Permanent / 200C |  |  | `KD_Product_KominkaLivePack.asset` | `live.kominka_pack` / `petfx.lantern` / `petfx.meteor` / `petfx.confetti` | 同上 |
| B-6 | VAnimal｜ぬいぐるみセット | Permanent / 200C |  |  | `KD_Product_PlushSet.asset` | `toy.paw_cushion` / `toy.taiyaki_plush` / `toy.ebiten_plush` | **初期公開に入れない** (game-design §4 おもちゃ、2026-10-03)。ID 不要のまま |

### C. 洗い場の追加道具 (裏山専用。DES-DOG-008 のセット、2026-10-11 Q5 の中身)

価格は DES-DOG-008 (2026-10-05 ユーザー決定): 各セット 500C、全部入り 1500C (4 つ別々なら 2000C)。中身は 9 品の分類と同じ (Q5)。**1 人が買えば同じインスタンスの全員が選べ、買った人が出ても今の洗いが終わるまで使える** (既存の CE 商品は本人だけが使える方式なので、判定の設計は IMP-DOG-056)。アセット名・productKey は案で、IMP-DOG-056 で確定する。

| # | Listing 名 (案) | 種別 / 価格 | **Product ID** | **Listing ID** | Unity アセット (案) | 接続先 productKey (案) | 中身 (9 品) |
|---|---|---|---|---|---|---|---|
| C-1 | VAnimal｜あらいばセット 泡 | Permanent / 500C | `prod_a538fd94-bd1e-49ab-89bc-3a34d3f89808` | `prod_0b367ec3-d288-4434-9097-93949dd1ec15` | `KD_Product_WashFoam.asset` | `wash.foam_set` | 泡もこもこ銃、肉球ミトン |
| C-2 | VAnimal｜あらいばセット 水 | Permanent / 500C | `prod_fd26e530-34dc-4eee-a186-20d1573987f3` | `prod_e0938881-138f-4c44-b7df-08bb969b76da` | `KD_Product_WashWater.asset` | `wash.water_set` | じょうろ、ホース、水鉄砲 |
| C-3 | VAnimal｜あらいばセット 仕上げ | Permanent / 500C | `prod_c23c31d2-ef0e-45b4-ab3f-f5abf5f041f8` | `prod_f78351ab-9cdc-4a43-997d-073fea2c2381` | `KD_Product_WashFinish.asset` | `wash.finish_set` | ドライヤー、柄タオル |
| C-4 | VAnimal｜あらいばセット 洗い場 | Permanent / 500C | `prod_396c89c7-3bd9-46d3-93af-28983d95feb7` | `prod_0632ffa5-a39e-4459-8ca3-5648c1d43356` | `KD_Product_WashStation.asset` | `wash.station_set` | 檜の洗い台、プール |
| C-5 | VAnimal｜あらいばセット 全部入り | Permanent / 1500C | (無し。C-1〜C-4 の 4 Product を含める Listing) | `prod_a5b387a0-a5ea-4e48-a2a5-d53b3169e02e` | (無し) | (無し。所有は C-1〜C-4 の Product で判定し、この Listing は購入画面を開くだけ) | 上の 4 セット (案 1、2026-10-14 決定) |

リスティング画像 (1024×1024、価格なし、犬なし) は [artifacts/wash-listings/2026-10-13-v05/](../../../artifacts/wash-listings/2026-10-13-v05/README.md) の 5 枚 (9 品の再設計版)。それ以前の撮影分 ([2026-10-13](../../../artifacts/wash-listings/2026-10-13/README.md)、[2026-10-08](../../../artifacts/wash-listings/2026-10-08/README.md)) は保全で、Storefront には使わない。

## 3. Listing 文面 (ユーザー起草。Storefront へそのまま貼る)

古民家と同じく**文面はユーザーが書き、開発 AI は文意を足し引きしない**。§4 の確認事項だけユーザーへ返す。2026-10-14 時点で裏山の文面は 1 本も無い。

- A-1 トレーナー: 古民家の文面 (古民家 ce-listings §3-7) をそのまま使う (同じ Listing なので裏山用の文面は無い)。芸は提供後に文面へ足す
- B-1〜B-5: 古民家の文面を流用するか裏山向けに書き直すかは未決
- C-1〜C-5: 未起草。書くときは「1 人買えば同じインスタンスの全員が使える」「買った人が出ても今の洗いが終わるまで使える」を入れるかをユーザーが決める (古民家のライブ 3 本に「開催すると、その場にいる人はみんな一緒に見られます (買っていない人も)」を足した前例)

### C-1 あらいばセット 泡

```text
(未起草)
```

### C-2 あらいばセット 水

```text
(未起草)
```

### C-3 あらいばセット 仕上げ

```text
(未起草)
```

### C-4 あらいばセット 洗い場

```text
(未起草)
```

### C-5 あらいばセット 全部入り

```text
(未起草)
```

## 4. 食い違い・決めること (台帳では決めない。担当 Issue へ)

| # | 論点 | 現状 | 担当 |
|---|---|---|---|
| 4-1 | **移植コードが古民家の 6 商品 (B-1〜B-6) の Product / Listing ID を持ったまま** | `KD_ProductDefinitions.cs` 33〜38 行。2026-10-12 の「共通はトレーナーだけ」と食い違う。裏山でこれらを画面に出す前に、裏山用 ID へ差し替えるか、行を売り場から外す。コード・シーンは 2026-10-14 時点で未変更 (`Validate` が形式を見るだけで、古民家の ID かどうかは判別しない) | 飾り 2 件は DES-UI-005 の棚卸し、ライブ 3 件は DES-LIV-001、ぬいぐるみは初期公開外 |
| 4-2 | **全部入り (C-5) の作り方** | 公式で 1 Listing に複数 Product を入れられることは確認済み (§1-2)。**案 1**: Listing C-5 に C-1〜C-4 の 4 Product を含める (5 つ目の Product は無し)。所有判定はセットごとの Product を見るだけで済むが、`Row` は 1 行 1 `ceListingId` で、同じ `ceAsset` を別の Listing と共有する行を `Validate` が拒むので、`Store.OpenListing(C-5)` を開く入口を置くには Row の拡張か Listing だけの項目が要る。**未確認: 同じ Udon Product を 2 つの Listing (C-1 と C-5) に入れられるか** (Storefront で確かめる)。**案 2**: 5 つ目の Udon Product `wash.all_set` を作る。Row はそのまま使えるが、所有判定側で「全部入り OR 各セット」を書き、二重の判定を持つ。どちらもコードの変更が要る。費用を見て IMP-DOG-056 で決める。**決定: 2026-10-14 ユーザー「案 1」。Storefront で 1 つの Listing に 4 Product を追加できたことを実測** (同じ Product を C-1〜C-4 の各 Listing にも入れた状態で成立するかは、4 つの Listing を作った時点で確かめる) | IMP-DOG-056 (決定済み。Row の拡張 = Listing だけの項目は 056 §1) |
| 4-3 | **インスタンス共有の所有判定** | 既存 CE は購入者本人の `Store` 判定だけ。洗い場は「誰か 1 人が所有」+「洗い終わりまで継続」+「所有者 0 で無料に戻る」。所有者の退室・途中参加・洗いの終わりの判定が要る。`KH_WashSelection` には購入判定が無い (IMP-DOG-043 は購入なしで先行導入) | IMP-DOG-056 |
| 4-4 | 裏山の Listing 名の接尾 | 古民家と同名の Listing を 2 つ作ると Storefront で見分けにくい。「｜裏山」などを付けるかはユーザー | DES-UI-005 |
| 4-6 | 全部入りと個別の重複購入 | **決定: 2026-10-14 ユーザー。個別のセットを 1 つでも持っている人には全部入りの購入ボタンを非活性にする** (高くなるだけで中身は同じ)。ギフトは古民家 IMP-PRG-012 と同じで、買う前も買った後も Listing を開ける (所有セットのボタンは「ギフトする」)。副作用: 全部入りをギフトできるのはセットを持たない人だけ | IMP-DOG-056 (実装済み) |
| 4-5 | 画像の価格表記・犬なしの既定 | 古民家の既定 (1024×1024、価格なし) を踏襲。C の 5 枚は撮影済み | — |

## 記録

- 2026-10-14: 起票 (ユーザー依頼)。ID はすべて空欄、コード・シーン・Storefront は未変更。公式 Listings の「複数 Product」を確認して §4-2 に記載。同日、Codex 相当の確認で §1-5 (メニューは統括が実行) と §4-2 (案 1 も Row の拡張が要る、未確認は Product の 2 Listing 共有) を訂正。
- 2026-10-14: ユーザーが C-1〜C-4 の Product ID 4 件と C-1〜C-5 の Listing ID 5 件を貼付 (形式検査 OK、9 件とも異なる)。Listing 名は表の案のままで、Storefront 上の実名は未確認。コードの定数への写しは IMP-DOG-056 で行う。
- 2026-10-14: ユーザー決定「案 1」(全部入りは 4 Product を含める Listing、5 つ目の Product は作らない)。Storefront で 1 Listing に 4 Product を追加できたと報告。C-5 の行を Listing だけに直した。ID はまだ空欄。
- 2026-10-14: §2 C の 9 ID をコードへ写した (`KD_ProductDefinitions.cs` の Wash* 定数、IMP-DOG-056)。`KD_Product_Wash{Foam,Water,Finish,Station}.asset` を作成 (ID 一致)、Menu/4a・WashPremium/44 が台帳との一致を検査。Listing はまだ公開していない (実課金の検査の直前にユーザーが有効化)。
