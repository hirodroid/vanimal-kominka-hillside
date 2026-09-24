// フリスビー ClientSim ハーネスの入口。Unity への呼び出しは必ず scripts/unity-api.sh を通す。
// 使い方:  node scripts/kh-frisbee-harness.cjs start|status|stop|list
//            [--variants=0,4] [--shots=flat,low] [--budget=600] [--mode=netorder|sixdogs] [--repeats=2] [--breed=Lab]
//
//   start   Unity のメニュー Tools/KominkaHillside/Frisbee/7 Harness (ClientSim) を叩くだけ。
//           返る START_REQUEST_ACCEPTED は「受け付けた」であって PASS ではない。status で結果を見る。
//   status  Logs/kh-frisbee-harness/latest.json の要約。
//   stop    停止フラグを置き、メニューの 8 Harness Stop も叩く。結果は BLOCKED。
//   list    実行する投げの表・netorder のケース表と、この台が保証しないもの。
//
//   --mode=netorder は BUG-DOG-003 用。36 投げの代わりに、通知が先で held が後、という
//   **順序**の 4 ケースを repeats 回 (既定 2) 流す。合否は req の行の順序 (netOk) だけ。
//
//   --breed=<Key> (IMP-DOG-012) は試作シーンの犬種レーンを選ぶ (Corgi / Pome / Lab / Dachs / Shiba / Samoyed、既定 Lab)。
//   Lab 以外のレーンは Lab と同じ配置を x へずらしたもので、投げ表と期待する終わり方は同じ表を使う。
//   --mode=sixdogs (IMP-DOG-012) は負荷測定。各周で Lab 1 匹 → 6 匹同時 に flat を投げ、同じ区間の frameTime
//   (上限を外した値)・描画コール数・計画器の候補探索回数を並べる。repeats 周 (既定 2)。
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
// IMP-DOG-012: 試作シーンのレーン (KH_FrisbeePrototypeEditor.LaneBreeds = KH_FrisbeeJumpClipEditor.Breeds と同じ順)。
const BREEDS = ['Corgi', 'Pome', 'Lab', 'Dachs', 'Shiba', 'Samoyed'];
const VARIANTS = [
  { v: 0, name: 'Normal' }, { v: 1, name: 'RunInSide' }, { v: 2, name: 'RunInFar' },
  { v: 3, name: 'RunInBeside' }, { v: 4, name: 'Twist' }, { v: 5, name: 'Curve' },
];
// BUG-DOG-003 の --mode=netorder。Unity 側の KH_FrisbeeHarnessEditor.NetCases と同じ表を持つ
// (Shots / SHOTS と同じく、直すときは 2 か所そろえる)。
// 切り替えは捕り手の debugHeldOverride (0 = 実値 / 1 = 保持扱い / -1 = 解放扱い)。
// 時刻はすべて KH_FrisbeeCatch.PendingHoldSeconds (= Hold) から導く。CLI 側には数字を持たない。
const NET_CASES = [
  {
    name: 'held-first', variant: 0, shot: 'flat',
    setup: '投げる前に override=1 (まだ手の中)。犬が Ready になるまで最大 3 秒待つ',
    switch: 'Interact のあと Hold x 0.25 で override=0 (手を離す)',
    expect: 'req hold -> req resolved -> throw -> plan=',
    netOk: '上の順序 + outcome が caught か ground + 一周が終わった (completed)',
    note: '直したい順序そのもの。通知が先に着き、円盤はまだ手の中に見える',
  },
  {
    name: 'release-first', variant: 0, shot: 'flat',
    setup: 'override=0 のまま (従来どおり)',
    switch: 'なし',
    expect: 'req accept path=local (req hold は出ない)',
    netOk: '上 + 既定モードの 4 条件 (outcomeOk / completed / motionOk / expectKind)',
    note: '対照。保留の実装を入れても普通の投げが今までどおり通ることを見る',
  },
  {
    name: 'expired', variant: 0, shot: 'flat',
    setup: '投げる前に override=1、犬が Ready になるまで待つ',
    switch: 'Hold x 1.3 まで 1 のまま (req expired を観測) -> 0 に戻し、さらに 2.0 秒観測',
    expect: 'req hold -> req expired、その後は静か',
    netOk: '上 + 失効のあと throw / plan= / req resolved が 1 行も出ない + state が Idle/Ready のまま + 2.0 秒を最後まで観測できた',
    note: '手の中のまま期限を越えたら投球は捨てる。あとから手を離しても蘇らないこと',
  },
  {
    name: 'regrab', variant: 0, shot: 'flat',
    setup: 'override=0 (普通に受理させる)',
    switch: '最初の plan= を見たら override=1 (掴み直し) -> Hold x 0.5 後に 0',
    expect: 'req accept -> plan= -> req cancel reason=held-regrab',
    netOk: '上 + 取消から 1.0 秒以内に state が Idle/Ready へ戻る',
    note: '解放を 1 度見た後の手は新しい掴みとして勝つ。古い held で追走が消えないこと',
  },
  {
    name: 'steal-rethrow', variant: 0, shot: 'flat',
    setup: 'override=0 (普通に受理させる)',
    switch: 'req accept と最初の plan= を見たら override=1 にして**その場で投げ台をもう一度 Interact** '
      + '(投げ台は実値の IsHeldNow を見るので投げは通り、捕り手は「まだ手の中」に見えて保留に入る) -> Hold x 0.25 後に 0',
    expect: 'req accept(gen N) -> plan= -> req cancel reason=held-regrab|superseded (gen N) '
      + '-> req hold(gen N+1) -> req resolved(gen N+1) -> throw -> plan=',
    netOk: '上の順序 (**古い gen の取消が新しい gen の受理より先**) + 2 投げ目の持ち帰りの証拠 '
      + '(咥えた state 3/7 か catch OK の行、かつ 返す state 8/9)',
    note: '追走中に相手が取って即投げ直す (Codex U3)。1 ケースで 2 投げぶん走るので窓を 6 秒伸ばしてある',
  },
];
const NOT_COVERED = [
  '広場の縁 (playArea)。試作シーンの捕り手は playAreaRadius = 0 なので DogMayGo は常に真で、着地点が広場の外へ出る候補を落とす規則はここでは 1 度も働かない (本シーンの Verify の幾何と実機 Play で見る)',
  'Ready (持ち手が構えている間の待機位置)。プロトタイプに持ち手がいないので走り込み系 v1..v3 は v0 と同じ投げになる',
  'VR の手投げ・狙い表示',
  // BUG-DOG-003: netorder は順序を 1 クライアントで作るだけ。通信そのものは通らない。
  '他プレイヤー・Owner 移譲・同期。--mode=netorder は debugHeldOverride で「通知が先、held が後」の順序を作り、保留の論理を通すが、通すのは 1 クライアント上の論理であって通信ではない。実際の到着順・Owner 移譲・IsHeld の同期は 2 人以上の実機でしか見ていない',
  '保留中に相手が取得 → 弱く放す (別人の保持者・Owner) は単一クライアントでは再現できない。netorder の保持者は常に円盤の Owner (= ローカルプレイヤー) なので、別人が持つ・Owner が移る・その人が弱く放すという経路は 1 度も通らない。2 人実機で確認する',
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
  const mode = arg('mode') || 'default';
  if (mode !== 'default' && mode !== 'netorder' && mode !== 'sixdogs') throw new Error('--mode must be default | netorder | sixdogs');
  const breed = arg('breed') || 'Lab';
  if (!BREEDS.includes(breed)) throw new Error('--breed must be one of ' + BREEDS.join(' | '));
  if (mode === 'sixdogs' && arg('breed')) throw new Error('--mode=sixdogs throws every lane; --breed does not apply');
  const options = {
    globalTimeoutSeconds: Number(arg('budget')) || 0,
    variants: arg('variants'),
    shots: arg('shots'),
    // Unity 側の分岐: "netorder" = 順序ケース (v0 / flat、合否は netOk)、"sixdogs" = 負荷測定 (IMP-DOG-012。
    // v0 / flat 固定で --variants / --shots は効かない。合否は犬ごとの捕獲 + 完了 (state で判定) だけで、
    // 取り方・動きの質は見ない)、空 / "default" = 従来の投げ表 (4 条件)。
    mode: mode === 'netorder' || mode === 'sixdogs' ? mode : '',
    // netorder はケース表の周回数、sixdogs は (Lab 1 匹 + 6 匹同時) の周回数。既定モードでは使わない。
    repeats: mode === 'netorder' || mode === 'sixdogs' ? (Number(arg('repeats')) || 2) : 0,
    // IMP-DOG-012: 投げるレーン (既定 Lab)。default と netorder で効く。sixdogs は全レーンを投げるので空。
    breed: mode === 'sixdogs' ? '' : breed,
  };
  fs.writeFileSync(`${base}/options.json`, JSON.stringify(options, null, 2));
  const menu = unityMenu(MENU + '7 Harness (ClientSim)');
  console.log(JSON.stringify({
    status: 'START_REQUEST_ACCEPTED',
    menu,
    options,
    plan: mode === 'netorder'
      ? `${NET_CASES.length} cases x ${options.repeats} repeats = ${NET_CASES.length * options.repeats} throws (${NET_CASES.map(c => c.name).join(', ')})`
      : mode === 'sixdogs'
        ? `${options.repeats} rounds x (Lab only + ${BREEDS.length} dogs at once) = ${options.repeats * (1 + BREEDS.length)} dog-throws, shot flat, v0`
        : `lane ${breed}`,
    note: 'Unity が Play へ入り、終わると Edit モードへ戻る。結果は status で読む。受付は PASS ではない。'
      + (mode === 'netorder'
        ? ' netorder は捕り手の debugHeldOverride を 0/1 に切り替えて req の行の順序を測る。捕り手にその変数が無ければ BLOCKED (FAIL ではない)。'
        : mode === 'sixdogs'
          ? ' sixdogs は Play 中だけ Application.targetFrameRate = -1 / QualitySettings.vSyncCount = 0 にし、どの終わり方でも戻して読み直す (frameCapRestored)。試作シーンのレーンが揃っていなければ BLOCKED (Frisbee/4 Build Scene)。'
          : ' 試作シーンにそのレーンが無ければ BLOCKED (Frisbee/4 Build Scene)。'),
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
  const throws = r.throwResults || [];
  if (r.mode === 'netorder') {
    // netorder の合否は netOk だけ。他の列 (outcome / completed / motion / expectKind) は参考。
    console.log(JSON.stringify({
      runId: r.runId,
      mode: r.mode,
      repeats: r.repeats,
      status: r.status,
      started: r.started,
      ended: r.ended,
      failure: r.failure,
      running: fs.existsSync(`${base}/journal.json`),
      throws: `${throws.filter(t => t.netOk).length}/${throws.length} netOk (planned ${r.throwsPlanned})`,
      // 全終了経路で debugHeldOverride を 0 へ戻したか (戻せていなければここに残る)
      overrideRestored: r.overrideRestored,
      cases: throws.map(t => ({
        i: t.index, case: t.netCase, netOk: t.netOk,
        // 順序の証拠。req の行の 1 本目 (通常は hold か accept) をそのまま出す
        firstReq: (t.reqLines || [])[0] || '(no [KH_FrisbeeCatch] req line)',
        reqCount: (t.reqLines || []).length,
        // 持ち帰りの証拠 (Codex M1)。Idle へ戻っただけでは一周と数えない
        roundTrip: t.netRoundTrip, states: t.netStates,
        why: t.netNote,
        // 参考列。held-first / release-first / steal-rethrow 以外では合否に使わない
        info: `outcome=${t.outcome} completed=${t.completed} motionOk=${t.motionOk} expectOk=${t.expectOk} endState=${t.endState} ${t.seconds}s`,
      })),
      failed: throws.filter(t => !t.netOk).map(t => `${t.netCase}#${t.index}: ${t.netNote}`),
      reqLines: throws.map(t => ({ case: t.netCase, i: t.index, lines: t.reqLines || [] })),
      errors: r.errors,
      sceneFileUnchanged: r.sceneFileUnchanged,
      notCovered: r.notCovered,
      scope: r.scope,
      report: `${base}/${r.runId}/report.json`,
    }, null, 2));
    process.exitCode = r.status === 'PASS' ? 0 : 1;
    return;
  }
  if (r.mode === 'sixdogs') {
    const six = r.sixdogs || {};
    console.log(JSON.stringify({
      runId: r.runId, mode: r.mode, repeats: r.repeats, status: r.status, started: r.started, ended: r.ended, failure: r.failure,
      running: fs.existsSync(`${base}/journal.json`),
      throws: `${r.throwsOk}/${throws.length} dog-throws ok (planned ${r.throwsPlanned}); ok = caught (state) + completed`,
      // 6 匹 / Lab 1 匹 の比。IMP-DOG-012 の負荷の答え (Desktop・上限なし・試作シーン)
      ratio: six.ratio,
      frameCap: six.frameCap, frameCapRestored: six.frameCapRestored, playerAt: six.playerAt,
      rounds: (six.rounds || []).map(x => ({
        round: x.round, label: x.label, dogs: x.dogs, ok: x.ok,
        flight: `${x.flight.frames}f ${x.flight.seconds}s mean ${x.flight.meanMs.toFixed(2)} p95 ${x.flight.p95Ms.toFixed(2)} max ${x.flight.maxMs.toFixed(2)} ms, batches ${x.flight.meanBatches.toFixed(0)} (max ${x.flight.maxBatches.toFixed(0)}), drawCalls ${x.flight.meanDrawCalls.toFixed(0)}, setPass ${x.flight.meanSetPass.toFixed(0)}`,
        cycle: `${x.cycle.frames}f ${x.cycle.seconds}s mean ${x.cycle.meanMs.toFixed(2)} p95 ${x.cycle.p95Ms.toFixed(2)} max ${x.cycle.maxMs.toFixed(2)} ms`,
        planner: `scans ${x.planScans} evaluations ${x.planEvaluations} hookTries ${x.planHookTries}`,
        perDog: (x.perDog || []).map(d => `${d.breed}: ${d.ok ? 'ok' : 'NG'} kind=${d.kind || '-'} caught=${d.caught} completed=${d.completed} ${d.seconds}s states=${d.states} eval=${d.planEvaluations}`),
      })),
      cannotSay: six.cannotSay,
      errors: r.errors,
      sceneFileUnchanged: r.sceneFileUnchanged,
      report: `${base}/${r.runId}/report.json`,
    }, null, 2));
    process.exitCode = r.status === 'PASS' ? 0 : 1;
    return;
  }
  const summary = {
    runId: r.runId,
    mode: r.mode || 'default',
    breed: r.breed || 'Lab',
    status: r.status,
    started: r.started,
    ended: r.ended,
    failure: r.failure,
    running: fs.existsSync(`${base}/journal.json`),
    budgetSeconds: r.budgetSeconds,
    throws: `${r.throwsOk}/${throws.length} ok (planned ${r.throwsPlanned})`,
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
    breeds: { flag: '--breed=<Key> (既定 Lab)', lanes: BREEDS, note: 'IMP-DOG-012: 試作シーンは犬種ごとのレーン (x に 12 m ずつ)。Lab レーンは従来の名前と座標のまま。Lab 以外は投げの前にプレイヤーをレーンの出現位置へ移す (犬は投げた人へ持ち帰るため)' },
    sixdogs: {
      flag: 'node scripts/kh-frisbee-harness.cjs start --mode=sixdogs [--repeats=2]',
      what: '各周: Lab 1 匹だけ (参考値) → 6 匹同時 に flat (v0) を投げる。投げてから 4 s (flight) と全員が Idle へ戻るまで (cycle) の frameTime・UnityStats の描画コール数、捕り手の _planScans / _planEvaluations / _planHookTries の増分を記録',
      pass: '犬ごとに state で「取れた (JumpCarry か、Pickup を経ずに Return)」と「一周 (動いた後に Idle)」。全員 ok・Console エラー 0・frameCapRestored が OK: で PASS。取り方の内訳と動きの質は判定しない (行が犬を名乗らない)',
      frameCap: 'Application.targetFrameRate = -1 / QualitySettings.vSyncCount = 0 で測り、終了時に戻して読み直す',
      cannotSay: '試作シーンの値 (本シーンの地形・草木・他の犬の徘徊は含まない)。Desktop の frameTime は VR の予測にならない (比率と描画コール数だけ使う)',
    },
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
    netorder: {
      flag: 'node scripts/kh-frisbee-harness.cjs start --mode=netorder [--repeats=2]',
      purpose: 'BUG-DOG-003。投げの通知 (ネットワークイベント) と円盤の held (VRCPickup の同期) は別経路で届くので、'
        + '犬の Owner では「通知が先、まだ手の中」という順序が起こる。その順序だけを 1 クライアントで作って、'
        + '保留 (pending) の論理 — 保留する / 手を離した最初のフレームで受理する / 期限を越えたら捨てる / '
        + '掴み直しで取り消す — を通す',
      how: '捕り手の debugHeldOverride (0 = 実値 / 1 = 保持扱い / -1 = 解放扱い) を投げの前後で切り替える。'
        + '切り替えの時刻は KH_FrisbeeCatch.PendingHoldSeconds から導くので、定数を変えれば追従する',
      pass: '1 ケースの合否は netOk (req の行の順序 + state の観測) だけ。全体の PASS は全ケースが netOk、'
        + 'Console エラー 0、かつ overrideRestored が OK: で始まること。'
        + 'outcome / completed / motionOk / expectKind の扱いはケースごとに違う: '
        + 'held-first は outcome (caught|ground) と completed を使い、release-first は 4 条件すべてを使い、'
        + 'steal-rethrow は state で見る持ち帰りの証拠を使う。expired / regrab では合否に使わない (参考列)',
      roundTrip: 'Codex M1: held-first / release-first / steal-rethrow は「Idle へ戻った」だけでは一周と数えない。'
        + '咥えた state (JumpCarry 3 / Pickup 7) か catch OK の行、**かつ** 返す state (Return 8 / Drop 9) を'
        + '実際に見たことを求める (plan=2 のまま取り消されて Idle に落ちた投げを通さないため)',
      blocked: '捕り手に debugHeldOverride が無ければ BLOCKED。FAIL ではない (測れていないだけ)',
      teardown: 'どの終わり方 (完了・BLOCKED・停止フラグ・番犬・例外) でも debugHeldOverride を 0 へ戻し、'
        + '**読み直して 0 を確かめてから** report の overrideRestored に残す。'
        + '確かめられなければ errors に 1 件足して FAIL にし、後続の終了経路で再試行する (Codex M2)',
      cases: NET_CASES,
      throwsPlanned: `${NET_CASES.length} x repeats (既定 2) = ${NET_CASES.length * 2} 投げ`,
      budget: `既定モードと同じ式 (計画した投げ数 x 28 s + 30 s、下限 240 s) なので ${NET_CASES.length * 2} 投げで `
        + `${Math.max(240, NET_CASES.length * 2 * 28 + 30)} s。steal-rethrow だけ 1 ケースで 2 投げぶん走るので、`
        + '判定の窓と番犬を 6 s 伸ばしてある (既定モードの 16 s / 18 s は変えていない)',
      notProven: 'これは順序の論理を 1 クライアントで通すだけ。実際の通信の到着順・Owner 移譲・IsHeld の同期は 2 人以上の実機でしか出ない',
    },
    notCovered: NOT_COVERED,
  }, null, 2));
}

try {
  const command = process.argv[2] || 'list';
  if (command === 'start') start();
  else if (command === 'stop') stop();
  else if (command === 'status') status();
  else if (command === 'list') list();
  else throw new Error('Use start | status | stop | list  [--variants=0,4] [--shots=flat,low] [--budget=600] [--mode=netorder|sixdogs] [--repeats=2] [--breed=Lab]');
} catch (e) {
  console.error(e.message);
  process.exitCode = 1;
}
