#!/usr/bin/env node
/*
 * 在庫を減らす口が 1 つしか無いことの構造ガード (IMP-TRT-001 Phase 4b)。
 *
 *   node scripts/check-consume-once.js    違反があれば exit 1
 *
 * ---------------------------------------------------------------------------
 * ⚠ **この検査は補助でしかない。** (2026-08-24 Codex 指摘で射程を書き直した)
 *
 * 「呼び出し口が 1 つ」は **実行回数を保証しない** — 同じ 1 行が
 * 遅延イベントの重複や `OnOwnershipTransferred` の再通知で 2 回踏まれれば
 * 二重消費は起きる。二重消費を実際に防いでいるのは:
 *
 *   1. `KD_TreatBox.StepDispenseWith` の**在庫が減る辺がちょうど 1 本**であること
 *   2. その辺が**必ず状態を動かす**ので、呼び出し側の
 *      「状態が動いた呼び出しでだけ副作用を実行する」ガードが効くこと
 *   3. **世代 (`evSeq`) が合わないイベントを捨てる**こと
 *
 * この 3 つは `Treat System/3 - Verify` の自己テストが機械検査する
 * (遷移表を全走査して「減る辺が 1 本」を数え上げる)。
 *
 * **ここが守るのはもっと素朴なこと** — 将来「ここでも 1 個減らしたい」と
 * **2 つ目の口が生える**のを止める。口が増えた瞬間、上の 3 つの保証は
 * 「その口を通った場合だけ」の保証に落ちる。
 * IMP-PRG-001 が獲得の口を private `Grant` 1 つに閉じたのと同じ発想を、
 * クラスを跨ぐので静的検査で実現している。
 * ---------------------------------------------------------------------------
 *
 * 検査するもの:
 *   Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/ と Assets/KominkaHillside/Scripts/ 配下で
 *   `_TryConsume(` の**呼び出し**が 2 箇所以上ない
 *
 * 裏山改変 (IMP-PRG-001 段階 1): 古民家 scripts/check-consume-once.js からの移植。見る場所を裏山の
 * 取り込み先 (Imported/KominkaDogRun/Scripts) へ替え、裏山自作の Scripts も同じ台帳を叩きうるので足した。
 *
 * 数えないもの (**単純 grep だと全部数えてしまう**):
 *   - 宣言行 (`public bool _TryConsume(int unlockId)`)
 *   - 行コメント / ブロックコメント / XML doc の中
 *   - 文字列リテラルの中
 *   - Editor/ 配下 (自己テストや Verify から呼ぶのは検査の一部であって消費ではない)
 *
 * 限界: 字句解析であって構文解析ではない。`nameof(_TryConsume)` のような
 * 「呼び出しに見えるが呼び出しでないもの」は拾えない (現状そう書いていない)。
 */

const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
// 裏山改変: 取り込み先 + 裏山自作。どちらかが無ければ NG (黙って検査を無効にしない)
const SRCS = ['Assets/KominkaHillside/Imported/KominkaDogRun/Scripts', 'Assets/KominkaHillside/Scripts'].map(p => path.join(ROOT, p));

/** 在庫を減らす唯一の口。増やすときは**この検査ごと**考え直すこと。 */
const CONSUME = '_TryConsume';

/**
 * 宣言の**見出しだけ** (修飾子 + 戻り値型 + メソッド名 + `(`)。呼び出しと区別するために修飾子を見る。
 *
 * 裏山改変 (2026-09-24 Codex M-1): 旧版は `[^;=]*` で**本文まで一致**し、1 行メソッド
 * `public void _Extra() { ledger._TryConsume(...); }` の呼び出しを宣言と誤認して行ごと除外していた。
 * 見出しは ( ) { } = ; を含まないので、それらを跨がせない。さらに行の除外をやめ、
 * **行内の一致を全件数えてから、宣言の見出し 1 件ぶんだけ引く** (countCalls)。
 */
const DECL_RE = new RegExp(`\\b(?:public|private|internal|protected)\\b[^;={}()]*?(?<![A-Za-z0-9_])${CONSUME}\\s*\\(`);

/**
 * 呼び出しの検出。**識別子の境界を見る。**
 *
 * 初版は `line.includes('_TryConsume(')` で数えていたが、これは
 * **`Foo_TryConsume(` のような別の名前まで 1 件として数える。**
 * 2026-08-24 に負のテスト (呼び出しを `NEGTEST_TryConsume` へ改名して
 * 検出されるか見る) で発覚した — **検査を締めた直後に、その検査自身の穴が出た。**
 * `\b` は `_` を単語文字として扱うので使えない (`_TryConsume` の直前で境界にならない)。
 * 明示的に「直前が識別子文字でない」を書く。
 */
const CALL_RE = new RegExp(`(?<![A-Za-z0-9_])${CONSUME}\\s*\\(`, 'g');

/** 1 行 (コメント・文字列を潰した後) の呼び出し件数。宣言の見出しは数えず、本文の呼び出しは全件数える。 */
function countCalls(line) {
  const all = (line.match(CALL_RE) || []).length;
  return DECL_RE.test(line) ? Math.max(0, all - 1) : all;
}

// ---- セルフテスト (文字列だけ。ファイルは書かない) ----
// 検査自身の穴を負の例で押さえる (Codex M-1: 1 行メソッドの 2 つ目の呼び出しを見逃して PASS していた)
{
  const cases = [
    ['public bool _TryConsume(string productKey)', 0, '宣言の見出しは数えない'],
    ['    public bool _TryConsume(string productKey) { int i = 0;', 0, '宣言 + 本文の始まり'],
    ['if (ledger._TryConsume(key)) Consume();', 1, '通常の呼び出し'],
    ['public void _Extra() { ledger._TryConsume("treat.bone_biscuit"); }', 1, '1 行メソッドの本文の呼び出し'],
    ['public bool _Extra() => ledger._TryConsume("treat.bone_biscuit");', 1, '式形式メソッドの呼び出し'],
    ['a._TryConsume(x); b._TryConsume(y);', 2, '同一行の複数呼び出し'],
    ['public bool _TryConsume(string k) { return other._TryConsume(k); }', 1, '宣言と同じ行の本文の呼び出し'],
    ['Foo_TryConsume(x); NEGTEST_TryConsume(y);', 0, '別の名前は数えない'],
    ['// ledger._TryConsume(x);', 0, 'コメントは数えない'],
  ];
  const bad = cases.filter(([src, want]) => countCalls(stripCommentsAndStrings(src)) !== want);
  if (bad.length) {
    console.error('[check-consume-once] NG — セルフテストが落ちた (検査自身が壊れている)');
    for (const [src, want, what] of bad)
      console.error('  ' + what + ': 期待 ' + want + ' / 実際 ' + countCalls(stripCommentsAndStrings(src)) + ' — ' + src);
    process.exit(1);
  }
}

function listCs(dir) {
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...listCs(p));
    else if (entry.name.endsWith('.cs')) out.push(p);
  }
  return out;
}

/**
 * コメントと文字列リテラルを空白へ潰す。
 * **これをやらないと doc コメントの `<see cref="_TryConsume"/>` まで数える。**
 */
function stripCommentsAndStrings(src) {
  let out = '';
  let i = 0;
  while (i < src.length) {
    const two = src.substr(i, 2);
    if (two === '//') {
      while (i < src.length && src[i] !== '\n') { out += ' '; i++; }
    } else if (two === '/*') {
      while (i < src.length && src.substr(i, 2) !== '*/') { out += src[i] === '\n' ? '\n' : ' '; i++; }
      out += '  '; i += 2;
    } else if (src[i] === '"') {
      // 逐語的文字列 (@"...") は "" だけがエスケープ。通常文字列は \ でエスケープ
      const verbatim = i > 0 && src[i - 1] === '@';
      out += ' '; i++;
      while (i < src.length) {
        if (verbatim) {
          if (src[i] === '"' && src[i + 1] === '"') { out += '  '; i += 2; continue; }
          if (src[i] === '"') break;
        } else {
          if (src[i] === '\\') { out += '  '; i += 2; continue; }
          if (src[i] === '"') break;
        }
        out += src[i] === '\n' ? '\n' : ' ';
        i++;
      }
      out += ' '; i++;
    } else {
      out += src[i]; i++;
    }
  }
  return out;
}

// **「見に行けなかった」を成功にしない** (2026-08-24 Codex 指摘)。
// SKIP で exit 0 にすると、パスがずれた日から**検査が黙って無効になる**。
for (const SRC of SRCS) if (!fs.existsSync(SRC)) {
  console.error(`[check-consume-once] NG — Scripts ディレクトリが無い: ${SRC}
  検査対象が見つからないことを成功にしてはいけない (検査が黙って無効になる)。
  パスが変わったならこのスクリプトを直すこと。`);
  process.exit(1);
}

const callSites = [];
for (const file of SRCS.flatMap(listCs)) {
  const rel = path.relative(ROOT, file).replace(/\\/g, '/');
  const lines = stripCommentsAndStrings(fs.readFileSync(file, 'utf8')).split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    const n = countCalls(lines[i]);   // 宣言の見出しは引く。本文の呼び出しは全件数える (Codex M-1)
    for (let k = 0; k < n; k++) callSites.push(`${rel}:${i + 1}`);
  }
}

if (callSites.length > 1) {
  console.error(`[check-consume-once] NG — ${CONSUME} の呼び出しが ${callSites.length} 箇所ある`);
  for (const s of callSites) console.error('  ' + s);
  console.error(`
  **在庫を減らす口は 1 つに保つ** (IMP-TRT-001 Phase 4b)。
  口が増えると「減る辺がちょうど 1 本」「状態が動いたときだけ副作用」
  「世代で冪等化」の 3 つの保証が、その口を通った場合だけの保証に落ちる。
  減らしたい局面が増えたなら、口を増やすのではなく
  KD_TreatBox の遷移表に辺を足して、そこから 1 箇所を呼ぶこと。`);
  process.exit(1);
}

if (callSites.length === 0) {
  // **0 件を成功にしない** (2026-08-24 Codex 指摘)。
  //
  // 初版は「Phase 4b の Step 5 までは 0 件でよい」として OK にしていた。
  // **実装が入った今、それは「消費処理を丸ごと削除しても PASS する」検査**であり、
  // 守りたかったものを 1 つも守らない (検査が vacuous になっている典型)。
  //
  // **「まだ実装前だから緩くする」は、実装後に締め直さないと必ず残る。**
  console.error(`[check-consume-once] NG — ${CONSUME} の呼び出しが 1 箇所も無い

  在庫を減らす口が消えている。おやつを出しても所持数が減らない状態
  (IMP-TRT-001 Phase 4b の完了条件そのもの)。
  実装を消したなら、この検査も一緒に見直すこと。`);
  process.exit(1);
}

// ---------------------------------------------------------------------------
// 順序の検査: **状態へ入る処理が、在庫を減らすより先に来ていること**
//
// 2026-08-24 Codex 指摘の高 1 件がまさにこれだった — 初版は
// `Consume()` → `EnterState()` の順で、**消費のあとに `_CommitOut()` という
// 失敗しうる操作が残っていた**。落ちると「在庫は減ったのに枠は Out にならず
// 犬が来られない」= 設計が名指しで塞いだプレイヤーの損。
//
// **これは純関数の自己テストでは守れない** (遷移表は「減る辺が 1 本」しか言わず、
// 副作用をどの順で実行するかは呼び出し側の話)。だから静的検査で守る。
// ---------------------------------------------------------------------------
const ORDER_DIR = path.join(ROOT, "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts"); // 裏山改変: 取り込み先
/** partial 分割 (IMP-INF-019) に備え、KD_TreatPouch.cs と KD_TreatPouch.<部>.cs / KD_TreatPouch_<部>.cs を全部見る。
 * 遷移の適用ブロック (EnterState(next) と Consume()) は**同じファイル**に居なければならない —
 * 別ファイルへ割れると順序の検査が成立しない。 */
const walkCs = (dir, out = []) => { for (const e of fs.readdirSync(dir, { withFileTypes: true })) { const p = path.join(dir, e.name); if (e.isDirectory()) walkCs(p, out); else if (e.name.endsWith('.cs')) out.push(p); } return out; };
// サブフォルダへ移しても見失わない (Codex 指摘 2026-09-13: Scripts/Menu/ へ動かすと空振りしていた)
const orderFiles = fs.existsSync(ORDER_DIR)
  ? walkCs(ORDER_DIR).filter(f => /^KD_TreatPouch([._]\w+)?\.cs$/.test(path.basename(f)))
  : [];
if (orderFiles.length) {
  const hits = orderFiles.map(f => {
    const body = stripCommentsAndStrings(fs.readFileSync(f, "utf8"));
    return { f: path.relative(ROOT, f).split(path.sep).join("/"),
      enter: body.search(/EnterState\s*\(\s*next\s*\)/), cons: body.search(/(?<![A-Za-z0-9_])Consume\s*\(\s*\)/) };
  });
  const withEnter = hits.filter(h => h.enter >= 0), withCons = hits.filter(h => h.cons >= 0);
  if (withEnter.length !== 1 || withCons.length !== 1) {
    console.error(`[check-consume-once] NG — EnterState(next) が ${withEnter.length} ファイル / Consume() が ${withCons.length} ファイルに見つかった (期待: それぞれ 1)
  順序の検査が空振りしている (名前が変わったか、partial 分割で遷移の適用ブロックが割れた)。`);
    process.exit(1);
  }
  if (withEnter[0].f !== withCons[0].f) {
    console.error(`[check-consume-once] NG — EnterState(next) (${withEnter[0].f}) と Consume() (${withCons[0].f}) が別ファイルにある
  遷移の適用ブロックは 1 ファイルに置く (順序を検査できる単位を保つ)。`);
    process.exit(1);
  }
  const ORDER_FILE = withEnter[0].f;
  if (withCons[0].cons < withEnter[0].enter) {
    console.error(`[check-consume-once] NG — ${ORDER_FILE} で Consume() が EnterState(next) より先にある

  **在庫が減るのは、失敗しうる操作がすべて終わった後。**
  消費のあとに枠を Out にする処理 (_CommitOut) を残すと、そこで落ちたときに
  「在庫は減ったのに犬が来られない」= プレイヤーの損になる。
  (shop-foundation §2「緩い側の向きは局面ごとに違う」— 消費では
   「出たけど減っていない」が緩い側で、「減ったのに出ていない」が損)`);
    process.exit(1);
  }
}

console.log(`[check-consume-once] OK — ${CONSUME} の呼び出しはちょうど 1 箇所 (${callSites[0]}) ` +
  '/ 状態へ入る処理が消費より先');
