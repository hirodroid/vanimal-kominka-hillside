---
id: IMP-UI-009
type: IMP
system: UI
status: review
priority: Medium
summary: メニューの撮影カメラに PC のみ利用できることを明記する
next: ユーザーの Play で写真タブの入口「撮影カメラ (PCのみ)」の表示 (13 言語の枠内の収まり) を確認。Android / iOS で入口が出ないことと撮影相手の許可は既存のまま (未変更)
updated: 2026-10-14
---

# IMP-UI-009 メニューの撮影カメラに PC のみの表記を追加

## 依頼 (2026-10-14)

メニューの「撮影カメラ」に PC のみの表記がなければ追加する。既存の UI Issue に該当する対応がなかったため、新規に起票した。

## 起票時の確認

- `Assets/KominkaHillside/Imported/KominkaDogRun/Data/UITranslations.json` の `video.open` は「撮影カメラ」で、PC のみの注記がない。
- `Assets/KominkaHillside/Scripts/KH_VideoCamera.cs` の `WordTitle` も「撮影カメラ」。メニュー入口の生成は `KH_VideoCameraEditor.cs` の `BuildEntry` で同じ翻訳キーを使う。
- 撮影カメラ自体は PC 専用で、Android / iOS では入口を非表示にする既存実装がある。今回の依頼は、その対応端末を文言でも伝える改善。
- ソースと翻訳データを確認した段階。Unity 上の現行表示・実機表示は未確認。

## 作業範囲

- メニューの撮影カメラ入口に「PCのみ」を明記する。文言例は **「撮影カメラ（PCのみ）」**。既存レイアウトに合わせた補助表記でもよいが、入口を見て分かること。
- 対応言語の翻訳、フォールバック文言、シーンに保存される表示を揃え、文字の欠け・枠からのはみ出しを確認する。
- 共通の翻訳キーを他の表示でも使っている場合は、注記を追加する範囲を確認して反映する。
- PC 専用なのはカメラを使う側。Quest・スマホの参加者も被写体になれ、撮影相手の許可・取消は全端末対応という既存仕様を維持する。

## 完了条件

- [x] メニュー入口に、撮影カメラを使えるのは PC のみであることが明記されている (2026-10-14 実装。Edit で確認、Play は未確認)。
- [x] 対応言語で意味が揃い (13 言語、既存の「(PCのみ)」表記に倣う)、メニュー内で注記を読める (枠内の収まりは Play で確認)。
- [x] Android / iOS で入口を隠す既存の挙動と、撮影相手の全端末での許可・取消を維持している (コード未変更。Video/2・Video/5 PASS)。

## 関連

- [IMP-WLD-008](IMP-WLD-008-dog-follow-video-camera.md): 撮影カメラ本体と対応端末の仕様。
- [DES-UI-007](DES-UI-007-cart-join-guidance.md): カート看板側の撮影モニター案内。メニュー入口の本件とは別に管理する。

## 起票時の状態

表記の追加を記録した段階。文言・コード・Unity シーンの変更は未着手。

## 記録

2026-10-14: 実装 (統括 Fable)。モニターの題 `video.open` は共有キーなので変えず、入口専用のキー `video.open.entry` を追加 (ja「撮影カメラ (PCのみ)」、他 12 言語は `KD_DistanceFadeSettings.EntryButton` の「(PC only)」相当の既存表記をそのまま付けた)。`KH_VideoCamera.WordEntry / KeyEntry`、`KH_VideoCameraEditor.BuildEntry` と `Lang.cs` の W5 表 (106 → 107 キー)・静的ラベルの束縛を入口だけ新キーへ。検証: C# 0 / U# 0 → Menu/11 → Menu/22 PASS → Video/1 (39 labels、keys 941) → Video/2 PASS (W5 107 キー × 13 言語) → Video/5 PASS → 保存。未確認: Play での表示と枠内の収まり (13 言語)。
