#!/usr/bin/env node
// Kickoff wall "light curtain" (2026-09-27 owner picked mockup option B).
//
// One model for the whole wall, in the plugin's kickoff frame: origin = the
// centre spot on the grass, +x along the halfway line, the centre-circle arc
// bulges to local -y (the plugin turns it 180 deg for the other half).
//   halfway line : x = +-252.5 .. +-1280 (foundation walls), y = 0
//   arc          : radius 252.5, 32 segments
//   height       : 0 .. HEIGHT, two-sided (faces in both windings)
// Material: csgo_effects.vfx, additive (black = invisible), unlit. The colour
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
    const fade = v > 0.99 ? 0 : Math.pow(v, 2.2) * 0.75; // body
    // Bright base line; the last rows stay black so wrap sampling at the top
    // edge (v = 0) cannot bleed the foot line onto the upper border.
    const foot = v > 0.99 ? 0 : v > 0.955 ? 1 : v > 0.92 ? (v - 0.92) / 0.035 : 0;
    const i = Math.min(1, fade + foot);
    const white = foot * 0.55 + Math.pow(v, 6) * 0.25;
    for (let c = 0; c < 3; c++) b[o + c] = clamp(255 * i * (rgb[c] * (1 - white) + white));
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

// csgo_effects parameters: the full set the stock effect materials carry
// (read from de_vertigo glow / de_train sun glow with ValveResourceFormat).
// Left out, the shader defaults (fade max, fresnel max, tint, mask scale)
// made the first version invisible in game.
const effectsCommon = (colorTex, mask1, extra) => `"Layer0"
{
\t"shader"\t"csgo_effects.vfx"
${extra}\t"F_RENDER_BACKFACES"\t"1"
\t"F_DO_NOT_CAST_SHADOWS"\t"1"
\t"g_bFogEnabled"\t"1"
\t"g_nTextureAddressModeU"\t"0"
\t"g_nTextureAddressModeV"\t"0"
\t"g_flFadeDistance"\t"0.000"
\t"g_flFadeFalloff"\t"1.000"
\t"g_flFadeMax"\t"1.000"
\t"g_flFadeMin"\t"0.000"
\t"g_flFresnelExponent"\t"0.001"
\t"g_flFresnelFalloff"\t"1.000"
\t"g_flFresnelMax"\t"1.000"
\t"g_flFresnelMin"\t"0.000"
\t"g_vColorTint"\t"[1.000000 1.000000 1.000000 0.000000]"
\t"g_vMask1Scale"\t"[1.000 1.000]"
\t"g_vMask2Scale"\t"[1.000 1.000]"
\t"g_vMask3Scale"\t"[1.000 1.000]"
\t"g_vMask2PanSpeed"\t"[0.000 0.000]"
\t"g_vMask3PanSpeed"\t"[0.000 0.000]"
\t"g_vTexCoordScrollSpeed"\t"[0.000 0.000]"
\t"TextureColor"\t"${colorTex}"
${mask1 ? `\t"TextureMask1"\t"${mask1}"\n` : ""}}
`;
const vmat = (colorTex) => effectsCommon(colorTex, `${MAT_DIR}/curtain_streaks.png`,
  `\t"F_ADDITIVE_BLEND"\t"1"
\t"F_DEPTH_FEATHER"\t"1"
\t"g_flFeatherDistance"\t"8.000"
\t"g_flFeatherFalloff"\t"1.000"
\t"g_flColorBoost"\t"1.400"
\t"g_flOpacityScale"\t"1.000"
\t"g_vMask1PanSpeed"\t"[0.000 -0.180]"
`);

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

const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
write(`${MODEL}.dmx`, dmx);
write(`${MODEL}.vmdl`, vmdl);
write(`${MAT_DIR}/curtain_red_color.png`, curtainColor([1.0, 0.22, 0.18]));
write(`${MAT_DIR}/curtain_blue_color.png`, curtainColor([0.22, 0.55, 1.0]));
write(`${MAT_DIR}/curtain_streaks.png`, streakMask());
write(`${MAT_RED}.vmat`, vmat(`${MAT_DIR}/curtain_red_color.png`));
write(`${MAT_BLUE}.vmat`, vmat(`${MAT_DIR}/curtain_blue_color.png`));
write(`${BLOCK_MODEL}.dmx`, dmxFor("colon_blocker", bp, buv, bn, bf, BLOCK_MAT));
write(`${BLOCK_MODEL}.vmdl`, blockVmdl);
write(`${BLOCK_MAT}_color.png`, png(8, 8, (x, y, b, o) => { b[o] = 0; b[o + 1] = 0; b[o + 2] = 0; }));
write(`${BLOCK_MAT}_trans.png`, png(8, 8, (x, y, b, o) => { b[o] = 255; b[o + 1] = 255; b[o + 2] = 255; }));
write(`${BLOCK_MAT}.vmat`, blockVmat);
console.log(`kickoff curtain: ${positions.length} verts, ${faces.length} quads; colon blocker ${BLOCK_W}x${BLOCK_H} -> ${out}`);
