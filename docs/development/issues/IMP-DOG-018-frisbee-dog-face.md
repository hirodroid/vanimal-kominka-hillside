---
id: IMP-DOG-018
type: IMP
system: DOG
status: review
priority: High
summary: フリスビーが誰の (どの犬の) ものか分からない (体験会 2026-09-26)。ディスクの上下面に犬の顔 (シーン内モデルのスクリーンショット 256px) を印字。2026-09-26 実装・撮影・Build・Verify PASS、権利と 6 枚の見た目はユーザー確認済み (2026-09-29、微調整は後で)。PCVR / 実機の見え方待ち
next: PCVR で 3 m 離れて誰のディスクか分かるか、持ったときに顔が消えて離すと戻るか。6 枚の見た目の微調整はユーザーが後で (首輪の輪が頭の上に写っている)。iOS / Quest は VER-WLD-001
updated: 2026-09-29
---

# IMP-DOG-018 フリスビーに犬の顔を印字して、誰のディスクか分かるようにする

[体験会の記録](../../plans/2026-09-26-group-playtest.md) (2026-09-26、15 人) でユーザーが「フリスビーが誰のか分からない」「犬の顔をプリントする」「犬の写真・顔をポンと入れて、どれが誰のか分かるように」と要望した。ディスクは犬別 6 枚で、いまは色だけで見分けている ([DES-DOG-002](DES-DOG-002-toy-behaviour.md))。犬は自分のディスクしか追わないので、取り違えると犬が来ない。

## やること

1. **顔画像 6 枚**を用意する。出どころは購入モデル (TORINOSUKE) の顔をレンダリングするか、手描き・AI 生成の似顔絵か。参照元と権利の確認は [共通制作手順](../../../.claude/skills/vrc-asset-production/references/source-rights.md) に従い、記録は本 Issue か制作票に残す
2. ディスクの上面 (と裏面) に貼る。1 枚のアトラス (6 顔) にして材質は犬別 6 個のまま `_MainTex` + UV 切り替え、または顔だけ別メッシュの薄い板。テクスチャは Android / iOS で 512 以下、容量は [ビルドサイズ実測](../../plans/2026-09-24-build-size-audit.md) の余力内
3. 色は残す (遠くからは色、近くでは顔)。色名・犬名の文字は入れない (フォント容量と多言語の理由)
4. ~~持ち中の半透明材質 (`…Held.mat`) にも同じテクスチャを付ける~~ → 実装では持っている本人の画面でだけ顔を非表示にした (半透明の親と不透明の顔の板が混ざると視界を塞ぐため)。他の人には常に見える
5. ボール ([DES-DOG-002](DES-DOG-002-toy-behaviour.md) の柄違い商品) と見分けが付くか、メニューの「おもちゃ」行の名前と一致するかを確認する

## 素材の記録

- 参照元: 購入アセット TORINOSUKE の犬モデル 6 体 (シーン `Assets/Scenes/VRCDefaultWorldScene.unity` の `KD_Dog_{Corgi|Pome|Lab|Dachs|Shiba|Samoyed}`) を Unity Editor でレンダリングしたスクリーンショット (ユーザー指示 2026-09-26「いま使っている犬のモデルのスクリーンショットでよい」)
- 確認日: 2026-09-26
- 生成手順: `Tools/KominkaHillside/Frisbee/6 Capture Dog Faces` (`Assets/KominkaHillside/Scripts/Editor/KH_DogFaceCaptureEditor.cs`)。Edit モードで犬の初期位置のまま、頭の少し前・上から正射影カメラ (犬レイヤーのみ) で 256×256 を撮り、背景をディスクの識別色で塗る。出力 `Assets/KominkaHillside/Textures/Frisbee/KH_DogFace_{犬}.png`、ログ `Temp/KH_Frisbee/dog-faces.txt`
- 貼り付け: `KH_FrisbeePrototypeEditor.BuildDisc` がディスクの上下面に円メッシュ `FaceTop` / `FaceBottom` (直径 0.16 m、材質 `Data/Frisbee/KH_FrisbeeFace_{犬}.mat`) を付ける。持っている本人の画面でだけ非表示 (`KH_Frisbee.faceRenderers`)
- 権利の可否: 問題なし (ユーザー確認 2026-09-29)。見た目は一旦 OK、微調整は後で

## 完了条件

- [x] 6 匹の顔画像の出どころを記録した (上の「素材の記録」。権利はユーザー確認済み 2026-09-29)
- [x] 撮影 6 枚 OK (犬の画素率 33〜74%、向きは鼻方向)、`Walking/3 Build` → `Walking/5 Verify` 490/0、`Frisbee/4 Build Scene` → `Frisbee/5 Verify` 0 件、`Build Size/8 - Verify Mobile Caps` missing 0 (2026-09-26)
- [ ] PCVR で 3 m 離れて誰のディスクか分かる、iOS / Quest 単体で顔が潰れない
- [ ] Android / iOS のビルドサイズがモバイル上限内 (実測を記録へ追記)
- [ ] 犬が自分のディスクを追う動き ([IMP-DOG-012](IMP-DOG-012-frisbee-six-dogs.md)) に影響がない (Verify とハーネスが通る)

関連: [BUG-DOG-005](BUG-DOG-005-ios-disc-colors.md) (iOS で色が落ちる)。
