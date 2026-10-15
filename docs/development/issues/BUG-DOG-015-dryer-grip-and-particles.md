---
id: BUG-DOG-015
type: BUG
system: DOG
status: review
priority: Medium
summary: ドライヤーの持ち手の位置がおかしい (ユーザー報告 2026-10-15)。風のパーティクルは不要 (ユーザー決定 2026-10-15) なので外す
next: 適用済み (48 → 44 1148 PASS → 保存 → Wash/12 VERIFY_OK)。残りはユーザー: PCVR 右手・左手 + Desktop で取っ手が手に収まり送風口が肘→手の向きか、粒子が出ない・Air の音は従来どおり、しずくも不要かの回答。別セッション: パレードの plazaRenderers の bind 再実行
updated: 2026-10-15
---

# BUG-DOG-015 ドライヤーの持ち手の位置と、風のパーティクルの撤去

## ユーザー報告 (2026-10-15)

> ドライヤーが持ち手の位置がおかしい。パーティクル不要。

- 症状は「持ち手の**位置**」。向きの問題と断定しない (泡銃 [BUG-DOG-010](BUG-DOG-010-wash-tool-grip.md) は向きだったが、ドライヤーは持つ点が取っ手の外にある疑いが強い。下の「分かっていること」)。
- 左右の手・端末 (PCVR / Desktop) は未記録。
- **風のパーティクルは不要 (ユーザー決定 2026-10-15)。** [IMP-DOG-047](IMP-DOG-047-wash-item-review-changes.md) Q6 で入れた風の筋 (Stretch 粒子) をやめる。音 (Air ループ) については指示がないので残す。

## 分かっていること (2026-10-15 起票時の調査。Unity 未実行)

### 持ち手

- ドライヤーは品 7、枠 4 (`KH_WashSelectionEditor.cs:24` `Slots[7]=4`。仕上げの種類は枠 3 = タオル、枠 4 = ドライヤーで、ドライヤーだけが枠 4 を使う)。
- 持ち方は `gunHolds[7]` が null なので **Grip + ExactGrip = FBX の `GripAnchor`** ([KH_WashSelection.cs:325](../../../Assets/KominkaHillside/Scripts/KH_WashSelection.cs) `ConfigureSlot` の else 側)。泡銃が直す前に通っていた経路と同じ。
- 生成元 (asset-lab `assets/wash-premium/scripts/`) の値。**生成スクリプトの値で、FBX 自体はまだ実測していない** (泡銃は BUG-DOG-010 で node から FBX を読んで軸を確かめた。ドライヤーはその手順がまだ):
  - `models_v02.py:129-138`: 筐体は Blender の -Y 向きの円筒 (`lathe(... loc=(0,0,.082), rot=(pi/2,0,0))`)、吹き出し口 `(0,-.117,.082)`、ノズルアンカー `noz=(0,-.122,.082)`、取っ手 `grip('Dryer ivory handle',(0,.025,-.013),0)`。
  - `end('Dryer',common(noz=...))` で GripAnchor は `common()` の既定 `grip=(0,0,0)`、向き `(0,-1,0)` を +Z に向ける (`build_premium.py:289-290`)。つまり **GripAnchor は原点で、取っ手 (Blender (0,.025,-.013) 付近) の中ではなく、+Z = 送風方向**。
- 疑われる原因は 2 つで、両方あり得る:
  1. **位置**: 持つ点が原点 (筐体の付け根あたり) で取っ手の中にない → 報告の「位置がおかしい」に合う。
  2. **向き**: +Z = 送風の軸を Grip の規則 (VR: ExactGrip の +X が前・+Z が上・30° 前へ倒す / Desktop: +Z が前・+X が下) で使っている → 泡銃と同じく、VR で送風が上を向き、Desktop で横倒れになる可能性。
- 直し方の候補 (**どれにするかは実測後に決める**。BUG-DOG-010 の Gun の持ち方は PCVR で未確認 = `review` のままなので、同じ型を当てるのは前提ではない):
  - A. 泡銃と同じ: コードで子 (例 `KH_DryerGunHold`) をモデル直下に作り、+X = 送風 (モデル +Z)、+Z = 上、位置 = 取っ手の中心 (Blender (0,.025,-.013) → モデル座標 (-x, z, -y) = (0,-.013,-.025) 付近。要実測) にして、`GunItems` に 7 を足す。`GunHolds()` は FBX に `<Name>_GunHold` が無い品で throw するので、泡銃のように専用の分岐が要る ([Refine.cs:35-50](../../../Assets/KominkaHillside/Scripts/Editor/KH_WashSelectionEditor.Refine.cs))。
  - B. asset-lab で `Dryer_GunHold` を FBX に入れて再書き出し (0.4.0 の水鉄砲・ホースと同じ経路。`handoff.md` と `verify_v05.py` の更新が要る)。
  - C. Grip のまま ExactGrip だけ取っ手の中へ動かす (向きの問題が無いと実測で分かった場合)。
- 泡銃の PCVR 確認 (BUG-DOG-010 の next) が先に済めば、A の確からしさが上がる。

### パーティクル

撤去で触る所 (実行時の U# は `spray == null` を既に許す: [KH_WashTool.cs:433](../../../Assets/KominkaHillside/Scripts/KH_WashTool.cs)、`ConfigureSlot` は `itemSprays[item]` をそのまま渡す。U# の変更は要らない見込み):

- [Build.cs:36](../../../Assets/KominkaHillside/Scripts/Editor/KH_WashSelectionEditor.Build.cs): `spray` の条件 `i>=1&&i<=7` が 7 を含み、`UseMist` を作っている → 7 を外す (42 は再実行しないので、シーンの既存 `UseMist` は 48 側で消す)。
- [Category.cs:104](../../../Assets/KominkaHillside/Scripts/Editor/KH_WashSelectionEditor.Category.cs): `sprays[7]==null` で throw し、`DryerStreaks(sprays[7])` を強制 → 逆に「無いこと」にする (シーンの `Dryer/.../UseMist` を削除し `itemSprays[7]=null`)。`Dryer*` 定数 (Category.cs:50) と `DryerStreaks` も消す。
- [Category.cs:233-235](../../../Assets/KominkaHillside/Scripts/Editor/KH_WashSelectionEditor.Category.cs): 44 の `dryer wind streaks` 検査 → `itemSprays[7]==null` かつドライヤーの下に ParticleSystem が無い、に置き換える。
- [Verify.cs:24](../../../Assets/KominkaHillside/Scripts/Editor/KH_WashSelectionEditor.Verify.cs) NOT_TESTED の `dryer streak look (Play)` を消す。
- ハーネス `Testing/KH_CourseWalkHarnessEditor.WashPremium.cs` はドライヤーを取る動作 (`PremiumTake(PDryer)`) だけで粒子は見ていない (要再確認)。
- 音 (`itemSounds[7]` = Air) とノズル (`nozzleAnchors[7]`、送風の当たり判定の起点) は残す。
- IMP-DOG-047 の「しずく: TODO」(濡れた所から飛ぶしずくを `KH_DogWash.ContactEffects` に足す案) は、この決定で不要になった可能性が高いが**未確認**。ユーザーに「しずくも不要か」を聞いてから扱いを決める。

## 対応 (2026-10-15)

### FBX の実測 (統括、asset-lab の `check_fbx_axes.cjs`、Unity の軸)

- `Dryer_GripAnchor`: モデルの原点、回転なし。つまり +Z = 送風、+Y = 上、+X = 右 (泡銃の `FoamCannon_GripAnchor` と同じ)。
- `Dryer_NozzleAnchor`: (0, 0.082, 0.122)、+Z = 送風。
- 取っ手 (`models_v02.py` の `grip('Dryer ivory handle',(0,.025,-.013),0)` のロフト): モデル座標で x -0.02〜+0.02、y -0.07〜+0.045、z -0.049〜-0.002 (筐体の下に垂れ、少し後ろ)。**中心 (0, -0.013, -0.025)**。

### A (泡銃と同じ Gun の持ち方) にした理由

- 原因は泡銃と同じ ([BUG-DOG-010](BUG-DOG-010-wash-tool-grip.md) 「2026-10-11 泡銃の原因と修正」)。+Z = 送風の GripAnchor を Grip の規則 (VR: Exact Grip の +X が前・+Z が上・そこから 30° 前へ倒す / Desktop: +Z が前・+X が下) で使っていたので、VR では送風が上を向き、Desktop では横倒れになる。
- 持つ点も原点 = 取っ手の前面 (z -0.002 付近) で、取っ手の中心から 2.3 cm 前・1.3 cm 上。報告の「位置」に合う。**位置と向きの両方**がずれていた。
- ドライヤーは枠 4 を 1 品だけで使う (`Slots[7]=4`) ので、泡銃の枠 0 と同じく枠ごと Gun にできる (水の枠のような品ごとの切り替えは要らない)。FBX に `Dryer_GunHold` は無いので、B (asset-lab で再書き出し) より A が早い。C (Grip のまま位置だけ) は向きが直らない。

### 定数 (正本はコード: [Refine.cs](../../../Assets/KominkaHillside/Scripts/Editor/KH_WashSelectionEditor.Refine.cs))

- `DryerId=7`、`DryerHoldName="KH_DryerGunHold"`、`DryerHoldLocalPosition=(0,-0.013,-0.025)` (取っ手の中心)、`DryerHoldLocalRotation=Quaternion.LookRotation(Vector3.up,Vector3.right)` (+X = 送風 = モデル +Z、+Z = 上 = モデル +Y。泡銃と同じ枠組み)。
- この値で持つ点から見たノズルは (0.147, 0, 0.095) の見込み (手の 0.147 m 先、0.095 m 上)。
- `DryerId` は本来 `KH_WashSelectionEditor.cs:25` の `WateringCanId` などの隣に置くが、今回の編集範囲の外なので Refine.cs の定数の所に置いた。

### 変更 (ファイルごと)

- **Refine.cs**: `FoamCannonHold` の中身を `CodeGunHold(sel,id,holdName,localPosition,localRotation)` に一般化した (モデル直下に子を作るか更新し、その品の枠の Pickup を `orientation=Gun`・`ExactGun=子`、`gunHolds[id]=子`)。`FoamCannonHold(sel)` はその薄い呼び出しとして残し、`DryerHold(sel)` を足した。`GunItems` に 7、`GunHoldName(7)` は `KH_DryerGunHold`。`GunHolds()` は泡銃とドライヤーを子で作り (`CodeGunItem`)、FBX の `*_GunHold` を探すのは水鉄砲・ホースだけ。休止時も Gun のままの枠を `DedicatedGunSlot(s)` (枠 0 / 4) にまとめた。47 のログにドライヤーの行を足した。
- **Category.cs (48)**: 手順 4 の「休止時は Grip に戻す」から枠 4 も外した (`DedicatedGunSlot`)。保存されるシーンでも枠 4 は Gun + `KH_DryerGunHold`。`GunHolds(sel,log);` の次の行が `ResetApplyState(sel);sel._Apply(false);` であることを確認した。48 は選択を -1 にしてから `_Apply` するので、`ConfigureSlot(4,-1)` は持ち方を書き換えずに return する ([KH_WashSelection.cs:329](../../../Assets/KominkaHillside/Scripts/KH_WashSelection.cs))。
- **Verify.cs (44)**: `VerifyFoamCannonHold` を `VerifyGunHolds` に替えた (他の枠の検査を 1 回 + `VerifyCodeGunHold` を品 0 と 7)。ログ `hold Dryer <洗い場> orientation=Gun sprayInHold=... modelUpInHold=...` が出る。ノズルが手より前にある下限は泡銃 0.15 m (従来どおり)、ドライヤー 0.1 m。`RestHolds` (44 / 45 / MinLift の後始末) も枠 4 を Gun のまま残すようにした (直さないと 44 の後始末で枠 4 が Grip に戻り、そのまま保存すると 48 の結果が消える)。NOT_TESTED は Gun の品を「FoamCannon / WaterPistol / Hose / Dryer」にし、Grip の品を (PawMitt / WateringCan / PatternTowel) と明記、`dryer streak look (Play)` を消した。
- **Category.cs (44 の論理検査)**: 「hold per item」にドライヤーの Gun を、「Gun hold survives the category cycle」にドライヤーを足した。

### パーティクルの撤去

- **Build.cs (42)**: `spray` の条件を `i==0||(i>=1&&i<=6)||i==12` にし、7 用の色・数・速さ・大きさの分岐を消した。音 (Air) は残る。
- **Category.cs (48)**: `Dryer*` 定数と `DryerStreaks` を削除。`sprays[7]` があればログに出して GameObject ごと削除し `sprays[7]=null`、さらにドライヤーのモデルの下に残る ParticleSystem をすべて削除する (1 個ずつログ)。2 回目は何もしない (冪等)。`UseMist` は 42 がプレハブインスタンスに足した子なので削除できる。NetworkIDs の前後の掃除は既存の `CleanNetworkIds` のまま。49 も同じ本体 (`ConfigureStation`) を通るので同じく消す。
- **44 の新しい検査** (各洗い場): `dryer itemSprays[7] null`、`no ParticleSystem under the Dryer model`、`dryer slot4 spray null after the apply`、`saved backing itemSprays[7] (Dryer) null`。旧 `dryer wind streaks` は削除。

### 実行時コードの確認 (読んだだけ。U# の変更なし)

- [KH_WashTool.cs](../../../Assets/KominkaHillside/Scripts/KH_WashTool.cs) の `spray` の使用はすべて null を許す: 394 (`_OnShot`、`spray != null`)、433 (`ApplySpray`、`spray != null`)、940-945 (`SetFlow`、`spray == null` で return。じょうろの傾きだけ)。
- Air の音は `ApplySpray` の 434 で `sprayAudio` を `spray` と別に鳴らす / 止める。持つ人は `BusyFrame` → `SprayChecks` (666) → `ApplySpray(Want())`、他人は `_OnStateChanged` (364) と待機中の `SprayChecks`。どれも `spray` を見ない。枠 4 は `sprayInput=true` (Build.cs:35 の `k==4`) なので `_IsSprayer()` (61) が真で、この経路に入る。送風の当たり判定 `SprayRays` はノズルだけを使う。
- [KH_WashSelection.cs](../../../Assets/KominkaHillside/Scripts/KH_WashSelection.cs): `ConfigureSlot` の 330 は `t.spray=itemSprays[item]` を代入するだけ (null 可)、329 は `t.spray!=null` を確かめてから止める。264 の配列検査は長さだけで、要素の null は問わない。

### ハーネス

- `Testing/KH_CourseWalkHarnessEditor.WashPremium.cs` はドライヤーの粒子を読まない (`PremiumTake(PDryer)` と、`P-dryer-and-cosmetic-isolation` の dryer channel / `tool` の値だけ)。`spray` を読むのは `WashPremiumWater.cs` の水鉄砲・ホースの枠だけ。持ち方 (orientation) を見るケースは無い。
- 回していない (実装担当は Unity を触らない。回すかは統括が相談で決める)。

### 統括への注意

- 44 の件数は各洗い場で +12 の見込み (全部 PASS なら 4 か所で +48): 持ち方 +5 (他の枠の検査が 6 → 5、品ごとの検査 6 × 2)、粒子 +3 (1 → 4)、`Water.cs` の `VerifyAddonItems` +4。`VerifyAddonItems` は `GunItems` の 0 以外を回すので、ドライヤーの持つ点も測る (`Dryer gunHolds = KH_DryerGunHold under the model`、`Dryer Exact Gun +X = spray direction`、`Dryer Exact Gun +Z = model up, no roll`、`Dryer nozzle ahead of the hand along the aim` (> 0.12 m) と `hold Dryer ...` のログ行がもう 1 本)。Water.cs は今回の編集範囲の外なので重複のまま。
- 泡銃の検査文字列が変わった: `Exact Gun +X = spray direction` → `FoamCannon KH_FoamCannonGunHold +X = spray direction`、`Exact Gun +Z = gun top, no roll` → `FoamCannon KH_FoamCannonGunHold +Z = model top, no roll`、`hold point at handle centre` / `nozzle ahead of the hand` に品名の前置き、ExactGun の配線の検査は `= gunHolds[0]` も確かめる。
- 47 (Apply Gun Holds) も同じ `GunHolds` を通るので、48 の代わりに持ち方だけを書くときにも使える (粒子は消さない)。

### 適用 (2026-10-15、統括)

- 作業前の件数: `44 - Verify Selection` PASS (FAIL 0)、`Wash/12` VERIFY_OK (5393、FAIL 0)。シーンは別セッション (ハロウィンパレード) の未コミット分を含む保存済みの状態。
- `bash scripts/unity-compile.sh` C# 0 件 (U# の変更なし) → **WashPremium/48** (4 洗い場で `.../Premium_<洗い場>_Slot4/Dryer/Dryer/Dryer_NozzleAnchor/UseMist` を削除、`KH_DryerGunHold` を 4 つ作成、NetworkIDs 391 → 391) → **44 PASS 1148 / FAIL 0** (4 洗い場とも `hold Dryer ... sprayInHold=(1,0,0) modelUpInHold=(0,0,1) aimErr=0.00deg upErr=0.00deg local=(0.000, -0.013, -0.025) nozzleInHold=(0.147, 0.000, 0.095)`、`dryer itemSprays[7] null`・`no ParticleSystem under the Dryer model`・`saved backing itemSprays[7] (Dryer) null` PASS) → 保存 → **Wash/12 VERIFY_OK 5393 / FAIL 0** (作業前から増えない)。保存後の `.unity` に `gameObject: {fileID: 0}` 0 件。
- ハーネスは回していない (粒子は実行時コードを変えておらず、持ち方は Edit の 44 で軸と位置が決まる。手に持った見え方は PCVR / Desktop でしか分からない)。
- シーンの差分 (stage した分): 4 洗い場の KH_WashSelection / KH_WashTool / VRCPickup の値 (32 文書)、`KH_DryerGunHold` 4 個 (8 文書)、UseMist 4 個 (16 文書) の削除。別セッションの未コミット分は baseline patch の逆適用と文書単位の 3-way マージで除いた。
- **別セッションへの影響 (要対応、ユーザーに報告済み)**: ハロウィンパレードの未コミットの `KH_HalloweenParadePlayback.plazaRenderers` (広場の洗い場の Renderer 一覧) が、削除した UseMist の ParticleSystemRenderer (fileID 1732229381) を 1 件持っていて、Unity の保存で `{fileID: 0}` になった。その Editor 検査 (`KH_ParadePlazaEditor`、`All(r=>r!=null)`) は bind を再実行するまで通らない。パレード側で bind を再実行して一覧を作り直すこと (この票の変更は戻さない)。
- 残り: ユーザーの PCVR (右手・左手) と Desktop で、取っ手が手に収まり送風口が肘→手の延長上を向くか。風の粒子が出ないこと、音 (Air) が従来どおり。**しずく (濡れた所から飛ぶ粒) も不要かの回答** (IMP-DOG-047 の TODO を閉じるかどうか)。

### Codex 独立レビュー (2026-10-15、gpt-6-astra。高 0 / 中 0 / 低 2、全部反映)

| 指摘 | 対応 |
|---|---|
| 低: BUG-DOG-010 の追記「コードのみ、適用は統括手順」が適用済みの記録と食い違う | 「4 洗い場へ適用済み、PCVR / Desktop 待ち」に書き換え |
| 低: IMP-DOG-047 の `next` に「ドライヤーの風を見る」が残り、Q6 の旧定数の説明が現行に読める | `next` を更新し、旧実装の行に「撤去前の記録」と明記 |

観点 1〜4 (軸と位置、48/44/42/47/49 の整合、粒子撤去の抜け、既存コード) は問題なし。Water.cs の VerifyAddonItems もドライヤーを重複して検査する (実効の下限 0.12 m)。

## 完了条件

1. **持ち手**: ドライヤーを右手・左手で持ったとき、取っ手が手の中に収まり、送風口が肘→手の延長上を向く (泡銃の要望と同じ向き)。PCVR と Desktop の両方で確認し、端末ごとの結果を本票に記録する。FBX の実測値 (GripAnchor の位置・軸、取っ手の中心) を本票に残す。
2. **パーティクル**: ドライヤーを使っても粒子が出ない (自分・他人の両方)。音は従来どおり。`44 - Verify Selection` が新しい検査で PASS、`Wash/12` の FAIL が作業前から増えない。
3. 4 つの洗い場すべてに同じ設定が入っている (48 で冪等に書き、44 で確かめる)。

## 関連

- [BUG-DOG-010 洗浄道具の持ち方](BUG-DOG-010-wash-tool-grip.md): 泡銃の原因と Gun の持ち方。末尾に「水鉄砲とドライヤーは同じ原因の見込みで未対応」とある。水鉄砲 (ダック・魚・竹) は 9 品構成 (IMP-DOG-047) で提供から外れたので、残りはドライヤーだけ。
- [IMP-DOG-047 Q6 ドライヤーの風](IMP-DOG-047-wash-item-review-changes.md): 風の筋の実装 (本票で撤去)。`next` の「ドライヤーの風を見る」は本票の決定で不要。
- [IMP-DOG-049](IMP-DOG-049-wash-water-tools-and-reactions.md): 品ごとの Gun の持ち方 (`gunHolds[]`) の仕組み。
