---
id: IMP-DOG-012
type: IMP
system: DOG
status: wip
priority: Medium
---

# IMP-DOG-012 フリスビーを 6 匹へ展開する

2026-09-22 起票。[IMP-DOG-008](archive/IMP-DOG-008-frisbee-variations.md) でラブ 1 匹の通常版 (先回り型 + 動きの層) と 6 プリセット (待つ場所 3 種・ひねり・横曲がり) をユーザーが PCVR で「いい感じ」と評価し、全部採用した。次はこれを 6 匹へ展開する。採用範囲の設計は [DES-DOG-002](DES-DOG-002-toy-behaviour.md)、設計値と検証記録は [段階 3 の記録](../../plans/2026-09-21-frisbee-variations.md)、設定 UI は [DES-UI-002](DES-UI-002-frisbee-settings.md)。

## 範囲

- 犬種ごとの姿勢クリップ (Jump / Twist) と口の事実 (facts) を `KH_FrisbeeJumpClipEditor` で生成する。生成器は現在 `Breed = "Lab"` 固定なので、犬種を引数にして 6 犬種を回せるようにする (角度はモデルごとの制作値。側面図で確認)。
- 各犬に `KH_FrisbeeCatch` とディスク 1 枚 (識別色) を配線し、`DiscHome(i)` に置く。メニュー「おもちゃ」の「まだ」を外す。
- 購入犬の胴体追従 (`…/PB3/inu` の ParentConstraint) をフリスビーの犬すべてで無効化する (ラブで硬さの指摘なし。犬種ごとに骨格・Constraint の名前を確認)。
- 6 匹同時の負荷: 候補探索 (最大 5 パス × 32 時刻 × レーン・地面レイ) と 10 Hz の再計画の実測 (`vrc-performance`)。再計画タイミングの分散、軌道と地面高さのパス間再利用を検討。
- `KH_FrisbeeCatch` (1,800 行超) の分割: 同じ Behaviour のまま partial で ①同期・状態遷移 ②計画器 ③移動・跳躍 ④診断 に分ける (Codex M4)。捕獲種別と接近方法の名前・不変条件を整理してから展開する。
- ハーネスを犬種ごとに回せるようにし (投球表に開始位置・向き・期待する捕獲種別)、6 犬種 × 投球表で PASS。
- 複数ディスク・犬の割当 (誰のディスクをどの犬が追うか)、Owner 分裂時の扱いは DES-DOG-002 の決定に従う。
  - **2026-09-24 開発側の決定 (ユーザー確認待ち)**: ディスクは犬別 6 枚 (ボールと同じ、色は犬の識別色)。各犬は自分のディスクだけを追う。Owner 分裂は 1 匹版の仕組み (投げ手のクライアントから要求 → 犬の Owner が実行) を 6 匹に。6 プリセットの切替 (あそぶタブ) は「ディスクを持つ犬全部」に同じ値。

## やらないこと

設定 UI の中身 (DES-UI-002)、ディスクの柄違い、斜面でのジャンプ、Quest/iOS の合格判定 (VER 系で別途)。

## 完了条件

- [x] 6 犬種のクリップ・facts を生成し、側面図で姿勢を確認した (2026-09-24、Frisbee/2b 212/212 (上限適用後)、側面図 3 往復で 6 犬種とも確定。値に「仮」の印は残す)
- [x] 6 匹に Catch とディスクを配線し、Verify (Walking) が 6 匹ぶんを検査する (Walking/5 495 / 0、上限適用後)
- [x] 6 犬種 × 投球表のハーネスが PASS (捕獲・完了・動きの質) — **縮小表 (v0 / v4 × flat / low = 4 投げ) × 6 犬種と `sixdogs` が PASS (Shiba / Samoyed の縮小表は上限適用前の結果)。全 36 投げ × 6 犬種は未実施**
- [x] 6 匹同時の負荷を Editor Play で実測し、記録した (試作シーン、上限適用後。flight の総 frameTime 6 匹 / 1 匹 = 2.62 / 2.86 ms (× 0.92)、計画器の評価回数 355 / 28、batches 19 / 18。本シーン・VR の予測には使えない。[記録](../../plans/2026-09-24-frisbee-six-dogs.md))
- [x] `KH_FrisbeeCatch` の分割と名前の整理を済ませた
- [ ] PCVR で 6 匹の動きをユーザーが評価した (未実施項目は残課題として記録)

## 進捗

記録・値・チェックリストの正本は [6 匹展開の記録](../../plans/2026-09-24-frisbee-six-dogs.md)。

### 2026-09-24 実装 (Codex D+M レビュー済み。表は記録)

- **クリップ・facts (F1)**: `KH_FrisbeeJumpClipEditor` を犬種引数化 (6 犬種 = Corgi / Pome / Lab / Dachs / Shiba / Samoyed。Lab 互換 API は残置)。犬種テーブル `BreedSpec` が Lab の鍵に脚・胴・頭・ひねりの係数と ため / 回復の時間、足裏の厚みを掛ける (Lab は全部 1 = 変化なし。rise は全犬種 0.35 s)。メニュー Frisbee/2 (6 犬種)・2a (1 犬種)・2b Verify Clips (188/188)・3 / 3a (側面図)・6 Make Dog Bodies Rigid (Catch を持つ犬すべて)。側面図 3 往復で 6 犬種とも確定。犬種別の跳躍上限 (`maxRootRise`) を適用 (2026-09-24 確定): `0.9 × 立位の口の高さ ÷ 0.5559`、下限 0.45・上限 0.9。Corgi / Pome / Dachs 0.45 (下限) / Shiba 0.5807 / Samoyed 0.8150 / Lab 0.9000 (PCVR 承認済みの現行値を維持)。当初の下限 0.30 では Pome が flat を跳ばずに立って取ったので 0.45 に上げた。見え方は PCVR で確認。
- **配線・分割 (F2)**: `KH_FrisbeeCatch` を partial 4 本 (main / Planner / Motion / Diagnostics) に分割し、振る舞いは不変 (差分は Owner ローカルの計画器カウンタ 3 本だけ)。改名 `PlanGround`→`PlanRetrieve`、`PlanRun`→`PlanStandOrOvertake`、`ModeStand`→`ModeStraight`。`KH_DogWalkingEditor` が 6 匹に Catch とディスク (新規は `DiscHome(i)`、Lab は動かさない) を配線し胴体追従を無効化、Walking/5 が犬種ごとに検査。試作シーンは犬種ごとの 6 レーン。メニューの「まだ」は配線で自然に消える (`KD_TreatPouch.Toy.cs` 変更なし)。
- **Editor 検証 (司令塔)**: Frisbee/5 failures 0、19b (NetworkID 168 → 178)、Walking/5 489 / 0 → 上限適用後 495 / 0、Frisbee/2b 212/212、Menu/9 (`frisbeeCatchers` 6)・23・20 PASS、C# 0 / U# 82、シーン保存。
- **ハーネス**: `--breed=<Key>` と `--mode=sixdogs` を追加 ([手順](../../testing/kh-frisbee-harness.md))。縮小表 4 投げ × 6 犬種すべて 4/4 PASS (jump 2 / ground 2、motion 失敗 0)。上限適用後に Pome / Corgi / Dachs を再実行して 4/4 (Shiba / Samoyed は適用前の結果、Lab は不変)。下限 0.30 のときの Pome は 2/4 (stand 2) で、下限を 0.45 に上げた根拠。`sixdogs` (2 周) 14/14 PASS。
- **負荷 (試作シーン、Desktop、上限適用後)**: flight の総 frameTime 平均 6 匹 / Lab = 2.62 / 2.86 ms (× 0.92)、最大 9.22 / 27.05 ms。計画器の評価回数 355 / 28 (× 12.68)。batches 19 / 18。言えないこと: 本シーン (地形・草木) を含まない / Desktop の frameTime は VR の予測にならない / 単一クライアント (Owner 1 人) / UnityStats は Statistics 窓と別の計測 / 6 匹同時の回の犬ごとの取り方は未判定 / batches は 1 → 6 匹の描画コストではない (どちらの回も 6 匹が存在) / frameTime 比は Udon の処理時間比ではない。
- **Codex D+M レビュー後の是正 (実装済み)**: 跳躍上限の適用 (D1)、facts の内容検証をシーン変更前に行う (`ReadFacts` / `ReadAllFacts`、Walking/3・Frisbee/4 はシーンを変える前に止まる。M1)、ハーネスの新オプションの手順書 (M2)。
- **副作用**: ハーネス実行後に `ProjectSettings/QualitySettings.asset` の `m_CurrentQuality` が 2 → 3 に変わるので、実行ごとに司令塔が戻した。

### 残り

- PCVR: 6 匹の動き・犬種ごとの姿勢・跳躍の高さ (小型犬の下限 0.45 m = Pome の体高の約 2.9 倍)・複数ディスクの同時・Owner 分裂・リード中 / 集合中の除外との組み合わせ ([チェックリスト](../../plans/2026-09-24-frisbee-six-dogs.md#pcvr-チェックリスト-build--test-し直してから))。
- 全 36 投げ × 6 犬種のハーネス、本シーン (地形・草木込み) での負荷、Shiba / Samoyed の縮小表を上限適用後に取り直すこと。
- status は wip (2026-09-25 に todo から是正。実装済みで AI 側の残り (Shiba / Samoyed の取り直し・全 36 投げ) がある)。残りを済ませたら PCVR の評価待ちとして review へ移す。
