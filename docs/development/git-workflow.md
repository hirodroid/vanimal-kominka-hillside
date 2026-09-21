# KominkaHillside -- Git 運用

---

## 除外の方針

- **購入アセットはコミットしない。** 導入したその日に `.gitignore` へ 2 行足す
  (ディレクトリと `.meta` の両方)。後からだと履歴に入って消すのが面倒になる
- **`.meta` は必ずコミットする** — Unity のアセット参照に必要
- `Library/` `Logs/` `UserSettings/` `Packages/*/` は除外 (復元される)
- `.claude/skills/vrc-*/` は共有リポジトリへのジャンクションなので除外

## ブランチ

| 用途 | 名前 |
|------|------|
| 既定 | `main` |
| 機能 | `feature/kh-{短い名前}` |

## コミット

```
[種別] 要約

- 何を変えたか
- **なぜそうしたか** (後から読む自分のために。特に「別案を捨てた理由」)
```

種別: `[feat]` `[fix]` `[refactor]` `[docs]` `[chore]`

## Unity 固有の注意

- **シーンを保存してからコミットする。** エディタ上の変更は保存するまでファイルに無い
- **`git mv` は先に実行する。** 編集してから mv すると編集分がステージされず漏れる
- シーンやプレハブを機械的に変更した後は、**保存してからファイルを grep で確認**する
  (詳細は `vrc-scene-edit-safety` スキル)

## タグ

節目で打つ。**復元点があると、実機で壊れたときに戻れる。**

```bash
git tag m1-<内容>
```

## アップロード前

- [ ] [共通制作手順](../../.claude/skills/vrc-asset-production/references/source-rights.md) に従い、新規・変更素材の対象版・用途・記録・クレジット・未解決事項を照合する

- [ ] コンソールにエラーが無い
- [ ] シーンが保存されている
- [ ] `node scripts/check-doc-links.js` と `node scripts/check-issues.js` が通る
- [ ] 診断ツール (`Tools/VRC World Tools/`) を回して high が残っていない
