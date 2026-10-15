// Executes the arithmetic/boolean bodies extracted from the actual C# runtime predicates.
// This does not simulate Udon networking, ownership callbacks, root movement or Unity frames.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
const runtimePath = 'Assets/KominkaHillside/Scripts/KH_HalloweenParade.cs';
const source = fs.readFileSync(path.join(root, runtimePath), 'utf8');
const names = ['CanAcceptAnswer', 'CanSeal', 'AllSix', 'CanBeginShow', 'PhaseAfterOwnerChange', 'DogInMask', 'BlocksDog', 'FailedDogs', 'RecoveryExpired', 'CanResumeAfterParade'];
const fetch = fs.readFileSync(path.join(root, 'Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogFetchController.Owner.cs'), 'utf8');
let body = 'const Idle=0, Consent=1, Reserve=2, EndUses=3, Place=4, Playing=5, Restore=6, RecoveryFailed=7;\n';
for (const name of names) {
  const found = (source + fetch).match(new RegExp(`public static (?:bool|int) ${name}\\(([^)]*)\\)\\s*\\{([^{}]*)\\}`));
  assert.ok(found, `Runtime predicate must remain readable: ${name}`);
  const args = found[1].split(',').map(s => s.trim().replace(/^(?:int|bool)\s+/, '')).join(',');
  assert.match(found[2].trim(), /^return [\w\s=!<>&|~?:(),+-]+;$/,
    'Only the deliberately pure C# expression subset may be executed');
  body += `function ${name}(${args}) { ${found[2]} }\n`;
}
body += `return {${names.join(',')}};`;
const gates = Function(body)();
let cases = 0;
function check(label, actual, expected) { assert.equal(actual, expected, label); cases++; }
check('current unanswered requester can answer', gates.CanAcceptAnswer(1, 12, 12, 0, false), true);
check('old OK is ignored', gates.CanAcceptAnswer(1, 12, 11, 0, false), false);
check('duplicate OK is ignored', gates.CanAcceptAnswer(1, 12, 12, 0, true), false);
check('nonparticipant cannot answer', gates.CanAcceptAnswer(1, 12, 12, -1, false), false);
check('delayed OK after refusal cannot revive request', gates.CanAcceptAnswer(0, 12, 12, 0, false), false);
check('answers cannot amend a committed request', gates.CanAcceptAnswer(3, 12, 12, 0, false), false);
check('one unanswered participant cannot reserve', gates.CanSeal(1, true, true), false);
check('new participant invalidates full consent', gates.CanSeal(0, false, true), false);
check('split authority cannot reserve', gates.CanSeal(0, true, false), false);
check('all current users and authority can reserve', gates.CanSeal(0, true, true), true);
for (let mask = 0; mask < 64; mask++) check(`six-dog ACK mask ${mask}`, gates.AllSix(mask), mask === 63);
check('five ended dogs cannot start', gates.CanBeginShow(4, 31, 63), false);
check('five placed dogs cannot start', gates.CanBeginShow(4, 63, 31), false);
check('consent cannot start even with stale ACK bits', gates.CanBeginShow(1, 63, 63), false);
check('all ended and placed dogs can start', gates.CanBeginShow(4, 63, 63), true);
check('consent owner change cancels without commit', gates.PhaseAfterOwnerChange(1), 0);
check('reserve owner change cancels without commit', gates.PhaseAfterOwnerChange(2), 0);
check('ending owner change recovers', gates.PhaseAfterOwnerChange(3), 6);
check('placement owner change recovers', gates.PhaseAfterOwnerChange(4), 6);
check('playing owner change retains the clock phase', gates.PhaseAfterOwnerChange(5), 5);
check('failed recovery stays terminal after owner change', gates.PhaseAfterOwnerChange(7), 7);
check('restore expires at thirty seconds', gates.RecoveryExpired(6, 30000, 30000), true);
check('restore does not expire prematurely', gates.RecoveryExpired(6, 29999, 30000), false);
check('terminal state cannot run another timeout transition', gates.RecoveryExpired(7, 60000, 30000), false);
check('disabled sixth dog is isolated alone', gates.FailedDogs(31, 0), 32);
check('shared wash still held isolates that dog too', gates.FailedDogs(31, 2), 34);
for (let d = 0; d < 6; d++) check(`healthy admission versus isolated dog ${d}`, gates.DogInMask(32, d), d === 5);
for (let p = 0; p <= 7; p++) for (let d = 0; d < 6; d++)
  check(`phase ${p} admission dog ${d}`, gates.BlocksDog(p, 32, d), p >= 2 && p <= 6 || p === 7 && d === 5);
for (let mask = 0; mask < 16; mask++) {
  const idle = !!(mask & 1), candy = !!(mask & 2), disc = !!(mask & 4), isolated = !!(mask & 8);
  check(`pet recovery honors other activity ${mask}`, gates.CanResumeAfterParade(idle, candy, disc, isolated), idle && !candy && disc && !isolated);
}

const dog = fs.readFileSync(path.join(root, 'Assets/KominkaHillside/Scripts/KH_HalloweenParadeDog.cs'), 'utf8');
const wash = fs.readFileSync(path.join(root, 'Assets/KominkaHillside/Scripts/KH_DogWash.Parade.cs'), 'utf8');
const motion = fs.readFileSync(path.join(root, 'Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogWanderController.HillsideParade.cs'), 'utf8');
assert.ok(motion.includes('paradeDog.parade._DancePose(paradePoseTime,dogIndex)') && source.includes('rank/2==(int)((t-150f)/5f)'), 'Accepted greeting choreography uses procession pairs on five-second phrases');
// Execute the actual teardown body with small collaborators: EndUses can suppress petting before any pose existed.
const endStart = motion.indexOf('public void _EndParadePose()');
const endOpen = motion.indexOf('{', endStart);
let endClose = endOpen + 1, depth = 1;
while (depth) { if (motion[endClose] === '{') depth++; if (motion[endClose] === '}') depth--; endClose++; }
const teardown = motion.slice(endOpen + 1, endClose - 1).replace(/\b(\d+(?:\.\d+)?)f\b/g, '$1');
const runTeardown = Function('_paradePose', '_paradePetSuppressed', 'fetchController', 'IsDogOwner', 'areaCenter', 'dogRoot', 'Time',
  'let paradeActive=true, _liveCalled=false, _groupCalled=false, _look=false, _groupSit=false, _lastBusyTime=0;\n' + teardown);
for (const hadPose of [false, true]) {
  let refresh = 0;
  runTeardown(hadPose, true, {_RefreshPettingAfterParade(){refresh++;}}, () => false, null, null, {time:0});
  check(`pet suppression is released with prior pose=${hadPose}`, refresh, 1);
}
const terminalStart=dog.indexOf('if (phase == KH_HalloweenParade.RecoveryFailed)');
const terminalOpen=dog.indexOf('{',terminalStart);
let terminalClose=terminalOpen+1,terminalDepth=1;
while(terminalDepth){if(dog[terminalClose]==='{')terminalDepth++;if(dog[terminalClose]==='}')terminalDepth--;terminalClose++;}
const terminalBody=dog.slice(terminalOpen+1,terminalClose-1);
assert.ok(/return\s*;/.test(terminalBody)&&!/_CleanForParade|_PlaceForParade|_ReportDog/.test(terminalBody),'Terminal failure cannot replay cleanup or warp');
const runTerminal=Function('_terminalObserved','wander','fetch',terminalBody);
for(const observed of [false,true]){let refresh=0;runTerminal(observed,{_EndParadePose(){}},{_RefreshPettingAfterParade(){refresh++;}});check(`terminal isolation refresh once observed=${observed}`,refresh,observed?0:1);}
assert.ok(source.includes('phase == Idle || phase == Restore || phase == RecoveryFailed'), 'Repeated cancellation must not extend recovery deadline');
assert.ok(dog.includes('._CallerId()'), 'Washing consent must use the Wash button requester');
assert.ok(!/paradeWashUsers|paradeWashSeen|ParadeNoteWasher|ParadePruneUsers|_ParadeOverflow/.test(source + dog + wash),
  'Joint-washer registry was explicitly removed by the user');
assert.ok(!/Networking\.SetOwner\s*\(/.test(source + dog + wash), 'Parade does not take dog/resource ownership');
assert.ok(source.includes('sender != Networking.GetOwner(dogs[dog].root.gameObject)'), 'Dog ACK sender must own the visual root');
assert.ok(source.includes('id != requestId') && source.includes('step != phase'), 'Dog ACK must match request and current phase');
assert.ok(dog.includes('!parade.ShowStarted && placedRequest != id'), 'Recovery after Playing must not replay placement');
assert.ok(wash.includes('if (paradeCleanRequest == id) return true;'), 'Cleaning must be idempotent by committed request');
assert.ok(source.includes('AllSix(restoredMask) && SharedUsesReleased()'), 'Recovery cannot finish on a fixed timer');
assert.ok(source.includes('!_CanHost(caller) || phase != Idle'), 'Owner must authenticate hosting entitlement');
assert.ok(source.includes('hostingProduct.OwnedBy(player)'), 'Hosting requires the caller own the dedicated CE product');
assert.ok(!/if\s*\(developmentEnabled\)/.test(source), 'Retired preview switch cannot bypass paid hosting');
console.log(`PASS Halloween parade: ${cases} runtime predicate cases; consent identity, ACK authentication, recovery and default gate contracts.`);
console.log('Not covered: Unity/Udon compilation, actual callbacks, serialization order, movement, shared-resource side effects or multi-client/device behavior.');
