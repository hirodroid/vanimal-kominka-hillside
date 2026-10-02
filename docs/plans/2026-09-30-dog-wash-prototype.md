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

隣の部位の制限 (`_AllowPart`) はスポンジとタオルだけに掛ける。**シャワーは全部位に効く** (2026-10-02、体験改善要望): バインドポーズで前足の肩のテクセルの 4〜22 % (6 匹) は首か腰の Collider に最も近く、肩を狙った水が首・腰に当たると前足の泡を変えられなかった。スポンジの「反対の足へ貫通しない」は残る。

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
| `_RinseReach` | Float | 泡を消す広い減衰 (wOut) の外側の縁 = 縁のノイズ込みの半径 × この値。シャワーは `rinseReach` (1.7)、タオルと汚れの描画は 1.2 (既定値 = 以前の固定値) |
| `_RunoffStep` / `_RunoffAmount` | Float | パス 3 用。近傍を見る距離 (テクセル) と、8 近傍すべてが流された時に減る泡の量 |
| `_AllowPart` | Float の配列 (16) | 部位 ID ごとの許可 (0 / 1)。シャワーは全部 1 |
| `_BoneToPart` | Float の配列 (64) | 骨の番号 → 部位 ID |
| `_EdgeNoise` | Float | 縁の形をノイズで崩す強さ |
| `_CellTex` | 2D | パス 1 用。16×16、点で読まず線形で読む |
| `_Decay` | Vector | パス 2 用。(汚れ, 泡, 濡れ) から引く量 |

| パス | 内容 |
|---|---|
| 0 Brush | ブラシの範囲へ `_Delta` を重み付きで足し、0〜1 へ収める。位置マップが無効なテクセルは前の値のまま |
| 1 Restore | `_CellTex` から全面を書く (途中参加・復旧)。汚れには模様のノイズを掛ける |
| 2 Decay | 全面から `_Decay` を引く (乾く) |
| 3 Runoff | 泡のあるテクセルの 8 近傍 (±`_RunoffStep` テクセル、UV の島の外は数えない) のうち「流された」(濡れ 0.5 以上かつ泡 0.1 未満) 数 n で、泡 −`_RunoffAmount` × n / 8。汚れは泡の比で減り (増えない)、濡れはそのまま。流された近傍が無い・泡が変わらないテクセルは前の値のまま (2026-10-02) |

流れる処理 (Runoff): シャワーのストロークを受け付けると (自分のも受け取ったものも、全員)、最後のシャワーから `runoffSeconds` (1.5 s) の間、`runoffInterval` (0.1 s) ごとにパス 3 を 1 回 (Blit の予算に数える。塗り直し・再描画の待ちの間は動かない)。量は `runoffRate` (3.0 / s) × 経過 (0.25 s まで)。マスクでは流れて泡が消えた濡れたテクセルも「流された」に数えるので、1 回に最大 `runoffStep` テクセルずつ濡れた所の中を進む。

Owner はセル (16×16) の 8 近傍で同じ規則を `runoffCellGain` (1) 倍の量で行う。ただしセルで「流された」に数えるのは**シャワーのストロークが流したセルだけ** (Owner だけが持つ表。シャワーの内側の届く範囲 (半径 + `cellReach`) で濡れ 0.5 以上・泡 0.1 未満になったセル。スポンジが泡を足したセルは外れ、リセット・`_DebugSetDirt`・`_DebugClear`・epoch の変化で全部外れる)。流れて減ったセルからは先へ連鎖しないので、セルが減るのは流したセルの隣 1 周だけ (1 回の窓でマスクの前線が進む約 30 テクセル ≒ セル 2 個に対して 1 個)。真実がマスクより先に清潔になって、泡が見えているのに合図が出ることを防ぐ (gain 4 と連鎖は 2026-10-02 の review で外した)。新しい Owner は、引き継いだセルのうち濡れ 0.5 以上・泡 0.1 未満のものを「流した」として表を作り、窓を 1 回始める (引き継ぎの時に進行中だった窓の続き)。

途中参加: 最初の受信の塗り直し (Restore) は、受け取った時点までの Owner の流れを含む。その後も他の人の窓が続いていた場合に備え、塗り直しで描き直したマスクに泡と流されたセルがあれば、窓を 1 回 (`runoffSeconds`) 自分で始める (同じ規則で塗り直した状態から流す)。塗り直しは流れる処理から起きないので繰り返さない。同期変数・ネットワークイベントは増やさない。清潔の合図での一括消去もしない (2026-10-02 統括の判断)。

## 5. 殻のシェーダー `KominkaHillside/Wash Shell` (担当 A)

2 つ目の SkinnedMeshRenderer に付ける。geometry シェーダーを使わない。汚れと濡れの 1 パス (下の設定) と、泡の層のパス (PC 4 層・それ以外 2 層、`ZWrite On`、高さで切り抜く)。汚れと濡れのパスは`Queue = Transparent+10` から始める。`ZTest LEqual`、`ZWrite Off`、`Blend One OneMinusSrcAlpha` (合成済みの色で重ねる。暗さとつやを 1 パスで両立させるため。Blend だけを変えると暗くなる)。影は落とさない・受けない。シングルパスステレオのインスタンシングに対応する。

| プロパティ | 内容 |
|---|---|
| `_Mask` | 犬のマスク (RenderTexture) |
| `_PosMap` | 位置マップ (A から部位を引いて、顔の上限に使う) |
| `_BoneToPart` / `_PartFoamLimit` (配列 16) | 部位ごとの泡の上限 (頭は 0.35 から) |
| `_BaseOffset` | 毛の表面までの押し出し (メッシュ座標ではなく実寸 m。シェーダーの中でワールド空間で足す) |
| `_FoamThickness` | 泡の量 1 の時の追加の押し出し (m) |
| `_DirtColor` / `_FoamColor` | 色 |
| `_WetDarken` | 濡れの暗さ (Float、0〜1) |
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
| スポンジ | 0.11 | 泡 +1.6、濡れ +0.2 |
| シャワー | 0.07 (距離 1 m で 0.11) | 泡 −4.0 (消す範囲は半径 × 1.7 まで)、汚れ −1.0 (泡があれば × (1 + 泡 × 2.2))、濡れ +1.2。全部位に効く。止めた後 1.5 s、流した所の隣の泡が流れる |
| タオル | 0.10 | 濡れ −1.2、泡 −0.4、汚れ −0.15 |

全身を 1〜2 分で洗える速さを目標に、Play で決め直す。2026-10-02 (体験改善要望「ゴシゴシで泡がバーッと広がり、シャワーでサーッと流れる」): スポンジ 0.09 → 0.11 m・+0.9 → +1.6、シャワーの泡 −1.6 → −4.0・汚れ −0.5 → −1.0、`rinseReach` 1.7 と流れる処理を追加。値は `KH_DogWashSetupEditor.Dog.cs` の `WashTuning` (Wash/11 が書き、Wash/12 が検査する。コードの既定値はシーンへ届かない)。

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

## 9. 試し描きと撮影 (見た目を磨く時)

| メニュー | 内容 |
|---|---|
| `Wash/13 - Paint Test/1〜7` | 場面別の試し描き (泡 → すすぎ / 汚れだけ / 薄い泡 / 厚い泡 / 泡の上の帯 / 濡れだけ / きれいな犬の泡)。実行時と同じ式で描く。シーンは保存しない |
| `Wash/14` | 試し描きを消す |
| `Wash/15`・`Wash/16` | 6 構図の撮影、全場面の撮影 (`Temp/KH_WashShot_{場面}_{構図}.png`) |
| `Wash/17` | シェーダーの既定値を変えた時に、殻とマスクのマテリアルへ書き直す |
