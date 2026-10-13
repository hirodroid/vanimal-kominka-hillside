---
id: BUG-DOG-013
type: BUG
system: DOG
status: review
priority: Medium
summary: 洗浄の有料道具の Z ファイトは泡銃 FoamCannon (銃の後ろ側、胴と鞍の 2 つの蓋が同一平面で 7 cm² 重なる) と特定。ラボの元データから 1.0 mm ずらした 0.3.1 の FBX を書き出し、差し替え済み (走査で重なり 0)
next: "ユーザーの Play (PCVR / Quest): 泡銃を持って回し、後ろ側 (緑の鞍と象牙色の胴の境) のちらつきが消えているか。Quest で残るなら --offset-mm 2 で再書き出し"
updated: 2026-10-13
---

# BUG-DOG-013 洗浄の有料道具 (水鉄砲 / 泡銃) のモデルの Z ファイト

## ユーザー報告 (2026-10-13、原文)

「あとは洗浄用の水鉄砲だったか、もこもこ泡だったか、もうなんかモデルが Z ファイトしているので、これもちょっと直したいです。」

ユーザーもどの品か確かではない。**特定が最初の作業**。

## 候補と出どころ

| 候補 (Prefab `Assets/KominkaHillside/Prefabs/WashPremium/`) | 版 | 取り込みメニュー |
|---|---|---|
| `FoamCannon` (泡もこもこ銃) | 0.3.0 (18 品) | `WashPremium/10` (**再実行しない**決まり、[IMP-DOG-049](IMP-DOG-049-wash-water-tools-and-reactions.md)) |
| `DuckWaterGun` / `FishWaterGun` / `BambooWaterGun` (形違いの水鉄砲) | 0.3.0 | 同上 |
| `WaterPistol` (水鉄砲) | 0.4.0 (3 FBX) | `WashPremium/12 - Import 0.4.0 Visuals` |

元モデルはラボ `C:/work/unity/vrc-asset-lab/assets/wash-premium/` (source / exports / handoff.md)。材質は子の名前で割り当て (`*_Mesh` → `PremiumWash_Tools`、0.4.0 の透明は `PremiumWash_Addons` の Fade、renderQueue 2990)。

見立て (測る前の仮説): 同じ面に 2 枚のポリゴンが重なっている (胴と飾りの継ぎ目、タンクの内外、ノズルの差し込み)。透明材質の品 (水鉄砲の水のタンク) なら、不透明の胴と透明の面が同じ位置にある可能性もある。

## 特定 (2026-10-13)

**品は FoamCannon (泡もこもこ銃)、0.3.0 の品。** 走査 `ZFight/3 - Scan Premium Tools` (`Temp/KH_ZFightScan/premium.md`) で FoamCannon_Mesh に同じ向きの同一平面の重なりが 1 群 (隙間 0.0 mm、6.97 cm²、Prefab のモデル座標 (0.000, 0.105, −0.050))。近接の画像 [FoamCannon-zf1.jpg](../../plans/evidence/2026-10-13-zfight/FoamCannon-zf1.jpg) (後ろから) で、深緑の蓋の中に生成りがまだらに出る。他の候補: WaterPistol は透明の水窓が胴から 1.2 mm 浮いた near 群だけで、画像にちらつきなし。Duck / Fish / Bamboo は同一平面の重なり 0 で、しかも IMP-DOG-047 でシーンから外れている。

**場所はタンクの上ではなく、銃の後ろの蓋。** 形状 (ラボ `scripts/models_v02.py` 32–33 行) で確かめた: 胴 `Cannon ceramic housing` と背 `Cannon forest saddle` は loft の最後の断面がどちらも Blender y=+0.050 (Unity の z −0.050 = 後ろ)。loft() は両端に蓋を付けるので、胴の生成りの楕円の蓋 (z .061–.115、26.0 cm²) と背の深緑の楕円の蓋 (z .094–.122、12.1 cm²) が同じ平面・同じ向き (+Y) で z≈.10 付近に重なる (Blender で測った重なり 7.59 cm²、走査の値は三角形の組ごとの下限 0.1 cm² で切った値)。材質は同じ PremiumWash_Tools (タイル 0 と 1)。タンク (reservoir / collar / neck) は y=−0.080 で関係ない。

## 直し方 (ラボ 0.3.1、2026-10-13)

重なりは 2 つの違う楕円の一部どうしで、重複面ではない。片方を消すと穴が開き (裏面カリングで中が見える)、距離での結合は何もしない。そこで**大きい方の蓋 (胴) の輪 24 頂点を自分の法線の逆 (−Y、前へ) へ 1.0 mm 下げた**。背の蓋が上に残る (Unity では背が後から描かれて同点で勝っていたので、今の見た目 = 深緑が主を保つ)。胴の最後の側面が 1 mm 短くなるだけ。

| 項目 | 前 (0.3.0) | 後 (0.3.1) |
|---|---|---|
| 三角形 | 1,616 | 1,616 (削除 0、動かした頂点 24) |
| 同一平面の重なり | 1 群 7.59 cm² | 0 (直後と FBX の読み戻しの両方) |
| bounds・アンカー (Grip / Nozzle / Rest) | build.json | 同じ (誤差 1e-5 m 以下)。check_fbx_axes.cjs の Unity 軸も 0.3.0 と同値 |
| UV・材質 | — | UV 不一致 0、材質 1 つ |
| 法線 | — | 滑らかな側面の角 216 か所が最大 1.01° 変わる |

- スクリプト: ラボ `assets/wash-premium/scripts/fix-foamcannon-zfight.py` (Blender 5.2.2 をコマンドラインで実行。0.3.0 の `source/generated/PremiumWash.blend` を開き、**保存しない**。root を原点へ戻し、0.3.0 と同じ書き出し設定)。冪等 (毎回 0.3.0 の blend から始める。直った形には何もしない)。想定外の重なり (群が 1 つでない / Y 軸でない / |y|≠.050) なら止まる。`--offset-mm` で距離を変えられる
- 出力: ラボ `exports/0.3.1/FoamCannon.fbx` (sha256 `fbf22958…0de4`、67,356 bytes。書き出し日時を含むので再生成のたびに変わる)。記録: ラボ `validation/fix-foamcannon-zfight.json` (status pass)。exports 直下の 0.3.0 (sha256 `894c1241…a69f`、ワールドの FBX と同じ) は変えていない
- 0.3.0 の生成スクリプト (models_v02.py) は凍結 (build.json に SHA) のため触っていない。次に 18 品を作り直すとき .050→.049 を取り込むかは未決
- 走査の結果の見え方: 直した後も、走査の near 段 (隙間 ≤ 3 mm) に 1.0 mm・約 7 cm² の 1 群が出るのは想定どおり (WaterPistol の 3.00 mm と同じ段。走査の注記どおり 0.1 mm を超える隙間は手の距離で争わない)。合否は coplanar 0 と近接画像で見る

## 取り込み直しの手順 (0.3.0 の 1 品だけ。WashPremium/10 は使わない)

FBX ファイルだけを同じパスへ上書きする。`.meta` (guid `82076e060096dd544a4d63fe9c6ab09d`、`fileIdsGeneration: 2` = 名前から fileID) はそのままなので、オブジェクト名 (FoamCannon / FoamCannon_Mesh / FoamCannon_GripAnchor / _NozzleAnchor / _RestAnchor) が同じ新しい FBX でも Prefab (`FoamCannon.prefab` は FBX のモデル Prefab を入れ子にし、材質を子の Renderer に上書き) とシーンの参照 (`nozzleAnchors[0]` など) は保たれる。取り込み設定 (globalScale 1、法線 Import、接線 Mikk、圧縮 Off、材質 None、Read/Write 無し) も `.meta` に残っているので掛け直さない。10 を回すと Prefab を作り直すので使わない。

1. **書き換える前の確認** (Unity を触らない)
   - `git status --short -- Assets/KominkaHillside/Models/WashPremium/FoamCannon.fbx Assets/KominkaHillside/Models/WashPremium/FoamCannon.fbx.meta Assets/KominkaHillside/Prefabs/WashPremium/FoamCannon.prefab` が空
   - `sha256sum Assets/KominkaHillside/Models/WashPremium/FoamCannon.fbx` が `894c124160011e3c4a2c56f0ab541e39be003383b6029a4a0f1d03a11b18a69f` (ラボの 0.3.0 と同じ。違えば止まって原因を見る)
   - `.meta` の guid が上の値。ラボの `validation/fix-foamcannon-zfight.json` が `status: pass` で、`fbx_sha256` が置く FBX の sha256 と一致
2. **Unity (ユーザー)**: 編集モード、`VRCDefaultWorldScene` が保存済み。`WashPremium/21b - Capture Integration Baseline` を回す (今 `Temp/WashPremium/` が無いので、無いと 21 が "No runtime baseline" で止まる)
3. **FBX だけを上書き**: `cp C:/work/unity/vrc-asset-lab/assets/wash-premium/exports/0.3.1/FoamCannon.fbx Assets/KominkaHillside/Models/WashPremium/FoamCannon.fbx`。`.meta` はコピーも削除もしない。Unity へ戻ると自動で取り込み直す (Reimport All はしない)
4. **置いた直後の確認**
   - `git status` で変わったのが FoamCannon.fbx だけ (`.meta`・`FoamCannon.prefab`・シーンに差分なし)
   - Console に取り込みの警告・Missing が出ない。Prefab `FoamCannon` の子 `FoamCannon_Mesh` の Mesh が Missing でなく 1,616 三角形、材質 PremiumWash_Tools
5. **検査**: `WashPremium/21 - Verify Integration` PASS (FoamCannon tris=1616、合計 19,604 の定数は変わらない)、`WashPremium/44 - Verify Selection` PASS (泡銃の Gun 持ち・ExactGun・NozzleAnchor の向き。アンカーは変わっていないので差 0 のはず)、`Wash/12 - Verify Wash (All Dogs)` PASS。0.3.0 の受領書 `Temp/WashPremium/import.txt` は 10 を回さないので作り直されない。アンカーの角度は 44 と、FBX 段階の check_fbx_axes.cjs (0.3.0 と同値) で見る
6. **見た目の確認**: 先に前の画像を残す (`ZFight/4` は `docs/plans/evidence/2026-10-13-zfight/<品>-zf<順位>.jpg` と `<品>-0/90/180/270.jpg` を同じ名前で上書きする。`KH_ZFightScanEditor.Premium.cs` 191・222 行)。`FoamCannon-zf1.jpg` → `FoamCannon-zf1-before.jpg`、`FoamCannon-180.jpg` → `FoamCannon-180-before.jpg` に複製してから、`ZFight/3 - Scan Premium Tools` で FoamCannon の coplanar が 0 (near 段の 1.0 mm・約 7 cm² の 1 群は想定どおりで、退行ではない)、`ZFight/4 - Capture Premium Tools` で撮り直す (near 群があるので zf1 は同じ場所を撮る)。前後を並べてちらつきが消えたことを見る
7. シーンは保存しなくてよい (変わらない)。もし dirty になったら中身を見てから決める
8. **戻し方**: `git checkout -- Assets/KominkaHillside/Models/WashPremium/FoamCannon.fbx` → Unity へ戻って取り込み直し

## 範囲

1. 特定: ユーザーに品名か画像を聞く。聞けなければ Editor で 5 品を近接のカメラで回して見て、ちらつく面を記録する (画像を `docs/plans/evidence/` に残す)
2. 直し: ラボの元モデル (Blender) で重なる面をずらす・消す・結合する。ラボの検収 (`validation/`、三角形数・bounds) を通す
3. 取り込み直し: 0.4.0 なら `WashPremium/12` → `21` (三角形数 8,414 の検査値は変われば更新)。**0.3.0 なら** `10` を再実行しない決まりなので、その品だけ FBX を差し替える手順 (メッシュの差し替えで Prefab・材質・アンカー・`gunHolds` を保つ) を決めてから行う
4. `WashPremium/44` と `Wash/12` が PASS のまま

やらないこと: 道具の握り ([BUG-DOG-010](BUG-DOG-010-wash-tool-grip.md))、見た目・音の全体の見直し ([IMP-DOG-055](IMP-DOG-055-wash-premium-model-and-sound-quality.md))。

## 完了条件

AI 側で確かめる。

- [x] 品名と重なっていた面 (部位・材質) を記録し、直し方 (ずらした距離 / 消した面) と三角形数の前後を記録した (2026-10-13、上の「特定」「直し方」)
- [ ] 取り込み直しの後、`WashPremium/21`・`44`、`Wash/12` PASS。受領書 (`Temp/WashPremium/import-*.txt`) のアンカーの角度が 1° 以内のまま
- [ ] Editor の近接カメラの画像でちらつきが消えている (変更前後)

ユーザーが確かめる (Play / PCVR)。

- [ ] その品を持って回しても、置いても、ちらつきが出ない。Quest でも出ない
- [ ] 握り・噴出口の位置が変わっていない

## 記録

2026-10-13 (統括 Fable): 差し替え実施。前提 (git 差分なし・FBX sha 894c1241…・meta guid 82076e06…・validation json pass) を確認 → WashPremium/21b (基準) → FBX を同じパスへコピー (sha fbf22958…) → refresh (C# 0) → git 差分は FBX だけ → WashPremium/21 PASS (FoamCannon tris=1616、18 モデル 19,604 tris)、44 PASS、Wash/12 VERIFY_OK 5393 → ZFight/3: FoamCannon coplanar 0 (残るのは 1.0 mm 離れた near 1 群 7 cm²、想定どおり)、ZFight/4 の撮り直し FoamCannon-zf1.jpg でちらつきが消えた (前は FoamCannon-zf1-before.jpg)。再取り込みでシーンが dirty になったので保存 (差分は首輪セットの未コミット分)。

2026-10-13: 起票 (ユーザー依頼「Issue に登録しておいてほしい」)。優先度 Medium は AI の仮置き。実装は未着手。
2026-10-13: 特定と直し (統括 Fable、実装 Opus)。走査と近接画像で FoamCannon、形状で胴と背の後ろの蓋 (Blender y=+.050) と確認。ラボ 0.3.1 で胴の輪を 1.0 mm 下げ、exports/0.3.1/FoamCannon.fbx を書き出した (1,616 tri のまま、重なり 0、アンカー・bounds 不変、validation/fix-foamcannon-zfight.json pass)。ワールドの FBX はまだ置き換えていない (上の手順 3 から、Unity はユーザー)。
