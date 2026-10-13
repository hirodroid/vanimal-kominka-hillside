---
id: BUG-DOG-013
type: BUG
system: DOG
status: todo
priority: Medium
summary: 洗浄の有料道具 (水鉄砲か泡もこもこ銃) のモデルが Z ファイトしている。どの品か特定し、Blender の元モデルで面の重なりを直して取り込み直す
next: "どの品か特定する (候補: WaterPistol / DuckWaterGun / FishWaterGun / BambooWaterGun / FoamCannon。ユーザーに画像か品名を聞くか、Editor で 5 品を近接で確認)。直す先は vrc-asset-lab/assets/wash-premium の元モデル。0.3.0 の品なら取り込み直しの手順 (WashPremium/10 は再実行しない決まり) を先に決める"
updated: 2026-10-13
---

# BUG-DOG-013 洗浄の有料道具 (水鉄砲 / 泡銃) のモデルの Z ファイト

## ユーザー報告 (2026-10-13、原文)

「あとは洗浄用の水鉄砲だったか、もこもこ泡だったか、もうなんかモデルが Z ファイトしているので、これもちょっと直したいです。」

ユーザーもどの品か確かではない。**特定が最初の作業**。

## 候補と出どころ

| 候補 (Prefab `Assets/KominkaHillside/Prefabs/WashPremium/`) | 版 | 取り込みメニュー |
|---|---|---|
| `FoamCannon` (泡もこもこ銃) | 0.3.0 (18 品) | `WashPremium/10` (**再実行しない**決まり、[IMP-DOG-049](IMP-DOG-049-wash-water-tools-and-reactions.md)) |
| `DuckWaterGun` / `FishWaterGun` / `BambooWaterGun` (形違いの水鉄砲) | 0.3.0 | 同上 |
| `WaterPistol` (水鉄砲) | 0.4.0 (3 FBX) | `WashPremium/12 - Import 0.4.0 Visuals` |

元モデルはラボ `C:/work/unity/vrc-asset-lab/assets/wash-premium/` (source / exports / handoff.md)。材質は子の名前で割り当て (`*_Mesh` → `PremiumWash_Tools`、0.4.0 の透明は `PremiumWash_Addons` の Fade、renderQueue 2990)。

見立て (測る前の仮説): 同じ面に 2 枚のポリゴンが重なっている (胴と飾りの継ぎ目、タンクの内外、ノズルの差し込み)。透明材質の品 (水鉄砲の水のタンク) なら、不透明の胴と透明の面が同じ位置にある可能性もある。

## 範囲

1. 特定: ユーザーに品名か画像を聞く。聞けなければ Editor で 5 品を近接のカメラで回して見て、ちらつく面を記録する (画像を `docs/plans/evidence/` に残す)
2. 直し: ラボの元モデル (Blender) で重なる面をずらす・消す・結合する。ラボの検収 (`validation/`、三角形数・bounds) を通す
3. 取り込み直し: 0.4.0 なら `WashPremium/12` → `21` (三角形数 8,414 の検査値は変われば更新)。**0.3.0 なら** `10` を再実行しない決まりなので、その品だけ FBX を差し替える手順 (メッシュの差し替えで Prefab・材質・アンカー・`gunHolds` を保つ) を決めてから行う
4. `WashPremium/44` と `Wash/12` が PASS のまま

やらないこと: 道具の握り ([BUG-DOG-010](BUG-DOG-010-wash-tool-grip.md))、見た目・音の全体の見直し ([IMP-DOG-055](IMP-DOG-055-wash-premium-model-and-sound-quality.md))。

## 完了条件

AI 側で確かめる。

- [ ] 品名と重なっていた面 (部位・材質) を記録し、直し方 (ずらした距離 / 消した面) と三角形数の前後を記録した
- [ ] 取り込み直しの後、`WashPremium/21`・`44`、`Wash/12` PASS。受領書 (`Temp/WashPremium/import-*.txt`) のアンカーの角度が 1° 以内のまま
- [ ] Editor の近接カメラの画像でちらつきが消えている (変更前後)

ユーザーが確かめる (Play / PCVR)。

- [ ] その品を持って回しても、置いても、ちらつきが出ない。Quest でも出ない
- [ ] 握り・噴出口の位置が変わっていない

## 記録

2026-10-13: 起票 (ユーザー依頼「Issue に登録しておいてほしい」)。優先度 Medium は AI の仮置き。実装は未着手。
