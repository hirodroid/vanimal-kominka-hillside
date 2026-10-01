---
id: IMP-DOG-034
type: IMP
system: DOG
status: wip
priority: High
summary: 犬の Udon を軽くする。段階 1 (待機中の徘徊を fps 連動で間引く) は済。Quest 3 は 20〜30 fps で残りは Udon。棚卸しの結果、リード歩行の毎フレーム走査・首輪ロープ・洗浄セル評価が主因。A (挙動同一の入れ替え・キャッシュ) → B (監視の低頻度化) の順で実装する
next: ユーザーの Quest 3 で波 A+B の fps、PCVR で撫で・振動の感触を確認。W3-Dachs の追試
updated: 2026-10-01
---

# IMP-DOG-034 犬の Udon の間引き (軽さ優先)

親: [IMP-ENV-010](IMP-ENV-010-quest-performance.md) §4 の読み。Quest 3 で犬の Udon が CPU の主因 (犬 Udon OFF で 10〜20 → 30〜40 fps)。Editor の分解では犬配下の Udon 158 個で −4.0 ms、うち `KD_DogWanderController` ×6 が −1.7 ms (単独最大、内部は 1 か所の山ではなく毎フレームの tick が薄く広い)。

## 方針 (2026-09-30 ユーザー指定)

- **リアルタイム性より軽さ。** 30 fps なら間引いて 35 の方が体験は上。50 fps を超えたらリアルタイム性でよい
- Quest で fps が出れば低スペック PC も快適になる。「軽いのにすごい、体験が深い」を目指す
- 古民家の間引き (案 1〜3: 行動選択 10 Hz、近接 4 Hz、撫で接触 10 Hz) はこの裏山にも入っている。案 4 (遠い犬) は保留のまま。裏山で足した水・リード・洗浄の tick と、Update 持ちの Behaviour 109 個 (毎フレーム入口 約 130 本) は間引かれていない

## 設計

**間引き率は各スクリプトが自分で決める** (Behaviour を跨ぐ読み取りは extern なので共有しない): `Time.smoothDeltaTime` から
- ≥ 50 fps (≤ 20 ms) → 1 (毎フレーム。PC は今のまま)
- 35〜50 fps → 2
- < 35 fps (> 28.6 ms) → 3
戻りは 1 ms のヒステリシス付き (2→1 は 19 ms 未満 ≈ 52.6 fps 超、3→2 は 27.6 ms 未満)。一度 2 になった端末は 50 fps ちょうどでは 1 に戻らない。犬ごとに位相 (`dogIndex`) をずらし、6 匹が同じフレームに集まらないようにする。

**飛ばしてよいのは「握手していない」とき。** 徘徊・Fetch・フリスビー・水・洗浄は `Time.frameCount` の「今フレーム見た / 前フレーム見た」握手 (約 30 か所) で繋がっている。徘徊の Update を丸ごと飛ばすのは、犬が **待機中** (リード無し・水の外・洗われていない・Fetch/フリスビー Idle) のときだけ。busy なら毎フレーム (従来どおり)。
飛ばしたフレームの `Time.deltaTime` は積算し、次に実行する Update で移動・加減速・回転に使う (速度は変わらず、刻みだけ粗くなる)。「毎フレーム下ろしてから立てる」フラグは飛ばしたフレームでは前回の結論のまま残る。LateUpdate は飛ばしたフレームでは即 return する (Animator の Bool は変化時書き込みなので保持され、呼吸のフェードは次の tick で積算 dt ぶん進む)。

## 段階

1. **徘徊 ×6** (この Issue の本体): 待機中の Update を間引く + LateUpdate / Update で毎フレーム書いている extern (音量、Animator の float、ピッチ等) を変化時だけ書く。期待 −0.5〜1 ms (PC 換算)
2. 待機中の Behaviour (洗う道具 15・寝床 6・停止アンカー 6・リード金具 6・おやつ 6・ボール 6・ディスク 6+6) の Update を待機中だけ 1/5 に。握手を持つもの (ディスク・ボール・Fetch) は「待機中」の定義を厳密に
3. それでも足りなければ移動そのものを Quest だけ 1/2

## 完了条件

- [ ] 段階 1 の前後を Editor (ClientSim、上限解除、20 サンプルの中央値) で比較し、この Issue に記録
- [ ] 散歩ハーネス (Course/94b 系) とフリスビーハーネスの回帰が PASS (busy 時は従来どおり毎フレーム)
- [ ] PC (≥ 50 fps) で挙動が変わらない (div = 1)
- [ ] Quest 3 で fps と、犬の動きの刻み (1/2〜1/3) が許容範囲か (ユーザー)
- [ ] Codex レビュー反映

## 記録

### 2026-10-01 段階 1 実装 (`KD_DogWanderController.Tick.cs` + partial 7 本、.cs 33 行)

- `Update()` は Tick.cs へ移り、旧本体は `UpdateTick()`。busy 判定 `TickBusy()` は自分のフィールド (前回 tick の結論) → Fetch / ディスクの順で、迷ったら busy。`Dt()` は自分の tick 内で積算 dt、他 Behaviour から呼ばれた足では `Time.deltaTime`。`tickDividerOverride` (0 = 自動) は Editor 計測とハーネスで divider を固定するため (本番は 0)
- **Editor 計測** (ClientSim、i7-14700KF、上限解除、スポーンで待機中の 6 匹、20 サンプルの中央値): div=1 (従来と同じ経路) **8.2 ms** → div=3 **5.8〜7.0 ms** → div=2 **5.6 ms**。徘徊単体の −1.7 ms より大きい = 徘徊を止めると他も軽くなる相互作用込み。Quest 換算で −8〜15 ms の見込み (未実測)
- **ハーネス (div=3 に固定)**: 水 W0/W4/W6 14/14 PASS、フリスビー M3 dobon 2/2 PASS、洗い base 12/12 PASS、洗い react 14/14 (SKIP 2 は 1 人 ClientSim の N/A)、Travel quick 2/2、Smoke は観測のみ (橋を渡り到達)。**Course/63 (途中接続・帰還) は div=1 の対照でも同じ `Discontinuous dog movement dog 2 step=1.24` で FAIL = 本差分の回帰ではなく既存の不具合** (要トリアージ、別件)
- Codex 独立レビュー: 修正必須なし。推奨 2 件 (文書のヒステリシス説明、記録の追記) を反映。参考: 間引きで fps が上がって divider が往復する可能性は自動モードの実機で見る
- 未確認: 自動モード (smoothDeltaTime 連動) の切替頻度と犬の動きの刻み、Quest 3 の fps、実通信の非 Owner の見え方

### 2026-10-01 Quest 3 実機 (段階 1 + 殻 1 パス + 地形 Lite の版)

- **20〜30 fps** (調子の良いとき 30 超)。犬 Udon OFF で **60〜70 fps**。描画側 (地形・木・葉・入口・犬・川) を消しても 2〜3 fps しか変わらない → **残りは Udon の CPU**。目標はあと +10 fps
- 段階 1 が効かなかった理由 (棚卸しで判明): Quest の主な状態は **リードで連れて歩く = busy = 毎フレーム**で、待機中の間引きの対象外。しかもリード歩行の経路に重い走査が入っていた (下の A1)

### 2026-10-01 毎フレーム処理の棚卸し (静的解析。Unity 非接続)

Opus 6 本 (徘徊 / Fetch / フリスビー / 洗浄 / 撫で・リード / その他) と Codex 1 本が独立に読んだ結果。原本は [docs/_sessions/udon-audit-2026-10-01/](../../_sessions/udon-audit-2026-10-01/BRIEF.md) ([wander](../../_sessions/udon-audit-2026-10-01/wander.md) / [fetch](../../_sessions/udon-audit-2026-10-01/fetch.md) / [frisbee](../../_sessions/udon-audit-2026-10-01/frisbee.md) / [wash](../../_sessions/udon-audit-2026-10-01/wash.md) / [petleash](../../_sessions/udon-audit-2026-10-01/petleash.md) / [misc](../../_sessions/udon-audit-2026-10-01/misc.md) / [codex](../../_sessions/udon-audit-2026-10-01/codex.md))。**extern 数はすべてコードからの概算 (±50 %) で実測ではない。順位は Opus と Codex で一致した。**

全体像: フレーム入口を持つ Behaviour は 109 実体・135 入口。「6 匹をリードで連れて歩く・誰も洗っていない・玩具未使用・メニュー閉」の Owner 側で **約 10,000〜14,000 extern / フレーム**。

| 順 | どこ | 毎フレームの中身 | 概算 / フレーム |
|---|---|---|---|
| 1 | 徘徊 ×6 (リード歩行) | `TryStopover` がクールダウン・持ち手移動の早期 return **より前**に `route._TrailDirection` (経路リンク約 25 本の走査 ≈ 650) を呼ぶ。Owner tick ごとに陸の犬にも川の 34 頂点判定 ≈ 290。再選択で `activities.Available` ×3 | 4,500〜8,500 |
| 2 | `KH_LeashCollar` ×6 (PostLate) | ロープ骨 12 本の Sin/Cos・LookRotation、手の骨 4 回取得を犬ごとに (同じ手を 3 回)、manager 呼び出しの反復。**リード無しでも Renderer の enabled 4 つを毎フレーム書く (≈ 85)** | 1,900 |
| 3 | `KH_DogWash` ×6 | **誰も洗っていなくても** 0.5 秒ごとに `EvaluateState` が 256 セルを走査、6 匹が同じフレーム (Owner で約 17,000 のスパイク)。歩行中は 2 秒ごとに全セルのシリアライズと `Decay` (汚れが付くたび活動タイマーが延びる) | 1,300〜1,600 (平均) |
| 4 | `KD_DogPack` (Late) | 15 ペア比較、`activeInHierarchy` を 27 回読む | 330〜480 |
| 5 | `KD_DogPetController` ×6 | 何も起きていなくても LateUpdate ≈ 38 / 匹 (0 に届かないブレンド、他スクリプトの状態 3 つ)。リード中は 6 匹とも「近い」扱いで 0.1 秒ごとの接触判定が同じフレーム | 370〜660 |
| 6 | `KH_LeashHaptics` | 6 匹ぶんの所有・手・首輪位置を毎フレーム (手の読み 6 回)。`logPulls = true` が本番シーンに残る | 300〜330 |
| 7 | Fetch ×6 / ボール ×6 / `KH_ToyRecall` | Idle でも `_IsLeashed`・`BallClaimed` を毎フレーム。`KH_ToyRecall` は 0.25 秒ごとに公園のボールへも川判定 (約 2,800 のスパイク) | 240 + スパイク |
| 8 | フリスビー ×6 + Catch ×6 | しまってあるディスクが毎フレーム照準の非表示を再確認、0.25 秒ごとに 6 枚同時の川判定 (700〜1,200 のスパイク) | 150〜180 + スパイク |
| 9 | `KD_CallPose` | 誰も呼んでいなくても 20 Hz で寝床 6 枠を走査 | 150〜230 |
| 10 | その他 | `KH_WashTool` ×15 (棚の上でも ≈ 85)、`KD_TreatBox` (0.25 秒スパイク)、`KD_GroupActivities` (かけっこ未開催でもボール 6 個を毎フレーム ≈ 70)、`KD_DogStopAnchor` ×6 (**計測専用で実行時に誰も読まない** ≈ 70 + 入口 6)、`KD_TreatPouch` 閉 ≈ 60 | 400〜500 |

## 段階 2 以降の計画 (2026-10-01 改訂。旧「段階 2・3」を置き換え)

**A. 挙動を変えない (順番の入れ替え・キャッシュ・事前判定)。まずここ。合計 約 −7,000 (リード歩行時)**

| # | 変更 | 効果 / フレーム | 危険 |
|---|---|---|---|
| A1 | `TryStopover`: `_TrailDirection` をクールダウン・`HolderMoving` の後へ ([HillsideLeash.cs:867](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.HillsideLeash.cs#L867)) | −3,900 | 低 (結果同一) |
| A2 | `KH_WaterSpot._Contains` に外接矩形の事前判定 + `_Ready()` のキャッシュ。徘徊・Pack・Fetch・ToyRecall・ディスク・メニューの全部に効く | −1,500〜1,700 + スパイク 3 種が消える | 低 (結果同一) |
| A3 | 止まっているリード犬の `_DistanceToTrail` を「0.25 m 動いたら / 0.5 秒」でキャッシュ ([HillsideLeash.cs:612](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.HillsideLeash.cs#L612)) | −650 / 立ち止まった犬 | 低 |
| A4 | `KH_LeashCollar`: Sin/Cos 表を前計算、manager の配列と手の骨はフレームに 1 回、Renderer の切替は変化時だけ | −650〜900 | 低 |
| A5 | 再選択の重複 (`activities.Available` ×3、`WaterWetAt` ×2) を 1 回に | −800 | 低〜中 |
| A6 | Fetch Idle の条件の順序 (`!IsIdle` を先、`BallClaimed` は Owner の枝だけ)、ボール未保持なら即 return、ディスクの照準は `OnPickup`/`OnDrop` のフラグから | −150 | 低 |
| A7 | 本番で止める: `KD_DogStopAnchor` (計測専用。`KD_ReleaseCheckEditor` とハーネスの依存を先に確認)、`KD_GroupActivities` のボール監視 (かけっこ時だけ)、`KH_LeashHaptics.logPulls = false` | −140 + 入口 6 | 低 |
| A8 | `KD_CallPose`: 自分が呼んだ犬がいなければ即 return、0.25 秒 tick | −150〜230 | 低 |

**B. リアルタイム性を落とす (見た目に出ない監視・判定)。合計 約 −2,500**

| # | 変更 | 効果 / フレーム | 危険 |
|---|---|---|---|
| B1 | **洗浄**: `EvaluateState` はセルが変わったときだけ + 有効セルだけ 1 ループ + 犬ごとに位相。同期は量子化後のバイトが変わったときだけ。`Decay` は泡・濡れが無ければ回さず 2 秒 → 4 秒。汚れのサンプル 4 Hz → 1 Hz。待機中の `Update` は 5 Hz | −1,200〜1,600、17,000 のスパイクが消える | 低〜中 (「変わった」の立て忘れ = きれい判定が出なくなる) |
| B2 | `KH_WashTool` ×15: 持たれていない間は 2 Hz | −55〜85 | 低 |
| B3 | `KH_LeashHaptics` 10 Hz (引き速度は経過時間で) | −310 | 低 |
| B4 | `KD_DogPetController`: 完全待機なら LateUpdate 即 return、他スクリプトの状態は 5 Hz、接触判定の位相をずらす | −300 | 低 |
| B5 | `KD_DogPack` のペア比較を 10 Hz (接触中だけ毎フレーム) | −250〜350 | 中 |
| B6 | ボール・ディスクの川判定は陸で静止していれば飛ばす、タイマーの位相をずらす。`KD_TreatBox` は全枠空なら即 return | スパイク除去 | 低〜中 |

**C. 見た目に出る間引き (A・B の後、まだ足りないときだけ)**

- C1: ロープ骨 12 本を 35 fps 未満で 1/2、見えていなければ飛ばす (−数百〜1,000。中)
- C2: リード歩行の「判断」を 1/2〜1/3、移動は毎フレーム (−700。**高**: 撫で側の視線・尻尾は 1 フレームだけ有効、洗浄の `_washLastFrame == frameCount-1`、水の `_waterSeenFrame` を先に「前回 tick」基準へ直す必要)

**見立て**: A + B で約 12,000 → 約 3,000 extern / フレーム。Quest 換算は単価の幅が広く (1〜8 µs) 断定できないが、犬 Udon の差 (17〜36 ms) の 6〜7 割に当たり、**10 ms は A と B1 だけで狙える**。C は不要になる見込み。

**進め方の注意**: `KD_DogPack.cs`・`HillsideLeash.cs`・`HillsideWash.cs`・洗い場まわりは別セッションが作業中 (IMP-DOG-035。新規 `KH_WashStationCall` ほか 3 本にも Update がある)。同じファイルを同時に触らない。Unity が空いてから、ファイルの持ち主ごとに波で実装 → コンパイル → ハーネス → Codex。各波の前後を Editor で測る (リード 6 匹の状態を作って 20 サンプル)。

### 2026-10-01 波 A 実装 (挙動を変えない修正。.cs 17 本)

| # | 変更 | 概算の効果 / フレーム |
|---|---|---|
| A1 | `TryStopover`: 持ち手の 6 m 以内に立ち寄り地点が無ければ経路走査をしない (クールダウンと `HolderMoving` の後ろへ回すだけでは歩行中は両方通過するので効かない、と実装時に判明。地点 3 つの距離判定 ≈ 30 を先に置く) | リード犬 1 匹 −650、6 匹 −3,900 |
| A2 | `KH_WaterSpot._Contains` に外接矩形 (1 cm 余白) の事前判定、`_Ready()` をキャッシュ。NaN 頂点があれば矩形判定を使わない | 矩形の外の呼び出しごとに −350 |
| A3 | 止まっているリード犬の `_DistanceToTrail` (犬・手): 測った点からの移動量 m で真の距離が [d−m, d+m] に入ることを使い、**しきい値との大小が決まるときだけキャッシュ、決まらなければ引き直す** (比較結果は毎フレーム走査と同じ) | 立ち止まった犬 1 匹 約 1,300 → 15 |
| A4 | `KH_LeashCollar`: Renderer は状態が変わったときだけ書く、manager の配列はフレームに 1 回、`GetTrackingData` 1 回、ロープ 12 骨の Sin/Cos 表を前計算 (式と順序は同じ) | リード犬 320 → 205、6 匹 −700。リード無し 17 → 5 |
| A5 | 再選択の `activities.Available` ×3 → 1、`WaterWetAt` ×2 → 1 (tick 内メモ) | 再選択 tick ごとに約 −330 |
| A6 | Fetch Idle の条件順・`BallClaimed` は使う枝だけ、ボール未保持は早期 return、ディスクの照準は `_aimShown` | 約 −150 |
| A7 | `KD_GroupActivities` のボール 6 個監視は mode==2 (かけっこ。裏山のメニューからは到達しない) のときだけ。`KH_LeashHaptics.logPulls` を既定・ビルダー・シーンとも false | −55〜70 |
| A8 | `KD_CallPose`: 自分が呼んでいない犬は即 return、誰も呼んでいない間は 0.25 秒 tick | −150〜230 |
| B6 の安全な部分 | ディスクとボールの川判定タイマーを固定グリッド + 位相ずらし (定常時に同じフレームへ集まらない)、`KH_ToyRecall` のコンポーネント参照をキャッシュ、`KD_TreatBox.Reconcile` は全枠空・全犬未請求なら早期 return | スパイク除去 + 約 −300 / Owner tick |

見送り: `KD_DogStopAnchor` の停止 (`KD_ReleaseCheckEditor` とハーネスの依存確認が先)、LateUpdate の `_EndGreeting` のゲート (同期で引き継いだ挨拶マーカーを消す役があり同一性を示せない)、`WashErrandTick` の低頻度化 (IMP-DOG-035 の `StationTick` が入った)、手の骨の共有 (manager 経由は extern が逆に増える)。

- コンパイル: C#/U# 0 エラー。ロープ形状検査 (`Walking/Production 6`) PASS
- ハーネス: Smoke 観測のみ / 水 W0・W4・W6 14/14 / 水 Fetch W3 31/31 (連続実行では W3-Dachs が 1 件落ち、単独では全通過 = 文書化済みの負荷依存) / フリスビー M3 2/2・M2 9/9 / 洗い base 12/12・react 16/16 / Travel quick 2/2 / おやつ 17/17
- **落ちたまま (波 A の回帰ではない)**: 79 W2 (9/27 から断続的に FAIL。9/29 の 2 回も FAIL)、6 匹一周 (53) の `Discontinuous dog movement dog 0 step=1.25` (段階 1 の前の経路でも Course/63 が同じ症状)。**リード歩行で犬が約 1.25 m 飛ぶ既存の不具合があり、別件でトリアージが要る**
- Codex 独立レビュー: 必須 1 (A3 の旧実装は境目を跨いで戻る動きを取りこぼしヒステリシスが分かれる → 上の区間判定へ書き直し)、推奨 4 (A2 の NaN 頂点 → ガード追加 / B6 の初回は同フレームになり得る → コメント訂正 / A7 はかけっこ開始フレームの扱いが変わる → 裏山では到達しないので受容 / `KH_ToyRecall` のコンポーネントキャッシュは配列差し替えに追従しない → 実行時の差し替えは無いので受容)。修正後に再コンパイル、水 14/14・Smoke を再実行
- 未確認: Editor での前後計測 (リード 6 匹の状態を作る手段が無く、待機中の計測は揺れが大きい)、**Quest 3 の fps**

### 2026-10-01 波 B 実装 (見た目に出ない監視の低頻度化)

| # | 変更 | 概算の効果 / フレーム | プレイヤーが気づき得る差 (最悪) |
|---|---|---|---|
| B1a | `KH_DogWash.EvaluateState`: セルが変わったかもしれないときだけ走査 (`_cellsDirty` か配列の差し替え) + 4 秒ごとの保険、時間だけの判定 (殻 OFF) は 0.5 秒ごとに別関数、有効セルだけ 1 パス、犬ごとに位相 | 待機中 Owner 約 1,550 → 100〜130、**0.5 秒ごとの約 17,000 のスパイクが消える** | 殻が消えるのが +0.2 秒。きれい判定のチャイムは変わらない (洗っている間は毎フレーム) |
| B1b | 同期は送る量子化値が変わったときだけ (`dirtSeq` が動けば従来どおり) | 歩行中 約 1,700 → 400 | なし |
| B1c | `Decay`: 泡・濡れが無ければ回さない、通常 4 秒・洗っている間は 2 秒、実経過時間ぶん (上限 2 周期) | 上に含む | 乾きが 4 秒刻み (1 回 3〜4 %) |
| B1d | 汚れのサンプル 4 Hz → 1 Hz、主洗い場の二重チェックを除去 | −45 + 0.25 秒ごとの 420 | 旋回・往復の距離は 1 秒ごとの弦長になるので、汚れの付き方がわずかに少なくなり得る |
| B1e | 待機中の `Update` は 5 Hz。ストローク直後 1 秒・描画/送信/復元/リプレイ待ち・音のフェード・`_syncDue` があれば毎フレーム。受信・Owner 移譲・`_ReleaseHold`・`_RequestShake` で `Wake()` | −80 | 待機中の犬への同期・RT 復元が +0.2 秒 |
| B2 | `KH_WashTool` ×15: 自分が持っていない・スプレー表示なし・リセット待ちなしなら 2 Hz | 約 85 → 25 | 他人が置いた道具に気づくのが +0.5 秒 |
| B3 | `KH_LeashHaptics`: 引き判定 10 Hz、速度は実経過時間、パルス再生は毎フレーム | 約 330 → 15〜35 | 引きの振動の始まりが +0.1 秒 |
| B4 | `KD_DogPetController`: 完全待機なら LateUpdate 即 return (ブレンドを 0 へ丸め、中立姿勢を 1 回書いてから)、遅い状態は 5 Hz、リモート撫での確認 10 Hz、接触サンプルを犬ごとの位相へ、手が遠ければゾーン探索を省略 | リード歩行の犬 約 62 → 17、6 匹で −270 + スパイク分散 | 寝入り・起きの目、伸び・顎のせの開始が +0.2 秒。撫でている間は従来どおり |
| B5 | `KD_DogPack`: 犬ごとの値はフレームに 1 回、近いペアがいなければ 10 Hz、接触・接近中は毎フレーム | 約 330〜480 → 平均 100 (30 fps) | 急に寄った 2 匹の押し出し開始が最大 +0.1 秒 |
| A7 | `KD_DogStopAnchor`: `measureDrift` (既定 false) か計器が配線されているときだけ動く。実行時に出力を読むスクリプトは無い (grep で確認) | −54〜70 + 停止ごとの同期送信 | なし |

- コンパイル: C#/U# 0 エラー
- ハーネス: 撫での接触検査 PASS / 洗い 6 群 12/12・14/14 (SKIP 2 は N/A)・9/9・11/11・6/6・13/13 / 水 W0・W4・W6 14/14 / フリスビー M3 2/2 / Travel quick 2/2 / おやつ 17/17 / Smoke 観測のみ。ハーネスの `WrSetCells` (同じ配列へ直接書く) に `_DebugCellsChanged` の通知を 1 行追加
- **水 Fetch W3-Dachs が 30/31 (carryMouthLow=1) で 2 回連続 FAIL。** 9/30 から断続的に落ちるケース (履歴 8 回中 4 回 FAIL、波 A の後は 1 勝 1 敗)。波 B で率が上がったかは未判定 → 要追試
- Codex 独立レビュー: 修正必須なし。推奨 3 件を反映 (Pack の接近予測の速度項が半分だった / 待機判定に `_syncDue` を追加 / Decay の上限は旧実装と同じ扱いであることを明記)
- 未確認: **Quest 3 の fps**、PCVR での撫で (頭・尻尾が撫で終わりにきれいに戻るか、寝ている犬の目)、リードの振動の感触、複数人での洗浄の同期
- W3-Dachs の追試 (波 B コミット後に 2 回): FAIL 1 / PASS 1 (31/31)。波 B の後の通算は 4 回中 3 回 FAIL、それ以前の履歴は 8 回中 4 回 FAIL。**断続的に落ちる性質は変わらず、波 B で率が上がったかは回数が足りず判定できない** (どちらも `carryMouthLow=1` = くわえて泳ぐ 1 フレームだけ口が水面下)。原因の調査は別件
