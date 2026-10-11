---
id: IMP-DOG-046
type: IMP
system: DOG
status: review
priority: Medium
summary: 追加洗浄道具を種類ごとに選択し、泡・水・タオルを同時に出せるようにする
next: シーンへ適用済み (2026-10-11、44 707 PASS、Wash/12 は範囲外の 1 件だけ)。ユーザーの PCVR で 3 枠同時・持ったまま台の切替・基本に戻す・ドライヤーの風を見る。2 人で同じ選択。ハーネス WashPremium.cs の旧名の修正は未着手
updated: 2026-10-11
---

# IMP-DOG-046 洗浄道具を種類ごとに選択する

## 依頼と現状

2026-10-10 ユーザーは、追加道具が洗い場ごとに1品しか出せず、泡銃とシャワーを併用できない点を指摘。泡・水・タオルなど種類ごとに出す方向を了承し、まずIssue登録を依頼した。

[IMP-DOG-043](IMP-DOG-043-wash-premium-selection.md)の選択処理は `selectedTool` を1個だけ持つ。13モデルから1品だけ有効化し、いずれかの追加道具が保持中なら全選択を拒否する。1品制限を必要とする性能上の根拠は記録されていない。

## 対応方針

- 洗い場ごとの独立性を維持し、種類ごとに選択を記憶する。泡・水・タオルを同時に置き、使用できるようにする。
- 同じ種類の選択だけを交換し、別種類の使用中の道具を消したり移動させたりしない。交換対象の保持中チェックも種類ごとにする。
- 道具の置き場所を分け、重なりを避ける。途中参加・Owner交代・同時操作・リセットも種類別の状態に対応する。
- 仕上げの道具などの詳細な分類は、下記HTMLの採否・改善回答を受けて決める。購入セットの分類と同時に出せる道具枠は同一と決めつけない。
- 最新の対象はDES-DOG-008の最終9品方針。泡（泡銃・ミトン）、水（じょうろ・新規ホース・新規水鉄砲）、仕上げ（ドライヤー・タオル）を種類ごとに出して同時利用する。同じ種類内をさらに分けるかは未定。無料の標準道具とDesktop用の標準スポンジは維持する。

## アイテムの採否・改善レビュー

[確認用HTML](../../../artifacts/wash-item-review/2026-10-10/index.html)で、既存17品と未制作のプール候補について「いる／いらない／保留」、改善点、持ったときの違和感を記録する。初期値は全品未確認。回答はブラウザー内保存とJSON書き出しに対応し、回答ファイルを受け取ってから企画・実装へ反映する。採否を先取りして既存モデルを削除しない。

持ち方の不具合は [BUG-DOG-010](BUG-DOG-010-wash-tool-grip.md)、プール追加は [IMP-DOG-044](IMP-DOG-044-wash-pool-platform.md)。

2026-10-10: [回答JSON](../../../artifacts/wash-item-review/2026-10-10/vanimal-wash-review-2026-10-10T07-47-47-617Z.json)を受領後、チャットで最終9品へ更新された。採否の正本は[DES-DOG-008の最終調整方針](DES-DOG-008-wash-paid-tools-and-effects.md)。角型シャワー・石台・タイル台は外し、ホース・新しい水鉄砲を追加する。採否を反映する実装は[IMP-DOG-047](IMP-DOG-047-wash-item-review-changes.md)。種類別の同時利用方法は未実装。

## 完了条件

- 1か所で泡・水・タオルを同時に取り出せ、ある種類の交換が別種類に影響しない。
- 別の洗い場の選択を変えず、保持中の道具を消さない。
- 2人での同時使用・選択、途中参加、Owner交代、リセットを確認する。
- PCVR・Android等の対象端末で配置・操作・同時使用時の負荷を確認し、確認方法と結果を記録する。

今回は起票と確認用HTMLの用意まで。Unity・モデルの修正は未着手。

2026-10-11: 設計案を [docs/plans/2026-10-11-wash-items-design.md](../../plans/2026-10-11-wash-items-design.md) §1 に作成 (推奨は泡・水・仕上げの3枠、各枠1品、枠ごとの置き場所)。ユーザーの判断待ち: Q1 枠の数、Q2「基本に戻す」の範囲、Q3 道具を持っている間の台の切替。

決定: 2026-10-11 (ユーザー「推奨でOK」) [設計案](../../plans/2026-10-11-wash-items-design.md) の Q1〜Q3 を推奨どおり採用。

## 実装 (2026-10-11、未適用)

コードと Issue の記録。**C# / U# のコンパイルはエラー 0 (2026-10-11、統括。`KH_WashSelectionEditor.Category.cs` の `Rect.MinMaxRect` が同じクラスの `Rect(...)` と衝突したのを `UnityEngine.Rect.MinMaxRect` に直した後)。移行 (48)・検査 (44 / Wash/12)・保存・Play・実機は未実施。** [IMP-DOG-047](IMP-DOG-047-wash-item-review-changes.md) と同じ変更で、同じ手順で一度に適用する。

### 同期する状態 (洗い場ごと、Manual のまま)

- `selectedTool` (1 本) と `duckOn` を廃止し、`selectedFoam` / `selectedWater` / `selectedFinish` / `selectedBath` (各 int、-1 = 無料の道具・台) と、従来の `revision` / `resultPlayer` / `resultCode` / `resultSerial` を同期する。同期配列は使わない (初期化忘れで同期が止まる罠を避ける)。途中参加者は 4 本と revision を一度に受け取り、初回の受信は保持中でも復元する (従来どおり)。
- 要求は `ReqSelect(item, expectedRevision)` (`[NetworkCallable]`、Owner だけが処理。送り主の位置 6 m・0.3 秒間隔・revision を確認)。**品の妥当性は Owner が見る**: `itemCategories[item] < 0` (外した品・未制作の品) と、シーンにモデルが無い品は無視する (Q4。ボードで隠すだけを門にしない)。
- 判定は `_Decide(item, expectedRevision)` に分けた (ネットワーク呼び出しなし。44 が Edit モードで同じ本体を回す)。結果コード: 0 反映 / 1 その種類の道具が持たれている / 2 revision 違い / **3 基本に戻したが持たれている道具は残した (Q2)**。
  - 道具の要求は**その種類の枠だけ**を保持確認する (泡 = 枠 0・1、水 = 枠 2、仕上げ = 枠 3・4。出ていく品と入る品の両方の枠)。
  - 台 (檜) の要求は保持確認をしない (Q3: 道具を持っていても台は替わる)。
  - 「基本に戻す」(item -1) は持たれていない種類をすべて -1 に、台も -1 にする。持たれている種類は残し、コード 3 を返す (Q2)。
- 表示 (`_Apply`) は種類ごとに出ている品 (`_shownTools[3]`) と枠ごとの設定済み品 (`_slotItem[7]`) を持つ。決定の後に Pickup が届いた競合では、**持たれている種類だけ**旧モデルのまま待ち、他の種類と台は即座に切り替わる。枠の設定 (ノズル・粒子・音・当たり判定・持ち方) は品が変わった枠だけ書き直す (持っている枠を触らない)。使わない枠は `spray` を空にする。
- 持ち方は品ごとのデータ `gunHolds[]` (null = Grip + `gripAnchors`、非 null = Gun + その Exact Gun)。泡銃は `KH_FoamCannonGunHold` (BUG-DOG-010)。`VRC_Pickup.orientation` / `ExactGun` の setter は Udon に公開済み (VRC.Udon.VRCWrapperModules.dll に `__set_orientation__VRCSDKBaseVRC_PickupPickupOrientation` と `__set_ExactGun__UnityEngineTransform`、2026-10-11 確認)。**IMP-DOG-049 の水鉄砲は、水の枠 (枠 2) のまま `gunHolds[水鉄砲]` を入れれば Gun になり、じょうろに戻すと Grip に戻る** (枠を増やすときは `slotCategories` に 1 行足す)。
- 置き場所は種類ごとに 3 か所 (`PremiumTools/Home_Foam` / `Home_Water` / `Home_Finish`)。各枠の `KH_WashTool.home` をその種類の場所にした。トレーは棚の左で左から 泡 / 水 / 仕上げ (x = -1.25 / -1.07 / -0.785、z -0.04。トレー 0.73 × 0.42 m、中心 (-0.975, 0.03)。下の「配置の修正」で初回値から変更)。44 が当たり判定の箱から足跡と間隔を出す。
- 出現の音・粒子は、変わった種類の置き場所で鳴らす (台だけの変更はトレー中央)。
- 枠 5・6 (ブラシ・コロン) の Pickup ルートは残す (UdonBehaviour + VRCObjectSync を持つので、消すと型付きの NetworkIDs が宙に浮く。`reset.tools` / `KH_DogWash.tools` / `KH_WashFinishFx.tools` / Wash/12 の数も変えずに済む)。選べる品が無いので常に非表示・持てない。

### ボード

- タブは 5 つのまま (泡・水・仕上げ・洗い場・全部入り = 販売セットの分類と同じ)。並べるのは `order` に載った品だけ (外した品と未制作の品は出ない、Q4)。品名は `itemWords[id]` で引く。表は Editor の `ItemWords` (品の番号ごとの語番号、明示) を 48 が書く (20 + id の計算はしない。語 37 以降は別の文なので、新しい品は語を末尾に足してその番号を表に書く)。
- 出ている品のボタンは `outColor` (淡い金色)。台がすでに檜なら「選択中」(語 38) で押せない。道具は出ていても押せる (トレーへ戻す)。
- 「取り出す」は、選んだ品と**同じ種類**の道具が持たれている間だけ押せない (語 5 を「同じ種類の道具を置いてから選んでください」に変更)。
- 「基本に戻す」の結果がコード 3 なら語 42「持っている道具は、置いてから戻せます」を出す。結果の文 (反映・持たれている・コード 3・もう一度) は 4 秒 (`ResultSeconds`) のあいだ「同じ種類の道具を置いてから」の案内より優先する (KH_WashStationBoard の `_refuseUntil` と同じ形。2026-10-11 Codex 指摘: 持ったまま戻すと、同じ `_Tick` で案内が結果を上書きして見えなかった)。
- 一覧に出ない行 (非表示の行) の写真は空にする (実行時の `_Refresh` と 48)。旧 17 品のときの写真 (例: 泡タブ 3 行目の大きなスポンジ) が非表示の行に残り、外した品の画像がビルドに入り続けていた (2026-10-11 Codex 指摘)。
- Pickup の表示名は種類ごとに出ている品の名前にする (3 つ同時)。

### 変更したファイル

- `Assets/KominkaHillside/Scripts/KH_WashSelection.cs` (同期状態・要求・判定・表示を作り直し)
- `Assets/KominkaHillside/Scripts/KH_WashSelectionBoard.cs` (カタログ・種類別の保持・出ている印・結果 3)
- `Assets/KominkaHillside/Scripts/Editor/KH_WashSelectionEditor.Category.cs` (新規。メニュー 48、44 の種類別検査、カタログ定数)
- `Assets/KominkaHillside/Scripts/Editor/KH_WashSelectionEditor.Verify.cs` (44 を種類別に、45 は 3 種類 + 台を出して撮影)
- `Assets/KominkaHillside/Scripts/Editor/KH_WashSelectionEditor.Build.cs` (42 も同じ構成を作るように。既存シーンでは使わない)
- `Assets/KominkaHillside/Scripts/Editor/KH_WashSelectionEditor.Refine.cs` (泡銃の持ち方を `gunHolds[0]` にも書く。47 は Udon へ転記)
- `Assets/KominkaHillside/Scripts/Editor/KH_DogWashSetupEditor.StationVerify.cs` (Wash/12 の 2 か所。下の判断)
- 新しい U# ファイルは無い (Program Asset の作成は不要。既存の `KH_WashSelection.asset` / `KH_WashSelectionBoard.asset` を再コンパイルするだけ)。

### Wash/12 の基準 FAIL 11 件の判断

| FAIL | 判断 | 対応 |
|---|---|---|
| layout/course: 棚・脚・すのこの solid collider に PremiumTools の Slot0〜6 が入る (4 件) | **Wash/12 側の誤り。** 除外条件 `attachedRigidbody == null` は、**無効な Collider では Rigidbody があっても null を返す**。選ばれていない追加道具の枠は `pickupColliders[s].enabled = false` なので棚として数えられた (層は Pickup 13)。 | 「Rigidbody の下の Collider (= Pickup) は除く」に直し、除いた数と無効の数を行に出す: `[left out: N pickup colliders under a Rigidbody, 7 of them disabled (premium slots not selected)]` |
| floor layer: `Premium_*_SlotN spray collision leaves out 22` (5 件) | **配線側。** 追加品の粒子は Collision モジュールの層マスクが既定の Everything のまま (モジュール自体は無効)。加えて、選ばれていない枠が以前に選んだ品の粒子を持ち続けていた。 | 48 が残す品の粒子と出現の粒子に無料シャワーと同じマスク (Default + Environment) を書く。実行時も Edit 時も、使わない枠の `spray` を空にする。Wash/12 は変えない |
| bottle: tool 4 の KH_WashTool が棚のボトルだけ (8 / 4) | **Wash/12 が追加品を知らない。** 泡銃 (枠 0) は意図してボトルの泡効果 (tool 4 + sprayInput) を使う。 | 各セレクターの枠 0 の泡銃を別に数える: `... a station's bottle (4 / 4), besides the premium FoamCannon slot 0 of each selector, which uses the bottle's foam effect (4 / 4)` |
| layout: KH_WashStation の WashTable が地面に埋まる | 対象外 (別件) | なし |

期待: Wash/12 の FAIL は 11 → 1 (WashTable の 1 件だけ)。増えた FAIL があれば基準 (`scratchpad/baseline/wash12-fails.txt`) と行ごとに比べる。トレーを旧版 (x -1.17..-0.69、z -0.24..0.16) から x -1.34..-0.61、z -0.18..0.24 に広げたので、コースの離隔検査 (手置きの物・歩く線) に新しい FAIL が出たら位置の定数 (`KH_WashSelectionEditor.Category.cs` の `HomeLocal` / `Tray*`) を見直す。

### Unity で行う手順 (統括、この順で)

1. `bash scripts/unity-compile.sh --udon` — C# と Udon のエラー 0。新しい Program Asset は無い。 新しい Editor ファイル `KH_WashSelectionEditor.Category.cs` の `.meta` はここで Unity が作るので、コミット時に `.cs` と一緒に stage する (同じフォルダの `KH_HandFeed*` と `Testing/` の未追跡ファイルは別セッションのもの)。
2. `Tools/KominkaHillside/WashPremium/43 - Prepare Care Font` — **48 より先、シーンが保存済みの状態で。** 43 はシーンを保存する (`SaveOpenScenes`) ため、48 の後に回すと検査前の移行まで保存してしまう。新しい字 58 字は IMP-DOG-047 に一覧。結果 `Temp/WashSelection/font.txt` の `missing=0`、YAML で `CareFont.asset` の `m_SourceFontFile: {fileID: 0}` を確かめる (vrc-world-ui §8)。
3. `Tools/KominkaHillside/WashPremium/48 - Migrate To Category Slots` — 開始時に dirty なら止まる。dirty にするが**保存しない**。`Temp/WashSelection/migrate.txt` の期待値 (保存済みシーンを node で数えた値: 18 Prefab が各 5 個 = 4 洗い場 + 展示 1、展示の子は 17):
   - `selectors=4 removed station objects=44 gallery=11 offered=FoamCannon,PawMitt,WateringCan,Dryer,PatternTowel,HinokiBath`
   - 洗い場ごとに `removed=11 slatsHidden=5 freeTableRenderers=1 (Mesh) homes=Home_Foam(-1.250, 0.820, -0.040) Home_Water(-1.070, 0.820, -0.040) Home_Finish(-0.785, 0.820, -0.040)`、残す 5 品それぞれ `collider <洗い場> <品> size ... -> ... centre ... -> ...` (当たり判定の箱の作り直し。下の「配置の修正」) (freeTableRenderers の数は WashTable の下に他の Renderer が足されていれば増える。すのこと追加品のモデルが入っていないことが要点。初回は `created .../Home_*` が 3 行ずつ)
   - `NetworkIDs before: ... removedTypelessNull=… keptTypedNull=0` と `after:` の 2 行。`WARN NetworkID ... has types` が出たら止めて調べる (消さない)。
4. `Tools/KominkaHillside/WashPremium/44 - Verify Selection` — `Temp/WashSelection/verify.txt` が PASS。期待する行 (抜粋):
   - `PASS four stations own distinct pickups (28) and visuals (24 / 24: FoamCannon, PawMitt, WateringCan, Dryer, PatternTowel, HinokiBath)`
   - `PASS 13 languages / 43 words`、`PASS no dropped item instance in the scene (0; dropped = DuckWaterGun ... FloatingDuck)`、`PASS all 17 visual prefabs kept in Assets`
   - 各洗い場: `PASS itemCategories <洗い場> = [0,-1,-1,-1,-1,-1,1,2,2,0,-1,-1,-1,3,-1,-1,-1]`、`PASS itemSlots ... = [0,-1,-1,-1,-1,-1,2,4,3,1,-1,-1,-1,-1,-1,-1,-1]`、`PASS slotCategories ... = [0,0,1,2,2,-1,-1]`、`PASS three category homes ...`、`PASS slot0 home Home_Foam ...` 〜 `slot6 home TakeoutHome`
   - `footprint <洗い場> <品> x .. z ..` (実測の足跡)、`PASS tray gap <品> / <品> ... cm (>= 1 cm ...)`、`shelf <洗い場>: nearest tray item to the shelf's left edge ... cm`、`PASS tray items stay left of the shelf`
   - `PASS saved backing gunHolds: FoamCannon KH_FoamCannonGunHold, others Grip`、`value <洗い場> selectedFoam=-1 backing=-1` (4 本)、`PASS freeTableRenderers ... = [Mesh] (no slat, no premium model)`、`PASS old slats hidden under ImportedPlatform ... (0 / 5 enabled)`、`PASS dryer wind streaks ... (mode Stretch, speed 2.40)`
   - BUG-DOG-010 の既存行 (`hold FoamCannon ... aimErr=0.00deg upErr=0.00deg`、`slot1..6 stays Grip`) がそのまま PASS
   - 論理 (先頭の洗い場、Edit モードで実行時と同じ `_Decide` / `_Apply`): `PASS Q1 foam + water + finish out at once: pickups [0,2,4]`、`PASS hold per item: FoamCannon Gun / WateringCan Grip`、`PASS foam swap to PawMitt leaves water / finish`、`PASS held water: water request -> 1`、`PASS Q3 held water: platform -> Hinoki accepted and shown, free table hidden`、`PASS held water: foam -> FoamCannon accepted`、`PASS stale revision -> 2`、`PASS Q2 reset with water held -> 3, water stays, others and platform back to free`、`PASS held FoamCannon kept while foam -> PawMitt waits; platform applied meanwhile`、`PASS put down -> PawMitt replaces it`、`PASS FoamCannon Gun hold survives the category cycle`
   - 各洗い場: `PASS board rows reference only listed photos, hidden rows none <洗い場> ()`、`PASS ItemWords: one distinct name word per listed item, inside the 43 words`、`PASS board catalog: ... itemWords = ItemWords`
   - `PASS rendered locale overflow=0 missing=0`、`PASS every word's glyphs baked in VAnimal Care (missing ''; run 43)`
   - `NetworkIDs total=… typelessNull=0 typedNull=0`
5. `Tools/KominkaHillside/Wash/12 - Verify Wash (All Dogs)` — 上の表のとおり FAIL 1 件 (WashTable) だけ。`Temp/KH_DogWashSetupVerify.json` を基準と比べる。
6. シーンを保存。シーンには別セッションの未コミット変更があるので、stage は vrc-scene-edit-safety の手順で分ける。保存後の差分の見込み: 4 洗い場で外した品 44 個と展示 11 個の削除、`Home_*` 12 個の追加、トレー・支柱・TakeoutHome・28 枠の位置、すのこ 20 枚の Renderer 無効、KH_WashSelection と KH_WashSelectionBoard の値、Slot の KH_WashTool の値、粒子の値。
7. 保存後にもう一度 44。`typelessNull` が 0 でなければ 48 をもう一度 (冪等。保存済みなので dirty 判定を通る) → 44 → 保存。
8. `Tools/KominkaHillside/WashPremium/41 - Photograph Listings` (IMP-DOG-047) → 45 `Capture Selection` (`artifacts/wash-listings/2026-10-11/evidence/board.png` と `station.png`。3 種類 + 檜を同時に出した状態)。

### 残り

- ハーネス: `Assets/KominkaHillside/Scripts/Editor/Testing/KH_CourseWalkHarnessEditor.WashPremium.cs` の 4 ケースは古い変数名を文字列で読むため、コンパイルは通るが実行すると落ちる。直す内容: `selectedTool` / `_shownTool` → `selectedWater` などと `_shownTools`、`P-selection-pulse-toggle` は水鉄砲 (0.22 秒の短押し) が一覧から消えたので水の切替をじょうろで組み直す (短押しの確認は IMP-DOG-049 の水鉄砲で)、`P-held-change-and-first-restore` は水が 1 品なので泡 (泡銃 ↔ ミトン) で、`P-free-shower-regression` の期待値を 3 本に。回すかどうかは実装後に Codex と相談する。
- ユーザー: PCVR・Desktop で 3 品を同時に取り出して持つ・使う・置く。持ったまま台を替える (Q3)。1 品を持ったまま「基本に戻す」(Q2、持っている品が残り、案内が出る)。トレーの並びと間隔、棚・ボードとの干渉を見る。
- 2 人: 片方が水を持ったまま、もう片方が泡・台を替える。途中参加で 3 品が出た状態が復元される。Owner 交代。
- 端末: 3 品を同時に使ったときの負荷 (粒子は各 64、1 か所で最大 192) を Quest / Android で。

### 配置の修正 (2026-10-11、コードのみ、未適用)

統括の 1 回目 (48 → 44、保存せず破棄): 687 PASS / 20 FAIL、すべてトレーの配置 (じょうろが泡・ミトン・ドライヤー・タオルと 2〜26 cm 重なる、品が棚の左端 x -0.600 を 1〜9 cm 越える、4 洗い場とも)。足跡は洗い場ごとに違い、川辺が最小・展望のベンチが最大だった。

- 原因: 当たり判定の箱 (`colliderCenters` / `colliderSizes`) が膨らんでいた。42 の `LocalBounds` は Renderer.bounds (世界座標の軸平行の箱) の 8 隅を枠の座標へ戻して囲むので、向きのある洗い場では「回した箱の箱の箱」になり、幅 w・奥行き d の品は x = w + d·sin(2·yaw)、z = d + w·sin(2·yaw) に広がる。1 回目の足跡から逆算すると、川辺 (yaw 81.8°) の値から解いた実寸で展望のベンチ (221.5°) の足跡を 3 mm 以内で再現でき、実寸はラボの寸法と一致した (泡銃 0.083 × 0.203、ミトン 0.125 × 0.194、じょうろ 0.150 × 0.360、ドライヤー 0.082 × 0.184、タオル 0.331 × 0.225 m)。Pickup の実際の当たり判定も同じだけ大きかった。
- 修正: 48 が残す道具 5 品の箱を、各メッシュ自身の bounds の 8 隅から作り直す (`TightBounds`、洗い場の向きに依存しない)。旧→新を migrate.txt に 1 行ずつ出す。配置の定数 (`KH_WashSelectionEditor.Category.cs`): `HomeLocal` = (-1.25, 0.82, -0.04) / (-1.07, 0.82, -0.04) / (-0.785, 0.82, -0.04)、トレー `TrayLocal` (-0.975, 0.785, 0.03)・`TrayScale` (0.73, 0.03, 0.42)、支柱 `PostLocal` (-0.975, 0.38, 0.03)、`TrayCentre` (TakeoutHome) (-0.975, 0.82, -0.04)。
- 予測 (箱が向きに依存しなくなるので 4 洗い場で同じ): ミトン / じょうろ 5.3 cm、泡銃 / じょうろ 6.3 cm、じょうろ / タオル 4.4 cm、じょうろ / ドライヤー 16.9 cm、タオル / 棚の左端 2.0 cm。品の範囲 x -1.324..-0.620・z -0.152..0.217 に対しトレー x -1.340..-0.610・z -0.18..0.24 (左 1.6 cm・右 1.0 cm 内側)。いちばん狭いのはタオルと棚の 2.0 cm。
- 44 の許容範囲 (種類の違う品どうしが 1 cm 以上、棚の左端より左) は、このとおり一列で満たせるので緩めていない。棚の上の無料道具 (x -0.6..0.6)・ボード (x +0.95 側)・すのこ (z -1.55..-0.85)・脇の立ち位置 (z が同じ帯) とはトレーが重ならない。トレーと支柱には Collider が無い。
- 未確認: Wash/12 のコース離隔検査 (トレーが広がった分)。モデルの高さ (y) は以前の値のまま (向きは y 軸まわりだけなので高さは膨らまない)。

### シーンへの適用 (2026-10-11、統括)

コンパイル 0 (C# / U#) → `WashPremium/43` (CareFont 585 文字・不足 0、`77f7c3e`) → `48` 1 回目は 44 で 20 FAIL (トレーの重なり。当たり判定の箱が洗い場の回転で膨らんでいた) → 保存せず開き直し、配置を直して → `48` (各洗い場で 11 品を削除、提供 6 品 = 泡銃・肉球ミトン・じょうろ・ドライヤー・柄タオル・檜、NetworkIDs 336 → 336、型なしの削除 0) → `44` **707 PASS / FAIL 0** → 保存 → `Wash/12` **1 FAIL** (作業前 11。残りは範囲外の主洗い場の台の接地) → 保存後の `.unity` に `gameObject: {fileID: 0}` 0 件。シーンの差分は有料道具まわりと、中央支柱の位置の誤差の再計算 1 件だけ。

残り: ユーザーの PCVR (3 枠を同時に出す、道具を持ったまま台を替える、「基本に戻す」で持っている枠が残る、ドライヤーの風の筋)、Desktop、2 人で同じ選択が見えること。リスティング画像 (41・45) は未撮影。`Testing/KH_CourseWalkHarnessEditor.WashPremium.cs` は `selectedTool` などの旧名を読むので、実行すると失敗する (ハーネスは未修正・未実行)。訳は AI 作成・校正なし。
