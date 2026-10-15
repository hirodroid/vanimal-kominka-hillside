---
id: IMP-WLD-017
type: IMP
system: WLD
status: wip
priority: Medium
summary: 筆ペン (モサンゴ屋「筆ペン差し替えショートwith筆掛け」、QvPen 3.3.15、6 色) を手元メニューから色を選んで自分の前へ呼ぶ。後がち (持っている人の手から離して呼ぶ) で取り合い可。入口の場所を検討中 (推奨 = あそぶ、代案 = 設定)
next: "ユーザーが §3 の入口 (あそぶ / 設定) と後がちの実装 (2 段階) を決める → Codex 相談 → 実装 (Opus: KH_PenCall + PenPage ビルダー) → Edit 検査 → 独立レビュー → コミット → review"
updated: 2026-10-16
---

# IMP-WLD-017 筆ペンをメニューから色を選んで呼ぶ

## 1. 依頼 (2026-10-16)

ユーザー: 「筆ペン差し替えショートwith筆掛け」を追加したのだけど、メニューから色を選択して呼び出せるようにしたい。後がちで取り合いで OK。まずはメニューのどこで呼び出せるようにするのがいいか検討してください。設定かなぁ。

統括 Fable が司令塔。起票 → 入口の検討 (本票 §3) → ユーザー決定 → Codex 相談 → 実装 (Opus) → Edit 検査 → Codex 独立レビュー → コミット。

## 2. 起票時の現状 (読み取りだけ。Unity は未操作)

- **置いてある物**: プレハブ `Assets/モサンゴ屋/筆ぺん/筆ペン差し替えショートwith筆掛け.prefab` のインスタンス 1 つ。シーン直下、位置 (52.7, 2.3, 44.8) = 門外の広場、写真ボード `KH_PhotoBoard` の 3.6 m 北、洗い場の 14 m 東。シーンは別セッションの未コミット分に含まれる。`Assets/モサンゴ屋/` は購入アセットで `.gitignore` 済み (不変条件 8: 編集しない。派生物は `Assets/KominkaHillside/` へ)
- **中身**: QvPen 3.3.15 (`net.ureishi.qvpen`、`vpm-manifest.json` には IMP-UI-011 のコミットで記録済み)。`QvPen_Settings` 1、`PenManager (0)〜(5)` + `Pen` 6 本、消しゴム 3 本、筆掛け (ロング / ショートの FBX)、色材質 16 種 (`color (0)〜(15).mat`。「差し替え」は Editor で筆の色材質を差し替えるためのもので、実行時の切替ではない)。シーン側のオーバーライドは名前と位置だけ
- **6 本の色** (`colorGradient` の先頭キー): (0) 黒 / (1) 赤 / (2) 橙 / (3) 黄 / (4) 黄緑 / (5) 緑。インク色は Editor で決める `Gradient` で、**同期もせず実行時の変更口も無い** (線のデータに色は乗らず、各端末が自分のペンの gradient で描く)。したがって「色を選ぶ」= **6 本の中から 1 本を選ぶ** ことになる
- **QvPen の呼ぶ仕組み** (`QvPen_Manager.CallPen`): Desktop は **Tab+Q** で、両手が空いていれば最後に使ったペン (無ければ空いているペンを無作為) を `_TakeOwnership` して頭の 0.5 m 前へ置く。**持たれているペン (`isHeld`) は呼ばない**。`isHeld` は `StartUsing` / `EndUsing` のネットワークイベント (All) で全端末に伝わる。VR 向けの呼び口は無い。各 `PenManager.allowCallPen` は true
- **同期**: `QvPen_Pen` は NoVariableSync + `VRCObjectSync` (位置)、`PenManager` は Manual (線のデータ)。`PenManager.Respawn()` が `pen._Respawn()` (`pickup.Drop()` → Owner なら `objectSync.Respawn()` で筆掛けへ)。`PenManager.pen` は private `[SerializeField]` なので、配線は Editor から (SerializedObject か子 `Pen` の探索)
- **持たれているピックアップの所有権は奪えない**: VRChat は「Attempted to steal ownership of a held pickup.」を出して拒む ([BUG-DOG-006](BUG-DOG-006-steal-held-pickup.md) が追跡中)。`VRC_Pickup.Drop()` は自分が持っているときだけ効く (他人の手から直接は落とせない)
- **既存の「呼ぶ」の型** ([KH_ToyRecall.cs](../../../Assets/KominkaHillside/Scripts/KH_ToyRecall.cs)、おもちゃタブ): 門 (`_GateToy`: 持たれている / 犬が使用中 / 同じ犬のもう片方が出ている / クールダウンなら断る) → `SetOwner` → Update で所有権を待つ (4 s で時間切れ) → `Place` (胸の前の `HoverSpot` へ置き `FlagDiscontinuity`、速度 0) → 20 s 触られなければ床へ落として眠らせる。結果 (置いた / 断った / 時間切れ) は 4 s の状態行。sync None。**持たれているおもちゃは呼ばない** (後がちではない)
- **手元メニューの空き**: タブ列 (7 タブ、幅 1100 を使い切り) とヘッダ ([写真] [動画] で 4 通りの `HeaderLayout`) は満席。副画面は `Page_Settings/*Page` を `extraSettingsPages` に登録して `_OpenExtraSettings` で開く型 (写真 / 動画と同じ、ビルダーは `KH_VideoPlayerEditor` が最新の雛形)。**あそぶ**の一覧 (`KD_ActivitiesMenu` 面 0) は見出し 160 / 群「ばにまるトレーナー」120 / 行 60 (集合・おわる・フリスビーの取り方。幅 330×88、3 列) / 案内 -60 (高さ 84 = -18〜-102) / 状態 -225 (-194〜-256) / [戻る] [購入画面をひらく] -285。**-104〜-192 の帯 (88 px) が空いている** (行 3〜5 は隊形の面でだけ出る)。**設定**は自分 (音量・距離フェード・写真・ナイト・言語、Y 180) / 部屋 (呼ぶ・ライブの許可、Y -74) / 読む (クレジット・インフォ、Y -168) の 3 群で、新しい群を入れる余地は無い (既存の群へ 1 ボタン足すなら可)。**おもちゃ**は 6 匹の行 + 上段 (投げ方・柄) で満席
- **仕様の前提**: [game-design](../../game-design.md) §4・§7 に筆ペン・お絵かきの行は無い (新規の範囲)。線はワールド空間に残り、コース全域・Quest にも及ぶ。全消しは筆掛けの [Clear All] ボタン (QvPen 標準)。手元メニューの範囲の規則 ([DES-UI-003](DES-UI-003-menu-scope-and-polish.md) / world-ui-spec §4): 「持っている物を使う・自分の操作で閉じる」。共有物を自分の前へ呼ぶ先例はおもちゃタブ (おもちゃの呼び寄せ)

## 3. 設計の推奨 (統括 Fable。ユーザー確認待ち。推奨 = 「はい」で進める既定)

| # | 決めること | 推奨 | 代案 |
|---|---|---|---|
| 1 | **入口の場所** | **あそぶ** の一覧の空き帯 (Y -148、左の列、330×88、誰でも押せる。トレーナー群の行とは分ける) に **[筆ペン]**。押すと副画面 `Page_Settings/PenPage` が開く。理由: 筆ペンは散歩中に何度も使う「遊びの動作」で、設定 (自分・部屋の好みを 1 度決める物) ではない。共有物を呼ぶ先例 (おもちゃの呼び寄せ) もタブ側にある。あそぶは誰でも使う集合の入口でもあり、1 タップで届く。代償: 移植元の `KD_ActivitiesMenu` と `KD_ActivitiesEditor` に行を 1 つ足し、Menu/23 の Verify を追従させる | **設定** (ユーザー案)。自分の群の末尾 (写真 ON/OFF の隣) に [筆ペン] を置き、同じ副画面を開く。利点: 移植元の KD_ActivitiesMenu を触らない。欠点: 設定は好みの置き場で、動作のボタンが混ざる。[設定] → [筆ペン] → 色の 2 タップになり、散歩中に繰り返し呼ぶには遠い。**おもちゃ**が意味としては最も近いが満席、ヘッダ・新タブは不可 |
| 2 | **副画面の中身** | 本文の帯 (Y 148〜-232) に 6 色のボタン (3 列 × 2 段、330×88)。**ボタンをインク色で塗り**、文字は 1 語「筆ペン」(翻訳キー 1 つ) か無し。上段に状態の 1 行 (呼びました / あと n 秒 / 届きませんでした)、[もどる] (450, 170) は写真・動画の面と同じ。描くのは新しい U# `KH_PenCall` (ローカル専用、sync None、`KH_ToyRecall` の写し) | 6 色の名前を文字で出す (黒・赤・橙・黄・黄緑・緑 = 6 キー × 13 言語 + 字形)。色弱の人には名前が要るが、ペン本体が同じ色なので色のボタンだけで足りると見る |
| 3 | **後がち (持たれているペンも呼ぶ)** | **2 段階**: 押した人が All へイベント `_ReleasePen(番号)` → 持っている人の端末だけが `pickup.Drop()` → `OnDrop` → `EndUsing` (All) で `isHeld` が下りる → 押した人が `SetOwner` + 所有権待ち (4 s で時間切れ) → `Place`。**一瞬では来ない** (相手の端末を往復する 0.2〜1 s)。持たれていないときは 1 段目を飛ばす。`isHeld` が下りないまま 4 s 経てば「届きませんでした」。`KH_ToyRecall` と同じく 1 人 1 本の進行中 (連打は捨てる) | QvPen 標準どおり **持たれているペンは呼ばない** (「だれかが使っています」)。実装は門 1 つ分小さく、描いている人の手から消えないが、ユーザー指定の「後がち」ではない |
| 4 | **置き方と戻し方** | 置き方は `KH_ToyRecall.Place` と同じ (胸の前の HoverSpot、`FlagDiscontinuity`、速度 0。QvPen の「頭の 0.5 m 前」は VR では近すぎる)。**20 s 触られなければ `PenManager.Respawn()` で筆掛けへ戻す** (おもちゃは床へ落とすが、コースに落ちたペンは失われる。筆掛けがあるので戻す) | 床へ落とす (おもちゃと同じ) |
| 5 | 呼べる場所 | 制限しない (どこでも。川の中・坂でも呼べる。線はどこにでも描ける物なので取り出しの門は作らない) | フリスビーと同じ `_TakeoutOkAt` (川・水遊び場・25° 超の坂は断る) |
| 6 | 範囲の扱い | game-design §7 に「筆ペン (QvPen) を初期公開に入れる。線は同期・全域・全消しは筆掛け」の 1 行をユーザー決定として足す。Desktop の Tab+Q はそのまま (メニューは主に VR の呼び口) | — |

決定: (未。ユーザーの回答待ち)

## 4. 作業範囲 (推奨で進める場合)

- **U#** `Assets/KominkaHillside/Scripts/KH_PenCall.cs` (ローカル専用、sync None。`penManagers[6]` / `pens[6]` の参照、`_Call(i)`、`_ReleasePen` (All、`[NetworkCallable]`)、所有権待ち・置く・20 s の戻し、状態の 1 行、面が開いている間だけ Update) と `KD_TreatPouch.Pen.cs` (partial: `penPage`、`_PenOpen` / `_PenBack`。写真・動画と同じ戻り方)。あそぶの行は `KD_ActivitiesMenu` (裏山改変: 行 6 = [筆ペン] → `pouch._PenOpen`)
- **Editor** `Assets/KominkaHillside/Scripts/Editor/KH_PenCallEditor*.cs`: `Tools/KominkaHillside/Pen/0 Create Program Asset`、`1 Build` (シーンの「筆ペン差し替えショートwith筆掛け」を名前で探し、`QvPen_Settings.penManagers` → 各 `pen` を SerializedObject で読んで `KH_PenCall` に配線、`Page_Settings/PenPage` と 6 色のボタン、あそぶの [筆ペン] 行、翻訳・字形)、`2 Verify` (読むだけ: 6 本の配線、ボタンの色 = `colorGradient`、帯の収まり、あそぶの行の重なり無し、NetworkIDs)、`9 Remove`。`KD_ActivitiesEditor` の Verify (Menu/23) に行 6 を足す
- **文言** `UITranslations.json` に `pen.call.*` (入口・面の題・状態 3 種) × 13 言語。色の名前は出さない (推奨 2)
- **文書** game-design §7 に 1 行、§8 変更記録、issue_summary 再生成
- **コミット**: 本票・コード・翻訳・Program Asset。シーンは別セッションの未コミット分を含むので stage しない (Pen/1 で再生成できる)。プレハブは `.gitignore` 済み (コミットしない)

## 5. 完了条件

Edit で確定できるもの:
- [ ] あそぶの [筆ペン] (または決めた入口) から副画面が開き、[もどる] で元のタブへ戻る。6 色のボタンの色が各 PenManager の `colorGradient` と一致 (Pen/2)
- [ ] 6 本の配線・NetworkIDs・あそぶの行の重なり無し・13 言語の収まりが Pen/2 で PASS。C# / U# 0。Codex 独立レビュー反映

実機でしか決まらないもの (NOT_TESTED のまま review へ):
- [ ] VR で色を押すとそのペンが胸の前に浮き、掴んで描ける。20 s 放置で筆掛けへ戻る (A)
- [ ] 2 人: 相手が持っているペンを呼ぶと相手の手から離れて自分の前に来る (後がち)。相手の端末にエラー (steal ownership) が出ない (B)
- [ ] Quest で面が開き、呼べる。線が見える (D)

## 6. 記録

2026-10-16: 起票 (統括 Fable)。プレハブ・QvPen のコード・シーン (ファイル読み取り)・手元メニューの構成 (調査サブエージェント 1 本) の読み取りだけ。Unity 未操作、実装なし。入口の推奨 = あそぶ (§3-1)、代案 = 設定 (ユーザー案)。advisor 相談で、後がちは 2 段階 (相手の端末で Drop → 所有権) にしないと BUG-DOG-006 と同じエラーになる点、放置の戻し先を筆掛けにする点、色 = 6 本から選ぶ点を §3 へ明記。
