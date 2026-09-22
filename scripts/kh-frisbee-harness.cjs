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

// expectKind は「どう終わったか」の期待 (jump / stand / over / ground / any)。
// Unity 側の KH_FrisbeeHarnessEditor.Shots と同じ表を持つ (片方だけ直さないこと)。
const SHOTS = [
  { name: 'flat', speed: 7.5, elevationDeg: 10, allowed: ['caught'], expectKind: 'jump', note: '基準の投げ。跳んで取る' },
  { name: 'mid', speed: 5.8, elevationDeg: 8, allowed: ['caught'], expectKind: 'any', note: 'ClientSim の Desktop 投げの再現 (低く短い)。終わり方は問わない (round 5: 追い抜き over も全プリセットで出うる)' },
  { name: 'lob', speed: 6, elevationDeg: 35, allowed: ['caught'], expectKind: 'jump', note: '山なり。滞空が長いので先回りして跳ぶ' },
  { name: 'low', speed: 5, elevationDeg: 0, allowed: ['caught', 'ground'], expectKind: 'ground', note: '水平・低速。犬の手前に落ちるので拾いに行く' },
  { name: 'far', speed: 9, elevationDeg: 15, allowed: ['caught'], expectKind: 'jump', note: '遠投。走って追いついて跳ぶ' },
  { name: 'side', speed: 7, elevationDeg: 10, sideDeg: 25, allowed: ['caught', 'ground'], expectKind: 'any', note: '横 25 度 (round 5)。回り込みと横からの踏み切りを通す。合否ではなく etaErr を横の幾何でも採るための投げ' },
];
const VARIANTS = [
  { v: 0, name: 'Normal' }, { v: 1, name: 'RunInSide' }, { v: 2, name: 'RunInFar' },
  { v: 3, name: 'RunInBeside' }, { v: 4, name: 'Twist' }, { v: 5, name: 'Curve' },
];
const NOT_COVERED = [
  '広場の縁 (playArea)。試作シーンの捕り手は playAreaRadius = 0 なので DogMayGo は常に真で、着地点が広場の外へ出る候補を落とす規則はここでは 1 度も働かない (本シーンの Verify の幾何と実機 Play で見る)',
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
      v: t.variant, shot: t.shot, outcome: t.outcome,
      // 「取れた」と「一周終わった」は別の列 (Codex M1)。ok は両方 + 動き + 終わり方。
      caught: t.caught, completed: t.completed, ok: t.ok,
      // 終わり方 (Codex M2): 跳んで取ったのか、立って取ったのか、拾いに行ったのか
      kind: t.kind, expectKind: t.expectKind, expectOk: t.expectOk, expect: t.expectNote,
      // 動きの質 (round 4): 取れても「機械っぽい」なら ok にしない
      mode: t.mode, yawRateMax: t.yawRateMax, speedMin: t.speedMin, vTake: t.vTake, path: t.approachPath,
      // round 5: Eta の誤差 (実際 - 見積もり)。合否ではなく較正の観測値
      etaErr: t.etaErr, etaAt: t.etaAt, etaMode: t.etaMode,
      motionOk: t.motionOk, motion: t.motionNote,
      seconds: t.seconds, endState: t.endState, line: t.outcomeLine,
    })),
    caught: (r.throwResults || []).filter(t => t.caught).length,
    completed: (r.throwResults || []).filter(t => t.completed).length,
    motionFailed: (r.throwResults || []).filter(t => t.motionOk === false).length,
    expectFailed: (r.throwResults || []).filter(t => t.expectOk === false).length,
    kinds: ['jump', 'stand', 'over', 'ground'].reduce((a, k) => (a[k] = (r.throwResults || []).filter(t => t.kind === k).length, a), {}),
    // round 5: 見積もり (Eta) が楽観的かどうか。合否には使わない
    eta: r.eta,
    notCompleted: (r.throwResults || []).filter(t => t.completed === false).map(t => `v${t.variant}/${t.shot} endState=${t.endState}`),
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
    kinds: ['jump', 'stand', 'over', 'ground', 'any (期待側だけ)'],
    throwWindowSeconds: 16,
    pass: '1 投げの合否は 4 つすべて: (1) 想定内の結果 (allowed)、(2) 16 秒の窓の内に Idle へ戻って一周が終わった (completed)、'
      + '(3) 動きの質 (motion)、(4) 期待した終わり方 (expectKind)。全体の PASS は計画した投げが全部走り、全部 ok で、Console エラー 0',
    motion: 'round 4: 踏み切り / キャッチの行の mode (hook / run / stand / wait / overtake)・yawRateMax・speedMin・vTake で判定。'
      + '急旋回 (> 300 deg/s) は全 mode で不合格。wait 以外で踏み切り直前 1 秒の最小速度が 1.5 m/s 未満 (減速して跳んだ) も不合格。'
      + '跳んだのに数字が行に無い場合も不合格 (判定できない = 合格ではない)',
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
