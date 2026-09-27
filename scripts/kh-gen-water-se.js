#!/usr/bin/env node
// 水音 SE の合成: node scripts/kh-gen-water-se.js
// プロジェクトにも購入アセットにも水音が無いので、Node の標準機能だけで WAV (mono 22,050 Hz 16 bit) を作る (npm 追加なし)。
// 外部の音源・サンプルは使わない (AI 制作の合成音。制作票: docs/plans/2026-09-27-water-se-production.md)。
// 乱数は固定のシードなので、何度流しても同じファイルになる。出力: Assets/KominkaHillside/Audio/Water/
//   KH_WaterStep.wav   (a) 小さい水音「ぱしゃ」 0.25 s — 足が水に入る (KD_DogWanderController.waterStepClip に配線)
//   KH_WaterSplash.wav (b) 大きい水音「ばしゃっ」0.5 s — 入水
//   KH_WaterPaddle.wav (c) 犬かき 1.2 s (0.4 s 周期 × 3、継ぎ目なしのループ)
'use strict';
const fs = require('fs');
const path = require('path');

const RATE = 22050;
const OUT = path.join(__dirname, '..', 'Assets', 'KominkaHillside', 'Audio', 'Water');

// ---- 決まった乱数 (mulberry32) ----
function rng(seed) { let a = seed >>> 0; return () => { a = (a + 0x6D2B79F5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }

// ---- 1 次のフィルタ ----
function lowpass(x, hz) { const a = Math.exp(-2 * Math.PI * hz / RATE); const y = new Float64Array(x.length); let s = 0; for (let i = 0; i < x.length; i++) { s = (1 - a) * x[i] + a * s; y[i] = s; } return y; }
function highpass(x, hz) { const lp = lowpass(x, hz); const y = new Float64Array(x.length); for (let i = 0; i < x.length; i++) y[i] = x[i] - lp[i]; return y; }
function band(x, lo, hi) { return lowpass(highpass(x, lo), hi); }

// 水しぶき 1 回: 帯域を絞ったノイズの短い塊 + 小さな水滴 (上がっていく短いサイン)
function splash(n, start, len, gain, lo, hi, drops, rand, decay) {
  const out = new Float64Array(n);
  const noise = new Float64Array(len);
  for (let i = 0; i < len; i++) noise[i] = rand() * 2 - 1;
  const shaped = band(band(noise, lo, hi), lo, hi);
  for (let i = 0; i < len; i++) {
    const t = i / RATE;
    const env = Math.min(1, t / 0.004) * Math.exp(-t * decay);
    out[(start + i) % n] += shaped[i] * env * gain;
  }
  for (let d = 0; d < drops; d++) {
    const at = start + Math.floor((0.01 + rand() * 0.6 * len / RATE) * RATE);
    const f0 = 700 + rand() * 1400, rise = 1.4 + rand() * 1.2, dl = Math.floor((0.02 + rand() * 0.04) * RATE);
    let ph = 0;
    for (let i = 0; i < dl; i++) {
      const t = i / RATE, f = f0 * (1 + (rise - 1) * (i / dl));
      ph += 2 * Math.PI * f / RATE;
      out[(at + i) % n] += Math.sin(ph) * Math.exp(-t * 60) * gain * (0.25 + rand() * 0.2);
    }
  }
  return out;
}
function mix(a, b) { for (let i = 0; i < a.length; i++) a[i] += b[i]; return a; }
function normalize(x, peak) { let m = 0; for (const v of x) m = Math.max(m, Math.abs(v)); const k = m > 0 ? peak / m : 0; for (let i = 0; i < x.length; i++) x[i] *= k; return x; }
function fadeEdges(x, inS, outS) { const a = Math.floor(inS * RATE), b = Math.floor(outS * RATE); for (let i = 0; i < a && i < x.length; i++) x[i] *= i / a; for (let i = 0; i < b && i < x.length; i++) x[x.length - 1 - i] *= i / b; return x; }

function wav(samples) {
  const n = samples.length, data = Buffer.alloc(n * 2);
  for (let i = 0; i < n; i++) data.writeInt16LE(Math.max(-32767, Math.min(32767, Math.round(samples[i] * 32767))), i * 2);
  const h = Buffer.alloc(44);
  h.write('RIFF', 0); h.writeUInt32LE(36 + data.length, 4); h.write('WAVE', 8); h.write('fmt ', 12);
  h.writeUInt32LE(16, 16); h.writeUInt16LE(1, 20); h.writeUInt16LE(1, 22); h.writeUInt32LE(RATE, 24); h.writeUInt32LE(RATE * 2, 28);
  h.writeUInt16LE(2, 32); h.writeUInt16LE(16, 34); h.write('data', 36); h.writeUInt32LE(data.length, 40);
  return Buffer.concat([h, data]);
}

// (a) 小さい水音 0.25 s
function step() {
  const n = Math.floor(0.25 * RATE), r = rng(20260927);
  const x = splash(n, 0, n, 1, 900, 4200, 3, r, 22);
  return fadeEdges(normalize(x, 0.55), 0.002, 0.03);
}
// (b) 大きい水音 0.5 s: 低い「どぼん」+ 広い帯域のしぶき + 泡
function big() {
  const n = Math.floor(0.5 * RATE), r = rng(20260928);
  const x = splash(n, 0, n, 1, 250, 3200, 7, r, 9);
  let ph = 0;
  for (let i = 0; i < Math.floor(0.12 * RATE); i++) { const t = i / RATE; ph += 2 * Math.PI * (160 - 500 * t) / RATE; x[i] += Math.sin(ph) * Math.exp(-t * 28) * 0.6; }
  mix(x, splash(n, Math.floor(0.06 * RATE), n - Math.floor(0.06 * RATE), 0.35, 1200, 5000, 5, r, 14));
  return fadeEdges(normalize(x, 0.8), 0.002, 0.06);
}
// (c) 犬かき 1.2 s: 0.4 s ごとの柔らかい水かき。尾は先頭へ回り込ませる (リングに足す) ので、つないでも継ぎ目が出ない
function paddle() {
  const n = Math.floor(1.2 * RATE), r = rng(20260929);
  const x = new Float64Array(n);
  for (let k = 0; k < 3; k++) {
    const start = Math.floor((k * 0.4 + 0.02 * (r() - 0.5)) * RATE + n) % n;
    mix(x, splash(n, start, Math.floor(0.36 * RATE), 0.7 + 0.2 * r(), 500, 2600, 2, r, 11));
  }
  return normalize(x, 0.45);
}

fs.mkdirSync(OUT, { recursive: true });
let total = 0;
for (const [name, fn] of [['KH_WaterStep.wav', step], ['KH_WaterSplash.wav', big], ['KH_WaterPaddle.wav', paddle]]) {
  const buf = wav(fn()); total += buf.length;
  fs.writeFileSync(path.join(OUT, name), buf);
  console.log('[kh-gen-water-se] ' + name + ' ' + buf.length + ' bytes');
}
console.log('[kh-gen-water-se] total ' + total + ' bytes (limit 204800)');
if (total > 204800) { console.error('over the 200 KB budget'); process.exit(1); }
