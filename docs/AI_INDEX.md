# KominkaHillside -- AI Document Index

> AI エージェント用ナビゲーション入口。
> 編集・実装の前に必ずこのファイルから読み始めること。

## Reading Rules

1. **TIER 0 は常に最初に読む** -- CLAUDE.md (root) + このファイル
2. **タスクに関連する TIER 1 だけ読む** -- 全部読まない
3. **TIER 2 はドメインが関わるときだけ参照**
4. **体験の意図・見た目の判断は core-concept.md、仕様判断は game-design.md に立ち返る**

---

## Document Tree

### TIER 0: AI Gate (必読)

| File | Role |
|------|------|
| `../CLAUDE.md` | プロジェクト規約・命名規則・検証ワークフロー |
| `AI_INDEX.md` | このファイル。Tier 分類 + Reading Rules + 不変条件 |

### TIER 1: Authoritative (Single Source of Truth)

| File | Domain | Role |
|------|--------|------|
| `core-concept.md` | 企画・ビジュアル | 体験の核とビジュアル方針 (色・形・素材・光・UI・例外) の正本 |
| `game-design.md` | 仕様 | コアコンセプトから導く仕様・スコープの正本 |
| `world-ui-spec.md` | UI仕様 | ビジュアル方針から具体化するUIトークン・画面・状態/入力契約・検証記録。既存の同等資料があればそちらを正本にする |
| `development/overview.md` | 開発全体 | マイルストーンの進捗・完成条件 |

### TIER 2: Reference

見た目の制作・アセット選定・統合確認では `vrc-visual-direction` と `core-concept.md` の「ビジュアル方針」を先に読む。
意図と仕様が食い違ったらコアコンセプトに照らして矛盾を解消し、仕様やパレットを別資料で二重管理しない。

UI制作・改修・レビューは `vrc-udonsharp` §5 と `.claude/skills/vrc-udonsharp/reference/ui/00_START_HERE.md` (プロジェクトルート基準) から必要な章を選び、`world-ui-spec.md` の対象範囲を確認する。

| File | Domain |
|------|--------|
| `development/glossary.md` | 用語集 (ユビキタス言語) |
| `development/design/design_index.md` | 詳細設計の入口 |
| `development/issues/issue_summary.md` | 課題の入口 (状態ごとに 4 分割) |
| `development/git-workflow.md` | Git 運用ルール |
| `plans/` | 実装計画書・実機チェックリスト |
| `../.claude/skills/vrc-*/` | 共有 Skills (VRChat World 開発一般の知識・手順) |

---

## 不変条件 (Invariants)

<!-- このワールドで絶対に破ってはいけない設計上の約束をここに列挙する。
     例: 「画面は同期しない — 同期は共通条件と結果のみ」
         「スコープは game-design.md の『入れないもの』リストが正本」 -->

1.
2.

---

## 最初に着手すべきこと

<!-- 現在地と着手順をここに書く。セッションのたびに更新する。 -->
