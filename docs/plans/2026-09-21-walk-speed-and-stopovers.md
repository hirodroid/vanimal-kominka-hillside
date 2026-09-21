# 散歩の速さ 4 段階と寄り道・立ち寄り (IMP-DOG-009 / IMP-DOG-010) 実装記録

2026-09-21。PCVR 所感「犬が 2 段階しかなく人が引っ張る」「寄り道が分かりにくすぎる」を受けた 2 Issue の順序・分担・試作値・チェックリスト。何を作るかは Issue が持つ ([IMP-DOG-009](../development/issues/IMP-DOG-009-walk-speed-tiers.md) / [IMP-DOG-010](../development/issues/IMP-DOG-010-walk-stopovers.md))。仕様の正本は [game-design §4](../game-design.md)。

## 進め方と分担

Fable = 司令塔 (設計・ゲート・Unity メニュー・コミット)、Opus サブエージェント = 実装 (同じ Wave 内でファイル重複なし)、Codex = 設計レビュー (X/D/P、実装前) とコードレビュー (Wave 後)、ユーザー = Play / PCVR。

| Wave | 内容 | ファイル |
|---|---|---|
| 1-A | 速さ 4 段階・人の速度推定・先行保持・Run クリップの再生速度・Build/Verify | `KH_LeashManager.cs`, `KD_DogWanderController.HillsideLeash.cs`, `KD_DogWanderController.cs`, `.Shared.cs`, `Editor/KH_DogWalkingEditor.cs` |
| 1-B (並列) | 立ち寄り場所のデータ・生成 (`Walking/4d`)・検証 (`4e`) | 新規 `KH_WalkStopovers.cs`, 新規 `Editor/KH_WalkStopoverEditor.cs` |
| 2 | 寄り道の作り直し、姿勢の位置導出、Fetch/Frisbee へ譲るときの後始末、Owner 取得時の初期化、Sit の書き手 | `HillsideLeash.cs`, `KD_DogWanderController.cs` |
| 3 | 立ち寄りの犬の行動 (`LeashStopover`) | `HillsideLeash.cs`, `KH_LeashManager.cs`, `KH_DogWalkingEditor.cs` |
| 4 (条件付き) | PCVR で Pickup 流用が「嗅ぐ」に見えなければ Sniff クリップを生成 (Lab → 6 犬種)。任意ではなく完了条件への対応 | `KH_FrisbeeJumpClipEditor.cs` 系 |

各 Wave 後: `unity_force_refresh_assets` → Console エラー 0 → `Walking/3 Build` (保存済みシーン) → `4d Build Stopovers` → `5 Verify` / `4e Verify Stopovers` → Codex コードレビュー → 是正 → 明示パスでコミット。

## 設計判断と試作値 (すべて PCVR で決め直す前提)

### 速さ (IMP-DOG-009)

| 項目 | 試作値 | 理由 |
|---|---|---|
| 段階 | 歩き 1.1 / 速歩 2.4 (犬側、現状) / 走り `dashSpeed` 4.5 / 全力 `sprintSpeed` 7.2 (`KH_LeashManager`、Build が所有) | 人の歩き 2 / 走り 4 m/s より速い段階を 2 つ。`followSpeed` 4.8 は全力へ統合 |
| 人の速度 | 身体の水平位置の毎フレーム変位を EMA (時定数 0.4 s)。1 フレーム 1.0 m 超はワープとして無視。段階: 走り ≥3.0 (解除 <2.4) / 歩き ≥0.7 (解除 <0.4) | Issue 009 の「1 s 変位を使う」から変更: 1 Hz の変位は静止判定用で速度にならない (`_holderSample` は 0.6 m 超で更新されるだけ)。EMA は新しい判定周期を増やさない |
| 割り当て | 戻る: >1.6× 全力 / それ以外 走り。手まわり: >1.2× 全力 / >0.7× 走り / 歩き。先行: 後ろなら 人が走り→全力 / 歩き→走り / 静止→速歩。前でたるみ (<0.45×) は 人が動いていれば走り / 静止なら歩き | 人が静止・低速のときに近距離で走らせない (Codex X3) |
| 先行保持 | 前で 0.45×〜1.1× かつ人が動いている間は待機に入らず `clamp(人の速度 + 2.0·(待機点 − 手の距離), 歩き, 全力)` | 待機を外すだけだと 1.25× の帰還と往復する (Codex D3) |
| Run クリップ | `Animator.speed = clamp(実速度/速歩, 1, runAnimScaleMax 2.5)` を `ApplyMoveAnim` で。Fetch/Frisbee が Animator を持つ間は書かない。譲る瞬間に Pickup/Sit/LieDown と speed を落とし寄り道状態を捨てる | Frisbee 側の Pickup 書き込みは `!_outputValid` が無く Wander が立てた Pickup が残る (Codex D2)。`KH_FrisbeeCatch.cs` は別セッション変更中で触らない |
| コーナー | 通過距離 0.35 m は据え置き。身体の向きと移動方向の角度差 >60° で歩き、>30° で走りに制限 | `StepToward` は移動方向を即座に目標へ向け見た目だけ 300°/s なので高速で横滑り (Codex D4)。共有定数 `deceleration`/`turnSpeedDegPerSec` は変えない |

### 寄り道 (IMP-DOG-010 B-1)

| 項目 | 現状 | 試作値 |
|---|---|---|
| 行き先 | 前 0.5〜2.0 + 横 ±1.5〜2.7 m のランダム点 | 手を道の中心線へ投影した点の前方 0.5〜2.0 m から垂直に ±1.8〜2.3 m。`_DistanceToTrail ≥ 1.6 m` (左右揺れ 1.1 m と区別)、手から ≤0.8×、NavMesh・完全経路・経路長 ≤5 m。リード 3 m で成立する幾何 (Codex X1) |
| 発生 | 手から 0.5× 以内、4 s ごと 25 % | 0.75× 以内かつ人が歩き中、4 s ごと `detourChance×(0.7+0.2·(犬番号%4))`。同じ人の犬全体で静穏間隔 12 s (Codex X4) |
| 嗅ぐ | 止まるだけ 1.5〜3 s | 向いてから停止で Pickup 3〜5 s。上限 12 s (6 s 未到着で中止)。人が静止で 0.95× 以内なら 1 回だけ +5 s |
| 切り上げ | 0.9× で即終了 | 0.8× かつ人が離れる向きでポーズを下ろし歩き出す (固定待ち無し。Codex X2/D1) |
| 姿勢の見え方 | Owner のみ | 全クライアント (Owner 含む) が同期済みの位置から導出: 停止 0.5 s 連続・道から ≥1.6 m・手から ≥1.2 m・手は道上・Fetch/Frisbee 非活動 → Pickup。近似表示を許容 |

### 立ち寄り (IMP-DOG-010 B-2)

- 初回は **嗅ぐ (Pickup) + 座って眺める (Sit) の 2 種、3 か所** (見晴らしのベンチ脇 Sit → 古民家を見る / 道中の木の根元 Sniff / 門側の草際 Sniff)。伏せは「歩き続けると起こされる」ので見送り (Codex X4/P)。
- データ `KH_WalkStopovers` (sync None)、子 `Stop00..` は看板と同じく位置・向き・種類を人が動かせ、配列は毎回子から再生成。`4e Verify` は NavMesh 上・道から 1.2〜2.4 m・最寄り Node からの完全経路 ≤8 m・場所同士 ≥6 m・配線。
- 行動: 前方 6 m・手から 0.8× 以内を通過するたび `stopoverChance` 0.5 を 1 回。犬ごと 45 s、同じ場所は連続しない、同じ pack の他の犬が滞在中は引かない、静穏間隔 12 s。到着 → 向く → ポーズ 4〜8 s (+5 s 1 回) → 切り上げは寄り道と同じ。目的地は変えない。

### 夜の散歩 (IMP-DOG-010 C)

**見送り。** ナイトモードはローカル・非同期 (`KH_NightMode`、game-design §2) なので犬の行動を変えると人ごとに動きが食い違う。同期される夜 (ライブの夜) が入ったら再検討。

## Codex 設計レビュー (X/D/P) 指摘 / 対応

| # | 指摘 | 対応 |
|---|---|---|
| X1 | 道から 2.5〜4 m はリード 3 m で成立しない | 手の投影点基準 ±1.8〜2.3 m、`_DistanceToTrail ≥1.6`、立ち寄りも 1.2〜2.4 m。成立率を Console で数える |
| X2/D1 | 0.9× 到達後の固定 0.6 s 待ちは引きずりに見え、帰還条件と競合 | 0.8× + 人が離れる向きで即座にポーズを下ろし歩き出す。延長は 1 回だけ |
| X3 | 人が静止でも近距離で走る | 割り当てを人の速度段階でゲート。ヒステリシス |
| X4 | 群れ全体の連続発生、伏せの起こされ問題 | 同じ人の犬全体で静穏間隔 12 s。伏せは初回見送り |
| D1 | 位置だけでは姿勢が一意でない、Late Joiner、Owner 交代 | Owner も同じ位置導出で表示 (近似表示を許容)、停止 0.5 s 連続、手が道上、Owner 取得時初期化 |
| D2 | Frisbee の Pickup 書き込みに `!_outputValid` 無し、yield が状態を捨てない | Wander が譲る瞬間に Bool/speed を落とし状態破棄 |
| D3 | 待機解除だけでは 1.25× 帰還ループ | 先行保持を追加。Console の `[KH_Leash] ... Return` で周期を確認 |
| D4 | 旋回半径の前提が誤り、コーナー広げは逸脱リスク | 身体角度差での減速に変更 |
| D5 | EMA のワープ誤認、履歴初期化未定義 | 1.0 m/frame で不連続扱い。初回・接続者変更・Owner 取得・Fetch/Frisbee 復帰で初期化 |
| D6 | 配置できても使われない場所が合格、到着タイムアウト無し | 4e に経路・間隔・配線チェック、6 s 未到着で中止 |
| P1 | 体験判定の順序が Issue と逆、Sniff は任意でない | Wave 2 → 3 に分け、チェックリスト §0 で嗅ぐ姿勢を最初に判定。Wave 4 は完了条件への対応 |
| P2 | 試作値を採用値に固定、観測項目不足 | 全数値を試作値と明記、往復の回数・所要時間を記録項目に |
| 食い違い | 種類数 (X: 3 種 / P: 2 種) | core-concept「豪華さは密度ではなく犬・つながりで」→ 2 種。1 匹で確認後に増やす |

## 実装記録

(Wave ごとに追記)

## Play チェックリスト (ユーザー担当)

(実装完了後に確定)
