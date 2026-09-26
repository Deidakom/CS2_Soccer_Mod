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
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import crypto from "node:crypto";

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
const MODEL = "models/soccermod/grass_fine"; // new name: the 8 x 10 grass_shell_* tiles stay for older plugins
const MAT_GREEN = "materials/soccermod/grass_shell_green";
const MAT_WHITE = "materials/soccermod/grass_shell_white";

// ---- line markings (measured from the stadium's 85 pitch-line meshes) -----------
const rects = [
  // touchlines, goal lines, halfway line
  [1018, -1384, 1024, 1384], [-1024, -1384, -1018, 1384],
  [-1024, 1378, 1024, 1384], [-1024, -1384, 1024, -1378],
  [-1018, -3, -11, 3], [11, -3, 1018, 3],
  // penalty boxes (x ±602..608, y 832..1378) and their front line
  [602, 832, 608, 1378], [-608, 832, -602, 1378], [-608, 832, 608, 838],
  [602, -1378, 608, -832], [-608, -1378, -602, -832], [-608, -838, 608, -832],
  // goal areas (x ±314, y 1184..1378)
  [308, 1184, 314, 1378], [-314, 1184, -308, 1378], [-314, 1184, 314, 1190],
  [308, -1378, 314, -1184], [-314, -1378, -308, -1184], [-314, -1190, 314, -1184],
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

// ---- mesh ------------------------------------------------------------------------
const positions = [], uvs = [], faces = { green: [], white: [] };
const vert = (x, y, z) => { positions.push([x, y, z]); uvs.push([x / tile, -y / tile]); return positions.length - 1; };
const quad = (set, x0, y0, x1, y1, z) => {
  const a = vert(x0, y0, z), b = vert(x1, y0, z), c = vert(x1, y1, z), d = vert(x0, y1, z);
  faces[set].push([a, b, c, d]);
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
    quad("green", x0, y0, x0 + TILE_W, y0 + TILE_H, z);
  }
  const zw = z + 0.01;                              // white just above green in each layer
  // Lines are cut along the tile grid, so each piece is lit with its own tile
  // (a whole touchline in one tile took one light value along its length).
  for (const r of rects) for (let ty = 0; ty < TILES_Y; ty++) for (let tx = 0; tx < TILES_X; tx++) {
    const x0 = Math.max(r[0], -HALF_X + tx * TILE_W), x1 = Math.min(r[2], -HALF_X + (tx + 1) * TILE_W);
    const y0 = Math.max(r[1], -HALF_Y + ty * TILE_H), y1 = Math.min(r[3], -HALF_Y + (ty + 1) * TILE_H);
    if (x1 - x0 > 0.01 && y1 - y0 > 0.01) quad("white", x0, y0, x1, y1, zw);
  }
  for (const r of rings) ringFaces(...r, zw);
  for (const d of discs) discFaces(...d, zw);
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
function buildDmx(positions, uvs, faces) {
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
}
`;
}

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
\t\t\t\t\t{
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
\t\t\t\t]
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
  .replace(`\t"F_TRANSLUCENT"\t"1"\n`, `\t"F_ALPHA_TEST"\t"1"\n\t"g_flAlphaTestReference"\t"0.500"\n`);

const write = (rel, data) => { const out = path.join(addon, rel); fs.mkdirSync(path.dirname(out), { recursive: true }); fs.writeFileSync(out, data); console.log(`wrote ${rel} (${fs.statSync(out).size} B)`); };
// Split into tiles: every face goes to the tile holding its centroid; the
// vertices become local to the tile centre (the plugin spawns each tile
// there), UVs stay world-based so the texture runs on seamlessly.
let tilesWritten = 0;
for (let ty = 0; ty < TILES_Y; ty++) for (let tx = 0; tx < TILES_X; tx++) {
  const cx = -HALF_X + (tx + 0.5) * TILE_W, cy = -HALF_Y + (ty + 0.5) * TILE_H;
  const tileOf = (v, half, size, n) => Math.min(n - 1, Math.max(0, Math.floor((v + half) / size)));
  const inTile = (f) => {
    const mx = f.reduce((a, i) => a + positions[i][0], 0) / f.length, my = f.reduce((a, i) => a + positions[i][1], 0) / f.length;
    return tileOf(mx, HALF_X, TILE_W, TILES_X) === tx && tileOf(my, HALF_Y, TILE_H, TILES_Y) === ty;
  };
  const map = new Map(), P = [], U = [], out = { green: [], white: [] };
  const local = (i) => { if (!map.has(i)) { map.set(i, P.length); P.push([positions[i][0] - cx, positions[i][1] - cy, positions[i][2]]); U.push(uvs[i]); } return map.get(i); };
  for (const set of ["green", "white"]) for (const f of faces[set]) if (inTile(f)) out[set].push(f.map(local));
  const name = `${MODEL}_${tx}_${ty}`;
  write(`${name}.dmx`, buildDmx(P, U, out));
  write(`${name}.vmdl`, vmdlFor(name));
  tilesWritten++;
}
console.log(`tiles: ${tilesWritten} (${TILES_X} x ${TILES_Y}, ${TILE_W} x ${TILE_H} units)`);
write(`${MAT_GREEN}.vmat`, vmat(`${MAT_GREEN}_color.png`));
write(`${MAT_WHITE}.vmat`, vmat(`${MAT_WHITE}_color.png`));
write(`${MAT_GREEN}_cut.vmat`, vmatCut(`${MAT_GREEN}_color.png`));
write(`${MAT_WHITE}_cut.vmat`, vmatCut(`${MAT_WHITE}_color.png`));
write(`${MAT_GREEN}_color.png`, greenPng);
write(`${MAT_WHITE}_color.png`, whitePng);
write("materials/soccermod/grass_shell_trans.png", transPng);
console.log(`grass shell: ${layers} layers (${start}..${start + (layers - 1) * spacing} above the floor), ` +
  `${faces.green.length} green + ${faces.white.length} white faces, ${positions.length} vertices`);
