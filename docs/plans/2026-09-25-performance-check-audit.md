# パフォーマンスチェック機構の点検 (2026-09-25)

> 目的: 裏山の「性能チェック」が、一般的な Unity の作法と VRChat ワールドの作法から見て網羅できているかを点検する。
> 条件: **Unity 未接続。すべて静的 (コード・YAML・meta・記録文書の読み取り) で、メニューは 1 つも実行していない。** シーンの件数は prefab を展開して数えたが、FBX 内部のコンポーネント (犬 3 匹の SMR など) は数えられていない下限値。
> 分担: 主スレッドが範囲と統合、Opus 2 系統 (機構の網羅性 / シーンの静的棚卸し) が調査、主スレッドが食い違いと重要主張を再確認。
> 容量 (ビルドサイズ) は [2026-09-24 実測](2026-09-24-build-size-audit.md) が持つので、ここでは機構の穴だけ扱う。

## 1. 結論

- **容量は機械化されている** (Build Size 1〜8 + 実測記録)。**描画・CPU 負荷はほぼ「スキルの手順」だけで、本番シーンの描画コールも Quest / iOS の実機値も一度も記録されていない。**
- 「性能チェック」は 4 系統に分かれている: `vrc-performance` スキル (手順) / 共有 `Platform Audit` (統計・境界) / `Build Size` (容量) / VRWorld Toolkit 3.4.1 (World Debugger)。**どれをいつ流すかが文書化されているのは Build Size だけ。**
- Platform Audit は 2026-09-19 に犬 0 匹の状態で 1 回流れたきり。baseline は未保存で「変更事故の検出器」は裏山で一度も動いていない。
- GPU Instancing は**どの機構も見ていない** (スキルは「前後で測れ」と書くのみ)。静的に見ると、静的バッチングにも乗らず Instancing も OFF の同一 Mesh+Material は**犬まわりの道具だけ** (ラック 12 / リード留め具 12 / 持ち手 6 / 看板 9 / クリップ 6)。木・低木は BatchingStatic。
- 検査以前の状態として、**ライティング参照が VCC テンプレート初期状態のまま切れている** (LightingDataAsset / LightingSettings の guid がプロジェクトに無い。Directional は Baked 指定・影なし・ベイクデータなし)。Realtime GI も ON のまま。どの機構も指摘しない (VRWT は「未ベイク」警告を出すはずだが未実行)。

## 2. 既存機構の一覧

| 名前 | 場所 | 見ている項目 | 実行 | 最終実行の痕跡 |
|---|---|---|---|---|
| `vrc-performance` スキル | `claude-skills/vrc-performance/SKILL.md` | 上限外し、`UnityStats` の batches / setPass / tris / shadowCasters、要素ごとの OFF、報告の型。ビルドサイズの罠と点検項目 | 人が読む手順 | — |
| Platform Audit | `package/com.local.vrc-world-tools/Editor/Platform/VwtPlatformAudit.cs` | BOUNDARY (Unity / SDK 版)、STAT (tri / materials / 透明 / textures / SMR / Animator / Particle / camera / RT / lights)、STAT-001 (アクティブ tri 25 万 / 20 万)、MOB-TEX-001 (Renderer 経由の 1024 超テクスチャの Override) | `Tools/VRC World Tools/Platform Audit` | `Logs/vrc-world-tools/platform-audit.json` 2026-09-19 21:38 (犬 0 匹、tri 178,034)。**baseline なし** |
| config | `config/platform-support.json` | required = Windows / Android / iOS、mobilePolicy | Audit が読む | `postProcessingAllowed` / `realtimeLightBudget` / `renderTextureRecommendedMaxSize` / `unknownCustomShaderPolicy` / `transparentMaterialPolicy` は**エンジン未実装** |
| Build Size | `Imported/KominkaDogRun/Editor/KD_BuildSizeEditor.cs` | 1〜3 バンドル実測、4〜5 グループ別、6 参照グラフ、7〜8 Caps の Apply / Verify | `Tools/KominkaHillside/Build Size/1〜8` | [2026-09-24 実測](2026-09-24-build-size-audit.md)。出力先 `Temp/` は現在消えている |
| Texture Budget | `KD_TextureBudgetEditor.cs` | Allsky 1 枚 | Imported-Unused | Caps に吸収済みで冗長 |
| Dog Load Profiler | `KD_DogLoadProfilerEditor.cs` | Play 中に匹数を往復させ CPU Main / PhysBone / Udon / Skin の傾き | Imported-Unused → Play | **裏山で未実行** (`Logs/ver-dog-002-load.txt` なし) |
| Release Check | `KD_ReleaseCheckEditor.cs` | 機能面のみ (Debug 残骸・ShopItem・CE 解放・板の開きっぱなし) | Imported-Unused | **性能項目 0** |
| Terrain Verify | `Scripts/Editor/KH_TerrainPrototypeVerify.cs` | 形状・境界・detail 配置 | `Terrain Prototype/Verify` | 性能設定 (PixelError / Detail / DrawInstanced) は Builder が書くだけで検査なし |
| フリスビーハーネス sixdogs | `docs/testing/kh-frisbee-harness.md` | 上限外し frameTime / UnityStats | `node scripts/kh-frisbee-harness.cjs --mode=sixdogs` | 2026-09-24。**試作シーンのみ** (batches 19) |
| VRWorld Toolkit 3.4.1 | `Packages/dev.onevr.vrworldtoolkit` | 鏡、Occlusion、RT カメラ、トゥーンシェーダー警告、Crunch 率、未ベイク、Probe、Post Processing、GrabPass、Missing shader、Android の圧縮形式。**シェーダーのホワイトリストは target=Android のときだけ** | `VRWorld Toolkit/World Debugger` | `Assets/_LastBuild/` (未追跡・.gitignore 未登録)。Android / iOS のレポートなし。vpm-manifest への追加は未コミット |

VRWT が見ないもの: GPU Instancing、Static Batching、LODGroup、Terrain 設定、音源、Collider / Rigidbody / PhysBone、Canvas 数、ポリゴン予算。

## 3. 観点別の網羅表

○ 機械で見ている / △ 一部・手順のみ / × 見ていない / 実 実機頼み (未実施)

| 観点 | 状況 | 補足 |
|---|---|---|
| GPU Instancing | × | 手順は「前後で測れ」のみ。シーン参照 .mat 223 枚中 ON は 10 枚 |
| シェーダー種別 (Standard / lilToon / Legacy Transparent) | △ | VRWT の警告のみ。Quest ホワイトリストは target=Android 時だけ出る |
| GrabPass | ○ (VRWT) | シーンで使うシェーダーに GrabPass は無い (Uzumore は fur_two のみ使用) |
| Static フラグ / Static Batching | × | Builder が書くだけ |
| LODGroup | × | シーンに 0。LMArt の木 66 本は LOD なし |
| Occlusion ベイク | ○ (VRWT) | 未ベイク (`m_OcclusionCullingData: {fileID: 0}`) |
| ポリゴン数 | ○ | STAT-001。Terrain・TMP・Particle は対象外、非アクティブは含む、iOS は出ない |
| SkinnedMesh / Animator 数 | ○ (記録のみ) | 最終値 0 で古い |
| Mesh Read/Write | × | isReadable=1 の FBX 9 本 (犬 6、Pumpkin 3) |
| Realtime ライト / 影 | △ | STAT 件数のみ。config の `realtimeLightBudget` は未実装 |
| ライトマップ / Realtime GI / ベイク状態 | △ (VRWT) | 参照切れは誰も見ない |
| Reflection / Light Probe | ○ (VRWT) | 両方 0 |
| Fog / Post Processing | △ | Fog OFF、PP なし。config の `postProcessingAllowed` は未実装 |
| Terrain 設定 | × | DrawInstanced 0 / PixelError 5 / Detail 40・密度 1 / Tree 5000・Billboard 50 / Basemap 1000 / 影 TwoSided / 層 4 |
| テクスチャ解像度・圧縮 | ○ | MOB-TEX + Build Size/8。**Verify は Caps 接頭辞に無いパスを黙って飛ばす** (`KD_BuildSizeEditor.cs:661`) |
| POT / ミップ | △ | pcCompress 行だけ NPOT+mip を見る |
| Mipmap Streaming / Read/Write / アニソ | × | streaming ON は TORINOSUKE の 39 枚だけ |
| 音源 Load Type / Preload / Mono | △ | 参照グラフの記録のみ。16 本すべて DecompressOnLoad (短い SE、方針上は最後の手段) |
| カメラ / RT | △ | STAT + VRWT。更新頻度・cullingMask は見ない。現状 RT カメラ 0 |
| パーティクル | △ | 件数と maxParticles 合計のみ (118 系統、上限計 2,360) |
| Udon の Update 数 / 毎フレーム処理 | × | 動作中 U# 約 85 個 (犬 6 匹 × 9 種など)。Dog Load Profiler は未実行 |
| 物理 (Collider / Rigidbody / PhysBone / Contact / Constraint) | × | Rigidbody 21、PhysBone 20、Contact Receiver 55、VRC Constraint 42、Unity RotationConstraint 84 (犬 3 種 × 約 28) |
| UI (Canvas / Raycaster) | × | Canvas 9・Raycaster 5 (すべて inactive 既定) |
| Play 中の frameTime / UnityStats | △ | 本番シーンの記録なし |
| Quest / iOS 実機 | 実 | VER-WLD-001 #6 (todo)。Android Profiler / GPU・CPU 切り分けの手順はスキルに無い |
| 非圧縮メモリ | ○ | Build Size raw (Android 103 MB) |
| 運用 (いつ流すか) | △ | Build Size だけ「素材を足したら 7 → 8 → 2」。Platform Audit / VRWT は裏山の文書から未参照 |
| baseline 差分 | × | 未保存 |

## 4. 機構そのものの問題

1. **Build Size/8 Verify の 0 件は網羅を意味しない。** Caps の接頭辞に一致しないテクスチャは `cap == 0` で `continue` する (`KD_BuildSizeEditor.cs:661`)。「対象外で素通りした枚数」を出さない。Groups にあって Caps に無いフォルダ: `Assets/Scenes/`、`Assets/TextMesh Pro/`、`Packages/`、`Assets/KominkaHillside/Font`、`Materials/`。静的に見て Android / iOS 個別設定の無いシーン参照テクスチャは 80 枚 (Imported 47 / KH Textures 9 / LMArt 8 / モサンゴ屋 7 / TORINOSUKE 6 など)。
2. **MOB-TEX-001 の走査範囲が狭い。** Renderer のマテリアル経由だけで、Skybox (RenderSettings)、Terrain 層、UI Image / Sprite、Cubemap は対象外。
3. **上限の正本が 3 か所にあり値がずれる。** MOB-TEX は Override ≤1024 で可、Caps は非アルベド 512、TextureBudget は Allsky 1 枚。MOB-TEX は Caps 違反を見逃す。
4. **Platform Audit の結果が古い・baseline 未保存。** 09-19 実行時は犬 0 匹、required は Windows / Android のみ。
5. **config に宣言だけの項目がある** (§2 の未実装 5 項目)。書いてあるのに検査されない予算は安心材料に誤読される。
6. **Release Check に性能ゲートが無い。** Verify 0 件 / Android <100 MB / Audit error 0 / VRWT 確認 / デバッグログ OFF が入っていない。`KH_LeashHaptics.logPulls` は**コード既定値 true** で Editor も true を書く (`KH_LeashHaptics.cs:45`、`KH_DogWalkingEditor.cs:200`)。PCVR 記録用としては正当だが、公開ビルドでも毎秒 Debug.Log と文字列連結が走る。Release Check の項目 4 (`logToConsole`) と同型。
7. **Imported-Unused 配下は流されない前提。** Dog Load Profiler / Release Check がここにある。`KD_AndroidToonMaterialsVerifyEditor` は古民家固有の 12 スロットを期待するので裏山では偽 FAIL になる (推測)。
8. **VRWT の扱いが未整備。** `Assets/_LastBuild/` が未追跡で .gitignore にない。vpm-manifest の追加が未コミット。VRWT は Crunch 推奨だがスキルは「Crunch はモバイルで効かない」で衝突。Groups に `vrworldtoolkit` 行があり、ランタイム同梱の可能性は未確認。
9. **STAT-001 の限界。** Android 固定で iOS を出さない。非アクティブを tri に含み、Terrain は含まない。
10. **Build Size の出力が消える。** `Temp/` は再起動で消え、記録は手の転記だけ。

## 5. 静的に見て懸念になり得るもの (直していない)

1. **ライティング参照切れ。** `m_LightingDataAsset` / `m_LightingSettings` の guid がプロジェクトに無い (VCC テンプレート初期コミット由来)。Directional は `m_Lightmapping: 2` (Baked)・影なし・ベイクなし。Unity は未ベイクの Baked ライトを Realtime として描く (フォールバック) ので見た目は出ているが、状態が未定義で、Realtime GI も ON のまま。Mixed / Realtime に決めるか、ベイクを作るか。
2. **犬 6 匹の重なり。** lilToon ファー 2 パス + Uzumore が犬ごとに複数 SMR。RotationConstraint 84 / VRC Constraint 42 / PhysBone 20 / Contact Receiver 55 / 毎フレーム U# 約 85 / Animator 6 が AlwaysAnimate。Quest では主因になり得る。
3. **builtin Standard が多い。** 表示中スロット 91、.mat 100 枚 (古民家の建物 障子 23・木目 21 など)。Mobile Standard Lite は 75。
4. **Occlusion 未ベイク。** 静的物には Occluder / Occludee が付いているのにデータが無い。屋外なので効果は限定的と推測。
5. **同一組で非 static・Instancing OFF** は犬まわりの道具のみ (§1)。モバイルは動的バッチングも OFF (ProjectSettings) なので個別描画になる推定。
6. **Terrain `m_DrawInstanced: 0`** (Editor が明示的に false)。
7. **LODGroup 0。** LMArt の木・低木 66 本はフル解像度。
8. **isReadable=1 の FBX 9 本** (犬 6、Pumpkin 3)。メッシュのメモリが倍。
9. **音源 16 本すべて DecompressOnLoad、preload 9 本。** 短い SE で影響小。方針上は最後の手段。
10. **Particle 118 系統** (TreatFx 4 だけが playOnAwake ループ)。

## 6. 欠けているものの優先順 (Quest 単体・iOS への効き目順)

| # | 欠け | 足す先 (案) |
|---|---|---|
| 1 | Quest / iOS 実機の負荷測定手順と基準値 (OVR Metrics / VRChat のデバッグ表示、CPU と GPU の分離、記録の型) | `vrc-performance` スキル。数字は VER-WLD-001 |
| 2 | 本番シーンの Editor 描画コール基準 (batches / setPass / shadowCasters) | sixdogs の測定部を本番シーンへ流用。結果は IMP-ENV-001 |
| 3 | Terrain 設定の検査 (DrawInstanced / PixelError / Detail / Tree / 層数)、Terrain を STAT に数える | KH_TerrainPrototypeVerify、VwtPlatformAudit |
| 4 | Udon 毎フレーム負荷 (6 匹 + リード接続) | Dog Load Profiler を Imported-Unused から昇格。「Update 持ち UdonBehaviour 数」を STAT へ |
| 5 | ライティング状態の検査 (参照切れ / Baked 未ベイク / Realtime GI) と影の方針 | VwtPlatformAudit の MOB-LIGHT (Phase 2)。方針は人の判断 |
| 6 | config 未実装 5 項目の実装か削除 | VwtPlatformAudit Phase 2 |
| 7 | target に依らないシェーダー互換の列挙 (lilToon / Legacy Transparent / KH_NightOverlay) | VwtPlatformAudit の MOB-SHADER |
| 8 | Build Size/8 に「対象外で素通りした枚数」と Caps 外フォルダの警告 | KD_BuildSizeEditor |
| 9 | Release Check に性能ゲート (Verify 0 件 / Android <100 MB / Audit error 0 / `logPulls` OFF) | KD_ReleaseCheckEditor |
| 10 | PhysBone / Collider / Rigidbody / Canvas / Constraint の件数と基準値 | VwtPlatformAudit の STAT (型名で数える) |
| 11 | GPU Instancing の実測 (道具 5 組) | スキルの手順で前後を測る。機械検査は不要 |
| 12 | Mesh Read/Write / Mipmap Streaming / 音源 Load Type の列 | KD_BuildSizeEditor の参照グラフ |
| 13 | 運用の定義: Platform Audit / VRWT / Dog Load をいつ流すか、baseline の保存タイミング | AI_INDEX と VER-WLD-001 |

## 7. この点検で言えないこと

- Unity 未接続なので、メニューは 1 つも実行していない。現在の Audit / Verify / VRWT の結果、実際の batches・frameTime・PhysBone 数は不明。
- シーンの件数は prefab 展開後の値で、FBX 内部 (犬 3 匹の SMR、建物・木のメッシュ数、Animator の除去) は数えていない下限値。
- TerrainData はバイナリで、heightmap の実値・detail の描画方式・TreePrototype 数は未確認 (Editor コードから 0 と推定)。
- テクスチャの実ピクセル寸法と NPOT かどうかは未確認。
- 「優先順」は古民家の Quest 実績 (IMP-INF-022) とスキルの相場から推した判断で、裏山で実測した寄与ではない。
- VRChat クライアントは自前の画質設定を使うので、ProjectSettings の QualitySettings がそのまま効くわけではない。

## 8. 実測 (2026-09-26、Unity 接続後)

条件: Unity 2022.3.22f1 / Worlds SDK 3.10.5 / 本シーン `VRCDefaultWorldScene` (保存済み・dirty なし) / Editor Desktop Play 1920x1080 / Quality = VRC High (影 All・距離 150) / ClientSim 1 人 / 犬 6 匹待機、操作なし。メニュー実行は主スレッドが直列に行い、Play は一時 Editor スクリプト (計測後に削除、C# エラー 0 を確認) で 25 秒ウォームアップ後に上限を外し 400 フレーム平均。計測後に `targetFrameRate=90 / vSync=0` へ復元済み。

### 8.1 Platform Audit (Development) と baseline

`Logs/vrc-world-tools/platform-audit.md` 2026-09-26 08:59。error 0 / warning 5。同時に **baseline を初めて保存** (`platform-stats-baseline.json`)。次回以降は差分が出る。

| stat | 値 |
|---|---:|
| trianglesActive | 250,150 (**目安 250,000 をわずかに超過** = STAT-001 warning) |
| triangles (非アクティブ込み) | 1,807,734 |
| materials / transparent | 215 / 40 |
| texturesReferenced | 148 |
| skinnedMeshRenderers | 25 |
| animators / particleSystems / particleMaxTotal | 7 / 118 / 2,360 |
| lights / realtime / realtime+shadow | 1 / 0 / 0 |
| cameras / RT | 1 / 0 |

MOB-TEX-001 ×4: `Imported/KominkaDogRun/Textures/HartParticle.png` と `HartParticle2.png` (2048x1024) に Android / iOS の Override なし。

### 8.2 Build Size/8 Verify との食い違い (§4-1・§4-3 の実証)

`Temp/kd-buildsize/mobile-texture-caps.md`: textures over cap 71 / missing 0 / ok 71。**上の 2 枚は Caps 行 `Imp + "Textures/"` が maxSize 2048 なので合格扱い**になり、Audit (≤1024) とは判定が逆になる。3 か所の上限がずれている実例。

### 8.3 Edit モードの実状態 (一時プローブ `Temp/kh-perf-probe/edit.md`)

- **ライティング**: `Lightmapping.lightingDataAsset` = NULL、LightingSettings なし (Unity 既定)、lightmaps 0、Light Probe 0、Occlusion umbraDataSize 0。Directional は `bake=Baked shadows=None isBaked=False` → 未ベイクの Baked ライトとして Realtime に落ちて描かれている (静的推定どおり)。Play の `shadowCasters = 0` もこれと整合 (影は 1 つも出ていない)。
- **Terrain**: drawInstanced=False、PixelError 5、Detail 距離 40・密度 1、木 0 本 (prototypes 0 / instances 0)、TerrainLayer 4 (各 512、ノーマル無し)。Detail 5 層はすべて `GrassBillboard`・`useInstancing=False`・CoverageMode で、密度マップの合計は 1 層あたり 65〜193 万 (256×256 パッチ)。
- **マテリアル**: シーン参照 215 枚中 Instancing ON は 10 枚で、**アクティブなレンダラーに載っているのは 1 枚 (polyperfect atlas-LPAP) だけ**。残り 9 枚は非アクティブな飾り。アクティブなスロットのシェーダー: Particles/Alpha Blended 118、Standard 94、NatureLeaves 63、Mobile Standard Lite 45、SimpleArt 18、lilToon 8 + FurTwoPass 6 + lilToonFur 4。
- **同一 Mesh+Material の繰り返し (アクティブ MeshRenderer 182、うち BatchingStatic 131)**: 木・低木 (16/16/14/14/12/5/5) は BatchingStatic。非 static で Instancing OFF は KH_RackWood Cube 12、KH_SignPlaza Cube 6、KH_LeashSnap 6、KH_AttachmentRing 6、KH_SignHome 3。
- **三角形の内訳**: アクティブ静的メッシュ 189,052。うち **BroadTree02 の葉 5 本 × 18,136 = 90,680 (48%)**、幹 5 × 6,155 = 30,775。SMR 19 本 = 61,098 (犬 1 匹 ≈ 8.5〜11k)。
- **件数**: PhysBone 20 / PB Collider 1 / ContactReceiver 55 / ContactSender 19 / UdonBehaviour 155 / ObjectSync 21 / Pickup 21 / Unity RotationConstraint 84 / VRCParentConstraint 24 / VRCPositionConstraint 12 / LODGroup 0 / ReflectionProbe 0 / Animator 7 / Rigidbody 21 / LineRenderer 18。

### 8.4 Play の描画コール (本番シーンの初回基準。`Temp/kh-perf-probe/play.md`)

| 項目 | 値 (400 フレーム平均) |
|---|---:|
| frameTime (上限外し、Desktop 1080p) | mean 6.30 ms / p50 6.26 / p95 7.20 / max 8.64 |
| batches / setPassCalls / drawCalls | 152 / 79 / 172 |
| triangles / vertices | 311k / 309k |
| shadowCasters | 0 |
| visibleSkinnedMeshes | 19 |
| static batched draw calls / batches | 45 / 18 |
| dynamic batched draw calls / batches | 20 / 8 |
| **instanced** draw calls / batches | **0 / 0** |
| cameras active | 2 (Main + ClientSim) |

読み方: 試作シーンの batches 19 に対し本番は 152。GPU Instancing は 1 バッチも成立していない (対象の道具はすべて Instancing OFF)。静的バッチングは 45 → 18 に畳んでいる。**Desktop の frameTime は VR / Quest の予測に使わない** (`vrc-performance`)。使うのは描画コール数と、今後の変更前後の比だけ。

### 8.5 この実測で言えないこと

- VRWorld Toolkit の World Debugger は未実行 (ウィンドウ前提で API から流していない)。
- Quest 単体 / iOS の実機値は未取得 (VER-WLD-001 #6 のまま)。
- 犬が待機状態での値。散歩・フリスビー中の Udon / PhysBone 負荷は Dog Load Profiler (未実行) の領域。
- Detail (草) の描画コストは batches に含まれるが、単独の寄与は要素 OFF で測っていない。
