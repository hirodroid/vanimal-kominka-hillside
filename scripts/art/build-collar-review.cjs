// Rebuild the offline collar review sheet (artifacts/collar-review/2026-10-10/index.html).
// Images are referenced relatively (before: img/, after: after/img/; not embedded).
// Numbers come from tools/metrics.json (before) and after/tools/metrics.json (after, final R9 = option 1), the dry-run
// section values from after/tools/fit-report.txt (R9), and the Fit tuning tables are read from KH_LeashProductionEditor.cs.
// Node standard modules only. Usage: node scripts/art/build-collar-review.cjs
'use strict';
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const out = path.join(root, 'artifacts/collar-review/2026-10-10');
const imgDir = path.join(out, 'img');
const afterImgDir = path.join(out, 'after/img');
const metrics = JSON.parse(fs.readFileSync(path.join(out, 'tools/metrics.json'), 'utf8'));
const afterMetrics = JSON.parse(fs.readFileSync(path.join(out, 'after/tools/metrics.json'), 'utf8'));

const poseLabels = { rest: '静止 (rest)', aruku: '歩き (aruku)', Sit: '座り (Sit)', huse: '伏せ (huse)' };
const viewLabels = {
  left: '左側面 (left)', front: '正面 (front)', top: '真上 (top)', chin: 'あご下 (chin)',
  backtop: '背中側の上 (backtop)', closeleft: '左の近接 (closeleft)', closefront: '正面の近接 (closefront)', right: '右側面 (right)',
};
// rest: 8 views (left first = large image). Other poses: 3 views.
const poseViews = {
  rest: ['left', 'front', 'top', 'chin', 'backtop', 'closeleft', 'closefront', 'right'],
  aruku: ['left', 'front', 'top'], Sit: ['left', 'front', 'top'], huse: ['left', 'front', 'top'],
};

// Fit settings BEFORE the BUG-DOG-011 fix: KH_LeashProductionEditor.Fit put every collar at Lerp(Bone.003, Bone.004, 0.45);
// the only per-breed corrections were Lab (dog space: forward 40mm, down 8mm) and Shiba (down 20mm). localScale = 1 / neck.lossyScale.
// The lerp in metrics.json is the measured InverseLerpPoint, not the Fit setting.
// Band ellipse radii (rx, ry) are read from scripts/art/leash-assets.cjs so the sheet follows the mesh source.
const breedOrder = ['Corgi', 'Pome', 'Lab', 'Dachs', 'Shiba', 'Samoyed'];
const fail = m => { console.error('NG ' + m); process.exit(1); };
const leashSrc = fs.readFileSync(path.join(__dirname, 'leash-assets.cjs'), 'utf8');
const radiiMatch = leashSrc.match(/const radii=(\[[^;]*\]);/);
if (!radiiMatch) fail('radii not found in scripts/art/leash-assets.cjs');
const radii = JSON.parse(radiiMatch[1].replace(/(^|[\[,])\./g, '$10.'));
if (radii.length !== breedOrder.length) fail('radii count');
const radiiText = key => { const [rx, ry] = radii[breedOrder.indexOf(key)]; return `帯の楕円半径 rx ${rx} / ry ${ry}`; };

// ---- Fit tables after the fix (read, never retyped) ----
const csPath = 'Assets/KominkaHillside/Scripts/Editor/KH_LeashProductionEditor.cs';
const cs = fs.readFileSync(path.join(root, csPath), 'utf8');
const num = s => { const v = Number(s.trim().replace(/f$/, '')); if (!Number.isFinite(v)) fail(`not a number in ${csPath}: ${s}`); return v; };
function floatTable(name) {
  const m = cs.match(new RegExp(`static readonly float\\[\\] ${name}=\\{([^}]*)\\};`));
  if (!m) fail(`${name} not found in ${csPath}`);
  const v = m[1].split(',').map(num); if (v.length !== 6) fail(`${name} count`); return v;
}
function vecTable(name, type) {
  const m = cs.match(new RegExp(`static readonly ${type}\\[\\] ${name}=\\{(.*?)\\};`));
  if (!m) fail(`${name} not found in ${csPath}`);
  const v = [...m[1].matchAll(new RegExp(`${type}\\.zero|new ${type}\\(([^)]*)\\)`, 'g'))]
    .map(x => x[1] === undefined ? (type === 'Vector2' ? [0, 0] : [0, 0, 0]) : x[1].split(',').map(num));
  if (v.length !== 6) fail(`${name} count`); return v;
}
function boolTable(name) {
  const m = cs.match(new RegExp(`static readonly bool\\[\\] ${name}=\\{([^}]*)\\};`));
  if (!m) fail(`${name} not found in ${csPath}`);
  const v = m[1].split(',').map(x => { x = x.trim(); if (x !== 'true' && x !== 'false') fail(`not a bool in ${csPath}: ${x}`); return x === 'true'; });
  if (v.length !== 6) fail(`${name} count`); return v;
}
function constant(name) { const m = cs.match(new RegExp(`\\b${name}=([-.\\d]+f?)`)); if (!m) fail(`${name} not found in ${csPath}`); return num(m[1]); }
const tables = {
  BandRadii: vecTable('BandRadii', 'Vector2'), FurMargin: floatTable('FurMargin'), TiltBlend: floatTable('TiltBlend'),
  AlongOffset: floatTable('AlongOffset'), ExtraOffset: vecTable('ExtraOffset', 'Vector3'), MaxScaleByBreed: floatTable('MaxScaleByBreed'),
  // 2026-10-10 user decision (option 1): long-coated dogs hide the band inside the body, only the lifted ring shows.
  BuriedInFur: boolTable('BuriedInFur'), SinkDepth: floatTable('SinkDepth'), SideInset: floatTable('SideInset'), RingLift: floatTable('RingLift'),
};
const consts = {
  SliceHalfThickness: constant('SliceHalfThickness'), SearchRadiusFactor: constant('SearchRadiusFactor'),
  MinScale: constant('MinScale'), MaxScale: constant('MaxScale'), MinSamples: constant('MinSamples'),
};
tables.BandRadii.forEach((r, i) => { if (r[0] !== radii[i][0] || r[1] !== radii[i][1]) fail(`BandRadii[${breedOrder[i]}] ${r} differs from leash-assets.cjs radii ${radii[i]}`); });

// ---- Dry-run section values (R9, re-run after the apply; delta = mm from the applied placement) ----
const fitReport = fs.readFileSync(path.join(out, 'after/tools/fit-report.txt'), 'utf8').split(/\r?\n/).filter(l => /^\w+ samples=/.test(l)).map(l => {
  const pair = k => { const m = l.match(new RegExp(`${k}=\\(([-\\d.]+),([-\\d.]+)\\)`)); if (!m) fail(`fit-report ${k} missing: ${l}`); return [Number(m[1]), Number(m[2])]; };
  return {
    key: l.split(' ')[0], samples: Number(l.match(/samples=(\d+)/)[1]), centerShift: pair('centerShift'), halfExtents: pair('halfExtents'),
    targetRadii: pair('targetRadii'), kXY: pair('kXY'), delta: Number((l.match(/delta=([-\d.]+)/) || fail(`fit-report delta missing: ${l}`))[1]), note: l.replace(/^.*note=/, '').replace(/;\s*$/, '').trim(),
  };
});
if (fitReport.map(r => r.key).join() !== breedOrder.join()) fail('fit-report.txt rows do not match the 6 breeds');

// Observation text (before): the coordinator's analysis memo (2026-10-10) "犬ごとの見た目", corrected by the independent review.
// afterObservation: image check of the final (R9) shots, 2026-10-10 (Pome / Samoyed: the coordinator's record; the
// four other dogs: same as R4 plus the reduced margin, checked on after/img/).
const dogs = [
  { key: 'Corgi', name: 'コーギー', placement: `Fit: 首骨→頭骨の 0.45、犬別補正なし。${radiiText('Corgi')}`,
    observation: '左側面では上面は毛に乗っているが、下前方が胸毛の中へ消える。正面では帯が頭に隠れ、金具 (D リング) だけが見える。真上では帯の後ろ半分が浮いて首との間に隙間 (影) がある。伏せでも同じ。',
    afterObservation: '帯が首の付け根に収まり、正面・あご下からも帯が見える。毛の余白を減らしたので、帯の縁が毛に少し沈む (毛に半分沈んだ首輪)。' },
  { key: 'Pome', name: 'ポメラニアン', placement: `Fit: 0.45、犬別補正なし。${radiiText('Pome')}`,
    observation: '真上では帯が首の毛の上に乗って見えるが、左側面では帯の下端から白い毛が束で突き抜ける。正面では帯が頭に隠れ、金具だけが見える。',
    afterObservation: '帯は毛の中 (体の内側) に隠れて見えない。背中側で金具 (D リング) だけが毛の上に出る (左側面ではっきり見え、正面では頭の後ろに縁が少し見える)。' },
  { key: 'Lab', name: 'ラブラドール', placement: `計測上の位置 0.576 (Fit は 0.45 + 犬座標で前 40mm・下 8mm の補正)。localEuler (271.27, 180, 180) は (268.73, 0, 0) と同じ回転で、他犬種と同系統。${radiiText('Lab')}`,
    observation: '短毛なので最も素直。帯は顎下にきちんと回る。正面で帯が胸の前に見える (見た目の手本)。ただし数値では上 +15.8mm・埋まり 16% で、座りでは下側が −12.3mm と少し沈む。',
    afterObservation: '帯が首の付け根に収まり、正面・あご下からも帯が見える。座りでは下側が沈む (数値は右の表)。' },
  { key: 'Dachs', name: 'ダックスフンド', placement: `Fit: 0.45、犬別補正なし。localEuler X 278.6 (他は 268.7〜272.2)。${radiiText('Dachs')}`,
    observation: '耳のすぐ後ろ、首の付け根より高い位置に輪が掛かる。背中側の上から見ると、帯が首より左右にはみ出して見える。下側は胸毛に消える。',
    afterObservation: '帯が首の付け根に収まり、正面・あご下からも帯が見える。' },
  { key: 'Shiba', name: '柴', placement: `計測上の位置 0.345 (Fit は 0.45 + 下 20mm の補正)。${radiiText('Shiba')} (ポメラニアンと同じ大きさ)`,
    observation: '帯が体格に対して小さく、左側面から見ると下半分が首の中へ沈んで半円に見える (左の近接で下端が皮膚に食い込む)。正面では帯が全く見えない。',
    afterObservation: '帯が首の付け根に収まり、正面・あご下からも帯が見える。断面の探索半径を広げて胸側まで拾えるようにしたことで、静止の下側が大きく改善した (数値は右の表)。毛の余白を減らしたので、帯の縁が毛に少し沈む。' },
  { key: 'Samoyed', name: 'サモエド', placement: `Fit: 0.45、犬別補正なし。${radiiText('Samoyed')}`,
    observation: '上は毛の表面に乗るが、下前方は毛に沈み、毛が帯を突き抜けて「帯が途中で切れた」ように読める。真上では帯が毛に埋もれて細く見える。',
    afterObservation: '帯は毛の中 (体の内側) に隠れて見えない。金具は背中側の上から見ると毛の上にわずかに出るが、毛が厚いので左側面・正面ではほとんど見えない (ぎりぎり)。' },
];

const sectorMean = (entry, name) => {
  const s = entry.sectors.find(x => x.sector === name);
  if (!s) throw new Error(`sector ${name} missing for ${entry.dog} ${entry.pose}`);
  return s.meanMm;
};
const vec = s => s.replace(/[()]/g, '').split(',').map(Number);
const mOf = entry => ({
  top: sectorMean(entry, 'top'), bottom: sectorMean(entry, 'bottom'),
  left: sectorMean(entry, 'left'), right: sectorMean(entry, 'right'),
  buriedPct: Math.round(entry.buried / entry.bandVerts * 100),
  floatingPct: Math.round(entry.floating / entry.bandVerts * 100),
  buriedRatio: entry.buried / entry.bandVerts,
  offsetEst: Math.round((sectorMean(entry, 'top') - sectorMean(entry, 'bottom')) / 2), // whole mm, estimate only
  bandVerts: entry.bandVerts, lerp: entry.neckToHeadLerp,
  euler: entry.collarLocalEuler, scale: entry.collarLocalScale,
});

const problems = [];
let imageCount = 0, afterImageCount = 0;
const checkImg = (dir, rel) => { if (!fs.existsSync(path.join(dir, path.basename(rel)))) problems.push(`image missing: ${rel}`); };
const data = dogs.map(d => ({
  key: d.key, name: d.name, placement: d.placement, observation: d.observation,
  poses: Object.keys(poseViews).map(pose => {
    const entry = metrics.find(e => e.dog === `KD_Dog_${d.key}` && e.pose === pose);
    if (!entry) { problems.push(`metrics missing: ${d.key} ${pose}`); return null; }
    const views = poseViews[pose].map(view => {
      const file = `img/${d.key}-${pose}-${view}.jpg`;
      checkImg(imgDir, file); imageCount++;
      return { view, label: viewLabels[view], file };
    });
    return { pose, label: poseLabels[pose], clip: entry.clip, views, m: mOf(entry) };
  }),
}));

// Coat groups and the top guideline come from BUG-DOG-011 完了条件 (KH_DogWashSetupEditor coat table). Medium coat: undecided.
const coat = { Lab: ['短毛', 10], Dachs: ['短毛', 10], Pome: ['長毛', 25], Samoyed: ['長毛', 25], Corgi: ['中毛', null], Shiba: ['中毛', null] };
const afterDogs = dogs.map((d, i) => {
  const poses = Object.keys(poseViews).map(pose => {
    const b = metrics.find(e => e.dog === `KD_Dog_${d.key}` && e.pose === pose);
    const a = afterMetrics.find(e => e.dog === `KD_Dog_${d.key}` && e.pose === pose);
    if (!b || !a) { problems.push(`metrics missing (${!a ? 'after' : 'before'}): ${d.key} ${pose}`); return null; }
    const views = poseViews[pose].map(view => {
      const name = `${d.key}-${pose}-${view}.jpg`;
      checkImg(afterImgDir, `after/img/${name}`); afterImageCount++;
      return { view, label: viewLabels[view], before: `img/${name}`, after: `after/img/${name}` };
    });
    return { pose, label: poseLabels[pose], clip: a.clip, views, b: mOf(b), a: mOf(a) };
  });
  const rest = poses.find(p => p && p.pose === 'rest');
  const ls = rest ? vec(rest.a.scale) : [1, 1, 1];
  const [coatName, topTarget] = coat[d.key];
  const buried = tables.BuriedInFur[i];
  return {
    key: d.key, name: d.name, afterObservation: d.afterObservation, poses,
    fit: {
      rx: tables.BandRadii[i][0], ry: tables.BandRadii[i][1], furMarginMm: Math.round(tables.FurMargin[i] * 1000),
      tilt: tables.TiltBlend[i], maxScale: Math.min(consts.MaxScale, tables.MaxScaleByBreed[i]),
      along: tables.AlongOffset[i], extra: tables.ExtraOffset[i],
      // X/Y band scale = localScale.xy / localScale.z (Fit writes scaleXY / neck.lossyScale and 1 / neck.lossyScale.z).
      kX: +(ls[0] / ls[2]).toFixed(3), kY: +(ls[1] / ls[2]).toFixed(3),
      buried, sinkMm: Math.round(tables.SinkDepth[i] * 1000), insetMm: Math.round(tables.SideInset[i] * 1000), ringLiftMm: Math.round(tables.RingLift[i] * 1000),
    },
    coat: coatName, topTarget, buried,
    criteria: poses.filter(Boolean).map(p => ({
      pose: p.pose, label: p.label, top: p.a.top, bottom: p.a.bottom, buriedRatio: p.a.buriedRatio,
      bottomOk: buried ? null : p.a.bottom >= -5, buriedOk: buried ? null : p.a.buriedRatio <= 0.05,
      topOk: buried || topTarget == null ? null : p.a.top <= topTarget,
    })),
  };
});

const onDisk = fs.readdirSync(imgDir).filter(f => f.endsWith('.jpg')).length;
if (onDisk !== imageCount) problems.push(`img/ has ${onDisk} jpg but the sheet references ${imageCount}`);
const afterOnDisk = fs.readdirSync(afterImgDir).filter(f => f.endsWith('.jpg')).length;
if (afterOnDisk !== afterImageCount) problems.push(`after/img/ has ${afterOnDisk} jpg but the sheet references ${afterImageCount}`);
if (problems.length) { for (const p of problems) console.error('NG ' + p); process.exit(1); }

const payload = {
  generatedFrom: 'tools/metrics.json', dogs: data,
  after: {
    generatedFrom: 'after/tools/metrics.json', fitSource: csPath, fitReportSource: 'after/tools/fit-report.txt',
    consts, fitReport, dogs: afterDogs,
  },
};
const template = fs.readFileSync(path.join(__dirname, 'collar-review.template.html'), 'utf8');
if (!template.includes('/*COLLAR_DATA*/')) fail('template has no /*COLLAR_DATA*/ marker');
// Escape "<" so the JSON can never close the <script> element.
const json = JSON.stringify(payload).replace(/</g, '\\u003c');
fs.writeFileSync(path.join(out, 'index.html'), template.replace('/*COLLAR_DATA*/', () => json));
console.log(`Created collar review: ${path.relative(root, out)}/index.html — ${data.length} dogs, ${imageCount + afterImageCount} image refs (before ${imageCount} + after ${afterImageCount}, all exist)`);
