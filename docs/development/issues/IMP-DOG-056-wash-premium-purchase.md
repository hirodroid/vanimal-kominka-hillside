---
id: IMP-DOG-056
type: IMP
system: DOG
status: review
priority: Medium
summary: 洗い場の追加道具 5 セット (泡 / 水 / 仕上げ / 洗い場 各 500C、全部入り 1500C) を Credits 商品として接続する。Storefront の ID は ce-listings.md、所有判定は「インスタンスに 1 人でも所有者がいれば全員が選べる、洗い終わりまで継続」
next: "実装・静的検証 (4a / 44 PASS 1099 / 51)・Codex 独立レビュー反映・ユーザー決定 (継続中も購入ボタン、セット所有で全部入り非活性、ギフト入口)・Course/100p5 3 ケース PASS (run 20261013-235955) まで完了 (2026-10-14)。残り: ユーザーの実機 2 人 (§2.4: 他人の所有で選べる・途中参加・Owner 兼所有者の退室と 60 s 後の戻り・実課金 1 件・購入 0 件の人にも購入読込が届くか)、「洗いの終わり」の推奨 (§2.3) の確認、Listing 文面の起草、Listing の有効化 (実課金の直前)、ボードの表示文言の確認 (DES-UI-005)"
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

### 2. 所有判定 (既存 CE と違う部分) — 契約 2026-10-14 (統括 Fable。Codex 相談 (gpt-6-astra、読み取りのみ) の 7 点を反映)

既存の CE 商品 (飾り・ライブ・撫で FX) は購入者本人の `Store` 判定だけで解放する。洗い場は**インスタンス単位**。

**根拠 (公式 CE Udon Documentation、2026-10-14 確認)**: 購入情報はインスタンスの他の人の分も各クライアントへ届く (`OnPurchasesLoaded(products, player)` は自分の入室時と他の人が後から入った時、`OnPurchaseConfirmed` はインスタンスの誰かが買った時)。`Store.DoesPlayerOwnProduct` は他人にも使える。移植した `KD_ShopItem` は既にこの前提で全員分の所有者 id を観測し退室で外す (`_ceOwnerIds`)。**ただし「読込完了」を知っているのは自分の分だけ** (`_cePurchasesLoaded`) で、他人の分が未着の間の「所有者 0」は「居ない」ではなく「まだ分からない」(Codex 1)。

#### 2.1 所有の 3 状態 (KD_ShopItem、裏山改変)

- `KD_ShopItem` に読み取り専用 `CeInstanceOwnership()` を足す (`// net: callable-ok`)。戻り値 `InstanceUnknown = 0 / InstanceOwned = 1 / InstanceNone = 2`。
  - 在室者ごとに「購入情報が届いたか」を記録する (`OnPurchasesLoaded` の player を集合に入れ、`OnPlayerLeft` で外す)。
  - **Owned**: 在室者のうち観測済みの所有者が 1 人以上 (退室者は除く。`Store` の肯定は在室者とだけ照合)。観測が無い在室者には `Store.DoesPlayerOwnProduct` を聞き、肯定なら覚えて Owned。
  - **None**: 在室者全員の購入情報が届いていて所有者 0。
  - **Unknown**: それ以外 (誰かの分が未着)。
  - 結果はキャッシュし、`OnPurchasesLoaded` / `OnPurchaseConfirmed(Multiple)` / `OnPlayerJoined` / `OnPlayerLeft` で無効化する (ボードが毎 tick 聞くため)。
  - 既存の `Owned()` / `OwnedBy()` (本人向け) と期限切れの扱い (入室時判定、滞在中は剥がさない) は変えない。`_ceOwnerCount > 0 || DoesAnyPlayerOwnProduct` の単純 OR は採らない (Codex 4)。

#### 2.2 門 (KH_WashSelection、Owner 側 1 か所)

- `public KominkaDogRun.KD_ShopItem[] setItems` (添字 = 分類 0 泡 / 1 水 / 2 仕上げ / 3 洗い場 → `KD_ShopItem_Wash_Foam / Water / Finish / Station`)。WashPremium の新メニューが配線、44 が検査。
- `_SetState(category)`: `setItems` が無い・該当が null → **None 扱いで拒否** (無料開放にしない。44 が配線を検査するので、ここは診断ログ 1 回)。
- `_Decide(item, expectedRevision, setState)`: Store もネットも呼ばない裁定本体 (Edit モードの 44 が回す)。**順序は Held → Stale → 基本に戻す (item -1、所有に関係なく通す) → Unknown は `ResultPending = 5` → None は `ResultLocked = 4` → 反映** (Codex 3)。拒否のとき選択値と `revision` は変えない。
- `ReqSelect` は `_SetState(category)` を求めてから `_Decide` に渡す。要求者本人の所有は見ない (1 人買えば全員)。

#### 2.3 無料へ戻す (Owner だけ。再評価経路を 1 本に)

- `_Reevaluate()` (Owner のみ): 選択中の各分類について、`_SetState(c) == None` **かつ** `_StationIdle()` なら `SetSelected(c, -1)`。1 つでも変えたら `revision++` → `_Apply(true)` → `RequestSerialization` (要求の反映と同じ経路。途中参加は同期値で復元)。None でない・idle でないなら `_revertPending = true` にして 2 s の遅いポーリングで再評価を続ける (保留中だけ動く。解消したら止める)。**SetSelected の直前にもう一度 `_SetState` を見る** (戻す直前の購入通知を拾う。Codex 2)。
- きっかけ (全部同じ `_Reevaluate`): `OnPlayerLeft` (観測結果にかかわらず予約。`KD_ShopItem` 側の削除より先に来ても次の poll で追いつく)、`OnOwnershipTransferred` (新 Owner が保留を作り直す)、`OnPurchasesLoaded` / `OnPurchaseConfirmed(Multiple)` (KH_WashSelection 自身でも受けて再評価。Owned に転じれば保留を解く)。
- **保証の時点を明記**: 「所有者が居る間は戻さない」= **Owner が在室の所有者を確認できている間は戻さない**。Owner にまだ届いていない直近の購入との完全な順序付けは API の契約上できない (Codex 2)。
- **`_StationIdle()` = 「洗いの終わり」の判定 (DES-DOG-008 の未決。推奨案、ユーザー確認待ち)**: **洗い場単位**で、(a) この洗い場の道具 (追加道具 4 分類 + 無料道具。`KH_WashStationReset.tools` と同じ一覧) を誰も持っていない、(b) 台に犬が呼ばれていない (`call._HeldDog() < 0`)、(c) (a)(b) が **60 秒**続いた (道具を置き替える間・タオルへ移る間に消えない)。1 つの関数に閉じ、後から差し替える。分類ごとに戻さない (泡を置いて水を使っている途中で泡だけ戻らない。Codex 2)。リードで連れてきた犬を呼ばずに洗う場合も (a) で拾う (道具を使うから)。
- 期限切れ (`OnPurchaseExpired`) は既存どおり入室時判定。買い切りなので実質起きない。同期する所有状態は持たない (Store が正本。台帳にも書かない)。

#### 2.4 検証の分担 (Codex 6)

- **Edit (WashPremium/44)**: `_Decide` を Owned / None / Unknown × (Held / Stale / 基本に戻す / 反映) で回し、拒否時に選択値と revision が不変。`_StationIdle` を手持ち・犬・経過時間の差し替えで両枝。配線 (4 洗い場 × setItems 4 と `ceListingId` / Product ID が台帳と一致、全部入り Listing ID と 1500 がボードに入っている)。
- **ClientSim (Course の群 `premium-store`、回すかは相談)**: UdonProduct アセットの `Purchased` で自分の所有だけ模擬できる (`ClientSimStoreManager`、イベントは LocalPlayer)。読込後の未所有 → Locked、所有 → 反映、Play 中の購入通知 → 選べるようになる。他人の所有・退室後の戻しは模擬できない。
- **実機 2 人 (ユーザー)**: A の所有で未購入の B が選べる / B の途中参加で復元 / A (Owner 兼最後の所有者) が退室 → B の洗いは続く → 終了 60 s 後に無料へ / 実課金 1 件で購入通知と反対側の解放。全部入りを試す時は 1 件で 4 Product すべてを確認。**実課金には Listing の有効化が要る**ので、「実課金前の検査」と「実課金確認後の一般向け導線の公開」を分ける。

### 3. 表示 (ボード。最小で安全な既定、Codex 7。確定は DES-UI-005)

主操作 (取り出す / この見た目にする) と購入 (別ボタン [購入画面をひらく]) を分ける。所有の表示は選択の `revision` では更新されないので、ボードの tick で `_SetState` を見る。

| 状態 (選んだ品の分類のセット) | 表示・動作 |
|---|---|
| 他人の所有で使える (自分は未所有) | 「このインスタンスで利用できます」。取り出せる。自分を購入済みと表示しない。購入ボタンも出す (2026-10-14 ユーザー決定: 他の人も買うかもしれない) |
| 自分が所有するセット | ボタンのラベルは「ギフトする」(灰)。同じ Listing を開く (ギフトの操作は VRChat の画面)。2026-10-14 決定 |
| 全部入りタブで自分がセットを 1 つでも所有 | 購入ボタンは非活性、「セットを 1 つでも持っていると、全部入りは買えません」。2026-10-14 決定 |
| 所有者 0 を確認済み (None) | 価格 (500 C) と [購入画面をひらく] (`Store.OpenListing` でそのセットの Listing)。取り出しは Locked の語 |
| 確認中 (Unknown) | 「購入情報を確認しています…」。既存の道具は維持。未所有と断定せず、購入へ自動誘導しない |
| 所有者が出た後の継続中 (`_revertPending`) | 「今の洗いが終わるまで利用できます」 |

全部入りタブの購入ボタンは**全部入りの Listing** を開き、取り出しの可否は選んだ品の分類だけで判断する。自分が 4 セットすべてを持っていても「全部入り購入済み」とは表示しない。一部購入済みの人の差額・重複購入は約束しない。新しい語 (5〜6) は 13 言語に追加 (AI 作成・校正なし、の前例)、43 で字形を焼き直し、`m_SourceFontFile` が外れていることを確かめる。看板 A 4 枚目・Listing 文面への反映は DES-UI-005。

## 範囲外

泡の色・形・パーティクル、虹しぶき、毛のボリューム、散歩中の持続エフェクト (DES-DOG-008 の企画の残件)。古民家側での販売。価格・構成の変更。

## 完了条件

- [x] ce-listings.md §2 C の 9 ID (Product 4、Listing 5) が `KD_ProductDefinitions` の定数と一致し、`Menu/4a Verify Shop` PASS (wash は初期品 0 の分類として検査) (2026-10-14)
- [x] 所有判定の設計 (§2) を Codex 相談で確認し、契約を本票に記録 (2026-10-14)。「洗いの終わり」の推奨 (§2.3) はユーザー確認待ち
- [x] Edit (44): _Decide の 3 状態 × 4 経路、_StationIdle の両枝、配線と ID (PASS 1095)。ClientSim (premium-store): 未所有 → Locked、Purchased → 反映、Play 中の購入通知 (Course/100p5 3 / 3 PASS、run 20261013-233842) (2026-10-14)
- [ ] 実機 2 人 (ユーザー): §2.4 の 5 項目 (他人の所有で選べる・途中参加・Owner 兼所有者の退室と 60 s 後の戻り・実課金 1 件・全部入りなら 4 Product)。読込中の表示
- [ ] Listing の有効化は実課金の検査の直前に (ユーザー)。一般向けの導線 (看板・文面) の公開は実課金の確認後 (ce-listings §1-6)

## 記録

2026-10-14: 起票。IMP-DOG-043 の「次工程」を引き継ぐ。ce-listings.md を同日に作成 (ID は空欄)。コード・シーン・Storefront は未変更。

決定: 2026-10-14 (ユーザー) 全部入りは案 1 (4 Product を含める Listing)。Storefront で 1 Listing に 4 Product を追加できたと報告。同日 ID 9 件を台帳へ貼付。

2026-10-14: §2 の契約案を Codex 相談 (gpt-6-astra、読み取りのみ) に出し、7 点の指摘を反映 (所有を Unknown / Owned / None の 3 状態に、裁定順 Held → Stale → 戻す → Unknown → Locked → 反映、戻しの再評価経路を 1 本に、洗いの終わりは洗い場単位 + 60 s、Menu/4a の初期品要件 (wash は 0)、Pouch には自動で現れない、ID は 9 件)。採らなかった案: 「明示的な終了の記録」(新しい操作が要る。道具・犬・経過時間の観測で代える。ユーザー確認待ち)。

## 実装 (2026-10-14。統括 Fable、実装 Opus 2 名 (A: 商品定義・KD_ShopItem・4a、B: 選択・ボード・Editor 54/44・ハーネス)、Codex 独立レビュー)

- **商品定義** (`KD_ProductDefinitions.cs`): 分類 `wash` (Defaults は空 = 初期品なし)、CE 4 行 `wash.foam_set / water_set / finish_set / station_set` (500C、`KD_ShopItem_Wash_*`、`KD_Product_Wash*`)、定数 `WashAllListing` / `WashAllCredits = 1500` / `WashSetKeys` / `WashSetSceneNames`。Validate に全部入り Listing の形式・重複なし、wash が 4 行の検査。4a の初期品検査に wash (0 件)、文言キー表に 4 行 (フォールバック形。手元メニューには出さない)。
- **所有 3 状態** (`KD_ShopItem.CeInstanceOwnership()`、裏山改変): 在室者ごとの購入読込の記録 `_ceLoadedIds`、退室中の守り `_ceLeftId`、Owned / None はイベントで無効化されるまで・Unknown は 1 s キャッシュ (Codex 低 1 件の反映)、プレイヤー配列は再利用。
- **門と戻し** (`KH_WashSelection`): `setItems[4]` / `reset`、`_SetState`、`_Decide(item, rev, setState)` (Held → Stale → 戻す → Pending 5 → Locked 4 → 反映)、`_Reevaluate` (Owner。退室 / Owner 交代 / 購入通知で 2 s 以内の poll に予約、直前に再確認)、`_StationIdle` = 道具を誰も持たない + 台に犬なし + 最終保持 (`KH_WashTool._LastHeldAt()`、Codex 中 1 件の反映: poll の隙間で持って置いた道具も数える) と犬の最終時刻から 60 s。
- **ボード** (`KH_WashSelectionBoard`): 状態行 (Owned 47 / None 価格 + 48 / Unknown 49 / 継続 50)、[購入画面をひらく] (`_Buy` → `Store.OpenListing`、全部入りタブは全部入り Listing)、語 46〜51 を 13 言語 (es / fr / pt-br / it のボタン語は Editor のはみ出しで短縮)。
- **Editor**: `WashPremium/54 - Wire Store` (setItems / reset / allListingId / allCredits / Buy ボタン。dirty 判定・Undo・変更なしは書かない)、44 に配線・ID・`reset.tools` の完全照合 (Codex 中)・ボタン listener の状態と String モード (Codex 中)・裁定 3 状態 × 経路・`_StationIdle` の両枝 (実時計の 3 ケース含む)・13 言語 × 5 タブ × 4 状態の表示。
- **ハーネス**: 群 `premium-store` (Course/100p5、3 ケース)。既存 premium-* は `PremiumOwnAll` (ClientSim の購入経路で 4 セット所有) で救済、期待は不変。

**検証 (統括)**: C# 0 / U# 0 → Products/1 (UdonProduct 4 件、ID 一致) → Menu/4 PASS (60 件、WARN 4 = 文言キー未登録は想定内) → 4a PASS → 54 WIRE_STORE_OK (4 洗い場) → 44 PASS 1095 / FAIL 0 → 51 PASS (共有フォント Static、新しい字は端末の代替) → 19b (NetworkIDs 383 → 387) → 保存 → Course/100p5 WASH_PASS 3 / 3 (run 20261013-233842、sceneUnchanged: 未所有 → Locked、Play 前 Purchased で泡 Applied・水 Locked、Play 中の購入通知で解放)。

**Codex 独立レビュー (gpt-6-astra)**: 中 3 (60 s が poll の隙間で保証されない / 44 が reset.tools の欠けを見逃す / 購入ボタンの listener 状態を見ない)・低 1 (Unknown の走査負荷) → 全部反映。同期・権限・回帰・U# 制約・ハーネス・語・文書は問題なし。ハーネスは「修正後に 100p5 を単独で 3 ケース」の結論で回した。修正後の再確認は Codex ではなく 44 (指摘のケースそのものを実時計の検査に入れた) と 100p5 で行った。

**未確認 (実機 2 人でしか決まらない)**: 購入 0 件の在室者にも `OnPurchasesLoaded` が届くか (届かないと None に到達せず「確認しています…」が続く。契約の前提)、他人の所有で選べる、途中参加、Owner 兼最後の所有者の退室 → 60 s 後の戻り、実課金 1 件、同じ Product を 2 Listing に入れた状態の成立 (ce-listings §4-2)。

決定: 2026-10-14 (ユーザー) **継続中 (所有者が出た後) の購入ボタンは出す** (「他の人も買ってくれるかもしれないから」)。誰かが所有している状態でも自分が未所有なら購入ボタンを出す (同じ理由)。

決定: 2026-10-14 (ユーザー) **個別のセットを 1 つでも自分が持っていたら、全部入りは買えない (購入ボタンを非活性)。** 理由: 個別を 2 つ以上持って全部入りを買うと金額が高くなるだけで中身は同じ。4 つ全部持っていても同じ。

決定: 2026-10-14 (ユーザー) **ギフトは古民家ドッグランと同じ** ([IMP-PRG-012](../../../../KominkaDogRun/docs/development/issues/review/IMP-PRG-012-ce-gift-entry.md) の作り): 買う前も買った後も Listing の画面を開ける。ワールドは `Store.OpenListing` で同じ画面を開くだけで、贈る相手の選択は VRChat の画面が持つ。自分が所有するセットのタブはボタンのラベルを「ギフトする」にする。**副作用 (統括の読み、ユーザー未確認)**: 全部入りはセットを 1 つでも持つと非活性なので、全部入りをギフトできるのはセットを持たない人だけ。

決定: 2026-10-14 (ユーザー) ce-listings §4-1 (移植した 6 商品の定数が古民家の ID のまま) は**別の周回で対応**する。本票の範囲外のまま。

2026-10-14 (決定の反映、Opus B): ボードを変更。Owned でも自分が未所有なら購入ボタン、自分が所有するセットは「ギフトする」(灰 `giftColor`、同じ Listing を `Store.OpenListing`)、全部入りタブは自分がセットを 1 つでも `Owned()` なら非活性 + 語 53 (`_Buy` も空の Listing で何もしない)。語 52・53 を 13 言語に追加 (WordCount 54)。44 に 13 言語 × 5 タブ × 自分の所有 (なし / そのセット / 全部) × 4 状態の表示・活性・ラベル・色・Listing の照合 (Edit モードではボードの `_ownMask` を reflection で作る。`Owned()` は LocalPlayer が無い Edit モードでは立てられない)。premium-store 3 ケースの期待を決定に合わせて変更 (ケース 2 に全部入りの非活性を 2 行)。検証: C# 0 / U# 0 → 54 (boards 4) → 44 PASS 1099 / FAIL 0 → 保存 → Course/100p5 WASH_PASS 3 / 3 (run 20261013-235955、sceneUnchanged。期待のコード自体を確かめるため、前回の Codex の結論と同じ範囲で再実行)。Codex の再レビューは掛けていない (UI の表示だけで、同期・権限は不変)。**検査の分担の注記**: 44 の Edit モード検査は `_ownMask` を reflection で与えて描画 (ボタン・活性・ラベル・色・Listing) を見るだけで、実行時に `setItems[c].Owned()` から `_ownMask` が正しく作られることは見ていない。その経路は 100p5 のケース 2 (Play 前に泡を Purchased → 泡タブが「ギフトする」、全部入りタブが非活性 + 語 53) が通した。
