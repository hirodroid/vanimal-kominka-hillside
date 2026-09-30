---
id: IMP-DOG-026
type: IMP
system: DOG
status: todo
priority: High
summary: 洗浄の状態と同期の仕上げ (セルの正本、清潔の判定、再洗浄、途中参加、Owner 交代、2 人の同時操作)
next: IMP-DOG-025 の試作を受けて、セルの正本 (KH_DogWash の Manual 同期) を作る
updated: 2026-09-30
---

# IMP-DOG-026 洗浄の状態と同期の仕上げ

親は [IMP-DOG-024](IMP-DOG-024-walk-and-wash-experience.md)。方式は [設計書](../../plans/2026-09-30-dog-wash-design.md) §1-4、§1-2 (復旧)、§6。依存は [IMP-DOG-025](IMP-DOG-025-wash-prototype-one-dog.md)。設計書の値は試作値。

## 範囲

やること。

- `KH_DogWash` (犬ごと、Manual 同期): UV 16×16 のセル (汚れ・泡・濡れ)、`epoch` / `appliedSeq`、`washingUntil`、`cleanSerial`
- Owner を犬のルートの Owner に揃える。操作のたびに Owner を奪わない
- ストロークの受信の検証 (ID・座標の範囲・量・送り主と道具の持ち主の関係)、`amount` を接触時間を含む量にする
- 清潔の判定 (メッシュの無いセルを分母から外す)、再洗浄での `cleanSerial` の再発火 (幅を持たせる)
- 途中参加・RenderTexture の作り直し・Owner 交代の時だけセルからマスクを塗り直す。通常の受信では塗り直さない
- 同じ犬への 2 人の同時操作、操作者の描画済み区間の二重塗りの防止

やらないこと。洗われ中の枝・門・リード接続中の扱いは [IMP-DOG-027](IMP-DOG-027-wash-behavior-switching.md)。満足の合図の演出は [IMP-DOG-028](IMP-DOG-028-wash-sound-and-reactions.md) (この Issue は `cleanSerial` のエッジまで)。汚れの発生は [IMP-DOG-029](IMP-DOG-029-dirt-accumulation.md)。6 匹への適用は [IMP-DOG-030](IMP-DOG-030-wash-six-dogs.md)。

## 完了条件

AI 側で確かめる。

- [ ] コンパイル完了とエラー 0。Editor の Verify で、セルの有効表と `KH_DogWash` の配線を確認できる
- [ ] ClientSim ハーネスで、ストロークからセルの値が変わり、清潔の判定が期待どおりに切り替わることを確認した (親: 清潔判定と見た目の対応)
- [ ] ハーネスで、清潔でも泡がつく・泡は汚れを変えない・水だけで汚れが落ちることを確認した (親: 自由な手順、再洗浄)
- [ ] ハーネスで、`cleanSerial` が閾値の近くの往復で連発せず、汚れか泡が戻ってから再びきれいになった時だけ進むことを確認した
- [ ] ハーネスで、途中参加の最初の受信・Owner 交代の時だけ塗り直しが起き、通常の受信では起きないことを確認した (模擬の範囲。実通信の合格にしない)
- [ ] 送信量 (Manual のバイト数、ストロークの頻度) を実行時に測って記録した (設計書 §3)

ユーザーが確かめる (2 人、PCVR)。

- [ ] 2 人で同じ犬を同時に洗い (泡付けとすすぎの分担を含む)、各自の表示が見て分かるほどずれない。犬の Owner でない人でも手応えが即時 (親: 2 人協力)
- [ ] 途中参加の人に、おおまかな見た目 (セルから復元) で洗った跡が見える。Owner 交代後も状態が保たれる
- [ ] 2 人の表示が一時的に違った時の見え方が許容できるかの判断 (設計書 §1-4 の制限)

## 記録

2026-09-30: 起票。実装は未着手。
