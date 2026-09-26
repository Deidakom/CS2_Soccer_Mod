#!/usr/bin/env node
// Part G (owner, 2026-09-26): rebuild the XSL stadium's structure (stands,
// roof, trusses, outer walls) around our own pitch. Reads the XSL BSP
// (Workshop 1476746113, read-only, never shipped) and writes CS2 models made
// of its geometry, textured with OUR materials (generate-xsl-textures.mjs) or
// materials CS2 itself ships. Our pitch, walls, goals and lines stay as they
// are: everything inside the pitch box is left out.
//
//   node tools/xsl/extract-xsl-structure.mjs --bsp <soccer_xsl_stadium.bsp>
//        [--addon <content addon dir>] [--report 1]
// Writes models/soccermod_xsl/xsl_q<0..3>.vmdl + .dmx (4 quadrants, render + physics mesh).
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => {
  if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
const bspPath = args.bsp;
if (!bspPath) throw new Error("--bsp <soccer_xsl_stadium.bsp> is required");
const addon = args.addon ??
  "E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/content/csgo_addons/cs2sm_stadium_v1";

// XSL pitch centre (-45, 13), grass top z -901  ->  ours (0, 0), floor z -32.
const SHIFT = [45, -13, 869];
// Everything below this height inside the pitch box is ours and stays.
const PITCH_BOX = { x: 1300, y: 1700, zTop: -32 + 150 };

// XSL material -> our material. Own = generated (tools/xsl), cs2 = shipped with CS2.
const OWN = (n) => `materials/soccermod_xsl/${n}.vmat`;
const MATERIALS = {
  "SOCCER/METAL_GREY": OWN("metal_grey"),
  "SOCCER/METAL_RED": OWN("metal_red"),
  "SOCCER/METAL_BLUE": OWN("metal_blue"),
  "SOCCER/METAL_GREYGREEN": OWN("metal_greygreen"),
  "SOCCER/BLACKMETAL": OWN("blackmetal"),
  "SOCCER/GREY_WALL": OWN("grey_wall"),
  "SOCCER/SCH": OWN("sch"),
  "SOCCER/VITRE_SALE": OWN("vitre_sale"),
  "SOCCER/CEILING": OWN("ceiling"),
  "SOCCER/GLASS_CLEAR": OWN("glass_clear"),
  "SOCCER/METAL_RAILING": OWN("metal_railing"),
  "SOCCER/GRASSPXG": "materials/tm/grass20.vmat",
  "SOCCER/PITCH_LINE": "materials/soccer/pitch_line.vmat",
  "CONCRETE/BAGGAGE_CONCRETEFLOORA": "materials/ar_baggage/baggage_concrete_floor_01.vmat",
  "DE_DUST/GROUNDSAND03A": "materials/de_dust/groundsand03a.vmat",
  "METAL/METALFENCE004A": "materials/metal/metalfence004a.vmat",
  "CS_HAVANA/METALFENCE007A": "materials/cs_havana/metalfence007a.vmat",
  "DE_TRAIN/TRAIN_METALCEILING_02": "materials/de_train/train_metalceiling_02.vmat",
  "CS_ASSAULT/ASSAULT_TRAINSTATION_TRUSS_01C": OWN("truss"),
  "CS_ASSAULT/ASSAULT_TRAINSTATION_TRUSS_01A": OWN("truss"),
  "METAL/CITADEL_TILEFLOOR016A": OWN("tilefloor"),
  "CARPET/GREY02": OWN("sch"),
  "CS_ASSAULT/ASSAULT_DOOR1": OWN("grey_wall"),
  "GLASS/OFFWNDWB": OWN("glass_clear"),
  "METAL/METALHULL010B": OWN("blackmetal"),
};
const SKIP = /^(TOOLS\/|SOCCER\/SKY|SOCCER\/(XSL|LOLOBUBU|MARCO|ARTURO|BACK|PITCH_LINE|GRASSPXG)$)/i; // tools, skies, logos/ads, the pitch (ours stays)

// ---- BSP (Source 1, VBSP v21) ----------------------------------------------------------
const b = fs.readFileSync(bspPath);
if (b.toString("latin1", 0, 4) !== "VBSP") throw new Error("not a VBSP file");
const lump = (i) => ({ o: b.readInt32LE(8 + i * 16), n: b.readInt32LE(12 + i * 16) });
const L = { vert: lump(3), texinfo: lump(6), texdata: lump(2), face: lump(7), edge: lump(12), surfedge: lump(13), model: lump(14), ent: lump(0), sdata: lump(43), stable: lump(44) };
const vert = (i) => [0, 1, 2].map((k) => b.readFloatLE(L.vert.o + i * 12 + k * 4));
const names = []; for (let i = 0; i < L.stable.n / 4; i++) { const off = b.readInt32LE(L.stable.o + i * 4); names.push(b.toString("latin1", L.sdata.o + off, b.indexOf(0, L.sdata.o + off))); }
const texdata = []; for (let i = 0; i < L.texdata.n / 32; i++) { const o = L.texdata.o + i * 32; texdata.push({ name: names[b.readInt32LE(o + 12)], w: b.readInt32LE(o + 16), h: b.readInt32LE(o + 20) }); }
const texinfo = []; for (let i = 0; i < L.texinfo.n / 72; i++) {
  const o = L.texinfo.o + i * 72, f = (k) => b.readFloatLE(o + k * 4);
  texinfo.push({ s: [f(0), f(1), f(2), f(3)], t: [f(4), f(5), f(6), f(7)], flags: b.readInt32LE(o + 64), td: b.readInt32LE(o + 68) });
}
// World (model 0) plus the visible brush entities (func_brush / func_wall / func_door...).
const entText = b.toString("latin1", L.ent.o, L.ent.o + L.ent.n);
// Brush entity geometry is stored relative to the entity origin.
const brushModels = new Map([[0, [0, 0, 0]]]);
for (const m of entText.matchAll(/\{([^{}]*)\}/g)) {
  const kv = Object.fromEntries([...m[1].matchAll(/"([^"]+)"\s+"([^"]*)"/g)].map((x) => [x[1], x[2]]));
  if (/^\*\d+$/.test(kv.model ?? "") && /^func_(brush|wall|door|illusionary)/.test(kv.classname ?? "") && kv.rendermode !== "10") brushModels.set(Number(kv.model.slice(1)), (kv.origin ?? "0 0 0").split(/s+/).map(Number));
}

const quadrants = [0, 1, 2, 3].map(() => ({ positions: [], uvs: [], normals: [], sets: new Map() }));
const stats = { faces: 0, skippedTool: 0, skippedPitch: 0, unmapped: new Map(), perMaterial: new Map() };
for (const [m, origin] of brushModels) {
  const mo = L.model.o + m * 48, first = b.readInt32LE(mo + 40), num = b.readInt32LE(mo + 44);
  for (let fi = first; fi < first + num; fi++) {
    const fo = L.face.o + fi * 56;
    const firstEdge = b.readInt32LE(fo + 4), numEdges = b.readInt16LE(fo + 8), tix = b.readInt16LE(fo + 10), disp = b.readInt16LE(fo + 12);
    if (tix < 0 || disp >= 0 || numEdges < 3) continue;
    const ti = texinfo[tix], td = texdata[ti.td], mat = td.name.toUpperCase();
    if (SKIP.test(mat) || (ti.flags & 0x0004)) { stats.skippedTool++; continue; }           // SURF_SKY
    const poly = [];
    for (let e = 0; e < numEdges; e++) {
      const se = b.readInt32LE(L.surfedge.o + (firstEdge + e) * 4), eo = L.edge.o + Math.abs(se) * 4;
      poly.push(vert(se >= 0 ? b.readUInt16LE(eo) : b.readUInt16LE(eo + 2)));
    }
    const world = poly.map((p) => [p[0] + origin[0] + SHIFT[0], p[1] + origin[1] + SHIFT[1], p[2] + origin[2] + SHIFT[2]]);
    const inPitchBox = world.every((p) => Math.abs(p[0]) <= PITCH_BOX.x && Math.abs(p[1]) <= PITCH_BOX.y && p[2] <= PITCH_BOX.zTop);
    if (inPitchBox) { stats.skippedPitch++; continue; }
    const target = MATERIALS[mat];
    if (!target) { stats.unmapped.set(mat, (stats.unmapped.get(mat) ?? 0) + 1); continue; }
    // UVs from the face's texture axes, normalised by the original texture size.
    const uv = poly.map((p) => [(p[0] * ti.s[0] + p[1] * ti.s[1] + p[2] * ti.s[2] + ti.s[3]) / td.w,
                                 (p[0] * ti.t[0] + p[1] * ti.t[1] + p[2] * ti.t[2] + ti.t[3]) / td.h]);
    const a = world[0], c1 = world[1], c2 = world[2];
    const u = [c1[0] - a[0], c1[1] - a[1], c1[2] - a[2]], v = [c2[0] - a[0], c2[1] - a[1], c2[2] - a[2]];
    let n = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]];
    const len = Math.hypot(...n) || 1; n = n.map((x) => x / len);
    const cx = world.reduce((s, p) => s + p[0], 0) / world.length, cy = world.reduce((s, p) => s + p[1], 0) / world.length;
    const q = quadrants[(cx >= 0 ? 1 : 0) + (cy >= 0 ? 2 : 0)];
    const idx = world.map((p, k) => { q.positions.push(p); q.uvs.push(uv[k]); q.normals.push(n); return q.positions.length - 1; });
    if (!q.sets.has(target)) q.sets.set(target, []);
    // BSP winding is clockwise seen from the front; DMX wants counter-clockwise.
    q.sets.get(target).push(idx.reverse());
    stats.faces++;
    stats.perMaterial.set(target, (stats.perMaterial.get(target) ?? 0) + 1);
  }
}

// ---- DMX + vmdl -----------------------------------------------------------------------------
const id = () => crypto.randomUUID();
const list = (vals, ind) => vals.map((x) => `${ind}"${x}"`).join(",\n");
const f4 = (x) => Number(x.toFixed(4)).toString();
function dmx(q) {
  const I = { model: id(), dag: id(), bind: id() };
  const idx = list(q.positions.map((_, i) => i), "\t\t");
  const sets = [...q.sets].map(([mat, faces]) => `\t\t\t"DmeFaceSet"
\t\t\t{
\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t"name" "string" "${path.basename(mat, ".vmat")}"
\t\t\t\t"faces" "int_array"
\t\t\t\t[
${list(faces.flatMap((f) => [...f, -1]), "\t\t\t\t\t")}
\t\t\t\t]
\t\t\t\t"material" "DmeMaterial"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t\t"name" "string" "material"
\t\t\t\t\t"mtlName" "string" "${mat}"
\t\t\t\t}
\t\t\t}`).join(",\n");
  const transform = () => `"DmeTransform"\n\t{\n\t\t"id" "elementid" "${id()}"\n\t\t"position" "vector3" "0 0 0"\n\t\t"orientation" "quaternion" "0 0 0 1"\n\t}`;
  return `<!-- dmx encoding keyvalues2 4 format model 22 -->
"DmElement"
{
\t"id" "elementid" "${id()}"
\t"name" "string" "root"
\t"skeleton" "element" "${I.model}"
\t"model" "element" "${I.model}"
}

"DmeModel"
{
\t"id" "elementid" "${I.model}"
\t"name" "string" "xsl"
\t"transform" ${transform()}
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
\t"name" "string" "xsl"
\t"transform" ${transform()}
\t"shape" "DmeMesh"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "xsl"
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
${sets}
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
\t\t"normal$0"
\t]
\t"jointCount" "int" "0"
\t"flipVCoordinates" "bool" "0"
\t"position$0" "vector3_array"
\t[
${list(q.positions.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"position$0Indices" "int_array"
\t[
${idx}
\t]
\t"texcoord$0" "vector2_array"
\t[
${list(q.uvs.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"texcoord$0Indices" "int_array"
\t[
${idx}
\t]
\t"normal$0" "vector3_array"
\t[
${list(q.normals.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"normal$0Indices" "int_array"
\t[
${idx}
\t]
}
`;
}
const vmdl = (name) => `<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc28:version{fb63b6ca-f435-4aa0-a2c7-c66ddc651dca} -->
{
\trootNode =
\t{
\t\t_class = "RootNode"
\t\tchildren =
\t\t[
\t\t\t{
\t\t\t\t_class = "RenderMeshList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "RenderMeshFile"
\t\t\t\t\t\tname = "xsl"
\t\t\t\t\t\tfilename = "models/soccermod_xsl/${name}.dmx"
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t\t{
\t\t\t\t_class = "PhysicsShapeList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "PhysicsMeshFile"
\t\t\t\t\t\tname = "xsl_collision"
\t\t\t\t\t\tfilename = "models/soccermod_xsl/${name}.dmx"
\t\t\t\t\t\tsurface_prop = "metal"
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t]
\t}
}
`;

console.log(`faces kept ${stats.faces}, tool/sky/logo skipped ${stats.skippedTool}, inside our pitch box ${stats.skippedPitch}`);
if (stats.unmapped.size) console.log("UNMAPPED (left out):", Object.fromEntries(stats.unmapped));
console.log("per material:", Object.fromEntries(stats.perMaterial));
if (args.report) process.exit(0);
const outDir = path.join(addon, "models/soccermod_xsl");
fs.mkdirSync(outDir, { recursive: true });
quadrants.forEach((q, i) => {
  const name = `xsl_q${i}`;
  fs.writeFileSync(path.join(outDir, `${name}.dmx`), dmx(q));
  fs.writeFileSync(path.join(outDir, `${name}.vmdl`), vmdl(name));
  console.log(`wrote models/soccermod_xsl/${name}: ${q.positions.length} vertices, ${[...q.sets.values()].reduce((s, f) => s + f.length, 0)} faces`);
});
