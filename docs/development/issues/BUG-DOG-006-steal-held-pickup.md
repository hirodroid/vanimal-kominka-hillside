---
id: BUG-DOG-006
type: BUG
system: DOG
status: review
priority: Medium
summary: 実機 (2026-09-28 夜、7 人) で VRChat の「Attempted to steal ownership of a held pickup.」が 15 件 (22:57:07 に 2 件、22:57:21 に 13 件)。ログとコードでは、その時刻にこちらの端末のフリスビー側が SetOwner を呼んだ経路は見つからない (3 匹とも Ready・持ち直し直後で、SetOwner する状態遷移が無い)。同じ文言は他ワールドのログ 60 本にも出る。犬のディスクの SetOwner を AskDisc 1 か所にまとめて持たれている間は呼ばないようにし、全ての SetOwner を送り元の Owner 付きでログに出す
next: コンパイル確認のあと、次の複数人実機でエラーの時刻に `setowner` の行がこちらに出ているかを見る (出ていなければ他の端末の要求)。相手のログも同じ時刻に同じ行があるかを比べる
updated: 2026-09-29
---

# BUG-DOG-006 持たれているピックアップに SetOwner を呼んでいる (steal ownership 15 件)

## 観測 (実機 2026-09-28 夜)

ログはユーザーの PC VR クライアントのもの (`output_log_2026-09-28_22-33-09.txt`)。ワールドは ［TEST］ 古民家の裏山 (`wrld_a12bc96e`)。本人は player 1 で、この間ずっと 6 匹の犬の Owner だった。

- `[NetworkProcessing] Attempted to steal ownership of a held pickup.` が 22:57:07 に 2 件、22:57:21 に 13 件出た。この夜ほかの時間には出ていない。Udon の例外は 0 件。
- 前後の出来事 (`[KH_FrisbeeCatch]` の行):
  - 22:56:47〜22:57:13: 本人がラブのディスクを持っている。
  - 22:56:58: サモエドが `disc ownership lost state=9` から `req cancel reason=hold-lost` (Drop 中にくわえていたディスクを player 3 が取った)。その後 player 3 は 22:57:21 に `req hold ... held=true` を出し、22:57:22 に投げるまで持っていた。
  - 22:57:20: 本人が Drop 中のラブ (state 9) からディスクを掴んだ (`req cancel reason=held-regrab ... state=9 owner=1/1/1`)。0.9 s 後に 13 件がまとめて出た。
- ラブのディスクは本人が Owner で持っていたので、こちらの `IsOwner` 付きの SetOwner は呼ばれない。**奪おうとした相手は他人が持っていたピックアップと考えられる**。このときの有力な候補は、player 3 が持っていたサモエドのディスク。

## 呼び出し元の候補 (コードから)

SetOwner の呼び出し元のうち、「他人が持っている間でも呼びうる」ものを挙げる。

1. **`KH_FrisbeeCatch.ReleaseDisc`** ([KH_FrisbeeCatch.cs](../../../Assets/KominkaHillside/Scripts/KH_FrisbeeCatch.cs) の `ReleaseDisc`)。Drop の時間切れ ([KH_FrisbeeCatch.Motion.cs](../../../Assets/KominkaHillside/Scripts/KH_FrisbeeCatch.Motion.cs) の `ReleaseDisc(dogRoot.forward * 0.3f)`) は `HeldNow()` を見ずに呼ぶ。陸の `HoldWatch` は `_discConfirmed` が立つまで何もしない。そのため、ディスクの所有権が一度も確認されないまま Drop に来て、そこで他人に掴まれると、時間切れのときに SetOwner が出る。`GiveUp` 側は `!held` を見ているので対象外。
2. **`KH_FrisbeeCatch.ClaimDisc`** (`TransitionTo` の JumpCarry / Return)。捕球や回収の瞬間に他人が掴んでいれば、SetOwner は held のピックアップに向かう。ただしこの時刻にサモエド・ラブが JumpCarry / Return へ入った行は無いので、今回の件としては可能性が低い。
3. **`KH_FrisbeeCatch.RequestDisc`** ([KH_FrisbeeCatch.Water.cs](../../../Assets/KominkaHillside/Scripts/KH_FrisbeeCatch.Water.cs))。水中の状態 12 / 13 で毎秒呼ぶ。この時刻に水中のフリスビーは無かったので、今回の件ではない。ただし水中運搬中に相手が岸からディスクを掴むと同じ形になる。
4. **`KH_ToyRecall._RecallToy` / `BeginPark`** ([KH_ToyRecall.cs](../../../Assets/KominkaHillside/Scripts/KH_ToyRecall.cs))。呼び寄せ本体は `_GateToy` の後に SetOwner する。持たれているかは門の判定次第。`BeginPark` は `OtherInUse` を見る。KH_ToyRecall の Update が 13 回続けて再要求する経路は無いが、門が held を断るかどうかはまだ確かめていない。
5. `KH_Frisbee.OnPickup` の SetOwner は本人が掴んだときだけ呼ばれる。22:57:20 の掴みはラブ (自分が Owner) なので対象外。`CheckRiver`・浮きの処理は Owner だけが書くので SetOwner を呼ばない。
6. VRChat 側: 本人の手が他人の持っているディスクを掴もうとした場合も同じ文言になるかは未確認。13 件が 1 秒に集中しているのはフレームごとの試行に見える。本人は片手にラブのディスクを持っていたので、もう一方の手で player 3 のサモエドのディスクに触れた可能性もある。

## 次の手順

- 2 人で候補 1 (Drop 中に相手が掴む、所有権がまだ確認されていない Drop) と候補 4 (相手が持っているおもちゃの呼び寄せ・もう一方の片付け) を再現し、エラーが出るかを見る。
- 候補 6 を切り分ける: 片手で他人が持っているディスクに触る。
- 当たった経路で、SetOwner の直前に `HeldNow()` (ディスク) か `IsHeld` (ボール) の検査を足す。挙動は変えない (所有権を取らずにあきらめる)。

## 期待する挙動 (2026-09-29)

- ディスクの SetOwner を呼んでよいのは 4 か所だけ: (a) 自分が掴んだとき (`KH_Frisbee.OnPickup`、その時点で持っているのは自分)、(b) 犬の Owner が捕球・回収で口に入れるとき (`ClaimDisc`)、(c) 犬の Owner が口から離すとき (`ReleaseDisc`)、(d) 水中の回収待ち (`RequestDisc`、1 秒に 1 回、最大 2 秒)。呼び寄せと片付け (`KH_ToyRecall`) はメニュー 1 回につき 1 回。
- (b)〜(d) は **他人が持っている間 (`HeldNow()`) は呼ばない**。犬はディスクを取り返さずにあきらめる (Drop なら Idle、水中なら claim-failed / hold-lost)。1 フレームごとに呼ぶ経路を作らない。
- 呼び出しはすべて 1 行ログに残す (送り元の Owner 付き)。

## 調査 (2026-09-29、ログとコード)

- 22:57:07: ログは 2 行だけで、前後 6 秒に `[KH_*]` の行が無い。3 匹の状態: ラブは本人がディスクを持って Ready (22:56:47 に持ち直し)、サモエドは player 3 が 22:56:58 に Drop から取って Ready、ポメは player 2 が 22:56:48 に取って Ready。Ready は SetOwner を呼ばない。
- 22:57:21: 本人が 22:57:20 にラブの Drop から持ち直した (`held-regrab`、`GiveUp` は held を見て `ReleaseDisc` を呼ばない)。13 行のすぐ後に player 3 のサモエドへの投げ (`req hold`) が届いている。この間に Drop の時間切れ・捕球・回収・水中の状態は無い。
- こちらの 4 か所 (b)〜(d) は、同じフレームで先に `_seenReleased && HeldNow()` → `held-regrab` の取消が走るので、所有権が先に移って `IsHeld` の同期が遅れる数フレームの隙間でしか held のディスクに届かない。13 回を 1 秒で出すループ (0.25 s の検査を含む) はフリスビー側に無い: `RequestDisc` は 1 秒 1 回、`KH_ToyRecall` の 0.25 s ループ (排他・川) は SetOwner を呼ばない。
- 同じ文言は、このワールドと関係の無いワールドのログにも出ている (2024-06 以降のログ 60 本、焚き火・剣などのワールドで 1 秒に 3〜22 行のまとまり)。`[NetworkProcessing]` は受信処理の分類で、**こちらが送り元とは限らない** (他の端末が、誰かの持っているピックアップへ要求したときにも出る可能性がある)。このログだけでは送り元を決められない。

## 修正 (2026-09-29)

- `KH_FrisbeeCatch.AskDisc` ([KH_FrisbeeCatch.cs](../../../Assets/KominkaHillside/Scripts/KH_FrisbeeCatch.cs)): 犬のディスクの SetOwner を 1 か所にまとめた。自分が Owner なら何もしない、`HeldNow()` なら呼ばずに `setowner skipped: held` を 1 行、呼ぶときは `setowner disc why=claim|release|water from=<前の Owner>` を 1 行。`ClaimDisc` / `ReleaseDisc` / `RequestDisc` (Water.cs) はここを通る。
- `ReleaseDisc`: 持たれていれば何もしない (`release skipped: held`)。Drop の時間切れはその後 Idle へ移るだけ。
- `KH_Frisbee.OnPickup` と `KH_ToyRecall` (呼び寄せ / 片付け) の SetOwner にも送り元付きのログ (`[KH_Frisbee] setowner grab from=` / `[KH_ToyRecall] setowner recall|park`) を足した。門 (`_GateToy` の Held、`OtherInUse`) は元から持たれたおもちゃを断っている。
- 再試行の間隔は変えていない (水中 1 秒、他は 1 回きり)。スキップ行も同じ間隔でしか出ない。

## 検証

- 静的: 呼び出し元は上の 4 か所 + `KH_FrisbeeLauncher` (元から `IsHeldNow` で拒否) だけ。
- ClientSim では検査を足さない。held のガードが効くのは「ディスクの Owner が他人で、その人が持っている」ときだけで、1 人の ClientSim では再現できない (`debugHeldOverride` で持たせても、その前に `held-regrab` の取消が走る)。
- 実機 (2 人以上、PCVR): (1) 犬が Drop でくわえている間に相手が掴み、そのまま Drop の時間切れを待つ → こちらのログに `setowner` 行が無く、エラーも出ない。(2) エラーが出たら、同じ時刻のこちらの `setowner` 行の有無と、相手のログの同じ時刻を比べて送り元を決める。

## 関連

- [BUG-DOG-003](BUG-DOG-003-frisbee-non-master-throw.md): 他人の投げと Drop・持ち直しの経路
- [IMP-DOG-023](IMP-DOG-023-frisbee-anywhere.md): hold-lost / `_keepDisc` (M3) の設計
- [IMP-DOG-019](IMP-DOG-019-ball-and-disc-placement.md): おもちゃの呼び寄せと片付け
