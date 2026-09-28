#!/usr/bin/env node
// Pitch designs (owner 2026-09-28): alternative mowing patterns for the
// soccer_cssl_stadium_v8 pitch, chosen per player in !menu - Settings -
// Visuals. One flat model over the whole grass area (x +-1280, y +-1664),
// a few tenths of a unit above the floor, with one material group per design
// (skin 0..3). Each texture holds the pattern, a fine grass grain and the
// painted lines (same geometry as tools/grass/generate-shell-grass.mjs).
// Unlit like the bake grass (csgo_static_overlay F_LIT 0): the map's soft roof
// shadow is baked into the vertex colours from the floor lightmap export
// (.local/grass/soccer_cssl_stadium_v8_direct_light_shadows.png), the sun
// level goes into g_vColorTint - a lit prop would lose the baked shadow.
//
// usage: node tools/pitch/generate-pitch-designs.mjs <csgo_addons/soccermod_menu> [--preview <dir>]
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import crypto from "node:crypto";
import { fileURLToPath } from "node:url";

const out = process.argv[2];
if (!out) { console.error("usage: generate-pitch-designs.mjs <addon content dir> [--preview <dir>]"); process.exit(1); }
const previewDir = process.argv.includes("--preview") ? process.argv[process.argv.indexOf("--preview") + 1] : null;

const HALF_X = 1280, HALF_Y = 1664;
// power of two (mips). 2026-09-28 owner: "resolution at least 100 % higher" -
// 8192 (was 4096): 0.31 x 0.41 units per texel, about the map grass20 (0.33).
const TEX_W = 8192, TEX_H = 8192;
const MODEL = "models/soccermod/pitch/pitch_designs";
const MAT = (d) => `materials/soccermod/pitch/pitch_${d}`;
const DESIGNS = ["stripes", "lengthwise", "diamond", "circles"];
const SUN_LEVEL = 2.356, SHADE_LEVEL = 1.510; // as the bake grass
const GRID = 32; // vertex spacing for the baked shadow

// ---- line markings (tools/grass/generate-shell-grass.mjs) ------------------------
const rects = [
  [1018, -1384, 1024, 1384], [-1024, -1384, -1018, 1384],
  [-1024, 1378, 1024, 1384], [-1024, -1384, 1024, -1378],
  [-1018, -3, -11, 3], [11, -3, 1018, 3],
  [598, 832, 604, 1378], [-608, 832, -602, 1378], [-602, 832, 598, 838],
  [598, -1378, 604, -832], [-608, -1378, -602, -832], [-602, -838, 598, -832],
  [314, 1184, 320, 1378], [-320, 1184, -314, 1378], [-314, 1184, 314, 1190],
  [314, -1378, 320, -1184], [-320, -1378, -314, -1184], [-314, -1190, 314, -1184],
];
const half = Math.acos(128 / 258);
const rings = [
  [0, 0, 250, 256, 0, Math.PI * 2],
  [0, 960, 252, 258, -Math.PI / 2 - half, -Math.PI / 2 + half],
  [0, -960, 252, 258, Math.PI / 2 - half, Math.PI / 2 + half],
  [-1024, 1384, 48, 54, -Math.PI / 2, 0], [1024, 1384, 48, 54, Math.PI, Math.PI * 1.5],
  [-1024, -1384, 48, 54, 0, Math.PI / 2], [1024, -1384, 48, 54, Math.PI / 2, Math.PI],
];
const discs = [[0, 0, 11], [0, 1016, 11], [0, -1016, 11]];
const angleIn = (t, a0, a1) => { const tau = Math.PI * 2; const n = (v) => ((v % tau) + tau) % tau; if (a1 - a0 >= tau - 1e-9) return true; const s = n(a0), e = n(a1), x = n(t); return s <= e ? x >= s && x <= e : x >= s || x <= e; };
const onLine = (x, y) => {
  for (const [x0, y0, x1, y1] of rects) if (x >= x0 && x <= x1 && y >= y0 && y <= y1) return true;
  for (const [cx, cy, r0, r1, a0, a1] of rings) { const d = Math.hypot(x - cx, y - cy); if (d >= r0 && d <= r1 && angleIn(Math.atan2(y - cy, x - cx), a0, a1)) return true; }
  for (const [cx, cy, r] of discs) if (Math.hypot(x - cx, y - cy) <= r) return true;
  return false;
};

// ---- patterns: 0 = darkest, 1 = lightest -----------------------------------------
// 2026-09-28 owner: the first set looked cartoonish - now the map's own look
// (materials/tm/grass20, measured, not copied): its squares are two crossing
// sets of 83.2-unit mowing bands, so three shades (dark / mid / light); the
// designs use the same colours and grain, only the band layout changes.
const band = (v, w) => (Math.floor(v / w) & 1);
const pattern = {
  stripes: (x, y) => band(y + HALF_Y, 166.4),                     // across the pitch
  lengthwise: (x, y) => band(x + HALF_X, 160),                    // along the touchlines
  diamond: (x, y) => (band(x + y + 4000, 166.4) + band(x - y + 4000, 166.4)) / 2, // the map's squares, turned 45 deg
  circles: (x, y) => band(Math.hypot(x, y), 128),
};

// ---- textures -------------------------------------------------------------------
function png(w, h, pixel) {
  const raw = Buffer.alloc((w * 3 + 1) * h);
  for (let y = 0; y < h; y++) { raw[y * (w * 3 + 1)] = 0; for (let x = 0; x < w; x++) pixel(x, y, raw, y * (w * 3 + 1) + 1 + x * 3); }
  const table = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
  const crc = (b) => { let c = 0xffffffff; for (const v of b) c = table[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (type, data) => { const len = Buffer.alloc(4); len.writeUInt32BE(data.length); const td = Buffer.concat([Buffer.from(type), data]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([len, td, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 2;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", zlib.deflateSync(raw, { level: 6 })), chunk("IEND", Buffer.alloc(0))]);
}
const clamp = (v) => Math.max(0, Math.min(255, Math.round(v)));
function mulberry32(a) { return () => { a |= 0; a = (a + 0x6d2b79f5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }

// Grass grain shared by all designs, measured on grass20 at this texel size
// (about 0.65 units): white noise, one gaussian per texel, relative spread
// R 12.4 %, G 8.2 %, B 18.5 % moving together (bright specks go yellowish).
const rnd = mulberry32(11);
const grain = new Float32Array(TEX_W * TEX_H);
for (let i = 0; i < grain.length; i++) grain[i] = Math.sqrt(-2 * Math.log(1 - rnd())) * Math.cos(2 * Math.PI * rnd());
const GRAIN_SD = [0.124, 0.082, 0.185];
const blotchN = 64, blotch = Float32Array.from({ length: blotchN * blotchN }, () => rnd() - 0.5);
const blotchAt = (u, v) => { const fx = u * blotchN, fy = v * blotchN, x0 = Math.floor(fx), y0 = Math.floor(fy), tx = fx - x0, ty = fy - y0; const g = (i, j) => blotch[Math.min(blotchN - 1, j) * blotchN + Math.min(blotchN - 1, i)]; return (g(x0, y0) * (1 - tx) + g(x0 + 1, y0) * tx) * (1 - ty) + (g(x0, y0 + 1) * (1 - tx) + g(x0 + 1, y0 + 1) * tx) * ty; };

// Line coverage (3x3 supersampling), computed once for all designs.
const lineCov = new Float32Array(TEX_W * TEX_H);
{
  const ux = (2 * HALF_X) / TEX_W, uy = (2 * HALF_Y) / TEX_H;
  const boxes = [
    ...rects.map(([x0, y0, x1, y1]) => [x0, y0, x1, y1]),
    ...rings.map(([cx, cy, , r1]) => [cx - r1, cy - r1, cx + r1, cy + r1]),
    ...discs.map(([cx, cy, r]) => [cx - r, cy - r, cx + r, cy + r]),
  ];
  const seen = new Uint8Array(TEX_W * TEX_H);
  for (const [x0, y0, x1, y1] of boxes) {
    const px0 = Math.max(0, Math.floor((x0 + HALF_X) / ux) - 1), px1 = Math.min(TEX_W - 1, Math.ceil((x1 + HALF_X) / ux) + 1);
    const py0 = Math.max(0, Math.floor((HALF_Y - y1) / uy) - 1), py1 = Math.min(TEX_H - 1, Math.ceil((HALF_Y - y0) / uy) + 1);
    for (let py = py0; py <= py1; py++) for (let px = px0; px <= px1; px++) {
      const i = py * TEX_W + px; if (seen[i]) continue; seen[i] = 1;
      let hit = 0;
      for (let sy = 0; sy < 3; sy++) for (let sx = 0; sx < 3; sx++) {
        const x = -HALF_X + (px + (sx + 0.5) / 3) * ux, y = HALF_Y - (py + (sy + 0.5) / 3) * uy;
        if (onLine(x, y)) hit++;
      }
      lineCov[i] = hit / 9;
    }
  }
}

// grass20 quarter means: darkest 61.6/82.6/30.4, lightest 71.5/96.6/35.0
const DARK = [61.6, 82.6, 30.4], LIGHT = [71.5, 96.6, 35.0], LINE = [236, 238, 232];
// The public CS2 tools cap a texture at 2048 (PublicToolsDefaultMaxRes), so
// each design is SPLIT x SPLIT pieces of 2048 (the resource compiler caps a
// texture at 2048 whatever the vtex asks) of the 8192 image: piece k = qx + SPLIT qy,
// qx 0 = x < 0, qy 0 = y > 0 (top of the image).
const SPLIT = 4, QW = TEX_W / SPLIT, QH = TEX_H / SPLIT;
function designTexture(name, qx, qy) {
  const pat = pattern[name];
  const ux = (2 * HALF_X) / TEX_W, uy = (2 * HALF_Y) / TEX_H;
  return png(QW, QH, (qpx, qpy, b, o) => {
    const px = qpx + qx * QW, py = qpy + qy * QH;
    const x = -HALF_X + (px + 0.5) * ux, y = HALF_Y - (py + 0.5) * uy;
    // soften the band edges a little (2x2 sample)
    const l = (pat(x - ux / 3, y - uy / 3) + pat(x + ux / 3, y - uy / 3) + pat(x - ux / 3, y + uy / 3) + pat(x + ux / 3, y + uy / 3)) / 4;
    const i = py * TEX_W + px, c = lineCov[i];
    for (let k = 0; k < 3; k++) {
      const grass = (DARK[k] + (LIGHT[k] - DARK[k]) * l) * (1 + GRAIN_SD[k] * grain[i]);
      b[o + k] = clamp(grass * (1 - c) + LINE[k] * (1 + grain[i] * 0.03) * c);
    }
  });
}

// ---- baked roof shadow (as the bake grass) --------------------------------------
function readPngChannel0(file) {
  const b = fs.readFileSync(file); let o = 8, w = 0, h = 0, ct = 0; const idat = [];
  while (o < b.length) {
    const len = b.readUInt32BE(o), type = b.toString("ascii", o + 4, o + 8), d = b.subarray(o + 8, o + 8 + len);
    if (type === "IHDR") { w = d.readUInt32BE(0); h = d.readUInt32BE(4); ct = d[9]; }
    if (type === "IDAT") idat.push(d);
    o += 12 + len;
  }
  const ch = { 0: 1, 2: 3, 4: 2, 6: 4 }[ct], stride = w * ch, raw = zlib.inflateSync(Buffer.concat(idat)), px = Buffer.alloc(h * stride);
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
  return { w, h, at: (x, y) => px[Math.max(0, Math.min(h - 1, y)) * stride + Math.max(0, Math.min(w - 1, x)) * ch] };
}
const mask = readPngChannel0(path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "..", ".local", "grass", "soccer_cssl_stadium_v8_direct_light_shadows.png"));
const shadowAt = (x, y) => {
  const u = (30 + (x + HALF_X) / (2 * HALF_X) * 10845) / 65535 * mask.w - 0.5;
  const v = (270 + (HALF_Y - y) / (2 * HALF_Y) * 14099) / 65535 * mask.h - 0.5;
  const x0 = Math.floor(u), y0 = Math.floor(v), fx = u - x0, fy = v - y0, p = mask.at;
  return ((p(x0, y0) * (1 - fx) + p(x0 + 1, y0) * fx) * (1 - fy) + (p(x0, y0 + 1) * (1 - fx) + p(x0 + 1, y0 + 1) * fx) * fy) / 255;
};
const toGamma = (l) => (l <= 0.0031308 ? 12.92 * l : 1.055 * Math.pow(l, 1 / 2.4) - 0.055);

// ---- mesh: a GRID x GRID vertex grid, vertex colour = relative light -------------
// One grid per piece (own UVs 0..1, one face set / material each).
const PW = 2 * HALF_X / SPLIT, PH = 2 * HALF_Y / SPLIT, nx = PW / GRID, ny = PH / GRID;
const positions = [], uvs = [], cols = [], faceSets = Array.from({ length: SPLIT * SPLIT }, () => []);
for (let qy = 0; qy < SPLIT; qy++) for (let qx = 0; qx < SPLIT; qx++) {
  const k = qx + SPLIT * qy, x0 = -HALF_X + qx * PW, y0 = HALF_Y - qy * PH, base = positions.length;
  for (let j = 0; j <= ny; j++) for (let i = 0; i <= nx; i++) {
    const x = x0 + i * GRID, y = y0 - j * GRID;
    positions.push([x, y, 0]); uvs.push([i / nx, j / ny]);
    cols.push(toGamma(1 - (1 - SHADE_LEVEL / SUN_LEVEL) * shadowAt(x, y)));
  }
  for (let j = 0; j < ny; j++) for (let i = 0; i < nx; i++) {
    const a = base + j * (nx + 1) + i;
    faceSets[k].push([a, a + nx + 1, a + nx + 2, a + 1]); // counter-clockwise seen from above
  }
}
const faces = faceSets.flat();
const QMAT = (d, k) => MAT(`${d}_q${k}`);

const id = () => crypto.randomUUID();
const q = (vals, ind) => vals.map((v) => `${ind}"${v}"`).join(",\n");
const f4 = (n) => Number(n.toFixed(4)).toString();
const idx = q(positions.map((_, i) => i), "\t\t");
const I = { model: id(), dag: id(), bind: id() };
const dmx = `<!-- dmx encoding keyvalues2 4 format model 22 -->
"DmElement"
{
\t"id" "elementid" "${id()}"
\t"name" "string" "root"
\t"skeleton" "element" "${I.model}"
\t"model" "element" "${I.model}"
\t"exportTags" "DmeExportTags"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "exportTags"
\t\t"source" "string" "tools/pitch/generate-pitch-designs.mjs"
\t}
}

"DmeModel"
{
\t"id" "elementid" "${I.model}"
\t"name" "string" "pitch_designs"
\t"transform" "DmeTransform"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"position" "vector3" "0 0 0"
\t\t"orientation" "quaternion" "0 0 0 1"
\t}
\t"shape" "element" ""
\t"visible" "bool" "1"
\t"children" "element_array"
\t[
\t\t"element" "${I.dag}"
\t]
\t"jointList" "element_array"
\t[
\t\t"element" "${I.dag}"
\t]
\t"baseStates" "element_array"
\t[
\t\t"DmeTransformsList"
\t\t{
\t\t\t"id" "elementid" "${id()}"
\t\t\t"transforms" "element_array"
\t\t\t[
\t\t\t\t"DmeTransform"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t\t"position" "vector3" "0 0 0"
\t\t\t\t\t"orientation" "quaternion" "0 0 0 1"
\t\t\t\t}
\t\t\t]
\t\t}
\t]
\t"axisSystem" "DmeAxisSystem"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"upAxis" "int" "3"
\t\t"forwardParity" "int" "1"
\t\t"coordSys" "int" "0"
\t}
}

"DmeDag"
{
\t"id" "elementid" "${I.dag}"
\t"name" "string" "pitch_designs"
\t"transform" "DmeTransform"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"position" "vector3" "0 0 0"
\t\t"orientation" "quaternion" "0 0 0 1"
\t}
\t"shape" "DmeMesh"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "pitch_designs"
\t\t"bindState" "element" ""
\t\t"currentState" "element" "${I.bind}"
\t\t"baseStates" "element_array"
\t\t[
\t\t\t"element" "${I.bind}"
\t\t]
\t\t"deltaStates" "element_array"
\t\t[
\t\t]
\t\t"faceSets" "element_array"
\t\t[
${faceSets.map((fs, k) => `\t\t\t"DmeFaceSet"
\t\t\t{
\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t"name" "string" "pitch_q${k}"
\t\t\t\t"faces" "int_array"
\t\t\t\t[
${q(fs.flatMap((f) => [...f, -1]), "\t\t\t\t\t")}
\t\t\t\t]
\t\t\t\t"material" "DmeMaterial"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t\t"name" "string" "material"
\t\t\t\t\t"mtlName" "string" "${QMAT(DESIGNS[0], k)}.vmat"
\t\t\t\t}
\t\t\t}`).join(",\n")}
\t\t]
\t\t"deltaStateWeights" "vector2_array"
\t\t[
\t\t]
\t\t"deltaStateWeightsLagged" "vector2_array"
\t\t[
\t\t]
\t\t"visible" "bool" "1"
\t}
\t"visible" "bool" "1"
\t"children" "element_array"
\t[
\t]
}

"DmeVertexData"
{
\t"id" "elementid" "${I.bind}"
\t"name" "string" "bind"
\t"vertexFormat" "string_array"
\t[
\t\t"position$0",
\t\t"texcoord$0",
\t\t"normal$0",
\t\t"tangent$0",
\t\t"color$0"
\t]
\t"jointCount" "int" "0"
\t"flipVCoordinates" "bool" "0"
\t"position$0" "vector3_array"
\t[
${q(positions.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"position$0Indices" "int_array"
\t[
${idx}
\t]
\t"texcoord$0" "vector2_array"
\t[
${q(uvs.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"texcoord$0Indices" "int_array"
\t[
${idx}
\t]
\t"normal$0" "vector3_array"
\t[
${q(positions.map(() => "0 0 1"), "\t\t")}
\t]
\t"normal$0Indices" "int_array"
\t[
${idx}
\t]
\t"tangent$0" "vector4_array"
\t[
${q(positions.map(() => "1 0 0 1"), "\t\t")}
\t]
\t"tangent$0Indices" "int_array"
\t[
${idx}
\t]
\t"color$0" "color_array"
\t[
${q(cols.map((c) => { const v = Math.round(c * 255); return `${v} ${v} ${v} 255`; }), "\t\t")}
\t]
\t"color$0Indices" "int_array"
\t[
${idx}
\t]
}
`;

// Skins 0-3 = the designs (lit csgo_complex); 2026-09-28 glow test: skins
// 4-7 = the designs with glow material A, 8-11 with glow material B (see
// vmatGlow below; the plugin picks the set with css_sm2pitch glow off|a|b).
const groupFor = (name, d, suffix) => `\t\t\t\t\t{
\t\t\t\t\t\t_class = "MaterialGroup"
\t\t\t\t\t\tname = "${name}"
\t\t\t\t\t\tremaps =
\t\t\t\t\t\t[
${faceSets.map((_, k) => `\t\t\t\t\t\t\t{
\t\t\t\t\t\t\t\tfrom = "${QMAT(DESIGNS[0], k)}.vmat"
\t\t\t\t\t\t\t\tto = "${QMAT(d, k)}${suffix}.vmat"
\t\t\t\t\t\t\t},
`).join("")}\t\t\t\t\t\t]
\t\t\t\t\t},
`;
const groups = DESIGNS.slice(1).map((d) => groupFor(d, d, "")).join("")
  + DESIGNS.map((d) => groupFor(`${d}_glowa`, d, "_ga")).join("")
  + DESIGNS.map((d) => groupFor(`${d}_glowb`, d, "_gb")).join("");
const vmdl = `<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc28:version{fb63b6ca-f435-4aa0-a2c7-c66ddc651dca} -->
{
\trootNode =
\t{
\t\t_class = "RootNode"
\t\tchildren =
\t\t[
\t\t\t{
\t\t\t\t_class = "MaterialGroupList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "DefaultMaterialGroup"
\t\t\t\t\t\tremaps = [  ]
\t\t\t\t\t\tuse_global_default = false
\t\t\t\t\t\tglobal_default_material = ""
\t\t\t\t\t},
${groups}\t\t\t\t]
\t\t\t},
\t\t\t{
\t\t\t\t_class = "RenderMeshList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "RenderMeshFile"
\t\t\t\t\t\tname = "pitch_designs"
\t\t\t\t\t\tfilename = "${MODEL}.dmx"
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t]
\t}
}
`;
// 2026-09-28: the design floor was an unlit csgo_static_overlay (baked roof
// shadow in the vertex colours). Overlays cover each other in draw order, not
// by height, so it painted over the 3D grass blades (also overlays) - with a
// design chosen the grass vanished (confirmed in game: hiding the floor from
// grass players brought the blades back, over the map's own floor). Now an
// ordinary lit, opaque csgo_complex like the perimeter wall: drawn in the
// opaque pass like the map floor, so the grass overlays lie on top of it, and
// lit like the map floor (owner: "green brighter like the original classic").
// The vertex colours are no longer used; the roof shadow is the engine's.
const vmat = (d) => `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
\t"F_DO_NOT_CAST_SHADOWS"\t"1"
\t"g_flMetalness"\t"0.000"
\t"TextureColor"\t"${MAT(d)}_color.vtex"
\t"TextureRoughness"\t"[1.000000 1.000000 1.000000 0.000000]"
}
`;
// 2026-09-28 glow test (see tools/grass/generate-shell-grass.mjs --variant
// glow): the same floor, but its own lighting almost off (g_vColorTint
// GLOW_TINT, no reflectance) and the colour from self-illumination, with the
// soft roof shadow from the vertex colours - no sharp dynamic shadow. Level
// = the earlier unlit calibration (SUN_LEVEL x 2.27, measured against the map
// floor). A: brightness level / GLOW_TINT (self-illumination on the tinted
// albedo), B: brightness level (on the untinted albedo).
const GLOW_TINT = 0.1, GLOW_LEVEL = SUN_LEVEL * 2.27;
const vmatGlow = (d, untinted) => `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
\t"F_PAINT_VERTEX_COLORS"\t"1"
\t"F_SELF_ILLUM"\t"1"
\t"F_DO_NOT_CAST_SHADOWS"\t"1"
\t"g_flMetalness"\t"0.000"
\t"g_flReflectance"\t"0.000"
\t"g_vColorTint"\t"[${toGamma(GLOW_TINT).toFixed(6)} ${toGamma(GLOW_TINT).toFixed(6)} ${toGamma(GLOW_TINT).toFixed(6)} 0.000000]"
\t"g_flSelfIllumAlbedoFactor"\t"1.000"
\t"g_flSelfIllumBrightness"\t"${(untinted ? GLOW_LEVEL : GLOW_LEVEL / GLOW_TINT).toFixed(3)}"
\t"g_flSelfIllumScale"\t"1.000"
\t"g_vSelfIllumTint"\t"[1.000000 1.000000 1.000000 0.000000]"
\t"TextureColor"\t"${MAT(d)}_color.vtex"
\t"TextureSelfIllumMask"\t"[1.000000 1.000000 1.000000 0.000000]"
\t"TextureRoughness"\t"[1.000000 1.000000 1.000000 0.000000]"
}
`;
// Texture source with its own size limit: a plain PNG reference is capped at
// 2048 (the compiler cap); DXT1 keeps each piece at about 2 MB.
const vtex = (d) => `<!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->
"CDmeVtex"
{
    "m_inputTextureArray" "element_array"
    [
        "CDmeInputTexture"
        {
            "m_name" "string" "PitchColor"
            "m_fileName" "string" "${MAT(d)}_color.png"
            "m_colorSpace" "string" "srgb"
            "m_typeString" "string" "2D"
            "m_imageProcessorArray" "element_array"
            [
            ]
        }
    ]
    "m_outputTypeString" "string" "2D"
    "m_outputFormat" "string" "DXT1"
    "m_outputClearColor" "vector4" "0 0 0 0"
    "m_nOutputMinDimension" "int" "0"
    "m_nOutputMaxDimension" "int" "2048"
    "m_textureOutputChannelArray" "element_array"
    [
        "CDmeTextureOutputChannel"
        {
            "m_inputTextureArray" "string_array"
            [
                "PitchColor"
            ]
            "m_srcChannels" "string" "rgba"
            "m_dstChannels" "string" "rgba"
            "m_mipAlgorithm" "CDmeImageProcessor"
            {
                "m_algorithm" "string" "Box"
                "m_stringArg" "string" ""
                "m_vFloat4Arg" "vector4" "0 0 0 0"
            }
            "m_outputColorSpace" "string" "srgb"
        }
    ]
    "m_vClamp" "vector3" "0 0 0"
    "m_bNoLod" "bool" "0"
}
`;

const write =(rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
write(`${MODEL}.dmx`, dmx);
write(`${MODEL}.vmdl`, vmdl);
write("materials/soccermod/pitch/pitch_opaque.png", png(8, 8, (x, y, b, o) => { b[o] = 255; b[o + 1] = 255; b[o + 2] = 255; }));
for (const d of DESIGNS) {
  for (let k = 0; k < SPLIT * SPLIT; k++) {
    const key = `${d}_q${k}`, tex = designTexture(d, k % SPLIT, Math.floor(k / SPLIT));
    write(`${MAT(key)}_color.png`, tex);
    write(`${MAT(key)}_color.vtex`, vtex(key));
    write(`${MAT(key)}.vmat`, vmat(key));
    write(`${MAT(key)}_ga.vmat`, vmatGlow(key, false));
    write(`${MAT(key)}_gb.vmat`, vmatGlow(key, true));
    if (previewDir) fs.writeFileSync(path.join(previewDir, `pitch_${key}.png`), tex);
  }
}
console.log(`pitch designs: ${DESIGNS.join(", ")}; ${positions.length} verts, ${faces.length} quads, textures ${SPLIT * SPLIT} x ${QW}x${QH} per design (${TEX_W}x${TEX_H}) -> ${out}`);
