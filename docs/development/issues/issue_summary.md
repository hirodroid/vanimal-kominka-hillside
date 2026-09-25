# Issue サマリ (未着手・対応中・確認待ち・保留)

> 課題の入口。**状態ごとに 4 分割**して管理する。
> **実行キューは常にこのファイルが持つ** (確認待ち・保留も、完了以外はここの表に載せる)。

| 状態 | frontmatter | 置き場所 | 補助サマリ |
|------|-------------|----------|-----------|
| 未着手・対応中 | `todo` / `wip` | `issues/` 直下 | — |
| 確認待ち | `review` | `issues/review/` | `review/review_summary.md` |
| 保留 | `hold` | `issues/hold/` | `hold/hold_summary.md` |
| 完了 | `done` | `issues/archive/` | `archive/archive_summary.md` |

**補助サマリは「置き場所ごとの掲載先」ではなく、状態固有の情報を足す場所。**
確認待ちなら「何を確かめれば完了か」、保留なら「保留の理由と再検討のトリガー」。
**完了以外は、補助サマリに載せてもこのファイルの表からは消さない**
(消すと実行キューから漏れる)。

## 採番

```bash
node scripts/check-issues.js --next   # 次に使える ID。採番は必ずこれで
```

ID は `{BUG|IMP|DES|VER}-{システム 2〜3 文字}-{3 桁}`。
完了時に個別ファイルを作らなかった Issue もサマリの表には残るため、
ディレクトリを見るだけでは次の番号が分からない。

**Issue が 1 件も無いうちは `--next` が何も出さない** (既存 ID から次を計算するため)。
各システムの 1 本目だけは `IMP-XX-001` のように手で決める。

| 種別 | 意味 |
|------|------|
| `BUG` | 不具合 |
| `IMP` | 改善・実装 |
| `DES` | 設計判断が要るもの |
| `VER` | 検証・確認作業 |

## frontmatter

```yaml
---
id: IMP-GM-001
type: IMP
system: GM
status: todo      # todo / wip / review / hold / done
priority: High    # High / Medium / Low
---
```

`priority` を書いたら、**下の表の重要度欄と一致させる** (検査が食い違いを見る)。

## 一覧

| ID | 重要度 | 概要 | 状態 |
|----|--------|------|------|
| [BUG-DOG-003](BUG-DOG-003-frisbee-non-master-throw.md) | High | 非マスターの投球で犬が追わない。原因はコード上の経路で確認 (投球通知が Pickup の解放同期より先に届き、古い保持状態で即取消)。犬 Owner 側の 1 秒保留と (送信者, 世代, 発生時刻) 照合で修正、ClientSim netorder 10/10。2 人 PCVR の再検証待ち | wip |
| [IMP-DOG-011](hold/IMP-DOG-011-muchio-greeting.md) | Medium | 裏山で空いている犬が、自由行動の一つとしてたまにムチォの匂いを嗅ぎに行く。2026-09-22 保留 (他者のアバターギミック依存・集客寄りの動機)。理由と再検討条件は hold_summary | hold |
| [VER-DOG-001](hold/VER-DOG-001-avatar-pet-contact-research.md) | Medium | 来訪者の無改変が条件。ムチォは既存Senderで対応候補、犬っこは通常時の対象外。静的調査まで完了、IMP-DOG-011 と共に保留 | hold |
| [IMP-DOG-001](IMP-DOG-001-port-dog-system.md) | High | 古民家ドッグランの犬ギミックを移植する (タグ `export/kominka-hillside-v1` + 移植記録) | wip |
| [DES-DOG-001](DES-DOG-001-leash-walking.md) | High | 集合・目的地継承・拠点回収の契約を実装済み (Editor)。Play 後に停滞検知・端末別入力・購入権の双方向を詰める | wip |
| [IMP-DOG-003](IMP-DOG-003-walking-polish.md) | High | 自由な散歩の試作は PCVR で「かなり良かった」。残り: 犬同士の遊び、Desktop/Touch、複数人。看板の本制作・分かりやすさは DES-UI-004 へ分離 | wip |
| [IMP-ENV-001](IMP-ENV-001-hillside-polish.md) | Medium | 負荷の初回測定後、道・木陰・見晴らし・休憩場所を磨く | todo |
| [DES-WLD-001](DES-WLD-001-round-trip.md) | Medium | 古民家からの出発・帰還と状態の扱いを設計する | todo |
| [DES-DOG-002](DES-DOG-002-toy-behaviour.md) | Medium | フリスビーは 1 匹で採用内容を確定 (IMP-DOG-008 完了)。複数ディスク・犬の割当は犬別 6 枚で開発側決定 (2026-09-24、ユーザー確認待ち)。残り: 他のおもちゃの挙動、ボールの柄違いの持たせ方、公開範囲 | todo |
| [IMP-DOG-005](IMP-DOG-005-frisbee-jump-catch.md) | Medium | フリスビー通常版はPCVRでひとまずOK。項目別・2人の検証を保持。バリエーションは008、6匹展開は012 | wip |
| [IMP-DOG-012](IMP-DOG-012-frisbee-six-dogs.md) | Medium | フリスビー (先回り型 + 6 プリセット) を 6 匹へ展開する。2026-09-24 実装済み (ff76602): 犬種別クリップ・facts、犬別ディスク 6 枚、犬種別の跳躍上限 (小型犬 0.45 m)、Verify・縮小ハーネス・6 匹同時の負荷 PASS。残り: Shiba / Samoyed の縮小表の取り直し・全 36 投げ (AI)、PCVR (ユーザー) | wip |
| [DES-UI-002](DES-UI-002-frisbee-settings.md) | Medium | フリスビーの設定 (待つ場所・曲げ・取り方) の選ぶ / 自動の切り分けと操作権、開発用切替の撤去時期を設計する。[試作] 入口は 9/23 の KH_HandMenu 退役で消え、代替はユーザー判断 | todo |
| [DES-UI-003](DES-UI-003-menu-scope-and-polish.md) | Medium | 9/23 ユーザー回答: 初回付与200pt・獲得は滞在のみ・撫でFX採用・タブ構成A・おじいさんは門・犬目線/かけっこは対象外・距離フェードとおもちゃ柄は採用。正本へ反映済み。モック OK・計画書あり。IMP-UI-001 は段階 0-b まで完了 (9/24 PCVR OK)、段階 2・3 は 9/24 実装済み・PCVR 待ち | wip |
| [IMP-UI-001](review/IMP-UI-001-menu-table-driven-and-settings.md) | High | 手元メニューを古民家の器 (KD_TreatPouch + Editor ビルダー移植) に置き換え、設定面を作る。段階 0-a / 0-b / 3 は実装済み・Verify 全 PASS、0-b は 2026-09-24 PCVR OK。段階 3 (距離フェード・許可初期 ON・ようこそ等) の実機確認待ち。クレジット URL 3 件はユーザー記入待ち | review |
| [IMP-PRG-001](review/IMP-PRG-001-ledger-shopkeeper-points.md) | High | 台帳・おじいさん (門)・支度用ポイント (初回 200pt 一回・滞在 1分1pt)・購入と CE 判定。2026-09-24 実装済み・Verify 全 PASS (e4f5906、名前空間 KH_Commerce_v1)。T-007 / T-008・復元の実機確認待ち | review |
| [IMP-DOG-014](review/IMP-DOG-014-treats-port.md) | Medium | おやつ 4 種を古民家から移植 (メニューの [買う] 含む)。2026-09-24 実装済み・Verify 全 PASS (c27a7e3)。出す/あげる/しまう・補充・T-009 (枠 3) の Play・実機確認待ち。接続犬の反応は IMP-DOG-006 | review |
| [IMP-DOG-015](review/IMP-DOG-015-accessories-and-petfx-port.md) | Medium | 飾り 14 商品と撫でFX 11 種を移植。2026-09-24 実装済み・Verify 全 PASS (c27a7e3)。首輪干渉は 2026-09-24 案 C で決定・適用 (5 件は (b) で下げ、3 件は犬ごとに除外、Verify 6f PASS。049b173)。装着・同期・見た目の PCVR 確認待ち | review |
| [IMP-DOG-016](review/IMP-DOG-016-call-pose-port-and-slope.md) | Medium | 呼ぶ・ポーズを移植し、リード接続中の除外と斜面 (接地・法線) の判定を足す。2026-09-24 実装済み・Verify 全 PASS (c27a7e3)。法線 0.9 は仮採用。Play・T-010・斜面の PCVR 確認待ち | review |
| [DES-UI-004](DES-UI-004-walk-destinations-and-boards.md) | Medium | 散歩の目的地・位置と仮看板の本制作、案内／説明ボードの内容・配置・役割を検討する | todo |
| [DES-LIV-001](DES-LIV-001-hillside-live.md) | Medium | 古民家の有料ライブ購入者の利用と、裏山ライブの開催・再生・競合・復帰を設計する。演目企画は DES-LIV-002 | todo |
| [DES-LIV-002](DES-LIV-002-live-lineup-and-parade.md) | Medium | ハロウィン・Candy Step・ライブパックの裏山向け演目を企画する。散歩道を犬たちがパレードする専用ライブ 1 本を含め、枠・本数・構成を整理 | todo |
| [DES-WLD-002](DES-WLD-002-sound.md) | Medium | 裏山の音 (BGM・環境音) の方針・音源・ライブとの切替を設計する | todo |
| [IMP-DOG-004](review/IMP-DOG-004-leash-models-release.md) | High | 首輪・リードの本制作。PCVR 1 人は合格。2 人・Quest/iOS・座位/木陰の確認待ち | review |
| [BUG-DOG-002](BUG-DOG-002-leash-handle-orientation.md) | Medium | リードの輪と紐の接続部が横向き。肘→手の延長上・手の前側へ向くよう左右とも調整する (9/23 要望具体化) | wip |
| [IMP-DOG-006](IMP-DOG-006-leashed-treat-approach.md) | Medium | おやつで自分の接続犬の最寄り1匹を呼び、少し待つ間に撫でる/首輪を外す→次の1匹。9/23 操作方針了承、詳細設計・実装は未 | todo |
| [IMP-DOG-007](IMP-DOG-007-leash-pull-haptics.md) | Medium | リードを引っ張られたら、接続している側の手に振動を伝える (Haptics)。9/22 に VR 限定で実装 (伸び比率 + 止まっている時の引き、手ごとに最大)、PCVR の体感確認が残る | wip |
| [IMP-DOG-009](IMP-DOG-009-walk-speed-tiers.md) | High | 散歩の犬の速さを 4 段階に (最速は現状の速歩の 3 倍)。人が犬を引っ張る状態をなくす。Run クリップの再生速度・減速も合わせる。9/22 PCVR 1 人 1 匹で OK・採用、3〜6 匹・2 人以上・Desktop は未確認 | wip |
| [VER-WLD-001](VER-WLD-001-quest-ios-devices.md) | Medium | Quest 単体・iOS の実機で全機能 (散歩・メニュー・空/夜・フリスビー・首輪/リード・負荷・境界) を確認する。各 Issue の Quest/iOS 未確認をここに集約 | todo |
| [IMP-DOG-013](IMP-DOG-013-frisbee-pet-after-return.md) | Medium | フリスビーを持ち帰った犬をよしよしできる。くわえて待っている間は撫でを再開し (撫でている間はディスクを落とさない)、受け取っても 4 秒 / 2.5 m の猶予はその場で待つ。9/22 実装・Editor 検査済み、PCVR 確認が残る | wip |
| [IMP-DOG-010](IMP-DOG-010-walk-stopovers.md) | Medium | 寄り道が分かりにくい (嗅ぐ姿勢が無い・ずれが小さい・短い) ので作り直し、道沿いの立ち寄り場所と夜の散歩の違いを足す。9/22 PCVR 1 人 1 匹で OK・採用 (Sniff クリップは保留)、3〜6 匹・2 人以上・Desktop は未確認 | wip |

着手順と現在地の説明は [開発全体像](../overview.md) §3 が正本 (このファイルは実行キューと状態だけを持つ)。2026-09-20〜23 の着手順メモは git 履歴 (`ff76602` 時点のこのファイル) を参照。

---

## 移動の手順

状態が変わったら**ファイルの移動とサマリの更新を必ずセットで行う**。

1. `git mv` でファイルを移す — **先に mv する**
   (編集してから mv すると、編集分がステージされず漏れる)
2. frontmatter の `status` を書き換える
3. **このファイルの表を更新する**
   - 完了以外 (`todo` ⇄ `wip` ⇄ `review` ⇄ `hold`) — 状態欄とリンク先を直す。**行は残す**
   - 完了 (`done`) — 行を削除し、`archive/archive_summary.md` へ移す
4. 補助サマリがあれば、そちらの表も更新する
5. 検査を 2 本とも通す
   ```bash
   node scripts/check-issues.js
   node scripts/check-doc-links.js
   ```
   移動で `../` の段数がずれる。**`.md` だけでなく `Assets/` 配下のソースへのリンクも
   同じだけずれる** (見落としやすい)
6. `git add` してコミット
