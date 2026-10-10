---
id: IMP-DOG-016
type: IMP
system: DOG
status: review
priority: Medium
summary: 呼ぶ・ポーズを移植し、リード接続中の除外と斜面 (接地・法線) の判定を足す。2026-09-24 実装済み・Verify 全 PASS (c27a7e3)。法線 0.9 の判定は撤回 (10-03、IMP-DOG-032 が勝つ)。Play・T-010・斜面の PCVR 確認待ち
next: "残り: 「呼ぶを全員に許可」OFF で権限者以外が呼べないこと・2 人の予約の取り合い (B27)、Quest/iOS (VER-WLD-001)。斜面の項目は IMP-DOG-032 の確認 (A0-26・C20) で見る。法線 0.9 の判定の撤回は 8faf572 で実装済み (2026-10-11 にコードで確認)。PCVR 1 人 (A87〜A90) は 10-02 ユーザー総合判断で確認済み"
updated: 2026-10-11
---

# IMP-DOG-016 呼ぶ・ポーズを移植し、リード除外と斜面判定を足す

2026-09-23 起票。[DES-UI-003](DES-UI-003-menu-scope-and-polish.md) で **呼ぶ・ポーズは既存の種類・操作を保ち、リードを外して使う**と決まった。古民家の場所決めには **接地・傾斜・法線・NavMesh の判定が一切なく、`ValidPlacement` は NaN / Inf チェックだけ** (`KominkaDogRun: Assets/KominkaDogRun/Scripts/KD_SleepSlot.cs:277`)。平地の庭では成立していたが、裏山は斜面なのでここに判定を足す。

実装順は [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md) → [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md) → **本Issue** ([IMP-DOG-014](IMP-DOG-014-treats-port.md) / [IMP-DOG-015](IMP-DOG-015-accessories-and-petfx-port.md) と並行可)。**接続競合と斜面の契約は [DES-DOG-001](DES-DOG-001-leash-walking.md)**、UI 頁は IMP-UI-001 が持つ。

## 範囲

- `KD_SleepSlot` / `KD_SleepBoard` / `KD_SleepPlacement` / `KD_CallPose` と `KD_DogWanderController.Sleep` を裏山へ配線する。
- 継承するもの: 犬 → ポーズ → 場所 の流れ、5 ポーズ (ごろん / ふせ / おすわり / うずくまる / へそ天) + あご乗せ・お手・到着後の伸び+あくび、予約枠 (3 秒更新・15 秒途絶で解放・先着 1 人)、終了 4 条件 (自由にする / 権限者の取り上げ / 呼んだ人の退室 / Ownership 移譲)、寝ている犬を おもちゃ・おやつ・Fetch の候補から外す扱い。
- **4 動作 (へそ天 / あご乗せ / お手 / 伸び+あくび) 各 30pt の課金ゲート**を `ActionOwnedBy` で継承する。価格表は [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md)。
- 「呼ぶを全員に許可」を設定面へ置く (初期 ON、権限者 = ワールド作者・不在時 Master、権限者以外は操作不可)。初期 ON は 2026-09-23 のユーザー決定 (古民家の最近の変更に合わせる)。権限者は継承。
- **変更 1 — リード除外**: 自分・他人を問わずリード接続中の犬を対象外にする。行を暗色にし、理由を文言で出す (「リード中。さんぽで外してから」)。逆に、**呼ばれて待機中の犬はさんぽタブで接続不可**にする (暗色 + 理由)。
- **変更 2 — 場所の検証**: マーカーを真下へレイキャストして地面へ接地させ、**地面法線 ≥ 0.9 (約 25°、仮値) のみ有効**とする。無効時はマーカーを赤くして確定不可にし、色だけでなく文言「この姿勢には平らな場所が必要です」も出す。集合の判定より厳しくする理由は、ふせ・ごろん・へそ天が体を地面に密着させるため。数値は実測して確定する。
- **変更 3 — 犬の移動**: 古民家は水平距離の StepToward + Y の MoveTowards で接地スナップをしない。裏山では散歩と同じ地形追従に合わせる ([DES-DOG-001](DES-DOG-001-leash-walking.md) の「隊形と斜面」と同じ扱い)。
- 呼ぶタブの頁 (犬 6 → ポーズ 5 → 場所) と端末別の配置操作 (VR は手の前 + 板の ◀▶、Desktop は距離ホイール + Q/E、Touch は [ここへ呼ぶ]) は [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md)。
- UI 頁は古民家の `KD_TreatPouch` の partial `.Sleep` + `KD_CallPose` のポーズ面をそのまま使い、ビルダーは [計画書](../../plans/2026-09-23-menu-rebuild.md) 段階 0 で移植済みのものに配線を足す (新しい頁を自作しない)。ポーズ面のミニチュアは `KD_CallPoseEditor` を段階 2 で焼く。

## やらないこと

「離れたら解除」の復活 (`releaseDistance` / `arriveTimeout` は古民家でも定義のみで参照ゼロ。挙動を変えない)、接続したままポーズを取り終了時に散歩を再開する案 (保留を継続)、新しいポーズの追加、写真・ライブとの連動、Quest / iOS の合格判定。

## 完了条件

- [x] コンパイル 0 件で、呼ぶ・ポーズ・場所決めのシーン配線を Verify で確認した (2026-09-24: C# 0 / U# 82 本・エラー 0。Menu/6b Verify Sleep System (斜面の定数と法線値を出力)、20 (段階表 2)、21 (404/0)、22、25 (1661/0) 全 PASS、Walking/5 209/0)
- [x] 犬 → ポーズ → 場所 の流れが 6 匹すべてで平地で成立する (予約・配置・到着・終了) — **実装済み・確認待ち**
- [x] リード接続中の犬が暗色 + 理由で除外され、外せば呼べる。呼ばれて待機中の犬はさんぽで接続できない (T-010) — **実装済み・確認待ち** (集合中の犬も除外。同時に乗ったらリードが勝つ)
- [ ] 斜面で赤マーカーと文言が出て確定できず、平地では成立する。**採用した法線値を記録した** — **法線 0.9 を仮採用して実測を記録済み (下の「斜面の判定」)。赤マーカーと確定不可は実装済み・確認待ち**
- [x] 4 動作が所持で解放され、未所持では価格が出る — **実装済み・確認待ち** (`KD_ShopItem_Action_*` の 4 行を配線)
- [ ] 「呼ぶを全員に許可」を OFF にすると権限者以外が呼べない。寝ている犬は OFF にしても起きない — **実装済み・確認待ち** (許可の既定 ON は段階 3 の設定面)
- [ ] 2 人で同じ犬を選んで予約の取り合いになり、先着が保たれる — **実装済み・確認待ち**
- [x] PCVR でユーザーが斜面の見え方と配置操作を確認した。**Quest / iOS の合格判定は [VER-WLD-001](VER-WLD-001-quest-ios-devices.md)**

## 進捗

**2026-09-24 実装済み・Verify 全 PASS (Play 未実施)。** 実行順・Verify の数値・逸脱は [計画書「実装記録 / 段階 2」](../../plans/2026-09-23-menu-rebuild.md#段階-2-2026-09-24)、ファイルごとの差分は [kominka-dog-system「段階 2」](../imports/kominka-dog-system.md#2026-09-24-おやつかざり撫でfx呼ぶ-段階-2)。Codex D+M+U は実行中。コミットは未。

2026-09-25 撤回: コミット済み c27a7e3 (Codex D+M+U 済み)。

- 器: `KH_Dogs/KD_SleepBoard` + `KD_SleepSlot` ×6 (Menu/6)、`KH_Dogs/KD_SleepPlacement` (Menu/6i)、`KH_Dogs/KD_CallPose` (Menu/6j。6 匹 × 7 ポーズのミニチュアを `Models/DogPreviews` へ焼く。焼くときは首輪を隠す)。6 は 6 匹の wander / fetch、`KD_LiveAccess`、`pouch.sleepBoard`、`KH_LeashManager.sleepBoard` も配線する。予約枠・終了 4 条件・権限者は古民家のまま。
- **変更 1 (リード除外)**: `KD_SleepBoard` に読むだけの API (`_IsCalled` / `_IsLeashedDog` / `_IsGatheringDog` / `_IsWalkBusy`)。予約・確定・`_BeginSelection` はリード中・集合中の犬を拒否する。呼ぶタブの行は `SleepLeashed` (「リード中。さんぽで外してから」) / `SleepGathering` の状態で暗くする。`KD_CallPose` はリード中・集合中は開かず、開いている間にリードが付いたら操作を止めて理由を出す。逆方向は `KH_LeashManager` が呼ばれた犬への `ReqAttach` を拒否し、さんぽタブに理由 (`WordWalkGateCalled`) を出す。呼ぶとリードが同時に同じ犬に乗ったら、リードが勝って呼ぶを解放する。**集合中の犬の除外は契約外の追加** ([DES-DOG-001](DES-DOG-001-leash-walking.md) Q2 と揃えた)。
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

測った範囲 (拠点のまわりと散歩道) ではすべて置ける。限界付近で犬が沈む・浮くかは PCVR で見てから確定する ([計画書の実機チェックリスト](../../plans/2026-09-23-menu-rebuild.md#3-段階-2-おやつかざり撫でfx呼ぶ2026-09-24-追加) の「呼ぶ」)。

## 関連

- [DES-UI-003](DES-UI-003-menu-scope-and-polish.md) 順番 3: 継承する範囲と変更 1〜3、保留にした 2 件
- [DES-DOG-001](DES-DOG-001-leash-walking.md): 接続競合・隊形と斜面の契約。集合の配置判定と数値を揃える
- [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md): 追加動作 4 種 各 30pt の価格表と所持判定
- [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md): 呼ぶタブの頁と端末別の配置操作
- [world-ui-spec](../../world-ui-spec.md) T-010: リード中の除外と斜面・平地の検証条件

2026-10-02 ユーザー確認: PCVR 1 人の項目は「基本全部見たから OK」(総合判断。項目別の結果の記録なし。[play-checklist-2026-10-02](../../testing/play-checklist-2026-10-02.md) の A 節)。残り: 2 人 (権限者以外の呼び出し・予約の取り合い)、法線 0.9 を採用するかの決定、Quest/iOS。

決定: 2026-10-03 (ユーザー回答) 「斜面の法線 0.9 の採用。→ OK」。地面法線 ≥ 0.9 を採用値とする (A89)。ただし [IMP-DOG-032](IMP-DOG-032-call-placement-free-height.md) (初期公開に入れる決定、同日) は変更 2 を古民家どおりに戻す内容で、実装すると 0.9 の判定が外れる。順序・優先はユーザーに確認が要る。完了条件の斜面の確認 (赤マーカー・確定不可) は引き続き未確認。

## どちらが勝つかは未回答 (2026-10-03)

本 Issue の変更 2 (場所の検証、法線 0.9 の判定と接地) と [IMP-DOG-032](IMP-DOG-032-call-placement-free-height.md) (その判定を外して古民家どおり手の高さに追従) のどちらを優先するかは**未回答**。推奨は **032**。next の「法線 0.9 は採用 (10-03 決定)」は 032 と食い違ったまま残っている。回答を受けて、負けた側の記述を直す。設計案は [傾けて置く](../../plans/2026-10-03-call-placement-tilt.md) §6・§7 の質問 1。

## 撤回: 2026-10-03 (斜面の法線 0.9 の判定)

撤回: 2026-10-03 (ユーザー回答「推奨で」) 上の「決定: 2026-10-03 (ユーザー回答) 斜面の法線 0.9 の採用」は、同日の質問「IMP-DOG-032 と IMP-DOG-016 のどちらが勝つか」への回答で撤回された。理由: 推奨は 032 で、ユーザーが 2026-09-30 に呼ぶ機能について決めた判断 (古民家と同じにする。置けない場所は作らない。斜面は自分の上に乗せるか傾けて置けば済む) を優先するため。変更 2 (法線 0.9 の判定と赤マーカー・確定不可) と変更 3 (寝床に収まる途中の接地) の該当部分は [IMP-DOG-032](IMP-DOG-032-call-placement-free-height.md) で古民家どおりへ戻す。上の「どちらが勝つかは未回答」の節は回答済み。本 Issue のほかの変更 (リード接続中の除外・ポーズの移植) は維持する。完了条件の斜面の項目は、032 の実装後に「斜面でも置ける (古民家どおり)」へ読み替える (未実施)。

## 2026-10-03 (`8faf572`) 撤回の実装

撤回した変更 2 (法線 0.9 の判定・赤マーク・確定の抑止) と変更 3 のうち寝床へ収まる途中の接地は、[IMP-DOG-032](IMP-DOG-032-call-placement-free-height.md) として本番へ実装した (ValidPlacement は NaN / Inf だけ。シーンの赤マークは Menu/6i が削除。Menu/6b・6d を検証、Menu/6i ビルド済み)。変更 1 (リード接続中・集合中の拒否と Watch)・ポーズの移植・近づく足 (SleepStep) は維持した。チェックリストの A89 は「斜面・手の高さに置ける」へ読み替え済み (A0-26)。Play・PCVR での確認は 032 の項目で行う。

## 2026-10-11 撤回の実装をコードで確認

next に「変更 2・3 の該当部分は古民家どおりへ戻す」が残っていたが、戻す作業は 8faf572 (2026-10-03) で済んでいる。現在のコード (HEAD) で確かめた。

- `KD_SleepSlot.cs:280-286`: `ValidPlacement` は NaN / Inf だけを見る (古民家どおり)。`HillsideMinGroundNormal` (0.9)・`HillsideGroundMask`・`_PlacementGroundOk` は 8faf572 で削除され、今は無い。
- `KD_SleepPlacement.cs`: 赤い印 (`invalidMarks`)・地面へのレイ・確定の抑止 (`_groundOk`) は 8faf572 で削除。マーカーは手 / 頭の前にそのまま出る。HEAD のシーンにも `invalidMarks` は無い。
- `KD_DogWanderController.Sleep.cs:206`: 置いた点へ収まる途中の `SnapToGround` (変更 3 の該当部分) は外した。近づく足 (`SleepStep` = `GatherStep`) は残す。
- `KD_SleepSetupEditor.cs:461-462`: Menu/6b の Verify が、斜面判定が残っていないことを検査している。
- 残っているのは文言キー `KD_TreatPouch.WordSleepNeedFlat` だけ (表に残るが使わない。`KD_SleepSetupEditor.cs:449` の注記)。

Play・PCVR での確認は未実施のまま ([IMP-DOG-032](IMP-DOG-032-call-placement-free-height.md) の A0-26・C20)。
