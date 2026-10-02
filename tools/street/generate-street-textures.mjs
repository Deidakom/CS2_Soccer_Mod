#!/usr/bin/env node
// SoccerMod street arena: every texture and material, generated. The pictures with lettering and
// the graffiti come from render-street-art.ps1 (run that first); a picture that is missing is
// replaced by a flat colour, so the map can be looked at before the art exists.
//
//   node tools/street/generate-street-textures.mjs --art <dir> [--addon <content addon dir>] [--preview <dir>] [--only a,b]
// Writes materials/soccermod_street/<name>.vmat + _color / _normal / _trans / _illum .png into the
// addon, and the same pictures plus materials.json for the local viewer into the preview dir.
import fs from "node:fs";
import path from "node:path";
import { Img, readPng, makeNoise, mix, smooth, gridLine, clamp01 } from "../arena/lib/img.mjs";
import { shade, concrete, steel } from "../arena/lib/recipes.mjs";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => { if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
if (!args.art) throw new Error("usage: --art <dir> [--addon <dir>] [--preview <dir>] [--only a,b]");
const DIR = "materials/soccermod_street";
const only = args.only?.split(",");
const hash = (s) => { let h = 2166136261; for (const c of s) h = Math.imul(h ^ c.charCodeAt(0), 16777619) >>> 0; return h; };
const art = (name) => { const f = path.join(args.art, `${name}.png`); return fs.existsSync(f) ? readPng(f) : null; };
// a piece without its empty border (the pictures are drawn on a roomy canvas)
const trimmed = (src) => { let x0 = src.w, x1 = -1, y0 = src.h, y1 = -1; for (let y = 0; y < src.h; y++) for (let x = 0; x < src.w; x++) if (src.d[(y * src.w + x) * 4 + 3] > 10) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; } if (x1 < 0) return src; const out = new Img(x1 - x0 + 1, y1 - y0 + 1); for (let y = 0; y < out.h; y++) for (let x = 0; x < out.w; x++) { const o = ((y + y0) * src.w + x + x0) * 4; out.d.set(src.d.subarray(o, o + 4), (y * out.w + x) * 4); } return out; };
const missing = [];

// ---- small building blocks -------------------------------------------------------------------------
const flat = (rgb) => ({ color: new Img(16, 16).fill(() => rgb) });
const lum = (c) => (c[0] * 0.3 + c[1] * 0.59 + c[2] * 0.11) / 255;
// a picture from the art folder (opaque); with alpha: the picture's own alpha becomes the cut-out / blend mask
function picture(name, { alpha = false, fallback = null, weather = 0, faded = 0 } = {}) {
  const src = art(name);
  // faded: paint on the ground that many feet have worn - the whole picture thinner, rubbed off in patches
  if (src && faded) { const n = makeNoise(hash(name) % 997 + 3); for (let y = 0; y < src.h; y++) for (let x = 0; x < src.w; x++) { const u = x / src.w, v = y / src.h, o = (y * src.w + x) * 4; src.d[o + 3] *= (1 - faded * 0.45) * (1 - faded * smooth(0.42, 0.7, n.fbm(u, v, 7, 4) * 0.7 + n(u, v, 200) * 0.3)); } }
  // a painted front that has seen some years: uneven, streaked from above, dirty at the foot
  if (src && weather) { const n = makeNoise(hash(name) % 1000 + 1); for (let y = 0; y < src.h; y++) for (let x = 0; x < src.w; x++) { const u = x / src.w, v = y / src.h, o = (y * src.w + x) * 4, k = 1 + ((n.fbm(u * 2 % 1, v, 6, 3) - 0.5) * 0.3 + (n(u * 3 % 1, v, 256, 64) - 0.5) * 0.1 - Math.max(0, n.fbm(u * 2 % 1, v, 30, 3, 20) - 0.56) * 0.9 - smooth(0.86, 1, v) * 0.3) * weather; src.d[o] *= k; src.d[o + 1] *= k; src.d[o + 2] *= k; } }
  if (!src) { missing.push(name); const h = hash(name), c = fallback ?? [90 + h % 130, 90 + (h >> 8) % 130, 90 + (h >> 16) % 130]; const img = new Img(64, 64).fill(() => c); return alpha ? { color: img, alpha: new Img(64, 64).fill((x, y) => (Math.hypot(x - 32, y - 32) < 26 ? [200, 200, 200] : [0, 0, 0])) } : { color: img }; }
  if (!alpha) return { color: src };
  // with an alpha channel the compiler wants power-of-two sizes: resample to the next ones
  const pot = (v) => 2 ** Math.ceil(Math.log2(v) - 0.2), W = Math.min(2048, pot(src.w)), H = Math.min(2048, pot(src.h));
  if (W !== src.w || H !== src.h) { const big = new Img(W, H); for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) { const fx = (x + 0.5) * src.w / W - 0.5, fy = (y + 0.5) * src.h / H - 0.5, x0 = Math.max(0, Math.floor(fx)), y0 = Math.max(0, Math.floor(fy)), x1 = Math.min(src.w - 1, x0 + 1), y1 = Math.min(src.h - 1, y0 + 1), tx = Math.max(0, fx - x0), ty = Math.max(0, fy - y0); for (let c = 0; c < 4; c++) { const p = (X, Y) => src.d[(Y * src.w + X) * 4 + c]; big.d[(y * W + x) * 4 + c] = (p(x0, y0) * (1 - tx) + p(x1, y0) * tx) * (1 - ty) + (p(x0, y1) * (1 - tx) + p(x1, y1) * tx) * ty; } } return splitAlpha(big); }
  return splitAlpha(src);
}
function splitAlpha(src) {
  const color = new Img(src.w, src.h), a = new Img(src.w, src.h);
  color.d.set(src.d); for (let i = 0; i < src.w * src.h; i++) { const v = src.d[i * 4 + 3]; a.d.set([v, v, v, 255], i * 4); color.d[i * 4 + 3] = 255; }
  // bleed the colour into the transparent part, so the edges do not go dark
  for (let pass = 0; pass < 4; pass++) { const s = color.d.slice(); for (let y = 1; y < src.h - 1; y++) for (let x = 1; x < src.w - 1; x++) { const o = (y * src.w + x) * 4; if (a.d[o] > 8 || s[o + 3] === 254) continue; let r = 0, g = 0, b = 0, n = 0; for (const d of [-4, 4, -src.w * 4, src.w * 4]) if (a.d[o + d] > 8 || s[o + d + 3] === 254) { r += s[o + d]; g += s[o + d + 1]; b += s[o + d + 2]; n++; } if (n) color.d.set([r / n, g / n, b / n, 254], o); } }
  return { color, alpha: a };
}

function asphalt({ base = [78, 78, 82], seed = 1, patches = 0.5, size = 1024 } = {}) {
  const n = makeNoise(seed), img = new Img(size, size);
  const h = (x, y, u, v) => n(u, v, size / 2) * 1.6 + n(u + 0.3, v + 0.7, size / 4) * 0.8;
  img.fill((x, y, u, v) => {
    const grit = n(u, v, size / 2), fine = n(u + 0.37, v + 0.11, size), blotch = n.fbm(u, v, 4, 4) - 0.5, patch = smooth(0.56, 0.62, n.fbm(u + 0.5, v + 0.2, 3, 2)) * patches;
    let k = 1 + (grit - 0.5) * 0.34 + (fine - 0.5) * 0.2 + blotch * 0.3 - patch * 0.22;
    let c = shade(base, k);
    if (grit > 0.84) c = mix(c, [150, 148, 142], 0.45); else if (fine > 0.9) c = mix(c, [40, 40, 44], 0.5);
    const oil = Math.max(0, n.fbm(u + 0.1, v + 0.6, 5, 3) - 0.66) * 1.6; c = mix(c, [34, 34, 38], Math.min(0.6, oil));
    return c;
  });
  return { color: img, height: h };
}
// worn court paint: the colour with the asphalt's grain showing through where it is rubbed off
function courtPaint(rgb, seed) {
  const s = 512, n = makeNoise(seed), img = new Img(s, s);
  img.fill((x, y, u, v) => { const wear = smooth(0.52, 0.72, n.fbm(u, v, 6, 4) * 0.7 + n(u, v, 256) * 0.3), grit = (n(u, v, 256) - 0.5) * 0.18; return mix(shade(rgb, 1 + grit), shade([82, 82, 86], 1 + grit), wear * 0.85); });
  return { color: img };
}
// the rays of the court's painting: colour with the asphalt's grain, rubbed off in patches
function rayPaint(rgb, seed) {
  const s = 512, n = makeNoise(seed), img = new Img(s, s);
  img.fill((x, y, u, v) => { const wear = smooth(0.6, 0.8, n.fbm(u, v, 5, 4) * 0.72 + n(u, v, 256) * 0.28), grit = (n(u, v, 256) - 0.5) * 0.2, fade = 0.9 + (n.fbm(u + 0.4, v + 0.2, 3, 3) - 0.5) * 0.24; return mix(shade(rgb, fade + grit), shade([92, 90, 92], 1 + grit), wear * 0.75); });
  return { color: img, height: (x, y, u, v) => n(u, v, 256) * 0.8 };
}
// The court's painting, laid once over the whole court: loose rays sprayed from the centre spot (some missing,
// their edges soft and speckled, broken off where they are worn), two big rings, a field of dots. Thin
// everywhere: the asphalt shows through.
function courtArt() {
  const S = 2048, HX = 650, HY = 900, n = makeNoise(171), img = new Img(S, S), alpha = new Img(S, S);
  const pal = [[60, 168, 172], [226, 104, 86], [238, 198, 72], [214, 106, 160], [236, 232, 220]], RAYS = 16, step = 2 * Math.PI / RAYS;
  const ray = [...Array(RAYS).keys()].map((k) => { const h = hash("ray" + k); return { on: h % 5 !== 0 && h % 7 !== 1, c: pal[k % 4], r0: 150 + (h >> 4) % 130, r1: 520 + (h >> 9) % 620, half: 0.26 + ((h >> 14) % 20) / 100 }; });
  for (let y = 0; y < S; y++) for (let x = 0; x < S; x++) {
    const u = (x + 0.5) / S, v = (y + 0.5) / S, wx = (u - 0.5) * 2 * HX, wy = (0.5 - v) * 2 * HY, r = Math.hypot(wx, wy), o = (y * S + x) * 4;
    const speck = n(u, v, 700), wob = (n.fbm(u, v, 5, 3) - 0.5) * 0.5, ang = ((Math.atan2(wy, wx) + wob * 0.6 + 2 * Math.PI) % (2 * Math.PI)), k = Math.floor(ang / step), q = ray[k], d = Math.abs(ang / step - k - 0.5);
    let c = [0, 0, 0], a = 0;
    if (q.on && r > q.r0 * 0.7) {
      const edge = smooth(q.half, q.half - 0.12, d + (speck - 0.5) * 0.1), along = smooth(q.r0 * 0.7, q.r0 * 1.2, r) * smooth(q.r1, q.r1 * 0.72, r + (n.fbm(u, v, 20, 3) - 0.5) * 220);
      c = q.c; a = edge * along * 0.64;
    }
    // two sprayed rings and a field of dots that thins out
    for (const [cx, cy, rr, w, ci] of [[-170, 260, 250, 16, 4], [240, -330, 200, 13, 4]]) { const ringD = Math.abs(Math.hypot(wx - cx, wy - cy) - rr), ra = smooth(w, w * 0.35, ringD + (speck - 0.5) * 8) * 0.62; if (ra > a) { c = pal[ci]; a = ra; } }
    { const cell = 30, gx = ((wx + 2000) % cell) - cell / 2, gy = ((wy + 2000) % cell) - cell / 2, f = smooth(520, 120, Math.hypot(wx - 420, wy - 560)) + smooth(480, 120, Math.hypot(wx + 430, wy + 620)), da = smooth(9 * f, 9 * f - 2.5, Math.hypot(gx, gy)) * (f > 0.05 ? 0.5 : 0); if (da > a) { c = pal[2]; a = da; } }
    // worn off in patches, speckled like spray, never solid
    a *= (1 - smooth(0.48, 0.74, n.fbm(u, v, 9, 4) * 0.72 + speck * 0.28) * 0.72) * (0.82 + speck * 0.3);
    img.d[o] = c[0]; img.d[o + 1] = c[1]; img.d[o + 2] = c[2]; img.d[o + 3] = 255; const av = clamp01(a) * 255; alpha.d[o] = alpha.d[o + 1] = alpha.d[o + 2] = av; alpha.d[o + 3] = 255;
  }
  // where nothing is sprayed the colour of the nearest ray is kept (no dark fringe)
  for (let i = 0; i < S * S; i++) if (alpha.d[i * 4] < 2) { const x = i % S, y = Math.floor(i / S), u = (x + 0.5) / S, v = (y + 0.5) / S, k = Math.floor((((Math.atan2((0.5 - v) * 2 * HY, (u - 0.5) * 2 * HX) + 2 * Math.PI) % (2 * Math.PI))) / step), q = ray[k].c; img.d[i * 4] = q[0]; img.d[i * 4 + 1] = q[1]; img.d[i * 4 + 2] = q[2]; }
  return { color: img, alpha };
}
// cobbles: small uneven stones in rows
function cobble() {
  const s = 512, n = makeNoise(141), img = new Img(s, s), cell = 32;
  const at = (x, y) => { const row = Math.floor(y / cell), ox = (row % 2) * cell / 2, cx = Math.floor((x + ox) / cell), lx = (x + ox) % cell, ly = y % cell, id = hash(`c${cx % (s / cell)},${row}`), j = Math.min(lx, cell - 1 - lx, ly, cell - 1 - ly) + (((id >> 4) % 5) - 2) * 0.5; return { id, j }; };
  img.fill((x, y, u, v) => { const { id, j } = at(x, y); if (j < 2.2) return [58, 54, 50]; return shade([[150, 142, 130], [128, 122, 116], [166, 150, 132], [118, 110, 104]][id % 4], 0.86 + ((id >> 8) % 100) / 100 * 0.22 + (n(u, v, 128) - 0.5) * 0.2 + smooth(2.2, 7, j) * 0.1); });
  return { color: img, height: (x, y) => smooth(1.5, 7, at(x, y).j) * 2.2 };
}
// roof tiles: rows of round clay tiles
function roofTile() {
  const s = 256, n = makeNoise(142), img = new Img(s, s);
  img.fill((x, y, u, v) => { const col = Math.floor(x / 16), k = Math.abs(((x % 16) / 16) - 0.5) * 2, row = Math.floor(y / 32), lip = (y % 32) < 3 ? 0.7 : 1; return shade([190, 98, 62], (0.72 + (1 - k) * 0.4) * lip * (0.9 + ((hash(`t${col},${row}`) % 100) / 100) * 0.2 + (n(u, v, 64) - 0.5) * 0.14)); });
  return { color: img };
}
// a colonial window or door: an arched opening in a white frame; outside the arch nothing (the wall shows)
function archPic(kind, seed) {
  const w = 128, h = 256, n = makeNoise(seed), p = painter(w, h), r = w / 2 - 4, cy = r + 6;
  const inArch = (x, y, inset) => (y >= cy ? Math.abs(x - w / 2 + 0.5) < r - inset && y < h - (kind.startsWith("win") ? inset : 0) : Math.hypot(x - w / 2 + 0.5, y - cy) < r - inset);
  const shut = [[40, 110, 86], [38, 84, 140], [120, 52, 44]][seed % 3], wood = [[70, 104, 120], [112, 50, 42], [84, 60, 44]][seed % 3];
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    if (!inArch(x, y, 0)) continue;
    const u = x / w, v = y / h, g = (n(u, v, 64) - 0.5) * 0.14; let c;
    if (!inArch(x, y, 9)) c = shade([238, 234, 224], 0.94 + g - (x < w / 2 ? 0 : 0.06));
    else if (kind === "win_bell") { const bell = Math.hypot((x - w / 2) * 1.1, (y - h * 0.5) * 0.8) < 26 && y > h * 0.36 || (Math.abs(x - w / 2) < 30 && Math.abs(y - h * 0.62) < 6); c = bell ? shade([150, 116, 60], 0.8 + (1 - Math.abs(x - w / 2) / 30) * 0.4) : Math.abs(x - w / 2) < 3 && y < h * 0.4 ? [50, 40, 34] : shade([26, 22, 24], 1 + v * 0.6); }
    else if (kind.startsWith("door")) { const church = kind === "door_church", base = church ? [86, 56, 36] : wood, leaf = Math.abs(x - w / 2 + 0.5) < 1.5, panel = Math.min(gridLine(x - 16, church ? 24 : 40), gridLine(y - 30, church ? 44 : 70)) < 2.2, fan = y < cy + 6; c = fan && !church ? (Math.abs(((Math.atan2(cy + 6 - y, x - w / 2) / Math.PI) * 5) % 1 - 0.5) > 0.42 || Math.hypot(x - w / 2, y - cy - 6) < 12 ? [236, 232, 222] : [44, 52, 66]) : shade(base, (0.9 + g + (n.fbm(u, v, 30, 3, 10) - 0.5) * 0.3) * (leaf ? 0.4 : 1) * (panel ? 0.72 : 1)); if (church && Math.hypot(x - w / 2 - 8, y - h * 0.62) < 3.5 || church && Math.hypot(x - w / 2 + 8, y - h * 0.62) < 3.5) c = [200, 170, 90]; }
    else if (kind === "win_col_b") c = shade(shut, (0.82 + ((y % 9) / 9) * 0.3 + g) * (Math.abs(x - w / 2 + 0.5) < 1.5 ? 0.5 : 1));
    else { const bar = gridLine(x - w / 2, 26) < 2 || gridLine(y - cy, 40) < 2, lit = kind === "win_col_lit"; c = bar ? [238, 234, 224] : lit ? shade([255, 206, 128], 0.62 + Math.exp(-(((u - 0.5) / 0.3) ** 2 + ((v - 0.4) / 0.3) ** 2)) * 0.5) : mix([28, 34, 46], [110, 126, 150], smooth(0.8, 0.1, v) * 0.45); if (!bar && x < 30 + (seed % 2) * 6) c = shade(shut, 0.8 + ((y % 9) / 9) * 0.3); }
    p.px(x, y, c);
  }
  return bleed(p, [236, 232, 222]);
}
// the church's round window: a stone ring, spokes, coloured glass with light behind it
function roseWindow() {
  const s = 128, p = painter(s, s), pal = [[226, 60, 50], [240, 190, 50], [60, 120, 210], [60, 170, 110], [200, 80, 170]];
  for (let y = 0; y < s; y++) for (let x = 0; x < s; x++) { const d = Math.hypot(x - 63.5, y - 63.5), a = Math.atan2(y - 63.5, x - 63.5); if (d > 62) continue; const spoke = Math.abs((((a / Math.PI * 6) % 1) + 1) % 1 - 0.5) > 0.44, ring = d > 52 || Math.abs(d - 30) < 2.5 || d < 8; p.px(x, y, ring || spoke ? shade([222, 206, 170], 0.9 + (x + y) % 7 * 0.01) : shade(pal[(Math.floor((a / Math.PI + 1) * 6) + (d > 30 ? 2 : 0)) % pal.length], 0.9 + (52 - d) * 0.006)); }
  return bleed(p, [222, 206, 170]);
}
// a palm: the trunk's rings, and one frond (the rib runs up the picture, leaflets to both sides)
function palmTrunk() { const s = 128, n = makeNoise(151), img = new Img(s, s); img.fill((x, y, u, v) => shade([150, 132, 108], 0.8 + ((y % 16) < 3 ? -0.22 : ((y % 16) / 16) * 0.2) + (n(u, v, 64) - 0.5) * 0.2)); return { color: img }; }
function palmFrond() {
  const w = 128, h = 256, p = painter(w, h), n = makeNoise(152);
  for (let y = 0; y < h; y++) { const t = 1 - y / h, half = (8 + 52 * Math.sin(Math.min(1, t * 1.25) * Math.PI) ** 0.7) * (0.9 + n(0.3, y / h, 40) * 0.2); for (let x = 0; x < w; x++) { const dx = x - w / 2 + 0.5, a = Math.abs(dx); if (a > half) continue; const leaflet = ((y + a * 0.9) % 9) < 6.2 || a < 3; if (!leaflet) continue; p.px(x, y, a < 2.2 ? [150, 150, 84] : shade(mix([44, 110, 50], [110, 160, 60], (y + a) % 9 / 9), (0.74 + t * 0.3) * (dx < 0 ? 0.86 : 1.08))); } }
  return bleed(p, [60, 120, 56]);
}
// the canopy's sheet: eight plain colours for the flags (the last one is the string), four kites underneath
export const CANOPY_SHEET = { w: 256, h: 128, flags: [[226, 50, 46], [246, 200, 40], [50, 160, 84], [44, 110, 210], [236, 96, 160], [240, 130, 40], [240, 238, 228]], string: 7 };
function canopySheet() {
  const { w, h, flags } = CANOPY_SHEET, p = painter(w, h), cols = [...flags, [26, 26, 30]];
  for (let k = 0; k < 8; k++) p.rect(k * 32, 0, k * 32 + 32, 64, (x, y) => shade(cols[k], 0.92 + ((x * 5 + y * 3) % 9) * 0.014));
  const kites = [[[226, 50, 46], [246, 200, 40]], [[44, 110, 210], [240, 238, 228]], [[50, 160, 84], [246, 200, 40]], [[236, 96, 160], [44, 110, 210]]];
  for (let k = 0; k < 4; k++) for (let y = 0; y < 64; y++) for (let x = 0; x < 64; x++) { const dx = Math.abs(x - 31.5) / 30, dy = y < 22 ? (22 - y) / 21 : (y - 22) / 41; if (dx + dy > 1) continue; const stick = Math.abs(x - 31.5) < 1.2 || Math.abs(y - 22) < 1.2; p.px(k * 64 + x, 64 + y, stick ? [40, 34, 30] : kites[k][(x < 31.5) === (y < 22) ? 0 : 1]); }
  return bleed(p, [200, 200, 200]);
}
function brick({ base, mortar = [150, 146, 138], seed = 1, vary = 0.2, grime = 0.5, painted = null }) {
  const s = 512, n = makeNoise(seed), img = new Img(s, s), BW = 64, BH = 16;
  const cell = (x, y) => { const row = Math.floor(y / BH), xo = x + (row % 2) * BW / 2; return [Math.floor(xo / BW), row, ((xo % BW) + BW) % BW, y % BH]; };
  const joint = (x, y) => { const [, , ix, iy] = cell(x, y); return Math.min(ix, BW - 1 - ix, iy, BH - 1 - iy); };
  img.fill((x, y, u, v) => {
    const [cx, cy, ix, iy] = cell(x, y), j = Math.min(ix, BW - 1 - ix, iy, BH - 1 - iy), hsh = (hash(`${cx % 8},${cy}`) % 1000) / 1000, h2 = (hash(`b${cx % 8},${cy}`) % 1000) / 1000;
    const dirt = Math.max(0, n.fbm(u, v, 4, 3) - 0.5) * grime * 1.4, streak = (n.fbm(u, v, 24, 3, 12) - 0.5) * 0.16;
    if (j < 1.6) return shade(painted ? mix(mortar, painted, 0.7) : mortar, 0.9 + (n(u, v, 256) - 0.5) * 0.2 - dirt * 0.5);
    let c = shade(base, 1 + (hsh - 0.5) * vary * 2 + (n(u, v, 256) - 0.5) * 0.12 + streak);
    if (h2 > 0.9) c = shade(c, 0.72); else if (h2 < 0.07) c = mix(c, [178, 150, 128], 0.4);
    if (painted) c = mix(c, shade(painted, 1 + (hsh - 0.5) * 0.06 + (n(u, v, 256) - 0.5) * 0.06), 0.9 - Math.max(0, n.fbm(u + 0.4, v, 5, 3) - 0.62) * 2);
    return shade(c, 1 - dirt * 0.6 - smooth(3.4, 1.6, j) * 0.18);
  });
  return { color: img, height: (x, y) => -smooth(2.6, 0.6, joint(x, y)) * 3 };
}
// hollow clay blocks, the bare walls of unfinished upper floors
function hollowBrick() {
  const s = 512, n = makeNoise(31), img = new Img(s, s), BW = 128, BH = 64;
  img.fill((x, y, u, v) => {
    const row = Math.floor(y / BH), xo = x + (row % 2) * BW / 2, ix = ((xo % BW) + BW) % BW, iy = y % BH, j = Math.min(ix, BW - 1 - ix, iy, BH - 1 - iy), id = hash(`${Math.floor(xo / BW) % 4},${row}`) % 1000 / 1000;
    if (j < 4) return shade([138, 132, 122], 0.9 + (n(u, v, 256) - 0.5) * 0.3 + (iy < 4 ? 0.1 : 0));
    const groove = smooth(2.5, 0.5, gridLine(iy - 4, 14)) * 0.12;
    return shade([196, 112, 70], 0.92 + (id - 0.5) * 0.24 + (n(u, v, 256) - 0.5) * 0.12 - groove - Math.max(0, n.fbm(u, v, 5, 3) - 0.6) * 0.6);
  });
  return { color: img, height: (x, y) => { const row = Math.floor(y / BH), xo = x + (row % 2) * BW / 2, ix = ((xo % BW) + BW) % BW, iy = y % BH; return -smooth(5, 1, Math.min(ix, BW - 1 - ix, iy, BH - 1 - iy)) * 3; } };
}
// painted render of the hillside houses: colour, stains running down, cracks, patches down to the brick
function plaster(rgb, seed) {
  const s = 512, n = makeNoise(seed), img = new Img(s, s);
  img.fill((x, y, u, v) => {
    const blotch = n.fbm(u, v, 3, 4) - 0.5, run = Math.max(0, n.fbm(u, v, 22, 3, 14) - 0.55) * 0.5, fine = n(u, v, 256) - 0.5;
    let c = shade(rgb, 1 + blotch * 0.2 + fine * 0.06 - run * 0.5);
    const gone = smooth(0.7, 0.74, n.fbm(u + 0.3, v + 0.1, 4, 3)); if (gone > 0) { const row = Math.floor(y / 16), bx = (x + (row % 2) * 32) % 64, j = Math.min(bx, 63 - bx, y % 16, 15 - (y % 16)); c = mix(c, j < 1.5 ? [150, 142, 130] : shade([170, 96, 66], 0.9 + fine * 0.3), gone); }
    const crack = smooth(0.012, 0, Math.abs(n.fbm(u + 0.7, v + 0.4, 6, 3) - 0.5)); return shade(c, 1 - crack * 0.35);
  });
  return { color: img, height: (x, y, u, v) => n(u, v, 128) * 0.7 };
}
function slabs() {
  const s = 512, n = makeNoise(41), img = new Img(s, s), j = (x, y) => Math.min(gridLine(x, s / 2), gridLine(y, s / 2));
  img.fill((x, y, u, v) => { const id = (hash(`${Math.floor(x / 256)},${Math.floor(y / 256)}`) % 100) / 100, gum = n(u + 0.2, v + 0.5, 64) > 0.93 && n(u, v, 128) > 0.6; let c = shade([150, 148, 142], 0.92 + id * 0.14 + (n.fbm(u, v, 5, 4) - 0.5) * 0.22 + (n(u, v, 256) - 0.5) * 0.1 - smooth(3, 0.6, j(x, y)) * 0.4); if (gum) c = shade(c, 0.6); return c; });
  return { color: img, height: (x, y) => -smooth(3.4, 0.6, j(x, y)) * 3 };
}
// the wave mosaic of the Rio promenades: black and white bands of small stones
function mosaic() {
  const s = 1024, n = makeNoise(51), img = new Img(s, s), cell = 12;
  img.fill((x, y, u, v) => {
    const cx = Math.floor(x / cell), cy = Math.floor(y / cell), mx = (cx + 0.5) * cell, my = (cy + 0.5) * cell;
    const band = Math.floor(((my + Math.sin(mx / s * Math.PI * 2 * 2) * 52) / (s / 4)) + 100) % 2, id = (hash(`${cx},${cy}`) % 100) / 100;
    const j = Math.min(x % cell, cell - 1 - (x % cell), y % cell, cell - 1 - (y % cell));
    const stone = band ? shade([44, 44, 48], 0.85 + id * 0.4) : shade([214, 208, 196], 0.86 + id * 0.2);
    return j < 1.2 ? shade([120, 114, 104], 0.9 + (n(u, v, 256) - 0.5) * 0.2) : shade(stone, 1 + (n(u, v, 512) - 0.5) * 0.12 - Math.max(0, n.fbm(u, v, 5, 3) - 0.62) * 0.7);
  });
  return { color: img, height: (x, y) => -smooth(2, 0.4, Math.min(x % cell, cell - 1 - (x % cell), y % cell, cell - 1 - (y % cell))) * 2 };
}
// corrugated sheet / container wall: ribs along v, rust and dirt running down
function corrugated({ base, seed, rib = 24, rust = 0.5, dents = 0 }) {
  const s = 512, n = makeNoise(seed), img = new Img(s, s), wave = (x) => Math.sin((x / rib) * Math.PI * 2);
  img.fill((x, y, u, v) => {
    const w = wave(x), r = Math.max(0, n.fbm(u, v, 14, 3, 10) * 0.6 + n.fbm(u, v, 4, 3) * 0.6 - 0.66) * 3 * rust, d = (n.fbm(u, v, 3, 3) - 0.5) * dents;
    let c = shade(base, 0.86 + w * 0.2 + (n(u, v, 256) - 0.5) * 0.06 + d);
    c = mix(c, shade([124, 66, 34], 0.8 + n(u, v, 128) * 0.4), Math.min(0.85, r));
    return shade(c, 1 - smooth(0.9, 1, v) * 0.25);
  });
  return { color: img, height: (x) => wave(x) * 3 };
}
function planks({ base, seed, w = 64 }) {
  const s = 512, n = makeNoise(seed), img = new Img(s, s);
  img.fill((x, y, u, v) => { const k = Math.floor(x / w), tone = 0.86 + ((k * 37) % 11) / 11 * 0.26; return shade(base, tone * (1 + (n.fbm(u + k * 0.13, v, 90, 3, 12) - 0.5) * 0.3) - smooth(2.4, 0.4, gridLine(x, w)) * 0.4 - Math.max(0, n.fbm(u, v, 5, 3) - 0.6) * 0.5); });
  return { color: img, height: (x) => -smooth(3, 0.5, gridLine(x, w)) * 3 };
}
function chainlink() {
  const s = 256, cell = 32, img = new Img(s, s), alpha = new Img(s, s);
  const d = (x, y) => Math.min(gridLine(x + y, cell), gridLine(x - y, cell)) * 0.7071;
  img.fill((x, y) => shade([150, 154, 158], 0.8 + (((x + y) % 9) / 9) * 0.3));
  alpha.fill((x, y) => { const a = smooth(2.1, 0.9, d(x, y)) * 235; return [a, a, a]; });
  return { color: img, alpha };
}
function net(strength) {
  const s = 128, cell = 32, img = new Img(s, s), alpha = new Img(s, s), d = (x, y) => Math.min(gridLine(x, cell), gridLine(y, cell)), knot = (x, y) => Math.hypot(gridLine(x, cell), gridLine(y, cell));
  img.fill((x, y) => shade([206, 210, 214], knot(x, y) < 3 ? 1.08 : 0.94));
  alpha.fill((x, y) => { const a = Math.max(smooth(1.7, 0.7, d(x, y)), smooth(3.2, 2, knot(x, y))) * strength; return [a, a, a]; });
  return { color: img, alpha };
}
// riveted steel of the elevated line: dark green paint, rivet rows, rust
function steelEl({ girder = false } = {}) {
  const w = girder ? 600 : 256, h = girder ? 240 : 256, n = makeNoise(61), img = new Img(w, h);
  const rivet = (x, y) => { if (girder) { const onFlange = y < 30 || y > h - 31, rx = gridLine(x, 24), ry = Math.min(Math.abs(y - 15), Math.abs(y - (h - 16))); const rib = gridLine(x, w / 4); return Math.min(onFlange ? Math.hypot(rx, ry) : 99, Math.hypot(Math.min(Math.abs(rib - 9), 99), gridLine(y, 26))); } return Math.hypot(gridLine(x, 32), gridLine(y, 32)); };
  img.fill((x, y, u, v) => {
    const r = rivet(x, y), rust = Math.max(0, n.fbm(u, v, 5, 4) * 0.7 + n.fbm(u, v, 20, 3, 12) * 0.4 - 0.62) * 2.4;
    let c = shade([54, 78, 64], 0.86 + (n.fbm(u, v, 4, 3) - 0.5) * 0.3 + (n(u, v, 128) - 0.5) * 0.08);
    if (girder) { const rib = gridLine(x, w / 4); if (rib < 7) c = shade(c, 1.16 - rib * 0.04); if (y < 30 || y > h - 31) c = shade(c, 1.1); else if (y < 34 || y > h - 35) c = shade(c, 0.6); }
    if (r < 3.2) c = shade(c, r < 1.6 ? 1.35 : 0.7);
    return mix(c, shade([120, 62, 32], 0.8 + n(u, v, 128) * 0.4), Math.min(0.8, rust));
  });
  return { color: img };
}
// cut-out pictures: railings, fire escape parts, the dish
function cutout(w, h, solid, rgb = [36, 38, 42]) {
  const img = new Img(w, h), alpha = new Img(w, h);
  img.fill((x, y) => shade(rgb, 0.85 + ((x * 3 + y * 7) % 13) / 13 * 0.3)); alpha.fill((x, y) => (solid(x, y) ? [255, 255, 255] : [0, 0, 0]));
  return { color: img, alpha };
}
// a window: frame, sash bars, and what is behind the glass
function windowPic(kind, seed) {
  const w = 128, h = 192, n = makeNoise(seed), img = new Img(w, h), illum = new Img(w, h), favela = kind.startsWith("fav");
  const frameC = favela ? [[40, 110, 150], [226, 222, 208], [150, 60, 40], [60, 120, 70]][seed % 4] : [[226, 222, 212], [60, 44, 38], [44, 60, 52]][seed % 3];
  const frame = (x, y) => x < 9 || x > w - 10 || y < 9 || y > h - 10 || Math.abs(y - h / 2) < 4 || (!favela && Math.abs(x - w / 2) < 2.5 && y > h / 2);
  const lit = kind.includes("lit");
  img.fill((x, y, u, v) => {
    if (frame(x, y)) return shade(frameC, 0.9 + (n(u, v, 64) - 0.5) * 0.14 - (x < 3 || y < 3 ? 0.2 : 0));
    if (kind.includes("grill") && (gridLine(x, 18) < 1.8 || gridLine(y, 30) < 1.8)) return [30, 30, 34];
    if (kind.includes("blind")) return shade([196, 190, 172], 0.84 + ((y % 10) / 10) * 0.24);
    if (lit) { const warm = kind.endsWith("_b") ? [150, 196, 255] : [255, 206, 128], curtain = x < w * 0.3 || x > w * 0.72 ? 0.62 + Math.sin(x * 0.9) * 0.08 : 1, lamp = Math.exp(-(((u - 0.52) / 0.3) ** 2 + ((v - 0.34) / 0.3) ** 2)); return shade(warm, (0.5 + lamp * 0.55) * curtain); }
    return mix([26, 30, 40], [120, 136, 160], smooth(0.9, 0, v) * 0.5 + (n.fbm(u, v, 3, 2) - 0.5) * 0.2);
  });
  illum.fill((x, y) => (lit && !frame(x, y) ? [255, 255, 255] : [0, 0, 0]));
  return lit ? { color: img, illum } : { color: img };
}
function door(seed) {
  const w = 96, h = 224, n = makeNoise(seed), img = new Img(w, h), c = [[40, 96, 130], [150, 50, 44], [58, 110, 70]][seed % 3];
  img.fill((x, y, u, v) => { const fr = x < 6 || x > w - 7 || y < 6, panel = Math.min(gridLine(x - 14, 34), gridLine(y - 20, 66)) < 2.5; let k = 0.9 + (n.fbm(u, v, 20, 3, 8) - 0.5) * 0.24 - (panel ? 0.2 : 0); if (Math.hypot(x - (w - 18), y - h * 0.52) < 4) return [190, 170, 90]; return fr ? shade([70, 60, 52], k) : shade(c, k - smooth(0.88, 1, v) * 0.3); });
  return { color: img };
}
function awning(rgb) {
  const s = 128, n = makeNoise(71), img = new Img(s, s);
  img.fill((x, y, u, v) => shade(Math.floor(x / 32) % 2 ? rgb : [232, 226, 210], 0.86 + (n.fbm(u, v, 4, 3) - 0.5) * 0.2 - smooth(0.8, 1, v) * 0.2 - Math.max(0, n.fbm(u, v, 12, 3, 8) - 0.6) * 0.5));
  return { color: img };
}
function cloth(rgb, seed) {
  const s = 64, n = makeNoise(seed), img = new Img(s, s);
  img.fill((x, y, u, v) => shade(rgb, 0.84 + Math.sin(u * 9 + seed) * 0.1 + (n.fbm(u, v, 4, 2) - 0.5) * 0.2 + (seed % 2 && Math.floor(y / 8) % 2 ? -0.16 : 0)));
  return { color: img };
}
// the steps' mosaic: small tiles in the colours of Rio's tiled stairway
function stairTiles(seed) {
  // one bold colour leads (red on most steps, yellow and green on every third), small tiles of the others between
  const s = 256, n = makeNoise(seed), img = new Img(s, s), cell = 32, pal = seed % 2 ? [[240, 196, 40], [240, 196, 40], [40, 140, 76], [40, 140, 76], [40, 80, 180], [236, 232, 220]] : [[214, 40, 40], [214, 40, 40], [200, 30, 30], [214, 40, 40], [236, 232, 220], [240, 196, 40]];
  img.fill((x, y, u, v) => { const cx = Math.floor(x / cell), cy = Math.floor(y / cell), j = Math.min(x % cell, cell - 1 - (x % cell), y % cell, cell - 1 - (y % cell)), id = hash(`${seed},${cx},${cy}`); if (j < 1.4) return [222, 218, 206]; return shade(pal[id % pal.length], 0.86 + ((id >> 8) % 100) / 100 * 0.2 + (n(u, v, 128) - 0.5) * 0.1 + (j < 3 ? 0.08 : 0)); });
  return { color: img };
}
function crate(kind) {
  const s = 128, n = makeNoise(81), img = new Img(s, s);
  img.fill((x, y, u, v) => {
    if (kind === "milk") { const hole = gridLine(x, 21) > 5 && gridLine(y, 21) > 5; return shade(hole ? [20, 40, 30] : [230, 120, 40], 0.9 + (n(u, v, 64) - 0.5) * 0.2); }
    const slat = gridLine(y, 32) < 4, fruit = Math.hypot(gridLine(x, 22), gridLine(y - 16, 32) * 1.2) < 9;
    if (slat) return shade([170, 130, 84], 0.9 + (n.fbm(u, v, 40, 2, 8) - 0.5) * 0.3);
    return fruit ? shade(kind === "a" ? [240, 150, 30] : [120, 170, 50], 0.8 + (10 - Math.hypot(gridLine(x, 22), gridLine(y - 16, 32) * 1.2)) * 0.035) : [40, 34, 28];
  });
  return { color: img };
}
function glow(rgb, round = false) {
  const s = 32, img = new Img(s, s).fill(() => rgb);
  if (!round) return { color: img };
  const alpha = new Img(s, s).fill((x, y) => { const a = smooth(15, 9, Math.hypot(x - 15.5, y - 15.5)) * 255; return [a, a, a]; });
  return { color: img, alpha };
}
// car windows as one band: dark glass with a sky reflection, pillars between the panes
function carGlass(panes) {
  const w = 256, h = 64, img = new Img(w, h);
  img.fill((x, y, u, v) => (gridLine(x, w / panes) < 5 || y < 3 || y > h - 4 ? [30, 30, 32] : mix([34, 40, 52], [150, 150, 170], smooth(0.8, 0, v) * 0.6 + (x % (w / panes)) / (w / panes) * 0.15)));
  return { color: img };
}
// the old van's face: orange below with the cream V, split windscreen, round lamps (front) / lid and window (back)
function vanFace(front) {
  const w = 256, h = 272, img = new Img(w, h);
  img.fill((x, y, u, v) => {
    const cream = [236, 226, 200], orange = [222, 118, 44], glass = mix([34, 40, 52], [150, 150, 170], smooth(0.4, 0.05, v));
    if (v < 0.42) { if (front ? (Math.abs(u - 0.5) > 0.03 && Math.abs(u - 0.5) < 0.44 && v > 0.07 && v < 0.36) : (Math.abs(u - 0.5) < 0.3 && v > 0.08 && v < 0.34)) return glass; return cream; }
    if (front) { const vee = Math.abs(u - 0.5) < (0.86 - v) * 0.9; if (Math.hypot((u - 0.5) * w, (v - 0.56) * h) < 20) return [240, 240, 236]; for (const s of [0.16, 0.84]) if (Math.hypot((u - s) * w, (v - 0.66) * h) < 19) return Math.hypot((u - s) * w, (v - 0.66) * h) < 15 ? [255, 244, 210] : [200, 200, 200]; return vee ? cream : orange; }
    if (Math.abs(u - 0.5) < 0.3 && v > 0.5 && v < 0.86) return shade(orange, 0.86 + (Math.abs((v * 20) % 2 - 1) < 0.3 && v > 0.74 ? -0.3 : 0));
    for (const s of [0.1, 0.9]) if (Math.abs(u - s) < 0.05 && Math.abs(v - 0.6) < 0.08) return [220, 40, 30];
    return orange;
  });
  return { color: img };
}
function taxiFace(front) {
  const w = 256, h = 96, img = new Img(w, h);
  img.fill((x, y, u, v) => {
    if (Math.abs(u - 0.5) < 0.3 && v > 0.3 && v < 0.8) return front ? (gridLine(x, 10) < 2 ? [40, 40, 42] : [90, 92, 96]) : [240, 190, 30];
    for (const s of [0.1, 0.9]) if (Math.abs(u - s) < 0.07 && Math.abs(v - 0.5) < 0.2) return front ? [255, 246, 214] : [220, 40, 30];
    if (!front && Math.abs(u - 0.5) < 0.12 && Math.abs(v - 0.55) < 0.14) return [240, 240, 236];
    return shade([240, 190, 30], 1 - v * 0.15);
  });
  return { color: img };
}
// one stretch of a wall with graffiti: the wall's own surface, the pieces sprayed onto it (the
// paint follows the wall's light and dark), tags, dirt at the foot
function graffitiWall({ w, h, base, pieces = [], seed = 1 }) {
  const b = base(), img = new Img(w, h), src = b.color, n = makeNoise(seed);
  img.fill((x, y) => { const c = src.get(x, y); return [c[0], c[1], c[2]]; });
  for (const p of pieces) {
    const raw = art(p.name); if (!raw) { missing.push(p.name); continue; }
    const a = trimmed(raw);
    const pw = Math.round(p.w * w), ph = Math.round(pw * a.h / a.w), px = Math.round(p.x * w), py = Math.round((p.y ?? 0.5) * h - ph / 2);
    for (let y = 0; y < ph; y++) for (let x = 0; x < pw; x++) {
      const X = px + x, Y = py + y; if (X < 0 || Y < 0 || X >= w || Y >= h) continue;
      const s = a.get(Math.floor(x * a.w / pw), Math.floor(y * a.h / ph)), al = (s[3] / 255) * (p.alpha ?? 0.94); if (al <= 0.004) continue;
      const o = (Y * w + X) * 4, under = [img.d[o], img.d[o + 1], img.d[o + 2]], rel = Math.max(0.72, Math.min(1.1, 0.6 + 0.8 * lum(src.get(X, Y))));
      const wear = 1 - Math.max(0, n.fbm(X / w * 3, Y / h, 9, 3) - 0.66) * 2.2;
      for (let k = 0; k < 3; k++) img.d[o + k] = under[k] + (Math.min(255, s[k] * rel) - under[k]) * al * clamp01(wear);
    }
  }
  // dirt creeping up from the ground
  for (let y = Math.floor(h * 0.8); y < h; y++) for (let x = 0; x < w; x++) { const o = (y * w + x) * 4, k = 1 - smooth(0.8, 1, y / h) * (0.25 + 0.2 * n(x / w, 0.5, 64)); img.d[o] *= k; img.d[o + 1] *= k; img.d[o + 2] *= k; }
  return { color: img };
}
// the subway train's sheet (2048 x 1024, rows of 256): 0 the court side with a whole-car piece, 1 the far side,
// 2 = front | roof | underside | bogie. Lit windows and lamps are in the glow mask.
function trainSheet() {
  const w = 2048, h = 1024, n = makeNoise(141), img = new Img(w, h), illum = new Img(w, h);
  const side = (x, y, row) => {
    const u = x / w, v = y / 256, door = gridLine(x - 256, 512) < 46, doorGap = gridLine(x - 256, 512) < 2.5, win = v > 0.2 && v < 0.56 && (door ? gridLine(x - 256, 512) > 8 && gridLine(x - 256, 512) < 40 : gridLine(x - 110, 128) < 46 && !door);
    if (win) { const person = n(u * 3 % 1, 0.3 + row * 0.2, 96) > 0.72 && v > 0.3; return { c: person ? [70, 54, 44] : shade([255, 226, 160], 0.9 + (n(u, v, 64) - 0.5) * 0.1), lit: person ? 90 : 255 }; }
    let c = shade([186, 190, 194], 0.9 + Math.sin(y * 0.9) * (v > 0.6 ? 0.08 : 0.02) + (n.fbm(u * 4 % 1, v, 8, 3) - 0.5) * 0.14);
    if (v > 0.1 && v < 0.16) c = [232, 110, 30]; if (v > 0.9) c = shade([60, 62, 66], 1);
    if (doorGap || (door && Math.abs(gridLine(x - 256, 512) - 44) < 2)) c = shade(c, 0.5);
    const grime = Math.max(0, n.fbm(u * 3 % 1, v, 16, 3, 10) - 0.55) * 0.9; return { c: shade(c, 1 - grime * 0.6 - smooth(0.8, 1, v) * 0.15), lit: 0 };
  };
  img.fill((x, y) => {
    if (y < 512) return side(x, y % 256, Math.floor(y / 256)).c;
    if (y >= 768) return [20, 20, 22];
    const X = x % 512, V = (y - 512) / 256, U = X / 512;
    if (x < 512) { if (V > 0.18 && V < 0.58 && Math.abs(U - 0.5) > 0.04 && Math.abs(U - 0.5) < 0.42) return [30, 34, 42]; for (const s of [0.16, 0.84]) if (Math.hypot((U - s) * 512, (V - 0.76) * 256) < 16) return [255, 244, 210]; if (Math.hypot((U - 0.5) * 512, (V - 0.76) * 256) < 26) return Math.hypot((U - 0.5) * 512, (V - 0.76) * 256) < 22 ? [232, 110, 30] : [240, 240, 236]; return shade([150, 154, 158], 0.9 + (n(U, V, 64) - 0.5) * 0.1 - V * 0.2); }
    if (x < 1024) return shade([120, 124, 128], 0.86 + Math.sin(X * 0.4) * 0.06 + (gridLine(y - 512, 128) < 30 && gridLine(X, 256) < 70 ? -0.2 : 0));
    if (x < 1536) return shade([40, 40, 44], 0.9 + (n(U, V, 64) - 0.5) * 0.2);
    return Math.hypot(gridLine(X - 128, 256), (V - 0.6) * 256) < 60 ? [58, 56, 54] : [30, 30, 34];
  });
  illum.fill((x, y) => { if (y < 512) { const l = side(x, y % 256, Math.floor(y / 256)).lit; return [l, l, l]; } if (y < 768 && x < 512) { const U = x / 512, V = (y - 512) / 256; for (const s of [0.16, 0.84]) if (Math.hypot((U - s) * 512, (V - 0.76) * 256) < 16) return [255, 255, 255]; } return [0, 0, 0]; });
  // whole-car pieces on the court side, a few tags on the other
  const stamp = (name, px, py, pw, al = 0.92) => { const raw = art(name); if (!raw) { missing.push(name); return; } const a = trimmed(raw), ph = Math.round(pw * a.h / a.w); for (let y = 0; y < ph; y++) for (let x = 0; x < pw; x++) { const s = a.get(Math.floor(x * a.w / pw), Math.floor(y * a.h / ph)), k = (s[3] / 255) * al; if (k <= 0.004) continue; const o = ((py + y) * w + px + x) * 4; for (let c = 0; c < 3; c++) img.d[o + c] += (s[c] - img.d[o + c]) * k; illum.d[o] *= 1 - k; illum.d[o + 1] *= 1 - k; illum.d[o + 2] *= 1 - k; } };
  stamp("p_kacrew", 330, 96, 620); stamp("p_gol", 1180, 70, 420); stamp("t_tags_1", 1660, 110, 300); stamp("t_tags_3", 20, 120, 260);
  stamp("t_tags_2", 420, 256 + 120, 280); stamp("t_tags_1", 1300, 256 + 116, 300);
  return { color: img, illum };
}
// a wall surface rendered at an exact pixel size (tiles of a recipe's 512 px picture, `scale` texels per unit)
const tiled = (recipe, w, h, zoom = 1) => () => { const r = recipe(), img = new Img(w, h); img.fill((x, y) => r.color.get(Math.floor(x / zoom), Math.floor(y / zoom))); return { color: img }; };
// the low west wall: grey concrete with a band of weathered colour, 300 x 46 units per stretch
const lowWall = (rgb, seed) => () => { const c = concrete({ base: [150, 148, 142], seed, joints: 0, streaks: 0.7 }), n = makeNoise(seed + 5), img = new Img(2048, 314); img.fill((x, y, u, v) => { const g = c.color.get(x % 512, y % 512), band = v > 0.14 && v < 0.82, wear = smooth(0.5, 0.75, n.fbm(u * 4, v, 8, 3)); return band ? mix(shade(rgb, 0.9 + lum(g) * 0.3), [g[0], g[1], g[2]], wear * 0.8) : [g[0], g[1], g[2]]; }); return { color: img }; };

// the sunset sky, as a lat-long panorama: warm at the horizon in the west, deep blue above, lit clouds
function skyPanorama() {
  const w = 2048, h = 1024, n = makeNoise(91), img = new Img(w, h);
  const sunAz = Math.PI * (1 - 18 / 180), sunEl = 22 * Math.PI / 180;   // the sun stands opposite to the light's travel direction
  img.fill((x, y, u, v) => {
    const az = u * Math.PI * 2, el = (0.5 - v) * Math.PI, e = Math.max(0, el);
    const dx = Math.cos(el) * Math.cos(az), dy = Math.cos(el) * Math.sin(az), dz = Math.sin(el), sx = Math.cos(sunEl) * Math.cos(sunAz), sy = Math.cos(sunEl) * Math.sin(sunAz), sz = Math.sin(sunEl);
    const cosS = dx * sx + dy * sy + dz * sz, toward = (Math.cos(az - sunAz) + 1) / 2, t = Math.pow(1 - Math.min(1, e / (Math.PI / 2)), 2.2);
    let c = mix([44, 78, 150], mix([236, 170, 150], [255, 150, 70], toward), t * (0.55 + 0.45 * toward));
    c = mix(c, [255, 214, 150], Math.pow(Math.max(0, cosS), 10) * 0.75); c = mix(c, [255, 246, 226], Math.pow(Math.max(0, cosS), 220));
    // clouds: streaks low in the sky, lit pink-orange from below
    const cu = u * 6 + n.fbm(u, v, 6, 2) * 0.4, cl = smooth(0.5, 0.74, n.fbm(cu % 1, v * 2.4 % 1, 5, 4, 0.5)) * smooth(0.02, 0.2, e) * smooth(1.2, 0.5, e);
    c = mix(c, mix([120, 96, 120], [255, 176, 120], 0.35 + 0.65 * toward), cl * 0.8);
    if (el < 0) c = mix(c, [70, 60, 66], smooth(0, -0.12, el));
    return c;
  });
  return { color: img };
}

// ---- what stands far away: painted cut-outs (colour + alpha) -------------------------------------------
// a small painter over a colour picture and its mask
function painter(w, h) {
  const color = new Img(w, h), alpha = new Img(w, h);
  const px = (x, y, c, a = 1) => { x = Math.round(x); y = Math.round(y); if (x < 0 || y < 0 || x >= w || y >= h) return; const o = (y * w + x) * 4; if (a >= 1) { color.d[o] = c[0]; color.d[o + 1] = c[1]; color.d[o + 2] = c[2]; } else { color.d[o] += (c[0] - color.d[o]) * a; color.d[o + 1] += (c[1] - color.d[o + 1]) * a; color.d[o + 2] += (c[2] - color.d[o + 2]) * a; } color.d[o + 3] = 255; alpha.d[o] = alpha.d[o + 1] = alpha.d[o + 2] = 255; alpha.d[o + 3] = 255; };
  const rect = (x0, y0, x1, y1, c) => { for (let y = Math.round(y0); y < Math.round(y1); y++) for (let x = Math.round(x0); x < Math.round(x1); x++) px(x, y, typeof c === "function" ? c(x, y) : c); };
  const line = (x0, y0, x1, y1, t, c) => { const n = Math.ceil(Math.hypot(x1 - x0, y1 - y0)) + 1; for (let k = 0; k <= n; k++) { const x = x0 + (x1 - x0) * k / n, y = y0 + (y1 - y0) * k / n; for (let a = -t / 2; a <= t / 2; a++) for (let b = -t / 2; b <= t / 2; b++) px(x + a, y + b, c); } };
  const disc = (cx, cy, rx, ry, c) => { for (let y = Math.floor(cy - ry); y <= cy + ry; y++) for (let x = Math.floor(cx - rx); x <= cx + rx; x++) if (((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1) px(x, y, typeof c === "function" ? c(x, y) : c); };
  const solid = (x, y) => x >= 0 && y >= 0 && x < w && y < h && alpha.d[(Math.round(y) * w + Math.round(x)) * 4] > 0;
  for (let i = 0; i < w * h; i++) { color.d[i * 4 + 3] = 255; alpha.d[i * 4 + 3] = 255; }
  return { color, alpha, px, rect, line, disc, solid, w, h };
}
// where nothing is painted the colour of the nearest painted pixel in the column is kept (no dark fringe at the cut)
function bleed(p, fallback) {
  for (let x = 0; x < p.w; x++) { let last = null; for (let y = p.h - 1; y >= 0; y--) { const o = (y * p.w + x) * 4; if (p.alpha.d[o] > 0) last = [p.color.d[o], p.color.d[o + 1], p.color.d[o + 2]]; else { const c = last ?? fallback; p.color.d[o] = c[0]; p.color.d[o + 1] = c[1]; p.color.d[o + 2] = c[2]; } } }
  return { color: p.color, alpha: p.alpha };
}
// The hill behind the hillside houses, in two halves: slopes full of houses that get smaller with the
// height, trees and rock above them, a statue with open arms on the peak, a round rock further west.
// half: "a" = the east half (world x 0..1700 from right to left in the picture), "b" = the west half.
function farHill(half) {
  const W = 2048, H = 2048, UX = 1700, H0 = -40, H1 = 1400, p = painter(W, H), n = makeNoise(half === "a" ? 201 : 202), g = makeNoise(203);
  const xw = (x) => (half === "a" ? UX - (x + 0.5) / W * UX : -(x + 0.5) / W * UX), toX = (wx) => (half === "a" ? (UX - wx) / UX * W : -wx / UX * W);
  const toY = (hh) => (H1 - hh) / (H1 - H0) * H, sx = W / UX, sy = H / (H1 - H0);
  const bump = (x, c, wdt, hgt) => hgt * Math.exp(-(((x - c) / wdt) ** 2));
  const ridge = (x) => 870 + bump(x, 140, 380, 290) + bump(x, 140, 130, 60) + bump(x, -950, 280, 190) + bump(x, 1250, 500, 70) - smooth(-1250, -1700, x) * 240 + (g.fbm((x + 1700) / 3400, 0.3, 14, 4) - 0.5) * 60;
  // ground: trees and grass, rock where the slope gets steep under the peaks
  for (let x = 0; x < W; x++) {
    const wx = xw(x), top = ridge(wx), yTop = Math.max(0, Math.floor(toY(top)));
    for (let y = yTop; y < H; y++) {
      const hh = H1 - y / sy, u = x / W, v = y / H, under = top - hh, rock = smooth(0.52, 0.7, n.fbm(u, v, 9, 4)) * smooth(860, 1080, hh) + smooth(1100, 1200, hh) * 0.8;
      let c = mix([62, 92, 54], [44, 70, 44], n.fbm(u, v, 40, 3)); c = mix(c, [96, 120, 62], smooth(0.62, 0.8, n.fbm(u, v, 90, 2)) * 0.6);
      c = mix(c, mix([150, 126, 112], [112, 94, 88], n.fbm(u, v * 0.3, 60, 3)), Math.min(1, rock));
      c = mix(c, [255, 190, 130], smooth(26, 0, under) * 0.35);                    // the low sun catches the crest
      p.px(x, y, c);
    }
  }
  // houses, row by row from the top down (the lower ones stand in front)
  const pal = [[232, 190, 70], [60, 170, 170], [226, 124, 140], [232, 130, 60], [96, 168, 96], [226, 222, 208], [86, 140, 210], [176, 104, 78], [176, 104, 78]];
  let seed = half === "a" ? 77 : 78; const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
  const rows = []; for (let hh = 1010; hh > 300; hh -= 46 + (1010 - hh) * 0.035) rows.push(hh);
  for (const base of rows) {
    const s = 0.34 + (1010 - base) / 710 * 0.6;                                    // size: small near the top
    let wx = half === "a" ? -30 : -UX - 30; const end = half === "a" ? UX + 30 : 30;
    while (wx < end) {
      const wd = (96 + rnd() * 90) * s, floors = 1 + (rnd() < 0.6 ? 1 : 0) + (rnd() < 0.2 ? 1 : 0), ht = (floors * 84 + 12) * s, gap = rnd() < 0.2 ? wd * (0.3 + rnd()) : 2 * s, b0 = base + (rnd() - 0.5) * 30 * s;
      const cx = wx + wd / 2, limit = ridge(cx) - 120 - 170 * smooth(420, 60, Math.abs(cx - 140)) - 90 * smooth(260, 40, Math.abs(cx + 950));  // none on the peak's rock
      if (b0 + ht < limit && rnd() < 0.93) {
        const c = pal[Math.floor(rnd() * pal.length)], bare = c[0] === 176, xa = toX(wx + wd), xb = toX(wx), x0 = Math.min(xa, xb), x1 = Math.max(xa, xb), y1 = toY(b0), y0 = toY(b0 + ht), k = 0.86 + rnd() * 0.2;
        p.rect(x0, y0, x1, y1, (x, y) => { const side = x > x1 - (x1 - x0) * 0.16, brick = bare && (Math.floor(y / 3) % 2 === 0 || (x + Math.floor(y / 6) * 3) % 7 === 0); return shade(c, k * (side ? 0.62 : 1) * (brick ? 0.82 : 1) * (0.95 + (n(x / W, y / H, 300) - 0.5) * 0.16) * (1 - smooth(y1 - 5 * s, y1, y) * 0.25)); });
        p.rect(x0 - 1, y0 - 2.5 * s - 1, x1 + 1, y0, rnd() < 0.5 ? [168, 164, 156] : [120, 104, 92]);       // roof slab / tin
        const cols = Math.max(1, Math.round(wd / (70 * s))), ww = 24 * s * sx, wh = 30 * s * sy;
        for (let f = 0; f < floors; f++) for (let q = 0; q < cols; q++) {
          const wxc = x0 + (x1 - x0) * 0.84 * (q + 0.5) / cols, wyc = toY(b0 + (f * 84 + 54) * s), lit = rnd() < 0.36, door = f === 0 && q === 0 && rnd() < 0.7;
          if (door) p.rect(wxc - ww * 0.45, toY(b0 + 66 * s), wxc + ww * 0.45, y1, [[120, 44, 40], [40, 80, 110], [60, 50, 44]][Math.floor(rnd() * 3)]);
          else { p.rect(wxc - ww / 2 - 1, wyc - wh / 2 - 1, wxc + ww / 2 + 1, wyc + wh / 2 + 1, shade(c, 0.6)); p.rect(wxc - ww / 2, wyc - wh / 2, wxc + ww / 2, wyc + wh / 2, lit ? (rnd() < 0.75 ? [255, 214, 140] : [170, 210, 255]) : [34, 38, 50]); }
        }
        if (rnd() < 0.3) { const tx = x0 + (x1 - x0) * (0.3 + rnd() * 0.4), tw = 13 * s * sx, th = 20 * s * sy; p.rect(tx - tw, y0 - 2.5 * s - th, tx + tw, y0 - 2.5 * s, (x) => shade([44, 104, 180], 0.75 + ((x - tx + tw) / (2 * tw)) * 0.4)); }
      }
      wx += wd + gap;
    }
  }
  // trees in front of the houses here and there
  for (let k = 0; k < 150; k++) { const wx = (half === "a" ? 0 : -UX) + rnd() * UX, hh = 330 + rnd() * 760, r = (16 + rnd() * 20) * (0.5 + (1010 - Math.min(1010, hh)) / 1400); if (hh > ridge(wx) - 40) continue; const cx = toX(wx), cy = toY(hh); p.disc(cx, cy, r * sx, r * sy * 0.8, (x, y) => mix([40, 74, 44], [86, 124, 60], clamp01(0.5 - (y - cy) / (r * sy) * 0.6 + (x - cx) / (r * sx) * -0.3))); }
  // the statue with open arms on the peak (the east half only), a mast with a red light on the round rock
  if (half === "a") { const cx = toX(140), top = ridge(140), u = sx * 1.4, v = sy * 1.4, st = [232, 226, 214], sh = [176, 170, 168], y0 = toY(top - 6);
    p.rect(cx - 9 * u, y0 - 26 * v, cx + 9 * u, y0, sh); p.rect(cx - 6 * u, y0 - 26 * v, cx + 3 * u, y0, st);                   // pedestal
    p.rect(cx - 5 * u, y0 - 78 * v, cx + 5 * u, y0 - 26 * v, (x) => (x > cx + 1.5 * u ? sh : st));                              // robe
    p.rect(cx - 29 * u, y0 - 72 * v, cx + 29 * u, y0 - 63 * v, (x) => (x > cx + 5 * u ? sh : st));                              // arms
    p.disc(cx, y0 - 84 * v, 4.4 * u, 6 * v, st); }
  else { const cx = toX(-950), y0 = toY(ridge(-950) - 4); p.line(cx, y0, cx, y0 - 70 * sy, 2, [70, 70, 76]); p.line(cx - 12, y0 - 40 * sy, cx + 12, y0 - 40 * sy, 2, [70, 70, 76]); p.disc(cx, y0 - 72 * sy, 3.5, 3.5, [255, 60, 40]); }
  // evening haze: the further up the slope, the further away
  for (let i = 0; i < W * H; i++) { const y = Math.floor(i / W), t = 0.1 + 0.22 * (1 - y / H); for (let c = 0; c < 3; c++) p.color.d[i * 4 + c] += ([214, 150, 132][c] - p.color.d[i * 4 + c]) * t; }
  return bleed(p, [90, 100, 80]);
}
// The bay in the north, behind the colonial street: a steep round rock, a lower broad hill beside it with a
// cable car between the two, far ridges - lit from the left by the low sun, the right flanks in shade.
function farBay() {
  const W = 2048, H = 1024, X0 = -1700, X1 = 700, H0 = -40, H1 = 1400, p = painter(W, H), n = makeNoise(301);
  const toX = (wx) => (wx - X0) / (X1 - X0) * W, toY = (hh) => (H1 - hh) / (H1 - H0) * H, wxOf = (x) => X0 + (x + 0.5) / W * (X1 - X0), hOf = (y) => H1 - (y + 0.5) / H * (H1 - H0);
  const dome = (x, c, half, top, base, pow) => { const d = Math.abs(x - c) / half; return d >= 1 ? 0 : base + (top - base) * Math.pow(1 - Math.pow(d, pow), 0.5); };
  const ridge = (x) => 560 + (n.fbm((x - X0) / 2400, 0.2, 6, 4) - 0.5) * 260 + Math.exp(-(((x - 380) / 420) ** 2)) * 180;
  const rock = (x) => dome(x, -640, x < -640 ? 210 : 300, 1250, 420, x < -640 ? 2.2 : 3.2);          // the round rock: steeper on the left
  const urca = (x) => dome(x, -130, 380, 830, 380, 2.4);
  for (let x = 0; x < W; x++) {
    const wx = wxOf(x), hr = ridge(wx), hk = rock(wx), hu = urca(wx), top = Math.max(hr, hk, hu);
    for (let y = Math.max(0, Math.floor(toY(top))); y < H; y++) {
      const hh = hOf(y), u = x / W, v = y / H; let c;
      if (hh < hk || hh < hu) {
        const mine = hh < hk ? [hk, -640, 250] : [hu, -130, 380], side = clamp01(0.5 + (wx - mine[1]) / mine[2] * 0.7), under = mine[0] - hh;
        c = mix([176, 126, 110], [74, 60, 88], smooth(0.25, 0.8, side + (n.fbm(u, v, 8, 3) - 0.5) * 0.3));       // lit left, shaded right
        c = mix(c, shade(c, 0.8), smooth(0.5, 0.7, n.fbm(u, v * 0.25, 70, 3)) * 0.7);                              // streaks down the rock
        c = mix(c, mix([70, 96, 60], [44, 62, 56], side), smooth(0.45, 0.62, n.fbm(u, v, 14, 4)) * smooth(900, 420, hh) + smooth(40, 0, under) * 0.5);   // trees low down and on the top
        c = mix(c, [255, 196, 140], smooth(14, 0, under) * (1 - side) * 0.8);
      } else c = mix([128, 104, 132], [96, 84, 118], n.fbm(u, v, 10, 3));
      p.px(x, y, c);
    }
  }
  // the cable car: two ropes from the low hill's top to the rock's top, a cabin on its way
  const a = [toX(-150), toY(826)], b = [toX(-600), toY(1236)];
  for (const o of [0, 5]) p.line(a[0], a[1] + o, b[0], b[1] + o, 1.6, [40, 36, 48]);
  { const t = 0.42, cx = a[0] + (b[0] - a[0]) * t, cy = a[1] + (b[1] - a[1]) * t + 6; p.line(cx, cy, cx, cy + 9, 1.5, [40, 36, 48]); p.rect(cx - 13, cy + 9, cx + 13, cy + 25, [226, 60, 50]); p.rect(cx - 10, cy + 12, cx + 10, cy + 19, [150, 200, 230]); }
  for (const [wx, hh] of [[-150, 826], [-600, 1236]]) p.rect(toX(wx) - 12, toY(hh) - 10, toX(wx) + 12, toY(hh) + 4, [220, 214, 200]);
  // lights along the foot of the hills
  let seed = 311; const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
  for (let k = 0; k < 420; k++) { const x = rnd() * W, hh = 380 + rnd() * 190; if (hh < Math.max(rock(wxOf(x)), urca(wxOf(x)), ridge(wxOf(x))) - 20) p.rect(x, toY(hh), x + 3, toY(hh) + 3, rnd() < 0.8 ? [255, 214, 140] : [190, 220, 255]); }
  for (let i = 0; i < W * H; i++) { const y = Math.floor(i / W), t = 0.06 + 0.12 * (y / H); for (let c = 0; c < 3; c++) p.color.d[i * 4 + c] += ([226, 150, 136][c] - p.color.d[i * 4 + c]) * t; }
  return bleed(p, [120, 100, 128]);
}
// rooftops in the west, against the sun: dark, with a warm rim on top - water towers, chimneys, aerials, palms
function farRoofsOld() {
  const W = 2048, H = 512, p = painter(W, H), dark = [58, 46, 62], rim = [255, 176, 110]; let seed = 505; const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
  const block = (x0, x1, y0) => { p.rect(x0, y0, x1, H, (x, y) => shade(dark, 0.9 + ((x * 3 + y * 5) % 11) * 0.02)); p.rect(x0, y0, x1, y0 + 2, rim); for (let y = y0 + 12; y < H - 6; y += 16) for (let x = x0 + 6; x < x1 - 8; x += 12) if (rnd() < 0.16) p.rect(x, y, x + 4, y + 7, [255, 206, 130]); };
  let x = 0;
  while (x < W) {
    const wd = 70 + rnd() * 150, y0 = H * (0.3 + rnd() * 0.36), x1 = Math.min(W, x + wd), r = rnd(), mx = x + wd * (0.25 + rnd() * 0.5);
    block(x, x1, y0);
    if (r < 0.1) { p.rect(mx - 13, y0 - 74, mx + 13, y0, dark); for (let k = 0; k < 26; k++) p.rect(mx - 13 + k * 0.5, y0 - 75 - k, mx + 13 - k * 0.5, y0 - 74 - k, dark); p.line(mx, y0 - 100, mx, y0 - 118, 2, dark); p.line(mx - 6, y0 - 111, mx + 6, y0 - 111, 2, dark); p.rect(mx - 13, y0 - 74, mx - 11, y0, rim); }   // a church tower
    else if (r < 0.24) { p.rect(mx - 12, y0 - 20, mx + 12, y0, dark); p.rect(mx - 12, y0 - 20, mx + 12, y0 - 18, rim); }   // a water tank
    else if (r < 0.42) { p.rect(mx - 6, y0 - 30, mx + 6, y0, dark); p.rect(mx - 6, y0 - 30, mx + 6, y0 - 28, rim); }                                                                                                                                                               // chimney
    else if (r < 0.6) { p.line(mx, y0, mx, y0 - 58, 2, dark); for (const k of [20, 32, 44]) p.line(mx - 12 + k / 5, y0 - k, mx + 12 - k / 5, y0 - k, 1.5, dark); }                                                                                                                     // aerial
    else if (r < 0.8) { const top = y0 - 70 - rnd() * 40, lean = (rnd() - 0.5) * 20; p.line(mx, y0, mx + lean, top, 4, dark); for (let k = 0; k < 9; k++) { const a = Math.PI * (0.05 + k / 8 * 0.9), len = 34 + rnd() * 12; for (let q = 0; q <= 10; q++) { const t = q / 10; p.line(mx + lean + Math.cos(a) * len * t, top - Math.sin(a) * len * t * 0.7 + t * t * 26, mx + lean + Math.cos(a) * len * (t + 0.1), top - Math.sin(a) * len * (t + 0.1) * 0.7 + (t + 0.1) ** 2 * 26, 2.5 - t * 1.5, dark); } } }   // palm
    x = x1 + (rnd() < 0.15 ? 30 + rnd() * 50 : 0);
  }
  p.rect(0, H * 0.72, W, H, (x2, y) => shade(dark, 0.9 + ((x2 * 3 + y * 5) % 11) * 0.02));
  return bleed(p, dark);
}
// the backdrop in the west, against the sun: a dark hill ridge with palms and a church tower on it, the lights
// of houses on its slope (owner: no skyscrapers - the blocks with windows that stood here read as a skyline)
function farRoofs() {
  const W = 2048, H = 512, p = painter(W, H), n = makeNoise(506), dark = [50, 40, 52], rim = [255, 170, 104]; let seed = 507; const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
  const top = (x) => H * (0.5 - (n.fbm(x / W, 0.4, 5, 4) - 0.5) * 0.44 - Math.exp(-(((x / W - 0.72) / 0.12) ** 2)) * 0.12);
  for (let x = 0; x < W; x++) { const y0 = Math.floor(top(x)); for (let y = y0; y < H; y++) p.px(x, y, y - y0 < 3 ? mix(dark, rim, 0.8 - (y - y0) * 0.25) : shade(dark, 0.86 + n(x / W, y / H, 200) * 0.28)); }
  for (let k = 0; k < 26; k++) {                                                        // palms on the crest
    const mx = 30 + rnd() * (W - 60), y0 = top(mx) + 3, tp = y0 - 46 - rnd() * 44, lean = (rnd() - 0.5) * 22;
    p.line(mx, y0, mx + lean, tp, 3.4, dark);
    for (let f = 0; f < 9; f++) { const a = Math.PI * (0.04 + f / 8 * 0.92), len = 30 + rnd() * 12; for (let q = 0; q < 10; q++) { const t = q / 10; p.line(mx + lean + Math.cos(a) * len * t, tp - Math.sin(a) * len * t * 0.7 + t * t * 24, mx + lean + Math.cos(a) * len * (t + 0.1), tp - Math.sin(a) * len * (t + 0.1) * 0.7 + (t + 0.1) ** 2 * 24, 2.4 - t * 1.4, dark); } }
  }
  for (const mx of [0.2 * W, 0.71 * W]) { const y0 = top(mx) + 2; p.rect(mx - 11, y0 - 60, mx + 11, y0, dark); for (let k = 0; k < 22; k++) p.rect(mx - 11 + k * 0.5, y0 - 61 - k, mx + 11 - k * 0.5, y0 - 60 - k, dark); p.line(mx, y0 - 82, mx, y0 - 98, 2, dark); p.line(mx - 5, y0 - 92, mx + 5, y0 - 92, 2, dark); p.rect(mx - 11, y0 - 60, mx - 9, y0, rim); }
  for (let k = 0; k < 520; k++) { const x = rnd() * W, y = top(x) + 14 + rnd() * rnd() * (H * 0.5); if (y < H - 4) p.rect(x, y, x + 3, y + 3, rnd() < 0.85 ? [255, 206, 130] : [200, 226, 255]); }   // house lights
  return bleed(p, dark);
}
// a pair of sneakers hanging from their laces
function sneakers() {
  const s = 64, p = painter(s, s);
  p.line(32, 0, 20, 30, 1.5, [236, 232, 220]); p.line(32, 0, 45, 36, 1.5, [236, 232, 220]);
  for (const [cx, cy, c] of [[20, 44, [214, 44, 40]], [45, 50, [214, 44, 40]]]) { p.rect(cx - 10, cy - 14, cx + 1, cy - 4, c); p.rect(cx - 10, cy - 5, cx + 12, cy + 6, c); p.rect(cx - 11, cy + 6, cx + 13, cy + 9, [240, 238, 230]); p.rect(cx + 2, cy - 4, cx + 9, cy - 2, [240, 238, 230]); }
  return bleed(p, [214, 44, 40]);
}

// ---- materials ----------------------------------------------------------------------------------------
const P = { yellow: [232, 190, 70], teal: [60, 170, 170], pink: [226, 124, 140], orange: [232, 130, 60], green: [96, 168, 96], white: [226, 222, 208], blue: [86, 140, 210], coral: [226, 108, 92], ochre: [214, 164, 76] };
const decalMat = { rough: 0.7, translucent: true, noShadow: true };
const MATERIALS = {
  collide: { make: () => flat([255, 0, 255]), rough: 1 },
  // ---- ground
  asphalt: { make: () => asphalt({ base: [112, 108, 104], seed: 11, patches: 0.6 }), rough: 0.9, normal: 0.8, noShadow: true, surface: "Concrete" },
  road: { make: () => asphalt({ base: [62, 62, 66], seed: 12, patches: 0.9 }), rough: 0.9, normal: 0.8 },
  road_old: { make: () => asphalt({ base: [74, 72, 70], seed: 13, patches: 1.2 }), rough: 0.92, normal: 0.9 },
  ground_dirt: { make: () => asphalt({ base: [124, 104, 84], seed: 14, patches: 1 }), rough: 0.95, normal: 0.8 },
  sidewalk: { make: slabs, rough: 0.9, normal: 1 },
  mosaic: { make: mosaic, rough: 0.6, normal: 0.8 },
  curb: { make: () => concrete({ base: [168, 164, 154], seed: 15, streaks: 0.5 }), rough: 0.9 },
  line_white: { make: () => courtPaint([232, 230, 222], 21), rough: 0.8, noShadow: true },
  // the court's painting (one faded sprayed picture over the whole court) and the abstract pieces on it
  g_court: { make: courtArt, rough: 0.8, translucent: true, noShadow: true },
  ...Object.fromEntries(["a_arrows", "a_bubbles", "a_swoosh", "a_splat", "a_scribble", "a_wild", "g_tags_a", "g_tags_b"].map((k) => [k, { make: () => picture(k, { alpha: true, faded: 0.8 }), rough: 0.7, translucent: true, noShadow: true }])),
  paint_sand: { make: () => courtPaint([226, 190, 110], 26), rough: 0.8, noShadow: true },
  road_dash: { make: () => courtPaint([226, 200, 80], 24), rough: 0.8, noShadow: true },
  road_zebra: { make: () => courtPaint([226, 224, 214], 25), rough: 0.8, noShadow: true },
  // ---- walls of the cage
  concrete: { make: () => concrete({ base: [146, 144, 138], seed: 31, joints: 2, jointsV: 1, ties: true, streaks: 0.9 }), rough: 0.9, normal: 1.2 },
  concrete_old: { make: () => concrete({ base: [128, 122, 112], seed: 32, joints: 3, streaks: 1.2, stain: 1.4 }), rough: 0.92, normal: 1.2 },
  concrete_cap: { make: () => concrete({ base: [170, 168, 160], seed: 33, streaks: 0.4 }), rough: 0.88 },
  chainlink: { make: chainlink, rough: 0.5, metal: 0.4, translucent: true, twoSided: true, noShadow: true },
  steel_fence: { make: () => steel([128, 132, 136], 34), rough: 0.5, metal: 0.4 },
  net_top: { make: () => net(26), rough: 0.9, translucent: true, twoSided: true, noShadow: true },
  container_red: { make: () => corrugated({ base: [170, 44, 38], seed: 35, rust: 0.7, dents: 0.1 }), rough: 0.6, metal: 0.2, normal: 1 },
  container_blue: { make: () => corrugated({ base: [38, 82, 150], seed: 36, rust: 0.7, dents: 0.1 }), rough: 0.6, metal: 0.2, normal: 1 },
  container_in: { make: () => corrugated({ base: [70, 72, 76], seed: 37, rust: 0.4 }), rough: 0.7, metal: 0.2, normal: 1 },
  container_floor: { make: () => planks({ base: [120, 92, 64], seed: 38 }), rough: 0.8, normal: 0.8, surface: "Concrete" },
  // ---- houses and shops
  brick_red: { make: () => brick({ base: [150, 70, 54], seed: 41 }), rough: 0.9, normal: 1.2 },
  brick_brown: { make: () => brick({ base: [122, 82, 62], seed: 42, vary: 0.26 }), rough: 0.9, normal: 1.2 },
  brick_dark: { make: () => brick({ base: [96, 62, 54], seed: 43, mortar: [120, 114, 106] }), rough: 0.9, normal: 1.2 },
  brick_white: { make: () => brick({ base: [150, 70, 54], seed: 44, painted: [226, 220, 204] }), rough: 0.85, normal: 1.2 },
  brick_hollow: { make: hollowBrick, rough: 0.95, normal: 1.2 },
  cornice: { make: () => concrete({ base: [186, 178, 162], seed: 45, joints: 4, streaks: 1 }), rough: 0.85 },
  stoop: { make: () => concrete({ base: [128, 92, 74], seed: 46, streaks: 0.8 }), rough: 0.9 },
  roof_tar: { make: () => asphalt({ base: [50, 50, 54], seed: 47, patches: 0.4, size: 512 }), rough: 0.95 },
  roof_slab: { make: () => concrete({ base: [150, 146, 138], seed: 48, stain: 1.4 }), rough: 0.92 },
  roof_tin: { make: () => corrugated({ base: [150, 150, 148], seed: 49, rust: 1.3, rib: 16 }), rough: 0.6, metal: 0.4, normal: 1 },
  // ---- the colonial street and the church, palms
  cobble: { make: cobble, rough: 0.85, normal: 1.2 }, roof_tile: { make: roofTile, rough: 0.8 },
  trim_white: { make: () => concrete({ base: [234, 230, 218], seed: 143, streaks: 0.6 }), rough: 0.85 }, trim_stone: { make: () => concrete({ base: [104, 98, 92], seed: 144, streaks: 0.6 }), rough: 0.9 },
  win_col_a: { make: () => archPic("win_col_a", 0), rough: 0.4, alphaTest: 0.5 }, win_col_b: { make: () => archPic("win_col_b", 1), rough: 0.6, alphaTest: 0.5 }, win_col_lit: { make: () => archPic("win_col_lit", 2), rough: 0.4, alphaTest: 0.5, illum: 1.1, illumAll: true },
  door_col_a: { make: () => archPic("door_col_a", 3), rough: 0.6, alphaTest: 0.5 }, door_col_b: { make: () => archPic("door_col_b", 4), rough: 0.6, alphaTest: 0.5 }, door_church: { make: () => archPic("door_church", 5), rough: 0.6, alphaTest: 0.5 },
  win_bell: { make: () => archPic("win_bell", 6), rough: 0.7, alphaTest: 0.5 }, win_rose: { make: roseWindow, rough: 0.4, alphaTest: 0.5, illum: 1.2, illumAll: true },
  cross: { make: () => cutout(64, 128, (x, y) => Math.abs(x - 31.5) < 6 || (Math.abs(y - 40) < 6 && Math.abs(x - 31.5) < 26), [40, 36, 34]), rough: 0.6, alphaTest: 0.5, twoSided: true, noShadow: true },
  palm_trunk: { make: palmTrunk, rough: 0.9 }, palm_frond: { make: palmFrond, rough: 0.7, alphaTest: 0.5, twoSided: true, noShadow: true },
  // the flags, their strings and the kites (one sheet for the moving canopy model)
  canopy: { make: canopySheet, rough: 0.8, alphaTest: 0.5, twoSided: true, noShadow: true, illum: 0.35, illumAll: true },
  ...Object.fromEntries(Object.entries(P).map(([k, c], i) => [`plaster_${k}`, { make: () => plaster(c, 50 + i), rough: 0.9, normal: 0.8 }])),
  win_lit: { make: () => windowPic("lit", 0), rough: 0.4, illum: 1.6 },
  win_lit_b: { make: () => windowPic("lit_b", 1), rough: 0.4, illum: 1.2 },
  win_dark: { make: () => windowPic("dark", 2), rough: 0.15 },
  win_blind: { make: () => windowPic("blind", 3), rough: 0.6 },
  win_fav_lit: { make: () => windowPic("fav_lit", 4), rough: 0.4, illum: 1.6 },
  win_fav_lit_b: { make: () => windowPic("fav_lit_b", 5), rough: 0.4, illum: 1.2 },
  win_fav_dark: { make: () => windowPic("fav_dark", 6), rough: 0.2 },
  win_fav_grill: { make: () => windowPic("fav_grill", 7), rough: 0.3 },
  door_fav_a: { make: () => door(0), rough: 0.6 }, door_fav_b: { make: () => door(1), rough: 0.6 }, door_fav_c: { make: () => door(2), rough: 0.6 },
  awning_green: { make: () => awning([46, 120, 70]), rough: 0.8, twoSided: true },
  awning_red: { make: () => awning([190, 50, 44]), rough: 0.8, twoSided: true },
  awning_yellow: { make: () => awning([232, 180, 40]), rough: 0.8, twoSided: true },
  // ---- the elevated line
  steel_el: { make: () => steelEl(), rough: 0.6, metal: 0.4 },
  girder_el: { make: () => steelEl({ girder: true }), rough: 0.6, metal: 0.4 },
  deck_el: { make: () => planks({ base: [70, 58, 48], seed: 62, w: 32 }), rough: 0.9 },
  track_bed: { make: () => planks({ base: [92, 76, 60], seed: 63, w: 32 }), rough: 0.9 },
  rail: { make: () => steel([150, 146, 140], 64), rough: 0.35, metal: 0.6 },
  railing_el: { make: () => cutout(256, 128, (x, y) => y < 8 || y > 119 || Math.abs(y - 64) < 3 || gridLine(x, 128) < 5 || Math.abs(((x % 128) / 128) * 128 - y) < 3 || Math.abs(128 - ((x % 128) / 128) * 128 - y) < 3, [50, 72, 60]), rough: 0.6, metal: 0.3, alphaTest: 0.5, twoSided: true, noShadow: true },
  fire_rail: { make: () => cutout(256, 128, (x, y) => y < 8 || y > 120 || gridLine(x, 26) < 3), rough: 0.6, metal: 0.3, alphaTest: 0.5, twoSided: true, noShadow: true },
  fire_ladder: { make: () => cutout(64, 256, (x, y) => x < 7 || x > 56 || gridLine(y, 26) < 3.5), rough: 0.6, metal: 0.3, alphaTest: 0.5, twoSided: true, noShadow: true },
  dish: { make: () => cutout(64, 64, (x, y) => Math.hypot(x - 32, (y - 30) * 1.15) < 27, [200, 200, 196]), rough: 0.5, alphaTest: 0.5, twoSided: true, noShadow: true },
  globe_green: { make: () => glow([150, 200, 255]), rough: 0.3, illum: 3, illumAll: true, noShadow: true },   // the metro's lamps (blue-white)
  portal_dark: { make: () => { const img = new Img(64, 64); img.fill((x, y, u, v) => shade([14, 14, 16], 1 + smooth(0.5, 1, v) * 1.2)); return { color: img }; }, rough: 1, noShadow: true },
  // ---- lights
  lamp_head: { make: () => steel([40, 42, 46], 65), rough: 0.5, metal: 0.3 },
  lamp_glow: { make: () => glow([255, 214, 150]), rough: 0.3, illum: 6, illumAll: true, noShadow: true },
  bulb: { make: () => glow([255, 206, 130], true), rough: 0.3, illum: 5, illumAll: true, alphaTest: 0.4, twoSided: true, noShadow: true },
  cable: { make: () => flat([22, 22, 24]), rough: 0.8, twoSided: true, noShadow: true },
  // ---- things in the street
  steel_dark: { make: () => steel([44, 46, 50], 66), rough: 0.55, metal: 0.3 },
  tank_blue: { make: () => corrugated({ base: [40, 100, 176], seed: 67, rust: 0.1, rib: 40 }), rough: 0.5 },
  tank_wood: { make: () => planks({ base: [126, 92, 62], seed: 68, w: 32 }), rough: 0.85, normal: 0.8 },
  tank_roof: { make: () => steel([62, 54, 50], 69), rough: 0.7 },
  hydrant: { make: () => steel([190, 44, 36], 70), rough: 0.5, metal: 0.1 },
  crate_fruit_a: { make: () => crate("a"), rough: 0.8 }, crate_fruit_b: { make: () => crate("b"), rough: 0.8 }, crate_milk: { make: () => crate("milk"), rough: 0.6 },
  wood_dark: { make: () => planks({ base: [96, 70, 50], seed: 72 }), rough: 0.8 },
  wood_pole: { make: () => planks({ base: [92, 76, 62], seed: 73, w: 128 }), rough: 0.9 },
  pallet: { make: () => planks({ base: [176, 146, 104], seed: 74, w: 64 }), rough: 0.9 },
  dumpster: { make: () => corrugated({ base: [44, 96, 70], seed: 75, rust: 0.9, rib: 64 }), rough: 0.6, metal: 0.2 },
  dumpster_lid: { make: () => steel([34, 36, 38], 76), rough: 0.6 },
  barrel: { make: () => corrugated({ base: [196, 110, 40], seed: 77, rust: 1.2, rib: 512 }), rough: 0.6, metal: 0.3 },
  barrel_blue: { make: () => corrugated({ base: [44, 96, 170], seed: 78, rust: 0.6, rib: 512 }), rough: 0.6, metal: 0.2 },
  tyre: { make: () => steel([26, 26, 28], 79), rough: 0.95 },
  bin: { make: () => corrugated({ base: [150, 154, 156], seed: 80, rust: 0.4, rib: 20 }), rough: 0.5, metal: 0.5 },
  bin_lid: { make: () => steel([130, 134, 136], 81), rough: 0.5, metal: 0.5 },
  ac_unit: { make: () => corrugated({ base: [190, 188, 178], seed: 82, rust: 0.5, rib: 8 }), rough: 0.6 },
  transformer: { make: () => steel([120, 124, 126], 83), rough: 0.6, metal: 0.3 },
  chair_plastic: { make: () => flat([228, 226, 218]), rough: 0.5 },
  cloth_a: { make: () => cloth([230, 226, 214], 1), rough: 0.9, twoSided: true }, cloth_b: { make: () => cloth([214, 80, 60], 2), rough: 0.9, twoSided: true },
  cloth_c: { make: () => cloth([240, 196, 60], 3), rough: 0.9, twoSided: true }, cloth_d: { make: () => cloth([70, 150, 160], 4), rough: 0.9, twoSided: true },
  stair_tiles: { make: () => stairTiles(0), rough: 0.35 }, stair_tiles_b: { make: () => stairTiles(1), rough: 0.35, twoSided: true },
  stair_tread: { make: () => concrete({ base: [176, 96, 80], seed: 84, streaks: 0.5 }), rough: 0.7 },
  train: { make: trainSheet, rough: 0.45, metal: 0.4, illum: 1.6, twoSided: true },
  // ---- vehicles
  van_low: { make: () => steel([222, 118, 44], 131), rough: 0.4, metal: 0.2 }, van_up: { make: () => steel([236, 226, 200], 132), rough: 0.4, metal: 0.2 },
  van_glass: { make: () => carGlass(4), rough: 0.15 }, van_front: { make: () => vanFace(true), rough: 0.4 }, van_back: { make: () => vanFace(false), rough: 0.4 },
  taxi_yellow: { make: () => steel([240, 190, 30], 133), rough: 0.35, metal: 0.2 }, taxi_glass: { make: () => carGlass(2), rough: 0.15 },
  taxi_front: { make: () => taxiFace(true), rough: 0.4 }, taxi_back: { make: () => taxiFace(false), rough: 0.4 },
  taxi_checker: { make: () => { const img = new Img(32, 16); img.fill((x, y) => (y > 2 && y < 13 ? [30, 70, 170] : [240, 240, 236])); return { color: img }; }, rough: 0.5 },   // a blue band along the yellow cab
  taxi_sign: { make: () => glow([255, 236, 150]), rough: 0.3, illum: 3, illumAll: true, noShadow: true },
  car_dark: { make: () => steel([28, 28, 30], 134), rough: 0.7 },
  wheel: { make: () => { const s = 64, img = new Img(s, s), alpha = new Img(s, s); img.fill((x, y) => { const r = Math.hypot(x - 31.5, y - 31.5); return r < 12 ? shade([190, 190, 186], 1 - r * 0.02) : r < 15 ? [60, 60, 62] : [24, 24, 26]; }); alpha.fill((x, y) => (Math.hypot(x - 31.5, y - 31.5) < 31 ? [255, 255, 255] : [0, 0, 0])); return { color: img, alpha }; }, rough: 0.8, alphaTest: 0.5, twoSided: true },
  // ---- far away (cut-outs that keep their own brightness)
  far_hill_a: { make: () => farHill("a"), rough: 1, alphaTest: 0.5, illum: 0.5, illumAll: true, noShadow: true }, far_hill_b: { make: () => farHill("b"), rough: 1, alphaTest: 0.5, illum: 0.5, illumAll: true, noShadow: true },
  far_bay: { make: farBay, rough: 1, alphaTest: 0.5, illum: 0.34, illumAll: true, noShadow: true }, far_roofs: { make: farRoofs, rough: 1, alphaTest: 0.5, illum: 0.5, illumAll: true, noShadow: true },
  sneakers: { make: sneakers, rough: 0.8, alphaTest: 0.5, twoSided: true, noShadow: true },
  // ---- pictures: shop fronts and signs
  shop_mercado: { make: () => picture("shop_mercado", { weather: 1 }), rough: 0.7, illum: 0.25, illumAll: true }, shop_garage: { make: () => picture("shop_garage", { weather: 1 }), rough: 0.7 },
  shop_barber: { make: () => picture("shop_barber", { weather: 1 }), rough: 0.7, illum: 0.25, illumAll: true }, shop_deli: { make: () => picture("shop_deli", { weather: 1 }), rough: 0.7, illum: 0.3, illumAll: true },
  shop_house: { make: () => picture("shop_house", { weather: 1 }), rough: 0.8 }, shop_east: { make: () => picture("shop_east", { weather: 1 }), rough: 0.7 }, stoop_front: { make: () => picture("stoop_front", { weather: 1 }), rough: 0.8 },
  bar_front: { make: () => picture("bar_front", { weather: 1 }), rough: 0.7, illum: 0.3, illumAll: true }, sign_subway: { make: () => picture("sign_subway"), rough: 0.5, illum: 0.8, illumAll: true, twoSided: true },
  // ---- the walls with graffiti (pictures made of the wall and the pieces sprayed on it)
  ...Object.fromEntries([1, 2, 3, 4, 5].map((k) => [`wall_east_${k}`, { make: () => graffitiWall({ w: 2048, h: 853, seed: 100 + k, base: tiled(() => concrete({ base: [150, 148, 142], seed: 31 + k, joints: 2, jointsV: 1, ties: true, streaks: 1 }), 2048, 853, 2), pieces: EAST_PIECES[k - 1] }), rough: 0.85 }])),
  ...Object.fromEntries(["red_a", "red_b", "blue_a", "blue_b"].map((k, i) => [`wall_${k}`, { make: () => graffitiWall({ w: 2048, h: 551, seed: 110 + i, base: tiled(() => (k.startsWith("red") ? brick({ base: [140, 66, 52], seed: 41 }) : plaster(i === 2 ? P.yellow : P.teal, 55 + i)), 2048, 551, 2), pieces: END_PIECES[k] }), rough: 0.85 }])),
  ...Object.fromEntries([1, 2, 3, 4, 5, 6].map((k) => [`wall_west_${k}`, { make: () => graffitiWall({ w: 2048, h: 314, seed: 120 + k, base: lowWall([[52, 150, 156], [214, 96, 76], [232, 190, 70]][k % 3], 60 + k), pieces: WEST_PIECES[k - 1] }), rough: 0.85 }])),
  // ---- graffiti on the ground and on the pillars, houses, containers
  ...Object.fromEntries(["g_centre", "g_ten", "g_seven", "g_hopscotch", "g_manhole", "g_crack", "g_pillar_a", "g_pillar_b", "g_pillar_c", "g_fav_a", "g_fav_b", "g_fav_c", "g_fav_d", "g_goal_red", "g_goal_blue", "g_tunnel"]
    .map((k) => [k, { make: () => picture(k, { alpha: true }), ...decalMat }])),
};
// which pieces go where (x = left edge as a share of the stretch's width, w = width share, y = centre height share)
// the east wall is the hall of fame: one burner fills each stretch, tags and characters over the seams
const EAST_PIECES = [
  [{ name: "p_soccermod", x: 0, w: 1, y: 0.5 }, { name: "t_tags_1", x: 0.02, w: 0.2, y: 0.88 }],
  [{ name: "c_morro", x: 0.04, w: 0.92, y: 0.6 }, { name: "p_joga", x: 0.16, w: 0.68, y: 0.42 }],
  [{ name: "p_streetkings", x: 0.03, w: 0.94, y: 0.5 }, { name: "t_tags_2", x: 0.76, w: 0.22, y: 0.9 }],
  [{ name: "p_rua10", x: 0.0, w: 0.66, y: 0.5 }, { name: "c_ball", x: 0.62, w: 0.38, y: 0.5 }],
  [{ name: "c_wings", x: 0.14, w: 0.72, y: 0.5 }, { name: "t_tags_3", x: 0.0, w: 0.2, y: 0.84 }, { name: "t_tags_1", x: 0.82, w: 0.18, y: 0.8 }],
];
const END_PIECES = {
  red_a: [{ name: "p_rio", x: 0.06, w: 0.46, y: 0.5 }, { name: "c_ball", x: 0.6, w: 0.22, y: 0.5 }, { name: "t_tags_2", x: 0.82, w: 0.17, y: 0.6 }], red_b: [{ name: "p_kacrew", x: 0.02, w: 0.7, y: 0.5 }, { name: "t_tags_1", x: 0.72, w: 0.27, y: 0.5 }],
  blue_a: [{ name: "c_rays", x: 0, w: 1, y: 0.5, alpha: 0.85 }, { name: "p_favela", x: 0.12, w: 0.74, y: 0.5 }], blue_b: [{ name: "c_flag", x: 0.04, w: 0.36, y: 0.5 }, { name: "p_gol", x: 0.46, w: 0.46, y: 0.5 }],
};
const WEST_PIECES = [[{ name: "t_tags_1", x: 0.1, w: 0.3, y: 0.5 }], [{ name: "t_tags_3", x: 0.5, w: 0.4, y: 0.5 }], [{ name: "t_tags_2", x: 0.2, w: 0.28, y: 0.5 }], [{ name: "t_tags_3", x: 0.05, w: 0.4, y: 0.5 }, { name: "t_tags_1", x: 0.62, w: 0.3, y: 0.5 }], [], [{ name: "t_tags_2", x: 0.55, w: 0.28, y: 0.5 }]];

const f3 = (v) => v.toFixed(6);
function vmat(name, m, has) {
  const lines = ['\t"shader"\t"csgo_complex.vfx"'];
  if (m.alphaTest) lines.push('\t"F_ALPHA_TEST"\t"1"');
  if (m.translucent) lines.push('\t"F_TRANSLUCENT"\t"1"');
  if (m.illum) lines.push('\t"F_SELF_ILLUM"\t"1"');
  if (m.twoSided) lines.push('\t"F_RENDER_BACKFACES"\t"1"');
  if (m.noShadow) lines.push('\t"F_DO_NOT_CAST_SHADOWS"\t"1"');
  if (m.alphaTest) lines.push(`\t"g_flAlphaTestReference"\t"${m.alphaTest.toFixed(3)}"`);
  lines.push(`\t"g_flMetalness"\t"${(m.metal ?? 0).toFixed(3)}"`);
  if (m.illum) lines.push('\t"g_flSelfIllumAlbedoFactor"\t"1.000"', `\t"g_flSelfIllumBrightness"\t"${m.illum.toFixed(3)}"`, '\t"g_flSelfIllumScale"\t"1.000"', '\t"g_vSelfIllumTint"\t"[1.000000 1.000000 1.000000 0.000000]"');
  lines.push(`\t"TextureColor"\t"${DIR}/${name}_color.png"`);
  if (has.normal) lines.push(`\t"TextureNormal"\t"${DIR}/${name}_normal.png"`);
  if (has.alpha) lines.push(`\t"TextureTranslucency"\t"${DIR}/${name}_trans.png"`);
  if (m.illum) lines.push(has.illum ? `\t"TextureSelfIllumMask"\t"${DIR}/${name}_illum.png"` : '\t"TextureSelfIllumMask"\t"[1.000000 1.000000 1.000000 0.000000]"');
  lines.push(`\t"TextureRoughness"\t"[${f3(m.rough)} ${f3(m.rough)} ${f3(m.rough)} 0.000000]"`);
  if (m.surface) lines.push('\t"SystemAttributes"', "\t{", `\t\t"PhysicsSurfaceProperties"\t"${m.surface}"`, "\t}");
  return `"Layer0"\n{\n${lines.join("\n")}\n}\n`;
}

const outDirs = [args.addon && path.join(args.addon, DIR), args.preview && path.join(args.preview, "tex")].filter(Boolean);
for (const d of outDirs) fs.mkdirSync(d, { recursive: true });
const previewFile = args.preview && path.join(args.preview, "materials.json");
const preview = previewFile && fs.existsSync(previewFile) ? JSON.parse(fs.readFileSync(previewFile, "utf8")) : {};
for (const [name, m] of Object.entries(MATERIALS)) {
  if (only && !only.some((o) => name === o || (o.endsWith("*") && name.startsWith(o.slice(0, -1))))) continue;
  const r = m.make(), has = { normal: !!(m.normal && r.height), alpha: !!r.alpha, illum: !!r.illum && !m.illumAll };
  const normal = has.normal ? r.color.setHeight(r.height).normalMap(m.normal) : null;
  for (const d of outDirs) {
    r.color.png(path.join(d, `${name}_color.png`), 3);
    if (normal) normal.png(path.join(d, `${name}_normal.png`), 3);
    if (r.alpha) r.alpha.png(path.join(d, `${name}_trans.png`), 1);
    if (has.illum) r.illum.png(path.join(d, `${name}_illum.png`), 1);
  }
  if (args.addon) fs.writeFileSync(path.join(args.addon, DIR, `${name}.vmat`), vmat(name, m, has));
  preview[name] = { map: `${name}_color.png`, normal: normal ? `${name}_normal.png` : undefined, alpha: r.alpha ? `${name}_trans.png` : undefined,
    alphaTest: m.alphaTest, translucent: m.translucent, opacity: m.translucent ? 1 : undefined, emissive: m.illum ? Math.min(1.6, m.illum / 3) : undefined, emissiveMask: has.illum ? `${name}_illum.png` : undefined,
    roughness: m.rough, metalness: m.metal ?? 0, twoSided: m.twoSided, noShadow: m.noShadow };
  console.log(`${name}: ${r.color.w}x${r.color.h}${normal ? " +normal" : ""}${r.alpha ? " +alpha" : ""}${has.illum ? " +illum" : ""}`);
}
// the sky: a lat-long picture for the sky shader
if (!only || only.includes("sky_sunset")) {
  const sky = skyPanorama();
  for (const d of outDirs) sky.color.png(path.join(d, "sky_sunset_color.png"), 3);
  if (args.addon) fs.writeFileSync(path.join(args.addon, DIR, "sky_sunset.vmat"), `"Layer0"\n{\n\t"shader"\t"sky.vfx"\n\t"F_TEXTURE_FORMAT2"\t"0"\n\t"SkyTexture"\t"${DIR}/sky_sunset_color.png"\n\t"g_flBrightnessExposureBias"\t"0.000"\n\t"g_flRenderOnlyExposureBias"\t"0.000"\n\t"g_flRotation"\t"0.000"\n\t"g_flHorizonOffset"\t"0.000"\n}\n`);
  console.log("sky_sunset: 2048x1024");
}
if (previewFile) fs.writeFileSync(previewFile, JSON.stringify(preview, null, 1));
if (missing.length) console.log(`missing art (flat colour used): ${[...new Set(missing)].join(" ")}`);
