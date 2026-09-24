---
id: IMP-UI-001
type: IMP
system: UI
status: todo
priority: High
---

# IMP-UI-001 手元メニューを古民家の器 (KD_TreatPouch + ビルダー) に置き換え、設定面を作る

2026-09-23 起票、同日に範囲を書き換え。[DES-UI-003](DES-UI-003-menu-scope-and-polish.md) で **タブ構成は案 A**、ヘッダーは [残高] [設定] [閉じる]、ナイトモードは設定面のスライダーへ、距離フェードは PC のみで採用、と決まった。その後の実装調査で、**古民家の `KD_TreatPouch` 一式 (7 タブ・確認カード・設定面・試着プレビュー・言語) は裏山へ移植済みでコンパイルが通っている**一方、**生成側の Editor ビルダーが 1 本も無い**ことが分かった。当初方針の「`KH_HandMenuEditor` を表駆動に直して頁を足す」は撤回し、**古民家の器とビルダーをそのまま持ち込み、`KH_HandMenu` を退役させる**。順序・分担・移植対象・ビルド手順の対応表は [計画書 2026-09-23](../../plans/2026-09-23-menu-rebuild.md) が正本。

本Issueの範囲は計画書の **段階 0-a (移植の土台) / 0-b (器の構築。設定面の器 + ナイトモードのスライダーを含む) / 段階 3 (設定面の残りとようこそ)**。段階 1 ([IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md)) と段階 2 ([IMP-DOG-014](IMP-DOG-014-treats-port.md) / [IMP-DOG-015](IMP-DOG-015-accessories-and-petfx-port.md) / [IMP-DOG-016](IMP-DOG-016-call-pose-port-and-slope.md)) はその間に入る。本Issueは **UI の器と頁**だけを担当し、各タブの中身は各 Issue が持つ。

## 範囲

### 段階 0-a — 移植の土台

- **古民家の `Assets/KominkaDogRun/Editor/` を丸ごと** `Assets/KominkaHillside/Imported/KominkaDogRun/Editor/` へコピーする (使う Editor だけを選ぶと型参照の閉包が切れる。`KD_TreatSetupEditor.Pouch.cs` ほかが 30 種の Editor を参照する)。ファイル名 `KD_` と namespace は維持。
- **パス定数の置換一覧を作る**: `grep "Assets/KominkaDogRun"` の結果を [kominka-dog-system](../imports/kominka-dog-system.md) に表で記録する。Script / Program Asset は `Assets/KominkaHillside/Imported/KominkaDogRun/…`、Font / Textures / Data / Models / Materials は `Assets/KominkaHillside/…`。
- MenuItem を `Tools/KominkaHillside/Menu/…` へ付け替える。**未採用機能 (写真・犬の声・ギフトコード・ライブサムネ・ラジオ・寝室) の MenuItem は `Tools/KominkaHillside/Menu/Imported (未使用)/…` に隔離し、Build 手順から呼ばない** (コンパイル対象には残す)。
- この段階の出口は**コンパイル 0 と Console エラー 0**。シーンはまだ触らない。

### 段階 0-b — 器の構築

- スプライトを生成する (`KD_UiRound.png` / `KD_UiCircle.png` を古民家と同じ定数で焼き直す。色は Image.color の乗算 1 本)。
- **フォントを 48 pt へ再ベイクする** (現在 779 字 / 容量 784 で満杯。段階 0-b の最初に済ませる)。Charset は `PouchWords()` 方式へ一本化し、`KH_HandMenuEditor.Charset` は退役。手順は `vrc-tmp-font`。
- ローカライズ経路を通す (`UITranslations.json` / `UILocalizationBindings.json` を `Assets/KominkaHillside/Data/` へ置き、`KD_LocalizationEditor` で `KD_Language` の配列を生成、ja/en を焼く。`KD_LocalizationBuildGuard` を有効化)。
- `KD_TreatPouch` をシーンへ配置し、**案 A の 7 タブの器**を作る。タブ ID は古民家のまま PetFx2 → **Walk2 (さんぽ)** に改名。`Order = {2,0,5,3,1,6,4}`、**`Widths` の和 1064 (間隔 6×6 を加えて 1100)**、既定タブ = さんぽ。
- **段階フラグ** (`KD_TreatPouch.Hillside.cs` の `hillsideStage` 相当) を入れ、未導入のバックエンド (ledger / box / accessories / sleepBoard / live …) が null でも `LogError` を出さず「準備中」1 行を描くようにする。Verify も同じ段階表で「意図的な未導入」と「本来必要な参照の欠落」を分ける (計画書 D-1 の表)。
- **設定面の器とナイトモードのスライダー** (`KH_NightMode` 連動) をここで作る。KH_HandMenu 退役でナイトモードの入口が無くなるため、段階 3 まで待たない。
- Page_Acc の Content に `AccRow{0..15}` の後ろへ `FxRow{0..10}` を生成する (撫でFX の かざり合流。描画・購入は IMP-DOG-015。計画書 D-2)。
- 既存 3 機能を `KH_HandMenu` から器へ移す: さんぽ (`KD_TreatPouch.Walk.cs` を新設し AttachGate / Pending / GateText を移す → `KH_LeashManager`)、おもちゃ (`.Toy` の裏山改変で行に [ボール][ディスク] → `KH_ToyRecall`)、あそぶ (`KD_ActivitiesMenu` 裏山改変で集合のみ → `KD_GroupActivities` / `KH_TrainerPass`)。
- **`KH_HandMenu` / `KH_HandMenuEditor` / 旧 Panel を退役**させる。拠点の「メニューをひらく」板 3 枚 (`KH_MenuOpener`) は残し、呼び先を `pouch._Toggle` へ付け替える。
- 開閉値を古民家へ戻す (VR 右スティック下 **0.5 秒・しきい値 0.8**、Desktop `T`、Touch は Overlay ランチャー)。レイヤー 17 の当たり設定を `KD_HandMenuCollisionEditor` で適用・検査する。
- おやつ・かざり・呼ぶは行に「準備中」、ライブは準備中 1 行 ([DES-LIV-001](DES-LIV-001-hillside-live.md) の採用まで)。
- **ヘッダーの段構成は新しい判断を要しない**: 古民家はヘッダ (Y314) とタブ帯 (Y224) を**もともと別の段**に置いており、モックで懸念した「7 タブ + ヘッダ操作が 1 段に入らない」は実配置では起きない。金の下線もタブ幅のままで入る。
- 全売り場で共通の購入確認カード 1 枚 (暗幕 1140×700 + カード 720×300) を器として用意する。残高枠も枠だけ用意し、値は IMP-PRG-001 が供給する。
- 状態表示の規律 (未復元 / 実行可能 / 資源不足 / 権限・占有 / 処理待ち / 失敗) を古民家の「隠す vs 暗くする」の使い分けごと継承する ([world-ui-spec](../../world-ui-spec.md))。
- **実装時の判断 (2026-09-23)**: 設定ホームの 音量 / 距離フェード / インフォメーション / 写真 の入口は 0-b では**非表示**にした (押しても何も起きないボタンを出さないため。段階 3 で戻す)。インフォメーション・クレジット・ギャラリー・写真の器も 0-b では作らない。ラジオ設定頁は作らない。残高 (ヘッダの Balance) は ledger が入る段階 1 まで隠す。
- **退役で失ったもの (2026-09-23)**: `KH_HandMenu` のヘッダにあった [試作: …] (`KH_FrisbeeCatch._DevVariant`) の入口が無くなった。[DES-UI-002](DES-UI-002-frisbee-settings.md) は「6 匹展開の間は残す」としているので、代わりの入口 (設定ホームの [Dev] 行を段階 3 から前倒しする / `KH_FrisbeePrototypeEditor` の Editor 切替にする) を**ユーザーが判断する**。

### 段階 3 — 設定面の残りとようこそ

- 設定面 (タブではなく上段の別面、`KD_MenuSettings` の裏山改変。寝室・ラジオ・解像度は外す)。**器とナイトモードは段階 0-b で作るので、ここは残りの項目**。**2026-09-24 実装済み**: 設定ホームは 3 群 (自分の見え方・聞こえ方 / この部屋のみんな / 読む)。配置の正本は新規 `KD_MenuFinishEditor.Hillside.cs`。写真設定・ギフトコード・[Dev] は作らない。
- **距離フェード**: `Imported/KominkaDogRun/Scripts/KD_DistanceFadeSettings.cs` をそのまま配線する。4 プリセット + 4 入力欄 + [確定して保存]、PlayerData の 1 文字列、本人のみ・同期なし。Android / iOS は入口ボタンだけ閉じ、文言「距離フェード (PCのみ)」は全端末で出す。**2026-09-24 実装済み**: 保存キーを `KH_DistanceFade_v1` に改名 (古民家の値は引き継がない)、Android / iOS は Start で入口を押せなくする。Menu/14 / 14c (系は `KH_Dogs` 直下)。
- **ナイトモード**: ヘッダーの OFF/ON (暫定) を廃し、古民家と同じスライダー (夜の明るさ 0〜100%、PlayerData に保存「自分だけに反映・次回も復元」) にする。`KH_NightMode` の強度を連続値にし、設定面から受ける口を足す。**段階 0-b で実装済み**。段階 3 では配置を 3 群へ動かしただけ。
- **許可トグル 2 種は初期 ON** (呼ぶを全員に許可 / ライブ開催を全員に許可)。権限者は**インスタンスオーナー、不在時は Master** (`KD_SleepBoard.CanControl` の判定を継承)。ライブの実体は DES-LIV-001 後。**実装済み** (段階 0-a の版合わせで既定 ON、段階 3 で「この部屋のみんな」の群へ配置)。
- 言語 (`KD_Language`。ja/en を焼く)、クレジット、インフォメーション (案内)、音量 (中身は [DES-WLD-002](DES-WLD-002-sound.md)。当面 犬の声のみ)。**2026-09-24 実装済み**:
  - クレジット頁 `Page_Settings/CreditPage` 7 行 (犬モデル / 舞台 2 / 空 / おじいさん / フォント / OFL 表示)。**URL 3 件 (LMArt / Allsky / polyperfect) は未記入**で、Verify 22 は `CREDIT_URL_PENDING 3` と出す。ユーザーが `KD_MenuFinishEditor.HillsideCredits` に書く。
  - インフォメーション 4 頁 (はじめに / 生成AI / VAnimal / トレーナー。モックの順)。VAnimal とトレーナーの本文は裏山向けに差し替えた。
  - 音量頁は「外」の 1 群で犬の声のスライダーだけ (Menu/16 / 16a)。これで**今まで無音だった犬の声が 50% で鳴るようになる**。
- **ようこそ** (初回のみ、`KD_WelcomeGuideEditor` / `KD_HintDirector`、`KD_Onboarding_v1`、端末別フッタ)。**2026-09-24 実装済み**: 本文を全文へ差し替え (群「なにも持っていなくても」と「この板でできること」)、フッタに 2 行目「もう一度読むときは 設定 → インフォメーション → はじめに」を戻した。`KD_SleepSlot` 6 枠がそろったので `sleepNoticeEnabled = true`。
- 初回付与を受け取っていない間だけ、かざり・おやつの購入面に「入口のおじいさんから支度ポイントを受け取れます」を出す (受取済みなら出さない)。値は IMP-PRG-001。**実装済み** (段階 1 で準備中の行、段階 2 で おやつは状態行・かざりは板の下端へ)。
- 開発用 [Dev] の扱いは [DES-UI-002](DES-UI-002-frisbee-settings.md) の撤去時期に従う。→ 作らない (フリスビーの切替は 2026-09-24 に あそぶタブへ移設済み)。

## やらないこと

各タブのバックエンド (台帳・おじいさん = IMP-PRG-001、おやつ = IMP-DOG-014、かざり・撫でFX = IMP-DOG-015、呼ぶ・ポーズ = IMP-DOG-016)、ライブの中身と演目 (DES-LIV-001)、音源そのもの、おもちゃの柄違いの設計 ([DES-DOG-002](DES-DOG-002-toy-behaviour.md))、Quest / iOS の合格判定。

## 進捗

- 2026-09-23: **段階 0-a 完了** (`d87407f`)。
- 2026-09-23: **段階 0-b 実装済み、Verify 全 PASS、Codex D+M 済み、コミット a19112a**。`KH_HandMenu` は退役した。**2026-09-24: PCVR でユーザーが「現行 3 機能の見え方が古民家と同じ」を OK** (段階 0-b のゲート通過)。ClientSim は未実施。記録と PCVR チェックリストは [計画書の実装記録](../../plans/2026-09-23-menu-rebuild.md)、ファイル単位の差分は [kominka-dog-system](../imports/kominka-dog-system.md)。
- 2026-09-24: **段階 3 実装済み・Verify 全 PASS** (14c / 16a / 22 / 11c / 15a / 20 / 21。C# 0 / U# 82、シーン保存済み)。Codex D+M+U は段階 2 と合わせて実行中、コミット・PCVR は未。記録は [計画書「実装記録 / 段階 3」](../../plans/2026-09-23-menu-rebuild.md#段階-3-2026-09-24)、差分は [kominka-dog-system「段階 3」](../imports/kominka-dog-system.md#2026-09-24-設定面の残りとようこそ-段階-3)、確認手順は [実機チェックリスト §4](../../plans/2026-09-23-menu-rebuild.md#4-段階-3-設定面の残りようこそ2026-09-24-追加)。ユーザーの作業: クレジットの URL 3 件を書く。
- [試作] の代替は 2026-09-24 に あそぶタブのトレーナー群の行「フリスビーの取り方」として実装した (DES-UI-002 のユーザー決定)。

## 完了条件

- [x] **段階 0-a**: コンパイル 0 件・Console エラー 0 (フィルタ無し)、パス置換一覧が [kominka-dog-system](../imports/kominka-dog-system.md) に表で残り、未採用機能の MenuItem が隔離されている (2026-09-23、`d87407f`)
- [x] **段階 0-b の Verify** ([計画書の Verify 表](../../plans/2026-09-23-menu-rebuild.md)の「導入段階 0-b」分が全 PASS): `Sum(Widths) == 1064` かつ間隔込みで 1100 / 既定タブ `_tab == TabWalk` / Page_Walk の `WalkRow0..5` (起票時の仮名 `Dog{i}`) の 3 ボタンが `_WalkLeft{i}` `_WalkRight{i}` `_WalkOff{i}`、行き先 2 行 (`WalkRow6` / `WalkRow7`) が `_WalkPlaza` `_WalkGate` / `ToyRow{i}` の 2 ボタンが `_Toy{i}` `_Disc{i}` で `recall` (KH_ToyRecall) が非 null / Page_Acc Content の子順が `AccRow0..15`, `FxRow0..10` / 拠点板 3 枚の Interact 先が `_Toggle` / `KH_HandMenu` コンポーネント 0 個 / 同期変数 0 / ナイトモードの Slider が `KH_NightMode` / 欠字 0 / あそぶが集合のみ — **2026-09-23 PASS**: Menu/20 `VERIFY RESULT PASS`、21 pass 394 / fail 0、22 failures 0、23 failures 0、24 PASS、15a failures 0。コンパイル error CS 0、U# 82 本 0 エラー
- [x] 段階表 (計画書 D-1) により「意図的な未導入 (null が正)」と「参照の欠落 (FAIL)」が区別され、Live 行の検査は skip が明示されている (棚・寝台も同様) — 2026-09-23、Menu/20 が段階表で PASS + 理由を出力し、ライブ行の onClick・棚・寝台・ポーズ面・ミニチュア・おやつの枠を SKIP と明示
- [ ] 既存 3 機能に退行がない (`Walking/5 Verify` が従来どおり全 PASS)。**連打・タブ変更・閉じて再表示・拒否・4 秒超過**で結果表示が取り違わらない — 2026-09-23: `Walking/5 Verify` は TOTAL 208 / FAILED 0。取り違えの確認は ClientSim・実機とも**未** (計画書の PCVR チェックリスト §0)
- [x] **PCVR でユーザーが「現行 3 機能の見え方が古民家と同じ」ことを確認した** (段階 0-b のゲート。48 pt の可読性と、開閉が 0.6 秒 → 0.5 秒に変わった体感を含む) — **2026-09-24 ユーザー OK** (総合評価。項目別の記録は無し)
- [x] **段階 3 の Verify**: 2026-09-24 PASS。Menu/14c Verify Distance Fade、16a Verify Dog Voice、22 Verify Localization (3 群・クレジット 7 行・インフォメーション 4 行。`CREDIT_URL_PENDING 3`)、11c Verify Welcome (フッタ 1060×60・本文は表示枠 276 の 2 倍以内)、15a、20、21 (404/0)。C# 0 / U# 82
- [ ] 距離フェードが PC で反映され、保存して再入室で復元する。Android / iOS は入口が押せず文言だけ見える (T-011) — **実装済み・確認待ち**
- [ ] ナイトモードが設定面のスライダーで連続的に効き、保存値が再入室で復元される (ヘッダーからの移設で退行しない) — **実装済み・確認待ち** (0-b の PCVR 総合評価は OK。段階 3 で配置を動かした後は未確認)
- [ ] 許可トグル 2 種が初期 ON で、権限者以外は操作できない — **実装済み・確認待ち**
- [ ] 状態表示の規律がモック (DES-UI-003 版 3) と一致し、差分を記録した — **実装済み・確認待ち** (差分の記録は PCVR の後)
- [ ] **Quest / iOS の合格判定は [VER-WLD-001](VER-WLD-001-quest-ios-devices.md)** (計画書の段階 4)

## 関連

- [計画書 2026-09-23](../../plans/2026-09-23-menu-rebuild.md): 段階・ビルド手順の対応表・Verify 追加項目・リスクの正本
- [DES-UI-003](DES-UI-003-menu-scope-and-polish.md): 案 A のタブ構成・設定面の項目・状態表示の規律を決めた大元
- [DES-UI-001](archive/DES-UI-001-menu-and-leash-assist.md): 現行 3 タブの実装と PCVR 確認 (done。退行させない基準)
- [world-ui-spec](../../world-ui-spec.md): 画面・入力・状態・Unity 部品の正本。T-011 (距離フェード) はここ
- [kominka-dog-system](../imports/kominka-dog-system.md): ビルダー移植の元 SHA・範囲・差分の記録先
- [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md): 残高・未受取の案内の値を供給する
