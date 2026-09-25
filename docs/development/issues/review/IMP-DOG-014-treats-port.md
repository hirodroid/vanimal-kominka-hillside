---
id: IMP-DOG-014
type: IMP
system: DOG
status: review
priority: Medium
---

# IMP-DOG-014 おやつを古民家から移植する

2026-09-23 起票。[DES-UI-003](../DES-UI-003-menu-scope-and-polish.md) で **おやつの種類・基本操作・補充は古民家をそのまま継承**すると決まった (4 種と価格、出す / あげる / しまう、世界全体で枠 3、食べ 2.6s + お礼 1.6s、購入のみの補充)。スクリプトは `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/` に取り込み済みだが **シーン参照 0・Editor とメッシュ類が未移植**。本Issueはおやつの実体と配線を担当する。

実装順は [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md) → [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md) → **本Issue** (かざり / 呼ぶ と並行可)。台帳・価格表は IMP-PRG-001、おやつタブの UI 頁は IMP-UI-001、**接続犬の反応は [IMP-DOG-006](../IMP-DOG-006-leashed-treat-approach.md)** が持つ。

## 範囲

- `KD_TreatBox` / `KD_TreatSlot` ×3 / `KD_TreatController` / `KD_TreatFx` をシーンへ配線する。同期の権威 (Box = マスター固定、Slot = 出した人、実体 = VRCObjectSync) とタイムアウト (予約 20s / 放置 60s / Ownership 待ち 5s / 手順停滞 8s / 吸着 3s) を継承する。
- 古民家の Editor (`KominkaDogRun: Assets/KominkaDogRun/Editor/KD_TreatSetupEditor.cs` 一式) は **`KD_` 名のまま `Assets/KominkaHillside/Imported/KominkaDogRun/Editor/` へコピー**し、裏山の差分は partial (例 `KD_TreatSetupEditor.Hillside.cs`) に隔離する。**裏山で新規に書く Editor だけ `KH_{Name}Editor`** (2026-09-23 決定。[計画書](../../../plans/2026-09-23-menu-rebuild.md) D-3)。`TreatSlotCount = 3` の正本もここへ置く (古民家では `KD_DogDefs.cs:419`)。
- 門番 `DispenseGateWith` と `GiveStepWith` の純関数と理由の優先順 (おやつがありません / もう出している / いま出せません) を、自己テストごと継承する。
- おやつのメッシュ・マテリアル・FX テクスチャを移植する (古民家の自作素材)。移植記録は [kominka-dog-system](../../imports/kominka-dog-system.md) へ追記し、権利確認は [素材の確認記録](../../../AI_INDEX.md#素材の確認記録) の型に統合する。
- 4 種の商品定義 (骨型ビスケット 1pt / 肉球クッキー 2pt / 星型クッキー 3pt / ジャーキー 5pt、各 5 個パック・上限 20) は IMP-PRG-001 の価格表を参照する。骨型の初期 5 個も IMP-PRG-001 側。
- おやつタブ (4 行 + 状態 1 行 + [しまう] + [買う]) の頁は IMP-UI-001。**枠 ●●○ は出さない** (古民家で IMP-TRT-007 により削除済み)。本Issueは Box が返す状態をその頁へ渡すところまで。
- 犬側は 6 匹で動くことを確認する。`EligibleDog` の除外 (食べ始め保持 / 集合・ライブ等で拘束中 / お礼中 / Fetch 中 / 撫でられ中 / 呼ばれて待機中) を裏山の状態名へ合わせる。**接続犬を 1 匹ずつ呼ぶ変更 (EligibleDog へ接続状態を足す) は [IMP-DOG-006](../IMP-DOG-006-leashed-treat-approach.md)** で行い、本Issueでは足場だけ用意する。
- 複数人で枠 3 の表示を確認する (T-009)。
- UI 頁は古民家の `KD_TreatPouch` の partial `.Treat` を使い、ビルダーは [計画書](../../../plans/2026-09-23-menu-rebuild.md) 段階 0 で移植済みのものに配線を足す (新しい頁を自作しない)。
- **[買う] は裏山の追加**: 古民家の板にはおやつの購入が無く、棚で買う。裏山は棚のエリアが無いのでメニューで買う (DES-UI-003 のユーザー指示)。本Issueで **行の [買う] の生成 (ビルダー Hillside)・確認カードへの kind Treat 追加・`catalog._RequestBuy(key)`・キャンセル・残高と在庫上限 (20) の再確認・購入後の行の再描画**まで行う。台帳の口は [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md)。

## やらないこと

接続犬の反応設計と 1 匹ずつの呼び寄せ (IMP-DOG-006)、物理の棚 (`KD_ShopShelf`) の設置、おやつの新種、UITranslations の多言語化 (日本語のみ provisional)、Quest / iOS の合格判定。

## 完了条件

- [x] コンパイル 0 件で、おやつ一式のシーン配線を Verify で確認した (2026-09-24: C# 0 / U# 82 本・エラー 0。Menu/6d Verify Treats (構造・3 枠・メッシュ・定位置・タイムアウト 20/60/5/8・商品 4 行・[買う] 4 本・NetworkID 7 本・古民家の自己テスト Gate/Give/Dispense/Claims・FX)、20 (段階表 2)、21 (404/0)、22 全 PASS、Walking/5 209/0、`check-consume-once` OK)
- [ ] 出す / あげる / しまう が 1 人で成立する (吸着 → 犬の接近 → 食べ → お礼まで) — **実装済み・確認待ち**
- [ ] 在庫が食べさせるたびに減り、購入で 5 個増え、再入室で復元する — **実装済み・確認待ち**
- [ ] **初期 5 個を使い切った後にメニューの [買う] から補充できる** (残高不足・在庫上限 20・キャンセルの各表示を含む) — **実装済み・確認待ち** (確認カードは 上限 / 不足 / 購入可 の 3 態、[やめる] は「購入をやめました」)
- [ ] 4 人が同時に出して枠 3 の理由が出る。枠が空けば再操作でき、不成立時に在庫を失わない (T-009) — **実装済み・確認待ち** (複数人は未確認)
- [ ] 犬の食べ・お礼が 6 匹すべてで動く (犬種ごとのアニメと FX の位置を確認) — **実装済み・確認待ち**
- [ ] ClientSim ハーネスで固定手順 (出す → あげる → しまう → 在庫確認) を通した — 未。おやつ用のハーネスは無い (フリスビー用のみ)
- [ ] PCVR でユーザーが手元の操作感を確認した。**Quest / iOS の合格判定は [VER-WLD-001](../VER-WLD-001-quest-ios-devices.md)**

## 進捗

**2026-09-24 実装済み・Verify 全 PASS (Play 未実施)。** 実行順・Verify の数値・逸脱は [計画書「実装記録 / 段階 2」](../../../plans/2026-09-23-menu-rebuild.md#段階-2-2026-09-24)、ファイルごとの差分と資産コピーは [kominka-dog-system「段階 2」](../../imports/kominka-dog-system.md#2026-09-24-おやつかざり撫でfx呼ぶ-段階-2)。Codex D+M+U は実行中。コミットは未。

2026-09-25 撤回: コミット済み c27a7e3 (Codex D+M+U 済み)。

- 器: `KH_Dogs` 直下に `KD_TreatBox` (Manual、マスター固定)、`KD_TreatSlot_0..2`、`KD_Treat_0..2` (Pickup + Rigidbody + VRCObjectSync + 4 メッシュ)、`KD_TreatHome_0..2` (初回だけ `KH_LeashRack` の −Z 1.5 m に 0.6 m 間隔、地面へレイキャスト。以後は人の値)、`KD_TreatFx`。6 匹の `KD_WanderSystem_*.treatBox` を配線。入口は `Menu/6c Setup Treats` / `6d Verify Treats` (新規 `KD_TreatSetupEditor.Treats.cs`)。同期の権威とタイムアウトは古民家のまま。
- 犬の除外 (足場): `KD_TreatBox.HillsideDogBusy` が リード中 (`KH_LeashManager._IsLeashed`)・集合中 (`_IsGathering`)・フリスビー中 (Ready を含む) の犬を `EligibleDog` と `AllDogsEngaged` から外す。接続犬を 1 匹ずつ呼ぶ変更は [IMP-DOG-006](../IMP-DOG-006-leashed-treat-approach.md) で、差し込み口はコメントで示した。
- [買う] (裏山の追加): `_TreatBuy0..3` → 共有の確認カード (kind `ConfirmTreat` = 6) → `_TreatConfirmBuy` → `catalog._RequestBuy` → `KD_ShopItem._Buy` → `ledger._TryBuyStocked` (5 個 / 上限 20)。行のヒントは「5個入り N pt」+「あと N pt」/「所持数の上限です」。結果は状態行に 4 秒。枠 ●●○ は出さない。
- 未受取の案内は状態行 (statusLabel が空のとき) に出す。Page_Treat に上段の行が無いため、行 0 の上ではない。
- 文言の新規は 1 (`WordTreatBuyCancelled`)。資産 (Models/Treat 26、Materials の Treat・TreatFx・TreatPlate) は古民家と同じ GUID でコピー済み。
- 人が確認する点: [計画書の実機チェックリスト](../../../plans/2026-09-23-menu-rebuild.md#3-段階-2-おやつかざり撫でfx呼ぶ2026-09-24-追加) の「おやつ」(T-009 を含む)。

## 関連

- [DES-UI-003](../DES-UI-003-menu-scope-and-polish.md) 順番 2: 継承する範囲と変更点の切り分け
- [IMP-DOG-006](../IMP-DOG-006-leashed-treat-approach.md): 接続犬を 1 匹ずつ呼ぶ変更 (EligibleDog への接続状態の追加はこちら)
- [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md): 4 種の価格・パック数・上限・初期 5 個の正本
- [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md): おやつタブの頁と状態表示
- [world-ui-spec](../../../world-ui-spec.md) T-009: 4 人同時 × 枠 3 の検証条件
