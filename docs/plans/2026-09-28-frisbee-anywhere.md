# フリスビーをどこでも — 設計書 (DES-DOG-005 段階 3)

> 対象: DES-DOG-005

2026-09-28。[DES-DOG-005](../development/issues/DES-DOG-005-frisbee-anywhere.md) の段階 3 (どこでも) の設計。**実装していない。** 数値の係数は試作値で、PCVR で決め直す。段階 1 は [IMP-DOG-022](../development/issues/IMP-DOG-022-frisbee-takeout-area.md) で先に入れる。段階 2 (平らな場所の一覧) は行わない。

用語 (帯・輪切り・安全な岸・河床格子・`waterPose`) は [川の水遊びの設計書 §E](2026-09-27-river-play-and-floating-fetch.md#e-川のどこでも回収-2026-09-28) と同じ意味で使う。同書 §E-2 の水遊び場の漂流は、:413 (統合しない) と現行コードを正本とする。

改訂 (2026-09-28): Codex の独立レビューを反映した。対応は末尾の表にある。

## A. 目的と決定 (2026-09-28 ユーザー決定)

- **進め方**: 段階 1 (IMP-DOG-022) を先に入れ、そのあと段階 3 へ直接進む。段階 2 (平らな場所の一覧) は作らない。
- **川での跳び方**:
  - 犬は水の中からは跳ばない。
  - 岸で踏み切って川へ着地する (ドボン) のはよい。
  - ドボンしたあとは、安全な岸の輪切りへ泳ぐか歩き、ディスクを持ち帰る。岸の選び方は §E-3 のボールと同じ。投げた人の側で 30 m (z 方向) 以内にある安全な岸を優先し、無ければ反対側でいちばん近い岸、それも無ければ入った岸へ上がる。
- **川に落ちたディスク** (取り損ね・川へ投げた) は浮く。犬は IMP-DOG-021 のボールと同じ規則で、水の中へ取りに行く。後の IMP に回さず、今回入れる。
- **跳べない場所** (既定値。Inspector の欄にし、Course メニューが書く。PCVR で調整する):
  - 跳ぶのは、傾き ≤ 15° かつ踏み切りと着地の地面の高さの差 ≤ 0.5 m のときだけ。それ以外は走って地面から拾う。
  - 橋の床の上では跳ばない (拾うのはよい)。
  - 崖・木立・4 m を超える段差の先へは、走り込みも断り、狙いに × を出す (ディスクは飛ぶ)。
- **置き換わる既存の決定** (M4 で、各 Issue の節末に「決定: 2026-09-28 DES-DOG-005 で置換」と書く):
  - [DES-DOG-002](../development/issues/DES-DOG-002-toy-behaviour.md):21「遊び場は広場」と輪
  - [IMP-DOG-012](../development/issues/IMP-DOG-012-frisbee-six-dogs.md):27 の範囲外「斜面でのジャンプ」
  - 試作計画の P2 / Codex D1 (円で着地を判定する)
  - [IMP-DOG-020](../development/issues/IMP-DOG-020-river-wading-and-swim.md) 段階 7 (ディスクは必ず岸へ戻す)
  - [IMP-DOG-021](../development/issues/IMP-DOG-021-river-wide-fetch-and-recall.md):47 (ディスクの `waterPose` による拒否を外した)

## 0. 調査で確かめた事実 (追加分)

行番号は 2026-09-28 の `main`。

| # | 事実 | 場所 |
|---|---|---|
| 0-1 | 範囲外へ投げてもディスクは**飛ぶ**。断るのは犬だけ (灰色・×・一言)。Issue の「投げても飛ばず」は誤り | `KH_Frisbee.UpdateAim` :401 |
| 0-2 | B・C の計画は 3 点を別々に扱っている。キャッチ直下の `standRoot` を `GroundOk` にかけ (:562)、しゃがみ開始 `crouchStart` (:547) を `LaneClear` にかけ、踏み切り (`crouchStart + fwd·vTake·crouch`、:265) から `LandingOk` を計算する。基準の高さはディスク直下の地面 (:406) で、弧は実際に踏み切った `_groundY` から上がる (Motion :453)。この食い違いは ±0.25 m に抑えてあり、`catchTolerance` 0.30 の内側に収まっている | Planner.cs、Motion.cs |
| 0-3 | 毎フレーム `DogMayGo` を呼ぶ所は 4 か所: PlanNone の追従 (Motion :228、予測位置と `_landing` の 2 回)、`UpdateLand` :511、走り抜け :574、`UpdateRetrieve` :532。KeepPlan (:153・:156) は 10 Hz でレイを撃たない | Motion.cs、Planner.cs |
| 0-4 | Wander の「忙しい」分岐は、`IsWaterFetching` でないとき毎フレーム `_speed = 0` にしたうえで早期 return する。`AnimatorYielded()` の間は Animator.speed を書かない (Shared.cs:395)。非 Owner のボールは、Fetch が `_WaterFetchHold(carrying)` を呼んで足を動かしている (Fetch.HillsideWater.cs:219) | `KD_DogWanderController.cs` :1337-1374 |
| 0-5 | `CanPlay` は `waterPose` を見ない。水遊び中の犬が投げを受けると、水遊びは `preempted` で終わり、犬は川床へ下ろされる (今ある穴) | `KH_FrisbeeCatch.cs` :1031 |
| 0-6 | 水遊び場の中のディスクは、浮きも岸戻しもされず川床に残る (`InRiverBand` が場を除いている)。`EndFlight` は帯の中で `BankFor` を先に通す (:517) | `KH_Frisbee.cs` |
| 0-7 | `ClaimDisc` は `SetOwner` の直後に `_Caught()` を呼び、取得を確かめずに保持の状態へ進む (:877)。ボール側にある取得待ち (`ClaimTick`) と保持の見張り (`HoldTick`) は無い | `KH_FrisbeeCatch.cs` |
| 0-8 | 泳ぐ高さは `水面 + swimClear + (carrying ? carryExtra : 0) − Ms − swimSink`。`max(河床, …)` は付かない。泳ぎの始まりには高さの補間 (`_swimYValid`、`_swimSettled`、1.2 m/s) がある | Wander.HillsideWater.cs:563 |
| 0-9 | `LocalNearDiscArea` は半径 0 を無制限として扱う (:131)。Walking/5 は :596 (中心・半径)、:683 (Ready ≤ 半径)、:688-693 (`LandingOf` と `InCircleXZ`) を検査する。Frisbee/5 Verify も :876-890 で同じ検査をしている | ToyRecall、Editor |
| 0-10 | `LaneClear` は始点 + 0.3 m から**水平に**撃つ。緩い上り坂では地面に当たる (:252)。空中の弧は障害物を検査しない (Motion :452) | Planner.cs |
| 0-11 | メニューの描画は 6 行それぞれで `_GateToy` を呼ぶ (`KD_TreatPouch.Toy.cs` :230・287-288) | — |

## B. 地面基準のジャンプ

高さ・口の位置・犬種別の表 (`KH_{Jump|Twist}_{Breed}.json`) は犬と地面に対する相対なので、**取り直さない**。

### B-1. 4 つの点

| 名前 | 定義 | 地面 |
|---|---|---|
| C0 しゃがみ開始 | `crouchStart` (A は S と同じ) | 経路検査の終点 |
| T0 実踏み切り | `C0 + fwd·vTake·crouch` | `takeoffY`、傾きを見る |
| P0 頂点直下 | キャッチの根元 `catchRoot` の xz | 見ない (キャッチは口とディスクの 3D 距離 `EdgeGap` で判定する) |
| L0 着地 | T0 + 水平速度 × (tUp + tDown) | `landY`、傾きを見る |

`JumpGroundOk(p)` はレイを 1 本撃ち、高さと法線から傾きを取る (傾き = acos(normal.y))。条件は次のとおり。

- T0 と L0 の傾きがどちらも ≤ `jumpMaxSlopeDeg` 15°
- \|landY − takeoffY\| ≤ `jumpMaxRise` 0.5 m
- 頂点が着地の地面より 0.10 m 以上高い
- T0・L0 が橋の床 (`_OnBridgeDeck`) でない
- T0・L0 が帯 (`_InBand(p, BandMargin)`) の外 (M3 で L0 だけ D-2 に緩める)

立ったまま取る (D) と追い抜きざまに取る (D') にも、立ち位置に同じ傾きの条件をかける。15° を超える所では `PlanRetrieve` (走って拾う) しか選ばない。

### B-2. 循環の解き方 (有界反復)

`rise → tUp → C0 → T0 → takeoffY → rise` と `tDown → L0 → landY → tDown` は循環している。これを最大 2 回の反復で解く。

1. `FindBand` は、帯を `[lo − jumpMaxRise, hi + jumpMaxRise]` に広げた粗い篩にする (ディスク直下の地面を基準にするので、高低差のある所で取りこぼさない)。
2. 候補ごとに、ディスク直下の地面で `rise₀` → `tUp₀` → C0・T0 を出す。T0 にレイを撃って `takeoffY₁` を得て、`rise₁ = d.y − takeoffY₁ − _mouthCatch.y` を計算する。`rise₁` が [lo, hi] の外なら捨てる。`rise₁` で C0・T0 を出し直してもう 1 本撃ち、\|takeoffY₂ − takeoffY₁\| > 0.10 m なら棄却する。
3. 着地は `tDown₀ = tUp` で L0 にレイを撃ち、`landY₁` → `tDown₁` → L0 を出し直してもう 1 本撃つ。\|landY₂ − landY₁\| > 0.10 m なら棄却する。**採用するのは tDown₁・L0(tDown₁)・landY₂** (最終位置で撃ったレイの値)。
4. 棄却されると跳ばず、フォールバック (D → D' → 拾う) へ進む。

**KeepPlan** (10 Hz) では、C0 が前回レイを撃った点から 0.5 m 以上ずれたときだけ、T0 と L0 を撃ち直す (最大 4 本。手順 2 の後半と 3 の後半)。ずれていなければ値を使い回す。:153・:156 の `DogMayGo` はこの結果で置き換える。

**FineTune** (A・D の待機中) では、S のレイから `takeoffY` を取り直して `catchRoot.y = takeoffY + rise` とする。rise が帯の外に出たら `DropMode("ground")`。レイは今の `GroundYAt` の 1 本と置き換えるので、本数は増えない。

### B-3. 実踏み切り (Motion)

- しゃがみの間は今のまま `SnapToGround` で `_groundY` を撃ち直す (:418)。
- 足が離れる瞬間 (`!_airborne` の枝、:425-450) に、補正済みの水平速度 `vel` (:431) を使って次を計算する。
  - `vy = (_catchRoot.y − _groundY) / tUp + ½·g·tUp`。**計画上のキャッチ時刻** tUp に、口の世界 Y が計画どおりになるように出し直す。実際の放物線の頂点は `vy / g` で、踏み切りの地面が計画とずれると tUp と一致しない (平地では一致する)。
  - 着地時刻は `ta_land = (vy + √(vy² − 2g(landY − _groundY))) / g`。着地点は `start + vel·ta_land`。
  - `landY` は B-2 手順 3 と同じく 2 回撃って決める。残差が 0.10 m を超えたら、今の「liftoff refused」の経路へ進む。
- 空中の高さは `pos.y = _groundY + vy·ta − ½g·ta²` とし、`ta ≥ ta_land` になったら `landY` に置いて Land へ移る。

### B-4. クリップの尺

- `animSpeed` は**計画上のキャッチ時刻 (ta ≥ tUp。クリップのキャッチの齣。物理的な頂点 `vy / g` とは限らない) で 1 回だけ**書き直し、`_animScale · tUp / (ta_land − tUp)` にする。差が 0.02 s 以下なら書かない。
- キャッチが成立して JumpCarry へ移ることとは切り離す。キャッチは頂点の前後 `catchWindow` の間に起きるので、成立の瞬間には書かない。
- 着地の回復時間は、`UpdateLand` の `_recoverDur / _animScale` を `_recoverDur / animSpeed` に変える (同じ係数で直す)。Idle へ戻るときに 1 に戻す (今のまま)。
- 同期変数は増えない。書き直すたびに、既存の `animSpeed` の直列化が 1 回増える。非 Owner は、状態が同じまま届いた `animSpeed` を反映できる (:913・:938)。

### B-5. 地面が取れないとき

`GroundYAt` のレイが外れたら (4 m を超える段差・崖の下)、今のように犬の地面で代用せず `NoGround` を返す。その時刻の候補は捨て、着地の予測が外れたら `_landingValid = false` とする。

### B-6. M1 実装で変えた点

決定: 2026-09-28 M1 実装で変更 (Codex M1 レビュー 1-1・1-2・6-1・6-2 を反映)

- **帯の拡張 (B-2 手順 1)**: まず犬の帯 [lo, hi] で走査して評価する (平地の採用結果は今と同じ)。ディスク直下の地面の高低差が 0.05 m を超える (またはレイが外れる点がある) とき、**または犬の帯で候補が 1 つも無いとき**に、[lo − jumpMaxRise, hi + jumpMaxRise] で走査し直して追加評価する。追加の候補は点数が上回るときだけ入れ替わる。
- **A (回り込み)** の T0 は S。S の xz は rise に依存しないので、T0 = L0 = S のレイは 1 本。経由点 V は地面を実測してから lane を撃つ。
- **B・C** は T0 の反復のあと、C0 の地面を実測し (1 本)、その高さから `LaneClear(犬, C0)` と `LaneClear(C0, L0 + 0.6 m)` を撃つ (C-1 の斜面の lane の両端は実測値)。
- **離陸直前の実位置**: 足が離れる瞬間に、実際の踏み切り位置で `JumpGround` (傾き・橋の床・帯と水遊び場) と `fetch.WaterPose == 0` を確かめる。不適合なら既存の liftoff 拒否 (`liftoff refused: take-off (理由)`) へ進む。しゃがみ中の移動は `SnapToGround` だけで、他の門を通らないため。
- 跳躍の T0 / L0 と、D・D' の立ち位置は、帯と**水遊び場の両方**を拒否する (`InJumpWater`)。走る・立つ判定 (`PlayableAt` / `PathOk` / Ready) は、水遊び場を除いた帯だけを拒否する (`InRunRiver`)。水遊び場で走れる M1 前の例外 (`RiverBlocks`) はそのまま。浮きは M2。
- KeepPlan の撃ち直しは T0 1 本 + L0 2 本で、lane は撃たない (lane は採用時に判定済み)。

## C. 到達の可否

### C-1. 判定の関数 (犬と照準で共有する。`KH_FrisbeeCatch` の public メソッド)

| 関数 | 定義 |
|---|---|
| `PlayableAt(p)` | レイ 1 本: `p + up·0.5` から 4 m の範囲に地面があり、傾き ≤ `runMaxSlopeDeg` 25°、帯の外 |
| `PathOk(from, to)` | from→to の直線を 1 m 間隔でたどり、各点で地面へレイを撃つ。レイが外れるか、隣り合う点の高低差が `runMaxStep` 0.5 m を超えたら不可。25 m で打ち切り (最大 26 本)。崖の上面・段差・4 m を超える落差はここで落とす |
| `LaneClear(from, to)` | **斜面に沿わせる**: 始点の地面 + 0.3 m から終点の地面 + 0.3 m へ撃つ (両端の地面は既に撃ったレイの値を使う) |
| 到達可能 `Reachable(L, dog)` | 陸: `PlayableAt(L) && PathOk(犬, L の真下の地面)`。水 (M2 以降): D-1 の「浮く」が真で、泳がない犬なら `_DepthAt(L) ≤ _WaterFetchWadeLimit()` |

- NavMesh は使わない。犬の足は `SnapToGround` のレイで動く。NavMesh は帯を除外しており、`SamplePosition` の 2 m の吸着は崖の縁で誤って合格を出す。
- 木立は `LaneClear` と飛行の `EndWall` で扱う。Collider の無い木を Course/93 が数える (I-3)。

### C-2. 置き換えとレイの予算 (1 匹あたり)

| 呼び出し元 | 変更後 | 頻度 | 新しいレイ |
|---|---|---|---|
| `CanPlay(at, requester)` (`ReqCatch` は thrower、Idle→Ready は holder の ID を渡す) | `PlayableAt(at)`。0.25 s ごとに 1 回だけ撃ち直す。加えて `fetch.WaterPose != 0` なら不可にし、`fetch._AskWaterExitFor(requester)` を 1 秒に 1 回送る | ≤ 4 本/s | 4/s |
| Solve の候補 (手順 2・3) | スコアで勝つ候補にだけレイを払う (今の作法)。1 候補あたり T0 に 2 本、L0 に 2 本 (A は T0 = S なので 1 本) | Solve 1 回あたり | 1 回の Solve で上限 `SolveRayCap` 200 本。超えたら、その時点の最良候補で打ち切る |
| 採用した計画 | `PathOk(犬, L0 か _landing)` を 1 回 | 計画ごと | ≤ 26 |
| KeepPlan | B-2 (ずれたときだけ) | 10 Hz | ≤ 4 |
| PlanNone の追従 (0-3、:228) | `_landing` の到達可否は、Plan (10 Hz) で `_landingReachable` として計算しておき、それを使う。予測位置の判定は外す (途中の段差は歩幅の門が止める) | 毎フレームは 0 | 0 |
| `UpdateLand`・走り抜け・`UpdateRetrieve` | 円の判定を外す。`MoveToward` の歩幅の門で止める。`SnapToGround` が外れるか、1 歩の高低差が 0.5 m を超えたら `_groundStop` → `CancelLaunch("ground")`。今あるレイを見るだけ | 毎フレーム | 0 |
| 飛行の終わりのディスク位置 | `Reachable(disc)` | 1 回 | ≤ 27 |

Solve は DropMode・期限切れ・踏み切りの拒否・ミスのたびに走り直す。`SolveRayCap` があるので、Solve 1 回あたりの上限は 200 本で抑えられる。回数は Diagnostics の新しいカウンタ `_planRays` と `_solveRuns` で数え、ハーネスの sixdogs で `_planScans` / `_planEvaluations` と並べて読む。**毎フレームの地形の走査は足さない。**

### C-3. Ready の立ち位置

`ReadySpot` で円の中へ丸めていた処理 (Motion :114-118) を置き換える。次の順に `PlayableAt` を試し、最初に通った所に立つ (10 Hz の立ち位置の更新のときだけ。最大 3 本)。

1. 今の式の立ち位置
2. 同じ向きで距離を 0.5 倍にした所
3. 持ち手の横 1.5 m

3 つとも失敗したら Idle へ戻し、`_readyRetryAt = 今 + 1 s` とする。Idle の分岐はこの時刻まで Ready に入らない (毎フレームの行き来を防ぐ)。

### C-3b. M1 実装で変えた点

決定: 2026-09-28 M1 実装で変更 (Codex M1 レビュー 1-3・5 を反映)

- **PathOk の段差は上りと下りで違う**: 上り ≤ `runMaxStep` 0.5 m、下り ≤ `RunMaxDrop` 1.0 m (定数)。A の「4 m を超える段差の先へは断る」と T-down08 (0.8 m は降りられる) を両立させる。歩幅の門も同じ (上り 0.5 / 下り 1.0、地面が無ければ止まる)。
- **PathOk のレイ**: 各点で、前の地面 + (`groundRayDistance` 4 + `groundRayOffset` 0.5) = 4.5 m から下へ、前の地面 − 1.0 m まで撃つ。当たった高さが前の地面 + 0.5 を超えれば不可。始点を高くするのは、始点が Collider の中にあるとその Collider に当たらない (Unity の仕様) ため、箱や崖の天面を取り逃がさないようにするため。25 m を全部たどれたときは、終点の地面の高さと歩いた地面の高さの差も同じ範囲に収まることを求める (箱の天面にある目標を、箱の根元まで歩いて「着いた」としない)。`Reachable` の終点は `PlayableAt` のレイが当たった地面。
- **SolveRayCap 200 の集計範囲**: 1 回の Solve で**候補の置き場所に撃つレイ** (T0・L0・C0・V・lane) だけを数える。レイを撃つたびに予算を確かめ、使い切ったらその候補は `cap` で捨てる (上限を超えない)。帯の走査 (`GroundYAt`) は数えない (平地でも既に 120 本を超えるため)。ハーネスは `_solveRayMax` を読む。
- **カウンタの集計範囲**: `_planRays` は計画器が実際に撃ったレイの総数 (帯の走査、PlayableAt、PathOk は途中で失敗したらそこまでの本数、照準の判定を含む。歩幅の門の `SnapToGround` は既存のレイなので数えない)。`_solveRuns` は Solve の回数、`_solveRayMax` は 1 Solve の置き場所レイの最大。
- **Ready のレイの範囲**: 立ち位置は持ち手の足の高さから測った点なので、斜面では地面が上下にずれる。立ち位置の `PlayableAt` は持ち手の足 + 3 m から 6 m 下まで撃つ (C-3 の 3 点それぞれ 1 本)。
- **Return で段に阻まれたら Drop**: 持ち帰りの途中で歩幅の門に止められたら、その場で Drop へ移ってディスクを置く (返却の時間切れまで立ち尽くさない)。Chase・Retrieve・Pickup で止められたときは `CancelLaunch("ground")`。
- 行けない着地点を追わないときの取消の理由は `cannot-play` (旧 `out-of-area`)。

### C-4. 狙いの表示 (持っている人の端末だけ)

- 輪は Course/92 が GameObject を非表示にするだけ。捕り手は実行時に円を読まない。`HasArea` は輪の表示にだけ残し、M4 で消す。
- **×** は `!Reachable(予測着地点, 犬)` のときに出す。判定は 10 Hz で、結果を使い回す。`PathOk` を含めて ≤ 28 本 × 10 Hz。持っている人 1 人だけのコスト。
- 投げる位置の判定は別にする。`!PlayableAt(p0)` なら × と同じ表示を出す。
- 一言は「犬はここへ行けません」 (`AimHint`。Course/92 が本シーンの 6 枚の TMP に書く)。跳べないが拾える所 (15〜25°、橋の床) では何も出さない。

## D. 川

### D-1. 判定を分ける

| 判定 | 使う関数 | 範囲 |
|---|---|---|
| 浮く (ディスク・ボール) | `riverData._FloatsAt(p)` \|\| `waterSpot._FloatsAt(p)` | \|x − 中心\| ≤ 半幅、橋の帯の外、河床あり、中心が水面 + 0.3 m より下、同じ区間の z ± 30 m に安全な輪切り |
| 犬が入れる | `_FetchWater(p)` | 帯 (半幅 + 1 m)、橋の帯の外、河床あり |
| ドボンの着地 (M3) | 「浮く」が真 かつ 泳がない犬なら `_DepthAt ≤ 0.85 M` | 本物の水だけ。帯の縁の坂は含まない |
| 岸戻し (今の規則) | 帯の中で、浮かない所 | 橋の帯・岸の無い区間・帯の縁の坂 |
| 踏み切り | 帯の外 かつ `waterPose == 0` | 水の中からは跳ばない |

「ボールと同じ」とは、次の 3 点を指す。浮く領域、岸の選び方 (A)、時間の上限の式 (§E-3)。待ち時間の 3 s (`RiverBlockWait`) も同じにする。

### D-2. 浮き (M2) — `KH_Frisbee`

- 共通の判定 `FloatsHere(p)` = D-1 の「浮く」。この判定を 3 か所で使う。
  1. `EndFlight`: 帯の中でも `FloatsHere` なら `BankFor` を通さない (0-6 の順を変える)。
  2. 飛行中: `_InBand(next, 0)` → `next.y < _SurfaceY` → `FloatsHere` の順に安い判定から調べる。真なら水面で `EndFlight(…, EndLanded)` にする。理由の値は増やさない。
  3. `CheckRiver` (Owner、0.25 s ごと): `FloatsHere` なら浮かせる。そうでなく帯の中なら今の岸戻し。浮いていたのに条件を外れたら重力を戻す。
- 浮かせ方はボールと同じ。`VRCObjectSync.SetGravity(false)` にし、速度で `水面 − radius·floatSink` を保ち、角速度 0 で水平にする。
- **漂流の共有**: `KH_RiverData._DriftVelocity(Vector3 p, float drift, bool inSpot, float spotDownstreamZ)` が水平の速度を返す。
  - 場の中: `p.z > spotDownstreamZ ? −drift : 0`。x は 0。
  - 場の外: `_DriftStops` なら 0。それ以外は中心線に沿って流し、半幅 − 0.3 m の内側へ寄せる。
  - `drift` は呼ぶ側が `waterSpot.floatDrift` (無ければ `KH_WaterSpot.DefaultFloatDrift`) を渡すので、参照は変わらない。
  - `KH_ToyRecall.FloatBall` もこれを呼ぶ。この変更は純粋な切り出しで、88 のボールのケースで変化が無いことを確かめる。場の中と外の分岐を保てない実装になったら共有をやめ、`KH_Frisbee` 側に最小の写しを置いて、その旨をここへ書く。
- 重力を戻す所: `OnPickup`・`_Caught`・`_Release`・陸での `EndFlight`、および見回りのたび (RC-09 と同じく物理の状態と照らし合わせる)。
- **放した後の復旧**: ディスクの Owner の `CheckRiver` は、次をすべて満たすディスクを kinematic から外す (ボールの `UnfreezeIfFree` と同じ)。飛んでいない、手に無い、`catcher.IsCarrying` が偽、kinematic のまま。
- 計画器の `GroundYAt` は、`FloatsHere` の点に限り `max(レイ, 水面)` を返す。帯の中でも一律にはしない。

### D-3. ドボン (M3)

- 着地点 L0 が D-1 の「ドボンの着地」を満たすときだけ、帯への着地を許す。
- `landY`:
  - 泳ぐ犬で、`_DepthAt(L0) ≥ WaterSwimEnterDepth` のとき: `水面 + swimClear + carryExtra·(キャッチ済み) − Ms − swimSink`。新しい public `wander._WaterSwimRootY(p, carrying)` にする。式は :563 と同じで、`max` は付けない。
  - それ以外: 河床。
- 着地のフレームで `wander._WaterBeginAt(p, carrying)` を呼ぶ。深ければ `_waterSwimming = true`、`_waterSwimY = p.y`、`_swimYValid = true`、`_swimSettled = false` にする。このあと補間が水面の高さへ寄せる。
- Land / LandCarry へは移らない。キャッチ済みなら 12、外したなら 11 へ直接移る (今の Land は帯で `river-band` を立て、水中にディスクを放してしまう)。

### D-4. 水中の状態 (M3) — `KH_FrisbeeCatch.Water.cs`

`state` に 11・12・13 の値を足すだけで、同期変数は増えない。**KD_DogFetchController には渡さない。**

- Fetch はボール専用 (`_ballT`、`ClaimTick`/`HoldTick`、`_BallInMouth`) で、渡すと `fetch.IsIdle` の排他が崩れる。
- 足は Wander のものを借りる。
- Fetch の `RiverPlanEntry` / `RiverPlanExit` / `NavPathLength` を `KH_RiverData` へ移し、両方から呼ぶ。
  - `int _EntrySlice(Vector3 dog, Vector3 target)`: 安全な輪切りの code、無ければ −1。左右それぞれで target に最寄りのもの (z ± reach)。NavMesh の経路が短い方を選ぶ。
  - `int _ExitSlice(Vector3 dog, Vector3 thrower, int fallback)`: A の優先順位で選ぶ。
  - `float _NavPathLength(Vector3 a, Vector3 b)`: m 単位。完全な経路が無ければ −1。
  - 水中の速さは呼ぶ側で計算する: `max(0.2, (泳ぐ ? _WaterSwimSpeedNow() : walkSpeed) − drift)` m/s。

| 状態 | 入口 | Owner の動き | 出口 |
|---|---|---|---|
| 11 WaterChase | 浮いているディスク (遷移表) | `_WaterRiverStep(disc, false, entry, entered)` で追う。口がディスクから水平 0.25 m 以内、かつ −0.1 < 口 − ディスク < 1.0 m (ボール :262 と同じ) なら `SetOwner`。**取得を確かめるまで 11 のまま** (最大 2 s)。取れたら `_Caught` → 12 | 2 s で取れない → `claim-failed` GiveUp。上限時間 → `water-timeout` |
| 12 WaterCarry | ドボンでキャッチした / 11 で取った | `_WaterRiverStep(exit, true, exit, true)` | 出水点で深さ ≤ 0.02 → 13 |
| 13 WaterExit | — | `_WaterRiverHold(true)` で 1.2 s → 投げた人が同じ岸にいれば `_WaterFetchLandTo(thrower)` で 3 m まで運ぶ → Return (8)。対岸・経路が無ければ復帰点で Drop (9) | — |

- **保持の見張り (12・13・Return・Drop)**: 毎フレーム `IsOwner(disc)` を見る。失っていたら (誰かが手に取った等)、ディスクの物理には触れずに `CancelLaunch("hold-lost")` とする。ディスクの新しい Owner が D-2 の復旧で後を引き継ぐ。
- **犬の Owner が交代したとき**: 今の `owner-change` → GiveUp を使う (:994)。新しい Owner は、口にあったディスクを `ReleaseDisc` で放す (浮く)。濡れた犬は Wander の取り残しの規則で岸へ上がる。
- **表示 (全端末)**:
  - Owner: 足は `_WaterRiverStep` / `_WaterRiverHold`。
  - 非 Owner: 毎フレーム `wander._WaterFetchHold(carrying)` を呼ぶ (Fetch :219 と同じ。Walk・Run は観測した速さから)。
  - Animator.speed は KH_FrisbeeCatch の `ApplyAnimatorSpeed` が書く。11〜13 の間は `_speed` を全端末で `ObserveSpeed` から取る。Wander は `AnimatorYielded()` の間 speed を書かない。
  - 自前の Walk・Run の Bool は書かない。入るときと出るときに `wander._InvalidateLeashAnimation()` を呼び、`_outputValid = false` にする。
  - 口への貼り付けは `PostLateUpdate` で行う (`IsCarrying` に 12・13 を足す)。
- **Wander 側の例外**: 忙しい分岐の `_speed = 0` (全端末) と `_WaterPushBlocked` の水中の例外に、`frisbeeCatch.IsWaterBusy` (state 11〜13) を足す。

**11 への遷移表** (Chase・Retrieve・Land で見る。対象はディスク):

| ディスクの状態 | 動き |
|---|---|
| まだ飛んでいて、犬が帯で止まった | 最大 3 s 待つ。着水して浮いたら 11、陸で止まったら今の規則 |
| 浮いている (`FloatsHere`) | `_EntrySlice` で入口を決めて 11 (泳がない犬で深ければ `deep-disc` で諦める) |
| 11 の最中に水から出て、最後に浮いていた位置から 2 m 以内に止まった | Retrieve (陸) へ (ボールの「近くの陸なら陸の追跡」と同じ) |
| 11 の最中に水から出て、2 m より遠い (呼び寄せ・岸戻し) | `target-lost` → GiveUp。Wander が犬を岸へ上げる |
| `EndGrabbed` を受けた | 今の `flight-ended` と同じく取り消す |

### D-5. メニューの門 (M3、`KH_ToyRecall._GateToy` のディスク)

判定は上から順に行う。

| 順 | 条件 | 結果 |
|---|---|---|
| 1 | Unwired / Pending / OutOfArea / Held | 今の順のまま |
| 2 | 捕り手が 12・13 (口にある) | `InWater`。IMP-DOG-021:47 の前提が崩れたので戻す |
| 3 | 捕り手が 11 | 次の行へ進む (DogBusy にしない) |
| 4 | それ以外で `IsEngaged` | `DogBusy` |
| 5 | BallInUse → Cooldown | 今のまま |

### D-6. 岸戻しの残る所 (M2)

橋の帯、安全な岸が無い区間、帯の縁の坂 (D-1)。Course/89 に「水の中の点では `FloatsHere` が真で、岸へ戻さない」「帯の縁と橋の帯では戻す」の検査を足す。

## E. 同期

**新しい同期変数は無い。** 所有権の流れも変わらない (投げた人の `_Launch` → 犬の Owner へ `ReqCatch`。ディスクは `ClaimDisc` と `ReleaseDisc` だけで動かす)。

| 何 | 書く人 | 他の端末に見えるもの |
|---|---|---|
| 根元 (斜面・非対称の弧・水面) | 犬の Owner | `dogRoot` の VRCObjectSync |
| `state` 11〜13・`animSpeed` (頂点で 1 回) | 犬の Owner (Manual) | 状態に応じた表示。足は D-4 の非 Owner の経路 |
| `waterPose` | 犬の Owner (Wander の足) | しぶき・揺れ・リードと呼び寄せの門 |
| 浮き (重力オフ・速度) | ディスクの Owner | VRCObjectSync (重力も同期される)。途中参加の人にも見える |
| × と一言 | 持っている人だけ | — |

## F. 段階 1 との関係 (M4)

- M1〜M3 の間は `playAreaRadius` を 0 にしない。取り出しは今の「円 + 5 m」のまま。持ち出したディスクは、どこでも投げられる。
- M4 で門を `discCatchers[row]._PlayableAt(自分の位置)` に置き換え、そのときに半径を 0 にする。
  - 門はメニューの 6 行から呼ばれるので (0-11)、自分の位置の判定は `KH_ToyRecall` が 0.5 s ごとに 1 回だけ撃ち、行の間で結果を使い回す (≤ 2 本/s)。
  - `discTakeoutMargin` を削除する。
- 文言は次のとおり (13 言語)。
  - `course.toy.discArea`: 「犬が走れる場所で取り出せます」
  - `course.toy.discAreaState`: 「ここでは不可」

## G. 検証 (マイルストーンごと)

新しい Course メニュー:

- **Course/92 Build Frisbee Anywhere**: 6 匹に `jumpMaxSlopeDeg` 15 / `jumpMaxRise` 0.5 / `runMaxSlopeDeg` 25 / `runMaxStep` 0.5 を書き、輪を非表示にし、一言を書き換えて保存する。M4 では半径も 0 にする。
- **Course/93 Verify**: 周回路のノードを 2 m ごとにとり、左右 ±3 m の点で次を数える。
  - `Reachable` の割合と、跳べる割合
  - `PathOk` が断る区間
  - 帯の点のうち、浮く / 岸へ戻るの割合
  - Collider の無い木の数
  - 結果は `Temp/des-dog-005/anywhere.json` に出す。
- **Course/94 Walk Harness Frisbee Anywhere**: ClientSim。`KH_CourseWalkHarnessEditor.Frisbee.cs` を足す。

試作シーンには `Terrain` レーン (`Frisbee/4 Build Scene`、犬種のレーンと重ならない位置) を足す。10°・20° の板、0.4・0.8 m の下り段、0.4 m の上り段、5 m の箱、帯状の岩 (端点は平らで途中に障害物) を置く。

### M1 (陸)

| ケース | 期待 |
|---|---|
| 既定 26 投げ (Lab + Pome)・netorder | 今と同じ PASS (平面では規則が変わらない。`CanPlay` の引数を変えるので netorder も回す) |
| T-slope10 | `jump`。着地後 0.2 s の \|根元 − 地面\| ≤ 0.05 m (新しい列 `landGap`)。板は横傾斜 |
| T-up10 / T-down10 | 投げる方向へ上る / 下る 10° の板。`jump`・`landGap` ≤ 0.05 (C0 の実測の地面からの lane) |
| T-slope20 | `ground` (跳ばない) |
| T-down04 / T-up04 | 縁は z +4.4 (v3 の待機位置からの B chase の踏み切りと着地の間)。T-up04 の投げ台は 0.4 m の台の上 (平地からの投げでは rise が 0.3 m 前後で、0.4 m 上への着地は頂点の余裕 0.10 m を満たさない)。`jump`・`landGap` ≤ 0.05・**段を跨ぐ跳躍が 1 本以上**: 踏み切りと着地の地面が台と平地 (\|dY\| ≥ 0.3、下りは tDown > tUp、上りは tDown < tUp) |
| T-down08 | `jump`・`stand`・`ground` のどれか + 全跳躍の \|dY\| ≤ jumpMaxRise。段を降りて下で跳ぶのは規則どおり (C-3b の下り 1.0 m) |
| T-cliff | 5 m の箱の上から投げ、円盤は下へ落ちる。`none` + `cannot-play`。照準の静的検査で × |
| T-boxtop (箱の上へ) | 2 m の台から 2.5 m の箱の天面へ投げる。犬は平地。`none` + `cannot-play` (`PathOk` の段と終点の高さ)。照準の静的検査で × |
| T-rock (途中に岩) | 高さ 0.45 m・厚さ 0.2 m、犬の 0.5 m 先 (lane の 0.3 m には当たり、`runMaxStep` 以下なので PathOk は通る。助走の始点が岩の上に乗らない位置)。`LaneClear` (助走・着地) で候補を捨てる (`laneRefused` か `why=lane`)。取消が出るなら `cancel reason=ground` だけ |
| sixdogs | `_solveRayMax` (1 Solve の置き場所レイの最大) が 200 以下。frame の比を今の記録と比べる |

決定: 2026-09-28 M1 実装で変更 (T-down08・T-cliff の期待、T-up10 / T-down10 / T-boxtop の追加、T-down04 / T-up04 の跨ぎ条件、T-rock の高さ、sixdogs の指標。Codex M1 レビュー 1-2・1-3・7)
| Frisbee/5・Walking/5 | 全検査が通る。:683 は廃止し、:688-693 と PrototypeEditor の :876-890 は非対称の `LandingOf` の検査に置き換える |
| Course/94 | F1 (広場の回帰)、F8 (橋の床は拾うだけ)、F9 (20° は ground)、F10 (崖は × と cannot-play)、R1 (川の上へ投げる → 跳ばず、今の岸戻し)、R2 (水遊び中の犬に投げる → 受けずに出水の要求) |
| 75・89 | PASS のまま |

### M2 (浮き)

| ケース | 期待 |
|---|---|
| F5a 川の中央へ | 着水 (水面で `EndLanded`) → 浮いて漂い、橋の帯の手前で止まる。犬は帯で `river-band` (水中の追跡は M3) |
| F5b 水遊び場へ | 場の下流端で止まる |
| F6 橋の帯へ / F7 岸の無い区間へ / F7b 帯の縁へ | 岸戻し |
| F5c 浮いているディスクを手で取る / メニューで呼ぶ | 重力が戻る |
| 89 (追加の検査)・88 (ボール) | PASS (漂流の切り出しの回帰) |

### M3 (ドボン・水中)

| ケース | 期待 |
|---|---|
| F2 ポメ、z≈150 の岸から | ドボン → 12 → 投げた人の側の岸 → 手元 |
| F3 ラブ、流路 | ドボンして歩いて持ち帰る |
| F4 ラブ、深い所 | 跳ばない。浮いて `deep-disc` |
| F5 取り損ねて浮く | 11 → 12 → 手元 |
| F11 11 の最中に呼ぶ | `target-lost`、出水 |
| F12 12 の最中に呼ぶ | `InWater` で断る |
| F14 11 で取得に失敗 (ハーネスがディスクの Owner 移譲を止める) | `claim-failed`、ディスクは浮いたまま |
| 78・88・89 | PASS |

犬の Owner の交代 (11・12・13) は ClientSim では作れないので、PCVR の 45 に送る。

### M4 (門)

| ケース | 期待 |
|---|---|
| メニュー、道の上 | 取り出せる |
| メニュー、崖の縁・川の中 | `OutOfArea` と新しい文言 |
| Walking/5 | :596 を「半径 0」に直して PASS |

### PCVR (川の設計書のチェックリストの続き)

| # | 手順 | OK / NG | 確かめる設計値 |
|---|---|---|---|
| 37 | 道の斜面で上り / 下りへ投げる | 跳ぶ・拾うの切り替えが不自然でない | 15°・0.5 m |
| 38 | 0.3〜0.5 m 低い所で跳んで取る | 宙で着地しない・地面に刺さらない。下りのクリップの速さが変でない | B-3・B-4 |
| 39 | 崖・木立・岩の方へ構えて投げる | × と一言が出る。跳ぶ弧が岩や枝を通り抜けないか (空中は検査していない) | C-1・I-2 |
| 40 | 小型犬、岸から川の上へ | ドボン → 泳いで投げた人の岸へ → 手元 | D-3・D-4 |
| 41 | ラブ・サモエド、浅い / 深い | 浅いとドボンして歩く。深いと跳ばない | 0.85 M |
| 42 | 川へ低く投げて取り損ねさせる | 浮いて流れ、犬が取りに行く | D-2・D-4 |
| 43 | 橋の上 / 橋の近くの川 | 橋では拾うだけ。橋の帯では岸戻し | D-6 |
| 44 | 2 端末。水中で運ぶ間に C が途中参加 | 浮いた高さ・Walk・口のディスクが最初から見える | E |
| 45 | 2 端末。11・12・13 の間に犬の Owner が退出 | ディスクが浮いて残り、犬は岸へ上がる | D-4 |

### Quest / iOS

[VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) に項目 12 を足す。6 匹が斜面を追うときと水中を追うときの fps、持っている人の × を見る。Editor での目安は sixdogs の `_planRays` と `_solveRuns`、および次の走査回数。

- `_FloatsAt`: 中の `_NearestSlice` は最悪約 86 候補。呼ぶのは次の場面だけ。
  - `CheckRiver`: 帯の中だけ、4 Hz
  - 照準: 10 Hz
  - 飛行中: 水面より下へ入った 1 回
- メニューの門: 2 本/s

## H. 実装の順序

各 M の終わりにコンパイル → G の該当ケース → コミット。M2・M3 は確定前に Codex の独立レビュー (同期・所有権)。

| M | 内容 | 主なファイル | 規模 |
|---|---|---|---|
| M1 | B、C (`PlayableAt`・`PathOk`・斜面の `LaneClear`・歩幅の門・Ready・×)、輪の非表示、**川への踏み切り・着地の明示的な拒否** (帯は今の岸戻し。水遊び場は走る・立つのは今の例外のまま、跳躍の T0 / L0 だけ拒否。B-6)、水中の犬は受けない (D-1 の拒否分)、Course/92・93、terrain レーン、Walking/5 と PrototypeEditor の全検査 | `KH_FrisbeeCatch`・`.Planner`・`.Motion`・`.Diagnostics`、`KH_Frisbee`、`KH_FrisbeePrototypeEditor`、`KH_FrisbeeHarnessEditor` + cjs、新しい `KH_CourseFrisbeeAnywhereEditor`、`KH_DogWalkingEditor` | +550 / −150 |
| M2 | D-2 (浮き・着水・`EndFlight` の分岐・放した後の復旧・漂流の共有)、D-6、Course/89 | `KH_Frisbee`、`KH_RiverData`、`KH_ToyRecall`、`KH_CourseFrisbeeRiverEditor` | +200 / −40 |
| M3 | D-3・D-4・D-5 (状態 11〜13、取得待ちと喪失、非 Owner の表示、Wander の例外、計画器の移設) | 新しい `KH_FrisbeeCatch.Water`、`KH_RiverData`、`KD_DogFetchController.HillsideWater`、`KD_DogWanderController` (+`.HillsideWater`)、`KH_ToyRecall`、ハーネス | +500 / −90 |
| M4 | F、`HasArea` / `InArea` / `BuildRing` の削除、文言、A の決定の記録、ハーネスの文書・game-design | `KH_ToyRecall`、`KD_TreatPouch.Toy`、翻訳表、文書 | +80 + 文書 |

決定: 2026-09-28 M1 実装で変更 (M1 の行の水遊び場の扱い。段差・帯の拡張・SolveRayCap・カウンタ・Ready・Return の変更は B-6 / C-3b、ケースは G M1)

## I. 決めていないこと / リスク

1. **クリップ**: 下りの速さを変えた Jump クリップが自然に見えるか (38)。だめなら `jumpMaxRise` を下げる。
2. **空中の弧は検査しない** (今と同じ)。途中の岩・張り出し・枝を犬が通り抜けることがある。M1 では 39 で見て、目立つなら弧の 3 点 (T0–頂点–L0) に体幅の球 (SphereCast) をかける案を検討する。
3. **Collider の無い木**: 犬がすり抜ける。Course/93 の数を見て、その区間の扱いを決める。
4. **歩幅の門の誤停止**: 石・根の Collider (layer 0\|11) で止まるおそれ。T-rock と F1 で、誤停止が 0 件であることを確かめる。
5. **帯の縁の坂**: 岸戻しの後にディスクが転がって水へ戻り、浮きに変わる往復 (ボールの W5 で見た現象) が起きないかを F7b で確かめる。
6. **斜面の上の Ready**: 25° を超える所が続く道では、犬が持ち手の横に並ぶだけになる (37)。
7. **SetOwner が遅れる・断られる**: 陸の `ClaimDisc` は今も確認していない (0-7)。M3 では水中だけ直す。陸にも同じ取得待ちを入れるかは M3 のレビューで決める。

## 独立レビュー (Codex、2026-09-28) の指摘と対応

| 指摘 | 判定 | 対応 |
|---|---|---|
| 1-1 踏み切りとキャッチ直下の混同・循環・FineTune・FindBand | 正しい | B-1・B-2 |
| 1-2 2 回解いた後の採用値、実踏み切りの `vel` | 正しい | B-2 手順 3・B-3 |
| 1-3 キャッチ窓と下りの速度・回復時間 | 正しい | B-4 |
| 2-1 非 Owner の水中の表示経路・Animator.speed | 正しい | D-4 表示 |
| 2-2 取得待ち・保持の喪失・放置された kinematic・Owner の交代 | 正しい | D-4・D-2 復旧・G M3・45 |
| 3-1 :228 の毎フレームの判定 | 正しい | 0-3・C-2 |
| 3-2 Solve のやり直しと、形ごとの予算 | 正しい | C-2 (`SolveRayCap`・カウンタ) |
| 3-3 レイ以外の走査・メニュー・キャッシュの頻度 | 正しい | C-2・F・G Quest |
| 4-1 水への投げに × が出る | 正しい | C-1 `Reachable`・C-4 |
| 4-2 `EndFlight` の岸戻しが先に走る | 正しい | D-2 |
| 4-3 場の中の漂流・`floatDrift`・`_FetchWater` と `_FloatsAt` の違い | 正しい | D-1・D-2 |
| 4-4 泳ぐ高さの式・補間・拾いの高さ条件 | 正しい | 0-8・D-3・D-4 |
| 4-5 D-5 の優先順位 | 正しい | D-5 |
| 5-1 `CanPlay` の要求者、Ready 失敗の再試行 | 正しい | C-2・C-3 |
| 5-2 岸の優先順位と 11 への遷移表 | 正しい | A・D-4 |
| 5-3 30 m は z 方向、速さの定義、文言 | 正しい | D-1・D-4・F |
| 6-1 M1 で半径を 0 にすると段階 1 が外れる | 正しい | F・H |
| 6-2 Walking/5 の :693・PrototypeEditor | 正しい | 0-9・G M1 |
| 6-3 M2 が M3 に依存 | 正しい | H の並べ替え・G |
| 7-1 崖の上面は `PlayableAt` を通る | 正しい | C-1 `PathOk` |
| 7-2 水平の `LaneClear`・空中の弧 | 正しい | C-1・I-2 |
| 別件: §E-2 の併記 | 正しい | 冒頭で :413 を正本に指定。整理は別件 |
