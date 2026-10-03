// Pre-run gate for kh-harness-run.sh (2026-10-03 ユーザー指定)。Unity は呼ばない。
// Play を起動する入口は「Play で判別すること」と「回す前の AI 相談の結論」が無ければ止める。
// 群の全体・連続一周は、ユーザーが確定を明示的に指示した時だけ通す (コミット・Issue 完了は確定ではない)。
const fs = require('fs');
const path = require('path');
const playKinds = ['proto', 'walk', 'segment', 'lap', 'photo'];
// 群の全体・長い群を回す入口 (部分の入口が別にあるもの: 82 → 83 / 82b / 82c、88 → 88a / 88b)。「全体」のメニューを足したらここへ足す
const wideWalkMenus = /^Course\/(?:42|82|88|100|126)\s*-/;
const ledgerPath = 'Logs/kh-harness-run/ledger.jsonl';

function parse(args) {
  const flags = {}, rest = [];
  for (let i = 0; i < args.length; i++) {
    const m = /^--(reason|consult|confirmed)(?:=([\s\S]*))?$/.exec(args[i]);
    if (!m) { rest.push(args[i]); continue; }
    if (m[1] in flags) throw Error(`--${m[1]} は 1 回だけ指定する`);
    const value = (m[2] !== undefined ? m[2] : args[++i] || '').trim();
    if (!value || value.startsWith('--')) throw Error(`--${m[1]} に内容が無い`);
    flags[m[1]] = value;
  }
  return { flags, rest };
}

function isWide(kind, rest) {
  if (kind === 'lap') return true;
  if (kind === 'walk') return wideWalkMenus.test(rest[0] || '');
  if (kind === 'proto') return (rest[0] || 'default') === 'default' && !rest.some(a => /^--(?:shots|variants)=./.test(a));
  return false;
}

function check(kind, args) {
  if (!playKinds.includes(kind)) throw Error('Expected ' + playKinds.join(' | '));
  const { flags, rest } = parse(args);
  if (rest.some(a => /[\r\n]/.test(a)) || Object.values(flags).some(v => /[\r\n]/.test(v))) throw Error('引数に改行を入れない');
  if (!flags.reason) throw Error('--reason "<この Play でしか判別できないこと>" が必要。式・Edit プローブ・静的検査で決まるなら回さない');
  if (!flags.consult) throw Error('--consult "<回す前の AI 相談 (Codex) の結論: 今回すべきか・どの群か>" が必要 (vrc-review 相談モード)。同じ差分で結論が出ていれば同じ文を使う');
  const wide = isWide(kind, rest);
  if (wide && !flags.confirmed)
    throw Error('群の全体・連続一周は --confirmed "<ユーザーの確定指示>" が必要。コミット・Issue 完了・マイルストーンは確定ではない。普段は最低限の群・ケースを選ぶ (100a〜、126a / b、83 / 82b / 82c、88a / 88b、segment、proto の --shots / --variants)');
  // lap は kh-walk-cli.cjs が目的を report.executionPurpose と照合するので、同じ文を渡す
  return { kind, rest: kind === 'lap' ? [...rest, '--reason', flags.reason] : rest, wide, ...flags };
}

function append(entry) {
  fs.mkdirSync(path.dirname(ledgerPath), { recursive: true });
  fs.appendFileSync(ledgerPath, JSON.stringify(entry) + '\n');
}

module.exports = { check, isWide, parse };
if (require.main === module) {
  const [action, ...argv] = process.argv.slice(2);
  try {
    if (action === 'begin') {
      const [kind, ...args] = argv;
      const g = check(kind, args);
      const id = new Date().toISOString();
      append({ id, kind, args: g.rest, wide: g.wide, reason: g.reason, consult: g.consult, confirmed: g.confirmed || null });
      console.log([id, ...g.rest].join('\n'));
    } else if (action === 'end') {
      append({ id: argv[0], exit: Number(argv[1]), seconds: Number(argv[2]) });
    } else throw Error('Expected begin | end');
  } catch (e) { console.error('GATE_REFUSED: ' + e.message); process.exitCode = 64; }
}
