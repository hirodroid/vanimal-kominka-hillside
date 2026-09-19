#!/usr/bin/env node
/*
 * Issue の frontmatter とディレクトリ・サマリの整合を検査する。
 *
 *   node scripts/check-issues.js         不整合があれば exit 1
 *   node scripts/check-issues.js --next  次に使える連番だけ出す
 *
 * 検査するもの:
 *   1. frontmatter が存在し、必須キー (id / type / system / status) がそろっている
 *   2. id とファイル名・H1 見出しが一致する
 *   3. status と置き場所 (ディレクトリ) が一致する
 *   4. id が重複していない
 *   5. status に対応するサマリに 1 行だけ載っている (載せ忘れ / 二重掲載の検出)
 *   6. priority が frontmatter とサマリの「重要度」欄で食い違っていない
 *   7. (IMP-INF-019 K3) 正本の入口 (AI_INDEX / design_index / overview の表の 1 列目) が
 *      done の Issue を指していない — Issue は正本になれない。done にする前に決定事項を設計書へ抜く
 *   8. (IMP-INF-019 K2) plans/ の計画書: 冒頭の "> 対象: IMP-XXX-NNN" (無ければファイル名の ID) から
 *      対象 Issue を読み、全部 done かつ正本の入口から未参照なら WARN (archive 候補)。
 *      plans/archive/ の計画書が現役の入口 (AI_INDEX / design_index) から参照されていたら NG (overview の履歴からは可)
 *
 * 最後に **次に使える連番** を prefix ごとに出す (archive まで含めて採番するため)。
 */

const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const ISS = path.join(ROOT, 'docs/development/issues');
const NEXT_ONLY = process.argv.includes('--next');

/** status -> [ディレクトリ (ISS 相対), その状態を載せるサマリ] */
const PLACES = {
  todo:   { dir: '.', summary: 'issue_summary.md' },
  wip:    { dir: '.', summary: 'issue_summary.md' },
  review: { dir: 'review', summary: 'issue_summary.md' }, // 実行キューは TOP が持つ
  hold:   { dir: 'hold', summary: 'issue_summary.md' },
  done:   { dir: 'archive', summary: 'archive/archive_summary.md' },
};
const SUMMARY_FILES = new Set(['issue_summary.md', 'review/review_summary.md', 'hold/hold_summary.md', 'archive/archive_summary.md']);
const ID_RE = /^(BUG|IMP|DES|VER)-([A-Z]{2,3})-(\d{3})$/;

const errors = [];
const err = (file, msg) => errors.push(`${file}\n        ${msg}`);

/**
 * 表の本体行だけを返す。引用 (`> |`) の中の表は「判断の記録」なので数えない。
 * 区切り行 (|---|) も落とす。
 */
const tableRows = text => text.split('\n')
  .filter(l => l.startsWith('|') && !/^\|[\s:|-]*\|?$/.test(l));

/** 行の 1 セル目。ID は 1 セル目にあるものだけを「その Issue の行」と見なす。 */
const firstCell = row => row.split('|')[1] || '';

function listIssueFiles(dir = ISS, base = '') {
  const out = [];
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const rel = base ? `${base}/${e.name}` : e.name;
    if (e.isDirectory()) out.push(...listIssueFiles(path.join(dir, e.name), rel));
    else if (e.name.endsWith('.md') && !SUMMARY_FILES.has(rel)) out.push(rel);
  }
  return out;
}

function parseFrontmatter(text) {
  const m = text.replace(/^﻿/, '').match(/^---\n([\s\S]*?)\n---\n/);
  if (!m) return null;
  const out = {};
  for (const line of m[1].split('\n')) {
    const i = line.indexOf(':');
    if (i === -1) continue;
    out[line.slice(0, i).trim()] = line.slice(i + 1).trim().replace(/^["']|["']$/g, '');
  }
  return out;
}

const files = listIssueFiles();
const issues = [];
const seen = new Map();

for (const rel of files) {
  const text = fs.readFileSync(path.join(ISS, rel), 'utf8');
  const fm = parseFrontmatter(text);
  if (!fm) { err(rel, 'frontmatter (--- で囲んだ YAML) が無い'); continue; }

  for (const k of ['id', 'type', 'system', 'status']) {
    if (!fm[k]) err(rel, `frontmatter に ${k} が無い`);
  }
  if (!fm.id) continue;

  const idm = fm.id.match(ID_RE);
  if (!idm) { err(rel, `id "${fm.id}" が {BUG|IMP|DES|VER}-{システム}-{3桁} の形式でない`); continue; }
  const [, type, system] = idm;

  if (fm.type !== type) err(rel, `type: ${fm.type} が id (${fm.id}) と食い違う`);
  if (fm.system !== system) err(rel, `system: ${fm.system} が id (${fm.id}) と食い違う`);

  const base = path.posix.basename(rel);
  if (!base.startsWith(fm.id + '-')) err(rel, `ファイル名が id (${fm.id}) で始まっていない`);

  const h1 = (text.replace(/^﻿/, '').match(/^# (.*)$/m) || [])[1] || '';
  if (!h1.includes(fm.id)) err(rel, `H1 見出しに id (${fm.id}) が入っていない — "${h1.slice(0, 40)}"`);

  const place = PLACES[fm.status];
  if (!place) { err(rel, `status: ${fm.status} は未知 (${Object.keys(PLACES).join(' / ')} のいずれか)`); continue; }
  const dir = path.posix.dirname(rel);
  if (dir !== place.dir) err(rel, `status: ${fm.status} なら ${place.dir === '.' ? 'issues/ 直下' : place.dir + '/'} に置く (現在 ${dir === '.' ? '直下' : dir + '/'})`);

  if (seen.has(fm.id)) err(rel, `id ${fm.id} が ${seen.get(fm.id)} と重複している`);
  else seen.set(fm.id, rel);

  issues.push({ rel, fm, type, system, seq: Number(idm[3]) });
}

// --- サマリ掲載の検査 -------------------------------------------------
const summaryText = {};
for (const s of SUMMARY_FILES) {
  const p = path.join(ISS, s);
  if (fs.existsSync(p)) summaryText[s] = fs.readFileSync(p, 'utf8');
}

for (const { rel, fm } of issues) {
  const place = PLACES[fm.status];
  const text = summaryText[place.summary];
  if (text === undefined) { err(rel, `サマリ ${place.summary} が無い`); continue; }

  const rows = tableRows(text).filter(r => firstCell(r).includes(fm.id));
  if (rows.length === 0) err(rel, `${place.summary} の表に ${fm.id} の行が無い`);
  else if (rows.length > 1) err(rel, `${place.summary} に ${fm.id} の行が ${rows.length} 本ある (二重掲載)`);
  else if (fm.priority) {
    const cells = rows[0].split('|').map(c => c.replace(/\*/g, '').trim());
    if (cells.some(c => /^(High|Medium|Low)$/.test(c)) && !cells.includes(fm.priority)) {
      err(rel, `priority: ${fm.priority} が ${place.summary} の重要度欄と食い違う (表: ${cells.filter(c => /^(High|Medium|Low)$/.test(c)).join(', ')})`);
    }
  }
}

// --- 7. 正本ガード / 8. plans のライフサイクル (IMP-INF-019) ----------------
const DOCS = path.join(ROOT, 'docs');
const TIER_FILES = ['AI_INDEX.md', 'development/design/design_index.md', 'development/overview.md']
  .map(f => path.join(DOCS, f)).filter(f => fs.existsSync(f));
const statusOf = new Map(issues.map(i => [i.fm.id, i.fm.status]));
const norm = p => p.split(path.sep).join('/');
/** 表の 1 列目にある最初のリンク先 (ファイルからの相対) を絶対パスで返す。 */
function firstCellLinks(file) {
  const out = [];
  const dir = path.dirname(file);
  for (const row of tableRows(fs.readFileSync(file, 'utf8'))) {
    const m = firstCell(row).match(/\]\(([^)#]+)(?:#[^)]*)?\)/);
    if (m) out.push({ target: norm(path.resolve(dir, m[1])), row: firstCell(row).trim().slice(0, 60) });
  }
  return out;
}
const tierLinks = TIER_FILES.flatMap(f => firstCellLinks(f).map(l => ({ ...l, from: norm(path.relative(ROOT, f)) })));
const issueByPath = new Map(issues.map(i => [norm(path.join(ISS, i.rel)), i]));
for (const l of tierLinks) {
  const iss = issueByPath.get(l.target);
  if (iss && iss.fm.status === 'done')
    err(l.from, `正本の入口が done の Issue を指している: ${l.row} → ${iss.fm.id}\n        Issue は正本になれない。決定事項を設計書 (docs/development/design/) へ抜き、行をそちらへ向ける`);
}
const PLANS = path.join(DOCS, 'plans');
const warns = [];
if (fs.existsSync(PLANS)) {
  const tierText = TIER_FILES.map(f => fs.readFileSync(f, 'utf8')).join('\n');
  const referenced = base => tierText.includes(base);
  const idsOf = (text, base) => {
    const m = text.replace(/^\uFEFF/, '').match(/^>\s*対象[:：]\s*(.+)$/m);
    const src = m ? m[1] : base.toUpperCase();
    return [...new Set([...src.matchAll(/(BUG|IMP|DES|VER)-([A-Z]{2,3})-(\d{3})/g)].map(x => x[0]))];
  };
  for (const name of fs.readdirSync(PLANS).filter(n => n.endsWith('.md') && n !== 'README.md')) {
    const ids = idsOf(fs.readFileSync(path.join(PLANS, name), 'utf8'), name);
    const known = ids.filter(id => statusOf.has(id));
    if (!known.length) continue;
    if (known.every(id => statusOf.get(id) === 'done') && !referenced(name))
      warns.push(`docs/plans/${name} — 対象 ${known.join(', ')} は全部 done で、正本の入口から参照されていない (archive 候補)`);
  }
  const ARCH = path.join(PLANS, 'archive');
  if (fs.existsSync(ARCH)) {
    // overview は履歴 (§3 の経緯や §5) から archive を指してよい。落とすのは現役の入口 2 本だけ (Codex 指摘 2026-09-13)
    const entryText = TIER_FILES.filter(f => !f.endsWith('overview.md')).map(f => fs.readFileSync(f, 'utf8')).join('\n');
    for (const name of fs.readdirSync(ARCH).filter(n => n.endsWith('.md'))) {
      if (entryText.includes(name)) err(`docs/plans/archive/${name}`, '現役の入口 (AI_INDEX / design_index) から参照されている。archive の計画書は overview の履歴からだけ指す');
    }
  }
}

// --- 出力 -------------------------------------------------------------
if (!NEXT_ONLY) {
  for (const e of errors) console.log(`NG ${e}`);
  for (const w of warns) console.log(`WARN ${w}`);
  const byStatus = {};
  for (const { fm } of issues) byStatus[fm.status] = (byStatus[fm.status] || 0) + 1;
  console.log(`\n${issues.length} issues — ` + Object.entries(byStatus).map(([k, v]) => `${k}:${v}`).join(' / ') + (warns.length ? ` / plans の archive 候補 ${warns.length} 件 (WARN)` : ''));
}

// 採番はファイルのある Issue だけでは足りない。
// 完了済みには個別ファイルを作らなかったもの (BUG-UI-002 など) があり、
// サマリの表にしか存在しない。それを見落とすと ID が衝突する。
const next = {};
const bump = (type, system, seq) => {
  const k = `${type}-${system}`;
  next[k] = Math.max(next[k] || 0, seq);
};
for (const { type, system, seq } of issues) bump(type, system, seq);
let fromSummaryOnly = 0;
for (const text of Object.values(summaryText)) {
  for (const row of tableRows(text)) {
    const m = firstCell(row).match(/(BUG|IMP|DES|VER)-([A-Z]{2,3})-(\d{3})/);
    if (!m) continue;
    if (!seen.has(m[0])) fromSummaryOnly++;
    bump(m[1], m[2], Number(m[3]));
  }
}
console.log(`\n次に使える連番 (個別ファイル無しの ${fromSummaryOnly} 件も含めて採番):`);
for (const k of Object.keys(next).sort()) {
  console.log(`        ${k}-${String(next[k] + 1).padStart(3, '0')}`);
}

if (!NEXT_ONLY) console.log(errors.length ? `\n${errors.length} 件の不整合` : '\nall consistent');
process.exit(!NEXT_ONLY && errors.length ? 1 : 0);
