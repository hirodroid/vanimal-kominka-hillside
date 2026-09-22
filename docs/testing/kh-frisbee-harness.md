# フリスビーの ClientSim ハーネス

犬のフリスビーキャッチ (IMP-DOG-008) を **AI が Unity の Play で繰り返し試し、結果を JSON で読む**ための台。
ユーザーが毎回 Play して目で見る代わりに、決まった投げを決まった順で流し、
コンソールの `[KH_Frisbee]` / `[KH_FrisbeeCatch]` 行から成否を機械的に分類する。

**これは人の Play を置き換えない。** 取れたか取れなかったかの数は出るが、
「見ていて気持ちいいか」「不自然でないか」は出ない。判断はユーザーが Play で行う。

- 台本 (Editor): `Assets/KominkaHillside/Scripts/Editor/Testing/KH_FrisbeeHarnessEditor.cs`
- 入口 (CLI): `scripts/kh-frisbee-harness.cjs`
- 出力: `Logs/kh-frisbee-harness/<runId>/report.json` と `Logs/kh-frisbee-harness/latest.json` (Logs は Git 管理外)

---

## 前提

- Unity 2022.3.22f1 / Worlds SDK 3.10.5 / Synaptic AI Pro 接続済み。
- **Edit モードで、コンパイルエラー 0 件、開いているシーンが未変更**であること。
  どれか 1 つでも欠けると `BLOCKED` で何もしない (シーンの保存も破棄もしない)。
- プロトタイプシーン `Assets/Scenes/KH_FrisbeePrototype.unity` が存在すること
  (無ければ `Tools/KominkaHillside/Frisbee/4 Build Scene`)。

## 実行

```bash
node scripts/kh-frisbee-harness.cjs list      # 投げの表と、保証しないもの
node scripts/kh-frisbee-harness.cjs start     # Unity のメニューを叩くだけ
node scripts/kh-frisbee-harness.cjs status    # latest.json の要約 (PASS なら終了コード 0)
node scripts/kh-frisbee-harness.cjs stop      # 停止フラグ + メニューの停止
```

`start` が返す `START_REQUEST_ACCEPTED` は **受け付けただけで PASS ではない**。
結果は必ず `status` (または report.json) で読む。

絞り込みと時間の上書き:

```bash
node scripts/kh-frisbee-harness.cjs start --variants=0,4 --shots=flat,low --budget=300
```

`--variants` / `--shots` / `--budget` は `Logs/kh-frisbee-harness/options.json` に書かれ、
Unity 側が起動時に 1 度だけ読む。Unity のメニュー
`Tools/KominkaHillside/Frisbee/7 Harness (ClientSim)` から直接呼ぶこともできる
(その場合は前回の options.json がそのまま効く)。停止は `8 Harness Stop`。

## 何をするか

1. 今開いているシーンのパスを覚え、**未変更であることを確認**してからプロトタイプシーンを単独で開く。
2. Play へ入り、ClientSim のローカルプレイヤーが出るまで待つ (30 秒で打ち切り)。
3. プリセット `v` = 0..5 それぞれについて、次の 6 投げを順に流す (合計 36 投げ)。

   | 投げ | speed | elevationDeg | sideDeg | 想定内の結果 | 期待する終わり方 (`expectKind`) |
   |------|-------|--------------|---------|--------------|--------------|
   | flat | 7.5 | 10 | 0 | caught | `jump` (跳んで取る) |
   | mid  | 5.8 | 8  | 0 | caught | `any` (固定台では犬が 10.5 m 先で、立って取る / 追い抜く幾何になる) |
   | lob  | 6   | 35 | 0 | caught | `jump` |
   | low  | 5   | 0  | 0 | caught / ground | `ground` (犬の手前に落ちるので拾いに行く) |
   | far  | 9   | 15 | 0 | caught | `jump` |
   | side | 7   | 10 | **25** | caught / ground | `any` (round 5 で追加。横の幾何を通す) |

   `expectKind` は「取れたか」とは別の列 (Codex M2)。**v4 (ひねり) が flat / lob / far で
   跳ばずに立って取るのは 4 回目の回帰そのものなので、`caught` でも不合格**にする。

   `side` (round 5、2026-09-22) は**合否を足すための投げではない**。固定台は +Z の一本槍なので、
   回り込み・旋回・横からの踏み切りが 1 度も測られていなかった。取れ方も結果も問わず
   (`expectKind: any` / `allowed` に `ground` を含む)、**`etaErr` を横の幾何でも採る**ために走らせる。

   `over` (走り抜けざまに取る) は round 5 から**全プリセットで出うる** (v3 だけの動きではなくなった)。
   `overtake catch OK` の行は `outcome = caught` になるので、`allowed: [caught]` の投げでも落ちない。
   表は Unity 側 (`KH_FrisbeeHarnessEditor.Shots`) と CLI 側 (`SHOTS`) の両方にあるので、
   直すときは 2 か所そろえる。

   - プリセットは `KH_FrisbeeVariant` の cube (`cycleVariant`) を `Interact()` で叩き、
     捕り手の `variant` が目的の番号になるまで回す。犬が動いている間の要求は保留されるので、
     毎回 Idle を待ってから回す。
   - **投げる前に犬をシーンの立ち位置 (1.5, 0, 6.5 / 南向き) へ戻す** (唯一の注入。`report.injection`)。
     円盤の recall は円盤しか戻さず、犬は咥えて人の足元まで運んで置くので、これをしないと
     2 投げ目以降が「13-18 m 離れた所からの投げ」になり、同じ投げの比較にならない
     (実測: 戻さないと 24 投げ中 12 しか想定内にならず、戻すと 22)。
   - 投げは `KH_FrisbeeStand` の UdonBehaviour に `speed` / `elevationDeg` / `sideDeg` を書き、
     `Interact()` を 1 回。固定台は常に +Z へ投げる。
   - 投げてから捕り手の `state` が Idle (0) へ戻るまで、**最大 16 秒**待つ。
     遠投 (far) は取ってから人の足元まで咥えて戻り、置いて Idle へ戻るまでに 12-14 秒かかるので、
     10 秒では「取れたのに一周が終わっていない」投げが出ていた (Codex M1)。
   - 次の投げの前に `KH_FrisbeeRecall` を `Interact()` して円盤を台へ戻す。
4. 完了・失敗・停止・時間切れのいずれでも Play を抜け、**覚えておいた元のシーンを開き直す**。
   Play は SDK の NetworkID 復旧でシーンを dirty にするので、開き直しでその差分を捨てる
   (**保存はしない**。実行前に全シーンが未変更であることを確認しているので、捨てるのは Play 由来の差分だけ)。
   プロトタイプシーンのファイルハッシュを実行前後で比べ、`sceneFileUnchanged` に残す。

## 結果の読み方

`report.json` の `status`:

| status | 意味 |
|--------|------|
| `PASS` | 計画した投げがすべて走り、すべて `ok` (下の 4 条件すべて) で、Console のエラーが 0 |
| `FAIL` | `ok` でない投げがあった / Console にエラーが出た |
| `BLOCKED` | 前提を満たさない、時間切れ、停止、途中でシーンが変わった。**結果が出ていないという意味で、合格でも不合格でもない** |

**1 投げの `ok` は 4 つ全部**。どれか 1 つでも落ちればその投げは不合格:

| 列 | 意味 |
|----|------|
| `outcomeOk` | `outcome` がその投げの想定内 (`allowed`) に入っている |
| `completed` | 16 秒の窓の内に Idle へ戻り、**一周 (取る → 持ち帰る → 置く) が終わった** (Codex M1) |
| `motionOk` | 動きが機械でない (下記) |
| `expectOk` | `kind` が `expectKind` と一致した (Codex M2) |

`caught` (取れたか) と `completed` (一周終わったか) は**別の列**として出る。
取れても持ち帰りが終わらなければ `ok` にならない。

### `eta` — 合否ではない観測値 (round 5)

`status` / `report.json` の `eta` は、計画を決めた瞬間の見積もり (`Eta`) と、実際に着いた時刻の差
(`etaErr` = 実際 − 見積もり、秒。**正 = 見積もりが楽観的**) を 1 走行ぶんまとめたもの。
着いた瞬間は待機系なら保持帯へ入った時、踏み切る計画なら屈み始め、追い抜きなら点に届いた時。

**合否には一切使っていない。** 足した理由は、計画が土壇場で落ちる症状に対して
「余裕 (`HookEtaMargin`) を広げる」のか「`Eta` 自体に係数を入れる」のかを、
感触ではなく分布で決めるため。`verdict` の読み方:

| verdict | 意味 |
|---------|------|
| `OK` | median がほぼ 0。見積もりと実際が一致している |
| `SLIGHTLY_OPTIMISTIC` | median 0.05〜0.15 s。既存の余裕の内 |
| `OPTIMISTIC` | median > 0.15 s (= `HookEtaMargin` を食い潰す)。**余裕を広げるのではなく `Eta` に係数を入れる**ことを検討する |
| `PESSIMISTIC` | median < −0.15 s。候補を必要以上に捨てている可能性 |

**「着いた」をどこで採るかで答えが変わる。** 最初の実装は、勢いのまま跳ぶ計画の到着を*屈み始め*で採っていた。屈み始めの時刻は `_catchT` から逆算された固定値なので、`etaErr` が計画の `slack` と恒等になり、median +0.24 s の「一貫して楽観的」という**偽の結論**が出た。到着を**踏み切り点へ実際に届いた時**へ直すと median +0.115 s (30 標本、2026-09-22) で余裕の内。
**この列を読むときは `at=` を必ず見る** — `reach` / `hold` / `overtake` は実際に着いた時刻、`crouch` は「着かないまま屈み始めた」フォールバック (`dist=` にそのときの残り距離が出る)。

1 投げごとの `outcome`:

| outcome | 判定 |
|---------|------|
| `caught` | `catch OK` / `stand catch OK` / `overtake catch OK` の行があった |
| `miss` | `catch MISS` 系の行があり、`catch OK` が無い |
| `ground` | 最後の `plan=` が `plan=2` (落ちたものを拾いに行く) で、キャッチの行が無い |
| `none` | どの行も出なかった (投げ自体が出ていない / 犬が反応しなかった) |
| `timeout` | 16 秒で Idle へ戻らなかった |
| `blocked` | プリセットを合わせられなかった |

1 投げごとの `kind` (どう終わったか):

| kind | 判定 |
|------|------|
| `jump` | 素の `catch OK` / `catch MISS` (跳躍中にしか出ない行) |
| `stand` | `stand catch ...` (立ったまま) |
| `over` | `overtake catch ...` (走り抜けざま。round 5 から全プリセット) |
| `ground` | `plan=2` で落ちたものを拾いに行った |

`motionOk` (round 4 + Codex M3): 踏み切り / キャッチの行の `mode`・`yawRateMax`・`speedMin`・`vTake` で判定。
急旋回 (> 300 deg/s) は全 mode で不合格。`wait` 以外で踏み切り直前 1 秒の最小速度が 1.5 m/s 未満も不合格
(助走が 2.5 m 未満の投げは速度では判定しない)。**跳んだ / 取ったのに行に数字が無い場合も不合格** —
判定できないことを合格として数えない。

各投げには `planLine` (最後の `plan=`)、`takeoffLine`、`outcomeLine`、拾った
`[KH_Frisbee*]` の行がそのまま残る。プリセットごとの数は `perVariant` (`completed` の数も入る)。

## 時間の上限

- 1 投げ: 判定の窓 16 秒 + 番犬 18 秒 (投げてから判定まで。プリセット合わせと片付けは含まない)。
- 全体: 240 秒を下限に、**計画した投げ数 x 28 秒 + 30 秒**。
  6 プリセット x 6 投げ = 36 投げの全面実行は 240 秒では収まらないため
  (1 投げで走って跳んで咥えて戻すまで 6-14 秒かかる)、既定でこの式を使う。
  `--budget` で上書きできる。上限に当たった実行は `BLOCKED` で、そこまでの投げは残る。
- Editor が一時停止 (Pause) された場合も自動再開はせず打ち切る。

## 保証する範囲

実際に通るもの:

- 本番の UdonSharp (`KH_Frisbee` / `KH_FrisbeeCatch` / `KH_FrisbeeLauncher`) の Udon VM 実行、
  Update / LateUpdate、Animator、飛行モデル、プランナーと跳躍の実数値。
- 観測はすべて**ランタイムの `UdonBehaviour.TryGetProgramVariable`** から読む
  (Play 中の U# プロキシの値で合格にしない)。
- 投げの入力は本番の `KH_FrisbeeLauncher.Interact()` 経路。

**通っていないもの (`notCovered`)。PASS でも NOT_TESTED のまま:**

- **広場の縁 (playArea)。** 試作シーンの捕り手は `playAreaRadius = 0` (広場無し) なので `DogMayGo` は常に真で、
  **着地点が広場の外へ出る候補を落とす規則 (Codex D1) はここでは 1 度も働かない**。
  この規則は本シーン (`Walking/5 Verify`) と試作 Verify の幾何検査 (`LandingOf` / `InCircleXZ` を
  模擬の円に当てる) で見ており、実際の縁での着地は実機 Play のチェックリストで見る。
- **Ready (持ち手が構えている間の待機位置)。** プロトタイプに持ち手がいないので犬は Ready へ入らず、
  走り込み系プリセット v1..v3 は「どこで待つか」しか違わない = **ここでは v0 と同じ投げになる**。
  v1..v3 の PASS は「同じ投げを繰り返しても壊れない」以上の意味を持たない。
- VR の手投げ・狙い表示 (aim line / marker / 広場外の警告)。固定台の +Z の投げだけ。
- `Interact()` を直接呼ぶので、実際のレイキャスト・距離 (proximity)・InteractionText の当たり判定。
- 他プレイヤー、Owner 移譲、同期 (単一 ClientSim クライアント)。
- 体験としての見え方 (間・迫力・不自然さ)。**人の Play でしか判定できない。**
- 実機ビルド (PCVR / Quest / iOS) と実クライアント。

## 直近の実測 (2026-09-22, Codex 是正後)

6 プリセット x 5 投げ = 30 投げ、**`PASS` 30/30** (run `20260922-145701`、Console エラー 0、
`sceneFileUnchanged=true`、所要 5 分 04 秒)。判定の 4 条件 (outcomeOk / completed / motionOk / expectOk)
すべてを入れた後の数字。

- `flat` / `lob` / `far`: 全プリセットで `caught` かつ `kind=jump` (`mode=run`)。`far` は
  助走 3.8-4.5 m・踏み切り 3.2-3.7 m/s・跳ぶ前 1 秒の最小速度 2.6-2.9 m/s。
- `mid`: 全プリセットで `caught` / `kind=stand` (`mode=wait`)。固定台では犬が 10.5 m 先なので
  立って取る幾何になる (`expectKind=any` の理由)。
- `low`: 全プリセットで `ground`。想定内。
- `yawRateMax` は最大 240 deg/s (上限 300)。急旋回 0。**一周 (持ち帰って置く) も 30/30 完了**
  (`far` は 11.1-11.3 秒かかるので、10 秒の窓では落ちていた)。

## 初回の実測 (2026-09-21, 参考値)

6 プリセット x 4 投げ = 24 投げ、`FAIL` (22/24 が想定内、Console エラー 0)。

- `flat` / `lob`: v4 (Twist) の `flat` を除きすべて `caught`。
- `low`: 全プリセットで `ground` (犬の手前 6.5 m に落ちるので拾いに行く)。想定内。
- `far`: v0-v3, v5 で `caught` (v3 / v5 は `stand catch OK`)。
- **v4 (Twist) の `flat` と `far` だけ `catch MISS`** (`best mouth-edge=1.27 / 1.30`,
  `dxz=1.39 / 1.74`)。再現する。ひねりのクリップの口の位置が絡む問題として別途追う。

この数字は**この固定投げに対する結果**であって、手投げ・VR・他人がいる場での結果ではない。

## 途中で止まったとき

- `node scripts/kh-frisbee-harness.cjs stop` で停止フラグを置く (Unity 側は 0.5 秒ごとに見る)。
  結果は `BLOCKED`。
- Unity が落ちるなどして `Logs/kh-frisbee-harness/journal.json` が残ると、次の `start` は拒否される。
  Edit モードに戻してから `stop` を 1 回叩くと、その journal を `BLOCKED` として閉じる。
- ハーネスはシーンを保存しない。`Logs/` 以下を手で消して解決しようとしない
  (消すなら report ごと消す。journal だけ消すと記録が残らない)。
- 実装メモ: Play 退出後の片付け (シーンを戻す・journal を閉じる) は
  `EditorApplication.delayCall` ではなく `EditorApplication.update` で行う。
  delayCall は Play 退出まわりのドメイン再読み込みで登録が消え、**シーンが戻らないまま
  journal だけ残る**状態を実測した (2026-09-21)。予約は SessionState にも置くので、
  再読み込みをまたいでも次の tick で片付く。判定済み (`ended` あり) の journal が
  残っている状態は、次のコンパイル後に自動で閉じる。

## 設計の出どころ

古民家ドッグランの `KD_ClientSimHarnessEditor` / `scripts/kominka-harness.cjs`
(`C:/work/unity/vcc/KominkaDogRun/docs/testing/kominka-harness.md`) と同じ原則:
入口は固定のメニュー 1 つ、終了も固定 (どんな終わり方でも Edit モードへ戻る)、
シーンを保存しない、保存シーンにコンポーネントを足さない、結果はファイル、
`START_REQUEST_ACCEPTED` は PASS ではない。
