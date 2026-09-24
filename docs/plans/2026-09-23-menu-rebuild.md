# 手元メニューを古民家と同一の器へ作り替える実装計画

状態: **段階 0-a・0-b 完了** (2026-09-23 実装・Verify 全 PASS・Codex D+M・コミット a19112a、2026-09-24 に PCVR でユーザー OK)。次は段階 1 (IMP-PRG-001)。記録は下の「実装記録」、確認手順は「実機チェックリスト」。設計は 2026-09-23 のユーザー指示「モック OK。古民家の実際のメニューの配置・コンポーネントを確認して設計」に基づく。採否と全体構成の正本は [game-design §4](../game-design.md) と [DES-UI-003](../development/issues/DES-UI-003-menu-scope-and-polish.md)、画面・入力・状態・Unity 部品の正本は [world-ui-spec §4〜6・§8](../world-ui-spec.md)。見え方の合意はモック 版 3 (<https://claude.ai/artifact/K1moWuWCeWMUm7wUprZx3f>、ユーザー「OK」)。

この計画書は**順序と分担**だけを持つ。何を作るかは各 Issue が持つ。

## 古民家の器の事実 (2026-09-23 読取調査)

出典は古民家ドッグランの**作業ツリー 1cb8d61** (タグ `export/kominka-hillside-v1` = 44505384 の子孫)。行番号はこのツリーに対するもので、以後の古民家側の変更で動く。

**最重要: 手元メニューにプレハブは 1 つも無い。すべて Editor コードが生成する。**

| 事実 | 値 | 出典 |
|---|---|---|
| ビルダーは 2 段 | `.Pouch.cs` が器と配線、`KD_HandMenuScrollEditor` が座標・寸法を上書き (Pouch の tab 120×56 @Y160 は旧値で Scroll 側が勝つ) | `KominkaDogRun: Assets/KominkaDogRun/Editor/KD_TreatSetupEditor.Pouch.cs` / `Editor/KD_HandMenuScrollEditor.cs` |
| Canvas | WorldSpace、worldCamera null、sortingOrder 未設定、CanvasScaler は既定値、GraphicRaycaster + VRCUiShape、panel は layer 17 / root は layer 0 | `KominkaDogRun: Assets/KominkaDogRun/Editor/KD_TreatSetupEditor.Pouch.cs:407-420` |
| 板 | sizeDelta 1140×700、localScale 0.001 (1 mm/px)、板の位置は PouchPanelOffset (0.22,0.30,0.32) | `KominkaDogRun: Assets/KominkaDogRun/Editor/KD_TreatSetupEditor.Pouch.cs:353-356,423-425` / `KD_TreatSetupEditor.cs:81` |
| 配置 | 手追従なし。`PlaceInFront` が開いた瞬間 1 回だけ head の前方 0.95 m・高さ −0.05 へ置く。スムージングなし。60 フレームごとに眼高/1.6 でスケール | `KominkaDogRun: Assets/KominkaDogRun/Scripts/KD_TreatPouch.cs:1354-1377,1389-1415` |
| 開閉 | VR 右スティック下 (符号 −1・しきい値 0.8・0.5 秒) / Desktop `T` / Touch は Overlay ランチャー (sortingOrder 100、入力方式 Touch のときだけ表示。**横画面は上端中央、縦画面は右中央**)。Interact・Pickup 無し | `KominkaDogRun: Assets/KominkaDogRun/Scripts/KD_TreatPouch.cs:996-1013` / `Editor/KD_TreatSetupEditor.Pouch.cs:1261-1295` |
| スプライト | `1 - Bake UI Sprite` が毎回焼く。KD_UiRound 96×96 半径 12 border 30 (Sliced)、KD_UiCircle 96×96 半径 48 (Simple)。RingLevel 1.00 / FaceLevel 0.55 / PPU 100。色は Image.color の乗算 1 本 | `KominkaDogRun: Assets/KominkaDogRun/Editor/KD_UiSpriteEditor.cs:80-111,150` |
| フォント | `Assets/KominkaDogRun/Font/KD_NotoSerif SDF.asset` (Static 固定)。実効 26/24/22/20/18 pt。追加口は `AddGlyphs` 1 本 | `KominkaDogRun: Assets/KominkaDogRun/Editor/KD_ShopSetupEditor.Ui.cs:21` |
| 一覧 | GridLayoutGroup cell 508×112、spacing 12、padding 8、FixedColumnCount **2 列**、表示 2 列×3 段 = 6 | `KominkaDogRun: Assets/KominkaDogRun/Editor/KD_HandMenuScrollEditor.cs:252-254,373` |
| ScrollRect | 縦のみ Clamped、inertia false、scrollSensitivity 35、初期 verticalNormalizedPosition 1。Scrollbar x=530 幅 40 **常設**、Handle sizeDelta 0。Viewport 1044 幅 + RectMask2D。VR はレーザーでドラッグ (IDragHandler 0 件) | `KominkaDogRun: Assets/KominkaDogRun/Editor/KD_HandMenuScrollEditor.cs:245-256,323-337` |
| ヘッダ・タブ | ヘッダ Y=314 (高さ 44)、帯 Y=270、タブ Y=224 (高さ 46・22 pt・**幅の和 1064 + 間隔 6×6 = 1100**)。**ヘッダとタブは別の段** | `KominkaDogRun: Assets/KominkaDogRun/Editor/KD_HandMenuScrollEditor.cs:19-23,216-228` |
| 行プール | `Prefixes[id]+index`。おやつ Row 4 / おもちゃ ToyRow 6 / 撫でFX FxRow 上限 11 / 呼ぶ SleepRow 6 / ライブ LiveRow 9 / かざり AccRow 16。あそぶ (6) だけ ScrollRect 無しの固定配置 | `KominkaDogRun: Assets/KominkaDogRun/Editor/KD_HandMenuScrollEditor.cs:20` |
| 確認カード | 共有 1 枚。暗幕 1140×700 (自体が `_ConfirmNo`)、カード 720×300 z=−15、[やめる] 136×48 / [購入] 150×48 | `KominkaDogRun: Assets/KominkaDogRun/Editor/KD_TreatSetupEditor.Pouch.cs:1090-1167` |
| 再描画 | `_Refresh()` は **板が非表示でも要求監視 (`PollLiveRequest`) と再予約を続ける** (refreshSeconds **0.5**、`SendCustomEventDelayedSeconds` で自己再予約)。**描画を打ち切るのは `Redraw()`** (panelRoot 非表示なら即 return)。あそぶだけ `KD_ActivitiesMenu.Update` の **0.25 秒** | `KominkaDogRun: Assets/KominkaDogRun/Scripts/KD_TreatPouch.Redraw.cs` |
| 同期 | 板の同期変数は **0 本** (SyncMode None)。Verify が守る | `KominkaDogRun: Assets/KominkaDogRun/Editor/KD_TreatSetupEditor.Verify.cs:339` |
| 配線 | 唯一の口 `EnsureButton(...)` が `AddStringPersistentListener(onClick, SendCustomEvent, "_Xxx")` を毎回張り直す。対象は backing UdonBehaviour | `KominkaDogRun: Assets/KominkaDogRun/Editor/KD_ShopSetupEditor.Ui.cs:513-516` |
| ローカライズ | 正本 `Data/UITranslations.json` (schemaVersion 2、549 キー×13 言語) + `UILocalizationBindings.json` → `KD_LocalizationEditor.Configure()` が `KD_Language` の配列へ書く。実行時は `L(s) => language._Text(s)`。生成 C# const は無い。ja/en だけ焼く | `KominkaDogRun: Assets/KominkaDogRun/Editor/KD_LocalizationEditor.cs` / `Scripts/KD_TreatPouch.cs:920` |
| Verify の主張 | `Treat System/3` = Program 資産・SyncMode None と同期変数 0・root が layer 0 で Collider 無し・開閉パラメータ・板 1140×700・タブ配列長 7 で active 1 枚・スプライト種別・確認カード共有 1 枚・フォント Static かつ欠字 0・onClick 二重登録なし。`Hand Menu/4` = タブが重ならず板内・文字が幅に収まる・マスクと常設バー・初期タブ・2 列 6 項目・行数が定義と一致・onClick 1 本が `_Row{i}` 等と一致・隙間 ≥ 6 px | `KominkaDogRun: Assets/KominkaDogRun/Editor/KD_TreatSetupEditor.Verify.cs:15,339,603` / `Editor/KD_HandMenuScrollEditor.cs:345` |

## 裏山の現状と差

- `KH_HandMenu.cs` (495 行) + `KH_HandMenuEditor.cs` (447 行) は**座標ベタ書きの別実装**。タブ 3・ページ 3 固定、`Tabs`/`DogNames`/`Formations`/`Word*` は Editor の定数、ボタン 48 本を `ExpectedEvent()` の手動表で検査する。
- 一方で `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/` には **KD_TreatPouch.cs + 9 partial (計 3,969 行) が移植済みでコンパイルが通っている**。7 タブ・確認カード・設定面・試着プレビュー・言語の口はすべてコード上そろっている。未存在型への参照はコメント・Tooltip 内だけ。
- 足りないもの: **Editor ビルダーが 1 本も無い** (Imported 側に Editor ディレクトリが無い) / スプライト生成器が無い (KD_UiRound.png は手置き、KD_UiCircle.png は未使用) / フォントが 779 字 / 容量 784 で満杯 / 台帳・おやつ・かざり・呼ぶ・ライブの**実体がシーンに 1 つも無い** / ミニチュア (DogPreview) と飾り実体が無い / 確認カードと設定面が無い (ナイトモードはヘッダの OFF/ON 暫定、距離フェードは未配線) / `KD_Language` のテーブル生成 Editor が無く裏山は日本語直書き。
- 裏山だけの物: 拠点の「メニューをひらく」板 3 枚 (`KH_MenuOpener`、Interact → `menu._Toggle()`)、`KH_NightMode`、`KH_LeashManager` / `KH_ToyRecall` / `KH_TrainerPass` / `KD_GroupActivities` (裏山改変)。

## 設計判断

### D-1 メニュー本体は古民家の KD_TreatPouch を採用し、KH_HandMenu は退役させる

- **これは [DES-UI-003](../development/issues/DES-UI-003-menu-scope-and-polish.md)「開発側の実装方針」に書いた「KD_TreatPouch は丸ごと入れない / KH_HandMenu に頁を足す」方針の撤回である。** 当時は KD_TreatPouch が裏山で自足してコンパイルが通るかを確認していなかった。上の事実確認でそれが確かめられたため反転する。
- 理由: ユーザーの要求は「配置・文言・動線・見え方を古民家と全部同じ」。同じ runtime を使うのが最短で最も正確。KH_HandMenu は座標ベタ書きなので、頁を足すほど古民家から離れ、同じ修正を繰り返す。
- KH_HandMenu の裏山固有 3 機能は pouch 側へ移す: さんぽ (`KH_LeashManager` の接続/解除/行き先)、あそぶ (`KD_GroupActivities` 裏山改変 + `KH_TrainerPass`・隊形マーカー)、おもちゃ (`KH_ToyRecall` のボール/ディスク)。
- `KH_MenuOpener` (拠点板 3 枚) は残し、呼び先を pouch の `_Toggle` へ付け替える。`KH_NightMode` は残し、設定面のスライダーから 0〜100% を受ける口を足す。
- **段階 0 の構築入口と検査を別に定義する**。`KD_TreatPouch.Hillside.cs` に段階フラグ (例 `hillsideStage`) を持ち、段階 0 では box / ledger / catalog / sleepBoard / live / petFxBox / accessories が null でも `LogError` を出さず、該当タブは「準備中」1 行を描く (Start のガード `KominkaDogRun: Assets/KominkaDogRun/Scripts/KD_TreatPouch.cs:950` 付近を裏山改変)。Verify も同じ段階表を持ち、**「意図的な未導入 (その段階では null が正)」と「本来必要な参照の欠落 (FAIL)」を区別する**。

| 参照 | 段階 0 | 必須検査へ戻す条件 |
|---|---|---|
| `manager` (KH_LeashManager) / `recall` (KH_ToyRecall) / `activities` / `night` (KH_NightMode) | **必須** | 段階 0-b から必須 |
| `ledger` / `catalog` / `shopKeeper` | null 可 | 段階 1: 台帳配線後 (IMP-PRG-001) |
| `box` (KD_TreatBox) / treat slots | null 可 | 段階 2: おやつ配線後 (IMP-DOG-014) |
| `accessories` / `petFxBox` / 試着プレビュー | null 可 | 段階 2: かざり配線後 (IMP-DOG-015) |
| `sleepBoard` / `sleepPlacement` / `callPose` | null 可 | 段階 2: 呼ぶ配線後 (IMP-DOG-016) |
| `live` (KD_LiveSystem) | null 可 | DES-LIV-001 の採用後。それまでは skip を明示 |

### D-2 タブ ID は古民家のまま、パーティクル (2) の枠をさんぽに置き換える

- 定数は Treat0 / Toy1 / PetFx2 / Sleep3 / Live4 / Acc5 / Play6、TabCount 7。裏山は **PetFx2 → Walk2 (さんぽ)** に改名し、撫でFX の行 (FxRow) は Page_Acc の Content に AccRow の後ろへ並べる (群見出し「撫でエフェクト」)。行プールは同じ GridLayoutGroup に AccRow 16 + FxRow 11。
- 表示順 (案 A) `Order = {2,0,5,3,1,6,4}` = さんぽ / おやつ / かざり / 呼ぶ / おもちゃ / あそぶ / ライブ。幅 `Widths` (添字 = タブ ID) = {145,150,145,185,145,145,149} → さんぽ 145 / おやつ 145 / かざり 145 / 呼ぶ 185 / おもちゃ 150 / あそぶ 149 / ライブ 145。和 1064 + 6×6 = **1100** (古民家と同じ枠)。既定タブ `_tab = Walk2`。
- ヘッダ (Y314) とタブ帯 (Y224) は古民家でも別の段。モックで懸念した「1 段に入らない」は古民家の実配置では起きない。
- **撫でFX を かざり へ合流させる設計** (現物: `KominkaDogRun: Assets/KominkaDogRun/Scripts/KD_TreatPouch.Redraw.cs:58,61` で TabPetFx → `RedrawFx()`、TabAcc → `RedrawAcc()` の**排他分岐**。FX の購入確認更新も `RedrawFx` の中。Scroll ビルダーはタブごとに prefix 1 種):
  - (a) `RedrawFx` を「行の描画部 `RedrawFxRows(parent)`」と「タブ部」に分け、`RedrawAcc` の**カタログ階層 (`_accSetMenu == 0` かつ `_accProduct` 未選択) のときだけ** FxRows を描く。セット中身・装着方法・犬選択の階層では FxRow を非表示にする。
  - (b) 確認カードの kind Fx はそのまま使う (共有 1 枚は変えない)。
  - (c) 戻る操作は Acc の `_AccBack` を使う (FX 専用の戻るは作らない)。
  - (d) ビルダーの Hillside 拡張で Page_Acc の Content に `AccRow{0..15}` の後ろへ `FxRow{0..10}` を生成する (`Prefixes` は Acc だけ 2 種を許容する)。
  - (e) Verify に「混在行の順序」と「FX 行の onClick が `_Fx{i}`」を足す。
  - 担当: **ビルダー (d) は段階 0-b**、描画・購入 (a)(b)(c) は **IMP-DOG-015** (同 Issue の「そのまま使う」は「Acc へ合流する裏山改変」に限定する)。

### D-3 Editor ビルダーは古民家から丸ごと移植し、裏山の差分は別ファイルに隔離する

- 置き場 `Assets/KominkaHillside/Imported/KominkaDogRun/Editor/`。ファイル名は `KD_` のまま、namespace も古民家と同じ (一式コピーして個別管理する既定方針)。MenuItem は CLAUDE.md の規則で `Tools/KominkaHillside/Menu/…` へ付け替える (数字接頭辞で順序を表す方式は継承)。
- **命名の決定 (2026-09-23)**: 古民家からコピーする Editor は `KD_` 名のまま `Assets/KominkaHillside/Imported/KominkaDogRun/Editor/` に置き、裏山の差分は partial (例 `KD_TreatSetupEditor.Hillside.cs`) に隔離する。CLAUDE.md の `KH_{Name}Editor` 規則は**裏山で新規に書く Editor だけ**に適用する。段階 1・2 の Issue (IMP-PRG-001 / IMP-DOG-014 / IMP-DOG-015) の「Editor を `KH_` 名で作る」もこの決定に合わせて直した。
- **移植は「使う Editor を手で選ぶ」ではなく、`Assets/KominkaDogRun/Editor/` を丸ごと `Assets/KominkaHillside/Imported/KominkaDogRun/Editor/` へコピーする。** 理由は型参照の閉包が保てないため: 現物確認で `KD_TreatSetupEditor.Pouch.cs` ほかが `KD_ProductsEditor` / `KD_TreatFxSetupEditor` / `KD_ShopKeeperSetupEditor` / `KD_PhotoSetupEditor` / `KD_DogVoiceSettingsEditor` / `KD_GiftCodeSetupEditor` / `KD_SleepPlacementEditor` / `KD_AccessorySetupEditor` / `KD_LiveThumbnailEditor` など **30 種**を参照する。1 つ落とすとコンパイルが通らない。
- **「共通部品の移植」と「機能の有効化」を分ける。** コピーした Editor はすべてコンパイル対象にするが、未採用機能 (写真・犬の声・ギフトコード・ライブサムネ・ラジオ・寝室) の **MenuItem は `Tools/KominkaHillside/Menu/Imported (未使用)/…` に隔離し、Build 手順から呼ばない**。採用するときにこの隔離から出す。隔離対象の目安: `KD_PhotoSetupEditor` / `KD_DogVoiceSettingsEditor` / `KD_GiftCodeSetupEditor` / `KD_LiveThumbnailEditor` / `KD_DisplayGuidanceEditor` / `KD_UiReviewEditor` と、寝室・ラジオ系の Setup。実際の一覧はコピー後に MenuItem を数えて確定する。
- 裏山の差分は partial・派生に隔離する: `KD_TreatSetupEditor.Hillside.cs` (タブ表・Walk ページ生成・Toy のボール/ディスク・Live 準備中・PetFx 行の Acc 合流・Prune whitelist)、`KD_HandMenuScrollEditor` の Order/Widths/Prefixes は裏山値へ書き換え (差分をコメントで明示)。`KD_DogDefs` / `KD_ProductDefinitions` は裏山の値 (犬 6・商品) で保つ。
- **本体への変更はパス定数・定数表 (Order / Widths / Prefixes / タブ ID)・呼び出し口に限定し、機能差分は partial (`KD_TreatPouch.Hillside.cs` / `.Walk.cs` / `KD_TreatSetupEditor.Hillside.cs`) に置く。** 本体のロジックを書き直さない。
- 移植元 SHA・範囲・差分は [kominka-dog-system](../development/imports/kominka-dog-system.md) の既存書式で追記する。

### D-4 スプライト・フォント・文言

- スプライト: `KD_UiSpriteEditor` を移植し `Assets/KominkaHillside/Textures/UI/KD_UiRound.png` / `KD_UiCircle.png` を古民家と同じ定数 (96×96、半径 12、border 30、FaceLevel 0.55) で焼き直す。
- フォント: `Assets/KominkaHillside/Font/KD_NotoSerif SDF.asset` (古民家と同 GUID) を共有。**779/784 で満杯なので、段階 0-b の最初に 48 pt へ再ベイクする** (`KH_HandMenuEditor` の Rebake 経路を移植側の共通口へ寄せる)。Static 固定。
- 文言: `UITranslations.json` / `UILocalizationBindings.json` を `Assets/KominkaHillside/Data/` へコピーし、裏山向けキーを追加・差替 (さんぽ、ようこそ、設定の項目名、ライブ準備中、リード中の理由、斜面の注意、おじいさん)。`KD_LocalizationEditor` で `KD_Language` の配列を生成し ja/en を焼く。`KD_LocalizationBuildGuard` を有効にする。
- Charset は KD 側の `PouchWords()` 方式 (キー表から集める) へ一本化し、`KH_HandMenuEditor.Charset` は退役。

### D-5 各タブのバックエンド配線と担当

| タブ | 板の partial | 叩く先 | 担当 |
|---|---|---|---|
| さんぽ Walk2 (新規) | `KD_TreatPouch.Walk.cs` (新規。KH_HandMenu の AttachGate / Pending / GateText を移す) | `KH_LeashManager` (接続/解除/行き先)、犬 6 行 + 行き先 2 行 | 段階 0 |
| おやつ Treat0 | `.Treat` **裏山改変** (古民家の板にはおやつの購入が無く棚で買う。裏山は行に [買う] を生成し、確認カードに kind Treat を追加、`catalog._RequestBuy(key)`、キャンセル、残高と在庫上限 20 の再確認、購入後の行の再描画) | KD_TreatBox / Slot / Controller / Fx、購入は catalog | IMP-DOG-014 (台帳の口は IMP-PRG-001) |
| かざり Acc5 (+撫でFX) | `.Acc` + `.PetFx` | KD_AccessoryBox / KD_PetFxBox / KD_PlayerFxTag、ミニチュア | IMP-DOG-015 |
| 呼ぶ Sleep3 | `.Sleep` + KD_CallPose + KD_SleepPlacement | KD_SleepBoard / KD_SleepSlot、リード除外は KH_LeashManager、斜面は ValidPlacement 裏山改変 | IMP-DOG-016 |
| おもちゃ Toy1 | `.Toy` (裏山改変: 行に [ボール][ディスク]) | `KH_ToyRecall` (KD_ToyBox / KD_BallController は使わない) | 段階 0 |
| あそぶ Play6 | `KD_ActivitiesMenu` (裏山改変: 集合のみ、隊形 6・マーカー) | `KD_GroupActivities` 裏山改変 / `KH_TrainerPass` | 段階 0 |
| ライブ Live4 | `.Live` (裏山改変: live が null なら「準備中」1 行) | KD_LiveSystem は置かない (DES-LIV-001 後) | 段階 0 (準備中) |
| 残高・ポイント面 | ヘッダ Balance + ポイント面 (**入手経路 2 行のみ**。`KD_GiftCodePanel` は使わず、ギフトコードは未決定なので器を作らない) | KD_ProgressLedger / KD_ShopCatalog / KD_ShopKeeper | IMP-PRG-001 |
| 設定面 (器 + ナイトモード) | `KD_MenuSettings` (裏山改変: 寝室・ラジオ・解像度なし) | `KH_NightMode` のスライダー | **段階 0-b** (KH_HandMenu 退役でナイトモードの入口が無くなるため) |
| 設定面 (残り) | 同上 | 距離フェード、音量 (**当面 犬の声のみ。BGM・ライブは DES-WLD-002 / DES-LIV-001 後 — 開発側の暫定範囲として決定**)、呼ぶ許可・ライブ許可 (初期 ON)、言語、クレジット、案内 | 段階 3 |
| ようこそ | `KD_WelcomeGuideEditor` / `KD_HintDirector` | 初回のみ、`KD_Onboarding_v1` | 最小版 (題 + [メニューを見る]) は段階 1、全文は段階 3 |

板は**同期変数 0 本 (SyncMode None)** を不変条件とし Verify で守る。ローカル専用 public は `_` 接頭辞 (`vrc-networking`)。

**要求 (Pending) の保持先・ポーリング先・結果表示先**。KH_HandMenu が 1 か所で持っていたものを、頁ごとに分けて移す。

| 要求種別 | 保持先 | ポーリング先 | 結果の表示先 |
|---|---|---|---|
| つなぐ / 外す / 行き先 | `KD_TreatPouch.Walk.cs` (KH_HandMenu の `Pending` / `PollPending` / `GateText` を移す) | `KH_LeashManager` の共有状態の成立確認 | Walk 頁の状態行 (4 秒で「結果を確認できませんでした」) |
| 集合開始 / 集合終了 | `KD_ActivitiesMenu` 裏山改変 | `activities.LocalRequestState` (1 待ち / 2 受理 / 3 拒否 / 4 時間切れ) | Play 頁の状態行 (同上) |

退行確認の観点 (段階 0 の Verify / PCVR 項目に含める): **連打・タブ変更・閉じて再表示・拒否・4 秒超過**で結果表示が取り違わらないこと。

### D-6 開閉・配置・端末

古民家と同じにする: PlaceInFront (前方 0.95 m・高さ −0.05、眼高でスケール)、VR 右スティック下 **0.5 秒・しきい値 0.8**、Desktop `T`、Touch ランチャー (Overlay・sortingOrder 100)。**KH_HandMenu の 0.6 秒 / 0.6 しきい値は古民家値へ戻す (2026-09-23 決定。ユーザー指示「配置・文言・動線は全部古民家と同じ」に従う)。これは [DES-UI-001](../development/issues/archive/DES-UI-001-menu-and-leash-assist.md) で PCVR 確認済みの 0.6 秒から体感を変える変更なので、段階 0 の PCVR 確認項目に含める。** 拠点板 3 枚は裏山の追加として維持し (片手・座位対策)、Interact → `pouch._Toggle`。レイヤー 17 が Player(9)/PlayerLocal(10) と当たらない設定は `KD_HandMenuCollisionEditor` で適用・検査する。

### D-7 ミニチュア (試着プレビュー・ポーズ面)

`KD_DogPreviewEditor` / `KD_CallPoseEditor` を移植し、裏山の犬 6 匹 (同じモデル) から Preview Scene で焼く。出力 `Assets/KominkaHillside/Models/DogPreviews/`。シェーダ `KominkaDogRun/Dog Preview` と `KD_SleepPreview.mat` をコピー。layer 2、飾りは 1 メッシュ共有。使うのは IMP-DOG-015 (かざり) と IMP-DOG-016 (ポーズ面) で、段階 2 で焼く。

### D-8 段階と順序

| 段階 | 内容 | 担当 Issue | ゲート |
|---|---|---|---|
| **0-a** | **移植の土台**: `Assets/KominkaDogRun/Editor/` を丸ごとコピー、**パス定数の置換一覧を作る** (`grep "Assets/KominkaDogRun"` の結果を [kominka-dog-system](../development/imports/kominka-dog-system.md) に表で記録。Script / Program Asset は `Assets/KominkaHillside/Imported/KominkaDogRun/…`、Font / Textures / Data / Models / Materials は `Assets/KominkaHillside/…`)、MenuItem の付け替えと未採用機能の隔離 | [IMP-UI-001](../development/issues/IMP-UI-001-menu-table-driven-and-settings.md) (0-a) | **コンパイル 0** / Console エラー 0 / 置換一覧が imports に記録されている / Codex M |
| **0-b** | **器の構築**: スプライト焼き、フォント 48 pt 再ベイク、文言・言語生成、KD_TreatPouch を配置して 7 タブの器を作る。さんぽ・おもちゃ・あそぶを KH_HandMenu から移し、KH_HandMenu を退役。**設定面の器とナイトモードのスライダー**。ライブは準備中、おやつ・かざり・呼ぶは行に「準備中」。拠点板の付け替え | IMP-UI-001 (0-b) | コンパイル 0 / Verify (Pouch + Scroll Layout、下の Verify 表の段階 0 分) 全 PASS / 既存 3 機能の退行なし (`Walking/5 Verify` 全 PASS。起票時の「111」は古い件数で、2026-09-23 時点の項目数は 208) / Codex D+M / **PCVR で現行 3 機能の見え方が古民家と同じ (ユーザー)**。**到達状況 (2026-09-23): コンパイル 0・Verify 全 PASS・Walking/5 208/0 は済み。Codex 未・PCVR 未** |
| 1 | 台帳・おじいさん・ポイント・残高・確認カードの実体 (購入が動く)。**初回未受取の案内帯 (購入面の 1 行) と ようこそ の最小版 (題 + [メニューを見る]) を前倒しする** | [IMP-PRG-001](../development/issues/IMP-PRG-001-ledger-shopkeeper-points.md) | Verify + ClientSim (T-007/T-008) + Codex D+M |
| 2 | おやつ / かざり + 撫でFX + ミニチュア / 呼ぶ + ポーズ面 + 場所決め (3 本並列) | [IMP-DOG-014](../development/issues/IMP-DOG-014-treats-port.md) / [015](../development/issues/IMP-DOG-015-accessories-and-petfx-port.md) / [016](../development/issues/IMP-DOG-016-call-pose-port-and-slope.md) | 各 Verify + ハーネス + Codex D+M、PCVR (ユーザー)。加えて **入室 → おじいさん → 200pt → かざりで装着 → さんぽ の一周が 1 人で成立する** |
| 3 | 設定面 (距離フェード・ナイトモードスライダー・許可 2 種・言語・クレジット・案内・音量)、ようこそ | IMP-UI-001 (後半) | Verify + Codex D+M+U、PCVR |
| 4 | 複数人・Desktop/Touch・Quest/iOS | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) | 実機チェックリスト |

- 段階 0-a → 0-b が全体の前提。段階 1 → 2 の順 (購入が無いとかざり・おやつの行が試せない)。段階 2 の 3 本は触るファイルが**ほぼ**重ならない (Treat 系 / Acc+PetFx+Preview 系 / Sleep+CallPose 系) が、**共通ファイル (`.Pouch.cs` / `.Redraw.cs` / `.Confirm.cs` / `UITranslations.json`) は 3 本の担当が直接編集せず、差分を司令塔が順に統合する**。
- **各段階の手順**: 編集 → `unity_force_refresh_assets` → `unity_analyze_console_logs` をフィルタ無しでエラー 0 → Verify → Codex レビュー → コミット (docs は明示 stage)。**エージェントは Tools メニューを実行しない** (司令塔が実行して結果を渡す)。シーン・プレハブの一括変更は `vrc-scene-edit-safety` に従う。

## ビルド手順の対応表

古民家の実行順 (`KominkaDogRun: Assets/KominkaDogRun/Editor/` の MenuItem、数字接頭辞が順序) を裏山の `Tools/KominkaHillside/Menu/` へ移す。番号は提案。

| 古民家 | 裏山 (提案) | 段階 |
|---|---|---|
| 1 Hand Menu/1 Bake UI Sprite | Menu/1 Bake UI Sprite | 0-b |
| 2 Credit/1 Build Font Asset | Menu/2 Build Font Asset | 0-b では使わない (下の 2b で再ベイク) |
| (裏山のみ) | **Menu/2b Rebake Font 48pt** (`KD_TreatSetupEditor.Hillside.cs`。`PouchWords()` + `HillsideWords()` を 48 pt・Static で焼き直す) | 0-b |
| 3-4 Shop/1 Create Program Assets, Shop/2 Setup Shop | **段階 0 では飛ばす** → Menu/3 Create Shop Programs, 4 Setup Shop | 1 (IMP-PRG-001) |
| 5 Treat System/1 Create Program Assets | Menu/5 Create Pouch Programs | 0-b |
| 6 Sleep System/2 Setup | **段階 0 では飛ばす** → Menu/6 Setup Sleep | 2 (IMP-DOG-016) |
| 7 Treat System/2 Setup Treat System | **Menu/7 Build Menu = 裏山の入口 `SetupHillside()`** (`KD_TreatSetupEditor.Hillside.cs`。KD_DogPack 必須 → `KH_Dogs/KD_TreatPouch` → BuildPouchPanel → 開閉値 → `hillsideStage`・manager・recall → 拠点板 3 枚 → CopyProxyToUdon → 文言。段階 0 の参照は探さない)。古民家の Setup / Verify は `Menu/Imported-Unused/Treat System/2 - Setup Treat System (Kominka)` / `3 - Verify Treat System (Kominka)` へ移した | 0-b |
| 8-9 Activities/1,2 | Menu/8 Create Activities Programs, 9 Setup Activities (集合のみ) | 0-b |
| 10-11 Menu Finish/1,2 | Menu/10 Collect Words, 11 Setup Localization (文言・言語配列・グリフ・Bind) | 0-b |
| 12-13 Hand Menu/2 Rebuild Panel, 3 Apply Scroll Layout | Menu/12 Rebuild Panel, 13 Apply Scroll Layout | 0-b |
| 14 Distance Fade/2 | Menu/14 Setup Distance Fade | 3 |
| 15 Night Mode/1 | Menu/15 Setup Night Mode (裏山は `KH_NightMode` を指す) | **0-b** (スライダーの配線を段階 0-b へ前倒し。15a Verify も 0-b で実行) |
| 16 UI/5 Volume | Menu/16 Setup Volume | 3 |
| 17 UI/1 Refresh Menus | Menu/17 Refresh Menus | 0-b 以降毎回 |
| (Dog Previews/1 Build) | Menu/18 Build Dog Previews | 2 |
| Hand Menu Collision/1 | Menu/19 Apply Collision Layers | 0-b |
| (裏山のみ) | **Menu/19b Assign Network IDs** (SDK がビルド時に呼ぶ `NetworkIDAssignment.ConfigureNetworkIDs` を Editor から呼ぶ。新しい UdonBehaviour を置いた後と、器を消した後に実行) | 0-b 以降、器を足し引きするたび |
| 検証: Treat System/3, Hand Menu/4, Menu Finish/3・5, Activities/3, Hand Menu Collision/2, Dog Previews/2 | Menu/20 Verify Pouch, 21 Verify Scroll Layout, 22 Verify Localization, 23 Verify Activities, 24 Verify Collision, 25 Verify Dog Previews | 各段階 |

段階 0 で飛ばす 3 と 6 は、板側では「準備中」の行として成立させる。ライブ (Live4) は DES-LIV-001 の採用まで準備中のまま。

2026-09-23 の段階 0-b で司令塔が実行した順: 1 → 2b → 5 / 10 / 8 → 7 → 11 → 9 → 7 (再) → 11 (再) → 15 → 17 → 19 → 19b → 20〜24 / 15a / Walking/5。9 (あそぶ) の後に 7 と 11 をやり直すのは、7 が `activitiesMenu` を拾うため。結果は下の「実装記録」。

## Verify の追加項目 (古民家の検査に裏山分を足す)

| 検査 | 導入段階 | 対象オブジェクト | イベント名・接続先 | 期待値 | 未導入時の扱い |
|---|---|---|---|---|---|
| タブ幅の和 | 0-b | Tab0..Tab6 | — | `Sum(Widths) == 1064` かつ `Sum(Widths) + 6×6 == 1100` | 2026-09-23 PASS (Menu/20・21) |
| 既定タブ | 0-b | pouch | — | `_tab == TabWalk` | 2026-09-23 PASS |
| さんぽの行 | 0-b | Page_Walk の `WalkRow0..5` (6。計画時の仮名 `Dog{i}`) | `_WalkLeft{i}` / `_WalkRight{i}` / `_WalkOff{i}` → pouch backing | 3 ボタン × 6 行が名前どおり 1 本ずつ | 2026-09-23 PASS |
| さんぽの行き先 | 0-b | Page_Walk の `WalkRow6` (広場) / `WalkRow7` (古民家側の入口) | `_WalkPlaza` / `_WalkGate` | 各 1 本 | 2026-09-23 PASS |
| おもちゃの行 | 0-b | `ToyRow{i}` (6) | `_Toy{i}` / `_Disc{i}`、`pouch.recall` (KH_ToyRecall) | 2 ボタン / 行、recall 参照が非 null | 2026-09-23 PASS |
| かざりの混在行 | 0-b (順序) / 2 (FX の配線) | Page_Acc の Content | `_Acc{i}` / `_Fx{i}` | 子順が `AccRow0..15`, `FxRow0..10` | FX の onClick は段階 2 まで未検査 (FxRow は既定で非表示)。子順は 2026-09-23 PASS |
| 拠点板 | 0-b | `KH_MenuOpener` ×3 | Interact → pouch の `_Toggle` | 3 枚とも pouch を指す | 2026-09-23 PASS |
| 旧実装の不在 | 0-b | シーン全体 | — | `KH_HandMenu` コンポーネント **0 個**、旧 Panel なし | 2026-09-23 PASS (退役・保存後。シーン grep でも 0) |
| 同期変数 | 全段階 | pouch backing | — | **0 本** (SyncMode None) | 2026-09-23 PASS |
| ナイトモード | 0-b | 設定面の Slider | → `KH_NightMode` | 参照が非 null で強度 0〜100% を渡す | 2026-09-23 PASS (Menu/20・15a) |
| 欠字 | 全段階 | `KD_NotoSerif SDF` | — | `MissingGlyphs(PouchWords()) == 0`、Static | 2026-09-23 PASS (48 pt、`HillsideWords()` 込み) |
| あそぶ | 0-b | Page_Play | `KD_ActivitiesMenu` | 集合のみ (犬目線カメラ・2 匹の遊びが無い) | 2026-09-23 PASS (Menu/20・23) |
| ライブ | — | Page_Live | `_Live{i}` / `_LiveGift{i}` | **skip を明示** (DES-LIV-001 まで) | 準備中 1 行だけを確認。2026-09-23 は SKIP を出力して PASS |
| 参照の null 許容 | 段階表 (D-1) | pouch の各参照 | — | その段階で「null が正」か「FAIL」かを段階表で判定 | 意図的な未導入は PASS 扱いで理由を出力。2026-09-23 PASS |

最右列の「2026-09-23 PASS」は段階 0-b の実行結果 (Menu/20 `VERIFY RESULT PASS`、21 pass 394 / fail 0。詳細は「実装記録」)。

移植した Verify のうち古民家前提の検査は、**裏山の値へ書き換えるもの** (既定タブ = Walk / Page_Acc の Content に AccRow 16 + FxRow 11 / 行数の定義) と、**対象外として skip を明示するもの** (Live 行のリフレクション検査と行数 9、棚、寝台) を上表で分ける。黙って落とさない。

## リスク

| リスク | 対処 |
|---|---|
| KD_TreatPouch は 3,969 行で古民家固有の前提 (ライブ・寝室・写真・ラジオ) を含む | null 許容の裏山改変を最小限にし、差分は partial とコメントで残す。**丸ごと書き換えない** |
| フォントが 779/784 で満杯 | 48 pt 再ベイクを段階 0 で先に済ませ、可読性を PCVR で確認する (古民家は 60 pt) |
| pouch の Toy 実装は KD_ToyBox / KD_BallController 前提 | 裏山は `KH_ToyRecall` の口へ差し替える (partial `.Toy` の裏山改変) |
| 古民家の Verify の一部が裏山で成立しない | 該当検査を「裏山では対象外」と明示して skip する |
| 古民家の実機未検証項目 (18 pt 可読性、Touch/iOS、2 人の予約競合) | 裏山でも未検証として VER-WLD-001 へ送る |

## 実装記録

### 段階 0-a (2026-09-23)

**状態: 完了 (コンパイル 0)。C# のランタイムと Editor はエラー 0、U# は 83 本のコンパイルが完了した。** 記録の詳細は [kominka-dog-system「2026-09-23 Editor ビルダーの移植 (段階 0-a)」](../development/imports/kominka-dog-system.md#2026-09-23-editor-ビルダーの移植-段階-0-a)。

- 古民家 `Assets/KominkaDogRun/Editor/*.cs` の 129 本 (Testing 除く) を `Assets/KominkaHillside/Imported/KominkaDogRun/Editor/` にコピーした。有効なのは 101 本。28 本は欠けている型のため `Editor~/` へ除外した (デバッグ HUD、猫、ラジオ、パンプキン/キャンディのライブ制作系と、その連鎖先)。構築手順の Editor は 1 本も除外していない。
- パス置換 156 種 (延べ 227 か所) を node で機械的に適用した。`Assets/KominkaDogRun` の残りは 0。置換後の 101 本のうち 99 本は、`44505384` と置換の差以外は同一。残りの 2 本は下のガードだけが違う。
- MenuItem は、構築手順の 55 本を `Tools/KominkaHillside/Menu/1 Bake UI Sprite` 〜 `25c` に、それ以外の 241 本を `Menu/Imported-Unused/…` に置いた。すべて ASCII。KH_HandMenuEditor の 5 本は `Menu/Legacy/` へ移した。
- 版ずれを解消するため、未改変のランタイム 9 本 (KD_TreatPouch 本体 + Acc/Confirm/Live、KD_ActivitiesMenu、KD_LiveAccess、KD_ShopKeeper、KD_ShopShelf、KD_SleepBoard) を `503dde8f` 版から `44505384` 版へ更新した (46bc182f / abffd6c6)。ランタイム C# はエラー 0。
- 小アセット: `UITranslations.json` / `UILocalizationBindings.json` / `KD_SleepPreview.mat` / `KD_DogPreview.shader` を `Imported/KominkaDogRun/` にコピーした (GUID は古民家と同じ)。
- **裏山改変 (ガード)**: `KD_MenuFinishEditor.cs` (7 か所) と `KD_NightModeSettingsEditor.cs` (2 か所) にある購入アセットへの参照 (`HoboRadio_Controller` / `VolumeSlider` / `RBS.*` / `KD_MenuSettings.sleepKit`) を `#if KH_WITH_HOBORADIO` / `#if KH_WITH_SLEEPKIT` で囲んだ。以前のブロッカーは解消済み。行の一覧は移植記録の「裏山改変 (ガード)」表を見る。
- コンパイルの証拠:
  - `Assembly-CSharp-Editor.dll` は 21:00:18 に生成。
  - `Assembly-CSharp.dll` は 20:49:30 に生成。
  - U# は `Compile of 83 scripts finished in 00:03.809` が出て、新しいエラーは 0 件。
  - 既存の `KH_HandMenuEditor` の型は eval で解決できた。
- 0-b で扱う残件:
  - 案内文 9 か所に旧メニュー名が残っている。
  - シーン判定の条件 `Assets/Scenes/KominkaDogRun.unity` を裏山のシーンへ向ける (Menu/11 ほか)。
  - ラジオ無しでもラジオ設定頁の器が作られるので、作らないようにする。
- 未実施: シーン変更、Tools メニューの実行、Codex レビュー、コミット。

### 段階 0-b (2026-09-23)

**状態: 完了。** Verify 全 PASS・Codex D+M 済み・コミット a19112a。**2026-09-24 PCVR でユーザー OK** (現行 3 機能の見え方が古民家と同じ。総合評価)。Play (ClientSim・実機とも) も未実施。ファイルごとの差分は [kominka-dog-system「2026-09-23 器の構築 (段階 0-b)」](../development/imports/kominka-dog-system.md#2026-09-23-器の構築-段階-0-b)。

実装はランタイム担当 (R) と Editor 担当 (E) の 2 エージェントが並列で行い、司令塔が不足を直して Tools メニューを実行した。

**変更の要点**

- ランタイム (R):
  - `KD_TreatPouch.cs` は最小変更。`TabWalk = 2` を追加し、既定タブを Walk にした。Start の LogError の塊を `HillsideStartGuard()` 1 行に置き換えた。`_Tab2` は `SetTab(TabWalk)` を呼ぶだけ。
  - 新規 `KD_TreatPouch.Hillside.cs`: `hillsideStage` / `manager` / `recall` / `disc*` 配列 / `HillsideStartGuard` / `HillsidePreparing`。おやつ・かざり (FxRow も隠す)・呼ぶ・ライブのタブでは、行 0 に「準備中」を出して他を隠す。`ledger` が null の間はヘッダの Balance を隠す。通知はタブ別に 4 秒。ほかに `hillsideNeutralColor`。
  - 新規 `KD_TreatPouch.Walk.cs`: `walk*` 配列と `_WalkLeft0..5` / `_WalkRight0..5` / `_WalkOff0..5` / `_WalkPlaza` / `_WalkGate`。KH_HandMenu の AttachGate / Attach / Detach / ChooseDestination / GateText / Pending / PollPending を移した。4 秒で「結果を確認できませんでした」。
  - `.Toy.cs` 裏山改変: `_Toy{i}` がボール、`_Disc0..5` がディスクで、どちらも `KH_ToyRecall._RecallToy` を呼ぶ。`RedrawHillsideToy` を足した。`PollSharedRequests` から `PollWalkPending` / `PollHillsideToy` を呼ぶ。
  - `.Live.cs`: `_LiveSeeFx` の行き先を `TabAcc` にした。`.Redraw.cs`: TabWalk の分岐、`HillsidePreparing()` の 1 行、見出し `WordWalkTitle`。
  - `KD_ActivitiesMenu.cs` を集合のみに書き直した。`hillsidePass` = `KH_TrainerPass`。画面 0 は一覧 ([集合モード] / [開催をおわる] (トレーナーだけ) / [購入画面をひらく] → `_OpenStore`)、画面 1 は隊形 6 行 + [ここに集合]。結果は `LocalRequestState` で 4 秒。マーカーは 10 Hz で地面レイに載せる。
  - `KD_ActivitiesEditor.cs` を裏山向けに書き直した。裏山シーンのガード、Programs は GroupActivities / ActivitiesMenu だけ。`KH_Dogs/KD_Activities`、`KH_Dogs/KH_GatherMarkers/Marker0..5` (layer 14)。Verify は集合のみを見て、カメラ・商品・ライブは SKIP を明示する。
  - 設定面: `KD_MenuSettings.cs` に `hillsideNight` を足し、`_NightMode` から `_SetDarkness` を呼ぶ。ラジオの入口は `#if !KH_WITH_HOBORADIO` で閉じた。`KH_NightMode.cs` に `_SetDarkness(float)` / `_Darkness()` を足し、PlayerData のキー `KH_NightMode.dark` で保存・復元する。`KH_NightModeEditor.cs` の Verify は `KD_MenuSettings.hillsideNight` の配線を確かめる形にした。
  - `KH_MenuOpener.cs` は `pouch._Toggle` を呼ぶ。
  - `UITranslations.json` に 90 キー × 13 言語を足した (ja/en は必ず入れ、他の言語は en を複製)。`WordToyTitle` は ja「ボール・ディスクを手元へ」/ en "Bring a ball or disc to you"。
- Editor (E):
  - 新規 `KD_TreatSetupEditor.Hillside.cs`: 定数 `KD_Hillside` (`ScenePath` はシーン判定の正本で 1 か所だけ、`KH_Dogs`、拠点板 3 枚、`Stage = 0`)。`Menu/7 Build Menu` は `SetupHillside` になった (KD_DogPack 必須 → `KH_Dogs/KD_TreatPouch` → BuildPouchPanel → 開閉 −1 / 0.8 / 0.5 秒・panelDistance 0.95 → `hillsideStage = 0`・manager・recall → 拠点板 3 枚 → CopyProxyToUdon → `PouchWords` + `HillsideWords` → MarkSceneDirty)。段階 0 の参照 (box / ledger / items / catalog / sleepBoard / live …) は探さない。`HillsideBuildPages` が WalkRow0..7、ToyRow の Disc、Page_Acc の FxRow0..10 (既定非表示)、LiveRow0 の準備中を作る。`Menu/2b Rebake Font 48pt` もここ。
  - `.Pouch.cs`: Page_Walk を作る。items が null なら器 4 行、fxItems は常に空。既定タブ Walk、案内文 4 か所。
  - `KD_TreatSetupEditor.cs` / `.Verify.cs`: 古民家の Setup / Verify は `Menu/Imported-Unused/Treat System/2 - Setup Treat System (Kominka)` / `3 - Verify Treat System (Kominka)` へ移した。`Menu/20` は VerifyHillside。見るもの: 同期 0 / KH_HandMenu 0 個 (名前で数える) / 段階表 D-1 (PASS + 理由と FAIL) / タブ幅 1064+36 と Order / 既定タブ / さんぽ 8 行 / 行き先 / おもちゃ 2 ボタンと recall / かざりの子順 AccRow0..15 + FxRow0..10 / 拠点板 3 枚 / ナイトモードの Slider → KH_NightMode / 欠字 0・48 pt・Static / あそぶは集合のみ / 確認カード / Sliced / onClick 1 本 / NetworkIDs。
  - `KD_HandMenuScrollEditor.cs`: Order {2,0,5,3,1,6,4}、Widths {145,150,145,185,145,145,149}、`Prefixes[2] = "WalkRow"`、`RowsFor`、さんぽの犬行のボタン 156×3・18 pt。Verify は裏山の値で見る。21b / 21c は対象外として例外にした。
  - `KD_MenuFinishEditor.cs`: KD_Language / KD_MenuSettings / KD_LiveAccess を `KH_Dogs` 直下に置く。RadioSettings の頁とボタンは作らない。設定ホームの 音量 / 距離フェード / インフォメーション / 写真 の入口は非表示 (段階 3 で戻す)。BuildNightMode が `hillsideNight` を配線する。Verify 22 は裏山向け。
  - `KD_NightModeSettingsEditor.cs` は KH_NightMode 向け (SleepKit は SKIP)。`KD_LocalizationEditor.cs` / `KD_UiRefreshEditor.cs` は `KH_Dogs` を root にし、`PouchWords` + `HillsideWords` を使う。
  - シーン判定を定数 `KD_Hillside.ScenePath` にそろえた (19 ファイル)。旧メニュー名の案内文 9 か所を実名に直した。`UILocalizationBindings.json` は裏山に実在するものだけに絞った。`KH_DogWalkingEditor.cs` L56 では Walking/1 Create Programs の一覧から KH_HandMenu を外した (Walking/5 の項目は変わらない)。
- 司令塔の修正:
  - `KD_HandMenuScrollEditor.RowsFor` のおやつを `KD_ShopSetupEditor.TreatProductCount` (4) にした。Verify 21 が 6 を期待して FAIL していた。
  - `KD_TreatSetupEditor.Hillside.cs` で `accConfirmRoot` / `Label` / `Yes` を共有カードに配線した。古民家では `KD_DogPreviewEditor.RewireMenu` (段階 2) が配線するが、Verify 20 / 21 がこれを要求する。
  - 新メニュー `Menu/19b Assign Network IDs`。`VRC.SDKBase.Network.NetworkIDAssignment.ConfigureNetworkIDs(descriptor, out errors, new SetError[0])` を呼ぶ。SDK がビルド時に `AssignSceneNetworkIDs` で呼ぶものと同じ。
  - `KH_FrisbeePrototypeEditor.cs:239` の `KH_HandMenuEditor.FontPath` を `KominkaDogRun.Editor.KD_ShopSetupEditor.SharedFontPath` (同じ値) に替えた。
  - **KH_HandMenu を退役させた**: `KH_HandMenu.cs`・`.asset`・`KH_HandMenuEditor.cs` を削除し、シーンの `KH_Dogs/KH_HandMenu` (Panel / Launcher / Markers) を削除した。

**契約からの逸脱** (契約書は司令塔が固定したもの。どれも理由があり、司令塔が受け入れた)

- R:
  1. さんぽ・おもちゃの状態行は `statusLabel` でなく `feedbackLabel` (板の直下の Feedback) に出す。`statusLabel` は Page_Treat の子なので、他のタブでは見えない。
  2. ポーリングは `_Refresh` でなく `Update` の `PollSharedRequests` で行う。
  3. おもちゃの文言: 「フリスビー」→「ディスク」。区切りは「／」をやめて " / " にした (「／」は JSON に無く、字形が焼かれない)。結果は届いた時点で出す。押せないボタンは `interactable = false`。
  4. [ここに集合] は条件を満たさないと押せない。理由は状態行に出す。
  5. `KD_ActivitiesMenu.items` は宣言だけ残した (`KD_ProductsEditor` が参照する)。
  6. `_Tab2` と `hillsideNeutralColor` を足した。
- E:
  1. FxRow の 11 行は既定で非表示にした。
  2. Prune の whitelist に WalkRow などは足していない (Page の中にあるので不要)。
  3. MenuFinish の 3 System (KD_Language / KD_MenuSettings / KD_LiveAccess) は `KH_Dogs` 直下に置いた。インフォメーション・クレジット・ギャラリー・写真の器は 0-b では作らない (段階 3)。
  4. パス置換の副作用で、Editor 12 本の改行が LF にそろった (内容は同じ)。

**Tools メニューの実行と結果** (司令塔、この順)

| 順 | メニュー | 結果 |
|---|---|---|
| 1 | 1 Bake UI Sprite | KD_UiRound / KD_UiCircle を焼き直した |
| 2 | 2b Rebake Font 48pt | 60 pt / 784 字 (満杯、欠け 18) → 48 pt / 802 字、容量 1156、missing 0、Static。RESULT PASS |
| 3 | 5 / 10 / 8 | Program Asset はすべて既存 (変化なし) |
| 4 | 7 Build Menu | PASS。`KH_Dogs/KD_TreatPouch` を作成、SyncMethod None。行数は tab 0 = 4 / 1 = 6 / 2 = 8 / 3 = 6 / 4 = 9 / 5 = 27。拠点板 3/3。`hillsideStage = 0`、manager / recall True |
| 5 | 11 Setup Localization | Night mode slider → KH_NightMode。KD_SleepBoard は NOTE |
| 6 | 9 Setup Activities | BUILD complete |
| 7 | 7 を再実行 (`activitiesMenu = True`) → 11 を再実行 | — |
| 8 | 15 Setup Night Mode / 17 Refresh Menus / 19 Apply Collision Layers | 司令塔のラッパーの不備で各 2 回実行した。どれも冪等なので実害はない |
| 9 | 19b Assign Network IDs | 89 → 94。新規は KD_TreatPouch 103 / KD_Activities 99 / KD_Language 100 / KD_MenuSettings 102 / KD_LiveAccess 101。KH_HandMenu を退役させた後にもう一度実行し、KH_HandMenu (ID 73) の残骸を InvalidObject として除いた → 93、errors 0 |

**Verify 結果** (退役とシーン保存の後)

| メニュー | 結果 | SKIP を明示したもの |
|---|---|---|
| 20 Verify Pouch | `VERIFY RESULT PASS` | ライブ行の onClick / 棚 / 寝台・ポーズ面 / ミニチュア / おやつの枠 |
| 21 Verify Scroll Layout | pass 394 / fail 0 | — |
| 22 Verify Localization | PASS、failures 0 | ギャラリー / ライブ開催 / インフォメーション頁 / 写真の共有・ラジオ / クレジット板 |
| 23 Verify Activities | PASS、failures 0 | ライブの共有・管理者 / 商品 / 犬目線カメラ |
| 24 Verify Collision | PASS | — |
| 15a Verify Night Mode | failures 0 | SleepKit |
| Walking/5 Verify | TOTAL 208 / FAILED 0 | — (D-8 に書いた「111」は古い件数。今の項目数は 208) |

**コンパイルの証拠**

- `Assembly-CSharp.dll` と `Assembly-CSharp-Editor.dll` はどちらも 22:07 に再生成された。`error CS` は 0 件。
- U# は `Compile of 82 scripts finished` (KH_HandMenu の退役で 83 → 82)。Error / Exception は 0 件。

**シーン**: `Assets/Scenes/VRCDefaultWorldScene.unity` を保存した (dirty = False)。保存後のファイルを grep した件数: KD_TreatPouch 1 / KD_TreatPouchPanel 1 / KH_HandMenu 0 / Page_Walk 1 / WalkRow 8 / KD_Activities 1 / KD_MenuSettings 1 / KD_LiveAccess 1 / KD_Language 1 / KH_GatherMarkers 1。

**0-a の残件 3 件はすべて解消した**: 案内文 9 か所の旧メニュー名、シーン判定の条件 (`KD_Hillside.ScenePath` へ集約)、ラジオ設定頁 (作らない)。

**未実施**: Play (ClientSim・実機とも)、Codex レビュー、コミット。Play でしか分からない点は下の「実機チェックリスト」に入れた。

**失ったもの — [試作] の入口**: KH_HandMenu のヘッダにあった [試作: …] (`KH_FrisbeeCatch._DevVariant`、IMP-DOG-008 / [DES-UI-002](../development/issues/DES-UI-002-frisbee-settings.md)) は、退役とともに入口が無くなった。DES-UI-002 は「6 匹展開の間は残す」としている。代わりの入口 (設定ホームの [Dev] 行を段階 3 から前倒しする / `KH_FrisbeePrototypeEditor` の Editor 切替にする) は**ユーザーの判断待ち**。

**Codex レビュー (D + M、2026-09-23、gpt-6-astra)** — 対象は未コミット差分 + 新規 3 本。指摘 6 件 (中 4 / 低 2)。観点 (1) U# 制約 (2) `_` 規約 (3) 同期 0 本 (5) 段階 0 の null (7) PlayerData 復元順序、M (3) 契約の一致 (5) 後続段階の追跡性 (6) 旧実装の参照 は「問題なし」。

| # | 重要度 | 指摘 | 対応 |
|---|---|---|---|
| D-1 | 中 | `KD_ActivitiesMenu`: 開始要求の返事待ち中に [開催をおわる] を押すと、`_Request` は送らずに戻るのに `Pending(5)` へ上書きし、開始の受理を「集合を終えました」と誤表示する | **修正** (R)。`RequestBusy()` (自分の pending または `LocalRequestState == 1`) なら送らず戻る。待機中は [開催をおわる]・隊形の行・[ここに集合] を押せなくした |
| D-2 | 中 | `KD_TreatPouch.Hillside.cs`: 通知が全タブ共通の 1 枠。さんぽとおもちゃを同時に待たせると片方の待機・結果表示が消える | **修正** (R)。Walk / Toy / Play の 3 枠 (`_hsNoteKeys` / `_hsNoteArgs` / `_hsNoteUntil`)。表示は現在のタブの枠だけ、期限は枠ごと |
| D-3 | 中 | `Menu/19b Assign Network IDs` に対象シーンのガードが無く、別シーンの Descriptor も書き換える | **修正** (司令塔)。`KD_Hillside.Guard()` を通し、アクティブシーン内の Descriptor が 1 個のときだけ実行 |
| M-1 | 中 | `KD_ActivitiesMenu` / `KD_ActivitiesEditor` / `KD_MenuFinishEditor` は本体の直接置換で、partial 隔離の方針から外れる。古民家側の修正を取り込むとき機能差分の再解読が要る | **受け入れ (記録のみ)**。D-5 の表でこれらは「裏山改変」と決めていた (partial 限定は KD_TreatPouch 本体の方針)。上流差分を取り込む機会があれば partial 化を検討する。移植記録の裏山改変表が差分の一覧 |
| M-2 | 低 | `KD_ActivitiesEditor` の `HillsideScene` / `DogsRoot` が `KD_Hillside` と二重管理 | **修正** (R)。`KD_Hillside.ScenePath` / `DogsRootName` を参照 |
| M-3 | 低 | 本体の変更箇所の印が「裏山:」「裏山改変 (...)」と揺れ、規約の文字列で検索しても拾い切れない | **修正** (R)。本体 6 ファイル 14 か所を `裏山改変:` に統一 (理由・移動先 partial・復帰条件を続ける) |

修正後に force refresh → C# 0 (dll 22:19) → U# `Compile of 82 scripts finished`、Error 0 → Menu/20 PASS / 21 pass 394 fail 0 / 23 PASS / Walking/5 208 FAILED 0 を再確認した。フィールドの追加・改名は無く、シーンの proxy 値は変わっていない (Menu/7 の再実行は不要、シーンは dirty=False のまま)。


## 実機チェックリスト (段階 0-b の PCVR 同等性確認)

準備: シーン保存済み (2026-09-23 の段階 0-b 以降)・Console エラーなし。**Build & Test し直す。** 端末・人数を記録する。段階 0 は「現行 3 機能が古民家と同じ見え方になったか」だけを見る。おやつ・かざり・呼ぶ・ライブの中身、残高、設定面の残り (音量・距離フェード・インフォメーション・写真) は、この段階では**意図的に無い**。

### 0. エディタで判定できない項目 (1 人・PCVR)

- [ ] 板が古民家と同じ見え方。暗い面・白文字・金の下線。**48 pt で再ベイクした文字** (古民家は 60 pt) が木陰と夜でも読める。タブ 7 枚が重ならず、押し分けられる。開いたときの既定タブは さんぽ。
- [ ] さんぽ・おもちゃ・あそぶが**以前と同じように動く** (つなぐ / 外す / 行き先 / 目の前へ / 隊形と集合)。
  - さんぽ: [はずす] はつないでいる犬の行にだけ出る。選んでいる行き先は行の状態で分かる。
  - おもちゃ: 各行に [ボール] と [ディスク] がある。押せないボタンは暗い。**理由の行 (328×60、18 pt) が枠に収まり、切れていない**。
  - あそぶ: 未加入なら案内と [購入画面をひらく]。トレーナーなら [集合モード] → 隊形 6 → [ここに集合]。条件が足りない間は [ここに集合] が押せず、理由が状態行に出る。隊形のマーカーが地面に載る。
- [ ] **結果の表示が取り違わない**: 連打する / 結果が出る前にタブを変える / 閉じて開き直す / 拒否される / 4 秒を超える。それぞれで、別の操作の結果が出たり前の結果が残ったりしない。4 秒を超えたときは「結果を確認できませんでした」。
- [ ] 準備中のタブ (おやつ・かざり・呼ぶ・ライブ) は、行 0 に「準備中」だけが出る (ライブは「裏山のライブは準備中」と説明 1 行)。押しても何も壊れない。ヘッダに残高は出ない (段階 1 まで)。
- [ ] ナイトモード: 設定 → スライダーで夜の明るさが連続的に変わり、フェードが不自然でない。**退室して入り直すと値が戻る** (自分だけに反映)。設定ホームにラジオ・音量・距離フェード・インフォメーション・写真の入口が無いのは意図どおり (段階 3)。
- [ ] **開閉の長押しが 0.6 秒 → 0.5 秒・しきい値 0.6 → 0.8 に変わった体感が許容できる** (DES-UI-001 で PCVR 確認済みの値からの変更。誤爆・開けないが起きないか)。
- 参考 (チェック項目ではない): フリスビーの [試作] ボタンは無くなっている。代わりの入口は判断待ち (上の「失ったもの」)。

### 1. 1 人・端末別 (開閉 3 端末)

- [ ] VR: 右スティック下 **0.5 秒**で目の前に出る (0.6 秒から変わっている)。離れても残り、[閉じる] で消える。もう一度長押しで手元へ呼び戻る。
- [ ] Desktop: `T` で開閉。Touch: ランチャー [メニュー] で開く (VR では出ない)。**横画面では上端中央、縦画面では右中央**に出る。画面を回しても入力方式の切替 (VR ↔ Desktop ↔ Touch) でボタンの出方が壊れない。
- [ ] 拠点板: 入口・見晴らし・広場の「メニューをひらく」3 枚で開く。

| 症状 | 最初に見るところ |
|---|---|
| 文字が出ない・豆腐 | `Assets/TextMesh Pro/` の有無 (Menu/2a)。48 pt 再ベイク (Menu/2b) 後の欠字 0 検査 (Menu/20・22)。Build & Test し直したか |
| タブが重なる・はみ出す | Widths の和が 1064 で、間隔 6×6 を加えて 1100 か (Menu/21 Verify Scroll Layout) |
| 板が出ない・遠い | 開閉のしきい値 0.8 と 0.5 秒。PlaceInFront の 0.95 m。眼高スケール |
| ボタンが反応しない | layer 17 の当たり設定 (Menu/24)、onClick が 1 本か、backing への配線 |
| 拠点板で開かない | `KH_MenuOpener` の呼び先が `pouch._Toggle` になっているか |
| 結果がいつも「確認できませんでした」/ 前の結果が出る | さんぽは `KD_TreatPouch.Walk.cs` の Pending と `KH_LeashManager` の共有状態、あそぶは `activities.LocalRequestState`。ポーリングは `Update` の `PollSharedRequests` |
| 準備中のタブで行やボタンが崩れて出る | `hillsideStage = 0` と `HillsidePreparing()` (Menu/20 の段階表) |
| ナイトモードが効かない・戻らない | `KD_MenuSettings.hillsideNight` の配線 (Menu/15a)、PlayerData のキー `KH_NightMode.dark` |

## ユーザーへの報告 (開発側の決定)

以下は開発側で決めて進める。**異論があれば設計へ戻す。**

1. **KH_HandMenu を退役させ、KD_TreatPouch を裏山のメニュー本体にする** (D-1)。以前の「KH_HandMenu に頁を足す」方針からの転換になる。
2. **ギフトコードの器は作らない** (裏山での採否が未決定のため。ポイント面は入手経路 2 行のみ)。
3. **音量設定は当面「犬の声」だけ**にする (BGM・ライブは DES-WLD-002 / DES-LIV-001 の後)。

**ユーザーの判断が要るもの (2026-09-23 追加)**

4. **フリスビーの [試作] の入口**。KH_HandMenu の退役で無くなった。代わりに、設定ホームの [Dev] 行を段階 3 から前倒しするか、`KH_FrisbeePrototypeEditor` の Editor 切替にするか ([DES-UI-002](../development/issues/DES-UI-002-frisbee-settings.md))。
