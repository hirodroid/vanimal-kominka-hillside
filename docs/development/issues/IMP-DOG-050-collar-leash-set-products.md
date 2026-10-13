---
id: IMP-DOG-050
type: IMP
system: DOG
status: review
priority: Medium
summary: 首輪とリードのおそろいセット 4 種 (しま / チェック / もみじ 各 30pt、革と真鍮 50pt) を Point 商品として実装する。商品定義、首輪の同期 (飾りと同じ)、リードの同期 (本人の選択 + PlayerData 保存)、干渉表、古民家の正本への反映
next: "実装・静的検証・Codex レビュー反映・Course/129 (collar-pattern 2 ケース PASS、run 20261013-052444) まで完了 (2026-10-13)。残り: ユーザーの Play (購入 → 首輪とリードの柄、2 人で同期・60 秒・途中参加、近 → 遠 → 近の往復)、テクスチャ本制作 (BUG-DOG-011 の VR 確認後)、仮決定 collar.basic の確認、古民家側文書への反映 §6、ビルドサイズ (暫定テクスチャ 4 × 1024² は未計測。別セッションの未コミット作業があり Build Size/7 Apply が他の .meta を書き換えるため見送り)"
updated: 2026-10-13
---

# IMP-DOG-050 首輪とリードのおそろいセットを Point 商品として実装する

2026-10-11 起票。企画は [IMP-DOG-048](IMP-DOG-048-collar-and-leash-patterns.md) で 2026-10-10 に確定した (ユーザー回答 6 点)。この Issue はその実装とテクスチャ制作を持つ。決まっていることは 048 の決定だけを写した。ここで新しく決めたことは無い。

## 決まっていること (IMP-DOG-048、2026-10-10 ユーザー決定)

- 商品は首輪 + リードのおそろいセットだけ。単品 (首輪だけ・リードだけ) は売らない (回答 3)。
- 柄は 4 種。しま / チェック / もみじ は各 30pt、革と真鍮は 50pt (回答 4)。全部そろえると 140pt。Point 対象の合計は 650 → 790pt。
- 無料は今の犬別の色の首輪・リード。「首輪なし」は作らない。CE にしない (回答 6)。
- 首輪は飾りと同じ扱い。散歩中 (リード接続中) の犬でも、つないでいる人以外が変えられる。追加の制限は付けない (回答 1)。
- リードの柄は 1 人 1 つ (左右の手で別にしない)。選択を PlayerData に保存して、次回も同じ柄にする (回答 2)。
- 裏山の首輪は飾りの 5 部位とは別の「首輪」の行 (回答 5)。蝶ネクタイ・バンダナ・首飾りと同時に付けられる。
- 形は変えず、テクスチャ (と金具の色) だけを替える。柄は 6 犬種共通で、地の色に犬の識別色を残す。

## 範囲

### 1. 商品定義 (セット 4 行)

- `KD_ProductDefinitions.All` (`Imported/KominkaDogRun/Editor/KD_ProductDefinitions.cs`) の末尾に 4 行を足す。裏山専用品を末尾に足す前例は `live.hillside_dusk` (同ファイル 101 行)。Point のセットの前例は `accessory.floral_set` (60pt、同 87 行)。
- 所持は台帳の `own.{productKey}`。セットだけを売るので、所持キーはセット 1 つにつき 1 つ。首輪とリードはどちらも同じキーで解放する。
- 決めること: 分類 (`Categories` の `accessory` に入れるか、新しい分類を足すか)、productKey の名前、`KD_ShopItem` のアセット名。
- 価格表示と購入は手元メニューの共通確認カード ([DES-UI-005](DES-UI-005-trainer-and-paid-items-purchase-flow.md))。売り場の行をどのタブに置くかも決める。

### 2. 首輪の同期 (飾りと同じ)

- 所持の判定は飾りと同じ台帳。
- 装着状態は飾りと同じく犬に付く共有の見た目。60 秒ルールを当て、途中参加の人へは飾りと同じ Manual 同期の配列で配る。着せた人が退室しても残し、新しいインスタンスは既定 (犬別の色) から始める。
- 決めること: `KD_AccessoryBox` は `SlotCount = 5` 固定 (`KD_AccessoryBox.cs:65`)、装着は `_slotProduct` (同 100 行)。首輪を 6 つ目の枠にするか、準じる別の配列にするか。どちらにしても 5 部位の排他や干渉の対策 (案 C) に混ざらないようにする。

### 3. リードの同期 (本人の選択 + PlayerData 保存)

- 撫でFX と同じ方式。本人の選択だけを `KD_PlayerFxTag` (`Scripts/Progress/KD_PlayerFxTag.cs`) 型の PlayerObject で配る。60 秒ルールは当てない。
- 選択を PlayerData に保存し、次回の入室で戻す。所持していない柄が保存値に残っていたら既定へ戻す (推定。復元の順序は台帳の復元を待つ必要がある)。
- PlayerObject で配るので、他の人のリードもその人の選んだ柄で見える (048 の方式)。

### 4. 首元の飾りとの干渉表

- [IMP-DOG-015](IMP-DOG-015-accessories-and-petfx-port.md) の 42 組 (首元 7 品 × 6 犬種、案 C) と除外 3 件をそのまま使う。
- テクスチャだけを替えるので、新しい組は増えない見込み (推定)。見直すのは除外 3 件と、金具 (形・位置) に触れる組だけ。
- 帯は 1 枚の共有テクスチャ (1024²) の中で犬ごとに 128px の横帯を使っている (`scripts/art/leash-assets.cjs`、[リードの制作計画](../../plans/2026-09-20-leash-asset-production.md))。柄ごとに BaseColor を足す。

### 5. ポメ・サモエド (BUG-DOG-011 案 1)

- [BUG-DOG-011](BUG-DOG-011-collar-fit.md) の案 1 (2026-10-10 ユーザー決定) で、ポメ・サモエドは帯を毛の中に埋めて見せず、金具とリードで首輪を見せる。
- この 2 犬種で見える金具は帯メッシュの D リングではなく、`ClipAnchor` 配下の手続き生成の `AttachmentRing` (R12)。革と真鍮の金具の色は、この輪の材質にも当てる必要がある。
- しま / チェック / もみじ は帯のテクスチャだけの柄なので、この 2 犬種ではリードだけで見えることになる (推定。毛越しに帯が見えないため)。
- BUG-DOG-011 は wip (サモエドの金具の持ち上げ `RingLift` を調整中)。金具の位置と材質は 011 の結果に合わせる。

### 6. 古民家の正本への反映 (挙げるだけ。ここでは編集しない)

古民家 (`KominkaDogRun`) の文書は読んでいない。節番号は IMP-DOG-048 の記載による (未確認)。

- `docs/business-strategy.md` §5.2: 価格表におそろいセット 4 行 (30 / 30 / 30 / 50pt) を足す。Point 対象の合計を 650 → 790pt にする (裏山専用 Live 30pt を含めた実額は 680 → 820。文書の 650 は未更新と 048 に記載)。
- `docs/business-strategy.md` §14.1.1: 部位表で首輪は「胸・首元」の候補。裏山では 5 部位とは別の「首輪」の行にしたことを注記する。
- `docs/development/design/accessories.md` §1: 首輪は Point の飾り、CE にしない、セットだけを売る (既存規則「セットの中身は単品で買えない」(§5・§6) と一致) ことを書く。

## まだ決めていないこと

- ミニチュアは首輪込みで焼いてある。柄を反映するか、基本色のままにするか。
- 光る首輪 (夜に Emission で光る) は採否未定 (2026-10-10 ユーザーのアイデア)。この Issue の 4 種には入れない。
- 売り場の行の位置 (どのタブか) と、試着プレビューで柄を見せるか。

## やらないこと

単品販売、5 種目以上の柄、形の違う首輪・リード (鈴付き・布テープ・竹の持ち手)、CE 化、毛色との連動 ([DES-DOG-009](DES-DOG-009-instance-owner-fur-color.md))。

## 推奨 (2026-10-13、統括 Fable + Opus の静的確認)

HEAD `3b40285` のソースを読んだだけの推奨。Unity・Play・ハーネスは使っていない。

決定: 2026-10-13 (ユーザー決定「２．別ですね。推奨でお願いします」) Q1 = 別の挙動 (60 秒の待ちは飾りと独立)、Q2 = 新しい分類 `collar`、小さな 2 点 = 「首輪とリード」の小見出し・ミニチュアは基本色のまま。推奨の無かった「既定を空 + Verify の除外」か「無料の `collar.basic`」かは未決定のまま。

### Q1. 首輪の装着状態: 推奨 = 別の挙動 (新しい KH_ スクリプト、Manual、犬 6 匹の int 配列)

- 理由 1: `KD_AccessoryBox` は「枠の数は仕様なので 5 のまま持つ」と書いている (`KD_AccessoryBox.cs:62-65`)。見た目は兄弟ノードの `SetActive` だけで作り、材質に触らない作り (同 12-13・27-28、`Apply` 407-435)。テクスチャを替える首輪の柄はこの形に乗らない。
- 理由 2: 6 つ目の枠にすると守りが要る所が多い。(a) `Apply` は `nodes.Length < productIds.Length * dogCount` のとき全部を描かずに戻る (同 412) ので、首輪の品目にも空の器が要る。(b) 通常の Setup ごとに呼ばれる `BindCandyNodes` が品目を 18 + キャンディ 6 = 24 件に切り詰める (`KD_AccessorySetsEditor.cs:54-57`)。(c) 裏山の Setup は品目ごとに部位の錨へ器を作る (`KD_AccessorySetupEditor.Hillside.cs:513-530` 付近。部位は `Slot { Head, Face, Chest, Body, Tail }`、`KD_AccessorySetupEditor.cs:49`)。(d) 検査が 30 = 6 匹 × 5 枠を直書きしている (`KD_AccessorySetChecksEditor.cs:149, 174`)。(e) キャンディの演出中は普通の飾りを全部消す (`KD_AccessoryBox.cs:426`) ので、首輪を例外にする必要がある。
- 理由 3: 見た目の側は `KH_FurColor` がほぼそのまま前例になる (犬 6 匹の `[UdonSynced] int[]`、Manual、0 = 犬ごとの既定、`MaterialPropertyBlock` で `_MainTex` だけ差し替え、材質の複製なし。`KH_FurColor.cs:18-23, 48, 156-159`)。
- 差し込み口: 帯・縄・スナップ・持ち手はすべて共有材質 `KH_Leash_WovenMetal` (BaseColor 1024² が 1 枚) を使う (`KH_LeashProductionEditor.cs:35, 76-80, 260, 275-278`)。テクスチャは下の 768px が犬ごとに 128px の帯 6 本、その上が金具・持ち手・縄 (`scripts/art/leash-assets.cjs:14-15, 30`)。柄ごとに同じ並びの BaseColor を 1 枚作り、犬の `Band` の Renderer に PropertyBlock で当てれば、識別色の帯と金具の色を 1 回で替えられる見込み (推定。Blender 版の帯メッシュの UV は未確認)。埋もれ 2 犬種の `AttachmentRing` は別材質 `KH_ClipNeutral` (BUG-DOG-011) なので、革と真鍮では別に当てる。リードの側 (`KH_LeashCollar.ropeMesh / snapVisual / handleVisual`) は、歩かせている人の選択で同じ差し替えをする。
- 費用: 要求 → Owner の検証 → 応答・時間切れ・所持の再試行・退室時の解放 (`KD_AccessoryBox.cs:192-325, 372-404`) と 60 秒の計算 (同 455-479) を写す。新しい同期物なので Menu/19b Assign Network IDs が要る (前例 `KH_ToyTopRowEditor.Patterns.cs:27`)。手元メニューも、首輪の行は飾りの台帳でなくこの挙動を読む分岐が要る (`KD_TreatPouch.Acc.cs:155-218, 428-490` に当たる処理)。
- 要ユーザー判断: 60 秒の待ちを飾りと共有するか。飾りの待ちは犬ごとに 1 つ (`_changerId` / `_changedMs`、同 102-107)。6 つ目の枠なら共有になる (首輪を替えると、他人はその犬の飾りも 60 秒替えられない)。別の挙動なら独立になる。推奨は独立 (どちらでも「最後に替えた人以外は 60 秒待つ」は守られる)。
- 代案 A: 6 つ目の枠。上の (a)〜(e) を全部守り、`Unset` を「既定の色」と読み、`_Clear(dog, 5)` で既定へ戻す。要求・60 秒・途中参加は追加のコードなしで使える。代案 B: `KD_AccessoryBox` に犬 6 匹の配列をもう 1 本足す。同期物は増えず要求経路と 60 秒を共有できるが、`Apply` の分岐と、古民家から移植した KD_ ファイルの改変が増える。
- 触るファイル (推奨): 新規の挙動 (例 `Assets/KominkaHillside/Scripts/KH_CollarPattern.cs`) とその Setup / Verify、`KD_TreatPouch.Acc.cs` (首輪の行)、`KH_LeashCollar.cs` か `KH_LeashManager.cs` (リードの差し替え)、`scripts/art/leash-assets.cjs` (柄の BaseColor)。`KD_AccessoryBox.cs` は触らない。

### Q2. 商品の分類: 推奨 = 新しい分類 (例 `collar`)

- 理由 1 (決め手): カタログは分類ごとに 1 つの選択を PlayerData (`Sel.{分類}`) に保存する。復元は台帳の読み込みを待ち、持っていない保存値は既定へ戻し、購入するとそのまま選ぶ (`KD_ShopCatalog.cs:43, 54-75, 82-99, 125-126`)。§3 のリードの要件 (1 人 1 つ・PlayerData・未所持は既定へ・台帳の復元を待つ) を追加の保存処理なしで満たせる。撫でFX も同じ形 (`KD_PetFxBox.cs:103-112`、分類 `petfx` を見る消費者)。`accessory` に入れると `Sel.accessory` を飾りと共有し、既定がバンダナになる (`KD_ProductDefinitions.cs:29`)。
- 理由 2: 飾りの台帳と一覧は分類で拾わず、明示の表で配線している (`KD_AccessorySetsEditor.cs:27-31`、`KD_AccessorySetsEditor.Hillside.cs:79-94`)。どちらの分類でも、首輪のセットが体の部位の飾りとして勝手に出ることはない。
- 新しい分類で要る手当: `Categories` と `Defaults` は添字で対応する (`KD_ProductDefinitions.cs:28-29`)。既定を "" にするなら、裏山の Verify の「初期品がちょうど 1 つ」(`KD_ShopSetupEditor.Hillside.cs:416-421`) の除外 (live / experience / action) に足す。代わりに無料の行 (例 `collar.basic` 0pt、`petfx.heart` と同じ形) を既定に置けば検査はそのままで、手元メニューで「基本に戻す」を選ぶ口にもなる (カタログに選択を消す口は無い。`KD_ShopCatalog.cs:82-91`)。無料の行を足すかは要ユーザー判断 (売る商品は 4 種のまま、台帳の行が 1 つ増える)。
- 代案: `accessory` に入れる (分類は変えない。前例 `accessory.floral_set`)。この場合リードの選択は、別の PlayerData キーと復元の順序を自前で持つ。
- 触るファイル (推奨): `KD_ProductDefinitions.cs` (分類・既定・セット 4 行、無料の行を足すなら 1 行)、`KD_ShopSetupEditor.Hillside.cs` (`HillsideNameKeys` と初期品の検査)、文言 (UITranslations.json)、リードの消費者 (新規、`KD_PetFxBox` の形) とカタログの `consumers` の配線。`KD_ProductsEditor.HasMenuEntry` (50-57) は未知の分類で false を返すが、使うのは Imported-Unused の検査だけ (裏山の Verify は SKIP、`KD_ShopSetupEditor.Hillside.cs:449`)。

### 小さな未決定 2 点

- 売り場の位置: かざりタブ 1 ページ目の見えている行は 14、行のイベントは `_Acc0`〜`_Acc15` の 16 本 (`KD_TreatPouch.Acc.cs:40-55`、`KD_AccessorySetsEditor.cs:19`)。4 セットを同じ一覧に足すと 18 行になり入らない。推奨は撫でFX と同じく、かざりタブの中に「首輪とリード」の小見出しを別に作る (撫でFX の行の前例 `KD_TreatPouch.Acc.cs:399-403`)。
- ミニチュア: 焼くときに元の材質の `_MainTex` を写した材質を作る (`KD_DogPreviewEditor.cs:378-389`)。推奨は初回は基本色のまま (焼き直し不要)。試着で柄を見せるなら、焼いた首輪の Renderer に同じ PropertyBlock を当てる方法がある見込み (推定、未確認)。

## 完了条件

- [ ] 4 セットの商品行と所持キーを足し、購入で首輪とリードの両方が解放される。所持していない柄は選べない
- [ ] 首輪の柄が飾りと同じ同期で 2 人に同じに見え、途中参加の人にも見える。60 秒ルールが効く。リード接続中の犬でも、つないでいない人が変えられる
- [ ] リードの柄が本人の選択で他の人にも見え、再入室で戻る (PlayerData)
- [ ] 6 犬種 × 4 柄で、帯 (見える 4 犬種) と金具・リード (埋もれ 2 犬種) の見え方を確認した。首元の飾りとの干渉表を見直した
- [ ] 古民家の正本へ反映する箇所 (§6) をユーザーへ渡した
- [ ] 2 人・Quest / iOS の確認 (ユーザー)。Quest / iOS の合否は [VER-WLD-001](VER-WLD-001-quest-ios-devices.md)

## 関連

- 企画と決定: [IMP-DOG-048](IMP-DOG-048-collar-and-leash-patterns.md)
- 首輪の位置と埋もれ 2 犬種: [BUG-DOG-011](BUG-DOG-011-collar-fit.md)
- 飾りと干渉表: [IMP-DOG-015](IMP-DOG-015-accessories-and-petfx-port.md)
- 購入導線: [DES-UI-005](DES-UI-005-trainer-and-paid-items-purchase-flow.md)
- 識別色: [DES-DOG-002](DES-DOG-002-toy-behaviour.md)

## 実装 (2026-10-13、Opus サブエージェント。コードと仮テクスチャだけ。Unity・Play・ハーネスは使っていない)

**決定 (AI の仮決定、ユーザー確認待ち): 分類 collar の既定は無料の行 `collar.basic` (0pt、`petfx.heart` と同じ形)。** 裏山の Menu/4a「初期品がちょうど 1 つ」の検査はそのまま通り、手元メニューに「基本」の行ができて、犬の首輪と自分のリードを今の犬別の色へ戻す口になる。売る商品は 4 セットのまま (台帳の行が 1 つ増える)。代案 (既定を空 + Verify の除外) に替えるなら `KD_ProductDefinitions` の 1 行と `Defaults`、Collar/1・2 の表 [0] を直す。

### 作ったもの

- 商品: `KD_ProductDefinitions` に分類 `collar` (`Categories` の末尾、`Defaults` は `collar.basic`) と 5 行 `collar.basic` 0 / `collar.stripe` 30 / `collar.check` 30 / `collar.momiji` 30 / `collar.leather_brass` 50 (シーン名 `KD_ShopItem_Collar_Basic|Stripe|Check|Momiji|LeatherBrass`、作るのは Menu/4 Setup Shop)。文言キーは `HillsideNameKeys` に `collar.name.*`。所持キーは台帳の `own.{productKey}` 1 つで首輪とリードの両方。
- 文言: `UITranslations.json` の 13 言語に 9 キー (`collar.group` / `collar.name.basic|stripe|check|momiji|leatherBrass` / `collar.setBody` / `collar.myLeash` / `collar.onLeash`)。**AI の下書きで校正していない** (「鍮」など新しい字形は Collar/1 が共通フォントへ足す)。
- 首輪 (犬 6 匹、共有): 新規 `KH_CollarPattern` (Manual、`KH_Dogs/KH_CollarPattern`)。同期は `int[6] _pattern` (0 = 基本) / `_changerId` / `_changedMs` の 3 本。見た目は首輪の `Band` の `_MainTex` を MaterialPropertyBlock で差し替え、埋もれ 2 犬種 (ポメ・サモエド) は `ClipAnchor/AttachmentRing` の `_Color` を革と真鍮のときだけ真鍮色にする (ほかの柄は `KH_ClipNeutral` の色のまま)。材質の複製なし。`KD_AccessoryBox` は触っていない。
- リード (1 人 1 つ): 新規 `KH_CollarLeash` (None、カタログの consumer。`KD_PetFxBox` の形) と `KH_CollarTag` (Manual + VRCPlayerObject。`KD_PlayerFxTag` の形。同期は string 1 本)。選択の正本はカタログの `Sel.collar` (PlayerData)。各犬の縄・スナップ・持ち手 (`KH_LeashCollar.ropeMesh / snapVisual / handleVisual`) を、その犬をつないでいる人 (`KH_LeashManager.walkers`) の選択で描く。他人の選択は札から読み、所持 (`KD_ShopItem.OwnedBy`) を確かめてから描く。持っていなければ基本。
- 手元メニュー: `KD_TreatPouch.Collar.cs` (新規 partial)。かざりタブの一覧の面で、撫でFX の行の後ろに「首輪とリード」の小見出しと 5 行。所持 → [えらぶ] でセットの面 (1 行目 自分のリード、2〜7 行目 犬 6 匹、かざりの AccRow を使い回す)、未所持 → [買う] で共通の確認カード (kind `ConfirmCollar` = 18、DES-UI-005)。購入はカタログの `_RequestBuy` (払う → 解放 → リードの柄として選ぶ。犬の首輪は次の明示の操作)。既存ファイルへの差し込みは `KD_TreatPouch.Acc.cs` 4 か所、`.PetFx.cs` 1 行 (`HillsideAccCatalogLevel`)、`.Confirm.cs` 2 行。2 列の格子なので、前に並ぶセルが奇数なら小見出しの前に空きセル (`CollarHead0`) を出す (今は 14 + 2 + 11 = 27 で奇数)。
- 行の器: `Menu/7` は周回版で実行禁止なので、`Collar/1` が かざりの Content に `FxHead0..1` / `FxRow0` の複製で `CollarHead0..2` と `CollarRow0..4` を足す (撫でFX の同梱の拡張 `KD_BundledPetFxEditor` と同じ局所の足し方)。行の表 `KD_HandMenuScrollEditor.RowsFor(TabAcc)` と押し先 `ExpectedRowEvent` (`_Collar{i}`) に足したので、Menu/20・21 の並びの検査はこの行込みで見る。`Menu/4 Setup Shop` がカタログの通知先を作り直しても `KH_CollarLeash` を残す。
- テクスチャ (仮): `scripts/art/leash-assets.cjs --patterns` が `Textures/Walking/KH_Leash_Pattern_{Stripe,Check,Momiji,LeatherBrass}_provisional.png` (1024²、基本の BaseColor と同じ並び: 下 768px が犬ごとの 128px の帯で地は識別色、その上が金具・持ち手・縄) だけを書く (基本の 3 枚とメッシュは書かない)。取り込み設定は Collar/1 が基本の BaseColor と同じにする。**本番の絵は BUG-DOG-011 (金具の位置) の VR 確認の後。**
- メニュー `Tools/KominkaHillside/Collar/` (`KH_CollarPatternEditor`): `0 - Create Collar Programs` / `1 - Setup Collar Pattern` (冪等、保存しない) / `2 - Verify Collar Pattern` (`COLLAR_OK n` / `COLLAR_FAIL`、ログ `Temp/KH_Collar/verify.txt`)。

### 統括が回す順 (Unity)

1. C# コンパイル (`bash scripts/unity-compile.sh`)。`KD_TreatPouch` が `KH_CollarPattern` を参照するので、U# はまだ通らない。
2. `Collar/0 - Create Collar Programs` (Program Asset 3 つ) → `bash scripts/unity-compile.sh --udon`。
3. `Menu/4 Setup Shop` (collar の 5 行) → `Menu/4a Verify Shop`。
4. `Collar/1 - Setup Collar Pattern` (器 3 つ・配線・行・字形・言語表)。
5. `Menu/19b Assign Network IDs` (新しい同期物 `KH_CollarPattern` と PlayerObject の `KH_CollarTag`) → 保存。
6. 検査: `Collar/2 - Verify Collar Pattern`、`Menu/4a`、`Menu/20 Verify Pouch`、`Menu/21 Verify Scroll Layout`、`Menu/6h Verify Pet Fx` (カタログの通知先)、`Walking/5 Verify` (NetworkID)。

### 同期の設計 (要点)

- 首輪: 書くのは `KH_CollarPattern` の Owner だけ (既定 = マスター。`OnOwnershipRequest` は false、`SetOwner` なし)。Owner は呼んだ人 (`NetworkCalling.CallingPlayer`) について、犬と柄の範囲・60 秒 (最後に替えた人以外。飾りとは独立)・セットの所持 (その人の台帳の `own.{key}`) を確かめてから書く。購入直後で所持が届いていなければ「保留」を返し、要求側が 5 秒以内に再送する (飾りと同じ手順)。
- 途中参加: 3 本の同期配列を最初の `OnDeserialization` で受けて描く。新しいインスタンスは 0 (基本) から。
- 退室: 首輪の柄は残る。替えた人の退室は 60 秒の待ちだけを外す (Owner の `OnPlayerLeft` / 移譲後の見直し)。
- リード: 本人の選択は PlayerData (`Sel.collar`) とカタログが持ち、`KH_CollarTag` (PlayerObject) で配る。読む側が所持を確かめる。退室すると札は消え、その人の犬の縄も外れて隠れる。

### 残り

- 本番のテクスチャ 4 枚 (BUG-DOG-011 の VR 確認の後)。今の 4 枚は仮で、ファイル名に `provisional` を付けた。差し替えは同じパスへ上書きすれば配線は変わらない (名前を変えるなら Collar/1 の表も)。
- Play: 首輪とリードの見え方 (6 犬種 × 4 柄。帯が見える 4 犬種、埋もれ 2 犬種は金具とリード)、購入 → リードに反映、セットの面の 60 秒表示、再入室でリードが戻る。
- 2 人: 首輪の柄が両方に同じに見える・途中参加・60 秒・他人のリードの柄 (近い → 遠い (LineRenderer、柄なし) → 近いの往復で戻る)・未所持の柄を名乗っても基本に見える。
- ClientSim の 2 ケース (Course/129、群 `collar-pattern`。Codex 相談の結論、2026-10-13): `collar-restore-delayed-35s` (遅い復元を台帳のヒープで模擬) と `collar-select-basic-roundtrip`。**作っただけで未実行。** 回すのは統括の判断。
- Quest / iOS ([VER-WLD-001](VER-WLD-001-quest-ios-devices.md))。テクスチャ 4 枚 (1024²) ぶん容量が増える (Build Size で測る)。
- 首元の飾りとの干渉表 (§4) の見直しは未着手 (形は変えていないので新しい組は増えない見込み、推定)。

### 開いている問い (ユーザー確認待ち)

- 既定を無料の `collar.basic` にした仮決定でよいか。
- しま・チェック・もみじのとき、ポメ・サモエドの金具 (`AttachmentRing`) は `KH_ClipNeutral` の金色のまま。帯の金具 (灰色) とそろえるかは BUG-DOG-011 の「要判断」と同じ論点。
- リード台の留め具 (`KH_LeashClip`) と LineRenderer の縄は柄を付けていない (共有材質のまま)。付けるかは未決定。**既存の `KH_LeashCollar` は `lightRope` が false でも、他人の縄が自分から `ropeStraightDistance` (15 m) より遠いと LineRenderer で描く** (IMP-DOG-036 S6) ので、他人の選んだ柄は離れると消え、`ropeFullDistance` (13 m) より近づくと戻る。2 人の確認条件に「近い → 遠い → 近いの往復で柄が戻る」を入れる (Codex 独立レビュー 2026-10-13 低)。
- 購入するとリードの柄はすぐその柄になる (カタログの `_RequestBuy` が選ぶ) が、犬の首輪は替わらない (飾りと同じく「買う」と「つける」を分ける)。この分け方でよいか。
- 13 言語の文言 (AI の下書き、校正なし)。

### 古民家の正本への反映 (§6 に足すこと。ここでは編集しない)

- §6 の 3 か所に加えて: 裏山の実装では分類 `collar` を新設し、無料の既定 `collar.basic` を置いた (仮)。商品キーは `collar.stripe` / `collar.check` / `collar.momiji` / `collar.leather_brass`。古民家に同じ商品を入れるときは同じキーにそろえる。

## 記録

2026-10-13 (統括 Fable): Course/129 Walk Harness Collar Pattern を Codex 相談の結論どおり最小群だけ実行 (1 回目は起動の空応答で NOT_EXECUTED、25 s 置いて再試行)。run 20261013-052444 COLLAR_PATTERN_PASS cases 2 sceneUnchanged true (collar-restore-delayed-35s / collar-select-basic-roundtrip)。実通信・2 人・途中参加・Owner 退室・端末はユーザーの Play。

2026-10-11: 起票 (IMP-DOG-048 の決定から)。Unity・モデル・商品定義は未変更。

2026-10-13: 2 つの未決定 (枠・分類) の推奨を記録 (Opus の静的確認、統括 Fable)。コード・シーンは変更していない。

2026-10-13: ユーザー決定「推奨でお願いします」(別の挙動・新分類 collar・小見出し・ミニチュア基本色)。§推奨 の冒頭に決定行。実装は BUG-DOG-011 の金具の位置の後。

2026-10-13: 実装 (Opus サブエージェント、統括 Fable)。商品 5 行・文言 13 言語・`KH_CollarPattern` / `KH_CollarLeash` / `KH_CollarTag`・手元メニューの行・Collar/0〜2・仮テクスチャ 4 枚。既定は無料の `collar.basic` (AI の仮決定、ユーザー確認待ち)。Unity は未実行 (コンパイル・Setup・Verify は統括)。

2026-10-13: 統括の検証 (C# 0 / U# 0、Collar/2 COLLAR_OK 85、Menu/4a・20・21・6h・Walking/5 PASS、保存)。Codex 独立レビュー: 10 点のうち 8 点は問題なし、2 点を反映 (中: `KH_CollarLeash` の復元待ちの 30 s 打ち切りをやめ、`_Tick` が `catalog.Resolved()` を見るまで確かめ続ける / 低: 遠い他人の縄は LineRenderer で柄が消える旨の注記と、開いている問い・2 人の確認条件への追記)。ClientSim の 2 ケース (Course/129) を追加 (未実行)。
