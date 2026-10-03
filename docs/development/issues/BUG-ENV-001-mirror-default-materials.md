---
id: BUG-ENV-001
type: BUG
system: ENV
status: todo
priority: Medium
summary: ビルダーが作った材質 40 個が Standard Lite の既定値 (金属 1・つや 1) のままで、空を鏡のように映す。ディスク・狙いの線・首輪・リード・リード台・洗い場の棚などの色が設計どおりに見えない可能性。iOS の「フリスビーの色が全部同じ」(BUG-DOG-005) の原因候補
next: ユーザーに「つや消しへ直すか」を確認 (今の見た目は PCVR で確認済みのため)。直すなら各ビルダーで金属 0・つや 0.2〜0.35 を書き、前後を撮影して比べる
updated: 2026-10-03
---

# BUG-ENV-001 ビルダー製の材質が既定値のまま鏡面になっている

## 見つけた経緯

2026-10-03、手洗いのボトル ([IMP-DOG-039](IMP-DOG-039-hand-wash-with-shampoo-bottle.md)) が Edit モードの撮影で青く写った。材質の色はピンクで、原因は `VRChat/Mobile/Standard Lite` の既定値 `_Metallic 1`・`_Glossiness 1` (金属マップ無し) で空を映していたこと。同日、看板用の撮影 ([IMP-UI-002](IMP-UI-002-guide-board-capture-instructions.md)) で、フリスビーの「行けない」灰色の線が通常の線と同じ青に写り、同じ原因と分かった。

## 範囲 (2026-10-03、`Assets/KominkaHillside` の .mat 261 個を走査)

金属 ≥ 0.9・つや ≥ 0.9・金属マップ無しは **40 個**、すべて Standard Lite:

- `Data/Frisbee/`: `KH_FrisbeeAim`・`KH_FrisbeeAimOut` (狙いの線の通常 / 行けない)、`KH_FrisbeeDisc` と犬別 6 色、`KH_FrisbeeFace_*` 6、`KH_FrisbeeGround`・`Recall`・`Rock`・`Stand`・`Terrain`・`Variant`・`Wall`
- `Data/Walking/`: `KH_ClipNeutral`・`KH_ClipReady` (リードの先の通常 / つながる時の緑)、`KH_Collar_*` 6、`KH_GatherMarker`、`KH_RackWood` (リード台と散歩の帯の柱)、`KH_Rope`、`KH_SignPlaza`
- `Data/Wash/Tools/`: `KH_WashTool_Shelf` (洗い場の棚・「あらいば」の札・RESET の札)、`Shower`・`ShowerHead`・`Sponge`・`Towel`・`TowelKinari` (取り込んだモデルの下に隠れるものを含む)

作っているビルダー: `KH_FrisbeePrototypeEditor.cs:80, 509`、`KH_DogWalkingEditor.cs:194`、`KH_LeashProductionEditor.cs:77`、`KH_DogWashSetupEditor.Dog.cs:326` / `.Station.cs` (`ToolMaterial`)。`new Material(Shader.Find("VRChat/Mobile/Standard Lite"))` のあと色だけを書いている。ボトルの 2 材質は 10-03 に金属 0・つや 0.35 へ直した (`BottleMaterial`)。

## 影響の見立て (未確認)

- 色で区別させている所が、空の映り込みで近い色に寄る: 狙いの線の通常 (水色) と行けない (灰色)、リードの先の通常と緑、犬別のディスク 6 色・首輪 6 色
- 端末で映り込みの出方が違う (反射プローブ・スペキュラの扱い)。**[BUG-DOG-005](BUG-DOG-005-ios-disc-colors.md) (iOS でフリスビーの色が全部同じ) の原因候補**。iOS 実機での確認はまだ
- 木のはずのリード台・洗い場の棚の札が紺〜黒っぽく見える (Edit モードの撮影)。実機の見え方はユーザーが PCVR で総合 OK としている (10-02) ので、今の見た目が気に入られている可能性もある

## 直し方の案

各ビルダーで材質を作る / 直す時に `_Metallic 0`・`_Glossiness 0.2〜0.35` を毎回書く (既存の .mat も直る。ボトルの `BottleMaterial` と同じ形)。ディスクは少しつやを残す (0.5) など、物ごとに決める。前後を Edit モードで撮影して比べ、ユーザーが実機で確認する。

## 完了条件

- [ ] ユーザーが直すかどうかを決めた (今の見た目を保つ物があれば除外)
- [ ] 直す材質が、ビルダーの再実行でも戻らない
- [ ] 狙いの線の通常 / 行けない、リードの先の通常 / 緑、ディスク 6 色・首輪 6 色が、色で見分けられる (PCVR・Quest・iOS)
- [ ] BUG-DOG-005 の iOS での再確認

## 記録

2026-10-03: 起票。走査のみ。材質・ビルダーは未変更 (ボトルの 2 材質を除く)。
