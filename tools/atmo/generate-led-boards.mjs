#!/usr/bin/env node
// "Arena Vision" LED boards v2 (2026-09-29 owner: "like real advertising boards", brand -
// text - brand - text, pixel look, pixel transition). Input: the 1280 x 200 pages from
// tools/atmo/render-board-pages.ps1. Each page is sampled onto the board's LED grid
// (320 x 50 LEDs, 4 x 4 source pixels per LED) and drawn back as round LED dots with dark
// gaps (texture 1280 x 200: close up you see the pixels, far away they blend). The pixel
// transition needs frames: every page also as "70 % of the LEDs lit" and "30 % lit" (one
// fixed random value per LED, so the same LEDs switch in every frame), plus one dark board.
// Skins: pages 0-9, page + 10 = 70 %, page + 20 = 30 %, 30 = dark (AtmoBoards.cs).
//
// usage: node tools/atmo/generate-led-boards.mjs <addon content dir> <pages dir>
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import { meshDmx, vmdlText } from "./dmx-lib.mjs";

const out = process.argv[2], pagesDir = process.argv[3];
if (!out || !pagesDir) { console.error("usage: generate-led-boards.mjs <addon content dir> <pages dir>"); process.exit(1); }
// another page set under another name (tools/brands): --pages a,b,c --model <model path without .vmdl> --mats <material dir>
const opt = Object.fromEntries(process.argv.slice(4).reduce((p, a, i, all) => { if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
const PAGES = (opt.pages ?? "kickfuel,voltwave,goalcrest,topcorner,pitchline,soccermod,fairplay,respect,goal_red,goal_blue").split(",");
const GW = 320, GH = 50, DOT = 4, TW = GW * DOT, TH = GH * DOT;
const MODEL = opt.model ?? "models/soccermod/atmo/led_board", MATS = opt.mats ?? "materials/soccermod/atmo/boards";

// ---- PNG in (8-bit RGB / RGBA) and out ----------------------------------------------------
function readPng(file) {
  const b = fs.readFileSync(file); let o = 8, w = 0, h = 0, ct = 0; const idat = [];
  while (o < b.length) {
    const len = b.readUInt32BE(o), type = b.toString("ascii", o + 4, o + 8), d = b.subarray(o + 8, o + 8 + len);
    if (type === "IHDR") { w = d.readUInt32BE(0); h = d.readUInt32BE(4); ct = d[9]; }
    if (type === "IDAT") idat.push(d);
    o += 12 + len;
  }
  const ch = { 2: 3, 6: 4 }[ct]; if (!ch) throw new Error(`${file}: colour type ${ct} not supported`);
  const stride = w * ch, raw = zlib.inflateSync(Buffer.concat(idat)), px = Buffer.alloc(h * stride);
  for (let y = 0; y < h; y++) {
    const ft = raw[y * (stride + 1)], src = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
    const cur = px.subarray(y * stride, (y + 1) * stride), prev = y ? px.subarray((y - 1) * stride, y * stride) : Buffer.alloc(stride);
    for (let x = 0; x < stride; x++) {
      const a = x >= ch ? cur[x - ch] : 0, up = prev[x], c = x >= ch ? prev[x - ch] : 0; let v = src[x];
      if (ft === 1) v += a; else if (ft === 2) v += up; else if (ft === 3) v += (a + up) >> 1;
      else if (ft === 4) { const p = a + up - c, pa = Math.abs(p - a), pb = Math.abs(p - up), pc = Math.abs(p - c); v += pa <= pb && pa <= pc ? a : pb <= pc ? up : c; }
      cur[x] = v & 255;
    }
  }
  return { w, h, get: (x, y, k) => px[y * stride + x * ch + k] };
}
const crcT = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
const crc = (b) => { let c = 0xffffffff; for (const v of b) c = crcT[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
const chunk = (t, d) => { const l = Buffer.alloc(4); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(t), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
function writePng(file, get) {
  const raw = Buffer.alloc((TW * 3 + 1) * TH);
  for (let y = 0; y < TH; y++) for (let x = 0; x < TW; x++) { const c = get(x, y); raw.set(c, y * (TW * 3 + 1) + 1 + x * 3); }
  const ih = Buffer.alloc(13); ih.writeUInt32BE(TW, 0); ih.writeUInt32BE(TH, 4); ih[8] = 8; ih[9] = 2;
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ih), chunk("IDAT", zlib.deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]));
}

// ---- LED grid ----------------------------------------------------------------------------
let seed = 20260929;
const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
const ledRandom = Float32Array.from({ length: GW * GH }, rnd);     // the dissolve order, shared by all pages
function ledColours(page) {
  const img = readPng(path.join(pagesDir, `${page}.png`));
  const sx = img.w / GW, sy = img.h / GH, leds = new Array(GW * GH);
  for (let gy = 0; gy < GH; gy++) for (let gx = 0; gx < GW; gx++) {
    const acc = [0, 0, 0]; let n = 0;
    for (let y = Math.floor(gy * sy); y < Math.floor((gy + 1) * sy); y++) for (let x = Math.floor(gx * sx); x < Math.floor((gx + 1) * sx); x++) { for (let k = 0; k < 3; k++) acc[k] += img.get(x, y, k); n++; }
    leds[gy * GW + gx] = acc.map((v) => v / n);
  }
  return leds;
}
// round LED dot + dim rim + dark gap (the pixel structure of a real board)
const dotLevel = (x, y) => { const dx = (x % DOT) - (DOT - 1) / 2, dy = (y % DOT) - (DOT - 1) / 2, r = Math.hypot(dx, dy); return r < 1.3 ? 1 : r < 2.0 ? 0.55 : 0.16; };
const OFF = [22, 22, 24];                                           // an unlit LED
function ledTexture(file, leds, litShare) {
  writePng(file, (x, y) => {
    const i = Math.floor(y / DOT) * GW + Math.floor(x / DOT), lit = litShare >= 1 || ledRandom[i] < litShare;
    const c = lit ? leds[i] : OFF, k = dotLevel(x, y);
    return [Math.round(c[0] * k), Math.round(c[1] * k), Math.round(c[2] * k)];
  });
}

const matDir = path.join(out, MATS);
fs.rmSync(matDir, { recursive: true, force: true });
const skins = [];
const vmat = (name) => `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
\t"F_SELF_ILLUM"\t"1"
\t"F_RENDER_BACKFACES"\t"1"
\t"F_DO_NOT_CAST_SHADOWS"\t"1"
\t"g_flMetalness"\t"0.000"
\t"g_flSelfIllumAlbedoFactor"\t"1.000"
\t"g_flSelfIllumBrightness"\t"3.200"
\t"g_flSelfIllumScale"\t"1.000"
\t"g_vSelfIllumTint"\t"[1.000000 1.000000 1.000000 0.000000]"
\t"TextureColor"\t"${MATS}/${name}_color.png"
\t"TextureRoughness"\t"[0.600000 0.600000 0.600000 0.000000]"
}
`;
const colours = PAGES.map(ledColours);
for (const [suffix, share] of [["", 1], ["_70", 0.7], ["_30", 0.3]])
  PAGES.forEach((page, p) => {
    const name = `led_${page}${suffix}`;
    ledTexture(path.join(matDir, `${name}_color.png`), colours[p], share);
    fs.writeFileSync(path.join(matDir, `${name}.vmat`), vmat(name));
    skins.push(name);
  });
ledTexture(path.join(matDir, "led_dark_color.png"), colours[0], 0);
fs.writeFileSync(path.join(matDir, "led_dark.vmat"), vmat("led_dark"));
skins.push("led_dark");
fs.writeFileSync(path.join(matDir, "led_frame.vmat"), `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
\t"F_RENDER_BACKFACES"\t"1"
\t"g_flMetalness"\t"0.200"
\t"TextureColor"\t"[0.060000 0.065000 0.070000 0.000000]"
\t"TextureRoughness"\t"[0.500000 0.500000 0.500000 0.000000]"
}
`);

// ---- model: 320 x 50 front panel (skin material) + dark frame, facing +x --------------------
const W = 320, H = 50, D = 6;
const bones = [{ name: "root", pos: [0, 0, 0] }];
function boxMesh(material, quads) {
  const m = { name: "led_board", material, positions: [], normals: [], uvs: [], weights: [], indices: [], faces: [] };
  for (const q of quads) { const b = m.positions.length; for (const v of q.v) { m.positions.push(v.p); m.normals.push(q.n); m.uvs.push(v.uv); m.weights.push([1, 0]); m.indices.push([0, 0]); } m.faces.push([b, b + 1, b + 2, b + 3]); }
  return m;
}
const matOf = (name) => `${MATS}/${name}.vmat`;
const front = boxMesh(matOf(skins[0]), [{ n: [1, 0, 0], v: [{ p: [0, -W / 2, 0], uv: [0, 1] }, { p: [0, -W / 2, H], uv: [0, 0] }, { p: [0, W / 2, H], uv: [1, 0] }, { p: [0, W / 2, 0], uv: [1, 1] }] }]);
const frameQuads = [
  { n: [-1, 0, 0], v: [{ p: [-D, W / 2, 0], uv: [0, 0] }, { p: [-D, W / 2, H + 2], uv: [0, 1] }, { p: [-D, -W / 2, H + 2], uv: [1, 1] }, { p: [-D, -W / 2, 0], uv: [1, 0] }] },
  { n: [0, 0, 1], v: [{ p: [0, -W / 2, H + 2], uv: [0, 0] }, { p: [-D, -W / 2, H + 2], uv: [0, 1] }, { p: [-D, W / 2, H + 2], uv: [1, 1] }, { p: [0, W / 2, H + 2], uv: [1, 0] }] },
  { n: [1, 0, 0], v: [{ p: [0.2, -W / 2, H], uv: [0, 0] }, { p: [0.2, -W / 2, H + 2], uv: [0, 1] }, { p: [0.2, W / 2, H + 2], uv: [1, 1] }, { p: [0.2, W / 2, H], uv: [1, 0] }] },
];
const MODEL_FRAME = `${MODEL}_frame`;
const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
write(`${MODEL}.dmx`, meshDmx("tools/atmo/generate-led-boards.mjs", bones, front));
write(`${MODEL_FRAME}.dmx`, meshDmx("tools/atmo/generate-led-boards.mjs", bones, boxMesh(matOf("led_frame"), frameQuads)));
const groups = skins.slice(1).map((name) => ({ name, remaps: [[matOf(skins[0]), matOf(name)]] }));
let vmdl = vmdlText(MODEL, "led_board", groups, []);
vmdl = vmdl.replace(`\t\t\t\t\t{\n\t\t\t\t\t\t_class = "RenderMeshFile"\n\t\t\t\t\t\tname = "led_board"\n\t\t\t\t\t\tfilename = "${MODEL}.dmx"\n\t\t\t\t\t},`,
  `\t\t\t\t\t{\n\t\t\t\t\t\t_class = "RenderMeshFile"\n\t\t\t\t\t\tname = "led_board"\n\t\t\t\t\t\tfilename = "${MODEL}.dmx"\n\t\t\t\t\t},\n\t\t\t\t\t{\n\t\t\t\t\t\t_class = "RenderMeshFile"\n\t\t\t\t\t\tname = "led_frame"\n\t\t\t\t\t\tfilename = "${MODEL_FRAME}.dmx"\n\t\t\t\t\t},`);
vmdl = vmdl.replace(/\t\t\t\{\n\t\t\t\t_class = "AnimationList"[\s\S]*?\n\t\t\t\},\n/, "");
write(`${MODEL}.vmdl`, vmdl);
console.log(`${MODEL}: ${skins.length} skins (${skins.slice(0, PAGES.length).join(" ")} | +${PAGES.length} = 70 % | +${2 * PAGES.length} = 30 % | ${3 * PAGES.length} = dark)`);
