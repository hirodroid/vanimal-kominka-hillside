# VRChatワールド パフォーマンス改善チェックリスト

**調査基準日：2026年9月26日（JST）**  
**収録：174のチェック項目／88件の出典台帳／5区分のプラットフォーム比較**  
対象：Windows Desktop／PCVR／Android単体VR（Quest等）／Androidスマートフォン・タブレット／iOS・iPadOS。

VRChat公式資料、Unity 2022.3の公式マニュアル、Quest計測についてMeta公式資料を照合した。以下はワールドの実測診断ではなく、検査項目と検証計画である。VAnimal固有の優先順位は末尾に実務提案として記載する。

## 1. 読み方と調査範囲

- **仕様**：利用可否・強制制限・APIの挙動。根拠に対象と時期を付ける。
- **公式推奨**：目安。超過を即アップロード不可とは扱わない。
- **検査提案**：公式の仕組みを開発作業へ落としたチェック。全項目を一律に削る指示ではなく、効果と回帰を測定する。
- 末尾の出典台帳にURL・更新日または対象版を記載。すべての資料の今回の確認日は2026-09-26。検索エンジンのクロール日、サイトの著作権年、Unity文書のビルド日を本文の改訂日とは扱っていない。
- Unity 2022.3資料は制作環境との一致を優先して採用した。公開年が古い基礎説明も現行資料と照合し、特定の古いGPU向け助言は現行端末へ一般化していない。
- Unityの一般機能と、VRChatが許可するコンポーネント・公開Udon APIは別。Project Settings、Jobs/Burst、描画パイプライン変更を自由にクライアントへ持ち込める前提にしない。APIは導入SDKのClass Exposure Treeで確認する。[S67]

## 2. 現行仕様と更新時期

| 項目 | 2026-09-26時点で確認した内容 | 性質・出典 |
|---|---|---|
| 制作Unity | 2022.3.22f1 | 正式制作環境。2025-10-03更新ページを再確認 [S01] |
| 正式SDK | リリース一覧の最新は3.10.5、2026-09-04公開 | 使用中プロジェクトの導入版も記録する [S02] |
| Unity 6 | 2026.3.3クライアントOpen Beta。9/15告知はSDKプロジェクトをUnity 6へ移行しないよう明記。9/24時点もクライアントはBeta | 正式制作環境との混同禁止 [S03] [S04] |
| Androidワールド容量 | ビルド時圧縮後100 MBを超えるとupload/access不可 | ハード制限 [S23] |
| iOSワールド容量 | Androidとlimitsを共有する公式説明から100 MB制限を適用 | iOS専用の独立数値条項ではなく、共通制限の適用 [S21] [S23] |
| モバイル三角形 | ワールド全体で約250,000 trianglesを予算化 | 公式推奨。固定のアップロード上限ではない [S23] |
| モバイルテクスチャ | 1024×1024程度までを基本目安 | 公式推奨。2K自体が一律禁止ではない [S23] |
| ワールド容量一般 | 200 MB未満を目指す | 2024-02-13更新の公開ガイドの推奨。PCのハード上限200 MBとはいえない [S24] |
| 公式公開ガイドのFPS | spawnにVRユーザー1人で最低45 FPSを目指す記述 | 同じく2024年の目安。現行全端末・多人数の快適性保証ではない [S24] |
| リアルタイム反射 | SDK3.10.5で全platformのRealtime Reflection Probes有効無効をUdonから制御可能 | 2026-09-04の変更。使用可能でも描画コストは残る [S02] |
| Distance Shadowmask | SDK3.10.5でPCの制御追加 | モバイルと同じ扱いにしない [S02] |
| World Dynamics | PhysBones／Contacts／VRC ConstraintsはSDK3.10.0でworld対応 | 2025-10-24。avatar専用という古い前提を除外 [S17] |
| VRCTween | SDK3.10.4で追加。自動ネットワーク同期なし | 2026-06-17。Udonの手動補間を減らす候補 [S18] [S16] |

**一律の合格値を置かない項目**：PC三角形数、draw calls、SetPass、worldの同時AudioSource数、UdonBehaviour数、NPC数など。今回確認した資料から全端末で通用する単一上限は確定できない。自分の最低対象端末と想定人数で予算を決める。

## 3. プラットフォーム別の整理

| 試験区分 | コンテンツbuild | 重点的に見るもの | 実機試験 |
|---|---|---|---|
| PC Desktop | Windows | CPU main/render、GPU、VRAM、解像度、群衆、鏡・動画・追加camera | PC版Build & Testと実インスタンス |
| PCVR | Windows | 両眼描画、HMD解像度、refresh rate、GPU frame time、stale/reprojected frame、鏡 | HMDを装着して試験。Desktop結果を流用しない |
| Quest等単体VR | Android | 透過overdraw、影、RT、CPU、共有メモリ、継続動作時の熱とフレーム落ち | ADB Build & Test。QuestはOVR Metricsも利用 |
| Androidスマホ／タブレット | Android | 機種差、画面解像度、メモリ、継続負荷、省電力、UI操作 | 対象スマホ実機でADB Build & Test |
| iPhone／iPad | iOS | Metal shader互換、圧縮形式、メモリ、RT、継続負荷、UI操作 | VTP／同一LANによるBuild & Test |

QuestをPCへ接続しPC版VRChatを動かす場合はPCVRに分類する。Android共通buildでも単体VRとスマホを同じ性能試験としない。iOSはAndroidコンテンツのfallbackの説明があるが、専用buildと実機確認を基本にする。[S27] [S28] [S29] [S04]

| ワールド機能 | PC | Android／Quest単体／iOS | 注意点 |
|---|---|---|---|
| カスタムshader | 利用可 | worldでは禁止されていない | avatarのshader allowlistと別。API互換と負荷を確認 |
| Camera／RenderTexture | 利用可 | world利用可 | 台数・画素数・更新頻度・描画範囲 |
| Realtime Light | 利用可 | worldの全面禁止ではない | bake優先、影と照射重複を実測 |
| Post Processing | 対応するPPSv2を確認 | 無効 | モバイルの見た目をPPSに依存させない |
| Cloth | 許可リスト掲載 | 無効 | 代替表現もコストを測る |
| Rigidbody／Collider／Joint | 利用可 | world利用可 | avatarの禁止と混同しない |
| Unity Constraint | 利用可 | world利用可・負荷注意 | 不要な常時評価を確認 |
| AudioSource／Particles | 利用可 | worldで利用可 | avatar専用の個数制限をそのままNPCへ適用しない |

機能差の根拠：[S25] [S26]。iOSへの適用：[S20] [S21]。カスタムshaderの許可は品質保証ではなく、使用するshaderの版・feature・graphics APIごとの実機検証が必要。

## 4. 何を、どこで計測するか

| 手段 | 主な用途 | 限界・注意 |
|---|---|---|
| Unity Profiler | CPU、Rendering、Memory、GC、Physics、Audio、UIの負荷候補 | Editor／ClientSimの相対比較。通常のPlayer接続にはDevelopment Buildが必要 [S09] |
| Unity Frame Debugger | camera／pass別のdraw、batching成立、不要描画 | Editor側の構造解析。VRChat実クライアントへ直接接続可能とは仮定しない [S10] |
| ClientSim | 機能・変数・UI・保存処理の早い反復 | 全機能・通信を再現しない。byteCountも実クライアントのbytesと同じ意味ではない [S07] |
| VRChat Performance tab | 実クライアントの性能グラフ | 2026-01-27正式追加。Debug UI→Performance。端末で取得できる指標を選ぶ [S05] |
| Net Objects／Players | Size、Bps、間隔、Sleeping、Suffering等 | 作者権限またはWorld Debuggingが必要な表示あり。多数表示自体にも負荷 [S06] |
| Audio Sources／Logs | 再生音源とUdon停止・エラー | PlayOneShotはClip欄に反映されない。Backgroundログ収集にも負荷 [S06] |
| Quest OVR Metrics Tool | FPS、App GPU Time、CPU/GPU利用率、熱、stale frames、CSV | 実行中アプリ上の外部HUD。worldにMeta SDKを追加する手順ではない [S08] |
| Android／iOS Build & Test | 公開uploadせずに実機クライアントで確認 | AndroidはADB、iOSはVTP。独自Development Playerの作成とは別 [S28] [S29] |

入口はQuick MenuのSettings最下部にあるToggle Debug UI。キーボードが使えるPCはRight Shift＋「1の左のキー」＋番号でも開ける。FPSだけでなくフレーム時間、スパイク、メモリ増加、通信待ち、入室から操作可能までの時間を分けて記録する。[S06]

## 5. 共通チェックリスト（全体で174項目）

以下の各行は検査・改善候補。根拠列は仕組みの一次資料であり、すべての提案が公式の必須手順という意味ではない。「実務提案」は今回の検査計画として補ったもの。測れない数値を推定で埋めず、利用した計測手段と取得範囲を記録する。

### 5.1 計測条件・負荷の切り分け

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C001 | 環境記録 | Unity／SDK／client build・Live/Beta、端末、OS、CPU/GPU/RAM、解像度、VR refresh rateを残す。 | 実務提案 |
| □ C002 | 比較条件 | 同じ地点・視線・人数・アバター表示設定・機能状態で変更前後を比較。1系統ずつ変更。 | 実務提案 |
| □ C003 | フレーム時間 | 平均FPSに加え、遅いフレーム、95/99パーセンタイル、瞬間停止を確認。CPU待ちとCPU処理を区別。 | [S09] |
| □ C004 | 負荷分類 | CPU／GPU／メモリ／通信／読み込みを分ける。FPS正常でも同期だけ遅れる場合を識別。 | [S09] [S06] |
| □ C005 | 人数による増加 | 1人、代表的少人数、想定最大人数で比較。軽い既知のavatarと実利用avatarを分ける。 | 実務提案 |
| □ C006 | 最悪の組合せ | NPC全稼働＋ライブ＋camera＋鏡＋動画＋UIを試す。各単体試験だけで合格にしない。 | 実務提案 |
| □ C007 | 初回と定常 | 初回DL、入室、初回ギミック使用と、十分読み込んだ通常プレイを分ける。 | 実務提案 |
| □ C008 | 持続負荷 | モバイル／Questは継続動作後も比較。充電・省電力・温度・録画の条件を記録。 | [S08]／実務提案 |
| □ C009 | 計測の上乗せ | ログ、Debug UI、録画、複数クライアント同時起動の負荷を統一。最終試験は不要なログを止める。 | [S06] |
| □ C010 | ビルド内訳 | 圧縮後MBと、texture／mesh／audio／font／lightmap等の内訳を記録。参照された不要素材を調べる。 | [S23]／実務提案 |
| □ C011 | 長時間増加 | UI開閉、生成返却、毛色変更、画像更新を繰り返し、メモリ・処理回数・音源数が増え続けないか。 | 実務提案 |
| □ C012 | 最適化の回帰 | 見た目、音、操作、撫で反応、投擲、遅参、所有者退出まで確認して採用。 | 実務提案 |

### 5.2 計測と描画命令

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C013 | CPU/GPU のどちらが詰まるか | rendering の CPU time、GPU time を分ける。CPU 待ち時間を CPU 計算負荷と読み違えない。負荷箇所が分かってから改善 | [S38] |
| □ C014 | 実機と Editor の差 | 開発 PC の相対比較は利用できるが、Android の絶対性能を代用しない | [S23] |
| □ C015 | Draw Calls / Batches / SetPass | 三つを別に記録。SetPass は shader pass 切替回数。材質の種類数と同じではない | [S39] |
| □ C016 | 描画三角形・頂点 | asset の総量に加え、重い地点のフレームで処理された量を記録 | [S39] |
| □ C017 | 追加描画の内訳 | Frame Debugger で pass、影、追加 camera、instanced 表示を調べる | [S39], [S42] |
| □ C018 | Shadow Casters | 同じ object が複数の影へ描かれる場合も含めて数える | [S39] |
| □ C019 | RT と GPU buffers | RenderTextures 数/bytes、切替、vertex/index upload を異常調査に使う | [S39] |
| □ C020 | 比較条件 | 同じ場所・視線・解像度・人数・avatar・mirror・effect 状態で変更前後を比較する（調査を再現可能にするための提案） | [S38] に基づく提案 |

### 5.3 メッシュ・材質・バッチング

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C021 | 可視 Renderer 数 | 非表示エリアや微小物まで提出されていないか | [S38] |
| □ C022 | material slots / submesh | 小物を同時表示単位で共有材質・atlas 化。world 全体を一材質にまとめることを目標にしない | [S23] |
| □ C023 | Static Batching | 動かない対象で成立を確認し、削減量とメモリ増加を比較 | [S40], [S41] |
| □ C024 | 反復する木・岩の Static 化 | 同じ mesh でも結合 mesh 内にコピーされる。密林でメモリ増大する場合を確認 | [S41] |
| □ C025 | Dynamic Batching | 頂点変換の CPU 負荷を伴う。draw call 減だけを改善とみなさない | [S40] |
| □ C026 | GPU Instancing | 同一 mesh/material、対応 shader、有効チェックと Frame Debugger の実績を確認 | [S42] |
| □ C027 | NPC の instancing | 通常の SkinnedMeshRenderer は Unity の自動 GPU instancing 対象外 | [S42] |
| □ C028 | mesh の結合単位 | 建物と地面、遠く離れた小物を巨大結合しない。culling や光の影響範囲を保つ | [S23], [S48] |
| □ C029 | 実頂点数 | UV/normal 等の頂点属性や分割を含む Unity 側の数を確認。DCC の面数だけで判断しない | [S55] |
| □ C030 | mesh Read/Write | runtime 読み書き等が不要なら無効を検討。CPU 側コピーの保持を避ける | [S55] |
| □ C031 | mesh compression | ファイル容量と形状精度の選択。FPS 改善を保証する設定と説明しない | [S55] |
| □ C032 | 不要な頂点データ | 使わない tangent、normal、blendshape 等を輸入しない。shader と衝突機能の必要条件は保持 | [S55] |
| □ C033 | Skin Weights | NPC ごとの bone 数・一頂点への影響数を点検。品質を保って減らせるか | [S56] |
| □ C034 | Skinned bounds | 動作最大範囲を覆い、過大にも過小にもしない。全行動で欠けないか | [S56] |
| □ C035 | Update When Offscreen | 不要な常時 skinning を止める。bounds を適切に設定する方を優先して検討 | [S56] |

### 5.4 可視範囲・LOD・山林

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C036 | Far Clip | 世界の体験範囲に必要な距離へ。背景の消え方も確認 | [S38], [S37] |
| □ C037 | Near Clip | 極端な値を最適化目的で固定しない。VRChat 設定の影響も実機確認 | [S37] |
| □ C038 | LayerCullDistances | 小物と遠景を別距離へ。32 entries、0 は FarClip を利用。reserved layer は制約あり | [S37] |
| □ C039 | LayerCullSpherical | 現行 Udon API は無効/no-op。使用可能な最適化として紹介しない | [S37] |
| □ C040 | Occlusion bake | 壁に隠れる領域の削減と追加 CPU/メモリを両方測る | [S43] |
| □ C041 | Occluder/Occludee | 固定遮蔽物の設定、窓・扉・低い視点で破綻しないか。動的物体は遮られる側にはなれるが遮る側にはならない | [S43], [S44] |
| □ C042 | LOD が実際に切り替わるか | 遠い木・岩・建物の描画三角形を減らす。最終 cull/billboard も調整 | [S45] |
| □ C043 | LOD Cross Fade | 遷移中は両方の LOD を描く。広い遷移域で同時負荷が増えないか | [S46] |
| □ C044 | Terrain Pixel Error | 画質が許す範囲で増やし描画負荷を比較 | [S47] |
| □ C045 | Terrain Base Map Distance | 遠距離で合成低解像度表現へ切替 | [S47] |
| □ C046 | 草・木の距離/密度 | Detail Distance/Density、Tree Distance、Billboard Start、Max Mesh Trees を実視点で調整 | [S47] |
| □ C047 | 葉・草の重なり | 形状が少なくても transparent/cutout 面の重なりを測る | [S23], [S54] |

### 5.5 照明・影・反射

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C048 | 固定照明の bake | 動かない景観の illumination を焼き、動く NPC/avatars には probes を検討 | [S23], [S60] |
| □ C049 | ライトマップ総容量 | map の枚数と解像度を確認。bake しても memory は必要 | [S23] |
| □ C050 | realtime per-pixel lights | 数だけでなく、同じ mesh に重なるライトと追加 pass を確認 | [S48] |
| □ C051 | ライト範囲と結合 mesh | 大きな mesh を複数ライトが照らす構造で無駄な再描画がないか | [S48] |
| □ C052 | realtime shadows | casters 数と追加描画を比較し、装飾の Cast Shadows を見直す | [S39] |
| □ C053 | Reflection Probe 種別 | static 景観は baked を検討。realtime が本当に必要か | [S49] |
| □ C054 | Realtime Probe 更新 | Every Frame を点検。On Awake/必要時更新/Time Slicing を比較 | [S49] |
| □ C055 | Probe 解像度・HDR | cubemap は 6 方向。realtime は非圧縮で memory が解像度と HDR に依存 | [S49] |
| □ C056 | Probe Culling Mask | 反射で不要な小物を除外 | [S49] |

### 5.6 ミラー・カメラ・RenderTexture

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C057 | mirror 起動時状態 | 原則 off から、手動または近接起動を検討 | [S36] |
| □ C058 | mirror layers | world 全レイヤーを映す必要があるか。軽量設定を用意 | [S36] |
| □ C059 | mirror 解像度 | VR は片目ごとの設定。user の Unlimited で負荷が増える条件もある | [S36] |
| □ C060 | mirror MSAA/light/occlusion | Maximum Antialiasing、Disable Pixel Lights、Turn Off Mirror Occlusion を個別点検 | [S36] |
| □ C061 | 追加 camera 数 | 犬目線・監視・水面等の同時稼働数を数え、機能 off 時に描画停止を確認 | [S23], [S50] に基づく提案 |
| □ C062 | camera 対象範囲 | Culling Mask、Near/Far を必要範囲に限定する | [S37], [S38] |
| □ C063 | RT 解像度・形式 | color format、depth buffer、MSAA、mipmap の要否を確認 | [S50] |
| □ C064 | 更新頻度 | 静止画でよい対象は随時、動く映像も用途相応の頻度で試す（30/8 fps 等を公式基準と呼ばない） | 実務提案 |
| □ C065 | Photo/Stream camera | Screen と Photo の条件を分け、写真 camera on による増分も比較 | [S37] |
| □ C066 | 深度 texture 用ライト | 深度取得のためだけに置いた影付き light を調査。VRCCameraSettings.DepthTextureMode が利用可能 | [S37] |
| □ C067 | HDR/MSAA の override | VRCCameraSettings とユーザー画質設定が相互作用する。世界から無断で品質を落とすのではなく比較設定へ | [S37] |

### 5.7 テクスチャ・メモリ

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C068 | GPU 展開後容量 | download/bundle size と runtime texture memory を別に管理 | [S23], [S51] |
| □ C069 | Crunch | 配布サイズの削減であり GPU メモリ節約にはならない | [S23] |
| □ C070 | プラットフォーム override | PC は BC/DXT 等、Android/iOS は ASTC 等の対応圧縮を確認。見た目と容量で block size を選ぶ | [S51] |
| □ C071 | ASTC block size | 4x4 より 6x6/8x8 等は小容量だが細部品質が落ちる。UI/顔/床を同率に処理しない | [S51] に基づく提案 |
| □ C072 | HDR texture 圧縮対応 | ASTC HDR 非対応なら展開先形式が変わる。通常 ASTC LDR 対応と混同しない | [S51] |
| □ C073 | Texture Read/Write | script pixel 操作が不要なら off。CPU 側コピーを増やさない | [S52] |
| □ C074 | Mipmaps | 視距離のある景観に適用を点検。UI 等は用途別に判断 | [S52] |
| □ C075 | Mip Streaming | CPU の小さな代償で必要 mip のみ読み込む。asset 設定・実クライアントで効くか確認 | [S53] |
| □ C076 | Terrain streaming の例外 | Unity 2022.3 の Terrain texture は mip streaming 対象外 | [S53] |
| □ C077 | 見た目の QA | atlas のにじみ、normal や alpha の破綻、圧縮文字、遠距離 shimmer を点検 | 圧縮・mipmap 設定に対する実務提案 |

### 5.8 シェーダー・透過・パーティクル

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C078 | Shader の不要機能 | normal/specular/追加 texture sample/演算を必要分へ。PC だけで品質判定しない | [S54], [S23] |
| □ C079 | 透明面の面積と重なり | 水、窓、霧、煙、光、葉、ライブ粒子を切り分ける。単純な粒数だけで決めない | [S23] |
| □ C080 | Shader precision | half でよい計算と float を維持する位置/UV を分ける。mobile 実機で誤差確認 | [S54] |
| □ C081 | cutout は万能か | discard/clip の費用は GPU 依存。古い PowerVR 記述を全 iPhone の仕様にしない | [S54] |
| □ C082 | パーティクル同時数 | Max Particles、寿命、放出、重なる effect 数を含め worst case を測る | [S57] に基づく提案 |
| □ C083 | 画面占有率 | Billboard の Max Particle Size と近距離での面積、重なりを確認 | [S58] |
| □ C084 | Offscreen simulation | Culling Mode/Always Simulate の必要性を確認 | [S57] |
| □ C085 | Catch-up spike | Pause And Catch-up の再表示時にシミュレーションが集中しないか | [S57] |
| □ C086 | mesh particles | GPU Instancing は Mesh mode + 対応 shader を確認 | [S58], [S59] |
| □ C087 | Particle sort/trail | Sort Mode の要否と trail 用材質も点検 | [S58] |
| □ C088 | 停止処理 | 終了時 Stop Action/Disable 等で常時実行を残さない | [S57] |

### 5.9 Udon実行

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C089 | Update/LateUpdate/FixedUpdateにある全件ループ | 毎フレーム必須か。イベント駆動、低頻度実行、フレーム分散。全犬×全プレイヤー×全おもちゃの積を調べる。検証：NPC数・人数を増やしたときのCPU時間、処理回数 | [S61] |
| □ C090 | 遅延イベント/タイマー | 同じ定期処理をOnEnable等で二重開始していないか。再有効化ごとに多重化しない設計にする。検証：開閉/入退場を反復して負荷が増えないか | 実装監査提案、[S61] |
| □ C091 | GetComponent、Find、transform、LocalPlayer取得 | 参照をInspector/Start等で保持。UdonSharpBehaviourのGetComponentは型探索も重い。検証：呼出頻度、対象数 | [S61]/[S68] |
| □ C092 | publicメソッド・別Behaviour呼出 | 内部専用はprivate。頻繁な処理の不要なBehaviour間往復を抑える。検証：メソッド呼出数とCPU時間 | [S61] |
| □ C093 | Udonで再実装している単純運動 | Unity Animation等へ置換できるか。ただしAnimatorも実測する。検証：同じ見た目でA/B比較 | [S61]/[S72] |
| □ C094 | 配列/文字列/ログ生成 | 毎フレームnew配列・文字列連結・JSON再構築・Debug.Logを繰り返していないか。値が変化時だけ表示更新。検証：GC Alloc、スパイク、ログ量 | [S68] + 実装監査 |
| □ C095 | Animator/MaterialプロパティID | 頻出文字列指定をStringToHash/PropertyToIDのキャッシュにできるか。利用APIはExposure Tree確認。検証：長いホットループでA/B比較 | [S68]/[S67] |
| □ C096 | 非表示時のロジック | Rendererが見えないだけでUdon、Animator、物理も止まると思い込まない。UIを閉じた/棚を隠した後の処理を確認。検証：機能ON/OFF差、Unityプロファイル | 実装監査 |

### 5.10 物理・衝突判定

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C097 | Collider数・形状・重複 | 見た目用高密度Meshをそのまま当たり判定にしない。単純Box/Sphere/Capsuleや簡略Meshに置き換える候補。検証：Physics CPU、接触数、歩行/投擲回帰 | [S69]/[S70] |
| □ C098 | 動くColliderとRigidbody | 動く物体に適切なRigidbodyを設定。不要な常時動的Body、Joints、接触を調べる。検証：動的Body数、接触数、CPU | [S69]/[S70] |
| □ C099 | 衝突レイヤー | 相互作用不要な組合せを外す。VRChatは**22〜30**のカスタム層の名前・collision matrixを保持。予約層を勝手に使わない。検証：投擲、犬、プレイヤー、UI操作を各プラットフォームで再検証 | [S71] |
| □ C100 | Raycast/Overlapの頻度・範囲 | 距離、LayerMask、Trigger対象を必要最小限に。毎犬が毎フレーム全領域検査しない。検証：呼出数とCPU、判定漏れ | [S71]/[S73] |
| □ C101 | Physics NonAlloc | RaycastAll等を必要に応じNonAllocにし配列再利用。ただし満杯時は結果打切り、順序不定、最寄り保証なし。検証：バッファ満杯検知、検出漏れ、GC | [S71]/[S73] |
| □ C102 | Physics戻り値の有効性 | 予約/保護対象を取得した場合nullになり得るためUtilities.IsValid。無効参照によるUdon停止を性能劣化と取り違えない。検証：実機ログ、異常再現 | [S71] |
| □ C103 | CCD | 全BodyをContinuous Dynamicにしない。速いボール等のすり抜け対策が必要な対象だけ選択。検証：最高速度投擲、CPU時間 | [S69] |
| □ C104 | MeshCollider cooking | 実行中の頻繁なメッシュ変更/cooking、非既定Cooking設定、Read/Write必要性を調べる。検証：スパイク/メモリ、実機衝突 | [S70] |
| □ C105 | 固定更新への移動 | UpdateをFixedUpdateへ移しただけでは軽量化保証なし。見た目/ゲーム処理/物理を適切に分ける。検証：固定更新回数と重いフレーム、触感 | 実装監査 |

### 5.11 NPC・ナビゲーション・アニメーション

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C106 | NPC思考と描画の更新頻度 | 追跡対象選択・撫で検出・近傍検出を毎フレームである必要と分ける。近い犬の触感は維持する。検証：全犬稼働、群衆、撫で応答 | [S61] + 実装提案 |
| □ C107 | NavMesh経路再計算 | SetDestinationの無条件連打、全NPC同一フレームの再経路計算、runtime bakeを調べる。検証：全犬呼出/追跡のCPUスパイク | [S74] + 実装監査 |
| □ C108 | NavMeshObstacle carving | 頻繁に動く全物体へcarvingしない。Move Threshold/Carve Only Stationaryを検討。検証：通路塞ぎ/復帰、CPU | [S75] |
| □ C109 | Animator Culling Mode | 画面外で停止可能なキャラか。Cull Completely候補だがroot motion/Animation Event/ゲーム状態に依存する犬を無条件停止しない。検証：視線外→復帰、遠隔ユーザー、イベント正しさ | [S72] + 回帰提案 |
| □ C110 | Update When Offscreen・Bounds | 常時ONの理由を確認。不正なBoundsは適切な範囲へ修正。Boundsを極端に大きくするとカリング効果が落ちる。検証：全モーション、鏡/追加カメラ、見切れ | [S76] |
| □ C111 | Skinning | 骨数、頂点当たりウェイト、SkinnedMesh数、不要BlendShape、遠距離LOD候補。見た目と変形品質を保って削る。検証：Animation/Rendering CPU、GPU、変形 | [S76]/[S77] |
| □ C112 | Controller/Clip | 使わないレイヤー、カーブ、scale animation、重複パラメータ書込を確認。検証：同時アニメ時CPU、ビルド容量 | [S72] |

### 5.12 同期・所有権・保存

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C113 | ローカル/共有の仕分け | 音量設定、個人UI、個人向けFX、個人カメラを本当に同期すべきか。検証：通信量、ユーザー体験 | [S64] |
| □ C114 | 状態とイベントの仕分け | 遅参者に必要な状態をイベントだけで表現しない。検証：遅参、再入室、所有者退出 | [S64] |
| □ C115 | 送信頻度/データ量 | 値の有意変化時だけ更新。毎フレームRequestSerialization/大配列送信が不要か。固定動作は時刻・開始状態だけ同期候補。検証：byteCount、通信量、遅延 | [S62]/[S64] |
| □ C116 | Ownership | 毎回の所有権取り合い、複数プレイヤー間の所有権競合と、複数NPCのおもちゃ対象競合を調べる。検証：同時操作・owner退出 | [S64] |
| □ C117 | ネットワーク輻輳 | Networking.IsClogged、OnPostSerialization.success/byteCount、GetAllQueuedEventsを記録。検証：値が継続増加するキュー、イベント遅延 | [S62]/[S63]/[S65] |
| □ C118 | Network Stats | ThroughputPercentage、BytesOutAverage、Suffering、オブジェクト毎Size/BytesPerSecondAverage/ReceiveInterval/Sleepingを使う。検証：FPS問題と同期問題を分離 | [S78] |
| □ C119 | VRCObjectSync | 不要なRigidbody同期を避ける。自作同期と同じTransformを二重駆動していないか。検証：投擲、持替、手放し、待機時送信 | [S64]/[S65] |
| □ C120 | ObjectPool | 連続Instantiate/Destroyではなく再利用候補。VRCObjectPoolは有効状態同期と遅参に対応。pool容量もメモリとの交換。検証：使用→返却反復、同時上限、復帰状態 | [S65] + 実装監査 |
| □ C121 | 同期Behaviour構成 | 同一GameObjectでManualとContinuousを混ぜるとManualへ。synced arrayは空配列でも初期化必須。検証：serialization成功/不成功 | [S62] |
| □ C122 | PlayerData | 大きい全保存データの高頻度再送、全キー走査、全プレイヤー更新処理を調べる。必要キーはTryGet。検証：ポイント加算/お気に入り/入室集中時通信 | [S66] |

### 5.13 UI・Canvas

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C123 | Canvas再構築 | 静的UIと頻繁に変化する数値/タイマーをCanvasで分ける。ただし細分化で描画回数が増えることも測定。検証：Canvas rebuild、UI CPU | [S79] |
| □ C124 | 不要Raycast | 非操作UIのGraphicRaycaster、背景/文字のRaycast Target、blocking maskを確認。検証：UI CPUと操作回帰 | [S79] |
| □ C125 | Layout・一覧 | 深いLayoutGroup、ContentSizeFitter依存、全件UI生成、大量重ね描きを確認。必要ページ/行のみ生成候補。検証：メニュー展開スパイク、GC、overdraw | [S79] + 実装監査 |
| □ C126 | UI非表示 | Canvas無効化は描画停止だが子スクリプトやAnimatorは別途確認。表示切替と再構築のトレードオフ。検証：開閉反復、閉じた状態のCPU | [S79] |

### 5.14 音声

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C127 | AudioClip設定 | 長いBGM/環境音のDecompress On Load、短いSEのStreaming一律設定を避ける候補。検証：Audio memory、DSP CPU、Streaming CPU | [S80] |
| □ C128 | 音声圧縮/チャンネル/サンプルレート | monoで十分な位置音、品質、短いSEはPCM/ADPCM、長い音楽は圧縮等を音質確認して選ぶ。各build targetで再encode。検証：build容量、メモリ、聴感 | [S80] |
| □ C129 | 音源同時再生 | 常時鳴る環境音、距離外SE、全犬の重複鳴き声、連打で積まれるOneShotを監査。検証：音源/voice数、CPU、音切れ | 実装監査 |
| □ C130 | SpatialAudio設定 | Far/near/volumetric radiusと聞こえる範囲を確認。avatar音の40m制限をworld上限に転用しない。spatializationプロパティanimationは非対応。検証：現地音響/実機 | [S81] |

### 5.15 外部画像・文字列

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C131 | 画像DLのレート/容量 | 1枚/5秒（scene全体）、最大2048×2048、queue1000、入出力buffer各32MB。まとめ要求と再試行暴走を調べる。検証：遅参、失敗時、queue | [S82] |
| □ C132 | 画像DLの破棄 | 不要になった個別のIVRCImageDownloadをDisposeしないと画像が蓄積し得る。VRCImageDownloader全体のDisposeとは区別する。検証：長時間滞在/繰返更新メモリ | [S82] |
| □ C133 | 文字列DL | 1件/5秒、1件最大100MB、queue1000。上限以内でも巨大JSON解析・全UI再構築は別負荷。検証：更新直後スパイク、GC、回線 | [S83] + 実装監査 |

### 5.16 動画

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C134 | 複数動画・遅参 | 新URL処理はユーザー全video player合算で5秒に1回。複数動画を同時に初期化しない。検証：遅参、切替、失敗後retry | [S32] |
| □ C135 | 配信ファイル | 自前MP4はFast Start。動画の解像度/同時decode/RenderTexture/画面数を実機比較。検証：GPU/CPU/メモリと開始待ち | [S32] + 実装提案 |
| □ C136 | Editorと実機差 | AVProはEditorで再生不可。Androidは昔の「URL resolverがない」前提が古い。再生可否は各platform実機で試す。検証：PC/Quest/Android/iOS | [S32] |

### 5.17 現行SDKのDynamics・Tween・多言語

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C137 | PhysBonesの実負荷 | component数だけでなく対象transform・collider・global collision範囲を確認。avatar用のQuest総量上限と混同しない。1コンポーネント256 transforms上限は別に確認する。 | [S14] |
| □ C138 | PhysBone Limits | 見た目が保てる場合にcolliderの代わりにLimitsを比較。Polarの大量使用を見直す。 | [S14] |
| □ C139 | Contactsの範囲 | Unity Colliderとは別システム。Content Types／Collision Tagsと受信処理を限定し、NPC×人数で増える処理を測る。1 Contactにつき最大16 tagsの制限も確認する。 | [S15] |
| □ C140 | 動的な設定変更 | PhysBones／ContactsのApplyConfigurationChangesを毎フレーム繰り返さず、必要な変更をまとめる。 | [S14] [S15] |
| □ C141 | Constraintの稼働数 | 犬、装飾、小物の追従constraintを棚卸し。Unity Constraintもworldで動くため、mobileで実測。 | [S25] [S17] |
| □ C142 | VRCTweenの使い方 | 手動補間との比較候補。大量同時生成、重複、無限tweenの破棄漏れを確認。同期が必要な状態は別途設計。 | [S16] |
| □ C143 | フォント容量 | 全言語分の巨大atlasを同梱していないか。未使用文字とatlas解像度を整理し、公式fallbackを実機確認。 | [S12] |
| □ C144 | 多言語切替 | 一斉テキスト更新時のCanvas再構築とフォント展開を測る。通常表示と切替直後を分ける。 | [S12]／実務提案 |
| □ C145 | Mip Streamingの実用 | 現在のclientではworldにも導入済み。assetの設定と実機を確認し、全textureのpriorityを一律に上げない。 | [S11]／実務提案 |
| □ C146 | Shader差分と白飛び | 使用shaderの正確な版・機能・build targetを記録。PC／Android／Metalで表示と性能を別確認。 | [S33] [S34] |

### 5.18 見落としやすい追加負荷

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C147 | 影の描画距離 | SetShadowDistanceのPC low/medium/highとmobile値を用途に合わせて検証。大きい距離を強制しない。 | [S13] |
| □ C148 | shaderの追加pass | アウトライン、毛の多重pass、画面コピー、スクリーン表現をOFFにしてdraw／GPU時間差を測る。 | [S10]／実務提案 |
| □ C149 | Particle衝突 | Collision moduleの品質、対象layer、動的collider対象を確認。装飾粒子に不要な衝突処理を残さない。 | [S88] |
| □ C150 | Particleの増幅 | sub-emitter、particle lights、trailsの同時発生を含めて上限時の負荷を測る。 | 実務提案 |
| □ C151 | materialの複製 | Renderer.materialアクセスが共有materialを複製する場合に注意。色変更反復で複製数が増えないか。 | [S86] |
| □ C152 | RenderTextureの生存期間 | 動的に作ったRTの再作成と解放を確認。ReleaseはGPUリソースを解放するが、object自体の破棄とは別。 | [S87] |
| □ C153 | 入室時の初期化集中 | Start／OnPlayerJoined／保存復元で一度に全NPC・全UI・全オブジェクトを処理していないか。分散後も依存順を保つ。 | [S61]／実務提案 |
| □ C154 | ローカル品質設定 | 追加camera・FX・影などの軽量設定を用意する場合、ゲーム結果を変えず、設定変更自体が同期・再初期化を乱発しないか。 | 実務提案 |

## 6. プラットフォーム固有の追加確認

共通チェックに加えて次を確認する。これは端末別の試験計画で、公式の新しい数値制限ではない。

### プラットフォーム別チェック

| ID | 確認項目 | 確認・改善と検証 | 根拠 |
|---|---|---|---|
| □ C155 | PC Desktop：低性能側のPCと高解像度設定 | CPU main/renderとGPU時間を分け、解像度変更で負荷がどう変わるか。 | 実務提案 |
| □ C156 | PC Desktop：VRAMの取り合い | world単体に加え複数avatar、写真camera、動画で不足・カクつきが出ないか。 | 実務提案 |
| □ C157 | PC Desktop：PC限定表現 | PPS、影、reflection品質を段階設定で比較。高品質が常時必須でないか。 | 実務提案 |
| □ C158 | PCVR：HMD実機・両眼 | Desktop成功だけで終えず、stereo shader、片目欠け、文字、影、水面を確認。 | 実務提案 |
| □ C159 | PCVR：描画解像度とrefresh rate | render scaleと表示周期を記録。再投影込みの表示FPSと新規描画FPSを混同しない。 | 実務提案 |
| □ C160 | PCVR：鏡・写真・追加camera併用 | 両眼mirrorと犬目線camera等が同時に動く最大負荷を試す。 | 実務提案 |
| □ C161 | Quest等単体VR：圧縮後100 MBと共有メモリ | build制限内でも実行中メモリ不足はあり得る。入室・群衆・継続動作を試す。 | 実務提案／[S23] |
| □ C162 | Quest等単体VR：透明面と影 | 近距離particle、毛、草、水面、窓、ライブを重ねた実視点で試す。 | 実務提案 |
| □ C163 | Quest等単体VR：CPU・GPU・熱 | QuestではOVR Metricsの基本指標から測り、stale frameや継続劣化を確認。 | 実務提案／[S08] |
| □ C164 | Quest等単体VR：Blitの深度条件 | VRCGraphics.BlitではZTest Alwaysまたは出力RTのdepth無効化というQuest向け公式注意を確認。 | 実務提案／[S30] |
| □ C165 | Androidスマホ：機種別 | Questで動いたことだけで合格にせず、最低対象スマホと別GPU系統を試す。 | 実務提案 |
| □ C166 | Androidスマホ：画面条件 | 実解像度とUIの見やすさ・操作判定を確認。高解像度画面の描画コストを記録。 | 実務提案 |
| □ C167 | Androidスマホ：長時間と復帰 | 継続利用・省電力・バックグラウンドからの復帰で停止処理や再初期化の重複を確認。 | 実務提案 |
| □ C168 | iOS／iPadOS：専用build | iOS用のimport・shader・texture設定を点検し、実機VTP試験を通す。 | 実務提案／[S29] |
| □ C169 | iOS／iPadOS：Metal固有差 | geometry stage前提のshaderを避け、精度・深度・UV・RT形式を確認。 | 実務提案／[S33] [S34] |
| □ C170 | iOS／iPadOS：圧縮形式 | ASTC LDRとHDRの対応を混同しない。非対応形式の展開によるメモリ増加を確認。 | 実務提案／[S35] |
| □ C171 | iOS／iPadOS：メモリと継続動作 | Androidと同じ容量制限でも同じ余力ではない。iPhone/iPadの対象機で確認。 | 実務提案／[S20] [S21] |
| □ C172 | 全platform間：同期構造を保持 | 軽量化で同期対象を削除・差し替える際、Network ID・Udon同期構造の整合を確認。 | 実務提案／[S84] [S85] |
| □ C173 | 全platform間：品質差分とゲーム結果 | FXを減らしても当たり判定・共有状態・報酬が一致するか。外見の差分とゲーム状態を混ぜない。 | 実務提案 |
| □ C174 | 全platform間：リリース一致 | 同一world IDで各buildの更新版を記録。見た目だけ古いbuildが残らないか確認。 | 実務提案／[S27] |

## 7. 通信・保存の数値を読むときの注意

| 対象 | 公式記載 | 時期・解釈 |
|---|---|---|
| Udon同期の概数 | 約11 KB/s、Manual約280,496 B/serialization、Continuous約200 B/serialization | 2025-08-08更新。仕様変更あり。上限を埋める目標にはしない [S62] |
| Network Events | パラメータ合計最大16 KB。送出約18 KB/sはoverhead込み、実効は通常8〜10 KB/s程度との説明 | 2026-06-17更新。通常のUdon同期とthroughput制限の規則を共有。上の11 KB/sと別枠で加算しない [S63] |
| Event rate | イベント側設定は既定5件/s・最大100件/s。全体にも動的な約100件/s制限 | 大きいイベントの内部分割も件数に算入。送信キューの継続増加を確認 [S63] |
| PlayerData | 1キー変更でも、そのプレイヤーの全PlayerDataを送る | 大きな低頻度データと高頻度データの混在を確認。必要ならPlayerObjectへ分離 [S66] |

型のメモリサイズと実送信量は異なる。実clientのOnPostSerialization.byteCount、成功可否、Network Statsを使用する。ClientSimの数値は同じ意味ではない。[S78] [S07]

## 8. 古い・誤用しやすい最適化情報

| よくある説明 | 今回の整理 |
|---|---|
| Questのworldも指定Mobile shader以外は禁止 | avatarの制限。worldはcustom shader可。性能と互換性は別途確認 |
| 25万polyが強制上限／1K超textureは禁止 | 公式の推奨。trianglesとして扱い、面数やDCCのpolygon数と混同しない |
| ポリゴンとdownload MBが少なければ軽い | overdraw・draw提出・script・physics・runtimeメモリを別に測る |
| CrunchでVRAMが減る | 主に配布容量の圧縮。GPU展開後容量とは別 |
| 全meshを1個にまとめれば最速 | cullingとlightの範囲を悪化させ得る。static batchingのメモリ増にも注意 |
| InstancingをONにすれば犬も一括描画 | 成立条件があり、通常のSkinnedMeshRendererは自動instancing対象外 [S42] |
| Occlusion bakeは必ず高速化 | CPU・ベイクデータの費用もある。室内と開けた山林で効果を比較 [S43] |
| 鏡ではOcclusionが絶対に効かない | 公式VRCMirrorに無効化設定が存在。実際の可視性・ちらつきを検証 [S36] |
| 透明フェードすれば描画コストは消える | 透明度だけでは描画命令の消滅を保証しない。Renderer／camera／script停止を別確認（実務上の推論） |
| モバイルでRealtime Reflection Probeは常に不可 | SDK3.10.5の2026-09-04変更を反映する [S02] |
| Unity6 client betaだから制作projectも移行可能 | 9/15公式告知はSDK project移行不可と明記 [S03] |
| Editor Profilerへ実VRChatをそのまま接続 | 一般UnityのDevelopment Player手順とworld bundleを混同しない [S09] |
| Force GCを押せば軽くなる | 公式は改善策として否定。反復使用は不安定化し得る [S06] |
| avatar向けPer-Platform Overrideをworldへそのまま使える | 今回のworld共通手順には採用しない。target別import・build管理を確認 [S27] |

LayerCullSphericalは現行Udonでは無効（no-op）。LayerCullDistancesは利用可能だが予約layer等の制限がある。画面camera、写真camera、world内追加cameraで適用範囲を確認する。[S37]

## 9. VAnimalで優先的に測るなら

以下は共有されている機能構成を基にした**検査優先案であり、ボトルネックを特定した診断ではない**。

| 順番 | 対象 | 最初の比較 |
|---|---|---|
| 1 | 犬・猫NPC | 全員待機→全員移動→Fetch→集合→ライブ。Udon思考、NavMesh、Animator、skinningを分ける |
| 2 | 犬目線camera | ON/OFF、解像度、更新頻度、描画layer、見ていないときの停止。ローカルでも追加描画負荷はある |
| 3 | ライブ・撫でFX | 最大同時発動、目の前の粒子、透明面の重なり。粒数だけで判断しない |
| 4 | 裏山の植生 | 同じ見晴らしで草木の距離・LOD・密度・shaderを1つずつ比較。遮蔽の少ない場所のOcclusion効果も測る |
| 5 | 古民家の部屋と鏡 | 室内外同時描画、ライト、影、遮蔽、鏡を比較 |
| 6 | おもちゃ・おやつ・棚 | 非表示後に物理・Udon・同期が残らないか、プール返却とownership競合 |
| 7 | ポイント・お気に入り・多言語UI | PlayerData全量送信、UI再構築、font容量、言語切替時のスパイク |
| 8 | 長時間・多人数 | 操作反復、遅参、owner退出、機能全部使用でメモリ／キュー／処理が増え続けないか |

NPCを「見えないから全停止」にすると、別の人とのふれあいや共有状態を壊す可能性がある。描画、ローカル演出、ゲームの進行・同期を分けて停止条件を設計する。

## 10. 毎回使う測定記録と完了条件

測定1件ごとに下記を記録する。取得できない指標は「未取得」と記載。

| 記録欄 | 内容 |
|---|---|
| 識別 | world revision／SDK／Unity／client build・Live/Beta／試験日 |
| 端末 | 端末名／OS／CPU・GPU／RAM・VRAM／画面解像度／HMD refresh rate |
| 条件 | 場所・向き・移動ルート／人数／avatar表示条件／ONの機能／温度・充電・省電力 |
| 性能 | FPSとframe ms／遅い区間・スパイク／CPU・GPU分類／stale frames |
| 内訳 | Batches・SetPass・tris／texture・mesh・RT・audioメモリ／Udon・物理・UI |
| 通信 | Bps／serialization bytes／queue／IsClogged／遅参収束時間 |
| 読み込み | 初回DL／入室から操作まで／初回機能使用／長時間後 |
| 判定 | 変更前後の差／画質と機能の回帰／採用・差戻し／残課題 |

実務上の完了条件：最低対象端末で合意したフレーム時間を満たす、想定最大負荷でも操作が成立する、継続利用でメモリやイベントキューが増え続けない、全platformで共有状態と主要機能が維持される。公式上限通過だけで完了としない。

**数学の補足**：1フレームの時間は `1000 ÷ FPS` ms。60 FPSは約16.67 ms、72は13.89 ms、90は11.11 ms、120は8.33 ms。これはVRChat本体とavatarも含めた全体の時間で、worldだけの予算ではない。学ぶなら「反比例」と「統計（平均・分位点・ばらつき）」が測定結果を読むのに役立つ。

## 11. 出典台帳

VRChatは本文の最終更新日またはリリース日、Unityは対象版を記載。Unityの2022.3マニュアル本文の個別改訂日が明確でない場合は「不明」としている。資料が今も掲載されていることだけで全記述の新しさを保証せず、更新履歴と対象範囲を併用した。

| ID | 一次資料 | 日付・対象版 |
|---|---|---|
| S01 | [Current Unity Version](https://creators.vrchat.com/sdk/upgrade/current-unity-version/) | 2025-10-03 更新 |
| S02 | [SDK 3.10.5](https://creators.vrchat.com/releases/release-3-10-5/) | 2026-09-04 公開 |
| S03 | [2026.3.3 Open Beta](https://docs.vrchat.com/docs/vrchat-202633-open-beta) | 2026-09-15 Build 1913 の記述／9月22日分まで確認 |
| S04 | [Developer Update 24 September 2026](https://ask.vrchat.com/t/developer-update-24-september-2026/48972) | 2026-09-24 公開 |
| S05 | [2026.1.1 Performance Debug Dashboard](https://docs.vrchat.com/docs/vrchat-202611) | 2026-01-27 正式公開 |
| S06 | [World Debug Views](https://creators.vrchat.com/worlds/udon/world-debug-views/) | 2026-01-29 更新 |
| S07 | [ClientSim](https://creators.vrchat.com/worlds/clientsim/) | 2025-01-17 更新 |
| S08 | [OVR Metrics Tool](https://developers.meta.com/horizon/documentation/unity/ts-ovrmetricstool/) | 2026-06-21 更新 |
| S09 | [Unity Profiler接続とEditor計測](https://docs.unity3d.com/ja/2022.3/Manual/profiler-profiling-applications.html) | Unity 2022.3／本文改訂日不明 |
| S10 | [Unity Frame Debugger](https://docs.unity3d.com/ja/2022.3/Manual/FrameDebugger.html) | Unity 2022.3／本文改訂日不明 |
| S11 | [VRChat Mipmap Streaming導入](https://docs.vrchat.com/docs/vrchat-202442) | 2024-12-05 正式公開／導入履歴 |
| S12 | [VRChat TextMesh Pro](https://creators.vrchat.com/worlds/components/textmeshpro/) | 2024-05-21 更新 |
| S13 | [VRCQualitySettings](https://creators.vrchat.com/worlds/udon/vrc-graphics/vrc-quality-settings/) | 2026-06-10 更新／新機能はSDK3.10.5告知も参照 |
| S14 | [PhysBones](https://creators.vrchat.com/common-components/physbones/) | 2026-08-27 更新 |
| S15 | [Contacts](https://creators.vrchat.com/common-components/contacts/) | 2026-06-17 更新 |
| S16 | [VRCTween](https://creators.vrchat.com/worlds/udon/vrctween/) | 2026-05-28 ページ更新／SDK3.10.4で6月17日正式追加 |
| S17 | [SDK3.10.0 World Dynamics](https://creators.vrchat.com/releases/release-3-10-0/) | 2025-10-24 公開 |
| S18 | [SDK3.10.4](https://creators.vrchat.com/releases/release-3-10-4/) | 2026-06-17 公開 |
| S19 | [Platforms](https://creators.vrchat.com/platforms/) | 2025-10-08 更新 |
| S20 | [iOS](https://creators.vrchat.com/platforms/iOS/) | 2025-12-05 更新 |
| S21 | [Setting up Unity for Creating iOS Content](https://creators.vrchat.com/platforms/iOS/setting-up-unity-for-creating-ios-content/) | 2025-05-16 更新 |
| S22 | [Mobile Best Practices](https://creators.vrchat.com/platforms/android/android-best-practices/) | 2024-08-02 更新 |
| S23 | [Android Content Optimization](https://creators.vrchat.com/platforms/android/quest-content-optimization/) | 2025-06-25 更新 |
| S24 | [World Creation, Optimization, and Community Labs Tips](https://creators.vrchat.com/worlds/submitting-a-world-to-be-made-public/) | 2024-02-13 更新 |
| S25 | [Android Content Limitations](https://creators.vrchat.com/platforms/android/quest-content-limitations/) | 2025-10-08 更新 |
| S26 | [Allowlisted World Components](https://creators.vrchat.com/worlds/whitelisted-world-components/) | 2025-11-25 更新 |
| S27 | [Cross-Platform Setup](https://creators.vrchat.com/platforms/android/cross-platform-setup/) | 2025-03-11 更新 |
| S28 | [Build and Test for Android Mobile](https://creators.vrchat.com/platforms/android/build-test-mobile/) | 2026-02-26 更新 |
| S29 | [Build and Test for iOS](https://creators.vrchat.com/platforms/iOS/build-test-mobile/) | 2026-02-26 更新 |
| S30 | [VRCGraphics](https://creators.vrchat.com/worlds/udon/vrc-graphics/) | 2025-04-15 更新 |
| S31 | [VRChat is Now Available on iOS and Android!（年は blog index でも確認）](https://hello.vrchat.com/blog/vrchat-mobile-release) | 2025-10-24 記事日 |
| S32 | [Video Players](https://creators.vrchat.com/worlds/udon/video-players/) | 2024-08-06 更新 |
| S33 | [Unity 2022.3 Targeting shader models and GPU features in HLSL](https://docs.unity3d.com/2022.3/Documentation/Manual/SL-ShaderCompileTargets.html) | Unity 2022.3／本文改訂日不明 |
| S34 | [Unity 2022.3 Platform-specific rendering differences](https://docs.unity3d.com/2022.3/Documentation/Manual/SL-PlatformDifferences.html) | Unity 2022.3／本文改訂日不明 |
| S35 | [Unity 2022.3 Recommended and supported texture formats, by platform](https://docs.unity3d.com/2022.3/Documentation/Manual/class-TextureImporterOverride.html) | Unity 2022.3／本文改訂日不明 |
| S36 | [VRC Mirror Reflection](https://creators.vrchat.com/worlds/components/vrc_mirrorreflection/) | ページ最終更新 2024-07-31 |
| S37 | [VRCCameraSettings](https://creators.vrchat.com/worlds/udon/vrc-graphics/vrc-camera-settings/) | ページ最終更新 2025-08-27 |
| S38 | [Graphics performance fundamentals](https://docs.unity3d.com/2022.3/Documentation/Manual/OptimizingGraphicsPerformance.html) | Unity 2022.3／本文改訂日不明 |
| S39 | [Rendering Profiler](https://docs.unity3d.com/ja/2022.3/Manual/ProfilerRendering.html) | Unity 2022.3／本文改訂日不明 |
| S40 | [Draw call batching](https://docs.unity3d.com/2022.3/Documentation/Manual/DrawCallBatching.html) | Unity 2022.3／本文改訂日不明 |
| S41 | [Static batching](https://docs.unity3d.com/ja/2022.3/Manual/static-batching.html) | Unity 2022.3／本文改訂日不明 |
| S42 | [GPU instancing](https://docs.unity3d.com/2022.3/Documentation/Manual/GPUInstancing.html) | Unity 2022.3／本文改訂日不明 |
| S43 | [Occlusion culling](https://docs.unity3d.com/ja/2022.3/Manual/OcclusionCulling.html) | Unity 2022.3／本文改訂日不明 |
| S44 | [Dynamic occlusion](https://docs.unity3d.com/ja/2022.3/Manual/occlusion-culling-dynamic-gameobjects.html) | Unity 2022.3／本文改訂日不明 |
| S45 | [Mesh LOD](https://docs.unity3d.com/ja/2022.3/Manual/LevelOfDetail.html) | Unity 2022.3／本文改訂日不明 |
| S46 | [LOD Group](https://docs.unity3d.com/ja/2022.3/Manual/class-LODGroup.html) | Unity 2022.3／本文改訂日不明 |
| S47 | [Terrain settings](https://docs.unity3d.com/ja/2022.3/Manual/terrain-OtherSettings.html) | Unity 2022.3／本文改訂日不明 |
| S48 | [Forward rendering](https://docs.unity3d.com/ja/2022.3/Manual/RenderTech-ForwardRendering.html) | Unity 2022.3／本文改訂日不明 |
| S49 | [Reflection probe performance](https://docs.unity3d.com/ja/2022.3/Manual/RefProbePerformance.html) | Unity 2022.3／本文改訂日不明 |
| S50 | [RenderTexture](https://docs.unity3d.com/ja/2022.3/Manual/class-RenderTexture.html) | Unity 2022.3／本文改訂日不明 |
| S51 | [Texture compression by platform](https://docs.unity3d.com/ja/2022.3/Manual/class-TextureImporterOverride.html) | Unity 2022.3／本文改訂日不明 |
| S52 | [Texture default import settings](https://docs.unity3d.com/ja/2022.3/Manual/texture-type-default.html) | Unity 2022.3／本文改訂日不明 |
| S53 | [Mipmap streaming](https://docs.unity3d.com/2022.3/Documentation/Manual/TextureStreaming.html) | Unity 2022.3／本文改訂日不明 |
| S54 | [Shader runtime performance](https://docs.unity3d.com/ja/2022.3/Manual/SL-ShaderPerformance.html) | Unity 2022.3／本文改訂日不明 |
| S55 | [Model importer](https://docs.unity3d.com/ja/2022.3/Manual/FBXImporter-Model.html) | Unity 2022.3／本文改訂日不明 |
| S56 | [Skinned Mesh Renderer](https://docs.unity3d.com/ja/2022.3/Manual/class-SkinnedMeshRenderer.html) | Unity 2022.3／本文改訂日不明 |
| S57 | [Particle main module](https://docs.unity3d.com/ja/2022.3/Manual/PartSysMainModule.html) | Unity 2022.3／本文改訂日不明 |
| S58 | [Particle renderer module](https://docs.unity3d.com/ja/2022.3/Manual/PartSysRendererModule.html) | Unity 2022.3／本文改訂日不明 |
| S59 | [Particle GPU instancing](https://docs.unity3d.com/ja/2022.3/Manual/PartSysInstancing.html) | Unity 2022.3／本文改訂日不明 |
| S60 | [Light Probes](https://docs.unity3d.com/ja/2022.3/Manual/LightProbes.html) | Unity 2022.3／本文改訂日不明 |
| S61 | [performance tips](https://creators.vrchat.com/worlds/udon/udonsharp/performance-tips/) | Last updated 2025-01-13 |
| S62 | [network details](https://creators.vrchat.com/worlds/udon/networking/network-details/) | 2025-08-08。約11KB/s等、将来変更可能 |
| S63 | [events](https://creators.vrchat.com/worlds/udon/networking/events/) | 2026-06-17。新network events |
| S64 | [performance](https://creators.vrchat.com/worlds/udon/networking/performance/) | 2025-04-28 |
| S65 | [network components](https://creators.vrchat.com/worlds/udon/networking/network-components/) | 2024-11-19。ObjectPool、IsClogged、OnPostSerialization |
| S66 | [player data](https://creators.vrchat.com/worlds/udon/persistence/player-data/) | 2025-11-25 |
| S67 | [class exposure tree](https://creators.vrchat.com/worlds/udon/udonsharp/class-exposure-tree/) | 2024-08-06 |
| S68 | [BestPracticeUnderstandingPerformanceInUnity7](https://docs.unity3d.com/ja/2022.3/Manual/BestPracticeUnderstandingPerformanceInUnity7.html) | Unity 2022.3／本文改訂日不明 |
| S69 | [class Rigidbody](https://docs.unity3d.com/2022.3/Documentation/Manual/class-Rigidbody.html) | Unity 2022.3／本文改訂日不明 |
| S70 | [class MeshCollider](https://docs.unity3d.com/2022.3/Documentation/Manual/class-MeshCollider.html) | Unity 2022.3／本文改訂日不明 |
| S71 | [layers](https://creators.vrchat.com/worlds/layers/) | 2026-08-17。NonAlloc、IsValid、22-30 |
| S72 | [MecanimPeformanceandOptimization](https://docs.unity3d.com/2022.3/Documentation/Manual/MecanimPeformanceandOptimization.html) | Unity 2022.3／本文改訂日不明 |
| S73 | [Physics.RaycastNonAlloc](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Physics.RaycastNonAlloc.html) | Unity 2022.3／本文改訂日不明 |
| S74 | [ai navigation](https://creators.vrchat.com/worlds/udon/ai-navigation/) | 2024-07-24。runtime NavMesh生成可能、custom agent types制約 |
| S75 | [class NavMeshObstacle](https://docs.unity3d.com/ja/2022.3/Manual/class-NavMeshObstacle.html) | Unity 2022.3／本文改訂日不明 |
| S76 | [class SkinnedMeshRenderer](https://docs.unity3d.com/2022.3/Documentation/Manual/class-SkinnedMeshRenderer.html) | Unity 2022.3／本文改訂日不明 |
| S77 | [ModelingOptimizedCharacters](https://docs.unity3d.com/ja/2022.3/Manual/ModelingOptimizedCharacters.html) | Unity 2022.3／本文改訂日不明 |
| S78 | [network stats](https://creators.vrchat.com/worlds/udon/networking/network-stats/) | 2025-08-01 |
| S79 | [unity ui optimization tips](https://unity.com/how-to/unity-ui-optimization-tips) | 現行Unity公式・日付/版明記なし、uGUI一般論。VRChat適用は回帰テスト |
| S80 | [class AudioClip](https://docs.unity3d.com/2022.3/Documentation/Manual/class-AudioClip.html) | Unity 2022.3／本文改訂日不明 |
| S81 | [vrc_spatialaudiosource](https://creators.vrchat.com/worlds/components/vrc_spatialaudiosource/) | 2024-12-10 |
| S82 | [image loading](https://creators.vrchat.com/worlds/udon/image-loading/) | 2026-06-17 |
| S83 | [string loading](https://creators.vrchat.com/worlds/udon/string-loading/) | 2025-04-08 |
| S84 | [Network ID Utility](https://creators.vrchat.com/worlds/udon/networking/network-id-utility/) | 2025-08-11 更新 |
| S85 | [Network Compatibility](https://creators.vrchat.com/worlds/udon/networking/compatibility/) | 2025-11-13 更新 |
| S86 | [Renderer.material](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Renderer-material.html) | Unity 2022.3／本文改訂日不明 |
| S87 | [RenderTexture.Release](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/RenderTexture.Release.html) | Unity 2022.3／本文改訂日不明 |
| S88 | [Particle Collision module](https://docs.unity3d.com/ja/2022.3/Manual/PartSysCollisionModule.html) | Unity 2022.3／本文改訂日不明 |

## 12. 調査の限界

- プロジェクトのシーン・コード・アセットや実機を直接計測していないため、改善率や現在の原因は断定していない。
- lilToon等の第三者shader固有の不具合は、実際の導入版・設定が未提示のため、この共通仕様調査では確定していない。
- PC worldの絶対容量上限、world共通の固定draw call上限・同時音源上限は今回の一次資料から確定せず、架空の数値を置いていない。
- 対応OS／最低機種の全一覧は本チェックリストの対象外。試験端末の選定時にはVRChatの最新サポート情報と自分の提供対象を合わせる。
- Beta機能は正式版の標準手順として扱わない。SDK／clientの更新時に仕様・使用API・計測条件を再確認する。

[S01]: https://creators.vrchat.com/sdk/upgrade/current-unity-version/
[S02]: https://creators.vrchat.com/releases/release-3-10-5/
[S03]: https://docs.vrchat.com/docs/vrchat-202633-open-beta
[S04]: https://ask.vrchat.com/t/developer-update-24-september-2026/48972
[S05]: https://docs.vrchat.com/docs/vrchat-202611
[S06]: https://creators.vrchat.com/worlds/udon/world-debug-views/
[S07]: https://creators.vrchat.com/worlds/clientsim/
[S08]: https://developers.meta.com/horizon/documentation/unity/ts-ovrmetricstool/
[S09]: https://docs.unity3d.com/ja/2022.3/Manual/profiler-profiling-applications.html
[S10]: https://docs.unity3d.com/ja/2022.3/Manual/FrameDebugger.html
[S11]: https://docs.vrchat.com/docs/vrchat-202442
[S12]: https://creators.vrchat.com/worlds/components/textmeshpro/
[S13]: https://creators.vrchat.com/worlds/udon/vrc-graphics/vrc-quality-settings/
[S14]: https://creators.vrchat.com/common-components/physbones/
[S15]: https://creators.vrchat.com/common-components/contacts/
[S16]: https://creators.vrchat.com/worlds/udon/vrctween/
[S17]: https://creators.vrchat.com/releases/release-3-10-0/
[S18]: https://creators.vrchat.com/releases/release-3-10-4/
[S19]: https://creators.vrchat.com/platforms/
[S20]: https://creators.vrchat.com/platforms/iOS/
[S21]: https://creators.vrchat.com/platforms/iOS/setting-up-unity-for-creating-ios-content/
[S22]: https://creators.vrchat.com/platforms/android/android-best-practices/
[S23]: https://creators.vrchat.com/platforms/android/quest-content-optimization/
[S24]: https://creators.vrchat.com/worlds/submitting-a-world-to-be-made-public/
[S25]: https://creators.vrchat.com/platforms/android/quest-content-limitations/
[S26]: https://creators.vrchat.com/worlds/whitelisted-world-components/
[S27]: https://creators.vrchat.com/platforms/android/cross-platform-setup/
[S28]: https://creators.vrchat.com/platforms/android/build-test-mobile/
[S29]: https://creators.vrchat.com/platforms/iOS/build-test-mobile/
[S30]: https://creators.vrchat.com/worlds/udon/vrc-graphics/
[S31]: https://hello.vrchat.com/blog/vrchat-mobile-release
[S32]: https://creators.vrchat.com/worlds/udon/video-players/
[S33]: https://docs.unity3d.com/2022.3/Documentation/Manual/SL-ShaderCompileTargets.html
[S34]: https://docs.unity3d.com/2022.3/Documentation/Manual/SL-PlatformDifferences.html
[S35]: https://docs.unity3d.com/2022.3/Documentation/Manual/class-TextureImporterOverride.html
[S36]: https://creators.vrchat.com/worlds/components/vrc_mirrorreflection/
[S37]: https://creators.vrchat.com/worlds/udon/vrc-graphics/vrc-camera-settings/
[S38]: https://docs.unity3d.com/2022.3/Documentation/Manual/OptimizingGraphicsPerformance.html
[S39]: https://docs.unity3d.com/ja/2022.3/Manual/ProfilerRendering.html
[S40]: https://docs.unity3d.com/2022.3/Documentation/Manual/DrawCallBatching.html
[S41]: https://docs.unity3d.com/ja/2022.3/Manual/static-batching.html
[S42]: https://docs.unity3d.com/2022.3/Documentation/Manual/GPUInstancing.html
[S43]: https://docs.unity3d.com/ja/2022.3/Manual/OcclusionCulling.html
[S44]: https://docs.unity3d.com/ja/2022.3/Manual/occlusion-culling-dynamic-gameobjects.html
[S45]: https://docs.unity3d.com/ja/2022.3/Manual/LevelOfDetail.html
[S46]: https://docs.unity3d.com/ja/2022.3/Manual/class-LODGroup.html
[S47]: https://docs.unity3d.com/ja/2022.3/Manual/terrain-OtherSettings.html
[S48]: https://docs.unity3d.com/ja/2022.3/Manual/RenderTech-ForwardRendering.html
[S49]: https://docs.unity3d.com/ja/2022.3/Manual/RefProbePerformance.html
[S50]: https://docs.unity3d.com/ja/2022.3/Manual/class-RenderTexture.html
[S51]: https://docs.unity3d.com/ja/2022.3/Manual/class-TextureImporterOverride.html
[S52]: https://docs.unity3d.com/ja/2022.3/Manual/texture-type-default.html
[S53]: https://docs.unity3d.com/2022.3/Documentation/Manual/TextureStreaming.html
[S54]: https://docs.unity3d.com/ja/2022.3/Manual/SL-ShaderPerformance.html
[S55]: https://docs.unity3d.com/ja/2022.3/Manual/FBXImporter-Model.html
[S56]: https://docs.unity3d.com/ja/2022.3/Manual/class-SkinnedMeshRenderer.html
[S57]: https://docs.unity3d.com/ja/2022.3/Manual/PartSysMainModule.html
[S58]: https://docs.unity3d.com/ja/2022.3/Manual/PartSysRendererModule.html
[S59]: https://docs.unity3d.com/ja/2022.3/Manual/PartSysInstancing.html
[S60]: https://docs.unity3d.com/ja/2022.3/Manual/LightProbes.html
[S61]: https://creators.vrchat.com/worlds/udon/udonsharp/performance-tips/
[S62]: https://creators.vrchat.com/worlds/udon/networking/network-details/
[S63]: https://creators.vrchat.com/worlds/udon/networking/events/
[S64]: https://creators.vrchat.com/worlds/udon/networking/performance/
[S65]: https://creators.vrchat.com/worlds/udon/networking/network-components/
[S66]: https://creators.vrchat.com/worlds/udon/persistence/player-data/
[S67]: https://creators.vrchat.com/worlds/udon/udonsharp/class-exposure-tree/
[S68]: https://docs.unity3d.com/ja/2022.3/Manual/BestPracticeUnderstandingPerformanceInUnity7.html
[S69]: https://docs.unity3d.com/2022.3/Documentation/Manual/class-Rigidbody.html
[S70]: https://docs.unity3d.com/2022.3/Documentation/Manual/class-MeshCollider.html
[S71]: https://creators.vrchat.com/worlds/layers/
[S72]: https://docs.unity3d.com/2022.3/Documentation/Manual/MecanimPeformanceandOptimization.html
[S73]: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Physics.RaycastNonAlloc.html
[S74]: https://creators.vrchat.com/worlds/udon/ai-navigation/
[S75]: https://docs.unity3d.com/ja/2022.3/Manual/class-NavMeshObstacle.html
[S76]: https://docs.unity3d.com/2022.3/Documentation/Manual/class-SkinnedMeshRenderer.html
[S77]: https://docs.unity3d.com/ja/2022.3/Manual/ModelingOptimizedCharacters.html
[S78]: https://creators.vrchat.com/worlds/udon/networking/network-stats/
[S79]: https://unity.com/how-to/unity-ui-optimization-tips
[S80]: https://docs.unity3d.com/2022.3/Documentation/Manual/class-AudioClip.html
[S81]: https://creators.vrchat.com/worlds/components/vrc_spatialaudiosource/
[S82]: https://creators.vrchat.com/worlds/udon/image-loading/
[S83]: https://creators.vrchat.com/worlds/udon/string-loading/
[S84]: https://creators.vrchat.com/worlds/udon/networking/network-id-utility/
[S85]: https://creators.vrchat.com/worlds/udon/networking/compatibility/
[S86]: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Renderer-material.html
[S87]: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/RenderTexture.Release.html
[S88]: https://docs.unity3d.com/ja/2022.3/Manual/PartSysCollisionModule.html
