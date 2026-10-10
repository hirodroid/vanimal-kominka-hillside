---
id: IMP-WLD-002
type: IMP
system: WLD
status: review
priority: Medium
summary: 在室の 1 人を呼ぶ ([来てもらう]。呼ばれた人が [行く] を押したときだけ自分で呼んだ人の近くへ移動し、接続犬も同行する) を、合流の受付箱の交渉を流用して実装する
next: "実装と ClientSim は済み (82c 29/29、82・82b の回帰なし、3c307f8)。確認待ち: PCVR 2〜3 人 (設計書 §F.7 #10〜#15、濡れた犬の同行を含む。チェックリスト B46〜B51) と Touch (E16)。AI: C7-wet の通し方を直した (2026-10-11、岸の先の浅い水際に置き、本体が書いた濡れを切符まで保つ)。コンパイル確認と Course/82c (C7-wet を含む群) の実施は未了で、回すかは相談のうえ統括が決める"
updated: 2026-10-11
---

# IMP-WLD-002 プレイヤーを呼ぶ ([来てもらう])

2026-09-29 ユーザー要望「合流だけじゃなくて、呼ぶのも欲しい」。**1 人を呼ぶ**と決めた。仕様の正本は [設計書 §F](../../plans/2026-09-27-map-and-travel.md#f-呼ぶ-2026-09-29-ユーザー要望) で、本 Issue は段階と完了の条件だけを持つ。設計の親は [DES-WLD-003](DES-WLD-003-map-and-travel.md)、合流の実装は [IMP-WLD-001](IMP-WLD-001-map-warp-and-join.md)。

決定: 2026-09-29 (ユーザー。1 人を呼ぶ。呼ばれた人が自分で移動し、誰も他人に動かされない)

決定: 2026-09-29 (Codex 相談。設計書 §F 第 2 版: 入口は `ReqCall(int)` を別に足す、呼ぶの返事は準備中 (4) → 到着 (1) / 中止 (5) の 2 段、承認は (送り主, seq, 種類, 世代) に固定、交差は確定優先 → playerId の小さい方、30 秒は拒否だけ、受付 OFF は新規だけ止める)

## 要点 (正本は設計書 §F)

- R が [人] の一覧で T を選び、フッターの [来てもらう] を押す。T の通知板に「○○さんが呼んでいます [行く] [今は行かない]」。T が [行く] を押し、T の端末で移動を始められたときだけ T が自分で移動する。T の接続犬は合流と同じ切符で同行する。R は動かない。
- ネットワークから呼べるのは `ReqJoin(int)` / `ReqCall(int)` / `AnsJoin(int, int)` / `CancelJoin(int)` の 4 つ。同期変数は `accepting` 1 本のまま。受け付けの設定は「合流・呼び出しを受ける」の 1 つ。

## 段階と完了条件

- [x] Codex 相談 (`vrc-review` 相談モード)。指摘は設計書 §F 第 2 版に反映した。
- [x] 実装 (下の「実装 (2026-09-29)」)。C#・翻訳表・ハーネスまで。シーンの再生成は統括。
- [x] コンパイルのエラーが 0 (`unity-compile.sh --udon`)。Course/85 → Course/27 → Course/81 が `TRAVEL_OK` (新しい 2 つのボタン・通知板のボタン 2 つと理由の行の配線とイベント、13 言語の収まり、ネットワーク入口の名前と署名、試験用の値が 0)。 — 2026-09-29 (3c307f8)
- [x] ClientSim: Course/82c が `TRAVEL_CALL_PASS` (設計書 §F.6 の期待表。C7-wet は除く)。同じ版で Course/82 (`TRAVEL_OBSERVED`)・82b (`TRAVEL_RC_OBSERVED`) が通る。 — 2026-09-29 82c C1〜C11 29/29 PASS、82 (22)・82b (8) 回帰なし (3c307f8)
- [x] C7-wet (陸で接続した犬が濡れてから [行く]): 到達手順を Edit で確かめてから 82c に足す。 — 2026-09-29 `19db81d`。ケースは足したが**未実施 (NOT_RUN)**。接続中の犬は NavMesh の追従でしか水へ近づけず (泳ぎに入る経路は無い)、Edit モードの存在の走査 (本体と同じ深さ 0.02 m、格子 87,023 点 + NavMesh 三角形 1,953 枚・約 1,611 万点) で濡れ判定に入る NavMesh 上の点が 0。82c は 通過 29 / 未実施 1 / 失敗 0 (run 20260929-121534)。この走査条件での結果で、検証済みではない。濡れた犬の同行は PCVR で見る。地形か NavMesh を変えたら探索が地点を見つけてケースが実施される
- [ ] C7-wet を 2026-10-11 の通し方 (下の「C7-wet の通し方の修正」) で実施する: `unity-compile.sh` のエラー 0 → Course/82c で C7-wet が PASS (未実施 0)。回すかは相談のうえ統括が決める
- [ ] PCVR 2〜3 人: 設計書 §F.7 の #10〜#15 (ユーザー)。
- [ ] Quest / iOS の Touch で 2 つのボタンと通知板の [行く] [今は行かない] を押し分けられる ([VER-WLD-001](VER-WLD-001-quest-ios-devices.md) の回)。

## 実装 (2026-09-29)

- `Assets/KominkaHillside/Scripts/KH_TravelMailbox.cs`: `ReqCall` と共通の `Receive` / `ReceiveCancel`、送り主ごとの見た最大の seq、世代と 0.5 秒、種類の差し替え、交差 (確定優先 → playerId、`debugCrossInvert`)、R の 2 段の期限 (返事 22 秒・到着 20 秒、無応答は `CallUnknown`)、T の [行く] (測定 → 着地点 → `_TravelBeginJoin` → code 4、Manager の結果で code 1 / 5)、`CancelJoin` の照合先に「呼ばれて移動中」を追加、試験用の入口 (`debugIncomingFrom/Kind/Seq`・`_DebugIncomingNow`・`_DebugIncomingCancelNow`・`debugReply*`)。合流の既存の動き (承認・測定・30 秒・T9/T10/T12) は変えない。
- `Assets/KominkaHillside/Scripts/KH_TravelNotice.cs`: 種類ごとのボタン実体 (`joinButtons` / `callButtons`)、世代つきの押下 (`_Yes/_No/_Go/_NotNow`)、呼ぶの題と犬の数、理由の行。
- `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatPouch.Travel.cs`: [人] のフッターの [合流をたのむ] / [来てもらう] (`_TravelCall`)、取消は送った側のボタンだけ、返事待ちの行の 2 行目、結果の文言、印「呼ばれています」。
- `Assets/KominkaHillside/Scripts/Editor/KH_CourseTravelJoinEditor.cs`: Course/85 の生成・配線 (`JoinAsk` / `JoinCall`、`Button4` / `Button5`、`Reason`、自動縮小) と Course/81 の検査 (配線・イベント・装飾の Raycast・Canvas の条件・ネットワーク入口・試験用の値・13 言語の収まり)。
- `Assets/KominkaHillside/Scripts/Editor/KH_CourseTravelEditor.cs`: ビルドの門番に新しい試験用の値。
- `Assets/KominkaHillside/Data/Environment/Course/CourseUITranslations.json`: `course.travel.call.*` 13 キーと設定の文言 2 つ × 13 言語。
- `Assets/KominkaHillside/Scripts/Editor/Testing/KH_CourseWalkHarnessEditor.Call.cs` (新) ほか: Course/82c (C0 準備・R 側 4・T 側 C1〜C8・C9 片付け)。`[KH_Join]` のログも取り込む。[人] の実行ボタンの表示は [合流をたのむ] から読む (82・82b の T9 の表示の比較)。

## C7-wet の通し方の修正 (2026-10-11)

**原因** (コードの読み。Unity・ハーネスは動かしていない): 「濡れ判定に入る NavMesh 上の点」は焼き方の決まりで空集合になる。地形のせいではないので、地形を変えても見つからない。
- 犬の濡れ (`waterPose` 1 / 2) は根の位置だけで決まる (`KD_DogWanderController.HillsideWater.cs` の `WaterPoseRefresh` → `WaterWetAt`)。水遊び場の多角形の外は「川の帯 (半幅 + `BandMargin` 1 m) の中、かつ深さ > 0.02 m」。上がった後に残る濡れのフラグやタイマーは無い (`WaterShake` はリードの無い水遊びだけ。`KH_DogWash._wet` は洗いの毛の濡れで別のもの)。
- 川の帯は NavMesh の川床の除外と同じ範囲 (`KH_RiverData.cs` の `BandMargin = 1f; // = NavMesh riverbed exclusion`、`KH_RiverNavigationEditor.cs` の `RiverbedNotWalkable`、`KH_CourseNavigationEditor.cs` の「Riverbed walkable」検査)。
- 接続中の犬は NavMesh の上しか動かない (`LeashGo` / `TeleportTo` / 切符の置き場所)。泳ぎを立てる水遊び・Fetch・ディスクは接続中には走らない (`HillsideWater.cs` の C1-2 の注記「Leashed walks run on the NavMesh (the riverbed is excluded)」)。
- 旧版 (`a06178c`。上の記録の `19db81d` は別のコミット (Course/99 の BGM) で、C7-wet を足したのは `a06178c`) は、この空集合を探していた。run 20260929-121534 の 0 件 (格子 87,023 点 + 三角形 1,953 枚・約 1,611 万点) はその結果。

**変更** (`Assets/KominkaHillside/Scripts/Editor/Testing/KH_CourseWalkHarnessEditor.Call.cs` だけ。本体のコードは変えていない):
- Edit モードの探索 `CwSearch` を置き換えた。T の立ち位置は、川の Fetch が実際に使う出入り口 (ok な岸のスライス `KH_RiverData.sliceBank`。Course/90 で焼き 91 で検査済み。NavMesh の上・帯の外・橋の区域の外) のうち、乾いていて最寄りの周回路ノードから完全な経路があるもの。犬 0 を置く点は、その岸から川の中心へ 0.1 m ずつ進んで最初に深さ > 0.08 m になる点 (規則は 0.02 m。置いた直後にリードが数 cm 歩かせても濡れたままでいる余裕)。0.3 m より深い・水遊び場の中のスライスは使わない。浅い順 → 岸に近い順 → コード順に選ぶ。存在の走査 (格子と三角形) は外した。
- Play: 犬 0 (陸で接続済み) をその点に置き、**本体の規則が `waterPose` を書く**のを待つ (ハーネスは `waterPose` を書かない)。犬の判断は間引かれていて (IMP-DOG-034 / 036)、`waterPose` の再計算は判断のときだけ、間のフレームは脚が前の経路の角へ歩く。そのため最初の再計算までは、ハーネスの各回で同じ点へ置き直す (押す前の `CallMark` より前なので、位置の不連続の数には入らない。置いた回数を notes に書く)。その値を `_debugPoseHold` (Course/78 W4-leash-stale・WashDirt と同じ作法) で保ってから [行く] を押す。保つ理由: リードは帯の中の犬を NavMesh へ歩かせて 1 秒足らずで乾かすので、保たないと暗転の間に濡れが消える。保持は犬 0 が切符で動いたのを観測した時点で外す (観測より先に流れが終わったら直後に外す。C11 の片付けでも外す)。その後の `waterPose` 0 は本体の再計算が書く。
- 合格条件は弱めていない。従来の条件 (C1 の一式、犬 0 が切符で動く直前まで濡れていた、水の拒否 `_waterLeashVeto` が立たない、着いた後の犬 0〜2 の `waterPose` 0・`_waterSwimming` / `_swimYValid` false・`waterState` 0・接続が残る・地面から 0.2 m 以内) に「保持が外れている」を足した。地点が見つからないときは NOT_RUN ではなく FAIL にした。
- 模擬は 2 つあり、notes に書く: 置き方 (接続中の犬はそこへ歩いては入れない) と保持。泳ぎの高さの取り残しは 82b の T-swimresidue-* が受け持つ。接続中の犬は泳ぎに入れないので、このケースで見る濡れは `waterPose` 1 (足が濡れた状態)。
- 設計書 §F.4 の行「陸で接続した犬が、後で濡れた (泳いでいる)」と §F.6 の C7-wet の行 (「82c の初回には入れない」) は今の作りと合っていない。直すかは統括が決める (本 Issue では設計書を変えていない)。

**未了**: コンパイル確認 (`unity-compile.sh`) と Course/82c の実施。どちらもしていない。82c を回す前に、プロジェクトの決まりどおり相談する (`vrc-review` 相談モード)。回すかどうかと、いつ回すかは統括が決める。回すなら群は 82c だけ。82b (T-swimresidue-*) が使う Call.cs の部品 (`CwPose` / `CwDogState` / `CwGroundGap`) は変えていないので、82・82b を回帰に含めるかは相談で決める。

決定: 2026-10-11 (C7-wet は岸の先の浅い水際に置き、本体が書いた濡れを `_debugPoseHold` で切符まで保つ。ハーネスの変更だけで、コミットは統括)

## 関連

- [DES-WLD-003](DES-WLD-003-map-and-travel.md): 設計の親。
- [IMP-WLD-001](IMP-WLD-001-map-warp-and-join.md): 合流・受付箱・通知板・切符の実装 (本 Issue はその上に足す)。
- [VER-WLD-001](VER-WLD-001-quest-ios-devices.md): 端末での確認。
