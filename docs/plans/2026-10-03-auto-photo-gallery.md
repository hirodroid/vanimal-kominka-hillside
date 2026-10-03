# 自動写真とギャラリー (IMP-WLD-005) — 導入範囲の設計

2026-10-03 作成 (AI 設計案、ユーザー未確認)。Issue: [IMP-WLD-005](../development/issues/IMP-WLD-005-auto-photo-and-gallery.md)。前作は読み取りだけで、変更していない。

## 1. 結論 (推奨案)

- **初期公開は「小さい版」にする。** 前作の自動写真の仕組みは裏山にコピー済みなので、それをそのまま配線する。
  - 枚数: **8 枚** (前作は 24 + 寝室 4)
  - 解像度: **PC 1280×720、Quest / iOS 640×360**
  - 写真を見る場所: **門の看板 A の隣の写真ボード 1 枚**と、**手元メニューの写真の副画面**
- **撮るきっかけは、前作にもあるものだけにする。** ボールを持ってきた・追いかけた、撫でている、犬同士のじゃれ合い、トレーナーの集合写真。新しく足すのは「フリスビーを取った瞬間」の 1 つだけにする。
- **写真は各自の端末の中だけにあり、退室で消える。** 同期も保存もしない (前作と同じ)。持ち帰るには、ボードに VRChat のカメラを近づけて撮る。スマホはボードをタップして拡大し、画面を撮る。
- **大きい版は公開の後にする。** 散歩中の景色 (古民家が見える構図)、洗い場・水遊び、枚数とカテゴリの追加、フレーム、SNS の文言、が対象。
- 前作の写真は「未採用」と記録されている。これは犬ギミックの移植のとき (2026-09-20) に範囲の外とした、という意味で、不採用を決めたものではない (§2.3)。2026-10-03 のユーザー決定 (初期公開に入れる) を優先する。

## 2. 前作の仕組み (読取調査)

前作の場所は `C:/work/unity/vcc/KominkaDogRun/`。資料は `docs/development/design/photo-information.md`・`photo-frames.md`、Issue の IMP-CAM-001 / IMP-GAL-001 / IMP-GAL-005。

### 2.1 撮り方

- **撮るのはワールドの中の Unity Camera で、Udon が動かす。**
  - 見えない `PhotoCamera` を 1 台持つ (FOV 50、far 60、普段は無効)。
  - `KD_PhotoCapture._CaptureTo(slot)` がカメラを 1 フレームだけ有効にし、撮った RenderTexture を `VRCGraphics.Blit` でギャラリーの枠へ写す (KD_PhotoCapture.cs:66-85)。
  - 画像のダウンロード、プレイヤーの VRChat 写真、外部サーバーは使わない。
- **撮るのも見るのも各自の端末だけ。** 写真の Udon はどれも `BehaviourSyncMode.None` で、同期変数は 0 本。届くのは「出来事が起きた」ことだけで、すでに同期されている犬の状態から各自が知る。
- **撮るきっかけ** (時間で撮ることもシャッターも無い。シャッターは意図して作っていない)

| きっかけ | 場所 | 間隔 |
|---|---|---|
| ボールを持ってきて置いた | `KD_DogFetchController.Owner` の Drop 入り → 0.4 s 後 | 構図 4 種を順に |
| 撫でている | 3 s 撫で続けたら 4 枚続けて撮る | 30 s あける。撫でた本人だけ |
| 走る・追いかける | Chase 入り、走り始め | 45 s |
| 犬同士のじゃれ合い | `NotifyPlay` | 25 s、相手が 6 m 以内 |
| 集合写真 | `KD_GroupActivities` の `photoRevision` (同期) が変わったら、各自が撮る | 集合ごとに 1 回 |
| 寝室 | 前作だけ (裏山に寝室は無い) | — |

- 撮る犬が重なったときは、しばらく撮っていない犬を優先する。寝ている犬は撮らない。カメラの位置は、壁や床にめり込まないようレイで手前へ引く。

### 2.2 置き場所と見せ方

- **枠の数と容量**: 1280×720 (RGBA8、depth なし) を 24 + 4 枚、撮影用の RT が 1 枚 (depth あり)。
  - 前作の実測は **109 MiB**。PC の 2K 設定では 436 MiB になる。
  - 端末の設定は 2K / 標準 / OFF の 3 つで、PlayerData に保存する。Android / iOS では 2K を出さない。
- **ボード**: 6 列 × 4 段。列ごとにカテゴリがあり、同じカテゴリの中でいちばん古い写真から置き換える。
- **何も無いとき**: 最初に撮れるまでボードを隠す。「取得待ち・失敗」の表示は無い (ダウンロードしないので要らない)。
- **専用シェーダー `KD_PhotoPrint`**: VRChat カメラをボードに 35 cm まで近づけると、その写真がカメラの画面いっぱいに出る。
  - iOS では画面が黒くなる (BUG-CAM-003、シェーダーでは直せないと結論済み)。そのため、VR でないスマホではこの動きを切っている。
- **スマホのビューア**: ボードをタップすると、目の前に大きく出す (`KD_PhotoViewer`)。VR では出さない (BUG-CAM-004)。
- **手元メニュー**: 写真の専用ページは無い。設定とインフォメーションだけ。

### 2.3 裏山に今あるもの

- **実行用のスクリプト 9 本と専用シェーダー 4 本は、コピー済み。**
  - コピー先は `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_Photo*` / `KD_GalleryController` ほか。
  - 犬の側の受け口も残っている。`KD_DogFetchController.photoDirector`・`KD_DogWanderController.photoDirector`・`NotifyPlay`・`KD_GroupActivities` の `photos` / `photoRevision`。
- **まだ無いもの**: シーンへの配置、枠の RT と材質。移植記録の段階外 (`docs/development/imports/kominka-dog-system.md` L26・L92・L152-168) のとおり。`KH_DogWalkingEditor` は `act.photos = null` と配線している。
- **文言**: `photo.*` / `gallery.*` の 13 言語は、UITranslations にもうある。
- **「未採用」の意味**: 移植記録の「写真…はシーンへ配置しない。散歩に必要な機能を先に検証するため」のこと。game-design §7 の対象外は**犬目線カメラ** (常時描くモニター) で、写真のことではない。なので、2026-10-03 の採用と食い違う決定は無い。ただし game-design §7 には写真の行が無いので、採用を書き足す必要がある。

## 3. VRChat でできること・できないこと

| 項目 | 判断 | 根拠・確認 |
|---|---|---|
| Camera → RenderTexture を Udon で撮る | できる | 前作で公開・運用済み。**裏山の SDK 3.10.5 でのコンパイルは未確認** → `bash scripts/unity-compile.sh --udon` で確かめる |
| 実行時に RT を作って差し替える (`KD_PhotoSettings`) | 前作では動いている | 同上。小さい版では使わない (§4.3 を参照。固定の資産で足りる) |
| 写真を保存して次回に残す | **できない (採らない)** | Udon は画像をディスクへ書けない (KD_PhotoCapture.cs:12 の注記)。PlayerData に画像を文字列にして入れる案は、容量と処理時間から現実的でない (未検証。採らないので検証もしない) |
| 他の人と写真を共有する (同期) | **しない** | 画像の同期は帯域の点で現実的でない。同じ出来事を各自が撮る (集合写真は全員が同じ瞬間を撮る) |
| 撮る負荷 | 1 回あたり 1 フレームだけ | 常時描く RT カメラは約 0.9 ms・描画コール約 3 倍 (`vrc-performance`)。1 フレームなので一瞬だけ重くなる。影は切り、`cullingMask` と far 60 で絞る。Quest で一瞬引っかかるかは実機で見る |
| メモリ | 枚数 × 解像度で決まる | 「先に総量を決めてから枚数と解像度を割り振る」(`vrc-udonsharp`)。§4.3 |
| iOS で VRChat カメラに近づける動き | 画面が黒くなる | 前作の BUG-CAM-003。切る (前作どおり) |

## 4. 小さい版 (初期公開)

### 4.1 範囲

| 入れる | 入れない (大きい版へ) |
|---|---|
| 前作の撮るきっかけ 4 つ + 集合写真 (トレーナー) | 散歩中の景色、洗い場・水遊びのきっかけ |
| **フリスビーを取った瞬間** (新しいきっかけ 1 つ) | カテゴリ別の列、寝室 |
| 枠 8 枚。いちばん古い写真から置き換える (カテゴリ無し) | 2K、解像度の 3 段階 |
| 写真ボード 1 枚 (門の看板 A の隣) | フレーム、SNS の文言、複数のボード |
| 手元メニューの写真の副画面 (同じ RT を表示) | ページ送り・お気に入り・撮り直し (前作も作っていない) |
| 設定に「写真: ON / OFF」(既存の `photo.settings.off` / `standard`) | |

- フリスビーは裏山でいちばん写真になる場面なので、1 つだけ新しく足す。足し方は次のとおり。
  - `KH_FrisbeeCatch` の同期された状態が「跳んで取る」に入ったら、各自の端末で `photoDirector` の Drop と同じ口を呼ぶ。0.1 s 後に、犬の横からの構図で撮る。
  - 間隔は 20 s。

### 4.2 置き場所と画面

- **写真ボード**
  - 場所: 門の看板 A「あそびかた」の隣。人が戻ってくる場所で、洗い場からも近い。
  - 見た目: 看板と同じ古民家風の木枠 (`KH_GuideBoardsEditor` の材質と枠を使う)。
  - 中身: 4 列 × 2 段、1 枚 0.48×0.27 m。見出しは `gallery.caption` を流用する (文面は裏山向けに直す)。
  - VRChat カメラを近づけると全画面になる (PC / Quest VR)。スマホはタップでビューアを出す。
- **手元メニュー**
  - 入口: ヘッダーに [写真] ボタンを 1 つ足す。場所は見出しと残高の間 (x −65、120×44)。見出しの枠は 430 から 400 に縮める (−550〜−120 → −550〜−150。見出しはタブごとに変わる (例: 「ボール・ディスクを手元へ」、en "Bring a ball or disc to you")。全タブ × 13 言語で 400 に入るかを Verify で測り、入らなければボタンを 100 幅にする)。どのタブを開いていても 1 回で写真へ行ける。
  - 副画面: 地図の副画面と同じ `extraSettingsPages` 方式。4×2 の縮小表示 (各 240×135) を並べ、押すと大きく (800×450) 出す。[もどる] は前のタブへ戻る。
  - **ボードと同じ RT を表示するので、メモリは増えない。**
- **状態の表示** (どれも既存の文言を使う。足りない 2 つだけ新しく作る)

| 状態 | ボード | メニューの副画面 |
|---|---|---|
| まだ 1 枚も無い | ボードは出し、枠は空。見出しの下に「犬と遊ぶと自動で撮れます」(新しいキー `photo.empty`) | 同じ文言 |
| 写真が OFF | 「撮影 OFF」(`photo.settings.off`) と、設定の場所 | 同じ文言 + [設定へ] |
| 撮れた | 新しい写真から順に左上に並べる | 同じ。新しい 1 枚に印 (新しいキー `photo.new`「新しい」) |
| 取得待ち・失敗 | 該当なし (ダウンロードしないため)。Issue の完了条件の「取得待ち・失敗」は「まだ無い・OFF」に置き換える | — |

### 4.3 メモリの予算

| 端末 | 枠 | 撮影用 RT | 合計 (計算値) |
|---|---|---|---|
| PC | 1280×720 RGBA8 × 8 = 28.1 MiB | color 3.5 + depth 約 7 MiB | **約 39 MiB** |
| Quest / iOS | 640×360 × 8 = 7.0 MiB | 約 2.6 MiB | **約 10 MiB** |

- 前作 (109 MiB) の約 1/3 (PC) / 1/10 (Quest)。
- 端末で解像度を分ける方法: 枠の RT を 2 組作るとビルドに両方入るので避ける。**実行時に端末ごとに RT を 1 組だけ作る**。前作の `KD_PhotoSettings` がやっている方式で、2K の段階を外して使う。
- 実際の量は Quest の実機か、Android ビルドの代理計測で確かめる (`vrc-performance` のビルドサイズチェック)。合格は VER-WLD-001 に残す。

### 4.4 同期・権限

- 同期変数は増えない。写真の Udon は全部 SyncMode None。集合写真は、既存の `photoRevision` を読むだけ。フリスビーは既存の `state` を読むだけ。
- 操作権は要らない (自分の端末の写真を自分で見るだけ)。設定の ON/OFF も自分だけ。

## 5. 大きい版 (公開後)

- 散歩の場面:
  - リードでつないで歩いているところを、古民家が見える構図で撮る (見晴らし・橋。立ち寄り場所 `KH_WalkStopovers` を構図の手がかりにする)
  - 川で泳ぐ・浅瀬
  - 洗い場の泡だらけ → ブルブル
- カテゴリ別の列 (前作の 8/8/4/4 の型)、ボードを各拠点に置く、枚数を増やす (Quest の実測を見てから)
- フレーム (`KD_PhotoFrame`、前作の材料あり)、SNS の文言
- PC の 2K 設定

## 6. 実装計画 (小さい版)

| 作業 | ファイル | 中身 |
|---|---|---|
| 資産 | `Assets/KominkaHillside/Textures/Photo/` (新規) | 枠の RT 8 枚 + 撮影用 RT の雛形。材質 `M_KH_PhotoPrint` ×8 |
| 組み立て (新規) | `Assets/KominkaHillside/Scripts/Editor/KH_PhotoGalleryEditor.cs`。メニューは `Tools/KominkaHillside/Photo/1 - Build Photo and Gallery` / `2 - Verify` | カメラ・`KD_PhotoCapture`・`KD_PhotoDirector`・`KD_GalleryController` (1 カテゴリ・8 枠)・`KD_PhotoSettings` (2K を外す)・ボード・`KD_PhotoViewer` を作る。犬の `photoDirector` と `KD_GroupActivities.photos` を配線する。<br>`cullingMask` は裏山のレイヤーで作り直す (地面 11 を含め、UI・メニュー・自分の頭を外す)。<br>前作の `KD_PhotoSetupEditor` を下敷きにするが、そのまま実行はしない (前作専用の参照と寝室があるため) |
| フリスビーのきっかけ | `KH_FrisbeeCatch.cs` (状態の変化を見る所) + `KD_PhotoDirector.cs` (構図 1 つ) | §4.1 |
| 手元メニュー | 新しい Course メニュー (ヘッダーのボタン + 副画面)、`KD_TreatPouch` の新しい partial `KD_TreatPouch.Photo.cs` | `_PhotoOpen` / `_PhotoBack` / `_PhotoPick{i}` (ローカル専用なので `_` 付き) |
| 設定 | `KD_MenuSettings` 裏山改変 (`BuildHillsideSettings`) | 写真 ON/OFF の行 |
| 文言 | 既存の `photo.*` / `gallery.*` + 新しいキー 2 つ (`photo.empty`、`photo.new`) × 13 言語。`photo.help.body` を裏山向けに書き直す (寝室・かけっこを消し、フリスビーを足す) | Course/27 で字形 |
| 移植記録 | `docs/development/imports/kominka-dog-system.md` | 段階外から採用へ、の差分 |
| 仕様 | game-design §7 (採用の行)、world-ui-spec §4 (PHOTO の画面の行) | 決定後に反映する |

- **ハーネス**: 新しい ClientSim の小さいケース群 `Photo/3 - Harness`。
  - 確かめること: ボールの Drop・集合・フリスビーで写真が 1 枚増えること、9 枚目で最古が置き換わること、OFF で撮らないこと。
  - 見た目 (構図・明るさ) はユーザーが確かめる。
  - 歩行と洗浄のハーネスは回さない。
- **実機**: 撮った瞬間に引っかかるか (Quest 3)、メモリ、iOS でタップビューアが出ること、ボードの写真が木陰・夜でも見えること。

## 7. 工数とリスク

- **工数**: 1.5〜2.5 日 (組み立て 1 本 + メニュー 1 本 + きっかけ 1 つ + 文言)。加えて、ユーザーの PCVR と Quest の確認。
- **リスク**: 中〜高。4 件の中でいちばん大きい。
  - 本番シーンへカメラとボードを足す。
  - 撮影用カメラの写り込み (`cullingMask`)。前作でも Canvas が 5 枚写り込む Verify の FAIL が残っている。
  - Quest での一瞬の引っかかり。
  - ヘッダーの配置が前作と変わる (前作と同じにするという指示との関係。§8 の質問 2)。
- **公開日との関係**: 10-07 までに Quest で一度見られなければ、ボードとヘッダーのボタンを置かずに公開する (組み立てメニューの切り戻し) ことを推奨する。犬の受け口は null のまま動くので、外しても他の機能は壊れない。

## 8. ユーザーへの質問

1. **小さい版 (8 枚・ボード 1 枚・メニューの副画面・きっかけは前作 + フリスビー) で初期公開してよいか。** 推奨: はい。散歩の景色の写真は公開後にする。
2. **手元メニューの入口はヘッダーの [写真] ボタンでよいか。** 推奨: はい。ヘッダーは「古民家と同じ配置」の約束から外れるので、確認したい。代わりの場所は設定面の中 (前作の写真設定の場所) だが、見つけにくい。
3. **ボードは門の看板 A の隣でよいか。** 推奨: はい。広場の縁に置く案もある。

## 9. 実装メモ (2026-10-03)

2026-10-03 のユーザー決定 (小さい版を採用) を受けて実装した。**未コンパイル (Unity 未接続。オフラインの C# 検査だけ通過)・未実行・未検証。** 実行と検査の順は §9.6。

### 9.1 作ったもの

| 種類 | ファイル | 中身 |
|---|---|---|
| 組み立て (新規) | `Assets/KominkaHillside/Scripts/Editor/KH_PhotoGalleryEditor.cs` / `.Place.cs` / `.Verify.cs` | `Tools/KominkaHillside/Photo/0` (Program Asset)・`1 - Build Photo and Gallery`・`2 - Verify`・`4 - Place Board, Menu Button and Settings Row (optional)`・`5 - Remove …`・`9 - Remove Photo System` |
| ハーネス (新規) | `Assets/KominkaHillside/Scripts/Editor/Testing/KH_PhotoHarnessEditor.cs` | `Photo/3 - Harness` (`3b - Stop Harness`)。結果は `Logs/kh-photo-harness/latest.json` |
| 実行 (新規) | `Assets/KominkaHillside/Scripts/KH_PhotoAlbum.cs` | 並べ方と見せ方 (新しい順・ボードとメニューに同じ RT・状態の文・設定の ON/OFF・撮影の見回りの有効/無効)。Update なし |
| 実行 (新規) | `Imported/KominkaDogRun/Scripts/KD_TreatPouch.Photo.cs` | `_PhotoOpen` / `_PhotoBack` / `_PhotoToSettings` |
| 移した写真の裏山改変 | `KD_PhotoCapture.cs` | 撮れた直後 (Blit の後) に `capturedListener` へ `_OnPhotoCaptured`、`LastSlot` |
| 〃 | `KD_PhotoSettings.cs` | `highEnabled` (2K を外す)、`standardWidth/Height` と `mobileWidth/Height` (端末別に実行時の RT を 1 組だけ作る)、`boards` を任意に、`stateListener` へ `_OnPhotoStateChanged`、読み口 `CurrentMode` / `Ready` / `Failed` |
| 〃 | `KD_PhotoDirector.cs` | 見回りを `tickInterval` (0.1 s) に間引く (撫での積算は実時間)、`OnDisable` で予約を畳む、フリスビーのきっかけ `_OnFrisbeeCatch` (0.1 s 後に犬の横から、20 s 間隔) |
| フリスビーの口 | `KH_FrisbeeCatch.cs` の `ApplyState` に 1 行 | 同期された state が `StateJumpCarry` (跳んで取った) に入ったエッジで、全クライアントが `wander.photoDirector._OnFrisbeeCatch(dogIndex)`。新しいフィールドは足していない (徘徊の既存の受け口を読む) |
| 既存の組み立ての手直し | `KH_DogWalkingEditor.cs` | Walking/3 が `act.photos` / `f.photoDirector` を null に戻さない (Photo/1 の持ち物) |
| 〃 | `KD_HandMenuScrollEditor.LayoutFrame` | `PhotoButton` があるときだけ見出しを 430 → 400 (-550〜-150)、ボタンを (-65, 314) 120×44 |
| 〃 | `KD_MenuFinishEditor.Hillside.cs` | `PhotoToggleName` と位置、Verify 22 は置いたときだけ群「自分」に数える |
| 文言 | `UITranslations.json` | 新しいキー `photo.menu` / `photo.empty` / `photo.new` (13 言語)、`photo.help.body` を裏山向けに書き直し (13 言語)。`UILocalizationBindings.json` は変えていない |

### 9.2 設計からの差 (実装で決めたこと)

- **新しいキーは 3 つ。** §4.2 の 2 つ (`photo.empty`・`photo.new`) に加え、ヘッダの [写真]・面の見出し・設定の行の語として `photo.menu` (写真 / Photos …) を足した。既存に「写真」1 語のキーが無かった (`KD_ShopKeeper.WordPhotos` は「写真のこと」)
- **設定の行は 1 つのボタン [写真: ON / OFF]** (呼ぶ・ライブの許可ボタンと同じ「現在値を出すボタン」)。場所は入口の行の右端 (455, 128) 190×60。`photo.settings.standard` は「標準 1280×720」で Quest・iOS の 640×360 と食い違うので使わない。OFF の表示は `photo.settings.off`
- **見える物は任意の段 (Photo/4) に分けた。** Photo/1 だけの構成では何も見えず、`KH_PhotoAlbum.surfacesPlaced` が false なので撮らない (撮影の見回りも無効)。10-07 の切り戻しは Photo/5 (ボード・ボタン・設定の行を外す。撮影も止まる)。中核ごと外すなら Photo/9 (犬の受け口を null へ戻す)
- **文言は束縛表 (`UILocalizationBindings.json`) に登録しない。** 任意の段の部品を登録すると、置かない構成で Menu/11 の Bind と Guide/1 の解決が落ちる。表示中の文は `KH_PhotoAlbum` が書き、言語の切替は `KD_PhotoSettings` の既存の 4 Hz の見回りが `_OnPhotoStateChanged` で知らせる
- **名前は `KH_PhotoSystem`** (前作の `KD_PhotoSystem` にすると、`KD_LocalizationEditor.ValidateScene` などが前作のギャラリーの文字を検査しに来る)
- **ボードの置き場所:** 看板 A の右端の先に、A と直角に立てる (L 字。A を読む人が右を向くとボードが正面。読む場所を共有)。A の右端は門の線から約 2.1 m しかなく、看板の規則「門の線から 1.5 m」はこの形では取れない (オフラインの試算で約 1.06 m) ので、**写真ボードだけ 1.0 m に緩め**、塀の実物は Collider の検査で見る。候補 (L 字右 12・左 9・一直線 4) を順に試し、全部だめなら理由を出して何も変えない。既にあるボードの位置・向きは守る (人が動かしてよい)
- **cullingMask (裏山のレイヤーで作り直し):** 写すのは 0 Default / 8 Interactive / 9 Player / 11 Environment / 13 Pickup / 14 PickupNoEnvironment / 18 MirrorReflection (自分の全身。10 PlayerLocal は顔が欠けるので使わない)。手元メニューは 17 Walkthrough (KD_TreatPouchPanel・起動ボタン) と 2、案内の吹き出しは 5、洗い場の自分の床は 22 にあり、写さない。**17 には洗い場の棚・台・リフトもあるので、写真の背景に棚が写らない** (小さい版は洗い場を撮らない)。看板・洗い場の板・おじいさんの売り場は 0 にあり、世界の物として写る
- 古民家の `KD_GalleryController.Start` の「スマホでは吸着を切る」(BUG-CAM-003) は、ボードを `KH_PhotoAlbum` が持つので同じ処理をそちらへ置いた (`gallery.boards = null`)
- 見出しの 400 幅に全タブの見出しが入るかは Photo/2 が ja / en で測る (`TITLE_OVERFLOW` は WARN。入らなければ §4.2 どおりボタンを 100 幅に)

### 9.3 メモリと撮影 1 回の負荷 (計算値。実測は Quest / Android ビルドで)

| 端末 | 枠 8 枚 (RGBA8、深度なし) | 撮影用 RT (色 + 深度。深度は最悪の D32S8 = 8 byte/px で数える) | 合計 |
|---|---|---|---|
| PC (1280×720) | 28.1 MiB | 3.5 + 7.0 MiB | **約 38.7 MiB** (Verify の上限 48) |
| Quest / iOS (640×360) | 7.0 MiB | 0.9 + 1.8 MiB | **約 9.7 MiB** (上限 12。深度が D24S8 なら約 8.8) |

- RT アセット (雛形、640×360) は描かれる前に `KD_PhotoSettings` が実行時の 1 組へ差し替えて解放する (写真は撮れるまで隠れているので、雛形が確保されることはない)
- **撮影 1 回 = 撮影カメラが 1 フレームだけ 1 回描く** (片目分・上記の解像度・far 60・UI とメニューを外した cullingMask・MSAA と HDR なし) + `VRCGraphics.Blit` 1 回。常時描く RT カメラの相場 (約 0.9 ms・描画コール約 3 倍、`vrc-performance`) の 1 フレーム分が一瞬だけ乗る。影は品質設定どおり (カメラごとに切る手段は Udon に無い)。Quest での一瞬の引っかかりは実機で見る
- **頻度の上限:** 受け渡し 1 回に 1 枚 / 撫では 30 s に 1 回の 4 枚連続 (3 フレームおき) / 走り 45 s / じゃれ合い 25 s / フリスビー 20 s / 集合写真は集合ごとに 1 枚。カメラは 1 フレームに 1 回しか撮れない (`CanCapture`)。撮影 OFF・読み込み前・ボードとボタンが無い間は見回り (`KD_PhotoDirector`) ごと無効
- 常時動くもの: `KD_PhotoDirector` の Update (10 Hz に間引き、OFF なら無効)、`KD_PhotoSettings` の Update (言語を 4 Hz で見る既存の処理。毎フレームは時刻の比較 1 回)、`KD_PhotoViewer` の PostLateUpdate (開いていなければ即 return)。`KH_PhotoAlbum` は Update なし

### 9.4 UdonSharp の危ない所 (コンパイルと実機で確かめる)

- Camera → RenderTexture: 有効にしたフレームに 1 回描かせ、次のフレームで無効 + `VRCGraphics.Blit` (前作で公開・運用済み)。**SDK 3.10.5 での U# コンパイルは未確認**
- 実行時の `new RenderTexture(w, h, depth, format, readWrite)` / `Create` / `Release` / `Destroy` (`KD_PhotoSettings`。前作どおり)。Wrapper の綴りは確認済み (ctor・Create・Release・`RawImage.texture`・`Material.mainTexture`・`Behaviour.enabled`)。ノード定義での確認 (`vrc-udonsharp` §1.5) は未実施
- `UdonBehaviour.enabled` を `KH_PhotoAlbum.directorUdon` (KD_PhotoDirector の backing) へ毎回書く (読み取りはしない)。`KD_PhotoDirector` は自分の `enabled` を読んで、無効の間は通知 (受け渡し・集合写真・フリスビー) でも撮らない (`CapturePaused`)
- `UdonBehaviour.InteractionText` を板 8 枚とビューアへ書く (KD_Language と同じ API)
- U# の型をまたぐ参照は一般型 (`UdonSharpBehaviour`) で持つ (`KD_PhotoCapture.capturedListener`・`KD_PhotoSettings.stateListener`・`KD_TreatPouch.photoAlbum`)。`KH_PhotoAlbum` の Program Asset が無い間もほかの U# がコンパイルできるように

- **2026-10-03 ハーネス初回 (run 20261002-232223) の FAIL の原因:** `KD_PhotoSettings.boards` を null で配線しても、シーン保存で Unity が空配列 `[]` にして焼き、実行時は「長さ 0 != 8」で `Fail()` → `CaptureAllowed` が永久に false → 撮影の見回りが有効にならず 1 枚も撮れなかった。null と空配列を同じ「無し」として扱うよう直した (再コンパイルのみ。配線は変えていない)。ハーネスは `_failed` を最初に見て、写真が無いときは添字を引かずに理由を出すようにした

### 9.5 Play / 実機でしか分からないこと

- 写真の構図・明るさ (夜・木陰)、フリスビーの横からの構図、ボードの見え方と文字の大きさ (PC / PCVR / Quest)、看板 A との並び (ユーザーが見て直す)
- VRChat カメラを寄せた全画面化 (PC / Quest VR)、iOS で全画面化を切っていること (BUG-CAM-003)、スマホのタップ表示と画面端の [メニュー] が隠れること
- 撮影の一瞬の引っかかり (Quest 3)、実際のメモリ (Quest / Android ビルド)、11 のクライアント言語の字形と枠
- 2 人以上で: 集合写真が各自の端末で撮れること、フリスビーの取りが他人の端末でも撮れること (同期された state のエッジが届くか)

### 9.6 実行の順 (統括)

1. `bash scripts/unity-compile.sh` (C#) → `Tools/KominkaHillside/Photo/0 - Create Program Asset` → `bash scripts/unity-compile.sh --udon`
2. `Photo/1 - Build Photo and Gallery` (保存まで行う。前のシーンは `Temp/kh-photo/before-photo-build.unity`) → `Course/27 - Add Course UI Glyphs` → `Photo/2 - Verify`
3. (任意の段) `Photo/4 - Place Board, Menu Button and Settings Row (optional)` → `Photo/2 - Verify` → `Menu/22 Verify Localization` (Verify 22 の設定面の検査) → `Guide/2` (看板の置き場所の検査に写真ボードが当たらないこと)
4. `Photo/3 - Harness` (約 1 分。`bash scripts/kh-harness-run.sh photo`)
