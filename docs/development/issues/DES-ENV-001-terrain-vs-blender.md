---
id: DES-ENV-001
type: DES
system: ENV
status: todo
priority: High
---

# DES-ENV-001 地形を Unity Terrain で作るか Blender で作るか

企画原本 §4 は「Unity Terrain で制作する方向」だが、ユーザーは未決定としている (2026-09-19)。
舞台の骨格を決める最初の判断なので、試作 (VER-ENV-001) で確かめてから決める。

## 判断軸 (検証前の仮説。試作の実測で確かめる)

下表は比較の観点と**仮説**であり、根拠のある実測値ではない。採否は VER-ENV-001 の条件付き実測 (端末・設定・数値) で決める。

| 軸 | Unity Terrain (仮説) | Blender メッシュ (仮説) |
|---|---|---|
| 制作速度 (地形の起伏・道・調整) | Editor 上でブラシ。試行が速い | 形状の自由度は高いが往復 (書き出し → 取り込み) が要る |
| スマホ負荷 | Detail / Tree / Layer 数で変わる。古民家の庭 (Terrain) の Android 計測 (KominkaDogRun IMP-INF-022) が参考値。`vrc-performance` で実測し「Terrain だから軽い」とも「重い」とも決めつけない (不変条件 5) | ポリゴン数とテクスチャを自分で管理できる。LOD も明示的 |
| 端の処理 (切断面を見せない) | 矩形の境界が出る。景観用の余白がどれだけ要るかは試作で見る | 端を意図した形に閉じられる |
| 草 (Paint Details) | 古民家と同じ方法が使える (秋色の草テクスチャも流用可) | 別途ばら撒きが要る |
| 道の表現 | Layer のペイント | UV / 頂点カラー / 別メッシュ |
| 古民家との連続性 | 古民家の庭は Terrain。同じ作りで質感を揃えやすい | 質感合わせを別途 |
| ベイク | 面積が大きいと Lightmap 解像度と容量の兼ね合いが要る (古民家の庭で実績あり: IMP-ENV-017) | メッシュは通常どおり |
| Udon からの操作 | Terrain は Udon から触れない (古民家 DES-PRG-002 の実測。現時点で必要な機能は無い) | Renderer なので触れる (夜演出などをするなら) |

## 進め方 (着手と確定を分ける)

**着手 (試作前)**: この Issue で比較条件と「Blender へ切り替える基準」を決める。方式の決定はしない。

1. **まず Terrain で試作する** (VER-ENV-001)。安く速く、古民家と同じ方法だから
2. 試作で次のどれかに当たったら Blender を検討する (切り替え基準):
   - 端の余白を広げても散歩道から切れ目が見える
   - Android 実機で Terrain の描画が目標 (§4 で決める数値) に収まらない (Detail / Layer を削っても)
   - 道・段差・崖の形が Terrain のブラシで作れない

**確定 (試作後)**: VER-ENV-001 の実測・画像を根拠に方式を決め、`game-design.md` §4 の「地形の作り方」を日付付きで更新し、この Issue に理由と証拠を残す。

## 完了条件

- [ ] 方式が決まり、`game-design.md` §4 と `core-concept.md` §3 の「主要アセット候補と制約」を更新した
- [ ] 判断の根拠 (試作の画像・負荷の数値・当たった問題) をこの Issue に記録した

## 参照

- 企画原本 §4〜6: `../../reference/VAnimal_KominkaHillside_Concept.md`
- 試作: [VER-ENV-001](VER-ENV-001-prototype-view-and-edges.md)
- Blender を使うなら `vrc-synaptic-blender` / `vrc-asset-production`
