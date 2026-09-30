// 犬を洗う機能の仮の効果音 (IMP-DOG-025 用) を Node の標準機能だけで合成する。
// 実行: node scripts/kh-gen-wash-se.js  → Assets/KominkaHillside/Audio/Wash/*.wav
// 乱数は固定シード。mono 44.1 kHz 16 bit。仕上げは IMP-DOG-028 で作り直す。
const fs = require('fs');
const path = require('path');
const SR = 44100;
const OUT = path.join(__dirname, '..', 'Assets', 'KominkaHillside', 'Audio', 'Wash');

function rngMaker(seed) {
  let s = seed >>> 0;
  return () => {
    s = (s + 0x6D2B79F5) >>> 0;
    let t = s;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
const gauss = (r) => Math.sqrt(-2 * Math.log(r() + 1e-12)) * Math.cos(2 * Math.PI * r());

function noise(n, r) { const a = new Float32Array(n); for (let i = 0; i < n; i++) a[i] = gauss(r); return a; }

// 循環バッファとして 2 周流し、2 周目を返す (ループの継ぎ目でフィルタの状態を連続させる)
function circ(a, fn) {
  const n = a.length, b = new Float32Array(n * 2);
  b.set(a, 0); b.set(a, n);
  const o = fn(b);
  return o.slice(n);
}
function lp1(a, fc) {
  const k = 1 - Math.exp(-2 * Math.PI * fc / SR);
  return circ(a, (b) => { let y = 0; const o = new Float32Array(b.length); for (let i = 0; i < b.length; i++) { y += k * (b[i] - y); o[i] = y; } return o; });
}
function hp1(a, fc) { const l = lp1(a, fc); const o = new Float32Array(a.length); for (let i = 0; i < a.length; i++) o[i] = a[i] - l[i]; return o; }
function lp2(a, fc) { return lp1(lp1(a, fc), fc); }
function bp(a, lo, hi) { return hp1(lp2(a, hi), lo); }

// 周期的な揺らぎ (ループ長に整数周期を収める)
function lfo(n, cycles, phase) { const a = new Float32Array(n); for (let i = 0; i < n; i++) a[i] = Math.sin(2 * Math.PI * (cycles * i / n + phase)); return a; }

// 無音でなくなる端の段差を消す等パワー交差 (末尾 C 標本を先頭へ混ぜる)。buf は n + C 標本
function loopify(buf, n, C) {
  const o = new Float32Array(n);
  for (let i = 0; i < n; i++) {
    if (i < C) { const t = (i + 0.5) / C * Math.PI / 2; o[i] = buf[i] * Math.sin(t) + buf[n + i] * Math.cos(t); }
    else o[i] = buf[i];
  }
  return o;
}
// 粒を循環バッファへ足す (端で切れず周期的)
function addGrain(dst, pos, g) { const n = dst.length; for (let i = 0; i < g.length; i++) dst[(pos + i) % n] += g[i]; }
function pingGrain(r, freq, ms, amp) { // 減衰する正弦
  const len = Math.floor(SR * ms / 1000), g = new Float32Array(len);
  const f0 = freq, ph = r() * 6.28;
  for (let i = 0; i < len; i++) { const t = i / SR; g[i] = amp * Math.sin(ph + 2 * Math.PI * f0 * (1 + 0.3 * (i / len)) * t) * Math.exp(-5 * i / len); }
  return g;
}
function burstGrain(r, ms, amp, fc) { // 短い帯域ノイズ
  const len = Math.floor(SR * ms / 1000), g = new Float32Array(len);
  let y = 0; const k = 1 - Math.exp(-2 * Math.PI * fc / SR);
  for (let i = 0; i < len; i++) { y += k * (gauss(r) - y); g[i] = amp * y * Math.exp(-4 * i / len); }
  return g;
}

function stats(a) {
  let pk = 0, ss = 0; for (const v of a) { pk = Math.max(pk, Math.abs(v)); ss += v * v; }
  return { peak: pk, rms: Math.sqrt(ss / a.length) };
}
const db = (x) => 20 * Math.log10(x + 1e-12);
// RMS を目標へ、ピークが -3 dBFS を超えるなら +ピークを優先して下げる。最後に 7 kHz のなだらかな高域抑制済みを前提とする
function normalize(a, targetRmsDb) {
  let s = stats(a); let g = Math.pow(10, targetRmsDb / 20) / s.rms;
  if (s.peak * g > Math.pow(10, -3.2 / 20)) g = Math.pow(10, -3.2 / 20) / s.peak;
  const o = new Float32Array(a.length); for (let i = 0; i < a.length; i++) o[i] = a[i] * g; return o;
}
function fade(a, ms) { const n = Math.floor(SR * ms / 1000); for (let i = 0; i < n; i++) { const g = i / n; a[i] *= g; a[a.length - 1 - i] *= g; } }

function writeWav(name, a) {
  const buf = Buffer.alloc(44 + a.length * 2);
  buf.write('RIFF', 0); buf.writeUInt32LE(36 + a.length * 2, 4); buf.write('WAVEfmt ', 8);
  buf.writeUInt32LE(16, 16); buf.writeUInt16LE(1, 20); buf.writeUInt16LE(1, 22);
  buf.writeUInt32LE(SR, 24); buf.writeUInt32LE(SR * 2, 28); buf.writeUInt16LE(2, 32); buf.writeUInt16LE(16, 34);
  buf.write('data', 36); buf.writeUInt32LE(a.length * 2, 40);
  for (let i = 0; i < a.length; i++) buf.writeInt16LE(Math.max(-32768, Math.min(32767, Math.round(a[i] * 32767))), 44 + i * 2);
  fs.mkdirSync(OUT, { recursive: true });
  fs.writeFileSync(path.join(OUT, name), buf);
}
function mix(...parts) { const n = parts[0].length, o = new Float32Array(n); for (const p of parts) for (let i = 0; i < n; i++) o[i] += p[i]; return o; }
function mul(a, env, base, depth) { const o = new Float32Array(a.length); for (let i = 0; i < a.length; i++) o[i] = a[i] * (base + depth * env[i]); return o; }

// ---- ループ音 ----
function loopSound(seconds, seed, build, rmsDb) {
  const n = Math.floor(SR * seconds), C = Math.floor(SR * 0.2), r = rngMaker(seed);
  const a = build(n, C, r);
  const o = normalize(a, rmsDb);
  return o;
}
function noiseLoop(n, C, r, fn) { // n + C 標本を作って交差し、n 標本へ
  const raw = noise(n + C, r);
  return loopify(fn(raw), n, C);
}

const sounds = {};

// 噴射: 高めの帯域 (1.5-6.5 kHz) のシャー、わずかな揺らぎ
sounds['KH_Wash_Spray_Loop.wav'] = loopSound(4, 101, (n, C, r) => {
  const nz = noiseLoop(n, C, r, (raw) => bp(raw, 1500, 6500));
  const l1 = lfo(n, 5, 0.1), l2 = lfo(n, 13, 0.6);
  const body = mul(nz, mix(l1.map(v => v * 0.6), l2.map(v => v * 0.4)), 1, 0.12);
  const low = noiseLoop(n, C, r, (raw) => bp(raw, 400, 1500)).map(v => v * 0.25);
  return lp2(mix(body, low), 7000);
}, -20);

// 毛への接触: 低めの帯域のザー + ばらついた水滴の粒 (パタパタ)
sounds['KH_Wash_ContactFur_Loop.wav'] = loopSound(4, 202, (n, C, r) => {
  const bed = noiseLoop(n, C, r, (raw) => bp(raw, 250, 2500));
  const env = lfo(n, 7, 0.3);
  let a = mul(bed, env, 1, 0.25).map(v => v * 0.9);
  const g = new Float32Array(n);
  const count = 260;
  for (let i = 0; i < count; i++) {
    const pos = Math.floor(r() * n), amp = 0.5 + 1.8 * r() * r();
    addGrain(g, pos, r() < 0.55 ? burstGrain(r, 4 + 10 * r(), amp * 1.3, 700 + 2500 * r()) : pingGrain(r, 700 + 1800 * r(), 5 + 8 * r(), amp * 0.5));
  }
  return lp2(mix(a, g), 6500);
}, -20);

// 泡: 細かいはじける粒を多く、柔らかい (シュワー・プシュー)
sounds['KH_Wash_ContactFoam_Loop.wav'] = loopSound(4, 303, (n, C, r) => {
  const bed = noiseLoop(n, C, r, (raw) => bp(raw, 900, 4200));
  const env = lfo(n, 3, 0.2);
  const a = mul(bed, env, 1, 0.3).map(v => v * 0.55);
  const g = new Float32Array(n);
  for (let i = 0; i < 900; i++) {
    const pos = Math.floor(r() * n);
    addGrain(g, pos, pingGrain(r, 1800 + 3200 * r(), 1.5 + 3 * r(), 0.15 + 0.9 * r() * r()));
  }
  return lp2(mix(a, g), 6500);
}, -20);

// 泡立て: 約 3 Hz の強弱の柔らかいノイズ + 泡の粒
sounds['KH_Wash_Scrub_Loop.wav'] = loopSound(3, 404, (n, C, r) => {
  const bed = noiseLoop(n, C, r, (raw) => bp(raw, 500, 3200));
  const env = lfo(n, 9, 0.0); // 3 秒で 9 周期 = 3 Hz
  const sh = env.map(v => Math.pow(0.5 + 0.5 * v, 1.5));
  const a = mul(bed, sh, 0.25, 1.0);
  const g = new Float32Array(n);
  for (let i = 0; i < 520; i++) {
    const pos = Math.floor(r() * n), e = sh[pos];
    addGrain(g, pos, pingGrain(r, 1500 + 3000 * r(), 1.5 + 3 * r(), (0.1 + 0.9 * r() * r()) * (0.3 + e)));
  }
  return lp2(mix(a, g.map(v => v * 0.9)), 6500);
}, -20);

// タオル: 低めの布の摩擦。ゆっくりした「サッ サッ」 (約 2.7 Hz)
sounds['KH_Wash_Towel_Loop.wav'] = loopSound(3, 505, (n, C, r) => {
  const bed = noiseLoop(n, C, r, (raw) => bp(raw, 120, 1800));
  const env = lfo(n, 8, 0.15);
  const sh = env.map(v => Math.pow(0.5 + 0.5 * v, 2));
  const air = noiseLoop(n, C, r, (raw) => bp(raw, 1800, 4500));
  return lp2(mix(mul(bed, sh, 0.15, 1.0), mul(air, sh, 0.0, 0.25)), 5500);
}, -20);

// ブルブル: 約 12 Hz の強弱 + 水滴の粒。単発 1.2 秒
(function () {
  const n = Math.floor(SR * 1.2), r = rngMaker(606);
  const bed = bp(noise(n, r), 300, 3500);
  const env = new Float32Array(n), win = new Float32Array(n);
  for (let i = 0; i < n; i++) {
    const t = i / n;
    win[i] = Math.pow(Math.sin(Math.PI * Math.min(1, t * 1.05)), 0.7) * Math.exp(-1.8 * t);
    env[i] = 0.45 + 0.55 * Math.pow(0.5 + 0.5 * Math.sin(2 * Math.PI * 12 * i / SR), 1.5);
  }
  const g = new Float32Array(n * 2);
  for (let i = 0; i < 320; i++) {
    const pos = Math.floor(Math.pow(r(), 1.4) * n * 0.95), amp = (0.3 + 1.4 * r() * r()) * (win[Math.min(n - 1, pos)] + 0.1);
    const gr = r() < 0.5 ? pingGrain(r, 900 + 2600 * r(), 4 + 8 * r(), amp * 0.7) : burstGrain(r, 3 + 8 * r(), amp * 1.3, 1000 + 2500 * r());
    for (let j = 0; j < gr.length; j++) g[pos + j] += gr[j];
  }
  const a = new Float32Array(n);
  for (let i = 0; i < n; i++) a[i] = bed[i] * env[i] * win[i] + g[i] * 0.6;
  const o = normalize(lp2(a, 6500), -20);
  fade(o, 8);
  sounds['KH_Wash_Shake.wav'] = o;
})();

// ---- 出力と計測 ----
const rows = [];
for (const [name, a] of Object.entries(sounds)) {
  writeWav(name, a);
}
// 書いたファイルを読み直して計測する
for (const name of Object.keys(sounds)) {
  const b = fs.readFileSync(path.join(OUT, name));
  const n = (b.length - 44) / 2, a = new Float32Array(n);
  for (let i = 0; i < n; i++) a[i] = b.readInt16LE(44 + i * 2) / 32767;
  const s = stats(a);
  // 8 kHz 超の割合 (1 次 HP の 8 kHz 通過分の RMS を全体 RMS で割る)
  const hi = stats(hp1(hp1(a, 8000), 8000)).rms / s.rms;
  rows.push({ name, bytes: b.length, sec: (n / SR).toFixed(2), peak: db(s.peak).toFixed(1), rms: db(s.rms).toFixed(1), seam: Math.abs(a[0] - a[n - 1]).toFixed(4), maxStep: (() => { let m = 0; for (let i = 1; i < n; i++) m = Math.max(m, Math.abs(a[i] - a[i - 1])); return m.toFixed(4); })(), hi8k: db(hi).toFixed(1) });
}
console.table(rows);
console.log('total bytes', rows.reduce((x, r) => x + r.bytes, 0));
