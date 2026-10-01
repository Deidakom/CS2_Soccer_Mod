#!/usr/bin/env node
// "3D grass" for the SoccerMod stadium pitch (soccer_soccermod_stadium and
// soccer_cssl_stadium_v8 share it: grass x ±1280, y ±1664, floor z = -32).
//
// Shell technique: 9 stacked, translucent layers 0.25 units apart carry a turf
// texture whose alpha is made of small blade dots. Seen from player height the
// same dot in every layer reads as a short blade with depth. White layers of
// the same kind follow the painted lines, so the lines look like grass too.
// Everything here is generated; no third-party texture or model is used.
//
// Writes into the Feature Package content addon (default soccermod_menu):
//   models/soccermod/grass_shell_<x>_<y>.vmdl + .dmx (80 tiles, 8 x 10)
//   materials/soccermod/grass_shell_green.vmat, grass_shell_white.vmat
//   materials/soccermod/grass_shell_{green,white}_color.png, grass_shell_trans.png
// Each tile model is centred on its tile (the plugin, Grass.cs, spawns tile
// (x, y) at its centre on the floor, z = -32); no physics at all.
//
//   node tools/grass/generate-shell-grass.mjs [--addon <content addon dir>]
//        [--layers 9] [--spacing 0.25] [--start 0.25] [--tile 128] [--dots 70000] [--seed 7]
//        [--variant fine|bake]
//   bake only: [--shadow-mask <png>] [--sun-level 2.356] [--shade-level 1.510]
//        [--depth-dark 0.55] [--shadow-step 16] [--vc-linear] [--alpha-ref 0.40] [--preview <dir>]
//
// --variant bake (2026-09-27, test server only): a separate UNLIT model set
// models/soccermod/grass_bake_<x>_<y>.vmdl + materials/soccermod/grass_bake_
// {green,white}.vmat with the map's own soft roof shadow baked in. The sun of
// soccer_cssl_stadium_v8 is a Stationary light: the floor shows its baked
// shadow mask (soft, about 40 units of penumbra), dynamic props get the sharp
// cascade shadow, and CS2 ignores disablereceiveshadows / EF_NORECEIVESHADOW
// on them (tested on 27018). Unlit props get no light and no shadow at all,
// so the lighting is baked: vertex colour = sun or shade level from the mask
// (same bilinear ramp as the floor) x a darker bottom for the lower shells.
// Only the bake files are written; the grass_fine_* set and the shared
// textures are left alone.
//   Shader: csgo_static_overlay.vfx with F_LIT 0 (unlit), F_BLEND_MODE 2
// (alpha test, the cut-out look of skin 1), F_PAINT_VERTEX_COLORS,
// F_RENDER_BACKFACES, F_DO_NOT_CAST_SHADOWS. csgo_complex has no unlit mode
// and core generic.vfx has no CsgoForward pass. Output = texture x vertex
// colour x g_vColorTint; the tint carries the sun level (the shader runs it
// through SrgbGammaToLinear, so it is written gamma-encoded).
//   Levels (floor lighting in the shader's units, irradiance lightmap decoded
// with Source2Viewer-CLI): sunlit floor = sun 1.0 x NdotL 0.707 (pitch 45)
// + indirect 1.649 (mean over the sunlit pitch) = 2.356; roof shadow =
// indirect only, 1.510 (mean over the shaded pitch).
// The mask PNG is the map's lightmaps/direct_light_shadows.vtex_c exported
// with Source2Viewer-CLI -d (255 = shadow in that export); the pitch floor's
// lightmap UV rectangle below was read from lightmap_query_data.kv3.
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import crypto from "node:crypto";
import { fileURLToPath } from "node:url";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => {
  if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
const addon = args.addon ??
  "E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/content/csgo_addons/soccermod_menu";
const layers = Number(args.layers ?? 9);
const spacing = Number(args.spacing ?? 0.25);
const start = Number(args.start ?? 0.25);          // first layer above the floor (0 would z-fight the grass)
const tile = Number(args.tile ?? 128);             // world units per texture repeat
const dots = Number(args.dots ?? 70000);           // blade dots per texture
let seed = Number(args.seed ?? 7) >>> 0 || 7;
const rand = () => { seed ^= seed << 13; seed >>>= 0; seed ^= seed >>> 17; seed ^= seed << 5; seed >>>= 0; return seed / 0x100000000; };

const HALF_X = 1280, HALF_Y = 1664;
// 80 tiles: CS2 lights a dynamic prop with one sample, so one pitch-sized
// prop missed the stadium roof shadow baked into the floor (owner, 2026-09-26).
// 2026-09-26: 16 x 20 tiles (160 x 166 u). A prop gets one light value, so
// smaller tiles follow the roof shadow edge more closely (was 8 x 10).
const TILES_X = 16, TILES_Y = 20, TILE_W = (2 * HALF_X) / TILES_X, TILE_H = (2 * HALF_Y) / TILES_Y;
// --design <stripes|lengthwise|diamond|circles> (2026-09-28 owner: "make the
// 3D grass work for all 4 pitch design variants"): a bake set whose blades
// carry the mowing pattern of that pitch design (tools/pitch/generate-pitch-
// designs.mjs, same bands and the same dark/light shades) in their vertex
// colours. First written as 4 x 4 chunks with an own material - the owner's
// client did not draw them although the server transmitted them. Now exactly
// like the working bake grass: the same 16 x 20 tiles and the same materials
// (grass_bake_green / _white), only the vertex colours differ:
// models/soccermod/grass_dtile_<name>_<tx>_<ty>.
const design = args.design ?? null;
const DESIGN_PATTERNS = {
  // keep in sync with `pattern` in tools/pitch/generate-pitch-designs.mjs (0 = dark, 1 = light)
  stripes: (x, y) => (Math.floor((y + 1664) / 166.4) & 1),
  lengthwise: (x, y) => (Math.floor((x + 1280) / 160) & 1),
  diamond: (x, y) => ((Math.floor((x + y + 4000) / 166.4) & 1) + (Math.floor((x - y + 4000) / 166.4) & 1)) / 2,
  circles: (x, y) => (Math.floor(Math.hypot(x, y) / 128) & 1),
};
if (design && !DESIGN_PATTERNS[design]) throw new Error(`--design must be one of ${Object.keys(DESIGN_PATTERNS).join(", ")}`);
// stripes / lengthwise bands are exactly one tile row / column (166.4 / 160 units)
const designPerTile = design === "stripes" || design === "lengthwise";
const variant = design ? "bake" : args.variant ?? "fine";
if (!["fine", "bake", "glow"].includes(variant)) throw new Error(`--variant must be fine, bake or glow, not ${variant}`);
// --variant glow (2026-09-28, grass over the pitch designs): the bake set's
// geometry and baked vertex colours, but an opaque, alpha-tested csgo_complex
// that writes depth (the static_overlay bake grass is painted over by the
// design floor prop). Its own lighting is turned almost off (g_vColorTint
// GLOW_TINT, no reflectance) and the colour comes from self-illumination, so
// the stadium's sharp dynamic shadow barely shows and the soft baked one does.
// Two material groups for an in-game comparison: default (A) assumes the
// self-illumination uses the tinted albedo, "b" that it uses the untinted one.
const glow = variant === "glow";
const bake = variant === "bake" || glow;
// fine: new name, the 8 x 10 grass_shell_* tiles stay for older plugins. bake: unlit test set.
const MODEL = design ? `models/soccermod/grass_dtile_${design}` : glow ? "models/soccermod/grass_glow" : bake ? "models/soccermod/grass_bake" : "models/soccermod/grass_fine";
const MAT_GREEN = glow ? "materials/soccermod/grass_glow_green" : bake ? "materials/soccermod/grass_bake_green" : "materials/soccermod/grass_shell_green";
const MAT_WHITE = glow ? "materials/soccermod/grass_glow_white" : bake ? "materials/soccermod/grass_bake_white" : "materials/soccermod/grass_shell_white";
const TEX_GREEN = "materials/soccermod/grass_shell_green_color.png"; // shared by both variants
const TEX_WHITE = "materials/soccermod/grass_shell_white_color.png";
const TEX_TRANS = "materials/soccermod/grass_shell_trans.png";

// ---- line markings (measured from the stadium's 85 pitch-line meshes) -----------
const rects = [
  // touchlines, goal lines, halfway line
  [1018, -1384, 1024, 1384], [-1024, -1384, -1018, 1384],
  [-1024, 1378, 1024, 1384], [-1024, -1384, 1024, -1378],
  [-1018, -3, -11, 3], [11, -3, 1018, 3],
  // 2026-09-27 re-measured from the painted pitch_line meshes of both maps
  // (soccer_cssl_stadium_v8 func_brush 2:49167:99 and our stadium's meshes):
  // the box is not symmetric (sides x -608..-602 and 598..604, front line
  // x -602..598) and the goal-area sides lie outside x ±314 (314..320). The
  // old values put the goal-area blades one line width inside the paint.
  // penalty boxes (sides x -608..-602 and 598..604, y 832..1378) and their front line
  [598, 832, 604, 1378], [-608, 832, -602, 1378], [-602, 832, 598, 838],
  [598, -1378, 604, -832], [-608, -1378, -602, -832], [-602, -838, 598, -832],
  // goal areas (sides x ±314..320, y 1184..1378, front line x -314..314)
  [314, 1184, 320, 1378], [-320, 1184, -314, 1378], [-314, 1184, 314, 1190],
  [314, -1378, 320, -1184], [-320, -1378, -314, -1184], [-314, -1190, 314, -1184],
];
const rings = [
  // [cx, cy, rIn, rOut, a0, a1] angles in radians
  [0, 0, 250, 256, 0, Math.PI * 2],
  // penalty arcs, only the part in front of the box (|y| < 832)
  [0, 960, 252, 258, Math.PI * 1.5 - Math.acos(128 / 258) + Math.PI / 2 - Math.PI / 2, 0],
  [0, -960, 252, 258, 0, 0],
  // corner arcs (quarter circles into the pitch)
  [-1024, 1384, 48, 54, -Math.PI / 2, 0], [1024, 1384, 48, 54, Math.PI, Math.PI * 1.5],
  [-1024, -1384, 48, 54, 0, Math.PI / 2], [1024, -1384, 48, 54, Math.PI / 2, Math.PI],
];
// penalty arcs: the visible part spans from where the circle meets y = ±832
{
  const half = Math.acos(128 / 258);              // angle from the vertical to the box edge
  rings[1][4] = -Math.PI / 2 - half; rings[1][5] = -Math.PI / 2 + half;   // below (0, 960)
  rings[2][4] = Math.PI / 2 - half;  rings[2][5] = Math.PI / 2 + half;    // above (0, -960)
}
const discs = [[0, 0, 11], [0, 1016, 11], [0, -1016, 11]];
// The map's painted line triangles (world xy; see the white faces below).
const LINE_TRIS = JSON.parse(fs.readFileSync(path.join(path.dirname(fileURLToPath(import.meta.url)), "pitch-line-tris.json"), "utf8")).tris;

// ---- baked lighting (--variant bake only) -------------------------------------------
function readPngChannel0(file) {
  const b = fs.readFileSync(file); let o = 8, w = 0, h = 0, ct = 0, bd = 8; const idat = [];
  while (o < b.length) {
    const len = b.readUInt32BE(o), type = b.toString("ascii", o + 4, o + 8), d = b.subarray(o + 8, o + 8 + len);
    if (type === "IHDR") { w = d.readUInt32BE(0); h = d.readUInt32BE(4); bd = d[8]; ct = d[9]; }
    if (type === "IDAT") idat.push(d);
    o += 12 + len;
  }
  if (bd !== 8) throw new Error(`${file}: only 8-bit PNGs are supported`);
  const ch = { 0: 1, 2: 3, 4: 2, 6: 4 }[ct], stride = w * ch, raw = zlib.inflateSync(Buffer.concat(idat)), out = Buffer.alloc(h * stride);
  for (let y = 0; y < h; y++) {
    const ft = raw[y * (stride + 1)], src = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
    const cur = out.subarray(y * stride, (y + 1) * stride), prev = y ? out.subarray((y - 1) * stride, y * stride) : Buffer.alloc(stride);
    for (let x = 0; x < stride; x++) {
      const a = x >= ch ? cur[x - ch] : 0, up = prev[x], c = x >= ch ? prev[x - ch] : 0; let v = src[x];
      if (ft === 1) v += a; else if (ft === 2) v += up; else if (ft === 3) v += (a + up) >> 1;
      else if (ft === 4) { const p = a + up - c, pa = Math.abs(p - a), pb = Math.abs(p - up), pc = Math.abs(p - c); v += pa <= pb && pa <= pc ? a : pb <= pc ? up : c; }
      cur[x] = v & 255;
    }
  }
  return { w, h, at: (x, y) => out[Math.max(0, Math.min(h - 1, y)) * stride + Math.max(0, Math.min(w - 1, x)) * ch] };
}
const defaultMask = path.join(path.dirname(fileURLToPath(import.meta.url)),
  "..", "..", ".local", "grass", "soccer_cssl_stadium_v8_direct_light_shadows.png");
const shadowMask = bake ? readPngChannel0(args["shadow-mask"] ?? defaultMask) : null;
const sunLevel = Number(args["sun-level"] ?? 2.356);     // sunlit floor lighting (x albedo)
const shadeLevel = Number(args["shade-level"] ?? 1.510); // roof-shadow floor lighting (x albedo)
const depthDark = Number(args["depth-dark"] ?? 0.55);    // lowest shell layer vs the top one
const shadowStep = Number(args["shadow-step"] ?? 16);    // subdivision where the shadow changes
const vcLinear = "vc-linear" in args;                    // default: vertex colours gamma-encoded
// Alpha-test reference of the bake materials. The compiled texture keeps
// about 20-25% of texels >= 0.5 in every mip (PreserveCoverage), so at 0.5 the
// grass thins to a quarter from a few hundred units away and the floor shows
// through ("grass not rendering from distance", 2026-09-27). At 0.40 the far
// mips (5+) pass almost fully and near blades grow from 20% to 27% coverage.
const alphaRef = Number(args["alpha-ref"] ?? 0.40);
// 2026-09-28 owner: brighter green like the original floor. The unlit green
// blades showed at about sRGB (65, 102, 30) next to the lit map floor at
// (106, 141, 56); x2.0 in linear light (about x1.37 in sRGB) on the green
// material only (the white lines are bright enough).
const greenExposure = Number(args["green-exposure"] ?? 2.0);
const toGamma = (l) => (l <= 0.0031308 ? 12.92 * l : 1.055 * Math.pow(l, 1 / 2.4) - 0.055);
// Pitch floor in the lightmap atlas (UV in 1/65535): x -1280..1280 -> u 30..10875,
// y 1664..-1664 -> v 270..14369 (soccer_cssl_stadium_v8, lightmap_query_data.kv3).
// Bilinear like the GPU samples the floor lightmap, so the ramp is the same.
const shadowAt = (x, y) => {
  if (!shadowMask) return 0;
  const u = (30 + (x + HALF_X) / (2 * HALF_X) * 10845) / 65535 * shadowMask.w - 0.5;
  const v = (270 + (HALF_Y - y) / (2 * HALF_Y) * 14099) / 65535 * shadowMask.h - 0.5;
  const x0 = Math.floor(u), y0 = Math.floor(v), fx = u - x0, fy = v - y0, p = shadowMask.at;
  return ((p(x0, y0) * (1 - fx) + p(x0 + 1, y0) * fx) * (1 - fy) + (p(x0, y0 + 1) * (1 - fx) + p(x0 + 1, y0 + 1) * fx) * fy) / 255;
};
// Linear vertex colour: 1 in the sun, shade/sun under the roof, x the shell depth.
const bakedColor = (x, y, z) => {
  const layer = Math.max(0, Math.min(layers - 1, Math.round((z - start) / spacing)));
  const depth = layers > 1 ? depthDark + (1 - depthDark) * layer / (layers - 1) : 1;
  return (1 - (1 - shadeLevel / sunLevel) * shadowAt(x, y)) * depth;
};
// Pitch design shade (--design): the floor's texture colour runs from DARK to
// LIGHT (green channel 82.6 .. 96.6 of the pitch designs); in linear light the
// blade colour follows that ratio ^2.2. Vertex colours stop at 1, so they carry
// shade / light: the light bands are as bright as the classic grass, the dark
// ones darker (same material as the classic grass, no tint boost).
const DESIGN_DARK = 82.6, DESIGN_LIGHT = 96.6, DESIGN_MEAN = (DESIGN_DARK + DESIGN_LIGHT) / 2;
const DESIGN_K = Math.pow(DESIGN_LIGHT / DESIGN_MEAN, 2.2);
const designShade = (l) => Math.pow((DESIGN_DARK + (DESIGN_LIGHT - DESIGN_DARK) * l) / DESIGN_MEAN, 2.2) / DESIGN_K;
// Box-filtered pattern around a vertex (the vertex spacing wide), so the
// colour ramps across band edges instead of aliasing.
const designAt = (x, y, w) => {
  const pat = DESIGN_PATTERNS[design]; let s = 0;
  for (let j = 0; j < 4; j++) for (let i = 0; i < 4; i++) s += pat(x + ((i + 0.5) / 4 - 0.5) * w, y + ((j + 0.5) / 4 - 0.5) * w);
  return s / 16;
};
let designCtx = null; // null (white lines, classic) | { shade } per tile | { step } per vertex

// Does the shadow change along x / along y inside this rectangle? (sampled at
// half the step). A quad is only split along the axis that needs it.
const shadeVaries = (x0, y0, x1, y1) => {
  if (!shadowMask) return [false, false];
  const s = shadowStep / 2, xs = [], ys = [], tol = 0.1;
  for (let x = x0; x < x1; x += s) xs.push(x); xs.push(x1);
  for (let y = y0; y < y1; y += s) ys.push(y); ys.push(y1);
  let alongX = false, alongY = false;
  for (const y of ys) { const r = shadowAt(x0, y); if (xs.some((x) => Math.abs(shadowAt(x, y) - r) > tol)) { alongX = true; break; } }
  for (const x of xs) { const r = shadowAt(x, y0); if (ys.some((y) => Math.abs(shadowAt(x, y) - r) > tol)) { alongY = true; break; } }
  return [alongX, alongY];
};

// ---- mesh ------------------------------------------------------------------------
const positions = [], uvs = [], colors = [], faces = { green: [], white: [] };
const designFactor = (x, y) => !designCtx ? 1 : designCtx.shade ?? designShade(designAt(x, y, designCtx.step));
const vert = (x, y, z) => { positions.push([x, y, z]); uvs.push([x / tile, -y / tile]); if (bake) colors.push(bakedColor(x, y, z) * designFactor(x, y)); return positions.length - 1; };
const DESIGN_DIV = 8; // diamond / circles: vertices every 20 units in every tile
const quad = (set, x0, y0, x1, y1, z) => {
  // Split only where the baked shadow changes, so the soft edge has vertices.
  const [splitX, splitY] = shadeVaries(x0, y0, x1, y1);
  let nx = splitX ? Math.max(1, Math.ceil((x1 - x0) / shadowStep)) : 1, ny = splitY ? Math.max(1, Math.ceil((y1 - y0) / shadowStep)) : 1;
  if (designCtx?.step) { nx = Math.max(nx, DESIGN_DIV); ny = Math.max(ny, DESIGN_DIV); }
  if (nx === 1 && ny === 1) {
    const a = vert(x0, y0, z), b = vert(x1, y0, z), c = vert(x1, y1, z), d = vert(x0, y1, z);
    faces[set].push([a, b, c, d]);
    return;
  }
  const idx = [];
  for (let j = 0; j <= ny; j++) for (let i = 0; i <= nx; i++) idx.push(vert(x0 + (x1 - x0) * i / nx, y0 + (y1 - y0) * j / ny, z));
  for (let j = 0; j < ny; j++) for (let i = 0; i < nx; i++) {
    const a = idx[j * (nx + 1) + i];
    faces[set].push([a, a + 1, a + nx + 2, a + nx + 1]);
  }
};
const ringFaces = (cx, cy, rIn, rOut, a0, a1, z) => {
  const segs = Math.max(8, Math.ceil(Math.abs(a1 - a0) / (Math.PI / 48)));
  for (let s = 0; s < segs; s++) {
    const t0 = a0 + (a1 - a0) * s / segs, t1 = a0 + (a1 - a0) * (s + 1) / segs;
    const p = (r, t) => vert(cx + Math.cos(t) * r, cy + Math.sin(t) * r, z);
    faces.white.push([p(rIn, t0), p(rOut, t0), p(rOut, t1), p(rIn, t1)]);
  }
};
const discFaces = (cx, cy, r, z) => {
  const c = vert(cx, cy, z); const segs = 16;
  for (let s = 0; s < segs; s++) {
    const t0 = Math.PI * 2 * s / segs, t1 = Math.PI * 2 * (s + 1) / segs;
    faces.white.push([c, vert(cx + Math.cos(t0) * r, cy + Math.sin(t0) * r, z), vert(cx + Math.cos(t1) * r, cy + Math.sin(t1) * r, z)]);
  }
};
for (let l = 0; l < layers; l++) {
  const z = start + l * spacing;
  for (let ty = 0; ty < TILES_Y; ty++) for (let tx = 0; tx < TILES_X; tx++) {
    const x0 = -HALF_X + tx * TILE_W, y0 = -HALF_Y + ty * TILE_H;
    if (design) designCtx = designPerTile
      ? { shade: designShade(DESIGN_PATTERNS[design](x0 + TILE_W / 2, y0 + TILE_H / 2)) }
      : { step: Math.max(TILE_W, TILE_H) / DESIGN_DIV };
    quad("green", x0, y0, x0 + TILE_W, y0 + TILE_H, z);
    designCtx = null;
  }
  const zw = z + 0.01;                              // white just above green in each layer
  // Lines are cut along the tile grid, so each piece is lit with its own tile
  // (a whole touchline in one tile took one light value along its length).
  for (const r of rects) for (let ty = 0; ty < TILES_Y; ty++) for (let tx = 0; tx < TILES_X; tx++) {
    const x0 = Math.max(r[0], -HALF_X + tx * TILE_W), x1 = Math.min(r[2], -HALF_X + (tx + 1) * TILE_W);
    const y0 = Math.max(r[1], -HALF_Y + ty * TILE_H), y1 = Math.min(r[3], -HALF_Y + (ty + 1) * TILE_H);
    if (x1 - x0 > 0.01 && y1 - y0 > 0.01) quad("white", x0, y0, x1, y1, zw);
  }
  // 2026-09-29 owner: white blades beside the painted circle. The map paints
  // its arcs and spots as coarse polygons (the centre circle is a 24-gon,
  // r 251..256), our smooth rings were up to 2 units off. The curves and
  // spots now use the map's own line triangles (pitch-line-tris.json); the
  // straight lines above were already exact and stay tile-cut.
  for (const t of LINE_TRIS) {
    if (rects.some(([x0, y0, x1, y1]) => [0, 2, 4].every((i) => t[i] >= x0 - 0.01 && t[i] <= x1 + 0.01 && t[i + 1] >= y0 - 0.01 && t[i + 1] <= y1 + 0.01))) continue;
    // Half of the map's triangles are wound clockwise (seen from above). The lit fine grass draws
    // both sides, so those faced the ground and showed black blades in the circle, the arcs and
    // the spots (owner 2026-10-01, on the arena). All of them face up now.
    const up = (t[2] - t[0]) * (t[5] - t[1]) - (t[4] - t[0]) * (t[3] - t[1]) > 0;
    faces.white.push(up
      ? [vert(t[0], t[1], zw), vert(t[2], t[3], zw), vert(t[4], t[5], zw)]
      : [vert(t[0], t[1], zw), vert(t[4], t[5], zw), vert(t[2], t[3], zw)]);
  }
}

// ---- textures ----------------------------------------------------------------------
const TEX = 1024;
const color = new Float32Array(TEX * TEX * 3), alpha = new Float32Array(TEX * TEX);
// turf colour around the pitch texture's average (62, 90, 30), with soft variation
for (let y = 0; y < TEX; y++) for (let x = 0; x < TEX; x++) {
  const n = 0.85 + 0.15 * Math.sin(x * 0.037 + Math.sin(y * 0.021) * 2) * Math.cos(y * 0.029);
  const i = (y * TEX + x) * 3; color[i] = 58 * n; color[i + 1] = 92 * n; color[i + 2] = 28 * n;
}
// blade dots: short soft strokes, tiling seamlessly
for (let d = 0; d < dots; d++) {
  const cx = rand() * TEX, cy = rand() * TEX, len = 1.5 + rand() * 3.5, ang = rand() * Math.PI;
  const a = 0.55 + rand() * 0.45, shade = 0.75 + rand() * 0.55;
  const dx = Math.cos(ang), dy = Math.sin(ang);
  for (let t = -len; t <= len; t += 0.5) {
    for (let w = -1; w <= 1; w++) {
      const px = Math.round(cx + dx * t - dy * w * 0.6), py = Math.round(cy + dy * t + dx * w * 0.6);
      const x = ((px % TEX) + TEX) % TEX, y = ((py % TEX) + TEX) % TEX, i = y * TEX + x;
      const fall = (1 - Math.abs(t) / (len + 0.5)) * (w === 0 ? 1 : 0.45);
      alpha[i] = Math.max(alpha[i], a * fall);
      const ci = i * 3; color[ci] = 58 * shade; color[ci + 1] = 95 * shade; color[ci + 2] = 27 * shade;
    }
  }
}
function png(w, h, pixel) {
  const raw = Buffer.alloc((w * 3 + 1) * h);
  for (let y = 0; y < h; y++) { raw[y * (w * 3 + 1)] = 0; for (let x = 0; x < w; x++) pixel(x, y, raw, y * (w * 3 + 1) + 1 + x * 3); }
  const table = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
  const crc = (b) => { let c = 0xffffffff; for (const v of b) c = table[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (type, data) => { const len = Buffer.alloc(4); len.writeUInt32BE(data.length); const td = Buffer.concat([Buffer.from(type), data]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([len, td, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 2;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", zlib.deflateSync(raw, { level: 9 })), chunk("IEND", Buffer.alloc(0))]);
}
const clamp = (v) => Math.max(0, Math.min(255, Math.round(v)));
const greenPng = png(TEX, TEX, (x, y, b, o) => { const i = (y * TEX + x) * 3; b[o] = clamp(color[i]); b[o + 1] = clamp(color[i + 1]); b[o + 2] = clamp(color[i + 2]); });
const whitePng = png(TEX, TEX, (x, y, b, o) => { const v = clamp(215 + 30 * alpha[y * TEX + x]); b[o] = v; b[o + 1] = v; b[o + 2] = v; });
const transPng = png(TEX, TEX, (x, y, b, o) => { const v = clamp(alpha[y * TEX + x] * 255); b[o] = v; b[o + 1] = v; b[o + 2] = v; });

// ---- DMX (keyvalues2, one mesh, two face sets) ---------------------------------------
const id = () => crypto.randomUUID();
const q = (vals, ind) => vals.map((v) => `${ind}"${v}"`).join(",\n");
const f4 = (n) => Number(n.toFixed(4)).toString();
const faceSet = (name, mat, list) => `\t\t\t"DmeFaceSet"
\t\t\t{
\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t"name" "string" "${name}"
\t\t\t\t"faces" "int_array"
\t\t\t\t[
${q(list.flatMap((f) => [...f, -1]), "\t\t\t\t\t")}
\t\t\t\t]
\t\t\t\t"material" "DmeMaterial"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t\t"name" "string" "material"
\t\t\t\t\t"mtlName" "string" "${mat}.vmat"
\t\t\t\t}
\t\t\t}`;
function buildDmx(positions, uvs, faces, cols = null) {
const idx = q(positions.map((_, i) => i), "\t\t");
const I = { model: id(), dag: id(), bind: id() };
return `<!-- dmx encoding keyvalues2 4 format model 22 -->
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
\t\t"source" "string" "tools/grass/generate-shell-grass.mjs"
\t}
}

"DmeModel"
{
\t"id" "elementid" "${I.model}"
\t"name" "string" "grass_shell"
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
\t"name" "string" "grass_shell"
\t"transform" "DmeTransform"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"position" "vector3" "0 0 0"
\t\t"orientation" "quaternion" "0 0 0 1"
\t}
\t"shape" "DmeMesh"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "grass_shell"
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
${[faces.green.length ? faceSet("green", MAT_GREEN, faces.green) : null, faces.white.length ? faceSet("white", MAT_WHITE, faces.white) : null].filter(Boolean).join(",\n")}
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
\t\t"tangent$0"${cols ? ',\n\t\t"color$0"' : ""}
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
\t]${cols ? `
\t"color$0" "color_array"
\t[
${q(cols.map((c) => { const v = Math.round((vcLinear ? c : toGamma(c)) * 255); return `${v} ${v} ${v} 255`; }), "\t\t")}
\t]
\t"color$0Indices" "int_array"
\t[
${idx}
\t]` : ""}
}
`;
}

// --variant glow: material group "b" (skin 1) = the B materials.
const glowGroup = () => `\t\t\t\t\t{
\t\t\t\t\t\t_class = "MaterialGroup"
\t\t\t\t\t\tname = "b"
\t\t\t\t\t\tremaps =
\t\t\t\t\t\t[
\t\t\t\t\t\t\t{
\t\t\t\t\t\t\t\tfrom = "${MAT_GREEN}.vmat"
\t\t\t\t\t\t\t\tto = "${MAT_GREEN}_b.vmat"
\t\t\t\t\t\t\t},
\t\t\t\t\t\t\t{
\t\t\t\t\t\t\t\tfrom = "${MAT_WHITE}.vmat"
\t\t\t\t\t\t\t\tto = "${MAT_WHITE}_b.vmat"
\t\t\t\t\t\t\t},
\t\t\t\t\t\t]
\t\t\t\t\t},
`;
const vmdlFor = (model) => `<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc28:version{fb63b6ca-f435-4aa0-a2c7-c66ddc651dca} -->
{
\trootNode =
\t{
\t\t_class = "RootNode"
\t\tchildren =
\t\t[
\t\t\t{
\t\t\t\t_class = "BoneMarkupList"
\t\t\t\tchildren = [  ]
\t\t\t\tbone_cull_type = "None"
\t\t\t},
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
${glow ? glowGroup() : bake ? "" : `\t\t\t\t\t{
\t\t\t\t\t\t_class = "MaterialGroup"
\t\t\t\t\t\tname = "cutout"
\t\t\t\t\t\tremaps =
\t\t\t\t\t\t[
\t\t\t\t\t\t\t{
\t\t\t\t\t\t\t\tfrom = "${MAT_GREEN}.vmat"
\t\t\t\t\t\t\t\tto = "${MAT_GREEN}_cut.vmat"
\t\t\t\t\t\t\t},
\t\t\t\t\t\t\t{
\t\t\t\t\t\t\t\tfrom = "${MAT_WHITE}.vmat"
\t\t\t\t\t\t\t\tto = "${MAT_WHITE}_cut.vmat"
\t\t\t\t\t\t\t},
\t\t\t\t\t\t]
\t\t\t\t\t},
`}\t\t\t\t]
\t\t\t},
\t\t\t{
\t\t\t\t_class = "RenderMeshList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "RenderMeshFile"
\t\t\t\t\t\tname = "grass_shell"
\t\t\t\t\t\tfilename = "${model}.dmx"
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t]
\t}
}
`;
const vmat = (colorTex) => `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
\t"F_TRANSLUCENT"\t"1"
\t"F_RENDER_BACKFACES"\t"1"
\t"F_DO_NOT_CAST_SHADOWS"\t"1"
\t"TextureColor"\t"${colorTex}"
\t"TextureTranslucency"\t"materials/soccermod/grass_shell_trans.png"
\t"TextureRoughness"\t"[0.900000 0.900000 0.900000 0.000000]"
\t"SystemAttributes"
\t{
\t\t"PhysicsSurfaceProperties"\t"Grass"
\t}
}
`;

// 2026-09-26 owner: the roof shadow on the grass is blocky. Translucent
// materials get one light value per model (so per tile); opaque ones are lit
// per pixel like the floor. Material group 1 ("cutout", skin 1) swaps in
// alpha-tested copies of both materials: the gaps between the blades show
// the real floor with its baked shadow and mowing stripes.
const vmatCut = (colorTex) => vmat(colorTex)
  .replace(`\t"F_TRANSLUCENT"\t"1"\n`, `\t"F_ALPHA_TEST"\t"1"\n\t"g_flAlphaTestReference"\t"${alphaRef.toFixed(3)}"\n`);
// 2026-09-28: 0.40 like the bake grass (was 0.500: thin, see-through lines
// and blades from a distance on the design pitches).

// --variant glow (see the header). level = the unlit bake level (sun x
// exposure); A: brightness level / GLOW_TINT, B: brightness level.
const GLOW_TINT = 0.1;
const vmatGlow = (colorTex, exposure, untinted) => {
  const level = sunLevel * exposure;
  const c = toGamma(GLOW_TINT).toFixed(6);
  return `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
\t"F_ALPHA_TEST"\t"1"
\t"F_PAINT_VERTEX_COLORS"\t"1"
\t"F_SELF_ILLUM"\t"1"
\t"F_RENDER_BACKFACES"\t"1"
\t"F_DO_NOT_CAST_SHADOWS"\t"1"
\t"g_flAlphaTestReference"\t"${alphaRef.toFixed(3)}"
\t"g_flMetalness"\t"0.000"
\t"g_flReflectance"\t"0.000"
\t"g_vColorTint"\t"[${c} ${c} ${c} 0.000000]"
\t"g_flSelfIllumAlbedoFactor"\t"1.000"
\t"g_flSelfIllumBrightness"\t"${(untinted ? level : level / GLOW_TINT).toFixed(3)}"
\t"g_flSelfIllumScale"\t"1.000"
\t"g_vSelfIllumTint"\t"[1.000000 1.000000 1.000000 0.000000]"
\t"TextureColor"\t"${colorTex}"
\t"TextureTranslucency"\t"${TEX_TRANS}"
\t"TextureSelfIllumMask"\t"[1.000000 1.000000 1.000000 0.000000]"
\t"TextureRoughness"\t"[1.000000 1.000000 1.000000 0.000000]"
\t"SystemAttributes"
\t{
\t\t"PhysicsSurfaceProperties"\t"Grass"
\t}
}
`;
};

// Unlit cut-out for --variant bake (see the header): no light, no shadow, the
// baked vertex colours x the sun level in g_vColorTint (gamma-encoded, the
// shader applies SrgbGammaToLinear to it).
const vmatBake = (colorTex, exposure = 1) => {
  const t = toGamma(sunLevel * exposure).toFixed(6);
  return `"Layer0"
{
\t"shader"\t"csgo_static_overlay.vfx"
\t"F_LIT"\t"0"
\t"F_BLEND_MODE"\t"2"
\t"F_PAINT_VERTEX_COLORS"\t"1"
\t"F_RENDER_BACKFACES"\t"1"
\t"F_DO_NOT_CAST_SHADOWS"\t"1"
\t"g_flAlphaTestReference"\t"${alphaRef.toFixed(3)}"
\t"g_vColorTint"\t"[${t} ${t} ${t} 0.000000]"
\t"TextureColor"\t"${colorTex}"
\t"TextureTranslucency"\t"${TEX_TRANS}"
\t"SystemAttributes"
\t{
\t\t"PhysicsSurfaceProperties"\t"Grass"
\t}
}
`;
};

// --preview <dir> (bake): top layer shading rasterised from the real mesh
// (bilinear per quad) next to the floor as the map lights it, plus one tile
// on the shadow edge with the cut-out blades. Display = gamma(level / sun).
function writePreviews(dir) {
  fs.mkdirSync(dir, { recursive: true });
  const topZ = start + (layers - 1) * spacing;
  const shadeImg = (W, H, x0, y0, scale) => {        // scale = world units per pixel
    const img = new Float32Array(W * H).fill(-1);
    for (const f of faces.green) {
      if (f.length !== 4 || Math.abs(positions[f[0]][2] - topZ) > 1e-3) continue;
      const [a, , c] = f, qx0 = positions[a][0], qy0 = positions[a][1], qx1 = positions[c][0], qy1 = positions[c][1];
      const px0 = Math.max(0, Math.ceil((qx0 - x0) / scale - 0.5)), px1 = Math.min(W - 1, Math.floor((qx1 - x0) / scale - 0.5));
      const py0 = Math.max(0, Math.ceil((y0 - qy1) / scale - 0.5)), py1 = Math.min(H - 1, Math.floor((y0 - qy0) / scale - 0.5));
      for (let py = py0; py <= py1; py++) for (let px = px0; px <= px1; px++) {
        const wx = x0 + (px + 0.5) * scale, wy = y0 - (py + 0.5) * scale;
        const fx = (wx - qx0) / (qx1 - qx0), fy = (wy - qy0) / (qy1 - qy0);
        const k = f.map((i) => colors[i]);
        img[py * W + px] = (k[0] * (1 - fx) + k[1] * fx) * (1 - fy) + (k[3] * (1 - fx) + k[2] * fx) * fy;
      }
    }
    return img;
  };
  const g = (l) => clamp(toGamma(Math.max(0, Math.min(1, l))) * 255);
  // 1) whole pitch, 10 u/px: floor (map mask) | gap | baked grass top layer
  const PW = 256, PH = 333, gap = 8, grass = shadeImg(PW, PH, -HALF_X, HALF_Y, 10);
  const floor = (px, py) => 1 - (1 - shadeLevel / sunLevel) * shadowAt(-HALF_X + (px + 0.5) * 10, HALF_Y - (py + 0.5) * 10);
  fs.writeFileSync(path.join(dir, "grass_bake_pitch_preview.png"), png(PW * 2 + gap, PH, (x, y, b, o) => {
    let v = 128;
    if (x < PW) v = g(floor(x, y)); else if (x >= PW + gap) { const s = grass[y * PW + x - PW - gap]; v = s < 0 ? 255 : g(s); }
    b[o] = v; b[o + 1] = v; b[o + 2] = v;
  }));
  // 2) one tile on the roof-shadow edge, 2 px/u: shading | cut-out blades over a flat floor
  const tx = 2, ty = 10, S = 0.5, TW = Math.round(TILE_W / S), TH = Math.round(TILE_H / S);
  const x0 = -HALF_X + tx * TILE_W, y1 = -HALF_Y + (ty + 1) * TILE_H, tileShade = shadeImg(TW, TH, x0, y1, S);
  const lin = (v) => Math.pow(v / 255, 2.2);
  fs.writeFileSync(path.join(dir, `grass_bake_tile_${tx}_${ty}_preview.png`), png(TW * 2 + gap, TH, (x, y, b, o) => {
    if (x < TW) { const v = g(tileShade[y * TW + x]); b[o] = v; b[o + 1] = v; b[o + 2] = v; return; }
    if (x < TW + gap) { b[o] = b[o + 1] = b[o + 2] = 128; return; }
    const px = x - TW - gap, wx = x0 + (px + 0.5) * S, wy = y1 - (y + 0.5) * S;
    const tu = (((wx / tile) % 1) + 1) % 1, tv = (((-wy / tile) % 1) + 1) % 1;
    const ti = Math.min(TEX - 1, Math.floor(tv * TEX)) * TEX + Math.min(TEX - 1, Math.floor(tu * TEX));
    const shade = alpha[ti] >= 0.5 ? tileShade[y * TW + px] : 0.8 * floor((wx + HALF_X) / 10 - 0.5, (HALF_Y - wy) / 10 - 0.5);
    for (let c = 0; c < 3; c++) b[o + c] = g(lin(alpha[ti] >= 0.5 ? color[ti * 3 + c] : [58, 90, 30][c]) * shade * 2.5);
  }));
  console.log(`previews: ${dir}/grass_bake_pitch_preview.png, grass_bake_tile_${tx}_${ty}_preview.png`);
}

const write = (rel, data) => { const out = path.join(addon, rel); fs.mkdirSync(path.dirname(out), { recursive: true }); fs.writeFileSync(out, data); console.log(`wrote ${rel} (${fs.statSync(out).size} B)`); };
// Split into tiles: every face goes to the tile holding its centroid; the
// vertices become local to the tile centre (the plugin spawns each tile
// there), UVs stay world-based so the texture runs on seamlessly.
let tilesWritten = 0;
const PER_X = 1, PER_Y = 1, OUT_X = TILES_X / PER_X, OUT_Y = TILES_Y / PER_Y;
for (let ty = 0; ty < OUT_Y; ty++) for (let tx = 0; tx < OUT_X; tx++) {
  const OW = TILE_W * PER_X, OH = TILE_H * PER_Y;
  const cx = -HALF_X + (tx + 0.5) * OW, cy = -HALF_Y + (ty + 0.5) * OH;
  const tileOf = (v, half, size, n) => Math.min(n - 1, Math.max(0, Math.floor((v + half) / size)));
  const inTile = (f) => {
    const mx = f.reduce((a, i) => a + positions[i][0], 0) / f.length, my = f.reduce((a, i) => a + positions[i][1], 0) / f.length;
    return tileOf(mx, HALF_X, OW, OUT_X) === tx && tileOf(my, HALF_Y, OH, OUT_Y) === ty;
  };
  const map = new Map(), P = [], U = [], C = [], out = { green: [], white: [] };
  const local = (i) => { if (!map.has(i)) { map.set(i, P.length); P.push([positions[i][0] - cx, positions[i][1] - cy, positions[i][2]]); U.push(uvs[i]); C.push(colors[i]); } return map.get(i); };
  for (const set of ["green", "white"]) for (const f of faces[set]) if (inTile(f)) out[set].push(f.map(local));
  const name = `${MODEL}_${tx}_${ty}`;
  write(`${name}.dmx`, buildDmx(P, U, out, shadowMask ? C : null));
  write(`${name}.vmdl`, vmdlFor(name));
  tilesWritten++;
}
console.log(`tiles: ${tilesWritten} (${OUT_X} x ${OUT_Y}, ${TILE_W * PER_X} x ${TILE_H * PER_Y} units)`);
if (design) {
  // Only the tile models: both materials are the bake grass's.
  console.log(`grass design ${design}: ${faces.green.length} green + ${faces.white.length} white faces, ${positions.length} vertices`);
  process.exit(0);
}
if (glow) {
  write(`${MAT_GREEN}.vmat`, vmatGlow(TEX_GREEN, greenExposure, false));
  write(`${MAT_WHITE}.vmat`, vmatGlow(TEX_WHITE, 1, false));
  write(`${MAT_GREEN}_b.vmat`, vmatGlow(TEX_GREEN, greenExposure, true));
  write(`${MAT_WHITE}_b.vmat`, vmatGlow(TEX_WHITE, 1, true));
  console.log(`grass glow: ${faces.green.length} green + ${faces.white.length} white faces, ${positions.length} vertices`);
  process.exit(0);
}
if (bake) {
  // Only the new files: the shared textures and the grass_fine_* set stay untouched.
  write(`${MAT_GREEN}.vmat`, vmatBake(TEX_GREEN, greenExposure));
  write(`${MAT_WHITE}.vmat`, vmatBake(TEX_WHITE));
  console.log(`bake: sun ${sunLevel} (tint ${toGamma(sunLevel).toFixed(4)}), shade ${shadeLevel} (x${(shadeLevel / sunLevel).toFixed(3)}), ` +
    `depth-dark ${depthDark}, step ${shadowStep}, vertex colours ${vcLinear ? "linear" : "gamma-encoded"}`);
  if (args.preview) writePreviews(args.preview);
  console.log(`grass bake: ${faces.green.length} green + ${faces.white.length} white faces, ${positions.length} vertices`);
  process.exit(0);
}
write(`${MAT_GREEN}.vmat`, vmat(`${MAT_GREEN}_color.png`));
write(`${MAT_WHITE}.vmat`, vmat(`${MAT_WHITE}_color.png`));
write(`${MAT_GREEN}_cut.vmat`, vmatCut(`${MAT_GREEN}_color.png`));
write(`${MAT_WHITE}_cut.vmat`, vmatCut(`${MAT_WHITE}_color.png`));
write(`${MAT_GREEN}_color.png`, greenPng);
write(`${MAT_WHITE}_color.png`, whitePng);
write("materials/soccermod/grass_shell_trans.png", transPng);
console.log(`grass shell: ${layers} layers (${start}..${start + (layers - 1) * spacing} above the floor), ` +
  `${faces.green.length} green + ${faces.white.length} white faces, ${positions.length} vertices`);
