---
id: VER-WLD-001
type: VER
system: WLD
status: todo
priority: Medium
summary: Quest 単体・iOS の実機で全機能 (散歩・メニュー・空/夜・フリスビー・首輪/リード・負荷・境界) を確認する。各 Issue の Quest/iOS 未確認をここに集約
---

# VER-WLD-001 Quest 単体・iOS の実機で全機能を確認する

項目ごとの確認記録は主に **PC / PCVR** のもの。2026-09-22 に 6 人 PCVR でリード散歩が成立し ([IMP-DOG-002](IMP-DOG-002-leash-walking.md))、手元メニュー・空・ナイトモード・フリスビーも PCVR で評価済み。9/26の体験会にはiOSからの不具合報告もあるが、**Quest単体・iOSの端末別/項目別の検証記録は未完**。各 Issue の未確認条件を、ここへ集約する。

不変条件は「**PC に加え Quest 単体・iOS 対応を目指す**」 ([AI_INDEX](../../AI_INDEX.md) 不変条件 5、[game-design §1](../../game-design.md))。**目指すであって対応済みではない**ので、ここで実機を見るまで「対応済み」と書かない。

各 Issue の完了条件はそのまま残る。ここは実機で見る場所であって、元の Issue の責任を肩代わりしない。

## 確認に使える端末 (2026-09-27)

ユーザー回答：**Quest 3（単体） / iPhone 15 Pro**。DES-ENV-002 の周回コース拡張では、この2台を実機確認の基準にする。16人参加・犬6匹の条件で、入口・上の広場・橋・川岸と区画の切り替わりを確認する。OS・VRChat版・解像度/リフレッシュレート・アバター表示設定・発熱状態は測定時に記録する。Androidスマホの機種は未提示であり、Quest 3 の結果をAndroidスマホ全般の合格にはしない。端末が決まっただけで、実機測定は未実施。

## 確認する内容 (括弧内が出どころの Issue)

| # | 対象 | 見るもの | 出どころ |
|---|------|---------|---------|
| 1 | リード散歩 | 先端の取り出し・首輪への接続・解除、片手 3 匹／合計 6 匹、接続中の追従、複数人での競合・退出・途中参加 | [IMP-DOG-002](IMP-DOG-002-leash-walking.md) の Quest/iOS 条件 |
| 2 | 手元メニューの入口・可読性 | メニューを開く操作が Quest コントローラ／スマホのタッチで届くか、文字が読めるか、3 タブ (さんぽ / あそぶ / おもちゃ) が押せるか | [DES-UI-001](DES-UI-001-menu-and-leash-assist.md) |
| 3 | 空とナイトモード | 乗算オーバーレイが **GrabPass に依存せず** 出るか、暗さが PCVR と同程度か、空のキューブマップ (Android/iOS は 1024 圧縮) の見え方 | [IMP-ENV-002](IMP-ENV-002-skybox-and-night-mode.md) |
| 4 | フリスビーの照準線・半透明材質・強さ入力 | 照準線 (LineRenderer) と着地マーカーが両眼／スマホ画面で見えるか、半透明材質 (`Legacy Shaders/Transparent/Diffuse`) が Android/iOS ビルドで shader 参照を保つか、**強さの右スティック上下 (`InputLookVertical`)** が Quest で効くか、**スマホで右側のバーチャルスティックが `InputLookVertical` に来るか** (来なければ端末別に「仰角だけ・強さ固定」へ) | [IMP-DOG-005](IMP-DOG-005-frisbee-jump-catch.md) の Quest/iOS 項目、[フリスビー記録](../../plans/2026-09-20-frisbee-prototype.md) の「スマホは右側バーチャルスティック」 |
| 5 | 首輪・リードのモデル | 明所／木陰での見え方、動作中のずれ、持ち手の向き、材質が端末で破綻しないか | [IMP-DOG-004](IMP-DOG-004-leash-models-release.md) の Quest/iOS |
| 6 | 負荷 | Quest 単体・iOS の fps と描画コールの目安 (`vrc-performance` の手順)。6 匹散歩・複数人での数字 | [IMP-ENV-001](IMP-ENV-001-hillside-polish.md) の「負荷の初回測定」③ |
| 7 | 境界 | Terrain の縁・遠景の継ぎ目 (南東・南西の隅)、柵・斜面の外への落下が端末でも同じか | [IMP-ENV-001](IMP-ENV-001-hillside-polish.md) |
| 8 | 容量 | Android / iOS のダウンロードサイズがモバイル上限 100 MB を下回っているか。実機に載せる前に `Tools/KominkaHillside/Build Size/8 - Verify Mobile Caps` を 0 件にして `2 - Bundle Size Android` を測る。最新の実測と余力は [ビルドサイズ実測](../../plans/2026-09-24-build-size-audit.md) だけが持つ (ここに数字を書かない)。実機での見た目 (1024 に落とした樹皮・古民家・空) はこの Issue で見る | [ビルドサイズ実測](../../plans/2026-09-24-build-size-audit.md) |
| 9 | 地図・ワープ・合流 | Touch で [地図と移動]・一覧の行・[ここへワープ]・通知板の 4 ボタンを押し分けられるか、通知板 (目の前 1.1 m・15° 下) と Focus View の重なり、暗転の板 (queue 5000) が両眼・スマホ画面で黒くなるか、表示名の欠けた字 (□)、16 人で申し込みが重なったときの通知の間隔 (受け手ごとに 20 秒) | [IMP-WLD-001](IMP-WLD-001-map-warp-and-join.md)、[実機チェックリスト](../../plans/2026-09-27-map-and-travel.md#実機チェックリスト-2026-09-27) |
| 10 | 川の水遊び | 不透明な水面の上で、泳ぐ小型犬 (頭だけが出る) と浅瀬の大型犬が、通常の目線と撫でる低い目線で見分けられるか。しぶき・波紋の粒子が Quest の両眼・iPhone で出るか (ピンク・片眼欠落が無いか)。夜のモードでも見分けられるか | [IMP-DOG-020](IMP-DOG-020-river-wading-and-swim.md)、[設計書 D-2](../../plans/2026-09-27-river-play-and-floating-fetch.md#d-2-quest-単体--ios-ver-wld-001-へ集約) |
| 11 | 水遊びの負荷と途中参加 | 6 匹が同時に水遊びしているときの fps (Editor の目安はフレーム時間 平均 11.1 ms・最大 45.8 ms)。泳いでいる犬がいるときに途中参加した人の画面で、最初から泳ぎの姿勢 (浮いた高さ・Walk) で見えるか。iPhone のタッチでリードを外して水遊びを始められ、案内の一言が読めるか | [IMP-DOG-020](IMP-DOG-020-river-wading-and-swim.md) (設計書 D-1 の S2・D-4) |

## 体験会 (2026-09-26) の所感 — 合格には読み替えない

[体験会の記録](../../plans/2026-09-26-group-playtest.md): グループインスタンスで同時最大 15 人、延べ 20 人。ユーザー報告では**スマホからの参加があり、スマホでも快適だった**。ただし端末の内訳・fps・描画コールは未記録で、参加者の所感なので、項目 6 (負荷) の記録にはしない。**iOS の参加者から「フリスビーの色が全部同じ」の報告**があり、項目 4 の NG として [BUG-DOG-005](BUG-DOG-005-ios-disc-colors.md) に切った。

## 完了条件

- [ ] **Quest 単体**で起動・操作・見え方・fps を記録した (上の 1〜7 について、成立 / 要調整 (何が) / 未 で書く)
- [ ] **iOS** で起動・操作・見え方・fps を記録した (同上)
- [ ] NG だった項目を、元の Issue へ戻すか新しい Issue として切った (ここで直さない)
- [ ] **未実施の端末を対応済みにしていない** — 片方だけ見た場合は、見た方だけを済みとして書く

## 参照

- 端末方針と検証計画: [game-design §1・§6](../../game-design.md)
- 性能の測り方: `vrc-performance` スキル
- 実機チェックリストの書式・分担: `vrc-verification` スキル
