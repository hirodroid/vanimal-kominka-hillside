const { test } = require('node:test');
const assert = require('node:assert/strict');
const { check } = require('./kh-harness-gate.cjs');
const ok = ['--reason', '離陸直前の再判定は Play でしか取れない', '--consult', 'Codex: 100f だけで足りる'];
const washAll = 'Course/100 - Harness Wash (all groups)';
const washGroup = 'Course/100f - Harness Wash call + height';

test('every Play entry needs both a purpose and the consult conclusion', () => {
  for (const [kind, args] of [['walk', [washGroup]], ['segment', ['bridge-190-choice-2-dogs-6']], ['photo', []], ['proto', ['terrain', '--shots=T-up04']]]) {
    assert.throws(() => check(kind, args), /--reason/);
    assert.throws(() => check(kind, [...args, '--reason', 'x']), /--consult/);
    assert.throws(() => check(kind, [...args, '--consult', 'x']), /--reason/);
    assert.deepEqual(check(kind, [...args, ...ok]).rest, args);
  }
});
test('blank, missing, duplicated and multi-line values are refused', () => {
  for (const bad of [['--reason', '  ', '--consult', 'x'], ['--reason', '--consult', 'x'], ['--reason'], ['--reason=', '--consult=x'],
    ['--reason', 'a', '--reason', 'b', '--consult', 'x'], ['--reason', 'a\nb', '--consult', 'x']])
    assert.throws(() => check('photo', bad));
  const g = check('photo', ['--reason=a b', '--consult=c']);
  assert.equal(g.reason, 'a b'); assert.equal(g.consult, 'c'); assert.equal(g.wide, false);
});
test('whole-group menus, the full default table and laps need the user confirmation', () => {
  for (const [kind, args] of [['walk', [washAll]], ['walk', ['Course/42 - Walk Harness Segments']],
    ['walk', ['Course/126 - Harness Toy Tab (throw style, catch-style permission, ball patterns)']],
    ['proto', []], ['proto', ['default']], ['proto', ['default', '--budget=300']], ['lap', ['forward']]]) {
    assert.throws(() => check(kind, [...args, ...ok]), /--confirmed/);
    assert.equal(check(kind, [...args, ...ok, '--confirmed', 'ユーザー: 洗浄を確定して']).wide, true);
  }
});
test('selected groups and cases run without a confirmation', () => {
  for (const [kind, args] of [['walk', [washGroup]], ['walk', ['Course/126a - Harness Toy Tab throw style + catch-style permission (DES-UI-002)']],
    ['walk', ['Course/45 - Walk Harness Gate Boundary']], ['segment', ['gate-wrap-choice-3-dogs-6']],
    ['proto', ['default', '--variants=5', '--shots=flat,mid']], ['proto', ['terrain']], ['proto', ['netorder', '--repeats=2']], ['photo', []]])
    assert.equal(check(kind, [...args, ...ok]).wide, false);
});
test('lap hands the same purpose to the walk CLI', () => {
  const g = check('lap', ['reverse', ...ok, '--confirmed', 'ユーザー: 公開前の確定']);
  assert.deepEqual(g.rest, ['reverse', '--reason', ok[1]]);
});
test('compile and Edit-mode menu are not Play entries', () => {
  assert.throws(() => check('menu', ['Course/89 - Wire and Check Frisbee River Band', ...ok]));
  assert.throws(() => check('compile', ok));
});
