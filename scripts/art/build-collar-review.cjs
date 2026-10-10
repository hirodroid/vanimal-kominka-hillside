// Rebuild the offline collar review sheet (artifacts/collar-review/2026-10-10/index.html).
// Images are referenced relatively from img/ (not embedded). Numbers come from tools/metrics.json.
// Node standard modules only. Usage: node scripts/art/build-collar-review.cjs
'use strict';
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const out = path.join(root, 'artifacts/collar-review/2026-10-10');
const imgDir = path.join(out, 'img');
const metrics = JSON.parse(fs.readFileSync(path.join(out, 'tools/metrics.json'), 'utf8'));

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

// Fit settings: KH_LeashProductionEditor.Fit puts every collar at Lerp(Bone.003, Bone.004, 0.45); the only per-breed
// corrections are Lab (dog space: forward 40mm, down 8mm) and Shiba (down 20mm). localScale = 1 / neck.lossyScale.
// The lerp in metrics.json is the measured InverseLerpPoint, not the Fit setting.
// Band ellipse radii (rx, ry) are read from scripts/art/leash-assets.cjs so the sheet follows the mesh source.
const breedOrder = ['Corgi', 'Pome', 'Lab', 'Dachs', 'Shiba', 'Samoyed'];
const leashSrc = fs.readFileSync(path.join(__dirname, 'leash-assets.cjs'), 'utf8');
const radiiMatch = leashSrc.match(/const radii=(\[[^;]*\]);/);
if (!radiiMatch) { console.error('NG radii not found in scripts/art/leash-assets.cjs'); process.exit(1); }
const radii = JSON.parse(radiiMatch[1].replace(/(^|[\[,])\./g, '$10.'));
if (radii.length !== breedOrder.length) { console.error('NG radii count'); process.exit(1); }
const radiiText = key => { const [rx, ry] = radii[breedOrder.indexOf(key)]; return `帯の楕円半径 rx ${rx} / ry ${ry}`; };

// Observation text: the coordinator's analysis memo (2026-10-10) "犬ごとの見た目", corrected by the independent review.
const dogs = [
  { key: 'Corgi', name: 'コーギー', placement: `Fit: 首骨→頭骨の 0.45、犬別補正なし。${radiiText('Corgi')}`,
    observation: '左側面では上面は毛に乗っているが、下前方が胸毛の中へ消える。正面では帯が頭に隠れ、金具 (D リング) だけが見える。真上では帯の後ろ半分が浮いて首との間に隙間 (影) がある。伏せでも同じ。' },
  { key: 'Pome', name: 'ポメラニアン', placement: `Fit: 0.45、犬別補正なし。${radiiText('Pome')}`,
    observation: '真上では帯が首の毛の上に乗って見えるが、左側面では帯の下端から白い毛が束で突き抜ける。正面では帯が頭に隠れ、金具だけが見える。' },
  { key: 'Lab', name: 'ラブラドール', placement: `計測上の位置 0.576 (Fit は 0.45 + 犬座標で前 40mm・下 8mm の補正)。localEuler (271.27, 180, 180) は (268.73, 0, 0) と同じ回転で、他犬種と同系統。${radiiText('Lab')}`,
    observation: '短毛なので最も素直。帯は顎下にきちんと回る。正面で帯が胸の前に見える (見た目の手本)。ただし数値では上 +15.8mm・埋まり 16% で、座りでは下側が −12.3mm と少し沈む。' },
  { key: 'Dachs', name: 'ダックスフンド', placement: `Fit: 0.45、犬別補正なし。localEuler X 278.6 (他は 268.7〜272.2)。${radiiText('Dachs')}`,
    observation: '耳のすぐ後ろ、首の付け根より高い位置に輪が掛かる。背中側の上から見ると、帯が首より左右にはみ出して見える。下側は胸毛に消える。' },
  { key: 'Shiba', name: '柴', placement: `計測上の位置 0.345 (Fit は 0.45 + 下 20mm の補正)。${radiiText('Shiba')} (ポメラニアンと同じ大きさ)`,
    observation: '帯が体格に対して小さく、左側面から見ると下半分が首の中へ沈んで半円に見える (左の近接で下端が皮膚に食い込む)。正面では帯が全く見えない。' },
  { key: 'Samoyed', name: 'サモエド', placement: `Fit: 0.45、犬別補正なし。${radiiText('Samoyed')}`,
    observation: '上は毛の表面に乗るが、下前方は毛に沈み、毛が帯を突き抜けて「帯が途中で切れた」ように読める。真上では帯が毛に埋もれて細く見える。' },
];

const sectorMean = (entry, name) => {
  const s = entry.sectors.find(x => x.sector === name);
  if (!s) throw new Error(`sector ${name} missing for ${entry.dog} ${entry.pose}`);
  return s.meanMm;
};

const problems = [];
let imageCount = 0;
const data = dogs.map(d => ({
  key: d.key, name: d.name, placement: d.placement, observation: d.observation,
  poses: Object.keys(poseViews).map(pose => {
    const entry = metrics.find(e => e.dog === `KD_Dog_${d.key}` && e.pose === pose);
    if (!entry) { problems.push(`metrics missing: ${d.key} ${pose}`); return null; }
    const views = poseViews[pose].map(view => {
      const file = `${d.key}-${pose}-${view}.jpg`;
      if (!fs.existsSync(path.join(imgDir, file))) problems.push(`image missing: img/${file}`);
      imageCount++;
      return { view, label: viewLabels[view], file: `img/${file}` };
    });
    return {
      pose, label: poseLabels[pose], clip: entry.clip, views,
      m: {
        top: sectorMean(entry, 'top'), bottom: sectorMean(entry, 'bottom'),
        left: sectorMean(entry, 'left'), right: sectorMean(entry, 'right'),
        buriedPct: Math.round(entry.buried / entry.bandVerts * 100),
        floatingPct: Math.round(entry.floating / entry.bandVerts * 100),
        offsetEst: Math.round((sectorMean(entry, 'top') - sectorMean(entry, 'bottom')) / 2), // whole mm, estimate only
        bandVerts: entry.bandVerts, lerp: entry.neckToHeadLerp,
        euler: entry.collarLocalEuler, scale: entry.collarLocalScale,
      },
    };
  }),
}));

const onDisk = fs.readdirSync(imgDir).filter(f => f.endsWith('.jpg')).length;
if (onDisk !== imageCount) problems.push(`img/ has ${onDisk} jpg but the sheet references ${imageCount}`);
if (problems.length) { for (const p of problems) console.error('NG ' + p); process.exit(1); }

const payload = { generatedFrom: 'tools/metrics.json', dogs: data };
const template = fs.readFileSync(path.join(__dirname, 'collar-review.template.html'), 'utf8');
if (!template.includes('/*COLLAR_DATA*/')) { console.error('NG template has no /*COLLAR_DATA*/ marker'); process.exit(1); }
// Escape "<" so the JSON can never close the <script> element.
const json = JSON.stringify(payload).replace(/</g, '\\u003c');
fs.writeFileSync(path.join(out, 'index.html'), template.replace('/*COLLAR_DATA*/', () => json));
console.log(`Created collar review: ${path.relative(root, out)}/index.html — ${data.length} dogs, ${imageCount} image refs (all exist)`);
