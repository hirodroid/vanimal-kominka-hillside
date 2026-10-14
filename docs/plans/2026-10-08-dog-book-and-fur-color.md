# 犬紹介パネル (呼び名・お気に入り) と毛色 — 2026-10-08

Issue: [DES-DOG-009](../development/issues/DES-DOG-009-instance-owner-fur-color.md)。関連: [game-design §4](../game-design.md#4-犬リード遊びの仕様)、[world-ui-spec §4](../world-ui-spec.md#4-画面と情報構造)、[移植記録](../development/imports/kominka-dog-system.md#2026-10-08-犬紹介パネルと毛色-des-dog-009)、[門前の配置](2026-10-06-spawn-area-layout.md)。

状態 (2026-10-08): コード・文書は実装担当、Unity は統括。`DogBook/0` (`KH_FurColor.asset`) → C# / U# コンパイル 0 → `DogBook/4`・`1` (板 (50.80, 2.75, 41.50) yaw 65.9) → `5` (targets 8 / textures 8、柴 2・サモエド 2、WARN 13 = この repo に無い候補) → `3 Verify` PASS → シーン保存 (`KD_DogBook` / `KH_FurColor` / `KD_PlayerDogTag` 各 1)。その後 Codex の独立レビューの指摘 3 件を反映 (§6 の受け取り前の判定・Verify の入口・この文書)。**未確認は Play (見た目)・2 人の同期と権限・実機**だけ (§9)。

第 2 波 (2026-10-08、ユーザーの回答 5 点を反映。§10): 板を看板「遊び方」の隣の**立て看板** `KH_Dogs/KH_DogBookStand` へ移す (`DogBook/2 - Build Stand And Place`、§3)、毛色を**入江の名前と順の 21 色**にする (古民家のパックのテクスチャ 13 枚を持ち込み、§5)。コードと文書は実装担当 (scratchpad の csproj で実行側・Editor 側ともコンパイル 0)。Unity での構築 (§9.1 の第 2 波の手順) は統括、Play と 2 人は未確認。

この文書の「決定」は特記なき限り**統括の設計 (AI の読み)** で、ユーザーの確認は §10 に挙げたものが残っている。ユーザーの決定は Issue の 2026-10-06 / 10-08 の行が正本。

## 1. 決定の一覧

| # | 内容 | 出どころ |
|---|---|---|
| A-1 | 古民家の犬紹介の板 (`KD_DogBook`) と訪問タグ (`KD_PlayerDogTag`、VRCPlayerObject・Manual) を**そのまま**使う (個別管理の移植。改変は最小で、改変は移植記録へ) | Issue 10-08 ユーザー指定 / 統括 |
| A-2 | 板の組み立ては古民家の Editor (`KD_DogBookEditor.Setup`) を流用し、裏山の partial (`KD_DogBookEditor.Hillside.cs`) が置き場所・見出し・毛色の行だけを足す。メニューは `Tools/KominkaHillside/DogBook/0〜5` (古民家の `Menu/Imported-Unused/Dog Book/0〜4` はそのまま残す) | 統括 |
| A-3 | お迎え・気まぐれな立ち寄り (Greeting) は**有効のまま** (コード変更なし)。根拠は §4。頻度は古民家の値のまま | 統括の指示に沿って実装担当が判断 / **2026-10-08 ユーザー「古民家の値のままで OK」** |
| A-4 | 呼び名・お気に入りの保存は PlayerData `KD_DogBook_v1` のまま (キー名は古民家と同じ)。PlayerData はワールドごとの保存なので、**値は両ワールドで共有されない** (§2) | 統括 (既定) / **2026-10-08 ユーザー「OK」** |
| A-5 | 見出しは裏山専用のキー `dogbook.title.hillside`「裏山の犬たち」(13 言語)。古民家の `dogbook.title`「庭の犬たち」は残す (裏山に「庭」は無い) | 実装担当 (ja の 1 語で判断) |
| A-6 | 状態の 1 行に裏山の 3 つを足す: 散歩中 (リード)・水遊び・洗い場 (`dogbook.state.walk` / `.water` / `.wash`)。足さないとリードの犬が「のんびり」と出る | 実装担当 |
| B-1 | 毛色は新しい U# `KH_FurColor` (1 つ、Manual 同期): `[UdonSynced] int[6]`、0 = もとの色。**書けるのはこの GameObject の Owner だけ** (§6) | Issue 10-06 ユーザー決定 / 統括 |
| B-2 | 適用は毛の材質スロットへの `MaterialPropertyBlock` (スロットごとの読み・書き戻し)。材質の実体は作らず、sharedMaterial も書かない (§7) | 統括 |
| B-3 | 色の一覧は購入パックのテクスチャだけ (作らない・加工しない)。足りない色は**古民家のパックのテクスチャを持ち込む** (`Assets/KominkaHillside/Textures/FurColors/<犬>/<色>.png`)。色名と順は**入江 (A_Cove_to_Unwind) の毛色の一覧に合わせ**、今の色 (index 0) にも入江の名前を付ける。全 21 色 (§5) | Issue 09-22 (古民家) / **2026-10-08 ユーザー (持ち込み・入江の名前)** |
| C-3 | 板は看板「遊び方」の隣・広場の側に建てる**遊び方と同じ作りの屋根つきの立て看板** `KH_DogBookStand` の canvas に載せる。見出し「裏山の犬たち」は看板の黒い見出しの板に出し、パネルの索引の見出しは隠す (§3) | **2026-10-08 ユーザー (場所と作り)** / 統括 (置き場所の探し方) / 実装担当 (見出しを 1 つに) |
| C-1 | 詳細ページ (犬 1 匹) の下段、[閉じる] の左に毛色の行 `◀ 色名 ▶ + 注記`。呼び名の [保存]・お気に入りの段 (y = -147) とは伝言の行 (y = -228) を挟んで離す。押した瞬間に全員へ反映 (確定ボタンは無い) | Issue 10-08 ユーザー指定 (操作を分ける) / 統括 |
| C-2 | マスター以外には ◀ ▶ を出さず、色名と「毛色はインスタンスのマスターが決めます」だけ。その犬にほかの色が無いときは全員に「この犬はほかの毛色がありません」 | Issue (設計で決めること) / 統括 |

## 2. 保存キー (呼び名・お気に入り) — ユーザーに聞く

`KD_DogBook.SaveKey = "KD_DogBook_v1"` は古民家と同じ名前のまま。統括の前提は「同じキー = 両ワールドで共通」だったが、VRChat の PlayerData (Persistence) は**ワールドごと・プレイヤーごと**に保存される (公式の Persistence の説明。[game-design §4](../game-design.md#4-犬リード遊びの仕様)「PlayerData はワールド別に持つ」と同じ前提)。このため**キー名が同じでも、古民家で付けた呼び名は裏山には出ない見込み** (この repo では確かめていない。2 つのワールドで同じ人が入って確かめられる)。

| 案 | 中身 | 変更 |
|---|---|---|
| **そのまま (既定)** | キー名を変えない。裏山では裏山の保存として働く (初回は空) | なし |
| 裏山の名前にする | `KH_DogBook_v1` などへ (距離フェードの `KH_DistanceFade_v1` と同じ流儀。中身は同じく別々) | `KD_DogBook.SaveKey` の 1 行 + 移植記録 |
| 両ワールドで共有したい | PlayerData では作れない (ワールドをまたぐ保存は無い)。今回の範囲外 | — |

ユーザーへの質問は「両ワールドで同じ呼び名にしたいか」。したい場合は上のとおり今の仕組みでは作れないことを伝える (§10)。

決定: 2026-10-08 ユーザー。呼び名・お気に入りが古民家と共有されないこと (PlayerData はワールドごと) は OK。キー名はそのまま。

## 3. 板の配置

決定: 2026-10-08 ユーザー「板の位置: 看板『遊び方』(Asobikata) の隣、広場の方向に、遊び方と同じ作りの立て看板 (屋根つき) を建て、そこに犬紹介パネルを作る」。最初の案 (おじいさんの左前 (50.8, 41.5) に板だけを立てる、§3.3) はやめる。

### 3.1 看板の作り

| 項目 | 値 |
|---|---|
| 置き場 | `KH_Dogs/KH_DogBookStand` (板 `KD_DogBook` はその子)。**`KH_GuideBoards` の下には置かない**: パネルは Udon・押す面 (VRCUiShape・GraphicRaycaster・trigger の BoxCollider) を持ち、Guide/2 の「案内板は読むだけ」の検査に掛かる。`Guide/1` は 3 枚 (Asobikata / Map / Kominka) だけを作り直すので、この看板には触らない |
| 作り | 案内板と同じ関数 (`KH_GuideBoardsEditor.BuildStructure` / `EnsureCanvas` / 見出しの板 `Img` + `Txt`): 背板・柱 2 本 (地面へ 0.25 m 埋める)・棟・屋根 2 枚、`KH_RiverWood`、Collider なし、Batching Static。canvas は読むだけ (World Space、1 単位 = 1 mm、表 = -Z) |
| canvas | 1130 × 920 mm = パネル 1050 × 710 + 左右 40、上 25 + 見出し 130 + 間 30 + 下 25。下端は地面 + 0.85 m (案内板は 0.80。パネルの中心が地面 + 約 1.23 m で、前の置き方の 1.25 m とほぼ同じ) |
| 見出し | 黒い見出しの板 (1090 × 130) に「裏山の犬たち」(`dogbook.title.hillside`、自動縮小 60〜96)。文言は `KD_DogBook.staticLabels` に入れて実行時に 13 言語で引く (案内板の翻訳の結び付け `UILocalizationBindings.json` は使わない: Guide/1 の検査が `KH_GuideBoards` の外の結び付けを嫌うため)。**パネルの索引ページの見出し (Heading) は隠す** (同じ題を二重に出さない。看板の見出しは詳細・説明のページを開いている間も見える) |
| パネル | 看板の子、ローカル (0, canvas の中心 + 見出しの下, -0.01 m) = canvas の面より 10 mm 手前 (詳細・説明のページはさらに 40 mm 手前)。向きは看板と同じ、1 px = 1 mm (scale 0.001) |
| 毎回作り直すもの | `Structure` と `Canvas` の下 (古いものは消す)。パネルは `DogBook/2` と `DogBook/1` が canvas の位置へ据え直す (Setup の「以後は動かさない」は、看板があるときは「看板に従う」に変えた) |

### 3.2 置き場所の探し方 (`DogBook/2` の初回だけ。以後 x / z / yaw は人のもの、y だけ地面へ据え直す)

案内板 `Asobikata` の横方向 (根の ±X) のうち `KH_Plaza` (72.0, 19.0, 127.0) のある側 (今は -X = 北北西 (-0.574, +0.819)) へ、次の順に試す (統括の指示 2026-10-08)。

1. 横の距離: Asobikata の canvas の幅 3.24 m + 0.4 m = **3.64 m** から 0.5 m 刻みで 5.64 m まで
2. 各距離で背面側 (Asobikata の +Z、読む人と反対) へ 0 → 0.25 → 0.5 → 0.75 → 1.0 m
3. 各点で yaw を Asobikata と同じ (55°) → +10° → -10° → +20° → -20°

各点 (125 通り) を判定する。**hard** = 看板の体積 (足元 × 地面 + 0.05〜2.8 m) と読む人の場所 (前 0.3〜2.0 m、地面 + 0.3〜1.9 m) に固い Collider が無い (`Physics.OverlapBox`、Terrain と trigger と人が当たらない層は除く)、地面の段差 1.2 m 以下、ほかの案内板の足元から 0.3 m 以上。**soft** = 案内板の置き場所の規則 (Guide/2 の `Placement`: 周回の道 1.8 m・犬の自由行動の中心 3.3 m・スポーン 3.0 m・おじいさん / リード台 / ようこそ板 / おやつ 2.0 m・門の線・洗い場の床・入口 18 m 以内・振り返りの視界) + ほかの案内板・写真ボードの読む場所に入らない。

- hard と soft を全部満たす**最初の点**に置く。
- 無ければ、hard を満たす最初の点 (= 指定どおりの 3.64 m・背面 0・同じ yaw から順) に置き、破った soft の規則を WARN でログに出す。`DogBook/3 Verify` はその規則を FAIL にする (**ユーザーが Play で見て、場所を変えるか規則の例外にするかを決める**)。
- hard を満たす点が 1 つも無ければ例外 (何も変えない)。
- 候補ごとの結果・採った点・破った規則は `Logs/dog-book-stand.txt` (統括が流した結果を §3.4 に写す)。

### 3.3 事前の見積もり (Unity なし。10-06 の門前の測量の座標と案内板の規則で計算した AI の読み)

10-06 の測量 (`evidence/2026-10-06-spawn-layout/layout-after.svg`、32 px/m、原点 = スポーン) の周回の戻り道 (54.30, 50.75) → (49.53, 48.56) → (44.77, 46.36)、犬の自由行動の中心 x 44 / 46 / 48・z 46 / 48 で、上の 125 点を計算した。**全部の規則を満たす点は無い見込み**。

| 候補 | 中心 | 周回の道から (1.8 以上) | 自由行動の中心から (3.3 以上) |
|---|---|---|---|
| 3.64 m・背面 0・55° (指定どおり。hard が通れば採る点) | (50.91, 47.48) | **0.54 m** | **2.06 m** ((48, 48) の輪) |
| 4.14〜5.64 m | 北北西へ | 0.0〜0.05 m (道に乗る) | 1.75〜1.85 m |
| 3.64 m・背面 1.0 m・65° (道から最も離れる組) | — | 0.77 m | 1.4 m |
| 反対側 (+X、3.64 m) | (55.09, 41.52) | 6.9 m | 7.3 m だが**門の塀の線の外 (-0.46 m)** |

広場の側は、周回の戻り道 (東北東から門前へ降りてくる道) と犬の待機の輪が Asobikata のすぐ北西にあるため、横並びでは規則を満たせない (Asobikata 自身は道から 3 m 以上)。読みが合っていれば、看板は指定どおりの 3.64 m に建ち、Verify は「周回の道」「自由行動の中心」の 2 つを FAIL にする。Play で見て、看板の脇を散歩の人と犬が通れるか (看板の枠に Collider は無いので、犬と人は柱をすり抜ける) をユーザーが決める。

### 3.4 Unity での結果 (2026-10-08)

2026-10-08 統括が流した結果: C#/U# コンパイル 0。DogBook/2: 125 通りのどれも規則を全部は満たさず (WARN)、体積が空いている最初の点に建てた = 横 3.64 m・後ろ 0・yaw 55 → **看板 (50.91, 2.39, 47.48)**、板は看板の canvas (50.90, 3.62, 47.48)、題は看板の Header「裏山の犬たち」(板の Heading は隠す)。破った規則 2 つ: 周回の戻り道の中心線まで 0.54 m (基準 1.8)、犬の待機の輪の中心まで 2.10 m (基準 3.3、半径 3)。DogBook/5: targets 8 / textures 21 (Corgi 5・Pome 4・Lab 3・Dachs 3・Shiba 4・Samoyed 2)、warn 0。DogBook/3: **FAIL 2 = 上の規則 2 つだけ** (USER DECISION)。それ以外は全部 ok (21 色・13 言語・字形・配線)。シーン保存済み (KH_Dogs/KH_DogBookStand 1 つ)。**ユーザー判断待ち: Play で現物を見て、この場所のまま (規則の例外として Verify の期待に入れる) か、場所を変えるか。**

決定: 2026-10-14 ユーザー「看板の場所も推奨で OK です。例外で」。この場所 (50.91, 2.39, 47.48) yaw 55 のまま採用し、戻り道 0.54 m と待機の輪 2.10 m の 2 規則は例外。同日、統括が `DogBook/3 Verify` を更新: 看板がその場所 (±0.05 m、±1°) にある間だけこの 2 規則を「placement rule exception」として PASS に数え、動かしたら元どおり FAIL にする (`KH_GuideBoardsEditor.DogBookStand.cs` の `StandDecidedPos`)。ほかの規則は例外にしない。

### 3.5 前の置き方 (2026-10-08 第 1 波、やめた)

おじいさん (49.90, 42.80) の左前 1.58 m、写真ボードの手前 (50.8, 41.5) に板だけを立てていた (高さ 地面 + 1.25 m、表をスポーンへ)。看板が無いときの初回の `DogBook/1` だけが今もこの仮の位置に置く (`KD_DogBookEditor.HillsideBoardXZ`)。

## 4. お迎え・立ち寄り (Greeting) の扱いと根拠

お気に入りがあると、自由な犬が 1 回だけあいさつに来て (お迎え)、ときどき立ち寄る (古民家 IMP-DOG-033 / 035)。**裏山でもそのまま有効にした** (`greetingEnabled` のような止めるスイッチは足していない)。開始できるのは「自由な犬」だけであることを、次のコードで確かめた。

| 裏山の状態 | Greeting が始まらない理由 (ファイル) |
|---|---|
| リード中 | `UpdateTick` の早い段で `UpdateHillsideLeash` が return (`KD_DogWanderController.cs` の Owner 権威の直後)。さらに開始判定 `SelfAvailable()` = `KD_GroupActivities.Available` が `hillsideLeashes._IsLeashed(i)` で false |
| 洗い場 (呼ばれて向かう・台の上・洗われ中・満足の合図) | 行動の選択より前の `UpdateHillsideWash()` が return (`UpdateWashStation` / `UpdateLeaveLowering` / `_WashHold`) |
| 水の中・水遊び | 選択の最初の `UpdateWaterPlay()` が先に取る。`Available` も `WaterPose != 0` で false |
| 集合 | `UpdateActivity()` が Greeting より前。参加しない犬も集合中は `Available` が false (`Mode == 1 && !_IsParticipant`) |
| フリスビー・ボール | `!fetchController.IsIdle \|\| FrisbeeActive` の枝が先に return。`Available` も同じ |
| そばで寝る・ライブ・おやつ・催促 | `UpdateSleep` / `UpdateLive` / `UpdateTreat` / `UpdateNudge` が Greeting より前 |
| 撫で中・休憩・眠り | `Available` が false |
| 洗い場の円の中にいる人 | 開始時と向かう途中に `KeepOutPlayer` / `KeepOutAt` で除外 (裏山改変 2026-10-02、`HillsideStationLeave.cs`) |
| 遠い人・川の対岸の人 | `GreetTooFar` (2026-10-07 破壊監査 RC-5、じゃれ合いと同じ 25 m・`_OtherBank`) |

Greeting の継続中にリードが付いた・洗い場に呼ばれた等は、上の早い段の return が毎フレーム先に取るので、Greeting はそのフレームで止まる (`FinishInterruptedGreeting` が Fetch の印を消す)。**未確認**: 散歩の人に自由な犬が付いて来る見え方、立ち寄りの頻度 (`dropByChance` 0.35 / 犬 240 s / 人 600 s は古民家の値) が裏山の広さで多すぎないか。Play で見て、多ければ値を下げるか `greetingEnabled` を足して止める (第 2 段階)。

決定: 2026-10-08 ユーザー。お迎え・立ち寄りの頻度は古民家の値のままで OK。

## 5. 毛色の表

対象スロット: 犬ごとに、毛のシェーダー (Uzumore `082bc8…` / lilToon Fur 2 pass `54bc8b…`) の材質スロットのうち、**距離フェードのスロット (Uzumore) と同じ `_MainTex` のもの** (第 1 波の Build で targets 8)。目 (`me`)・口 (`kuti`)・その他 (`sonota`) と、別のテクスチャを使う毛のスロット (柴の `ha.005` = ke、サモエドの サモエド毛) は対象外。

決定: 2026-10-08 ユーザー。①代替色のテクスチャは古民家から持ってくる。②色名は入江 (A_Cove_to_Unwind) と合わせる (13 言語の訳も入江のもの)。

根拠: 入江の毛色の一覧 (catalog) が同じ購入パックの同じ犬種について名前と順を決めており、入江と裏山で同じ犬の同じ色が別の名前になるのを避ける (統括が入江の catalog と古民家のパックから表を確定した)。今の色 (index 0) も入江の名前で出す (「もとの色」とは書かない。ラブの今のイエローを入江は「茶」と呼ぶ)。

テクスチャの出どころ: 古民家ドッグランの購入パック (同じ TORINOSUKE 犬ギミック) の ke 1〜4 などのベースカラーを、統括が `Assets/KominkaHillside/Textures/FurColors/<犬>/<色>.png` へコピーした (13 枚 + .meta。**加工なし、インポート設定はパックのまま**。購入アセットの派生物として自作の置き場へ置く。不変条件 8)。柴 1 とサモエド 1 は、この repo のパックの材質をそのまま使う (コピーしない)。

板の順 (◀ ▶) = 表の順。**全 21 色** (コーギー 5・ポメ 4・ラブ 3・ダックス 3・柴 4・サモエド 2)、色名 13 種。

| 犬 | index | キー (`furcolor.`) | ja / en | テクスチャ |
|---|---|---|---|---|
| コーギー | 0 | tanWhite | 茶白 / Tan/white | 今の色 (パック コーギー/マテリアル_Base_color.png) |
| コーギー | 1 | lightWhite | うす茶白 / Light/white | FurColors/Corgi/lightWhite.png (古民家 ke 1) |
| コーギー | 2 | darkWhite | こげ茶白 / Dark/white | FurColors/Corgi/darkWhite.png (ke 2) |
| コーギー | 3 | tricolor | 三色 / Tricolor | FurColors/Corgi/tricolor.png (ke 3) |
| コーギー | 4 | mostWhite | 白多め / White mix | FurColors/Corgi/mostWhite.png (ke 4) |
| ポメ | 0 | white | 白 / White | 今の色 |
| ポメ | 1 | black | 黒 / Black | FurColors/Pome/black.png (ke 2) |
| ポメ | 2 | cream | クリーム / Cream | FurColors/Pome/cream.png (ke 3) |
| ポメ | 3 | brown | 茶 / Brown | FurColors/Pome/brown.png (ke 4) |
| ラブ | 0 | brown | 茶 / Brown | 今の色 (入江は今のイエローを「茶」と呼ぶ) |
| ラブ | 1 | white | 白 / White | FurColors/Lab/white.png (ke 2) |
| ラブ | 2 | black | 黒 / Black | FurColors/Lab/black.png (ke 3) |
| ダックス | 0 | brown | 茶 / Brown | 今の色 |
| ダックス | 1 | light | うす茶 / Light tan | FurColors/Dachs/light.png (ke 2) |
| ダックス | 2 | blackTan | 黒茶 / Black/tan | FurColors/Dachs/blackTan.png (ke 3) |
| 柴 | 0 | red | 赤茶 / Red | 今の色 (マテリアル_Base_color 2.png) |
| 柴 | 1 | light | うす茶 / Light tan | この repo のパック 柴犬_remake/material/ke.mat (マテリアル_Base_color.png。第 1 波の「淡い赤」) |
| 柴 | 2 | dark | こげ茶 / Dark tan | FurColors/Shiba/dark.png (ke 2) |
| 柴 | 3 | white | 白 / White | FurColors/Shiba/white.png (ke 3) |
| サモエド | 0 | white | 白 / White | 今の色 |
| サモエド | 1 | black | 黒 / Black | この repo のパック サモエド/mat/サモエド毛.mat (DefaultMaterial.001_Base_Color 2.png) |

実装: `KH_FurColorEditor.Table` が上の表そのもの (犬ごとの先頭行 = 今の色、material / texture とも null)。`DogBook/5` は表の行の順に配線し、見つからない行・同じテクスチャ・置き場にあって表に無いファイルを WARN にする。続けて色名の ja / en の字形を共通フォントへ足し、`KD_Language` の表を作り直す (`Configure`。DogBook/1 と同じ)。`DogBook/3` は「21 色・犬ごとの数・各色が表の行のテクスチャ・13 色名が 13 言語にある・使わない `furcolor.*` が無い・`KD_Language` に色名がある」を数え、各色の平均色を NOTE に出す。

**同じ UV 配置のテクスチャであること**が前提 (古民家と同じパックなので同じはず。柴の `ke.mat` は remake 前の配置の可能性があり、Play で 1 枚見る)。テクスチャ 13 枚ぶん容量が増える (`Build Size/` で測る)。

## 6. 同期と権限

| 項目 | 内容 |
|---|---|
| 置き場 | `KH_Dogs/KH_FurColor` (UdonBehaviour 1 つ、Manual。VRCObjectSync は付けない) |
| 同期変数 | `[UdonSynced] int[] _colors = new int[6]` (24 byte)。0 = もとの色、それ以外は犬ごとの一覧の番号 |
| 書く人 | **この GameObject の Owner だけ** (`_Set` / `_Step` の入口で `_CanEdit()`)。シーンのオブジェクトの Owner は既定でインスタンスのマスターで、Owner が退出すると VRChat が (新しい) マスターへ渡す。`isMaster` は読まない (vrc-networking §4「Master を機能のゲートに使わない。Ownership で組む」) |
| 取られない | `OnOwnershipRequest` は常に false。ワールドの中に `SetOwner` を呼ぶコードは無い (退出時の自動の受け渡しは要求ではないので止まらない) |
| 受け取る前に書かない | `_CanEdit()` は Owner であることに加え、受信済み (`OnDeserialization`) か `Networking.IsNetworkSettled` か「初代 Owner」のときに限る。初代 Owner = 自分の `OnPlayerJoined` の時点で Owner・1 人だけで、他人の `OnPlayerJoined` を一度も見ていない (Owner でなくなったら外す)。**人数 1 だけでは許さない** (Codex 2026-10-08 中: 途中参加して何も受け取らないうちに旧 Owner が抜けた唯一の残存者も 1 人なので、その 0 の配列で全部の犬を元へ戻せてしまう)。残る穴: 自分の入室の通知より前に旧 Owner が抜けた場合は初代と区別できない (KH_AutoNight と同じ割り切り) |
| 送る | 変わった時だけ `RequestSerialization`。Owner は自分の画面へすぐ適用する (Owner には `OnDeserialization` が来ない) |
| 受ける | `OnDeserialization` で 6 匹ぶんを適用。適用は「前に書いた番号と違う犬」だけ (毎フレーム書かない) |
| 途中参加 | 同期変数が届けば `OnDeserialization` で同じ色になる (一度も変えていなければ全員 0 のまま) |
| マスター交代 | 色は残る (新しい Owner は最後に受け取った配列を持っている)。新しいマスターが変えられる |
| 既知の限界 | マスターが変えた直後 (送信が誰にも届く前) に退出すると、その変更は失われる。一覧の長さが違う版の値 (範囲外) は 0 として表示する |

UI: 板の行 (`KD_DogBook.RefreshFur`) は 0.5 s ごとの更新で `_CanEdit()` を見て ◀ ▶ を出し分ける (マスターが替われば 0.5 s 以内に入れ替わる)。

## 7. 毛の材質を触るほかの仕組みとの共存

| 仕組み | 触るもの | ぶつからない根拠 |
|---|---|---|
| 距離フェード (`KD_DistanceFadeSettings._ApplyCurrent`) | 毛の Uzumore スロットの PropertyBlock に `_DistanceFade` / `_DistanceFadeColor` | 同じ手順 (`Clear` → `GetPropertyBlock(block, slot)` → 自分の値だけ足す → `SetPropertyBlock(block, slot)`) で、相手の値を読み戻してから書く。毛色は `_MainTex` / `_BaseMap` だけ、フェードは 2 つの値だけ。どちらが後でも残る。Verify が「フェードのスロットは毛色の対象にも入っている」を数える |
| 洗い (`KH_DogWash`) | 別の SkinnedMeshRenderer `shellRenderer` の `.material` 複製と、マスク用の材質 | 胴体の Renderer・材質・PropertyBlock は触らない (`KH_DogWash.cs` で `shellRenderer` / `maskMaterialHolder` 以外の Renderer を書いていないことを確かめた)。殻のシェーダー `KH_WashShell` は胴体の `_MainTex` を読まない。汚れ・泡は毛色の上に重なる (黒い犬では泥が見えにくい可能性 → Play で見る) |
| 撫でパーティクル (`KD_PetFxBox` 等) | パーティクルの Renderer | 犬の胴体は触らない |
| キャンディの衣装の光 (`KD_CandyDogPerformance.CostumeGlow`) | 衣装の Renderer のスロット 0 の `_EmissionGain` | 衣装は `KD_CandyCostume_` の下で毛色の対象外 |
| 飾り・首輪 (`KD_Acc_*` / `KH_Collar_*`) | 表示の切り替え | 対象外の部品。PropertyBlock は SetActive をまたいで残る |
| 写真・動画 | カメラで撮る | 実物を撮るので毛色が写る |

材質アセット (TORINOSUKE の `.mat`) は書かない。PropertyBlock はシーンにも保存されない (実行時だけ)。

**毛色が反映されないもの (既知の制限)**: 犬紹介の板の写真 (`Textures/DogBook/<key>.png`、焼いた時の色)、手元メニューのかざりの試着・呼ぶのポーズのミニチュア (焼いたメッシュと元の材質)。これらは「もとの色」のまま。

## 8. 画面と文言

詳細ページ (1050 × 710 px) の下段 y = -309:

| 部品 | 位置 (px) | 大きさ | 中身 |
|---|---|---|---|
| ◀ `FurPrev` | (-485, -309) | 64 × 58 | `_FurPrev` (Owner で色が 2 つ以上のときだけ表示) |
| 色名 `FurValue` | (-370, -309) | 160 × 58 | 26pt (自動縮小 16〜26)。例「茶白」「こげ茶白」(今の色も入江の名前で出す) |
| ▶ `FurNext` | (-255, -309) | 64 × 58 | `_FurNext` (同上) |
| 注記 `FurNote` | (-15, -309) | 400 × 62 | 20pt (自動縮小 14〜20、折り返し 2 行) |
| (既存) [閉じる] | (350, -309) | 275 × 62 | 注記の右端 185 < [閉じる] の左端 212 |

注記の出し分け: Owner (マスター) = `furcolor.label`「毛色 (マスターだけが変えられます)」/ ほかの人 = `furcolor.master.only`「毛色はインスタンスのマスターが決めます」/ その犬にほかの色が無い = `furcolor.none`「この犬はほかの毛色がありません」。

新しいキー (UITranslations.json の 13 言語すべて。注記と見出しは **AI 作成・校正なし**、色名は入江の訳):

| キー | ja |
|---|---|
| `dogbook.title.hillside` | 裏山の犬たち |
| `dogbook.state.walk` / `.water` / `.wash` | いまは、散歩に出かけています / いまは、水遊びをしています / いまは、洗ってもらっています |
| `furcolor.label` / `.master.only` / `.none` | 上の注記 3 つ (`furcolor.default`「もとの色」は第 2 波で削った) |
| `furcolor.tanWhite` / `.lightWhite` / `.darkWhite` / `.tricolor` / `.mostWhite` / `.white` / `.black` / `.cream` / `.brown` / `.light` / `.blackTan` / `.red` / `.dark` | §5 の表。**13 言語とも入江の `coat.*` の訳をそのまま** (第 2 波。第 1 波の lightred / choco と旧訳は削った) |

字形: 板は共通 UI フォント (Static) を使う。`DogBook/1 Setup` (古民家の Setup) が `KD_LocalizationEditor.BundledCharacters` (ja・en の全文) を焼くので、新しい漢字 (毛・色・黒・赤・淡 など) もそこで入る。ほかの 11 言語は VRChat 内蔵の字形に任せる (client 言語、既存と同じ)。◀ ▶ は写真の頁送りと同じ字で既にある。

## 9. 検証

### 9.1 Unity (2026-10-08 統括が実施、すべて通過)

| 順 | メニュー / 作業 | 結果 |
|---|---|---|
| 0 | `Tools/KominkaHillside/DogBook/0 - Create Program Assets` → コンパイル待ち | `KH_FurColor.asset` 作成 (このメニューより前は `KD_DogBook` が `KH_FurColor` を参照するので U# のコンパイルが失敗する) |
| 1 | `bash scripts/unity-compile.sh --udon` | C# / U# 0 エラー |
| 2 | `DogBook/4 - Bake Portraits` | `Textures/DogBook/<key>.png` 6 枚 |
| 3 | `DogBook/1 - Setup` | 板 (50.80, 2.75, 41.50) yaw 65.9 |
| 4 | `DogBook/5 - Build Fur Colors` | targets 8 / textures 8 (柴 2・サモエド 2・ほか 1)、WARN 13 (この repo に無い候補) |
| 5 | `DogBook/3 - Verify` | PASS |
| 6 | シーン保存 | `KD_DogBook` / `KH_FurColor` / `KD_PlayerDogTag` 各 1 |

レビュー反映 (§6 の初代 Owner、Verify の入口の分岐) の後は、再コンパイル → `DogBook/3` → 保存を統括がもう一度回す。Verify の入口: 共通の `Verify()` は板が裏山の Setup のもの (`KH_Dogs` 直下、または見出しが `dogbook.title.hillside`) のときだけ裏山の検査をし、それ以外は古民家の「`KD_Places` の下」の検査だけをする (古民家の Setup の板を裏山の検査で FAIL にしない)。

Codex の独立レビュー (2026-10-08) で確認済み: U# の公開 (`MaterialPropertyBlock.SetTexture` / `Renderer.Get/SetPropertyBlock(block, slot)`)、距離フェード・洗いとの材質の共存 (§7)、Greeting の分岐 (§4)、`KD_DogBook` の改変の範囲。

### 9.1b 第 2 波の手順 (統括、未実施)

| 順 | メニュー / 作業 | 期待 |
|---|---|---|
| 0 | refresh → `bash scripts/unity-compile.sh --udon` | C# / U# 0 エラー。新しいファイル `KH_GuideBoardsEditor.DogBookStand.cs` の `.meta` が出来る (コミットする)。`KH_FurColor` は定数 `DefaultKey` を外しただけ (U# の再コンパイルあり) |
| 1 | (必要なら) `DogBook/1 - Setup` | 字形 (色名の新しい漢字) と `KD_Language` の表。板はまだ看板が無いので動かない (WARN「看板に載っていない」) |
| 2 | `DogBook/2 - Build Stand And Place` | `KH_Dogs/KH_DogBookStand` が出来て板が載る。`Logs/dog-book-stand.txt` に候補 125 通りの結果と採った点。§3.3 の読みどおりなら「WARN no point passes every rule … PLACED at the first point with a clear volume」|
| 3 | `DogBook/5 - Build Fur Colors` | `RESULT targets=8 textures=21 (dogs: Corgi=5 Pome=4 Lab=3 Dachs=3 Shiba=4 Samoyed=2) warn=0`。字形の追加と `Configure` も行う |
| 4 | `DogBook/3 - Verify` | 毛色・看板の作り・パネルの位置は ok。置き場所の規則 (§3.3: 周回の道・自由行動の中心) は FAIL の見込み = ユーザー判断待ち。それ以外の FAIL は不具合 |
| 5 | シーン保存 | `KH_DogBookStand` 1、`KD_DogBook` 1 (親が看板)、`KH_FurColor` の textures 21 |

`Guide/1` は回さなくてよい (3 枚の案内板は変わらない)。`Guide/2` の検査対象にも入らない。

### 9.2 ハーネスは今回は回さない

Codex の相談の結論 (a): **回さない**。Owner の権限・交代・途中参加・見た目は、2 人の実機でしか証明できない (ClientSim 1 人では自分が常に Owner で、非 Owner の拒否・本当の途中参加・交代を作れない)。Course ハーネスのケース案 `FC-1` (▶ で柴の `_MainTex` が替わり `_DistanceFade` は残る / ◀ でもとのテクスチャを入れ直す / ヒープの `_colors` と `OnDeserialization` で模擬の途中参加) は記録として残すが、作っていない (差し込み先の `KH_CourseWalkHarnessEditor.cs` / `.Travel.cs` は別セッションの作業中でもあった)。

### 9.3 2 人で見る順番 (チェックリスト B81〜B85)

A = マスター (最初に入る人)、B = あとから入る人。

1. **単独開始 → 通常同期**: A が柴・サモエドの色を変える → B が入る (操作なしで同じ色に復元) → A がもう一度変える・もとの色へ戻す → B に届く。触っていない別の犬は変わらない。
2. **権限と交代**: B の板には ◀ ▶ が無い。A が退出 → 色はそのままで B に ◀ ▶ が出て変えられる。A が再参加しても A には編集の権限が戻らない。
3. **未受信での交代**: B が入った直後 (読み込み中) に A が退出。初期の同期が終わるまで B の ◀ ▶ は出ず、受け取った後に出る。触っていない犬がもとの色へ戻らない。
4. **描画の共存**: 距離フェードを別の設定 (深め・カスタム) にしてから色を変える / 色を変えてからフェードを変える (両方の順)。柴の模様がずれない、サモエドの目・口・歯が黒くならない、洗っている間の泥と泡が乗る、もとの色へ戻る。
5. **Greeting と個人設定**: お気に入りの犬がリード中・洗い場に呼ばれている間はあいさつが始まらない。あいさつの途中でリードを付ける・洗い場に呼ぶと止まる。呼び名の保存・お気に入りの切り替えで毛色が変わらない。

Android (Quest) を使えるなら、マスターのアプリをバックグラウンドへ移したときの交代 (色が残る・残った人が変えられる) も見る。


## 10. ユーザーへの質問と回答

2026-10-08 に 5 点を聞き、同日に回答があった (正本は Issue の決定の行)。

| # | 質問 | 回答 (2026-10-08 ユーザー) | 反映 |
|---|---|---|---|
| 1 | 足りない毛色のテクスチャをどこから持ってくるか | 古民家から持ってくる | 統括が 13 枚を FurColors へコピー。§5 |
| 2 | 呼び名・お気に入りを両ワールドで共有したいか | 共有されない (PlayerData はワールドごと) のは OK | キー名はそのまま。§2 |
| 3 | 板の位置 | 看板「遊び方」の隣、広場の方向に、遊び方と同じ作りの立て看板 (屋根つき) を建て、そこに作る | `KH_DogBookStand`。§3 |
| 4 | 色名の目視 | 色名は入江と合わせる | 入江の名前・順・13 言語の訳。§5 |
| 5 | お迎え・立ち寄りの頻度 | 古民家の値のままで OK | 変更なし。§4 |

残り: 看板の置き場所が案内板の規則 (周回の道・犬の自由行動の円) を破る見込み (§3.3)。統括が `DogBook/2` を流した結果と Play の見え方で、ユーザーが場所か例外かを決める。

## 11. 実装メモ

| ファイル | 内容 |
|---|---|
| `Assets/KominkaHillside/Scripts/KH_FurColor.cs` (新規) | 同期・権限・適用 (§6・7) |
| `Assets/KominkaHillside/Scripts/Editor/KH_FurColorEditor.cs` (新規) | `DogBook/5 - Build Fur Colors`、候補の表、平均色、`VerifyInto` |
| `Imported/KominkaDogRun/Editor/KD_DogBookEditor.Hillside.cs` (新規) | `DogBook/0〜4`、置き場所、見出し、毛色の行、Verify の差し込み |
| `Imported/KominkaDogRun/Editor/KD_DogBookEditor.cs` (改変) | `partial` 化。Verify の入口で `ClassifyBoard` が裏山の板と判定したときだけ `VerifyPlacement` / `VerifyHillside`、それ以外は元の「KD_Places の下」 |
| `Imported/KominkaDogRun/Scripts/KD_DogBook.cs` (改変) | `furColor` と行の描画 (`RefreshFur`)・`_FurPrev` / `_FurNext`、状態の 3 つ |
| `Imported/KominkaDogRun/Data/UITranslations.json` | 第 1 波 16 キー × 13 言語。第 2 波で色名を入江の 13 キーへ (furcolor.default / lightred / choco を削除、§8) |
| `Scripts/Editor/KH_GuideBoardsEditor.DogBookStand.cs` (新規、第 2 波) | 看板 `KH_DogBookStand` の作り・置き場所の探索・Verify (`KH_GuideBoardsEditor` の partial。既存の 3 枚のコードは変えない) |
| `Textures/FurColors/<犬>/<色>.png` (第 2 波、統括がコピー) | 古民家のパックのベースカラー 13 枚 (加工なし) |

未確認 (2026-10-08 時点): Play での見た目 (柴の淡い色の UV 配置、黒い犬の泥、板の読みやすさ・位置)、2 人の同期と権限 (§9.3 の 1〜5)、Quest / iOS の見え方 (lilToon の毛の Fallback で `_MainTex` が効くか)、容量 (今の 2 色はシーンで使われているテクスチャなので増えない見込み)。Unity の構築・Verify・U# のコンパイルは §9.1 で通過。
