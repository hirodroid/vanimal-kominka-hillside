// フリスビーの計画プローブ (Edit モード、Play なし) の入口。Unity への呼び出しは scripts/unity-api.sh を通す。
// 使い方:  node scripts/kh-plan-probe.cjs [proto|course] [--cases=<file.json>] [--all] [--save-baseline] [--timeout=600]
//
//   proto   Tools/KominkaHillside/Frisbee/10 Plan Probe (試作シーン: Terrain レーン 10 ケース + 投げ表 x 6 犬種)
//   adhoc   Tools/KominkaHillside/Frisbee/11 Plan Probe (ad-hoc cases, open scene): --cases のケースだけを、開いているシーンで
//   course  Tools/KominkaHillside/Course/95 - Frisbee Plan Probe (本シーン: Course/94 の 8 ケースと 94b の 11 ケース x 狙い 6 通り)
//   --cases=<file>  臨時のケース ({"cases":[{name, catcher, p0:{x,y,z}, v0:{x,y,z}, curve, dogPos, dogFwd, variant, tNow, dogSpeed,
//                   waterFetchOff, expect, continue:[{tNow, dogPos, speed, dogFwd?}]}]}) を Temp/frisbee-plan-probe-cases.json へ写してから流す (開いているシーンで)。
//                   expect は "jump|stand|over|ground|none|any|dobon|nodobon|float|deep|cannot-play|reach" の | 区切り。
//                   省略時は前回の Temp/frisbee-plan-probe-cases.json が残っていればそれも流れる (--no-cases で消す)。
//   --all   情報行 (94b の狙いごとの行など、合否に数えない行) も表に出す。
//
// メニューは同期で走る (2026-09-28: delayCall は API 経由・フォーカス無しでは走らなかった)。最初に status RUNNING の JSON を書き、
// 終わると結果で上書きする。長い run (course) では API 呼び出しが 60 s で切れるが Unity 側は続くので、ここでは JSON が
// RUNNING 以外になるまで待つ。Editor.log の [KH Plan Probe] start / stage / end 行が進み具合。
// 終了コード: PLAN_PROBE_OK = 0 / PLAN_PROBE_FAIL・ERROR・時間切れ = 1。
// 見るのは「最初の計画」だけ (docs/testing/kh-frisbee-harness.md「Play の前に: 計画プローブ」)。
const fs = require('fs');
const path = require('path');
const cp = require('child_process');

const root = path.resolve(__dirname, '..');
process.chdir(root);
const OUT = 'Temp/frisbee-plan-probe.json';
const CASES = 'Temp/frisbee-plan-probe-cases.json';
const MENUS = {
  proto: 'Tools/KominkaHillside/Frisbee/10 Plan Probe',
  course: 'Tools/KominkaHillside/Course/95 - Frisbee Plan Probe',
  adhoc: 'Tools/KominkaHillside/Frisbee/11 Plan Probe (ad-hoc cases, open scene)',
};

function bash(command) {
  const shells = ['C:/Program Files/Git/bin/bash.exe', 'bash'];
  let last;
  for (const shell of shells) {
    try { return cp.execFileSync(shell, ['-c', command], { encoding: 'utf8', timeout: 90000, cwd: root }); }
    catch (e) { last = e; }
  }
  throw new Error('bash failed: ' + (last && (last.stderr || last.message)));
}

function unityMenu(item) {
  fs.mkdirSync('Temp', { recursive: true });
  const request = `Temp/plan-probe-request-${process.pid}.json`;
  fs.writeFileSync(request, JSON.stringify({ tool: 'unity_run_csharp', params: { code: `return UnityEditor.EditorApplication.ExecuteMenuItem("${item}");` } }));
  let raw;
  try { raw = bash(`bash scripts/unity-api.sh execute "$(cat '${request}')"`); }
  finally { try { fs.unlinkSync(request); } catch (_) { /* ignore */ } }
  const outer = JSON.parse(raw);
  const inner = typeof outer.result === 'string' ? JSON.parse(outer.result) : outer.result;
  if (!outer.success || !inner || inner.success !== true || inner.resultSet !== true || inner.result !== true)
    throw new Error('Unity did not execute the menu item (compile errors? wrong project?): ' + raw);
}

function arg(name) {
  const hit = process.argv.slice(2).find(a => a === '--' + name || a.startsWith('--' + name + '='));
  if (!hit) return '';
  return hit.includes('=') ? hit.slice(name.length + 3) : 'true';
}

const sleep = ms => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
const f2 = v => (typeof v === 'number' ? v.toFixed(2) : '');
const pad = (s, n) => { s = String(s == null ? '' : s); return s.length >= n ? s : s + ' '.repeat(n - s.length); };

function main() {
  const which = process.argv[2] && !process.argv[2].startsWith('--') ? process.argv[2] : 'proto';
  if (!MENUS[which]) throw new Error('Use proto | course | adhoc  [--cases=<file.json>] [--no-cases] [--all] [--timeout=300]');
  if (arg('cases') && arg('cases') !== 'true') fs.copyFileSync(arg('cases'), CASES);
  if (arg('no-cases')) { try { fs.unlinkSync(CASES); } catch (_) { /* ignore */ } }
  const before = fs.existsSync(OUT) ? fs.statSync(OUT).mtimeMs : 0;
  const started = Date.now();
  let callError = null;
  try { unityMenu(MENUS[which]); }
  catch (e) { callError = e.message; console.error('menu call did not return cleanly (a long run outlives the 60 s API call; waiting for the JSON): ' + callError.slice(0, 300)); }
  const limit = Date.now() + (Number(arg('timeout')) || 600) * 1000;
  let rep = null, seen = false;
  while (Date.now() < limit) {
    if (fs.existsSync(OUT) && fs.statSync(OUT).mtimeMs > before) {
      seen = true;
      try { rep = JSON.parse(fs.readFileSync(OUT, 'utf8')); } catch (_) { rep = null; /* still being written */ }
      if (rep && rep.status !== 'RUNNING') break;
      rep = null;
    }
    if (!seen && callError && Date.now() - started > 30000) { console.error('NOT_STARTED: no ' + OUT + ' 30 s after a failed menu call (compile errors? Play mode? wrong project?)'); process.exitCode = 1; return; }
    sleep(1000);
  }
  if (!rep && seen) { console.error('TIMEOUT: ' + OUT + ' still RUNNING (see Editor.log [KH Plan Probe] stage lines)'); process.exitCode = 1; return; }
  if (!rep) { console.error(`TIMEOUT: ${OUT} was not written (is Unity in Edit mode with the scene saved?)`); process.exitCode = 1; return; }
  if (rep.status === 'ERROR') { console.error(`ERROR ${rep.menu}: ${rep.failure}`); process.exitCode = 1; return; }
  const all = !!arg('all');
  console.log(`${rep.menu}  scene=${rep.scene}`);
  console.log(pad('suite', 8) + pad('case', 30) + pad('kind/mode', 15) + pad('dY', 7) + pad('landing', 9) + pad('next', 34) + 'verdict');
  for (const row of rep.rows || []) {
    if (!row.counted && !all && !(row.suite === '94b' && !row.name.includes('#'))) continue;
    const r = row.r || {};
    const km = r.ok ? r.kind + (['jump', 'stand', 'over'].includes(r.kind) ? '/' + r.mode + (r.kind === 'jump' && r.landWater ? '/water' : '') : '') : '-';
    const land = r.ok && r.landingValid ? (r.landingFloat ? 'float' : 'land') + (r.landingReachable ? '+' : 'x') : '';
    const why = r.ok && r.kind !== 'jump' && r.bandIn >= 0 ? `  why=A:${r.whyA},B:${r.whyB},C:${r.whyC}` : '';
    console.log(pad(row.suite, 8) + pad(row.name, 30) + pad(km, 15) + pad(r.ok && r.kind === 'jump' ? f2(r.dY) : '', 7) + pad(land, 9)
      + pad(r.runtimeNext || '', 34) + (row.counted ? '' : '(info) ') + (row.verdict || '') + why);
    if (row.suite === 'adhoc' && row.note) for (const n of row.note.split(' || ')) console.log('          continue ' + n);
  }
  for (const n of rep.notes || []) console.log('NOTE ' + n);
  if (all) for (const row of rep.rows || []) if (row.suite !== 'adhoc' && row.note && row.note.includes(' || ')) { console.log('--- ' + row.suite + ' ' + row.name); for (const n of row.note.split(' || ')) console.log('    ' + n); }
  if (arg('save-baseline')) {
    // land controls (suite landctl): the decision signatures of THIS run become the baseline the next runs are compared with.
    // Run it on the code BEFORE a change. Rows of the other scene are kept.
    const B = 'Temp/frisbee-plan-probe-landctl-baseline.json';
    let base = { rows: [] };
    try { base = JSON.parse(fs.readFileSync(B, 'utf8')); } catch (_) { /* new */ }
    const now = (rep.rows || []).filter(r => r.suite === 'landctl' && r.sig);
    base.rows = (base.rows || []).filter(b => !now.some(r => r.name === b.name)).concat(now.map(r => ({ name: r.name, sig: r.sig })));
    fs.writeFileSync(B, JSON.stringify(base, null, 2));
    console.log(`baseline: ${now.length} land-control row(s) -> ${B}`);
  }
  console.log(`${rep.status} ${rep.pass}/${rep.total}   (${OUT})`);
  process.exitCode = rep.status === 'PLAN_PROBE_OK' ? 0 : 1;
}

try { main(); } catch (e) { console.error(e.message); process.exitCode = 1; }
