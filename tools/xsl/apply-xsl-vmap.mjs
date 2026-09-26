#!/usr/bin/env node
// Part G: turns soccer_soccermod_stadium.vmap into the XSL-style stadium.
// Removes our own stands - every world mesh, and every decorative brush
// entity (func_brush / func_wall / func_reflective_glass), that lies
// completely outside the pitch box - and places the four XSL-structure
// models (tools/xsl/extract-xsl-structure.mjs, world coordinates) as
// prop_static at the origin. The pitch, its walls, goals, nets, lines,
// triggers, spawns and all logic stay byte-identical.
//
//   node tools/xsl/apply-xsl-vmap.mjs --in <vmap> --out <vmap> [--dry-run 1]
import fs from "node:fs";
import crypto from "node:crypto";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => {
  if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
if (!args.in || (!args.out && !args["dry-run"])) throw new Error("usage: --in <vmap> --out <vmap> [--dry-run 1]");
const BOX = { x: 1300, y: 1700 };                 // the pitch with its walls (grass +-1280 x +-1664)
const MODELS = [0, 1, 2, 3].map((q) => `models/soccermod_xsl/xsl_q${q}.vmdl`);
const REMOVABLE_ENTITIES = new Set(["func_brush", "func_wall", "func_reflective_glass"]);

const text = fs.readFileSync(args.in, "utf8");

// ---- element tree: every "{" of the KV2 file is an element; its type is the
// quoted token right before it. Strings are skipped.
const nodes = [];
const stack = [];
let lastTokStart = -1, lastTokEnd = -1;
for (let i = 0; i < text.length; i++) {
  const c = text[i];
  if (c === '"') {
    const start = i;
    for (i++; i < text.length && text[i] !== '"'; i++) if (text[i] === "\\") i++;
    lastTokStart = start; lastTokEnd = i;
  } else if (c === "{") {
    const node = { type: text.slice(lastTokStart + 1, lastTokEnd), tokStart: lastTokStart, open: i, close: -1,
      parent: stack.length ? stack[stack.length - 1] : null };
    nodes.push(node); stack.push(node);
  } else if (c === "}") {
    stack.pop().close = i;
  }
}
const own = (node) => {            // the element's text without its child elements' texts
  let s = "", at = node.open;
  for (const child of nodes) if (child.parent === node) { s += text.slice(at, child.open); at = child.close + 1; }
  return s + text.slice(at, node.close + 1);
};
const vec = (s) => s ? s.trim().split(/\s+/).map(Number) : null;

// World-space bounding sphere of a mesh: origin + farthest vertex * scale.
// A sphere keeps rotated meshes on the safe side of the "fully outside" test.
function meshSphere(mesh) {
  const block = text.slice(mesh.open, mesh.close + 1);
  const origin = vec(block.match(/"origin"\s+"vector3"\s+"([^"]+)"/)?.[1]) ?? [0, 0, 0];
  const scales = vec(block.match(/"scales"\s+"vector3"\s+"([^"]+)"/)?.[1]) ?? [1, 1, 1];
  const stream = block.match(/"standardAttributeName"\s+"string"\s+"position"[\s\S]*?"data"\s+"vector3_array"\s*\[([\s\S]*?)\]/);
  const points = [...(stream?.[1] ?? "").matchAll(/"([^"]+)"/g)].map((m) => vec(m[1]));
  if (!points.length) return null;
  const c = [0, 1, 2].map((k) => points.reduce((a, p) => a + p[k], 0) / points.length);
  const r = Math.max(...points.map((p) => Math.hypot(...[0, 1, 2].map((k) => (p[k] - c[k]) * scales[k]))));
  return { center: [0, 1, 2].map((k) => origin[k] + c[k] * scales[k]), radius: r };
}
const outsideBox = (s) => Math.abs(s.center[0]) - s.radius > BOX.x || Math.abs(s.center[1]) - s.radius > BOX.y;
const ancestors = (n) => { const a = []; for (let p = n.parent; p; p = p.parent) a.push(p); return a; };
const material = (n) => text.slice(n.open, n.close + 1).match(/"materials"\s+"string_array"\s*\[\s*"([^"]+)"/)?.[1] ?? "";

const remove = [];
const report = { worldMeshes: 0, entities: {}, keptInEntities: 0, materials: {} };
for (const n of nodes) {
  if (n.type === "CMapMesh" && !ancestors(n).some((a) => a.type === "CMapEntity")) {
    const s = meshSphere(n);
    const mat = material(n);
    if (s && outsideBox(s) && !mat.startsWith("materials/tools/")) {
      remove.push(n); report.worldMeshes++; report.materials[mat] = (report.materials[mat] ?? 0) + 1;
    }
  } else if (n.type === "CMapEntity") {
    const cls = own(n).match(/"classname"\s+"string"\s+"([^"]+)"/)?.[1];
    if (!REMOVABLE_ENTITIES.has(cls)) continue;
    const meshes = nodes.filter((m) => m.type === "CMapMesh" && ancestors(m).includes(n));
    const spheres = meshes.map(meshSphere).filter(Boolean);
    if (spheres.length && spheres.every(outsideBox)) { remove.push(n); report.entities[cls] = (report.entities[cls] ?? 0) + 1; }
  }
}
// Drop nested removals (an entity already removed takes its meshes along).
const removed = remove.filter((n) => !ancestors(n).some((a) => remove.includes(a)));
console.log(JSON.stringify(report, null, 1));
console.log(`remove ${removed.length} elements`);
if (args["dry-run"]) process.exit(0);

// ---- rewrite: cut each element (type token .. closing brace) and one comma.
let out = text;
for (const n of [...removed].sort((a, b) => b.tokStart - a.tokStart)) {
  let start = n.tokStart, end = n.close + 1;
  const after = out.slice(end).match(/^\s*,/);
  if (after) end += after[0].length;
  else { const before = out.slice(0, start).match(/,\s*$/); if (before) start -= before[0].length; }
  out = out.slice(0, start) + out.slice(end);
}

// ---- the four XSL models: copies of the existing prop_static entity.
const tIdx = out.indexOf('"classname" "string" "prop_static"');
const tStart = out.lastIndexOf('"CMapEntity"', tIdx);
let depth = 0, tEnd = -1;
for (let k = out.indexOf("{", tStart); k < out.length; k++) { if (out[k] === "{") depth++; else if (out[k] === "}" && --depth === 0) { tEnd = k + 1; break; } }
const template = out.slice(tStart, tEnd);
let nodeId = Math.max(...[...out.matchAll(/"nodeID"\s+"int"\s+"(\d+)"/g)].map((m) => Number(m[1])));
const copies = MODELS.map((model) => template
  .replace(/"elementid" "[0-9a-f-]+"/g, () => `"elementid" "${crypto.randomUUID()}"`)
  .replace(/"nodeID" "int" "\d+"/, () => `"nodeID" "int" "${++nodeId}"`)
  .replace(/"referenceID" "uint64" "0x[0-9a-f]+"/, () => `"referenceID" "uint64" "0x${crypto.randomBytes(8).toString("hex")}"`)
  .replace(/"model" "string" "[^"]*"/, `"model" "string" "${model}"`)
  .replace(/"randomSeed" "int" "\d+"/, () => `"randomSeed" "int" "${crypto.randomInt(1, 2 ** 31 - 1)}"`));
out = out.slice(0, tEnd) + ",\n" + copies.join(",\n") + out.slice(tEnd);
fs.writeFileSync(args.out, out);
console.log(`wrote ${args.out}: ${text.length} -> ${out.length} bytes, +${copies.length} prop_static`);
