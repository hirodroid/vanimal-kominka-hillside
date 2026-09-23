---
id: IMP-PRG-001
type: IMP
system: PRG
status: todo
priority: High
---

# IMP-PRG-001 台帳・おじいさん・支度用ポイント・購入を裏山へ配線する

2026-09-23 起票。[DES-UI-003](DES-UI-003-menu-scope-and-polish.md) で **初回付与 200pt をおじいさんの初回会話で一回だけ / 獲得は滞在 1 分 = 1pt のみ / 価格は古民家と同じ / おじいさんは門 (散歩の入口) 付近**と決まった。古民家の Progress 一式は `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/Progress/` に取り込み済みだが **シーン参照 0・Editor スクリプトなし** (2026-09-23 時点の GUID grep) で、価格表・品目表の正本ごと未移植。本Issueはその配線を担当する。system PRG = progress は古民家と同じ区分で、裏山ではこれが 1 本目。

実装順は [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md) の表駆動化 → **本Issue** → [IMP-DOG-014](IMP-DOG-014-treats-port.md) / [IMP-DOG-015](IMP-DOG-015-accessories-and-petfx-port.md) / [IMP-DOG-016](IMP-DOG-016-call-pose-port-and-slope.md)。各商品の実体 (メッシュ・FX・動作) は後続の 3 本が持ち、本Issueは**台帳・価格表・購入経路・おじいさん**までを持つ。

## 範囲

- `KD_ProgressLedger` / `KD_ShopItem` / `KD_ShopCatalog` / `KD_PlayerFxTag` をシーンへ配線し、Editor を `KH_` 名で作る (古民家の `KominkaDogRun: Assets/KominkaDogRun/Editor/KD_ProductDefinitions.cs` / `KD_ProgressSetupEditor.cs` 相当)。価格表・品目表の正本をここに置く。
- 価格は古民家と同じ。飾り 9 品 + 花セット = **260pt**、撫でFX 6 種 0〜40 = **120pt**、追加動作 4 種 各 30 = **120pt**、おもちゃ 5 品 10〜50 = **150pt**、おやつ 4 種 1〜5pt / 5 個。CE 商品 (王様・夏・キャンディ・パンプキン・ぬいぐるみ 3 点・ライブパック) は Credits。
- 付与は **Bonus.keeper の 200pt 一回のみ**。古民家の初期配布 100 (Bonus.initial) は**置かない**。骨型ビスケット初期 5 個 (`_GrantInitialStockOnce`) は継承し、話しかけなくてもおやつは使える。滞在は 1 分 = 1pt (AFK 除外なし・上限なし)。達成ボーナス・デイリーは入れない。
- 重複防止を継承する: 付与済み判定 (Bonus.keeper の既存値) → 台帳の復元完了待ち → `_giftPending` の 1 秒再試行 → 「フラグ true → Earned 加算」の書込順 (`KominkaDogRun: Assets/KominkaDogRun/Scripts/Progress/KD_ProgressLedger.cs:179`)。購入は「Own=true → Spent」の順。
- PlayerData 名前空間を決める (**KH_Commerce_v1 を推奨**。KD_ のままでも別ワールドなら衝突しないが、検査と文書で区別しやすい)。キー体系 (.Earned / .Spent / .Own.{productKey} / .StockB / .StockE / .Sel.{categoryKey} / .Bonus.{bonusKey}) は継承する。
- CE は台帳に書かず `Store.DoesPlayerOwnProduct` で判定する。**同一 Product / Listing を裏山シーンの UdonProduct として参照**し、購入情報の読込後に判定する。読込中を未所持扱いしない契約は [world-ui-spec](../../world-ui-spec.md) §6 / T-006 に従う。
- おじいさん (`KD_ShopKeeper`) を門付近に配置する。Interact のみ・近接自動オープンなし・4m で閉じるを継承。会話 UI の板を継承し、ナビを裏山向けへ差し替える (過ごし方 / さんぽ / おやつ / 呼ぶ / またね)。付与後の挨拶で「メニューの かざり で支度できる」と端末別のメニューの開き方を案内する。NPC 板から かざり を開く導線は作らない。
- 残高はメニューのヘッダー右に常時表示する。表示枠は [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md)、値の供給が本Issue。
- 消費が世界で 1 箇所であることの機械検査 (古民家の `scripts/check-consume-once.js` 相当) を裏山へ移植する。

## やらないこと

ギフトコード (古民家にはあるが裏山では未決定。必要になったら別Issue)、古民家からのポイント・所持の引き継ぎ (対象外と決定)、追加の売り場や告知帯 (保留)、CE の実購入検収 (VER 系)、物理の棚の設置 (おもちゃの棚は [DES-DOG-002](DES-DOG-002-toy-behaviour.md))。

## 完了条件

- [ ] コンパイル 0 件で、Progress 一式のシーン配線を Verify で確認した
- [ ] おじいさんに話しかけて 200pt がちょうど一回だけ入る。連打・台帳の復元遅延中・退室後の再入室で重複しない (T-007)
- [ ] 滞在でポイントが増え、再入室で残高が復元する
- [ ] 購入で Own.* と Spent が保存され、再入室で所持が復元する。CE は Store の判定で所持が出る
- [ ] **残高 0 のまま**散歩・リード解除・ボール・フリスビー・撫で・集合が使える。ポイントを要求する表示が出ない (T-008)
- [ ] ClientSim ハーネスで付与・購入・復元の固定手順を通した。PCVR はユーザーが確認する
- [ ] **Quest / iOS の合格判定は [VER-WLD-001](VER-WLD-001-quest-ios-devices.md)**

## 関連

- [DES-UI-003](DES-UI-003-menu-scope-and-polish.md): 付与額・獲得条件・おじいさんの配置を決めた大元 (順番 1 と「ユーザー回答と決定」)
- [game-design §4](../../game-design.md): 支度用ポイントと解放の正本。[world-ui-spec](../../world-ui-spec.md) §6 が CE の読込・購入通知の契約と T-006〜T-008
- [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md): 残高枠・購入確認カード・未受取の案内の置き場所
- [kominka-dog-system](../imports/kominka-dog-system.md): 古民家からの移植記録 (Progress 一式の取り込み状況)
- [DES-WLD-001](DES-WLD-001-round-trip.md): 古民家⇄裏山の往復で何が残るか (所持は引き継がない前提)
