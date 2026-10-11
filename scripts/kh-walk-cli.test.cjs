const { test } = require('node:test');
const assert = require('node:assert/strict');
const { request, result } = require('./kh-walk-cli.cjs');
const name = 'bridge-190-choice-2-dogs-6';
test('hand-feed receipt accepts only its completed scenario with restoration evidence', () => {
  const args = ['Course/97e - Walk Harness Hand Feed (HF1 only, fresh profile)'];
  const receipt = () => ({ ...make(), status: 'HAND_FEED_PASS', commerceSet: 5,
    cases: [{ name: 'HF1-high-hand-to-mouth-and-recovery', status: 'PASS' }] });
  assert.equal(result(receipt(), 'walk', args).ok, true);
  for (const mutate of [j => j.commerceSet = 2, j => j.cases[0].name = 'TR6',
    j => j.cases[0].status = 'FAIL', j => j.playerDataRestored = false, j => j.errors.push('runtime error')]) {
    const j = receipt(); mutate(j); assert.equal(result(j, 'walk', args).ok, false);
  }
});
const make = () => ({status:'SEGMENTS_OBSERVED', endedUtc:'2026-10-02T00:00:00Z', sceneFileUnchanged:true,
  settingsRestored:true, playerDataRestored:true, errors:[], selectedWalkCase:name, lapChoice:0,
  cases:[{name,status:'SEGMENT_OBSERVED'}]});

test('all 18 existing segment combinations are selectable; malformed names fail before Unity', () => {
  for (const site of ['bridge-190','bridge-490','gate-wrap']) for (const choice of [2,3]) for (const count of [1,3,6])
    assert.match(request('segment',[`${site}-choice-${choice}-dogs-${count}`]).params.code,/RunSegment/);
  for (const bad of ['', 'bridge-191-choice-2-dogs-6', name+'\n', name+'"; return true;', 'gate-wrap-choice-4-dogs-6'])
    assert.throws(() => request('segment',[bad]));
  assert.throws(() => request('segment',[name,'extra']));
});
test('ordinary walk cannot start either continuous lap', () => {
  for (const n of [53,54]) assert.throws(() => request('walk',[`Course/${n} - Walk Harness Six Dogs Full Lap`]));
  assert.throws(() => request('walk',[]));
  assert.match(request('walk',['Course/40 - Walk Harness Smoke']).params.code,/ExecuteMenuItem/);
});
test('lap requires a direction and purpose; Japanese and special characters round-trip as data', () => {
  for (const args of [[], ['forward'], ['sideways','--reason','test'], ['forward','--reason','  ']])
    assert.throws(() => request('lap',args));
  const reason = '長時間 "follow" `tick` $() \\ boundary';
  const code = request('lap',['reverse','--reason',reason]).params.code;
  assert.match(code,/RunLap\(3,/);
  assert.equal(Buffer.from(code.match(/FromBase64String\("([^"]+)"\)/)[1],'base64').toString('utf8'),reason);
});
test('a completed selected segment succeeds without upgrading its status to PASS', () => {
  const j=make(); assert.equal(result(j,'segment',[name]).ok,true); assert.equal(j.status,'SEGMENTS_OBSERVED');
  assert.equal(result(j,'walk',['Course/40 - Walk Harness Smoke']).ok,true);
});
test('wrong case, full lap, failed/pending cases and missing cleanup evidence never succeed', () => {
  for (const mutate of [j=>j.selectedWalkCase='other', j=>j.lapChoice=2, j=>j.cases.push({...j.cases[0]}),
    j=>j.cases[0].status='FAIL', j=>j.cases[0].status='NOT_RUN', j=>j.cases[0].name='other',
    j=>j.endedUtc=null, j=>j.settingsRestored=false, j=>j.playerDataRestored=false,
    j=>j.sceneFileUnchanged=false, j=>j.errors.push('runtime error'), j=>j.status='NOT_PASS']) {
    const j=make(); mutate(j); assert.equal(result(j,'segment',[name]).ok,false);
  }
});
test('lap receipt must match direction and recorded purpose', () => {
  const j={...make(),status:'LAP_OBSERVED',lapChoice:2,executionPurpose:'long follow',cases:[{name:'lap',status:'LAP_OBSERVED'}]};
  assert.equal(result(j,'lap',['forward','--reason','long follow']).ok,true);
  assert.equal(result(j,'lap',['reverse','--reason','long follow']).ok,false);
  assert.equal(result(j,'lap',['forward','--reason','other']).ok,false);
  assert.equal(result(j,'walk',['Course/40 - Walk Harness Smoke']).ok,false);
});
test('existing partial wash keeps SKIP distinct, but NOT_RUN cannot be hidden by WASH_PASS', () => {
  const j={...make(),status:'WASH_PASS',cases:[{name:'W-foam',status:'PASS'},{name:'W-remote',status:'SKIP'}]};
  const args=['Course/100a - Harness Wash base (foam, rinse, hold, clean, reject)'];
  assert.equal(result(j,'walk',args).ok,true);
  j.cases[1].status='NOT_RUN'; assert.equal(result(j,'walk',args).ok,false);
});
