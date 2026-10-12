---
id: IMP-DOG-051
type: IMP
system: DOG
status: review
priority: Medium
summary: フリスビーのモデル改善。2026-10-11にA「まる縁スタンダード」をユーザー採用。2026-10-12にメッシュ (1,728三角形) と差し替えメニュー Frisbee/6 を制作。Unityへの取り込み・適用は統括、見た目の確認は未実施
next: ユーザーの Play (A0-66: 持つ・飛ぶ・くわえる・両面の顔・持ち主の半透明表示で皿の内側がどう透けるか)、Quest/iOS の色 (VER-WLD-001)。Frisbee/5 (試作シーンを追加で開く) は未実行
updated: 2026-10-12
decided: 2026-10-11
---

# IMP-DOG-051 フリスビーのモデル改善

2026-10-11 ユーザー要望。フリスビーのモデル改善を課題として記録。同日、HTMLで比較したA「まる縁スタンダード」をユーザーが採用した。モデル実装は未着手。

## 採用方針（2026-10-11・ユーザー決定）

ユーザーの「AのおすすめでOKです」により、**A・まる縁スタンダードを採用**。犬別の6色と両面の顔を残し、外周を丸め、中央を薄く、裏面を浅い皿状にする。B・Cは比較資料として残し、今回の制作対象には含めない。

[改善案のHTML](../../plans/2026-10-11-frisbee-model-review.html) で、3案の回転・裏面・犬別の色と顔・断面を比較できる。希望をテキストとして保存する機能も付けた。選択はIssueへ自動反映されず、採用決定ではない。

- **A・まる縁スタンダード（採用）**: 外周を丸め、中央を薄く、裏面を浅い皿状にする。犬別の6色と両面の顔を維持。
- **B・ふっくらソフトリム**: 縁を幅広く膨らませ、柔らかい玩具の印象に。小型犬の口元で縁が太く見えすぎないか比較する。
- **C・きなりリング**: Aの形にきなり色の輪を追加する意匠候補。識別色の面積と白いディスクの見分けやすさを確認する。

生成コードでは現行の本体は直径240mm・厚さ24mmの円柱、顔は両面160mm。保存済み材質は非金属・低光沢（Metallic 0 / Glossiness 0.3）。材質の不具合を断定せず、形状を改善の主対象とする。現行のUnity実行状態の計測は未実施。

外径240mm・外周最大厚24mm・顔160mmを維持して試作する方針。中央厚・くぼみ・分割数などの細部はHTMLの初期値から制作時に詰める。表示メッシュと両面の顔の配置を変更し、当たり判定・飛行設定・保持時の表示切替は初回試作では維持する。制作目標・視距離・検証条件・出典はHTMLに記載。図は簡易形状で、Unityへの適用・端末での見え方や負荷の検証は未実施。

## 対応内容

- 採用したA案の形状・厚み・縁を制作し、既存の材質を起点に調整する。
- [コアコンセプト](../../core-concept.md) のビジュアル方針に沿って見た目を改善する。
- 犬ごとの識別色を維持し、手に持った状態・飛行中・犬がくわえた状態で見え方を確認する。

## 完了条件

- [x] 改善方針を記録し、ユーザーが採用案を決めた（2026-10-11・A案）。
- [ ] 採用方針をモデルへ反映した (2026-10-12 メッシュ制作済み。本シーンへの適用は Unity で Frisbee/6 を通してから)。
- [ ] 本シーンで見た目を確認し、ユーザーが改善後のモデルを確認した。
- [ ] 持つ・投げる・キャッチ・持ち帰りに支障がないことを確認した。
- [ ] Quest・iOS でも識別色と材質の見え方を確認した (端末別の記録は [VER-WLD-001](VER-WLD-001-quest-ios-devices.md))。

## 関連

- [DES-DOG-002 おもちゃの挙動](DES-DOG-002-toy-behaviour.md): 犬別ディスクと識別色。
- [BUG-DOG-005 iOS のディスク色](BUG-DOG-005-ios-disc-colors.md): 既存の端末別表示の問題。

## 記録

2026-10-11: 起票。モデル改善の実装は未着手。

2026-10-11: ユーザーの追加依頼により、具体案を比較するHTMLを作成。提案の確認待ち。Issueの状態はモデル改善が未着手のため todo を維持。

同日、ChromeでPC幅1200px・スマホ幅390pxの表示、案・犬別色・裏面の切替、相談用メモの生成を確認。横溢れ・JavaScript例外なし。実装担当によるセルフレビューに加え、セッション内の独立担当 `review_frisbee_proposal` が提案内容を静的レビュー。顔が縁を透けて見える描画の指摘を修正し、担当が該当コードを再確認した。Unityの実機検証とは別の記録。

同日、ユーザーがA案の採用を了承。IssueとHTMLに決定を反映した。モデル制作・Unityへの適用は未着手のため todo を維持。

2026-10-12: メッシュ制作済み・Unity 差し替えは統括。A案を vrc-asset-lab の `assets/frisbee/` (0.1.0、[IMP-AST-004](../../../../../vrc-asset-lab/docs/development/issues/review/IMP-AST-004-frisbee.md)) でヘッドレスの Blender 5.2.2 により生成し、[KH_Frisbee_A.fbx](../../../Assets/KominkaHillside/Models/Frisbee/KH_Frisbee_A.fbx) へコピーした。断面はHTMLの `profiles.A` を円弧で滑らかにした回転体 (周方向48)。外径240.0 mm・外周最大厚24.0 mm (断面半径12 mmの丸い縁)・中央厚4.0 mm・裏のくぼみ16.0 mm・顔の面は直径160 mmの平面 (表 y=+8 mm、裏 y=+4 mm、外周の上端より4 mm低い)。1,728三角形・866頂点、1材質、テクスチャなし。UVは表裏の平面投影2島で、顔の円は歪みなし (面積比1.000)。退化・非多様体・自己交差0、FBXを読み戻して三角形・寸法・UV・法線が一致。Unityでの範囲は x・z ±0.120 m、y ±0.012 m の予測 (原点は外周の厚みの中央 = 既存のBoxColliderの中心)。`.meta` は `Models/WashTools/Sponge.fbx.meta` を写して GUID だけ新規 (Materials None・Animation なし・Read/Write オフ)。

同日、Editor 側 ([KH_FrisbeePrototypeEditor.cs](../../../Assets/KominkaHillside/Scripts/Editor/KH_FrisbeePrototypeEditor.cs)): `Tools/KominkaHillside/Frisbee/6 - Apply Rounded Disc Mesh` が作業中のシーン (本シーンは6枚、試作シーンは7枚) の KH_Frisbee の MeshFilter をFBXのメッシュへ替え、localScale を (0.24, 0.012, 0.24) から 1 へ、BoxCollider の size をワールド寸法 0.24 × 0.024 × 0.24 m のまま換算し、FaceTop / FaceBottom をメッシュから測った面の 1 mm 外 (y=+9 mm / +3 mm) へ置き直す。材質・Rigidbody・Pickup・ObjectSync・KH_Frisbee は触らない。拒否条件 (メッシュの bounds、ディスク数、Collider、未知の子、プレハブ) は変更前にすべて検査し、保存はしない。`Frisbee/6a - Verify Rounded Disc Mesh` が厳密に検査し (全枚が新メッシュ・Collider と Rigidbody は BuildDisc の値・顔の位置・顔の画像)、`Course/93` も6枚が新メッシュかを見る (`roundedDiscs=6/6`)。`Frisbee/5` は試作シーンの円柱 (Frisbee/4 を回さない限り残る) も「整合した円柱」として通し、INFO で知らせる。BuildDisc も新メッシュで作る形に直した (Frisbee/4・Course/92 は回さない前提)。実行時のコードは変えていない (KH_Frisbee・KH_ToyRecall のディスクの大きさは `radius` 0.12 を使い、localScale を見るのはボールだけ)。scratchpad の一時 csproj で実行側・Editor 側とも0エラー。Unity での取り込み・メニュー実行・Play は未実施。
2026-10-12 (統括): Unity に取り込み、Frisbee/6 Apply Rounded Disc Mesh converted=6、Frisbee/6a PASS、Course/93 FRISBEE_ANYWHERE_OK roundedDiscs=6/6、シーン保存 (本シーンの 6 枚が新メッシュ・scale 1)。コミット ec4dcf6 (コード・FBX)・d86a3a5 (シーン)。ラボ 106471a / 57fb06f (adopted_by)。Frisbee/5 は試作シーンを追加で開くため省略。
