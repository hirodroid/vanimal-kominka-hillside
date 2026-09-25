#!/usr/bin/env node
/*
 * Issue の検査とサマリ生成。**frontmatter の status が唯一の状態**。
 * Issue は全部 docs/development/issues/ 直下に置く (状態別のサブディレクトリは作らない)。
 *
 *   node scripts/check-issues.js                 検査 (不整合があれば exit 1)
 *   node scripts/check-issues.js --write         issue_summary.md を生成し直す
 *   node scripts/check-issues.js --next          次に使える ID を prefix ごとに出す
 *   node scripts/check-issues.js --next IMP DOG  IMP-DOG の次の ID だけ出す
 *   node scripts/check-issues.js --json          Issue 一覧を JSON で出す (他ツール用)
 *
 * 検査するもの:
 *   1. frontmatter があり、必須キー (id / type / system / status / priority) がそろっている
 *   2. status (todo | wip | review | hold | done) と priority (High | Medium | Low) が既定値
 *   3. id の形式、type / system との一致、ファイル名と H1 見出しに id が入っている
 *   4. id が重複していない
 *   5. 任意キー updated / decided が YYYY-MM-DD
 *   6. issues/ 配下のサブディレクトリに .md が無い (状態はディレクトリで表さない)。
 *      サブディレクトリに残った .md の id も重複検査と採番には数える (id の再利用を防ぐ)
 *   7. issue_summary.md が生成結果と一致する (手で編集しない。ずれたら --write)
 *   8. (IMP-INF-019 K3) 正本の入口 (AI_INDEX / design_index / overview の表の 1 列目) が
 *      done の Issue を指していない — Issue は正本になれない。done にする前に決定事項を設計書へ抜く
 *   9. (IMP-INF-019 K2) plans/ の計画書: 冒頭の "> 対象: IMP-XXX-NNN" (無ければファイル名の ID) の
 *      Issue が全部 done かつ正本の入口から未参照なら WARN (archive 候補)。
 *      plans/archive/ の計画書が現役の入口 (AI_INDEX / design_index) から参照されていたら NG
 *
 * 任意キー: summary (一覧の 1 行。無ければ H1 直後の最初の段落の先頭 120 字)、
 *           next (次にやること 1 行)、updated / decided (YYYY-MM-DD。完了の並び順に使う)。
 * --next / --write は検査 1〜6・8・9 にエラーが 1 件でもあれば、採番を出さず・サマリを書かずに exit 1。
 * require('./check-issues.js') すると frontmatter の解析関数だけを返す (migrate-issues-flat.js が使う)。
 * 運用ルールは docs/development/issues/README.md。Node 組み込みモジュールだけで動く。
 */

const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const ISS = path.join(ROOT, 'docs/development/issues');
const SUMMARY = path.join(ISS, 'issue_summary.md');
const NON_ISSUE = new Set(['issue_summary.md', 'README.md']);

const args = process.argv.slice(2);
const WRITE = args.includes('--write');
const JSON_OUT = args.includes('--json');
const NEXT_AT = args.indexOf('--next');
const NEXT = NEXT_AT !== -1;
const NEXT_KEY = NEXT && args[NEXT_AT + 1] && args[NEXT_AT + 2] && !args[NEXT_AT + 1].startsWith('--')
  ? `${args[NEXT_AT + 1].toUpperCase()}-${args[NEXT_AT + 2].toUpperCase()}` : null;

const STATUSES = ['wip', 'review', 'todo', 'hold', 'done'];  // 一覧の並び順
const STATUS_LABEL = { wip: '対応中', review: '確認待ち', todo: '未着手', hold: '保留', done: '完了' };
const PRIORITIES = ['High', 'Medium', 'Low'];
const REQUIRED = ['id', 'type', 'system', 'status', 'priority'];
const ID_RE = /^(BUG|IMP|DES|VER)-([A-Z]{2,3})-(\d{3})$/;
const DATE_RE = /^\d{4}-\d{2}-\d{2}$/;

const errors = [];
const warns = [];
const err = (file, msg) => errors.push(`${file}\n        ${msg}`);
const norm = p => p.split(path.sep).join('/');
const readText = p => fs.readFileSync(p, 'utf8').replace(/^﻿/, '').replace(/\r\n/g, '\n');

/** 表の本体行 (区切り行は除く)。引用 (`> |`) の中の表は数えない。 */
const tableRows = text => text.split('\n').filter(l => l.startsWith('|') && !/^\|[\s:|-]*\|?$/.test(l));
const firstCell = row => row.split('|')[1] || '';

/**
 * frontmatter を平たい key: value として読む。値の前後の引用符は外し ("..." の中の \" は戻す)、
 * 前後の空白を落とす。BOM と CRLF は無視する。frontmatter が無ければ null。
 */
function parseFrontmatter(text) {
  const m = text.replace(/^﻿/, '').replace(/\r\n/g, '\n').match(/^---\n([\s\S]*?)\n---\n/);
  if (!m) return null;
  const out = {};
  for (const line of m[1].split('\n')) {
    const i = line.indexOf(':');
    if (i === -1 || /^\s/.test(line)) continue;
    let v = line.slice(i + 1).trim();
    if (/^".*"$/.test(v)) v = v.slice(1, -1).replace(/\\"/g, '"').replace(/\\\\/g, '\\');
    else if (/^'.*'$/.test(v)) v = v.slice(1, -1).replace(/''/g, "'");
    else v = v.replace(/\s+#.*$/, '');  // 行末コメント (status: todo  # ...)
    out[line.slice(0, i).trim()] = v.trim();
  }
  return out;
}

/** H1 の後の最初の段落 (引用記号は外す) を 1 行にする。フェンス付きコードブロックは中身ごと飛ばす。 */
function firstParagraph(text) {
  const body = text.replace(/^﻿/, '').replace(/\r\n/g, '\n').replace(/^---\n[\s\S]*?\n---\n/, '');
  const lines = body.split('\n');
  let i = lines.findIndex(l => /^# /.test(l));
  if (i === -1) return '';
  const para = [];
  let fence = null; // 開いているフェンス (``` / ~~~ とその長さ)
  for (i += 1; i < lines.length; i++) {
    const l = lines[i].replace(/^>\s?/, '').trim();
    const f = l.match(/^(`{3,}|~{3,})(.*)$/);
    if (fence) {
      if (f && f[1][0] === fence[0] && f[1].length >= fence.length && !f[2].trim()) fence = null;
      continue;
    }
    if (f) { if (para.length) break; fence = f[1]; continue; }
    if (!l) { if (para.length) break; continue; }
    if (/^(#|\||---)/.test(l)) { if (para.length) break; continue; }
    para.push(l);
  }
  return para.join(' ');
}

// require されたときは解析関数だけを渡し、検査・出力はしない
if (require.main !== module) {
  module.exports = { parseFrontmatter, firstParagraph, STATUSES, PRIORITIES, ID_RE, DATE_RE };
  return;
}
const clip = (s, n) => ([...s].length > n ? [...s].slice(0, n).join('') + '…' : s);
const cell = s => (s || '').replace(/\r?\n/g, ' ').replace(/\|/g, '\\|').trim();

// --- Issue を読む -------------------------------------------------------
const issues = [];
const seen = new Map();   // id → rel (直下とサブディレクトリの両方。重複検査用)
const numbered = [];      // 採番に数える {type, system, seq} (サブディレクトリに残った分も含む)
function register(id, rel) {
  const m = id.match(ID_RE);
  if (!m) return;
  if (seen.has(id)) err(rel, `id ${id} が ${seen.get(id)} と重複している`);
  else seen.set(id, rel);
  numbered.push({ type: m[1], system: m[2], seq: Number(m[3]) });
}

for (const e of fs.existsSync(ISS) ? fs.readdirSync(ISS, { withFileTypes: true }) : []) {
  if (e.isDirectory()) {
    const walk = (dir, rel) => {
      for (const c of fs.readdirSync(dir, { withFileTypes: true })) {
        const r = `${rel}/${c.name}`;
        if (c.isDirectory()) { walk(path.join(dir, c.name), r); continue; }
        if (!c.name.endsWith('.md')) continue;
        err(r, 'issues/ 配下のサブディレクトリに .md がある。状態は frontmatter の status だけで表し、Issue は全部 issues/ 直下に置く');
        // 置き場所が違っても id は使用済み。重複検査と採番には数える
        const fm = parseFrontmatter(readText(path.join(dir, c.name))) || {};
        const id = fm.id && ID_RE.test(fm.id) ? fm.id : ((c.name.match(/^((?:BUG|IMP|DES|VER)-[A-Z]{2,3}-\d{3})\b/) || [])[1] || '');
        if (id) register(id, r);
      }
    };
    walk(path.join(ISS, e.name), e.name);
    continue;
  }
  if (!e.name.endsWith('.md') || NON_ISSUE.has(e.name)) continue;
  const rel = e.name;
  const text = readText(path.join(ISS, rel));
  const fm = parseFrontmatter(text);
  if (!fm) { err(rel, 'frontmatter (--- で囲んだ YAML) が無い'); continue; }

  let bad = false;
  for (const k of REQUIRED) if (!fm[k]) { err(rel, `frontmatter に ${k} が無い`); bad = true; }
  if (!fm.id) continue;

  const idm = fm.id.match(ID_RE);
  if (!idm) { err(rel, `id "${fm.id}" が {BUG|IMP|DES|VER}-{システム}-{3桁} の形式でない`); continue; }
  const [, type, system] = idm;
  if (fm.type && fm.type !== type) err(rel, `type: ${fm.type} が id (${fm.id}) と食い違う`);
  if (fm.system && fm.system !== system) err(rel, `system: ${fm.system} が id (${fm.id}) と食い違う`);
  if (!rel.startsWith(fm.id + '-')) err(rel, `ファイル名が id (${fm.id}) で始まっていない`);

  const h1 = (text.match(/^# (.*)$/m) || [])[1] || '';
  if (!h1.includes(fm.id)) err(rel, `H1 見出しに id (${fm.id}) が入っていない — "${h1.slice(0, 40)}"`);

  if (fm.status && !STATUSES.includes(fm.status)) { err(rel, `status: ${fm.status} は未知 (${STATUSES.join(' / ')} のいずれか)`); bad = true; }
  if (fm.priority && !PRIORITIES.includes(fm.priority)) { err(rel, `priority: ${fm.priority} は未知 (${PRIORITIES.join(' / ')} のいずれか)`); bad = true; }
  for (const k of ['updated', 'decided']) if (fm[k] && !DATE_RE.test(fm[k])) err(rel, `${k}: ${fm[k]} が YYYY-MM-DD でない`);

  register(fm.id, rel);

  issues.push({
    rel, fm, type, system, seq: Number(idm[3]), bad,
    summary: fm.summary || clip(firstParagraph(text), 120),
    date: [fm.updated, fm.decided, fm.completed].filter(d => d && DATE_RE.test(d)).sort().pop() || '',
  });
}

// --- サマリの生成 -------------------------------------------------------
const byId = (a, b) => a.fm.id.localeCompare(b.fm.id);
const pri = i => PRIORITIES.indexOf(i.fm.priority);

function renderSummary() {
  const ok = issues.filter(i => !i.bad);
  const queue = ok.filter(i => i.fm.status !== 'done')
    .sort((a, b) => STATUSES.indexOf(a.fm.status) - STATUSES.indexOf(b.fm.status) || pri(a) - pri(b) || byId(a, b));
  const done = ok.filter(i => i.fm.status === 'done')
    .sort((a, b) => (b.date || '').localeCompare(a.date || '') || byId(a, b));
  const count = STATUSES.map(s => `${STATUS_LABEL[s]} ${ok.filter(i => i.fm.status === s).length}`).join(' / ');
  const link = i => `[${i.fm.id}](${i.rel})`;
  const out = [
    '# Issue サマリ',
    '',
    '> 生成ファイル。手で編集しない。運用は [README.md](README.md)。',
    '> 更新は `node scripts/check-issues.js --write` (状態・概要・次は各 Issue の frontmatter が正本)。',
    '',
    `${ok.length} 件 — ${count}`,
    '',
    '## 実行キュー',
    '',
    '| ID | 重要度 | 概要 | 状態 | 次 |',
    '|----|--------|------|------|----|',
    ...queue.map(i => `| ${link(i)} | ${i.fm.priority} | ${cell(i.summary)} | ${i.fm.status} | ${cell(i.fm.next)} |`),
    '',
    '## 完了',
    '',
    '| ID | 重要度 | 概要 | 日付 |',
    '|----|--------|------|------|',
    ...done.map(i => `| ${link(i)} | ${i.fm.priority} | ${cell(i.summary)} | ${i.date} |`),
    '',
  ];
  return out.join('\n');
}

// --- 8. 正本ガード / 9. plans のライフサイクル (IMP-INF-019) ----------------
const DOCS = path.join(ROOT, 'docs');
const TIER_FILES = ['AI_INDEX.md', 'development/design/design_index.md', 'development/overview.md']
  .map(f => path.join(DOCS, f)).filter(f => fs.existsSync(f));
const statusOf = new Map(issues.map(i => [i.fm.id, i.fm.status]));
function firstCellLinks(file) {
  const out = [];
  const dir = path.dirname(file);
  for (const row of tableRows(readText(file))) {
    const m = firstCell(row).match(/\]\(([^)#]+)(?:#[^)]*)?\)/);
    if (m) out.push({ target: norm(path.resolve(dir, m[1])), row: firstCell(row).trim().slice(0, 60) });
  }
  return out;
}
const issueByPath = new Map(issues.map(i => [norm(path.join(ISS, i.rel)), i]));
for (const f of TIER_FILES) {
  for (const l of firstCellLinks(f)) {
    const iss = issueByPath.get(l.target);
    if (iss && iss.fm.status === 'done')
      err(norm(path.relative(ROOT, f)), `正本の入口が done の Issue を指している: ${l.row} → ${iss.fm.id}\n        Issue は正本になれない。決定事項を設計書 (docs/development/design/) へ抜き、行をそちらへ向ける`);
  }
}
const PLANS = path.join(DOCS, 'plans');
if (fs.existsSync(PLANS)) {
  const tierText = TIER_FILES.map(readText).join('\n');
  const idsOf = (text, base) => {
    const m = text.match(/^>\s*対象[:：]\s*(.+)$/m);
    const src = m ? m[1] : base.toUpperCase();
    return [...new Set([...src.matchAll(/(BUG|IMP|DES|VER)-([A-Z]{2,3})-(\d{3})/g)].map(x => x[0]))];
  };
  for (const name of fs.readdirSync(PLANS).filter(n => n.endsWith('.md') && n !== 'README.md')) {
    const known = idsOf(readText(path.join(PLANS, name)), name).filter(id => statusOf.has(id));
    if (known.length && known.every(id => statusOf.get(id) === 'done') && !tierText.includes(name))
      warns.push(`docs/plans/${name} — 対象 ${known.join(', ')} は全部 done で、正本の入口から参照されていない (archive 候補)`);
  }
  const ARCH = path.join(PLANS, 'archive');
  if (fs.existsSync(ARCH)) {
    // overview は履歴から archive を指してよい。落とすのは現役の入口 2 本だけ (Codex 指摘 2026-09-13)
    const entryText = TIER_FILES.filter(f => !f.endsWith('overview.md')).map(readText).join('\n');
    for (const name of fs.readdirSync(ARCH).filter(n => n.endsWith('.md'))) {
      if (entryText.includes(name)) err(`docs/plans/archive/${name}`, '現役の入口 (AI_INDEX / design_index) から参照されている。archive の計画書は overview の履歴からだけ指す');
    }
  }
}

// --- 採番 ---------------------------------------------------------------
const next = {};
for (const { type, system, seq } of numbered) {
  const k = `${type}-${system}`;
  next[k] = Math.max(next[k] || 0, seq);
}
const nextId = k => `${k}-${String((next[k] || 0) + 1).padStart(3, '0')}`;

// --- 出力 ---------------------------------------------------------------
/** 検査エラーがあれば、採番・サマリ書き込みをせずにエラーだけ出して終わる。 */
function refuseIfErrors(what) {
  if (!errors.length) return;
  for (const e of errors) console.error(`NG ${e}`);
  console.error(`\n${errors.length} 件の不整合があるので ${what}。直してから再実行する`);
  process.exit(1);
}

if (NEXT) {
  refuseIfErrors('採番しない (id の重複・再利用を避けるため)');
  if (NEXT_KEY) {
    if (!/^(BUG|IMP|DES|VER)-[A-Z]{2,3}$/.test(NEXT_KEY)) { console.error(`--next の種別・システムが不正: ${NEXT_KEY}`); process.exit(1); }
    console.log(nextId(NEXT_KEY));
  } else {
    console.log('次に使える連番:');
    for (const k of Object.keys(next).sort()) console.log(`        ${nextId(k)}`);
    console.log('(新しいシステムの 1 本目は `--next IMP XX` で -001 が出る)');
  }
  process.exit(0);
}

if (JSON_OUT) {
  const list = issues.map(i => ({
    id: i.fm.id, type: i.type, system: i.system, status: i.fm.status, priority: i.fm.priority,
    summary: i.summary, next: i.fm.next || '', updated: i.fm.updated || '', decided: i.fm.decided || '',
    file: norm(path.relative(ROOT, path.join(ISS, i.rel))),
  })).sort((a, b) => a.id.localeCompare(b.id));
  console.log(JSON.stringify({ issues: list, errors: errors.length }, null, 2));
  process.exit(errors.length ? 1 : 0);
}

const generated = renderSummary();
if (WRITE) {
  refuseIfErrors('issue_summary.md を書き換えない');
  fs.writeFileSync(SUMMARY, generated);
  console.log(`issue_summary.md を生成 — ${issues.filter(i => !i.bad).length} 件`);
} else {
  const current = fs.existsSync(SUMMARY) ? readText(SUMMARY) : null;
  if (current === null) err('issue_summary.md', '無い。`node scripts/check-issues.js --write` を実行');
  else if (current !== generated) err('issue_summary.md', '生成結果と一致しない (Issue の frontmatter を変えたか、手で編集した)。`node scripts/check-issues.js --write` を実行');
}

for (const e of errors) console.log(`NG ${e}`);
for (const w of warns) console.log(`WARN ${w}`);
const byStatus = {};
for (const { fm } of issues) byStatus[fm.status] = (byStatus[fm.status] || 0) + 1;
console.log(`\n${issues.length} issues — ` + Object.entries(byStatus).map(([k, v]) => `${k}:${v}`).join(' / ') +
  (warns.length ? ` / plans の archive 候補 ${warns.length} 件 (WARN)` : ''));
console.log(errors.length ? `${errors.length} 件の不整合` : 'all consistent');
process.exit(errors.length ? 1 : 0);
