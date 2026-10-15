---
id: BUG-ENV-004
type: BUG
system: ENV
status: review
priority: Low
summary: 自作の小物 2 件に同一平面の重なり (写真ボードの仕上げ板 KH_PhotoBoard/Structure/FinishedVisual 219 cm² gap 2 mm、門前の案内板の屋根の棟 Ridge と RoofFront / RoofBack 7 cm² gap 0)。BUG-ENV-003 の走査で見つかった古民家以外の残り
next: "BUG-ENV-005 の Quest 確認 (D34) に合流。棟・屋根板・写真ボードの重なりは ZFight/7 で 0 組"
updated: 2026-10-15
---

# BUG-ENV-004 写真ボードの仕上げ板と案内板の屋根の棟の Z ファイト候補

[BUG-ENV-003](BUG-ENV-003-farmhouse-exterior-zfight.md) の走査 (`Tools/KominkaHillside/ZFight/1 - Scan Farmhouse`、2026-10-13) で、古民家以外にスポーンから 9 m 以内の同一平面・同じ向きの重なりが 2 件残った。ユーザー報告には無い (古民家の修正の副産物)。

| もの | 場所 | 面積 | 隙間 | 距離 |
|---|---|---|---|---|
| 写真ボードの仕上げ板 `KH_PhotoBoard/Structure/FinishedVisual` (自身の領域同士、材質 KH_PropFinish) | (52.44, 2.97, 41.22) | 219 cm² | 2.0 mm | 4.9 m |
| 門前の案内板の屋根 `KH_GuideBoards/Kominka/Structure/Ridge` と `RoofFront` / `RoofBack` (材質 KH_RiverWood) | (47.3〜49.2, 3.78, 38.4〜39.4) | 7 cm² × 4 | 0.0 mm | 1.8 m |

- どちらも生成物 (仕上げ板は [IMP-ENV-012](IMP-ENV-012-close-prop-finish.md) の KH_PropFinishEditor、案内板は Guide/1) なので、直す先は生成側の寸法 (4 mm 以上離す)。シーンの手直しでは次の生成で戻る
- Editor の静止画では判別できない。ユーザーの Play (PCVR / Quest) でちらつきが見えた時に着手する

## 完了条件

- [ ] 生成側を直し、Guide/1 / 小物の仕上げを再実行しても ZFight/1 の 9 m 以内の coplanar に出ない
- [ ] ユーザーの Play でちらつきが出ない

## 記録

2026-10-13: 起票 (BUG-ENV-003 の走査の残り)。優先度 Low は AI の仮置き。未着手。
2026-10-14: 統括 (Fable) 判断で着手せず。着手条件 (ユーザーの Play でちらつきが見える) が満たされておらず、生成側の 4 mm の修正は Guide/1 / Course/162 の再生成を伴うため、条件が揃うまで保留。
2026-10-15: ユーザーが Quest で看板・ポスターのちらつきを報告 ([BUG-ENV-005](BUG-ENV-005-quest-board-label-poster-zfight.md))。着手条件が満たされたので wip。2 件とも 005 の修正に含める。
2026-10-15: BUG-ENV-005 で直した (棟を板の下へ、屋根板を頂で突き合わせ、写真ボードは Blender で枠・背板・屋根を作り直し)。ZFight/7 で 0 組。Quest の確認は 005 の D34。
