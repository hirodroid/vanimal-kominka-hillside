---
id: IMP-DOG-016
type: IMP
system: DOG
status: review
priority: Medium
---

# IMP-DOG-016 呼ぶ・ポーズを移植し、リード除外と斜面判定を足す

2026-09-23 起票。[DES-UI-003](../DES-UI-003-menu-scope-and-polish.md) で **呼ぶ・ポーズは既存の種類・操作を保ち、リードを外して使う**と決まった。古民家の場所決めには **接地・傾斜・法線・NavMesh の判定が一切なく、`ValidPlacement` は NaN / Inf チェックだけ** (`KominkaDogRun: Assets/KominkaDogRun/Scripts/KD_SleepSlot.cs:277`)。平地の庭では成立していたが、裏山は斜面なのでここに判定を足す。

実装順は [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md) → [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md) → **本Issue** ([IMP-DOG-014](IMP-DOG-014-treats-port.md) / [IMP-DOG-015](IMP-DOG-015-accessories-and-petfx-port.md) と並行可)。**接続競合と斜面の契約は [DES-DOG-001](../DES-DOG-001-leash-walking.md)**、UI 頁は IMP-UI-001 が持つ。

## 範囲

- `KD_SleepSlot` / `KD_SleepBoard` / `KD_SleepPlacement` / `KD_CallPose` と `KD_DogWanderController.Sleep` を裏山へ配線する。
- 継承するもの: 犬 → ポーズ → 場所 の流れ、5 ポーズ (ごろん / ふせ / おすわり / うずくまる / へそ天) + あご乗せ・お手・到着後の伸び+あくび、予約枠 (3 秒更新・15 秒途絶で解放・先着 1 人)、終了 4 条件 (自由にする / 権限者の取り上げ / 呼んだ人の退室 / Ownership 移譲)、寝ている犬を おもちゃ・おやつ・Fetch の候補から外す扱い。
- **4 動作 (へそ天 / あご乗せ / お手 / 伸び+あくび) 各 30pt の課金ゲート**を `ActionOwnedBy` で継承する。価格表は [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md)。
- 「呼ぶを全員に許可」を設定面へ置く (初期 ON、権限者 = ワールド作者・不在時 Master、権限者以外は操作不可)。初期 ON は 2026-09-23 のユーザー決定 (古民家の最近の変更に合わせる)。権限者は継承。
- **変更 1 — リード除外**: 自分・他人を問わずリード接続中の犬を対象外にする。行を暗色にし、理由を文言で出す (「リード中。さんぽで外してから」)。逆に、**呼ばれて待機中の犬はさんぽタブで接続不可**にする (暗色 + 理由)。
- **変更 2 — 場所の検証**: マーカーを真下へレイキャストして地面へ接地させ、**地面法線 ≥ 0.9 (約 25°、仮値) のみ有効**とする。無効時はマーカーを赤くして確定不可にし、色だけでなく文言「この姿勢には平らな場所が必要です」も出す。集合の判定より厳しくする理由は、ふせ・ごろん・へそ天が体を地面に密着させるため。数値は実測して確定する。
- **変更 3 — 犬の移動**: 古民家は水平距離の StepToward + Y の MoveTowards で接地スナップをしない。裏山では散歩と同じ地形追従に合わせる ([DES-DOG-001](../DES-DOG-001-leash-walking.md) の「隊形と斜面」と同じ扱い)。
- 呼ぶタブの頁 (犬 6 → ポーズ 5 → 場所) と端末別の配置操作 (VR は手の前 + 板の ◀▶、Desktop は距離ホイール + Q/E、Touch は [ここへ呼ぶ]) は [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md)。
- UI 頁は古民家の `KD_TreatPouch` の partial `.Sleep` + `KD_CallPose` のポーズ面をそのまま使い、ビルダーは [計画書](../../../plans/2026-09-23-menu-rebuild.md) 段階 0 で移植済みのものに配線を足す (新しい頁を自作しない)。ポーズ面のミニチュアは `KD_CallPoseEditor` を段階 2 で焼く。

## やらないこと

「離れたら解除」の復活 (`releaseDistance` / `arriveTimeout` は古民家でも定義のみで参照ゼロ。挙動を変えない)、接続したままポーズを取り終了時に散歩を再開する案 (保留を継続)、新しいポーズの追加、写真・ライブとの連動、Quest / iOS の合格判定。

## 完了条件

- [x] コンパイル 0 件で、呼ぶ・ポーズ・場所決めのシーン配線を Verify で確認した (2026-09-24: C# 0 / U# 82 本・エラー 0。Menu/6b Verify Sleep System (斜面の定数と法線値を出力)、20 (段階表 2)、21 (404/0)、22、25 (1661/0) 全 PASS、Walking/5 209/0)
- [ ] 犬 → ポーズ → 場所 の流れが 6 匹すべてで平地で成立する (予約・配置・到着・終了) — **実装済み・確認待ち**
- [ ] リード接続中の犬が暗色 + 理由で除外され、外せば呼べる。呼ばれて待機中の犬はさんぽで接続できない (T-010) — **実装済み・確認待ち** (集合中の犬も除外。同時に乗ったらリードが勝つ)
- [ ] 斜面で赤マーカーと文言が出て確定できず、平地では成立する。**採用した法線値を記録した** — **法線 0.9 を仮採用して実測を記録済み (下の「斜面の判定」)。赤マーカーと確定不可は実装済み・確認待ち**
- [ ] 4 動作が所持で解放され、未所持では価格が出る — **実装済み・確認待ち** (`KD_ShopItem_Action_*` の 4 行を配線)
- [ ] 「呼ぶを全員に許可」を OFF にすると権限者以外が呼べない。寝ている犬は OFF にしても起きない — **実装済み・確認待ち** (許可の既定 ON は段階 3 の設定面)
- [ ] 2 人で同じ犬を選んで予約の取り合いになり、先着が保たれる — **実装済み・確認待ち**
- [ ] PCVR でユーザーが斜面の見え方と配置操作を確認した。**Quest / iOS の合格判定は [VER-WLD-001](../VER-WLD-001-quest-ios-devices.md)**

## 進捗

**2026-09-24 実装済み・Verify 全 PASS (Play 未実施)。** 実行順・Verify の数値・逸脱は [計画書「実装記録 / 段階 2」](../../../plans/2026-09-23-menu-rebuild.md#段階-2-2026-09-24)、ファイルごとの差分は [kominka-dog-system「段階 2」](../../imports/kominka-dog-system.md#2026-09-24-おやつかざり撫でfx呼ぶ-段階-2)。Codex D+M+U は実行中。コミットは未。

2026-09-25 撤回: コミット済み c27a7e3 (Codex D+M+U 済み)。

- 器: `KH_Dogs/KD_SleepBoard` + `KD_SleepSlot` ×6 (Menu/6)、`KH_Dogs/KD_SleepPlacement` (Menu/6i)、`KH_Dogs/KD_CallPose` (Menu/6j。6 匹 × 7 ポーズのミニチュアを `Models/DogPreviews` へ焼く。焼くときは首輪を隠す)。6 は 6 匹の wander / fetch、`KD_LiveAccess`、`pouch.sleepBoard`、`KH_LeashManager.sleepBoard` も配線する。予約枠・終了 4 条件・権限者は古民家のまま。
- **変更 1 (リード除外)**: `KD_SleepBoard` に読むだけの API (`_IsCalled` / `_IsLeashedDog` / `_IsGatheringDog` / `_IsWalkBusy`)。予約・確定・`_BeginSelection` はリード中・集合中の犬を拒否する。呼ぶタブの行は `SleepLeashed` (「リード中。さんぽで外してから」) / `SleepGathering` の状態で暗くする。`KD_CallPose` はリード中・集合中は開かず、開いている間にリードが付いたら操作を止めて理由を出す。逆方向は `KH_LeashManager` が呼ばれた犬への `ReqAttach` を拒否し、さんぽタブに理由 (`WordWalkGateCalled`) を出す。呼ぶとリードが同時に同じ犬に乗ったら、リードが勝って呼ぶを解放する。**集合中の犬の除外は契約外の追加** ([DES-DOG-001](../DES-DOG-001-leash-walking.md) Q2 と揃えた)。
- **変更 2 (場所の検証)**: `KD_SleepSlot._PlacementGroundOk` = 真下へのレイキャストで高さと法線を見る。定数は 1 か所 (`HillsideMinGroundNormal = 0.9`、layer 0 / 11、probe 0.5 m、高さの許容 0.1 m)。`ValidPlacement` は「場所を選んで呼ぶ」の確定にだけ効く。`KD_SleepPlacement` はマーカーを地面へ落とし、無効な場所では赤マーク (`Models/SleepPlacement/Invalid.mat`) + 「この姿勢には平らな場所が必要です」を出す。ボタン / Desktop のクリック / VR の長押しの 3 経路とも確定できない。
- **変更 3 (犬の移動)**: `KD_DogWanderController.Sleep.cs` の `SleepStep` は集合と同じ `GatherStep` (NavMesh + 接地) で向かう。失敗したら旧 `StepToward`。最後の据わりも接地させる。
- 文言の新規は 4 (`WordSleepLeashed` / `WordSleepNeedFlat` / `WordWalkStateCalled` / `WordWalkGateCalled`)。ポーズ面・場所決めの固定ラベル 18 行の束縛を戻した。

### 斜面の判定 (2026-09-24 実測)

採用値は **地面法線 ≥ 0.9 (約 25.8°。仮値)**。集合 (0.5) より厳しくするのは、伏せる・寝る姿勢は体を地面に付けるため。

| 測った範囲 | 合格 / 点数 | 最小の法線 |
|---|---|---|
| ラック・広場・見晴らしの各 3 m 以内 | 113 / 113 | 0.926 |
| 散歩道の 17 ノード | 833 / 833 | 0.963 |
| 地形全体 | ≥ 0.9 が 83%、≥ 0.85 が 96% | — |

測った範囲 (拠点のまわりと散歩道) ではすべて置ける。限界付近で犬が沈む・浮くかは PCVR で見てから確定する ([計画書の実機チェックリスト](../../../plans/2026-09-23-menu-rebuild.md#3-段階-2-おやつかざり撫でfx呼ぶ2026-09-24-追加) の「呼ぶ」)。

## 関連

- [DES-UI-003](../DES-UI-003-menu-scope-and-polish.md) 順番 3: 継承する範囲と変更 1〜3、保留にした 2 件
- [DES-DOG-001](../DES-DOG-001-leash-walking.md): 接続競合・隊形と斜面の契約。集合の配置判定と数値を揃える
- [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md): 追加動作 4 種 各 30pt の価格表と所持判定
- [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md): 呼ぶタブの頁と端末別の配置操作
- [world-ui-spec](../../../world-ui-spec.md) T-010: リード中の除外と斜面・平地の検証条件
