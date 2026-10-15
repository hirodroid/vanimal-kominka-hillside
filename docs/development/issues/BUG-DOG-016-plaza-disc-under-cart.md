---
id: BUG-DOG-016
type: BUG
system: DOG
status: review
priority: Medium
summary: 広場に置いてあるフリスビーがカートに踏まれている (ユーザー報告 2026-10-15)。乗り場の見本カートがディスクの列の真上にあり、走行の軌道も 1.4 m しか離れていない。ディスクの置き場所を見直す
next: 適用済み (Walking/5b → 5 PASS → Cart/2 CART_OK → 保存)。残りはユーザーの Play: カートで広場を通ってディスクを踏まない・見本カートの下にディスクがない・犬とのフリスビー遊びが今までどおり (西側の新しい置き場 (67.6, 131.0) 中心)
updated: 2026-10-15
---

# BUG-DOG-016 広場のフリスビーがカートに踏まれている (置き場所の見直し)

## ユーザー報告 (2026-10-15)

> ひろばに置いてあるフリスビーがカートに踏まれているで、場所を見直して

端末・どのカート (見本カートか走行中のカートか) は未記録。

## 分かっていること (2026-10-15 起票時にシーンの YAML を読んだ値。Unity 未実行)

### ディスクの位置

- ディスク 6 枚 (`KH_Dogs/KH_Frisbee_<犬種>`) は `KH_DogWalkingEditor.DiscHome(i)` の値そのまま: x = 74.6 / 75.5 / 76.4、z = 132.25 / 133.15、y ≈ 19.03。広場の中心 (72, 127) の北東 3.5 m を起点にした 3×2 の格子 (旧ボール格子の 2.2 m 奥。[IMP-DOG-019](IMP-DOG-019-ball-and-disc-placement.md))。
- Build (Walking/3) はディスクを**新規作成時だけ** `DiscHome` に置く ([KH_DogWalkingEditor.cs:342](../../../Assets/KominkaHillside/Scripts/Editor/KH_DogWalkingEditor.cs) `if(created)`、以後は人が置いた位置が正)。`VRCObjectSync` の復帰先はシーン開始時の位置なので、**シーンの 6 枚を動かせば実行時の初期位置と復帰先が変わる**。`DiscHome` は新しいシーン・再作成のときの値なので合わせて直す。
- Walking/5 Verify はディスクが広場の平坦部 (中心から 12 m 以内、地面 y = 19 ± 0.05) にあることと、置き場 (KH_ToyPark) の上にあることだけを見る ([KH_DogWalkingEditor.cs:620-622](../../../Assets/KominkaHillside/Scripts/Editor/KH_DogWalkingEditor.cs))。カートとの距離は見ていない。

### カート側

- **乗り場 1 (広場) の見本カート `KH_Cart/Stations/StationCart_1` が (76.7, 19.0, 132.8)、パネル `Station_1` が (76.4, 20.3, 132.8)。** 見本カートの本体は 0.82 × 1.2 m (`BodyColliderSize`) なので、Lab (76.4, 132.25) と Samoyed (76.4, 133.15) のディスクは**見本カートの真下**、Pome / Shiba (75.5) も車体の縁に掛かる。見本カートは 2026-10-12 の要望「見本カートが道に掛かって邪魔」で、軌道上の開始点から乗り場の側 (`StationSide[1]=+1` = 進行方向の右 = 東) へ 2.2〜3.5 m 離した位置に置かれた ([DES-WLD-004](DES-WLD-004-auto-walk-cart.md) M3、`KH_CartSetupEditor.StationCartPoseFor`)。ディスクの列 (2026-09-26 に決めた位置) はその後の配置で確認されていない。見本カートは Udon / Collider なしのメッシュだけなので物理的には押さないが、見た目は「踏まれている」。
- 走行の軌道は広場を南北に貫く: `KH_WalkRoute` の Node15 (77.4, 121.7) → Node16 (72.0, 127.0 = 広場の中心) → LoopNode017 (73.2, 133.4) → (74.4, 139.9)。節点の折れ線からディスクまでの最短は Corgi / Dachs (x 74.6) で **1.4〜1.6 m**、Pome / Shiba 2.3〜2.5 m、Lab / Samoyed 3.2〜3.4 m (実際の軌道は折れ線を滑らかにしているので多少ずれる)。走行カートは千鳥で中心から ±0.6 m (`KH_CartManager.lateral`)、車体の半幅 0.41 m、連れている犬は横 ±0.6 m に揺れるので、**軌道の中心から 1.6 m 以内は車体か犬が通る**。走行カートの本体は BoxCollider (トリガーでない、layer 2) で `SetPositionAndRotation` で動き、layer 2 と 13 (おもちゃ) は当たる設定なので、通りがかりにディスクを押す・乗り上げることがある。
- カートの Build の障害物の掃引 (`SweepObstacles`) は車高 0.3〜1.6 m の箱で見るので、地面にある厚さ数 cm のディスクは拾わない ([KH_CartSetupEditor.Track.cs:135](../../../Assets/KominkaHillside/Scripts/Editor/Cart/KH_CartSetupEditor.Track.cs))。乗り場の見本カートの配置も、置いた先に何があるかを見ていない。

### 広場にある他の物 (置き場所の候補を考えるときの制約。シーンの YAML から)

- 洗い場 Hillside (66.4, 121.3) と棚・リセットボタン (65.9〜67.2, 120.8〜122.2)、アイテム案内板 (62.5, 124.0)、リード乗り場 `KH_Station_Plaza` (82.2, 123.7)、ライブの犬の位置 `KD_LiveDogSpot` (71.6, 124.5)、ハロウィンパレードの Finale_0〜5 (69.3〜74.7, 125.6〜127.9) とライブステージ (中心)、`KH_World/KH_Dummies` の犬のダミー 5 体 (68〜76, 124〜131。有効かどうかは未確認)。
- 広場の平坦部は半径 12 m。ボールは広場の 4 m 下の置き場にあり、この件の対象外。

## 直し方の候補 (決めるのは実装時。シーンで実際の見え方を確かめてから)

1. **ディスクの列を動かす** (報告の指示どおり)。条件: 広場の平坦部の中 (Walking/5 の検査)、見本カート (本体 + パネル + 乗る人の立つ範囲) と重ならない、軌道の中心から **2.5 m 以上** (車体 0.41 + 千鳥 0.6 + 犬の揺れ 0.6 + 余裕)、上の他の物と重ならない、犬がディスクを拾いに行ける地面。例: 軌道の西側 (x 66〜69、z 128〜131 付近) か、見本カートより東 (x ≥ 79)。
2. 見本カートの横ずれ (`StationCartLateral`) を変える案は、ユーザーの「道に掛かって邪魔」の要望に戻るので、1 で収まらないときだけ。

## 対応 (2026-10-15)

### 決定: 候補 1 の西側。3×2 の格子のまま広場の中心の西へ移す

- 格子 (3×2、間隔 0.9 m) はそのままにして、中心を広場の中心 (72, 127) + (-4.4, +4.0) = **(67.6, 131.0)** へ移した。軌道 (Node15 → Node16 → LoopNode017) は広場を南南東から北北東へ抜けるので、その西側なら見本カート (東側、`StationSide[1]=+1`) とパネルから 8 m 離れ、軌道の中心からも 4 m 以上取れる。東 (x ≥ 79) はリード乗り場 `KH_Station_Plaza` (82.2, 123.7) と招待の板 (82〜84, 121.8〜125.3) に近く、見本カートの乗り降りの側でもあるので採らなかった。
- 見本カートの横ずれ (候補 2) は変えない。
- 高さは従来どおり地面 + 0.03 m (`DiscRestHeight`)。i は Build と同じ `Keys` の番号 (x = i % 3、z = i / 3)。

| ディスク | x | z | 軌道の中心から (xz) | 広場の中心から |
|---|---|---|---|---|
| Corgi | 66.7 | 130.55 | 5.87 m | 6.4 m |
| Pome | 67.6 | 130.55 | 4.98 m | 5.7 m |
| Lab | 68.5 | 130.55 | 4.10 m | 5.0 m |
| Dachs | 66.7 | 131.45 | 6.03 m | 6.9 m |
| Shiba | 67.6 | 131.45 | 5.15 m | 6.3 m |
| Samoyed | 68.5 | 131.45 | 4.26 m | 5.7 m |

y = 地面 + 0.03 (広場の平坦部なので ≈ 19.03)。

### 測った距離 (シーンの YAML とコードの値から。Unity 未実行)

- 軌道: 上の表の値は `KH_WalkRoute.nodePositions` の閉路 436 節の全区間 (閉じる区間を含む) に対する最短で、6 枚とも Node16 (72.0, 127.0) → LoopNode017 の区間が最寄り。最小は Lab の 4.10 m (条件 2.5 m)。
- 見本カート `StationCart_1` (76.66, 132.8) から最寄りのディスク (Samoyed) まで 8.3 m、パネル `Station_1` (76.37, 132.83) まで 8.0 m。
- ハロウィンパレードの Finale_0 から 3.1 m (統括の測定)、ライブの犬の位置 `KD_LiveDogSpot` (71.6, 124.5) から 6.8 m、洗い場 Hillside (66.4, 121.3) から 9.3 m・その棚 (67.2, 122.2) から 8.4 m、アイテム案内板 (62.5, 124.0) から 7.8 m (Corgi)、リード乗り場から 15 m。
- 広場の中心から 5.0〜6.9 m (平坦部の半径 12 m の内側。Walking/5 の「disc rests on the flat clearing」の条件は変わらない)。

### 変えたコード

- `KH_DogWalkingEditor.DiscHome(i)`: 旧ボール格子 (`BallHomeCenter` / `BallSpacing` / `BallRestHeight` / `PlazaToySpot`) からの派生をやめ、`DiscHomeOffset = (-4.4, 4.0)` (広場の中心から)、`DiscSpacing = 0.9`、`DiscRestHeight = 0.03` で直接計算する。旧ボール格子の 4 つはディスクだけが使っていたので消した (`Assets/` 全体を grep して他の参照なし)。検査の閾値 `DiscTrackClearance = 2.5`、`DiscCartMargin = 0.5`、`DiscPanelClearance = 1.0` も同じ所に置いた。
- **置き場所の持ち主を変えた**: 2026-10-15 までディスクの位置は人の持ち物 (Build は新規作成時だけ `DiscHome` に置く)。これ以後はコードの持ち物で、`DiscHome` が正。Build の `if(created)` はそのまま (既存のディスクは動かさない)。
- 新しいメニュー **Walking/5b Rehome Discs** (`RehomeDiscs`、冪等): シーンが dirty なら止まる。6 枚がそろっているのを確かめてから、`KH_Dogs/KH_Frisbee_<犬種>` を `DiscHome(i)`・回転 0 へ移し、移す前後の位置と軌道までの距離、新しい位置から 0.5 m 以内にある (地形とトリガー以外の) Collider を情報として `Temp/KH_DogPort/rehome.txt` に書く。シーンは dirty にするだけで**保存しない** (Walking/5 と Cart/2 を見てから保存)。`VRCObjectSync` の復帰先はシーン開始時の位置なので、シーンの 6 枚を動かすのはこのメニュー。
- Walking/5b は `Undo.RecordObjects` で戻せる (Codex 指摘で追加)。
- Walking/5 Verify (「all 6 discs present and apart」の後) にディスクごとの検査を足した:
  - `cart track clearance <犬種> x.xx m >= 2.50` (シーンのディスクから閉路の折れ線まで、xz)
  - `DiscHome code value clear of the cart track <犬種> ...` (コードの値そのものも 2.5 m 以上。シーンを手で動かしてもコードの値が正しいままか)
  - `disc rests at DiscHome <犬種> (xz off ..., y off ..., each <= 0.05 ...)` (y も見る。Codex 指摘)
  - `disc outside every display cart body box + 0.50 m <犬種> (nearest StationCart_s local xz ...)` (見本カート 5 台の座標系で `BodyColliderCenter ± BodyColliderSize/2` を 0.5 m 広げた箱の外。y は見ない)
  - `disc clear of every station panel <犬種> (nearest Station_s ... >= 1.00)`
  - `NavMesh within 1 m of the disc (SamplePosition; not a path check) <犬種>` (`NavMesh.SamplePosition` 1 m。経路の到達性ではなく、1 m 以内に NavMesh があることだけ。Codex 指摘で表現を直した)
  - `KH_WalkRoute` の `nodePositions` や `KH_Cart/Stations` が無いときは、その名前の FAIL を 1 つ出す (例外にしない)。見本カート・パネルの欠けも 1 項目で数える。
- Cart/2 Verify (`KH_CartSetupEditor.VerifyM3.cs` の見本カートの検査): `StationCart_s no toy (layer 13) within the body box + 0.5 m (xz, collider extent included; ...)`。有効な layer 13 の Collider を全部走査し、`bounds.center` を中心に xz の広がり (extents の大きさ) を半径とする円として、見本カートの座標系 (xz) で本体の箱 + 0.5 m と比べる (中心だけだと箱の縁を跨ぐ Collider が通る。Codex 指摘) (見本カートは Collider を持たないので物理の問い合わせは使わない)。箱の判定は `KH_CartSetupEditor.OutsideBodyXZ` に 1 つだけ置き、Walking/5 もこれを呼ぶ。
- 見本カートの座標系は回転だけで変換する (箱の寸法は m。見本カートは `localScale = 1` で作られるので `InverseTransformPoint` と同じ値)。

### 検証の方針

- ハーネスは回さない。置き場所は幾何の問題なので、Edit モードの Walking/5 と Cart/2 で決まる。カートに乗って広場を通る確認と、フリスビー遊びが変わらないことはユーザーの Play (完了条件 3)。
- 軌道の中心からの距離は節点の折れ線で測っている。走行の xz も節点間の `Vector3.Lerp` (KH_CartManager.TrackPoint) なので同じ線で、平滑化されるのは高さとピッチだけ (Codex 指摘で訂正。起票時の「滑らかにしている」は誤り)。最小 4.1 m に対して条件 2.5 m なので、その差は余裕の内。

### 適用 (2026-10-15、統括)

- 作業前の件数: Walking/5 FAIL 0、Cart/2 CART_OK (pass 3647)。シーンは別セッション (ハロウィンパレード) の未コミット分を含む保存済みの状態で、dirty なし。
- `bash scripts/unity-compile.sh` C# 0 件 → **Walking/5b** (6 枚とも 8.08 m 移動。新しい位置の 0.5 m 以内に地形以外の Collider なし。`Temp/KH_DogPort/rehome.txt`) → 保存 → **Walking/5 530 PASS / FAIL 0** (新検査の実測: 軌道から Corgi 5.87 / Pome 4.98 / Lab 4.10 / Dachs 6.03 / Shiba 5.15 / Samoyed 4.26 m、最寄りの見本カートは StationCart_1 でローカル xz (-7.6〜-9.5, -2.8〜-4.1) 対 半箱 (0.91, 1.10)、パネル Station_1 から 7.99〜9.94 m、6 枚とも NavMesh 上) → **Cart/2 CART_OK pass 3652** (作業前 +5 = 見本カート 5 台の「no toy (layer 13) within the body box + 0.5 m」、おもちゃの Collider 34 個を走査)。
- ハーネスは回していない (幾何の問題で Edit の検査で決まる)。
- シーンの差分は `KH_Frisbee_<犬種>` 6 枚の Transform だけ (別セッションの未コミット分は baseline patch の逆適用で除いて stage)。
- 残り: ユーザーの Play (完了条件 3)。

### Codex 独立レビュー (2026-10-15、gpt-6-astra。高 0 / 中 3 / 低 2、全部反映)

| 指摘 | 対応 |
|---|---|
| 中: DiscHome との比較が xz だけで、埋まっていても PASS | y の差も 0.05 m 以内を検査 |
| 中: 5b に Undo が無い | `Undo.RecordObjects` で 6 枚を登録 |
| 中: Cart/2 が Collider の中心だけを見るので縁を跨ぐと PASS | xz の広がり (extents) を半径に足して判定 |
| 低: 「犬が到達できる」は SamplePosition の結果より強い | 文言を「1 m 以内に NavMesh あり (経路の検査ではない)」に |
| 低: 「走行は折れ線を滑らかにした線」はコードと違う (xz は Lerp) | 本票の記述を訂正 |

反映後: C# 0 件、Walking/5 530 PASS / FAIL 0、Cart/2 CART_OK pass 3652。


## 完了条件

1. 6 枚のディスクが見本カート・パネルと重ならず、走行の軌道の中心から 2.5 m 以上離れた広場の平坦部にある (シーンの位置と `DiscHome` の両方)。
2. Walking/5 Verify に「各ディスクが軌道の中心から 2.5 m 以上、`StationCart_*` の AABB の外」の検査を足し、PASS。Cart Verify 側にも「乗り場の見本カートの足元に layer 13 (おもちゃ) の物がない」を足すか、どちらか一方に寄せる。
3. ユーザーの Play: 広場でカートに乗って通り、置いてあるディスクを踏まない・押さない。見本カートの下にディスクがない。犬にディスクを投げて戻ってくる遊びが今までどおりできる (置き場所を変えても `KH_FrisbeeCatch` の遊びの判定は足もとで決まる。[IMP-DOG-023](IMP-DOG-023-frisbee-anywhere.md) M4)。

## 関連

- [IMP-DOG-019 ボールとディスクの置き場](IMP-DOG-019-ball-and-disc-placement.md): `DiscHome` と置き場の経緯。
- [DES-WLD-004 自動散歩カート](DES-WLD-004-auto-walk-cart.md): 乗り場の見本カート (M3) と 2026-10-12 の横ずれの要望、障害物の掃引。
- [BUG-DOG-014](BUG-DOG-014-cart-dog-clipping.md): カートと犬の重なり (別件)。
