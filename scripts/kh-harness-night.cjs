// 夜のまとめ回帰 (2026-10-03 ユーザー指定)。ユーザーが「回す」と決めた時だけ使う。普段の開発では使わない。
// 全体の群を kh-harness-run.sh で順に回し、落ちても次へ進み、Logs/kh-harness-night/latest.md に結果表を書く。
// 入口は `bash scripts/kh-harness-run.sh night --confirmed "<ユーザーの指示>"` (Git Bash の bash を引き継ぐため)。
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const walk = (id, menu, extra) => ({ id, kind: 'walk', args: ['Course/' + menu], ...extra });
// 順序: 短い群を先に、長い群と連続一周を後に。全体の入口 (100・126・88) は wait の上限 (2500 s) と
// 群ごとの結果を読むために群へ分けて回す。部分集合の入口 (45・83・94c・100g1) は入れない
const suites = [
  walk('smoke', '40 - Walk Harness Smoke'),
  walk('segments', '42 - Walk Harness Segments'),
  walk('bench', '57 - Walk Harness Viewpoint Bench'),
  walk('midway', '63 - Walk Harness Midway Attach and Return'),
  walk('river', '75 - Walk Harness River Band W5'),
  walk('water', '78 - Walk Harness Water W0 W4 W6'),
  walk('swim', '79 - Walk Harness Water Swim W1 W2'),
  walk('fetch-a', '88a - Walk Harness Water Fetch W3 (IMP-DOG-020 cases)'),
  walk('fetch-b', '88b - Walk Harness Water Fetch river-wide (IMP-DOG-021 cases)'),
  walk('travel', '82 - Walk Harness Travel (T1-T9, T11)'),
  walk('travel-rc', '82b - Walk Harness Travel RC cases (T6b, T12, T9 new)'),
  walk('travel-call', '82c - Walk Harness Travel Call (C1-C8)'),
  walk('travel-spots', '83d - Walk Harness Travel All Spots (IMP-WLD-004)'),
  walk('collar', '83b - Walk Harness Collar Interact (IMP-DOG-031)'),
  walk('collar-pattern', '129 - Walk Harness Collar Pattern (IMP-DOG-050)'),   // 2026-10-13 IMP-DOG-050 (Codex 相談: 2 ケースだけ)。--only collar は 83b とこの群の両方
  walk('nightmode', '83c - Walk Harness Night Mode Auto Manual (IMP-ENV-005)'),
  walk('live', '128 - Walk Harness Live Duration and liveId (audit RC-13)'),   // 2026-10-07 破壊監査 Phase A (RC-13)
  walk('frisbee-m2', '94 - Walk Harness Frisbee Anywhere (M2 float)'),
  walk('frisbee-m3', '94b - Walk Harness Frisbee Anywhere (M3 water)'),
  walk('ledger', '96 - Walk Harness Ledger (L0-L6, fresh profile)'),
  walk('ledger-reentry', '96b - Walk Harness Ledger Reentry (R0-R3, after 96)', { after: 'ledger' }),
  walk('treat', '97 - Walk Harness Treats (TR0-TR11, fresh profile)'),
  walk('treat-call', '97c - Walk Harness Treat Call on the Leash (TC0-TC8, fresh profile)'),
  walk('wash-a', '100a - Harness Wash base (foam, rinse, hold, clean, reject)'),
  walk('wash-b', '100b - Harness Wash behavior + react'),
  walk('wash-c', '100c - Harness Wash sync'),
  walk('wash-d', '100d - Harness Wash dirt + station'),
  walk('wash-e', '100e - Harness Wash dogs + course stations'),
  walk('wash-f', '100f - Harness Wash call + height'),
  walk('wash-g', '100g - Harness Wash hand + bottle'),
  walk('toy-a', '126a - Harness Toy Tab throw style + catch-style permission (DES-UI-002)'),
  walk('toy-b', '126b - Harness Toy Tab ball patterns (DES-DOG-002)'),
  { id: 'proto-default', kind: 'proto', args: ['default'] },
  { id: 'proto-netorder', kind: 'proto', args: ['netorder', '--repeats=2'] },
  { id: 'proto-terrain', kind: 'proto', args: ['terrain'] },
  { id: 'photo', kind: 'photo', args: [] },
  { id: 'lap-forward', kind: 'lap', args: ['forward'] },
  { id: 'lap-reverse', kind: 'lap', args: ['reverse'] },
];
const reason = '夜のまとめ回帰 (ユーザーが起動)';
const consult = '不要: ユーザーが全体を回すと決めた';
const reportFile = { walk: 'Logs/kh-course-walk/latest.json', lap: 'Logs/kh-course-walk/latest.json', proto: 'Logs/kh-frisbee-harness/latest.json', photo: 'Logs/kh-photo-harness/latest.json' };

// "wash" は wash-a〜g、"wash-c" はその 1 つを選ぶ
function select(only, skip) {
  const hit = (s, names) => names.some(n => s.id === n || s.id.startsWith(n + '-'));
  const unknown = [...only, ...skip].filter(n => !suites.some(s => hit(s, [n])));
  if (unknown.length) throw Error('知らない群: ' + unknown.join(', ') + ' (--list で一覧)');
  return suites.filter(s => (!only.length || hit(s, only)) && !hit(s, skip));
}

function badCases(kind, j) {
  if (!j) return [];
  if (kind === 'proto') return (j.throwResults || []).filter(x => !x.ok).map(x => (x.shot || '?') + ': ' + (x.terrainNote || x.expectNote || ''));
  if (kind === 'photo') return (j.cases || []).filter(c => c.status !== 'PASS').map(c => c.name + ': ' + (c.reason || c.status));
  return (j.cases || []).filter(c => /FAIL|NOT_RUN|ERROR|TIMEOUT/.test(c.status)).map(c => c.name + ': ' + c.status + ' ' + (c.reason || ''));
}

// kh-walk-cli.cjs は TRAVEL_OBSERVED・TOY_ROW_PASS など一部の完了状態に合否を出さず exit 1 を返す。
// 落ちたケースも実行時エラーも無い完了は NG と分けて「観測完了」と書く (合格ではない。読む人が status を見る)
function classify(exit, kind, j) {
  if (exit === 0) return 'OK';
  if (exit === 3 || exit === 64) return '未実行';
  if (exit === 2 || !j) return '未完了';
  const done = /(?:^PASS|_PASS|_OBSERVED|MEASURED)$/.test(j.status || '');
  return done && !badCases(kind, j).length && !(j.errors || []).length && restored(j) ? '観測完了' : 'NG';
}
// シーン・設定・PlayerData の復旧。報告に項目が無いハーネス (試作は設定・PlayerData を持たない) は false と書かれた時だけ未復旧と見る
const restored = j => j.sceneFileUnchanged !== false && j.settingsRestored !== false && j.playerDataRestored !== false;

function render(head, rows) {
  const count = v => rows.filter(r => r.verdict === v).length;
  const cell = s => String(s == null ? '' : s).replace(/\|/g, '/').replace(/\s+/g, ' ').slice(0, 300);
  return [`# 夜のまとめ回帰 ${head.id}`, '',
    `- 開始 ${head.started} / 更新 ${new Date().toISOString()}${head.finished ? ' (終了)' : ' (実行中)'}`,
    `- コミット ${head.commit} / ユーザーの指示: ${head.confirmed}`,
    `- OK ${count('OK')} / 観測完了 ${count('観測完了')} / NG ${count('NG')} / 未完了 ${count('未完了')} / 未実行 ${count('未実行')} (予定 ${head.planned})`,
    head.note ? `- ${head.note}` : null, '',
    '「観測完了」は落ちたケースも実行時エラーも無い完了で、合格の判定ではない。機能・実機の合格は status と report で読む。', '',
    '| 群 | 結果 | status | ケース | 分 | run | 落ちたケース・理由 |', '|---|---|---|---:|---:|---|---|',
    ...rows.map(r => `| ${r.id} | ${r.verdict} | ${cell(r.status)} | ${r.cases} | ${(r.seconds / 60).toFixed(1)} | ${cell(r.run)} | ${cell(r.detail)} |`), '',
    'run の証拠: 歩行・洗浄などは `Logs/kh-course-walk/<run>/report.json`、試作は `Logs/kh-frisbee-harness/`、写真は `Logs/kh-photo-harness/`。', '',
  ].filter(l => l !== null).join('\n');
}

// exec(kind, args) → { exit, line }、readReport(kind) → report | null、stop(kind)、sleep(seconds)、write(markdown)、now() → ms
// 落ちた検査は次へ進む。止めるのは、復旧が確認できない・Unity で開始や終了ができない・時間の上限、の 3 つ
function runAll({ plan, head, exec, readReport, stop, sleep, write, now = Date.now, maxSeconds = Infinity }) {
  const rows = [], began = now(); let stuck = 0, halt = '';
  const push = row => { rows.push(row); write(render(head, rows)); };
  for (const s of plan) {
    const skip = why => push({ id: s.id, verdict: '未実行', status: '', cases: '', seconds: 0, detail: why });
    if (!halt && stuck >= 3) halt = '中断: 3 群続けて Unity で開始・終了できなかった';
    if (!halt && (now() - began) / 1000 > maxSeconds) halt = '中断: 時間の上限を超えた';
    if (halt) { skip(halt); continue; }
    const need = s.after && rows.find(r => r.id === s.after);
    if (s.after && (!need || need.verdict === '未実行' || need.verdict === '未完了')) { skip('前提の ' + s.after + ' が完了していない'); continue; }
    const before = (readReport(s.kind) || {}).runId, t0 = now();
    let r = exec(s.kind, s.args);
    // exit 3 = Unity が待機状態でない (前の群の Play が残っている等)。待って 2 回までやり直す
    for (let i = 0; i < 2 && r.exit === 3; i++) { sleep(120); r = exec(s.kind, s.args); }
    const after = readReport(s.kind), j = after && after.runId !== before ? after : null;
    const verdict = classify(r.exit, s.kind, j), bad = badCases(s.kind, j);
    stuck = verdict === '未実行' || verdict === '未完了' ? stuck + 1 : 0;
    // exit 2 = 待ち切れ・開始できず。Play が残っていれば止めてもらい、次の群の前に待つ
    if (r.exit === 2) { stop(s.kind === 'lap' ? 'walk' : s.kind); sleep(120); }
    if (j && !restored(j)) halt = '中断: ' + s.id + ' の後、シーン・設定・PlayerData の復旧を確認できなかった';
    push({ id: s.id, verdict, status: j ? j.status : '', cases: j ? (j.cases || j.throwResults || []).length : '', seconds: (now() - t0) / 1000, run: j ? j.runId : '',
      detail: bad.length ? bad.join(' / ') : verdict === 'OK' || verdict === '観測完了' ? '' : r.line });
  }
  head.finished = true; write(render(head, rows));
  return rows;
}

module.exports = { suites, select, classify, badCases, runAll, render };
if (require.main === module) {
  const argv = process.argv.slice(2), opt = { only: [], skip: [], "max-hours": "8" };
  try {
    for (let i = 0; i < argv.length; i++) {
      const m = /^--(confirmed|only|skip|max-hours|list)(?:=([\s\S]*))?$/.exec(argv[i]);
      if (!m) throw Error('usage: night --confirmed "<ユーザーの指示>" [--only wash,toy] [--skip lap] [--max-hours 8] | night --list');
      if (m[1] === 'list') opt.list = true;
      else { const v = (m[2] !== undefined ? m[2] : argv[++i] || '').trim(); opt[m[1]] = m[1] === 'only' || m[1] === 'skip' ? v.split(',').filter(Boolean) : v; }
    }
    const plan = select(opt.only, opt.skip);
    if (opt.list) { console.log(plan.map(s => s.id.padEnd(16) + s.kind + ' ' + s.args.join(' ')).join('\n')); process.exit(0); }
    const maxHours = Number(opt["max-hours"]);
    if (!(maxHours > 0)) throw Error('--max-hours は正の数');
    if (!opt.confirmed || opt.confirmed.startsWith('--')) throw Error('GATE_REFUSED: night は --confirmed "<ユーザーの指示>" が必要。AI からは始めない');
    const sh = (...a) => { const r = spawnSync('bash', ['scripts/kh-harness-run.sh', ...a], { encoding: 'utf8' }); const out = ((r.stdout || '') + (r.stderr || '')).trim().split(/\r?\n/); return { exit: r.status == null ? 2 : r.status, line: out[out.length - 1] }; };
    const id = new Date().toISOString().replace(/[-:]/g, '').slice(0, 15), dir = path.join('Logs/kh-harness-night', id);
    fs.mkdirSync(dir, { recursive: true });
    const head = { id, started: new Date().toISOString(), confirmed: opt.confirmed, planned: plan.length,
      commit: (spawnSync('git', ['rev-parse', '--short', 'HEAD'], { encoding: 'utf8' }).stdout || '?').trim()
        + ((spawnSync('git', ['status', '--porcelain', '--', 'Assets', 'Packages'], { encoding: 'utf8' }).stdout || '').trim() ? ' + 未コミットの変更あり' : '') };
    const write = md => { fs.writeFileSync(path.join(dir, 'summary.md'), md); fs.writeFileSync('Logs/kh-harness-night/latest.md', md); };
    const compile = sh('compile');
    if (compile.exit !== 0) { head.note = 'コンパイルを通らなかったので何も回していない: ' + compile.line; head.finished = true; write(render(head, [])); console.log('night ABORTED ' + compile.line); process.exit(1); }
    const rows = runAll({ plan, head, write, maxSeconds: maxHours * 3600, stop: kind => sh('stop', kind),
      exec: (kind, args) => sh(kind, ...args, '--reason', reason, '--consult', consult, '--confirmed', opt.confirmed),
      readReport: kind => { try { return JSON.parse(fs.readFileSync(reportFile[kind], 'utf8')); } catch (e) { return null; } },
      sleep: s => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, s * 1000) });
    const n = v => rows.filter(r => r.verdict === v).length;
    console.log(`night ${id} OK ${n('OK')} 観測完了 ${n('観測完了')} NG ${n('NG')} 未完了 ${n('未完了')} 未実行 ${n('未実行')} → Logs/kh-harness-night/latest.md`);
    process.exit(n('NG') + n('未完了') + n('未実行') ? 1 : 0);
  } catch (e) { console.error(e.message); process.exit(64); }
}
