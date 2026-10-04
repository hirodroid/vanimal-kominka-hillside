---
id: BUG-ENV-001
type: BUG
system: ENV
status: review
priority: High
summary: ビルダーが作った材質 40 個が Standard Lite の既定値 (金属 1・つや 1) のままで、Quest でフリスビーが真っ黒だった (10-04 ユーザー報告)。40 個とも金属 0 へ直した (10-04、Materials/1・2 PASS)。PC の画面では 6 色と顔が出る。Quest・iOS の確認待ち
next: ユーザーが Quest (できれば iOS) で、フリスビーの 6 色と顔・狙いの線が見えるかを確認。首輪とリード本体 (KH_Leash_WovenMetal、金属マップ付きで今回は未変更) が黒くないかも見る。黒ければ追加で直す
updated: 2026-10-04
---

# BUG-ENV-001 ビルダー製の材質が既定値のまま鏡面になっている

## 見つけた経緯

2026-10-03、手洗いのボトル ([IMP-DOG-039](IMP-DOG-039-hand-wash-with-shampoo-bottle.md)) が Edit モードの撮影で青く写った。材質の色はピンクで、原因は `VRChat/Mobile/Standard Lite` の既定値 `_Metallic 1`・`_Glossiness 1` (金属マップ無し) で空を映していたこと。同日、看板用の撮影 ([IMP-UI-002](IMP-UI-002-guide-board-capture-instructions.md)) で、フリスビーの「行けない」灰色の線が通常の線と同じ青に写り、同じ原因と分かった。

## 範囲 (2026-10-03、`Assets/KominkaHillside` の .mat 261 個を走査)

金属 ≥ 0.9・つや ≥ 0.9・金属マップ無しは **40 個**、すべて Standard Lite:

- `Data/Frisbee/`: `KH_FrisbeeAim`・`KH_FrisbeeAimOut` (狙いの線の通常 / 行けない)、`KH_FrisbeeDisc` と犬別 6 色、`KH_FrisbeeFace_*` 6、`KH_FrisbeeGround`・`Recall`・`Rock`・`Stand`・`Terrain`・`Variant`・`Wall`
- `Data/Walking/`: `KH_ClipNeutral`・`KH_ClipReady` (リードの先の通常 / つながる時の緑)、`KH_Collar_*` 6、`KH_GatherMarker`、`KH_RackWood` (リード台と散歩の帯の柱)、`KH_Rope`、`KH_SignPlaza`
- `Data/Wash/Tools/`: `KH_WashTool_Shelf` (洗い場の棚・「あらいば」の札・RESET の札)、`Shower`・`ShowerHead`・`Sponge`・`Towel`・`TowelKinari` (取り込んだモデルの下に隠れるものを含む)

作っているビルダー: `KH_FrisbeePrototypeEditor.cs:80, 509`、`KH_DogWalkingEditor.cs:194`、`KH_LeashProductionEditor.cs:77`、`KH_DogWashSetupEditor.Dog.cs:326` / `.Station.cs` (`ToolMaterial`)。`new Material(Shader.Find("VRChat/Mobile/Standard Lite"))` のあと色だけを書いている。ボトルの 2 材質は 10-03 に金属 0・つや 0.35 へ直した (`BottleMaterial`)。

## 影響の見立て (未確認)

- 色で区別させている所が、空の映り込みで近い色に寄る: 狙いの線の通常 (水色) と行けない (灰色)、リードの先の通常と緑、犬別のディスク 6 色・首輪 6 色
- 端末で映り込みの出方が違う (反射プローブ・スペキュラの扱い)。**[BUG-DOG-005](BUG-DOG-005-ios-disc-colors.md) (iOS でフリスビーの色が全部同じ) の原因候補**。iOS 実機での確認はまだ
- 木のはずのリード台・洗い場の棚の札が紺〜黒っぽく見える (Edit モードの撮影)。実機の見え方はユーザーが PCVR で総合 OK としている (10-02) ので、今の見た目が気に入られている可能性もある

## 直し方の案

各ビルダーで材質を作る / 直す時に `_Metallic 0`・`_Glossiness 0.2〜0.35` を毎回書く (既存の .mat も直る。ボトルの `BottleMaterial` と同じ形)。ディスクは少しつやを残す (0.5) など、物ごとに決める。前後を Edit モードで撮影して比べ、ユーザーが実機で確認する。

## Quest でフリスビーが真っ黒 (2026-10-04 ユーザー報告)

> Quest で確認したが、フリスビーが真っ黒。iOS も同じっぽい。シェーダーを変えないといけないかな?

**シェーダーは変えなくてよい見立て。材質の値を直す。**

- ディスク (`KH_FrisbeeDisc_*`) も顔 (`KH_FrisbeeFace_*`) も `_Metallic 1`・`_Glossiness 1`・金属マップ無し。金属 1 では光が当たっても材質の色は出ず、**見える色は「周りの映り込み × `_Color`」だけ**になる
- PC は空が映り込むので色が付いて見える。Quest・iOS で映り込みが暗い (または無い) と、真っ黒になる。顔の印字も同じ材質の値なので見えないはず
- [BUG-DOG-005](BUG-DOG-005-ios-disc-colors.md) の「iOS でフリスビーの色が全部同じ」(09-26 体験会) は、全部が黒かった可能性が高い
- 端末で映り込みが違う理由は特定していない (反射の扱い、空のキューブマップの端末別の圧縮など)。金属 0 にすれば映り込みに頼らなくなるので、理由の特定より先に直す
- 訂正 (10-04 夜、材質ファイルを全文で確認): 40 個とも、映り込みと反射光は**既に切ってある** (`_GLOSSYREFLECTIONS_OFF`・`_SPECULARHIGHLIGHTS_OFF` のキーワードあり)。起票時に「キーワードが立っていない」と書いたのは、検索結果の読み違いだった
- 映り込みを切った金属の色は、キューブマップではなく**シーンの間接反射色** (`m_IndirectSpecularColor`) × `_Color` で決まる。この色は PC のエディタで保存したシーンでは青 (0.04, 0.15, 0.45)、10-04 にユーザーのビルド後に保存されていたシーンでは黒 (0, 0, 0) だった。PC で青っぽく、Quest で真っ黒に見える説明になる (ビルド対象が Android の時に黒になる、というのは保存内容からの推測)

首輪・リードの先・狙いの線・集合の目印も同じ値なので、Quest では黒っぽいはず。

決定: 2026-10-04 ユーザー「OK」— 統括の質問「首輪・リードの先・狙いの線も黒っぽいか。黒ければフリスビーと同じ回で直す」への回答。同じ回で直す。Quest でそれらが実際に黒く見えたかの個別の観察は聞いていない (AI の読み: 同じ回で直すことへの了承)。

## 完了条件

- [x] ユーザーが直すかどうかを決めた — フリスビーは直す (10-04 の報告で確定)。首輪・リードの先・狙いの線なども同じ回で直す (10-04 ユーザー了承)
- [x] 直す材質が、ビルダーの再実行でも戻らない — 材質を作る 4 か所 (`KH_DogWalkingEditor`・`KH_FrisbeePrototypeEditor` の 2 か所・`KH_DogWashSetupEditor.ToolMaterial`) が `KH_MatteMaterialsEditor.Matte` を呼ぶ。コード上の確認で、ビルダーの再実行はしていない (旧メニューは再実行を禁じているものがある)
- [ ] 狙いの線の通常 / 行けない、リードの先の通常 / 緑、ディスク 6 色・首輪 6 色が、色で見分けられる (PCVR・Quest・iOS)
- [ ] BUG-DOG-005 の iOS での再確認

## 記録

2026-10-04 (修正): `Tools/KominkaHillside/Materials/1 - Fix Mirror Defaults` で `Data/` の 40 個を金属 0・つや 0.3 へ直した (映り込みと反射光は切ったまま)。`Materials/2 - Verify` PASS (82 個を走査、残り 0)。C# コンパイル エラー 0。変えたのは材質ファイルだけで、シーンは触っていない。

- 前後の比較 (PC のエディタ、Edit モード、広場の 6 枚): [修正前](../../plans/evidence/2026-10-04-matte-materials/before-discs.png) は 6 枚とも青〜紺に寄り、顔も青黒い。[修正後](../../plans/evidence/2026-10-04-matte-materials/after-discs.png) は赤・桃・黄・青・橙・白の 6 色と顔が出る。**これは PC の画面で、Quest・iOS の合格ではない**
- 色は環境光 (空 0.60・水平 0.35・地面 0.19 の 3 色) だけで決まるようになった。PC でも見た目が変わる (青みが消える)
- **今回変えていないもの**: 首輪とリード本体の `KH_Leash_WovenMetal` (金属マップ付き、映り込みあり)。意図して金属にした本制作の材質で、PC では空を映して紺に見える。Quest で黒いかは未確認
- 画面に見える変化はフリスビー・狙いの線・集合の目印など。木のリード台・洗い場の棚は IMP-ENV-012 の新しい材質が表示されているので見た目は変わらない
- 独立レビューは省略 (材質の値と Editor ツールだけで、同期・保存・権限に触れない)

2026-10-04 (夜): Quest でフリスビーが真っ黒とのユーザー報告を追記し、priority を High へ。材質ファイルの値を確認しただけで、材質・ビルダーは未変更 (Unity は未接続)。

2026-10-04: ユーザーが棚・リード掛け・写真ボード・ボトルの品質改善を指示。[IMP-ENV-012](IMP-ENV-012-close-prop-finish.md)で新規表示材質とBlenderモデルを適用し、木製部分の金属反射を除いた。旧材質は非表示の互換用部品に保持しており、40材質を一括修正したものではない。リード先端・首輪・ディスクの状態色、洗い場の操作札などは今回変更していない。

2026-10-03: 起票。走査のみ。材質・ビルダーは未変更 (ボトルの 2 材質を除く)。
