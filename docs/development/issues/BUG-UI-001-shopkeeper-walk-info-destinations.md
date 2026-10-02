---
id: BUG-UI-001
type: BUG
system: UI
status: todo
priority: Low
summary: おじいさん (KD_ShopKeeper) の「さんぽ」の案内文が、行き先を「広場」と「古民家側の入口」の 2 つだけと言っている (実際は 6 目的地)
next: 案内文 `KD_ShopKeeper.InfoWalk` の行き先の記述を、現行の手元メニューの 6 目的地に合わせるか、個数に触れない書き方へ直す。13 言語の翻訳と枠への収まりも確認する
updated: 2026-10-03
---

# BUG-UI-001 おじいさんの「さんぽ」案内が行き先を 2 つだけと言っている

2026-10-03 の Issue 整理で見つかった (別の作業者の報告)。どの Issue も担当していなかったので起票した。

## 症状

おじいさんに話しかけ「さんぽ」を選ぶと出る案内文 (`Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_ShopKeeper.cs` の `InfoWalk`) に「行き先は『広場』か『古民家側の入口』だ。」とある。周回コースの採用 (2026-09-27) 以降、手元メニューの行き先は 6 目的地 ([IMP-ENV-004](IMP-ENV-004-river-loop-course.md))。案内が実際の選択肢と食い違う。

## 範囲

- `InfoWalk` の該当 2 行を、現行の目的地に合わせて直す (個数や名前を書くなら、手元メニューの呼び名と揃える。書かない案もある)。IMP-PRG-001 の「裏山向けに差し替える」ナビ文言の続き
- 翻訳表 (`UITranslations.json`) に同じ文があれば同じ直しを入れ、11 言語 (client 字形) で枠に収まるかを確認する
- 古民家から移植したスクリプトの裏山改変なので、変更点を [移植記録](../imports/kominka-dog-system.md) の裏山側変更へ残す

## 完了条件

- [ ] 案内文が現行の行き先と一致している (コンパイルと Menu の Verify が通る)
- [ ] 多言語で枠に収まる (翻訳表がある場合)
- [ ] 本文が書き換わった旨を記録した

## 記録

2026-10-03: 起票。関連: [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md) (おじいさん)、[DES-UI-004](DES-UI-004-walk-destinations-and-boards.md) (看板の行き先案内と揃える)。
