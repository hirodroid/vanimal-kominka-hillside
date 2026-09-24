# フリスビーの 6 匹展開 (犬種別クリップ・配線・負荷) — 記録と PCVR チェックリスト

> 対象: IMP-DOG-012

状態: **2026-09-24 実装。Editor Verify 全 PASS。犬種別の跳躍上限を適用後、ClientSim ハーネスは縮小表 (Pome / Corgi / Dachs を再実行、Shiba / Samoyed / Lab は適用前の結果) と 6 匹同時の負荷 (`sixdogs`) が PASS。全 36 投げ × 6 犬種と PCVR は未実施**。作業票は [IMP-DOG-012](../development/issues/IMP-DOG-012-frisbee-six-dogs.md)、採用範囲の設計は [DES-DOG-002](../development/issues/DES-DOG-002-toy-behaviour.md)、1 匹での設計値と経緯は [段階 3 の記録](2026-09-21-frisbee-variations.md)、ハーネスの手順は [kh-frisbee-harness](../testing/kh-frisbee-harness.md)。ここは「犬種の値と根拠」「検証記録」「PCVR チェックリスト」。

## 開発側の決定 (2026-09-24。ユーザーへ報告し、異論があれば戻す)

- **ディスクは犬別 6 枚** (ボールと同じ)。色はその犬の識別色 (`KH_DogWalkingEditor.DiscColors`)。**各犬は自分のディスクだけを追う** (誰のディスクをどの犬が追うかは色で決まる)。同じ人が 2 枚以上を続けて投げても、犬ごとに独立して動く。
- **Owner 分裂** (犬の Owner と投げ手が別) は 1 匹版の仕組み (投げ手のクライアントから要求 → 犬の Owner が実行) をそのまま 6 匹に使う。同期変数は増やしていない。
- **6 プリセットの切替** (あそぶタブのトレーナー群「フリスビーの取り方」) は「ディスクを持つ犬全部」に同じ値を頼む (`KD_ActivitiesMenu.frisbeeCatchers` を 6 に)。犬ごとの個別設定は [DES-UI-002](../development/issues/DES-UI-002-frisbee-settings.md)。
- 姿勢クリップの角度の正本は `KH_FrisbeeJumpClipEditor` の犬種テーブル (`BreedSpec`) 1 か所。

## 犬種テーブル (F1、`KH_FrisbeeJumpClipEditor.BreedSpec`)

Lab の制作値 (`Keys()` / `TwistKeys()`) を出発点に、関節角へ係数 (groundLegs = 着地・ためで地面側の脚、airLegs = 空中の脚、trunk = 胴、head / twist) を掛け、ため (crouch)・上昇 (rise)・回復 (recover) の時間と足裏の厚み (pawPad) を犬種ごとに持つ。**Lab は係数が全部 1 = 変化なし** (既存の `KH_Jump_Lab` / `KH_Twist_Lab` と同じ)。コードには「仮」の印を残している (PCVR で見直す値)。

| 犬種 | 脚長 (m) | Lab 比 | 脚/胴 | groundLegs | airLegs | trunk | crouch / recover (s) | pawPad (m) | 立位の口の高さ (m) | 跳躍上限 maxRootRise (m、確定) | F1 初版の提案 (m、不採用) |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Corgi | 0.207 | 0.48 | 0.76 | 0.55 | 1.0 | 0.8 | 0.20 / 0.32 | 0.010 | 0.228 | **0.45** (下限) | 0.41 |
| Pome | 0.140 | 0.32 | 0.88 | 0.85 | 1 | 1 | 0.20 / 0.32 | 0.005 | 0.157 | **0.45** (下限) | 0.30 |
| Lab | 0.434 | 1 | — | 1 | 1 | 1 | 0.25 / 0.40 | 0.015 | 0.556 | **0.9000** (現行維持) | 0.75 |
| Dachs | 0.182 | 0.42 | 0.65 | 0.5 | 0.9 | 0.7 | 0.20 / 0.32 | 0.007 | 0.230 | **0.45** (下限) | 0.36 |
| Shiba | 0.347 | 0.80 | 1.17 | 1 | 1 | 1 | 0.22 / 0.36 | 0.006 | 0.359 | **0.5807** | 0.60 |
| Samoyed | 0.414 | 0.95 | 0.91 | 0.9 | 1 | 1 | 0.25 / 0.40 | 0.010 | 0.503 | **0.8150** | 0.73 |

- **rise は全犬種 0.35 s**。計画器の跳躍高さの下限 0.19 m (= 0.35 s を上限倍率 1.8 で割った上昇時間から) は全犬種共通のまま。
- **根拠**: 係数と時間は脚長の Lab 比と脚/胴の比から決め、側面図で「口がディスクの高さに届く / 脚が地面を突き抜けない / 尾・胴が不自然に折れない」を見て詰めた。脚の短い犬 (Corgi / Dachs / Pome) は地面側の脚の曲げを弱め (groundLegs)、ためと回復を短く、胴の長い Dachs / Corgi は胴の曲げ (trunk) も抑えた。
- **跳躍上限 (`maxRootRise`、確定 2026-09-24)**: 式は `0.9 × 立位の口の高さ ÷ 0.5559 (Lab)`、**下限 0.45**・上限 0.9。Lab は PCVR 承認済みの 0.9 m を維持。経緯: 実装済みの上限は全犬種共通 0.9 m だった (F1 初版の 0.75 基準の値は提案のまま未使用。Codex D1)。当初の下限 0.30 では **Pome が flat を跳ばずに立って取り** (run `20260924-194228-0397de`: 2/4 ok、expectFailed 2、kinds stand 2 / ground 2)、司令塔が下限を 0.45 に上げた。小型犬でも 0.45 m (Pome の口の高さの約 2.9 倍) は跳ぶことになる — 見え方は PCVR で確認する。facts (`maxRootRise`) → `ConfigureCatcher` → Verify (Walking/5・Frisbee/5) に通っている。
- 生成物: `Assets/KominkaHillside/Animations/KH_Jump_<Breed>.anim` / `KH_Twist_<Breed>.anim`、`Assets/KominkaHillside/Data/Frisbee/KH_Jump_<Breed>.json` / `KH_Twist_<Breed>.json` (facts)、`AC_KD_Dog_<Breed>` の Jump / Twist State。

メニュー (`Tools/KominkaHillside/Frisbee/`): `2` 6 犬種を生成、`2a` 1 犬種 (EditorPrefs `KH_Frisbee_Breed`)、`2b Verify Clips` (12 クリップ + 12 facts + controller の State。上限適用後 212/212 PASS: facts の `maxRootRise` = 式 (Lab = 0.9)、`mouthOffsetStand.y` = テーブルの standMouthY ±0.005)、`3` 6 犬種の側面図 (`Temp/KH_Frisbee/<Breed>/sheet-side*.png`)、`3a` 1 犬種、`6 Make Dog Bodies Rigid` (Catch を持つ犬すべての `…/PB3/inu` ParentConstraint を無効化)。

### 側面図の確認 (司令塔、3 往復)

- ためと着地のキーで腹が床に付かない (最低点 0.003 m)。
- catch で頭が上、口が前上を向く。
- Twist は catch で 90° ひねり、着地では前向きへ戻る。
- 最低の跳躍高さでの空中の足元の余裕 (airClearanceAtMinRise) 0.115〜0.160 m。
- Corgi / Dachs の踏み切りの pawGap 0.018 / 0.015 m は後ろ足のつま先 (算術上の値。見た目の浮きではない)。
- **判定: 6 犬種とも確定** (姿勢の見え方の最終判断は PCVR)。

## 配線と分割 (F2)

- `KH_FrisbeeCatch` を partial 4 本に分けた: main (フィールド・同期・状態遷移・要求) / `.Planner` (Eta〜ScanOvertake、Pass 1〜4、引っ掛け跳び) / `.Motion` (UpdateReady〜PostLateUpdate) / `.Diagnostics` (ログ・動きの標本・計画器カウンタ `_planScans` / `_planEvaluations` / `_planHookTries`)。行の多重集合の比較で、移動以外の差分は Owner ローカルのカウンタ 3 本だけ (振る舞いは不変)。
- 名前の整理: `PlanGround` → `PlanRetrieve`、`PlanRun` → `PlanStandOrOvertake`、`ModeStand` → `ModeStraight` (ログの `stand` はそのまま。コメントで説明)。
- facts の内容検証 (Codex M1 の是正): `Facts` に name / breed / maxRootRise。`ReadFacts` が JSON の内容 (name・breed・配列長・有限値・時間の範囲・clipLength = crouch + 2 × rise + recover ±0.02・maxRootRise 0.30〜1.5・口の高さ > 0) を検査し、`ReadAllFacts` が 6 犬種 × 2 変種を先に読むので、Walking/3 と Frisbee/4 は**シーンを変える前に**止まる。Walking/5 は無効なファイルを FAIL 行に出す。
- `KH_DogWalkingEditor`: `FrisbeeKeys` = 6 犬種、`RequireFrisbeeAssets` (facts / clip が欠けたら例外)、犬種ごとに `ReadFacts(name, breed)` → `ConfigureCatcher` → `MakeBodyRigid`。新しいディスクは `DiscHome(i)` に置く (Lab のディスクは動かさない)。recall / leash / gathering / wander / fetch の配列を 6 匹に。
- `Walking/5 Verify` が犬種ごとに検査: facts JSON・クリップ長・Animator の Jump / Twist State・facts と Catch の値の一致・剛体化・NetworkID・ディスクの色、6 枚のディスクが互いに 0.30 m 以上離れていること。
- `KD_TreatPouch.Toy.cs` は変更不要 (ディスクが配線された犬は [ディスク] が有効になり、「まだ」が消える)。

### 司令塔の実行 (2026-09-24)

Frisbee/2 → 2b → 3 (側面図 3 往復) → Frisbee/6 → Frisbee/4 → Frisbee/5 (failures 0) → Walking/3 → 19b (NetworkID 168 → 178、新規 10 = 5 匹のディスクと Catch) → Walking/5 (489 / 0) → Menu/9 (`frisbeeCatchers` を 6 に) → Menu/23 PASS → Menu/20 PASS → 保存。C# エラー 0 / U# 82。

上限適用後の再実行: Frisbee/2 → 2b (212/212) → Frisbee/4 → 5 (failures 0) → Walking/3 → Walking/5 (495 / 0) → 保存。

QualitySettings の副作用 (下記) は、各ハーネス実行後に司令塔が `git checkout` で戻している。

## 試作シーンの 6 レーン

`KH_FrisbeePrototype` は犬種ごとのレーンを x 方向に 12 m 間隔で並べる (Corgi −24 / Pome −12 / **Lab 0** / Dachs 12 / Shiba 24 / Samoyed 36)。Lab レーンは従来の名前・座標のままなので、過去のハーネス記録と比べられる。他のレーンは同じ配置を平行移動し、名前に `_<Breed>` が付く。地面は 80 m → 120 m 四方 (scale 8 → 12)。`Frisbee/4 Build Scene` は 6 犬種の facts がそろっていないと止まり、`Frisbee/5 Verify` はレーンごとに配線を検査する。

## ハーネス結果 (2026-09-24)

縮小表 = プリセット v0 / v4 × 投げ flat / low の 4 投げ (`--variants=0,4 --shots=flat,low --breed=<Key>`)。**全 36 投げ × 6 犬種は未実施**。

| 犬種 | 跳躍上限 | run | 結果 | 取り方 | motion / expect 失敗 | eta (標本 2) |
|---|---|---|---|---|---|---|
| Pome | 0.45 (適用後) | `20260924-194936-3541b9` | 4/4 PASS | jump 2 / ground 2 | 0 / 0 | PESSIMISTIC median −0.24 s |
| Corgi | 0.45 (適用後) | `20260924-195038-fd626b` | 4/4 PASS | jump 2 / ground 2 | 0 / 0 | PESSIMISTIC −0.25 s |
| Dachs | 0.45 (適用後) | `20260924-195141-3231f1` | 4/4 PASS | jump 2 / ground 2 | 0 / 0 | PESSIMISTIC −0.25 s |
| Shiba | 共通 0.9 (適用前) | `20260924-192747-85b601` | 4/4 PASS | jump 2 / ground 2 | 0 / 0 | OK −0.08 s |
| Samoyed | 共通 0.9 (適用前) | `20260924-192905-c3b50d` | 4/4 PASS | jump 2 / ground 2 | 0 / 0 | PESSIMISTIC −0.17 s |
| Lab | 0.9 (不変) | `20260924-193010-c0186c` | 4/4 PASS | jump 2 / ground 2 | 0 / 0 | OK −0.15 s |

- **Shiba (0.5807) / Samoyed (0.8150) は上限適用後に縮小表を再実行していない** (表の値は共通 0.9 m のときの結果)。Lab は 0.9 m で不変なので適用前の結果がそのまま有効。
- **下限を上げた根拠 (残す)**: 下限 0.30 のとき Pome は run `20260924-194228-0397de` で 2/4 ok (expectFailed 2、kinds stand 2 / ground 2。flat を跳ばずに立って取った)。同じ下限の中間値 (Corgi / Dachs ≈ 0.37) では run `20260924-194331-1df2d0` / `20260924-194433-42bc7b` が 4/4 だった。
- 上限適用前の Pome / Corgi / Dachs (共通 0.9 m) も 4/4 PASS だった (run `20260924-192642-c7b922` / `192537-d60bbf` / `192420-cef7a3`)。
- low は全犬種で想定どおり `ground` (手前に落ちて拾いに行く)。Console エラー 0、`sceneFileUnchanged=true`。
- eta は標本 2 つずつなので傾向として読まない (合否にも使わない)。上限適用後の小型犬は median −0.24〜−0.25 s の PESSIMISTIC で、全表を流すときに候補を捨てすぎていないかを確かめる。

## 負荷の記録 (`--mode=sixdogs`、上限適用後 run `20260924-195243-316191`)

`vrc-performance` の報告の型に従う。

1. **測定条件**: Unity 2022.3.22f1 / Worlds SDK 3.10.5 / Editor の Desktop Play (ClientSim、単一クライアント) / 試作シーン (平面・犬 6 匹・投げ台) / flat (v0) / 2 周 (各周: Lab 1 匹 → 6 匹同時)。上限を外して測った (`targetFrameRate=-1`、`vSyncCount=0`。測定前は 60 / 0 → 終了後に復元 OK)。区間 flight = 投げてから 4 s、cycle = 全員が Idle へ戻るまで。
2. **合計と予算**: flight の frameTime 平均 6 匹 / Lab 1 匹 = **2.62 / 2.86 ms (× 0.92)**、最大 9.22 / 27.05 ms。6 匹の方が小さいのは測定の揺れの範囲で、「6 匹にしても総 frameTime はほぼ変わらない」とだけ読む。90 fps の予算 11.1 ms に対する割合は Desktop の値なので VR の予測に使わない。(上限適用前の run `20260924-193126-f2b014` は 2.62 / 2.48 ms (× 1.05)、最大 9.34 / 26.66 ms。)
3. **寄与 (計画器)**: 候補の評価回数 (`planEvaluations`) は 1 投げあたり 6 匹合計 **355** / Lab 28 (× 12.68。適用前は 316 / 28 = × 11.29、犬ごとには Lab 28、他の 5 匹は各 65 で差の理由は未調査)。評価回数が 12 倍でも総 frameTime はほぼ変わらなかった。Issue の範囲にあった再計画タイミングの分散・軌道のパス間再利用は入れていない (試作シーンの値では急ぐ理由が見えない。本シーンで測ってから判断する)。
4. **描画コール**: flight の batches 6 匹 / Lab = 19 / 18 (UnityStats)。適用前の cycle (持ち帰りまで) の最大は 6 匹で 43、一周の所要は 6 匹 13.7 s / Lab 9.0 s。
5. **この測定で言えないこと**:
   - 試作シーンの値。本シーン (地形・草木・遠景・他の犬の徘徊・リード・メニュー) の負荷は含まない。
   - Desktop の frameTime は VR (解像度・2 眼・90 fps) の予測に使えない。使えるのは比率と描画コール数だけ。
   - 単一 ClientSim クライアントなので、犬の Owner が 1 人に集まった場合の負荷。Owner が分かれた実機の分担は測っていない。
   - 描画コール数は `UnityEditor.UnityStats` の値で、Game 画面の Statistics とは別の計測。同じ計測どうしの比だけを読む。
   - 6 匹同時の回では、犬ごとの取り方 (jump / stand / over) と動きの質を判定していない (Console の行が犬を名乗らないため。state で「取れた・一周した」だけを見る)。
   - **batches 19 / 18 は「1 匹 → 6 匹」の描画コストではない** — Lab だけの回でも試作シーンには 6 匹とも存在している (Codex 注記)。
   - **× 0.92 (適用前 × 1.05) は総 frameTime の比で、Udon の処理時間の比ではない** (Codex 注記)。計画器の重さは評価回数の比で読む。

副作用: ハーネスの実行後に `ProjectSettings/QualitySettings.asset` の `m_CurrentQuality` が 2 → 3 に変わる。司令塔が実行ごとに `git checkout -- ProjectSettings/QualitySettings.asset` で戻した (手順は [kh-frisbee-harness](../testing/kh-frisbee-harness.md#6-匹同時の負荷---modesixdogs-imp-dog-012))。

## コードレビュー (Codex D + M、2026-09-24、gpt-6-astra) と対応

| 指摘 | 重さ | 対応 |
|---|---|---|
| D1: facts の `maxRootRise` が計画器に未適用で、実際の上限は全犬種共通 0.9 m | 中 | 修正。F1 が式で facts に書き、F2 が `ConfigureCatcher` と Verify に通した。司令塔が下限を 0.45 に (上の「跳躍上限」) |
| M1: facts の事前検査が存在確認だけ | 中 | 修正。F2 の `ReadFacts` (内容検査) / `ReadAllFacts` (シーン変更前に全犬種を読む) |
| M2: ハーネスの新オプションと手順書の契約 | 低 | 修正。cjs のコメント (F2)、[手順書](../testing/kh-frisbee-harness.md) の `--breed` / `sixdogs` 節 |

問題なし: D (1) 分割・同期・Owner、(2) 6 匹の独立配線、(3) facts の犬種選択、(4) ハーネスの副作用、(5) U#、(6) 負荷記録の境界。M (1) BreedSpec、(2) ファイル境界・改名、(3) Build / Verify、(4) 名前・座標。負荷についての注記 2 点は「この測定で言えないこと」に入れた。

## 未実施

- PCVR (下のチェックリスト)。
- 全 36 投げ × 6 犬種のハーネス。
- 本シーンでの 6 匹同時の負荷 (地形・草木込み)。
- Shiba / Samoyed の縮小表を上限適用後に取り直すこと。

## PCVR チェックリスト (Build & Test し直してから)

事前: Console エラー 0 / Walking・Menu Verify 失敗 0 / アップロードし直した / 投げ手は広場の中央付近に立つ。

### §0 エディタで判定できない項目

- [ ] 6 匹とも、自分の色のディスクを投げると跳んで取り、持ち帰って置く
- [ ] **犬種ごとの姿勢**: ため・跳躍・着地で脚が地面を突き抜けない、腹が床に付かない、胴・尾が不自然に折れない (特に Corgi / Dachs の胴の長さ)
- [ ] 口がディスクの高さに届いて見える (小型犬が口の手前で空振りに見えないか)
- [ ] **跳躍の高さ**: 高い投げ (lob) で、犬種別の上限 (Corgi / Pome / Dachs 0.45 (下限) / Shiba 0.58 / Samoyed 0.815 / Lab 0.9 m) が体高に対して自然か。**小型犬の 0.45 m は Pome の体高の約 2.9 倍** — 跳びすぎに見えないか、逆に低すぎて取れない犬種がないかを記録
- [ ] ひねり (v4) が 6 犬種とも 90° ひねって取り、着地で前を向く
- [ ] 胴体の追従を切った 5 匹 (Corgi / Pome / Dachs / Shiba / Samoyed) の歩き・向き変えが硬く見えないか

### 1 人 (PCVR)

- [ ] メニュー「おもちゃ」の 6 行すべてで [ディスク] が押せ (「まだ」が出ない)、その犬の手元へ来る
- [ ] **複数ディスクの同時**: 2〜3 枚を続けて投げると、それぞれの犬が自分のディスクだけを追う (他の犬のディスクを取りに行かない)
- [ ] 6 枚を順に投げたとき、犬同士がぶつかる・同じ場所で重なる見え方が気にならないか
- [ ] あそぶタブ「フリスビーの取り方」を切り替えると、ディスクを持つ犬全部が同じプリセットになる
- [ ] **リード中・集合中の除外**: リードに繋いだ犬・集合中の犬は、自分のディスクを投げても追わない (受理済みのリード・集合が勝つ)。外すと再び遊べる
- [ ] 1 匹が追走中に別の犬をリードに繋ぐ・集合させても、追走中の犬は影響を受けない
- [ ] 6 匹同時に遊んでいる間、体感で重くならないか (本シーンの負荷は未測定)

### 2 人 (実機)

- [ ] **Owner 分裂**: 犬の Owner でない人が投げても、その犬が取る (投げ手 → 犬の Owner へ要求)
- [ ] 2 人が別の犬のディスクを同時に投げても、それぞれ取る
- [ ] 途中参加者の画面で 6 匹の追走・跳躍が滑らか

### 症状 → 見るところ

| 症状 | 見るところ |
|---|---|
| ある犬種だけ姿勢が崩れる | `Temp/KH_Frisbee/<Breed>/sheet-side*.png` と比べる。値は `KH_FrisbeeJumpClipEditor.BreedSpec`。直したら Frisbee/2 → 2b → 3 → 4 → 5、Walking/3 → 5 |
| 口が届かず `catch MISS` が続く | その犬種の facts (`KH_Jump_<Breed>.json` の口の位置) と Catch の値の一致 (Walking/5) |
| 別の犬のディスクを追う | Catch とディスクの配線 (Walking/5 の犬種ごとの検査)、`KH_ToyRecall.discs` の並び |
| [ディスク] が「まだ」のまま | その犬の `discs[i]` が null (Walking/3 の配線漏れ) |
| 硬く見える | `…/PB3/inu` の ParentConstraint を切った影響。「ジャンプ中だけ切る」へ戻す案 |
| 重い | 本シーンで sixdogs 相当を測る (未実施)。計画器の評価回数は `_planEvaluations` |
