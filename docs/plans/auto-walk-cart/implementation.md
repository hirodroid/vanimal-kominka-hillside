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

Play 2 回目の追加 (2026-10-12):
- 座席の前後: `[UdonSynced] public int[] cartFwdSteps = new int[8]` (1 段 = 5 cm、+ が前、−6..+6)。`ReqSeat(int dHeight, int dYaw, int dFwd)`、`KH_CartSeat._ApplySeat(int steps, int yawSteps, int fwdSteps)` が Anchor_Seat のローカル z にも反映。ローカル API `_SeatForward() / _SeatBack()`。
- 操作帯は 10 ボタン: 0 メニュー / 1 モニター / 2 高さ▲ / 3 高さ▼ / 4 前後▲ / 5 前後▼ / 6 向き◀ / 7 向き▶ / 8 停止 / 9 受付 (`_PressSeatForward` / `_PressSeatBack` を追加)。キー `cart.seat.forward` / `cart.seat.back`。
- 操作帯の最小化: `KH_CartControls.body` (10 ボタンの親) / `tab` (body の外、常に見える切替) / 任意の `tabLabel` (展開中「×」・最小化中「≡」を書く)、tab の onClick → `_PressToggle()`。乗車開始 (`_SetVisible(true)`) で展開に戻す。
- 撮影モニターを隠す: `KH_VideoCamera._HideMonitor()` (`_open` のまま `monitorRoot` だけ非表示。挙動は system root、RecordSnap は monitor の兄弟なので撮影・RecordSnap・ドローン送信・パターンは続く)、`_ShowMonitor()` (= `_Recall`。隠し中なら再表示)、`_MonitorHidden()`。× (`_Close`) は従来どおり全停止。操作帯のモニターは 閉 → `_Open` / 表示中 → `_HideMonitor` / 隠し中 → `_ShowMonitor` の循環。
- コスト: 他人のカートは 2 フレームに 1 回 (補間なし、SmoothDamp は経過 dt を明示)、接地は前回から 3 フレーム以上 (実質 4 フレームに 1 回)。軌道の区間探索は前回の区間から近傍をたどる (二分探索は外れたときだけ)。パネルは version・言語・自分の行き先が変わったときだけ書き直す。

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

## M2: すれ違い・犬の配置・掲示・カメラ・調整 (2026-10-12)

ユーザー決定 (2026-10-12): 停止後は仕様どおり乗り場からの再出発のみ (その場に残さない)。グループ走行と移動中の合流は Play で OK。FPS は最後に確認。**M2 を全部実装する。** 契約は M1/M1.1 と同じ流儀 (Runtime は `Scripts/Cart/` と指定ファイル、Editor は `Scripts/Editor/Cart/` と翻訳表、Unity 操作は統括)。

### 担当とファイルの所有

| 担当 | 所有 | 内容 |
|---|---|---|
| Runtime (impl-runtime) | `Scripts/Cart/*`、`KH_LeashManager*.cs` | 接地 v3、交通 (すれ違い・待機)、犬用アンカー API、操作帯の「案内」、受付 OFF の表示、調整値 |
| Editor (impl-editor) | `Scripts/Editor/Cart/*`、`CourseUITranslations.json`、`Data/Cart/` | 道幅の分類と待避点、掲示 (背面・乗り場)、調整 JSON、Verify/Probe |
| Dog (impl-dog) | `Imported/KominkaDogRun/Scripts/KD_DogWanderController.HillsideLeash*.cs`、`KH_WalkStopovers.cs` (読むだけ可) | 乗車中の犬の前方配置とくん活抑制 |
| Camera (impl-camera) | `Scripts/KH_VideoCamera*.cs`、`Scripts/Editor/KH_VideoCameraEditor*.cs`、`UITranslations.json` の video.* | 隊列カット |
| Model (model-cart) | `scripts/art/osanpo-cart.py`、`Models/Cart/` | 車軸の LOD1 |

### Runtime 1: 接地 v3 (犬と同じ方式に寄せる)

犬は「自分の現在位置 + 1.5 m から下へ 4 m、Default + Environment、最初の当たりに y を合わせる (平滑化なし)」で埋まらない (`KD_DogWanderController.SnapToGround`)。カートは「道の高さ py + 4 から 10 m、py に最も近い当たりを採用、[py−1, py+1.5] に clamp、SmoothDamp」で、道の高さを基準にしている点が違う。v3:
- 原点は **カートの前回の高さ** `_y[c] + 1.5` (初回・snap 時は py + 1.5)。長さ 4 m。最初の当たりを採用。当たらなければ py + 4 から 10 m で撃ち直し (復帰用)。
- 障害物 (ベンチ・犬・看板) の上に乗らない: 当たりが前回の高さより 0.45 m 以上高く、かつその 2 cm 下から撃ち直して 1.0 m 以内に別の当たりがあれば、低い方を採る (最大 2 回)。
- 高さの平滑化は残すが `smoothTime` を 0.08 s に下げる (坂での遅れを 2 cm 程度に)。ピッチは前後 2 点から。
- **診断ログ**: |cart y − py| > 0.5 m が 1 秒以上続いたら 5 秒に 1 回 `[KH_Cart] ground gap=<m> d=<m> at=<pos> hit=<collider名>` を出す (埋まり・浮きの場所を Play ログから特定する)。

### Runtime 2: 交通 (対向グループのすれ違いと待機)

Editor が焼く配列 (null 既定): `int[] trackClass` (区間 i: 0 = 広い (千鳥可)、1 = 一列なら双方通れる、2 = 一列でも通れない)、`float[] narrowFrom, narrowTo` (class 2 の連続区間の距離範囲、軌道距離)、`float[] waitBefore, waitAfter` (各 narrow 区間の手前の待機点距離: 順方向から入る側 / 逆方向から入る側。待避できる class 0/1 の点で、区間の端から 3 m + 想定隊列長 (gap × 4) 手前)。
同期 (追加): `int[] groupLane = new int[8]` (0 千鳥 / 1 一列・右寄せ / 2 一列・中央)、`float[] groupHoldDist = new float[8]` (−1 = なし。先頭の距離の上限。dir 方向でこれを越えない)、`int[] groupHoldSinceMs = new int[8]`。
Owner の判定 (OwnerTick 4 Hz、`groupState == Running` の全組):
1. **一列化**: 自分の先頭から `approach` (25 m) 先までに対向グループ (dir が逆) の車があれば、双方 `groupLane = 1` (右寄せ 0.55 m、横ずれ 0)。class 2 区間の中と手前 8 m は `groupLane = 2` (中央)。対向が居なくなり 10 m 離れたら 0 に戻す。同方向のグループは対象外。
2. **狭い区間の先入優先**: 対向グループが class 2 区間の中に居る (先頭〜最後尾のどこかが区間内) か、区間の手前 `approach` 以内に居て自分より区間に近いなら、自分は区間手前の待機点で止まる: `groupHoldDist = waitBefore/After`。対向の最後尾が区間を出て 2 m 進んだら解除 (`groupHoldDist = −1` と錨の打ち直し: 現在の先頭距離・現在時刻)。
3. 両方が手前なら、区間に近い方が先。同距離なら group 番号が小さい方。待機は最長 60 s で解除 (詰まり防止、ログ `traffic timeout`)。
4. 待機は交通上の自動停止であり、所属・目的地・マスター・受付は変えない。待機中は操作帯の結果欄に `cart.result.waiting`「すれ違い待ち。自動で進みます」を出す (パネルの `resultLabel` ではなく操作帯に 1 行、`KH_CartControls` に `TextMeshProUGUI noteLabel` を追加)。
Motion: 横ずれは `groupLane` で決める (0 = 従来の千鳥、1 = 右へ +0.55、2 = 0)。切り替えは LateralSpeed で滑らかに。距離は `groupHoldDist` で clamp (目的地 clamp と同じ扱い)。
ログ: `traffic lane g=.. lane=..` / `traffic hold g=.. at=.. because=..` / `traffic release g=..`。

### Runtime 3: 犬用 API と操作帯

- `public bool _IsPlayerRiding(int playerId)`、`public Vector3 _DogAnchor(int playerId, int slot)` (slot 0..2: 乗っているカートの前方 0.9 m、横 (−0.45, +0.45, 0) の順。`groupLane != 0` のときは横を半分に。カートが無ければ `Vector3.zero`)、`public Vector3 _CartForward(int playerId)`。犬担当が呼ぶ。
- 操作帯に「案内」ボタン (11 個目、文言 `cart.guide`「お手入れ道具の案内」): `KD_TreatPouch` を開き、課金道具の案内ページがあればそのページへ (無ければ `_Toggle()` だけ)。ドックへ寄せるのは従来どおり。
- 乗り場パネル: 受付 OFF / 終了中のグループも行に出し、`cart.result.closed` の短縮形 (`cart.row.closed`「受付停止」/ `cart.row.ending`「到着中」) を人数の後ろに付け、ボタンは無効表示 (押しても `CartClosed`)。
- 調整値: `speed / gap / lateral / approach / holdTimeoutSec / seatStep / yawStep` を public のまま、Editor の `Data/Cart/tuning.json` から Build が書く (下記)。

Runtime 実装で決めたこと (2026-10-12):
- 判定は副作用なしの公開メソッド (Probe が Reflection で呼ぶ): `int TrafficDecide(float myLead, int myDir, int myCount, int myGroup, float otherLead, int otherDir, int otherCount, int otherGroup, float zoneFrom, float zoneTo)` (0 進む / 1 手前で待つ / 2 自分が区間内) と `float TrafficHoldDistance(float myLead, int myDir, int zone)` (待機距離。越えていれば先頭の距離、範囲外 −1)。最後尾は `lead − dir·(count−1)·gap`。
- 待機中は先頭が待機点で止まり、後続は間隔を保って止まる (目的地の clamp と違い詰めない)。解除・タイムアウトで錨を今の先頭距離・今の時刻へ打ち直す。タイムアウト後 20 s は同じグループを待たせない。到着中 (Ending) のグループに待機は残さない。
- 接地の高さの平滑化は新しい `groundSmoothTime = 0.08` (既存の `smoothTime` はシーンに 0.25 が保存されているのでピッチ用に残す)。
- 追加の配線 (任意): `KH_CartControls.guideTab` (int、−1 = 開くだけ)、`KH_CartSeat.signCaption`、`KH_CartStationPanel.prLabel / photo`。

### Editor

1. **道幅の分類** (Build、`.Track.cs`): 軌道の各区間の中点で、進行方向の左右へ 0.25 m 刻みに `NavMesh.SamplePosition` (半径 0.3) が通る幅を測り (最大 4 m)、左右合計の通行幅 W を得る。class 0: W ≥ 3.4、class 1: 2.2 ≤ W < 3.4、class 2: W < 2.2 または `linkAllowsDetour = false` (橋)。連続する class 2 を区間にまとめ、前後 1.5 m の余裕を足す。待機点は区間端から `3 m + gap×4` 手前で class ≤ 1 の最初の点。`trackClass / narrowFrom / narrowTo / waitBefore / waitAfter` を Manager へ書く。build.json に class ごとの延長と区間一覧を出す。
2. **掲示**: 背面の Sign に `Data/Cart/KH_CartSign.png` (512×256、`Data/WashPremium/Photos/Set*.png` の 1 枚を枠に収め、価格・文字なし) を Build が合成して `KH_OsanpoCart_Sign.mat` へ。Sign の板の下に小さな TMP (`cart.sign.caption`「ばにまるのお手入れ道具」、共通フォント、13 言語) を `Anchor_SignBack` の子に置く (操作なし、layer Default)。乗り場パネルに写真 1 枚 (同じ Set 画像) と 1 行 (`cart.station.pr`「散歩のあとは、お手入れ道具で洗ってあげよう」) を注意書きの下に追加。乗り場は注意書きが先。
3. **調整 JSON**: `Data/Cart/tuning.json` (speed 1.3, gap 3.0, lateral 0.6, approach 25, holdTimeoutSec 60, seatStep 0.05, yawStep 15, laneOffset 0.55)。Build が読んで Manager へ書く。`Tools/KominkaHillside/Cart/4 Apply Tuning` は JSON だけ再適用 (シーンの他は触らない)。
4. **Verify/Probe**: class 配列の長さ = 区間数、class 2 が橋 2 本を含む、待機点が class ≤ 1、待機点と区間の距離、`groupLane` 配列の長さ、操作帯 11 ボタン、Sign 材質の画像、caption の配線、tuning の値一致。Probe: 対向 2 グループの模擬 (Edit で Owner 判定の純関数 `TrafficDecide(...)` を Reflection で呼び、先入優先・待機点・解除の 6 ケース)。

### Dog (impl-dog)

- 持ち主が乗車中 (`cartManager._IsPlayerRiding(holderId)`) の犬は、リードの目標を手ではなく `cartManager._DogAnchor(holderId, slot)` にする (slot は持ち主の接続犬の中での順)。追従は既存の速度制御のまま (目標が 1.3 m/s で進むので歩き続ける)。
- くん活 (`LeashStopover` / sniff / detour の開始) を持ち主が乗車中は始めない。進行中なら中断して追従へ戻す。
- 持ち主が降りたら通常へ戻す (次の判定で自然に戻る形でよい)。
- `KH_CartManager` への参照は `KD_DogWanderController` の非同期フィールド `public KH_CartManager cartManager;` (null なら従来どおり)。配線は Cart/1 Build が行う (Editor 担当: 6 匹の `cartManager` を SerializedObject で書く)。
- 変更は乗車中だけに効くこと。通常の散歩の挙動 (目的地・寄り道・集合) を変えない。

### Camera (impl-camera)

- 新カット「隊列」(`video.mode.convoy`「隊列」): 自分が乗っているグループの全カートと犬を 1 画面に収める。被写体の中心 = グループのカートの重心、距離 = 隊列の広がり (先頭〜最後尾 + 犬の余裕 2 m) から画角で求め、後ろ斜め上 (進行方向の後方 35°、仰角 20°) から追う。乗っていなければ既存の「背中ごし」に戻る。既存の切替 UI (Modes の並び) に追加し、パターン再生の対象にはしない (任意)。
- `KH_CartManager` への参照 `public KH_CartManager cartManager;` (Build で配線)。

### Model (model-cart)

- `_AxleFront_LOD1 / _AxleRear_LOD1` (各 ≤ 180 三角形、8 分割) を FBX に追加し、LOD1 合計 ≤ 1,500 (Body_LOD1 1,136 + 車軸 2 本)。production.md と report を更新。Editor の LODGroup は LOD1 に車軸 LOD1 を含める (Editor 担当)。

### 乗車中の撫で・おやつ (決定)

特別な処理は入れず **許可**する。撫では手の接触なのでそのまま、おやつ呼びは犬が持ち主のアンカーに居るので近い。Play で破綻があれば M3。

### 検証と引き渡し (M2)

compile → Cart/1 (Program Asset 無し) → Cart/2 → Cart/3 → Video/1 → Video/2 → Course/27 → 保存 → Codex → コミット。Play で見ること: ①逆方向 2 グループが広い道で一列・右寄せになってすれ違う ②橋の手前で後から来た側が待ち、相手が抜けたら進む ③犬が前方の定位置を歩き、くん活で止まらない ④背面の掲示と乗り場の写真 ⑤「隊列」カット ⑥埋まり・浮きの場所が `ground gap` ログに出るか ⑦FPS (最後)。

## M3: 門前の経路の整形・カートステーション・その場に残すカート (2026-10-12)

ユーザー指摘と要望 (2026-10-12、Play 5 回目後):
- 「広場から川辺へ行くとき、古民家の前を通る経路がスムーズに曲がらず、一旦古民家の方へ突き抜けてぐるんと方向転換する。犬の散歩も同じ。経路を見直してほしい」
- 「各乗り場には動かないただのモデルのカートを置き (最も軽く)、その横の取っ手の上にメニューを置く。ザ・カートステーション」
- 「途中で止まったらカートをそこに残すモードにしたい。降りたらカートはそこにあり、そこがカートステーションになる。停止して降りたら同じメニューがカートの上に出て、そこから再開も、グループへの合流もできる。グループからは一旦外れてよく、今いる場所を起点に一人で周回を始めることも、別の・同じグループへ合流することもできる」(2026-10-12 の「乗り場からの再出発のみ」の決定を**上書き**)

### 原因 (経路)

周回路は節 432 (49.5, 48.6) → 433 (44.8, 46.4) → 434 (44.0, 45.6) → 435 (44.3, 44.6) → **0 (47.6, 40.1) = 門 (入口の目的地)** → 1 (43.5, 45.8) → 2 (40.1, 51.8) と閉じる。Course/21 が延長路の終端を元の節 1 の位置へ戻し、そこから元の節 0 (門) へ下がって節 1 へ戻るため、門へ 6 m 突き出して折り返す棘ができる。犬 (周回・入口以外の目的地) もカートもここを通る。カートの軌道は節 0 を除いただけで 433〜435 の折れが残る。

### Editor 1: 門前の折返しを滑らかに (`Tools/KominkaHillside/Course/22 - Smooth Gate Turn`、impl-editor)

既存の route を**その場で**整形する (節の数と番号は変えない。Course/21 の再実行は不要):
- 節 432 と節 2 の間の節 433・434・435・0・1 を、432 → 2 を結ぶ 2 次ベジエ (制御点は門側へ膨らむ (44.5, 45.0) 付近) 上に等間隔で置き直す。各点は Raycast (Default + Environment) で y を取り、`NavMesh.SamplePosition` (0.5 m) で通行面へ寄せる。
- 節 0 (入口の目的地) は曲線上で門に最も近い点にする (入口到着 = 門前の曲線上。門から約 5〜7 m)。
- 置き直した節の `arriveRadius` を 1.2 に下げる (間隔が 2〜3 m になるため)。`route.nodes[i].position` (Transform) と `nodePositions` の両方を更新。
- `loopDistances / loopLength` を再計算し、隣接リンクの `NavMesh.CalculatePath` が繋がり長さが直線の 1.3 倍以内であることを検査。`KH_CourseRouteEditor.Index(route)` (セル索引) を呼び直し、`CopyProxyToUdon`、保存。`Temp/des-env-002/before-gate-turn.unity` を退避。
- 検査 (`Course/22b - Verify Gate Turn`): 432→2 の連続する 3 節の向きの変化が 45° 未満、各節が NavMesh 上、門枠の bounds から 1.0 m 以上離れている、`spotNodes[0]`=435 の位置が乗り場 0 (警告のみ: 乗り場 0 の着地位置は Course/80 のまま)。
- 実行順: Course/22 → Course/22b → Cart/1 (軌道を焼き直す) → Cart/2・3。

### 設計 (その場に残すカート)

- `cartState` に **3 = Parked** を追加。個人の停止・降車と到着終了で、カートは Idle (プール) ではなく Parked になり、その場に残る。同期: `float[] cartParkDist` (軌道距離)、`int[] cartParkedMs` (サーバー時刻)。Motion は Parked のカートを `cartParkDist` の軌道点から右へ `parkOffset` (0.9 m、class 2 の区間では 0.3 m) に置く (他の車の通行を妨げない)。
- Parked のカートには **カート上のメニュー** (`KH_CartStationPanel` を `spot = -1`、`cart = index` で各カートに 1 枚、右肘掛けの上 0.55 m、外側を向く) が出る。内容は乗り場のパネルと同じ (出発 / 合流の行 / 注意書き / PR)。Parked でないときは非表示。
- **カートからの出発** (`ReqStartFrom(int cart, int destination)`): そのカートが Parked なら、要求者を乗員にし、`groupAnchorDist = cartParkDist` で 1 人グループを作る (カートはその場から動き出す)。現在地が目的地なら `CartHere`。
- **カートからの合流** (`ReqJoinFrom(int cart, int group, int gen)`): そのカートが Parked なら、要求者を乗員にして末尾に加える (カートは暗転で隊列へ移動。乗り場からの合流と同じ)。
- **乗り場からの出発・合流**: 空き (Idle) のカートが無ければ、`cartParkedMs` が最も古い Parked カートを回収して使う (ログ `reclaim cart=.. parkedFor=..s`)。Parked のカートを誰かが使い始めた瞬間に、そのカートのメニューは消える。
- 使えるのは誰でも (Parked のカートは公共の乗り場)。乗車中のワープ・リスポーン・退出での離脱も Parked にする (置き去りが増えるが回収で吸収)。
- `KH_CartSeat`: Parked のとき `_SetVisible(true)`、カート上メニューの表示切替。
- 受付 OFF / 到着中の行、世代照合、タイムアウトは乗り場と同じ関数を使う (パネルの `spot` が −1 なら `cart` の位置を開始点にする)。

Runtime 実装で決めたこと (2026-10-12):
- 状態: Idle 0 (プール・地中) / Riding 1 / Ending 2 / Parked 3 (乗員 0・グループ −1・可視)。乗員の判定・隊列の数え上げは Riding/Ending だけ (`IsRidingState`)。RemoveCart (停止・到着降車・ワープ・リスポーン・退出・見張りの gone) はグループがあれば今の目標距離で Parked、無い不整合だけ Idle。Maintain は Parked を退出・不整合の判定から外す。
- 置き方: 降りる人が座っている間はその場、離れたら止まったときの向きのまま右へ寄せ (LateralSpeed)、寄せ終われば以後は書かない。走行 → Parked、Parked → 走行の距離の差は `_err` でなめらかに追う (合流は跳びが大きいので暗転のまま跳ぶ)。
- 乗り場の出発・合流のカートは `ReclaimCandidate()` (Idle の空車 → 最古の Parked → −1。どちらも解放から 3 s の猶予外)。カートからの出発・合流 (`_StartFrom / _JoinFrom`) は猶予を見ない (使う人は外に立っている)。
- Probe 用 (副作用なし): `float ParkedStartDistance(int cart)`、`int ReclaimCandidate()`。
- 配線: `KH_CartSeat.cartPanel` (KH_CartStationPanel)、`KH_CartStationPanel.cart` (int、乗り場 −1)、任意の `KH_CartStationPanel.content` (隠す中身の親。null なら直下の子すべて + パネルの Collider)。カート上メニューの結果表示は `carts[c].cartPanel` で引く。

Codex M3 レビュー後 (2026-10-12):
- 同期 `int[] cartParkDir` (駐車時の進行方向 ±1)。向きと右側はこれで決める (Late Joiner も同じ)。
- 駐車枠 (`KH_CartManager.Park.cs`、副作用なし・Probe 用): `float ParkSlotDistance(float wantDist, int dir, int self)` (狭い区間の中なら進行方向側の端 + 3 m、同じ向きの Parked と 1.6 m 未満なら後ろへ 1.6 m ずつ最大 8 回) と `float ParkSlotLateral(float dist, int dir)` (class 0: 1.6 / class 1: 1.2、NavMesh 0.5 m 検査、外なら 0.3 m ずつ内側、下限 1.0)。旧 `parkOffset / parkOffsetNarrow` は使わない。
- 停止・到着は、乗員を乗せたまま駐車距離へ speed で進み、横に寄せ切ってから外側 (右) へ降ろす。ワープ・合流の移動はすぐ降ろす。
- 中間なしの再駐車は `cartParkedMs / cartParkDist` の変化で見分けて即時配置。Parked → 走行は、距離の差 1 m 超 (回収・合流) なら全員即時配置、その場からの再開は駐車姿勢から補間。

### Editor 2: カートステーション (impl-editor)

- 乗り場 5 地点: 乗り場パネルの横に**静的なカート** (FBX のメッシュだけ: Body + 車軸、LODGroup 付き、Udon・Station・Collider なし、layer Default) を置く。向きは道の進行方向、位置は乗り場の着地点の右手 1.2 m。
- 乗り場パネルはその静的カートの右肘掛けの上 0.55 m に移し、通路側 (着地点側) を向ける。大きさは現状のまま (幅 760 → 肘掛け幅より大きいので、肘掛けの真上から少し外へ出してよい)。
- 8 台の走行カートにカート上メニュー (`Panel` 子オブジェクト、`KH_CartStationPanel` 1 枚ずつ、`spot=-1`, `cart=index`) を作り配線。Manager の `panels` は乗り場 5 + カート 8 = 13 枚 (`ApplyState` の `_Refresh` 対象)。
- Verify: 静的カート 5 台 (Udon なし)、パネルの位置、カート上メニューの配線、`cartParkDist / cartParkedMs` の長さ。Probe: `ReqStartFrom` の開始距離 = `cartParkDist`、回収の順序 (最古)。

### 文言

`cart.start.here`「ここから出発」(乗り場と同じ文言でよければ `cart.start` を再利用)、`cart.parked`「ここに停めてあります。乗って再開できます」(カート上メニューの注意書きの代わり)。

### 検証と引き渡し (M3)

compile → Course/22 → Course/22b → Cart/1 (Program Asset 無し) → Cart/2 → Cart/3 → 保存 → Codex → コミット。Play: ①広場→川辺で門前を滑らかに曲がる (犬も) ②乗り場に静的カートとその上のメニュー ③途中で停止するとカートが残り、上にメニューが出る ④そこから一人で再開 / 同じ・別のグループへ合流 ⑤乗り場で空車が無いとき最古の置き去りカートが回収される。
