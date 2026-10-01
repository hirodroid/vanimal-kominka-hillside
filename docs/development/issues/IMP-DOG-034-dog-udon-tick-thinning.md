---
id: IMP-DOG-034
type: IMP
system: DOG
status: wip
priority: High
summary: 犬の Udon を軽くする。段階 1 (待機中の徘徊を fps 連動で間引く) は済。Quest 3 は 20〜30 fps で残りは Udon。棚卸しの結果、リード歩行の毎フレーム走査・首輪ロープ・洗浄セル評価が主因。A (挙動同一の入れ替え・キャッシュ) → B (監視の低頻度化) の順で実装する
next: ユーザーの承認と Unity の空きを待って A1〜A8 → B1 を実装 (別セッションの IMP-DOG-035 とファイルが重なるので順番を調整)
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
