# PC連続泡 v3 — 実装候補の記録

2026-10-11。密集球を不採用としたユーザー画像を受けた再設計。仕様は [設計書 §17](../../2026-10-11-pc-shampoo-foam-design.md)、状態は [IMP-DOG-052](../../../development/issues/IMP-DOG-052-pc-volumetric-shampoo-foam.md)。外観方式v3、生成・Runtime互換Version2。実機採用の証拠ではない。

## 比較画像

同じ著者姿勢、同じカメラ・光の非Play撮影。単体は640×512、洗い場は1280×1024。撮影用に骨変形を焼いたMeshRendererを使い、実SkinnedMeshRendererは別のgeometry検査で描画した。実際に手で洗った画像ではなく、maskを指定した比較画像。

- [柴：下地のみ](Shiba-bed-full-quarter.png) / [連続面と丘](Shiba-new-full-quarter.png) / [人工すすぎ帯](Shiba-new-band-quarter.png)
- [洗い場での柴](Shiba-wash-station.png)
- [サモエド](Samoyed-new-full-quarter.png) / [ポメラニアン](Pome-new-full-quarter.png)
- 入力版・カメラ・条件：[単体](capture-isolated.txt) / [洗い場](capture-station.txt)

V担当の最終画像レビューは「比較候補として引継ぎ可」。独立した球の反復を除去し、サモエドの全身・half被覆時の胴脚の毛貫通を是正した。露出した毛の斑点は除去対象と別。白いスーツのような均一さ、肩首の盛り上がり、尾・足先の角張り、人工帯の硬い境界、顔側縁の小さな毛状の抜けは残る。柔らかさの達成・外観採用は未判定。

## 実施した確認

| 確認 | 結果と範囲 |
|---|---|
| C# / Udon | コンパイルエラー各0。Runtime変更はpcFoamVersionの1→2のみ |
| [geometry](geometry.json) | 全6匹PASS。保存meshのstreams、継ぎ目の7段階変位、元body・元辺を基準にした実skin、骨回転、実SMR描画、mask0/.05/.15/.3/.5/.75/1、すすぎ帯 |
| [生成](generation.json) | 6匹、合計18,846頂点・20,637三角形。最大skin比較誤差0.000003815 m、許可seam差0 |
| [実参照](verify.txt) | 6匹のmesh・bounds・版・proxy/backing・流下粒子上限16を確認、保存済み |
| [Edit実メソッド](runtime-checks.txt) | mask共有・表示制御・粒子throttle/cap・reset/warp/乾燥・欠損fallback。Udon VMのフレーム進行や通信の代替ではない |
| [ビルド用コピー](build-checks.txt) | PC/Android/iOSで各6匹。モバイルから専用root・proxy/backing参照・依存を除外。改名・wrong mesh・stale scaleの拒否も確認 |
| [Windows本番シーンbundle](bundle-report-StandaloneWindows64.json) | run `4d80773f69dd40a0888958d8a6583b86`。実callbackで6匹処理、BuildReport成功・errors0、元scene SHA一致。SDK uploadではない |
| [group bundle再描画](geometry-bundle.json) | 読み込んだmeshの頂点・法線・tangent・UV0/1/2・三角形を現在assetと照合し、同じ実SMR検査で全6匹PASS |

上記はbody・既存薄膜を除く追加mesh数量。旧球方式の48,384頂点・92,160三角形より少ないが、厚い面が2passになり、透過画素・skin・実機速度を数量だけでは比較できない。PC下地2＋連続面2で計4pass。流体計算、毎フレームのmesh生成、CPUへのmask読戻しはRuntimeに追加していない。

PC専用groupのbundleは圧縮1,670,610 bytes、非圧縮5,432,992 bytes。依存を含むgroup全体であり、全ワールドの差分容量・常駐メモリではない。今回のAndroid/iOS確認はコピー処理と依存除外まで。旧版の実target bundle記録を新版の実targetビルド成功として扱わない。

## レビューと修正

- U/D/M/X/P/Vの6担当を別プロセスとして設計レビュー。実装後はD/M、外観画像はVが独立確認。
- D：薄い縁による深度遮蔽を、ほぼ不透明な領域と半透明の縁の2passに分割して修正。skin期待値を生成mesh自身から作らず元body・元辺と比較し、seamも保存streamで再計算。限定再確認で必須指摘解消。
- M：撮影前にもgenerator fingerprintと入力鮮度を検査。旧球のschema名を廃止。Udon backing版も照合。限定再確認で必須指摘解消。
- 実SMRで旧asset更新後にstrideエラーを検出。CPU配列から新Meshを組む対照は全6匹成功したため、CopySerializedをClear(false)＋各streamの明示設定へ置換。保存meshから全6匹PASS。Dがこの限定差分を再確認。
- V：長毛の貫通を指摘。Samoyed非頭部のcoat下限0.045 mを上下層へ共通適用。切除face境界4 cmでcoatをBaseOffsetへ戻し、浮いた襟状の縁も低減。[元材質の診断](materials.txt) を参考にした著者値で、毛変形全姿勢の保証値ではない。最終限定画像で胴脚の斑点是正を確認。

新Play・PCVR両眼・昼夜・全姿勢・実シャワーとの描画順・CPU/GPU・複数人／途中参加・SDKアップロード・実端末は未確認。旧球方式のPlayやbundle成功をv3の成功として流用しない。洗浄・通信ロジック自体は維持している。

## 共有作業の保全

作業開始時にシーンのbaselineを保存。泡の6proxy・6backing・6renderer boundsだけをコミット対象に抽出し、別作業の差分を作業ツリーに保持する。RenderSettingsの自動再計算差分は泡のコミット対象に含めない。購入資産、Quest/iOSの既存shader・設定は変更していない。
