// Request/receipt validation for kh-harness-run.sh. No Unity calls on its own.
const fs = require('fs');
const segmentPattern = /^(?:bridge-(?:190|490)|gate-wrap)-choice-[23]-dogs-[136]$/;
const lapMenuPattern = /^(?:Tools\/KominkaHillside\/)?Course\/(?:53|54)\s*-/;
const type = 'KominkaHillside.Editor.KH_CourseWalkHarnessEditor';
const csString = value => `System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("${Buffer.from(value, 'utf8').toString('base64')}"))`;

function request(kind, args) {
  let code;
  if (kind === 'segment') {
    if (args.length !== 1 || !segmentPattern.test(args[0])) throw Error('segment <bridge-190|bridge-490|gate-wrap>-choice-<2|3>-dogs-<1|3|6>');
    code = `${type}.RunSegment("${args[0]}"); return "True";`;
  } else if (kind === 'lap') {
    if (args.length !== 3 || !['forward', 'reverse'].includes(args[0]) || args[1] !== '--reason' || !args[2].trim())
      throw Error('lap <forward|reverse> --reason "What requires a continuous ~20 minute lap?"');
    code = `${type}.RunLap(${args[0] === 'forward' ? 2 : 3}, ${csString(args[2].trim())}); return "True";`;
  } else if (kind === 'walk') {
    if (args.length !== 1) throw Error('walk <Course menu>; for a local change use segment <case>');
    if (lapMenuPattern.test(args[0])) throw Error('Full lap is not an ordinary walk check. Use lap <forward|reverse> --reason "...".');
    if (!args[0].startsWith('Course/') || /[\r\n]/.test(args[0])) throw Error('Expected a Course/ menu.');
    code = `return UnityEditor.EditorApplication.ExecuteMenuItem(${csString('Tools/KominkaHillside/' + args[0])}).ToString();`;
  } else throw Error('Expected walk | segment | lap');
  return { tool: 'unity_run_csharp', params: { code } };
}

function result(j, kind, args) {
  request(kind, args); // reject invalid expectations too
  const cases = j.cases || [], issues = [];
  if (!j.endedUtc || !j.sceneFileUnchanged || !j.settingsRestored || !j.playerDataRestored || (j.errors || []).length)
    issues.push('incomplete evidence / restoration / runtime errors');
  if (kind === 'segment' && (j.selectedWalkCase !== args[0] || j.lapChoice !== 0 || cases.length !== 1 || cases[0].name !== args[0]))
    issues.push('selected segment mismatch');
  if (kind === 'lap' && (j.lapChoice !== (args[0] === 'forward' ? 2 : 3) || j.executionPurpose !== args[2].trim() || cases.length !== 1))
    issues.push('lap direction / purpose / case count mismatch');
  if (kind === 'walk' && j.lapChoice !== 0) issues.push('unexpected continuous lap');
  // OBSERVED is successful execution of its stated local checks, never full feature/device acceptance.
  const observed = j.status === 'SEGMENTS_OBSERVED' ? 'SEGMENT_OBSERVED' : j.status === 'LAP_OBSERVED' ? 'LAP_OBSERVED' : null;
  const pass = /^(?:PASS|(?:WASH|WATER|SWIM|FETCH|W5|FRISBEE_M2|FRISBEE_M3|TRAVEL_CALL|TREAT|LEDGER|LEDGER_REENTRY|LIVE)_PASS|WATER_PASS_PARTIAL)$/.test(j.status);
  if (kind === 'segment' && j.status !== 'SEGMENTS_OBSERVED') issues.push('segment did not complete');
  if (kind === 'lap' && j.status !== 'LAP_OBSERVED') issues.push('lap did not complete');
  const failures = cases.filter(c => c.status !== (observed || 'PASS') && !(pass && c.status === 'SKIP') && !(j.status === 'WATER_PASS_PARTIAL' && c.status === 'NOT_EXERCISED'));
  if (!(pass || observed) || !cases.length || failures.length || cases.every(c => c.status === 'SKIP')) issues.push('failed / incomplete cases or unaccepted status');
  return { ok: issues.length === 0, issues, failures };
}

module.exports = { request, result, lapMenuPattern };
if (require.main === module) {
  const [action, kind, ...args] = process.argv.slice(2);
  try {
    if (action === 'request') console.log(JSON.stringify(request(kind, args)));
    else if (action === 'result') {
      const j = JSON.parse(fs.readFileSync('Logs/kh-course-walk/latest.json', 'utf8'));
      const r = result(j, kind, args);
      console.log(kind, j.runId, j.status, 'cases', j.cases.length, 'sceneUnchanged', j.sceneFileUnchanged,
        j.executionPurpose ? 'purpose=' + j.executionPurpose : '',
        r.issues.join('; '), r.failures.map(c => c.name + ': ' + c.status + ' ' + (c.reason || '').slice(0,160)).join(' | '));
      process.exitCode = r.ok ? 0 : 1;
    } else throw Error('Expected request | result');
  } catch (e) { console.error(e.message); process.exitCode = action === 'result' ? 1 : 64; }
}
