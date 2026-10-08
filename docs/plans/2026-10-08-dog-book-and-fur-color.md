# 犬紹介パネル (呼び名・お気に入り) と毛色 — 2026-10-08

Issue: [DES-DOG-009](../development/issues/DES-DOG-009-instance-owner-fur-color.md)。関連: [game-design §4](../game-design.md#4-犬リード遊びの仕様)、[world-ui-spec §4](../world-ui-spec.md#4-画面と情報構造)、[移植記録](../development/imports/kominka-dog-system.md#2026-10-08-犬紹介パネルと毛色-des-dog-009)、[門前の配置](2026-10-06-spawn-area-layout.md)。

状態 (2026-10-08): コード・文書は実装担当、Unity は統括。`DogBook/0` (`KH_FurColor.asset`) → C# / U# コンパイル 0 → `DogBook/4`・`1` (板 (50.80, 2.75, 41.50) yaw 65.9) → `5` (targets 8 / textures 8、柴 2・サモエド 2、WARN 13 = この repo に無い候補) → `3 Verify` PASS → シーン保存 (`KD_DogBook` / `KH_FurColor` / `KD_PlayerDogTag` 各 1)。その後 Codex の独立レビューの指摘 3 件を反映 (§6 の受け取り前の判定・Verify の入口・この文書)。**未確認は Play (見た目)・2 人の同期と権限・実機**だけ (§9)。

この文書の「決定」は特記なき限り**統括の設計 (AI の読み)** で、ユーザーの確認は §10 に挙げたものが残っている。ユーザーの決定は Issue の 2026-10-06 / 10-08 の行が正本。

## 1. 決定の一覧

| # | 内容 | 出どころ |
|---|---|---|
| A-1 | 古民家の犬紹介の板 (`KD_DogBook`) と訪問タグ (`KD_PlayerDogTag`、VRCPlayerObject・Manual) を**そのまま**使う (個別管理の移植。改変は最小で、改変は移植記録へ) | Issue 10-08 ユーザー指定 / 統括 |
| A-2 | 板の組み立ては古民家の Editor (`KD_DogBookEditor.Setup`) を流用し、裏山の partial (`KD_DogBookEditor.Hillside.cs`) が置き場所・見出し・毛色の行だけを足す。メニューは `Tools/KominkaHillside/DogBook/0〜5` (古民家の `Menu/Imported-Unused/Dog Book/0〜4` はそのまま残す) | 統括 |
| A-3 | お迎え・気まぐれな立ち寄り (Greeting) は**有効のまま** (コード変更なし)。根拠は §4 | 統括の指示に沿って実装担当が判断 |
| A-4 | 呼び名・お気に入りの保存は PlayerData `KD_DogBook_v1` のまま (キー名は古民家と同じ)。ただし PlayerData はワールドごとの保存なので、**値は両ワールドで共有されない見込み** (§2)。共有したいかはユーザーに聞く | 統括 (既定) / ユーザー待ち |
| A-5 | 見出しは裏山専用のキー `dogbook.title.hillside`「裏山の犬たち」(13 言語)。古民家の `dogbook.title`「庭の犬たち」は残す (裏山に「庭」は無い) | 実装担当 (ja の 1 語で判断) |
| A-6 | 状態の 1 行に裏山の 3 つを足す: 散歩中 (リード)・水遊び・洗い場 (`dogbook.state.walk` / `.water` / `.wash`)。足さないとリードの犬が「のんびり」と出る | 実装担当 |
| B-1 | 毛色は新しい U# `KH_FurColor` (1 つ、Manual 同期): `[UdonSynced] int[6]`、0 = もとの色。**書けるのはこの GameObject の Owner だけ** (§6) | Issue 10-06 ユーザー決定 / 統括 |
| B-2 | 適用は毛の材質スロットへの `MaterialPropertyBlock` (スロットごとの読み・書き戻し)。材質の実体は作らず、sharedMaterial も書かない (§7) | 統括 |
| B-3 | 色の一覧は購入パックのテクスチャだけ (作らない)。Editor の表が「犬種 → 候補 (材質名 / テクスチャ) → 色名キー」を持ち、**ファイルがあるものだけ**配線する。無いものは WARN で一覧に出る (§5) | Issue 09-22 (古民家) / 統括 |
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

## 3. 板の配置

初回の `DogBook/1 Setup` だけが置き、以後の Setup は人の位置を動かさない (`DogBook/2` で初期位置へ戻せる)。**最終位置はユーザーが現物で決める**ので、位置は Editor の定数 (`KD_DogBookEditor.HillsideBoardXZ` 等) にしてある。

| 項目 | 値 |
|---|---|
| 親 | `KH_Dogs` 直下 `KD_DogBook` (訪問タグ `KD_PlayerDogTag` も `KH_Dogs` 直下) |
| 位置 (x, z) | **(50.8, 41.5)** = おじいさん (49.90, 42.80、向き 235°) の左前 1.58 m、写真ボードの手前。高さは地面 (レイ) + 1.25 m (板の縦 0.71 m → 下端は地面 + 0.9 m。古民家の Layout Wall と同じ) |
| 向き | 表 (-Z 側) をスポーン `KH_Entrance` へ向ける (`LookRotation(板 - スポーン)`、約 66°) |
| 大きさ | 1050 × 710 px × 0.001 = 1.05 × 0.71 m (古民家と同じ) |

選び方 (2026-10-08、門前の配置の調査の座標で格子 0.1 m を探索。板の幅 1.05 m を 7 点で見る): 次を満たす点のうち、おじいさんに近いもの。門前は設備が詰まっていて、全部を余裕で満たす点は無い。

| 条件 | 基準 | (50.8, 41.5) |
|---|---|---|
| おじいさん | 1.5〜3.0 m | 1.58 m |
| おじいさんの話す板 `KD_ShopKeeperMenu` | 1.5 m 以上 | 2.28 m |
| 写真ボード (中心) / 看板 A / ようこそ板 / リード台 | 1.5 m 以上 | 1.66 / 3.72 / 3.43 / 6.0 m |
| 写真ボードを読む場所 (表の前 0.3〜2.0 m、幅 2.32 m。`KH_PhotoGalleryEditor` の BoardReaders と同じ形) | 入らない | 横へ 0.2 m 外 |
| 看板 A を読む場所 (表の前 0.3〜2.0 m、幅 3.32 m) | 入らない | 入らない |
| スポーン | 3.0 m 以上 | 3.46 m |
| 犬の自由行動の中心 `KH_Roam_*` (半径 3 m) | 3.3 m 以上 | 5.30 m |
| 周回の戻り道 (49.5, 48.6)→(44.0, 45.7) | 1.8 m 以上 | 6.9 m |
| スポーン→おじいさんの線 | 0.8 m 以上 (おじいさんの陰にならない) | 1.5 m |
| スポーン→看板 A / 写真ボードの視線 | — (参考) | 0.41 / **0.12 m** |

**得失**: スポーンに立った位置からは、写真ボードの左半分にこの板が少し重なる (板の端が視線から 0.12 m)。写真ボードへ歩けば重ならない。ほかの候補:

| 候補 | おじいさん | 得 | 失 |
|---|---|---|---|
| (51.4, 42.4) (最初の案) | 1.55 m | スポーンからの視線に掛からない | **写真ボードを読む場所の中に立つ** (却下) |
| (50.8, 40.1) 門の右の塀ぎわ | 2.85 m | 写真ボード・看板を塞がない | 門のすぐ横、スポーンの真右 (振り向かないと見えない) |
| (53.5, 42.2) 写真ボードと看板 A のあいだ | 3.65 m | 視線に掛からない (1.3 m 以上) | おじいさんから遠い。2 枚の読む場所の間に挟まる (どちらにも 0.2〜0.5 m) |

おじいさんの右手 (北西) は犬の自由行動の円 (ラブ (48, 46)) に入り、背中側は看板 A の正面を塞ぐため外した。`DogBook/3 Verify` が距離・写真ボードを読む場所・高さ・向きを検査し、視線の余裕を NOTE に出す。

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

## 5. 毛色の表

対象スロット: 犬ごとに、毛のシェーダー (Uzumore `082bc8…` / lilToon Fur 2 pass `54bc8b…`) の材質スロットのうち、**距離フェードのスロット (Uzumore) と同じ `_MainTex` のもの**。今の本シーンでは、コーギーの `karada` (ke_Uzumore) と `ha.006` (ke)、ラブの `karada` と `ha.001` などが該当する見込み (Build のログで確定)。目 (`me`)・口 (`kuti`)・その他 (`sonota`) と、別のテクスチャを使う毛のスロット (柴の `ha.005` = ke、サモエドの サモエド毛) は対象外。

色名は**すべて推定** (古民家 DES-DOG-012 の材質名の言葉か、テクスチャの平均色)。

| 犬 | 候補 (材質名) | 色名キー | この repo | 備考 |
|---|---|---|---|---|
| コーギー | ke 1 / ke 2 / ke 3 / ke 4 | tricolor / black / cream / brown | **無し** | 古民家の資料は「ke 1〜4」とだけ。色名は推定 |
| ポメ | ke 2 / ke 3 / ke 4 | black / cream / brown | **無し** | |
| ラブ | ke 2 / ke 3 | white / black | **無し** | |
| ダックス | ke 2 / ke 3 | red (明るいレッド) / choco | **無し** | |
| 柴 | ke | lightred | **有り** | `柴犬_remake/material/ke.mat` → `shiba/マテリアル_Base_color.png` (平均 #bca485。今の ke 1 = `Base_color 2` #cc927a より淡い)。シーンでも `ha.005` が使っている (容量は増えない) |
| 柴 | ke 2 / ke 3 | black / white | **無し** | |
| サモエド | サモエド毛 | black | **有り** | `サモエド/mat/サモエド毛.mat` → `samoedo/DefaultMaterial.001_Base_Color 2.png` (平均 #312b2b。今の白 `Base_Color 1` と、毛の部分だけ白黒が逆の配置)。古民家の資料は「毛 2 黒」だが、この repo では 毛 2 が今の白、毛 (2 なし) が黒。シーンでも別の Renderer が使っている (容量は増えない) |

この repo に残っているベースカラーの PNG (2026-10-08、平均色を縮小して測った推定):

| ファイル | 平均色 | 読み |
|---|---|---|
| もちぽめ `Material_Base_Color.png` | #e5d8d8 | 今の毛 (白〜クリーム) |
| もちぽめ `DefaultMaterial_Base_Color.png` | #e28383 | 口 (sonota) |
| コーギー `マテリアル_Base_color.png` / `.001` | #d1aa91 / #1f1f1f | 今の毛 / 目 |
| ラブ `マテリアル_Base_color.png` / `.001` | #b88162 / #272728 | 今の毛 / 目 |
| ダックス `Material_Base_Color.png` / `DefaultMaterial.002` | #a46637 / #ee8c8c | 今の毛 (目も同じ絵) / 口 |
| 柴 `マテリアル_Base_color 2.png` / `マテリアル_Base_color.png` / `.001` | #cc927a / #bca485 / #0c0c0c | 今の毛 / **淡い色の候補** / 目 |
| サモエド `Base_Color 1` / `Base_Color` / `Base_Color 2` | #e1dbdb / #e0dada / #312b2b | 今の毛 / 目の材質 (同じ白) / **黒の候補** |

**持ち込み**: 足りない色は、購入パックの別の材質・テクスチャをユーザーが用意する (統括が聞く)。置き方は 2 通り。

- 購入パックのフォルダに材質 (`ke 2.mat` など) が入れば、表の行がそのまま拾う (表の材質名と同じ名前)。
- テクスチャだけなら、`Assets/KominkaHillside/Textures/FurColors/<犬の key>/<色>.png` にコピーして置く (key = Corgi / Pome / Lab / Dachs / Shiba / Samoyed、色 = black / white / cream / brown / red / lightred / tricolor / choco)。購入パックのフォルダは編集しない (不変条件 8)。

どちらも `DogBook/5` を流し直すと一覧に入る。**同じ UV 配置のテクスチャであること**が前提 (違う配置だと模様がずれる)。柴の淡い色は remake 前の配置かもしれず、Play で 1 枚見て確かめる。テクスチャが増えると容量が増える (`Build Size/` で測る)。

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
| 色名 `FurValue` | (-370, -309) | 160 × 58 | 26pt (自動縮小 16〜26)。例「もとの色」「黒」 |
| ▶ `FurNext` | (-255, -309) | 64 × 58 | `_FurNext` (同上) |
| 注記 `FurNote` | (-15, -309) | 400 × 62 | 20pt (自動縮小 14〜20、折り返し 2 行) |
| (既存) [閉じる] | (350, -309) | 275 × 62 | 注記の右端 185 < [閉じる] の左端 212 |

注記の出し分け: Owner (マスター) = `furcolor.label`「毛色 (マスターだけが変えられます)」/ ほかの人 = `furcolor.master.only`「毛色はインスタンスのマスターが決めます」/ その犬にほかの色が無い = `furcolor.none`「この犬はほかの毛色がありません」。

新しいキー (UITranslations.json の 13 言語すべて。**AI 作成・校正なし**):

| キー | ja |
|---|---|
| `dogbook.title.hillside` | 裏山の犬たち |
| `dogbook.state.walk` / `.water` / `.wash` | いまは、散歩に出かけています / いまは、水遊びをしています / いまは、洗ってもらっています |
| `furcolor.label` / `.master.only` / `.none` / `.default` | 上の注記 3 つ / もとの色 |
| `furcolor.black` / `.white` / `.cream` / `.brown` / `.red` / `.lightred` / `.tricolor` / `.choco` | 黒 / 白 / クリーム / 茶 / 赤 / 淡い赤 / トライカラー / チョコ |

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


## 10. ユーザーに聞くこと

1. **テクスチャの持ち込み**: §5 の「無し」の色 (コーギー 4・ポメ 3・ラブ 2・ダックス 2・柴 2) の材質かテクスチャを、購入パックのどこから持ってくるか。今のままなら選べるのは柴 (淡い赤) とサモエド (黒) だけ。
2. **保存キー**: 呼び名・お気に入りを両ワールドで同じにしたいか。PlayerData はワールドごとの保存なので、今の仕組みでは共有できない見込み (§2)。キー名を裏山の名前へ変えるかも合わせて。
3. **板の位置**: おじいさんの左前 1.58 m・写真ボードの手前 (50.8, 41.5) でよいか。スポーンからは写真ボードに少し重なる。ほかの 2 候補と合わせて現物で決める (§3)。
4. **色名の目視**: 柴の「淡い赤」、サモエドの「黒」、持ち込んだ色の名前が見た目と合っているか (平均色からの推定)。
5. **お迎え・立ち寄り**: 裏山でも有効にした (§4)。散歩中に自由な犬が寄ってくる頻度でよいか。

## 11. 実装メモ

| ファイル | 内容 |
|---|---|
| `Assets/KominkaHillside/Scripts/KH_FurColor.cs` (新規) | 同期・権限・適用 (§6・7) |
| `Assets/KominkaHillside/Scripts/Editor/KH_FurColorEditor.cs` (新規) | `DogBook/5 - Build Fur Colors`、候補の表、平均色、`VerifyInto` |
| `Imported/KominkaDogRun/Editor/KD_DogBookEditor.Hillside.cs` (新規) | `DogBook/0〜4`、置き場所、見出し、毛色の行、Verify の差し込み |
| `Imported/KominkaDogRun/Editor/KD_DogBookEditor.cs` (改変) | `partial` 化。Verify の入口で `ClassifyBoard` が裏山の板と判定したときだけ `VerifyPlacement` / `VerifyHillside`、それ以外は元の「KD_Places の下」 |
| `Imported/KominkaDogRun/Scripts/KD_DogBook.cs` (改変) | `furColor` と行の描画 (`RefreshFur`)・`_FurPrev` / `_FurNext`、状態の 3 つ |
| `Imported/KominkaDogRun/Data/UITranslations.json` | 16 キー × 13 言語 (§8) |

未確認 (2026-10-08 時点): Play での見た目 (柴の淡い色の UV 配置、黒い犬の泥、板の読みやすさ・位置)、2 人の同期と権限 (§9.3 の 1〜5)、Quest / iOS の見え方 (lilToon の毛の Fallback で `_MainTex` が効くか)、容量 (今の 2 色はシーンで使われているテクスチャなので増えない見込み)。Unity の構築・Verify・U# のコンパイルは §9.1 で通過。
