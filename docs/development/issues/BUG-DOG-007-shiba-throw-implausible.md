---
id: BUG-DOG-007
type: BUG
system: DOG
status: review
priority: Low
summary: 実機 (2026-09-28 夜) で柴のディスクの投げが 22:53〜22:59 に 5 回 `implausible` で断られた。原因は 200 m の距離の上限で、柴は投げ手から約 430〜516 m 離れた場所に残っていた (周回コース 600×720 m では正当に起こる)。拒否理由を条件ごとに分けた。統括の決定で、Ready と投げの受理を犬から 40 m 以内に限り (`readyMaxDistance`、理由 `far`)、照準は × と「犬が遠くにいます」を出す。200 m の `implausible-far` は二重の安全として残す
next: compile → Course/27 (字形) → Course/92 → Course/93 → 試作の既定 (Frisbee/5)。実機で 40 m の外から持つ・投げると犬が動かず、照準に「犬が遠くにいます」が出ることを確かめる
updated: 2026-09-29
---

# BUG-DOG-007 柴のディスクの投げが implausible で 5 回断られた

## 観測 (実機 2026-09-28 夜)

ログはユーザーの PC VR クライアントのもの (`output_log_2026-09-28_22-33-09.txt`)。本人は player 1 で、6 匹の Owner だった。

| 時刻 | 投げた人 | 行 |
|---|---|---|
| 22:53:32 | 本人 (local) | `req reject reason=implausible path=local thrower=1 gen=1 state=0`、直前に `[KH_Frisbee] launch v=8.03 curve=0.00` |
| 22:57:49 / 22:58:00 / 22:58:34 / 22:58:54 | player 3 (net) | `req reject reason=implausible ... held=true state=10` (gen 1〜4) |

投げの位置はほかの投げと同じ水遊び場の周り (x ≈ 495〜505、z ≈ 285〜300)。柴はこの後 23:04 に `cannot-play` で 1 回断られ、それ以降は 5 回捕球している。

## 条件の絞り込み

`KH_FrisbeeCatch._ReceiveLaunch` の「implausible」は 5 つの条件で共有されている ([KH_FrisbeeCatch.cs](../../../Assets/KominkaHillside/Scripts/KH_FrisbeeCatch.cs))。

1. `p0` / `v0` が有限でない
2. `launchServerTime` が NaN / Inf
3. `v0.magnitude > frisbee.maxSpeed * 1.05` (maxSpeed は 6 枚とも 11)
4. **`Vector3.Distance(p0, dogRoot.position) > 200`**
5. curve が NaN / Inf、または ±1 の外

本人の投げは v=8.03、curve=0.00 で、1・2・3・5 には当たらない。player 3 の投げも同じ人の他の犬への投げは通っている。したがって**残るのは 4 (犬が投げ位置から 200 m 以上離れていた)**。その間に `CanPlay` (リード・Fetch・集合・水など) で断られていないので、柴はリードにつながれておらず、Ready (state 10) か Idle にいた。コースの水遊び場から 200 m 以上離れた場所に 6 分ほどいたことになる。広場 (x ≈ 72、z ≈ 127) へ置かれていたか、どこかで動けなくなっていた可能性がある。柴の位置のログが無いので断定できない。

## 提案

- `LogReject` の理由を条件ごとに分ける (`nonfinite` / `bad-time` / `speed` / `far` / `curve`)。`far` のときは `dogPos` と距離も出す。
- 犬が投げ手から遠いときの見え方 (ディスクだけ持てて犬が来ない) が分かりにくければ、別件でメニューの呼び寄せや案内を検討する。

## 調査 (2026-09-29、ログ)

- 柴 (dogIndex 4) のディスクの投げは gen 1 (本人の 22:53:32、この夜の最初の投げ) と player 3 の gen 1〜4。拒否の直前に、持った瞬間の Ready の行がある: 22:53:28 `ready grace: holder=495.0 m` (本人が掴んだ)、22:57:42 `515.9 m`、22:57:57 `494.6 m`、22:58:06 `491.4 m`、22:58:50 `426.8 m`。いずれも `ready go (reason=distance)` へ続く。投げ位置は持ち手のそばなので、犬と投げ位置の距離は 200 m を大きく超えていた。**4 (far) で確定**。速度 (8.03 < 11×1.05)・curve 0 も条件に当たらない。
- 柴の行 (`dog=4` の Wander・リード・Fetch) は 22:33〜23:04 に 1 行も無い。23:04:05 に水遊び場で `water begin dog=4` が出て、以降は普通に 5 回捕球している。stuck / stranded / recover も柴には無い。引っかかりではなく、**誰も連れて行かなかった柴が別の場所に残っていた**と読める (初期位置の付近かは位置のログが無いので未確定)。距離が 516 → 426 m と縮んでいるのは、持つたびに Ready の立ち位置へ走り出していたため。
- コースは 600×720 m・周回約 2.41 km ([game-design](../../game-design.md))。犬と投げ手が 200 m 以上離れるのは正当に起こる。200 m の上限は偽の通知を弾くための妥当性検査として妥当 (500 m 先の犬が走って来て捕る投げは成り立たない)。**上限は変えない**。

## 修正 (2026-09-29)

- `KH_FrisbeeCatch.ReqCatch` の `implausible` を `implausible-nonfinite` / `implausible-time` / `implausible-speed` / `implausible-far` / `implausible-curve` に分けた ([KH_FrisbeeCatch.cs](../../../Assets/KominkaHillside/Scripts/KH_FrisbeeCatch.cs))。
- far のときは続けて `[KH_FrisbeeCatch] reject far dist=… p0=(…) dogPos=(…) state=… dog=…` を 1 行出す (`LogRejectFar`、[KH_FrisbeeCatch.Diagnostics.cs](../../../Assets/KominkaHillside/Scripts/KH_FrisbeeCatch.Diagnostics.cs))。拒否 1 回に 1 行。
- 判定の順番と値は変えていない。`implausible` の文字列を読むハーネス・スクリプトは無い (検索済み)。

## Ready と受理の距離の上限 (統括の決定 2026-09-29)

問題: 持ち手が 495 m 離れていても、Idle の柴は `HolderNow` を見て Ready へ入り、立ち位置へ数百 m 走り出していた。`CanPlay` は持ち手の足もとの遊べる地面だけを見て、距離を見ない。そのうえ投げは far で断られるので、「持つと遠くの犬が走ってくるが、投げても来ない」ことになっていた。

- `KH_FrisbeeCatch.readyMaxDistance` (既定 `DefaultReadyMaxDistance` = 40 m、0 = 無制限) を追加した。`CanPlay(at, requester)` は、犬と要求者の水平距離がこれを超えると不可にする (理由 `far`、`_TooFar`)。Ready に入る、Ready を続ける (毎フレームの再判定で Idle へ戻る)、投げの受理 (`req reject reason=far`)、保留した通知の受理はすべてこの門を通る。水の中の犬に「出ておいで」を頼む前に判定するので、遠い犬は水から呼び出さない。
- 投げの `implausible-far` (200 m) はそのまま残す。偽の通知を弾く二重の安全。
- 照準 (`KH_Frisbee.UpdateAim`): その犬が遠いときは線を灰色にして × を出し、一言「犬が遠くにいます」を表示する (`aimFarHint`、既存の「犬はここへ行けません」とは別の文字列)。判定は既存の 10 Hz の判定に含め、遠いときは地面のレイを撃たない。
- 値の置き場所: 本番シーンは Course/92 が 6 匹に 40 を書き、ヒント `FarHint` (既存ヒントの複製) を作って文字を書く。Course/93 が proxy と backing の両方を確かめる。試作とハーネスのシーンは `ConfigureCatcher` が 0 (無制限) を書く。字形は Course/27 (と `HillsideWords`) の対象に含めた。
- **遠い犬は、リードで連れて行くか、既存の呼び寄せの手段で近くへ来させる。** フリスビーから犬を遠くまで呼ぶことはしない。

決定: 2026-09-29 Ready/受理は 40 m 以内 (統括。コミット前)

## 検証

- 静的: 5 つの条件がそれぞれ別の理由で 1 回だけ LogReject を呼ぶ。
- ClientSim: 試作 (平地 26 投げ・terrain) は `readyMaxDistance` = 0 なので変化しない見込み。本番の Course/94・94b は犬を投げ手のそばに置くので 40 m 以内のはず (`reason=far` が出たら置き方を確かめる)。
- 実機: (1) 犬から 40 m より遠くでディスクを持つ → 犬は Ready へ入らず、照準に × と「犬が遠くにいます」が出る。投げると `req reject reason=far`。(2) 40 m 以内へ近づくと、普段どおり Ready・捕球する。(3) 200 m より遠くから投げた場合も、先に `far` で断られる (`implausible-far` は偽の通知の場合だけ出る)。

## 関連

- [BUG-DOG-003](BUG-DOG-003-frisbee-non-master-throw.md): 拒否理由の一覧 (`req reject reason=...`)
- [IMP-DOG-023](IMP-DOG-023-frisbee-anywhere.md): どこでも遊べる化 (犬が遠くにいる状況が増える)
