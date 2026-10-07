# 破壊監査 2026-10-07 — 機能分割 (_functions.md)

対象: HEAD `bfccaa1` (作業ツリーはクリーン)。公開 (2026-10-16) 前の総点検。全ランタイム機能を対象にし、9/28 の破壊監査 (水遊び・地図) と 10/1 の Udon 監査の後に変わった範囲 (洗い場・リード・10/3 の 11 件・自動写真・ポータル・夜モード) に重みを置く。
司令塔: Fable。Step 2 は Opus サブエージェント (FUNC ごと、4 並列 × 3 波)、FUNC-03 と FUNC-07 は Codex (`gpt-6-astra`) でも独立に監査する。全員読み取り専用 (出力は本ディレクトリの FUNC-XX.md だけ)。Unity・ハーネスは使わない。

## 事前検査

`node .claude/skills/vrc-review/scripts/run-static-checks.js . <dir>` (2026-10-07):

| 対象 | 結果 |
|---|---|
| Assets/KominkaHillside/Scripts | 51 ファイル、`UDON-OUT-REF-ARRAY` 0 件、exit 0 |
| Assets/KominkaHillside/Imported/KominkaDogRun/Scripts | 96 ファイル、0 件、exit 0 |

報告: `Logs/vrc-world-tools/udon-out-ref.json`。字句走査であり生成コードの証拠ではない。各 FUNC の Udon 変換境界は NOT_TESTED (生成コード未取得)。

## FUNC 一覧

| FUNC | 機能 | 観点 | 読むコード (Assets/KominkaHillside/ 基準) |
|---|---|---|---|
| FUNC-01 | リード散歩 (接続・左右上限 3+3・解除・退出・途中参加・紐の表示・判断の間引き) | N | Scripts/KH_LeashManager.cs, KH_LeashCollar.cs, KH_LeashClip.cs, KH_LeashHaptics.cs, Imported/KominkaDogRun/Scripts/KD_DogWanderController.HillsideLeash.cs, KD_DogWanderController.Tick.cs, KD_DogWanderController.Performance.cs, KD_TreatPouch.Walk.cs, KD_DogWanderController.HillsideTreatCall.cs (おやつで接続犬を呼ぶ) |
| FUNC-02 | 犬の基本行動 (Wander の状態機械・停止アンカー・休憩・寝る・挨拶・撫で・群れ・Owner 権威) | S | Imported/KominkaDogRun/Scripts/KD_DogWanderController.cs, .Shared.cs, .Rest.cs, .Sleep.cs, .Greeting.cs, .Nudge.cs, .Play.cs, .Treat.cs, KD_DogStopAnchor.cs, KD_DogPack.cs, KD_DogPetController.cs, KD_SleepSlot.cs, KD_SleepBoard.cs, KD_SleepPlacement.cs, KD_GroupActivities.cs, KD_CallPose.cs |
| FUNC-03 | 洗い場 (汚れの蓄積と同期・道具・洗う/すすぐ・台の高さ・呼ぶ/留める/出る・ボードの表示・リセット・2 人協力・4 か所) | C | Scripts/KH_DogWash.cs, KH_DogWash.Photo.cs, KH_WashTool.cs, KH_WashToolState.cs, KH_WashStationCall.cs, KH_WashStationBoard.cs, KH_WashStationReset.cs, KH_WashPersonalFloor.cs, KH_HandWash.cs, Imported/KominkaDogRun/Scripts/KD_DogWanderController.HillsideWash.cs, KD_DogWanderController.HillsideStationLeave.cs |
| FUNC-04 | フリスビー (投げ・曲げ (ローカル)・取り方 (トレーナー特典)・6 匹の計画・キャッチ・非マスターの投球・ドボン) | N | Scripts/KH_Frisbee.cs, KH_FrisbeeCatch.cs, .Planner.cs, .Motion.cs, .Water.cs, .Diagnostics.cs, .Probe.cs, KH_FrisbeeLauncher.cs, Imported/KominkaDogRun/Scripts/KD_TreatPouch.Toy.cs, KD_TreatPouch.ToyTop.cs, KD_ToyBox.cs |
| FUNC-05 | ボールの取ってこい・おもちゃの回収 (川の帯・岸戻し・浮き・水中回収) | S | Imported/KominkaDogRun/Scripts/KD_DogFetchController.cs, .Owner.cs, .Greeting.cs, .PetSync.cs, .HillsideWater.cs, KD_BallController.cs, Scripts/KH_ToyRecall.cs, KH_RiverData.cs |
| FUNC-06 | 川遊び (入水・浅瀬・泳ぎ・出水・復旧・水場・リード中の水の拒否) | S | Imported/KominkaDogRun/Scripts/KD_DogWanderController.HillsideWater.cs, Scripts/KH_WaterSpot.cs, KH_LeashManager.Water.cs, KD_TreatPouch.Water.cs |
| FUNC-07 | 地図・ワープ (1〜5)・合流 (受付箱・通知)・接続犬の同行・ポータル (古民家へ)・散歩の目的地と通過点 | N | Scripts/KH_LeashManager.Travel.cs, KH_TravelMailbox.cs, KH_TravelNotice.cs, KH_TravelFade.cs, KH_PortalBoard.cs, KH_WalkRoute.cs, KH_WalkStopovers.cs, Imported/KominkaDogRun/Scripts/KD_DogWanderController.HillsideTravel.cs, KD_TreatPouch.Travel.cs |
| FUNC-08 | 手元メニュー・設定・言語・台帳 (ポイント/トレーナー/購入/PlayerData 保存)・おやつ・飾り・店 | N | Imported/KominkaDogRun/Scripts/KD_TreatPouch.cs, .Settings.cs, .Confirm.cs, .Redraw.cs, .Hillside.cs, .Treat.cs, .Acc.cs, .PetFx.cs, .Sleep.cs, KD_MenuSettings.cs, KD_Language.cs, KD_ShopKeeper.cs, Progress/KD_ProgressLedger.cs, KD_ShopCatalog.cs, KD_ShopItem.cs, KD_ShopShelf.cs, KD_GiftCodePanel.cs, KD_PlayerFxTag.cs, KD_TreatBox.cs, KD_TreatSlot.cs, KD_TreatController.cs, KD_TreatFx.cs, KD_AccessoryBox.cs, KD_PetFxBox.cs, Scripts/KH_TrainerPass.cs, KH_MenuOpener.cs, KD_ActivitiesMenu.cs, KD_DogBook.cs, KD_HintDirector.cs |
| FUNC-09 | 自動写真・ギャラリー・ビデオカメラ・フィルムドローン・写真ボード | L | Scripts/KH_VideoCamera.cs, .Controls.cs, .Modes.cs, .ModesPlay.cs, .ModesWalk.cs, .People.cs, .Subjects.cs, KH_VideoCameraHook.cs, KH_PhotoAlbum.cs, KH_PhotoWalkWatcher.cs, KH_FilmDrone.cs, KH_TravelMailbox.Film.cs, Imported/KominkaDogRun/Scripts/KD_PhotoDirector.cs, .HillsideWalk.cs, .HillsideWash.cs, KD_PhotoCapture.cs, KD_PhotoBoard.cs, KD_PhotoFrame.cs, KD_PhotoHelp.cs, KD_PhotoSettings.cs, KD_PhotoViewer.cs, KD_GalleryController.cs, KD_BedroomPhotoDirector.cs, KD_TreatPouch.Photo.cs, KD_TreatPouch.Film.cs |
| FUNC-10 | ライブ・キャンディ犬・夜モード/オートナイト・BGM・環境音・距離フェード・名札 | L | Imported/KominkaDogRun/Scripts/KD_LiveSystem.cs, .Candy.cs, .Mount.cs, KD_CandyDogPerformance.cs, .Choreography.cs, .CostumeGlow.cs, .Live.cs, .Mount.cs, KD_DogWanderController.Candy.cs, .CandyMount.cs, .CandyMountLive.cs, .Live.cs, KD_LiveAccess.cs, KD_LiveNight.cs, KD_TreatPouch.Live.cs, KD_BgmPlayer.cs, KD_SleepAudio.cs, KD_DistanceFadeSettings.cs, KD_PlayerDogTag.cs, KD_DogPovCamera.cs, Scripts/KH_LiveNight.cs, KH_NightMode.cs, KH_AutoNight.cs |
| FUNC-11 | 横断: 同期・Owner・途中参加・退出・Owner 移譲・間引き (Tick/Performance) の整合 | N | 全ファイルの OnDeserialization / OnOwnershipTransferred / OnPlayerJoined / OnPlayerLeft / RequestSerialization / SetOwner / SendCustomNetworkEvent を横断。特に KD_DogWanderController.cs, .Tick.cs, .Performance.cs, KD_DogFetchController.cs, KH_LeashManager.cs, KH_DogWash.cs, KH_TravelMailbox.cs, KH_Frisbee.cs, KH_ToyRecall.cs |
| FUNC-12 | 横断: 長時間稼働・大人数 (16〜) ・GC・タイマー・配列上限・表示名フォント・翻訳 13 言語 | L | 全ファイルの SendCustomEventDelayed* / Update 内の確保 / 文字列連結 / 固定長配列 (walkers, players, slots) / PlayerData / Instantiate。特に KD_TreatPouch.*, KH_TravelMailbox.cs, KH_PhotoAlbum.cs, KD_DogWanderController.Performance.cs, KD_Language.cs |

| 観点 | 該当 FUNC | 件数 |
|---|---|---|
| N (Network) | 01, 04, 07, 08, 11 | 5 |
| C (Concurrency) | 03 | 1 |
| S (State) | 02, 05, 06 | 3 |
| L (Load/Lifetime) | 09, 10, 12 | 3 |

## 既存の検査との対応

| 検査 | 内容 | 関連 FUNC |
|---|---|---|
| Course/100 (ClientSim 全体 80 PASS / 2 SKIP、10-03) | 散歩・ワープ・洗い場の呼び出し等の一括 | 01, 03, 07 |
| Wash/12 (Verify 5316) | 洗い場の配線 | 03 |
| Photo ハーネス 6/6 | 自動写真 | 09 |
| フリスビーハーネス (既定表 26 投げ × 6 犬種) | 投げと取り方 | 04 |
| 9/28 破壊監査 FUNC-01〜07 | 水遊び・地図 (当時の版) | 05, 06, 07 |
| 10/1 Udon 監査 (fetch/frisbee/wander/wash/petleash/walk) | Udon 制約 | 02, 03, 04, 05 |

## 波の割り当て

| 波 | FUNC | 担当 |
|---|---|---|
| 1 | 01, 02, 03, 04 | Opus ×4 + Codex (03) |
| 2 | 05, 06, 07, 08 | Opus ×4 + Codex (07) |
| 3 | 09, 10, 11, 12 | Opus ×4 |
