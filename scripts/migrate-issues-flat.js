#!/usr/bin/env node
/*
 * 一度きりの移行: Issue の状態別ディレクトリ (review/ hold/ archive/) と手書きサマリをやめ、
 * frontmatter の status を唯一の状態にする。
 *
 *   node scripts/migrate-issues-flat.js --dry-run   予定だけ出す (何も書かない)
 *   node scripts/migrate-issues-flat.js             実行 (git mv / git rm / 書き換え)
 *
 * 手順:
 *   (a) review/ hold/ archive/ 配下の Issue を git mv で issues/ 直下へ (同名衝突は ERROR で中止)
 *   (b) git 管理下の全 .md の相対リンク (と、リポジトリ内を指す絶対パスのリンク) を、移動前の位置で
 *       解決してから移動先へ付け替える。アンカーは保持。移動したファイル自身のリンクも再計算。
 *       補助サマリへのリンクは issue_summary.md へ向ける。フェンス付きコードブロックとコードスパンは触らない
 *   (c) 手書きサマリ (issue_summary と review / hold / archive の補助サマリ全部) の行を個票へ移す:
 *       - frontmatter は check-issues.js と同じ解析 (引用符・前後空白を正規化)。status が既定値以外なら何もせず中止
 *       - 概要 → frontmatter summary (無ければ)。done は archive_summary、それ以外は issue_summary の行を優先し、
 *         採らなかった別の概要は記録に残す
 *       - review の「何を確かめれば完了か」→ next、hold の「再検討のトリガー」→ next (状態が一致するときだけ)
 *       - done の完了日 → updated (updated / decided / completed が無ければ)
 *       - 個票に無い列 (結果・備考・保留の理由・再検討のトリガー・完了日 など) → 個票の末尾
 *         「## 完了記録」(done) / 「## 保留の記録」(hold) / 「## 確認待ちの記録」(review) / 「## サマリからの移記」
 *       - 行の中の相対リンクは、その行があったサマリの位置から issues/ 直下へ付け替える
 *       - サマリの表にだけあって個票が無い ID → 最小の雛形を作り、上と同じ移記をかける (採番が衝突しないように)
 *       その後 review/review_summary.md、hold/hold_summary.md、archive/archive_summary.md を git rm
 *   (d) node scripts/check-issues.js --write で issue_summary.md を生成 (検査エラーがあれば生成されないので報告する)
 *
 * issues/README.md (運用ルール) はこのスクリプトでは作らない。無ければ最後に警告する。
 * git 管理外の .md (他セッションの作業中ファイルなど) は書き換えない。付け替え候補があれば警告だけ出す。
 */

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
// frontmatter の解析と既定値は check-issues.js と共通 (引用符・前後空白の扱いを揃える)
const { parseFrontmatter, STATUSES, PRIORITIES, DATE_RE } = require(path.join(__dirname, 'check-issues.js'));

const ROOT = path.resolve(__dirname, '..');
const ISS_REL = 'docs/development/issues';
const ISS = path.join(ROOT, ISS_REL);
const SUBS = ['review', 'hold', 'archive'];
const AUX = { review: 'review/review_summary.md', hold: 'hold/hold_summary.md', archive: 'archive/archive_summary.md' };
const DRY = process.argv.includes('--dry-run');
const TODAY = new Date().toISOString().slice(0, 10);
const ID_RE = /(BUG|IMP|DES|VER)-([A-Z]{2,3})-(\d{3})/;

const norm = p => p.split(path.sep).join('/');
const git = (...a) => execFileSync('git', a, { cwd: ROOT, encoding: 'utf8' });
const read = rel => fs.readFileSync(path.join(ROOT, rel), 'utf8');
const log = s => console.log(s);
const die = s => { console.error(`ERROR ${s}`); process.exit(1); };

// --- 0. 対象の洗い出し ----------------------------------------------------
if (!fs.existsSync(ISS)) die(`${ISS_REL} が無い`);
const moves = new Map();   // 旧 ROOT 相対 → 新 ROOT 相対
const removed = new Map(); // 消す補助サマリ (旧 ROOT 相対) → リンクの付け替え先
for (const sub of SUBS) {
  const dir = path.join(ISS, sub);
  if (!fs.existsSync(dir)) continue;
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.isDirectory()) die(`${ISS_REL}/${sub}/${e.name}/ — 想定外のサブディレクトリ。手で整理してから再実行`);
    if (!e.name.endsWith('.md')) continue;
    const from = `${ISS_REL}/${sub}/${e.name}`;
    if (`${sub}/${e.name}` === AUX[sub]) { removed.set(from, `${ISS_REL}/issue_summary.md`); continue; }
    const to = `${ISS_REL}/${e.name}`;
    if (fs.existsSync(path.join(ROOT, to))) die(`同名衝突: ${from} → ${to} が既にある`);
    if ([...moves.values()].includes(to)) die(`同名衝突: ${from} と別の移動元が同じ ${to} になる`);
    moves.set(from, to);
  }
}
const removedDirs = new Set(SUBS.map(s => `${ISS_REL}/${s}`));
const tracked = new Set(git('ls-files', '-z').split('\0').filter(Boolean));
for (const f of [...moves.keys(), ...removed.keys()]) if (!tracked.has(f)) die(`${f} が git 管理外。コミットしてから再実行`);

// --- 1. サマリの表を読む --------------------------------------------------
/** 表を {headers, cells} の行へ。空行で途切れた続きの行は直前の見出しで読む。 */
function readTables(text) {
  const lines = text.replace(/\r\n/g, '\n').split('\n');
  const rows = [];
  let headers = null;
  const split = l => l.replace(/^\|/, '').replace(/\|\s*$/, '').split(/(?<!\\)\|/).map(c => c.trim());
  for (let i = 0; i < lines.length; i++) {
    const l = lines[i];
    if (!l.startsWith('|')) continue;
    if (/^\|[\s:|-]*\|?$/.test(l)) continue;
    if (lines[i + 1] && /^\|[\s:|-]*\|?$/.test(lines[i + 1])) { headers = split(l); continue; }
    if (!headers) continue;
    const cells = split(l);
    const m = (cells[0] || '').match(ID_RE);
    if (!m) continue;
    const row = { id: m[0] };
    headers.forEach((h, k) => { row[h.replace(/\*/g, '')] = cells[k] || ''; });
    row.linked = /\]\(/.test(cells[0]);
    rows.push(row);
  }
  return rows;
}
const summaryRows = {}; // 'top' | sub → Map(id → row)
const topRel = `${ISS_REL}/issue_summary.md`;
summaryRows.top = new Map(fs.existsSync(path.join(ROOT, topRel)) ? readTables(read(topRel)).map(r => [r.id, r]) : []);
for (const sub of SUBS) {
  const rel = `${ISS_REL}/${AUX[sub]}`;
  summaryRows[sub] = new Map(fs.existsSync(path.join(ROOT, rel)) ? readTables(read(rel)).map(r => [r.id, r]) : []);
}

// --- 2. リンクの付け替え -----------------------------------------------------
const inverse = new Map([...moves].map(([a, b]) => [b, a]));
const toAbs = t => {
  if (/^file:\/\//i.test(t)) t = t.replace(/^file:\/\/\/?/i, '');
  if (/^[A-Za-z]:[\\/]/.test(t)) return path.normalize(t);
  const m = t.match(/^\/([A-Za-z])\/(.*)$/);
  if (m && process.platform === 'win32') return path.normalize(`${m[1]}:/${m[2]}`);
  return null;
};
/** ROOT 相対の旧パス → 新パス。付け替え不要なら null。 */
function mapTarget(oldRel) {
  if (moves.has(oldRel)) return moves.get(oldRel);
  if (removed.has(oldRel)) return removed.get(oldRel);
  if (removedDirs.has(oldRel.replace(/\/$/, ''))) return `${ISS_REL}/issue_summary.md`;
  return null;
}
/**
 * text 中のリンクを書き換える。oldDir / newDir は text が置かれていた / 置かれる場所 (ROOT 相対)。
 * 戻り値 [新しい text, 書き換えた数, 付け替え不能の一覧]
 */
function rewriteLinks(text, oldDir, newDir) {
  let count = 0;
  const fix = raw => {
    const inner = raw.replace(/^<|>$/g, '');
    if (!inner || /^(https?:|mailto:|tel:|data:|#)/.test(inner)) return raw;
    const hashAt = inner.indexOf('#');
    const target = hashAt === -1 ? inner : inner.slice(0, hashAt);
    let frag = hashAt === -1 ? '' : inner.slice(hashAt);
    let decoded;
    try { decoded = decodeURIComponent(target); } catch (_) { return raw; }
    const abs = toAbs(decoded);
    const oldRel = abs ? norm(path.relative(ROOT, abs)) : norm(path.posix.normalize(path.posix.join(oldDir, decoded)));
    if (abs && (oldRel.startsWith('..') || path.isAbsolute(oldRel))) return raw; // 別リポジトリ
    let newTargetRel = mapTarget(oldRel);
    if (newTargetRel && (removed.has(oldRel) || removedDirs.has(oldRel.replace(/\/$/, '')))) frag = ''; // 消えるファイルの見出しは無効
    if (!newTargetRel) {
      if (oldDir === newDir || abs) return raw;
      newTargetRel = oldRel; // 自分が動いた: 相手はそのまま、相対だけ再計算
    }
    let out;
    if (abs) {
      const drive = decoded.match(/^([A-Za-z]):/);
      out = norm(path.join(ROOT, newTargetRel));
      if (drive) out = drive[1] + out.slice(1);
    } else {
      out = path.posix.relative(newDir, newTargetRel) || path.posix.basename(newTargetRel);
    }
    const res = (raw.startsWith('<') ? `<${out}${frag}>` : `${out}${frag}`);
    if (res !== raw) count++;
    return res;
  };
  // フェンス外だけを処理。コードスパン (`...`) も避ける
  const parts = text.split(/(^```[\s\S]*?^```)/m);
  const outParts = parts.map((part, k) => {
    if (k % 2 === 1) return part;
    return part.split(/(`[^`\n]*`)/).map((seg, j) => {
      if (j % 2 === 1) return seg;
      seg = seg.replace(/\]\(\s*(<[^>]+>|[^)\s]+)((?:\s+["'(][^)]*)?)\s*\)/g, (m0, t, title) => `](${fix(t)}${title})`);
      seg = seg.replace(/^(\s*\[[^\]]+\]:\s*)(\S+)/gm, (m0, pre, t) => `${pre}${fix(t)}`);
      return seg;
    }).join('');
  });
  return [outParts.join(''), count];
}

// --- 3. 個票へ移す内容を決める --------------------------------------------
const issueFiles = new Map(); // id → 新 ROOT 相対
for (const e of fs.readdirSync(ISS)) if (ID_RE.test(e) && e.endsWith('.md')) issueFiles.set(e.match(ID_RE)[0], `${ISS_REL}/${e}`);
for (const [, to] of moves) issueFiles.set(path.posix.basename(to).match(ID_RE)[0], to);

const OVERVIEW_KEYS = ['ID', '重要度', '概要', '状態'];
const STATUS_SUB = { done: 'archive', review: 'review', hold: 'hold' };
const STATUS_WORD = { 未着手: 'todo', 対応中: 'wip', 確認待ち: 'review', 保留: 'hold', 完了: 'done' };
const RECORD_TITLE = { done: '完了記録', hold: '保留の記録', review: '確認待ちの記録' };
const edits = new Map(); // 新 ROOT 相対 → { add: {k:v}, append: string }
const stubs = [];
const pick = (row, re) => { const k = Object.keys(row).find(h => re.test(h)); return k ? row[k] : ''; };
const stripLinkedCell = s => (s || '').replace(/\\\|/g, '|').trim();
const auxName = sub => `${sub}/${path.posix.basename(AUX[sub])}`;
/** サマリの状態欄 (todo / `wip` / 確認待ち など) → frontmatter の status。読めなければ null。 */
function normStatus(v) {
  const s = (v || '').replace(/[`*_]/g, '').trim();
  if (STATUSES.includes(s.toLowerCase())) return s.toLowerCase();
  const w = Object.keys(STATUS_WORD).find(k => s.includes(k));
  return w ? STATUS_WORD[w] : null;
}
const normPriority = v => PRIORITIES.find(p => p.toLowerCase() === (v || '').replace(/[`*_]/g, '').trim().toLowerCase()) || null;

/**
 * 1 件分の移記内容を決める。text は移行前の個票 (雛形なら雛形) の本文。
 * トップ一覧と全補助サマリ (review / hold / archive) の行を集め、リンクは行の置き場所から issues/ 直下へ付け替える。
 * frontmatter は check-issues.js と同じ解析 (引用符・前後空白を正規化)。status が既定値以外なら移行を止める。
 * 戻り値 { add: frontmatter に足すキー, append: 末尾に足す節, summary: 採用した概要 }
 */
function planFor(id, text, where) {
  const fm = parseFrontmatter(text);
  if (!fm) die(`${where}: frontmatter が無い。直してから再実行 (移行は何もしていない)`);
  const status = fm.status;
  if (!STATUSES.includes(status)) die(`${where}: status "${fm.status || ''}" は ${STATUSES.join(' / ')} のどれでもない。直してから再実行 (移行は何もしていない)`);
  const has = k => Object.prototype.hasOwnProperty.call(fm, k);
  const add = {};
  const notes = [];
  const srcs = new Set();
  const rebase = (s, fromDirRel) => rewriteLinks(s, fromDirRel, ISS_REL)[0];

  const top = summaryRows.top.get(id);
  const rows = [
    ...(top ? [{ src: 'issue_summary.md', row: top, dir: ISS_REL }] : []),
    ...SUBS.filter(s => summaryRows[s].has(id)).map(s => ({ src: auxName(s), sub: s, row: summaryRows[s].get(id), dir: `${ISS_REL}/${s}` })),
  ];
  const multi = rows.filter(r => r.sub).length > 1;
  const note = (r, line) => { notes.push(multi && r.sub ? `- (${r.src}) ${line.slice(2)}` : line); srcs.add(r.src); };

  // 概要: done は archive の行、それ以外はトップ一覧の行を優先。採らなかった別の概要は記録に残す
  const summaries = rows.map(r => ({ r, s: rebase(stripLinkedCell(pick(r.row, /^概要$/)), r.dir) })).filter(x => x.s);
  const own = summaries.find(x => x.r.sub && x.r.sub === STATUS_SUB[status]);
  const topS = summaries.find(x => !x.r.sub);
  const chosen = (status === 'done' ? (own || topS) : (topS || own)) || summaries[0];
  const summary = chosen ? chosen.s : '';
  if (!has('summary') && summary) add.summary = summary;
  const kept = has('summary') ? fm.summary : summary;
  const noted = new Set([kept]);
  // 採用した概要か H1 にそのまま含まれる短い版は記録しない (同じ内容の重複を避ける)
  const squash = s => (s || '').replace(/\s+/g, '');
  const known = squash(kept) + '\n' + squash((text.match(/^# (.*)$/m) || [])[1]);
  for (const x of summaries) {
    if (noted.has(x.s) || known.includes(squash(x.s))) continue;
    noted.add(x.s);
    note(x.r, `- 旧一覧 (${x.r.src}) の概要: ${x.s}`);
  }

  for (const r of rows) {
    for (const [h, v] of Object.entries(r.row)) {
      if (['id', 'linked'].includes(h) || OVERVIEW_KEYS.includes(h) || !v || /^[-—(（]?なし/.test(v)) continue;
      const val = rebase(stripLinkedCell(v), r.dir);
      if (/確かめれば完了/.test(h)) {
        if (status === 'review' && !has('next') && !add.next) add.next = val; else note(r, `- ${h}: ${val}`);
        continue;
      }
      if (/再検討のトリガー/.test(h)) {
        if (status === 'hold' && !has('next') && !add.next) add.next = `再検討: ${val}`;
        note(r, `- ${h}: ${val}`);
        continue;
      }
      if (/完了日/.test(h) && DATE_RE.test(val)) {
        if (status === 'done' && !has('updated') && !has('decided') && !has('completed') && !add.updated) add.updated = val;
        note(r, `- 完了日: ${val}`);
        continue;
      }
      note(r, `- ${h}: ${val}`);
    }
  }
  // 手書きサマリにあった重要度と frontmatter の食い違いは報告だけ
  for (const r of rows) {
    const p = pick(r.row, /^重要度$/).replace(/\*/g, '').trim();
    if (p && fm.priority && p !== fm.priority) log(`WARN ${id}: ${r.src} の重要度 ${p} と frontmatter ${fm.priority} が違う (frontmatter を採用)`);
  }

  let append = '';
  if (notes.length) {
    const title = RECORD_TITLE[status] || 'サマリからの移記';
    const srcList = [...srcs];
    const exists = new RegExp(`^## ${title}\\s*$`, 'm').test(text);
    append = `\n${exists ? `### ${title} (${srcList.join('・')} から移記)` : `## ${title}`}\n\n` +
      (exists ? '' : `> ${TODAY} の Issue 運用一本化で ${srcList.map(s => `\`${s}\``).join('・')} の行から移記。\n\n`) + notes.join('\n') + '\n';
  }
  return { add, append, summary };
}

for (const id of [...issueFiles.keys()].sort()) {
  const file = issueFiles.get(id);
  const oldFile = inverse.get(file) || file;
  const { add, append } = planFor(id, read(oldFile), oldFile);
  if (Object.keys(add).length || append) edits.set(file, { add, append });
}

// サマリの表にだけあって個票が無い ID → 雛形を作り、既存の個票と同じ移記をかける (全サマリの行を統合)
const allIds = new Set([...summaryRows.top.keys(), ...SUBS.flatMap(s => [...summaryRows[s].keys()])]);
for (const id of [...allIds].sort()) {
  if (issueFiles.has(id)) continue;
  const top = summaryRows.top.get(id);
  const inSubs = SUBS.filter(s => summaryRows[s].has(id));
  const where = `サマリの行 ${id} (${[top ? 'issue_summary.md' : null, ...inSubs.map(auxName)].filter(Boolean).join(', ')})`;
  const rawStatus = top ? pick(top, /^状態$/) : '';
  const bySub = ['archive', 'hold', 'review'].find(s => inSubs.includes(s));
  const status = rawStatus ? normStatus(rawStatus) : (bySub === 'archive' ? 'done' : bySub || 'todo');
  if (!status) die(`${where}: 状態 "${rawStatus}" を ${STATUSES.join(' / ')} に読み替えられない。サマリを直してから再実行`);
  const rawPri = [top, ...inSubs.map(s => summaryRows[s].get(id))].filter(Boolean).map(r => pick(r, /^重要度$/)).find(Boolean) || '';
  const priority = rawPri ? normPriority(rawPri) : 'Medium';
  if (!priority) die(`${where}: 重要度 "${rawPri}" を ${PRIORITIES.join(' / ')} に読み替えられない。サマリを直してから再実行`);
  if (!rawPri) log(`WARN ${id}: サマリに重要度が無いので雛形は Medium`);
  const [, type, system] = id.match(ID_RE);
  const head = `---\nid: ${id}\ntype: ${type}\nsystem: ${system}\nstatus: ${status}\npriority: ${priority}\n---\n\n`;
  const intro = `> ${TODAY} の Issue 運用一本化で作った雛形。それまではサマリの表の行だけで、個別ファイルが無かった。\n> 着手するときに本文 (背景・完了条件) を起こす。\n`;
  const plan = planFor(id, `${head}# ${id}\n`, where);
  const words = (plan.summary.match(/^[A-Za-z0-9 /&+.-]+/) || [''])[0].trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
  const rel = `${ISS_REL}/${id}-${words || 'untitled'}.md`;
  const title = plan.summary.replace(/\[([^\]]*)\]\([^)]*\)/g, '$1').split(/ — |。/)[0];
  stubs.push({ id, rel, text: applyEdit(`${head}# ${id}${title ? ` ${title}` : ''}\n\n${intro}`, plan) });
}

function yamlValue(v) {
  v = v.replace(/\r?\n/g, ' ').trim();
  if (/^[\[{>|*&!%@`'"#-]|: | #|^\s|\s$/.test(v) || v === '') return `"${v.replace(/\\/g, '\\\\').replace(/"/g, '\\"')}"`;
  return v;
}
function applyEdit(text, { add, append }) {
  const eol = text.includes('\r\n') ? '\r\n' : '\n';
  let t = text.replace(/\r\n/g, '\n');
  if (Object.keys(add).length) {
    t = t.replace(/^(﻿?---\n[\s\S]*?)\n---\n/, (m0, head) =>
      `${head}\n${Object.entries(add).map(([k, v]) => `${k}: ${yamlValue(v)}`).join('\n')}\n---\n`);
  }
  if (append) t = t.replace(/\n*$/, '\n') + append;
  return eol === '\n' ? t : t.replace(/\n/g, eol);
}

// --- 4. リンク付け替えの予定 (git 管理下の全 .md) ---------------------------
const mdFiles = [...tracked].filter(f => f.endsWith('.md') && !removed.has(f));
const linkPlan = [];
for (const oldRel of mdFiles) {
  const newRel = moves.get(oldRel) || oldRel;
  if (!fs.existsSync(path.join(ROOT, oldRel))) continue;
  const text = read(oldRel);
  const [out, n] = rewriteLinks(text, path.posix.dirname(oldRel), path.posix.dirname(newRel));
  if (n) linkPlan.push({ oldRel, newRel, out, n });
}
// git 管理外の .md で付け替えが要るもの (書き換えない。警告だけ)
const untrackedHits = [];
for (const f of git('ls-files', '-z', '--others', '--exclude-standard').split('\0').filter(f => f.endsWith('.md'))) {
  const [, n] = rewriteLinks(read(f), path.posix.dirname(f), path.posix.dirname(f));
  if (n) untrackedHits.push(`${f} (${n} 件)`);
}

// --- 5. 予定の表示 ---------------------------------------------------------
log(`${DRY ? '[dry-run] ' : ''}Issue 運用の一本化 — ${path.basename(ROOT)}`);
log(`\n(a) 直下へ移動 ${moves.size} 件`);
for (const [a, b] of moves) log(`    ${a.slice(ISS_REL.length + 1)} → ${b.slice(ISS_REL.length + 1)}`);
log(`\n(b) リンクの付け替え ${linkPlan.reduce((s, p) => s + p.n, 0)} 本 / ${linkPlan.length} ファイル`);
for (const p of linkPlan) log(`    ${p.newRel}: ${p.n}`);
if (untrackedHits.length) log(`    WARN git 管理外で付け替えが要る (書き換えない): ${untrackedHits.join(', ')}`);
log(`\n(c) 個票への移記 ${edits.size} 件 / 雛形の新規作成 ${stubs.length} 件 / 補助サマリの削除 ${removed.size} 件`);
for (const [f, e] of edits) log(`    ${path.posix.basename(f)}: ${Object.keys(e.add).join(', ') || '-'}${e.append ? ' + 末尾に記録' : ''}`);
for (const s of stubs) log(`    NEW ${s.rel.slice(ISS_REL.length + 1)}`);
for (const r of removed.keys()) log(`    RM  ${r.slice(ISS_REL.length + 1)}`);

const before = issueFiles.size;
log(`\nIssue 数: 移行前 ${before} (+ 雛形 ${stubs.length}) → 移行後 ${before + stubs.length}`);
if (DRY) { log('\n[dry-run] 何も書いていない'); process.exit(0); }

// --- 6. 実行 ---------------------------------------------------------------
const pending = new Map(); // 新 ROOT 相対 → 書き込む内容 (git mv 前に読んだ内容を元にする)
for (const p of linkPlan) pending.set(p.newRel, p.out);
for (const [a, b] of moves) if (!pending.has(b)) pending.set(b, read(a));
for (const [f, e] of edits) pending.set(f, applyEdit(pending.get(f) || read(inverse.get(f) || f), e));

for (const [a, b] of moves) git('mv', a, b);
for (const [f, t] of pending) fs.writeFileSync(path.join(ROOT, f), t);
for (const s of stubs) fs.writeFileSync(path.join(ROOT, s.rel), s.text);
for (const r of removed.keys()) git('rm', '-q', r);
for (const d of removedDirs) { const p = path.join(ROOT, d); if (fs.existsSync(p) && !fs.readdirSync(p).length) fs.rmdirSync(p); }

try {
  const out = execFileSync(process.execPath, [path.join(ROOT, 'scripts/check-issues.js'), '--write'], { cwd: ROOT, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
  log(`\n(d) ${out.trim().split('\n')[0]}`);
} catch (e) {
  log(`\n(d) WARN issue_summary.md を生成できなかった (検査エラー)。直してから node scripts/check-issues.js --write:\n${String(e.stderr || e.stdout || e.message).trim()}`);
  process.exitCode = 1;
}
if (!fs.existsSync(path.join(ISS, 'README.md'))) log('WARN docs/development/issues/README.md が無い。運用ルールを移植する (templates にある)');
log('\n完了。node scripts/check-issues.js と node scripts/check-doc-links.js を通してからコミットする');
