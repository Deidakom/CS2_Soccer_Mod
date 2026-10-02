#!/usr/bin/env node
// SoccerMod gym: every texture and material, generated. Pictures with lettering come from
// render-gym-graphics.ps1 (run that first).
//
//   node tools/gym/generate-gym-textures.mjs --graphics <dir> [--addon <content addon dir>] [--preview <dir>] [--only a,b]
// Writes materials/soccermod_gym/<name>.vmat + _color / _normal / _trans / _illum .png into the
// addon, and the same pictures plus materials.json for the local viewer into the preview dir.
import fs from "node:fs";
import path from "node:path";
import { Img, readPng, makeNoise, mix, smooth, gridLine } from "../arena/lib/img.mjs";
import { shade, riser, steel } from "../arena/lib/recipes.mjs";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => { if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
if (!args.graphics) throw new Error("usage: --graphics <dir> [--addon <dir>] [--preview <dir>] [--only a,b]");
const DIR = "materials/soccermod_gym";
const only = args.only?.split(",");

// ---- recipes ----------------------------------------------------------------------------------------
const flat = (rgb) => ({ color: new Img(16, 16).fill(() => rgb) });
const picture = (name) => ({ color: readPng(path.join(args.graphics, `${name}.png`)) });
// Sports parquet: maple strips 4 units wide running along the court, random lengths, every strip
// its own tone and grain. 2048 px per 256 units = 8 texels per unit.
// stain: a colour the wood is glazed with (the grain stays), for the floor's painting
function parquet(S = 2048, stain = null, strength = 0.84) {
  const K = S / 2048, W = 32 * K, n = makeNoise(701), strips = S / W, img = new Img(S, S);
  // per strip: tone, grain offset, the joints (strip ends) along its length
  const info = [...Array(strips).keys()].map(() => {
    const joints = []; let at = Math.floor(Math.floor(n.rnd() * 500) * K);
    while (at < S) { joints.push(at); at += Math.floor((260 + Math.floor(n.rnd() * 420)) * K); }
    return { joints, off: n.rnd() };
  });
  // the board a pixel lies on (the one before the first joint is the last one: the texture tiles)
  const board = (k, y) => { const j = info[k].joints; let b = 0; for (let i = 0; i < j.length; i++) if (y >= j[i]) b = i + 1; return b % j.length; };
  const tone = (k, b) => { const h = Math.sin(k * 12.9898 + b * 78.233) * 43758.5453; return h - Math.floor(h); };
  const gap = (x) => Math.min(x % W, W - 1 - (x % W));
  const jointD = (k, y) => { let d = 1e9; for (const j of info[k].joints) d = Math.min(d, Math.abs(y - j)); return d; };
  img.fill((x, y, u, v) => {
    const k = Math.floor(x / W), b = board(k, y), t = tone(k, b), t2 = tone(k + 31, b + 7), vv = v + info[k].off + b * 0.37;
    // the grain runs along the strip: fine across it, long along it
    const grain = (n.fbm(u, vv, 512, 3, 24) - 0.5) * 0.2 + (n(u + t, v, 512, 12) - 0.5) * 0.05;
    const flame = Math.max(0, n.fbm(u + t2, vv, 48, 2, 6) - 0.6) * 0.6;
    let c = mix([218, 178, 118], [192, 144, 88], t * 0.7 + flame);
    c = mix(c, [228, 194, 138], t2 * 0.35);
    const k2 = 1 + grain - smooth(1.6 * K, 0.2 * K, gap(x)) * 0.3 - smooth(2.2 * K, 0.3 * K, jointD(k, y)) * 0.34;
    if (stain) { const l = (c[0] * 0.3 + c[1] * 0.59 + c[2] * 0.11) / 168; return shade(mix(c, shade(stain, l), strength), k2); }
    return shade(c, k2);
  });
  return { color: img, height: (x, y) => { const k = Math.floor(x / W); return -smooth(2 * K, 0.2 * K, gap(x)) * 1.2 - smooth(2.4 * K, 0.3 * K, jointD(k, y)) * 1.2; } };
}
// ---- the evening city behind the glass (painted cut-outs: colour, mask, and what glows) --------------------
function painter(w, h) {
  const color = new Img(w, h), alpha = new Img(w, h), illum = new Img(w, h);
  for (let i = 0; i < w * h; i++) { color.d[i * 4 + 3] = 255; alpha.d[i * 4 + 3] = 255; illum.d[i * 4 + 3] = 255; }
  const px = (x, y, c, glow = 0) => { x = Math.round(x); y = Math.round(y); if (x < 0 || y < 0 || x >= w || y >= h) return; const o = (y * w + x) * 4; color.d[o] = c[0]; color.d[o + 1] = c[1]; color.d[o + 2] = c[2]; alpha.d[o] = alpha.d[o + 1] = alpha.d[o + 2] = 255; illum.d[o] = illum.d[o + 1] = illum.d[o + 2] = glow * 255; };
  const rect = (x0, y0, x1, y1, c, glow = 0) => { for (let y = Math.round(y0); y < Math.round(y1); y++) for (let x = Math.round(x0); x < Math.round(x1); x++) px(x, y, typeof c === "function" ? c(x, y) : c, glow); };
  const line = (x0, y0, x1, y1, t, c, glow = 0) => { const n = Math.ceil(Math.hypot(x1 - x0, y1 - y0)) + 1; for (let k = 0; k <= n; k++) for (let a = -t / 2; a <= t / 2; a++) for (let b = -t / 2; b <= t / 2; b++) px(x0 + (x1 - x0) * k / n + a, y0 + (y1 - y0) * k / n + b, c, glow); };
  // unpainted pixels take the colour of the nearest painted one below them (no dark fringe at the cut)
  const done = (fallback) => { for (let x = 0; x < w; x++) { let last = null; for (let y = h - 1; y >= 0; y--) { const o = (y * w + x) * 4; if (alpha.d[o] > 0) last = [color.d[o], color.d[o + 1], color.d[o + 2]]; else { const c = last ?? fallback; color.d[o] = c[0]; color.d[o + 1] = c[1]; color.d[o + 2] = c[2]; } } } return { color, alpha, illum }; };
  return { px, rect, line, done, w, h };
}
// far: towers in three rows of depth, the nearer the darker; windows lit in warm and cold light, red lamps on
// the masts. The eye's height lies at about three quarters down the picture: we look out from a roof.
function cityFar() {
  const W = 2048, H = 1024, p = painter(W, H); let seed = 911; const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
  const rows = [[[118, 112, 150], 0.34, 0.98, 0.5], [[82, 80, 116], 0.2, 0.72, 0.62], [[50, 50, 78], 0.1, 0.5, 0.74]];
  for (const [c, lo, hi, lit] of rows) {
    let x = -30;
    while (x < W) {
      const wd = 46 + rnd() * 96, cluster = Math.exp(-(((x / W - 0.38) / 0.2) ** 2)) + 0.6 * Math.exp(-(((x / W - 0.8) / 0.1) ** 2)), ht = H * (lo + (hi - lo) * Math.min(1, cluster * (0.5 + rnd() * 0.6) + rnd() * 0.18)), top = H - ht, r = rnd();
      let xa = x, xb = x + wd, ya = top;
      p.rect(xa, ya, xb, H, (px2) => shade(c, (px2 > xb - wd * 0.28 ? 0.8 : 1) * (0.97 + ((px2 * 7) % 5) * 0.012)));
      p.rect(xa, ya, xb, ya + 2, shade(c, 1.3));
      if (r < 0.3) { const cut = wd * 0.2; p.rect(xa + cut, ya - ht * 0.12, xb - cut, ya, shade(c, 1.04)); ya -= ht * 0.12; xa += cut; xb -= cut; }
      if (r < 0.14) { const mx = (xa + xb) / 2; for (let k = 0; k < 5; k++) p.rect(mx - (5 - k) * 3, ya - (k + 1) * 9, mx + (5 - k) * 3, ya - k * 9, shade(c, 1.1)); p.line(mx, ya - 45, mx, ya - 110, 2, shade(c, 1.2)); p.rect(mx - 2, ya - 114, mx + 3, ya - 109, [255, 60, 40], 1); }
      else if (r > 0.86) { const mx = xa + wd * (0.3 + rnd() * 0.4); p.line(mx, ya, mx, ya - 46, 2, shade(c, 0.8)); p.rect(mx - 2, ya - 50, mx + 3, ya - 45, [255, 60, 40], 1); }
      // windows: whole floors of an office lit, single flats in a block
      const office = rnd() < 0.4, cool = rnd() < 0.5;
      for (let y = top + 7; y < H - 4; y += 9) { const floorLit = office && rnd() < 0.45; for (let wx = x + 4; wx < x + wd - 5; wx += 7) if (floorLit ? rnd() < 0.86 : rnd() < lit * 0.42) p.rect(wx, y, wx + 4, y + 5, office && cool ? [200, 226, 255] : rnd() < 0.8 ? [255, 210, 140] : [255, 236, 200], 1); }
      x += wd * (0.72 + rnd() * 0.5);
    }
  }
  // the haze of the streets' light, rising from below
  const out = p.done([60, 60, 90]);
  for (let i = 0; i < W * H; i++) { const y = Math.floor(i / W), t = smooth(0.55, 1, y / H) * 0.36 + 0.06; for (let c = 0; c < 3; c++) out.color.d[i * 4 + c] += ([255, 168, 120][c] - out.color.d[i * 4 + c]) * t; }
  return out;
}
// near: the roofs next door, dark - parapets, stair houses, tanks, cooling units, aerials, and a neon sign
function cityNear() {
  const W = 2048, H = 512, p = painter(W, H), dark = [34, 34, 48]; let seed = 921; const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
  let x = 0;
  while (x < W) {
    const wd = 150 + rnd() * 260, y0 = H * (0.3 + rnd() * 0.45), x1 = Math.min(W, x + wd), r = rnd(), mx = x + wd * (0.2 + rnd() * 0.6);
    p.rect(x, y0, x1, H, (px2, py) => shade(dark, 0.9 + ((px2 * 3 + py * 5) % 11) * 0.02)); p.rect(x, y0, x1, y0 + 3, [86, 80, 104]);
    for (let y = y0 + 16; y < H - 8; y += 20) for (let wx = x + 10; wx < x1 - 14; wx += 16) if (rnd() < 0.2) p.rect(wx, y, wx + 7, y + 11, rnd() < 0.7 ? [255, 206, 130] : [190, 220, 255], 1);
    if (r < 0.3) { p.rect(mx - 22, y0 - 40, mx + 22, y0, dark); p.rect(mx - 22, y0 - 40, mx + 22, y0 - 38, [86, 80, 104]); p.rect(mx - 6, y0 - 26, mx + 4, y0 - 6, [255, 206, 130], 1); }                       // stair house with a lit door
    else if (r < 0.55) { p.rect(mx - 16, y0 - 46, mx + 16, y0 - 14, dark); for (const e of [-13, 0, 13]) p.line(mx + e, y0 - 14, mx + e, y0, 2, dark); for (let k = 0; k < 10; k++) p.rect(mx - 18 + k * 1.8, y0 - 48 - k, mx + 18 - k * 1.8, y0 - 47 - k, dark); }   // water tank
    else if (r < 0.8) { for (let k = 0; k < 3; k++) p.rect(mx - 40 + k * 28, y0 - 16, mx - 18 + k * 28, y0, [52, 52, 66]); }                                                                               // cooling units
    else { p.line(mx, y0, mx, y0 - 70, 2, dark); for (const k of [24, 40, 56]) p.line(mx - 14 + k / 5, y0 - k, mx + 14 - k / 5, y0 - k, 1.5, dark); p.rect(mx - 2, y0 - 74, mx + 3, y0 - 69, [255, 60, 40], 1); }
    x = x1 + (rnd() < 0.3 ? 14 + rnd() * 40 : 0);
  }
  // a neon sign on a frame on one of the roofs: a ball and three bars
  { const sx = 760, sy = 96, glow = [255, 70, 90]; p.line(sx + 20, sy + 70, sx + 20, sy + 130, 3, dark); p.line(sx + 240, sy + 70, sx + 240, sy + 130, 3, dark); p.rect(sx, sy + 66, sx + 260, sy + 70, dark);
    for (let a = 0; a < 64; a++) { const t = a / 64 * 2 * Math.PI; p.rect(sx + 34 + Math.cos(t) * 28 - 2, sy + 30 + Math.sin(t) * 28 - 2, sx + 34 + Math.cos(t) * 28 + 2, sy + 30 + Math.sin(t) * 28 + 2, glow, 1); }
    for (let k = 0; k < 5; k++) { const t = -Math.PI / 2 + k * 2 * Math.PI / 5; p.line(sx + 34, sy + 30, sx + 34 + Math.cos(t) * 26, sy + 30 + Math.sin(t) * 26, 2, glow, 1); }
    for (let k = 0; k < 3; k++) p.rect(sx + 84, sy + 8 + k * 20, sx + 250 - k * 30, sy + 16 + k * 20, [255, 214, 120], 1); }
  return p.done(dark);
}
// the evening sky as a lat-long panorama: the last glow low in the east (where the glass front looks), deep
// blue above with the first stars
function skyDusk() {
  const w = 2048, h = 1024, n = makeNoise(931), img = new Img(w, h);
  img.fill((x, y, u, v) => {
    const az = u * Math.PI * 2, el = (0.5 - v) * Math.PI, e = Math.max(0, el), toward = (Math.cos(az) + 1) / 2, t = Math.pow(1 - Math.min(1, e / (Math.PI / 2)), 2.6);
    let c = mix([14, 22, 58], mix([120, 92, 140], [255, 138, 70], toward ** 1.5), t * (0.4 + 0.6 * toward));
    c = mix(c, [255, 206, 150], Math.pow(toward, 14) * smooth(0.22, 0, e) * 0.7);
    const cl = smooth(0.52, 0.76, n.fbm((u * 6) % 1, (v * 2.4) % 1, 5, 4, 0.5)) * smooth(0.02, 0.16, e) * smooth(0.9, 0.3, e);
    c = mix(c, mix([52, 46, 78], [236, 132, 110], toward ** 2), cl * 0.75);
    if (e > 0.3 && n(u, v, 1024) > 0.992) c = mix(c, [255, 255, 255], 0.9 * smooth(0.3, 0.8, e));
    if (el < 0) c = mix(c, [40, 36, 52], smooth(0, -0.1, el));
    return c;
  });
  return { color: img };
}
// glass: nearly clear, with the slanted streaks a pane shows against the light
function glass() {
  const s = 256, img = new Img(s, s), alpha = new Img(s, s);
  img.fill(() => [196, 220, 232]);
  alpha.fill((x, y) => { const d = ((x + y) % 256) / 256, streak = smooth(0.34, 0.4, d) * smooth(0.5, 0.44, d) * 0.5 + smooth(0.62, 0.64, d) * smooth(0.7, 0.66, d) * 0.3, a = (0.07 + streak * 0.1) * 255; return [a, a, a]; });
  return { color: img, alpha };
}
function gravel() {
  const s = 512, n = makeNoise(941), img = new Img(s, s);
  img.fill((x, y, u, v) => { const g = n(u, v, 256), f = n(u + 0.3, v + 0.6, 512); return shade([74, 72, 76], 0.7 + g * 0.5 + (f > 0.8 ? 0.3 : 0) + (n.fbm(u, v, 4, 3) - 0.5) * 0.3); });
  return { color: img, height: (x, y, u, v) => n(u, v, 256) * 1.5 };
}
function plantDoor() {
  const w = 128, h = 256, n = makeNoise(951), img = new Img(w, h);
  img.fill((x, y, u, v) => { const frame = x < 8 || x > w - 9 || y < 8, louvre = y > 40 && y < 110 && x > 24 && x < w - 24, sign = Math.abs(x - w / 2) < 16 && Math.abs(y - 150) < 12; if (sign) return [240, 200, 40]; return shade(frame ? [50, 52, 58] : [96, 104, 112], (0.9 + (n(u, v, 64) - 0.5) * 0.16) * (louvre ? 0.6 + ((y % 10) / 10) * 0.5 : 1)); });
  return { color: img };
}
function lino() {
  const s = 512, n = makeNoise(711), img = new Img(s, s);
  img.fill((x, y, u, v) => {
    const speck = n(u, v, 256), k = 1 + (n.fbm(u, v, 4, 3) - 0.5) * 0.1 + (speck - 0.5) * 0.08;
    let c = shade([74, 86, 98], k);
    if (speck > 0.88) c = mix(c, [150, 160, 170], 0.4);
    return c;
  });
  return { color: img, height: (x, y, u, v) => n(u, v, 256) * 0.5 };
}
// floor paint: flat colour, slightly worn
function paint(rgb, seed) {
  const s = 128, n = makeNoise(seed), img = new Img(s, s);
  img.fill((x, y, u, v) => shade(rgb, 0.96 + (n.fbm(u, v, 6, 3) - 0.5) * 0.12 + (n(u, v, 64) - 0.5) * 0.04));
  return { color: img };
}
// kick board: painted plywood, the lettering from the picture, scuffs where the ball hits
function kickboard() {
  const src = readPng(path.join(args.graphics, "gym_kickboard.png")), n = makeNoise(721), img = new Img(src.w, src.h);
  img.fill((x, y, u, v) => {
    const c = src.get(x, y), scuff = Math.max(0, n.fbm(u, v, 14, 3, 0.25) - 0.6) * 1.4 * smooth(0.15, 0.6, v);
    const k = 1 + (n.fbm(u, v, 60, 3, 0.1) - 0.5) * 0.12 - smooth(3, 0.5, gridLine(x, src.w / 2)) * 0.35;
    return mix(shade([c[0], c[1], c[2]], k), [170, 172, 176], Math.min(0.5, scuff));
  });
  return { color: img, height: (x) => -smooth(3, 0.5, gridLine(x, src.w / 2)) * 2 };
}
// steel mesh mats: upright rods every 6 units, double wires every 12 (48 units per tile)
function mesh() {
  const s = 256, img = new Img(s, s), alpha = new Img(s, s);
  const rod = (x) => gridLine(x, 32), wire = (y) => Math.min(gridLine(y - 5, 64), gridLine(y + 5, 64));
  img.fill((x, y) => shade([52, 56, 60], rod(x) < 2 ? 1.25 - rod(x) * 0.2 : 0.9));
  alpha.fill((x, y) => { const a = Math.max(smooth(2.6, 1.2, rod(x)), smooth(2.2, 1, wire(y))) * 255; return [a, a, a]; });
  return { color: img, alpha };
}
// knotted cord net (cell = a quarter of the tile)
function net(strength, rgb = [214, 216, 218]) {
  const s = 128, cell = 32, img = new Img(s, s), alpha = new Img(s, s);
  const d = (x, y) => Math.min(gridLine(x, cell), gridLine(y, cell)), knot = (x, y) => Math.hypot(gridLine(x, cell), gridLine(y, cell));
  img.fill((x, y) => shade(rgb, knot(x, y) < 3 ? 1.08 : 0.94));
  alpha.fill((x, y) => { const a = Math.max(smooth(1.7, 0.7, d(x, y)), smooth(3.2, 2, knot(x, y))) * strength; return [a, a, a]; });
  return { color: img, alpha };
}
// goal frame: red / white or blue / white in diagonal bands (48 units per tile)
function goalStripes(rgb) {
  const s = 64, img = new Img(s, s);
  img.fill((x, y) => ((x + y) % s < s / 2 ? rgb : [238, 238, 234]));
  return { color: img };
}
// impact wall: birch plywood panels 64 x 128 units with open joints and screw caps
function wallPanel() {
  const s = 512, n = makeNoise(731), img = new Img(s, s), jx = (x) => gridLine(x, s / 2), jy = (y) => gridLine(y, s);
  const screw = (x, y) => Math.hypot(gridLine(x - 16, s / 2) , gridLine(y - 64, s / 4));
  img.fill((x, y, u, v) => {
    const p = Math.floor(x / (s / 2)), grain = (n.fbm(u + p * 0.31, v, 96, 3, 12) - 0.5) * 0.16 + (n(u, v, 256, 16) - 0.5) * 0.05;
    let k = (p ? 0.97 : 1.03) + grain - smooth(3, 0.6, Math.min(jx(x), jy(y))) * 0.5;
    if (screw(x, y) < 3.2) k *= 0.72;
    return shade([222, 196, 150], k);
  });
  return { color: img, height: (x, y) => -smooth(3.5, 0.6, Math.min(jx(x), jy(y))) * 4 };
}
// painted block wall
function wallWhite() {
  const s = 512, n = makeNoise(741), img = new Img(s, s), bw = s / 4, bh = s / 8;
  const joint = (x, y) => Math.min(gridLine(x + (Math.floor(y / bh) % 2) * bw / 2, bw), gridLine(y, bh));
  img.fill((x, y, u, v) => shade([210, 209, 203], 1 + (n.fbm(u, v, 5, 3) - 0.5) * 0.06 + (n(u, v, 256) - 0.5) * 0.03 - smooth(2.2, 0.3, joint(x, y)) * 0.1));
  return { color: img, height: (x, y, u, v) => -smooth(2.6, 0.4, joint(x, y)) * 1.6 + n(u, v, 128) * 0.5 };
}
// wall bars: two uprights and fourteen rungs per frame, cut out
function wallBars() {
  const s = 256, n = makeNoise(751), img = new Img(s, s), alpha = new Img(s, s);
  const upright = (x) => Math.abs(x - 14) < 9 || Math.abs(x - (s - 14)) < 9, rung = (y) => y > 10 && gridLine(y - 9, 17.2) < 3.4;
  img.fill((x, y, u, v) => {
    const up = upright(x), k = 1 + (n.fbm(u, v, up ? 4 : 30, 3, up ? 0.08 : 8) - 0.5) * 0.22;
    return shade(up ? [196, 150, 92] : [214, 172, 112], rung(y) && !up ? k * (1.06 - gridLine(y - 9, 17.2) * 0.07) : k);
  });
  alpha.fill((x, y) => (upright(x) || rung(y) ? [255, 255, 255] : [0, 0, 0]));
  return { color: img, alpha };
}
function backboard() {
  const w = 256, h = 160, img = new Img(w, h), n = makeNoise(761);
  const frame = (x, y) => x < 6 || y < 6 || x > w - 7 || y > h - 7;
  const target = (x, y) => { const dx = Math.abs(x - w / 2), dy = y - 62; return (dx < 50 && dy > 0 && dy < 66) && !(dx < 43 && dy > 7 && dy < 59); };
  img.fill((x, y, u, v) => (frame(x, y) || target(x, y) ? [26, 26, 28] : shade([240, 240, 236], 1 + (n.fbm(u, v, 5, 3) - 0.5) * 0.05)));
  return { color: img };
}
// timber slats: warm oak battens on black felt
function timberSlats() {
  const s = 512, n = makeNoise(441), img = new Img(s, s), W = 32;
  img.fill((x, y, u, v) => {
    const slat = Math.floor(x / W), inX = x % W, gap = inX < 6, tone = 0.86 + ((slat * 37) % 11) / 11 * 0.22;
    if (gap) return [14, 13, 13];
    const grain = (n.fbm(u * 0.25 + slat * 0.13, v, 60, 3, 0.06) - 0.5) * 0.3 + (n(u * 3, v, 2, 180) - 0.5) * 0.08, edge = smooth(4, 0, Math.min(inX - 6, W - 1 - inX)) * 0.22;
    return shade([178, 128, 76], tone * (1 + grain) - edge);
  });
  return { color: img, height: (x) => ((x % W) < 6 ? -6 : 0) };
}
// a band of windows: daylight behind frosted glass, 240 units per tile
function windows() {
  const w = 512, h = 256, n = makeNoise(771), img = new Img(w, h), illum = new Img(w, h);
  const bar = (x, y) => gridLine(x, w / 4) < 5 || y < 7 || y > h - 8 || Math.abs(y - h * 0.42) < 3;
  img.fill((x, y, u, v) => (bar(x, y) ? [58, 60, 64] : shade(mix([222, 236, 250], [250, 252, 255], 1 - v), 1 + (n.fbm(u, v, 3, 3) - 0.5) * 0.05)));
  illum.fill((x, y) => (bar(x, y) ? [0, 0, 0] : [255, 255, 255]));
  return { color: img, illum };
}
function ceiling() {
  const s = 512, n = makeNoise(781), img = new Img(s, s), line = (x, y) => Math.min(gridLine(x, s / 2), gridLine(y, s / 2));
  img.fill((x, y, u, v) => {
    const hole = smooth(1.5, 0.6, Math.hypot(gridLine(x, 8), gridLine(y, 8)));
    return shade([204, 204, 199], 1 + (n.fbm(u, v, 4, 3) - 0.5) * 0.06 - hole * 0.1 - smooth(2.2, 0.4, line(x, y)) * 0.3);
  });
  return { color: img, height: (x, y) => -smooth(2.6, 0.4, line(x, y)) * 2 };
}
// glulam beam: lamination lines along the beam
function beam() {
  const s = 256, n = makeNoise(791), img = new Img(s, s);
  img.fill((x, y, u, v) => {
    const lam = Math.floor(y / 21), tone = 0.92 + ((lam * 53) % 7) / 7 * 0.16;
    return shade([206, 160, 100], tone * (1 + (n.fbm(u, v + lam * 0.21, 3, 3, 0.05) - 0.5) * 0.2) - smooth(1.6, 0.3, gridLine(y, 21)) * 0.22);
  });
  return { color: img };
}
function ledPanel() {
  const w = 256, h = 88, img = new Img(w, h), illum = new Img(w, h), lit = (x, y) => y > 5 && y < h - 6 && x > 5 && x < w - 6 && gridLine(x, w / 3) > 2;
  img.fill((x, y) => (lit(x, y) ? [255, 252, 244] : [44, 46, 50]));
  illum.fill((x, y) => (lit(x, y) ? [255, 255, 255] : [0, 0, 0]));
  return { color: img, illum };
}

// ---- materials ----------------------------------------------------------------------------------------
// rough / metal: constants; alphaTest: cut-out; translucent: blended (alpha picture);
// illum: self-illumination brightness (with the mask when the recipe has one, else the whole picture)
const MATERIALS = {
  parquet: { make: parquet, rough: 0.32, normal: 0.7, noShadow: true, surface: "Wood" },
  lino: { make: lino, rough: 0.6, normal: 0.6 },
  line_white: { make: () => paint([236, 236, 230], 801), rough: 0.4, noShadow: true },
  line_black: { make: () => paint([30, 32, 38], 802), rough: 0.4, noShadow: true },
  // the floor's painting: the parquet glazed in colours (1024 px: the same boards as the bare floor)
  // [colour, how strongly it covers the wood]: walnut and the accent are glazes, the wood shows through
  ...Object.fromEntries(Object.entries({ walnut: [[104, 66, 40], 0.5], accent: [[206, 96, 74], 0.42], white: [[240, 238, 228], 0.84] }).map(([k, [c, a]]) => [`wood_${k}`, { make: () => parquet(1024, c, a), rough: 0.34, normal: 0.7, noShadow: true, surface: "Wood" }])),
  ...Object.fromEntries(Object.entries({ coral: [232, 98, 84], navy: [30, 56, 120], mustard: [240, 182, 54], mint: [124, 206, 176] }).map(([k, c]) => [`panel_${k}`, { make: () => { const r = wallPanel(); r.color.fill((x, y) => { const p = r.color.get(x, y), l = (p[0] * 0.3 + p[1] * 0.59 + p[2] * 0.11) / 150; return [c[0] * l, c[1] * l, c[2] * l]; }); return r; }, rough: 0.55, normal: 1 }])),
  // the glass front, the terrace behind it, the evening city
  glass: { make: glass, rough: 0.05, translucent: true, twoSided: true, noShadow: true },
  roof_gravel: { make: gravel, rough: 0.95, normal: 1 }, parapet: { make: wallWhite, rough: 0.9, normal: 1 }, plant_wall: { make: wallWhite, rough: 0.9, normal: 1 }, plant_door: { make: plantDoor, rough: 0.6 },
  ac_unit: { make: () => steel([150, 154, 158], 961), rough: 0.5, metal: 0.4 },
  city_far: { make: cityFar, rough: 1, alphaTest: 0.5, illum: 2.4, noShadow: true }, city_near: { make: cityNear, rough: 1, alphaTest: 0.5, illum: 2.6, noShadow: true },
  emblem: { make: () => picture("gym_emblem"), rough: 0.36, noShadow: true },
  line_yellow: { make: () => paint([236, 190, 30], 803), rough: 0.4, noShadow: true },
  collide: { make: () => flat([255, 0, 255]), rough: 1 },
  kickboard: { make: kickboard, rough: 0.55 },   // no normal map: the compiler cannot build mips for a 1536 px one
  steel_dark: { make: () => steel([46, 48, 52], 82), rough: 0.5, metal: 0.3 },
  mesh: { make: mesh, rough: 0.5, metal: 0.3, translucent: true, twoSided: true, noShadow: true },
  net: { make: () => net(150), rough: 0.9, translucent: true, twoSided: true, noShadow: true },
  net_top: { make: () => net(85), rough: 0.9, translucent: true, twoSided: true, noShadow: true },
  goal_net: { make: () => net(215), rough: 0.9, translucent: true, twoSided: true, noShadow: true },
  goal_red: { make: () => goalStripes([206, 34, 38]), rough: 0.4, metal: 0.1 },
  goal_blue: { make: () => goalStripes([34, 84, 200]), rough: 0.4, metal: 0.1 },
  dark: { make: () => flat([20, 20, 23]), rough: 0.9 },
  wall_panel: { make: wallPanel, rough: 0.55, normal: 1 },
  stripe_red: { make: () => flat([200, 36, 40]), rough: 0.5 },
  stripe_blue: { make: () => flat([36, 84, 196]), rough: 0.5 },
  wall_white: { make: wallWhite, rough: 0.85, normal: 1 },
  wall_bars: { make: wallBars, rough: 0.5, alphaTest: 0.5, twoSided: true, noShadow: true },
  backboard: { make: backboard, rough: 0.3, twoSided: true },
  ring: { make: () => flat([236, 96, 28]), rough: 0.4, metal: 0.2, twoSided: true },
  scoreboard: { make: () => picture("gym_scoreboard"), rough: 0.3, illum: 2.2, illumAll: true },
  sign: { make: () => picture("gym_sign"), rough: 0.5, illum: 0.6, illumAll: true },
  banner_red: { make: () => picture("gym_banner_red"), rough: 0.9 },
  banner_blue: { make: () => picture("gym_banner_blue"), rough: 0.9 },
  wall_timber: { make: timberSlats, rough: 0.6, normal: 1.2 },
  windows: { make: windows, rough: 0.2, illum: 1.5 },
  riser: { make: riser, rough: 0.92 },
  ceiling: { make: ceiling, rough: 0.9, normal: 1 },
  beam: { make: beam, rough: 0.6 },
  led_panel: { make: ledPanel, rough: 0.3, illum: 3.5, noShadow: true },
};

const f3 = (v) => v.toFixed(6);
function vmat(name, m, has) {
  const lines = ['\t"shader"\t"csgo_complex.vfx"'];
  if (m.alphaTest) lines.push('\t"F_ALPHA_TEST"\t"1"');
  if (m.translucent) lines.push('\t"F_TRANSLUCENT"\t"1"');
  if (m.illum) lines.push('\t"F_SELF_ILLUM"\t"1"');
  if (m.twoSided) lines.push('\t"F_RENDER_BACKFACES"\t"1"');
  // (tried 2026-10-02: every material without shadows, so the top light reaches the floor - the sky light
  // then floods the hall, everything turns pale, and the ball still has no shadow. Not done.)
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
  if (only && !only.includes(name)) continue;
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
if (!only || only.includes("sky_dusk")) {
  const sky = skyDusk();
  for (const d of outDirs) sky.color.png(path.join(d, "sky_dusk_color.png"), 3);
  if (args.addon) fs.writeFileSync(path.join(args.addon, DIR, "sky_dusk.vmat"), `"Layer0"\n{\n\t"shader"\t"sky.vfx"\n\t"F_TEXTURE_FORMAT2"\t"0"\n\t"SkyTexture"\t"${DIR}/sky_dusk_color.png"\n\t"g_flBrightnessExposureBias"\t"0.000"\n\t"g_flRenderOnlyExposureBias"\t"0.000"\n\t"g_flRotation"\t"0.000"\n\t"g_flHorizonOffset"\t"0.000"\n}\n`);
  console.log("sky_dusk: 2048x1024");
}
if (previewFile) fs.writeFileSync(previewFile, JSON.stringify(preview, null, 1));
