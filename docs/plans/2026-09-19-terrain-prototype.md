# Terrain 試作 (VER-ENV-001) 実装計画

> 対象 Issue: [VER-ENV-001](../development/issues/VER-ENV-001-prototype-view-and-edges.md) / [DES-ENV-001](../development/issues/DES-ENV-001-terrain-vs-blender.md)
> 状態: **着手可** (2026-09-19。Codex レビュー 3 往復: 高 5 / 中 12 / 低 1 → 高 2 / 中 9 / 低 2 → 高 0 / 中 4 / 低 2。全件反映。§8)。
> 方針の正本: `core-concept.md` §3 (仮決め)。この計画は「順序と分担」を持ち、何を確かめるかは Issue が持つ。

## 0. ゴールと、この一回で決めないこと

**ゴール**: 「展望から古民家が見える / 一周して端が見えない / 歩ける」を Editor レンダリングと Play で確かめられる最小の舞台を、**数値を変えて作り直せる形**で作る。

**決めないこと**: 最終レイアウト・木の本数・岩・ベンチの意匠・ベイク・犬の実装。試作の数字は全部仮で、VER-ENV-001 の結果で動かす。

## 1. 材料 (揃っているもの)

| 材料 | 場所 | 状態 |
|---|---|---|
| Terrain Layer | `Assets/_TerrainAutoUpgrade/` (モサンゴ屋同梱)。**`garden_Autumn.terrainLayers` から diffuse 名で選ぶ**: `GrassHillAlbedo` / `GrassRockyAlbedo` / `SandAlbedo` / `MudRockyAlbedoSpecular` の 4 枚 (5 枚目の GrassHillAlbedo01 は使わない) | 導入済み。GUID は古民家と一致 |
| 秋の草 Detail 5 枠 (テクスチャ・色・寸法・描画方式・風) | `Assets/KominkaHillside/Data/Environment/Autumn/garden_Autumn.asset` | 移植済み (`imports/kominka-autumn-assets.md`)。**密度マップは移さず、道・広場のマスクから新規生成** |
| 広葉樹 `BroadleafTreeB01` + 秋の葉材質 | LMArt / `Materials/Environment/Autumn/Mat_BroadLeaves02_Autumn` | 導入済み |
| 遠景 `ViewSet01` + 秋材質 4 個 | LMArt / 同上 | 導入済み |
| 古民家の外観 | モサンゴ屋 `建物.prefab` (9,479 tris) + `外壁.prefab` (850 tris) | 導入済み。室内の子を無効化して約 7,000 tris |
| シーン | `Assets/Scenes/VRCDefaultWorldScene.unity` (VRCWorld / Main Camera / Directional Light / Floor / EventSystem) | テンプレートのまま |

無いもの: 岩 (Layer の MudRocky で代用) / ベンチ・柵 (Cube のプレースホルダ) / 犬 (カプセル) / 既存メニュー (依存を持ち込まないので**今回は置かない** → VER-ENV-001 項目 7 は「未」) / 空 (テンプレートの `SKY_UdonLab` のまま)。

## 2. 空間設計 (数値は全部仮)

Terrain 座標 (x: 東, z: 北)。Terrain 原点をワールド (0,0,0) に置き、**120 m (x) × 180 m (z)**、heightmap **129** (格子 0.94 × 1.41 m)、高さレンジ 40 m。
段差・横傾斜が 129 で解消できなければ **257** を試す (切替条件)。

```
z=180 ┌──────────────────────────────┐
      │  北の土手 (+5 m) + 木立。背後 z≈190 に ViewSet01 (北)   │
z=165 │ ┌──────────────────────────┐ │
      │ │  広場 (55,140) 平ら r=10   │ │  ← 犬ダミー 6 (毛色別) + ボール
      │ │        ↖         ↘         │ │
      │土│  (30,115)      展望 (95,125) │土│  ← ベンチ。古民家を見下ろす
      │手│      ↓            ↑        │手│
      │ │  入口 (30,82) → (60,86) → (85,94) │ │  ← 散歩道 (谷側を東へ登る)
z= 76 │ └── 柵 x 12〜108 + 透明壁 (連続) ─┘ │
      │        下り斜面 (約 22°)        │
z= 50 │                                │
      │   谷底 (平ら)  古民家 (60, 35)   │  ← 歩けない。遠景の余白
z= 20 │                                │
      │  南の尾根 (2 → 10 m) 。背後 z≈-10 に ViewSet01 (南)  │  ← 谷底の終端と空白を隠す
z=  0 └──────────────────────────────┘
      x=0                            x=120
```

| 要素 | 位置 (x, z) | 高さ | 備考 |
|---|---|---|---|
| 南の尾根 (景観) | z 0〜20 | 谷底 2 m → z=0 で 10 m | 展望から見下ろした先で谷底の終端が露出しないための背景地形。その背後に ViewSet01 (南) |
| 谷底 | z 20〜50 | 1〜2 m | 平ら。古民家と塀を置く。歩けない (柵 + 斜面) |
| 谷側の斜面 | z 50〜75 | 2 → 12 m | 約 22°。**侵入防止には使わない** (柵 + 透明壁が担う) |
| 歩ける丘 | x 15〜105, z 76〜165 | 12 → 26 m | 緩い起伏 (ガウス山 3〜4 個)。**道の高さは先に決め、地形を道へ寄せる** (§2.1) |
| 入口 (スポーン) | (30, 82) | 約 12 m | 門のプレースホルダは**境界の内側の演出物** (境界を通過する門ではない)。門の柱の Collider はスポーン周囲 2 m と道に重ねない。北向き。柵線 z=76 から 6 m 離す |
| 散歩道 | 入口 → (60,86) → (85,94) → 展望 (95,125) → 広場 (55,140) → (30,115) → 入口 | — | 幅 3 m + 路肩 2 m。**centripetal Catmull-Rom (α=0.5、閉曲線)**。全長は生成後の実測値を記録 (折れ線 200 m、曲線 約 207 m の見込み) |
| 展望・休憩 | (95, 125) | 約 22 m | r=6 を平らに (r=6〜14 で smoothstep 移行)。ベンチ (Cube)。古民家 (60, 1.5, 35) まで水平 97 m・高低差 約 21 m |
| 広場 | (55, 140) | 約 20 m | r=10 を平らに (r=10〜18 で移行)。犬ダミー 6 + ボール |
| 山側の土手 | x 0〜15 / x 105〜120 / z 165〜180 | 歩ける面 +4〜6 m | MudRocky。木を並べる |
| 境界 (Collider) | §2.2 | — | 見た目 (柵・土手) と別に、**連続した透明 BoxCollider の環**で閉じる。柵の見た目は x 12〜108 (側壁と同じ x) |
| 古民家 | (60, 1.5, 35)。縁側が北 (丘) を向く | — | `建物` + `外壁`。室内の子 (ふすま / タンス ×3 / 床の間 / 書院 ×2 / 天井) を非アクティブ。Static (Batching + Occluder/Occludee。Lightmap は今回付けない) |
| 木 | 散歩道の外側と土手に BroadleafTreeB01 を 12 本 (固定座標) | — | Prefab 接続を保持し、葉材質だけ秋へ override |
| 遠景 | ViewSet01 を北 (z≈190) と南 (z≈−10) | — | 秋材質へ override。**Terrain の外に置く景観物** (範囲検査の対象外) |
| ダミー犬 | 広場に 6 個 (カプセル r=0.25・h=0.5)。色: 茶白 (コーギー) / クリーム (ポメ) / 淡黄 (ラブ) / 濃茶 (ダックス) / 赤茶 (柴) / 白 (サモエド) | — | 毛色別の発見性を見る。うち 1 個を木陰に |
| ボール | 広場に 1 個 (球 r=0.1、赤) | — | 発見性 |

### 2.1 高さの生成順 (道の勾配 10° 以下を成立させる)

**後工程が前工程の勾配を壊さない順序**にする (平坦化を道の後にすると移行帯で 20° 超が出る):

1. 基本面: 南→北の緩い勾配 + ガウス山 3〜4 個 + 谷の斜面 + 土手 + 南の尾根。この段階で 3×3 平滑化 (以後は平滑化しない)
2. 展望・広場の平坦化 (半径 r、移行帯 8 m の smoothstep)。平坦部の高さを確定
3. **道の縦断**: 各制御点に高さを与える (平坦部を通る区間はその高さで水平)。曲線に沿って 1 m 刻みで補間し、勾配が 10° を超える区間は高低差を伸ばして解消 (2 m 上げるには水平 11.3 m 要る)
4. 道幅 3 m + 路肩 2 m (格子対角 1.69 m の補間に要る) を道の高さへ。その外 6 m を smoothstep で 2 の面へ戻す
5. 最終高さで §4 の検査 (道の中心・左右端の実 Terrain 高さ)

### 2.2 境界 (プレイヤー・犬が出ない)

- 谷側: 透明 BoxCollider の**歩行側の面**を z=76 に置く (柵の見た目はその面に合わせる。x 12〜108)。門とは無関係に連続
- 山側 (西 x=12 / 東 x=108 / 北 z=168): 透明 BoxCollider。土手の斜面には頼らない (ClientSim の Slope Limit 50° は実機の保証値ではない)
- **各壁の底は区間の最低地面より 1 m 下、上端は区間の最高地面より 4 m 上** (南は 3 m)。長い辺で地形の高低差が大きければ分割し、隣同士を重ねる
- 4 辺は端で重なる (隅の隙間ゼロ)。非 Trigger。Layer は `Environment`。enabled
- `RespawnHeightY` は Terrain 最低面より下 (−10) に置くが、**水平境界の代用にはしない**。万一谷底に入ったらメニュー Respawn で戻ることを Play で確認

Layer 塗り分けの規則: 道 = Sand / 斜度 25° 以上 = MudRocky / 土手 = MudRocky / 残り = GrassHill、ノイズで一部 GrassRocky。各点の重み合計 = 1。alphamap 解像度 256。
草 Detail: `SetDetailResolution(256, 16)`、5 枠を歩ける丘に薄く (各枠の密度・散布 seed を設定に持つ)、道と広場の中心は 0。`detailObjectDistance` **40 m**、`detailObjectDensity` 1 から。`wavingGrass*` は garden_Autumn から複写。

VRCWorld: `spawns[0]` を生成した入口 Transform に**毎回**再配線 (再 Build で旧 Transform は消える)。`RespawnHeightY = -10`。テンプレートの `Floor` は削除。

## 3. 実装方式

**恒久の Editor スクリプト** (一時ではない。作り直しの道具として残す):

| ファイル | メニュー (`Tools/KominkaHillside/Terrain Prototype/`) | 役割 |
|---|---|---|
| `Assets/KominkaHillside/Scripts/Editor/KH_TerrainPrototypeEditor.cs` | `Build Stage A (Ground)` | TerrainData 生成 (高さ・Layer・splat・Collider)、古民家、境界、スポーン、ダミー・ボール。**再実行は `KH_World` を消して作り直す** (手で置いたものは `KH_World` の外に置く) |
| 同上 | `Build Stage B (Dressing)` | 木・草 Detail・ViewSet01・秋材質の override。Stage A の上に足す |
| 同上 | `Verify` | §4 を数える。Build の戻り値は成功判定に使わない |
| `Assets/KominkaHillside/Scripts/Editor/KH_ViewShotsEditor.cs` | `Render View Shots` | 固定カメラ (目線 1.6 m、FOV 60、1920×1080) から Play せずにレンダリングし `Temp/KH_ViewShots/*.png` へ。AI が Read で見る |

TerrainData の契約 (Codex 指摘):

- パス `Assets/KominkaHillside/Data/Terrain/KH_Hill_TerrainData.asset` (バイナリ → `.gitattributes`)。**既存なら LoadAssetAtPath で開いて中身を書き換え、GUID を維持**する。`CreateAsset` は無いときの初回だけ。既存アセットと `.meta` は消さない
- Terrain は `Terrain.CreateTerrainGameObject(data)` で作る (Terrain と TerrainCollider の両方へ同じ data が入る)
- `SetHeights(0,0, float[z,x])` 正規化値 (m ÷ 40)。一括で 1 回呼ぶ (遅延版は使わない)
- `SetAlphamaps(0,0, float[z,x,layer])`、各点の合計 1。heightmap / alphamap / detail の解像度は別々に設定し、非正方形の実寸へ座標変換する
- `DetailPrototype` は **`new DetailPrototype(source)` のコピーコンストラクタで複製**し、変える項目だけ書く (手で項目を写すと noiseSpread / noiseSeed / useInstancing / alignToGround / positionJitter 等が落ちる)。`detailScatterMode` は元と同じ値を明示。密度マップは `SetDetailLayer` でマスクから生成
- **Stage A は Detail マップを全枠クリア**し、Stage B は装飾ルート `KH_Trees` / `KH_Scenery` を消してから置き直し、Detail マップを置換する (再実行で重複しない)
- **Undo は対応しない** (TerrainData の変更・削除・作成・Descriptor 再配線を Undo グループにまとめるコストに見合わない)。戻すのは git と再 Build。Build 前にシーンが dirty なら中断する
- 保存の順序: 全変更適用 → 保存前 Verify → `EditorUtility.SetDirty(terrainData)` → `AssetDatabase.SaveAssets()` → `SaveOpenScenes()` → **再読込後 Verify** (Terrain / TerrainCollider が同じ TerrainData を指す、override が残る、`.unity` の grep はここで判定。保存前 Verify では「未実施」)
- Stage B は Stage A の生成物 (`KH_World` と TerrainData) を前提にする。**地形の設定を変えたら A から再実行**
- 設定クラス (`KH_TerrainPrototypeConfig`) に持つ値: Terrain 寸法・解像度 (height / alphamap / detail) / 谷・斜面・丘・土手・尾根の高さと境界 z / ガウス山の位置・振幅・広がり / 道の制御点 (x, z, **高さ**) と幅 3 m・路肩 **左右各 2 m**・移行帯 6 m / 平坦部の中心・半径・移行帯 / 境界壁の位置と露出高さ / スポーンの位置・向き・接地オフセット / 古民家・塀の位置と向き、無効化する子の相対パス / 木・遠景・ダミー・ボールの配置と色 / Detail の密度・seed・距離 / Layer 規則の閾値。値は実装時に仮置きしてよい
- 購入プレハブは Unpack しない。室内の非アクティブ化は **Prefab インスタンス内の子 GameObject** を相対パスで探し、`new SerializedObject(child.gameObject)` → `m_IsActive = false` → `ApplyModifiedProperties()` で override として記録 (見つからない子があれば失敗扱い)。材質差し替えも SerializedObject 経由。保存・再読込後も残ることを Verify

階層: `KH_World/{KH_Terrain, KH_Places/{KH_FarKominka, KH_Entrance, KH_Viewpoint, KH_Plaza}, KH_Bounds, KH_Trees, KH_Scenery, KH_Dummies}`。将来の犬・UI はこの外。

実行経路は `vrc-synaptic-unity` (force refresh → Editor.log でコンパイル確認 → eval で `ExecuteMenuItem`。メニュー名は ASCII)。一括変更の順序は `vrc-scene-edit-safety`: dirty 確認 → Build → Verify → 保存 → `.unity` を grep。

## 4. Verify で数えるもの

| 項目 | 期待 |
|---|---|
| Terrain Layer 数 / 識別 | 4 / diffuse テクスチャ名が GrassHillAlbedo, GrassRockyAlbedo, SandAlbedo, MudRockyAlbedoSpecular (Layer 資産名は `NewLayer*` なので名前では見ない) |
| heightmap 解像度 / サイズ | **設定クラスの値と一致** (初期 129 / (120, 40, 180)。257 に変えたら期待値も追従) |
| TerrainData のアセットパス | `Assets/KominkaHillside/Data/Terrain/KH_Hill_TerrainData.asset` (再読込後も同じ) |
| `TerrainCollider` が有効で、Terrain と同じ TerrainData を参照 | true |
| 散歩道: **生成後の曲線**を 1 m 刻みで、中心と左右端 (±1.5 m) の実 Terrain 高さを読む | 縦断勾配 ≤ 10° (隣接高低差 ≤ tan10° × 実水平距離。別の段差閾値は置かない) / 横傾斜 ≤ 5° (左端↔中心、中心↔右端の両方) |
| 道の縁と柵線の離隔 | 道の縁 (中心 ±1.5 m) の最小 z ≥ 柵 z + 2 m |
| 展望 (目線 1.6 m) → 古民家の屋根の棟・両端・壁の隅 (5 点) | **TerrainCollider に対する有限長 Raycast** の遮蔽数を**診断値として出す** (合否は View Shots の画像で「屋根と建物が識別できる」)。木・柵・屋根自身は対象外 |
| 古民家の有効三角形数 / 室内の子のアクティブ数 / override が保存後も残る | ≤ 8,000 / 0 / true |
| 接地対象 (道の制御点・スポーン・展望・広場・ダミー・ボール) が歩ける範囲 (x 15〜105, z 76〜165) 内 | 範囲外 0。**景観物 (ViewSet01・南の尾根) と境界部材 (柵・透明壁) は対象外** (境界は次の行で見る) |
| 境界配置: 柵の見た目が x 12〜108・z 76 / 透明壁の位置が §2.2 (西 12 / 東 108 / 北 168 / 南 76) と一致 / **4 辺の端と、分割した同一辺の全継ぎ目**が三次元的に重なる / 壁沿いを 2 m 刻みで **TerrainCollider に限定した `Collider.Raycast`** で地面を取り、地面から壁上端までの露出高さ ≥ 3 m (南) / 4 m (他) かつ **壁底 ≤ 区間の最低地面 − 1 m** / 非 Trigger / Layer=Environment / enabled | 隙間 0、露出不足 0 (サンプル上。区間全体の保証ではない) |
| `Floor` が無い / `spawns[0]` が入口 Transform を参照、北向き / 接地: TerrainCollider への下向き Raycast のヒット高さと Transform の高さの差が接地オフセット ±0.05 m / 埋没: `Physics.OverlapBox(halfExtents=(0.25,1,0.25))` に門の Collider が入らない / 離隔: 門柱の Collider とスポーンの水平距離 ≥ 2 m、門柱が道幅 (中心 ±1.5 m) と交差しない | true |
| Stage A の状態: Detail 全枠がゼロ / `KH_Trees` `KH_Scenery` が無い | true (A 直後) |
| Stage B の状態: 木の本数・遠景 2 個・ダミー 6 + ボール 1 が設定どおり / 秋材質の override が効いている (葉 Renderer の sharedMaterial が `*_Autumn`) / Detail 5 枠のプロトタイプが `Validate()` を通り、歩ける丘の Detail 合計 > 0、道の中心と広場中心で 0 | true (B 直後) |
| `VRCSceneDescriptor.NetworkIDs` の `gameObject: {fileID: 0}` | 0 (保存後の `.unity` を grep) |

## 5. 順序と分担

| # | 作業 | 担当 | 確認 |
|---|---|---|---|
| 1 | 計画の Codex レビュー → 反映 | AI | 済 (§8) |
| 2 | Editor スクリプト 2 本 → force refresh → コンパイル確認 (Editor.log に `error CS` 無し) | AI | `vrc-synaptic-unity` |
| 3 | **Stage A** (地形・道・古民家・境界・スポーン・ダミー) → Verify → 保存 → grep → View Shots (**展望→古民家と問題箇所だけ**) | AI | §4 の該当項目。展望から古民家の形が読めるか、道が柵に食い込まないかを**ここで**直す |
| 4 | **Stage B** (木・草・遠景・秋材質) → Verify → 保存 → View Shots | AI | §4 全項目 |
| 5 | View Shots (Stage B 後にまとめて): 入口・各カーブ (4)・展望・広場 × 谷側・山側・進行方向 + 展望→古民家 + **ダミーとボールを 3 m / 10 m から + 木陰のダミー** (目線 1.6 m / FOV 60 / 位置・向き・俯角を記録) | AI | 画像を `docs/plans/evidence/2026-09-19-terrain-prototype/` へ。VER-ENV-001 項目 1〜3 の**一次判定** (Editor)。木陰は撮った毛色だけ合格。項目 7 は「未」 |
| 6 | `Tools/VRC World Tools/` の診断 | AI | high が無い |
| 7 | Play で歩く (§6) | **ユーザー** | 歩行中の連続した見え方と VR の印象は Play でしか分からない |
| 8 | 結果を VER-ENV-001 と `core-concept.md` §3 に記録。数字を直すなら設定を変えて 3 から | AI | — |
| 9 | **次回**: 犬なしの Android 計測 (地形単体の参考値) → DES-ENV-001 確定 → IMP-DOG-001 (犬移植) → 総負荷計測 | ユーザー + AI | VER-ENV-001 項目 6 は**犬なしで一度取る** (地形方式の判断材料) |

## 6. Play チェックリスト (ユーザー)

§0 エディタで判定できない項目 (歩行中の連続した見え方・VR での印象):

- [ ] 一周のどこかで Terrain の切れ目・空の床・Skybox の底が見えた場所: ____ (固定画像では拾えない角度)
- [ ] 展望で古民家が「あの建物だ」と分かる (Editor 画像で一次判定済み。VR での距離感を確認)

1 人で:

- [ ] 一周の**歩行時間** (寄り道なし、秒): ____ / **滞在時間** (寄り道あり): ____
- [ ] 散歩道を外れずに一周できた。道が分かりにくかった場所: ____
- [ ] 谷側の柵で止まる。柵の両端 (x=12 / 108 の側壁との接合部) を回り込めない
- [ ] 土手を登って外に出られない (通常歩行・ジャンプ)
- [ ] (もし谷底に入れたら) メニューの Respawn で入口へ戻る
- [ ] 広場のダミー 6 個 + ボールを 3 m / 10 m / 木陰で見失わない。見失った色: ____
- [ ] 入口に立ったとき「山の入口」に見える (古民家が背後 / 下にある感じがする)

| 症状 | 見るところ |
|---|---|
| 柵をすり抜ける・回り込める | `KH_Bounds` の BoxCollider 寸法・重なり / Layer / isTrigger |
| 土手の外に出た | 透明 Collider の高さ |
| 切れ目が見える | Terrain の端の高さ (土手・南の尾根を上げる) / ViewSet01 の位置 |
| 古民家が小さすぎる | 古民家を北へ寄せる (z 35 → 45) か展望を南へ |
| 道が急・段差 | §2.1 の勾配上限 / heightmap を 257 へ |

## 7. 入れないもの (この計画で)

ベイク / 岩モデル / ベンチ・柵の本モデル / 空の差し替え / 犬の実装 / 既存メニュー (項目 7 は未) / Android ビルド (次回。犬なしの参考値として取る) / ポストプロセス / 谷底からの自動復帰 (範囲判定が要る。侵入防止を主にする)。

## 8. Codex レビューの反映 (2026-09-19)

| 指摘 | 対応 |
|---|---|
| 高: 南側の地形端 (谷底の終端と空白) が展望から露出する | 南の尾根 (z 0〜20、2 → 10 m) と ViewSet01 (南) を追加。Terrain を z 180 に延長 |
| 高: Catmull-Rom の膨らみで道が柵に入る | centripetal を明記。入口を柵から 6 m 離す。Verify に「道の縁と柵線の離隔 ≥ 2 m」 |
| 高: 境界が閉じていない (柵 x 20〜100、歩行域 x 15〜105) | §2.2: 連続した透明 BoxCollider の環 (4 辺が重なる、非 Trigger、Layer Environment)。Verify に隙間 0 |
| 高: 遠景 (z≈170) が「Terrain 内」検査に落ちる | 範囲検査を接地対象に限定。景観物は対象外 |
| 高: Android 計測と地形方式確定の前後 | §5 手順 9: 犬なし Android 計測 → DES 確定 → 犬移植 → 総負荷計測 |
| 中: 全長 250 m が折れ線 (約 210 m) と合わない / 5〜10 分と食い違う | 実測値に置き換える。歩行時間と滞在時間を分けて記録 |
| 中: 道の勾配 10° を成立させる生成方法が無い | §2.1: 道の縦断を先に決め、地形を道へ寄せる。平坦化に移行帯 |
| 中: heightmap 129 の粒度 | 格子寸法を明記。257 への切替条件。Verify は生成後の曲線と実高さで |
| 中: TerrainData の生成・保存契約 | §3 に配列順・正規化・解像度・SaveAssets・GUID 維持を明記 |
| 中: Detail の設定不足 | 解像度・patch・密度・seed・風・Validate を明記。密度マップはマスクから生成 |
| 中: Prefab override が保存後に残る保証 | SerializedObject 経由 + 再読込後の Verify |
| 中: Layer 4 枚は絶対制限ではない / 使う 4 枚を明示 | diffuse 名で 4 枚を選ぶ。Static は用途別フラグを明記 |
| 中: RespawnHeightY が谷底 (y 0〜2) を戻さない | 水平境界の代用にしない。メニュー Respawn を Play で確認 |
| 中: TerrainCollider とスポーン参照の検査 | Verify に追加。spawns[0] は毎回再配線 |
| 中: Raycast が屋根・木に当たる / Collider 無しで通る | TerrainCollider への有限長 Raycast、5 点 |
| 中: Editor 画像と Play の担当分け | 古民家の識別は Editor で一次判定。端の非露出は Play (§0)。撮影地点 × 方向と視点高さ・FOV を記録 |
| 中: 項目 3・7 の確認対象が無い | 毛色別ダミー 6 + ボール + 木陰 1 個。既存メニューは置かず項目 7 は「未」 |
| 低: 生成処理の段階化 | Stage A (地形・道・古民家・境界) → Stage B (植生・秋材質) |

### 2 回目 (高 2 / 中 9 / 低 2)

| 指摘 | 対応 |
|---|---|
| 高: 平坦化・平滑化を道の後にすると勾配を壊す (移行帯で約 22.7°) | §2.1 を「基本面 + 平滑化 → 平坦部 → 道の縦断 (平坦部は水平) → 道 + 路肩 → 検査」に並べ替え。最後の一律平滑化は削除 |
| 高: Collider の底面・有効高さが未定義 | §2.2: 底は最低地面 −1 m、上端は最高地面 +4 m (南 +3 m)。分割して重ねる。Verify は壁沿いの露出高さ・三次元の重なり・enabled |
| 中: 柵 x 10〜110 が範囲検査 (15〜105) に落ちる | 柵を x 12〜108 (側壁と同じ) に。範囲検査から境界部材を外し「境界配置」の行へ |
| 中: 検査の固定値 (129 / Layer 名) | 設定クラスの値と比較。Layer は diffuse テクスチャ名で識別 |
| 中: 段差 0.15 m が実質 8.5° の別制限 | 隣接高低差 ≤ tan10° × 水平距離に統一。横傾斜は左↔中↔右の両方 |
| 中: 門と透明壁の位置関係 | 門は境界内側の演出物。南壁は連続。門の Collider はスポーン周囲・道に重ねない。OverlapBox で埋没を検査 |
| 中: Stage A/B 再実行の状態 | A は Detail を全クリア、B は装飾ルートと Detail を置換 |
| 中: DetailPrototype の手動複製で項目が抜ける | コピーコンストラクタで複製。detailScatterMode 明示 |
| 中: Undo の範囲 | 対応しないと定義。戻すのは git と再 Build。dirty なら中断 |
| 中: SaveAssets の順序 | 適用 → Verify → SetDirty → SaveAssets → シーン保存 → 再読込 Verify |
| 中: 項目 3 の撮影条件 (3 m / 10 m / 木陰) | View Shots に追加。木陰は撮った毛色だけ合格 |
| 低: 折れ線長 210 m / 図の矢印の向き | 200 m / 曲線 約 207 m に修正。矢印を経路表に合わせた |
| 低: 5 点 Raycast が目的より厳しい | 診断値に格下げ。合否は画像 |
| — (問題なし): CreateTerrainGameObject / SerializedObject m_IsActive の具体化 | §3 に明記 |

### 3 回目 (高 0 / 中 4 / 低 2 → 「修正後に着手可」)

| 指摘 | 対応 |
|---|---|
| 中: 壁底の検査 (§4「地面より下」) が生成条件 (§2.2「−1 m」) より緩い | §4 を「壁底 ≤ 最低地面 − 1 m」に |
| 中: Stage B の成立条件が無い (B 無しでも全項目通る) | §4 に A 直後 / B 直後の状態検査を追加。B は A の生成物が前提、地形設定変更は A から |
| 中: 境界検査の Raycast が壁・柵を拾う / 分割した辺の継ぎ目 / 壁位置の照合 | TerrainCollider 限定の `Collider.Raycast`、全継ぎ目、§2.2 との位置照合。「サンプル上」と明記 |
| 中: スポーン検査の範囲不足 (OverlapBox は半寸法 / 2 m 離隔 / 道との交差 / 浮き) | 埋没・離隔・交差・接地オフセットを分けて検査 |
| 低: 保存手順の重複 | 1 本に統合。`.unity` grep と override 永続化は再読込後に判定 |
| 低: 設定クラスの対象列挙 / 路肩の左右 | §3 に列挙。路肩は左右各 2 m |
