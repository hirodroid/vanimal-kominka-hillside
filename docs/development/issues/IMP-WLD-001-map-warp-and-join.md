---
id: IMP-WLD-001
type: IMP
system: WLD
status: review
priority: Medium
summary: 地図の副画面、川辺・奥広場の 2 地点ワープ、インスタンス内のプレイヤーへの合流、接続犬の同行、暗転を、DES-WLD-003 の確定設計 (2026-09-27) どおりに実装する
next: "残り: PCVR 2〜3 人 (設計書 §C.2 #1・#2・#6・#7、#3〜#5)、VER-WLD-001 (Quest 単体・iOS の Touch・通知板と Focus View・暗転・表示名の字 #8b・16 人の通知間隔)。PCVR 1 人 (A118〜A122) は 10-02 ユーザー総合判断で確認済み。破壊監査 (2026-09-28) の 4 RC は反映済み"
updated: 2026-10-02
---

# IMP-WLD-001 地図・2 地点ワープ・合流と接続犬の同行を実装する

[DES-WLD-003](DES-WLD-003-map-and-travel.md) の設計は、2026-09-27 にユーザーが確定した。その初期公開の範囲を実装する。仕様の正本は [設計書](../../plans/2026-09-27-map-and-travel.md) で、本 Issue は段階と完了の条件だけを持つ。契約の中身 (要求 ID 付きの切符、Phase の同期、本人が着いた後に 1 回だけ適用すること、共通の検査 5 つ、受付箱) は設計書 §B・§D.3 から写さない。

## 初期公開の範囲 (設計書 §0「初期公開に必須のものと、後回しにするもの」)

- 含むもの:
  - さんぽタブの副画面に地図を出す。目印は 6 地点を描くが、ワープできるのは 2 地点だけ。
  - **川辺 (`spot.river`) と奥広場 (`spot.upper`) の 2 地点**へのワープ。
  - 接続している犬が一緒に来ること。
  - 暗転 (全画面の黒、queue 5001)。
  - インスタンス内のプレイヤーへの合流 (フレンド判定は使わない)。受け付けの初期値は ON。
- 含まないもの:
  - 残り 4 地点へのワープ。追加は Play の後にユーザーが決める。`spotWarpEnabled` を変えて再生成するだけで足せるように作る。
  - 地図に全員を常時表示すること、地図の精細化、リスポーン後に切符を出すこと、一部の犬だけを連れていく操作。

## 段階と完了条件

### 段階 1: 2 地点ワープと接続犬の同行 (設計書 §B・§D.3〜D.6)

- `KH_LeashManager` に次を足す: 切符の配列 7 本 (長さ 6 で初期化)、`ReqTravel` / `CancelTravel` / `ReqTravelDone`、送り主と `dogMask` の照合、同じ犬への 5 秒のクールダウン。
- 犬の側に次を足す: `KD_DogWanderController.HillsideTravel.cs` を新設し、適用処理を 1 か所にまとめる (Return の判定より前に置く)。そこで経路の状態を初期化し、完了を報告する。
- 地点 ID の解決: Editor の生成器と Verify を作る。`spotPositions` / `spotYaws` / `spotWarpEnabled` を持たせ、共通の検査 1〜4 をかける。
- 仮の UI: さんぽタブの上段に、[奥広場へワープ] と [川辺へワープ] のボタンを置く (段階 2 で地図の面へ移す)。要求の待ち (4 秒で再送、8 秒で取りやめ)、門番、クールダウンも入れる。

完了条件:

- [x] コンパイルのエラーが 0。`_` の規約の検査を通す。Verify で、Manager の同期配列の長さと初期値、2 地点の解決と共通の検査、`spotWarpEnabled` が PASS する。
- [x] ClientSim ハーネス `KH_CourseWalkHarnessEditor` の travel モードで、設計書 §C.1 の T1〜T9 と T11 が PASS する。T1 は 4 構成 × 3 回の移動。T10 は段階 3 で行う。
- [ ] PCVR 2〜3 人で、設計書 §C.2 の #1・#2・#6・#7 を行う。1 人で 2 地点、Owner でない人の移動、Owner の交代、途中参加。

#### 実装 (2026-09-27)

- 変更: `KH_LeashManager.cs` (partial 化、解除で切符を消す、Update で要求の待ちと期限切れ) / 新規 `KH_LeashManager.Travel.cs` (切符 7 本、`ReqTravel` / `CancelTravel` / `ReqTravelDone`、要求する人の門番と待ち、共通の検査 5 つ、試験用の debug 値) / 新規 `KD_DogWanderController.HillsideTravel.cs` (適用処理) と `HillsideLeash.cs` の 2 行 (Return より前の呼び出し、準備中は追従テレポートを止める) / 新規 `KD_TreatPouch.Travel.cs` と `Walk.cs`・`Toy.cs` の 1 行ずつ / 新規 `Editor/KH_CourseTravelEditor.cs` / 新規 `Editor/Testing/KH_CourseWalkHarnessEditor.Travel.cs` と本体の 3 行 / `CourseUITranslations.json` に `course.travel.*` 13 キー × 13 言語。
- メニュー: `Course/80 - Build Travel Spots and Warp Buttons` (地点の解決・Manager への書き込み・上段の 2 ボタン・翻訳の入れ直し。シーンを保存する)、`Course/81 - Verify Travel Spots and Warp Buttons` (`Temp/imp-wld-001/travel-verify.json`、PASS は `TRAVEL_OK`)、`Course/82 - Walk Harness Travel (T1-T9, T11)`、`Course/83 - Walk Harness Travel Quick`。停止は `Course/41`、PlayerData の復旧は `Course/46`。
- 統括が実行する順: コンパイル (`bash scripts/unity-compile.sh --udon`) → Course/80 → Course/27 (新しい字形) → Course/81 と Course/26 → Course/83 → Course/82。ハーネスの結果は `Logs/kh-course-walk/<runId>/report.json` と `travel.json`、PASS は `TRAVEL_OBSERVED`。
- 設計からの変更: `ReqTravelDone` の上限は 6/s (1 回の移動で最大 6 匹ぶん届くため)。T9 は合流の部分を段階 3 に回し、取消と書き込みの行き違いと退出だけを行う。
- 実測 (2026-09-27、統括が実行): C#/U# のコンパイル エラー 0。Course/80 BUILT (6 地点を解決、ワープは upper / river)。Course/27、Course/81 TRAVEL_OK、Course/26 OK。Course/83 TRAVEL_QUICK_OBSERVED (T1 犬 1 匹 × 3 回の移動: 位置の変更は全頭ちょうど 1 回、Phase 1→2、到着後 30 秒の Return 0。T11 連打: 新しい reqId は 1 つ、2 回目以降はクールダウンの表示)。Course/82 TRAVEL_OBSERVED (20260927-043153: T1 の 4 構成、T2〜T9、T11 の 12 ケース PASS。T2〜T9 の Owner 交代・遅着・偽装・退出は 1 端末の模擬で、実通信・PCVR は未確認)。
- 独立レビュー (Codex、D+M / U+X+P) の反映: 移動の直前に人の検査をやり直す、送り主の犬に載っている reqId より古い要求を捨てる、T6 を Owner 交代の模擬にする、Verify は保存した川・橋の値で検査する、待ちの残り秒数・「犬を待っています」・「ワープを取りやめました: 理由」の表示 (course.travel.* に 5 キー追加)。反映後は Course/80 と 27 を流し直してから 81 → 83 → 82。

#### 破壊監査の反映 (2026-09-28、RC-01・RC-02。ClientSim で全通過)

- 実測 (2026-09-28、統括が実行): C#/U# コンパイル エラー 0。Course/80 BUILT、81 TRAVEL_OK。Course/83 TRAVEL_QUICK_OBSERVED 2/2 (20260927-221133)。Course/82b TRAVEL_RC_OBSERVED 8/8 (20260927-230713: RC-setup、T6b、T12 × 4、T9 × 2)。Course/82 TRAVEL_OBSERVED **22/22** (20260927-230956)。途中の FAIL 3 件はどれもハーネス側の競合で、実装は Return の Coast 待ち以外変えていない: T12-walk-in が前の要求の Phase 2 を完了と読んだ (reqId で照合)、T9-join-history-kept の NRE (同フレームで 16 人湧かせた直後の VRCPlayerApi。1 s 待って IsValid で選ぶ)、T12-join-near-landing の「動いた」判定が切符の寿命 15 s の後に走った (12 s で判定し、期限切れ待ちを 14 s に)。ClientSim では RemovePlayer で OnPlayerLeft が来ることを確認した。

監査の記録は `docs/_sessions/destructive-audit-2026-09-28/` (FUNC-01・02・06・07 と triage.md)。

- RC-01 (FUNC-01 BUG-01・FUNC-06 BUG-01、本人の移動前に犬が切符を消費する): 切符の Phase に **4 = 本人が移動済み** を足した。
  - 要求する人は TeleportTo の後、着地点から 2 m 以内に入ったのを一度確かめてから `ReqTravelMoved(reqId)` を Manager Owner へ送る。Phase 1 の犬が残る間は 1 秒ごとに送り直し、確かめた後の再送は距離によらない (初回が失われた後に本人が 2 m より離れても止まらない)。
  - Manager は、送り主の要求 ID で Holder・接続が一致する Phase 1 だけを 4 にし、適用期限をそこから数え直す。取消済み・別の要求・もう 4 / 2 の切符は変えないので、遅延・重複の通知は冪等で、取消済みの切符を復活させない。
  - 犬の側の適用条件は「Phase 4 かつ飼い主が L から 6 m 以内」。Phase は同期値なので、途中参加・Owner 交代でも復元できる。完了 (Phase 2) は 4 からだけ。取消は従来どおり 1 → 3 だけ (本人が移動した後は取り消せない)。
- 反映後の 83・82 で T1-dogs-1-L1 が「到着後 30 s に Return」で落ちた (2026-09-28): 本人の TeleportTo から Phase 4 が同期されるまでの数フレーム、犬は handDist 588 m で Return に入っていた。`KD_DogWanderController.HillsideLeash.cs` で、未適用の切符が待ち (Phase 1 か 4、期限内、holder 一致) の間は Return に入らず Coast で待つようにした (期限切れなら通常の Return、適用済みの完了再送中は通常どおり追う)。
- RC-02 (FUNC-01 BUG-02、Done の再送が距離で止まる): 適用済みの要求の完了の再送を距離の判定より前へ移した。要求 ID・接続先 (Holder == walkers)・Phase 4・15 秒の期限の検査は残す。
- 変更: `KH_LeashManager.Travel.cs` (Phase 4、`ReqTravelMoved`、`TravelTickDogs` の送り直し、`debugDelayTravelMoved`、期限切れの片付けに Phase 4 を含める、`_TravelCancelable()`)、`KD_DogWanderController.HillsideTravel.cs` (適用条件と再送の順序)、`Editor/KH_CourseTravelEditor.cs` (新しい debug 値も 0 を検査)、ハーネス。
- 独立レビュー (Codex、2026-09-28): 高 1・中 2・低 1 → すべて反映。高 = 移動済みの再送が毎回 2 m 以内を要求していた (`_tvArrivedOnce` で初回だけに)。中 = T12 遅延ケースが再送なしでも通る (初回を捨てるケースを追加)、T9 履歴が 16 枠の循環配列でも通る (在室中の有効 ID・30 秒経過・退室を追加)。低 = コメントの食い違い (再送の条件、本人待ち 15 秒と犬の期限の起点の違い)。RC-02・RC-06 の網羅・RC-07 の探索順・`_` 規約・NetworkCallable の照合・既存 T1〜T11 は問題なし。
- ハーネスのケース (Course/82、2026-09-28 時点): T1 × 4 構成、T2 応答なし、T3 遅れた応答、T4 本人が移動しない、T5 移動前の Owner 交代 (ハーネスの TeleportTo の後に移動済みも代わりに送る)、T6 完了前の Owner 交代 (Phase 4 のまま)、T7 待ち中の接続変更、T8 他人の犬・偽装、T9 取消と退出、T10 合流の着地点 5 条件、T9 合流側 4 件、**T6b** 完了を犬ごとに 3 回落として本人が 6 m より遠くへ (RC-02)、**T12 歩いて近づく** 確認待ち中に着地点 4.5 m まで歩く (RC-01、移動済みの後だけ適用)、**T12 移動済みの遅延・重複** 2 秒遅れ + 2 回処理 (RC-01)、**T12 初回の移動済みの喪失** 初回通知を捨て (`debugDropTravelMoved`)、本人が着地点の 2〜6 m へ動いた後の再送で Phase 4 → 適用 1 回 → Done (RC-01、レビューの高)、**T12 近い合流** 着地点 3〜5 m で (a) TeleportTo を省く・(b) 暗転中に実ボタンで取消 → 本人未移動なら犬の適用 0 回 (RC-01)、**T9 速い承認の取消** 0.2 秒承認・0.3 秒で実ボタン (RC-06)、**T9 履歴** 拒否 17 件・断り 9 件に加え、在室中 16 人で満たしてからの 17 件目で伸長して全件保持 (拒否・断り)、30 秒経過の断りの枠の再利用、退室後に両方の履歴から枠が空く (RC-07、レビューの中)、T11 連打。Course/83 は T1 1 匹と T11。 **Course/82b** は破壊監査の新規ケース (T6b・T12 の 4 つ・T9 速い承認の取消・T9 履歴) だけを、準備 (犬 0〜2 を左手・上の広場へ 1 回) の後に回す (結果 TRAVEL_RC_OBSERVED)。新規ケースの完了待ちは今回の要求 ID の切符だけを数える (82 の初回で T12 歩いて近づく が前のケースの Phase 2 を読んで先へ進んだため、2026-09-28)。

#### 既知制限 / 再開条件 (2026-09-28 破壊監査のトリアージ)

| RC | 元の BUG | 影響 | 見送りの理由 | 再開する条件 |
|---|---|---|---|---|
| RC-03 | FUNC-01 BUG-03 (B) | 準備の切符を受けた後に端末が 17 秒ほど止まってから再開すると、犬の側では期限切れの切符で本人だけ移動する (犬は通常の追従で追いつく) | 通常の追従で回復する。確認時と移動直前に犬側と同じ期限を見る修正は、長い停止からの復帰でワープを拒否する挙動と暗転の解除が増える | 実機で停止からの復帰後に犬が置き去りになる報告が出たとき |
| RC-05 | FUNC-01 BUG-05 (B) | 完了した切符を接続したまま約 25 日保持すると、符号付きの時刻差で新しいワープが「5 秒以内」として拒否される | 長期間の保持が前提で、リードの解除で回復する。5 秒の連打抑制を弱めない確認が要る | インスタンスの長時間運用で実際に拒否が出たとき |
| RC-04 | FUNC-01 BUG-04・FUNC-02 BUG-01 (A → 実害は B) | 別々のワープ・合流が互いの移動前の位置を読み、同じ着地点付近 (0.5 m 間隔) に着く。人・犬・リードが重なる | 監査で示された実害は重なりだけで、足場・水域の検査は破られていない。犬 0 匹も含む着地点の予約・失効・退出処理は費用に見合わず、予約しても第三者の歩き込みは防げない | 体験会・実機で重なりが繰り返し問題になったとき (Manager で着地点を裁定する案から) |

### 段階 2: 地図の副画面と暗転 (設計書 §A.1〜A.3・A.5・D.1・D.2・D.8)

- 地図のテクスチャを作る。周回路と川のデータから描き、512² を試作の初期値にする。
- 地図の面を作る: 1 列 × 2 行の一覧、固定フッター、[ここへワープ]、自分の位置と向きの表示 (10 Hz)。
- 着いた後に `PlaceInFront` をもう一度呼び、[さんぽへもどる] を出す。
- `KH_TravelFade` を作る (alpha で黒へ、queue 5001、黒を描いてから移動)。着いたときの向きも合わせる。
- 文言はコースの翻訳表と字形の追加の手順に乗せる。

完了条件:

- [x] Verify で、面の配線・文言の収まり・欠けた字 0・テクスチャの設定 (ミップマップなし・Clamp・圧縮形式) が PASS する。容量を測って記録する。
- [x] PCVR 1 人で、設計書 §C.2 #1 の暗転 (昼・夜・ライブ中、両眼)・向き・「ワープした後に歩き出すか」を、ユーザーが確かめる。

#### 実装 (2026-09-27、段階 2。未コンパイル・未検証)

- 変更: 新規 `KH_TravelFade.cs` (ローカルの暗転。0.2 秒で黒 → 黒を描いたフレームの次に移動 → 1 フレーム後に 0.3 秒で戻す、黒のまま 3 秒で自動で戻す) と `Shaders/KH_TravelFade.shader` (alpha で黒へ、queue 5001、ミラーでは描かない) / `KH_LeashManager.Travel.cs` (状態 3「暗転中」、`travelFade` の参照、暗転の間も取消・接続の変化・人の検査) / `KD_TreatPouch.Travel.cs` (副画面、10 Hz の自分の位置と向き、選ぶ → [ここへワープ]、着いたら `PlaceInFront` と [さんぽへもどる]) / 新規 `Editor/KH_CourseTravelMapEditor.cs` / `Editor/KH_CourseTravelEditor.cs` (Course/80 は地点と翻訳だけにし、81 から段階 2 の検査を呼ぶ) / ハーネス T1 に暗転の観測 / `CourseUITranslations.json` に `course.travel.map.*` 12 キー × 13 言語。
- メニュー: `Course/84 - Build Travel Map and Fade` (1 回目は KH_TravelFade の Program Asset を作って止まる。2 回目で地図テクスチャ `Assets/KominkaHillside/Textures/Map/KH_TravelMap.png`、暗転の板、[地図と移動] と副画面を作ってシーンを保存。`Temp/imp-wld-001/travel-map-build.json`)。検査は `Course/81` に含めた (容量は `travel-verify.json` の `mapPngBytes` / `mapGpuBytesPC` / `mapGpuBytesMobile`)。
- 統括が実行する順: C# コンパイル → Course/84 (PROGRAM_ASSET_CREATED) → `unity-compile.sh --udon` → Course/84 (BUILT) → Course/27 → Course/81 (TRAVEL_OK) と Course/26 → Course/83 → Course/82 (T1 の notes に「fade: black before the move」)。
- 翻訳表の `course.travel.map.with` は残し、使わない: 英語が 590 幅からはみ出したため `course.travel.map.footer2` に差し替え、表は追記のみの規則なので旧キーを消さない。段階 3 の W2 レビュー U10 で、フッターは「ワープ先 / 犬の数 / 散歩先」の 3 行 (`map.target` / `map.dogs` / `map.walkdest`) に分けた。
- 設計からの変更: 地図の番号は ①〜⑥ ではなく数字 1〜6 (丸数字の字形を足さないため)。状態の 1 行は板の下端ではなく副画面の右下 (設定の枠の中では下端の Feedback が出ないため)。[人] の切替は段階 3。暗転中 (明けるまで) は副画面の入力 ([もどる]・地点の選択・[ここへワープ]) を受け付けない (W2 レビュー D3 で追加)。暗転の queue は 5000 (W2 レビュー D1。設計書 §D.1 の実装の行)。

### 段階 3: 合流 (設計書 §A.4・A.6・A.7・D.3・D.4)

- `KH_TravelMailbox` を作る。`VRCPlayerObject` に載せ、同期は Manual で `accepting` を 1 本。`ReqJoin` / `AnsJoin` / `CancelJoin` を持つ。
- 人の一覧を作る。開いている間は並べ替えず、playerId で持つ。一人のときの表示も用意する。
- `KH_TravelNotice` を作る (名前と場所、3 m 歩いたら置き直す、手元のメニューからもう一度出せる)。
- 応答の code 4 つ、通知の間隔 20 秒、10 分の休止、この滞在中は受けない設定、設定面の「合流の申し込みを受ける」を入れる。
- 合流の着地点の候補を決める (相手が橋の上・水中・歩行中・コースの外のとき)。
- 表示名の欄に補助フォントを試作する (設計書 §A.6)。

完了条件:

- [x] Verify で、受付箱の同期方式と配線、通知板、設定の行が PASS する (設定の行は副画面の [人] の上段へ移した。段階 3 の実装記録)。ClientSim で T10 (合流の着地点) と T9 (退出・取消の競合) が PASS する。
- [ ] PCVR 2〜3 人で、設計書 §C.2 の #3〜#5 を行う (申し込みの code ごとの文言、相手が橋の上・川床・歩行中、退出)。

#### 実装 (2026-09-27、段階 3。未コンパイル・未検証)

- 変更: 新規 `KH_TravelMailbox.cs` (VRCPlayerObject・Manual・同期は `accepting` 1 本。`ReqJoin` / `AnsJoin` / `CancelJoin` は `[NetworkCallable]` で CallingPlayer と seq を照合。受け手の保留 1 件・20 秒の時間切れ・前の通知から 20 秒は code 2・10 分の休止は code 3・「この人からは受けない」は code 0。同じ相手へは断り・時間切れから 30 秒。受け付けの ON/OFF は PlayerData `KH_TravelJoin.accepting` に保存) / 新規 `KH_TravelNotice.cs` (通知板。目の前 1.1 m・15° 下、3 m 歩いたら置き直し、手元の「申し込み 1 件」から出し直し) / `KH_LeashManager.Travel.cs` (`_TravelFindJoinLanding`: 通常・歩行中・橋の上・川の中・道の外の 5 つと理由、`_TravelBeginJoin`: 地点ワープと同じ切符と暗転) / `KD_TreatPouch.Travel.cs` ([地点] / [人] の切替、16 行の人の一覧、[合流をたのむ] / [取り消す]) / 新規 `Editor/KH_CourseTravelJoinEditor.cs` / ハーネス T10 (合流の着地点 5 条件) と T9 の合流側 (退出・取消と承認の行き違い) / `course.travel.join.*` ほか 40 キー × 13 言語。
- メニュー: `Course/85 - Build Travel Join` (1 回目は KH_TravelMailbox / KH_TravelNotice の Program Asset を作って止まる。2 回目で受付箱の雛形・通知板・[人] の一覧・「申し込み 1 件」を作ってシーンを保存。`Temp/imp-wld-001/travel-join-build.json`)。検査は `Course/81` に含めた。
- 設計からの変更: 「合流の申し込みを受ける」は設定面ではなく副画面の [人] の上段に置いた (設定ホームの「この部屋のみんな」の群に 44 px の行を足す余白が無く、配置の正本 `KD_MenuFinishEditor.Hillside` の再配置が要るため)。表示名の補助フォント (§A.6) は段階 4 で入れた (当初の「DES-UI-004 へ引き継ぐ」は撤回。2026-09-27 ユーザー決定「初期公開にこれ全て入れる」)。`ReqJoin` の引数は `seq` だけ (送り主は CallingPlayer で決まるので playerId を運ばない)。
- 採用の理由: 人の一覧は入退室のイベントではなく 1 秒ごとの見直しで保つ (KD_TreatPouch に OnPlayerJoined / Left の override を足さず、古民家から移した本体に手を入れないため。反映は最大 1 秒遅れる)。距離と自分の位置は副画面を開いている間だけ 10 Hz で書く (相手の受付箱は行ごとに覚え、開いたとき・入退室・未取得のときだけ探す)。「この人からは受けない」は「このインスタンスにいる間」で 13 言語をそろえた (`course.travel.join.notice.block2`。旧キーは残す)。
- T10 の記録: 相手は ClientSim の人形 (Udon なし) で、playerId は SpawnRemotePlayer が割り当てたもの。置き場所は 通常 = 広場のノード +8 / 橋の上 = z 490 の床のノード / 川の中 = z 285 の川の中心 / 歩行中 = 広場のノード +20 から 2 m/s で道の正方向 / コースの外 = (-55, 0, 715)。速さは受付箱が 0.5 秒ごとに 2 回以上測ってから着地点を決める。
- 未確認: 2 端末での ReqJoin / AnsJoin の順序、VRCPlayerObject の入室直後の「準備中」、通知板の見え方と Focus View、16 人のときの通知の間隔。ClientSim の T10 / T9 は人形 (Udon なし) を相手にした模擬 (`debugJoinLoopback`)。
- ClientSim で確かめたこと (2026-09-27、82 TRAVEL_OBSERVED 15/15、コミット a4935ab): T1 4 構成 × 3 移動 (位置の変更は全頭ちょうど 1 回・Phase 1→2・到着後 30 秒 Return 0・alpha ≥ 0.99 を観測してから移動し 1.5 秒以内に明転)、T2〜T9、T10 の 5 条件 (通常 3.5 m / 橋 18 m / 川 14.1 m / 歩行中 5.5 m / コースの外は移動しない)、T9 の合流側 4 件、T11。81 TRAVEL_OK (配線・収まり・欠字 0・地図テクスチャ 35,051 byte、GPU PC 131,072 / mobile 118,336 byte)。
- 実機でしか分からないこと: 2 端末での申し込みと返事の順序・Owner の交代・途中参加、暗転の見え方 (昼・夜・ライブ・両眼) と取消が暗転中に押せるか、通知板・通知音・Focus View、表示名の字 (段階 4 の補助フォントで出るか)、Touch の押し分け、16 人での通知の間隔。T1 の「跳び」の失敗は、走りの歩幅をハーネスが誤って数えたもので、判定をゲーム時間の間隔に直して確定した。

#### 破壊監査の反映 (2026-09-28、RC-06・RC-07。ClientSim で全通過。実測は段階 1 の同名の節)

- RC-06 (FUNC-02 BUG-02、承認後の速さの測定待ちで取り消せない): 取消できるかの判定を `KH_TravelMailbox._JoinCancelable()` (返事待ち・承認後の測定待ち・切符待ち・暗転中。本人が移動した後は含めない) と `KH_LeashManager._TravelCancelable()` に集め、手元メニューは `TravelCancelable()` 1 か所で実行ボタンの表示 ([取り消す] と残り秒数) と押したときの処理の両方を決める。相手の選び直しも同じ判定で止める。
- RC-07 (FUNC-02 BUG-03・BUG-04、FUNC-07 BUG-01、拒否の履歴の循環上書き): 「この人からは受けない」は、同じ人 → 空き枠 → 退室した人の枠の順に使い、在室中の人の記録は上書きしない。「同じ相手へ 30 秒」は 16 枠 (人数の上限) にし、同じ人 → 空き枠 → 期限切れか退室した人の枠の順。どちらも足りなければ 16 枠ずつ伸ばし、退室した人の枠は `OnPlayerLeft` でも空ける。2 つの寿命は分けたまま。再入室した人を同じ人として覚える仕組みは入れない (playerId は使い回されない)。
- 変更: `KH_TravelMailbox.cs`、`KD_TreatPouch.Travel.cs`、ハーネス (上の段階 1 のケース一覧の T9 速い承認の取消・T9 履歴)。

#### 既知制限 / 再開条件 (2026-09-28 破壊監査のトリアージ)

| RC | 元の BUG | 影響 | 見送りの理由 | 再開する条件 |
|---|---|---|---|---|
| RC-04 | FUNC-01 BUG-04・FUNC-02 BUG-01 (A → 実害は B) | 別々の合流が同時に成立すると、到着者どうしの 1 m の制限を破って近くに着く | 段階 1 の表と同じ (着地点の予約は導入しない) | 段階 1 の表と同じ |
| RC-08 | FUNC-02 BUG-05 (B、実機確認後) | 受け手の端末で申し込んだ人の受付箱だけがまだ作られていないと、承認を送れないまま通知だけ閉じる (申し込んだ人は時間切れになる) | VRCPlayerObject の生成順は実通信でしか確かめられない。直すなら、箱が取れるまで返事を期限内だけ保持し、取消・退出・期限切れで捨て、遅れた承認が終わった申し込みを復活させないよう seq を照合する | 実機 (VER-WLD-001 または PCVR 2〜3 人) で、入室直後の人への承認が届かない例を確かめたとき |

### 段階 4: 積み残し — 表示名の補助フォント (設計書 §A.6。2026-09-27 ユーザー決定「初期公開にこれ全て入れる」)

- 表示名を出すラベル (人の一覧 16 行・通知板の名前) に限って、共通フォントと同じ書体の **Static** フォント `KH_DisplayName SDF` を使う。元のフォントはプロジェクト内の `NotoSerifJP-Regular.ttf` (OFL)。Noto Serif JP に無い Latin 拡張 A (Ł など) は、TMP 付属の LiberationSans から作る小さな Static の fallback `KH_DisplayName Latin SDF` (512²) で出す。OS のフォントには頼らない。
- 文字集合は `scripts/kh-gen-name-charset.js` が作る `Font/KH_DisplayName_Charset.txt` (3,680 字: ASCII 94・Latin-1 94・Latin 拡張 A 128・ひらがな 90・カタカナ 156 (半角を含む)・全角英数記号 94・JIS 第 1 水準 2,965 (Node の EUC-JP で 16〜47 区を引く)・記号 59)。**対象外**: 簡体字の多く・ハングル・絵文字・JIS 第 2 水準以降 (□ で出る。記録だけ)。
- 大きさ: サンプリング 18 pt・余白 2・SDFAA・Alpha8、本体 1024×2048 (1 マス約 22 px で約 4,200 マス、使用率の見込み約 7 割)。最初の Static 版 (20 pt・余白 3・2048²) は Android 46.11 MB (+1.23) で目標を超えたので半分にした (2048² 版は残さない)。焼いた後は元フォントの参照を実行時用・Editor 用とも外す。
- 方式の変更 (2026-09-27): 最初の版は Dynamic (512²・Multi Atlas・実行時に焼く) だったが、Dynamic は元の TTF (7.33 MiB) をビルドに同梱するため **Android が +3.53 MB** (44.88 → 48.41 MB) になった。Static で焼いてから元フォントの参照を外す作法 (Course/27 と同じ) に替え、TTF をビルドに入れない。

完了条件:

- [x] Course/81 で、対象集合の字の欠字が 0 (見本: 英・日・記号は欠字 0、中 (簡体字を含む)・ハングル・絵文字は対象外の字を記録だけ)。フォント 2 本が Static・元フォントの参照なし (実行時用・Editor 用とも)・アトラス 1 枚でフォント内の Alpha8 (取り込み設定が無いので全プラットフォーム共通)・大きさ・fallback の配線、ラベル 17 本の配線が PASS。シーンが使う TMP フォント全部 (fallback を辿る) と部品の直の Font 参照から、ビルドに入る TTF の参照が 0 (`nameFontBuildRefs`)。アトラスのバイト数 (幅 × 高さ、Alpha8) と使用率を `travel-verify.json` に出す (`nameFontAtlasBytes`・`nameFontUsage`)。
- [x] Build Size (Android / iOS) で、Dynamic を入れる前 (Android 44.88 MB) からの**圧縮後の増分 ≤ 1.0 MB** (Android ≤ 45.88 MB)。内訳は Build Size/5 (Android)・5b (iOS) の `kh-fonts` 行。超えるなら漢字を頻出 1,500 字 (教育漢字 + 人名用の頻出) に絞る (一覧の出典は別途決める)。
- [ ] PCVR と Quest / iOS で、日本語・英語以外 (欧文の記号付き文字) を含む表示名が人の一覧と通知板で読めるか (VER-WLD-001)。

#### 実装 (2026-09-27、段階 4。Static 1024×2048 版で確定。PCVR・端末は未確認)

- 変更: 新規 `scripts/kh-gen-name-charset.js` と生成物 `Font/KH_DisplayName_Charset.txt`、新規 `Editor/KH_CourseTravelNameFontEditor.cs` (`Course/86 - Build Display Name Font`: 2 本のフォントを消して作り直し、Dynamic の間に TryAddCharacters で焼いて Static へ戻し、ラベルへ配線してシーンを保存。`Temp/imp-wld-001/name-font-build.json` に焼けた字数・欠字。81 の検査)、`Editor/KH_CourseTravelEditor.cs` (81 から呼ぶ・容量と使用率の欄)、`Editor/KH_CourseTravelJoinEditor.cs` (85 を流し直しても配線を戻す)、`Imported/.../KD_BuildSizeEditor.cs` (`Build Size/5b - Group Cost iOS` を追加)。
- 測定 (2026-09-27、20 pt・2048² 版): 86 BUILT (字形 3,582 + 98、欠字 0、ラベル 17)。Android 46.11 MB (+1.23)、iOS 45.29 MB (+1.22)。iOS の 47.60 MB は `bundle-sizes.txt` の一つ前の行 (Dynamic 版) で、Static 版の iOS は Android と同じ増分。
- 設計からの変更: 設計書 §A.6 の「共通フォントの TMP fallback」ではなく、表示名のラベルだけのフォントを差し替えた (共通フォントの字形の検査と見え方を変えないため)。Dynamic 版の記録: 81 で Ł (Noto Serif JP に無い) と「张伟」(簡体字) が欠字、Android +3.53 MB。これを受けて上の Static 版へ替えた。
- 確定版の実測 (2026-09-27、18 pt・余白 2・1024×2048 + Latin 512²): 86 BUILT (3,582 + 98、欠字 0)、81 TRAVEL_OK (アトラス 2,359,296 B、使用率 75.2 %、TTF の参照 0 / TMP フォント 3 本)。Build Size は Android 45.86 MB (44.88 から +0.98)、iOS 45.04 MB (44.07 から +0.97) で、どちらも目標 +1.0 MB の内側 (`bundle-sizes.txt` の最終 2 行)。
- 判断 (2026-09-27、統括): この版で確定。頻出 1,500 字への絞り込みは、表示名の欠字 (□) を増やすので採らない。

#### 既知制限 / 再開条件 (2026-09-28 破壊監査のトリアージ)

| RC | 元の BUG | 影響 | 見送りの理由 | 再開する条件 |
|---|---|---|---|---|
| RC-23 | FUNC-07 BUG-04 (B) | 一覧で読めた表示名 (例: 「斎藤」) が、選んだ後の固定フッター (「◯◯さんへ合流」) では共通フォントなので欠字になる | フッターは案内文と表示名が混ざる行で、補助フォントへ替えると各言語の字形・字幅・収まりを確かめ直す必要がある。Udon で font を差し替える方法は使わない | フッターも Editor で補助フォントへ配線して 81 の検査に足すとき (13 言語の収まりを 81 で測り直す) |

### 端末と人数

- [ ] Quest 3 単体と iPhone 15 Pro で、Touch の押し分け・通知板と Focus View の重なり・暗転の見え方・表示名の字を確かめる。16 人で申し込みが重なったときの通知の間隔を確かめる。どちらも [VER-WLD-001](VER-WLD-001-quest-ios-devices.md) の実機の回と、体験会の形式の人数の回で行う。

## 関連

- [DES-WLD-003](DES-WLD-003-map-and-travel.md): 設計と決定。
- [DES-DOG-001](DES-DOG-001-leash-walking.md): リードの契約。
- [DES-UI-004](DES-UI-004-walk-destinations-and-boards.md): 地点の呼び名。
- [IMP-ENV-004](IMP-ENV-004-river-loop-course.md): 周回路のノード。地点 ID はここから引き直す。
- [VER-WLD-001](VER-WLD-001-quest-ios-devices.md): 端末での確認。

2026-10-02 ユーザー確認: PCVR 1 人の項目は「基本全部見たから OK」(総合判断。項目別の結果の記録なし。[play-checklist-2026-10-02](../../testing/play-checklist-2026-10-02.md) の A 節)。残り: 2〜3 人、Quest/iOS、16 人。

2026-10-04: 通知板の [この人からは受けない] の文言を「このインスタンスにいる間は、この人から受けない」から「この人は退室まで受けない」へ短くした (ユーザー決定。保存はせず滞在中だけ効く、という動きは変えていない。[動画カメラの設計書 §14.4](../../plans/2026-10-03-video-camera.md))。ja の 1 行だけで、ほかの 12 言語は変更なし。Unity での枠の検査は回していない。
