---
id: VER-ENV-001
type: VER
system: ENV
status: wip
priority: High
---

# VER-ENV-001 試作: 眺め・移動・端の見え方・実機負荷を確認する

企画原本 §9 の着手依頼。簡単な地形・軽量化した古民家の外観 (仮)・犬を置いた試作で、次の 3 点を優先して確かめる。

1. 山から古民家が見える
2. 犬と無理なく遊べる
3. 小さな空間でも散歩した感じがする

`core-concept.md` §3 の代表シーン検証も兼ねる (仮決め → 本制作基準への更新はこの結果で判断)。

## 作るもの (最小)

| 要素 | 内容 |
|---|---|
| 地形 | Terrain (DES-ENV-001 の 1 手目)。入口 → 散歩道 → 展望 → 広場 → 入口 の短い周回路。歩ける範囲の外に景観用の余白。**Layer と草は古民家の庭と同じものを使う** (世界観を合わせる。2026-09-19 ユーザー決定): Layer は モサンゴ屋同梱の Standard Assets 地面テクスチャ (`_TerrainAutoUpgrade/TerrainAssets/SurfaceTextures/` の GrassRocky / Cliff / MudRocky / Sand / GrassHill)、草は古民家側 Git 管理の秋色 4 枚 (`Assets/KominkaDogRun/Textures/Environment/Autumn/`) と `garden_Autumn.asset` の Healthy/Dry Color を `.meta` ごとコピー。モバイルは Layer 4 枚以下に絞る |
| 木 | LMArt `BroadleafTreeB01` を数本。秋色は古民家の `Assets/KominkaDogRun/{Materials,Textures}/Environment/Autumn/` を `.meta` ごとコピーして流用 (GUID 維持) |
| 遠景 | LMArt `ViewSet01` (秋色) を試す。展望から見て邪魔なら外す |
| 古民家 (仮) | **モサンゴ屋「庭付き古民家」をこのプロジェクトへインポート** (ユーザー。古民家と同じ版。`.gitignore` 追記済み) し、建物の外観 (屋根・壁・縁側) だけを残して室内・家具・ギミックを無効化した軽量版を遠景に置く。「あの場所だ」と分かる形が目的。近づけない距離に置くので LOD / 三角形数は試作で測る |
| 犬 | **この段階では持ち込まない** (2026-09-19 ユーザー決定: タグ付けと移植は地形方式が決まってから IMP-DOG-001 で)。犬のサイズ感・発見性の確認には犬サイズのダミー (カプセル等、犬の毛色に近い色) を置く。ボールも同様にダミー可 |
| 小物 | ボール 1 個 / 既存メニュー 1 枚 (UI 可読性の確認用) |
| 端 | 谷側に柵 (Collider 付き)、山側に土手・岩 |

## 確認項目

| # | 項目 | 方法 | 判定 |
|---|---|---|---|
| 1 | 展望場所から古民家の屋根・建物が識別できる | Editor でカメラをレンダリングして画像を残す (OurUsualPark の `OP_ViewShotsEditor` が前例) | **Editor OK** (v2: 塀に囲まれた古民家を谷底に見下ろす。[計画 §10](../../plans/2026-09-19-terrain-prototype.md)) |
| 2 | 一周して Terrain の切断面・空白・空の床が見えない | 端 4 方向のスクリーンショット | **Editor 概ね OK** (南東・南西の隅は Play で確認) |
| 3 | 犬とボールが背景から浮く (3 m / 10 m / 木陰) | 画像 | **Editor OK** (木陰は白のみ確認。濃茶は目立たない) |
| 4 | 柵・斜面の外へ落ちない | Play で柵に沿って歩く (ユーザー) | 未 |
| 5 | 犬が斜面・道で破綻なく動く | Play (ユーザー)。**IMP-DOG-001 の後** | 未 |
| 6 | Android 実機での負荷の目安 | 実機ビルド (ユーザー)。`vrc-performance` の手順で Editor 側の描画コールも取る | 未 |
| 7 | UI が読める | 画像 | 未 |
| 8 | 犬と無理なく遊べる: 広場でボールを投げ、犬が取りに行って戻る。斜面・道の縁でボールが転がり落ちない | Play (ユーザー)。**IMP-DOG-001 の後**。成立 / 要調整 (何が) を記録 | 未 |
| 9 | 散歩した感じがする: 入口から一周して戻る。次の目的地が見える、道を外れない、短すぎ / 長すぎ (所要時間を記録) | Play (ユーザー)。所要時間と印象を記録 | 未 |
| 10 | 犬を連れて一周できる (追従がある場合)。犬が道を外れて詰まらない | Play (ユーザー)。**IMP-DOG-001 の後** | 未 |

項目は 2 段階で確認する: **地形の段階** (1〜4, 6, 7。ダミーで) → DES-ENV-001 を確定 → **移植後** (5, 8〜10)。
AI の担当は Editor で取れる 1〜3, 7 とビルド前のコンパイル確認まで。4〜6, 8〜10 は Play / 実機の引き継ぎ (`vrc-verification`)。結果は `docs/plans/` のチェックリストに「成立 / 要調整 (内容) / 未」で記録し、要調整は次の Issue へ切る。

## 完了条件

- [ ] 確認項目 1〜10 の結果と証拠を記録した (`docs/plans/` のチェックリスト)
- [ ] 地形の段階 (1〜4, 6, 7) の結果で DES-ENV-001 に判断材料を渡した (犬項目を待たない)
- [ ] `core-concept.md` §3 「代表シーンと検証」を更新した (仮決めのままか、本制作基準か)
- [ ] `game-design.md` §4 の未決定 (広さ・道の長さ・配置・行動範囲) に試作の数字を書いた

## 参照

- 企画原本 §5 (空間構成のたたき台), §9: `../../reference/VAnimal_KominkaHillside_Concept.md`
- イメージ図: `../../reference/world/`
- 古民家の秋色: KominkaDogRun `docs/development/issues/review/IMP-ENV-016-autumn-season-dressing.md`
- 地形の方式: [DES-ENV-001](DES-ENV-001-terrain-vs-blender.md)
