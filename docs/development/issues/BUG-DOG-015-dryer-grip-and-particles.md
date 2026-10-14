---
id: BUG-DOG-015
type: BUG
system: DOG
status: todo
priority: Medium
summary: ドライヤーの持ち手の位置がおかしい (ユーザー報告 2026-10-15)。風のパーティクルは不要 (ユーザー決定 2026-10-15) なので外す
next: 1) ドライヤーの FBX の GripAnchor の位置・軸を実測し、取っ手の中に持つ点を作る (泡銃と同じ Gun の持ち方か、別案かは実測後に決める) 2) UseMist を外し、48 / 44 / NOT_TESTED の風の筋の扱いを消す。しずくの扱いはユーザーに確認
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

## 完了条件

1. **持ち手**: ドライヤーを右手・左手で持ったとき、取っ手が手の中に収まり、送風口が肘→手の延長上を向く (泡銃の要望と同じ向き)。PCVR と Desktop の両方で確認し、端末ごとの結果を本票に記録する。FBX の実測値 (GripAnchor の位置・軸、取っ手の中心) を本票に残す。
2. **パーティクル**: ドライヤーを使っても粒子が出ない (自分・他人の両方)。音は従来どおり。`44 - Verify Selection` が新しい検査で PASS、`Wash/12` の FAIL が作業前から増えない。
3. 4 つの洗い場すべてに同じ設定が入っている (48 で冪等に書き、44 で確かめる)。

## 関連

- [BUG-DOG-010 洗浄道具の持ち方](BUG-DOG-010-wash-tool-grip.md): 泡銃の原因と Gun の持ち方。末尾に「水鉄砲とドライヤーは同じ原因の見込みで未対応」とある。水鉄砲 (ダック・魚・竹) は 9 品構成 (IMP-DOG-047) で提供から外れたので、残りはドライヤーだけ。
- [IMP-DOG-047 Q6 ドライヤーの風](IMP-DOG-047-wash-item-review-changes.md): 風の筋の実装 (本票で撤去)。`next` の「ドライヤーの風を見る」は本票の決定で不要。
- [IMP-DOG-049](IMP-DOG-049-wash-water-tools-and-reactions.md): 品ごとの Gun の持ち方 (`gunHolds[]`) の仕組み。
