# ビルドサイズ実測 — 裏山 PC / Android と削り代 (2026-09-24)

> **拡張前の本シーン（2026-09-27）：Android 42.75 MiB / iOS 41.78 MiB。別シーンの最新コースは43.20 / 42.40 MiB（斜面の連続補間とNav/PVS更新後、下記）。** 下の2026-09-24の37.91 MBは過去の測定値。PCの最新再計測は未実施。
> 手順・罠・相場の正本は共有 Skill `vrc-performance` の「ビルドサイズチェック」。この文書は裏山の実測値と上限表の運用だけを持つ。
> 古民家の記録は [KominkaDogRun 2026-09-10](../../../KominkaDogRun/docs/plans/2026-09-10-build-size-audit.md)。
>
> **モバイルの見た目は未確認。** 1024 に落とした樹皮・古民家・遠景の山、512 に落とした犬のノーマル・メタリック、256 のおもちゃのノーマル・AO は [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) #8 で実機を見る。

計測ツールは `Tools/KominkaHillside/Build Size/1〜8` ([KD_BuildSizeEditor.cs](../../Assets/KominkaHillside/Imported/KominkaDogRun/Editor/KD_BuildSizeEditor.cs)。古民家からの取り込みを 2026-09-24 に Imported-Unused から昇格し、表を裏山向けに書き換えた)。出力は `Temp/kd-buildsize/` (Git 対象外)。

---

## 2026-09-27 表示名フォント (Static 1024×2048 + Latin 512²) を足した本番シーン

| 対象 | 圧縮ダウンロード | 非圧縮 | 水遊び・地図 (44.88 / 44.07 MB) との差 |
|---|---:|---:|---:|
| Android | 45.86 MB | 121.34 MB | +0.98 |
| iOS | 45.04 MB | 117.75 MB | +0.97 |

[IMP-WLD-001](../development/issues/IMP-WLD-001-map-warp-and-join.md) 段階 4。内訳は `KH_DisplayName SDF` の Alpha8 アトラス 1024×2048 (2 MiB) と Latin 512² (256 KiB)、字形表 3,680 字。元の TTF はビルドに入らない (81 で参照 0)。途中の版: Dynamic は TTF 同梱で Android 48.41 MB (+3.53)、Static 2048² は 46.11 MB (+1.23)。

## 2026-09-27 川の水遊び・地図/ワープ/合流を実装した本番シーン

| 対象 | 圧縮ダウンロード | 非圧縮 | 秋の橋・地面 (44.83 / 44.01 MiB) との差 |
|---|---:|---:|---:|
| Android | 44.88 MB | 118.73 MB | +0.05 |
| iOS | 44.07 MB | 115.14 MB | +0.06 |

`Tools/KominkaHillside/Build Size/2・3` の保存シーンの代理容量 (Temp/kd-buildsize/bundle-sizes.txt)。追加分は KH_RiverData / KH_WaterSpot の配列、水しぶき・波紋の 64² テクスチャ 2 枚、地図 512² (PC DXT1 / モバイル ASTC 6x6)、暗転の材質、翻訳、字形。IMP-DOG-020 の予算 +0.5 MiB の内側。端末メモリ・16 人性能ではない。

## 2026-09-27 遠景スクリプト除去・周回復帰修正後

| 対象 | 圧縮ダウンロード | 非圧縮 |
|---|---:|---:|
| Android | 43.22 MiB (45316353 bytes) | 116.15 MiB |
| iOS | 42.41 MiB (44471305 bytes) | 112.55 MiB |

保存シーンの代理容量。SDKアップロードや端末メモリ・16人性能ではない。[原記録](des-env-002/verification-records.md#full-course-walk-measurements)。

## 2026-09-27 地形の遮蔽を追加した全周コピー

| 対象 | 圧縮ダウンロード | 非圧縮 | 遮蔽追加前との差（圧縮） |
|---|---:|---:|---:|
| Android | 43.23 MiB (45,326,664 bytes) | 116.15 MiB | +437.2 KiB |
| iOS | 42.41 MiB (44,471,721 bytes) | 112.55 MiB | +453.0 KiB |

地形の遮蔽フラグ・20 View Volumeと所有PVSデータを含む保存シーンの同条件代理計測。夜の確認は材質の複製によるEditor描画だけで、ランタイム資産は追加していない。非圧縮サイズは実機の使用メモリではない。SDKアップロード・Quest 3/iPhone 15 Proの実表示・16人性能は未確認。 [原データ](des-env-002/verification-records.md#full-course-occlusion-measurements) / [画像と描画量の比較](des-env-002/verification-records.md#full-course-occlusion)。

## 2026-09-27 景観・LODを追加した全周コピー

| 対象 | 圧縮ダウンロード | 非圧縮 | 景観追加前との差（圧縮） |
|---|---:|---:|---:|
| Android | 42.80 MiB (44,878,960 bytes) | 114.87 MiB | +0.95 MiB |
| iOS | 41.97 MiB (44,007,872 bytes) | 111.28 MiB | +0.93 MiB |

木15本・石61群・草24区画、LOD用の材質と更新NavMeshを含む保存シーンの代理バンドル。遠景木の秋色アトラス Tex_BroadNature02_Autumn はモバイルのみ2048→1024上限・圧縮ありへ設定し、PCと元画像は維持。共有秋色画像の変更で、上限の正本はKD_BuildSizeEditorの個別行と追跡対象.meta。前段と同じ圧縮・対象で比較し、景観追加分を記録した。非圧縮サイズは端末メモリの実測ではない。Quest 3／iPhone 15 Proの実表示、犬連れと16人の性能は未検証。 [原データ](des-env-002/verification-records.md#full-course-scenery-measurements) / [描画寄与](des-env-002/verification-records.md#full-course-scenery-benchmark)。下記の41.85 / 41.03 MiBは景観追加前の値。

## 2026-09-27 経路とメニューを接続した全周コピー

| 対象 | 圧縮ダウンロード | 非圧縮 |
|---|---:|---:|
| Android | 41.85 MiB (43,882,121 bytes) | 112.80 MiB |
| iOS | 41.03 MiB (43,028,246 bytes) | 109.21 MiB |

全周NavMesh・436地点の経路・6目的地メニューを含む別シーンの代理容量。字形追加時に共有フォントのランタイムTTF参照が外れたため、前回より容量が減った。経路だけの削減量ではなく、共有フォント変更を含む値。基本／追加文言の字形と旧817グリフの保全は別検査済み。16人時の性能・使用メモリ・Quest 3/iPhone 15 Proの見た目は未検証。 [原データ](des-env-002/verification-records.md#full-course-route-measurements) / [制作と検査](des-env-002/implementation.md)。同じ共有フォントを参照する本シーンも依存資産が変わっているので、拡張前42.75/41.78 MiBを現在の本シーン容量とは呼ばない。本シーン自体の再計測は未実施。

## 2026-09-27 コース拡張前の再計測

同じ代理計測メニューで、保存済み `VRCDefaultWorldScene` を再ビルド。Unity 2022.3.22f1 / SDK 3.10.5。モバイルテクスチャ上限は対象71枚・不足0。以下は **MiB (1,048,576 bytes)**。非圧縮バンドルは実機の使用メモリではない。

| 対象 | 圧縮ダウンロード | 非圧縮 | 100 MiBまでの差 |
|---|---:|---:|---:|
| Android / ASTC | 42.75 MiB (44,828,470 bytes) | 110.03 MiB | 57.25 MiB |
| iOS | 41.78 MiB (43,806,128 bytes) | 105.64 MiB | 58.22 MiB |

川の代表区間を含まない追加前の値。過去との差の内訳は未分解。SDKアップロード・Quest 3 / iPhone 15 Proでの合格を示すものではない。原データは `Temp/kd-buildsize/bundle-sizes.txt`（ローカル生成物）、作業記録は [コース拡張の計測と制作](des-env-002/implementation.md)。

### 別シーンの川代表区間を加えた比較（同日）

`KH_RiverRepresentative.unity`は、既存ワールドのコピーに180mの川岸・水面・小橋1基・樹木2本・代表区間用NavMeshを加えた検証シーン。最新は自然な川岸へ改修したv2（非対称の浅瀬、小石48個、128²木目）。全周コースの完成版ではない。圧縮/非圧縮とも別の新規出力先へビルドし、完了とファイル生成を確認した。

| 対象 | 圧縮ダウンロード | 非圧縮 | 追加前との差（圧縮） |
|---|---:|---:|---:|
| Android / ASTC | 43.65 MiB (45,770,965 bytes) | 111.81 MiB | +0.90 MiB |
| iOS | 42.84 MiB (44,924,909 bytes) | 108.22 MiB | +1.07 MiB |

[測定原データとシーンハッシュ](des-env-002/verification-records.md#measurements)。River Lite / Scenery LiteのGLES3/Metalバリアントをビルドした範囲で確認。v1からの自然化による増分はAndroid +63,379 bytes（61.89 KiB）/ iOS +82,830 bytes（80.89 KiB）。v1は [旧測定](des-env-002/verification-records.md#measurements-v1) に保存した。容量から実機FPS・常駐メモリ・16人時の動作を判断しない。本シーン/既存Terrain/QualitySettingsのハッシュ不変も同JSONに記録した。

### 全周地形の別シーン（同日、NavMesh統合前）

KH_RiverLoopCourse.unity：既存シーンのコピーへ600×720m・20 Terrain、24水面区間、小橋2基、外周余白を構築。地表材質・川・橋・遠景素材は共有。旧NavMeshは残るが新道のNavMeshと犬の経路は未接続。

| 対象 | 圧縮ダウンロード | 非圧縮 | 拡張前との差（圧縮） |
|---|---:|---:|---:|
| Android | 45.06 MiB (47,244,780 bytes) | 119.73 MiB | +2.30 MiB |
| iOS | 44.11 MiB (46,247,543 bytes) | 115.34 MiB | +2.33 MiB |

[全周地形の計測JSON](des-env-002/verification-records.md#full-course-measurements)。Unity 2022.3.22f1 / SDK 3.10.5、Android ASTC・iOSの新規出力先で圧縮/非圧縮の完了を確認。主シーン・旧Terrain・QualitySettingsのSHA256は拡張前と一致。容量は現段階の値で、植生・全周NavMesh・仕上げによる増分は今後再計測する。常駐メモリ・描画性能・16人の実機合格ではない。

## 1. 実測値 (2026-09-24、Unity 2022.3.22f1 / Worlds SDK 3.10.5、代理計測 = シーンの LZMA バンドル。SDK 実ビルドは未実施)

| 段階 | PC ダウンロード / 非圧縮 | Android ダウンロード / 非圧縮 | Android 余力 |
|---|---|---|---|
| 基準 (Verify 11 件漏れのまま) | 75.93 / 205.75 MB | 51.56 / 127.26 MB | 48.44 MB |
| ① モバイル上限の適用 (9 枚。§3) | 75.93 / 205.75 MB (不変) | 41.94 / 115.66 MB | 58.06 MB |
| ② アルベド以外を半分 (36 枚。§3) | 未計測 (規則はモバイル専用) | 37.91 / 103.27 MB | 62.09 MB |

- iOS は未計測 (古民家の実測では Android より 1 MB 前後小さい)。
- 基準の「漏れ 11 件」のうち 2 件は 96×96 の UI 円 (非圧縮の PC 側)。36 KB なので圧縮強制の対象から外した (`Textures/UI/` の行)。残り 9 件が①。

## 2. 内訳 (Android、グループごとの単体バンドル)

| グループ | 基準 | ② 適用後 | 中身 |
|---|---|---|---|
| trees-textures | 14.19 MB | **3.70 MB** | LMArt の木・遠景の山 (樹皮 6 枚 2048 → 1024、ノーマル 512) |
| dogpreview-meshes | 7.02 MB | 7.02 MB | 呼ぶ姿勢・ミニチュアのプレビュー (古民家の重複排除後) |
| people-meshes | 6.60 MB | 6.60 MB | polyperfect の FBX 丸ごと (参照メッシュは 2.73 MB) |
| dog-textures | 5.19 MB | **3.24 MB** | 犬 6 種 39 枚 (ノーマル・メタリック・毛マスク 512) |
| kh-fonts | 4.30 MB | 4.30 MB | KD_NotoSerif SDF + 元 ttf |
| kominka-textures | 2.64 MB | **1.51 MB** | 遠景の古民家 (2 枚 2048 → 1024) |
| kh-textures | 1.81 MB | 1.81 MB | 首輪・リード・UI の自作テクスチャ |
| toy-textures | 1.64 MB | **1.06 MB** | おもちゃ 16 枚 (512、ノーマル・AO 256) |
| dog-meshes | 1.49 MB | 1.49 MB | 犬 6 種の FBX |
| toy-meshes | 1.44 MB | 1.44 MB | おもちゃ 8 ファイル |
| sky-cubemap | 1.44 MB | 1.44 MB | Allsky (モバイル 1024) |
| trees-meshes | 1.26 MB | 1.26 MB | LMArt のメッシュ |
| kd-textures | 1.21 MB | 1.21 MB | 古民家由来の演出・UI テクスチャ |
| accessory-textures | 964 KB | **808 KB** | 飾り 29 枚 |
| shaders-liltoon | 606 KB | 606 KB | lilToon 2 本 |
| accessory-meshes | 591 KB | 591 KB | 飾り 24 ファイル |
| kd-model-meshes | 352 KB | 352 KB | おやつ・寝床のメッシュ |
| kd-audio | 351 KB | 351 KB | SE 14 本 (触っていない) |
| kh-terrain-data | 265 KB | 265 KB | Terrain 1 面 |
| dog-animclips | 176 KB | 176 KB | 犬のアニメ 134 本 |
| kominka-meshes | 164 KB | 164 KB | 古民家のメッシュ 2 ファイル |
| kd-animclips | 115 KB | 115 KB | 自作アニメ 44 本 |
| terrain-layer-textures | 82 KB | 82 KB | Terrain 層 3 枚 |
| kd-shaders | 38 KB | 38 KB |  |
| kh-animclips | 18 KB | 18 KB |  |
| people-textures | 8 KB | 8 KB | polyperfect のアトラス |
| kh-shaders | 7 KB | 7 KB |  |
| (合計) | 53.88 MB | **39.57 MB** | |

- `people-meshes` は polyperfect の FBX 丸ごと (13 ファイル) の値で、参照グラフの推定ではシーンが使うメッシュは 2.73 MB。**FBX 単位の行は上振れする**。
- `dogpreview-meshes` 144 本は古民家の重複排除 (§7) 後の姿勢プレビュー。姿勢そのものが商品なので削り代にしない。
- `kh-fonts` 4.30 MB は `KD_NotoSerif SDF` (2048² アトラス) + **元 ttf 7.33 MB (非圧縮)**。アトラスは Static だが `m_SourceFontFile` を残しているため ttf が同梱される。構築ツール (`KD_AccessorySetsEditor` / `KD_ActionProductsEditor`) が字形追加のたびに元 ttf を差し直すので、外すなら「最終ビルド前に null にする」手順が要る (見積り -4 MB。字形欠落の確認が必須、`vrc-tmp-font`)。
- サモエドの `DefaultMaterial.001_Base_Color 2.png` は PC で 2048 RGB24 非圧縮 (16 MB)。モバイルは Override が圧縮する。PC に上限が無いので古民家と同じく急がない。
- ライトマップは無い (未ベイク)。Terrain データは 265 KB。

## 3. 上限表 (正本は `KD_BuildSizeEditor.Caps()`。`.meta` ではない)

対象フォルダの多くは Git 対象外 (購入アセット) なので、**クローン・素材追加・VPM 更新のあとは `7 - Apply` を流し直す**。`8 - Verify` が 0 件になってから `2 - Bundle Size Android` を測る。

| 対象 | モバイル上限 (アルベド) | アルベド以外 | 根拠 |
|---|---|---|---|
| `Assets/TORINOSUKE/` (犬 6 種) | 1024 | 512 | 古民家の決定 |
| `Assets/モサンゴ屋/` (遠景の古民家) | 1024 | 512 | 古民家の決定。裏山では遠景専用 |
| `Assets/LMArt-StylizedEnvironmentKit/` (木・遠景の山) | 1024 | 512 | 古民家の決定。`Tex_Mountains01` 2048×512 → 1024×256 は遠景の山 |
| `Assets/polyperfect/` (おじいさん) | 1024 | 512 | 新規 |
| `Assets/Allsky/` (空) | 1024 | — | IMP-ENV-002 と同値。512 は Quest で帯が見える判断 (古民家) があり据え置き |
| `Assets/_TerrainAutoUpgrade/` (Terrain 層) | 1024 | 512 | 新規 |
| `Imported/KominkaDogRun/Models/` (飾り) | 1024 | 512 | 古民家の決定 |
| `Imported/KominkaDogRun/Models/Toys/` | 512 | 256 | 古民家 2026-09-15 の決定 |
| `Imported/KominkaDogRun/Textures/`、`Assets/KominkaHillside/Textures/` | 2048 + 全 PF で圧縮強制 | — | 自作物。非圧縮の 1 枚を通さない |
| `Assets/KominkaHillside/Textures/UI/` | 2048 (圧縮強制なし) | — | 96×96 の UI 円。BC7 で縁が荒れる方が損 |
| `Assets/KominkaHillside/Models/` (首輪・リード等) | 1024 | 512 | 新規 |

**アルベド以外の判定** (`IsNonAlbedo`): Importer が NormalMap、またはファイル名に normal / _ao / occlusion / metallic / smoothness / roughness / _spec / _height / _mask 等を含むか `_nm` / `_n` で終わる。行の上限の半分 (下限 256)。犬の `ke.png` (毛のマスク) も対象になった。

**音源は削っていない** (`AudioCaps()` は空、SE 14 本は毎回「music over its cap: 0」)。**音源は最後の手段で基本やらない** (2026-09-24 ユーザー決定)。曲を足しても、よほどの無駄が無い限り `AudioCaps()` に行を足さない (古民家の q=0.5 はこの決定より前の個別判断)。

## 4. 残っている削り代 (見積り。実測は削ってから測る)

余力は十分なので、どれも今は要らない。将来ライブ (モバイル 1 本 約 1.8 MB) や飾りを足すときの予算として残す。

| 手 | 見積り (Android) | 何を判断するか |
|---|---|---|
| フォントの元 ttf を最終ビルドから外す | 9/27に共有フォントのランタイム参照解除を確認 | 上記の全周コピー容量へ反映済み。単独の削減量は未分解。将来の字形追加でもEditorRefと収録字を検査する |
| 空を 512 に | -1 MB | Quest の帯。ユーザー指示「スカイボックスも下げる。下面は限りなく」は単一 Cubemap では面別にできず、`Skybox/6 Sided` に分ける形になる (未実測) |
| 犬のプレビュー姿勢を間引く | -2 MB 前後 | 姿勢は商品。最後 |
| 犬・木のアルベドを 512 に | -3〜5 MB | 主役と景観の見た目。実機で見てから |

## 5. 何を足すと何 MB 増えるか

古民家の単価表 ([§5](../../../KominkaDogRun/docs/plans/2026-09-10-build-size-audit.md)) がそのまま使える (同じ犬・同じ飾り・同じ木)。裏山の追加分: Terrain 1 面 265 KB、首輪・リード一式 (`kh-textures` 1.81 MB + `kh-meshes`)。

## 6. 次の一手

1. **Quest / iOS の実機で見た目を確認する** (VER-WLD-001 #8)。1024 の樹皮・古民家・山、512 の犬のノーマル・メタリック、256 のおもちゃのノーマル・AO。NG なら `Caps()` の該当行を戻して `7 - Apply` → 再計測。
2. 素材を足したら `7 - Apply` → `8 - Verify` 0 件 → `2 - Bundle Size Android`。この表に日付順で積む。
3. 最後に Android の SDK 実ビルドで確定させる (代理計測との差は古民家で 0.01%)。

## 2026-09-27 樹皮・接地・Nav/PVS更新後

| 対象 | 圧縮 | 非圧縮 |
|---|---:|---:|
| Android | 43.22 MiB (45319246 bytes) | 116.15 MiB |
| iOS | 42.41 MiB (44475140 bytes) | 112.55 MiB |

保存シーンの代理容量。実機メモリ・性能ではない。[原記録](des-env-002/verification-records.md#full-course-polish-measurements)。

## 2026-09-27 ベンチ回避Nav更新後

| 対象 | 圧縮 | 非圧縮 |
|---|---:|---:|
| Android | 43.22 MiB (45318865 bytes) | 116.15 MiB |
| iOS | 42.41 MiB (44469408 bytes) | 112.55 MiB |

保存シーンの代理容量。[原記録](des-env-002/verification-records.md#full-course-bench-measurements)。

## 2026-09-27 外周地形/PVS更新後

| 対象 | 圧縮 | 非圧縮 |
|---|---:|---:|
| Android | 43.23 MiB (45328377 bytes) | 116.15 MiB |
| iOS | 42.41 MiB (44466545 bytes) | 112.55 MiB |

保存シーンの代理容量。[原記録](des-env-002/verification-records.md#full-course-landscape-measurements)。

## 2026-09-27 斜面の連続補間とNav/PVS更新後

| 対象 | 圧縮 | 非圧縮 |
|---|---:|---:|
| Android | 43.20 MiB (45302927 bytes) | 116.09 MiB |
| iOS | 42.40 MiB (44455391 bytes) | 112.50 MiB |

保存シーンの代理容量。[原記録](des-env-002/verification-records.md#full-course-shoulder-measurements)。
