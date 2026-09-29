#!/usr/bin/env node
// "Arena Vision" crowd, test section (2026-09-29, v8 only): ~500 fan cards on
// the measured lower tier of the red end (+y), x -600..600, 11 rows (tread k:
// z = 107 + 20.7k, back edge y = 2064 + 36(k+1)). Cards face the pitch (-y).
// Fans come from a code-drawn atlas (own art, 16 variants x 2 poses), alpha
// tested like the 3D grass. 24 column bones; clips idle (bob), cheer (jumps,
// 2 Hz, staggered), wave (La Ola across the section, 3 s).
//
// usage: node tools/atmo/generate-crowd.mjs <addon content dir>
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import { meshDmx, animDmx, vmdlText } from "./dmx-lib.mjs";

const out = process.argv[2];
// --team blue: the same section mirrored to the blue end (-y), blue kits.
const BLUE = process.argv.includes("--team") && process.argv[process.argv.indexOf("--team") + 1] === "blue";
const SIDE = BLUE ? -1 : 1;
if (!out) { console.error("usage: generate-crowd.mjs <addon content dir>"); process.exit(1); }
const SOURCE = "tools/atmo/generate-crowd.mjs";
const MODEL = BLUE ? "models/soccermod/atmo/crowd_test_blue" : "models/soccermod/atmo/crowd_test";
const MAT = BLUE ? "materials/soccermod/atmo/crowd_fans_blue.vmat" : "materials/soccermod/atmo/crowd_fans.vmat";
const TEX = BLUE ? "crowd_fans_blue" : "crowd_fans";
const FPS = 30;

let seed = 20260929;
const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
const pick = (a) => a[Math.floor(rnd() * a.length)];

// ---- atlas: 16 columns x 2 rows of 64x128 cells (row 0 arms down, row 1 arms up) ----------
const AW = 1024, AH = 256, CW = 64, CH = 128;
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
const RED = BLUE ? [34, 78, 200] : [196, 34, 30], RED_DARK = BLUE ? [20, 48, 140] : [140, 22, 20], WHITE = [238, 238, 234], BLACK = [34, 36, 42], GREY = [120, 124, 130];
for (let v = 0; v < 16; v++) for (let pose = 0; pose < 2; pose++) {
  const ox = v * CW, oy = pose * CH;
  const skin = SKINS[v % 5], hair = HAIRS[(v * 3) % 5], style = v % 4, scarf = v % 3 === 0;
  const neutral = v >= 13;                     // a few neutral fans
  const shirt = neutral ? pick([WHITE, BLACK, GREY]) : style === 2 ? BLACK : RED;
  const sleeve = shirt;
  if (pose === 1) {
    line(ox, oy, 20, 58, 9, 21, 9, sleeve); line(ox, oy, 44, 58, 55, 21, 9, sleeve);
    circle(ox, oy, 9, 17, 5, skin); circle(ox, oy, 55, 17, 5, skin);
    if (scarf && !neutral) { rect(ox, oy, 7, 8, 50, 10, RED); for (let i = 0; i < 6; i++) rect(ox, oy, 10 + i * 8, 8, 3, 10, WHITE); }
  } else {
    line(ox, oy, 17, 58, 13, 92, 9, sleeve); line(ox, oy, 47, 58, 51, 92, 9, sleeve);
    circle(ox, oy, 13, 95, 4, skin); circle(ox, oy, 51, 95, 4, skin);
  }
  rect(ox, oy, 16, 52, 32, 56, shirt); circle(ox, oy, 24, 56, 8, shirt); circle(ox, oy, 40, 56, 8, shirt);
  if (style === 1 && !neutral) for (let i = 0; i < 3; i++) rect(ox, oy, 21 + i * 9, 54, 3, 52, WHITE);
  if (style === 2 && !neutral) rect(ox, oy, 16, 62, 32, 7, RED);
  rect(ox, oy, 19, 106, 26, 22, BLACK);
  rect(ox, oy, 28, 42, 8, 11, skin); circle(ox, oy, 32, 34, 11, skin);
  if (style === 3) { for (let j = -12; j <= 0; j++) for (let i = -12; i <= 12; i++) if (i * i + j * j <= 144) px(ox + 32 + i, oy + 31 + j, neutral ? BLACK : RED); rect(ox, oy, 32, 28, 16, 4, neutral ? BLACK : RED_DARK); }
  else { for (let j = -12; j <= -2; j++) for (let i = -12; i <= 12; i++) if (i * i + j * j <= 132) px(ox + 32 + i, oy + 31 + j, hair); if (v % 5 === 1) { rect(ox, oy, 21, 30, 4, 13, hair); rect(ox, oy, 39, 30, 4, 13, hair); } }
  if (scarf && pose === 0 && !neutral) { rect(ox, oy, 20, 46, 24, 7, RED); for (const x of [24, 32, 40]) rect(ox, oy, x, 46, 3, 7, WHITE); rect(ox, oy, 36, 50, 7, 20, RED); }
  rect(ox, oy, 27, 34, 3, 2, [40, 30, 25]); rect(ox, oy, 34, 34, 3, 2, [40, 30, 25]);
}
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
// Transparent pixels take the colour of a dark crowd background so mip-mapping does not halo.
writePng(path.join(matDir, `${TEX}_color.png`), 3, (x, y, c) => { const o = (y * AW + x) * 4; return rgba[o + 3] ? rgba[o + c] : [40, 34, 36][c]; });
writePng(path.join(matDir, `${TEX}_trans.png`), 1, (x, y) => rgba[(y * AW + x) * 4 + 3]);
fs.writeFileSync(path.join(matDir, `${TEX}.vmat`), `"Layer0"
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

// ---- section mesh ------------------------------------------------------------------------------
const X0 = -600, X1 = 600, ROWS = 11, SEAT = 26, FAN_W = 30, FAN_H = 60, COLS = 24;
const colOf = (x) => Math.min(COLS - 1, Math.floor((x - X0) / ((X1 - X0) / COLS)));
const bones = [{ name: "root", pos: [0, 0, 0] }];
for (let c = 0; c < COLS; c++) bones.push({ name: `col${c}`, pos: [X0 + (c + 0.5) * (X1 - X0) / COLS, SIDE * 2200, 200] });
const mesh = { name: "crowd_test", material: MAT, positions: [], normals: [], uvs: [], weights: [], indices: [], faces: [] };
let fans = 0;
for (let k = 0; k < ROWS; k++) {
  const z = 107 + 20.7 * k + 1, y = SIDE * (2064 + 36 * k + 24);
  for (let x = X0 + SEAT / 2; x < X1; x += SEAT) {
    if (rnd() < 0.04) continue;                       // a few empty seats
    const jx = x + (rnd() - 0.5) * 6, h = FAN_H * (0.92 + rnd() * 0.14), w = FAN_W * (0.92 + rnd() * 0.12);
    const variant = Math.floor(rnd() * 16), pose = rnd() < 0.18 ? 1 : 0;
    const u0 = variant / 16, u1 = (variant + 1) / 16, v0 = pose * 0.5, v1 = v0 + 0.5;
    const b = colOf(jx) + 1, base = mesh.positions.length;
    // quad in the XZ plane at this row's y, facing -y (towards the pitch)
    for (const [dx, dz, u, v] of [[-w / 2, 0, u0, v1], [w / 2, 0, u1, v1], [w / 2, h, u1, v0], [-w / 2, h, u0, v0]]) {
      mesh.positions.push([SIDE * (jx + dx), y, z + dz]); mesh.normals.push([0, -SIDE, 0]); mesh.uvs.push([u, v]);
      mesh.weights.push([1, 0]); mesh.indices.push([b, 0]);
    }
    mesh.faces.push([base, base + 3, base + 2, base + 1]);
    fans++;
  }
}

// ---- animation clips ---------------------------------------------------------------------------
const phase = bones.map(() => rnd() * Math.PI * 2);
function clip(seconds, fn) {
  const n = Math.round(seconds * FPS), frames = [];
  for (let f = 0; f <= n; f++) { const t = f / FPS, m = new Map(); for (let c = 1; c <= COLS; c++) { const d = fn(t, c - 1, phase[c], seconds); if (d) m.set(c, [0, 0, d]); } frames.push(m); }
  return frames;
}
const clips = [
  { name: "idle", looping: true, frames: clip(2.0, (t, c, ph) => 1.2 * Math.sin(2 * Math.PI * t / 2 + ph)) },
  { name: "cheer", looping: true, frames: clip(1.0, (t, c, ph) => 11 * Math.max(0, Math.sin(2 * Math.PI * 2 * t + ph))) },
  { name: "wave", looping: false, frames: clip(3.0, (t, c) => { const head = t / 3 * (COLS + 8) - 4; return 24 * Math.exp(-((c - head) ** 2) / 3.5); }) },
];

const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
write(`${MODEL}.dmx`, meshDmx(SOURCE, bones, mesh));
for (const c of clips) write(`${MODEL}_anims/${c.name}.dmx`, animDmx(SOURCE, bones, "crowd_test", c.name, c.frames, FPS));
write(`${MODEL}.vmdl`, vmdlText(MODEL, "crowd_test", [], clips));
console.log(`${MODEL}: ${fans} fans, ${mesh.positions.length} vertices, ${bones.length} bones, clips ${clips.map((c) => c.name).join(" ")}`);
