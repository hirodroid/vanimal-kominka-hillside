# KH_HalloweenRibbon 003 — 秋葉の小さなリボン

2026-10-14追加指示: 現在は003。ユーザーが細かな写実描写を否定したため、太い橙紫の斜め縞、大きな黄色の星へ変更。以下の002の織り・刺繍は履歴で、現在の柄ではない。003のBlender原本と生成画像はLive Generator shows/kh-halloween-parade/versions/003/ribbon-source。ポリゴン数と取り付け寸法は同じ。

## 目的・用途と見た目

ハロウィンパレード専用の首前アクセサリー1種。ACC / rigid / neck_front / seasonal。今回新規制作。橙の布を左右へ折り返し、落ち着いた紫の結び布と小さな橙の葉を中央に置く。短い二本の端でリボンの向きを見せる。犬が主役で、顔・耳・足を飾りで隠さない。ワールドの `docs/core-concept.md` §3 とA案の「小リボン＋葉」に基づく。犬の材質・購入モデルは変更していない。

## 対象と予算

PC / Quest単体 / iOSを対象に設計。最短視距離0.3m、通常1–3m、6個同時。BASELINE：1個1,000tri以内、1 Mesh、1材質、共有BaseColorアトラス（原寸1774×887、Unity上限2048）、骨0、透明0、発光0、Collider0。実機予算の合格ではない。小物の輪郭と折り返しに面を配り、002ではユーザーの実見後の指示に従い、布の織り・金の縫い・月と秋葉の刺繍を描き込んだ。揺れ機構は追加していない。

## 形状・寸法と作り方

幅0.120m、高さ0.066m、厚み0.02375m（突出した葉を含む）。Low-firstの生成型。左右ループはそれぞれ閉じた布帯の表裏を0.8mm離し、短い端も厚み付き。中心の紫は閉じた結び布、葉は0.5mm厚。プリミティブを並べた近似ではなく、折り返し・結び・V字の短い端を目的に合わせて頂点から構成した。

原点は結び布中央、Blender X横/Z上/−Y前。FBX変換後の契約はUnity Y上/Z前。葉のある面が外、短い端が下。単体のmeshは三角化を固定。物理・Collider・LODはなし。

## 接続点・可動・適合

首前の一つの骨へRigid追従。既存の首輪を置き換えず、その前面の少し下または外へ装着する。購入モデルを結合・編集しない。共通1種だが全犬種へ同一offset/scaleを適用して合格にしない。

| 犬種 | 適合状態 | 次の確認 |
|---|---|---|
| コーギー | Edit静止画確認済み | 歩行/座位/首振り |
| ポメラニアン | Edit静止画で胸毛の外に識別 | 歩行/首振り/低視点実機 |
| ラブラドール | Edit静止画で顔と胸を遮らない | 歩行/伏せ |
| ダックスフンド | Edit静止画で顎/足を遮らない | 歩行時の地面との距離 |
| 柴犬 | Edit静止画確認済み | 首振り/座位 |
| サモエド | Edit静止画で白毛の外に識別 | 歩行/首振りの埋没 |

Unity側の取り付け・個体別fit・通常装飾との干渉確認はホスト担当。足元や顔へ近づく個体はuniform scaleを小さくし、位置と角度も別に調整する。見えないまま付ける/首輪を消すことで合格にしない。

## 材質・UV・テクスチャ

1スロット `KH_HalloweenRibbon_Cloth`。Opaque、非金属、低光沢。`KH_HalloweenRibbon_BaseColor.png` は1774×887、左半分が橙の刺繍布、右半分が紫の刺繍布。built-in Imagegenで今回新規生成し、Blenderの連続投影UVへ割り当てた。三角形ごとの模様の再開始を解消。厚み面を含む全三角形のUV面積を確認した。1材質と共有画像で6匹へ同じ配色を渡す。

Unity材質の基準：既存のモバイル対応シェーダーにこの画像をsRGB BaseColorとして割り当て、乗算色白、metallic=0、smoothness≈0.16、Emissionなし。BlenderのPrincipled材質自体を本番材質の正本としない。両面shader不要、厚みを実メッシュで持つ。Normal/AOマップとlightmap UVは用途上不要。

## ファイルと引き渡し

本フォルダにFBX、BaseColor、`validation.json`、正面/斜め/背面の検査PNG。FBXにカメラ・照明・アニメーション・骨を含めない。PNGプレビューは検収資料であり、実行時材質へ割り当てない。

生成元：`C:/work/unity/vrc-live-generator/shows/kh-halloween-parade/versions/002/ribbon-source/build_ribbon.py`。
編集元の `.blend` は同じUnity外フォルダに保存。本番の取り付けはホスト側Prefab/Setupで行う。

```powershell
& 'C:/Program Files (x86)/Steam/steamapps/common/Blender/blender.exe' --background --factory-startup --disable-autoexec --python-exit-code 1 --python 'C:/work/unity/vrc-live-generator/shows/kh-halloween-parade/versions/002/ribbon-source/build_ribbon.py'
```

生成時に独立したFBX再読込を行い、tri数・UV面積・manifoldを照合してから検査JSONを書く。Unity側は読み書きOFF、骨/アニメーションなし、Collider生成OFF、法線Importを開始案とし、寸法・軸・材質・影・犬の装着を確認する。`meta`は既存GUIDを保つ。

## 参照元と権利

形状は今回専用の自作。購入小物・既存リボンの流用なし。002の布画像は built-in Imagegen による新規生成（参照画像なし）で、上記002原本フォルダに生成画像を保存している。一般的な蝶結びと秋の葉を要件から設計した。Blender 5.2.2 LTSの独立background生成、幾何生成元を保存。第三者素材のクレジット指定なし。用途はこのVRChatワールド内のパレード装飾、改変可。

2026-10-14、制作者が書き出した正面/斜め/背面の単体画像を確認。気になる特定作品の特徴には気づいていない。特定ブランドの結び方・模様・ロゴを参照していない。購入犬の素材権利は本モデルへ転用していない。

## 仮決め・未確定

六犬種のoffset/scaleはホストの`Parade/11 - Refine Ribbon Fits`で個別調整。首輪からの下げ量と、実ベイク胸面の前方2.5cmを基準に配置し、独立Vが2026-10-14の6匹装着静止画で識別と顔/足の非遮蔽を確認した。[統合制作票](../../../../../docs/plans/halloween-parade/production.md)に画像を保存。歩行・首振り・座位の干渉、夜・実機端末は未検証。公開採用とユーザーの完成承認は未取得。静止画を動作時の適合へ読み替えない。

## 確認結果

2026-10-14 / Blender 5.2.2 LTS：340頂点、668tri、1材質、骨0。6個のtri和4,008（実描画回数やfpsとは別）。FBX再読込も340頂点/668tri/1材質。3D面積ゼロ0、UV面積ゼロ0、non-manifold edge0。

[正面](preview-front.png) / [斜め](preview-three-quarter.png) / [背面](preview-back.png)。布の折り返し、厚み、短い端、中央の葉を制作者が実見。色出力でlinear値を二重変換していた初稿を修正し、最終PNGの橙/紫と再レンダーを確認した。これらは独立第三者レビューではない。

最終hashは `validation.json`。原本保存時に環境外 `.thumbnails` へ書けない警告が出たが、FBX/PNG/.blendと検査JSONの保存成功を確認。Unity操作は実施していない。

## 変更履歴

- 2026-10-14 / 001：新規折り返しリボン、中央葉、1材質パレット、FBX往復/UV検査、3方向画像。初稿のBaseColor出力を修正。六犬種適合はホストへ引き渡し。
