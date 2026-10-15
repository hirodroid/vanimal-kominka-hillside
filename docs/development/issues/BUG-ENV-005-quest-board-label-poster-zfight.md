---
id: BUG-ENV-005
type: BUG
system: ENV
status: review
priority: High
summary: Quest 単体だけで看板 (木と板の境)・ボトルのラベル・塀のポスターがちらつく (Z ファイト)。PC・スマホでは出ない。生成物の平行な面の重なり (10 / 2 / 0.3 / 0 mm) を、隙間の調整ではなく構造で無くす
next: "ユーザーの Quest 3 (play-checklist D34): 看板 3 基・アイテム案内 4 枚・犬紹介の台・塀のポスター 2 枚・ボトル 4 本・写真ボードで 1〜3 m と 5 m 以上からちらつきが消えているか、見た目の変化 (紙と木が同じ平面、棟が薄い、写真ボードの枠) が許容できるか。質問 3 件 (10-14 より前の Quest ビルドで出ていたか・Near の上書き設定・古民家の外壁) の回答も"
updated: 2026-10-15
---

# BUG-ENV-005 Quest だけで看板・ボトルのラベル・ポスターがちらつく (Z ファイト)

## ユーザー報告 (2026-10-15、原文)

「PC/スマホだと OK だけど、Quest だと看板とかが ZFight しちゃっている。あと、ボトルのラベルも。看板は木とボードのくっついているところとか。あとポスターもかな。ちらちらする。Z-fight なのかな？ Quest だけおきちゃう。」

同日「起票後、対応お願いします」で修正まで依頼。公開は 2026-10-16 ([overview](../overview.md)) なので High。

## 対象の候補と、生成側で測った隙間

ユーザーが見た個体は未特定 (看板・ボトル・ポスターは全部生成物で同じ作りなので、同種を全部直す)。

| もの | 重なっている面の組 | 隙間 | 生成元 |
|---|---|---|---|
| 案内板 3 基 (`KH_GuideBoards/Asobikata・Map・Kominka`)、アイテム案内 4 枚 (`KH_ItemGuideBoards/*`)、犬紹介の台 (DogBookStand) | 紙の Canvas (ローカル z 0) と背板 `Structure/Back` の前面 (z 0.01。中心 BackZ 0.03、厚み 0.04) | 10 mm | `KH_GuideBoardsEditor.BuildStructure` (Guide/1・22、DogBook) |
| 同上の屋根 | 棟 `Ridge` と屋根板 `RoofFront` / `RoofBack` | 0 mm、7 cm² × 4 ([BUG-ENV-004](BUG-ENV-004-prop-zfight-photoboard-guideroof.md)) | 同上 |
| 塀のポスター 2 枚 (`KH_WallPosters`) | 画像の Canvas と板 `Back` の前面 | 2 mm (URL 欄は 4 mm) | `KH_GuideBoardsEditor.WallPosters` (Guide/20) |
| シャンプーのボトル 4 本 (洗い場の `KH_WashTool` ToolBottle) | ラベル `Printed sleeve` (半径 0.0303、32 角) と胴 `Bottle body` (半径 0.030、32 角・同位相) | 0.3 mm | `scripts/art/prop-finish.py` → `Models/PropFinish/Bottle.fbx` (Course/162) |
| 写真ボードの仕上げ板 (`KH_PhotoBoard/Structure/FinishedVisual`) | 自身の領域同士 | 2 mm、219 cm² (BUG-ENV-004) | 同じ `prop-finish.py` の PhotoFrame |

「木とボードのくっついているところ」は、紙 (Canvas) と背板の 10 mm、または棟と屋根板の 0 mm のどちらか。

## 原因の見立て

- **PC で出ないのは、PC (DirectX 11) が reversed-Z の浮動小数点深度で、数 mm の重なりでは争わないため。** Quest (Android) は固定小数点の深度で、刻みは 距離² / (Near × 2^bit)。
- **Near が 2026-10-14 に 0.01 m になった** ([BUG-WLD-002](BUG-WLD-002-camera-clipping-and-scenery-culling.md)、ユーザー承認。それまでは Reference Camera 0.3 → VRChat が 0.05 へ丸めた値)。刻みは一律 5 倍粗くなった。`KH_ZFightScanEditor.Fix.cs` の深度の注記と古民家の 4 mm の目標は Near ≤ 0.05 を前提にしており、**この前提は古い** (本票で注記を直す)。
- 24-bit なら Near 0.01 でも 3 m で 0.05 mm 刻みで、10 mm の紙は争わない計算。**10 mm が 2〜3 m で争うには 16-bit 相当の精度が要る** (Near 0.01・16-bit: 1 m 1.5 mm、2 m 6 mm、3 m 14 mm、5 m 38 mm)。Quest の深度の bit 数は未確認。ユーザーの Near の上書き設定 (Dynamic / Forced、最小 0.001) でもさらに粗くなる。
- どの前提でも、**距離が伸びればどんな隙間も争う**。だから直し方は mm の調整ではなく「平行に重なる面を無くす」。

ユーザーへの質問 (回答を待たずに直す。答えで精度の下限が分かる):

1. Quest で見たビルドは 10-14 の Near 0.01 の後にアップロードしたものか。10-14 より前の Quest ビルドでは出ていなかったか
2. VRChat の設定で Near の上書き (Dynamic / Forced) を使っているか
3. 古民家の外壁・門 (BUG-ENV-003 で 4 mm に離した) も Quest でちらついているか

## 直し方 (方針: 平行な重なりを構造で無くす)

1. **看板・アイテム案内・犬紹介の台・ポスター** (`KH_GuideBoardsEditor`): 背板・板の Cube を、前面を持たない「盆」のメッシュ (縁 + 側面 + 裏板、紙・画像の矩形の位置に穴) にコードで生成し、Canvas を盆の前面の平面へ置く。紙と木は同じ平面で隣り合うだけになり、重なる面が無い。紙の外に出る UI (題字・札) が木の縁に掛からないことを走査で確かめる。棟と屋根板は面が一致しない組み方へ (体積の食い込みは良い、面の一致が悪い)。Verify (Guide/2・21・23、DogBook/3) には盆メッシュと Canvas の平面の検査を足す (実装の節)。
2. **ボトル** (`prop-finish.py`、Blender): 別オブジェクトの `Printed sleeve` をやめ、胴 1 本の面にラベルの UV 帯を持たせる (胴を下・ラベル・上の 3 区間に分けて同じリング頂点を共有するか、リングごとの UV 切替)。直径 < 0.061・底 −0.085・口先の位置 (Course/163 の検査値)・三角形 ≤ 2,000 を保つ。再生成は Steam の Blender (`handoff.md` の呼び出し) で、書き戻すのは `Bottle.fbx` (と FinishedVisual を直すなら `PhotoFrame.fbx`) だけ。他の FBX・テクスチャは `git checkout`、`.meta` は触らない。
3. **走査**: `ZFight/7` に、看板 3 基・アイテム案内・犬紹介の台・ポスター・写真ボード・ボトルを対象に、Canvas の各 Graphic を四角形として扱い、平行で重なる面の組 (隙間 ≤ 30 mm) を出すメニューを足す。AI 側の完了条件はこの走査で 0 組。
4. **Near 0.05 への戻し**は本票では行わない (BUG-WLD-002 の承認値)。公開前に Quest で確かめられない場合の 1 行の緩和策として、ユーザーの判断に委ねる。

## 範囲外

- 古民家の外壁・門 (BUG-ENV-003、4 mm に離して review 中。質問 3 の答えで Quest の精度が分かれば、そちらで再判断)
- 泡銃 (BUG-DOG-013、1 mm。手の距離 0.3〜0.6 m では Near 0.01・16-bit でも刻み 0.5 mm 以下)

## 完了条件

AI 側:

- [x] `ZFight/7` で対象の平行な重なり (隙間 ≤ 30 mm) が 0 組 (変更前 485 組。2026-10-15)
- [x] Guide/1 → Guide/2 (FAIL 4 はアトラス .meta の別セッション分)、Guide/20 → 21、Guide/22 → 23、Photo/4 → 2、DogBook/2 → 3、Course/163、Wash/12 PASS。C# エラー 0 (U# は変更なし) (2026-10-15)
- [x] ボトルの三角形数・bounds・口先の位置を変更前後で記録 (2026-10-15、記録の節)
- [x] `KH_ZFightScanEditor.Fix.cs` の深度の注記を Near 0.01 に合わせて直した (2026-10-15)
- [x] Codex の独立レビュー (2026-10-15、中 4・低 3 を反映)

ユーザー (Quest 3、[play-checklist D34](../../testing/play-checklist-2026-10-02.md)):

- [ ] 看板 (入口 A・地図・古民家の案内板)、アイテム案内、犬紹介の台、塀のポスター、ボトル、写真ボードでちらつきが消えている (1〜3 m と 5 m 以上)
- [ ] 見た目の変化が許容できる (紙・画像と木が同じ平面、柱の出 30 → 20 mm、棟 60 → 28.5 mm で頂に 25 × 27 mm の V 字、軒 6 mm 外、写真ボードは板厚 70 mm・横桟 29 mm・45° 面取り・棟が両端 40 mm 長い・ボルト頭がドーム、ポスターの URL 欄に塀が 5 % 透ける、犬紹介の詳細ページ時に 50 mm の袋が見える)

## 記録

2026-10-15: 起票 (ユーザー依頼)。BUG-ENV-004 の着手条件 (ユーザーの Play でちらつきが見える) が満たされたので、004 の 2 件は本票で一緒に直す (004 は wip、本票を指す)。

2026-10-15 (Opus、Blender 側): `prop-finish.py` の読み戻し検査 `scripts/art/check-prop-overlap.py` (新規。平行・同じ向き・射影で重なる・隙間 ≤ 30 mm・0.1 cm² 以上の三角形の組を数える) で、変更前は Bottle 142 組 157 cm² (ほぼ sleeve と胴の 0.3 mm)、PhotoFrame 101 組 2,289 cm² (BUG-ENV-004 の 219 cm² より多い: 枠と背板 0 / 2 / 17 mm、屋根板の面取りと棟 3〜25 mm、ボルト頭と柱 5 mm)。直し: ボトルは sleeve を廃し胴の UV 帯 (−.054〜.027、tile 3、位相は旧 sleeve の 90° に合わせた) に、胴の上蓋 (キャップの蓋と 19 mm で平行) を削除。三角形 1,184 → 1,158、bounds 直径 .0606 → .060、底 −.085・口先は不変。写真ボードは枠を全奥行きの部材に、背板を開口だけの板に、屋根板と棟を 45° の面取り 1 段 (面が 25° と 0° で平行にならない)、上の板を頂で 7 mm 短く、棟を両端 40 mm 延長、ボルト頭をドーム。三角形 1,920 → 1,656。両方 0 組。書き戻しは Bottle.fbx・PhotoFrame.fbx と blend だけ (他 3 FBX は checkout、PNG はバイト一致、.meta 不変)。
- 残件 (同じスクリプトの別モデル。本票の報告には無い): Shelf 170 組 777 cm² (0 mm 38 cm²、5 mm 174 cm²)、Rack 31 組 90 cm²、RackGuide 0。洗い場の棚・リード掛けも Quest でちらつく可能性が高い。本票の主対象が済んでから着手するか別票にする (ユーザー判断)。
- 写真ボードの追加 (同日、統括判断): 写真面の Quad (z −6 mm) と Canvas の Cell 画像、Canvas (z 0) と背板の前面 (z +10 mm) も平行な重なり。背板を盆 (HeaderPlate・Paper の矩形に穴、裏板は外向きの面だけ) にし、Paper・Cell を写真の矩形を避ける格子にして写真の Quad を z 0 に揃える (`KH_PhotoGalleryEditor.Place.cs`)。
- 写真ボードの追加分 (同日、Opus): PhotoFrame の背板を盆 (縁は z 0 で HeaderPlate・Paper の矩形に穴、穴の壁 z 0〜0.068、裏板 z 0.068 は外向きの面だけ)。枠は全奥行き (z −0.007〜0.07、板厚 0.05 → 0.07)、横桟 32 → 29 mm、三角形 1,696、0 組。`KH_PhotoGalleryEditor.Place.cs`: 写真の Quad を z 0 (PhotoFrontZ)、Paper を Graphic 無しの親にして和紙 13 片 (Washi0..12) と Cell ごとの 12 mm 帯 4 本に分割 (写真の矩形と辺で接するだけ)、ページ送りの板を −462 → −458 mm で紙の内側へ。Verify に「写真は PhotoFrontZ、Canvas から 30 mm 以内の Graphic は写真の矩形と重ならない」を追加。**既知の限界**: 空の写真枠 (KH_PhotoAlbum が Quad を消す) が抜けて見えないよう、Canvas の 35 mm 後ろに暗い裏当て 2 枚 (PaperBack / HeaderBack、UI) を置いた。写真面と平行なので、16-bit・Near 0.01 の最悪条件では約 5 m 以上で写真に暗い斑点が出得る (ZFight/7 の 30 mm 閾値には掛からない)。空の枠に暗い印刷面を出す実行時の変更 (U#) は公開直前なので見送り、Quest で見えたら再検討。再構築は Photo/4 (Place Board) → Photo/2 → Course/162 → 163 → Wash/12。

## 2026-10-15 実装 (統括 Fable、実装 Opus 2 名)

**看板側** (`KH_GuideBoardsEditor.Tray.cs` 新規、`.cs` / `.Verify.cs` / `.ItemGuide.cs` / `.DogBookStand.cs` / `.WallPosters.cs`): 背板 `Back` を盆メッシュに (縁 = Canvas の平面 z 0、Canvas の最上位の Image / RawImage の矩形ごとに穴、穴は 50 mm の袋 (犬紹介の台は索引を隠して詳細を 40 mm 手前に出すので、穴が抜けないために袋が要る)、外側の側面と裏面。T 字接合なし、UV は Cube の面と同じ木目)。メッシュは `Assets/KominkaHillside/Meshes/Boards/*_Back.asset` 10 個 (再実行は同じ GUID へ上書き)。構築は Canvas の中身を作った後に行い、描かれる Graphic が穴の中に収まらなければ保存前に例外。ポスターは板厚 20 mm で袋を作れないので穴だけ (背景は塀、画像・URL 欄から塀まで 30 mm)。棟は板の下面から 1 mm 下 (高さ 60 → 28.5 mm)、屋根板 2 枚は各自の斜面に沿って 7 mm 外へ下げて頂で突き合わせ (端面の 1 cm² の重なりが消える。軒は 6.3 mm 外・3 mm 下、頂の V 字の切れ込みは 25 × 27 mm に広がるが棟が下にある)。Verify (Guide/2・21・23、DogBook/3) に IsTray と Canvas z == 0 の検査を追加。`KH_ZFightScanEditor.Fix.cs` の深度の注記を Near 0.01 に直した (24-bit: 3 m 0.054 mm、8 m 0.38 mm で 4 mm は約 10 倍。16-bit: 1.6 m を超えると 4 mm では足りない。Quest の bit 数は未確認)。

**走査** `ZFight/7 - Scan Boards And Props` (`KH_ZFightScanEditor.Boards.cs` 新規): 対象は KH_GuideBoards・KH_ItemGuideBoards・KH_WallPosters・KH_PhotoBoard・KH_DogBookStand と、Body 子を持つ KH_WashTool のボトル 4 本 (泡銃は tool 番号が同じだが BUG-DOG-013 の範囲なので除外)。UI の Graphic を四角形として扱い、平行 (|dot| > 0.999)・同じ向き・射影で 1 cm² 以上重なる・隙間 ≤ 30 mm の組を数える (UI 同士は ZWrite Off なので除外、裏面だけから見える組は back side として数えない)。報告: `Temp/KH_ZFightScan/boards.md` / `.json`。

**Unity の実行 (統括、直列)**: C# エラー 0 (新規 partial 2 本の 1 回目の CS0103 は取り込み途中。2 回目で 0)。変更前 ZFight/7 = **485 組** (看板 155、アイテム案内 243、ポスター 10、写真ボード 24、犬紹介の台 45、ボトル (泡銃を含む) 8)。Guide/1 (GUIDE_BOARDS_FAIL 4 = アトラス .meta の既知の別セッション分。盆の 3 検査 OK) → Guide/22 → 23 ITEM_GUIDE_OK 731 → Guide/20 → 21 WALL_POSTERS_OK 50 (WARN 2 は既存のユーザー決定) → Photo/4 PHOTO_PLACE_OK (位置 (52.44, 1.52, 41.24) yaw 155 を保持) → Photo/2 PASS → DogBook/2 → 保存 → DogBook/3 PASS → Wash/12 VERIFY_OK 5393。中間の ZFight/7 は 24 組 (屋根板 2 枚の端面 1 cm² × 16 + 泡銃 8) → 屋根板の突き合わせを直して Guide/1・22・DogBook/2 を再実行 → **ZFight/7 = 0 組** ([報告](../../plans/evidence/2026-10-15-zfight-quest/scan-boards-after.md))。Course/163 PASS (15 体、ボトル 4 本の底・直径・口先 OK)。

- **Course/162 の潜在バグを直した**: FinishBottle の対象が `tool == ToolBottle` だけで、同じ番号の泡銃 4 本 (IMP-DOG-049) にも Bottle の見た目を足していた (163 が 19 体・口先不一致で FAIL)。Body 子を持つ物に限定し、誤って足した 4 体は一時メニューで外して保存 (一時スクリプトは削除済み)。Course/161 は `survey.json` が無く失敗するが before.unity は書くので 162 は通る (161 は 10-04 の比較用で今回は不要)。
- WashPremium/21 は `Texture import AddonsNormal` で例外。WashPremium の .png.meta は別セッションが変更中で、本票の変更 (泡銃の見た目を Course/162 前の状態に戻しただけ) とは無関係。
- 見た目の変化 (ユーザーの Play で見る): 紙・画像と木が同じ平面 (縁が 10 mm 手前に出て柱の出は 30 → 20 mm)、棟が薄い、軒が 6 mm 外、写真ボードは板厚 50 → 70 mm・横桟 32 → 29 mm・ボルト頭がドーム・屋根板と棟が 45° 面取り・棟が両端 40 mm 長い、ポスターの画像は 2 mm 後ろ (URL 欄は α 0.95 なので塀が 5 % 透ける)、犬紹介の台の板は 10 mm 後ろ (詳細ページ時に 50 mm の袋が斜めから見える)、ボトルのラベルは段差なし。
- 既知の限界: 袋の底 (紙の 50 mm 後ろ) と写真ボードの裏当て (35 mm) は紙・写真と平行。16-bit・Near 0.01 なら 50 mm は約 5.8 m、35 mm は約 4.8 m 以上でちらつき得る (24-bit なら Near 0.001 でも 50 mm は約 29 m、35 mm は約 24 m 以上)。穴の壁 (紙に垂直) は走査に出ないが、粗い深度では紙の縁に細い帯が出得る。
- 文書: 制作票 [2026-10-04-prop-finish.md](../../plans/2026-10-04-prop-finish.md) の三角形数を更新。`Imported/KominkaDogRun/Editor/KD_DogBookEditor.Hillside.cs` のコメント「面より 10 mm 手前へ」は古いが、別セッションが Imported 配下を編集中なので触らない。
- 未着手: 洗い場の棚 (Shelf 170 組) とリード掛け (Rack 31 組)。

2026-10-15 (Codex 独立レビュー、`codex exec -m gpt-6-astra`、コミット前): 高 0・中 4・低 3。中: (1) Verify の IsTray が保存先・影・scale しか見ず穴の無いメッシュでも通る、URL Canvas の z 未検査 → 盆を再生成して照合、全 Canvas の z 検査、(2) 盆アセットを板ごとに保存するので途中の例外でメッシュだけ変わる → 全部を検証してから保存、(3) 走査の対象が無い・重複・読めないでも OK 0 が出る → INCOMPLETE、(4) Blender 検査の重心距離の除外が 30 mm 以内の重なりを落とす → 頂点基準。低: (5) UI と逆向き面の組を一律に数えない → 裏面は保証外と明記し別表、(6) 三角形単位 1 cm² の閾値が 10 × 10 mm の重なりを隠す → 面ごとに集約、(7) 検収条件「屋根の形は同じ」と 35 / 50 mm の距離の数値が記録と不一致 → 本文を直した。観点 1〜4 の生成ロジック・走査の幾何・Place.cs の分割・prop-finish.py の後方互換は問題なし。(1)〜(6) は実装側で反映中。

2026-10-15 (レビュー反映後、統括): Verify に盆の再生成照合 (TrayMatches: 位置・回転・scale・影・アセット名・頂点 0.05 mm 以内) と全 Canvas の平面検査、盆アセットの保存を 1 回の実行で全部検証した後に (失敗は巻き戻し)、走査は必須対象の欠落・重複・読めないで INCOMPLETE、面ごとの集約 (三角形 0.1 cm² / 幅 0.1 mm だけ捨て、群 1 cm² で判定)、裏面は保証外と明記、Blender 検査は頂点基準。再コンパイル (別セッションの KH_CourseTravelJoinEditor.cs の編集途中で一時落ちたので待った) → Verify 4 本 PASS (新しい照合を通る) → Guide/1・22・20・DogBook/2 を再実行して盆メッシュ 10 個の md5 不変 → ZFight/7 OK 0 (ボトル 4、1 cm² 未満 0、裏面 13 は数えない)。ボトルの絞り込み (Body 子) は書き直しで一度落ちたので戻した。review へ (Quest はユーザー)。

2026-10-15: 最終シーンで Photo/2 PASS (fails 0 / warns 0) を再確認。コミット 895cf43。共有 Skill へ知見 2 件 (Quest だけの Z ファイトの仕組みと直し方 → vrc-asset-production camera-design §4、unity-compile.sh の偽 ok → vrc-synaptic-unity)。
