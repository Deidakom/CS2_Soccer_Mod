#!/usr/bin/env node
// SoccerMod 1v1 cage (the drained pool): every texture and material, generated. The pictures with
// lettering come from render-pool-graphics.ps1 (run that first); the graffiti on the tiles are pieces of
// the street arena's art (render-street-art.ps1, --street-art <dir>). A picture that is missing is
// replaced by a flat colour, so the map can be looked at before the art exists.
//
//   node tools/pool/generate-pool-textures.mjs --graphics <dir> --street-art <dir> [--addon <content addon dir>] [--preview <dir>] [--only a,b]
// Writes materials/soccermod_pool/<name>.vmat + _color / _normal / _trans / _illum .png into the
// addon, and the same pictures plus materials.json for the local viewer into the preview dir.
import fs from "node:fs";
import path from "node:path";
import { Img, readPng, makeNoise, mix, smooth, gridLine, clamp01 } from "../arena/lib/img.mjs";
import { shade, concrete, steel } from "../arena/lib/recipes.mjs";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => { if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
if (!args.graphics) throw new Error("usage: --graphics <dir> [--street-art <dir>] [--addon <dir>] [--preview <dir>] [--only a,b]");
const DIR = "materials/soccermod_pool";
const only = args.only?.split(",");
const missing = [];
const hash = (s) => { let h = 2166136261; for (const c of s) h = Math.imul(h ^ c.charCodeAt(0), 16777619) >>> 0; return h; };

// ---- helpers ------------------------------------------------------------------------------------------
const flat = (rgb) => ({ color: new Img(16, 16).fill(() => rgb) });
const load = (dir, name) => { const f = dir && path.join(dir, `${name}.png`); return f && fs.existsSync(f) ? readPng(f) : null; };
// a picture with its own alpha: colour + mask, resampled to power-of-two sizes (the compiler needs them with
// an alpha channel), the colour bled into the transparent part; faded = thinner and rubbed off in patches
function cutPicture(src, name, { faded = 0, tiles = 0 } = {}) {
  if (!src) { missing.push(name); const h = hash(name), img = new Img(64, 64).fill(() => [90 + h % 130, 90 + (h >> 8) % 130, 90 + (h >> 16) % 130]); return { color: img, alpha: new Img(64, 64).fill((x, y) => (Math.hypot(x - 32, y - 32) < 26 ? [200, 200, 200] : [0, 0, 0])) }; }
  const pot = (v) => Math.min(2048, 2 ** Math.ceil(Math.log2(v) - 0.2)), W = pot(src.w), H = pot(src.h), color = new Img(W, H), alpha = new Img(W, H), n = makeNoise(hash(name) % 997 + 3);
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    const fx = (x + 0.5) * src.w / W - 0.5, fy = (y + 0.5) * src.h / H - 0.5, x0 = Math.max(0, Math.floor(fx)), y0 = Math.max(0, Math.floor(fy)), x1 = Math.min(src.w - 1, x0 + 1), y1 = Math.min(src.h - 1, y0 + 1), tx = Math.max(0, fx - x0), ty = Math.max(0, fy - y0), o = (y * W + x) * 4;
    const p = (c) => { const q = (X, Y) => src.d[(Y * src.w + X) * 4 + c]; return (q(x0, y0) * (1 - tx) + q(x1, y0) * tx) * (1 - ty) + (q(x0, y1) * (1 - tx) + q(x1, y1) * tx) * ty; };
    let a = p(3); const u = x / W, v = y / H;
    if (faded) a *= (1 - faded * 0.35) * (1 - faded * smooth(0.44, 0.72, n.fbm(u, v, 7, 4) * 0.7 + n(u, v, 200) * 0.3));
    // on tiles the paint is gone in the joints
    if (tiles && (gridLine(x, tiles) < 1.2 || gridLine(y, tiles) < 1.2)) a *= 0.35;
    color.d[o] = p(0); color.d[o + 1] = p(1); color.d[o + 2] = p(2); color.d[o + 3] = 255; alpha.d[o] = alpha.d[o + 1] = alpha.d[o + 2] = a; alpha.d[o + 3] = 255;
  }
  for (let pass = 0; pass < 4; pass++) { const s = color.d.slice(), m = alpha.d.slice(); for (let y = 1; y < H - 1; y++) for (let x = 1; x < W - 1; x++) { const o = (y * W + x) * 4; if (m[o] > 8 || s[o + 3] === 254) continue; let r = 0, g = 0, b = 0, k = 0; for (const d of [-4, 4, -W * 4, W * 4]) if (m[o + d] > 8 || s[o + d + 3] === 254) { r += s[o + d]; g += s[o + d + 1]; b += s[o + d + 2]; k++; } if (k) color.d.set([r / k, g / k, b / k, 254], o); } }
  return { color, alpha };
}
const sign = (name, opts) => () => { const src = load(args.graphics, name); if (!src) { missing.push(name); return flat([200, 196, 180]); } const n = makeNoise(hash(name) % 991 + 2); for (let y = 0; y < src.h; y++) for (let x = 0; x < src.w; x++) { const o = (y * src.w + x) * 4, k = 1 + (n.fbm(x / src.w, y / src.h, 5, 3) - 0.5) * 0.24 - Math.max(0, n.fbm(x / src.w, y / src.h, 26, 3, 16) - 0.58) * 0.8; src.d[o] *= k; src.d[o + 1] *= k * 0.98; src.d[o + 2] *= k * 0.94; } return opts?.cut ? cutPicture(src, name) : { color: src }; };

// ---- tiles ----------------------------------------------------------------------------------------------
// A field of glazed tiles: size px picture, tile px per tile (tw wide, th high), colour(cx, cy, u, v) per tile.
// dirt(u, v) darkens, gone(u, v) > 0 shows the grey bed where tiles have fallen off.
function tiles({ size = 512, w = size, h = size, tw, th = tw, colour, grout = [150, 156, 150], seed = 1, vary = 0.12, dirt = null, gone = null, offset = false }) {
  const n = makeNoise(seed), img = new Img(w, h);
  const at = (x, y) => { const cy = Math.floor(y / th), ox = offset && cy % 2 ? tw / 2 : 0, cx = Math.floor((x + ox) / tw), lx = (x + ox) % tw, ly = y % th; return { cx, cy, j: Math.min(lx, tw - 1 - lx, ly, th - 1 - ly), lx, ly }; };
  img.fill((x, y, u, v) => {
    const { cx, cy, j, lx, ly } = at(x, y), id = hash(`${seed},${cx % Math.round(w / tw)},${cy}`), g = gone ? gone(u, v, id) : 0;
    if (j < 1 || g > 0.5) return shade(g > 0.5 ? [128, 124, 114] : grout, 0.86 + n(u, v, 128) * 0.26);
    let c = shade(colour(cx, cy, u, v, id), 1 + (((id >> 6) % 100) / 100 - 0.5) * 2 * vary + (n.fbm(u, v, 4, 3) - 0.5) * 0.1);
    c = shade(c, 1 + smooth(tw * 0.5, 1, lx + ly) * 0.07 - smooth(2.4, 1, j) * 0.1);                    // a soft light on the glaze, the edge a little darker
    return dirt ? shade(c, 1 - clamp01(dirt(u, v)) * 0.6) : c;
  });
  return { color: img, height: (x, y) => { const { j } = at(x, y); return smooth(0.5, 2.5, j) * 1.4; } };
}
// the pool's wall, one picture over its whole height: a dark band of tiles under the edge with a white
// stripe, the old water line below it, lime runs, algae and grime towards the foot, tiles fallen off
function poolWall() {
  const n = makeNoise(21);
  return tiles({ size: 512, tw: 16, th: 17, seed: 21, vary: 0.1, grout: [128, 150, 146],
    colour: (cx, cy, u, v, id) => (v < 0.2 ? (v > 0.13 && v < 0.168 ? [232, 236, 230] : [28, 60, 118]) : id % 23 === 0 ? [52, 150, 156] : mix([78, 182, 186], [64, 160, 170], n.fbm(u, v, 3, 2))),
    dirt: (u, v) => smooth(0.2, 0.26, v) * smooth(0.36, 0.28, v) * 0.5 + smooth(0.62, 1, v) * (0.35 + n.fbm(u, v, 6, 3) * 0.6) + Math.max(0, n.fbm(u, v * 0.3, 30, 3, 12) - 0.6) * 1.4,
    gone: (u, v) => smooth(0.74, 0.78, n.fbm(u + 0.2, v + 0.5, 5, 3)) * (v > 0.24 ? 1 : 0) });
}
function poolFloor() {
  const n = makeNoise(22);
  return tiles({ size: 512, tw: 24, seed: 22, vary: 0.09, grout: [132, 152, 150], colour: (cx, cy, u, v, id) => (id % 31 === 0 ? [150, 196, 200] : [184, 222, 220]),
    dirt: (u, v) => Math.max(0, n.fbm(u, v, 4, 4) - 0.5) * 1.5 + Math.max(0, n.fbm(u + 0.4, v + 0.1, 14, 3) - 0.62) * 1.2, gone: (u, v) => smooth(0.8, 0.83, n.fbm(u + 0.6, v + 0.2, 6, 3)) });
}
// the mosaic on the away end's wall: the sea in bands, a low sun, two leaping fish, a border - all in small tiles
function mosaic() {
  const W = 1024, H = 448, t = 8, n = makeNoise(31), img = new Img(W, H);
  const scene = (u, v) => {
    if (u < 0.03 || u > 0.97 || v < 0.07 || v > 0.93) return (Math.floor(u * 64) + Math.floor(v * 28)) % 2 ? [226, 190, 80] : [22, 60, 122];
    const sun = Math.hypot((u - 0.5) * 2.28, v - 0.5), wave = v - 0.56 - Math.sin(u * 22) * 0.02;
    if (sun < 0.3 && wave < 0) return sun < 0.22 ? [240, 196, 70] : [232, 150, 60];
    for (const [fx, fy, dir] of [[0.24, 0.42, 1], [0.76, 0.36, -1]]) { const dx = (u - fx) * dir, dy = v - fy + dx * 0.4; if ((dx / 0.085) ** 2 + (dy / 0.05) ** 2 < 1) return [226, 232, 230]; if (dx < -0.07 && dx > -0.125 && Math.abs(dy) < (-(dx + 0.07)) * 1.3) return [226, 232, 230]; }
    if (wave < 0) { const ray = Math.abs(((Math.atan2(v - 0.5, u - 0.5) / Math.PI) * 14) % 1); return ray < 0.5 ? [150, 200, 220] : [120, 180, 210]; }
    const band = Math.floor((wave + Math.sin(u * 30 + v * 40) * 0.012) * 26);
    return [[24, 70, 140], [40, 110, 170], [60, 150, 180], [30, 90, 150]][((band % 4) + 4) % 4];
  };
  img.fill((x, y) => { const cx = Math.floor(x / t), cy = Math.floor(y / t), j = Math.min(x % t, t - 1 - (x % t), y % t, t - 1 - (y % t)), id = hash(`m${cx},${cy}`); if (j < 0.8) return [70, 76, 78]; if (id % 37 === 0) return [96, 100, 98]; return shade(scene((cx + 0.5) * t / W, (cy + 0.5) * t / H), 0.84 + ((id >> 5) % 100) / 100 * 0.3 + (n(x / W, y / H, 128) - 0.5) * 0.1); });
  return { color: img };
}
// the name band: the lettering's picture cut into gold tiles on blue
function nameBand() {
  const src = load(args.graphics, "sign_name"); if (!src) { missing.push("sign_name"); return flat([22, 64, 122]); }
  const t = 8, img = new Img(src.w, src.h);
  img.fill((x, y) => { const cx = Math.floor(x / t), cy = Math.floor(y / t), j = Math.min(x % t, t - 1 - (x % t), y % t, t - 1 - (y % t)), id = hash(`n${cx},${cy}`); if (j < 0.8) return [70, 76, 78]; return shade(src.get(Math.min(src.w - 1, cx * t + 4), Math.min(src.h - 1, cy * t + 4)), 0.84 + ((id >> 5) % 100) / 100 * 0.3); });
  return { color: img };
}

// ---- cut-outs and other recipes --------------------------------------------------------------------------
function mesh() {
  const s = 256, img = new Img(s, s), alpha = new Img(s, s);
  const d = (x, y) => Math.min(gridLine(x + y, 46), gridLine(x - y, 46));
  img.fill((x, y) => shade([70, 76, 80], d(x, y) < 2 ? 1.3 : 0.9));
  alpha.fill((x, y) => { const a = smooth(3.4, 1.6, d(x, y)) * 255; return [a, a, a]; });
  return { color: img, alpha };
}
function net(strength, rgb = [214, 216, 218]) {
  const s = 128, cell = 32, img = new Img(s, s), alpha = new Img(s, s);
  const d = (x, y) => Math.min(gridLine(x, cell), gridLine(y, cell)), knot = (x, y) => Math.hypot(gridLine(x, cell), gridLine(y, cell));
  img.fill((x, y) => shade(rgb, knot(x, y) < 3 ? 1.08 : 0.94));
  alpha.fill((x, y) => { const a = Math.max(smooth(1.7, 0.7, d(x, y)), smooth(3.2, 2, knot(x, y))) * strength; return [a, a, a]; });
  return { color: img, alpha };
}
function cutout(w, h, solid, colour = () => [226, 222, 208]) {
  const img = new Img(w, h), alpha = new Img(w, h);
  img.fill((x, y) => colour(x, y)); alpha.fill((x, y) => (solid(x, y) ? [255, 255, 255] : [0, 0, 0]));
  return { color: img, alpha };
}
// the arcade's arch: plaster, open under a flat arc between the columns
function arch() {
  const w = 512, h = 256, n = makeNoise(41), base = concrete({ size: 512, base: [208, 198, 170], seed: 41, streaks: 0.9 }).color;
  return cutout(w, h, (x, y) => ((x - w / 2 + 0.5) / (w / 2 - 22)) ** 2 + ((h - 1 - y) / (h * 0.86)) ** 2 > 1, (x, y) => { const e = ((x - w / 2 + 0.5) / (w / 2 - 22)) ** 2 + ((h - 1 - y) / (h * 0.86)) ** 2, c = base.get(x, y); return shade(c, e < 1.12 ? 0.7 : 1 + (n(x / w, y / h, 64) - 0.5) * 0.06); });
}
// arched and round windows: night behind the glass, the moon's light in it
function windowPic(round) {
  const w = round ? 256 : 128, h = 256, img = new Img(w, h), alpha = new Img(w, h), illum = new Img(w, h), n = makeNoise(51), r = w / 2 - 3, cy = round ? h / 2 : r + 4;
  const inside = (x, y, inset) => (round || y < cy ? Math.hypot(x - w / 2 + 0.5, y - cy) < r - inset : Math.abs(x - w / 2 + 0.5) < r - inset && y < h - inset);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const o = (y * w + x) * 4, u = x / w, v = y / h; let c = [226, 220, 200], a = 0, e = 0;
    if (inside(x, y, 0)) { a = 255;
      const frame = !inside(x, y, 9) || (round ? Math.abs(x - w / 2) < 3 || Math.abs(y - cy) < 3 || Math.abs(Math.hypot(x - w / 2, y - cy) - r * 0.5) < 2.5 : gridLine(x - w / 2, w / 3) < 2.4 || gridLine(y - cy, 46) < 2.4);
      if (frame) c = shade([70, 96, 84], 0.9 + n(u, v, 64) * 0.2);
      else { const moon = round ? Math.hypot(x - w * 0.62, y - h * 0.36) : 1e9, glow = Math.exp(-((moon / 60) ** 2)); c = moon < 26 ? [244, 240, 220] : mix(mix([20, 30, 62], [44, 66, 110], 1 - v), [150, 170, 200], glow * 0.6 + n.fbm(u, v, 4, 3) * 0.12); e = moon < 26 ? 255 : 90 + glow * 120; } }
    img.d[o] = c[0]; img.d[o + 1] = c[1]; img.d[o + 2] = c[2]; img.d[o + 3] = 255; alpha.d[o] = alpha.d[o + 1] = alpha.d[o + 2] = a; alpha.d[o + 3] = 255; illum.d[o] = illum.d[o + 1] = illum.d[o + 2] = e; illum.d[o + 3] = 255;
  }
  for (let i = 0; i < w * h; i++) if (alpha.d[i * 4] === 0) { img.d[i * 4] = 70; img.d[i * 4 + 1] = 96; img.d[i * 4 + 2] = 84; }
  return { color: img, alpha, illum };
}
// the vault's glass: a steel grid, dull panes with the dirt of years on them
function skylight() {
  const s = 256, n = makeNoise(61), img = new Img(s, s), alpha = new Img(s, s);
  const bar = (x, y) => Math.min(gridLine(x, 128), gridLine(y, 128)) < 5 || Math.min(gridLine(x, 32), gridLine(y, 128 / 3)) < 1.2;
  img.fill((x, y, u, v) => (bar(x, y) ? shade([52, 70, 62], 0.9 + n(u, v, 64) * 0.2) : mix([150, 176, 190], [96, 104, 100], n.fbm(u, v, 5, 3))));
  alpha.fill((x, y, u, v) => { const a = bar(x, y) ? 255 : 70 + Math.max(0, n.fbm(u, v, 5, 3) - 0.45) * 330; return [a, a, a]; });
  return { color: img, alpha };
}
function glow(rgb, round = false) {
  const s = 32, img = new Img(s, s).fill(() => rgb);
  if (!round) return { color: img };
  return { color: img, alpha: new Img(s, s).fill((x, y) => { const a = smooth(15, 9, Math.hypot(x - 15.5, y - 15.5)) * 255; return [a, a, a]; }) };
}
function puddle() {
  const s = 256, n = makeNoise(71), img = new Img(s, s).fill(() => [26, 38, 42]);
  return { color: img, alpha: new Img(s, s).fill((x, y, u, v) => { const r = Math.hypot(u - 0.5, v - 0.5) * 2, a = smooth(0.95, 0.5, r + (n.fbm(u, v, 5, 3) - 0.5) * 0.7) * 120; return [a, a, a]; }) };
}
function rustRun() {
  const w = 32, h = 128, n = makeNoise(72), img = new Img(w, h).fill((x, y, u, v) => shade([120, 70, 38], 0.8 + n(u, v, 32) * 0.4));
  return { color: img, alpha: new Img(w, h).fill((x, y, u, v) => { const a = smooth(0.5, 0.1, Math.abs(u - 0.5 + (n(0.3, v, 6) - 0.5) * 0.3)) * smooth(1, 0.15, v) * (0.5 + n(u, v, 32) * 0.5) * 190; return [a, a, a]; }) };
}
function drain() {
  const s = 128; return cutout(s, s, (x, y) => Math.hypot(x - 63.5, y - 63.5) < 62, (x, y) => { const r = Math.hypot(x - 63.5, y - 63.5); return r > 52 ? [150, 150, 146] : gridLine(x, 14) < 3.5 ? [16, 18, 20] : [110, 112, 110]; });
}
function buoy() {
  const s = 128; return cutout(s, s, (x, y) => { const r = Math.hypot(x - 63.5, y - 63.5); return r < 62 && r > 30; }, (x, y) => (Math.floor(((Math.atan2(y - 63.5, x - 63.5) / Math.PI + 1) * 4) + 0.5) % 2 ? [220, 60, 44] : [240, 238, 228]));
}
function planks(rgb, seed, w = 64) {
  const s = 256, n = makeNoise(seed), img = new Img(s, s);
  img.fill((x, y, u, v) => shade(rgb, 0.86 + ((hash(`p${seed},${Math.floor(x / w)}`) % 100) / 100) * 0.2 + (n.fbm(u, v, 40, 3, 0.1) - 0.5) * 0.24 - (gridLine(x, w) < 1.5 ? 0.3 : 0)));
  return { color: img };
}
function speaker() {
  const s = 128, img = new Img(s, s);
  img.fill((x, y) => { const a = Math.hypot(x - 64, y - 84), b = Math.hypot(x - 64, y - 28); if (a < 34) return a < 10 ? [60, 60, 64] : shade([30, 30, 34], 1 + Math.sin(a) * 0.14); if (b < 14) return b < 5 ? [60, 60, 64] : [34, 34, 38]; return x < 4 || y < 4 || x > 123 || y > 123 ? [60, 60, 64] : [20, 20, 23]; });
  return { color: img };
}
// the night sky as a lat-long panorama: deep blue, stars, thin clouds, the moon high in the north
function skyNight() {
  const w = 2048, h = 1024, n = makeNoise(81), img = new Img(w, h), moonAz = Math.PI / 2, moonEl = 1.0;
  img.fill((x, y, u, v) => {
    const az = u * Math.PI * 2, el = (0.5 - v) * Math.PI, e = Math.max(0, el);
    const cosM = Math.cos(el) * Math.cos(moonEl) * Math.cos(az - moonAz) + Math.sin(el) * Math.sin(moonEl);
    let c = mix([26, 36, 72], [8, 12, 30], smooth(0, 1.2, e));
    c = mix(c, [150, 170, 210], Math.pow(Math.max(0, cosM), 40) * 0.5); if (cosM > 0.9993) c = [244, 240, 224];
    const cl = smooth(0.56, 0.8, n.fbm((u * 5) % 1, (v * 2) % 1, 5, 4, 0.5)); c = mix(c, [60, 70, 100], cl * 0.5);
    if (n(u, v, 1024) > 0.99 && cl < 0.3) c = mix(c, [255, 255, 255], 0.85);
    if (el < 0) c = mix(c, [20, 22, 30], smooth(0, -0.1, el));
    return c;
  });
  return { color: img };
}

// ---- materials ----------------------------------------------------------------------------------------
const graf = (name, src) => ({ make: () => cutPicture(load(args["street-art"], src), name, { faded: 0.5, tiles: 16 }), rough: 0.6, translucent: true, noShadow: true });
const pic = (name, extra = {}) => ({ make: sign(name), rough: 0.6, ...extra });
const MATERIALS = {
  collide: { make: () => flat([255, 0, 255]), rough: 1 },
  // ---- the pool
  pool_floor: { make: poolFloor, rough: 0.28, normal: 0.7, noShadow: true, surface: "Concrete" },
  pool_wall: { make: poolWall, rough: 0.3, normal: 0.7 },
  tile_black: { make: () => tiles({ size: 256, tw: 24, seed: 23, colour: () => [26, 30, 38], grout: [70, 76, 78] }), rough: 0.3, noShadow: true },
  niche: { make: () => tiles({ size: 256, tw: 16, seed: 24, colour: (cx, cy, u, v, id) => (id % 9 === 0 ? [30, 70, 110] : [20, 44, 80]), grout: [60, 70, 76] }), rough: 0.3 },
  line_white: { make: () => { const n = makeNoise(25), img = new Img(128, 128); img.fill((x, y, u, v) => shade([236, 234, 226], 0.92 + (n.fbm(u, v, 6, 3) - 0.5) * 0.2 - smooth(0.6, 0.8, n.fbm(u + 0.4, v, 9, 3)) * 0.4)); return { color: img }; }, rough: 0.5, noShadow: true },
  puddle: { make: puddle, rough: 0.04, translucent: true, noShadow: true },
  drain: { make: drain, rough: 0.5, metal: 0.5, alphaTest: 0.5, noShadow: true },
  rust_run: { make: rustRun, rough: 0.8, translucent: true, noShadow: true },
  lamp_ring: { make: () => steel([196, 200, 204], 26), rough: 0.25, metal: 0.8 },
  lamp_glow: { make: () => glow([196, 255, 240]), rough: 0.2, illum: 5, illumAll: true, noShadow: true },
  chrome: { make: () => steel([206, 210, 214], 27), rough: 0.18, metal: 0.9 },
  goal_frame: { make: () => steel([232, 232, 226], 28), rough: 0.4, metal: 0.2 },
  goal_net: { make: () => net(215), rough: 0.9, translucent: true, twoSided: true, noShadow: true },
  // ---- the cage
  mesh: { make: mesh, rough: 0.5, metal: 0.4, translucent: true, twoSided: true, noShadow: true },
  mesh_top: { make: mesh, rough: 0.5, metal: 0.4, translucent: true, twoSided: true, noShadow: true },
  steel_cage: { make: () => steel([64, 70, 74], 29), rough: 0.5, metal: 0.4 },
  // ---- deck, arcade, gallery
  deck_tile: { make: () => { const n = makeNoise(32); return tiles({ size: 512, tw: 48, seed: 32, vary: 0.08, grout: [128, 122, 108], colour: () => [198, 188, 162], dirt: (u, v) => Math.max(0, n.fbm(u, v, 5, 4) - 0.48) * 1.3 }); }, rough: 0.5, normal: 0.8 },
  coping: { make: () => concrete({ base: [216, 212, 198], seed: 33, joints: 8, streaks: 0.5 }), rough: 0.6 },
  column: { make: () => concrete({ base: [206, 198, 174], seed: 34, joints: 3, streaks: 0.9 }), rough: 0.8 },
  arch: { make: arch, rough: 0.85, alphaTest: 0.5 },
  wall_plaster: { make: () => concrete({ base: [208, 198, 170], seed: 35, streaks: 1.1, stain: 1.3 }), rough: 0.9, normal: 0.8 },
  ceiling_plaster: { make: () => concrete({ base: [196, 190, 170], seed: 36, streaks: 0.8, stain: 1.5 }), rough: 0.9 },
  wall_tile: { make: () => { const n = makeNoise(37); return tiles({ size: 512, tw: 48, th: 24, seed: 37, offset: true, vary: 0.06, grout: [150, 150, 142], colour: (cx, cy, u, v, id) => (id % 41 === 0 ? [70, 130, 110] : [228, 230, 222]), dirt: (u, v) => Math.max(0, n.fbm(u, v * 0.4, 20, 3, 10) - 0.58) * 1.2, gone: (u, v) => smooth(0.82, 0.85, n.fbm(u + 0.1, v + 0.7, 5, 3)) }); }, rough: 0.3, normal: 0.7 },
  wall_trim: { make: () => tiles({ size: 256, tw: 32, th: 16, seed: 38, colour: () => [56, 124, 100], grout: [120, 130, 120] }), rough: 0.3 },
  gallery_floor: { make: () => planks([150, 116, 80], 39), rough: 0.7 },
  wood_dark: { make: () => planks([92, 68, 48], 40), rough: 0.7 },
  balustrade: { make: () => cutout(64, 64, (x, y) => y < 5 || y > 57 || Math.abs(x - 31.5) < 5 + Math.sin(y / 64 * Math.PI) * 6 * (y > 14 && y < 50 ? 1 : 0.3), (x, y) => shade([226, 220, 200], 0.82 + (x > 31 ? 0 : 0.16) + Math.sin(y / 64 * Math.PI) * 0.06)), rough: 0.7, alphaTest: 0.5, twoSided: true, noShadow: true },
  bulb: { make: () => glow([255, 206, 130], true), rough: 0.3, illum: 5, illumAll: true, alphaTest: 0.4, twoSided: true, noShadow: true },
  cabins: pic("cabins"),
  win_arch: { make: () => windowPic(false), rough: 0.3, alphaTest: 0.5, illum: 1.4 },
  win_round: { make: () => windowPic(true), rough: 0.3, alphaTest: 0.5, illum: 1.6 },
  buoy: { make: buoy, rough: 0.6, alphaTest: 0.5, twoSided: true, noShadow: true },
  // ---- tower and blocks
  concrete: { make: () => concrete({ base: [150, 148, 140], seed: 42, joints: 2, streaks: 1 }), rough: 0.9, normal: 1 },
  pipe_rail: { make: () => cutout(64, 64, (x, y) => y < 5 || Math.abs(y - 34) < 2.5 || gridLine(x, 64) < 3, () => [200, 204, 208]), rough: 0.25, metal: 0.8, alphaTest: 0.5, twoSided: true, noShadow: true },
  ladder_pic: { make: () => cutout(64, 64, (x, y) => x < 6 || x > 57 || gridLine(y, 32) < 3, () => [200, 204, 208]), rough: 0.25, metal: 0.8, alphaTest: 0.5, twoSided: true, noShadow: true },
  block: { make: () => concrete({ base: [228, 226, 216], seed: 43, streaks: 0.6 }), rough: 0.6 },
  block_top: pic("block_top", { twoSided: true }),
  // ---- the hall's signs and pictures
  sign_depth_deep: pic("sign_depth_deep"), sign_depth_shallow: pic("sign_depth_shallow"), sign_nodiving: pic("sign_nodiving"), sign_deep: pic("sign_deep"), sign_rules: pic("sign_rules"),
  sign_name: { make: nameBand, rough: 0.3 }, mosaic: { make: mosaic, rough: 0.3 },
  clock: { make: sign("clock", { cut: true }), rough: 0.4, alphaTest: 0.5, illum: 0.5, illumAll: true },
  scoreboard: pic("scoreboard"),
  skylight: { make: skylight, rough: 0.2, translucent: true, twoSided: true, noShadow: true },
  steel_rib: { make: () => steel([56, 80, 70], 44), rough: 0.6, metal: 0.3, twoSided: true },
  // ---- graffiti on the tiles (pieces of the street arena's art)
  graf_crew: graf("graf_crew", "p_kacrew"), graf_gol: graf("graf_gol", "p_gol"), graf_tags: graf("graf_tags", "t_tags_2"), graf_ball: graf("graf_ball", "c_ball"), graf_wild: graf("graf_wild", "a_wild"), graf_rei: graf("graf_rei", "t_tags_1"),
  // ---- what the players brought
  worklight: { make: () => steel([226, 184, 40], 45), rough: 0.5, metal: 0.2 },
  lamp_warm: { make: () => glow([255, 226, 180]), rough: 0.2, illum: 7, illumAll: true, noShadow: true, twoSided: true },
  generator: { make: () => steel([176, 52, 42], 46), rough: 0.5, metal: 0.3 },
  cable: { make: () => flat([18, 18, 20]), rough: 0.8, twoSided: true, noShadow: true },
  speaker: { make: speaker, rough: 0.7 },
  crate: { make: () => planks([176, 146, 104], 47, 32), rough: 0.9 },
};

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
if (!only || only.includes("sky_night")) {
  const sky = skyNight();
  for (const d of outDirs) sky.color.png(path.join(d, "sky_night_color.png"), 3);
  if (args.addon) fs.writeFileSync(path.join(args.addon, DIR, "sky_night.vmat"), `"Layer0"\n{\n\t"shader"\t"sky.vfx"\n\t"F_TEXTURE_FORMAT2"\t"0"\n\t"SkyTexture"\t"${DIR}/sky_night_color.png"\n\t"g_flBrightnessExposureBias"\t"0.000"\n\t"g_flRenderOnlyExposureBias"\t"0.000"\n\t"g_flRotation"\t"0.000"\n\t"g_flHorizonOffset"\t"0.000"\n}\n`);
  console.log("sky_night: 2048x1024");
}
if (previewFile) fs.writeFileSync(previewFile, JSON.stringify(preview, null, 1));
if (missing.length) console.log(`missing art (flat colour used): ${[...new Set(missing)].join(" ")}`);
