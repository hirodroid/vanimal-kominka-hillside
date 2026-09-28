---
id: IMP-DOG-022
type: IMP
system: DOG
status: review
priority: Medium
summary: ディスクの手元への取り出し (おもちゃタブ) を、投げられる広場の近く (円 + 5 m) に限る。外では「広場の近くで取り出せます」と案内する (DES-DOG-005 段階 1)
next: 統括がコンパイル、翻訳の入れ直し (Course/85 → 27 → 81)、Walking/5 Verify・Frisbee/5 Verify。ユーザーの PCVR で、広場の外 (川辺・道) でディスクの行が暗くなり理由が出ること、広場の縁 (約 17 m) の内外で切り替わることを見る
updated: 2026-09-28
---

# IMP-DOG-022 ディスクの取り出しを広場の近くに限る

## 経緯

[DES-DOG-005](DES-DOG-005-frisbee-anywhere.md) の段階 1。ユーザー決定 (2026-09-28): 段階 1 (取り出しをエリアに限る) を先に入れてから段階 3 (どこでも) へ進む。

今はディスクをどこでも手元へ呼べるが、広場の外では投げても犬が受けず (`KH_FrisbeeCatch.CanPlay` の円判定)、照準に「広場の中で遊ぼう」が出るだけだった。「取り出せるのに投げられない」を無くす。

## 仕様

- 取り出せる範囲: その犬の捕り手 (`KH_FrisbeeCatch`) の遊び場の円 (`playAreaCenter` / `playAreaRadius`) + `KH_ToyRecall.discTakeoutMargin` (既定 5 m)。水平 (XZ) の距離で、ローカルのプレイヤーの位置を見る。本シーンでは広場 (半径 12 m) の中心から 17 m 以内。投げの判定 (円 + `playAreaMargin` 2 m) より広く取り、縁の外から歩いて入る余地を残す。
- 範囲外では `_GateToy(row, KindDisc)` が `OutOfArea` (7) を返す。順は Unwired → Pending → **OutOfArea** → Held → DogBusy → Cooldown。ボールは対象外。
- 円を持たない捕り手 (`playAreaRadius <= 0`: 試作シーン・フリスビーのハーネス) と、ローカルのプレイヤーが無いとき (ClientSim の無いエディタ) は範囲内とみなす。
- 案内文 (おもちゃタブ): 押したときの下端 `course.toy.discArea` 「広場の近くで取り出せます」、行の 2 行目 `course.toy.discAreaState` 「広場の外」(例「ボール: 呼べる / ディスク: 広場の外」)。13 言語を翻訳表に足した。
- 手で持ち運ぶことは止めない (メニューの取り出しだけを限る)。同期変数・ネットワーク越しの呼び出しは増やさない (`KH_ToyRecall` は None のまま、判定はローカルの読み取りだけ)。

## 変更ファイル

- `Assets/KominkaHillside/Scripts/KH_ToyRecall.cs`: `OutOfArea = 7`、`discTakeoutMargin = 5f`、ディスクの門に範囲判定、`LocalNearDiscArea` (private)。
- `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatPouch.Toy.cs`: `HillsideToyGateKey` / `HillsideToyShort` に `OutOfArea` の文言キー。
- `Assets/KominkaHillside/Data/Environment/Course/CourseUITranslations.json`: `course.toy.discArea` / `course.toy.discAreaState` (13 言語)。

## 検証

- [ ] コンパイル (`bash scripts/unity-compile.sh --udon`)、Console エラー 0
- [ ] 翻訳をシーンへ入れ直す: 翻訳表は `KD_Language` の extension に焼く (`KH_CourseTravelEditor.ApplyCatalog`)。入れ直すメニューは Course/76・80・84・85 のどれか。UI だけを作り直す `Course/85 - Build Travel Join` → `Course/27 - Add Course UI Glyphs` (新しい字形) → `Course/81 - Verify Travel Spots and Warp Buttons` (字形の検査を含む) を使う
- [ ] `Walking/5 Verify` と `Frisbee/5 Verify` が通ったまま (新しい公開欄 `discTakeoutMargin` の既定値はコードの初期値 5)
- [ ] フリスビーの ClientSim ハーネス (`Frisbee/7 Harness (ClientSim)`) は試作シーンで捕り手に円が無いので、この判定は通らない (範囲内とみなす)。回帰が無いことだけを見る
- [ ] PCVR (ユーザー): 広場の外 (川辺・道) でディスクの行が暗く「広場の外」、押すと「広場の近くで取り出せます」。広場の縁から約 17 m で切り替わる。広場の中では今までどおり呼べる

## 段階 3 での扱い

段階 3 (どこでも) では、この判定は「その場の地面で遊べるか」の判定に置き換わる予定。`OutOfArea` の文言と `discTakeoutMargin` もそのときに見直す。段階 2 (平らな場所を増やす) では、円の一覧のどれかの近くにいれば取り出せる形に広げる。

## 関連

- [DES-DOG-005](DES-DOG-005-frisbee-anywhere.md): フリスビーをどこでも (段階 1〜3)
- [IMP-DOG-019](IMP-DOG-019-ball-and-disc-placement.md): おもちゃタブと、1 匹 1 つ・置き場 (park)
- [docs/testing/kh-frisbee-harness.md](../../testing/kh-frisbee-harness.md): フリスビーの ClientSim ハーネス
