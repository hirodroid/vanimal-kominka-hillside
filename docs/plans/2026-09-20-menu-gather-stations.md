# メニュー・集合・リード拠点の実装計画と記録

状態: 着手 (2026-09-20)。合意の正本は [game-design §4「集合と拠点のリード」「メニュー補助の追加合意」](../game-design.md)、残る実装詳細は [DES-DOG-001](../development/issues/DES-DOG-001-leash-walking.md)、画面は [world-ui-spec §4〜7](../world-ui-spec.md)、UI 課題は [DES-UI-001](../development/issues/DES-UI-001-menu-and-leash-assist.md)。ユーザー指示「設計を整備したので実装、適宜レビュー」(コミット `e46b555`) に基づく。

## 段階

| 段階 | 内容 | ゲート |
|---|---|---|
| 1 | 共有契約と拠点: 目的地の本人選択と追加犬への継承、自己解除の距離不問、集合 (`KD_GroupActivities` の裏山改変: 接続犬除外・参加犬の固定・斜面配置・ライブなし・トレーナー権利 `KH_TrainerPass`)、集合終了後の自由行動の中心、リード拠点 3 か所と最寄り回収、ボールの個別回収 `KH_ToyRecall` | Editor 検査 + Codex D/M |
| 2 | 手元メニュー `KH_HandMenu` (古民家と同じ開き方・板・タブ意匠): さんぽ / あそぶ / おもちゃ。日本語 TMP フォントの導入。仮看板の格下げ | Editor 検査 + Codex D/M/U/V |
| 3 | Play・PCVR・2 人・Desktop/Touch・購入権の双方向確認 (ユーザー) | チェックリスト |

## 段階 1 の設計判断 (実装時に決めた値。Play で見直す)

- **本人の選択**: 目的地の選択は各クライアントがローカルに保持 (`KH_LeashManager._myChoice`、既定 = 広場)。接続要求に選択を添えて送り、Manager Owner が `destinations[dog]` に書く。これで「追加犬は本人の選択を継承」「全解除後も同じ滞在中は保持」「再入室・未選択は広場」「接続者交代時は新しい接続者の選択」がすべて 1 つの規則になる。前段階の `lastWalkers` による初期化規則は撤去。
- **自己解除**: 自分の接続犬への `ReqDetach` は距離不問。他人の犬は不可 (従来どおり)。
- **集合**: `KD_GroupActivities` (裏山コピー) を改変。開始時の参加犬をビットマスク `participants` に固定し、`Available(i)` は接続犬・参加外を除外。`ValidPlacement` は地面法線 0.5 以上・高低差の制限なしに緩和し、障害物判定の球は法線方向へ持ち上げる。ライブ参照が無くても開始できる。権利は `KH_TrainerPass` (同じ Product/Listing) で判定。60 秒制限・途中終了・開催者退出は継承。
- **歩いて集合**: Wander の集合移動 (mode 1) を裏山では NavMesh 経路優先にし、完全経路が無いときだけ直線で進む (すり抜け)。停滞検知の追加は Play 後。
- **終了後の自由行動の中心**: 集合が終わった瞬間 (60 秒・途中終了・開催者退出) に Activities が Manager の `_OnGatherEnded` を呼び、参加犬の現在位置 (NavMesh 上へ吸着) を `releasedHomes` に書いて同期。到着前に終了した犬も現在位置。
- **拠点**: 入口 (既存ラック)・見晴らし・広場に各 1 基。各拠点に左用・右用の 2 スロット、6 本のローカル道具 (3L/3R) をスロットへ配る。VR は持った手が左右、Desktop/Touch は道具の左右ラベルが左右。接続成功・手放し・タイムアウト後は、その地点から NavMesh 経路長が最短で空きスロットのある拠点へ戻る (同距離は拠点順)。解除地点に道具を出す旧経路は撤去。
  - **合意との差 (要確認)**: 合意文は「解除後の道具を解除地点の最寄り拠点へ回収」だが、この実装では道具は接続が成立した瞬間に最寄り拠点へ戻り、散歩中に道具は存在しない (紐は首輪側が描く)。解除時に戻す物が無いため、解除地点での回収処理は無い。各拠点は誰かが道具を握っている間を除き常に左右 1 本ずつを保つので、「山上で外したら山上の拠点から付け直せる」は満たす。合意文の意図がこれと違えば設計へ戻す。
- **単一の権威**: `KD_GroupActivities` は `KH_LeashManager` と同じ GameObject (`KH_Leashes`) に置き、接続と集合の裁定を同じ Owner が直列に行う (Codex D 指摘)。集合の終了は Activities 側が終了した瞬間に `_OnGatherEnded(参加マスク)` で Manager に渡す (Manager の 0.5 s ポーリングは撤去)。
- **ボール**: `KH_ToyRecall` が古民家の個別回収を移植。使用中判定 (持たれている / Fetch 中 / かけっこ) を継承し、頭の向き 1.2 m 先の地表へ置く (下向きレイ、無ければ足元高さ)。一括リセットなし。

## 実装記録

### 段階 1 (2026-09-20)

- 追加: `KH_TrainerPass.cs`、`KH_ToyRecall.cs`、`Assets/KominkaHillside/Data/CE/KD_Product_Trainer.asset` (古民家と同 GUID)。改変: `KH_LeashManager.cs`、`KH_LeashClip.cs`、`KH_LeashCollar.cs`、`KH_WalkSign.cs`、`KD_GroupActivities.cs` (裏山コピー)、`KD_DogWanderController.Live.cs` / `.HillsideLeash.cs` (GatherStep)、`KH_DogWalkingEditor.cs` (Build/Verify)。
- Editor 検証: C# / U# (75 スクリプト) コンパイル成功、Udon 診断 0、Console エラー 0。`Walking/5 Verify` **111 項目、失敗 0** ([出力](evidence/2026-09-20-menu-gather-stations/verify.txt))。追加項目: 集合権威が Manager と同じ GameObject・Manual、pack/6 ボール/ライブなし/斜面許容の配線、トレーナー権利の Product/Listing ID 一致、全 Wander/Fetch の activities 参照、ToyRecall 6/6、3 拠点 × 左右 1 スロットずつ (親・一意)、6 道具が各スロットに 1 本ずつ、拠点の高さ、NetworkIDs の残骸 0。backing の参照配列 (stationSlots / activities / balls / hillsidePass) は Inspect Compilation で確認 ([出力](evidence/2026-09-20-menu-gather-stations/compilation.txt))。
- Editor 画像: [広場の拠点](evidence/2026-09-20-menu-gather-stations/20-KH_Station_Plaza.png)。
- Play・実機・2 人・購入権の双方向は未確認 (段階 3)。

#### 独立レビューと是正 (段階 1、Codex D/M)

| 指摘 | 対応 |
|---|---|
| D 高: Manager Owner と Activities Owner が別人だと同じ犬の接続と集合参加を両方受理し得る | Activities を Manager と同じ GameObject へ移し単一 Owner に。旧 `KH_Activities` は Build が削除し、NetworkIDs の残骸も掃除 |
| D 高: 集合終了を Manager の 0.5 s ポーリングで観測すると取りこぼす (即取消・Owner 交代) | Activities が終了した瞬間に `_OnGatherEnded(participants)` を直接呼ぶ (同じ Owner)。ポーリング撤去 |
| D 中: ボール回収の pending 中に別要求すると結果の対象が入れ替わる | 拒否は記録せず、完了・失敗・タイムアウト時に対象行と結果を同時に書く |
| M 中: 拠点の初期配置が看板の存在 (構築順) に依存 | 地形の場所 (KH_Places) と看板と同じオフセットから直接置く。場所が無ければ Build を止める |
| M 中: 「解除地点の最寄り拠点へ回収」の合意と実装の差 | 上記「合意との差 (要確認)」として明記。ユーザー確認待ち |
| M 中: Verify が各拠点の左右 1 枠・重複を見ていない | 6 個の一意な Transform、(拠点, 左右) の一意性、親の一致、道具位置の重複なしを検査 |
| M 低: KD_GroupActivities の Owner 説明が旧構造 | ヘッダを裏山の構造 (Manager と同一 GameObject) に書き換え |
