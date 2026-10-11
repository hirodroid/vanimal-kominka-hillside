---
id: IMP-UI-005
type: IMP
system: UI
status: todo
priority: High
summary: 13言語の枠だけでなく、散歩・追加画面を含む全UIの翻訳を揃える
next: 実装着手時に現行コードと仕様の差を整理する。今回はIssueと資料の更新のみ
updated: 2026-10-08
---

# IMP-UI-005 — 13言語の未訳と翻訳表外のUIを解消する

2026-10-08 ユーザー依頼。今回はIssueと資料の更新のみ。Unityへ接続せず、コード・翻訳データ・シーンは変更していない。実装・動作確認は未実施。

## 確認した問題

比較資料の採取時点では主表779キー×13言語。今回の起票時の再読では883キー×13言語へ増えているが、散歩の43キー（KD_TreatPouch.WordWalk*）は追加11言語すべてでenと同文のまま。例: WordWalkTab=Walk、WordWalkTitle=Walk with the dogs。空欄なしを翻訳完了として扱えない。

動画UIは[IMP-WLD-008](IMP-WLD-008-dog-follow-video-camera.md)で別途作業中。以前の日本語定数の記録を現在の未実装件数と混同せず、着手時に最新の変更を読み直す。本票は散歩を含む全表示面の翻訳網羅・未訳検査を扱い、動画側の変更を重複実装しない。

## 要件

- 必須13コード: ja / en / ru / uk / ko / de / zh-cn / es / fr / pt-br / it / pl / zh-hk。繁体字の別名と初期言語・fallback・個人保存は既存方針を保つ。
- 主表・周回表に加え、設定、案内、写真・動画、散歩・洗浄・呼ぶ、状態・エラー・入力別案内、Interact/Pickupを対象にする。
- 英語と同文の項目は候補として列挙し、通常の文章・操作名は実訳にする。固有名・記号・単位などの正当な同文は理由を記録する。
- 共通操作の意味と文言はシリーズで揃える。機能・品揃え・タブ数の違いは許容する。

## 完了条件

- [ ] 最新データから未訳・表外表示・動的表示の一覧を作り、既存動画Issueとの分担を確定する。
- [ ] 散歩43キーを含む通常文言が13言語で翻訳され、欠落・英語埋め戻し・表外追加を検知する検査を用意する。
- [ ] 全画面の切替・保存復元・再表示と、端末別の欠字・長文の収まりを確認する。未確認端末は明記する。
- [ ] 新機能の追加でも同じ検査を通す運用を記録する。カタログだけの成功を実機合格としない。

## 関連

- [UI仕様](../../world-ui-spec.md)
- [共有の継承契約](C:/work/unity/vrc-world-agent/docs/development/issues/IMP-SK-002-animal-menu-localization-contract.md)
- [比較HTML（採取時点の証拠）](C:/work/unity/vcc/A_Cove_to_Unwind/SourceArt/Cove/review/menu-comparison-20261008/index.html)
