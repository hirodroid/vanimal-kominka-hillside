---
id: BUG-DOG-006
type: BUG
system: DOG
status: todo
priority: Medium
summary: 実機 (2026-09-28 夜、7 人) で VRChat の「Attempted to steal ownership of a held pickup.」が 15 件 (22:57:07 に 2 件、22:57:21 に 13 件)。他人が持っているピックアップへ、こちらの端末が SetOwner を呼んでいる。呼び出し元は未特定で、候補はドロップの時間切れ時の ReleaseDisc とメニューの呼び寄せ
next: 2 人で「犬が Drop でくわえている間に相手がディスクを掴む」「相手が持っているディスクや同じ犬のもう一方のおもちゃを呼び寄せる」を再現し、エラーが出るかを確かめる。出たら SetOwner の直前に HeldNow の検査を足す
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

## 関連

- [BUG-DOG-003](BUG-DOG-003-frisbee-non-master-throw.md): 他人の投げと Drop・持ち直しの経路
- [IMP-DOG-023](IMP-DOG-023-frisbee-anywhere.md): hold-lost / `_keepDisc` (M3) の設計
- [IMP-DOG-019](IMP-DOG-019-ball-and-disc-placement.md): おもちゃの呼び寄せと片付け
