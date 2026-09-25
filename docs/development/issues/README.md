# Issue の運用

> 課題の置き場と運用ルール。**一覧は [issue_summary.md](issue_summary.md)** (生成ファイル。手で編集しない)。
>
> 着手順と現在地の説明は [開発全体像](../overview.md) §3 が正本 (一覧は実行キューと状態だけを持つ)。
> 2026-09-20〜23 の着手順メモは git 履歴 (`ff76602` 時点の issue_summary.md) を参照。

## 原則

- **Issue は全部 `docs/development/issues/` 直下に置く。** 状態別のサブディレクトリは作らない
  (完了しても移動しない。移動のたびに相対リンクがずれる)
- **状態は frontmatter の `status` だけが正本。** 一覧 (`issue_summary.md`) は frontmatter から生成する
- 状態・概要・次にやることを変えたら、個票の frontmatter を書き換えて一覧を生成し直す

```bash
node scripts/check-issues.js --write   # issue_summary.md を生成し直す
node scripts/check-issues.js           # 検査 (一覧が古いと NG)
```

## 採番

```bash
node scripts/check-issues.js --next           # 既存の prefix ごとに次の ID
node scripts/check-issues.js --next IMP GM    # IMP-GM の次の ID だけ (新しいシステムなら -001)
```

ID は `{BUG|IMP|DES|VER}-{システム 2〜3 文字}-{3 桁}`。ファイル名は `{ID}-{英小文字の短い名前}.md`、
H1 見出しにも ID を入れる。**完了した Issue もファイルを消さない** (採番はファイルから計算する)。

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
status: todo        # todo / wip / review / hold / done
priority: High      # High / Medium / Low
summary: 一覧に出す 1 行 (任意。無ければ H1 直後の最初の段落の先頭 120 字)
next: 次にやること 1 行 (任意)
updated: 2026-01-01 # 任意。完了の並び順に使う
decided: 2026-01-01 # 任意。設計判断を決めた日
---
```

`id` / `type` / `system` / `status` / `priority` は必須。値に `: ` や先頭の記号 (`[` `*` `` ` `` など) を含むときは `"..."` で囲む。

## 状態の意味

| status | 意味 | 一覧での扱い |
|--------|------|-------------|
| `todo` | 未着手 | 実行キュー |
| `wip` | 対応中 | 実行キュー (先頭) |
| `review` | 実装は終わり、**まだ確かめていない** (Play・実機・ユーザー確認待ち) | 実行キュー。`next` に「何を確かめれば完了か」を書く |
| `hold` | **設計判断として今は着手しない** (判断材料が揃っていない / 今やると過剰) | 実行キュー (末尾)。`next` に再検討のトリガーを書く |
| `done` | 完了 | 完了の表 (`updated` / `decided` の新しい順) |

### 確認待ち (review)

1. 確認方法は Issue 本文の「完了条件」に書かれているものを使う
   (ここで新しく考え直さない。考え直したくなったら完了条件の方が誤っている)
2. OK なら `status: done`、NG なら `status: wip` (または `todo`) に戻す
3. NG だった場合、**何がズレたかを Issue へ追記してから**戻す
   — 期待値が誤っていたのか、実装が誤っていたのかを区別して残す

AI とユーザーの確認分担は `vrc-verification` Skill §1 と各プロジェクトの CLAUDE.md (検証ワークフローの例外) が正本。ここでは上限を定めない。`review` は Play・実機・ユーザー確認の引き継ぎ待ちであることが多い。

### 保留 (hold)

**「再検討のトリガー」を必ず書く** (`next` と本文)。
条件を書かずに保留すると、二度と見返されないまま公開まで行く。
「M4 に着手したとき」「2 人以上で遊ぶようになったとき」のように、**観測できる出来事**で書く。

**却下 (スコープ外) と保留は別物。**
「設計判断として不適」なものは Issue にせず `game-design.md` の「入れないもの」へ理由つきで書く。

### 完了 (done)

- 本文の末尾「## 完了記録」に、完了日と**どう確かめたか**を残す — 次に同じ症状が出たとき、
  検証方法が分かっていることの方が価値が高い
- **想定と実際がずれた点を書く** — 着手前の見立てが外れた記録は、次の見積もりの精度に直結する
- 一般化できる知見を得たら、**共有リポジトリの Skill へ反映してから**完了にする
  (Issue に書いて満足すると、次のワールドで同じ失敗をする)

## 状態を変える手順

1. 個票の frontmatter の `status` を書き換える (必要なら `next` / `summary` / `updated` も)
2. `node scripts/check-issues.js --write` で一覧を生成し直す
3. 検査を 2 本とも通す
   ```bash
   node scripts/check-issues.js
   node scripts/check-doc-links.js
   ```
4. 個票と `issue_summary.md` を一緒に `git add` してコミット

ファイルは動かさないので、リンクの付け替えは要らない。
