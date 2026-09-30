---
id: IMP-ENV-010
type: IMP
system: ENV
status: wip
priority: High
summary: Quest 3 単体で 10〜20 fps・地形が光る (2026-09-30 実機報告)。Android/iOS ビルドだけ地形を Lite シェーダーへ差し替え、群ごとの ON/OFF プローブで寄与を実機で測る
next: 殻 1 パス版を Android でビルド → Quest 3 で犬を見ている状態の fps と地形の光りを確認 → 犬 Udon の間引きをやるか決める (ユーザー)
updated: 2026-09-30
---

# IMP-ENV-010 Quest 単体の負荷を切り分け、地形の光りを直す

**2026-09-30 ユーザー実機報告 (Quest 3 単体): 入った瞬間 10〜20 fps。地形が変に光る。** 人数・視点・アバター・ビルド版は未記録。同日 PC 側の構成を Unity から集計して原因候補を出した (下の §1)。[VER-WLD-001](VER-WLD-001-quest-ios-devices.md) 項目 6 (負荷) の初回値であり、この Issue で直す。

方針: **PC の見た目・挙動は変えない** (承認済みの川沿い周回版)。Android/iOS のビルド時だけ差し替える (古民家 IMP-INF-022 の `KD_AndroidToonMaterialsEditor` と同じ型)。「重くなりそう」で削らず、Quest で群ごとに ON/OFF して寄与を測ってから次を決める (`vrc-performance` 手順 4)。

## 1. 構成の集計 (2026-09-30、Editor の一時監査。Quest の割合はこれからは出ない)

| 群 | 実測値 | Quest への読み |
|---|---|---|
| 地形 | Terrain 20 枚 (120×180 m、heightmap 257、alphamap 256、4 レイヤー)。全部 `Nature/Terrain/Standard` (既定)。`basemapDistance=1000`、`heightmapPixelError=8`、`drawInstanced=1`、影 TwoSided (影自体は無し) | 画面の大半を占める地形が両眼で PBR 4 レイヤー + 反射サンプル。遠景も簡略化されない。**光る原因の第一候補** (下) |
| 犬・Udon | UdonBehaviour 198 個稼働、Animator 7、SkinnedMesh 19。毛は lilToon FurTwoPass 4 / Uzumore FurTwoPass 6 材質 | 古民家 (同じ 6 匹、室内) が Quest 3 で 30〜40 fps だった ([IMP-INF-022](../../../../KominkaDogRun/docs/development/issues/IMP-INF-022-android-runtime-performance.md))。毛は geometry 非対応で描かれず Fallback。犬側は描画より Udon の CPU |
| 木・葉 | 有効 Renderer 628 (LOD0 の三角 65 万、Terrain 別)。`KominkaHillside/Foliage Lite` 99 個 27 万 tri、LMArt `NatureLeaves` 62 個 12 万 tri。どちらも Cull Off + alpha clip | 重ね描き (フィルレート)。Quest が弱い部分 |
| 草 | 入口タイル `KH_Terrain` のみ GrassBillboard 5 種 (detail 256、距離 30 m) | 入口だけ |
| 川・粒子 | `River Lite` 24 個、ParticleSystem 120 個 (大半は停止中) | 小 |
| 照明 | Directional 1 灯 (Baked 指定だがベイク無し → 実質リアルタイム)、影なし、ライトマップ 0、ポストプロセス・RT カメラ・GrabPass なし | 影・PP は無い |

### 地形が光る理由 (仮説 2 つ。原因は未確定)

仮説 A (鏡面の経路): `Nature/Terrain/Standard` は滑らかさを「拡散テクスチャの alpha × レイヤーの Smoothness」で決める ([TerrainSplatmapCommon.cginc](file:///C:/Program%20Files/Unity/Hub/Editor/2022.3.22f1/Editor/Data/CGIncludes/TerrainSplatmapCommon.cginc) `SplatmapMix`)。レイヤーの Smoothness は 4 枚とも 0 なので、この式どおりなら PC でも Android でも光らないはず。Android の上書き形式 (ASTC_6x6 / ETC2_RGBA8) は alpha チャンネルを持つが、`alphaSource=None` なので元画像の alpha は入らない (alpha=1)。**圧縮形式の違いだけでは説明できない**。Android の実行時に Smoothness の扱いが変わる (テクスチャに alpha があると Smoothness を無視する等) 可能性を疑っている段階。
仮説 B (法線の経路): Draw Instanced 地形は法線を `_TerrainNormalmapTexture` から読む。Adreno でこのテクスチャの形式・サンプリングが崩れると陰影が壊れて光って見える。

**Lite 化で消えるのは仮説 A だけ。** Lite も `drawInstanced` と法線テクスチャは使い続ける。Quest で Lite にしても光るなら仮説 B → ビルドフックで `drawInstanced=false` にして再確認 (`KH_MobileTerrainBuildEditor` の定数 1 つ)。

## 2. やること

1. `KominkaHillside/Terrain Lite` シェーダー (4 レイヤー splat + Lambert 1 灯 + SH 環境光 + fog、鏡面なし、ベースマップ pass 付き) と `KH_TerrainLite.mat`
2. `KH_MobileTerrainBuildEditor` (IProcessSceneWithReport): **Android/iOS ビルドのシーンコピーだけ** 20 枚の `materialTemplate` を Lite へ、`heightmapPixelError` 8→20、`basemapDistance` 1000→60、`detailObjectDistance` 30→20 に変更。PC・Editor・Play は対象外。ログを `Temp/kh-mobile-terrain-build.txt` へ
3. Editor 確認用メニュー `Tools/KominkaHillside/Mobile/Preview Terrain Lite (toggle)` (見た目確認。保存しない)
4. **切替プローブ** `KH_PerfProbe` (ローカル専用、同期なし): スポーン右前の Use ボタン 7 個で 地形 (Terrain 20 枚) / 木・岩 (Scenery) / 葉 (Foliage Lite・NatureLeaves) / 入口タイル (Entrance = `KH_Terrain` の地表 + 草。**草だけは切れない**: Udon から `Terrain.drawTreesAndFoliage` に触れないため) / 犬 (Renderer) / 川 / 犬の Udon (enabled) を個別 ON/OFF。OFF 時に各要素の enabled を控え、ON で元へ戻す (元から無効のものを有効にしない)。VRChat のデバッグ表示で fps を読む。**公開前に外す** (完了条件)

やらないこと。PC の見た目の変更。植生・犬の削減 (測ってから)。容量 (別 Issue)。

### 実装記録 (2026-09-30)

- 上の 1〜4 を実装。C#/U# コンパイル 0 エラー。`Mobile/12` PASS、`Mobile/21` PASS (104 項目、バッキング配列の照合を含む)。Editor プレビュー (`Mobile/11`) で Lite の描画 (インスタンシング地形の高さ・スプラット・60 m 先のベースマップ) を撮影比較: `Temp/imp-env-010/standard-*.png` / `lite-*.png` (Standard よりやや暖色・少し明るい程度)。ClientSim Play で 7 トグルの enabled 切替を確認 (Terrain 20→0→20、犬 Udon 158→1→158、入口、犬の Renderer)。
- Codex 独立レビュー (必須 3 / 推奨 4) を反映: OFF→ON の復元を「全部 true」から「控えた状態へ戻す」に修正、Issue の「草 OFF」を「入口タイル + 草 OFF」へ、光る理由を仮説 A/B に分離、Verify にバッキングヒープの配列・group・entranceTerrain の照合を追加、プレビュー OFF が Lite 材質の正常性に依存しないよう修正、犬 Udon の測定条件を §4 に明記。「対象シーンをルート名で判定」は古民家で実ビルド確認済みの型を優先して据え置き (ログ `replaced=` で結果は見える)
- 未確認: Android 実ビルドでの GLES3/Vulkan シェーダーコンパイルとフック動作 (`Temp/kh-mobile-terrain-build.txt` に `replaced=20 remainingStandard=0`)、Quest 実機の見え方・fps

## 3. 完了条件

- [ ] Android/iOS ビルドで地形が Lite に差し替わる (ビルドログ `Temp/kh-mobile-terrain-build.txt` に `replaced=20 remainingStandard=0`)。PC ビルド・Editor は元のまま
- [ ] Quest 3 で地形の光りが消えた (ユーザー実機)。消えなければ仮説 B (`drawInstanced=false` で再確認)
- [ ] Quest 3 で群ごとの fps を記録した (基準 → 1 群 OFF → 戻す、の順。2 群同時 OFF で語らない)。記録先はこの Issue の §4
- [ ] 記録をもとに次の削減 (植生 / 犬 Udon / 入口の草) を決め、別 Issue か本 Issue の続きへ
- [ ] `Mobile/22 - Remove Perf Probe` で `KH_PerfProbe` を外し (死んだ NetworkIDs も落とす)、保存してから公開する

## 4. 実機記録 (ユーザー)

測り方: 1 人のインスタンスで、何も持たず、スポーンの位置・向きを揃えて VRChat のデバッグ表示 (fps) を読む。基準 → 1 群 OFF → 数字 → 同じ群を ON → 次の群。**犬 Udon は最後に**測る (OFF は Update を止めるだけで、ネットワークイベントは届く。Owner の更新が止まるので他の参加者にも影響する。ON に戻しても状態がずれることがあり、測ったら入り直す)。「犬 Udon OFF」の差は「犬の Udon 全体のコスト」ではなく「犬配下 157 個の Update/LateUpdate のコスト」。

| 日付 | 端末 / 人数 / 場所 | 基準 fps | 地形 OFF | 木・岩 OFF | 葉 OFF | 入口タイル + 草 OFF | 犬 OFF | 川 OFF | 犬 Udon OFF | 備考 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 2026-09-30 | Quest 3 単体 / 1 人 / スポーン | 10〜20 | ほぼ同じ | ほぼ同じ | ほぼ同じ | ほぼ同じ | ほぼ同じ | ほぼ同じ | **30〜40** | 犬 Udon OFF のまま犬を視界から外すと **60〜70**。地形の光りは未報告 (Lite 版で確認待ち) |

### 読み (2026-09-30)

- **環境 (地形・木・葉・川) は Quest の主因ではない**。地形の光りは別問題として Lite 版で確認
- **GPU の主因は洗浄の殻** (`KH_WashShell`、6 匹の 2 枚目 SkinnedMesh)。「犬 OFF」が効かなかったのは `KH_DogWash.EvaluateState` が 0.5 秒ごとに殻を再有効化するため (犬の体は消えていた)。犬 Udon OFF で殻の再有効化も止まり、視界から外すと 60〜70 fps → 殻 = 30〜40 → 60〜70 の差 (殻は汚れが少しでもあれば常時 ON。汚れは洗うまで消えない)。モバイル SubShader は 3 パス (泥 + 泡 2 層) で毎ピクセルにノイズ多数 → **1 パス・ノイズ 1 回の軽量版へ差し替え** (PC の SubShader は不変)。同日コミット
- **CPU の主因は犬の Udon** (10〜20 → 30〜40)。Editor (ClientSim、i7-14700KF、上限解除) で群ごとに `enabled=false` にして測った内訳 (基準 8.0 ms、6 サンプルの中央値、±0.3 ms の揺れ):
  - 犬配下の Udon 158 個すべて OFF: **−4.0 ms** (PC の 1 フレームの半分)
  - `KD_DogWanderController` ×6 だけ OFF: **−1.7 ms** (最大の単独項目)。`KD_DogPack` / `KD_DogStopAnchor` ×6: 0
  - 洗浄 ×6 / Fetch・フリスビー 25 個 / 撫で ×6 / リード 18 個 / メニュー等 90 個: それぞれ −0.3〜0 (揺れの範囲。合計しても −1 ms に満たず、残り 2 ms 強は多数の Behaviour の固定費か、徘徊を止めると他が軽くなる相互作用)
  - 徘徊の Update 内 (一時的な打ち切り点、10 サンプル): Update 全体 −1.4、うち先頭の `IsDogOwner`/`ObserveSpeed` 約 0.3、`WaterTick` 約 0.4、以降の割り込み連鎖 + `UpdateWander` 約 0.7、`LateUpdate` 約 0.6〜1.0。**1 か所の山ではなく毎フレームの tick が広く薄く積まれている**
  - Quest の CPU 単コアは PC の 5〜8 倍遅いとして、犬 Udon 4 ms → 20〜30 ms = 実機の 10〜20 → 30〜40 fps と整合
- 次の判断 (ユーザー): 殻の軽量版で GPU 側が消えれば、犬を見ている状態でも CPU の 30〜40 fps が上限になる見込み。古民家 v1.0.0 は Quest 3 で同じ 30〜40 fps を受け入れて公開した。これ以上は徘徊の tick 間引き (モバイルだけ `WaterTick` / Owner 判定 / 遠い犬の Update を 2〜4 フレームに 1 回) など挙動に触る最適化になるので、公開範囲と日程と合わせて決める
