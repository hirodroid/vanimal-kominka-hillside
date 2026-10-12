---
id: IMP-DOG-053
type: IMP
system: DOG
status: todo
priority: High
summary: リードをつけたまま犬を川へ連れて入る (DES-DOG-010 方式 B の実装)。NavMesh は変えず、川の Fetch の脚 (川の規則・先読みの泳ぎ・泳ぎの高さ) でリードの追従を川の中へ延ばし、水中のリード接続の拒否 (RC-14) を退役する
next: 入口は決定済み (§0、混在方式)。第 2 波で実装 (新しい partial HillsideLeashRiver + 退役の一覧 + ハーネスの期待の変更)。着手は DES-DOG-001 の停滞検知 (KH_LeashManager.cs) が入ってから
updated: 2026-10-12
decided: 2026-10-11
---

# IMP-DOG-053 リードをつけたまま川へ入る (実装)

設計と決定は [DES-DOG-010](DES-DOG-010-leashed-river-entry.md) (方式 B と 8 件、2026-10-11 ユーザー「すべて推奨で問題なしです」)。この Issue は DES-DOG-010「B の形」を実装の単位に分けたもの。水遊び・川の脚の既存の実装は [IMP-DOG-020](IMP-DOG-020-river-wading-and-swim.md) / [IMP-DOG-021](IMP-DOG-021-river-wide-fetch-and-recall.md)、設計書は [水遊びの設計書](../../plans/2026-09-27-river-play-and-floating-fetch.md) §B・§E。

**調査結果 (Course/130) を見て入口の作り方を決めてから着手する。**

## 0. 調査 (第 1 波で用意済み、2026-10-12)

`Tools/KominkaHillside/Course/130 - Survey Leash River Entry` (`Assets/KominkaHillside/Scripts/Editor/KH_CourseLeashRiverSurveyEditor.cs`)。読むだけでシーンを書かない (Idle の確認と、終わりに dirty でないことを記録)。出力は `Temp/kh-leash-river/survey.json` と Console の `[KH Leash River]` 要約行 (JSON の `summary` と同じ)。

| 項目 | 測るもの |
|---|---|
| 1 帯の縁と NavMesh | z 0〜720 を 2 m ごと・左右。帯の縁 (中心 ± (半幅 + `BandMargin` 1 m)) の地面から最も近い NavMesh (8 m 以内、対岸は数えない) の距離、横の符号つきの位置 (負 = 帯の中まで NavMesh がある)、帯の縁を通る横の線で NavMesh が始まる位置、最寄りのルートのノードからの完全経路、そこから水際 (中心 ± 半幅) までの地面の最大傾斜、その間の石 (layer 11、Terrain 以外)、同じ側の最寄りの ok な岸のスライス。区分 (inside / near ≤ 1 m / far / none / bridge) と、far・none の z の連続区間 |
| 「直接入れる」 | NavMesh の始まりが帯の縁から 1 m 以内・経路あり・水際まで 25° 以下 (Course/76・90 の出水の規則)・石なし・橋の区域の外 |
| 2 ok な岸のスライス | `KH_RiverData.sliceOk` の左右・どちらか側の間隔の分布、8 m を超える間隔 (橋の区域を跨ぐか)、橋ごとの上下の最寄りの ok、ok でない理由 (Course/90 と同じ `ComputeSlices` をリフレクションで呼ぶ。保存値との不一致も数える) |
| 3 水遊び場 | entry / exits / returns の帯との位置、NavMesh 上か、1 m 中心寄りの水深と `_FetchWater`、最寄りの ok スライス |
| 4 深さ | 川全体の水 (`_FetchWater`) の格子の水深の分布。犬種ごと (シーンの `waterMouthHeight` M・`waterSwimMouth` Ms・`waterSwimAllowed`、M は `KH_Jump_*.json` と照合): 歩ける上限 = 泳がない犬 min(0.85 M, Ms − 0.03)・泳ぐ犬 Ms − 0.03、泳ぎ始め min(0.75 M, Ms − 0.04)。濡れたセルのうち歩く / 泳ぐ / 届かない割合、横断できる行、泳がない犬が帯の縁から歩いて入れる幅、参考に自由行動の 0.45 M の割合 |
| 5 保持者 | 中心線で、プレイヤーの当たり (PlayerLocal と衝突する層) が河床に立つか (保持者自身が川へ入れるか = 手が水の上に来るか) |

要約の最後の行 `entry hint` は判断の材料 (直接入れる割合 ≥ 90 % なら直接を基本に例外区間だけスライス、50〜90 % なら混在、それ未満ならスライス経由)。**決めるのは実装の着手時で、ここに「決定: YYYY-MM-DD」を 1 行足す。**

決定: 2026-10-12 (統括、Course/130 の結果 run DONE findings 0 から。AI の判断): **混在方式**。帯の縁から 1 m 以内で NavMesh が始まり、経路がつながり、水際までの傾斜 25° 以下で石が無いサンプル (686 中 465 = 67.8 %) は NavMesh の縁から帯へ直接入る。それ以外 (急傾斜 28・石 65・NavMesh が帯の手前で終わる 96 区間) は最も近い ok な岸のスライス (NavMesh の始点から中央値 0.82 m、p90 2.4 m、最大 11.5 m) を通る。橋の区域 (z 190・490) は入らず、上下の最寄りの ok スライスは 9.5〜12.5 m 離れている。深さは全犬種で届かないセルが 0 (泳がないラブ・サモエドも 0.85 M で川幅を横断できる)。調査の前に Course/90 を流して岸のスライスの保存値を作り直した (再計算との不一致 33 → 0、+x の ok が 390 → 357)。

統括の手順: `bash scripts/unity-compile.sh` → `Tools/KominkaHillside/Course/130 - Survey Leash River Entry` → `Temp/kh-leash-river/survey.json` の `summary` と `findings` を読む (findings が出たら先に Course/90・91 の再構築が要るか確かめる)。

## 1. 実装の単位

DES-DOG-010「B の形」の項目ごと。場所は主なもの。新しい同期変数は作らない (位置・`waterPose`・`walkers` で足りる)。リードの川の状態は犬の Owner のローカル。

| # | 単位 | 中身 | 主な場所 |
|---|---|---|---|
| 1 | 状態機械 | 陸 (今の `LeashGo`) → 入水 → 川で追従 / 限界で待つ → 上がる → 陸。入る条件 = 濡れている、または手へ向かう次の一歩 (NavMesh の角か目標) が帯 (`_InBand(BandMargin)`) へ入る。入口は §0 の調査で決めた方式 (NavMesh の縁から直接 / `_NearestSlice` の ok な岸の点を通る)。上がる = 目標が陸に戻り、岸 (ok スライスの岸の点か、帯の外の NavMesh) へ着いた。Owner 交代は位置・水深・`waterPose` から組み直す (WaterTick の ownerEdge と同じ考え) | 新しい partial (例 `KD_DogWanderController.HillsideLeashRiver.cs`)、`HillsideLeash.cs` の `LeashGo` / Return の前 |
| 2 | 深さの規則 | 川の中の一歩は `_WaterRiverStep` と同じ: `_navStep` を立てず、`_waterRiverFrame` (川の規則) と `_waterFetchFrame` 相当の印で `_BlockedByRiver` の川の枝を通す (沈み防止 0.03 m、泳がない犬は 0.85 M、橋の区域・河床データの無い帯は入らない)。`WaterSwimLookAhead`・`WaterApplySwimHeight`・`WaterPoseRefresh` を同じ順で。**リードに 0.85 M を採るのは新しい設計の選択** (水遊び場の自由行動の 0.45 M は変えない)。印は Fetch 用と分けるか共用するかを実装時に決め、`RiverFetchNow()` の読み手 (`WaterSwimLookAhead`・`_WaterPushBlocked`) を漏らさない | `HillsideWater.cs` (`_BlockedByRiver`・`WaterSwimLookAhead`・`FetchWadeRatio`) |
| 3 | 目標の縮め方 | 目標 = 手 + 犬ごとのずらし (`_WalkingTarget`)。届かない目標 (泳がない犬の上限より深い・橋の区域・河床データなし) は、犬から目標への線上で最後に届く点へ縮め、そこで止まって保持者を向く。待つ間は停滞 (出水の監視の 0.1 m / 1 s) に数えない。同じ保持者の複数匹は犬ごとに縮める (1 匹だけ深い側に入り得る) | 新しい partial |
| 4 | 追いつき | 水中は速さを泳ぎ / 歩きの上限 (`WaterStepSpeed`) より上げない (Return の 1.25 × / 1.6 × で `dashSpeed` / `sprintSpeed` に上げない)。入水へ近づく間も全力の速さを落とす。`catchUpDistance` (10 m) を超えたら水中から `TeleportTo` しない: 保持者側の岸 (`_ExitSlice` と同じ選び方: 保持者の側で犬に近い ok スライス、無ければ反対側) へ上がり始める。上がれないときだけ既存の 3 段 (10 s・停滞 → 別の出口 → 復帰点へ瞬間移動)。上陸後は今の Return / `TeleportTo` | `HillsideLeash.cs:418-445` (Return)、`HillsideWater.cs` の出水の 3 段 |
| 5 | 引き継ぎ | 泳いでいる犬に接続したら、水遊び (`waterState`) を `WaterEnd` で明示的に終え、泳ぎの高さ (`_waterSwimming` / `_swimYValid` / `_waterSwimY`) を保ったまま川の追従へ移る (河床へ一度落とさない)。今は `preempted` で終わる (`HillsideWater.cs:358`)。Fetch 非 Idle とディスクの接続拒否は水と関係なく今のまま | `HillsideWater.cs:356-381`、`UpdateWaterPlay` |
| 6 | `WaterClearFloat` の条件 | リード処理では「乾いていて、リードの川の状態でない」ときだけ (`HillsideLeash.cs:390`)。切符は**位置を変えたときだけ**消し、適用後に `WaterPoseRefresh` (今は位置を変えない適用でも消す、`HillsideTravel.cs:78-97`)。他の呼び出し (`HillsideTreatCall.cs:98`、`HillsideWash.cs:274`・`:296`) も、川の追従中に高さを落とさないかを確かめる | 各所 |
| 7 | 水中の止まり方 | `LeashPose` は `waterPose` ≠ 0 なら伏せ・嗅ぐ・座るを選ばない (全端末。同期の `waterPose` で判断) | `HillsideLeash.cs:690` |
| 8 | 間引き | `LeashLegsOk` (`Tick.cs:454`) に「入水へ近づいている (目標か次の角が帯の手前 N m 以内)・リードの川の状態」を足し、毎フレーム判断にする。水の旗は既に毎フレームを強制している (`waterState`・`_waterSwimming`・`_swimYValid`・`_waterLastWet`)。6 匹が水中なら 6 匹とも毎フレーム (自由な水遊びと同じ費用) | `Tick.cs:395-397`・`:454-480` |
| 9 | 押し合い | 同じ保持者の犬どうしは押さない (`KD_DogPack.cs:271`、変えない)。別の保持者・自由な犬との押し合いで、リードの川の状態の犬にも川の規則を読ませる: `_WaterPushBlocked` の浮いている側 (橋の区域・河床なしの帯へ押さない) と歩いている側 (`_waterRiverFrame` を立てて `_BlockedByRiver`) の両方 | `HillsideWater.cs:270-290` |
| 10 | 自由行動の中心 | 水中で外したとき、自由行動の中心を川の中にしない (決定 8)。今は `ReqWaterRelease` だけが元の値を戻している (`KH_LeashManager.Water.cs:48-50`)。その考えを `Release` (`KH_LeashManager.cs:268-273`) へ移す。中心の候補: (a) 前の `releasedHomes` を残す (今の `ReqWaterRelease` と同じ)、(b) 犬の位置に最寄りの ok スライスの復帰点 (`KH_RiverData`、全端末が持つ静的データ)。推奨 (b) (「外す前の陸の位置」に近く、必ず陸で NavMesh 上)。外された犬は今の取り残しの規則で自分で近い岸へ上がる | `KH_LeashManager.cs`、`.Water.cs` |
| 11 | 洗い場への呼び出し | 今は水遊びの出水を待つ (`WashYieldsToWater`、`HillsideWash.cs:399`: `waterPose` か `waterState`)。リードの川の状態でも「先に上がる」を受け持つ (`HeldStation` を出水の理由にする)。`StationWaterTimeout` の救済はそのまま | `HillsideWash.cs:258-296` |

### 1.1 退役する拒否 (送る側と受ける側を一緒に)

| 場所 | 中身 |
|---|---|
| `KH_LeashManager.cs:222` | `ReqAttach` の `_InWater` で断って `_AskWaterExitFor` |
| `KH_LeashManager.Intro.cs:27` | 入口の紹介の可否 (`_IntroDogAvailable`) の `_InWater`。DES-DOG-010 の一覧に無かった所。拒否をそろえるなら外す (推奨) |
| `KD_TreatPouch.Walk.cs:137`・`:150`・`:202`、`KD_TreatPouch.Water.cs:17-25` | 手元メニューの `WalkGateWater` (10)・`WaterAsk`・文言キー `course.water.busy` |
| `CourseUITranslations.json` の `course.water.busy` (13 言語)、`KH_WalkTabLayoutEditor.cs:406` | 文言の行とレイアウト検査の参照。キーを消すなら検査の一覧からも消す |
| `KD_DogWanderController.cs:1334-1336`、`Tick.cs:164`・`:395` | `_waterLeashVeto` でリード処理を飛ばす分岐 |
| `HillsideWater.cs:348`・`:373`・`:427-468`・`:482-486`・`:1031` | `_waterLeashVeto` / `WaterLeashTick` の拒否の半分 (接続の端を見て拒否・解除を頼む)。端の検出そのもの (`_waterWasLeashed` / `_waterLeashHolder`) は引き継ぎ (#5) に使えるので残してよい |
| `KH_LeashManager.Water.cs:26-53` | `_RequestWaterRelease` (送る側) と `ReqWaterRelease` (受ける側) |

残す: `KH_LeashManager._InWater` そのもの (`KH_PhotoWalkWatcher.cs:212` の「水の中は撮らない」が使う)、移動後の姿勢の更新 (`WaterPoseRefresh`)、取り残しの出水 (水中で外された犬・保持者の退室・Fetch の終了後)、Fetch 非 Idle とディスクの接続拒否、おもちゃの呼び寄せの `course.water.busyToyBack` (`KD_TreatPouch.Toy.cs:262`)、`KH_FrisbeeCatch.cs:1246` の出水の依頼。

### 1.2 書き直すコメント・記録

`HillsideWater.cs` の先頭 (:13-16「NavMesh moves (LeashGo / GatherStep) are left as they were」・:31「A leash / toy request is refused」) と RC-14 の節 (:427-435)、`HillsideWater.cs:366-372` (「Leashed walks run on the NavMesh … RC-14 is untouched」)、`KH_LeashManager.Water.cs` の先頭、`HillsideLeash.cs:387-389` (`WaterClearFloat` の前の「no water veto」)、[IMP-DOG-034](IMP-DOG-034-dog-udon-tick-thinning.md) W1 (:198)、[IMP-DOG-036](IMP-DOG-036-leash-decision-thinning.md) の RC-14 の行 (:82・:133)、[IMP-DOG-020](IMP-DOG-020-river-wading-and-swim.md):20、水遊びの設計書の B-1 (:170・:173)・C の 10 (:316)・決めてもらうこと 3 (:448)、[地図と移動の設計書](../../plans/2026-09-27-map-and-travel.md) §F.4 (:613)。それぞれ「2026-10-11 の決定 (DES-DOG-010) で上書き」の注記を残して直す。

**game-design §3・§4 はこの Issue では書き直さない** (別セッションの作業の区切りを待つ。DES-DOG-010「次の手順」4)。完了の前に、書き直しが済んだか、別の Issue に残っているかを確かめる。

## 2. ハーネスの期待の変更

回すかは実装後に相談 (`vrc-review` 相談モード) で決める。全体は回さない。

| ケース | 今の期待 | 新しい期待 |
|---|---|---|
| 78 W4-leash | 水中でメニューから接続 → 拒否 (`walkers` -1)、出水 ≤ 1.5 s、上陸 ≤ 10 s | 接続が成立し、川の追従へ移る。拒否・強制の出水を合格条件から外す |
| 78 W4-leash-stale | 同期の姿勢を 0 に保ったまま濡れた犬へ接続 → 水深で拒否 | 接続が成立する。古い姿勢は拒否の根拠にならない |
| 78 W4-leash-late | 受理済みの接続 → 犬の Owner が拒否し解除を頼む | 接続が残り、川の追従へ移る。拒否・解除・強制の出水を外す |
| 88 S5b-sim | リードと呼び寄せを同じ tick → 呼び寄せだけ受理、リードは拒否 | 両方受理される |
| 82c C7-wet-sim | 模擬 (陸で姿勢を保持) | 実装までは模擬のまま。実装後、リードで歩いて入水 → [行く] の実際の経路のケース (C7-wet) に置き換える |
| 82b T-swimresidue-* | 陸の浮きの取り残しを消す | 残す。加えて、水中の正当な浮き (リードの川の追従中) を切符の位置を変えない適用で消さない逆向きの検査を足す |

## 3. 最小の検証

Play の前に Edit で: 目標の縮め方と入口の選び方を、実装と同じコードを呼ぶプローブで確かめる (Course/130 の格子・岸のデータの上で、犬種ごとに「手が対岸 / 深い所 / 橋の区域にあるときの縮めた目標」と「入口」を表にする。既知の拒否例 = 橋の区域・河床なし・泳がない犬の深すぎる所を入れる)。

実装後の動的な確認 (相談で選んだものだけ):

- 泳ぐ小型犬 1 匹: 岸 → 入水 → 浮いて止まる → 同じ岸へ上がる
- 泳がない大型犬 1 匹: 限界で待つ → 保持者が離れる → 上がって追う (10 m を超えても水中で瞬間移動しない)
- 泳いでいる犬への接続 (水遊び場の中 / 外): 河床へ落ちない
- 橋の区域と使えない岸: 帯へ入らない・別の岸から上がる
- 泳いでいる犬の切符 (位置を変える / 変えない)
- 水中で外す: 自由行動の中心が陸、犬は自分で上がる
- 6 匹の混在、保持者 ≠ Owner、途中参加、退室は PCVR の 2 人以上 (実機チェックリストへ)

## 4. 完了条件

- §0 の入口の決定が記録されている
- §1 の 1〜11 と退役の一覧が実装され、§1.2 のコメント・記録が直っている
- コンパイル 0 エラー (`bash scripts/unity-compile.sh --udon`)、Edit のプローブが期待どおり
- §2 の期待が直り、相談で選んだ群が通る
- 独立レビュー (`vrc-review`) の指摘を反映
- PCVR の確認項目が実機チェックリストにある (見え方・2 人以上)
- game-design §3・§4 の書き直しの行き先が決まっている (済み、または別の Issue)

## 記録

- 2026-10-12: 起票 (DES-DOG-010 の第 1 波)。Edit 調査の道具 Course/130 を追加 (未実行。統括が流す)。ランタイムのコードは未変更。
