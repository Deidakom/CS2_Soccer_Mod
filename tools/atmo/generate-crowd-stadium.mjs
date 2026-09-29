#!/usr/bin/env node
// "Arena Vision" full stadium crowd (2026-09-29, v8 only). Owner: red fans
// behind the red goal (+y), blue behind the away goal (-y), the sides mixed
// with all kinds of colours like in real life. Every measured tread of both
// tiers gets fan cards (2 triangles each, alpha tested, one shared atlas):
//   lower tier: 11 rows, tread k at z 107 + 20.7k, inner edge |x| 1680 / |y| 2064 + 36k
//   upper tier: 11 rows, tread k at z 477 + 20.6k, inner edge |x| 1912 / |y| 2296 + 36k
//   straight parts end at |x| 1442 (ends) / |y| 1798 (sides) on the lower tier,
//   1494 / 1870 on the upper; 45-degree corner chamfers join them (in the side models).
//   stairs (no fans): |x| or |y| 285-395 and 975-1085.
// 8 models (ends x tiers, sides x tiers), each with 24 bones along its length
// and clips idle / cheer / wave, so the plugin can cheer an end or run a
// stadium-wide Mexican wave section by section.
//
// usage: node tools/atmo/generate-crowd-stadium.mjs <addon content dir>
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import { meshDmx, animDmx, vmdlText } from "./dmx-lib.mjs";

const out = process.argv[2];
if (!out) { console.error("usage: generate-crowd-stadium.mjs <addon content dir>"); process.exit(1); }
const SOURCE = "tools/atmo/generate-crowd-stadium.mjs";
const DIR = "models/soccermod/atmo/crowd";
const MAT = "materials/soccermod/atmo/crowd_stadium.vmat";
const TEX = "crowd_stadium";
const FPS = 30;

let seed = 20260930;
const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
const pick = (a) => a[Math.floor(rnd() * a.length)];

// ---- atlas: 16 variants x (3 groups x 2 poses) of 64x128 cells; groups red / blue / mixed -------
const AW = 1024, AH = 1024, CW = 64, CH = 128, VARIANTS = 16;
const rgba = new Uint8Array(AW * AH * 4);
function px(x, y, c) { x |= 0; y |= 0; if (x < 0 || y < 0 || x >= AW || y >= AH) return; const o = (y * AW + x) * 4; rgba[o] = c[0]; rgba[o + 1] = c[1]; rgba[o + 2] = c[2]; rgba[o + 3] = 255; }
function rect(ox, oy, x, y, w, h, c) { for (let j = 0; j < h; j++) for (let i = 0; i < w; i++) px(ox + x + i, oy + y + j, c); }
function circle(ox, oy, cx, cy, r, c) { for (let j = -r; j <= r; j++) for (let i = -r; i <= r; i++) if (i * i + j * j <= r * r) px(ox + cx + i, oy + cy + j, c); }
function line(ox, oy, x1, y1, x2, y2, w, c) {
  const minx = Math.min(x1, x2) - w, maxx = Math.max(x1, x2) + w, miny = Math.min(y1, y2) - w, maxy = Math.max(y1, y2) + w;
  const dx = x2 - x1, dy = y2 - y1, l2 = dx * dx + dy * dy;
  for (let y = miny; y <= maxy; y++) for (let x = minx; x <= maxx; x++) {
    const t = Math.max(0, Math.min(1, ((x - x1) * dx + (y - y1) * dy) / l2)), qx = x1 + t * dx - x, qy = y1 + t * dy - y;
    if (qx * qx + qy * qy <= (w / 2) ** 2) px(ox + x, oy + y, c);
  }
}
const SKINS = [[241, 199, 166], [224, 172, 133], [198, 138, 95], [141, 90, 59], [94, 58, 38]];
const HAIRS = [[29, 26, 24], [59, 42, 30], [107, 74, 43], [176, 138, 74], [125, 125, 125]];
const WHITE = [238, 238, 234], BLACK = [34, 36, 42], GREY = [120, 124, 130];
const GROUPS = [
  { name: "red", main: [196, 34, 30], dark: [140, 22, 20] },
  { name: "blue", main: [34, 78, 200], dark: [20, 48, 140] },
  { name: "mixed" },
];
// Everyday clothes for the neutral sides.
const CASUAL = [[196, 34, 30], [34, 78, 200], [110, 72, 44], BLACK, WHITE, [230, 196, 40], [40, 140, 70], GREY, [235, 120, 30], [26, 36, 80], [150, 40, 110], [90, 160, 200], [200, 180, 150], [70, 90, 60]];
GROUPS.forEach((g, gi) => {
  for (let v = 0; v < VARIANTS; v++) for (let pose = 0; pose < 2; pose++) {
    const ox = v * CW, oy = (gi * 2 + pose) * CH;
    const skin = SKINS[(v + gi) % 5], hair = HAIRS[(v * 3 + gi) % 5], style = v % 4;
    const mixed = g.name === "mixed";
    const neutral = mixed || v >= 13;
    const shirt = mixed ? CASUAL[(v * 5 + gi) % CASUAL.length] : neutral ? pick([WHITE, BLACK, GREY]) : style === 2 ? BLACK : g.main;
    const scarf = !neutral && v % 3 === 0;
    const trousers = mixed ? pick([BLACK, [40, 50, 80], [90, 80, 70], GREY]) : BLACK;
    if (pose === 1) {
      line(ox, oy, 20, 58, 9, 21, 9, shirt); line(ox, oy, 44, 58, 55, 21, 9, shirt);
      circle(ox, oy, 9, 17, 5, skin); circle(ox, oy, 55, 17, 5, skin);
      if (scarf) { rect(ox, oy, 7, 8, 50, 10, g.main); for (let i = 0; i < 6; i++) rect(ox, oy, 10 + i * 8, 8, 3, 10, WHITE); }
    } else {
      line(ox, oy, 17, 58, 13, 92, 9, shirt); line(ox, oy, 47, 58, 51, 92, 9, shirt);
      circle(ox, oy, 13, 95, 4, skin); circle(ox, oy, 51, 95, 4, skin);
    }
    rect(ox, oy, 16, 52, 32, 56, shirt); circle(ox, oy, 24, 56, 8, shirt); circle(ox, oy, 40, 56, 8, shirt);
    if (!neutral && style === 1) for (let i = 0; i < 3; i++) rect(ox, oy, 21 + i * 9, 54, 3, 52, WHITE);
    if (!neutral && style === 2) rect(ox, oy, 16, 62, 32, 7, g.main);
    if (mixed && style === 1) rect(ox, oy, 16, 70, 32, 6, pick(CASUAL));   // a print / stripe
    if (mixed && style === 2) { rect(ox, oy, 16, 52, 7, 56, pick([BLACK, [60, 60, 70]])); rect(ox, oy, 41, 52, 7, 56, pick([BLACK, [60, 60, 70]])); } // open jacket
    rect(ox, oy, 19, 106, 26, 22, trousers);
    rect(ox, oy, 28, 42, 8, 11, skin); circle(ox, oy, 32, 34, 11, skin);
    if (style === 3) {
      const cap = mixed ? pick(CASUAL) : neutral ? BLACK : g.main;
      for (let j = -12; j <= 0; j++) for (let i = -12; i <= 12; i++) if (i * i + j * j <= 144) px(ox + 32 + i, oy + 31 + j, cap);
      rect(ox, oy, 32, 28, 16, 4, mixed ? cap : neutral ? BLACK : g.dark);
    } else {
      for (let j = -12; j <= -2; j++) for (let i = -12; i <= 12; i++) if (i * i + j * j <= 132) px(ox + 32 + i, oy + 31 + j, hair);
      if (v % 5 === 1) { rect(ox, oy, 21, 30, 4, 13, hair); rect(ox, oy, 39, 30, 4, 13, hair); }
    }
    if (scarf && pose === 0) { rect(ox, oy, 20, 46, 24, 7, g.main); for (const x of [24, 32, 40]) rect(ox, oy, x, 46, 3, 7, WHITE); rect(ox, oy, 36, 50, 7, 20, g.main); }
    rect(ox, oy, 27, 34, 3, 2, [40, 30, 25]); rect(ox, oy, 34, 34, 3, 2, [40, 30, 25]);
  }
});
const crcT = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
const crc = (b) => { let c = 0xffffffff; for (const v of b) c = crcT[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
const chunk = (t, d) => { const l = Buffer.alloc(4); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(t), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
function writePng(file, channels, get) {
  const raw = Buffer.alloc((AW * channels + 1) * AH);
  for (let y = 0; y < AH; y++) { raw[y * (AW * channels + 1)] = 0; for (let x = 0; x < AW; x++) for (let c = 0; c < channels; c++) raw[y * (AW * channels + 1) + 1 + x * channels + c] = get(x, y, c); }
  const ih = Buffer.alloc(13); ih.writeUInt32BE(AW, 0); ih.writeUInt32BE(AH, 4); ih[8] = 8; ih[9] = channels === 3 ? 2 : 0;
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ih), chunk("IDAT", zlib.deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]));
}
const matDir = path.join(out, "materials/soccermod/atmo");
writePng(path.join(matDir, `${TEX}_color.png`), 3, (x, y, c) => { const o = (y * AW + x) * 4; return rgba[o + 3] ? rgba[o + c] : [40, 34, 36][c]; });
writePng(path.join(matDir, `${TEX}_trans.png`), 1, (x, y) => rgba[(y * AW + x) * 4 + 3]);
fs.writeFileSync(path.join(out, MAT), `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
\t"F_ALPHA_TEST"\t"1"
\t"F_RENDER_BACKFACES"\t"1"
\t"F_DO_NOT_CAST_SHADOWS"\t"1"
\t"g_flAlphaTestReference"\t"0.400"
\t"g_flMetalness"\t"0.000"
\t"TextureColor"\t"materials/soccermod/atmo/${TEX}_color.png"
\t"TextureTranslucency"\t"materials/soccermod/atmo/${TEX}_trans.png"
\t"TextureRoughness"\t"[0.850000 0.850000 0.850000 0.000000]"
}
`);

// ---- stand geometry -------------------------------------------------------------------------------
const TIERS = {
  lower: { z0: 107, rise: 20.7, innerX: 1680, innerY: 2064, endX: 1442, sideY: 1798, rows: 11 },
  upper: { z0: 477, rise: 20.6, innerX: 1912, innerY: 2296, endX: 1494, sideY: 1870, rows: 11 },
};
// Real tread surfaces of the stands (flat map triangles per row height, exported from the
// v8 world mesh; .local/crowd/v8_treads.json). 2026-09-29 owner: fans stood inside the
// stand in the corners - the chamfer rows were only an estimate. Every fan now has to stand
// on a real tread: straight parts are unchanged; elsewhere the spot is moved up to 20 u along
// the row normal onto the tread, or left empty.
const treads = JSON.parse(fs.readFileSync(new URL("../../.local/crowd/v8_treads.json", import.meta.url), "utf8"));
const treadGrid = new Map();
for (const [level, list] of Object.entries(treads)) {
  const grid = new Map();
  for (const t of list) {
    const xs = [t[0], t[2], t[4]], ys = [t[1], t[3], t[5]];
    for (let gx = Math.floor(Math.min(...xs) / 32); gx <= Math.floor(Math.max(...xs) / 32); gx++)
      for (let gy = Math.floor(Math.min(...ys) / 32); gy <= Math.floor(Math.max(...ys) / 32); gy++) {
        const key = gx + "," + gy; if (!grid.has(key)) grid.set(key, []); grid.get(key).push(t);
      }
  }
  treadGrid.set(Number(level), grid);
}
// nearest exported level (the export rounded 210.5 down, JS rounds it up)
const levelKey = (level) => [...treadGrid.keys()].reduce((a, b) => Math.abs(b - level) < Math.abs(a - level) ? b : a);
function onTread(level, x, y) {
  const cell = treadGrid.get(levelKey(level))?.get(Math.floor(x / 32) + "," + Math.floor(y / 32));
  if (!cell) return false;
  for (const [ax, ay, bx, by, cx, cy] of cell) {
    const d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy); if (Math.abs(d) < 1e-6) continue;
    const l1 = ((by - cy) * (x - cx) + (cx - bx) * (y - cy)) / d, l2 = ((cy - ay) * (x - cx) + (ax - cx) * (y - cy)) / d;
    if (l1 >= -1e-3 && l2 >= -1e-3 && 1 - l1 - l2 >= -1e-3) return true;
  }
  return false;
}
// the fan needs its whole footprint (a few units round the spot) on the tread
const standsOn = (level, x, y) => onTread(level, x, y) && onTread(level, x + 6, y) && onTread(level, x - 6, y) && onTread(level, x, y + 6) && onTread(level, x, y - 6);
function treadSpot(level, x, y, nx, ny) {
  if (standsOn(level, x, y)) return [x, y];
  for (let d = 2; d <= 20; d += 2) for (const s of [-1, 1]) {
    const px = x + nx * d * s, py = y + ny * d * s;
    if (standsOn(level, px, py)) return [px, py];
  }
  return null;
}
// 2026-09-29 owner: "some people are standing on the stairs". Aisle stairs have a half step
// about 10 u above each row (horizontal map triangles between the tread levels, exported to
// .local/crowd/v8_midsteps.json as [ax, ay, bx, by, cx, cy, z]). A fan whose footprint touches
// such a step of its own row is on a staircase and is left out.
const midSteps = JSON.parse(fs.readFileSync(new URL("../../.local/crowd/v8_midsteps.json", import.meta.url), "utf8"));
const midGrid = new Map();
for (const t of midSteps) {
  // stair steps only: small triangles (a step is ~100 x 18 u); big concourse floors do not count
  { const w = Math.max(t[0], t[2], t[4]) - Math.min(t[0], t[2], t[4]), d = Math.max(t[1], t[3], t[5]) - Math.min(t[1], t[3], t[5]); if (Math.max(w, d) > 120 || Math.min(w, d) > 30) continue; }
  const xs = [t[0], t[2], t[4]], ys = [t[1], t[3], t[5]];
  for (let gx = Math.floor(Math.min(...xs) / 32); gx <= Math.floor(Math.max(...xs) / 32); gx++)
    for (let gy = Math.floor(Math.min(...ys) / 32); gy <= Math.floor(Math.max(...ys) / 32); gy++) {
      const key = gx + "," + gy; if (!midGrid.has(key)) midGrid.set(key, []); midGrid.get(key).push(t);
    }
}
function onMidStep(level, x, y) {
  for (const [ax, ay, bx, by, cx, cy, z] of midGrid.get(Math.floor(x / 32) + "," + Math.floor(y / 32)) || []) {
    if (z < level - 14 || z > level + 34 || Math.abs(z - level) < 2) continue;   // the half steps next to this row
    const d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy); if (Math.abs(d) < 1e-6) continue;
    const l1 = ((by - cy) * (x - cx) + (cx - bx) * (y - cy)) / d, l2 = ((cy - ay) * (x - cx) + (ax - cx) * (y - cy)) / d;
    if (l1 >= 0 && l2 >= 0 && l1 + l2 <= 1) return true;
  }
  return false;
}
const onStairs = (level, x, y) => { for (const dx of [-12, 0, 12]) for (const dy of [-12, 0, 12]) if (onMidStep(level, x + dx, y + dy)) return true; return false; };
let movedFans = 0, droppedFans = 0, stairFans = 0;

// Side sections: about 20 % red-shirt (atlas variant 8 of the mixed row) and 20 % blue-shirt
// (variant 11) supporters, the rest random casual colours. One rnd() per fan either way, so the
// seat layout does not change.
const MIXED_RED = 8, MIXED_BLUE = 11, SET_NAMES = ["col", "red", "blue"];
const pickVariant = (group, r) => group !== 2 ? Math.floor(r * VARIANTS) : r < 0.2 ? MIXED_RED : r < 0.4 ? MIXED_BLUE : Math.floor((r - 0.4) / 0.6 * VARIANTS);
const DEPTH = 36, STAND_IN = 24, SEAT = 26, FAN_W = 30, FAN_H = 60, COLS = 24;
const inAisle = (s) => { const a = Math.abs(s); return (a > 285 && a < 395) || (a > 975 && a < 1085); };

// Row polyline for a section. end: +1/-1 = the +y/-y end (straight part only);
// side: +1/-1 = the +x/-x side including both corner chamfers.
function rowPolyline(t, k, kind, sign) {
  const off = DEPTH * k + STAND_IN;
  const X = t.innerX + off, Y = t.innerY + off;
  const ex = t.endX + off * 0.414, sy = t.sideY + off * 0.414;   // 45-degree chamfer grows outward
  // Every row runs anticlockwise (seen from above), so a wave clip travels the same way everywhere.
  if (kind === "end") return [[sign * ex, sign * Y], [-sign * ex, sign * Y]];
  return [[sign * ex, -sign * Y], [sign * X, -sign * sy], [sign * X, sign * sy], [sign * ex, sign * Y]];
}

function buildSection(name, tierName, kind, sign, group) {
  const t = TIERS[tierName];
  const mesh = { name, material: MAT, positions: [], normals: [], uvs: [], weights: [], indices: [], faces: [] };
  // the section's length (row 0) splits into 24 bone columns
  const poly0 = rowPolyline(t, 0, kind, sign);
  const len0 = poly0.slice(1).reduce((s, p, i) => s + Math.hypot(p[0] - poly0[i][0], p[1] - poly0[i][1]), 0);
  // 2026-09-29 owner: "when CT scores the blue fans cheer even more, the same for T" - the mixed
  // side sections get one column set per supporter colour (0 neutral, 1 red shirts, 2 blue shirts),
  // so a goal clip can make the scoring team's fans jump harder everywhere in the stadium.
  const sets = group === 2 ? 3 : 1;
  const bones = [{ name: "root", pos: [0, 0, 0] }];
  for (let set = 0; set < sets; set++) for (let c = 0; c < COLS; c++) bones.push({ name: set ? `${SET_NAMES[set]}${c}` : `col${c}`, pos: [0, 0, t.z0 + 100] });
  let fans = 0;
  for (let k = 0; k < t.rows; k++) {
    const z = t.z0 + t.rise * k + 1;
    const poly = rowPolyline(t, k, kind, sign);
    const segs = poly.slice(1).map((p, i) => ({ a: poly[i], b: p, len: Math.hypot(p[0] - poly[i][0], p[1] - poly[i][1]) }));
    const total = segs.reduce((s, g) => s + g.len, 0);
    let along = 0;
    for (const g of segs) {
      const tx = (g.b[0] - g.a[0]) / g.len, ty = (g.b[1] - g.a[1]) / g.len;
      // outward normal (away from the pitch) and the viewer's right when looking at the fan
      let nx = ty, ny = -tx;
      const mx = (g.a[0] + g.b[0]) / 2, my = (g.a[1] + g.b[1]) / 2;
      if (nx * mx + ny * my < 0) { nx = -nx; ny = -ny; }
      const rx = ny, ry = -nx;
      for (let s = SEAT / 2; s < g.len; s += SEAT) {
        const x = g.a[0] + tx * s, y = g.a[1] + ty * s;
        const straightCoord = kind === "end" ? x : Math.abs(nx) > 0.9 ? y : null;   // stairs on straight parts only
        if (straightCoord !== null && tierName === "lower" && inAisle(straightCoord)) continue;   // upper-tier aisles: onStairs (measured)
        if (rnd() < 0.035) continue;                                         // a few empty seats
        const h = FAN_H * (0.92 + rnd() * 0.14), w = FAN_W * (0.92 + rnd() * 0.12), j = (rnd() - 0.5) * 6;
        const level = tierName === "lower" ? Math.round(107 + 20.7 * k) : Math.round(477 + 20.6 * k);
        const spot = treadSpot(level, x + tx * j, y + ty * j, nx, ny);
        if (spot && onStairs(level, spot[0], spot[1])) { stairFans++; if (process.env.STAIRLOG) console.error("STAIR", name, k, Math.round(spot[0]), Math.round(spot[1])); continue; }
        if (!spot) { droppedFans++; if (process.env.DROPLOG) console.error("DROP", name, k, Math.round(x), Math.round(y)); continue; }
        if (Math.hypot(spot[0] - (x + tx * j), spot[1] - (y + ty * j)) > 0.1) movedFans++;
        const [cx, cy] = spot;
        const variant = pickVariant(group, rnd()), pose = rnd() < 0.18 ? 1 : 0;
        const u0 = variant / VARIANTS, u1 = (variant + 1) / VARIANTS;
        const row = group * 2 + pose, v0 = row * CH / AH, v1 = (row + 1) * CH / AH;
        const col = Math.min(COLS - 1, Math.floor(((along + s) / total) * COLS));
        const set = group === 2 ? (variant === MIXED_RED ? 1 : variant === MIXED_BLUE ? 2 : 0) : 0;
        const b = set * COLS + col + 1, base = mesh.positions.length;
        for (const [dr, dz, u, v] of [[-w / 2, 0, u0, v1], [w / 2, 0, u1, v1], [w / 2, h, u1, v0], [-w / 2, h, u0, v0]]) {
          mesh.positions.push([cx + rx * dr, cy + ry * dr, z + dz]); mesh.normals.push([-nx, -ny, 0]); mesh.uvs.push([u, v]);
          mesh.weights.push([1, 0]); mesh.indices.push([b, 0]);
        }
        mesh.faces.push([base, base + 3, base + 2, base + 1]);
        fans++;
      }
      along += g.len;
    }
  }
  // bone positions: the middle of each column on row 0 (only used as pivots; the clips move them in z)
  let acc = 0; const segs0 = poly0.slice(1).map((p, i) => ({ a: poly0[i], b: p, len: Math.hypot(p[0] - poly0[i][0], p[1] - poly0[i][1]) }));
  for (let c = 0; c < COLS; c++) {
    let d = (c + 0.5) / COLS * len0; acc = 0;
    for (const g of segs0) { if (d <= acc + g.len) { const f = (d - acc) / g.len; for (let set = 0; set < sets; set++) bones[set * COLS + c + 1].pos = [g.a[0] + (g.b[0] - g.a[0]) * f, g.a[1] + (g.b[1] - g.a[1]) * f, t.z0 + 100]; break; } acc += g.len; }
  }
  return { mesh, bones, fans };
}

// group 0 red end, 1 blue end, 2 mixed side. Bone b > 0: column (b - 1) % COLS, colour set (b - 1) / COLS.
function clips(bones, group) {
  const phase = bones.map(() => rnd() * Math.PI * 2);
  const clip = (seconds, fn) => {
    const n = Math.round(seconds * FPS), frames = [];
    for (let f = 0; f <= n; f++) {
      const t = f / FPS, m = new Map();
      for (let b = 1; b < bones.length; b++) { const d = fn(t, (b - 1) % COLS, phase[b], Math.floor((b - 1) / COLS)); if (d) m.set(b, [0, 0, d]); }
      frames.push(m);
    }
    return frames;
  };
  const idle = (t, c, ph) => 1.2 * Math.sin(2 * Math.PI * t / 2 + ph);
  const cheer = (t, c, ph) => 11 * Math.max(0, Math.sin(2 * Math.PI * 2 * t + ph));
  // the scoring team's own fans: higher, faster jumps (loop 2 s = 5 jumps at 2.5 Hz)
  const ecstatic = (t, c, ph) => 22 * Math.abs(Math.sin(Math.PI * 2.5 * t + ph));
  // goal clips: team = 0 red, 1 blue. Own end / own-colour side fans ecstatic, neutral side fans
  // cheer, the other team's fans stand still (a slow, small sag).
  const goal = (team) => (t, c, ph, set) => {
    const own = group === team || (group === 2 && set === team + 1);
    const other = group === 1 - team || (group === 2 && set === 2 - team);
    return own ? ecstatic(t, c, ph) : other ? 0.6 * Math.sin(2 * Math.PI * t / 2 + ph) - 0.6 : cheer(t, c, ph);
  };
  return [
    { name: "idle", looping: true, frames: clip(2.0, idle) },
    { name: "cheer", looping: true, frames: clip(1.0, cheer) },
    { name: "wave", looping: false, frames: clip(2.0, (t, c) => { const head = t / 2 * (COLS + 8) - 4; return 24 * Math.exp(-((c - head) ** 2) / 3.5); }) },
    { name: "goal_red", looping: true, frames: clip(2.0, goal(0)) },
    { name: "goal_blue", looping: true, frames: clip(2.0, goal(1)) },
  ];
}

const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
// group: 0 red, 1 blue, 2 mixed. Red (T, home) fans behind +y.
const SECTIONS = [
  ["end_red_lower", "lower", "end", 1, 0], ["end_red_upper", "upper", "end", 1, 0],
  ["end_blue_lower", "lower", "end", -1, 1], ["end_blue_upper", "upper", "end", -1, 1],
  ["side_east_lower", "lower", "side", 1, 2], ["side_east_upper", "upper", "side", 1, 2],
  ["side_west_lower", "lower", "side", -1, 2], ["side_west_upper", "upper", "side", -1, 2],
];
let totalFans = 0;
for (const [name, tier, kind, sign, group] of SECTIONS) {
  const { mesh, bones, fans } = buildSection(name, tier, kind, sign, group);
  const model = `${DIR}/${name}`;
  const cl = clips(bones, group);
  write(`${model}.dmx`, meshDmx(SOURCE, bones, mesh));
  for (const c of cl) write(`${model}_anims/${c.name}.dmx`, animDmx(SOURCE, bones, name, c.name, c.frames, FPS));
  write(`${model}.vmdl`, vmdlText(model, name, [], cl));
  totalFans += fans;
  console.log(`${model}: ${fans} fans, ${mesh.positions.length} vertices`);
}
console.log(`total ${totalFans} fans (moved onto a tread: ${movedFans}, dropped - no tread: ${droppedFans}, on stairs: ${stairFans})`);
