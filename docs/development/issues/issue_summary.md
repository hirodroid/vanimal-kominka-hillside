# Issue サマリ

> 生成ファイル。手で編集しない。運用は [README.md](README.md)。
> 更新は `node scripts/check-issues.js --write` (状態・概要・次は各 Issue の frontmatter が正本)。

37 件 — 対応中 12 / 確認待ち 6 / 未着手 10 / 保留 2 / 完了 7

## 実行キュー

| ID | 重要度 | 概要 | 状態 | 次 |
|----|--------|------|------|----|
| [BUG-DOG-003](BUG-DOG-003-frisbee-non-master-throw.md) | High | 非マスターの投球で犬が追わない。原因はコード上の経路で確認 (投球通知が Pickup の解放同期より先に届き、古い保持状態で即取消)。犬 Owner 側の 1 秒保留と (送信者, 世代, 発生時刻) 照合で修正、ClientSim netorder 10/10。2 人 PCVR の再検証待ち | wip |  |
| [DES-DOG-001](DES-DOG-001-leash-walking.md) | High | 集合・目的地継承・拠点回収の契約を実装済み (Editor)。Play 後に停滞検知・端末別入力・購入権の双方向を詰める | wip |  |
| [IMP-DOG-001](IMP-DOG-001-port-dog-system.md) | High | 古民家ドッグランの犬ギミックを移植する (タグ `export/kominka-hillside-v1` + 移植記録) | wip |  |
| [IMP-DOG-003](IMP-DOG-003-walking-polish.md) | High | 自由な散歩の試作は PCVR で「かなり良かった」。残り: 犬同士の遊び、Desktop/Touch、複数人。看板の本制作・分かりやすさは DES-UI-004 へ分離 | wip |  |
| [IMP-DOG-009](IMP-DOG-009-walk-speed-tiers.md) | High | 散歩の犬の速さを 4 段階に (最速は現状の速歩の 3 倍)。人が犬を引っ張る状態をなくす。Run クリップの再生速度・減速も合わせる。9/22 PCVR 1 人 1 匹で OK・採用、3〜6 匹・2 人以上・Desktop は未確認 | wip |  |
| [BUG-DOG-002](BUG-DOG-002-leash-handle-orientation.md) | Medium | リードの輪と紐の接続部が横向き。肘→手の延長上・手の前側へ向くよう左右とも調整する (9/23 要望具体化) | wip |  |
| [DES-UI-003](DES-UI-003-menu-scope-and-polish.md) | Medium | 9/23 ユーザー回答: 初回付与200pt・獲得は滞在のみ・撫でFX採用・タブ構成A・おじいさんは門・犬目線/かけっこは対象外・距離フェードとおもちゃ柄は採用。正本へ反映済み。モック OK・計画書あり。IMP-UI-001 は段階 0-b まで完了 (9/24 PCVR OK)、段階 2・3 は 9/24 実装済み・PCVR 待ち | wip |  |
| [IMP-DOG-005](IMP-DOG-005-frisbee-jump-catch.md) | Medium | フリスビー通常版はPCVRでひとまずOK。項目別・2人の検証を保持。バリエーションは008、6匹展開は012 | wip |  |
| [IMP-DOG-007](IMP-DOG-007-leash-pull-haptics.md) | Medium | リードを引っ張られたら、接続している側の手に振動を伝える (Haptics)。9/22 に VR 限定で実装 (伸び比率 + 止まっている時の引き、手ごとに最大)、PCVR の体感確認が残る | wip |  |
| [IMP-DOG-010](IMP-DOG-010-walk-stopovers.md) | Medium | 寄り道が分かりにくい (嗅ぐ姿勢が無い・ずれが小さい・短い) ので作り直し、道沿いの立ち寄り場所と夜の散歩の違いを足す。9/22 PCVR 1 人 1 匹で OK・採用 (Sniff クリップは保留)、3〜6 匹・2 人以上・Desktop は未確認 | wip |  |
| [IMP-DOG-012](IMP-DOG-012-frisbee-six-dogs.md) | Medium | フリスビー (先回り型 + 6 プリセット) を 6 匹へ展開する。2026-09-24 実装済み (ff76602): 犬種別クリップ・facts、犬別ディスク 6 枚、犬種別の跳躍上限 (小型犬 0.45 m)、Verify・縮小ハーネス・6 匹同時の負荷 PASS。残り: Shiba / Samoyed の縮小表の取り直し・全 36 投げ (AI)、PCVR (ユーザー) | wip |  |
| [IMP-DOG-013](IMP-DOG-013-frisbee-pet-after-return.md) | Medium | フリスビーを持ち帰った犬をよしよしできる。くわえて待っている間は撫でを再開し (撫でている間はディスクを落とさない)、受け取っても 4 秒 / 2.5 m の猶予はその場で待つ。9/22 実装・Editor 検査済み、PCVR 確認が残る | wip |  |
| [IMP-DOG-004](IMP-DOG-004-leash-models-release.md) | High | 首輪・リードの本制作。PCVR 1 人は合格。2 人・Quest/iOS・座位/木陰の確認待ち | review | PCVR 1 人は合格 (2026-09-20)。残り: 2 人・Quest/iOS・座位/木陰。持ち手の向きは BUG-DOG-002 |
| [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md) | High | 台帳・おじいさん (門)・支度用ポイント (初回 200pt 一回・滞在 1分1pt)・購入と CE 判定。2026-09-24 実装済み・Verify 全 PASS (e4f5906、名前空間 KH_Commerce_v1)。T-007 / T-008・復元の実機確認待ち | review | 200pt が一回だけ入る (T-007)、滞在加算と再入室の復元、購入の保存と CE の所持判定、残高 0 で基本機能が使える (T-008)。**AI 側の残作業**: 台帳用の ClientSim ハーネスが未作成で、付与・購入・復元の固定手順が未実行 (ユーザーの PCVR 確認とは別) |
| [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md) | High | 手元メニューを古民家の器 (KD_TreatPouch + Editor ビルダー移植) に置き換え、設定面を作る。段階 0-a / 0-b / 3 は実装済み・Verify 全 PASS、0-b は 2026-09-24 PCVR OK。段階 3 (距離フェード・許可初期 ON・ようこそ等) の実機確認待ち。クレジット URL 3 件はユーザー記入待ち | review | 段階 0-b は PCVR OK (2026-09-24)。残り: 退行・取り違えの確認、段階 3 の距離フェード (T-011)・ナイトモードのスライダー・許可トグル 2 種・状態表示の規律。クレジット URL 3 件はユーザー記入。Quest/iOS は VER-WLD-001 |
| [IMP-DOG-014](IMP-DOG-014-treats-port.md) | Medium | おやつ 4 種を古民家から移植 (メニューの [買う] 含む)。2026-09-24 実装済み・Verify 全 PASS (c27a7e3)。出す/あげる/しまう・補充・T-009 (枠 3) の Play・実機確認待ち。接続犬の反応は IMP-DOG-006 | review | 出す / あげる / しまう が 1 人で成立、在庫の増減と復元、[買う] の補充、4 人で枠 3 (T-009)、6 匹の食べ・お礼。**AI 側の残作業**: おやつ用の ClientSim ハーネスが未作成で、出す → あげる → しまう → 在庫確認の固定手順が未実行 (ユーザーの PCVR 確認とは別) |
| [IMP-DOG-015](IMP-DOG-015-accessories-and-petfx-port.md) | Medium | 飾り 14 商品と撫でFX 11 種を移植。2026-09-24 実装済み・Verify 全 PASS (c27a7e3)。首輪干渉は 2026-09-24 案 C で決定・適用 (5 件は (b) で下げ、3 件は犬ごとに除外、Verify 6f PASS。049b173)。装着・同期・見た目の PCVR 確認待ち | review | 6 匹への装着と他人への表示、購入・試着と台帳の一致、撫でFX の配布、60 秒ルールと退室後の残存 (2 人)、首まわりの見た目 (案 C 適用後) |
| [IMP-DOG-016](IMP-DOG-016-call-pose-port-and-slope.md) | Medium | 呼ぶ・ポーズを移植し、リード接続中の除外と斜面 (接地・法線) の判定を足す。2026-09-24 実装済み・Verify 全 PASS (c27a7e3)。法線 0.9 は仮採用。Play・T-010・斜面の PCVR 確認待ち | review | 6 匹で平地の流れが成立、リード中の除外と逆方向 (T-010)、斜面の赤マーカーと法線 0.9 の確定、4 動作の課金ゲート、許可トグル、2 人の予約の取り合い |
| [DES-DOG-002](DES-DOG-002-toy-behaviour.md) | Medium | フリスビーは 1 匹で採用内容を確定 (IMP-DOG-008 完了)。複数ディスク・犬の割当は犬別 6 枚で開発側決定 (2026-09-24、ユーザー確認待ち)。残り: 他のおもちゃの挙動、ボールの柄違いの持たせ方、公開範囲 | todo |  |
| [DES-LIV-001](DES-LIV-001-hillside-live.md) | Medium | 古民家の有料ライブ購入者の利用と、裏山ライブの開催・再生・競合・復帰を設計する。演目企画は DES-LIV-002 | todo |  |
| [DES-LIV-002](DES-LIV-002-live-lineup-and-parade.md) | Medium | ハロウィン・Candy Step・ライブパックの裏山向け演目を企画する。散歩道を犬たちがパレードする専用ライブ 1 本を含め、枠・本数・構成を整理 | todo |  |
| [DES-UI-002](DES-UI-002-frisbee-settings.md) | Medium | フリスビーの設定 (待つ場所・曲げ・取り方) の選ぶ / 自動の切り分けと操作権、開発用切替の撤去時期を設計する。[試作] 入口は 9/23 の KH_HandMenu 退役で消え、代替はユーザー判断 | todo |  |
| [DES-UI-004](DES-UI-004-walk-destinations-and-boards.md) | Medium | 散歩の目的地・位置と仮看板の本制作、案内／説明ボードの内容・配置・役割を検討する | todo |  |
| [DES-WLD-001](DES-WLD-001-round-trip.md) | Medium | 古民家からの出発・帰還と状態の扱いを設計する | todo |  |
| [DES-WLD-002](DES-WLD-002-sound.md) | Medium | 裏山の音 (BGM・環境音) の方針・音源・ライブとの切替を設計する | todo |  |
| [IMP-DOG-006](IMP-DOG-006-leashed-treat-approach.md) | Medium | おやつで自分の接続犬の最寄り1匹を呼び、少し待つ間に撫でる/首輪を外す→次の1匹。9/23 操作方針了承、詳細設計・実装は未 | todo |  |
| [IMP-ENV-001](IMP-ENV-001-hillside-polish.md) | Medium | 負荷の初回測定後、道・木陰・見晴らし・休憩場所を磨く | todo |  |
| [VER-WLD-001](VER-WLD-001-quest-ios-devices.md) | Medium | Quest 単体・iOS の実機で全機能 (散歩・メニュー・空/夜・フリスビー・首輪/リード・負荷・境界) を確認する。各 Issue の Quest/iOS 未確認をここに集約 | todo |  |
| [IMP-DOG-011](IMP-DOG-011-muchio-greeting.md) | Medium | 裏山で空いている犬が、自由行動の一つとしてたまにムチォの匂いを嗅ぎに行く。2026-09-22 保留 (他者のアバターギミック依存・集客寄りの動機)。理由と再検討条件は本文末尾の保留の記録 | hold | 再検討: 「これがあると散歩・犬との体験そのものが良くなる」と Play で感じたとき。集客目的だけでは解除しない |
| [VER-DOG-001](VER-DOG-001-avatar-pet-contact-research.md) | Medium | 来訪者の無改変が条件。ムチォは既存Senderで対応候補、犬っこは通常時の対象外。静的調査まで完了、IMP-DOG-011 と共に保留 | hold | 再検討: IMP-DOG-011 の保留解除時。静的調査結果は [調査結果](../../plans/2026-09-21-avatar-pet-contact-research.md) に残す |

## 完了

| ID | 重要度 | 概要 | 日付 |
|----|--------|------|------|
| [DES-ENV-001](DES-ENV-001-terrain-vs-blender.md) | High | 地形を Unity Terrain で作るか Blender で作るか → **Terrain 採用** | 2026-09-22 |
| [DES-UI-001](DES-UI-001-menu-and-leash-assist.md) | High | 古民家と揃えた手元メニュー (さんぽ / あそぶ / おもちゃ)・接続補助・目的地選択・集合・ボール個別回収 | 2026-09-22 |
| [IMP-DOG-002](IMP-DOG-002-leash-walking.md) | High | 先端 Pickup → 首輪接続、片手 3 匹・合計 6 匹、接続中の追従、Fetch 競合抑止、退出時解除、途中参加復元 | 2026-09-22 |
| [IMP-DOG-008](IMP-DOG-008-frisbee-variations.md) | Medium | ラブ 1 匹でフリスビーの走り込み・取り方・飛び方を試し、先回り型 + 動きの層に作り替え、6 プリセット全部採用 | 2026-09-22 |
| [IMP-ENV-002](IMP-ENV-002-skybox-and-night-mode.md) | Medium | 空を古民家と同じキューブマップにし、手元メニューのヘッダーに OFF / ON のナイトモード (RBS 不使用、乗算オーバーレイ + 空の露出) | 2026-09-22 |
| [VER-ENV-001](VER-ENV-001-prototype-view-and-edges.md) | High | 試作: 眺め・移動・端の見え方・実機負荷を確認する (代表シーン検証を兼ねる) | 2026-09-22 |
| [BUG-DOG-001](BUG-DOG-001-ball-spawn-slope.md) | High | ボール 6 個を坂から上の広場の平坦部へ | 2026-09-20 |
