---
id: IMP-WLD-002
type: IMP
system: WLD
status: review
priority: Medium
summary: 在室の 1 人を呼ぶ ([来てもらう]。呼ばれた人が [行く] を押したときだけ自分で呼んだ人の近くへ移動し、接続犬も同行する) を、合流の受付箱の交渉を流用して実装する
next: "実装と ClientSim は済み (82c 29/29、82・82b の回帰なし、3c307f8)。確認待ち: PCVR 2〜3 人 (設計書 §F.7 #10〜#15、濡れた犬の同行を含む。チェックリスト B46〜B51) と Touch (E16)。AI: C7-wet は現行の地形で濡れ判定に入る NavMesh 上の点が 0 のため 82c で未実施 (19db81d)。通し方を直す"
updated: 2026-10-03
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

## 関連

- [DES-WLD-003](DES-WLD-003-map-and-travel.md): 設計の親。
- [IMP-WLD-001](IMP-WLD-001-map-warp-and-join.md): 合流・受付箱・通知板・切符の実装 (本 Issue はその上に足す)。
- [VER-WLD-001](VER-WLD-001-quest-ios-devices.md): 端末での確認。
