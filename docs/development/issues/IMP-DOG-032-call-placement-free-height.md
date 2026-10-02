---
id: IMP-DOG-032
type: IMP
system: DOG
status: todo
priority: Medium
summary: 場所を決めて呼ぶのマーカーを古民家どおり手の高さに追従させる (地面への吸着と斜面で置けない判定を外す)
next: KD_SleepPlacement / KD_SleepSlot / KD_DogWanderController.Sleep の IMP-DOG-016 変更 2・3 の該当部分を古民家へ戻し、Editor の Verify を追従させる
updated: 2026-09-30
decided: 2026-09-30
---

# IMP-DOG-032 場所を決めて呼ぶの高さを古民家どおり自由にする

「場所を決めて呼ぶ」のマーカーが地面に吸い付き、手を上げても高さが変わらない。膝や胸の上に置けない (2026-09-30 ユーザー指摘)。

原因は [IMP-DOG-016](IMP-DOG-016-call-pose-port-and-slope.md) の変更 2・3。斜面対策として AI が提案した仕様で、法線 0.9 は仮の値だった。古民家は DES-DOG-006 (2026-09-05 ユーザー決定) で「置いた位置・向き・高さのまま寝る。空中も可。近くまで歩いてから浮く」としている。

- 変更 2: マーカーを真下の地面へ落とし、地面から 0.1 m 以上離れた場所や法線 0.9 未満の場所では確定させない (`KD_SleepPlacement.UpdatePreview`、`KD_SleepSlot._PlacementGroundOk`)
- 変更 3: 寝床に収まる途中も犬を接地させる (`KD_DogWanderController.Sleep.cs` の `UpdatePlacedSleep` の `SnapToGround`)

## 決定

2026-09-30 ユーザー判断: **古民家と同じにする。** 高さは手 / 頭の前に追従して自由、向きは水平の yaw だけ (古民家と同じ)。**置けない場所は作らない** (斜面が気になるなら自分の上に乗せればよい)。手の傾きに追従させる案は変更が大きいので、ここではやらず [IMP-DOG-033](IMP-DOG-033-call-placement-tilt.md) へ分ける。

## 範囲

やること。

- `KD_SleepPlacement`: マーカーを古民家どおり手の前 0.15 m / 頭の前の距離にそのまま置く。赤い印 (`invalidMarks`)・「平らな場所が必要です」の文言・確定ボタンと長押しの抑止を外す
- `KD_SleepSlot`: `ValidPlacement` を古民家どおり NaN / Inf だけにする。`_PlacementGroundOk` と `Hillside*` 定数を外す
- `KD_DogWanderController.Sleep`: 寝床へ収まる途中の `SnapToGround` を外す (置いた高さへ浮く)。近づくまでの地形追従 (`SleepStep`) は残す
- Editor: `KD_SleepPlacementEditor` (Menu/6i の赤い印の生成と Verify)、`KD_SleepSetupEditor` (斜面の判定と `HillsideGroundMask` の Verify)、`KD_TreatSetupEditor.Verify` の NOTE を追従させる。シーンに残る赤い印の扱い (消すか残すか) を決める。文言 `WordSleepNeedFlat` は未使用になる
- 関連文書の変更 2・3 の記述を更新する ([IMP-DOG-016](IMP-DOG-016-call-pose-port-and-slope.md)、[計画書](../../plans/2026-09-23-menu-rebuild.md)、[kominka-dog-system](../imports/kominka-dog-system.md))

やらないこと。手の傾きに追従させる ([IMP-DOG-033](IMP-DOG-033-call-placement-tilt.md))。リード・集合・フリスビー中の除外 (IMP-DOG-016 変更 1) は変えない。

## 完了条件

AI 側で確かめる。

- [ ] コンパイル完了とエラー 0、Sleep 系の Verify が通る
- [ ] 3 つのスクリプトの呼ぶ部分が、変更 1 と近づき方 (`SleepStep`) を除いて古民家と同じになったことを差分で確認した

ユーザーが確かめる (Play / PCVR)。

- [ ] VR でマーカーが手の高さに追従し、膝・胸の上に置くと犬がそこへ浮いて寝る
- [ ] 斜面でも確定でき、赤い印や文言が出ない
- [ ] 呼び終わった犬が地面へ戻る

## 記録

2026-09-30: 起票 (ユーザー指摘と判断)。実装は未着手。

決定: 2026-10-03 (ユーザー決定) 初期公開に入れる。未決定: 本 Issue 内の設計判断は無い (起票時の方針のまま実装できる)。確認が要る点: IMP-DOG-016 の法線 0.9 の判定 (変更 2、10-03 に採用と決定) は本 Issue が古民家どおりに戻すと外れる。どちらを優先するかは実装前にユーザーへ確認する。

## 質問が未回答 (2026-10-03)

IMP-DOG-016 (斜面の法線 0.9 の判定と接地) と本 Issue (その判定を外し古民家どおり手の高さに追従) のどちらが勝つかは**未回答**。推奨は **032** (9/30 にユーザー自身が呼ぶ機能について決めた判断。斜面は自分の上に乗せるか傾けて置けば済む)。IMP-DOG-016 の next には「法線 0.9 は採用 (10-03 決定)」とあり、本 Issue の決定 (09-30) と食い違ったまま残っている。ユーザーの回答を待つ。設計案は [傾けて置く](../../plans/2026-10-03-call-placement-tilt.md) §7 の質問 1。
