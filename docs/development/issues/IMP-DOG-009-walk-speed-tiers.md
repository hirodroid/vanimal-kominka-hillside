---
id: IMP-DOG-009
type: IMP
system: DOG
status: wip
priority: High
summary: 散歩の犬の速さを 4 段階に (最速は現状の速歩の 3 倍)。人が犬を引っ張る状態をなくす。Run クリップの再生速度・減速も合わせる。9/22 PCVR 1 人 1 匹で OK・採用、3〜6 匹・2 人以上・Desktop は未確認
---

# IMP-DOG-009 散歩の犬の速さを 4 段階にし、人が引っ張る状態をなくす

2026-09-21 の PCVR 所感。散歩中の犬の足の速さは「ゆっくり」と「ちょっと速い」の 2 段階しかなく、本物の犬は人よりずっと速いのに、今は人が犬を引っ張る形になってしまう。**あと 2 段階足し、いちばん速い段階は今の速い方の 3 倍程度**にする (ユーザー指示: 「めっちゃ速いでいい」)。中間の段階も合わせて作る。自由な散歩の全体は [IMP-DOG-003](IMP-DOG-003-walking-polish.md)、仕様の正本は [game-design §4](../../game-design.md)。

## 現状

| 段階 | 値 | 使われる場面 |
|---|---|---|
| 歩き `walkSpeed` | 1.1 m/s | 通過点へ先行、寄り道、手まわりの追従 ([HillsideLeash.cs:218](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.HillsideLeash.cs#L218)) |
| 速歩 `runSpeed` | 2.4 m/s | 人より後ろから追い越す、戻る、誘いで手前へ戻る (×0.8) |
| (復帰専用) `followSpeed` | 4.8 m/s | 手から 1.6×リード長以上離れたときの戻り、道外追従の遠距離 ([KH_LeashManager.cs:38](../../../Assets/KominkaHillside/Scripts/KH_LeashManager.cs#L38)) |

- 既定値は `KD_DogWanderController.cs` の `DefaultRunSpeed` 2.4 / `DefaultWalkSpeed` 1.1。古民家から移植したままの値で、ドッグランの広さに合わせたもの。
- VRChat の人の移動は既定で歩き 2 m/s・走り 4 m/s。**犬の「速い」2.4 m/s は人の歩きと同程度**なので、人が普通に歩くだけで犬は追い越せず、走れば置き去りになる。
- 移動アニメは `ApplyMoveAnim` で Walk / Run の Bool を切り替えるだけで、再生速度は実速度に追従しない ([Shared.cs:334](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.Shared.cs#L334))。フリスビーの追走 (10 m/s) だけは `KH_FrisbeeCatch.ApplyAnimatorSpeed` で `_speed / runSpeed` を `maxRunAnimScale` まで掛けている ([KH_FrisbeeCatch.cs:1175](../../../Assets/KominkaHillside/Scripts/KH_FrisbeeCatch.cs#L1782))。

## 目的と範囲

- 散歩中の速さを **歩き / 速歩 / 走り / 全力** の 4 段階にする。初期値の案: 歩き 1.1 (現状) / 速歩 2.4 (現状) / 走り 約 4.5 / 全力 約 7.2 (現状の速歩の 3 倍)。値は Play で決め、game-design §4 の実装値表へ反映する。
- どの場面でどの段階を使うかを決める。案:
  - 走り: 人より後ろからの追い越し、リード端まで離れたときの戻り、誘いで手前へ戻る。人が走っている (手の移動速度が歩き超) ときの先行。
  - 全力: 手から 1.6×リード長以上の復帰 (現行 `followSpeed` の置き換え)、到着地点へ最後にダッシュ、将来の犬同士の遊びの追いかけ。
  - 歩き / 速歩の用途は現状維持。「人が止まっている・ゆっくり歩いている」ときまで速くしない。
- 速い段階でも見た目と動きが破綻しないようにする。
  - Run クリップの再生速度を実速度に合わせて上げる (フリスビーの `ApplyAnimatorSpeed` と同じ方針で `KD_DogWanderController` 側にも入れる。上限倍率を持つ)。
  - 減速距離 (`brakeSpeed = √(2ad)`) と旋回速度 `turnSpeedDegPerSec` を段階に合わせ、目標を通り過ぎたり急停止したりしない。通過点の到着半径 (途中 2.2 m) で曲がり角を飛ばさない。
  - 寄り道の「紐が 0.9× で中断」、リード端で止まって人を向く動きが、速い段階でも紐を引きちぎるように見えない。
- 同期は増やさない。位置は VRCObjectSync のまま、速度段階は Owner のローカル判断。他人の画面で 7 m/s の補間が飛んで見えないかは確認対象。
- 範囲外: 人側の移動速度の変更、犬種ごとの速さの違い (必要なら別 Issue)、フリスビー追走の速度 (IMP-DOG-008)。

## 進め方

1. `runSpeed` の上に 2 段階 (仮に `dashSpeed` / `sprintSpeed`) を足し、`followSpeed` を全力へ統合するか判断する。既定値の未設定救済 (`<= 0.01f` で既定へ戻す) も同じ形で入れる。
2. 場面ごとの段階の割り当てを HillsideLeash.cs の `LeashGo` 呼び出しに入れる。人の移動速度の推定は既存の「1 s ごとの変位」を使い、新しい判定周期を増やさない。
3. Run クリップの再生速度スケールと減速・旋回の調整を入れ、Editor で Verify を通す (Walking 146/146 を維持)。
4. PCVR で 1 匹 → 3 匹 → 6 匹。人が歩く / 走る / 止まる / 引き返すの各場面で、犬が人を引っ張る側に回っているか、速すぎて怖くないか、紐の見え方を評価する。
5. 採用値を game-design §4 の実装値表と保存済みシーンへ整合させる。

## 試作の記録

- **2026-09-21 設計と Codex 設計レビュー (X/D/P) を反映、実装開始**: 値は `KH_LeashManager` に `dashSpeed` 4.5 / `sprintSpeed` 7.2 (`followSpeed` を統合、Build が所有)。人の速度は「1 s の変位」ではなく毎フレームの EMA (1 Hz の変位は静止判定用で速度にならないため)。人が走っても 1.25× の帰還と往復しないよう「先行保持」を追加、Run クリップは `Animator.speed` で追従、コーナーは身体の向きとの角度差で減速。設計値・指摘/対応・分担は [記録](../../plans/2026-09-21-walk-speed-and-stopovers.md)。Play は未。

- **2026-09-21 Wave 1 実装 (Editor 検査済み、Play 未)**: コミット 3a94a9f。Codex コードレビュー (D/M) の是正込み。Walking Verify 176/176 (以後 178/178)。Play チェックリストは記録の「Play チェックリスト」。

- **2026-09-22 PCVR Play (1 人 1 匹)**: ユーザー評価 OK。人が歩いても走っても犬が前へ出る、全力の見た目・曲がり角とも問題なし。試作値をそのまま採用値とする。3 匹・6 匹、2 人以上、Desktop は未確認。

## 完了条件

- [x] 散歩の速さが 4 段階になり、最速は現状の速歩の 3 倍程度で、場面ごとの割り当てを記録した
- [x] 人が普通に歩いても走っても、犬が人より前へ出られる (人が犬を引っ張る形にならない) と PCVR でユーザーが評価した
- [x] 速い段階で足が滑って見えない (Run クリップの再生速度が実速度に追従する)
- [x] 速い段階で目標の通り過ぎ・急停止・曲がり角の飛ばし・崖側への逸脱が起きない
- [ ] 2 人で他人の犬の速い動きが飛んで見えないことを確認した (または残課題として記録した) — 2026-09-22 時点で未確認。3 匹・6 匹・Desktop も同様
- [x] 採用値を game-design §4、生成側、保存済みシーンへ整合させた
