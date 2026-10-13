---
id: IMP-DOG-056
type: IMP
system: DOG
status: todo
priority: Medium
summary: 洗い場の追加道具 5 セット (泡 / 水 / 仕上げ / 洗い場 各 500C、全部入り 1500C) を Credits 商品として接続する。Storefront の ID は ce-listings.md、所有判定は「インスタンスに 1 人でも所有者がいれば全員が選べる、洗い終わりまで継続」
next: "案 1 で決定 (2026-10-14)。ユーザー: Storefront の Udon Product 4 と Listing 5 の ID を ce-listings.md §2 C に貼る (全部入りは Listing ID だけ)、Listing 文面の起草。AI: 貼られたら商品定義の行追加・所有判定の設計 (§2) → Codex 相談 → 実装"
updated: 2026-10-14
---

# IMP-DOG-056 洗い場の追加道具を Credits 商品として接続する

2026-10-14 起票。[IMP-DOG-043](IMP-DOG-043-wash-premium-selection.md) は購入なしで使える写真選択・道具を先行導入し、「購入画面・商品 ID・所有権判定は次工程」と残していた。その次工程がこの Issue。企画と価格は [DES-DOG-008](DES-DOG-008-wash-paid-tools-and-effects.md) (2026-10-05 ユーザー決定、2026-10-11 Q5)、ID の置き場は [CE 商品台帳](../design/ce-listings.md) §2 C。ここで新しく価格・構成を決めない。

## 決まっていること (DES-DOG-008)

- 4 セット (泡 / 水 / 仕上げ / 洗い場) + 全部入り。各 500C、全部入り 1500C。中身は 9 品の分類と同じ (泡 = 泡銃・ミトン、水 = じょうろ・ホース・水鉄砲、仕上げ = ドライヤー・タオル、洗い場 = 檜・プール)。
- 古民家とは共通にしない (裏山だけの商品。共通購入権はトレーナーだけ、game-design §5 2026-10-12)。
- **1 人が買えば、同じインスタンスの全員が選べる。** 洗い場の見た目もこれに従う。
- 買った人がインスタンスを出ても、今している洗いが終わるまでは使い続けられる。終わった時に買った人が 1 人もいなければ、無料の道具・見た目に戻る。
- 無料の標準道具は維持する。

## 範囲

### 1. 商品定義と Storefront の ID

- ユーザーが Storefront で作り、[ce-listings.md §2 C](../design/ce-listings.md) に貼る (手順は同 §1)。全部入りの作り方は **案 1 で決定 (2026-10-14 ユーザー。Storefront で 1 Listing に 4 Product を追加できた)**: 全部入りは C-1〜C-4 の Product を含める Listing で、5 つ目の Product は作らない。所有判定はセットごとの Product だけを見る。`Row` は 1 行 1 `ceListingId` で、同じ `ceAsset` を別 Listing と共有する行を `Validate` が拒むので、全部入りの購入画面 (`Store.OpenListing`) を開く入口は **Listing だけの項目** (Product を持たない定義、または `KD_ShopItem` とは別の参照) として足す。設計は Codex 相談で確認する。
- 貼られたら `KD_ProductDefinitions.All` の末尾に CE 行を足す (分類は新規 `wash` か。既存 `Categories` を確認して決める)。`IsCE` 行は `price=0`・`ceCredits>0`、同じアセットを共有する行は Product / Listing / Credits が一致 (`Validate` の契約)。UdonProduct アセットは `Products/1 - Apply Metadata`、配線は `Menu/4 Setup Shop` → `4a Verify Shop` (ユーザー実行)。
- 手元メニューに売り場の行を置くか、洗い場の写真選択パネルから `Store.OpenListing` を開くかは [DES-UI-005](DES-UI-005-trainer-and-paid-items-purchase-flow.md) の導線設計と合わせる。

### 2. 所有判定 (既存 CE と違う部分)

既存の CE 商品 (飾り・ライブ・撫で FX) は購入者本人の `Store` 判定だけで解放する。洗い場は**インスタンス単位**なので設計が要る:

- 各参加者がローカルで自分の所有 (購入情報の読込完了後。読込中 / 未所持 / 取得失敗を分ける) を Owner へ申告し、Owner が「セットごとの所有者数」を Manual 同期で持つ (案)。途中参加は OnDeserialization で受ける。
- 所有者の退室で数を減らすが、**洗い中は無料へ戻さない**。「洗いの終わり」は DES-DOG-008 の「決めること」のまま未決 (犬ごとの洗い終わり / 洗い場のリセット / すすぎ後のタオル)。IMP-DOG-043 の選択 (`KH_WashSelection`、Manual、洗い場ごと) と IMP-DOG-046 の種類別の枠に、所有ゲートをどこで掛けるか (要求時に Owner が判定 / 表示時に各自が判定) を決める。
- 所有者が 0 になった時の「無料へ戻す」は、保持中のモデル切替を保留する既存契約 (IMP-DOG-043 配線・操作契約) と整合させる。
- 購入・期限切れ通知で再判定する。購入画面の開閉を購入成功としない。

### 3. 表示

- 写真選択パネル (`KH_WashSelectionBoard`) に未所持の表示 (価格、[購入画面をひらく]) を足す。13 言語の文言は後で確定 (AI 作成・校正なし、の前例に従う)。
- 看板 A 4 枚目・Listing 文面への反映は DES-UI-005。

## 範囲外

泡の色・形・パーティクル、虹しぶき、毛のボリューム、散歩中の持続エフェクト (DES-DOG-008 の企画の残件)。古民家側での販売。価格・構成の変更。

## 完了条件

- [ ] ce-listings.md §2 C の 10 セル (Product 4 または 5、Listing 5) が埋まり、`KD_ProductDefinitions` の定数と一致。`4a Verify Shop` PASS
- [ ] 所有判定の設計 (§2) を Codex 相談で確認し、同期変数・Owner・退室・途中参加・洗い終わりの契約を本票に記録
- [ ] ClientSim: 未所持で選択不可 → 所有者 1 人で全員が選べる → 所有者退室 (洗い中は継続、終わりで無料へ戻る) の模擬
- [ ] 実機 2 人 (ユーザー): 実課金 1 件で購入 → 反対側の端末が選べる → 購入者退室 → 洗い終わりで戻る。読込遅延・取得失敗の表示
- [ ] Listing の公開スイッチは上の検証が終わってから (ce-listings §1-6)

## 記録

2026-10-14: 起票。IMP-DOG-043 の「次工程」を引き継ぐ。ce-listings.md を同日に作成 (ID は空欄)。コード・シーン・Storefront は未変更。

決定: 2026-10-14 (ユーザー) 全部入りは案 1 (4 Product を含める Listing)。Storefront で 1 Listing に 4 Product を追加できたと報告。ID は未貼付。
