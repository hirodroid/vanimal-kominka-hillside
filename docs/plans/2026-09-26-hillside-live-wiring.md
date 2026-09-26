# 裏山ライブ「紅葉が星になる」をシーンで再生する配線計画 (調査・順序・Play チェックリスト)

状態: **調査と計画だけ (2026-09-26)。C#・シーン・データは未変更。** 次の波で実装する。演目の中身は Live Generator の [hillside-dusk 企画001](../../../../vrc-live-generator/docs/v2/kominka-hillside/concepts/hillside-dusk/001/README.md) ([Q&A](../../../../vrc-live-generator/docs/v2/kominka-hillside/concepts/hillside-dusk/001/qa.md))、開催・再生の設計は [DES-LIV-001](../development/issues/DES-LIV-001-hillside-live.md)、演目の企画は [DES-LIV-002](../development/issues/DES-LIV-002-live-lineup-and-parade.md)。移植の出所は [犬ギミック移植記録](../development/imports/kominka-dog-system.md)。

根拠は `ファイル:行` で書く。パスは特記なければ `Assets/KominkaHillside/Imported/KominkaDogRun/` からの相対 (`Scripts/…` / `Editor/…`)。行番号は 2026-09-26 の作業ツリー。シーンは別セッションの未コミット変更を含む作業ツリーの版をテキストで読んだ (Unity は操作していない)。**推測は「未確認」と書く。**

## 1. 結論

- **ランタイムは揃っている。** `KD_LiveSystem` (Manual。再生台帳の同期変数は 3 本。KD_LiveSystem 全体では .Candy 4 本 / .Mount 6 本を含め 13 本。今回の増加は 0) / `KD_LiveAccess` / `KD_DogWanderController.Live.cs` / `KD_DogFetchController` のライブ中 Fetch 停止 / `KD_TreatPouch.Live.cs` (ライブタブ) は古民家 `44505384` と同一 (差は裏山改変 2 か所だけ)。シーンには `KD_LiveAccess` とライブタブの器 (`Page_Live`、`LiveRow0..8`) があり、`live` が null なので「準備中」を描いている。
- **足りないのは配線と裏山用の表・ステージ。** シーンに `KD_LiveSystem` / ステージ / 犬の集合地点が無い。Live System の Editor 4 本 (`Editor/KD_LiveSetupEditor.cs`) は古民家のシーン構成 (`====KominkaDogRun====/KD_Places`、庭の木、外壁、`KD_Skybox.mat`) に固定されていて裏山では使えない。Live Generator パッケージも裏山の `Packages/manifest.json` に無い。
- **夜は `KD_LiveNight` を使わない。** `KH_NightMode` がすでに `liveDarkness` の入力を持ち「強い方を採る」で合成する (`Assets/KominkaHillside/Scripts/KH_NightMode.cs:21-22,33-34,62`)。`KD_LiveNight` は空の共有材質と別の覆いを書くので、置くと 2 枚目の乗算になり掛け算で濃くなる。**同期時計から暗さを導く小さな橋 `KH_LiveNight` を新設**して `liveDarkness` を書く。
- **リード接続中の犬は、コードを変えずに対象外になる。** 犬の優先順位でリード (`KD_DogWanderController.cs:1325`) がライブの観客 (`:1435`) より先に return する。呼ばれている犬 (`:1421` の UpdateSleep) と Fetch・フリスビー中の犬 (`:1333` 付近) も同じく先。**ただしフリスビーはライブ中でも新しく投げを受ける**ので 1 行足す (§2.4)。
- 次の波の順は §4。統括の Unity 操作は「パッケージ → コンパイル → 取り込み (`--no-scene`) → Shop → Live Setup → Menu/7 → Menu/11 → Night → Network IDs → Verify 群 → 保存」。

## 2. 調査結果

### 2.1 Live 系の移植状態

| ファイル | 裏山の状態 | 古民家最新 (作業ツリー) との差 | コンパイル |
|---|---|---|---|
| `Scripts/KD_LiveSystem.cs` (+ `.Candy.cs` / `.Mount.cs`) | あり。シーン未配置 (`KD_LiveSystem` の Program Asset GUID `f7e97157…` はシーンに 0 件) | 差なし (389 / 179 / 150 行) | 対象 |
| `Scripts/KD_LiveAccess.cs` | あり。シーンに `KD_LiveAccess` 1 個 (Menu/11 が `KH_Dogs` 直下に置いた) | 差なし (既定 ON の `abffd6c6` 版) | 対象 |
| `Scripts/KD_LiveNight.cs` | あり。シーン未配置。**裏山では使わない** (ナイトモードは `KH_NightMode`。移植記録「移植時の差分」) | 差なし | 対象 (未使用) |
| `Scripts/KD_DogWanderController.Live.cs` | あり | 裏山改変 1 か所: 集合 (mode 1) の歩きを NavMesh (`GatherStep`) にした (`:107-109`)。**ライブの観客の歩き (`UpdateLive` `:54`) は古民家のまま直線** | 対象 |
| `Scripts/KD_DogWanderController.CandyMountLive.cs` / `KD_CandyDogPerformance.Live.cs` | あり | 差なし | 対象 (犬の出演作品が無ければ動かない) |
| `Scripts/KD_TreatPouch.Live.cs` | あり | 裏山改変 1 か所: `_LiveSeeFx` の行き先をかざり (`TabAcc`) に (`:182-183`) | 対象 |
| `Editor/KD_LiveSetupEditor.cs` | あり。MenuItem 4 本は `Tools/KominkaHillside/Menu/Imported-Unused/Live System/0〜3` (`:142,161,482,765`) | パスの付け替えだけ (30 行) | 対象。**中身は古民家のシーン専用** (`FindPlaces` `KD_Places`、`SeasonTreeName` / `WallName`、`EnsureSkybox`、`KD_DogDefs.SystemsPath`) |
| `Editor/KD_ProductDefinitions.cs` / `KD_ProductsEditor.cs` / `KD_LiveThumbnailEditor.cs` | あり | パス・シーン判定の付け替えだけ | 対象 |
| `Editor/KD_DogDefs.cs` の `LiveShows` / `LiveProducts` / `LiveStagePosition` | 古民家の 9 曲・7 商品のまま (`:505-532`)、ステージ `(-5,-0.39,-1)` 回転 Y 270 (`:535-543`) | パスだけ | 対象 |
| `Editor~/KD_PumpkinPreviewEditor.Stage.cs` ほか 28 本 | コンパイル外 (移植記録「除外した Editor」)。`VirtualCampers.LiveGenerator` や `KD_CandyRehearsalEditor` が要る | — | 外。**今回の 1 曲 (犬の出演なし) には不要**。Candy / Pumpkin を移すときに戻す |

- `KH_WITH_SLEEPKIT` / `KH_WITH_HOBORADIO`: 裏山では定義していない。関係するのは `KD_MenuSettings` と Editor 2 本のガード (移植記録「裏山改変 (ガード)」)。`KD_LiveNight` を使わないので、**ライブのために分岐を整理する必要は無い** (移植記録の「`KH_WITH_SLEEPKIT` 分岐の整理はライブ移植時」は「不要」と閉じてよい。決めるのは統括)。
- 犬の出演 (`dogPerformances`) は空配列が既定 (`Scripts/KD_LiveSystem.Candy.cs:14`)。`DogPerformanceOf` が null なら通常ライブの経路だけ通る (`Scripts/KD_LiveSystem.cs:287,304,353`)。

### 2.2 シーンの現状 (`Assets/Scenes/VRCDefaultWorldScene.unity`)

- 無いもの: `KD_LiveSystem` / `KD_LiveNight` / `KD_LiveStage_*` / `LiveStage_*` / `KD_LiveDogSpot`。`KD_Systems` / `KD_Places` / `====KominkaDogRun====` も無い (裏山の置き場は `KH_Dogs` と `KH_World/KH_Places`)。
- あるもの: `KD_TreatPouch` の `live` は null、`liveItems` 9 本・`liveProducts` 7 本・`liveTitles` 9 本は古民家の品ぞろえで配線済み (Menu/7 が `KD_DogDefs.LiveShows` から作った)。`LiveRow0..8` と `LiveDetail`。カタログに古民家のライブ商品 `KD_ShopItem_Live_*` 7 行 (Menu/4 が `KD_ProductDefinitions.All` の全行を作る)。犬 6 匹の `KD_DogWanderController.liveSystem` / `liveSpot` と `KD_DogFetchController.liveSystem` はすべて `{fileID: 0}`。`KD_GroupActivities.live` も未配線 (裏山の `Editor/KD_ActivitiesEditor.cs:203` は live を SKIP)。
- `KH_NightMode` は 1 個 (`liveDarkness` 入力あり、`KH_World` の外)。
- **注意**: git status でシーンは M (別セッションの変更 +232/−80 行)。触る前に baseline patch を取る (`vrc-scene-edit-safety`「別セッションと作業ツリーを共有しているとき」)。

### 2.3 夜 (暗転) と KH_NightMode の合成

| 項目 | 古民家 `KD_LiveNight` | 裏山で取る形 |
|---|---|---|
| 暗さの入力 | `KD_LiveSystem.Elapsed` / `CurrentDuration` / `CurrentDarkness` の純関数 (`Scripts/KD_LiveNight.cs:160-172`)。フェードイン 3 秒 → 上限 → 終了後 3 秒で昼へ | 同じ純関数を新設の `KH_LiveNight` (None) に置き、`KH_NightMode.liveDarkness` へ毎フレーム書く。**同期変数は増やさない**。途中入室もその時点の暗さから |
| 覆い・空 | 自前の覆い + 共有材質 `KD_Skybox.mat` を直接書く (`:208-227`) | **書かない。** 空と覆いの実行時の書き手は `KH_NightMode` だけ ([空とナイトモード計画](2026-09-21-sky-and-night-mode.md) §1「材質の書き先」)。`KH_NightMode.Darkness` = max(手動, liveDarkness) (`KH_NightMode.cs:62`) |
| 木・塀を消す | 暗くなり切ったら庭の木と外壁の Renderer を切る | **持ち込まない** (葉が昇る木そのものが演出の起点) |
| SleepKit との積の縮小 | `MaxDarknessUnderSleepKit` (`:106-113`) | 不要 (max で合成するので積にならない) |
| 終了時に 0 へ戻す契約 | — | 純関数なので終了後 3 秒で自然に 0。`live` が null / `CurrentLiveId < 0` でも 0 を書く |

「夜で終わる」(Q09) は、この形なら **曲の最後の音まで夜、Timeline 終了後 3 秒で昼へ戻る** (古民家と同じ)。企画 README の「導入は夕方 → 山で夜に届く」の段階的な暗くなり方は、今の器 (3 秒で上限) では表せない。§3 の決定 D2 で選ぶ。

### 2.4 犬: 観客参加・リード除外・Fetch との優先順位

`KD_DogWanderController.cs` の毎フレームの枝 (上ほど強い):

| 順 | 枝 | 行 | ライブ中の結果 |
|---|---|---|---|
| 1 | リード接続中 `UpdateHillsideLeash` (`KH_LeashManager._IsLeashed`) | `:1325` | **接続中の犬は観客にならない (Q13 の決定どおり。変更不要)**。観客として伏せている犬に後からリードを付けると、その犬はリードへ移って列から抜ける (`KH_LeashManager.ReqAttach` `Assets/KominkaHillside/Scripts/KH_LeashManager.cs:175-194` にライブの拒否条件は無い) |
| 2 | Candy / Pumpkin の出演 | `:1327` | 今回は出演作品なし |
| 3 | Fetch 中 / フリスビー中 | `:1333-1392` | 済ませてから合流 (古民家と同じ)。凍結も解く (`:1361`) |
| 4 | そばで寝る / 呼ぶ (`UpdateSleep`、`KD_SleepBoard._IsCalled` = 枠の指名) | `:1421` | **呼ばれている犬も対象外** (Q13 と同じ扱い) |
| 5 | ライブの観客 `UpdateLive` | `:1435` | 撫でられている犬は、よそに居る間は呼ばない (`KD_DogWanderController.Live.cs:27-28`) |
| 6 | 集合 (mode 1) | `:1443` | ライブ中は集合を開始できない (`Scripts/KD_GroupActivities.cs:110`)、集合中はライブを開始できない (`:105` の `!Busy`) |

- **新しい Fetch を止める口**: ボールは `KD_DogFetchController.LiveWatching()` が `liveSystem.IsPlaying` で投擲を断る (`Scripts/KD_DogFetchController.cs:786-788,820`)。**フリスビーは断らない**: `KH_FrisbeeCatch.CanPlay` (`Assets/KominkaHillside/Scripts/KH_FrisbeeCatch.cs:1015-1022`) にライブの条件が無いので、ライブ中に投げると未接続の犬が列から走り出す。→ `fetch != null && fetch.liveSystem != null && fetch.liveSystem.IsPlaying` なら false を 1 行足す (古民家の「ライブ中は新しい Fetch を始めない」と揃える)。
- **斜面の歩き**: `UpdateLive` は直線で集合地点へ向かう (`KD_DogWanderController.Live.cs:54`)。散歩の途中で離された犬は道の下にも居る (`KH_LeashManager.releasedHomes`) ので、集合と同じく `GatherStep` (NavMesh、`KD_DogWanderController.HillsideLeash.cs:1084`) を先に試す 1 行を足す (集合の裏山改変 `Live.cs:107-109` と同形)。
- 終了後: 観客の凍結が解けて通常の徘徊へ戻る (`Live.cs:23`)。集合のように「終わった場所を新しい居場所にする」(`KH_LeashManager._OnGatherEnded`) はライブには無い → 広場から元の居場所へ歩いて帰る。§3 D5。
- 観客の並び: 集合地点 `liveSpot` を原点に、集合と同じ隊形 (`KD_GroupActivities.Offset` `:191-215`) を開催時に選ぶ (トレーナー所持者は指定、他は乱数 `:105-106`)。隊形の幅は数 m で、広場の平坦部 (半径 12 m) に収まる。

### 2.5 商品と所有

- 裏山の台帳は **`productKey` の文字列で所有を持つ**: PlayerData `KH_Commerce_v1.Own.<productKey>` (`Scripts/Progress/KD_ProgressLedger.cs:18,78,239-245,287-295`、[IMP-PRG-001](../development/issues/IMP-PRG-001-ledger-shopkeeper-points.md))。古民家の数値の unlockId は欠番の検査 (`Editor/KD_LiveSetupEditor.cs:129` `RetiredUnlockId = 24`) にしか残っていない。**採番は `KD_ProductDefinitions.Validate` の規則** (`Editor/KD_ProductDefinitions.cs:154`: `^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$`、カテゴリ登録済み、重複なし、`test.` 始まり禁止)。一度使ったキーは再利用しない (`:53` の退役キーの注記)。
- 追加する行 (案): `new Row("live.hillside_dusk", "live", "KD_ShopItem_Live_HillsideDusk", 30, 0, 0, 0, "")`。Point の買い切り (CE の Listing なし)。キー名は曲 ID が決まったら確定 (§3 D1)。
- 所有判定: 板は `KD_ShopItem.Owned()` (`Scripts/Progress/KD_ShopItem.cs:269-275`)、開催の受理は Owner 側で `hostingItems[id].OwnedBy(caller)` (`Scripts/KD_LiveSystem.cs:299-303`) → `ledger.IsUnlockedFor` が相手の PlayerData を読む (`KD_ProgressLedger.cs:289-295`)。**開催者本人の所有で開く。鑑賞は全員**。
- 購入: ライブタブの行 → 詳細 → [買う] → 確認カード → `catalog._RequestBuy` → `ledger._TryBuyUnlock` (`Scripts/KD_TreatPouch.Live.cs` の `_LiveDetailAction` / `_LiveBuy`)。既存の経路のまま。
- 古民家のライブ 7 商品の行はカタログに残す (台帳の行。画面には `LiveProducts` に載せたものだけ出る)。CE (Candy / Pumpkin / Pack) の購入者利用は DES-LIV-001 の別論点で、今回は触らない。

### 2.6 手元メニューのライブタブ (Live4)

- タブの器は完成済み: `TabLive = 4` (`Scripts/KD_TreatPouch.cs:462`)、表示順の最後 (DES-UI-003 D-2 の `Order = {2,0,5,3,1,6,4}`)。`live == null` の間だけ「準備中」を描く (`Scripts/KD_TreatPouch.Hillside.cs:205-216`)。`live` が入れば古民家と同じ `RedrawLive` (一覧 → 詳細 → 買う / 開催、状態行「○○さんが開催中 残り N 秒」「次の開催まであと N 秒」)。
- 配線の経路: Menu/7 (`Editor/KD_TreatSetupEditor.Pouch.cs:717-890`) が **シーン全体から `KD_LiveSystem` を名前で探して** `pouch.live` に入れ、`KD_DogDefs.LiveShows` / `LiveProducts` から行と商品を作る。Menu/11 が `live.access` と `live.hostingItems = pouch.liveItems` を書く (`Editor/KD_MenuFinishEditor.cs:110`)。
- 最小構成 (1 曲・開始ボタン・所有表示) に要るもの:
  1. `KD_DogDefs.LiveShows` / `LiveProducts` を裏山の 1 行ずつに差し替える (裏山改変)。
  2. **余る `LiveRow1..8` の扱い。** 1 曲にすると Menu/7 は `liveLabels` を 1 本で作り直すが、`EnsureUiChild` は既存の行を消さない。配列から外れた行は `RedrawLive` が触らず、古い文字のまま残る (`KD_TreatPouch.Live.cs:380-412` は配列の長さだけ回す)。→ 裏山ビルダーで `LiveRow{n..8}` を消し、**`Data/UILocalizationBindings.json` の `LiveRow1..7/Button/Label` の束縛も同時に消す** (無い部品への束縛は Bind が例外で止まる。移植記録 U1 の注記)。Menu/13 Apply Scroll Layout が余りを消すかは未確認。
  3. 文言 (`Data/UITranslations.json`、13 言語。ja / en 必須、他は en を複製): 曲名 (`KD_TreatPouch.liveTitleKeys.0` は今「Happy Tails, Sunny Trails」。添字の鍵なので書き換えになる)、商品名キー、詳細 4 本 `live.detail.hillside_dusk.{jp,meta,mood,scene}` (`KD_TreatPouch.Live.cs:280-286` の組み立て)。`Editor/KD_ShopSetupEditor.Hillside.cs:71` が `live.happy_tails` の名前に `liveTitleKeys.0` を借りているので、ここも別キーへ直す。
  4. 参考画像 `Textures/LiveThumbs/live.hillside_dusk.png` は任意 (無ければ色面 + 準備中。WARN のみ)。
  5. 音量スライダーのライブ行は無い (DES-WLD-002 / DES-LIV-001 後の暫定のまま)。

### 2.7 舞台: 上の広場

| 物 | 値 (world、m) | 根拠 |
|---|---|---|
| 広場の中心 = 通過点 17 点の最後 `Node16` = `KH_Places/KH_Plaza` | (72, 19.04, 127) | シーン、`Assets/KominkaHillside/Scripts/Editor/KH_TerrainPrototypeConfig.cs:67,80` |
| 平坦部 | 半径 12 m (直径 24 m) が高さ 19、外側 18 m で地形へなじむ | 同 `:67` |
| 見晴らし・ベンチ・拠点 | `KH_Viewpoint` (80, 19, 108)、ベンチはその +Z 2 m、`KH_Station_Viewpoint` (83.3, 19.8, 108.3) | シーン |
| 遊び道具の初期位置 | ボール (73.5, 19.1, 126)、ディスク 6 枚 (74.6〜76.4, 19.03, 132.3〜133.2) | シーン |
| 広場まわりの広葉樹 | (63, 136) 中心から 12.7 m、(88, 141) 21.3 m。見晴らしの右 (95, 108) 29.8 m | `KH_TerrainPrototypeConfig.cs:130-136` (実物の位置と高さは未確認) |
| 古民家 | (55.34, 1.5, 29.82)。広場の中心から方位 190°、水平 98.6 m、17.5 m 下 (見下ろし約 11°) | 同 `:111` |

**案 (未確認・要ユーザー確認)**:

- ステージ `KD_LiveStage_<slug>` のルート = 広場の中心 **(72, 19.0, 127)、回転 Y 190°** (+Z が谷と古民家の方向。`quaternion (0, 0.9962, 0, -0.0872)`)。粒は「広場の上空と谷の方向」(企画 README) をこの +Z に取る。**古民家は +Z の水平線より約 11° 下**に見えるので、+Z 側の粒を目の高さより上に置けば見返しを塞がない。
- 集合地点 `KD_LiveDogSpot` = ステージと同じ位置・同じ向き (犬は +Z = 谷を向いて伏せる)。人は犬の後ろ (北側) や周りに座る。
- 生成器へ渡すステージ座標系の目印 (ステージ local、x = 右、z = 前): 木 (63,136) → (10.4, 0, −7.3)、木 (88,141) → (−13.3, 0, −16.6)、見晴らし → (−11.2, 0, 17.3)、ディスク置き場 → (−2.5, 0, −6.2)、古民家 → (−0.5, −17.5, 98.6)。葉が昇る起点は後ろ側の 2 本になる (「広場を囲む木」と呼べる本数は 2 本だけ。増やすかは IMP-ENV-001 / 演目側で判断)。
- 置き場所は **`KH_World` の外** (Build が作り直すため。AI_INDEX 末尾)。`KH_Dogs/KH_Live/` の下に `KD_LiveSystem` / `KH_LiveNight` / `KD_LiveStage_<slug>` / `KD_LiveDogSpot` をまとめる案。値は Editor の定数が正本 (Adopt が毎回書く。古民家 `KD_DogDefs.LiveStagePosition` と同じ方針)。
- 未確認: 座った目の高さから広場の中心で古民家が見えるか (見晴らしの平坦部の縁が手前にある)。太陽 (空の `_Rotation` 180) の方角。

### 2.8 Live Generator パッケージと取り込み

- 裏山の `Packages/manifest.json` に無い。古民家の行 (`C:/work/unity/vcc/KominkaDogRun/Packages/manifest.json:4`) と同じものを足す:
  `"com.virtualcampers.live-generator": "file:C:/work/unity/vrc-live-generator/unity-package",`
  依存の `com.unity.timeline` (1.7.5) と `com.unity.nuget.newtonsoft-json` (3.2.2) は裏山に既にある。
- 裏山に `Assets/VirtualCampers.LiveGenerator/` は無い (古民家には `Shows/` と Editor 3 本。古民家は `.gitignore:151-152` で除外しつつ 1,819 ファイルを追跡)。ステージのルートには `EffectBinding` (MonoBehaviour、パッケージの Runtime) が付く — パッケージが無いと Missing Script になる。
- 取り込みは古民家と同じ **`--no-scene`** (`vrc-live-generator/.claude/skills/vclive-loop/SKILL.md:43`)。`--scene` はラボの見た目 (`ApplyLabLook`: 空を外し環境光を黒・Directional を 0.08) を**対象シーンに書いて保存する** (`vrc-live-generator/unity-package/Editor/VcLiveCli.cs:153-157,333-347`)。`--no-scene` でも既定で**アクティブシーンを保存する** (`:144,187-191`)。共有ツリーでは `vclive.sh raw` で `"saveScene": false` を渡す案。作られるのは root の `LiveStage_<slug>` と `Assets/VirtualCampers.LiveGenerator/Shows/<slug>/`。
- `vclive.sh` の既定の接続先は AICampersWorld / 8086。裏山は `VCLIVE_UNITY_PROJECT=C:/work/unity/vcc/VAnimal_KominkaHillside` を必ず付ける (裏山の `scripts/unity-api.sh` も 8086。2 つ同時に開かない)。

## 3. 実装前に決めること

| # | 論点 | 案 (開発側) | 決める人 |
|---|---|---|---|
| D1 | 曲 ID (slug)・曲名・商品キー | slug は生成器の曲 ID (未定)。商品キー `live.hillside_dusk`、シーン名 `KD_ShopItem_Live_HillsideDusk`、30pt | Music Agent / 統括 |
| D2 | 暗くなり方 | (a) 古民家と同じ 3 秒で上限へ。(b) 企画の流れに合わせ、曲の頭から山 (約 1:40) にかけて上限へ近づける (`KH_LiveNight` が liveId ごとの到達秒を持つ。同期時計の純関数のまま)。**開発側の推奨は (b) を試作して Play で比べる** | ユーザー |
| D3 | 「夜で終わる」の終わり方 | 曲の最後まで夜 → 終了後 3 秒で昼 (古民家と同じ)。余韻を長くするなら終了後のフェード秒だけ伸ばす | ユーザー |
| D4 | ライブ中に観客の犬へリードを付けられるか | 付けられる (今のコードのまま。付けた犬は列から抜けて散歩へ)。集合と同じく拒否するなら `ReqAttach` に 1 条件 | ユーザー |
| D5 | 終了後の犬の居場所 | 古民家と同じく元の居場所へ戻る。集合と同じく広場を新しい居場所にする案もある | ユーザー |
| D6 | ステージ位置・向き | §2.7 の案 (広場の中心、+Z = 谷)。Play で見て調整 | ユーザー |
| D7 | 音の届く範囲 | 未確認 (生成器の AudioSource 設定を見てから)。広場と見晴らしで聞こえ、門では小さく | DES-WLD-002 と合わせて統括 |
| D8 | 生成物の Git 管理 | 古民家と同じく `Assets/VirtualCampers.LiveGenerator/` の該当曲を追跡する (再生成できても採用版の固定のため) | 統括 |

**決定 (統括、2026-09-26。ユーザーの既決 Q09 / Q13 と古民家の既定から。Play で見直す前提):**
D1 演出 slug `kh-hillside-dusk`、商品キー `live.hillside_dusk`、シーン名 `KD_ShopItem_Live_HillsideDusk`、30pt。曲 ID は Music Agent の報告で確定 /
D2 **(b)** 曲の頭から山 (企画の 1:15〜1:40) にかけて上限 1.0 へ近づける。到達秒は `KH_LiveNight` の liveId ごとの表 (同期時計の純関数のまま)。フェードイン 3 秒の (a) は比較用に定数で切り替えられるようにする /
D3 曲の最後まで夜 → 終了後 3 秒で昼 (古民家と同じ) /
D4 付けられる (今のコードのまま。Q13「つながれている間はそのまま」の裏返し) /
D5 元の居場所へ戻る (古民家と同じ) /
D6 §2.7 の案 (広場の中心 (72, 19.0, 127)、Y 190°) /
D7 生成器の AudioSource の既定を見てから。広場と見晴らしで聞こえ、門で小さいことを Play で確認 /
D8 追跡する。

## 4. 次の波の実装手順

### 4.1 コード・データ (Opus サブエージェント。Unity は触らない)

| # | ファイル | 変更内容 | 検証 (AI) |
|---|---|---|---|
| C1 | `Packages/manifest.json` | §2.8 の 1 行を追加 (`packages-lock.json` は Unity が更新) | 統括の U1 |
| C2 | `Assets/KominkaHillside/Scripts/KH_LiveNight.cs` (新規、None) | `live` (KD_LiveSystem) と `nightMode` (KH_NightMode) を持ち、`KD_LiveNight.DerivedPhase` と同じ純関数 × `CurrentDarkness` (D2 (b) なら到達秒の表) を `nightMode.liveDarkness` へ毎フレーム書く。live 未配線・liveId < 0 は 0 | コンパイル。同期変数 0 本 |
| C3 | `Scripts/KD_DogWanderController.Live.cs` | `UpdateLive` の接近を `if (hillsideLeashes == null \|\| !GatherStep(spot, speed)) StepToward(...)` に (裏山改変コメント 1 行) | コンパイル |
| C4 | `Assets/KominkaHillside/Scripts/KH_FrisbeeCatch.cs` | `CanPlay` に「`fetch.liveSystem` が IsPlaying なら false」 | コンパイル + フリスビーハーネスの部分実行 ([手順](../testing/kh-frisbee-harness.md)) |
| C5 | `Editor/KD_DogDefs.cs` | `LiveShows` = 1 行 (slug / 曲名 / `live.hillside_dusk` / 暗さ上限)、`LiveProducts` = 1 行 (裏山改変) | コンパイル |
| C6 | `Editor/KD_ProductDefinitions.cs` | §2.5 の行を追加 | `Validate` が通ること (Menu/4) |
| C7 | `Editor/KD_ShopSetupEditor.Hillside.cs` | `HillsideNameKeys` に新商品、`live.happy_tails` の名前キーを `liveTitleKeys.0` から外す | Menu/4a |
| C8 | `Editor/KD_LiveSetupEditor.cs` を `partial` にし、`Editor/KD_LiveSetupEditor.Hillside.cs` (新規) | `Tools/KominkaHillside/Menu/26 Adopt Live Stage` (旧 `KD_LiveStage_<slug>` を削除し、新生成物 `LiveStage_<slug>` を `KD_LiveStage_<slug>` へ改名 → `KH_Dogs/KH_Live` へ → 定数 transform)、`26a Setup Live` (§4.3 の配線)、`26b Verify Live`。古民家の `EnsureAudioStreaming` / 粒の queue 5000 / `PlaybackDuration` / `PruneNullNetworkIds` を再利用。**KD_LiveNight・木と塀・Skybox は作らない** | 26b の全 PASS |
| C9 | `Editor/KD_TreatSetupEditor.Hillside.cs` (+ `.Verify.cs` の裏山分岐) | 余る `LiveRow{n..8}` を消す。live があるときは Verify のライブ行 (古民家の検査 `KD_TreatSetupEditor.Verify.cs:827-915`) を SKIP から戻す | Menu/20 |
| C10 | `Data/UITranslations.json` / `Data/UILocalizationBindings.json` | §2.6 の 3 の文言、消した行の束縛の削除 | Menu/22 |
| C11 | `Assets/KominkaHillside/Scripts/Editor/KH_NightModeEditor.cs` | Verify に「KH_LiveNight があれば nightMode がこの KH_NightMode」を 1 項目 | Night/2 |

同期・権限・保存に触る変更 (C2 の境界、C4、C8 の配線) なので確定前に Codex の独立レビュー (`vrc-review`)。

### 4.2 Unity 操作 (統括が直列に。サブエージェントは実行しない)

| 順 | 操作 | 確認点 |
|---|---|---|
| U0 | `bash scripts/unity-api.sh check`。`git diff -- Assets/Scenes/VRCDefaultWorldScene.unity > <scratchpad>/baseline.patch` | 接続先が裏山。シーンが dirty でない |
| U1 | C1 の後に refresh (パッケージ解決) → `bash scripts/unity-compile.sh --udon` | `csErrors 0` / `udonErrors 0`。Package Manager に Live Generator |
| U2 | C2〜C11 の後に `bash scripts/unity-compile.sh --udon`。`KH_LiveNight` の Program Asset を作る (Editor の Create Programs 相当) → もう一度 `--udon` | 0 / 0。Program Asset の `.asset` ができた |
| U3 | 取り込み: `VCLIVE_UNITY_PROJECT=C:/work/unity/vcc/VAnimal_KominkaHillside tools/unity/vclive.sh raw '{"command":"import","showplan":"<abs>","effectsFile":"<effects>","saveScene":false}'` (生成器側で曲ができてから) → `vclive.sh verify <slug> --budget-particles 15000 --budget-materials 80 --budget-lights 0` | root に `LiveStage_<slug>` が 1 個。`asset missing` 無し。予算内 |
| U4 | `Menu/4 Setup Shop` → `4a Verify Shop` | 新商品の行 (30pt、category live) |
| U5 | `Menu/26 Adopt Live Stage` → `26a Setup Live` → `26b Verify Live` | ステージ 1 本・`playOnAwake` false・inactive・粒 queue 5000・音源 Streaming・犬 6 匹 × (wander 2 + fetch 1) の配線・`activities.live`・`KH_LiveNight` |
| U6 | `Menu/7 Build Menu` → `Menu/13 Apply Scroll Layout` → `Menu/10 Collect Words` → `Menu/11 Setup Localization` | `pouch.live` 配線、`LiveRow` 1 本、`live.access` / `hostingItems` |
| U7 | `Night/1 Setup Sky And Night` (必要なら) → `Menu/19b Assign Network IDs` | `KD_LiveSystem` (Manual) に Network ID。errors 0 |
| U8 | Verify: `26b` / `20 Verify Pouch` / `21 Verify Scroll Layout` / `22 Verify Localization` / `23 Verify Activities` / `4a` / `Night/2` / `Walking/5 Verify` (散歩の退行) / `24 Verify Collision` | 全 PASS (SKIP は理由付き) |
| U9 | シーン保存 → ファイルを grep (`KD_LiveSystem` 1、`KD_LiveStage_` 1、`LiveStage_` の root 0、`liveSystem: {fileID: 0}` 0) → baseline と突き合わせて自分の分だけ stage | 他セッションの変更を巻き込まない |
| U10 | `Build Size/7 - Apply` → `2 - Bundle Size Android` | 増分を [ビルドサイズ実測](2026-09-24-build-size-audit.md) に記録 |

AI が ClientSim で見られるのは「開始要求 → 台帳の liveId / startMs → ステージが active → 60 秒待機」まで。粒の見え方・暗さ・犬の並びはユーザーの Play (§5)。

### 4.3 26a Setup Live が書く配線 (一覧)

| 書き先 | 値 |
|---|---|
| `KD_LiveSystem` (`KH_Dogs/KH_Live` の子) | `stageDirectors` / `stageRoots` / `stageDurations` (Timeline から `PlaybackDuration`) / `stageDarkness` (長さ = LiveShows)、`activities` = シーンの `KD_GroupActivities` (`KH_Leashes` と同じ GameObject)、`access` = `KD_LiveAccess`、`hostingItems` (Menu/11 も書く)、`dogPerformances` = 空、`candyPerformance` = null、`candyLiveId` = -1、proxy / backing を有効化、Sync Manual |
| `KD_GroupActivities.live` | `KD_LiveSystem` (無いとライブの開始要求 op 4 が常に拒否される。`KD_GroupActivities.cs:105`) |
| 犬 6 匹 | `KD_DogWanderController.liveSystem` / `liveSpot`、`KD_DogFetchController.liveSystem` |
| `KD_LiveDogSpot` | 無ければ作る。位置 = ステージ local (0, 0, 2.5) (観客の 2.5 m 前・谷側、演出の anchors.dogs と同じ。world 約 (71.57, 19.0, 124.54))、向き = ステージと同じ Y 190° |
| `KH_LiveNight` | `live` / `nightMode` = シーンの `KH_NightMode`、`rampSeconds` = {88} (添字 = liveId)、`useKominkaFade` = false、proxy / backing を有効化、Sync None |
| ステージ | 毎回、親 (`KH_Dogs/KH_Live`) と world transform を定数 (72, 19.0, 127)・Y 190° へ戻す。`playOnAwake` false / inactive / 粒 queue 5000 / 音源 Streaming |
| SleepAudio | SKIP (`KD_SleepAudioSetupEditor.RewireIfPresent` は呼ばない。裏山に `KD_SleepAudio` は無い) |

## 5. Play チェックリスト (ユーザー)

事前準備: コンソールにエラー無し / シーン保存済み / **Build & Test をし直した** / 検証前の残高と所持 (ライブ商品を買っていないこと) をメモ。ポイントが 30 未満なら先におじいさんの受け取り。

### §0 エディタで判定できない項目 (NG なら他の結果は無意味)

- [ ] 広場の中心に座ると、ステージの粒が見える方向に谷と古民家があり、粒が古民家の見返しを塞がない (ステージ位置・向きは推定値)
- [ ] 粒が暗転の覆いより手前に明るく描かれる (queue 5000 > 覆い 4999)。鏡・写真では覆いが 1 回
- [ ] 手元メニューの文字が暗転の最中でも読める

### 1 人 (PCVR)

- [ ] ライブタブに曲が 1 行だけ出る。未所持は値段 30pt と [くわしく]。古民家の曲名・「準備中」が残っていない
- [ ] 詳細 → [買う] → 確認カード → 購入で残高が 30 減り、行が [開催] に変わる。再入室しても所持のまま
- [ ] [開催] → 3 秒後に曲と粒が始まる (この 3 秒は暗さ 0)。状態行に「開催中 残り N 秒」
- [ ] 暗くなり方が D2 (b) どおり: 曲の頭から 88 秒かけて 1.0 (真っ暗) へ近づき、曲の最後まで夜、終了後 3 秒で昼へ戻る (D3)
- [ ] ナイトモード ON のままライブを見ても、ナイトモード単独やライブ単独より濃くならない (強い方を採る)。ライブ後はナイトモードの暗さに戻る
- [ ] 未接続の犬が集合地点へ歩いて来て、谷の方を向いて伏せる / おすわりする。坂の下に居た犬も道なりに来る
- [ ] リードでつないでいる犬は来ない (散歩のまま)。呼ぶで待たせている犬も来ない
- [ ] 観客の犬にリードを付けたときの動き (D4 の決定どおり)
- [ ] ライブ中にボール・ディスクを投げても犬が走らない。ライブ前から追っていた犬は済ませてから列に来る
- [ ] 終了で犬が自由行動に戻る (D5)。60 秒間は「次の開催まであと N 秒」で開催できない
- [ ] ライブ中に集合 (あそぶ) を始められない。集合中はライブを始められない

### 2 人 (PCVR 同士、または PCVR + Desktop)

- [ ] A が開催すると B にも同じ時刻で粒・音・暗転が出る。B はライブを所持していなくても見られる
- [ ] B が未所持なら B の画面では [開催] ではなく [くわしく]
- [ ] 途中入室した人は途中から始まり、暗さもその時点の値
- [ ] 開催者が退室しても最後まで流れ、60 秒待機に入る
- [ ] 「ライブ開催を全員に許可」を OFF にすると、管理者以外の所持者は開催できない

### 端末別 (不変条件 7。PCVR の総合評価を他端末の合格にしない)

- [ ] Quest 単体・iOS で粒・暗転が出る。fps の体感 ([VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) へ記録)

| 症状 | 見るところ |
|---|---|
| まだ「準備中」 | アップロードし直したか → `pouch.live` (Menu/7 のログ) |
| [開催] を押しても始まらない | `KD_GroupActivities.live` 未配線 (4.3)、所有の読み込み中、開催許可 OFF、60 秒待機中 |
| 暗くならない / 二重に濃い | `KH_LiveNight.nightMode` の配線、`KD_LiveNight` が誤って置かれていないか |
| 犬が来ない | `liveSystem` / `liveSpot` の配線 (26b)、その犬がリード・呼ぶ・撫でられ中でないか |
| 犬が斜面を突っ切る / 止まる | C3 (NavMesh) の有無、NavMesh のベイク範囲 |
| ディスクで犬が走り出す | C4 の有無 |
| 粒が暗い | 粒の材質の renderQueue (26a が 5000 を書く) |

### 結果 (2026-09-26 ユーザー Play 1 回目)

- **購入**: 1 回目は確認カードの [購入] が反応せず (カードは出た。他の 30pt 品は同じカードで買えた)。ClientSim の診断 (`Tools/KominkaHillside/Testing/Live Buy Probe (ClientSim)`、`KH_LiveBuyProbeEditor.cs`。イベント直呼びと Yes の onClick.Invoke の両方) では購入成功・Yes が raycast の最前面で再現せず。ユーザーの再試行で**買えた**。原因は未特定 (候補: 開いた直後のカタログ未復元で黙って断られる → 「読み込み中」表示の有無を次回見る)。
- **開催・演出**: 開催でき、印象は「シンプルでこれはこれでいい」(ユーザー)。項目別 (暗転の進み方・犬の集合・葉→星の見え方・古民家の見返し・音の範囲・2 人・端末別) は未記録。
- 診断スクリプトは IMP-PRG-001 の台帳ハーネスの種として Testing/ に残す (Logs/kh-live-buy-probe/ に結果、ClientSim のセーブは退避 → 復元)。

## 6. 未確認・迷った点

- 座った目の高さで広場の中心から古民家が見えるか。見えなければ集合地点を見晴らし寄り (南) へ寄せるが、平坦部は見晴らしの縁で狭い。
- Menu/13 Apply Scroll Layout が余った `LiveRow` を消すか (§2.6 の 2)。消さない前提で計画した。
- 生成器のステージに付く AudioSource の 3D 設定と、取り込み時に作られる材質数・粒数。曲がまだ無いので未計測。
- `KD_LiveSystem` を `KH_Dogs/KH_Live` に置いたとき、古民家の Verify (Imported-Unused/Live System/3) が名前で探す前提 (`KD_DogDefs.SystemsPath`) とずれる。古民家版は使わない前提で、裏山版 26b だけを正にする。
- `KD_TreatPouch.hillsideStage` の段階表 (`KD_Hillside.Stage = 2`) にライブを足すか (段階 3 にするか、live は段階と独立に「あれば検査」にするか)。後者を推奨。
- 古民家のライブ商品 7 行を裏山のカタログに残すこと (購入入口は出ないが台帳上は買える形)。CE 購入者の利用 (DES-LIV-001) の結論が出るまで残す。
- 別件候補: `Editor/KD_LiveThumbnailEditor.cs:55` の旧 Capture は `KD_LiveNight` と `====KominkaDogRun====` を必須とし、裏山では例外になる。参考画像 (サムネイル) を作るときに対応する。
- 観客の犬の Ownership は動かさない (古民家の器どおり)。犬の位置更新は各犬の Owner だけで、同期変数は増えない。これは古民家の実装を読んだ結論で、裏山の 2 人検証は未実施。
