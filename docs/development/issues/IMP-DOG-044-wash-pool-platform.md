---
id: IMP-DOG-044
type: IMP
system: DOG
status: review
priority: Medium
summary: 洗浄台の種類にプールを追加し、洗い場ごとの写真選択から切り替えられるようにする
next: 配置済み。主洗い場の斜面は台の最低の高さ (0.22 m、プールと檜を選んだ時だけ) で解消。ユーザーの PCVR で水面の透け方・泡との描き順・主洗い場の高さと水の中の草を確認。地形を平らにする (a) などへ替えるかはユーザー
updated: 2026-10-11
---

# IMP-DOG-044 洗浄台の種類にプールを追加

## 依頼

2026-10-08 ユーザー：「洗浄の台種類の一つに、プールがあると良さそう。プール追加のイシュー登録しといて。」

[洗いの追加アイテム企画 DES-DOG-008](DES-DOG-008-wash-paid-tools-and-effects.md)の洗い場の種類として追加する。[写真選択 IMP-DOG-043](IMP-DOG-043-wash-premium-selection.md)の既存の台選択へ組み込む。今回は起票のみで、モデル制作・Unity実装は未着手。

2026-10-10: [アイテム採否の回答JSON](../../../artifacts/wash-item-review/2026-10-10/vanimal-wash-review-2026-10-10T07-47-47-617Z.json)でプール候補を「いる」と確認。形状や水面の具体的な改善コメントは空欄。追加方針の再確認として記録し、未制作の形状承認とは扱わない。

## 対応範囲

- プール型の洗浄台を制作し、写真と説明を用意する。VAnimalのブランド方針と課金アイテムとしての素材品質を引き継ぐ。
- 選択した洗い場だけの台を切り替える。既存の犬に応じた台サイズ、高さ調整、中央支柱との適合を確認する。
- Android・スマホを含む対象端末を想定し、メッシュ・テクスチャ・水面の描画負荷を抑える。

## 設計時に決めること

- 外形、縁の高さ、深さ、水位、水面の見せ方。
- 犬の立ち位置と出入り、既存の洗浄操作や支柱を妨げない寸法。
- 洗い場セットの写真・説明・リスティング画像の更新範囲。販売条件は既存企画を参照する。

泳ぐ動作・浮力・水の物理シミュレーションは今回の依頼には含まれていない。必要性は設計時に判断する。

## 完了条件

- プールの見た目を確認し、既存の写真選択から洗い場ごとに切り替えられる。
- 犬のサイズ変更・台の高さ変更でも、犬・水面・縁・支柱の位置関係と洗浄操作が破綻しない。
- PCVRとAndroid等の対象端末で見た目・操作・負荷を確認し、複数人での選択共有を確認する。確認結果は実施方法とともに記録する。

2026-10-11: 設計案を [docs/plans/2026-10-11-wash-items-design.md](../../plans/2026-10-11-wash-items-design.md) §4 に作成 (推奨は今の1.3 × 0.7 mに合わせた浅い角丸長方形、立つ面の高さは変えず縁だけ立ち上げる、水位は足首より下)。ユーザーの判断待ち: Q13 素材と形、Q14 水面の見せ方。犬ごとの台の大きさへの追従は [IMP-ENV-013](IMP-ENV-013-wash-platform-size-per-dog.md) (入江プロジェクトの場所の回答待ち) の決定まで寸法を決めない。

決定: 2026-10-11 (ユーザー「推奨でOK」) [設計案](../../plans/2026-10-11-wash-items-design.md) の Q13・Q14 (帆布 + 木枠の角丸長方形、薄く透ける水面) を推奨どおり採用。

2026-10-11: 企画画像 (各 A / B の 2 案) を作った。画像はラボ `vrc-asset-lab/assets/wash-premium/design/2026-10-11/`、確認用ページは [artifacts/wash-concept-review/2026-10-11](../../../artifacts/wash-concept-review/2026-10-11/README.md)。ユーザーが案を選んでから Blender で制作する。

2026-10-11 (ユーザー回答 [JSON](../../../artifacts/wash-concept-review/2026-10-11/vanimal-wash-concept-2026-10-11T01-43-01-752Z.json)): 案 A (木の板の外枠・丸い深緑の縁・帆布) で、形は「円形がいいかな」。Q13 の「1.3 × 0.7 m の角丸長方形」から変わるので、円 (直径 0.95 m まで。人が棚側に立つ空き 0.5 m のため) と楕円 (1.3 × 0.7 m) の 2 回目の企画画像で確認中 ([round2](../../../artifacts/wash-concept-review/2026-10-11/round2/index.html))。円なら犬ごとの大きさ (IMP-ENV-013) は縦横同じ倍率で拡縮する。
決定: 2026-10-11 (ユーザー) プールの作りは企画画像の案 A。形 (円 / 楕円) は 2 回目の回答待ち。

2026-10-11 (ユーザー 2 回目の回答 [JSON](../../../artifacts/wash-concept-review/2026-10-11/round2/vanimal-wash-concept-r2-2026-10-11T02-02-00-932Z.json)): 形は 2 (楕円)。
決定: 2026-10-11 (ユーザー) プールは楕円 1.3 × 0.7 m、作りは案 A (木の板の外枠・丸い深緑の縁・帆布)。Q13 の角丸長方形は楕円に置き換え。犬ごとの大きさ (IMP-ENV-013) は縦横別の倍率のまま。ラボで 0.4.0 として Blender 制作に着手。

## 段 A 実装 (2026-10-11、未適用)

[IMP-DOG-049 の「段 A 実装」](IMP-DOG-049-wash-water-tools-and-reactions.md#段-a-実装-2026-10-11未適用) と同じ変更・同じ手順 (コンパイル → 12 → 43 → 41 → 21b → 49 → 44 → 21 → Wash/12 → 保存 → 45)。**C# / U# コンパイル 0 (2026-10-11、統括)。** Unity の操作・保存・Play は未実施。 プールに関わる所:

- 品の番号 **19 = PoolBath** (台、`Groups` 3)。`Order` の末尾 (洗い場タブで檜の次)、名前は語 45 (プール / Pool ほか 13 言語、AI 作成・校正なし)。洗い場のセット説明 (語 18) を「檜の台かプール」に、41 のキャプションを「香りのよい檜の台と、水を張ったプール。」にした。
- 取り込み (`WashPremium/12`): `PoolBath.fbx` の `PoolBath_Floor / _Wall / _Rim` → 新 `PremiumWash_Addons` (Standard 不透明、`PremiumAddons_*` 1024、Android ASTC 6x6)、`PoolBath_Water` → 新 `PremiumWash_Water` (Standard Fade、色 (0.62, 0.80, 0.84, α .38)、なめらかさ .55、映り込みなし、影なし、**renderQueue 2990**)。三角形 3,346 (444 + 1,568 + 1,280 + 54)。
- 水面の描き順: 洗いの粒子は 3000、犬の濡れ・泡の殻は 3010 なので、水面 → 粒子 → 殻の順に描く (水越しの足先の泡は殻が上に描かれる)。物ごとの前後の並べ替えではないので、**泡・しぶきと水面の見え方は Play / 実機で確かめる** (CHECK はまだ開いたまま)。
- 配置 (`WashPremium/49`): 各洗い場の `Lift/WashTable` の原点 (檜と同じ置き方、拡縮 1)。選んでいないときは非表示、選ぶと無料の台の見た目が隠れる (従来の台の仕組み)。支柱の取り付け深さ `BathDepth(19) = .036` (ラボの `PoolBath_SupportMount` が原点の 0.036 m 下)。層 17 の長方形の当たり判定はそのまま (楕円の外の四隅に犬が立てる件はラボの注記どおり未対応)。犬ごとの大きさ (IMP-ENV-013) はプールを選んでいる間も掛けない (ラボの既定の逃げ道)。
- 検査 (44): `bathDepths` (檜 .038・プール .036)、プールが WashTable の原点にあること、**支柱の取り付け深さの実測** (天板 Collider の下面 − `*_SupportMount`、檜とプールの両方、1 mm 以内。シーンの WashTable の Collider は中心 y .015・厚さ .03 で下面が原点)、水面の材質と Queue (2990 < 粒子 3000 < 殻 3010)、論理で「台をプールにすると檜が隠れて支柱の深さが .036」。21: Prefab の子ごとの材質と三角形、透明材質。
- 45 の証拠画像は台をプールにして撮る (`artifacts/wash-listings/2026-10-11-040/evidence/station.png`)。

残り: 水面の見た目 (透け方・色) と泡との描き順、縁の高さ (後ろ .150 / 前 .120) と洗う手・道具の届き方、犬の昇降・大きさを変えたときの犬・水面・縁・支柱の関係 (Play)。PCVR・Android・2 人での選択共有は未確認。

### 段 A の適用結果 (2026-10-11、統括)

`2bf922e` で 4 洗い場の台の枠にプールを配置 (44 は 962 PASS / FAIL 0、21 PASS、Wash/12 FAIL 0)。台の枠の写真とリスティング画像は `artifacts/wash-listings/2026-10-11-040/` (04-station は檜とプール)。

**見つけた問題 (ユーザーの判断待ち):** 主洗い場ではプールが地面に半分埋まり、縁だけが C の字に見える (`artifacts/wash-listings/2026-10-11-040/evidence/station.png`)。主洗い場の台の足元は斜面で、いちばん高い地面が台より 0.185 m 上にあり、台は Z ファイティングを避けてユーザーが置いた高さ (局所 y −0.028、IMP-ENV-007) のまま。高さ 0.22 m のプールは坂の上側が埋まる。候補: (a) 主洗い場の台の足元の地形を平らにする、(b) 主洗い場だけ台の基準を上げる (Z ファイティングを避けられる高さで)、(c) 主洗い場ではプールを選べなくする。推奨は (a)。道中の 3 か所は画像では確かめていない (Play で見る)。

2026-10-11 (統括): 斜面に埋まるのは主洗い場だけ。Wash/12 (`293b7db` の時点) で道中の 3 か所は台が地面に乗っている: `PASS course: KH_WashStations/River (川辺): WashTable seated on the ground (local y -0.009, highest ground under it -0.009)`、`Upper (奥広場) ... (local y 0.000, highest ground under it 0.000)`、`Hillside (展望のベンチ) ... (local y 0.000, highest ground under it 0.000)`。主洗い場は `kept at the user's placement (local y -0.028; highest ground under it 0.185)`。犬ごとの大きさ (IMP-ENV-013) ではプールは拡縮しない (1.3 × 0.7 m のまま)。

## 主洗い場の斜面: 台の最低の高さ (2026-10-11、未適用)

**決定 (統括、元に戻せる):** 上の候補 (b) の形で、主洗い場の台の基準は動かさず、**地面に埋まる台 (プール等) を選んでいる間だけ、その洗い場の台を自動で最低の高さまで上げる**。(a) 台の足元の地形を平らにする、(c) 主洗い場ではプールを選べなくする、はユーザーの判断のために残す。選んだときの戻し方:

- (a) 地形を平らにする: 台の品のいちばん低い点より地面が下がった分だけ効く。平らにした後で 50 を流し直すと、地面が台の品の底より 1 mm 以上高い所が残らなければ 0、残れば小さくなった値になる (50 は毎回測り直して書く)。
- (c) 主洗い場でプールを選べなくする: 50 は洗い場ごとの無効化を知らず、どの洗い場でも全部の台の品を測って書く。値が残っていても選ばれなければ使われないが、選べなくするにはコードの変更が要る: 洗い場ごとの「出さない品」(例 `KH_WashSelection` の品ごとの可否の配列) を `_Valid` / `ReqSelect` と板 (`KH_WashSelectionBoard`) の表示で見て、50 はその品を 0 にする (44 も同じ規則で測る)。今は無い。
- (b) (この節の実装) は今の方式そのもの。仕組みごと戻すなら `KH_WashSelection._Apply` の `call.minLift` を書く 1 行を外す (台は従来どおり犬がいなければ 0)。

設計:

- 洗い場ごと・台の品ごとの最低の高さ `KH_WashSelection.bathMinLifts[品番号]` (同期しない)。各クライアントが同期済みの `selectedBath` から `_Apply` で `KH_WashStationCall.minLift` (`[NonSerialized]`) へ書く。新しい同期変数・ネットワークイベントは無い。
- 台の高さ = max(犬がいる間は同期した lift・いなければ 0, minLift) (`KH_WashStationCall._LiftTarget()`。`Update` は同じ式を呼び出し無しで毎フレーム計算するので、台を切り替えて minLift が変わると止まっていた台も動き出す)。**犬がいなくても台は minLift で止まる** (プールは犬の有無と関係なく表示されるため)。IMP-DOG-035 の「犬がいない時は 0」は同期した lift の規則として残り、台そのものは主洗い場でプールを選んでいる間だけ 0 に戻らない。
- 台 ▲▼ は今まで通り同期した lift を 0.1 m 刻みで動かす。lift が minLift より低い間は台は minLift のまま (表示も今まで通り同期した lift)。0 で「台を下げる」は従来どおり何もしない。
- 犬は `Lift/Anchor` (liftRoot の子) に乗るので台と一緒に動く。解放されて台から下りる処理 (`KD_DogWanderController.HillsideStationLeave.cs` の 2 か所) は「下り切った」を 0 ではなく `_LiftRest()` (= minLift) で判定する (0 のままだと下り切らず、期限で地面へ瞬間移動する)。そこから地面までは従来の自分で下りる処理 (liftSpeed)。支柱の見た目 (`KH_WashSupportVisual`) は台の Collider の下面を毎フレーム追う。自分の床 (`KH_WashPersonalFloor`) は変えない。
- 値の決め方 (`WashPremium/50` と 44 が同じ関数 `FitMinLift` を使う): 台が lift 0 (Lift が洗い場の原点) のとき、台の品の模型のいちばん低い点 (メッシュの箱、洗い場の座標) と、その箱の広さ (楕円のプールは外接長方形 = 安全側) を 5 cm 格子で測った地面 (Wash/11・12 と同じ `StationGround`、地形だけ) のいちばん高い点の差が埋まり込み。1 mm 以下 (地面に乗っている。道中の洗い場) なら 0、それより深ければ 埋まり込み + 0.01 m を mm に切り上げ。0.1 m 刻みには丸めない (刻むのは同期した lift だけ)。liftMax を超えたら 50 は書かずに止まる。

ファイル:

- `Assets/KominkaHillside/Scripts/KH_WashStationCall.cs` (`minLift`、`_LiftRest`、`_LiftTarget`、`Update` の目標)
- `Assets/KominkaHillside/Scripts/KH_WashSelection.cs` (`bathMinLifts`、`call`、`_Apply` で `call.minLift` を書く。`_Ready` には入れない: 50 の前のシーンは従来どおり)
- `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.HillsideStationLeave.cs` (下り切りの判定 2 か所)
- `Assets/KominkaHillside/Scripts/Editor/KH_WashSelectionEditor.MinLift.cs` (新規: メニュー `Tools/KominkaHillside/WashPremium/50 - Fit Bath Min Lift`、44 の検査 `VerifyMinLift` / `VerifyMinLiftLogic`)
- `Assets/KominkaHillside/Scripts/Editor/KH_WashSelectionEditor.Verify.cs` (44 から上の検査を呼ぶ。45 の撮影で台を minLift へ上げる)
- `Assets/KominkaHillside/Scripts/Editor/KH_DogWashSetupEditor.Station.cs` (`StationGround` を internal に。中身は同じ)
- `Assets/KominkaHillside/Scripts/Editor/Testing/KH_CourseWalkHarnessEditor.WashHeight.cs` (`W-height-stand` の始めに主洗い場の台の品を無料の台へ戻す `WhFreePlatform`。期待値は変えない)

50: 汚れたシーンは拒否、4 洗い場を全部測ってから書く、`call` の配線と `bathMinLifts` を書いて proxy → Udon、シーンを dirty にするだけで保存しない、2 回目は `changed=0`。記録は `Temp/WashSelection/fit-min-lift.txt`。
44 に足した検査: 洗い場ごとに `bathMinLifts` を表示し、測り直した値と 1 mm 以内、台以外の品は 0、liftMax 以内、`call` がその洗い場の Call、backing にも同じ値。主洗い場 (stationIndex 0) で、台の品ごとと台なしについて `_Apply` → `call.minLift`、`_LiftTarget` (犬なし / lift 0 / 0.1 / liftMax) = max(lift, minLift) を Edit mode で確かめる。Wash/12 の検査は変えていない (Lift は原点、台はユーザーの置いた高さ、同期した lift の検査はそのまま)。 **44 の最低の高さの値の検査は 50 と同じ関数 `FitMinLift` で測り直すので、独立した検査ではない** (配線・backing・保存後も値が残ることと、規則の実行時の本体 `_Apply` / `_LiftTarget` を確かめる)。値が見た目に正しいことの独立の確認は、保存後に `WashPremium/45` で主洗い場をプールで撮った `artifacts/wash-listings/2026-10-11-040/evidence/station.png` (45 はプールを選んだ間だけ主洗い場の台を `_LiftRest()` = minLift まで上げて撮り、元に戻す。Edit mode では `Update` が動かないため。プールが斜面から出ているかを画で見る。犬の上り下り・遅れて入った人の高さは Play / 実機)。ハーネス `W-height-stand` (Course/100、主洗い場の台の実際の高さ 0.2 → 0.1 → 0 を見る) は始めに主洗い場の台の品を無料の台へ戻す (`KH_CourseWalkHarnessEditor.WashHeight.cs` の `WhFreePlatform`、Call.minLift が 0 であることも見る)。

統括の実行順: `bash scripts/unity-compile.sh --udon` → `WashPremium/50` → `WashPremium/44` → 保存 → `Wash/12` → `WashPremium/44` (50 はシーンを dirty にし、Wash/12 は保存していないシーンを拒否する: `KH_CourseGroundEditor.Idle`)。期待: 主洗い場のプールだけ (檜も埋まるなら檜も) 0 より大きく、道中の 3 か所はおそらく 0。道中で小さな値 (例 0.012) が出たら、5 cm 格子が台の据え付け (Wash/11 の 5 点) より高い地面を見つけたということで不具合ではない (50 の記録で確かめる)。

危険と残り:

- 主洗い場でプールを選んでいる間、犬の立つ高さが minLift (およそ 0.2 m 程度の見込み) だけ上がる。洗う人の手の高さが合わないときは自分の床 (▲) を使う必要がある。台の表示は同期した lift (0.0 m) のままなので、表示と見た目の高さが食い違う。
- lift が minLift より低い間の ▲ (0.1、0.2 m) は見た目が動かない。
- 呼ばれた犬は minLift の台へ 1.5 m/s で上がる (0.2 m なら約 0.15 s)。解放後は台の最低の高さから地面まで自分で下りる (0.5 m/s。プールの床をすり抜けるように見える。縁 0.12〜0.15 m を歩いて抜けるのは道中の洗い場でも同じ)。
- 入室直後・台を切り替えた直後は、台が 0.5 m/s で minLift まで上がる間 (0.4 s 程度) だけプールが埋まって見える。
- 無料の台 (すのこ) の主洗い場での埋まり方は従来のまま (minLift は台の品を選んでいる時だけ)。
- 2 人以上・遅れて入った人で同じ高さになること、犬の上り下り、主洗い場のプールの見た目は Play / 実機で確かめる。

### 主洗い場の斜面: 適用 (2026-10-11、統括)

適用の最初の回で、基準を「台の最低点」にしていたため、平らな道中の 3 か所まで檜・プールで 4.6〜4.8 cm 持ち上がる結果になった (有料の台の外装はもともと台の上面から 3.6〜3.8 cm 下へ沈む設計で、10-08 に了承済みの檜の見た目)。保存せずに破棄し、基準を **台の座 (`Lift/WashTable` の局所 y) より地面がどれだけ高いか** に直した (座が `Lift/WashTable` でなければ止める。Codex 短い再確認で「新基準の補正計算は問題なし」、低 2 件を反映)。

結果 (`WashPremium/50`): **主洗い場 檜 0.222 m・プール 0.223 m**、道中 3 か所はすべて 0。→ 44 **1,011 PASS / FAIL 0** → 保存 → Wash/12 **FAIL 0 (5,387)** → 保存後の 44 も 1,011 PASS。撮影 45 (`artifacts/wash-listings/2026-10-11-040/evidence/station.png`) で、主洗い場のプールが地面から出て楕円の形がそのまま見えることを目視で確認。

気づいた点 (Play で見る): 主洗い場ではプールの水の中に地面の草が少し突き出して見える (草の描画は地形のもの)。台を選んでいる間、主洗い場の犬は約 0.22 m 高く立つ (洗う人は自分の床の ▲ を使う)。

### ハーネスのケース追加 (2026-10-11、未実行)

44 の最低の高さの検査は 50 と同じ関数で測り直すので独立ではなく、Edit mode では `Update` も犬も動かない。実行時の台の高さ・犬の立つ高さ・降台を見るケースを足した。**コードだけ: コンパイル・実行は未実施** (統括)。U# は変えていない。

- 群 `height-minlift` / メニュー `Course/100f2 - Harness Wash min lift` (100f にも入る。`W-height-stand` の後に回る)。ケース `W-height-minlift` (`Scripts/Editor/Testing/KH_CourseWalkHarnessEditor.WashHeight.cs` の `MakeWashMinLiftCases`)。対象は主洗い場 (`KH_WashStation`、stationIndex 0)、犬は柴。
- 手順と期待 (実測を notes に出す):
  1. 始めの選択 (泡・水・仕上げ・台) を記録。柴を放して門の横へ (`WcSetup`)、選択を全部無料へ (`PremiumBind`) → 台が 0。
  2. 板の UI でプール → Owner の応答が成功、`bathMinLifts[19]` が 2026-10-11 の当てはめ **0.223 ± 0.005** (`WhPoolMinLiftFitted`。50 を当て直したら両方を変える)、`_Apply` が `Call.minLift` に同じ値を書く、`_LiftRest()` = minLift。犬なし・同期の lift 0 のまま、台 (`liftRoot` の局所 y − `_liftBase.y`) が 3 s 以内に `_LiftRest()` ± 0.005 へ上がる (かかった秒を出す。0.5 m/s で約 0.45 s の見込み)。
  3. `ReqCall` で柴を呼んで着かせ 1 s: 台は minLift のまま、根元の y が anchor の y と 0.03 m 以内、水平 0.05 m 以内 (anchor の地形からの高さを出す)。
  4. 板の 台 ▲ ×2 (lift 0.2 < minLift) → 台は minLift のまま動かない。▲ をもう 1 回 (lift 0.3) → 台が 0.3 へ、根元も一緒に上がる。
  5. 板の おわり → 解放、同期の lift 0、台は minLift まで戻って**それより下がらない** (最低値を出す。0.3 → 0.223 は約 0.15 s、+0.5 s まで許す)。台が下りる間は根元が anchor に乗ったまま (差 0.03 m 未満。その観測が 1 回も無ければ空振りとして FAIL、観測数を出す)。minLift に着いたら犬が自分で地面へ下りる (`_leaveOwnEase`)。地面 (地形だけ、Wash/12・50 と同じ `StationGround`) より 0.03 m 以上下に根元が行かない、跳ね落ち (1 tick の下がりが liftSpeed × 経過 + 0.02 m を超える) が無い、地面に着いてから立入りの円の外へ出る (`WcLeaveLimit(0.3)` 以内)、歩き出しの期限切れ (`_leaveTimeouts`) が増えない。
  6. 0.5 s 後も犬のいない台は minLift のまま。板の「リセット」 (台も無料へ) → minLift 0、台が 3 s 以内に 0 へ。
  7. 始めの選択を戻す (直接書いて `_Apply`。シーンは保存しない)。
- ClientSim で確かめられないもの: 2 人・途中参加の人が同じ高さを見ること (minLift は同期した `selectedBath` から各端末が出す)、プールの床をすり抜けて下りる見た目、台を切り替えた直後に埋まって見える 0.4 s、洗う人の手の高さ → Play / 実機。
- 回すかは相談。回すなら `100f2` 単独 (柴を台へ呼ぶ 1 ケース、1〜2 分の見込み)。

### 追加したハーネスの実行 (2026-10-11、統括)

Codex 相談 (「100p4 → 100b1 → 100f2 を単独で順に。100f2 の降台の観測なしで PASS する穴と 100b1 の連射抑制を飛ばして PASS する穴を先に補強」) のとおり補強してから実行: `Course/100p4` (水鉄砲の 1 発・ホースの押している間) run `20261011-073459` **WASH_PASS 2 ケース**、`Course/100b1` (犬の水の反応: 水鉄砲で跳ねる・ホースでパクパク・無料シャワーは足上げだけ・2 本持ちは反応しない) run `20261011-073759` **WASH_PASS 4 ケース**、`Course/100f2` (主洗い場でプールを選ぶと台が 0.223 m へ上がり、犬が乗って下りる) run `20261011-073959` **WASH_PASS 1 ケース**。いずれも sceneUnchanged true。100b1 のメニュー名の「/」が Unity でサブメニューの区切りになり起動できなかったので「,」に直した。起動の 1 回目の空応答 (既知の間欠) は再試行。

ClientSim で確かめられないもの (変わらず未確認): 他の端末での 1 発・反応の受信、途中参加、Manual 同期で連射がまとまる見え方、Owner 交代、ノズルの Raycast と弧を描く粒の差、VR の握りと Use、跳ぶ・顎・声の見た目、2 人で同じ台の高さに見えること。
