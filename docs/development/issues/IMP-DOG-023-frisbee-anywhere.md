---
id: IMP-DOG-023
type: IMP
system: DOG
status: wip
priority: Medium
summary: フリスビーをどこでも (DES-DOG-005 段階 3)。M1 陸の地面基準ジャンプと到達判定、M2 川のディスクの浮き、M3 岸からのドボンと水中回収、M4 取り出しの門の置き換え
next: M2 実装済み (コンパイル・Course/89・Course/94・88/78/75 の回帰・Codex レビューが残り)。各 M の終わりにコンパイル → ハーネス → Codex レビュー (M2・M3 は必須) → コミット。PCVR 37〜45 はユーザー
updated: 2026-09-28
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
- [ ] Course/94 (M2 の 8 ケース) が `FRISBEE_M2_PASS`
- [ ] 88 / 78 / 89 / W5 (75) が PASS のまま (ボール側の回帰)
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

- [ ] 着地の帯を §D-3 の条件で許す。状態 11〜13 (`KH_FrisbeeCatch.Water.cs`)、Wander の足を借りる、非 Owner の表示経路、`ClaimDisc` の取得待ちと喪失処理、Wander の `_speed = 0` の例外、`_GateToy` の門 (§D-5)
- [ ] Course/94 (F2〜F13)

### M4. 段階 1 の門の置き換えと文書

- [ ] `OutOfArea` の門を `_PlayableAt(自分の位置)` に。`playAreaRadius` を 0 に。文言 2 キー (13 言語)。`HasArea` / `InArea` / `BuildRing` の削除
- [ ] 置き換わる決定の記録 (DES-DOG-002 / IMP-DOG-012 / IMP-DOG-020 段階 7 / IMP-DOG-021:47)、ハーネス文書、game-design、[VER-WLD-001](VER-WLD-001-quest-ios-devices.md) 項目 12

## 検証の記録

- ベースライン (M1 前、コミット fc8e584): フリスビーハーネス既定モード 26/26 PASS (run 20260928-134052)。
- M1 (2026-09-28、Codex レビュー 9 件反映後): コンパイル C#/U# 0。Frisbee/4 (犬種 6 レーン + Terrain 10 パッド)、Frisbee/5 failures=0。ハーネス 既定 Lab 26/26 (run 144852)・Pome 26/26 (155148)・netorder 10/10 (145559)・sixdogs 14/14、solveRayMax ≤ 200 (155713)。terrain 10/10 (162232。配置を 3 度直した: T-up10 は仰角 15°、T-down04/up04 は縁を z +4.4 に、T-up04 は投げ台を 0.4 m の台へ、T-rock は岩を犬の 0.5 m 先へ、T-cliff は円盤が止まってから照準を判定。いずれも規則側ではなく配置・判定時点の不備)。本シーン: Course/92 BUILD_OK (6 匹に値、輪を非表示、一言)、Course/93 OK (周回路 4344 点: 到達 99.5%・跳べる 96.4%・noGround 0・帯 18 点は岸戻し)、Course/89 OK、W5 (75) PASS。Walking/5 は TOTAL 498 / FAILED 21 で、M1 の項目 (asymmetric landing・DES-DOG-005 values・play area) は 6 匹とも PASS。**21 件の FAIL は M1 前からのもの** (route 配列 n=436・門↔広場の nextHop・ディスクが広場の平坦部にある・拠点の高さ。09-27 の川沿い周回コース採用後、旧 Terrain Prototype の設定値 `KH_TerrainPrototypeConfig` と照合する検査が古くなっている。別件で検査を周回版に合わせる)。

- M1 の実装のずれ (上り 0.5 / 下り 1.0 m、帯の拡張条件、SolveRayCap の集計範囲、Ready のレイ範囲、Return の Drop、水遊び場は走り込み可・跳躍不可) は設計書 §B-6・§C-3b に「決定: 2026-09-28 M1 実装で変更」として記録。Codex の M1 レビュー 9 件 (高 3) はすべて反映。

- M2 (2026-09-28、Codex レビュー 4 件反映後): コンパイル C#/U# 0。Course/89 FRISBEE_RIVER_OK (float: water=24 / bridge=8 / edge=6 / noBank=0 / spot=True)。Course/94 FRISBEE_M2_PASS 8/8 (run 082637: F5a-river・F5b-spot・F5c-grab・F5c-recall・F5d-roll・F6-bridge・F7-nobank・F7b-edge)。ボール側の回帰: 78 WATER_PASS 14、75 W5_PASS、試作既定 26/26 (171911)。**88b は RW-down-Pome / RW-up-Pome が FAIL だが M2 の差分が原因ではない**: c4fd29a (ポメの swimSink 0.035 m、PCVR 決定) で泳ぐ口の高さが 0.086 → 0.051 m に下がり、ハーネスの口の余裕 0.03 m の基準を川の流路で割る (mouthLow)。c4fd29a 以降の最初の 88b (075505) から一貫して FAIL、時間・漂流・入水点は合格運転と同じ。PCVR の決定を変える (swimSink / swimPitch) かハーネス基準を下げるかはユーザー判断 → IMP-DOG-020 / 021 の範囲で別件。

## 関連

- [DES-DOG-005](DES-DOG-005-frisbee-anywhere.md) / [設計書](../../plans/2026-09-28-frisbee-anywhere.md)
- [IMP-DOG-022](IMP-DOG-022-frisbee-takeout-area.md): 段階 1
- [IMP-DOG-017](IMP-DOG-017-frisbee-intercept-solver.md) / [IMP-DOG-021](IMP-DOG-021-river-wide-fetch-and-recall.md): 置き換える計画器と、流用する川の回収
- [ハーネス](../../testing/kh-frisbee-harness.md)
