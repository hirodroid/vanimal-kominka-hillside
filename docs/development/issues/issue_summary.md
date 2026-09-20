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
| [DES-DOG-001](DES-DOG-001-leash-walking.md) | High | リード散歩の操作・行動・同期を設計する。片手 3 匹・合計 6 匹を基準に未決定事項を詰める | wip |
| [IMP-DOG-002](IMP-DOG-002-leash-walking.md) | High | 基本実装・PCVR体験好評。上限・競合・退出・途中参加などの項目別確認と修正 | wip |
| [IMP-DOG-003](IMP-DOG-003-walking-polish.md) | High | 次の実装: 接続の分かりやすさ、犬の距離・動き、首輪・先端を磨く | todo |
| [IMP-ENV-001](IMP-ENV-001-hillside-polish.md) | Medium | 負荷の初回測定後、道・木陰・見晴らし・休憩場所を磨く | todo |
| [DES-WLD-001](DES-WLD-001-round-trip.md) | Medium | 古民家からの出発・帰還と状態の扱いを設計する | todo |

着手順: IMP-DOG-003を次の比較実装とし、IMP-DOG-001/002・VER-ENV-001の実機確認を並行する。負荷と地形の判断をDES-ENV-001へ返し、その後にIMP-ENV-001、DES-WLD-001を進める。DES-DOG-001は各段階の採用値・おしゃれ・機能範囲をまとめる。改善意図と段階ごとの判断は [開発全体像](../overview.md) §3。

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
