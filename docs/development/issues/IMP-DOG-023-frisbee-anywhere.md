---
id: IMP-DOG-023
type: IMP
system: DOG
status: review
priority: Medium
summary: フリスビーをどこでも (DES-DOG-005 段階 3)。M1 陸の地面基準ジャンプと到達判定、M2 川のディスクの浮き、M3 岸からのドボンと水中回収、M4 取り出しの門の置き換え
next: "AI 側の検証は M1〜M4 とも済み。残りは 2 人の実機 (設計書 #44〜#47、B31) と VER-WLD-001 項目 12。PCVR 1 人の 37〜43・48 (跳び込み 40/41 を含む。A78・A79・A113・A115・A116) は 10-02 ユーザー総合判断で確認済み"
updated: 2026-10-02
---

# IMP-DOG-023 フリスビーをどこでも (段階 3)

2026-09-28 起票。[DES-DOG-005](DES-DOG-005-frisbee-anywhere.md) の段階 3。段階 1 は [IMP-DOG-022](IMP-DOG-022-frisbee-takeout-area.md) で先に入れた。段階 2 (平地の一覧) は行わない (ユーザー決定 2026-09-28)。

何をどう作るかの正本は [設計書 2026-09-28](../../plans/2026-09-28-frisbee-anywhere.md) (Codex レビュー 21 件を反映済み)。数値・式・状態遷移・レイの予算・検証ケースは設計書を見る。本 Issue は進行と結果だけを持つ。

## 前提 (設計書 §A・§E)

- 犬は水中からは跳ばない。岸から踏み切って川に着地する (ドボン) のは可。ドボン後は §E-3 の順 (投げた人の側 → 反対側の最寄り → 入った岸) で安全な岸へ上がり持ち帰る。
- 川に落ちたディスクは浮き、犬はボール (IMP-DOG-021) と同じ規則で水中へ取りに行く。
- 跳ぶのは傾き ≤ 15° かつ高低差 ≤ 0.5 m のときだけ。それ以外は走って拾う。橋の床では跳ばない。崖・木立・4 m 超の段差の先は走り込みも断り、照準に ×。
- 同期変数は増やさない。所有権の流れ (投げた人 → 犬の Owner が計画・移動、`ClaimDisc` / `ReleaseDisc`) は変えない。
- 犬種別のキャッチ表は地面相対なので取り直さない。

## 順序と完了条件 (設計書 §G・§H)

各 M は、コンパイル・Console エラー 0・該当ハーネス PASS・Codex レビュー (M2・M3 は必須) の後にコミットする。Unity のメニュー実行・保存は統括の担当。

### M1. 陸 (地面基準のジャンプ・到達判定) — 2026-09-28 完了

- [x] `JumpGroundOk` (4 点 C0/T0/P0/L0、有界反復、残差 > 0.10 m で棄却)、非対称の弧、`animSpeed` の頂点での書き直し、`NoGround`
- [x] `PlayableAt` / `PathOk` / 斜面に沿う `LaneClear` / `Reachable`。`CanPlay` / `DogMayGo` の置き換えとレイの予算 (`_planRays` / `_solveRuns`)
- [x] 川は踏み切りも着地も拒否 (M3 まで)。`waterPose != 0` の犬は投げを受けず出水を頼む (今ある穴 0-5 を塞ぐ)
- [x] 輪の非表示・× と「犬はここへ行けません」。`playAreaRadius` は M4 まで据え置き (段階 1 の門が使う)
- [x] Course/92 Build・93 Verify、Walking/5・Frisbee/5 の円の検査の置き換え、ハーネス `--mode=terrain` (T-*)。既定 26 投げは変更なしで PASS

### M2. 川のディスクの浮き

- [x] `KH_Frisbee` の着水と浮き (`EndFlight` の岸戻し分岐の変更を含む)、漂流の共有 (`_DriftVelocity`。場内の停止規則と `floatDrift` を保つ)、解放後の復旧、岸戻しの残る所 (§D-6)、Course/89 の追加検査 — 2026-09-28 実装 (コンパイル・ハーネス未)
- [x] Course/94 (M2 の 8 ケース) が `FRISBEE_M2_PASS` — 2026-09-28 (run 082637、下の検証の記録)
- [x] 88 / 78 / 89 / W5 (75) が PASS のまま (ボール側の回帰) — 2026-09-28。88b のポメは swimSink の件で基準を 0.010 に緩和 (ユーザー決定、c88b893)
- [x] Codex レビュー (同期・所有権) — 2026-09-28。観点 1・3・4・6 は問題なし。高 1 (Retrieve 中に浮く水へ移ったディスク) と中 3 (非 Owner の重力操作、ハーネスの凍結合格と水遊び場の除外、M2 の実機項目) を反映 (再実行は未)

M2 でやったこと (2026-09-28):
- `KH_Frisbee`: 浮く判定 `FloatKind` (水遊び場の `_FloatsAt` → 川の `_FloatsAt`)。飛行中に水面より下へ入り浮く所なら水面で `EndLanded` (理由は増やさない)。`EndFlight` は浮く所なら `BankFor` を通さず浮かせ、それ以外の帯は今の岸戻し。`CheckRiver` (Owner、0.25 s) は kinematic の放置を外す → 浮かせる → 浮かない所で重力を戻す → 帯なら岸戻し、の順。浮かせ方はボールと同じ (重力 OFF・`水面 − radius·floatSink` を速度で保つ・角速度 0・水平)。重力は `OnPickup`・`_Caught`・`_Recall` (メニューの呼び寄せと駐車)・陸の `EndFlight`・見回りで戻す。`_Release` は放した位置で決める。同期変数は増やしていない。
- `KH_RiverData._DriftVelocity` を足し、`KH_ToyRecall.FloatBall` をそれに切り替えた (式はそのまま移した純粋な切り出し)。
- `KH_FrisbeeCatch._Reachable`: 浮いている水は M2 では到達不可 (水遊び場の水も。水中の追跡は M3)。`GroundYAt` の水面は M3。どちらもコードに TODO(M3)。
- Course/89 に §D-6 の静的検査 (水の中は浮く、橋の帯・岸の無い区間・帯の縁は岸戻し、水遊び場の水は浮く、`_DriftVelocity` の場内規則)。出力は `FRISBEE_RIVER_OK` のまま (行に float の件数を足した)。
- Course/94 `Walk Harness Frisbee Anywhere (M2 float)` を新設 (`KH_CourseWalkHarnessEditor.Frisbee.cs`)。
- Codex M2 レビューの反映: `UpdateRetrieve` (0.25 s ごと) と `UpdatePickup` (Return の直前) で浮く水のディスクを `cannot-play` で断る。`_Caught` / `_Release` の重力は Owner のときだけ、後から届いた所有権で重力を書き直す。Course/94 に F5d-roll を足し、追跡中の水遊び場への侵入と、岸戻し後の非 kinematic・重力ありを必須にした。設計書 PCVR に 46〜48 (M2 の途中参加・Owner 退出・陸で放す) を足した。

どこをずらしたか: 設計書 §D-2 と §G M2 の節末の「決定: 2026-09-28 M2 実装で変更」 (水遊び場の水も到達不可、`_Release` は位置で重力、`GroundYAt` は M3、Course/94 は M2 のケースだけ)。

### M3. ドボンと水中回収

- [x] 着地の帯を §D-3 の条件で許す。状態 11〜13 (`KH_FrisbeeCatch.Water.cs`)、Wander の足を借りる、非 Owner の表示経路、`ClaimDisc` の取得待ちと喪失処理、Wander の `_speed = 0` の例外、`_GateToy` の門 (§D-5) — 2026-09-28 実装 (コンパイル・ハーネス未)
- [x] Course/95 (計画プローブ、継続判定込み) PLAN_PROBE_OK 15/15、Course/94c (F2・F3) PASS — 2026-09-28 (ポメは 1 試行目、ラブは 2 試行目)
- [x] Course/94b (F2・F3・F4・F5・F11・F12・F13・F14・F14b・F2m-miss-float・F2m-miss-land) が `FRISBEE_M3_PASS` (F4 は川のデータに該当が無く SKIP。F13 は場所を探索、無ければ SKIP) — 2026-09-28 10/11 (F4 N/A)、c88b893
- [x] 回帰: 試作既定 26/26・terrain 10/10、Course/94 `FRISBEE_M2_PASS` 8/8、88a / 88b / 78 / 75 / 89 (ボールの計画器の移設は純粋な切り出し) — 2026-09-28 すべて PASS (88a 20・78 14・75 3・89 OK、c88b893)
- [x] Codex レビュー (同期・所有権) — 2026-09-28。観点 2・3・5・6 は問題なし。高 3 (12 の取得未確認のまま 13 へ進む、11 の取得待ちで EndGrabbed を見ない、近くの陸へ出たディスクで泳ぐ犬を川床へ落とす)・中 4 (水遊び場のドボンに岸の条件、F14 の分割、外したドボンの検証、F12 の門の値)・低 2 を反映 (再実行は未)

M3 でやったこと (2026-09-28):
- `KH_FrisbeeCatch.Water.cs` (新規 partial): ドボンの判定 `DobonAt` (D-1 の「浮く」・`_FetchWater`・泳がない犬は 0.85 M と沈み止め)、着地で直接 12 / 11 へ (`EnterWaterFromJump`)、11 WaterChase (入口の輪切り → 川の足、口が届いたら所有権を待つ・2 s で `claim-failed`)、12 WaterCarry (出口は投げた人の側から)、13 WaterExit (1.2 s → NavMesh で投げた人の 3 m まで → Return、対岸・経路無しは復帰点で Drop)、保持の見張り (`hold-lost`)、帯での 3 s 待ち (`RiverBlockWait`)。
- Planner: 着地点だけ `JumpLand` (水への着地を許す。踏み切りは今のまま陸だけ)、水への落差は最大 1.0 m、着地の lane は水面で判定、`_Reachable` の水の行 (浮く所は泳ぐ犬なら可、泳がない犬は 0.85 M まで)。
- Motion / 本体: Chase・Land・Retrieve・Pickup の「浮いている」分岐を 11 へ (M2 の `cannot-play` は切り替えで残す)、水中では Owner も速さを観測し `Animator.speed` だけ書く (Walk / Run は Wander の足)、`IsCarrying` に 12・13、11 は手で取れる、GiveUp は所有権を失ったディスクに触れない。
- `KH_RiverData`: `_EntrySlice` / `_ExitSlice` / `_NavPathLength` / `_LastEntryPath` / `_OtherBank` を足し、`KD_DogFetchController.HillsideWater` の入口・出口・経路長をそれに切り替えた (式はそのまま)。
- `KD_DogWanderController`: 忙しい分岐の `_speed = 0` と `_WaterPushBlocked` の水中の例外に `frisbeeCatch.IsWaterBusy`、`_WaterBeginAt` / `_WaterSwimRootY` / `_WaterLandY` / `_WaterWalkMouth` を公開 (`_` 付き、ローカル)。
- `KH_ToyRecall._GateToy`: 12・13 は `InWater`、11 は `DogBusy` にしない (§D-5)。
- ハーネス: Course/94 (M2) は犬 0 のヒープ `_debugWaterFetchOff` で M2 の規則のまま。M3 は Course/94b。

どこをずらしたか: 設計書 §D-5b と §G M3・§H の節末の「決定: 2026-09-28 M3 実装で変更」。

M3 の検証で直したこと (2026-09-28、Play の前に静的に詰める方針に切り替えた後):
- 94b の最初の走行でドボンが 1 度も起きなかった。机上解析と計画プローブ (Course/95、Edit モードで本物の計画器の最初の Plan と継続の Plan を再現) で、原因を「犬が帯の縁から 4.8 m 後ろで時間不足」と特定した。ドボンの狙いは Play 前に探索する形にした (本物の計画器が水への跳躍と答え、最初の Plan の遅れ・向きのずれ ±25° でも崩れず、投げる位置が受理されるものだけ。無ければ BLOCKED で Play しない)。F2・F3 だけの Course/94c を足した。
- ラブの水への跳躍が計画されなかった: 帯のふるいが水の上で河床を地面にしていた → `GroundYAt` は水中の状態が有効なとき川の水域で水面を地面にする (設計 §D-2 の最後の行)。
- 94c の 1 回目: 採用後の `KeepPlan` の fit が踏み切り速度を下げて T0 を 0.5〜0.64 m 水側の急な岸へ動かし、離陸で `slope` / 再計画で `drop=ground`。**決定: 2026-09-28 (Codex 相談) 水着地の計画は検証済みの踏み切り点を固定し速度側で合わせる** (設計 §D-3。案 (a)、水着地の B / C だけ。陸の計画は不変)。しゃがみは T0 へのベクトルと残り時間で寄せ、最初の空中フレームで T0 に計画時の高さのまま置く。補正不能 (通過・速さ > 8 m/s・差 > 3.5 m/s) は救済せず再計画。
- 94c の 2 回目: ラブの 1 本目は投げる位置の地面で拒否 (狙い探索に `p0Playable` を追加)、2 本目は T0 の高さの取り直し (0.068 m) で着地の判定が対岸の水際を跨いだ (高さも固定)。直した後の 94c で PASS (ラブは 2 試行目)。
- Codex M3b 最終レビューの反映: 残り時間が短くてもしゃがみ補正の速さ・速さの差を必ず検査、12 / 13 の取り消しは所有権の無いディスクに触れない (owner-change の復旧は除く)、`_fxOn` を計画を離れるすべての経路で解除、F13 の場所を川のデータから探索 (無ければ SKIP)。

### M4. 段階 1 の門の置き換えと文書

- [x] `OutOfArea` の門を足もとの判定 (`KH_FrisbeeCatch._TakeoutOkAt`) に。0.5 s に 1 回だけ撃ち、6 行で使い回す。`discTakeoutMargin` 削除 — 2026-09-28 実装 (コンパイル・ハーネス未)
- [x] `playAreaRadius` を 0 に (Course/92 が書く、Course/93・Walking/5 が 0 を検査)。`HasArea` / `InArea` / `BuildRing` / `areaRing`、`InCircleXZ`、`playAreaMargin` の削除。輪の GameObject は Course/92 (本シーン) と Frisbee/4 (試作) が消す
- [x] 文言 2 キー (13 言語): `course.toy.discArea`「川や急な斜面を離れると取り出せます」、`course.toy.discAreaState`「ここでは不可」
- [x] 置き換わる決定の記録 (DES-DOG-002 / IMP-DOG-012 / IMP-DOG-020 段階 7 / IMP-DOG-021 §1 / IMP-DOG-022 / 試作記録 P2・D1・輪)、ハーネス文書、game-design、[VER-WLD-001](VER-WLD-001-quest-ios-devices.md) 項目 12
- [x] コンパイル (C#・U#)、Course/92 → 93、Walking/5、Frisbee/5、翻訳の入れ直し (Course/85 → 27 → 81) — 2026-09-28 (0440455。Walking/5 の M4 項目 6 件 PASS)
- [x] Course/95 の F15-gate 行が期待どおり、Course/94 が `FRISBEE_M2_PASS` 9/9 (F15-gate を含む) — 2026-09-28 Course/94 9/9 (F15 4/4)、88b 11/11 (0440455)。Course/95 の gate 行の結果はコミットに記録が無い

M4 でやったこと (2026-09-28):
- 門の規則 (設計 F を具体化): 取り出せるのは、ローカルのプレイヤーの足もとで犬が遊び始められるとき。(1) 川の帯 (半幅 + 1 m。橋の床は帯ではない) と水遊び場の多角形の外 (`InJumpWater`。D-1 の「水の中からは跳ばない」と同じ範囲。`PlayableAt` の水遊び場の例外は歩く・立つためのもので、遊びの始点にはしない)、(2) 足もと + 0.5 m から 4 m 以内に地面 (layer 0 / 11)、(3) 傾き ≤ `runMaxSlopeDeg` 25°。レイ 1 本、`_planRays` に数えず `_playY` も書かない。6 匹の値は同じなので、最初に聞いた行の捕り手で判定し 0.5 s 使い回す (≤ 2 本/s)。優先順は Unwired → Pending → OutOfArea → Held → 12・13 は InWater / 11 は通す / 他の係りは DogBusy → BallInUse → Cooldown のまま。
- 場所ごとの期待:

| 場所 | 門 | 理由 |
|---|---|---|
| 道・広場 | 取り出せる | 地面あり・緩い (Course/93 で周回路の 99.5% が到達可) |
| 川の中 (帯、岸から 1 m を含む) | `OutOfArea` | 水から遊びを始めない (D-1) |
| 水遊び場 (水も、多角形の中の岸も) | `OutOfArea` | 同上。M1 の歩く例外は門には使わない |
| 橋の床 | 取り出せる | 床は帯ではない。犬は床を走って拾える (跳ばないだけ) |
| 崖の上の平らな所・崖際の平らな所 | 取り出せる | 局所判定。犬が行けない投げは照準の × と一言で断る |
| 25° を超える斜面 (崖の斜面) | `OutOfArea` | `runMaxSlopeDeg` |
| 木立 | 地面が 25° 以下なら取り出せる | 木は門では見ない (照準の lane で断る) |
| 足もとの 0.5 m 上から下へ 4 m (足もとより 3.5 m 下まで) に地面が無い所 | `OutOfArea` | 地面なし |

- ハーネス: Course/94 に F15-gate (道 / 川の中 / 橋の床 / 32〜42° の急斜面の 4 か所。Edit モードで探して `Temp/frisbee-gate-sites.json` に書き、Play は読むだけ)。道と川は必須 (見つからない・プレイヤーが留まらない = FAIL)。橋・急斜面は N/A を理由付きで数える (Codex M4 レビュー 中)。読み出しはハーネス専用の `KH_ToyRecall._HsGateDisc` (ローカル、`_` 付き)。Course/95 は同じ探索を流し、`_TakeoutOkAt` (proxy) と期待を照合する。詳細は[ハーネス文書](../../testing/kh-frisbee-harness.md#本シーンの川のディスク-course94-des-dog-005-m2)。

どこをずらしたか: 設計書 §F と §G M4 の節末の「決定: 2026-09-28 M4 実装で変更」 (水遊び場も断る、文言、`playAreaCenter` はハーネスの広場の目印として残す、`playAreaMargin` も削除)。

## 検証の記録

- ベースライン (M1 前、コミット fc8e584): フリスビーハーネス既定モード 26/26 PASS (run 20260928-134052)。
- M1 (2026-09-28、Codex レビュー 9 件反映後): コンパイル C#/U# 0。Frisbee/4 (犬種 6 レーン + Terrain 10 パッド)、Frisbee/5 failures=0。ハーネス 既定 Lab 26/26 (run 144852)・Pome 26/26 (155148)・netorder 10/10 (145559)・sixdogs 14/14、solveRayMax ≤ 200 (155713)。terrain 10/10 (162232。配置を 3 度直した: T-up10 は仰角 15°、T-down04/up04 は縁を z +4.4 に、T-up04 は投げ台を 0.4 m の台へ、T-rock は岩を犬の 0.5 m 先へ、T-cliff は円盤が止まってから照準を判定。いずれも規則側ではなく配置・判定時点の不備)。本シーン: Course/92 BUILD_OK (6 匹に値、輪を非表示、一言)、Course/93 OK (周回路 4344 点: 到達 99.5%・跳べる 96.4%・noGround 0・帯 18 点は岸戻し)、Course/89 OK、W5 (75) PASS。Walking/5 は TOTAL 498 / FAILED 21 で、M1 の項目 (asymmetric landing・DES-DOG-005 values・play area) は 6 匹とも PASS。**21 件の FAIL は M1 前からのもの** (route 配列 n=436・門↔広場の nextHop・ディスクが広場の平坦部にある・拠点の高さ。09-27 の川沿い周回コース採用後、旧 Terrain Prototype の設定値 `KH_TerrainPrototypeConfig` と照合する検査が古くなっている。別件で検査を周回版に合わせる)。

- M1 の実装のずれ (上り 0.5 / 下り 1.0 m、帯の拡張条件、SolveRayCap の集計範囲、Ready のレイ範囲、Return の Drop、水遊び場は走り込み可・跳躍不可) は設計書 §B-6・§C-3b に「決定: 2026-09-28 M1 実装で変更」として記録。Codex の M1 レビュー 9 件 (高 3) はすべて反映。

- M2 (2026-09-28、Codex レビュー 4 件反映後): コンパイル C#/U# 0。Course/89 FRISBEE_RIVER_OK (float: water=24 / bridge=8 / edge=6 / noBank=0 / spot=True)。Course/94 FRISBEE_M2_PASS 8/8 (run 082637: F5a-river・F5b-spot・F5c-grab・F5c-recall・F5d-roll・F6-bridge・F7-nobank・F7b-edge)。ボール側の回帰: 78 WATER_PASS 14、75 W5_PASS、試作既定 26/26 (171911)。**88b は RW-down-Pome / RW-up-Pome が FAIL だが M2 の差分が原因ではない**: c4fd29a (ポメの swimSink 0.035 m、PCVR 決定) で泳ぐ口の高さが 0.086 → 0.051 m に下がり、ハーネスの口の余裕 0.03 m の基準を川の流路で割る (mouthLow)。c4fd29a 以降の最初の 88b (075505) から一貫して FAIL、時間・漂流・入水点は合格運転と同じ。PCVR の決定を変える (swimSink / swimPitch) かハーネス基準を下げるかはユーザー判断 → IMP-DOG-020 / 021 の範囲で別件。

- M3 (2026-09-28 夕、中断時点・**未コミット**): Codex レビュー 9 件 (高 3) を反映済み。修正前コードの部分走行: 試作既定 26/26 (180823)、terrain 9/10 (181350。T-up10 の stand は Play 中の編集によるフレーム停止で最初の計画が 0.23 s 遅れたため。修正版で再確認)、89 OK、94 M2 8/8 (100037)。**94b (100355): F2-dobon-Pome / F3-dobon-Lab が 6 試行すべて RETRY で「ドボンの捕りが 1 度も成立しない」** (設計書 I のリスクどおり。狙いの配置か計画器の点数の見直しが要る)。同 run で FMOD の出力デバイス初期化エラーも出た (環境)。88a (101543): W3-Dachs FAIL (t=24.3、carryMouthLow=1) → M3 の Fetch の切り出し (`_EntrySlice` / `_ExitSlice`) の回帰か、swimSink の件の Dachs 版かは未切り分け。

- M3・M4 (2026-09-28、コミット c88b893 / 0440455 のメッセージから転記): 上のチェック欄のとおり。M3 は 95 15/15、94c PASS、94b 10/11 (F4 N/A)、94 8/8、試作 26/26・terrain 10/10、88a 20・78 14・75 3、89・93 OK。M4 は 94 9/9 (F15 4/4)、88b 11/11、92 / 93 / Frisbee/5 / 81 OK、Walking/5 の M4 項目 6 件 PASS。

- 実機 (2026-09-28 夜、ユーザーの PC VR ログ `output_log_2026-09-28_22-33-09.txt`、［TEST］ 古民家の裏山、最大 5 人・延べ 7 人、84 分): ビルドは M3 以降を確実に含む (`water chase entry=` など。M4 は実行時ログが無いので判別できず、0440455 の可能性が高い)。投げ 170 (7 人全員)、捕球 74 (空中 69・立ち 4・追い越し 1)。**liftoff 69 はすべて catch OK、`landGap` は全件 0.000**。etaErr の絶対値の平均 0.12〜0.22、Udon の例外 0。水では、浮き (着水) 6、water chase 8・入水 7・水中運搬 5・出口の計画 6。**ドボンは 0 件で、M3 の岸からの跳び込みは未確認 (PCVR 40/41 が残る)**。水中追跡の 7 回はすべて着水後に歩いて入る経路だった。ほかに `hold-lost` 89 (86 件が state 9 Drop、口から取られた。[IMP-DOG-013](IMP-DOG-013-frisbee-pet-after-return.md) の検討候補)、`cannot-play` 取消 6・拒否 4、`in-water` 拒否 10。別件で [BUG-DOG-006](BUG-DOG-006-steal-held-pickup.md) (steal ownership 15 件) と [BUG-DOG-007](BUG-DOG-007-shiba-throw-implausible.md) (柴の implausible 5 件) を起票した。

## 関連

- [DES-DOG-005](DES-DOG-005-frisbee-anywhere.md) / [設計書](../../plans/2026-09-28-frisbee-anywhere.md)
- [IMP-DOG-022](IMP-DOG-022-frisbee-takeout-area.md): 段階 1
- [IMP-DOG-017](IMP-DOG-017-frisbee-intercept-solver.md) / [IMP-DOG-021](IMP-DOG-021-river-wide-fetch-and-recall.md): 置き換える計画器と、流用する川の回収
- [ハーネス](../../testing/kh-frisbee-harness.md)

2026-10-02 ユーザー確認: PCVR 1 人の項目は「基本全部見たから OK」(総合判断。項目別の結果の記録なし。[play-checklist-2026-10-02](../../testing/play-checklist-2026-10-02.md) の A 節)。残り: 2 人 (#44〜#47)、VER-WLD-001 項目 12。
