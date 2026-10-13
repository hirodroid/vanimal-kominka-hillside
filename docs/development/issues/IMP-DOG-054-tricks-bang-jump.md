---
id: IMP-DOG-054
type: IMP
system: DOG
status: wip
priority: Medium
summary: 呼んで座らせた犬に芸「バーン」「ジャンプ」を指示する (DES-DOG-012 第 2 版の実装)。VR は手をゾーンへ速く入れる合図、全端末で呼ぶタブのボタン。トレーナー特典 (裏山だけ)、伸び/あくびと同じ一発再生 (trickKind + stretchMs)、使える状態になったらアクションアシスタント的なガイドを出す
next: "2026-10-13 実装・検査・Codex レビュー反映まで完了 (status review)。残りはユーザー: PCVR で合図 (バーン = 頭へ手をサッと、ジャンプ = 頭の上へサッと) の受理・不受理ケース・小型犬への到達性、ガイドの見え方、Desktop のボタン、2 人で同じ時刻に同じ芸が見えること。tricksEnabled は今 ON (確認用)。初期公開のビルドの前に OFF にするか (§2.10) と「モード」の読み (§1 決定 7) の確認"
updated: 2026-10-13
decided: 2026-10-13
---

# IMP-DOG-054 芸「バーン」「ジャンプ」と使える時のガイド (実装)

設計と決定は [DES-DOG-012](DES-DOG-012-gesture-tricks-and-trainer.md) (§方式の提案 第 2 版)。この Issue は第 2 版を実装の単位と担当に分け、並行する 3 人の実装担当の契約 (§4) を決めたもの。トレーナーの販売説明は [DES-UI-005](DES-UI-005-trainer-and-paid-items-purchase-flow.md)、ガイドの見せ方の前例は [IMP-UI-006](IMP-UI-006-first-walk-intro.md) (`KH_WalkIntro`)、受信側の権利検査の型は [BUG-DOG-009](BUG-DOG-009-frisbee-style-receiver-check.md)。

## 1. 依頼と決定

DES-DOG-012「ユーザーに決めてもらうこと」の 6 件と、2026-10-13 に足された要件 1 件。1・2・4・5・6 の中身は DES-DOG-012 の推奨文の写し。

決定: 2026-10-13 (ユーザー決定「すべて推奨で」) 1 入力の方式: **ゾーンへ入る動き (速さ + 向き) で一発受理。** 指形は第一段で使わない。理由: 既存の `UpdatePaw` の手の位置をそのまま使え、アバターに依存しない。お手 (止まる) との区別は速さで付く見込みだが仮説で、試作の不受理ケースで確かめる。指形 (指鉄砲) は後段の試作が成立したら足す。

決定: 2026-10-13 (ユーザー決定「すべて推奨で」) 2 使う手: **空いている手のうちゾーンに近い方 (左右どちらでも)。** 左手固定にしない。理由: お手と同じ振る舞いで、利き手・座る向きの差を吸収する。左手固定を選ぶなら既存のお手も左手固定にする。

決定: 2026-10-12 (ユーザー回答「いいね。バーン、ジャンプ。にしようかな。2 案」) 3 第一段の芸: **バーン と ジャンプ**。一発再生の伸び/あくびの型を使い、クリップは Editor の生成器で 6 犬種分を出す。

決定: 2026-10-13 (ユーザー決定「すべて推奨で」) 4 権利: **第一段の芸はすべてトレーナー特典。既存の 4 動作は現行の 30pt のまま。トレーナーの価格は今回変えない** (価格の判断は DES-UI-005 が持つ)。トレーナーは古民家と同じ Listing なので、特典に芸を足すと Listing の説明と古民家側の案内も変わる。**芸が使えるのは裏山だけ** (古民家には実装しない)。**提供前に販売説明へ載せない** (初期公開の看板・Listing は現行特典のまま。芸を出した版で DES-UI-005 と古民家側の案内を更新する。古民家側は別 Issue を古民家の repo に起こす)。DES-UI-005 側の記録は同日の決定行 (特典の範囲そのものの決定ではない)。

決定: 2026-10-13 (ユーザー決定「すべて推奨で」) 5 対象犬: **自分が呼んで到着した 1 匹、おすわり中。** リード中・周囲の犬全員は対象外。

決定: 2026-10-13 (ユーザー決定「すべて推奨で」) 6 公開時期: **初期公開には入れず、公開後の最初の追加。** 理由: 初期公開は Play・実機の確認待ちが多く、4 で特典にするので Listing の変更は古民家と足並みが要る。**この決定は機能の提供 (ワールドで使えること) と案内 (Listing・看板・古民家側) の両方を初期公開から外す。実装は今進め、提供は初期公開の後** (Codex 仕様レビュー 2026-10-13 で訂正)。実装は今進める (ユーザー指示「実装イシュー起こしたら実装も進めて完了させてください」)。ユーザーが出すと決めるまで、芸を販売説明・看板・Listing で案内しない。初期公開のビルドに芸を載せないためのスイッチは §2.10。

決定: 2026-10-13 (ユーザー依頼「このモードにしたときに、アクションアシスタント的にガイドが出てほしい」) 7 ガイド (新規): 芸の指示が使える状態になったら、本人にだけガイドを出す。**読み (統括の読み、ユーザー確認待ち)**: 第 2 版はモードの切り替えを採らない (DES-DOG-012 §方式の提案 第 2 版 2 段落目「短い芸の指示モードをメニューで有効にする」案は採らない) ので、「このモードにしたとき」= **自分が呼んだ犬が到着しておすわり (PoseId 2) し、本人がトレーナーを持ち、芸が再生中でなく、8 s の間隔が過ぎた (または残り秒を出す) とき**と読む。トグルは足さない。見せ方は §3。

## 2. 仕様 (DES-DOG-012 第 2 版の写し + 実装の細目)

### 2.1 番号の対応 (1 つの表に揃える)

| 芸 | 要求の op (`KD_SleepSlot`) | 同期 `trickKind` | Animator State | `CallPose` | 長さ (窓と正規化) | クリップ |
|---|---|---|---|---|---|---|
| 無し | — | 0 | — | 姿勢どおり | — | — |
| 伸び/あくび (既存) | Stretch 9 | 1 | CallStretch | 7 | 6000 ms | `KD_StretchYawn_<犬>` (既存) |
| バーン | **Bang 10** | 2 | **CallBang** | **8** | 6000 ms | `KH_Bang_<犬>` (新規) |
| ジャンプ | **Jump 11** | 3 | **CallJump** | **9** | 2000 ms | `KH_TrickJump_<犬>` (新規) |

op の表と State 番号の表は別 (op 8 は Chin、State 8 は CallBang)。ゾーンの配列とガイドの API の「種類」(kind) も `trickKind` の値 (2 = バーン、3 = ジャンプ) を使い、4 つ目の番号を作らない。`<犬>` は Corgi / Pome / Lab / Dachs / Shiba / Samoyed。

### 2.2 VR の合図 (ゾーン)

一発型なので「ゾーンに**入る動き**」で受理する。お手 (ゾーンで止まる) とは速さで分ける想定だが、これは**仮説**で、不受理ケースで確かめる (§5)。

| 芸 | ゾーン (犬のローカル、座り姿勢。中心 + 半径) | 受理の条件 | 犬の動き |
|---|---|---|---|
| バーン | 中心 = 頭の骨から鼻の向きへ 0.35 m、頭の高さ。半径 0.15 m | 手が犬の頭へ向かって速さ 0.6 m/s 以上でゾーンへ入る (速度ベクトルと手→頭の向きが 30° 以内) | 撃たれて右へ倒れ、へそ天で 3 s 止まり、起きて座る (全体 6 s) |
| ジャンプ | 中心 = 頭の骨の上 0.45 m。半径 0.15 m | 手が上向きに速さ 0.6 m/s 以上でゾーンへ入る (速度ベクトルの上向き成分が 0.6 m/s 以上) | しゃがんで跳び (腰が 0.2〜0.3 m 上がる)、着地して座る (全体 2 s) |

- **手の選び方**: 候補は空いている手 (道具を持つ手は `GetPickupInHand` で外す。[KH_HandWash.cs](../../../Assets/KominkaHillside/Scripts/KH_HandWash.cs) と同じ型。手元メニューは Pickup ではないので外れない)。2 本とも空いていればゾーンの中心に近い方。左右固定にしない。
- **速度**: 現行 `UpdatePaw` の `speed` は「選んだ手」と「前回の手」の差で、左右が切り替わると跳ねるスカラー。合図には**左右別の位置履歴から速度ベクトル**を出し、手の切替・追跡の復帰で履歴を初期化する。
- **1 回の合図で 1 回だけ受理**し、手がゾーンから出るまで次を受けない。受理後は伸び・バーン・ジャンプで**共有の 8 s の間隔**。間隔中にゾーンへ入った手は、いったん出て入り直すまで受けない。**再生中は新しい芸を受けない。**
- **不受理にする日常の動き** (試作で確かめる): 撫で始めに顔へ手を伸ばす、撫で終えて手を上げる、お手の前に手を速く出す、メニューを操作する手、空いた手を振る。速さ・角度・ゾーンの値はこれで決める (上の 0.6 m/s・30°・0.35 m・0.45 m・0.15 m は初期値。Setup の定数 1 か所に置く)。
- **到達性**: ゾーンは犬の頭の高さ基準なので、小型犬 (ダックス・コーギー・ポメ) では立位 VR でかがむ必要がある。犬種ごとに立位・着座 VR で届くかを見て、届きにくい場合はゾーンの高さを上げる。VR でもボタンで同じ芸を出せることを案内に書く (§3)。
- **指形** (指鉄砲など) は第一段では使わない。

実装の細目 (統括の判断): ゾーンの中心は `pawTargets` と同じく**犬の根 (DogRoot) のローカル座標**で持つ。Setup がプレビューシーンで座り姿勢 (`KD_Sit_<犬>`) にした複製から、頭の骨 **`Bone.004`** (KD_CompanionMotionEditor が頭の駆動に使う骨) の位置で計算する。「鼻の向き」は**犬の根の前方 (水平に投影)** と読む (座った犬は前を向いている。骨の軸の向きを推測しない)。呼ばれた犬は配置点に凍結しているので、実行時は `root.TransformPoint` で足りる (骨を毎フレーム追わない)。合図の判定は既存 `UpdatePaw` の **VR の分岐だけ** (Desktop・Touch に合図の経路は足さない)、20 Hz の tick の中で、`_TrickStatus(dog) == 0` のときだけ行う (判定の可否の出どころを 1 つにする)。既存のお手の判定はそのまま残す。

**ジャンプの高さの読み (2026-10-13、統括の判断。Codex 独立レビューの指摘 中 を受けて記録)**: DES の「腰が 0.2〜0.3 m 上がる」は、実装では **頂点で体の最低点が地面から 0.20〜0.30 m 離れる** (= 見えるジャンプの高さ) として生成・検査する (実測 0.254〜0.267 m)。座り姿勢の腰を基準に 0.25 m だけ上げる読みは、座り → 空中姿勢 (脚が伸びる) で腰が先に 0.38〜0.65 m 上がるため、Lab で脚が地面を割る。腰の上昇量は `build.txt` の `pelvisRiseOverSit` に記録するだけで合否にしない。

### 2.3 Desktop・Touch・VR のボタン

呼ぶタブのポーズ面 (既存 `stretchButton` の並び) に [バーン] [ジャンプ] を足す。伸びと同じ一発のボタン (距離条件なし)。VR でも同じボタンを押せる。新しいタブは作らない。ボタンの状態:

| 状態 | 表示 | 押すと | `_TrickStatus` |
|---|---|---|---|
| トレーナー未所持 | 芸名 + 「トレーナー」 | 購入画面への案内 (看板と同じ [購入画面をひらく]) | 6 |
| 所持の読み込み中 | 押せない + 既存「よみこみ中」 | — | 7 |
| 座り以外・伸び中・おやつ中・配置中・要求中 | 押せない + 理由 (既存 `call.unavailable` 系) | — | 2・3 |
| 再生中 | 押せない + 「再生中」 | — | 4 |
| 8 s の間隔の残り | 押せない + 残り秒 | — | 5 |
| 使える | 芸名 | 要求を送る | 0 |

- 未所持の表示は芸名 + `call.trick.trainerTag`。「押すと」: 状態行に `call.trick.trainer` を出し、`trainerPass._OpenStore()` (トレーナーの Listing。[KD_ActivitiesMenu.cs](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_ActivitiesMenu.cs) の [購入画面をひらく] と同じ呼び出し) を呼ぶ。既存の `_purchase` / `pouch._ShowItemConfirm` (KD_ShopItem 用のポイント購入カード) には載せない (トレーナーは KD_ShopItem ではない)。
- 「よみこみ中」は既存の `KD_ShopShelf.WordLoading`、理由は既存の `call.unavailable` / `call.lockedHint` を流用。
- **配置の決定 (1-B に任せる、未決)**: ポーズ面のボタンの格子 (y −150: Stretch / Apply / Place、y −210: Back / Paw / Release、[KD_CallPoseEditor.cs](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Editor/KD_CallPoseEditor.cs) :197-202) は埋まっている。行を 1 つ足すか、幅を詰めて並べる。枠からあふれない・既存ボタンの位置を動かす場合は既存の手調整を壊さないことを Verify で確かめる。

### 2.4 対象犬・姿勢

自分が呼んで到着した 1 匹 (`IsSleeperLocal`) がおすわり (PoseId 2) のとき。伏せ・あご乗せ・へそ天・丸まりからは出さない (クリップの始点が座りなので)。伸び中・おやつ中 (`CalledTreatActive`)・配置中・`CallPending` は不可。リード中の犬は呼べないので、散歩中の芸は初期範囲外。

### 2.5 同期 (`KD_SleepSlot`)

`hasStretch` (bool) を **`trickKind` (int: 0 無し / 1 伸び / 2 バーン / 3 ジャンプ)** に置き換え、`stretchMs` を開始時刻として共用する (同期変数の数は変えない)。

- 長さは種類ごと (伸び 6 s / バーン 6 s / ジャンプ 2 s) で、**窓の判定 (`TrickActive`) と正規化 (`TrickTime` = 経過 / 種類の長さ) の両方**に使う (現行の `/ 6000f` 固定 ([KD_SleepSlot.cs](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepSlot.cs):77) を直す。ジャンプを 6000 で割ると 0.33 で止まり着地まで再生されない)。
- 「再生が終わる」(窓を過ぎる) と「開始の履歴を捨てる」を分ける。**8 s の間隔は `stretchMs` だけで判定**し (`trickKind` に依らない)、`Pose` の受信で `trickKind = 0` にして再生を止めても `stretchMs` は残す (現行の `hasStretch = false` は姿勢変更で間隔を回避できる)。`Commit` (呼び直し、:254) と `PublishRelease` (:377) では両方を消す (`trickKind = 0; stretchMs = 0`、新しい世代)。
- 要求は 8 引数のまま。受信 (`HandleRequest`) の **Bang / Jump の行**: `sleeperId == id && (op == Bang || op == Jump) && poseId == 2 && arrived && CanStretch && トレーナー所持 (送り手)` → `stretchMs = now; trickKind = 種類; pawHeld = false; chinHand = 0`。既存の Stretch の行には座り限定を**足さない** (伸びは今の姿勢条件のまま。`trickKind = 1` を書く)。
- `CalledTreatActive`・配置中・`CallPending` は**送り手のローカル状態なので送り手側で落とす** (受信側では見ない)。
- Animator: `CallPose` 8 / 9 の一発 State **CallBang / CallJump** を足し、CallStretch と同じく `timeParameter = CallTime` にする。犬側は `trickKind` 1 / 2 / 3 → CallPose 7 / 8 / 9 と `TrickTime` を `CallTime` に書く。書く場所は [KD_DogWanderController.cs](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.cs) :1872-1883 の `if (stretch) animation = 7;` と `stretch ? slot.StretchTime : 0f` (`ApplyCompanionAnimation` の定義がある `Performance.cs` ではない)。
- 「States の末尾に足す」(DES) は **CallPose の番号の並び**の意味。`KD_CallPoseEditor.States` の C# 配列は伸ばさない (§4 1-A)。
- 途中参加は窓の中なら途中から見え、過ぎていれば再生しない。Owner 交代は、受理済みの芸は同期値 (種類・開始時刻) から復元されるので影響なし。**未受理の要求** (交代をまたいで宛先 Owner が変わった要求) は伸びと同じで再送しない。送り手は結果が来なければ再操作する (ボタンは「要求中」を 2 s で解く)。

getter の書き換え (1-B、名前は契約):

| getter | 定義 |
|---|---|
| `int TrickKind` | `LiveSleeperId >= 0 ? trickKind : 0` |
| `int TrickLengthMs(int kind)` | 1 → 6000、2 → 6000、3 → 2000、他 → 0 |
| `bool TrickActive` | `LiveSleeperId >= 0 && trickKind != 0 && 0 <= age < TrickLengthMs(trickKind)` (age = サーバー時刻 − `stretchMs`) |
| `float TrickTime` | `Clamp01(age / TrickLengthMs(trickKind))` |
| `bool CanStretch` | `LiveSleeperId >= 0 && arrived && !TrickActive && (stretchMs == 0 \|\| age >= 8000)` (伸び・バーン・ジャンプ共通の間隔) |
| `bool StretchActive` / `float StretchTime` | 互換のため残す: `StretchActive = TrickActive` (種類を問わない。お手・あご乗せ・おやつ・撫での頭の追従を止める既存の読み手 (`PawHeld`・`ChinHand`・`KD_DogWanderController.IsStretching` :888・:1872) がそのまま芸全体に効く)、`StretchTime = TrickTime` |

`stretchMs == 0` を「履歴なし」に使うので、受理時に `now == 0` なら 1 にする (`calledMs` と同じ扱い)。

### 2.6 権利

検査の置き場所は `HandleRequest` (Owner の直接呼び出しも `RequestSlot` もここを通る)。規則: **`Stretch` (9) は送り手の `ActionOwnedBy(StretchYawn)`、`Bang` (10) と `Jump` (11) は送り手の `KH_TrainerPass._OwnedBy`**。「それ以外は拒否」は一発芸の分岐の中だけ (既存の Reserve〜Chin の行は変えない)。参照が無い (TrainerPass 未配線) ときも拒否。未ロードは拒否。滞在中の失効は再入室で判定 (現行の方針)。送り手側 (`KD_SleepSlot._Bang()` / `_Jump()`) も同じ判定で早期に落とし、未所持の人には [購入画面をひらく] の案内を出す (BUG-DOG-009 の型)。**芸が使えるのは裏山だけ** (古民家の repo には実装しない)。

`KD_SleepSlot` と `KD_CallPose` に `public KominkaHillside.KH_TrainerPass trainerPass;` を足す (前例 `KD_ActivitiesMenu.hillsidePass`)。Setup が `KH_Dogs/KH_TrainerPass` を配線し、Verify が null でないことを確かめる。

### 2.7 競合

集合・フリスビー・リードは「呼ぶ」の入口 (`_IsWalkBusy`) で、洗浄は犬側 (`WashYieldsToErrand`) で、呼ばれている犬から外れている。再生中は伸びと同じ扱い: おやつ・お手・あご乗せは止まり、**撫では続く** (ハート・尻尾は出て、頭の追従だけ抑える)。バーンで倒れている犬を撫でられるのはそのまま (止めるなら追加の変更、この Issue ではしない)。

### 2.8 文言 (13 言語、`KD_Language`)

DES の 7 件 + 統括の追加 2 件 (ボタンの添え字 `call.trick.trainerTag`、ガイドの Desktop・Touch の 1 行 `call.trick.hint.menu` (決定 7))、計 9 件。[UITranslations.json](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Data/UITranslations.json) の各言語の `texts` に足す (既存 `call.stretch` の並びの後)。数の差し込みは既存の `{0}` の書式。**13 言語の訳は AI 作成、校正なし** (ja/en 以外の字形・収まりは実機で確認)。

| キー | 用途 | ja (案) |
|---|---|---|
| `call.trick.bang` | ボタンの芸名 | バーン |
| `call.trick.jump` | ボタンの芸名 | ジャンプ |
| `call.trick.hint.bang` | 合図の案内 (状態行・ガイド) | 頭へ向けて手をサッと出す |
| `call.trick.hint.jump` | 合図の案内 (状態行・ガイド) | 頭の上へ手をサッと上げる |
| `call.trick.playing` | 再生中 | 再生中 |
| `call.trick.wait` | 間隔の残り | あと {0} 秒 |
| `call.trick.trainer` | 未所持の案内 (状態行。既存 `activities.hillside.needTrainer` の文体) | バーン・ジャンプはトレーナー特典です。購入画面からどうぞ |
| `call.trick.trainerTag` | 未所持のボタンの添え字 (芸名 + この語。統括の追加) | トレーナー |
| `call.trick.hint.menu` | ガイドの Desktop・Touch の 1 行 (決定 7) | メニュー「呼ぶ」の [バーン] [ジャンプ] |

ja の文面は案。1-C が既存の文体 (`call.pawHint` 等) に合わせて決めてよい。ボタンの文字は `KD_CallPose.Draw` が毎回書くので、[UILocalizationBindings.json](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Data/UILocalizationBindings.json) への行は要らない (`stretchButton` と同じ)。

### 2.9 クリップの生成

Editor の生成器 (Blender 無し)。出力は `Assets/KominkaHillside/Animations/`。

- **バーン** `KH_Bang_<犬>.anim` (6 s、ループなし): 座り (`KD_Sit_<犬>`) → 右へ横倒し (**中間キー**) → へそ天 (`KD_BellyUp_<犬>`) で 3 s 止まる → 起きて座る。2 姿勢の補間だけでは「横へ倒れる」は定義されないので、倒れる側 (右へ固定)、途中の横倒しの姿勢、腰の軌道、起き上がる順序を生成器に持たせる。終わりのフレームは座りと一致させる (CallSit へ戻る遷移で跳ばない)。
- **ジャンプ** `KH_TrickJump_<犬>.anim` (2 s、ループなし): 座り → `KH_Jump_<犬>` の姿勢 + **腰 (`AnimationRoot`) と尻尾 (`SiblingTail`) の両方**の上下 0.2〜0.3 m → 座り。根 (DogRoot) は動かさない (呼ばれた犬は配置点に凍結しているので、移動の権限・NavMesh・リードに触れない)。[KH_FrisbeeJumpClipEditor.cs](../../../Assets/KominkaHillside/Scripts/Editor/KH_FrisbeeJumpClipEditor.cs) と同じく Constraint の元側へ焼く。元の `KH_Jump_<犬>` は待機姿勢始まりなので、座りへの接続を作る。
- **犬ごとの確認**: 倒れ方 (中間で胴が地面を横切らない)、跳ぶ高さ、Constraint 適用後の四肢・胴の接地 (脚が浮いて見えない)。出所は [KD_CompanionMotionEditor.cs](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Editor/KD_CompanionMotionEditor.cs) (座り・へそ天の生成)、KH_FrisbeeJumpClipEditor.cs (跳びの姿勢)。

### 2.10 提供前のスイッチ (統括の判断、ユーザー確認待ち)

決定 6 (初期公開に入れない) と「実装は今進める」を両立させるため、`KD_SleepSlot` に `public bool tricksEnabled` を置く (6 枠とも同じ値。Setup が書く)。OFF のとき: 受信は Bang / Jump の行を拒否、`_TrickStatus` は 8 (ボタンを出さない・合図を判定しない・ガイドを出さない)。伸び/あくびは影響を受けない。

- Setup は **既存の値を保持する** (新規配線で未設定なら ON。無条件に ON へ上書きしない)。確認用の ON と公開用の OFF は Tricks メニューの明示の切り替えで行う。
- **初期公開のビルドの前に OFF にして Verify で確かめる手順は、§6 の「出すと決めた時」とは独立した必須手順** (Codex 仕様レビュー 2026-10-13 で訂正)。
- Verify は 6 枠の値が一致することを合否にし、OFF のとき: 受信が op 10 / 11 を拒否する、ボタン・合図・ガイド・購入案内が出ない、既存の伸び/あくびは使える、を確認項目にする (§5)。
- ON/OFF をどちらに保存しておくかは、初期公開の時期と実機確認の順序で統括がユーザーに確認する。

## 3. ガイド (決定 7)

**統括の判断 (UI の通常の判断)、ユーザー確認待ち**。出すきっかけの読みは §1 決定 7 のとおり (モードの切り替えは無い)。

### 3.1 出す・消す

`KH_TrickGuide` が `KD_CallPose._TrickGuideDog()` で対象の 1 匹を取り、その `_TrickStatus(dog)` で決める。

| `_TrickStatus` | ガイド |
|---|---|
| 0 使える | 出す (VR はゾーン、Desktop・Touch は 1 行) |
| 5 間隔の残り | 出す。ゾーンの所に「あと N 秒」(`call.trick.wait`)、ゾーンは薄く |
| 4 再生中 | 受理した側のゾーンが一度光って消える。「再生中」(`call.trick.playing`) を 1 行 |
| 1・2・3・6・7・8 | 出さない (出ていれば消す) |

- **始まり** = 状態が {0, 4, 5} の外から {0, 5} へ入った時。2.0 s でフェードイン。4 → 5 → 0 (芸の後の間隔) は同じ続きとして扱い、フェードし直さない。
- **毎回出す**: 状態が始まるたびに出す (呼び直し・座り直しのたび)。初回だけにはしない。
- **消える**: 犬が立つ・ポーズを変える (状態 2)、解除・呼び直し (状態 1)、本人が犬から水平 6 m を超えて離れる (解除の距離 8 m の手前)、間隔中でも他の理由で状態 3 になった時。
- **未所持の人には何も出さない** (状態 6)。ボタンが既に「トレーナー」と購入の案内を出しているので、ガイドで販売を押し付けない。決定 4 の「提供前は販売説明へ載せない」とも合う。
- 設定のトグル `KH_TrickGuide.hidden.v1` (PlayerData、`KH_WalkIntro.hidden.v1` と同じ型) は**任意**。設定タブに空いた行があれば 1-C が足す。無ければ足さずに §7 の別件へ記録する。

### 3.2 VR で見せるもの

- ゾーンの印 2 つ: 各ゾーンの中心 (`_TrickZoneCenter(dog, kind)`) に半径 (`_TrickZoneRadius(dog, kind)`) の輪。色で分ける (バーン / ジャンプ。夜でも区別できる明るい縁)。輪の上にラベル「バーン」「ジャンプ」(`call.trick.bang` / `call.trick.jump`)。
- ゾーンごとに 1 行の短い案内 (`call.trick.hint.bang` / `call.trick.hint.jump`)。
- 弾む矢印は任意 (KH_WalkIntro の矢印を流用してよい)。
- 毎フレーム犬に追従する (座って凍結しているので根のローカルからの計算で足りる。PostLateUpdate で位置を書く)。
- 受理したら、その側の印が一度光って消え、「再生中」。間隔中は「あと N 秒」を印の所に出す。

### 3.3 Desktop・Touch

ゾーンは出さない (手を自由な位置へ出す入力が無い)。犬の頭の近くに 1 行「メニュー「呼ぶ」の [バーン] [ジャンプ]」(`call.trick.hint.menu`) を出し、手元メニューを指す。ボタンに距離の条件は無い (伸びと同じ) が、ガイドは犬から 6 m 以内だけ出す。

### 3.4 実装の要点

- 新しいローカルの `KH_TrickGuide` (`Assets/KominkaHillside/Scripts/KH_TrickGuide.cs`、同期 None、独立した GameObject 1 つ)。`KD_CallPose` / `KD_SleepSlot` の public な読み取りだけを使う (§4 の契約)。**所有権を取らない・共有状態を書かない・要求を送らない。**
- 描画は KH_WalkIntro と同じ専用マテリアル + 描画順 (輪・矢印 5000、文字 5001。KH_NightOverlay 4999 の後) で、夜の暗化に沈ませない。KH_WalkIntro のマテリアル・メッシュは変更しない (メッシュの資産を参照するのは可)。
- 判定は 0.15 s 間隔 (KH_WalkIntro と同じ)、位置は PostLateUpdate。犬を呼んでいない間は何もしない (`_TrickGuideDog() < 0` で早期に戻る)。
- 読む面に Collider / Raycaster を持たせない。

## 4. 実装の分担とインターフェース契約

3 人を並行で走らせる。**同じファイルを 2 人が触らない。** 統括が Unity のメニュー実行・コンパイル・保存を持つ (担当は Unity を操作しない)。`Imported/KominkaDogRun/` の既存ファイルへの変更には `// 裏山改変: IMP-DOG-054 …` の注記を付ける。

### 4.1 ファイルの持ち分

| 波 | 担当の中身 | 持つファイル (このファイルだけ編集・作成する) |
|---|---|---|
| 1-A クリップと Animator (Editor だけ) | クリップ 2 種 × 6 犬種の生成器、Controller 6 つへの State 追加 | 新規 `Assets/KominkaHillside/Scripts/Editor/KH_TrickClipEditor.cs`、生成物 `Assets/KominkaHillside/Animations/KH_Bang_<犬>.anim`・`KH_TrickJump_<犬>.anim` (と `.meta`)。Controller (`AC_KD_Dog_<犬>.controller`) は 1-A のメニューを統括が流して変わる (担当は手で編集しない) |
| 1-B Runtime の核とボタン | 同期・受信・権利・getter、犬側の CallPose の対応、合図の判定、`_TrickStatus` 等の API、[バーン] [ジャンプ] ボタン (作成・状態表・押下)、ゾーン配列の Setup / Verify、提供前のスイッチ | `KD_SleepSlot.cs`、`KD_CallPose.cs`、`KD_CallPoseEditor.cs` (ボタンを 6j でも作るなら)、`KD_DogWanderController.cs` の **:1870-1884 の 1 ブロックだけ** (他の担当・別セッションも触る大きいファイル。ほかの行は触らない)、新規 `Assets/KominkaHillside/Scripts/Editor/KH_TrickSetupEditor.cs` |
| 1-C ガイドと文言 | `KH_TrickGuide`、その Setup / Verify とマテリアル、文言 9 件 × 13 言語、(任意) 設定のトグル | 新規 `Assets/KominkaHillside/Scripts/KH_TrickGuide.cs`、新規 `Assets/KominkaHillside/Scripts/Editor/KH_TrickGuideEditor.cs`、新規マテリアル (`Assets/KominkaHillside/Materials/KH_TrickGuide_*.mat` 等)、`UITranslations.json`。設定のトグルを足す場合だけ `KD_TreatPouch.Settings.cs` (1-B は触らない) |

**依頼文からの変更 (統括へ)**: ボタンは 1-C の「KD_TreatPouch の呼ぶタブ」の想定だったが、ポーズ面のボタンは `KD_CallPoseEditor.Setup` が作り (`Button("Stretch", …, "_Stretch")`)、`KD_CallPose` が押下と `Draw` を持つ。どちらも 1-B のファイルなので、**ボタンは 1-B が端から端まで持つ**。1-C の Runtime は `KH_TrickGuide` と文言だけ。

### 4.2 1-B が公開する API (1-C・統括が使う。名前と意味は固定)

`KD_SleepSlot` (犬 1 匹の枠):

| メンバー | 意味 |
|---|---|
| `const int Bang = 10, Jump = 11` | 要求の op (private のままでよい) |
| `const int TrickNone = 0, TrickStretch = 1, TrickBang = 2, TrickJump = 3` | `trickKind` の値 (public) |
| `int TrickKind`、`bool TrickActive`、`float TrickTime`、`int TrickLengthMs(int kind)`、`bool CanStretch` | §2.5 の表 |
| `int TrickWaitMs` | 間隔の残り (ms)。`stretchMs == 0` または 8000 ms を過ぎていれば 0 |
| `bool tricksEnabled` | §2.10 |
| `KominkaHillside.KH_TrainerPass trainerPass` | §2.6 |
| `void _Bang()` / `void _Jump()` | 送り手。`IsSleeperLocal && !CallPending && PoseId == 2 && CanStretch && tricksEnabled && trainerPass != null && trainerPass._Loaded() && trainerPass._OwnedBy(LocalPlayer)` のときだけ op 10 / 11 を送る (8 引数、`pose` = 0、`flag` = false)。送ったら 2 s の「要求中」(`TrickRequestPending`) を立てる |
| `bool TrickRequestPending` | 送ってから 2 s、または結果 (`stretchMs` の変化) が来るまで true |

`KD_CallPose` (呼ぶタブのポーズ面と手の判定。犬は添字 0〜5):

| メンバー | 意味 |
|---|---|
| `void _Bang()` / `void _Jump()` | ボタンの押下。未所持なら §2.3 の案内。使えるなら送り手だけの条件 (`CalledTreatActive`・`placement.Active`・`IsCompanionReady`) を見てから `slot._Bang()` / `_Jump()`。合図の判定もこの 2 つを呼ぶ (ボタンと合図の入口を 1 つに) |
| `int _TrickStatus(int dog)` | 下の表。副作用なし。dog が範囲外なら 1 |
| `float _TrickWaitSeconds(int dog)` | 状態 5 の残り秒 (切り上げ前の値)。それ以外は 0 |
| `int _TrickGuideDog()` | ガイドの対象。本人が呼んでいる犬のうち状態が {0, 4, 5} の最初の 1 匹 (犬の添字の小さい順)。無ければ −1 |
| `Vector3 _TrickZoneCenter(int dog, int kind)` | kind 2 / 3 のゾーンの中心 (ワールド)。`DogRoot().TransformPoint(trickZoneCenters[dog * 2 + kind − 2])`。不正なら `Vector3.zero` |
| `float _TrickZoneRadius(int dog, int kind)` | 半径 (m)。不正なら 0 |
| `int _TrickLastAccepted(int dog)` | 直近に受理された種類 (同期の `TrickKind`。再生中のガイドの光らせ分けに使う) |
| `Vector3[] trickZoneCenters` (12)、`float[] trickZoneRadii` (12) | 添字 `dog * 2 + (kind − 2)`。犬の根のローカル。Setup が書く |
| `KominkaHillside.KH_TrainerPass trainerPass` | §2.6 |

`_TrickStatus(dog)` の値と優先順 (上から最初に当たったもの):

| 値 | 意味 | 条件 |
|---|---|---|
| 8 | 提供前 (無効) | `!slot.tricksEnabled` |
| 7 | 所持の読み込み中 | `trainerPass == null` ではなく `!trainerPass._Loaded()` (null は 6) |
| 6 | トレーナー未所持 | `trainerPass == null \|\| !trainerPass._OwnedBy(LocalPlayer)` |
| 1 | 呼んだ犬がいない | `!slot.IsSleeperLocal` または未到着・`!IsCompanionReady` |
| 2 | 座っていない | `slot.PoseId != 2` |
| 3 | ほかの事をしている | 伸び中 (`TrickKind == 1 && TrickActive`)、おやつ中 (`CalledTreatActive`)、配置中 (`placement.Active`)、`CallPending`、`TrickRequestPending` |
| 4 | 再生中 | `TrickActive` で `TrickKind` が 2 か 3 |
| 5 | 間隔の残り | `!CanStretch` で再生中でない (`TrickWaitMs > 0`) |
| 0 | 使える | 上のどれでもない |

状態 6 は犬がいなくても返る (ボタンは全員に「トレーナー」を出すため)。ボタン・合図・ガイドの 3 つともこの値だけで分岐する。

### 4.3 1-A の契約

- メニュー (ASCII、`Tools/KominkaHillside/Tricks/`): `1 Build Trick Clips` (6 犬種 × 2)、`1a Build Trick Clips (One Breed)`、`2 Add Trick States` (Controller 6 つ)、`2b Verify Trick Clips and States`。統括が流す。
- **`KD_CallPoseEditor.States` を伸ばさない。** `AnimatorConfigured()` (:103) と Verify (:395-396) はその配列で数えるので、9 に伸ばすと次の `Menu/6j Setup Call Poses` が「未構成」と見て 7 State を作り直す (fileID が変わる、:98-100 の注記)。1-A は自分の配列 `{ "CallBang", "CallJump" }` を持ち、**冪等に**足す (同名の State と遷移があれば消してから足す)。
- 足す State の形は `KD_CallPoseEditor.ConfigureAnimator` (:70-100) と同じ: AnyState → State (`hasExitTime = false`、固定 0.2 s、`canTransitionToSelf = false`、条件 `CallReady` かつ `CallPose == 8` / `9`)、`timeParameterActive = true`、`timeParameter = "CallTime"`、出口は Run / Walk (`!CallReady && Run|Walk`、0 s) と既定 State (`!CallReady`、0.2 s)。
- `Menu/6j` が何かの理由で 7 State を作り直したら、`Tricks/2` を流し直す (統括の手順に書く)。
- Verify: 6 犬種 × 2 のクリップがあり長さが 6.0 s / 2.0 s、ループなし、最初と最後のフレームが `KD_Sit_<犬>` と一致 (許容差を定数に)、根の位置カーブが無い (ジャンプ)、Controller 6 つに CallBang / CallJump が 1 つずつ・遷移の条件が 8 / 9。可能ならプレビューシーンで CallPose 8 / 9・CallTime 0〜1 を流して接地 (四肢の最低点) と胴の最低点を記録する (KH_FrisbeeJumpClipEditor の `3 Render Stages` 相当の静止画は任意)。

### 4.4 1-B の Setup / Verify

- 新規 `KH_TrickSetupEditor.cs`、メニュー `Tools/KominkaHillside/Tricks/3 Setup Call Tricks` と `4 Verify Call Tricks`。**`Menu/6j Setup Call Poses` は流さない** (配置ゴースト・ミニチュアまで作り直す広い Setup)。`3` は新しいフィールドだけを書く: 6 枠の `trainerPass`・`tricksEnabled`、`KD_CallPose.trainerPass`・`trickZoneCenters`・`trickZoneRadii`、ボタン 2 つ (既存の `KD_ShopSetupEditor.EnsureButton` で冪等に)。6j を将来流してもボタンが消えないよう、6j 側にも同じ作成を足すかは 1-B が判断して記録する。
- `4` の検査: 配線が null でない、ゾーン 12 個がすべて有限で、バーンの中心が頭より前 (根のローカルで鼻の向き)・ジャンプの中心が頭より上、半径 0.15 m (定数と一致)、ボタン 2 つが実在し押下イベントが `_Bang` / `_Jump`、`tricksEnabled` の現在値を表示。結果は `Temp/kh-tricks/verify.txt`。
- ゾーンの値 (犬ごとの頭の高さ・中心) を表にして Verify のログに出す (到達性の判断材料)。

### 4.5 1-C の Setup / Verify

- `KH_TrickGuideEditor.cs`、メニュー `Tools/KominkaHillside/Tricks/5 Setup Trick Guide` と `6 Verify Trick Guide`。GameObject 1 つ (同期 None の実 Udon)、マテリアル、`callPose` / `language` の配線、保存時は非表示、Collider なし。
- 文言 9 件 × 13 言語が `UITranslations.json` にあることを Verify で確かめる。翻訳表をシーンへ入れるメニューと ja/en の字形の焼き込み (Course/27) は統括が流す。
- 1-C は §4.2 の名前どおりに呼ぶ。1-C 単独でのコンパイル成立は求めず、1-B の API と統合した後に統括がコンパイルを確かめる (Codex 仕様レビュー 2026-10-13 で訂正)。

### 4.6 統括の順序

1. 3 波を並行で実装 → `bash scripts/unity-compile.sh --udon` (C# 0 / U# 0)。
2. `Tricks/1` → `1`・`2` → `2b`、`3` → `4`、`5` → `6`、文言の取り込みと Course/27。保存。
3. Codex 独立レビュー (`vrc-review`)。指摘の反映後、ハーネスを回すかを相談。

### 4.7 契約への追補 (2026-10-13 Codex 仕様レビューの反映。§4.2〜4.5 に優先する)

1. **合図の犬の添字 (1-B)**: 共通処理 `RequestTrick(int dog, int kind)` を置き、ボタンは `_dog`、合図は判定した犬の添字を渡す。合図はメニューを開いていなくても動く (`PageOpen` 不要、`_dog` が −1 でも可)。
2. **間隔中・再生中の手の履歴 (1-B)**: 状態 4・5 でも手の位置履歴・速度・ゾーン内外は毎フレーム更新する。止めるのは要求の発行だけ。状態 0 に戻った時に手が既にゾーン内なら、外へ出るまで受理しない。
3. **要求中の解除 (1-B)**: 送信前に旧 `stretchMs` を記録して要求中を立て、Owner での同期的な受理 (送った直後の変化) でも非 Owner の後着でも同じ判定 (stretchMs の変化 or 2 s) で解除する。状態 3 がジャンプ 2 s の再生 (状態 4) を隠さない。Commit / Release でリセット。
4. **`TrickTime` (1-B)**: 種類 0・長さ 0・履歴なしなら 0f。それ以外は float 除算して Clamp01。
5. **ボタンの状態 1 (1-B)**: 「呼んだ犬がいない / 未到着」は押せない + 既存 `call.unavailable` 系の理由文言 (伸びのボタンと同じ)。
6. **既存 Verify (1-B の必須編集)**: `KD_CallPoseEditor.cs` の Verify (:391-393 付近) は `hasStretch` を参照しているので `trickKind` へ置き換える。
7. **ガイドの消去と復帰 (1-C)**: `_TrickGuideDog()` が −1 でも表示中のものを必ず消して戻る。0 → 3 → 4 の経路では直前の対象犬をキャッシュし、同じ犬が 4 に入ったら受理演出 (`_TrickLastAccepted` の輪を 犬 × 開始 単位で 1 回光らせる) と「再生中」を出す。6 m 圏内への復帰・対象犬の変更は新しい表示開始 (フェードイン)。
8. **VR のボタン案内 (1-C)**: `call.trick.hint.menu` は VR でも補助行として出す (Desktop / Touch では主行)。文言は 9 件のまま、文を両用にする。状態 8 は 6 と同じく何も出さない。
9. **Animator の冪等 (1-A)**: 既存の CallBang / CallJump と遷移が正しければ保持し、不足・不一致だけ修復する (fileID を変えない)。`KD_CallPoseEditor.States` を伸ばさない根拠は「既存 7 State の生成・検査の責務を KD_CallPoseEditor に残すため」(AnimatorConfigured は States の名前だけ数えるので 8/9 を足しても壊れない)。Menu/6j の後は Tricks/2b で検査し、必要な時だけ Tricks/2 で修復。Tricks/2b は State が各 Controller にちょうど 1 つ・AnyState からの遷移 1 本・条件 CallReady && CallPose == 8/9・出口 3 本 (Run / Walk の移動先 2 本 + 既定 State 1 本) を合否にする (Codex 独立レビュー 2026-10-13 で「2 つ」を訂正)。
10. **用語**: 送り手の `SendRequest` は 7 引数 (内部で sentMs を足す)、ネットワークの `RequestSlot` が 8 引数。
11. `KD_DogWanderController.cs` の編集範囲は行番号でなく「`LateSleepSlot()` から `ApplyCompanionAnimation(...)` の呼び出しまで」のブロックで示す (別セッションの行ずれに備える)。

## 5. 検証

| 項目 | 確かめ方 | 担当 |
|---|---|---|
| コンパイル | C# 0 / U# 0 (`--udon`) | AI |
| クリップと State | `Tricks/2b` PASS (6 × 2、長さ、始終点 = 座り、CallBang / CallJump の遷移) | AI |
| 配線とゾーン | `Tricks/4` PASS、`Tricks/6` PASS | AI |
| 独立レビュー | Codex (`vrc-review`)、指摘を反映 | AI |
| ClientSim | **相談 (`vrc-review` 相談モード) の後で、決めるケースだけ**。全体は回さない | AI (相談で決めたもの) |
| 見え方・合図・届き方 | PCVR の Play | ユーザー |

DES-DOG-012 §次の手順 2 の検証条件 (写し)。AI = Editor の検査・プローブ・ClientSim で決まる見込み、ユーザー = PCVR / 2 人以上の Play が要る。

- [ ] 誤発火・保持連打・中断復帰・競合・非Owner操作・未加入者の偽装要求・途中参加・Owner退出・Store読込/失効・端末別負荷 (完了条件 4 の一覧) — 偽装要求・途中参加は AI (ClientSim、相談で選ぶ)、Owner 退出・Store・端末別負荷はユーザー
- [ ] 「指示した本人以外の画面でも同じ芸が同じ時刻に見える」 — ユーザー (2 人)
- [ ] 「未所持の送り手の偽装要求を `HandleRequest` が落とす」 — AI
- [ ] 「途中参加は窓の中なら途中から見え、過ぎていれば再生しない」 — AI (ClientSim の候補) + ユーザー
- [ ] 「ジャンプが着地まで再生される (正規化)」 — AI (`TrickTime` の計算と `Tricks/2b` のクリップ長) + ユーザー (見え方)
- [ ] 「同一世代の姿勢変更 (Pose) では 8 s の間隔を維持し、呼び直し・再配置 (Commit。現行の `_TryCallPlaced()` も Commit を通る) ではリセットする」 — AI (受信の行の静的確認、ClientSim の候補)。元の「再配置で回避できない」は設計の Commit (両方消す) と両立しないので訂正 (Codex 仕様レビュー 2026-10-13)
- [ ] 「撫で始め・撫で終わり・お手の前の移動・メニュー操作でバーン・ジャンプが出ない」 — ユーザー (PCVR、不受理ケース)
- [ ] 「左右の手の切替で速度が跳ねない」 — ユーザー (PCVR)。AI は左右別の履歴の静的確認
- [ ] 「再生中・8 s 以内の二重受理が無い」 — AI (受信の行) + ユーザー
- [ ] 「小型犬で立位・着座 VR からゾーンに届く」 — ユーザー (PCVR、ダックス・コーギー・ポメ)

ガイド (決定 7) の確認 — ユーザー (PCVR と Desktop):

- [ ] 犬が座ると 2 s でゾーンの輪・ラベル・案内が出て、犬・手・メニューを隠さない。夜でも読める。
- [ ] 受理で光って消える → 「再生中」→「あと N 秒」→ 再び使える、の流れが分かる。
- [ ] 立たせる・解除・6 m 離れると消える。未所持の人・他人には何も見えない。
- [ ] Desktop・Touch は 1 行の案内でメニューのボタンへたどれる。

## 6. 公開時の手順 (決定 6)

**0. (独立した必須手順、公開許可と無関係)** 初期公開のビルドの前に `tricksEnabled` を OFF にして Tricks/4 Verify で確かめる (§2.10)。

以下はユーザーが「出す」と決めた時に行う。それまでは行わない。

1. `tricksEnabled` を ON にしたビルド (§2.10)。
2. Listing の説明: トレーナーは古民家と同じ Listing なので、特典に「バーン・ジャンプ (裏山だけ)」を足す文面をユーザーに確認する。ワールド内の説明文 (`information.trainer.body`・`activities.hillside.trainerIntro` など、トレーナーの特典を列挙している既存の文言) も同じ時に直す。実装の時点では変えない。
3. 看板 A 4 枚目と DES-UI-005 の特典の棚卸しへ「バーン・ジャンプ」を足す。
4. 古民家 repo に別 Issue (古民家側の案内・Listing の説明の更新)。この repo の外。起票は公開時に統括が行う。
5. [game-design](../../game-design.md) §4 と [world-ui-spec](../../world-ui-spec.md) の CALL 行へ 1 行ずつ反映。§3・§4 の大きな書き直しは別セッションの区切り待ち。

## 7. 完了条件

- [x] §2 の同期・受信・権利・getter・Animator の対応・合図・ボタンが実装されている (2026-10-13、1-B。`KD_SleepSlot` / `KD_CallPose` + `KD_CallPose.Tricks.cs` / `KD_DogWanderController` の 1 ブロック / `KH_TrickSetupEditor`)
- [x] クリップ 2 種 × 6 犬種と CallBang / CallJump が Controller 6 つにある (`Tricks/2b` TRICKS_CLIPS_OK 325。2 回目の `Tricks/2` は 6 つとも unchanged。接地の最低 −0.0013 m (許容 −0.015)、ジャンプ頂点の離れ 0.254〜0.267 m。証拠画像 `docs/plans/evidence/2026-10-13-tricks/` 12 枚)
- [x] `KH_TrickGuide` の配線・材質・文言 (`Tricks/6` TRICKGUIDE_OK 71)。出る・消える振る舞いはユーザーの PCVR / Desktop
- [x] 文言 9 件 × 13 言語 (AI 作成、校正なし)、ja/en の字形 (`Tricks/5a`、Menu/22 Verify Localization PASS failures=0)
- [x] C# 0 / U# 0、`Tricks/4` PASS (Menu/6b Verify Sleep System PASS 573 も)
- [x] Codex 独立レビューの指摘を反映 (2026-10-13: 高 0・中 2・低 2。ボタンの状態 1〜3 の表示と理由、ジャンプの高さの読みを §2.2 に記録、§4.7 の出口 3 本、DES-DOG-012 の検証条件)
- [x] ハーネスは相談で決めたケースだけ通る (2026-10-13 Codex 相談の結論: **回さない**。Edit の Verify と静的確認で判別でき、伸び・お手・あご乗せの直接の回帰群は Testing 配下に無く、近接の Course/97f・78 では置換の主要条件を検査できない。合図・不受理・到達性は PCVR、実通信・途中参加・Owner 交代は複数人に残す)
- [ ] §5 のユーザー確認の項目が実機チェックリストにある (未: 初期公開後の追加なので、公開時 (§6) にチェックリストへ足す)
- [ ] §2.10 のスイッチの保存値をユーザーに確認した
- [ ] 公開 (§6) はユーザーが出すと決めた後。この Issue の完了には含めない (公開の作業は §6 の手順で別に記録する)

## 記録

2026-10-13 (統括 Fable、実装 Opus 3 波): 実装・取り込み・検査まで完了。**1-A** `KH_TrickClipEditor` (+ States / Verify) で `KH_Bang_<犬>` 6.0 s / `KH_TrickJump_<犬>` 2.0 s を 6 犬種分生成 (座り → 横倒し → へそ天 3 s → 座り / 座り → しゃがみ → 空中 (接地姿勢 + 0.25 m) → 着地 → 座り。床パスで接地キーを足し、最低点 −0.0013 m 以上、頂点の離れ 0.254〜0.267 m)、Controller 6 つへ CallBang (8) / CallJump (9) を修復型の冪等で追加 (2 回目は unchanged)。定数カーブを 2 キーに縮約して 12 本 56 MB → 25 MB。証拠 `docs/plans/evidence/2026-10-13-tricks/` 12 枚。**1-B** `KD_SleepSlot` (`hasStretch` → `trickKind`、種類別の長さ、op 10/11、受信の行、`trainerPass` / `tricksEnabled`、要求中)、`KD_CallPose.Tricks.cs` (左右別の速度ベクトル、ゾーンへの外 → 内で 1 回受理、`_TrickStatus` ほか §4.2 の API、ボタン 2 つ = ポーズ面 3 段目 y −270)、`KD_DogWanderController` の 1 ブロック (7/8/9)、`KD_CallPoseEditor` の Verify 2 行、`KH_TrickSetupEditor` (Tricks/3・3b・3c・4)。**1-C** `KH_TrickGuide` (同期 None、輪 2 つ + 芸名 + 合図の案内 + メニュー案内、queue 5000/5001、0.15 s の状態判定、6 m)、`KH_TrickGuideEditor` (Tricks/5・5a・6)、文言 9 件 × 13 言語 (AI 作成・校正なし)。検証: C# 0 / U# 0 (3 回)、Tricks/1 → 2 → 2b OK 337、3 → 4 PASS、5 → 5a → 6 OK 71、Menu/6b PASS 573、Menu/22 PASS、保存。Codex 仕様レビュー (実装前、§4.7) と独立レビュー (実装後、高 0・中 2・低 2 を反映)。ハーネスは相談の結論で回していない。設定のトグル (`KH_TrickGuide.hidden.v1`) は別件 (設定タブの空きを Unity で見てから)。ビルドサイズ計測 (Build Size/7 → 8 → 2 Android → 5 Group Cost): Android 圧縮 71.26 MB / 非圧縮 206.23 MB (10-03 の 50.70 / 130.49 から +20.6 / +75.7。この 10 日間の他セッションの追加分を含む)。芸のクリップ 12 本は `kh-animclips` 群 (24 ファイル) に入り、群の合計が 572 KB (圧縮) なので **芸の追加分は 0.6 MB 未満**。増分の主因は `kh-fonts` 21.59 MB・`kh-textures` 19.34 MB・`dogpreview-meshes` 7.24 MB (別件候補、[build-size-audit](../../plans/2026-09-24-build-size-audit.md) 2026-10-13 の節)。7 Apply が他セッションの素材 (WashPremium / PropFinish / Guide の .meta 10 件) にモバイル上限を掛けたので戻した (8 Verify は適用後 81 枚 ok。未適用のままの素材は各担当の判断)。未確認: ユーザーの PCVR / Desktop (§5 のユーザー項目)、2 人、端末別。

2026-10-13: Codex 仕様レビュー (gpt-6-astra、観点 7): 基本仕様の写しは DES 第 2 版・決定 6 件・モードの読みと一致。高 3 (合図の犬の添字 / 間隔中の手の履歴 / 決定 6 は機能提供も初期公開後) ・中 9・低 3 を §1・§2.10・§4.5・§4.7・§5・§6 に反映し、実装中の 3 波へ送った。

- 2026-10-13: 起票 (DES-DOG-012 の 1〜6 と追加の要件 7 の決定を受けて)。§4 の契約で 3 波 (1-A クリップと Animator・1-B Runtime とボタン・1-C ガイドと文言) に分ける。依頼文からの変更: ボタンは 1-B (ポーズ面は KD_CallPose / KD_CallPoseEditor のもの)、文言は DES の 7 件 + 統括の追加 2 件 (ボタンの添え字・ガイドの Desktop 行)、提供前のスイッチ `tricksEnabled` を追加 (統括の判断、ユーザー確認待ち)。コード・シーンは未変更。
