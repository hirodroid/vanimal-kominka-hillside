#!/usr/bin/env node
// 進捗ダッシュボード: node scripts/kh-dashboard.js
// docs/plans/2026-09-27-progress-dashboard.html (自己完結の 1 ファイル。file:// で開く) を生成する。
// 数字や一覧は手で書かず、次から毎回読む:
//   - Issue: IMP-DOG-020 / IMP-WLD-001 (frontmatter の status・next、「完了条件」の節の ### 見出しと - [ ] / - [x])、DES-DOG-004 / DES-WLD-003 (status)
//   - ハーネス: Logs/kh-course-walk/*/report.json (種別ごとの最新 run)
//   - Verify: Temp/imp-dog-020/water-spot-verify.json、Temp/imp-wld-001/travel-verify.json
//   - 容量: Temp/kd-buildsize/bundle-sizes.txt (Android / iOS の最後の行)
//   - git log (最新の [feat] / [docs] 10 件。読むだけ)
// 段階の見出しは Issue から動的に読む (段階の数・名前を決め打ちしない)。無いファイルは「未取得」と出して止まらない。
'use strict';
const fs = require('fs');
const path = require('path');
const cp = require('child_process');

const root = path.join(__dirname, '..');
const OUT = 'docs/plans/2026-09-27-progress-dashboard.html';
const outDir = path.dirname(path.join(root, OUT));
const abs = (p) => path.join(root, p);
const readText = (p) => { try { return fs.readFileSync(abs(p), 'utf8').replace(/^﻿/, ''); } catch (e) { return null; } };
const readJson = (p) => { const t = readText(p); if (t == null) return null; try { return JSON.parse(t); } catch (e) { return null; } };
const esc = (s) => String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const link = (repoPath, anchor) => path.relative(outDir, abs(repoPath)).split(path.sep).join('/') + (anchor ? '#' + anchor : '');
// Markdown の `code` と [text](url) を読める HTML に (url は Issue のある場所から解決する)
function md(s, fromDir) {
  let h = esc(s);
  h = h.replace(/`([^`]+)`/g, '<code>$1</code>');
  h = h.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
  h = h.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (m, text, url) => {
    if (/^https?:/.test(url)) return `<a href="${url}">${text}</a>`;
    const [p, a] = url.split('#');
    const target = path.relative(root, path.resolve(abs(fromDir), p)).split(path.sep).join('/');
    return `<a href="${esc(link(target, a))}">${text}</a>`;
  });
  return h;
}

// ============================================================ Issue
const PCVR = /PCVR|実機|Quest|iOS|iPhone|2 人|3 人|2〜3 人|16 人|ユーザーの|体験会|VER-WLD-001/;
function parseIssue(file) {
  const text = readText(file);
  if (text == null) return { file, missing: true, stages: [], fm: {} };
  const lines = text.split(/\r?\n/);
  const fm = {};
  let i = 0;
  if (lines[0] === '---') { for (i = 1; i < lines.length && lines[i] !== '---'; i++) { const m = /^(\w+):\s*(.*)$/.exec(lines[i]); if (m) fm[m[1]] = m[2]; } i++; }
  const title = (lines.find((l) => /^# /.test(l)) || '').replace(/^# /, '');
  const stages = []; const sections = [];
  let h2 = '', cur = null, curSection = null;
  for (; i < lines.length; i++) {
    const l = lines[i];
    if (/^## /.test(l)) { h2 = l.slice(3).trim(); cur = null; curSection = { h2, lines: [] }; sections.push(curSection); continue; }
    if (curSection) curSection.lines.push(l);
    if (/^### /.test(l)) {
      cur = /完了条件|段階/.test(h2) ? { title: l.slice(4).trim(), h2, done: [], todo: [], text: [] } : null;
      if (cur) stages.push(cur);
      continue;
    }
    if (!cur) continue;
    cur.text.push(l);
    const m = /^\s*- \[( |x|X)\] (.*)$/.exec(l);
    if (m) (m[1] === ' ' ? cur.todo : cur.done).push(m[2]);
  }
  for (const s of stages) {
    const n = s.done.length + s.todo.length;
    s.total = n;
    s.state = n === 0 ? 'none' : s.todo.length === 0 ? 'done' : s.todo.every((t) => PCVR.test(t)) ? 'pcvr' : s.done.length === 0 ? 'todo' : 'wip';
    s.body = s.text.join('\n');
  }
  return { file, fm, title, stages, sections, text };
}

// ============================================================ ハーネス
function kindOf(r) {
  if (r.travelOnly) return r.full ? 'travel' : 'travel quick';
  if (r.riverOnly) return 'W5';
  if (r.waterFetch) return 'fetch';   // Course/88 (W3 / S4 / S5b)。waterOnly も立つので water より先に見る
  if (r.waterOnly) return r.waterSwim || /^SWIM/.test(r.status || '') ? 'swim' : 'water';
  return null;
}
function harness() {
  const dir = abs('Logs/kh-course-walk');
  const latest = {};
  let names = [];
  try { names = fs.readdirSync(dir).filter((d) => /^\d{8}-\d{6}$/.test(d)).sort(); } catch (e) { return latest; }
  for (const d of names) {
    const r = readJson('Logs/kh-course-walk/' + d + '/report.json');
    if (!r) continue;
    const kind = kindOf(r);
    if (!kind) continue;
    const cases = r.cases || [];
    const pass = cases.filter((c) => /PASS|OBSERVED/.test(c.status || '')).length;
    latest[kind] = { runId: r.runId || d, status: r.status, pass, total: cases.length, failed: cases.filter((c) => !/PASS|OBSERVED/.test(c.status || '')).map((c) => c.name + ':' + c.status), reason: r.reason || '', utc: r.endedUtc || r.utc };
  }
  return latest;
}
const KIND_RULES = [
  { kind: 'W5', re: /W5/ },
  { kind: 'fetch', re: /W3|Course\/88/ },
  { kind: 'water', re: /W0|W4|W6/ },
  { kind: 'swim', re: /W1|W2/ },
  { kind: 'travel', re: /\bT1\b|T1〜|T9|T10|T11|travel/ },
  { kind: 'travel quick', re: /Quick|83/ },
];

// ============================================================ 読む
const ISSUE_DIR = 'docs/development/issues/';
const features = [
  { id: 'IMP-DOG-020', name: '川の浅瀬遊びと泳ぎ', file: ISSUE_DIR + 'IMP-DOG-020-river-wading-and-swim.md', design: ISSUE_DIR + 'DES-DOG-004-river-play-and-floating-fetch.md', plan: 'docs/plans/2026-09-27-river-play-and-floating-fetch.md' },
  { id: 'IMP-WLD-001', name: '地図・2 地点ワープ・合流', file: ISSUE_DIR + 'IMP-WLD-001-map-warp-and-join.md', design: ISSUE_DIR + 'DES-WLD-003-map-and-travel.md', plan: 'docs/plans/2026-09-27-map-and-travel.md' },
].map((f) => Object.assign(f, { issue: parseIssue(f.file), des: parseIssue(f.design) }));
const runs = harness();
const verifyWater = readJson('Temp/imp-dog-020/water-spot-verify.json');
const verifyTravel = readJson('Temp/imp-wld-001/travel-verify.json');
const sizes = {};
(readText('Temp/kd-buildsize/bundle-sizes.txt') || '').split(/\r?\n/).forEach((l) => { const m = /^(\w+)\s+target=(\w+)\s+download=([\d.]+ MB)\s+uncompressed=([\d.]+ MB)\s+headroom=([\d.]+ MB)/.exec(l); if (m) sizes[m[1]] = { target: m[2], download: m[3], uncompressed: m[4], headroom: m[5] }; });
let commits = [];
try {
  const log = cp.execFileSync('git', ['log', '-n', '60', '--date=short', '--pretty=format:%h%x09%ad%x09%s'], { cwd: root, encoding: 'utf8' });
  commits = log.split('\n').map((l) => l.split('\t')).filter((c) => c.length >= 3 && /^\[(feat|docs)\]/.test(c[2])).slice(0, 10);
} catch (e) { commits = []; }
const generated = new Date();
const stamp = generated.toLocaleString('ja-JP', { timeZone: 'Asia/Tokyo', hour12: false }) + ' JST';

// 積み残し・初期公開の外 (Issue から)
function deferred(f) {
  const out = [];
  for (const s of f.issue.stages) if (/積み残し/.test(s.title)) {
    s.done.forEach((t) => out.push({ item: t, where: s.title, state: '済' }));
    s.todo.forEach((t) => out.push({ item: t, where: s.title, state: '未' }));
    s.text.filter((l) => /^- (?!\[)/.test(l)).forEach((l) => out.push({ item: l.slice(2), where: s.title, state: '—' }));
  }
  for (const sec of f.issue.sections) {
    const idx = sec.lines.findIndex((l) => /^- 含まないもの/.test(l));
    if (idx < 0) continue;
    for (let k = idx + 1; k < sec.lines.length && /^\s+- /.test(sec.lines[k]); k++) out.push({ item: sec.lines[k].replace(/^\s+- /, ''), where: sec.h2 + ' / 含まないもの', state: '後回し' });
  }
  for (const s of f.issue.stages) if (!/積み残し/.test(s.title)) s.todo.filter((t) => /^未対応/.test(t)).forEach((t) => out.push({ item: t, where: s.title, state: '未' }));
  return out;
}
// ユーザーの判断待ち (決定済みの行は除く)
function judgments(f) {
  const out = [];
  for (const file of [f.file]) {   // Issue だけを見る (計画書は試作値の「決め直す」が多く、判断待ちと区別できない)
    const t = readText(file); if (!t) continue;
    t.split(/\r?\n/).forEach((l) => {
      if (/ユーザーが決める|ユーザーが判断|ユーザーの判断|試作で決める|泳がせるかを決める|PCVR で判断|PCVR で決める|判断待ち/.test(l) && !/決定[:：]|決定 20|\(決定/.test(l)) out.push({ text: l.replace(/^\s*[-*]\s*(\[.\]\s*)?/, ''), file });
    });
  }
  return out;
}
// 実機チェックリスト (計画書の見出しから)
function checklist(f) {
  const t = readText(f.plan); if (!t) return null;
  const h = t.split(/\r?\n/).find((l) => /^## 実機チェックリスト/.test(l));
  if (!h) return null;
  const title = h.replace(/^## /, '');
  const anchor = title.toLowerCase().replace(/[()（）]/g, '').replace(/\s+/g, '-');
  return { title, href: link(f.plan, anchor) };
}

// ============================================================ HTML
const STATE = { done: ['完了', 'var(--green)'], pcvr: ['ClientSim 完了 / PCVR 待ち', 'var(--water)'], wip: ['進行中', 'var(--orange)'], todo: ['未着手', '#b9bcae'], none: ['条件なし', '#d8dacd'] };
const pill = (state) => `<span class="state" style="background:${STATE[state][1]}">${STATE[state][0]}</span>`;
function bar(stages) {
  const counted = stages.filter((s) => s.state !== 'none');
  const n = counted.length || 1;
  const seg = ['done', 'pcvr', 'wip', 'todo'].map((st) => { const c = counted.filter((s) => s.state === st).length; return c ? `<i style="width:${(c / n) * 100}%;background:${STATE[st][1]}" title="${STATE[st][0]} ${c}"></i>` : ''; }).join('');
  return `<div class="bar">${seg}</div>`;
}
function runCell(kind) {
  const r = runs[kind];
  if (!r) return `<span class="muted">${esc(kind)}: run なし</span>`;
  const ok = /PASS|OBSERVED/.test(r.status || '');
  return `<span class="run ${ok ? 'ok' : 'ng'}">${esc(kind)} ${esc(r.runId)} ${esc(r.status)} (${r.pass}/${r.total})</span>`;
}
function stageRuns(s) {
  const kinds = KIND_RULES.filter((k) => k.re.test(s.title + '\n' + s.body)).map((k) => k.kind);
  const ids = Array.from(new Set((s.body.match(/\b\d{8}-\d{6}\b/g) || [])));
  return kinds.map(runCell).join(' ') + ids.map((id) => ` <span class="run">${esc(id)}</span>`).join('');
}

const featureHtml = features.map((f) => {
  const is = f.issue;
  if (is.missing) return `<section class="card"><h2>${esc(f.id)}</h2><p class="muted">Issue が見つからない: ${esc(f.file)}</p></section>`;
  const counted = is.stages.filter((s) => s.state !== 'none');
  const done = counted.filter((s) => s.state === 'done' || s.state === 'pcvr').length;
  const list = is.stages.map((s) => `<article class="stage"><header><h3>${esc(s.title)}</h3>${pill(s.state)}<span class="muted">${s.done.length} / ${s.total}</span></header>
<ul class="checks">${s.done.map((t) => `<li class="x">${md(t, ISSUE_DIR)}</li>`).join('')}${s.todo.map((t) => `<li class="o">${md(t, ISSUE_DIR)}</li>`).join('')}</ul>
<div class="runs">${stageRuns(s) || '<span class="muted">根拠の run: なし</span>'}</div></article>`).join('\n');
  return `<section class="card feature" id="${esc(f.id)}"><div class="section-number">${esc(f.id)} · status ${esc(is.fm.status || '?')} · ${esc(f.des.fm.id || '')} ${esc(f.des.fm.status || '')}</div>
<h2>${esc(f.name)}</h2><p class="muted">${md(is.fm.summary || '', ISSUE_DIR)}</p>
<div class="progress-line"><b>${done} / ${counted.length}</b> 段階が ClientSim まで完了 ${bar(is.stages)}</div>
<p><strong>next:</strong> ${md(is.fm.next || '', ISSUE_DIR)} <a href="${esc(link(f.file))}">Issue</a> · <a href="${esc(link(f.plan))}">計画書</a></p>
<div class="stages">${list}</div></section>`;
}).join('\n');

const overviewHtml = features.map((f) => {
  const counted = f.issue.stages.filter((s) => s.state !== 'none');
  const c = (st) => counted.filter((s) => s.state === st).length;
  return `<div class="overview-item"><a href="#${esc(f.id)}"><b>${esc(f.name)}</b></a><span class="muted">${esc(f.id)} · ${esc(f.issue.fm.status || '?')}</span>${bar(f.issue.stages)}
<div class="counts"><span style="color:var(--green)">完了 ${c('done')}</span><span style="color:var(--water)">PCVR 待ち ${c('pcvr')}</span><span style="color:var(--orange)">進行中 ${c('wip')}</span><span class="muted">未着手 ${c('todo')}</span><span class="muted">/ ${counted.length} 段階</span></div></div>`;
}).join('');

const deferredRows = features.flatMap((f) => deferred(f).map((d) => `<tr><td>${esc(f.id)}</td><td>${md(d.item, ISSUE_DIR)}</td><td>${esc(d.state)}</td><td class="muted">${esc(d.where)}</td></tr>`)).join('');
const waitRows = features.map((f) => {
  const cl = checklist(f);
  const js = judgments(f);
  return `<article class="card"><h3>${esc(f.name)}</h3>${cl ? `<p>PCVR: <a href="${esc(cl.href)}">${esc(cl.title)}</a> (${esc(path.basename(f.plan))})</p>` : '<p class="muted">実機チェックリストが見つからない</p>'}
${js.length ? `<p><strong>判断待ち</strong></p><ul>${js.map((j) => `<li>${md(j.text, path.dirname(j.file))}</li>`).join('')}</ul>` : '<p class="muted">判断待ちの行は見つからない</p>'}</article>`;
}).join('');
const kinds = ['W5', 'water', 'swim', 'fetch', 'travel quick', 'travel'];
const runRows = kinds.map((k) => { const r = runs[k]; return r ? `<tr><td>${esc(k)}</td><td><code>${esc(r.runId)}</code></td><td class="${/PASS|OBSERVED/.test(r.status) ? 'ok' : 'ng'}">${esc(r.status)}</td><td>${r.pass} / ${r.total}</td><td class="muted">${esc(r.failed.join(', ') || '—')}</td><td class="muted small">${esc((r.reason || '').slice(0, 160))}</td></tr>` : `<tr><td>${esc(k)}</td><td colspan="5" class="muted">run なし</td></tr>`; }).join('');
const verifyRows = [
  ['水遊び場 (Course/77)', 'Temp/imp-dog-020/water-spot-verify.json', verifyWater],
  ['地図・ワープ・合流 (Course/81)', 'Temp/imp-wld-001/travel-verify.json', verifyTravel],
].map(([name, file, v]) => v ? `<tr><td>${esc(name)}</td><td class="${/PASS|OK/.test(v.status) ? 'ok' : 'ng'}">${esc(v.status)}</td><td>${esc(v.errors)}</td><td class="muted">${esc(v.utc)}</td><td class="muted small">${esc(file)}</td></tr>` : `<tr><td>${esc(name)}</td><td colspan="4" class="muted">未取得 (${esc(file)})</td></tr>`).join('');
const mapBytes = verifyTravel && verifyTravel.mapPngBytes ? `<p class="muted">地図テクスチャ: PNG ${verifyTravel.mapPngBytes.toLocaleString()} byte / GPU PC ${Number(verifyTravel.mapGpuBytesPC).toLocaleString()} · mobile ${Number(verifyTravel.mapGpuBytesMobile).toLocaleString()} byte (${esc(verifyTravel.mapFormats || '')})</p>` : '';
const sizeRows = ['android', 'ios'].map((k) => { const s = sizes[k]; return s ? `<tr><td>${esc(s.target)}</td><td><b>${esc(s.download)}</b></td><td>${esc(s.uncompressed)}</td><td>${esc(s.headroom)} 余力 (上限 100 MB)</td></tr>` : `<tr><td>${esc(k)}</td><td colspan="3" class="muted">未取得</td></tr>`; }).join('');
const commitRows = commits.map((c) => `<tr><td><code>${esc(c[0])}</code></td><td class="muted">${esc(c[1])}</td><td>${esc(c[2])}</td></tr>`).join('') || '<tr><td colspan="3" class="muted">git log を読めなかった</td></tr>';

const html = `<!doctype html>
<html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>古民家の裏山｜進捗ダッシュボード (水遊び・地図/ワープ/合流)</title>
<style>
:root{--ink:#26372f;--muted:#62736a;--paper:#f5f3ea;--card:#fffef9;--green:#42624a;--orange:#c56a37;--water:#467f96;--line:#dedfd3;--light:#e5eadb}
*{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font-family:'Yu Gothic UI','Meiryo',sans-serif;line-height:1.7}
a{color:#285d70;text-underline-offset:3px}header{background:#243b30;color:#f8f5e9;padding:36px max(5vw,16px) 30px}
.eyebrow{font-size:12px;letter-spacing:.15em;font-weight:700;color:#bfcbb4}h1{font-size:clamp(24px,4vw,40px);line-height:1.3;margin:10px 0 8px}header p{margin:0;color:#e0e6d8}
main{max-width:1180px;margin:auto;padding:0 16px 60px}section{padding:28px 0;border-bottom:1px solid var(--line)}
.section-number{font-size:12px;letter-spacing:.12em;font-weight:700;color:var(--orange)}h2{font-size:24px;margin:6px 0 12px;line-height:1.45}h3{font-size:16px;margin:0 0 6px}
.card{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:18px}.feature{margin-top:22px}.muted{color:var(--muted);font-size:13px}.small{font-size:12px}
.overview{display:grid;grid-template-columns:1fr 1fr;gap:16px}.overview-item{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:16px;display:flex;flex-direction:column;gap:6px}
.bar{display:flex;height:14px;border-radius:7px;overflow:hidden;background:#e7e8dc;min-width:120px;flex:1}.bar i{display:block;height:100%}
.counts{display:flex;gap:12px;flex-wrap:wrap;font-size:13px;font-weight:700}.progress-line{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin:6px 0 10px}
.stages{display:grid;grid-template-columns:repeat(auto-fill,minmax(320px,1fr));gap:12px}.stage{border:1px solid var(--line);border-radius:12px;padding:12px;background:#fbfaf3}
.stage header{background:none;color:inherit;padding:0;display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-bottom:6px}
.state{color:#fff;font-size:11px;font-weight:700;border-radius:10px;padding:1px 8px}.checks{margin:0;padding:0;list-style:none;font-size:13px}
.checks li{padding-left:22px;position:relative;margin:3px 0}.checks li:before{position:absolute;left:0;top:0;font-weight:700}.checks li.x:before{content:'✔';color:var(--green)}.checks li.o:before{content:'□';color:var(--muted)}
.runs{margin-top:8px;display:flex;gap:6px;flex-wrap:wrap}.run{font-size:11px;border-radius:8px;padding:1px 7px;background:#e5ebdf}.run.ok{background:#dcebdd;color:var(--green)}.run.ng{background:#f5dfd3;color:#924920}
.table-wrap{overflow-x:auto}table{border-collapse:collapse;width:100%;font-size:13px;background:var(--card)}th,td{border-bottom:1px solid var(--line);padding:6px 8px;text-align:left;vertical-align:top}th{background:var(--light)}
td.ok{color:var(--green);font-weight:700}td.ng{color:#b0442a;font-weight:700}code{font-size:12px;background:#eef0e5;padding:0 4px;border-radius:4px}
.grid2{display:grid;grid-template-columns:1fr 1fr;gap:16px}
@media(max-width:760px){.overview,.grid2{grid-template-columns:1fr}.stages{grid-template-columns:1fr}h2{font-size:20px}}
</style></head><body>
<header><div class="eyebrow">VANIMAL / KOMINKA HILLSIDE / PROGRESS</div><h1>水遊び・地図/ワープ/合流の進捗</h1>
<p>生成: ${esc(stamp)} ／ <code>node scripts/kh-dashboard.js</code> で作り直す (数字は Issue・ハーネス・検査の出力から毎回読む)</p></header>
<main>
<section><div class="section-number">OVERVIEW</div><h2>全体の進捗</h2><div class="overview">${overviewHtml}</div>
<p class="muted">色: <b style="color:var(--green)">完了</b> ／ <b style="color:var(--water)">ClientSim 完了・PCVR 待ち</b> (残りが実機・端末・人数の項目だけ) ／ <b style="color:var(--orange)">進行中</b> ／ 未着手。段階は Issue の「完了条件」の ### 見出し。</p></section>
${featureHtml}
<section><div class="section-number">DEFERRED</div><h2>今回外して初期公開に戻したもの・積み残し</h2><div class="table-wrap"><table><thead><tr><th>機能</th><th>項目</th><th>状態</th><th>出どころ</th></tr></thead><tbody>${deferredRows || '<tr><td colspan="4" class="muted">なし</td></tr>'}</tbody></table></div></section>
<section><div class="section-number">WAITING FOR USER</div><h2>ユーザー待ち</h2><div class="grid2">${waitRows}</div></section>
<section><div class="section-number">HARNESS</div><h2>最新のハーネス結果 (ClientSim)</h2><div class="table-wrap"><table><thead><tr><th>種別</th><th>run</th><th>結果</th><th>ケース</th><th>落ちたケース</th><th>理由</th></tr></thead><tbody>${runRows}</tbody></table></div>
<h3 style="margin-top:16px">Editor の検査</h3><div class="table-wrap"><table><thead><tr><th>検査</th><th>結果</th><th>エラー</th><th>時刻 (UTC)</th><th>ファイル</th></tr></thead><tbody>${verifyRows}</tbody></table></div>${mapBytes}</section>
<section><div class="section-number">CAPACITY</div><h2>容量 (モバイルの代理計測)</h2><div class="table-wrap"><table><thead><tr><th>端末</th><th>ダウンロード</th><th>展開後</th><th>余力</th></tr></thead><tbody>${sizeRows}</tbody></table></div><p class="muted">Temp/kd-buildsize/bundle-sizes.txt の最後の行。SDK のアップロード容量・実機メモリではない。</p></section>
<section><div class="section-number">COMMITS</div><h2>最新のコミット ([feat] / [docs])</h2><div class="table-wrap"><table><tbody>${commitRows}</tbody></table></div></section>
</main></body></html>
`;
fs.writeFileSync(abs(OUT), html);
console.log('[kh-dashboard] wrote ' + OUT + ' (' + features.map((f) => f.id + ' ' + f.issue.stages.length + ' stages').join(', ') + ', runs ' + Object.keys(runs).join('/') + ')');
