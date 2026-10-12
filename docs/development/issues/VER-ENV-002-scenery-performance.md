---
id: VER-ENV-002
type: VER
system: ENV
status: wip
priority: High
summary: 景観・小物の仕上げ後に Quest 3 が 20〜40 (10-04)、カート・洗い道具・招待の追加後は常時 20 (10-12 ユーザー報告)。毎フレーム処理の増加と描画を切り分けて原因を特定し、眺めを保ちながら調整する
next: "2026-10-12 に C (無駄の修正) を実装・コミット。次はユーザーの Quest 実機で fps を読む (本文「ユーザーの Play / Quest で見てほしいこと」6 項目)。戻り切らなければ 10-04 の景観 (抜きの草・板の木、Quest の小草の近距離化) と材質の Mobile 差し替えを候補に"
updated: 2026-10-12
---

# VER-ENV-002 景観改善後の性能調査と調整

2026-10-04 ユーザー指定で [IMP-ENV-001](IMP-ENV-001-hillside-polish.md) の③と景観追加前後の負荷・容量比較を引き受ける。[IMP-ENV-009](IMP-ENV-009-course-roadside-scenery.md) と合わせて先に景観品質を改善する。

変更前のユーザー報告はQuest 3で通常40〜50 FPS、負荷が高い場所でも30 FPS。人数・視点・計測方法・発熱状態は未記録なので、同条件の自動比較基準やiOS合格とは扱わない。

## 2026-10-04 ユーザー報告 (見た目の調整が一通り済んだ後)

> パフォーマンスは見た目を整えた後で見る想定だったが、やはり下がった。20〜40 ぐらいになったので 10 fps ぐらい下がっていそう。毎フレーム処理しているところが増えていないかも踏まえて、全体的に確認したい。

- Quest 3。何もしていなくても下がる
- 川を見ているときに下がりがちだったかもしれない
- 犬が遊んでいても、遊んでいなくても。散歩しているときも
- 40 まで行くときもあれば、20 台後半まで下がるときもある。安定しない。これといった条件は分からなかった
- 人数・版・滞在時間・発熱の状態は未記録

## 変更前の報告の後に入ったもの (原因の候補。どれが効いているかは未計測)

「通常 40〜50」の報告の後に入ったのは景観と小物 (未コミットのシーン変更を含む)。10-03 の「だいたい 40、平常時 50」(`409e227` 相当) から数えると、写真の増量・手洗い・動画カメラ・看板も入る。

**描画**

| 追加 | 量 (制作記録の値。LOD の近景・遠景を両方含む保存数で、同時に描く数ではない) |
|---|---|
| 道沿いの木立・草・ベンチ (`KH_RoadsideAutumn`) | Renderer 319。近景木 18・板木 43・低木 54、草 50 チャンク (17,780 三角形)、ベンチ 3 |
| 川沿い | Renderer 111。近景木 4・低木 25・石 19・草 26 チャンク。保存メッシュ合計 198,743 三角形 |
| 小物の仕上げ ([IMP-ENV-012](IMP-ENV-012-close-prop-finish.md)) | 棚・リード掛け・写真ボード・ボトルのモデルとノーマルマップ付き材質 |
| 写真 ([IMP-WLD-007](IMP-WLD-007-photo-capacity-walk-and-menu-snap.md)) | RenderTexture 24 枚、ボードの板 8 枚。**撮る瞬間に景色をもう 1 回描く** (撫では 4 枚連続、散歩は 90 秒かつ 40 m ごとなど) |
| 看板・図解 | 散歩の帯 3 か所、洗い場の図解 4 コマ、遊びの看板 |

「川を見ているときに下がりがち」は、川沿いの追加 (葉・草の抜きの重ね描き) と、川に沿った長い見通し (遮蔽が効きにくい)、川の水面の描画が重なる場所と一致する。

**Udon (毎フレーム・定期の処理)**

- Update / LateUpdate / PostLateUpdate を持つスクリプトは約 55 か所 (移植した `KD_` を含む。シーンに居ないものも含む数)
- 10-03 以降に増えた・変わったもの
  - `KH_HandWash`: Update なし。VR で洗い場の犬の近くにいる間だけ毎フレーム
  - `KH_DogWash.Update` (6 匹): 大きく書き換えた。休んでいる間はすぐ戻る作りだが、実測はしていない
  - `KH_WashStationBoard.Update` (4 か所): 遠いときは間引く
  - `KH_PhotoWalkWatcher`: 0.5 秒ごと、リードでつないだ犬がいる間だけ
  - `KD_PhotoDirector` (写真 ON の間 10 Hz)、`KD_PhotoSettings` (4 Hz)、`KD_PhotoViewer.PostLateUpdate`
  - `KH_VideoCamera`: Android / iOS では Start で止める (PC 専用)
- [IMP-DOG-036](IMP-DOG-036-leash-decision-thinning.md) で保留にした項目 (水の取ってこい・洗浄中・フリスビー中の判断、自分の紐、表示だけのスクリプト、おもちゃの監視) は未着手のまま

**その他**

- 川音の 20 音源 (10-02 に追加。追加後の fps は測っていない)
- 発熱による低下 (「安定しない」の候補。入室直後と 15 分後で比べる)

## 進め方

1. **静的な棚卸し (AI、Unity 不要)**: 毎フレームの入口 約 55 か所を表にし、「誰も使っていないときにすぐ戻るか」「10-03 の `409e227` から増えたか」を見る。シーンの保存ファイルから、追加した Renderer・材質・影・LOD の設定を数える
2. **計測プローブを一時的に戻す (AI、Unity が空いてから)**: `KH_PerfProbe` (10-03 に Mobile/22 で撤去) を Mobile/20 で置き直す。群に「10-04 の景観 (道沿い・川沿い)」「写真」「川音」を足す
3. **Quest 3 で読む (ユーザー)**: 固定地点 (門・川沿いの上流と下流・奥広場・展望) で、群を 1 つずつ OFF にして fps を読む。1 人・犬 6 匹が待機の状態で、入室直後と 15 分後
4. **大きい順に調整 → 同じ地点で再計測**。景観の見え方は比較 HTML の視点で前後を比べる
5. プローブを撤去する

## 完了条件

- [x] [制作記録](../../plans/2026-10-04-roadside-scenery.md)から追加オブジェクト・共有素材・LOD・比較画像・変更前の記録を引き取る
- [x] 毎フレーム処理の棚卸しの表を残した (増えた所と、止められる所) — [静的な棚卸し](../../plans/2026-10-04-performance-inventory.md)
- [ ] 代表地点で描画・メモリ・容量を区別して計測し、Quest 3/iOSの実機条件・人数・犬数を記録する
- [ ] 低下の主な原因を、群の ON/OFF の計測値で特定した (推定だけで調整しない)
- [ ] 必要な調整を行い、景観の見え方を保ちながら負荷と容量を再確認する。Quest 3 で変更前の水準 (通常 40 以上) に戻ったかをユーザーが確認する
- [ ] 計測プローブを撤去した
- [ ] [VER-WLD-001](VER-WLD-001-quest-ios-devices.md) に端末別結果を反映する

計測手順は `vrc-performance`。Play計測は `vrc-verification` の相談・部分実行手順による。性能の合格を景観制作だけから推定しない。

## 静的な棚卸しの結果 (2026-10-04、手順 1)

[記録](../../plans/2026-10-04-performance-inventory.md)。計測ではなく、コードと保存ファイルの読み取り。

- **毎フレームの Udon は 10-03 の版 (`409e227`) からほぼ増えていない。** 増えたのは洗い道具 4 個 (0.5 秒周期)・手洗い (0.5 秒周期)・待機中は止まる写真の散歩監視と動画カメラだけ。犬の歩行・Fetch・フリスビー・`KH_DogWash` の Update・`KH_ToyRecall` は変更なし
- **描画は大きく増えた。** 有効な Renderer 787 → 1,187、LOD のグループ 115 → 357、抜きの葉・草の材質を使う Renderer 24 → 331
- **川沿いの草 (26 かたまり、各 約 30 m) と板の木 (43 本) は、概算で 200 m 前後まで消えない。** 「川を見ると下がる」と合う。川の水面は不透明の軽い作り
- 小物 15 個 (`KH_PropFinish`) と洗浄道具は Unity 標準の Standard シェーダー (ノーマルマップ + 金属マップ)
- 写真は誰も何もしていなくても撮る (犬の走り出し・じゃれ合い)。1 枚ごとに景色をもう 1 回描く。一瞬の落ち込みの候補
- 前から在る固定費の上位は、犬の歩行・Fetch の待機・`KH_ToyRecall` の見回り・フリスビーの待機。間引ける余地は記録にまとめた (低下の原因ではなく底上げの候補)

手順 2 の計測用ボタンの群は、記録の「次の計測で分ける群」に合わせる (景観全部 / 草だけ / 板の木だけ / 小物 / 犬の Udon。写真は設定の ON/OFF)。

## 計測のしかた (2026-10-04 に設置、2026-10-06 に撤去)

> 2026-10-06 にユーザー指示で撤去した (下の記録)。読むときは Mobile/20 で置き直す。以下は設置していたときの手順。

計測用のボタンは **スポーンの右横** に 2 段で置いた (前の場所はおじいさんの立ち位置と重なるので移した)。押すと自分の画面だけ ON/OFF する。

| 段 | ボタン (左から) |
|---|---|
| 上 | Terrain / Scenery (10-03 までの木・石) / Leaves (10-03 までの葉) / Entrance / Dogs / River |
| 下 | DogUdon / **NewGrass** (10-04 の草 76) / **NewBoards** (10-04 の板の木 130) / **NewOther** (10-04 の低木・近景木・石・ベンチ 224) / **Props** (小物 15) |

手順 (1 人・犬 6 匹は待機のまま):

1. 全部 ON のまま、川沿いで川下を向いて fps を読む (基準)。ワープで行ってよい
2. スポーンへ戻り NewGrass を OFF → 同じ場所・同じ向きで読む → ON へ戻す
3. NewBoards、NewOther、Props、DogUdon も同じように 1 つずつ
4. NewGrass・NewBoards・NewOther を 3 つとも OFF (= 景観の追加前に近い状態) で読む
5. 余裕があれば、門・奥広場・展望でも 1 と 4 を読む。入室直後と 15 分後で基準が変わるかも見る

DogUdon を OFF にすると犬の状態がずれることがある。計測の最後に回し、終わったら入り直す。

| 地点・向き | 基準 | NewGrass OFF | NewBoards OFF | NewOther OFF | 3 つとも OFF | Props OFF | DogUdon OFF |
|---|---|---|---|---|---|---|---|
| 川沿い・川下 | | | | | | | |
| 川沿い・川上 | | | | | | | |
| 門 | | | | | | | |

## 2026-10-12 ユーザー報告 (第 2 回) と静的監査

ユーザー報告 (Quest 3 単体、カート版): 「入った瞬間から常時 20 fps、30 に一度も届かない、ひどい時は 10 代後半。2 匹連れてカートで 10 分散歩して 20 代前半。前回のチューニング以降 (カート等) の毎フレーム Udon が怪しい」。10-04 の「20〜40 で安定しない」から、**視点に依らない定常コストが積み増しされた**読み。10-04 の描画側の切り分けは未実施のままなので、今回の低下は「10-04 の未計測の描画 + 10-06 以降の増分」の重なりとして扱う (Udon だけで説明しない)。

### やったこと (Fable 司令塔、Opus 監査 2 本、コード・シーン・Unity の設定は未変更)

- Edit モードで実体数を取った (シーンファイルの集計はプレハブの中身が見えない)。有効 UdonBehaviour **364** (10-06 のシーン保存では 234)、Canvas 35 枚有効 (TMP 447 個)、AudioSource 118 (待機中に鳴る新規は 0)、VRCObjectSync + Pickup **68** (洗い道具 47、犬 21)、Unity Standard の Renderer 170 (カート 78 = 乗用 8 + 乗り場の見本 5、洗い道具・小物。Android のビルドフックは地形しか差し替えない)。KH_WashTool は 19 → **47** 個。
- Udon の毎フレーム経路 (91b3fe3 以降の差分 72 ファイル): [記録](../../plans/2026-10-12-performance-inventory-udon.md)。描画・UI・音・物理・同期: [記録](../../plans/2026-10-12-performance-inventory-scene.md)。
- 統括が現物で検証した上位 5 件 (下の表の 1〜5) と、シーンの同期方式 (KH_WashTool 47 個のうち 28 個が `_syncMethod: 2` = Continuous、19 個が None。スクリプトは `NoVariableSync` 宣言)。

### 結果 (計測ではない。extern 数は ±50% の概算、20 fps 換算)

10-06 以降に**待機中の Udon が約 +900〜1,200 extern/フレーム増えた** (10-04 の固定費 約 500〜800 のほぼ 2 倍)。加えて 0.5 秒ごとに約 4,000 extern の塊が 1 フレームに乗る。周期型 (0.15〜1 秒) は fps が下がるほど 1 フレームあたりが増える (悪循環)。Quest の extern 単価は未計測。09-30 の実測 (犬配下 158 個 OFF で PC −4.0 ms、Quest 10〜20 → 30〜40 fps) から、PC の数 ms は Quest で 5〜8 倍になる。

| 順 | 何 (個数) | 待機中 /フレーム | 10-04 比 | 直し方 (挙動を変えない) |
|---|---|---|---|---|
| 1 | 犬の誘い `KD_DogWanderController.HillsideInvitation.cs:32-35` (6) | 約 150〜250 | GREW | `inviteDogStation[dogIndex]` (同期配列 1 回) を `_InvitationAvailable()` / `_IntroDogAvailable()` (他 Behaviour 約 30 回) より先に見る。結果は同じ |
| 2 | 誘いの保守 `KH_LeashManager.Invitation.cs:132-172` (Owner、0.5 s) | 1 回 約 4,000 (平均 約 200) | GREW (新規の塊) | 報告の表が空・`inviteHeldMask == 0`・全犬 `inviteDogStation == 0` なら 80 要素のループ 3 本と 6 匹の判定を飛ばす。何も書かない結果は同じ |
| 3 | `KH_WashTool` (47) 0.5 s 巡回 | 約 120〜190 | GREW (19 → 47) | 選ばれていないスロット (`!selectionActive && returnSeconds <= 0`) は idle ループを止め `_SetSelected(true)` で再開。28 個の Continuous は別件 (`NoVariableSync` は Continuous と共存できる宣言で None の指定ではない。同居の VRCObjectSync とネットワークイベントを確かめてから) |
| 4 | `KH_WashSupportVisual.cs:15` (4) LateUpdate 毎フレーム | 約 140 (fps によらない) | NEW | 台が動いた時だけ (`KH_WashStationCall.cs:562` と `_StandPose` の後に `_RefreshVisual()`、`KH_WashSelection.cs:186` は既にそうしている)。LateUpdate を外す |
| 5 | `KH_CartManager.cs:164` (1) | 約 130 + 1 s ごと約 700 (ApplyCore) | NEW | 時刻比較を `IsOwner` より先に。表示中・駐車待ちの台が 0 なら MotionTick のループを飛ばす。ApplyCore の毎秒全呼び出しは安全網なので据え置きでよい |
| 6 | `KH_WashSelection.cs:72-73` (4) 0.25 s 自己再送 | 約 80 | NEW | 待つカテゴリが無ければ再予約しない (受信・要求・道具の OnDrop で再開。「持っている間の切替を Drop 後に反映する」役割があるので単純削除は不可) |
| 7 | `KH_WashSelectionBoard.cs:74` (4) 0.25 s、距離なし | 約 80 | NEW | `KH_WashStationBoard` と同じく近い時だけ 0.25 s、遠ければ 1〜2 s |
| 8 | `KH_WalkIntro.cs:116-121` (1) 0.15 s、終わっても止まらない | 約 75 (導入中は約 700) | NEW | 完了・非表示・却下かつ Quiet なら Tick を飛ばす (`_Replay` / `_OnLeashChanged` / 設定で戻す) |
| 9 | `KD_DogBook.cs:164-165` (1) 0.5 s、距離なし | 約 65 | GREW 少し | 開いている・近い時だけ 0.5 s |
| — | 犬の Update / LateUpdate 本体、Fetch・フリスビー・撫で・おやつ・寝床・ライブ、カートの Dock / Seat / Controls / StopButton / StationPanel、WashToolState、FurColor、ShopItem (51)、KH_VideoCamera (Android で Start が止める) | 不変 / 0 | — | — |

描画・UI・音・同期側 (スポーン 30 m 以内で 10-06 以降に増えたもの): 見本カート 1 台 + 掲示 (17 m)、洗い場の有料ボードと器具 (8 m)、犬紹介の Canvas 3 枚 (8 m)。概算 +15〜40 draw call で、**これだけで 40 → 20 (+25 ms) は説明できない**。新規の AudioSource 48・ParticleSystem 36 は待機中に鳴らない・出ない。洗い道具の Rigidbody 47 は全部 kinematic。非 Udon で数に比例して増えたのは VRCObjectSync + Pickup (40 → 68) だけ (推定 1 ms 以下)。10-04 の景観 429 Renderer は static でなく、オクルージョンのベイクは 09-27 (景観・カート・洗い場より前)。Quest の lodBias は 2 の可能性が高く、10-04 棚卸しの「約 200 m まで消えない」は倍の距離になりうる。

未コミットの別セッションの変更について: `ProjectSettings/AudioManager.asset` の voice 64/32 → 32/24 は、ワールドの AssetBundle に ProjectSettings は乗らないので Quest には効かない見込み (確度 約 90%)。シーンの `m_IndirectSpecularColor` が黒になったのは再保存の副産物で性能には無関係。どちらも触っていない。

### 次の一手 (提案。ユーザー判断待ち)

1. **Play 無しの確認 (ユーザー、今の版で)**: スポーンで真上の空を見る。20 のままなら CPU (Udon・同期) 側、上がれば描画側。OVR Metrics の GPU % / CPU % が読めればなお良い。
2. **明らかな無駄 (上の表 1〜9) を先に直す** (挙動を変えない順序の入れ替えと、動いていない時に止めるだけ)。実装は Opus、Codex の独立レビュー後に ClientSim で Cart / Wash / Invitation の既存ケースを絞って回す。
3. **計測ボタンを置き直して Quest で群別に読む** (Mobile/20 の群を更新: 10-04 景観 (草 / 板の木 / 他) / カート一式 / 洗い道具 47 / Canvas 全部 / 新規 Udon (招待・案内・洗い選択・支柱) / 犬 Udon / 川音 20)。シーン保存が要るので、別セッションの未コミット差分と調整してから。
4. 描画側の候補 (測ってから): カート・洗い道具・小物の材質を Android/iOS ビルドだけ `VRChat/Mobile/Standard Lite` へ差し替えるフック (地形と同じ型)、Quest の小草の消える距離を先に縮める (樹木の輪郭は残す)、遠い掲示の Canvas + Raycaster を 40 m 超で切る、川音を近い 3〜4 本だけ鳴らす。static 化 + オクルージョン再ベイクは遮蔽物 (建物・岩) を選んでから。


### 決定: 2026-10-12 ユーザー「景観のレンダー停止はとくに変わらなかった。C かな。計測不要かも。原因はほぼ確定している」

C (無駄の修正、表 1〜9) を先行し、Quest の群別計測 (B) は行わない。10-04 の景観の切り分けは、ユーザーの観察 (Renderer を止めても変わらない) を記録として残す。修正後の確認は Quest 実機でユーザーが fps を読む。

### Codex 相談 (2026-10-12、vrc-review 相談モード、gpt-6-astra。採否は統括が現物で判断)

| 問い | Codex の結論 | 採否 |
|---|---|---|
| 常時 20 は「10-04 の描画 + 10-06 以降の Udon 増分」の重なりか | 第一仮説にしてよい。ただし extern 数は処理時間ではなく、寄与率は未確定。0.5 s の山は引っ掛かりの説明にはなるが常時 20 の単独原因とは断定できない。追加候補の順は ① 近景の Standard・Canvas/TMP の描画、② 洗い道具の Rigidbody・Collider・ObjectSync の実稼働、③ 犬の Animator・スキニングの既存固定費。Pickup / Station / Canvas / Continuous 28 個は個数だけでは評価できない | 採用。本票の読みと同じ |
| 次の一手 (A: ClientSim の群別 ms / B: Quest の計測ボタン / C: 無駄を先に直す) | **B → C → B の再計測**。最初の B は同じ位置・向き・犬の状態で「基準 → 新規 Udon 停止 → 復帰 → 10-04 景観の Renderer 停止 → 復帰」、片方ずつで差が小さい時だけ両方停止。その後に必要な側だけカート・洗い道具・Canvas・犬へ分解。A は Quest が当面測れない時の CPU 候補の順位づけには使えるが、主因・改善量は決まらないので必須にしない。`enabled=false` では遅延イベント (WashSelection の `_Poll` 等) が止まらないので、停止が成立したかの確認を群に入れる。計測 UI は Canvas 群から除外し、ビルド内容 (未コミット差分を含む) を固定して記録する | 採用。Play (ClientSim) は今回は回さない。計測ボタンの群は「新規 Udon」(招待・案内・洗い選択・支柱・カート管理) を 1 群にまとめ、遅延イベントが止まったかを Verify に入れる |
| C を先に直す順と禁止事項 | 招待の判定順序 → 支柱の変更時更新 → 案内の休止 → 洗い選択の必要時巡回。Continuous → None は別件 (`NoVariableSync` は Continuous と共存できる宣言)。招待の Owner 側は「自分が持っていない」で止めず、他人の報告・期限切れ・解除の同期・Owner 交代時の復元を残す。握手フレームと Update / LateUpdate の順序は維持。Late Joiner 用の同期状態をイベントへ置き換えない。案内は完了直後の成功表示を終えてから休止 | 採用。表の 1〜9 の順序と注記を揃えた |
| 10-04 の景観を測る前に静的に決められる調整 | 方針までは選べる (Quest の小草を先に近距離化、樹木の輪郭は残す)。LOD は画面占有率なので Bounds と lodBias を確かめて対象別に。static 化は用途別フラグで、抜きの葉・板の木を遮蔽物にしない。再ベイクを最初の切り分けにしない | 採用。描画側の候補 4 の「static 化 + 再ベイク」は「測った後、遮蔽物を選んでから」に格下げ |


### 実装 (2026-10-12、C: 無駄の修正。Opus 3 本 + 統括の検証 + Codex 独立レビュー)

コード 10 ファイル (下)。シーン・プレハブ・`.asset`・Unity の設定は未変更 (配線の追加なし。支柱は `liftRoot.parent.Find("SupportGroundRoot")` で Start に取得)。C# 0 / U# 0。Edit モードの検査: Leash Invitation/2 Verify 49/49・3 Probe Transitions 11/11、Walk Intro/3 Verify 81/81、Cart/2 Verify `CART_OK` 2,320、WashPremium/44 Verify Selection 1,011 PASS / 0 FAIL、Wash/23 Verify Stand Sizes 0 FAIL。**これらは既存の回帰の確認で、新しい早期 return・休止・再開の経路は通らない** (Codex 指摘)。新しい経路の確認は Play (ユーザー) に残る。

| 表の # | 変更 | 不変条件 |
|---|---|---|
| 1 | `KD_DogWanderController.HillsideInvitation.cs` `UpdateInvitation`: `inviteDogStation[dogIndex]` → `inviteUntil` → 可否判定 2 つ、の順 | 全 exit が同じ `_invitationStation = -1; return false`。可否判定は純粋な読み取り (WashHeld のメモはフレーム内キャッシュ) |
| 2 | `KH_LeashManager.Invitation.cs` `InvitationTick`: Owner 側の早期 return (`!_inviteAnyReport && inviteHeldMask == 0 && now >= _inviteRebuildUntil && 全 inviteLookIds == -1 && 全 inviteDogStation == 0`)。`_inviteAnyReport` は `RememberInvitation` (mask != 0) で立ち、tick が `held != 0` で再計算 | その条件下では 5 本のループが何も書かず `changed` は false (`ReduceInvitation(0,0)` は `inviteHeldMask == 0` なら無書き込み)。ローカルの報告・他人の報告・Owner 交代の 3 秒・期限切れは従来どおり。反射で呼ばれる 4 メソッドの署名は不変 |
| 3 | `KH_WashTool.cs`: 未選択スロットの idle ループを `CanRest` で停止 (selectionActive false・returnSeconds <= 0・返却要求なし・非保持・保持者なし・debugHeldOverride 0・state.spraying false)。再開 `WakeIdle`: `_SetSelected(true)`・`_OnStateChanged`・`OnOwnershipTransferred`・TrackHolder が保持者を見た時。`SprayChecks` は `state.spraying` を 1 回だけ読む | 未選択スロットは `ConfigureSlot` で pickupable false + Collider off なので掴めない。止まるのは有料 28 スロットのうち未選択のもの (4 洗い場で 3 カテゴリ選択なら 16 個。無料 19 個は従来どおり) |
| 4 | `KH_WashSupportVisual.cs` LateUpdate 削除、Start で 1 回、同じ値なら書かない。`KH_WashStationCall.cs` lift の書き込みと `_StandPose` で dirty、Update 末尾で 1 回 `_RefreshVisual` | 台を動かすのは StationCall (lift・_StandPose) と `KH_WashSelection._Apply` (直接呼ぶ) だけ。更新が LateUpdate から Update 末尾へ移るが、その間に支柱を読む物は無い |
| 5 | `KH_CartManager.cs` 時刻比較を `IsOwner` より先 (非 Owner も時刻を進め、`OnOwnershipTransferred` で 0 に)。`Motion.cs` `_motionCount` (= `_shown || !_parked` の台数、EnsureLocals / ApplyCore 末尾 / ParkCart で数え直し) が 0 なら MotionTick を即 return | count 0 = 全車 `!_shown && _parked` = 旧ループが全部 continue していた状態。書き込み箇所は 3 か所で全部カバー、漏れても 1 秒の ApplyCore で自己修正 |
| 6 | `KH_WashSelection.cs` `_Poll` は `_PollPending()` (未適用、または保持待ちカテゴリ `_shownTools[c] != _Selected(c)`) の間だけ再予約。Start / OnDeserialization / OnOwnershipTransferred / ReqSelect から `PollIfPending`、`_polling` で一重化 | 待ちが無い tick は `_Apply` が `same` で戻っていた。Drop は遠隔では poll だけが検知するので、待ちの間は 0.25 s を維持 |
| 7 | `KH_WashSelectionBoard.cs` タイマーは常に 0.25 s の 1 本。12 m より遠く、要求待ちでも結果表示中でもない時だけ本体を飛ばして約 1 s に 1 回。操作 (`_Take` / `_Reset` / Tab / Pick / `_Next`) で `Poke` → 次の tick で本体 | 本体は不変。予約は常に 1 本 (二重・消失なし)。操作への応答 ≤ 0.25 s、遠くでの受動的な文言更新 ≤ 1 s (例外として記録) |
| 8 | `KH_WalkIntro.cs` `_resting`: Pause 経路 (完了 / 非表示 / 却下 かつ !_replay) で立ち、resting の Tick は言語と `_inside` だけ。`_OnLeashChanged` / `_Replay` / `_ToggleAuto` / `OnPlayerRestored` で解除 | 本番の `walkers` の書き込み (接続・解除・受信) は全部 `IntroChanged()` → `_OnLeashChanged` を通る。PostLateUpdate 不変 |
| 9 | `KD_DogBook.cs` ページを開いていない・12 m より遠い時は Refresh を 2 s (距離は 0.5 s ごとに見る) | 近くでは従来どおり。遠くでの一覧の言語・毛色更新 ≤ 2 s (例外として記録) |

#### Codex 独立レビュー (2026-10-12、gpt-6-astra) の採否

| 重要度 | 指摘 | 対応 |
|---|---|---|
| 中 | 掲示板の遠距離 1 s 予約が操作直後の応答まで遅らせる。ハーネス `WashPremium.cs:35` (返信後 0.45 s で次の `_Take`) が偽 FAIL になりうる | **反映**: 予約は 0.25 s の 1 本のまま、遠い時は本体だけ間引き、操作で Poke (表の 7) |
| 中 | 掲示板 (遠距離 1 s) と犬紹介 (遠距離 2 s) は厳密には挙動不変でない (文言更新の遅れ) | **例外として採用** (IMP-DOG-034 の方針「リアルタイム性より軽さ」。12 m より遠い板の文字は読めない)。記録に残す |
| 中 | `KH_WashSelection` の巡回を再開する位相が変わり、Drop の反映時刻が最大 0.25 s ずれる | **据え置き**: 旧版も Drop と tick の位相は無関係 (任意) で、どちらも Drop 後 0.25 s 以内に反映する。分布は同じ |
| 中 (推測) | 未選択スロットの休止中に、遠隔の保持 (選択変更を受ける直前の掴み直し) の履歴を失い、Drop 後に届いた洗浄通知を `_IsHeldBy` が拒否しうる | **据え置き (既知の残リスク)**: 相手の端末が選択変更を受けると `pickupable = false` で手放されるので、保持は通信のずれの間 (< 0.5 s) だけ。旧版の 0.5 s 巡回でもその長さの保持は取りこぼす。実通信で問題が出たら、休止中も TrackHolder だけ 0.5 s で回す案へ |
| 低 | カート Owner の A→B→A で保守が最大 0.25 s 早まる | **据え置き**: OwnerTick は冪等で、早まる方向だけ |
| 中 | 既存の Edit 検査・プローブは新しい早期 return・休止・再開を通らない (偽 PASS に読まない) | **記録**: 上の検査結果は回帰の確認。新しい経路は Play (ユーザー) で |
| 中 | 削減は全 47 個ではない (無料 19 個は継続、有料は未選択分だけ) | **記録** (表の 3) |
| 低 | コメントの過大表現 3 か所 (支柱の「直後」「旧 LateUpdate と同じ」、掲示板の「何も変わらない」) | **反映** |
| — | 招待 (a)・WashSelection の起床漏れ (c)・WalkIntro (d)・`_motionCount` (e)・Udon VM の成立・二重予約: 問題なし | — |

#### ハーネスの要否 (Codex 相談 2026-10-12、短い型)

結論「今は WashPremium 群を回さず、ユーザーの Play で変更経路を確認」を採用 (`P-held-category-refused` / `P-held-change-and-first-restore` は保留 → Drop → 自動反映の検証にならない)。ClientSim の Play は回していない。

#### ユーザーの Play / Quest で見てほしいこと (新しい経路。Edit 検査では通らない)

1. **Quest 3 の fps**: スポーンで何もしない時 (前回 常時 20)。期待は 10-03 の水準 (40 前後) へ戻る方向。戻り切らなければ 10-04 の景観が残りの候補
2. **洗い場**: 未選択のまま放置 → ボードで選ぶ → 道具を取って使う (休止したスロットが起きる)。道具を持ったまま別の道具へ切り替え → 置いた後に切り替わる (0.25 s 以内)。12 m より離れた所から戻ってボードを押す → 待ち表示がすぐ解け、続けて押せる。台の高さ・大きさが変わる時に支柱が付いてくる
3. **招待**: 誰も台を持たない状態からリードの先端を台のそばで持つ → 犬が寄る (早期 return からの再開)
4. **初回案内**: 完了後に設定から再表示、リードの接続・解除で案内が戻る
5. **カート**: 全車プールの状態から乗る → 動く。降りて駐車 → 再び乗る。Owner 交代 (2 人)
6. **犬紹介**: 遠くから近づいた時に一覧の表示が 2 s 以内に追いつく


## 記録

2026-10-12 (実装): C の無駄の修正 10 ファイルを実装 (上の実装の節)。C# 0 / U# 0、Edit 検査 6 本 PASS、Codex 独立レビュー反映、ハーネスは相談の結果回さず。シーン・`.asset` は未変更。Quest の fps と新しい経路の Play はユーザー待ち。

2026-10-12: ユーザー報告 (常時 20 fps) を受け、Edit モードの実体数 + Udon / 描画側の静的監査 (上の節と 2 本の記録)。Unity の設定・コード・シーンは未変更。Codex 相談 (B → C → B) を節末に記録。Play は回していない。

2026-10-06 (撤去): ユーザー指示 (配信者・宣伝動画の制作者へ渡すメディアフィックス版の準備。他のデバッグ用の物も消す) で `Mobile/22 - Remove Perf Probe` を実行し、`KH_PerfProbe` とボタン 11 個を外して保存した。読み直し: 根 0・Udon 0・NetworkIDs 258 (死んだ項目 0)・Console エラー 0。このボタンでの Quest 計測は行っていない (10-04 の 20〜40 fps 報告の切り分けは未実施のまま)。他にデバッグ用の物は見当たらない (`KH_World/KH_Dummies` の仮の犬 6 体は Terrain 試作の名残で非表示のまま。Walking の Verify が「非表示」を前提にしているので残した)。

2026-10-04 (設置): 計測用のボタンに 4 群 (NewGrass / NewBoards / NewOther / Props) を足して置き直した。Mobile/20 → 保存 → Mobile/21 PASS (163 / 0)。C#・U# コンパイル エラー 0。Leaves・Scenery の群は 10-04 の追加分を含まない (10-03 までの内容だけ)。**公開前に Mobile/22 で撤去する。**

2026-10-04 (ユーザー): 設定の写真 OFF では fps は特に変わらなかった。写真の撮影は主な原因ではない。

2026-10-04 (夜): 手順 1 の静的な棚卸しを実施し、status を wip へ。Unity は未接続、コード・シーンは未変更。

2026-10-04: ユーザー報告 (20〜40 fps) を記録し、候補と進め方を追記。priority を High へ。計測・調整は未着手 (Unity は未接続)。
