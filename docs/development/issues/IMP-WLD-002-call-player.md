---
id: IMP-WLD-002
type: IMP
system: WLD
status: wip
priority: Medium
summary: 在室の 1 人を呼ぶ ([来てもらう]。呼ばれた人が [行く] を押したときだけ自分で呼んだ人の近くへ移動し、接続犬も同行する) を、合流の受付箱の交渉を流用して実装する
next: Codex 相談 → 実装
updated: 2026-09-29
---

# IMP-WLD-002 プレイヤーを呼ぶ ([来てもらう])

2026-09-29 ユーザー要望「合流だけじゃなくて、呼ぶのも欲しい」。**1 人を呼ぶ**と決めた。仕様の正本は [設計書 §F](../../plans/2026-09-27-map-and-travel.md#f-呼ぶ-2026-09-29-ユーザー要望) で、本 Issue は段階と完了の条件だけを持つ。設計の親は [DES-WLD-003](DES-WLD-003-map-and-travel.md)、合流の実装は [IMP-WLD-001](IMP-WLD-001-map-warp-and-join.md)。

決定: 2026-09-29 (ユーザー。1 人を呼ぶ。呼ばれた人が自分で移動し、誰も他人に動かされない)

## 要点 (正本は設計書 §F)

- R が [人] の一覧で T を選び、フッターの [来てもらう] を押す。T の通知板に「○○さんが呼んでいます [行く] [今は行かない]」。T が [行く] を押し、着地点の検査 (§D.3、基準は R) を通ったときだけ T が自分で移動する。T の接続犬は合流と同じ切符で同行する。
- 通信は `ReqJoin(int seq)` を `ReqJoin(int seq, int kind)` に置き換えるだけ。`AnsJoin` / `CancelJoin` と同期変数 (`accepting` 1 本) は変えない。受け付けの設定は「合流・呼び出しを受ける」の 1 つ。
- 同じ送り主から種類が変わったら差し替え、互いに申し込んだら playerId の小さい側だけ残す、T の移動中は R の取消が勝つ。

## 段階と完了条件

- [ ] Codex 相談 (`vrc-review` 相談モード): 設計書 §F の通信の変更・差し替え・交差の規則・T が暗転中に取り消したときの R の表示・水中の犬への切符。
- [ ] 実装: `KH_TravelMailbox.cs`・`KH_TravelNotice.cs`・`KD_TreatPouch.Travel.cs`・Course/85 (フッターの 2 つのボタン・通知板)・Course/81 の門番・文言 (13 言語と Course/27 の字形)。見積りは設計書 §F.8。
- [ ] コンパイルのエラーが 0。`_` の規約の検査を通す (ネットワークから呼べるのは `ReqJoin` / `AnsJoin` / `CancelJoin` の 3 つのまま)。Verify で、2 つのボタンと通知板の配線・文言の収まり・欠けた字 0・試験用の値が 0 / -1。
- [ ] ClientSim: Course/82c の C1〜C8 が PASS (設計書 §F.6)。既存の Course/82・82b が引き続き PASS。
- [ ] PCVR 2〜3 人: 設計書 §F.7 の #10〜#15 (ユーザー)。
- [ ] Quest / iOS の Touch で 2 つのボタンと通知板の [行く] [今は行かない] を押し分けられる ([VER-WLD-001](VER-WLD-001-quest-ios-devices.md) の回)。

## 関連

- [DES-WLD-003](DES-WLD-003-map-and-travel.md): 設計の親。
- [IMP-WLD-001](IMP-WLD-001-map-warp-and-join.md): 合流・受付箱・通知板・切符の実装 (本 Issue はその上に足す)。
- [VER-WLD-001](VER-WLD-001-quest-ios-devices.md): 端末での確認。
