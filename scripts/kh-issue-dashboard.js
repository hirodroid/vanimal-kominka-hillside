#!/usr/bin/env node
// IMP-DOG-020 (川の水遊び) の進捗ダッシュボードを HTML で生成する。
//   node scripts/kh-issue-dashboard.js            -> Temp/imp-dog-020/dashboard.html
//   node scripts/kh-issue-dashboard.js --out x.html
// 読むもの: Issue 文書 (frontmatter・チェック箱)、git log / diff、Logs/kh-course-walk/<run>/、Temp/imp-dog-020/*.json
'use strict';
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

const ROOT = path.resolve(__dirname, '..');
const ISSUE_ID = 'IMP-DOG-020';
const ISSUE_FILE = path.join(ROOT, 'docs/development/issues/IMP-DOG-020-river-wading-and-swim.md');
const RUNS_DIR = path.join(ROOT, 'Logs/kh-course-walk');
const TEMP_DIR = path.join(ROOT, 'Temp/imp-dog-020');
const ISSUE_START_COMMIT = '5dea996'; // Issue 作成 (2026-09-27 11:57)
// 別セッションの全体版 (両 Issue): node scripts/kh-dashboard.js → docs/plans/2026-09-27-progress-dashboard.html。こちらはハーネスの履歴と失敗理由に寄せている。
const JST_OFFSET_MIN = 9 * 60;

const argOut = (() => { const i = process.argv.indexOf('--out'); return i >= 0 ? process.argv[i + 1] : null; })();
const OUT = argOut ? path.resolve(argOut) : path.join(TEMP_DIR, 'dashboard.html');

// ---------- helpers ----------
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const readJson = (p) => { try { return JSON.parse(fs.readFileSync(p, 'utf8')); } catch { return null; } };
const git = (args) => { try { return execSync(`git ${args}`, { cwd: ROOT, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim(); } catch { return ''; } };
const toJst = (iso) => { if (!iso) return null; const d = new Date(iso); if (isNaN(d)) return null; return new Date(d.getTime() + JST_OFFSET_MIN * 60000); };
const hm = (d) => d ? `${String(d.getUTCHours()).padStart(2, '0')}:${String(d.getUTCMinutes()).padStart(2, '0')}` : '--:--';
const ymdhm = (d) => d ? `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, '0')}-${String(d.getUTCDate()).padStart(2, '0')} ${hm(d)}` : '';
const fmtDur = (ms) => { if (ms == null || ms < 0) return '-'; const m = Math.round(ms / 60000); const h = Math.floor(m / 60); return h ? `${h} 時間 ${m % 60} 分` : `${m} 分`; };
const f3 = (v) => (typeof v === 'number' ? v.toFixed(3) : '-');
const f1 = (v) => (typeof v === 'number' ? v.toFixed(1) : '-');
const f2 = (v) => (typeof v === 'number' ? v.toFixed(2) : '-');

const nowJst = new Date(Date.now() + JST_OFFSET_MIN * 60000);

// ---------- issue doc ----------
const issueText = fs.existsSync(ISSUE_FILE) ? fs.readFileSync(ISSUE_FILE, 'utf8') : '';
const fm = {};
const fmMatch = issueText.match(/^---\r?\n([\s\S]*?)\r?\n---/);
if (fmMatch) for (const line of fmMatch[1].split(/\r?\n/)) { const m = line.match(/^(\w+):\s*(.*)$/); if (m) fm[m[1]] = m[2]; }
const issueMtime = fs.existsSync(ISSUE_FILE) ? new Date(fs.statSync(ISSUE_FILE).mtimeMs + JST_OFFSET_MIN * 60000) : null;

// セクション (### N. 見出し) ごとのチェック箱
const sections = [];
{
  const parts = issueText.split(/\r?\n(?=### )/);
  for (const p of parts) {
    const h = p.match(/^### (\d+)\. (.+?)(?:\s+[—-]\s+(.+))?\s*$/m);
    if (!h) continue;
    const boxes = [...p.matchAll(/^- \[( |x|X)\] (.+)$/gm)].map((m) => ({ done: m[1] !== ' ', text: m[2].trim() }));
    const uncertain = [...p.matchAll(/^- (未確定|未対応)[:：]?\s*(.+)$/gm)].map((m) => m[1] + ' ' + m[2]);
    const pendingBold = [...p.matchAll(/\*\*([^*]*(?:まだ|要る|待ち)[^*]*)\*\*/g)].map((m) => m[1]);
    sections.push({ n: +h[1], title: h[2].trim(), note: (h[3] || '').trim(), boxes, uncertain, pendingBold });
  }
}

// ---------- git ----------
const commitsRaw = git(`log --format=%h%x1f%ad%x1f%s --date=iso-strict ${ISSUE_START_COMMIT}^..HEAD`);
const commits = commitsRaw ? commitsRaw.split('\n').map((l) => { const [h, d, s] = l.split('\x1f'); return { h, d: toJst(d), s }; }) : [];
const dogCommits = commits.filter((c) => /IMP-DOG-020|水遊び|川/.test(c.s));
const head = git('log -1 --format=%h%x1f%ad --date=iso-strict').split('\x1f');
const headTime = toJst(head[1]);
const issueStartTime = toJst(git(`log -1 --format=%ad --date=iso-strict ${ISSUE_START_COMMIT}`));

const numstat = git('diff --numstat').split('\n').filter(Boolean).map((l) => { const [a, d, f] = l.split('\t'); return { add: +a || 0, del: +d || 0, file: f }; });
const waterFileRe = /Water|River|ToyRecall|DogPack|DogWander|DogFetch|IMP-DOG-020|river-play|CourseWalkHarness|overview\.md|issue_summary/;
const uncommitted = numstat.filter((x) => waterFileRe.test(x.file) && !/\.asset$/.test(x.file));
const uncommittedAssets = numstat.filter((x) => waterFileRe.test(x.file) && /\.asset$/.test(x.file)).length;
const sceneDirty = numstat.some((x) => x.file.endsWith('VRCDefaultWorldScene.unity'));

// ---------- harness runs ----------
const runs = [];
if (fs.existsSync(RUNS_DIR)) {
  for (const d of fs.readdirSync(RUNS_DIR).filter((x) => /^\d{8}-\d{6}$/.test(x)).sort()) {
    const p = path.join(RUNS_DIR, d);
    const rep = readJson(path.join(p, 'report.json')) || {};
    let kind = null, detail = null;
    if (fs.existsSync(path.join(p, 'water.json'))) { detail = readJson(path.join(p, 'water.json')); kind = rep.waterFetch ? 'fetch' : rep.waterSwim ? 'swim' : 'water'; }
    else if (fs.existsSync(path.join(p, 'river-w5.json'))) { detail = readJson(path.join(p, 'river-w5.json')); kind = 'river'; }
    if (!kind) continue;
    const start = toJst(rep.utc), end = toJst(rep.endedUtc);
    runs.push({
      id: d, kind, start, end, status: rep.status || detail?.status || '?', reason: rep.reason || '', errors: (rep.errors || []).length,
      cases: (detail?.cases || []).map((c) => ({
        name: c.name, status: c.status, reason: c.reason || '', wet: c.wet, landed: c.landed,
        frameMsAvg: c.frameMsAvg, frameMsMax: c.frameMsMax, minPair: c.minPairDistance,
        simul: c.simultaneousWetSeconds, all6: c.allSixWetSeconds, close: c.closeSeconds,
        pickedUp: c.pickedUp, ballStillFloating: c.ballStillFloating, ballEndDistance: c.ballEndDistance, maxFetchState: c.maxFetchState, fetchEnded: c.fetchEnded, deepBallLogged: c.deepBallLogged,
        stats: (c.stats || []).map((s) => ({
          breed: s.breed, swimmer: s.swimmer, landed: s.landed, mouthLow: s.mouthLowFrames, footLow: s.footLowFrames, big: s.bigSteps,
          band: s.bandOutsideSpotFrames, tooDeep: s.tooDeepFrames, minMouth: s.minMouthAboveWater, minMouthRoot: s.minMouthAboveWaterFromRoot,
          swimS: s.swimSeconds, longest: s.longestSwim, back: s.backToShallow, wetS: s.wetSeconds, recover: s.recoverStage,
        })),
      })),
    });
  }
}
const KIND = {
  river: { menu: '75', label: '川の帯 W5', desc: '水遊び場の外で犬が川へ入らず、ボールが岸へ戻る' },
  water: { menu: '78', label: '浅瀬 W0 / W4 / W6', desc: '解除から入水、水中での要求、Owner 交代と復旧' },
  swim: { menu: '79', label: '泳ぎ W1 × 6 / W2', desc: '小型犬が浮いて泳ぎ、大型犬は浅瀬のみ。6 匹同時' },
  fetch: { menu: '88', label: '浮くボール W3 × 6 / S4 / S5b', desc: '川へ投げる → 追う → くわえる → 上陸 → 持ち帰る。大型犬は深いボールを追わない' },
};
const KINDS = Object.keys(KIND);
const latestOf = (k) => [...runs].reverse().find((r) => r.kind === k);
const latestRiver = latestOf('river'), latestWater = latestOf('water'), latestSwim = latestOf('swim'), latestFetch = latestOf('fetch');
const latestRun = runs[runs.length - 1];
const isRunning = latestRun && latestRun.status === 'RUNNING';
const isPass = (s) => /PASS$|OBSERVED$|^BUILT$/.test(String(s || '')) && !/PARTIAL/.test(String(s || ''));

// ---------- build / verify json ----------
const riverBuild = readJson(path.join(TEMP_DIR, 'river-data-build.json'));
const riverVerify = readJson(path.join(TEMP_DIR, 'river-data-verify.json'));
const spotBuild = readJson(path.join(TEMP_DIR, 'water-spot-build.json'));
const spotVerify = readJson(path.join(TEMP_DIR, 'water-spot-verify.json'));
const frisbeeRiver = readJson(path.join(TEMP_DIR, 'frisbee-river.json'));
const waterAudio = readJson(path.join(TEMP_DIR, 'water-audio.json'));

// ---------- stage states ----------
// 段階 = Issue の ### N. 見出し (数と名前は決め打ちしない)。状態は成果物 (Verify / ハーネスの最新結果) と、
// 成果物の無い段はチェック箱から決める。見出しの語で成果物を選ぶ。
const runEv = (menu, r, running) => {
  if (!r) return { ok: false, text: `Course/${menu} 未実行` };
  const passN = r.cases.filter((c) => c.status === 'PASS').length;
  return { ok: isPass(r.status), running: r.status === 'RUNNING', text: `Course/${menu} 最新 ${r.status} ${passN}/${r.cases.length} (${hm(r.start)}${r.end ? '' : '〜実行中'})` };
};
const jsonEv = (menu, j) => ({ ok: isPass(j?.status) || /_OK$/.test(String(j?.status || '')), text: `Course/${menu} ${j?.status || '未実行'}${j?.utc ? ' (' + hm(toJst(j.utc)) + ')' : ''}` });
function stageState(sec) {
  const t = sec.title;
  const evs = [];
  if (/前提/.test(t)) evs.push(jsonEv('74', riverVerify), runEv('75', latestRiver));
  else if (/拡張/.test(t)) evs.push(jsonEv('77', spotVerify), runEv('78', latestWater));
  else if (/^水遊び場/.test(t)) evs.push(jsonEv('76', spotBuild), jsonEv('77', spotVerify));
  else if (/浅瀬/.test(t)) evs.push(runEv('78', latestWater));
  else if (/泳ぎ/.test(t)) evs.push(runEv('79', latestSwim));
  else if (/ボール/.test(t)) evs.push(runEv('88', latestFetch));
  else if (/積み残し/.test(t)) evs.push(jsonEv('89', frisbeeRiver), jsonEv('87', waterAudio));
  const boxes = sec.boxes;
  const doneB = boxes.filter((b) => b.done).length;
  const userBoxes = boxes.filter((b) => /PCVR|実機|Quest|iOS|ユーザー|2 人|16 人/.test(b.text));
  const aiBoxes = boxes.filter((b) => !userBoxes.includes(b));
  const aiDone = aiBoxes.filter((b) => b.done).length;
  let state;
  if (evs.length) {
    if (evs.some((e) => e.running)) state = 'running';
    else if (evs.every((e) => e.ok)) state = aiBoxes.length && aiDone < aiBoxes.length ? 'wip' : 'done';
    else if (evs.some((e) => e.ok) || evs.some((e) => /未実行/.test(e.text) === false)) state = 'wip';
    else state = 'todo';
  } else {
    state = boxes.length && doneB === boxes.length ? 'done' : doneB ? 'wip' : 'todo';
  }
  const evidence = evs.map((e) => e.text).concat(boxes.length ? [`チェック ${doneB}/${boxes.length}` + (userBoxes.length ? ` (うちユーザー担当 ${userBoxes.filter((b) => b.done).length}/${userBoxes.length})` : '')] : []).join('・');
  return { state, evidence };
}
const STAGE_LABEL = { done: 'ClientSim 通過', running: '実行中', wip: '作業中', todo: '未着手' };

// ---------- swim breed table (latest complete swim run + latest running) ----------
const SWIM_CLEAR = 0.03; // 口 − 水面 の下限 (m)
function breedRows(run) {
  if (!run) return [];
  const rows = [];
  for (const c of run.cases) {
    for (const s of c.stats) rows.push({ case: c.name, status: c.status, ...s });
  }
  return rows;
}

// ---------- render ----------
const stages = sections.map((sec) => {
  const m = sec.title.match(/^(.+?)\s*\((.+)\)\s*$/);
  return { n: sec.n, short: `段階 ${sec.n}`, title: m ? m[1] : sec.title, sub: m ? m[2] : (sec.note || ''), note: sec.note, sec, ...stageState(sec) };
});
const doneStages = stages.filter((s) => s.state === 'done').length;
const stageCols = Math.min(4, Math.max(1, stages.length));

const caseCell = (c) => {
  const cls = c.status === 'PASS' ? 'ok' : c.status === 'FAIL' ? 'ng' : c.status === 'RUNNING' ? 'run' : 'na';
  const glyph = c.status === 'PASS' ? '✓' : c.status === 'FAIL' ? '✕' : c.status === 'PENDING' ? '…' : '–';
  return `<td class="cell ${cls}" title="${esc(c.name)}: ${esc(c.status)}${c.reason ? '\n' + esc(c.reason.slice(0, 300)) : ''}"><span class="g">${glyph}</span><span class="cn">${esc(c.name.replace(/^W\d-?/, ''))}</span></td>`;
};
function runTable(kind) {
  const rs = runs.filter((r) => r.kind === kind);
  if (!rs.length) return `<p class="muted">実行記録なし</p>`;
  const names = [...new Set(rs.flatMap((r) => r.cases.map((c) => c.name)))];
  const th = names.map((n) => `<th>${esc(n)}</th>`).join('');
  const rows = rs.map((r) => {
    const cells = names.map((n) => { const c = r.cases.find((x) => x.name === n); return c ? caseCell(c) : `<td class="cell na">–</td>`; }).join('');
    const scls = r.status === 'RUNNING' ? 'run' : isPass(r.status) ? 'ok' : 'ng';
    const pass = r.cases.filter((c) => c.status === 'PASS').length;
    return `<tr><td class="mono">${hm(r.start)}<span class="muted">〜${r.end ? hm(r.end) : '…'}</span></td><td><span class="pill ${scls}">${esc(r.status)}</span></td><td class="mono num">${pass}/${r.cases.length}</td>${cells}</tr>`;
  }).join('');
  return `<div class="scroll"><table class="matrix"><thead><tr><th>開始</th><th>結果</th><th>通過</th>${th}</tr></thead><tbody>${rows}</tbody></table></div>`;
}

function swimDetail(run) {
  if (!run) return '';
  const rows = breedRows(run);
  const w2 = run.cases.find((c) => c.name === 'W2');
  const body = rows.map((s) => {
    const mouthCls = typeof s.minMouth === 'number' && s.minMouth < 9 ? (s.minMouth >= SWIM_CLEAR ? 'ok' : 'ng') : 'na';
    const pct = typeof s.minMouth === 'number' && s.minMouth < 9 ? Math.max(0, Math.min(100, (s.minMouth / 0.1) * 100)) : 0;
    const anyBad = s.mouthLow || s.footLow || s.big || s.band || s.tooDeep;
    return `<tr>
      <td>${esc(s.case)}</td><td>${esc(s.breed)}<span class="muted"> ${s.swimmer ? '泳ぐ' : '浅瀬'}</span></td>
      <td><span class="pill ${s.status === 'PASS' ? 'ok' : s.status === 'FAIL' ? 'ng' : 'na'}">${esc(s.status)}</span></td>
      <td class="bar-cell"><span class="bar"><span class="fill ${mouthCls}" style="width:${pct}%"></span><span class="tick" style="left:${SWIM_CLEAR * 1000}%"></span></span><span class="mono num ${mouthCls === 'ng' ? 'bad' : ''}">${s.minMouth < 9 ? f3(s.minMouth) : '-'}</span></td>
      <td class="mono num">${f1(s.swimS)}</td><td class="mono num">${s.back ?? '-'}</td>
      <td class="mono num ${anyBad ? 'bad' : ''}">${[s.mouthLow, s.footLow, s.big, s.band, s.tooDeep].map((v) => v ?? '-').join(' / ')}</td>
      <td>${s.landed ? '上陸' : '<span class="bad">未</span>'}</td></tr>`;
  }).join('');
  const w2line = w2 ? `<p class="w2">W2 (6 匹同時): ${esc(w2.status)}。フレーム時間 平均 ${f2(w2.frameMsAvg)} ms・最大 ${f2(w2.frameMsMax)} ms、最小距離 ${f2(w2.minPair)} m、2 匹以上が水に ${f1(w2.simul)} s、6 匹とも ${f1(w2.all6)} s、0.3 m より近い ${f1(w2.close)} s</p>` : '';
  return `<div class="scroll"><table class="detail"><thead><tr><th>ケース</th><th>犬</th><th>結果</th><th>口 − 水面 の最小 (m、下限 ${SWIM_CLEAR})</th><th>泳いだ s</th><th>浅瀬へ戻る</th><th>違反 (口 / 足 / 飛び / 帯外 / 深すぎ)</th><th>陸</th></tr></thead><tbody>${body}</tbody></table></div>${w2line}`;
}

const failLog = runs.flatMap((r) => r.cases.filter((c) => c.status === 'FAIL').map((c) => ({ run: r, c })));

function fetchDetail(run) {
  if (!run) return '<p class="muted">Course/88 は未実行</p>';
  const body = run.cases.map((c) => {
    const st = c.stats && c.stats[0];
    return `<tr><td>${esc(c.name)}</td><td><span class="pill ${c.status === 'PASS' ? 'ok' : c.status === 'FAIL' ? 'ng' : 'na'}">${esc(c.status)}</span></td>
      <td>${c.pickedUp ? 'くわえた' : '<span class="muted">-</span>'}</td><td>${typeof c.landed === 'number' && c.landed >= 0 ? f1(c.landed) + ' s' : '<span class="muted">-</span>'}</td>
      <td>${c.ballStillFloating ? '浮いたまま' : c.fetchEnded ? '終了' : '<span class="muted">-</span>'}</td><td class="mono num">${f2(c.ballEndDistance)}</td>
      <td class="mono num">${st ? [st.mouthLow, st.footLow, st.big, st.band, st.tooDeep].map((v) => v ?? '-').join(' / ') : '-'}</td>
      <td class="muted" style="font-size:11px;overflow-wrap:anywhere">${esc((c.reason || '').slice(0, 140))}</td></tr>`;
  }).join('');
  return `<div class="scroll"><table class="detail"><thead><tr><th>ケース</th><th>結果</th><th>くわえ</th><th>上陸</th><th>ボール</th><th>ボール→人 m</th><th>違反 (口 / 足 / 飛び / 帯外 / 深すぎ)</th><th>理由</th></tr></thead><tbody>${body}</tbody></table></div>`;
}

const nextText = fm.next || '';
const elapsedIssue = issueStartTime ? nowJst - issueStartTime : null;
const sinceHead = headTime ? nowJst - headTime : null;
const totalCases = runs.reduce((a, r) => a + r.cases.length, 0);
const totalPass = runs.reduce((a, r) => a + r.cases.filter((c) => c.status === 'PASS').length, 0);

const html = `<title>川の水遊び 進捗</title>
<meta name="description" content="IMP-DOG-020 川の水遊びの実装状況 (ClientSim ハーネス・Verify・コミット)">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Zen+Kaku+Gothic+New:wght@400;500;700&family=IBM+Plex+Mono:wght@400;500&display=swap">
<style>
:root{
  --bg:#f3f5f4; --surface:#ffffff; --surface2:#e9eeec; --line:#d3dbd8; --ink:#1c2422; --ink2:#4d5a56; --muted:#7d8a86;
  --accent:#1a6f80; --accent-ink:#0f4f5c; --accent-soft:#d8ecef;
  --ok:#2c7a4b; --ok-soft:#dcefe2; --ng:#b4322a; --ng-soft:#f6dedb; --warn:#9a6300; --warn-soft:#f6e9cc; --run:#4b5fbf; --run-soft:#dfe3f7; --na:#9aa5a1; --na-soft:#eceff0;
  color-scheme:light;
}
@media (prefers-color-scheme:dark){ :root:not([data-theme="light"]){
  --bg:#141a19; --surface:#1d2523; --surface2:#26302d; --line:#36423e; --ink:#e8edeb; --ink2:#b7c2be; --muted:#8a9793;
  --accent:#5fb6c7; --accent-ink:#8fd0dc; --accent-soft:#1f3a40;
  --ok:#6cc48e; --ok-soft:#1f3a2a; --ng:#f08a80; --ng-soft:#46231f; --warn:#e0b050; --warn-soft:#3f3218; --run:#9aa9f0; --run-soft:#262d4a; --na:#7d8a86; --na-soft:#2a3230;
  color-scheme:dark; } }
:root[data-theme="dark"]{
  --bg:#141a19; --surface:#1d2523; --surface2:#26302d; --line:#36423e; --ink:#e8edeb; --ink2:#b7c2be; --muted:#8a9793;
  --accent:#5fb6c7; --accent-ink:#8fd0dc; --accent-soft:#1f3a40;
  --ok:#6cc48e; --ok-soft:#1f3a2a; --ng:#f08a80; --ng-soft:#46231f; --warn:#e0b050; --warn-soft:#3f3218; --run:#9aa9f0; --run-soft:#262d4a; --na:#7d8a86; --na-soft:#2a3230;
  color-scheme:dark; }
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.6 "Zen Kaku Gothic New","Hiragino Sans","Yu Gothic UI",system-ui,sans-serif;padding-block:0 48px;padding-inline:16px}
.wrap{max-width:1180px;margin:0 auto}
.mono{font-family:"IBM Plex Mono",ui-monospace,Consolas,monospace;font-variant-numeric:tabular-nums}
.num{text-align:right}
.muted{color:var(--muted)}
.bad{color:var(--ng);font-weight:700}
header{display:flex;flex-wrap:wrap;align-items:flex-end;gap:12px 24px;padding-block:28px 12px;border-bottom:2px solid var(--accent)}
header h1{margin:0;font-size:26px;line-height:1.2;font-weight:700;letter-spacing:.01em;text-wrap:balance}
header .id{font-size:12px;letter-spacing:.12em;text-transform:uppercase;color:var(--accent-ink);font-weight:500}
header .stamp{margin-left:auto;text-align:right;color:var(--ink2);font-size:13px}
.pill{display:inline-block;padding:1px 9px;border-radius:999px;font-size:12px;font-weight:700;letter-spacing:.04em;line-height:1.7;white-space:nowrap}
.pill.ok{background:var(--ok-soft);color:var(--ok)} .pill.ng{background:var(--ng-soft);color:var(--ng)} .pill.run{background:var(--run-soft);color:var(--run)} .pill.na{background:var(--na-soft);color:var(--na)} .pill.warn{background:var(--warn-soft);color:var(--warn)} .pill.wip{background:var(--accent-soft);color:var(--accent-ink)}
.live{animation:pulse 1.6s ease-in-out infinite} @media (prefers-reduced-motion:reduce){.live{animation:none}}
@keyframes pulse{0%,100%{opacity:1}50%{opacity:.55}}
.tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:12px;margin-block:20px}
.tile{background:var(--surface);border:1px solid var(--line);border-radius:8px;padding:12px 14px}
.tile .k{font-size:12px;color:var(--muted);letter-spacing:.06em}
.tile .v{font-size:24px;font-weight:700;line-height:1.2;margin-top:2px}
.tile .s{font-size:12px;color:var(--ink2);margin-top:4px}
h2{font-size:17px;margin:32px 0 10px;padding-bottom:6px;border-bottom:1px solid var(--line);display:flex;align-items:baseline;gap:12px}
h2 small{font-size:12px;color:var(--muted);font-weight:400}
.stages{display:grid;grid-template-columns:repeat(${stageCols},1fr);gap:8px}
@media (max-width:980px){.stages{grid-template-columns:repeat(2,1fr)}}
@media (max-width:560px){.stages{grid-template-columns:1fr}}
.stage{background:var(--surface);border:1px solid var(--line);border-left:5px solid var(--na);border-radius:6px;padding:10px 12px;min-height:120px}
.stage.done{border-left-color:var(--ok)} .stage.wip{border-left-color:var(--warn)} .stage.running{border-left-color:var(--run)} .stage.todo{border-left-color:var(--na)}
.stage .n{font-size:11px;letter-spacing:.1em;color:var(--muted);text-transform:uppercase}
.stage .t{font-weight:700;margin:2px 0}
.stage .sub{font-size:12px;color:var(--ink2)}
.stage .ev{font-size:12px;margin-top:8px;padding-top:6px;border-top:1px dashed var(--line);color:var(--ink2)}
.stage ul{margin:6px 0 0;padding-left:16px;font-size:12px;color:var(--ink2)}
.stage li.d{color:var(--ok)} .stage li.u{color:var(--ink2)}
.scroll{overflow-x:auto;background:var(--surface);border:1px solid var(--line);border-radius:8px}
table{border-collapse:collapse;width:100%;font-size:13px}
th{font-weight:500;color:var(--muted);text-align:left;padding:8px 10px;border-bottom:1px solid var(--line);white-space:nowrap;font-size:12px}
td{padding:7px 10px;border-bottom:1px solid var(--line);vertical-align:middle}
tbody tr:last-child td{border-bottom:0}
.matrix td.cell{text-align:center;padding:4px 6px;min-width:64px}
.matrix td.cell .g{display:block;font-weight:700;font-size:15px;line-height:1.1}
.matrix td.cell .cn{display:block;font-size:10px;color:var(--muted);line-height:1.2}
.matrix td.ok{background:var(--ok-soft);color:var(--ok)} .matrix td.ng{background:var(--ng-soft);color:var(--ng)} .matrix td.run{background:var(--run-soft);color:var(--run)} .matrix td.na{color:var(--na)}
.bar-cell{min-width:200px}
.bar{position:relative;display:inline-block;width:110px;height:10px;background:var(--surface2);border-radius:3px;vertical-align:middle;margin-right:8px;overflow:visible}
.bar .fill{position:absolute;left:0;top:0;bottom:0;border-radius:3px;background:var(--na)} .bar .fill.ok{background:var(--ok)} .bar .fill.ng{background:var(--ng)}
.bar .tick{position:absolute;top:-3px;bottom:-3px;width:2px;background:var(--ink)}
.w2{font-size:13px;color:var(--ink2);margin:8px 2px 0}
.timeline{list-style:none;margin:0;padding:0;border-left:2px solid var(--line);margin-left:6px}
.timeline li{position:relative;padding:4px 0 10px 18px;font-size:13px}
.timeline li::before{content:"";position:absolute;left:-6px;top:11px;width:10px;height:10px;border-radius:50%;background:var(--accent);border:2px solid var(--bg)}
.timeline li.head::before{background:var(--ok)}
.timeline .t{font-weight:500;margin-right:8px}
.two{display:grid;grid-template-columns:1fr 1fr;gap:20px} @media (max-width:860px){.two{grid-template-columns:1fr}}
.fails li{margin-bottom:6px;font-size:13px}
.fails .r{color:var(--ink2);display:block;font-size:12px;overflow-wrap:anywhere}
.next{background:var(--accent-soft);border-radius:8px;padding:12px 16px;border-left:5px solid var(--accent)}
.next p{margin:4px 0}
ul.plain{margin:6px 0;padding-left:18px;font-size:13px}
footer{margin-top:36px;font-size:12px;color:var(--muted)}
code{font-family:"IBM Plex Mono",ui-monospace,monospace;font-size:12px;background:var(--surface2);padding:1px 5px;border-radius:3px}
</style>
<div class="wrap">
<header>
  <div>
    <div class="id">${esc(ISSUE_ID)} · 古民家の裏山</div>
    <h1>川の水遊び 進捗</h1>
  </div>
  <div><span class="pill wip">${esc(fm.status || '?')}</span> ${isRunning ? `<span class="pill run live">ハーネス実行中 ${esc(latestRun.id)}</span>` : ''}</div>
  <div class="stamp">スナップショット ${ymdhm(nowJst)} JST<br><span class="muted">Issue 更新 ${ymdhm(issueMtime)} · HEAD ${esc(head[0])} ${hm(headTime)}</span></div>
</header>

<div class="tiles">
  <div class="tile"><div class="k">段階の到達</div><div class="v">${doneStages} / ${stages.length}</div><div class="s">ClientSim 通過。${stages.filter((s) => s.state === 'wip' || s.state === 'running').map((s) => '段階 ' + s.n).join('・') || '残りは未着手'} を${isRunning ? '検証中' : '作業中'}</div></div>
  <div class="tile"><div class="k">Issue 着手から</div><div class="v">${fmtDur(elapsedIssue)}</div><div class="s">${hm(issueStartTime)} に作成 (${esc(ISSUE_START_COMMIT)})</div></div>
  <div class="tile"><div class="k">コミット (今日)</div><div class="v">${dogCommits.length}</div><div class="s">最後のコミットから ${fmtDur(sinceHead)}</div></div>
  <div class="tile"><div class="k">ハーネス実行</div><div class="v">${runs.length}<span class="muted" style="font-size:14px"> 回</span></div><div class="s">ケース通過 ${totalPass} / ${totalCases} (累計)</div></div>
  <div class="tile"><div class="k">未コミットの変更</div><div class="v">+${uncommitted.reduce((a, x) => a + x.add, 0)} <span class="muted" style="font-size:14px">/ −${uncommitted.reduce((a, x) => a + x.del, 0)}</span></div><div class="s">水遊び関連 ${uncommitted.length} ファイル${sceneDirty ? '・シーン変更あり' : ''}</div></div>
</div>

<h2>段階 <small>Issue の ### 1〜${stages.length}。状態は Verify とハーネスの最新結果から。見出しの注記は Issue のまま</small></h2>
<div class="stages">
${stages.map((s) => `<div class="stage ${s.state}">
  <div class="n">${esc(s.short)}${s.note ? ` <span style="text-transform:none;letter-spacing:0;color:var(--accent-ink)">— ${esc(s.note)}</span>` : ''}</div><div class="t">${esc(s.title)}</div><div class="sub">${esc(s.sub)}</div>
  <div class="ev"><span class="pill ${s.state === 'done' ? 'ok' : s.state === 'running' ? 'run' : s.state === 'wip' ? 'warn' : 'na'}">${esc(STAGE_LABEL[s.state])}</span><br>${esc(s.evidence)}</div>
  ${s.sec && s.sec.boxes.length ? `<ul>${s.sec.boxes.map((b) => `<li class="${b.done ? 'd' : 'u'}">${b.done ? '☑' : '☐'} ${esc(b.text.replace(/\(.*?\)/g, '').slice(0, 60))}</li>`).join('')}</ul>` : ''}
</div>`).join('')}
</div>
<p class="muted" style="font-size:12px;margin-top:8px">段階の色は成果物の JSON (Verify・ハーネスの最新 run) から判定し、成果物の無い段はチェック箱で判定している。「ClientSim 通過」は PCVR・実通信・Quest/iOS の合格ではない。ユーザー担当のチェック (PCVR・実機) は色に入れていない。</p>

<h2>次にやること <small>Issue frontmatter の next</small></h2>
<div class="next">
  <p><strong>${esc(nextText)}</strong></p>
  ${stages.flatMap((s) => s.sec ? s.sec.pendingBold.map((t) => `<p>段階 ${s.n}: ${esc(t)}</p>`) : []).join('')}
  ${stages.flatMap((s) => s.sec ? s.sec.uncertain.map((t) => `<p class="muted">段階 ${s.n}: ${esc(t.slice(0, 160))}</p>`) : []).join('')}
</div>

<h2>泳ぎハーネス 79 の最新結果 <small>${latestSwim ? esc(latestSwim.id) + ' · ' + esc(latestSwim.status) + (latestSwim.end ? '' : ' (実行中、途中の値)') : '未実行'}</small></h2>
${swimDetail(latestSwim)}
${latestSwim && latestSwim.status === 'RUNNING' && runs.filter((r) => r.kind === 'swim' && r.end).length ? (() => { const prev = [...runs].reverse().find((r) => r.kind === 'swim' && r.end); return `<h2>1 つ前の完了した泳ぎハーネス <small>${esc(prev.id)} · ${esc(prev.status)}</small></h2>${swimDetail(prev)}`; })() : ''}

<h2>浮くボールハーネス 88 の最新結果 <small>${latestFetch ? esc(latestFetch.id) + ' · ' + esc(latestFetch.status) : '未実行'}</small></h2>
${fetchDetail(latestFetch)}

<h2>ハーネスの履歴 <small>Logs/kh-course-walk。セルにカーソルを載せると理由</small></h2>
${KINDS.map((k) => `<h3 style="font-size:14px;margin:18px 0 6px">Course/${KIND[k].menu} ${esc(KIND[k].label)} <span class="muted" style="font-weight:400;font-size:12px">${esc(KIND[k].desc)}</span></h3>${runTable(k)}`).join('')}

<div class="two">
<div>
<h2>今日のコミット <small>${esc(ISSUE_START_COMMIT)} 以降、水遊びに触れたもの</small></h2>
<ul class="timeline">
${dogCommits.map((c, i) => `<li class="${i === 0 ? 'head' : ''}"><span class="t mono">${hm(c.d)}</span><span class="mono muted">${esc(c.h)}</span><br>${esc(c.s)}</li>`).join('')}
</ul>
</div>
<div>
<h2>失敗したケースと原因 <small>ハーネスの reason (新しい順)</small></h2>
<ul class="fails">
${failLog.slice().reverse().slice(0, 12).map(({ run, c }) => `<li><span class="mono">${hm(run.start)}</span> <span class="pill ng">FAIL</span> <strong>${esc(c.name)}</strong> <span class="muted">(${esc(KIND[run.kind].label)})</span><span class="r">${esc(c.reason.slice(0, 260))}</span></li>`).join('') || '<li class="muted">なし</li>'}
</ul>
</div>
</div>

<h2>未コミットの作業 <small>git diff --numstat、水遊び関連</small></h2>
<div class="scroll"><table><thead><tr><th>ファイル</th><th class="num">+</th><th class="num">−</th></tr></thead><tbody>
${uncommitted.sort((a, b) => (b.add + b.del) - (a.add + a.del)).map((x) => `<tr><td class="mono">${esc(x.file)}</td><td class="mono num">${x.add}</td><td class="mono num">${x.del}</td></tr>`).join('') || '<tr><td colspan="3" class="muted">なし</td></tr>'}
</tbody></table></div>
<p class="muted" style="font-size:12px">ほかに .asset (Udon プログラム) ${uncommittedAssets} 件${sceneDirty ? ' と本番シーン' : ''} が変更されている。</p>

<h2>Verify の値 <small>Temp/imp-dog-020</small></h2>
<div class="two">
<div class="scroll"><table><thead><tr><th colspan="2">水遊び場 (76 / 77) ${spotVerify ? hm(toJst(spotVerify.utc)) : ''}</th></tr></thead><tbody>
<tr><td>Build / Verify</td><td>${esc(spotBuild?.status || '-')} / ${esc(spotVerify?.status || '-')}</td></tr>
<tr><td>格子・濡れたセル</td><td class="mono">${spotBuild?.gridNx ?? '-'}×${spotBuild?.gridNz ?? '-'} · ${spotBuild?.wetCells ?? '-'}${spotBuild?.zFrom != null ? ` (z ${f1(spotBuild.zFrom)}〜${f1(spotBuild.zTo)})` : ''}</td></tr>
<tr><td>入水点 (通過点から)</td><td class="mono">${f2(spotBuild?.entryDistance)} m</td></tr>
<tr><td>目印の石・仮看板</td><td class="mono">${Array.isArray(spotBuild?.markerStones) ? spotBuild.markerStones.length : (spotBuild?.markerStones ?? '-')} · ${spotBuild?.sign ? (typeof spotBuild.sign === 'string' ? esc(spotBuild.sign) : 'あり') : '-'}</td></tr>
<tr><td>フリスビー帯判定 (89) / 水音 (87)</td><td class="mono">${esc(frisbeeRiver?.status || '-')} / ${esc(waterAudio?.status || '-')}${waterAudio?.wavBytes ? ' (' + Math.round(waterAudio.wavBytes / 1024) + ' KB)' : ''}</td></tr>
<tr><td>水面の勾配 最大 / 平均</td><td class="mono">${spotVerify ? (spotVerify.surfaceSlopeMax * 100).toFixed(2) + ' % / ' + (spotVerify.surfaceSlopeMean * 100).toFixed(2) + ' %' : '-'}</td></tr>
<tr><td>出水点の傾き</td><td class="mono">${(spotVerify?.exitSlopeDeg || []).map((v) => v.toFixed(1)).join(' / ')} °</td></tr>
<tr><td>領域の石</td><td class="mono">${spotVerify?.stonesInRegion ?? '-'}</td></tr>
<tr><td>揺れの取り付け先</td><td class="mono" style="font-size:11px">${(spotBuild?.bobTargets || []).map(esc).join('<br>')}</td></tr>
</tbody></table></div>
<div class="scroll"><table><thead><tr><th>犬種</th><th class="num">口の高さ M</th><th class="num">浅瀬セル</th><th class="num">泳げるセル</th><th class="num">泳ぎ帯 m</th><th>泳ぐ</th></tr></thead><tbody>
${(spotVerify?.breeds || []).map((b) => `<tr><td>${esc(b.name)}</td><td class="mono num">${f3(b.mouth)}</td><td class="mono num">${b.wadeCells}</td><td class="mono num">${b.swimCells}</td><td class="mono num">${f2(b.swimWidth)}</td><td>${b.swims ? '泳ぐ' : '浅瀬のみ'}</td></tr>`).join('')}
</tbody></table></div>
</div>
<p class="muted" style="font-size:12px">川の帯 (73 / 74): ${esc(riverVerify?.status || '-')}、サンプル ${riverVerify?.samples ?? '-'}、水面下の NavMesh 点 ${riverVerify?.edgeUnderwater ?? '-'}、配線 ${riverVerify?.wanderWired ?? '-'} / ${riverVerify?.fetchWired ?? '-'} / ${riverVerify?.toyRecallWired ?? '-'} (${hm(toJst(riverVerify?.utc))})。</p>

<footer>生成: <code>node scripts/kh-issue-dashboard.js</code> → <code>${esc(path.relative(ROOT, OUT).replace(/\\/g, '/'))}</code>。正本は Issue 文書 <code>docs/development/issues/IMP-DOG-020-river-wading-and-swim.md</code> と設計書。</footer>
</div>
`;

fs.mkdirSync(path.dirname(OUT), { recursive: true });
fs.writeFileSync(OUT, html, 'utf8');
console.log(`wrote ${OUT}`);
console.log(`stages: ${stages.map((s) => `${s.n}=${s.state}`).join(' ')} | runs ${runs.length} | latest ${latestRun ? latestRun.id + ' ' + latestRun.status : '-'} | commits ${dogCommits.length}`);
