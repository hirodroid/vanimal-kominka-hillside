---
id: IMP-DOG-025
type: IMP
system: DOG
status: wip
priority: High
summary: 代表 1 匹 (柴) で洗浄の一周を細く通し、設計書の賭け 3 つを確かめる試作
next: 犬メッシュの調査と位置マップの焼き込みツールを作り、賭け 1 (UV の重なり) を Editor で確かめる
updated: 2026-09-30
---

# IMP-DOG-025 代表 1 匹 (柴) の洗浄の一周

親は [IMP-DOG-024](IMP-DOG-024-walk-and-wash-experience.md) (実装順 2「代表 1 匹・主洗い場の試作」)。方式は [設計書](../../plans/2026-09-30-dog-wash-design.md) §1・§4・§6・§7。設計書の値はすべて試作値で、この Issue の結果で確定する。

## 範囲

やること (設計書 §6 の IMP-DOG-025 の行)。

- 柴の UV・部位・スケールの検査と、位置マップの焼き込み (§1-1、`Tools/KominkaHillside/Wash/1`)
- 部位 Collider (ボーンの子、専用 Layer、Trigger) と、マスク (RenderTexture 2 枚のダブルバッファ) の Blit の描画 (§1-2)
- 殻 (2 つ目の SkinnedMeshRenderer) 1 層 (§1-3)
- スポンジとシャワーの最小入力 (VR / Desktop)、飛沫、噴射と接触の仮の音 (§1-7、§1-8)
- ストロークの最小の同期 (§1-4)、コミカルな反応 1 種 (§1-6)
- 白い毛・暗い毛・小型への簡易表示 (サモエド・ダックスフンド、§4)
- 賭け 3 つの確認 (§7)

やらないこと (他の Issue)。セルの正本・清潔の判定・再洗浄・途中参加・Owner 交代は [IMP-DOG-026](IMP-DOG-026-wash-state-and-sync.md)。行動の切替は [IMP-DOG-027](IMP-DOG-027-wash-behavior-switching.md)。音素材の制作・タオル・ブルブルは [IMP-DOG-028](IMP-DOG-028-wash-sound-and-reactions.md)。汚れの発生は [IMP-DOG-029](IMP-DOG-029-dirt-accumulation.md) (試作中の汚れは設定した初期値)。洗い場の整備は [IMP-ENV-007](IMP-ENV-007-main-wash-station.md)。6 匹は [IMP-DOG-030](IMP-DOG-030-wash-six-dogs.md)。

## 完了条件

AI 側で確かめる。

- [ ] コンパイルが完了し、エラーが 0 (`bash scripts/unity-compile.sh --udon`)
- [ ] 賭け 1: 柴の UV の重なり・ミラーの数を Editor の検査で出し、位置マップの有効テクセルの割合と部位 Collider・ボーンの対応を記録した。外れた場合は設計書 §7 の切り替え先を記録した
- [ ] 殻の Renderer (メッシュ・骨・影の設定・bounds) を Editor の Verify で確認できる。汚れ・泡・濡れの無い犬は殻が無効になる
- [ ] ClientSim ハーネスで、ストロークを与えるとマスクの値が変わり、接触が切れた・部位が変わった時に区間が切れることを確認した (試作の範囲)

ユーザーが確かめる (Play / PCVR、2 人)。

- [ ] 賭け 2: PC の Editor と Play で、殻が毛の上に自然に乗り、横腹・脇・足・顔で毛と前後しない
- [ ] 賭け 3 のうち、PC での連続描画と 2 人の表示のずれの確認 (対象端末の実機は [VER-DOG-002](VER-DOG-002-wash-experience-and-devices.md))
- [ ] 泡の育ち、シャワーをゆっくり横へ動かした時の帯状の境目、接触の飛沫と仮の音が同じ場所・同じタイミングで出る (親: 帯状のすすぎ、ASMR の初期評価)
- [ ] 動く足 1 本にマスクが追従し、コミカルな反応 1 種が出る (親: コミカルな反応の初期確認)
- [ ] 犬の Owner でない人の操作でも、その人の手元で即座に泡と接触が返り、もう 1 人にも帯が見える (親: 2 人協力の初期確認)

この Issue の結果は、DES-DOG-006 の「賭けの結果を受けて方式を確定した」の根拠になる。

## 記録

2026-09-30: 起票。犬メッシュの調査と位置マップの焼き込みツールの実装に着手 (未検証)。
