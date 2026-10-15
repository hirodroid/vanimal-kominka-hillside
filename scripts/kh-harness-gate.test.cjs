const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');
const { check } = require('./kh-harness-gate.cjs');
const ok = ['--reason', '離陸直前の再判定は Play でしか取れない', '--consult', 'Codex: 100f だけで足りる'];
const washAll = 'Course/100 - Harness Wash (all groups)';
const washGroup = 'Course/100f - Harness Wash call + height';

test('every Play entry needs both a purpose and the consult conclusion', () => {
  for (const [kind, args] of [['walk', [washGroup]], ['segment', ['bridge-190-choice-2-dogs-6']], ['photo', []], ['parade', []], ['proto', ['terrain', '--shots=T-up04']]]) {
    assert.throws(() => check(kind, args), /--reason/);
    assert.throws(() => check(kind, [...args, '--reason', 'x']), /--consult/);
    assert.throws(() => check(kind, [...args, '--consult', 'x']), /--reason/);
    assert.deepEqual(check(kind, [...args, ...ok]).rest, args);
  }
});

test('parade permits only its two bounded cases and cannot expand by a hidden flag', () => {
  assert.equal(check('parade', ok).wide, false);
  assert.equal(check('parade', ['--night', ...ok]).wide, false);
  assert.throws(() => check('parade', ['--night', '--dance', ...ok]), /bounded/);
  assert.throws(() => check('parade', ['--all', ...ok]), /bounded/);
  assert.throws(() => check('parade', ['--cases=all', ...ok]), /bounded/);
});
test('blank, missing, duplicated and multi-line values are refused', () => {
  for (const bad of [['--reason', '  ', '--consult', 'x'], ['--reason', '--consult', 'x'], ['--reason'], ['--reason=', '--consult=x'],
    ['--reason', 'a', '--reason', 'b', '--consult', 'x'], ['--reason', 'a\nb', '--consult', 'x']])
    assert.throws(() => check('photo', bad));
  const g = check('photo', ['--reason=a b', '--consult=c']);
  assert.equal(g.reason, 'a b'); assert.equal(g.consult, 'c'); assert.equal(g.wide, false);
});
test('whole-group menus, unfiltered prototype runs and laps need the user instruction to run them', () => {
  for (const [kind, args] of [['walk', [washAll]], ['walk', ['Course/42 - Walk Harness Segments']],
    ['walk', ['Course/126 - Harness Toy Tab (throw style, catch-style permission, ball patterns)']],
    ['walk', ['Course/82 - Walk Harness Travel (T1-T9, T11)']], ['walk', ['Course/88 - Walk Harness Water Fetch W3']],
    ['walk', ['Course/94b - Walk Harness Frisbee Anywhere (M3 water)']],
    ['proto', []], ['proto', ['default']], ['proto', ['default', '--budget=300']], ['proto', ['default', '--shots=']],
    ['proto', ['terrain']], ['proto', ['netorder', '--repeats=2']], ['proto', ['sixdogs', '--shots=flat']], ['lap', ['forward']]]) {
    assert.throws(() => check(kind, [...args, ...ok]), /--confirmed/);
    assert.equal(check(kind, [...args, ...ok, '--confirmed', 'ユーザー: 今夜まとめて回して']).wide, true);
  }
});
test('selected groups and cases run without the user instruction', () => {
  for (const [kind, args] of [['walk', [washGroup]], ['walk', ['Course/126a - Harness Toy Tab throw style + catch-style permission (DES-UI-002)']],
    ['walk', ['Course/45 - Walk Harness Gate Boundary']], ['walk', ['Course/82b - Walk Harness Travel RC cases (T6b, T12, T9 new)']],
    ['walk', ['Course/88a - Walk Harness Water Fetch W3 (IMP-DOG-020 cases)']], ['walk', ['Course/83 - Walk Harness Travel Quick (T1 one dog, T11)']],
    ['walk', ['Course/94c - Walk Harness Frisbee Anywhere (M3 dobon F2 F3 only)']], ['segment', ['gate-wrap-choice-3-dogs-6']],
    ['proto', ['default', '--variants=5', '--shots=flat,mid']], ['proto', ['terrain', '--shots=T-up04']], ['photo', []]])
    assert.equal(check(kind, [...args, ...ok]).wide, false);
});
test('measurement menus enter Play too, so they need the purpose and the consult, and only those menus pass', () => {
  for (const menu of ['Course/1 - Measure Current Scene', 'Course/11 - Measure River Scenery Contribution',
    'Course/31 - Measure Course Scenery Contribution', 'Course/39 - Measure Course Occlusion Contribution']) {
    assert.throws(() => check('measure', [menu]), /--reason/);
    assert.equal(check('measure', [menu, ...ok]).wide, false);
  }
  for (const bad of [[], ['Course/2 - Stop Measurement'], [washGroup], ['Course/1 - Measure Current Scene', 'extra']])
    assert.throws(() => check('measure', [...bad, ...ok]), /measure/);
});
test('lap hands the same purpose to the walk CLI', () => {
  const g = check('lap', ['reverse', ...ok, '--confirmed', 'ユーザー: 公開前に回して']);
  assert.deepEqual(g.rest, ['reverse', '--reason', ok[1]]);
});
test('a repeated filter cannot hide a whole run behind a later partial one', () => {
  // 下位の CLI は最初の値を使う。--shots= --shots=flat は全体が走るので、門で重複を拒否する
  assert.throws(() => check('proto', ['default', '--shots=', '--shots=flat', ...ok]), /1 回だけ/);
  assert.throws(() => check('proto', ['default', '--variants=0', '--variants=4', ...ok]), /1 回だけ/);
  assert.throws(() => check('proto', ['terrain', '--mode=default', '--shots=flat', ...ok]), /--mode/);
});
test('the prototype CLI refuses a direct start and unknown filter names before touching Unity', () => {
  const run = (args, gate) => spawnSync(process.execPath, [path.join(__dirname, 'kh-frisbee-harness.cjs'), 'start', ...args],
    { encoding: 'utf8', cwd: os.tmpdir(), env: { ...process.env, KH_HARNESS_GATE: gate } });
  let r = run(['--shots=flat'], '');
  assert.equal(r.status, 1); assert.match(r.stderr, /GATE_REFUSED/);
  r = run(['--shots=flta'], 't');
  assert.equal(r.status, 1); assert.match(r.stderr, /unknown shot\(s\) flta/);
  r = run(['--variants=9'], 't');
  assert.equal(r.status, 1); assert.match(r.stderr, /unknown variant\(s\) 9/);
  r = run(['--shots=', '--shots=flat'], 't');
  assert.equal(r.status, 1); assert.match(r.stderr, /1 回だけ/);
});
// シェルの境界: Unity の API を「呼ばれたことを記録するだけ」の偽物に差し替える。
// 拒否では 1 度も呼ばれず、通過では門の後で初めて呼ばれることを見る (偽物は Play 中と答えるので、ハーネスは起動しない)
test('kh-harness-run.sh never reaches Unity on a refusal, and records the run once the gate passes', () => {
  const unix = p => p.replace(/\\/g, '/');
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'kh-gate-')), calls = path.join(dir, 'calls.txt'), ledger = path.join(dir, 'ledger.jsonl');
  const stub = unix(path.join(dir, 'api.sh'));
  fs.writeFileSync(stub, ['echo "$1" >> "' + unix(calls) + '"', '[ "$1" = check ] && exit 0', 'echo \'{"result":"True False"}\'', ''].join('\n'));
  const sh = (...args) => spawnSync('bash', [unix(path.join(__dirname, 'kh-harness-run.sh')), ...args],
    { encoding: 'utf8', env: { ...process.env, KH_UNITY_API: 'bash ' + stub, KH_HARNESS_LEDGER: ledger } });
  const called = () => fs.existsSync(calls) ? fs.readFileSync(calls, 'utf8').trim().split('\n') : [];
  for (const args of [['walk', washGroup], ['segment', 'bridge-190-choice-2-dogs-6', '--reason', 'x'], ['walk', washAll, ...ok], ['photo'],
    ['parade'], ['parade', '--all', ...ok], ['menu', 'Parade/10 - Bounded Harness'],
    ['measure', 'Course/1 - Measure Current Scene'], ['measure', 'Course/100a - Harness Wash base', ...ok], ['menu', 'Course/1 - Measure Current Scene'],
    ['menu', washAll], ['menu', 'Testing/Live Buy Probe (ClientSim)'], ['menu', 'Photo/3 - Harness'], ['menu', 'Course/41 - Stop Walk Harness']])
    assert.equal(sh(...args).status, 64, args.join(' '));
  assert.deepEqual(called(), []); assert.equal(fs.existsSync(ledger), false);
  const r = sh('walk', washGroup, ...ok);
  assert.equal(r.status, 3); assert.match(r.stdout, /GUARD_FAIL/);
  assert.deepEqual(called(), ['check', 'execute']);
  const lines = fs.readFileSync(ledger, 'utf8').trim().split('\n').map(l => JSON.parse(l));
  assert.deepEqual([lines[0].kind, lines[0].args, lines[0].reason, lines[0].consult], ['walk', [washGroup], ok[1], ok[3]]);
  assert.deepEqual([lines[1].id, lines[1].exit], [lines[0].id, 3]);
  // 停止は Play 中でも Unity へ届く (guard を通さない)
  sh('stop', 'walk');
  assert.deepEqual(called().slice(2), ['check', 'execute', 'execute']);
  fs.rmSync(dir, { recursive: true, force: true });
});
test('compile and Edit-mode menu are not Play entries', () => {
  assert.throws(() => check('menu', ['Course/89 - Wire and Check Frisbee River Band', ...ok]));
  assert.throws(() => check('compile', ok));
});
