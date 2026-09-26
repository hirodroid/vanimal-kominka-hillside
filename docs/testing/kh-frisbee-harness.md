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
node scripts/kh-frisbee-harness.cjs list      # 投げの表・netorder のケース表と、保証しないもの
node scripts/kh-frisbee-harness.cjs start     # Unity のメニューを叩くだけ
node scripts/kh-frisbee-harness.cjs status    # latest.json の要約 (PASS なら終了コード 0)
node scripts/kh-frisbee-harness.cjs stop      # 停止フラグ + メニューの停止
```

モードは 3 つ。既定は下の 36 投げ + 位置取りの 2 投げ (IMP-DOG-017、`--shots` を省略すると 38)、`--mode=netorder` は
[ネット順序モード](#ネット順序モード---modenetorder-bug-dog-003) の 5 ケース x 2 周 = 10 投げ、
`--mode=sixdogs` は [6 匹同時の負荷](#6-匹同時の負荷---modesixdogs-imp-dog-012) (IMP-DOG-012)。
既定モードと netorder は `--breed=<Key>` で[犬種のレーン](#犬種のレーン---breed-imp-dog-012)を選ぶ (既定 Lab)。

`start` が返す `START_REQUEST_ACCEPTED` は **受け付けただけで PASS ではない**。
結果は必ず `status` (または report.json) で読む。

絞り込みと時間の上書き:

```bash
node scripts/kh-frisbee-harness.cjs start --variants=0,4 --shots=flat,low --budget=300
```

`--variants` / `--shots` / `--budget` / `--mode` / `--repeats` / `--breed` は `Logs/kh-frisbee-harness/options.json` に書かれ、
Unity 側が起動時に 1 度だけ読む。Unity のメニュー
`Tools/KominkaHillside/Frisbee/7 Harness (ClientSim)` から直接呼ぶこともできる
(その場合は前回の options.json がそのまま効く)。停止は `8 Harness Stop`。

## 変更に応じた実行範囲

調整中は再現ケースと影響する周辺ケースを選ぶ。選んだ条件と理由、結果、今回走らせない範囲を既存の作業記録へ短く残す。部分PASSを全体PASSにしない。

| 変更 | 選び方 |
|---|---|
| 文書・通常コメントだけ | ハーネス不要。検査用コメントや期待表を変えた場合はその影響も確認 |
| 特定プリセットの調整 | 対象プリセットの再現投げ＋境界条件と、通常プリセットの関連投げ。ひねりならv4のflat/lob/farとv0の関連投げ等、実際の差分から選ぶ |
| 全プリセット共通の移動・捕獲・復帰処理 | 調整途中は再現ケースで確認し、まとまった変更の確定前は既定モード全体。保留・取消・世代管理も変えるならnetorderを追加 |
| 通知とheldの順序・保留・取消 | netorder＋既定モードの影響する通常投げ。実通信の到着順やOwner移譲は別途実機へ引き継ぐ |
| フリスビー機能の確定・公開前 | 既定モード全体とnetorder、該当する本シーン・実機確認。対象版の既存結果は関連コード・依存・条件が同じ場合に再利用可 |

全体の選択条件はCLIから明示し、実行後のreportで予定ケースを照合する。メニュー直実行は前回optionsが残るので全体実行の保証にならない。

```bash
node scripts/kh-frisbee-harness.cjs start --mode=default --variants=0,1,2,3,4,5 --shots=flat,mid,lob,low,far,side
node scripts/kh-frisbee-harness.cjs start --mode=default --shots=beside,chase
node scripts/kh-frisbee-harness.cjs start --mode=netorder --repeats=2
```

1 行目が従来の 36 投げ (比較できる基準)。2 行目は IMP-DOG-017 の位置取りの 2 投げで、`--variants` に関係なく各 1 回。
`--shots` を省略すると 1 回の実行で両方 (38 投げ) を流す。

この2コマンドは各runの終了・復旧を確認してから順に実行する。同じコード・条件の合格を修正のたびに取り直さない。失敗時は再現条件を絞って原因を調べ、変更前からの失敗は証拠付きで別件へ残す。今回の回帰や必須条件の失敗を既知扱いで除外せず、期待値を緩めて通さない。

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

   **位置取りの投げ (IMP-DOG-017、2026-09-26)。** 上の 6 投げは犬を落下点の横 (1.5, 0, 6.5) から走らせるので、
   「落下点の後ろから追いついて跳ぶ」(取り方 `chase`) が 1 度も測られていなかった。次の 2 投げは
   **自分のプリセットで 1 回だけ**流し (`--variants` に関係しない。`--shots` で選ばれたときだけ)、
   犬をシーンの立ち位置ではなく**投げ台からの相対位置**へ置く (横は立ち位置のある側 = +x):

   | 投げ | プリセット | 犬の置き方 | speed / elev | 想定内の結果 | 期待 |
   |------|-----------|------------|--------------|--------------|------|
   | beside | v0 | 投げ台の横 0.6 m、投げ台の方を向く (よしよしの後に投げる流れ) | 6 / 10 (PCVR 09-25 の実測 5.5〜7.1 の中央値) | caught (Lab は ground も) | Lab 以外: `jump` かつ `mode=chase`。Lab: `jump` / `over` / `ground` のどれか (跳んだなら `mode=chase`) |
   | chase | v3 | v3 の待機位置 = 投げ台の前 `ReadyAhead3` (4 m)・横 `ReadyLateral3` (2.5 m)、投げる方向 (+Z) を向く | 7 / 10 | caught | 全犬種 `jump` かつ `mode=chase` |

   - 跳んだ投げの `mode` は `expectOk` で見る (`expectMode`)。`mode` は踏み切り / キャッチの行の `mode=` (無ければ takeoff 行)。
   - 試作シーンには持ち手がいないので犬は Ready に入らない。chase は**置く位置で v3 の待機の幾何を作る**
     (実際の Ready は持ち手の頭の向きから測る。ここでは投げ台を持ち手とみなす)。
   - 置いた位置と向きは投げごとの `dogStart` (`x,y,z yaw=… (beside)`) に残る。

   - プリセットは `KH_FrisbeeVariant` の cube (`cycleVariant`) を `Interact()` で叩き、
     捕り手の `variant` が目的の番号になるまで回す。犬が動いている間の要求は保留されるので、
     毎回 Idle を待ってから回す。
   - **投げる前に犬をシーンの立ち位置 (1.5, 0, 6.5 / 南向き) へ戻す** (唯一の注入。`report.injection`)。
     円盤の recall は円盤しか戻さず、犬は咥えて人の足元まで運んで置くので、これをしないと
     2 投げ目以降が「13-18 m 離れた所からの投げ」になり、同じ投げの比較にならない
     (実測: 戻さないと 24 投げ中 12 しか想定内にならず、戻すと 22)。
     位置取りの投げ (beside / chase、IMP-DOG-017) だけは立ち位置の代わりに投げ台からの相対位置へ置く (上の表)。
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

以下は既定モード (36 投げ) の話。`--mode=netorder` の読み方は
[ネット順序モード](#ネット順序モード---modenetorder-bug-dog-003) を見る
(`status` は `PASS` / `FAIL` / `BLOCKED` の意味だけ共通)。

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
| `OPTIMISTIC` | median > 0.15 s (= 旧 `HookEtaMargin` を食い潰す量。定数は IMP-DOG-017 で廃止、閾値の数字は据え置き)。**余裕を広げるのではなく `Eta` に係数を入れる**ことを検討する |
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
`mode` は IMP-DOG-017 から `around` (回り込み) / `chase` (追いつき) / `front` (正面・横) / `stand` (立ち取り) / `overtake` (並走取り)
(旧 `hook` / `run` / `wait` は廃止。旧 `stand` は勢いのある直進の名前だったが、今の `stand` は旧 `wait` の立ち取り)。
急旋回 (> 300 deg/s) は全 mode で不合格 (IMP-DOG-017 から yawRateMax は walkSpeed 以上で走っている区間だけを数え、静止からの向き直りは `launchYaw=` に別記。speedMin も 1.5 m/s に初めて達した後だけ)。`around` と `stand` 以外で踏み切り直前 1 秒の最小速度が 1.5 m/s 未満も不合格
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

## ネット順序モード (`--mode=netorder`, BUG-DOG-003)

```bash
node scripts/kh-frisbee-harness.cjs start --mode=netorder            # 5 ケース x 2 周 = 10 投げ
node scripts/kh-frisbee-harness.cjs start --mode=netorder --repeats=3
node scripts/kh-frisbee-harness.cjs status                           # netCase / netOk / req の行
```

### 目的

投げの通知 (ネットワークイベント) と円盤の `IsHeld` (VRCPickup の同期) は**別の経路で届く**ので、
犬の Owner では「通知は着いたのに円盤はまだ手の中に見える」という順序が起こる。
これが BUG-DOG-003 (Master 以外が投げると犬が動かない) の原因で、
`KH_FrisbeeCatch` は通知をいったん**保留 (pending)** し、円盤が手から出たと見えた最初のフレームで受理する。

このモードは**その順序だけ**を 1 クライアントで作って、保留の論理 5 つを通す:
保留する / 手を離した最初のフレームで受理する / 期限を越えたら捨てる / 掴み直しで取り消す /
追走中に奪われて投げ直されたら古い投球を捨てて新しい方だけを通す。

順序は捕り手の `debugHeldOverride` (`0` = 実値 / `1` = 保持扱い / `-1` = 解放扱い) を
ハーネスが投げの前後で書いて作る。切り替えの時刻は**すべて `KH_FrisbeeCatch.PendingHoldSeconds`
から導く** (Editor 側は定数を直接参照するので、定数を変えればケースも追従する。秒の写経はしない)。
以下 `Hold` = `PendingHoldSeconds`。

**この変数が捕り手に無ければ `BLOCKED`。** FAIL ではない (順序が壊れていると分かったのではなく、測れていない)。

### ケース表

5 ケースすべて必須。プリセットは v0 固定、投げは `flat` 1 本だけ (順序を見るモードなので幾何は変えない)。
表は Unity 側 (`KH_FrisbeeHarnessEditor.NetCases`) と CLI 側 (`NET_CASES`) の両方にあるので、
直すときは投げの表と同じく 2 か所そろえる。

| case | 投げる前 | 切り替え | 期待する順序 | `netOk` |
|------|----------|----------|--------------|---------|
| `held-first` | `1` (まだ手の中)。犬が Ready になるまで最大 3 s 待つ | Interact の `Hold x 0.25` 後に `0` | `req hold` → `req resolved` → `throw` → `plan=` | 左の順序 + `outcome` が `caught` か `ground` + `completed` + **持ち帰りの証拠** (下記) |
| `release-first` | `0` (従来どおり) | なし | `req accept path=local` (`req hold` は出ない) | 左 + **既定モードの 4 条件** (outcomeOk / completed / motionOk / expectOk) + **持ち帰りの証拠** |
| `expired` | `1`、Ready 待ち | `Hold x 1.3` まで `1` のまま → `0` に戻し、**さらに 2.0 s 観測** | `req hold` → `req expired`、その後は静か | 左 + 失効の後に `throw` / `plan=` / `req resolved` が 1 行も出ない + state が Idle/Ready のまま + 2.0 s を最後まで観測できた |
| `regrab` | `0` | 最初の `plan=` を見たら `1` → `Hold x 0.5` 後に `0` | `req accept` → `plan=` → `req cancel reason=held-regrab` | 左 + 取消から **1.0 s 以内**に state が Idle/Ready へ戻る |
| `steal-rethrow` | `0` | `req accept` と最初の `plan=` を見たら `1` にして**その場で投げ台をもう一度 Interact** → `Hold x 0.25` 後に `0` | `req accept`(gen N) → `plan=` → **`req cancel reason=held-regrab` か `superseded`(gen N) と `req hold`(gen N+1) の 2 本 (順不同)** → `req resolved`(gen N+1) → `throw` → `plan=` | 下の**半順序**を満たすこと + 2 投げ目の**持ち帰りの証拠** |

- **持ち帰りの証拠 (Codex M1)。** 「Idle へ戻った」(`completed`) だけでは一周と数えない —
  `plan=2` のまま取り消されて Idle に落ちた投げも `completed` は真になるから。
  毎フレーム読んでいる `state` に、**咥えた state (JumpCarry `3` / Pickup `7`) か `catch OK` の行**が
  あり、**かつ 返す state (Return `8` / Drop `9`)** があることを求める。
  `report` の `netRoundTrip` / `netStates` に、通った state がそのまま残る。
- **`steal-rethrow` が見ているのは全順序ではなく半順序。** 実装が求めるのは次の 4 つだけで、
  取消 (gen N) と保留 (gen N+1) のどちらが先に出るかは**問わない**:

  1. `req accept`(gen N) < 最初の `plan=`
  2. `req cancel`(gen N, `held-regrab` か `superseded`) が `plan=` より後、かつ **`req resolved`(gen N+1) より前**
  3. `req hold`(gen N+1) が `plan=` より後、かつ **`req resolved`(gen N+1) より前**
  4. `req resolved`(gen N+1) < `throw` < `plan=`

  この 2 本の順序を固定しないのは、投げ台が投げを 1 フレーム以上遅らせる (`_pending` + Owner 要求) ためで、
  捕り手が先に「手が見えた」と気づけば取消が先、投げ直しの通知が先に届けば保留が先になる。
  **本質は「古い投球の取消が新しい投球の受理より先」**で、そこだけを固定している。
- `steal-rethrow` は 2 投げぶんを 1 ケースで走らせるので、**2 投げ目が受理された瞬間に state を数え直す**
  (1 投げ目の追走を 2 投げ目の証拠にしない)。判定の窓と番犬もこのケースだけ 6 s 伸ばす。
- `held-first` で `ground` を許すのは、保留の分だけ受理が遅れて幾何がずれるため。
  **取れ方 (`kind`) は問わない** — このモードが見ているのは順序であって跳び方ではない。
- `2.0 s` (失効の後の観測) と `1.0 s` (取消の後の猶予) は保留の長さとは無関係の**観測窓**なので、
  `Hold` からは導かない。Editor 側でも別の定数として置いてある。
- `release-first` だけ既定モードの 4 条件を合否に入れる。**保留を足しても普通の投げが今までどおり通る**
  ことを見る対照だから。
- `steal-rethrow` の投げ直しが通るのは、投げ台 (`KH_FrisbeeLauncher`) が `debugHeldOverride` を見ない
  **実値の `IsHeldNow`** で判断しているから (飛行中の円盤は「持たれていない」)。
  捕り手だけが「まだ手の中」に見えるので、投げは出るのに保留へ入る、という順序が作れる。
  取消の理由は、奪取が生きた投球に追いついたか (`superseded`) 先に手が見えたか (`held-regrab`) で変わるので、
  **どちらでも合格**にする。

### 読み方

`report.json` の `mode` が `"netorder"`、投げごとに `netCase` / `netOk` / `netNote` / `reqLines[]` が付く。

- **合否は `netOk` だけ** (`ok` は `netOk` と同じ値)。ただし `netOk` の中身はケースによって違い、
  既定モードの列を使うものと使わないものがある (`status` の `info` 列には常に全部出る):

  | case | 行の順序 | `outcome` | `completed` | `motionOk` | `expectOk` | 持ち帰りの証拠 |
  |------|----------|-----------|-------------|-----------|-----------|----------------|
  | `held-first` | 全順序 (`hold` < `resolved` < `throw` < `plan=`) | **使う** (`caught` か `ground`) | **使う** | 参考 | 参考 | **使う** |
  | `release-first` | `accept`(`path=local`) があり `hold` が無い | **使う** (`allowed`) | **使う** | **使う** | **使う** | **使う** |
  | `expired` | 全順序 (`hold` < `expired`) + 以降が静か | 参考 | 参考 | 参考 | 参考 | 使わない (そもそも走らせない) |
  | `regrab` | 全順序 (`accept` < `plan=` < `cancel`) | 参考 | 参考 | 参考 | 参考 | 使わない (追走を止めるのが正しい) |
  | `steal-rethrow` | **半順序** (上の 4 条件。`cancel` と `hold` は順不同) | 参考 | 参考 | 参考 | 参考 | **使う** (2 投げ目ぶん) |

- `netNote` に、どの行が何番目に出たか (`hold@3 resolved@7 throw@8 plan@9 ...`) がそのまま入る。
  落ちたケースはここを読めば「順序のどこで崩れたか」が分かる。
- `status` は投げごとに `firstReq` (`[KH_FrisbeeCatch] req ...` の 1 本目) を出し、
  最後に `reqLines` で全ケースの req 行を並べる。
- `netRoundTrip` / `netStates`: 判定した区間で実際に通った `state` と、持ち帰りの証拠が揃ったか。
- `overrideRestored`: `debugHeldOverride` を `0` へ戻し、**読み直して 0 を確かめた**かの記録。
  **どの終わり方 (完了・BLOCKED・停止フラグ・番犬・例外) でも戻してから Play を抜ける。**
  この値は Play 中の Udon ヒープにしかなく、ハーネスはシーンを保存しないので保存物には残らないが、
  「抜けたから大丈夫」を理由にせず、抜ける前に戻して読み直した結果をここへ書く。
  **確かめられなかった実行は PASS にしない** (Codex M2): 読み直しが 0 でない・例外・Play が先に終わった
  のいずれでも `errors` に 1 件足して `FAIL` にし、後続の終了経路でもう一度戻しにいく。
  `PASS` になれるのは `overrideRestored` が `OK:` で始まるときだけ
  (既定モードは `NOT_APPLICABLE...` のまま = 一度も書いていない)。
- `expired` は **Ready で終わるのが正しい**。既定モードと違い「Idle へ戻らなかった」だけでは
  `timeout` にしない。

### 時間の上限

1 ケースの窓・番犬は既定モードと同じ (判定 16 s / 番犬 18 s)。
**`steal-rethrow` だけ 1 ケースで 2 投げぶん走る**ので、この 2 つを 6 s ずつ伸ばす
(判定 22 s / 番犬 24 s。既定モードの 16 s / 18 s は変えていない)。
全体も同じ式 (`計画した投げ数 x 28 s + 30 s`、下限 240 s) なので、既定の 10 投げでは 310 s。
実際は `expired` が `Hold x 1.3 + 2.0 s`、`regrab` が数秒で終わるので、
かかるのは `held-first` / `release-first` の一周 (6-14 s) と `steal-rethrow` (10-20 s) が支配的。

### 通していないもの

**netorder が通すのは 1 クライアント上の順序の論理であって、通信そのものではない。**
`debugHeldOverride` で作った「通知が先、held が後」は本物の到着順の*模擬*で、
実際の到着順・Owner 移譲・`IsHeld` の同期の遅れは 2 人以上の実機でしか出ない。
下の `notCovered` の「他プレイヤー・Owner 移譲・同期」はこのモードを足しても **NOT_TESTED のまま**。

とくに、**保留中に相手が取得 → 弱く放す (別人の保持者・Owner)** は単一クライアントでは再現できない。
netorder の保持者は `debugHeldOverride > 0` のとき常に**円盤の Owner (= ローカルプレイヤー)** なので、
別人が持つ・別人へ Owner が移る・その人が弱く放す、という経路は 1 度も通らない。**2 人実機で確認する。**

## 犬種のレーン (`--breed`, IMP-DOG-012)

試作シーン `KH_FrisbeePrototype` は犬種ごとのレーンを x 方向に 12 m 間隔で並べる
(Corgi −24 / Pome −12 / **Lab 0** / Dachs 12 / Shiba 24 / Samoyed 36。地面は 120 m 四方)。
**Lab レーンは従来の名前・座標のまま**なので、Lab の結果は過去の記録と比べられる。
他のレーンは同じ配置 (犬の立ち位置・投げ台・recall・プリセットの cube・壁) を平行移動し、名前に `_<Breed>` が付く。

```bash
node scripts/kh-frisbee-harness.cjs start --breed=Corgi --variants=0,4 --shots=flat,low
```

- キーは `Corgi` / `Pome` / `Lab` / `Dachs` / `Shiba` / `Samoyed`。1 回の実行で 1 犬種。省略すると Lab。
- Lab 以外は投げの前に**プレイヤーをそのレーンの出現位置へテレポート**する (犬は投げた人へ持ち帰るため)。
  「何をするか」の立ち位置 (1.5, 0, 6.5) などは、レーンの x を足した位置になる。
- 投げの表・判定の 4 条件は既定モードと同じ。レーンがそろっていなければ `BLOCKED` (`Frisbee/4 Build Scene` で作り直す)。

## 6 匹同時の負荷 (`--mode=sixdogs`, IMP-DOG-012)

```bash
node scripts/kh-frisbee-harness.cjs start --mode=sixdogs --repeats=2
```

- **投げは v0 / flat に固定** (`--variants` / `--shots` は効かない。`--breed` を付けるとエラー。全レーンを投げる)。
- 1 周 = **Lab 1 匹だけ (参考値) → 6 匹同時**。`--repeats` 周 (既定 2) で `周数 x (1 + 6)` 犬ぶん (既定 14)。
- 区間 **flight** (投げてから 4 s) と **cycle** (全員が Idle へ戻るまで) の frameTime (平均 / p95 / 最大)、
  `UnityEditor.UnityStats` の batches / drawCalls / setPass、捕り手の計画器カウンタ
  (`_planScans` / `_planEvaluations` / `_planHookTries` の増分) を記録する。
  `_planHookTries` は IMP-DOG-017 の計画器の書き直しで無くなりうるので、無ければ 0 として読む (前 2 つは必須)。
  report の `sixdogs.rounds[]` に周ごと、その `perDog` に犬ごと、`sixdogs.ratio` に 6 匹 / Lab の比。
- **PASS = 全犬が「取った (`caught`)」かつ「一周した (`completed`)」**。`expectKind` は `any`、`motionOk` は判定しない
  (`[KH_FrisbeeCatch]` の行が犬を名乗らないので、6 匹同時だとどの行がどの犬か分からない)。
  犬ごとの取り方と動きの質は `--breed` の既定モードで見る。

**frame cap の扱い**: Play 中だけ `Application.targetFrameRate = -1` / `QualitySettings.vSyncCount = 0` で上限を外して測り、
どの終わり方でも元の値へ戻して読み直す。report の `sixdogs.frameCap` (測定前 → 測定中) と
`sixdogs.frameCapRestored` (`OK: …` なら復元済み) で確かめる。`OK` でなければ Unity の設定を手で戻す。

**保証しないもの (report の `sixdogs.cannotSay`)。PASS でも数字をこの範囲の外へ持ち出さない:**

- 試作シーン (平面・犬 6 匹・投げ台) の値。本シーン (地形・草木・遠景・他の犬の徘徊・リード・メニュー) の負荷は含まない。
- Editor の Desktop Play で上限を外した frameTime。VR (解像度・2 眼・90 fps 予算) の予測には使えない。使ってよいのは**比率と描画コール数だけ**。
- 単一 ClientSim クライアント。犬の Owner が 1 人に集まった場合の負荷で、Owner が分かれた実機の分担は測っていない。
- 描画コール数は `UnityStats` の値で、Game 画面の Statistics とは一致しない。同じ計測どうしの比だけを読む。
- 犬ごとの取り方 (jump / stand / over) の内訳と動きの質は判定していない。

**副作用 (2026-09-24 の実行ごとに発生)**: 実行後に `ProjectSettings/QualitySettings.asset` の
`m_CurrentQuality` が 2 → 3 に変わっていた (どの段で変わったかは未特定。`frameCapRestored` は OK だった)。
ハーネスの後は `git status ProjectSettings/` を見て、QualitySettings.asset が変わっていれば
Unity を Edit モードにしたまま `git checkout -- ProjectSettings/QualitySettings.asset` で戻す
(意図した設定変更と混ぜてコミットしない)。

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
- **他プレイヤー、Owner 移譲、同期 (単一 ClientSim クライアント)。**
  `--mode=netorder` は `debugHeldOverride` で「通知が先、held が後」という**順序**を作って
  保留の論理を通すが、通すのは 1 クライアント上の論理であって通信ではない。
  実際の到着順・Owner 移譲・`IsHeld` の同期は 2 人以上の実機でしか見ていない。
- **保留中に相手が取得 → 弱く放す (別人の保持者・Owner) は単一クライアントでは再現できない。**
  netorder の保持者は常に円盤の Owner (= ローカルプレイヤー) なので、別人が持つ・Owner が移る・
  その人が弱く放す、という経路は 1 度も通らない。2 人実機で確認する。
- 体験としての見え方 (間・迫力・不自然さ)。**人の Play でしか判定できない。**
- 実機ビルド (PCVR / Quest / iOS) と実クライアント。
- **複数ディスクを別の人が同時に投げる・Owner が犬ごとに分かれる** (IMP-DOG-012)。`sixdogs` は 1 クライアントが 6 台の投げ台を同時に叩くだけ。

## 直近の実測 (2026-09-24, IMP-DOG-012 の 6 匹展開)

**縮小表** (`--variants=0,4 --shots=flat,low --breed=<Key>` = 4 投げ) を 6 犬種、すべて **`PASS` 4/4**
(Console エラー 0、`sceneFileUnchanged=true`)。**全 36 投げ x 6 犬種は未実施** — 縮小表の PASS を全表の PASS にしない。

| 犬種 | run | 取り方 | eta (標本 2、合否に使わない) |
|---|---|---|---|
| Dachs | `20260924-192420-cef7a3` | jump 2 / ground 2 | PESSIMISTIC median −0.18 s |
| Corgi | `20260924-192537-d60bbf` | jump 2 / ground 2 | PESSIMISTIC −0.17 s |
| Pome | `20260924-192642-c7b922` | jump 2 / ground 2 | PESSIMISTIC −0.20 s |
| Shiba | `20260924-192747-85b601` | jump 2 / ground 2 | OK −0.08 s |
| Samoyed | `20260924-192905-c3b50d` | jump 2 / ground 2 | PESSIMISTIC −0.17 s |
| Lab | `20260924-193010-c0186c` | jump 2 / ground 2 | OK −0.15 s |

`low` は全犬種で `ground` (想定内)、`motionOk` の失敗 0。

**`--mode=sixdogs`** (`--repeats=2`、run `20260924-193126-f2b014`): **`PASS` 14/14** (全犬が取って一周した)。
flight の frameTime 平均 6 匹 / Lab = 2.62 / 2.48 ms (x1.05)、最大 9.34 / 26.66 ms、
計画器の評価回数 316 / 28 (x11.29。犬ごとに 65 か 28)、flight の batches 19 / 18、一周 13.7 / 9.0 s。
(ここまでの sixdogs の数値は上限適用前。) 上限は 60 / vSync 0 → 測定中 −1 / 0 → 復元 OK。数字の読み方と言えないことは
[6 匹展開の記録](../plans/2026-09-24-frisbee-six-dogs.md)。

**犬種別の跳躍上限を入れた後の再実行** (同日): Pome `20260924-194936-3541b9` / Corgi `20260924-195038-fd626b` /
Dachs `20260924-195141-3231f1` が 4/4 (jump 2 / ground 2、motion / expect 失敗 0、eta PESSIMISTIC −0.24〜−0.25 s)。
下限 0.30 のときの Pome (`20260924-194228-0397de`) は 2/4 (flat を立って取る stand 2) で、これが下限を 0.45 に上げた根拠。
**Shiba / Samoyed は上限適用後に再実行していない** (上の表は共通 0.9 m のときの値)。Lab は 0.9 m で不変。
上限適用後の sixdogs (run `20260924-195243-316191`) も 14/14、総 frameTime 6 匹 / Lab = 2.62 / 2.86 ms (x0.92)、
評価回数 355 / 28 (x12.68)、batches 19 / 18。batches は 1 → 6 匹の描画コストではなく (どちらの回も 6 匹がシーンにいる)、
frameTime 比は Udon の処理時間比ではない。

run `20260924-193745-d8da73` (Corgi) は文書作業中の誤起動で、2 投げ後に `stop` で止めた `BLOCKED`。結果として読まない。

## 2026-09-23 の実測 (BUG-DOG-003 の保留を入れた後)

### `--mode=netorder`

5 ケース x 2 周 = 10 投げ、**`PASS` 10/10** (run `20260923-122627-de9b6a`、2026-09-23、
Console エラー 0、`sceneFileUnchanged=true`、`overrideRestored` OK)。

- `held-first` の `heldFor` は 0.25-0.27 s。これは**注入値**
  (`PendingHoldSeconds x 0.25` でハーネスが手を離す時刻) であって、**実通信の観測値ではない**。
  実際に通知と `IsHeld` がどれだけずれるかは 2 人実機の `heldFor` ログでしか分からない。
- `expired` は 1.00-1.02 s で期限切れ (`PendingHoldSeconds = 1.0` どおり)。
- `steal-rethrow` は gen N の `held-regrab` 取消が gen N+1 の `resolved` より前に出た
  (今回は取消が先。半順序なので逆順でも合格になる)。
- どの投げにも recall 由来の `req end … target=…` が 1 行入る。**参考情報**で、
  どの `netOk` の規則にも使っていない。

### 既定モード (同じビルド)

6 プリセット x 6 投げ = 36 投げ、**27/36** (run `20260923-122807-c9c769`)。
落ちたのは `lob` の miss、`far` の stand / 急旋回、`side` の急旋回。

**これは BUG-DOG-003 の変更で出た回帰ではない。** 同じ投げを変更前の HEAD `2cde145` で流した
差分実行 (run `20260923-114552-6e8727`、18 投げ中 11) でも同じ 3 つが再現する。
`2cde145` の時点で既に、下の投げ表の期待 (`far` → `jump` など) と実際の動きが食い違っている。
**別件として追う** — この食い違いを隠すために表の期待を緩めない (期待表は変更していない)。

## 2026-09-22 の実測 (BUG-DOG-003 前、参考値)

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
