---
id: BUG-DOG-007
type: BUG
system: DOG
status: todo
priority: Low
summary: 実機 (2026-09-28 夜) で柴のディスクの投げが 22:53〜22:59 に 5 回、`req reject reason=implausible` で断られた。速度・curve は正常で、残る条件は「投げ位置と犬の距離が 200 m を超えた」。柴が遠くにいたか、どこかに引っかかっていた疑い。LogReject は理由を分けていない
next: `implausible` を条件ごとの理由 (nonfinite / time / speed / far / curve) に分け、far のときは犬の位置と距離をログに出す。次の実機で柴が遠くにいた理由を確かめる
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

## 関連

- [BUG-DOG-003](BUG-DOG-003-frisbee-non-master-throw.md): 拒否理由の一覧 (`req reject reason=...`)
- [IMP-DOG-023](IMP-DOG-023-frisbee-anywhere.md): どこでも遊べる化 (犬が遠くにいる状況が増える)
