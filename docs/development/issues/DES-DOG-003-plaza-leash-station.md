---
id: DES-DOG-003
type: DES
system: DOG
status: review
priority: Medium
summary: 体験会 (2026-09-26) で「広場のリード置き場が遠い」。同日夜に決定: 広場の拠点を広場の縁に戻す (3 拠点 × 左右 1 本)。Build が初回だけ縁に置き、位置はユーザーが手で微調整する。実装・Verify 済み、位置はユーザーが手調整済み (2026-09-29)。PCVR 確認待ち
next: PCVR で広場の拠点が遠くないか確認 (位置は 2026-09-29 にユーザーが手調整済み)。OK なら done
updated: 2026-09-29
---

決定: 2026-09-26 夜 (ユーザー) 「広場の真ん中にあったので、広場のちょっと縁かな。場所は手動で調整する」。→ 広場の縁に拠点を戻し、位置の微調整はユーザーが Unity で行う。仕様は [game-design §4](../../game-design.md#集合と拠点のリード) の決定行 (2026-09-26 夜)。

決定: 2026-09-29 (ユーザー) KH_Station_Plaza の位置を手で調整済み

## 実装 (2026-09-26 夜)

- [KH_DogWalkingEditor.cs](../../../Assets/KominkaHillside/Scripts/Editor/KH_DogWalkingEditor.cs): `PlazaStation` を復活。`Walking/3 Build` は `KH_Station_Plaza` に Crossbar が無いときだけ広場の縁 (中心から 10 m、おもちゃの置き場の反対側、中心向き) に置く。以後は人の配置を尊重する。3 拠点 × SlotL / SlotR に戻し、27e703f の 4 枠 (SlotL2 / SlotR2) は削除。メニュー板 (`MenuPlate`) は見晴らしのものを複製
- Verify (`Walking/5 Verify`): 3 拠点の枠、古い 4 枠が無いこと、広場の拠点が中心から 6〜12.5 m にあること
- `KD_TreatSetupEditor.Hillside.cs` の `OpenerStations` と `KD_SleepSetupEditor.cs` の実測対象を 3 拠点に

# DES-DOG-003 広場のリード拠点の位置を決め直す

[体験会の記録](../../plans/2026-09-26-group-playtest.md) (2026-09-26、15 人) でユーザーが報告: **広場のリード置き場が遠い。**

## 経緯

- 2026-09-26 (体験会の前、`27e703f`) にユーザー指示で**広場の中心の拠点を撤去**し、見晴らしの拠点が広場を兼ねることにした ([game-design §4 集合と拠点のリード](../../game-design.md#集合と拠点のリード) の決定行、[DES-UI-004](DES-UI-004-walk-destinations-and-boards.md))。理由は「広場の中心にあると邪魔」
- 現在の拠点: 入口のラック `KH_LeashRack` (門の脇) と見晴らし `KH_Station_Viewpoint` (83.3 / 19.8 / 108.3)。広場中心 `KH_Plaza` (72.0 / 19.0 / 127.0) からは約 22 m
- 体験会では広場で遊んだ後に犬をつなぎ直すのに見晴らしまで戻る必要があり、遠いと感じた

「中心にあると邪魔」と「遠い」の両方を満たすなら、**広場の縁** (通行・おもちゃの置き場・古民家の眺めを妨げない位置) が候補になる。

## 決めること

1. 広場の拠点を戻すか (縁に置く)、見晴らしの拠点を広場寄りに動かすか、両方か
2. 置く場合の位置・向き。ボールとディスクの置き場 ([IMP-DOG-019](IMP-DOG-019-ball-and-disc-placement.md)) と説明ボード ([DES-UI-004](DES-UI-004-walk-destinations-and-boards.md)) との並びも一緒に決める
3. 解除後の道具の戻し先 (解除地点に最も近い利用可能な拠点) の計算に拠点が増えて問題が無いか

## 完了条件

- [ ] 候補の位置を現地の画像付きで出し、ユーザーが選んだ
- [ ] game-design §4 の 2026-09-26 決定を上書きする決定行を書いた
- [ ] Build で再生成しても配置が残り、Verify が通る。PCVR で「広場から拠点まで遠くない」を確認した
