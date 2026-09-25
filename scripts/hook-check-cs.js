#!/usr/bin/env node
// PostToolUse フック (Edit|Write|Bash|PowerShell): 編集の種類に応じて scripts/ の検査を走らせる。
// 違反時は exit 2 で検査出力を Claude へ差し戻す (.claude/settings.json の hooks.PostToolUse から呼ばれる)。
//   - Edit / Write で .cs を触った         → コード検査 (下記 cs)。毎回走る
//   - Edit / Write で .md を触った         → 文書検査 (下記 md)。毎回走る
//   - Bash / PowerShell                    → コマンドからは編集対象が分からないので git status で
//                                            変更中の .cs / .md (追加・削除・rename 含む) を調べ、
//                                            .cs があればコード検査、.md があれば文書検査。
//                                            対象ファイル群の内容ハッシュが前回の成功時と同じなら
//                                            「変更なし」を 1 行出して飛ばす (読み取りだけのコマンド対策)。
//                                            ハッシュは os.tmpdir()/claude-hook-<プロジェクトパスのハッシュ>.json
// 検査の一覧: config/hook-checks.json ({ "cs": [...], "md": [...] }) があればそれを使う。
//   無ければ cs = scripts/check-all.js が実在すればそれだけ (内包する個別検査を二重に走らせない)、
//   無ければ scripts/check-*.js のうち文書向け (DOC_CHECKS) 以外で実在するもの。
//   md = check-doc-links.js と check-issues.js のうち実在するもの。
//   設定に書かれていて実在しない検査は飛ばし、飛ばしたことを 1 行出す。
// 原本: vrc-world-agent/templates/scripts/hook-check-cs.js (setup-vrc-ai.ps1 が配布)
const { execFileSync } = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');

// 文書・Skill 向けの検査。.cs 編集時の既定一覧から外す
const DOC_CHECKS = ['check-doc-links.js', 'check-issues.js', 'check-skills.js'];
const DEFAULT_MD = ['check-doc-links.js', 'check-issues.js'];
const ALL_CS = 'check-all.js';

const root = process.env.CLAUDE_PROJECT_DIR || path.join(__dirname, '..');
const scriptsDir = path.join(root, 'scripts');
const exists = (s) => fs.existsSync(path.join(scriptsDir, s));

function loadConfig() {
  const p = path.join(root, 'config', 'hook-checks.json');
  if (!fs.existsSync(p)) return {};
  try { return JSON.parse(fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, '')) || {}; } catch (e) {
    console.log(`[hook] config/hook-checks.json を読めないため既定の一覧を使う (${e.message})`);
    return {};
  }
}

function autoCsChecks() {
  if (!fs.existsSync(scriptsDir)) return [];
  if (exists(ALL_CS)) return [ALL_CS];
  return fs.readdirSync(scriptsDir).filter(f => /^check-.*\.js$/.test(f) && !DOC_CHECKS.includes(f)).sort();
}

function checksFor(kind, cfg) {
  if (kind === 'cs') return Array.isArray(cfg.cs) ? cfg.cs : autoCsChecks();
  return Array.isArray(cfg.md) ? cfg.md : DEFAULT_MD.filter(exists);
}

function run(kind, list) {
  const skipped = list.filter(s => !exists(s));
  if (skipped.length) console.log(`[hook] ${kind}: 存在しない検査を飛ばした — ${skipped.join(', ')}`);
  for (const script of list) {
    if (skipped.includes(script)) continue;
    try {
      execFileSync(process.execPath, [path.join('scripts', script)], { cwd: root, stdio: ['ignore', 'pipe', 'pipe'] });
    } catch (e) {
      process.stderr.write(`[hook] scripts/${script} が失敗 (${kind})\n` + String(e.stdout || '') + String(e.stderr || ''));
      process.exit(2);
    }
  }
}

/** 作業ツリーで変更中 (未追跡・削除・rename 含む) の .cs / .md を [{ status, file }] で返す。git が無ければ null。 */
function changedFiles() {
  let out;
  try {
    out = execFileSync('git', ['-c', 'core.quotepath=false', 'status', '--porcelain', '-z', '--untracked-files=all',
      '--', '*.cs', '*.md'], { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'], timeout: 5000 });
  } catch (_) { return null; }
  const parts = out.split('\0');
  const files = [];
  for (let i = 0; i < parts.length; i++) {
    const entry = parts[i];
    if (entry.length < 4) continue;
    const status = entry.slice(0, 2);
    files.push({ status, file: entry.slice(3) });
    // -z の rename / copy は「XY 新パス\0旧パス」。旧パスも対象に含める
    if (/[RC]/.test(status) && i + 1 < parts.length) files.push({ status: status + '<', file: parts[++i] });
  }
  return files.filter(f => /\.(cs|md)$/i.test(f.file));
}

/** 状態 + パス + 内容から作るハッシュ。削除済みは内容の代わりに印を使う。 */
function hashOf(files) {
  const h = crypto.createHash('sha1');
  for (const f of [...files].sort((a, b) => (a.file + a.status).localeCompare(b.file + b.status))) {
    h.update(f.status + '\0' + f.file + '\0');
    const p = path.join(root, f.file);
    try { h.update(fs.readFileSync(p)); } catch (_) { h.update('<missing>'); }
    h.update('\0');
  }
  return h.digest('hex');
}

const stateFile = path.join(os.tmpdir(),
  'claude-hook-' + crypto.createHash('sha1').update(path.resolve(root).toLowerCase()).digest('hex').slice(0, 16) + '.json');
function loadState() { try { return JSON.parse(fs.readFileSync(stateFile, 'utf8')); } catch (_) { return {}; } }
function saveState(hash) { try { fs.writeFileSync(stateFile, JSON.stringify({ root: path.resolve(root), hash })); } catch (_) { /* 保存失敗は次回も検査するだけ */ } }

let input = '';
process.stdin.on('data', (c) => { input += c; });
process.stdin.on('end', () => {
  let data = {};
  try { data = JSON.parse(input) || {}; } catch (_) { /* 入力不正は対象外扱い */ }
  const toolInput = data.tool_input || {};
  const toolName = data.tool_name || '';
  const filePath = toolInput.file_path || '';
  const isShell = /^(Bash|PowerShell)$/.test(toolName) || (!filePath && typeof toolInput.command === 'string');
  const cfg = loadConfig();

  if (!isShell) {
    const kind = /\.cs$/i.test(filePath) ? 'cs' : /\.md$/i.test(filePath) ? 'md' : null;
    if (kind) run(kind, checksFor(kind, cfg));
    return;
  }

  const files = changedFiles();
  if (!files || !files.length) return;
  const hash = hashOf(files);
  if (loadState().hash === hash) {
    console.log('[hook] 変更なし (.cs / .md が前回の検査時と同じ)');
    return;
  }
  if (files.some(f => /\.cs$/i.test(f.file))) run('cs', checksFor('cs', cfg));
  if (files.some(f => /\.md$/i.test(f.file))) run('md', checksFor('md', cfg));
  saveState(hash); // 失敗時 (exit 2) は保存しないので、直すまで毎回差し戻す
});
