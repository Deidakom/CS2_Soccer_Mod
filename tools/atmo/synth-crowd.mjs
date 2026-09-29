#!/usr/bin/env node
// "Arena Vision" sound lab (2026-09-29): stadium crowd sounds synthesised
// from scratch, so they are our own material (no licence questions).
// Hundreds of virtual voices = sawtooth "glottis" through two formant
// band-pass filters (vowel), random pitch/vibrato/pan/onset, plus band
// limited noise for breath/claps and a Schroeder reverb for the bowl.
//
// usage: node tools/atmo/synth-crowd.mjs <outDir>
// writes: murmur.wav (loopable bed), roar.wav (goal), ooh.wav (near miss),
//         applause.wav (save), chant.wav (drums + "hey"), whistle.wav
import fs from "node:fs";
import path from "node:path";

const outDir = process.argv[2] || ".";
fs.mkdirSync(outDir, { recursive: true });
const SR = 44100;
let seed = 12345;
const rand = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
const rr = (a, b) => a + rand() * (b - a);

// RBJ band-pass (constant 0 dB peak)
function bandpass(f, q) {
  const w = 2 * Math.PI * f / SR, a = Math.sin(w) / (2 * q), c = Math.cos(w), a0 = 1 + a;
  const b0 = a / a0, b2 = -a / a0, a1 = -2 * c / a0, a2 = (1 - a) / a0;
  let x1 = 0, x2 = 0, y1 = 0, y2 = 0;
  return (x) => { const y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x; y2 = y1; y1 = y; return y; };
}
function lowpass(f) { const k = Math.exp(-2 * Math.PI * f / SR); let y = 0; return (x) => (y = x + k * (y - x)); }

const VOWELS = { a: [780, 1180], o: [450, 820], e: [520, 1750], u: [340, 720], i: [300, 2150], ae: [650, 1550] };

// A crowd of voices. env(t, voice) -> 0..1 gain, pitch(t, voice) -> multiplier, vowel(t, voice) -> key.
function voices(seconds, n, { env, pitch = () => 1, vowel = () => "a", level = 1 }) {
  const len = Math.floor(seconds * SR), L = new Float32Array(len), R = new Float32Array(len);
  for (let v = 0; v < n; v++) {
    const male = rand() < 0.72, f0 = male ? rr(92, 150) : rr(170, 255);
    const vib = rr(4, 6.5), vibDepth = rr(0.01, 0.025), phase0 = rand();
    const pan = rr(0.05, 0.95), gain = rr(0.5, 1) * level;
    const voice = { f0, male, seed: rand(), onset: rr(0, 0.3) };
    let vw = vowel(0, voice), F = VOWELS[vw];
    let f1 = bandpass(F[0] * rr(0.92, 1.08), 5), f2 = bandpass(F[1] * rr(0.92, 1.08), 7);
    let ph = phase0;
    const soft = lowpass(male ? 1500 : 2100);   // rounds off the buzzy saw (owner: less scratchy)
    for (let i = 0; i < len; i++) {
      const t = i / SR;
      if (i % 2205 === 0) { const nv = vowel(t, voice); if (nv !== vw) { vw = nv; F = VOWELS[vw]; f1 = bandpass(F[0] * rr(0.92, 1.08), 5); f2 = bandpass(F[1] * rr(0.92, 1.08), 7); } }
      const e = env(t, voice); if (e <= 0.0005) { f1(0); f2(0); continue; }
      const f = f0 * pitch(t, voice) * (1 + vibDepth * Math.sin(2 * Math.PI * vib * t));
      ph += f / SR; if (ph >= 1) ph -= 1;
      const saw = soft(2 * ph - 1);
      const s = (f1(saw) + 0.55 * f2(saw)) * e * gain;
      L[i] += s * (1 - pan); R[i] += s * pan;
    }
  }
  return [L, R];
}

function noise(seconds, env, { lo = 400, hi = 3000, level = 1 } = {}) {
  const len = Math.floor(seconds * SR), L = new Float32Array(len), R = new Float32Array(len);
  const bpL = bandpass((lo + hi) / 2, 0.5), bpR = bandpass((lo + hi) / 2, 0.5);
  let b0 = 0, b1 = 0, b2 = 0;
  for (let i = 0; i < len; i++) {
    const w = rand() * 2 - 1; b0 = 0.99765 * b0 + w * 0.099046; b1 = 0.963 * b1 + w * 0.2965164; b2 = 0.57 * b2 + w * 1.0526913;
    const pink = (b0 + b1 + b2 + w * 0.1848) * 0.2, e = env(i / SR) * level;
    L[i] = bpL(pink) * e; R[i] = bpR(pink + (rand() - 0.5) * 0.05) * e;
  }
  return [L, R];
}

// One hand clap (v3, 2026-09-29 owner: applause sounded wrong): 0.5 ms attack, 3-7 ms decay,
// the hand cavity as a resonant band (own per person) plus a broadband snap; far clappers lose treble.
function clapAt(L, R, st, amp, pan, hand = { f: rr(800, 2600), q: rr(1.4, 2.8) }, air = 1) {
  const res = bandpass(hand.f * rr(0.93, 1.07), hand.q), body = bandpass(rr(330, 520), 1.2), snapLp = lowpass(1800 + 5200 * air), tau = SR * rr(0.003, 0.007), att = SR * 0.0005;
  let hp = 0;
  for (let i = 0; i < tau * 6 && st + i < L.length; i++) {
    const e = (i < att ? i / att : 1) * Math.exp(-i / tau), n = (rand() * 2 - 1) * e;
    const snap = snapLp(n); hp += 0.3 * (snap - hp);
    const v = (res(n) * 1.3 + body(n) * 0.9 + (snap - hp) * 0.6) * amp;
    L[st + i] += v * (1 - pan * 0.8); R[st + i] += v * (0.2 + pan * 0.8);
  }
}
// count random claps (cheering crowd under the roar)
function claps(seconds, count, density, level = 1) {
  const len = Math.floor(seconds * SR), L = new Float32Array(len), R = new Float32Array(len);
  for (let k = 0; k < count; k++) clapAt(L, R, Math.floor(density() * SR), rr(0.3, 1) * level, rand());
  return [L, R];
}
// people clapping rhythmically: own rate (3.4-5.4 Hz, steady like real people), own hand timbre,
// distance d (a few close, most far): level 1/d and treble loss with distance.
function clappers(seconds, n, swell, level = 1, near = 0.06) {
  const len = Math.floor(seconds * SR), L = new Float32Array(len), R = new Float32Array(len);
  for (let p = 0; p < n; p++) {
    const d = rand() < near ? rr(1, 2.5) : rr(4, 18), air = Math.max(0, 1 - (d - 1) / 17);
    const rate = rr(3.4, 5.4), pan = rand(), loud = level / d, stop = rr(0.6, 1) * seconds, hand = { f: rr(800, 2600), q: rr(1.4, 2.8) };
    let period = 1 / rate;
    for (let t = rr(0, 0.35); t < stop; t += period * rr(0.96, 1.04)) { period *= 0.9995; clapAt(L, R, Math.floor(t * SR), loud * swell(t) * rr(0.8, 1), pan, hand, air); }
  }
  return [L, R];
}

function drum(buf, t, f = 140, g = 0.8) {
  const [L, R] = buf, st = Math.floor(t * SR);
  let ph = 0;
  for (let i = 0; i < SR * 0.4 && st + i < L.length; i++) {
    const tt = i / SR, fr = 52 + (f - 52) * Math.exp(-tt / 0.05); ph += fr / SR;
    const s = Math.sin(2 * Math.PI * ph) * Math.exp(-tt / 0.12) * g + (i < 300 ? (rand() - 0.5) * 0.3 * (1 - i / 300) : 0);
    L[st + i] += s; R[st + i] += s;
  }
}

// Schroeder reverb: 4 combs + 2 allpasses per channel, a stadium-sized tail.
function reverb([L, R], wet = 0.35, tail = 1.0) {
  const out = [L, R].map((ch, c) => {
    const combs = [1557, 1617, 1491, 1422].map((d) => ({ d: Math.floor(d * tail) + c * 23, buf: new Float32Array(Math.floor(d * tail) + c * 23), i: 0, g: 0.84 }));
    const aps = [556, 441].map((d) => ({ d, buf: new Float32Array(d), i: 0 }));
    const lp = lowpass(3500), res = new Float32Array(ch.length);
    for (let n = 0; n < ch.length; n++) {
      let s = 0; const x = ch[n];
      for (const cb of combs) { const y = cb.buf[cb.i]; cb.buf[cb.i] = x + y * cb.g; cb.i = (cb.i + 1) % cb.d; s += y; }
      s /= 4;
      for (const ap of aps) { const y = ap.buf[ap.i]; const v = s + y * 0.5; ap.buf[ap.i] = v; ap.i = (ap.i + 1) % ap.d; s = y - v * 0.5; }
      res[n] = x * (1 - wet) + lp(s) * wet * 2.2;
    }
    return res;
  });
  return out;
}

function mix(...bufs) {
  const len = Math.max(...bufs.map((b) => b[0].length)), L = new Float32Array(len), R = new Float32Array(len);
  for (const [bl, br] of bufs) for (let i = 0; i < bl.length; i++) { L[i] += bl[i]; R[i] += br[i]; }
  return [L, R];
}

// Master (owner: louder, less scratchy): 60 Hz highpass, 2-pole ~6.5 kHz lowpass, RMS to a
// fixed level, soft tanh saturation instead of hard peaks, then peak-normalise.
function master([L, R], { lp = 6500, rms = 0.2, limiter = false } = {}) {
  const out = [L, R].map((ch) => {
    const lp1 = lowpass(lp), lp2 = lowpass(lp), hp = lowpass(60), res = new Float32Array(ch.length);
    for (let i = 0; i < ch.length; i++) { const x = lp2(lp1(ch[i])); res[i] = x - hp(x); }
    return res;
  });
  let sum = 0; for (const ch of out) for (const v of ch) sum += v * v;
  const g = rms / (Math.sqrt(sum / (out[0].length * 2)) || 1e-9);
  if (!limiter) {
    const d = Math.tanh(1.4);
    for (const ch of out) for (let i = 0; i < ch.length; i++) ch[i] = Math.tanh(1.4 * ch[i] * g) / d;
    return out;
  }
  // lookahead peak limiter (keeps the snap of claps): 2 ms lookahead, 80 ms release, ceiling 0.95
  const look = Math.floor(0.002 * SR), rel = Math.exp(-1 / (0.08 * SR)), n = out[0].length, need = new Float32Array(n);
  for (let i = 0; i < n; i++) { const p = Math.max(Math.abs(out[0][i]), Math.abs(out[1][i])) * g; need[i] = p > 0.95 ? 0.95 / p : 1; }
  let gain = 1;
  for (let i = 0; i < n; i++) {
    let m = 1; for (let j = i; j < Math.min(n, i + look); j++) m = Math.min(m, need[j]);
    gain = m < gain ? m : 1 - (1 - gain) * rel;
    out[0][i] *= g * gain; out[1][i] *= g * gain;
  }
  return out;
}

function writeWav(name, buf, fadeIn = 0.02, fadeOut = 0.2, opts = {}) {
  const [L, R] = master(buf, opts);
  let peak = 1e-9; for (let i = 0; i < L.length; i++) peak = Math.max(peak, Math.abs(L[i]), Math.abs(R[i]));
  const k = 0.95 / peak, len = L.length, data = Buffer.alloc(len * 4);
  for (let i = 0; i < len; i++) {
    const t = i / SR, g = Math.min(1, t / fadeIn, (len / SR - t) / fadeOut);
    data.writeInt16LE(Math.round(Math.max(-1, Math.min(1, L[i] * k * g)) * 32767), i * 4);
    data.writeInt16LE(Math.round(Math.max(-1, Math.min(1, R[i] * k * g)) * 32767), i * 4 + 2);
  }
  const h = Buffer.alloc(44);
  h.write("RIFF", 0); h.writeUInt32LE(36 + data.length, 4); h.write("WAVE", 8); h.write("fmt ", 12); h.writeUInt32LE(16, 16);
  h.writeUInt16LE(1, 20); h.writeUInt16LE(2, 22); h.writeUInt32LE(SR, 24); h.writeUInt32LE(SR * 4, 28); h.writeUInt16LE(4, 32); h.writeUInt16LE(16, 34);
  h.write("data", 36); h.writeUInt32LE(data.length, 40);
  fs.writeFileSync(path.join(outDir, name), Buffer.concat([h, data]));
  console.log(`${name} ${(len / SR).toFixed(1)} s`);
}

// Babble: syllables with random vowels, gaps; loops over 12 s.
function babbleEnv(seconds) {
  return (t, v) => {
    if (!v.syl) { v.syl = []; let s = rr(0, 0.6); while (s < seconds) { const d = rr(0.09, 0.26); v.syl.push([s, s + d, rr(0.25, 1)]); s += d + (rand() < 0.3 ? rr(0.25, 0.9) : rr(0.02, 0.08)); } v.k = 0; v.vw = []; for (let i = 0; i < v.syl.length; i++) v.vw.push("aeiou"[Math.floor(rand() * 5)].replace("i", "ae")); }
    while (v.k < v.syl.length - 1 && t > v.syl[v.k][1]) v.k++;
    const [a, b, g] = v.syl[v.k]; if (t < a || t > b) return 0;
    return g * Math.min(1, (t - a) / 0.03, (b - t) / 0.05);
  };
}

const murmurLen = 12;
const bab = babbleEnv(murmurLen);
writeWav("murmur.wav", reverb(mix(
  voices(murmurLen, 90, { env: bab, vowel: (t, v) => (v.vw ? v.vw[v.k || 0] : "a"), level: 0.8 }),
  noise(murmurLen, () => 0.25, { lo: 300, hi: 1600 }),
), 0.45, 1.2), 0.5, 0.5);

writeWav("roar.wav", reverb(mix(
  voices(5, 160, {
    env: (t, v) => { const on = v.onset * 0.8; if (t < on) return 0; const a = Math.min(1, (t - on) / 0.3); return a * (t < 2.4 ? 1 : Math.exp(-(t - 2.4) / 0.9)); },
    pitch: (t) => 1 + 0.14 * Math.min(1, t / 0.6),
    vowel: (t) => (t < 1.8 ? "a" : "e"),
  }),
  noise(5, (t) => Math.min(1, t / 0.3) * (t < 2.4 ? 1 : Math.exp(-(t - 2.4) / 1.0)), { lo: 500, hi: 2400, level: 1.2 }),
  claps(5, 2600, () => 0.6 + Math.pow(rand(), 1.4) * 4.2, 0.5),
), 0.4, 1.3));

writeWav("ooh.wav", reverb(voices(2.8, 150, {
  env: (t, v) => { const on = v.onset * 0.5; if (t < on) return 0; return Math.min(1, (t - on) / 0.18) * (t < 1.1 ? 1 : Math.exp(-(t - 1.1) / 0.45)); },
  pitch: (t) => 1.08 - 0.22 * Math.min(1, t / 1.8),
  vowel: () => "o",
}), 0.42, 1.2));

writeWav("applause.wav", reverb(mix(
  clappers(5, 420, (t) => Math.min(1, t / 0.35) * (t < 2.8 ? 1 : Math.exp(-(t - 2.8) / 1.0))),
  noise(5, (t) => 0.05 * Math.min(1, t / 0.5) * (t < 2.8 ? 1 : Math.exp(-(t - 2.8) / 1.0)), { lo: 300, hi: 1500 }),
), 0.28, 1.2), 0.02, 0.6, { lp: 14000, rms: 0.19, limiter: true });

{
  const chant = mix(noise(8, () => 0.06, { lo: 300, hi: 1500 }), [new Float32Array(8 * SR), new Float32Array(8 * SR)]);
  for (let bar = 0; bar < 4; bar++) for (const x of [0, 0.5, 1.0, 1.25, 1.5]) drum(chant, 0.1 + bar * 2 + x);
  const hey = voices(8, 120, {
    env: (t) => { for (let bar = 0; bar < 4; bar++) { const s = 0.1 + bar * 2 + 1.75; if (t >= s && t < s + 0.35) return Math.min(1, (t - s) / 0.03) * Math.exp(-(t - s) / 0.25); } return 0; },
    vowel: () => "e", pitch: () => 1.2,
  });
  writeWav("chant.wav", reverb(mix(chant, hey), 0.35, 1.2));
}

{
  const len = Math.floor(1.3 * SR), L = new Float32Array(len), R = new Float32Array(len);
  let ph = 0, ph2 = 0;
  for (let i = 0; i < len; i++) {
    const t = i / SR, f = 2800 + 200 * Math.min(1, t / 0.08), am = 0.55 + 0.45 * Math.sin(2 * Math.PI * 27 * t);
    ph += f / SR; ph2 += 2 * f / SR;
    const e = Math.min(1, t / 0.03, (1.2 - t) / 0.08) * (t < 1.2 ? 1 : 0);
    const s = (Math.sin(2 * Math.PI * ph) + 0.15 * Math.sin(2 * Math.PI * ph2)) * am * e * 0.6 + (rand() - 0.5) * 0.02 * e;
    L[i] = s; R[i] = s;
  }
  writeWav("whistle.wav", reverb([L, R], 0.3, 1.0));
}
