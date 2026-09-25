# 完了した Issue

> 完了 (`status: done`) の一覧。**採番のためにも行は消さない。**
> 個別ファイルを作らずに完了したものも、ここに行だけ残す。
>
> 入口と移動手順は [issue_summary.md](../issue_summary.md)。

| ID | 概要 | 完了日 | 備考 |
|----|------|--------|------|
| [BUG-DOG-001](BUG-DOG-001-ball-spawn-slope.md) | ボール 6 個を坂から上の広場の平坦部へ | 2026-09-20 | Builder が設計値として毎回配置、Verify で平坦部・NavMesh 上を検査。PCVR で落下報告なし |
| [IMP-DOG-008](IMP-DOG-008-frisbee-variations.md) | ラブ 1 匹でフリスビーの走り込み・取り方・飛び方を試し、先回り型 + 動きの層に作り替え、6 プリセット全部採用 | 2026-09-22 | Editor Verify + ClientSim ハーネス (30 投げ) + PCVR 5 回。ひねりの MISS の真因は購入犬の胴体追従 Constraint。6 匹展開は IMP-DOG-012、設定は DES-UI-002 |
| [DES-ENV-001](DES-ENV-001-terrain-vs-blender.md) | 地形を Unity Terrain で作るか Blender で作るか → **Terrain 採用** | 2026-09-22 | ユーザー判断「もうテレインで OK」。v1〜v4 の試作 (v4 Verify 53/53、Console Error 0) と PCVR の評価で切り替え基準に当たらず確定。Android / iOS の負荷測定は未実施で IMP-ENV-001 へ |
| [VER-ENV-001](VER-ENV-001-prototype-view-and-edges.md) | 試作: 眺め・移動・端の見え方・実機負荷を確認する (代表シーン検証を兼ねる) | 2026-09-22 | 眺め・端は PCVR で良好、DES-ENV-001 と同時に完了。項目別の残り (項目 2 の南東・南西の隅、4 の落下、6 の Quest/iOS 負荷) は IMP-ENV-001 の「負荷の初回測定」へ。(不変条件 7) |
| [DES-UI-001](DES-UI-001-menu-and-leash-assist.md) | 古民家と揃えた手元メニュー (さんぽ / あそぶ / おもちゃ)・接続補助・目的地選択・集合・ボール個別回収 | 2026-09-22 | Editor 検査 (Menu 21/21・Walking 111/111) 後、ユーザーが PCVR で確認済み。モックは作らず実装→PCVR で代替。残り: 座位・Desktop/Touch → IMP-DOG-003、複数人・他人の接続犬 → IMP-DOG-002、権限の再判定・端末別入力 → DES-DOG-001 |
| [IMP-DOG-002](IMP-DOG-002-leash-walking.md) | 先端 Pickup → 首輪接続、片手 3 匹・合計 6 匹、接続中の追従、Fetch 競合抑止、退出時解除、途中参加復元 | 2026-09-22 | Editor 検査 (コンパイル・配置・材質・独立レビュー) + ユーザーの PCVR。1 人は 2026-09-20、**競合・退出・途中参加は 2026-09-22 の 6 人 PCVR で問題なし** (総合評価。項目別の合否は未記録)。DES-UI-001 から引き継いだ「別の人の接続犬への非影響」も同じ場で扱った。**Quest 単体・iOS は未実施 → VER-WLD-001** |
| [IMP-ENV-002](IMP-ENV-002-skybox-and-night-mode.md) | 空を古民家と同じキューブマップにし、手元メニューのヘッダーに OFF / ON のナイトモード (RBS 不使用、乗算オーバーレイ + 空の露出) | 2026-09-22 | Editor 検査 (Menu 25/25・Night 32/32) + Codex レビュー 2 回の後、PCVR で確認済み。**Quest / iOS と 2 人での非伝播は未確認**。ライブの夜とは max 合成の口だけ用意 (DES-LIV-001) |

---

## 記録の指針

- **「直した」だけでなく「どう確かめたか」を残す** — 次に同じ症状が出たとき、
  検証方法が分かっていることの方が価値が高い
- **想定と実際がずれた点を書く** — 着手前の見立てが外れた記録は、
  次の見積もりの精度に直結する
- 一般化できる知見を得たら、**共有リポジトリの Skill へ反映してから**完了にする
  (Issue に書いて満足すると、次のワールドで同じ失敗をする)
