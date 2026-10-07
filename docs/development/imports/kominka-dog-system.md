# 古民家ドッグランからの犬ギミック移植記録

- コピー元リポジトリ: `C:/work/unity/vcc/KominkaDogRun`
- コピー元タグ: `export/kominka-hillside-v1` (annotated、移動しない)
- コピー元コミットSHA（完全長）: `503dde8ff702e8d9f4dd73b4a6473ede7dcfd638`
- 移植日: 2026-09-20
- 範囲: 同じ6犬種のモデル、Animator、Pet/Fetch/Wander/StopAnchor/DogPack、各犬のボール。型依存を保つためDebug以外の既存runtimeスクリプト・Program Assetもコピー。元シーンの必要部分1133ブロックを抽出。ファイル594件と元データSHA-256は [一覧](kominka-dog-files.json)。`.meta` のGUIDを維持。
- 配置: 自作資産を `Assets/KominkaHillside/Imported/KominkaDogRun/`、購入資産を元と同じ `Assets/TORINOSUKE/`。新しい散歩機能は `Assets/KominkaHillside/Scripts/KH_Leash*.cs`。既存クラスは`KD_`とnamespaceを維持し、参照切れを増やさない。
- 管理方針: 以後はこのワールドで個別管理する。コピー元の変更は自動同期しない。

## Git外の依存

| 対象 | 版・出所・必要条件 |
|---|---|
| TORINOSUKEの6犬種とモーション | ユーザー所有・古民家の導入済みファイルから必要なGUIDだけをコピー。販売版番号は取得できず、ファイル一覧とハッシュで識別。元のPrefab/骨/アニメーション契約を維持。Git除外済み |
| lilToon | 2.3.4、既存VPM導入。犬導入前に最適化された設定では毛のマスク・陰影が無効だったため、公式同梱メニュー `Assets/lilToon/[Shader] Refresh shaders` で更新。元の犬材質は編集していない |
| UzumoreShader | `jp.sigmal00.uzumore-shader` 1.0.20、古民家の同名Packagesからコピー。lilToon >=1.9.0依存。VPM manifestに記録。欠落すると6匹の毛がピンクになる |
| SDK | 移植先Worlds/Base 3.10.5。Avatar DescriptorなどWorldsで解決しないコンポーネントは、移植したシーン内だけ除去。購入Prefabは変更しない |

タグだけで購入物・パッケージを復元できるとは扱わない。一覧の`missing`には組み込み参照と省略したAvatar関連GUID等が含まれるため、その数を参照整合の合否には使わない。Editor検査では実際のシーンのmissing script・材質・必須配線を確認する。

## 移植時の差分

- 元の作業ツリーにあった未コミットのシーン・Program Asset・音量設定は取り込まない。trackedファイルは `git show <SHA>:<path>` から採取した。
- `KH_DogPortSource.unity` は移植用の中間シーン。本番は `Assets/Scenes/VRCDefaultWorldScene.unity`。犬を`KH_Dogs`に置き、Terrain再生成の`KH_World`の外で保持する。
- 室内、睡眠部屋、ショップ、進行・保存、ライブ、写真、トレーナーの各サービスはシーンへ配置しない。散歩に必要な機能を先に検証するため。型依存のコードや元モデル内の非表示の飾りは残るが、選択UI・保存・解放を実装済みとは扱わない。
  - 写真 (自動写真とギャラリー) は、この時点では「段階外 (後で取り込む)」だった。不採用の決定ではない。2026-10-03 のユーザー決定で初期公開に入れ、[IMP-WLD-005](../issues/IMP-WLD-005-auto-photo-and-gallery.md) の小さい版 (8 枚・1 カテゴリ、ボード 1 枚、手元メニューの写真の面) として配線した。古民家の `KD_PhotoSetupEditor` (Imported-Unused) は使わず、`Tools/KominkaHillside/Photo/` (`KH_PhotoGalleryEditor`) が組む。移した写真のスクリプトの裏山改変 (`KD_PhotoCapture` の撮れた通知、`KD_PhotoSettings` の 2K 外し・端末別の解像度・板の任意化・状態の通知、`KD_PhotoDirector` の見回りの間引き・フリスビーのきっかけ、`KD_TreatPouch.Photo.cs`) は [設計の実装メモ](../../plans/2026-10-03-auto-photo-gallery.md#9-実装メモ-2026-10-03) にある。寝室 (`KD_BedroomPhotoDirector`)・写真フレーム (`KD_PhotoFrame`)・写真の情報面 (`KD_PhotoHelp`) は段階外のまま
- `KD_MenuSettings` のSleepKit参照を`KH_WITH_SLEEPKIT`で分離。既定では参照しない。夜・部屋用サービスは未配置。ナイトモードは 2026-09-21 に新規 `KH_NightMode` が担う (IMP-ENV-002。`KD_LiveNight` / `KD_MenuSettings` は使わず、覆いシェーダーは `KD_NightOverlay.shader` を `Assets/KominkaHillside/Shaders/KH_NightOverlay.shader` へコピーしてミラー抑制を足した派生物)。`KH_WITH_SLEEPKIT` 分岐の整理はライブ移植時。
- WanderにHillsideLeash partialを追加。犬のOwnerだけがNavMesh経路と既存の加減速・地面追従を使って動かす。位置同期は元のVRCObjectSyncを継続。
- Fetchは接続中の開始を拒否。接続状態が遅れて届いた競合は既存GiveUpを通してボールを解放する。Idleに戻る際に全クライアントでWanderのアニメ出力を失効し、次の更新で再適用。
- 門前の犬・ボール配置、自由行動の中心、地面レイヤー、首輪・先端・経路を裏山用へ設定。犬の子階層はPickupNoEnvironmentとし、地面レイが自分のColliderを拾わないようにする。
- 元の移動・触れ合い・ボール処理の全挙動が裏山でも成立するかは、ユーザーのPlay・2人試験で確認する。

## 再現と確認

`scripts/kh-dog-port.js` は固定SHAから初回コピーする補助ツール。`--copy`は初回のみ。改変済みの移植先と違う内容があれば上書きを拒否する。`--stage-only`は中間シーンのみ再生成する。パッケージは別途上表どおり導入する。

Unityメニュー `Tools/KominkaHillside/Walking/` の順序は、1 Create Programs → コンパイル完了確認 → 3 Build → 4 Bake Navigation → 5 Verify。Buildは既存犬の配置・調整済み首輪の位置を保持する。8 Refit Collarsは犬種別の初期値へ意図的に戻すメニュー。

コピー元はユーザーの既存ワールドの動作実績を土台とするが、この固定SHAの全機能を再試験した記録は今回取得していない。移植先の証拠・未確認条件は [散歩実装記録](../../plans/2026-09-20-leash-walking.md) を正とする。

## 後日の修正取り込み

| 日付 | 原因・修正元のコミット／Issue | このワールドへの適用内容・見送り理由 | 確認結果 |
|---|---|---|---|
| 2026-09-20 | IMP-DOG-002 | リードとFetchの競合・アニメ出力キャッシュは裏山固有の追加処理。古民家へ自動逆移植しない | 静的レビュー済み。ユーザーPlay/PCVR総合評価は良好、競合の項目別結果は未取得 |
| 2026-09-23 | 46bc182f (IMP-PRG-012 ギフト入口) / abffd6c6 (呼ぶ・ライブ全員許可の既定 ON)。866385e9・68676eaa は Editor/PNG のみ | 段階 0-a の版合わせ。未改変のランタイム 9 本を `44505384` 版へ更新 (下記「差分の適用」)。裏山で改変済みのファイルとの重なりは 0 | C# のランタイムと Editor はどちらもエラー 0。U# は 83 本のコンパイルが完了し、エラー 0 |

## 2026-09-23 Editor ビルダーの移植 (段階 0-a)

[メニュー再構築計画](../../plans/2026-09-23-menu-rebuild.md) D-3・D-8 の段階 0-a。古民家の手元メニューを作る Editor ビルダーを裏山へ持ち込み、パスと MenuItem を付け替えた。シーン・Program Asset は変えていない。Tools メニューも実行していない。

- 移植元: `C:/work/unity/vcc/KominkaDogRun` の作業ツリー。HEAD は `1cb8d619ae6811fd50ea26ea269dca77b15fdf2a`。`Assets/KominkaDogRun/Editor` と `Scripts` は HEAD および `4450538433a4b358577cd2dc9bcd2cd02479fd3f` と同じ内容 (`git diff --quiet` で確認)。ツリーは dirty だが、変更は 7 件 (4 つの Program Asset、シーン、`ProjectSettings` 2 件) で、Editor/Scripts の `.cs` には入っていない。
- 範囲: `Assets/KominkaDogRun/Editor/*.cs` の 129 本。`Testing/` は除く。`.meta` はコピーせず、Unity に新しい GUID を振らせた。コピー先は `Assets/KominkaHillside/Imported/KominkaDogRun/Editor/` で、ファイル名と namespace (`KominkaDogRun.Editor` / `KominkaDogRun.EditorTools`) は変えていない。
- 同一性: `git show 44505384:<path>` に同じ置換規則を当てた結果と照合した。124 本の時点で全一致した。最終の 101 本のうち 99 本も一致し、差は置換だけである。残りの 2 本は、下の「裏山改変 (ガード)」を入れた `KD_MenuFinishEditor.cs` と `KD_NightModeSettingsEditor.cs`。
- 置換は node スクリプトで機械的に行った。パスは 156 種 (延べ 227 か所)、MenuItem は 319 本 (直書き) と 16 本 (const 接頭辞 4 系統) を置き換えた。`Menu.SetChecked` が使う const 1 本と、案内文の中にある完全一致の旧メニュー名 12 か所も直した。

### 差分の適用 (版ずれの解消)

裏山のランタイムはタグ `503dde8f` から取り込んだものなので、コピーした Editor (`44505384` 相当) より古い。`503dde8f`→`44505384` の間に上流で変わったランタイムは 18 件ある。そのうち裏山で改変済みのファイルとの重なりは 0 件だった。そこで司令塔の判断に従い、未改変の 9 本を `44505384` 版に更新した。更新前に、9 本すべてが `503dde8f` 版と同一 (裏山で未改変) であることを diff で確かめた。`.asset` と `.meta` は触っていない。

| ファイル (`Imported/KominkaDogRun/Scripts/`) | 元コミット | 行数 (+/-) |
|---|---|---|
| `KD_TreatPouch.cs` | 46bc182f (ギフト入口 IMP-PRG-012) | 15/5 |
| `KD_TreatPouch.Acc.cs` | 46bc182f | 70/5 |
| `KD_TreatPouch.Confirm.cs` | 46bc182f | 24/0 |
| `KD_TreatPouch.Live.cs` | 46bc182f | 70/11 |
| `KD_ActivitiesMenu.cs` | 46bc182f | 2/1 |
| `Progress/KD_ShopShelf.cs` | 46bc182f | 44/1 |
| `KD_LiveAccess.cs` | abffd6c6 (呼ぶ/ライブ全員許可の既定 ON) | 2/1 |
| `KD_ShopKeeper.cs` | abffd6c6 | 1/1 |
| `KD_SleepBoard.cs` | abffd6c6 | 2/1 |

同じ区間の上流コミットのうち、`866385e9` (Touch ランチャーの iOS 修正) は Editor 2 本 (`KD_TreatSetupEditor.Pouch.cs` / `.Verify.cs`) とシーンだけを変えている。`68676eaa` (案内文言) が変えたのは `Data/Panfret/explain-onboarding.png` だけである。Editor 側の変更は今回のコピーに含まれる。PNG は取り込んでいない (Welcome Guide は段階 1・3 で扱う)。`KD_CatController.cs` も取り込んでいない (猫は裏山では使わない)。

### パス置換

規則は先に書いたものから順に適用し、最初に一致した 1 本だけを使う。

| 旧 | 新 |
|---|---|
| `Assets/KominkaDogRun/Textures/KD_UiRound.png` / `KD_UiCircle.png` | `Assets/KominkaHillside/Textures/UI/…` |
| `Assets/KominkaDogRun/Scripts/…` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/…` |
| `Assets/KominkaDogRun/Editor/…` | `Assets/KominkaHillside/Imported/KominkaDogRun/Editor/…` |
| `Assets/KominkaDogRun/Font/…` | `Assets/KominkaHillside/Font/…` |
| `Assets/KominkaDogRun/Data/CE…` | `Assets/KominkaHillside/Data/CE…` |
| `Assets/KominkaDogRun…` (上記以外。`CreateFolder("Assets/KominkaDogRun", …)` の親フォルダも含む) | `Assets/KominkaHillside/Imported/KominkaDogRun…` |

156 種の内訳は、移植先に実在 87 / 未コピー 42 / 接頭辞・フォルダ 27。フォルダは実行時に作られるか、まだ無い。未コピーの 42 種は、使う段階で取り込む。

- 段階 1: `Scripts/KD_ProgressManager.asset`
- 段階 2: `Animations/KD_PickupPose.anim`、`Materials/M_KD_Ball.mat`、`Models/Accessories/BlenderOptimized/Crown/KD_Crown_Optimized.fbx`、`Prefabs/KD_Ball.prefab`、`Scripts/KD_BallResetButton.asset`、`Textures/KD_HeartParticle.png`
- 段階外 (移植時に取り込まなかった機能。35 種): 猫、写真、ギャラリー (2026-10-03 に IMP-WLD-005 で採用。前作の資産は使わず `Assets/KominkaHillside/Textures/Photo`・`Materials/Photo` に作り直す)、クレジット板、寝室のブランケット、ポスター、夜空、プレビューシーン、Android プローブ、`KD_TreatSystem` (古民家でも廃止済み) など

<details><summary>156 種の全置換表</summary>

| 旧パス | 新パス | 箇所 | 移植先 |
|---|---|---|---|
| `Assets/KominkaDogRun` | `Assets/KominkaHillside/Imported/KominkaDogRun` | 15 | あり |
| `Assets/KominkaDogRun/` | `Assets/KominkaHillside/Imported/KominkaDogRun/` | 2 | あり |
| `Assets/KominkaDogRun/Animations` | `Assets/KominkaHillside/Imported/KominkaDogRun/Animations` | 4 | あり |
| `Assets/KominkaDogRun/Animations/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Animations/` | 1 | あり |
| `Assets/KominkaDogRun/Animations/AC_Cat.controller` | `Assets/KominkaHillside/Imported/KominkaDogRun/Animations/AC_Cat.controller` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Animations/AC_KD_Dog_` | `Assets/KominkaHillside/Imported/KominkaDogRun/Animations/AC_KD_Dog_` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Animations/AC_KD_Dog_Pome.controller` | `Assets/KominkaHillside/Imported/KominkaDogRun/Animations/AC_KD_Dog_Pome.controller` | 1 | あり |
| `Assets/KominkaDogRun/Animations/AC_KD_Dog.controller` | `Assets/KominkaHillside/Imported/KominkaDogRun/Animations/AC_KD_Dog.controller` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Animations/AC_KD_Npc.controller` | `Assets/KominkaHillside/Imported/KominkaDogRun/Animations/AC_KD_Npc.controller` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Animations/AN_CatSuwari2.anim` | `Assets/KominkaHillside/Imported/KominkaDogRun/Animations/AN_CatSuwari2.anim` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Animations/Companion` | `Assets/KominkaHillside/Imported/KominkaDogRun/Animations/Companion` | 1 | あり |
| `Assets/KominkaDogRun/Animations/Companion/KD_` | `Assets/KominkaHillside/Imported/KominkaDogRun/Animations/Companion/KD_` | 3 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Animations/KD_LieDown_` | `Assets/KominkaHillside/Imported/KominkaDogRun/Animations/KD_LieDown_` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Animations/KD_LieDown_Shiba.anim` | `Assets/KominkaHillside/Imported/KominkaDogRun/Animations/KD_LieDown_Shiba.anim` | 1 | あり |
| `Assets/KominkaDogRun/Animations/KD_PawHold_` | `Assets/KominkaHillside/Imported/KominkaDogRun/Animations/KD_PawHold_` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Animations/KD_PickupPose_` | `Assets/KominkaHillside/Imported/KominkaDogRun/Animations/KD_PickupPose_` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Animations/KD_PickupPose.anim` | `Assets/KominkaHillside/Imported/KominkaDogRun/Animations/KD_PickupPose.anim` | 1 | 未コピー (段階 2) |
| `Assets/KominkaDogRun/Animations/PumpkinPreview` | `Assets/KominkaHillside/Imported/KominkaDogRun/Animations/PumpkinPreview` | 1 | あり |
| `Assets/KominkaDogRun/Data` | `Assets/KominkaHillside/Imported/KominkaDogRun/Data` | 2 | あり |
| `Assets/KominkaDogRun/Data/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Data/` | 2 | あり |
| `Assets/KominkaDogRun/Data/BGM` | `Assets/KominkaHillside/Imported/KominkaDogRun/Data/BGM` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Data/BGM/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Data/BGM/` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Data/CE` | `Assets/KominkaHillside/Data/CE` | 1 | あり |
| `Assets/KominkaDogRun/Data/Panfret/explain-onboarding.png` | `Assets/KominkaHillside/Imported/KominkaDogRun/Data/Panfret/explain-onboarding.png` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Data/Panfret/explain.png` | `Assets/KominkaHillside/Imported/KominkaDogRun/Data/Panfret/explain.png` | 2 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Data/SE/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Data/SE/` | 3 | あり |
| `Assets/KominkaDogRun/Data/UILocalizationBindings.json` | `Assets/KominkaHillside/Imported/KominkaDogRun/Data/UILocalizationBindings.json` | 1 | あり |
| `Assets/KominkaDogRun/Data/UITranslations.json` | `Assets/KominkaHillside/Imported/KominkaDogRun/Data/UITranslations.json` | 1 | あり |
| `Assets/KominkaDogRun/Editor/KD_PumpkinPreviewEditor.MountInspection.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Editor/KD_PumpkinPreviewEditor.MountInspection.cs` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Font/` | `Assets/KominkaHillside/Font/` | 1 | あり |
| `Assets/KominkaDogRun/Font/KD_NotoSerif` | `Assets/KominkaHillside/Font/KD_NotoSerif` | 4 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Font/Noto_Serif_JP/static/NotoSerifJP-Regular.ttf` | `Assets/KominkaHillside/Font/Noto_Serif_JP/static/NotoSerifJP-Regular.ttf` | 6 | あり |
| `Assets/KominkaDogRun/KD_AndroidToonBuildProbe.unity` | `Assets/KominkaHillside/Imported/KominkaDogRun/KD_AndroidToonBuildProbe.unity` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/KD_MobileStripProbe-` | `Assets/KominkaHillside/Imported/KominkaDogRun/KD_MobileStripProbe-` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Materials` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials` | 2 | あり |
| `Assets/KominkaDogRun/Materials/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/` | 6 | あり |
| `Assets/KominkaDogRun/Materials/KD_FullscreenQuad.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/KD_FullscreenQuad.asset` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Materials/KD_GrassOverrideKeeper.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/KD_GrassOverrideKeeper.mat` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Materials/KD_NightOverlay.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/KD_NightOverlay.mat` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Materials/KD_NightSkybox.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/KD_NightSkybox.mat` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Materials/KD_Skybox.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/KD_Skybox.mat` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Materials/KD_SleepPreview.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/KD_SleepPreview.mat` | 3 | あり |
| `Assets/KominkaDogRun/Materials/KD_ToyPhysics.physicMaterial` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/KD_ToyPhysics.physicMaterial` | 1 | あり |
| `Assets/KominkaDogRun/Materials/M_KD_` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Materials/M_KD_Ball_` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_Ball_` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Materials/M_KD_Ball.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_Ball.mat` | 1 | 未コピー (段階 2) |
| `Assets/KominkaDogRun/Materials/M_KD_BlanketBlock_Android.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_BlanketBlock_Android.mat` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Materials/M_KD_BlanketHoundstooth_Android.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_BlanketHoundstooth_Android.mat` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Materials/M_KD_BlanketPlaid_Android.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_BlanketPlaid_Android.mat` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Materials/M_KD_CatEye_Android.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_CatEye_Android.mat` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Materials/M_KD_CreditPlaque.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_CreditPlaque.mat` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Materials/M_KD_GallerySlot{i}.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_GallerySlot{i}.mat` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Materials/M_KD_GalleryTextBackground.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_GalleryTextBackground.mat` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Materials/M_KD_Heart.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_Heart.mat` | 2 | あり |
| `Assets/KominkaDogRun/Materials/M_KD_PhotoPrint.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_PhotoPrint.mat` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Materials/M_KD_PhotoViewerBack.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_PhotoViewerBack.mat` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Materials/M_KD_PhotoViewerPhoto.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_PhotoViewerPhoto.mat` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Materials/M_KD_PosterBoard_Android.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_PosterBoard_Android.mat` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Materials/M_KD_PosterYSS_Android.mat` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_PosterYSS_Android.mat` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Materials/NightQueue` | `Assets/KominkaHillside/Imported/KominkaDogRun/Materials/NightQueue` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Models/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Models/` | 2 | あり |
| `Assets/KominkaDogRun/Models/Accessories/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/` | 3 | あり |
| `Assets/KominkaDogRun/Models/Accessories/BlenderOptimized` | `Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/BlenderOptimized` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Models/Accessories/BlenderOptimized/Crown/KD_Crown_Optimized.fbx` | `Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/BlenderOptimized/Crown/KD_Crown_Optimized.fbx` | 1 | 未コピー (段階 2) |
| `Assets/KominkaDogRun/Models/Accessories/CandyStep/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/CandyStep/` | 1 | あり |
| `Assets/KominkaDogRun/Models/Accessories/Everyday` | `Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/Everyday` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Models/Accessories/PumpkinParade/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/PumpkinParade/` | 1 | あり |
| `Assets/KominkaDogRun/Models/Accessories/ThemeSets/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/ThemeSets/` | 2 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Models/DogPreviews` | `Assets/KominkaHillside/Imported/KominkaDogRun/Models/DogPreviews` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Models/DogPreviews/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Models/DogPreviews/` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Models/KD_PhotoFrameOverlay.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Models/KD_PhotoFrameOverlay.asset` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Models/SleepPlacement` | `Assets/KominkaHillside/Imported/KominkaDogRun/Models/SleepPlacement` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Models/Toys` | `Assets/KominkaHillside/Imported/KominkaDogRun/Models/Toys` | 1 | あり |
| `Assets/KominkaDogRun/Models/Toys/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Models/Toys/` | 5 | あり |
| `Assets/KominkaDogRun/Models/Treat/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Models/Treat/` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Prefabs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Prefabs` | 2 | あり |
| `Assets/KominkaDogRun/Prefabs/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Prefabs/` | 1 | あり |
| `Assets/KominkaDogRun/Prefabs/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Prefabs/` | 1 | あり |
| `Assets/KominkaDogRun/Prefabs/KD_Ball_` | `Assets/KominkaHillside/Imported/KominkaDogRun/Prefabs/KD_Ball_` | 3 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Prefabs/KD_Ball.prefab` | `Assets/KominkaHillside/Imported/KominkaDogRun/Prefabs/KD_Ball.prefab` | 1 | 未コピー (段階 2) |
| `Assets/KominkaDogRun/Prefabs/PetFx` | `Assets/KominkaHillside/Imported/KominkaDogRun/Prefabs/PetFx` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Scenes` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scenes` | 2 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Scenes/CandyStepPreview.unity` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scenes/CandyStepPreview.unity` | 2 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Scenes/CandyStepPreview002.unity` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scenes/CandyStepPreview002.unity` | 2 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Scenes/LiveThumbnailCaptureTemp.unity` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scenes/LiveThumbnailCaptureTemp.unity` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Scenes/PumpkinParadePreview001.unity` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scenes/PumpkinParadePreview001.unity` | 2 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Scripts/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/` | 10 | あり |
| `Assets/KominkaDogRun/Scripts/KD_AccessoryBox.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_AccessoryBox.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_AccessoryBox.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_AccessoryBox.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_BallController.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_BallController.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_BallController.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_BallController.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_BallResetButton.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_BallResetButton.asset` | 1 | 未コピー (段階 2) |
| `Assets/KominkaDogRun/Scripts/KD_BgmPlayer.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_BgmPlayer.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_BgmPlayer.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_BgmPlayer.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_CallPose.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_CallPose.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_CallPose.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_CallPose.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_CandyDogPerformance.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_CandyDogPerformance.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_CandyDogPerformance.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_CandyDogPerformance.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_CatController.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_CatController.asset` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Scripts/KD_CatController.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_CatController.cs` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Scripts/KD_DistanceFadeSettings.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DistanceFadeSettings.asset` | 2 | あり |
| `Assets/KominkaDogRun/Scripts/KD_DistanceFadeSettings.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DistanceFadeSettings.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_DogFetchController.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogFetchController.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_DogFetchController.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogFetchController.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_DogPack.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogPack.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_DogPack.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogPack.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_DogPetController.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogPetController.asset` | 2 | あり |
| `Assets/KominkaDogRun/Scripts/KD_DogPetController.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogPetController.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_DogStopAnchor.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogStopAnchor.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_DogStopAnchor.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogStopAnchor.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_DogWanderController.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_DogWanderController.CandyMount.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.CandyMount.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_DogWanderController.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_LiveNight.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_LiveNight.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_LiveNight.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_LiveNight.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_LiveSystem.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_LiveSystem.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_LiveSystem.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_LiveSystem.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_ProgressManager.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_ProgressManager.asset` | 1 | 未コピー (段階 1) |
| `Assets/KominkaDogRun/Scripts/KD_ShopKeeper.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_ShopKeeper.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_ShopKeeper.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_ShopKeeper.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_SleepAudio.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepAudio.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_SleepAudio.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepAudio.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_SleepBoard.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepBoard.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_SleepBoard.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepBoard.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_SleepPlacement.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepPlacement.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_SleepPlacement.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepPlacement.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_SleepSlot.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepSlot.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_SleepSlot.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepSlot.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_TreatBox.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatBox.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_TreatBox.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatBox.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_TreatController.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatController.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_TreatController.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatController.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_TreatFx.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatFx.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_TreatFx.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatFx.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_TreatPouch.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatPouch.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_TreatPouch.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatPouch.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_TreatSlot.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatSlot.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_TreatSlot.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatSlot.cs` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/KD_TreatSystem.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatSystem.asset` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Scripts/KD_TreatSystem.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatSystem.cs` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Scripts/Progress/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/Progress/` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/Progress/KD_GiftCodePanel.asset` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/Progress/KD_GiftCodePanel.asset` | 1 | あり |
| `Assets/KominkaDogRun/Scripts/Progress/KD_GiftCodePanel.cs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/Progress/KD_GiftCodePanel.cs` | 1 | あり |
| `Assets/KominkaDogRun/Shaders/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Shaders/` | 1 | あり |
| `Assets/KominkaDogRun/Shaders/KD_NightOverlay.shader` | `Assets/KominkaHillside/Imported/KominkaDogRun/Shaders/KD_NightOverlay.shader` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Textures` | `Assets/KominkaHillside/Imported/KominkaDogRun/Textures` | 1 | あり |
| `Assets/KominkaDogRun/Textures/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Textures/` | 3 | あり |
| `Assets/KominkaDogRun/Textures/DogBook` | `Assets/KominkaHillside/Imported/KominkaDogRun/Textures/DogBook` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Textures/KD_CreditPlaque.png` | `Assets/KominkaHillside/Imported/KominkaDogRun/Textures/KD_CreditPlaque.png` | 1 | 未コピー (段階外 (未採用機能)) |
| `Assets/KominkaDogRun/Textures/KD_HeartParticle.png` | `Assets/KominkaHillside/Imported/KominkaDogRun/Textures/KD_HeartParticle.png` | 1 | 未コピー (段階 2) |
| `Assets/KominkaDogRun/Textures/KD_UiCircle.png` | `Assets/KominkaHillside/Textures/UI/KD_UiCircle.png` | 1 | あり |
| `Assets/KominkaDogRun/Textures/KD_UiRound.png` | `Assets/KominkaHillside/Textures/UI/KD_UiRound.png` | 1 | あり |
| `Assets/KominkaDogRun/Textures/LiveThumbs` | `Assets/KominkaHillside/Imported/KominkaDogRun/Textures/LiveThumbs` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Textures/LiveThumbs/<productKey>.png` | `Assets/KominkaHillside/Imported/KominkaDogRun/Textures/LiveThumbs/<productKey>.png` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |
| `Assets/KominkaDogRun/Textures/Particle/` | `Assets/KominkaHillside/Imported/KominkaDogRun/Textures/Particle/` | 2 | あり |
| `Assets/KominkaDogRun/Textures/PhotoFrames` | `Assets/KominkaHillside/Imported/KominkaDogRun/Textures/PhotoFrames` | 1 | 接頭辞・フォルダ (実行時に生成または未作成) |

</details>

### MenuItem

- 構築手順に使うもの (計画の対応表) は `Tools/KominkaHillside/Menu/<番号> <名前>` に並べた。同じ系統に属するほかの項目には、近い番号に英字を付けた。対象は 55 本。
- それ以外は `Tools/KominkaHillside/Menu/Imported-Unused/<古民家での Tools/KominkaDogRun/ 以下のパス>` に隔離した。コンパイル対象に残ったのは 241 本 (系統別の本数は下)。
- 日本語を含むパス 4 本は ASCII に直した: `Debug/KD_DogAnimDebugHud - Attach/Detach` と `Photo System/4,5 - Debug Snap Probe ON/OFF`。前者は除外したファイルにある。
- 既存の `KH_HandMenuEditor` の 5 本 (`0 Import TMP Essentials` 〜 `4 Verify Menu`) は `Tools/KominkaHillside/Menu/Legacy/…` へ改名し、番号の衝突を避けた。KH_HandMenu は段階 0-b で退役させる。
- 残っているもの: 案内文 9 か所が、系統名だけで古い `Tools/KominkaDogRun/…` を指している (例 `KD_TreatSetupEditor.Pouch.cs:76`「先に Tools/KominkaDogRun/Progress System」)。機能には影響しない。シーンを判定する条件 `"Assets/Scenes/KominkaDogRun.unity"` (`KD_ActionProductsEditor` / `KD_AccessoryEverydayEditor` ほか) は指定範囲外なので置き換えていない。段階 0-b で該当メニューを使う前に確認する。

| 古民家 (`Tools/KominkaDogRun/…`) | 裏山 (`Tools/KominkaHillside/Menu/…`) | ファイル |
|---|---|---|
| Hand Menu/1 - Bake UI Sprite | **1 Bake UI Sprite** | KD_UiSpriteEditor.cs |
| Credit/1 - Build Font Asset | **2 Build Font Asset** | KD_CreditSetupEditor.cs |
| Credit/0 - Import TMP Essentials | **2a Import TMP Essentials** | KD_CreditSetupEditor.cs |
| Shop/1 - Create Program Assets | **3 Create Shop Programs** | KD_ShopSetupEditor.cs |
| Shop/2 - Setup Shop | **4 Setup Shop** | KD_ShopSetupEditor.Setup.cs |
| Shop/3 - Verify Shop | **4a Verify Shop** | KD_ShopSetupEditor.Verify.cs |
| Shop/4 - CE Unlock (dev) | **4b CE Unlock (dev)** | KD_ShopSetupEditor.cs |
| Shop/5 - CE Relock | **4c CE Relock** | KD_ShopSetupEditor.cs |
| Treat System/1 - Create Program Assets | **5 Create Pouch Programs** | KD_TreatSetupEditor.cs |
| Sleep System/2 - Setup Sleep System | **6 Setup Sleep** | KD_SleepSetupEditor.cs |
| Sleep System/1 - Create Program Assets | **6a Create Sleep Programs** | KD_SleepSetupEditor.cs |
| Sleep System/3 - Verify Sleep System | **6b Verify Sleep System** | KD_SleepSetupEditor.cs |
| Treat System/2 - Setup Treat System | **7 Build Menu** | KD_TreatSetupEditor.cs |
| Activities/1 - Create Programs | **8 Create Activities Programs** | KD_ActivitiesEditor.cs |
| Activities/2 - Build | **9 Setup Activities** | KD_ActivitiesEditor.cs |
| Menu Finish/1 - Create Programs | **10 Collect Words** | KD_MenuFinishEditor.cs |
| Menu Finish/0 - Survey | **10a Survey Menu Finish** | KD_MenuFinishEditor.cs |
| Menu Finish/2 - Setup | **11 Setup Localization** | KD_MenuFinishEditor.cs |
| Menu Finish/7 - Allow Partial Translations (Next Build Only) | **11a Allow Partial Translations (Next Build Only)** | KD_MenuFinishEditor.cs |
| Hand Menu/2 - Rebuild Panel | **12 Rebuild Panel** | KD_HandMenuScrollEditor.cs |
| Hand Menu/3 - Apply Scroll Layout | **13 Apply Scroll Layout** | KD_HandMenuScrollEditor.cs |
| Distance Fade/2 - Setup | **14 Setup Distance Fade** | KD_DistanceFadeSettingsEditor.cs |
| Distance Fade/1 - Create Program Asset | **14a Create Distance Fade Programs** | KD_DistanceFadeSettingsEditor.cs |
| Distance Fade/0 - Survey | **14b Survey Distance Fade** | KD_DistanceFadeSettingsEditor.cs |
| Distance Fade/3 - Verify | **14c Verify Distance Fade** | KD_DistanceFadeSettingsEditor.cs |
| Distance Fade/4 - Preview UI | **14d Preview Distance Fade UI** | KD_DistanceFadeSettingsEditor.cs |
| Distance Fade/5 - Verify VM Parsers | **14e Verify VM Parsers** | KD_DistanceFadeSettingsEditor.cs |
| Night Mode/1 - Setup | **15 Setup Night Mode** | KD_NightModeSettingsEditor.cs |
| Night Mode/2 - Verify | **15a Verify Night Mode** | KD_NightModeSettingsEditor.cs |
| Night Mode/3 - Render | **15b Render Night Mode** | KD_NightModeSettingsEditor.cs |
| UI/5 - Setup Volume Settings | **16 Setup Volume** | KD_DogVoiceSettingsEditor.cs |
| UI/4 - Verify Dog Voice Settings | **16a Verify Dog Voice Settings** | KD_DogVoiceSettingsEditor.cs |
| UI/6 - Verify Volume Layout | **16b Verify Volume Layout** | KD_UiReviewEditor.cs |
| UI/1 - Refresh Menus | **17 Refresh Menus** | KD_UiRefreshEditor.cs |
| UI/2 - Verify Layout | **17a Verify Layout** | KD_UiReviewEditor.cs |
| UI/3 - Verify Touch Orientation | **17b Verify Touch Orientation** | KD_TouchLauncherVerifyEditor.cs |
| Dog Previews/1 - Build | **18 Build Dog Previews** | KD_DogPreviewEditor.cs |
| Dog Previews/5 - Fit Miniatures | **18a Fit Miniatures** | KD_DogPreviewEditor.cs |
| Hand Menu Collision/1 - Apply | **19 Apply Collision Layers** | KD_HandMenuCollisionEditor.cs |
| Treat System/3 - Verify Treat System | **20 Verify Pouch** | KD_TreatSetupEditor.Verify.cs |
| Hand Menu/4 - Verify Scroll Layout | **21 Verify Scroll Layout** | KD_HandMenuScrollEditor.cs |
| Hand Menu/5 - Preview Scroll Pages | **21a Preview Scroll Pages** | KD_HandMenuScrollEditor.cs |
| Hand Menu/6 - Preview Live Catalog Only | **21b Preview Live Catalog Only** | KD_HandMenuScrollEditor.cs |
| Hand Menu/7 - Preview Particle Catalog Only | **21c Preview Particle Catalog Only** | KD_HandMenuScrollEditor.cs |
| Menu Finish/3 - Verify | **22 Verify Localization** | KD_MenuFinishEditor.cs |
| Menu Finish/5 - Verify Language Extension | **22b Verify Language Extension** | KD_MenuFinishEditor.cs |
| Menu Finish/4 - Preview | **22c Preview Menu Finish** | KD_MenuFinishEditor.cs |
| Menu Finish/6 - Preview Photo Share Context | **22d Preview Photo Share Context** | KD_MenuFinishEditor.cs |
| Activities/3 - Verify | **23 Verify Activities** | KD_ActivitiesEditor.cs |
| Activities/4 - Preview UI | **23a Preview Activities UI** | KD_ActivitiesEditor.cs |
| Hand Menu Collision/2 - Verify | **24 Verify Collision** | KD_HandMenuCollisionEditor.cs |
| Dog Previews/2 - Verify | **25 Verify Dog Previews** | KD_DogPreviewEditor.cs |
| Dog Previews/3 - Render | **25a Render Dog Previews** | KD_DogPreviewEditor.cs |
| Dog Previews/4 - Measure Skin | **25b Measure Skin** | KD_DogPreviewEditor.cs |
| Dog Previews/6 - Check Menu States | **25c Check Menu States** | KD_DogPreviewEditor.cs |

Imported-Unused の系統別の本数: Accessories 6 / Android Toon Materials 4 / Archive 45 / BGM 3 / Bedroom and Onboarding 7 / Build Size 8 / Call Poses 7 / Cat 3 / Companion Motions 3 / Credit 2 / Debug 5 / Display Guidance 3 / Dog Book 5 / Dog Breath 2 / Dog Load 2 / Dog Pack 3 / Dog Performance 1 / Dog Sync 5 / Dog Unit 5 / Fetch System 6 / Fur Material 2 / Gift Code 4 / Live System 4 / Npc Controller 2 / Pet Sampling 1 / Pet System 13 / Photo Frames 4 / Photo System 8 / Photo and Information 4 / Plush Review 7 / Plush Shelf 5 / Products 3 / Progress System 3 / Release Check 1 / Scene Layout 3 / Shop Display LOD 3 / Shop Keeper 4 / Sleep Audio 4 / Sleep Placement 8 / Stop Anchor 5 / Texture Budget 2 / Toy Box 7 / Toy Catalog 3 / Toy Physics 6 / VR Interaction 4 / Wander System 3 / Welcome Guide 3

- 2026-09-24: `Build Size` 8 本は Imported-Unused から `Tools/KominkaHillside/Build Size/1〜8` へ昇格した (裏山の容量計測に使う)。`Groups()` / `Caps()` / `AudioCaps()` は裏山の実在フォルダに書き換え、TerrainData を数える kind `d` を足した。上限値は古民家の決定をそのまま使う。実測は [ビルドサイズ実測](../../plans/2026-09-24-build-size-audit.md)。`Texture Budget` 2 本は Allsky 1 枚だけの旧表で、Build Size 7 / 8 の Allsky 行が代替するため Imported-Unused のまま

### 除外した Editor (`Imported/KominkaDogRun/Editor~/`、Unity は読み込まない)

28 本。どれも、構築手順に使う Editor から参照されていない。依存を閉包計算して確かめた。

| ファイル | 欠けている型 | 必要になる段階 |
|---|---|---|
| `KD_DogAnimDebugHudEditor.cs` / `KD_DogPlayDebugHudEditor.cs` / `KD_ProgressDebugHudEditor.cs` | `KominkaDogRun.DebugTools` (古民家の `Scripts/Debug/`。取り込んでいない) | 不要 (デバッグ HUD) |
| `KD_CatSetupEditor.cs` | `KD_CatController` | 不要 (猫は裏山では使わない) |
| `KD_MobileStripEditor.cs` | `HoboRadio_Controller` (購入ラジオ) | 不要 (ラジオは使わない) |
| `KD_PumpkinProductionEditor.Revision.cs` / `KD_PumpkinPreviewEditor.Inspection.cs` | `VirtualCampers.LiveGenerator` (別リポジトリ) | DES-LIV-001 の採用後 |
| `KD_PumpkinPreviewEditor.Stage.cs` | `KD_CandyRehearsalEditor` (`Editor/Testing/`。コピー対象外) | DES-LIV-001 の採用後 |
| Stage.cs を外すと連鎖して使えなくなるもの: `KD_PumpkinPreviewEditor.{cs,Bodies,Costumes,MountAnimation,MountAnimatorInspection,MountInspection,Props,Revision}` / `KD_PumpkinProductionEditor.{cs,Scene}` / `KD_CandyDogSetupEditor.{cs,Choreography,Mount}` / `KD_CandyPreviewEditor` / `KD_ApprovedLivesEditor` / `KD_DogAnimatorBuilderEditor.{cs,Clips,Diagnose,RootAudit,Verify}` | 同じクラスの別ファイル (partial) にある `One` / `GuardStage` / `ShowVersion` などと、`KD_PumpkinPreviewEditor.MountHopPath` ほか | ライブ採用後。犬の Animator を作り直すときも戻す |

### 小アセットのコピー

`.meta` ごとコピーし、GUID を古民家と同じにした。

| ファイル (`Imported/KominkaDogRun/`) | 用途 |
|---|---|
| `Data/UITranslations.json` (1,034,236 B) / `Data/UILocalizationBindings.json` (40,208 B) | 文言の正本。`KD_MenuFinishEditor.CatalogPath` / `KD_LocalizationEditor` が参照する |
| `Materials/KD_SleepPreview.mat` (GUID `e26c2cf3…`) | ミニチュアのプレビュー |
| `Shaders/KD_DogPreview.shader` (GUID `530184e0…`、`KominkaDogRun/Dog Preview`) | 同上 |

置き場所は指示どおり `Imported/KominkaDogRun/Data/` にした。計画 D-4 の「`Assets/KominkaHillside/Data/` へコピー」とは違うので、0-b で文言に裏山向けの変更を入れるときにどちらへ置くかを決める。

### 裏山改変 (ガード)

司令塔の判断 (2026-09-23) で、購入アセットへの参照をプリプロセッサで分けた。partial では既存メソッドの本体にある参照を消せないため。ランタイムの `KD_MenuSettings` が使っている `#if KH_WITH_SLEEPKIT` と同じやり方にそろえた。どちらの定義も裏山では立てていないので、`#else` 側 (または何もしない側) がコンパイルされる。各ガードの直前に「裏山改変: 購入アセット (HoboRadio / SleepKit) は裏山に無い」の 1 行コメントがある。行番号は改変後のファイルのもの。

| ファイル | 行 | 定義名 | 囲んだもの | 裏山での動作 (`#else`) | 理由 |
|---|---|---|---|---|---|
| `KD_MenuFinishEditor.cs` | L66-80 (Setup) | `KH_WITH_HOBORADIO` | `HoboRadio_Controller` / `VolumeSlider` の探索と必須チェック | 「ラジオ未導入」の Log を 1 行出す | 購入ラジオが無い |
| 同上 | L99-104 (Setup) | `KH_WITH_HOBORADIO` | `settings.radio` / `radioAudio` / `radioKnob` / `knobMin` / `knobMax` への代入 | `radio` / `radioAudio` / `radioKnob` を null にする (以後の null チェックで無効) | 同上 |
| 同上 | L213-219 (BuildNightMode) | `KH_WITH_SLEEPKIT` | `RBS.SleepKit2.Udon.SleepKitSettingsManager` の探索と必須チェック | なし (スライダーだけ作る) | 購入 SleepKit が無い |
| 同上 | L225-227 (BuildNightMode) | `KH_WITH_SLEEPKIT` | `settings.sleepKit = sleepKit` | なし | `KD_MenuSettings.sleepKit` は同じ定義で外してある |
| 同上 | L235-239 (BuildNightMode) | `KH_WITH_SLEEPKIT` | ログ行の `sleepKit.name` | 「SleepKit 無し。KH_NightMode への配線は段階 0-b」のログを出す | 同上 |
| 同上 | L442-453 (Survey) | `KH_WITH_HOBORADIO` | ラジオの列挙 | `RADIO none` をログに出す | 購入ラジオが無い |
| 同上 | L618-622 (Verify) | `KH_WITH_SLEEPKIT` | `settings.sleepKit!=null` を含む Check | `sleepKit` の条件だけを除いた同じ Check (スライダーが設定ホームにあること) | 同上 |
| `KD_NightModeSettingsEditor.cs` | L54-59 (Verify) | `KH_WITH_SLEEPKIT` | `SleepKitSettingsManager` の探索と Check | 「裏山では KH_NightMode に置き換える (段階 0-b)」の Log を出して return | 同上 |
| 同上 | L81-87 (Verify) | `KH_WITH_SLEEPKIT` | backing `sleepKit` の Check | なし (return 後なので実行されない。CS0162 警告のみ) | 同上 |

注意: `KD_MenuFinishEditor.Setup` は元はラジオが無いと例外で止まった。裏山ではラジオ無しのまま進み、ラジオ設定頁 (`RadioSettings`) の器は作られる。0-b では、裏山版の `KD_MenuSettings` (寝室・ラジオ・解像度なし、D-5) に合わせて、この頁を作らない方へ直す必要がある。

### コンパイル結果 — **コンパイル 0 (達成)**

- C#: ガード追加後に force refresh した。Editor.log の新しい区間に `error CS` は 0 件。
  - `Library/ScriptAssemblies/Assembly-CSharp-Editor.dll` は 2026-09-23 21:00:18 に生成された (1,669,120 B)。最後に編集したファイル (`KD_MenuFinishEditor.cs`、21:00:15) より新しい。
  - `Assembly-CSharp.dll` は、9 本のランタイム更新後の 20:49:30 に生成されたもの。
- U#: `UdonSharp.UdonSharpProgramAsset.CompileAllCsPrograms(true)` を実行し、次の行が出た。

  ```
  [UdonSharp] Compile of 83 scripts finished in 00:03.809
  ```

  - 新しい Error / Exception は 0 件。
  - 同じ区間に、シーン内の `障子.*` / `引き戸*` (プレハブのインスタンス) について「has not been fully setup」「Cannot setup behaviour on prefab instance」の警告が出た。これは今回の変更前から同じセッションのログに出ている既存の警告で、シーンは保存していない。
- eval の `AssetDatabase.LoadAssetAtPath<MonoScript>(…).GetClass()` が次の型を返し、新しいアセンブリで型が解決できることを確かめた。
  - `KH_HandMenuEditor` → `KominkaHillside.Editor.KH_HandMenuEditor` (Legacy メニューを持つ既存の Editor)
  - `KD_MenuFinishEditor` → `KominkaDogRun.Editor.KD_MenuFinishEditor`
  - `KD_TreatSetupEditor` → `KominkaDogRun.Editor.KD_TreatSetupEditor`
- 以前のブロッカー (`KD_MenuFinishEditor.cs` / `KD_NightModeSettingsEditor.cs` の `RBS.*`、`HoboRadio_Controller`、`VolumeSlider`、`KD_MenuSettings.sleepKit` の参照) は、上のガードで**解消済み**。

### 0-b で扱う残件

- **案内文に残る旧メニュー名 (9 か所):** 系統名だけで `Tools/KominkaDogRun/…` を指している。表示だけの問題で、動作には影響しない。
  - `KD_MenuFinishEditor.cs`: Sleep System
  - `KD_SleepSetupEditor.cs`: Dog Pack
  - `KD_TreatSetupEditor.cs`: Dog Pack
  - `KD_TreatSetupEditor.Pouch.cs`: Progress System / Fetch System / Sleep System/2 / Live System/2 の 4 か所
  - `KD_WanderSetupEditor.cs`: Dog Pack/2
  - 除外した `KD_ProgressDebugHudEditor.cs` に 1 か所
- **シーン判定の条件:** `"Assets/Scenes/KominkaDogRun.unity"` を条件にしているため、裏山のシーン `Assets/Scenes/VRCDefaultWorldScene.unity` では該当メニューが止まる。構築手順で使うものの例は `KD_MenuFinishEditor.Setup` (Menu/11)。ほかに `KD_ActionProductsEditor` と `KD_AccessoryEverydayEditor` などにもある。0-b でメニューを実行する前に、裏山のシーンへ向ける。
- **ラジオ設定頁:** 上の注意のとおり、ラジオ無しでも器が作られるので、作らない方へ直す。

→ 3 件とも段階 0-b で解消した (下の節)。

## 2026-09-23 器の構築 (段階 0-b)

[メニュー再構築計画](../../plans/2026-09-23-menu-rebuild.md) D-1〜D-6・D-8 の段階 0-b。移植した `KD_TreatPouch` と Editor ビルダーで手元メニューの器をシーンに作り、`KH_HandMenu` を退役させた。実行順・Verify の数値・未確認の点は計画書の「実装記録 / 段階 0-b」が正本。ここには**古民家の移植元から何を変えたか**だけを残す。移植元の SHA は段階 0-a と同じ (`44505384` 相当)。

状態: Verify 全 PASS、シーン保存済み。Codex レビュー・コミット・Play (ClientSim・実機) は未実施。

### 新規と削除

| 区分 | ファイル | 内容 |
|---|---|---|
| 新規 partial | `Imported/KominkaDogRun/Scripts/KD_TreatPouch.Hillside.cs` | 段階フラグ `hillsideStage`、`manager` (KH_LeashManager) / `recall` (KH_ToyRecall)、ディスクのボタン配列、`HillsideStartGuard()` (段階 0 で未導入の参照は Log 1 行、必須の参照だけ LogError)、`HillsidePreparing()` (準備中の描画)、ledger null の間はヘッダの Balance を隠す、タブ別 4 秒の通知 |
| 新規 partial | `Imported/KominkaDogRun/Scripts/KD_TreatPouch.Walk.cs` | さんぽ (Walk2)。`KH_HandMenu` の AttachGate / Attach / Detach / ChooseDestination / GateText / Pending / PollPending を移した |
| 新規 partial | `Imported/KominkaDogRun/Editor/KD_TreatSetupEditor.Hillside.cs` | 定数 `KD_Hillside` (シーン判定の正本 `ScenePath`、`KH_Dogs`、拠点板 3 枚、`Stage`)、`Menu/7 Build Menu` = `SetupHillside`、`HillsideBuildPages`、`HillsideWords`、`Menu/2b Rebake Font 48pt`、`Menu/19b Assign Network IDs`、確認カードの共有配線 |
| 削除 | `Scripts/KH_HandMenu.cs` / `KH_HandMenu.asset` / `Scripts/Editor/KH_HandMenuEditor.cs` (各 `.meta` 共) | 退役。シーンの `KH_Dogs/KH_HandMenu` (Panel / Launcher / Markers) も削除し、NetworkIDs の残骸 (ID 73) を除いた |

### 裏山改変の一覧

担当は R = ランタイム担当、E = Editor 担当、司 = 司令塔。

| ファイル | 何を | 理由 | 担当 |
|---|---|---|---|
| `KD_TreatPouch.cs` | `TabWalk = 2` を追加 (`TabPetFx` はコンパイル互換で残す)、既定タブ Walk、Start の LogError を `HillsideStartGuard()` 1 行へ、`_Tab2` → `SetTab(TabWalk)` | D-2 (パーティクルの枠をさんぽに)、D-1 (段階 0 で null を許す) | R |
| `KD_TreatPouch.Toy.cs` | `_Toy{i}` ボール / `_Disc0..5` ディスク → `KH_ToyRecall._RecallToy`、`RedrawHillsideToy`、`PollSharedRequests` から `PollWalkPending` / `PollHillsideToy` | 裏山のおもちゃは KD_ToyBox / KD_BallController でなく KH_ToyRecall | R |
| `KD_TreatPouch.Live.cs` | `_LiveSeeFx` → `TabAcc` | 撫でFX は かざりへ合流 (D-2) | R |
| `KD_TreatPouch.Redraw.cs` | TabWalk の分岐、`HillsidePreparing()` 1 行、見出し `WordWalkTitle` | 呼び出し口だけ (D-3) | R |
| `KD_ActivitiesMenu.cs` | 集合のみに書き直し。`hillsidePass` (KH_TrainerPass)、一覧 / 隊形 6 + [ここに集合]、`LocalRequestState` 4 秒、マーカー 10 Hz・地面レイ。`items` は宣言だけ残す | 犬目線カメラ・2 匹の遊びは対象外 (DES-UI-003)。`items` は `KD_ProductsEditor` が参照 | R |
| `KD_ActivitiesEditor.cs` | 裏山シーンのガード、Programs は GroupActivities / ActivitiesMenu、`KH_Dogs/KD_Activities`、`KH_Dogs/KH_GatherMarkers/Marker0..5` (layer 14)、Verify は集合のみ (カメラ・商品・ライブは SKIP 明示) | 同上 | R |
| `KD_MenuSettings.cs` | `hillsideNight` を追加、`_NightMode` → `KH_NightMode._SetDarkness`、ラジオ入口を `#if !KH_WITH_HOBORADIO` で閉じる | ナイトモードは古民家と同じスライダー (DES-UI-003)。ラジオは裏山に無い | R |
| `Scripts/KH_NightMode.cs` (裏山の自作) | `_SetDarkness(float)` / `_Darkness()`、PlayerData キー `KH_NightMode.dark` で保存・復元 | 同上 | R |
| `Scripts/KH_MenuOpener.cs` (裏山の自作) | 呼び先を `pouch._Toggle` へ | 拠点板 3 枚は裏山の追加として残す (D-6) | R |
| `Scripts/Editor/KH_NightModeEditor.cs` (裏山の自作) | Verify を `KD_MenuSettings.hillsideNight` の配線確認に | 同上 | R |
| `Data/UITranslations.json` | 90 キー × 13 言語を追加 (ja/en 必須、他は en を複製)。`WordToyTitle` を ja「ボール・ディスクを手元へ」/ en "Bring a ball or disc to you" に差替 | さんぽ・ディスク・準備中・ライブ準備中などの裏山文言 | R |
| `KD_TreatSetupEditor.Pouch.cs` | Page_Walk、items null なら器 4 行、fxItems は常に空、既定タブ Walk、案内文 4 か所 | 段階 0 はおやつ・撫でFX の実体が無い | E |
| `KD_TreatSetupEditor.cs` / `.Verify.cs` | 古民家の Setup / Verify を Imported-Unused へ、`Menu/20` = VerifyHillside (段階表 D-1 で PASS+理由 / FAIL を分ける) | 古民家の Setup はおやつの器一式を要求する | E |
| `KD_HandMenuScrollEditor.cs` | Order {2,0,5,3,1,6,4}、Widths {145,150,145,185,145,145,149}、`Prefixes[2] = "WalkRow"`、`RowsFor`、さんぽの犬行のボタン 156×3・18 pt、Verify を裏山値に、21b / 21c は対象外 | D-2 | E |
| 同上 (`RowsFor` のおやつ) | `KD_ShopSetupEditor.TreatProductCount` (4) | Verify 21 が 6 を期待して FAIL していた | 司 |
| `KD_MenuFinishEditor.cs` | 3 System を `KH_Dogs` 直下、RadioSettings の頁・ボタンを作らない、設定ホームの 音量 / 距離フェード / インフォメーション / 写真 の入口を非表示 (段階 3 で戻す)、BuildNightMode で `hillsideNight` を配線、Verify 22 を裏山向けに | ラジオが無い / 段階 3 の項目は 0-b で出さない | E |
| `KD_NightModeSettingsEditor.cs` | KH_NightMode 向け (SleepKit は SKIP) | SleepKit が無い | E |
| `KD_LocalizationEditor.cs` / `KD_UiRefreshEditor.cs` | root を `KH_Dogs`、文字集合を `PouchWords` + `HillsideWords` | 裏山のシーン構成 | E |
| `Data/UILocalizationBindings.json` | 裏山に実在するものだけに絞った: labels 205 → 108 / interactions 33 → 0 / pickups 3 → 0 / names 38 → 4。追加 34、差替 3 | 無い対象への束縛で Verify が落ちないように | E |
| `Scripts/Editor/KH_DogWalkingEditor.cs` L56 (裏山の自作) | Walking/1 Create Programs の一覧から KH_HandMenu を外した (Walking/5 の項目は不変) | 退役 | E |
| `Scripts/Editor/KH_FrisbeePrototypeEditor.cs:239` (裏山の自作) | `KH_HandMenuEditor.FontPath` → `KominkaDogRun.Editor.KD_ShopSetupEditor.SharedFontPath` (同じ値) | 退役 | 司 |

- 文言ファイルの置き場所は `Imported/KominkaDogRun/Data/` のまま編集した (段階 0-a で保留した `Assets/KominkaHillside/Data/` への移動はしていない)。

### MenuItem の変更

| メニュー | 変更 |
|---|---|
| `Tools/KominkaHillside/Menu/7 Build Menu` | 古民家の `Setup()` から裏山の `SetupHillside()` へ付け替え |
| `Menu/Imported-Unused/Treat System/2 - Setup Treat System (Kominka)` / `3 - Verify Treat System (Kominka)` | 古民家の Setup / Verify の移動先 (新設) |
| `Menu/20 Verify Pouch` | 裏山の `VerifyHillside` |
| `Menu/2b Rebake Font 48pt` | 新設 (48 pt・Static で再ベイク) |
| `Menu/19b Assign Network IDs` | 新設 (`NetworkIDAssignment.ConfigureNetworkIDs` を呼ぶ) |
| `Menu/Legacy/…` (KH_HandMenuEditor の 5 本) | `KH_HandMenuEditor.cs` の削除で消えた |

### 0-a の残件の解消

- **シーン判定**: `"Assets/Scenes/KominkaDogRun.unity"` の条件を、定数 `KD_Hillside.ScenePath` (`Assets/Scenes/VRCDefaultWorldScene.unity`) の参照に置き換えた。E の報告では 19 ファイル。現物で `KD_Hillside.ScenePath` を参照する `.cs` は定義元の `KD_TreatSetupEditor.Hillside.cs` を含めて 21 本。`Editor~` 以外に旧パスの文字列は残っていない。
- **案内文の旧メニュー名 9 か所**: `Tools/KominkaHillside/Menu/…` の実名へ直した。
- **ラジオ設定頁**: `KD_MenuFinishEditor` が作らなくなった。ランタイムの入口も `#if !KH_WITH_HOBORADIO` で閉じた。
- 副作用: パス置換の都合で Editor 12 本の改行が LF にそろった (内容は同じ)。

## 2026-09-24 台帳・おじいさん・ポイント (段階 1)

[メニュー再構築計画](../../plans/2026-09-23-menu-rebuild.md) D-5・D-8 の段階 1 ([IMP-PRG-001](../issues/IMP-PRG-001-ledger-shopkeeper-points.md))。古民家の台帳・カタログ・おじいさん・ようこそを裏山のシーンに置き、手元メニューに残高とポイント面を足した。実行順・Verify の数値・未確認の点は計画書の「実装記録 / 段階 1」が正本。ここには**古民家の移植元から何を変えたか**だけを残す。移植元の SHA は段階 0-a と同じ (`44505384` 相当)。

状態: Verify 全 PASS、シーン保存済み。Codex レビュー (実行中)・コミット・Play (ClientSim・実機) は未了。

### 準備のコピー (前コミット 7165e45)

段階 1 の準備として、次を古民家からコピーした。記録はコミット 7165e45 のメッセージにある。

| 対象 | 置き場 | 備考 |
|---|---|---|
| polyperfect (Low Poly Animated People + Common) | `Assets/polyperfect/` | 購入アセット、git 管理外 (`.gitignore`)。おじいさんの man_farm に使う |
| `AC_KD_Npc.controller` | `Imported/KominkaDogRun/Animations/` | おじいさんの Animator |
| CE の UdonProduct 6 本 (`KD_Product_CandyStep` / `KominkaLivePack` / `PlushSet` / `PumpkinParade` / `RoyalSet` / `SummerSet`) | `Assets/KominkaHillside/Data/CE/` | 同じ Product / Listing ID を参照する |

### 新規

| 区分 | ファイル | 内容 |
|---|---|---|
| 新規 partial | `Imported/KominkaDogRun/Editor/KD_ShopSetupEditor.Hillside.cs` | `Menu/4 Setup Shop` / `4a Verify Shop` の裏山版。`KH_Dogs/KD_ShopCatalog` + `KD_ShopItem_*` 50 行 (= `KD_ProductDefinitions.All`、全行 menuOnly で Collider・描画・棚・listener なし)、`BindCatalog` (ledger.products / categoryKeys / defaults / CE の UdonProduct)、`KH_Dogs/KD_PlayerFxTag`、文言表 (`displayNameKey` / `displayName`。古民家ではシーンにしか無かった)、購入音の口 `HillsideBuyClip()` / `HillsideRejectClip()` |
| 新規 partial | `Imported/KominkaDogRun/Editor/KD_ShopKeeperSetupEditor.Hillside.cs` | `Menu/4d Setup Shop Keeper` / `4e Verify Shop Keeper`。おじいさんの配置 (初回だけ)、man_farm と `AC_KD_Npc`、ナビ 5 項目の表 |
| 新規 script | `scripts/check-consume-once.js` | 古民家から移植。`Imported/KominkaDogRun/Scripts` と `Assets/KominkaHillside/Scripts` を見て、`_TryConsume` の呼び出しが 1 か所かを確かめる |

### 裏山改変の一覧

担当は P1 = 台帳・カタログ、P2 = おじいさん、P3 = 板・ようこそ、司 = 司令塔。

| ファイル | 何を | 理由 | 担当 |
|---|---|---|---|
| `Scripts/Progress/KD_ProgressLedger.cs` | 名前空間 `KH_Commerce_v1` / テスト `KH_TestCommerce_v1`。`initialPoints` 既定 0 (0 以下なら `Bonus.initial` もフラグも書かない)、`keeperPoints` 既定 200。Grant の口と「フラグ true → Earned 加算」の順は不変 | 付与はおじいさんの 200pt 一本 (game-design §4)。古民家の保存データと区別する | P1 |
| `Editor/KD_ProgressSetupEditor.cs` | Setup / Verify を `Menu/3a` / `3b` へ付け替え。`KH_Dogs/KD_ProgressSystem`、SyncMode None、名前空間・initialPoints 0・keeperPoints 200 を書く | 裏山のシーン構成 | P1 |
| `Editor/KD_ShopSetupEditor.Setup.cs` / `.Verify.cs` | 古民家版を `Menu/Imported-Unused/Shop/2・3 (Kominka)` へ | 裏山に棚は無く、購入はメニュー | P1 |
| `Editor/KD_ShopSetupEditor.Ui.cs` | `EnsurePlayerFxTag` に親の引数 (`KH_Dogs`) を足す。既存なら親も位置も触らない | 裏山は `KD_Systems` を作らない | P1 |
| `Scripts/KD_ShopKeeper.cs` | ナビ 5 項目 (過ごし方 / さんぽ (新 `_OpenWalk`) / おやつ / 呼ぶ / またね)。古民家だけの `_OpenHowTo` / `_OpenPetting` / `_OpenDogs` / `_OpenPhotos` / `_OpenShop` は残して navIndex −1。挨拶 2 種に「支度は手元メニューの「かざり」で」+ 端末別の開き方。初回は「{0}pt だ。一度きりだよ」。`_TryGift` (復元待ち 1 秒再試行) は不変 | DES-UI-003 のナビと導線 | P2 |
| `Editor/KD_ShopKeeperSetupEditor.cs` | partial 化し、`BuildMenu` と Verify の核を裏山のナビ表に置き換えた (古民家版の入口 `Imported-Unused/Shop Keeper/2・3` も 5 項目で組む) | 裏山専用の入口を別に作ると二重管理になる | P2 |
| `Scripts/KD_TreatPouch.Hillside.cs` | ledger 配線時にヘッダの Balance を出す。`hillsidePoints*`、`_HillsidePoints` / `_HillsidePointsBack`、`HillsideRedrawPoints` (「いま N pt」+ 入手経路 2 行、未復元は「読み込み中...」)、`HillsideGrantLineVisible()` と準備中の行のサブ文への `WordGrantLine` | 残高・ポイント面・未受取の案内 (D-5) | P3 |
| `Scripts/KD_TreatPouch.Redraw.cs` | 設定の枠の中で `HillsideRedrawPoints()` を呼ぶ 1 行 | 呼び出し口だけ (D-3) | P3 |
| `Editor/KD_TreatSetupEditor.Hillside.cs` | `KD_Hillside.Stage = 1`、`HillsideFindStage1` (ledger / catalog / treat 4 / petfx 11。無ければ FAIL で書かない)、購入音の配線、`HillsideBuildPoints` (PointsButton、`Page_Settings/PointsPage`、ギフトコードの器を作らず残骸 `GiftCodePage` を削除)、段階 1 で `KD_WelcomeGuideEditor.BuildPage` を呼ぶ | 段階表 D-1 を 1 へ | P3 |
| `Editor/KD_TreatSetupEditor.Pouch.cs` | 効果音の NOTE の文言 (棚が無いので SetupHillside が配線する旨) | 段階 1 の配線元の変更 | P3 |
| `Editor/KD_TreatSetupEditor.Verify.cs` | Verify 20 の段階表を 1 に (treat 4 必須、petfx 11 枠で null は WARN)、ledger / catalog が `KH_Dogs` 直下、`KD_ShopKeeper` 1 個、購入音、Balance と PointsButton、ポイント面 2 行 + [戻る]、ギフトコード無し (`KD_GiftCodePanel` / `GiftCodePage` / `TMP_InputField` が 0)、welcome page・hints・welcomeSpot。棚は SKIP 明示 | 同上 | P3 |
| `Editor/KD_WelcomeGuideEditor.cs` | 古民家の Build / Verify を `Imported-Unused/Welcome Guide/1・2 (Kominka)` へ。新 `BuildPage`、`Menu/11b` / `11c`。`KH_Dogs/KD_WelcomeSpot` (新規時だけ `KH_Entrance` の前 1.8 m・高さ 1.3 m)、`KH_Dogs/KD_HintDirector` (sync None、`sleepNoticeEnabled = false`、保存キー `KD_Onboarding_v1`) | ようこそ の最小版を段階 1 へ前倒し (D-5) | P3 |
| `Data/UILocalizationBindings.json` | `Page_Welcome` の 4 束縛、`KD_ShopKeeperMenu` の 6 束縛、interactions 1 を追加 (labels 118 / interactions 1) | 新しい板の文言 | P3 |
| `Data/UITranslations.json` | 差替 15 (P2 7 + P3 8)、新規 7 (P2 2 + P3 5)。13 言語、計 664 キー。`welcome.*` の既存キーは差替 (再読の行は段階 3 まで無いので除去) | おじいさんのナビ・挨拶、ポイント面、未受取の案内、ようこそ | 司 (P2 / P3 の断片を統合) |

### MenuItem の変更

| メニュー | 変更 |
|---|---|
| `Tools/KominkaHillside/Menu/3a Setup Progress` / `3b Verify Progress` | `Menu/Imported-Unused/Progress System/2・3` から付け替え (裏山改変) |
| `Menu/4 Setup Shop` / `4a Verify Shop` | 裏山版 (`KD_ShopSetupEditor.Hillside.cs`) に。古民家版は `Menu/Imported-Unused/Shop/2 - Setup Shop (Kominka)` / `3 - Verify Shop (Kominka)` へ |
| `Menu/4d Setup Shop Keeper` / `4e Verify Shop Keeper` | 新設 (`KD_ShopKeeperSetupEditor.Hillside.cs`) |
| `Menu/11b Build Welcome` / `11c Verify Welcome` | 新設 (`KD_WelcomeGuideEditor.cs`)。古民家版は `Menu/Imported-Unused/Welcome Guide/1 - Build (Kominka)` / `2 - Verify (Kominka)` へ |
| `Menu/7 Build Menu` / `20 Verify Pouch` | 段階 1 の分岐と検査を追加 (入口の名前は不変) |

## 2026-09-24 おやつ・かざり・撫でFX・呼ぶ (段階 2)

[メニュー再構築計画](../../plans/2026-09-23-menu-rebuild.md) D-2・D-5・D-7・D-8 の段階 2 ([IMP-DOG-014](../issues/IMP-DOG-014-treats-port.md) / [IMP-DOG-015](../issues/IMP-DOG-015-accessories-and-petfx-port.md) / [IMP-DOG-016](../issues/IMP-DOG-016-call-pose-port-and-slope.md))。古民家のおやつ・飾り・撫でFX・呼ぶ・ポーズ・場所決めを裏山のシーンに置き、手元メニューの準備中を外した。実行順・Verify の数値・首輪干渉と斜面の実測・未確認の点は計画書の「実装記録 / 段階 2」が正本。ここには**古民家の移植元から何を変えたか**と、資産のコピーだけを残す。移植元の SHA は段階 0-a と同じ (`44505384` 相当)。

状態: Verify 全 PASS、シーン保存済み。Codex レビュー (段階 3 と合わせて実行中)・コミット・Play (ClientSim・実機) は未了。

### 資産のコピー (司令塔、素材の確認記録)

古民家からコピーした資産は 315 ファイル (`.meta` を除く)。**GUID は古民家と同じ**。置き場は `Assets/KominkaHillside/Imported/KominkaDogRun/` の下。

| 置き場 | 件数 | 中身 | 使う Issue |
|---|---|---|---|
| `Models/Treat/` | 26 | おやつ 4 種 (BoneCookie / PawCookie / StarCookie / Jerky) の FBX とテクスチャ、籠 (`woven+basket+3d+model/tripo_convert_…`) | 014 |
| `Models/SleepPlacement/` | 12 | 犬 6 匹の場所決めマーカー (asset + mat)。赤マーク `Invalid.mat` を含む (IMP-DOG-032 で未使用) | 016 |
| `Models/Accessories/` | +58 (計 70) | 飾りの FBX・材質・テクスチャ (BlenderOptimized / Everyday / ThemeSets) | 015 |
| `Models/DogPreviews/` | 159 | 古民家の焼き済みミニチュア。裏山は Menu/18・6j で首輪込みに焼き直した | 015 / 016 |
| `Prefabs/PetFx/` | 10 | 撫でFX の prefab (同梱 5 種 × 2 層) | 015 |
| `Data/SE/` | +7 | おやつ・購入などの効果音。作業ツリーで未追跡なのは `Cat/` の 2 本 (Firefly 生成の猫の声) | 014 / 015 |
| `Textures/Particle/` | 13 | 撫でFX・おやつ FX の粒 (Heart / GoldPaw / SakuraHeart / Candy / Pumpkin / Lantern / Meteor / Confetti / Bat ほか) | 014 / 015 |
| `Shaders/` | 7 | `KD_NightOverlay` / `KD_Photo*` 4 本 / `KD_PosterBoardTransparent` / `KD_PumpkinPalette` | 015 (材質の参照) |
| `Materials/` | 23 | Treat 4、TreatFx の png 4 + mat 4、TreatPlate 1、PetFx の Candy / Confetti / Lantern / Meteor / Pumpkin と各 `_L2` (10) | 014 / 015 |

素材の確認記録 ([共通の型](../../AI_INDEX.md#素材の確認記録)):

| 項目 | 記録 |
|---|---|
| 素材名・成果物 | 上表の 315 ファイル。版は古民家 `44505384` 相当の作業ツリーにあったもの (GUID 同一) |
| 参照区分・参照元 | 移植。参照元は `C:/work/unity/vcc/KominkaDogRun` (Kominka 自作 = ユーザー所有) |
| 予定用途 | 裏山ワールドへの組み込み (古民家と同じ用途: おやつ・飾り・撫でFX・呼ぶのミニチュア) |
| 利用条件の根拠 | ユーザー所有の自作物を、同じユーザーの別ワールドで使う。AI 生成のものは古民家の記録に従う: Tripo 生成の 3D は Pro プランで商用可・証憑取得済み (2026-08-22。古民家 `docs/development/history/2026-09-08-ai-index.md`)、Firefly 生成の音は商用可・クレジット不要 (古民家 `docs/development/issues/archive/IMP-DOG-015-panting-breath.md`) |
| 確認日 | 2026-09-24 |
| 生成ツール・加工 | 生成履歴は古民家の記録のまま。裏山での加工は、ミニチュアの焼き直し (首輪込み) だけ |
| 採用前確認 | 司令塔、2026-09-24。Verify 6d / 6f / 6h / 6b / 25 (1661/0) / 20 が PASS し、資産の欠けは 0 (014・015 の報告)。見た目の確認は PCVR 待ち |
| 状態 | 確認済み (上の用途・条件について)。未解決: `Data/SE/Cat/` の 2 本と `Shaders/` の写真系 4 本は、裏山の機能では使わない。コピーの範囲に入ったもので、要否は未確認 |

### 新規

| 区分 | ファイル | 内容 |
|---|---|---|
| 新規 partial | `Imported/KominkaDogRun/Editor/KD_TreatSetupEditor.Treats.cs` | `Menu/6c Setup Treats` / `6d Verify Treats`。`KD_TreatHome_0..2` / `KD_Treat_0..2` / `KD_TreatSlot_0..2` / `KD_TreatBox` (Manual) / `KD_TreatFx` を `KH_Dogs` 直下に置く。[買う] の生成 `HillsideBuildTreatBuy`、`TreatBuyButtonName` / `TreatBuyEventOf` |
| 新規 partial | `Imported/KominkaDogRun/Editor/KD_AccessorySetupEditor.Hillside.cs` | `Menu/6e Setup Accessories` / `6f Verify Accessories`。24 品 × 6 匹の見た目ノード、`KH_Dogs/KD_AccessorySystem`、首輪干渉表と (a) の自動適用 |
| 新規データ | `Imported/KominkaDogRun/Editor/KD_AccessoryFits.Kominka.json` | 古民家シーンの飾りノード 144 個の実測値。Editor フォルダなのでビルドに入らない |
| 新規 partial | `Imported/KominkaDogRun/Editor/KD_AccessorySetsEditor.Hillside.cs` | `HillsideWire`: 商品を書き換えず、台帳の商品参照・セット表・一覧を配線する |
| 新規 partial | `Imported/KominkaDogRun/Editor/KD_BundledPetFxEditor.Hillside.cs` | `Menu/6g Setup Pet Fx` / `6h Verify Pet Fx`。同梱 5 種 × 2 層、`KH_Dogs/KD_PetFxBox` (None)、petControllers の往復、`catalog.consumers`、`pouch.petFxBox` |
| 新規 partial | `Imported/KominkaDogRun/Editor/KD_DogPreviewEditor.Hillside.cs` | 焼き済みのミニチュアとの比較 |

### 裏山改変の一覧

担当は 014 = おやつ、015 = かざり・撫でFX・ミニチュア、016 = 呼ぶ・ポーズ・場所決め、統 = 統合担当、司 = 司令塔。

| ファイル | 何を | 理由 | 担当 |
|---|---|---|---|
| `Scripts/KD_TreatBox.cs` | `HillsideDogBusy`: リード中 (`KH_LeashManager._IsLeashed`)・集合中 (`_IsGathering`)・フリスビー中 (Ready を含む) を `EligibleDog` と `AllDogsEngaged` から外す。IMP-DOG-006 の差し込み口をコメントで示す | 裏山の拘束状態 (IMP-DOG-014) | 014 |
| `Scripts/KD_TreatPouch.Treat.cs` | [買う] `_TreatBuy0..3` → kind `ConfirmTreat` (6) → `_TreatConfirmBuy` → `catalog._RequestBuy` → `ledger._TryBuyStocked` (5 / 上限 20)。`_TreatConfirmNo`、行のヒント、カードの 3 態、結果は状態行に 4 秒、未受取の案内も状態行 | 古民家は棚で買う。裏山は棚が無いのでメニューで買う (DES-UI-003) | 014 |
| `Editor/KD_TreatFxSetupEditor.cs` | `containerPath` 引数 | FX を `KH_Dogs` 直下に置く | 014 |
| `Scripts/KD_TreatPouch.PetFx.cs` | `RedrawFxRows(visible)`、`HillsideAccCatalogLevel` / `HillsideFxFeedbackVisible` / `HillsideAccFooter`、群見出し `hillsideFxHeadRows` / `hillsideFxHeadLabel`、Equip はカタログ階層だけ | 撫でFX を かざりへ合流 (D-2) | 015 |
| `Scripts/KD_TreatPouch.Acc.cs` | `RedrawAcc` の末尾で、カタログ階層のときだけ `RedrawFxRows`。確認カード kind Fx、戻るは `_AccBack`。CE セットの [ギフト] は `HillsideAccGiftEnabled = false` | 同上。ギフトは裏山で未決定 | 015 |
| `Editor/KD_AccessorySetupEditor.cs` / `KD_AccessorySetsEditor.cs` / `KD_BundledPetFxEditor.cs` / `KD_DogPreviewEditor.cs` | partial 化 (裏山の差分は `.Hillside.cs`)。Verify 25 に「飾りのメッシュは 1 つを共有」の検査 | 裏山のシーン構成 | 015 |
| `Scripts/KD_SleepBoard.cs` | `hillsideLeashes`、読むだけの API `_IsCalled` / `_IsLeashedDog` / `_IsGatheringDog` / `_IsWalkBusy` | リード・集合との排他 (IMP-DOG-016 変更 1) | 016 |
| `Scripts/KD_SleepSlot.cs` | `HillsideMinGroundNormal = 0.9`、layer 0 / 11、probe 0.5 m、高さの許容 0.1 m。`_PlacementGroundOk` (真下へのレイキャスト)。`ValidPlacement` は「場所を選んで呼ぶ」の確定だけ。予約・確定・`_BeginSelection` はリード中・集合中を拒否。Watch: 同時ならリードが勝つ | 古民家の `ValidPlacement` は NaN / Inf だけを見る (変更 2)。**IMP-DOG-032 (2026-10-03) で斜面の定数・`_PlacementGroundOk` を外し古民家どおりに戻した** (リード・集合の拒否と Watch は残る) | 016 / 032 |
| `Scripts/KD_SleepPlacement.cs` | ~~マーカーを地面へ落とす。無効は赤マーク + 文言。3 経路とも確定不可 (`invalidMarks`)~~ → **IMP-DOG-032 で古民家と同一に戻した** (差分なし) | 同上 | 016 / 032 |
| `Scripts/KD_DogPack.cs` | 押し出しは `PlacedSleepHeld` の犬を押さない (洗い場の台の `StationLocked` と同じ扱い) | 古民家は置いて寝ている犬も押して接地させる。古民家の置き場所は床なので目立たなかったが、手の高さに置けると落ちる | 032 |
| `Scripts/KD_CallPose.cs` | リード中・集合中は開かない。開いている間にリードが付いたら操作不可 + 理由 | 変更 1 | 016 |
| `Scripts/KD_TreatPouch.Sleep.cs` | 行の状態 `SleepLeashed` / `SleepGathering` | 変更 1 | 016 |
| `Scripts/KD_DogWanderController.Sleep.cs` | `SleepStep` = `GatherStep` (NavMesh + 接地)。失敗したら旧 `StepToward`。~~最後の据わりも接地~~ (IMP-DOG-032 で外した。古民家どおり置いた高さへ浮く)。`PlacedSleepHeld` (置いた点へ収まる途中・寝ている間) を足した | 古民家は接地スナップが無い (変更 3)。近づく足だけ地形追従を残す | 016 / 032 |
| `Editor/KD_SleepSetupEditor.cs` | 6a / 6 / 6b を裏山向けに。`KH_Dogs/KD_SleepBoard` + `KD_SleepSlot` ×6、6 匹の wander / fetch、`KD_LiveAccess`、`pouch.sleepBoard`、`KH_LeashManager.sleepBoard` | 裏山のシーン構成 | 016 |
| `Editor/KD_SleepPlacementEditor.cs` | Menu/6i。`KH_Dogs/KD_SleepPlacement`。IMP-DOG-032 で赤マークを作らず、残っていれば消す (Verify は残りが無いことを見る) | 同上 | 016 / 032 |
| `Editor/KD_CallPoseEditor.cs` | Menu/6j。`KH_Dogs/KD_CallPose`。シーンのバックアップ保存をやめた。7 状態がそろった animator は作り直さない。焼くとき `KH_*` (首輪) を隠す。`KD_ShopItem_Action_*` の 4 行だけ配線 | 同上 | 016 |
| `Scripts/KD_TreatPouch.Confirm.cs` / `.Redraw.cs` / `.Hillside.cs` / `.Toy.cs` / `.Walk.cs` | kind Treat の振り分け、`RedrawTreat` の末尾で裏山の描画、準備中の分岐を段階 2 で外す、おもちゃの対象から寝ている犬を外す (古民家の `∧ !sleeping`)、さんぽの理由 (呼ばれて待機中)。`RedrawFx()` (66 行) を削除 | 3 本の patch の統合 | 統 |
| `Editor/KD_TreatSetupEditor.Hillside.cs` | `KD_Hillside.Stage = 2`。`HillsideFindStage2` が 6 参照 (box / accessories / petFxBox / sleepBoard / sleepPlacement / callPose) を探し、欠ければ pouch に触れる前に止める。配線は box → かざり・撫でFX → 呼ぶ の 1 ブロック。[買う] と群見出し `FxHead0/1` を作る | 段階表 D-1 を 2 へ | 統 |
| `Editor/KD_TreatSetupEditor.Verify.cs` | Verify 20 の段階表 2 (6 参照・[買う] 4 本・FX の onClick・試着ミニチュア)。SKIP 3 行を 1 つの if/else に。「TMP_InputField が無い」を「Page_Settings の外に無い」に | 同上。段階 3 の入力欄は正規 | 統 / 司 |
| `Editor/KD_HandMenuScrollEditor.cs` | おやつ行に [出す] と [買う]、`ExpectedRowEvent` に `_TreatBuy{i}`、かざりの行の表に群見出し | Menu/21 の検査 | 統 |
| `Editor/KD_DogPreviewEditor.cs` (Verify 25) | 確認カードの検査を P13 以降の共有カード (暗幕 / クリック遮断で listener 0 / 文言は実行時に書く) に合わせた | 古民家の Verify 25 は P13 以前の形で、古民家でも FAIL になる | 司 |
| `Editor/KD_ShopSetupEditor.Hillside.cs` | Menu/4 を流し直しても `catalog.consumers` の `KD_PetFxBox` を消さない | 段階 2 の後に Menu/4 を流すと、撫でFX の選択が犬に届かなくなるため | 統 |
| `Scripts/KH_LeashManager.cs` (裏山の自作) | `sleepBoard`。呼ばれた犬への `ReqAttach` を拒否 | 呼ばれて待機中の犬はつなげない (変更 1 の逆方向) | 統 |
| `Scripts/Editor/KH_DogWalkingEditor.cs` (裏山の自作) | Walking/3 が sleepBoard / companionSlot を null に戻さない (Walking/5 の FAIL を直した) | 同上 | 統 |
| `Data/UILocalizationBindings.json` | ポーズ面・場所決めの固定ラベル 18 行を戻した (0-b で外したもの) | 器ができたため。Bind の順序は計画書「Bind の循環依存」 | 統 |
| `Data/UITranslations.json` | 新規 6 (`WordTreatBuyCancelled` / `WordFxGroup` / `WordSleepLeashed` / `WordSleepNeedFlat` / `WordWalkStateCalled` / `WordWalkGateCalled`) | 裏山の文言 | 司 |

### MenuItem の変更

| メニュー | 変更 |
|---|---|
| `Tools/KominkaHillside/Menu/6c Setup Treats` / `6d Verify Treats` | 新設 (`KD_TreatSetupEditor.Treats.cs`) |
| `Menu/6e Setup Accessories` / `6f Verify Accessories` | 新設 (`KD_AccessorySetupEditor.Hillside.cs`) |
| `Menu/6g Setup Pet Fx` / `6h Verify Pet Fx` | 新設 (`KD_BundledPetFxEditor.Hillside.cs`) |
| `Menu/6a Create Sleep Programs` / `6 Setup Sleep` / `6b Verify Sleep System` | 裏山向けに書き換え (`KD_SleepSetupEditor.cs`) |
| `Menu/6i Setup Sleep Placement` / `6j Setup Call Poses` | 裏山向け (`KD_SleepPlacementEditor.cs` / `KD_CallPoseEditor.cs`) |
| `Menu/7 Build Menu` / `20 Verify Pouch` / `25 Verify Dog Previews` | 段階 2 の分岐と検査を追加 (入口の名前は不変) |

## 2026-09-24 設定面の残りとようこそ (段階 3)

[メニュー再構築計画](../../plans/2026-09-23-menu-rebuild.md) D-5 の「設定面 (残り)」と「ようこそ」([IMP-UI-001](../issues/IMP-UI-001-menu-table-driven-and-settings.md) 段階 3)。実行順・Verify の数値は計画書の「実装記録 / 段階 3」が正本。移植元の SHA は段階 0-a と同じ (`44505384` 相当)。新しい資産のコピーは無い。

状態: Verify 全 PASS、シーン保存済み。Codex レビュー (段階 2 と合わせて実行中)・コミット・PCVR は未了。

### 新規

| 区分 | ファイル | 内容 |
|---|---|---|
| 新規 partial | `Imported/KominkaDogRun/Editor/KD_MenuFinishEditor.Hillside.cs` | 設定ホームの配置の正本。群見出し 3 (`settings.group.self` / `room` / `read`)、クレジット頁 `Page_Settings/CreditPage` の 7 行 (`HillsideCredits`。URL 3 件は未記入)、インフォメーション 4 行 (はじめに / 生成AI / VAnimal / トレーナー)、`VerifyHillsideSettings` |
| 新規 partial | `Imported/KominkaDogRun/Editor/KD_DistanceFadeSettingsEditor.Hillside.cs` | `Menu/14 Setup Distance Fade` / `14c Verify`。系は `KH_Dogs` 直下、猫なし |
| 新規 partial | `Imported/KominkaDogRun/Editor/KD_DogVoiceSettingsEditor.Hillside.cs` | `Menu/16 Setup Volume` / `16a Verify Dog Voice`。音量頁は「外」の 1 群 = 犬の声のスライダー + 値 + 注記 + [戻る]。音源は `KH_Dogs` の bark / breath / joy |

### 裏山改変の一覧

担当は U1 = 設定面、U2 = ようこそ・インフォメーション、司 = 司令塔。

| ファイル | 何を | 理由 | 担当 |
|---|---|---|---|
| `Scripts/KD_DistanceFadeSettings.cs` | PlayerData のキーを `KD_DistanceFade_v1` → `KH_DistanceFade_v1`。Android / iOS は Start で入口を `interactable = false` | 古民家の保存値と分ける。距離フェードは PC のみ (DES-UI-003) | U1 |
| `Scripts/KD_MenuSettings.cs` | `creditPage` / `creditScroll` / `_OpenCredits`、見出し `ui.extra.022` | クレジットを板でなく設定面の頁に置く | U1 |
| `Editor/KD_MenuFinishEditor.cs` | partial 化。BuildSettings: 0-b の入口の非表示をやめ、写真設定を作らず、ナイトモード・言語・許可を 3 群へ動かし、末尾で `BuildHillsideSettings`。BuildInformation: 写真設定なし。Verify 22 の SKIP を解除 | 設定面の残り (段階 3) | U1 |
| `Editor/KD_DistanceFadeSettingsEditor.cs` / `KD_DogVoiceSettingsEditor.cs` | partial 化。古民家版の 14 / 14c / 16 / 16a は `Menu/Imported-Unused/Distance Fade/…` / `Volume/…` へ | 裏山版を `.Hillside.cs` に隔離 | U1 |
| `Editor/KD_WelcomeGuideEditor.Hillside.cs` | 11b ははじめにの面を作らない (Menu/11 が作る)。`KD_SleepSlot` 6 枠で `sleepNoticeEnabled = true`。11c に はじめにの面・通知・フッタ 2 行・本文の群と行 (写真・ライブ・ギフトコード・縁側・庭のおじいさんが無い)・`VerifyHillsideFit` (フッタ 1060×60、本文は表示枠 276 の 2 倍以内) | ようこその全文 (段階 3) | U2 |
| `Data/UITranslations.json` | U1 新規 7 (群見出し 3・`settings.volume.scope`・`credit.environment.kit` / `credit.sky` / `credit.keeper`)、U2 差替 7 (`welcome.body`、`welcome.footer.vr` / `.desktop` / `.touch` (2 行目の再読の案内を戻す)、`welcome.reread.body`、`information.vanimal.body`、`information.trainer.body`)。段階 2 と合わせて 13 言語 677 キー | 裏山の文言 | 司 (U1 / U2 の断片を統合) |
| `Data/UILocalizationBindings.json` | U1: 追加 27 (群見出し 3・CreditButton・距離フェード 14・クレジット 9)、削除 5 (音量頁の寝室・ライブの部品)、差替 1 (`VolumeScope` → `settings.volume.scope`)。U2: インフォメーション系 15 行を戻した | 部品の増減に合わせる。無い部品への束縛は Bind が例外で止まる | U1 / U2 |

- **犬の声**: 6 匹の `KD_BarkSource` / `KD_BreathSource` はシーンで mute のまま焼かれていた。pouch の dogVoiceSources が 0 本だったので、実行時にも解除されず鳴っていなかった。16 の配線後は、古民家と同じく `KD_TreatPouch.Start` が 50% で解除する。`KD_PetSystem_*` の joyAudioSource は 6 匹とも null (古民家の構築も配っていない)。

### MenuItem の変更

| メニュー | 変更 |
|---|---|
| `Tools/KominkaHillside/Menu/14 Setup Distance Fade` / `14c Verify Distance Fade` | 裏山版 (`KD_DistanceFadeSettingsEditor.Hillside.cs`)。古民家版は `Menu/Imported-Unused/Distance Fade/…` へ |
| `Menu/16 Setup Volume` / `16a Verify Dog Voice` | 裏山版 (`KD_DogVoiceSettingsEditor.Hillside.cs`)。古民家版は `Menu/Imported-Unused/Volume/…` へ |
| `Menu/11 Setup Localization` / `22 Verify Localization` | 設定面の残り (入口 4 つ・音量頁・距離フェード・クレジット・インフォメーション・群見出し) をまとめて作り、裏山の値で検査する (入口の名前は不変) |
| `Menu/11b Build Welcome` / `11c Verify Welcome` | 全文の検査を追加 (入口の名前は不変) |

## 2026-10-03 おじいさんの「さんぽ」の行き先 (BUG-UI-001)

| ファイル | 裏山の変更 | 理由 |
|---|---|---|
| `Scripts/KD_ShopKeeper.cs` | `InfoWalk` の行き先 2 行「行き先は「広場」か / 「古民家側の入口」だ。」を 4 行「行き先は6つから選べる。/「広場」「奥の広場」「川辺」/ と、ぐるっと回る周回が2つ、/ それに「古民家側の入口」だ。」へ (1 行 14 字以内) | 周回コース (2026-09-27) 以降の手元メニューの 6 目的地に合わせる |
| `Data/UITranslations.json` | `KD_ShopKeeper.InfoWalk` の ja を上の const と同じ文へ。他 12 言語 (英文のまま) の「Choose the destination: Plaza or Farmhouse-side gate.」を「Choose from 6 destinations: Plaza, Upper clearing, Riverbank, two loop walks, or Farmhouse-side gate.」へ | 表示の正本は文言表 (const は ja の写し。Shop Keeper の Verify が一致を見る) |

### 2026-10-07 空間音設定の補正

移植側の `KD_WanderSetupEditor`、`KD_ShopKeeperSetupEditor`、`KD_TreatFxSetupEditor`、`KD_TreatSetupEditor.Pouch`、`KD_LiveSetupEditor.Hillside` に裏山の `KH_AudioSpatialEditor.Configure` 呼び出しを追加。犬の既存VRC距離を保持し、生成時にもUnity側と整合する。店主・おやつ演出は有限距離の3D、ポーチ操作音・ライブは既存2Dを維持。購入元の素材・プレハブ・古民家ドッグランの作業ツリーは変更していない。設定値と検証範囲は[川音制作票 §9](../../plans/2026-10-02-river-ambience-production.md#9-全音源の空間音設定見直し-2026-10-07)。