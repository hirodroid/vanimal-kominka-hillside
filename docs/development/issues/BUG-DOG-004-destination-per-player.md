---
id: BUG-DOG-004
type: BUG
system: DOG
status: todo
priority: High
summary: 体験会 (2026-09-26、15 人) で「行き先が犬ごとに決まっていて、プレイヤーごとのローカル選択になっていない」と報告。コードは自分の選択を自分の接続犬に写す設計なので、何がどう見えたかを 2 人で再現して切り分ける
next: 2 人で別の行き先を選び、自分の犬だけが従うか・メニューの点灯が自分の選択を示すかを確認。NG なら Manager Owner 側の ReqDestination / ReqAttach の経路を疑う
updated: 2026-09-26
---

# BUG-DOG-004 散歩の行き先がプレイヤーごとに効いていない (体験会の報告、要再現)

[体験会の記録](../../plans/2026-09-26-group-playtest.md) (2026-09-26、同時最大 15 人) でユーザーが報告: **行き先が犬ごとで決まっているので、ローカルでプレイヤーごとの選択になっていなかった。** どの場面で何が起きたか (自分の犬が別の人の行き先へ行った / 点灯が自分の選択と違った / 他人の犬まで変わった) は未記録なので、まず再現して症状を確定する。

## 仕様と現在のコード

仕様 ([game-design §4 目的地と指示の対象](../../game-design.md#目的地と指示の対象)): 行き先は**自分の接続犬に対して**指示し、**犬ごとに保持**する。他人の犬・未接続犬は変えない。追加でつないだ犬は接続者が選んだ行き先を引き継ぐ。

コード ([KH_LeashManager.cs](../../../Assets/KominkaHillside/Scripts/KH_LeashManager.cs)):

- `destinations[6]` は `[UdonSynced]` の犬ごとの値 (0 = 入口、1 = 広場、既定 1)
- `_myChoice` は**ローカル**の「この滞在で自分が選んだ行き先」。未選択なら既定 (広場)
- `_ChooseDestination` → Owner へ `ReqDestination` (NetworkCallable、毎秒 6 回まで)。Owner は `walkers[i] == 呼び出し元` の犬だけ `destinations[i]` を書き換える
- `ReqAttach(dog, hand, destination)` は接続時に `_MyChoice()` を渡し、その犬の行き先を接続者の選択で上書きする
- メニュー ([KD_TreatPouch.Walk.cs](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatPouch.Walk.cs) `RedrawWalk`) の [ここへ] の点灯は `_MyChoice()` (ローカル)、下端のまとめは自分の接続犬の `destinations[]` を数える

つまり設計上は「プレイヤーごとの選択 → 自分の犬に反映」になっている。報告どおりなら、次のどれかが起きている:

1. **Owner 側で書き換わらない**: `ReqDestination` が届かない / 落ちる (毎秒 6 回の上限の数え方は要確認)、`NetworkCalling.CallingPlayer` が無効、Manager の Owner が入れ替わった直後
2. **表示だけの問題**: 点灯は自分の選択だが犬は前の保持者の行き先のまま、または再入室後に `_myChoice` が既定へ戻り、犬の行き先と食い違う
3. **仕様の理解違い**: ユーザーの期待が「行き先はプレイヤーの状態で、犬はそれに従う」なら、いまの「犬ごとに保持し、接続時に写す」と結果は同じはずなので、期待と結果のどこがずれたかを聞く

2026-09-26 夜: `ReqDestination` (呼び出し元・行き先・対象の犬の数・変更の有無) と `ReqAttach` (犬・呼び出し元・手・行き先) を Owner 側で `[KH_Leash]` ログに出すようにした (挙動は変えていない)。次の 2 人検証では Manager Owner (通常はマスター) のログを見る。

## 再現手順 (2 人、PCVR または Desktop)

1. A が広場、B が入口を選び、それぞれ 1 匹ずつつなぐ。A の犬が広場へ、B の犬が入口へ向かうか
2. A が途中で入口に変える。A の犬だけ変わり、B の犬は変わらないか
3. B が犬を外し、A が同じ犬をつなぐ。犬の行き先が A の選択になるか
4. A がメニューを閉じて開き直す。[ここへ] の点灯が A の選択のままか
5. Manager の Owner (通常はインスタンスマスター) が退出した後に 1〜2 を繰り返す

## 完了条件

- [ ] 症状を上の 1〜3 のどれかに確定し、この Issue に書いた
- [ ] 修正後、再現手順 1〜5 が 2 人で通る (ClientSim の疑似入力は合格にしない)
- [ ] 仕様の理解違いだった場合は、game-design §4 の文言を体験会の期待に合わせて直すか、直さない理由を書いた

関連: [DES-DOG-001](DES-DOG-001-leash-walking.md) (同期契約)、[DES-UI-001](DES-UI-001-menu-and-leash-assist.md) (目的地選択の UI、完了)。
