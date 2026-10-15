---
id: IMP-WLD-017
type: IMP
system: WLD
status: review
priority: Medium
summary: 筆ペン (モサンゴ屋「筆ペン差し替えショートwith筆掛け」、QvPen 3.3.15、6 色) を手元メニューの設定 (群「読む・書く」の [筆ペン]) から色を選んで自分の胸の前へ呼ぶ (2026-10-16 実装、Edit 検査 PASS)。持たれているペンは「使用中」で押せない。Play・実機は未確認
next: "ユーザーの Play / 実機で §5 の A (VR で呼ぶ・掴む・20 s で筆掛けへ)、B (2 人: 使用中で押せない・遅参)、D (Quest)、E (11 言語の収まり) を確かめる。OK なら done"
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
| 3 | **持たれているペン** (2026-10-16 ユーザー決定: 代案を採用 → 「使用中」で押せなくする) | ~~**2 段階**~~: 押した人が All へイベント `_ReleasePen(番号)` → 持っている人の端末だけが `pickup.Drop()` → `OnDrop` → `EndUsing` (All) で `isHeld` が下りる → 押した人が `SetOwner` + 所有権待ち (4 s で時間切れ) → `Place`。**一瞬では来ない** (相手の端末を往復する 0.2〜1 s)。持たれていないときは 1 段目を飛ばす。`isHeld` が下りないまま 4 s 経てば「届きませんでした」。`KH_ToyRecall` と同じく 1 人 1 本の進行中 (連打は捨てる) | QvPen 標準どおり **持たれているペンは呼ばない** (「だれかが使っています」)。実装は門 1 つ分小さく、描いている人の手から消えないが、ユーザー指定の「後がち」ではない |
| 4 | **置き方と戻し方** | 置き方は `KH_ToyRecall.Place` と同じ (胸の前の HoverSpot、`FlagDiscontinuity`、速度 0。QvPen の「頭の 0.5 m 前」は VR では近すぎる)。**20 s 触られなければ `PenManager.Respawn()` で筆掛けへ戻す** (おもちゃは床へ落とすが、コースに落ちたペンは失われる。筆掛けがあるので戻す) | 床へ落とす (おもちゃと同じ) |
| 5 | 呼べる場所 | 制限しない (どこでも。川の中・坂でも呼べる。線はどこにでも描ける物なので取り出しの門は作らない) | フリスビーと同じ `_TakeoutOkAt` (川・水遊び場・25° 超の坂は断る) |
| 6 | 範囲の扱い | game-design §7 に「筆ペン (QvPen) を初期公開に入れる。線は同期・全域・全消しは筆掛け」の 1 行をユーザー決定として足す。Desktop の Tab+Q はそのまま (メニューは主に VR の呼び口) | — |

決定: 2026-10-16 (ユーザー)。**入口は設定** (「遊ぶに入れると意味が分からない。空きではなく文脈で判断したい」→「設定の中に置いて」)。**後がちはやめ、持たれているペンは「使用中」と書いて押せなくする** (§3-3 の代案 = QvPen 標準)。残り (§3-2・4・5・6) は推奨で進める。**§3-a の見出しの改名「読む・書く」と読む行の 4 等分 (既存 3 ボタンの幅 340→260) はユーザーの最後の指示より後の統括の判断で、ユーザー未確認** (戻す代償: 見出しは 1 キーの文言、行の幅は定数 1 か所)。

### 3-a 設定の中の置き場 (統括の案。設定の面は 3 群で満席なので既存の行の右端へ)

| 置き場 | 内容 |
|---|---|
| **群「読む」の行を 4 等分 (幅 260、中心 -420 / -140 / 140 / 420) にして右端に [筆ペン]** (実装 2026-10-16。最初の案「右端 (455, -214) 190×60」は、IMP-UI-006 の [はじめに] (`WalkIntroButton`、`KH_WalkIntroEditor` 直書き) が既に座っていて Pen/1 の重なり検査で FAIL) | [クレジット] (-180) [インフォメーション] (180) の右の空き (360〜550) → [クレジット] [インフォメーション] [はじめに] [筆ペン] の 4 つ。行の定数は `KD_MenuFinishEditor.Hillside.cs` (`HillsideReadEntrySize` 260×60) が正本で、Menu/11・散歩の導入・Pen/1 が同じ定数を読む。写真の ON/OFF が群「自分」の行の右端に座るのと同じ型 (`KH_PhotoGalleryEditor.Place` の `PhotoCaptureToggle`)。群の見出し `settings.group.read` の文言を **「読む」→「読む・書く」** に変える (既存キーの文言だけ。13 言語)。読む = クレジット・インフォ、書く = 筆ペン、で群の意味が通る |
| 群「自分」の言語の行の右端 (455, -22) | 空きはあるが「自分の見え方・聞こえ方」に筆ペンは合わない |
| 新しい群 | 見出し 30 + 行 60 の縦の余地が無い (下端 -250) |

押すと副画面 `Page_Settings/PenPage` (§3-2) が開き、[もどる] で設定の面へ戻る (写真・動画の `_OpenExtraSettings` と同じ。戻り先は設定ホーム)。

## 4. 作業範囲 (2026-10-16 決定 + Codex 相談を反映)

- **U#** `Assets/KominkaHillside/Scripts/KH_PenCall.cs` — `QvPen.UdonScript.QvPen_PenCallbackListener` を継承 (OnPenPickup / OnPenDrop を受ける)、ローカル専用、sync None。**面 (PenPage) の子ではなく、非表示にならないシーン側 (KH_ToyRecall の隣) に置く**: 面を閉じても所有権待ち・20 s の戻しは続ける (描画の更新だけ面が開いている間)。参照 `penManagers[6]` / `pens[6]` はビルダーが確保・配線 (宣言時に `new [6]` しない。vrc-udonsharp §同長配列)。
  - 呼ぶ: `_Call(i)`: 門 = `pens[i].isHeld || pens[i].IsUser || pickup.IsHeld` なら何もしない (ボタンは「使用中」で押せない) → `Networking.SetOwner(lp, pens[i].gameObject)` を 1 回 → Update で所有権待ち (4 s で `WordToyFailed`) → 門を再確認 → 胸の前 (KH_ToyRecall の HoverSpot の式だけ流用。**kinematic 解除・重力・速度 0・床へ落とす後処理は写さない**: 6 本とも kinematic・重力なし) へ position/rotation を書き、同じフレームで `VRCObjectSync.FlagDiscontinuity()`。**PenManager の所有権は取らない** (本当の Pickup で QvPen が取る)。QvPen の状態機械 (currentState / ChangeStateTo*) は触らない。1 人 1 本の進行中 (進行中は他の色を捨てる)
  - 戻す: 置いた時刻から 20 s、**ペンごと**に予約 (単一タイマーにしない)。予約の取消 = 自分の OnPenPickup (通知にペン番号が無いので `pens[i].IsUser` で特定。isUser は StartUsing より先に立つ) / 所有権を失った / `isHeld` が立った / 位置が置いた点から 0.3 m 以上動いた。期限で、まだ Owner・未使用・予約が有効なら `penManagers[i].Respawn()` (筆掛けへ)。退室後は予約が消える (保証しない)
  - 面: 6 色のボタンの文字 = `pen.call.open`「筆ペン」(押せるとき) / `KD_ShopShelf.WordInUse`「使用中」(isHeld のとき、interactable = false)。状態の 1 行 = `WordToyCalling` / `WordToyPlaced` / `WordToyFailed` (4 s)。面が開いている間だけ 0.5 s の見回りで isHeld を描き直す。`_PageOpened` / `_RefreshLanguage` は KH_VideoPage と同じ
- **partial** `KD_TreatPouch.Pen.cs`: `penPage` / `penPageBehaviour`、`_PenOpen` (設定ホームの [筆ペン] から。戻り先は設定ホーム) / `_PenBack`。Video.cs と同じ作り (本体への追記は SetOpen / _RefreshLanguage の 1 行ずつ)
- **Editor** `Assets/KominkaHillside/Scripts/Editor/KH_PenCallEditor.cs` / `.Verify.cs`: `Tools/KominkaHillside/Pen/0 Create Program Asset`、`1 Build`、`2 Verify`、`9 Remove`。Build: シーンの「筆ペン差し替えショートwith筆掛け」(名前で 1 つ。無ければ失敗) → `QvPen_Settings` の serialized `pensParent` の配下の `QvPen_PenManager` を名前 (0)〜(5) の順に 6 件 → 各 Manager の serialized `pen` (**`QvPen_Settings.penManagers` は NonSerialized で Start まで空なので使わない**。名前探索で黙って補完せず失敗させる) → 各 Pen に VRCPickup / VRCObjectSync / Rigidbody (kinematic) があることを検証 → `KH_PenCall` (KH_ToyRecall の隣) に配線。`Page_Settings/PenPage` (extraSettingsPages へ登録): 状態 (-170,170) 700×44、[もどる] (450,170) 220×44、6 色 330×88 を 3×2 (帯 148〜-232 の中)、ボタンの色 = 各 Manager の `colorGradient` の先頭キー。設定ホームの `PenCallButton` (読む・書く の行の右端 = `HillsidePenCallPos` (420,-214)、`HillsideReadEntrySize` 260×60。§3-a) → `pouch._PenOpen` (定数は `KD_MenuFinishEditor.Hillside.cs` に `PenCallButtonName` / `HillsidePenCallPos` / `HillsidePenCallSize`。Photo の `PhotoCaptureToggle` と同じ置き方)。翻訳の適用と字形 (変えた見出しも含む) は Player/1 と同じ経路。保存前検査 (不合格なら保存しない)。Verify (読むだけ): 6 本の配線・順・重複なし・同じルート内、ボタン色 = gradient、面の帯の収まり、設定ホームの重なり無し、extraSettingsPages 登録、13 言語の収まり、NetworkIDs (自作分だけ。別セッション由来の残骸を一括削除しない)。Remove は自作物とその ID だけ
- **Verify 22 の追従** (`KD_MenuFinishEditor.Hillside.cs` の members): [筆ペン] は **群「読む」(members[2])** に「置いたときだけ数える」(Photo と同じ分岐)。Menu/23 は触らない
- **文言** `UITranslations.json`: 新規 **1 キー** `pen.call.open`「筆ペン」× 13 言語 + `settings.group.read` の文言を「読む」→「読む・書く」(13 言語)。再利用: `KD_ShopShelf.WordInUse`、`KD_TreatPouch.WordAccBack`、`KD_TreatPouch.WordToyCalling / WordToyPlaced / WordToyFailed` (これらの 11 言語は英語のまま = 既存の状態。翻訳済みとは言わない)
- **文書** game-design §7 に 1 行、§8 変更記録、issue_summary 再生成
- **コミット**: 本票・コード・翻訳・Program Asset・`KD_MenuFinishEditor.Hillside.cs`。シーンは別セッションの未コミット分を含むので stage しない (Pen/1 で再生成できる)。プレハブは `.gitignore` 済み

## 5. 完了条件

Edit で確定できるもの:
- [ ] 設定の群「読む・書く」の [筆ペン] から副画面が開き、[もどる] で設定ホームへ戻る。6 色のボタンの色が各 PenManager の `colorGradient` と一致 (Pen/2)
- [ ] 6 本の配線 (serialized pen)・NetworkIDs・設定ホームの重なり無し (Menu/22 PASS)・**13 言語の文言の存在 + 同梱フォント (ja / en) の収まり**が Pen/2 で PASS。C# / U# 0。Codex 独立レビュー反映

実機でしか決まらないもの (NOT_TESTED のまま review へ):
- [ ] VR で色を押すとそのペンが胸の前に浮き、掴んで描ける。20 s 放置で筆掛けへ戻る。掴んで置いた後は戻らない (A)
- [ ] 2 人: 相手が持っているペンは「使用中」で押せず、相手の手から動かない。離すと押せるようになる。遅参者にも「使用中」が出る。相手の端末に steal ownership が出ない (B)
- [ ] Quest で面が開き、呼べる。線を描き足した状態と Clear 後の負荷を見る (呼べただけでは負荷は決まらない) (D)
- [ ] 11 言語 (ja / en 以外) の [筆ペン]・「読む・書く」・6 色のボタンの収まり (VRChat のフォールバックフォント。Pen/2 は測れない)。起動直後に設定を開いたとき [筆ペン] が今の言語になっている (entryLabel は KH_PenCall.Start で描き、KD_Language の解決順に依存。動画のヘッダと同じ前提) (E)

## 6. 記録

2026-10-16: 起票 (統括 Fable)。

2026-10-16 ユーザー決定: 入口は設定 (あそぶは文脈に合わない)、持たれているペンは「使用中」で押せない (後がちはやめる)。§3-a に設定の中の置き場の案 (群「読む」の行の右端、見出しを「読む・書く」に)。

2026-10-16 Codex 相談 (`codex exec -m gpt-6-astra`、読むだけ) を反映: 6 本とも kinematic なので Place の物理操作は写さない / PenManager の所有権は取らない / 遅参者には PenManager.OnPlayerJoined が StartUsing を再送するので isHeld で足りる (補助に IsUser・pickup.IsHeld) / 戻しは Callback (QvPen_PenCallbackListener) を主にペンごとの予約 / 配線は serialized pensParent → pen (QvPen_Settings.penManagers は NonSerialized) / 面を閉じても処理を続けるため KH_PenCall は面の外 / Verify 22 は群「読む」 / 翻訳は新規 1 キー + 見出しの文言変更 + 既存の再利用 / 撤回した後がち・あそぶの記述を §4・§5 から削除。見送り: なし。

2026-10-16 実装 (統括 Fable、実装 Opus 2 本 (ランタイム + 翻訳 / Editor ビルダー + 設定ホーム))。

- **ランタイム**: `KH_PenCall.cs` (新規 U#、`QvPen_PenCallbackListener` 継承、sync None、`KH_Dogs/KH_PenCall` = 面の外。Busy = isHeld / IsUser / pickup.IsHeld → SetOwner 1 回 → Update で所有権待ち (4 s) → Busy 再確認 → 胸の前 (KH_ToyRecall の HoverSpot の式。壁・地面の間合いだけ写し、物理は触らない) へ SetPositionAndRotation + FlagDiscontinuity。戻しはペンごとの予約 (20 s)、取消 = 自分の OnPenPickup (IsUser で特定) / 所有権喪失 / 誰かが持った / 0.3 m 動いた。見張りは 0.25 s ごと、面の描き直しは開いている間だけ 0.5 s)。`KD_TreatPouch.Pen.cs` (partial: `penPage / penPageBehaviour`、`_PenOpen / _PenBack`、本体は 2 行)。翻訳: 新規 `pen.call.open` × 13 言語 (ja 筆ペン / en Brush pen / ko 붓펜 / de Pinselstift / zh 毛笔・毛筆 / ru Кисть-ручка / uk・es・fr・pt-br・it・pl は「筆」1 語)、`settings.group.read` を ja 読む・書く / 他 12 言語 "Read & Write" (既存の self / room の見出しも ja 以外は英語なので揃えた)。再利用: WordInUse / WordAccBack / WordToyCalling / Placed / Failed。字形 7 (筆 毛 笔 붓 펜 ・ &) を共有フォントへ
- **Editor**: `KH_PenCallEditor.cs / .Verify.cs` (Pen/0 Program Asset、1 Build = 筆掛けを名前で 1 つ → `QvPen_Settings.pensParent` 配下の PenManager (0)〜(5) → serialized `pen` (読むだけ、Apply しない) → KH_PenCall に new 配列で配線、`Page_Settings/PenPage` (状態 (-170,170)、[もどる] (450,170)、6 色 330×88 を 3×2 (y 50 / -60)、Image = gradient の先頭、文字は輝度で白 / 濃)、設定ホームの `PenCallButton` → `_PenOpen`、読む・書く の行の並べ直し、翻訳と字形、保存前検査 (FAIL なら保存しない。退避 `Temp/imp-wld-017/before-build.unity`)、2 Verify (読むだけ: 配線・順・色・帯・設定ホームの重なり・4 等分の行・ja/en の収まり・NetworkIDs)、9 Remove (自作物とその ID だけ))。**設定ホームの読む行**: 最初の案の右端 (455,-214) は IMP-UI-006 の [はじめに] (`WalkIntroButton`) が座っていて Pen/1 の重なり検査で FAIL → 行を 4 等分 (260×60、中心 -420 / -140 / 140 / 420) に。定数の正本は `KD_MenuFinishEditor.Hillside.cs` (`HillsideReadEntrySize`、`HillsideReadRow`、`LayoutHillsideReadRow`)、Menu/11 (BuildHillsideSettings)・Walk Intro/1・Pen/1 の 3 経路が同じ定数を読む。Verify 22 の群「読む」は `ReadRowMembers` (必須 2 + 置いてある任意 2)。`KH_WalkIntroEditor` と同 Verify は定数参照に
- **検証 (Edit、統括)**: C# 0 / U# 0。Pen/0 → (シーンが dirty で Pen/1 が安全側で停止 → `SaveOpenScenes` で別セッションの未保存分ごと保存) → Pen/1 1 回目 VERIFY_FAILED (WalkIntroButton と重なり。退避から復元) → 行の 4 等分 → Pen/1 BUILD_OK (保存) → Pen/2 PASS fails=0 warns=0 → Menu/22 PASS 13087/0 → Course/Walk Intro/3 PASS。**Play・実機は未確認** (ClientSim で QvPen の Pickup・同期は見ていない)。11 言語の収まりは VRChat のフォールバックフォントなので実機
- **Codex 独立レビュー (D / M / X、高 0・中 3・低 2) の反映**: (中 D) Walk Intro/1 を先に流すと [はじめに] だけ 260 へ動き旧幅の [インフォメーション] と重なる → `KH_WalkIntroEditor` も `LayoutHillsideReadRow` で行全体を並べ直し、Walk Intro/3 に「他の設定ホームのボタンと重ならない」を追加 / (中 M) §5 の「13 言語の収まり」を「13 言語の文言の存在 + 同梱フォント (ja / en) の収まり」に直し、11 言語の収まりを実機 (E) へ / (中 X) 「使用中」のドイツ語 `Aktiv` は「使えない理由」に読めない → `In Benutzung` に (共用キー `KD_ShopShelf.WordInUse`。首輪・リードの「使用中」にも通じる) / (低 M) Pen/2 に `colorLabels` / `backLabel` の backing 検査を追加 / (低 M) §4 の旧座標 (455,-214) 190×60 を現行値に。誤指摘: なし。別件候補 (Codex): 設定の群見出し 12 言語の英語残存と状態文 11 言語の未翻訳は既存の多言語課題 ([IMP-UI-005](IMP-UI-005-complete-thirteen-language-ui.md)) で扱う。反映後: C# 0、Pen/1 BUILD_OK (保存)、Pen/2 PASS 0/0、Menu/22 PASS 13087/0、Walk Intro/3 PASS 82/0、Console エラー 0
- **コミット**: 本票・overview・game-design・`KH_PenCall.cs/.asset`・`KD_TreatPouch.Pen.cs`・`KD_TreatPouch.cs` (2 行)・`KH_PenCallEditor*.cs`・`KD_MenuFinishEditor*.cs`・`KH_WalkIntroEditor*.cs`・`UITranslations.json`・フォント (字形 7)。シーンは別セッションの未コミット分を含むので stage しない (Pen/1 で再生成できる)。プレハブは `.gitignore` 済み

2026-10-16 起票の詳細: プレハブ・QvPen のコード・シーン (ファイル読み取り)・手元メニューの構成 (調査サブエージェント 1 本) の読み取りだけ。Unity 未操作、実装なし。入口の推奨 = あそぶ (§3-1)、代案 = 設定 (ユーザー案)。advisor 相談で、後がちは 2 段階 (相手の端末で Drop → 所有権) にしないと BUG-DOG-006 と同じエラーになる点、放置の戻し先を筆掛けにする点、色 = 6 本から選ぶ点を §3 へ明記。
