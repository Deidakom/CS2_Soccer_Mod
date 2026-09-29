#!/usr/bin/env node
// "Arena Vision" LED perimeter boards (v8 only). Owner 2026-09-29: rotating
// advertising like in football games, English fantasy brands with their own
// logos (no real brands), plus SoccerMod pages and goal takeovers in team
// colour. Everything is drawn here: each page is laid out on a 256 x 40 LED
// grid (logo + 5x7 dot-matrix font) and every grid cell becomes a round LED
// dot on a 1024 x 160 texture. One board model (256 x 40 units, dark frame),
// one material group (skin) per page; the plugin flips all boards together.
//
// usage: node tools/atmo/generate-boards.mjs <addon content dir>
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import { meshDmx, vmdlText } from "./dmx-lib.mjs";

const out = process.argv[2];
if (!out) { console.error("usage: generate-boards.mjs <addon content dir>"); process.exit(1); }
const MODEL = "models/soccermod/atmo/led_board";
const GW = 256, GH = 40, DOT = 4;           // LED grid and texture pixels per LED
const TW = GW * DOT, TH = GH * DOT;

// ---- 5x7 font -----------------------------------------------------------------------------------
const FONT = {
  A: "01110 10001 10001 11111 10001 10001 10001", B: "11110 10001 10001 11110 10001 10001 11110", C: "01111 10000 10000 10000 10000 10000 01111",
  D: "11110 10001 10001 10001 10001 10001 11110", E: "11111 10000 10000 11110 10000 10000 11111", F: "11111 10000 10000 11110 10000 10000 10000",
  G: "01111 10000 10000 10111 10001 10001 01111", H: "10001 10001 10001 11111 10001 10001 10001", I: "11111 00100 00100 00100 00100 00100 11111",
  J: "00111 00010 00010 00010 00010 10010 01100", K: "10001 10010 10100 11000 10100 10010 10001", L: "10000 10000 10000 10000 10000 10000 11111",
  M: "10001 11011 10101 10101 10001 10001 10001", N: "10001 11001 10101 10011 10001 10001 10001", O: "01110 10001 10001 10001 10001 10001 01110",
  P: "11110 10001 10001 11110 10000 10000 10000", Q: "01110 10001 10001 10001 10101 10010 01101", R: "11110 10001 10001 11110 10100 10010 10001",
  S: "01111 10000 10000 01110 00001 00001 11110", T: "11111 00100 00100 00100 00100 00100 00100", U: "10001 10001 10001 10001 10001 10001 01110",
  V: "10001 10001 10001 10001 10001 01010 00100", W: "10001 10001 10001 10101 10101 10101 01010", X: "10001 10001 01010 00100 01010 10001 10001",
  Y: "10001 10001 01010 00100 00100 00100 00100", Z: "11111 00001 00010 00100 01000 10000 11111", 0: "01110 10011 10101 10101 11001 10001 01110",
  1: "00100 01100 00100 00100 00100 00100 01110", 2: "01110 10001 00001 00110 01000 10000 11111", 3: "11110 00001 00001 01110 00001 00001 11110",
  4: "00010 00110 01010 10010 11111 00010 00010", 5: "11111 10000 11110 00001 00001 10001 01110", 6: "00110 01000 10000 11110 10001 10001 01110",
  7: "11111 00001 00010 00100 01000 01000 01000", 8: "01110 10001 10001 01110 10001 10001 01110", 9: "01110 10001 10001 01111 00001 00010 01100",
  "!": "00100 00100 00100 00100 00100 00000 00100", ".": "00000 00000 00000 00000 00000 00000 00100", "-": "00000 00000 00000 11111 00000 00000 00000",
  " ": "00000 00000 00000 00000 00000 00000 00000",
};

function page(bg) {
  const g = Array.from({ length: GH }, () => Array.from({ length: GW }, () => bg));
  const set = (x, y, c) => { x = Math.round(x); y = Math.round(y); if (x >= 0 && y >= 0 && x < GW && y < GH) g[y][x] = c; };
  const text = (str, x, y, c, scale = 1) => {
    let cx = x;
    for (const ch of str) {
      const rows = (FONT[ch] || FONT[" "]).split(" ");
      rows.forEach((r, ry) => [...r].forEach((bit, rx) => { if (bit === "1") for (let sy = 0; sy < scale; sy++) for (let sx = 0; sx < scale; sx++) set(cx + rx * scale + sx, y + ry * scale + sy, c); }));
      cx += 6 * scale;
    }
    return cx;
  };
  const width = (str, scale = 1) => str.length * 6 * scale - scale;
  const poly = (pts, c) => {
    const ys = pts.map((p) => p[1]), y0 = Math.floor(Math.min(...ys)), y1 = Math.ceil(Math.max(...ys));
    for (let y = y0; y <= y1; y++) for (let x = 0; x < GW; x++) {
      let inside = false;
      for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) { const [xi, yi] = pts[i], [xj, yj] = pts[j]; if ((yi > y + 0.5) !== (yj > y + 0.5) && x + 0.5 < (xj - xi) * (y + 0.5 - yi) / (yj - yi) + xi) inside = !inside; }
      if (inside) set(x, y, c);
    }
  };
  const disc = (cx, cy, r, c) => { for (let y = -r; y <= r; y++) for (let x = -r; x <= r; x++) if (x * x + y * y <= r * r + r * 0.6) set(cx + x, cy + y, c); };
  return { g, set, text, width, poly, disc };
}

const pages = [];
function brand(name, bg, fg, sub, logo) {
  const p = page(bg);
  logo(p);
  const scale = name.length > 8 ? 3 : 4;   // 4x only fits 8 letters after the logo
  // with a subline: big name in rows 2-30 (4x) or 5-26 (3x), subline in rows 32-38
  p.text(name, 52, sub ? (scale === 4 ? 2 : 5) : (GH - 7 * scale) / 2, fg, scale);
  if (sub) p.text(sub, 54, 32, [210, 210, 200], 1);
  const id = name.toLowerCase().replace(/[^a-z]/g, "");
  pages.push({ name: pages.some((q) => q.name === id) ? id + "_arena" : id, g: p.g });
}
// Fantasy brands (own designs). Logos sit in the 40 x 40 square on the left.
brand("KICKFUEL", [12, 10, 6], [255, 196, 32], "ENERGY DRINK", (p) => p.poly([[22, 3], [9, 22], [19, 22], [14, 37], [31, 15], [21, 15], [27, 3]], [255, 150, 20]));
brand("VOLTWAVE", [4, 10, 18], [80, 210, 255], "MOBILE - 6G READY", (p) => { for (let k = 0; k < 3; k++) for (let x = 4; x < 38; x++) { const y = 12 + k * 8 + Math.sin(x / 4 + k) * 3; p.set(x, y, [80, 210, 255]); p.set(x, y + 1, [80, 210, 255]); } });
brand("GOALCREST", [8, 8, 14], [240, 200, 90], "BANK - SAVE LIKE A KEEPER", (p) => { p.poly([[6, 5], [34, 5], [34, 20], [20, 36], [6, 20]], [200, 160, 60]); p.poly([[20, 10], [23, 17], [30, 17], [24, 22], [26, 29], [20, 25], [14, 29], [16, 22], [10, 17], [17, 17]], [30, 26, 16]); });
brand("TOPCORNER", [16, 8, 4], [255, 140, 40], "BURGERS", (p) => { for (let x = 7; x < 34; x++) { const t = (x - 7) / 26; p.set(x, 13 - Math.round(Math.sin(t * Math.PI) * 6), [255, 170, 70]); for (let y = 14 - Math.round(Math.sin(t * Math.PI) * 6); y < 16; y++) p.set(x, y, [230, 140, 50]); } for (let x = 6; x < 35; x++) { p.set(x, 19, [90, 200, 60]); p.set(x, 22, [140, 60, 30]); p.set(x, 23, [140, 60, 30]); p.set(x, 26, [230, 140, 50]); p.set(x, 27, [230, 140, 50]); } });
brand("PITCHLINE", [4, 8, 16], [200, 225, 255], "AIR - FLY TO THE FINAL", (p) => { p.poly([[4, 22], [20, 17], [30, 6], [33, 8], [27, 18], [37, 21], [36, 24], [26, 23], [22, 30], [19, 30], [20, 22], [5, 25]], [200, 225, 255]); });
brand("SOCCERMOD", [4, 14, 6], [255, 255, 255], "ARENA - CS2", (p) => { p.disc(20, 20, 15, [240, 240, 240]); p.poly([[20, 13], [26, 17], [24, 24], [16, 24], [14, 17]], [20, 20, 20]); for (const [x, y] of [[20, 7], [31, 15], [28, 30], [12, 30], [9, 15]]) p.disc(x, y, 2, [20, 20, 20]); });
{
  const p = page([10, 10, 10]);
  const msg = "PLAY FAIR - RESPECT";
  p.text(msg, (GW - p.width(msg, 2)) / 2, 13, [255, 255, 255], 2);
  pages.push({ name: "fairplay", g: p.g });
}
{
  const p = page([10, 10, 10]);
  for (let x = 0; x < GW; x++) for (let y = 0; y < GH; y++) if ((x + y) % 16 < 8 && (y < 4 || y > 35)) p.set(x, y, x < GW / 2 ? [220, 40, 30] : [40, 90, 230]);
  const msg = "SOCCERMOD";
  p.text(msg, (GW - p.width(msg, 4)) / 2, 6, [255, 255, 255], 4);
  pages.push({ name: "soccermod_banner", g: p.g });
}
for (const [team, c] of [["red", [230, 40, 30]], ["blue", [40, 100, 245]]]) {
  const p = page(c);
  for (let x = 0; x < GW; x += 128) p.text("GOAL!", x + 6, 6, [255, 255, 255], 4);
  pages.push({ name: `goal_${team}`, g: p.g });
}

// ---- textures: every grid cell -> a round LED dot with a faint glow ---------------------------------
const crcT = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
const crc = (b) => { let c = 0xffffffff; for (const v of b) c = crcT[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
const chunk = (t, d) => { const l = Buffer.alloc(4); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(t), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
function png(file, w, h, get) {
  const raw = Buffer.alloc((w * 3 + 1) * h);
  for (let y = 0; y < h; y++) { raw[y * (w * 3 + 1)] = 0; for (let x = 0; x < w; x++) { const c = get(x, y); for (let k = 0; k < 3; k++) raw[y * (w * 3 + 1) + 1 + x * 3 + k] = c[k]; } }
  const ih = Buffer.alloc(13); ih.writeUInt32BE(w, 0); ih.writeUInt32BE(h, 4); ih[8] = 8; ih[9] = 2;
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ih), chunk("IDAT", zlib.deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]));
}
const matDir = path.join(out, "materials/soccermod/atmo/boards");
const matOf = (name) => `materials/soccermod/atmo/boards/led_${name}.vmat`;
for (const pg of pages) {
  png(path.join(matDir, `led_${pg.name}_color.png`), TW, TH, (x, y) => {
    const gx = Math.floor(x / DOT), gy = Math.floor(y / DOT), c = pg.g[gy][gx];
    const dx = (x % DOT) - (DOT - 1) / 2, dy = (y % DOT) - (DOT - 1) / 2, r = Math.hypot(dx, dy);
    const k = r < 1.3 ? 1 : r < 2.0 ? 0.55 : 0.18;       // dot, rim, gap
    return [Math.round(c[0] * k), Math.round(c[1] * k), Math.round(c[2] * k)];
  });
  // Self-illuminated like an LED panel, lit the same everywhere (sun or roof shade).
  fs.writeFileSync(path.join(out, matOf(pg.name)), `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
\t"F_SELF_ILLUM"\t"1"
\t"F_RENDER_BACKFACES"\t"1"
\t"F_DO_NOT_CAST_SHADOWS"\t"1"
\t"g_flMetalness"\t"0.000"
\t"g_flSelfIllumAlbedoFactor"\t"1.000"
\t"g_flSelfIllumBrightness"\t"4.000"
\t"g_flSelfIllumScale"\t"1.000"
\t"g_vSelfIllumTint"\t"[1.000000 1.000000 1.000000 0.000000]"
\t"TextureColor"\t"materials/soccermod/atmo/boards/led_${pg.name}_color.png"
\t"TextureRoughness"\t"[0.600000 0.600000 0.600000 0.000000]"
}
`);
}
fs.writeFileSync(path.join(matDir, "led_frame.vmat"), `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
\t"F_RENDER_BACKFACES"\t"1"
\t"g_flMetalness"\t"0.200"
\t"TextureColor"\t"[0.060000 0.065000 0.070000 0.000000]"
\t"TextureRoughness"\t"[0.500000 0.500000 0.500000 0.000000]"
}
`);

// ---- model: front panel (page material) + dark back/frame, origin at the bottom centre, facing +x ----
// Board 320 wide (local y), 50 high (z), 6 deep (x). The plugin rotates it to face the pitch.
const W = 320, H = 50, D = 6;
const bones = [{ name: "root", pos: [0, 0, 0] }];
function boxMesh(material, quads) {
  const m = { name: "led_board", material, positions: [], normals: [], uvs: [], weights: [], indices: [], faces: [] };
  for (const q of quads) { const b = m.positions.length; for (const v of q.v) { m.positions.push(v.p); m.normals.push(q.n); m.uvs.push(v.uv); m.weights.push([1, 0]); m.indices.push([0, 0]); } m.faces.push([b, b + 1, b + 2, b + 3]); }
  return m;
}
const front = boxMesh(matOf(pages[0].name), [{ n: [1, 0, 0], v: [{ p: [0, -W / 2, 0], uv: [0, 1] }, { p: [0, -W / 2, H], uv: [0, 0] }, { p: [0, W / 2, H], uv: [1, 0] }, { p: [0, W / 2, 0], uv: [1, 1] }] }]);
const frameQuads = [
  { n: [-1, 0, 0], v: [{ p: [-D, W / 2, 0], uv: [0, 0] }, { p: [-D, W / 2, H + 2], uv: [0, 1] }, { p: [-D, -W / 2, H + 2], uv: [1, 1] }, { p: [-D, -W / 2, 0], uv: [1, 0] }] },
  { n: [0, 0, 1], v: [{ p: [0, -W / 2, H + 2], uv: [0, 0] }, { p: [-D, -W / 2, H + 2], uv: [0, 1] }, { p: [-D, W / 2, H + 2], uv: [1, 1] }, { p: [0, W / 2, H + 2], uv: [1, 0] }] },
  { n: [1, 0, 0], v: [{ p: [0.2, -W / 2, H], uv: [0, 0] }, { p: [0.2, -W / 2, H + 2], uv: [0, 1] }, { p: [0.2, W / 2, H + 2], uv: [1, 1] }, { p: [0.2, W / 2, H], uv: [1, 0] }] },
];
// Two meshes merged: page quad first, then the frame with its own material (second face set via a second file).
const MODEL_FRAME = `${MODEL}_frame`;
const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
write(`${MODEL}.dmx`, meshDmx("tools/atmo/generate-boards.mjs", bones, front));
write(`${MODEL_FRAME}.dmx`, meshDmx("tools/atmo/generate-boards.mjs", bones, boxMesh("materials/soccermod/atmo/boards/led_frame.vmat", frameQuads)));
const groups = pages.slice(1).map((pg) => ({ name: pg.name, remaps: [[matOf(pages[0].name), matOf(pg.name)]] }));
let vmdl = vmdlText(MODEL, "led_board", groups, []);
// second render mesh for the frame, and no animation list
vmdl = vmdl.replace(`\t\t\t\t\t{\n\t\t\t\t\t\t_class = "RenderMeshFile"\n\t\t\t\t\t\tname = "led_board"\n\t\t\t\t\t\tfilename = "${MODEL}.dmx"\n\t\t\t\t\t},`,
  `\t\t\t\t\t{\n\t\t\t\t\t\t_class = "RenderMeshFile"\n\t\t\t\t\t\tname = "led_board"\n\t\t\t\t\t\tfilename = "${MODEL}.dmx"\n\t\t\t\t\t},\n\t\t\t\t\t{\n\t\t\t\t\t\t_class = "RenderMeshFile"\n\t\t\t\t\t\tname = "led_frame"\n\t\t\t\t\t\tfilename = "${MODEL_FRAME}.dmx"\n\t\t\t\t\t},`);
vmdl = vmdl.replace(/\t\t\t\{\n\t\t\t\t_class = "AnimationList"[\s\S]*?\n\t\t\t\},\n/, "");
write(`${MODEL}.vmdl`, vmdl);
fs.writeFileSync(path.join(out, "materials/soccermod/atmo/boards/pages.json"), JSON.stringify(pages.map((p, i) => ({ skin: i, name: p.name })), null, 1));
console.log(`${MODEL}: ${pages.length} pages (skins): ${pages.map((p, i) => `${i}=${p.name}`).join(" ")}`);
