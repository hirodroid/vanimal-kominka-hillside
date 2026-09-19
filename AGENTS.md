# KominkaHillside — Codex向け補足

[CLAUDE.md](CLAUDE.md) を共通指示として読み、[docs/AI_INDEX.md](docs/AI_INDEX.md) から作業に必要な資料だけ辿る。
共通ルール・検査コマンドをここに複製しない。Claudeの権限設定やhookがCodexにも適用されるとは仮定しない。

- `.agents/skills/` と `.claude/skills/` は `C:/work/unity/vrc-world-agent/claude-skills/` の同じ原本へのジャンクション。本文を複製しない。
- 公開対象は [config/agent-skills.json](config/agent-skills.json)。既存のClaude側18スキルをCodexにも公開する。
- 再設定はプロジェクトルートで `powershell -NoProfile -ExecutionPolicy Bypass -File C:/work/unity/vrc-world-agent/scripts/setup-codex.ps1 -Project .`。確認のみは `-DryRun`。
- 初回のリンク作成は通常ユーザーのPowerShellまたは承認済み権限拡張で行う。詳細は [共有の接続手順](../../vrc-world-agent/docs/codex-setup.md)。一覧の認識は新しいセッションで確認する。
- Claude固有のツール名は利用可能な同等ツールに読み替える。Unity APIは共通指示どおり `scripts/unity-api.sh` 経由とし、操作前に接続先プロジェクトを確認する。
- Windowsの `bash` はWSL版を指すため、この環境では `C:/Program Files/Git/bin/bash.exe` を明示する。接続確認は Synaptic AI Pro 導入後に `scripts/unity-api.sh` で `Application.dataPath` が本プロジェクトの `Assets` であることを見る。
- Codexが実装するとき、Skill内の「Claudeが実装、Codexがレビュー」という担当の前提は適用しない。
- 独立レビューは担当と対象を明記し、セルフレビューを第三者レビューと呼ばない。レビュー担当は追加レビューを再帰的に起動しない。
