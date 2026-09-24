# Blender軽量アクセサリー：制作指摘修正済み

- Crown: 2928 tris / 1 Material
- BowTie: 1052 tris / 1 Material
- StrawHat: 2656 tris / 1 Material
- TailRibbon: 2440 tris / 1 Material

この4品の合計9,076 tris。4品内で頭・胸・尻尾を各1品選ぶ6匹分の最大は38,520 tris（王冠＋蝶ネクタイ＋リボン）。王冠の陰影と宝石金属度、帽子のUVとつば、リボン接続・微小穴を修正済み。既存matとFBX GUIDを維持。

初版の制作段階ではUnity 2022.3.22f1 / Standard、Windows Editorの単体プレビューを確認。後続の犬種別fit・実配置の確認範囲は以下の追記と全品ブラッシュアップ記録を参照。実機は未確認。

2026-09-12追記：Crownは6犬種×idle／sit／sleepの静止姿勢、元の毛の描画、実配置・照明と眼鏡併用を確認。サモエドの毛が宝石帯を貫く問題を装着高さで調整し、試着へ反映した。FBX・材質・256px画像2枚は維持。UV面積0は3色パレットを参照する意図した構造で、Normalは使用していない。確認範囲と実機の残件は[全品ブラッシュアップ記録](../../../../../../docs/plans/2026-09-12-accessory-polish-round.md)を参照。他の3品のこの段落以前の検証範囲は、今回のCrown確認によって更新されない。

制作元・検収：C:/work/unity/vrc-world-agent/artifacts/KominkaDogRun/accessories-optimized-20260907/Validation/production-fix/validation-report.md

2026-09-12追記：StrawHatは形状2,656trisを維持し、細い編み帯が周囲を回るBase ColorとNormalへ改修。1024px画像2枚・既存材質・GUIDを継続する。4犬種の装着と試着を調整し、6犬種3姿勢・元の毛の描画・実配置を確認した。制作元は`C:/work/unity/vrc-world-agent/artifacts/KominkaDogRun/accessory-polish-20260912/straw/`。再生成は`session-before.blend`から開始し、保存済み`source-mesh.json`を入力に`build.cjs`でOBJ・画像・APIペイロードを生成してSynapticで取り込む。完成したblendへ初回取り込みを重ねない。最終検収の状態と実機の残件は[全品ブラッシュアップ記録](../../../../../../docs/plans/2026-09-12-accessory-polish-round.md)を参照する。

2026-09-12追記：BowTieは薄い翼と根元のひだ、根元を覆う丸角の巻き布へ改修し、2,296→1,052tris。Base Color／Normal各1枚を1024→512pxへ整理した。既存材質・商品ID2・GUIDを保持し、コーギー／ラブラドール／サモエドの胸毛との接触を調整して試着へ反映。制作元は`C:/work/unity/vrc-world-agent/artifacts/KominkaDogRun/accessory-polish-20260912/bow/`。再生成は`before.blend`から開始し、`build.cjs`でOBJ・画像・APIペイロードを作りSynapticで取り込む。最終検収と実機の残件は全品ブラッシュアップ記録を参照する。

2026-09-12追記：TailRibbonは既存2,440tris・FBX・UV・材質を保持し、強い横縞を細かな布目へ変更。Base Color／Normalは各512px。サモエド／柴犬を巻き尾の外側へ、ラブラドールを胴体と交差しない尻尾側へ、ポメを布面へ毛が入らない高さへ調整し、試着へ反映した。制作元は`C:/work/unity/vrc-world-agent/artifacts/KominkaDogRun/accessory-polish-20260912/tail/`。`build-textures.cjs`で画像を再生成できる。今回はBlenderでの形状変更・再書き出しなし。最終検収と実機の残件は全品ブラッシュアップ記録を参照する。
