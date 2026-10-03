# 犬との自動動画撮影カメラ (IMP-WLD-008) — 設計

2026-10-03 作成 (AI 設計案、ユーザー未確認)。Issue: [IMP-WLD-008](../development/issues/IMP-WLD-008-dog-follow-video-camera.md)。土台は [写真の増量・散歩撮影・メニュー吸着](2026-10-03-photo-expansion.md) と [自動写真の設計・実装記録](2026-10-03-auto-photo-gallery.md)、許可は [地図と移動 §A.4](2026-09-27-map-and-travel.md)。前作 (`C:/work/unity/vcc/KominkaDogRun/`) は読み取りだけで、変更していない。Unity は動かしていない (読取調査と計算だけ)。行番号は 2026-10-03 の作業ツリー (写真の Udon は別の作業が編集中なので、多少ずれることがある)。数値は特に断りがなくても試作値 (Play・実機で決め直す)。

## 0. 決定の一覧 (案)

| # | 決定 (案) | 理由の要点 |
|---|---|---|
| 1 | **描画用の Camera 1 台・ライブ RT 1 枚** を持つ `KH_VideoCamera` を、シーンに 1 組だけ置く (ローカル。同期なし)。撮り方は「モード番号 + モードごとの姿勢の関数」。選んだモードの関数だけを呼ぶ | Issue の確定方針「1 台・1 組を使い回し、選択中の撮り方だけ動かす」 |
| 2 | RT は **1280×720・RGBA8 (sRGB)・深度 24・MSAA なし**。開いたときに作り、× で `Release` + `Destroy`。メモリ 約 7.0 MiB (最悪 10.5 MiB) | 写真の PC 解像度と同じ (KH_PhotoGalleryEditor.cs:84)。閉じている間はメモリも 0 にする |
| 3 | 描く頻度は **30 fps**。**(W1 の最終形: 拍で Camera を有効にし、その Camera の `OnPostRender` で無効にする。拍ごとにちょうど 1 回、普通のカメラの段階で描く。W1 の手直し 2・3 を参照)** 前作 `KD_DogPovCamera` と同じ「1 フレームだけ有効にする」方式。姿勢の計算も描く回だけ | 前作の実績 (KD_DogPovCamera.cs:69・83)。`Camera.Render()` に賭けない (前作 DES-CAM-001:85) |
| 4 | **歩いても VRChat カメラに映し続ける方法は「録画モード」の吸着板 1 枚**。既存の `KD_PhotoPrint` を **変えずに**、吸着距離 100000 m・エイム判定なし・目では見えない、の材質で使う。板のメッシュは境界 (bounds) を大きくした専用の Quad にして、どこを向いても視錐台で間引かれないようにする | シェーダーは「写真カメラの描画のときだけ全画面」を既に持つ (KD_PhotoPrint.shader:100-203)。距離の条件を外せば、どこからでも全画面になる。Udon から VRChat カメラの位置を取る必要がない |
| 5 | モニター (目で見る画面) は **出した場所に固定**、吸着はさせない (ただの Unlit 表示)。録画用の全画面は 4 の板が受け持つ | モニターを動画カメラから外すレイヤーに置ける (再帰の描画を防ぐ)。吸着の経路を 1 本にする |
| 6 | 他人を「追う・見る」撮り方は、**その人の端末が同期する許可の表** (受付箱 `KH_TravelMailbox` に足す) に「撮る人の id と撮影の回の番号」があるときだけ動く。許可の判定は UI ではなく、**他人の位置を読む唯一の関数** (`TryPersonPose`) の中で毎回行う | Issue「画面のボタンだけで許可を判定せず、追従・出力の開始条件でも検査する」 |
| 7 | 申し込み・通知・いいよ / いまはだめ・時間切れ・休止・ブロックは **合流と同じ受付箱と通知板を使い、種類 `KindFilm = 2` を足す**。許可の状態は申し込みの保留枠とは別の表に持つ。いいよでワープしない | Issue「許可の取り方は既存ワープと同じ」「撮影依頼と分かる表示」「撮影を許可してもワープしない」 |
| 8 | 撮影機能は **PC (Windows) のビルドだけ**に入れる。Android / iOS ビルドでは入口・リグを置かない (`#if` と Editor の削り)。Quest / スマホの人も **被写体にはなれる** (通知と取り消しの UI は全端末にある) | Issue「PC 専用」。通知板に端末の分岐が無い (KH_TravelNotice は全端末で同じ。調査 §1.5) |
| 9 | 実装は **MVP (リグ・モニター・録画モード・犬追従・自分の追従) を先に作り、実機で「歩いても映る」を確かめてから**、モードを群ごとに足す。許可は MVP の次、おまかせは最後 | いちばん危ないのは吸着の継続と録画の経路。12 モードを先に作ってから崩れると捨てる量が多い |
| 10 | 独立レビュー (Codex) は **§5 許可の同期 (必須)**、§3 の録画モードの板 (推奨) | 同期・権限 (CLAUDE.md の `vrc-review` 補足) |

## 1. 調査 (読取り)

### 1.1 写真の仕組み (裏山にあるもの)

| 既存 | 場所 | 動画での扱い |
|---|---|---|
| 写真カメラを 1 フレームだけ有効にして共有の RT へ描く | `KD_PhotoCapture._CaptureTo` / `_FinishCapture` (KD_PhotoCapture.cs:73-101) | 同じ「有効 → 次のフレームで無効」の形を 30 fps で回す。カメラは別に 1 台 (写真のカメラは写真が使う) |
| 実行時に RT を作る・寸法を変える・解放する | `new RenderTexture(...)` / `Create` / `Release` / `Destroy` (KD_PhotoSettings.cs:137・154-166・196) | Udon で RT の生成と解放が使えることの根拠。動画の RT も開くときに作り、閉じるときに捨てる |
| 撮影カメラの写すレイヤー (許可リスト) 0 / 8 / 9 / 11 / 13 / 14 / 18 | `PhotoLayers` (KH_PhotoGalleryEditor.cs:117)、必ず外すレイヤー 2 / 4 / 5 / 10 / 12 / 17 / 19 / 22 (:119) | 動画のカメラも同じ mask。自分の顔は MirrorReflection (18) で写る (KD_PhotoDirector.cs:48-49、BUG-CAM-006)。手元メニュー (17) は写らない |
| 地形の遮り `cameraBlockMask` = Default + Environment | KH_PhotoGalleryEditor.cs:122、KD_PhotoDirector.cs:134 | 遮りとめり込みの判定に同じ mask を使う |
| 犬の頭・胴・大きさ | `DogHead` / `DogSubject` / `DogSize` (KD_PhotoDirector.cs:918-949) | 構図の基準に同じ式を使う (private なので、動画側に同じ式を写すか、public の読み口を足す。§2.4) |
| 画面の縦を犬が占める割合から距離を出す | `FrameDistance` (KD_PhotoDirector.cs:955-969) | 追従の距離を犬種によらず決める |
| カメラを壁・床の手前へ出す | `ClearCameraPos` (KD_PhotoDirector.cs:995-1036。庇を床と読まない修正つき) | 遮りの処理の基本。動画では 10 Hz で呼び、結果をなめらかにする |
| 犬目線 (犬の前方の景色) と自撮り風の幾何、前と下の空きの検査 | `WalkDogEye` / `WalkSelfie` (KD_PhotoDirector.HillsideWalk.cs:102-199) | 犬目線モード B (景色) と、自分の追従の向きの選び方に流用 |
| 撫での犬目線 (犬の頭から自分を見上げる) | `ShotDogEye` / `ShotDogEyeOver` (photo-expansion §2.1、KD_PhotoDirector.cs:746-765) | 犬目線モード A (見上げ) の位置の式 |
| 吸着のシェーダー | `KD_PhotoPrint.shader` (裏山の写し: Imported/KominkaDogRun/Shaders/KD_PhotoPrint.shader)。全画面の条件 `isPhotoCamera && !isMirror && camDistance < _AttachDistance && aimOk` (:150)、`_AttachMargin < 0` でエイム判定なし (:145)、`_HideFromEyes` で目には描かない (:255-259) | §3 の録画モードの板にそのまま使う |
| iOS では全画面の中身が黒くなる (BUG-CAM-003) | KH_PhotoAlbum.cs:100-107 (スマホでは吸着を切る) | 撮影は PC だけなので当たらない |

### 1.2 前作のライブの犬目線モニター (`KD_DogPovCamera`、読取りだけ)

- スクリプトは `C:/work/unity/vcc/KominkaDogRun/Assets/KominkaDogRun/Scripts/KD_DogPovCamera.cs` (同期なし、:9)。ビルダー `KD_ActivitiesEditor.cs:177-252`、Verify :264-309。
- RT: PC 512×512 / モバイル 256×256、色 format 4 (R8G8B8A8_SRGB と読める)、深度 format 90 (D16 と読める)、MSAA 1 (RT_KD_DogPov_PC.renderTexture:17-18)。端末で選ぶ `#if UNITY_ANDROID || UNITY_IOS` (KD_DogPovCamera.cs:36-40)。
- カメラ: FOV 80、near 0.01、far 60、HDR・MSAA なし、最初は無効 (KD_ActivitiesEditor.cs:181-182)。mask は写真の mask から UI (5)・InternalUI (19) を外したもの、MirrorReflection を含む (:185)。
- 頻度: `Camera.Render()` は使わず、PostLateUpdate で 1 フレームだけ有効にして次に無効へ戻す (:69・83)。PC 33 ms・モバイル 125 ms ごと。モニターが頭から 10 m 超・視野の外 (前方との内積 < 0.3) なら描かない (:79)。
- モニター: ローカルの VRCPickup の板 (AutoHold、同期なし)、頭の前 0.85 m・下 0.18 m に出す (:50-59)。[×] とメニューの「モニターをしまう」で閉じる (:60-65)。所有・犬が消えたら自動で閉じる (:71-72)。
- 姿勢のなめらかさ: **無し**。頭の骨の位置へ直付け (:80-82)。「走ると激しく揺れる (それが味)」(前作 DES-CAM-001:57)。
- 止め方: `enabled = false` (:62・69・85)。RT はアセットで、解放しない。
- 費用: **実測なし** (前作 IMP-INF-022:98・142-144 は未計測のまま)。
- ライブの RT を吸着シェーダーで出したことは **無い** (画面は Unlit/Texture。前作 DES-CAM-001:55・60 は「全画面化しない」方針)。
- 裏山: スクリプトは取り込み済みだがシーンで使っていない。game-design.md:365 で「常に描き続けるモニター」は対象外 (2026-09-23)。**本件を採用するときは game-design §7 の対象外の行を書き換える** (§10 の W5)。

### 1.3 SDK で決まること

調べた所: `Packages/com.vrchat.worlds` の DLL (文字列) と UdonSharp の源、ClientSim の源。Unity は動かしていない。

| 問い | 分かったこと | 出典 |
|---|---|---|
| (a) VRChat カメラの位置・向き・有効を Udon で読めるか | **読める**。`VRC.SDK3.Rendering.VRCCameraSettings` の静的な `PhotoCamera` / `ScreenCamera` に、読み取りの `Active`・`Position`・`Rotation`・`Forward`・`FieldOfView`・`PixelWidth` / `PixelHeight`・`CameraMode` などがある (Udon の外部関数 `VRCSDK3RenderingVRCCameraSettings.__get_Position__...`)。`CullingMask`・`NearClipPlane` などは読み書き。イベントは `OnVRCCameraSettingsChanged(VRCCameraSettings)` (UdonSharpBehaviour.cs:468) と `OnVRCQualitySettingsChanged` (:469)。「カメラのモードが変わった」専用のイベントは無い | `Runtime/Udon/External/VRC.Udon.VRCWrapperModules.dll`、`Runtime/VRCSDK/Plugins/VRCSDK3.dll`、`Integrations/UdonSharp/Runtime/UdonSharpBehaviour.cs:468-469` |
| | `CameraMode` の値の一覧は読めなかった (列挙の反射が失敗)。DLL に `PhotoOrVideo` / `DroneFPV` / `DroneHandheld` の文字列はあるが、列挙の値かは未確認 | 同上 |
| | **ClientSim は写真カメラを作らない** (`ScreenCamera` のモードを Screen に決め打ち、イベントは画面の解像度が変わったときだけ)。写真カメラの値は実機でしか見られない | `Integrations/ClientSim/Runtime/System/ClientSimMain.cs:339・639-640` |
| | 裏山・前作とも `VRCCameraSettings` を使ったコードは無い (0 件) | grep |
| (a') シェーダーのグローバル値 | `_VRChatCameraMode` は SDK の中に宣言・説明が **無い** (VRChat がランタイムで渡す値)。プロジェクトでは `KD_PhotoPrint` / `KD_PhotoFrame` / `KD_PhotoFrameViewer` / `KH_TravelFade` が使い、「0 = 通常の目線、それ以外 = 写真カメラ等」と書く (KD_PhotoPrint.shader:72)。`_VRChatScreenCameraPos` / `_VRChatPhotoCameraPos` などは SDK にもプロジェクトにも無い | SDK の grep、KD_PhotoPrint.shader:72-73 |
| (b) `Camera.Render()` と「有効にして描く」 | `Camera.Render` は Udon に公開されている (`UnityEngineCamera.__Render__SystemVoid`)。ただし前作・裏山の実行時コードは使っておらず、1 フレームだけ有効にする方式 (KD_PhotoCapture.cs:80-81・90、KD_DogPovCamera.cs:69・85)。`targetTexture`・`cullingMask`・`fieldOfView` は読み書きできる。`RenderTexture` の生成 (`(w,h,depth,format)` など)・`Create`・`Release` も公開。`GetTemporary` は無い | `Runtime/Udon/External/VRC.Udon.Wrapper.dll` |
| (c) 他人のアバター・自分の頭 | ワールドのカメラは Player (9) で他人、MirrorReflection (18) で頭を縮めていない自分を写す (前作の実測 BUG-CAM-006:29-53、KD_PhotoDirector.cs:48-49)。PlayerLocal (10) は VR で頭が縮むので使わない。SDK の源には書かれていない (未確認の層の仕様は前作の実測に頼る) | 前作の記録 |
| (d) ワールドのカメラの禁止事項 | `UnityEngine.Camera` は許可リストにある (WorldValidation.cs:236)。RT の最大寸法・clear flags の強制・`targetTexture = null` のときの扱いは SDK の源に無い。`camera-design.md:44` (共有 Skill) によると VRChat カメラの near は 0.001〜0.05 に丸められる。**このワールドのカメラは必ず `targetTexture` に RT を張ってから有効にする** (画面へ描かない) | `Packages/com.vrchat.base/.../WorldValidation.cs:236`、`.claude/skills/vrc-asset-production/references/camera-design.md:21-22・44` |
| (e) 音 | ワールド側で要るものは無い。VRChat のカメラは映像だけで、音は録画ソフトが PC の音 (VRChat の出力) を録る。撮影カメラの位置の音を作る必要は無い (Issue §音声)。どう入るかは実機の録画で確かめる | — |
| VRCPlayerObject の Owner が移らないこと | SDK の源・DLL の文字列からは **確かめられなかった** (not found)。§5.7 で読む側が Owner を照合して守る | — |
| 性能の相場 | 「RT に描くカメラ 1 台で約 0.9 ms、描画呼び出しが 2/3 ほど増える。VR では両目の上に乗る」「下げ方は効く順に、頻度 15〜30 fps → cullingMask → 影 → farClipPlane → 解像度 → OFF にできること」 | `.claude/skills/vrc-performance/SKILL.md:34・39` |

- **前の記述の更新**: KD_PhotoPrint.shader:8-10 と photo-expansion §3.1 の「VRChat カメラの姿勢は Udon からは取れない」は、吸着を作った当時 (KanjiFind) の前提。SDK 3.10.5 では `VRCCameraSettings.PhotoCamera` で読める。吸着のシェーダーの作り (描く側で解く) はそのまま有効で、本設計も読める値には頼らない (§3.2)。

### 1.4 状態の読み口 (モードの入力)

| 入力 | 場所 | 同期 | 動画での使い道 |
|---|---|---|---|
| 犬を誰がつないでいるか `walkers[dog]` (playerId) | KH_LeashManager.cs:35、`_IsMine` :84-87 | 同期 | 犬追従の「一緒に歩く人」、散歩の撮り方、おまかせの場面判定 |
| 立ち寄りのポーズ中の場所 `PoseSpotIndex` | KD_DogWanderController.HillsideLeash.cs:94 (各端末で位置から出す) | ローカル導出 | 犬目線 B、おまかせの散歩 |
| 立ち寄りの場所と注視点 | `KH_WalkStopovers._Position` / `_LookTarget` / `_Kind` (KH_WalkStopovers.cs:21-35) | 静的 | 犬目線 B の注視点 |
| 写真の場所 (見晴らし・上流の橋・川辺) | `KH_PhotoWalkWatcher.spotPositions` / `spotBackdrops` (KH_PhotoWalkWatcher.cs:35-39)、座標は photo-expansion §12 | 静的 | 通り過ぎるカメラの候補地点 |
| フリスビーの飛行 (閉じた式) | `KH_Frisbee._PositionAt` / `_VelocityAt` (KH_Frisbee.cs:150-179)。打ち出しの事実 `_p0 / _v0 / _t0 / _curve` は **投げた人の端末 (Owner) だけ**が持つ (:105-107・214) | 位置は VRCObjectSync | フリスビー追従・落下地点の待機 |
| 犬の取りの状態 `state` と投げた人 `throwerId` | KH_FrisbeeCatch.cs:399-400、状態の番号 :218-224 (Chase 1 / Jump 2 / JumpCarry 3 / Land 4 / LandCarry 5 / Retrieve 6 / Pickup 7 / Return 8 / Drop 9 / Ready 10 / Water 11-13)、`IsCarrying` :587、`IsAirborne` :590 | 同期 | おかえり、フリスビーの場面判定 |
| 予測した着地点 `_landing` | KH_FrisbeeCatch.Planner.cs:648 (犬の Owner だけ、private) | ローカル | **使わない** (撮る人の端末に無いことが多い)。§4.7 で各自が予測する |
| 撫でている人・ゾーン | `KD_DogFetchController.PetActive` / `PetPlayerId` / `PetZone` (KD_DogFetchController.cs:416-425、同期 `petState` :255) | 同期 | ふれあいアップ |
| おやつを食べている | `KD_DogWanderController.IsTreatBusy` (KD_DogWanderController.cs:863) | 各端末 | ふれあいアップ (食べる瞬間) |
| 洗い場に留められた犬・着いたか・呼んだ人 | `KH_WashStationCall._HeldDog` / `_Arrived` / `_CallerId` / `_AnchorPosition` (KH_WashStationCall.cs:127-136、同期 :76-79) | 同期 | 洗浄の定点・手元アップ |
| 最後にこすった部位・洗っている人 | `KH_DogWash._LastStrokePart` (:1657)、`_WasherPlayerId` (:1576)、`_WasherPosition` (:1646)、部位の骨 `partBones` (:194)、ぶるぶる `_ShakeSerial` (:1678) | 部位はローカル (受信を含む)、洗う人は同期 | 洗浄の手元アップ、洗い終わりの表情 |
| 洗われている | `KD_DogWanderController.IsBeingWashed` (HillsideWash.cs:128) | 各端末 | おまかせの場面判定 |
| 川の水遊び場 | `KH_WaterSpot.entry` / `exits` / `returns` / `_Contains` (KH_WaterSpot.cs:24-26・207・300-302)。場所は「川辺」脇の東岸の曲がり (river-play-and-floating-fetch.md:11・42・125) | 静的 | 川遊びの定点 |
| 洗い場の位置 | 主の洗い場 `KH_WashStation` (42.50, 2.07, 43.00) (wash-station-layout.md:35)。道中を含む全洗い場 `KH_DogWash.washStations` (KH_DogWash.cs:325、手洗いの設計では計 4) | 静的 | 洗浄の定点 |
| 暗転中 (ワープ) | `KH_TravelFade._Busy()` (KH_TravelFade.cs:73) | ローカル | 暗転と明けて 1 s は姿勢を飛ばす (なめらかにしない) |

### 1.5 許可の仕組み (合流・呼ぶ)

- **受付箱は人ごと**: `KH_TravelMailbox` は `VRCPlayerObject` の雛形 1 つから各人に複製され、その人が Owner (KH_TravelMailbox.cs:12、Course/85 = KH_CourseTravelJoinEditor.cs:24・132-136)。同期は Manual (:32)、同期変数は `accepting` 1 本だけ (:35。PlayerData `KH_TravelJoin.accepting` に保存 :51・600-607)。
- **申し込み・返事・取り消しはネットワークイベント**: `ReqJoin(seq)` / `ReqCall(seq)` → 相手の箱の Owner (:120)、`AnsJoin(seq, code)` (:505)、`CancelJoin(seq)` (:736)。どれも `[NetworkCallable(maxEventsPerSecond: 2)]` (:178・259・266・351)。相手の箱は `Networking.GetPlayerObjects` で引く (`_FindBox` :745-757。無ければ「準備中」)。
- **送り主の照合**: どの口も最初に `Networking.IsOwner(gameObject)` (:181・262・269・354)。返事は `NetworkCalling.CallingPlayer.playerId == _outTo && seq == _outSeq` のときだけ採る (:182-183)。申し込みは `CallingPlayer` を送り主にし、送り主ごとの見た最大の seq 以下を捨てる (:290-293、`SeenSeq` / `MarkSeen` :550-571)。取り消しは seq を見たことにする (遅れて着いた申し込みが蘇らない、:362)。
- **種類**: `KindJoin = 0, KindCall = 1` (:52)。入口は共通 (`_Request(targetId, kind)` :104、`Receive` :288) だが、2 種類に決め打ちの所がある (:107・:291 の検査、:120 の入口の選び方、:198 の返事の分岐、通知板の `joinButtons` / `callButtons` と `call` の bool。KH_TravelNotice.cs:120-138)。
- **時間と上限**: 返事 20 s (`AnswerSeconds`、:60・708)、通知を閉じてから 20 s は code 2 (`NoticeGap`、:315)、休止 10 分 (code 3、:403・311)、断り・時間切れの相手へは 30 s 申し込めない (`SameTargetGap`、:113・581-585)、保留は 1 件だけ (2 件目は code 2、:314)、交差は playerId の小さい方が勝つ (:316-325)、ブロックは滞在中だけ黙って code 0 (:405-410・312)。
- **申し込む側の保留枠も 1 つ**: `_outTo >= 0` などのとき新しい申し込みは「自分が忙しい」 (:110)。
- **通知板**: `KH_TravelNotice` はローカル 1 枚 (同期なし、:17)、頭の前 1.1 m・15° 下、3 m 歩くと置き直す (:30・86-111)、音は 5 s に 1 回 (:52)。**端末の分岐なし** (`#if`・`IsUserInVR` なし)。Quest・スマホでも出る。実機の確認は未 (IMP-WLD-001:118・120・164)。
- **残っている穴 RC-08** (IMP-WLD-001:133): 受け手の端末にまだ申し込んだ人の箱が無いと返事が失われ、時間切れになる。撮影でも同じ (害は「時間切れ」で、勝手に許可にはならない)。
- **ハーネス**: `Scripts/Editor/Testing/KH_CourseWalkHarnessEditor.Travel.cs` は ClientSim で 2 人目を出す (`SpawnRemotePlayer`、:595 ほか)。2 人目の Udon は動かないので、返事は `debugJoinLoopback` と `_DebugAnswerNow` / `debugAnswerCode` で作る (:666-667・737・746)。呼ぶの C0-C11 (Call.cs) に休止・ブロック・種類の置き換え・交差・取り消し後の遅い申し込みのケースがある。

### 1.6 端末の見分け方 (今のコード)

- ビルドで分ける: `#if UNITY_ANDROID || UNITY_IOS` (KH_PhotoAlbum.cs:119、KD_DogPovCamera.cs:36、KD_PhotoSettings.cs:51・66 など)。Android は Quest とスマホの両方なので、Quest を除くときは `IsUserInVR()` を足す (KD_PhotoViewer.cs:78-90)。
- VR / Desktop: `IsUserInVR()` (KH_Frisbee.cs:134 など多数)。タッチ: `InputManager.GetLastUsedInputMethod() == VRCInputMethod.Touch` (KD_TreatPouch.cs:1193)。
- `Application.platform` / `UNITY_STANDALONE` は使っていない。
- PC だけにする機能の前例: 距離フェード (「PCのみ」。Android / iOS は入口を押せない、world-ui-spec の settings.distanceFade)。

## 2. 構成 (A)

### 2.1 物の並び (シーン)

```
KH_VideoCamera (GameObject、最初は無効。PC ビルドだけ。Android / iOS ビルドでは Editor が削る)
├── Rig            KH_VideoCamera (UdonSharpBehaviour、BehaviourSyncMode.None)
├── Cam            Camera (enabled = false、targetTexture は実行時に張る)
├── Monitor        出した場所に固定する板 (layer 17 Walkthrough)
│   ├── Screen     Quad 0.80 × 0.45 m、Unlit/Texture の材質 (RT を実行時に張る)
│   ├── Frame      木枠 (既存の写真ボードの材質を流用)
│   └── Panel      World Space Canvas (layer 17 + VRCUiShape)。モード・相手・おまかせ・録画モード・×
└── RecordSnap     録画モードの吸着板 (§3.2)。Quad (bounds を広げたメッシュ)、KD_PhotoPrint の材質
```

- `KH_VideoCamera` の Udon は 1 つ。モードは partial class に分ける: `KH_VideoCamera.cs` (状態・開閉・描く頻度・なめらかさ)、`.Modes.cs` (モードの姿勢関数)、`.Subjects.cs` (犬・人・ディスクの読み口と許可の門)、`.Auto.cs` (おまかせ)、`.Ui.cs` (パネル)。前例: `KD_PhotoDirector` の partial (KD_PhotoDirector.cs:52)。
- 定点はデータだけ: `spotNames[]` / `spotPositions[]` / `spotLooks[]` / `spotFov[]` / `spotKind[]` (洗い場・川・通過) を Editor のビルダーが Transform の子 (人が置く) から書く。写真の場所と同じ持ち方 (KH_PhotoWalkWatcher.cs:35-39)。定点ごとの Camera・RT は作らない。

### 2.2 1 台・1 枚・選択中だけの計算

```
状態: _open (モニターが出ている) / _mode (0..12) / _auto (おまかせ ON) / _target* (犬・人・ディスク)
PostLateUpdate (_open の間だけ。閉じたら Behaviour ごと無効):
  if (_camOn) { cam.enabled = false; _camOn = false; }          // 前の回の 1 フレームを閉じる
  if (Time.time < _next) return;
  _next = Time.time + 1/30
  dt = Time.time - _lastPoseAt
  if (_auto) AutoStep(dt)                                       // 今のカットのモードを決めるだけ (§6)
  ok = PoseFor(_activeMode, dt)                                 // switch でそのモードの関数を 1 つだけ呼ぶ
  if (!ok) { ShowLost(); return; }                              // 描かない (RT は最後の絵のまま)
  Smooth(dt); cam.transform.SetPositionAndRotation(...); cam.fieldOfView = _fov
  cam.enabled = true; _camOn = true
```

- **モードの関数は「欲しい位置・注視点・FOV・なめらかさの時定数」を返すだけ** (フィールドへ書く。U# のユーザー関数は out 引数を使わない)。なめらかさ・Roll の固定・遮りは共通の後処理。
- 選ばれていないモードは何も呼ばれない。探索 (近い犬・飛んでいるディスクを探す) も、そのモードの関数の中で、0.5 s ごとに 1 回だけ。
- 前作と同じく、描かない回は `cam.enabled = false` のまま。RT は最後の絵を保つ。

### 2.3 RT と描く頻度・メモリ

| 項目 | 値 | 理由 |
|---|---|---|
| 寸法 | **1280×720** (試作値) | 写真の PC と同じ。VRChat の写真カメラの出力 (1080p 以上) へ拡大されるが、ライブの絵なので 720p で見る価値がある。1920×1080 は撮影中の描画が 2.25 倍になるので、計測の後に選択肢として足す (§8) |
| 色 | RGBA8 (ARGB32)、sRGB | 写真の RT と同じ (KH_PhotoGalleryEditor.cs:88)。HDR はカメラ側で切る (写真と同じ) |
| 深度 | 24 bit (Unity では D24S8 か D32S8) | 深度なしではシーンが描けない |
| MSAA | 1 (なし) | 前作と同じ。必要なら計測の後に 2 |
| 頻度 | **30 fps** (試作値)。60 は計測の後の選択肢 | Issue の候補値。前作 PC 33 ms と同じ |
| 姿勢の計算 | 描く回だけ (30 Hz)。遮りのレイは 10 Hz | 計算を描く頻度に揃える |

- **メモリ**: 色 1280×720×4 = 3.52 MiB + 深度 3.52 MiB (D24S8) = **7.0 MiB**。最悪 (D32S8 = 8 byte) で **10.5 MiB**。写真 24 枚 (PC 約 95 MiB、photo-expansion §4.1) に比べて 1 割。
- **作る・捨てる**: 開いたとき `new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32)` → `Create()`、失敗したら開かずに「撮影カメラを用意できませんでした」。× で `cam.targetTexture = null` → 材質の `mainTexture = null` → `Release()` → `Destroy()`。写真の設定と同じ API (KD_PhotoSettings.cs:137・154-166)。
- **開閉 10 回で増えないこと**: RT はフィールド 1 つだけで持ち、開くときに前のものが残っていたら先に捨てる。Camera・モニター・吸着板はシーンに 1 組で、作らない (有効・無効だけ)。

### 2.4 犬の幾何の共有

`DogHead` / `DogSubject` / `DogSize` / `ClearCameraPos` は `KD_PhotoDirector` の private。動画からも同じ式を使いたいので、**`KD_PhotoDirector` に読み取りだけの public 関数を 4 つ足す** (`_DogHeadOf(i)` など。裏山の改変として印)。写真の作業と同じファイルなので、写真の作業が落ち着いてから (W1 の最後)。待てない場合は、同じ式を `KH_VideoCamera.Subjects.cs` に写す (どちらにしたかを記録)。

### 2.5 開閉の流れ

- **開く** (`_Open`): PC でないなら何もしない (そもそも入口が無い)。RT を作る → Monitor を「頭の前 1.3 m、目の高さ − 0.25 m、こちら向き」に置き、以後動かさない (Issue の確定) → Camera に RT → Screen の材質に RT → RecordSnap を録画モードの値で有効 (§3.2) → モード = 前回のモード (初回は犬追従、対象は自分の犬のうち最初の 1 匹。いなければ自分) → Behaviour を有効。
- **出し直し**: メニューの入口をもう一度押すと、閉じずにモニターだけを今の目の前へ置き直す (前作の「呼び戻す」と同じ。KD_DogPovCamera.cs:50-59)。
- **閉じる** (`_Close`): モニターの [×] と、メニューの入口の [しまう]。Camera 無効 → RecordSnap 無効 → 撮影中の許可の回を終える (§5.4) → RT を捨てる → Monitor 無効 → Behaviour 無効。
- **自動で閉じる**: 退室 (自然に消える)、ワールドの暗転中は閉じない (追従を飛ばすだけ)。
- **閉じている間**: Camera 0 台・RT 0 枚・Update 0 本。動くのは「メニューの入口の表示」だけ。
- **PC 以外**: `KH_VideoCamera` はビルドに無い (Editor の Android / iOS ビルド前処理で削る。前作の `KD_MobileStripEditor.cs:45` と同じ考え方)。入口のボタンも置かない。ただし **受付箱の撮影の許可の部分 (§5) は全端末にある** (被写体になるため)。

## 3. モニターと吸着 (B)

### 3.1 問題

Issue の要件は「モニターは出した場所に固定」と「歩いて移動した後も録画映像が継続」。既存の吸着 (`KD_PhotoPrint`) は **板から `_AttachDistance` 以内に VRChat カメラがあるときだけ** 全画面になる (KD_PhotoPrint.shader:101・150)。モニターに近づけて吸着させても、カメラを持って歩き去ると (0.8 m を出た瞬間に) 外れる。

### 3.2 比べた案

| 案 | 中身 | 判断 |
|---|---|---|
| (1) 既存の吸着そのまま | モニターの Screen を KD_PhotoPrint にする。吸着 0.8 m | **採らない**。歩くと外れる (要件を満たさない) |
| (2a) 吸着板を VRChat カメラの前へ毎フレーム動かす | `VRCCameraSettings.PhotoCamera.Active` / `Position` / `Forward` (§1.3。SDK 3.10.5 で Udon に公開) を読み、普通の Quad を写真カメラの前 0.3 m へ毎フレーム置く | **代わりとして残す**。API は公開されているが、ClientSim では値が出ない・実機の値は未確認・毎フレームの Udon と順番 (PostLateUpdate と VRChat カメラの描画の前後) に頼る。(2b) は Udon の値に頼らないので、先に試すのは (2b) |
| **(2b) 録画モードの吸着板** | 専用の Quad 1 枚に KD_PhotoPrint の新しい材質 `M_KH_VideoRecordSnap` (`_AttachDistance = 100000`、`_AttachMargin = -1` (エイム判定なし)、`_HideFromEyes = 1`)。メッシュは Editor で作る専用 Quad (頂点は ±0.5 のまま、**bounds だけ 1 辺 4000 m**)、MeshRenderer の `allowOcclusionWhenDynamic = false`、影なし。材質の renderQueue 2001 | **採る**。シェーダーは変えない。VRChat の写真カメラの描画 (`_VRChatCameraMode > 0.5`、鏡でない) のときだけ、どこにいても画面いっぱいに動画の RT を出す。目・鏡・ワールドのカメラ (動画・写真の自動撮影) では `_HideFromEyes` で何も描かない |
| (3) 手持ち・体に付ける小さなモニター | モニターを手やからだに付けて、吸着 0.8 m の中に保つ | Issue の「出した場所に固定」に反する。**(2b) が実機で成り立たないときの代わり**として残す (§3.6) |

### 3.3 録画モードの板がはたらく理由 (シェーダーの読み)

- 全画面の条件は 4 つだけ: 写真カメラ (`_VRChatCameraMode > 0.5`)、鏡でない、距離 < `_AttachDistance`、エイム判定 (KD_PhotoPrint.shader:150)。
  - 距離を 100000 m にすれば、ワールドのどこでも成り立つ。
  - `_AttachMargin < 0` でエイム判定は常に真 (:145)。
- 全画面の頂点は板の位置と無関係に NDC へ置かれる (:156-165)。板がどこにあっても、絵は画面いっぱい・ニアクリップの手前。縦横比は contain (:183-196)、上下反転にも対応 (:199)。
- ただし、**頂点シェーダーが動くのは、その Renderer が写真カメラで「描く物」に選ばれたときだけ**。Unity は Renderer の bounds で視錐台の外を間引く。だから bounds を広げたメッシュにする (1 辺 4000 m の箱はワールド全体を含むので、カメラの視錐台と必ず交わる)。オクルージョンカリングに間引かれないよう、動的オクルージョンを切る。
- レイヤー: RecordSnap は **Default (0)** に置く。VRChat の写真カメラが Default を描くことは、写真ボード (layer 0、KH_PhotoGalleryEditor.Place.cs:239・288) の吸着で確かめ済み (前作で公開済み)。
  - 動画のカメラ・写真の自動撮影カメラも Default を描くが、そのときは `_VRChatCameraMode` が 0 のはず → `_HideFromEyes` で discard → 何も写らない (§3.5 の確認 2)。
- 他の吸着 (写真ボード 0.35 m・メニュー 0.6 m) と重なったとき: どれもニアクリップの同じ深さに描くので、後に描く方が勝つ (ZTest LEqual)。RecordSnap は renderQueue 2001 で後に描くので、録画モード中は動画が勝つ。
- 他の人: RecordSnap はローカルの物 (撮影者の端末でだけ有効)。他の人の VRChat カメラには何も起きない。

### 3.4 操作

- モニターのパネルに **[VRChatカメラに映す: ON / OFF]** (= RecordSnap の有効・無効)。開いたときは **ON**。
- ON の間は、VRChat カメラ (手持ちカメラ・ストリームカメラ・ドローンなど、写真カメラの描画のすべて) を **どこで、どこへ向けても** 動画が画面いっぱいに出る。普通の写真を撮りたいときは OFF にする (パネルに 1 行で書く)。
- Desktop のスクリーンショット (F12 など) でも写真カメラ扱いになる場合は、動画が写る (§3.5 の確認 4)。
- × で閉じると必ず OFF。
- 状態の行に「VRChatカメラ: 出ていません / 映しています」を出す。`VRCCameraSettings.PhotoCamera.Active` を 2 Hz で読むだけ (板の動作には使わない)。あわせて W1 では `PhotoCamera.CullingMask` に Default (0) と Walkthrough (17) が入っているかを 1 回ログに出す (§3.5 の確認 5・IMP-WLD-007 W3 の「VRChat カメラが 17 を写すか」の材料。書き換えはしない)。

### 3.5 PC 実機で確かめること (ユーザーの 5 分テスト)

ClientSim は `_VRChatCameraMode` を再現しない (photo-expansion §9 の W3 と同じ)。**MVP (W1) をアップロードした後、PCVR で 1 回**:

1. 手元メニューから撮影カメラを出す。モニターに犬が動いて映る。
2. VRChat カメラを出し、**モニターから 20 m 歩き離れ、モニターと反対の方を向けて**、画面いっぱいに動画が出るか。体を回して 360° 向けても途切れないか (視錐台の間引きの確認)。
3. その状態で **ストリームカメラ** (VRChat カメラの Stream) にして、デスクトップのウィンドウに動画が出るか。OBS などで録画を 30 秒。音 (ワールドの音・自分の声) が入るか。
4. パネルで [VRChatカメラに映す] を OFF → VRChat カメラが普通の景色に戻るか。ON に戻して出るか。Desktop モードでも 2 と 4 を 1 回 (Desktop のカメラ)。
5. 動画の中に、モニター自身・全画面の板が写り込んでいないか (動画の中で動画が入れ子になっていないか)。鏡があれば、鏡の中で全画面にならないか。

合格: 2・3・5 が成り立つ。**2 が成り立たない (歩くと消える・向きで消える) ときは §3.6 へ。5 が成り立たないときは RecordSnap を写真カメラだけが描くレイヤーへ移す案 (Walkthrough 17 が VRChat カメラに写るかは IMP-WLD-007 W3 の確認で分かる) を試す。**

### 3.6 代わりの案 (2b が成り立たないとき)

- **第 1 の代わり (2a)**: 写真カメラの位置は Udon で読める (§1.3)。RecordSnap を普通の Quad に戻し、`PhotoCamera.Active` の間だけ毎フレーム PostLateUpdate で `Position + Forward × 0.3` へ置く。距離は 100000 のまま。視錐台の間引きはこれで避けられる。2b の検査で「向きで消える」が出たときはこちら。
- **第 2 の代わり (3)**: 「手元モニター」。モニターを左手首 (VR) / 視点の右下 (Desktop) に付け、VRChat カメラは自撮りの持ち方で近づけておく。Issue の「固定」と違うので、ユーザーに相談してから。
- どちらもだめなとき: 録画は「ストリームカメラでモニターを撮る」(全画面にはならない) か、Desktop の画面録画に限る。Issue の完了条件「歩いた後も継続」を満たせないことを記録する。

## 4. 撮り方 12 種 (C)

### 4.1 共通の約束

- **なめらかさ**: 位置は `Vector3.SmoothDamp` (時定数 τp)、向きは注視点を SmoothDamp (τl) してから `Quaternion.LookRotation(look − pos, Vector3.up)`。**Roll は常に 0** (上向きは世界の上)。FOV も τ 0.5 s で寄せる。
- **飛び**: 目標が 1 回で 6 m 以上動いた (ワープ・モード切替・対象変更) ときは、なめらかさを捨てて一度で置く (カット)。暗転中 (`KH_TravelFade._Busy()`) は描かない。
- **遮り・めり込み** (10 Hz): 被写体 → カメラへレイを撃ち (`cameraBlockMask`)、当たったら手前へ寄せる (`ClearCameraPos` と同じ、庇を床と読まない版)。下へもレイを撃ち、地面 + 0.25 m より下へ行かない。**犬の体の中**: カメラと犬の胴の中心の距離が `DogSize × 0.45` 未満なら、犬から外へ押し出す。
- **被写体を見失ったとき**: 3 s は最後の構図のまま (RT は止めない)、その後はパネルに「(犬の名前) が見つかりません」と出して描かない。犬は名簿から消えないので、見失うのは主に「人が退室した」「許可が切れた」「ディスクが無い」。
- **人の位置は 1 つの関数でしか読まない** (`TryPersonPose(playerId)`、§5.5)。自分 (`isLocal`) は常に通り、他人は有効な許可があるときだけ通る。
- **mask**: 写真と同じ (§1.1)。FOV の既定は 50。

| τ | 値 (試作値) |
|---|---|
| 追従の位置 τp | 0.6 s |
| 注視点 τl | 0.25 s |
| ドローン・一周 | 1.2 s / 0.5 s |
| 定点・待機 | 位置は固定、注視点 0.35 s |

### 4.2 一覧

「許可」の列: 他人を位置の基準か注視点に使うときに要る。犬だけ・ディスクだけ・定点の景色なら要らない (背景に写る人は対象外。Issue の確定)。

| # | モード | 位置の基準 | 見る先 | FOV | 入力 (既存) | 許可 |
|---|---|---|---|---|---|---|
| 1 | 犬追従 | 選んだ犬の root。進む向き (速度が 0.3 m/s 以上なら速度の水平方向、未満なら root の前) から 斜め前 40°・距離 `FrameDistance(DogSize, 0.4)` (下限 1.2 m・上限 5 m)、高さ 犬の頭 + 0.35 m | 犬の胴 (`DogSubject`) から進む向きへ 0.5 m 先 | 50 | `dogRoots`、`DogSubject` / `DogSize` | 要らない |
| 2 | ユーザー追従 | 選んだ人の頭。体の向きから 斜め前 35°・2.4 m・頭 − 0.1 m | 頭と「その人につながる犬の中心」を 0.4 : 0.6 で結んだ点 (つながる犬がいなければ胸 = 頭 − 0.35 m) | 55 | `TryPersonPose`、`walkers[]` | 自分は要らない。他人は要る |
| 3 | 犬目線 | A 見上げ (既定): 犬の頭 + (犬の頭 − 見る人の頭 の水平方向) × 0.35 + 上 0.05 (`ShotDogEye` の式)。B 景色: 頭アンカー + 前 × clamp(DogSize×0.25, 0.08, 0.2) + 上 0.04 (`WalkDogEye` の式) | A: 見る人の頭。見る人 = その犬をつないでいる人 (`walkers`)、いなければ自分。B: 立ち寄りの注視点 (`PoseSpotIndex` → `_LookTarget`) か、犬の前方 3° 上 | A 60 / B 70 | `DogHead`、`walkers`、`PoseSpotIndex` | A で見る人が他人なら要る (許可が無ければ B に落とす) |
| 4 | ドローン俯瞰 | 追う点 = 自分 (既定) か選んだ犬。点の後ろ上 (水平 6 m・高さ 7 m、仰角 約 50°)。向きは追う点の進む向きの後ろ。地形の高さ + 4 m より下へ行かない | 追う点 + 進む向きへ 2 m | 60 | `TryPersonPose(自分)`、犬 | 追う点が他人なら要る |
| 5 | スポット定点 | 置いた定点 (洗い場 4・川 2。§4.5) | 定点の注視点。対象 (留められた犬・水の中の犬) があればその犬の胴へ 30% だけ寄せる | 定点ごと | 定点のデータ、`_HeldDog`、`KH_WaterSpot._Contains` | 要らない (人を注視しない) |
| 6 | フリスビー追従 | ディスクの位置の後ろ 2.5 m・上 0.8 m (ディスクの水平速度の逆向き)。**ディスクの回転は使わない** | 追いかける犬 (そのディスクの犬) の胴とディスクの中点。犬が 15 m 以上離れたら犬 | 60 | ディスクの transform、`KH_FrisbeeCatch.State` | 要らない |
| 7 | 落下地点の待機 | 予測した着地点 L から 5 m、投げた向きの横 (犬が来る側) へ、高さ 1.2 m。置いた後は動かない | L + 上 0.5。ディスクが 8 m 以内に来たらディスクと L の中点 | 55 | §4.7 | 要らない |
| 8 | おかえり | 受け取る人 (投げた人 `throwerId`) の前 1.6 m (犬が来る側)・高さ 0.45 m | 戻ってくる犬の頭 | 55 | `State` = Return / WaterExit / Drop、`throwerId` | 受け取る人が他人なら要る (無ければ犬の前 3 m から振り返る形に落とす) |
| 9 | ふれあいアップ | 犬の頭の横 0.55 m・頭 + 0.05 m。撫でる人の側の反対から | 犬の頭と撫でている手の中点 (手が読めないときは頭) | 45 | `PetActive` / `PetPlayerId` / `PetZone`、`IsTreatBusy`、`_ShakeSerial` | 手の持ち主が他人なら要る (無ければ頭だけ) |
| 10 | 洗浄の手元アップ | 最後にこすった部位の骨 (`partBones[_LastStrokePart]`) から、洗う人と反対側 0.6 m・上 0.25 m | 部位の骨 (洗う人の手は読まない) | 45 | `_HeldDog` / `_Arrived`、`_LastStrokePart`、`_WasherPlayerId` (向きだけ) | **要らない案**: 見る先は犬の部位、洗う人の位置は「反対側」を決める向きにだけ使う。ただし洗う人を読むので、§5.5 の門は通す (他人で許可が無いときは、犬の向きから右側に固定) |
| 11 | ゆっくり一周 | 中心 = 自分 (既定) と自分の犬の中心。半径 3.5 m・高さ 1.4 m、1 周 60 s (6°/s) | 中心 + 上 0.6 | 50 | `TryPersonPose`、犬 | 中心が他人なら要る |
| 12 | 通り過ぎる散歩 | 道の脇の撮影地点 (§4.8) に置いて待つ | 近づく人と犬。見る先は **自分の犬** の胴 (人は見ない) | 50 | 撮影地点のデータ、自分の犬の位置 | 自分の犬なら要らない。他人の犬を選ぶときは犬だけを見る (人を読まない) ので要らない |

### 4.3 犬追従・ユーザー追従の細部

- **進む向き**: 速度は 0.2 s ごとの位置の差から出し、τ 0.8 s でならす。止まっている (0.3 m/s 未満が 1 s 続く) ときは向きを固定 (くるくる回らない)。
- **曲がり角**: 進む向きのならしで、カメラは外側をゆっくり回る。向きの変化が 1 s に 90° を超えたら、位置の τp を 1.0 s に伸ばす (振り回されない)。
- **前・横・後ろの構図**: 角度の 40° をパネルの [前] [横] [後ろ] で 40° / 90° / 160° に変えられる (Issue の「前・横・後ろは追従系統内の設定候補」)。**初めは置かない** (W3 で入れるか相談)。
- **人と犬の両方が入るか**: ユーザー追従の見る先は人と犬の間の点。入っているかの判定は自撮り風と同じ計算 (画面の内側 80%、KD_PhotoDirector.HillsideWalk.cs:171-190) で、入っていなければ距離を 1.2 倍まで広げる。それでも入らなければそのまま (人を優先)。

### 4.4 犬目線

- 前作のように頭の骨に直付けしない (Issue)。頭アンカーの位置を τ 0.3 s でならし、向きは root の水平の前方から作る (頭の上下動を入れない)。
- A (見上げ) と B (景色) をパネルの小さな切替で選ぶ。既定 A (Issue の表の構図)。
- B の前 2.5 m の壁・下 1.5 m の地面の検査 (KD_PhotoDirector.HillsideWalk.cs:127-138) で外れたときは、A に切り替えずに「前が見えません」と 1 行出して A の位置へ後ろ 0.5 m 引く (写真と違い、撮らないことはできない)。

### 4.5 スポット定点 (洗い場・川)

- **候補 (現地を Editor で見て決める。座標は推定)**:
  - 洗い場 (主) `wash.main`: 洗い場の棚 (42.50, 2.07, 43.00) の前のすのこ中心 (42.50, 41.80) から、南東へ 2.6 m・高さ 1.5 m (人の立つ東西の余白 (wash-station-layout.md:39-41) と、スポーン側の平らな地面を使う)。注視点 = すのこの中心 + 上 0.4。
  - 道中の洗い場 3 か所 (`KH_DogWash.washStations` の残り): 同じ形の相対位置 (棚の向き基準) を Editor が置き、人が直す。
  - 川 `river.bank`: 水遊び場の岸 (`KH_WaterSpot.entry`) から水の側を見る、岸の上 1.8 m。`river.low`: 浅瀬の出口 (`exits[0]`) の横、水面 + 0.5 m の低い位置。
- 定点は「場所を選ぶ」画面で選ぶ (近い順)。洗い場では、そこに留められた犬 (`_HeldDog`、着いた `_Arrived`) がいれば注視点を犬の胴へ 30% 寄せる。川では `_Contains(犬の位置)` の犬のうち、いちばん近い 1 匹。
- 定点の Camera は 1 台 (定点ごとに描かない)。定点を変えるとカット。

### 4.6 フリスビー追従

- **どのディスク**: ディスクは犬ごとに 1 枚 (6 枚。KH_FrisbeeCatch.dogIndex)。パネルでは「犬」を選ぶ = その犬のディスク。既定は「自分が最後に投げたディスク」(投げた人 `throwerId` が自分になった最後の犬)。
- **飛んでいるか**: 撮る人の端末では `IsFlying` は Owner (投げた人) でしか正しくない (KH_Frisbee.cs:94・115)。そこで「その犬の `State` が Chase / Jump / Land (1・2・4) か、ディスクの速さが 2 m/s 以上」を飛行中とする。
- **位置**: ディスクの位置 (VRCObjectSync で各自に届く) の、水平速度の逆向き 2.5 m・上 0.8 m。速度は 0.1 s の差から出し τ 0.3 s でならす。ディスクの回転は読まない。
- **見る先**: 犬の胴とディスクの中点。犬が画面に入らないほど離れたら (15 m 以上)、犬だけを見る。キャッチ (`JumpCarry` / `LandCarry` / `Pickup`) の後は、犬の後ろ上へ 1.5 s で移ってから、手動のときはその犬の犬追従と同じ構図で待つ。
- **飛んでいないとき**: ディスクを持っている人がいる (`HolderPlayer`) なら、そのディスクの犬を犬追従の構図で撮る (人は見ない)。

### 4.7 落下地点の待機 (予測の方法)

- **自分が投げたとき** (多い場合): 投げた人の端末には打ち出しの事実がある (`_p0 / _v0 / _t0 / _curve`、KH_Frisbee.cs:105-107・214)。`KH_Frisbee` に **読み取りだけの口 `FlightP0` / `FlightV0` / `FlightT0` / `FlightCurveNow` / `FlightLocal` (自分が Owner で飛行中)** を足し (同期は増えない)、照準の線と同じ刻み (0.08 s、`aimStep`、:74・408) で `_PositionAt` を進め、`obstacleMask` に当たった所か 8 s を L とする。照準の線と同じ計算なので、投げる前に見えていた着地の印とほぼ一致する。
- **他の人が投げたとき**: 打ち出しの事実は届かない (犬の Owner には `ReqCatch` で届くが、撮る人とは限らない)。飛び始めて 0.25 s の位置 2 つから速度を出し、今の位置と速度を `_PositionAt(p, v, 0, t)` に入れて同じ刻みで進める。揚力の項は最初の水平速度を使う式なので、途中から始めると少しずれる (Issue の「正確な予測は保証しない」の範囲)。
- **置き直し**: 置いた後は動かない。ただし 0.5 s ごとに予測し直し、L が 5 m 以上ずれたら 1 回だけ置き直す (カット)。2 回目以降は動かない。
- **場面**: 投げる前 (誰かがディスクを持っている) は、持っている人の向いている先 12 m を仮の L として置く……のは人の向きを読むので、**自分が持っているときだけ**。他人が持っているときは、そのディスクの犬の追従で待つ。キャッチ・着地の後 2.5 s は同じ場所で撮り、手動ならそのまま待つ (次の投げで置き直す)。
- **キャッチが着地より前**: 犬が空中で取ったら (`JumpCarry`)、その場面がちょうど画面にあるはず (L 付近を狙っているので)。取った場所が L から 6 m 以上離れていたら、注視点だけを犬へ寄せる (位置は動かさない)。
- **複数のディスク**: 選んだ 1 枚だけを見る。おまかせでは §6 の「自分が投げたディスクを優先」。

### 4.8 通り過ぎる散歩カメラ

- **撮影地点 (候補)**: 写真の場所 3 か所 (`viewpoint` 見晴らしのベンチ (80, 19.0, 108)、`bridgeUp` 上流の橋の着地点 (403.5, 21.3, 502.3)、`river` 川辺 (499.3, 9.7, 279.3)、photo-expansion §12) と、立ち寄り場所のうち木陰の 2 か所 (Editor で選ぶ)。地点ごとに「道のどちら側に立つか」「高さ」を Editor が置く。
- **待ち構え方**: 散歩のルート (`KH_WalkRoute.nodePositions` / `loopDistances`) 上で、自分 (または選んだ犬) より **前方 25〜120 m** にある地点のうち、いちばん近いものを選ぶ (0.5 s ごと)。カメラはそこに置き、近づく犬を見る。
  - 近づく: 犬が 25 m 以内に入ったら注視を始める (それまでは道の先を見る)。
  - 通る: いちばん近づいた後、15 m 遠ざかるか、通過から 8 s で「終わり」。次の地点へカット。
  - **あきらめ**: 選んだ地点に 60 s たっても 25 m 以内に来ない (道を外れた・戻った)、または犬が地点から 150 m より遠くへ離れていく、ときは次の候補へ。候補が無ければ犬追従に落とす。
- 見る先は犬の胴。人は背景 (許可は要らない)。

### 4.9 ゆっくり一周

- 中心は 2 秒の平均で固定気味に (τ 2 s)。人が座っている (Station) ときも同じ。
- 半径の円上で、遮り (レイ) に当たった角度は半径を縮めて (下限 1.5 m) 通り抜ける。下限でも当たるなら、その角度の範囲を飛ばす (180° まで)。
- 1 周 60 s。地面の高さに沿って高さを保つ (下へ 1 本のレイ)。

### 4.10 ふれあい・洗浄の手元

- ふれあいは、撫でが 1.0 s 続いたら (`PetActive`)。おやつを食べる (`IsTreatBusy`) ときは口元 (頭アンカー + 前 0.1 m) を見る。洗い終わりのぶるぶる (`_ShakeSerial` が変わった) から 3 s は、犬の正面 1.2 m から顔を撮る。
- 手元アップの部位は 0.5 s ごとに読み直し、部位が変わったら注視点を τ 0.4 s で移す (カットしない)。部位がまだ無い (`-1`) ときは犬の胴。

## 5. 他人を撮る許可 (E) — Codex レビュー必須

### 5.1 方針

- 合流の **受付箱 (`KH_TravelMailbox`) と通知板 (`KH_TravelNotice`) を使い、種類 `KindFilm = 2` を足す**。申し込みの送り方・送り主の照合・seq・保留 1 件・通知の間隔・休止・ブロック・交差は合流と同じ処理を通る。
- **許可そのもの (撮ってよいか) は、申し込みの保留枠とは別の同期された表に持つ**。返事のイベント (`AnsJoin` と同じ形) は UI の表示のためだけで、撮影を始める条件には使わない。
- **撮影を始める・続ける条件は、撮る人の端末が毎回「被写体の箱の同期された表」を読んで決める**。被写体の箱は被写体だけが書ける (VRCPlayerObject の Owner は本人。§5.7 で確認)。
- いいよで **ワープしない** (撮影の種類は `FinishAccept` などの合流の処理を通らない)。合流の許可と撮影の許可は別の状態で、互いに流用しない。

### 5.2 同期するもの (受付箱に足す。どれも Owner = 本人)

| 変数 | 型 | 意味 | 書く時 |
|---|---|---|---|
| `filmAccepting` | bool (既定 true) | 撮影の申し込みを受けるか (合流の `accepting` とは別)。PlayerData `KH_TravelJoin.filmAccepting` に保存 | 設定の切り替え |
| `filmGrantIds` | int[4] (既定 -1) | **撮ってよいと許した相手** (撮る人の playerId) | いいよ / 取り消し / 相手の退室 |
| `filmGrantSeqs` | int[4] | その許可がどの申し込み (撮る人の seq) に対するものか | 同上 |
| `filmTargetId` | int (既定 -1) | **自分が今撮っている他人** (撮る人として。いなければ -1) | 撮影の回の開始・終了 |
| `filmSeq` | int | 今の撮影の回の番号 (= その回の申し込みの seq) | 同上 |

- 1 人が同時に許せる撮る人は 4 人まで (5 人目は code 2「いまは受けられない」)。
- 増える同期は箱 1 つあたり bool 1 + int 10。送るのは変わったときだけ (`RequestSerialization`)。

### 5.3 申し込み〜許可の流れ

撮る人 F、被写体 S。

1. F がパネルで「人を選ぶ」→ S を選ぶ (インスタンスの一覧。§7.3)。S が自分なら許可は要らない (ここで終わり)。
2. F の端末: `_Request(S, KindFilm)`。条件は合流と同じ (保留中でない・相手の箱がある・相手の `filmAccepting`・30 s の再申請待ち) + 「撮影カメラが開いている」。`_outSeq` を進め、S の箱へ `ReqFilm(seq)` を送る (`NetworkCallable(maxEventsPerSecond: 2)`)。F の画面は「(S) さんの返事を待っています (20)」+ [取り消す]。
3. S の端末 (`ReqFilm`): `IsOwner` → `Receive(CallingPlayer, seq, KindFilm)`。合流と同じ検査 (自分・不正・古い seq・ブロック・休止・通知の間隔・保留 1 件) に加え、`filmAccepting` が false なら code 3、許可の表が満杯なら code 2。通れば通知板を「撮影」の形で出す (§7.4)。
4. S が [いいよ] (0.5 s の入力の守り、世代・種類の一致。KH_TravelMailbox.cs:413-419 の `Accepts`): S の箱が **許可の表に (F, seq) を書き** (同じ F の古い行は置き換え)、`RequestSerialization`。その後 `AnsJoin(seq, 1)` を F へ送る (表示用)。
5. S が [いまはだめ] / 20 s の時間切れ: 表は書かない。`AnsJoin(seq, 0)` (合流と同じ)。F は「今は撮影できないようです」、30 s はその人へ申し込めない。
6. F の端末: `AnsJoin` は `CallingPlayer == S && seq == _outSeq` のときだけ表示に使う (既存 :182-183)。code 1 を受けたら **撮影の回を (S, seq) で開き**、自分の箱の `filmTargetId = S`、`filmSeq = seq` を書いて送る。
7. **撮影の開始・継続の条件 (F の端末、描く回ごと = 30 Hz)**: `TryPersonPose(S)` が §5.5 の検査を通ること。通らなければ、その回は人を読まない (落とし先の構図か「許可がありません」)。

### 5.4 終わり方・取り消し

| 出来事 | 誰が | 何をする | 撮影が止まる理由 |
|---|---|---|---|
| S が取り消す (手元メニュー・撮影中の札) | S | 表から F の行を消す → 送る。`FilmRevoked(seq)` を F へ (表示用) | F の端末が次の回で表に (F, seq) が無いのを見る |
| F が × で閉じる・対象を変える・人以外のモードへ移って 60 s | F | 撮影の回を終える (`filmTargetId = -1`、送る)。`EndFilm(seq)` を S の箱へ | F の回の番号が無くなる (古い許可は seq が合わず使えない) |
| S が退室 | — | S の箱が消える | F の `_FindBox(S)` が null → 撮らない |
| F が退室 | S の端末 | `OnPlayerLeft(F)` で表から F の行を消す | (F はもういない) |
| S の受信 (`EndFilm`) | S | `CallingPlayer == F` かつ表の seq と一致する行だけ消す | — |
| 届かない `EndFilm` | S | 表の行が残っても、F の箱の `filmTargetId / filmSeq` が (S, seq) でなければ、S の「撮影中」の表示は出さない (§5.6)。行は F の退室か、S が見たときに消す (S の端末が 10 s ごとに「F の箱が (S, seq) を撮っていない行」を掃除する) | — |

- **同じ回の再開はしない**: F が閉じて開き直したら、新しい申し込みが要る (seq が変わる)。許可は「撮る人・被写体・その回」に限る (Issue の相談案)。
- **古い返事・再入室**: 返事は seq の一致でしか採らない。再入室した人は playerId が変わるので、前の許可の行には当たらない。F が S を撮る許可は S の箱の表にしか無く、F の端末の中に「許可された」という覚えを持たない (覚えていても、毎回表を読むので効かない)。
- **途中参加**: 途中から入った人には、各箱の同期された表がそのまま届く。途中参加者自身が撮る人・被写体になるのは、新しい申し込みから。

### 5.5 撮る人の端末の門 (`TryPersonPose`) — 唯一の入口

他人の位置・頭・手を読むコードは、すべてこの関数を通す (他のファイルで `GetPlayerById(...).GetPosition` / `GetTrackingData` を他人に使わない。Verify がソースを grep して検査する)。

```
bool TryPersonPose(int pid):
  p = VRCPlayerApi.GetPlayerById(pid); if (!Utilities.IsValid(p)) return false
  if (p.isLocal) return true                                    // 自分は許可不要
  if (!_filmOpen || _filmTarget != pid) return false            // 今の回の相手でない
  box = _FindBox(p); if (box == null) return false              // 相手の箱が無い (退室・準備中)
  o = Networking.GetOwner(box.gameObject)
  if (!Utilities.IsValid(o) || o.playerId != pid) return false  // 表を書いたのが本人であること (§5.7)
  me = LocalPlayer.playerId
  for k in 0..3: if (box.filmGrantIds[k] == me && box.filmGrantSeqs[k] == _filmSeq) → 通る
  return false
```

- `_filmTarget` / `_filmSeq` は F の端末のローカル (§5.3 の 6 で、自分の申し込みの code 1 を受けたときだけ書く)。表の読みは同期された値なので、S の取り消しは S の送信が届いた時点 (通常 1 s 以内) で効く。
- 許可が無いときの落とし先は、各モードが「人を読まない構図」(§4.2 の各行) を持つ。おまかせは、許可の無い人のカットを選ばない (§6.3)。
- **モニターのプレビューも同じ門を通る** (許可前に相手の追従の絵を出さない。Issue の完了条件)。

### 5.6 被写体の側の表示

- いいよの直後、通知板は「(F) さんが撮影中です。いつでも止められます」と 4 s 出して閉じる。
- 手元メニューのさんぽタブの上段 (今の「申し込み 1 件」と同じ場所) に、**撮影されている間ずっと「撮影中 (F)」[やめてもらう]** を出す。条件は「自分の表に (F, seq) があり、かつ F の箱の `filmTargetId == 自分 && filmSeq == seq`」(両方が同期された値)。
- 「撮影中」の印をアバターの近くに出すことはしない (他の人に見せる必要がない。ユーザーに確認したいなら §11 の質問 6)。

### 5.7 送り主の照合と Owner の決まり

- `ReqFilm` / `EndFilm` / `FilmRevoked` (と既存 `AnsJoin`) は `[NetworkCallable(maxEventsPerSecond: 2)]`、最初に `Networking.IsOwner(gameObject)` を見る。送り主は `NetworkCalling.CallingPlayer` だけを信じる (引数に playerId を入れない)。
- 同期変数を書くのは箱の Owner (本人) だけ。VRCPlayerObject の Owner は本人のまま移らない、というのが VRChat の Player Object の考え方だが、**SDK の源では確かめられなかった** (§1.3)。もし他人が `SetOwner` できる経路があると、許可の表を偽造できる。そこで表を読む側 (F) は **`Networking.GetOwner(box.gameObject).playerId == S` も毎回見る** (§5.5 の門に入れる。1 行で済むので必ず入れる)。受付箱の側も、`OnOwnershipRequest` があれば拒否を返す (W4 で SDK の公開を確かめてから)。
- 連打: `NetworkCallable` の 2 回/s と、受け手の保留 1 件・通知の間隔 20 s・同じ相手へ 30 s。撮影でも同じ値。
- 交差 (F が S に撮影、S が F に合流を同時に): 既存の交差の処理 (playerId の小さい方が勝つ、:316-325) は種類を問わない。撮影も同じ扱いでよい。
- 種類の置き換え (同じ相手から合流 → 撮影): 既存の「別の種類の申し込みは世代を進めて置き換える」(:304-307) に従う。

### 5.8 既存のコードで変える所

- `KH_TravelMailbox`: `KindFilm = 2`、検査 :107・:291 を 3 種類に、`ReqFilm` の入口、`HandleAnswer` (:198) の分岐に撮影 (いいよで表に書くのは受け手側、受けた F は回を開くだけ)、`FinishAccept` を撮影では呼ばない、新しい同期変数 (§5.2)、`OnPlayerLeft` の掃除、`filmAccepting` の保存。**partial にして `KH_TravelMailbox.Film.cs` へ分ける**。
- `KH_TravelNotice`: 3 つめのボタンの組 `filmButtons` ([いいよ] [いまはだめ]) と見出しの文。`call` の bool を種類の int に。
- `KD_TreatPouch.Travel.cs`: さんぽタブ上段の「撮影中 (F) [やめてもらう]」、設定の「撮影の申し込みを受ける」。
- 合流・呼ぶの既存のハーネス (T・C の各ケース) が通ることを、撮影を足した後にも確かめる。

### 5.9 Quest・スマホの人は被写体になれるか

**推奨: なれる。** 通知板と手元メニューは全端末にあり (§1.5)、受付箱も全端末にある。撮影は PC の人だけが始める。被写体になるのに要るのは「通知に答える」と「取り消す」だけで、どちらも既存の UI の延長。ただし Quest・スマホの通知板の実機確認は合流でもまだ (IMP-WLD-001)。撮影の実機確認 (§9.3) に Quest 1 台を入れる。

## 6. おまかせ撮影 (D)

### 6.1 形

小さな状態機械を 0.5 s ごとに 1 回だけ回す (`AutoStep`)。結果は「今のカットのモードと対象」だけで、描くのはいつも 1 カット (Issue)。

```
場面 (scene) を決める → 場面ごとのカットの表から次のカットを選ぶ → 最短の長さが過ぎるまでは切り替えない
```

### 6.2 場面の判定 (優先の順)

| 場面 | 条件 (既存の状態) |
|---|---|
| フリスビー | 自分が投げたディスク、または自分が選んだ犬のディスクの犬の `State` が Idle / Ready 以外 (`IsEngaged`、KH_FrisbeeCatch.cs:593)、または自分がディスクを持っている |
| 洗う | 自分が呼んだ犬 (`_CallerId == 自分`) が洗い場に着いている (`_Arrived`)、または自分の近く 4 m で `IsBeingWashed` の犬 |
| ふれあい | 自分が撫でている (`PetPlayerId == 自分`) か、自分の犬がおやつを食べている |
| 散歩 | 自分につながる犬が 1 匹以上 (`_IsMine`) |
| 何もない | 上のどれでもない |

- 場面が変わったら、今のカットが最短の長さを過ぎていればすぐ、過ぎていなければ過ぎてから切り替える。フリスビーの投げた瞬間だけは最短を無視して切り替える (飛ぶ場面を逃さない)。

### 6.3 カットの表 (試作値)

| 場面 | 順 | 長さ (最短〜最長) |
|---|---|---|
| フリスビー | 投げる人 (自分のユーザー追従。投げた人が他人で許可が無ければ飛ばす) → 投げたらフリスビー追従 → ディスクが落ち始めたら (縦の速度 < 0 かつ L まで 8 m) 落下地点の待機 → キャッチ・着地の後に犬が戻り始めたら (Return) おかえり → 受け渡し (Drop) の 2 s 後に投げる人へ戻る | 投げる人 3〜20 s、追従は飛行中ずっと、待機 着地後 2.5 s まで、おかえり Return の間ずっと |
| 散歩 | 犬追従 (自分の犬) → 通り過ぎる (地点が前方 25〜120 m にあるとき) → 犬目線 B (立ち寄りでポーズに入ったとき優先) → ユーザー追従 (自分) → ドローン俯瞰 → 犬追従…… | 各 12〜40 s。通り過ぎるは通過まで (最長 60 s) |
| 洗う | 洗い場の定点 → 手元アップ (こすりが 2 s 続いたら) → 定点 → ぶるぶるで ふれあい (犬の顔、3 s) | 定点 8〜30 s、手元 6〜20 s |
| ふれあい | ふれあいアップ → ゆっくり一周 (撫でが 20 s を超えたら、自分が中心) | アップ 8〜30 s、一周 20〜60 s |
| 何もない | 自分の追従 (中心は自分) → 30 s 何もなければゆっくり一周 → さらに 60 s で、自分の犬・選んだ犬がいれば犬追従、いなければ一周を続ける | — |

- **許可の無い人へ切り替えない**: カットを選ぶとき、そのカットが他人を読むなら `TryPersonPose` の結果で判定し、通らなければ表の次へ。おまかせは自分で申し込みを出さない (申し込みは人を選ぶ操作でだけ出る)。
- **撮れなかったとき**: カットの関数が false を 2 回続けて返したら次のカットへ。全部だめなら「何もない」の表へ。
- **切り替え方**: いつもカット (なめらかにつながない)。ただし同じモードで対象だけ変わるときは τ を 2 倍にしてつなぐ。
- **手動の選択との関係**: おまかせ ON の間はモードの一覧が「今のカット」を光らせるだけになる。モードを押すとおまかせは OFF になり、押したモードで固定。

## 7. UI (F)

### 7.1 入口

- **ヘッダーの [写真] で開く写真の面に、[撮影カメラ] のボタンを 1 つ足す** (PC のビルドだけ。Android / iOS のビルドではボタン自体を置かない)。写真と動画は同じ「撮る」話なので、同じ面に置く。タブは増やさない (タブは 7 で決定済み、world-ui-spec §4)。
- 開いている間、同じボタンは [モニターを呼ぶ] と [しまう] の 2 つになる。
- 他の案: 「あそぶ」タブ。フリスビー・集合と同じ並びになるが、写真の面のほうが探しやすいと考える (§11 の質問 2)。

### 7.2 モニターのパネル (幅 0.80 m の画面の下、0.80 × 0.36 m)

```
┌──────────────── 動画 (0.80 × 0.45) ────────────────┐   [×]
└────────────────────────────────────────────────┘
 [おまかせ: ON/OFF]  [VRChatカメラに映す: ON/OFF]   状態の 1 行 (撮影中・待っています・許可がありません)
 [犬追従][人追従][犬目線][ドローン][定点][ディスク]
 [落下待ち][おかえり][ふれあい][手元][一周][通り過ぎ]
 対象: [犬 ◀ コーギー ▶]  [人: じぶん ▼]  [定点 ◀ 洗い場 ▶]   (モードに要るものだけ出す)
```

- 12 のモードを 2 段 × 6 のボタン (各 120 × 44 px 相当)。選択中は点灯色 + 下線 (world-ui-spec の「選択中」)。そのモードで今は撮れない理由 (例: ふれあい「撫でていません」) は状態の行に出し、ボタンは押せるまま (押すと待つ構図になる)。
- **犬を選ぶ**: ◀ ▶ で 6 匹を回す。既定は自分につながる犬、いなければ最後に選んだ犬。
- **人を選ぶ**: [人: じぶん ▼] を押すと、合流のプレイヤー一覧と同じ並び (開いた時点の距離順で凍結、playerId で持つ。map-and-travel §A.4) の小さな一覧。他人を押すと §5.3 の申し込み。
- **ディスクを選ぶ**: 犬を選ぶのと同じ (ディスクは犬ごと)。既定は「自分が最後に投げたディスク」。
- **定点を選ぶ**: ◀ ▶ で近い順。
- **×**: 前作と同じく目で見える × と、押すための透明な面 (前作 IMP-CAM-008、KD_ActivitiesEditor.cs:225-251)。

### 7.3 文言 (ja) と 13 言語

| キー | ja |
|---|---|
| `video.open` | 撮影カメラ |
| `video.recall` / `video.close` | モニターを呼ぶ / しまう |
| `video.auto` | おまかせ |
| `video.snap` | VRChatカメラに映す |
| `video.snap.note` | ON の間は VRChatカメラがこの映像になります。普通の写真は OFF にしてから |
| `video.mode.dog` … `video.mode.passby` | 犬追従 / 人追従 / 犬目線 / ドローン / 定点 / ディスク / 落下待ち / おかえり / ふれあい / 手元 / 一周 / 通り過ぎ |
| `video.lost` | {0} が見つかりません |
| `video.wait.permit` | {0} さんの返事を待っています ({1}) |
| `video.no.permit` | {0} さんの許可がないので、犬だけを撮っています |
| `film.notice` | {0} さんが撮影カメラであなたを撮りたいそうです (映像は {0} さんの画面だけに出ます) |
| `film.notice.yes` / `film.notice.no` | いいよ / いまはだめ |
| `film.active` | 撮影中 ({0}) |
| `film.stop` | やめてもらう |
| `film.setting` | 撮影の申し込みを受ける |

- 13 言語は既存の `UITranslations.json` に足す (写真の W4 と同じ作り方)。字形は Photo/1 と同じく焼く。**文言の翻訳は W5 にまとめる** (W1〜W4 は ja だけで出し、他の言語は ja を出す)。
- 被写体の側の文言 (`film.*`) は全端末で出る (Quest・スマホにも)。

### 7.4 通知板 (撮影の形)

- 見出し: `film.notice`。ボタン [いいよ] [いまはだめ]、[しばらく受けない (10 分)]、[この人からは受けない] (合流と同じ)。20 s の秒読み、音 1 回。置き方は合流と同じ (頭の前 1.1 m・15° 下、3 m で置き直す)。
- 合流と見た目で区別できるように、見出しの左にカメラの形の小さな絵 (既存の写真ボタンの形を流用) を出す。

## 8. 性能と容量 (G)

| 状態 | 費用 |
|---|---|
| 撮影カメラを使っていない人 (全端末) | Camera 0・RT 0・Update 0。受付箱の同期が bool 1 + int 10 増える (変わったときだけ送る) |
| 閉じている (PC) | 同上。入口のボタンの表示だけ |
| 撮影中 (PC) | **シーンをもう 1 回、1280×720 で 30 fps 描く** (視点・画角しだい。ドローン・一周は見える範囲が広い)。姿勢の計算は 30 Hz、レイは 10 Hz で 3〜6 本。おまかせの判定は 2 Hz。録画モードの板は VRChat カメラの描画で全画面の 1 枚を描くだけ (むしろ VRChat カメラの描画の上に 1 枚乗る) |
| メモリ | RT 7.0 MiB (最悪 10.5 MiB)。閉じると 0 |
| ビルド容量 | コード・材質・専用 Quad のメッシュ・UI。新しい画像・音・フォントは作らない (カメラの絵は既存の形)。増分は小さい見込みだが、計測で確かめる |

- **前作の数字が無い**: 前作の 512² のライブモニターも費用は未計測 (§1.2)。`vrc-performance` の相場「RT カメラ常時描画で約 0.9 ms」(photo-expansion §6.3) は目安。720p・30 fps では、目の描画 (PCVR 2 眼) に比べて 1 枚あたりの画素数は小さいが、影・アバター・木の数は同じだけ描く。**PC で計測するまで重さは言わない。**
- **計測の段取り (PC、W1 の後にユーザーと)**:
  1. 撮影 OFF・同じ場所・同じ向きで、`KH_PerfProbe` (既存) か VRChat のデバッグ表示でフレーム時間を 30 s。
  2. 撮影 ON (犬追従 / ドローン / 一周) で同じく 30 s ずつ。
  3. 2 にストリームカメラ + 録画ソフトを足して 30 s。
  4. 閉じる → 1 と同じ値に戻るか。開閉 10 回の後のメモリ (VRChat のデバッグ表示 or プロファイラ)。
  - 記録: 端末 (GPU)・HMD の解像度・場所・各フレーム時間。60 fps / 1080p を選択肢に足すかはこの結果で決める。
- **Quest / iOS で動かないことの確認**: Android / iOS ビルドで `KH_VideoCamera` が無いこと (Editor の検査で、ビルド前処理の後のシーンを数える)、入口のボタンが無いこと。実機では手元メニューにボタンが無いのを見るだけ。

## 9. 確かめ方 (H)

### 9.1 Edit (AI が回せる)

- `unity-compile.sh --udon` (コンパイル)。
- `Video/2 Verify` (新規): 配線、レイヤー (Monitor = 17・RecordSnap = 0・Camera の mask = 写真と同じ)、材質の値 (`_AttachDistance` 100000・`_AttachMargin` < 0・`_HideFromEyes` 1・`_DebugProbe` 0・renderQueue 2001)、RecordSnap のメッシュの bounds (≥ 2000 m)・`allowOcclusionWhenDynamic` false、モニターの [×] の押せる面、定点のデータの長さと NavMesh・地面の検査、**他人の位置を読む呼び出しが `TryPersonPose` の外に無いこと (ソースの grep)**、Android / iOS のビルド前処理の後に `KH_VideoCamera` が残らないこと (前処理の関数を Edit で呼んで数える)。
- Edit の検査で分かるのは配線・値・ソースの形まで。吸着の見え方・録画・構図は分からない。

### 9.2 ClientSim ハーネス (最小。動画の群だけ)

`bash scripts/kh-harness-run.sh video --cases=...` (新しい群。全体の既定にしない、`vrc-verification` §7)。

| ケース | 確かめること | 波 |
|---|---|---|
| V1-lifecycle | 開く → 2 s → 閉じる を 10 回。毎回、Camera が閉じた後に無効、RT のフィールドが null、モニター・吸着板が無効、Behaviour が無効。開いている間の描く回が 1 s あたり 28〜32 回 | W1 |
| V2-pose-dog | 犬 1 匹を直線で 3 m/s 動かす (Transform を書く)。犬追従のカメラが犬から 1.2〜5 m、犬が画面の内側 80% (計算で判定)、Roll = 0、1 フレームの位置の変化が 0.5 m 以下 | W1 |
| V3-pose-self | 自分を動かし、ユーザー追従の注視点が頭と犬の間にある | W1 |
| V4-modes | モードごとに、模擬の被写体 (犬・ディスクの位置の列) で姿勢の関数を呼び、位置が地面の上・遮られていない・犬の体の中でない | W2-W3 |
| V5-landing | 決めた打ち出しの事実で、落下地点の予測が照準の線の着地点と 0.5 m 以内 (自分が投げた場合)。位置からの推定は 3 m 以内 (目安) | W3 |
| V6-permit | 2 人目 (`SpawnRemotePlayer`) を相手に、合流のハーネスと同じ折り返し (`debugJoinLoopback`) で返事を作る。いいよ前 / 断り / 時間切れ / 取り消し / 回の終わり / 古い seq の返事 / 相手の退室 で `TryPersonPose` が正しく true / false を返す。プレビューが許可前に相手を読まない (読んだ回数を数える) | W4 |
| V7-auto | 場面の入力 (状態の値) を順に書き、おまかせのカットの列が表どおり。許可の無い人のカットへ行かない | W5 |

- 2 人目の Udon は ClientSim では動かない (§1.5)。許可の表は、ハーネスが 2 人目の箱の同期変数を直接書いて作る (受け手側の処理は合流の C ケースと同じく折り返しで試す)。**実通信の合格にはしない** (CLAUDE.md)。
- 合流・呼ぶの既存の T・C ケースは、受付箱を変える W4 で 1 回だけ回す (関係する群)。

### 9.3 ユーザーの実機確認

- PCVR (W1 の後): §3.5 の 5 分テスト + §8 の計測。
- PCVR (W2-W3 の後): 各モードの構図 (散歩 1 区間・洗い場 1 回・フリスビー 3 投・川)。揺れ・曲がり角・止まったとき。
- PC 2 台 (または PC + Quest) (W4 の後): 申し込み・いいよ・断り・時間切れ・取り消し・閉じる・退室・同時に 2 人から申し込み。許可の前にモニターに相手の追従が出ないこと。被写体の「撮影中」の表示。Quest の人が被写体になれること。
- 録画: 使う録画ソフト (OBS など) で 1 分録り、音 (ワールドの音・犬・自分の声) の入り方。
- Quest / iPhone: 手元メニューに撮影カメラの入口が無いこと。

## 10. 実装の段取り (I)

| 波 | ファイル | 中身 | 確認 | レビュー |
|---|---|---|---|---|
| **W1 MVP** | 新規 `KH_VideoCamera.cs` (+ `.Modes.cs` の犬追従・自分の追従、`.Subjects.cs` の `TryPersonPose` (この波は自分だけ通す))、新規 `Editor/KH_VideoCameraEditor.cs` (`Video/1 Build`: Camera・モニター・パネル (モード 2 つ・×・おまかせは灰色)・RecordSnap の専用メッシュと材質 `M_KH_VideoRecordSnap`、`Video/2 Verify`、Android / iOS のビルド前処理)、`KH_PhotoAlbum` の写真の面 (入口ボタン 1 つ。**写真の作業と同じファイルなので、統括が順番を決める**)、`KD_PhotoDirector` (犬の幾何の読み口、§2.4) | 開閉・RT・30 fps・なめらかさ・録画モードの吸着 | compile → `Video/1` → `Video/2` → ハーネス V1-V3 | 録画モードの板 (推奨) |
| (W1 の後) | — | **ユーザーの 5 分テスト (§3.5) と計測 (§8)。ここで「歩いても映る」が成り立たなければ、§3.6 の代わりへ戻って相談** | — | — |
| W2 散歩と犬のモード | `.Modes.cs` (犬目線 A/B・ドローン・ゆっくり一周・通り過ぎる)、通り過ぎるの地点のデータ (ビルダー) | §4.3-4.4・4.8-4.9 | V4 | 不要 |
| W3 遊びと洗いのモード | `.Modes.cs` (定点・フリスビー追従・落下待ち・おかえり・ふれあい・手元)、`KH_Frisbee.cs` (読み取りだけの口 §4.7)、定点のデータ (人が置く) | §4.5-4.7・4.10 | V4・V5 + フリスビーの群のハーネス (KH_Frisbee を触るので、関係する群だけ) | 不要 (読み取りの口だけ) |
| W4 許可 | `KH_TravelMailbox.Film.cs` (新規 partial) と本体の分岐、`KH_TravelNotice.cs`、`KD_TreatPouch.Travel.cs`、`.Subjects.cs` (他人の門)、パネルの人の一覧、Course/85 の Verify に同期変数を足す | §5 | V6 + 合流・呼ぶの既存の T・C ケース | **Codex 必須 (設計の §5 を実装の前に、差分を実装の後に)** |
| W5 おまかせと仕上げ | `.Auto.cs`、パネルのおまかせ、13 言語 (`UITranslations.json`)、仕様 (game-design §7 の対象外の行・world-ui-spec の PHOTO / 新しい VIDEO の行・§6 の状態キー)、操作の案内 | §6・§7.3 | V7、`check-doc-links`、`Menu/22` (文字) | 不要 (同期なし) |

- **並べて作れるもの**: W2 と W3 はモードの関数が別なので並行できる (`.Modes.cs` を W2 用と W3 用の 2 つの partial に分ける)。W4 の受付箱の変更は W2・W3 と独立 (`.Subjects.cs` の門のところだけ合わせる)。W5 は全部の後。
- 写真の作業 (IMP-WLD-007) と同じファイル (`KH_PhotoAlbum.cs`、`KD_PhotoDirector.cs`、`UITranslations.json`、写真の Editor) は、写真の作業がコミットされてから触る。
- 古い `Walking/3・4・4b`、`Menu/7・12・13・17` の作り直しは使わない (CLAUDE.md)。モニターとパネルは新しいビルダーがシーンへ直接作る。

## 11. ユーザーへの質問 (J)

1. **初めて出すときの範囲**: 推奨は **W1 (MVP: 犬追従・自分の追従・録画モード) を先に作って、5 分テストで「歩いても映る」を確かめてから残りを作る**。初期公開に入れるかは、全体ができてから決める (Issue の「導入時期は未確定」のまま)。
2. **入口の場所**: 推奨は **写真の面の [撮影カメラ]** (PC だけ。他の端末ではボタンを置かない)。代わりは「あそぶ」タブ。
3. **Quest・スマホの人を被写体に選べるか**: 推奨は **選べる** (許可と取り消しの UI は全端末にある)。撮影するのは PC の人だけ。
4. **画質**: 推奨は **1280×720・30 fps** で始め、計測の後に 1920×1080 / 60 fps を選べるようにするか決める。
5. **他の人にモニターを見せるか**: 推奨は **見せない (撮る人の画面だけ)**。モニターの同期も、他の人への小さな表示もしない。
6. **撮られている人が分かる印を、周りの人にも見せるか**: 推奨は **見せない** (被写体本人の手元メニューにだけ「撮影中」)。
7. **撮影の申し込みを受ける設定は、合流と別にするか**: 推奨は **別 (「撮影の申し込みを受ける」初期 ON)**。合流は受けるが撮影は受けない人がいるため。休止 10 分とブロックは合流と共通。
8. **[VRChatカメラに映す] を開いたときから ON にするか**: 推奨は **ON** (開いたらすぐ録れる)。ON の間は VRChat カメラで普通の写真が撮れないことをパネルに書く。

## 12. 実機でしか分からないこと

- 録画モードの板が、VRChat カメラ (手持ち・ストリーム・Desktop) で **どこからでも・どの向きでも** 全画面になるか (視錐台の間引き・オクルージョン・`_VRChatCameraMode` の値)。
- 動画のカメラ (ワールドの Camera) の描画で `_VRChatCameraMode` が 0 になっているか (= 録画モードの板が動画の中に写り込まないか)。
- ライブの RT (毎フレーム書き換わる) を吸着シェーダーで出したときに、ちらつき・1 フレームの遅れ・黒い画面が出ないか (前作で一度も試していない)。
- ストリームカメラの出力 → 録画ソフトの経路で、どの操作が要るか。音がどう入るか。
- 撮影中のフレーム時間 (PC の GPU ごと)。
- 2 人以上での許可の届き方 (送信の遅れ、取り消しが効くまでの時間)、Quest の人の通知板。
- §1.3 の SDK の API が、実際のセッションでどんな値を返すか。

## W1 実装メモ (2026-10-03)

**未コンパイル (Unity では未確認。オフラインの Roslyn 検査は実行時・Editor の両方で 0 エラー)・未実行。** 既定の案 (§11) を採用して実装した: 先に MVP、入口は写真の面の [撮影カメラ] (PC だけ)、モニターは自分だけ、周りへの印なし、開いたら RecordSnap は ON、1280×720・30 fps。Codex の設計レビュー (2026-10-03) の W1 に関わる 4 点も入れた (下の「設計からの変更」)。

| ファイル | 中身 |
|---|---|
| `Scripts/KH_VideoCamera.cs` (新規) | 本体 (sync None、同期変数・ネットワークの口なし)。開閉 (`_Open` = 開く / 開いていれば呼び戻す、`_Recall`、`_Close`)、PostLateUpdate の 30 fps の拍で Camera を有効にし、`KH_VideoCameraHook.OnPostRender` → `_OnCamRendered` で無効にする (拍ごとに 1 回、普通のカメラの段階。W1 の手直し 3)、共通の後処理 (遮り 10 Hz・床・犬からの押し出し・6 m のカット・指数のなめらかさ・Roll 0)、パネルの表示、`VRCCameraSettings.PhotoCamera.Active` の 2 Hz の読み出し (状態の行だけ)。Start は Android / iOS で何も出さず、どの端末でも最後に自分を無効にする (閉じている間は毎フレームの処理 0) |
| `Scripts/KH_VideoCamera.Modes.cs` (新規) | モードの切替 (switch 1 つ)・進む向き (速度をならす。遅いときは前の向きを保つ。急な曲がりで位置の時定数を 1.0 s)・犬追従・じぶん。**なめらかさの遅れの先取り** (`Lead`: 目標を速度 × 時定数だけ前へ。設計 §4.1 に無かった。3 m/s の犬で 1.8 m 遅れるため) |
| `Scripts/KH_VideoCamera.Subjects.cs` (新規) | プレイヤーを読む唯一のファイル。W1 は自分だけ (`SelfHead`)。犬の幾何は `KD_PhotoDirector` の式の写し (§2.4 の「写す」を選んだ。写真のファイルは触っていない)。W4 の `TryPersonPose` はここに足す |
| `Scripts/Editor/KH_VideoCameraEditor.cs` (新規) | `Video/0` Program Asset、`Video/1` 組み立て (カメラ・モニター・パネル・RecordSnap・メッシュ・材質・写真の面の入口 3 つ・試作値の表・字形)、`Video/9` 取り外し |
| `Scripts/Editor/KH_VideoCameraEditor.Verify.cs` (新規) | `Video/2` (読むだけ)。配線・マスクとレイヤーの組・材質の値・メッシュの bounds・ボタンの配線・試作値・字形・**全 partial のソース検査** (同期とネットワークの口が無い、他人を引かない、プレイヤーの姿勢は Subjects.cs だけ、public は `_` 始まり) |
| `Scripts/Editor/Testing/KH_VideoHarnessEditor.cs` (新規) | `Video/3` ハーネス・`Video/3b` 停止 (V0-V3) |
| `scripts/kh-harness-run.sh`・`scripts/kh-harness-gate.cjs` | `video [--cases=V1,V3]` と `stop video` を足した (Play の入口なので `--reason` / `--consult` が要る) |

**PC 以外の扱い**: シーンはどの端末でも同じ (ビルド前に何も外さない)。

- `KH_VideoCamera.Start` が `#if UNITY_ANDROID || UNITY_IOS` で `_supported = false` にし、カメラ・モニター・RecordSnap を隠し、自分を無効にする。RT は作らない。
- 入口の 3 つのボタンは **シーンに隠した状態で保存**され、PC の Start だけが [撮影カメラ] を出す。どの物も他から参照されていないので、どの端末でも参照切れは出ない。
- 設計 §2.5 の「Android / iOS ビルドでは削る」は採らなかった。U# のプロキシを削る順番の罠 (`vrc-udonsharp` §7.1) を避けるため。端末で増えるのは、使われない Udon 1 つ・Quad 2 枚・材質 2 つ・小さなメッシュ 1 つ。

**設計からの変更 (Codex レビューを受けて)**:

- **RecordSnap のレイヤーは 4 Water** (`KH_VideoCameraEditor.RecordSnapLayer`、1 か所)。
  - 動画のカメラと写真の自動撮影カメラはこのレイヤーを描かない。だから「自分たちのカメラには写らない」が、自分たちの描画のときの `_VRChatCameraMode` の値に頼らなくなった。
  - Water を選んだ理由は 4 つ。シーンの基準カメラ (Main Camera) は Everything を描く。Water は普通に描かれるレイヤー。このシーンに Water の物は 0 個。写真のカメラは前から Water を外している。
  - Default (0) は VRChat カメラに写ることが分かっている (写真のボード) が、自分たちのカメラも描く。17 は未確認 (IMP-WLD-007 W3)。
  - **5 分テストで全画面にならないときは、0 にして Video/1 を回し、確認 5 (入れ子が無いか) を見直す。**
  - Verify は「0 固定」ではなく、レイヤーとマスクの組を見る。
- **renderQueue は 5001**。写真のビューア (4000/4001)・写真の枠 (4100/4101)・夜の覆い (4999。下回ると動画が二重に暗くなる)・移動の暗転 (5000) より後に描く。
  - 規則: **ON の間は VRChat カメラの描画はいつもこの映像になる。** ボードやメニューの吸着を使うときは OFF にする。パネルの説明文に書いた。
- **説明の文**: `_VRChatCameraMode > 0.5` はスクリーンショットも含む。パネルの文は「VRChatカメラの写真・動画・配信とスクリーンショットがこの映像になります」。
- **× / しまう**: カメラを止め、画面と RecordSnap の材質から RT を外してから RT を捨てる。モニターと RecordSnap は隠す。なめらかさ・向き・遮りのキャッシュは捨て、次に開いたときはカットから始める。
- 見失ったときに最後の構図を 3 秒保つのは **犬だけ**。W4 の許可が切れたときはこの経路を使わない (コードに注記)。

**Unity での手順 (統括)**:

1. `unity-compile.sh` (refresh)
2. `Video/0` → `unity-compile.sh --udon`
3. `Video/1`
4. `Video/2`
5. 相談の後で `kh-harness-run.sh video --reason .. --consult ..`

最初の実行で予想される失敗:

- (a) `Video/0` の前の `Video/1` は「program asset missing」で何も変えずに止まる。
- (b) U# が `VRCCameraSettings.PhotoCamera` / `.Active` を受け付けない場合は U# のコンパイルエラーになる。公開は DLL の文字列で確認しただけ。そのときは `ReadPhotoCamera` を消す (状態の行の「VRChatカメラ: …」が無くなるだけ)。
- (c) 字形が焼けていない状態での `Video/2` は FAIL。`Video/1` が焼くので、`Video/1` の後なら通る見込み。
- (d) 言語の検査 (`Menu/22` など) が、写真の面の日本語の直書き 3 つを指摘するかもしれない。W1 は直書きのまま。13 言語は W5。

**ハーネス (Video/3、約 1 分。V0 は必ず回る)**:

| ケース | 確かめること |
|---|---|
| V0 | Start の後、閉じた状態 (無効・カメラ OFF・隠れている)。写真の面は [撮影カメラ] だけ |
| V1 | 開閉 10 回。開: RT 1280×720 (深度あり) がカメラ・画面・RecordSnap に入る。描く回数は 1 秒で 32 以下かつ min(26, 0.85 × fps) 以上。閉: カメラ OFF、RT のフィールドが null で RT の実体は破棄済み、材質の RT を外した、隠れた、無効、閉じている間は描かない。10 回の後: 作った RT 10 枚がすべて破棄済み、余分な 1280×720 の RT が残っていない、システムの物の数が同じ |
| V2 | ハーネスの犬 (実行時だけ犬 0 の代わりに置く) が 3 m/s で 3 s 歩く。カメラは犬の胴から 0.3〜5.3 m (遮りが無ければ 1.1〜5.1 m)、胴が画面の内側 80 %、Roll 0、カット以外で 1 フレーム 0.5 m を超えて動かない。20 m 飛ぶとカット |
| V3 | じぶん: 立ったままで、頭 + 回転 35° × 向き × 2.4 − 0.1 上 (遮りが無ければ 0.35 m 以内)、前側にある、胸を見る (自分の犬なし)、Roll 0。15 m のテレポートでカット |

- 見ないもの: VRChat カメラの吸着 (ClientSim は `_VRChatCameraMode` も写真カメラも作らない)、絵の見た目・顔、費用、録画。

**ユーザーの 5 分テスト (PCVR、W1 のアップロード後。チェックリスト用の文)**:

1. 手元メニューの [写真] → [撮影カメラ]。目の前にモニターが出て、犬が動いて映る。[じぶん] で自分が映る。
2. VRChat カメラを出す。モニターから 20 m 歩き離れて、モニターと反対へ向ける。画面いっぱいに動画が出る。その場で 1 周回っても消えない。
3. VRChat カメラを配信 (Stream) にし、デスクトップの窓に動画が出る。録画ソフトで 30 秒録り、ワールドの音・犬の音・自分の声が入っているか聞く。
4. パネルの [VRChatカメラに映す] を OFF → VRChat カメラが普通の景色に戻る。ON → 動画に戻る。Desktop モードでも 2 と 4 を 1 回。
5. 録った動画の中に、モニターや動画そのものが入れ子に写っていない。鏡の前で、鏡の中が全画面にならない。× で閉じたら、VRChat カメラに古い映像が残らない。

- 2 で全画面にならない: `RecordSnapLayer` を 0 にして作り直し、もう一度 2 と 5。
- それでもだめなら §3.6 (写真カメラの位置を読んで板を動かす)。

### W1 の手直し (2026-10-03、Unity での初回 Video/2 PASS の後。Codex の実行前相談を受けて)

- **閉じた後に開き直したとき、前の構図を出さない**: 開く・モードを変える・犬を変えるたびに、次のものを捨てる。
  - その対象の「有効な姿勢を得た回数」(`_subjectPoses`)。
  - 見失ったときの猶予の時計。
  - なめらかさ・遮りのキャッシュ。
  - さらに、画面と RecordSnap の材質から RT を外す (黒になる)。
  - この回で有効な姿勢が 1 回出るまでは描かず、RT も張らない。状態の行は「(犬) が見つかりません」。
  - 3 秒の猶予は、この回に同じ対象を描いた後だけ。猶予が切れたら黒にする。
- **姿勢の式を 1 か所に**: 犬追従・じぶんの位置と注視点、先取り、構図の距離、なめらかさの 1 歩、Roll 0 の回転を、`KH_VideoCamera.Modes.cs` の static な純関数にした。
  - 実行時と `Video/2b - Probe Poses` (Edit モード、Play なし) が同じ関数を呼ぶ。
  - 探査に写したのは、速度のならし (1 行) とカットの規則 (1 行) だけ。
- **ハーネス**:
  - 描画の回数は `Camera.onPostRender` (撮影カメラだけ) で数える。
  - 最初の失敗で止める。止める前に、閉じる・犬 0 の参照を戻す・`_dog` と `_mode` を戻す・ハーネスの犬を消す、をしてから Play を抜ける。残りのケースは SKIPPED。
  - Verify は、ボタンごとに呼び先 (撮影カメラの UdonBehaviour)・関数 (`SendCustomEvent`)・文字列の引数を照合する。
- **式で分かったこと (Unity の前に node で同じ式を計算した)**:
  - 犬追従: 頭の高さ 0.25 / 0.4 / 0.6 m の犬で、距離は 1.27 / 1.67 / 2.46 m、画面の中心からのずれは最大 0.55 (内側 80 % の中)。
  - いちばん小さい犬が歩き出す間は、距離が一時 0.91 m まで縮む。先取りがならした速度に遅れて追いつくため。探査の下限は 0.8 m にし、DogDistMin を下回った行は注記する。
  - **じぶんで足元に自分の犬がいると、犬が画面の下へ外れる** (selfLookToDogs 0.4・FOV 55・2.4 m で、縦のずれ 約 1.2)。0.6 にすると犬は 約 0.79、頭は 約 0.86 で、両方入る。**W2 の前に値を決める** (V3 は犬をつながないので、この値は検査しない)。

### W1 の手直し 2 (2026-10-03、ハーネス V1 の失敗 run 20261003-113800 を受けて)

- **起きたこと**: 90 fps で、拍 (Camera を有効にしたフレーム) は 31 回/秒だったのに、実際の描画 (`Camera.onPostRender`) は 49 回/秒あった。1 回有効にするごとに約 1.6 回描いていた。
- **原因の読み**:
  - PostLateUpdate は LateUpdate の中で、描画の前に動く (SDK の `PostLateUpdater`、実行順 31000)。
  - そのため「フレーム N で有効 → N+1 の PostLateUpdate で無効」なら、プレイヤーループの中で描くのは N の 1 回だけのはず。拍が続けて 2 回来ても、同じフレームで 2 回描くことは無い。
  - 残る説明は、**プレイヤーループの外の描画**。Editor の Game ビューの再描画 (インスペクタ・ハーネスの更新・API の呼び出しなどで起きる) は、有効な Camera を全部描く。有効な時間 (N の LateUpdate から N+1 の LateUpdate まで) の間にそれが入ると、もう 1 回描かれる。
  - 確かめるために、ハーネスは描画ごとに `Time.frameCount` を記録する。そして「描いた別々のフレームの数」と「同じフレームの中の 2 回目以降」を出す。次の run でどちらかが分かる。
  - 実機でも、他の描画の経路 (VRChat のカメラ・鏡など) と重なる可能性を消せない。
- **変えたこと**: Camera の部品は **常に無効**。拍ごとに PostLateUpdate で姿勢を置き、`cam.Render()` を 1 回呼ぶ (`RenderOnce`)。
  - `_renders` は拍の数。
  - Udon への公開は `UnityEngineCamera.__Render__SystemVoid`。前作が避けたのは公開が分からなかったからで、具体的な不具合の記録は無い。
  - RT は `targetTexture` のまま。目・ステレオの状態は RT のカメラには関係しない。費用は 1 回の描画と同じ。
  - 違いは 1 つ: 描くのが LateUpdate の中なので、肌のあるメッシュ (犬・アバター) は 1 フレーム前の変形で描かれる (`UpdateAllSkinnedMeshes` は LateUpdate の後。90 fps で 11 ms)。姿勢 (骨・Transform) はそのフレームのもの。
- **Verify**: `cam.enabled = true` がソースに無いこと、`cam.Render()` の呼び出しが 1 か所だけであること、を見る。
- **ハーネス**:
  - V1 は「実際の描画 = 拍の数 ±1」「Camera の部品は無効のまま」を見る。V2 は描画が増えたときだけ調べる。
  - 失敗したときの表示: 終わったケースは PASS、始まっていないケースだけ SKIPPED (前の run の V0 の SKIPPED は表示の誤り)。

### W1 の手直し 3 (2026-10-04、PCVR 実機でのユーザー確認を受けて)

- **実機の結果**:
  - **吸着は成立した。** 録画の板 (レイヤー 4・renderQueue 5001) で、VRChat カメラに動画が画面いっぱいに出た。
  - **不具合**: ユーザーは OVR Advanced Settings の遊び場のずらし (playspace offset) で地面に座っている。普段の見え方・VRChat カメラ・自動写真では、アバターは地面に座って見える。ところが動画 (じぶん) だけ、自分のアバターが約 1 m 浮き、空中であぐらをかいていた。犬と移動は正しかった。
- **原因の読み**:
  - 手直し 2 で、PostLateUpdate (LateUpdate の中、実行順 31000) から `Camera.Render` を呼ぶ形にした。この時点では、VRChat がそのフレームの自分のアバターの最終の位置 (遊び場のずらしの補正を含む) をまだ置いていない。だから補正の前の位置で描かれた。
  - 自動写真のカメラ (`KD_PhotoCapture`: 有効にして、次のフレームで無効) は、普通のカメラの段階で描くので正しい。
  - 他の人のアバターは、ずれても 1 フレームの遅れだけの見込み (未確認)。
- **新しい形**: 普通のカメラの段階で描き、拍ごとにちょうど 1 回にする。
  - 拍で `EnableForTick` が Camera を有効にする (有効にするのはここだけ)。
  - Unity は、そのフレームの描画で他のカメラと一緒に描く。
  - Camera の GameObject にある `KH_VideoCameraHook` (sync None) が、Unity の `OnPostRender` を受けて `rig._OnCamRendered()` を呼ぶ。そこで、すぐに無効にする。
  - Udon での裏づけ:
    - `UdonBehaviour.OnPostRender` → `RunEvent("_onPostRender")` (Runtime/Udon/UdonBehaviour.cs:841)。
    - U# は `VRC.Udon.Graph.dll` の `Event_OnPostRender` で名前を対応づける (CompilerUdonInterface.cs:94-107)。
    - 描画のイベントとして注入される (UdonSharpEditorManager.cs:236)。
  - Unity は `OnPostRender` を、そのカメラの GameObject にある有効なスクリプトにだけ送る。だから補助は Camera の物に置き、常に有効にする。
  - **安全網**: 呼び戻しが来なかったら、次の PostLateUpdate で無効にし、`_missedCallbacks` を数える。当初の「1 フレームだけ有効」と同じ時刻なので、拍あたり最大 1 回の描画は保たれる。
  - 手直し 2 の余分な描画 (有効な間にプレイヤーループの外の描画が入る) は、描画の直後に無効にするので起きない。
  - `_ticks` は拍の数、`_renders` は呼び戻しの数 (実際の描画)。画面と RecordSnap に RT を張るのは、その対象の最初の実際の描画の後。
- **Verify**: 手動の `Render()` が無いこと。`cam.enabled = true` は `EnableForTick` の 1 か所だけ。`_OnCamRendered` が無効にすること。補助がカメラの物に 1 つあり、配線・sync None・有効であること。補助のソースが `OnPostRender` → `_OnCamRendered` で、同期とネットワークが無いこと。
- **モニターの裏**: 後ろから見ると、パネルの文字が裏返って見えた (ワールドの UI は両面に描かれる)。キャンバスの 4 mm 後ろに、枠の色の板 (`Back`、0.86 × 0.76 m、Unlit/Color、後ろ向き) を置いた。前からは裏面なので描かれず、見え方は変わらない。Verify は位置・向き・大きさを見る。
- **ハーネス V1**:
  - 実際の描画 (`Camera.onPostRender`) = 拍 ±1。
  - 補助の呼び戻し = 拍 ±1。
  - 呼び戻しの取りこぼし 0。
  - フレームとフレームの間 (EditorApplication.update) で Camera が有効になっていた回数 0。
  - 閉じた後は無効。
- **ユーザーの再確認 (PCVR)**: OVR Advanced Settings の遊び場のずらしで地面に座り、撮影カメラを [じぶん] にする。動画の中の自分のアバターが、普段の見え方と同じ地面の位置に座っていること (浮かない)。立って歩いたときも、足が地面に付いていること。モニターを後ろから見ると、文字が見えず、無地の板であること。

## W2・W3 実装メモ (2026-10-04)

**未コンパイル (Unity では未確認。Roslyn は実行時・Editor の両方で 0 エラー)・Unity は未実行・実機は未確認。** 撮り方 12 種を 1 つの Camera と RT で動かす。被写体は自分・犬・ディスクだけで、他の人は読まない (W4)。他の人のポーズが要る場面 (他人が撫でている手、自分以外が投げた、自分以外が洗っている) では、犬だけの構図に落とす。

### ファイル

| ファイル | 中身 |
|---|---|
| `Scripts/KH_VideoCamera.cs` | モード 12 の番号と文言 (`ModeWord`)、ピッカーの種類 (`PickerKind`: 犬 / 定点 / なし)、`_Mode0`〜`_Mode11` (W1 の `_ModeDog` / `_ModeSelf` は残す)、モードごとの配線 (犬ごとのフリスビー・ディスク・撫で・徘徊・洗い、洗い場、水遊び場、定点と通り過ぎのデータ)、試作値、モードごとの後処理の指定 (位置・注視の時定数、データ位置 = 遮りと床の補正なし、地面からの最低の高さ)、見失いの行 (犬の名前 / モード名 / 洗っている犬がいません / 定点がありません) |
| `Scripts/KH_VideoCamera.Modes.cs` | モードの switch (選んだモードだけ計算)、`AutoSubject` (定点 = 自分にいちばん近い所、ふれあい = 自分が撫でている犬)、W1 の 2 種、**全モードの static な式** (Probe と同じ関数) |
| `Scripts/KH_VideoCamera.ModesWalk.cs` (新規) | 犬目線・ドローン・定点・一周・通り過ぎ |
| `Scripts/KH_VideoCamera.ModesPlay.cs` (新規) | ディスク・落下待ち・おかえり・ふれあい・手元 |
| `Scripts/KH_VideoCamera.Subjects.cs` | `SelfId` (自分の id、同期された id と比べるだけ)、`SelfHandNear` (自分の手)。プレイヤーを読むのは今もこのファイルだけで、自分だけ |
| `Scripts/KH_Frisbee.cs` | 読み取りだけの口 `FlightLocal` / `FlightP0` / `FlightV0` / `FlightT0` / `FlightCurveNow` (同期・挙動は変えない) |
| `Scripts/KH_FrisbeeCatch.cs` | 読み取りだけの口 `ThrowerId` (同期済みの `throwerId`) |
| `Scripts/Editor/KH_VideoCameraEditor.cs` | 試作値の表 (`ModeTuning`、名前 → 値。書く・照合するの両方がこの表を使う)、12 個のボタンの並び、文言の字形 |
| `Scripts/Editor/KH_VideoCameraEditor.Data.cs` (新規) | 犬ごとの参照の配線、定点と通り過ぎの地点をシーンから作る (Edit モードで、中に埋まらない・見通し・下 4 m 以内の地面を確かめる) |
| `Scripts/Editor/KH_VideoCameraEditor.Verify.cs` | 12 個のボタン (呼び先・`SendCustomEvent`・引数・文字・120×50 px 以上)、W1 のボタンが残っていないこと、犬ごとの配列、洗い場の順、定点と通り過ぎの地点 (地面・見通し)、試作値 (プロキシ + backing の値が入っていれば照合)、ソースの規則 (全 partial: 同期・ネットワークの口なし・他人を引かない・プレイヤーの姿勢は Subjects.cs だけ) |
| `Scripts/Editor/KH_VideoCameraEditor.Probe.cs` | 全モードを合成の被写体で計算 (Edit モード)。フリスビーはシーンのディスクの `_PositionAt` で飛ばす |
| `Scripts/Editor/Testing/KH_VideoHarnessEditor.cs` | V4-walk-modes・V5-play-modes (コードだけ。回していない) |

### 撮り方の一覧 (作ったもの。値は試作値で、Video/1 が書く)

| # | 板の文字 | 位置の基準 | 見る先 | FOV | なめらかさ (位置 / 注視) | 見失ったとき | 主な試作値 |
|---|---|---|---|---|---|---|---|
| 0 | 犬追従 | 犬の胴から進む向き 40°・構図の距離 (1.2〜5 m)・頭 + 0.35 | 胴 + 進む向き 0.5 m | 50 | 0.6 / 0.25 s、先取りあり | 犬がいない → 3 秒保持 → 黒 | dogFill 0.4 |
| 1 | じぶん | 自分の頭から 35°・2.4 m (自分の犬がいれば **3.2 m**)・−0.1 | 犬なし: 胸。犬あり: 頭 → 犬の中心の **0.55** | 55 | 0.6 / 0.25 s | なし | selfDistanceWithDog 3.2、selfLookToDogs 0.55 |
| 2 | 犬目線 | 自分が 6 m 以内: 犬の頭の横 (自分と反対側から 50°)・0.35 m・+0.05。遠い: 頭の前 0.15 m | 自分の頭 / 犬の進む先 (3° 上) | 60 / 70 | 0.3 / 0.25 s | 犬がいない | dogEyeUserMax 6 |
| 3 | ドローン | 自分の足元の後ろ 6 m・上 7 m (地面から 4 m 以上) | 自分の前 2 m | 60 | 1.2 / 0.25 s、先取りあり | なし | droneHeight 7 |
| 4 | 定点 | データの位置 (洗い場 = 台から 2.6 m・1.5 m 上、川 = 水遊び場から 6 m・1.8 m 上と 4 m・0.6 m 上) | データの注視点。その洗い場に着いた犬 / 水の中の犬へ 30% 寄せる | 55 | 固定 / 0.35 s | 定点が無い | spotDogPull 0.3 |
| 5 | ディスク | 飛行中: ディスクと犬の中点の右・離れた分だけ (3〜12 m)・上 0.8、ディスクの速度 × 0.3 s の先取り。飛んでいない: 犬追従 | 中点 (15 m より離れたら犬) | 60 | 0.3 / 0.1 s | 犬かディスクが無い | discSideK 1.0 |
| 6 | 落下待ち | 予測した着地点 L から犬の側へ 5 m・越えて 3 m・上 1.2 m に置き、待つ (予測が 5 m 以上ずれたら 1 回だけ置き直す)。投げる前: 犬追従 | L + 0.5 (ディスクが 8 m 以内ならディスクとの中点) | 55 | 0.6 / 0.25 s | 同上 | landSide 5、landBeyond 3、landStep 0.12 |
| 7 | おかえり | 運んで戻る間。自分が投げた: 自分から犬の方へ 1.6 m・高さ 0.45。他の人: 犬の前 3 m から振り返る。それ以外: 犬追従 | 犬の頭 | 55 | 0.6 / 0.25 s | 犬がいない | welcomeDist 1.6 |
| 8 | ふれあい | 犬の頭の横 0.55 m (自分の手の反対から 60°、撫でていなければ犬の左から)・+0.05。ぶるぶるの後 3 秒は正面 1.2 m | 自分が撫でている: 頭と手の中点。おやつ: 口元 | 45 | 0.6 / 0.25 s | 犬がいない | fureaiDist 0.55 |
| 9 | 手元 | 最後にこすった部位から、洗う人の反対側 0.6 m・上 0.25 (洗う人が自分でなければ犬の左側を洗う人とみなす) | 部位 | 45 | 0.6 / 0.4 s | 洗っている犬がいない (自分が呼んだ洗い場 → 6 m 以内の洗い場) | washDist 0.6 |
| 10 | 一周 | 自分と自分の犬の中心 (2 s でならす) の周り半径 3.5 m・高さ 1.4、6°/s。自分の前から始める | 中心 + 0.6 | 50 | 0.3 / 0.25 s | なし | orbitDegPerSec 6 |
| 11 | 通り過ぎ | 通り過ぎの地点 (写真の場所 3 か所を道の脇 3〜4.5 m・1.4 m 上へずらしたもの) のうち、自分の犬 (いなければ自分) の前方 5〜120 m でいちばん近い所。近づく → 通る → 15 m 遠ざかるか通ってから 8 s で次へ。60 s 来ない・150 m 離れたら次へ | 被写体 + 0.5 | 50 | 固定 / 0.25 s | 前方に地点が無いときはじぶんの構図 | passNear 25、passFar 120 |

- **犬目線が 2 つあることについて**: この動画の「犬目線」は、Issue の定義「犬のそばの低い位置からユーザーを見上げる」(相手は自分)。写真の「犬目線」はユーザーが「犬が向いている方向の景色」に決め直した (photo-expansion §0)。動画では、自分が 6 m より遠いときだけ写真と同じ景色の構図に切り替える。こうすると、誰もいない方向を見上げることがない。
- **設計からの変更 (Probe で決めた)**:
  - ディスク: 設計 §4.6 の「ディスクの後ろ 2.5 m」だと、追いかける犬がカメラの後ろに入る (合成の投げで 65 フレーム中 61 が範囲外)。ディスクと犬の組の横から撮る形に変えた。位置はディスクと一緒に動く。
  - 落下待ち: L の真横だと、飛んでくるディスクが外れることがある (90 中 4)。L を 3 m 越えた所にして、飛来と犬の駆け込みがカメラへ向かってくる形にした。
  - 通り過ぎ: 見る先を被写体 + 0.5 m にした (近づいたときに頭と足が両方入る)。
  - じぶん: W1 の課題を直した (足元の犬が画面の下へ外れる)。自分の犬がいるときは 3.2 m・0.55。
- **ピッカー**: 犬 (犬追従・犬目線・ディスク・落下待ち・おかえり・ふれあい。ディスクは選んだ犬のディスク) / 定点 (定点) / なし (じぶん・ドローン・手元 (自動)・一周・通り過ぎ)。

### 板の並び (キャンバス 840 × 880 px、1 px = 1 mm)

- 上端 y 412: 題「撮影カメラ」と ×。
- 画面 0.80 × 0.45 m: 中心 y 157。
- モードのボタン 2 段 × 6 (各 128 × 56 px = 12.8 × 5.6 cm、間隔 136 px)。y −112 が 犬追従・じぶん・犬目線・ドローン・定点・ディスク、y −176 が 落下待ち・おかえり・ふれあい・手元・一周・通り過ぎ。
- ピッカー: y −244 (◀ 80 × 56 / 名前 250 / ▶)。
- [VRChatカメラに映す]: y −310。注記 y −360、状態の行 y −404。
- 裏の板は 0.86 × 0.90 m。モニターは前より 14 cm 高い。

### Probe の数字 (2026-10-04、同じ式を node で計算した。Unity の Video/2b はまだ回していない。ディスクの式は KH_Frisbee の既定値 (drag 0.15・lift 0.30) で計算した。シーンのディスクの値が違えば数字も変わる)

| モード | 合成の場面 | 距離 | 内側 80 % の外 | 最悪の |ndc| | 1 フレームの最大の移動 | 跳んだらカット |
|---|---|---|---|---|---|---|
| 0 犬追従 | 頭 0.25 / 0.4 / 0.6 m、0〜3 m/s | 0.91〜2.46 m | 0 | 0.55 | 0.11 m | する |
| 1 じぶん | 犬なし / 足元に犬 (右前・左・2 m 前) / 歩く | 2.40 / 3.20 / 2.16〜2.38 m | 0 | 0.79 (犬 2 m 前) | 0.05 m | する |
| 2 犬目線 | 自分が横 1.2 m・前 1 m / 遠い | 2.14〜2.20 m / — | 0 | 0.10 | 0.04 m | する |
| 3 ドローン | 1.4 m/s で歩く | 9.26〜9.47 m | 0 | 0.25 | 0.05 m | する |
| 4 定点 | 洗い台の犬と、横 0.9 m の洗う人 | 2.86 m | 0 | 0.33 | 0 | (固定) |
| 5 ディスク | 9 m/s の投げを 7 m/s で追う犬 | 3.55〜4.75 m (ディスクまで) | 0 | 0.68 | 0.29 m | する |
| 6 落下待ち | 着地 18.5 m 先、カメラは L から 5.95 m | 5.95 m | 0 | 0.62 | 0 | (固定) |
| 7 おかえり | 15 m から 3 m/s で戻る (自分が投げた / 他の人) | 1.40〜12.4 / 2.37〜3.03 m | 0 | 0.06 | 0.11 m | 他の人の場合はする |
| 8 ふれあい | 自分の手で撫でる / 誰も撫でない | 0.55 m | 0 | 0.14 | 0 | する |
| 9 手元 | 背中、自分は左 | 0.65 m | 0 | 0.48 | 0 | する |
| 10 一周 | 自分と犬 1 匹、20 s | 3.77 m | 0 | 0.63 (足元) | 0.01 m | (周回) |
| 11 通り過ぎ | 道の脇 3 m を 1.4 m/s で通る | 3.16〜25 m | 0 | 0.51 | 0 | (固定) |

### Unity の手順 (統括)

1. refresh。
2. `unity-compile.sh --udon`。新しい partial 2 つは既存の Program Asset に入る。KH_Frisbee・KH_FrisbeeCatch も再コンパイルされる。
3. `Video/1`: 12 個のボタン・ピッカー・大きくした板、犬ごとの配線、定点と通り過ぎの地点を作る。
4. `Video/2`。
5. `Video/2b`。
6. 相談の後、ハーネス 1 ケース。

**最初に予想される失敗**:

- (a) U# が受け付けないもの (Udon でしか分からない): 他の Behaviour のメソッドに引数を渡して値を受け取る呼び出し (`disc._PositionAt(...)`、`waterSpot._Contains(...)`)、他の Behaviour の配列の読み取り (`w.partBones`)、static メソッドを返り値つきで呼ぶもの。前例はある (`KH_FrisbeeCatch.LandingOf`、`catcher.ReqCatch`)。
- (b) `Video/1` の「no usable camera point」の WARN (洗い場・川・通り過ぎ)。地形しだい。WARN の地点は作られず、Verify は残りを照合する。
- (c) `Video/2` の WARN「backing heap に試作値がまだ無い」(保存・読み直しの後に消える見込み)。
- (d) 文字の幅: 「通り過ぎ」「落下待ち」が 128 px に 22 → 14 pt で収まるか。

**最初に回す 1 ケース**: **V5-play-modes**。Udon でしか確かめられない、他の Behaviour の読み取り (State・IsCarrying・PetActive・_ShakeSerial・_Arrived・_CallerId・_AnchorPosition) の多くを、ワールドを動かさずに 1 回で通る。V4 は自分と犬の位置しか読まないので、V5 の後でよい。

### ユーザーの確認 (PCVR、1 行ずつ。見ること: 距離・高さ・揺れ・被写体が入るか)

- 0 犬追従: 犬をつないで歩く・止まる・曲がる。犬が画面の中ほどに大きめに入り、揺れないか。
- 1 じぶん: 犬をつないで立つ・歩く。自分の顔と足元の犬が両方入るか (前の課題)。
- 2 犬目線: 犬の横に立つ。低い所から自分を見上げる絵か。7 m 以上離れると、犬の前の景色に変わるか。
- 3 ドローン: 散歩する。上から自分と犬と道が見えるか。高すぎ・低すぎ・地面に潜らないか。
- 4 定点: 洗い場で犬を洗う / 川で遊ばせる。◀ ▶ で地点を替える。犬と洗う人が入るか、地点の位置 (植え込み・壁越しでないか)。
- 5 ディスク: フリスビーを投げる。飛ぶディスクと追う犬が一緒に入るか。ディスクの回転で画面が回らないか。
- 6 落下待ち: 投げる。落ちる所の近くで待ち、飛んでくる・取る・落ちるが入るか。人が投げたときも同じか。
- 7 おかえり: 投げて、犬が戻ってくるのを待つ。犬がこちらへ駆けてくる正面か。
- 8 ふれあい: 犬の頭を撫でる・おやつをあげる・洗い終わる。顔と手が寄って入るか。
- 9 手元: 洗い場で洗う。泡の手元が寄って入るか。洗っていないときは「洗っている犬がいません」が出るか。
- 10 一周: 犬と座る。ゆっくり回る (1 周 60 秒)。景色ごと入り、木や斜面にぶつからないか。
- 11 通り過ぎ: 橋・見晴らし・川辺の近くを散歩する。先回りして待ち、近づく・通る・遠ざかるのが撮れるか。

### W2・W3 の手直しとキャッチ待ち (2026-10-04、Unity での Video/2 PASS 351・Video/2b PASS の後)

**未コンパイル (Roslyn だけ 0 エラー)・Unity 未実行。**

- **モード 6 を「キャッチ待ち」にした** (ユーザーの指定「着地地点ではなくキャッチ地点」「投げた瞬間にわかるから」)。上の表の 6 行目はこの段落が優先。
  - **読むもの**: 犬が投げを受けたときに決めるキャッチの予定。`KH_FrisbeeCatch` に読み取りだけの口を足した: `PlanReadable` / `PlanCatchT` / `PlanCatchRoot` / `PlanP0` / `PlanV0` / `PlanCurve`。
    - 予定を作るのは **犬の Owner の端末だけ** (`ReqCatch` が Owner で走る。`_localPlanValid` はそこだけで立つ)。だから `PlanReadable` は、自分が犬の Owner で、予定があるときだけ真。
  - **キャッチ点 C** = その時刻のディスクの位置 (`KH_Frisbee._PositionAt`)。飛ぶ向きは `_VelocityAt`。
  - **カメラ**: C の横 6 m (犬のいる側)・C + 0.3 m に、投げた時に 1 回置いて待つ。見る先は C から飛んでくる側へ 2 m の点。FOV 55。
    - 予定のキャッチの根元が 1 m 以上変わったときだけ置き直す (最大 2 回)。
  - **予定が読めないとき** (他の端末・犬が届かない・投げが遮られた): 0.6 s 待ってから、前の「落下待ち」の着地の予測を使う。自分が投げたなら投げの事実から、他の人なら同期された位置から。
  - 犬がディスクをくわえたら、出来事 `ShotCatch` を 1 回出す。
  - 試作値: catchSide 6、catchUp 0.3、catchLookBack 2、catchPlanWait 0.6、catchReplanMove 1。
  - **Probe** (同じ式を node で計算。合成の投げ: 9 m/s、下りの口の高さ 1.0 m で 2.27 s 後・17.3 m 先で取る。犬は投げた人の 2 m 後ろから走り込み、最後の 0.4 s で跳ぶ):
    - カメラから C まで 6.01 m。
    - 画面に入るか: キャッチ点・最後の 0.6 s のディスク・最後の 0.8 s の犬 (根元と頭) が、114 フレームすべて内側 80 % に入る。最悪の |ndc| は 0.63。
    - 横 5 m・C + 0.2 m・見る先 1.5 m だと、0.83 で 1 フレーム外れた。それより手前の犬 (10 m 以上後ろ) は対象にしない。
  - Unity の Video/2b は、シーンのディスクの値 (drag・lift) で同じ行を計算する。
- **後でパターンの再生役 (シーケンサー) が使う口** (ユーザーの「撮影パターン」2026-10-04。今は作らない):
  - 入口は `_SetShot(int mode, int dog, int spot)` の 1 つ。負の値は今のまま。同じモードでも新しい対象として始まる (カット・猶予なし)。
  - 読み取りの口: `IsOpen` / `CurrentMode` / `CurrentDog` / `ShotSerial` / `ShotKind`。
  - 出来事: `ShotLost` (対象を見失い始めた) / `ShotCatch` (キャッチ待ちで犬がくわえた) / `ShotReturned` (おかえりで運び終えた) / `ShotPassed` (通り過ぎが通過した)。
  - `shotListener` (任意の UdonSharpBehaviour) に `_OnVideoShot` を送る。
  - 再生役は、出来事か自分の時計で次の `_SetShot` を呼べばよい。例: パターン 1 = キャッチ待ち → `ShotCatch` → 犬追従。パターン 2 = キャッチ待ち → `ShotCatch` + 1.5 s → おかえり。
- **直したこと**:
  - (1) 通り過ぎの履歴 `_passLast` を、対象が変わるたびに捨てるようにした (開き直しやモードの入り直しで残っていた)。
  - (2) Verify で、配列は添字の前にすべて確かめる (短い・null の配列は例外ではなく FAIL の行になる)。
  - (3) 写真の面が Photo/1 で作り直されても、入口の 3 つのボタンは毎回、名前で探すか作り直し、最初の全面の重ね (Large か HelpPanel) の手前へ並べる。
  - (4) Video/2 の WARN「layer 4 に他の Renderer が 1 つ」は、写真のメニューの吸着の板 (`SnapPhoto`) が同じレイヤー 4 へ移ったため。意図どおり (自分たちのカメラには写らない) なので、名前で確かめて PASS の行にした。他の物がレイヤー 4 にあれば、今までどおり WARN。
- **ハーネス**:
  - V4 の通り過ぎ: 「自分から 30 m 以内」を「passFar + 5 m 以内」に直した (誤った FAIL)。
  - V5 は、本物の投げを 1 回する (ディスク自身の `_CheckThrow` → `_Launch` → `ReqCatch`。ClientSim は 1 人なのでディスクも犬も自分が Owner で、予定はこの端末で作られる)。キャッチ待ちを選んだ状態で投げ、予定と着地のどちらで置いたかを記録する。
  - 続けて、同じ飛行の間にディスク、運んで戻る間におかえり (`ThrowerId` = 自分)、ふれあい。最後に手元を見る: 洗い場に犬 0 が着いている状態を heap に書き、読み取りは本物 (最寄りの洗い場の経路 → 自分が呼んだ洗い場の経路)。
  - 届かなかった読み取りは `report.unproven` と結果の行 (`NOT_PROVEN=`) に出し、黙って PASS にしない。
  - この run で証明しないもの: 本物のこすり・本物の洗い場の呼び出し (heap に書いた状態)、他の人の投げ (離れた端末の着地の推定)、キャッチ待ちを選んだままの実際のキャッチの出来事。

- **V5 の 手元 の FAIL (run 20261003-184456、カメラが部位 0 から 2.82 m) を読んだ結果 (2026-10-04)**:
  - **製品の不具合**: `KH_DogWash.partBones` はコライダーの番号で並ぶ。`partColliders` / `partOfCollider` と同じ並び (KH_DogWash.cs:194-196)。一方 `_LastStrokePart` は部位の番号 0〜8 (`partOfCollider[colliderIndex]`、:1183・:1196)。だから `partBones[part]` は別の骨だった。直し方: その部位に当たるコライダーの骨を全部取り、位置の平均を使う (`PartPoint`)。
  - **2.82 m そのもの**: 製品もハーネスも同じ `partBones[0]` を見ていたので、この不具合は数字の原因ではない。カメラが目標に追いついていなかった差と読む。heap に「洗い場に着いた」と書いたので、犬 0 は実際には洗い場へ歩いている途中で、手元の構図は先取りが無く、位置の時定数は 0.6 s。ただし犬の速さを記録していなかったので、確定ではない。
  - **ハーネスを直した**: 次の 4 つを分けて見るようにした。部位の点 (コライダーの骨の平均) の照合、欲しい位置と部位の距離 (0.55〜0.75 m)、カメラと欲しい位置の差 (犬が止まっているときだけ 0.35 m 以内を求める)、犬の速さ・遮り・高さの記録。
  - **V5 の再実行が 1 回要る**: 製品の変更の確認と、2.82 m の原因の確定のため。

## 13. Codex 設計レビュー (2026-10-03) を受けた修正 — この節が §5 (と §1.3・§2.2・§4.1・§9.3 の該当行) より優先

W4 の実装はこの節を正本にする。用語は次のとおり。F = 撮る人、S = 撮られる人、seq = F の申し込みの番号。

### 13.1 責務と保証の範囲

- **許可の正本は S の箱の同期された許可表** (`filmGrantIds` / `filmGrantSeqs`)。
- F の `filmTargetId` / `filmSeq` は「使っています」という申告にすぎない。申告だけで許可は成り立たない。
- `filmAccepting` は受付の設定で、許可の代わりにはならない。
- **PlayerObject の Owner は本人から移らない** (VRChat 公式の Player Object 仕様。§1.3 の「確かめられなかった」を取り消す)。
  - `GetOwner(box) == S` の照合は「構成の異常を拒否する追加の検査」として残す。所有権の移転への対策とは書かない。
- **改造したクライアントは防げない。** 検査はすべて F の端末にあるので、改造した F は迂回できる。
  - S の画面の「撮影中」は、**F が同期した申告を表示しているもの**。実際に描画・録画しているかは S には分からない。仕様と UI の説明文にそう書く。
- §5.5 の「他人を読むのは `TryPersonPose` だけ」という検査は、**動画カメラのファイル (`KH_VideoCamera*.cs`) に限る**。
  - 既存のプレイヤー一覧・通知は、許可の前に距離や場所を読む仕様なので対象外。

### 13.2 撮影の回の状態 (F の端末。seq ごと)

| 状態 | 入る条件 | 出る条件 |
|---|---|---|
| **pending** (返事待ち) | `ReqFilm(seq)` を送った | 承認の返事 → grant-in-transit。断り・時間切れ (22 s)・取り消し・× / 対象変更・S の退室 → ended |
| **grant-in-transit** (承認は来たが表がまだ届いていない) | `AnsJoin(seq, 1)` を受けた (送り主 = S、seq 一致、ローカルの種類が撮影) | S の表に (F, seq) が届く → in-use。**10 s で届かない → ended** (RC-08 と遅延の期限) |
| **in-use** | 表に (F, seq) があり、F が申告を同期した | 取り消し・× / 対象変更・退室・表から (F, seq) が消えた → ended |
| **ended** | 上のどれか | 終わり。**同じ seq は二度と開かない** |

- 人を読む・人を見る構図で描いてよいのは **in-use のときだけ**。プレビューも同じ。
- **承認の返事と許可表の両方が必要。** 表だけを見て自動で始めることはしない (時間切れの後に表が届いても、ended の seq は開かない)。

### 13.3 優先の規則

- **× / 対象変更 / F の取り消しは、後から届く承認より優先する。** × と対象変更では、pending と in-use の両方をその場で ended にする。種類が撮影の保留だけを消し、合流・呼ぶの保留は消さない。既存の `SendCancelTo` + `ResetOut` に当たる処理。
- **S の取り消しイベント `FilmRevoked(seq)` は表示用ではない。** 次の 2 つを満たすときだけ、その seq を ended にする。
  - 送り主 = その回の S
  - seq = 保留中または使用中の seq
  - ended にした後は、後から届く承認でも古い同期値でも開き直さない。
  - 古い取り消し (違う seq) で新しい seq を止めない。
- **許可が切れたら、その場で止める。** `TryPersonPose` が false になった描画の回から次のことをする。
  - 人を読むモードは描かない。
  - **RecordSnap とモニターの画面から RT を外す** (古い絵を出さない)。
  - なめらかさ・注視点・速度・人の姿勢のキャッシュを捨てる。
  - 次は「人を読まない構図」の新しいフレームからカットで始める。
  - **3 秒の猶予・最後の構図の保持 (§4.1・§2.2) は犬を見失ったときだけ**で、許可には使わない。
  - おまかせのカットの最短時間、「2 回失敗まで待つ」、同じモード内の補間よりも、許可切れを優先する。
- **描く直前の再検査**: 人の位置を読む検査と、描いてよいかの検査を分ける。`TryPersonPose` を通った後、`cam.enabled = true` の直前にもう一度、状態が in-use かを見る。既存のワープも、動く直前に条件を見直している。

### 13.4 イベントごとの照合

すべて `[NetworkCallable]` で、最初に `IsOwner(箱)` を見る。送り主は `NetworkCalling.CallingPlayer` だけを信じる。

| イベント | 受け取る側 | 採る条件 |
|---|---|---|
| `ReqFilm(seq)` | S | 送り主は自分以外の有効な人。`seq > SeenSeq(送り主)`。受付・ブロック・休止・保留・枠の条件 (13.6) を **新しい申し込みにも、種類の置き換えにも** 当てる |
| `AnsJoin(seq, code)` | F | 送り主 = 保留中の相手、seq = 保留中の seq、ローカルの保留の種類 = 撮影。code 1 なら grant-in-transit |
| `EndFilm(seq)` | S | 表の行の F = 送り主、かつ seq が同じ行だけを消す |
| `FilmRevoked(seq)` | F | 送り主 = その回の S、かつ seq が pending / grant-in-transit / in-use のとき → ended |

- 許可・取り消し・設定の切り替えなど、ローカルの入口はすべて `_` で始める。

### 13.5 S の許可表 (4 枠)

- **書く順**: 同じ F の行があればそこを置き換える → 無ければ空いている行 → それも無ければ断る (code 2)。他の人の有効な許可は追い出さない。
- **掃除してよい行** (S の端末が 10 s ごとに見る):
  - F が退室した行。
  - F の申告が「別の相手」か「別の seq」になってから 10 s 続いた行。
  - 書いてから 20 s たっても F の申告が (S, seq) にならない行 (F が開始しなかった)。
  - **書いた直後の行は消さない。** F の申告をまだ受け取っていないことを、「終わった」とは扱わない。
- `filmSeq` は申し込みの seq をそのまま写した値。他の申し込みで変えない。カメラを開き直しても、番号の数え始めは戻さない。
- 退室 (`OnPlayerLeft`) では、その人の表の行・保留・人の姿勢のキャッシュを、はっきり消す。「入り直せば playerId が変わる」だけには頼らない。
- 同期配列 2 本は、宣言のときに長さ 4・値 -1 で初期化する。

### 13.6 合流と共用するもの・種類ごとに分けるもの

| 共用 (種類によらない) | 種類ごと |
|---|---|
| 通知の間隔 20 s、保留は 1 件、断り・時間切れの後は同じ相手へ 30 s 申し込めない、休止 10 分、この人から受けない (滞在中)、seq の採番、交差の処理 | 受付の設定 (`accepting` / `filmAccepting`)、結果の表示 (`joinResultCode` を種類で分ける)、取り消しの操作、送る側の期限と掃除 (KindFilm の 22 s と §13.2 の 10 s)、通知の形 |

- **種類を置き換えるときも、新しい種類の受付と枠を確かめる** (合流の保留中に、撮影を受けていない相手へ撮影が来た場合を断る)。
- 受付 OFF・休止・ブロックは **新しい申し込みだけに効く**。使用中の許可は消さない。消すのは S の [やめてもらう] だけ。
- 既存の、変える必要がある所 (W4 の対象):
  - 送る側の時間切れが KindJoin / KindCall だけ (Mailbox 680-708)。
  - 「呼ぶ以外は合流」と扱う取り消しの判定 (Pouch.Travel 140-150・640-646)。
  - 結果の表示 (Pouch.Travel 363-395・430-454)。
  - いいよの後 4 s の表示が、保留を消すと閉じる問題 (Mailbox 423-435、Notice 81-84)。結果の表示用の状態を分ける。

### 13.7 RC-08 (返事が届かない)

- 撮影では、S が先に許可表を書く。返事が届かなくても F は開始しない (13.2 の grant-in-transit から 10 s で ended)。
- 開始しなかった行は、S の側が 13.5 の 20 s の規則で消す。
- S の「撮影中」は、F の申告がそろうまで出さない。いいよの直後に無条件で「撮影中」と出すことはしない。

### 13.8 被写体の UI

- 手元メニューに「撮影を許した人」の一覧を出す (最大 4 行。開始を確認できていない行も含む)。行ごとに [やめてもらう] を置く。上段の 1 行 (保留 1 件専用) とは別にする。
- 「撮影中」は申告がそろった行だけ。説明文は「相手の画面からの申告です」とする。

### 13.9 検証

- Verify (Course/85 と Video/2) は **すべての partial ファイル** (`KH_TravelMailbox*.cs`、`KH_VideoCamera*.cs`) を読み、ネットワークの口・ローカル専用の口・同期変数を検査する。今の `CheckNetworkEntries` は本体の `.cs` しか読まない。
- ハーネス V6 に足すケース: 遅れて届く承認、返事と取り消しの順番が逆、× の直後の承認、取り消した後の同じ相手への申し込み直し、5 人目、申告がそろう前の掃除。
- 2 回/s は、届く時刻の保証ではない。上限を超えたイベントは待ち行列に入り、他のイベントに追い越されることがある。どの規則も、届く順番に頼らない。「取り消しが効くまで 1 s」は、計測するまで目安として書くだけ (保証値にしない)。同期の量は `OnPostSerialization.byteCount` で確かめる。
- 実機: **Quest (VR) と Android / iOS のタッチを分けて**、承認と取り消しを確かめる。

### 13.10 録画の板 (W1 で実施済み)

- レイヤー 4 (Water) を 1 か所で決めた。自分たちのカメラは描かない。Verify はレイヤーとマスクの組を見る。
- renderQueue は 5001。ON の間は VRChat カメラの描画をこの映像が取る。
- `_VRChatCameraMode` はスクリーンショットも含む。パネルの文に書いた。
- × で古い映像を残さない。
- 詳しくは「W1 実装メモ」。手持ち・配信・Desktop の実際の出力と入れ子の描画は、まだ実機で見ていない。
