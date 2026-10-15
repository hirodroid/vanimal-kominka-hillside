---
id: BUG-DOG-010
type: BUG
system: DOG
status: review
priority: Medium
summary: 洗浄道具を持つ・装備すると形や持ち手が不自然に見える
next: ユーザーの PCVR で泡銃を右手・左手で持ち、ノズルが肘→手の向きか・横倒れしないか・握りの位置を見る (Desktop も)。ずれたら FoamHoldLocalRotation / Position を直して 47 → 44。水鉄砲 3 種とドライヤーは同じ原因で未対応
updated: 2026-10-11
---

# BUG-DOG-010 洗浄道具を持ったときの見た目を修正

## ユーザー報告

2026-10-10 当初：「今持ち手がおかしい」「持った時の装備した時に形が変」。握り位置や向きの問題と、モデル自体の形状・スケールの問題を断定せず切り分ける。

同日[回答JSON](../../../artifacts/wash-item-review/2026-10-10/vanimal-wash-review-2026-10-10T07-47-47-617Z.json)で対象が **泡もこもこ銃（FoamCannon）** と判明。持つと横を向く／横に倒れる。噴出口を **肘から手へ向かう方向の延長上** に向け、リードが伸びるのと同じような方向に合わせてほしい、という要望。具体的な左右の手・端末は未記録。モデル自体の変形が原因かは未確定。

## 調査・修正範囲

- 泡銃の回答を起点に、持った状態の症状と目標方向を確認する。他アイテムのgrip欄が空欄であることを、握りの合格と扱わない。
- 対象ごとに、置いた状態と保持時のモデル形状、親子Transform、左右の握り位置と向き、GripAnchor／ExactGrip、噴出口や接触面の向きを確認する。
- 必要なモデル修正とUnityの保持設定を分けて対応し、生成元や設定の正本にも反映する。
- 単体写真だけで装備時の合格とせず、実際に持つ操作で確認する。

## 完了条件

- 指摘対象を左右の手で持ったとき、持ち手が自然に収まり、意図しない変形・拡縮・手への埋まり・大きなずれがない。
- 噴射・拭き取り等の操作方向が見た目と一致する。
- 泡銃の噴出口が肘→手の方向に向き、不自然な横倒れがなく、ユーザーが求める握りと一致する。
- PCVR、Desktop/Touch、他人からの見え方を確認し、対象アイテムと端末ごとの結果を記録する。

関連：[種類別の道具選択 IMP-DOG-046](IMP-DOG-046-wash-tools-by-category.md)、[追加道具の先行導入 IMP-DOG-043](IMP-DOG-043-wash-premium-selection.md)。今回は起票のみ。

## 2026-10-11 泡銃の原因と修正 (コードのみ、Unity 未実行)

### 原因

- 泡銃の持ち方は Pickup の `orientation=Grip`（[Build.cs:34](../../../Assets/KominkaHillside/Scripts/Editor/KH_WashSelectionEditor.Build.cs)、7 枠すべて）で決まる。ExactGrip には FBX の `FoamCannon_GripAnchor` を**そのまま**入れている（Build.cs:35 `Anchor(go,"GripAnchor")` → [KH_WashSelection.cs:92](../../../Assets/KominkaHillside/Scripts/KH_WashSelection.cs) `ExactGrip=gripAnchors[...]`）。
- この GripAnchor は Blender 側の作り方の約束で、向きは「ローカル +Z = 噴射方向」（asset-lab `scripts/build_premium.py:290` `common()` が方向 (0,-1,0) を +Z に向ける）。位置は既定値 (0,0,0) のままで、取っ手の中ではない（取っ手の前面より 1.2cm 前、`models_v02.py:36,45`）。引き渡し文書には「GripはUnity ExactGripではないため取り込み時に合わせる」とある（asset-lab `handoff.md:33`）が、この合わせ込みは行われていなかった。
- 軸の実測：FBX を node で直接読むと、ルート `Lcl Rotation=(-90,0,0)`、GripAnchor・NozzleAnchor とも `(+90,0,0)`、移動は GripAnchor が 0。Unity では合成すると回転なしになるので、**ExactGrip の座標軸 = 銃モデルの座標軸（+Z = 噴射、+Y = 銃の上、+X = 右）**。2026-10-08 の Verify 出力でも `anchor FoamCannon ... forward=(0.000, 0.000, 1.000)`（artifacts/wash-listings/2026-10-08/evidence/selection-verify.txt:13）。
- VRChat の規則（確かさ別）：
  - **公式文書に記載**（[VRC Pickup](https://creators.vrchat.com/worlds/components/vrc_pickup/)、2026-10-11 取得）：Grip は VR では「Exact Grip の +X が前、+Z が上、そこから前へ 30° 倒す」、非 VR では「+Z が前、+X が下」。Gun は「Exact Gun の +X が狙う方向、+Z が上」。Gun には文書上 VR / 非VR の区別がない（同じ規則だと確認したわけではない）。Pickup の Version 1.0 / 1.1 の違いは Auto Hold だけで、向きの規則はバージョンに関係しない。原文：
    > Gun: When picked up, the pickup rotates so the +X of Exact Gun faces forwards (in the direction the player is aiming), and its +Z faces up.
    > Grip: Similar to Gun, except the orientation changes based on whether the grabbing player is in VR or not: In VR, the pickup rotates so the +X of Exact Grip faces forwards, and its +Z faces up. From there, it is then pitched forwards by 30 degrees. In non-VR, the pickup instead rotates so the +Z of Exact Grip faces forwards, and its +X faces down.
  - 今の軸をこれに当てはめると、VR では噴射方向 (+Z) が上を向いて 30° 前へ倒れ、銃の右側面 (+X) が前を向く（= 横を向く）。Desktop では噴射は前を向くが、銃の右側面が下になる（= 横倒れ）。報告の症状と合う。
  - **ソースで確認できたもの**：ClientSim（Desktop の近似）は Grip = 手の回転 × Euler(0,35,0)、Gun = × Euler(0,305,0) で、2 つのモードは 90° 違う（Packages/com.vrchat.worlds/Integrations/ClientSim/Runtime/Player/ClientSimPlayerHand.cs:36-37, 299-305）。VR 本体の挙動ではない。
  - **未確認**：VR 本体での実際の向き、左右の手の違い（公式文書に記載なし）、Gun で手が来る点が手のどこか。
- 結論：銃身が前を向く道具に Grip の規則を当てていた。Grip の規則は VR と Desktop で違うため、1 つの ExactGrip では両方を合わせられない。ユーザーが求める「肘→手の延長上に噴出口」は、VRChat では Gun の「狙う方向」がそれにあたる。

### 変更

- [KH_WashSelectionEditor.Refine.cs](../../../Assets/KominkaHillside/Scripts/Editor/KH_WashSelectionEditor.Refine.cs) に `FoamCannonHold(sel)` を追加した。泡銃専用の枠 0（`Slots[0]`、他の道具は使わない）だけを `orientation=Gun` にし、`ExactGun` には新しく作る子 `KH_FoamCannonGunHold`（泡銃モデルの直下）を入れる。子の向きは `LookRotation(up, right)` で、+X = 噴射方向（モデルの +Z）、+Z = 銃の上（モデルの +Y）。位置は取っ手の中心 `(0,-0.012,-0.035)`（Blender の取っ手中心 (0,.035,-.012) をモデル座標に直した値）。値は定数 `FoamHoldLocalPosition` と `FoamHoldLocalRotation` で、正本はコード。枠 1〜6 は Grip のまま。U# の値・実行時コードは変えていない（`_Apply` は今まで通り ExactGrip だけを書く。Gun では使われない）。
- 新しいメニュー `Tools/KominkaHillside/WashPremium/47 - Apply Foam Cannon Hold` を追加した。4 つの洗い場に冪等に書き込み、シーンを dirty にするが**保存はしない**。開始時に dirty なら止まる。結果は `Temp/WashSelection/foam-hold.txt` に書く。`42 - Build Selection` からも同じ関数を呼ぶ（Build.cs:37）。
- [Verify.cs](../../../Assets/KominkaHillside/Scripts/Editor/KH_WashSelectionEditor.Verify.cs) `44 - Verify Selection` に検査を追加した。各洗い場で `orientation==Gun`、`ExactGun` が `KH_FoamCannonGunHold`（モデル直下）、+X と噴射方向のずれ・+Z と銃の上のずれ（実測値を表示、各 0.5° 未満）、位置が定数どおり（誤差 0.5mm 未満）、噴出口が手より 0.15m 以上前、枠 1〜6 が Grip のまま、17 品の `_Apply` を一巡したあとも Gun の設定が残ること。ログに `hold FoamCannon <洗い場> sprayInHold=(1,0,0) modelUpInHold=(0,0,1) ...` が出る。NOT_TESTED 行も更新した。

### 残り

- 統括：`bash scripts/unity-compile.sh`（Editor の C# だけの変更で、Udon の変更はない）→ 47 を実行 → 44 を実行 → `Temp/WashSelection/verify.txt` の `hold FoamCannon` 4 行と PASS を確認 → シーンを保存。書き込み先は**シーンだけ**（4 洗い場の Slot0 の VRCPickup と、新しい子 4 個）。プレハブ資産は変わらない。シーンには別セッションの未コミット変更があるので、stage は分ける（vrc-scene-edit-safety）。
- ユーザー：PCVR で右手と左手、それぞれ泡銃を持つ。噴出口が肘→手の延長上を向くか、横倒れがないか、取っ手が手に収まるかを見る。Desktop も確認する。向きがずれていたら `FoamHoldLocalRotation` を、手の位置がずれていたら `FoamHoldLocalPosition` を直し、47→44 をやり直す。
- 対象外（同じ原因の可能性あり）：アヒル・魚・竹の水鉄砲とドライヤーも FBX の GripAnchor（+Z = 噴射）を Grip で使っている。水鉄砲の枠 2 はシャワー・じょうろと共用なので、道具ごとに orientation を切り替える必要がある。ユーザー確認のあとで別途扱う。

2026-10-11 (統括): シーンへ適用した。コンパイル 0 → `WashPremium/47` (4 洗い場で Slot0 を Gun、`KH_FoamCannonGunHold` を新規 4) → `44 - Verify Selection` 431 PASS / FAIL 0 (4 か所とも `aimErr=0.00deg upErr=0.00deg`、ノズルは手の 0.182 m 先) → 保存。保存後の差分は Slot0 の VRCPickup 4、持つ点 4、泡銃のプレハブインスタンス 4、中央支柱の位置の 2µm の再計算 1 だけ。Codex 独立レビュー (`c0ca3e5`) は軸の対応を問題なしとした。残り: ユーザーの PCVR (右手・左手でノズルが肘→手の向きか、握りの位置)、Desktop。水鉄砲 (アヒル・魚・竹) とドライヤーは同じ原因の見込みで未対応 (水の枠は道具ごとに持ち方を切り替える必要がある)。

2026-10-15: ドライヤーの持ち手 (位置) と風のパーティクルの撤去は [BUG-DOG-015](BUG-DOG-015-dryer-grip-and-particles.md) で扱う (ユーザー報告・決定 2026-10-15)。水鉄砲 3 種は 9 品構成で提供から外れた。

2026-10-15: ドライヤーも泡銃と同じ Gun の持ち方にした (コードの子 `KH_DryerGunHold`、取っ手の中心 (0,-0.013,-0.025)、枠 4 は休止時も Gun。同日に 4 洗い場へ適用済み (48 → 44 1148 PASS)、PCVR / Desktop の確認待ち。記録は [BUG-DOG-015](BUG-DOG-015-dryer-grip-and-particles.md))。
