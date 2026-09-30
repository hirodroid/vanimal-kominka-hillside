# 代表 1 匹の一周 — 実装の約束 (IMP-DOG-025)

> 対象: [IMP-DOG-025](../development/issues/IMP-DOG-025-wash-prototype-one-dog.md)。方式は [設計書](2026-09-30-dog-wash-design.md)

2026-09-30。代表 1 匹 (柴) の一周を、複数の担当が並行で書くための受け渡しの約束。名前と単位をここで固定し、各担当はこの文書に無い名前を相手のファイルへ要求しない。値は試作値で、Play の評価で変える。

## 0. 済んだこと

| 項目 | 結果 |
|---|---|
| 賭け 1 (UV の重なり) | **通過。** 6 匹とも有害な重なり 0 テクセル、UV は全頂点が 0〜1 の中、ミラーなし、胴体のサブメッシュは 1 つ (`Tools/KominkaHillside/Wash/1`、2026-09-30) |
| 位置マップ | 6 匹分を焼き、検査 PASS (`Wash/2`・`Wash/3`)。`Assets/KominkaHillside/Data/Wash/PositionMaps/KH_WashPos_{犬}.asset` (256²、RGBAHalf、RGB = バインドポーズのメッシュ座標の生の値、A = 骨の番号 + 1、無効は 0) と `.json` (骨ごとの「ボーンローカル → バインドポーズのメッシュ座標」の行列) |
| 実寸の倍率 | メッシュ座標 1 単位の実寸は犬で違う (`meshToWorldScale`: 柴 1.615、コーギー 1.310、サモエド 1.300、ラブ 1.152、ポメとダックス 1.000) |

## 1. 担当とファイル

| 担当 | 書くファイル (これ以外を編集しない) |
|---|---|
| A シェーダー | `Assets/KominkaHillside/Shaders/KH_WashMask.shader`、`Assets/KominkaHillside/Shaders/KH_WashShell.shader` |
| B 実行時 | `Assets/KominkaHillside/Scripts/KH_DogWash.cs`、`Assets/KominkaHillside/Scripts/KH_WashTool.cs` |
| C 犬の行動 | `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.HillsideWash.cs` (新規) と、`KD_DogWanderController.cs` の Update への最小の挿入 |
| D 仮の音 | `Assets/KominkaHillside/Audio/Wash/` 以下と、制作票 `docs/plans/2026-09-30-wash-se-production.md` |
| E ビルダー (A〜D の後) | `Assets/KominkaHillside/Scripts/Editor/KH_DogWashSetupEditor.cs` |

Unity の操作 (refresh・コンパイル・メニュー・Play・保存) と git は統括だけが行う。

## 2. 空間と単位

- **ブラシの座標はバインドポーズのメッシュ座標** (位置マップの RGB と同じ空間、生の値)。実寸 (m) の半径は `meshToWorldScale` で割ってメッシュ座標へ直してからシェーダーへ渡す
- 当たった点の変換: `boneLocal = bone.worldToLocalMatrix × worldPoint`、`meshPoint = boneToMesh[bone] × boneLocal`。`boneToMesh` は `.json` の行列
- 部位 Collider は骨の子。`localScale` を `1 / bone.lossyScale` にして半径などをメートルで書く (`vrc-character-rig` §13)。PhysBone のチェーンの中の骨へは足さない (足す場合は `ignoreTransforms` へ登録。§10)

## 3. 部位

柴の試作は 9 部位。骨の名前は 6 犬種で共通 (`Bone`・`Bone.001` …)。

| ID | 部位 | 骨 (Collider を付ける骨) | 隣の部位 |
|---|---|---|---|
| 0 | 胴 (腰) | Bone.001 | 1、5、6、8 |
| 1 | 胴 (胸) | Bone.002 | 0、2、3、4 |
| 2 | 首 | Bone.003 | 1、7 |
| 3 | 前足 左 | Bone.011_L〜Bone.014_L | 1 |
| 4 | 前足 右 | Bone.011_R〜Bone.014_R | 1 |
| 5 | 後ろ足 左 | Bone.005_L〜Bone.016_L | 0 |
| 6 | 後ろ足 右 | Bone.005_R〜Bone.016_R | 0 |
| 7 | 頭 | Bone.004 と、その下の耳・頬・あご・鼻 | 2 |
| 8 | 尻尾 | Bone.018〜Bone.021 | 0 |

骨の番号 → 部位 ID の表 (`boneToPart[]`) は E が `.json` の骨の名前と親子から作る。表に無い骨は、親をたどって最初に見つかった部位。左右の足は互いに隣にしない (ブラシが反対の足へ貫通しないように)。

## 4. マスクのシェーダー `KominkaHillside/Wash Mask` (担当 A)

`VRCGraphics.Blit(prev, next, material, pass)` で使う。`ZTest Always`、`ZWrite Off`、`Cull Off`、`Blend Off`。出力は RGBA = (汚れ, 泡, 濡れ, 1)。

| プロパティ | 型 | 内容 |
|---|---|---|
| `_MainTex` | 2D | 前のマスク (Blit のソース) |
| `_PosMap` | 2D | 位置マップ。**A は点で読む** (骨の番号が混ざらないように。`Load` か、テクセルの中心へ丸めた UV) |
| `_BrushA` / `_BrushB` | Vector | カプセルの両端 (xyz = メッシュ座標)。1 点の時は同じ値 |
| `_BrushRadius` | Float | 半径 (メッシュ座標) |
| `_BrushSoft` | Float | 縁のぼかしの幅 (半径に対する割合 0〜1) |
| `_Delta` | Vector | (汚れ, 泡, 濡れ) へ足す量。負は減らす。接触の時間を掛けた後の値 |
| `_RinseFoamBoost` | Float | 汚れを減らす時、その場の泡の量 × この値だけ速く落とす |
| `_FoamRinseGate` | Float | 1 の時、泡が残っている所の汚れは、泡が落ちた分だけ落ちる (帯の境目を泡と汚れで揃える) |
| `_AllowPart` | Float の配列 (16) | 部位 ID ごとの許可 (0 / 1) |
| `_BoneToPart` | Float の配列 (64) | 骨の番号 → 部位 ID |
| `_EdgeNoise` | Float | 縁の形をノイズで崩す強さ |
| `_CellTex` | 2D | パス 1 用。16×16、点で読まず線形で読む |
| `_Decay` | Vector | パス 2 用。(汚れ, 泡, 濡れ) から引く量 |

| パス | 内容 |
|---|---|
| 0 Brush | ブラシの範囲へ `_Delta` を重み付きで足し、0〜1 へ収める。位置マップが無効なテクセルは前の値のまま |
| 1 Restore | `_CellTex` から全面を書く (途中参加・復旧)。汚れには模様のノイズを掛ける |
| 2 Decay | 全面から `_Decay` を引く (乾く) |

## 5. 殻のシェーダー `KominkaHillside/Wash Shell` (担当 A)

2 つ目の SkinnedMeshRenderer に付ける。geometry シェーダーを使わない。1 パス、`Queue = Transparent+10` から始める。`ZTest LEqual`、`ZWrite Off`、`Blend SrcAlpha OneMinusSrcAlpha`。影は落とさない・受けない。シングルパスステレオのインスタンシングに対応する。

| プロパティ | 内容 |
|---|---|
| `_Mask` | 犬のマスク (RenderTexture) |
| `_PosMap` | 位置マップ (A から部位を引いて、顔の上限に使う) |
| `_BoneToPart` / `_PartFoamLimit` (配列 16) | 部位ごとの泡の上限 (頭は 0.35 から) |
| `_BaseOffset` | 毛の表面までの押し出し (メッシュ座標ではなく実寸 m。シェーダーの中でワールド空間で足す) |
| `_FoamThickness` | 泡の量 1 の時の追加の押し出し (m) |
| `_DirtColor` / `_FoamColor` / `_WetDarken` | 色 |
| `_DirtPattern` | 汚れの模様 (無ければ手続きのノイズ) |
| `_FoamScale` / `_FoamSoft` | 泡の粒のノイズの細かさと縁 |

見た目の規則: 泡が一番上、次に汚れ、濡れは暗さとつや。どれも 0 に近い画素は `clip`。泡は縁をノイズで粒にし、量が増えると粒が繋がって厚く見える。頂点の段でマスクを読んで押し出す (`tex2Dlod`)。

## 6. 実行時 (担当 B)

### `KH_DogWash` (犬ごと、`BehaviourSyncMode.Manual`)

Editor が配線する値 (public か `[SerializeField]`):

| 名前 | 内容 |
|---|---|
| `dogIndex` | 0〜5 (`KD_DogDefs.All` の順) |
| `dogRoot` | 犬のルート (Owner を揃える相手) |
| `shellRenderer` | 殻の SkinnedMeshRenderer |
| `maskA` / `maskB` | RenderTexture のアセット |
| `maskMaterial` | マスクのシェーダーのマテリアル |
| `posMap` | 位置マップ |
| `cellTex` | 16×16 の Texture2D (実行時に書く。Editor が読み書きできるアセットで用意) |
| `meshToWorldScale` | 実寸の倍率 |
| `partBones[]` / `partOfCollider[]` / `partColliders[]` | 部位 Collider と、その骨・部位 ID |
| `boneToMesh[]` | 部位 Collider ごとの行列 (Matrix4x4) |
| `boneToPart[]` (float 64) / `partNeighbors[]` | §3 の表 |
| `cellValid[]` (bool 256) / `cellCenter[]` (Vector3 256) / `cellPart[]` (int 256) | セルの表 (E が位置マップから作る) |
| `hitSplash` / `foamPuff` | 飛沫と泡の粒の ParticleSystem (犬ごとに 1 つずつ、`Emit` で使う) |
| `contactFur` / `contactFoam` / `scrub` | 接触の音の AudioSource (ループ、3D) |
| `initialDirt` | 試作用の初期の汚れ (0〜1)。足と腹に多く |

外から呼ぶ入口 (ローカル専用は `_` 始まり):

| 名前 | 内容 |
|---|---|
| `_LocalStroke(int tool, int colliderIndex, Vector3 worldFrom, Vector3 worldTo, float seconds)` | 道具が毎フレーム呼ぶ。自分のマスクへ描き、8 Hz へ間引いて送る |
| `NetStroke(int tool, int part, int from, int to, int amount, int seq, int epoch)` | `[NetworkCallable(maxEventsPerSecond: 8)]`。引数を検証し、送り主が自分でなければ描く。Owner はセルへ積算する |
| `_WashHold()` | 洗われ中か (サーバー時刻 < `washingUntil`)。犬の行動が読む |
| `_WasherPosition()` | 最後に操作した人の位置 (犬が向く先)。いなければ犬の位置 |
| `_LastStrokePart()` | 最後のストロークの部位 ID (自分のも受け取ったものも)。無ければ -1。犬の反応のきっかけに使う |
| `_CleanSerial()` | 満足の合図のエッジ |
| `_DebugSetDirt(float)` / `_DebugClear()` | 試作・ハーネス用 |

道具の ID: 0 = スポンジ (泡)、1 = シャワー (水)、2 = タオル。

### `KH_WashTool` (道具ごと。`VRC_Pickup` と同じ GameObject、同期なし。位置は `VRCObjectSync`)

| 名前 | 内容 |
|---|---|
| `tool` | 道具の ID |
| `nozzle` | 先端 (Raycast の起点と向き、スポンジの接触点) |
| `washes[]` | 全犬の `KH_DogWash` |
| `washLayer` | 部位 Collider のレイヤー |
| `spray` | 放水の ParticleSystem (シャワーだけ) |
| `sprayAudio` | 噴射の音 (ループ) |
| `state` | 放水中を同期する子の Udon (`KH_WashToolState`、Manual。同じファイルへ書かず、必要なら B が `KH_WashToolState.cs` を足してよい) |

- シャワー: 持っている人が Use の間、ノズルから中心 1 本 + 周囲 4 本の Raycast (届く距離 1.5 m、半径は距離で広がる)。当たった部位へ `_LocalStroke`。放水の見た目と噴射の音は、放水中の同期値から全員が出す
- スポンジ・タオル: 持っている間、先端の球 (半径 0.08 m) と部位 Collider の最近点。VR は当てるだけで効く。Desktop / Touch は Use の間だけ、視線の中心から 2 m の Raycast の当たった点を接触点にする
- 接触の音と飛沫は `KH_DogWash` 側が、ストロークの位置で出す (自分のも他人のも)

### 量の決め方 (試作値)

| 道具 | 半径 (m) | 1 秒あたりの変化 |
|---|---|---|
| スポンジ | 0.09 | 泡 +0.9、濡れ +0.2 |
| シャワー | 0.07 (距離 1 m で 0.11) | 泡 −1.6、汚れ −0.5 (泡があれば最大 −1.6)、濡れ +1.2 |
| タオル | 0.10 | 濡れ −1.2、泡 −0.4、汚れ −0.15 |

全身を 1〜2 分で洗える速さを目標に、Play で決め直す。

## 7. 犬の行動 (担当 C)

設計書 §1-5 のうち、この試作で入れるのは次だけ。残りは [IMP-DOG-027](../development/issues/IMP-DOG-027-wash-behavior-switching.md)。

- リードを付けていない犬が洗われ中 (`_WashHold()`) は、その場に止まり、操作している人の方へ顔を向ける
- 水の中 (`WaterPose != 0`) では止めない
- 最後のストロークから 3 秒で解ける
- コミカルな反応 1 種: 首をかしげる (`_SetGaze` のロール) を、ストロークの部位が変わった時に。開始は 4 秒あける
- 参照 `KH_DogWash wash` は `KD_DogWanderController` の public フィールドとして足し、null なら何もしない (配線前でも既存の動作を変えない)

## 8. 検証

| 段 | 内容 | 担当 |
|---|---|---|
| コンパイル | C# と U# のエラー 0 | 統括 |
| Editor の Verify | 殻の Renderer (メッシュ・骨・影)、部位 Collider と骨、マスクと位置マップの配線、セルの表、道具の配線 | E が書き、統括が実行 |
| Editor での描画の確認 | Edit モードでマスクへブラシを 1 本描き、殻に帯が出た画像を撮る | 統括 |
| Play / PCVR | 泡 → 水 → 帯、音、反応、Desktop の入力 | ユーザー |
| 実機 | Quest 単体・iOS での描画 (賭け 2・3) | ユーザー |
