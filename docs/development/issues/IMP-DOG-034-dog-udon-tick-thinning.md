---
id: IMP-DOG-034
type: IMP
system: DOG
status: wip
priority: High
summary: 犬の Udon を「fps が低いときだけ間引く」(50 fps 以上は毎フレーム、35〜50 は 1/2、35 未満は 1/3)。段階 1 は徘徊 ×6 の待機中 Update の間引きと毎フレーム extern の変化時書き込み
next: ユーザーの Quest 3 で fps と犬の動きの刻み (自動モード) を確認 → 段階 2 (待機中の Behaviour の 1/5 化) をやるか決める
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
