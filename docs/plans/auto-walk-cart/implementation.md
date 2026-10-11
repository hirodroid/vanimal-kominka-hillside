# おさんぽカート 実装設計 M1 (2026-10-11)

親 Issue: [DES-WLD-004](../../development/issues/DES-WLD-004-auto-walk-cart.md)。外観は [制作票](asset-brief.md) / [本制作](production.md)。本書は**実装の契約** (クラス・フィールド・メソッド・メニュー名) で、実装担当 2 名 (Runtime / Editor) が同じ名前で並行して書くための正本。ユーザー了承 (2026-10-11「そちらで改善し、Unityへ取り込んで機能も組み込み」) に基づき、M1 の範囲を先に決める。

## 範囲

| 段階 | 入れるもの |
|---|---|
| **M1 (今回)** | 乗り場 5 地点 (既存ワープ先 0〜4) で開始・合流、犬の散歩設定 (`KH_LeashManager._MyChoice()`) を共用、周回 (順/逆) と目的地到着での終了、千鳥走行 (橋など `linkAllowsDetour=false` の区間は一列)、個人の停止・降車、マスターの参加順引き継ぎ、受付 ON/OFF (初期 ON・マスターだけ)、乗車中のワープ・合流・リスポーン・退出での降車優先、Late Joiner への整合、13 言語の固定文言、乗り場の注意書き、Editor の Build/Verify/Probe |
| **M2 (記録のみ、今回作らない)** | 対向グループの待機・先入優先・待避場所、くん活の抑制と犬の前方配置、掲示 (背面 Sign) の内容と手元パネル、速度・車間・横ずれ量の実機調整、現在地が目的地のときの案内の磨き込み、乗車中の撫で・おやつ、動画カメラとの併用確認 |

M1 でも「犬が同行する」のは既存のリード追従 (持ち主へ 7.2 m/s で追い、10 m 離れると瞬間移動) に任せる。カートの速度はこれより十分遅い (1.3 m/s)。カートから犬を動かすコードは書かない。

## 方式

- **動きは決定論**: グループごとに同期した錨 `(anchorDistance, anchorTimeMs, direction)` と共通速度から、各クライアントが毎フレーム `KH_WalkRoute.loopDistances` 上の距離 → 位置を計算する。VRCObjectSync は使わない。合流・離脱・到着・開始のたびに錨を打ち直す。位置でなく距離で追うので入口付近の始点/終点の近接 (ノード 0 と 435 が隣接) に惑わされない。
- **権威は 1 つ**: `KH_CartManager` (Manual)。`KH_LeashManager` と同じ流儀 (既定 Owner、`SetOwner` しない、`[NetworkCallable]` 要求を `NetworkCalling.CallingPlayer` で照合、同期配列は宣言時に初期化、Owner 自身の書き込みはローカルにも反映)。
- **カートはシーンオブジェクト** (Setup が FBX のメッシュから組み立て、Prefab インスタンスにしない → `vrc-udonsharp` §3 のプレハブ境界の罠を避ける)。待機中のカートは `SetActive(false)` にしない (同期とイベントが止まる)。Renderer を消して駐車位置に置く。
- **座席は VRCStation** (カート本体 GameObject に `KH_CartSeat` と同居。`OnStationEntered/Exited` を受ける)。ジャンプで降りられる状態を保つ (VR 酔いの退路)。降車の全経路 (停止ボタン・ジャンプ・ワープ・リスポーン・退出・到着) を 1 本の離脱処理に通す。
- **着座**: 乗り場パネルの出発/合流を押す → Owner が割当 → 同期で自分のカートに `cartRider == 自分` が来たら、そのクライアントが `station.UseStation(local)`。Owner 自身が要求者ならローカル反映の直後に同じ処理。
- **降車**: `station.ExitStation(local)` の前に、`Anchor_ExitL/R` のうち `NavMesh.SamplePosition` で立てる方を station の exit transform (`stationExitPlayerLocation`) に入れておく。

## 定数と既定値

| 名前 | 値 | 意味 |
|---|---|---|
| `MaxCarts` | 8 | プール台数 (= 最大同時乗車人数) |
| `MaxGroups` | 8 | グループ上限 (= MaxCarts) |
| `speed` | 1.3 m/s | 全員共通の巡航速度 (public、Setup は既定のまま) |
| `gap` | 3.0 m | 隊列の前後間隔 (経路距離) |
| `lateral` | 0.6 m | 千鳥の横ずれ。`linkAllowsDetour=false` の区間は 0 |
| `departDelayMs` | 3000 | 出発までの猶予 (着座してから動き出すまで) |
| `arriveSlack` | 0.5 m | 到着判定の余裕 |
| `groundMask` | `KH_LeashManager.TravelGroundMask` | 接地 Raycast |
| `smoothTime` | 0.25 s | 高さの平滑化 (`SmoothDamp`) |
| `pitchLimit` | 8° | 車体ピッチの上限。ロールは常に 0 |
| `lookAhead` | 1.5 m | ヨーを決める先読み距離 |

行き先の値 (既存): 0 入口 / 1 広場 / 2 周回 (順) / 3 周回 (逆) / 4 奥の広場 / 5 川辺。乗り場 (spot) 0〜4 は `KH_LeashManager.Travel.cs` の `spotPositions / spotYaws / spotNodes` と同じ番号 (0 古民家側の入口、1 広場、2 奥の広場、3 上流の橋、4 川辺)。

## Runtime (Assets/KominkaHillside/Scripts/Cart/)

名前空間は既存の `KH_*` と同じ (ファイル先頭の既存流儀に合わせる)。各 `.cs` の隣に `UdonSharpProgramAsset` (`.asset`) を Setup が初回に作る。

### `KH_CartManager` — `[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]`

partial: `KH_CartManager.cs` (フィールド・同期・適用) / `KH_CartManager.Requests.cs` (要求と裁定) / `KH_CartManager.Motion.cs` (距離→姿勢・毎フレーム) / `KH_CartManager.Groups.cs` (隊列順・引き継ぎ・到着)。

参照 (非同期。**既定は null**、Setup が配線):
```
public KH_WalkRoute route; public KH_LeashManager leash; public KominkaDogRun.KD_Language language; public KH_TravelFade fade;
public KH_CartSeat[] carts;            // MaxCarts 台
public KH_CartStationPanel[] panels;   // 5 枚 (spot 順)
public Transform parking;              // 待機カートを置く場所 (Setup が作る。地中/範囲外)
```
設定: `public float speed = 1.3f, gap = 3f, lateral = .6f, arriveSlack = .5f, smoothTime = .25f, pitchLimit = 8f, lookAhead = 1.5f; public int departDelayMs = 3000;`

同期 (すべて宣言時に初期化、長さ固定):
```
[UdonSynced] public int[] cartState   = new int[8];   // 0 Idle / 1 Riding / 2 Ending (到着停止へ向かう)
[UdonSynced] public int[] cartRider   = new int[8];   // playerId、0 = 空
[UdonSynced] public int[] cartGroup   = new int[]{-1,-1,-1,-1,-1,-1,-1,-1};
[UdonSynced] public int[] cartOrder   = new int[8];   // グループ内の順 (0 = 先頭)
[UdonSynced] public int[] cartSpot    = new int[8];   // 開始した乗り場
[UdonSynced] public int[] groupState  = new int[8];   // 0 Free / 1 Running / 2 Ending
[UdonSynced] public int[] groupMaster = new int[]{-1,-1,-1,-1,-1,-1,-1,-1};   // cart index
[UdonSynced] public int[] groupDest   = new int[8];   // 0..5
[UdonSynced] public int[] groupDir    = new int[8];   // +1 / -1 (loopDistances が増える向きが +1)
[UdonSynced] public bool[] groupAccepting = new bool[8];
[UdonSynced] public float[] groupAnchorDist = new float[8];   // 先頭 (order 0) の経路距離 [0, loopLength)
[UdonSynced] public int[] groupAnchorMs   = new int[8];       // Networking.GetServerTimeInMilliseconds() の値
[UdonSynced] public int[] groupCount  = new int[8];
[UdonSynced] public int version;                              // 変更ごとに +1 (パネルの再描画と検査用)
[UdonSynced] public int[] cartFreedMs = new int[8];           // 解放したサーバー時刻 ms (0 = 猶予なし)。3000 ms 以内の空車は割り当てない
[UdonSynced] public int[] groupGen    = new int[8];           // グループ作成ごとに +1。ReqJoin の世代照合に使う
```
「なし」を表す値は `cartRider` が 0、`cartGroup` / `groupMaster` が -1。0 を「なし」として読む配列と -1 を読む配列を混ぜない。
`cartFreedMs` / `groupGen` は Owner 交代後も猶予と世代を保つため同期に置く (Codex 独立レビュー 2026-10-11 #3・#4)。

要求 (ローカル API → Owner へのネットワークイベント)。戻り値は結果コード `const int`: `CartOk=0, CartBusy=1 (自分が乗車中), CartFull=2 (空車なし), CartClosed=3 (受付 OFF/終了中/存在しない), CartHere=4 (現在地が目的地), CartUnavailable=5`:
```
public int  _Start(int spot);                 // leash._MyChoice() を添えて ReqStart
public int  _Join(int spot, int group, int gen); // ReqJoin。gen はパネルが表示したときの groupGen[group]
public void _Leave();                         // 自分が乗っていれば ReqLeave + 即ローカルで降車
public void _LeaveForTravel();                // KH_LeashManager の _TravelBegin/_TravelBeginJoin の門から呼ぶ。保留中の要求を取り消し、乗っていれば _Leave と同じ (暗転は呼び元)
public void _ToggleAccepting();               // 自分がマスターなら ReqAccepting(!現在)
public bool _IsRiding();  public int _MyCart();  public int _MyGroup();  public bool _IsMaster();
public int  _GroupDestination(int g); public bool _GroupAccepting(int g); public int _GroupState(int g); public int _GroupCount(int g); public int _GroupMasterPlayerId(int g); public int _GroupGen(int g);
[NetworkCallable(maxEventsPerSecond: 4)] public void ReqStart(int spot, int destination);   // net: callable-ok: CallingPlayer で照合
[NetworkCallable(maxEventsPerSecond: 4)] public void ReqJoin(int spot, int group, int gen);
[NetworkCallable(maxEventsPerSecond: 4)] public void ReqLeave();
[NetworkCallable(maxEventsPerSecond: 4)] public void ReqAccepting(bool on);
```
裁定 (Owner):
- `ReqStart`: 要求者が既に乗車中 → 無視 (ローカル側で `CartBusy` を先に返す)。現在地 (spot のノード) が目的地ノード (`route._DestinationNode(destination)`) と同じ → 開始しない (ローカルで `CartHere` を先に判定して要求を送らない)。空車なし → 何もしない。要求者は送信後 2 秒以内に `cartRider == 自分` にならなければ `cart.result.full` を表示する (Owner から結果コードは返さない)。周回以外の目的地は、`route._Next` と同じ短い側で向きを決める。空きグループを 1 つ取り、`groupMaster = cart, groupDest = destination, groupDir, groupAccepting = true, groupState = 1, groupCount = 1, groupAnchorDist = loopDistances[spotNodes[spot]], groupAnchorMs = now + departDelayMs`。カートは `cartState=1, cartRider=caller, cartGroup=g, cartOrder=0, cartSpot=spot`。
- 保留要求 (レビュー #1): 要求者は出発・合流を送ると保留要求 (ローカルの連番) を持ち、2 秒の時間切れ・`_LeaveForTravel`・リスポーンで取り消す。自分に新しく来た割当は、有効な保留要求と乗り場・グループ (合流は世代も) が合うときだけ着座し、合わなければ座らずに `ReqLeave` で返す (ログ `late-assign returned`)。
- 空車 (レビュー #3): `cartFreedMs` から 3000 ms 以内のカートは候補から外す (代わりに使うこともしない)。
- `ReqJoin`: (先に到着判定を現在時刻で確定) group が Running かつ accepting、`groupGen[group] == gen`、空車あり、要求者未乗車 → `cartOrder = groupCount` で末尾に加え `groupCount++`。世代違いは拒否 (ログ `reject ... reason=gen`)。出発ごとに `groupGen[g]++`。錨は打ち直さない (末尾は距離 `anchor - dir*order*gap` に現れる)。ローカル側: 自分のカートが来たら `fade` で短く暗転して着座 (暗転は `KH_TravelFade` の既存 API を使う。無ければ暗転なし)。
- `ReqLeave`: 要求者のカートを `Idle` に戻し、グループから外す。残りの `cartOrder` を詰める (順序維持)。先頭が抜けた場合は錨を**新しい先頭の現在距離**に打ち直す (他の車が前へ詰めない)。マスターなら `cartOrder` 最小の車の乗員へ引き継ぎ、`groupDest/groupDir/groupAccepting` は維持。`groupCount == 0` → `groupState = 0` に解放。
- 到着 (Owner の Update・`ReqLeave` の解放前・`ReqJoin` の裁定前に同じ関数 `SettleArrival` で判定): `groupDest` が周回 (2/3) でなければ、先頭の距離が目的地ノードの距離に `arriveSlack` 以内 → `groupState = 2`、以後の `ReqJoin` は拒否。解放 (錨の打ち直し) より先に確定するので、Owner 自身が先頭で到着降車しても Ending が遅れない (レビュー #2)。各車は自分の距離が目的地距離に達した時点で止まる (Motion 側で clamp)。各クライアントは**自分の**カートが止まった (= 到着距離に達した) 時点で自分で降車処理 (`_Leave` と同じローカル処理 + `ReqLeave`)。Owner は `groupCount == 0` で解放。
- `OnPlayerLeft(player)` (Owner): その playerId のカートを `ReqLeave` と同じ処理で解放。`OnPlayerRespawn(player)`: ローカルなら保留中の要求を取り消し、乗車中なら降車処理 (station には触れない)。
- `OnDeserialization` と Owner のローカル反映は同じ `ApplyState()` を通す: 自分のカートが新しく割り当てられたら着座、自分のカートが消えていたら降車、全パネル `_Refresh()`、全カートの可視と駐車の更新。

Motion (全クライアント、`Update`):
- 距離: `d = groupAnchorDist + dir * speed * max(0, (now - groupAnchorMs)/1000) - dir * order * gap`。周回は `loopLength` で正規化。目的地走行は `dir` 方向で目的地距離を越えない (clamp)。サーバー時刻の差は `Networking.GetServerTimeInMilliseconds()` の差で取り、桁あふれに備えて `int` の差を `float` 秒に直す。
- `KH_WalkRoute` に追加する `public Vector3 _PointAtDistance(float d)`、`public Vector3 _DirectionAtDistance(float d)`、`public bool _DetourAtDistance(float d)` (その区間の `linkAllowsDetour`) を使う。横ずれは `side * lateral * (detour ? 1 : 0)`、`side = (order % 2 == 0) ? +1 : -1` を進行方向の右手に取る。`detour = _DetourAtDistance(d) && _DetourAtDistance(d + dir * LateralLookAhead)` (`LateralLookAhead = 2.0 m` ≒ `lateral / LateralSpeed(0.4 m/s) × speed`)。橋の手前から中央へ寄せ、入口で一列になり終わる (レビュー #5)。
- 接地: `pos + up*2` から下へ 6 m Raycast (`groundMask`)。当たらなければ前フレームの高さ。高さは `Mathf.SmoothDamp`。ヨーは `d + dir*lookAhead` の点へ。ピッチは接地法線から `±pitchLimit` に制限、ロール 0。
- 車輪: `KH_CartSeat._Spin(deltaDistance)` で前後車軸を X 軸回転 (半径 0.14)。
- 乗っていない Idle カートは `parking` に置き Renderer を消す。

### `KH_CartSeat` — `[UdonBehaviourSyncMode(BehaviourSyncMode.None)]`

カート本体 GameObject。`VRCStation` と同居。
```
public KH_CartManager manager; public int index;
public VRC.SDK3.Components.VRCStation station;
public Transform seat, exitL, exitR, stopL, stopR, signBack, axleFront, axleRear;
public Renderer[] renderers;
public KH_CartStopButton stopButton, acceptButton;   // 子 GameObject (Interact を受ける)
public void _SetVisible(bool on);
public void _Spin(float deltaDistance);
public void _Board();          // ローカルプレイヤーを station.UseStation
public void _Unboard();        // exit 位置を決めて station.ExitStation (降車位置は下記)
public override void OnStationEntered(VRCPlayerApi p);  // ログのみ
public override void OnStationExited(VRCPlayerApi p);   // p がローカルで manager._IsRiding() なら manager._Leave() (冪等)
```
Station の設定 (Setup): `PlayerMobility = Immobilize`、`disableStationExit = false` (ジャンプで降りられる)、`seated = true`、`stationEnterPlayerLocation = seat`、`stationExitPlayerLocation = exitL` (降車時に L/R を選び直す)、`canUseStationFromStation = false`。本体の UdonBehaviour は `DisableInteractive = true` (クリック着座を禁止。着座はパネル経由だけ。シリアライズされないので `KH_CartSeat.Start()` が実行時に入れる)。

降車位置 (レビュー #6): `Start` で `exitL/exitR` の車体ローカル位置を控える。降車時 (と着座中 0.5 秒ごと) に車体基準の候補を世界座標で作り、`NavMesh.SamplePosition(候補, 1 m)` が通る側 (L 優先) のアンカー Transform を `hit.position + 0.05 m 上` に置いてから `stationExitPlayerLocation` に設定する。両側とも不可なら控えた位置に戻した `exitL`。毎回書く (同じ側でも車体が動けば点が変わる)。

### `KH_CartStopButton` — None

肘掛け前端の子 GameObject (BoxCollider、Interact)。`public KH_CartManager manager; public KominkaDogRun.KD_Language language; public int kind;` (0 = 停止 → `manager._Leave()`、1 = 受付切替 → `manager._ToggleAccepting()`)。`InteractionText` は `Start()` で `language._Text("cart.stop")` / `("cart.accepting")` を入れる (非 ASCII は実行時なら可)。左 = 停止、右 = 受付切替 (M1 の暫定。X レビューの左右対称は M2 で見直す)。

### `KH_CartStationPanel` — None

乗り場 1 枚 (`KH_TravelNotice` と同じ WorldSpace Canvas、scale 0.001、`VRCUiShape`、BoxCollider trigger)。
```
public KH_CartManager manager; public KominkaDogRun.KD_Language language; public int spot;
public TextMeshProUGUI titleLabel, noticeLabel, startLabel, resultLabel;
public GameObject startButton;
public GameObject[] joinButtons;           // MaxGroups 行 (Running かつ accepting のグループだけ表示)
public TextMeshProUGUI[] joinLabels;       // マスターの表示名だけ (VRCPlayerApi.GetPlayerById(id).displayName。表示名の補助フォント KH_DisplayName)
public TextMeshProUGUI[] joinInfoLabels;   // 「· <行き先> · <人数>」(共通フォント、8 本。補助フォントにはハングル・キリルが無いので分ける。レビュー #7)
public void _Refresh();                    // manager が ApplyState で呼ぶ。言語変更時は manager 経由で再呼出
public void _PressStart();                 // Button → SendCustomEvent
public void _PressJoin0() … _PressJoin7(); // 行ごと (U# は引数付き onClick 不可)。描いたときの世代 (_rowGen[]) を _Join に添える
```
結果表示: `cart.result.*` を `resultLabel` に 4 秒出す。注意書き `cart.notice` (「自動で移動するカートです。VR酔いを感じたら、停止して降りてください。」) を常時表示。

### `KH_WalkRoute` への追加 (既存ファイル、末尾に 3 メソッド)

`_PointAtDistance(float d)`: `d` を `[0, loopLength)` に正規化し、`loopDistances` を二分探索して線形補間。`_DirectionAtDistance(float d)`: 区間の単位ベクトル (水平成分)。`_DetourAtDistance(float d)`: 区間 i の `linkAllowsDetour[i]` (配列が無ければ true)。

### `KH_LeashManager.Travel.cs` への追加

`public KH_CartManager cartManager;` (非同期、Setup が配線)。`_TravelBegin` と `_TravelBeginJoin` で、**暗転へ進む経路に入ってから** (`TravelStartFade()` の直前、または切符を送る直前) `if (cartManager != null) cartManager._LeaveForTravel();` を呼ぶ (乗っていなくても保留中の出発・合流を取り消すため。`_LeaveForTravel` は冪等。レビュー #1)。取消・失敗だけでは降車させない方針どおり、門で弾かれる経路では呼ばない。

### 文言キー (`Assets/KominkaHillside/Data/Environment/Course/CourseUITranslations.json`、ja 必須、他 12 言語は既存の流儀で)

`cart.title` おさんぽカート / `cart.start` ここから出発 / `cart.join` 合流 / `cart.notice` 自動で移動するカートです。VR酔いを感じたら、停止して降りてください。 / `cart.stop` 停止して降りる / `cart.accepting` 合流の受付 / `cart.accepting.on` 受付中 / `cart.accepting.off` 受付停止 / `cart.result.full` いまは空いているカートがありません / `cart.result.closed` このグループは合流を受け付けていません / `cart.result.here` ここが目的地です。さんぽの行き先を変えてください / `cart.result.busy` すでに乗車中です / `cart.result.started` 出発します / `cart.result.joined` 合流しました / `cart.result.left` 降りました / 行き先名は既存のさんぽの行き先キー (`KH_CourseMenuEditor.cs` の並び) を参照し、新規に作らない。

## Editor (Assets/KominkaHillside/Scripts/Editor/Cart/)

`KH_CartSetupEditor.cs` (+ partial `.Verify.cs`, `.Probe.cs`)。流儀は `KH_CourseTravelJoinEditor.Build` (Course/85) を踏襲: Program Asset が無ければ作ってコンパイルして止まる (2 回目で配線) → 退避 → `AddUdonSharpComponent` → `SyncMethod` を実物で Manual に固定 → SerializedObject で配線 → `CopyProxyToUdon` → 保存 → NetworkID の残骸除去。

| メニュー | 内容 |
|---|---|
| `Tools/KominkaHillside/Cart/1 Build Carts And Stations` | ルート `KH_Cart` を作る (冪等)。`CartManager` (Manual)。`Carts/Cart_0..7`: FBX `Assets/KominkaHillside/Models/Cart/KH_OsanpoCart.fbx` を `PrefabUtility.InstantiatePrefab` → `UnpackPrefabInstance (Completely)`、材質 `Assets/KominkaHillside/Materials/Cart/KH_OsanpoCart_Body.mat` (Standard、BaseColor = `KH_OsanpoCart_BaseColor.png`、Smoothness 0.25、GPU Instancing ON) と `KH_OsanpoCart_Sign.mat` (Sign.png) を作って割当。BoxCollider (車体)。`VRCStation` + `KH_CartSeat` (本体)、`StopL/StopR` の子に BoxCollider + `KH_CartStopButton`。`Parking` (Transform、地下 -50 m)。`Stations/Station_0..4`: `KH_LeashManager.spotPositions[spot]` から進行方向右へ 2.5 m・ヨーは spot を向く。パネルは `KH_TravelNotice` の Canvas 構成をコピーして作る (TMP フォントは既存の共通フォント参照を使う。`EnsureButton` は `KD_ShopSetupEditor.Ui.cs` の流儀)。配線: manager.carts/panels/route/leash/language/fade/parking、各 seat.manager/index/…、各 panel.manager/language/spot、`KH_LeashManager.cartManager`。文言: JSON のキーを `KH_CourseTravelEditor.ApplyCatalog` と同じ経路で `KD_Language` へ適用 (グリフ焼き Course/27 は統括が実行) |
| `Tools/KominkaHillside/Cart/2 Verify Carts` | `Temp/cart-20261011/verify.json`: 件数 (8/5/2)、backing の SyncMethod == Manual、全参照 non-null (backing heap の実物も)、Station 設定値、材質とテクスチャの参照、GPU Instancing、`cartManager` 配線、文言キー 13 言語の有無、Program Asset の存在。NG を数えて結果を書く |
| `Tools/KominkaHillside/Cart/3 Probe Cart Motion` | Edit モードでプロキシの純関数を呼ぶ: `_PointAtDistance` が 0 / loopLength/2 / loopLength-0.01 / 負値で連続、`spotNodes[0..4]` の距離 → 位置が `spotPositions` に 3 m 以内、目的地ごとの向き (`groupDir`) の選択、隊列 (order 0..3) の前後/横オフセット、橋の区間で横ずれ 0、到着距離の判定。結果を `Temp/cart-20261011/probe.json` |

Setup は `Assets/KominkaHillside/Models/Cart/` の中身を変更しない (FBX・PNG は制作担当の出力)。

## 検証と引き渡し

- 統括: `bash scripts/unity-compile.sh --udon` → Build (2 回) → Verify → Probe → グリフ焼き (Course/27) → シーン保存 → Codex 独立レビュー → コミット。
- ClientSim: VRCStation の着座・移動は ClientSim では参考値 (`vrc-verification`)。ハーネスの新しい群は M1 では作らず、Edit の Verify/Probe と Codex レビューで引き渡す。
- ユーザーの Play / 実機チェックリスト (M1): ①乗り場 1 で出発し 3 秒後に動き出す ②周回 (順/逆) と目的地到着で止まり降りられる ③停止ボタン・ジャンプで降りて元の散歩へ戻れる ④2 人目が乗り場から合流し千鳥で並ぶ、橋で一列 ⑤先頭が降りても後続が続き受付状態が維持される ⑥乗車中にワープ・合流・リスポーンすると降車して移動する ⑦犬が付いてくる (置き去りにならない) ⑧13 言語で文言が収まる ⑨Quest/スマホで座席・ボタンが操作できる。

## 既知の制約 (M1)

- 空車なしのときの結果通知は要求側のタイムアウト (2 秒) で代用する。
- 合流時の着座は暗転で済ませ、走って追いつく演出はしない。
- すれ違い・交通待機は無い。逆方向のグループは同じ道で重なり得る (M2)。

## M1.1: Play 1 回目の指摘と追加要望 (2026-10-12)

Issue の「Play 確認 1 回目と M1.1」の症状と原因候補に対応する。ユーザー要望 (2026-10-12): 座席の高さ・回転を変えたい / 乗車中はカート横のボタンで手元メニューを呼び、カートの左側で一緒に動いてほしい / 写真・撮影モニターも追従 / 調整 UI は左斜め前、カメラに映らないレイヤー。**乗車中に限り、2026-09-03 の「板は据えた後は追従しない」を上書きする** (降りれば従来どおり)。ClientSim は移動入力やジャンプで Station を抜ける (`ClientSimPlayerStationManager.CanPlayerMoveWhileSeated`) ため、ClientSim でメニューを開いて降りてしまう件は ClientSim の挙動として記録し、実機 (TestBuild) で確認する。

### Runtime (impl-runtime)

1. **接地の頑健化** (`KH_CartManager.Motion.cs`): レイは `p.y + 4` から下へ 10 m、マスクは従来 (Default + Environment)。当たった物が `TerrainCollider` でなく、かつ `|hit.y − p.y| > 0.35` なら、その当たり点の 2 cm 下から撃ち直す (最大 3 回)。採用は道の高さ `p.y` に最も近い当たり。最終の高さは `[p.y − 1.0, p.y + 1.5]` に clamp。ピッチは前後 ±0.42 m の 2 点の高さ差から取り (法線は使わない)、ロールは 0 のまま。レイの頻度: 自分が乗っているカートは毎フレーム 2 点 (前後)、他のカートは 3 フレームに 1 回 1 点、駐車中は 0。ベンチ・看板・犬 (Default のカプセル) の上に乗らないことが狙い。
2. **カート専用の軌道** (`KH_CartManager.Motion.cs` / `.Groups.cs` / `.Requests.cs`): Setup が書く非同期配列 (既定 null) を優先し、null なら従来の route を使う。
   ```
   public Vector3[] trackPositions;  public float[] trackDistances;  public float trackLength;   // 節 0 (入口の枝) を除いた閉路
   public bool[] trackDetour;        // 区間 i (trackPositions[i] → [i+1]) で横ずれ可か
   public float[] noLateralFrom, noLateralTo;   // 追加で横ずれ 0 にする距離範囲 (ベンチ・門枠)
   public float[] spotTrackDistance;            // 乗り場 0..4 の開始距離 (軌道上)
   public float[] destTrackDistance;            // 行き先 0..5 の到着距離 (2/3 の周回は -1)
   ```
   `_PointAtDistance` 相当の補間・方向・detour 判定を軌道配列に対して行う関数を Manager 側に持つ (`TrackPoint / TrackDirection / TrackDetour / TrackNoLateral`)。開始・到着・向きの判定 (短い側) もこの距離で行う。Probe が照合するので、関数名はこの 4 つにする。
3. **座席の調整** (同期、乗っている本人だけ変更可): `[UdonSynced] public int[] cartSeatSteps = new int[8]` (高さ、1 段 = 5 cm、−6..+6) と `[UdonSynced] public int[] cartYawSteps = new int[8]` (1 段 = 15°、−6..+6)。`[NetworkCallable(maxEventsPerSecond: 6)] public void ReqSeat(int dHeight, int dYaw)` を要求者 = そのカートの乗員のときだけ適用し Commit。全クライアントの ApplyState で `carts[c]._ApplySeat(steps, yawSteps)` を呼び、`KH_CartSeat` が `seat` (Anchor_Seat) のローカル位置 y と Y 回転を既定 + 段数で置く。降車 (RemoveCart) で 0 に戻す。ローカル API: `_SeatUp() / _SeatDown() / _TurnLeft() / _TurnRight()`。
4. **ドック** (新規 `KH_CartDock.cs`、None、ローカル専用、各カートに 1 つ): 参照 `KominkaDogRun.KD_TreatPouch menu; KH_VideoCamera video; KominkaDogRun.KD_PhotoViewer photo; Transform dockMenu, dockMonitor, dockPhoto; KH_CartManager manager; int index;`。`LateUpdate` で「ローカルプレイヤーがこのカートに乗っている」間だけ、開いている板をドックの姿勢へ毎フレーム置く: 手元メニューは `menu.transform` を動かし `menu.panelRoot` の中心が `dockMenu` に来るよう補正 (PlaceInFront と同じ実測補正)、撮影モニターは `video.monitorRoot.transform`、写真ビューアは `photo.panelRoot` の親 (`photo.transform`) を同じ要領。板が閉じていれば触らない。降りたら以後触らない (置き去りのまま。据え直しは各板の既存操作)。ボタン: `_PressMenu()` → `menu._Toggle()`、`_PressMonitor()` → モニターが開いていれば `video._Recall()`、閉じていれば `video._Open()`。写真はボタン無し (開いている間だけ追従)。
5. **操作帯** (新規 `KH_CartControls.cs`、None、ローカル専用): 左斜め前の小さな WorldSpace Canvas (Setup が作る、layer 17 Walkthrough = 撮影カメラに映らず操作できる、既存の撮影モニターと同じ)。ボタン 8 個: メニュー / モニター / 座面↑ / 座面↓ / 向き← / 向き→ / 停止 / 受付。表示は自分が乗っているときだけ (`_SetVisible`)。受付はマスターのときだけ有効表示。文言キー `cart.menu` メニュー / `cart.monitor` 撮影モニター / `cart.seat.up` 座面を上げる / `cart.seat.down` 座面を下げる / `cart.turn.left` 左を向く / `cart.turn.right` 右を向く (既存 `cart.stop` / `cart.accepting` を再利用)。
6. 右肘掛けの `StopR` は停止 (kind 0) に変える (左右対称、X レビュー)。受付切替は操作帯だけ。

Runtime 実装で契約から変えた点 (2026-10-12):
- 1 の「TerrainCollider 以外」は判定しない。Udon に `TerrainCollider` 型が公開されていない (VRC.Udon.Wrapper.dll に無い) ので、距離の条件 (|hit.y − p.y| > 0.35) だけで撃ち直す。Terrain は下からは当たらないので、採用される高さは同じ。他人のカートのピッチは道の勾配 (`d` と `d + dir·lookAhead` の点の高さ差) から取る (レイを増やさない)。
- 4 の写真ビューアはドックへ寄せない。`KD_PhotoViewer` は自分の `PostLateUpdate` で毎フレーム頭の前へ置き直すので、LateUpdate で動かしても上書きされる。頭に付いて動くので乗車中も一緒に動く。`photo` / `dockPhoto` は配線だけ受ける。
- ドックの Transform は +Z = 座っている人から板へ向かう方向 (PlaceInFront・PlaceMonitor と同じ約束)。
- 配線の追加: `KH_CartSeat.controls` (KH_CartControls)、`KH_CartSeat.dock` (KH_CartDock)。`KH_CartControls` は `manager, index, dock, language, panel (GameObject), buttons (GameObject[8]), labels (TextMeshProUGUI[8])`、並びは 0 メニュー / 1 モニター / 2 座面↑ / 3 座面↓ / 4 向き← / 5 向き→ / 6 停止 / 7 受付、onClick は `_PressMenu … _PressAccepting`。

### Editor (impl-editor)

1. **軌道の生成** (Build): `KH_WalkRoute` の `nodePositions` から節 0 を除いた閉路 (435 → 1 を直結) で `trackPositions / trackDistances / trackLength / trackDetour` を作る (`linkAllowsDetour` を区間へ写す。435→1 の区間は 435 の値)。乗り場 0..4 は `spotNodes` の位置を軌道へ投影した距離、行き先 0..5 は `destinationNodes` の位置を投影した距離 (入口 = 節 0 の位置を投影 → 門前の直結区間上)、周回 2/3 は −1。障害物の掃引 (トレイル沿い ±0.6 m、車高 0.3〜1.6 m、`Renderer.bounds`、Terrain / 草 / 木 / 石 / 犬 / カートを除く) で見つけた物の距離 ±4 m を `noLateralFrom/To` に書く (現状はベンチ d≈93 と門枠 d≈0 の 2 件が見込み)。配列は SerializedObject → `CopyProxyToUdon`。**Course/21 で経路を作り直したら Cart/1 を再実行する**旨をログと本書に残す。
2. **ドック・操作帯の生成** (Build): 各カートに `Dock_Menu` (左側 1.1 m、高さ 1.0、面を座席へ)、`Dock_Monitor` (左前 1.2 m、高さ 1.1)、`Dock_Photo` (左後 1.0 m) の Transform。`Controls` Canvas (左斜め前 0.55 m、高さ 0.95、幅 0.28 m、layer 17、VRCUiShape、BoxCollider trigger、既存の共通フォント) にボタン 8 個を `EnsureButton` で配線。`KH_CartDock` と `KH_CartControls` を `AddUdonSharpComponent`、参照を配線 (`menu` = シーンの `KD_TreatPouch`、`video` = `KH_VideoCamera`、`photo` = `KD_PhotoViewer`。無ければ null のまま)。`StopR` の kind を 0 に。Program Asset は初回に作ってコンパイル待ちで止まる (従来どおり)。
3. **文言**: 上記 6 キー × 13 言語を `CourseUITranslations.json` へ (統括が Course/27 を流す)。
4. **Verify / Probe**: 軌道の節数 = 435、`trackLength` が route より約 11 m 短い、軌道と route の最大乖離 (節 0 近傍を除き 0)、乗り場の開始点が門枠の bounds に入らない、入口の到着点が門枠の手前 (z が門枠より大きい側)、`noLateral` がベンチの距離を覆う、ドック 3 点と操作帯の配線 (プロキシと backing)、layer 17、`StopR` kind 0、座席配列の長さ。

### 検証と引き渡し (M1.1)

統括: compile → Build (Program Asset 2 本の初回は 2 回) → Verify → Probe → Course/27 → Codex 独立レビュー → コミット。Play で見ること: 門に入らず広場へ向かう / ベンチ脇で一列になり浮かない / 坂で埋まらない (起きたら場所を報告) / 操作帯のメニューで手元メニューが左側に出て一緒に動く / 撮影モニター・写真ビューアが追従 / 座面の上下と向きが自分と他人の画面で一致 / ClientSim ではメニュー操作で降りるが TestBuild で座ったままか。
