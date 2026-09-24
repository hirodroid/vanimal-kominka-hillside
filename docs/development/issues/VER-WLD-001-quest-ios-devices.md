---
id: VER-WLD-001
type: VER
system: WLD
status: todo
priority: Medium
---

# VER-WLD-001 Quest 単体・iOS の実機で全機能を確認する

ここまでの確認は **PC / PCVR だけ**で行っている。2026-09-22 に 6 人 PCVR でリード散歩が成立し ([IMP-DOG-002](archive/IMP-DOG-002-leash-walking.md))、手元メニュー・空・ナイトモード・フリスビーも PCVR で評価済みだが、**Quest 単体と iOS は一度も実機に載せていない**。各 Issue が「Quest/iOS 未確認」を個別に抱えたままだと、どれが残っているか分からなくなる。この Issue が**端末側の確認を 1 か所に集める**。

不変条件は「**PC に加え Quest 単体・iOS 対応を目指す**」 ([AI_INDEX](../../AI_INDEX.md) 不変条件 5、[game-design §1](../../game-design.md))。**目指すであって対応済みではない**ので、ここで実機を見るまで「対応済み」と書かない。

各 Issue の完了条件はそのまま残る。ここは実機で見る場所であって、元の Issue の責任を肩代わりしない。

## 確認する内容 (括弧内が出どころの Issue)

| # | 対象 | 見るもの | 出どころ |
|---|------|---------|---------|
| 1 | リード散歩 | 先端の取り出し・首輪への接続・解除、片手 3 匹／合計 6 匹、接続中の追従、複数人での競合・退出・途中参加 | [IMP-DOG-002](archive/IMP-DOG-002-leash-walking.md) の Quest/iOS 条件 |
| 2 | 手元メニューの入口・可読性 | メニューを開く操作が Quest コントローラ／スマホのタッチで届くか、文字が読めるか、3 タブ (さんぽ / あそぶ / おもちゃ) が押せるか | [DES-UI-001](archive/DES-UI-001-menu-and-leash-assist.md) |
| 3 | 空とナイトモード | 乗算オーバーレイが **GrabPass に依存せず** 出るか、暗さが PCVR と同程度か、空のキューブマップ (Android/iOS は 1024 圧縮) の見え方 | [IMP-ENV-002](archive/IMP-ENV-002-skybox-and-night-mode.md) |
| 4 | フリスビーの照準線・半透明材質・強さ入力 | 照準線 (LineRenderer) と着地マーカーが両眼／スマホ画面で見えるか、半透明材質 (`Legacy Shaders/Transparent/Diffuse`) が Android/iOS ビルドで shader 参照を保つか、**強さの右スティック上下 (`InputLookVertical`)** が Quest で効くか、**スマホで右側のバーチャルスティックが `InputLookVertical` に来るか** (来なければ端末別に「仰角だけ・強さ固定」へ) | [IMP-DOG-005](IMP-DOG-005-frisbee-jump-catch.md) の Quest/iOS 項目、[フリスビー記録](../../plans/2026-09-20-frisbee-prototype.md) の「スマホは右側バーチャルスティック」 |
| 5 | 首輪・リードのモデル | 明所／木陰での見え方、動作中のずれ、持ち手の向き、材質が端末で破綻しないか | [IMP-DOG-004](review/IMP-DOG-004-leash-models-release.md) の Quest/iOS |
| 6 | 負荷 | Quest 単体・iOS の fps と描画コールの目安 (`vrc-performance` の手順)。6 匹散歩・複数人での数字 | [IMP-ENV-001](IMP-ENV-001-hillside-polish.md) の「負荷の初回測定」③ |
| 7 | 境界 | Terrain の縁・遠景の継ぎ目 (南東・南西の隅)、柵・斜面の外への落下が端末でも同じか | [IMP-ENV-001](IMP-ENV-001-hillside-polish.md) |
| 8 | 容量 | Android / iOS のダウンロードサイズがモバイル上限 100 MB を下回っているか。実機に載せる前に `Tools/KominkaHillside/Build Size/8 - Verify Mobile Caps` を 0 件にして `2 - Bundle Size Android` を測る。最新の実測と余力は [ビルドサイズ実測](../../plans/2026-09-24-build-size-audit.md) だけが持つ (ここに数字を書かない)。実機での見た目 (1024 に落とした樹皮・古民家・空) はこの Issue で見る | [ビルドサイズ実測](../../plans/2026-09-24-build-size-audit.md) |

## 完了条件

- [ ] **Quest 単体**で起動・操作・見え方・fps を記録した (上の 1〜7 について、成立 / 要調整 (何が) / 未 で書く)
- [ ] **iOS** で起動・操作・見え方・fps を記録した (同上)
- [ ] NG だった項目を、元の Issue へ戻すか新しい Issue として切った (ここで直さない)
- [ ] **未実施の端末を対応済みにしていない** — 片方だけ見た場合は、見た方だけを済みとして書く

## 参照

- 端末方針と検証計画: [game-design §1・§6](../../game-design.md)
- 性能の測り方: `vrc-performance` スキル
- 実機チェックリストの書式・分担: `vrc-verification` スキル
