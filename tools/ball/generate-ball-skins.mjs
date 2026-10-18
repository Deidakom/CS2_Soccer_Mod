#!/usr/bin/env node
// World Cup ball skins (owner 2026-10-18): a per-player ball look, drawn as a
// shell around the match ball by the plugin (SoccerModMvpPlugin.BallSkins.cs).
//
//   models/soccermod/ball/wc_ball.vmdl - a cube-sphere (subdivided cube pushed
//   onto the sphere, no pole pinch), radius = the native ball radius x 1.01 so
//   it covers the map ball. Skin 0..4 = Trionda 2026, Al Rihla 2022, Telstar 18,
//   Jabulani 2010 and an own SoccerMod design (Brazuca = the map ball already).
//
// Texture: one 2048 x 1024 atlas per ball, 4 x 2 tiles of 512 (6 cube faces).
// Every texel is turned into its direction on the sphere and coloured by the
// ball's design function (ball-skin-designs.mjs), so panels and seams sit
// exactly on the model.
//
// usage: node tools/ball/generate-ball-skins.mjs <csgo_addons/soccermod_atmo> [--preview <png>]
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import crypto from "node:crypto";
import { DESIGNS } from "./ball-skin-designs.mjs";

const args = process.argv.slice(2);
const out = args[0];
const previewAt = args.indexOf("--preview");
const preview = previewAt > 0 ? args[previewAt + 1] : null;
if (!out) { console.error("usage: generate-ball-skins.mjs <addon content dir> [--preview <png>]"); process.exit(1); }

const BALL_R = 18.805 * 1.01;                // DefaultBallCollisionRadius (map ball) + 1 %
const MODEL = "models/soccermod/ball/wc_ball", MAT_DIR = "materials/soccermod/ball";
const TILE = 512, AW = TILE * 4, AH = TILE * 2, SUB = 24;

// ---- cube faces: tile slot, and the cube point for face coords (a, b) in [-1, 1] ----
const FACES = [
  { slot: [0, 0], p: (a, b) => [1, a, b] },    // +x
  { slot: [1, 0], p: (a, b) => [-1, -a, b] },  // -x
  { slot: [2, 0], p: (a, b) => [-a, 1, b] },   // +y
  { slot: [3, 0], p: (a, b) => [a, -1, b] },   // -y
  { slot: [0, 1], p: (a, b) => [-b, a, 1] },   // +z
  { slot: [1, 1], p: (a, b) => [b, a, -1] },   // -z
];
const norm = (v) => { const l = Math.hypot(v[0], v[1], v[2]); return [v[0] / l, v[1] / l, v[2] / l]; };
// equal-angle warp: spreads texels evenly over each face
const warp = (t) => Math.tan(t * Math.PI / 4);

// ---- PNG (as generate-ball-shadow.mjs) --------------------------------------------
function png(w, h, rgb) {
  const raw = Buffer.alloc((w * 3 + 1) * h);
  for (let y = 0; y < h; y++) { raw[y * (w * 3 + 1)] = 0; rgb.copy(raw, y * (w * 3 + 1) + 1, y * w * 3, (y + 1) * w * 3); }
  const table = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
  const crc = (b) => { let c = 0xffffffff; for (const v of b) c = table[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (type, data) => { const len = Buffer.alloc(4); len.writeUInt32BE(data.length); const td = Buffer.concat([Buffer.from(type), data]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([len, td, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 2;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", zlib.deflateSync(raw, { level: 9 })), chunk("IEND", Buffer.alloc(0))]);
}
const c255 = (v) => Math.max(0, Math.min(255, Math.round(v * 255)));

// 2x2 supersampled atlas texel -> design colour
function atlas(design) {
  const buf = Buffer.alloc(AW * AH * 3);
  for (const f of FACES) for (let y = 0; y < TILE; y++) for (let x = 0; x < TILE; x++) {
    let r = 0, g = 0, b = 0;
    for (const [sx, sy] of [[0.25, 0.25], [0.75, 0.25], [0.25, 0.75], [0.75, 0.75]]) {
      const c = design.color(norm(f.p(warp((x + sx) / TILE * 2 - 1), warp(1 - (y + sy) / TILE * 2))));
      r += c[0]; g += c[1]; b += c[2];
    }
    const o = ((f.slot[1] * TILE + y) * AW + f.slot[0] * TILE + x) * 3;
    buf[o] = c255(r / 4); buf[o + 1] = c255(g / 4); buf[o + 2] = c255(b / 4);
  }
  return buf;
}

// ---- mesh: per face (SUB+1)^2 vertices, UVs half a texel inside the tile ----------
const positions = [], normals = [], uvs = [], faces = [];
for (const f of FACES) {
  const base = positions.length;
  for (let j = 0; j <= SUB; j++) for (let i = 0; i <= SUB; i++) {
    const s = i / SUB, t = j / SUB, n = norm(f.p(warp(s * 2 - 1), warp(1 - t * 2)));
    positions.push(n.map((v) => v * BALL_R)); normals.push(n);
    const inset = (v) => (0.5 + v * (TILE - 1)) / TILE;
    uvs.push([(f.slot[0] + inset(s)) * TILE / AW, (f.slot[1] + inset(t)) * TILE / AH]);
  }
  for (let j = 0; j < SUB; j++) for (let i = 0; i < SUB; i++) {
    const a = base + j * (SUB + 1) + i;
    faces.push([a, a + SUB + 1, a + SUB + 2, a + 1]);
  }
}

const id = () => crypto.randomUUID();
const q = (vals, ind) => vals.map((v) => `${ind}"${v}"`).join(",\n");
const f4 = (n) => Number(n.toFixed(4)).toString();
function dmx(name, mtl) {
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
\t\t"source" "string" "tools/ball/generate-ball-skins.mjs"
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
\t\t\t\t"name" "string" "ball"
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
${q(uvs.map((p) => p.map((v) => v.toFixed(6)).join(" ")), "\t\t")}
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

const vmat = (tex) => `"Layer0"
{
	"shader"	"csgo_complex.vfx"
	"g_flMetalness"	"0.000"
	"g_vColorTint"	"[1.000000 1.000000 1.000000 0.000000]"
	"TextureColor"	"${tex}"
	"TextureRoughness"	"[0.550000 0.550000 0.550000 0.000000]"
}
`;
const vmdl = (mats) => `<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc28:version{fb63b6ca-f435-4aa0-a2c7-c66ddc651dca} -->
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
${mats.slice(1).map((m, i) => `\t\t\t\t\t{
\t\t\t\t\t\t_class = "MaterialGroup"
\t\t\t\t\t\tname = "${DESIGNS[i + 1].key}"
\t\t\t\t\t\tremaps =
\t\t\t\t\t\t[
\t\t\t\t\t\t\t{
\t\t\t\t\t\t\t\tfrom = "${mats[0]}.vmat"
\t\t\t\t\t\t\t\tto = "${m}.vmat"
\t\t\t\t\t\t\t},
\t\t\t\t\t\t]
\t\t\t\t\t},
`).join("")}\t\t\t\t]
\t\t\t},
\t\t\t{
\t\t\t\t_class = "RenderMeshList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "RenderMeshFile"
\t\t\t\t\t\tname = "wc_ball"
\t\t\t\t\t\tfilename = "${MODEL}.dmx"
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t]
\t}
}
`;

// ---- preview: lit orthographic views of the textured sphere (atlas sampled) -------
function sampler(buf) {
  return (d) => {
    const ax = Math.abs(d[0]), ay = Math.abs(d[1]), az = Math.abs(d[2]);
    let f, a, b;
    if (ax >= ay && ax >= az) { f = d[0] > 0 ? 0 : 1; a = d[1] / ax * (d[0] > 0 ? 1 : -1); b = d[2] / ax; }
    else if (ay >= az) { f = d[1] > 0 ? 2 : 3; a = d[0] / ay * (d[1] > 0 ? -1 : 1); b = d[2] / ay; }
    else { f = d[2] > 0 ? 4 : 5; a = d[1] / az; b = d[2] > 0 ? -d[0] / az : d[0] / az; }
    const s = (Math.atan(a) * 4 / Math.PI + 1) / 2, t = (1 - Math.atan(b) * 4 / Math.PI) / 2;
    const x = Math.min(TILE - 1, Math.floor(s * TILE)), y = Math.min(TILE - 1, Math.floor(t * TILE));
    const o = ((FACES[f].slot[1] * TILE + y) * AW + FACES[f].slot[0] * TILE + x) * 3;
    return [buf[o] / 255, buf[o + 1] / 255, buf[o + 2] / 255];
  };
}
function previewImage(atlases) {
  const V = 300, views = [[0, 0], [90, 0], [180, 0], [0, 70]];
  const W = V * views.length, H = V * atlases.length, img = Buffer.alloc(W * H * 3, 235);
  const light = norm([0.5, -0.6, 0.65]);
  atlases.forEach((buf, row) => {
    const sample = sampler(buf);
    views.forEach(([yaw, pitch], col) => {
      const cy = Math.cos(yaw * Math.PI / 180), sy = Math.sin(yaw * Math.PI / 180);
      const cp = Math.cos(pitch * Math.PI / 180), sp = Math.sin(pitch * Math.PI / 180);
      for (let y = 0; y < V; y++) for (let x = 0; x < V; x++) {
        const u = (x + 0.5) / V * 2.2 - 1.1, v = 1.1 - (y + 0.5) / V * 2.2, r2 = u * u + v * v;
        if (r2 > 1) continue;
        const z = Math.sqrt(1 - r2);                          // view space: x right, y up, z to camera
        const vs = [z, u, v];                                 // world: camera looks along -x
        const p = [vs[0] * cp - vs[2] * sp, vs[1], vs[0] * sp + vs[2] * cp];
        const w = [p[0] * cy - p[1] * sy, p[0] * sy + p[1] * cy, p[2]];
        const c = sample(w), lam = 0.45 + 0.55 * Math.max(0, z * light[2] + u * light[0] + v * light[1]);
        const o = ((row * V + y) * W + col * V + x) * 3;
        img[o] = c255(c[0] * lam); img[o + 1] = c255(c[1] * lam); img[o + 2] = c255(c[2] * lam);
      }
    });
  });
  return png(W, H, img);
}

const write = (rel, data) => { const f = path.join(out, rel); fs.mkdirSync(path.dirname(f), { recursive: true }); fs.writeFileSync(f, data); };
const atlases = [], mats = [];
for (const d of DESIGNS) {
  const buf = atlas(d), mat = `${MAT_DIR}/wc_${d.key}`;
  atlases.push(buf); mats.push(mat);
  write(`${mat}_color.png`, png(AW, AH, buf));
  write(`${mat}.vmat`, vmat(`${mat}_color.png`));
}
write(`${MODEL}.dmx`, dmx("wc_ball", mats[0]));
write(`${MODEL}.vmdl`, vmdl(mats));
if (preview) fs.writeFileSync(preview, previewImage(atlases));
console.log(`wrote ${MODEL}.vmdl (${positions.length} verts, ${faces.length} quads), ${DESIGNS.length} skins: ${DESIGNS.map((d) => d.key).join(", ")}`);
