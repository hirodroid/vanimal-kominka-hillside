---
id: IMP-PRG-001
type: IMP
system: PRG
status: review
priority: High
summary: 台帳・おじいさん (門)・支度用ポイント (初回 200pt 一回・滞在 1分1pt)・購入と CE 判定。2026-09-24 実装済み・Verify 全 PASS (e4f5906、名前空間 KH_Commerce_v1)。T-007 / T-008・復元の実機確認待ち
next: "200pt が一回だけ入る (T-007)、滞在加算と再入室の復元、購入の保存と CE の所持判定、残高 0 で基本機能が使える (T-008)。**AI 側の残作業**: 台帳用の ClientSim ハーネスが未作成で、付与・購入・復元の固定手順が未実行 (ユーザーの PCVR 確認とは別)"
---

# IMP-PRG-001 台帳・おじいさん・支度用ポイント・購入を裏山へ配線する

2026-09-23 起票。[DES-UI-003](DES-UI-003-menu-scope-and-polish.md) で **初回付与 200pt をおじいさんの初回会話で一回だけ / 獲得は滞在 1 分 = 1pt のみ / 価格は古民家と同じ / おじいさんは門 (散歩の入口) 付近**と決まった。古民家の Progress 一式は `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/Progress/` に取り込み済みだが **シーン参照 0・Editor スクリプトなし** (2026-09-23 時点の GUID grep) で、価格表・品目表の正本ごと未移植。本Issueはその配線を担当する。system PRG = progress は古民家と同じ区分で、裏山ではこれが 1 本目。

実装順は [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md) の表駆動化 → **本Issue** → [IMP-DOG-014](IMP-DOG-014-treats-port.md) / [IMP-DOG-015](IMP-DOG-015-accessories-and-petfx-port.md) / [IMP-DOG-016](IMP-DOG-016-call-pose-port-and-slope.md)。各商品の実体 (メッシュ・FX・動作) は後続の 3 本が持ち、本Issueは**台帳・価格表・購入経路・おじいさん**までを持つ。

## 範囲

- `KD_ProgressLedger` / `KD_ShopItem` / `KD_ShopCatalog` / `KD_PlayerFxTag` をシーンへ配線する。古民家の Editor (`KominkaDogRun: Assets/KominkaDogRun/Editor/KD_ProductDefinitions.cs` / `KD_ProgressSetupEditor.cs` / `KD_ShopSetupEditor.cs`) は **`KD_` 名のまま `Assets/KominkaHillside/Imported/KominkaDogRun/Editor/` へコピー**し、裏山の差分は partial (例 `KD_TreatSetupEditor.Hillside.cs`) に隔離する。**裏山で新規に書く Editor だけ `KH_{Name}Editor`** (2026-09-23 決定。[計画書](../../plans/2026-09-23-menu-rebuild.md) D-3)。価格表・品目表の正本は裏山値の `KD_ProductDefinitions` に置く。
- 価格は古民家と同じ。飾り 9 品 + 花セット = **260pt**、撫でFX 6 種 0〜40 = **120pt**、追加動作 4 種 各 30 = **120pt**、おもちゃ 5 品 10〜50 = **150pt**、おやつ 4 種 1〜5pt / 5 個。CE 商品 (王様・夏・キャンディ・パンプキン・ぬいぐるみ 3 点・ライブパック) は Credits。
- 付与は **Bonus.keeper の 200pt 一回のみ**。古民家の初期配布 100 (Bonus.initial) は**置かない**。骨型ビスケット初期 5 個 (`_GrantInitialStockOnce`) は継承し、話しかけなくてもおやつは使える。滞在は 1 分 = 1pt (AFK 除外なし・上限なし)。達成ボーナス・デイリーは入れない。
- 重複防止を継承する: 付与済み判定 (Bonus.keeper の既存値) → 台帳の復元完了待ち → `_giftPending` の 1 秒再試行 → 「フラグ true → Earned 加算」の書込順 (`KominkaDogRun: Assets/KominkaDogRun/Scripts/Progress/KD_ProgressLedger.cs:179`)。購入は「Own=true → Spent」の順。
- PlayerData 名前空間は **`KH_Commerce_v1` に決定** (2026-09-24 実装時。テスト用は `KH_TestCommerce_v1`)。KD_ のままでも別ワールドなら衝突しないが、検査と文書で区別しやすい。キー体系 (.Earned / .Spent / .Own.{productKey} / .StockB / .StockE / .Sel.{categoryKey} / .Bonus.{bonusKey}) は継承する。
- CE は台帳に書かず `Store.DoesPlayerOwnProduct` で判定する。**同一 Product / Listing を裏山シーンの UdonProduct として参照**し、購入情報の読込後に判定する。読込中を未所持扱いしない契約は [world-ui-spec](../../world-ui-spec.md) §6 / T-006 に従う。
- おじいさん (`KD_ShopKeeper`) を門付近に配置する。Interact のみ・近接自動オープンなし・4m で閉じるを継承。会話 UI の板を継承し、ナビを裏山向けへ差し替える (過ごし方 / さんぽ / おやつ / 呼ぶ / またね)。付与後の挨拶で「メニューの かざり で支度できる」と端末別のメニューの開き方を案内する。NPC 板から かざり を開く導線は作らない。
- 残高はメニューのヘッダー右に常時表示する。表示枠は [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md)、値の供給が本Issue。
- 消費が世界で 1 箇所であることの機械検査 (古民家の `scripts/check-consume-once.js` 相当) を裏山へ移植する。
- UI 頁は古民家の `KD_TreatPouch` のヘッダ残高と共有の購入確認カード (`confirmRoot`) をそのまま使い、ビルダーは [計画書](../../plans/2026-09-23-menu-rebuild.md) 段階 0 で移植済みのものに配線を足す (新しい頁を自作しない)。

## やらないこと

ギフトコード (古民家にはあるが裏山では未決定。必要になったら別Issue)、古民家からのポイント・所持の引き継ぎ (対象外と決定)、追加の売り場や告知帯 (保留)、CE の実購入検収 (VER 系)、物理の棚の設置 (おもちゃの棚は [DES-DOG-002](DES-DOG-002-toy-behaviour.md))。

## 完了条件

- [x] コンパイル 0 件で、Progress 一式のシーン配線を Verify で確認した (2026-09-24: C# 0 / U# 82 本・エラー 0。Menu/3b・4a・4e・20・21 (394/0)・22・23・11c・24・15a 全 PASS、Walking/5 208/0、`check-consume-once` OK)
- [ ] おじいさんに話しかけて 200pt がちょうど一回だけ入る。連打・台帳の復元遅延中・退室後の再入室で重複しない (T-007) — **実装済み・確認待ち** (実機または ClientSim)
- [ ] 滞在でポイントが増え、再入室で残高が復元する — **実装済み・確認待ち**
- [ ] 購入で Own.* と Spent が保存され、再入室で所持が復元する。CE は Store の判定で所持が出る — **台帳・カタログは実装済み・確認待ち**。板の購入行は段階 2 (IMP-DOG-014 / 015) で出る
- [ ] **残高 0 のまま**散歩・リード解除・ボール・フリスビー・撫で・集合が使える。ポイントを要求する表示が出ない (T-008) — **実装済み・確認待ち**
- [ ] ClientSim ハーネスで付与・購入・復元の固定手順を通した。PCVR はユーザーが確認する — 未。台帳用のハーネスは無い (フリスビー用のみ)
- [ ] **Quest / iOS の合格判定は [VER-WLD-001](VER-WLD-001-quest-ios-devices.md)**

## 進捗

**2026-09-24 実装済み・Verify 全 PASS (Play 未実施)。** 実行順・Verify の数値・逸脱は [計画書「実装記録 / 段階 1」](../../plans/2026-09-23-menu-rebuild.md#段階-1-2026-09-24)、ファイルごとの差分は [kominka-dog-system「段階 1」](../imports/kominka-dog-system.md#2026-09-24-台帳おじいさんポイント-段階-1)。Codex D+M は実行中。コミットは未。

2026-09-25 撤回: コミット済み e4f5906 (Codex D+M 済み)。

- 台帳: `KH_Dogs/KD_ProgressSystem` (`KD_ProgressLedger` 裏山改変。名前空間 `KH_Commerce_v1`、initialPoints 0 = `Bonus.initial` を書かない、keeperPoints 200、SyncMode None)。付与の順序と重複防止は古民家のまま。
- カタログ: `KH_Dogs/KD_ShopCatalog` + `KD_ShopItem_*` 50 行 (= `KD_ProductDefinitions.All`。全行 menuOnly で棚なし)、CE は `Data/CE` の UdonProduct を参照。`KH_Dogs/KD_PlayerFxTag` (VRCPlayerObject)。
- おじいさん: `KH_Dogs/KD_ShopKeeper` を門付近 (48.00, 1.73, 43.00) にラックの方を向けて置いた (初回だけ。以後は人の値)。見た目は man_farm (polyperfect、購入アセット) + `AC_KD_Npc`。ナビ 5 項目 (過ごし方 / さんぽ / おやつ / 呼ぶ / またね)、付与後の挨拶で「かざり」と端末別の開き方を案内。
- 板: ヘッダの残高表示、ポイント面 (「いま N pt」+ 入手経路 2 行 + [戻る]。ギフトコードの器なし)、未受取の案内 (おやつ・かざりの準備中の行のサブ文)。
- ようこそ (最小版): `KH_Dogs/KD_WelcomeSpot` (入口の前) と `KH_Dogs/KD_HintDirector` (手元ヒント、保存キー `KD_Onboarding_v1`)。
- 消費口の機械検査: `scripts/check-consume-once.js` を移植 (`_TryConsume` の呼び出しは 1 か所)。
- 人が確認する点: man_farm の見た目・立ち位置・向き、`KD_WelcomeSpot` の位置、ポイント面と ようこその配置。手順は [計画書の実機チェックリスト](../../plans/2026-09-23-menu-rebuild.md#2-段階-1-台帳おじいさんポイント2026-09-24-追加)。

## 関連

- [DES-UI-003](DES-UI-003-menu-scope-and-polish.md): 付与額・獲得条件・おじいさんの配置を決めた大元 (順番 1 と「ユーザー回答と決定」)
- [game-design §4](../../game-design.md): 支度用ポイントと解放の正本。[world-ui-spec](../../world-ui-spec.md) §6 が CE の読込・購入通知の契約と T-006〜T-008
- [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md): 残高枠・購入確認カード・未受取の案内の置き場所
- [kominka-dog-system](../imports/kominka-dog-system.md): 古民家からの移植記録 (Progress 一式の取り込み状況)
- [DES-WLD-001](DES-WLD-001-round-trip.md): 古民家⇄裏山の往復で何が残るか (所持は引き継がない前提)
