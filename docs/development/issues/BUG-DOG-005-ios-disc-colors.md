---
id: BUG-DOG-005
type: BUG
system: DOG
status: todo
priority: Medium
summary: 体験会 (2026-09-26) で iOS 参加者から「フリスビーの色が全部同じ」。ディスクは Standard Lite + _Color で犬別 6 色。iOS 実機で再現し、材質・ビルドのどこで色が落ちるかを特定する
next: iOS 実機で 6 枚の色を見る。同じなら iOS ビルドの材質 (Standard Lite の _Color、持ち中の Transparent/Diffuse) と Android ビルドとの差を疑う
updated: 2026-09-26
---

# BUG-DOG-005 iOS でフリスビーの色が全部同じに見える

[体験会の記録](../../plans/2026-09-26-group-playtest.md) (2026-09-26) で、iOS からの参加者が「フリスビーの色が全部同じ」と報告した。ディスクは犬別 6 枚で、識別は色だけ ([DES-DOG-002](DES-DOG-002-toy-behaviour.md))。色が落ちると誰のディスクか分からず、犬も自分のディスクしか追わないので遊びが成り立たない。

## 現在の作り

- ディスクの材質は `Assets/KominkaHillside/Data/Frisbee/KH_FrisbeeDisc_{犬}.mat`。シェーダは `VRChat/Mobile/Standard Lite`、色は `_Color` (Corgi 赤 / Pome 桃 / Lab 黄 / Dachs 青 / Shiba 橙 / Samoyed 白。[KH_DogWalkingEditor.cs](../../../Assets/KominkaHillside/Scripts/Editor/KH_DogWalkingEditor.cs) の `DiscColors`)
- 持っている間だけ、持ち主のローカルで `KH_FrisbeeDisc_{犬}Held.mat` (`Legacy Shaders/Transparent/Diffuse`、同じ色の半透明) に切り替える ([KH_Frisbee.cs](../../../Assets/KominkaHillside/Scripts/KH_Frisbee.cs))
- テクスチャは無く、色だけで見分けている

## 疑う順

1. **iOS 実機で本当に全部同じか**、どの色に見えるか (白なら `_Color` が効いていない、灰やピンクなら材質かシェーダの欠落)
2. iOS ビルドで Standard Lite の `_Color` が落ちる条件 (Android ビルドとの差、[ビルドサイズ実測](../../plans/2026-09-24-build-size-audit.md) の圧縮設定が材質に触れていないか)
3. 持ち中の Transparent/Diffuse の半透明材質が iOS で不透明・無色になっていないか (これは持ち主だけの見え方なので「全部同じ」の説明にはなりにくい)

## 完了条件

- [ ] iOS 実機で 6 枚の色の見え方を記録した (Quest 単体も同じ機会に見る)
- [ ] 原因を特定し、修正後に iOS で 6 色が見分けられる
- [ ] 色以外の識別 ([IMP-DOG-018](IMP-DOG-018-frisbee-dog-face.md) の犬の顔) が入るまでの暫定策が要るかを決めた

端末側の確認は [VER-WLD-001](VER-WLD-001-quest-ios-devices.md) の項目 4 に集約する。
