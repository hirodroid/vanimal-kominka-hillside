#!/usr/bin/env node
// PostToolUse フック: .cs を編集したら scripts/ の該当検査を必ず通す
// (CLAUDE.md「コードを変更したら検査も通すこと」のコード化。
//  違反時は exit 2 で検査出力を Claude へ差し戻す)
// 原本: vrc-world-agent/templates/scripts/hook-check-cs.js (setup-vrc-ai.ps1 が配布)
const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

// 検査スクリプトを持たないプロジェクトにも配れるよう、存在するものだけ実行する
const CHECKS = ['check-naming.js', 'check-sync-anchors.js', 'check-consume-once.js'];

let input = '';
process.stdin.on('data', (c) => { input += c; });
process.stdin.on('end', () => {
  let filePath = '';
  try { filePath = (JSON.parse(input).tool_input || {}).file_path || ''; } catch (_) { /* 入力不正は対象外扱い */ }
  if (!/\.cs$/i.test(filePath)) return;

  const root = process.env.CLAUDE_PROJECT_DIR || path.join(__dirname, '..');
  for (const script of CHECKS) {
    if (!fs.existsSync(path.join(root, 'scripts', script))) continue;
    try {
      execSync(`node scripts/${script}`, { cwd: root, stdio: ['ignore', 'pipe', 'pipe'] });
    } catch (e) {
      process.stderr.write(String(e.stdout || '') + String(e.stderr || ''));
      process.exit(2);
    }
  }
});
