> 2026-10-12 の静的監査 (Opus サブエージェント、統括 Fable が上位 5 件を現物で検証)。[VER-ENV-002](../development/issues/VER-ENV-002-scenery-performance.md) の 2026-10-12 節から参照する。**計測ではない。** 対となる描画・UI・音・同期側は [2026-10-12-performance-inventory-scene.md](2026-10-12-performance-inventory-scene.md)。10-04 の前回分は [2026-10-04-performance-inventory.md](2026-10-04-performance-inventory.md)。

# Udon の毎フレーム処理の静的監査 (2026-10-12、91b3fe3..HEAD 3e01e11)

**これは計測ではない。** コードと git 差分を読んだ結果と、そこからの概算。Unity・Play・スクリプトは使っていない。

## 前提と数え方

- 場面: Quest (Android ビルド)、VR、1 人 (= 全オブジェクトの Owner)、スポーン付近に立っている、犬 6 匹は自由、カートは全車プール (誰も乗っていない)、洗っていない、リードなし。
- 「extern」= Udon VM の EXTERN 1 回。U# では四則演算・比較・Vector3 の成分の読み書き・配列の要素の読み書き・UnityEngine.Object の null 比較も extern。他の UdonBehaviour のメソッド / プロパティは SetProgramVariable (引数ごと) + SendCustomEvent + GetProgramVariable (戻り値) の 2〜4 回 + 相手側の本体。数は ±50% 程度の目安。
- 「1 フレームあたり」は **20 fps (1 フレーム 50 ms) で平均した値**。時間で回る周期処理 (0.15〜1 秒ごと) は、fps が下がるほど 1 フレームあたりの負担が増える (1 秒あたりは同じ)。毎フレーム型は fps によらない。40 fps の値は周期型をほぼ半分にしたもの。
- 個数はユーザーの census (`Temp/kh-census-udon-summary.txt` (git 管理外)) の値を使った。
- extern 1 回の Quest 上の時間は測っていない。**仮に 1〜2 µs/回とすると**、1,000 extern/フレーム ≈ 1〜2 ms。

## 結論 (先に)

1. 91b3fe3 以降に増えた Udon の待機コストは、20 fps 換算で **約 +900〜1,200 extern/フレーム** (10-04 の固定費 約 500〜800 のほぼ 2 倍)。0.5 秒ごとに **約 4,000 extern の塊**が 1 フレームに乗る (KH_LeashManager.InvitationTick)。
2. 最大の新規分は **リード掛けの「誘い」(IMP-DOG-045)**。マネージャーの 0.5 秒ごとの処理 (80 要素の配列ループ × 5 本 + 犬 6 匹の可否判定) と、犬が行動を選び直すたびに走る `UpdateInvitation` (誘いが無いことを確かめる前に、他の Behaviour を 30 回前後呼ぶ可否判定を通る)。どちらも「誘いが無い」なら結果を変えずに省ける。
3. 次は洗い場の新しい部品: `KH_WashSupportVisual` ×4 が毎フレーム LateUpdate (Transform 書き込み + BlendShape 書き込み、止まらない)、`KH_WashSelection` ×4 と `KH_WashSelectionBoard` ×4 が 0.25 秒ごとの自己再帰 (距離の条件なし)、`KH_WashTool` が 19 → 47 個 (0.5 秒ごとの見回りは 1 個あたりほぼ同じ)。
4. カート (`KH_CartManager`) は全車プール中でも毎フレーム約 75 + 周期処理。軽いが 0 ではない。KH_CartDock / Controls / Seat / StopButton / StationPanel は待機中は 0 (毎フレームの入口なし、文字の書き込みは変化時だけ)。
5. `KH_WalkIntro` は終わった後も 0.15 秒ごとに回り続ける (約 200 extern/回)。**一度もリードをつないでいない人がスポーン付近にいる間は 1 回 約 2,000 extern** (20 fps 換算 約 700/フレーム)。
6. 犬 (`KD_DogWanderController`) の Update / LateUpdate の脚のフレームは、91b3fe3 から実質変わっていない (足したのは自分のフィールドの比較だけ)。カート追従・川のリードは「リード中」の枝の中だけで、待機中は通らない。
7. PC 専用の `KH_VideoCamera` は Android で Start が自分を止め、再び有効にする経路も `_supported` (Android では false) で閉じている。カートのドック・操作帯からも Android では呼ばれない。
8. **見立て**: Udon の増分は本物で、Quest で 1〜3 ms/フレーム程度 (仮定の換算) と 0.5 秒ごとの数 ms の段差。ただし 40 → 20 fps (+25 ms) をこれだけで説明するには足りない可能性が高い。描画側 (scene-audit) と合わせて、計測で切り分けるべき。

---

## 機能ごとの詳細

### 1. カート (Cart/*、すべて新規)

| スクリプト (個数) | 入口 | 待機中の処理 | 1 フレームあたり (20 fps) |
|---|---|---|---|
| `KH_CartManager` (1) | Update (`KH_CartManager.cs:164`) | MotionTick + LocalTick 毎フレーム、OwnerTick 0.25 秒、LocalTick の遅い枝 1 秒 | 約 75 毎フレーム + 周期 約 55 = **約 130** |
| `KH_CartDock` (8) | LateUpdate (`KH_CartDock.cs:52`) | Start で `enabled = false` (乗っていない間) (`:36-40`) | 0 |
| `KH_CartSeat` (8) | なし | ApplyCore (1 秒ごと) から `_SetRider` / `_ApplySeat` / `_SetParked` が呼ばれる。どれも変化時だけ書く | 0 (呼ばれる分はマネージャー側に計上) |
| `KH_CartControls` (8) | なし | `_Refresh` は `_SetRider` から。`manager._MyCart()` を呼び返して `_SetVisible(false)` が即 return | 0 |
| `KH_CartStopButton` (16) | なし | `_SetOn` / `_SetAccepting` は変化時だけ | 0 |
| `KH_CartStationPanel` (13) | なし | `_Refresh` は `RefreshPanels` (版・言語・行き先が変わった時) だけ。遅延は結果表示の消去 1 回だけ (`:144`) | 0 |

**KH_CartManager の中身 (全車プール時、確認済み)**

- `Update` (`KH_CartManager.cs:164-173`): `_started` → `MotionTick()` → `LocalTick()` → 毎フレーム `Networking.IsOwner(gameObject)` (時刻の比較より先) → 0.25 秒ごとに `OwnerTick()`。
- `MotionTick` (`KH_CartManager.Motion.cs:138-158`): `EnsureRoute()` (キャッシュ後は `_useTrack` を見て true、約 2)、`Networking.GetServerTimeInMilliseconds()` 1 回、`Time.deltaTime`・`Time.time`、8 台のループ (`c < carts.Length` を毎回読む、`_shown[c]`、`_parked[c]`)。駐車済みなら `ParkCart` は呼ばれない。**約 55〜60 extern/フレーム**。ParkCart は台ごとに 1 回だけ。
- `LocalTick` (`KH_CartManager.cs:444-507`): 待機中はフラグの比較 7〜8 個と `_IsRiding()` で **約 12**。1 秒ごとに `language.CurrentLanguage` (他 Behaviour)、`ApplyCore()`、`_MyDestination()` (→ `leash._MyChoice()`)。パネルの書き直しは変化時だけ (2026-10-12 の削減が効いている)。
- `ApplyCore` (`KH_CartManager.cs:282-397`, 1 秒ごと): 8 台 × (`CartActive`・駐車の比較) + 末尾のループで 8 台 × (`_SetRider` → 停止ボタン `_SetOn`・受付 `_SetAccepting`/`_SetOn`・`dock._SetRiding`・`controls._Refresh` (→ `manager._MyCart()`)、`_ApplySeat`、`_SetParked`) = 他 Behaviour 呼び出し 約 10 回/台、+ `VoiceSync()`。**約 600〜800 extern/秒 (1 フレームに塊で)**。何も変わっていなくても毎秒全部呼ぶ (安全網)。
- `OwnerTick` (`KH_CartManager.Groups.cs:269-283`, 0.25 秒): `SettleArrival` × 8 グループ (Free なら即 false)、`TrafficTick` (8 グループ、Running 以外は即 continue)、1 秒ごとに `Maintain` (8 台 + 8 グループ)。**約 80〜120/回**。
- **1 台乗車中** (参考): 自分のカートは `PlaceCart` で毎フレーム `GroundAt` 2 回 (各 Raycast 1〜3 回)、同じグループの他のカートは 3 フレームに 1 回 `GroundAt` 1 回。`PlaceCart` 1 台 約 200〜250 extern + レイ。`_Spin` (他 Behaviour) を台ごと毎フレーム。KH_CartDock の LateUpdate が乗っているカートだけ有効 (メニューを開いていれば Transform 書き込み 3 回)。リード中の犬は判断ごとに `CartHolderRiding` (`RiderCart` の 8 台ループ) と `CartFollow` (`_DogAnchor` ×1〜3、`_CartForward`、`_CartSpeed`) で 1 匹 約 200〜300 extern/判断。

### 2. 洗い場の道具・部品

| スクリプト (個数) | 入口・周期 | 待機中 1 回 | 1 フレームあたり (20 fps) | 10-04 比 |
|---|---|---|---|---|
| `KH_WashSupportVisual` (4) | **LateUpdate 毎フレーム** (`KH_WashSupportVisual.cs:15`)、止まらない | 約 35 + Transform 書き込み 1 + `SetBlendShapeWeight` 1 | **約 140** (fps によらない) | NEW |
| `KH_WashTool` (47) | 遅延イベント `_ToolIdle` 0.5 秒 (`KH_WashTool.cs:534-552`)、道具ごとに乱数の位相 | 約 25〜40 + イベント 1 回 | **約 120〜190** (40 fps なら 60〜95) | GREW (19 → 47 個) |
| `KH_WashToolState` (47) | なし (イベント駆動) | — | 0 | NEW (0) |
| `KH_WashSelection` (4) | 遅延 `_Poll` 0.25 秒の自己再帰 (`KH_WashSelection.cs:72-73`)、止まらない | 約 90〜110 (`_Ready()` の null / 長さ比較 約 45 + `_Selected` ×4) | **約 80** | NEW |
| `KH_WashSelectionBoard` (4) | 遅延 `_Tick` 0.25 秒の自己再帰 (`KH_WashSelectionBoard.cs:45/46-75`)、距離の条件なし | 約 80〜110 (言語、`selection.revision`、`selection._CategoryHeld` → 各スロットの `_HeldNow()`、`_ShownItem` ×3、`actionButton.interactable` 書き込み) | **約 80** | NEW |
| `KH_WashStationCall` (4) | Update (`KH_WashStationCall.cs:547-561`) | 約 10〜12 (`StandStep` のキー比較と `minLift` 比較が増えた) | 約 45 | GREW (+約 25) |
| `KH_WashFinishFx` (6) | なし (取りこぼしの再試行 1 回だけ) | — | 0 | NEW (0) |
| `KH_WashStationBoard` / `PersonalFloor` / `Reset` / `KH_HandWash` | 91b3fe3 から変更なし | — | 10-04 と同じ | UNCHANGED |

**(b) KH_WashSupportVisual.LateUpdate → `_RefreshVisual()` (確認済み)**: 毎フレーム 4 個とも、`tableCollider` / `sleeves` / `transform.parent` の null 比較、`tableCollider.center`・`size`、`tableCollider.transform.TransformPoint`、`transform.parent.InverseTransformPoint`、`transform.localPosition = …` (毎フレーム書き込み)、`sleeves.SetBlendShapeWeight(…)` (毎フレーム書き込み) (`KH_WashSupportVisual.cs:17-27`)。台が止まっていても同じ値を書き続ける。台の高さ・大きさを動かすのは `KH_WashStationCall.Update` (`liftRoot.localPosition`、`_StandPose`) と `KH_WashSelection._Apply` (こちらは既に `support._RefreshVisual()` を直接呼んでいる、`KH_WashSelection.cs:186`)。
- 未確認 (描画側): 同じ値の `SetBlendShapeWeight` が毎フレーム SkinnedMeshRenderer の再スキニングを起こすかどうか。Transform の毎フレーム書き込みは TransformChanged の通知を出す。

**(c) KH_WashTool ×47 (確認済み)**: Update は無い (IMP-DOG-036 S5)。置いてある道具は `_ToolIdle` が 0.5 秒ごとに `IdleStep()` (`KH_WashTool.cs:575-583`): `TrackHolder()` (`_pickup.currentPlayer` + IsValid、約 5)、`ReturnTick()` (本番値 `returnSeconds <= 0` で約 6 で戻る)、`_IsSprayer()` なら `SprayChecks()` → `Want()` (`selectionActive`・`_IsSprayer`・`state.spraying` (他 Behaviour)・`pulseSeconds`・`_shotShownUntil`) → `ApplySpray` (変化なしで即 return) → `state.spraying` をもう一度。91b3fe3 からの変化は `tool == ToolShower` → `_IsSprayer()` (`:578`) と `Want()` の伸び (`:359-366`) で、**シャワー以外 (ホース・ピストル・じょうろ) も SprayChecks に入るようになった**。置いてある間の Transform 書き込みは無い (`ReturnHome` は returnSeconds > 0 のときだけ)。選ばれていない premium スロット (`selectionActive = false`) も同じ周期で回る。
- scene-audit からの共有 (シーン側): 47 個のうち 28 個 (10-06 以降に足した premium スロット) の UdonBehaviour の同期方式が Continuous、19 個が None。スクリプトは `NoVariableSync` (`KH_WashTool.cs:43`)。同期変数が無いので Udon の extern は増えないが、Continuous の Behaviour 28 個ぶんの直列化の手間が Owner (1 人なら自分) に乗る可能性がある (規模は未確認)。スクリプトの宣言に揃えるのが筋。

### 3. 犬

| スクリプト (個数) | 毎フレームの経路の変化 | 判断の経路の変化 | 1 フレームあたり (20 fps) |
|---|---|---|---|
| `KD_DogWanderController` (6) | 実質なし | `UpdateInvitation` が選び直しのたびに走る | 10-04 の 約 150〜350 + **約 150〜250** |
| `KD_DogFetchController` (6) / `KD_DogPetController` (6) / `KH_FrisbeeCatch` (6) / `KH_Frisbee` (6) / `KD_DogStopAnchor` (6) / `KD_BallController` (6) | 91b3fe3..HEAD で変更なし | — | 10-04 と同じ |
| `KH_DogWash` (6) | 待機の判定に `_dfCount == 0` が 1 つ増えただけ (`KH_DogWash.cs:2059`) | — | 約 100 (判定 約 16 × 6)。UNCHANGED |
| `KD_CallPose` (1) | 呼んでいない犬は `!slot.IsSleeperLocal` で即 return。足した `CalledTreatActive` はその後ろ (`KD_CallPose.cs:218-221`) | — | UNCHANGED |
| `KD_SleepSlot` (6) | Update は同じ。0.5 秒の `Watch` に到着待ちの時間切れ (呼ばれている時だけ) (`KD_SleepSlot.cs:348-357`) | — | UNCHANGED |

**(a) KD_DogWanderController.Tick.cs の Update / LateUpdate に 91b3fe3 以降足されたもの (全部、確認済み)**

| 場所 | 追加 | 待機中の判定 |
|---|---|---|
| `Tick.cs:165` `RestThinRecord` | `_waterLeashVeto` → `_lrState != LrOff` | 自分のフィールド (判断のフレームだけ) |
| `Tick.cs:395` `TickBusy` (脚のフレームごと) | `_waterLeashVeto` の行を削除 | — |
| `Tick.cs:413` `TickBusy` | `if (_selectedBehavior == BehaviorInvitation) return true;` | 自分のフィールド 1 比較。他 Behaviour の読み取りより前 |
| `Tick.cs:462` `LeashLegsOk` | `if (_lrState != LrOff \|\| _lrNear) return false;` | リード中の犬だけ。自分のフィールド |
| `KD_DogWanderController.cs:1836` LateUpdate | `if (_wpKind == 1 \|\| _wpJawWrote) WaterPlayJawLate();` | 自分のフィールド 2 比較。洗い場の水遊びの時だけ中へ |
| `Performance.cs:52` `ContinueSelectedBehavior` | `case BehaviorInvitation: return false;` | 誘い中だけ |

- **カート追従** (`HillsideLeashCart.cs`): 呼び出しは `UpdateHillsideLeashFull` の中 (`HillsideLeash.cs:374, 461-462`) だけ。リードが無い犬は `_IsLeashed` で手前で return (`HillsideLeash.cs:276-288`、91b3fe3 と同じ)。毎フレームの経路には無い。
- **川のリード** (`HillsideLeashRiver.cs`): `UpdateLeashRiver` は同じくリードの枝の中 (`HillsideLeash.cs:399`)。`WaterTick` の変更 (`HillsideWater.cs:359, 380`) は自分のフィールドの置き換えだけ。
- **呼ばれた犬のおやつ** (`HillsideCalledTreat.cs`): `UpdateSleep` の中 (`Sleep.cs:101, 182, 220`) で、呼ばれている犬だけ。
- **誘い** (`HillsideInvitation.cs`): `UpdateTick` の選び直し (10 Hz 上限) の中、`UpdateStationLeave` の後 (`KD_DogWanderController.cs:1514`)。待機中は毎回ここまで来る。

**UpdateInvitation の待機中の費用 (確認済みの順序、費用は概算)**: `HillsideInvitation.cs:32-38`

```csharp
if (hillsideLeashes == null || dogIndex < 0 || hillsideLeashes.inviteSpots == null
    || !_InvitationAvailable() || !hillsideLeashes._IntroDogAvailable(dogIndex))
{ _invitationStation = -1; return false; }
int station = hillsideLeashes.inviteDogStation[dogIndex] - 1;
...
if (station < 0 || now >= hillsideLeashes.inviteUntil[station]) { _invitationStation = -1; return false; }
```

- 「この犬に誘いが来ているか」(`inviteDogStation[dogIndex]`、同期配列 1 回の読み取り) を見るのは、**高い可否判定 2 つの後**。
- `_InvitationAvailable()` (`:13-28`): 他 Behaviour の読み取り 約 18 回 (`fetchController.IsIdle` / `PetActive` / `WaterPose`、`frisbeeCatch.IsIdle`、`WashHeld` → `wash._WashHold()` (サーバー時刻を読む) + `HeldStation()` (4 か所の走査)、`petController.IsPetting` / `IsShaking`、`treatBox.LiveSlotOf`、`sleepBoard._IsCalled`、`liveSystem.IsPlaying`、`activities.Mode` 最大 3 回 …)。約 120〜150 extern。
- `_IntroDogAvailable(dog)` (`KH_LeashManager.Intro.cs:23-33`): `fetchers[dog].IsIdle`、`_IsGathering` (`activities.Mode`)、`frisbeeCatchers[dog].IsEngaged`、`sleepBoard._IsCalled`、`WashHeld(dog)` (→ 犬の `WashHeld` をもう一度) で 約 80〜120。
- 合計 **1 回 約 200〜270 extern**。待機中、選び直しは判断ごと (20 fps では休む犬 9 フレームに 1 回、歩く犬 4 フレームに 1 回) なので、6 匹で 1 フレームに約 1 回 → **約 150〜250 extern/フレーム**。40 fps では約 0.6 回/フレーム → 約 120〜160。

### 4. リード

| スクリプト (個数) | 入口・周期 | 待機中 | 1 フレームあたり (20 fps) | 比較 |
|---|---|---|---|---|
| `KH_LeashManager` (1) | Update (`KH_LeashManager.cs:460`) | 毎フレームは `TravelTick` (状態 0 で即 return) + 時刻比較 (変更なし)。**0.5 秒ごとの保守に `InvitationTick()` が増えた** (`:469`) | 毎フレーム 約 10 + **約 4,000 / 0.5 秒 = 平均 約 200 (塊で)** | GREW |
| `KH_WalkIntro` (1) | Update (0.15 秒ごとに Tick) + PostLateUpdate (`KH_WalkIntro.cs:116-159`) | 終わった後: Tick 約 200 / 回、PostLateUpdate は `_state == Quiet` で即 return | **約 75** (終わった人) / **約 700** (導入中) | NEW |
| `KH_LeashClip` (6) | 遅延 `_ClipTick` は持っている間だけ (`KH_LeashClip.cs:72-84`) | 0 | 0 | UNCHANGED |
| `KH_LeashCollar` (6) | 遅延 `_RopeTick` はリード中だけ | 0 | 0 | UNCHANGED |

**(f) InvitationTick (`KH_LeashManager.Invitation.cs:108-174`、確認済み)**: 1 人 = Owner なので全部通る。
- ローカルのクリップ (`localClips`) の走査 (持っていなければ各 約 8)。
- `:133-137` 80 要素の `_inviteMasks` ループ (約 5 × 80 = 400)。
- `InvitationReportedMask` (`:87-96`) 80 要素 (double の引き算・比較込み、約 9 × 80 = 720)。
- `:143-149` 乗り場 3 × 80 要素の「見る人」探し (誘いが無いと最後まで回る、約 7 × 240 = 1,700)。
- `:150-172` 犬 6 匹 × (`_IntroDogAvailable` 約 100 + `inviteDogs[dog]._InvitationAvailable()` 約 140)。誘い (held) が 0 でも全部回る。約 1,400。
- 合計 **約 4,000 extern / 0.5 秒**、1 フレームにまとめて乗る (20 fps で 10 フレームに 1 回)。誘いが無ければ何も書かず、送信もしない (`changed` は false のまま)。
- Intro partial (`KH_LeashManager.Intro.cs`): 周期なし。`IntroChanged()` は接続・解除・受信の時だけ `walkIntro._OnLeashChanged` を呼ぶ。

**KH_WalkIntro (確認済み)**: Tick は**止まらない** (`_completed` でも 0.15 秒ごと)。
- 終わった人 (`_completed`、`_hidden`、`_dismissed`、入口から 10 m 外): `_OnLeashChanged()` (`manager.walkers` を要素ごとに GetProgramVariable、6 匹 × 2 ループ 約 105)、言語 (他 Behaviour)、位置、`manager._IntroStateReady()`、`Pause()` → `HideVisuals()` (SetActive は変化時だけだが、`activeSelf` を約 10 個読む) で **約 200 / 回**。
- 導入中 (一度もリードをつないでいない人が入口 10 m 以内、2 秒後から): クリップ走査 (各 約 40) + 犬 6 匹 × (`manager._IntroDogAvailable` + `manager._CanIntroAttach` ×2 (それぞれ中で `_IntroDogAvailable` をもう一度 + `_MyCount`)) で **約 2,000 / 回** → 20 fps で約 700/フレーム。IMP-UI-006 の静止画名 `take-spawn.png` から、入口はスポーンから見える位置と読める (距離は未確認)。新しい人は全員ここを通る。一度つなげば PlayerData に残り、以後は「終わった人」の経路。

### 5. おやつ

| スクリプト (個数) | 毎フレーム / 周期の経路 | 結論 |
|---|---|---|
| `KD_TreatBox` (1) | Update は `_nextReconcile` (4 Hz) と IsOwner の順で変更なし。差分は `NothingToResolve()` の後ろ (`KD_TreatBox.cs:382-412`) | UNCHANGED |
| `KD_TreatPouch` (1) | Update (`:1580`) / LateUpdate (`:1507`) は変更なし。追加の地面レイ (`PanelGroundLift`) は板を据えるとき (`PlaceInFront`) だけ。`_Refresh` の自己再帰は板が閉じていれば `Redraw` 冒頭で return | UNCHANGED |
| `KD_TreatSlot` (3) | Update 0.25 秒、`Out` の時だけ `_RefreshHandOffer()` が増えた (`KD_TreatSlot.cs:323`)。待機中は `SlotFree` で即 return | UNCHANGED |
| `KD_TreatController` (3) | `ApplySlot(false)` は Free / Returning で状態が同じなら即 return (`:177`)。追加の `OutMs` / `HandFedFor` はその後ろ | UNCHANGED |
| `KD_TreatFx` (1) | 差分は位置の出所だけ | UNCHANGED |

### 6. 撮影 (PC 専用)

- `KH_VideoCamera.Start` (`KH_VideoCamera.cs:408-427`): Android / iOS では `_supported = false` (`:411-413`)、カメラと板を止め、最後に `enabled = false` (`:427`)。PostLateUpdate (`:578`) は無効なので来ない。
- 再び有効にするのは `_Open` の `enabled = true` (`:468`) だけで、`_Open` は冒頭の `if (!_started || !_supported) return;` (`:435`) で Android では何もしない。`_ShowMonitor` → `_Recall`、`_HideMonitor` / `_MonitorHidden` は `_open` (false) で何もしない。
- `KH_CartDock._PressMonitor` (`KH_CartDock.cs:78-85`) はボタン操作のときだけ。`KH_CartControls._SetVisible` が Android ではモニターのボタンを隠す (`KH_CartControls.cs:88-91`)。乗車中のドックの LateUpdate はモニターの枝で `_monitor.activeSelf` (Android では常に false) を 1 回見るだけ。
- 結論: **Android で毎フレーム動く経路は無い** (確認済み)。

### 7. その他

| スクリプト | 結論 |
|---|---|
| `KD_DogBook` (1) | Update は同じ形 (`KD_DogBook.cs:158-165`)。0.5 秒ごとの `Refresh()` に距離・表示の条件が無い (元から)。`RefreshFur` (毛色の他 Behaviour 4 回 + 文字 2 本) と `StatusKey` の他 Behaviour 3〜4 回が増えた。Refresh 全体で約 600〜700 / 回 → 約 65/フレーム (うち増分 約 10〜15)。文字の書き込みは同じ文字列なので TMP が捨てる (scene-audit の指摘どおり) |
| `KH_FurColor` (1) | Update なし。`SetPropertyBlock` は値が変わった犬だけ (Start / 受信 / 操作) (`KH_FurColor.cs:137-161`)。毎フレームの材質書き込みなし。0 |
| `KD_LiveSystem` (1) | Update の追加行 (`:355-356`) は `liveId < 0 && _playingId < 0` の即 return (`:352`) の後ろ。待機中は不変 |
| `KD_ShopItem` (51) | Update も遅延ループも無い (購入・Interact・退室のイベントだけ)。0 |
| `KH_TravelNotice` (1) | Update は `!_showing` で即 return。差分は文言の出所だけ |
| `KH_WalkRoute` / `KD_PhotoDirector` (+2) / `KH_DogWash` の Photo | 毎フレームの経路は変更なし |
| 10-04 → 10-06 (01323a9..91b3fe3) | `KH_ToyRecall` の浮かせ (`_hoverAny` の旗で止まる)、`KH_TravelMailbox` の Update (待機の判定に 2 比較)、`KH_Frisbee` は待機中ほぼ不変 |

---

## 何もしていない状態で重い順 (20 fps 換算、概算)

| 順 | スクリプト (個数) | 1 フレームあたり (20 fps) | 40 fps | 中身 | 10-04 比 |
|---|---|---|---|---|---|
| 1 | `KD_DogWanderController` (6) | 約 300〜600 | 約 270〜510 | 既存の歩行・判断 + 選び直しごとの `UpdateInvitation` 約 200〜270 | **GREW** (+150〜250) |
| 2 | `KH_LeashManager` (1) | 約 210 (0.5 秒ごとに 約 4,000 の塊) | 約 110 | `InvitationTick`: 80 要素ループ × 5 本 + 犬 6 匹の可否判定 | **GREW (新規の塊)** |
| 3 | `KH_WashTool` (47) | 約 120〜190 | 約 60〜95 | 0.5 秒ごとの見回り × 47 | **GREW** (19 → 47 個) |
| 4 | `KH_WashSupportVisual` (4) | 約 140 | 約 140 | 毎フレームの LateUpdate、Transform + BlendShape 書き込み | **NEW** |
| 5 | `KH_CartManager` (1) | 約 130 (1 秒ごとに 約 700 の塊) | 約 100 | 8 台のループ毎フレーム、ApplyCore 1 秒、OwnerTick 0.25 秒 | **NEW** |
| 6 | `KD_DogFetchController` (6) | 約 100 | 約 100 | 休んでいても毎フレーム Owner 判定とボールの見張り | UNCHANGED |
| 7 | `KH_DogWash` (6) | 約 100 | 約 100 | 待機の判定 約 16 × 6 (+1 比較) | UNCHANGED (10-04 表には無し) |
| 8 | `KH_ToyRecall` (1) | 約 40〜130 | 同 | 0.25 秒ごとの 2 種の見回り | UNCHANGED |
| 9 | `KH_WashSelection` (4) | 約 80 | 約 40 | 0.25 秒の自己再帰、`_Ready()` の比較の列 | **NEW** |
| 10 | `KH_WashSelectionBoard` (4) | 約 80 | 約 40 | 0.25 秒の自己再帰、距離の条件なし | **NEW** |
| 11 | `KH_WalkIntro` (1) | 約 75 (導入中は 約 700) | 約 43 (約 350) | 0.15 秒の Tick が止まらない | **NEW** |
| 12 | `KD_DogBook` (1) | 約 65 | 約 33 | 0.5 秒ごとの Refresh (距離の条件なし) | GREW (少し) |
| 13 | `KH_FrisbeeCatch` (6) / `KD_DogPetController` (6) | 各 約 50〜60 | 同 | 10-04 と同じ | UNCHANGED |
| 14 | `KH_WashStationCall` (4) | 約 45 | 約 45 | 台の大きさのキー比較が増えた | GREW (+25) |
| 15 | `KH_Frisbee` (6) / `KH_WashStationBoard` (4) | 各 約 25 | 同 | 10-04 と同じ | UNCHANGED |
| — | Cart の Dock / Seat / Controls / StopButton / StationPanel、WashToolState、WashFinishFx、FurColor、ShopItem、LeashClip、VideoCamera (Android) | 0 | 0 | 毎フレームの入口なし / 自分で止まる | NEW (0) |

- 新しく毎フレーム Udon へ入る入口: `KH_CartManager.Update`、`KH_WalkIntro.Update` + `PostLateUpdate`、`KH_WashSupportVisual.LateUpdate` ×4 = **7 本/フレーム**。遅延イベントは 1 秒あたり WashSelection 16 + Board 16 + WashTool 94 (19 個のときは 38) = 126 回。
- 新規・増分の合計: 20 fps で **約 +900〜1,200 extern/フレーム**、40 fps で約 +600〜800。10-04 の固定費 (約 500〜800 + 入口 65) のほぼ 2 倍。

## 安く削れるところ (挙動を変えない候補。どれも未実装)

1. **犬の誘いの順序** — `KD_DogWanderController.HillsideInvitation.cs:32-35`: `inviteDogStation[dogIndex] - 1 < 0` (または `now >= inviteUntil[station]`) を `_InvitationAvailable()` / `_IntroDogAvailable()` より先に見る。どちらの順でも「`_invitationStation = -1; return false`」で同じ結果。約 150〜250/フレームが約 5 に。
2. **InvitationTick の空振り** — `KH_LeashManager.Invitation.cs:132-172`: 報告の表 (`_inviteMasks`) が全部 0 で `inviteHeldMask == 0`、全犬の `inviteDogStation == 0`、`now >= _inviteRebuildUntil` なら、80 要素のループ 3 本と犬 6 匹の判定を飛ばす (何も書かない結果は同じ)。表に入っている数を数えておけば 80 要素の走査も要らない。0.5 秒ごとの 約 4,000 が数十に。
3. **KH_WashSupportVisual** — `KH_WashSupportVisual.cs:15`: 台が動いた時だけ呼ぶ。`KH_WashStationCall.Update` の `liftRoot.localPosition` 書き込み (`KH_WashStationCall.cs:562`) と `_StandPose` (`:585-590`) の後に `support._RefreshVisual()` を呼び、LateUpdate は外すか `enabled = false`。最低でも、前回の `target` と同じなら書き込みを飛ばす。約 140/フレームと Transform / BlendShape の毎フレーム書き込みが消える。
4. **KH_WashSelection の poll** — `KH_WashSelection.cs:72-73`: `_Apply` の「持たれていて待つ」カテゴリが無ければ (`_shownTools[c] == _Selected(c)` が全部成り立てば) 再予約しない。`OnDeserialization` / `ReqSelect` / 道具の `OnDrop` から再開。`_Ready()` の結果も Start でキャッシュできる (配列は実行中に変わらない)。
5. **KH_WashSelectionBoard の tick** — `KH_WashSelectionBoard.cs:74`: `KH_WashStationBoard` と同じく近く (例 12 m) にいる時だけ 0.25 秒、遠ければ 1〜2 秒。
6. **KH_WalkIntro** — `KH_WalkIntro.cs:116-121`: `(_completed || _hidden || _dismissed) && !_replay && _state == Quiet` なら Tick を飛ばす (`_Replay` / `_OnLeashChanged` / 設定の操作で戻す)。導入中の経路は `_CanIntroAttach` の中の `_IntroDogAvailable` の二度引き (`KH_LeashManager.Intro.cs:18` と `KH_WalkIntro.cs:210, 215-216`) を 1 回にできる。
7. **KH_WashTool の選ばれていないスロット** — `KH_WashTool.cs:546-552 / 575-583`: `!selectionActive && returnSeconds <= 0` の道具は idle loop を止め、`_SetSelected(true)` (`:958-967`) で再開 (選ばれていない間は `_SetSelected(false)` が既に噴射を止めている)。何個が未選択かは実行時の状態次第なので、効果は未確認。シーン側の同期方式 (28 個が Continuous) も宣言 (`NoVariableSync`) に揃える。
8. **KH_CartManager** — `KH_CartManager.cs:167-172`: `Time.time >= _nextOwnerTick` を `Networking.IsOwner` より先に。`KH_CartManager.Motion.cs:140-151`: 表示中・駐車待ちの台の数を `ApplyCore` で数えておき、0 ならループを飛ばす。約 60/フレーム。
9. **KD_DogBook** — `KD_DogBook.cs:164-165`: 開いている (`detail` / `help` が active) か近い時だけ 0.5 秒ごと、そうでなければ 2 秒程度。

## 確かめたこと / 推定のこと

- **コードで確かめた**: 各入口の有無と早期 return の順序、自分で止まるか (`KH_CartDock`・`KH_VideoCamera`)、自己再帰の周期 (WashSelection / Board 0.25 秒、WashTool 0.5 秒、WalkIntro 0.15 秒、InvitationTick 0.5 秒、CartManager の OwnerTick 0.25 秒・ApplyCore 1 秒)、犬の Tick.cs / LateUpdate の 91b3fe3 以降の追加 (上の表で全部)、カート追従・川・呼ばれた犬のおやつがリード中 / 呼ばれた犬だけの枝にあること、Android でビデオカメラが動かないこと、待機中に文字の書き込みが無いこと (カート一式)。
- **推定**: すべての extern の数 (±50%)、選び直しの頻度 (`DecisionFrames` と 10 Hz の上限からの計算)、WalkIntro の入口とスポーンの距離 (静止画の名前からの推測)、Continuous 同期の直列化の負担、同じ値の `SetBlendShapeWeight` の描画側の負担、extern 1 回の Quest 上の時間。
- **見ていない**: 実行時の描画、Udon 以外の CPU (物理・アニメーター・スキニング)、他のプレイヤーがいる場合 (InvitationTick は Owner だけ重い、受信側の OnDeserialization)。
