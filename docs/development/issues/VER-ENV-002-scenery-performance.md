---
id: VER-ENV-002
type: VER
system: ENV
status: todo
priority: Medium
summary: 景観改善後に描画・メモリ・容量とQuest 3/iOSの負荷を調査し、眺めを保ちながら必要な調整を行う
next: IMP-ENV-001/009と小物の品質改善後、代表地点で追加物の寄与を計測する。変更前のユーザー報告と同条件の実測は区別する
updated: 2026-10-04
---

# VER-ENV-002 景観改善後の性能調査と調整

2026-10-04 ユーザー指定で [IMP-ENV-001](IMP-ENV-001-hillside-polish.md) の③と景観追加前後の負荷・容量比較を引き受ける。[IMP-ENV-009](IMP-ENV-009-course-roadside-scenery.md) と合わせて先に景観品質を改善する。

変更前のユーザー報告はQuest 3で通常40〜50 FPS、負荷が高い場所でも30 FPS。人数・視点・計測方法・発熱状態は未記録なので、同条件の自動比較基準やiOS合格とは扱わない。

## 引き継ぐ資料

- [景観の制作記録](../../plans/2026-10-04-roadside-scenery.md): 追加オブジェクト・共有素材・LOD・前後比較画像・静的検証。
- [小物の制作票](../../plans/2026-10-04-prop-finish.md): 低ポリモデル・共有材質・テクスチャ設定と検証。

## 完了条件

- [x] 景観と小物の追加内容、変更前の報告と比較画像を引き取る
- [ ] 代表地点で描画・メモリ・容量を区別して計測し、Quest 3/iOSの実機条件・人数・犬数を記録する
- [ ] 必要な調整を行い、景観の見え方を保ちながら負荷と容量を再確認する
- [ ] [VER-WLD-001](VER-WLD-001-quest-ios-devices.md) に端末別結果を反映する

計測手順は `vrc-performance`。Play計測は `vrc-verification` の相談・部分実行手順による。性能の合格を景観制作だけから推定しない。
