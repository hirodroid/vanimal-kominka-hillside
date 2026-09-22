---
id: IMP-DOG-012
type: IMP
system: DOG
status: todo
priority: Medium
---

# IMP-DOG-012 フリスビーを 6 匹へ展開する

2026-09-22 起票。[IMP-DOG-008](archive/IMP-DOG-008-frisbee-variations.md) でラブ 1 匹の通常版 (先回り型 + 動きの層) と 6 プリセット (待つ場所 3 種・ひねり・横曲がり) をユーザーが PCVR で「いい感じ」と評価し、全部採用した。次はこれを 6 匹へ展開する。採用範囲の設計は [DES-DOG-002](DES-DOG-002-toy-behaviour.md)、設計値と検証記録は [段階 3 の記録](../../plans/2026-09-21-frisbee-variations.md)、設定 UI は [DES-UI-002](DES-UI-002-frisbee-settings.md)。

## 範囲

- 犬種ごとの姿勢クリップ (Jump / Twist) と口の事実 (facts) を `KH_FrisbeeJumpClipEditor` で生成する。生成器は現在 `Breed = "Lab"` 固定なので、犬種を引数にして 6 犬種を回せるようにする (角度はモデルごとの制作値。側面図で確認)。
- 各犬に `KH_FrisbeeCatch` とディスク 1 枚 (識別色) を配線し、`DiscHome(i)` に置く。メニュー「おもちゃ」の「まだ」を外す。
- 購入犬の胴体追従 (`…/PB3/inu` の ParentConstraint) をフリスビーの犬すべてで無効化する (ラブで硬さの指摘なし。犬種ごとに骨格・Constraint の名前を確認)。
- 6 匹同時の負荷: 候補探索 (最大 5 パス × 32 時刻 × レーン・地面レイ) と 10 Hz の再計画の実測 (`vrc-performance`)。再計画タイミングの分散、軌道と地面高さのパス間再利用を検討。
- `KH_FrisbeeCatch` (1,800 行超) の分割: 同じ Behaviour のまま partial で ①同期・状態遷移 ②計画器 ③移動・跳躍 ④診断 に分ける (Codex M4)。捕獲種別と接近方法の名前・不変条件を整理してから展開する。
- ハーネスを犬種ごとに回せるようにし (投球表に開始位置・向き・期待する捕獲種別)、6 犬種 × 投球表で PASS。
- 複数ディスク・犬の割当 (誰のディスクをどの犬が追うか)、Owner 分裂時の扱いは DES-DOG-002 の決定に従う。

## やらないこと

設定 UI の中身 (DES-UI-002)、ディスクの柄違い、斜面でのジャンプ、Quest/iOS の合格判定 (VER 系で別途)。

## 完了条件

- [ ] 6 犬種のクリップ・facts を生成し、側面図で姿勢を確認した
- [ ] 6 匹に Catch とディスクを配線し、Verify (Walking) が 6 匹ぶんを検査する
- [ ] 6 犬種 × 投球表のハーネスが PASS (捕獲・完了・動きの質)
- [ ] 6 匹同時の負荷を Editor Play で実測し、記録した
- [ ] `KH_FrisbeeCatch` の分割と名前の整理を済ませた
- [ ] PCVR で 6 匹の動きをユーザーが評価した (未実施項目は残課題として記録)
