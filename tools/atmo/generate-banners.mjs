#!/usr/bin/env node
// "Arena Vision" (owner 2026-09-29: "let the banners wave a little, as if the
// wind moves them"). The v8 roof banners are two func_brush entities (one per
// end, map models unnamed_2_49167_250 / _263 at (0.5, +-1956, 790.5)): four
// banners each at x = +-546 / +-1100, 64 wide, 180 high (z 700.5..880.5), 4
// thick, swallow-tail cut up to 68 above the bottom, map materials
// materials/xgoal/red2.vmat (+y) and blue2.vmat (-y). The plugin hides the
// brushes on v8 and spawns this model eight times: the same banner, hanging
// from its top edge, with a bone chain down its length and three looping wind
// animations (so neighbours never move in step). Skin 1 = blue.
//
// usage: node tools/atmo/generate-banners.mjs <addon content dir>
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";

const out = process.argv[2];
if (!out) { console.error("usage: generate-banners.mjs <addon content dir>"); process.exit(1); }
const MODEL = "models/soccermod/atmo/banner_wave";
// Own fabric materials in the map banners' tone (tools/atmo/generate-banner-fabric.mjs).
const RED = "materials/soccermod/atmo/banner_red.vmat", BLUE = "materials/soccermod/atmo/banner_blue.vmat";
const W = 64, H = 180, NOTCH = 68, ROWS = 18, FPS = 30;
const BONES = 6; // below the root; bone k sits at depth H * k / BONES

// ---- mesh: two-sided strip in the local XZ plane (normal +y), origin at the top centre ----
const positions = [], normals = [], uvs = [], weights = [], indices = [], faces = [];
const boneOf = (depth) => {
  const f = Math.min(depth / H * BONES, BONES), k = Math.floor(f), t = f - k;
  return k >= BONES ? [[BONES, 1]] : [[k, 1 - t], [k + 1, t]];
};
function vertex(x, z, ny) {
  const depth = -z, w = boneOf(depth);
  positions.push([x, 0, z]); normals.push([0, ny, 0]); uvs.push([(x + W / 2) / W, depth / H]);
  weights.push([w[0][1], w[1] ? w[1][1] : 0]); indices.push([w[0][0], w[1] ? w[1][0] : 0]);
  return positions.length - 1;
}
// Bottom edge of the swallow tail: at the sides z = -H, in the middle -H + NOTCH.
const bottomAt = (x) => -H + NOTCH * (1 - Math.abs(x) / (W / 2));
for (const ny of [1, -1]) {
  const cols = [-W / 2, 0, W / 2];
  const grid = cols.map((x) => { const col = []; for (let r = 0; r <= ROWS; r++) col.push(vertex(x, bottomAt(x) * r / ROWS, ny)); return col; });
  for (let c = 0; c < 2; c++) for (let r = 0; r < ROWS; r++) {
    const a = grid[c][r], b = grid[c + 1][r], d = grid[c][r + 1], e = grid[c + 1][r + 1];
    faces.push(ny > 0 ? [a, d, e, b] : [a, b, e, d]);
  }
}

// ---- bones and wind ----------------------------------------------------------------------------
const bones = [{ name: "root", pos: [0, 0, 0] }];
for (let k = 1; k <= BONES; k++) bones.push({ name: `b${k}`, pos: [0, 0, -H * k / BONES] });
const winds = [
  { name: "wind_a", seconds: 4.0, amp: 22, freq: 1, lag: 0.55, side: 5 },
  { name: "wind_b", seconds: 5.0, amp: 18, freq: 1, lag: 0.7, side: -6 },
  { name: "wind_c", seconds: 3.6, amp: 25, freq: 1, lag: 0.45, side: 4 },
];
function windFrames(w) {
  const n = Math.round(w.seconds * FPS), frames = [];
  for (let f = 0; f <= n; f++) {
    const t = f / n * 2 * Math.PI * w.freq, m = new Map();
    for (let k = 1; k <= BONES; k++) {
      const s = k / BONES, reach = s ** 1.6;
      // swing out from the stand (+y towards the pitch side of the plane), gusting, with a travelling ripple
      const dy = w.amp * reach * (0.55 + 0.45 * Math.sin(t - k * w.lag)) + 3 * s * Math.sin(3 * t - k * 1.1);
      const dx = w.side * reach * Math.sin(t * 2 - k * 0.8);
      const lift = -(dy * dy) / (2 * H * s + 1) * 0.35; // swinging out raises the lower edge a little
      m.set(k, [dx, dy, lift]);
    }
    frames.push(m);
  }
  return frames;
}

// ---- DMX writers (same layout as tools/net/generate-dynamic-net.mjs) ----------------------------
const id = () => crypto.randomUUID();
const f4 = (n) => Number(n.toFixed(4)).toString();
const list = (vals, ind) => vals.map((v) => `${ind}"${v}"`).join(",\n");
const xf = (xid, pos) => `"DmeTransform"\n{\n\t"id" "elementid" "${xid}"\n\t"name" "string" "transform"\n\t"position" "vector3" "${pos.map(f4).join(" ")}"\n\t"orientation" "quaternion" "0 0 0 1"\n}`;
const indent = (s, t) => s.split("\n").map((l) => t + l).join("\n");
function skeleton(withDag) {
  const ids = bones.map(() => ({ joint: id(), xf: id(), base: id() }));
  const dag = withDag ? { id: id(), xf: id(), base: id() } : null;
  const jointElems = bones.map((bn, b) => `"DmeJoint"\n{\n\t"id" "elementid" "${ids[b].joint}"\n\t"name" "string" "${bn.name}"\n${indent(`"transform" ${xf(ids[b].xf, bn.pos)}`, "\t")}\n\t"shape" "element" ""\n\t"visible" "bool" "1"\n\t"children" "element_array"\n\t[\n${b === 0 ? bones.slice(1).map((_, k) => `\t\t"element" "${ids[k + 1].joint}"`).join(",\n") : ""}\n\t]\n}`);
  const jointList = [...ids.map((x) => x.joint), ...(dag ? [dag.id] : [])];
  const baseXfs = [...bones.map((bn, b) => xf(ids[b].base, bn.pos)), ...(dag ? [xf(dag.base, [0, 0, 0])] : [])];
  const model = (modelId, name) => `"DmeModel"\n{\n\t"id" "elementid" "${modelId}"\n\t"name" "string" "${name}"\n${indent(`"transform" ${xf(id(), [0, 0, 0])}`, "\t")}\n\t"shape" "element" ""\n\t"visible" "bool" "1"\n\t"children" "element_array"\n\t[\n\t\t"element" "${ids[0].joint}"${dag ? `,\n\t\t"element" "${dag.id}"` : ""}\n\t]\n\t"jointList" "element_array"\n\t[\n${jointList.map((j) => `\t\t"element" "${j}"`).join(",\n")}\n\t]\n\t"baseStates" "element_array"\n\t[\n\t\t"DmeTransformsList"\n\t\t{\n\t\t\t"id" "elementid" "${id()}"\n\t\t\t"name" "string" "bind"\n\t\t\t"transforms" "element_array"\n\t\t\t[\n${baseXfs.map((x) => indent(x, "\t\t\t\t")).join(",\n")}\n\t\t\t]\n\t\t}\n\t]\n\t"axisSystem" "DmeAxisSystem"\n\t{\n\t\t"id" "elementid" "${id()}"\n\t\t"upAxis" "int" "3"\n\t\t"forwardParity" "int" "1"\n\t\t"coordSys" "int" "0"\n\t}\n}`;
  return { ids, dag, jointElems, model };
}
const header = (key, value) => `<!-- dmx encoding keyvalues2 4 format model 22 -->
"DmElement"
{
\t"id" "elementid" "${id()}"
\t"name" "string" "root"
\t"skeleton" "element" "${value}"
\t"${key}" "element" "${key === "model" ? value : "__LIST__"}"
\t"exportTags" "DmeExportTags"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "exportTags"
\t\t"source" "string" "tools/atmo/generate-banners.mjs"
\t}
}
`;
function meshDmx() {
  const sk = skeleton(true), modelId = id(), vd = id(), seq = positions.map((_, i) => i);
  return `${header("model", modelId)}
${sk.model(modelId, "banner_wave")}

${sk.jointElems.join("\n\n")}

"DmeDag"
{
\t"id" "elementid" "${sk.dag.id}"
\t"name" "string" "banner_wave"
${indent(`"transform" ${xf(sk.dag.xf, [0, 0, 0])}`, "\t")}
\t"shape" "DmeMesh"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "banner_wave"
\t\t"bindState" "element" "${vd}"
\t\t"currentState" "element" "${vd}"
\t\t"baseStates" "element_array"
\t\t[
\t\t\t"element" "${vd}"
\t\t]
\t\t"deltaStates" "element_array"
\t\t[
\t\t]
\t\t"faceSets" "element_array"
\t\t[
\t\t\t"DmeFaceSet"
\t\t\t{
\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t"name" "string" "banner"
\t\t\t\t"faces" "int_array"
\t\t\t\t[
${list(faces.flatMap((f) => [...f, -1]), "\t\t\t\t\t")}
\t\t\t\t]
\t\t\t\t"material" "DmeMaterial"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t\t"name" "string" "material"
\t\t\t\t\t"mtlName" "string" "${RED}"
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
\t"id" "elementid" "${vd}"
\t"name" "string" "bind"
\t"vertexFormat" "string_array"
\t[
\t\t"position$0",
\t\t"texcoord$0",
\t\t"normal$0",
\t\t"tangent$0",
\t\t"blendweights$0",
\t\t"blendindices$0"
\t]
\t"jointCount" "int" "2"
\t"flipVCoordinates" "bool" "0"
\t"position$0" "vector3_array"
\t[
${list(positions.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"position$0Indices" "int_array"
\t[
${list(seq, "\t\t")}
\t]
\t"texcoord$0" "vector2_array"
\t[
${list(uvs.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"texcoord$0Indices" "int_array"
\t[
${list(seq, "\t\t")}
\t]
\t"normal$0" "vector3_array"
\t[
${list(normals.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"normal$0Indices" "int_array"
\t[
${list(seq, "\t\t")}
\t]
\t"tangent$0" "vector4_array"
\t[
${list(positions.map(() => "1 0 0 1"), "\t\t")}
\t]
\t"tangent$0Indices" "int_array"
\t[
${list(seq, "\t\t")}
\t]
\t"blendweights$0" "float_array"
\t[
${list(weights.flatMap((w) => w.map(f4)), "\t\t")}
\t]
\t"blendindices$0" "int_array"
\t[
${list(indices.flat(), "\t\t")}
\t]
}
`;
}
function animDmx(name, frames) {
  const sk = skeleton(false), modelId = id(), listId = id(), clipId = id();
  const times = frames.map((_, f) => f4(f / FPS));
  const channels = bones.map((bn, b) => {
    const pos = b === 0 ? "const" : frames.map((m) => { const d = m.get(b) || [0, 0, 0]; return bn.pos.map((v, i) => f4(v + d[i])).join(" "); });
    const xid = sk.ids[b].xf;
    const chan = (suffix, attr, logType, valType, values, def) => `\t\t"DmeChannel"
\t\t{
\t\t\t"id" "elementid" "${id()}"
\t\t\t"name" "string" "${bn.name}_${suffix}"
\t\t\t"fromElement" "element" "${xid}"
\t\t\t"fromAttribute" "string" "${attr}"
\t\t\t"fromIndex" "int" "0"
\t\t\t"toElement" "element" "${xid}"
\t\t\t"toAttribute" "string" "${attr}"
\t\t\t"toIndex" "int" "0"
\t\t\t"mode" "int" "3"
\t\t\t"log" "${logType}"
\t\t\t{
\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t"name" "string" "${valType} log"
\t\t\t\t"layers" "element_array"
\t\t\t\t[
\t\t\t\t\t"${logType}Layer"
\t\t\t\t\t{
\t\t\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t\t\t"name" "string" "${valType} log"
\t\t\t\t\t\t"times" "time_array"
\t\t\t\t\t\t[
${list(values === "const" ? [times[0]] : times, "\t\t\t\t\t\t\t")}
\t\t\t\t\t\t]
\t\t\t\t\t\t"curvetypes" "int_array"
\t\t\t\t\t\t[
\t\t\t\t\t\t]
\t\t\t\t\t\t"values" "${valType}_array"
\t\t\t\t\t\t[
${list(values === "const" ? [def] : values, "\t\t\t\t\t\t\t")}
\t\t\t\t\t\t]
\t\t\t\t\t}
\t\t\t\t]
\t\t\t\t"curveinfo" "element" ""
\t\t\t\t"usedefaultvalue" "bool" "0"
\t\t\t\t"defaultvalue" "${valType}" "${def}"
\t\t\t}
\t\t}`;
    return [chan("p", "position", "DmeVector3Log", "vector3", pos, bn.pos.map(f4).join(" ")), chan("o", "orientation", "DmeQuaternionLog", "quaternion", "const", "0 0 0 1")].join(",\n");
  });
  return `${header("animationList", modelId).replace("__LIST__", listId)}
${sk.model(modelId, "banner_wave")}

${sk.jointElems.join("\n\n")}

"DmeAnimationList"
{
\t"id" "elementid" "${listId}"
\t"name" "string" "animationList"
\t"animations" "element_array"
\t[
\t\t"element" "${clipId}"
\t]
}

"DmeChannelsClip"
{
\t"id" "elementid" "${clipId}"
\t"name" "string" "${name}"
\t"timeFrame" "DmeTimeFrame"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "timeFrame"
\t\t"start" "time" "0"
\t\t"duration" "time" "${f4((frames.length - 1) / FPS)}"
\t\t"offset" "time" "0"
\t\t"scale" "float" "1"
\t}
\t"color" "color" "0 0 0 0"
\t"text" "string" ""
\t"mute" "bool" "0"
\t"trackGroups" "element_array"
\t[
\t]
\t"displayScale" "float" "1"
\t"channels" "element_array"
\t[
${channels.join(",\n")}
\t]
\t"frameRate" "int" "${FPS}"
}
`;
}

const animNode = (name) => `\t\t\t\t\t{
\t\t\t\t\t\t_class = "AnimFile"
\t\t\t\t\t\tname = "${name}"
\t\t\t\t\t\tsource_filename = "${MODEL}_anims/${name}.dmx"
\t\t\t\t\t\tfade_in_time = 0.0
\t\t\t\t\t\tfade_out_time = 0.0
\t\t\t\t\t\tlooping = true
\t\t\t\t\t\tdelta = false
\t\t\t\t\t\tworldSpace = false
\t\t\t\t\t\thidden = false
\t\t\t\t\t},`;
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
\t\t\t\t\t\t\t\tfrom = "${RED}"
\t\t\t\t\t\t\t\tto = "${BLUE}"
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
\t\t\t\t\t\tname = "banner_wave"
\t\t\t\t\t\tfilename = "${MODEL}.dmx"
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t\t{
\t\t\t\t_class = "AnimationList"
\t\t\t\tchildren =
\t\t\t\t[
${winds.map((w) => animNode(w.name)).join("\n")}
\t\t\t\t]
\t\t\t},
\t\t]
\t}
}
`;

const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
write(`${MODEL}.dmx`, meshDmx());
for (const w of winds) write(`${MODEL}_anims/${w.name}.dmx`, animDmx(w.name, windFrames(w)));
write(`${MODEL}.vmdl`, vmdl);
console.log(`${MODEL}: ${positions.length} vertices, ${faces.length} quads, ${bones.length} bones, ${winds.map((w) => w.name).join(" ")}`);
