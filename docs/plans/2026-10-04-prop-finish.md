# 近距離の小物を整える — 制作票

## 目的・用途と見た目

2026-10-04 ユーザー追加指定。景観更新と一緒に、配置済みの棚・リード掛け・写真ボード・ボトルの品質を上げる。Blenderで形状を制作し、面数を過度に増やさず、面取り・接合・輪郭と木目・ノーマルの役割を分ける。[ビジュアル方針](../core-concept.md)に沿う、落ち着いた屋外の木製設備と手入れ道具。

## 対象と予算

棚4台、リード設備3か所（現在の有効状態を保持）、写真ボード1台、ボトル4本。目安は棚3,000、ラック2,500、ボード4,000、ボトル2,000 triangles以下／モデル。共有の不透明材質1種、BaseColor・Normal・Metallic/Smoothnessのアトラス各2048以下、モバイル1024以下。負荷実測・追加最適化はユーザー指定により別Issue VER-ENV-002で後続。

## 形状・寸法と作り方

棚は幅1.2・奥行0.45・天板高0.8 m。板の厚み、角の丸み、脚の接合、下段のすのこを作る。ラックは既存横木1.2 mと柱間1.12 mを維持し、掛け金具と接合を追加。ボードは既存幅2.78 mの屋根と2.24×1.04 mの表示面を保持。ボトルは直径0.06 m、底Y−0.085 m、肩とキャップを滑らかに接続。原本は再生成スクリプトとAssets外の生成blend。

## 接続点・可動・適合

既存ルート、Transform、Collider、Pickup、Udon、帰還点、噴射Nozzle、写真／Canvas／Pagerを維持。新しいMeshだけを子に追加し、旧表示を無効化する。写真面Z−0.006とCanvas前面を枠で隠さない。通常ビルダーにも適用経路を設ける。

## 材質・UV・テクスチャ

長手に沿う木目、端面、塗装の微細な凹凸、落ち着いた金具、ボトルの印刷ラベル。面取りは実形状、微細凹凸は接線空間Normal。非金属の木・樹脂と金具をアトラス内のMetallic/Smoothnessで区別。新規材質を作り、既存の手編集テクスチャは上書きしない。

## ファイルと引き渡し

- 制作: `scripts/art/prop-finish.py`
- 寸法設定: `art-source/prop-finish/config.json`。生成blend: `art-source/prop-finish/generated/PropFinish.blend`（UnityのAssets外）
- FBX読戻し検証: `scripts/art/verify-prop-finish.py`
- Unity: `Assets/KominkaHillside/Models/PropFinish/`
- 適用・検証: `KH_PropFinishEditor`（Course/160〜164）
- 比較: [HTML](evidence/2026-10-04-prop-finish/index.html)

## 参照元と権利

外部画像・商品ロゴを使用しない。既存ワールド内の寸法と用途を基にした自作形状・手続き生成テクスチャ。既存WashToolsの操作仕様を参照。2026-10-04確認。Blender生成履歴はスクリプトに残す。

## 仮決め・未確定

代表距離0.3〜3 mでEditor実景比較し、木目の強さ・丸みを調整。ユーザーの実機評価とQuest/iOSでの見え方・負荷は未確認。

## 確認結果

2026-10-04、rootが制作・Unityへの適用・固定視点撮影を担当。scenery_consultが独立相談とD/V/Mレビューを担当。詳細は[検証記録](evidence/2026-10-04-prop-finish/validation.json)。

| モデル | triangles / 体 | 配置数 |
|---|---:|---:|
| 棚 | 2,216 | 4 |
| リード掛け | 932 | 3 |
| リード案内枠 | 324 | 3 |
| 写真ボードの外枠・屋根 | 1,920 → 1,696 (2026-10-15 BUG-ENV-005) | 1 |
| ボトル | 1,184 → 1,158 (2026-10-15 BUG-ENV-005) | 4 |

- 生成と別プロセスのFBX読戻しで三角形数一致、UV退化0、有限頂点・単位法線を確認。木目は実景比較後に抑え、ボトルのラベルの向きと円筒の法線を調整した。
- 共有Standard不透明材質1種。BaseColor/Normal/MetallicSmoothnessは各2048、Android/iPhoneは1024・ASTC 6×6、mipmap有効・CPU read/writeなし。木・樹脂は金属0、金具領域のみ金属を使用。
- Course/163: 15モデルの材質・接線・寸法・Nozzle整合・追加Colliderなし・インポート設定を確認、errors 0。
- 既存機能コンポーネント968個のEditorシリアライズ内容が適用前後で一致。シーン全20,351既存ブロックの消失0。変更は旧44 MeshRendererの非表示と15親Transformへの子追加のみ。既存Transformの位置・回転・拡縮、機能・Collider・写真参照は変化なし。
- Wash/32合格、Wash/12は5,316 checks合格、Photo/2はfails 0・warns 0。C#コンパイルerrors 0。Playは実行していない。
- 比較HTMLは7視点・前後14枚、カメラ位置／向きが一致、撮影後scene dirty=false。HTMLのスクリプト構文と画像参照を確認。ブラウザー自動操作の接続が無いため、ブラウザー内の表示・操作の自動確認は未実施。
- 独立レビューの指摘2件（写真Structureの旧6 Renderer固定検査、ボトルの旧4 Renderer有効検査）を修正。限定再確認で追加必須修正なし。最終ボトルの遮蔽・ラベル・輪郭も確認。
- 実機の見え方・操作感、Quest/iOSの描画負荷は未確認。[IMP-ENV-012](../development/issues/IMP-ENV-012-close-prop-finish.md)をreviewとし、性能は[VER-ENV-002](../development/issues/VER-ENV-002-scenery-performance.md)へ引き継ぐ。

## 変更履歴

- 2026-10-04: ユーザーの小物品質改善指示を追加、現行150 MeshRendererを調査。制作前の実景と本番シーンを退避してから適用する。
- 同日: Blender製5モデルと共有マップを導入。Wash・Walking・Guide・Photoビルダーに表示の再適用を組み込み、既存機能を保持した比較HTMLを作成。
- 同日: 比較HTML提示後にユーザーから「OKです。」を受領。小物の仕上がりは了承済み。実機の見え方・操作感・性能の合格を示す確認ではない。
