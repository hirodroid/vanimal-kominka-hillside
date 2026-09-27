---
id: IMP-WLD-001
type: IMP
system: WLD
status: wip
priority: Medium
summary: 地図の副画面、川辺・奥広場の 2 地点ワープ、インスタンス内のプレイヤーへの合流、接続犬の同行、暗転を、DES-WLD-003 の確定設計 (2026-09-27) どおりに実装する
next: 段階 1 は ClientSim (81 TRAVEL_OK、83、82 TRAVEL_OBSERVED 12 ケース) まで通過。段階 2 (地図の副画面と暗転) を実装し、PCVR 2〜3 人の #1・#2・#6・#7 はユーザーへ引き継ぐ
updated: 2026-09-27
---

# IMP-WLD-001 地図・2 地点ワープ・合流と接続犬の同行を実装する

[DES-WLD-003](DES-WLD-003-map-and-travel.md) の設計は、2026-09-27 にユーザーが確定した。その初期公開の範囲を実装する。仕様の正本は [設計書](../../plans/2026-09-27-map-and-travel.md) で、本 Issue は段階と完了の条件だけを持つ。契約の中身 (要求 ID 付きの切符、Phase の同期、本人が着いた後に 1 回だけ適用すること、共通の検査 5 つ、受付箱) は設計書 §B・§D.3 から写さない。

## 初期公開の範囲 (設計書 §0「初期公開に必須のものと、後回しにするもの」)

- 含むもの:
  - さんぽタブの副画面に地図を出す。目印は 6 地点を描くが、ワープできるのは 2 地点だけ。
  - **川辺 (`spot.river`) と奥広場 (`spot.upper`) の 2 地点**へのワープ。
  - 接続している犬が一緒に来ること。
  - 暗転 (全画面の黒、queue 5001)。
  - インスタンス内のプレイヤーへの合流 (フレンド判定は使わない)。受け付けの初期値は ON。
- 含まないもの:
  - 残り 4 地点へのワープ。追加は Play の後にユーザーが決める。`spotWarpEnabled` を変えて再生成するだけで足せるように作る。
  - 地図に全員を常時表示すること、地図の精細化、リスポーン後に切符を出すこと、一部の犬だけを連れていく操作。

## 段階と完了条件

### 段階 1: 2 地点ワープと接続犬の同行 (設計書 §B・§D.3〜D.6)

- `KH_LeashManager` に次を足す: 切符の配列 7 本 (長さ 6 で初期化)、`ReqTravel` / `CancelTravel` / `ReqTravelDone`、送り主と `dogMask` の照合、同じ犬への 5 秒のクールダウン。
- 犬の側に次を足す: `KD_DogWanderController.HillsideTravel.cs` を新設し、適用処理を 1 か所にまとめる (Return の判定より前に置く)。そこで経路の状態を初期化し、完了を報告する。
- 地点 ID の解決: Editor の生成器と Verify を作る。`spotPositions` / `spotYaws` / `spotWarpEnabled` を持たせ、共通の検査 1〜4 をかける。
- 仮の UI: さんぽタブの上段に、[奥広場へワープ] と [川辺へワープ] のボタンを置く (段階 2 で地図の面へ移す)。要求の待ち (4 秒で再送、8 秒で取りやめ)、門番、クールダウンも入れる。

完了条件:

- [ ] コンパイルのエラーが 0。`_` の規約の検査を通す。Verify で、Manager の同期配列の長さと初期値、2 地点の解決と共通の検査、`spotWarpEnabled` が PASS する。
- [ ] ClientSim ハーネス `KH_CourseWalkHarnessEditor` の travel モードで、設計書 §C.1 の T1〜T9 と T11 が PASS する。T1 は 4 構成 × 3 回の移動。T10 は段階 3 で行う。
- [ ] PCVR 2〜3 人で、設計書 §C.2 の #1・#2・#6・#7 を行う。1 人で 2 地点、Owner でない人の移動、Owner の交代、途中参加。

#### 実装 (2026-09-27)

- 変更: `KH_LeashManager.cs` (partial 化、解除で切符を消す、Update で要求の待ちと期限切れ) / 新規 `KH_LeashManager.Travel.cs` (切符 7 本、`ReqTravel` / `CancelTravel` / `ReqTravelDone`、要求する人の門番と待ち、共通の検査 5 つ、試験用の debug 値) / 新規 `KD_DogWanderController.HillsideTravel.cs` (適用処理) と `HillsideLeash.cs` の 2 行 (Return より前の呼び出し、準備中は追従テレポートを止める) / 新規 `KD_TreatPouch.Travel.cs` と `Walk.cs`・`Toy.cs` の 1 行ずつ / 新規 `Editor/KH_CourseTravelEditor.cs` / 新規 `Editor/Testing/KH_CourseWalkHarnessEditor.Travel.cs` と本体の 3 行 / `CourseUITranslations.json` に `course.travel.*` 13 キー × 13 言語。
- メニュー: `Course/80 - Build Travel Spots and Warp Buttons` (地点の解決・Manager への書き込み・上段の 2 ボタン・翻訳の入れ直し。シーンを保存する)、`Course/81 - Verify Travel Spots and Warp Buttons` (`Temp/imp-wld-001/travel-verify.json`、PASS は `TRAVEL_OK`)、`Course/82 - Walk Harness Travel (T1-T9, T11)`、`Course/83 - Walk Harness Travel Quick`。停止は `Course/41`、PlayerData の復旧は `Course/46`。
- 統括が実行する順: コンパイル (`bash scripts/unity-compile.sh --udon`) → Course/80 → Course/27 (新しい字形) → Course/81 と Course/26 → Course/83 → Course/82。ハーネスの結果は `Logs/kh-course-walk/<runId>/report.json` と `travel.json`、PASS は `TRAVEL_OBSERVED`。
- 設計からの変更: `ReqTravelDone` の上限は 6/s (1 回の移動で最大 6 匹ぶん届くため)。T9 は合流の部分を段階 3 に回し、取消と書き込みの行き違いと退出だけを行う。
- 実測 (2026-09-27、統括が実行): C#/U# のコンパイル エラー 0。Course/80 BUILT (6 地点を解決、ワープは upper / river)。Course/27、Course/81 TRAVEL_OK、Course/26 OK。Course/83 TRAVEL_QUICK_OBSERVED (T1 犬 1 匹 × 3 回の移動: 位置の変更は全頭ちょうど 1 回、Phase 1→2、到着後 30 秒の Return 0。T11 連打: 新しい reqId は 1 つ、2 回目以降はクールダウンの表示)。Course/82 TRAVEL_OBSERVED (20260927-043153: T1 の 4 構成、T2〜T9、T11 の 12 ケース PASS。T2〜T9 の Owner 交代・遅着・偽装・退出は 1 端末の模擬で、実通信・PCVR は未確認)。
- 独立レビュー (Codex、D+M / U+X+P) の反映: 移動の直前に人の検査をやり直す、送り主の犬に載っている reqId より古い要求を捨てる、T6 を Owner 交代の模擬にする、Verify は保存した川・橋の値で検査する、待ちの残り秒数・「犬を待っています」・「ワープを取りやめました: 理由」の表示 (course.travel.* に 5 キー追加)。反映後は Course/80 と 27 を流し直してから 81 → 83 → 82。

### 段階 2: 地図の副画面と暗転 (設計書 §A.1〜A.3・A.5・D.1・D.2・D.8)

- 地図のテクスチャを作る。周回路と川のデータから描き、512² を試作の初期値にする。
- 地図の面を作る: 1 列 × 2 行の一覧、固定フッター、[ここへワープ]、自分の位置と向きの表示 (10 Hz)。
- 着いた後に `PlaceInFront` をもう一度呼び、[さんぽへもどる] を出す。
- `KH_TravelFade` を作る (alpha で黒へ、queue 5001、黒を描いてから移動)。着いたときの向きも合わせる。
- 文言はコースの翻訳表と字形の追加の手順に乗せる。

完了条件:

- [ ] Verify で、面の配線・文言の収まり・欠けた字 0・テクスチャの設定 (ミップマップなし・Clamp・圧縮形式) が PASS する。容量を測って記録する。
- [ ] PCVR 1 人で、設計書 §C.2 #1 の暗転 (昼・夜・ライブ中、両眼)・向き・「ワープした後に歩き出すか」を、ユーザーが確かめる。

#### 実装 (2026-09-27、段階 2。未コンパイル・未検証)

- 変更: 新規 `KH_TravelFade.cs` (ローカルの暗転。0.2 秒で黒 → 黒を描いたフレームの次に移動 → 1 フレーム後に 0.3 秒で戻す、黒のまま 3 秒で自動で戻す) と `Shaders/KH_TravelFade.shader` (alpha で黒へ、queue 5001、ミラーでは描かない) / `KH_LeashManager.Travel.cs` (状態 3「暗転中」、`travelFade` の参照、暗転の間も取消・接続の変化・人の検査) / `KD_TreatPouch.Travel.cs` (副画面、10 Hz の自分の位置と向き、選ぶ → [ここへワープ]、着いたら `PlaceInFront` と [さんぽへもどる]) / 新規 `Editor/KH_CourseTravelMapEditor.cs` / `Editor/KH_CourseTravelEditor.cs` (Course/80 は地点と翻訳だけにし、81 から段階 2 の検査を呼ぶ) / ハーネス T1 に暗転の観測 / `CourseUITranslations.json` に `course.travel.map.*` 12 キー × 13 言語。
- メニュー: `Course/84 - Build Travel Map and Fade` (1 回目は KH_TravelFade の Program Asset を作って止まる。2 回目で地図テクスチャ `Assets/KominkaHillside/Textures/Map/KH_TravelMap.png`、暗転の板、[地図と移動] と副画面を作ってシーンを保存。`Temp/imp-wld-001/travel-map-build.json`)。検査は `Course/81` に含めた (容量は `travel-verify.json` の `mapPngBytes` / `mapGpuBytesPC` / `mapGpuBytesMobile`)。
- 統括が実行する順: C# コンパイル → Course/84 (PROGRAM_ASSET_CREATED) → `unity-compile.sh --udon` → Course/84 (BUILT) → Course/27 → Course/81 (TRAVEL_OK) と Course/26 → Course/83 → Course/82 (T1 の notes に「fade: black before the move」)。
- 設計からの変更: 地図の番号は ①〜⑥ ではなく数字 1〜6 (丸数字の字形を足さないため)。状態の 1 行は板の下端ではなく副画面の右下 (設定の枠の中では下端の Feedback が出ないため)。[人] の切替は段階 3。暗転中の他のボタンの無視はしていない (暗転中に押せるのは [ここへワープ] だけで、それは処理中として弾く)。

### 段階 3: 合流 (設計書 §A.4・A.6・A.7・D.3・D.4)

- `KH_TravelMailbox` を作る。`VRCPlayerObject` に載せ、同期は Manual で `accepting` を 1 本。`ReqJoin` / `AnsJoin` / `CancelJoin` を持つ。
- 人の一覧を作る。開いている間は並べ替えず、playerId で持つ。一人のときの表示も用意する。
- `KH_TravelNotice` を作る (名前と場所、3 m 歩いたら置き直す、手元のメニューからもう一度出せる)。
- 応答の code 4 つ、通知の間隔 20 秒、10 分の休止、この滞在中は受けない設定、設定面の「合流の申し込みを受ける」を入れる。
- 合流の着地点の候補を決める (相手が橋の上・水中・歩行中・コースの外のとき)。
- 表示名の欄に補助フォントを試作する (設計書 §A.6)。

完了条件:

- [ ] Verify で、受付箱の同期方式と配線、通知板、設定の行が PASS する。ClientSim で T10 (合流の着地点) と T9 (退出・取消の競合) が PASS する。
- [ ] PCVR 2〜3 人で、設計書 §C.2 の #3〜#5 を行う (申し込みの code ごとの文言、相手が橋の上・川床・歩行中、退出)。

### 端末と人数

- [ ] Quest 3 単体と iPhone 15 Pro で、Touch の押し分け・通知板と Focus View の重なり・暗転の見え方・表示名の字を確かめる。16 人で申し込みが重なったときの通知の間隔を確かめる。どちらも [VER-WLD-001](VER-WLD-001-quest-ios-devices.md) の実機の回と、体験会の形式の人数の回で行う。

## 関連

- [DES-WLD-003](DES-WLD-003-map-and-travel.md): 設計と決定。
- [DES-DOG-001](DES-DOG-001-leash-walking.md): リードの契約。
- [DES-UI-004](DES-UI-004-walk-destinations-and-boards.md): 地点の呼び名。
- [IMP-ENV-004](IMP-ENV-004-river-loop-course.md): 周回路のノード。地点 ID はここから引き直す。
- [VER-WLD-001](VER-WLD-001-quest-ios-devices.md): 端末での確認。
