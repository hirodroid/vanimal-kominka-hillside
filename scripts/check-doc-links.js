#!/usr/bin/env node
/*
 * ドキュメント内のリンク切れを検出する。
 *
 *   node scripts/check-doc-links.js                          リンク切れがあれば exit 1
 *   node scripts/check-doc-links.js --orphans                どこからもリンクされていない .md も出す
 *   node scripts/check-doc-links.js --write-orphans-baseline 現在の orphan 一覧を config/doc-orphans.baseline.txt へ
 *
 * 見るもの:
 *   1. 相対リンクの参照先ファイルが存在するか (.md 以外・画像も含む)
 *   2. 見出しアンカー (#...) が実在するか — 同一ファイル / 他ファイルの両方
 *   3. (--orphans) git 管理下の docs/ でどこからもリンクされていない .md。
 *      config/doc-orphans.baseline.txt があれば **baseline に無い新規の orphan だけ** fail にする
 *      (archive の文書は summary からしか被リンクされないのが正常なので、全 orphan を fail にすると運用が壊れる)
 *   4. git 管理下の文書から、このリポジトリ内で gitignore された文書 (docs/_sessions 等) へのリンクを WARN
 *      (クローン先では dangling。orphan 判定にも untracked からの被リンクは使わない) — IMP-INF-019 K4
 *   5. 別リポジトリへの参照 (絶対パス、または `../` でリポジトリ外へ出る相対パス) は
 *      参照先が実在すれば OK (警告しない)、実在しなければ BROKEN。4 の WARN の対象にしない
 *
 * Issue は issues/ 直下の 1 ディレクトリに置き、状態が変わっても移動しない (2026-09-25)。
 * 状態は frontmatter の status、一覧は `node scripts/check-issues.js --write` で生成する。
 * 文書を移動・改名したときは必ずこれを通すこと。
 */

const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const SCAN_DIRS = ['docs'];
const EXTRA_FILES = ['CLAUDE.md'];
const WRITE_ORPHANS = process.argv.includes('--write-orphans-baseline');
const SHOW_ORPHANS = process.argv.includes('--orphans') || WRITE_ORPHANS;
const ORPHAN_BASELINE = path.join(ROOT, 'config/doc-orphans.baseline.txt');

const norm = p => p.split(path.sep).join('/');
const abs = rel => path.join(ROOT, rel);
/** ROOT 相対の正規化済みパスがリポジトリの外を指すか。 */
const isOutside = rel => rel === '..' || rel.startsWith('../') || path.isAbsolute(rel);

/** 絶対パス表記 (C:/x, C:\x, /c/x (Git Bash), file:///C:/x) を OS の絶対パスへ。該当しなければ null。 */
function toAbsolute(target) {
  let t = target;
  if (/^file:\/\//i.test(t)) t = t.replace(/^file:\/\/\/?/i, '');
  if (/^[A-Za-z]:[\\/]/.test(t)) return path.normalize(t);
  const m = t.match(/^\/([A-Za-z])\/(.*)$/);
  if (m && process.platform === 'win32') return path.normalize(`${m[1]}:/${m[2]}`);
  if (t.startsWith('/')) return path.normalize(t);
  return null;
}

/** リポジトリ外の参照先を確かめる。問題があれば理由、無ければ null。 */
function checkOutside(absPath, frag, label) {
  if (!fs.existsSync(absPath)) return `ファイルが無い (${label})`;
  if (frag && absPath.endsWith('.md') && !headingAnchors(fs.readFileSync(absPath, 'utf8')).has(frag)) {
    return `見出し「#${frag}」が無い (${label})`;
  }
  return null;
}

/** git 管理下のファイル集合。git が無い / リポジトリでない場合は null (= 全部 tracked 扱い)。 */
function trackedSet() {
  try {
    // リポジトリ全体 (docs 以外の画像・config・コードへのリンクも参照先になるため)
    const out = require('child_process').execFileSync('git', ['ls-files', '-z'],
      { cwd: ROOT, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
    return new Set(out.split('\0').filter(Boolean));
  } catch (_) { return null; }
}
const tracked = trackedSet();
const isTracked = rel => !tracked || tracked.has(rel) || (fs.existsSync(abs(rel)) && fs.statSync(abs(rel)).isDirectory());

function walk(dir, out = []) {
  if (!fs.existsSync(dir)) return out;
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, out);
    else if (e.name.endsWith('.md')) out.push(norm(path.relative(ROOT, p)));
  }
  return out;
}

/** GitHub の見出しアンカー生成に合わせる (小文字化 → 記号除去 → 空白をハイフンへ)。 */
function slug(heading) {
  return heading
    .replace(/`([^`]*)`/g, '$1')          // コードスパン
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1') // リンクはラベルだけ残す
    .replace(/[*_~]/g, '')                // 強調
    .trim()
    .toLowerCase()
    .replace(/[^\p{L}\p{N}\p{M}\- _]/gu, '') // 記号を落とす (・ § () / なども対象)
    .replace(/ /g, '-');
}

/** フェンス付きコードブロックを除去する (中の ](...) を拾わないため)。 */
const stripFences = text => text.replace(/^```[\s\S]*?^```/gm, '');

function headingAnchors(text) {
  const seen = new Map();
  const out = new Set();
  for (const m of stripFences(text).matchAll(/^#{1,6}\s+(.*)$/gm)) {
    const s = slug(m[1]);
    if (!s) continue;
    const n = seen.get(s) || 0;
    seen.set(s, n + 1);
    out.add(n === 0 ? s : `${s}-${n}`);
  }
  return out;
}

const files = [...SCAN_DIRS.flatMap(d => walk(abs(d))), ...EXTRA_FILES.filter(f => fs.existsSync(abs(f)))];
const anchorCache = new Map();
const getAnchors = rel => {
  if (!anchorCache.has(rel)) anchorCache.set(rel, headingAnchors(fs.readFileSync(abs(rel), 'utf8')));
  return anchorCache.get(rel);
};

const broken = [];
const linkedTo = new Set();
const untrackedLinks = [];

for (const file of files) {
  const dir = path.posix.dirname(file);
  const body = stripFences(fs.readFileSync(abs(file), 'utf8'));

  for (const m of body.matchAll(/\]\(\s*([^)]+?)\s*\)/g)) {
    let raw = m[1];
    raw = raw.replace(/\s+["'(].*$/, '');   // ](path "title")
    raw = raw.replace(/^<|>$/g, '');        // ](<path>)
    if (!raw || /^(https?:|mailto:|tel:|data:)/.test(raw)) continue;

    const hashAt = raw.indexOf('#');
    const target = hashAt === -1 ? raw : raw.slice(0, hashAt);
    const frag = hashAt === -1 ? '' : decodeURIComponent(raw.slice(hashAt + 1));
    const where = `${file}:${(body.slice(0, m.index).match(/\n/g) || []).length + 1}`;

    // 同一ファイル内アンカー
    if (!target) {
      if (frag && !getAnchors(file).has(frag)) broken.push([where, `#${frag}`, '見出しが無い']);
      continue;
    }

    const decoded = decodeURIComponent(target);
    const absTarget = toAbsolute(decoded);
    const resolved = absTarget
      ? norm(path.relative(ROOT, absTarget))
      : norm(path.posix.normalize(path.posix.join(dir, decoded)));
    if (isOutside(resolved)) {
      // 別リポジトリへの参照: 実在すれば OK、無ければ BROKEN。git 管理外 WARN は出さない
      const why = checkOutside(absTarget || path.resolve(ROOT, resolved), frag,
        absTarget ? '別リポジトリへの絶対パス' : 'リポジトリ外への相対パス');
      if (why) broken.push([where, raw, why]);
      continue;
    }
    if (!fs.existsSync(abs(resolved))) {
      broken.push([where, raw, 'ファイルが無い']);
      continue;
    }
    if (isTracked(file)) {
      linkedTo.add(resolved);
      if (!isTracked(resolved)) untrackedLinks.push([where, raw]);
    }

    if (frag && resolved.endsWith('.md') && !getAnchors(resolved).has(frag)) {
      broken.push([where, raw, `${resolved} に見出し「#${frag}」が無い`]);
    }
  }
}

for (const [where, link, why] of broken) console.log(`BROKEN ${where}\n        ${link}  — ${why}`);
for (const [where, link] of untrackedLinks) console.log(`WARN-UNTRACKED ${where}\n        ${link}  — git 管理外の文書へのリンク (クローン先では切れる)`);

let newOrphans = 0;
if (SHOW_ORPHANS) {
  // 入口 (索引・EXTRA_FILES・各 Skill の SKILL.md) は被リンクが無くても正常
  const isEntry = f => f === 'docs/AI_INDEX.md' || EXTRA_FILES.includes(f) || /^claude-skills\/[^/]+\/SKILL\.md$/.test(f);
  const orphans = files.filter(f => isTracked(f) && !linkedTo.has(f) && !isEntry(f));
  if (WRITE_ORPHANS) {
    fs.mkdirSync(path.dirname(ORPHAN_BASELINE), { recursive: true });
    fs.writeFileSync(ORPHAN_BASELINE, orphans.join('\n') + '\n');
    console.log(`orphan baseline 更新 — ${orphans.length} 件 → ${norm(path.relative(ROOT, ORPHAN_BASELINE))}`);
  } else {
    const base = fs.existsSync(ORPHAN_BASELINE)
      ? new Set(fs.readFileSync(ORPHAN_BASELINE, 'utf8').replace(/^\uFEFF/, '').split(/\r?\n/).filter(Boolean)) : null;
    const fresh = base ? orphans.filter(o => !base.has(o)) : orphans;
    const fixed = base ? [...base].filter(o => !orphans.includes(o)) : [];
    if (orphans.length) {
      console.log(`\nORPHAN (git 管理下でどこからもリンクされていない .md) — ${orphans.length} 件${base ? ` (うち新規 ${fresh.length})` : ''}`);
      for (const o of orphans) console.log(`        ${base && !base.has(o) ? 'NEW ' : '    '}${o}`);
    }
    if (fixed.length) console.log(`baseline から消えた (リンクされた) — ${fixed.length} 件。--write-orphans-baseline で更新してよい`);
    // baseline が無い = 全部が新規扱い (baseline を消して通す抜け道を塞ぐ。Codex 指摘 2026-09-13)
    newOrphans = fresh.length;
  }
}

console.log(`\n${files.length} files scanned — ${broken.length ? `${broken.length} broken link(s)` : 'all links OK'}` +
  (untrackedLinks.length ? ` / untracked へのリンク ${untrackedLinks.length} 件 (WARN)` : '') +
  (newOrphans ? ` / 新規 orphan ${newOrphans} 件 (NG)` : ''));
process.exit(broken.length || newOrphans ? 1 : 0);
