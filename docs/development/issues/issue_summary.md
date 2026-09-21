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
| [DES-ENV-001](DES-ENV-001-terrain-vs-blender.md) | High | 地形を Unity Terrain で作るか Blender で作るか (着手: 比較条件と切替基準 → VER-ENV-001 の実測で確定) | todo |
| [VER-ENV-001](VER-ENV-001-prototype-view-and-edges.md) | High | 試作: 眺め・移動・端の見え方・実機負荷を確認する (代表シーン検証を兼ねる)。v4生成・PCVR総合評価は良好、境界・負荷は項目別検証中 | wip |
| [IMP-DOG-001](IMP-DOG-001-port-dog-system.md) | High | 古民家ドッグランの犬ギミックを移植する (タグ `export/kominka-hillside-v1` + 移植記録) | wip |
| [DES-DOG-001](DES-DOG-001-leash-walking.md) | High | 集合・目的地継承・拠点回収の契約を実装済み (Editor)。Play 後に停滞検知・端末別入力・購入権の双方向を詰める | wip |
| [IMP-DOG-002](IMP-DOG-002-leash-walking.md) | High | 基本実装・PCVR体験好評。上限・競合・退出・途中参加などの項目別確認と修正 | wip |
| [IMP-DOG-003](IMP-DOG-003-walking-polish.md) | High | 自由な散歩の試作は PCVR で「かなり良かった」。残り: 犬同士の遊び、看板の分かりやすさ、Desktop/Touch、複数人 | wip |
| [IMP-ENV-001](IMP-ENV-001-hillside-polish.md) | Medium | 負荷の初回測定後、道・木陰・見晴らし・休憩場所を磨く | todo |
| [DES-WLD-001](DES-WLD-001-round-trip.md) | Medium | 古民家からの出発・帰還と状態の扱いを設計する | todo |
| [DES-UI-001](DES-UI-001-menu-and-leash-assist.md) | High | 手元メニュー (さんぽ/あそぶ/おもちゃ)・接続補助・目的地・集合・ボール回収を実装、Editor 検査済み。Play 確認待ち | wip |
| [DES-DOG-002](DES-DOG-002-toy-behaviour.md) | Medium | フリスビー通常版の体験はひとまずOK。1匹で取り方・飛び方の採用内容を固めてから6匹へ展開する | todo |
| [IMP-DOG-005](IMP-DOG-005-frisbee-jump-catch.md) | Medium | フリスビー通常版はPCVRでひとまずOK。項目別・2人の検証を保持。バリエーションは008、6匹展開はその後の別Issue | wip |
| [IMP-DOG-008](IMP-DOG-008-frisbee-variations.md) | Medium | ラブ1匹で走り込み 3 候補・ひねり・横曲がりを開発用の切替で比べる (設計済み、実装中)。通常版と採用バリエーションを評価後に6匹展開 | wip |
| [DES-LIV-001](DES-LIV-001-hillside-live.md) | Medium | 裏山専用ライブと古民家の有料ライブ購入者の利用を設計する | todo |
| [DES-WLD-002](DES-WLD-002-sound.md) | Medium | 裏山の音 (BGM・環境音) の方針・音源・ライブとの切替を設計する | todo |
| [IMP-DOG-004](review/IMP-DOG-004-leash-models-release.md) | High | 首輪・リードの本制作。PCVR 1 人は合格。2 人・Quest/iOS・座位/木陰の確認待ち | review |
| [BUG-DOG-002](BUG-DOG-002-leash-handle-orientation.md) | Medium | リードの持ち手が左右どちらの手でも左向きに付く。最後の微調整 | todo |
| [IMP-DOG-006](IMP-DOG-006-leashed-treat-approach.md) | Medium | リード接続中もおやつに反応して寄ってくる。反応対象・操作・散歩への復帰を具体化する | todo |
| [IMP-DOG-007](IMP-DOG-007-leash-pull-haptics.md) | Medium | リードを引っ張られたら、接続している側の手に振動を伝える (Haptics)。判定・振動パターン・端末対応を具体化する | todo |
| [IMP-ENV-002](IMP-ENV-002-skybox-and-night-mode.md) | Medium | 空を古民家と同じキューブマップにし、手元メニューのヘッダーに OFF / ON のナイトモード (RBS 不使用。乗算オーバーレイ + 空の露出) を実装。Editor 検査済み、Play・Quest/iOS 確認待ち | wip |
| [IMP-DOG-009](IMP-DOG-009-walk-speed-tiers.md) | High | 散歩の犬の速さを 4 段階に (最速は現状の速歩の 3 倍)。人が犬を引っ張る状態をなくす。Run クリップの再生速度・減速も合わせる | todo |

着手順 (2026-09-20 PCVR 好評後に整理): **DES-UI-001** (古民家型メニュー本体: 目的地選択・接続/解除補助・集合・ボール集め。仮看板を置き換える) を主軸に、小さな UX 調整 (BUG-DOG-002 持ち手の向き、看板文字、IMP-DOG-003 の犬同士の遊び・項目別確認) を並行。次に DES-DOG-002 (おもちゃ) と DES-DOG-001 (おしゃれ)。音は DES-WLD-002、ライブは DES-LIV-001 で設計を並行し、制作は基本体験の後。複数人・Quest/iOS・境界の検証 (IMP-DOG-002 / VER-ENV-001) と往復導線 (DES-WLD-001) は継続、IMP-ENV-001 の景観増量は後回し。具体的な目的と設計提案は [開発全体像](../overview.md) §3。

最新の制作指示ではIMP-DOG-004のBlender本制作とおもちゃ提案を優先して着手。上記の機能改善順は残し、モデルの仕上げと区別する。

2026-09-21 フリスビーの追加合意: 通常版の体験はVRでひとまずOK。**フリスビーの次の制作はIMP-DOG-008** (1匹で外からの走り込み → 取り方・飛び方の候補比較 → 採用組み合わせの評価)。1匹でOKになってから6匹展開を別Issueにする。IMP-DOG-005の未確認項目は継続する。

2026-09-20追加相談でIMP-DOG-003を目的地と通過点による自由な散歩へ具体化。目的地指定に必要なDES-DOG-001/ DES-UI-001の契約は散歩試作に先行させ、メニュー全体の完成を前提にしない。

同日、ユーザー指示でBUG-DOG-001とIMP-DOG-003の手順1〜2を実装 ([記録](../../plans/2026-09-20-free-walk.md))。PCVR で好評。続けて DES-UI-001 / DES-DOG-001 の合意を実装 ([記録](../../plans/2026-09-20-menu-gather-stations.md))。次の Play/PCVR で一括確認する。

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
