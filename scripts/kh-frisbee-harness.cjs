// フリスビー ClientSim ハーネスの入口。Unity への呼び出しは必ず scripts/unity-api.sh を通す。
// 使い方:  node scripts/kh-frisbee-harness.cjs start|status|stop|list [--variants=0,4] [--shots=flat,low] [--budget=600]
//
//   start   Unity のメニュー Tools/KominkaHillside/Frisbee/7 Harness (ClientSim) を叩くだけ。
//           返る START_REQUEST_ACCEPTED は「受け付けた」であって PASS ではない。status で結果を見る。
//   status  Logs/kh-frisbee-harness/latest.json の要約。
//   stop    停止フラグを置き、メニューの 8 Harness Stop も叩く。結果は BLOCKED。
//   list    実行する投げの表と、この台が保証しないもの。
const fs = require('fs');
const path = require('path');
const cp = require('child_process');

const root = path.resolve(__dirname, '..');
process.chdir(root);
const base = 'Logs/kh-frisbee-harness';
const MENU = 'Tools/KominkaHillside/Frisbee/';

const SHOTS = [
  { name: 'flat', speed: 7.5, elevationDeg: 10, allowed: ['caught'], note: '基準の投げ' },
  { name: 'lob', speed: 6, elevationDeg: 35, allowed: ['caught'], note: '山なり。滞空が長い' },
  { name: 'low', speed: 5, elevationDeg: 0, allowed: ['caught', 'ground'], note: '水平・低速。立ったまま取る / 落ちたものを拾う のどちらも想定内' },
  { name: 'far', speed: 9, elevationDeg: 15, allowed: ['caught'], note: '遠投' },
];
const VARIANTS = [
  { v: 0, name: 'Normal' }, { v: 1, name: 'RunInSide' }, { v: 2, name: 'RunInFar' },
  { v: 3, name: 'RunInBeside' }, { v: 4, name: 'Twist' }, { v: 5, name: 'Curve' },
];
const NOT_COVERED = [
  'Ready (持ち手が構えている間の待機位置)。プロトタイプに持ち手がいないので走り込み系 v1..v3 は v0 と同じ投げになる',
  'VR の手投げ・狙い表示',
  '他プレイヤー・Owner 移譲・同期',
  '体験としての見え方 (人の Play が要る)',
  '実機ビルドと実クライアント',
];

function bash(command) {
  const shells = ['C:/Program Files/Git/bin/bash.exe', 'bash'];
  let last;
  for (const shell of shells) {
    try {
      return cp.execFileSync(shell, ['-c', command], { encoding: 'utf8', timeout: 90000, cwd: root });
    } catch (e) { last = e; }
  }
  throw new Error('bash failed: ' + (last && (last.stderr || last.message)));
}

function unityMenu(item) {
  fs.mkdirSync(base, { recursive: true });
  const request = `${base}/request-${process.pid}.json`;
  fs.writeFileSync(request, JSON.stringify({
    tool: 'unity_run_csharp',
    params: { code: `return UnityEditor.EditorApplication.ExecuteMenuItem("${item}");` },
  }));
  let raw;
  try { raw = bash(`bash scripts/unity-api.sh execute "$(cat '${request}')"`); }
  finally { try { fs.unlinkSync(request); } catch (_) { /* ignore */ } }
  const outer = JSON.parse(raw);
  const inner = typeof outer.result === 'string' ? JSON.parse(outer.result) : outer.result;
  if (!outer.success || !inner || inner.success !== true || inner.resultSet !== true || inner.result !== true)
    throw new Error('Unity did not execute the menu item: ' + raw);
  return item;
}

function arg(name) {
  const hit = process.argv.slice(3).find(a => a.startsWith('--' + name + '='));
  return hit ? hit.slice(name.length + 3) : '';
}

function start() {
  if (fs.existsSync(`${base}/journal.json`))
    throw new Error(`A previous run did not finish (${base}/journal.json). Run "stop" first, then check Unity is in Edit mode.`);
  fs.mkdirSync(base, { recursive: true });
  try { fs.unlinkSync(`${base}/stop.flag`); } catch (_) { /* ignore */ }
  const options = {
    globalTimeoutSeconds: Number(arg('budget')) || 0,
    variants: arg('variants'),
    shots: arg('shots'),
  };
  fs.writeFileSync(`${base}/options.json`, JSON.stringify(options, null, 2));
  const menu = unityMenu(MENU + '7 Harness (ClientSim)');
  console.log(JSON.stringify({
    status: 'START_REQUEST_ACCEPTED',
    menu,
    options,
    note: 'Unity が Play へ入り、終わると Edit モードへ戻る。結果は status で読む。受付は PASS ではない。',
    status_command: 'node scripts/kh-frisbee-harness.cjs status',
  }, null, 2));
}

function stop() {
  fs.mkdirSync(base, { recursive: true });
  fs.writeFileSync(`${base}/stop.flag`, new Date().toISOString());
  let menu = null, error = null;
  try { menu = unityMenu(MENU + '8 Harness Stop'); } catch (e) { error = e.message; }
  console.log(JSON.stringify({ status: 'STOP_REQUESTED', flag: `${base}/stop.flag`, menu, error }, null, 2));
}

function status() {
  const file = `${base}/latest.json`;
  if (!fs.existsSync(file)) { console.log(JSON.stringify({ status: 'NOT_TESTED' }, null, 2)); return; }
  const r = JSON.parse(fs.readFileSync(file, 'utf8'));
  const summary = {
    runId: r.runId,
    status: r.status,
    started: r.started,
    ended: r.ended,
    failure: r.failure,
    running: fs.existsSync(`${base}/journal.json`),
    budgetSeconds: r.budgetSeconds,
    throws: `${r.throwsOk}/${r.throwResults ? r.throwResults.length : 0} ok (planned ${r.throwsPlanned})`,
    perVariant: r.perVariant,
    results: (r.throwResults || []).map(t => ({
      v: t.variant, shot: t.shot, outcome: t.outcome, ok: t.ok,
      seconds: t.seconds, reachedIdle: t.reachedIdle, endState: t.endState, line: t.outcomeLine,
    })),
    errors: r.errors,
    sceneFileUnchanged: r.sceneFileUnchanged,
    notCovered: r.notCovered,
    scope: r.scope,
    report: `${base}/${r.runId}/report.json`,
  };
  console.log(JSON.stringify(summary, null, 2));
  process.exitCode = r.status === 'PASS' ? 0 : 1;
}

function list() {
  console.log(JSON.stringify({
    scene: 'Assets/Scenes/KH_FrisbeePrototype.unity',
    menu: MENU + '7 Harness (ClientSim)',
    variants: VARIANTS,
    shots: SHOTS,
    throwsPlanned: VARIANTS.length * SHOTS.length,
    outcomes: ['caught', 'miss', 'ground', 'none', 'timeout', 'blocked'],
    pass: 'すべての投げが caught か、その投げに書いた想定内の結果 (low の ground など) であること',
    notCovered: NOT_COVERED,
  }, null, 2));
}

try {
  const command = process.argv[2] || 'list';
  if (command === 'start') start();
  else if (command === 'stop') stop();
  else if (command === 'status') status();
  else if (command === 'list') list();
  else throw new Error('Use start | status | stop | list  [--variants=0,4] [--shots=flat,low] [--budget=600]');
} catch (e) {
  console.error(e.message);
  process.exitCode = 1;
}
