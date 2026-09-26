#!/usr/bin/env node
// Our own textures for the XSL-style stadium (Part G). They are generated from
// scratch to match the look of the XSL stadium's custom textures (colour,
// grain, streak pattern and size measured from the originals); no pixel of
// the originals is used.
//
//   node tools/xsl/generate-xsl-textures.mjs [--out <dir>] [--preview <dir>]
// Writes <out>/<name>_color.png (and _trans.png for glass).
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => {
  if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
const out = args.out ??
  "E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/content/csgo_addons/cs2sm_stadium_v1/materials/soccermod_xsl";

// ---- tileable value noise -------------------------------------------------------
function makeNoise(seed) {
  let s = seed >>> 0 || 1;
  const rnd = () => { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return s / 0x100000000; };
  const grids = new Map();
  const grid = (n) => { if (!grids.has(n)) grids.set(n, Float32Array.from({ length: n * n }, rnd)); return grids.get(n); };
  // periodic 2D value noise with nx x ny cells over the unit square
  return (u, v, nx, ny = nx) => {
    const g = grid(Math.max(nx, ny) * 2);
    const n = Math.max(nx, ny) * 2;
    const x = u * nx, y = v * ny, x0 = Math.floor(x), y0 = Math.floor(y), fx = x - x0, fy = y - y0;
    const at = (i, j) => g[((((j % ny) + ny) % ny) * n) + (((i % nx) + nx) % nx)];
    const sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
    const a = at(x0, y0) + (at(x0 + 1, y0) - at(x0, y0)) * sx;
    const b = at(x0, y0 + 1) + (at(x0 + 1, y0 + 1) - at(x0, y0 + 1)) * sx;
    return a + (b - a) * sy;
  };
}
const fbm = (noise, u, v, base, octaves, stretchY = 1) => {
  let sum = 0, amp = 1, norm = 0, f = base;
  for (let o = 0; o < octaves; o++) { sum += amp * noise(u, v, f, Math.max(1, Math.round(f / stretchY))); norm += amp; amp *= 0.5; f *= 2; }
  return sum / norm;
};

// ---- recipes -------------------------------------------------------------------------
// Painted metal: base colour, vertical wear streaks (noise stretched along Y),
// soft blotches and a fine grain - the pattern of XSL's METAL_* textures.
function paintedMetal(size, rgb, seed) {
  const n = makeNoise(seed);
  return (x, y) => {
    const u = x / size, v = y / size;
    const streak = fbm(n, u, v, 24, 4, 12) - 0.5;          // thin, long vertical streaks
    const blotch = fbm(n, u + 0.37, v + 0.11, 4, 3) - 0.5;
    const grain = n(u, v, size / 2) - 0.5;
    // worn paint: light scuffs where fine, vertically stretched noise peaks
    const scuffNoise = fbm(n, u + 0.13, v + 0.71, 32, 3, 4);
    const scuff = Math.max(0, scuffNoise - 0.68) * 2.2;
    // hair-line vertical scratches
    const scratch = Math.max(0, n(u + 0.5, v, Math.round(size / 3), 3) - 0.86) * 3.0;
    const k = 1 + streak * 0.22 + blotch * 0.12 + grain * 0.08 + scuff * 0.22 + scratch * 0.18;
    return rgb.map((c) => c * k);
  };
}
// Fine grain (XSL's SCH / VITRE_SALE: an even, woven-looking grey).
function grain(size, rgb, seed, strength = 0.1, gridOn = 0) {
  const n = makeNoise(seed);
  return (x, y) => {
    const u = x / size, v = y / size;
    const fine = n(u, v, size / 2) - 0.5, weave = ((x + y) % 2 === 0 ? 1 : -1) * 0.02;
    // faint seat grid (16 px cells, as in XSL SCH)
    const cell = (x % 16 === 0 || y % 16 === 0) ? -0.05 : 0;
    const k = 1 + fine * strength + weave + cell * gridOn + (fbm(n, u, v, 8, 2) - 0.5) * 0.06;
    return rgb.map((c) => c * k);
  };
}
// Rough plaster / concrete ceiling: mottled cells over a light grey.
function plaster(size, rgb, seed) {
  const n = makeNoise(seed);
  return (x, y) => {
    const u = x / size, v = y / size;
    const cells = Math.abs(fbm(n, u, v, 48, 3) - 0.5) * 2;   // pitted look
    // small distinct pits: dark core, light rim on the lower-right edge
    const p = fbm(n, u, v, 64, 2), pr = fbm(n, u - 1.5 / size, v - 1.5 / size, 64, 2);
    const pit = Math.max(0, p - 0.6) * 3.2, rim = Math.max(0, pr - 0.6) * 3.2 - pit;
    const k = 1 - cells * 0.1 - pit * 0.14 + Math.max(0, rim) * 0.1
      + (fbm(n, u + 0.5, v, 6, 3) - 0.5) * 0.1 + (n(u, v, size / 2) - 0.5) * 0.06;
    return rgb.map((c) => c * k);
  };
}
const flat = (rgb, seed, size) => grain(size, rgb, seed, 0.04);

// Roof truss (XSL uses CS:S assault_trainstation_truss): a steel lattice -
// top and bottom chords, verticals at the edges and one diagonal - over
// painted metal. The holes come from truss_trans.png (alpha test).
const TRUSS = 256, BAR = 18;
function trussMask(x, y) {
  const d = Math.abs(x - y);                                   // one diagonal, corner to corner
  return y < BAR || y >= TRUSS - BAR || x < BAR || x >= TRUSS - BAR || d < BAR * 0.75;
}
function truss(size, rgb, seed) {
  const metal = paintedMetal(size, rgb, seed);
  return (x, y) => {
    if (!trussMask(x, y)) return rgb.map((c) => c * 0.5);    // hidden by alpha; dark avoids halos
    const edge = Math.min(y % (TRUSS - BAR), x % (TRUSS - BAR)) < 2 ? 0.85 : 1;
    return metal(x, y).map((c) => c * edge);
  };
}
// Tiled metal floor (XSL: citadel_tilefloor016a): 64 px square plates with
// dark seams and a faint diamond tread.
function tileFloor(size, rgb, seed) {
  const n = makeNoise(seed);
  return (x, y) => {
    const u = x / size, v = y / size;
    const seam = (x % 64 < 2 || y % 64 < 2) ? 0.62 : 1;
    const tread = (((x + y) % 16 < 2) || ((x - y + 1024) % 16 < 2)) ? 1.06 : 1;
    const k = seam * tread * (1 + (fbm(n, u, v, 8, 3) - 0.5) * 0.12 + (n(u, v, size / 2) - 0.5) * 0.06);
    return rgb.map((c) => c * k);
  };
}

const textures = [
  // name, size, recipe, xsl original it replaces
  ["metal_grey", 512, paintedMetal(512, [54, 59, 66], 11), "SOCCER/METAL_GREY"],
  ["metal_red", 512, paintedMetal(512, [95, 46, 46], 12), "SOCCER/METAL_RED"],
  ["metal_blue", 512, paintedMetal(512, [46, 66, 95], 13), "SOCCER/METAL_BLUE"],
  ["metal_greygreen", 512, paintedMetal(512, [58, 66, 62], 14), "SOCCER/METAL_GREYGREEN"],
  ["blackmetal", 256, paintedMetal(256, [44, 45, 47], 15), "SOCCER/BLACKMETAL"],
  ["grey_wall", 512, paintedMetal(512, [52, 51, 52], 16), "SOCCER/GREY_WALL"],
  ["sch", 128, grain(128, [98, 98, 94], 17, 0.1, 1), "SOCCER/SCH"],
  ["vitre_sale", 128, grain(128, [85, 87, 83], 18), "SOCCER/VITRE_SALE"],
  ["ceiling", 512, plaster(512, [160, 164, 159], 19), "SOCCER/CEILING"],
  ["glass_clear", 256, flat([214, 240, 233], 20, 256), "SOCCER/GLASS_CLEAR"],
  ["metal_railing", 128, paintedMetal(128, [150, 152, 155], 21), "SOCCER/METAL_RAILING"],
  ["truss", TRUSS, truss(TRUSS, [72, 74, 78], 22), "CS_ASSAULT/ASSAULT_TRAINSTATION_TRUSS_01A/C"],
  ["tilefloor", 256, tileFloor(256, [84, 86, 88], 23), "METAL/CITADEL_TILEFLOOR016A"],
];
// Material kind per texture: glass is see-through, the truss is cut out.
const KIND = { glass_clear: "translucent", truss: "alphatest" };

function png(w, h, pixel) {
  const raw = Buffer.alloc((w * 3 + 1) * h);
  for (let y = 0; y < h; y++) {
    raw[y * (w * 3 + 1)] = 0;
    for (let x = 0; x < w; x++) {
      const c = pixel(x, y), o = y * (w * 3 + 1) + 1 + x * 3;
      for (let i = 0; i < 3; i++) raw[o + i] = Math.max(0, Math.min(255, Math.round(c[i])));
    }
  }
  const t = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
  const crc = (d) => { let c = 0xffffffff; for (const v of d) c = t[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const ch = (ty, d) => { const l = Buffer.alloc(4); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(ty), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
  const ih = Buffer.alloc(13); ih.writeUInt32BE(w, 0); ih.writeUInt32BE(h, 4); ih[8] = 8; ih[9] = 2;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), ch("IHDR", ih), ch("IDAT", zlib.deflateSync(raw, { level: 9 })), ch("IEND", Buffer.alloc(0))]);
}

fs.mkdirSync(out, { recursive: true });
for (const [name, size, recipe, original] of textures) {
  const file = path.join(out, `${name}_color.png`);
  fs.writeFileSync(file, png(size, size, recipe));
  let r = 0, g = 0, b = 0;
  for (let y = 0; y < size; y += 4) for (let x = 0; x < size; x += 4) { const c = recipe(x, y); r += c[0]; g += c[1]; b += c[2]; }
  const n = (size / 4) ** 2;
  console.log(`${name.padEnd(16)} ${size}x${size}  avg ${Math.round(r / n)},${Math.round(g / n)},${Math.round(b / n)}  (for ${original})`);
}
// Glass: mostly transparent. Truss: the lattice mask.
fs.writeFileSync(path.join(out, "glass_clear_trans.png"), png(16, 16, () => [70, 70, 70]));
fs.writeFileSync(path.join(out, "truss_trans.png"), png(TRUSS, TRUSS, (x, y) => trussMask(x, y) ? [255, 255, 255] : [0, 0, 0]));

// One csgo_complex material per texture, next to it (materials/soccermod_xsl/).
const rel = (name, suffix) => `materials/soccermod_xsl/${name}_${suffix}.png`;
for (const [name] of textures) {
  const kind = KIND[name];
  const lines = [
    '"Layer0"', "{",
    '\t"shader"\t"csgo_complex.vfx"',
    ...(kind === "translucent" ? ['\t"F_TRANSLUCENT"\t"1"', `\t"TextureTranslucency"\t"${rel(name, "trans")}"`] : []),
    ...(kind === "alphatest" ? ['\t"F_ALPHA_TEST"\t"1"', '\t"F_RENDER_BACKFACES"\t"1"', '\t"g_flAlphaTestReference"\t"0.500"', `\t"TextureTranslucency"\t"${rel(name, "trans")}"`] : []),
    `\t"TextureColor"\t"${rel(name, "color")}"`,
    `\t"TextureRoughness"\t"[${name.startsWith("glass") ? "0.100000 0.100000 0.100000" : "0.800000 0.800000 0.800000"} 0.000000]"`,
    "}", "",
  ];
  fs.writeFileSync(path.join(out, `${name}.vmat`), lines.join("\n"));
}
console.log(`wrote ${textures.length} materials to ${out}`);
