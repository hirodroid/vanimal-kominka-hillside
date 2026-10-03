const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const { suites, select, classify, runAll } = require('./kh-harness-night.cjs');
const { check } = require('./kh-harness-gate.cjs');
const { request } = require('./kh-walk-cli.cjs');

// 群ごとに新しい runId の report を返す偽の Unity。script[id] = { exit, status, cases } で結果を決める
function fake(plan, script = {}, extra = {}) {
  const calls = [], sleeps = [], stops = []; let n = 0, last = {}, md = '';
  const byArgs = (kind, args) => plan.find(s => s.kind === kind && s.args.join() === args.join());
  const rows = runAll({ plan, head: { id: 't', started: 's', confirmed: 'c', planned: plan.length, commit: 'x' },
    exec: (kind, args) => {
      const s = byArgs(kind, args), step = [].concat(script[s.id] || { exit: 0 }), r = step[Math.min(calls.filter(c => c === s.id).length, step.length - 1)];
      calls.push(s.id);
      if (r.status !== null && r.exit !== 3) last[kind === 'lap' ? 'walk' : kind] = { runId: 'r' + (++n), status: r.status || 'PASS', cases: r.cases || [{ name: 'c', status: 'PASS' }], errors: [], sceneFileUnchanged: true, ...r.report };
      return { exit: r.exit, line: 'line ' + s.id };
    },
    readReport: kind => last[kind === 'lap' ? 'walk' : kind] || null, stop: kind => stops.push(kind), sleep: s => sleeps.push(s), write: m => { md = m; }, ...extra });
  return { rows, calls, sleeps, stops, md, verdict: id => rows.find(r => r.id === id).verdict };
}

test('every suite names an existing harness menu and passes the gate and the walk CLI', () => {
  const dir = path.join(__dirname, '../Assets/KominkaHillside/Scripts/Editor/Testing');
  const src = fs.readdirSync(dir).filter(f => f.endsWith('.cs')).map(f => fs.readFileSync(path.join(dir, f), 'utf8')).join('\n');
  for (const s of suites) {
    if (s.kind === 'walk') assert.ok(src.includes(`MenuItem("Tools/KominkaHillside/${s.args[0]}")`), s.id);
    const g = check(s.kind, [...s.args, '--reason', 'r', '--consult', 'c', '--confirmed', 'u']);
    if (s.kind !== 'proto' && s.kind !== 'photo') request(s.kind, g.rest);
  }
  assert.equal(new Set(suites.map(s => s.id)).size, suites.length);
});
test('a failed group does not stop the rest, and the table lists every group', () => {
  const plan = select(['smoke', 'river', 'water'], []);
  const f = fake(plan, { river: { exit: 1, status: 'FAIL', cases: [{ name: 'W5', status: 'FAIL', reason: 'depth' }] } });
  assert.deepEqual(f.calls, ['smoke', 'river', 'water']);
  assert.deepEqual(f.rows.map(r => r.verdict), ['OK', 'NG', 'OK']);
  assert.match(f.md, /\| river \| NG \| FAIL \| 1 \| [\d.]+ \| r2 \| W5: FAIL depth \|/);
  assert.match(f.md, /OK 2 \/ 観測完了 0 \/ NG 1/);
});
test('completed states the CLI does not grade are reported apart from NG and from OK', () => {
  const ok = { status: 'TRAVEL_OBSERVED', cases: [{ name: 'T1', status: 'TRAVEL_OBSERVED' }], errors: [], sceneFileUnchanged: true };
  assert.equal(classify(1, 'walk', ok), '観測完了');
  assert.equal(classify(1, 'walk', { ...ok, status: 'TOY_ROW_PASS' }), '観測完了');
  assert.equal(classify(1, 'walk', { ...ok, cases: [{ name: 'T1', status: 'FAIL' }] }), 'NG');
  assert.equal(classify(1, 'walk', { ...ok, errors: ['runtime'] }), 'NG');
  assert.equal(classify(1, 'walk', { ...ok, sceneFileUnchanged: false }), 'NG');
  assert.equal(classify(1, 'walk', { ...ok, status: 'STOPPED' }), 'NG');
  assert.equal(classify(1, 'walk', null), '未完了');
  assert.equal(classify(2, 'walk', ok), '未完了');
  assert.equal(classify(3, 'walk', null), '未実行');
});
test('a stale report from the previous group is never credited to a group that produced no run', () => {
  const f = fake(select(['smoke', 'river'], []), { river: { exit: 1, status: null } });
  assert.equal(f.verdict('river'), '未完了');
  assert.equal(f.rows[1].status, '');
});
test('Unity busy is retried twice after a wait, then counted as not run', () => {
  const plan = select(['smoke', 'river'], []);
  const f = fake(plan, { smoke: [{ exit: 3 }, { exit: 0 }], river: { exit: 3 } });
  assert.deepEqual(f.calls, ['smoke', 'smoke', 'river', 'river', 'river']);
  assert.deepEqual(f.rows.map(r => r.verdict), ['OK', '未実行']);
  assert.deepEqual(f.sleeps, [120, 120, 120]);
});
test('three groups in a row that cannot start or finish abort the remaining groups', () => {
  const plan = select(['smoke', 'segments', 'bench', 'midway', 'river'], []);
  const f = fake(plan, { segments: { exit: 2, status: null }, bench: { exit: 3 }, midway: { exit: 2, status: null } });
  assert.deepEqual(f.rows.map(r => r.verdict), ['OK', '未完了', '未実行', '未完了', '未実行']);
  assert.ok(!f.calls.includes('river'));
  assert.match(f.rows[4].detail, /中断/);
});
test('a run that cannot finish is asked to stop before the next group, also for a lap', () => {
  const f = fake(select(['smoke', 'lap-forward'], []), { smoke: { exit: 2, status: null }, 'lap-forward': { exit: 2, status: null } });
  assert.deepEqual(f.stops, ['walk', 'walk']); assert.deepEqual(f.sleeps, [120, 120]);
});
test('missing restoration evidence stops the night instead of running on a dirty state', () => {
  for (const report of [{ playerDataRestored: false }, { settingsRestored: false }, { sceneFileUnchanged: false }]) {
    const f = fake(select(['smoke', 'river', 'water'], []), { river: { exit: 1, report } });
    assert.deepEqual(f.calls, ['smoke', 'river']);
    assert.deepEqual(f.rows.map(r => r.verdict), ['OK', 'NG', '未実行']);
    assert.match(f.rows[2].detail, /復旧を確認できなかった/);
  }
});
test('no new group starts after the time limit', () => {
  let t = 0;
  const f = fake(select(['smoke', 'river', 'water'], []), {}, { now: () => (t += 1000 * 1000), maxSeconds: 3600 });
  assert.equal(f.calls.length < 3, true);
  assert.match(f.rows[2].detail, /時間の上限/);
});
test('ledger reentry runs only after a completed ledger run', () => {
  assert.equal(fake(select(['ledger'], []), { ledger: { exit: 1, status: 'FAIL', cases: [{ name: 'L0', status: 'FAIL' }] } }).verdict('ledger-reentry'), 'OK');
  const f = fake(select(['ledger'], []), { ledger: { exit: 2, status: null } });
  assert.equal(f.verdict('ledger-reentry'), '未実行'); assert.deepEqual(f.calls, ['ledger']);
  assert.equal(fake(select(['ledger-reentry'], [])).verdict('ledger-reentry'), '未実行');
});
test('only / skip select by group prefix; unknown names are refused', () => {
  assert.deepEqual(select(['wash'], ['wash-c']).map(s => s.id), ['wash-a', 'wash-b', 'wash-d', 'wash-e', 'wash-f', 'wash-g']);
  assert.equal(select([], ['lap', 'proto', 'photo']).some(s => /^(lap|proto|photo)/.test(s.id)), false);
  assert.equal(select([], []).length, suites.length);
  assert.throws(() => select(['wahs'], []));
});
