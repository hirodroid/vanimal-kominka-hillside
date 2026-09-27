# DES-ENV-002 現状調査と周回コース案

2026-09-27。成果物は [HTML提案書](index.html)。**周回構成は採用済み。寸法・座標・水面方式・性能予算は未確定。** 下記は提案時点の調査履歴。着手承認後の計測・川の代表区間制作は [実装記録](implementation.md) に続く。本シーンと既存Terrainは保全し、別シーン/新規コードで制作を開始した。

## ユーザーの意図

- 犬としっかり歩いて一周約20分。寄り道・休憩込みなら30〜40分でもよい。
- 何周も続けて楽しめる散歩。既存の道を保全する。
- 今の登り道から広場へ進み、背後から下り、右側の川沿いを小橋で渡りながら古民家側へ戻る案。
- 仮の地形で終わらず、完成品質で追加する方法を確認する。今回は案だけをHTMLで提示する。

## 調査方法と確認値

`scripts/unity-api.sh check` で接続先を `C:/work/unity/vcc/VAnimal_KominkaHillside/Assets` と照合。wrapper経由の読み取りevalと既存の `KH_WalkRouteEditor.Inspect()` を使用。

| 項目 | 値と根拠 |
|---|---|
| 開いているシーン | `Assets/Scenes/VRCDefaultWorldScene.unity` |
| 開始・撮影後の状態 | Edit mode / scene dirty=False |
| Terrain | `KH_Terrain`、原点(0,0,0)、size(120,50,180)m、heightmap257、alphamap256、detail256、TerrainLayer4 |
| 草の描画距離 | 40m（読み取り値。性能の合格値ではない） |
| 門前 | (47.639,1.606,40.084)m |
| 見晴らし | (80,19,108)m |
| 広場 | (72,19,127)m |
| 道 | 17点・16リンク。backing Udonの配列が存在し、管理側の参照も存在 |
| 既存経路長 | `route.routeCost[16] = 113.8808m`（格納済み経路表。今回の新規歩行計測ではない） |
| NavMesh | 読み取り時の三角形頂点配列939要素 |
| Main Camera far | 1000m。VRChat実視点での適用値は未確認 |

実画像： [全体俯瞰](assets/current-perspective.png) / [真上](assets/current-top.png) / [既存広場](assets/current-plaza.png)。Scene Viewだけを動かして撮影し、pivot(68.049,7.769,119.390)、rotation Euler(46.513,234.866,0)、size15.77507、perspectiveへ復元した（開始値の小数点3桁までの復元）。Play・Build・シーン保存・refreshは行っていない。

既存ノードと提案座標は [proposal-data.json](proposal-data.json) に保存。既存ノードはInspectの出力（小数点2桁）。新規点の座標・高さ・川位置は設計案。

## 修正が必要な根拠

1. [KH_TerrainPrototypeEditor.cs](../../../Assets/KominkaHillside/Scripts/Editor/KH_TerrainPrototypeEditor.cs)：`BuildStageA` は `KH_World` を削除。現状の再生成を拡張の入口にすると既存を保全できない。追加専用の生成範囲と接続帯の管理を設ける。
2. [KH_TerrainPrototypeConfig.cs](../../../Assets/KominkaHillside/Scripts/Editor/KH_TerrainPrototypeConfig.cs)：境界X12〜108、Z12〜168。新規の道は既存境界で塞がれる。遠景リングは中心(60,0,90)、直径561mとのソース記録で、新設範囲と重なる。遠景は歩ける地形として扱わない。
3. [KH_DogWalkingEditor.cs](../../../Assets/KominkaHillside/Scripts/Editor/KH_DogWalkingEditor.cs)：NavigationのVolumeが120×70×180m固定。`Ground` は最初のTerrainを使う。[KH_WalkRouteEditor.cs](../../../Assets/KominkaHillside/Scripts/Editor/KH_WalkRouteEditor.cs)にも同じ前提。複数Terrainと橋面に対応する必要がある。
4. `KH_WalkRouteEditor.BuildRoute()` は一本のPathから隣接リンクのみを生成し、全対の最短経路を計算。目的地は入口・広場の2個。データ配列の器はグラフにできるが、現行ビルダーのまま周回は生成しない。
5. [KH_WalkRoute.cs](../../../Assets/KominkaHillside/Scripts/KH_WalkRoute.cs)：近傍リンクと道までの距離は水平距離。両岸・橋・上下に近接する道で区間を誤認しない設計が必要。
6. [KD_DogWanderController.HillsideLeash.cs](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.HillsideLeash.cs)：`ChooseEntry` は全点を走査するが水平30mで候補を絞り、それからNavMesh計算。`len + routeCost` を最小化するので、周回する意図は現状では保持しない。
7. 新しい水面・橋は今回選定していない。ファイル名検索でSynaptic付属のWater shader等は存在するが、VRChat・各端末適合や景観との統合を確認していないため、そのまま採用できる素材とは判断しない。

## 提案座標の計算

- 既存経路表113.8808m + 新規点間の3D折れ線1693.8827m = **1807.7635m**。
- 平均1.5m/sを仮定すると **20.0863分**。実際の連続歩行速度は未測定。
- 1.0m/s：30.1分、2.0m/s：15.1分、2.4m/s：12.6分。犬の速度設定から人の速度を決めない。
- 比較用外枠600×720m、既存120×180mの20倍。現状のタイル寸法を保つなら最大5×4区画。詳細地形の実面積・タイル数は未決定。
- 橋の接続帯は図上30m。橋本体6〜10m、川幅3〜6m、通行幅3〜4mは初期提案で、既存素材の実寸ではない。
- 図は折れ線を表示。曲線化・地形への接地・NavMesh経路の実寸が決まったら距離を再算出する。

## 性能の扱い

- 過去のAndroid代理計測37.91MBは2026-09-24の値。[容量記録](../2026-09-24-build-size-audit.md)。現行シーンの再ビルドはしていない。
- HTMLの追加20MBは作業予算の**提案**。地形/NavMesh8、水/橋/岸6、植生/LOD4、環境音2MB。約58MBという和は完成品の予測・保証ではない。
- 257×257×2byte×20面 = 2,641,960byte は仮に20枚の高さ配列を持つ場合の生データの算術例。Unity実メモリやバンドルの合計ではない。
- 追補で描画100コールの仮配分を撤回。裏付けとなる現行版の余力を未測定のため、代表区間の実測から配分を決める。20MBの容量配分も未検証の作業案であり保証ではない。
- 6匹・複数人、正逆周回、橋、途中参加、Owner交代、60〜120分継続を実機条件に含める。旧コースのおもちゃ・リード・見晴らしの回帰確認も必要。

## 参照した公式資料

- [Unity 2022.3 / 隣接Terrain](https://docs.unity3d.com/ja/2022.3/Manual/terrain-CreateNeighborTerrains.html)：隣接区画を追加し境界高を合わせる機能。自動で完成した谷や川を作る保証ではない。
- [VRChat / Android Content Optimization](https://creators.vrchat.com/platforms/android/quest-content-optimization/)：ワールドでカスタムshaderは利用可、透過と描画負荷に注意。
- [VRChat / World Creation, Optimization, and Community Labs Tips](https://creators.vrchat.com/worlds/submitting-a-world-to-be-made-public/)：VR向けshader、VRでの確認。

## 今回の完成範囲

現状調査、HTMLでの配置提案、変更計画、仕上げと実機検証の条件。**実装可否は構成上の見通しで、実装済み・性能合格を意味しない。** 初回提示後、2026-09-27にユーザーが周回構成を採用。core-concept / game-designへ反映した。寸法・水面方式・性能予算の確定と実装Issue化は残る。

## 提案書の検証と独立レビュー

- 作成・セルフレビュー：主担当 `/root`。独立レビュー：同じCodex環境内の別担当 `/root/proposal_review`。提案書と根拠コードを読み取り確認。外部Claude CLIは応答が得られず停止し、権限拡張の再試行は外部送信の懸念で自動承認レビューに拒否されたため、使用を中止した。
- 独立レビューの指摘：上流橋を渡った後、東岸を南へ歩く際の川は右手。左右の説明を修正し、下り途中は「谷側」と記載。指摘解消を同じ担当が確認した。
- セルフ確認：下流橋の後の道をX395→375（Z150）へ移し、川中心線との近接を解消。新しい距離・所要時間はJSON・HTML・本記録へ反映した。
- 静的検査：HTML内の相対リンク・画像・アンカーが存在、重複IDなし、埋め込みデータとJSONが一致、JavaScript構文が正常。提案の川横断は2か所で、上流橋(X417.5,Z490)・下流橋(X417.5,Z190)と一致。
- ヘッドレスChromeのHTML描画：PC幅1424pxと狭幅500pxを確認。狭幅でページ全体がはみ出す問題を修正（地図内は横スクロール）。通常ブラウザ接続は利用できなかったため、専用一時プロファイルの画像化で確認した。ユーザーのブラウザ設定は変更していない。
- 操作検査：8地点の選択、川・橋の同時表示切替、地形区画切替、速度変更による所要時間計算がPASS。最後の図は総距離1807.7634508m、1.5m/sで20.0862606分。
- `node scripts/check-doc-links.js`：リンク切れなし。`node scripts/check-issues.js --write`：45件・整合。HTMLの独自リンク検査もPASS。
- 最後のUnity読取でもEdit mode・scene dirty=False。地形寸法・Scene View復元値を確認。シーンファイルに今回の差分なし。

## 2026-09-27 容量・区画非表示の追補

ユーザーから容量・性能を最優先の懸念として再確認。HTMLの「容量・非表示設計」に6区画の候補、常時見せる遠景、既存体験の保全、代表区間での計測を追加。シーン変更・商品導入・実測は行っていない。

- ダウンロード容量・実行時メモリ・描画/処理時間を分ける。表示非表示はアンロードではない。素材共有、地形/ライトマップ/NavMeshの総量も管理する。
- A既存、B林道、C奥広場、D下り/上流橋、E中流、F下流橋/帰着を初期候補。地形タイルとは別の表示単位。遠くても見える古民家・地形輪郭を残し、対岸や曲がり角前後の表示範囲を重ねる。
- 犬・リード・道具・同期・Collider・NavMeshを景観の非表示に巻き込まない。ローカルの景観表示として設計し、Owner/別区画の参加者/途中参加/撮影視点を確認する。
- 商品はMUZoneVisibility、参照元はユーザー指定の[作者BOOTH](https://booth.pm/ja/items/7458751)。確認日は2026-09-27。Webの商品説明を方式調査に使用。掲載版v1.12。配布コード・画像は取り込んでいない。説明で確認できたのはエリア単位の見た目切替、GameObject非アクティブ化を避ける方式、重複範囲と常時表示の指定。Terrain/LOD対応、端末互換、内部処理は未確認。採用前に現物を確認する。
- ページの利用条件は改変可・商用利用での二次配布不可と記載。今回は説明の参照のみ。導入時は同梱規約と実際のワールド用途を照合する。
- [Unity遮蔽の設定](https://docs.unity3d.com/2022.3/Documentation/Manual/occlusion-culling-getting-started.html)も比較対象。山を置くだけで自動的に全て隠れるとせず、ベイク/表示範囲/LODの効果を確認する。
- 完成品質の川岸・橋・木立・境界の代表区間で、全表示/区画非表示等を同条件比較してから全周へ展開。展開後には総容量・常駐メモリ・経路負荷を再計測する。
- 追補の検証：HTMLの相対リンク・ID・埋め込みデータ・JS構文の検査、文書リンク検査、Issue整合検査がPASS。ヘッドレスChromeでPC幅1424px・狭幅500pxの表示を確認し、ページ全体の横はみ出しなし。独立担当 `/root/proposal_review` が追補の技術・ユーザー観点を確認し、修正が必要な指摘なし。これは計画の検証であり実機性能の合格ではない。

## 2026-09-27 コース構成の採用

ユーザーが周回構成を了承。犬が主役、景観は背景。現在程度の簡素さを許容し、美しさを多少落としてもAndroidを含む16人参加・犬6匹での快適さを優先する。要所に犬と遊べる小さな発見を置く方向で、具体的内容は未決定。

川用シェーダーは既存品が条件を満たせば再利用、不足時のみ専用制作。不透明・主描画1パス・流れ模様のUV移動を技術案として、反射用カメラ・画面取得による屈折・多層透過を初期基準から外す。[VRChat公式のモバイル最適化](https://creators.vrchat.com/platforms/android/quest-content-optimization/)のワールド用シェーダー・透過負荷の説明を参照（2026-09-27）。方式は未採用・未実装。16人の集合時/分散時を同条件で比較し、人数だけではアバター由来の負荷を規定できないため表示設定・アバター・端末も記録する。シーン・コードの変更なし。

採用反映の独立レビュー：担当 `/root/proposal_review`。冒頭とJSON/HTML埋込データに残った「採用未決定」表記の不整合1件を修正し、構成採用・詳細未確定へ統一。その他の採用範囲・性能目標・水面技術案の区別は指摘なし。文書/Issue/HTMLデータの検査を実施。
