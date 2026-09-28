#!/usr/bin/env node
// Kickoff wall "light curtain" (2026-09-27 owner picked mockup option B).
//
// One model for the whole wall, in the plugin's kickoff frame: origin = the
// centre spot on the grass, +x along the halfway line, the centre-circle arc
// bulges to local -y (the plugin turns it 180 deg for the other half).
//   halfway line : x = +-252.5 .. +-1280 (foundation walls), y = 0
//   arc          : radius 252.5, 32 segments
//   height       : 0 .. HEIGHT, two-sided (faces in both windings)
// Material: csgo_complex translucent + self-illum (see vmat below). The colour
// texture fades from a bright foot to nothing at the top; mask 1 holds soft
// vertical light streaks and pans upward (g_vMask1PanSpeed). Two material
// groups: default = red, "blue" = blue (skin 1).
//
// usage: node tools/kickoff/generate-kickoff-curtain.mjs <csgo_addons/soccermod_menu>
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import crypto from "node:crypto";

const out = process.argv[2];
if (!out) { console.error("usage: generate-kickoff-curtain.mjs <addon content dir>"); process.exit(1); }

const RADIUS = 252.5, WALL_X = 1280, HEIGHT = 120, ARC_SEGMENTS = 32, U_PER_UNIT = 1 / 128;
const MODEL = "models/soccermod/kickoff/kickoff_curtain";
const MAT_DIR = "materials/soccermod/kickoff";
const MAT_RED = `${MAT_DIR}/curtain_red`, MAT_BLUE = `${MAT_DIR}/curtain_blue`;

// ---- geometry: a polyline along the ground, extruded up -----------------------------
const polyline = [];
polyline.push([[-WALL_X, 0], [-RADIUS, 0]]);
const arc = [];
for (let i = 0; i <= ARC_SEGMENTS; i++) {
  const a = Math.PI - (i * Math.PI) / ARC_SEGMENTS; // -x .. +x through -y
  arc.push([RADIUS * Math.cos(a), -RADIUS * Math.sin(a)]);
}
polyline.push(arc);
polyline.push([[RADIUS, 0], [WALL_X, 0]]);

const positions = [], uvs = [], normals = [], faces = [];
for (const line of polyline) {
  let u = 0;
  for (let i = 0; i < line.length - 1; i++) {
    const [x0, y0] = line[i], [x1, y1] = line[i + 1];
    const len = Math.hypot(x1 - x0, y1 - y0);
    const u1 = u + len * U_PER_UNIT;
    const nx = -(y1 - y0) / len, ny = (x1 - x0) / len;
    // two sides: front winding with +n, back winding with -n
    for (const side of [1, -1]) {
      const b = positions.length;
      positions.push([x0, y0, 0], [x1, y1, 0], [x1, y1, HEIGHT], [x0, y0, HEIGHT]);
      uvs.push([u, 1], [u1, 1], [u1, 0], [u, 0]);
      for (let k = 0; k < 4; k++) normals.push([nx * side, ny * side, 0]);
      faces.push(side > 0 ? [b, b + 1, b + 2, b + 3] : [b, b + 3, b + 2, b + 1]);
    }
    u = u1;
  }
}

// ---- textures ---------------------------------------------------------------------
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
// v = 0 top (y = 0 in the image) .. 1 foot. Intensity: bright foot line,
// then a smooth fade to 0 at the top. A whiter core near the foot.
function curtainColor(rgb) {
  const W = 64, H = 256;
  return png(W, H, (x, y, b, o) => {
    const v = y / (H - 1);                      // 0 top .. 1 foot
    // Full team colour everywhere (owner: the dark upper part looked grey -
    // translucency alone fades it out now), whiter towards the foot line.
    const foot = v > 0.955 ? 1 : v > 0.92 ? (v - 0.92) / 0.035 : 0;
    const white = foot * 0.55 + Math.pow(v, 6) * 0.25;
    for (let c = 0; c < 3; c++) b[o + c] = clamp(255 * (rgb[c] * (1 - white) + white));
  });
}
// Soft vertical streaks, each a short segment so an upward pan reads as
// light running up. Floor 0.55 so the curtain never vanishes between streaks.
function streakMask() {
  const S = 256, rnd = mulberry32(27);
  const m = new Float32Array(S * S);
  for (let k = 0; k < 26; k++) {
    const cx = rnd() * S, cy = rnd() * S, w = 1.5 + rnd() * 3, len = 30 + rnd() * 90, a = 0.5 + rnd() * 0.5;
    for (let y = 0; y < S; y++) for (let dx = -8; dx <= 8; dx++) {
      const x = Math.round(cx + dx), xx = ((x % S) + S) % S;
      let dy = Math.abs(y - cy); dy = Math.min(dy, S - dy);
      const across = Math.exp(-(dx * dx) / (2 * w * w)), along = Math.max(0, 1 - dy / (len / 2));
      m[y * S + xx] = Math.max(m[y * S + xx], a * across * along * along);
    }
  }
  return png(S, S, (x, y, b, o) => { const v = clamp(255 * (0.55 + 0.45 * m[y * S + x])); b[o] = v; b[o + 1] = v; b[o + 2] = v; });
}
function mulberry32(a) { return () => { a |= 0; a = (a + 0x6d2b79f5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }

// ---- DMX (keyvalues2) ---------------------------------------------------------------
const id = () => crypto.randomUUID();
const q = (vals, ind) => vals.map((v) => `${ind}"${v}"`).join(",\n");
const f4 = (n) => Number(n.toFixed(4)).toString();
function dmxFor(name, positions, uvs, normals, faces, mtl) {
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
\t\t"source" "string" "tools/kickoff/generate-kickoff-curtain.mjs"
\t}
}

"DmeModel"
{
\t"id" "elementid" "${I.model}"
\t"name" "string" "${name}"
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
\t"name" "string" "${name}"
\t"transform" "DmeTransform"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"position" "vector3" "0 0 0"
\t\t"orientation" "quaternion" "0 0 0 1"
\t}
\t"shape" "DmeMesh"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "${name}"
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
\t\t\t"DmeFaceSet"
\t\t\t{
\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t"name" "string" "curtain"
\t\t\t\t"faces" "int_array"
\t\t\t\t[
${q(faces.flatMap((f) => [...f, -1]), "\t\t\t\t\t")}
\t\t\t\t]
\t\t\t\t"material" "DmeMaterial"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t\t"name" "string" "material"
\t\t\t\t\t"mtlName" "string" "${mtl}.vmat"
\t\t\t\t}
\t\t\t}
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
\t\t"tangent$0"
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
${q(normals.map((p) => p.map(f4).join(" ")), "\t\t")}
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
}
`;
}
const dmx = dmxFor("kickoff_curtain", positions, uvs, normals, faces, MAT_RED);

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
\t\t\t\t\t{
\t\t\t\t\t\t_class = "MaterialGroup"
\t\t\t\t\t\tname = "blue"
\t\t\t\t\t\tremaps =
\t\t\t\t\t\t[
\t\t\t\t\t\t\t{
\t\t\t\t\t\t\t\tfrom = "${MAT_RED}.vmat"
\t\t\t\t\t\t\t\tto = "${MAT_BLUE}.vmat"
\t\t\t\t\t\t\t},
\t\t\t\t\t\t]
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t\t{
\t\t\t\t_class = "RenderMeshList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "RenderMeshFile"
\t\t\t\t\t\tname = "kickoff_curtain"
\t\t\t\t\t\tfilename = "${MODEL}.dmx"
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t]
\t}
}
`;

// Material: csgo_complex, translucent + self-illumination (the set-up of
// the stock de_dust window glow, read with ValveResourceFormat). Two
// csgo_effects versions (additive, then with the full parameter set) spawned
// fine but stayed invisible in game. Colour = team colour, translucency =
// bright foot fading upward, self-illum mask = the light streaks, scrolled
// upward with g_vSelfIllumScrollSpeed.
const vmat = (colorTex) => `"Layer0"
{
	"shader"	"csgo_complex.vfx"
	"F_TRANSLUCENT"	"1"
	"F_SELF_ILLUM"	"1"
	"F_RENDER_BACKFACES"	"1"
	"F_DO_NOT_CAST_SHADOWS"	"1"
	"g_bFogEnabled"	"1"
	"g_flMetalness"	"0.000"
	"g_flModelTintAmount"	"1.000"
	"g_flSelfIllumAlbedoFactor"	"1.000"
	"g_flSelfIllumBrightness"	"2.500"
	"g_flSelfIllumScale"	"1.000"
	"g_vColorTint"	"[1.000000 1.000000 1.000000 0.000000]"
	"g_vSelfIllumTint"	"[1.000000 1.000000 1.000000 0.000000]"
	"g_vSelfIllumScrollSpeed"	"[0.000 -0.180]"
	"g_vTexCoordScale"	"[1.000 1.000]"
	"TextureColor"	"${colorTex}"
	"TextureTranslucency"	"${MAT_DIR}/curtain_trans.png"
	"TextureSelfIllumMask"	"${MAT_DIR}/curtain_streaks.png"
	"TextureRoughness"	"[1.000000 1.000000 1.000000 0.000000]"
}
`;
// Translucency: the same foot line + upward fade as the colour, as grey.
function curtainTrans() {
  const W = 64, H = 256;
  return png(W, H, (x, y, b, o) => {
    const v = y / (H - 1);
    // Owner: see through to the other side - the upper 40 % is fully clear,
    // the glow stays low; the last rows stay 0 so wrap sampling at the top
    // edge (v = 0) cannot bleed the foot line onto the upper border.
    const body = v < 0.4 ? 0 : Math.pow((v - 0.4) / 0.6, 2.5) * 0.45;
    const fade = v > 0.99 ? 0 : body;
    const foot = v > 0.99 ? 0 : v > 0.955 ? 0.9 : v > 0.92 ? 0.9 * (v - 0.92) / 0.035 : 0;
    const t = clamp(255 * Math.min(1, fade + foot));
    b[o] = t; b[o + 1] = t; b[o + 2] = t;
  });
}

// ---- roof screen colon blocker --------------------------------------------------------
// The stadium's roof screens have the old scoreboard colon (two white dots)
// baked into the map. A small opaque black plate, spawned by the plugin just
// in front of it (MapScoreText.cs), hides it. Plate in the x/z plane (the
// screens face +-y), centred on the origin, two-sided.
const BLOCK_W = 56, BLOCK_H = 40;
const BLOCK_MODEL = "models/soccermod/scoreboard/colon_blocker";
const BLOCK_MAT = "materials/soccermod/scoreboard/colon_blocker";
const bp = [], buv = [], bn = [], bf = [];
for (const side of [1, -1]) {
  const b = bp.length;
  bp.push([-BLOCK_W / 2, 0, -BLOCK_H / 2], [BLOCK_W / 2, 0, -BLOCK_H / 2], [BLOCK_W / 2, 0, BLOCK_H / 2], [-BLOCK_W / 2, 0, BLOCK_H / 2]);
  buv.push([0, 1], [1, 1], [1, 0], [0, 0]);
  for (let k = 0; k < 4; k++) bn.push([0, -side, 0]);
  bf.push(side > 0 ? [b, b + 1, b + 2, b + 3] : [b, b + 3, b + 2, b + 1]);
}
const blockVmdl = vmdl
  .replace(/\t\t\t\t\t\{\n\t\t\t\t\t\t_class = "MaterialGroup"[\s\S]*?\n\t\t\t\t\t\},\n/, "")
  .replace(`name = "kickoff_curtain"`, `name = "colon_blocker"`)
  .replace(`${MODEL}.dmx`, `${BLOCK_MODEL}.dmx`);
// Opaque black: the unlit csgo_static_overlay set-up the bake grass uses
// (F_LIT 0, alpha test), with an all-white translucency so nothing is cut.
const blockVmat = `"Layer0"
{
	"shader"	"csgo_static_overlay.vfx"
	"F_LIT"	"0"
	"F_BLEND_MODE"	"2"
	"F_RENDER_BACKFACES"	"1"
	"F_DO_NOT_CAST_SHADOWS"	"1"
	"g_flAlphaTestReference"	"0.500"
	"g_vColorTint"	"[0.000000 0.000000 0.000000 0.000000]"
	"TextureColor"	"${BLOCK_MAT}_color.png"
	"TextureTranslucency"	"${BLOCK_MAT}_trans.png"
}
`;

// ---- perimeter wall (owner 2026-09-27) ----------------------------------------------
// Replaces the map's red metal railings (28 prop_dynamic metal_railing_001
// pieces the plugin hides, keeping their collision) with a low anthracite wall
// like the XSL stadium had. One model in map coordinates, origin on the pitch
// floor: long sides x = +-1282 for |y| 129..1665 (gap at the halfway line, as
// the railings), short sides y = +-1666 for x -1281..1279.
const WALL_H = 40, WALL_T = 8, WALL_TOP = 3;
const WALL_MODEL = "models/soccermod/stadium/perimeter_wall";
const WALL_MAT = "materials/soccermod/stadium/perimeter_wall";
const wp = [], wuv = [], wn = [], wf = [];
function wallBox(x0, y0, x1, y1) {
  // axis-aligned box from (x0,y0) to (x1,y1) on the ground, thickness WALL_T
  const horizontal = Math.abs(x1 - x0) > Math.abs(y1 - y0);
  const hx = horizontal ? 0 : WALL_T / 2, hy = horizontal ? WALL_T / 2 : 0;
  const ax = Math.min(x0, x1) - hx, bx = Math.max(x0, x1) + hx, ay = Math.min(y0, y1) - hy, by = Math.max(y0, y1) + hy;
  const quad = (p, n, uvw) => { const b = wp.length; wp.push(...p); wn.push(n, n, n, n); wuv.push(...uvw); wf.push([b, b + 1, b + 2, b + 3]); };
  const len = horizontal ? bx - ax : by - ay, u = len / 64, v = 1;
  // sides (outward normals), top
  quad([[ax, ay, 0], [bx, ay, 0], [bx, ay, WALL_H], [ax, ay, WALL_H]], [0, -1, 0], [[0, 1], [u, 1], [u, 0], [0, 0]]);
  quad([[bx, by, 0], [ax, by, 0], [ax, by, WALL_H], [bx, by, WALL_H]], [0, 1, 0], [[0, 1], [u, 1], [u, 0], [0, 0]]);
  quad([[ax, by, 0], [ax, ay, 0], [ax, ay, WALL_H], [ax, by, WALL_H]], [-1, 0, 0], [[0, 1], [u, 1], [u, 0], [0, 0]]);
  quad([[bx, ay, 0], [bx, by, 0], [bx, by, WALL_H], [bx, ay, WALL_H]], [1, 0, 0], [[0, 1], [u, 1], [u, 0], [0, 0]]);
  quad([[ax, ay, WALL_H], [bx, ay, WALL_H], [bx, by, WALL_H], [ax, by, WALL_H]], [0, 0, 1], [[0, 1], [u, 1], [u, 0], [0, 0]]);
}
for (const sx of [-1282, 1282]) { wallBox(sx, 129, sx, 1665); wallBox(sx, -1665, sx, -129); }
for (const sy of [-1666, 1666]) wallBox(-1281 - WALL_T / 2, sy, 1279 + WALL_T / 2, sy);
const wallVmdl = blockVmdl.replace(`name = "colon_blocker"`, `name = "perimeter_wall"`).replace(`${BLOCK_MODEL}.dmx`, `${WALL_MODEL}.dmx`);
// Anthracite (RAL 7016-like) with a faint lighter top edge band in the texture.
// 2026-09-28 owner: the wall as dark formwork concrete (reference photo:
// anthracite panels with seams and tie holes). Our own procedural texture,
// not the photo: one panel per 64 units (u repeats every 64, v = wall
// height), a vertical seam at the panel edge, four tie holes, pores, faint
// stains; a normal map from the same height field.
const wallVmat = `"Layer0"
{
	"shader"	"csgo_complex.vfx"
	"F_RENDER_BACKFACES"	"1"
	"g_flMetalness"	"0.000"
	"TextureColor"	"${WALL_MAT}_color.png"
	"TextureNormal"	"${WALL_MAT}_normal.png"
	"TextureRoughness"	"[0.900000 0.900000 0.900000 0.000000]"
}
`;
const CW = 512, CH = 256; // power of two (the texture compiler needs it for the mips)
const rndC = mulberry32(2809);
const lat = Array.from({ length: 4 }, (_, o) => { const n = 8 << o; return { n, v: Float32Array.from({ length: n * n }, () => rndC()) }; });
const vnoise = (x, y) => { // tileable value noise, x,y in 0..1
  let t = 0, amp = 0.5, sum = 0;
  for (const { n, v } of lat) {
    const fx = x * n, fy = y * n, x0 = Math.floor(fx), y0 = Math.floor(fy), tx = fx - x0, ty = fy - y0;
    const g = (i, j) => v[((j % n + n) % n) * n + ((i % n + n) % n)];
    const sx = tx * tx * (3 - 2 * tx), sy = ty * ty * (3 - 2 * ty);
    const val = (g(x0, y0) * (1 - sx) + g(x0 + 1, y0) * sx) * (1 - sy) + (g(x0, y0 + 1) * (1 - sx) + g(x0 + 1, y0 + 1) * sx) * sy;
    t += val * amp; sum += amp; amp *= 0.5;
  }
  return t / sum;
};
const holes = [[0.2, 0.26], [0.8, 0.26], [0.2, 0.74], [0.8, 0.74]];
const pores = Array.from({ length: 260 }, () => [rndC() * CW, rndC() * CH, 0.6 + rndC() * 1.6]);
const heightC = new Float32Array(CW * CH), colC = new Float32Array(CW * CH);
for (let y = 0; y < CH; y++) for (let x = 0; x < CW; x++) {
  const u = x / CW, v = y / CH;
  let h = 0.5 + (vnoise(u * 2, v * 2) - 0.5) * 0.25;
  let c = 0.19 + (vnoise(u + 0.37, v * 1.6 + 0.11) - 0.5) * 0.16 + (vnoise(u * 3 + 0.5, v * 0.4) - 0.5) * 0.06 + (rndC() - 0.5) * 0.03;
  const seam = Math.min(x, CW - 1 - x); // vertical panel seam at the texture edge
  if (seam < 4) { h -= 0.6 * (1 - seam / 4); c *= 0.4 + 0.15 * seam; }
  for (const [hx, hy] of holes) {
    const d = Math.hypot(x - hx * CW, (y - hy * CH)) ;
    if (d < 9) { h -= 0.6 * (1 - (d / 9) ** 2); c *= d < 7 ? 0.45 : 0.8; }
  }
  heightC[y * CW + x] = h; colC[y * CW + x] = c;
}
for (const [px, py, r] of pores) {
  for (let dy = -3; dy <= 3; dy++) for (let dx = -3; dx <= 3; dx++) {
    const x = Math.round(px + dx), y = Math.round(py + dy); if (x < 0 || y < 0 || x >= CW || y >= CH) continue;
    const d = Math.hypot(dx, dy); if (d > r) continue;
    heightC[y * CW + x] -= 0.25 * (1 - d / r); colC[y * CW + x] *= 0.7;
  }
}
const wallColor = png(CW, CH, (x, y, b, o) => {
  const c = colC[y * CW + x] * 255;
  b[o] = clamp(c * 0.98); b[o + 1] = clamp(c * 1.0); b[o + 2] = clamp(c * 1.04);
});
const wallNormal = png(CW, CH, (x, y, b, o) => {
  const H = (i, j) => heightC[Math.min(CH - 1, Math.max(0, j)) * CW + ((i % CW) + CW) % CW];
  const k = 3.0, nx = (H(x - 1, y) - H(x + 1, y)) * k, ny = (H(x, y - 1) - H(x, y + 1)) * k, len = Math.hypot(nx, ny, 1);
  b[o] = clamp((nx / len * 0.5 + 0.5) * 255); b[o + 1] = clamp((ny / len * 0.5 + 0.5) * 255); b[o + 2] = clamp((1 / len * 0.5 + 0.5) * 255);
});

const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
write(`${MODEL}.dmx`, dmx);
write(`${MODEL}.vmdl`, vmdl);
write(`${MAT_DIR}/curtain_red_color.png`, curtainColor([1.0, 0.22, 0.18]));
write(`${MAT_DIR}/curtain_blue_color.png`, curtainColor([0.22, 0.55, 1.0]));
write(`${MAT_DIR}/curtain_streaks.png`, streakMask());
write(`${MAT_DIR}/curtain_trans.png`, curtainTrans());
write(`${MAT_RED}.vmat`, vmat(`${MAT_DIR}/curtain_red_color.png`));
write(`${MAT_BLUE}.vmat`, vmat(`${MAT_DIR}/curtain_blue_color.png`));
write(`${BLOCK_MODEL}.dmx`, dmxFor("colon_blocker", bp, buv, bn, bf, BLOCK_MAT));
write(`${BLOCK_MODEL}.vmdl`, blockVmdl);
write(`${BLOCK_MAT}_color.png`, png(8, 8, (x, y, b, o) => { b[o] = 0; b[o + 1] = 0; b[o + 2] = 0; }));
write(`${BLOCK_MAT}_trans.png`, png(8, 8, (x, y, b, o) => { b[o] = 255; b[o + 1] = 255; b[o + 2] = 255; }));
write(`${BLOCK_MAT}.vmat`, blockVmat);
write(`${WALL_MODEL}.dmx`, dmxFor("perimeter_wall", wp, wuv, wn, wf, WALL_MAT));
write(`${WALL_MODEL}.vmdl`, wallVmdl);
write(`${WALL_MAT}_color.png`, wallColor);
write(`${WALL_MAT}_normal.png`, wallNormal);
write(`${WALL_MAT}.vmat`, wallVmat);
console.log(`perimeter wall: ${wf.length} quads`);
console.log(`kickoff curtain: ${positions.length} verts, ${faces.length} quads; colon blocker ${BLOCK_W}x${BLOCK_H} -> ${out}`);
