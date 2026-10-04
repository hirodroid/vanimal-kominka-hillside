# 小型撮影ドローン KH_VideoDrone

## 目的・用途と見た目
PROP / 撮影位置の目印・移動小物。撮影者と被写体だけに表示する仕様（表示制御は別担当）。[core-concept](../core-concept.md) §3（2026-09-22本制作基準、2026-09-27追記）に合わせ、生成り・濃灰・くすんだ橙、非金属・低光沢。丸い箱と短い腕、前だけに大きな橙の輪と黒いレンズを設ける。単体制作済み、ワールド統合未実施。

## 対象と予算
PC・Quest単体・iOS向け。距離0.5〜10m、3mで前が分かることを目標とする。指定上限700三角形、1メッシュ・1材質、テクスチャ・骨0。同時表示数は未定。実端末性能は未計測。

## 形状・寸法と作り方
幅0.320m × 高さ0.100m × 奥行0.250m。Low-firstの箱・円筒・回転断面を生成し、胴の縁を2段面取り。Exact Booleanで統合して内部接触面を除去、三角化。原点は黒い光学面中心、橙のリムは4mm前へ突出。Blender -Y前/+Z上からFBX -Z forward/Y upでUnity +Z前/+Y上へ。1単位=1m、変換適用済み。Collider・LODなし、影を落とさない運用。

## 接続点・可動・適合
原点を動画カメラ位置へ接続。リグ・アニメーションなし。円盤は静止表示。追従・見える相手の制限は別担当。

## 材質・UV・テクスチャ
Color / FLOAT_COLOR / CORNERの頂点色、FBXはcolors_type=LINEAR。色値はvalidation.jsonに記録。1材質、Opaque、白のテクスチャ入力×頂点色、白Tint、Metallic=0、低いSmoothness（目安0.18）。Blenderノードはプレビュー用、Unity材質は別途作成。UV0は各三角形の直交投影を0.05〜0.95へ配置、意図的な重なりあり・退化なし。ライトマップUV・テクスチャ・ベイクなし。

## ファイルと引き渡し
原本は[生成スクリプト](../../scripts/art/video-drone.py)。`blender --background --factory-startup --python scripts/art/video-drone.py`でFBX・3画像・検査JSON・この制作票を再生成する。[FBX](../../Assets/KominkaHillside/Models/VideoDrone/KH_VideoDrone.fbx)、[正面](evidence/2026-10-04-video-drone/preview-front.png)、[上面](evidence/2026-10-04-video-drone/preview-top.png)、[側面](evidence/2026-10-04-video-drone/preview-side.png)。Unity取り込みはScale Factor=1、単位変換有効を出発点に実寸・+Z前・原点を確認。Normals Import、Generate Colliders/Lightmap UVs無効、Cast Shadows Off、不要ならRead/Write Off。`.meta`は制作しない。

## 参照元と権利
特定の商品や写真の参照なしの自作。一般的なクアッドコプターの構造とユーザー仕様から設計。購入素材・外部画像・画像生成サービス・外部送信なし。既存roadside-bench.pyとriver-verge.pyは生成・FBX出力の作法のみ参照。確認日2026-10-04。ロゴ・固有装飾なし。

## 仮決め・未確定
Unityでの統合確認は未実施。里山の昼/木陰/夜、0.5m・3m・10mの視認性、PC・Quest単体・iOSの実機、追従表示と撮影映像への写り込みは別担当が確認する。単体の拡大レンダーを3mの実機検証と見なさない。

## 確認結果
[validation.json](evidence/2026-10-04-video-drone/validation.json): 692三角形・348頂点、1メッシュ・1材質。UV退化0、3D退化0、重複面0、非多様体辺0、不整合のある面方向0、非隣接三角形交差0、符号付き体積正。原点と光学面中心一致。保存FBXをBlenderへ再読込して面数・寸法・UV・形状検査・LINEAR頂点色一致を確認。Unityインポーターによる分割頂点数は未計測。画像は600角の単体レンダーで正面・側面はやや上方視点。

## 変更履歴
2026-10-04 / 初版 / scripts/art/video-drone.py、Blender 5.2.2 LTS、独立background + factory-startup実行。形状・頂点色・FBX往復検査と証拠生成。Unity操作・git操作なし。
