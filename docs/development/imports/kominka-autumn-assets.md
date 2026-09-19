# 古民家ドッグランからの秋色資産の移植記録

犬ギミックの移植記録 (`kominka-dog-system.md`。IMP-DOG-001 で作成) とは別に、舞台の秋色資産をここに記録する。

- コピー元リポジトリ: `C:/work/unity/vcc/KominkaDogRun`
- コピー元コミットSHA (完全長): `503dde8ff702e8d9f4dd73b4a6473ede7dcfd638` (2026-09-19。コピー対象パスに未コミット変更なし)
- 移植日: 2026-09-19
- 移植した範囲 (`.meta` ごとコピーし GUID を維持。コピー先は `Assets/KominkaHillside/` 配下の同じ相対パス):

| コピー元 (`Assets/KominkaDogRun/`) | 内容 |
|---|---|
| `Textures/Environment/Autumn/` (PNG 11 枚) | 草 4 枚 (GrassFrond01/02, grassFlower01/07) / 広葉樹の葉 2 枚 / 遠景 ViewSet01 の植生・地面・山 5 枚 |
| `Materials/Environment/Autumn/` (材質 5 個) | `Mat_BroadLeaves02_Autumn` (木の葉) / `Mat_BroadPlants01_Autumn` `Mat_BroadGround02_Autumn` `Mat_Mountains01_Autumn` `Mat_BoardTrees01_Autumn` (遠景 ViewSet01 用) |
| `Data/Environment/Autumn/garden_Autumn.asset` (TerrainData) | 庭の地形そのものではなく、**Layer 5 枚と草 Detail 5 枠 (秋の Healthy/Dry Color・寸法・風設定) の参照元**として持つ。裏山の TerrainData は新規に作り、ここから Layer / Detail をコピーする。`.gitattributes` で `binary` 指定 |

- Git 外の依存 (このプロジェクトへ導入済み。`.gitignore` で除外):
  - LMArt-StylizedEnvironmentKit `BroadleafTreeField` — 木・遠景のモデルとシェーダ (`LMArtShader/NatureLeaves`, `SimpleArt`)。秋材質はこれらを GUID で参照
  - モサンゴ屋「庭付き古民家」— インポート時に生成される `Assets/_TerrainAutoUpgrade/` に Terrain Layer (`NewLayer*.terrainlayer`) と Standard Assets の地面テクスチャ (GrassHill / GrassRocky / Sand / MudRocky) がある。`garden_Autumn.asset` はこれを GUID で参照
  - 両方とも古民家ドッグランと同じ版であることを GUID 一致で確認済み (2026-09-19)
- 移植時に加えた変更: なし (バイト単位で同一)
- 省略したもの: `SourceAssets/Environment/Autumn/manifest.json` (色変換の記録。古民家側 IMP-ENV-016 から辿れる) / 庭の Terrain 形状 (使わない)
- コピー元の動作確認範囲: 古民家の Editor 構造検証 (146 + 124 項目) 済み。Play / 実機は古民家側で確認待ち (IMP-ENV-016)
- 移植先の確認結果: Unity での参照解決を eval で確認 (材質 5 個のシェーダ・テクスチャ、TerrainData の Layer 5 / Detail 5 が null なし)。配置後の見た目は未検証 (→ VER-ENV-001)
- 管理方針: 以後はこのワールドで個別管理する。古民家側で色を変えても自動同期しない。派生物の許諾は元アセット (LMArt / モサンゴ屋 / Unity Standard Assets) に従い、自作・CC0 とは扱わない

## Terrain Layer の使い分け (裏山)

庭の 5 枚 (GrassHill / GrassRocky / Sand / MudRocky / GrassHill01) のうち、モバイルの 1 パス制限 (4 枚) に収める:

| 用途 | Layer |
|---|---|
| 草地 (基本) | GrassHill |
| 草地 (荒れ・岩混じり) | GrassRocky |
| 道 | Sand |
| 土手・岩場の土 | MudRocky |

## 後日の修正取り込み

| 日付 | 原因・修正元のコミット／Issue | このワールドへの適用内容・見送り理由 | 確認結果 |
|---|---|---|---|
