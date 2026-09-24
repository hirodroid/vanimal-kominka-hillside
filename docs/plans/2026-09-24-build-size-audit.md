# ビルドサイズ実測 — 裏山 PC / Android と削り代 (2026-09-24)

> **Android の余力は十分。基準 51.56 MB → モバイル上限適用後 41.94 MB → アルベド以外を半分にして 37.91 MB (上限 100 MB)。PC は 75.93 MB で不変。**
> 手順・罠・相場の正本は共有 Skill `vrc-performance` の「ビルドサイズチェック」。この文書は裏山の実測値と上限表の運用だけを持つ。
> 古民家の記録は [KominkaDogRun 2026-09-10](../../../KominkaDogRun/docs/plans/2026-09-10-build-size-audit.md)。
>
> **モバイルの見た目は未確認。** 1024 に落とした樹皮・古民家・遠景の山、512 に落とした犬のノーマル・メタリック、256 のおもちゃのノーマル・AO は [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) #8 で実機を見る。

計測ツールは `Tools/KominkaHillside/Build Size/1〜8` ([KD_BuildSizeEditor.cs](../../Assets/KominkaHillside/Imported/KominkaDogRun/Editor/KD_BuildSizeEditor.cs)。古民家からの取り込みを 2026-09-24 に Imported-Unused から昇格し、表を裏山向けに書き換えた)。出力は `Temp/kd-buildsize/` (Git 対象外)。

---

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

音楽の上限は空 (裏山にライブ・BGM が無い。SE は触らない)。曲を足したら `AudioCaps()` に 1 行 (古民家: 曲 q=0.5、wav マスターは PC q=0.7)。

## 4. 残っている削り代 (見積り。実測は削ってから測る)

余力は十分なので、どれも今は要らない。将来ライブ (モバイル 1 本 約 1.8 MB) や飾りを足すときの予算として残す。

| 手 | 見積り (Android) | 何を判断するか |
|---|---|---|
| フォントの元 ttf を最終ビルドから外す | -4 MB | 構築ツールが字形追加で差し直す運用と両立させる手順。字形欠落の確認 |
| 空を 512 に | -1 MB | Quest の帯。ユーザー指示「スカイボックスも下げる。下面は限りなく」は単一 Cubemap では面別にできず、`Skybox/6 Sided` に分ける形になる (未実測) |
| 犬のプレビュー姿勢を間引く | -2 MB 前後 | 姿勢は商品。最後 |
| 犬・木のアルベドを 512 に | -3〜5 MB | 主役と景観の見た目。実機で見てから |

## 5. 何を足すと何 MB 増えるか

古民家の単価表 ([§5](../../../KominkaDogRun/docs/plans/2026-09-10-build-size-audit.md)) がそのまま使える (同じ犬・同じ飾り・同じ木)。裏山の追加分: Terrain 1 面 265 KB、首輪・リード一式 (`kh-textures` 1.81 MB + `kh-meshes`)。

## 6. 次の一手

1. **Quest / iOS の実機で見た目を確認する** (VER-WLD-001 #8)。1024 の樹皮・古民家・山、512 の犬のノーマル・メタリック、256 のおもちゃのノーマル・AO。NG なら `Caps()` の該当行を戻して `7 - Apply` → 再計測。
2. 素材を足したら `7 - Apply` → `8 - Verify` 0 件 → `2 - Bundle Size Android`。この表に日付順で積む。
3. 最後に Android の SDK 実ビルドで確定させる (代理計測との差は古民家で 0.01%)。
