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

### Wave 1 (2026-09-21、コミット 3a94a9f)

- 1-A: `KH_LeashManager.dashSpeed 4.5 / sprintSpeed 7.2` (`followSpeed` は Build の `RemoveVariable` で除去)、`KD_DogWanderController.runAnimScaleMax 2.5` (Build が所有)、人の速度 EMA とヒステリシス、先行保持、`ApplyMoveAnim` の `Animator.speed`、`ReleaseAnimatorSpeed()` (Fetch/Frisbee へ譲るとき・解除時・Update の早期 return で speed を戻す。読み戻して自分の値のときだけ 1 へ)、`LeashGo` の角度減速。Console: `[KH_Leash] dog{i} Return handDist=… holderSpeed=…` / `holder tier a->b`。
- 1-B: `KH_WalkStopovers` + `Walking/4d Build Stopovers` / `4e Verify Stopovers`。子 `KH_Dogs/KH_Stopovers/StopNN_{Sniff|Sit}` (+ 任意の `Look` 子) を人が動かし、配列は名前順で再生成。初回 3 か所: `Stop00_Sit` (ベンチ脇、古民家を見る) / `Stop01_Sniff` (Node07 の道端。木の根元の候補が無くフォールバック) / `Stop02_Sniff` (Node02 の道端)。
- 検査: Walking Verify 176/176、Stopovers Verify 45/45、Console エラー 0。Play 未。
- Codex コードレビュー (D/M) 指摘 / 対応:

| # | 指摘 | 対応 |
|---|---|---|
| D 中 | 解除と Fetch 開始が重なると Run の再生倍率が残る (Fetch は speed を書かない) | `ReleaseAnimatorSpeed()` を Update の Fetch/Frisbee 早期 return と解除経路からも呼ぶ |
| D 中 | yield 時の speed=1 が Frisbee の書いた値を上書きし得る | 読み戻して自分の書いた値のときだけ 1 へ戻す |
| D 中 | 4e が配列不整合で例外終了 | 全配列の形を先に検査し、揃ったときだけループ。backing も全要素照合 |
| D 低 | `SaveOpenScenes` が他のシーンも保存 | `SaveScene(scene)` に限定 |
| M 中 | `runAnimScaleMax` の所有者が無い (Verify は ==2.5) | Build が各犬に既定値を書く |
| M 低 | Fetch と Frisbee の責務のコメント誤り、「Play 確定値」の文言 | コメント修正、「試作値を Build/Verify で固定」に |

### Wave 2 (2026-09-21、コミット 7f56c98) と Wave 3 + Wave 2 是正 (コミット d36a1b8)

- Wave 2: `PickDetour` を手の投影点基準に作り直し (`KH_WalkRoute._ProjectToTrail/_TrailDirection` 追加)、向いてから Pickup で嗅ぐ、延長 1 回、固定待ち無しの切り上げ、`LeashPose` の位置導出 (Owner も同じ規則)、`_wantSit` の書き手、Owner 取得時の初期化。
- Wave 3: `LeashStopover` (通過時 1 回抽選 → 速歩で行く → 見る先を向く → 4〜8 s)、`stopoverIndex` の公開 (同じ人の他の犬の滞在判定)、姿勢導出に「場所」規則を追加 (伏せ > 場所 > 道端 > 対面)、Verify に `stopovers` の配線チェック (+2)。
- 検査: Walking Verify 178/178、Stopovers Verify 45/45、Console エラー 0。Play 未。
- Console (Owner): `[KH_Leash] dog{i} detour funnel rolls=a ok=b arrived=c posed=d` / `stopover funnel …` (10 回ごと)、`stopover k=… kind=…` / `stopover end reason=arrived|abort|cutoff|cap|interrupted`。
- Codex コードレビュー (Wave 2、D/M/U) 指摘 / 対応 (d36a1b8 に反映):

| # | 指摘 | 対応 |
|---|---|---|
| D 高 | 到着 0.35 m 手前で止まるので道から 1.6 m に届かず嗅ぐ姿勢が出ない | 到着 0.2 m、姿勢は 1.5 (保持 1.3) のヒステリシス、候補の下限は 1.5+0.2 から 1.7 に導出 |
| D 高 | 抽選距離 0.75× (2.25 m) が先行保持の帯 (2.34〜2.70 m) より内側で、普通に先行する犬は抽選されない | 0.95× に |
| D 中 | Return/Rest/Invite/解除などの割り込みで `EndDetour` を通らず静穏間隔が更新されない | `EndPause` に統一し、フレーム冒頭で前フレームのモードと比較して割り込みを検出 |
| D 中 | 回転中でも停止 0.5 s で頭が下がる | yaw 速度 >20°/s は停止に数えない。到着期限 6 s と整列期限 3 s を分離 |
| D 中 | 「手は道上」が 6 m で広すぎる | 姿勢用の道幅 3 m を別定数に |
| D 中 | Owner/非 Owner の境界で判定が割れる | 手距離 1.2/1.0 のヒステリシス、テレポート観測で停止履歴を初期化 |
| D 中 | 譲渡時に LookAt が残る (既存の穴) | `YieldAnimator` で LookAt も下ろす |
| U 中 | 延長中に誘いが横取りする | 寄り道/立ち寄り中は誘いを保留 |
| U 中 | 同じ人の犬が同時に寄り道する | 進行中の犬がいれば開始しない |
| U 高 | 「1 往復で数回」を数字だけでは期待できない | 漏斗ログ (抽選/候補/到着/姿勢) で Play で測る |
| M | `lastPauseEnd` の契約、過大なコメント、初期化関数の範囲、係数の散在、Sit の注記 | 契約を明記 (中止・割り込みも含む終了時刻、Owner 交代で引き継がない)、`ResetPauseAndPoseState` に改名、定数ブロックへ集約 |

### Wave 3 の Codex コードレビュー (D/M/U) 指摘 / 対応 (最終コード round)

| # | 指摘 | 対応 |
|---|---|---|
| D 高 | 先行中の犬では「犬の前方」と「手から 2.4 m 以内」が両立せず、往復しても立ち寄りの抽選が起きない | 通過判定を手基準に (手から 6 m 以内・手より 1 m 以上後ろでない)。当たりは保留し、手から届いた時点で開始 |
| U 高 | 歩き続ける人は 2.4 m の窓を約 1.6 s で通過し、姿勢が見える前に終わる | 立ち寄りだけ窓を 1.0× に広げる。「歩き続ける人には短い反応、止まった人には 4〜8 s」と割り切り、チェックリストで分けて評価 |
| U 中 | 整列期限切れを「向いた」扱いにすると古民家を見ずに座る | Sit は 45° 以上ずれていたら座らず終了 (reason=align)。整列中は 90°/s で回る |
| D 中 | 割り込み検出の 1 フレーム遅れと、直後の解除で終了記録が抜ける | `pausing` フラグを終了処理まで保持し、解除・初期化経路でも `EndPause` を通す |
| D 中 | Owner 分裂時に旧 Owner 側の古い滞在状態が他の犬を永久に抑止 | 自分が所有する犬の状態だけ信用。Owner を失ったら滞在状態を破棄 |
| D 中 | 通過履歴の解除が抽選呼び出しに依存 | 毎フレーム手基準で「通り過ぎた」を更新 |
| D 中 | 発進フレームに Sit と Walk/Run が同時に立つ | `ApplyMoveAnim` で移動中は Sit を落とす |
| D 中 | 場所由来の姿勢にヒステリシスが無い | 半径 0.7/0.9、手 1.0/0.8 のラッチ |
| M 中 | 漏斗ログが 10 抽選ごとでは 1 往復で出ない、`posed` は要求フラグ | 終了ごとにも出す。`poseReq` と実保持時間 `held` を分ける |
| M 低 | 共通定数の名前と重複ロジック、古いコメント | `Pause*` に改名、切り上げ・延長の小さな共通化、コメント修正 |
| D/U 問題なし | 通常待機点が場所の 0.7 m 以内なら抽選不成立でも場所の姿勢になる (導出方式の仕様)、Sit の Animator 遷移は C# では確認不能 | PCVR で見る |

## Play チェックリスト (ユーザー担当)

準備: 最新をビルドして PCVR で入る。犬 1 匹 (どれでも) をリードにつなぐ。Console (または Udon ログ) は `[KH_Leash]` で絞ると読める。**すべて試作値**なので「合格/不合格」より「どう感じたか」を書き残す。

### §0 エディタで判定できない項目 (ここが NG なら他は見なくてよい)

- [ ] **寄り道が「道端で何かを嗅いでいる」と分かる** (道からはっきり外れ、頭を下げて数秒止まる)。Pickup 流用が「くわえている」にしか見えないなら Wave 4 (Sniff クリップ) へ
- [ ] 人が普通に歩いても走っても、犬が前へ出る (人が犬を引っ張る形にならない)
- [ ] 7 m/s の全力で足が滑って見えない・怖くない

### 1 人・1 匹

速さ (IMP-DOG-009):
- [ ] 歩く: 犬が先に出て待機点で止まり、人が追いつくとまた出る。止まったり走ったりのガクつきが気にならない
- [ ] 走る (ダッシュ): 犬が前を保ち続ける。直線を走り続けても Console に `Return handDist=` が周期的に出ない (出るなら先行保持の係数が弱い)
- [ ] 止まる: 犬が近くで急に走り出さない (静止中は歩き/速歩まで)
- [ ] 引き返す: 後ろから追い越すときの速さが自然 (人が歩き→走り 4.5、走り→全力 7.2)
- [ ] 曲がり角: 通り過ぎ・横滑り・崖側への逸脱が無い。減速して曲がって見える
- [ ] 遠くから戻る (1.6× 超): 全力で戻って来て、手前で止まれる
- [ ] Console `holder tier a->b v=` が自分の動きと合っている (歩き ≈2、走り ≈4)

寄り道 (IMP-DOG-010 B-1):
- [ ] 往復 1 回で寄り道が何回起きたか (目安: 数回)。Console `detour candidates ok=a fail=b` の比率 (ok がほぼ 0 なら幾何が成立していない)
- [ ] 人が止まって見ていると嗅ぎ続ける (+5 s)、歩き続けると顔を上げて自分で戻る (引きずりに見えない)
- [ ] 嗅いでいる最中にフリスビー/ボールを投げても姿勢が残らない

立ち寄り (IMP-DOG-010 B-2):
- [ ] 見晴らし (ベンチ脇) **人が止まって見る場合**: 座って古民家の方を見る (4〜8 s)。座る向き・位置が不自然でない
- [ ] 同じ場所を **歩き続けて通り過ぎる場合**: 犬が場所へ寄って短く反応し (座る前に切り上げ)、引きずられて見えない。「興味を持った」と読めるか
- [ ] Node07 / 門側の道端: 嗅ぐ。道端の「そこに何かある」感があるか (無ければ場所を動かす: `KH_Dogs/KH_Stopovers/StopNN_*` を Inspector で移動して `Walking/4d` を再実行)
- [ ] Console `stopover k=… end reason=` で回数と理由 (arrived/abort/cutoff/cap) を控える。abort が多ければ場所が遠い
- [ ] 目的地への到着が寄り道・立ち寄りで「遅すぎる」と感じない

### 3 匹・6 匹

- [ ] 同じ場所に 2 匹以上が同時に止まらない。次々と止まって「また待つのか」にならない (静穏 12 s)
- [ ] 速い段階で 6 匹が走っても重ならず、紐の見え方が破綻しない
- [ ] 往復の所要時間と寄り道・立ち寄りの合計回数を控える

### 2 人以上

- [ ] 他人の犬が全力で走る様子が飛んで見えない (VRCObjectSync の補間)
- [ ] 他人の犬の嗅ぐ/座る姿勢が、自分の画面でも同じ場所で出る (近似表示なので 0.5 s 程度の遅れは許容)
- [ ] 途中参加者にも、止まっている犬の姿勢が出る

### Desktop

- [ ] Shift ダッシュで犬が前を保つ。Desktop の歩き速度でも「引っ張る」形にならない

| 症状 | 最初に見るところ |
|---|---|
| 犬が人より遅い・引っ張る形 | `holder tier` ログが歩き/走りに上がっているか (EMA の閾値 0.7 / 3.0)。上がっていれば割り当て表 (game-design §4) |
| 直線で周期的に `Return` が出る | 先行保持の係数 2.0 と上限 1.1×。人の速度が全力 7.2 を超えることは無いので係数不足 |
| 足が滑る | `runAnimScaleMax` 2.5 (Frisbee は 3.5 でブレと判断)。速歩 2.4 を基準にした比率 |
| 曲がり角で通り過ぎる | 角度減速 (60°/30°) と `LeashGo` のコーナー通過 0.35 m |
| 寄り道が起きない | `detour candidates` の ok/fail。fail ばかりなら道幅・NavMesh 側の余裕 (±1.8〜2.3 m) が無い |
| 嗅ぐ姿勢が出ない/他人の画面で出ない | 導出条件 (停止 0.5 s・道から 1.6 m・手から 1.2 m・手は道上)。道幅が広い所では 1.6 m に届いていない可能性 |
| 立ち寄りで abort が多い | 場所が道から遠い (1.2〜2.4 m の帯) か経路が回り込む。`4e Verify Stopovers` の経路長 |
| 座らない | Sit の Bool 遷移 (購入 Controller の Idle→Sit)。`_wantSit` は Wave 3 が初めて立てる |
